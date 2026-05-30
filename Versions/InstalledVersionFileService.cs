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
        string LaunchOptions,
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
            version.LaunchOptions,
            version.IsModded,
            version.InstalledModId);
    }

    public static void WriteInfoFile(
        string versionFolder,
        LauncherGameProfile profile,
        string displayName,
        string folderName,
        string originalDownload,
        string launchOptions = "",
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
            launchOptions,
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
        string launchOptions = "",
        bool isModded = false,
        string installedModId = "",
        long? manifestId = null)
    {
        var builder = new StringBuilder()
            .AppendLine($"{launcherMarker}=true")
            .AppendLine($"DisplayName={InstalledVersionNaming.NormalizeSavedDisplayName(displayName)}")
            .AppendLine($"FolderName={folderName}")
            .AppendLine($"OriginalDownload={originalDownload}")
            .AppendLine($"LaunchOptions={launchOptions?.Trim() ?? string.Empty}")
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
                if (detectedProfile == null)
                    continue;

                detectedProfile.RemoveSteamAppIdFiles(dir);

                string expectedInfoName = detectedProfile.InfoFileName;
                string expectedMarker = detectedProfile.LauncherMarker;
                string expectedInfoPath = Path.Combine(dir, expectedInfoName);
                bool expectedExists = File.Exists(expectedInfoPath);
                List<string> conflictingInfoPaths = GetConflictingInfoPaths(dir, expectedInfoName);

                ParsedInfo? parsed = TryParseInfoFile(expectedInfoPath);
                if (parsed == null)
                {
                    foreach (string conflictingInfoPath in conflictingInfoPaths)
                    {
                        parsed = TryParseInfoFile(conflictingInfoPath);
                        if (parsed != null)
                            break;
                    }
                }

                string resolvedOriginalDownload = parsed != null
                    ? ResolveOriginalDownload(dir, detectedProfile, parsed.OriginalDownload)
                    : string.Empty;
                string resolvedDisplayName = parsed != null
                    ? ResolveDisplayName(parsed.DisplayName, resolvedOriginalDownload, parsed.FolderName)
                    : string.Empty;

                bool needsOriginalDownloadRepair = parsed != null &&
                                                  !string.Equals(
                                                      parsed.OriginalDownload,
                                                      resolvedOriginalDownload,
                                                      StringComparison.Ordinal);
                bool needsDisplayNameRepair = parsed != null &&
                                              !string.Equals(
                                                  parsed.DisplayName,
                                                  resolvedDisplayName,
                                                  StringComparison.Ordinal);

                if (parsed != null &&
                    (!expectedExists ||
                     !HasLauncherMarker(expectedInfoPath, expectedMarker) ||
                     needsOriginalDownloadRepair ||
                     needsDisplayNameRepair))
                {
                    WriteInfoFile(
                        expectedInfoPath,
                        expectedMarker,
                        resolvedDisplayName,
                        parsed.FolderName,
                        resolvedOriginalDownload,
                        parsed.LaunchOptions,
                        parsed.IsModded,
                        parsed.InstalledModId,
                        parsed.ManifestId);

                    Logger.Log($"Repaired launcher metadata for {detectedProfile.DisplayName} folder '{dir}'.");
                    continue;
                }

                if (expectedExists)
                {
                    foreach (string conflictingInfoPath in conflictingInfoPaths.Where(File.Exists))
                    {
                        File.Delete(conflictingInfoPath);
                        Logger.Log($"Removed conflicting metadata file '{conflictingInfoPath}'.");
                    }
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
            string launchOptions = "";
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
                else if (line.StartsWith("LaunchOptions=", StringComparison.Ordinal))
                    launchOptions = line["LaunchOptions=".Length..];
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
                launchOptions,
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

        foreach (string conflictingPath in GetConflictingInfoPaths(directory, fileName))
        {
            if (File.Exists(conflictingPath))
                File.Delete(conflictingPath);
        }
    }

    private static List<string> GetConflictingInfoPaths(string directory, string fileName)
    {
        return LauncherGameProfiles.All
            .Select(profile => profile.InfoFileName)
            .Where(infoName => !string.Equals(infoName, fileName, StringComparison.OrdinalIgnoreCase))
            .Select(infoName => Path.Combine(directory, infoName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveOriginalDownload(
        string versionFolder,
        LauncherGameProfile profile,
        string existingOriginalDownload)
    {
        if (profile.InstallDefinitions.Any(def =>
                string.Equals(def.Id, existingOriginalDownload, StringComparison.Ordinal)))
        {
            return existingOriginalDownload;
        }

        return VersionIdentityResolver.TryDetectOriginalVersion(
            versionFolder,
            profile,
            out GameVersionInstallDefinition? detectedVersion,
            out _,
            out _)
            ? detectedVersion!.Id
            : existingOriginalDownload;
    }

    private static string ResolveDisplayName(
        string existingDisplayName,
        string originalDownload,
        string folderName)
    {
        if (!string.IsNullOrWhiteSpace(existingDisplayName))
            return existingDisplayName;

        GameVersionInstallDefinition? definition = LauncherGameProfiles.All
            .SelectMany(profile => profile.InstallDefinitions)
            .FirstOrDefault(def => string.Equals(def.Id, originalDownload, StringComparison.Ordinal));

        return definition != null
            ? InstalledVersionNaming.BuildBaseDisplayName(definition.Id, definition.DisplayName)
            : folderName;
    }
}
