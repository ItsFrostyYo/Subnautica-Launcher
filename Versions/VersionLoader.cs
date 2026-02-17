using System.Collections.Generic;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Versions;

public static class VersionLoader
{
    [SupportedOSPlatform("windows")]
    public static List<InstalledVersion> LoadInstalled()
        => InstalledVersionFileService.LoadInstalled(
            "Version.info",
            InstalledVersion.FromInfo);

    public static void Save(InstalledVersion version)
        => InstalledVersionFileService.Save(
            version,
            "Version.info",
            "IsSubnauticaLauncherVersion");
}
