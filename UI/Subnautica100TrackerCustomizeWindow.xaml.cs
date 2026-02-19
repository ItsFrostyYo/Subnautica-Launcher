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
using Brushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.UI
{
    public partial class Subnautica100TrackerCustomizeWindow : Window
    {
        private const string DefaultBackground = "Lifepod";
        private bool _syncingGamemodes;

        public Subnautica100TrackerCustomizeWindow(
            bool trackerEnabled,
            Subnautica100TrackerOverlaySize currentSize,
            bool unlockPopupEnabled,
            SpeedrunGamemode gamemodeSelection,
            bool biomeTrackerEnabled,
            SubnauticaBiomeTrackerCycleMode biomeCycleMode,
            SubnauticaBiomeTrackerScrollSpeed biomeScrollSpeed)
        {
            InitializeComponent();

            ApplyBackground();
            SelectComboByTag(SizeComboBox, currentSize.ToString(), fallbackIndex: 1);
            SelectComboByTag(BiomeCycleComboBox, biomeCycleMode.ToString(), fallbackIndex: 0);
            SelectComboByTag(BiomeSpeedComboBox, biomeScrollSpeed.ToString(), fallbackIndex: 1);

            TrackerEnabled = trackerEnabled;
            UnlockPopupCheckBox.IsChecked = unlockPopupEnabled;
            GamemodeSelection = gamemodeSelection;
            BiomeTrackerEnabled = biomeTrackerEnabled;

            _syncingGamemodes = true;
            SelectComboByTag(GamemodesDropdown, gamemodeSelection.ToString(), fallbackIndex: 0);
            _syncingGamemodes = false;

            UpdateTrackerEnabledButton();
            UpdateBiomeTrackerButton();
        }

        public bool TrackerEnabled { get; private set; }
        public Subnautica100TrackerOverlaySize SelectedSize { get; private set; } = Subnautica100TrackerOverlaySize.Medium;
        public bool UnlockPopupEnabled { get; private set; } = true;
        public SpeedrunGamemode GamemodeSelection { get; private set; } = SpeedrunGamemode.SurvivalHardcore;
        public bool BiomeTrackerEnabled { get; private set; }
        public SubnauticaBiomeTrackerCycleMode BiomeCycleMode { get; private set; } = SubnauticaBiomeTrackerCycleMode.Databanks;
        public SubnauticaBiomeTrackerScrollSpeed BiomeScrollSpeed { get; private set; } = SubnauticaBiomeTrackerScrollSpeed.Medium;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SelectedSize = ReadEnumSelection(SizeComboBox, Subnautica100TrackerOverlaySize.Medium);
            UnlockPopupEnabled = UnlockPopupCheckBox.IsChecked == true;
            BiomeCycleMode = ReadEnumSelection(BiomeCycleComboBox, SubnauticaBiomeTrackerCycleMode.Databanks);
            BiomeScrollSpeed = ReadEnumSelection(BiomeSpeedComboBox, SubnauticaBiomeTrackerScrollSpeed.Medium);
            DialogResult = true;
        }

        private void GamemodesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingGamemodes)
                return;

            GamemodeSelection = ReadEnumSelection(GamemodesDropdown, SpeedrunGamemode.SurvivalHardcore);
            LauncherSettings.Current.SpeedrunGamemode = GamemodeSelection;
            LauncherSettings.Save();
        }

        private void TrackerEnabledToggleButton_Click(object sender, RoutedEventArgs e)
        {
            TrackerEnabled = !TrackerEnabled;
            UpdateTrackerEnabledButton();
        }

        private void BiomeTrackerToggleButton_Click(object sender, RoutedEventArgs e)
        {
            BiomeTrackerEnabled = !BiomeTrackerEnabled;
            UpdateBiomeTrackerButton();
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

        private void UpdateTrackerEnabledButton()
        {
            TrackerEnabledToggleButton.Content = $"100% Tracker: {(TrackerEnabled ? "Enabled" : "Disabled")}";
            TrackerEnabledToggleButton.Background = TrackerEnabled ? Brushes.Green : Brushes.DarkRed;
        }

        private void UpdateBiomeTrackerButton()
        {
            BiomeTrackerToggleButton.Content = $"Biome Tracker: {(BiomeTrackerEnabled ? "Enabled" : "Disabled")}";
            BiomeTrackerToggleButton.Background = BiomeTrackerEnabled ? Brushes.Green : Brushes.DarkRed;
        }
    }
}
