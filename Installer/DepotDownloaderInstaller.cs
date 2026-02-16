using SubnauticaLauncher.Core;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

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

    public static bool IsInstalled()
    {
        return File.Exists(DepotDownloaderExe);
    }

    public static async Task EnsureInstalledAsync()
    {
        if (IsInstalled())
            return;

        Directory.CreateDirectory(ToolsPath);

        string zipPath = Path.Combine(ToolsPath, "DepotDownloader.zip");
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");

        var data = await client.GetByteArrayAsync(DepotDownloaderZipUrl);
        await File.WriteAllBytesAsync(zipPath, data);

        ZipFile.ExtractToDirectory(zipPath, ToolsPath, true);
        File.Delete(zipPath);

        if (!IsInstalled())
            throw new FileNotFoundException("DepotDownloader.exe was not found after extraction.", DepotDownloaderExe);
    }
}
