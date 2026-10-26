// READ-ONLY probe: verifies P/Invoke signatures on the dev machine. No Set*/write calls.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
string section = args.Length > 0 ? args[0] : "all";
bool Want(string s) => section == "all" || section == s;

if (Want("pdh")) Probe.Pdh();
if (Want("timer")) Probe.Timer();
if (Want("topo")) Probe.Topology();
if (Want("power")) Probe.PowerInfo();
if (Want("ntq")) Probe.NtQuery();
if (Want("dev")) Probe.Devices();
if (Want("drv")) Probe.Drivers();
if (Want("cpuset")) Probe.CpuSets();
if (Want("proc")) Probe.ProcessInfo();
if (Want("etwq")) Probe.EtwQuery();
if (Want("priv")) Probe.Priv();
if (Want("procs")) Probe.Procs();
if (Want("llapi"))
{
    // READ-ONLY calls from LowLevelApi.cs
    var (en, det) = Hateeze.LowLevel.RestorePoint.IsProtectionEnabled();
    Console.WriteLine($"SystemProtection enabled={en} {det}");
    foreach (var rp in Hateeze.LowLevel.RestorePoint.List().TakeLast(3)) Console.WriteLine($"  RP #{rp.seq} '{rp.desc}' {rp.created}");
    var bk = Hateeze.LowLevel.MsiConfig.Read(@"PCI\VEN_1E4B&DEV_1602&SUBSYS_16021E4B&REV_01\4&2B5FE750&0&0009");
    Console.WriteLine($"NVMe backup: {bk}");
    var snap = Hateeze.LowLevel.Proc.Snapshot();
    Console.WriteLine($"Snapshot processes={snap.Count} first={snap[0]} explorerWS={snap.FirstOrDefault(p => p.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)).WorkingSet / 1048576} MB");
    var ss = Hateeze.LowLevel.TimerRes.MeasureSleep(10, 1, 20);
    Console.WriteLine($"MeasureSleep: {ss}");
}
if (Want("mon"))
{
    using var pdh = new Hateeze.Monitoring.PdhMonitor(new[] {
        @"\Processor Information(*)\% Processor Utility", @"\Processor Information(*)\% Processor Performance",
        @"\Processor Information(*)\Processor Frequency", @"\Processor Information(*)\Parking Status",
        @"\Processor Information(*)\% Interrupt Time", @"\Processor Information(*)\% DPC Time",
        @"\Processor Information(*)\Interrupts/sec", @"\Processor Information(*)\DPCs Queued/sec",
        @"\Memory\Page Reads/sec", @"\Memory\Pages Input/sec", @"\PhysicalDisk(*)\Avg. Disk sec/Transfer", @"\Nonexistent\Foo" });
    Console.WriteLine($"PDH available {pdh.Available.Count}/12");
    Thread.Sleep(1000);
    var s = pdh.Sample();
    foreach (var (k, v) in s) Console.WriteLine($"  {k}: {string.Join(" ", v.OrderBy(x => x.Key, StringComparer.Ordinal).Take(4).Select(x => $"[{x.Key}]={x.Value:F1}"))} ... n={v.Count}");
    var t = Hateeze.Monitoring.Topology.Read();
    Console.WriteLine($"Topology: cores={t.Cores.Count} L3 groups={t.L3.Count} ({string.Join(",", t.L3.Select(c => $"{c.SizeBytes >> 20}MB mask=0x{c.Mask:X}"))}) numa={t.NumaNodes} groups={t.Groups}");
    foreach (var d in Hateeze.Monitoring.DeviceInventory.PciWithInterrupts())
        Console.WriteLine($"  {d.Mode,-11} sup={d.InterruptSupport} max={d.MessageMax} svc={d.Service,-9} {d.Name}");
    foreach (var ic in Hateeze.Monitoring.DeviceInventory.InputControllers().DistinctBy(x => x.Kind + x.ControllerId))
        Console.WriteLine($"  {ic.Kind}: {ic.ControllerName} [{ic.ControllerId}]");
    var dm = Hateeze.Monitoring.DriverMap.Load();
    int i1 = dm.Find(dm.Base[^1] + 16);
    Console.WriteLine($"DriverMap modules={dm.Base.Length} zeroBases={dm.ZeroBases} lookup(last+16)={(i1 >= 0 ? dm.Name[i1] : "none")} lookup(0x1000)={dm.Find(0x1000)}");
}
if (Want("nvml"))
{
    int r = Nv.nvmlInit_v2();
    Console.WriteLine($"nvmlInit_v2={r}");
    if (r == 0)
    {
        r = Nv.nvmlDeviceGetHandleByIndex_v2(0, out nint dev);
        r = Nv.nvmlDeviceGetTemperature(dev, 0 /*NVML_TEMPERATURE_GPU*/, out uint t);
        Console.WriteLine($"GPU0 temp rc={r} value={t} C");
        Nv.nvmlShutdown();
    }
}
if (Want("setupapi"))
{
    foreach (var id in new[] { @"PCI\VEN_10DE&DEV_2882&SUBSYS_180110DE&REV_A1\4&1FC990D7&0&0019", @"PCI\VEN_10DE&DEV_22BE&SUBSYS_180110DE&REV_A1\4&1FC990D7&0&0119" })
    {
        Console.WriteLine(Hateeze.Devices.DevRestart.ProbeInstallParams(id));
        Console.WriteLine(Hateeze.Devices.DevRestart.ReadMsiViaCm(id));
    }
}

