using SubnauticaLauncher.Core;
using SubnauticaLauncher.Display;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Macros;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Gameplay
{
    public static class GameEventDocumenter
    {
        private const int ForegroundPollIntervalMs = 1;
        private const int BackgroundPollIntervalMs = 4;
        private const int IdlePollIntervalMs = 100;

        private static readonly object Sync = new();
        private static readonly Dictionary<string, DynamicMonoGameplayEventTracker> Trackers = new();
        private static readonly Dictionary<string, ProcessStateTracker> StateTrackers = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly ConcurrentQueue<GameplayEvent> _eventQueue = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static Task? _drainTask;
        public static event Action<GameplayEvent>? EventWritten;
        public static event Action<IReadOnlyList<GameplayEvent>>? BatchEventWritten;

        public static void Start()
        {
            lock (Sync)
            {
                if (_loopTask != null)
                    return;

                EnsureOutputFileExists();
                SubnauticaBiomeCatalog.EnsureCatalogWritten();
                SubnauticaUnlockPairingCatalog.EnsureCatalogWritten();
                WriteLifecycleEvent("DocumenterStarted");

                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
                _drainTask = Task.Run(() => DrainQueueAsync(_cts.Token));
                Logger.Log("Game event documenter started.");
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;
            Task? loop;
            Task? drain;

            lock (Sync)
            {
                cts = _cts;
                loop = _loopTask;
                drain = _drainTask;
                _cts = null;
                _loopTask = null;
                _drainTask = null;
            }

            if (cts == null)
                return;

            try
            {
                cts.Cancel();
                var tasks = new List<Task>(2);
                if (loop != null) tasks.Add(loop);
                if (drain != null) tasks.Add(drain);
                if (tasks.Count > 0)
                    Task.WaitAll(tasks.ToArray(), 500);
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

                DrainRemainingEvents();
                WriteLifecycleEvent("DocumenterStopped");
                Logger.Log("Game event documenter stopped.");
            }
        }

        private static async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                PollMode pollMode = PollMode.Idle;

                try
                {
                    pollMode = PollGame("Subnautica", token);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "Game event documenter loop error");
                }

                try
                {
                    int delay = pollMode switch
                    {
                        PollMode.Foreground => ForegroundPollIntervalMs,
                        PollMode.Background => BackgroundPollIntervalMs,
                        _ => IdlePollIntervalMs
                    };

                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task DrainQueueAsync(CancellationToken token)
        {
            var batch = new List<GameplayEvent>(64);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    batch.Clear();
                    while (_eventQueue.TryDequeue(out var evt))
                        batch.Add(evt);

                    if (batch.Count > 0)
                        WriteBatchAndNotify(batch);

                    await Task.Delay(5, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "Event drain loop error");
                }
            }
        }

        private static void WriteBatchAndNotify(List<GameplayEvent> batch)
        {
            var formatted = new List<GameplayEvent>(batch.Count);
            var jsonLines = new StringBuilder(batch.Count * 200);
            var logLines = new StringBuilder(batch.Count * 120);

            foreach (var evt in batch)
            {
                var output = GameplayEventFormatter.FormatForOutput(evt);
                formatted.Add(output);

                try
                {
                    string json = JsonSerializer.Serialize(output, JsonOptions);
                    jsonLines.AppendLine(json);
                }
                catch { }

                logLines.AppendLine($"[GameEvent] {output.Game} {output.Type} key={output.Key} delta={output.Delta}");
            }

            try
            {
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "gameplay_events.jsonl");
                File.AppendAllText(file, jsonLines.ToString());
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to write gameplay event batch");
            }

            Logger.Log(logLines.ToString().TrimEnd());

            var snapshot = formatted.ToArray();

            if (BatchEventWritten != null)
            {
                _ = Task.Run(() =>
                {
                    try { BatchEventWritten?.Invoke(snapshot); } catch { }
                });
            }

            foreach (var output in snapshot)
            {
                if (output.Type == GameplayEventType.BlueprintUnlocked ||
                    output.Type == GameplayEventType.DatabankEntryUnlocked ||
                    output.Type == GameplayEventType.ItemPickedUp ||
                    output.Type == GameplayEventType.ItemDropped ||
                    output.Type == GameplayEventType.ItemCrafted)
                    continue;

                try
                {
                    EventWritten?.Invoke(output);
                }
                catch { }
            }
        }

        private static void DrainRemainingEvents()
        {
            var batch = new List<GameplayEvent>();
            while (_eventQueue.TryDequeue(out var evt))
                batch.Add(evt);

            if (batch.Count > 0)
            {
                try
                {
                    WriteBatchAndNotify(batch);
                }
                catch { }
            }
        }

        private static PollMode PollGame(string processName, CancellationToken token)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return PollMode.Idle;

            var alive = new HashSet<int>(processes.Select(p => p.Id));
            bool isForeground = IsAnyForegroundProcess(alive);
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

            var trackerPairs = new List<(Process proc, string key, DynamicMonoGameplayEventTracker tracker)>();

            foreach (var process in processes)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    if (process.HasExited)
                    {
                        process.Dispose();
                        continue;
                    }

                    string trackerKey = processName + ":" + process.Id;
                    DynamicMonoGameplayEventTracker tracker = GetOrCreateTracker(processName, trackerKey);

                    TryEmitStateEvents(processName, process, trackerKey, tracker);

                    trackerPairs.Add((process, trackerKey, tracker));
                }
                catch (InvalidOperationException)
                {
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, $"Game event polling failed for {processName}");
                    process.Dispose();
                }
            }

            foreach (var (proc, key, tracker) in trackerPairs)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    if (proc.HasExited)
                        continue;

                    if (!tracker.TryPoll(proc, out var events) || events.Count == 0)
                        continue;

                    foreach (var evt in events)
                        _eventQueue.Enqueue(evt);
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
                    proc.Dispose();
                }
            }

            return isForeground ? PollMode.Foreground : PollMode.Background;
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
                if (tracker.TryDetectRunStart(process, out string runStartReason))
                {
                    WriteEvent(new GameplayEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Game = processName,
                        ProcessId = process.Id,
                        Type = GameplayEventType.RunStarted,
                        Key = string.IsNullOrWhiteSpace(runStartReason) ? "AutosplitterStart" : runStartReason,
                        Delta = 1,
                        Source = "autosplitter-start"
                    });
                }

                // Lowest-latency path: end event is consumed by timer stop logic immediately.
                if (tracker.TryDetectRocketLaunch(process, out bool runEndedTriggered) && runEndedTriggered)
                {
                    WriteEvent(new GameplayEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Game = processName,
                        ProcessId = process.Id,
                        Type = GameplayEventType.RunEnded,
                        Key = "RocketLaunch",
                        Delta = 0,
                        Source = "autosplitter-rocket-launch"
                    });
                }

                GameState state;
                string source;

                if (tracker.TryDetectState(process, out state))
                {
                    source = "dynamic-state";
                }
                else
                {
                    // For Subnautica event tracking we rely only on dynamic-mono state,
                    // to match autosplitter behavior and avoid pixel false positives.
                    return;
                }

                // Ignore Unknown noise so the event log stays useful.
                if (state == GameState.Unknown)
                    return;

                bool changed;
                GameState previous;
                GameState current;
                bool hasBiome = tracker.TryDetectBiome(process, out string biomeRaw);
                bool hasDepth = tracker.TryDetectPlayerDepth(process, out float playerY);
                bool biomeChanged = false;
                string currentBiome = string.Empty;
                SubnauticaBiomeCatalog.BiomeMatch biomeMatch = default;

                if (hasBiome)
                {
                    biomeMatch = SubnauticaBiomeCatalog.Resolve(biomeRaw, hasDepth ? playerY : null);
                    SubnauticaBiomeCatalog.RegisterObserved(biomeRaw, biomeMatch);
                }

                lock (Sync)
                {
                    if (!StateTrackers.TryGetValue(trackerKey, out var trackerState))
                    {
                        trackerState = new ProcessStateTracker();
                        StateTrackers[trackerKey] = trackerState;
                    }

                    changed = trackerState.TryPromote(state, out previous, out current);

                    if (hasBiome)
                        biomeChanged = trackerState.TryPromoteBiome(
                            biomeMatch.CanonicalKey,
                            biomeMatch.IsKnown,
                            out _,
                            out currentBiome);
                }

                if (changed)
                {
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
                }

                if (biomeChanged)
                {
                    WriteEvent(new GameplayEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Game = processName,
                        ProcessId = process.Id,
                        Type = GameplayEventType.BiomeChanged,
                        Key = SubnauticaBiomeCatalog.GetDisplayName(currentBiome),
                        Delta = 0,
                        Source = "dynamic-biome"
                    });
                }

            }
            catch
            {
                // state detection is best-effort, never break event tracking
            }
        }

        private static bool IsAnyForegroundProcess(HashSet<int> processIds)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            _ = GetWindowThreadProcessId(hwnd, out uint foregroundPid);
            if (foregroundPid == 0)
                return false;

            return processIds.Contains((int)foregroundPid);
        }

        private sealed class ProcessStateTracker
        {
            private const int StableSamples = 3;
            private const int StableBiomeSamples = 2;

            private bool _hasCandidate;
            private GameState _candidate;
            private int _candidateCount;

            private bool _hasStable;
            private GameState _stable;
            private bool _hasBiomeCandidate;
            private string _biomeCandidate = string.Empty;
            private int _biomeCandidateCount;
            private bool _hasBiomeStable;
            private string _biomeStable = string.Empty;

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
                if (_candidateCount < StableSamples)
                    return false;

                if (!_hasStable)
                {
                    _stable = rawState;
                    _hasStable = true;

                    previousStable = _stable;
                    stableState = _stable;
                    return true;
                }

                if (_stable == rawState)
                    return false;

                previousStable = _stable;
                _stable = rawState;
                stableState = _stable;

                return true;
            }

            public bool TryPromoteBiome(string rawBiome, bool isKnown, out string previousBiome, out string stableBiome)
            {
                previousBiome = string.Empty;
                stableBiome = string.Empty;

                if (string.IsNullOrWhiteSpace(rawBiome))
                    return false;

                // Unqualified/unknown biome aliases (ex: "WreckInterior") should not replace a known stable biome.
                if (!isKnown && _hasBiomeStable)
                    return false;

                if (!_hasBiomeCandidate || !string.Equals(_biomeCandidate, rawBiome, StringComparison.Ordinal))
                {
                    _biomeCandidate = rawBiome;
                    _biomeCandidateCount = 1;
                    _hasBiomeCandidate = true;
                    return false;
                }

                _biomeCandidateCount++;
                if (_biomeCandidateCount < StableBiomeSamples)
                    return false;

                if (!_hasBiomeStable)
                {
                    _biomeStable = rawBiome;
                    _hasBiomeStable = true;
                    previousBiome = _biomeStable;
                    stableBiome = _biomeStable;
                    return true;
                }

                if (string.Equals(_biomeStable, rawBiome, StringComparison.Ordinal))
                    return false;

                previousBiome = _biomeStable;
                _biomeStable = rawBiome;
                stableBiome = _biomeStable;
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
            bool isTimingCritical =
                output.Type == GameplayEventType.RunStarted ||
                output.Type == GameplayEventType.RunEnded;

            void NotifyListeners()
            {
                try
                {
                    BatchEventWritten?.Invoke(new[] { output });
                }
                catch { }

                try
                {
                    EventWritten?.Invoke(output);
                }
                catch
                {
                    // UI listeners are best-effort only.
                }
            }

            // Run start/end power the timer and should not wait on disk I/O.
            if (isTimingCritical)
                NotifyListeners();

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

            if (output.Type == GameplayEventType.RunEnded)
                Logger.Log($"[GameEvent] {output.Game} RunEnded key={output.Key} (run ending)");

            if (!isTimingCritical)
                NotifyListeners();

            return output;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private enum PollMode
        {
            Idle,
            Background,
            Foreground
        }
    }
}
