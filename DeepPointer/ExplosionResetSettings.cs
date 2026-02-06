using System;
using System.IO;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetSettings
    {
        private static readonly string FilePath =
            Path.Combine(AppPaths.DataPath, "ExplosionReset.info");

        // Existing
        public static bool Enabled { get; set; } = false;
        public static ExplosionResetPreset Preset { get; set; }
            = ExplosionResetPreset.Min46_To_4630;

        // 🔥 NEW SETTINGS
        public static bool OverlayEnabled { get; set; } = true;   // default ON
        public static bool TrackResets { get; set; } = false;     // default OFF

        public static void Load()
        {
            if (!File.Exists(FilePath))
                return;

            foreach (var line in File.ReadAllLines(FilePath))
            {
                var split = line.Split('=');
                if (split.Length != 2)
                    continue;

                switch (split[0])
                {
                    case "Enabled":
                        Enabled = bool.Parse(split[1]);
                        break;

                    case "Preset":
                        Preset = Enum.Parse<ExplosionResetPreset>(split[1]);
                        break;

                    case "OverlayEnabled":
                        OverlayEnabled = bool.Parse(split[1]);
                        break;

                    case "TrackResets":
                        TrackResets = bool.Parse(split[1]);
                        break;
                }
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(AppPaths.DataPath);

            File.WriteAllLines(FilePath, new[]
{
    $"Enabled={Enabled}",
    $"Preset={Preset}",
    $"OverlayEnabled={OverlayEnabled}",
    $"TrackResets={TrackResets}"
});
        }
    }
}