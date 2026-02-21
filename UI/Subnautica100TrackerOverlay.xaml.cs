using SubnauticaLauncher.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SubnauticaLauncher.UI
{
    public partial class Subnautica100TrackerOverlay : Window
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

        public Subnautica100TrackerOverlay()
        {
            InitializeComponent();
            Left = 10;
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
                    StepText.FontSize = 12;
                    ProgressText.FontSize = 10;
                    BreakdownText.FontSize = 9;
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    StepText.FontSize = 18;
                    ProgressText.FontSize = 15;
                    BreakdownText.FontSize = 14;
                    break;
                default:
                    StepText.FontSize = 15;
                    ProgressText.FontSize = 13;
                    BreakdownText.FontSize = 12;
                    break;
            }

            ApplyBiomeSizePreset(size);
        }

        public void ApplyBiomeSizePreset(Subnautica100TrackerOverlaySize size)
        {
            switch (size)
            {
                case Subnautica100TrackerOverlaySize.Small:
                    SetEntryFonts(typeSize: 8.5, nameSize: 9.5);
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    SetEntryFonts(typeSize: 9, nameSize: 10.25);
                    break;
                default:
                    SetEntryFonts(typeSize: 8, nameSize: 9.25);
                    break;
            }
        }

        public void ConfigureCompositeLayout(
            double trackerWidth,
            double trackerHeight,
            bool biomeVisible,
            double biomeWidth,
            double biomeHeight)
        {
            TrackerCard.Width = trackerWidth;
            TrackerCard.Height = trackerHeight;

            Canvas.SetLeft(BiomeCard, trackerWidth + 8);
            BiomeCard.Width = biomeWidth;
            BiomeCard.Height = biomeHeight;
            BiomeCard.Visibility = biomeVisible ? Visibility.Visible : Visibility.Collapsed;

            ToastCard.Width = trackerWidth;
            Canvas.SetTop(ToastCard, trackerHeight + 8);

            double totalWidth = biomeVisible ? (trackerWidth + 8 + biomeWidth) : trackerWidth;
            double topBandHeight = Math.Max(trackerHeight, biomeVisible ? biomeHeight : trackerHeight);
            double toastBandHeight = Math.Max(42, ToastCard.ActualHeight > 1 ? ToastCard.ActualHeight : 46);
            double totalHeight = topBandHeight + 8 + toastBandHeight;

            OverlayCanvas.Width = totalWidth;
            OverlayCanvas.Height = totalHeight;
            Width = totalWidth;
            Height = totalHeight;
        }

        public void SetBiomeVisible(bool visible)
        {
            BiomeCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                TopEntriesCanvas.Children.Clear();
                BottomEntriesCanvas.Children.Clear();
                _topSignature = string.Empty;
                _bottomSignature = string.Empty;
            }
        }

        public void SetProgress(int totalUnlocked, int totalRequired, int blueprintUnlocked, int blueprintRequired, int entriesUnlocked, int entriesRequired)
        {
            double percent = totalRequired > 0
                ? (double)totalUnlocked * 100d / totalRequired
                : 0d;

            ProgressText.Text = $"({totalUnlocked}/{totalRequired}) {Math.Round(percent)}%";
            BreakdownText.Text = $"BP's: {blueprintUnlocked}/{blueprintRequired} | Ency: {entriesUnlocked}/{entriesRequired}";
        }

        public void SetBiomeEntries(
            IReadOnlyList<(string Type, string Name)> topEntries,
            IReadOnlyList<(string Type, string Name)> bottomEntries,
            int rowCount,
            int columnsPerRow,
            double scrollProgress)
        {
            if (BiomeCard.Visibility != Visibility.Visible)
                return;

            _rowCount = Math.Max(1, rowCount);
            _columnsPerRow = Math.Max(1, columnsPerRow);
            double clampedProgress = Math.Max(0, Math.Min(0.999, scrollProgress));

            ConfigureRowLayout();

            double viewportWidth = TopViewport.ActualWidth;
            if (viewportWidth <= 1)
                viewportWidth = Math.Max(1, BiomeCard.Width - 8);

            double viewportHeight = RootGrid.ActualHeight;
            if (viewportHeight <= 1)
                viewportHeight = Math.Max(1, BiomeCard.Height - 8);

            double rowHeight = _rowCount > 1
                ? Math.Max(1, (viewportHeight - RowGap) / 2.0)
                : viewportHeight;

            double laneCount = _columnsPerRow;
            double availableWidth = Math.Max(1, viewportWidth - ((laneCount - 1) * CardGap));
            double slotWidth = availableWidth / laneCount;
            slotWidth = Math.Max(MinimumSlotWidth, Math.Min(slotWidth, viewportWidth - CardGap));

            double stride = slotWidth + CardGap;
            double slotHeight = Math.Max(MinimumSlotHeight, rowHeight - 2);

            IReadOnlyList<(string Type, string Name)> normalizedTop = NormalizeTopRow(topEntries, _columnsPerRow);
            IReadOnlyList<(string Type, string Name)> normalizedBottom = _rowCount > 1
                ? NormalizeBottomRow(bottomEntries)
                : Array.Empty<(string Type, string Name)>();

            SetRowItems(TopEntriesCanvas, normalizedTop, ref _topSignature, slotWidth, slotHeight);

            if (_rowCount > 1)
                SetRowItems(BottomEntriesCanvas, normalizedBottom, ref _bottomSignature, slotWidth, slotHeight);
            else
            {
                BottomEntriesCanvas.Children.Clear();
                _bottomSignature = string.Empty;
            }

            double offsetX = -(clampedProgress * stride);
            TopEntriesTranslateTransform.X = offsetX;
            BottomEntriesTranslateTransform.X = offsetX;
        }

        public void ShowToast(string text)
        {
            UnlockText.Text = text;
            ToastTranslate.X = 0;
            ToastCard.Opacity = 1;
            ToastCard.Visibility = Visibility.Visible;
        }

        public void HideToast()
        {
            ToastCard.Visibility = Visibility.Collapsed;
            ToastCard.Opacity = 0;
            ToastTranslate.X = 0;
        }

        public async Task ShowToastAnimatedAsync(string text)
        {
            UnlockText.Text = text;
            ToastCard.Visibility = Visibility.Visible;
            ToastCard.Opacity = 0;
            ToastTranslate.X = 28;

            await AnimateToastAsync(
                fromX: 28,
                toX: 0,
                fromOpacity: 0,
                toOpacity: 1,
                durationMs: 300,
                easing: new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
        }

        public async Task HideToastAnimatedAsync(bool quick)
        {
            if (ToastCard.Visibility != Visibility.Visible)
            {
                HideToast();
                return;
            }

            int durationMs = quick ? 140 : 1000;
            double fromX = ToastTranslate.X;
            double toX = quick ? fromX + 24 : fromX;
            double fromOpacity = ToastCard.Opacity <= 0 ? 1 : ToastCard.Opacity;

            await AnimateToastAsync(
                fromX: fromX,
                toX: toX,
                fromOpacity: fromOpacity,
                toOpacity: 0,
                durationMs: durationMs,
                easing: null);

            HideToast();
        }

        private Task AnimateToastAsync(
            double fromX,
            double toX,
            double fromOpacity,
            double toOpacity,
            int durationMs,
            IEasingFunction? easing)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var xAnimation = new DoubleAnimation(fromX, toX, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = easing
            };

            var opacityAnimation = new DoubleAnimation(fromOpacity, toOpacity, TimeSpan.FromMilliseconds(durationMs));

            opacityAnimation.Completed += (_, _) => tcs.TrySetResult(true);

            ToastTranslate.BeginAnimation(TranslateTransform.XProperty, xAnimation);
            ToastCard.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);

            return tcs.Task;
        }

        private static IReadOnlyList<(string Type, string Name)> NormalizeTopRow(
            IReadOnlyList<(string Type, string Name)> source,
            int fallbackCount)
        {
            if (source != null && source.Count > 0)
                return source;

            return BuildRepeatedFallback("Waiting for biome data", Math.Max(1, fallbackCount));
        }

        private static IReadOnlyList<(string Type, string Name)> NormalizeBottomRow(
            IReadOnlyList<(string Type, string Name)> source)
        {
            if (source != null && source.Count > 0)
                return source;

            return Array.Empty<(string Type, string Name)>();
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
            Canvas target,
            IReadOnlyList<(string Type, string Name)> entries,
            ref string signature,
            double slotWidth,
            double slotHeight)
        {
            string nextSignature = BuildSignature(entries, slotWidth, slotHeight);
            if (string.Equals(signature, nextSignature, StringComparison.Ordinal))
                return;

            target.Children.Clear();
            double stride = slotWidth + CardGap;

            for (int i = 0; i < entries.Count; i++)
            {
                (string type, string name) = entries[i];
                double nameFontSize = FitNameFontSize(name, slotWidth, slotHeight);

                var border = new Border
                {
                    Width = slotWidth,
                    Height = slotHeight,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x00, 0x00, 0x00)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4, 3, 4, 3)
                };

                if (string.IsNullOrWhiteSpace(type))
                {
                    border.Child = new TextBlock
                    {
                        Text = name,
                        FontSize = Math.Max(10, nameFontSize + 1.25),
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x4F, 0xD3, 0x8B)),
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.None,
                        Margin = new Thickness(0)
                    };
                }
                else
                {
                    var contentGrid = new Grid();
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    var typeText = new TextBlock
                    {
                        Text = type,
                        FontSize = _typeFontSize,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x4F, 0xD3, 0x8B)),
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    Grid.SetRow(typeText, 0);

                    var nameText = new TextBlock
                    {
                        Text = name,
                        FontSize = nameFontSize,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.None,
                        Margin = new Thickness(0)
                    };
                    Grid.SetRow(nameText, 1);

                    contentGrid.Children.Add(typeText);
                    contentGrid.Children.Add(nameText);
                    border.Child = contentGrid;
                }

                Canvas.SetLeft(border, i * stride);
                Canvas.SetTop(border, 0);
                target.Children.Add(border);
            }

            target.Width = entries.Count * stride;
            target.Height = slotHeight;

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
    }
}
