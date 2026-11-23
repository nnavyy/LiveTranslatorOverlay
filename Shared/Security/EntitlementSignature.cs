using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace LiveTranslatorOverlay.Core.Security;

public sealed record SignedEntitlement(
    string Subject,
    string ActiveTier,
    long VerifiedTotalCents,
    bool BetaAccessUnlocked,
    string HardwareFingerprintHash,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc,
    string SignatureBase64)
{
    public string ToCanonicalString() => string.Join("\n",
        "livetranslator.app:entitlement:v1",
        Subject.Trim().ToLowerInvariant(),
        ActiveTier.Trim().ToLowerInvariant(),
        VerifiedTotalCents.ToString(CultureInfo.InvariantCulture),
        BetaAccessUnlocked ? "1" : "0",
        HardwareFingerprintHash.Trim().ToLowerInvariant(),
        IssuedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        ExpiresUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}

public sealed record BinaryProvenance(string AssemblyName, string Version, string Sha256, string SourcePath);

/// <summary>
/// ECDSA P-256 (SHA-256) signature verification and binary provenance helpers for
/// tamper detection on entitlements and update manifests.
/// </summary>
public static class EntitlementSignature
{
    public static (string PublicKeyPem, string PrivateKeyPem) GenerateKeyPairPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfoPem(), ecdsa.ExportPkcs8PrivateKeyPem());
    }

    public static SignedEntitlement SignEntitlement(
        string subject,
        string activeTier,
        long verifiedTotalCents,
        bool betaAccessUnlocked,
        string hardwareFingerprintHash,
        DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc,
        string privateKeyPem)
    {
        var unsigned = new SignedEntitlement(
            subject,
            activeTier,
            verifiedTotalCents,
            betaAccessUnlocked,
            hardwareFingerprintHash,
            issuedUtc,
            expiresUtc,
            "");
        string sig = SignCanonicalPayload(unsigned.ToCanonicalString(), privateKeyPem);
        return unsigned with { SignatureBase64 = sig };
    }

    public static bool VerifyEntitlement(
        SignedEntitlement? token,
        string publicKeyPem,
        string expectedHardwareFingerprintHash,
        DateTimeOffset nowUtc)
    {
        if (token == null || string.IsNullOrWhiteSpace(publicKeyPem) || string.IsNullOrWhiteSpace(token.SignatureBase64))
            return false;

        if (!string.Equals(token.HardwareFingerprintHash, expectedHardwareFingerprintHash, StringComparison.OrdinalIgnoreCase))
            return false;

        if (token.ExpiresUtc <= nowUtc || token.IssuedUtc > nowUtc.AddMinutes(5))
            return false;

        return VerifyCanonicalPayload(token.ToCanonicalString(), token.SignatureBase64, publicKeyPem);
    }

    public static string SignCanonicalPayload(string canonicalPayload, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        byte[] data = Encoding.UTF8.GetBytes(canonicalPayload);
        byte[] sig = ecdsa.SignData(data, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(sig);
    }

    public static bool VerifyCanonicalPayload(string canonicalPayload, string signatureBase64, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(canonicalPayload) ||
            string.IsNullOrWhiteSpace(signatureBase64) ||
            string.IsNullOrWhiteSpace(publicKeyPem))
            return false;

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            byte[] data = Encoding.UTF8.GetBytes(canonicalPayload);
            byte[] sig = Convert.FromBase64String(signatureBase64);
            return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    public static string ComputeFileSha256(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "";
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static BinaryProvenance GetRunningBinaryProvenance()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(EntitlementSignature).Assembly;
        string name = asm.GetName().Name ?? "LiveTranslatorOverlay";
        string version = asm.GetName().Version?.ToString(3) ?? "1.1.1";
        string path = asm.Location;
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.ProcessPath ?? "";
        string sha = "";
        try { sha = ComputeFileSha256(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new BinaryProvenance(name, version, sha, path);
    }
}
