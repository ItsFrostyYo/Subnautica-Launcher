using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetSettings
    {
        public static bool Enabled
        {
            get => LauncherSettings.Current.ExplosionResetEnabled;
            set => LauncherSettings.Current.ExplosionResetEnabled = value;
        }

        public static ExplosionResetPreset Preset
        {
            get => LauncherSettings.Current.ExplosionPreset;
            set => LauncherSettings.Current.ExplosionPreset = value;
        }

        public static int CustomMinSeconds
        {
            get
            {
                NormalizeCustomRange();
                return LauncherSettings.Current.ExplosionCustomMinSeconds;
            }
            set
            {
                LauncherSettings.Current.ExplosionCustomMinSeconds = value;
                NormalizeCustomRange();
            }
        }

        public static int CustomMaxSeconds
        {
            get
            {
                NormalizeCustomRange();
                return LauncherSettings.Current.ExplosionCustomMaxSeconds;
            }
            set
            {
                LauncherSettings.Current.ExplosionCustomMaxSeconds = value;
                NormalizeCustomRange();
            }
        }

        public static bool OverlayEnabled
        {
            get => LauncherSettings.Current.ExplosionOverlayEnabled;
            set => LauncherSettings.Current.ExplosionOverlayEnabled = value;
        }

        public static bool TrackResets
        {
            get => LauncherSettings.Current.ExplosionTrackResets;
            set => LauncherSettings.Current.ExplosionTrackResets = value;
        }

        public static void Load()
        {
            LauncherSettings.Load();
            if (NormalizeCustomRange())
                LauncherSettings.Save();
        }

        public static void Save()
        {
            NormalizeCustomRange();
            LauncherSettings.Save();
        }

        public static void SetCustomRange(int minimumSeconds, int maximumSeconds)
        {
            LauncherSettings.Current.ExplosionCustomMinSeconds = minimumSeconds;
            LauncherSettings.Current.ExplosionCustomMaxSeconds = maximumSeconds;
            NormalizeCustomRange();
        }

        public static (ExplosionResetPreset preset, float min, float max) GetConfiguredRange()
        {
            NormalizeCustomRange();

            return Preset == ExplosionResetPreset.Custom
                ? (Preset, LauncherSettings.Current.ExplosionCustomMinSeconds, LauncherSettings.Current.ExplosionCustomMaxSeconds)
                : (Preset, ExplosionPresetRanges.Get(Preset).min, ExplosionPresetRanges.Get(Preset).max);
        }

        private static bool NormalizeCustomRange()
        {
            int min = ExplosionCustomRange.Clamp(LauncherSettings.Current.ExplosionCustomMinSeconds);
            int max = ExplosionCustomRange.Clamp(LauncherSettings.Current.ExplosionCustomMaxSeconds);

            if (min > max)
                max = min;

            bool changed =
                min != LauncherSettings.Current.ExplosionCustomMinSeconds ||
                max != LauncherSettings.Current.ExplosionCustomMaxSeconds;

            LauncherSettings.Current.ExplosionCustomMinSeconds = min;
            LauncherSettings.Current.ExplosionCustomMaxSeconds = max;
            return changed;
        }
    }
}
