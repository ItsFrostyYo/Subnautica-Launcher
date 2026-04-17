using SubnauticaLauncher.Display;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Macros
{
    [SupportedOSPlatform("windows")]
    public static class GameStateDetector
    {
        private static readonly ConcurrentDictionary<string, DynamicMonoGameplayEventTracker> MemoryTrackers =
            new(StringComparer.OrdinalIgnoreCase);

        public static GameState Detect(GameStateProfile p, DisplayInfo display)
        {
            Process? process = GetFirstProcess("Subnautica");
            return DetectCore(process, "Subnautica", p, display, focusGame: true);
        }

        public static GameState Detect(string processName, GameStateProfile p, DisplayInfo display, bool focusGame = true)
        {
            Process? process = GetFirstProcess(processName);
            return DetectCore(process, processName, p, display, focusGame);
        }

        public static GameState Detect(Process process, string processName, GameStateProfile p, DisplayInfo display, bool focusGame = true)
        {
            return DetectCore(process, processName, p, display, focusGame);
        }

        public static bool IsBlackScreen(GameStateProfile p, DisplayInfo display)
        {
            Color c = GetPixel(display.ScalePoint(p.BlackPixel));
            return c.R < 8 && c.G < 8 && c.B < 8;
        }

        private static GameState DetectCore(Process? process, string processName, GameStateProfile p, DisplayInfo display, bool focusGame)
        {
            if (focusGame)
                FocusGame(process);

            if (IsBlackScreen(p, display))
                return GameState.BlackScreen;

            if (UsesMemoryStateDetection(processName))
            {
                if (process != null && TryDetectFromMemory(processName, process, out GameState state))
                    return state;

                return GameState.Unknown;
            }

            if (Matches(display.ScalePoint(p.MainMenuPixel), p.MainMenuColor, p.ColorTolerance))
                return GameState.MainMenu;

            if (Matches(display.ScalePoint(p.InGamePixel), p.InGameColor, p.ColorTolerance))
                return GameState.InGame;

            return GameState.Unknown;
        }

        private static bool UsesMemoryStateDetection(string processName)
        {
            return processName.Equals("Subnautica", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDetectFromMemory(string processName, Process process, out GameState state)
        {
            state = GameState.Unknown;

            try
            {
                if (process.HasExited)
                    return false;
            }
            catch
            {
                return false;
            }

            DynamicMonoGameplayEventTracker tracker = MemoryTrackers.GetOrAdd(
                processName,
                static name => new DynamicMonoGameplayEventTracker(name));

            return tracker.TryDetectState(process, out state);
        }

        private static bool Matches(Point p, Color expected, int tolerance)
        {
            Color actual = GetPixel(p);
            return Math.Abs(actual.R - expected.R) <= tolerance &&
                   Math.Abs(actual.G - expected.G) <= tolerance &&
                   Math.Abs(actual.B - expected.B) <= tolerance;
        }

        private static Color GetPixel(Point p)
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(p, Point.Empty, new Size(1, 1));
            return bmp.GetPixel(0, 0);
        }

        private static Process? GetFirstProcess(string processName)
        {
            return GameProcessMonitor.TryOpenRunningProcess(processName, out Process? process)
                ? process
                : null;
        }

        private static void FocusGame(Process? process)
        {
            if (process != null && process.MainWindowHandle != IntPtr.Zero)
                SetForegroundWindow(process.MainWindowHandle);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
