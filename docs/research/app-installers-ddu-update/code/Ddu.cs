using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Hateeze.Net;
using Hateeze.Security;

namespace Hateeze.Ddu;

public enum GpuVendor { Nvidia, Amd, Intel }

public sealed record DduOptions(
    GpuVendor Vendor,
    bool RemovePhysX = true,          // -RemovePhysx  (PhysX ставится заново вместе с драйвером NVIDIA)
    bool RemoveNvidiaApp = false,     // -RemoveGFE    (в DDU 18.1.6.x этот флаг удаляет и GeForce Experience, и NVIDIA App: "_display.nvapp")
    bool RemoveNvBroadcast = false,   // -RemoveNVBROADCAST
    bool RemoveNvidiaCache = false,   // -RemoveNvidiaCache (кэш шейдеров)
    bool RemoveNvidiaDirs = false,    // -RemoveNvidiaDirs
    bool PreventWinUpdate = true,     // -PreventWinUpdate: SearchOrderConfig=0 (ВЕРНУТЬ =1 после установки нового драйвера!)
    bool RemoveAmdCp = false,         // -RemoveAMDCP
    bool RemoveIntelCp = false);      // -RemoveINTELCP

public enum DduStage { None, Armed, InSafeMode, Cleaned, Done, Failed, Cancelled }

public sealed record DduSession(string Id, DduStage Stage, string DduExe, string Args, int? OriginalSearchOrderConfig,
    DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, string? Error);

public static class DduPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "hateeze");
    public static readonly string DduDir = Path.Combine(Root, "ddu");
    public static readonly string GuardDir = Path.Combine(Root, "guard");
    public static readonly string GuardExe = Path.Combine(GuardDir, "hateeze.exe");
    public static readonly string StateFile = Path.Combine(Root, "ddu-session.json");
    public static readonly string Bcdedit = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
    public static readonly string Sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
    public static readonly string Shutdown = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
    public const string GuardServiceName = "hateezeSafeBootGuard";
    public const string RunOnceValue = "*hateezeDDU";       // "*" => запускается и в безопасном режиме
    public const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
}

public static partial class DduDownloader
{
    /// <summary>Официальный машинный источник версии (его же опрашивает сам DDU, см. CheckUpdate.vb). Ответ: "18.1.6.2".</summary>
    public const string VersionTxt = "https://www.wagnardsoft.com/DDU/currentversion2.txt";
    /// <summary>JSON {"latestVersion","releaseDate","downloadUrl",...}. downloadUrl на 2026-10-26 ведёт на 404 — НЕ использовать.</summary>
    public const string VersionJson = "https://www.wagnardsoft.com/api/ddu/version.json";

    public static IEnumerable<Uri> PortableUrls(Version v)
    {
        yield return new Uri($"https://download.wagnardsoft.com/DDU/DDU%20v{v}.exe");          // 18.1.6.x — проверено (200)
        yield return new Uri($"https://www.wagnardsoft.com/DDU/download/DDU%20v{v}.exe");      // старый путь: работал до 18.1.5.4
    }

