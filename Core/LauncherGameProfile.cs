using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Subnautica2;
using SubnauticaLauncher.Versions;
using System.IO;

namespace SubnauticaLauncher.Core;

internal sealed class LauncherGameProfile
{
    public required LauncherGame Game { get; init; }
    public required string DisplayName { get; init; }
    public required string ProcessName { get; init; }
    public required string ExecutableName { get; init; }
    public required string LaunchExecutableRelativePath { get; init; }
    public required IReadOnlyList<string> DetectExecutableRelativePaths { get; init; }
    public required IReadOnlyList<string> SteamAppIdRelativeDirectories { get; init; }
    public required string InfoFileName { get; init; }
    public required string LauncherMarker { get; init; }
    public required string SteamAppId { get; init; }
    public required string ActiveFolderName { get; init; }
    public required string UnmanagedReservedFolderName { get; init; }
    public required string SaveDataFolderName { get; init; }
    public required IReadOnlyList<GameVersionInstallDefinition> InstallDefinitions { get; init; }
    public required Func<string, string, InstalledVersion?> FromInfo { get; init; }

    public bool HasExpectedExecutable(string versionFolder)
    {
        return !string.IsNullOrWhiteSpace(versionFolder) &&
               EnumerateDetectExecutablePaths(versionFolder).Any(File.Exists);
    }

    public string GetLaunchExecutablePath(string versionFolder)
    {
        return Path.Combine(versionFolder, LaunchExecutableRelativePath);
    }

    public string GetLaunchWorkingDirectory(string versionFolder)
    {
        string launchExecutablePath = GetLaunchExecutablePath(versionFolder);
        return Path.GetDirectoryName(launchExecutablePath) ?? versionFolder;
    }

    public string GetActiveFolderPath(string commonPath)
    {
        return Path.Combine(commonPath, ActiveFolderName);
    }

    public bool MatchesActiveFolderName(string? folderName)
    {
        return !string.IsNullOrWhiteSpace(folderName) &&
               string.Equals(folderName, ActiveFolderName, StringComparison.OrdinalIgnoreCase);
    }

    public void EnsureSteamAppIdFile(string gameFolder)
    {
        foreach (string relativeDirectory in SteamAppIdRelativeDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string targetDirectory =
                string.IsNullOrWhiteSpace(relativeDirectory) || relativeDirectory == "."
                    ? gameFolder
                    : Path.Combine(gameFolder, relativeDirectory);

            if (!string.Equals(targetDirectory, gameFolder, StringComparison.OrdinalIgnoreCase) &&
                !Directory.Exists(targetDirectory))
            {
                continue;
            }

            SteamAppIdFileHelper.EnsureSteamAppIdFile(targetDirectory, SteamAppId);
        }
    }

    private IEnumerable<string> EnumerateDetectExecutablePaths(string versionFolder)
    {
        IEnumerable<string> relativePaths = DetectExecutableRelativePaths.Count > 0
            ? DetectExecutableRelativePaths
            : [ExecutableName];

        foreach (string relativePath in relativePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(versionFolder, relativePath);
        }
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
        LaunchExecutableRelativePath = "Subnautica.exe",
        DetectExecutableRelativePaths = ["Subnautica.exe"],
        SteamAppIdRelativeDirectories = ["."],
        InfoFileName = "Version.info",
        LauncherMarker = "IsSubnauticaLauncherVersion",
        SteamAppId = SteamAppIdFileHelper.SubnauticaAppId,
        ActiveFolderName = "Subnautica",
        UnmanagedReservedFolderName = "SubnauticaUnmanagedVersion",
        SaveDataFolderName = "SNAppData",
        InstallDefinitions = VersionRegistry.AllVersions.Cast<GameVersionInstallDefinition>().ToArray(),
        FromInfo = InstalledVersion.FromInfo
    };

