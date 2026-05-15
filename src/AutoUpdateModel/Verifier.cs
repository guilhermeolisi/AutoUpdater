using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using NSec.Cryptography;

[assembly: InternalsVisibleTo("AutoUpdater.Tests")]

namespace AutoUpdaterModel;

public static class Verifier
{
    /// <summary>
    /// Verifies a downloaded package against an expected SHA-256 hash and an
    /// Ed25519 signature produced with the matching private key. Uses the
    /// embedded <see cref="PublicKey"/> constant.
    /// </summary>
    /// <returns>Null on success, an error message otherwise.</returns>
    public static string? Verify(string filePath, string? expectedSha256Hex, string? signatureBase64)
    {
        if (!PublicKey.IsConfigured)
            return "Ed25519 public key is not configured in this build";
        return VerifyWithKey(filePath, expectedSha256Hex, signatureBase64, PublicKey.Bytes);
    }

    /// <summary>
    /// Same as <see cref="Verify"/> but accepts the public key bytes explicitly.
    /// Internal — used by tests that need to verify with a key generated at runtime.
    /// </summary>
    internal static string? VerifyWithKey(string filePath, string? expectedSha256Hex, string? signatureBase64, byte[] publicKeyBytes)
    {
        if (!File.Exists(filePath))
            return $"Package file not found: {filePath}";

        if (string.IsNullOrWhiteSpace(expectedSha256Hex))
            return "Expected SHA-256 is missing from the manifest";

        if (string.IsNullOrWhiteSpace(signatureBase64))
            return "Signature is missing from the manifest";

        if (publicKeyBytes is null || publicKeyBytes.Length == 0)
            return "Public key is empty";

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            return $"Could not read package file: {ex.Message}";
        }

        // 1) Integrity — SHA-256
        string computedHex = Convert.ToHexString(SHA256.HashData(fileBytes));
        if (!computedHex.Equals(expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            return $"SHA-256 mismatch. Expected {expectedSha256Hex}, got {computedHex}. " +
                   "Package was corrupted or tampered with.";

        // 2) Authenticity — Ed25519 signature
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return "Signature in manifest is not valid base64";
        }

        try
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            var pubKey = NSec.Cryptography.PublicKey.Import(algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            if (!algorithm.Verify(pubKey, fileBytes, signature))
                return "Ed25519 signature verification failed. Package is not from a trusted source.";
        }
        catch (Exception ex)
        {
            return $"Signature verification error: {ex.Message}";
        }

        return null;
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
