using System.IO;

namespace SubnauticaLauncher.Services;

public static class SpeedrunningModDetector
{
    public const string DisplayName = "Subnautica Speedrunning Mod";
    public const string DefaultFolderName = "SubnauticaRanked";
    public const string LaunchCommandFileName = "Launch Mod.cmd";

    private static readonly string[] InstallationMarkers =
    [
        @"SubnauticaSpeedrunningMod\Bootstrap\SubnauticaSpeedrunningMod.Bootstrap.dll",
        @"SubnauticaSpeedrunningMod\Runtime\SubnauticaSpeedrunningMod.Runtime.dll",
        @"SubnauticaSpeedrunningMod\Launch Mod.exe"
    ];

    public static bool IsDetected(string gameFolder) =>
        !string.IsNullOrWhiteSpace(gameFolder) &&
        Directory.Exists(Path.Combine(gameFolder, "SubnauticaSpeedrunningMod")) &&
        InstallationMarkers.Any(path => File.Exists(Path.Combine(gameFolder, path)));

    public static string GetLaunchCommandPath(string gameFolder) => Path.Combine(gameFolder, LaunchCommandFileName);
}
