using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Hateeze.Apps;

public sealed record InstalledApp(string Hive, string KeyName, string DisplayName, string? DisplayVersion,
    string? Publisher, string? InstallLocation, string? DisplayIcon, string? UninstallString, string? QuietUninstallString);

/// <summary>Правило обнаружения из каталога apps.json.</summary>
public sealed record DetectRule(
    string[]? KeyNames,            // точные имена подключей Uninstall (например "Discord", "Steam", "{58571ef5-...}_is1")
    string? KeyNameRegex,          // напр. "^\\{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8\\}_Display\\.NvApp$"
    string? DisplayNameRegex,      // напр. "^Mozilla Firefox" (DisplayName может быть ЛОКАЛИЗОВАН — см. NVIDIA)
    string? PublisherRegex,
    string[]? Files);              // напр. "%ProgramFiles(x86)%\\Steam\\steam.exe"

public static class InstalledAppsDetector
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// Перечисляет HKLM (64 и 32-бит, т.е. WOW6432Node) и HKU\&lt;SID интерактивного пользователя&gt;.
    /// ВАЖНО: из повышенного процесса HKCU = профиль того, КТО повысил права. Если пользователь вводил логин/пароль
    /// другого админа (over-the-shoulder UAC), HKCU будет чужим — поэтому берём SID владельца explorer.exe.
    /// </summary>
    public static List<InstalledApp> Enumerate()
    {
        var list = new List<InstalledApp>();
        Scan(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64), "HKLM64", list);
        Scan(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32), "HKLM32", list);
        var sid = InteractiveUser.GetShellUserSid() ?? WindowsIdentity.GetCurrent().User?.Value;
        if (sid is not null)
        {
            using var hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            using var userRoot = hku.OpenSubKey(sid);
            if (userRoot is not null) Scan(userRoot, "HKCU", list);
        }
        return list;
    }

    private static void Scan(RegistryKey root, string hiveTag, List<InstalledApp> list)
    {
        using (root)
        using (var key = root.OpenSubKey(UninstallPath))
        {
            if (key is null) return;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub?.GetValue("DisplayName") is not string dn || string.IsNullOrWhiteSpace(dn)) continue;
                list.Add(new InstalledApp(hiveTag, name, dn,
                    sub.GetValue("DisplayVersion") as string, sub.GetValue("Publisher") as string,
                    sub.GetValue("InstallLocation") as string, sub.GetValue("DisplayIcon") as string,
                    sub.GetValue("UninstallString") as string, sub.GetValue("QuietUninstallString") as string));
            }
        }
    }

    public static InstalledApp? Find(IReadOnlyList<InstalledApp> all, DetectRule r)
    {
        foreach (var a in all)
        {
            if (r.KeyNames is { } kn && kn.Any(k => k.Equals(a.KeyName, StringComparison.OrdinalIgnoreCase))) return a;
            if (r.KeyNameRegex is { } kr && Regex.IsMatch(a.KeyName, kr, RegexOptions.IgnoreCase)) return a;
            if (r.DisplayNameRegex is { } dr && Regex.IsMatch(a.DisplayName, dr, RegexOptions.IgnoreCase) &&
                (r.PublisherRegex is null || (a.Publisher is { } p && Regex.IsMatch(p, r.PublisherRegex, RegexOptions.IgnoreCase))))
                return a;
        }
        return null;
    }

    /// <summary>Версия из exe, если DisplayVersion пуст (Riot, Steam-клиент и т.п.).</summary>
    public static string? FileVersionOf(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
        int comma = path.LastIndexOf(",", StringComparison.Ordinal);          // DisplayIcon вида "C:\x\app.exe,0"
        if (comma > 2 && !File.Exists(path)) path = path[..comma];
        return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).ProductVersion : null;
    }
}

public static class InteractiveUser
{
    /// <summary>SID пользователя, которому принадлежит оболочка (explorer.exe рабочего стола).</summary>
    public static string? GetShellUserSid()
    {
        IntPtr hwnd = GetShellWindow();
        if (hwnd == IntPtr.Zero) return null;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr hProc = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (hProc == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(hProc, 0x0008 /*TOKEN_QUERY*/, out var tok)) return null;
            try { using var wi = new WindowsIdentity(tok); return wi.User?.Value; }
            finally { CloseHandle(tok); }
        }
        catch { return null; }
        finally { CloseHandle(hProc); }
    }

    /// <summary>true, если повышение прав сделано под ДРУГОЙ учёткой (тогда per-user установщики надо запускать без повышения).</summary>
    public static bool ElevatedAsDifferentUser()
    {
        var shell = GetShellUserSid();
        var me = WindowsIdentity.GetCurrent().User?.Value;
        return shell is not null && me is not null && !shell.Equals(me, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
}
