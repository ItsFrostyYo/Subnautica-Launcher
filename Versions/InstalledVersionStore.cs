using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Versions;

internal static class InstalledVersionStore
{
    public static List<InstalledVersion> LoadInstalled(LauncherGame game)
    {
        return LoadInstalled(LauncherGameProfiles.Get(game));
    }

    public static List<InstalledVersion> LoadInstalled(LauncherGameProfile profile)
    {
        return InstalledVersionFileService.LoadInstalled(profile);
    }

    public static List<InstalledVersion> LoadInstalledFromRoots(
        IReadOnlyList<string> commonPaths,
        LauncherGame game)
    {
        return LoadInstalledFromRoots(commonPaths, LauncherGameProfiles.Get(game));
    }

    public static List<InstalledVersion> LoadInstalledFromRoots(
        IReadOnlyList<string> commonPaths,
        LauncherGameProfile profile)
    {
        return InstalledVersionFileService.LoadInstalledFromRoots(commonPaths, profile);
    }

    public static void Save(LauncherGame game, InstalledVersion version)
    {
        Save(LauncherGameProfiles.Get(game), version);
    }

    public static void Save(LauncherGameProfile profile, InstalledVersion version)
    {
        InstalledVersionFileService.Save(version, profile);
    }
}
