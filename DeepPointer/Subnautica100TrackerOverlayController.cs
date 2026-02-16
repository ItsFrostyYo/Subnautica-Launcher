using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Application = System.Windows.Application;
using System.Windows;
using System.Windows.Media.Animation;

namespace SubnauticaLauncher.Gameplay
{
    public static class Subnautica100TrackerOverlayController
    {
        private const int OverlayPadding = 12;
        private const int MaxAliasLength = 96;

        private static readonly object Sync = new();
        private static readonly Regex CookedCuredChecklistRegex = new(@"^Cooked\s*\+\s*Cured\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CookedCuredPairingRegex = new(@"^Cooked\s*\+\s*Cured\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingCountRegex = new(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);
        private static readonly Regex TrailingIdRegex = new(@"\s*\((\d+)\)\s*$", RegexOptions.Compiled);
        private static readonly Regex TokenSplitRegex = new(@"[^A-Za-z0-9]+", RegexOptions.Compiled);
        private static readonly HashSet<string> AliasStopWords = new(StringComparer.Ordinal)
        {
            "the", "and", "of", "to", "on", "for", "in", "with", "a", "an",
            "unlocked", "unlock", "entry"
        };

        private static readonly HashSet<string> RequiredBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> RequiredDatabankEntries = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PreInstalledBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PreInstalledDatabankEntries = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, HashSet<string>> BlueprintAliasIndex = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> DatabankAliasIndex = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, HashSet<string>> BlueprintRequirementsByTechType = new();
        private static readonly Dictionary<string, string> BlueprintDisplayNames = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> DatabankDisplayNames = new(StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<string, string> DatabankLocalizedRequirementByKey =
            SubnauticaDatabankLocalizationMap.KeyToRequirement;

        private static readonly HashSet<string> RunBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> RunDatabankEntries = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<string>> BiomeBlueprintRequirementsByCanonical = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<string>> BiomeDatabankRequirementsByCanonical = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> BiomeCanonicalByDisplay = new(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> LoggedUnmatchedBlueprintAliases = new(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedUnmatchedDatabankAliases = new(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedUnmatchedBiomePairingEntries = new(StringComparer.OrdinalIgnoreCase);

        // Common internal-name to checklist-name mismatches and shorthand aliases.
        private static readonly Dictionary<string, string[]> SpecialBlueprintAliasMap = new(StringComparer.Ordinal)
        {
            ["airsack"] = new[] { "bladderfish", "cookedbladderfish", "curedbladderfish" },
            ["bladderfishanalysis"] = new[] { "bladderfish", "cookedbladderfish", "curedbladderfish" },
            ["lavaboomerang"] = new[] { "magmarang", "cookedmagmarang", "curedmagmarang" },
            ["lavaeyeye"] = new[] { "redeyeye", "cookedredeyeye", "curedredeyeye" },
            ["exosuit"] = new[] { "prawnsuit" },
            ["prawnsuit"] = new[] { "prawnsuit" },
            ["tank"] = new[] { "standardo2tank" },
            ["doubletank"] = new[] { "highcapacityo2tank" },
            ["highcapacitytank"] = new[] { "lightweighthighcapacitytank" },
            ["plasteeltank"] = new[] { "ultrahighcapacitytank" },
            ["aramidfibers"] = new[] { "syntheticfibers" },
            ["welder"] = new[] { "repairtool" },
            ["knife"] = new[] { "survivalknife" },
            ["builder"] = new[] { "habitatbuilder" },
            ["pipesurfacefloater"] = new[] { "floatingairpump" },
            ["divereel"] = new[] { "pathfindertool" },
            ["ledlight"] = new[] { "lightstick" },
            ["techlight"] = new[] { "floodlight" },
            ["waterfiltrationsuit"] = new[] { "stillsuit" },
            ["heatblade"] = new[] { "thermoblade" },
            ["constructor"] = new[] { "mobilevehiclebay" },
            ["smallstorage"] = new[] { "waterprooflocker" },
            ["medicalcabinet"] = new[] { "medicalkitfabricator" },
            ["workbench"] = new[] { "modificationstation" },
            ["gravsphere"] = new[] { "gravtrap" },
            ["cyclopsdecoy"] = new[] { "creaturedecoy" },
            ["baseroom"] = new[] { "multipurposeroom" },
            ["basemaproom"] = new[] { "scannerroom" },
            ["maproomhudchip"] = new[] { "scannerroomhudchip" },
            ["maproomcamera"] = new[] { "cameradrone" },
            ["maproomupgradescanrange"] = new[] { "scannerroomrangeupgrade" },
            ["maproomupgradescanspeed"] = new[] { "scannerroomspeedupgrade" },
            ["baseobservatory"] = new[] { "observatory" },
            ["basemoonpool"] = new[] { "moonpool" },
            ["basewaterpark"] = new[] { "aliencontainment" },
            ["basebioreactor"] = new[] { "bioreactor" },
            ["basenuclearreactor"] = new[] { "nuclearreactor" },
            ["basebulkhead"] = new[] { "bulkhead" },
            ["baseupgradeconsole"] = new[] { "vehicleupgradeconsole" },
            ["basefiltrationmachine"] = new[] { "waterfiltrationmachine" },
            ["filtrationmachine"] = new[] { "waterfiltrationmachine" },
            ["hullreinforcementmodule"] = new[] { "hullreinforcement" },
            ["vehiclepowerupgrademodule"] = new[] { "engineefficiencymodule" },
            ["powerupgrademodule"] = new[] { "cyclopsengineefficiencymodule" },
            ["vehiclestoragemodule"] = new[] { "storagemodule" },
            ["seamothsolarcharge"] = new[] { "seamothsolarcharger" },
            ["seamothelectricaldefense"] = new[] { "seamothperimeterdefensesystem" },
            ["seamothtorpedomodule"] = new[] { "seamothtorpedosystem" },
            ["seamothsonarmodule"] = new[] { "seamothsonar" },
            ["vehiclehullmodule1"] = new[] { "seamothdepthmodulemk1" },
            ["vehiclehullmodule2"] = new[] { "seamothdepthmodulemk2" },
            ["vehiclehullmodule3"] = new[] { "seamothdepthmodulemk3" },
            ["exohullmodule1"] = new[] { "prawnsuitdepthmodulemk1" },
            ["exohullmodule2"] = new[] { "prawnsuitdepthmodulemk2" },
            ["exosuitthermalreactormodule"] = new[] { "prawnsuitthermalreactor" },
            ["exosuitjetupgrademodule"] = new[] { "prawnsuitjumpjetupgrade" },
            ["exosuitpropulsionarmmodule"] = new[] { "prawnsuitpropulsioncannon" },
            ["exosuitgrapplingarmmodule"] = new[] { "prawnsuitgrapplingarm" },
            ["exosuitdrillarmmodule"] = new[] { "prawnsuitdrillarm" },
            ["exosuittorpedoarmmodule"] = new[] { "prawnsuittorpedoarm" },
            ["whirlpooltorpedo"] = new[] { "vortextorpedo" },
            ["precursorkeypurple"] = new[] { "purpletablet" },
            ["precursorkeyblue"] = new[] { "bluetablet" },
            ["precursorkeyorange"] = new[] { "orangetablet" },
            ["cyclopshullmodule1"] = new[] { "cyclopsdepthmodulemk1" },
            ["cyclopshullmodule2"] = new[] { "cyclopsdepthmodulemk2" },
            ["cyclopshullmodule3"] = new[] { "cyclopsdepthmodulemk3" },
            ["cyclopsshieldmodule"] = new[] { "cyclopsshieldgenerator" },
            ["cyclopssonarmodule"] = new[] { "cyclopssonarupgrade" },
            ["cyclopsdecoymodule"] = new[] { "cyclopsdecoytubeupgrade" },
            ["cyclopsfiresuppressionmodule"] = new[] { "cyclopsfiresuppressionsystem" },
            ["cyclopsseamothrepairmodule"] = new[] { "cyclopsdockingbayrepairmodule" },
            ["precursorionbattery"] = new[] { "ionbattery" },
            ["precursorionpowercell"] = new[] { "ionpowercell" },
            ["rocketbase"] = new[] { "neptunelaunchplatform" },
            ["rocketbaseladder"] = new[] { "neptunegantry" },
            ["rocketstage1"] = new[] { "neptuneionboosters" },
            ["rocketstage2"] = new[] { "neptunefuelreserve" },
            ["rocketstage3"] = new[] { "neptunecockpit" },
            ["starshipdesk"] = new[] { "desk" },
            ["starshipchair"] = new[] { "swivelchair" },
            ["starshipchair2"] = new[] { "officechair" },
            ["starshipchair3"] = new[] { "commandchair" },
            ["bed1"] = new[] { "basicdoublebed" },
            ["bed2"] = new[] { "quilteddoublebed" },
            ["narrowbed"] = new[] { "singlebed" },
            ["trashcans"] = new[] { "trashcan" },
            ["labtrashcan"] = new[] { "nuclearwastedisposal" },
            ["labcounter"] = new[] { "counter" },
            ["planterpot"] = new[] { "basicplantpot" },
            ["planterpot2"] = new[] { "compositeplantpot" },
            ["planterpot3"] = new[] { "chicplantpot" },
            ["planterbox"] = new[] { "indoorgrowbed" },
            ["farmingtray"] = new[] { "exteriorgrowbed" },
            ["baseplanter"] = new[] { "wallplanter" },
            ["plantershelf"] = new[] { "plantshelf" },
            ["marki2"] = new[] { "anunusualdoll" },
            ["waterfiltrationsystem"] = new[] { "waterfiltrationmachine" },
            ["neptuneescaperocket"] = new[] { "neptunelaunchplatform", "neptunegantry", "neptuneionboosters", "neptunefuelreserve", "neptunecockpit" }
        };

        private static readonly Dictionary<string, string[]> SpecialDatabankAliasMap = new(StringComparer.Ordinal)
        {
            ["airsack"] = new[] { "bladderfish" },
            ["bladderfishanalysis"] = new[] { "bladderfish" },
            ["lavaeyeye"] = new[] { "redeyeye" },
            ["lavaboomerang"] = new[] { "magmarang" },
            ["bigcoraltubes"] = new[] { "giantcoraltubes" },
            ["metalsalvage"] = new[] { "scatteredwreckage" },
            ["metalsavlage"] = new[] { "scatteredwreckage" },
            ["droopingstingers"] = new[] { "droopingstinger" },
            ["spottedreeds"] = new[] { "spotteddockleaf" },
            ["bulbbush"] = new[] { "giantbulbbush" },
            ["treecovetree"] = new[] { "giantcovetree" },
            ["prawnsuit"] = new[] { "prawnsuitmkiii" },
            ["exo"] = new[] { "prawnsuitmkiii" },
            ["indoorgrowbed"] = new[] { "interiorgrowbed" },
            ["bulkhead"] = new[] { "bulkheaddoor" },
            ["marblemelon"] = new[] { "marblemelonplant" },
            ["seaemperorleviathanjuvenile"] = new[] { "seaemperorjuvenile" },
            ["seaemperorleviathaneggs"] = new[] { "theseaemperorseggs" },
            ["seaemperorleviathan"] = new[] { "seaemperorleviathanresearchdata" },
            ["lifepod12medicalofficerdanbyscrewlog"] = new[] { "lifepod12medicaloffierdanbyscrewlog" },
            ["hatchingenzymes"] = new[] { "hatchingenzymes", "hatchingenzymesold" },
            ["curedcreature"] = new[] { "specimenwithinfectionsymptomsinhibited" },
            ["infection"] = new[] { "specimenwithsymptomsofinfection" },
            ["reinforceddivesuit"] = new[] { "reinforcedsuit" },
            ["aliencontainment"] = new[] { "waterpark" },
            ["creaturedecoy"] = new[] { "decoy" }
        };

        private static readonly IReadOnlyDictionary<int, string> TechTypeDatabase = TechTypeNames.GetAll();

        private static Subnautica100TrackerOverlay? _window;
        private static SubnauticaBiomeTrackerOverlay? _biomeWindow;
        private static Subnautica100UnlockToastOverlay? _toastWindow;
        private static CancellationTokenSource? _cts;
        private static CancellationTokenSource? _toastDisplayCts;
        private static Task? _loopTask;
        private static bool _runActive;
        private static bool _rulesLoaded;
        private static bool _toastVisible;
        private static double _overlayLeft;
        private static double _overlayTop;
        private static bool _runStartedFromCreative;
        private static string _currentBiomeCanonical = string.Empty;
        private static double _biomeScrollOffset;
        private static DateTime _lastBiomeScrollUtc = DateTime.MinValue;
        private static bool _collectPreRunUnlocks;
        private static readonly HashSet<string> PendingPreRunBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PendingPreRunDatabankEntries = new(StringComparer.Ordinal);
        private static readonly string[] CreativeExcludedDatabankRawNames =
        {
            "Bioreactor",
            "Nuclear Reactor",
            "Thermal Plant",
            "Alien Containment",
            "Creature Decoy",
            "Reinforced Dive Suit",
            "Repulsion Cannon",
            "Modification Station",
            "Moonpool",
            "Cyclops"
        };
        private static readonly SemaphoreSlim ToastSemaphore = new(1, 1);
        private static int RequiredBlueprintTotal => RequiredBlueprints.Count;
        private static int RequiredDatabankTotal => RequiredDatabankEntries.Count;
        private static int RequiredCombinedTotal => RequiredBlueprintTotal + RequiredDatabankTotal;

        private readonly record struct BiomeCycleItem(bool IsBlueprint, string Requirement);
        private readonly record struct BiomeDisplayFrame(
            IReadOnlyList<(string Type, string Name)> TopRow,
            IReadOnlyList<(string Type, string Name)> BottomRow,
            int RowCount,
            int ColumnsPerRow,
            double ScrollProgress);

        public static void Start()
        {
            lock (Sync)
            {
                if (_loopTask != null)
                    return;

                EnsureRulesLoaded();
                ResetRunState();

                GameEventDocumenter.EventWritten += OnEventWritten;
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => OverlayLoopAsync(_cts.Token));
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;
            Task? loopTask;
            CancellationTokenSource? toastCts;

            lock (Sync)
            {
                cts = _cts;
                loopTask = _loopTask;
                toastCts = _toastDisplayCts;
                _cts = null;
                _toastDisplayCts = null;
                _loopTask = null;
                _runActive = false;
                _toastVisible = false;
                _currentBiomeCanonical = string.Empty;
                _biomeScrollOffset = 0;
                _lastBiomeScrollUtc = DateTime.MinValue;
                _collectPreRunUnlocks = false;
                RunBlueprints.Clear();
                RunDatabankEntries.Clear();
                PendingPreRunBlueprints.Clear();
                PendingPreRunDatabankEntries.Clear();
            }

            GameEventDocumenter.EventWritten -= OnEventWritten;

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    loopTask?.Wait(1500);
                }
                catch
                {
                    // best effort shutdown
                }
                finally
                {
                    cts.Dispose();
                }
            }

            if (toastCts != null)
            {
                try
                {
                    toastCts.Cancel();
                }
                catch
                {
                    // best effort shutdown
                }
                finally
                {
                    toastCts.Dispose();
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
                _biomeWindow?.Close();
                _biomeWindow = null;
                _toastWindow?.Close();
                _toastWindow = null;
            });
        }

        private static void EnsureRulesLoaded()
        {
            if (_rulesLoaded)
                return;

            SubnauticaChecklistCatalog checklistCatalog = SubnauticaChecklistCatalog.GetOrLoad();
            ApplyChecklistCatalog(checklistCatalog);
            BuildBlueprintTechTypeRequirementIndex();

            SubnauticaUnlockPairingCatalog pairingCatalog = SubnauticaUnlockPairingCatalog.GetOrLoad();
            if (pairingCatalog.Groups.Count == 0)
                Logger.Warn("[100Tracker] Biome unlock pairing database is empty.");
            BuildBiomeRequirementIndex(pairingCatalog);

            _rulesLoaded = true;

            Logger.Log(
                $"[100Tracker] Database loaded. " +
                $"techTypes={TechTypeDatabase.Count}, " +
                $"requiredBlueprints={RequiredBlueprints.Count} (pre={PreInstalledBlueprints.Count}), " +
                $"requiredDatabank={RequiredDatabankEntries.Count} (pre={PreInstalledDatabankEntries.Count}), " +
                $"unlockableBlueprints={Math.Max(0, RequiredBlueprints.Count - PreInstalledBlueprints.Count)}, " +
                $"unlockableDatabank={Math.Max(0, RequiredDatabankEntries.Count - PreInstalledDatabankEntries.Count)}, " +
                $"techTypeBlueprintMappings={BlueprintRequirementsByTechType.Count}, " +
                $"targetTotal={RequiredCombinedTotal}");
        }

        private static void ApplyChecklistCatalog(SubnauticaChecklistCatalog catalog)
        {
            RequiredBlueprints.Clear();
            RequiredDatabankEntries.Clear();
            PreInstalledBlueprints.Clear();
            PreInstalledDatabankEntries.Clear();
            BlueprintAliasIndex.Clear();
            DatabankAliasIndex.Clear();
            BlueprintRequirementsByTechType.Clear();
            BlueprintDisplayNames.Clear();
            DatabankDisplayNames.Clear();

            foreach (SubnauticaChecklistCatalog.ChecklistEntry entry in catalog.BlueprintEntries)
            {
                string normalizedName = NormalizeChecklistName(entry.Name);
                if (normalizedName.Length == 0)
                    continue;

                if (TryExpandCookedCuredRequirement(entry.Name, entry.IsPreInstalled))
                    continue;

                AddRequirement(ParseMode.Blueprint, normalizedName, entry.Name, entry.IsPreInstalled);
            }

            foreach (SubnauticaChecklistCatalog.ChecklistEntry entry in catalog.DatabankEntries)
            {
                string normalizedName = NormalizeChecklistName(entry.Name);
                if (normalizedName.Length == 0)
                    continue;

                AddRequirement(ParseMode.Databank, normalizedName, entry.Name, entry.IsPreInstalled);
            }

            AddDerivedRequirementAliases();
        }

        private static bool TryExpandCookedCuredRequirement(string rawName, bool preInstalled)
        {
            Match match = CookedCuredChecklistRegex.Match(rawName ?? string.Empty);
            if (!match.Success)
                return false;

            string fishRaw = match.Groups[1].Value.Trim();
            if (fishRaw.Length == 0)
                return false;

            string cookedRaw = "Cooked " + fishRaw;
            string curedRaw = "Cured " + fishRaw;
            string cookedNormalized = NormalizeChecklistName(cookedRaw);
            string curedNormalized = NormalizeChecklistName(curedRaw);

            if (cookedNormalized.Length == 0 || curedNormalized.Length == 0)
                return false;

            AddRequirement(ParseMode.Blueprint, cookedNormalized, cookedRaw, preInstalled);
            AddRequirement(ParseMode.Blueprint, curedNormalized, curedRaw, preInstalled);
            return true;
        }

        private static void BuildBlueprintTechTypeRequirementIndex()
        {
            BlueprintRequirementsByTechType.Clear();

            foreach (var kv in TechTypeDatabase)
            {
                int techTypeId = kv.Key;
                string enumName = kv.Value;
                if (string.IsNullOrWhiteSpace(enumName))
                    continue;

                var aliases = new HashSet<string>(StringComparer.Ordinal);
                AddAliasCandidate(aliases, NormalizeChecklistName(enumName));
                AddAliasCandidate(aliases, NormalizeChecklistName(HumanizeIdentifier(enumName)));
                AddAliasCandidate(aliases, BuildTokenAlias(enumName));
                AddAliasCandidate(aliases, BuildTokenAlias(HumanizeIdentifier(enumName)));
                ExpandAliasesInPlace(aliases, ExpandBlueprintAlias);

                var matches = new HashSet<string>(StringComparer.Ordinal);
                foreach (string alias in aliases)
                {
                    if (!BlueprintAliasIndex.TryGetValue(alias, out HashSet<string>? mapped))
                        continue;

                    foreach (string requirement in mapped)
                        matches.Add(requirement);
                }

                if (matches.Count > 0)
                    BlueprintRequirementsByTechType[techTypeId] = matches;
            }
        }

        private static void BuildBiomeRequirementIndex(SubnauticaUnlockPairingCatalog pairingCatalog)
        {
            BiomeBlueprintRequirementsByCanonical.Clear();
            BiomeDatabankRequirementsByCanonical.Clear();
            BiomeCanonicalByDisplay.Clear();

            foreach (SubnauticaUnlockPairingCatalog.BiomeUnlockGroup group in pairingCatalog.Groups)
            {
                if (string.IsNullOrWhiteSpace(group.CanonicalBiomeKey))
                    continue;

                if (!BiomeCanonicalByDisplay.ContainsKey(group.DisplayName))
                    BiomeCanonicalByDisplay[group.DisplayName] = group.CanonicalBiomeKey;

                var mappedBlueprints = new List<string>();
                var mappedDatabanks = new List<string>();
                var blueprintSeen = new HashSet<string>(StringComparer.Ordinal);
                var databankSeen = new HashSet<string>(StringComparer.Ordinal);

                SubnauticaUnlockPairingCatalog.BiomeCycleEntries cycleEntries =
                    SubnauticaUnlockPairingCatalog.GetCycleEntries(group);

                foreach (string pairingEntry in cycleEntries.BlueprintEntries)
                {
                    HashSet<string> matches = ResolveBlueprintPairingMatches(pairingEntry);
                    if (matches.Count == 0)
                    {
                        LogUnmatchedBiomePairing(group.DisplayName, "Blueprint", pairingEntry);
                        continue;
                    }

                    foreach (string requirement in matches)
                    {
                        if (blueprintSeen.Add(requirement))
                            mappedBlueprints.Add(requirement);
                    }
                }

                foreach (string pairingEntry in cycleEntries.DatabankEntries)
                {
                    HashSet<string> matches = ResolveDatabankMatches(pairingEntry);
                    if (matches.Count == 0)
                    {
                        LogUnmatchedBiomePairing(group.DisplayName, "Databank", pairingEntry);
                        continue;
                    }

                    foreach (string requirement in matches)
                    {
                        if (databankSeen.Add(requirement))
                            mappedDatabanks.Add(requirement);
                    }
                }

                BiomeBlueprintRequirementsByCanonical[group.CanonicalBiomeKey] = mappedBlueprints;
                BiomeDatabankRequirementsByCanonical[group.CanonicalBiomeKey] = mappedDatabanks;
            }
        }

        private static void AddRequirement(ParseMode mode, string normalizedName, string rawName, bool preInstalled)
        {
            string tokenAlias = BuildTokenAlias(rawName);
            string displayName = BuildDisplayName(rawName, normalizedName);

            if (mode == ParseMode.Blueprint)
            {
                RequiredBlueprints.Add(normalizedName);
                if (preInstalled)
                    PreInstalledBlueprints.Add(normalizedName);

                AddAlias(BlueprintAliasIndex, normalizedName, normalizedName);
                AddAlias(BlueprintAliasIndex, tokenAlias, normalizedName);
                if (!BlueprintDisplayNames.ContainsKey(normalizedName))
                    BlueprintDisplayNames[normalizedName] = displayName;
            }
            else
            {
                RequiredDatabankEntries.Add(normalizedName);
                if (preInstalled)
                    PreInstalledDatabankEntries.Add(normalizedName);

                AddAlias(DatabankAliasIndex, normalizedName, normalizedName);
                AddAlias(DatabankAliasIndex, tokenAlias, normalizedName);
                if (!DatabankDisplayNames.ContainsKey(normalizedName))
                    DatabankDisplayNames[normalizedName] = displayName;
            }
        }

        private static void AddDerivedRequirementAliases()
        {
            foreach (string requirement in RequiredBlueprints)
            {
                if (!requirement.StartsWith("cookedcured", StringComparison.Ordinal))
                    continue;

                string fish = requirement.Substring("cookedcured".Length);
                if (fish.Length == 0)
                    continue;

                AddAlias(BlueprintAliasIndex, fish, requirement);
                AddAlias(BlueprintAliasIndex, "cooked" + fish, requirement);
                AddAlias(BlueprintAliasIndex, "cured" + fish, requirement);

                if (fish == "bladderfish")
                {
                    AddAlias(BlueprintAliasIndex, "airsack", requirement);
                    AddAlias(BlueprintAliasIndex, "cookedairsack", requirement);
                    AddAlias(BlueprintAliasIndex, "curedairsack", requirement);
                }
                else if (fish == "magmarang")
                {
                    AddAlias(BlueprintAliasIndex, "lavaboomerang", requirement);
                    AddAlias(BlueprintAliasIndex, "cookedlavaboomerang", requirement);
                    AddAlias(BlueprintAliasIndex, "curedlavaboomerang", requirement);
                }
                else if (fish == "redeyeye")
                {
                    AddAlias(BlueprintAliasIndex, "lavaeyeye", requirement);
                    AddAlias(BlueprintAliasIndex, "cookedlavaeyeye", requirement);
                    AddAlias(BlueprintAliasIndex, "curedlavaeyeye", requirement);
                }
            }
        }

        private static void AddAlias(Dictionary<string, HashSet<string>> aliasIndex, string alias, string requirement)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(requirement))
                return;

            if (!aliasIndex.TryGetValue(alias, out HashSet<string>? targets))
            {
                targets = new HashSet<string>(StringComparer.Ordinal);
                aliasIndex[alias] = targets;
            }

            targets.Add(requirement);
        }

        private static void OnEventWritten(GameplayEvent evt)
        {
            if (!evt.Game.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                return;

            lock (Sync)
            {
                if (evt.Type == GameplayEventType.GameStateChanged)
                {
                    string state = NormalizeEventName(evt.Key);
                    if (state == "mainmenu")
                    {
                        ResetRunState();
                        UpdateOverlayText();
                        return;
                    }

                    if (state == "ingame")
                    {
                        _collectPreRunUnlocks = true;
                        PendingPreRunBlueprints.Clear();
                        PendingPreRunDatabankEntries.Clear();
                    }

                    return;
                }

                if (evt.Type == GameplayEventType.RunStarted)
                {
                    bool creativeStart = IsCreativeRunStart(evt.Key);
                    bool survivalStart = IsSurvivalRunStart(evt.Key);

                    if (!_runActive)
                    {
                        StartRunState(creativeStart);
                        if (creativeStart)
                            ApplyCreativeDatabankExclusions();
                        ApplyPendingPreRunUnlocks();
                        Logger.Log($"[100Tracker] Run started from RunStarted event. reason={evt.Key}");
                        UpdateOverlayText();
                    }
                    else if (_runStartedFromCreative && survivalStart)
                    {
                        Logger.Log(
                            $"[100Tracker] Run start fallback: restarting from creative provisional start to survival start. reason={evt.Key}");
                        StartRunState(creativeStart: false);
                        ApplyPendingPreRunUnlocks();
                        UpdateOverlayText();
                    }

                    return;
                }

                if (evt.Type == GameplayEventType.BiomeChanged)
                {
                    SetCurrentBiome(evt.Key);
                    return;
                }

                if (evt.Type == GameplayEventType.BlueprintUnlocked)
                {
                    var matches = ResolveBlueprintMatches(evt.Key);

                    if (!_runActive)
                    {
                        BufferPreRunUnlocks(matches, PendingPreRunBlueprints);
                        if (matches.Count == 0)
                            LogUnmatchedBlueprint(evt.Key);
                        return;
                    }

                    int added = 0;
                    var newlyAdded = new List<string>();
                    foreach (string requirement in matches)
                    {
                        if (RunBlueprints.Add(requirement))
                        {
                            added++;
                            newlyAdded.Add(requirement);
                        }
                    }

                    if (added > 0)
                    {
                        UpdateOverlayText();
                        foreach (string requirement in newlyAdded)
                            QueueUnlockToast(GameplayEventType.BlueprintUnlocked, requirement, evt.Key);
                    }
                    else
                    {
                        LogUnmatchedBlueprint(evt.Key);
                    }

                    return;
                }

                if (evt.Type == GameplayEventType.DatabankEntryUnlocked)
                {
                    var matches = ResolveDatabankMatches(evt.Key);

                    if (!_runActive)
                    {
                        BufferPreRunUnlocks(matches, PendingPreRunDatabankEntries);
                        if (matches.Count == 0)
                            LogUnmatchedDatabank(evt.Key);
                        return;
                    }

                    int added = 0;
                    var newlyAdded = new List<string>();
                    foreach (string requirement in matches)
                    {
                        if (RunDatabankEntries.Add(requirement))
                        {
                            added++;
                            newlyAdded.Add(requirement);
                        }
                    }

                    if (added > 0)
                    {
                        UpdateOverlayText();
                        foreach (string requirement in newlyAdded)
                            QueueUnlockToast(GameplayEventType.DatabankEntryUnlocked, requirement, evt.Key);
                    }
                    else
                    {
                        LogUnmatchedDatabank(evt.Key);
                    }
                }
            }
        }

        private static void BufferPreRunUnlocks(HashSet<string> matches, HashSet<string> pending)
        {
            if (!_collectPreRunUnlocks || matches.Count == 0)
                return;

            foreach (string requirement in matches)
                pending.Add(requirement);
        }

        private static void ApplyPendingPreRunUnlocks()
        {
            int addedBlueprints = 0;
            int addedDatabank = 0;

            foreach (string requirement in PendingPreRunBlueprints)
            {
                if (RunBlueprints.Add(requirement))
                    addedBlueprints++;
            }

            foreach (string requirement in PendingPreRunDatabankEntries)
            {
                if (RunDatabankEntries.Add(requirement))
                    addedDatabank++;
            }

            PendingPreRunBlueprints.Clear();
            PendingPreRunDatabankEntries.Clear();
            _collectPreRunUnlocks = false;

            if (addedBlueprints > 0 || addedDatabank > 0)
            {
                Logger.Log(
                    $"[100Tracker] Applied pre-run unlocks: blueprints={addedBlueprints}, databank={addedDatabank}");
            }
        }

        private static bool IsCreativeRunStart(string runStartKey)
        {
            string normalized = NormalizeEventName(runStartKey);
            return normalized.StartsWith("creative", StringComparison.Ordinal)
                || normalized.StartsWith("fallback", StringComparison.Ordinal);
        }

        private static bool IsSurvivalRunStart(string runStartKey)
        {
            string normalized = NormalizeEventName(runStartKey);
            return normalized == "cutsceneskipped"
                || normalized == "lifepodradiodamaged"
                || normalized == "cutscenedetectedaftercreativestart";
        }

        private static void ApplyCreativeDatabankExclusions()
        {
            int added = 0;

            foreach (string rawName in CreativeExcludedDatabankRawNames)
            {
                foreach (string requirement in ResolveDatabankMatches(rawName))
                {
                    if (RunDatabankEntries.Add(requirement))
                        added++;
                }
            }

            if (added > 0)
                Logger.Log($"[100Tracker] Applied creative databank exclusions: count={added}");
        }

        private static HashSet<string> ResolveBlueprintMatches(string eventKey)
        {
            var matches = new HashSet<string>(StringComparer.Ordinal);

            if (TryExtractTechTypeId(eventKey, out int techTypeId) &&
                BlueprintRequirementsByTechType.TryGetValue(techTypeId, out HashSet<string>? mappedById))
            {
                foreach (string requirement in mappedById)
                    matches.Add(requirement);

                return matches;
            }

            foreach (string alias in EnumerateBlueprintAliases(eventKey))
            {
                if (!BlueprintAliasIndex.TryGetValue(alias, out HashSet<string>? mapped))
                    continue;

                foreach (string requirement in mapped)
                    matches.Add(requirement);
            }

            return matches;
        }

        private static HashSet<string> ResolveBlueprintPairingMatches(string pairingEntry)
        {
            HashSet<string> directMatches = ResolveBlueprintMatches(pairingEntry);
            if (directMatches.Count > 0)
                return directMatches;

            Match cookedCuredMatch = CookedCuredPairingRegex.Match(pairingEntry ?? string.Empty);
            if (!cookedCuredMatch.Success)
                return directMatches;

            string fishName = cookedCuredMatch.Groups[1].Value.Trim();
            if (fishName.Length == 0)
                return directMatches;

            HashSet<string> expanded = ResolveBlueprintMatches("Cooked " + fishName);
            foreach (string requirement in ResolveBlueprintMatches("Cured " + fishName))
                expanded.Add(requirement);

            return expanded;
        }

        private static HashSet<string> ResolveDatabankMatches(string eventKey)
        {
            var matches = new HashSet<string>(StringComparer.Ordinal);

            string normalizedEventKey = NormalizeEventName(eventKey);
            if (DatabankLocalizedRequirementByKey.TryGetValue(normalizedEventKey, out string? mappedRequirement))
                matches.Add(mappedRequirement);

            foreach (string alias in EnumerateDatabankAliases(eventKey))
            {
                if (!DatabankAliasIndex.TryGetValue(alias, out HashSet<string>? mapped))
                    continue;

                foreach (string requirement in mapped)
                    matches.Add(requirement);
            }

            return matches;
        }

        private static IEnumerable<string> EnumerateBlueprintAliases(string eventKey)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            AddAliasCandidate(aliases, NormalizeEventName(eventKey));
            AddAliasCandidate(aliases, BuildTokenAlias(eventKey));

            if (TryExtractTechTypeId(eventKey, out int id) &&
                TechTypeDatabase.TryGetValue(id, out var enumName) &&
                !string.IsNullOrWhiteSpace(enumName))
            {
                AddAliasCandidate(aliases, NormalizeChecklistName(enumName));
                AddAliasCandidate(aliases, NormalizeChecklistName(HumanizeIdentifier(enumName)));
                AddAliasCandidate(aliases, BuildTokenAlias(enumName));
                AddAliasCandidate(aliases, BuildTokenAlias(HumanizeIdentifier(enumName)));
            }

            ExpandAliasesInPlace(aliases, ExpandBlueprintAlias);
            return aliases;
        }

        private static IEnumerable<string> EnumerateDatabankAliases(string eventKey)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            AddAliasCandidate(aliases, NormalizeEventName(eventKey));
            AddAliasCandidate(aliases, BuildTokenAlias(eventKey));

            if (TryExtractTechTypeId(eventKey, out int id) &&
                TechTypeDatabase.TryGetValue(id, out var enumName) &&
                !string.IsNullOrWhiteSpace(enumName))
            {
                AddAliasCandidate(aliases, NormalizeChecklistName(enumName));
                AddAliasCandidate(aliases, NormalizeChecklistName(HumanizeIdentifier(enumName)));
                AddAliasCandidate(aliases, BuildTokenAlias(enumName));
                AddAliasCandidate(aliases, BuildTokenAlias(HumanizeIdentifier(enumName)));
            }

            ExpandAliasesInPlace(aliases, ExpandDatabankAlias);
            return aliases;
        }

