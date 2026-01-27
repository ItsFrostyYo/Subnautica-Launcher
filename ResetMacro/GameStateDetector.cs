using SubnauticaLauncher.Display;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Macros
{
    [SupportedOSPlatform("windows")]
    public static class GameStateDetector
    {
        // 🔥 NEW: display-aware detect
        public static GameState Detect(GameStateProfile p, DisplayInfo display)
        {
            FocusSubnautica();

            if (IsBlackScreen(p, display))
                return GameState.BlackScreen;

            if (Matches(display.ScalePoint(p.MainMenuPixel), p.MainMenuColor, p.ColorTolerance))
                return GameState.MainMenu;

            if (Matches(display.ScalePoint(p.InGamePixel), p.InGameColor, p.ColorTolerance))
                return GameState.InGame;

            return GameState.Unknown;
        }

        // 🔥 NEW: display-aware black screen
        public static bool IsBlackScreen(GameStateProfile p, DisplayInfo display)
        {
            var c = GetPixel(display.ScalePoint(p.BlackPixel));
            return c.R < 8 && c.G < 8 && c.B < 8;
        }

        // ================= INTERNAL =================

        private static bool Matches(Point p, Color e, int t)
        {
            var a = GetPixel(p);
            return Math.Abs(a.R - e.R) <= t &&
                   Math.Abs(a.G - e.G) <= t &&
                   Math.Abs(a.B - e.B) <= t;
        }

        private static Color GetPixel(Point p)
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(p, Point.Empty, new Size(1, 1));
            return bmp.GetPixel(0, 0);
        }

        private static void FocusSubnautica()
        {
            var p = Process.GetProcessesByName("Subnautica");
            if (p.Length > 0)
                SetForegroundWindow(p[0].MainWindowHandle);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}