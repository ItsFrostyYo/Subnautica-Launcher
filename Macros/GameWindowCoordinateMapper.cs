using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Macros
{
    [SupportedOSPlatform("windows")]
    internal static class GameWindowCoordinateMapper
    {
        private const float BaseWidth = 1920f;
        private const float BaseHeight = 1080f;

        public static bool TryMapLogicalPoint(Process? process, Point logicalPoint, out Point screenPoint)
        {
            screenPoint = logicalPoint;

            if (process == null)
                return false;

            try
            {
                if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                    return false;

                if (!GetClientRect(process.MainWindowHandle, out RECT clientRect))
                    return false;

                POINT origin = new() { X = 0, Y = 0 };
                if (!ClientToScreen(process.MainWindowHandle, ref origin))
                    return false;

                int clientWidth = clientRect.Right - clientRect.Left;
                int clientHeight = clientRect.Bottom - clientRect.Top;
                if (clientWidth <= 0 || clientHeight <= 0)
                    return false;

                int scaledX = origin.X + (int)Math.Round((logicalPoint.X / BaseWidth) * clientWidth);
                int scaledY = origin.Y + (int)Math.Round((logicalPoint.Y / BaseHeight) * clientHeight);
                screenPoint = new Point(scaledX, scaledY);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
