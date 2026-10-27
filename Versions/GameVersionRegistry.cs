namespace SubnauticaLauncher.Versions;

public sealed class GameVersionDefinition
{
    public GameVersionDefinition(string id, string displayName, long manifestId)
    {
        Id = id;
        DisplayName = displayName;
        ManifestId = manifestId;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public long ManifestId { get; }
}

public static class GameVersionRegistry
{
    public static class Subnautica
    {
        public const int SteamAppId = 264710;
        public const int SteamDepotId = 264712;

        public static IReadOnlyList<GameVersionDefinition> All { get; } =
        [
            new("Subnautica_3Oct2025", "*Latest* 3 Oct 2025 (Subnautica Security Hotfix)", 4495119477327785570),
            new("Subnautica_7Dec2021", "*Legacy* 7 Dec 2021 (Minor Update B89)", 455616957047142657),
            new("Subnautica_30Sep2018", "*Speedrun* 30 Sep 2018 (Speedrunners Version)", 5196577974721678848),
            new("Subnautica_12Aug2025", "12 Aug 2025 (Subnautica 2025 Patch)", 8427274798903478123),
            new("Subnautica_7Mar2023", "7 Mar 2023 (Steam Deck Update)", 3638894940716012854),
            new("Subnautica_13Dec2022", "13 Dec 2022 (Living Large Update)", 7985964044056395698),
            new("Subnautica_30Apr2021", "30 Apr 2021 (2021 Release)", 6416485996936522597),
            new("Subnautica_5Aug2020", "5 Aug 2020", 1908309407776017760),
            new("Subnautica_9Jan2020", "9 Jan 2020 (2020 Release)", 8054727543627818758),
            new("Subnautica_11Dec2019", "11 Dec 2019", 8843040285246122107),
            new("Subnautica_19Nov2019", "19 Nov 2019 (Big Little Update)", 5751432439227487029),
            new("Subnautica_7Nov2019", "7 Nov 2019", 8817635210992009768),
            new("Subnautica_5Nov2019", "5 Nov 2019", 7578413010439027748),
            new("Subnautica_25May2018", "25 May 2018", 4775135007227916113),
            new("Subnautica_23Apr2018", "23 Apr 2018", 4000115425749409787),
            new("Subnautica_8Mar2018_2", "8 Mar 2018 (2nd)", 6710957958784452942),
            new("Subnautica_8Mar2018", "8 Mar 2018", 7962826601715113171),
            new("Subnautica_24Jan2018", "24 Jan 2018", 5598040865097130595),
            new("Subnautica_23Jan2018", "23 Jan 2018", 1082186551939693710),
            new("Subnautica_19Jan2018", "19 Jan 2018", 775423436449659881),
            new("Subnautica_17Jan2018_2", "17 Jan 2018 (2nd)", 4110373882486297086),
            new("Subnautica_17Jan2018", "17 Jan 2018", 4745214041007085306),
            new("Subnautica_16Jan2018", "16 Jan 2018", 3244918507114829941),
            new("Subnautica_11Jan2018", "11 Jan 2018 (First Release)", 3447648779063024334),
            new("Subnautica_12Dec2017", "*Early Access* 12 Dec 2017 (Recent Stable EA)", 9219915328445201166),
            new("Subnautica_7Dec2017", "*Early Access* 7 Dec 2017", 8721704526654136876),
            new("Subnautica_31Oct2017", "*Early Access* 31 Oct 2017", 6038350512483430267),
            new("Subnautica_5Oct2017", "*Early Access* 5 Oct 2017", 1489966536296736688),
            new("Subnautica_14Sep2017", "*Early Access* 14 Sep 2017 (Solid Stable EA)", 3436420285625495570),
            new("Subnautica_21Aug2017", "*Early Access* 21 Aug 2017", 7963555839872105964),
            new("Subnautica_24Jun2017", "*Early Access* 24 Jun 2017", 5356611662407607386),
            new("Subnautica_23Jun2017", "*Early Access* 23 Jun 2017", 8086283335663590861),
            new("Subnautica_21Jun2017", "*Early Access* 21 Jun 2017", 6105791666921504628),
            new("Subnautica_28Apr2017_2", "*Early Access* 28 Apr 2017 (2nd)", 4598449459296387424),
            new("Subnautica_28Apr2017", "*Early Access* 28 Apr 2017", 5905274444617041945),
            new("Subnautica_2Mar2017", "*Early Access* 2 Mar 2017 (Early Stable EA)", 3489434290486619086),
            new("Subnautica_26Jan2017", "*Early Access* 26 Jan 2017", 6480035690178517809),
            new("Subnautica_15Dec2016", "*Early Access* 15 Dec 2016", 6085131123232786834),
            new("Subnautica_29Oct2016", "*Early Access* 29 Oct 2016", 4026479156897042751),
            new("Subnautica_27Oct2016", "*Early Access* 27 Oct 2016", 8649106983434854458),
            new("Subnautica_22Sep2016", "*Early Access* 22 Sep 2016", 1940285088991838678),
            new("Subnautica_23Aug2016", "*Early Access* 23 Aug 2016", 8828699716634360096),
            new("Subnautica_6Jul2016", "*Early Access* 6 Jul 2016", 6313368048591876057),
            new("Subnautica_1Jun2016", "*Early Access* 1 Jun 2016", 75943538358159305),
            new("Subnautica_9May2016", "*Early Access* 9 May 2016", 4664821317529819372),
            new("Subnautica_6May2016", "*Early Access* 6 May 2016", 7628378783742010515),
            new("Subnautica_10Apr2016", "*Early Access* 10 Apr 2016", 2384808563326856020),
            new("Subnautica_5Apr2016_2", "*Early Access* 5 Apr 2016 (2nd)", 9166344840090582047),
            new("Subnautica_5Apr2016", "*Early Access* 5 Apr 2016", 2109821229581594882),
            new("Subnautica_19Mar2016", "*Early Access* 19 Mar 2016", 7198782486061784410),
            new("Subnautica_16Mar2016", "*Early Access* 16 Mar 2016", 1336982092687680910),
            new("Subnautica_25Feb2016", "*Early Access* 25 Feb 2016", 7776709715034860738),
            new("Subnautica_28Jan2016", "*Early Access* 28 Jan 2016", 1580519701516085427),
            new("Subnautica_22Jan2016", "*Early Access* 22 Jan 2016", 1212694301320165232),
            new("Subnautica_19Jan2016", "*Early Access* 19 Jan 2016", 6681909542588491556),
            new("Subnautica_14Jan2016", "*Early Access* 14 Jan 2016", 583910169009050531),
            new("Subnautica_24Dec2015", "*Early Access* 24 Dec 2015", 1655606525889648403),
            new("Subnautica_15Dec2015", "*Early Access* 15 Dec 2015", 7277816528247396310),
            new("Subnautica_21Nov2015", "*Early Access* 21 Nov 2015", 8038542029515181440),
            new("Subnautica_17Nov2015", "*Early Access* 17 Nov 2015", 868590271391810508),
            new("Subnautica_5Nov2015", "*Early Access* 5 Nov 2015", 463793915279153537),
            new("Subnautica_10Sep2015", "*Early Access* 10 Sep 2015", 6594882437028716016),
            new("Subnautica_6Aug2015", "*Early Access* 6 Aug 2015", 6457759372727956870),
            new("Subnautica_19Jun2015", "*Early Access* 19 Jun 2015", 8411266954273912076),
            new("Subnautica_28May2015", "*Early Access* 28 May 2015", 5841781506463269834),
            new("Subnautica_18May2015", "*Early Access* 18 May 2015", 7486576346241353516),
            new("Subnautica_16May2015", "*Early Access* 16 May 2015", 7819851546136108030),
            new("Subnautica_15May2015", "*Early Access* 15 May 2015", 5621703788531038350),
            new("Subnautica_30Apr2015", "*Early Access* 30 Apr 2015", 8185928966442498319),
            new("Subnautica_10Apr2015", "*Early Access* 10 Apr 2015", 5151359364006624878),
            new("Subnautica_9Apr2015", "*Early Access* 9 Apr 2015", 8226210910213036658),
            new("Subnautica_18Mar2015_2", "*Early Access* 18 Mar 2015 (2nd)", 5042398543312946015),
            new("Subnautica_18Mar2015", "*Early Access* 18 Mar 2015", 206696663110566955),
            new("Subnautica_13Mar2015", "*Early Access* 13 Mar 2015", 1499434914373698616),
            new("Subnautica_27Feb2015", "*Early Access* 27 Feb 2015", 7744883512581895888),
            new("Subnautica_5Feb2015", "*Early Access* 5 Feb 2015", 3514506787386438029),
            new("Subnautica_16Jan2015", "*Early Access* 16 Jan 2015", 3432814105944994359),
            new("Subnautica_23Dec2014", "*Early Access* 23 Dec 2014", 4055989805385256494),
            new("Subnautica_16Dec2014", "*Early Access* 16 Dec 2014", 7805288174559162834)
        ];
    }

