using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Versions;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Mods;

public static class ModInstallerService
{
    private static readonly HttpClient Http = BuildClient();
    private static readonly Regex ModVersionRegex = new(@"v(?<version>\d+\.\d+\.\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ModDefinition> GetAvailableModsForVersion(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        return ManagedModFamilies
            .GetCompatibleFamilies(game, originalDownload, displayName, folderName)
            .Select(family => ModCatalog.GetById(family.Id))
            .Where(mod => mod != null)
            .Cast<ModDefinition>()
            .ToList();
    }

    public static IReadOnlyList<ModDefinition> GetInstallableModsForVersion(
        LauncherGame game,
        InstalledVersion version)
    {
        IReadOnlyList<ModDefinition> available = GetAvailableModsForVersion(
            game,
            version.OriginalDownload,
            version.DisplayName,
            version.FolderName);

        if (available.Count == 0 || string.IsNullOrWhiteSpace(version.InstalledModId))
            return available;

        return available
            .Where(mod => !string.Equals(mod.Id, version.InstalledModId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static bool SupportsLegacySpeedrunRng(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        return ManagedModFamilies.SpeedrunRng.Game == game &&
               ManagedModFamilies.SpeedrunRng.SupportsVersion(originalDownload, displayName, folderName);
    }

    public static bool SupportsModernSpeedrunRng(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        return ManagedModFamilies.SpeedrunRng20Plus.Game == game &&
               ManagedModFamilies.SpeedrunRng20Plus.SupportsVersion(originalDownload, displayName, folderName);
    }

    public static async Task InstallBundleAsync(
        ModDefinition mod,
        LauncherGame game,
        string targetFolder,
        DepotInstallCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        callbacks?.OnStatus?.Invoke($"Downloading {mod.DisplayName}...");
        callbacks?.OnOutput?.Invoke($"Downloading mod bundle from {mod.DownloadUrl}");

        string tempRoot = Path.Combine(Path.GetTempPath(), "SubnauticaLauncher", "mods", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string zipPath = Path.Combine(tempRoot, mod.BundleZipFileName);
        string extractDir = Path.Combine(tempRoot, "extract");

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                mod.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream destination = File.Create(zipPath))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;

                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;

                    if (contentLength is > 0)
                    {
                        double percent = Math.Clamp((double)totalRead * 100d / contentLength.Value, 0, 100);
                        callbacks?.OnProgress?.Invoke(percent);
                    }
                }
            }

            callbacks?.OnStatus?.Invoke($"Extracting {mod.DisplayName}...");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            string contentRoot = ResolveContentRoot(extractDir);
            ValidateExtractedBundleOrThrow(mod, contentRoot);

            callbacks?.OnStatus?.Invoke($"Installing {mod.DisplayName}...");
            callbacks?.OnOutput?.Invoke($"Copying mod bundle into {targetFolder}");

            CleanupStaleBundleFiles(
                mod,
                contentRoot,
                targetFolder,
                mod.PreservedRelativePaths,
                mod.StaleCleanupRelativeRoots);
            CopyDirectoryContents(contentRoot, targetFolder, mod.PreservedRelativePaths);

            LauncherGameProfiles.Get(game).EnsureSteamAppIdFile(targetFolder);
            ValidateInstalledBundleOrThrow(mod, targetFolder);

            callbacks?.OnProgress?.Invoke(100);
            callbacks?.OnStatus?.Invoke($"{mod.DisplayName} installed.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    public static void RemoveManagedMod(
        InstalledVersion version,
        LauncherGame game)
    {
        foreach (string relativePath in GetGenericRemovalTargets())
        {
            string fullPath = Path.Combine(version.HomeFolder, relativePath);
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            else if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        version.HasBepInEx = false;
        version.DetectedModNames.Clear();
        version.IsModded = false;
        version.InstalledModId = string.Empty;

        LauncherGameProfiles.Get(game).EnsureSteamAppIdFile(version.HomeFolder);
    }

    public static string BuildModdedDisplayName(string baseDisplayName, int instanceNumber = 1)
    {
        return InstalledVersionNaming.BuildModdedDisplayName(baseDisplayName, instanceNumber);
    }

    public static string BuildModdedFolderName(string baseFolderName, int instanceNumber = 1)
    {
        string suffix = instanceNumber <= 1 ? "_RNGMod" : $"_RNGMod{instanceNumber}";
        return baseFolderName + suffix;
    }

    public static ModDefinition? GetInstalledModDefinition(LauncherGame game, InstalledVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.InstalledModId))
            return null;

        ModDefinition? mod = ModCatalog.GetById(version.InstalledModId);
        return mod?.Game == game ? mod : null;
    }

    public static Version? TryReadInstalledModVersion(InstalledVersion version)
    {
        IEnumerable<string> candidates = GetVersionMarkerCandidates(version);

        foreach (string relativePath in candidates)
        {
            string versionFilePath = Path.Combine(version.HomeFolder, relativePath);
            if (!File.Exists(versionFilePath))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(versionFilePath).Trim();
            }
            catch
            {
                continue;
            }

            Match match = ModVersionRegex.Match(text);
            if (!match.Success)
                continue;

            if (Version.TryParse(match.Groups["version"].Value, out Version? versionValue))
                return versionValue;
        }

        return null;
    }

