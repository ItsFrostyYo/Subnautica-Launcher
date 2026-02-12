using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Macros;

namespace SubnauticaLauncher
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
        public bool BZResetMacroEnabled { get; set; } = false;
        public Key BZResetHotkey { get; set; } = Key.None;
        public GameMode BZResetGameMode { get; set; } = GameMode.Survival;
        public bool RenameOnCloseEnabled { get; set; } = true;

        // Tools
        public bool HardcoreSaveDeleterEnabled { get; set; } = false;

        // Explosion reset settings
        public bool ExplosionResetEnabled { get; set; } = false;
        public ExplosionResetPreset ExplosionPreset { get; set; } =
            ExplosionResetPreset.Min46_To_4630;
        public bool ExplosionOverlayEnabled { get; set; } = true;
        public bool ExplosionTrackResets { get; set; } = false;

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
