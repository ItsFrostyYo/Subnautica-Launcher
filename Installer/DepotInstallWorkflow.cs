using SubnauticaLauncher.Versions;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Installer;

internal static class DepotInstallWorkflow
{
    public static async Task InstallAsync(
        GameVersionInstallDefinition version,
        string username,
        string password,
        string installDir,
        string infoFileName,
        string launcherMarker)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.");

        Directory.CreateDirectory(installDir);

        string args =
            $"-app {version.SteamAppId} -depot {version.SteamDepotId} -manifest {version.ManifestId} -username \"{username}\" -password \"{password}\" -dir \"{installDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName = DepotDownloaderInstaller.DepotDownloaderExe,
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start DepotDownloader.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception("DepotDownloader failed. Check console output.");

        CleanupDepotDownloaderFolders(installDir);
        WriteVersionInfo(version, installDir, infoFileName, launcherMarker);
    }

    private static void CleanupDepotDownloaderFolders(string installDir)
    {
        string hiddenDepotDir = Path.Combine(installDir, ".DepotDownloader");
        if (Directory.Exists(hiddenDepotDir))
            Directory.Delete(hiddenDepotDir, true);

        foreach (var depotFolder in Directory.GetDirectories(installDir, "depot_*"))
        {
            foreach (var entry in Directory.GetFileSystemEntries(depotFolder))
            {
                string targetPath = Path.Combine(installDir, Path.GetFileName(entry));

                if (Directory.Exists(entry))
                {
                    if (!Directory.Exists(targetPath))
                        Directory.Move(entry, targetPath);
                }
                else
                {
                    File.Move(entry, targetPath, true);
                }
            }

            Directory.Delete(depotFolder, true);
        }
    }

    private static void WriteVersionInfo(
        GameVersionInstallDefinition version,
        string installDir,
        string infoFileName,
        string launcherMarker)
    {
        string infoPath = Path.Combine(installDir, infoFileName);

        File.WriteAllText(infoPath,
$@"{launcherMarker}=true
DisplayName={version.DisplayName}
FolderName={version.Id}
OriginalDownload={version.Id}
Manifest={version.ManifestId}
");
    }
}
