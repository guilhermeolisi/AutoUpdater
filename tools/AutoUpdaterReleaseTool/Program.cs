using AutoUpdaterModel;
using NSec.Cryptography;
using System.Security.Cryptography;

namespace AutoUpdaterReleaseTool;

internal static class Program
{
    private static readonly string KeyStoreFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoUpdater");

    private static readonly string KeyStoreFile = Path.Combine(KeyStoreFolder, "private.bin");

    private static IKeyVault Vault => KeyVaultFactory.Create(KeyStoreFile);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            string command = args[0].ToLowerInvariant();
            return command switch
            {
                "init"           => CmdInit(args),
                "sign"           => CmdSign(args),
                "verify"         => CmdVerify(args),
                "show-public"    => CmdShowPublic(),
                "export-private" => CmdExportPrivate(args),
                "import-private" => CmdImportPrivate(args),
                "-h" or "--help" => PrintUsageAndReturn(0),
                _                => PrintUsageAndReturn(1, $"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    // -------------- init --------------
    private static int CmdInit(string[] args)
    {
        bool force = HasFlag(args, "--force");

        if (File.Exists(KeyStoreFile) && !force)
        {
            Console.Error.WriteLine($"A key already exists at {KeyStoreFile}");
            Console.Error.WriteLine("Use --force to overwrite (PREVIOUS KEY WILL BE LOST — back it up first with 'export-private').");
            return 1;
        }

        Directory.CreateDirectory(KeyStoreFolder);

        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        byte[] privateRaw = key.Export(KeyBlobFormat.RawPrivateKey);
        byte[] publicRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        SaveProtectedKey(privateRaw);

        string publicBase64 = Convert.ToBase64String(publicRaw);

        Console.WriteLine();
        Console.WriteLine("Ed25519 key pair generated.");
        Console.WriteLine($"Private key stored: {Vault.Description}");
        Console.WriteLine();
        Console.WriteLine("Paste the following constant into:");
        Console.WriteLine("  src/AutoUpdateModel/PublicKey.cs");
        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine($"public const string Ed25519PublicKeyBase64 = \"{publicBase64}\";");
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine();
        Console.WriteLine("BACK UP THE PRIVATE KEY: run 'AutoUpdaterReleaseTool export-private --out backup.key'");
        Console.WriteLine("Without a backup, reformatting the machine will permanently lose the signing key.");
        return 0;
    }

    // -------------- sign --------------
    private static int CmdSign(string[] args)
    {
        string zipPath    = RequireOption(args, "--zip");
        string version    = RequireOption(args, "--version");
        string osKey      = RequireOption(args, "--os");
        string url        = RequireOption(args, "--url");
        string manifestPath = GetOption(args, "--manifest") ?? "version.json";
        string minVersion = GetOption(args, "--min-version");

        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Zip not found: {zipPath}");

        if (!IsValidOs(osKey))
            throw new ArgumentException($"--os must be one of: windows, linux, macos. Got: {osKey}");

        if (!Version.TryParse(version, out _))
            throw new ArgumentException($"--version is not a valid version: {version}");

        if (!string.IsNullOrWhiteSpace(minVersion) && !Version.TryParse(minVersion, out _))
            throw new ArgumentException($"--min-version is not a valid version: {minVersion}");

        byte[] privateRaw = LoadProtectedKey();

        byte[] zipBytes = File.ReadAllBytes(zipPath);
        string sha256 = Convert.ToHexString(SHA256.HashData(zipBytes));

        var algorithm = SignatureAlgorithm.Ed25519;
        byte[] signature;
        using (var key = Key.Import(algorithm, privateRaw, KeyBlobFormat.RawPrivateKey))
        {
            signature = algorithm.Sign(key, zipBytes);
        }
        string signatureBase64 = Convert.ToBase64String(signature);

        VersionManifest manifest = LoadOrCreateManifest(manifestPath);
        manifest.Version = version;
        if (!string.IsNullOrWhiteSpace(minVersion))
            manifest.MinimumVersion = minVersion;
        manifest.Artifacts[osKey] = new ArtifactInfo
        {
            Url = url,
            Sha256 = sha256,
            Signature = signatureBase64
        };

        File.WriteAllText(manifestPath, manifest.Serialize());

        Console.WriteLine($"Signed {Path.GetFileName(zipPath)} ({zipBytes.Length:N0} bytes)");
        Console.WriteLine($"  os:         {osKey}");
        Console.WriteLine($"  version:    {version}");
        if (!string.IsNullOrWhiteSpace(minVersion))
            Console.WriteLine($"  minVersion: {minVersion}");
        Console.WriteLine($"  sha256:     {sha256}");
        Console.WriteLine($"  signature:  {signatureBase64}");
        Console.WriteLine($"  manifest:   {Path.GetFullPath(manifestPath)}");

        Array.Clear(privateRaw);
        return 0;
    }

    // -------------- verify --------------
    private static int CmdVerify(string[] args)
    {
        string zipPath      = RequireOption(args, "--zip");
        string osKey        = RequireOption(args, "--os");
        string manifestPath = GetOption(args, "--manifest") ?? "version.json";
        string publicKeyB64 = GetOption(args, "--public-key");

        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Zip not found: {zipPath}");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: {manifestPath}");
        if (!IsValidOs(osKey))
            throw new ArgumentException($"--os must be one of: windows, linux, macos. Got: {osKey}");

        VersionManifest manifest = VersionManifest.Parse(File.ReadAllText(manifestPath));
        if (manifest?.Artifacts is null || !manifest.Artifacts.TryGetValue(osKey, out ArtifactInfo info))
            throw new Exception($"Manifest has no entry for OS '{osKey}'");

        // Reuse Verifier when public key is configured in PublicKey.cs;
        // otherwise allow an explicit --public-key override for ad-hoc checks.
        if (!string.IsNullOrWhiteSpace(publicKeyB64))
        {
            byte[] zipBytes = File.ReadAllBytes(zipPath);
            string computed = Convert.ToHexString(SHA256.HashData(zipBytes));
            if (!computed.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"FAIL: SHA-256 mismatch (expected {info.Sha256}, got {computed})");
                return 1;
            }
            byte[] sig = Convert.FromBase64String(info.Signature);
            byte[] pub = Convert.FromBase64String(publicKeyB64);
            var algorithm = SignatureAlgorithm.Ed25519;
            var pk = NSec.Cryptography.PublicKey.Import(algorithm, pub, KeyBlobFormat.RawPublicKey);
            if (!algorithm.Verify(pk, zipBytes, sig))
            {
                Console.Error.WriteLine("FAIL: Ed25519 signature did not verify");
                return 1;
            }
        }
        else
        {
            string err = Verifier.Verify(zipPath, info.Sha256, info.Signature);
            if (err is not null)
            {
                Console.Error.WriteLine("FAIL: " + err);
                return 1;
            }
        }

        Console.WriteLine("OK — package matches manifest");
        return 0;
    }

