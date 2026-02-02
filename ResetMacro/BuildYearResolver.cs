using System;
using System.IO;

namespace SubnauticaLauncher.Macros
{
    public static class BuildYearResolver
    {
        // Existing Subnautica logic (UNCHANGED)
        public static int ResolveGroupedYear(string root)
        {
            string[] paths =
            {
                Path.Combine(root, "__buildtime.txt"),
                Path.Combine(root, "Subnautica_Data", "StreamingAssets", "__buildtime.txt")
            };

            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;

                if (DateTime.TryParse(File.ReadAllText(p), out var dt))
                    return Normalize(dt.Year);
            }

            return 2022; // safe default
        }

        // ✅ BELOW ZERO: fixed group (no detection)
        public static int ResolveBelowZero()
        {
            return BELOW_ZERO_GROUP;
        }

        // 🔒 single constant used everywhere
        public const int BELOW_ZERO_GROUP = -1;

        private static int Normalize(int year)
        {
            if (year <= 2017) return 2017;
            if (year <= 2021) return 2018;
            return 2022;
        }
    }
}