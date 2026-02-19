using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Timer;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using System.Drawing;
using System.Windows.Forms;

namespace SubnauticaLauncher.UI
{
    public partial class SpeedrunTimerStylePlacementWindow : Window
    {
        private const string DefaultBackground = "Lifepod";
        private const double PreviewAspect = 16.0 / 9.0;

        private bool _dragging;
        private Point _dragOffset;
        private bool _syncing;

        public SpeedrunTimerStylePlacementWindow()
        {
            InitializeComponent();
            ApplyBackground();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _syncing = true;
            // Reload from file so we always show last saved values (e.g. font size 35 after restart)
            LauncherSettings.Reload();
            LoadSettingsIntoUI();
            InitializePreviewBackground();
            PopulateBackgroundDropdown();
            _syncing = false;
            
            // Wait for layout to be ready before updating previews
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateAllPreviews();
                PositionTimerPreviewFromNormalized();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void LoadSettingsIntoUI()
        {
            var s = LauncherSettings.Current;

            // Load actual saved settings, not defaults
            TextColorInput.Text = s.TimerForegroundColor ?? "#FFFFFF";
            TextOutlineCheckBox.IsChecked = s.TimerTextBorderEnabled;
            OutlineThicknessInput.Text = s.TimerTextBorderThickness.ToString(CultureInfo.InvariantCulture);
            OutlineColorInput.Text = s.TimerTextBorderColor ?? "#000000";
            PaddingHInput.Text = s.TimerPaddingH.ToString(CultureInfo.InvariantCulture);
            PaddingVInput.Text = s.TimerPaddingV.ToString(CultureInfo.InvariantCulture);
            FontSizeInput.Text = s.TimerFontSize.ToString(CultureInfo.InvariantCulture);
            BgColorInput.Text = s.TimerBackgroundColor ?? "#000000";
            BgOpacitySlider.Value = s.TimerBackgroundOpacity * 100;
            BgOpacityLabel.Text = $"{(int)(s.TimerBackgroundOpacity * 100)}%";

            OutlineOptionsPanel.Visibility = s.TimerTextBorderEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        #region Color helpers

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

        private static SolidColorBrush BrushFromHex(string hex, double opacity = 1.0)
        {
            if (TryParseHex(hex, out Color c))
            {
                c.A = (byte)(opacity * 255);
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
            return Brushes.Transparent;
        }

        /// <summary>Show system color picker; returns hex string or null if cancelled.</summary>
        private static string? ShowColorPicker(string currentHex)
        {
            try
            {
                if (!TryParseHex(currentHex, out Color wpfColor))
                    wpfColor = Colors.White;
                using var dialog = new ColorDialog
                {
                    Color = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B),
                    FullOpen = true
                };
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void TextColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_syncing) return;
            string? hex = ShowColorPicker(TextColorInput?.Text ?? "#FFFFFF");
            if (hex != null && TextColorInput != null)
            {
                _syncing = true;
                TextColorInput.Text = hex;
                _syncing = false;
                TextColorInput_TextChanged(null!, null!);
                LauncherSettings.Current.TimerForegroundColor = hex;
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void OutlineColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_syncing) return;
            string? hex = ShowColorPicker(OutlineColorInput?.Text ?? "#000000");
            if (hex != null && OutlineColorInput != null)
            {
                _syncing = true;
                OutlineColorInput.Text = hex;
                _syncing = false;
                OutlineColorInput_TextChanged(null!, null!);
                LauncherSettings.Current.TimerTextBorderColor = hex;
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void BgColorPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_syncing) return;
            string? hex = ShowColorPicker(BgColorInput?.Text ?? "#000000");
            if (hex != null && BgColorInput != null)
            {
                _syncing = true;
                BgColorInput.Text = hex;
                _syncing = false;
                BgColorInput_TextChanged(null!, null!);
                LauncherSettings.Current.TimerBackgroundColor = hex;
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        #endregion

        #region Style event handlers

        private void TextColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            if (TextColorInput == null || TextColorPreview == null || TimerPreviewText == null)
                return;

            if (TryParseHex(TextColorInput.Text, out Color c))
            {
                TextColorPreview.Background = new SolidColorBrush(c);
                TimerPreviewText.Foreground = new SolidColorBrush(c);
                
                // Save and apply live
                LauncherSettings.Current.TimerForegroundColor = TextColorInput.Text.Trim();
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void TextOutlineCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            if (TextOutlineCheckBox == null || OutlineOptionsPanel == null) return;
            bool enabled = TextOutlineCheckBox.IsChecked == true;
            OutlineOptionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateOutlineEffect();
            
            // Save and apply live
            LauncherSettings.Current.TimerTextBorderEnabled = enabled;
            SpeedrunTimerController.ReapplyStyle();
        }

        private void OutlineColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            if (OutlineColorInput == null || OutlineColorPreview == null) return;
            if (TryParseHex(OutlineColorInput.Text, out Color c))
            {
                OutlineColorPreview.Background = new SolidColorBrush(c);
                UpdateOutlineEffect();
                
                // Save and apply live
                LauncherSettings.Current.TimerTextBorderColor = OutlineColorInput.Text.Trim();
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void BgColorInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            if (BgColorInput == null || BgColorPreview == null) return;
            if (TryParseHex(BgColorInput.Text, out Color c))
            {
                BgColorPreview.Background = new SolidColorBrush(c);
                UpdateBackgroundPreview();
                
                // Save and apply live
                LauncherSettings.Current.TimerBackgroundColor = BgColorInput.Text.Trim();
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing || BgOpacityLabel == null) return;
            BgOpacityLabel.Text = $"{(int)BgOpacitySlider.Value}%";
            UpdateBackgroundPreview();
            
            // Save and apply live
            LauncherSettings.Current.TimerBackgroundOpacity = BgOpacitySlider.Value / 100.0;
            SpeedrunTimerController.ReapplyStyle();
        }

        private void OutlineThicknessInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            UpdateOutlineEffect();
            
            // Save and apply live
            if (OutlineThicknessInput != null &&
                double.TryParse(OutlineThicknessInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double thickness))
            {
                LauncherSettings.Current.TimerTextBorderThickness = Math.Clamp(thickness, 0.5, 5);
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void PaddingHInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            UpdatePreviewSize();
            
            // Save and apply live
            if (PaddingHInput != null &&
                double.TryParse(PaddingHInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double ph))
            {
                LauncherSettings.Current.TimerPaddingH = Math.Max(0, ph);
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void PaddingVInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            UpdatePreviewSize();
            
            // Save and apply live
            if (PaddingVInput != null &&
                double.TryParse(PaddingVInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double pv))
            {
                LauncherSettings.Current.TimerPaddingV = Math.Max(0, pv);
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        private void FontSizeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            UpdatePreviewSize();
            
            // Save and apply live
            if (FontSizeInput != null &&
                double.TryParse(FontSizeInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double fontSize))
            {
                LauncherSettings.Current.TimerFontSize = Math.Clamp(fontSize, 8, 72);
                SpeedrunTimerController.ReapplyStyle();
            }
        }

        #endregion

        #region Preview updates

        private void UpdateAllPreviews()
        {
            if (TryParseHex(TextColorInput.Text, out Color textColor))
            {
                TextColorPreview.Background = new SolidColorBrush(textColor);
                TimerPreviewText.Foreground = new SolidColorBrush(textColor);
            }

            if (TryParseHex(OutlineColorInput.Text, out Color outlineColor))
                OutlineColorPreview.Background = new SolidColorBrush(outlineColor);

            if (TryParseHex(BgColorInput.Text, out Color bgColor))
                BgColorPreview.Background = new SolidColorBrush(bgColor);

            UpdateBackgroundPreview();
            UpdateOutlineEffect();
            if (TimerPreviewText != null)
                TimerPreviewText.Text = "00:00:00.000";
            UpdatePreviewSize();
        }

        private void UpdatePreviewSize()
        {
            if (TimerPreviewBorder == null || TimerPreviewText == null ||
                PaddingHInput == null || PaddingVInput == null || FontSizeInput == null)
                return;

            // Update font size
            if (double.TryParse(FontSizeInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double fontSize))
            {
                double scaleFactor = 0.5; // Preview scale factor
                TimerPreviewText.FontSize = Math.Clamp(fontSize, 8, 72) * scaleFactor;
            }

            // Update padding
            if (double.TryParse(PaddingHInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double ph) &&
                double.TryParse(PaddingVInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double pv))
            {
                double scaleFactor = 0.5; // Preview scale factor
                TimerPreviewBorder.Padding = new Thickness(ph * scaleFactor, pv * scaleFactor,
                    ph * scaleFactor, pv * scaleFactor);
            }

            // Rounded corners scaled to fit the timer (same proportion as real overlay)
            if (double.TryParse(FontSizeInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double fs) &&
                double.TryParse(PaddingVInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double pv2))
            {
                double scaleFactor = 0.5;
                double effectiveHeight = (fs * scaleFactor * 1.2) + (pv2 * scaleFactor * 2);
                double radius = Math.Max(2, Math.Min(effectiveHeight * 0.4, 12));
                TimerPreviewBorder.CornerRadius = new CornerRadius(radius);
            }

            // Force layout update and wait for it to complete
            TimerPreviewBorder.UpdateLayout();
            TimerPreviewText.UpdateLayout();
            
            // Use dispatcher to ensure layout is complete before recalculating position
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PositionTimerPreviewFromNormalized();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateBackgroundPreview()
        {
            if (BgOpacitySlider == null || BgColorInput == null || TimerPreviewBorder == null) return;
            double opacity = BgOpacitySlider.Value / 100.0;
            TimerPreviewBorder.Background = BrushFromHex(BgColorInput.Text, opacity);
        }

        private void UpdateOutlineEffect()
        {
            if (TextOutlineCheckBox == null || TimerPreviewText == null) return;
            if (TextOutlineCheckBox.IsChecked != true)
            {
                TimerPreviewText.Effect = null;
                return;
            }

            if (OutlineThicknessInput == null || OutlineColorInput == null) return;
            if (!double.TryParse(OutlineThicknessInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double thickness))
                thickness = 1;

            if (!TryParseHex(OutlineColorInput.Text, out Color shadowColor))
                shadowColor = Colors.Black;

            TimerPreviewText.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = shadowColor,
                BlurRadius = thickness * 2,
                ShadowDepth = 0,
                Direction = 0,
                Opacity = 1.0
            };
        }

        #endregion

        #region Placement drag

        private void ScreenPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point clickPos = e.GetPosition(ScreenPreviewCanvas);
            double left = Canvas.GetLeft(TimerPreviewBorder);
            double top = Canvas.GetTop(TimerPreviewBorder);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            double bw = TimerPreviewBorder.ActualWidth;
            double bh = TimerPreviewBorder.ActualHeight;

            if (clickPos.X >= left && clickPos.X <= left + bw &&
                clickPos.Y >= top && clickPos.Y <= top + bh)
            {
                _dragging = true;
                _dragOffset = new Point(clickPos.X - left, clickPos.Y - top);
                ScreenPreviewCanvas.CaptureMouse();
            }
        }

        private void ScreenPreview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;

            Point pos = e.GetPosition(ScreenPreviewCanvas);
            double canvasW = ScreenPreviewCanvas.ActualWidth;
            double canvasH = ScreenPreviewCanvas.ActualHeight;
            double bw = TimerPreviewBorder.ActualWidth;
            double bh = TimerPreviewBorder.ActualHeight;

            double maxLeft = Math.Max(0, canvasW - bw - PreviewMargin);
            double maxTop = Math.Max(0, canvasH - bh - PreviewMargin);
            double newLeft = Math.Clamp(pos.X - _dragOffset.X, PreviewMargin, maxLeft);
            double newTop = Math.Clamp(pos.Y - _dragOffset.Y, PreviewMargin, maxTop);

            Canvas.SetLeft(TimerPreviewBorder, newLeft);
            Canvas.SetTop(TimerPreviewBorder, newTop);

            UpdatePositionLabel(newLeft, newTop, canvasW, canvasH, bw, bh);
        }

        private void ScreenPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ScreenPreviewCanvas.ReleaseMouseCapture();
        }

        private const double PreviewMargin = 1; // Timer can sit within 10px of preview edges

        // Reference resolution for displayed coordinates (position scales to 1K/2K/3K/4K in-game)
        private const int RefWidth = 1920;
        private const int RefHeight = 1080;

        private void PositionTimerPreviewFromNormalized()
        {
            TimerPreviewBorder.UpdateLayout();

            double canvasW = ScreenPreviewCanvas.ActualWidth;
            double canvasH = ScreenPreviewCanvas.ActualHeight;
            double bw = TimerPreviewBorder.ActualWidth;
            double bh = TimerPreviewBorder.ActualHeight;

            if (canvasW < 1 || canvasH < 1 || bw < 1 || bh < 1)
            {
                ScreenPreviewCanvas.Dispatcher.InvokeAsync(() =>
                    PositionTimerPreviewFromNormalized(),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            double normX = LauncherSettings.Current.TimerPositionX;
            double normY = LauncherSettings.Current.TimerPositionY;

            // Usable area inside margin so timer never clips
            double usableW = Math.Max(0, canvasW - 2 * PreviewMargin);
            double usableH = Math.Max(0, canvasH - 2 * PreviewMargin);
            double maxLeft = Math.Max(0, usableW - bw);
            double maxTop = Math.Max(0, usableH - bh);

            double left = PreviewMargin + normX * maxLeft;
            double top = PreviewMargin + normY * maxTop;

            // Clamp so timer stays fully inside preview
            left = Math.Clamp(left, PreviewMargin, canvasW - bw - PreviewMargin);
            top = Math.Clamp(top, PreviewMargin, canvasH - bh - PreviewMargin);

            Canvas.SetLeft(TimerPreviewBorder, left);
            Canvas.SetTop(TimerPreviewBorder, top);

            UpdatePositionLabel(left, top, canvasW, canvasH, bw, bh);
        }

        private void UpdatePositionLabel(double leftPx, double topPx, double canvasW, double canvasH, double bw, double bh)
        {
            if (PositionLabel == null) return;
            if (canvasW < 1 || canvasH < 1) return;

            // Convert to 1920×1080 reference; show only X and Y (2 coordinates)
            double scaleX = RefWidth / canvasW;
            double scaleY = RefHeight / canvasH;
            int xRef = (int)Math.Round(leftPx * scaleX);
            int yRef = (int)Math.Round(topPx * scaleY);

            PositionLabel.Text = $"X {xRef} px,  Y {yRef} px";
        }

        #endregion

        #region Actions

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsFromUI();
            DialogResult = true;
        }

        private void ResetToDefault_Click(object sender, RoutedEventArgs e)
        {
            _syncing = true;

            TextColorInput.Text = "#FFFFFF";
            TextOutlineCheckBox.IsChecked = false;
            OutlineThicknessInput.Text = "1";
            OutlineColorInput.Text = "#000000";
            PaddingHInput.Text = "10";
            PaddingVInput.Text = "6";
            FontSizeInput.Text = "40";
            BgColorInput.Text = "#000000";
            BgOpacitySlider.Value = 53;
            LauncherSettings.Current.TimerFormat = SpeedrunTimerFormat.RightAligned;

            _syncing = false;

            OutlineOptionsPanel.Visibility = Visibility.Collapsed;
            UpdateAllPreviews();

            LauncherSettings.Current.TimerPositionX = 1.0;
            LauncherSettings.Current.TimerPositionY = 0.0;
            PositionTimerPreviewFromNormalized();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        #endregion

        #region Save / Load

        private void SaveSettingsFromUI()
        {
            var s = LauncherSettings.Current;

            s.TimerForegroundColor = TextColorInput.Text.Trim();
            s.TimerTextBorderEnabled = TextOutlineCheckBox.IsChecked == true;

            if (double.TryParse(OutlineThicknessInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double thickness))
                s.TimerTextBorderThickness = Math.Clamp(thickness, 0.5, 5);

            s.TimerTextBorderColor = OutlineColorInput.Text.Trim();

            if (double.TryParse(PaddingHInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double ph))
                s.TimerPaddingH = Math.Max(0, ph);

            if (double.TryParse(PaddingVInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double pv))
                s.TimerPaddingV = Math.Max(0, pv);

            if (double.TryParse(FontSizeInput.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double fontSize))
                s.TimerFontSize = Math.Clamp(fontSize, 8, 72);

            s.TimerBackgroundColor = BgColorInput.Text.Trim();
            s.TimerBackgroundOpacity = BgOpacitySlider.Value / 100.0;
            s.TimerFormat = SpeedrunTimerFormat.RightAligned;

            double canvasW = ScreenPreviewCanvas.ActualWidth;
            double canvasH = ScreenPreviewCanvas.ActualHeight;
            double bw = TimerPreviewBorder.ActualWidth;
            double bh = TimerPreviewBorder.ActualHeight;
            double left = Canvas.GetLeft(TimerPreviewBorder);
            double top = Canvas.GetTop(TimerPreviewBorder);
            if (double.IsNaN(left)) left = PreviewMargin;
            if (double.IsNaN(top)) top = PreviewMargin;

            double usableW = Math.Max(0, canvasW - 2 * PreviewMargin);
            double usableH = Math.Max(0, canvasH - 2 * PreviewMargin);
            double maxLeft = Math.Max(0, usableW - bw);
            double maxTop = Math.Max(0, usableH - bh);
            s.TimerPositionX = maxLeft > 0 ? Math.Clamp((left - PreviewMargin) / maxLeft, 0, 1) : 1;
            s.TimerPositionY = maxTop > 0 ? Math.Clamp((top - PreviewMargin) / maxTop, 0, 1) : 0;

            LauncherSettings.Save();
        }

        #endregion

        #region Background

        private void ApplyBackground()
        {
            string preset = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(preset))
                preset = DefaultBackground;

            foreach (string candidate in new[] { preset, DefaultBackground })
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri($"pack://application:,,,/Assets/Backgrounds/{candidate}.png", UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();

                    if (FindResource("BackgroundBrush") is ImageBrush brush)
                        brush.ImageSource = image;

                    return;
                }
                catch { }
            }
        }

        private void InitializePreviewBackground()
        {
            if (PreviewBackgroundImage == null) return;
            
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri($"pack://application:,,,/Assets/Backgrounds/{DefaultBackground}.png", UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                PreviewBackgroundImage.Fill = new ImageBrush(image);
            }
            catch
            {
                // Fallback to solid color if image fails
                PreviewBackgroundImage.Fill = new SolidColorBrush(Color.FromArgb(255, 20, 20, 30));
            }
        }

        private void PopulateBackgroundDropdown()
        {
            if (PreviewBackgroundDropdown == null) return;

            var backgrounds = new[]
            {
                "Lifepod", "Safe Shallows", "Kelp Forest", "Grassy Plateau", "Grand Reef",
                "Lost River", "Cove Tree", "Jellyshroom Caves", "Ghost Leviathan",
                "Reaper Leviathan", "Sea Dragon Leviathan", "Floating Island",
                "Aurora Borealis", "Snowfox", "Icy Land", "Ice Worm", "Squid Shark",
                "Twisty Bridges", "Arcitect Facility", "Red Crystals", "Shadow Leviathan",
                "Snow Stalker"
            };

            PreviewBackgroundDropdown.Items.Clear();
            foreach (var bg in backgrounds)
            {
                var item = new ComboBoxItem { Content = bg, Tag = bg };
                PreviewBackgroundDropdown.Items.Add(item);
            }

            // Select Lifepod by default
            SelectComboByTag(PreviewBackgroundDropdown, DefaultBackground, fallbackIndex: 0);
        }

        private void PreviewBackgroundDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || PreviewBackgroundDropdown == null || PreviewBackgroundImage == null) return;

            if (PreviewBackgroundDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string bgName)
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri($"pack://application:,,,/Assets/Backgrounds/{bgName}.png", UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();

                    PreviewBackgroundImage.Fill = new ImageBrush(image);
                }
                catch
                {
                    // Fallback to solid color if image fails
                    PreviewBackgroundImage.Fill = new SolidColorBrush(Color.FromArgb(255, 20, 20, 30));
                }
            }
        }

        #endregion

        #region Helpers

        private static void SelectComboByTag(ComboBox comboBox, string tagValue, int fallbackIndex)
        {
            ComboBoxItem? item = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is string tag &&
                    tag.Equals(tagValue, StringComparison.OrdinalIgnoreCase));

            comboBox.SelectedItem = item ?? comboBox.Items[fallbackIndex];
        }

        private static TEnum ReadEnumSelection<TEnum>(ComboBox comboBox, TEnum fallback)
            where TEnum : struct, Enum
        {
            if (comboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, ignoreCase: true, out TEnum parsed))
            {
                return parsed;
            }
            return fallback;
        }

        #endregion
    }
}