static unsafe class Probe
{
    // ---------------- PDH ----------------
    public static void Pdh()
    {
        Console.WriteLine("=== PDH ===");
        // localized name lookup for index 238 (Processor) and 6 (% Processor Time)
        foreach (uint idx in new uint[] { 238, 6, 1846 /*Processor Information? may vary*/ })
        {
            uint size = 0;
            uint st = N.PdhLookupPerfNameByIndexW(null, idx, null, ref size);
            char* buf = stackalloc char[(int)Math.Max(size, 1)];
            st = N.PdhLookupPerfNameByIndexW(null, idx, buf, ref size);
            Console.WriteLine($"PdhLookupPerfNameByIndex({idx}) st=0x{st:X8} name='{(st == 0 ? new string(buf) : "")}'");
        }

        uint s = N.PdhOpenQueryW(null, 0, out nint q);
        Console.WriteLine($"PdhOpenQuery st=0x{s:X8}");
        string[] paths =
        {
            @"\Processor Information(*)\% Processor Utility",
            @"\Processor Information(*)\% Processor Performance",
            @"\Processor Information(*)\Processor Frequency",
            @"\Processor Information(*)\Parking Status",
            @"\Processor Information(*)\% Processor Time",
            @"\Processor(_Total)\% Interrupt Time",
            @"\Processor(_Total)\% DPC Time",
            @"\Processor(_Total)\DPCs Queued/sec",
            @"\Processor(_Total)\Interrupts/sec",
            @"\Memory\Page Faults/sec",
            @"\Memory\Pages Input/sec",
            @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer",
        };
        var counters = new List<(string, nint)>();
        foreach (var p in paths)
        {
            uint r = N.PdhAddEnglishCounterW(q, p, 0, out nint c);
            Console.WriteLine($"PdhAddEnglishCounter '{p}' st=0x{r:X8}");
            if (r == 0) counters.Add((p, c));
        }
        // Compare: non-English API with English path (may fail on localized UI)
        uint r2 = N.PdhAddCounterW(q, @"\Processor(_Total)\% Processor Time", 0, out _);
        Console.WriteLine($"PdhAddCounterW (english path, localized API) st=0x{r2:X8}");

        N.PdhCollectQueryData(q);
        Thread.Sleep(1000);
        N.PdhCollectQueryData(q);
        const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
        const uint PDH_MORE_DATA = 0x800007D2;
        foreach (var (p, c) in counters)
        {
            uint bufSize = 0, count = 0;
            uint r = N.PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref bufSize, out count, null);
            if (r != PDH_MORE_DATA) { Console.WriteLine($"{p}: sizing st=0x{r:X8}"); continue; }
            byte* buf = (byte*)NativeMemory.Alloc(bufSize);
            try
            {
                r = N.PdhGetFormattedCounterArrayW(c, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref bufSize, out count, buf);
                var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)buf;
                var sb = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    string name = new string(items[i].szName);
                    sb.Append($"[{name}]={(items[i].FmtValue.CStatus == 0 ? items[i].FmtValue.doubleValue.ToString("F2") : "st" + items[i].FmtValue.CStatus.ToString("X"))} ");
                }
                Console.WriteLine($"{p} (st=0x{r:X8}, n={count}): {sb}");
            }
            finally { NativeMemory.Free(buf); }
        }
        N.PdhCloseQuery(q);
        Console.WriteLine($"sizeof(PDH_FMT_COUNTERVALUE_ITEM_W)={sizeof(PDH_FMT_COUNTERVALUE_ITEM_W)}");
    }

    // ---------------- Timer ----------------
    public static void Timer()
    {
        Console.WriteLine("=== TIMER ===");
        int st = N.NtQueryTimerResolution(out uint min, out uint max, out uint cur);
        Console.WriteLine($"NtQueryTimerResolution st=0x{st:X8} Min(coarsest)={min} ({min / 10000.0} ms) Max(finest)={max} ({max / 10000.0} ms) Current={cur} ({cur / 10000.0} ms)");
        // measure Sleep(1) (process did NOT request resolution)
        var samples = new List<double>();
        long f = Stopwatch.Frequency;
        for (int i = 0; i < 21; i++)
        {
            long a = Stopwatch.GetTimestamp();
            N.Sleep(1);
            long b = Stopwatch.GetTimestamp();
            samples.Add((b - a) * 1000.0 / f);
        }
        samples.RemoveAt(0);
        samples.Sort();
        Console.WriteLine($"Sleep(1) without own request: min={samples[0]:F3} avg={samples.Average():F3} max={samples[^1]:F3} ms");
        Console.WriteLine($"QPC frequency={f}");
    }

    // ---------------- Topology ----------------
    public static void Topology()
    {
        Console.WriteLine("=== TOPOLOGY ===");
        uint len = 0;
        N.GetLogicalProcessorInformationEx(0xFFFF, null, ref len);
        byte* buf = (byte*)NativeMemory.Alloc(len);
        try
        {
            if (!N.GetLogicalProcessorInformationEx(0xFFFF, buf, ref len)) { Console.WriteLine("GLPIEx failed " + Marshal.GetLastPInvokeError()); return; }
            int coreIdx = 0;
            for (byte* p = buf; p < buf + len;)
            {
                uint rel = *(uint*)p; uint size = *(uint*)(p + 4);
                byte* u = p + 8;
                switch (rel)
                {
                    case 0: // RelationProcessorCore
                        {
                            byte flags = u[0], eff = u[1]; ushort gc = *(ushort*)(u + 22);
                            ulong mask = *(ulong*)(u + 24); ushort grp = *(ushort*)(u + 32);
                            Console.WriteLine($"Core#{coreIdx++}: SMT={(flags & 1) == 1} EffClass={eff} group={grp} mask=0x{mask:X}");
                            break;
                        }
                    case 2: // RelationCache
                        {
                            byte level = u[0]; uint csize = *(uint*)(u + 4); uint type = *(uint*)(u + 8);
                            ushort gc = *(ushort*)(u + 30); ulong mask = *(ulong*)(u + 32);
                            if (level == 3) Console.WriteLine($"L3 cache: size={csize / 1024 / 1024} MB type={type} groupCount={gc} mask=0x{mask:X}");
                            break;
                        }
                    case 1: // NUMA
                        {
                            uint node = *(uint*)u; ulong mask = *(ulong*)(u + 24);
                            Console.WriteLine($"NUMA node {node}: mask=0x{mask:X}");
                            break;
                        }
                    case 3: Console.WriteLine($"Package: mask=0x{*(ulong*)(u + 24):X}"); break;
                    case 5: Console.WriteLine($"Die: mask=0x{*(ulong*)(u + 24):X}"); break;
                    case 7: Console.WriteLine($"Module: mask=0x{*(ulong*)(u + 24):X}"); break;
                    case 4:
                        {
                            ushort maxG = *(ushort*)u; ushort actG = *(ushort*)(u + 2);
                            Console.WriteLine($"Groups: max={maxG} active={actG}");
                            break;
                        }
                }
                p += size;
            }
        }
        finally { NativeMemory.Free(buf); }
    }

    // ---------------- Power ----------------
    public static void PowerInfo()
    {
        Console.WriteLine("=== CallNtPowerInformation(ProcessorInformation) ===");
        int n = Environment.ProcessorCount;
        var arr = new PROCESSOR_POWER_INFORMATION[n];
        fixed (PROCESSOR_POWER_INFORMATION* p = arr)
        {
            int st = N.CallNtPowerInformation(11, null, 0, p, (uint)(sizeof(PROCESSOR_POWER_INFORMATION) * n));
            Console.WriteLine($"st=0x{st:X8} sizeof={sizeof(PROCESSOR_POWER_INFORMATION)}");
        }
        foreach (var x in arr) Console.WriteLine($"CPU{x.Number}: MaxMhz={x.MaxMhz} CurrentMhz={x.CurrentMhz} MhzLimit={x.MhzLimit} MaxIdle={x.MaxIdleState} CurIdle={x.CurrentIdleState}");
    }

    // ---------------- NtQuerySystemInformation ----------------
    public static void NtQuery()
    {
        Console.WriteLine("=== NtQuerySystemInformation ===");
        int n = Environment.ProcessorCount;
        var a = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[n];
        var b = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[n];
        fixed (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION* pa = a)
            Console.WriteLine($"class8 st=0x{N.NtQuerySystemInformation(8, pa, (uint)(sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION) * n), out uint rl):X8} retLen={rl} sizeof={sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION)}");
        var ia = new SYSTEM_INTERRUPT_INFORMATION[n];
        fixed (SYSTEM_INTERRUPT_INFORMATION* pi = ia)
            Console.WriteLine($"class23 st=0x{N.NtQuerySystemInformation(23, pi, (uint)(sizeof(SYSTEM_INTERRUPT_INFORMATION) * n), out uint rl2):X8} retLen={rl2} sizeof={sizeof(SYSTEM_INTERRUPT_INFORMATION)}");
        Thread.Sleep(1000);
        fixed (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION* pb = b)
            N.NtQuerySystemInformation(8, pb, (uint)(sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION) * n), out _);
        var ib = new SYSTEM_INTERRUPT_INFORMATION[n];
        fixed (SYSTEM_INTERRUPT_INFORMATION* pi = ib)
            N.NtQuerySystemInformation(23, pi, (uint)(sizeof(SYSTEM_INTERRUPT_INFORMATION) * n), out _);
        for (int i = 0; i < n; i++)
        {
            long idle = b[i].IdleTime - a[i].IdleTime, kern = b[i].KernelTime - a[i].KernelTime, user = b[i].UserTime - a[i].UserTime;
            long dpc = b[i].DpcTime - a[i].DpcTime, isr = b[i].InterruptTime - a[i].InterruptTime;
            long total = kern + user; // kernel includes idle
            double busy = total > 0 ? 100.0 * (total - idle) / total : 0;
            Console.WriteLine($"CPU{i}: busy={busy:F1}% dpc={100.0 * dpc / Math.Max(total, 1):F3}% isr={100.0 * isr / Math.Max(total, 1):F3}% ints/s={b[i].InterruptCount - a[i].InterruptCount} dpcCount/s={ib[i].DpcCount - ia[i].DpcCount} ctxsw/s={ib[i].ContextSwitches - ia[i].ContextSwitches} TimeIncrement={ib[i].TimeIncrement}");
        }
    }

    // ---------------- Devices (CfgMgr32) ----------------
    static readonly Guid DevPkeyGuidDevice = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly Guid DevPkeyGuidRel = new("4340a6c5-93fa-4706-972c-7b648008a5a7");
    static readonly Guid DevPkeyGuidPci = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
    static readonly Guid DevPkeyGuidInst = new("78c34fc8-104a-4aca-9ea4-524d52996e57");

    static object? GetProp(uint devInst, Guid g, uint pid)
    {
        var key = new DEVPROPKEY { fmtid = g, pid = pid };
        uint size = 0;
        uint cr = N.CM_Get_DevNode_PropertyW(devInst, &key, out uint type, null, ref size, 0);
        if (cr != 0x1A /*CR_BUFFER_SMALL*/ && cr != 0) return null;
        byte* buf = stackalloc byte[(int)Math.Max(size, 4)];
        cr = N.CM_Get_DevNode_PropertyW(devInst, &key, out type, buf, ref size, 0);
        if (cr != 0) return null;
        return type switch
        {
            0x12 => new string((char*)buf).TrimEnd('\0'),               // DEVPROP_TYPE_STRING
            0x07 => *(uint*)buf,                                          // DEVPROP_TYPE_UINT32
            0x11 => buf[0] != 0,                                          // BOOLEAN
            0x2012 => string.Join("|", new string((char*)buf, 0, (int)size / 2).Split('\0', StringSplitOptions.RemoveEmptyEntries)), // STRING_LIST
            _ => $"type0x{type:X}"
        };
    }

    static string GetDeviceId(uint devInst)
    {
        char* b = stackalloc char[512];
        return N.CM_Get_Device_IDW(devInst, b, 512, 0) == 0 ? new string(b) : "?";
    }

    static List<(int irq, uint flags, ulong aff)> GetIrqs(uint devInst)
    {
        var list = new List<(int, uint, ulong)>();
        const uint ALLOC_LOG_CONF = 2, ResType_IRQ = 4;
        if (N.CM_Get_First_Log_Conf(out nuint lc, devInst, ALLOC_LOG_CONF) != 0) return list;
        nuint cur = lc;
        bool first = true;
        while (N.CM_Get_Next_Res_Des(out nuint rd, cur, ResType_IRQ, out uint rid, 0) == 0)
        {
            if (!first) N.CM_Free_Res_Des_Handle(cur);
            first = false;
            if (N.CM_Get_Res_Des_Data_Size(out uint sz, rd, 0) == 0 && sz >= 24)
            {
                byte* b = stackalloc byte[(int)sz];
                if (N.CM_Get_Res_Des_Data(rd, b, sz, 0) == 0)
                {
                    uint flags = *(uint*)(b + 8); int alloc = *(int*)(b + 12); ulong aff = *(ulong*)(b + 16);
                    list.Add((alloc, flags, aff));
                }
            }
            cur = rd;
        }
        if (!first) N.CM_Free_Res_Des_Handle(cur);
        N.CM_Free_Log_Conf_Handle(lc);
        return list;
    }

    public static void Devices()
    {
        Console.WriteLine("=== DEVICES (CfgMgr32, PCI present) ===");
        const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x1, CM_GETIDLIST_FILTER_PRESENT = 0x100;
        uint flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
        uint cr = N.CM_Get_Device_ID_List_SizeW(out uint len, "PCI", flags);
        char[] buf = new char[len];
        fixed (char* p = buf) cr = N.CM_Get_Device_ID_ListW("PCI", p, len, flags);
        var ids = new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"list cr={cr} count={ids.Length}");
        foreach (var id in ids)
        {
            if (N.CM_Locate_DevNodeW(out uint dn, id, 0) != 0) continue;
            var irqs = GetIrqs(dn);
            if (irqs.Count == 0) continue;
            var name = GetProp(dn, DevPkeyGuidDevice, 14) ?? GetProp(dn, DevPkeyGuidDevice, 2);
            var intSup = GetProp(dn, DevPkeyGuidPci, 14);
            var msgMax = GetProp(dn, DevPkeyGuidPci, 15);
            var loc = GetProp(dn, DevPkeyGuidDevice, 15);
            var svc = GetProp(dn, DevPkeyGuidDevice, 6);
            var parent = GetProp(dn, DevPkeyGuidRel, 8);
            N.CM_Get_DevNode_Status(out uint status, out uint problem, dn, 0);
            bool msi = irqs.Any(x => x.irq < 0);
            Console.WriteLine($"{name} | svc={svc} | loc={loc} | IntSupport={intSup} MsgMax={msgMax} | mode={(msi ? "MSI" : "LineBased")} msgs={irqs.Count} | IRQs={string.Join(",", irqs.Select(x => $"{x.irq}(f=0x{x.flags:X},aff=0x{x.aff:X})"))} | status=0x{status:X} problem={problem}");
            Console.WriteLine($"     id={id} parent={parent} instIdProp={GetProp(dn, DevPkeyGuidInst, 256)}");
        }

        Console.WriteLine("=== HID mouse -> controller chain ===");
        cr = N.CM_Get_Device_ID_List_SizeW(out len, "HID", flags);
        buf = new char[len];
        fixed (char* p = buf) N.CM_Get_Device_ID_ListW("HID", p, len, flags);
        foreach (var id in new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (N.CM_Locate_DevNodeW(out uint dn, id, 0) != 0) continue;
            var cls = GetProp(dn, DevPkeyGuidDevice, 9) as string;
            if (cls is not ("Mouse" or "Keyboard")) continue;
            var sb = new StringBuilder($"[{cls}] {id}");
            uint curDn = dn;
            for (int depth = 0; depth < 12; depth++)
            {
                if (N.CM_Get_Parent(out uint par, curDn, 0) != 0) break;
                string pid = GetDeviceId(par);
                sb.Append($"\n   -> {pid} ({GetProp(par, DevPkeyGuidDevice, 14) ?? GetProp(par, DevPkeyGuidDevice, 2)})");
                if (pid.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) break;
                curDn = par;
            }
            Console.WriteLine(sb);
        }
    }

    // ---------------- Drivers ----------------
    public static void Drivers()
    {
        Console.WriteLine("=== DRIVERS ===");
        N.K32EnumDeviceDrivers(null, 0, out uint needed);
        int cnt = (int)needed / 8;
        var bases = new nint[cnt];
        fixed (nint* pb = bases) N.K32EnumDeviceDrivers(pb, needed, out needed);
        char* name = stackalloc char[260];
        int zeros = bases.Count(b => b == 0);
        Console.WriteLine($"EnumDeviceDrivers count={cnt} zeroBases={zeros}");
        for (int i = 0; i < Math.Min(5, cnt); i++)
        {
            uint l = N.K32GetDeviceDriverBaseNameW(bases[i], name, 260);
            Console.WriteLine($"  0x{bases[i]:X16} {new string(name, 0, (int)l)}");
        }
        // SystemModuleInformation (11)
        uint size = 0;
        N.NtQuerySystemInformation(11, null, 0, out size);
        byte* buf = (byte*)NativeMemory.Alloc(size + 4096);
        try
        {
            int st = N.NtQuerySystemInformation(11, buf, size + 4096, out size);
            uint n = *(uint*)buf;
            Console.WriteLine($"SystemModuleInformation st=0x{st:X8} modules={n} sizeofEntry=296");
            for (int i = 0; i < Math.Min(4, n); i++)
            {
                byte* e = buf + 8 + i * 296;
                ulong imageBase = *(ulong*)(e + 16); uint imageSize = *(uint*)(e + 24);
                ushort off = *(ushort*)(e + 38);
                string full = Marshal.PtrToStringAnsi((nint)(e + 40)) ?? "";
                Console.WriteLine($"  base=0x{imageBase:X16} size=0x{imageSize:X} name={full.Substring(Math.Min(off, full.Length))} full={full}");
            }
        }
        finally { NativeMemory.Free(buf); }
    }

    // ---------------- CPU Sets ----------------
    public static void CpuSets()
    {
        Console.WriteLine("=== CPU SETS ===");
        N.GetSystemCpuSetInformation(null, 0, out uint len, 0, 0);
        byte* buf = (byte*)NativeMemory.Alloc(len);
        try
        {
            if (!N.GetSystemCpuSetInformation(buf, len, out len, 0, 0)) { Console.WriteLine("fail " + Marshal.GetLastPInvokeError()); return; }
            for (byte* p = buf; p < buf + len;)
            {
                uint sz = *(uint*)p; uint type = *(uint*)(p + 4);
                uint id = *(uint*)(p + 8); ushort grp = *(ushort*)(p + 12);
                byte lp = p[14], core = p[15], llc = p[16], numa = p[17], eff = p[18], fl = p[19];
                Console.WriteLine($"CpuSet id=0x{id:X} grp={grp} LP={lp} core={core} LLC={llc} numa={numa} eff={eff} flags=0x{fl:X} (parked={(fl & 1) != 0}) size={sz}");
                p += sz;
            }
        }
        finally { NativeMemory.Free(buf); }
    }

    // ---------------- ETW query (read-only) ----------------
    public static void EtwQuery()
    {
        Console.WriteLine("=== ETW sessions (QueryAllTracesW) ===");
        Console.WriteLine($"sizeof EVENT_TRACE_PROPERTIES={sizeof(Hateeze.Latency.EVENT_TRACE_PROPERTIES)} (expect 120), EVENT_TRACE_LOGFILEW={sizeof(Hateeze.Latency.EVENT_TRACE_LOGFILEW)} (448), EVENT_RECORD={sizeof(Hateeze.Latency.EVENT_RECORD)} (112), EVENT_HEADER={sizeof(Hateeze.Latency.EVENT_HEADER)} (80)");
        foreach (var s in Hateeze.Latency.Etw.ListSessions())
            Console.WriteLine($"  {s.name,-50} mode=0x{s.mode:X8} sysLogger={(s.mode & 0x02000000) != 0} rt={(s.mode & 0x100) != 0} flags=0x{s.enableFlags:X} lost={s.lost}");
    }

    // ---------------- Processes via SystemProcessInformation (read-only) ----------------
    static Dictionary<long, (string name, long cpu, uint hard, long prio)> Snap()
    {
        var d = new Dictionary<long, (string, long, uint, long)>();
        uint size = 1 << 20;
        while (true)
        {
            byte* buf = (byte*)NativeMemory.Alloc(size);
            try
            {
                int st = N.NtQuerySystemInformation(5, buf, size, out uint need);
                if (st == unchecked((int)0xC0000004)) { size = Math.Max(need + 65536, size * 2); continue; } // STATUS_INFO_LENGTH_MISMATCH
                if (st != 0) throw new Exception($"NtQSI(5) 0x{st:X8}");
                for (byte* p = buf; ;)
                {
                    uint next = *(uint*)p;
                    uint hard = *(uint*)(p + 16);
                    long user = *(long*)(p + 40), kern = *(long*)(p + 48);
                    ushort nlen = *(ushort*)(p + 56); char* nbuf = *(char**)(p + 64);
                    int basePrio = *(int*)(p + 72);
                    long pid = *(long*)(p + 80);
                    string name = pid == 0 ? "Idle" : (nbuf != null ? new string(nbuf, 0, nlen / 2) : "?");
                    d[pid] = (name, user + kern, hard, basePrio);
                    if (next == 0) break;
                    p += next;
                }
                return d;
            }
            finally { NativeMemory.Free(buf); }
        }
    }
    public static void Procs()
    {
        Console.WriteLine("=== PROCESSES (NtQuerySystemInformation class 5) ===");
        var a = Snap(); long t0 = Stopwatch.GetTimestamp();
        Thread.Sleep(1000);
        var b = Snap(); double dt = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
        int n = Environment.ProcessorCount;
        var rows = b.Where(kv => a.ContainsKey(kv.Key)).Select(kv => (pid: kv.Key, kv.Value.name, cpu: (kv.Value.cpu - a[kv.Key].cpu) / 1e7 / dt / n * 100, hard: kv.Value.hard, hardDelta: kv.Value.hard - a[kv.Key].hard, prio: kv.Value.prio))
            .OrderByDescending(r => r.cpu).Take(8);
        Console.WriteLine($"processes={b.Count}");
        char* path = stackalloc char[1024];
        foreach (var r in rows)
        {
            string img = "";
            nint h = N.OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, (uint)r.pid);
            if (h != 0) { uint len = 1024; if (N.QueryFullProcessImageNameW(h, 0, path, ref len)) img = new string(path, 0, (int)len); N.CloseHandle(h); }
            else img = $"<OpenProcess err {Marshal.GetLastPInvokeError()}>";
            Console.WriteLine($"  pid={r.pid,-6} {r.name,-28} cpu={r.cpu,6:F2}% hardFaultsTotal={r.hard} hardFaults/s={r.hardDelta} basePrio={r.prio} path={img}");
        }
    }

    // ---------------- Token privileges ----------------
    public static void Priv()
    {
        Console.WriteLine("=== TOKEN ===");
        const uint TOKEN_QUERY = 0x8, TOKEN_ADJUST_PRIVILEGES = 0x20;
        if (!N.OpenProcessToken(N.GetCurrentProcess(), TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out nint tok)) { Console.WriteLine("OpenProcessToken failed"); return; }
        N.LookupPrivilegeValueW(null, "SeDebugPrivilege", out LUID luid);
        Console.WriteLine($"SeDebugPrivilege LUID={luid.LowPart}:{luid.HighPart} enabledBefore={IsEnabled(tok, luid)}");
        var tp = new TOKEN_PRIVILEGES1 { PrivilegeCount = 1, Luid = luid, Attributes = 0x2 /*SE_PRIVILEGE_ENABLED*/ };
        bool ok = N.AdjustTokenPrivileges(tok, false, &tp, 0, null, null);
        int err = Marshal.GetLastPInvokeError(); // 1300 = ERROR_NOT_ALL_ASSIGNED (privilege not held)
        Console.WriteLine($"AdjustTokenPrivileges ok={ok} lastErr={err} enabledAfter={IsEnabled(tok, luid)} sizeof(TOKEN_PRIVILEGES1)={sizeof(TOKEN_PRIVILEGES1)}");
        N.CloseHandle(tok);
    }
    static bool IsEnabled(nint tok, LUID luid)
    {
        N.GetTokenInformation(tok, 3 /*TokenPrivileges*/, null, 0, out uint len);
        byte* b = stackalloc byte[(int)len];
        if (!N.GetTokenInformation(tok, 3, b, len, out len)) return false;
        uint cnt = *(uint*)b;
        var arr = (LUID_AND_ATTRIBUTES*)(b + 4);
        for (int i = 0; i < cnt; i++) if (arr[i].Luid.LowPart == luid.LowPart && arr[i].Luid.HighPart == luid.HighPart) return (arr[i].Attributes & 2) != 0;
        return false;
    }

    // ---------------- Process info (read-only) ----------------
    public static void ProcessInfo()
    {
        Console.WriteLine("=== PROCESS (self, read-only) ===");
        nint h = N.GetCurrentProcess();
        Console.WriteLine($"PriorityClass=0x{N.GetPriorityClass(h):X}");
        uint io = 0;
        int st = N.NtQueryInformationProcess(h, 33, &io, 4, out _);
        Console.WriteLine($"NtQueryInformationProcess(ProcessIoPriority=33) st=0x{st:X8} io={io}");
        PROCESS_POWER_THROTTLING_STATE s = new() { Version = 1 };
        bool ok = N.GetProcessInformation(h, 4, &s, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
        Console.WriteLine($"GetProcessInformation(ProcessPowerThrottling) ok={ok} err={(ok ? 0 : Marshal.GetLastPInvokeError())} Control=0x{s.ControlMask:X} State=0x{s.StateMask:X}");
        uint pagePrio = 0;
        ok = N.GetProcessInformation(h, 0, &pagePrio, 4);
        Console.WriteLine($"GetProcessInformation(ProcessMemoryPriority) ok={ok} value={pagePrio}");
        // Explorer as example of other process
        foreach (var p in Process.GetProcessesByName("explorer").Take(1))
        {
            Console.WriteLine($"explorer pid={p.Id} prio={p.PriorityClass} path={p.MainModule?.FileName}");
        }
    }
}

