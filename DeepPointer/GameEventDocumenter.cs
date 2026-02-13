using SubnauticaLauncher.Display;
using SubnauticaLauncher.Macros;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Gameplay
{
    public static class GameEventDocumenter
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, DynamicMonoGameplayEventTracker> Trackers = new();
        private static readonly Dictionary<string, ProcessStateTracker> StateTrackers = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        public static event Action<GameplayEvent>? EventWritten;

        public static void Start()
        {
            lock (Sync)
            {
                if (_loopTask != null)
                    return;

                EnsureOutputFileExists();
                WriteLifecycleEvent("DocumenterStarted");

                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
                Logger.Log("Game event documenter started.");
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;
            Task? loop;

            lock (Sync)
            {
                cts = _cts;
                loop = _loopTask;
                _cts = null;
                _loopTask = null;
            }

            if (cts == null)
                return;

            try
            {
                cts.Cancel();
                loop?.Wait(2000);
            }
            catch
            {
                // ignore shutdown race
            }
            finally
            {
                cts.Dispose();
                lock (Sync)
                {
                    Trackers.Clear();
                    StateTrackers.Clear();
                }

                WriteLifecycleEvent("DocumenterStopped");
                Logger.Log("Game event documenter stopped.");
            }
        }

        private static async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    PollGame("Subnautica", token);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "Game event documenter loop error");
                }

                try
                {
                    await Task.Delay(250, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static void PollGame(string processName, CancellationToken token)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            var alive = new HashSet<int>(processes.Select(p => p.Id));
            string prefix = processName + ":";

            lock (Sync)
            {
                var stale = Trackers.Keys
                    .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                    .Where(key =>
                    {
                        if (!int.TryParse(key.Substring(prefix.Length), out int pid))
                            return true;
                        return !alive.Contains(pid);
                    })
                    .ToList();

                foreach (string key in stale)
                {
                    Trackers.Remove(key);
                    StateTrackers.Remove(key);
                }
            }

            foreach (var process in processes)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    if (process.HasExited)
                        continue;

                    string trackerKey = processName + ":" + process.Id;
                    DynamicMonoGameplayEventTracker tracker = GetOrCreateTracker(processName, trackerKey);

                    TryEmitStateEvents(processName, process, trackerKey, tracker);

                    if (!tracker.TryPoll(process, out var events) || events.Count == 0)
                        continue;

                    foreach (var evt in events)
                    {
                        var outputEvent = WriteEvent(evt);
                        Logger.Log($"[GameEvent] {outputEvent.Game} {outputEvent.Type} key={outputEvent.Key} delta={outputEvent.Delta}");
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process exited mid-poll; safe to ignore.
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, $"Game event polling failed for {processName}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static DynamicMonoGameplayEventTracker GetOrCreateTracker(string processName, string trackerKey)
        {
            lock (Sync)
            {
                if (Trackers.TryGetValue(trackerKey, out var tracker))
                    return tracker;

                tracker = new DynamicMonoGameplayEventTracker(processName);
                Trackers[trackerKey] = tracker;
                return tracker;
            }
        }

        private static void TryEmitStateEvents(
            string processName,
            Process process,
            string trackerKey,
            DynamicMonoGameplayEventTracker tracker)
        {
            try
            {
                GameState state;
                string source;

                if (tracker.TryDetectState(process, out state))
                {
                    source = "dynamic-state";
                }
                else
                {
                    int yearGroup;
                    if (processName.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                    {
                        string? exe = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(exe))
                            return;

                        string root = Path.GetDirectoryName(exe)!;
                        yearGroup = BuildYearResolver.ResolveGroupedYear(root);
                    }
                    else
                    {
                        yearGroup = BuildYearResolver.ResolveBelowZero();
                    }

                    var profile = GameStateDetectorRegistry.Get(yearGroup);
                    var display = DisplayInfo.GetPrimary();
                    state = GameStateDetector.Detect(processName, profile, display, focusGame: false);
                    source = "pixel-state";
                }

                // Ignore Unknown noise so the event log stays useful.
                if (state == GameState.Unknown)
                    return;

                bool changed;
                bool shouldEmitRunStart;
                GameState previous;
                GameState current;

                lock (Sync)
                {
                    if (!StateTrackers.TryGetValue(trackerKey, out var trackerState))
                    {
                        trackerState = new ProcessStateTracker();
                        StateTrackers[trackerKey] = trackerState;
                    }

                    changed = trackerState.TryPromote(state, out previous, out current);
                    shouldEmitRunStart = trackerState.ShouldEmitRunStart(previous, current);
                }

                if (!changed)
                {
                    return;
                }

                WriteEvent(new GameplayEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    Game = processName,
                    ProcessId = process.Id,
                    Type = GameplayEventType.GameStateChanged,
                    Key = current.ToString(),
                    Delta = 0,
                    Source = source
                });

                if (shouldEmitRunStart)
                {
                    WriteEvent(new GameplayEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Game = processName,
                        ProcessId = process.Id,
                        Type = GameplayEventType.RunStarted,
                        Key = $"{previous}->InGame",
                        Delta = 1,
                        Source = "state-transition"
                    });
                }
            }
            catch
            {
                // state detection is best-effort, never break event tracking
            }
        }

        private sealed class ProcessStateTracker
        {
            private const int StableSamples = 3;
            private const int StableSamplesInGameToBlackScreen = 8;
            private static readonly TimeSpan RunStartCooldown = TimeSpan.FromSeconds(10);

            private bool _hasCandidate;
            private GameState _candidate;
            private int _candidateCount;

            private bool _hasStable;
            private GameState _stable;

            private bool _runArmed;
            private bool _sawBlackScreenAfterMainMenu;
            private DateTime _lastRunStartedUtc = DateTime.MinValue;

            public bool TryPromote(GameState rawState, out GameState previousStable, out GameState stableState)
            {
                previousStable = GameState.Unknown;
                stableState = GameState.Unknown;

                if (!_hasCandidate || _candidate != rawState)
                {
                    _candidate = rawState;
                    _candidateCount = 1;
                    _hasCandidate = true;
                    return false;
                }

                _candidateCount++;
                int requiredSamples = StableSamples;
                if (_hasStable && _stable == GameState.InGame && rawState == GameState.BlackScreen)
                    requiredSamples = StableSamplesInGameToBlackScreen;

                if (_candidateCount < requiredSamples)
                    return false;

                if (!_hasStable)
                {
                    _stable = rawState;
                    _hasStable = true;
                    if (_stable == GameState.MainMenu)
                    {
                        _runArmed = true;
                        _sawBlackScreenAfterMainMenu = false;
                    }
                    else if (_stable == GameState.BlackScreen && _runArmed)
                    {
                        _sawBlackScreenAfterMainMenu = true;
                    }

                    previousStable = _stable;
                    stableState = _stable;
                    return true;
                }

                if (_stable == rawState)
                    return false;

                previousStable = _stable;
                _stable = rawState;
                stableState = _stable;

                if (_stable == GameState.MainMenu)
                {
                    _runArmed = true;
                    _sawBlackScreenAfterMainMenu = false;
                }
                else if (_stable == GameState.BlackScreen && _runArmed)
                {
                    _sawBlackScreenAfterMainMenu = true;
                }

                return true;
            }

            public bool ShouldEmitRunStart(GameState previousState, GameState currentState)
            {
                if (currentState != GameState.InGame)
                    return false;

                // Require a loading/cutscene phase before in-game, matching autosplitter behavior.
                if (previousState != GameState.BlackScreen)
                    return false;

                if (!_runArmed)
                    return false;

                if (!_sawBlackScreenAfterMainMenu)
                    return false;

                DateTime now = DateTime.UtcNow;
                if ((now - _lastRunStartedUtc) < RunStartCooldown)
                    return false;

                _runArmed = false;
                _lastRunStartedUtc = now;
                return true;
            }
        }

        private static void WriteLifecycleEvent(string key)
        {
            WriteEvent(new GameplayEvent
            {
                TimestampUtc = DateTime.UtcNow,
                Game = "Launcher",
                ProcessId = Environment.ProcessId,
                Type = GameplayEventType.GameStateChanged,
                Key = key,
                Delta = 0,
                Source = "documenter"
            });
        }

        private static void EnsureOutputFileExists()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "gameplay_events.jsonl");
                if (!File.Exists(file))
                    File.WriteAllText(file, string.Empty);
            }
            catch
            {
                // ignore file initialization errors
            }
        }

        private static GameplayEvent WriteEvent(GameplayEvent evt)
        {
            GameplayEvent output = GameplayEventFormatter.FormatForOutput(evt);

            try
            {
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "gameplay_events.jsonl");
                string json = JsonSerializer.Serialize(output, JsonOptions);
                File.AppendAllText(file, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to write gameplay event log");
            }

            try
            {
                EventWritten?.Invoke(output);
            }
            catch
            {
                // UI listeners are best-effort only.
            }

            return output;
        }
    }
}
