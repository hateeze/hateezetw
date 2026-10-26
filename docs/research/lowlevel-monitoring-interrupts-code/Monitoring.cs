// Production-style READ-ONLY monitoring helpers (all executed by the probe on the dev machine).
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace Hateeze.Monitoring;

// ============================ PDH ============================
public sealed unsafe partial class PdhMonitor : IDisposable
{
    [StructLayout(LayoutKind.Explicit)]
    public struct PDH_FMT_COUNTERVALUE { [FieldOffset(0)] public uint CStatus; [FieldOffset(8)] public double doubleValue; [FieldOffset(8)] public long largeValue; }
    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM_W { public char* szName; public PDH_FMT_COUNTERVALUE FmtValue; } // 24 bytes on x64

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint PdhAddEnglishCounterW(nint query, string fullPath, nint userData, out nint counter);
    [LibraryImport("pdh.dll")] private static partial uint PdhCollectQueryData(nint query);
    [LibraryImport("pdh.dll")] private static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, void* items);
    [LibraryImport("pdh.dll")] private static partial uint PdhCloseQuery(nint query);

    public const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
    public const uint PDH_MORE_DATA = 0x800007D2, PDH_CSTATUS_NO_OBJECT = 0xC0000BB8, PDH_CSTATUS_NO_COUNTER = 0xC0000BB9, PDH_INVALID_DATA = 0xC0000BC6;

    readonly nint _q;
    readonly Dictionary<string, nint> _c = new();
    byte* _buf; uint _bufSize;

    public PdhMonitor(IEnumerable<string> englishPaths)
    {
        uint st = PdhOpenQueryW(null, 0, out _q);
        if (st != 0) throw new InvalidOperationException($"PdhOpenQuery 0x{st:X8}");
        foreach (var p in englishPaths)
        {
            st = PdhAddEnglishCounterW(_q, p, 0, out nint c);
            if (st == 0) _c[p] = c; // else: счётчик недоступен (старый Win10 / отключены perf counters) — пропускаем
        }
        PdhCollectQueryData(_q); // первый сбор: базовая точка для rate-счётчиков
    }

    public IReadOnlyCollection<string> Available => _c.Keys;

    /// <summary>Call once per interval (e.g. 1 s) from ONE thread. Returns path -> (instance -> value).</summary>
    public Dictionary<string, Dictionary<string, double>> Sample()
    {
        PdhCollectQueryData(_q);
        var res = new Dictionary<string, Dictionary<string, double>>(_c.Count);
        foreach (var (path, c) in _c)
        {
            uint size = 0;
            uint st = PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint n, null);
            if (st != PDH_MORE_DATA) continue;
            if (size > _bufSize) { NativeMemory.Free(_buf); _buf = (byte*)NativeMemory.Alloc(size); _bufSize = size; }
            st = PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out n, _buf);
            if (st != 0) continue;
            var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)_buf;
            var d = new Dictionary<string, double>((int)n);
            for (int i = 0; i < n; i++)
                if (items[i].FmtValue.CStatus <= 1) // PDH_CSTATUS_VALID_DATA(0) / NEW_DATA(1)
                    d[new string(items[i].szName)] = items[i].FmtValue.doubleValue;
            res[path] = d;
        }
        return res;
    }

    public void Dispose() { PdhCloseQuery(_q); NativeMemory.Free(_buf); _buf = null; }
}

// ============================ CPU topology ============================
public sealed record CoreInfo(int Index, ulong Mask, byte EfficiencyClass, bool Smt);
public sealed record CacheGroup(int Level, uint SizeBytes, ulong Mask);
public sealed record CpuTopology(List<CoreInfo> Cores, List<CacheGroup> L3, int NumaNodes, int Groups);

