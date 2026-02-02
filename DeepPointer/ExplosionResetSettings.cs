using System;
using System.IO;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetSettings
    {
        private static readonly string FilePath =
            Path.Combine(AppPaths.DataPath, "ExplosionReset.info");

        public static bool Enabled { get; set; } = false;

        public static ExplosionResetPreset Preset { get; set; }
            = ExplosionResetPreset.Min46_To_4630;

        public static void Load()
        {
            if (!File.Exists(FilePath))
                return;

            foreach (var line in File.ReadAllLines(FilePath))
            {
                var split = line.Split('=');
                if (split.Length != 2)
                    continue;

                if (split[0] == "Enabled")
                    Enabled = bool.Parse(split[1]);

                if (split[0] == "Preset")
                    Preset = Enum.Parse<ExplosionResetPreset>(split[1]);
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(AppPaths.DataPath);

            File.WriteAllLines(FilePath, new[]
            {
                $"Enabled={Enabled}",
                $"Preset={Preset}"
            });
        }
    }
}