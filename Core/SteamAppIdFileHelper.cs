using System;
using System.IO;

namespace SubnauticaLauncher.Core;

internal static class SteamAppIdFileHelper
{
    public const string SubnauticaAppId = "264710";
    public const string BelowZeroAppId = "848450";
    public const string Subnautica2AppId = "1962700";
    public const string SteamAppIdFileName = "steam_appid.txt";

    public static void EnsureSubnauticaSteamAppIdFile(string gameFolder)
    {
        EnsureSteamAppIdFile(gameFolder, SubnauticaAppId);
    }

    public static void EnsureBelowZeroSteamAppIdFile(string gameFolder)
    {
        EnsureSteamAppIdFile(gameFolder, BelowZeroAppId);
    }

    public static void EnsureSubnautica2SteamAppIdFile(string gameFolder)
    {
        EnsureSteamAppIdFile(gameFolder, Subnautica2AppId);
    }

    internal static void EnsureSteamAppIdFile(string gameFolder, string appId)
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

    internal static void RemoveSteamAppIdFile(string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder))
            throw new ArgumentException("Game folder is required.", nameof(gameFolder));

        string filePath = Path.Combine(gameFolder, SteamAppIdFileName);
        if (!File.Exists(filePath))
            return;

        File.Delete(filePath);
    }
}
