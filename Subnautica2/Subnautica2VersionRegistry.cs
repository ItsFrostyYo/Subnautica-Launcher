using System.Collections.Generic;

namespace SubnauticaLauncher.Subnautica2;

public static class Subnautica2VersionRegistry
{
    public static IReadOnlyList<Subnautica2VersionInstallDefinition> AllVersions { get; } =
        new List<Subnautica2VersionInstallDefinition>
        {
            new(
                "Subnautica2_UnlistedUpdate",
                "Sep3rd, 2026 (Unlisted Update)",
                2218551816517627922,
                0),
            new(
                "Subnautica2_BuddySystemHotfix",
                "Sep1st, 2026 (1.2.2 | Buddy System Hotfix 1)",
                3270283820977561875,
                128456),
            new(
                "Subnautica2_BuddySystem",
                "Aug19th, 2026 (1.2 | Buddy System)",
                3467256529046907925,
                123362),
                
            new(
                "Subnautica2_EAHotfix4",
                "July14th, 2026 (Early Access Hotix 4)",
                2045494873371530057,
                0),

            new(
                "Subnautica2_AdaptiveMeasures",
                "July8th, 2026 (1.1 | Adaptive Measures)",
                9075255258717112216,
                0),

            new(
                "Subnautica2_EAHotfix3",
                "June, 2026 (Early Access Hotfix 3)",
                8163669658365755688,
                0),

            new(
                "Subnautica2_EAHotfix2",
                "May22nd, 2026 (Early Access Hotfix 2)",
                267872327096816318,
                114707),

            new(
                "Subnautica2_EAHotfix1",
                "May19th, 2026 (Early Access Hotfix 1)",
                8500743838928293422,
                113933),

            new(
                "Subnautica2_LaunchBuild",
                "May14th, 2026 (Official Launch Build)",
                4222263125962173451,
                113109),

            new(
                "Subnautica2_FirstBuild",
                "*First Release* May11th, 2026 (Earliest Available Build)",
                4500581681485274324,
                1100265)
        };
}
