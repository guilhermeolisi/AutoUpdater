# AutoUpdater

Self-update mechanism for .NET CLI applications. Distribute new versions through
Azure Blob Storage (or any HTTPS host); the client app verifies SHA-256 +
Ed25519 signatures before installing atomically.

- **Cross-platform runtime**: Windows, Linux, macOS
- **Free**: no certificate authority, no paid signing
- **Tamper-resistant**: even a fully compromised storage account cannot deliver
  a valid update package without the offline private key
- **Atomic**: failed installs roll back to the previous state

---

## Architecture

```
┌──────────────────────┐        references        ┌─────────────────────┐
│ YourApp.exe          │ ───────────────────────▶ │ AutoUpdaterHelp.dll │
│ (host CLI app)       │                          │ (the library)       │
└──────────┬───────────┘                          └──────────┬──────────┘
           │                                                 │
           │  AutoUpdater.HasNewVersion(url)                 │
           │  AutoUpdater.Update(ver, url, email)            │
           │                                                 │
           │  Process.Start(AutoUpdaterConsole.exe args[7])  │
           ▼                                                 │
┌─────────────────────────────────────────────┐              │
│ AutoUpdaterConsole.exe                      │              │
│ (sits in YourApp\AutoUpdater\)              │              │
│                                             │              │
│   1. Wait for YourApp.exe (PID) to exit     │              │
│   2. Download manifest from Azure Blob      │◀─────────────┘
│   3. Download zip artifact                  │
│   4. SHA-256 + Ed25519 verify               │
│   5. Atomic install (backup + replace)      │
│   6. Relaunch YourApp.exe                   │
└─────────────────────────────────────────────┘
```

### Solution layout

```
AutoUpdater.sln
├── src/
│   ├── AutoUpdateModel/        # shared: Manifest, Verifier, Logger, OS detection
│   ├── AutoUpdateHelp/         # library referenced by host apps (AutoUpdaterLitte.cs)
│   └── AutoUpdaterConsole/     # the external updater executable
└── tools/
    └── AutoUpdaterReleaseTool/ # CLI to sign packages and publish manifests
```

---

## Quick start

### 1) One-time setup (developer machine)

Generate the Ed25519 key pair and embed the public key in the build.

```powershell
cd tools\AutoUpdaterReleaseTool\bin\Debug\net10.0
.\AutoUpdaterReleaseTool.exe init
```

The output prints a constant — paste it into `src/AutoUpdateModel/PublicKey.cs`,
replacing the placeholder, then rebuild:

```csharp
public const string Ed25519PublicKeyBase64 = "APnG000mWi3rLt+I1h/TyuSMm3R/N9qmR1whUIN7X00=";
```

Back up the private key (DPAPI is tied to your Windows user — reformatting the
machine without backup loses it):

```powershell
.\AutoUpdaterReleaseTool.exe export-private --out C:\offline-backup\autoupdater.key
```

Store this file in a password manager attachment, encrypted USB, or 1Password
secure note. Without it, you cannot publish updates.

### 2) Cutting a release

For each supported OS, build a self-contained zip and sign it:

```powershell
# Windows build
.\AutoUpdaterReleaseTool.exe sign `
    --zip       MyApp-1.4.2-windows.zip `
    --version   1.4.2 `
    --os        windows `
    --url       "https://myapp.blob.core.windows.net/releases/MyApp-1.4.2-windows.zip" `
    --manifest  version.json

# Linux build
.\AutoUpdaterReleaseTool.exe sign `
    --zip       MyApp-1.4.2-linux.zip `
    --version   1.4.2 `
    --os        linux `
    --url       "https://myapp.blob.core.windows.net/releases/MyApp-1.4.2-linux.zip" `
    --manifest  version.json

# Optional: force clients below 1.0.0 to update
.\AutoUpdaterReleaseTool.exe sign ... --min-version 1.0.0
```

Then upload to Azure Blob Storage:
- `MyApp-1.4.2-windows.zip`
- `MyApp-1.4.2-linux.zip`
- `version.json`

The `version.json` URL is what your app calls `manifestUrl`.

### 3) Wiring the host app

