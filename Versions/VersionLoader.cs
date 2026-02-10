using System;
using System.Collections.Generic;
using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Versions;

public static class VersionLoader
{
    public static List<InstalledVersion> LoadInstalled()
    {
        var list = new List<InstalledVersion>();
        foreach (var common in AppPaths.SteamCommonPaths)
        {
            foreach (var dir in Directory.GetDirectories(common))
            {
                string info = Path.Combine(dir, "Version.info");
                if (!File.Exists(info))
                    continue;

                var v = InstalledVersion.FromInfo(dir, info);
                if (v == null)
                    continue;

                v.HomeFolder = dir;
                list.Add(v);
            }
        }

        return list;
    }

    public static void Save(InstalledVersion version)
    {
        string infoPath = Path.Combine(version.HomeFolder, "Version.info");

        File.WriteAllText(infoPath,
$@"IsSubnauticaLauncherVersion=true
DisplayName={version.DisplayName}
FolderName={version.FolderName}
OriginalDownload={version.OriginalDownload}
");
    }

    private static bool PathsAreSame(string a, string b)
    {
        return string.Equals(
            Path.GetFullPath(a).TrimEnd('\\'),
            Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase
        );
    }
}