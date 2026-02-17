using SubnauticaLauncher.Versions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Installer;

public static class DepotDownloaderService
{
    public static Task InstallVersionAsync(
        VersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        return InstallInternalAsync(version, username, password, installDir);
    }

    public static Task InstallVersionAsync(
        VersionInstallDefinition version,
        DepotInstallAuthOptions auth,
        string installDir,
        DepotInstallCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        return DepotInstallWorkflow.InstallAsync(
            version,
            auth,
            installDir,
            "Version.info",
            "IsSubnauticaLauncherVersion",
            callbacks,
            cancellationToken);
    }

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
            version.Id);

        return InstallInternalAsync(version, username, password, installDir);
    }

    private static Task InstallInternalAsync(
        VersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        return DepotInstallWorkflow.InstallAsync(
            version,
            username,
            password,
            installDir,
            "Version.info",
            "IsSubnauticaLauncherVersion");
    }
}
