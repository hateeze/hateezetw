// Production-style helpers. COMPILE-CHECKED. Methods that change state are NOT executed by the probe.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Hateeze.LowLevel;

// ============================ Privileges ============================
public static unsafe partial class Privileges
{
    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct TOKEN_PRIVILEGES1 { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }
    const uint TOKEN_QUERY = 0x8, TOKEN_ADJUST_PRIVILEGES = 0x20, SE_PRIVILEGE_ENABLED = 0x2;
    const int ERROR_NOT_ALL_ASSIGNED = 1300;

    [LibraryImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)][return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? system, string name, out LUID luid);
    [LibraryImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TOKEN_PRIVILEGES1* newState, uint len, void* prev, uint* retLen);
    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint h);

    /// <summary>Enables a privilege the token already HAS (e.g. "SeDebugPrivilege", "SeIncreaseBasePriorityPrivilege").
    /// Returns false if the token does not hold it (ERROR_NOT_ALL_ASSIGNED).</summary>
    public static bool Enable(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out nint tok)) return false;
        try
        {
            if (!LookupPrivilegeValueW(null, name, out LUID luid)) return false;
            var tp = new TOKEN_PRIVILEGES1 { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            bool ok = AdjustTokenPrivileges(tok, false, &tp, 0, null, null);
            return ok && Marshal.GetLastPInvokeError() != ERROR_NOT_ALL_ASSIGNED; // AdjustTokenPrivileges returns TRUE even if not assigned!
        }
        finally { CloseHandle(tok); }
    }
}

// ============================ Timer ============================
public static unsafe partial class TimerRes
{
    [LibraryImport("ntdll.dll")] public static partial int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
    [LibraryImport("ntdll.dll")] public static partial int NtSetTimerResolution(uint desired, [MarshalAs(UnmanagedType.U1)] bool set, out uint current);
    [LibraryImport("kernel32.dll")] private static partial void Sleep(uint ms);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(nint h, int cls, void* info, uint size);
    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();

