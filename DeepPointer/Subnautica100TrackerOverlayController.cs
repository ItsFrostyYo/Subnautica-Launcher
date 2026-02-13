using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Macros;
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

namespace SubnauticaLauncher.Gameplay
{
    public static class Subnautica100TrackerOverlayController
    {
        private const int OverlayPadding = 12;
        private const int RequiredBlueprintTotal = 157;
        private const int RequiredDatabankTotal = 277;
        private const int RequiredCombinedTotal = RequiredBlueprintTotal + RequiredDatabankTotal;
        private const int MaxAliasLength = 96;

        private static readonly object Sync = new();
        private static readonly Regex ChecklistLineRegex = new(@"^(TRUE|FALSE)\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingCountRegex = new(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);
        private static readonly Regex TrailingIdRegex = new(@"\s*\((\d+)\)\s*$", RegexOptions.Compiled);

        private static readonly HashSet<string> RequiredBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> RequiredDatabankEntries = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PreInstalledBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PreInstalledDatabankEntries = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, HashSet<string>> BlueprintAliasIndex = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> DatabankAliasIndex = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, HashSet<string>> BlueprintRequirementsByTechType = new();

        private static readonly HashSet<string> RunBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> RunDatabankEntries = new(StringComparer.Ordinal);

        private static readonly HashSet<string> LoggedUnmatchedBlueprintAliases = new(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedUnmatchedDatabankAliases = new(StringComparer.Ordinal);

        // Common internal-name to checklist-name mismatches and shorthand aliases.
        private static readonly Dictionary<string, string[]> SpecialBlueprintAliasMap = new(StringComparer.Ordinal)
        {
            ["airsack"] = new[] { "bladderfish", "cookedcuredbladderfish" },
            ["bladderfishanalysis"] = new[] { "bladderfish", "cookedcuredbladderfish" },
            ["lavaboomerang"] = new[] { "magmarang", "cookedcuredmagmarang" },
            ["lavaeyeye"] = new[] { "redeyeye", "cookedcuredredeyeye" },
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
            ["lavaboomerang"] = new[] { "magmarang" }
        };

        private static readonly IReadOnlyDictionary<int, string> TechTypeDatabase = TechTypeNames.GetAll();

        private static Subnautica100TrackerOverlay? _window;
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static bool _runActive;
        private static bool _rulesLoaded;
        private static GameState _lastStableState = GameState.Unknown;

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

            lock (Sync)
            {
                cts = _cts;
                loopTask = _loopTask;
                _cts = null;
                _loopTask = null;
                _runActive = false;
                _lastStableState = GameState.Unknown;
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

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
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

                AddRequirement(mode, normalizedName, preInstalled);
            }

            AddDerivedRequirementAliases();
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

        private static void AddRequirement(ParseMode mode, string normalizedName, bool preInstalled)
        {
            if (mode == ParseMode.Blueprint)
            {
                RequiredBlueprints.Add(normalizedName);
                if (preInstalled)
                    PreInstalledBlueprints.Add(normalizedName);

                AddAlias(BlueprintAliasIndex, normalizedName, normalizedName);
            }
            else
            {
                RequiredDatabankEntries.Add(normalizedName);
                if (preInstalled)
                    PreInstalledDatabankEntries.Add(normalizedName);

                AddAlias(DatabankAliasIndex, normalizedName, normalizedName);
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
                        _lastStableState = GameState.MainMenu;
                        UpdateOverlayText();
                        return;
                    }

                    if (state == "ingame")
                    {
                        if (!_runActive && _lastStableState == GameState.MainMenu)
                        {
                            // Fallback arming: if autosplitter RunStarted misses on a build,
                            // still start tracker on a stable MainMenu -> InGame transition.
                            StartRunState();
                            Logger.Log("[100Tracker] Run started from state transition (MainMenu -> InGame).");
                            UpdateOverlayText();
                        }

                        _lastStableState = GameState.InGame;
                        return;
                    }

                    _lastStableState = GameState.Unknown;
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

                if (!_runActive)
                    return;

                if (evt.Type == GameplayEventType.BlueprintUnlocked)
                {
                    int added = 0;
                    var matches = ResolveBlueprintMatches(evt.Key);
                    foreach (string requirement in matches)
                    {
                        if (RunBlueprints.Add(requirement))
                            added++;
                    }

                    if (added > 0)
                    {
                        UpdateOverlayText();
                    }
                    else
                    {
                        LogUnmatchedBlueprint(evt.Key);
                    }

                    return;
                }

                if (evt.Type == GameplayEventType.DatabankEntryUnlocked)
                {
                    int added = 0;
                    var matches = ResolveDatabankMatches(evt.Key);
                    foreach (string requirement in matches)
                    {
                        if (RunDatabankEntries.Add(requirement))
                            added++;
                    }

                    if (added > 0)
                    {
                        UpdateOverlayText();
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

            if (TryExtractTechTypeId(eventKey, out int id) &&
                TechTypeDatabase.TryGetValue(id, out var enumName) &&
                !string.IsNullOrWhiteSpace(enumName))
            {
                AddAliasCandidate(aliases, NormalizeChecklistName(enumName));
                AddAliasCandidate(aliases, NormalizeChecklistName(HumanizeIdentifier(enumName)));
            }

            ExpandAliasesInPlace(aliases, ExpandBlueprintAlias);
            return aliases;
        }

        private static IEnumerable<string> EnumerateDatabankAliases(string eventKey)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            AddAliasCandidate(aliases, NormalizeEventName(eventKey));

            if (TryExtractTechTypeId(eventKey, out int id) &&
                TechTypeDatabase.TryGetValue(id, out var enumName) &&
                !string.IsNullOrWhiteSpace(enumName))
            {
                AddAliasCandidate(aliases, NormalizeChecklistName(enumName));
                AddAliasCandidate(aliases, NormalizeChecklistName(HumanizeIdentifier(enumName)));
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

        private static void ResetRunState()
        {
            _runActive = false;
            RunBlueprints.Clear();
            RunDatabankEntries.Clear();
            LoggedUnmatchedBlueprintAliases.Clear();
            LoggedUnmatchedDatabankAliases.Clear();
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
                        && TryGetFocusedSubnauticaWindowRect(out rect);

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

                            _window.Left = rect.Left + OverlayPadding;
                            _window.Top = rect.Top + OverlayPadding;

                            if (!_window.IsVisible)
                                _window.Show();
                        }
                        else
                        {
                            _window?.Hide();
                        }
                    });
                }
                catch
                {
                    Application.Current.Dispatcher.Invoke(() => _window?.Hide());
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

        private static bool TryGetFocusedSubnauticaWindowRect(out RECT rect)
        {
            rect = default;

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;

            var processes = Process.GetProcessesByName("Subnautica");
            try
            {
                var proc = processes.FirstOrDefault(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
                if (proc == null)
                    return false;

                return proc.MainWindowHandle == foreground && GetWindowRect(proc.MainWindowHandle, out rect);
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