Reference `AutoUpdaterHelp.dll` from your CLI app. The `AutoUpdater\` subfolder
of your install (containing `AutoUpdaterConsole.exe` and friends) is created
automatically by `VerifyUpdateOfAutoUpdater` on first run.

```csharp
using AutoUpdaterHelp;
using AutoUpdaterModel;

const string ManifestUrl =
    "https://myapp.blob.core.windows.net/releases/version.json";

const string AutoUpdaterManifestUrl =
    "https://myapp.blob.core.windows.net/releases/autoupdater-version.json";

// 1) Make sure the updater itself is present and current
string updErr = AutoUpdater.VerifyUpdateOfAutoUpdater(
    AutoUpdaterManifestUrl,
    progress => Console.Write($"\rFetching updater: {progress}%"));
if (updErr is not null)
    Console.Error.WriteLine($"AutoUpdater check skipped: {updErr}");

// 2) Check for a new app version
UpdateCheckResult check = AutoUpdater.HasNewVersion(
    ManifestUrl,
    TimeSpan.FromHours(24));

if (check.Message is not null)
{
    Console.Error.WriteLine($"Update check failed: {check.Message}");
}
else if (check.IsCurrentBelowMinimum)
{
    Console.WriteLine($"Your version ({check.CurrentVersion}) is below the " +
                      $"minimum supported ({check.MinimumVersion}). Update is required.");
    if (check.HasUpdate)
        ForceUpdate(check.NewVersion);
    else
        Environment.Exit(1);
}
else if (check.HasUpdate)
{
    Console.WriteLine($"New version {check.NewVersion} available. Updating...");
    string err = AutoUpdater.Update(check.NewVersion, ManifestUrl,
                                     emailToReportIssue: "support@myapp.com");
    if (err is null)
        Environment.Exit(0); // exit so the updater can replace files
    Console.Error.WriteLine(err);
}

// ... rest of the app ...

void ForceUpdate(Version v)
{
    AutoUpdater.Update(v, ManifestUrl, "support@myapp.com");
    Environment.Exit(0);
}
```

---

## Manifest format

The manifest is a JSON file uploaded next to the zips. Fields:

```json
{
  "version": "1.4.2",
  "minimumVersion": "1.0.0",
  "artifacts": {
    "windows": {
      "url":       "https://myapp.blob.core.windows.net/releases/MyApp-1.4.2-windows.zip",
      "sha256":    "4C5D9E30DEBDB65C526D8B0737B910869F7FDD6133977706B79989463339EE66",
      "signature": "7/CbSVnIcB6wkmTr7GlskJC7OD0VTjDuB4laqQJeeIrK407lpfWPbbl6lgTiOyZsClP2fFVqEJ3mAkdNf4WZBw=="
    },
    "linux":  { "url": "...", "sha256": "...", "signature": "..." },
    "macos":  { "url": "...", "sha256": "...", "signature": "..." }
  }
}
```

| Field | Required | Notes |
|---|---|---|
| `version` | yes | The new version. Compared with the running assembly's version using `System.Version`. |
| `minimumVersion` | no | If set, clients below this version are flagged via `result.IsCurrentBelowMinimum`. Useful to retire vulnerable versions. |
| `artifacts.<os>.url` | yes | HTTPS URL of the zip for that OS. |
| `artifacts.<os>.sha256` | yes | Hex SHA-256 of the zip bytes. |
| `artifacts.<os>.signature` | yes | Base64 Ed25519 signature of the zip bytes. |

Supported OS keys: `windows`, `linux`, `macos`.

---

## Security model

Three layers, each protecting against different attacks.

| Layer | Provides | Defeats |
|---|---|---|
| Azure Blob "Blob"-level anonymous read + HTTPS | Confidentiality, basic auth, transport integrity | Casual MITM, public listing, unauthenticated writes |
| **SHA-256** | Bit-level integrity | Corruption in transit, accidental modification |
| **Ed25519 signature** with offline private key | Authenticity | Compromise of the storage account, leaked SAS, rogue uploads, MITM with broken TLS |

**Key facts:**
- The **signature is over the zip bytes**, not over the manifest. An attacker
  who rewrites the manifest cannot produce a valid signature without your
  private key.
- The **private key never touches the production servers**. It lives DPAPI-encrypted
  in `%APPDATA%\AutoUpdater\private.bin` on your dev machine. Lose it and you
  can no longer publish updates (must rotate public key in clients).
- The **public key is embedded** as a constant in [PublicKey.cs](src/AutoUpdateModel/PublicKey.cs).
  No network lookup, no key server.
- If `PublicKey.cs` still contains the placeholder, **all verifications fail**
  with a clear error — fail-closed by design.

### What this does NOT protect against

- **Loss of the private key** through dev machine compromise. Mitigation: keep
  an offline backup; rotate periodically.
- **Pinned-old-version DoS**: an attacker can serve an old (still validly signed)
  manifest to keep the user on a vulnerable version. Mitigation: use
  `--min-version` to refuse to run below a baseline; rotate URLs / use SAS
  with expiry; monitor your Azure logs.
- **Code signing for Windows SmartScreen / macOS Gatekeeper**: this updater
  does not buy you OS-level trust. Apps with no Microsoft Authenticode signature
  will still trigger SmartScreen on first run. That requires a paid certificate
  (Sectigo / DigiCert / SSL.com) and is independent of the Ed25519 layer.

---

## AutoUpdaterReleaseTool reference

Build the tool once:

```bash
dotnet build tools/AutoUpdaterReleaseTool/AutoUpdaterReleaseTool.csproj -c Release
```

Then call commands:

```text
init [--force]
    Generate a new Ed25519 key pair. Stores the private key DPAPI-encrypted
    under %APPDATA%\AutoUpdater\private.bin. Prints the public key as a C#
    constant to paste into src/AutoUpdateModel/PublicKey.cs.

