using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Mods;

public static class ModCatalog
{
    private const string Owner = "ItsFrostyYo";
    private const string Repo = "Subnautica-Launcher";
    private const string Branch = "main";
    private const string BundleFileName = "Assembly-CheatSharp.v1.0.5+BepInEx_5.4.23.5.zip";

    public static ModDefinition SpeedrunRng { get; } = new()
    {
        Id = "SpeedrunRng",
        DisplayName = "Speedrun RNG Mod",
        Game = LauncherGame.Subnautica,
        BundleZipFileName = BundleFileName,
        DownloadUrl = $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/Mods/{Uri.EscapeDataString(BundleFileName)}",
        RemovalTargets = new[]
        {
            "BepInEx",
            ".doorstop_version",
            "doorstop_config.ini",
            "winhttp.dll",
            "changelog.txt"
        }
    };

    public static IReadOnlyList<ModDefinition> AllMods { get; } = new[]
    {
        SpeedrunRng
    };

    public static ModDefinition? GetById(string? modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return null;

        return AllMods.FirstOrDefault(mod => string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetDisplayName(string? modId)
    {
        ModDefinition? mod = GetById(modId);
        return mod?.DisplayName ?? string.Empty;
    }
}
