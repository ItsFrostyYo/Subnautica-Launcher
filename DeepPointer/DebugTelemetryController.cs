using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application = System.Windows.Application;

namespace SubnauticaLauncher.Gameplay
{
    public static class DebugTelemetryController
    {
        private static readonly object Sync = new();
        private static DebugTelemetryWindow? _window;
        private static CancellationTokenSource? _cts;
        private static Task? _pollTask;
        private static string _subnauticaState = "Unknown";
        private static string _belowZeroState = "Unknown";

        private static int _resolverPid = -1;
        private static IExplosionResolver? _resolver;

        public static void Start()
        {
            lock (Sync)
            {
                if (_window != null)
                    return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _window = new DebugTelemetryWindow();
                    _window.Show();
                    _window.SetState(BuildStateText());
                });

                GameEventDocumenter.EventWritten += OnEventWritten;
                _cts = new CancellationTokenSource();
                _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
            }
        }

        public static void Stop()
        {
            CancellationTokenSource? cts;
            Task? pollTask;

            lock (Sync)
            {
                cts = _cts;
                pollTask = _pollTask;
                _cts = null;
                _pollTask = null;
            }

            GameEventDocumenter.EventWritten -= OnEventWritten;

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    pollTask?.Wait(1500);
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

        private static async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    PollSnapshot();
                }
                catch
                {
                    // polling must be resilient
                }

                try
                {
                    await Task.Delay(125, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static void PollSnapshot()
        {
            Process[] snProcesses = Process.GetProcessesByName("Subnautica");
            Process[] bzProcesses = Process.GetProcessesByName("SubnauticaZero");

            Process? sn = snProcesses.FirstOrDefault(p => !p.HasExited);
            Process? bz = bzProcesses.FirstOrDefault(p => !p.HasExited);

            string processText = sn == null && bz == null
                ? "none"
                : $"Subnautica={(sn?.Id.ToString() ?? "not running")} | Below Zero={(bz?.Id.ToString() ?? "not running")}";

            double? explosionSeconds = null;
            float? x = null;
            float? y = null;
            float? z = null;

            if (sn != null)
            {
                if (_resolverPid != sn.Id || _resolver == null)
                {
                    string? exePath = sn.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        string root = Path.GetDirectoryName(exePath)!;
                        int yearGroup = BuildYearResolver.ResolveGroupedYear(root);
                        _resolver = ExplosionResolverFactory.Get(yearGroup);
                        _resolverPid = sn.Id;
                    }
                }

                if (_resolver != null && _resolver.TryRead(sn, out var snap))
                {
                    explosionSeconds = snap.ExplosionTime >= 0 ? snap.ExplosionTime : null;
                    x = snap.PosX;
                    y = snap.PosY;
                    z = snap.PosZ;
                }
            }
            else
            {
                _resolverPid = -1;
                _resolver = null;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_window == null)
                    return;

                _window.SetProcessText(processText);
                _window.SetExplosionTime(explosionSeconds);
                _window.SetPosition(x, y, z);
                _window.SetState(BuildStateText());
            });

            foreach (var p in snProcesses)
                p.Dispose();

            foreach (var p in bzProcesses)
                p.Dispose();
        }

        private static void OnEventWritten(GameplayEvent evt)
        {
            if (evt.Type == GameplayEventType.GameStateChanged)
            {
                if (evt.Game.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                    _subnauticaState = evt.Key;
                else if (evt.Game.Equals("SubnauticaZero", StringComparison.OrdinalIgnoreCase))
                    _belowZeroState = evt.Key;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window?.SetState(BuildStateText());
                _window?.AppendEvent(evt);
            });
        }

        private static string BuildStateText()
        {
            return $"Subnautica={_subnauticaState} | Below Zero={_belowZeroState}";
        }
    }
}
