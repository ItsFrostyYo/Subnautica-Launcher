using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private const double FallbackOverlayHeight = 88;
        private const int RequiredBlueprintTotal = 157;
        private const int RequiredDatabankTotal = 277;
        private const int RequiredCombinedTotal = RequiredBlueprintTotal + RequiredDatabankTotal;
        private const int MaxAliasLength = 96;

        private static readonly object Sync = new();
        private static readonly Regex ChecklistLineRegex = new(@"^(TRUE|FALSE)\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CookedCuredChecklistRegex = new(@"^Cooked\s*\+\s*Cured\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
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

        private static readonly HashSet<string> LoggedUnmatchedBlueprintAliases = new(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedUnmatchedDatabankAliases = new(StringComparer.Ordinal);

        // Common internal-name to checklist-name mismatches and shorthand aliases.
        private static readonly Dictionary<string, string[]> SpecialBlueprintAliasMap = new(StringComparer.Ordinal)
        {
            ["airsack"] = new[] { "bladderfish", "cookedbladderfish", "curedbladderfish" },
            ["bladderfishanalysis"] = new[] { "bladderfish", "cookedbladderfish", "curedbladderfish" },
            ["lavaboomerang"] = new[] { "magmarang", "cookedmagmarang", "curedmagmarang" },
            ["lavaeyeye"] = new[] { "redeyeye", "cookedredeyeye", "curedredeyeye" },
            ["exosuit"] = new[] { "prawnsuitmkiii" },
            ["prawnsuit"] = new[] { "prawnsuitmkiii" },
            ["tank"] = new[] { "standardo2tank" },
            ["welder"] = new[] { "repairtool" },
            ["knife"] = new[] { "survivalknife" },
            ["builder"] = new[] { "habitatbuilder" },
            ["pipesurfacefloater"] = new[] { "floatingairpump" },
            ["constructor"] = new[] { "mobilevehiclebay" },
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
            ["metalsavlage"] = new[] { "scatteredwreckage" }
        };

        private static readonly IReadOnlyDictionary<int, string> TechTypeDatabase = TechTypeNames.GetAll();

        private static Subnautica100TrackerOverlay? _window;
        private static Subnautica100UnlockToastOverlay? _toastWindow;
        private static CancellationTokenSource? _cts;
        private static CancellationTokenSource? _toastDisplayCts;
        private static Task? _loopTask;
        private static bool _runActive;
        private static bool _rulesLoaded;
        private static bool _toastVisible;
        private static double _overlayLeft;
        private static double _overlayTop;
        private static readonly SemaphoreSlim ToastSemaphore = new(1, 1);

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
                RunBlueprints.Clear();
                RunDatabankEntries.Clear();
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
                _toastWindow?.Close();
                _toastWindow = null;
            });
        }

        private static void EnsureRulesLoaded()
        {
            if (_rulesLoaded)
                return;

            string text = ReadEmbeddedChecklistText();
            if (string.IsNullOrWhiteSpace(text))
            {
                Logger.Warn("[100Tracker] Embedded checklist database is missing. Tracker will stay idle.");
                return;
            }

            ParseChecklistText(text);
            BuildBlueprintTechTypeRequirementIndex();
            _rulesLoaded = true;

            Logger.Log(
                $"[100Tracker] Database loaded. " +
                $"techTypes={TechTypeDatabase.Count}, " +
                $"requiredBlueprints={RequiredBlueprints.Count} (pre={PreInstalledBlueprints.Count}), " +
                $"requiredDatabank={RequiredDatabankEntries.Count} (pre={PreInstalledDatabankEntries.Count}), " +
                $"techTypeBlueprintMappings={BlueprintRequirementsByTechType.Count}, " +
                $"targetTotal={RequiredCombinedTotal}");
        }

        private static string ReadEmbeddedChecklistText()
        {
            try
            {
                var asm = typeof(Subnautica100TrackerOverlayController).Assembly;
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Subnautica100Checklist.txt", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    return string.Empty;

                using Stream? stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null)
                    return string.Empty;

                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ParseChecklistText(string text)
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

            ParseMode mode = ParseMode.None;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("!ALL BLUEPRINTS!", StringComparison.OrdinalIgnoreCase))
                {
                    mode = ParseMode.Blueprint;
                    continue;
                }

                if (line.StartsWith("!ALL DATABANK ENTRIES!", StringComparison.OrdinalIgnoreCase))
                {
                    mode = ParseMode.Databank;
                    continue;
                }

                if (mode == ParseMode.None)
                    continue;

                Match match = ChecklistLineRegex.Match(line);
                bool preInstalled;
                string candidateName;
                if (match.Success)
                {
                    preInstalled = match.Groups[1].Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    candidateName = match.Groups[2].Value;
                }
                else
                {
                    if (LooksLikeSectionHeader(line))
                        continue;

                    preInstalled = false;
                    candidateName = line;
                }

                string normalizedName = NormalizeChecklistName(candidateName);
                if (normalizedName.Length == 0)
                    continue;

                if (mode == ParseMode.Blueprint && TryExpandCookedCuredRequirement(candidateName, preInstalled))
                    continue;

                AddRequirement(mode, normalizedName, candidateName, preInstalled);
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
                        return;
                    }
                    return;
                }

                if (evt.Type == GameplayEventType.RunStarted)
                {
                    if (!_runActive)
                    {
                        StartRunState();
                        Logger.Log("[100Tracker] Run started from RunStarted event.");
                        UpdateOverlayText();
                    }

                    return;
                }

                if (!_runActive &&
                    (evt.Type == GameplayEventType.ItemPickedUp ||
                     evt.Type == GameplayEventType.ItemDropped ||
                     evt.Type == GameplayEventType.ItemCrafted))
                {
                    StartRunState();
                    Logger.Log($"[100Tracker] Run fallback started from {evt.Type} event.");
                    UpdateOverlayText();
                }

                if (evt.Type == GameplayEventType.BlueprintUnlocked)
                {
                    var matches = ResolveBlueprintMatches(evt.Key);

                    if (!_runActive && matches.Any(requirement => !PreInstalledBlueprints.Contains(requirement)))
                    {
                        StartRunState();
                        Logger.Log("[100Tracker] Run fallback started from non-default blueprint unlock.");
                        UpdateOverlayText();
                    }

                    if (!_runActive)
                        return;

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

                    if (!_runActive && matches.Any(requirement => !PreInstalledDatabankEntries.Contains(requirement)))
                    {
                        StartRunState();
                        Logger.Log("[100Tracker] Run fallback started from non-default databank unlock.");
                        UpdateOverlayText();
                    }

                    if (!_runActive)
                        return;

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

            string spaced = HumanizeIdentifier(value);
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

        private static void QueueUnlockToast(GameplayEventType type, string requirement, string fallbackEventKey)
        {
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

                if (ExplosionResetDisplayController.IsActive)
                    return;

                double targetLeft = 0;
                double targetTop = 0;
                bool hasAnchor = false;

                _toastWindow ??= new Subnautica100UnlockToastOverlay();

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
                    if (!TryGetSubnauticaWindowRect(out var rect))
                        return;

                    _overlayLeft = rect.Left + OverlayPadding;
                    _overlayTop = rect.Top + OverlayPadding;
                    targetLeft = _overlayLeft;
                    targetTop = _overlayTop + FallbackOverlayHeight + 8;
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

            double fromLeft = 0;
            double fromOpacity = 0;
            bool canAnimate = false;

            if (_toastWindow == null || !_toastWindow.IsVisible)
            {
                _toastVisible = false;
                return;
            }

            fromLeft = _toastWindow.Left;
            fromOpacity = _toastWindow.Opacity;
            canAnimate = true;

            if (!canAnimate)
            {
                _toastVisible = false;
                return;
            }

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
            _toastVisible = false;
            RunBlueprints.Clear();
            RunDatabankEntries.Clear();
            LoggedUnmatchedBlueprintAliases.Clear();
            LoggedUnmatchedDatabankAliases.Clear();

            Application.Current.Dispatcher.Invoke(() =>
            {
                _toastWindow?.Hide();
            });
        }

        private static void StartRunState()
        {
            _runActive = true;

            RunBlueprints.Clear();
            RunDatabankEntries.Clear();

            foreach (string name in PreInstalledBlueprints)
                RunBlueprints.Add(name);

            foreach (string name in PreInstalledDatabankEntries)
                RunDatabankEntries.Add(name);
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

        private static double GetToastTop()
        {
            double overlayHeight = _window?.ActualHeight ?? 0;
            if (overlayHeight <= 1)
                overlayHeight = _window?.Height ?? 88;

            return _overlayTop + overlayHeight + 8;
        }

        private static async Task OverlayLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool runActive;
                    lock (Sync)
                        runActive = _runActive;

                    RECT rect = default;
                    bool shouldShow = runActive
                        && !ExplosionResetDisplayController.IsActive
                        && TryGetSubnauticaWindowRect(out rect);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (shouldShow)
                        {
                            if (_window == null)
                            {
                                _window = new Subnautica100TrackerOverlay();
                                _window.SetProgress(
                                    RunBlueprints.Count + RunDatabankEntries.Count,
                                    RequiredCombinedTotal,
                                    RunBlueprints.Count,
                                    RequiredBlueprintTotal,
                                    RunDatabankEntries.Count,
                                    RequiredDatabankTotal);
                            }

                            _overlayLeft = rect.Left + OverlayPadding;
                            _overlayTop = rect.Top + OverlayPadding;

                            _window.Left = _overlayLeft;
                            _window.Top = _overlayTop;

                            if (!_window.IsVisible)
                                _window.Show();

                            if (_toastWindow != null && _toastWindow.IsVisible)
                            {
                                _toastWindow.Left = _overlayLeft;
                                _toastWindow.Top = GetToastTop();
                            }
                        }
                        else
                        {
                            _window?.Hide();
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
                        _toastWindow?.Hide();
                    });
                }

                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static bool TryGetSubnauticaWindowRect(out RECT rect)
        {
            rect = default;

            var processes = Process.GetProcessesByName("Subnautica");
            try
            {
                if (processes.Length == 0)
                    return false;

                IntPtr foreground = GetForegroundWindow();
                Process? proc = processes.FirstOrDefault(p =>
                    !p.HasExited &&
                    p.MainWindowHandle != IntPtr.Zero &&
                    p.MainWindowHandle == foreground);

                proc ??= processes.FirstOrDefault(p =>
                    !p.HasExited &&
                    p.MainWindowHandle != IntPtr.Zero);

                if (proc == null)
                    return false;

                if (IsIconic(proc.MainWindowHandle))
                    return false;

                return GetWindowRect(proc.MainWindowHandle, out rect);
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
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

        private static bool LooksLikeSectionHeader(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            if (line.StartsWith("!", StringComparison.Ordinal))
                return true;

            if (Regex.IsMatch(line, @"^[A-Z0-9\s&+\-':]+(?:\(\d+\))?\s*$"))
                return true;

            return false;
        }

        private static string NormalizeEventName(string key)
        {
            return NormalizeNameCore(key, removeTrailingNumericParentheses: true);
        }

        private static string NormalizeNameCore(string value, bool removeTrailingNumericParentheses)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim();
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
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private enum ParseMode
        {
            None,
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
