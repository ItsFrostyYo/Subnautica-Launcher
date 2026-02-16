using SubnauticaLauncher.Core;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Display
{
    public enum MonitorTier
    {
        Unknown,
        OneK_1080p,
        TwoK_1440p,
        ThreeK_1800p,
        FourK_2160p
    }

    [SupportedOSPlatform("windows")]
    public sealed class DisplayInfo
    {
        public int PhysicalWidth { get; }
        public int PhysicalHeight { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public MonitorTier Tier { get; }

        private DisplayInfo(int width, int height)
        {
            PhysicalWidth = width;
            PhysicalHeight = height;

            // 🔥 BASELINE = 1080p
            ScaleX = width / 1920f;
            ScaleY = height / 1080f;

            Tier = DetectTier(width, height);

            Logger.Log(
                $"DisplayInfo Fetched for Reset Macro |" +
                $"Resolution={width}x{height} | " +
                $"ScaleX={ScaleX:F3} ScaleY={ScaleY:F3}"
            );
        }

        public static DisplayInfo GetPrimary()
        {
            Logger.Log("Detecting primary display");

            var screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                Logger.Log("ERROR: No Primary Screen could be Detected");
                throw new InvalidOperationException("No primary screen");
            }

            Logger.Log(
                $"Primary Screen Bounds Detected: " +
                $"{screen.Bounds.Width}x{screen.Bounds.Height}"
            );

            return new DisplayInfo(
                screen.Bounds.Width,
                screen.Bounds.Height
            );
        }

        private static MonitorTier DetectTier(int w, int h)
        {
            MonitorTier tier;

            if (w >= 3800 && h >= 2100)
                tier = MonitorTier.FourK_2160p;
            else if (w >= 3000 && h >= 1700)
                tier = MonitorTier.ThreeK_1800p;
            else if (w >= 2500 && h >= 1400)
                tier = MonitorTier.TwoK_1440p;
            else if (w >= 1800 && h >= 1000)
                tier = MonitorTier.OneK_1080p;
            else
                tier = MonitorTier.Unknown;
            
            return tier;
        }

        // 🔥 THIS IS THE MONEY METHOD
        public Point ScalePoint(Point p)
        {
            var scaled = new Point(
                (int)Math.Round(p.X * ScaleX),
                (int)Math.Round(p.Y * ScaleY)
            
            );

            return scaled;
        }
    }
}