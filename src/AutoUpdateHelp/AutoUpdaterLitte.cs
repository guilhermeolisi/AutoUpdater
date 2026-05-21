using AutoUpdaterModel;
using BaseLibrary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace AutoUpdaterHelp;

public static class AutoUpdater
{
    private static readonly string folderAutoUpdaterSufix = "AutoUpdater";
    private static readonly string[] autoUpdateExec = { "AutoUpdaterConsole" };

    /// <summary>
    /// Checks whether a newer version of the calling program is available online.
    /// </summary>
    /// <param name="manifestUrl">URL of the JSON version manifest (e.g. Azure Blob Storage URL).</param>
    /// <param name="frequency">Minimum interval between online checks.</param>
    public static UpdateCheckResult HasNewVersion(string manifestUrl, TimeSpan frequency)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return new UpdateCheckResult { Message = "manifestUrl is required" };

        int os = Services.CheckOS();
        if (os < 0)
            return new UpdateCheckResult { Message = "Unsupported operating system" };

        var program = Assembly.GetEntryAssembly();
        UpdaterLog.Init(program?.GetName().Name);
        Version verCurrent = program?.GetName().Version;

        if (!Connectivity.IsEndpointReachable(manifestUrl))
        {
            UpdaterLog.Warn($"HasNewVersion: manifest endpoint unreachable: {manifestUrl}");
            return new UpdateCheckResult
            {
                CurrentVersion = verCurrent,
                Message = "The manifest endpoint is not reachable. Check internet connection or firewall."
            };
        }

        string folderProgram = AppContext.BaseDirectory;
        if (!Directory.Exists(folderProgram))
            return new UpdateCheckResult
            {
                CurrentVersion = verCurrent,
                Message = "The folder of program was not found: " + folderProgram
            };

        string fileLastVerification = Path.Combine(folderProgram, "LastVerificationVersion");
        if (File.Exists(fileLastVerification))
        {
            DateTime lastCheck = ReadLastVerification(fileLastVerification);
            if ((DateTime.UtcNow - lastCheck) < frequency)
            {
#if !DEBUG
                return new UpdateCheckResult { CurrentVersion = verCurrent };
#endif
            }
        }

        try
        {
            File.WriteAllText(fileLastVerification, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write last-verification timestamp: {ex.Message}");
        }

        VersionManifest manifest;
        try
        {
            manifest = DownloadManifest(manifestUrl, folderProgram);
        }
        catch (Exception ex)
        {
            UpdaterLog.Error("HasNewVersion: failed to download/parse manifest", ex);
            return new UpdateCheckResult { CurrentVersion = verCurrent, Message = ex.Message };
        }

        if (!Version.TryParse(manifest.Version, out Version verOnline))
            return new UpdateCheckResult
            {
                CurrentVersion = verCurrent,
                Message = $"Manifest version is not in a valid format: '{manifest.Version}'"
            };

        Version verMinimum = null;
        if (!string.IsNullOrWhiteSpace(manifest.MinimumVersion))
        {
            if (!Version.TryParse(manifest.MinimumVersion, out verMinimum))
                UpdaterLog.Warn($"HasNewVersion: ignoring invalid minimumVersion '{manifest.MinimumVersion}'");
        }

        bool hasNewVersion = verOnline > verCurrent;
        UpdaterLog.Info($"HasNewVersion: current={verCurrent} online={verOnline} minimum={verMinimum?.ToString() ?? "none"} hasNew={hasNewVersion}");

        return new UpdateCheckResult
        {
            NewVersion = hasNewVersion ? verOnline : null,
            CurrentVersion = verCurrent,
            MinimumVersion = verMinimum
        };
    }

