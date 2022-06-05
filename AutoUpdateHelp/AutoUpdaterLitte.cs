using AutoUpdaterModel;
using BaseLibrary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AutoUpdaterHelp;

public static class AutoUpdater
{
    //https://drive.google.com/uc?export=download&id=FILEID
    public static (Version newVersion, string message, string urlToDownload) HasNewVersion(string urlVersion, TimeSpan Frequency)
    {

        string message = null;
        //Verifica conexão com internet
        if (!HTTPMethods.IsConnectedToInternetPing())
        {
            message = "The computer don't have acess to internet to verify new version";
            return ReturnWhenError(message);
        }
#if DEBUG
        var trash = Assembly.GetExecutingAssembly();
        var trash2 = Assembly.GetCallingAssembly();
        var trash3 = Assembly.GetEntryAssembly();
#endif
        //windows, linux, macos
        int os = Services.CheckOS();

        var program = Assembly.GetEntryAssembly();
        string folderProgram = Path.GetDirectoryName(program.Location);
        if (!Directory.Exists(folderProgram))
        {
            message = "The folder of program was not found: " + folderProgram;
            return ReturnWhenError(message);
        }
        string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string fileLastVerification = Path.Combine(folderProgram, "LastVerificationVersion");
        //folder = Path.Combine(folder, "Version");
        if (File.Exists(fileLastVerification))
        {
            DateTime dateTime = File.GetCreationTime(fileLastVerification);
            if ((DateTime.Now - dateTime) < Frequency)
            {
#if !DEBUG
                return ReturnWhenError(null);
#endif
            }
            File.Delete(fileLastVerification);

        }
        try
        {
            File.WriteAllText(fileLastVerification, "");
        }
        catch (Exception ex)
        {

        }

        Version verOnline;
        string urlToDownload;
        try
        {
            GetVersionOnline(urlVersion, os, folderProgram, out verOnline, out urlToDownload);
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return ReturnWhenError(message);
        }

        Version verCurret = program.GetName().Version;

        bool hasNewVersion = verOnline > verCurret;

        return (hasNewVersion ? verOnline : null, message, urlToDownload);

    }
    private static (Version newVersion, string error, string urlToDownload) ReturnWhenError(string error)
    {
        return (null, error, null);
    }
    //https://drive.google.com/file/d/1Qb2hT8HaCavf5sIu8gs03qNgVm5oflV-/view?usp=sharing
    static readonly string urlVersionAutoUpdater = "https://drive.google.com/uc?export=download&id=1Qb2hT8HaCavf5sIu8gs03qNgVm5oflV-";
    static readonly string folderAutoUpdaterSufix = "AutoUpdater";
    static readonly string[] autoUpdateExec = { "AutoUpdaterConsole", "AutoUpdaterGUI" };
    /// <summary>
    /// 
    /// </summary>
    /// <returns>error message</returns>
    public static string VerifyUpdateOfAutoUpdater(Action<int> downloadNotifier)
    {
        //windows, linux, macos
        int os = Services.CheckOS();

        var program = Assembly.GetEntryAssembly();
        var folderProgram = Path.GetDirectoryName(program.Location);
        var folderAutoUpdater = Path.Combine(folderProgram, folderAutoUpdaterSufix);
        bool needUpdate = false;

        string urlToDownloadUpdater = null;
        string error;
        Version verOnlineUpdater = null;
        Version verCurrentUpdater = null;
        if (!Directory.Exists(folderAutoUpdater))
        {
            needUpdate = true;
        }
        else
        {
            //Verifica a versão online
            GetVersionOnline(urlVersionAutoUpdater, os, folderAutoUpdater, out verOnlineUpdater, out urlToDownloadUpdater);
            string fileAutoUpdaterExec = GetAutoUpdaterExec(folderAutoUpdater, os);

            if (!string.IsNullOrWhiteSpace(fileAutoUpdaterExec))
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(fileAutoUpdaterExec);
                string verTemp = fvi.ProductVersion.ToString();
                verCurrentUpdater = new(verTemp);
                if (verCurrentUpdater is not null && verOnlineUpdater is not null)
                {
                    needUpdate = verOnlineUpdater > verCurrentUpdater;
                    if (needUpdate)
                    {
                        Directory.Delete(folderAutoUpdater, true);
                    }
                }
            }
        }
        if (!needUpdate || string.IsNullOrWhiteSpace(urlToDownloadUpdater))
        {
            if (!needUpdate)
                return null;
            error = "no url to download AutoUpdater";
            return error;
        }

