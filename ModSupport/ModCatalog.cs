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

    private sealed record CatalogEntry(string Name, string Path, string DownloadUrl);

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

        ManagedModFamily? knownFamily = ManagedModFamilies.GetById(modId);
        string resolvedId = knownFamily?.Id ?? modId;

        lock (Sync)
        {
            return AllMods.FirstOrDefault(mod => string.Equals(mod.Id, resolvedId, StringComparison.OrdinalIgnoreCase));
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
        string apiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/git/trees/{Branch}?recursive=1";
        using HttpResponseMessage response = await Http.GetAsync(apiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var entries = new List<CatalogEntry>();
        if (!document.RootElement.TryGetProperty("tree", out JsonElement tree))
            return entries;

        foreach (JsonElement item in tree.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out JsonElement typeProp) ||
                !string.Equals(typeProp.GetString(), "blob", StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetProperty("path", out JsonElement pathProp))
            {
                continue;
            }

            string? path = pathProp.GetString();
            if (string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase) ||
                !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = Path.GetFileName(path);
            string downloadUrl = $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/{path}";
            if (string.IsNullOrWhiteSpace(name))
                continue;

            entries.Add(new CatalogEntry(name, path, downloadUrl));
        }

        return entries;
    }

    private static ModDefinition? ResolveLatest(
        IReadOnlyList<CatalogEntry> entries,
        ManagedModFamily family)
    {
        List<ModBundleDefinition> bundles = new();
        Version? packageVersion = null;

        foreach (ManagedModBundlePart bundlePart in family.BundleParts)
        {
            var match = entries
                .Select(entry =>
                {
                    var regexMatch = bundlePart.BundleFileNamePattern.Match(entry.Name);
                    if (!regexMatch.Success)
                        return null;

                    string versionText = regexMatch.Groups["version"].Value.Replace('-', '.');
                    if (!Version.TryParse(versionText, out Version? version))
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

            bundles.Add(new ModBundleDefinition
            {
                Id = bundlePart.Id,
                DisplayName = bundlePart.DisplayName,
                BundleZipFileName = match.Entry.Name,
                DownloadUrl = match.Entry.DownloadUrl,
                InstallRelativePath = bundlePart.InstallRelativePath,
                PreserveTopLevelDirectory = bundlePart.PreserveTopLevelDirectory,
                RequiredRelativePaths = bundlePart.RequiredRelativePaths
            });

            if (bundlePart.ProvidesPackageVersion)
                packageVersion = match.Version;
        }

        if (packageVersion == null)
            return null;

        return new ModDefinition
        {
            Id = family.Id,
            DisplayName = family.DisplayName,
            Game = family.Game,
            PackageVersion = packageVersion,
            RuntimeDisplayName = family.RuntimeDisplayName,
            InstallRootRelativePath = family.InstallRootRelativePath,
            RuntimeRootRelativePath = family.RuntimeRootRelativePath,
            DetectionRelativePaths = family.DetectionRelativePaths,
            VersionMarkerRelativePath = family.VersionMarkerRelativePath,
            BundleParts = bundles,
            RemovalTargets = family.RemovalTargets,
            PreservedRelativePaths = family.PreservedRelativePaths,
            StaleCleanupRelativeRoots = family.StaleCleanupRelativeRoots,
            DetectedDisplayNames = family.DetectedDisplayNames,
            ManagedModFolderNames = family.ManagedModFolderNames
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
