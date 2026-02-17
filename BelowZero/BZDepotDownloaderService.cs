using SubnauticaLauncher.Installer;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher.BelowZero;

public static class BZDepotDownloaderService
{
    public static Task BZInstallVersionAsync(
        BZVersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        return BZInstallInternalAsync(version, username, password, installDir);
    }

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
            version.Id);

        return BZInstallInternalAsync(version, username, password, installDir);
    }

    private static Task BZInstallInternalAsync(
        BZVersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        return DepotInstallWorkflow.InstallAsync(
            version,
            username,
            password,
            installDir,
            "BZVersion.info",
            "IsBelowZeroLauncherVersion");
    }
}
