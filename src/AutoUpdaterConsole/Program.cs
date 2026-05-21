// AutoUpdaterConsole

using AutoUpdaterModel;
using BaseLibrary;
using System.Diagnostics;
using System.Globalization;

CultureInfo culture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = culture;
Thread.CurrentThread.CurrentUICulture = culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Preferimos a classe instanciável (IConsoleServices) à API estática
// ConsoleUtility: permite mock em testes de unidade.
IConsoleServices console = new ConsoleServices();

int os = Services.CheckOS();
if (os < 0)
{
    Console.Error.WriteLine("Unsupported operating system");
    return;
}

Version versionOld, versionNew;
string manifestUrl, folderToInstall, emailToReportIssue, nameProgram;
int callerPid;
bool relaunchCaller;
bool isFirst = true;
string error;

error = Services.ProcessArg(args, out versionOld, out versionNew, out manifestUrl,
                            out folderToInstall, out emailToReportIssue, out nameProgram,
                            out callerPid, out relaunchCaller);

// Compatibilidade com hosts legados: versões antigas do Sindarin embrulhavam
// o caminho de instalação terminado em '\', o que o CommandLineToArgvW do
// Windows corrompe (\" vira aspa escapada). Se o parse padrão falhar, recupera
// re-tokenizando a linha de comando crua de forma tolerante.
bool recoveredFromLegacy = false;
if (!string.IsNullOrWhiteSpace(error))
{
    string[] legacyArgs = Services.TokenizeArgsLenient(Environment.CommandLine);
    string legacyError = Services.ProcessArg(legacyArgs, out versionOld, out versionNew, out manifestUrl,
                                             out folderToInstall, out emailToReportIssue, out nameProgram,
                                             out callerPid, out relaunchCaller);
    if (string.IsNullOrWhiteSpace(legacyError))
    {
        recoveredFromLegacy = true;
        error = null;
    }
}

UpdaterLog.Init(nameProgram);
if (recoveredFromLegacy)
    UpdaterLog.Warn("Recovered arguments from a legacy/mangled command line (compatibility path).");
UpdaterLog.Info($"AutoUpdaterConsole started. args: oldVer={versionOld} newVer={versionNew} " +
                $"folder={folderToInstall} program={nameProgram} pid={callerPid} relaunch={relaunchCaller}");

if (!string.IsNullOrWhiteSpace(error))
{
    ProcessError(error);
    return;
}

if (!Connectivity.IsEndpointReachable(manifestUrl))
{
    ProcessError($"Manifest endpoint is not reachable: {manifestUrl}. Check internet connection or firewall.");
    return;
}

Console.WriteLine($"Updating {nameProgram} from {versionOld} to {versionNew}");

WaitForCallerExit(callerPid);

// AppContext.BaseDirectory funciona sob Native AOT/single-file
// (Assembly.Location retorna vazio nesses modos).
string folderUpdaterSelf = AppContext.BaseDirectory;
string folderRepository = Path.Combine(folderUpdaterSelf, "Repository");
Directory.CreateDirectory(folderRepository);

ArtifactInfo artifact;
try
{
    artifact = DownloadAndParseManifest(manifestUrl, folderRepository);
}
catch (Exception ex)
{
    UpdaterLog.Error("Failed to obtain manifest", ex);
    ProcessError("Failed to obtain manifest: " + ex.Message);
    return;
}

if (string.IsNullOrWhiteSpace(artifact.Url))
{
    ProcessError($"Manifest has no download URL for '{OsKey.Current()}'");
    return;
}

string fileNameDownloaded = Path.Combine(folderRepository, nameProgram + ".zip");
if (File.Exists(fileNameDownloaded))
    File.Delete(fileNameDownloaded);

UpdaterLog.Info($"Downloading artifact from {artifact.Url}");
DownloadNewVersion(artifact.Url, fileNameDownloaded);

Console.Write("Verifying package integrity and signature... ");
string verifyError = Verifier.Verify(fileNameDownloaded, artifact.Sha256, artifact.Signature);
if (!string.IsNullOrWhiteSpace(verifyError))
{
    Console.WriteLine();
    UpdaterLog.Error("Package verification failed: " + verifyError);
    try { File.Delete(fileNameDownloaded); } catch { }
    ProcessError("Package verification failed: " + verifyError);
    return;
}
Console.WriteLine("ok");
UpdaterLog.Info("Package verification passed");

error = Services.ReplaceFiles(folderToInstall, folderRepository, os, folderUpdaterSelf);
if (!string.IsNullOrWhiteSpace(error))
{
    UpdaterLog.Error("ReplaceFiles failed: " + error);
    ProcessError(error);
    return;
}

UpdaterLog.Info($"Update to {versionNew} completed successfully");

if (callerPid > 0 && relaunchCaller)
    RelaunchHostApp(folderToInstall, nameProgram, os);
else if (callerPid > 0)
    UpdaterLog.Info("Relaunch suppressed by caller (relaunch=0).");


