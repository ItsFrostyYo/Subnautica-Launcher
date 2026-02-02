using SubnauticaLauncher.Versions;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher.BelowZero;

public static class BZDepotDownloaderService
{
    // =========================
    // PUBLIC ENTRY
    // =========================
    public static Task BZInstallVersionAsync(
        BZVersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        return BZInstallInternalAsync(version, username, password, installDir);
    }

    // =========================
    // CONVENIENCE OVERLOAD
    // =========================
    public static Task BZInstallVersionAsync(
        BZVersionInstallDefinition version,
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

        return BZInstallInternalAsync(version, username, password, installDir);
    }

    // =========================
    // CORE IMPLEMENTATION
    // =========================
    private static async Task BZInstallInternalAsync(
        BZVersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.");

        Directory.CreateDirectory(installDir);

        string args = $"-app {BZVersionInstallDefinition.AppId} -depot {BZVersionInstallDefinition.DepotId} -manifest {version.ManifestId} -username \"{username}\" -password \"{password}\" -dir \"{installDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName = BZDepotDownloaderInstaller.DepotDownloaderExe,
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
        WriteVersionInfo(version, installDir);
    }

    // =========================
    // CLEANUP
    // =========================
    private static void CleanupDepotDownloaderFolders(string installDir)
    {
        string hiddenDepotDir = Path.Combine(installDir, ".DepotDownloader");
        if (Directory.Exists(hiddenDepotDir))
            Directory.Delete(hiddenDepotDir, true);

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
        BZVersionInstallDefinition version,
        string installDir)
    {
        string infoPath = Path.Combine(installDir, "BZVersion.info");

        File.WriteAllText(infoPath,
$@"IsBelowZeroLauncherVersion=true
DisplayName={version.DisplayName}
FolderName={version.Id}
OriginalDownload={version.Id}
Manifest={version.ManifestId}
");
    }
}