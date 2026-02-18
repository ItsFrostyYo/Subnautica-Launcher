using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Settings
{
    public sealed class LauncherSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static bool _loaded;

        public static LauncherSettings Current { get; private set; } = new LauncherSettings();

        public static string FilePath =>
            Path.Combine(AppPaths.DataPath, "launcher_settings.json");

        // =========================
        // GLOBAL SETTINGS
        // =========================
        public string BackgroundPreset { get; set; } = "Lifepod";

        // Reset macro settings
        public bool ResetMacroEnabled { get; set; } = false;
        public Key ResetHotkey { get; set; } = Key.None;
        public GameMode ResetGameMode { get; set; } = GameMode.Survival;
        public bool RenameOnCloseEnabled { get; set; } = true;

        // Tools
        public bool HardcoreSaveDeleterEnabled { get; set; } = false;
        public bool Subnautica100TrackerEnabled { get; set; } = false;
        public Subnautica100TrackerOverlaySize Subnautica100TrackerSize { get; set; } =
            Subnautica100TrackerOverlaySize.Medium;
        public bool Subnautica100TrackerUnlockPopupEnabled { get; set; } = true;
        public bool Subnautica100TrackerSurvivalStartsEnabled { get; set; } = true;
        public bool Subnautica100TrackerCreativeStartsEnabled { get; set; } = false;
        public bool SubnauticaBiomeTrackerEnabled { get; set; } = true;
        public SubnauticaBiomeTrackerCycleMode SubnauticaBiomeTrackerCycleMode { get; set; } =
            SubnauticaBiomeTrackerCycleMode.Databanks;
        public SubnauticaBiomeTrackerScrollSpeed SubnauticaBiomeTrackerScrollSpeed { get; set; } =
            SubnauticaBiomeTrackerScrollSpeed.Medium;

        // Explosion reset settings
        public bool ExplosionResetEnabled { get; set; } = false;
        public ExplosionResetPreset ExplosionPreset { get; set; } =
            ExplosionResetPreset.Min46_To_4630;
        public bool ExplosionOverlayEnabled { get; set; } = true;
        public bool ExplosionTrackResets { get; set; } = false;

        // Launcher presentation mode
        public LauncherStartupMode StartupMode { get; set; } = LauncherStartupMode.Window;
        public Key OverlayToggleKey { get; set; } = Key.Tab;
        public ModifierKeys OverlayToggleModifiers { get; set; } =
            ModifierKeys.Control | ModifierKeys.Shift;
        public double OverlayPanelOpacity { get; set; } = 0.5;

        // DepotDownloader install preferences
        public string DepotDownloaderLastUsername { get; set; } = "";
        public bool DepotDownloaderRememberPassword { get; set; } = false;
        public bool DepotDownloaderUseRememberedLoginOnly { get; set; } = false;
        public bool DepotDownloaderPreferTwoFactorCode { get; set; } = true;
        public bool DepotDownloaderRememberedLoginSeeded { get; set; } = false;

        public static void Load()
        {
            if (_loaded)
                return;

            Directory.CreateDirectory(AppPaths.DataPath);

            if (!File.Exists(FilePath))
            {
                _loaded = true;
                return;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(FilePath), JsonOptions);

                Current = settings ?? new LauncherSettings();
            }
            catch
            {
                Current = new LauncherSettings();
            }

            _loaded = true;
        }

        public static void Save()
        {
            Directory.CreateDirectory(AppPaths.DataPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }

        public static void Reload()
        {
            _loaded = false;
            Load();
        }
    }
}