    public static void ApplyInstalledModDetection(InstalledVersion version)
    {
        string bepinexFolder = Path.Combine(version.HomeFolder, "BepInEx");
        version.HasBepInEx = Directory.Exists(bepinexFolder);
        version.DetectedModNames = DetectInstalledPluginNames(version).ToList();
        version.IsModded = version.HasBepInEx;

        if (!version.HasBepInEx)
        {
            version.InstalledModId = string.Empty;
            return;
        }

        version.InstalledModId = DetectKnownManagedModId(version);
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
        return client;
    }

    private static string ResolveContentRoot(string extractDir)
    {
        string[] directories = Directory.GetDirectories(extractDir);
        string[] files = Directory.GetFiles(extractDir);

        if (directories.Length == 1 && files.Length == 0)
            return directories[0];

        return extractDir;
    }

    private static void ValidateExtractedBundleOrThrow(ModDefinition mod, string contentRoot)
    {
        string pluginRoot = Path.Combine(contentRoot, NormalizeRelativePath(mod.PluginRootRelativePath));
        string versionMarker = Path.Combine(contentRoot, NormalizeRelativePath(mod.VersionMarkerRelativePath));

        if (!Directory.Exists(pluginRoot))
        {
            Logger.Warn($"[Mods] Invalid bundle for {mod.Id}: plugin root '{mod.PluginRootRelativePath}' missing in '{contentRoot}'.");
            throw new InvalidDataException($"Mod bundle '{mod.BundleZipFileName}' is missing the plugin folder.");
        }

        if (!Directory.GetFiles(pluginRoot, "*.dll", SearchOption.TopDirectoryOnly).Any())
        {
            Logger.Warn($"[Mods] Invalid bundle for {mod.Id}: no plugin DLL found in '{pluginRoot}'.");
            throw new InvalidDataException($"Mod bundle '{mod.BundleZipFileName}' does not contain a plugin DLL.");
        }

        if (!File.Exists(versionMarker))
        {
            Logger.Warn($"[Mods] Invalid bundle for {mod.Id}: version marker '{mod.VersionMarkerRelativePath}' missing.");
            throw new InvalidDataException($"Mod bundle '{mod.BundleZipFileName}' is missing its version marker.");
        }

        foreach (string cleanupRoot in mod.StaleCleanupRelativeRoots)
        {
            string fullCleanupRoot = Path.Combine(contentRoot, NormalizeRelativePath(cleanupRoot));
            if (!Directory.Exists(fullCleanupRoot))
            {
                Logger.Warn($"[Mods] Invalid bundle for {mod.Id}: cleanup root '{cleanupRoot}' missing.");
                throw new InvalidDataException($"Mod bundle '{mod.BundleZipFileName}' is missing required preset data.");
            }
        }
    }

    private static void ValidateInstalledBundleOrThrow(ModDefinition mod, string targetFolder)
    {
        string pluginRoot = Path.Combine(targetFolder, NormalizeRelativePath(mod.PluginRootRelativePath));
        string versionMarkerPath = Path.Combine(targetFolder, NormalizeRelativePath(mod.VersionMarkerRelativePath));

        if (!Directory.Exists(pluginRoot))
            throw new InvalidDataException($"Installed mod root '{mod.PluginRootRelativePath}' was not created.");

        if (!File.Exists(versionMarkerPath))
            throw new InvalidDataException($"Installed version marker '{mod.VersionMarkerRelativePath}' is missing.");

        Version? installedVersion = TryReadVersionFile(versionMarkerPath);
        if (installedVersion == null)
            throw new InvalidDataException($"Installed version marker '{mod.VersionMarkerRelativePath}' could not be parsed.");

        if (installedVersion != mod.PackageVersion)
        {
            Logger.Warn($"[Mods] Installed version marker mismatch for {mod.Id}. Expected={mod.PackageVersion}, Actual={installedVersion}, Folder='{targetFolder}'.");
            throw new InvalidDataException($"Installed mod version is '{installedVersion}', expected '{mod.PackageVersion}'.");
        }
    }

