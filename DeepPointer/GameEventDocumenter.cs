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
        private static readonly Dictionary<string, GameState> LastStates = new();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;

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
                    LastStates.Clear();
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
                    PollGame("SubnauticaZero", token);
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
                    LastStates.Remove(key);
                }
            }

            foreach (var process in processes)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    string trackerKey = processName + ":" + process.Id;
                    DynamicMonoGameplayEventTracker tracker = GetOrCreateTracker(processName, trackerKey);

                    TryEmitStateEvents(processName, process, trackerKey);

                    if (!tracker.TryPoll(process, out var events) || events.Count == 0)
                        continue;

                    foreach (var evt in events)
                    {
                        WriteEvent(evt);
                        Logger.Log($"[GameEvent] {evt.Game} {evt.Type} key={evt.Key} delta={evt.Delta}");
                    }
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

        private static void TryEmitStateEvents(string processName, Process process, string trackerKey)
        {
            try
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
                var state = GameStateDetector.Detect(processName, profile, display, focusGame: false);

                // Ignore Unknown noise so the event log stays useful.
                if (state == GameState.Unknown)
                    return;

                bool hadPrevious;
                GameState previous;

                lock (Sync)
                {
                    hadPrevious = LastStates.TryGetValue(trackerKey, out previous);
                    LastStates[trackerKey] = state;
                }

                if (!hadPrevious)
                {
                    WriteEvent(new GameplayEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Game = processName,
                        ProcessId = process.Id,
                        Type = GameplayEventType.GameStateChanged,
                        Key = state.ToString(),
                        Delta = 0,
                        Source = "pixel-state"
                    });
                    return;
                }

                if (previous != state)
                {
                    WriteEvent(new GameplayEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Game = processName,
                        ProcessId = process.Id,
                        Type = GameplayEventType.GameStateChanged,
                        Key = state.ToString(),
                        Delta = 0,
                        Source = "pixel-state"
                    });

                    if (state == GameState.InGame &&
                        (previous == GameState.MainMenu || previous == GameState.BlackScreen))
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
            }
            catch
            {
                // state detection is best-effort, never break event tracking
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

        private static void WriteEvent(GameplayEvent evt)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataPath);
                string file = Path.Combine(AppPaths.DataPath, "gameplay_events.jsonl");
                string json = JsonSerializer.Serialize(evt, JsonOptions);
                File.AppendAllText(file, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to write gameplay event log");
            }
        }
    }
}
