using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.UI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Application = System.Windows.Application;

namespace SubnauticaLauncher.Gameplay
{
    public static class AcidMushroomRunOverlayController
    {
        private const int AcidMushroomTechType = 3021;
        private const int OverlayPadding = 12;

        private static readonly object Sync = new();
        private static AcidMushroomRunOverlay? _window;
        private static CancellationTokenSource? _cts;
        private static Task? _loopTask;

        private static bool _runActive;
        private static int _totalAcidMushrooms;

        public static void Start()
        {
            lock (Sync)
            {
                if (_loopTask != null)
                    return;

                _runActive = false;
                _totalAcidMushrooms = 0;

                GameEventDocumenter.EventWritten += OnEventWritten;
                _cts = new CancellationTokenSource();
                _loopTask = Task.Run(() => OverlayLoopAsync(_cts.Token));
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
                _runActive = false;
                _totalAcidMushrooms = 0;
            }

            GameEventDocumenter.EventWritten -= OnEventWritten;

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    loopTask?.Wait(1500);
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

        private static void OnEventWritten(GameplayEvent evt)
        {
            if (!evt.Game.Equals("Subnautica", StringComparison.OrdinalIgnoreCase))
                return;

            lock (Sync)
            {
                if (evt.Type == GameplayEventType.GameStateChanged &&
                    evt.Key.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
                {
                    _runActive = false;
                    _totalAcidMushrooms = 0;
                    UpdateOverlayText();
                    return;
                }

                if (evt.Type == GameplayEventType.RunStarted)
                {
                    _runActive = true;
                    _totalAcidMushrooms = 0;
                    UpdateOverlayText();
                    return;
                }

                if (!_runActive || evt.Type != GameplayEventType.ItemPickedUp)
                    return;

                if (!IsAcidMushroomKey(evt.Key))
                    return;

                int amount = evt.Delta > 0 ? evt.Delta : 1;
                _totalAcidMushrooms += amount;
                UpdateOverlayText();
            }
        }

        private static void UpdateOverlayText()
        {
            int total = _totalAcidMushrooms;
            Application.Current.Dispatcher.Invoke(() => _window?.SetTotal(total));
        }

        private static async Task OverlayLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool runActive;
                    lock (Sync)
                        runActive = _runActive;

                    RECT rect = default;
                    bool shouldShow = runActive
                        && !ExplosionResetDisplayController.IsActive
                        && TryGetFocusedSubnauticaWindowRect(out rect);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (shouldShow)
                        {
                            if (_window == null)
                            {
                                _window = new AcidMushroomRunOverlay();
                                _window.SetTotal(_totalAcidMushrooms);
                            }

                            _window.Left = rect.Left + OverlayPadding;
                            _window.Top = rect.Top + OverlayPadding;

                            if (!_window.IsVisible)
                                _window.Show();
                        }
                        else
                        {
                            _window?.Hide();
                        }
                    });
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
        }

        private static bool TryGetFocusedSubnauticaWindowRect(out RECT rect)
        {
            rect = default;

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
                return false;

            var processes = Process.GetProcessesByName("Subnautica");
            try
            {
                var proc = processes.FirstOrDefault(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
                if (proc == null)
                    return false;

                return proc.MainWindowHandle == foreground && GetWindowRect(proc.MainWindowHandle, out rect);
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }

        private static bool IsAcidMushroomKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.Equals(AcidMushroomTechType.ToString(), StringComparison.Ordinal))
                return true;

            if (key.Contains("(3021)", StringComparison.Ordinal))
                return true;

            return key.Contains("Acid Mushroom", StringComparison.OrdinalIgnoreCase)
                || key.Contains("AcidMushroom", StringComparison.OrdinalIgnoreCase);
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