        Directory.CreateDirectory(folderAutoUpdater);
        string folderRepository = Path.Combine(folderAutoUpdater, "Repository");
        Directory.CreateDirectory(folderRepository);
        string fileNameDownloaded = Path.Combine(folderRepository, "autoupdater.zip");
        using (var client = new HttpClientDownloadWithProgress(urlToDownloadUpdater, fileNameDownloaded))
        {

            client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
            {
                if (downloadNotifier is not null)
                    downloadNotifier.Invoke(progressPercentage is not null ? (int)progressPercentage : 0);
            };
            client.StartDownload();
        }
        //Atualiza os arquivos
        error = Services.ReplaceFiles(folderAutoUpdater, folderRepository, os);

        return error;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="verOnline"></param>
    /// <param name="urlToUpdate"></param>
    /// <param name="emailToReportIssue"></param>
    /// <param name="downloadNotifier"></param>
    /// <returns>error message</returns>
    public static string Update(Version verOnline, string urlToUpdate, string emailToReportIssue, Action<int> downloadNotifier)
    {
        //windows, linux, macos
        int os = Services.CheckOS();

        var program = Assembly.GetEntryAssembly();
        var folderProgram = Path.GetDirectoryName(program.Location);
        var folderAutoUpdater = Path.Combine(folderProgram, folderAutoUpdaterSufix);

        string fileAutoUpdaterExec = GetAutoUpdaterExec(folderAutoUpdater, os);

        //executar o processo do instalador
        ProcessStartInfo processInfo = new ProcessStartInfo();
        processInfo.FileName = fileAutoUpdaterExec;
        processInfo.UseShellExecute = true;
        processInfo.CreateNoWindow = false;
        bool isQuiet = false;
        if (isQuiet)
        {
            processInfo.CreateNoWindow = true;
            //arg += " -quietUX";
        }
        // versionOld, versionNew, urlToDownload, folderToInstall, emailToReportIssue, nameProgram
        processInfo.Arguments = string.Format("\"{0}\" \"{1}\" \"{2}\" \"{3}\" \"{4}\" \"{5}\"", verOnline.ToString(), program.GetName().Version.ToString(), urlToUpdate, folderProgram, emailToReportIssue, program.GetName().Name);

        Process.Start(processInfo);
        return null;
    }
    private static string GetAutoUpdaterExec(string folderAutoUpdater, int os)
    {
        string fileAutoUpdaterExec = null;
        for (int i = 0; i < autoUpdateExec.Length; i++)
        {
            fileAutoUpdaterExec = os == 0 ? Path.Combine(folderAutoUpdater, autoUpdateExec[i] + ".exe") : Path.Combine(folderAutoUpdater, autoUpdateExec[i]);
            if (File.Exists(fileAutoUpdaterExec))
            {
                break;
            }
            fileAutoUpdaterExec = null;
        }
        return fileAutoUpdaterExec;
    }
    private static void GetVersionOnline(string urlVersion, int os, string folderProgram, out Version verOnline, out string urlToDownload)
    {
        string fileVersion = Path.Combine(folderProgram, "OnlineVersion");
        if (File.Exists(fileVersion))
            File.Delete(fileVersion);

        using (var client = new HttpClientDownloadWithProgress(urlVersion, fileVersion))
        {
            client.StartDownload().Wait();
        }
        verOnline = null;
        if (!File.Exists(fileVersion))
        {
            throw new Exception("No file version was downloaded");
        }

        string textOnline;
        using (StreamReader sr = new StreamReader(fileVersion))
        {
            textOnline = sr.ReadToEnd();
        }
        string[] lines = textOnline.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            throw new Exception("The file version downloaded is wrong format");
        }
        int countLine = 0;
        urlToDownload = null;
        foreach (var item in lines)
        {
            if (item.Length == 0 || (item[0] == '/' && item.Length > 1 && item[1] == '/'))
                continue;
            switch (countLine)
            {
                case 0:
                    verOnline = new(lines[countLine]);
                    break;
                default:
                    if (countLine != os + 1)
                    {
                        break;
                    }

                    if (HTTPMethods.IsValidURL(item))
                    {
                        urlToDownload = item;
                    }
                    else
                    {
                        throw new Exception("The url to download new version for " + (os == 0 ? "Windows" : os == 1 ? "Linux" : os == 2 ? "macOS" : "") + " is not in correct format");
                    }
                    break;
            }
            countLine++;
        }
        if (File.Exists(fileVersion))
            File.Delete(fileVersion);
    }
}