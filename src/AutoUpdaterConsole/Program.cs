// AutoUpdaterConsole

using AutoUpdaterModel;
using BaseLibrary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

CultureInfo culture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = culture;
Thread.CurrentThread.CurrentUICulture = culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

int os = Services.CheckOS();
if (os < 0)
{
    Console.Error.WriteLine("Unsupported operating system");
    return;
}

Version versionOld, versionNew;
string manifestUrl, folderToInstall, emailToReportIssue, nameProgram;
int callerPid;
bool isFirst = true;
string error;

error = Services.ProcessArg(args, out versionOld, out versionNew, out manifestUrl,
                            out folderToInstall, out emailToReportIssue, out nameProgram, out callerPid);

UpdaterLog.Init(nameProgram);
UpdaterLog.Info($"AutoUpdaterConsole started. args: oldVer={versionOld} newVer={versionNew} " +
                $"folder={folderToInstall} program={nameProgram} pid={callerPid}");

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

string folderRepository = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
folderRepository = Path.Combine(folderRepository, "Repository");
Directory.CreateDirectory(folderRepository);

ArtifactInfo artifact;
try
{
    artifact = DownloadAndParseManifest(manifestUrl, folderRepository, os);
}
catch (Exception ex)
{
    UpdaterLog.Error("Failed to obtain manifest", ex);
    ProcessError("Failed to obtain manifest: " + ex.Message);
    return;
}

if (string.IsNullOrWhiteSpace(artifact.Url))
{
    ProcessError($"Manifest has no download URL for OS '{OsKey.FromIndex(os)}'");
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

string folderUpdaterSelf = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
error = Services.ReplaceFiles(folderToInstall, folderRepository, os, folderUpdaterSelf);
if (!string.IsNullOrWhiteSpace(error))
{
    UpdaterLog.Error("ReplaceFiles failed: " + error);
    ProcessError(error);
    return;
}

UpdaterLog.Info($"Update to {versionNew} completed successfully");

if (callerPid > 0)
    RelaunchHostApp(folderToInstall, nameProgram, os);


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
    string exe = currentOs == 0
        ? Path.Combine(folder, name + ".exe")
        : Path.Combine(folder, name);

    if (!File.Exists(exe))
    {
        UpdaterLog.Warn($"Host app executable not found, skipping relaunch: {exe}");
        Console.WriteLine($"Note: host app executable not found at {exe}. Please launch it manually.");
        return;
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            WorkingDirectory = folder
        };
        Process.Start(psi);
        UpdaterLog.Info($"Relaunched host app: {exe}");
        Console.WriteLine($"Launched: {exe}");
    }
    catch (Exception ex)
    {
        UpdaterLog.Error($"Could not relaunch host app: {ex.Message}", ex);
        Console.WriteLine($"Note: could not relaunch host app ({ex.Message}). Please launch it manually.");
    }
}

ArtifactInfo DownloadAndParseManifest(string url, string repository, int currentOs)
{
    string manifestFile = Path.Combine(repository, "version.json");
    if (File.Exists(manifestFile))
        File.Delete(manifestFile);

    using (var client = new HttpClientDownloadWithProgress(url, manifestFile))
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

    string osKey = OsKey.FromIndex(currentOs);
    if (!manifest.Artifacts.TryGetValue(osKey, out ArtifactInfo info))
        throw new Exception($"Manifest has no entry for OS '{osKey}'");

    return info;
}

void DownloadNewVersion(string downloadFileUrl, string destination)
{
    Console.WriteLine("Downloading files...");
    isFirst = true;

    using var client = new HttpClientDownloadWithProgress(downloadFileUrl, destination);
    client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
    {
        if (isFirst)
        {
            isFirst = false;
            double sizeMb = (totalFileSize ?? 0) / (1024d * 1024d);
            Console.WriteLine($"Total size: {sizeMb:F2} MB");
            ConsoleUtility.WriteProgressBar(0);
        }
        downloadProgressChanged(progressPercentage is not null ? (int)progressPercentage : 0);
    };
    client.StartDownload();
    downloadFileCompleted();
}

void downloadFileCompleted() => Console.WriteLine(" Done");

void downloadProgressChanged(int progressPercentage)
    => ConsoleUtility.WriteProgressBar(progressPercentage, true);

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

    if (!string.IsNullOrWhiteSpace(emailToReportIssue))
        ExceptionMethods.SendException(emailToReportIssue, new Exception(message), false, null);
}
#endregion