sign --zip <path> --version <ver> --os <windows|linux|macos>
     --url <download-url> [--manifest <path>] [--min-version <ver>]
    Compute SHA-256, sign with Ed25519, write/update the entry for that OS
    in the JSON manifest. Default manifest path: ./version.json.

verify --zip <path> --os <windows|linux|macos>
       [--manifest <path>] [--public-key <base64>]
    Sanity-check that a zip matches the manifest. Without --public-key,
    uses the embedded constant from AutoUpdateModel. Useful as a
    post-publish check (download what you just uploaded, run verify).

show-public
    Print the public key derived from the stored private key.
    Use this to re-extract the constant if you lose track of it.

export-private --out <path>
    Export the raw 32-byte private key (UNENCRYPTED) for offline backup.

import-private --in <path> [--force]
    Import a raw 32-byte Ed25519 private key and DPAPI-encrypt it locally.
    Use this to restore from backup on a new machine.
```

DPAPI key storage is **Windows-only**. On Linux/macOS, use
`export-private` (on Windows) → encrypt the file with `age` or `gpg` →
transfer → decrypt → `import-private`. (Linux/macOS support of DPAPI-equivalent
is on the backlog.)

---

## Library API reference (`AutoUpdaterHelp.AutoUpdater`)

```csharp
public static UpdateCheckResult HasNewVersion(string manifestUrl, TimeSpan frequency);
```
Downloads the manifest, compares the offered version to the running one, returns a
result struct. The `frequency` parameter throttles checks: subsequent calls
within the window return the cached "no update" without hitting the network
(except in DEBUG builds). State is stored in `LastVerificationVersion` next to
the entry assembly.

```csharp
public static string Update(Version verOnline, string manifestUrl, string emailToReportIssue);
```
Launches the external `AutoUpdaterConsole.exe` and returns immediately.
**The host app must exit shortly after** so the updater can replace its files
(it waits up to 30 s for the host PID to exit, then aborts to avoid file locks).

```csharp
public static string VerifyUpdateOfAutoUpdater(
    string manifestUrlAutoUpdater,
    Action<int> downloadNotifier);