public static unsafe partial class Topology
{
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref uint length);

    /// <summary>Group 0 only (≤64 LP). L3 groups = CCX on Ryzen (Zen2: 2 per CCD; Zen3+: 1 per CCD).</summary>
    public static CpuTopology Read()
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(0xFFFF, null, ref len);
        byte* buf = (byte*)NativeMemory.Alloc(len);
        try
        {
            if (!GetLogicalProcessorInformationEx(0xFFFF, buf, ref len)) throw new System.ComponentModel.Win32Exception();
            var cores = new List<CoreInfo>(); var l3 = new List<CacheGroup>(); int numa = 0, groups = 1;
            for (byte* p = buf; p < buf + len; p += *(uint*)(p + 4))
            {
                uint rel = *(uint*)p; byte* u = p + 8; // union starts at offset 8
                switch (rel)
                {
                    case 0: cores.Add(new CoreInfo(cores.Count, *(ulong*)(u + 24), u[1], (u[0] & 1) != 0)); break; // RelationProcessorCore
                    case 1: numa++; break;                                                                         // RelationNumaNode
                    case 2: if (u[0] == 3) l3.Add(new CacheGroup(3, *(uint*)(u + 4), *(ulong*)(u + 32))); break;  // RelationCache
                    case 4: groups = *(ushort*)(u + 2); break;                                                     // RelationGroup.ActiveGroupCount
                }
            }
            return new CpuTopology(cores, l3, numa, groups);
        }
        finally { NativeMemory.Free(buf); }
    }
}

// ============================ Devices (CfgMgr32) ============================
public sealed record IrqInfo(int AllocNum, uint Flags, ulong Affinity) { public bool IsMsi => AllocNum < 0; }
public sealed record DeviceInfo(string InstanceId, string? Name, string? Class, string? Service, string? Location, string? Parent,
    uint? InterruptSupport, uint? MessageMax, List<IrqInfo> Irqs, uint Status, uint Problem)
{
    public bool SupportsMsi => InterruptSupport is uint s && (s & 0x6) != 0;  // 2 = MSI, 4 = MSI-X
    public string Mode => Irqs.Count == 0 ? "нет IRQ" : Irqs.Any(i => i.IsMsi) ? (Irqs.Count > 1 ? $"MSI ×{Irqs.Count}" : "MSI") : "Line-based";
}

