using System.IO;

namespace SubnauticaLauncher.Services;

public static class SpeedrunningModDetector
{
    public const string DisplayName = "Subnautica Speedrunning Mod";
    public const string DefaultFolderName = "SubnauticaRanked";
    public const string LaunchCommandFileName = "Launch Mod.cmd";

    private static readonly string[] RequiredPaths =
    [
        LaunchCommandFileName,
        "SubnauticaSpeedrunningMod",
        "winhttp.dll",
        ".doorstop_version",
        "doorstop_config.ini"
    ];

    public static bool IsDetected(string gameFolder) =>
        !string.IsNullOrWhiteSpace(gameFolder) &&
        RequiredPaths.All(path => File.Exists(Path.Combine(gameFolder, path)) || Directory.Exists(Path.Combine(gameFolder, path)));

    public static string GetLaunchCommandPath(string gameFolder) => Path.Combine(gameFolder, LaunchCommandFileName);
}