    private static Version? TryReadVersionFile(string versionFilePath)
    {
        try
        {
            string text = File.ReadAllText(versionFilePath).Trim();
            Match match = ModVersionRegex.Match(text);
            if (!match.Success)
                return null;

            return Version.TryParse(match.Groups["version"].Value, out Version? versionValue)
                ? versionValue
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyDirectoryContents(
        string sourceDir,
        string targetDir,
        IReadOnlyList<string> preservedRelativePaths)
    {
        Directory.CreateDirectory(targetDir);

        var preserved = new HashSet<string>(
            preservedRelativePaths.Select(NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string destination = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (preserved.Contains(NormalizeRelativePath(relative)) && File.Exists(destination))
                continue;

            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void CleanupStaleBundleFiles(
        ModDefinition mod,
        string sourceDir,
        string targetDir,
        IReadOnlyList<string> preservedRelativePaths,
        IReadOnlyList<string> cleanupRoots)
    {
        var preserved = new HashSet<string>(
            preservedRelativePaths.Select(NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (string cleanupRoot in cleanupRoots)
        {
            string normalizedRoot = NormalizeRelativePath(cleanupRoot);
            string sourceRoot = Path.Combine(sourceDir, normalizedRoot);
            string targetRoot = Path.Combine(targetDir, normalizedRoot);
            if (!Directory.Exists(sourceRoot) || !Directory.Exists(targetRoot))
                continue;

            var sourceFiles = new HashSet<string>(
                Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                    .Select(path => NormalizeRelativePath(Path.Combine(
                        normalizedRoot,
                        Path.GetRelativePath(sourceRoot, path)))),
                StringComparer.OrdinalIgnoreCase);

            foreach (string targetFile in Directory.GetFiles(targetRoot, "*", SearchOption.AllDirectories))
            {
                string relative = NormalizeRelativePath(Path.Combine(
                    normalizedRoot,
                    Path.GetRelativePath(targetRoot, targetFile)));

                if (preserved.Contains(relative) || sourceFiles.Contains(relative))
                    continue;

                Logger.Log($"[Mods] Removing stale file '{relative}' while updating {mod.Id}.");
                File.Delete(targetFile);
            }

            foreach (string directory in Directory.GetDirectories(targetRoot, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('/', '\\').TrimStart('\\');
    }

    private static IEnumerable<string> DetectInstalledPluginNames(InstalledVersion version)
    {
        string pluginRoot = Path.Combine(version.HomeFolder, "BepInEx", "plugins");
        if (!Directory.Exists(pluginRoot))
            return Array.Empty<string>();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dllPath in Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileNameWithoutExtension(dllPath);
            string? friendlyName = TryMapKnownPluginName(version, fileName);

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                string relativeDir = Path.GetRelativePath(pluginRoot, Path.GetDirectoryName(dllPath)!);
                friendlyName = string.Equals(relativeDir, ".", StringComparison.Ordinal)
                    ? fileName
                    : Path.GetFileName(relativeDir);
            }

            names.Add(friendlyName);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string DetectKnownManagedModId(InstalledVersion version)
    {
        LauncherGameProfile? profile = LauncherGameProfiles.DetectFromFolder(version.HomeFolder);
        if (profile == null)
            return string.Empty;

        IReadOnlyList<ManagedModFamily> compatibleFamilies = ManagedModFamilies.GetCompatibleFamilies(
            profile.Game,
            version.OriginalDownload,
            version.DisplayName,
            version.FolderName);

        if (compatibleFamilies.Count == 0)
            return string.Empty;

        string pluginRoot = Path.Combine(version.HomeFolder, "BepInEx", "plugins");
        if (!Directory.Exists(pluginRoot))
            return string.Empty;

        string[] pluginFiles = Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories);

        foreach (ManagedModFamily family in compatibleFamilies)
        {
            if (pluginFiles.Any(path => Path.GetFileName(path).Contains(family.ManagedPluginFileNameToken, StringComparison.OrdinalIgnoreCase)))
                return family.Id;
        }

        return string.Empty;
    }

    private static string? TryMapKnownPluginName(InstalledVersion version, string fileName)
    {
        ManagedModFamily? family = ManagedModFamilies.GetById(DetectKnownManagedModId(version));
        if (family == null)
            return null;

        return fileName.Contains(family.ManagedPluginFileNameToken, StringComparison.OrdinalIgnoreCase)
            ? family.DisplayName
            : null;
    }

    private static IEnumerable<string> GetVersionMarkerCandidates(InstalledVersion version)
    {
        ManagedModFamily? knownFamily = ManagedModFamilies.GetById(version.InstalledModId);
        if (knownFamily != null)
            yield return knownFamily.VersionMarkerRelativePath;

        foreach (string candidate in ManagedModFamilies.All
                     .Select(family => family.VersionMarkerRelativePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (knownFamily != null &&
                string.Equals(candidate, knownFamily.VersionMarkerRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static IReadOnlyList<string> GetGenericRemovalTargets()
    {
        return ManagedModFamilies.All
            .SelectMany(family => family.RemovalTargets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
