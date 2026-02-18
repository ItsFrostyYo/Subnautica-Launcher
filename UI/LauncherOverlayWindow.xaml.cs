using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UpdatesData = SubnauticaLauncher.Updates.Updates;

namespace SubnauticaLauncher.UI
{
    public partial class LauncherOverlayWindow : Window
    {
        private readonly MainWindow _main;
        private bool _capturingOverlayHotkey;
        private bool _capturingResetHotkey;
        private bool _syncingSelections;
        private bool _syncingStartupMode;
        private bool _syncingModes;
        private bool _syncingOpacity;
        private bool _syncingBackground;
        private bool _allowClose;
        private bool _updatesBuilt;

        public LauncherOverlayWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();

            PreviewKeyDown += LauncherOverlayWindow_PreviewKeyDown;
        }

        public void AllowClose()
        {
            _allowClose = true;
        }

        public void ApplyOverlaySizing()
        {
            Rect area = SystemParameters.WorkArea;
            Height = Math.Clamp(area.Height * 0.31, 320, 390);
            Width = area.Width;
            Left = area.Left;
            Top = area.Top;
        }

        public void ApplyOverlayOpacity(double value)
        {
            double clamped = Math.Clamp(value, 0, 1);
            byte alpha = (byte)Math.Round(clamped * 255d);

            _syncingOpacity = true;
            try
            {
                OverlayRoot.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
                OverlayTransparencySlider.Value = clamped;
                OverlayTransparencyText.Text = $"{(int)Math.Round(clamped * 100)}%";
            }
            finally
            {
                _syncingOpacity = false;
            }
        }

        public void RefreshFromMain()
        {
            OverlaySnVersionsList.ItemsSource = _main.GetSubnauticaVersionsForOverlay();
            OverlayBzVersionsList.ItemsSource = _main.GetBelowZeroVersionsForOverlay();

            RefreshVersionStatusOnly();
            SyncSelectedVersionsFromMain();

            _syncingStartupMode = true;
            try
            {
                OverlayStartupModeDropdown.SelectedIndex = _main.IsOverlayStartupModeForOverlay() ? 1 : 0;
            }
            finally
            {
                _syncingStartupMode = false;
            }

            OverlayHotkeyBox.Text = _main.GetOverlayHotkeyTextForOverlay();
            ApplyOverlayOpacity(_main.GetOverlayOpacityForOverlay());

            _syncingBackground = true;
            try
            {
                SelectBackgroundPreset(_main.GetBackgroundPresetForOverlay());
            }
            finally
            {
                _syncingBackground = false;
            }

            OverlayRenameOnCloseButton.Content =
                $"Allow Launching through Steam: {(_main.IsRenameOnCloseEnabledForOverlay() ? "Enabled" : "Disabled")}";
            OverlayRenameOnCloseButton.Background =
                _main.IsRenameOnCloseEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            OverlayExplosionDisplayButton.Content =
                $"Explosion Overlay: {(_main.IsExplosionOverlayEnabledForOverlay() ? "On" : "Off")}";
            OverlayExplosionDisplayButton.Background =
                _main.IsExplosionOverlayEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            OverlayExplosionTrackButton.Content =
                $"Track Resets: {(_main.IsExplosionTrackingEnabledForOverlay() ? "On" : "Off")}";
            OverlayExplosionTrackButton.Background =
                _main.IsExplosionTrackingEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            OverlayResetMacroButton.Content =
                $"Reset Macro: {(_main.IsResetMacroEnabledForOverlay() ? "Enabled" : "Disabled")}";
            OverlayResetMacroButton.Background =
                _main.IsResetMacroEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            OverlayResetHotkeyBox.Text = _main.GetResetHotkeyForOverlay().ToString();

            _syncingModes = true;
            try
            {
                SelectComboItemByContent(OverlayResetGamemodeDropdown, _main.GetResetGameModeForOverlay().ToString());
                SelectComboItemByTag(OverlayExplosionPresetDropdown, _main.GetExplosionPresetForOverlay().ToString());
                OverlayExplosionPresetDropdown.IsEnabled = _main.IsExplosionResetEnabledForOverlay();
            }
            finally
            {
                _syncingModes = false;
            }

            OverlayExplosionResetButton.Content =
                $"Reset Explosion: {(_main.IsExplosionResetEnabledForOverlay() ? "On" : "Off")}";
            OverlayExplosionResetButton.Background =
                _main.IsExplosionResetEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            OverlayHardcoreButton.Content =
                $"Hardcore Deleter: {(_main.IsHardcoreSaveDeleterEnabledForOverlay() ? "On" : "Off")}";
            OverlayHardcoreButton.Background =
                _main.IsHardcoreSaveDeleterEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            OverlayTrackerButton.Content =
                $"100% Tracker: {(_main.IsSubnauticaTrackerEnabledForOverlay() ? "Enabled" : "Disabled")}";
            OverlayTrackerButton.Background =
                _main.IsSubnauticaTrackerEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            if (!_updatesBuilt)
            {
                BuildUpdatesView();
                _updatesBuilt = true;
            }
        }

