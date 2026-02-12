using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI
{
    public partial class AddVersionWindow : Window
    {
        private const string DefaultBg = "Lifepod";

        private sealed class InstallCandidate
        {
            public required LauncherGame Game { get; init; }
            public required string Id { get; init; }
            public required string DisplayName { get; init; }
            public required long ManifestId { get; init; }
        }

        public AddVersionWindow()
        {
            InitializeComponent();
            Loaded += AddVersionWindow_Loaded;

            GameDropdown.SelectedIndex = 0;
            RefreshAvailableVersions();
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private void AddVersionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Load();
            string bg = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            ApplyBackground(bg);
        }

        private void ApplyBackground(string preset)
        {
            try
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();

                if (File.Exists(preset))
                {
                    img.UriSource = new Uri(preset, UriKind.Absolute);
                }
                else
                {
                    img.UriSource = new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{preset}.png",
                        UriKind.Absolute);
                }

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

        private static InstallCandidate CreateCandidate(LauncherGame game, string id, string displayName, long manifestId)
        {
            return new InstallCandidate
            {
                Game = game,
                Id = id,
                DisplayName = displayName,
                ManifestId = manifestId
            };
        }

        private void RefreshAvailableVersions()
        {
            LauncherGame game = GetSelectedGame();

            IReadOnlyList<InstallCandidate> items = game == LauncherGame.Subnautica
                ? VersionRegistry.AllVersions
                    .Select(v => CreateCandidate(LauncherGame.Subnautica, v.Id, v.DisplayName, v.ManifestId))
                    .ToList()
                : BZVersionRegistry.AllVersions
                    .Select(v => CreateCandidate(LauncherGame.BelowZero, v.Id, v.DisplayName, v.ManifestId))
                    .ToList();

            AvailableVersionsList.ItemsSource = items;
            AvailableVersionsList.DisplayMemberPath = nameof(InstallCandidate.DisplayName);
        }

        private LauncherGame GetSelectedGame()
        {
            if (GameDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<LauncherGame>(tag, out var game))
            {
                return game;
            }

            return LauncherGame.Subnautica;
        }

        private void GameDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshAvailableVersions();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableVersionsList.SelectedItem is not InstallCandidate candidate)
                return;

            var login = new DepotDownloaderLoginWindow { Owner = this };
            bool? result = login.ShowDialog();

            if (result != true)
                return;

            try
            {
                InstallButton.IsEnabled = false;

                string installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Steam",
                    "steamapps",
                    "common",
                    candidate.Id);

                if (candidate.Game == LauncherGame.Subnautica)
                {
                    var version = new VersionInstallDefinition(
                        candidate.Id,
                        candidate.DisplayName,
                        candidate.ManifestId);

                    await SubnauticaLauncher.Installer.BZDepotDownloaderService.InstallVersionAsync(
                        version,
                        login.Username,
                        login.Password,
                        installDir);
                }
                else
                {
                    var version = new BZVersionInstallDefinition(
                        candidate.Id,
                        candidate.DisplayName,
                        candidate.ManifestId);

                    await SubnauticaLauncher.BelowZero.BZDepotDownloaderService.BZInstallVersionAsync(
                        version,
                        login.Username,
                        login.Password,
                        installDir);
                }

                MessageBox.Show(
                    "Installation complete.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Install Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                InstallButton.IsEnabled = true;
            }
        }

        private void AddUnmanaged_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddUnmanagedVersionWindow(GetSelectedGame())
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
            {
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
