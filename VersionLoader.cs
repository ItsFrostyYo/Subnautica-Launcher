using SubnauticaLauncher.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace SubnauticaLauncher
{
    public static class VersionLoader
    {
        public static List<InstalledVersion> LoadInstalled()
        {
            var list = new List<InstalledVersion>();
            string common = AppPaths.SteamCommonPath;

            if (!Directory.Exists(common))
                return list;

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
}