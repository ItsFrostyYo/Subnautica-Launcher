using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.BelowZero;

public static class BZVersionLoader
{
    [SupportedOSPlatform("windows")]
    public static List<BZInstalledVersion> LoadInstalled()
    {
        var list = new List<BZInstalledVersion>();
        foreach (var common in AppPaths.SteamCommonPaths)
        {
            foreach (var dir in Directory.GetDirectories(common))
            {
                string info = Path.Combine(dir, "BZVersion.info");
                if (!File.Exists(info))
                    continue;

                var v = BZInstalledVersion.FromInfo(dir, info);
                if (v == null)
                    continue;

                v.HomeFolder = dir;
                list.Add(v);
            }
        }

        return list;
    }

    public static void Save(BZInstalledVersion version)
    {
        string infoPath = Path.Combine(version.HomeFolder, "BZVersion.info");

        File.WriteAllText(infoPath,
$@"IsBelowZeroLauncherVersion=true
DisplayName={version.DisplayName}
FolderName={version.FolderName}
OriginalDownload={version.OriginalDownload}
");
    }

}
