using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetDisplayController
    {
        public static bool Enabled => ExplosionResetSettings.OverlayEnabled;

        private static ExplosionResetDisplay? _window;
        private static int _resetCount;
        private static CancellationTokenSource? _timeCts;

        public static int ResetCount => _resetCount;

        public static void Start(Process proc, IExplosionResolver resolver)
        {
            if (!Enabled || _window != null)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _resetCount = 0;
                _window = new ExplosionResetDisplay();
                _window.Show();
                _window.SetStep("Macro Idle");
                _window.SetResetCount(0);
            });

            // ✅ CLOSE OVERLAY WHEN LAUNCHER EXITS
            Application.Current.Exit -= OnAppExit;
            Application.Current.Exit += OnAppExit;

            StartExplosionTimeUpdater(proc, resolver);
        }

        private static void OnAppExit(object? sender, ExitEventArgs e)
        {
            Stop("Launcher Closed");
        }

        private static void StartExplosionTimeUpdater(Process proc, IExplosionResolver resolver)
        {
            _timeCts?.Cancel();
            _timeCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_timeCts.IsCancellationRequested)
                {
                    if (resolver.TryRead(proc, out var snap))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            _window?.SetExplosionTime(snap.ExplosionTime);
                        });
                    }

                    await Task.Delay(100);
                }
            });
        }

        public static void SetStep(string text)
        {
            if (!Enabled || _window == null)
                return;

            Application.Current.Dispatcher.Invoke(() =>
                _window.SetStep(text));
        }

        public static void IncrementResetCount()
        {
            _resetCount++;
            Application.Current.Dispatcher.Invoke(() =>
                _window?.SetResetCount(_resetCount));
        }

        public static void Stop(string finalStep, int closeDelayMs = 0)
        {
            if (_window == null)
                return;

            _timeCts?.Cancel();
            _timeCts = null;

            Application.Current.Dispatcher.Invoke(async () =>
            {
                _window.SetStep(finalStep);

                if (closeDelayMs > 0)
                    await Task.Delay(closeDelayMs);

                _window.Close();
                _window = null;
            });
        }

        // 🔥 HARD KILL (used on launcher exit)
        public static void ForceClose()
        {
            _timeCts?.Cancel();
            _timeCts = null;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
            });
        }
    }
}