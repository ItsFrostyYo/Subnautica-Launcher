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
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => DisplayLoopAsync(_cts.Token));
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

                    bool subnauticaFocused = TryGetFocusedSubnauticaRect(out var rect);

                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (subnauticaFocused)
                            {
                                if (_window == null)
                                {
                                    _window = new SpeedrunTimerOverlay();
                                    _window.ApplyStyle(LauncherSettings.Current);
                                    _window.SetTime(elapsed);
                                }
                                else
                                {
                                    _window.SetTime(elapsed);
                                }

                                PositionOverlay(rect);

                                if (!_window.IsVisible)
                                    _window.Show();
                            }
                            else
                            {
                                _window?.Hide();
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
