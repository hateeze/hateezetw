using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Hateeze.Security;

/// <summary>
/// Проверка подписи Authenticode скачанного установщика (WinVerifyTrust) + сверка CN подписанта с ожидаемым.
/// Ожидаемые CN (сняты Get-AuthenticodeSignature с установленных бинарников на машине разработчика 2026-10-26):
///   Discord Inc. | Valve Corp. | Riot Games, Inc. | ESL FACEIT Group Ltd. | Skowsand Tecnologia Ltda (ExitLag) |
///   Telegram FZ-LLC | OBS Project, LLC | Google LLC | YANDEX LLC | Microsoft Corporation | Spotify AB |
///   NVIDIA Corporation | Wagnardsoft (DDU, сертификат Sectigo, действует 2026-05-15..2027-08-13).
/// 7-Zip НЕ подписан — для него только SHA-256 (digest из GitHub API или winget-манифест).
/// </summary>
public static class Authenticode
{
    public sealed record Result(bool Valid, int WinTrustCode, string? SubjectCn, string? Thumbprint);

    public static Result Verify(string file)
    {
        int code = WinVerify(file);
        string? cn = null, thumb = null;
        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            cn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            thumb = cert.Thumbprint;
        }
        catch (Exception) { /* не подписан */ }
        return new Result(code == 0, code, cn, thumb);
    }

    public static bool IsTrustedFrom(string file, params string[] expectedCn)
    {
        var r = Verify(file);
        return r.Valid && r.SubjectCn is not null && expectedCn.Any(e => e.Equals(r.SubjectCn, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------- WinVerifyTrust ----------------
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData;
        public uint dwUIChoice; public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile;
        public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
        public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

    private static int WinVerify(string path)
    {
        var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFi = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fi, pFi, false);
            var wd = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,               // WTD_UI_NONE
                fdwRevocationChecks = 0,      // WTD_REVOKE_NONE (без сети к CRL: в РФ CRL/OCSP могут не открываться)
                dwUnionChoice = 1,            // WTD_CHOICE_FILE
                pFile = pFi,
                dwStateAction = 1,            // WTD_STATEACTION_VERIFY
                dwProvFlags = 0x00000080,     // WTD_REVOCATION_CHECK_NONE
            };
            int r = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wd);
            wd.dwStateAction = 2;             // WTD_STATEACTION_CLOSE
            WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, ref wd);
            return r;                         // 0 = OK; 0x800B0100 = TRUST_E_NOSIGNATURE; 0x80096010 = TRUST_E_BAD_DIGEST
        }
        finally { Marshal.FreeHGlobal(pFi); }
    }
}
