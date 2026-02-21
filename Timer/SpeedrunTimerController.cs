using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Settings;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Application = System.Windows.Application;
using SubnauticaLauncher.UI;

namespace SubnauticaLauncher.Timer
{
    public static class SpeedrunTimerController
    {
        private static readonly object Sync = new();
        private const double ParkedWindowLeft = -32000;
        private const double ParkedWindowTop = -32000;
        private static SpeedrunTimerOverlay? _window;
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;
        private static readonly Stopwatch _stopwatch = new();
        private static bool _timerRunning;

        public static void Start()
        {
            lock (Sync)
            {
                if (_loopTask != null)
                    return;

                _timerRunning = false;
                _stopwatch.Reset();
                GameEventDocumenter.EventWritten += OnEventWritten;

                EnsureOverlayWindowInitialized();
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => DisplayLoopAsync(_cts.Token));
            }
        }

        public static void WarmupCaptureWindow()
        {
            lock (Sync)
            {
                EnsureOverlayWindowInitialized();
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
                _timerRunning = false;
                _stopwatch.Reset();
            }

            GameEventDocumenter.EventWritten -= OnEventWritten;

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    loopTask?.Wait(500);
                }
                catch { }
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

        private static void OnEventWritten(GameplayEvent evt)
        {
            if (!evt.Game.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                return;

            if (evt.Type == GameplayEventType.GameStateChanged)
            {
                string key = evt.Key?.ToLowerInvariant() ?? string.Empty;
                if (key == "mainmenu")
                {
                    lock (Sync)
                    {
                        _stopwatch.Reset();
                        _timerRunning = false;
                    }
                }
                return;
            }

            if (evt.Type == GameplayEventType.RunStarted)
            {
                SpeedrunGamemode configuredGamemode = LauncherSettings.Current.SpeedrunGamemode;
                bool isSurvivalStart = IsSurvivalStart(evt.Key);
                bool isCreativeStart = IsCreativeStart(evt.Key);

                bool shouldStart =
                    (configuredGamemode == SpeedrunGamemode.SurvivalHardcore && isSurvivalStart) ||
                    (configuredGamemode == SpeedrunGamemode.Creative && isCreativeStart);

                if (!shouldStart)
                    return;

                lock (Sync)
                {
                    if (!_timerRunning)
                    {
                        _stopwatch.Restart();
                        _timerRunning = true;
                    }
                }

                if (Application.Current != null)
                {
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try { _window?.SetTime(TimeSpan.Zero); } catch { }
                    }, System.Windows.Threading.DispatcherPriority.Send);
                }

                return;
            }

            if (evt.Type == GameplayEventType.RunEnded)
            {
                TimeSpan? finalTime = null;

                lock (Sync)
                {
                    if (_timerRunning || _stopwatch.IsRunning)
                    {
                        // Pause immediately on RunEnded so the overlay freezes at final time.
                        _stopwatch.Stop();
                        _timerRunning = false;
                        finalTime = _stopwatch.Elapsed;
                    }
                }

                if (finalTime.HasValue)
                {
                    if (Application.Current != null)
                    {
                        _ = Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try { _window?.SetTime(finalTime.Value); } catch { }
                        }, System.Windows.Threading.DispatcherPriority.Send);
                    }

