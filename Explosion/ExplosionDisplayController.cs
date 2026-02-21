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
        public static bool IsActive => _overlayCts != null && Enabled;
        private const double ParkedWindowLeft = -32000;
        private const double ParkedWindowTop = -32000;

        private static ExplosionResetDisplay? _window;
        private static int _resetCount;
        private static CancellationTokenSource? _overlayCts;
        private static string _currentStep = "Macro Idle";
        private static double _currentExplosionSeconds;
        private static double _overlayLeft;
        private static double _overlayTop;

        public static int ResetCount => _resetCount;

        public static void Start(Process proc, IExplosionResolver resolver)
        {
            if (!Enabled || _window != null)
                return;

            _resetCount = 0;
            _currentStep = "Macro Idle";
            _currentExplosionSeconds = 0;
            _overlayLeft = 0;
            _overlayTop = 0;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _window = new ExplosionResetDisplay();
                _window.Show();
                _window.SetStep(_currentStep);
                _window.SetResetCount(_resetCount);
                _window.SetExplosionTime(_currentExplosionSeconds);
            });

            // Close overlay when launcher exits.
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
                            Application.Current.Dispatcher.Invoke(ParkOverlayForCapture);
                        }
                        else
                        {
                            if (resolver.TryRead(proc, out var snap))
                            {
                                _currentExplosionSeconds = snap.ExplosionTime;
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
                        Application.Current.Dispatcher.Invoke(ParkOverlayForCapture);
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
                Application.Current.Dispatcher.Invoke(ParkOverlayForCapture);

                return;
            }

            _overlayLeft = rect.Left + 12;
            _overlayTop = rect.Top + 12;

            bool gameFocused = GetForegroundWindow() == gameHwnd;
            bool keepForCaptureFriendlyForeground = IsCaptureFriendlyForeground();
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_window == null)
                    return;

                if (!gameFocused && !keepForCaptureFriendlyForeground)
                {
                    ParkOverlayForCapture();
                    return;
                }

                _window.Left = _overlayLeft;
                _window.Top = _overlayTop;
                _window.Topmost = gameFocused;
                _window.Opacity = 1;

                if (!_window.IsVisible)
                    _window.Show();
            });
        }

        public static void SetStep(string text)
        {
            if (!Enabled)
                return;

            _currentStep = string.IsNullOrWhiteSpace(text) ? "Macro Idle" : text;

            if (_window == null)
                return;

            Application.Current.Dispatcher.Invoke(() => _window.SetStep(_currentStep));
        }

        public static void IncrementResetCount()
        {
            _resetCount++;

            Application.Current.Dispatcher.Invoke(() => _window?.SetResetCount(_resetCount));
        }

        public static void Stop(string finalStep, int closeDelayMs = 0)
        {
            if (_window == null)
                return;

            _overlayCts?.Cancel();
            _overlayCts = null;

            if (!string.IsNullOrWhiteSpace(finalStep))
                _currentStep = finalStep;

            Application.Current.Dispatcher.Invoke(async () =>
            {
                if (_window == null)
                    return;

                _window.SetStep(_currentStep);

                if (closeDelayMs > 0)
                    await Task.Delay(closeDelayMs);

                _window.Close();
                _window = null;
            });
        }

        // Hard kill (used on launcher exit)
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

        private static string BuildExplosionTimeText(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"Explosion Time: {(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        private static void ParkOverlayForCapture()
        {
            if (_window == null)
                return;

            if (!_window.IsVisible)
                _window.Show();

            _window.Topmost = false;
            _window.Opacity = 1;
            _window.Left = ParkedWindowLeft;
            _window.Top = ParkedWindowTop;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private static bool IsCaptureFriendlyForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;

            _ = GetWindowThreadProcessId(foreground, out uint processId);
            if (processId == 0)
                return false;

            if (processId == (uint)Environment.ProcessId)
                return true;

            try
            {
                using Process process = Process.GetProcessById((int)processId);
                string name = process.ProcessName;
                return name.Equals("obs64", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obs32", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("obs", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("SubnauticaLauncher", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
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
