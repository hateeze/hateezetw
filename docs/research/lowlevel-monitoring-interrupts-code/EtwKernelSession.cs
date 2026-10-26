// Raw ETW (advapi32) kernel DPC/ISR session WITHOUT TraceEvent.
// In this probe only the QUERY path (ListSessions) is executed. Start()/Run() are compiled but NOT run here.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hateeze.Latency;

[StructLayout(LayoutKind.Sequential)]
public struct WNODE_HEADER
{
    public uint BufferSize, ProviderId;
    public ulong HistoricalContext;
    public long TimeStamp;          // union with KernelHandle
    public Guid Guid;
    public uint ClientContext;      // 1 = QPC, 2 = SystemTime, 3 = CPU cycles
    public uint Flags;              // WNODE_FLAG_TRACED_GUID = 0x00020000
}

[StructLayout(LayoutKind.Sequential)]
public struct EVENT_TRACE_PROPERTIES
{
    public WNODE_HEADER Wnode;
    public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
    public int AgeLimit;
    public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
    public nint LoggerThreadId;
    public uint LogFileNameOffset, LoggerNameOffset;
}

// Only the fields we need, at verified x64 offsets. Total size 448.
[StructLayout(LayoutKind.Explicit, Size = 448)]
public unsafe struct EVENT_TRACE_LOGFILEW
{
    [FieldOffset(0)] public char* LogFileName;
    [FieldOffset(8)] public char* LoggerName;
    [FieldOffset(16)] public long CurrentTime;
    [FieldOffset(24)] public uint BuffersRead;
    [FieldOffset(28)] public uint ProcessTraceMode;          // union LogFileMode
    // [32..119] EVENT_TRACE CurrentEvent; [120..399] TRACE_LOGFILE_HEADER LogfileHeader (280 bytes)
    [FieldOffset(120 + 256)] public long LogfileHeader_PerfFreq;
    [FieldOffset(400)] public nint BufferCallback;
    [FieldOffset(408)] public uint BufferSize;
    [FieldOffset(412)] public uint Filled;
    [FieldOffset(416)] public uint EventsLost;
    [FieldOffset(424)] public delegate* unmanaged<EVENT_RECORD*, void> EventRecordCallback;
    [FieldOffset(432)] public uint IsKernelTrace;
    [FieldOffset(440)] public nint Context;
}

[StructLayout(LayoutKind.Sequential)]
public struct EVENT_DESCRIPTOR { public ushort Id; public byte Version, Channel, Level, Opcode; public ushort Task; public ulong Keyword; }

[StructLayout(LayoutKind.Sequential)]
public struct EVENT_HEADER
{
    public ushort Size, HeaderType, Flags, EventProperty;
    public uint ThreadId, ProcessId;
    public long TimeStamp;           // raw QPC when PROCESS_TRACE_MODE_RAW_TIMESTAMP + ClientContext=1
    public Guid ProviderId;          // for classic kernel events: the event CLASS guid (PerfInfo, Image, DiskIo...)
    public EVENT_DESCRIPTOR EventDescriptor; // Opcode = classic EventType (66/67/68/69...)
    public ulong ProcessorTime;
    public Guid ActivityId;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EVENT_RECORD
{
    public EVENT_HEADER EventHeader;
    public byte ProcessorNumber, Alignment; public ushort LoggerId; // ETW_BUFFER_CONTEXT (ProcessorIndex if flag 0x200)
    public ushort ExtendedDataCount, UserDataLength;
    public nint ExtendedData;
    public byte* UserData;
    public nint UserContext;
}

public static unsafe partial class Etw
{
    public const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
    public const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
    public const uint EVENT_TRACE_SYSTEM_LOGGER_MODE = 0x02000000;
    public const uint EVENT_TRACE_INDEPENDENT_SESSION_MODE = 0x08000000;
    public const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
    public const uint PROCESS_TRACE_MODE_RAW_TIMESTAMP = 0x00001000;
    public const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    public const uint EVENT_TRACE_CONTROL_QUERY = 0, EVENT_TRACE_CONTROL_STOP = 1;
    public const int TraceSystemTraceEnableFlagsInfo = 4;
    public const ulong INVALID_PROCESSTRACE_HANDLE = ulong.MaxValue;
    // Kernel EnableFlags (EVENT_TRACE_FLAG_*)
    public const uint FLAG_PROCESS = 0x1, FLAG_THREAD = 0x2, FLAG_IMAGE_LOAD = 0x4, FLAG_DISK_IO = 0x100, FLAG_DISK_IO_INIT = 0x400,
                      FLAG_MEMORY_HARD_FAULTS = 0x2000, FLAG_DPC = 0x20, FLAG_INTERRUPT = 0x40;
    // Classic event-class GUIDs (EventHeader.ProviderId for kernel events)
    public static readonly Guid PerfInfoGuid = new("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc"); // DPC 66/68/69, ISR 50/67
    public static readonly Guid ImageGuid = new("2cb15d1d-5fc1-11d2-abe1-00a0c911f518");    // Load 10, DCStart 3
    public static readonly Guid DiskIoGuid = new("3d6fa8d4-fe05-11d0-9dda-00c04fd7ba7c");   // Read 10, Write 11
    public static readonly Guid PageFaultGuid = new("3d6fa8d3-fe05-11d0-9dda-00c04fd7ba7c"); // HardFault 32

