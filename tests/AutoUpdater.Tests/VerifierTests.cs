using System.Security.Cryptography;
using AutoUpdaterModel;
using NSec.Cryptography;
using Xunit;

namespace AutoUpdater.Tests;

public class VerifierTests : IDisposable
{
    private readonly string _tempFile;
    private readonly byte[] _publicKey;
    private readonly byte[] _payload;
    private readonly string _sha256;
    private readonly string _signatureBase64;

    public VerifierTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "verifier-" + Guid.NewGuid().ToString("N") + ".bin");
        _payload = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        File.WriteAllBytes(_tempFile, _payload);

        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        _publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        byte[] sig = algorithm.Sign(key, _payload);
        _signatureBase64 = Convert.ToBase64String(sig);
        _sha256 = Convert.ToHexString(SHA256.HashData(_payload));
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public void Valid_package_returns_null()
    {
        string err = Verifier.VerifyWithKey(_tempFile, _sha256, _signatureBase64, _publicKey);
        Assert.Null(err);
    }

    [Fact]
    public void Modified_payload_fails_sha256()
    {
        File.AppendAllText(_tempFile, "tampered");
        string err = Verifier.VerifyWithKey(_tempFile, _sha256, _signatureBase64, _publicKey);
        Assert.NotNull(err);
        Assert.Contains("SHA-256 mismatch", err);
    }

    [Fact]
    public void Wrong_sha256_in_manifest_fails()
    {
        string err = Verifier.VerifyWithKey(_tempFile, "00" + _sha256.Substring(2), _signatureBase64, _publicKey);
        Assert.NotNull(err);
        Assert.Contains("SHA-256 mismatch", err);
    }

    [Fact]
    public void Wrong_public_key_fails_signature()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var otherKey = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        byte[] otherPublic = otherKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        string err = Verifier.VerifyWithKey(_tempFile, _sha256, _signatureBase64, otherPublic);
        Assert.NotNull(err);
        Assert.Contains("signature verification failed", err);
    }

    [Fact]
    public void Tampered_signature_fails()
    {
        byte[] sig = Convert.FromBase64String(_signatureBase64);
        sig[0] ^= 0xFF;
        string tampered = Convert.ToBase64String(sig);

        string err = Verifier.VerifyWithKey(_tempFile, _sha256, tampered, _publicKey);
        Assert.NotNull(err);
        Assert.Contains("signature verification failed", err);
    }

    [Fact]
    public void Missing_file_returns_friendly_error()
    {
        string err = Verifier.VerifyWithKey(_tempFile + ".does-not-exist", _sha256, _signatureBase64, _publicKey);
        Assert.NotNull(err);
        Assert.Contains("not found", err);
    }

    [Fact]
    public void Empty_sha256_is_rejected()
    {
        string err = Verifier.VerifyWithKey(_tempFile, "", _signatureBase64, _publicKey);
        Assert.Contains("SHA-256 is missing", err);
    }

    [Fact]
    public void Empty_signature_is_rejected()
    {
        string err = Verifier.VerifyWithKey(_tempFile, _sha256, "", _publicKey);
        Assert.Contains("Signature is missing", err);
    }

    [Fact]
    public void Invalid_base64_signature_returns_friendly_error()
    {
        string err = Verifier.VerifyWithKey(_tempFile, _sha256, "!!!not-base64!!!", _publicKey);
        Assert.NotNull(err);
        Assert.Contains("base64", err);
    }

    [Fact]
    public void Public_method_fails_when_key_not_configured()
    {
        // The default PublicKey constant is the placeholder, so the public Verify
        // must return the configuration error before touching crypto.
        string err = Verifier.Verify(_tempFile, _sha256, _signatureBase64);
        Assert.Contains("not configured", err);
    }

    [Fact]
    public void ComputeSha256Hex_matches_explicit_hash()
    {
        string computed = Verifier.ComputeSha256Hex(_tempFile);
        Assert.Equal(_sha256, computed);
    }
}
