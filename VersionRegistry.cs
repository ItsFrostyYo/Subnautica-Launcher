using System.Collections.Generic;

namespace SubnauticaLauncher
{
    public static class VersionRegistry
    {
        public static IReadOnlyList<VersionInstallDefinition> AllVersions { get; } =
            new List<VersionInstallDefinition>
            {
                new(
                    "Subnautica_Oct2025",
                    "*Latest* Oct, 2025 (Subnautica Security Hotfix)",
                    4495119477327785570
                ),

                new(
                    "Subnautica_Dec2021",
                    "*Legacy* Dec, 2021 (Minor Update B89)",
                    455616957047142657
                ),

                new(
                    "Subnautica_Sep2018",
                    "*Speedrun* Sep, 2018 (Speedrunners Version)",
                    5196577974721678848
                ),

                new(
                    "Subnautica_Aug2025",
                    "Aug, 2025 (Subnautica 2025 Patch)",
                    8427274798903478123
                ),

                new(
                    "Subnautica_Mar2023",
                    "Mar, 2023 (Steam Deck Update)",
                    3638894940716012854
                ),

                new(
                    "Subnautica_Dec2022",
                    "Dec, 2022 (Living Large Update)",
                    7985964044056395698
                ),

                new(
                    "Subnautica_Jan2020",
                    "Jan, 2020 (2020 Release)",
                    8054727543627818758
                ),

                new(
                    "Subnautica_Nov2019",
                    "Nov, 2019 (Big Little Update)",
                    5751432439227487029
                ),

                new(
                    "Subnautica_Jan2018",
                    "Jan, 2018 (First Release)",
                    3244918507114829941
                ),

                new(
                    "Subnautica_Dec2017",
                    "*Early Access* Dec, 2017 (Recent Stable EA)",
                    9219915328445201166
                ),

                new(
                    "Subnautica_Sep2017",
                    "*Early Access* Sep, 2017 (Solid Stable EA)",
                    3436420285625495570
                ),

                new(
                    "Subnautica_Mar2017",
                    "*Early Access* Mar, 2017 (Early Stable EA)",
                    3489434290486619086
                )
            };
    }
}