[StructLayout(LayoutKind.Explicit)]
struct PDH_FMT_COUNTERVALUE
{
    [FieldOffset(0)] public uint CStatus;
    [FieldOffset(8)] public int longValue;
    [FieldOffset(8)] public double doubleValue;
    [FieldOffset(8)] public long largeValue;
}
unsafe struct PDH_FMT_COUNTERVALUE_ITEM_W { public char* szName; public PDH_FMT_COUNTERVALUE FmtValue; }
struct PROCESSOR_POWER_INFORMATION { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }
struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION { public long IdleTime, KernelTime, UserTime, DpcTime, InterruptTime; public uint InterruptCount; }
struct SYSTEM_INTERRUPT_INFORMATION { public uint ContextSwitches, DpcCount, DpcRate, TimeIncrement, DpcBypassCount, ApcBypassCount; }
struct DEVPROPKEY { public Guid fmtid; public uint pid; }
struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }
struct LUID { public uint LowPart; public int HighPart; }
struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }
struct TOKEN_PRIVILEGES1 { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

static partial class Nv
{
    [LibraryImport("nvml.dll")] public static partial int nvmlInit_v2();
    [LibraryImport("nvml.dll")] public static partial int nvmlShutdown();
    [LibraryImport("nvml.dll")] public static partial int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);
    [LibraryImport("nvml.dll")] public static partial int nvmlDeviceGetTemperature(nint device, int sensor, out uint temp);
}

