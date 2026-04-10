using System;
using System.IO;

namespace SubnauticaLauncher.Core;

internal static class SteamAppIdFileHelper
{
    public const string SubnauticaAppId = "264710";
    private const string SteamAppIdFileName = "steam_appid.txt";

    public static void EnsureSubnauticaSteamAppIdFile(string gameFolder)
    {
        EnsureSteamAppIdFile(gameFolder, SubnauticaAppId);
    }

    private static void EnsureSteamAppIdFile(string gameFolder, string appId)
    {
        if (string.IsNullOrWhiteSpace(gameFolder))
            throw new ArgumentException("Game folder is required.", nameof(gameFolder));

        Directory.CreateDirectory(gameFolder);

        string filePath = Path.Combine(gameFolder, SteamAppIdFileName);
        string existing = File.Exists(filePath) ? File.ReadAllText(filePath).Trim() : string.Empty;

        if (string.Equals(existing, appId, StringComparison.Ordinal))
            return;

        File.WriteAllText(filePath, appId);
    }
}
