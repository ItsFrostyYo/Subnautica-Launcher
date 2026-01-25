using System.Collections.Generic;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Versions;

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
                "Subnautica_Apr2021",
                "Apr, 2021 (2021 Release)",
                6416485996936522597
            ),

            new(
                "Subnautica_Aug2020",
                "Aug, 2020",
                1908309407776017760
            ),

            new(
                "Subnautica_Jan2020",
                "Jan, 2020 (2020 Release)",
                8054727543627818758
            ),

            new(
                "Subnautica_Dec2019",
                "Dec, 2019",
                8843040285246122107
            ),

            new(
                "Subnautica_Nov2019",
                "Nov, 2019 (Big Little Update)",
                5751432439227487029
            ),

            new(
                "Subnautica_May2018",
                "May, 2018",
                4775135007227916113
            ),

            new(
                "Subnautica_Apr2018",
                "Apr, 2018",
                4000115425749409787
            ),

            new(
                "Subnautica_Mar2018",
                "Mar, 2018",
                6710957958784452942
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
            ),
            
            new(
                "Subnautica_Oct2017",
                "*Early Access* Oct, 2017",
                6038350512483430267
            ),

            new(
                "Subnautica_Aug2017",
                "*Early Access* Aug, 2017",
                7963555839872105964
            ),

            new(
                "Subnautica_Jun2017",
                "*Early Access* June, 2017",
               5356611662407607386
            ),

            new(
                "Subnautica_Apr2017",
                "*Early Access* Apr, 2017",
                4598449459296387424
            ),

            new(
                "Subnautica_Jan2017",
                "*Early Access* Jan, 2017",
                6480035690178517809
            ),

            new(
                "Subnautica_Dec2016",
                "*Early Access* Dec, 2016",
                6085131123232786834
            ),

            new(
                "Subnautica_Oct2016",
                "*Early Access* Oct, 2016",
                4026479156897042751
            ),

            new(
                "Subnautica_Sep2016",
                "*Early Access* Sep, 2016",
                1940285088991838678
            ),

            new(
                "Subnautica_Aug2016",
                "*Early Access* Aug, 2016",
                8828699716634360096
            ),

            new(
                "Subnautica_Jul2016",
                "*Early Access* Jul, 2016",
                6313368048591876057
            ),

            new(
                "Subnautica_Jun2016",
                "*Early Access* Jun, 2016",
               75943538358159305
            ),

            new(
                "Subnautica_May2016",
                "*Early Access* May, 2016",
               4664821317529819372
            ),

            new(
                "Subnautica_Apr2016",
                "*Early Access* Apr, 2016",
               2384808563326856020
            ),

            new(
                "Subnautica_Mar2016",
                "*Early Access* Mar, 2016",
                7198782486061784410
            ),

            new(
                "Subnautica_Feb2016",
                "*Early Access* Feb, 2016",
                7776709715034860738
            ),

            new(
                "Subnautica_Jan2016",
                "*Early Access* Jan, 2016",
                1580519701516085427
            ),

            new(
                "Subnautica_Dec2015",
                "*Early Access* Dec, 2015",
                1655606525889648403
            ),

            new(
                "Subnautica_Nov2015",
                "*Early Access* Nov, 2015",
               8038542029515181440
            ),

            new(
                "Subnautica_Sep2015",
                "*Early Access* Sep, 2015",
               6594882437028716016
            ),

            new(
                "Subnautica_Aug2015",
                "*Early Access* Aug, 2015",
               6457759372727956870
            ),

            new(
                "Subnautica_Jun2015",
                "*Early Access* Jun, 2015",
               8411266954273912076
            ),

            new(
                "Subnautica_May2015",
                "*Early Access* May, 2015",
               5841781506463269834
            ),

            new(
                "Subnautica_Apr2015",
                "*Early Access* Apr, 2015",
               8185928966442498319
            ),

            new(
                "Subnautica_Mar2015",
                "*Early Access* Mar, 2015",
                5042398543312946015
            ),

            new(
                "Subnautica_Feb2015",
                "*Early Access* Feb, 2015",
                7744883512581895888
            ),

            new(
                "Subnautica_Jan2015",
                "*Early Access* Jan, 2015",
               3432814105944994359
            ),

            new(
                "Subnautica_Dec2014",
                "*Early Access* Dec, 2014",
               7805288174559162834
            ),
        };
}