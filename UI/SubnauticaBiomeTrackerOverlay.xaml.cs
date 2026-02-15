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
        private const double MinimumSlotHeight = 10;
        private int _visibleSlots = 4;
        private double _typeFontSize = 8;
        private double _nameFontSize = 9;
        private double _lineHeight = 9.5;
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
                    SetEntryFonts(typeSize: 7, nameSize: 8, lineHeight: 8);
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    SetEntryFonts(typeSize: 9.5, nameSize: 10.5, lineHeight: 10.5);
                    break;
                default:
                    SetEntryFonts(typeSize: 8, nameSize: 9, lineHeight: 9.5);
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

            double slotPitch = viewportHeight / _visibleSlots;
            double slotHeight = Math.Max(MinimumSlotHeight, slotPitch - 2);

            string signature = BuildSignature(entries, slotHeight);
            if (!string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                var models = entries.Select(entry => new BiomeEntryModel
                {
                    Type = entry.Type,
                    Name = entry.Name,
                    SlotHeight = slotHeight,
                    TypeFontSize = _typeFontSize,
                    NameFontSize = _nameFontSize,
                    LineHeight = _lineHeight
                }).ToList();

                EntryItemsControl.ItemsSource = models;
                _lastSignature = signature;
            }

            EntriesTranslateTransform.Y = -clampedProgress * slotPitch;
        }

        private void SetEntryFonts(double typeSize, double nameSize, double lineHeight)
        {
            _typeFontSize = typeSize;
            _nameFontSize = nameSize;
            _lineHeight = lineHeight;
            _lastSignature = string.Empty;
        }

        private static string BuildSignature(IReadOnlyList<(string Type, string Name)> entries, double slotHeight)
        {
            var sb = new StringBuilder(256);
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
            public double SlotHeight { get; set; }
            public double TypeFontSize { get; set; }
            public double NameFontSize { get; set; }
            public double LineHeight { get; set; }
        }
    }
}
