using SubnauticaLauncher.Core;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Versions;

internal static class InstalledVersionFileService
{
    [SupportedOSPlatform("windows")]
    public static List<T> LoadInstalled<T>(
        string infoFileName,
        Func<string, string, T?> fromInfo)
        where T : InstalledVersion
    {
        var list = new List<T>();

        foreach (var common in AppPaths.SteamCommonPaths)
        {
            foreach (var dir in Directory.GetDirectories(common))
            {
                string info = Path.Combine(dir, infoFileName);
                if (!File.Exists(info))
                    continue;

                var version = fromInfo(dir, info);
                if (version == null)
                    continue;

                version.HomeFolder = dir;
                list.Add(version);
            }
        }

        return list;
    }

    public static void Save(
        InstalledVersion version,
        string infoFileName,
        string launcherMarker)
    {
        string infoPath = Path.Combine(version.HomeFolder, infoFileName);

        File.WriteAllText(infoPath,
$@"{launcherMarker}=true
DisplayName={version.DisplayName}
FolderName={version.FolderName}
OriginalDownload={version.OriginalDownload}
");
    }
}