                    Logger.Log($"Run ended (Rocket Launch). Final time: {finalTime.Value:hh\\:mm\\:ss\\.fff}");
                }
            }
        }

        private static bool IsSurvivalStart(string? key)
        {
            string normalized = StripToAlphanumericLower(key);
            return normalized is "lifepodradiodamaged" or "cutsceneskipped"
                or "introcinematicended" or "oxygensept2018" or "legacyintrocinematicended";
        }

        private static bool IsCreativeStart(string? key)
        {
            string normalized = StripToAlphanumericLower(key);
            return normalized.StartsWith("creative", StringComparison.Ordinal);
        }

        private static string StripToAlphanumericLower(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static string FormatRightAligned(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}";
        }

        private static string FormatLeftAligned(TimeSpan t)
        {
            return t.TotalHours >= 1
                ? $"{t.Milliseconds:D3}.{t.Seconds:D2}:{t.Minutes:D2}:{(int)t.TotalHours}"
                : $"{t.Milliseconds:D3}.{t.Seconds:D2}:{t.Minutes:D2}";
        }

        private static async Task DisplayLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TimeSpan elapsed;

                    lock (Sync)
                    {
                        elapsed = _stopwatch.Elapsed;
                    }

                    bool hasRect = TryGetOverlayTargetRect(out RECT rect, out bool gameFocused);

                    LauncherSettings settings = LauncherSettings.Current;
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (hasRect)
                            {
                                if (_window == null)
                                {
                                    _window = new SpeedrunTimerOverlay();
                                    _window.ApplyStyle(settings);
                                    _window.SetTime(elapsed);
                                }
                                else
                                {
                                    _window.SetTime(elapsed);
                                }

                                _window.Topmost = gameFocused;
                                _window.Opacity = 1;
                                PositionOverlay(rect);

                                if (!_window.IsVisible)
                                    _window.Show();
                            }
                            else
                            {
                                ParkOverlayForCapture();
                            }
                        }
                        catch { }
                    }, System.Windows.Threading.DispatcherPriority.Send);
                }
                catch { }

                try
                {
                    await Task.Delay(16, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public static void ReapplyStyle()
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _window?.ApplyStyle(LauncherSettings.Current);
            });
        }

        private static void EnsureOverlayWindowInitialized()
        {
            if (Application.Current == null)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_window != null)
                    return;

                _window = new SpeedrunTimerOverlay();
                _window.ApplyStyle(LauncherSettings.Current);
                _window.SetTime(TimeSpan.Zero);
                ParkOverlayForCapture();
            });
        }

        private const double EdgeMargin = 10; // Timer can sit within 10px of game window edges

        private static void PositionOverlay(RECT rect)
        {
            if (_window == null) return;

            double windowWidth = _window.ActualWidth > 1 ? _window.ActualWidth : 120;
            double windowHeight = _window.ActualHeight > 1 ? _window.ActualHeight : 40;
            double gameWidth = rect.Right - rect.Left;
            double gameHeight = rect.Bottom - rect.Top;

            double normX = LauncherSettings.Current.TimerPositionX;
            double normY = LauncherSettings.Current.TimerPositionY;

            double maxLeft = Math.Max(0, gameWidth - 2 * EdgeMargin - windowWidth);
            double maxTop = Math.Max(0, gameHeight - 2 * EdgeMargin - windowHeight);
            double left = rect.Left + EdgeMargin + normX * maxLeft;
            double top = rect.Top + EdgeMargin + normY * maxTop;

            _window.Left = left;
            _window.Top = top;
        }

        private static void ParkOverlayForCapture()
        {
            if (_window == null)
                return;

            if (!_window.IsVisible)
                _window.Show();

            // Ensure WPF measures the size at least once so capture apps can match it reliably.
            _window.UpdateLayout();
            if (_window.ActualWidth < 1 || _window.ActualHeight < 1)
            {
                if (double.IsNaN(_window.Width) || _window.Width < 1)
                    _window.Width = 140;
                if (double.IsNaN(_window.Height) || _window.Height < 1)
                    _window.Height = 44;
            }

            _window.Topmost = false;
            _window.Opacity = 1;
            _window.Left = ParkedWindowLeft;
            _window.Top = ParkedWindowTop;
        }

        private static bool TryGetOverlayTargetRect(out RECT rect, out bool gameFocused)
        {
            gameFocused = TryGetFocusedSubnauticaRect(out rect);
            if (gameFocused)
                return true;

            if (IsCaptureFriendlyForeground() && TryGetAnySubnauticaRect(out rect))
                return true;

            return false;
        }

        private static bool TryGetFocusedSubnauticaRect(out RECT rect)
        {
            rect = default;

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || IsIconic(foreground))
                return false;

            _ = GetWindowThreadProcessId(foreground, out uint processId);
            if (processId == 0)
                return false;

            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!process.ProcessName.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                return false;
            }

            return GetWindowRect(foreground, out rect);
        }

        private static bool TryGetAnySubnauticaRect(out RECT rect)
        {
            rect = default;

            Process[] processes = Process.GetProcessesByName("Subnautica");
            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    IntPtr hwnd = process.MainWindowHandle;
                    if (hwnd == IntPtr.Zero || IsIconic(hwnd))
                        continue;

                    if (GetWindowRect(hwnd, out rect))
                        return true;
                }
                catch
                {
                    // process exit race, ignore
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }

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
                using var process = Process.GetProcessById((int)processId);
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