    public static async Task<Version> GetLatestVersionAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var s = (await Http.Client.GetStringAsync(VersionTxt, cts.Token)).Trim();
            if (Version.TryParse(s, out var v)) return v;
        }
        catch (HttpRequestException) { }
        using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(VersionJson, cts.Token));
        return Version.Parse(doc.RootElement.GetProperty("latestVersion").GetString()!);
    }

    /// <summary>Скачивает портативный DDU (7-Zip SFX "7z.sfx"), проверяет подпись "Wagnardsoft", распаковывает: "-y" "-o&lt;dir&gt;".</summary>
    public static async Task<string> DownloadAndExtractAsync(Version v, IProgress<(long, long?)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(DduPaths.DduDir);
        string sfx = Path.Combine(DduPaths.DduDir, $"DDU v{v}.exe");
        Exception? last = null;
        foreach (var url in PortableUrls(v))
        {
            try { await Http.DownloadAsync(url, sfx, progress, TimeSpan.FromSeconds(30), ct); last = null; break; }
            catch (Exception ex) { last = ex; }
        }
        if (last is not null) throw last;

        if (!Authenticode.IsTrustedFrom(sfx, "Wagnardsoft"))
            throw new InvalidDataException("Подпись DDU недействительна или подписант не Wagnardsoft — файл удалён.");

        string outDir = Path.Combine(DduPaths.DduDir, v.ToString());
        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        var psi = new ProcessStartInfo(sfx) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-y");                    // 7z.sfx: только ровно "-y" (иначе "Bad command")
        psi.ArgumentList.Add("-o" + outDir);           // "-o<путь>" слитно; кавычки ставит ArgumentList при пробелах
        using (var p = Process.Start(psi)!)
        {
            if (!p.WaitForExit(120_000)) { try { p.Kill(); } catch { } throw new TimeoutException("Распаковка DDU > 2 мин"); }
            if (p.ExitCode != 0) throw new InvalidOperationException($"Распаковка DDU: код {p.ExitCode}");
        }
        // Структуру внутри архива НЕ фиксируем (может быть подпапка "DDU vX.Y.Z.W") — ищем exe рекурсивно.
        var exe = Directory.EnumerateFiles(outDir, "Display Driver Uninstaller.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new FileNotFoundException("После распаковки не найден 'Display Driver Uninstaller.exe'");
        if (!Authenticode.IsTrustedFrom(exe, "Wagnardsoft")) throw new InvalidDataException("Подпись Display Driver Uninstaller.exe не прошла проверку");
        return exe;
    }

    public static string BuildArgs(DduOptions o)
    {
        var a = new List<string> { "-silent", "-NoSafeModeMsg" };      // БЕЗ -Restart: перезагружает hateeze после выхода DDU
        switch (o.Vendor)
        {
            case GpuVendor.Nvidia:
                a.Add("-CleanNvidia");
                if (o.RemovePhysX) a.Add("-RemovePhysx");
                if (o.RemoveNvidiaApp) a.Add("-RemoveGFE");
                if (o.RemoveNvBroadcast) a.Add("-RemoveNVBROADCAST");
                if (o.RemoveNvidiaCache) a.Add("-RemoveNvidiaCache");
                if (o.RemoveNvidiaDirs) a.Add("-RemoveNvidiaDirs");
                break;
            case GpuVendor.Amd:
                a.Add("-CleanAmd");
                if (o.RemoveAmdCp) a.Add("-RemoveAMDCP");
                break;
            case GpuVendor.Intel:
                a.Add("-CleanIntel");
                if (o.RemoveIntelCp) a.Add("-RemoveINTELCP");
                break;
        }
        if (o.PreventWinUpdate) a.Add("-PreventWinUpdate");
        a.Add("-NoRestorePoint");   // в безопасном режиме точку восстановления создать нельзя; создаём её сами ДО перезагрузки
        return string.Join(' ', a);
    }
}

public static class SafeBoot
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CLEANBOOT = 67;
    /// <summary>0 = обычная загрузка, 1 = безопасный режим, 2 = безопасный режим с сетью.</summary>
    public static int CurrentBootMode => GetSystemMetrics(SM_CLEANBOOT);

    /// <summary>
    /// Установлен ли safeboot в BCD для {current}: читаем куст HKLM\BCD00000000 (не зависит от языка вывода bcdedit).
    /// Элемент 0x25000080 = safeboot (0 Minimal, 1 Network, 2 DsRepair).
    /// </summary>
    public static bool IsSafeBootArmed()
    {
        var guid = CurrentLoaderGuid();
        if (guid is null) return false;
        using var k = Registry.LocalMachine.OpenSubKey($@"BCD00000000\Objects\{guid}\Elements\25000080");
        return k is not null;
    }

    public static string? CurrentLoaderGuid()
    {
        // "bcdedit /enum {current} /v" печатает GUID вместо алиаса; подпись поля может быть локализована — берём первый GUID.
        var (code, output) = Run(DduPaths.Bcdedit, "/enum", "{current}", "/v");
        if (code != 0) return null;
        var m = Regex.Match(output, @"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static bool Arm(bool withNetwork) =>
        Run(DduPaths.Bcdedit, "/set", "{current}", "safeboot", withNetwork ? "network" : "minimal").code == 0;

    /// <summary>Идемпотентно: успех, если после вызова значения нет (bcdedit вернёт ≠0, когда удалять нечего).</summary>
    public static bool Disarm()
    {
        for (int i = 0; i < 3; i++)
        {
            Run(DduPaths.Bcdedit, "/deletevalue", "{current}", "safeboot");
            if (!IsSafeBootArmed()) return true;
            Thread.Sleep(2000);
        }
        return false;
    }

    internal static (int code, string output) Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20_000)) { try { p.Kill(); } catch { } return (-1, "timeout"); }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}

