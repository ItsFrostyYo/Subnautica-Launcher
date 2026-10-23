using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Services;

namespace SubnauticaLauncher.Mods;

public sealed class SubnauticaModService
{
    private const string BepInExVersion = "5.4.23.5";
    private const string BepInExDownloadUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";
    private const string SpeedrunningReleaseManifestUrl = "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Speedrunning-Mod/main/release/latest.json";
    private const string BepInExId = "bepinex";
    private const string QModManagerId = "qmodmanager";
    private const string SpeedrunningModId = "speedrunning-mod";
    private static string _bepInExCatalogError = string.Empty;
    private static string _speedrunningCatalogError = string.Empty;
    private static DownloadableModPackage? _cachedBepInExPackage;
    private static DownloadableModPackage? _cachedSpeedrunningPackage;
    private static DateTime _speedrunningCatalogCheckedUtc;

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly HashSet<string> VanillaRootFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MonoBleedingEdge",
        "SNAppData",
        "Subnautica_Data",
        "AssetBundles",
        "OST",
        "SNUnmanagedData"
    };
    private static readonly string[] DoorstopFiles = [".doorstop_version", "doorstop_config.ini"];
    private static readonly string[] BepInExRemovalTargets = ["BepInEx", ".doorstop_version", "doorstop_config.ini", "winhttp.dll"];
    private static readonly string[] SpeedrunningRemovalTargets =
    [
        "SubnauticaSpeedrunningMod",
        "INSTALL.txt",
        "Launch Mod.cmd",
        "winhttp.dll",
        ".doorstop_version",
        "doorstop_config.ini",
        "monitor_log.txt",
        @"SNUnmanagedData\LanguageFiles\SubnauticaSpeedrunningMod.synced.txt"
    ];

    public async Task<ModInstallationSnapshot> ScanAsync(LauncherVersion version, CancellationToken cancellationToken = default)
    {
        string gameFolder = Path.GetFullPath(version.GameFolder);
        bool hasSpeedrunningMod = SpeedrunningModDetector.IsDetected(gameFolder);
        bool hasBepInEx = Directory.Exists(Path.Combine(gameFolder, "BepInEx"));
        bool hasQModsFolder = Directory.Exists(Path.Combine(gameFolder, "QMods"));
        bool hasQModManagerAssembly = FindFilesSafe(Path.Combine(gameFolder, "BepInEx"), "QModInstaller.dll").Any();
        bool hasQModManager = hasQModsFolder || hasQModManagerAssembly;
        bool hasDoorstopMarker = DoorstopFiles.Any(name => File.Exists(Path.Combine(gameFolder, name)));

        string[] unknownFolders = Directory.Exists(gameFolder)
            ? Directory.GetDirectories(gameFolder)
                .Where(path => !VanillaRootFolders.Contains(Path.GetFileName(path)))
                .Where(path => !string.Equals(Path.GetFileName(path), "BepInEx", StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(Path.GetFileName(path), "QMods", StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(Path.GetFileName(path), "SubnauticaSpeedrunningMod", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : [];

        var installed = new List<InstalledModItem>();
        if (hasSpeedrunningMod)
        {
            installed.Add(CreateSpeedrunningItem(gameFolder));
        }
        else if (hasQModManager)
        {
            installed.AddRange(ReadQMods(gameFolder));
        }
        else if (hasBepInEx)
        {
            installed.AddRange(ReadBepInExPlugins(gameFolder));
        }

        if (!hasSpeedrunningMod)
        {
            installed.AddRange(unknownFolders.Select(path => new InstalledModItem(
                "folder:" + path,
                ModItemKind.UnknownFolder,
                Path.GetFileName(path),
                "Unknown",
                "Unknown",
                "This non-standard root folder is treated as manually installed mod content.",
                path)));
        }

        SubnauticaModEnvironment environment = hasSpeedrunningMod
            ? SubnauticaModEnvironment.SpeedrunningMod
            : hasQModManager
                ? SubnauticaModEnvironment.QModManager
                : hasBepInEx
                    ? SubnauticaModEnvironment.BepInEx
                    : hasDoorstopMarker || unknownFolders.Length > 0
                        ? SubnauticaModEnvironment.Unknown
                        : SubnauticaModEnvironment.Vanilla;

        bool incompatibleWithSpeedrunning = hasSpeedrunningMod && (hasBepInEx || hasQModManager);
        string warning = incompatibleWithSpeedrunning
            ? "BepInEx or QModManager files were found beside the Subnautica Speedrunning Mod. This can prevent the speedrunning mod from working. Remove the incompatible loader files."
            : string.Empty;

        DownloadableModPackage? bepinex = await TryGetLatestBepInExAsync(cancellationToken).ConfigureAwait(false);
        DownloadableModPackage? speedrunning = IsSpeedrunningCompatible(version)
            ? await TryGetLatestSpeedrunningModAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var loaders = new List<ModLoaderItem>();
        if (hasQModManager)
        {
            loaders.Add(new ModLoaderItem(
                QModManagerId,
                "QModManager",
                "Subnautica Modding",
                ReadQModManagerVersion(gameFolder),
                "Legacy QMod loader detected and supported for inspection and removal. New QModManager installations are not provided by this launcher.",
                true,
                false,
                string.Empty));
        }
        else if (hasBepInEx)
        {
            loaders.Add(new ModLoaderItem(
                BepInExId,
                "BepInEx",
                "BepInEx Team",
                ReadAssemblyVersion(Path.Combine(gameFolder, "BepInEx", "core", "BepInEx.dll")),
                "Extensible Unity mod loader. Add compatible mod assemblies to BepInEx/plugins.",
                true,
                false,
                string.Empty));
        }
        else if (!hasSpeedrunningMod)
        {
            loaders.Add(new ModLoaderItem(
                BepInExId,
                "BepInEx",
                "BepInEx Team",
                bepinex?.Version ?? "Unavailable",
                "Installs the latest stable Windows x64 BepInEx 5 release for Subnautica.",
                false,
                bepinex is not null,
                bepinex is null ? FirstNonEmpty(_bepInExCatalogError, "The latest BepInEx release could not be retrieved.") : string.Empty));
        }

        if (!hasSpeedrunningMod && !hasBepInEx && !hasQModManager)
        {
            bool compatible = IsSpeedrunningCompatible(version);
            loaders.Add(new ModLoaderItem(
                SpeedrunningModId,
                SpeedrunningModDetector.DisplayName,
                "ItsFrostiSR",
                speedrunning?.Version ?? "Unavailable",
                "Subnautica Speedrunning Mod with Speedrunning Practice Saves, BetterRNG Save Creation for Any% BetterRNG Category, 1v1 Racing Features and Custom Practice Commands",
                false,
                compatible && speedrunning is not null,
                !compatible
                    ? "Only the September 2018 Subnautica build is supported."
                    : speedrunning is null
                        ? FirstNonEmpty(_speedrunningCatalogError, "The latest speedrunning release could not be retrieved.")
                        : string.Empty));
        }

        string status = environment switch
        {
            SubnauticaModEnvironment.Vanilla => "Vanilla - no mod loader or non-standard mod folders detected",
            SubnauticaModEnvironment.BepInEx => "Modded - BepInEx",
            SubnauticaModEnvironment.QModManager => "Modded - QModManager",
            SubnauticaModEnvironment.SpeedrunningMod => "Modded - Subnautica Speedrunning Mod",
            _ => "Modded - unknown or manual mod files detected"
        };

        return new ModInstallationSnapshot(
            gameFolder,
            environment,
            status,
            installed,
            loaders,
            hasBepInEx,
            hasQModManager,
            hasSpeedrunningMod,
            hasDoorstopMarker || unknownFolders.Length > 0,
            warning,
            Path.Combine(gameFolder, "BepInEx", "plugins"));
    }

    public async Task InstallAsync(string loaderId, LauncherVersion version, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        string gameFolder = Path.GetFullPath(version.GameFolder);
        ModInstallationSnapshot snapshot = await ScanAsync(version, cancellationToken).ConfigureAwait(false);
        if (snapshot.HasSpeedrunningMod || snapshot.HasBepInEx || snapshot.HasQModManager)
            throw new InvalidOperationException("Remove the installed mod loader before installing a different one.");

        DownloadableModPackage package = loaderId switch
        {
            BepInExId => await GetLatestBepInExAsync(cancellationToken).ConfigureAwait(false),
            SpeedrunningModId when IsSpeedrunningCompatible(version) => await GetLatestSpeedrunningModAsync(cancellationToken).ConfigureAwait(false),
            SpeedrunningModId => throw new InvalidOperationException("The Subnautica Speedrunning Mod only supports the September 2018 build."),
            _ => throw new InvalidOperationException("That mod loader is not managed by this launcher.")
        };

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "SubnauticaLauncher", "mods", Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(temporaryRoot, "package.zip");
        string extractionPath = Path.Combine(temporaryRoot, "extract");
        Directory.CreateDirectory(extractionPath);
        try
        {
            progress?.Report($"Downloading {package.Name} v{package.Version}...");
            await DownloadAsync(package.DownloadUrl, archivePath, cancellationToken).ConfigureAwait(false);
            progress?.Report($"Extracting {package.Name}...");
            ZipFile.ExtractToDirectory(archivePath, extractionPath, overwriteFiles: true);
            string contentRoot = ResolveContentRoot(extractionPath, package.RequiredPaths);
            ValidateRequiredPaths(contentRoot, package.RequiredPaths, package.Name);
            progress?.Report($"Installing {package.Name}...");
            CopyDirectory(contentRoot, gameFolder);

            if (loaderId == SpeedrunningModId)
            {
                string marker = Path.Combine(gameFolder, "SubnauticaSpeedrunningMod", "launcher.version.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                File.WriteAllText(marker, "v" + package.Version);
            }

            progress?.Report($"{package.Name} v{package.Version} installed.");
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    public void RemoveInstalledMod(ModInstallationSnapshot snapshot, string itemId)
    {
        InstalledModItem item = snapshot.InstalledMods.FirstOrDefault(candidate => candidate.Id == itemId)
            ?? throw new InvalidOperationException("The selected installed mod could no longer be found.");
        if (item.Kind == ModItemKind.SpeedrunningMod)
        {
            IEnumerable<string> targets = snapshot.HasBepInEx || snapshot.HasQModManager
                ? SpeedrunningRemovalTargets.Where(path => path is not "winhttp.dll" and not ".doorstop_version" and not "doorstop_config.ini")
                : SpeedrunningRemovalTargets;
            DeleteRelativeTargets(snapshot.GameFolder, targets);
            return;
        }
        DeleteWithinGameFolder(snapshot.GameFolder, item.RemovalPath);
    }

    public void RemoveLoader(ModInstallationSnapshot snapshot, string loaderId)
    {
        if (loaderId == QModManagerId)
        {
            DeleteRelativeTargets(snapshot.GameFolder, snapshot.HasSpeedrunningMod
                ? ["QMods", "BepInEx"]
                : ["QMods", "BepInEx", ".doorstop_version", "doorstop_config.ini", "winhttp.dll"]);
            return;
        }

        if (loaderId == BepInExId)
        {
            DeleteRelativeTargets(snapshot.GameFolder, snapshot.HasSpeedrunningMod ? ["BepInEx"] : BepInExRemovalTargets);
            return;
        }

        if (loaderId == SpeedrunningModId)
        {
            DeleteRelativeTargets(snapshot.GameFolder, SpeedrunningRemovalTargets);
            return;
        }

        throw new InvalidOperationException("That mod loader is not managed by this launcher.");
    }

    public void RemoveIncompatibleLoaders(ModInstallationSnapshot snapshot)
    {
        DeleteWithinGameFolder(snapshot.GameFolder, Path.Combine(snapshot.GameFolder, "BepInEx"));
        DeleteWithinGameFolder(snapshot.GameFolder, Path.Combine(snapshot.GameFolder, "QMods"));
    }

    public static void OpenBepInExPluginsFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private static IEnumerable<InstalledModItem> ReadBepInExPlugins(string gameFolder)
    {
        string pluginRoot = Path.Combine(gameFolder, "BepInEx", "plugins");
        if (!Directory.Exists(pluginRoot)) yield break;

        foreach (string directory in Directory.GetDirectories(pluginRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string[] assemblies = FindFilesSafe(directory, "*.dll").ToArray();
            string metadataAssembly = assemblies.FirstOrDefault() ?? string.Empty;
            yield return CreateAssemblyModItem(
                "bepinex-folder:" + directory,
                ModItemKind.BepInExPlugin,
                Path.GetFileName(directory),
                metadataAssembly,
                directory);
        }

        foreach (string assembly in Directory.GetFiles(pluginRoot, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            yield return CreateAssemblyModItem(
                "bepinex-file:" + assembly,
                ModItemKind.BepInExPlugin,
                Path.GetFileNameWithoutExtension(assembly),
                assembly,
                assembly);
        }
    }

    private static IEnumerable<InstalledModItem> ReadQMods(string gameFolder)
    {
        string qmodsRoot = Path.Combine(gameFolder, "QMods");
        if (!Directory.Exists(qmodsRoot)) yield break;

        foreach (string directory in Directory.GetDirectories(qmodsRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string fallbackName = Path.GetFileName(directory);
            string manifestPath = Path.Combine(directory, "mod.json");
            QModManifest? manifest = null;
            try
            {
                if (File.Exists(manifestPath))
                    manifest = JsonSerializer.Deserialize<QModManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // Malformed third-party metadata should not hide an installed mod.
            }

            yield return new InstalledModItem(
                "qmod:" + directory,
                ModItemKind.QMod,
                FirstNonEmpty(manifest?.DisplayName, manifest?.Id, fallbackName),
                FirstNonEmpty(manifest?.Author, "Unknown"),
                FirstNonEmpty(manifest?.Version, "Unknown"),
                FirstNonEmpty(manifest?.Description, "Legacy QMod installed in the QMods folder."),
                directory);
        }
    }

    private static InstalledModItem CreateAssemblyModItem(string id, ModItemKind kind, string fallbackName, string assemblyPath, string removalPath)
    {
        FileVersionInfo? info = null;
        try { if (File.Exists(assemblyPath)) info = FileVersionInfo.GetVersionInfo(assemblyPath); } catch { }
        return new InstalledModItem(
            id,
            kind,
            FirstNonEmpty(info?.ProductName, info?.FileDescription, fallbackName),
            FirstNonEmpty(info?.CompanyName, "Unknown"),
            FirstNonEmpty(info?.ProductVersion, info?.FileVersion, ReadAssemblyVersion(assemblyPath)),
            FirstNonEmpty(info?.Comments, info?.FileDescription, "BepInEx plugin detected from its installed assembly."),
            removalPath);
    }

    private static InstalledModItem CreateSpeedrunningItem(string gameFolder)
    {
        string marker = Path.Combine(gameFolder, "SubnauticaSpeedrunningMod", "launcher.version.txt");
        string version = File.Exists(marker) ? File.ReadAllText(marker).Trim().TrimStart('v', 'V') : "Unknown";
        if (version == "Unknown")
        {
            string assembly = FindFilesSafe(Path.Combine(gameFolder, "SubnauticaSpeedrunningMod"), "*.dll").FirstOrDefault() ?? string.Empty;
            version = ReadAssemblyVersion(assembly);
        }
        string assemblyVersion = ReadAssemblyVersion(Path.Combine(gameFolder, "SubnauticaSpeedrunningMod", "Runtime", "SubnauticaSpeedrunningMod.Runtime.dll"));
        if (!string.Equals(assemblyVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
            version = assemblyVersion;

        return new InstalledModItem(
            SpeedrunningModId,
            ModItemKind.SpeedrunningMod,
            SpeedrunningModDetector.DisplayName,
            "ItsFrostiSR",
            version,
            "Subnautica Speedrunning Mod with Speedrunning Practice Saves, BetterRNG Save Creation for Any% BetterRNG Category, 1v1 Racing Features and Custom Practice Commands",
            Path.Combine(gameFolder, "SubnauticaSpeedrunningMod"));
    }

    private static async Task<DownloadableModPackage> GetLatestBepInExAsync(CancellationToken cancellationToken)
        => await TryGetLatestBepInExAsync(cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException("The latest compatible BepInEx release could not be found.");

    private static async Task<DownloadableModPackage?> TryGetLatestBepInExAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        _bepInExCatalogError = string.Empty;
        return _cachedBepInExPackage ??= new DownloadableModPackage(
            BepInExId,
            "BepInEx",
            BepInExVersion,
            BepInExDownloadUrl,
            ["BepInEx", "winhttp.dll", "doorstop_config.ini"]);
    }

    private static async Task<DownloadableModPackage> GetLatestSpeedrunningModAsync(CancellationToken cancellationToken)
        => await TryGetLatestSpeedrunningModAsync(cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException("The latest Subnautica Speedrunning Mod release could not be found.");

    private static async Task<DownloadableModPackage?> TryGetLatestSpeedrunningModAsync(CancellationToken cancellationToken)
    {
        if (_cachedSpeedrunningPackage is not null && DateTime.UtcNow - _speedrunningCatalogCheckedUtc < TimeSpan.FromMinutes(10)) return _cachedSpeedrunningPackage;
        try
        {
            DownloadableModPackage? package = await ReadSpeedrunningManifestAsync(cancellationToken).ConfigureAwait(false);
            _speedrunningCatalogError = package is null ? "The latest speedrunning release has no compatible package." : string.Empty;
            _cachedSpeedrunningPackage = package;
            _speedrunningCatalogCheckedUtc = DateTime.UtcNow;
            return _cachedSpeedrunningPackage;
        }
        catch (Exception exception) { _speedrunningCatalogError = exception.Message; return null; }
    }

    private static async Task<DownloadableModPackage?> ReadSpeedrunningManifestAsync(CancellationToken cancellationToken)
    {
        using Stream stream = await Http.GetStreamAsync(SpeedrunningReleaseManifestUrl, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string version = root.TryGetProperty("version", out JsonElement versionElement) ? versionElement.GetString() ?? string.Empty : string.Empty;
        string assetName = root.TryGetProperty("zipFileName", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
        string downloadUrl = root.TryGetProperty("zipUrl", out JsonElement urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
        if (!assetName.StartsWith("SubnauticaSpeedrunningMod-Beta-", StringComparison.OrdinalIgnoreCase) ||
            !assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            !downloadUrl.StartsWith("https://github.com/ItsFrostyYo/Subnautica-Speedrunning-Mod/releases/download/", StringComparison.OrdinalIgnoreCase))
            return null;

        return new DownloadableModPackage(
            SpeedrunningModId,
            SpeedrunningModDetector.DisplayName,
            version.Trim().Replace("Beta-", string.Empty, StringComparison.OrdinalIgnoreCase),
            downloadUrl,
            ["SubnauticaSpeedrunningMod", "Launch Mod.cmd", "winhttp.dll", ".doorstop_version", "doorstop_config.ini"]);
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveContentRoot(string extractionRoot, IReadOnlyList<string> requiredPaths)
    {
        if (requiredPaths.All(path => Exists(Path.Combine(extractionRoot, path)))) return extractionRoot;
        string[] directories = Directory.GetDirectories(extractionRoot);
        return directories.Length == 1 && requiredPaths.All(path => Exists(Path.Combine(directories[0], path)))
            ? directories[0]
            : extractionRoot;
    }

    private static void ValidateRequiredPaths(string root, IReadOnlyList<string> requiredPaths, string packageName)
    {
        string? missing = requiredPaths.FirstOrDefault(path => !Exists(Path.Combine(root, path)));
        if (missing is not null) throw new InvalidDataException($"The downloaded {packageName} package is missing '{missing}'.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteRelativeTargets(string gameFolder, IEnumerable<string> relativeTargets)
    {
        foreach (string relative in relativeTargets)
            DeleteWithinGameFolder(gameFolder, Path.Combine(gameFolder, relative));
    }

    private static void DeleteWithinGameFolder(string gameFolder, string target)
    {
        string root = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to remove a path outside the selected Subnautica installation.");
        if (Directory.Exists(fullTarget)) Directory.Delete(fullTarget, recursive: true);
        else if (File.Exists(fullTarget)) File.Delete(fullTarget);
    }

    private static string ReadQModManagerVersion(string gameFolder)
    {
        string assembly = FindFilesSafe(Path.Combine(gameFolder, "BepInEx"), "QModInstaller.dll").FirstOrDefault() ?? string.Empty;
        return ReadAssemblyVersion(assembly);
    }

    private static string ReadAssemblyVersion(string path)
    {
        if (!File.Exists(path)) return "Unknown";
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return NormalizeVersionLabel(FirstNonEmpty(info.ProductVersion, info.FileVersion, "Unknown"));
        }
        catch { return "Unknown"; }
    }

    private static string NormalizeVersionLabel(string value)
    {
        int metadata = value.IndexOf('+');
        return metadata > 0 ? value[..metadata] : value;
    }

    private static IEnumerable<string> FindFilesSafe(string root, string pattern)
    {
        if (!Directory.Exists(root)) return [];
        try { return Directory.GetFiles(root, pattern, SearchOption.AllDirectories); }
        catch { return []; }
    }

    private static bool IsSpeedrunningCompatible(LauncherVersion version) =>
        string.Equals(version.OriginalVersionId, "Subnautica_Sep2018", StringComparison.OrdinalIgnoreCase) ||
        version.OriginalVersionDisplayName.Contains("September 2018", StringComparison.OrdinalIgnoreCase) ||
        version.DisplayName.Contains("September 2018", StringComparison.OrdinalIgnoreCase);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Unknown";

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher-ModManager");
        return client;
    }

    private sealed class QModManifest
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
