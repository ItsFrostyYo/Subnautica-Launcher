using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Versions;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Mods;

public static class ModInstallerService
{
    private static readonly HttpClient Http = BuildClient();
    private static readonly Regex ModVersionRegex = new(@"v(?<version>\d+(?:\.\d+){2,3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Sn2ModsTxtLineRegex = new(
        @"^\s*(?<name>[^;#][^:]*)\s*:\s*(?<enabled>[01])\s*$",
        RegexOptions.Compiled);
    private static readonly string[] Sn2BuiltInModFolderNames =
    [
        "BPML_GenericFunctions",
        "BPModLoaderMod",
        "CheatManagerEnablerMod",
        "ConsoleCommandsMod",
        "ConsoleEnablerMod",
        "Keybinds",
        "LineTraceMod",
        "shared",
        "SplitScreenMod"
    ];
    private static readonly Dictionary<string, string> Sn2FriendlyModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SN2CommandsEnablerMod"] = "SN2 Commands Enabler Mod",
        ["Kallie'sCustomSN2Commands"] = "Kallie's Custom SN2 Commands"
    };

    private sealed class Sn2ModJsonEntry
    {
        [JsonPropertyName("mod_name")]
        public string ModName { get; set; } = string.Empty;

        [JsonPropertyName("mod_enabled")]
        public bool ModEnabled { get; set; }
    }

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

        string installedModId = ManagedModFamilies.GetById(version.InstalledModId)?.Id ?? version.InstalledModId;
        return available
            .Where(mod => !string.Equals(mod.Id, installedModId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static async Task InstallBundleAsync(
        ModDefinition mod,
        LauncherGame game,
        string targetFolder,
        DepotInstallCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        string installBasePath = GetInstallBasePath(mod, targetFolder);
        string tempRoot = Path.Combine(Path.GetTempPath(), "SubnauticaLauncher", "mods", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            foreach (ModBundleDefinition bundle in mod.BundleParts)
            {
                callbacks?.OnStatus?.Invoke($"Downloading {bundle.DisplayName}...");
                callbacks?.OnOutput?.Invoke($"Downloading mod bundle from {bundle.DownloadUrl}");

                string bundleTempRoot = Path.Combine(tempRoot, SanitizeFileName(bundle.Id));
                Directory.CreateDirectory(bundleTempRoot);

                string zipPath = Path.Combine(bundleTempRoot, bundle.BundleZipFileName);
                string extractDir = Path.Combine(bundleTempRoot, "extract");

                await DownloadBundleAsync(bundle, zipPath, callbacks, cancellationToken);

                callbacks?.OnStatus?.Invoke($"Extracting {bundle.DisplayName}...");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                string contentRoot = ResolveContentRoot(bundle, extractDir);
                ValidateExtractedBundleOrThrow(bundle, contentRoot);

                callbacks?.OnStatus?.Invoke($"Installing {bundle.DisplayName}...");
                string bundleInstallPath = GetBundleInstallPath(mod, installBasePath, bundle);
                callbacks?.OnOutput?.Invoke($"Copying mod bundle into {bundleInstallPath}");

                CleanupStaleBundleFiles(
                    mod,
                    contentRoot,
                    bundleInstallPath,
                    mod.PreservedRelativePaths,
                    mod.StaleCleanupRelativeRoots);
                CopyDirectoryContents(contentRoot, bundleInstallPath, mod.PreservedRelativePaths);
            }

            if (mod.Game == LauncherGame.Subnautica2)
                EnsureSn2ManagedModsEnabled(mod, targetFolder);

            WriteManagedVersionMarker(mod, targetFolder);

            LauncherGameProfiles.Get(game).RemoveSteamAppIdFiles(targetFolder);
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
        if (game == LauncherGame.Subnautica2)
        {
            IReadOnlyList<string> managedSn2Folders = GetManagedSn2FolderNames(version);
            if (managedSn2Folders.Count > 0)
                RemoveSn2ManagedModConfigEntries(version.HomeFolder, managedSn2Folders);
        }

        foreach (string fullPath in GetRemovalTargetsForGame(version.HomeFolder, game))
        {
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            else if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        version.HasBepInEx = false;
        version.DetectedModNames.Clear();
        version.IsModded = false;
        version.InstalledModId = string.Empty;

        LauncherGameProfiles.Get(game).RemoveSteamAppIdFiles(version.HomeFolder);
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

    public static string? GetCustomLaunchRelativePath(
        LauncherGame game,
        InstalledVersion? version,
        string versionFolder)
    {
        ManagedModFamily? family = null;

        if (!string.IsNullOrWhiteSpace(version?.InstalledModId))
            family = ManagedModFamilies.GetById(version.InstalledModId);

        if (family == null && game == LauncherGame.Subnautica)
        {
            var probeVersion = version ?? new InstalledVersion
            {
                HomeFolder = versionFolder
            };

            family = DetectKnownManagedModFamily(probeVersion, game);
        }

        if (family == null || string.IsNullOrWhiteSpace(family.CustomLaunchRelativePath))
            return null;

        string launchPath = Path.Combine(versionFolder, NormalizeRelativePath(family.CustomLaunchRelativePath));
        return File.Exists(launchPath) ? launchPath : null;
    }

    public static Version? TryReadInstalledModVersion(InstalledVersion version)
    {
        foreach (string versionFilePath in GetVersionMarkerCandidates(version))
        {
            if (!File.Exists(versionFilePath))
                continue;

            Version? versionValue = TryReadVersionFile(versionFilePath);
            if (versionValue != null)
                return versionValue;
        }

        return null;
    }

    public static void ApplyInstalledModDetection(InstalledVersion version)
    {
        LauncherGameProfile? profile = LauncherGameProfiles.DetectFromFolder(version.HomeFolder);
        if (profile == null)
        {
            version.HasBepInEx = false;
            version.DetectedModNames.Clear();
            version.IsModded = false;
            version.InstalledModId = string.Empty;
            return;
        }

        version.HasBepInEx = HasManagedRuntime(version, profile.Game);
        version.InstalledModId = DetectKnownManagedModId(version, profile.Game);
        version.DetectedModNames = DetectInstalledPluginNames(version, profile.Game).ToList();
        version.IsModded = version.HasBepInEx || version.DetectedModNames.Count > 0;

        if (!version.IsModded)
            version.InstalledModId = string.Empty;
    }

    public static string GetInstalledRuntimeDisplayName(InstalledVersion version)
    {
        LauncherGameProfile? profile = LauncherGameProfiles.DetectFromFolder(version.HomeFolder);
        return profile == null
            ? "BepInEx"
            : GetRuntimeDisplayName(profile.Game, version);
    }

    public static string GetRuntimeDisplayName(LauncherGame game, InstalledVersion? version = null)
    {
        if (!string.IsNullOrWhiteSpace(version?.InstalledModId) &&
            ManagedModFamilies.GetById(version.InstalledModId) is ManagedModFamily installedFamily)
        {
            return installedFamily.RuntimeDisplayName;
        }

        ManagedModFamily? compatibleFamily = version == null
            ? ManagedModFamilies.All.FirstOrDefault(f => f.Game == game)
            : ManagedModFamilies.GetCompatibleFamilies(game, version.OriginalDownload, version.DisplayName, version.FolderName)
                .FirstOrDefault();

        if (compatibleFamily != null)
            return compatibleFamily.RuntimeDisplayName;

        return game == LauncherGame.Subnautica2 ? "UE4SS" : "BepInEx";
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
        return client;
    }

    private static async Task DownloadBundleAsync(
        ModBundleDefinition bundle,
        string zipPath,
        DepotInstallCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(
            bundle.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream destination = File.Create(zipPath);

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

    private static string ResolveContentRoot(ModBundleDefinition bundle, string extractDir)
    {
        if (bundle.PreserveTopLevelDirectory)
            return extractDir;

        string[] directories = Directory.GetDirectories(extractDir);
        string[] files = Directory.GetFiles(extractDir);

        if (directories.Length == 1 && files.Length == 0)
            return directories[0];

        return extractDir;
    }

    private static void ValidateExtractedBundleOrThrow(ModBundleDefinition bundle, string contentRoot)
    {
        foreach (string requiredRelativePath in bundle.RequiredRelativePaths)
        {
            string fullPath = Path.Combine(contentRoot, NormalizeRelativePath(requiredRelativePath));
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
                continue;

            Logger.Warn($"[Mods] Invalid bundle for {bundle.Id}: required path '{requiredRelativePath}' missing in '{contentRoot}'.");
            throw new InvalidDataException($"Mod bundle '{bundle.BundleZipFileName}' is missing required files.");
        }
    }

    private static void ValidateInstalledBundleOrThrow(ModDefinition mod, string targetFolder)
    {
        string runtimeRoot = GetInstalledPath(targetFolder, mod.InstallRootRelativePath, mod.RuntimeRootRelativePath);
        bool detectionExists = mod.DetectionRelativePaths.All(relativePath =>
            EnumerateInstalledPaths(targetFolder, mod.InstallRootRelativePath, relativePath)
                .Any(path => Directory.Exists(path) || File.Exists(path)));
        string versionMarkerPath = ResolvePreferredInstalledPath(targetFolder, mod.InstallRootRelativePath, mod.VersionMarkerRelativePath);

        if (!Directory.Exists(runtimeRoot))
            throw new InvalidDataException($"Installed runtime '{mod.RuntimeDisplayName}' was not created.");

        if (!detectionExists)
            throw new InvalidDataException(
                $"Installed mod roots '{string.Join(", ", mod.DetectionRelativePaths)}' were not created.");

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

    private static string GetInstallBasePath(ModDefinition mod, string targetFolder)
    {
        return string.IsNullOrWhiteSpace(mod.InstallRootRelativePath)
            ? targetFolder
            : Path.Combine(targetFolder, NormalizeRelativePath(mod.InstallRootRelativePath));
    }

    private static string GetBundleInstallPath(ModDefinition mod, string installBasePath, ModBundleDefinition bundle)
    {
        if (mod.Game == LauncherGame.Subnautica2 &&
            string.Equals(NormalizeRelativePath(bundle.InstallRelativePath), "Mods", StringComparison.OrdinalIgnoreCase))
        {
            return GetSn2ManagedModsInstallPath(installBasePath);
        }

        return string.IsNullOrWhiteSpace(bundle.InstallRelativePath)
            ? installBasePath
            : Path.Combine(installBasePath, NormalizeRelativePath(bundle.InstallRelativePath));
    }

    private static string GetInstalledPath(string targetFolder, string installRootRelativePath, string relativePath)
    {
        string installBasePath = string.IsNullOrWhiteSpace(installRootRelativePath)
            ? targetFolder
            : Path.Combine(targetFolder, NormalizeRelativePath(installRootRelativePath));

        return string.IsNullOrWhiteSpace(relativePath)
            ? installBasePath
            : Path.Combine(installBasePath, NormalizeRelativePath(relativePath));
    }

    private static void WriteManagedVersionMarker(ModDefinition mod, string targetFolder)
    {
        string markerPath = ResolvePreferredInstalledPath(targetFolder, mod.InstallRootRelativePath, mod.VersionMarkerRelativePath);
        string? markerDirectory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrWhiteSpace(markerDirectory))
            Directory.CreateDirectory(markerDirectory);

        File.WriteAllText(markerPath, $"v{mod.PackageVersion}");
    }

    private static IEnumerable<string> DetectInstalledPluginNames(InstalledVersion version, LauncherGame game)
    {
        if (game == LauncherGame.Subnautica2)
        {
            ManagedModFamily? knownFamily = DetectKnownManagedModFamily(version, game);
            return DetectInstalledSn2ModNames(version, knownFamily);
        }

        ManagedModFamily? knownManagedFamily = DetectKnownManagedModFamily(version, game);
        if (knownManagedFamily != null)
            return [knownManagedFamily.DisplayName];

        return DetectInstalledBepInExPluginNames(version);
    }

    private static IEnumerable<string> DetectInstalledBepInExPluginNames(InstalledVersion version)
    {
        string pluginRoot = Path.Combine(version.HomeFolder, "BepInEx", "plugins");
        if (!Directory.Exists(pluginRoot))
            return Array.Empty<string>();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dllPath in Directory.GetFiles(pluginRoot, "*.dll", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileNameWithoutExtension(dllPath);
            string relativeDir = Path.GetRelativePath(pluginRoot, Path.GetDirectoryName(dllPath)!);
            string friendlyName = string.Equals(relativeDir, ".", StringComparison.Ordinal)
                ? fileName
                : Path.GetFileName(relativeDir);

            names.Add(friendlyName);
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> DetectInstalledSn2ModNames(InstalledVersion version, ManagedModFamily? knownFamily)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (knownFamily != null)
        {
            foreach (string displayName in knownFamily.DetectedDisplayNames)
                names.Add(displayName);
        }

        var ignoredFolderNames = new HashSet<string>(Sn2BuiltInModFolderNames, StringComparer.OrdinalIgnoreCase);
        if (knownFamily != null)
        {
            foreach (string folderName in knownFamily.ManagedModFolderNames)
                ignoredFolderNames.Add(folderName);
        }

        foreach (string modsRoot in GetSn2ModRoots(version.HomeFolder))
        {
            if (!Directory.Exists(modsRoot))
                continue;

            foreach (string directory in Directory.GetDirectories(modsRoot))
            {
                string folderName = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(folderName) ||
                    string.Equals(folderName, "Mods", StringComparison.OrdinalIgnoreCase) ||
                    ignoredFolderNames.Contains(folderName))
                {
                    continue;
                }

                names.Add(GetFriendlySn2ModName(folderName));
            }
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string DetectKnownManagedModId(InstalledVersion version, LauncherGame game)
    {
        ManagedModFamily? family = DetectKnownManagedModFamily(version, game);
        return family?.Id ?? string.Empty;
    }

    private static ManagedModFamily? DetectKnownManagedModFamily(InstalledVersion version, LauncherGame game)
    {
        IReadOnlyList<ManagedModFamily> compatibleFamilies = ManagedModFamilies.GetCompatibleFamilies(
            game,
            version.OriginalDownload,
            version.DisplayName,
            version.FolderName);

        return compatibleFamilies
            .OrderByDescending(family => family.DetectionRelativePaths.Count)
            .ThenByDescending(family => family.DetectionRelativePaths.Max(path => path.Length))
            .FirstOrDefault(family =>
            {
                return family.DetectionRelativePaths.All(relativePath =>
                    EnumerateInstalledPaths(version.HomeFolder, family.InstallRootRelativePath, relativePath)
                        .Any(path => Directory.Exists(path) || File.Exists(path)));
            });
    }

    private static IEnumerable<string> GetVersionMarkerCandidates(InstalledVersion version)
    {
        LauncherGameProfile? profile = LauncherGameProfiles.DetectFromFolder(version.HomeFolder);
        if (profile == null)
            yield break;

        ManagedModFamily? knownFamily = ManagedModFamilies.GetById(version.InstalledModId);
        if (knownFamily != null)
        {
            foreach (string path in EnumerateInstalledPaths(version.HomeFolder, knownFamily.InstallRootRelativePath, knownFamily.VersionMarkerRelativePath))
                yield return path;
        }

        foreach (ManagedModFamily family in ManagedModFamilies.GetCompatibleFamilies(
                     profile.Game,
                     version.OriginalDownload,
                     version.DisplayName,
                     version.FolderName))
        {
            if (knownFamily != null &&
                string.Equals(family.Id, knownFamily.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string path in EnumerateInstalledPaths(version.HomeFolder, family.InstallRootRelativePath, family.VersionMarkerRelativePath))
                yield return path;
        }
    }

    private static IEnumerable<string> GetRemovalTargetsForGame(string versionHomeFolder, LauncherGame game)
    {
        return ManagedModFamilies.All
            .Where(family => family.Game == game)
            .SelectMany(family => family.RemovalTargets.SelectMany(target => EnumerateInstalledPaths(versionHomeFolder, family.InstallRootRelativePath, target)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path.Length);
    }

    private static bool HasManagedRuntime(InstalledVersion version, LauncherGame game)
    {
        ManagedModFamily? knownFamily = DetectKnownManagedModFamily(version, game);
        if (knownFamily != null)
        {
            string runtimeRoot = GetInstalledPath(
                version.HomeFolder,
                knownFamily.InstallRootRelativePath,
                knownFamily.RuntimeRootRelativePath);
            return Directory.Exists(runtimeRoot) || File.Exists(runtimeRoot);
        }

        if (game == LauncherGame.Subnautica2)
        {
            string ue4ssRoot = GetInstalledPath(version.HomeFolder, @"Subnautica2\Binaries\Win64", "ue4ss");
            return Directory.Exists(ue4ssRoot);
        }

        return Directory.Exists(Path.Combine(version.HomeFolder, "BepInEx"));
    }

    private static IEnumerable<string> GetSn2ModRoots(string versionHomeFolder)
    {
        string installBasePath = GetInstalledPath(versionHomeFolder, @"Subnautica2\Binaries\Win64", string.Empty);
        yield return Path.Combine(installBasePath, "Mods");
        yield return Path.Combine(installBasePath, @"ue4ss\Mods");
    }

    private static string GetSn2ManagedModsInstallPath(string installBasePath)
    {
        return Path.Combine(installBasePath, @"ue4ss\Mods");
    }

    private static IEnumerable<string> EnumerateInstalledPaths(string targetFolder, string installRootRelativePath, string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string installBasePath = string.IsNullOrWhiteSpace(installRootRelativePath)
            ? targetFolder
            : Path.Combine(targetFolder, NormalizeRelativePath(installRootRelativePath));

        if (normalizedRelativePath.Equals("Mods", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelativePath.StartsWith(@"Mods\", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = normalizedRelativePath.Length == 4
                ? string.Empty
                : normalizedRelativePath["Mods\\".Length..];

            yield return string.IsNullOrWhiteSpace(suffix)
                ? Path.Combine(installBasePath, "Mods")
                : Path.Combine(installBasePath, "Mods", suffix);

            yield return string.IsNullOrWhiteSpace(suffix)
                ? Path.Combine(installBasePath, @"ue4ss\Mods")
                : Path.Combine(installBasePath, @"ue4ss\Mods", suffix);
            yield break;
        }

        yield return GetInstalledPath(targetFolder, installRootRelativePath, relativePath);
    }

    private static string ResolvePreferredInstalledPath(string targetFolder, string installRootRelativePath, string relativePath)
    {
        foreach (string path in EnumerateInstalledPaths(targetFolder, installRootRelativePath, relativePath))
        {
            if (Directory.Exists(path) || File.Exists(path))
                return path;
        }

        return EnumerateInstalledPaths(targetFolder, installRootRelativePath, relativePath).First();
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return value;
    }

    private static void EnsureSn2ManagedModsEnabled(ModDefinition mod, string targetFolder)
    {
        if (mod.ManagedModFolderNames.Count == 0)
            return;

        foreach (string modsRoot in GetSn2ConfigRoots(targetFolder))
        {
            Directory.CreateDirectory(modsRoot);
            EnsureSn2ModsTxtEntries(modsRoot, mod.ManagedModFolderNames);
            EnsureSn2ModsJsonEntries(modsRoot, mod.ManagedModFolderNames);
        }

        string managedModsRoot = GetSn2ManagedModsInstallPath(GetInstallBasePath(mod, targetFolder));
        foreach (string folderName in mod.ManagedModFolderNames)
        {
            string modFolderPath = Path.Combine(managedModsRoot, folderName);
            if (!Directory.Exists(modFolderPath))
                continue;

            string enabledFilePath = Path.Combine(modFolderPath, "enabled.txt");
            if (!File.Exists(enabledFilePath))
                File.WriteAllText(enabledFilePath, string.Empty);
        }
    }

    private static IReadOnlyList<string> GetManagedSn2FolderNames(InstalledVersion version)
    {
        ManagedModFamily? family = ManagedModFamilies.GetById(version.InstalledModId) ??
                                   DetectKnownManagedModFamily(version, LauncherGame.Subnautica2);
        if (family?.Game != LauncherGame.Subnautica2)
            return Array.Empty<string>();

        return family.ManagedModFolderNames;
    }

    private static IEnumerable<string> GetSn2ConfigRoots(string versionHomeFolder)
    {
        string installBasePath = GetInstalledPath(versionHomeFolder, @"Subnautica2\Binaries\Win64", string.Empty);
        yield return GetSn2ManagedModsInstallPath(installBasePath);
    }

    private static void EnsureSn2ModsTxtEntries(string modsRoot, IReadOnlyList<string> managedModFolderNames)
    {
        string modsTxtPath = Path.Combine(modsRoot, "mods.txt");
        List<string> lines = File.Exists(modsTxtPath)
            ? File.ReadAllLines(modsTxtPath).ToList()
            : new List<string>();

        foreach (string folderName in managedModFolderNames)
        {
            string enabledLine = $"{folderName} : 1";
            int existingIndex = FindSn2ModsTxtLineIndex(lines, folderName);
            if (existingIndex >= 0)
                lines[existingIndex] = enabledLine;
            else
                lines.Add(enabledLine);
        }

        File.WriteAllLines(modsTxtPath, lines);
    }

    private static void EnsureSn2ModsJsonEntries(string modsRoot, IReadOnlyList<string> managedModFolderNames)
    {
        string modsJsonPath = Path.Combine(modsRoot, "mods.json");
        List<Sn2ModJsonEntry> entries = ReadSn2ModsJsonEntries(modsJsonPath);

        foreach (string folderName in managedModFolderNames)
        {
            Sn2ModJsonEntry? existingEntry = entries.FirstOrDefault(entry =>
                string.Equals(entry.ModName, folderName, StringComparison.OrdinalIgnoreCase));
            if (existingEntry != null)
            {
                existingEntry.ModEnabled = true;
                continue;
            }

            entries.Add(new Sn2ModJsonEntry
            {
                ModName = folderName,
                ModEnabled = true
            });
        }

        WriteSn2ModsJsonEntries(modsJsonPath, entries);
    }

    private static void RemoveSn2ManagedModConfigEntries(string versionHomeFolder, IReadOnlyList<string> managedModFolderNames)
    {
        if (managedModFolderNames.Count == 0)
            return;

        foreach (string modsRoot in GetSn2ConfigRoots(versionHomeFolder))
        {
            RemoveSn2ModsTxtEntries(modsRoot, managedModFolderNames);
            RemoveSn2ModsJsonEntries(modsRoot, managedModFolderNames);
        }
    }

    private static void RemoveSn2ModsTxtEntries(string modsRoot, IReadOnlyList<string> managedModFolderNames)
    {
        string modsTxtPath = Path.Combine(modsRoot, "mods.txt");
        if (!File.Exists(modsTxtPath))
            return;

        HashSet<string> managedNames = new(managedModFolderNames, StringComparer.OrdinalIgnoreCase);
        List<string> updatedLines = File.ReadAllLines(modsTxtPath)
            .Where(line => !TryParseSn2ModsTxtLine(line, out string? modName) || modName == null || !managedNames.Contains(modName))
            .ToList();

        File.WriteAllLines(modsTxtPath, updatedLines);
    }

    private static void RemoveSn2ModsJsonEntries(string modsRoot, IReadOnlyList<string> managedModFolderNames)
    {
        string modsJsonPath = Path.Combine(modsRoot, "mods.json");
        if (!File.Exists(modsJsonPath))
            return;

        HashSet<string> managedNames = new(managedModFolderNames, StringComparer.OrdinalIgnoreCase);
        List<Sn2ModJsonEntry> updatedEntries = ReadSn2ModsJsonEntries(modsJsonPath)
            .Where(entry => !managedNames.Contains(entry.ModName))
            .ToList();

        WriteSn2ModsJsonEntries(modsJsonPath, updatedEntries);
    }

    private static List<Sn2ModJsonEntry> ReadSn2ModsJsonEntries(string modsJsonPath)
    {
        if (!File.Exists(modsJsonPath))
            return new List<Sn2ModJsonEntry>();

        try
        {
            List<Sn2ModJsonEntry>? entries = JsonSerializer.Deserialize<List<Sn2ModJsonEntry>>(File.ReadAllText(modsJsonPath));
            return entries ?? new List<Sn2ModJsonEntry>();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Mods] Failed to parse SN2 mods.json at '{modsJsonPath}': {ex.Message}");
            return new List<Sn2ModJsonEntry>();
        }
    }

    private static void WriteSn2ModsJsonEntries(string modsJsonPath, IReadOnlyList<Sn2ModJsonEntry> entries)
    {
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(modsJsonPath, json);
    }

    private static int FindSn2ModsTxtLineIndex(IReadOnlyList<string> lines, string folderName)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (TryParseSn2ModsTxtLine(lines[index], out string? existingModName) &&
                string.Equals(existingModName, folderName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryParseSn2ModsTxtLine(string line, out string? modName)
    {
        Match match = Sn2ModsTxtLineRegex.Match(line);
        if (!match.Success)
        {
            modName = null;
            return false;
        }

        modName = match.Groups["name"].Value.Trim();
        return !string.IsNullOrWhiteSpace(modName);
    }

    private static string GetFriendlySn2ModName(string folderName)
    {
        return Sn2FriendlyModNames.TryGetValue(folderName, out string? displayName)
            ? displayName
            : folderName;
    }
}
