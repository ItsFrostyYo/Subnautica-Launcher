using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Versions;
using System.IO;

namespace SubnauticaLauncher.Core;

internal sealed class LauncherGameProfile
{
    public required LauncherGame Game { get; init; }
    public required string DisplayName { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutableName { get; init; }
    public required string InfoFileName { get; init; }
    public required string LauncherMarker { get; init; }
    public required string SteamAppId { get; init; }
    public required string ActiveFolderName { get; init; }
    public required string UnmanagedReservedFolderName { get; init; }
    public required IReadOnlyList<GameVersionInstallDefinition> InstallDefinitions { get; init; }
    public required Func<string, string, InstalledVersion?> FromInfo { get; init; }

    public bool HasExpectedExecutable(string versionFolder)
    {
        return !string.IsNullOrWhiteSpace(versionFolder) &&
               File.Exists(Path.Combine(versionFolder, ExecutableName));
    }

    public void EnsureSteamAppIdFile(string gameFolder)
    {
        SteamAppIdFileHelper.EnsureSteamAppIdFile(gameFolder, SteamAppId);
    }
}

internal static class LauncherGameProfiles
{
    public static LauncherGameProfile Subnautica { get; } = new()
    {
        Game = LauncherGame.Subnautica,
        DisplayName = "Subnautica",
        ProcessName = "Subnautica",
        ExecutableName = "Subnautica.exe",
        InfoFileName = "Version.info",
        LauncherMarker = "IsSubnauticaLauncherVersion",
        SteamAppId = SteamAppIdFileHelper.SubnauticaAppId,
        ActiveFolderName = "Subnautica",
        UnmanagedReservedFolderName = "SubnauticaUnmanagedVersion",
        InstallDefinitions = VersionRegistry.AllVersions.Cast<GameVersionInstallDefinition>().ToArray(),
        FromInfo = InstalledVersion.FromInfo
    };

    public static LauncherGameProfile BelowZero { get; } = new()
    {
        Game = LauncherGame.BelowZero,
        DisplayName = "Below Zero",
        ProcessName = "SubnauticaZero",
        ExecutableName = "SubnauticaZero.exe",
        InfoFileName = "BZVersion.info",
        LauncherMarker = "IsBelowZeroLauncherVersion",
        SteamAppId = SteamAppIdFileHelper.BelowZeroAppId,
        ActiveFolderName = "SubnauticaZero",
        UnmanagedReservedFolderName = "SubnauticaZeroUnmanagedVersion",
        InstallDefinitions = BZVersionRegistry.AllVersions.Cast<GameVersionInstallDefinition>().ToArray(),
        FromInfo = BZInstalledVersion.FromInfo
    };

    public static IReadOnlyList<LauncherGameProfile> All { get; } =
    [
        Subnautica,
        BelowZero
    ];

    public static LauncherGameProfile Get(LauncherGame game)
    {
        return game == LauncherGame.BelowZero
            ? BelowZero
            : Subnautica;
    }

    public static LauncherGameProfile GetByProcessName(string processName)
    {
        return string.Equals(processName, BelowZero.ProcessName, StringComparison.OrdinalIgnoreCase)
            ? BelowZero
            : Subnautica;
    }

    public static LauncherGameProfile GetBySteamAppId(int steamAppId)
    {
        return steamAppId.ToString() == BelowZero.SteamAppId
            ? BelowZero
            : Subnautica;
    }

    public static LauncherGameProfile? DetectFromFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        if (BelowZero.HasExpectedExecutable(folderPath))
            return BelowZero;

        if (Subnautica.HasExpectedExecutable(folderPath))
            return Subnautica;

        return null;
    }
}
