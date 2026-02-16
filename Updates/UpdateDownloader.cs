using System.IO;
using System.Net.Http;

namespace SubnauticaLauncher.Updates;

public static class UpdateDownloader
{
    public static async Task<string> DownloadAsync(
        string url,
        string fileName,
        IProgress<double>? progressPercent = null,
        CancellationToken cancellationToken = default)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), fileName);

        using var http = new HttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long bytesWritten = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesWritten += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                double percent = (double)bytesWritten / totalBytes.Value * 100.0;
                progressPercent?.Report(percent);
            }
        }

        progressPercent?.Report(100);
        return tempPath;
    }
}
