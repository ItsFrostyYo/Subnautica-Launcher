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
    public static List<InstalledVersion> LoadInstalled(LauncherGameProfile profile)
    {
        IReadOnlyList<string> commonPaths = AppPaths.SteamCommonPaths;
        if (commonPaths.Count == 0)
            commonPaths = new List<string> { AppPaths.SteamCommonPath };

        return LoadInstalledFromRoots(commonPaths, profile);
    }

    [SupportedOSPlatform("windows")]
    public static List<InstalledVersion> LoadInstalledFromRoots(
        IReadOnlyList<string> commonPaths,
        LauncherGameProfile profile)
    {
        var list = new List<InstalledVersion>();

        foreach (string common in commonPaths)
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
                string info = Path.Combine(dir, profile.InfoFileName);
                if (!File.Exists(info))
                    continue;

                if (!HasLauncherMarker(info, profile.LauncherMarker))
                    continue;

                if (!profile.HasExpectedExecutable(dir))
                    continue;

                var version = profile.FromInfo(dir, info);
                if (version == null)
                    continue;

                version.HomeFolder = dir;
                list.Add(version);
            }
        }

        return list;
    }

    [SupportedOSPlatform("windows")]
    public static void RepairMisplacedInfoFiles(IReadOnlyList<string> commonPaths)
    {
        foreach (string commonPath in commonPaths)
            RepairMisplacedInfoFiles(commonPath);
    }

    private sealed record ParsedInfo(
        string DisplayName,
        string FolderName,
        string OriginalDownload,
        bool IsModded,
        string InstalledModId,
        long? ManifestId);

    public static void Save(InstalledVersion version, LauncherGameProfile profile)
    {
        string infoPath = Path.Combine(version.HomeFolder, profile.InfoFileName);
        WriteInfoFile(
            infoPath,
            profile.LauncherMarker,
            version.DisplayName,
            version.FolderName,
            version.OriginalDownload,
            version.IsModded,
            version.InstalledModId);
    }

    public static void WriteInfoFile(
        string versionFolder,
        LauncherGameProfile profile,
        string displayName,
        string folderName,
        string originalDownload,
        bool isModded = false,
        string installedModId = "",
        long? manifestId = null)
    {
        string infoPath = Path.Combine(versionFolder, profile.InfoFileName);
        WriteInfoFile(
            infoPath,
            profile.LauncherMarker,
            displayName,
            folderName,
            originalDownload,
            isModded,
            installedModId,
            manifestId);
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
        DeleteConflictingInfoFile(infoPath);
    }

    private static bool HasLauncherMarker(string infoPath, string launcherMarker)
    {
        try
        {
            foreach (string line in File.ReadLines(infoPath))
            {
                if (line.StartsWith($"{launcherMarker}=", StringComparison.Ordinal))
                    return line.EndsWith("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static void RepairMisplacedInfoFiles(string commonPath)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(commonPath);
        }
        catch
        {
            return;
        }

        foreach (string dir in directories)
        {
            try
            {
                LauncherGameProfile? detectedProfile = LauncherGameProfiles.DetectFromFolder(dir);
                bool hasSubnauticaExe = LauncherGameProfiles.Subnautica.HasExpectedExecutable(dir);
                bool hasBelowZeroExe = LauncherGameProfiles.BelowZero.HasExpectedExecutable(dir);

                if (hasSubnauticaExe == hasBelowZeroExe)
                    continue;

                if (detectedProfile == null)
                    continue;

                detectedProfile.EnsureSteamAppIdFile(dir);

                string expectedInfoName = detectedProfile.InfoFileName;
                string expectedMarker = detectedProfile.LauncherMarker;
                string? conflictingInfoName = GetConflictingInfoName(detectedProfile.InfoFileName);
                if (string.IsNullOrWhiteSpace(conflictingInfoName))
                    continue;

                string expectedInfoPath = Path.Combine(dir, expectedInfoName);
                string conflictingInfoPath = Path.Combine(dir, conflictingInfoName);

                bool expectedExists = File.Exists(expectedInfoPath);
                bool conflictingExists = File.Exists(conflictingInfoPath);

                ParsedInfo? parsed = TryParseInfoFile(expectedInfoPath) ??
                                     TryParseInfoFile(conflictingInfoPath);

                if (parsed != null && (!expectedExists || !HasLauncherMarker(expectedInfoPath, expectedMarker)))
                {
                    WriteInfoFile(
                        expectedInfoPath,
                        expectedMarker,
                        parsed.DisplayName,
                        parsed.FolderName,
                        parsed.OriginalDownload,
                        parsed.IsModded,
                        parsed.InstalledModId,
                        parsed.ManifestId);

                    Logger.Log($"Repaired launcher metadata for {detectedProfile.DisplayName} folder '{dir}'.");
                    continue;
                }

                if (expectedExists && conflictingExists)
                {
                    File.Delete(conflictingInfoPath);
                    Logger.Log($"Removed conflicting metadata file '{conflictingInfoPath}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"Failed to repair launcher metadata in '{dir}'.");
            }
        }
    }

    private static ParsedInfo? TryParseInfoFile(string infoPath)
    {
        if (!File.Exists(infoPath))
            return null;

        try
        {
            string displayName = "";
            string folderName = "";
            string originalDownload = "";
            bool isModded = false;
            string installedModId = "";
            long? manifestId = null;

            foreach (string line in File.ReadLines(infoPath))
            {
                if (line.StartsWith("DisplayName=", StringComparison.Ordinal))
                    displayName = line["DisplayName=".Length..];
                else if (line.StartsWith("FolderName=", StringComparison.Ordinal))
                    folderName = line["FolderName=".Length..];
                else if (line.StartsWith("OriginalDownload=", StringComparison.Ordinal))
                    originalDownload = line["OriginalDownload=".Length..];
                else if (line.StartsWith("Modded=", StringComparison.Ordinal))
                    isModded = bool.TryParse(line["Modded=".Length..], out bool parsed) && parsed;
                else if (line.StartsWith("InstalledMod=", StringComparison.Ordinal))
                    installedModId = line["InstalledMod=".Length..];
                else if (line.StartsWith("Manifest=", StringComparison.Ordinal) &&
                         long.TryParse(line["Manifest=".Length..], out long parsedManifest))
                    manifestId = parsedManifest;
            }

            if (string.IsNullOrWhiteSpace(displayName) &&
                string.IsNullOrWhiteSpace(folderName) &&
                string.IsNullOrWhiteSpace(originalDownload))
            {
                return null;
            }

            return new ParsedInfo(
                displayName,
                folderName,
                originalDownload,
                isModded,
                installedModId,
                manifestId);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteConflictingInfoFile(string infoPath)
    {
        string? directory = Path.GetDirectoryName(infoPath);
        string fileName = Path.GetFileName(infoPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return;

        string? conflictingName = GetConflictingInfoName(fileName);

        if (string.IsNullOrWhiteSpace(conflictingName))
            return;

        string conflictingPath = Path.Combine(directory, conflictingName);
        if (File.Exists(conflictingPath))
            File.Delete(conflictingPath);
    }

    private static string? GetConflictingInfoName(string fileName)
    {
        return fileName switch
        {
            "Version.info" => "BZVersion.info",
            "BZVersion.info" => "Version.info",
            _ => null
        };
    }
}
