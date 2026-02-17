using System.Collections.Generic;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.BelowZero;

namespace SubnauticaLauncher.BelowZero;

public static class BZVersionRegistry
{
    public static IReadOnlyList<BZVersionInstallDefinition> AllVersions { get; } =
        new List<BZVersionInstallDefinition>
        {
            new(
                "SubnauticaZeroOct2025",
                "*Latest* Oct, 2025 (Subnautica Security Hotfix)",
                3284616936916893028
            ),

            new(
                "SubnauticaZeroAug2021",
                "*Speedrun* Aug, 2021 (Language Pack Patch)",
                3036005960129838677
            ),

            new(
                "SubnauticaZeroAug2025",
                "Aug, 2025 (Below Zero 2025 Patch)",
                4887349951766836612
            ),

            new(
                "SubnauticaZeroOct2022",
                "Oct, 2022 (Teeny-Tiny Hotfix)",
                3183944276780659315
            ),

            new(
                "SubnauticaZeroSep2022",
                "Sep, 2022 (What the Dock Update)",
                5173008513109062040
            ),

            new(
                "SubnauticaZeroMay2021",
                "May, 2021",
                5701798976717775906
            ),

            new(
                "SubnauticaZeroFeb2021",
                "Feb, 2021 (Seaworthy Update)",
                3484037291168779440
            ),

            new(
                "SubnauticaZeroDec2020",
                "*Early Access* Dec, 2020 (Teeny-Tiny Update)",
                4996545525299039845
            ),

            new(
                "SubnauticaZeroOct2020",
                "*Early Access* Oct, 2020",
                5742455615835309040
            ),

            new(
                "SubnauticaZeroAug2020",
                "*Early Access* Aug, 2020",
                3395134997758555135
            ),

            new(
                "SubnauticaZeroJul2020",
                "*Early Access* Jul, 2020",
                7989735351161191407
            ),

            new(
                "SubnauticaZeroJun2020",
                "*Early Access* Jun, 2020",
                3590460764832737788
            ),

            new(
                "SubnauticaZeroApr2020",
                "*Early Access* Apr, 2020 (Frostbite Update)",
                789060194330636471
            ),

            new(
                "SubnauticaZeroFeb2020",
                "*Early Access* Feb, 2020",
                1573152149863871875
            ),

            new(
                "SubnauticaZeroNov2019",
                "*Early Access* Nov, 2019 (Deep Dive Update)",
                150054511446098676
            ),

            new(
                "SubnauticaZeroSep2019",
                "*Early Access* Sep, 2019",
                6320496991720799758
            ),

            new(
                "SubnauticaZeroJul2019",
                "*Early Access* Jul, 2019",
                1877676728833435366
            ),

            new(
                "SubnauticaZeroJun2019",
                "*Early Access* Jun, 2019",
                6031371122074479145
            ),

            new(
                "SubnauticaZeroMay2019",
                "*Early Access* May, 2019",
                9015759517461719539
            ),

            new(
                "SubnauticaZeroApr2019",
                "*Early Access* Apr, 2019",
                1468389225676593976
            ),

            new(
                "SubnauticaZeroMar2019",
                "*Early Access* Mar, 2019",
                7109419288448066306
            ),

            new(
                "SubnauticaZeroFeb2019",
                "*Early Access* Feb, 2019",
                4604359450461112821
            ),

            new(
                "SubnauticaZeroJan2019",
                "*Early Access* Jan, 2019 (Early Access Release)",
                3828807225573055106
            ),
        };
}
