using System.Collections.Generic;
using System.Runtime.Versioning;
using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Versions;

public static class VersionLoader
{
    [SupportedOSPlatform("windows")]
    public static List<InstalledVersion> LoadInstalled()
        => InstalledVersionStore.LoadInstalled(LauncherGame.Subnautica);

    public static void Save(InstalledVersion version)
        => InstalledVersionStore.Save(LauncherGame.Subnautica, version);
}
