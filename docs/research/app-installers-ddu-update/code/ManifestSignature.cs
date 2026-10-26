using System.Security.Cryptography;

namespace Hateeze.Update;

/// <summary>
/// (Опционально, но рекомендуется) подпись latest.json собственным ключом ECDSA P-256.
/// Защищает от подмены релиза при компрометации GitHub-аккаунта/зеркала: SHA-256 в latest.json
/// сам по себе защищает только от порчи при передаче.
/// Публикация: latest.json + latest.json.sig (base64 подписи DER/IEEE P1363 — см. формат ниже).
/// Генерация ключа (один раз, приватный ключ хранить вне репозитория, в секретах CI):
///   using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
///   File.WriteAllText("update-signing.pem", k.ExportPkcs8PrivateKeyPem());
///   File.WriteAllText("update-signing.pub.pem", k.ExportSubjectPublicKeyInfoPem());
/// </summary>
public static class ManifestSignature
{
    // Вставить содержимое update-signing.pub.pem
    private const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
REPLACE_WITH_REAL_KEY
-----END PUBLIC KEY-----
""";

    public static bool Verify(byte[] latestJsonBytes, string signatureBase64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(PublicKeyPem);
        byte[] sig = Convert.FromBase64String(signatureBase64.Trim());
        return ecdsa.VerifyData(latestJsonBytes, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Для CI: подписать файл (privatePem — из секрета).</summary>
    public static string Sign(byte[] latestJsonBytes, string privatePem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privatePem);
        return Convert.ToBase64String(ecdsa.SignData(latestJsonBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