    // -------------- show-public --------------
    private static int CmdShowPublic()
    {
        byte[] privateRaw = LoadProtectedKey();
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algorithm, privateRaw, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        byte[] publicRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        Console.WriteLine($"public const string Ed25519PublicKeyBase64 = \"{Convert.ToBase64String(publicRaw)}\";");
        Array.Clear(privateRaw);
        return 0;
    }

    // -------------- export-private --------------
    private static int CmdExportPrivate(string[] args)
    {
        string outPath = RequireOption(args, "--out");
        byte[] privateRaw = LoadProtectedKey();
        File.WriteAllBytes(outPath, privateRaw);
        Array.Clear(privateRaw);

        Console.WriteLine($"Private key exported (RAW, NOT ENCRYPTED) to: {outPath}");
        Console.WriteLine("Treat this file as a SECRET. Store offline (e.g. password-manager attachment, encrypted USB).");
        return 0;
    }

    // -------------- import-private --------------
    private static int CmdImportPrivate(string[] args)
    {
        string inPath = RequireOption(args, "--in");
        bool force = HasFlag(args, "--force");

        if (File.Exists(KeyStoreFile) && !force)
        {
            Console.Error.WriteLine($"A key already exists at {KeyStoreFile}. Use --force to overwrite.");
            return 1;
        }

        byte[] privateRaw = File.ReadAllBytes(inPath);
        if (privateRaw.Length != 32)
            throw new Exception($"Expected 32-byte raw Ed25519 private key, got {privateRaw.Length} bytes");

        Directory.CreateDirectory(KeyStoreFolder);
        SaveProtectedKey(privateRaw);
        Array.Clear(privateRaw);

        Console.WriteLine($"Private key imported: {Vault.Description}");
        return 0;
    }

