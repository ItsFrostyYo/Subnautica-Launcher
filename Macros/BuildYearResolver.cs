using SubnauticaLauncher.Core;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.Macros
{
    public static class BuildYearResolver
    {
        public static int ResolveGroupedYear(string root)
        {
            if (VersionIdentityResolver.TryReadBuildTime(
                    root,
                    LauncherGameProfiles.Subnautica,
                    out var buildTime,
                    out _))
            {
                return Normalize(buildTime.Year);
            }

            return 2022;
        }

        public static int ResolveBelowZero()
        {
            return BELOW_ZERO_GROUP;
        }

        public const int BELOW_ZERO_GROUP = -1;

        private static int Normalize(int year)
        {
            if (year <= 2017) return 2017;
            if (year <= 2021) return 2018;
            return 2022;
        }
    }
}
