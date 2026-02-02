using System;
using System.Collections.Generic;
using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.BelowZero;

namespace SubnauticaLauncher.BelowZero;

public static class BZVersionLoader
{
    public static List<BZInstalledVersion> LoadInstalled()
    {
        var list = new List<BZInstalledVersion>();
        string common = AppPaths.SteamCommonPath;

        if (!Directory.Exists(common))
            return list;

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

    private static bool PathsAreSame(string a, string b)
    {
        return string.Equals(
            Path.GetFullPath(a).TrimEnd('\\'),
            Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase
        );
    }
}