    // -------------- helpers --------------
    private static void SaveProtectedKey(byte[] plain) => Vault.Save(plain);

    private static byte[] LoadProtectedKey()
    {
        if (!File.Exists(KeyStoreFile))
            throw new Exception($"No private key found at {KeyStoreFile}. Run 'AutoUpdaterReleaseTool init' first.");
        return Vault.Load();
    }

    private static bool IsValidOs(string osKey) =>
        osKey is OsKey.Windows or OsKey.Linux or OsKey.MacOS;

    private static VersionManifest LoadOrCreateManifest(string path)
    {
        if (!File.Exists(path))
        {
            return new VersionManifest
            {
                Version = "0.0.0",
                Artifacts = new Dictionary<string, ArtifactInfo>()
            };
        }
        return VersionManifest.Parse(File.ReadAllText(path));
    }

    private static string RequireOption(string[] args, string name)
    {
        string value = GetOption(args, name);
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException($"Missing required option: {name}");
        return value;
    }

    private static string GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static int PrintUsageAndReturn(int code, string message = null)
    {
        if (!string.IsNullOrEmpty(message))
            Console.Error.WriteLine(message);
        PrintUsage();
        return code;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
@"AutoUpdaterReleaseTool — Ed25519 signing tool for AutoUpdater packages.

USAGE:
  AutoUpdaterReleaseTool <command> [options]

COMMANDS:
  init [--force]
      Generate a new Ed25519 key pair, store the private key DPAPI-encrypted
      under %APPDATA%\AutoUpdater\private.bin, and print the public key as a
      C# constant to paste into src/AutoUpdateModel/PublicKey.cs.

  sign --zip <path> --version <ver> --os <windows|linux|macos>
       --url <download-url> [--manifest <path>] [--min-version <ver>]
      Compute SHA-256 of the zip, sign with Ed25519, and add/update the entry
      for the given OS in the JSON manifest (default: version.json).
      --min-version sets the manifest's minimumVersion field (clients running
      below this version will be flagged as needing a mandatory update).

  verify --zip <path> --os <windows|linux|macos>
         [--manifest <path>] [--public-key <base64>]
      Sanity-check that the zip matches the manifest entry. Without --public-key,
      uses the embedded PublicKey.cs constant from AutoUpdaterModel.

  show-public
      Print the public key (as a C# constant) derived from the stored private key.

  export-private --out <path>
      Export the raw private key (32 bytes, UNENCRYPTED) for external backup.

  import-private --in <path> [--force]
      Import a raw 32-byte Ed25519 private key and DPAPI-encrypt it locally.

KEY STORAGE:
  - Windows: DPAPI-encrypted, tied to your Windows user account.
  - Linux/macOS: PBKDF2 (SHA-256, 600k iter) + AES-256-GCM with a passphrase.
    Set AUTOUPDATER_PASSPHRASE in the environment (preferred for CI),
    or you will be prompted interactively.
  - The signing tool is a release-time utility; it never ships with the app.
");
    }
}