#region Methods
void WaitForCallerExit(int pid)
{
    if (pid <= 0)
        return;

    try
    {
        Process caller = Process.GetProcessById(pid);
        Console.Write($"Waiting for caller process (pid {pid}) to exit... ");
        if (!caller.WaitForExit(30_000))
        {
            Console.WriteLine();
            UpdaterLog.Error($"Caller process (pid {pid}) did not exit within 30s. Aborting.");
            Console.WriteLine($"Caller process (pid {pid}) did not exit within 30s. Aborting to avoid file locks.");
            Environment.Exit(1);
        }
        Console.WriteLine("done");
    }
    catch (ArgumentException)
    {
        // Process already exited — nothing to wait for.
    }
    catch (Exception ex)
    {
        UpdaterLog.Warn($"Could not wait for caller process: {ex.Message}");
        Console.WriteLine($"Could not wait for caller process: {ex.Message}");
    }
}

void RelaunchHostApp(string folder, string name, int currentOs)
{
    // No macOS o apphost não roda (framework-dependent, sem assinatura):
    // o host é o .dll iniciado via "dotnet"; nos demais SOs é o apphost.
    string target = currentOs == 2
        ? Services.AppDllPath(folder, name)
        : currentOs == 0
            ? Path.Combine(folder, name + ".exe")
            : Path.Combine(folder, name);

    if (!File.Exists(target))
    {
        UpdaterLog.Warn($"Host app executable not found, skipping relaunch: {target}");
        Console.WriteLine($"Note: host app executable not found at {target}. Please launch it manually.");
        return;
    }

    try
    {
        ProcessStartInfo psi = Services.BuildAppStartInfo(folder, name, currentOs, null);
        psi.WorkingDirectory = folder;
        Process.Start(psi);
        UpdaterLog.Info($"Relaunched host app: {target}");
        Console.WriteLine($"Launched: {target}");
    }
    catch (Exception ex)
    {
        UpdaterLog.Error($"Could not relaunch host app: {ex.Message}", ex);
        Console.WriteLine($"Note: could not relaunch host app ({ex.Message}). Please launch it manually.");
    }
}

ArtifactInfo DownloadAndParseManifest(string url, string repository)
{
    string manifestFile = Path.Combine(repository, "version.json");
    if (File.Exists(manifestFile))
        File.Delete(manifestFile);

    using (var client = new FileDownloader(url, manifestFile))
    {
        client.StartDownload().GetAwaiter().GetResult();
    }

    if (!File.Exists(manifestFile))
        throw new Exception("Manifest was not downloaded");

    string json = File.ReadAllText(manifestFile);
    File.Delete(manifestFile);

    VersionManifest manifest = VersionManifest.Parse(json);
    if (manifest is null || manifest.Artifacts is null)
        throw new Exception("Manifest is invalid (missing 'artifacts')");

    string osKey = OsKey.Current();
    if (!manifest.Artifacts.TryGetValue(osKey, out ArtifactInfo info))
        throw new Exception($"Manifest has no entry for '{osKey}'");

    return info;
}

void DownloadNewVersion(string downloadFileUrl, string destination)
{
    Console.WriteLine("Downloading files...");
    isFirst = true;

    using var client = new FileDownloader(downloadFileUrl, destination);
    client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
    {
        if (isFirst)
        {
            isFirst = false;
            double sizeMb = (totalFileSize ?? 0) / (1024d * 1024d);
            Console.WriteLine($"Total size: {sizeMb:F2} MB");
            console.WriteProgressBar(0);
        }
        downloadProgressChanged(progressPercentage is not null ? (int)progressPercentage : 0);
    };
    // Aguarda a conclusão: a verificação de assinatura roda logo em seguida e
    // não pode ver um arquivo parcial.
    client.StartDownload().GetAwaiter().GetResult();
    downloadFileCompleted();
}

void downloadFileCompleted() => Console.WriteLine(" Done");

void downloadProgressChanged(int progressPercentage)
    => console.WriteProgressBar(progressPercentage, update: true);

void ProcessError(string message)
{
    UpdaterLog.Error(message);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("An error was found, the installation/update will be aborted");
    Console.ResetColor();
    Console.WriteLine("Error message:");
    Console.WriteLine(message);
    if (!string.IsNullOrEmpty(UpdaterLog.LogPath))
        Console.WriteLine($"Full log: {UpdaterLog.LogPath}");

    // O envio de e-mail (BaseLibrary.Exception) foi removido: era incompatível
    // com Native AOT (reflexão/Assembly.Load) e já estava desativado no fluxo
    // Sindarin (ReportEmail vazio). Erros ficam registrados em UpdaterLog.
    if (!string.IsNullOrWhiteSpace(emailToReportIssue))
        UpdaterLog.Info($"Issue-report email '{emailToReportIssue}' is configured, but email reporting is disabled in this build. See the log file.");
}
#endregion