```
Ensures the updater itself (`AutoUpdaterConsole.exe` and friends) is present
in the `AutoUpdater\` subfolder and at the version named in
`manifestUrlAutoUpdater`. Downloads + verifies + installs atomically.
Call this once at startup before `HasNewVersion` so the updater is ready
when needed.

### `UpdateCheckResult` fields

| Field | Type | Meaning |
|---|---|---|
| `Message` | `string` | Non-null = check failed (offline, manifest invalid, etc.). Other fields may be unset. |
| `CurrentVersion` | `Version` | Version of the running assembly. |
| `NewVersion` | `Version` | Newer version available, or null if up-to-date. |
| `MinimumVersion` | `Version` | Server-demanded floor, or null if not set. |
| `HasUpdate` | `bool` | Shortcut for `NewVersion is not null`. |
| `IsCurrentBelowMinimum` | `bool` | True if you should refuse to run / force update. |

---

## Troubleshooting

### Where are the logs?

`%LOCALAPPDATA%\<programName>\updater.log` on Windows,
`~/.local/share/<programName>/updater.log` on Linux/macOS.

The file contains UTC ISO-8601 timestamps and rotates to `.old` past 1 MB.
Both the host process (during `HasNewVersion`/`Update`) and the
`AutoUpdaterConsole.exe` write to the same file, so you get an end-to-end
view in one place.

### Common errors

| Message | Cause | Fix |
|---|---|---|
| `Ed25519 public key is not configured in this build` | `PublicKey.cs` still has the placeholder | Run `init`, paste the constant, rebuild |
| `SHA-256 mismatch` | Zip was corrupted in transit, or someone substituted it | Re-upload the zip; check Azure Blob versioning history |
| `Ed25519 signature verification failed` | Zip was signed with a different private key, or someone tampered with it | Re-sign with the correct private key |
| `Caller process (pid X) did not exit within 30s` | Host app hung after calling `Update()` | Make sure `Update()` is followed promptly by `Environment.Exit(0)` |
| `Manifest endpoint is not reachable` | Network/firewall, or wrong URL | Test the URL in a browser; check corporate proxy settings |
| `No private key found at ...\private.bin` | Tried to sign without running `init` | Run `init` (first time) or `import-private` (restoring backup) |

### Recovering from a botched update

The atomic installer writes `*.update.bak` files during install and removes
them on success. If a crash happens between backup and commit:
- Leftover `*.update.bak` files are cleaned up automatically on the next
  `AutoUpdaterConsole.exe` invocation
- The host app folder will contain a mix of new and old files (whichever side
  the crash happened on) — re-running the updater corrects this

If the install folder is unrecoverable, restore from your backup or re-install
the app from scratch and let it self-update on first run.

---

## Limitations / known issues

1. **Single private key per project.** Rotating the public key requires shipping
   a build with the new constant before any clients can receive updates signed
   with the new key. Plan for an orderly handover (sign with both keys during
   the transition, or accept a downtime window for clients to update).

2. **Atomic, but not crash-proof at the OS level.** A power loss between the
   backup and the commit step leaves `*.update.bak` files plus a mix of
   old/new files. The next run cleans these up, but the app may be broken
   between the crash and that next run. True crash-proofing would need
   journaling — overkill for a CLI updater.

3. **Same-volume assumption.** `File.Move` is atomic only on the same volume.
   The repository folder is created inside the install folder's
   `AutoUpdater\Repository\` subdirectory, so this normally holds.
   If you reconfigure the repository location across volumes, the move
   becomes a copy and atomicity is lost.

4. **Linux/macOS DPAPI gap.** The release tool's encrypted private key
   storage works only on Windows. Cross-platform release pipelines need to
   ship the key manually (encrypted) to the signing machine.

5. **No code signing for OS-level trust.** Apps shipped this way still
   trigger SmartScreen / Gatekeeper warnings on first run. Add a
   commercial code-signing certificate if you need a smooth first-run UX.

6. **No delta updates.** Each release ships the full zip. Fine up to a few
   tens of MB. For large apps, consider delta packages or splitting binaries
   from data.

---

## Contributing

The project lives under `src/` (production code) and `tools/` (signing
utility). The `BaseLibrary` reference is a sibling repository checked out at
`../BaseLibrary` — when working in a git worktree, create a junction
(`mklink /J ..\BaseLibrary <real-path>`) so the relative path resolves.

Run a full build with:

```bash
dotnet build AutoUpdater.sln
```

There is no automated test suite yet — see "P1 backlog" for plans.