    // Stored for future Below Zero support; not wired into install/UI yet.
    public static class BelowZero
    {
        public const int SteamAppId = 848450;
        public const int SteamDepotId = 848452;

        public static IReadOnlyList<GameVersionDefinition> All { get; } =
        [
            new("BelowZero_3Oct2025", "*Latest* 3 Oct 2025 (Subnautica Security Hotfix)", 3284616936916893028),
            new("BelowZero_18Aug2021", "*Speedrun* 18 Aug 2021 (Language Pack Patch)", 3036005960129838677),
            new("BelowZero_12Aug2025", "12 Aug 2025 (Below Zero 2025 Patch)", 4887349951766836612),
            new("BelowZero_4Oct2022", "4 Oct 2022 (Teeny-Tiny Hotfix)", 3183944276780659315),
            new("BelowZero_14Sep2022", "14 Sep 2022 (What the Dock Update)", 5173008513109062040),
            new("BelowZero_13Sep2022", "13 Sep 2022", 3800117612749373654),
            new("BelowZero_19Aug2021", "19 Aug 2021", 3173096341325717512),
            new("BelowZero_14May2021", "14 May 2021", 5701798976717775906),
            new("BelowZero_25Feb2021", "25 Feb 2021 (Seaworthy Update)", 3484037291168779440),
            new("BelowZero_4Dec2020", "*Early Access* 4 Dec 2020 (Teeny-Tiny Update)", 4996545525299039845),
            new("BelowZero_29Oct2020", "*Early Access* 29 Oct 2020", 5742455615835309040),
            new("BelowZero_13Aug2020", "*Early Access* 13 Aug 2020", 3395134997758555135),
            new("BelowZero_20Jul2020", "*Early Access* 20 Jul 2020", 7989735351161191407),
            new("BelowZero_30Jun2020", "*Early Access* 30 Jun 2020", 3590460764832737788),
            new("BelowZero_25Jun2020", "*Early Access* 25 Jun 2020", 1490086949325473156),
            new("BelowZero_30Apr2020", "*Early Access* 30 Apr 2020 (Frostbite Update)", 789060194330636471),
            new("BelowZero_19Feb2020", "*Early Access* 19 Feb 2020", 1573152149863871875),
            new("BelowZero_27Nov2019", "*Early Access* 27 Nov 2019 (Deep Dive Update)", 150054511446098676),
            new("BelowZero_17Sep2019", "*Early Access* 17 Sep 2019", 6320496991720799758),
            new("BelowZero_18Jul2019", "*Early Access* 18 Jul 2019", 1877676728833435366),
            new("BelowZero_17Jul2019", "*Early Access* 17 Jul 2019", 4086396465666002355),
            new("BelowZero_5Jun2019", "*Early Access* 5 Jun 2019", 6031371122074479145),
            new("BelowZero_13May2019", "*Early Access* 13 May 2019", 9015759517461719539),
            new("BelowZero_24Apr2019", "*Early Access* 24 Apr 2019", 1468389225676593976),
            new("BelowZero_8Mar2019", "*Early Access* 8 Mar 2019", 7109419288448066306),
            new("BelowZero_11Feb2019", "*Early Access* 11 Feb 2019", 4604359450461112821),
            new("BelowZero_30Jan2019", "*Early Access* 30 Jan 2019 (Early Access Release)", 3828807225573055106)
        ];
    }
}