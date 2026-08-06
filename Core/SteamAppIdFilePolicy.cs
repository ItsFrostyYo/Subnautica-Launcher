using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Versions;
using System.IO;

namespace SubnauticaLauncher.Core;

internal static class SteamAppIdFilePolicy
{
    public static void ApplyCurrent(LauncherGameProfile profile, string gameFolder)
    {
        Apply(profile, gameFolder, LauncherSettings.Current.ForceLaunchWithoutSteam);
    }

    public static void Apply(
        LauncherGameProfile profile,
        string gameFolder,
        bool keepSteamAppIdFile)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return;

        if (keepSteamAppIdFile)
            profile.EnsureSteamAppIdFile(gameFolder);
        else
            profile.RemoveSteamAppIdFiles(gameFolder);
    }

    public static void ApplyCurrent(
        LauncherGameProfile profile,
        IEnumerable<InstalledVersion> versions)
    {
        Apply(profile, versions, LauncherSettings.Current.ForceLaunchWithoutSteam);
    }

    public static void Apply(
        LauncherGameProfile profile,
        IEnumerable<InstalledVersion> versions,
        bool keepSteamAppIdFile)
    {
        foreach (string gameFolder in versions
                     .Select(version => version.HomeFolder)
                     .Where(folder => !string.IsNullOrWhiteSpace(folder))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Apply(profile, gameFolder, keepSteamAppIdFile);
            }
            catch (Exception ex)
            {
                Logger.Exception(
                    ex,
                    $"[SteamAppId] Failed to {(keepSteamAppIdFile ? "ensure" : "remove")} " +
                    $"{SteamAppIdFileHelper.SteamAppIdFileName} for '{gameFolder}'");
            }
        }
    }
}
