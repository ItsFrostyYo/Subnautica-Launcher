using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            public required GameVersionInstallDefinition Definition { get; init; }
            public string Id => Definition.Id;
            public string DisplayName => Definition.DisplayName;
            public long ManifestId => Definition.ManifestId;
        }

        public AddVersionWindow()
        {
            InitializeComponent();
            Loaded += AddVersionWindow_Loaded;
            LoadVersionLists();
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

        private static InstallCandidate CreateCandidate(LauncherGame game, GameVersionInstallDefinition definition)
        {
            return new InstallCandidate
            {
                Game = game,
                Definition = definition
            };
        }

        private void LoadVersionLists()
        {
            IReadOnlyList<InstallCandidate> snItems = LauncherGameProfiles.Subnautica.InstallDefinitions
                .Select(v => CreateCandidate(LauncherGame.Subnautica, v))
                .ToList();

            IReadOnlyList<InstallCandidate> bzItems = LauncherGameProfiles.BelowZero.InstallDefinitions
                .Select(v => CreateCandidate(LauncherGame.BelowZero, v))
                .ToList();

            SnAvailableVersionsList.ItemsSource = snItems;
            BzAvailableVersionsList.ItemsSource = bzItems;
        }

        private void SnAvailableVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SnAvailableVersionsList.SelectedItem != null && BzAvailableVersionsList.SelectedItem != null)
                BzAvailableVersionsList.SelectedItem = null;
        }

        private void BzAvailableVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BzAvailableVersionsList.SelectedItem != null && SnAvailableVersionsList.SelectedItem != null)
                SnAvailableVersionsList.SelectedItem = null;
        }

        private InstallCandidate? GetSelectedCandidate()
        {
            if (SnAvailableVersionsList.SelectedItem is InstallCandidate sn)
                return sn;

            if (BzAvailableVersionsList.SelectedItem is InstallCandidate bz)
                return bz;

            return null;
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

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var candidate = GetSelectedCandidate();
            if (candidate == null)
                return;

            DepotInstallAuthOptions? authOptions = DepotDownloaderLoginWindow.PromptForAuth(this);
            if (authOptions == null)
                return;

            try
            {
                InstallButton.IsEnabled = false;
                using IDisposable busyOperation = LauncherBusyCoordinator.Begin($"Install {candidate.Id}");

                string installDir = Path.Combine(authOptions.InstallCommonPath, candidate.Id);
                bool installDirExistedBefore = Directory.Exists(installDir);

                Func<DepotInstallCallbacks, CancellationToken, Task> installAction =
                    (callbacks, cancellationToken) =>
                        GameDepotDownloaderService.InstallVersionAsync(
                            candidate.Game,
                            candidate.Definition,
                            authOptions,
                            installDir,
                            callbacks,
                            cancellationToken);

                var installWindow = new DepotDownloaderInstallWindow(
                    candidate.DisplayName,
                    installAction);

                bool? installResult = DialogWindowHelper.ShowDialog(this, installWindow);
                if (installResult != true)
                {
                    if (installWindow.WasCancelled)
                        TryDeleteCancelledInstallFolder(installDir, installDirExistedBefore);

                    return;
                }

                LauncherSettings.Current.DepotDownloaderRememberedLoginSeeded = true;
                LauncherSettings.Current.DepotDownloaderRememberPassword = true;
                LauncherSettings.Save();

                MessageBox.Show(
                    "Installation complete.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogWindowHelper.Finish(this, true);
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
            var win = new AddUnmanagedVersionWindow();

            if (DialogWindowHelper.ShowDialog(this, win) == true)
            {
                DialogWindowHelper.Finish(this, true);
            }
        }

        private void InstallMods_Click(object sender, RoutedEventArgs e)
        {
            var win = new InstallModsWindow();

            if (DialogWindowHelper.ShowDialog(this, win) == true)
                DialogWindowHelper.Finish(this, true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogWindowHelper.Finish(this, false);
        }

        private static void TryDeleteCancelledInstallFolder(string installDir, bool existedBeforeInstall)
        {
            if (existedBeforeInstall)
                return;

            try
            {
                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
