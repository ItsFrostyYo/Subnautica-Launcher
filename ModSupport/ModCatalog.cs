using SubnauticaLauncher.Enums;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Mods;

public static class ModCatalog
{
    private const string Owner = "ItsFrostyYo";
    private const string Repo = "Subnautica-Launcher";
    private const string Branch = "master";
    private static readonly HttpClient Http = BuildClient();
    private static readonly object Sync = new();

    private static readonly Regex LegacyBundleRegex = new(
        @"^Assembly-CheatSharp\.v(?<version>\d+\.\d+\.\d+)\+BepInEx_.*\.zip$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModernBundleRegex = new(
        @"^Assembly-CheatSharp\.v(?<version>\d+\.\d+\.\d+)-Subnautica2025\+BepInEx_.*\.zip$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] CommonRemovalTargets =
    {
        "BepInEx",
        ".doorstop_version",
        "doorstop_config.ini",
        "winhttp.dll",
        "changelog.txt"
    };

    private static readonly string[] PreservedPaths =
    {
        @"BepInEx\plugins\Assembly-CheatSharp\Options.txt",
        @"BepInEx\plugins\Assembly-CheatSharp\Presets\Custom.preset",
        @"BepInEx\plugins\Assembly-CheatSharp\Presets\Custom.SpawnLoc"
    };

    private sealed record CatalogEntry(string Name, string DownloadUrl);

    public static IReadOnlyList<ModDefinition> AllMods { get; private set; } = Array.Empty<ModDefinition>();

    public static async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CatalogEntry> files = await FetchEntriesAsync(cancellationToken).ConfigureAwait(false);

        var resolvedMods = new List<ModDefinition>(2);

        ModDefinition? legacy = ResolveLatest(
            files,
            LegacyBundleRegex,
            "SpeedrunRng",
            "Speedrun RNG Mod");
        if (legacy != null)
            resolvedMods.Add(legacy);

        ModDefinition? modern = ResolveLatest(
            files,
            ModernBundleRegex,
            "SpeedrunRng20Plus",
            "Speedrun RNG Mod 2.0+");
        if (modern != null)
            resolvedMods.Add(modern);

        lock (Sync)
        {
            AllMods = resolvedMods;
        }
    }

    public static void EnsureLoaded()
    {
        lock (Sync)
        {
            if (AllMods.Count > 0)
                return;
        }

        RefreshAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public static ModDefinition? GetById(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return null;

        EnsureLoaded();
        return AllMods.FirstOrDefault(mod => string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetDisplayName(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return string.Empty;

        string? knownName = modId switch
        {
            "SpeedrunRng" => "Speedrun RNG Mod",
            "SpeedrunRng20Plus" => "Speedrun RNG Mod 2.0+",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(knownName))
            return knownName;

        ModDefinition? mod = GetById(modId);
        if (mod != null)
            return mod.DisplayName;

        return string.Empty;
    }

    private static async Task<IReadOnlyList<CatalogEntry>> FetchEntriesAsync(CancellationToken cancellationToken)
    {
        string apiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/contents/Mods?ref={Branch}";
        using HttpResponseMessage response = await Http.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var entries = new List<CatalogEntry>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out JsonElement nameProp) ||
                !item.TryGetProperty("download_url", out JsonElement downloadProp))
            {
                continue;
            }

            string? name = nameProp.GetString();
            string? downloadUrl = downloadProp.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
                continue;

            entries.Add(new CatalogEntry(name, downloadUrl));
        }

        return entries;
    }

    private static ModDefinition? ResolveLatest(
        IReadOnlyList<CatalogEntry> entries,
        Regex pattern,
        string modId,
        string displayName)
    {
        var match = entries
            .Select(entry =>
            {
                Match regexMatch = pattern.Match(entry.Name);
                if (!regexMatch.Success)
                    return null;

                if (!Version.TryParse(regexMatch.Groups["version"].Value, out Version? version))
                    return null;

                return new
                {
                    Entry = entry,
                    Version = version
                };
            })
            .Where(item => item != null)
            .OrderByDescending(item => item!.Version)
            .FirstOrDefault();

        if (match == null)
            return null;

        return new ModDefinition
        {
            Id = modId,
            DisplayName = displayName,
            Game = LauncherGame.Subnautica,
            PackageVersion = match.Version,
            BundleZipFileName = match.Entry.Name,
            DownloadUrl = match.Entry.DownloadUrl,
            RemovalTargets = CommonRemovalTargets,
            PreservedRelativePaths = PreservedPaths
        };
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(8);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
        return client;
    }
}
