using System.IO;
using System.Text.Json;
using SubnauticaLauncher.Core;

namespace SubnauticaLauncher.Services;

/// <summary>Single persisted home for launcher preferences and version state.</summary>
public sealed class LauncherConfig
{
    private static string FilePath => Path.Combine(AppPaths.DataPath, "config.json");

    public string BackgroundPreset { get; set; } = "Aurora Borealis";
    public bool ForceLaunchWithSteam { get; set; }
    public bool ApplySavedSubnauticaSettingsToNewInstalls { get; set; } = true;
    public string? ActiveVersionFolder { get; set; }
    public string DepotDownloaderUsername { get; set; } = string.Empty;
    public string DepotDownloaderInstallCommonPath { get; set; } = string.Empty;
    public bool DepotDownloaderRememberPassword { get; set; }
    public bool DepotDownloaderUseRememberedLogin { get; set; }
    public bool DepotDownloaderRememberedLoginSeeded { get; set; }
    public bool DepotDownloaderPreferTwoFactorCode { get; set; }
    public bool DepotDownloaderMobileAuthMigrationApplied { get; set; }
    public bool DepotDownloaderRememberChoiceMigrationApplied { get; set; }

    public static LauncherConfig Load()
    {
        Directory.CreateDirectory(AppPaths.DataPath);
        try
        {
            if (File.Exists(FilePath))
            {
                LauncherConfig existingConfig = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(FilePath)) ?? new LauncherConfig();
                existingConfig.ApplyMobileAuthMigration();
                existingConfig.ApplyRememberChoiceMigration();
                RetireLegacyFiles();
                return existingConfig;
            }

            var config = new LauncherConfig();
            TryReadString(Path.Combine(AppPaths.DataPath, "appearance.json"), "BackgroundPreset", value => config.BackgroundPreset = value);
            TryReadBoolean(Path.Combine(AppPaths.DataPath, "appearance.json"), "ForceLaunchWithSteam", value => config.ForceLaunchWithSteam = value);
            TryReadString(Path.Combine(AppPaths.DataPath, "launcher-state.json"), "ActiveVersionFolder", value => config.ActiveVersionFolder = value);
            config.ApplyMobileAuthMigration(save: false);
            config.ApplyRememberChoiceMigration(save: false);
            config.Save();
            RetireLegacyFiles();
            return config;
        }
        catch { return new LauncherConfig(); }
    }

    private void ApplyMobileAuthMigration(bool save = true)
    {
        if (DepotDownloaderMobileAuthMigrationApplied) return;

        // The former default forced -no-mobile. Prefer Steam's normal mobile approval flow,
        // while preserving any choice the user makes after this one-time migration.
        DepotDownloaderPreferTwoFactorCode = false;
        DepotDownloaderMobileAuthMigrationApplied = true;
        if (save) Save();
    }

    private void ApplyRememberChoiceMigration(bool save = true)
    {
        if (DepotDownloaderRememberChoiceMigrationApplied) return;

        // Previous rebuilds forced this on. Reset it once so saving a login
        // becomes an explicit choice from the install login screen.
        DepotDownloaderRememberPassword = false;
        DepotDownloaderRememberChoiceMigrationApplied = true;
        if (save) Save();
    }

    public void Save()
    {
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, FilePath, true);
    }

    private static void TryReadString(string path, string propertyName, Action<string> apply)
    {
        try { using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)); if (document.RootElement.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String) apply(value.GetString() ?? string.Empty); } catch { }
    }

    private static void TryReadBoolean(string path, string propertyName, Action<bool> apply)
    {
        try { using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)); if (document.RootElement.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False) apply(value.GetBoolean()); } catch { }
    }

    private static void RetireLegacyFiles()
    {
        foreach (string name in new[] { "appearance.json", "launcher-state.json" })
        {
            try { File.Delete(Path.Combine(AppPaths.DataPath, name)); } catch { }
        }
    }
}
