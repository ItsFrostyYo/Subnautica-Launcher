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
        }

        public static void Save()
        {
            LauncherSettings.Save();
        }
    }
}
