using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Enums;
using System;
using System.IO;
using System.Threading;
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
        DepotInstallAuthOptions auth,
        string installDir,
        DepotInstallCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        return GameDepotDownloaderService.InstallVersionAsync(
            LauncherGame.BelowZero,
            version,
            auth,
            installDir,
            callbacks,
            cancellationToken);
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
        return GameDepotDownloaderService.InstallVersionAsync(
            LauncherGame.BelowZero,
            version,
            username,
            password,
            installDir);
    }
}
