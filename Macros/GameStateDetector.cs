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
            Color c = GetPixel(null, string.Empty, p.BlackPixel, display);
            return c.R < 8 && c.G < 8 && c.B < 8;
        }

        public static bool IsBlackScreen(Process? process, GameStateProfile p, DisplayInfo display)
        {
            Color c = GetPixel(process, "Subnautica", p.BlackPixel, display);
            return c.R < 8 && c.G < 8 && c.B < 8;
        }

        private static GameState DetectCore(Process? process, string processName, GameStateProfile p, DisplayInfo display, bool focusGame)
        {
            if (focusGame)
                FocusGame(process);

            if (UsesMemoryStateDetection(processName))
            {
                if (process != null && TryDetectFromMemory(processName, process, out GameState state))
                    return state;
            }

            if (IsBlackScreen(process, p, display))
                return GameState.BlackScreen;

            if (Matches(process, processName, p.MainMenuPixel, p.MainMenuColor, p.ColorTolerance, display))
                return GameState.MainMenu;

            if (Matches(process, processName, p.InGamePixel, p.InGameColor, p.ColorTolerance, display))
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

        private static bool Matches(Process? process, string processName, Point logicalPoint, Color expected, int tolerance, DisplayInfo display)
        {
            Color actual = GetPixel(process, processName, logicalPoint, display);
            return Math.Abs(actual.R - expected.R) <= tolerance &&
                   Math.Abs(actual.G - expected.G) <= tolerance &&
                   Math.Abs(actual.B - expected.B) <= tolerance;
        }

        private static Color GetPixel(Process? process, string processName, Point logicalPoint, DisplayInfo display)
        {
            bool useWindowRelative = string.Equals(processName, "Subnautica", StringComparison.OrdinalIgnoreCase);
            Point p = useWindowRelative && GameWindowCoordinateMapper.TryMapLogicalPoint(process, logicalPoint, out Point mapped)
                ? mapped
                : display.ScalePoint(logicalPoint);

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
