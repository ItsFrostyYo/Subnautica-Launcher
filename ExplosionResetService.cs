using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Macros;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetService
    {
        public static bool IsSupportedVersion()
        {
            string activePath =
                Path.Combine(AppPaths.SteamCommonPath, "Subnautica");

            int year = BuildYearResolver.ResolveGroupedYear(activePath);

            return year == 2018 || year == 2023;
        }

        public static async Task RunAsync(
            GameMode mode,
            ExplosionTimePreset preset)
        {
            // Placeholder until logic is added
            await Task.Yield();
        }
    }
}