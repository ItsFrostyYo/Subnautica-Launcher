using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Mods;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI
{
    public partial class InstallModsWindow : Window
    {
        private const string DefaultBg = "Lifepod";

        private sealed class InstallCandidate
        {
            public required LauncherGame Game { get; init; }
            public required string Id { get; init; }
            public required string DisplayName { get; init; }
            public required long ManifestId { get; init; }
            public required int SteamAppId { get; init; }
            public required int SteamDepotId { get; init; }
        }

        private readonly List<InstallCandidate> _subnauticaCandidates;
        private readonly List<InstallCandidate> _belowZeroCandidates;
        private List<InstalledVersion> _subnauticaInstalled = new();
        private List<BZInstalledVersion> _belowZeroInstalled = new();
        private bool _dataLoaded;

        public InstallModsWindow()
        {
            InitializeComponent();
            Loaded += InstallModsWindow_Loaded;

            _subnauticaCandidates = LauncherGameProfiles.Subnautica.InstallDefinitions
                .Select(v => new InstallCandidate
                {
                    Game = LauncherGame.Subnautica,
                    Id = v.Id,
                    DisplayName = v.DisplayName,
                    ManifestId = v.ManifestId,
                    SteamAppId = v.SteamAppId,
                    SteamDepotId = v.SteamDepotId
                })
                .ToList();

            _belowZeroCandidates = LauncherGameProfiles.BelowZero.InstallDefinitions
                .Select(v => new InstallCandidate
                {
                    Game = LauncherGame.BelowZero,
                    Id = v.Id,
                    DisplayName = v.DisplayName,
                    ManifestId = v.ManifestId,
                    SteamAppId = v.SteamAppId,
                    SteamDepotId = v.SteamDepotId
                })
                .ToList();

            LoadGameSelectors();
            RefreshLists();
        }

        private async void InstallModsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Load();
            string bg = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            ApplyBackground(bg);

            await LoadRuntimeDataAsync();
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

        private void LoadGameSelectors()
        {
            NewGameComboBox.ItemsSource = BuildGameChoices();
            NewGameComboBox.SelectedIndex = 0;

            ExistingGameComboBox.ItemsSource = BuildGameChoices();
            ExistingGameComboBox.SelectedIndex = 0;
        }

        private static IReadOnlyList<System.Windows.Controls.ComboBoxItem> BuildGameChoices()
        {
            return new[]
            {
                CreateComboItem("Subnautica", LauncherGame.Subnautica),
                CreateComboItem("Below Zero", LauncherGame.BelowZero)
            };
        }

        private void RefreshLists()
        {
            LauncherGame newGame = GetSelectedGame(NewGameComboBox);
            NewVersionsList.ItemsSource = newGame == LauncherGame.Subnautica
                ? _subnauticaCandidates
                : _belowZeroCandidates;
            NewVersionsList.SelectedIndex = NewVersionsList.Items.Count > 0 ? 0 : -1;
            RefreshNewModChoices();

            LauncherGame existingGame = GetSelectedGame(ExistingGameComboBox);
            ExistingVersionsList.ItemsSource = existingGame == LauncherGame.Subnautica
                ? _subnauticaInstalled.Cast<InstalledVersion>().ToList()
                : _belowZeroInstalled.Cast<InstalledVersion>().ToList();
            ExistingVersionsList.SelectedIndex = ExistingVersionsList.Items.Count > 0 ? 0 : -1;
            RefreshExistingModChoices();
        }

        private static LauncherGame GetSelectedGame(System.Windows.Controls.ComboBox comboBox)
        {
            return comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                   item.Tag is LauncherGame game
                ? game
                : LauncherGame.Subnautica;
        }

        private void RefreshNewModChoices()
        {
            InstallCandidate? candidate = NewVersionsList.SelectedItem as InstallCandidate;
            LauncherGame game = GetSelectedGame(NewGameComboBox);

            IReadOnlyList<ModDefinition> mods = candidate == null
                ? Array.Empty<ModDefinition>()
                : ModInstallerService.GetAvailableModsForVersion(game, candidate.Id, candidate.DisplayName, candidate.Id);

            NewModComboBox.ItemsSource = BuildModChoices(mods);
            NewModComboBox.SelectedIndex = 0;
            UpdateButtonStates();
        }

        private void RefreshExistingModChoices()
        {
            if (ExistingVersionsList.SelectedItem is not InstalledVersion version)
            {
                ExistingModComboBox.ItemsSource = BuildModChoices(Array.Empty<ModDefinition>());
            }
            else
            {
                LauncherGame game = GetSelectedGame(ExistingGameComboBox);
                IReadOnlyList<ModDefinition> availableMods = ModInstallerService.GetAvailableModsForVersion(
                    game,
                    version.OriginalDownload,
                    version.DisplayName,
                    version.FolderName);
                IReadOnlyList<ModDefinition> installableMods = ModInstallerService.GetInstallableModsForVersion(game, version);
                ExistingModComboBox.ItemsSource = BuildExistingModChoices(availableMods, installableMods);
            }

            ExistingModComboBox.SelectedIndex = 0;
            UpdateButtonStates();
        }

        private static IReadOnlyList<System.Windows.Controls.ComboBoxItem> BuildModChoices(IReadOnlyList<ModDefinition> mods)
        {
            if (mods.Count == 0)
            {
                return new[]
                {
                    CreateComboItem("No Mods Available", string.Empty, isPlaceholder: true)
                };
            }

            return mods
                .Select(mod => CreateComboItem(mod.DisplayName, mod.Id, isPlaceholder: false))
                .ToList();
        }

        private static IReadOnlyList<System.Windows.Controls.ComboBoxItem> BuildExistingModChoices(
            IReadOnlyList<ModDefinition> availableMods,
            IReadOnlyList<ModDefinition> installableMods)
        {
            if (installableMods.Count > 0)
                return BuildModChoices(installableMods);

            if (availableMods.Count == 0)
            {
                return new[]
                {
                    CreateComboItem("No Mods Available", string.Empty, isPlaceholder: true)
                };
            }

            return new[]
            {
                CreateComboItem("All Allowed Mods Installed", string.Empty, isPlaceholder: true)
            };
        }

        private void UpdateButtonStates()
        {
            if (!_dataLoaded)
            {
                InstallNewModdedButton.IsEnabled = false;
                InstallExistingModButton.IsEnabled = false;
                return;
            }

            bool newModSelected = TryGetSelectedModId(NewModComboBox, out _, out bool newPlaceholder) && !newPlaceholder;
            InstallNewModdedButton.IsEnabled = NewVersionsList.SelectedItem is InstallCandidate && newModSelected;

            bool existingModSelected = TryGetSelectedModId(ExistingModComboBox, out _, out bool existingPlaceholder) && !existingPlaceholder;
            bool existingVersionValid = ExistingVersionsList.SelectedItem is InstalledVersion;
            InstallExistingModButton.IsEnabled = existingVersionValid && existingModSelected;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogWindowHelper.Finish(this, false);

        private void NewGameComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshLists();

        private void ExistingGameComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshLists();

        private void NewVersionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshNewModChoices();

        private void ExistingVersionsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshExistingModChoices();

        private void NewModComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtonStates();

        private void ExistingModComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtonStates();

        private void InstallNewModded_Click(object sender, RoutedEventArgs e)
        {
            if (NewVersionsList.SelectedItem is not InstallCandidate candidate ||
                !TryGetSelectedModId(NewModComboBox, out string? modId, out bool isPlaceholder) ||
                isPlaceholder)
            {
                return;
            }

            ModDefinition? mod = ModCatalog.GetById(modId);
            if (mod == null)
                return;

            var login = new DepotDownloaderLoginWindow();
            if (DialogWindowHelper.ShowDialog(this, login) != true || login.AuthOptions == null)
                return;

            try
            {
                InstallNewModdedButton.IsEnabled = false;

                (string folderName, string displayName, string installDir) = BuildUniqueModdedInstallLocation(candidate);
                var installVersion = new GameVersionInstallDefinition(
                    folderName,
                    displayName,
                    candidate.ManifestId,
                    candidate.SteamAppId,
                    candidate.SteamDepotId);

                Func<DepotInstallCallbacks, CancellationToken, Task> installAction = async (callbacks, cancellationToken) =>
                {
                    await GameDepotDownloaderService.InstallVersionAsync(
                        candidate.Game,
                        installVersion,
                        login.AuthOptions,
                        installDir,
                        callbacks,
                        cancellationToken);

                    callbacks?.OnStatus?.Invoke($"Applying {mod.DisplayName}...");
                    await ModInstallerService.InstallBundleAsync(mod, candidate.Game, installDir, callbacks, cancellationToken);
                    SaveInstalledModdedVersion(candidate.Game, installDir, folderName, displayName, candidate.Id, mod.Id);
                };

                var installWindow = new DepotDownloaderInstallWindow(displayName, installAction);
                bool? result = DialogWindowHelper.ShowDialog(this, installWindow);
                if (result == true)
                {
                    LauncherSettings.Current.DepotDownloaderRememberedLoginSeeded = true;
                    LauncherSettings.Current.DepotDownloaderRememberPassword = true;
                    LauncherSettings.Save();

                    DialogWindowHelper.Finish(this, true);
                }
                else if (installWindow.WasCancelled && Directory.Exists(installDir))
                {
                    Directory.Delete(installDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mod Install Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                InstallNewModdedButton.IsEnabled = true;
            }
        }

        private void InstallExistingMod_Click(object sender, RoutedEventArgs e)
        {
            if (ExistingVersionsList.SelectedItem is not InstalledVersion version ||
                !TryGetSelectedModId(ExistingModComboBox, out string? modId, out bool isPlaceholder) ||
                isPlaceholder)
            {
                return;
            }

            LauncherGame game = GetSelectedGame(ExistingGameComboBox);
            LauncherGameProfile profile = LauncherGameProfiles.Get(game);
            GameProcessMonitor.RefreshNow();
            if (GameProcessMonitor.GetSnapshot().Get(profile.ProcessName).IsRunning)
            {
                MessageBox.Show(
                    "Close the game before installing mods into this version.",
                    "Install Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ModDefinition? mod = ModCatalog.GetById(modId);
            if (mod == null)
                return;

            try
            {
                InstallExistingModButton.IsEnabled = false;

                Func<DepotInstallCallbacks, CancellationToken, Task> installAction = async (callbacks, cancellationToken) =>
                {
                    await ModInstallerService.InstallBundleAsync(mod, game, version.HomeFolder, callbacks, cancellationToken);
                    version.IsModded = true;
                    version.InstalledModId = mod.Id;

                    InstalledVersionStore.Save(game, version);
                };

                var installWindow = new DepotDownloaderInstallWindow(version.DisplayName, installAction);
                if (DialogWindowHelper.ShowDialog(this, installWindow) == true)
                    DialogWindowHelper.Finish(this, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Mod Install Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                InstallExistingModButton.IsEnabled = true;
            }
        }

        private static (string FolderName, string DisplayName, string InstallDir) BuildUniqueModdedInstallLocation(InstallCandidate candidate)
        {
            string commonPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam",
                "steamapps",
                "common");
            string baseDisplayName = InstalledVersionNaming.BuildBaseDisplayName(candidate.Id, candidate.DisplayName);

            int instance = 1;
            while (true)
            {
                string folderName = ModInstallerService.BuildModdedFolderName(candidate.Id, instance);
                string displayName = ModInstallerService.BuildModdedDisplayName(baseDisplayName, instance);
                string installDir = Path.Combine(commonPath, folderName);

                if (!Directory.Exists(installDir))
                    return (folderName, displayName, installDir);

                instance++;
            }
        }

        private static void SaveInstalledModdedVersion(
            LauncherGame game,
            string installDir,
            string folderName,
            string displayName,
            string originalDownload,
            string modId)
        {
            if (game == LauncherGame.BelowZero)
            {
                InstalledVersionStore.Save(game, new BZInstalledVersion
                {
                    HomeFolder = installDir,
                    FolderName = folderName,
                    DisplayName = displayName,
                    OriginalDownload = originalDownload,
                    IsModded = true,
                    InstalledModId = modId
                });
                return;
            }

            InstalledVersionStore.Save(game, new InstalledVersion
            {
                HomeFolder = installDir,
                FolderName = folderName,
                DisplayName = displayName,
                OriginalDownload = originalDownload,
                IsModded = true,
                InstalledModId = modId
            });
        }

        private static System.Windows.Controls.ComboBoxItem CreateComboItem(string content, object tag, bool isPlaceholder = false)
        {
            return new System.Windows.Controls.ComboBoxItem
            {
                Content = content,
                Tag = tag,
                ToolTip = content,
                DataContext = isPlaceholder
            };
        }

        private static bool TryGetSelectedModId(System.Windows.Controls.ComboBox comboBox, out string? modId, out bool isPlaceholder)
        {
            if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                modId = item.Tag as string;
                isPlaceholder = item.DataContext is bool placeholder && placeholder;
                return true;
            }

            modId = null;
            isPlaceholder = true;
            return false;
        }

        private async Task LoadRuntimeDataAsync()
        {
            try
            {
                await ModCatalog.EnsureLoadedAsync();

                InstalledVersionScanSnapshot snapshot = await InstalledVersionScanService.ScanAsync();
                _subnauticaInstalled = snapshot.SubnauticaVersions.ToList();
                _belowZeroInstalled = snapshot.BelowZeroVersions.ToList();
                _dataLoaded = true;
                RefreshLists();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load mod install data.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "Load Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