public static unsafe partial class DeviceInventory
{
    [StructLayout(LayoutKind.Sequential)] public struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    static readonly Guid G_Dev = new("a45c254e-df1c-4efd-8020-67d146a850e0"), G_Rel = new("4340a6c5-93fa-4706-972c-7b648008a5a7"),
                         G_Pci = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
    public static readonly DEVPROPKEY FriendlyName = new() { fmtid = G_Dev, pid = 14 }, DeviceDesc = new() { fmtid = G_Dev, pid = 2 },
        Class = new() { fmtid = G_Dev, pid = 9 }, Service = new() { fmtid = G_Dev, pid = 6 }, LocationInfo = new() { fmtid = G_Dev, pid = 15 },
        Parent = new() { fmtid = G_Rel, pid = 8 }, InterruptSupport = new() { fmtid = G_Pci, pid = 14 }, InterruptMessageMaximum = new() { fmtid = G_Pci, pid = 15 };

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint CM_Get_Device_ID_List_SizeW(out uint len, string? filter, uint flags);
    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint CM_Get_Device_ID_ListW(string? filter, char* buf, uint len, uint flags);
    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_DevNode_PropertyW(uint devInst, DEVPROPKEY* key, out uint type, byte* buf, ref uint size, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_Device_IDW(uint devInst, char* buf, uint len, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_First_Log_Conf(out nuint logConf, uint devInst, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_Next_Res_Des(out nuint resDes, nuint current, uint forResource, out uint resourceId, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_Res_Des_Data_Size(out uint size, nuint resDes, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Get_Res_Des_Data(nuint resDes, byte* buf, uint size, uint flags);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Free_Res_Des_Handle(nuint resDes);
    [LibraryImport("cfgmgr32.dll")] private static partial uint CM_Free_Log_Conf_Handle(nuint logConf);

    const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1, CM_GETIDLIST_FILTER_PRESENT = 0x100, CR_SUCCESS = 0, CR_BUFFER_SMALL = 0x1A;
    const uint ALLOC_LOG_CONF = 2, ResType_IRQ = 4;

    public static string[] ListIds(string? enumerator, bool presentOnly = true)
    {
        uint flags = (enumerator != null ? CM_GETIDLIST_FILTER_ENUMERATOR : 0) | (presentOnly ? CM_GETIDLIST_FILTER_PRESENT : 0);
        if (CM_Get_Device_ID_List_SizeW(out uint len, enumerator, flags) != CR_SUCCESS) return Array.Empty<string>();
        var buf = new char[len];
        fixed (char* p = buf) if (CM_Get_Device_ID_ListW(enumerator, p, len, flags) != CR_SUCCESS) return Array.Empty<string>();
        return new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public static object? GetProp(uint dn, DEVPROPKEY key)
    {
        uint size = 0;
        uint cr = CM_Get_DevNode_PropertyW(dn, &key, out uint type, null, ref size, 0);
        if (cr != CR_BUFFER_SMALL && cr != CR_SUCCESS || size == 0) return null;
        byte[] arr = new byte[size];
        fixed (byte* b = arr)
        {
            if (CM_Get_DevNode_PropertyW(dn, &key, out type, b, ref size, 0) != CR_SUCCESS) return null;
            return type switch
            {
                0x12 => new string((char*)b, 0, (int)size / 2).TrimEnd('\0'),  // DEVPROP_TYPE_STRING
                0x07 => *(uint*)b,                                               // DEVPROP_TYPE_UINT32
                0x11 => b[0] != 0,                                               // DEVPROP_TYPE_BOOLEAN
                0x2012 => new string((char*)b, 0, (int)size / 2).Split('\0', StringSplitOptions.RemoveEmptyEntries), // STRING_LIST
                _ => arr
            };
        }
    }

    public static string? GetId(uint dn) { char* b = stackalloc char[400]; return CM_Get_Device_IDW(dn, b, 400, 0) == CR_SUCCESS ? new string(b) : null; }

    /// <summary>Allocated IRQs. MSI/MSI-X => IRQD_Alloc_Num is negative (as Int32); one entry per allocated message.</summary>
    public static List<IrqInfo> GetIrqs(uint dn)
    {
        var list = new List<IrqInfo>();
        if (CM_Get_First_Log_Conf(out nuint lc, dn, ALLOC_LOG_CONF) != CR_SUCCESS) return list; // нет ресурсов / устройство остановлено
        nuint cur = lc; bool own = false;
        while (CM_Get_Next_Res_Des(out nuint rd, cur, ResType_IRQ, out _, 0) == CR_SUCCESS)
        {
            if (own) CM_Free_Res_Des_Handle(cur);
            own = true; cur = rd;
            if (CM_Get_Res_Des_Data_Size(out uint sz, rd, 0) != CR_SUCCESS || sz < 24) continue;
            byte* d = stackalloc byte[(int)sz];
            if (CM_Get_Res_Des_Data(rd, d, sz, 0) != CR_SUCCESS) continue;
            // IRQ_DES_64: +0 Count, +4 Type, +8 Flags (DWORD; observed 0x10002 for MSI, 0x1 for shared level line), +12 Alloc_Num, +16 Affinity (KAFFINITY)
            list.Add(new IrqInfo(*(int*)(d + 12), *(uint*)(d + 8), *(ulong*)(d + 16)));
        }
        if (own) CM_Free_Res_Des_Handle(cur);
        CM_Free_Log_Conf_Handle(lc);
        return list;
    }

    public static DeviceInfo? Describe(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint dn, instanceId, 0) != CR_SUCCESS) return null;
        CM_Get_DevNode_Status(out uint status, out uint problem, dn, 0);
        return new DeviceInfo(instanceId,
            (GetProp(dn, FriendlyName) ?? GetProp(dn, DeviceDesc)) as string, GetProp(dn, Class) as string, GetProp(dn, Service) as string,
            GetProp(dn, LocationInfo) as string, GetProp(dn, Parent) as string,
            GetProp(dn, InterruptSupport) as uint?, GetProp(dn, InterruptMessageMaximum) as uint?, GetIrqs(dn), status, problem);
    }

    /// <summary>All present PCI devices that have IRQ resources.</summary>
    public static List<DeviceInfo> PciWithInterrupts() =>
        ListIds("PCI").Select(Describe).Where(d => d is { Irqs.Count: > 0 }).Select(d => d!).ToList();

    /// <summary>Walk parents from a HID device to the first PCI controller (xHCI). Returns (instanceId, name) or null (PS/2, Bluetooth via ACPI, etc).</summary>
    public static (string Id, string? Name)? FindPciController(string instanceId)
    {
        if (CM_Locate_DevNodeW(out uint dn, instanceId, 0) != CR_SUCCESS) return null;
        for (int depth = 0; depth < 16; depth++)
        {
            if (CM_Get_Parent(out uint par, dn, 0) != CR_SUCCESS) return null;
            string? id = GetId(par);
            if (id is null || id.StartsWith(@"HTREE\ROOT", StringComparison.OrdinalIgnoreCase)) return null;
            if (id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
                return (id, (GetProp(par, FriendlyName) ?? GetProp(par, DeviceDesc)) as string);
            dn = par;
        }
        return null;
    }

    /// <summary>Mouse/keyboard HID children (class Mouse/Keyboard) and their PCI controllers.</summary>
    public static List<(string Kind, string HidId, string? ControllerId, string? ControllerName)> InputControllers()
    {
        var res = new List<(string, string, string?, string?)>();
        foreach (var id in ListIds("HID"))
        {
            if (CM_Locate_DevNodeW(out uint dn, id, 0) != CR_SUCCESS) continue;
            if (GetProp(dn, Class) is not string cls || (cls != "Mouse" && cls != "Keyboard")) continue;
            var ctl = FindPciController(id);
            res.Add((cls == "Mouse" ? "Мышь" : "Клавиатура", id, ctl?.Id, ctl?.Name));
        }
        return res;
    }
}

// ============================ Kernel modules (Routine address -> driver) ============================
public sealed unsafe partial class DriverMap
{
    [LibraryImport("ntdll.dll")] private static partial int NtQuerySystemInformation(int cls, void* buf, uint len, out uint retLen);
    public ulong[] Base = Array.Empty<ulong>(), End = Array.Empty<ulong>();
    public string[] Name = Array.Empty<string>(), Path = Array.Empty<string>();
    public int ZeroBases;

    /// <summary>SystemModuleInformation (11). On Win11 24H2+ base addresses are ZERO unless SeDebugPrivilege is ENABLED
    /// in the caller token (call Privileges.Enable("SeDebugPrivilege") first).</summary>
    public static DriverMap Load()
    {
        NtQuerySystemInformation(11, null, 0, out uint size);
        size += 64 * 1024;
        byte* buf = (byte*)NativeMemory.Alloc(size);
        try
        {
            int st = NtQuerySystemInformation(11, buf, size, out _);
            if (st != 0) throw new InvalidOperationException($"NtQuerySystemInformation(11) 0x{st:X8}");
            int n = (int)*(uint*)buf;
            var rows = new List<(ulong b, uint s, string name, string path)>(n);
            int zeros = 0;
            for (int i = 0; i < n; i++)
            {
                // RTL_PROCESS_MODULE_INFORMATION (x64, 296 bytes): +16 ImageBase, +24 ImageSize, +38 OffsetToFileName, +40 FullPathName[256] (ANSI)
                byte* e = buf + 8 + i * 296;
                ulong b = *(ulong*)(e + 16); uint s = *(uint*)(e + 24); ushort off = *(ushort*)(e + 38);
                string full = Marshal.PtrToStringAnsi((nint)(e + 40)) ?? "";
                if (b == 0) zeros++;
                rows.Add((b, s, full.Substring(Math.Min(off, full.Length)), full));
            }
            rows.Sort((x, y) => x.b.CompareTo(y.b));
            return new DriverMap
            {
                Base = rows.Select(r => r.b).ToArray(), End = rows.Select(r => r.b + r.s).ToArray(),
                Name = rows.Select(r => r.name).ToArray(), Path = rows.Select(r => r.path).ToArray(), ZeroBases = zeros
            };
        }
        finally { NativeMemory.Free(buf); }
    }

    public int Find(ulong addr)
    {
        int lo = 0, hi = Base.Length - 1, f = -1;
        while (lo <= hi) { int m = (lo + hi) >>> 1; if (Base[m] <= addr) { f = m; lo = m + 1; } else hi = m - 1; }
        return f >= 0 && addr < End[f] ? f : -1;
    }
}
