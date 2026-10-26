using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace Hateeze.Latency;

/// <summary>TraceEvent variant (compile-checked against TraceEvent 4.0.0, NOT executed).</summary>
public sealed class TeDpcIsrMonitor : IDisposable
{
    public const string SessionName = "hateeze-latency-te";
    readonly TraceEventSession _session;
    readonly Thread _thread;
    public long DpcCount, IsrCount, HardFaults;
    public double DpcMaxMs, IsrMaxMs, HardFaultMaxMs, DiskMaxMs;
    public ulong DpcMaxRoutine, IsrMaxRoutine;

    public TeDpcIsrMonitor()
    {
        TraceEventSession.SetDebugPrivilege(); // нужно для адресов модулей на 24H2 (наш процесс), ETW ему не требуется
        if (TraceEventSession.GetActiveSessionNames().Contains(SessionName))
            TraceEventSession.GetActiveSession(SessionName)?.Stop(noThrow: true); // сирота после падения
        _session = new TraceEventSession(SessionName) { StopOnDispose = true, BufferSizeMB = 64 };
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.DeferedProcedureCalls |
            KernelTraceEventParser.Keywords.Interrupt |
            KernelTraceEventParser.Keywords.MemoryHardFaults |
            KernelTraceEventParser.Keywords.DiskIO);
        var k = _session.Source.Kernel;
        k.PerfInfoDPC += OnDpc; k.PerfInfoTimerDPC += OnDpc; k.PerfInfoThreadedDPC += OnDpc;
        k.PerfInfoISR += OnIsr;
        k.MemoryHardFault += d => { HardFaults++; if (d.ElapsedTimeMSec > HardFaultMaxMs) HardFaultMaxMs = d.ElapsedTimeMSec; };
        k.DiskIORead += OnDisk; k.DiskIOWrite += OnDisk;
        _thread = new Thread(() => _session.Source.Process()) { IsBackground = true, Name = "ETW", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    void OnDpc(DPCTraceData d)
    {
        DpcCount++;
        double ms = d.ElapsedTimeMSec;
        if (ms > DpcMaxMs) { DpcMaxMs = ms; DpcMaxRoutine = d.Routine; }
        int cpu = d.ProcessorNumber; _ = cpu;
    }
    void OnIsr(ISRTraceData d)
    {
        IsrCount++;
        double ms = d.ElapsedTimeMSec;
        if (ms > IsrMaxMs) { IsrMaxMs = ms; IsrMaxRoutine = d.Routine; }
        int vector = d.Vector, msg = d.Message; _ = vector; _ = msg;
    }
    void OnDisk(DiskIOTraceData d) { if (d.ElapsedTimeMSec > DiskMaxMs) DiskMaxMs = d.ElapsedTimeMSec; }

    public int EventsLost => _session.EventsLost;

    public void Dispose()
    {
        _session.Stop(noThrow: true);
        _session.Dispose();
        _thread.Join(2000);
    }
}