        private static void ExpandAliasesInPlace(HashSet<string> aliases, Func<string, IEnumerable<string>> expander)
        {
            var queue = new Queue<string>(aliases);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string expanded in expander(current))
                {
                    if (string.IsNullOrWhiteSpace(expanded) || expanded.Length > MaxAliasLength)
                        continue;

                    if (aliases.Add(expanded))
                        queue.Enqueue(expanded);
                }
            }
        }

        private static IEnumerable<string> ExpandBlueprintAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                yield break;

            string stripped = StripCommonSuffixes(alias);
            if (!string.Equals(alias, stripped, StringComparison.Ordinal))
                yield return stripped;

            if (alias.StartsWith("cookedcured", StringComparison.Ordinal) &&
                alias.Length > "cookedcured".Length)
            {
                string fish = alias.Substring("cookedcured".Length);
                yield return fish;
            }
            else if (alias.StartsWith("cooked", StringComparison.Ordinal) && alias.Length > "cooked".Length)
            {
                string fish = alias.Substring("cooked".Length);
                yield return fish;
                if (!fish.StartsWith("cured", StringComparison.Ordinal))
                    yield return "cookedcured" + fish;
            }
            else if (alias.StartsWith("cured", StringComparison.Ordinal) && alias.Length > "cured".Length)
            {
                string fish = alias.Substring("cured".Length);
                yield return fish;
                if (!fish.StartsWith("cured", StringComparison.Ordinal))
                    yield return "cookedcured" + fish;
            }

            if (SpecialBlueprintAliasMap.TryGetValue(alias, out string[]? mapped))
            {
                foreach (string item in mapped)
                    yield return item;
            }
        }

        private static IEnumerable<string> ExpandDatabankAlias(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                yield break;

            string stripped = StripCommonSuffixes(alias);
            if (!string.Equals(alias, stripped, StringComparison.Ordinal))
                yield return stripped;

            if (SpecialDatabankAliasMap.TryGetValue(alias, out string[]? mapped))
            {
                foreach (string item in mapped)
                    yield return item;
            }
        }

        private static string StripCommonSuffixes(string alias)
        {
            string value = alias;
            value = StripSuffix(value, "blueprint");
            value = StripSuffix(value, "fragment");
            value = StripSuffix(value, "analysis");
            value = StripSuffix(value, "databox");
            value = StripSuffix(value, "unlocked");
            value = StripSuffix(value, "unlock");
            return value;
        }

        private static string StripSuffix(string value, string suffix)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal) && value.Length > suffix.Length)
                return value.Substring(0, value.Length - suffix.Length);

            return value;
        }

        private static void AddAliasCandidate(HashSet<string> aliases, string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return;

            aliases.Add(alias);
        }

        private static string BuildTokenAlias(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string spaced = HumanizeIdentifier(NormalizeCompatibilityCharacters(value));
            var rawTokens = TokenSplitRegex.Split(spaced);
            var kept = new List<string>(rawTokens.Length);

            foreach (string token in rawTokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                string lower = token.ToLowerInvariant();
                if (AliasStopWords.Contains(lower))
                    continue;

                kept.Add(lower);
            }

            while (kept.Count > 0 &&
                   (kept[0] == "ency" || kept[0] == "encyclopedia" || kept[0] == "databank" || kept[0] == "desc" || kept[0] == "entry"))
            {
                kept.RemoveAt(0);
            }

            if (kept.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(kept.Sum(t => t.Length));
            foreach (string token in kept)
                sb.Append(token);

            return sb.ToString();
        }

        private static bool TryExtractTechTypeId(string key, out int id)
        {
            id = 0;
            Match m = TrailingIdRegex.Match(key ?? string.Empty);
            if (!m.Success)
                return false;

            return int.TryParse(m.Groups[1].Value, out id);
        }

        private static void LogUnmatchedBlueprint(string eventKey)
        {
            string alias = NormalizeEventName(eventKey);
            if (alias.Length == 0 || !LoggedUnmatchedBlueprintAliases.Add(alias))
                return;

            Logger.Log($"[100Tracker] Unmatched blueprint unlock: key={eventKey}");
        }

        private static void LogUnmatchedDatabank(string eventKey)
        {
            string alias = NormalizeEventName(eventKey);
            if (alias.Length == 0 || !LoggedUnmatchedDatabankAliases.Add(alias))
                return;

            Logger.Log($"[100Tracker] Unmatched databank unlock: key={eventKey}");
        }

        private static void LogUnmatchedBiomePairing(string biomeDisplayName, string category, string entryName)
        {
            string key = $"{biomeDisplayName}|{category}|{entryName}";
            if (!LoggedUnmatchedBiomePairingEntries.Add(key))
                return;

            Logger.Log($"[100Tracker] Unmatched biome pairing {category}: biome={biomeDisplayName}, entry={entryName}");
        }

        private static void QueueUnlockToast(GameplayEventType type, string requirement, string fallbackEventKey)
        {
            if (!IsUnlockPopupEnabled())
                return;

            string displayName = ResolveRequirementDisplayName(type, requirement, fallbackEventKey);
            string prefix = type == GameplayEventType.BlueprintUnlocked ? "Blueprint" : "Databank";
            string message = $"{prefix} \"{displayName}\"";

            CancellationTokenSource currentCts;
            CancellationTokenSource? previousCts;
            lock (Sync)
            {
                previousCts = _toastDisplayCts;
                currentCts = new CancellationTokenSource();
                _toastDisplayCts = currentCts;
            }

            if (previousCts != null)
            {
                try
                {
                    previousCts.Cancel();
                }
                catch
                {
                    // best effort cancel
                }
                finally
                {
                    previousCts.Dispose();
                }
            }

            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await ShowToastSequenceAsync(message, currentCts.Token);
            });
        }

        private static string ResolveRequirementDisplayName(
            GameplayEventType type,
            string requirement,
            string fallbackEventKey)
        {
            if (type == GameplayEventType.BlueprintUnlocked &&
                BlueprintDisplayNames.TryGetValue(requirement, out string? blueprintName) &&
                !string.IsNullOrWhiteSpace(blueprintName))
            {
                return blueprintName;
            }

            if (type == GameplayEventType.DatabankEntryUnlocked &&
                DatabankDisplayNames.TryGetValue(requirement, out string? entryName) &&
                !string.IsNullOrWhiteSpace(entryName))
            {
                return entryName;
            }

            string fallback = BuildDisplayName(fallbackEventKey, NormalizeEventName(fallbackEventKey));
            return string.IsNullOrWhiteSpace(fallback)
                ? requirement
                : fallback;
        }

        private static async Task ShowToastSequenceAsync(string message, CancellationToken token)
        {
            // Keep the full toast lifecycle on the UI thread to avoid cross-thread WPF ownership faults.
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(() => ShowToastSequenceAsync(message, token)).Task.Unwrap();
                return;
            }

            try
            {
                await ToastSemaphore.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (token.IsCancellationRequested || !_runActive)
                    return;

                if (!IsUnlockPopupEnabled())
                {
                    _toastWindow?.Hide();
                    _toastVisible = false;
                    return;
                }

                if (ExplosionResetDisplayController.IsActive)
                    return;

                double targetLeft = 0;
                double targetTop = 0;
                bool hasAnchor = false;

                _toastWindow ??= new Subnautica100UnlockToastOverlay();
                if (_window != null)
                {
                    double overlayWidth = _window.ActualWidth > 1 ? _window.ActualWidth : _window.Width;
                    if (overlayWidth > 1)
                        _toastWindow.Width = overlayWidth;
                }

                if (_window != null && _window.IsVisible)
                {
                    _overlayLeft = _window.Left;
                    _overlayTop = _window.Top;

                    targetLeft = _overlayLeft;
                    targetTop = GetToastTop();
                    hasAnchor = true;
                }

                if (!hasAnchor)
                {
                    if (!TryGetFocusedSubnauticaWindowRect(out var rect))
                        return;

                    _overlayLeft = rect.Left + OverlayPadding;
                    _overlayTop = rect.Top + OverlayPadding;
                    targetLeft = _overlayLeft;
                    targetTop = _overlayTop + GetOverlayDimensions().Height + 8;
                }

                if (_toastVisible)
                    await AnimateToastOutQuickAsync();

                if (token.IsCancellationRequested || !_runActive)
                    return;

                double startLeft = targetLeft + 28;

                if (_toastWindow == null)
                    return;

                _toastWindow.SetMessage(message);
                _toastWindow.Left = startLeft;
                _toastWindow.Top = targetTop;
                _toastWindow.Opacity = 0;

                if (!_toastWindow.IsVisible)
                    _toastWindow.Show();

                _toastVisible = true;
                await AnimateToastAsync(
                    fromLeft: startLeft,
                    toLeft: targetLeft,
                    fromOpacity: 0,
                    toOpacity: 1,
                    durationMs: 300,
                    easing: new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    await AnimateToastOutQuickAsync();
                    return;
                }

                if (!IsUnlockPopupEnabled())
                {
                    _toastWindow?.Hide();
                    _toastVisible = false;
                    return;
                }

                await AnimateToastAsync(
                    fromLeft: targetLeft,
                    toLeft: targetLeft,
                    fromOpacity: 1,
                    toOpacity: 0,
                    durationMs: 1000,
                    easing: null);

                _toastWindow?.Hide();

                _toastVisible = false;
            }
            catch (Exception ex)
            {
                _toastVisible = false;
                _toastWindow?.Hide();
                Logger.Exception(ex, "[100Tracker] Unlock toast failed.");
            }
            finally
            {
                ToastSemaphore.Release();
            }
        }

        private static async Task AnimateToastOutQuickAsync()
        {
            if (!_toastVisible)
                return;

            if (_toastWindow == null || !_toastWindow.IsVisible)
            {
                _toastVisible = false;
                return;
            }

            double fromLeft = _toastWindow.Left;
            double fromOpacity = _toastWindow.Opacity;

            await AnimateToastAsync(
                fromLeft: fromLeft,
                toLeft: fromLeft + 24,
                fromOpacity: fromOpacity <= 0 ? 1 : fromOpacity,
                toOpacity: 0,
                durationMs: 140,
                easing: null);

            _toastWindow?.Hide();

            _toastVisible = false;
        }

        private static Task AnimateToastAsync(
            double fromLeft,
            double toLeft,
            double fromOpacity,
            double toOpacity,
            int durationMs,
            IEasingFunction? easing)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_toastWindow == null)
            {
                tcs.TrySetResult(true);
                return tcs.Task;
            }

            var storyboard = new Storyboard();

            var leftAnimation = new DoubleAnimation(fromLeft, toLeft, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = easing
            };
            Storyboard.SetTarget(leftAnimation, _toastWindow);
            Storyboard.SetTargetProperty(leftAnimation, new PropertyPath(Window.LeftProperty));

            var opacityAnimation = new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromMilliseconds(durationMs));
            Storyboard.SetTarget(opacityAnimation, _toastWindow);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

            storyboard.Children.Add(leftAnimation);
            storyboard.Children.Add(opacityAnimation);
            storyboard.Completed += (_, _) => tcs.TrySetResult(true);
            storyboard.Begin();

            return tcs.Task;
        }

        private static void ResetRunState()
        {
            CancellationTokenSource? toastCts = _toastDisplayCts;
            _toastDisplayCts = null;
            if (toastCts != null)
            {
                try
                {
                    toastCts.Cancel();
                }
                catch
                {
                    // best effort cancel
                }
                finally
                {
                    toastCts.Dispose();
                }
            }

            _runActive = false;
            _runStartedFromCreative = false;
            _toastVisible = false;
            _currentBiomeCanonical = string.Empty;
            _biomeScrollOffset = 0;
            _lastBiomeScrollUtc = DateTime.MinValue;
            _collectPreRunUnlocks = false;
            RunBlueprints.Clear();
            RunDatabankEntries.Clear();
            PendingPreRunBlueprints.Clear();
            PendingPreRunDatabankEntries.Clear();
            LoggedUnmatchedBlueprintAliases.Clear();
            LoggedUnmatchedDatabankAliases.Clear();

            Application.Current.Dispatcher.Invoke(() =>
            {
                _biomeWindow?.Hide();
                _toastWindow?.Hide();
            });
        }

        private static void StartRunState(bool creativeStart)
        {
            _runActive = true;
            _runStartedFromCreative = creativeStart;
            _biomeScrollOffset = 0;
            _lastBiomeScrollUtc = DateTime.MinValue;
            _collectPreRunUnlocks = false;

            // Creative starts can happen before a BiomeChanged event arrives; default to lifepod biome.
            if (string.IsNullOrWhiteSpace(_currentBiomeCanonical))
                _currentBiomeCanonical = "safe_shallows";

            RunBlueprints.Clear();
            RunDatabankEntries.Clear();

            foreach (string name in PreInstalledBlueprints)
                RunBlueprints.Add(name);

            foreach (string name in PreInstalledDatabankEntries)
                RunDatabankEntries.Add(name);
        }

        private static void SetCurrentBiome(string displayName)
        {
            string canonical = string.Empty;

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                if (!BiomeCanonicalByDisplay.TryGetValue(displayName.Trim(), out string? mappedCanonical) ||
                    string.IsNullOrWhiteSpace(mappedCanonical))
                    canonical = SubnauticaBiomeCatalog.Resolve(displayName).CanonicalKey;
                else
                    canonical = mappedCanonical;
            }

            if (string.IsNullOrWhiteSpace(canonical))
                return;

            if (string.Equals(_currentBiomeCanonical, canonical, StringComparison.Ordinal))
                return;

            _currentBiomeCanonical = canonical;
            _biomeScrollOffset = 0;
            _lastBiomeScrollUtc = DateTime.MinValue;
        }

        private static void UpdateOverlayText()
        {
            int blueprints = RunBlueprints.Count;
            int entries = RunDatabankEntries.Count;
            int total = blueprints + entries;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window?.SetProgress(
                    total,
                    RequiredCombinedTotal,
                    blueprints,
                    RequiredBlueprintTotal,
                    entries,
                    RequiredDatabankTotal);
            });
        }

        private static bool IsUnlockPopupEnabled() => LauncherSettings.Current.Subnautica100TrackerUnlockPopupEnabled;
        private static bool IsBiomeTrackerEnabled() => LauncherSettings.Current.SubnauticaBiomeTrackerEnabled;

        private static (double Width, double Height) GetOverlayDimensions()
        {
            return LauncherSettings.Current.Subnautica100TrackerSize switch
            {
                Subnautica100TrackerOverlaySize.Small => (160, 64),
                Subnautica100TrackerOverlaySize.Large => (260, 104),
                _ => (200, 80)
            };
        }

        private static (double Width, double Height) GetBiomeOverlayDimensions()
        {
            return LauncherSettings.Current.Subnautica100TrackerSize switch
            {
                Subnautica100TrackerOverlaySize.Small => (240, 64),
                Subnautica100TrackerOverlaySize.Large => (620, 104),
                _ => (420, 80)
            };
        }

        private static void ApplyOverlaySizePreset()
        {
            if (_window == null)
                return;

            (double width, double height) = GetOverlayDimensions();
            _window.Width = width;
            _window.Height = height;
            _window.ApplySizePreset(LauncherSettings.Current.Subnautica100TrackerSize);
        }

        private static void ApplyBiomeOverlaySizePreset()
        {
            if (_biomeWindow == null)
                return;

            (double width, double height) = GetBiomeOverlayDimensions();
            _biomeWindow.Width = width;
            _biomeWindow.Height = height;
            _biomeWindow.ApplySizePreset(LauncherSettings.Current.Subnautica100TrackerSize);
        }

        private static int GetBiomeScrollIntervalMs()
        {
            return LauncherSettings.Current.SubnauticaBiomeTrackerScrollSpeed switch
            {
                SubnauticaBiomeTrackerScrollSpeed.Slow => 7000,
                SubnauticaBiomeTrackerScrollSpeed.Medium => 4500,
                _ => 2800
            };
        }

        private static (int RowCount, int ColumnsPerRow) GetBiomeGridLayout()
        {
            return LauncherSettings.Current.Subnautica100TrackerSize switch
            {
                Subnautica100TrackerOverlaySize.Small => (1, 2),
                Subnautica100TrackerOverlaySize.Large => (2, 3),
                _ => (2, 2)
            };
        }

        private static List<BiomeCycleItem> BuildCurrentBiomeMissingItems()
        {
            var items = new List<BiomeCycleItem>();
            var seenDatabank = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenBlueprint = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!_runActive || string.IsNullOrWhiteSpace(_currentBiomeCanonical))
                return items;

            bool includeDatabanks = LauncherSettings.Current.SubnauticaBiomeTrackerCycleMode != SubnauticaBiomeTrackerCycleMode.Blueprints;
            bool includeBlueprints = LauncherSettings.Current.SubnauticaBiomeTrackerCycleMode != SubnauticaBiomeTrackerCycleMode.Databanks;

            if (includeDatabanks &&
                BiomeDatabankRequirementsByCanonical.TryGetValue(_currentBiomeCanonical, out List<string>? databankRequirements))
            {
                foreach (string requirement in databankRequirements)
                {
                    if (string.IsNullOrWhiteSpace(requirement))
                        continue;

                    if (!seenDatabank.Add(requirement))
                        continue;

                    if (!RunDatabankEntries.Contains(requirement))
                        items.Add(new BiomeCycleItem(false, requirement));
                }
            }

            if (includeBlueprints &&
                BiomeBlueprintRequirementsByCanonical.TryGetValue(_currentBiomeCanonical, out List<string>? blueprintRequirements))
            {
                foreach (string requirement in blueprintRequirements)
                {
                    if (string.IsNullOrWhiteSpace(requirement))
                        continue;

                    if (!seenBlueprint.Add(requirement))
                        continue;

                    if (!RunBlueprints.Contains(requirement))
                        items.Add(new BiomeCycleItem(true, requirement));
                }
            }

            return items;
        }

        private static BiomeDisplayFrame BuildBiomeDisplayRows()
        {
            (int rowCount, int columnsPerRow) = GetBiomeGridLayout();
            int visibleSlots = rowCount * columnsPerRow;
            List<BiomeCycleItem> missingItems = BuildCurrentBiomeMissingItems();

            if (missingItems.Count == 0)
            {
                _biomeScrollOffset = 0;
                _lastBiomeScrollUtc = DateTime.MinValue;

                string message = string.IsNullOrWhiteSpace(_currentBiomeCanonical)
                    ? "Waiting for biome data"
                    : "Biome Completed";

                // Show a single full-width completion card instead of per-row duplicates.
                var topFallback = new List<(string Type, string Name)> { (string.Empty, message) };
                return new BiomeDisplayFrame(topFallback, Array.Empty<(string Type, string Name)>(), 1, 1, 0);
            }

            // If all remaining items already fit in view, keep them static and do not duplicate/scroll.
            if (missingItems.Count <= visibleSlots)
            {
                _biomeScrollOffset = 0;
                _lastBiomeScrollUtc = DateTime.MinValue;

                List<(string Type, string Name)> BuildStaticRow(int rowOffset)
                {
                    var row = new List<(string Type, string Name)>(columnsPerRow);
                    int start = rowOffset * columnsPerRow;
                    for (int i = 0; i < columnsPerRow; i++)
                    {
                        int index = start + i;
                        if (index >= missingItems.Count)
                            break;

                        row.Add(FormatBiomeCycleItem(missingItems[index]));
                    }

                    return row;
                }

                List<(string Type, string Name)> staticTopRow = BuildStaticRow(0);
                IReadOnlyList<(string Type, string Name)> staticBottomRow = rowCount > 1
                    ? BuildStaticRow(1)
                    : Array.Empty<(string Type, string Name)>();

                return new BiomeDisplayFrame(staticTopRow, staticBottomRow, rowCount, columnsPerRow, 0);
            }

            int rowItemCount = Math.Min(columnsPerRow + 5, missingItems.Count);

            DateTime utcNow = DateTime.UtcNow;
            if (_lastBiomeScrollUtc == DateTime.MinValue)
                _lastBiomeScrollUtc = utcNow;

            double elapsedMs = Math.Max(0, (utcNow - _lastBiomeScrollUtc).TotalMilliseconds);
            _lastBiomeScrollUtc = utcNow;

            _biomeScrollOffset += elapsedMs / Math.Max(1, GetBiomeScrollIntervalMs());
            while (_biomeScrollOffset >= missingItems.Count)
                _biomeScrollOffset -= missingItems.Count;

            int startIndex = (int)Math.Floor(_biomeScrollOffset);
            double smoothProgress = _biomeScrollOffset - startIndex;

            List<(string Type, string Name)> BuildRow(int rowOffset)
            {
                var row = new List<(string Type, string Name)>(rowItemCount);
                int baseIndex = startIndex + (rowOffset * columnsPerRow);
                for (int i = 0; i < rowItemCount; i++)
                {
                    int index = PositiveModulo(baseIndex + i, missingItems.Count);
                    row.Add(FormatBiomeCycleItem(missingItems[index]));
                }

                return row;
            }

            List<(string Type, string Name)> topRow = BuildRow(0);
            IReadOnlyList<(string Type, string Name)> bottomRow = rowCount > 1
                ? BuildRow(1)
                : Array.Empty<(string Type, string Name)>();
            return new BiomeDisplayFrame(topRow, bottomRow, rowCount, columnsPerRow, smoothProgress);
        }

        private static int PositiveModulo(int value, int modulo)
        {
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static (string Type, string Name) FormatBiomeCycleItem(BiomeCycleItem item)
        {
            string type = item.IsBlueprint ? "Blueprint" : "Databank";
            string name = item.IsBlueprint
                ? ResolveRequirementDisplayName(GameplayEventType.BlueprintUnlocked, item.Requirement, item.Requirement)
                : ResolveRequirementDisplayName(GameplayEventType.DatabankEntryUnlocked, item.Requirement, item.Requirement);

            if (string.IsNullOrWhiteSpace(name))
                name = item.Requirement;
            if (string.IsNullOrWhiteSpace(name))
                name = "Unknown";

            return (type, name);
        }

        private static double GetToastTop()
        {
            double overlayHeight = _window?.ActualHeight ?? 0;
            if (overlayHeight <= 1)
                overlayHeight = _window?.Height ?? GetOverlayDimensions().Height;

            return _overlayTop + overlayHeight + 8;
        }

        private static async Task OverlayLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool runActive;
                    bool biomeTrackerEnabled;
                    BiomeDisplayFrame biomeFrame;
                    lock (Sync)
                    {
                        runActive = _runActive;
                        biomeTrackerEnabled = IsBiomeTrackerEnabled();
                        (int rowCount, int columnsPerRow) = GetBiomeGridLayout();
                        biomeFrame = biomeTrackerEnabled
                            ? BuildBiomeDisplayRows()
                            : new BiomeDisplayFrame(
                                Array.Empty<(string Type, string Name)>(),
                                Array.Empty<(string Type, string Name)>(),
                                rowCount,
                                columnsPerRow,
                                0);
                    }

                    RECT rect = default;
                    bool shouldShow = runActive
                        && !ExplosionResetDisplayController.IsActive
                        && TryGetFocusedSubnauticaWindowRect(out rect);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (shouldShow)
                        {
                            if (_window == null)
                            {
                                _window = new Subnautica100TrackerOverlay();
                                ApplyOverlaySizePreset();
                                _window.SetProgress(
                                    RunBlueprints.Count + RunDatabankEntries.Count,
                                    RequiredCombinedTotal,
                                    RunBlueprints.Count,
                                    RequiredBlueprintTotal,
                                    RunDatabankEntries.Count,
                                    RequiredDatabankTotal);
                            }
                            else
                            {
                                ApplyOverlaySizePreset();
                            }

                            _overlayLeft = rect.Left + OverlayPadding;
                            _overlayTop = rect.Top + OverlayPadding;

                            _window.Left = _overlayLeft;
                            _window.Top = _overlayTop;

                            if (!_window.IsVisible)
                                _window.Show();

                            if (biomeTrackerEnabled)
                            {
                                _biomeWindow ??= new SubnauticaBiomeTrackerOverlay();
                                ApplyBiomeOverlaySizePreset();

                                double trackerWidth = _window.ActualWidth > 1 ? _window.ActualWidth : _window.Width;
                                _biomeWindow.Left = _overlayLeft + trackerWidth + 8;
                                _biomeWindow.Top = _overlayTop;
                                _biomeWindow.SetEntries(
                                    biomeFrame.TopRow,
                                    biomeFrame.BottomRow,
                                    biomeFrame.RowCount,
                                    biomeFrame.ColumnsPerRow,
                                    biomeFrame.ScrollProgress);

                                if (!_biomeWindow.IsVisible)
                                    _biomeWindow.Show();
                            }
                            else
                            {
                                _biomeWindow?.Hide();
                            }

                            if (IsUnlockPopupEnabled() && _toastWindow != null && _toastWindow.IsVisible)
                            {
                                double overlayWidth = _window.ActualWidth > 1 ? _window.ActualWidth : _window.Width;
                                if (overlayWidth > 1)
                                    _toastWindow.Width = overlayWidth;

                                _toastWindow.Left = _overlayLeft;
                                _toastWindow.Top = GetToastTop();
                            }
                            else if (!IsUnlockPopupEnabled())
                            {
                                _toastWindow?.Hide();
                                _toastVisible = false;
                            }
                        }
                        else
                        {
                            _window?.Hide();
                            _biomeWindow?.Hide();
                            _toastWindow?.Hide();
                            _toastVisible = false;
                        }
                    });
                }
                catch
                {
                    _toastVisible = false;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _window?.Hide();
                        _biomeWindow?.Hide();
                        _toastWindow?.Hide();
                    });
                }

                try
                {
                    await Task.Delay(16, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static bool TryGetFocusedSubnauticaWindowRect(out RECT rect)
        {
            rect = default;

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || IsIconic(foreground))
                return false;

            _ = GetWindowThreadProcessId(foreground, out uint processId);
            if (processId == 0)
                return false;

            Process? process = null;
            try
            {
                process = Process.GetProcessById((int)processId);

                if (process.HasExited)
                    return false;

                if (!process.ProcessName.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                    return false;

                return GetWindowRect(foreground, out rect);
            }
            catch
            {
                return false;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private static string NormalizeChecklistName(string name)
        {
            return NormalizeNameCore(name, removeTrailingNumericParentheses: true);
        }

        private static string BuildDisplayName(string rawName, string normalizedFallback)
        {
            string stripped = TrailingCountRegex.Replace(rawName ?? string.Empty, string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(stripped))
                return stripped;

            return HumanizeIdentifier(normalizedFallback);
        }

        private static string NormalizeEventName(string key)
        {
            return NormalizeNameCore(key, removeTrailingNumericParentheses: true);
        }

        private static string NormalizeNameCore(string value, bool removeTrailingNumericParentheses)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = NormalizeCompatibilityCharacters(value).Trim();
            if (removeTrailingNumericParentheses)
            {
                while (true)
                {
                    string next = TrailingCountRegex.Replace(normalized, string.Empty).Trim();
                    if (next == normalized)
                        break;

                    normalized = next;
                }
            }

            var sb = new StringBuilder(normalized.Length);
            foreach (char c in normalized)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static string NormalizeCompatibilityCharacters(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(MapCompatibilityDigit(c));

            return sb.ToString();
        }

        private static char MapCompatibilityDigit(char c)
        {
            return c switch
            {
                '₀' or '⁰' => '0',
                '₁' or '¹' => '1',
                '₂' or '²' => '2',
                '₃' or '³' => '3',
                '₄' or '⁴' => '4',
                '₅' or '⁵' => '5',
                '₆' or '⁶' => '6',
                '₇' or '⁷' => '7',
                '₈' or '⁸' => '8',
                '₉' or '⁹' => '9',
                _ => c
            };
        }

        private static string HumanizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            char previous = '\0';

            foreach (char current in value)
            {
                if (sb.Length > 0 && ShouldInsertSpace(previous, current))
                    sb.Append(' ');

                sb.Append(current);
                previous = current;
            }

            return sb.ToString();
        }

        private static bool ShouldInsertSpace(char previous, char current)
        {
            if (previous == '\0' || previous == ' ')
                return false;

            if (char.IsDigit(previous) && char.IsLetter(current))
                return true;

            if (char.IsLetter(previous) && char.IsDigit(current))
                return true;

            return char.IsLower(previous) && char.IsUpper(current);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private enum ParseMode
        {
            Blueprint,
            Databank
        }

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
