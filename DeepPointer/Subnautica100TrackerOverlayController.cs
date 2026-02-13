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

namespace SubnauticaLauncher.Gameplay
{
    public static class Subnautica100TrackerOverlayController
    {
        private const int OverlayPadding = 12;
        private const int RequiredBlueprintTotal = 157;
        private const int RequiredDatabankTotal = 277;
        private const int RequiredCombinedTotal = RequiredBlueprintTotal + RequiredDatabankTotal;

        private static readonly object Sync = new();
        private static readonly Regex ChecklistLineRegex = new(@"^(TRUE|FALSE)\s+(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingCountRegex = new(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);

        private static readonly HashSet<string> RequiredBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> RequiredDatabankEntries = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PreInstalledBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PreInstalledDatabankEntries = new(StringComparer.Ordinal);

        private static readonly HashSet<string> RunBlueprints = new(StringComparer.Ordinal);
        private static readonly HashSet<string> RunDatabankEntries = new(StringComparer.Ordinal);

        private static Subnautica100TrackerOverlay? _window;
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static bool _runActive;
        private static bool _rulesLoaded;

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

            _rulesLoaded = true;

            string? checklistFile = FindChecklistFile();
            if (checklistFile == null)
            {
                Logger.Warn("[100Tracker] Checklist file not found. Overlay will show 0 progress.");
                return;
            }

            ParseChecklistFile(checklistFile);

            Logger.Log(
                $"[100Tracker] Loaded checklist rules. " +
                $"blueprints={RequiredBlueprints.Count} (pre={PreInstalledBlueprints.Count}), " +
                $"databank={RequiredDatabankEntries.Count} (pre={PreInstalledDatabankEntries.Count}), " +
                $"expectedTotal={RequiredCombinedTotal}");
        }

        private static string? FindChecklistFile()
        {
            string[] fileNames = { "Checklist.txt", "CheckList.txt" };
            var roots = new List<string>();

            if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
                roots.Add(AppContext.BaseDirectory);

            if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory) &&
                !roots.Contains(Environment.CurrentDirectory, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(Environment.CurrentDirectory);
            }

            foreach (string root in roots)
            {
                string? current = root;
                for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
                {
                    foreach (string fileName in fileNames)
                    {
                        string candidate = Path.Combine(current, fileName);
                        if (File.Exists(candidate))
                            return candidate;
                    }

                    current = Directory.GetParent(current)?.FullName;
                }
            }

            return null;
        }

        private static void ParseChecklistFile(string path)
        {
            RequiredBlueprints.Clear();
            RequiredDatabankEntries.Clear();
            PreInstalledBlueprints.Clear();
            PreInstalledDatabankEntries.Clear();

            ParseMode mode = ParseMode.None;
            foreach (string rawLine in File.ReadLines(path))
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

                    // Some checklist lines are plain item names without TRUE/FALSE.
                    // Treat them as required and not pre-installed.
                    preInstalled = false;
                    candidateName = line;
                }

                string name = NormalizeChecklistName(candidateName);
                if (name.Length == 0)
                    continue;

                if (mode == ParseMode.Blueprint)
                {
                    RequiredBlueprints.Add(name);
                    if (preInstalled)
                        PreInstalledBlueprints.Add(name);
                }
                else
                {
                    RequiredDatabankEntries.Add(name);
                    if (preInstalled)
                        PreInstalledDatabankEntries.Add(name);
                }
            }
        }

        private static void OnEventWritten(GameplayEvent evt)
        {
            if (!evt.Game.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                return;

            lock (Sync)
            {
                if (evt.Type == GameplayEventType.GameStateChanged &&
                    evt.Key.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
                {
                    ResetRunState();
                    UpdateOverlayText();
                    return;
                }

                if (evt.Type == GameplayEventType.RunStarted)
                {
                    StartRunState();
                    UpdateOverlayText();
                    return;
                }

                if (!_runActive)
                    return;

                if (evt.Type == GameplayEventType.BlueprintUnlocked)
                {
                    string name = NormalizeEventName(evt.Key);
                    if (RequiredBlueprints.Contains(name))
                    {
                        RunBlueprints.Add(name);
                        UpdateOverlayText();
                    }

                    return;
                }

                if (evt.Type == GameplayEventType.DatabankEntryUnlocked)
                {
                    string name = NormalizeEventName(evt.Key);
                    if (RequiredDatabankEntries.Contains(name))
                    {
                        RunDatabankEntries.Add(name);
                        UpdateOverlayText();
                    }
                }
            }
        }

        private static void ResetRunState()
        {
            _runActive = false;
            RunBlueprints.Clear();
            RunDatabankEntries.Clear();
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

            // Typical section/category header format in the checklist.
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
