// See https://aka.ms/new-console-template for more information
//AutoUpdaterConsole


using AutoUpdaterModel;
using BaseLibrary;
using System.Globalization;
using System.Reflection;

CultureInfo culture = CultureInfo.InvariantCulture;
Thread.CurrentThread.CurrentCulture = culture;
Thread.CurrentThread.CurrentUICulture = culture;

CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

PlatformID platform;
bool is64x = false;
int os = -1;

(platform, os, is64x) = SystemUtility.OSCheck();

Version versionOld, versionNew;
string urlToDownload, folderToInstall, urlToVerifyVersion, emailToReportIssue, nameProgram;
bool isFirst = true;
string error;
//Processa argumentos
if (args.Length == 7)
{
    error = Services.ProcessArg(args, out versionOld, out versionNew, out urlToDownload, out folderToInstall, out urlToVerifyVersion, out emailToReportIssue, out nameProgram);
    if (string.IsNullOrWhiteSpace(error))
    {
        WriteError(error);
        return;
    }
    //Verifica conexão com internet
    if (!HTTPMethods.IsConnectedToInternetPing())
    {
        WriteError("The computer don't have acess to internet");
        return;
    }
}
else
{
    versionOld = versionNew = null;
    urlToDownload = folderToInstall = urlToVerifyVersion = emailToReportIssue = nameProgram = null;
}

Console.WriteLine($"Updating the {nameProgram} from {versionOld} to {versionNew} version");


string toUpdateSufixFile = " - to update.zip";
string folderRepository = Path.GetDirectoryName(Assembly.GetAssembly(typeof(Program)).Location);
folderRepository = Path.Combine(folderRepository, "Repository");
if (!Directory.Exists(folderRepository))
{
    Directory.CreateDirectory(folderRepository);
}
if (!string.IsNullOrWhiteSpace(urlToDownload))
{
    string fileNameDownloaded = Path.Combine(folderRepository, nameProgram + ".zip");
    if (File.Exists(fileNameDownloaded))
    {
        File.Delete(fileNameDownloaded);
    }
    DownloadNewVersion(urlToDownload, fileNameDownloaded);
}

error = Services.ReplaceFiles(folderToInstall, folderRepository, os);
if (!string.IsNullOrWhiteSpace(error))
{
    WriteError(error);
    return;
}







#region Methods
void DownloadNewVersion(string downloadFileUrl, string fileNameDownloaded)
{
    // = Path.GetFullPath("file.zip");
    Console.WriteLine("Downloading files...");
    if (!isFirst)
        isFirst = true;



    using (var client = new HttpClientDownloadWithProgress(downloadFileUrl, fileNameDownloaded))
    {

        client.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
        {
            if (isFirst)
            {
                isFirst = false;
                Console.WriteLine($"Total size: {totalFileSize / 100000} MB");
                ConsoleUtility.WriteProgressBar(0);
            }
            downloadProgressChanged(progressPercentage is not null ? (int)progressPercentage : 0);
        };
        client.StartDownload();
        downloadFileCompleted();
    }
}

void downloadFileCompleted()
{
    Console.WriteLine(" Done");
}
void downloadProgressChanged(int progressPercentage)
{
    ConsoleUtility.WriteProgressBar(progressPercentage, true);
}
void WriteError(string message)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("A error was found, the instalation/update will be aborted");
    Console.ResetColor();
    Console.WriteLine("Error message:");
    Console.WriteLine(message);
}
#endregion
