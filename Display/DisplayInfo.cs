using SubnauticaLauncher.Core;
using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

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
        private static readonly object CacheLock = new();
        private static DisplayInfo? _cachedPrimary;
        private static int _cachedWidth;
        private static int _cachedHeight;
        private static DateTime _lastCacheRefreshUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromSeconds(2);
        private static bool _loggedNoPrimaryScreen;

        public int PhysicalWidth { get; }
        public int PhysicalHeight { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public MonitorTier Tier { get; }

        private DisplayInfo(int width, int height)
        {
            PhysicalWidth = width;
            PhysicalHeight = height;

            // Baseline = 1080p.
            ScaleX = width / 1920f;
            ScaleY = height / 1080f;

            Tier = DetectTier(width, height);
        }

        public static DisplayInfo GetPrimary()
        {
            lock (CacheLock)
            {
                DateTime now = DateTime.UtcNow;
                if (_cachedPrimary != null && now - _lastCacheRefreshUtc < CacheRefreshInterval)
                    return _cachedPrimary;

                var screen = Screen.PrimaryScreen;
                if (screen == null)
                {
                    if (!_loggedNoPrimaryScreen)
                    {
                        Logger.Error("[Display] No primary screen could be detected.");
                        _loggedNoPrimaryScreen = true;
                    }

                    throw new InvalidOperationException("No primary screen");
                }

                _loggedNoPrimaryScreen = false;

                int width = screen.Bounds.Width;
                int height = screen.Bounds.Height;
                bool changed = _cachedPrimary == null ||
                               width != _cachedWidth ||
                               height != _cachedHeight;

                if (changed)
                {
                    _cachedPrimary = new DisplayInfo(width, height);
                    _cachedWidth = width;
                    _cachedHeight = height;

                    Logger.Log(
                        $"[Display] Primary display {width}x{height} | " +
                        $"ScaleX={_cachedPrimary.ScaleX:F3} ScaleY={_cachedPrimary.ScaleY:F3} | Tier={_cachedPrimary.Tier}");
                }

                _lastCacheRefreshUtc = now;
                return _cachedPrimary!;
            }

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
