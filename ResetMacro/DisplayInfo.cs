using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Display
{
    public enum MonitorTier
    {
        Unknown,
        OneK_1080p,
        TwoK_1440p,
        FourK_2160p
    }

    [SupportedOSPlatform("windows")]
    public sealed class DisplayInfo
    {
        public int Width { get; }
        public int Height { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public MonitorTier Tier { get; }

        private DisplayInfo(int width, int height)
        {
            Width = width;
            Height = height;

            ScaleX = width / 1920f;
            ScaleY = height / 1080f;

            Tier = DetectTier(width, height);
        }

        public static DisplayInfo GetPrimary()
        {
            var screen = Screen.PrimaryScreen
                ?? throw new InvalidOperationException("No primary screen detected.");

            return new DisplayInfo(
                screen.Bounds.Width,
                screen.Bounds.Height
            );
        }

        private static MonitorTier DetectTier(int w, int h)
        {
            if (w >= 3800 && h >= 2100)
                return MonitorTier.FourK_2160p;

            if (w >= 2500 && h >= 1400)
                return MonitorTier.TwoK_1440p;

            if (w >= 1800 && h >= 1000)
                return MonitorTier.OneK_1080p;

            return MonitorTier.Unknown;
        }
    }
}