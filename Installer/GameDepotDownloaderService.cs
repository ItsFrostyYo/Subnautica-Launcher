using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Versions;
using System.IO;

namespace SubnauticaLauncher.Installer;

public static class GameDepotDownloaderService
{
    public static Task InstallVersionAsync(
        LauncherGame game,
        GameVersionInstallDefinition version,
        DepotInstallAuthOptions auth,
        string installDir,
        DepotInstallCallbacks? callbacks = null,
        CancellationToken cancellationToken = default)
    {
        LauncherGameProfile profile = LauncherGameProfiles.Get(game);
        return DepotInstallWorkflow.InstallAsync(
            version,
            auth,
            installDir,
            profile.InfoFileName,
            profile.LauncherMarker,
            callbacks,
            cancellationToken);
    }

    public static Task InstallVersionAsync(
        LauncherGame game,
        GameVersionInstallDefinition version,
        string username,
        string password,
        string installDir)
    {
        LauncherGameProfile profile = LauncherGameProfiles.Get(game);
        return DepotInstallWorkflow.InstallAsync(
            version,
            username,
            password,
            installDir,
            profile.InfoFileName,
            profile.LauncherMarker);
    }

    public static Task InstallVersionAsync(
        LauncherGame game,
        GameVersionInstallDefinition version,
        string username,
        string password)
    {
        string installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            version.Id);

        return InstallVersionAsync(game, version, username, password, installDir);
    }
}
