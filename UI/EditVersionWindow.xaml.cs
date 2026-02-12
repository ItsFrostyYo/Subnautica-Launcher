using SubnauticaLauncher.BelowZero;
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
        private const int MaxDisplayNameLength = 25;
        private const string DefaultBg = "GrassyPlateau";

        private readonly InstalledVersion? _snVersion;
        private readonly BZInstalledVersion? _bzVersion;

        private bool IsBelowZero => _bzVersion != null;

        public EditVersionWindow(InstalledVersion version)
        {
            InitializeComponent();

            _snVersion = version;
            TitleBarText.Text = $"Editing Subnautica Version \"{version.DisplayLabel}\"";
            DisplayNameBox.Text = version.DisplayName;
            FolderNameBox.Text = version.FolderName;

            Loaded += EditVersionWindow_Loaded;
        }

        public EditVersionWindow(BZInstalledVersion version)
        {
            InitializeComponent();

            _bzVersion = version;
            TitleBarText.Text = $"Editing Below Zero Version \"{version.DisplayLabel}\"";
            DisplayNameBox.Text = version.DisplayName;
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

        private void SaveRename_Click(object sender, RoutedEventArgs e)
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

            string newDisplay = DisplayNameBox.Text.Trim();
            string newFolder = FolderNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newDisplay) || string.IsNullOrWhiteSpace(newFolder))
            {
                MessageBox.Show(
                    "Display name and folder name are required.",
                    "Invalid Input",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (newDisplay.Length > MaxDisplayNameLength)
            {
                MessageBox.Show(
                    $"Display name must be {MaxDisplayNameLength} characters or fewer.",
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
                DialogResult = false;
                Close();
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

                Directory.Move(HomeFolder, newPath);

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

            DialogResult = true;
            Close();
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
                    var dialog = new BZDeleteVersionDialog { Owner = this };
                    dialog.ShowDialog();

                    switch (dialog.Choice)
                    {
                        case BZDeleteChoice.Cancel:
                            return;

                        case BZDeleteChoice.RemoveFromLauncher:
                            string bzInfoPath = Path.Combine(_bzVersion!.HomeFolder, "BZVersion.info");
                            if (File.Exists(bzInfoPath))
                                File.Delete(bzInfoPath);
                            break;

                        case BZDeleteChoice.DeleteGame:
                            Directory.Delete(_bzVersion!.HomeFolder, true);
                            break;
                    }
                }
                else
                {
                    var dialog = new DeleteVersionDialog { Owner = this };
                    dialog.ShowDialog();

                    switch (dialog.Choice)
                    {
                        case DeleteChoice.Cancel:
                            return;

                        case DeleteChoice.RemoveFromLauncher:
                            string infoPath = Path.Combine(_snVersion!.HomeFolder, "Version.info");
                            if (File.Exists(infoPath))
                                File.Delete(infoPath);
                            break;

                        case DeleteChoice.DeleteGame:
                            Directory.Delete(_snVersion!.HomeFolder, true);
                            break;
                    }
                }

                DialogResult = true;
                Close();
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool IsGameCurrentlyRunning()
        {
            string processName = IsBelowZero ? "SubnauticaZero" : "Subnautica";
            return Process.GetProcessesByName(processName).Length > 0;
        }
    }
}