    private static DateTime ReadLastVerification(string filePath)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            if (DateTime.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return parsed.ToUniversalTime();
        }
        catch
        {
            // Ignore — fall back to file timestamp.
        }
        return File.GetLastWriteTimeUtc(filePath);
    }

    /// <summary>
    /// Ensures the AutoUpdater itself is present and up to date.
    /// </summary>
    /// <param name="manifestUrlAutoUpdater">URL of the AutoUpdater JSON manifest (e.g. Azure Blob Storage URL).</param>
    /// <param name="downloadNotifier">Optional callback receiving download progress percentage.</param>
    /// <returns>Error message, or null on success.</returns>
    public static string VerifyUpdateOfAutoUpdater(string manifestUrlAutoUpdater, Action<int> downloadNotifier)
    {
        if (string.IsNullOrWhiteSpace(manifestUrlAutoUpdater))
            return "manifestUrlAutoUpdater is required";

        int os = Services.CheckOS();
        if (os < 0)
            return "Unsupported operating system";

        var program = Assembly.GetEntryAssembly();
        UpdaterLog.Init(program?.GetName().Name);

        if (!Connectivity.IsEndpointReachable(manifestUrlAutoUpdater))
        {
            UpdaterLog.Warn($"VerifyUpdateOfAutoUpdater: manifest endpoint unreachable: {manifestUrlAutoUpdater}");
            return "The AutoUpdater manifest endpoint is not reachable. Check internet connection or firewall.";
        }

        var folderProgram = AppContext.BaseDirectory;
        var folderAutoUpdater = Path.Combine(folderProgram, folderAutoUpdaterSufix);

        VersionManifest manifest;
        try
        {
            manifest = DownloadManifest(manifestUrlAutoUpdater, folderProgram);
        }
        catch (Exception ex)
        {
            return "Could not get AutoUpdater manifest online: " + ex.Message;
        }

        if (!Version.TryParse(manifest.Version, out Version verOnlineUpdater))
            return $"AutoUpdater manifest version is not in a valid format: '{manifest.Version}'";

        string osKey = OsKey.Current();
        if (!manifest.Artifacts.TryGetValue(osKey, out ArtifactInfo artifact))
            return $"AutoUpdater manifest has no artifact for '{osKey}'";

        bool needUpdate;
        if (!Directory.Exists(folderAutoUpdater))
        {
            needUpdate = true;
        }
        else
        {
            string fileAutoUpdaterExec = GetAutoUpdaterExec(folderAutoUpdater, os);
            if (string.IsNullOrWhiteSpace(fileAutoUpdaterExec))
            {
                needUpdate = true;
            }
            else
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(fileAutoUpdaterExec);
                if (Version.TryParse(fvi.ProductVersion, out Version verCurrentUpdater))
                {
                    needUpdate = verOnlineUpdater > verCurrentUpdater;
                    if (needUpdate)
                        Directory.Delete(folderAutoUpdater, true);
                }
                else
                {
                    needUpdate = true;
                    Directory.Delete(folderAutoUpdater, true);
                }
            }
        }

        if (!needUpdate)
            return null;

        if (string.IsNullOrWhiteSpace(artifact.Url))
            return "no url to download AutoUpdater";

        Directory.CreateDirectory(folderAutoUpdater);
        string folderRepository = Path.Combine(folderAutoUpdater, "Repository");
        Directory.CreateDirectory(folderRepository);
        string fileNameDownloaded = Path.Combine(folderRepository, "autoupdater.zip");

        using (var client = new HttpClientDownloadWithProgress(artifact.Url, fileNameDownloaded))
        {
            client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
            {
                downloadNotifier?.Invoke(progressPercentage is not null ? (int)progressPercentage : 0);
            };
            // Aguarda a conclusão: sem o GetResult o using descartava o
            // HttpClient antes do download terminar, e o autoupdater.zip nunca
            // era gravado (verificação falhava com "Package file not found").
            client.StartDownload().GetAwaiter().GetResult();
        }

        string verifyError = Verifier.Verify(fileNameDownloaded, artifact.Sha256, artifact.Signature);
        if (!string.IsNullOrWhiteSpace(verifyError))
        {
            UpdaterLog.Error("AutoUpdater package verification failed: " + verifyError);
            try { File.Delete(fileNameDownloaded); } catch { }
            return "AutoUpdater package verification failed: " + verifyError;
        }

        UpdaterLog.Info("AutoUpdater package verification passed");

        // Updating the AutoUpdater itself — nothing inside folderAutoUpdater needs preserving.
        string installError = Services.ReplaceFiles(folderAutoUpdater, folderRepository, os, folderToPreserve: null);
        if (!string.IsNullOrWhiteSpace(installError))
            UpdaterLog.Error("AutoUpdater install failed: " + installError);
        else
            UpdaterLog.Info($"AutoUpdater updated to {verOnlineUpdater}");

        return installError;
    }

    /// <summary>
    /// Launches the external AutoUpdater process to update the calling program.
    /// The current process should exit immediately after this call so the updater
    /// can replace its files.
    /// </summary>
    /// <param name="verOnline">Version reported by the manifest.</param>
    /// <param name="manifestUrl">URL of the JSON version manifest.</param>
    /// <param name="emailToReportIssue">Optional email used to report errors.</param>
    /// <param name="relaunchCaller">
    /// When true (default) the updater relaunches the calling program after
    /// installing. CLI hosts (e.g. Sindarin) pass false: relaunching a
    /// command-line tool with no arguments makes no sense.
    /// </param>
    public static string Update(Version verOnline, string manifestUrl, string emailToReportIssue, bool relaunchCaller = true)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return "manifestUrl is required";

        int os = Services.CheckOS();
        if (os < 0)
            return "Unsupported operating system";

        var program = Assembly.GetEntryAssembly();
        var folderProgram = AppContext.BaseDirectory;
        var folderAutoUpdater = Path.Combine(folderProgram, folderAutoUpdaterSufix);

        string autoUpdaterName = autoUpdateExec[0];

        // No macOS o apphost não roda (framework-dependent, sem assinatura):
        // valida o .dll; nos demais SOs valida o apphost.
        if (os == 2)
        {
            if (!File.Exists(Services.AppDllPath(folderAutoUpdater, autoUpdaterName)))
                return "AutoUpdater assembly not found at: " + folderAutoUpdater;
        }
        else if (string.IsNullOrWhiteSpace(GetAutoUpdaterExec(folderAutoUpdater, os)))
        {
            return "AutoUpdater executable not found at: " + folderAutoUpdater;
        }

        // versionOld, versionNew, manifestUrl, folderToInstall, emailToReportIssue, nameProgram, callerPid
        string baseArgs = string.Format(
            CultureInfo.InvariantCulture,
            "\"{0}\" \"{1}\" \"{2}\" \"{3}\" \"{4}\" \"{5}\" \"{6}\"",
            program.GetName().Version.ToString(),
            verOnline.ToString(),
            manifestUrl,
            folderProgram,
            emailToReportIssue ?? string.Empty,
            program.GetName().Name,
            Process.GetCurrentProcess().Id);

        // Só anexa o 8º argumento quando relançar está desativado, mantendo
        // compatibilidade com chamadores/updaters antigos (que esperam 7 args).
        if (!relaunchCaller)
            baseArgs += " \"0\"";

        ProcessStartInfo processInfo = Services.BuildAppStartInfo(folderAutoUpdater, autoUpdaterName, os, baseArgs);
        processInfo.CreateNoWindow = false;

        // Loga o comando exato: em caso de incompatibilidade host/updater, o
        // updater rejeita os argumentos e este registro torna o diagnóstico óbvio.
        UpdaterLog.Info($"Update: starting \"{processInfo.FileName}\" {processInfo.Arguments}");

        try
        {
            Process.Start(processInfo);
            UpdaterLog.Info($"Update: launched AutoUpdaterConsole for {program.GetName().Name} {program.GetName().Version} -> {verOnline}");
        }
        catch (Exception ex)
        {
            UpdaterLog.Error("Failed to start AutoUpdater process", ex);
            return "Failed to start AutoUpdater process: " + ex.Message;
        }
        return null;
    }

    private static string GetAutoUpdaterExec(string folderAutoUpdater, int os)
    {
        for (int i = 0; i < autoUpdateExec.Length; i++)
        {
            string fileAutoUpdaterExec = os == 0
                ? Path.Combine(folderAutoUpdater, autoUpdateExec[i] + ".exe")
                : Path.Combine(folderAutoUpdater, autoUpdateExec[i]);
            if (File.Exists(fileAutoUpdaterExec))
                return fileAutoUpdaterExec;
        }
        return null;
    }

    private static VersionManifest DownloadManifest(string manifestUrl, string folderProgram)
    {
        string fileManifest = Path.Combine(folderProgram, "OnlineVersion.json");
        if (File.Exists(fileManifest))
            File.Delete(fileManifest);

        using (var client = new HttpClientDownloadWithProgress(manifestUrl, fileManifest))
        {
            client.StartDownload().GetAwaiter().GetResult();
        }

        if (!File.Exists(fileManifest))
            throw new Exception("No manifest file was downloaded");

        try
        {
            string json = File.ReadAllText(fileManifest);
            VersionManifest manifest = VersionManifest.Parse(json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Artifacts is null)
                throw new Exception("Manifest is missing required fields ('version', 'artifacts')");
            return manifest;
        }
        finally
        {
            try { File.Delete(fileManifest); } catch { }
        }
    }
}
