// SetupAPI device restart (DIF_PROPERTYCHANGE). In the probe ONLY the read-only parts run:
// SetupDiOpenDeviceInfoW + SetupDiGetDeviceInstallParamsW (validates struct sizes) and CM_Open_DevNode_Key(KEY_READ).
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Hateeze.Devices;

[StructLayout(LayoutKind.Sequential)]
public struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public nint Reserved; }

[StructLayout(LayoutKind.Sequential)]
public struct SP_CLASSINSTALL_HEADER { public uint cbSize; public uint InstallFunction; }

[StructLayout(LayoutKind.Sequential)]
public struct SP_PROPCHANGE_PARAMS { public SP_CLASSINSTALL_HEADER ClassInstallHeader; public uint StateChange, Scope, HwProfile; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public unsafe struct SP_DEVINSTALL_PARAMS_W
{
    public uint cbSize, Flags, FlagsEx;
    public nint hwndParent, InstallMsgHandler, InstallMsgHandlerContext, FileQueue, ClassInstallReserved;
    public uint Reserved;
    public fixed char DriverPath[260];
}

public static unsafe partial class DevRestart
{
    const uint DIF_PROPERTYCHANGE = 0x12, DICS_PROPCHANGE = 3, DICS_FLAG_GLOBAL = 1;
    public const uint DI_NEEDRESTART = 0x80, DI_NEEDREBOOT = 0x100;

    [LibraryImport("setupapi.dll", SetLastError = true)] public static partial nint SetupDiCreateDeviceInfoList(Guid* classGuid, nint hwnd);
    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)][return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiOpenDeviceInfoW(nint set, string instanceId, nint hwnd, uint flags, ref SP_DEVINFO_DATA data);
    [LibraryImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiSetClassInstallParamsW(nint set, ref SP_DEVINFO_DATA data, SP_PROPCHANGE_PARAMS* p, uint size);
    [LibraryImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiCallClassInstaller(uint dif, nint set, ref SP_DEVINFO_DATA data);
    [LibraryImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInstallParamsW(nint set, ref SP_DEVINFO_DATA data, SP_DEVINSTALL_PARAMS_W* p);
    [LibraryImport("setupapi.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint set);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
    [LibraryImport("cfgmgr32.dll")] public static partial uint CM_Open_DevNode_Key(uint devInst, uint samDesired, uint hwProfile, uint disposition, out nint hkey, uint flags);

    /// <summary>READ-ONLY: opens the device and reads install params (validates struct sizes).</summary>
    public static string ProbeInstallParams(string instanceId)
    {
        nint set = SetupDiCreateDeviceInfoList(null, 0);
        try
        {
            var di = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            if (!SetupDiOpenDeviceInfoW(set, instanceId, 0, 0, ref di)) return $"OpenDeviceInfo err {Marshal.GetLastPInvokeError()}";
            var ip = new SP_DEVINSTALL_PARAMS_W { cbSize = (uint)sizeof(SP_DEVINSTALL_PARAMS_W) };
            bool ok = SetupDiGetDeviceInstallParamsW(set, ref di, &ip);
            return $"sizeof(SP_DEVINFO_DATA)={sizeof(SP_DEVINFO_DATA)} sizeof(SP_DEVINSTALL_PARAMS_W)={sizeof(SP_DEVINSTALL_PARAMS_W)} GetInstallParams ok={ok} err={(ok ? 0 : Marshal.GetLastPInvokeError())} Flags=0x{ip.Flags:X} devInst={di.DevInst}";
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    /// <summary>READ-ONLY: reads Interrupt Management values via the device HARDWARE key (= "Device Parameters").</summary>
    public static string ReadMsiViaCm(string instanceId)
    {
        const uint KEY_READ = 0x20019, RegDisposition_OpenExisting = 1, CM_REGISTRY_HARDWARE = 0;
        if (CM_Locate_DevNodeW(out uint dn, instanceId, 0) != 0) return "locate failed";
        uint cr = CM_Open_DevNode_Key(dn, KEY_READ, 0, RegDisposition_OpenExisting, out nint hk, CM_REGISTRY_HARDWARE);
        if (cr != 0) return $"CM_Open_DevNode_Key cr=0x{cr:X}";
        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(hk, ownsHandle: true));
        using var msi = key.OpenSubKey(@"Interrupt Management\MessageSignaledInterruptProperties");
        using var aff = key.OpenSubKey(@"Interrupt Management\Affinity Policy");
        return $"hwkey={key.Name} MSISupported={msi?.GetValue("MSISupported") ?? "-"} MessageNumberLimit={msi?.GetValue("MessageNumberLimit") ?? "-"} DevicePolicy={aff?.GetValue("DevicePolicy") ?? "-"}";
    }

    /// <summary>
    /// NOT RUN IN PROBE. Restarts a device so new MSI/affinity settings apply. Returns: 0 = restarted,
    /// 1 = reboot required (DI_NEEDREBOOT/DI_NEEDRESTART), negative = Win32 error.
    /// </summary>
    public static int Restart(string instanceId)
    {
        nint set = SetupDiCreateDeviceInfoList(null, 0);
        if (set == -1) return -Marshal.GetLastPInvokeError();
        try
        {
            var di = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            if (!SetupDiOpenDeviceInfoW(set, instanceId, 0, 0, ref di)) return -Marshal.GetLastPInvokeError();
            var pc = new SP_PROPCHANGE_PARAMS
            {
                ClassInstallHeader = new SP_CLASSINSTALL_HEADER { cbSize = (uint)sizeof(SP_CLASSINSTALL_HEADER), InstallFunction = DIF_PROPERTYCHANGE },
                StateChange = DICS_PROPCHANGE, Scope = DICS_FLAG_GLOBAL, HwProfile = 0
            };
            if (!SetupDiSetClassInstallParamsW(set, ref di, &pc, (uint)sizeof(SP_PROPCHANGE_PARAMS))) return -Marshal.GetLastPInvokeError();
            if (!SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, set, ref di)) return -Marshal.GetLastPInvokeError();
            var ip = new SP_DEVINSTALL_PARAMS_W { cbSize = (uint)sizeof(SP_DEVINSTALL_PARAMS_W) };
            if (SetupDiGetDeviceInstallParamsW(set, ref di, &ip) && (ip.Flags & (DI_NEEDREBOOT | DI_NEEDRESTART)) != 0) return 1;
            return 0;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
