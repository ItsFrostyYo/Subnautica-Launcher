using System.IO;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetTracker
    {
        private static readonly string LogPath =
            Path.Combine(AppPaths.DataPath, "LastExplosionReset.log");

        public static void WriteGood(double time, int resets)
        {
            if (!ExplosionResetSettings.TrackResets)
                return;

            Directory.CreateDirectory(AppPaths.DataPath);

            var ts = TimeSpan.FromSeconds(time);

            File.WriteAllLines(LogPath, new[]
            {
        $"Reason=Explo time was {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}",
        $"Resets={resets}"
    });
        }

        public static void WriteCanceled(int resets)
        {
            if (!ExplosionResetSettings.TrackResets)
                return;

            Directory.CreateDirectory(AppPaths.DataPath);

            File.WriteAllLines(LogPath, new[]
            {
                "Reason=Macro canceled",
                $"Resets={resets}"
            });
        }
    }
}