public static class DduOrchestrator
{
    private static readonly JsonSerializerOptions J = new() { WriteIndented = true };

    public static DduSession? LoadSession() =>
        File.Exists(DduPaths.StateFile) ? JsonSerializer.Deserialize<DduSession>(File.ReadAllText(DduPaths.StateFile)) : null;

    private static void Save(DduSession s)
    {
        Directory.CreateDirectory(DduPaths.Root);
        string tmp = DduPaths.StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s with { UpdatedUtc = DateTimeOffset.UtcNow }, J));
        File.Move(tmp, DduPaths.StateFile, overwrite: true);
    }

    /// <summary>Предпроверки. Возвращает список блокирующих причин (пусто = можно).</summary>
    public static List<string> Preflight()
    {
        var problems = new List<string>();
        if (SafeBoot.CurrentBootMode != 0) problems.Add("Система уже в безопасном режиме.");
        if (IsRebootPending()) problems.Add("Windows ожидает перезагрузку после обновления — сначала перезагрузите ПК.");
        if (LoadSession() is { Stage: DduStage.Armed or DduStage.InSafeMode }) problems.Add("Предыдущий сеанс DDU не завершён.");
        return problems;
    }

    public static bool IsRebootPending()
    {
        using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        if (lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is { } a) { a.Dispose(); return true; }
        if (lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is { } b) { b.Dispose(); return true; }
        using var sm = lm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
        return sm?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 };
    }

    /// <summary>
    /// Шаг 1 (обычный режим). ПОРЯДОК ВАЖЕН (как в DDU): сначала все страховки, последним — bcdedit /set safeboot.
    /// </summary>
    public static void ArmAndReboot(string dduExe, DduOptions o)
    {
        // 0) страховка №1: служба, стартующая ДО входа пользователя и в безопасном режиме (SafeBoot\Minimal|Network)
        Directory.CreateDirectory(DduPaths.GuardDir);
        File.Copy(Environment.ProcessPath!, DduPaths.GuardExe, overwrite: true);   // НЕ %TEMP%: его могут почистить
        var (sc, scOut) = SafeBoot.Run(DduPaths.Sc, "create", DduPaths.GuardServiceName,
            "binPath=", $"\"{DduPaths.GuardExe}\" --safeboot-guard", "start=", "auto", "DisplayName=", "hateeze Safe Mode guard");
        if (sc != 0 && sc != 1073 /*ERROR_SERVICE_EXISTS*/) throw new InvalidOperationException($"sc create: {sc} {scOut}");
        foreach (var mode in new[] { "Minimal", "Network" })
        {
            using var k = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Control\SafeBoot\{mode}\{DduPaths.GuardServiceName}");
            k.SetValue("", "Service");
        }

        // 1) страховка №2 + полезная нагрузка: RunOnce с "*" (выполняется в Safe Mode, ТОЛЬКО при входе администратора)
        string cmd = $"\"{DduPaths.GuardExe}\" --ddu-stage2";
        if (cmd.Length > 260) throw new InvalidOperationException("RunOnce: командная строка > 260 символов");
        using (var ro = Registry.LocalMachine.OpenSubKey(DduPaths.RunOnceKey, writable: true) ?? Registry.LocalMachine.CreateSubKey(DduPaths.RunOnceKey))
            ro.SetValue(DduPaths.RunOnceValue, cmd);

        // 2) состояние
        int? searchOrder = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching")?.GetValue("SearchOrderConfig") as int?;
        Save(new DduSession(Guid.NewGuid().ToString("N"), DduStage.Armed, dduExe, DduFormatArgs(o), searchOrder,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null));

        // 3) точка невозврата
        if (!SafeBoot.Arm(withNetwork: false))
        {
            RemoveGuardAndRunOnce();
            Save(LoadSession()! with { Stage = DduStage.Failed, Error = "bcdedit /set {current} safeboot minimal завершился с ошибкой" });
            throw new InvalidOperationException("Не удалось включить безопасный режим (bcdedit). Изменения отменены.");
        }
        SafeBoot.Run(DduPaths.Shutdown, "/r", "/t", "10", "/c", "hateeze: перезагрузка в безопасный режим для DDU", "/d", "p:4:1");
    }

    private static string DduFormatArgs(DduOptions o) => DduDownloader.BuildArgs(o);

    /// <summary>Шаг 2 (безопасный режим, после входа администратора; запускается из RunOnce).</summary>
    public static int Stage2()
    {
        var s = LoadSession();
        if (s is null || s.Stage is not (DduStage.Armed or DduStage.InSafeMode)) { SafeBoot.Disarm(); return 1; }
        // Первым делом — гарантировать, что СЛЕДУЮЩАЯ загрузка будет обычной (идемпотентно; служба могла уже сделать это).
        bool disarmed = SafeBoot.Disarm();
        Save(s with { Stage = DduStage.InSafeMode, Error = disarmed ? null : "safeboot не удалён" });

        if (SafeBoot.CurrentBootMode == 0) { Save(s with { Stage = DduStage.Failed, Error = "Stage2 запущен не в безопасном режиме" }); RemoveGuardAndRunOnce(); return 2; }

        int exit;
        var psi = new ProcessStartInfo(s.DduExe, s.Args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(s.DduExe)! };
        using (var p = Process.Start(psi)!)
        {
            if (!p.WaitForExit(TimeSpan.FromMinutes(30))) { try { p.Kill(true); } catch { } exit = -1; }
            else exit = p.ExitCode;
        }
        Save(LoadSession()! with { Stage = exit == 0 ? DduStage.Cleaned : DduStage.Failed, Error = exit == 0 ? null : $"DDU exit {exit}" });
        RemoveGuardAndRunOnce();
        if (!SafeBoot.IsSafeBootArmed())
            SafeBoot.Run(DduPaths.Shutdown, "/r", "/t", "15", "/c", "hateeze: DDU завершён, перезагрузка в обычный режим", "/d", "p:4:1");
        return exit;
    }

    /// <summary>Вызывать при каждом старте приложения в ОБЫЧНОМ режиме.</summary>
    public static void OnAppStartNormalMode()
    {
        var s = LoadSession();
        if (s is null || SafeBoot.CurrentBootMode != 0) return;
        if (s.Stage == DduStage.Armed && SafeBoot.IsSafeBootArmed())
        {
            // Мы "взвели" safe mode, но перезагрузки не было (пользователь отменил shutdown /a и т.п.) —
            // UI должен спросить: "Перезагрузиться сейчас" / "Отменить". Отмена = Disarm() + RemoveGuardAndRunOnce().
            return;
        }
        SafeBoot.Disarm();                // на всякий случай (идемпотентно)
        RemoveGuardAndRunOnce();
        // Stage Cleaned -> показать "Установите драйвер"; после установки вернуть SearchOrderConfig = s.OriginalSearchOrderConfig ?? 1.
    }

    public static void RemoveGuardAndRunOnce()
    {
        try { using var ro = Registry.LocalMachine.OpenSubKey(DduPaths.RunOnceKey, writable: true); ro?.DeleteValue(DduPaths.RunOnceValue, throwOnMissingValue: false); } catch { }
        foreach (var mode in new[] { "Minimal", "Network" })
            try { Registry.LocalMachine.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Control\SafeBoot\{mode}\{DduPaths.GuardServiceName}", throwOnMissingSubKey: false); } catch { }
        SafeBoot.Run(DduPaths.Sc, "stop", DduPaths.GuardServiceName);
        SafeBoot.Run(DduPaths.Sc, "delete", DduPaths.GuardServiceName);
    }
}
