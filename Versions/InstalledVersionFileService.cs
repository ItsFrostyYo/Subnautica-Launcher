using SubnauticaLauncher.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

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
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(common);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var dir in directories)
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
        WriteInfoFile(
            infoPath,
            launcherMarker,
            version.DisplayName,
            version.FolderName,
            version.OriginalDownload,
            version.IsModded,
            version.InstalledModId);
    }

    public static void WriteInfoFile(
        string infoPath,
        string launcherMarker,
        string displayName,
        string folderName,
        string originalDownload,
        bool isModded = false,
        string installedModId = "",
        long? manifestId = null)
    {
        var builder = new StringBuilder()
            .AppendLine($"{launcherMarker}=true")
            .AppendLine($"DisplayName={InstalledVersionNaming.NormalizeSavedDisplayName(displayName)}")
            .AppendLine($"FolderName={folderName}")
            .AppendLine($"OriginalDownload={originalDownload}")
            .AppendLine($"Modded={isModded}");

        if (!string.IsNullOrWhiteSpace(installedModId))
            builder.AppendLine($"InstalledMod={installedModId}");

        if (manifestId.HasValue)
            builder.AppendLine($"Manifest={manifestId.Value}");

        File.WriteAllText(infoPath, builder.ToString());
    }
}
