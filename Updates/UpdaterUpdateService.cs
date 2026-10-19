using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace SubnauticaLauncher.Updates;

/// <summary>Maintains the updater separately from the launcher update payload.</summary>
public sealed class UpdaterUpdateService
{
    private const string UpdaterName = "SNLUpdater.exe";
    private static readonly HttpClient HttpClient = new();
    private static Uri ManifestUri => new(
        Environment.GetEnvironmentVariable("SNL_UPDATER_MANIFEST_URL")
        ?? "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/main/Release/SNLUUpdates.xml");

    public async Task<string> EnsureLatestAsync(CancellationToken cancellationToken = default)
    {
        string updaterPath = Path.Combine(AppContext.BaseDirectory, UpdaterName);
        string xml = await HttpClient.GetStringAsync(ManifestUri, cancellationToken);
        LauncherUpdate? update = LauncherUpdateService.ParseManifest(xml)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        if (update is null || GetFileVersion(updaterPath) >= update.Version)
        {
            if (!File.Exists(updaterPath))
                throw new FileNotFoundException("SNLUpdater.exe is missing.", updaterPath);
            return updaterPath;
        }

        LauncherUpdateFile file = update.Files.FirstOrDefault(item => string.Equals(Path.GetFileName(item.Path), UpdaterName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("SNLUUpdates.xml does not contain SNLUpdater.exe.");
        string temporaryPath = updaterPath + ".download";
        await using (Stream source = await HttpClient.GetStreamAsync(new Uri(LauncherUpdateFeed.ContentBaseUri, file.Path.Replace('\\', '/')), cancellationToken))
        await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await source.CopyToAsync(destination, cancellationToken);
        ReplaceWithRetry(temporaryPath, updaterPath);
        return updaterPath;
    }

    private static Version GetFileVersion(string path)
    {
        if (!File.Exists(path)) return new Version(0, 0, 0, 0);
        return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out Version? version) ? version : new Version(0, 0, 0, 0);
    }

    private static void ReplaceWithRetry(string sourcePath, string destinationPath)
    {
        for (int attempt = 1; attempt <= 25; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, true);
                return;
            }
            catch (IOException) when (attempt < 25) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 25) { Thread.Sleep(100); }
        }

        throw new IOException("Could not replace SNLUpdater.exe after repeated attempts.");
    }
}