    public const int ProcessPowerThrottling = 4;
    public const uint PPT_VERSION = 1, PPT_EXECUTION_SPEED = 0x1, PPT_IGNORE_TIMER_RESOLUTION = 0x4;
    [StructLayout(LayoutKind.Sequential)] public struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    /// <summary>Win11: keep honoring OUR timer request when our window is minimized/occluded (ControlMask=IGNORE, StateMask=0).
    /// On Win10 the flag is unknown -> call may fail; ignore the result.</summary>
    public static bool AlwaysHonorTimerResolution(nint process = 0)
    {
        var s = new PROCESS_POWER_THROTTLING_STATE { Version = PPT_VERSION, ControlMask = PPT_IGNORE_TIMER_RESOLUTION, StateMask = 0 };
        return SetProcessInformation(process == 0 ? GetCurrentProcess() : process, ProcessPowerThrottling, &s, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
    }

    /// <summary>EcoQoS on (true) / HighQoS (false) / null = system managed.</summary>
    public static bool SetEcoQoS(nint process, bool? eco)
    {
        var s = new PROCESS_POWER_THROTTLING_STATE { Version = PPT_VERSION };
        if (eco is null) { s.ControlMask = 0; s.StateMask = 0; }
        else { s.ControlMask = PPT_EXECUTION_SPEED; s.StateMask = eco.Value ? PPT_EXECUTION_SPEED : 0; }
        return SetProcessInformation(process, ProcessPowerThrottling, &s, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
    }

    public readonly record struct SleepStats(double MinMs, double AvgMs, double MaxMs, double StdevMs, int Samples);

    /// <summary>Port of valleyofdoom MeasureSleep: QPC around Sleep(n), first sample discarded. Call on a high-priority thread.</summary>
    public static SleepStats MeasureSleep(int samples = 50, uint sleepMs = 1, int pauseMs = 100)
    {
        var d = new double[samples];
        double f = Stopwatch.Frequency;
        for (int i = 0; i < samples + 1; i++)
        {
            long a = Stopwatch.GetTimestamp();
            Sleep(sleepMs);
            long b = Stopwatch.GetTimestamp();
            if (i > 0) d[i - 1] = (b - a) * 1000.0 / f;
            if (pauseMs > 0) Sleep((uint)pauseMs);
        }
        double avg = d.Average();
        double sd = Math.Sqrt(d.Sum(x => (x - avg) * (x - avg)) / Math.Max(1, samples - 1));
        return new SleepStats(d.Min(), avg, d.Max(), sd, samples);
    }
}

// ============================ Processes ============================
public static unsafe partial class Proc
{
    public const uint PROCESS_SET_INFORMATION = 0x0200, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_SET_LIMITED_INFORMATION = 0x2000;
    public const uint IDLE_PRIORITY_CLASS = 0x40, BELOW_NORMAL_PRIORITY_CLASS = 0x4000, NORMAL_PRIORITY_CLASS = 0x20,
                      ABOVE_NORMAL_PRIORITY_CLASS = 0x8000, HIGH_PRIORITY_CLASS = 0x80, REALTIME_PRIORITY_CLASS = 0x100;
    public const int ProcessIoPriority = 33; // NtSet/QueryInformationProcess, value = IO_PRIORITY_HINT (0 VeryLow,1 Low,2 Normal,3 High)

    [LibraryImport("kernel32.dll", SetLastError = true)] public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseHandle(nint h);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetPriorityClass(nint h, uint cls);
    [LibraryImport("kernel32.dll", SetLastError = true)] public static partial uint GetPriorityClass(nint h);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetProcessAffinityMask(nint h, nuint mask);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetProcessAffinityMask(nint h, out nuint procMask, out nuint sysMask);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetProcessDefaultCpuSets(nint h, uint* ids, uint count);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetProcessDefaultCpuSets(nint h, uint* ids, uint count, out uint required);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool QueryFullProcessImageNameW(nint h, uint flags, char* buf, ref uint size);
    [LibraryImport("ntdll.dll")] public static partial int NtSetInformationProcess(nint h, int cls, void* info, uint len);
    [LibraryImport("ntdll.dll")] public static partial int NtQueryInformationProcess(nint h, int cls, void* info, uint len, out uint ret);
    [LibraryImport("ntdll.dll")] public static partial int NtQuerySystemInformation(int cls, void* buf, uint len, out uint retLen);

    public static string? ImagePath(uint pid)
    {
        nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try { char* b = stackalloc char[1024]; uint len = 1024; return QueryFullProcessImageNameW(h, 0, b, ref len) ? new string(b, 0, (int)len) : null; }
        finally { CloseHandle(h); }
    }

    public static bool SetPriority(uint pid, uint cls)
    {
        if (cls == REALTIME_PRIORITY_CLASS) throw new ArgumentException("REALTIME запрещён политикой приложения");
        nint h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return false; // ERROR_ACCESS_DENIED: защищённый/античит-процесс
        try { return SetPriorityClass(h, cls); } finally { CloseHandle(h); }
    }

    /// <summary>Soft affinity via CPU Sets (Win10+). ids from GetSystemCpuSetInformation (e.g. 0x100 + LP index). Empty = clear.</summary>
    public static bool SetCpuSets(uint pid, ReadOnlySpan<uint> ids)
    {
        nint h = OpenProcess(PROCESS_SET_LIMITED_INFORMATION, false, pid);
        if (h == 0) return false;
        try { fixed (uint* p = ids) return SetProcessDefaultCpuSets(h, ids.Length == 0 ? null : p, (uint)ids.Length); }
        finally { CloseHandle(h); }
    }

    /// <summary>IO priority: 0 VeryLow, 1 Low, 2 Normal; 3 High требует SeIncreaseBasePriorityPrivilege (не рекомендуется).</summary>
    public static int SetIoPriority(uint pid, uint hint)
    {
        nint h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
        if (h == 0) return -Marshal.GetLastPInvokeError();
        try { return NtSetInformationProcess(h, ProcessIoPriority, &hint, 4); } finally { CloseHandle(h); }
    }

    public readonly record struct ProcSample(long Pid, string Name, long CpuTime100ns, uint HardFaults, int BasePriority, ulong WorkingSet);

    /// <summary>All processes in ONE call, no per-process handles (works for protected processes too).</summary>
    public static List<ProcSample> Snapshot()
    {
        var list = new List<ProcSample>(400);
        uint size = 1 << 20;
        while (true)
        {
            byte* buf = (byte*)NativeMemory.Alloc(size);
            try
            {
                int st = NtQuerySystemInformation(5 /*SystemProcessInformation*/, buf, size, out uint need);
                if (st == unchecked((int)0xC0000004)) { size = Math.Max(need + 65536, size * 2); continue; } // STATUS_INFO_LENGTH_MISMATCH
                if (st != 0) throw new InvalidOperationException($"NtQuerySystemInformation(5) 0x{st:X8}");
                for (byte* p = buf; ;)
                {
                    uint next = *(uint*)p;
                    // x64 SYSTEM_PROCESS_INFORMATION offsets (verified on 22631):
                    // 0 NextEntryOffset, 4 NumberOfThreads, 8 WorkingSetPrivateSize, 16 HardFaultCount, 24 CycleTime,
                    // 32 CreateTime, 40 UserTime, 48 KernelTime, 56 ImageName(UNICODE_STRING: Length@56, Buffer@64),
                    // 72 BasePriority, 80 UniqueProcessId, 88 InheritedFromUniqueProcessId, 96 HandleCount, 100 SessionId,
                    // 144 WorkingSetSize
                    ushort nlen = *(ushort*)(p + 56); char* nbuf = *(char**)(p + 64);
                    long pid = *(long*)(p + 80);
                    list.Add(new ProcSample(pid, pid == 0 ? "Idle" : nbuf != null ? new string(nbuf, 0, nlen / 2) : "?",
                        *(long*)(p + 40) + *(long*)(p + 48), *(uint*)(p + 16), *(int*)(p + 72), *(ulong*)(p + 144)));
                    if (next == 0) break;
                    p += next;
                }
                return list;
            }
            finally { NativeMemory.Free(buf); }
        }
    }
}

// ============================ MSI / Affinity registry ============================
public static unsafe partial class MsiConfig
{
    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Open_DevNode_Key(uint devInst, uint sam, uint hwProfile, uint disposition, out nint hkey, uint flags);
    const uint KEY_READ = 0x20019, KEY_ALL_ACCESS = 0xF003F, RegDisposition_OpenAlways = 0, RegDisposition_OpenExisting = 1, CM_REGISTRY_HARDWARE = 0;
    const string MsiSub = @"Interrupt Management\MessageSignaledInterruptProperties";
    const string AffSub = @"Interrupt Management\Affinity Policy";

    /// <summary>Snapshot for rollback. null = value absent (rollback must DELETE it, not write 0).</summary>
    public sealed record Backup(string InstanceId, int? MSISupported, int? MessageNumberLimit, int? DevicePolicy, int? DevicePriority, byte[]? AssignmentSetOverride, RegistryValueKind? AsoKind);

    /// <summary>Opens "Enum\&lt;id&gt;\Device Parameters" (= device HARDWARE key).</summary>
    public static RegistryKey? OpenHwKey(string instanceId, bool write)
    {
        if (CM_Locate_DevNodeW(out uint dn, instanceId, 0) != 0) return null;
        uint cr = CM_Open_DevNode_Key(dn, write ? KEY_ALL_ACCESS : KEY_READ, 0, write ? RegDisposition_OpenAlways : RegDisposition_OpenExisting, out nint hk, CM_REGISTRY_HARDWARE);
        return cr == 0 ? RegistryKey.FromHandle(new SafeRegistryHandle(hk, true)) : null;
    }

    public static Backup Read(string instanceId)
    {
        using var hw = OpenHwKey(instanceId, false);
        using var m = hw?.OpenSubKey(MsiSub);
        using var a = hw?.OpenSubKey(AffSub);
        object? aso = a?.GetValue("AssignmentSetOverride");
        RegistryValueKind? asoKind = aso is null ? null : a!.GetValueKind("AssignmentSetOverride");
        byte[]? asoBytes = aso switch { byte[] b => b, int i => BitConverter.GetBytes(i), long l => BitConverter.GetBytes(l), _ => null };
        return new Backup(instanceId, m?.GetValue("MSISupported") as int?, m?.GetValue("MessageNumberLimit") as int?,
            a?.GetValue("DevicePolicy") as int?, a?.GetValue("DevicePriority") as int?, asoBytes, asoKind);
    }

    /// <summary>NOT executed in probe. Writes MSISupported (and optionally MessageNumberLimit). Requires device restart / reboot.</summary>
    public static void SetMsi(string instanceId, bool enable, int? messageLimit = null)
    {
        using var hw = OpenHwKey(instanceId, true) ?? throw new InvalidOperationException("device key");
        using var m = hw.CreateSubKey(MsiSub, true);
        m.SetValue("MSISupported", enable ? 1 : 0, RegistryValueKind.DWord);
        if (messageLimit is int lim) m.SetValue("MessageNumberLimit", lim, RegistryValueKind.DWord);
    }

    /// <summary>NOT executed. DevicePolicy 4 (SpecifiedProcessors) + mask; mask written as REG_BINARY little-endian, trailing zero bytes trimmed (min 1 byte).</summary>
    public static void SetAffinity(string instanceId, int devicePolicy, ulong? mask, int? devicePriority)
    {
        using var hw = OpenHwKey(instanceId, true) ?? throw new InvalidOperationException("device key");
        using var a = hw.CreateSubKey(AffSub, true);
        a.SetValue("DevicePolicy", devicePolicy, RegistryValueKind.DWord);
        if (devicePolicy == 4 && mask is ulong mk && mk != 0)
        {
            byte[] b = BitConverter.GetBytes(mk); // LE on x64
            int len = 8; while (len > 1 && b[len - 1] == 0) len--;
            a.SetValue("AssignmentSetOverride", b[..len], RegistryValueKind.Binary);
        }
        else a.DeleteValue("AssignmentSetOverride", false);
        if (devicePriority is int pr) a.SetValue("DevicePriority", pr, RegistryValueKind.DWord); else a.DeleteValue("DevicePriority", false);
    }

    /// <summary>NOT executed. Exact rollback: absent values are deleted, present values restored with original kind.</summary>
    public static void Restore(Backup b)
    {
        using var hw = OpenHwKey(b.InstanceId, true) ?? throw new InvalidOperationException("device key");
        using (var m = hw.CreateSubKey(MsiSub, true))
        {
            if (b.MSISupported is int v) m.SetValue("MSISupported", v, RegistryValueKind.DWord); else m.DeleteValue("MSISupported", false);
            if (b.MessageNumberLimit is int l) m.SetValue("MessageNumberLimit", l, RegistryValueKind.DWord); else m.DeleteValue("MessageNumberLimit", false);
        }
        using (var a = hw.CreateSubKey(AffSub, true))
        {
            if (b.DevicePolicy is int p) a.SetValue("DevicePolicy", p, RegistryValueKind.DWord); else a.DeleteValue("DevicePolicy", false);
            if (b.DevicePriority is int q) a.SetValue("DevicePriority", q, RegistryValueKind.DWord); else a.DeleteValue("DevicePriority", false);
            if (b.AssignmentSetOverride is byte[] aso)
            {
                switch (b.AsoKind)
                {
                    case RegistryValueKind.DWord: a.SetValue("AssignmentSetOverride", BitConverter.ToInt32(aso), RegistryValueKind.DWord); break;
                    case RegistryValueKind.QWord: a.SetValue("AssignmentSetOverride", BitConverter.ToInt64(aso), RegistryValueKind.QWord); break;
                    default: a.SetValue("AssignmentSetOverride", aso, RegistryValueKind.Binary); break;
                }
            }
            else a.DeleteValue("AssignmentSetOverride", false);
        }
    }
}

// ============================ System Restore (WMI via late-bound COM, no NuGet) ============================
public static class RestorePoint
{
    const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    /// <summary>READ-ONLY. True if System Protection is ON for the system drive (SPP\Clients list contains it) and not disabled by policy.</summary>
    public static (bool enabled, string details) IsProtectionEnabled()
    {
        string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:"; // "C:"
        using var pol = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore");
        if (pol?.GetValue("DisableSR") is int d && d == 1) return (false, "Отключено групповой политикой (DisableSR=1)");
        using var spp = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients");
        var vols = spp?.GetValue("{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}") as string[] ?? Array.Empty<string>();
        string tag = "(" + sysDrive.TrimEnd('\\').Replace(":", "%3A") + ")"; // "(C%3A)"
        bool on = vols.Any(v => v.EndsWith(tag, StringComparison.OrdinalIgnoreCase));
        using var sr = Registry.LocalMachine.OpenSubKey(SrKey);
        return (on, $"SPP clients={string.Join(";", vols)} RPSessionInterval={sr?.GetValue("RPSessionInterval")}");
    }

    /// <summary>READ-ONLY: list restore points (root\default:SystemRestore) via SWbemLocator.</summary>
    public static List<(uint seq, string desc, string created)> List()
    {
        var res = new List<(uint, string, string)>();
        dynamic loc = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
        dynamic svc = loc.ConnectServer(".", @"root\default");
        foreach (dynamic o in svc.ExecQuery("SELECT * FROM SystemRestore"))
            res.Add((Convert.ToUInt32(o.Properties_.Item("SequenceNumber").Value), (string)o.Properties_.Item("Description").Value, (string)o.Properties_.Item("CreationTime").Value));
        return res;
    }

    /// <summary>NOT executed. Returns WMI return code (0 = S_OK). Caller must compare List() before/after:
    /// within the frequency window Windows returns S_OK but creates nothing.</summary>
    public static int Create(string description, int restorePointType = 12 /*MODIFY_SETTINGS*/)
    {
        dynamic loc = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
        dynamic svc = loc.ConnectServer(".", @"root\default");
        dynamic cls = svc.Get("SystemRestore");
        dynamic inp = cls.Methods_.Item("CreateRestorePoint").InParameters.SpawnInstance_();
        inp.Properties_.Item("Description").Value = description;
        inp.Properties_.Item("RestorePointType").Value = restorePointType;
        inp.Properties_.Item("EventType").Value = 100; // BEGIN_SYSTEM_CHANGE
        dynamic outp = cls.ExecMethod_("CreateRestorePoint", inp);
        return Convert.ToInt32(outp.Properties_.Item("ReturnValue").Value);
    }

    /// <summary>NOT executed. Enables System Protection on the system drive ("C:\"). Only with explicit user consent.</summary>
    public static int EnableOnSystemDrive()
    {
        dynamic loc = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
        dynamic svc = loc.ConnectServer(".", @"root\default");
        dynamic cls = svc.Get("SystemRestore");
        string drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
        dynamic inp = cls.Methods_.Item("Enable").InParameters.SpawnInstance_();
        inp.Properties_.Item("Drive").Value = drive;
        dynamic outp = cls.ExecMethod_("Enable", inp);
        return Convert.ToInt32(outp.Properties_.Item("ReturnValue").Value);
    }

    /// <summary>NOT executed. Temporarily lift the 24h limit; returns previous value (null = absent) for exact rollback.</summary>
    public static int? SetFrequencyMinutes(int? minutes)
    {
        using var k = Registry.LocalMachine.CreateSubKey(SrKey, true);
        int? prev = k.GetValue("SystemRestorePointCreationFrequency") as int?;
        if (minutes is int m) k.SetValue("SystemRestorePointCreationFrequency", m, RegistryValueKind.DWord);
        else k.DeleteValue("SystemRestorePointCreationFrequency", false);
        return prev;
    }
}
