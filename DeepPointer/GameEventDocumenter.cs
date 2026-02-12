using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Gameplay
{
    public static class GameEventDocumenter
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, DynamicMonoGameplayEventTracker> Trackers = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;

        public static void Start()
        {
            lock (Sync)
            {
                if (_loopTask != null)
                    return;

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
                }

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
                    Trackers.Remove(key);
            }

            foreach (var process in processes)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    DynamicMonoGameplayEventTracker tracker;
                    string trackerKey = processName + ":" + process.Id;
                    lock (Sync)
                    {
                        if (!Trackers.TryGetValue(trackerKey, out tracker!))
                        {
                            tracker = new DynamicMonoGameplayEventTracker(processName);
                            Trackers[trackerKey] = tracker;
                        }
                    }

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
