using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Mods;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Core;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI
{
    public partial class VersionModsWindow : Window
    {
        private const string DefaultBg = "GrassyPlateau";

        private readonly InstalledVersion _version;
        private readonly LauncherGame _game;

        public VersionModsWindow(InstalledVersion version, LauncherGame game)
        {
            InitializeComponent();
            _version = version;
            _game = game;
            Loaded += VersionModsWindow_Loaded;
        }

        private void VersionModsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Load();
            string bg = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            ApplyBackground(bg);
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_version.HasBepInEx)
            {
                if (_version.HasDetectedPlugins)
                {
                    StatusText.Text = "Mods Installed";
                    string pluginList = string.Join(Environment.NewLine, _version.DetectedModNames.Select(name => $"• {name}"));
                    DescriptionText.Text =
                        $"Detected plugin(s):{Environment.NewLine}{pluginList}{Environment.NewLine}{Environment.NewLine}" +
                        "Removing mods will delete BepInEx and the installed plugin files, leaving the game as a clean launcher version.";
                    RemoveModsButton.Content = "Remove Mods";
                }
                else
                {
                    StatusText.Text = "BepInEx Installed";
                    DescriptionText.Text =
                        "BepInEx is installed, but no plugin DLLs were detected in the plugins folder." +
                        $"{Environment.NewLine}{Environment.NewLine}You can still remove BepInEx if you want this version clean again.";
                    RemoveModsButton.Content = "Remove BepInEx";
                }

                RemoveModsButton.IsEnabled = true;
                RemoveModsButton.Opacity = 1;
            }
            else
            {
                StatusText.Text = "No Mods Installed";
                DescriptionText.Text = "This version is currently vanilla. There are no launcher-managed mods to remove.";
                RemoveModsButton.Content = "Remove Mods";
                RemoveModsButton.IsEnabled = false;
                RemoveModsButton.Opacity = 0.55;
            }
        }

        private ImageBrush GetBackgroundBrush() => (ImageBrush)Resources["BackgroundBrush"];

        private void ApplyBackground(string preset)
        {
            try
            {
                BitmapImage img = new();
                img.BeginInit();
                img.UriSource = File.Exists(preset)
                    ? new Uri(preset, UriKind.Absolute)
                    : new Uri($"pack://application:,,,/Assets/Backgrounds/{preset}.png", UriKind.Absolute);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                GetBackgroundBrush().ImageSource = img;
            }
            catch
            {
                GetBackgroundBrush().ImageSource = new BitmapImage(new Uri(
                    $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                    UriKind.Absolute));
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogWindowHelper.Finish(this, false);

        private void RemoveMods_Click(object sender, RoutedEventArgs e)
        {
            if (!_version.HasBepInEx)
                return;

            LauncherGameProfile profile = LauncherGameProfiles.Get(_game);
            GameProcessMonitor.RefreshNow();
            if (GameProcessMonitor.GetSnapshot().Get(profile.ProcessName).IsRunning)
            {
                MessageBox.Show(
                    "Close the game before removing mods from this version.",
                    "Remove Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                ModInstallerService.RemoveManagedMod(_version, _game);
                InstalledVersionStore.Save(_game, _version);

                DialogWindowHelper.Finish(this, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Remove Mods Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