    public static LauncherGameProfile BelowZero { get; } = new()
    {
        Game = LauncherGame.BelowZero,
        DisplayName = "Below Zero",
        ProcessName = "SubnauticaZero",
        ExecutableName = "SubnauticaZero.exe",
        LaunchExecutableRelativePath = "SubnauticaZero.exe",
        DetectExecutableRelativePaths = ["SubnauticaZero.exe"],
        SteamAppIdRelativeDirectories = ["."],
        InfoFileName = "BZVersion.info",
        LauncherMarker = "IsBelowZeroLauncherVersion",
        SteamAppId = SteamAppIdFileHelper.BelowZeroAppId,
        ActiveFolderName = "SubnauticaZero",
        UnmanagedReservedFolderName = "SubnauticaZeroUnmanagedVersion",
        SaveDataFolderName = "SNAppData",
        InstallDefinitions = BZVersionRegistry.AllVersions.Cast<GameVersionInstallDefinition>().ToArray(),
        FromInfo = BZInstalledVersion.FromInfo
    };

    public static LauncherGameProfile Subnautica2 { get; } = new()
    {
        Game = LauncherGame.Subnautica2,
        DisplayName = "Subnautica 2",
        ProcessName = "Subnautica2-Win64-Shipping",
        ExecutableName = "Subnautica2.exe",
        LaunchExecutableRelativePath = "Subnautica2.exe",
        DetectExecutableRelativePaths =
        [
            "Subnautica2.exe",
            @"Subnautica2\Binaries\Win64\Subnautica2-Win64-Shipping.exe"
        ],
        SteamAppIdRelativeDirectories =
        [
            ".",
            @"Subnautica2\Binaries\Win64"
        ],
        InfoFileName = "SN2Version.info",
        LauncherMarker = "IsSubnautica2LauncherVersion",
        SteamAppId = SteamAppIdFileHelper.Subnautica2AppId,
        ActiveFolderName = "Subnautica2",
        UnmanagedReservedFolderName = "Subnautica2UnmanagedVersion",
        SaveDataFolderName = "Subnautica2AppData",
        InstallDefinitions = Subnautica2VersionRegistry.AllVersions.Cast<GameVersionInstallDefinition>().ToArray(),
        FromInfo = InstalledVersion.FromInfo
    };

    public static IReadOnlyList<LauncherGameProfile> All { get; } =
    [
        Subnautica,
        BelowZero,
        Subnautica2
    ];

    public static LauncherGameProfile Get(LauncherGame game)
    {
        return game switch
        {
            LauncherGame.BelowZero => BelowZero,
            LauncherGame.Subnautica2 => Subnautica2,
            _ => Subnautica
        };
    }

    public static LauncherGameProfile GetByProcessName(string processName)
    {
        if (string.Equals(processName, BelowZero.ProcessName, StringComparison.OrdinalIgnoreCase))
            return BelowZero;

        if (string.Equals(processName, Subnautica2.ProcessName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(processName, "Subnautica2", StringComparison.OrdinalIgnoreCase))
        {
            return Subnautica2;
        }

        return Subnautica;
    }

    public static LauncherGameProfile GetBySteamAppId(int steamAppId)
    {
        string appId = steamAppId.ToString();
        if (appId == BelowZero.SteamAppId)
            return BelowZero;
        if (appId == Subnautica2.SteamAppId)
            return Subnautica2;
        return Subnautica;
    }

    public static LauncherGameProfile? DetectFromFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        if (Subnautica2.HasExpectedExecutable(folderPath))
            return Subnautica2;

        if (BelowZero.HasExpectedExecutable(folderPath))
            return BelowZero;

        if (Subnautica.HasExpectedExecutable(folderPath))
            return Subnautica;

        return null;
    }

    public static bool IsReservedActiveFolderName(string? folderName)
    {
        return All.Any(profile => profile.MatchesActiveFolderName(folderName));
    }

    public static string GetReservedActiveFolderNamesDisplay()
    {
        string[] reservedNames = All
            .Select(profile => $"'{profile.ActiveFolderName}'")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return reservedNames.Length switch
        {
            0 => string.Empty,
            1 => reservedNames[0],
            2 => $"{reservedNames[0]} or {reservedNames[1]}",
            _ => string.Join(", ", reservedNames[..^1]) + $", or {reservedNames[^1]}"
        };
    }
}
