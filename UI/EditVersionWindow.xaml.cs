using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Versions;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI
{
    public partial class EditVersionWindow : Window
    {
        private const string DefaultBg = "GrassyPlateau";

        private readonly InstalledVersion? _snVersion;
        private readonly BZInstalledVersion? _bzVersion;

        private bool IsBelowZero => _bzVersion != null;

        public EditVersionWindow(InstalledVersion version)
        {
            InitializeComponent();

            _snVersion = version;
            TitleBarText.Text = $"Editing Subnautica Version \"{version.DisplayLabel}\"";
            DisplayNameBox.Text = InstalledVersionNaming.NormalizeSavedDisplayName(version.DisplayName);
            FolderNameBox.Text = version.FolderName;

            Loaded += EditVersionWindow_Loaded;
        }

        public EditVersionWindow(BZInstalledVersion version)
        {
            InitializeComponent();

            _bzVersion = version;
            TitleBarText.Text = $"Editing Below Zero Version \"{version.DisplayLabel}\"";
            DisplayNameBox.Text = InstalledVersionNaming.NormalizeSavedDisplayName(version.DisplayName);
            FolderNameBox.Text = version.FolderName;

            Loaded += EditVersionWindow_Loaded;
        }

        private string HomeFolder => IsBelowZero ? _bzVersion!.HomeFolder : _snVersion!.HomeFolder;

        private string CurrentDisplayName => IsBelowZero ? _bzVersion!.DisplayName : _snVersion!.DisplayName;

        private string CurrentFolderName => IsBelowZero ? _bzVersion!.FolderName : _snVersion!.FolderName;

        private void EditVersionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Load();
            string bg = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            ApplyBackground(bg);
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
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
                GetBackgroundBrush().ImageSource =
                    new BitmapImage(new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                        UriKind.Absolute));
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void SaveRename_Click(object sender, RoutedEventArgs e)
        {
            if (IsGameCurrentlyRunning())
            {
                MessageBox.Show(
                    "Cannot edit while this game is running.\n\nClose the game and try again.",
                    "Edit Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string newDisplay = InstalledVersionNaming.NormalizeSavedDisplayName(DisplayNameBox.Text);
            string newFolder = FolderNameBox.Text.Trim();
            DisplayNameBox.Text = newDisplay;

            if (string.IsNullOrWhiteSpace(newDisplay) || string.IsNullOrWhiteSpace(newFolder))
            {
                MessageBox.Show(
                    "Display name and folder name are required.",
                    "Invalid Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (newDisplay.Length > InstalledVersionNaming.MaxDisplayNameLength)
            {
                MessageBox.Show(
                    $"Display name must be {InstalledVersionNaming.MaxDisplayNameLength} characters or fewer.",
                    "Invalid Display Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (IsReservedActiveFolderName(newFolder))
            {
                MessageBox.Show(
                    "Folder name cannot be 'Subnautica' or 'SubnauticaZero'.\n\n" +
                    "Those names are reserved for active game folders.",
                    "Invalid Folder Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool displayChanged = !string.Equals(newDisplay, CurrentDisplayName, StringComparison.Ordinal);
            bool folderChanged = !string.Equals(newFolder, CurrentFolderName, StringComparison.Ordinal);

            if (!displayChanged && !folderChanged)
            {
                DialogWindowHelper.Finish(this, false);
                return;
            }

            if (folderChanged)
            {
                string commonPath = AppPaths.GetSteamCommonPathFor(HomeFolder);
                string newPath = Path.Combine(commonPath, newFolder);

                if (Directory.Exists(newPath))
                {
                    MessageBox.Show(
                        "A folder with that name already exists.",
                        "Rename Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                await LaunchCoordinator.MoveFolderWithRetryAsync(HomeFolder, newPath);

                if (IsBelowZero)
                {
                    _bzVersion!.HomeFolder = newPath;
                    _bzVersion.FolderName = newFolder;
                }
                else
                {
                    _snVersion!.HomeFolder = newPath;
                    _snVersion.FolderName = newFolder;
                }
            }

            if (displayChanged)
            {
                if (IsBelowZero)
                    _bzVersion!.DisplayName = newDisplay;
                else
                    _snVersion!.DisplayName = newDisplay;
            }

            if (IsBelowZero)
                BZVersionLoader.Save(_bzVersion!);
            else
                VersionLoader.Save(_snVersion!);

            DialogWindowHelper.Finish(this, true);
        }

        private static bool IsReservedActiveFolderName(string folderName)
        {
            return string.Equals(folderName, "Subnautica", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(folderName, "SubnauticaZero", StringComparison.OrdinalIgnoreCase);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (IsGameCurrentlyRunning())
            {
                MessageBox.Show(
                    "Cannot delete while this game is running.\n\nClose the game and try again.",
                    "Delete Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (IsBelowZero)
                {
                    var dialog = new DeleteVersionDialog();
                    DialogWindowHelper.ShowDialog(this, dialog);

                    switch (dialog.Choice)
                    {
                        case DeleteChoice.Cancel:
                            return;

                        case DeleteChoice.RemoveFromLauncher:
                            DeleteLauncherInfoFiles(_bzVersion!.HomeFolder);
                            break;

                        case DeleteChoice.DeleteGame:
                            Directory.Delete(_bzVersion!.HomeFolder, true);
                            break;
                    }
                }
                else
                {
                    var dialog = new DeleteVersionDialog();
                    DialogWindowHelper.ShowDialog(this, dialog);

                    switch (dialog.Choice)
                    {
                        case DeleteChoice.Cancel:
                            return;

                        case DeleteChoice.RemoveFromLauncher:
                            DeleteLauncherInfoFiles(_snVersion!.HomeFolder);
                            break;

                        case DeleteChoice.DeleteGame:
                            Directory.Delete(_snVersion!.HomeFolder, true);
                            break;
                    }
                }

                DialogWindowHelper.Finish(this, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void DeleteLauncherInfoFiles(string homeFolder)
        {
            foreach (string fileName in new[] { "Version.info", "BZVersion.info" })
            {
                string path = Path.Combine(homeFolder, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private void Mods_Click(object sender, RoutedEventArgs e)
        {
            InstalledVersion version = IsBelowZero ? _bzVersion! : _snVersion!;
            LauncherGame game = IsBelowZero ? LauncherGame.BelowZero : LauncherGame.Subnautica;
            var win = new VersionModsWindow(version, game);

            if (DialogWindowHelper.ShowDialog(this, win) == true)
                DialogWindowHelper.Finish(this, true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogWindowHelper.Finish(this, false);
        }

        private bool IsGameCurrentlyRunning()
        {
            string processName = IsBelowZero ? "SubnauticaZero" : "Subnautica";
            GameProcessMonitor.RefreshNow();
            return GameProcessMonitor.GetSnapshot().Get(processName).IsRunning;
        }
    }
}
