using System;
using System.IO;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetSettings
    {
        private static readonly string SettingsPath =
            Path.Combine(AppPaths.DataPath, "ExplosionReset.info");

        public static bool Enabled { get; set; } = false;

        public static ExplosionTimePreset Preset { get; set; } =
            ExplosionTimePreset.Min46_To_4630;

        public static void Load()
        {
            if (!File.Exists(SettingsPath))
                return;

            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var split = line.Split('=');
                if (split.Length != 2)
                    continue;

                if (split[0] == "Enabled")
                {
                    Enabled = bool.TryParse(split[1], out var v) && v;
                }
                else if (split[0] == "Preset")
                {
                    if (Enum.TryParse(split[1], out ExplosionTimePreset preset))
                        Preset = preset;
                }
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(AppPaths.DataPath);

            File.WriteAllLines(SettingsPath, new[]
            {
                $"Enabled={Enabled}",
                $"Preset={Preset}"
            });
        }
    }
}