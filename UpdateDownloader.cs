using System.Net.Http;
using System.IO;

namespace SubnauticaLauncher.Updater;

public static class UpdateDownloader
{
    public static async Task<string> DownloadAsync(string url)
    {
        var temp = Path.Combine(Path.GetTempPath(), "SubnauticaLauncher.new.exe");

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(url);

        await File.WriteAllBytesAsync(temp, bytes);
        return temp;
    }
}