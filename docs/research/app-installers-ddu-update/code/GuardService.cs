using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hateeze.Ddu;

/// <summary>
/// Минимальная служба Windows без NuGet (System.ServiceProcess не входит в базовый .NET 10).
/// Запуск: "hateeze.exe --safeboot-guard" из SCM. Логика повторяет DDUSafeBootService.vb (DDU 18.1.6.x):
///   стартует ДО входа пользователя (в т.ч. в Safe Mode благодаря ключу SafeBoot\Minimal\&lt;name&gt; = "Service"),
///   удаляет safeboot из {current} (3 попытки), затем удаляет себя. Если удалить не удалось И мы в Safe Mode —
///   остаётся зарегистрированной, чтобы повторить на следующей загрузке.
/// </summary>
public static class GuardService
{
    private const int SERVICE_WIN32_OWN_PROCESS = 0x10;
    private const int SERVICE_STOPPED = 1, SERVICE_START_PENDING = 2, SERVICE_RUNNING = 4, SERVICE_STOP_PENDING = 3;
    private const int SERVICE_ACCEPT_STOP = 1, SERVICE_CONTROL_STOP = 1, SERVICE_CONTROL_INTERROGATE = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS { public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_TABLE_ENTRY { public string? lpServiceName; public IntPtr lpServiceProc; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ServiceMainFn(int argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int HandlerExFn(int control, int eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcherW([In] SERVICE_TABLE_ENTRY[] table);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerExW(string name, HandlerExFn handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr h, ref SERVICE_STATUS status);

    // держим делегаты в статических полях, чтобы GC не собрал их, пока ими пользуется SCM
    private static readonly ServiceMainFn s_main = ServiceMain;
    private static readonly HandlerExFn s_handler = Handler;
    private static IntPtr s_statusHandle;
    private static SERVICE_STATUS s_status;

    /// <summary>Вызывать из Main до инициализации UI: if (args.Contains("--safeboot-guard")) return GuardService.Run();</summary>
    public static int Run()
    {
        var table = new[]
        {
            new SERVICE_TABLE_ENTRY { lpServiceName = DduPaths.GuardServiceName, lpServiceProc = Marshal.GetFunctionPointerForDelegate(s_main) },
            new SERVICE_TABLE_ENTRY { lpServiceName = null, lpServiceProc = IntPtr.Zero },
        };
        return StartServiceCtrlDispatcherW(table) ? 0 : Marshal.GetLastWin32Error(); // 1063 = запущено не из SCM
    }

    private static void SetState(int state, int waitHintMs = 0)
    {
        s_status.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
        s_status.dwCurrentState = state;
        s_status.dwControlsAccepted = state == SERVICE_RUNNING ? SERVICE_ACCEPT_STOP : 0;
        s_status.dwWaitHint = waitHintMs;
        s_status.dwCheckPoint = state is SERVICE_START_PENDING or SERVICE_STOP_PENDING ? s_status.dwCheckPoint + 1 : 0;
        SetServiceStatus(s_statusHandle, ref s_status);
    }

    private static int Handler(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        if (control == SERVICE_CONTROL_STOP) { SetState(SERVICE_STOPPED); return 0; }
        if (control == SERVICE_CONTROL_INTERROGATE) return 0;
        return 120; // ERROR_CALL_NOT_IMPLEMENTED
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        s_statusHandle = RegisterServiceCtrlHandlerExW(DduPaths.GuardServiceName, s_handler, IntPtr.Zero);
        SetState(SERVICE_START_PENDING, 90_000);
        SetState(SERVICE_RUNNING);
        try
        {
            Thread.Sleep(2000);
            bool removed = SafeBoot.Disarm();          // bcdedit /deletevalue {current} safeboot ×3 с проверкой по кусту BCD
            Log(removed ? "safeboot removed" : "safeboot NOT removed", removed ? "INFO" : "ERROR");
            bool keep = !removed && SafeBoot.CurrentBootMode != 0;   // не смогли и мы в Safe Mode -> оставить службу на следующую загрузку
            if (!keep)
            {
                foreach (var mode in new[] { "Minimal", "Network" })
                    try { Registry.LocalMachine.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Control\SafeBoot\{mode}\{DduPaths.GuardServiceName}", false); } catch { }
                // sc delete на самом себе: SCM пометит службу на удаление и удалит после остановки
                SafeBoot.Run(DduPaths.Sc, "delete", DduPaths.GuardServiceName);
            }
        }
        catch (Exception ex) { Log(ex.ToString(), "ERROR"); }
        finally { SetState(SERVICE_STOPPED); }
    }

    private static void Log(string msg, string t)
    {
        try
        {
            string path = Path.Combine(DduPaths.Root, "guard.log");
            Directory.CreateDirectory(DduPaths.Root);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{t}] {msg}{Environment.NewLine}");
        }
        catch { /* логирование никогда не должно ронять службу */ }
    }
}
