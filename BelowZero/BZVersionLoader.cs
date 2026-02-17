using SubnauticaLauncher.Versions;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.BelowZero;

public static class BZVersionLoader
{
    [SupportedOSPlatform("windows")]
    public static List<BZInstalledVersion> LoadInstalled()
        => InstalledVersionFileService.LoadInstalled(
            "BZVersion.info",
            BZInstalledVersion.FromInfo);

    public static void Save(BZInstalledVersion version)
        => InstalledVersionFileService.Save(
            version,
            "BZVersion.info",
            "IsBelowZeroLauncherVersion");
}