    [LibraryImport("advapi32.dll", EntryPoint = "StartTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint StartTrace(out ulong sessionHandle, string sessionName, EVENT_TRACE_PROPERTIES* props);
    [LibraryImport("advapi32.dll", EntryPoint = "ControlTraceW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ControlTrace(ulong sessionHandle, string? sessionName, EVENT_TRACE_PROPERTIES* props, uint controlCode);
    [LibraryImport("advapi32.dll", EntryPoint = "QueryAllTracesW")]
    public static partial uint QueryAllTraces(EVENT_TRACE_PROPERTIES** propertyArray, uint count, out uint loggerCount);
    [LibraryImport("advapi32.dll")]
    public static partial uint TraceSetInformation(ulong sessionHandle, int infoClass, void* info, uint length);
    [LibraryImport("advapi32.dll", EntryPoint = "OpenTraceW")]
    public static partial ulong OpenTrace(EVENT_TRACE_LOGFILEW* logfile);
    [LibraryImport("advapi32.dll")]
    public static partial uint ProcessTrace(ulong* handles, uint count, nint startTime, nint endTime);
    [LibraryImport("advapi32.dll")]
    public static partial uint CloseTrace(ulong handle);

    public const int MaxName = 1024; // chars
    public static int PropsSize => sizeof(EVENT_TRACE_PROPERTIES) + 2 * MaxName * sizeof(char);

    public static EVENT_TRACE_PROPERTIES* AllocProps()
    {
        var p = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)PropsSize);
        p->Wnode.BufferSize = (uint)PropsSize;
        p->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
        p->LogFileNameOffset = (uint)(sizeof(EVENT_TRACE_PROPERTIES) + MaxName * sizeof(char));
        return p;
    }

    /// <summary>Read-only: list all ETW sessions (max 64) with mode flags.</summary>
    public static List<(string name, uint mode, uint enableFlags, uint lost)> ListSessions()
    {
        const int N = 64;
        var arr = stackalloc EVENT_TRACE_PROPERTIES*[N];
        for (int i = 0; i < N; i++) arr[i] = AllocProps();
        var res = new List<(string, uint, uint, uint)>();
        try
        {
            uint err = QueryAllTraces(arr, N, out uint count);
            if (err != 0) throw new System.ComponentModel.Win32Exception((int)err);
            for (int i = 0; i < count; i++)
            {
                var name = new string((char*)((byte*)arr[i] + arr[i]->LoggerNameOffset));
                res.Add((name, arr[i]->LogFileMode, arr[i]->EnableFlags, arr[i]->EventsLost));
            }
        }
        finally { for (int i = 0; i < N; i++) NativeMemory.Free(arr[i]); }
        return res;
    }

    /// <summary>Stops a session by name (used to clean up an orphan from a previous crash). NOT executed in probe.</summary>
    public static uint StopSession(string name)
    {
        var p = AllocProps();
        try { return ControlTrace(0, name, p, EVENT_TRACE_CONTROL_STOP); }
        finally { NativeMemory.Free(p); }
    }
}

/// <summary>
/// Zero-allocation DPC/ISR aggregator. NOT executed in the probe (starting a session is a system-wide action).
/// </summary>
public static unsafe class KernelLatencySession
{
    public const string SessionName = "hateeze-latency"; // NOT "NT Kernel Logger"
    static readonly Guid SessionGuid = new("6b7f5f0e-3c1a-4f7e-9d61-5a2e0c9b7d11"); // any fixed GUID != SystemTraceControlGuid

    // Aggregation: driver index -> stats. Fixed arrays, written only by the ProcessTrace thread.
    public static ulong[] DriverBase = Array.Empty<ulong>();   // sorted ascending
    public static ulong[] DriverEnd = Array.Empty<ulong>();
    public static long[] DpcCount = Array.Empty<long>(), IsrCount = Array.Empty<long>();
    public static long[] DpcTotalTicks = Array.Empty<long>(), IsrTotalTicks = Array.Empty<long>();
    public static long[] DpcMaxTicks = Array.Empty<long>(), IsrMaxTicks = Array.Empty<long>();
    // Histogram buckets (us): <1,<2,<4,<8,<16,<32,<64,<128,<256,<512,<1024,<2048,>=2048
    public static long[] DpcHist = new long[13], IsrHist = new long[13];
    static long s_qpcFreq;
    static ulong s_traceHandle;

    public static uint Start(uint extraFlags = 0)
    {
        Etw.StopSession(SessionName); // kill orphan from previous crash (ignore result)
        var p = Etw.AllocProps();
        try
        {
            p->Wnode.Flags = Etw.WNODE_FLAG_TRACED_GUID;
            p->Wnode.ClientContext = 1;                    // QPC timestamps
            p->Wnode.Guid = SessionGuid;
            p->LogFileMode = Etw.EVENT_TRACE_REAL_TIME_MODE | Etw.EVENT_TRACE_SYSTEM_LOGGER_MODE | Etw.EVENT_TRACE_INDEPENDENT_SESSION_MODE;
            p->BufferSize = 64;                            // KB per buffer
            p->MinimumBuffers = (uint)Math.Max(64, Environment.ProcessorCount * 4);
            p->MaximumBuffers = p->MinimumBuffers * 2;
            p->FlushTimer = 1;                             // seconds
            p->EnableFlags = 0;                            // set below via TraceSetInformation (как TraceEvent)
            uint err = Etw.StartTrace(out ulong session, SessionName, p);
            if (err != 0) return err;                      // 5 = ACCESS_DENIED, 183 = ALREADY_EXISTS, 1450 = NO_SYSTEM_RESOURCES
            ulong flags = Etw.FLAG_DPC | Etw.FLAG_INTERRUPT | extraFlags;
            err = Etw.TraceSetInformation(session, Etw.TraceSystemTraceEnableFlagsInfo, &flags, sizeof(ulong));
            if (err != 0) { Etw.StopSession(SessionName); return err; }
            return 0;
        }
        finally { NativeMemory.Free(p); }
    }

    /// <summary>Blocks in ProcessTrace until Stop(). Call on a dedicated thread.</summary>
    public static uint Run()
    {
        fixed (char* name = SessionName)
        {
            EVENT_TRACE_LOGFILEW lf = default;
            lf.LoggerName = name;
            lf.ProcessTraceMode = Etw.PROCESS_TRACE_MODE_REAL_TIME | Etw.PROCESS_TRACE_MODE_EVENT_RECORD | Etw.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
            lf.EventRecordCallback = &OnEvent;
            ulong h = Etw.OpenTrace(&lf);
            if (h == Etw.INVALID_PROCESSTRACE_HANDLE) return (uint)Marshal.GetLastPInvokeError();
            s_traceHandle = h;
            s_qpcFreq = lf.LogfileHeader_PerfFreq != 0 ? lf.LogfileHeader_PerfFreq : System.Diagnostics.Stopwatch.Frequency;
            return Etw.ProcessTrace(&h, 1, 0, 0);       // returns when session stops or CloseTrace
        }
    }

    public static void Stop()
    {
        Etw.StopSession(SessionName);
        if (s_traceHandle != 0) Etw.CloseTrace(s_traceHandle);
    }

    [UnmanagedCallersOnly]
    static void OnEvent(EVENT_RECORD* r)
    {
        ref var h = ref r->EventHeader;
        if (h.ProviderId != Etw.PerfInfoGuid || r->UserDataLength < 16) return;
        byte op = h.EventDescriptor.Opcode;
        bool isDpc = op is 66 or 68 or 69; bool isIsr = op is 67 or 50;
        if (!isDpc && !isIsr) return;
        long initial = *(long*)r->UserData;            // InitialTime (QPC)
        ulong routine = *(ulong*)(r->UserData + 8);    // Routine (kernel VA)
        long ticks = h.TimeStamp - initial;            // raw QPC (needs RAW_TIMESTAMP!)
        if (ticks < 0) return;
        int idx = FindDriver(routine);
        long us = ticks * 1_000_000 / s_qpcFreq;
        int b = us <= 0 ? 0 : Math.Min(12, 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)us));
        if (isDpc)
        {
            DpcHist[b]++;
            if (idx >= 0) { DpcCount[idx]++; DpcTotalTicks[idx] += ticks; if (ticks > DpcMaxTicks[idx]) DpcMaxTicks[idx] = ticks; }
        }
        else
        {
            IsrHist[b]++;
            if (idx >= 0) { IsrCount[idx]++; IsrTotalTicks[idx] += ticks; if (ticks > IsrMaxTicks[idx]) IsrMaxTicks[idx] = ticks; }
            // byte vector = r->UserData[17]; uint msgIndex = UserDataLength >= 24 ? *(uint*)(r->UserData + 20) : 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int FindDriver(ulong addr)
    {
        var bases = DriverBase; int lo = 0, hi = bases.Length - 1, found = -1;
        while (lo <= hi) { int mid = (lo + hi) >>> 1; if (bases[mid] <= addr) { found = mid; lo = mid + 1; } else hi = mid - 1; }
        return found >= 0 && addr < DriverEnd[found] ? found : -1;
    }
}
