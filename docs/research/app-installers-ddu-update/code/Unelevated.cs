using System.Runtime.InteropServices;

namespace Hateeze.Apps;

/// <summary>
/// Запуск процесса БЕЗ повышения прав из повышенного процесса — через Explorer рабочего стола
/// (Raymond Chen, "How can I launch an unelevated process from my elevated process and vice versa?", 2013).
/// Цепочка: ShellWindows.FindWindowSW(CSIDL_DESKTOP) -> IServiceProvider.QueryService(SID_STopLevelBrowser)
///          -> IShellBrowser.QueryActiveShellView -> IShellView.GetItemObject(SVGIO_BACKGROUND, IDispatch)
///          -> IShellFolderViewDual.Application -> IShellDispatch2.ShellExecute.
/// Процесс запускает explorer.exe => он получает НЕповышенный токен ВЛАДЕЛЬЦА рабочего стола (правильный пользователь).
/// Ограничения: нужен запущенный explorer.exe (не работает из службы/SYSTEM и в сеансе без оболочки);
/// PID/код возврата запущенного процесса НЕ возвращается — ожидание делаем по процессу/файлам/реестру.
/// </summary>
public static class Unelevated
{
    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private const int CSIDL_DESKTOP = 0, SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, SVGIO_BACKGROUND = 0;

    /// <summary>Получить объект Shell.Application процесса explorer (IShellDispatch2) как dynamic.</summary>
    public static dynamic GetDesktopShellApplication()
    {
        var t = Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!;
        var shellWindows = (IShellWindows)Activator.CreateInstance(t)!;
        object loc = CSIDL_DESKTOP, root = Type.Missing;
        Marshal.ThrowExceptionForHR(shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out var disp));
        if (disp is null) throw new InvalidOperationException("Рабочий стол Explorer не найден (explorer.exe не запущен?)");

        var sp = (IServiceProvider)disp;
        Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
        Marshal.ThrowExceptionForHR(sp.QueryService(ref sid, ref iid, out var pBrowser));
        var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
        Marshal.Release(pBrowser);

        Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out var view));
        Guid iidDisp = IID_IDispatch;
        Marshal.ThrowExceptionForHR(view.GetItemObject(SVGIO_BACKGROUND, ref iidDisp, out var pFolderView));
        dynamic folderView = Marshal.GetObjectForIUnknown(pFolderView);
        Marshal.Release(pFolderView);
        return folderView.Application;               // IShellDispatch2
    }

    /// <summary>ShellExecute без повышения. showCmd: 1 = SW_SHOWNORMAL, 0 = SW_HIDE.</summary>
    public static void Start(string file, string arguments, string? workingDir = null, int showCmd = 1)
    {
        dynamic shell = GetDesktopShellApplication();
        // Порядок параметров IShellDispatch2.ShellExecute: (File, vArgs, vDir, vOperation, vShow)
        shell.ShellExecute(file, arguments, workingDir ?? Path.GetDirectoryName(file) ?? "", "open", showCmd);
    }

    // ---------- COM-интерфейсы (порядок методов в vtable важен!) ----------

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        [PreserveSig] int get_Count(out int count);
        [PreserveSig] int Item(object index, [MarshalAs(UnmanagedType.IDispatch)] out object? folder);
        [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object ppunk);
        [PreserveSig] int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);
        [PreserveSig] int RegisterPending(int lThreadId, ref object pvarloc, ref object pvarlocRoot, int swClass, out int plCookie);
        [PreserveSig] int Revoke(int lCookie);
        [PreserveSig] int OnNavigate(int lCookie, ref object pvarLoc);
        [PreserveSig] int OnActivated(int lCookie, [MarshalAs(UnmanagedType.VariantBool)] bool fActive);
        [PreserveSig] int FindWindowSW(ref object pvarLoc, ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions,
                                       [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);
        [PreserveSig] int OnCreated(int lCookie, [MarshalAs(UnmanagedType.IUnknown)] object punk);
        [PreserveSig] int ProcessAttachDetach([MarshalAs(UnmanagedType.VariantBool)] bool fAttach);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        // IShellBrowser
        [PreserveSig] int InsertMenusSB(IntPtr a, IntPtr b);
        [PreserveSig] int SetMenuSB(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int RemoveMenusSB(IntPtr a);
        [PreserveSig] int SetStatusTextSB(IntPtr a);
        [PreserveSig] int EnableModelessSB(int a);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr a, short b);
        [PreserveSig] int BrowseObject(IntPtr a, uint b);
        [PreserveSig] int GetViewStateStream(uint a, out IntPtr b);
        [PreserveSig] int GetControlWindow(uint a, out IntPtr b);
        [PreserveSig] int SendControlMsg(uint a, uint b, IntPtr c, IntPtr d, out IntPtr e);
        [PreserveSig] int QueryActiveShellView(out IShellView ppshv);
        [PreserveSig] int OnViewWindowActive(IShellView a);
        [PreserveSig] int SetToolbarItems(IntPtr a, uint b, uint c);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
        // IShellView
        [PreserveSig] int TranslateAccelerator(IntPtr a);
        [PreserveSig] int EnableModeless(int a);
        [PreserveSig] int UIActivate(uint a);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(IntPtr a, IntPtr b, IntPtr c, IntPtr d, out IntPtr e);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(IntPtr a);
        [PreserveSig] int AddPropertySheetPages(uint a, IntPtr b, IntPtr c);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr a, uint b);
        [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
    }
}
