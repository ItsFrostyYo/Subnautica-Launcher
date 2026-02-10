using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Installer;

public static class BZDepotDownloaderService
{
    // =========================
    // PUBLIC ENTRY (UI CALLS THIS)
    // =========================
    public static Task InstallVersionAsync(
        VersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        return InstallInternalAsync(version, username, password, installDir);
    }

    // =========================
    // CONVENIENCE OVERLOAD
    // =========================
    public static Task InstallVersionAsync(
        VersionInstallDefinition version,
        string username,
        string password)
    {
        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            version.Id
        );

        return InstallInternalAsync(version, username, password, installDir);
    }

    // =========================
    // CORE IMPLEMENTATION
    // =========================
    private static async Task InstallInternalAsync(
        VersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.");

        Directory.CreateDirectory(installDir);

        string args =
            $"-app {VersionInstallDefinition.AppId} -depot {VersionInstallDefinition.DepotId} -manifest {version.ManifestId} -username \"{username}\" -password \"{password}\" -dir \"{installDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName = DepotDownloaderInstaller.DepotDownloaderExe,
            Arguments = args,
            UseShellExecute = true,   // REQUIRED for Steam Guard
            CreateNoWindow = false   // SHOW CONSOLE
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start DepotDownloader.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception("DepotDownloader failed. Check console output.");

        // ✅ CLEANUP FIRST
        CleanupDepotDownloaderFolders(installDir);

        // ✅ THEN WRITE METADATA
        WriteVersionInfo(version, installDir);
    }

    // =========================
    // CLEANUP
    // =========================
    private static void CleanupDepotDownloaderFolders(string installDir)
    {
        // Remove .DepotDownloader
        string hiddenDepotDir = Path.Combine(installDir, ".DepotDownloader");
        if (Directory.Exists(hiddenDepotDir))
        {
            Directory.Delete(hiddenDepotDir, true);
        }

        // Flatten depot_* folders if present
        foreach (var depotFolder in Directory.GetDirectories(installDir, "depot_*"))
        {
            foreach (var entry in Directory.GetFileSystemEntries(depotFolder))
            {
                string targetPath = Path.Combine(
                    installDir,
                    Path.GetFileName(entry)
                );

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

    // =========================
    // VERSION.INFO
    // =========================
    private static void WriteVersionInfo(
        VersionInstallDefinition version,
        string installDir)
    {
        string infoPath = Path.Combine(installDir, "Version.info");

        File.WriteAllText(infoPath,
$@"IsSubnauticaLauncherVersion=true
DisplayName={version.DisplayName}
FolderName={version.Id}
OriginalDownload={version.Id}
Manifest={version.ManifestId}
");
    }
}
