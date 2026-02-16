using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionResetDisplayController
    {
        public static bool Enabled => ExplosionResetSettings.OverlayEnabled;
        public static bool IsActive => _window != null;

        private static ExplosionResetDisplay? _window;
        private static int _resetCount;
        private static CancellationTokenSource? _overlayCts;

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

            StartOverlayUpdater(proc, resolver);
        }

        private static void OnAppExit(object? sender, ExitEventArgs e)
        {
            Stop("Launcher Closed");
        }

        private static void StartOverlayUpdater(Process proc, IExplosionResolver resolver)
        {
            _overlayCts?.Cancel();
            _overlayCts = new CancellationTokenSource();
            CancellationToken token = _overlayCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        proc.Refresh();

                        if (proc.HasExited)
                        {
                            Application.Current.Dispatcher.Invoke(() => _window?.Hide());
                        }
                        else
                        {
                            if (resolver.TryRead(proc, out var snap))
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    _window?.SetExplosionTime(snap.ExplosionTime);
                                });
                            }

                            UpdateOverlayWindowPosition(proc);
                        }
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
            });
        }

        private static void UpdateOverlayWindowPosition(Process proc)
        {
            var window = _window;
            if (window == null)
                return;

            IntPtr gameHwnd = proc.MainWindowHandle;
            if (gameHwnd == IntPtr.Zero || !GetWindowRect(gameHwnd, out RECT rect))
            {
                Application.Current.Dispatcher.Invoke(() => window.Hide());
                return;
            }

            bool gameFocused = GetForegroundWindow() == gameHwnd;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_window == null)
                    return;

                if (!gameFocused)
                {
                    _window.Hide();
                    return;
                }

                _window.Left = rect.Left + 12;
                _window.Top = rect.Top + 12;

                if (!_window.IsVisible)
                    _window.Show();
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

            _overlayCts?.Cancel();
            _overlayCts = null;

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
            _overlayCts?.Cancel();
            _overlayCts = null;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
            });
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
