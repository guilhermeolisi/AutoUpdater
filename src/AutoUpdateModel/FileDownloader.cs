namespace AutoUpdaterModel;

/// <summary>
/// Download HTTP simples com relato de progresso, escrito direto sobre
/// <see cref="HttpClient"/>/BCL — sem dependências externas, compatível com
/// trimming e Native AOT. Substitui o helper de BaseLibrary.HTTP no fluxo do
/// AutoUpdater (que arrastava ApplicationInsights, incompatível com AOT).
/// </summary>
public sealed class FileDownloader : IDisposable
{
    public delegate void ProgressHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

    private readonly string url;
    private readonly string destinationPath;
    private readonly HttpClient httpClient;

    public event ProgressHandler? ProgressChanged;

    public FileDownloader(string url, string destinationPath)
    {
        this.url = url;
        this.destinationPath = destinationPath;
        httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task StartDownload()
    {
        using HttpResponseMessage response =
            await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        string? dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using FileStream target = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            received += read;
            double? pct = total is > 0 ? Math.Round(received * 100d / total.Value, 2) : null;
            ProgressChanged?.Invoke(total, received, pct);
        }
    }

    public void Dispose() => httpClient.Dispose();
}
