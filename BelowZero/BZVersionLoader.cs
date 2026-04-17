using SubnauticaLauncher.Versions;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.BelowZero;

public static class BZVersionLoader
{
    [SupportedOSPlatform("windows")]
    public static List<BZInstalledVersion> LoadInstalled()
        => InstalledVersionStore.LoadInstalled(LauncherGame.BelowZero)
            .OfType<BZInstalledVersion>()
            .ToList();

    public static void Save(BZInstalledVersion version)
        => InstalledVersionStore.Save(LauncherGame.BelowZero, version);
}
