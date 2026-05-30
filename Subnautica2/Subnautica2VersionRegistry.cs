using System.Collections.Generic;

namespace SubnauticaLauncher.Subnautica2;

public static class Subnautica2VersionRegistry
{
    public static IReadOnlyList<Subnautica2VersionInstallDefinition> AllVersions { get; } =
        new List<Subnautica2VersionInstallDefinition>
        {
            new(
                "Subnautica2_22ndMay2026",
                "May22nd, 2026 (Early Access Hotfix)",
                267872327096816318,
                114707),

            new(
                "Subnautica2_19thMay2026",
                "May19th, 2026 (Early Access Hotfix)",
                8500743838928293422,
                113933),

            new(
                "Subnautica2_May14th2026",
                "May14th, 2026 (Official Launch Build)",
                4222263125962173451,
                113109),

            new(
                "Subnautica2_11thMay2026",
                "*First Release* May11th, 2026 (Earliest Available Build)",
                4500581681485274324,
                1100265)
        };
}
