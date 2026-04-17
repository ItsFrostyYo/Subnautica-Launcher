using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Versions;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Mods;

internal sealed class ManagedModFamily
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required LauncherGame Game { get; init; }
    public required Regex BundleFileNamePattern { get; init; }
    public required Func<string?, string?, string?, bool> CompatibilityPredicate { get; init; }
    public required string PluginRootRelativePath { get; init; }
    public required string VersionMarkerRelativePath { get; init; }
    public required string ManagedPluginFileNameToken { get; init; }
    public required IReadOnlyList<string> RemovalTargets { get; init; }
    public required IReadOnlyList<string> PreservedRelativePaths { get; init; }
    public required IReadOnlyList<string> StaleCleanupRelativeRoots { get; init; }

    public bool SupportsVersion(string? originalDownload, string? displayName, string? folderName)
    {
        return CompatibilityPredicate(originalDownload, displayName, folderName);
    }

    public bool SupportsVersion(InstalledVersion version)
    {
        return SupportsVersion(version.OriginalDownload, version.DisplayName, version.FolderName);
    }
}

internal static class ManagedModFamilies
{
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

    private static readonly string[] StaleCleanupRoots =
    {
        @"BepInEx\plugins\Assembly-CheatSharp\Presets"
    };

    public static ManagedModFamily SpeedrunRng { get; } = new()
    {
        Id = "SpeedrunRng",
        DisplayName = "Speedrun RNG Mod",
        Game = LauncherGame.Subnautica,
        BundleFileNamePattern = new Regex(
            @"^Assembly-CheatSharp\.v(?<version>\d+\.\d+\.\d+)\+BepInEx_.*\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        CompatibilityPredicate = (originalDownload, displayName, folderName) =>
            ContainsYear(originalDownload, 2018) ||
            ContainsYear(displayName, 2018) ||
            ContainsYear(folderName, 2018),
        PluginRootRelativePath = @"BepInEx\plugins\Assembly-CheatSharp",
        VersionMarkerRelativePath = @"BepInEx\plugins\Assembly-CheatSharp\Presets\version.txt",
        ManagedPluginFileNameToken = "Assembly-CheatSharp",
        RemovalTargets = CommonRemovalTargets,
        PreservedRelativePaths = PreservedPaths,
        StaleCleanupRelativeRoots = StaleCleanupRoots
    };

    public static ManagedModFamily SpeedrunRng20Plus { get; } = new()
    {
        Id = "SpeedrunRng20Plus",
        DisplayName = "Speedrun RNG Mod 2.0+",
        Game = LauncherGame.Subnautica,
        BundleFileNamePattern = new Regex(
            @"^Assembly-CheatSharp\.v(?<version>\d+\.\d+\.\d+)-Subnautica2025\+BepInEx_.*\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        CompatibilityPredicate = (originalDownload, displayName, folderName) =>
            MatchesYearRange(originalDownload, 2022, 2025) ||
            MatchesYearRange(displayName, 2022, 2025) ||
            MatchesYearRange(folderName, 2022, 2025),
        PluginRootRelativePath = @"BepInEx\plugins\Assembly-CheatSharp",
        VersionMarkerRelativePath = @"BepInEx\plugins\Assembly-CheatSharp\Presets\version.txt",
        ManagedPluginFileNameToken = "Assembly-CheatSharp",
        RemovalTargets = CommonRemovalTargets,
        PreservedRelativePaths = PreservedPaths,
        StaleCleanupRelativeRoots = StaleCleanupRoots
    };

    public static IReadOnlyList<ManagedModFamily> All { get; } =
    [
        SpeedrunRng,
        SpeedrunRng20Plus
    ];

    public static ManagedModFamily? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return All.FirstOrDefault(family => string.Equals(family.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<ManagedModFamily> GetCompatibleFamilies(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        return All
            .Where(family => family.Game == game)
            .Where(family => family.SupportsVersion(originalDownload, displayName, folderName))
            .ToList();
    }

    private static bool ContainsYear(string? value, int year)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(year.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesYearRange(string? value, int minYear, int maxYear)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        for (int year = minYear; year <= maxYear; year++)
        {
            if (value.Contains(year.ToString(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
