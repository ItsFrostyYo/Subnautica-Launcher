using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using SubnauticaLauncher.Gameplay;

namespace SubnauticaLauncher.UI
{
    public partial class SubnauticaBiomeTrackerOverlay : Window
    {
        private const double MinimumSlotHeight = 24;
        private const double MinimumSlotWidth = 56;
        private const double CardGap = 4;
        private const double RowGap = 4;

        private int _rowCount = 1;
        private int _columnsPerRow = 2;
        private double _typeFontSize = 8;
        private double _nameFontSize = 9.25;
        private string _topSignature = string.Empty;
        private string _bottomSignature = string.Empty;
        private bool _isTwoRowLayout;

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
                    SetEntryFonts(typeSize: 7.5, nameSize: 8.5);
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    SetEntryFonts(typeSize: 9, nameSize: 10.25);
                    break;
                default:
                    SetEntryFonts(typeSize: 8, nameSize: 9.25);
                    break;
            }
        }

        public void SetEntries(
            IReadOnlyList<(string Type, string Name)> topEntries,
            IReadOnlyList<(string Type, string Name)> bottomEntries,
            int rowCount,
            int columnsPerRow,
            double scrollProgress)
        {
            _rowCount = Math.Max(1, rowCount);
            _columnsPerRow = Math.Max(1, columnsPerRow);
            double clampedProgress = Math.Max(0, Math.Min(1, scrollProgress));

            ConfigureRowLayout();

            double viewportWidth = TopViewport.ActualWidth;
            if (viewportWidth <= 1)
                viewportWidth = Math.Max(1, Width - 8);

            double viewportHeight = RootGrid.ActualHeight;
            if (viewportHeight <= 1)
                viewportHeight = Math.Max(1, Height - 8);

            double rowHeight = _rowCount > 1
                ? Math.Max(1, (viewportHeight - RowGap) / 2.0)
                : viewportHeight;

            double peekWidth = GetPeekWidth(viewportWidth);
            double stride = Math.Max(1, (viewportWidth - peekWidth) / Math.Max(1, _columnsPerRow));
            double slotWidth = Math.Max(MinimumSlotWidth, stride - CardGap);

            double slotHeight = Math.Max(MinimumSlotHeight, rowHeight - 2);
            int rowItemCount = _columnsPerRow + 2;

            IReadOnlyList<(string Type, string Name)> normalizedTop = NormalizeRow(topEntries, rowItemCount);
            IReadOnlyList<(string Type, string Name)> normalizedBottom = _rowCount > 1
                ? NormalizeRow(bottomEntries, rowItemCount)
                : Array.Empty<(string Type, string Name)>();

            SetRowItems(TopItemsControl, normalizedTop, ref _topSignature, slotWidth, slotHeight);

            if (_rowCount > 1)
            {
                SetRowItems(BottomItemsControl, normalizedBottom, ref _bottomSignature, slotWidth, slotHeight);
            }
            else
            {
                BottomItemsControl.ItemsSource = null;
                _bottomSignature = string.Empty;
            }

            double pitch = slotWidth + CardGap;
            // Keep scroll anchored to the current card; right-side buffer cards provide the "next" preview.
            double offsetX = -clampedProgress * pitch;
            TopEntriesTranslateTransform.X = offsetX;
            BottomEntriesTranslateTransform.X = offsetX;
        }

        private static IReadOnlyList<(string Type, string Name)> NormalizeRow(
            IReadOnlyList<(string Type, string Name)> source,
            int count)
        {
            count = Math.Max(1, count);
            if (source == null || source.Count == 0)
                return BuildRepeatedFallback("Waiting for biome data", count);

            if (source.Count >= count)
                return source.Take(count).ToArray();

            var rows = new List<(string Type, string Name)>(count);
            rows.AddRange(source);
            while (rows.Count < count)
                rows.Add(source[rows.Count % source.Count]);
            return rows;
        }

        private static IReadOnlyList<(string Type, string Name)> BuildRepeatedFallback(string message, int count)
        {
            var rows = new (string Type, string Name)[count];
            for (int i = 0; i < count; i++)
                rows[i] = ("Biome", message);
            return rows;
        }

        private void ConfigureRowLayout()
        {
            bool twoRows = _rowCount > 1;
            if (_isTwoRowLayout == twoRows)
                return;

            _isTwoRowLayout = twoRows;
            BottomViewport.Visibility = twoRows ? Visibility.Visible : Visibility.Collapsed;
            TopRowDefinition.Height = new GridLength(1, GridUnitType.Star);
            BottomRowDefinition.Height = twoRows
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0, GridUnitType.Pixel);
            TopViewport.Margin = twoRows
                ? new Thickness(0, 0, 0, RowGap / 2.0)
                : new Thickness(0);
            BottomViewport.Margin = twoRows
                ? new Thickness(0, RowGap / 2.0, 0, 0)
                : new Thickness(0);

            _topSignature = string.Empty;
            _bottomSignature = string.Empty;
        }

        private void SetRowItems(
            ItemsControl target,
            IReadOnlyList<(string Type, string Name)> entries,
            ref string signature,
            double slotWidth,
            double slotHeight)
        {
            string nextSignature = BuildSignature(entries, slotWidth, slotHeight);
            if (string.Equals(signature, nextSignature, StringComparison.Ordinal))
                return;

            target.ItemsSource = entries.Select(entry => new BiomeEntryModel
            {
                Type = entry.Type,
                Name = entry.Name,
                SlotWidth = slotWidth,
                SlotHeight = slotHeight,
                TypeFontSize = _typeFontSize,
                NameFontSize = FitNameFontSize(entry.Name, slotWidth, slotHeight)
            }).ToList();

            signature = nextSignature;
        }

        private double FitNameFontSize(string name, double slotWidth, double slotHeight)
        {
            double size = _nameFontSize;
            int length = string.IsNullOrWhiteSpace(name) ? 0 : name.Length;

            if (slotWidth < 90)
                size -= 0.9;
            else if (slotWidth < 112)
                size -= 0.5;

            if (slotHeight < 54)
                size -= 0.5;

            if (length > 22)
                size -= 0.3;
            if (length > 34)
                size -= 0.5;
            if (length > 48)
                size -= 0.75;

            return Math.Max(7, Math.Min(_nameFontSize, size));
        }

        private double GetPeekWidth(double viewportWidth)
        {
            double desired = _columnsPerRow >= 3
                ? viewportWidth * 0.18
                : _rowCount > 1
                    ? viewportWidth * 0.24
                    : viewportWidth * 0.22;

            double minPeek = _columnsPerRow >= 3 ? 40 : 32;
            double maxPeekWithoutGap = (viewportWidth / (_columnsPerRow + 1.0)) - CardGap - 2;
            double hardCap = _columnsPerRow >= 3 ? 120 : 96;
            double maxPeek = Math.Max(minPeek, Math.Min(hardCap, maxPeekWithoutGap));

            return Math.Clamp(desired, minPeek, maxPeek);
        }

        private void SetEntryFonts(double typeSize, double nameSize)
        {
            _typeFontSize = typeSize;
            _nameFontSize = nameSize;
            _topSignature = string.Empty;
            _bottomSignature = string.Empty;
        }

        private static string BuildSignature(
            IReadOnlyList<(string Type, string Name)> entries,
            double slotWidth,
            double slotHeight)
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
