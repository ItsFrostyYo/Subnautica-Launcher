using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Versions;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Mods;

internal sealed class ManagedModBundlePart
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required Regex BundleFileNamePattern { get; init; }
    public required bool ProvidesPackageVersion { get; init; }
    public required string InstallRelativePath { get; init; }
    public required bool PreserveTopLevelDirectory { get; init; }
    public required IReadOnlyList<string> RequiredRelativePaths { get; init; }
}

internal sealed class ManagedModFamily
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required LauncherGame Game { get; init; }
    public required Func<string?, string?, string?, bool> CompatibilityPredicate { get; init; }
    public required string RuntimeDisplayName { get; init; }
    public required string InstallRootRelativePath { get; init; }
    public required string RuntimeRootRelativePath { get; init; }
    public required IReadOnlyList<string> DetectionRelativePaths { get; init; }
    public required string VersionMarkerRelativePath { get; init; }
    public required IReadOnlyList<ManagedModBundlePart> BundleParts { get; init; }
    public required IReadOnlyList<string> RemovalTargets { get; init; }
    public required IReadOnlyList<string> PreservedRelativePaths { get; init; }
    public required IReadOnlyList<string> StaleCleanupRelativeRoots { get; init; }
    public required IReadOnlyList<string> DetectedDisplayNames { get; init; }
    public required IReadOnlyList<string> ManagedModFolderNames { get; init; }

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
    private static readonly string[] CommonBepInExRemovalTargets =
    {
        "BepInEx",
        ".doorstop_version",
        "doorstop_config.ini",
        "winhttp.dll",
        "changelog.txt"
    };

    private static readonly string[] SpeedrunPreservedPaths =
    {
        @"BepInEx\plugins\Assembly-CheatSharp\Options.txt",
        @"BepInEx\plugins\Assembly-CheatSharp\Presets\Custom.preset",
        @"BepInEx\plugins\Assembly-CheatSharp\Presets\Custom.SpawnLoc"
    };

    private static readonly string[] SpeedrunCleanupRoots =
    {
        @"BepInEx\plugins\Assembly-CheatSharp\Presets"
    };

    private static readonly string[] CommonSn2RemovalTargets =
    {
        "ue4ss",
        "dwmapi.dll",
        "UE4SS.log",
        @"ue4ss\UE4SS.log",
        @"Mods\SN2CommandsEnablerMod",
        @"Mods\Kallie'sCustomSN2Commands"
    };

    private static readonly string[] Sn2CleanupRoots = Array.Empty<string>();

    private static readonly ManagedModBundlePart SpeedrunBundle = new()
    {
        Id = "SpeedrunRng.Bundle",
        DisplayName = "Speedrun RNG Mod",
        BundleFileNamePattern = new Regex(
            @"^Assembly-CheatSharp\.v(?<version>\d+\.\d+\.\d+)\+BepInEx_.*\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ProvidesPackageVersion = true,
        InstallRelativePath = string.Empty,
        PreserveTopLevelDirectory = false,
        RequiredRelativePaths =
        [
            "BepInEx",
            "winhttp.dll",
            @"BepInEx\plugins\Assembly-CheatSharp"
        ]
    };

    private static readonly ManagedModBundlePart Speedrun20PlusBundle = new()
    {
        Id = "SpeedrunRng20Plus.Bundle",
        DisplayName = "Speedrun RNG Mod 2.0+",
        BundleFileNamePattern = new Regex(
            @"^Assembly-CheatSharp\.v(?<version>\d+\.\d+\.\d+)-Subnautica2025\+BepInEx_.*\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ProvidesPackageVersion = true,
        InstallRelativePath = string.Empty,
        PreserveTopLevelDirectory = false,
        RequiredRelativePaths =
        [
            "BepInEx",
            "winhttp.dll",
            @"BepInEx\plugins\Assembly-CheatSharp"
        ]
    };

    private static readonly ManagedModBundlePart Sn2RuntimeBundle = new()
    {
        Id = "Subnautica2.Ue4ssRuntime",
        DisplayName = "UE4SS Runtime",
        BundleFileNamePattern = new Regex(
            @"^UE4SS for Subnautica 2-\d+-(?<version>\d+-\d+-\d+)-\d+\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ProvidesPackageVersion = false,
        InstallRelativePath = string.Empty,
        PreserveTopLevelDirectory = false,
        RequiredRelativePaths =
        [
            "dwmapi.dll",
            "ue4ss",
            @"ue4ss\UE4SS.dll",
            @"ue4ss\Mods\mods.txt",
            @"ue4ss\Mods\BPModLoaderMod"
        ]
    };

    private static readonly ManagedModBundlePart Sn2CommandsEnablerBundle = new()
    {
        Id = "Subnautica2.CommandsEnabler",
        DisplayName = "SN2 Commands Enabler Mod",
        BundleFileNamePattern = new Regex(
            @"^SN2CommandsEnablerMod-\d+-(?<version>\d+-\d+-\d+)-\d+\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ProvidesPackageVersion = true,
        InstallRelativePath = "Mods",
        PreserveTopLevelDirectory = true,
        RequiredRelativePaths =
        [
            "SN2CommandsEnablerMod",
            @"SN2CommandsEnablerMod\enabled.txt",
            @"SN2CommandsEnablerMod\Scripts\main.lua"
        ]
    };

    private static readonly ManagedModBundlePart Sn2CustomCommandsBundle = new()
    {
        Id = "Subnautica2.CustomCommands",
        DisplayName = "Kallie's Custom SN2 Commands",
        BundleFileNamePattern = new Regex(
            @"^Kallie'sCustomSN2Commands-\d+-(?<version>\d+-\d+-\d+)-\d+\.zip$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ProvidesPackageVersion = true,
        InstallRelativePath = "Mods",
        PreserveTopLevelDirectory = true,
        RequiredRelativePaths =
        [
            "Kallie'sCustomSN2Commands",
            @"Kallie'sCustomSN2Commands\Scripts\main.lua"
        ]
    };

    public static ManagedModFamily SpeedrunRng { get; } = new()
    {
        Id = "SpeedrunRng",
        DisplayName = "Speedrun RNG Mod",
        Game = LauncherGame.Subnautica,
        CompatibilityPredicate = (originalDownload, displayName, folderName) =>
            ContainsYear(originalDownload, 2018) ||
            ContainsYear(displayName, 2018) ||
            ContainsYear(folderName, 2018),
        RuntimeDisplayName = "BepInEx",
        InstallRootRelativePath = string.Empty,
        RuntimeRootRelativePath = "BepInEx",
        DetectionRelativePaths = [@"BepInEx\plugins\Assembly-CheatSharp"],
        VersionMarkerRelativePath = @"BepInEx\plugins\Assembly-CheatSharp\Presets\version.txt",
        BundleParts = [SpeedrunBundle],
        RemovalTargets = CommonBepInExRemovalTargets,
        PreservedRelativePaths = SpeedrunPreservedPaths,
        StaleCleanupRelativeRoots = SpeedrunCleanupRoots,
        DetectedDisplayNames = ["Speedrun RNG Mod"],
        ManagedModFolderNames = Array.Empty<string>()
    };

    public static ManagedModFamily SpeedrunRng20Plus { get; } = new()
    {
        Id = "SpeedrunRng20Plus",
        DisplayName = "Speedrun RNG Mod 2.0+",
        Game = LauncherGame.Subnautica,
        CompatibilityPredicate = (originalDownload, displayName, folderName) =>
            MatchesYearRange(originalDownload, 2022, 2025) ||
            MatchesYearRange(displayName, 2022, 2025) ||
            MatchesYearRange(folderName, 2022, 2025),
        RuntimeDisplayName = "BepInEx",
        InstallRootRelativePath = string.Empty,
        RuntimeRootRelativePath = "BepInEx",
        DetectionRelativePaths = [@"BepInEx\plugins\Assembly-CheatSharp"],
        VersionMarkerRelativePath = @"BepInEx\plugins\Assembly-CheatSharp\Presets\version.txt",
        BundleParts = [Speedrun20PlusBundle],
        RemovalTargets = CommonBepInExRemovalTargets,
        PreservedRelativePaths = SpeedrunPreservedPaths,
        StaleCleanupRelativeRoots = SpeedrunCleanupRoots,
        DetectedDisplayNames = ["Speedrun RNG Mod 2.0+"],
        ManagedModFolderNames = Array.Empty<string>()
    };

    public static ManagedModFamily Sn2KalliesCommandEnabler { get; } = new()
    {
        Id = "Sn2KalliesCommandEnabler",
        DisplayName = "Kallie's Command Enabler Mod",
        Game = LauncherGame.Subnautica2,
        CompatibilityPredicate = static (_, _, _) => true,
        RuntimeDisplayName = "UE4SS",
        InstallRootRelativePath = @"Subnautica2\Binaries\Win64",
        RuntimeRootRelativePath = "ue4ss",
        DetectionRelativePaths =
        [
            @"Mods\SN2CommandsEnablerMod",
            @"Mods\Kallie'sCustomSN2Commands"
        ],
        VersionMarkerRelativePath = @"Mods\Kallie'sCustomSN2Commands\version.txt",
        BundleParts =
        [
            Sn2RuntimeBundle,
            Sn2CommandsEnablerBundle,
            Sn2CustomCommandsBundle
        ],
        RemovalTargets = CommonSn2RemovalTargets,
        PreservedRelativePaths = Array.Empty<string>(),
        StaleCleanupRelativeRoots = Sn2CleanupRoots,
        DetectedDisplayNames =
        [
            "SN2 Commands Enabler Mod",
            "Kallie's Custom SN2 Commands"
        ],
        ManagedModFolderNames =
        [
            "SN2CommandsEnablerMod",
            "Kallie'sCustomSN2Commands"
        ]
    };

    public static IReadOnlyList<ManagedModFamily> All { get; } =
    [
        SpeedrunRng,
        SpeedrunRng20Plus,
        Sn2KalliesCommandEnabler
    ];

    public static ManagedModFamily? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (string.Equals(id, "Sn2CustomCommands", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "Sn2CommandsEnabler", StringComparison.OrdinalIgnoreCase))
        {
            return Sn2KalliesCommandEnabler;
        }

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