static unsafe partial class N
{
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint PdhOpenQueryW(string? src, nint user, out nint query);
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint PdhAddEnglishCounterW(nint q, string path, nint user, out nint counter);
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint PdhAddCounterW(nint q, string path, nint user, out nint counter);
    [LibraryImport("pdh.dll")] public static partial uint PdhCollectQueryData(nint q);
    [LibraryImport("pdh.dll")] public static partial uint PdhGetFormattedCounterArrayW(nint c, uint fmt, ref uint bufSize, out uint count, void* items);
    [LibraryImport("pdh.dll")] public static partial uint PdhCloseQuery(nint q);
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint PdhLookupPerfNameByIndexW(string? machine, uint idx, char* buf, ref uint size);

    [LibraryImport("ntdll.dll")] public static partial int NtQueryTimerResolution(out uint min, out uint max, out uint cur);
    [LibraryImport("kernel32.dll")] public static partial void Sleep(uint ms);

    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetLogicalProcessorInformationEx(int rel, byte* buf, ref uint len);
    [LibraryImport("powrprof.dll")] public static partial int CallNtPowerInformation(int level, void* inBuf, uint inLen, void* outBuf, uint outLen);
    [LibraryImport("ntdll.dll")] public static partial int NtQuerySystemInformation(int cls, void* buf, uint len, out uint retLen);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint CM_Get_Device_ID_List_SizeW(out uint len, string? filter, uint flags);
    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint CM_Get_Device_ID_ListW(string? filter, char* buf, uint len, uint flags);
    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_DevNode_PropertyW(uint devInst, DEVPROPKEY* key, out uint type, byte* buf, ref uint size, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_Device_IDW(uint devInst, char* buf, uint len, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_First_Log_Conf(out nuint logConf, uint devInst, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_Next_Res_Des(out nuint resDes, nuint current, uint forResource, out uint resourceId, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_Res_Des_Data_Size(out uint size, nuint resDes, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Get_Res_Des_Data(nuint resDes, byte* buf, uint size, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Free_Res_Des_Handle(nuint resDes);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Free_Log_Conf_Handle(nuint logConf);

    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool K32EnumDeviceDrivers(nint* bases, uint cb, out uint needed);
    [LibraryImport("kernel32.dll")] public static partial uint K32GetDeviceDriverBaseNameW(nint imageBase, char* name, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetSystemCpuSetInformation(byte* info, uint len, out uint retLen, nint process, uint flags);

    [LibraryImport("kernel32.dll")] public static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll")] public static partial uint GetPriorityClass(nint h);
    [LibraryImport("ntdll.dll")] public static partial int NtQueryInformationProcess(nint h, int cls, void* info, uint len, out uint ret);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetProcessInformation(nint h, int cls, void* info, uint size);

    [LibraryImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool OpenProcessToken(nint process, uint access, out nint token);
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool LookupPrivilegeValueW(string? system, string name, out LUID luid);
    [LibraryImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, TOKEN_PRIVILEGES1* newState, uint bufLen, void* prev, uint* retLen);
    [LibraryImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool GetTokenInformation(nint token, int cls, void* info, uint len, out uint retLen);
    [LibraryImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool CloseHandle(nint h);
    [LibraryImport("kernel32.dll", SetLastError = true)] public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static partial bool QueryFullProcessImageNameW(nint h, uint flags, char* buf, ref uint size);
}
