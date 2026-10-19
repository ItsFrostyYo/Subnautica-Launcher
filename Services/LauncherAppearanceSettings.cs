using System.IO;
using System.Text.Json;
using SubnauticaLauncher.Core;

namespace SubnauticaLauncher.Services;

public sealed class LauncherAppearanceSettings
{
    private static string FilePath => Path.Combine(AppPaths.DataPath, "appearance.json");
    public string BackgroundPreset { get; set; } = "Aurora Borealis";
    public bool ForceLaunchWithSteam { get; set; }

    public static LauncherAppearanceSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<LauncherAppearanceSettings>(File.ReadAllText(FilePath)) ?? new LauncherAppearanceSettings()
                : new LauncherAppearanceSettings();
        }
        catch { return new LauncherAppearanceSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