        public void RefreshVersionStatusOnly()
        {
            OverlaySnVersionsList.Items.Refresh();
            OverlayBzVersionsList.Items.Refresh();
        }

        public void SyncSelectedVersionsFromMain()
        {
            _syncingSelections = true;
            try
            {
                OverlaySnVersionsList.SelectedItem = _main.GetSelectedSubnauticaVersionForOverlay();
                OverlayBzVersionsList.SelectedItem = _main.GetSelectedBelowZeroVersionForOverlay();
            }
            finally
            {
                _syncingSelections = false;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_allowClose)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        private static void SelectComboItemByContent(ComboBox comboBox, string content)
        {
            comboBox.SelectedItem = comboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Content, content, StringComparison.Ordinal));

            if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private static void SelectComboItemByTag(ComboBox comboBox, string tag)
        {
            comboBox.SelectedItem = comboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Tag, tag, StringComparison.Ordinal));

            if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private void SelectBackgroundPreset(string preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
            {
                OverlayBackgroundDropdown.SelectedItem = null;
                return;
            }

            string target = preset;
            if (preset.Contains("\\") || preset.Contains("/") || preset.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                target = "Custom";

            OverlayBackgroundDropdown.SelectedItem = OverlayBackgroundDropdown.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Content, target, StringComparison.Ordinal));

            if (OverlayBackgroundDropdown.SelectedItem == null && OverlayBackgroundDropdown.Items.Count > 0)
                OverlayBackgroundDropdown.SelectedIndex = 0;
        }

        private void BuildUpdatesView()
        {
            OverlayUpdatesPanel.Children.Clear();

            foreach (UpdateEntry update in UpdatesData.History)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var panel = new StackPanel();
                panel.Children.Add(new TextBlock
                {
                    Text = $"{update.Version} ({update.Title})",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = update.Date,
                    FontSize = 11,
                    Foreground = Brushes.LightGray,
                    Margin = new Thickness(0, 2, 0, 4)
                });

                foreach (string change in update.Changes)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "- " + change,
                        FontSize = 11,
                        Foreground = Brushes.Gainsboro,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                border.Child = panel;
                OverlayUpdatesPanel.Children.Add(border);
            }
        }

        private void LauncherOverlayWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingOverlayHotkey && !_capturingResetHotkey)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftShift or Key.RightShift or
                Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or
                Key.LWin or Key.RWin)
            {
                return;
            }

            if (_capturingOverlayHotkey)
            {
                ModifierKeys modifiers = Keyboard.Modifiers;
                if (modifiers == ModifierKeys.None)
                    modifiers = ModifierKeys.Control | ModifierKeys.Shift;

                _main.SetOverlayHotkeyFromOverlay(modifiers, key);
                _capturingOverlayHotkey = false;
            }
            else if (_capturingResetHotkey)
            {
                _main.SetResetHotkeyFromOverlay(key);
                _capturingResetHotkey = false;
            }

            RefreshFromMain();
            e.Handled = true;
        }

        private void OverlaySnVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelections)
                return;

            _main.SetSelectedVersionsFromOverlay(OverlaySnVersionsList.SelectedItem as InstalledVersion, null);
            SyncSelectedVersionsFromMain();
        }

        private void OverlayBzVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelections)
                return;

            _main.SetSelectedVersionsFromOverlay(null, OverlayBzVersionsList.SelectedItem as BZInstalledVersion);
            SyncSelectedVersionsFromMain();
        }

        private void OverlayLaunchButton_Click(object sender, RoutedEventArgs e) => _main.LaunchSelectedFromOverlay();
        private void OverlayAddButton_Click(object sender, RoutedEventArgs e)
        {
            _main.AddVersionFromOverlay();
            RefreshFromMain();
        }

        private void OverlayEditButton_Click(object sender, RoutedEventArgs e)
        {
            _main.EditVersionFromOverlay();
            RefreshFromMain();
        }

        private void OverlayOpenFolderButton_Click(object sender, RoutedEventArgs e) => _main.OpenInstallFolderFromOverlay();
        private async void OverlayCloseGameButton_Click(object sender, RoutedEventArgs e)
        {
            await _main.CloseGameFromOverlayAsync();
            RefreshFromMain();
        }

        private void OverlayCloseOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ExitLauncherFromOverlay();
        }

        private void OverlayStartupModeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingStartupMode)
                return;

            _main.SetStartupModeFromOverlay(OverlayStartupModeDropdown.SelectedIndex == 1);
            RefreshFromMain();
        }

        private void OverlaySetHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _capturingOverlayHotkey = true;
            OverlayHotkeyBox.Text = "Press new combo...";
        }

        private void OverlayBackgroundDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingBackground)
                return;

            if (OverlayBackgroundDropdown.SelectedItem is ComboBoxItem item &&
                item.Content is string preset)
            {
                if (string.Equals(preset, "Custom", StringComparison.Ordinal))
                    return;

                _main.SetBackgroundPresetFromOverlay(preset);
                RefreshFromMain();
            }
        }

        private void OverlayChooseBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ChooseCustomBackgroundFromOverlay();
            RefreshFromMain();
        }

        private void OverlayTransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _syncingOpacity)
                return;

            _main.SetOverlayOpacityFromOverlay(e.NewValue);
        }

        private void OverlayRenameOnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleRenameOnCloseFromOverlay();
            RefreshFromMain();
        }

        private void OverlayExplosionDisplayButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleExplosionOverlayFromOverlay();
            RefreshFromMain();
        }

        private void OverlayExplosionTrackButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleExplosionTrackingFromOverlay();
            RefreshFromMain();
        }

        private void OverlayResetMacroButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleResetMacroFromOverlay();
            RefreshFromMain();
        }

        private void OverlaySetResetHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _capturingResetHotkey = true;
            OverlayResetHotkeyBox.Text = "Press a key...";
        }

        private void OverlayResetGamemodeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingModes)
                return;

            if (OverlayResetGamemodeDropdown.SelectedItem is ComboBoxItem item &&
                item.Content is string modeText &&
                Enum.TryParse(modeText, out GameMode mode))
            {
                _main.SetResetGameModeFromOverlay(mode);
            }

            RefreshFromMain();
        }

        private void OverlayExplosionPresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingModes)
                return;

            if (OverlayExplosionPresetDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, out ExplosionResetPreset preset))
            {
                _main.SetExplosionPresetFromOverlay(preset);
            }

            RefreshFromMain();
        }

        private void OverlayExplosionResetButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleExplosionResetFromOverlay();
            RefreshFromMain();
        }

        private void OverlayHardcoreButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleHardcoreSaveDeleterFromOverlay();
            RefreshFromMain();
        }

        private void OverlayTrackerButton_Click(object sender, RoutedEventArgs e)
        {
            _main.ToggleSubnauticaTrackerFromOverlay();
            RefreshFromMain();
        }

        private void OverlayTrackerCustomizeButton_Click(object sender, RoutedEventArgs e)
        {
            _main.OpenTrackerCustomizeFromOverlay();
            RefreshFromMain();
        }

        private void OverlayDeleteHardcoreButton_Click(object sender, RoutedEventArgs e)
        {
            _main.OpenHardcorePurgeFromOverlay();
            RefreshFromMain();
        }

        private void OverlayGitHubButton_Click(object sender, RoutedEventArgs e) => _main.OpenGitHubFromOverlay();
        private void OverlayYouTubeButton_Click(object sender, RoutedEventArgs e) => _main.OpenYouTubeFromOverlay();
        private void OverlayDiscordButton_Click(object sender, RoutedEventArgs e) => _main.OpenDiscordFromOverlay();
    }
}
