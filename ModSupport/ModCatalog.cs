using SubnauticaLauncher.Enums;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SubnauticaLauncher.Mods;

public static class ModCatalog
{
    private const string Owner = "ItsFrostyYo";
    private const string Repo = "Subnautica-Launcher";
    private const string Branch = "master";
    private static readonly HttpClient Http = BuildClient();
    private static readonly object Sync = new();

    private sealed record CatalogEntry(string Name, string DownloadUrl);

    public static IReadOnlyList<ModDefinition> AllMods { get; private set; } = Array.Empty<ModDefinition>();
    private static Task? _inflightRefreshTask;

    public static Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (Sync)
        {
            if (AllMods.Count > 0)
                return Task.CompletedTask;

            _inflightRefreshTask ??= RefreshCoreAsync(cancellationToken);
            return _inflightRefreshTask;
        }
    }

    public static Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (Sync)
        {
            _inflightRefreshTask ??= RefreshCoreAsync(cancellationToken);
            return _inflightRefreshTask;
        }
    }

    public static ModDefinition? GetById(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return null;

        lock (Sync)
        {
            return AllMods.FirstOrDefault(mod => string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static string GetDisplayName(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return string.Empty;

        ManagedModFamily? knownFamily = ManagedModFamilies.GetById(modId);
        if (knownFamily != null)
            return knownFamily.DisplayName;

        ModDefinition? mod = GetById(modId);
        if (mod != null)
            return mod.DisplayName;

        return string.Empty;
    }

    public static IReadOnlyList<ModDefinition> GetSnapshot()
    {
        lock (Sync)
        {
            return AllMods.ToArray();
        }
    }

    private static async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CatalogEntry> files = await FetchEntriesAsync(cancellationToken).ConfigureAwait(false);

            var resolvedMods = ManagedModFamilies.All
                .Select(family => ResolveLatest(files, family))
                .Where(mod => mod != null)
                .Cast<ModDefinition>()
                .ToList();

            lock (Sync)
            {
                AllMods = resolvedMods;
            }
        }
        finally
        {
            lock (Sync)
            {
                _inflightRefreshTask = null;
            }
        }
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
        ManagedModFamily family)
    {
        var match = entries
            .Select(entry =>
            {
                var regexMatch = family.BundleFileNamePattern.Match(entry.Name);
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
            Id = family.Id,
            DisplayName = family.DisplayName,
            Game = family.Game,
            PackageVersion = match.Version,
            BundleZipFileName = match.Entry.Name,
            DownloadUrl = match.Entry.DownloadUrl,
            PluginRootRelativePath = family.PluginRootRelativePath,
            VersionMarkerRelativePath = family.VersionMarkerRelativePath,
            RemovalTargets = family.RemovalTargets,
            PreservedRelativePaths = family.PreservedRelativePaths,
            StaleCleanupRelativeRoots = family.StaleCleanupRelativeRoots
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
