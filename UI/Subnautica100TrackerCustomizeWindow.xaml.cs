using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace SubnauticaLauncher.UI
{
    public partial class Subnautica100TrackerCustomizeWindow : Window
    {
        private const string DefaultBackground = "Lifepod";

        public Subnautica100TrackerCustomizeWindow(
            Subnautica100TrackerOverlaySize currentSize,
            bool unlockPopupEnabled,
            bool survivalStartsEnabled,
            bool creativeStartsEnabled,
            bool biomeTrackerEnabled,
            SubnauticaBiomeTrackerCycleMode biomeCycleMode,
            SubnauticaBiomeTrackerScrollSpeed biomeScrollSpeed)
        {
            InitializeComponent();

            ApplyBackground();
            SelectComboByTag(SizeComboBox, currentSize.ToString(), fallbackIndex: 1);
            SelectComboByTag(BiomeCycleComboBox, biomeCycleMode.ToString(), fallbackIndex: 0);
            SelectComboByTag(BiomeSpeedComboBox, biomeScrollSpeed.ToString(), fallbackIndex: 1);

            UnlockPopupCheckBox.IsChecked = unlockPopupEnabled;
            SurvivalStartCheckBox.IsChecked = survivalStartsEnabled;
            CreativeStartCheckBox.IsChecked = creativeStartsEnabled;
            BiomeTrackerEnabledCheckBox.IsChecked = biomeTrackerEnabled;
        }

        public Subnautica100TrackerOverlaySize SelectedSize { get; private set; } = Subnautica100TrackerOverlaySize.Medium;
        public bool UnlockPopupEnabled { get; private set; } = true;
        public bool SurvivalStartsEnabled { get; private set; } = true;
        public bool CreativeStartsEnabled { get; private set; } = true;
        public bool BiomeTrackerEnabled { get; private set; }
        public SubnauticaBiomeTrackerCycleMode BiomeCycleMode { get; private set; } = SubnauticaBiomeTrackerCycleMode.Databanks;
        public SubnauticaBiomeTrackerScrollSpeed BiomeScrollSpeed { get; private set; } = SubnauticaBiomeTrackerScrollSpeed.Medium;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SelectedSize = ReadEnumSelection(SizeComboBox, Subnautica100TrackerOverlaySize.Medium);
            UnlockPopupEnabled = UnlockPopupCheckBox.IsChecked == true;
            SurvivalStartsEnabled = SurvivalStartCheckBox.IsChecked == true;
            CreativeStartsEnabled = CreativeStartCheckBox.IsChecked == true;
            BiomeTrackerEnabled = BiomeTrackerEnabledCheckBox.IsChecked == true;
            BiomeCycleMode = ReadEnumSelection(BiomeCycleComboBox, SubnauticaBiomeTrackerCycleMode.Databanks);
            BiomeScrollSpeed = ReadEnumSelection(BiomeSpeedComboBox, SubnauticaBiomeTrackerScrollSpeed.Medium);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void ApplyBackground()
        {
            string preset = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(preset))
                preset = DefaultBackground;

            string[] candidates =
            {
                preset,
                DefaultBackground
            };

            foreach (string candidate in candidates)
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
                catch
                {
                    // fallback candidate
                }
            }
        }

        private static void SelectComboByTag(ComboBox comboBox, string tagValue, int fallbackIndex)
        {
            ComboBoxItem? item = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(comboItem =>
                    comboItem.Tag is string tag &&
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
    }
}
