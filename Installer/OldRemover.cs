using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.Settings;
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace SubnauticaLauncher.Installer
{
    public static class OldRemover
    {
        /// <summary>
        /// Cleans up files and folders from older launcher versions.
        /// Safe to call every startup.
        /// </summary>
        public static void Run()
        {
            MigrateSettings();
            RemoveOldBackgroundsFolder();
        }

        // =========================
        // BACKGROUND CLEANUP (OLD VERSIONS)
        // =========================
        private static void RemoveOldBackgroundsFolder()
        {
            string oldBackgroundsPath =
                Path.Combine(AppPaths.DataPath, "backgrounds");

            if (!Directory.Exists(oldBackgroundsPath))
                return;

            try
            {
                Directory.Delete(oldBackgroundsPath, recursive: true);
            }
            catch
            {
                // Never block launcher startup
            }
        }

        // =========================
        // SETTINGS MIGRATION
        // =========================
        private static void MigrateSettings()
        {
            try
            {
                LauncherSettings.Load();
                var settings = LauncherSettings.Current;
                bool updated = false;

                // Background preset (BPreset.txt)
                string bgPresetPath = Path.Combine(AppPaths.DataPath, "BPreset.txt");
                if (File.Exists(bgPresetPath))
                {
                    var bg = File.ReadAllText(bgPresetPath).Trim();
                    if (!string.IsNullOrWhiteSpace(bg))
                    {
                        settings.BackgroundPreset = bg;
                        updated = true;
                    }

                    TryDelete(bgPresetPath);
                }

                // Macro settings (Settings.info)
                string macroSettingsPath = Path.Combine(AppPaths.DataPath, "Settings.info");
                if (File.Exists(macroSettingsPath))
                {
                    var lines = File.ReadAllLines(macroSettingsPath);
                    var dict = lines
                        .Select(l => l.Split('='))
                        .Where(x => x.Length == 2)
                        .ToDictionary(x => x[0], x => x[1]);

                    if (dict.TryGetValue("Enabled", out var enabled))
                    {
                        settings.ResetMacroEnabled = bool.Parse(enabled);
                        updated = true;
                    }

                    if (dict.TryGetValue("Hotkey", out var hotkey))
                    {
                        if (Enum.TryParse<Key>(hotkey, out var key))
                        {
                            settings.ResetHotkey = key;
                            updated = true;
                        }
                    }

                    if (dict.TryGetValue("Mode", out var mode))
                    {
                        if (Enum.TryParse<GameMode>(mode, out var gameMode))
                        {
                            settings.ResetGameMode = gameMode;
                            updated = true;
                        }
                    }

                    if (dict.TryGetValue("RenameOnClose", out var roc))
                    {
                        settings.RenameOnCloseEnabled = bool.Parse(roc);
                        updated = true;
                    }

                    TryDelete(macroSettingsPath);
                }

                // Explosion settings (ExplosionReset.info)
                string explosionPath = Path.Combine(AppPaths.DataPath, "ExplosionReset.info");
                if (File.Exists(explosionPath))
                {
                    foreach (var line in File.ReadAllLines(explosionPath))
                    {
                        var split = line.Split('=');
                        if (split.Length != 2)
                            continue;

                        switch (split[0])
                        {
                            case "Enabled":
                                settings.ExplosionResetEnabled = bool.Parse(split[1]);
                                updated = true;
                                break;
                            case "Preset":
                                settings.ExplosionPreset = Enum.Parse<ExplosionResetPreset>(split[1]);
                                updated = true;
                                break;
                            case "OverlayEnabled":
                                settings.ExplosionOverlayEnabled = bool.Parse(split[1]);
                                updated = true;
                                break;
                            case "TrackResets":
                                settings.ExplosionTrackResets = bool.Parse(split[1]);
                                updated = true;
                                break;
                        }
                    }

                    TryDelete(explosionPath);
                }

                if (updated)
                    LauncherSettings.Save();
            }
            catch
            {
                // Never block launcher startup
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }
}
