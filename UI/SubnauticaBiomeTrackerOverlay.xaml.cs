using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using SubnauticaLauncher.Gameplay;

namespace SubnauticaLauncher.UI
{
    public partial class SubnauticaBiomeTrackerOverlay : Window
    {
        private const double MinimumSlotHeight = 24;
        private const double MinimumSlotWidth = 56;
        private int _visibleSlots = 4;
        private double _typeFontSize = 8;
        private double _nameFontSize = 9;
        private string _lastSignature = string.Empty;

        public SubnauticaBiomeTrackerOverlay()
        {
            InitializeComponent();
            Left = 220;
            Top = 10;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowNative.MakeClickThrough(this);
        }

        public void ApplySizePreset(Subnautica100TrackerOverlaySize size)
        {
            switch (size)
            {
                case Subnautica100TrackerOverlaySize.Small:
                    SetEntryFonts(typeSize: 7, nameSize: 8.5);
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    SetEntryFonts(typeSize: 8.5, nameSize: 10);
                    break;
                default:
                    SetEntryFonts(typeSize: 7.5, nameSize: 9);
                    break;
            }
        }

        public void SetEntries(
            IReadOnlyList<(string Type, string Name)> entries,
            int visibleSlots,
            double scrollProgress)
        {
            _visibleSlots = Math.Max(1, visibleSlots);
            double clampedProgress = Math.Max(0, Math.Min(1, scrollProgress));

            double viewportHeight = EntryViewport.ActualHeight;
            if (viewportHeight <= 1)
                viewportHeight = Math.Max(1, Height - 8);

            double viewportWidth = EntryViewport.ActualWidth;
            if (viewportWidth <= 1)
                viewportWidth = Math.Max(1, Width - 8);

            double slotPitch = viewportWidth / _visibleSlots;
            double slotWidth = Math.Max(MinimumSlotWidth, slotPitch - 4);
            double slotHeight = Math.Max(MinimumSlotHeight, viewportHeight - 2);

            string signature = BuildSignature(entries, slotWidth, slotHeight);
            if (!string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                var models = entries.Select(entry => new BiomeEntryModel
                {
                    Type = entry.Type,
                    Name = entry.Name,
                    SlotWidth = slotWidth,
                    SlotHeight = slotHeight,
                    TypeFontSize = _typeFontSize,
                    NameFontSize = FitNameFontSize(entry.Name, slotWidth, slotHeight)
                }).ToList();

                EntryItemsControl.ItemsSource = models;
                _lastSignature = signature;
            }

            EntriesTranslateTransform.X = -clampedProgress * slotPitch;
        }

        private double FitNameFontSize(string name, double slotWidth, double slotHeight)
        {
            double size = _nameFontSize;
            int length = string.IsNullOrWhiteSpace(name) ? 0 : name.Length;

            if (slotWidth < 88)
                size -= 0.8;
            else if (slotWidth < 100)
                size -= 0.4;

            if (slotHeight < 68)
                size -= 0.6;

            if (length > 24)
                size -= 0.35;
            if (length > 36)
                size -= 0.55;
            if (length > 52)
                size -= 0.7;

            return Math.Max(7, Math.Min(_nameFontSize, size));
        }

        private void SetEntryFonts(double typeSize, double nameSize)
        {
            _typeFontSize = typeSize;
            _nameFontSize = nameSize;
            _lastSignature = string.Empty;
        }

        private static string BuildSignature(IReadOnlyList<(string Type, string Name)> entries, double slotWidth, double slotHeight)
        {
            var sb = new StringBuilder(256);
            sb.Append(slotWidth.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(';');
            sb.Append(slotHeight.ToString("F3", CultureInfo.InvariantCulture));

            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append('|');
                sb.Append(entries[i].Type);
                sb.Append(':');
                sb.Append(entries[i].Name);
            }

            return sb.ToString();
        }

        private sealed class BiomeEntryModel
        {
            public string Type { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public double SlotWidth { get; set; }
            public double SlotHeight { get; set; }
            public double TypeFontSize { get; set; }
            public double NameFontSize { get; set; }
        }
    }
}
