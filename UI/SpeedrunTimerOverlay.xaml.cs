using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace SubnauticaLauncher.UI
{
    public partial class SpeedrunTimerOverlay : Window
    {
        private SpeedrunTimerFormat _format = SpeedrunTimerFormat.RightAligned;

        public SpeedrunTimerOverlay()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowNative.MakeClickThrough(this);
        }

        public void ApplyStyle(LauncherSettings s)
        {
            _format = s.TimerFormat;

            if (TryParseHex(s.TimerForegroundColor, out Color fg))
                TimerText.Foreground = new SolidColorBrush(fg);

            TimerText.FontSize = Math.Clamp(s.TimerFontSize, 8, 72);

            if (TryParseHex(s.TimerBackgroundColor, out Color bg))
            {
                bg.A = (byte)(Math.Clamp(s.TimerBackgroundOpacity, 0, 1) * 255);
                OverlayBorder.Background = new SolidColorBrush(bg);
            }

            OverlayBorder.Padding = new Thickness(s.TimerPaddingH, s.TimerPaddingV,
                                                    s.TimerPaddingH, s.TimerPaddingV);

            // Corner radius scales with font size and padding so the background fits the timer nicely
            double effectiveHeight = (s.TimerFontSize * 1.2) + (s.TimerPaddingV * 2);
            double radius = Math.Max(4, Math.Min(effectiveHeight * 0.4, 24));
            OverlayBorder.CornerRadius = new CornerRadius(radius);

            if (s.TimerTextBorderEnabled &&
                TryParseHex(s.TimerTextBorderColor, out Color shadowColor))
            {
                TimerText.Effect = new DropShadowEffect
                {
                    Color = shadowColor,
                    BlurRadius = Math.Clamp(s.TimerTextBorderThickness, 0.5, 5) * 2,
                    ShadowDepth = 0,
                    Direction = 0,
                    Opacity = 1.0
                };
            }
            else
            {
                TimerText.Effect = null;
            }
        }

        public void SetTime(TimeSpan elapsed)
        {
            TimerText.Text = _format == SpeedrunTimerFormat.LeftAligned
                ? FormatLeftAligned(elapsed)
                : FormatRightAligned(elapsed);
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

        private static bool TryParseHex(string hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            hex = hex.Trim();
            if (!hex.StartsWith("#"))
                hex = "#" + hex;

            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
