using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Installer;

public static class DepotDownloaderInstaller
{
    private const string DepotDownloaderZipUrl =
        "https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-windows-x64.zip";

    // Root tools folder
    public static string ToolsPath => AppPaths.ToolsPath;

    // Public EXE path (THIS FIXES YOUR ERROR)
    public static string DepotDownloaderExe
    {
        get
        {
            var nested = Path.Combine(ToolsPath, "DepotDownloader", "DepotDownloader.exe");
            if (File.Exists(nested))
                return nested;

            return Path.Combine(ToolsPath, "DepotDownloader.exe");
        }
    }

    public static async Task EnsureInstalledAsync()
    {
        if (File.Exists(DepotDownloaderExe))
            return;

        Directory.CreateDirectory(ToolsPath);

        string zipPath = Path.Combine(ToolsPath, "DepotDownloader.zip");

        using var client = new HttpClient();
        var data = await client.GetByteArrayAsync(DepotDownloaderZipUrl);
        await File.WriteAllBytesAsync(zipPath, data);

        ZipFile.ExtractToDirectory(zipPath, ToolsPath, true);
        File.Delete(zipPath);
    }
}