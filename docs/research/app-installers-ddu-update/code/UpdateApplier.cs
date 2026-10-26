using System.Diagnostics;
using Hateeze.Net;

namespace Hateeze.Update;

/// <summary>
/// Безопасная замена запущенного single-file exe.
/// Идея: запущенный exe НЕЛЬЗЯ перезаписать/удалить, но МОЖНО переименовать (тот же том NTFS).
///   1) скачать в  hateeze.exe.new  (рядом, тот же том) и проверить SHA-256 (+ Authenticode, если подписываем);
///   2) hateeze.exe -> hateeze.exe.old ; hateeze.exe.new -> hateeze.exe  (при ошибке — откат);
///   3) запустить новый exe с  --post-update <pid> <oldVersion>  и ждать сигнала "жив" (именованное событие);
///   4) нет сигнала за 60 c / процесс упал -> убить новый, вернуть .old на место, перезапустить старый (откат);
///   5) новый процесс при старте удаляет *.old (с повторами; если занято — MoveFileEx DELAY_UNTIL_REBOOT).
/// </summary>
public static class UpdateApplier
{
    public const string HealthyEventPrefix = @"Local\hateeze-update-healthy-";

    public static string CurrentExe => Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath == null");

    public static async Task<bool> DownloadAndApplyAsync(UpdateInfo info, IProgress<(long, long?)>? progress, CancellationToken ct)
    {
        string exe = CurrentExe;
        string dir = Path.GetDirectoryName(exe)!;
        string newPath = exe + ".new", oldPath = exe + ".old";

        // 0) запись в папку программы возможна? (Program Files требует админа — приложение и так админ)
        var dl = await Http.DownloadAsync(info.AssetUrl, newPath, progress, TimeSpan.FromSeconds(30), ct);

        // 1) целостность
        if (info.Sha256 is null || !dl.Sha256Hex.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        { TryDelete(newPath); throw new InvalidDataException($"SHA-256 не совпадает: ожидали {info.Sha256}, получили {dl.Sha256Hex}"); }
        if (info.Size is long sz && sz != dl.Bytes)
        { TryDelete(newPath); throw new InvalidDataException($"Размер не совпадает: {dl.Bytes} != {sz}"); }
        // 1b) (опционально) Authenticode: Authenticode.Verify(newPath, expectedSubjectCn: "…") — если релизы подписываются

        // 1c) версия внутри файла должна совпадать с ожидаемой (защита от перепутанного ассета)
        var fvi = FileVersionInfo.GetVersionInfo(newPath);
        if (!SemVer.TryParse(fvi.ProductVersion?.Split('+')[0], out var fileVer) || fileVer != info.Version)
        { TryDelete(newPath); throw new InvalidDataException($"ProductVersion файла '{fvi.ProductVersion}' != {info.Version}"); }

        // 2) подмена
        TryDelete(oldPath);
        File.Move(exe, oldPath);                       // переименование запущенного exe — разрешено
        try { File.Move(newPath, exe); }
        catch { File.Move(oldPath, exe); throw; }      // мгновенный откат

        // 3) запуск нового и ожидание "здоровья"
        string evName = HealthyEventPrefix + Environment.ProcessId;
        using var healthy = new EventWaitHandle(false, EventResetMode.ManualReset, evName);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir };
        psi.ArgumentList.Add("--post-update");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        psi.ArgumentList.Add(Environment.GetEnvironmentVariable("HATEEZE_VERSION") ?? "");
        using var child = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start вернул null");

        int idx = await Task.Run(() => WaitHandle.WaitAny(new WaitHandle[] { healthy, new ProcessWaitHandle(child) }, TimeSpan.FromSeconds(60)), ct);
        if (idx == 0) return true;                     // новый процесс жив -> вызывающий код завершает текущий процесс

        // 4) откат
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); child.WaitForExit(10_000); } catch { /* ignore */ }
        TryDelete(exe);
        File.Move(oldPath, exe);
        return false;                                  // остаёмся на старой версии, показываем ошибку
    }

    /// <summary>Вызывать в новом процессе, когда UI (WebView2) успешно инициализирован.</summary>
    public static void SignalHealthyAndCleanup(string[] args)
    {
        int i = Array.IndexOf(args, "--post-update");
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var parentPid)) return;
        try { if (EventWaitHandle.TryOpenExisting(HealthyEventPrefix + parentPid, out var ev)) using (ev) ev.Set(); } catch { }
        try { using var p = Process.GetProcessById(parentPid); p.WaitForExit(15_000); } catch (ArgumentException) { }
        CleanupOld();
    }

    /// <summary>Вызывать при КАЖДОМ старте: удаляет хвост прошлого обновления.</summary>
    public static void CleanupOld()
    {
        string old = CurrentExe + ".old";
        for (int k = 0; k < 10 && File.Exists(old); k++)
        {
            try { File.Delete(old); return; }
            catch (IOException) { Thread.Sleep(500); }      // антивирус/старый процесс ещё держит файл
            catch (UnauthorizedAccessException) { Thread.Sleep(500); }
        }
        if (File.Exists(old)) NativeMethods.MoveFileEx(old, null, NativeMethods.MOVEFILE_DELAY_UNTIL_REBOOT);
        TryDelete(CurrentExe + ".new");
        TryDelete(CurrentExe + ".new.part");
    }

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(Process p) => SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(p.Handle, ownsHandle: false);
    }
}

internal static partial class NativeMethods
{
    public const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static partial bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);
}
