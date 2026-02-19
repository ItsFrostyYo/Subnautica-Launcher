using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;

namespace SubnauticaLauncher.UI
{
    public partial class SpeedrunTimerEditWindow : Window
    {
        private const string DefaultBackground = "Lifepod";
        private bool _syncing;

        private static readonly SpeedrunCategory[] CreativeBlockedCategories =
        {
            SpeedrunCategory.NoDamage,
            SpeedrunCategory.RequiredTools
        };

        public SpeedrunTimerEditWindow(
            bool timerEnabled,
            SpeedrunGamemode gamemodeSelection,
            SpeedrunCategory categorySelection,
            SpeedrunRunType runTypeSelection)
        {
            InitializeComponent();
            ApplyBackground();

            TimerEnabled = timerEnabled;
            UpdateTimerEnabledButton();

            _syncing = true;
            SelectComboByTag(GamemodesDropdown, gamemodeSelection.ToString(), fallbackIndex: 0);
            SelectComboByTag(CategoryDropdown, categorySelection.ToString(), fallbackIndex: 0);
            SelectComboByTag(RunTypeDropdown, runTypeSelection.ToString(), fallbackIndex: 0);
            _syncing = false;

            ApplyCategoryRestrictions();
        }

        public bool TimerEnabled { get; private set; }
        public SpeedrunGamemode GamemodeSelection { get; private set; } = SpeedrunGamemode.SurvivalHardcore;
        public SpeedrunCategory CategorySelection { get; private set; } = SpeedrunCategory.AnyPercent;
        public SpeedrunRunType RunTypeSelection { get; private set; } = SpeedrunRunType.Glitched;

        private void TimerEnabledToggleButton_Click(object sender, RoutedEventArgs e)
        {
            TimerEnabled = !TimerEnabled;
            UpdateTimerEnabledButton();
        }

        private void GamemodesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;

            GamemodeSelection = ReadEnumSelection(GamemodesDropdown, SpeedrunGamemode.SurvivalHardcore);
            LauncherSettings.Current.SpeedrunGamemode = GamemodeSelection;
            LauncherSettings.Save();
            ApplyCategoryRestrictions();
        }

        private void CategoryDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing)
                return;

            CategorySelection = ReadEnumSelection(CategoryDropdown, SpeedrunCategory.AnyPercent);
            ApplyCategoryRestrictions();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            GamemodeSelection = ReadEnumSelection(GamemodesDropdown, SpeedrunGamemode.SurvivalHardcore);
            CategorySelection = ReadEnumSelection(CategoryDropdown, SpeedrunCategory.AnyPercent);
            RunTypeSelection = ReadEnumSelection(RunTypeDropdown, SpeedrunRunType.Glitched);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void EditTimerStyleAndPlacement_Click(object sender, RoutedEventArgs e)
        {
            var window = new SpeedrunTimerStylePlacementWindow();
            window.Owner = this;
            window.ShowDialog();
        }

        private void ManageSplits_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Splits management is coming soon.",
                "Coming Soon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ApplyCategoryRestrictions()
        {
            SpeedrunCategory currentCategory = ReadEnumSelection(CategoryDropdown, SpeedrunCategory.AnyPercent);
            bool categoryBlocksCreative = Array.Exists(CreativeBlockedCategories, c => c == currentCategory);

            if (categoryBlocksCreative)
            {
                _syncing = true;
                SelectComboByTag(GamemodesDropdown, SpeedrunGamemode.SurvivalHardcore.ToString(), fallbackIndex: 0);
                GamemodeSelection = SpeedrunGamemode.SurvivalHardcore;
                _syncing = false;

                GamemodesDropdown.IsEnabled = false;
            }
            else
            {
                GamemodesDropdown.IsEnabled = true;
            }
        }

        private void UpdateTimerEnabledButton()
        {
            TimerEnabledToggleButton.Content = $"Speedrun Timer: {(TimerEnabled ? "Enabled" : "Disabled")}";
            TimerEnabledToggleButton.Background = TimerEnabled ? Brushes.Green : Brushes.DarkRed;
        }

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
    }
}
