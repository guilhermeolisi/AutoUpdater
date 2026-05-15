using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AutoUpdaterReleaseTool;

/// <summary>
/// Stores the Ed25519 private key on disk encrypted at rest.
/// On Windows: DPAPI (no passphrase needed; tied to the Windows user).
/// On Linux/macOS: PBKDF2-SHA256 + AES-256-GCM with a passphrase, supplied via
/// the AUTOUPDATER_PASSPHRASE env var or an interactive prompt.
/// </summary>
internal interface IKeyVault
{
    void Save(byte[] privateKey);
    byte[] Load();
    string Description { get; }
}

internal static class KeyVaultFactory
{
    public static IKeyVault Create(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new DpapiVault(filePath);
        return new PassphraseVault(filePath);
    }
}

[SupportedOSPlatform("windows")]
internal sealed class DpapiVault : IKeyVault
{
    private readonly string _path;
    public DpapiVault(string path) => _path = path;
    public string Description => $"DPAPI-encrypted at {_path} (Windows user-bound)";

    public void Save(byte[] privateKey)
    {
        byte[] cipher = ProtectedData.Protect(privateKey, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, cipher);
    }

    public byte[] Load()
    {
        byte[] cipher = File.ReadAllBytes(_path);
        return ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}

internal sealed class PassphraseVault : IKeyVault
{
    // File layout (little-endian on disk):
    //   magic[4]       'AUV1' (AutoUpdater Vault v1)
    //   iterations[4]  uint32 — PBKDF2 iteration count
    //   salt[16]       random bytes
    //   nonce[12]      random bytes
    //   tag[16]        AES-GCM authentication tag
    //   ciphertext[N]  encrypted private key
    private static readonly byte[] Magic = "AUV1"u8.ToArray();
    private const int IterationCount = 600_000;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly string _path;

    public PassphraseVault(string path) => _path = path;

    public string Description => $"Passphrase-encrypted (PBKDF2 + AES-256-GCM) at {_path}";

    public void Save(byte[] privateKey)
    {
        string passphrase = GetPassphrase(prompt: "Set a passphrase to protect the private key: ", confirm: true);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLength);
        byte[] tag = new byte[TagLength];
        byte[] cipher = new byte[privateKey.Length];

        byte[] key = DeriveKey(passphrase, salt);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, privateKey, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        using var fs = File.Create(_path);
        fs.Write(Magic);
        fs.Write(BitConverter.GetBytes(IterationCount));
        fs.Write(salt);
        fs.Write(nonce);
        fs.Write(tag);
        fs.Write(cipher);
    }

    public byte[] Load()
    {
        byte[] data = File.ReadAllBytes(_path);
        if (data.Length < Magic.Length + 4 + SaltLength + NonceLength + TagLength)
            throw new CryptographicException("Vault file is truncated.");
        if (!data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new CryptographicException("Vault file has wrong magic header.");

        int offset = Magic.Length;
        int iterations = BitConverter.ToInt32(data, offset);              offset += 4;
        byte[] salt = data.AsSpan(offset, SaltLength).ToArray();          offset += SaltLength;
        byte[] nonce = data.AsSpan(offset, NonceLength).ToArray();        offset += NonceLength;
        byte[] tag = data.AsSpan(offset, TagLength).ToArray();            offset += TagLength;
        byte[] cipher = data.AsSpan(offset).ToArray();

        string passphrase = GetPassphrase(prompt: "Passphrase for the private key: ", confirm: false);
        byte[] key = DeriveKey(passphrase, salt, iterations);
        byte[] plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            try
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Invalid passphrase or corrupted vault.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return plain;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations = IterationCount)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(passphrase),
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    private static string GetPassphrase(string prompt, bool confirm)
    {
        string env = Environment.GetEnvironmentVariable("AUTOUPDATER_PASSPHRASE");
        if (!string.IsNullOrEmpty(env))
            return env;

        Console.Write(prompt);
        string entered = ReadMasked();
        if (string.IsNullOrEmpty(entered))
            throw new InvalidOperationException("Passphrase is required (set AUTOUPDATER_PASSPHRASE or type it).");

        if (confirm)
        {
            Console.Write("Confirm passphrase: ");
            string repeat = ReadMasked();
            if (entered != repeat)
                throw new InvalidOperationException("Passphrases do not match.");
        }
        return entered;
    }

    private static string ReadMasked()
    {
        // Best-effort masked input. Falls back to plain ReadLine when the
        // input is redirected (e.g. piped from a file).
        if (Console.IsInputRedirected)
        {
            string line = Console.ReadLine();
            return line ?? string.Empty;
        }

        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
            if (k.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
                continue;
            }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
    }
}
