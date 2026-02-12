using Microsoft.Win32;
using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Versions;
using System;
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
    public partial class AddUnmanagedVersionWindow : Window
    {
        private const int MaxDisplayNameLength = 25;
        private const string DefaultBg = "Lifepod";

        private LauncherGame _selectedGame;

        public AddUnmanagedVersionWindow(LauncherGame initialGame = LauncherGame.Subnautica)
        {
            InitializeComponent();

            _selectedGame = initialGame;

            Loaded += AddUnmanagedVersionWindow_Loaded;
            GameDropdown.SelectedIndex = initialGame == LauncherGame.Subnautica ? 0 : 1;

            UpdateGameUi();
            LoadOriginalDownloads();
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private void AddUnmanagedVersionWindow_Loaded(object sender, RoutedEventArgs e)
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

        private void GameDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedGame = GetSelectedGameFromDropdown();
            UpdateGameUi();
            LoadOriginalDownloads();
            ClearForm();
        }

        private LauncherGame GetSelectedGameFromDropdown()
        {
            if (GameDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<LauncherGame>(tag, out var game))
            {
                return game;
            }

            return LauncherGame.Subnautica;
        }

        private void UpdateGameUi()
        {
            bool isSubnautica = _selectedGame == LauncherGame.Subnautica;

            HeaderTextBlock.Text = isSubnautica
                ? "Add Existing Subnautica Version"
                : "Add Existing Below Zero Version";

            FolderRequirementTextBlock.Text = isSubnautica
                ? "Folder (Must Contain Subnautica.exe)"
                : "Folder (Must Contain SubnauticaZero.exe)";
        }

        private void LoadOriginalDownloads()
        {
            OriginalDownloadBox.ItemsSource = _selectedGame == LauncherGame.Subnautica
                ? VersionRegistry.AllVersions.Select(v => v.Id).ToList()
                : BZVersionRegistry.AllVersions.Select(v => v.Id).ToList();

            OriginalDownloadBox.SelectedIndex = 0;
        }

        private void ClearForm()
        {
            FolderPathBox.Text = string.Empty;
            FolderNameBox.Text = string.Empty;
            DisplayNameBox.Text = string.Empty;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = _selectedGame == LauncherGame.Subnautica
                    ? "Select Subnautica folder"
                    : "Select Below Zero folder",
                InitialDirectory = AppPaths.SteamCommonPath,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder"
            };

            if (dialog.ShowDialog() != true)
                return;

            string folder = Path.GetDirectoryName(dialog.FileName)!;
            ValidateFolder(folder);
        }

        private void ValidateFolder(string folder)
        {
            string folderName = Path.GetFileName(folder);

            if (IsReservedActiveFolderName(folderName))
            {
                MessageBox.Show(
                    "Folder name cannot be 'Subnautica' or 'SubnauticaZero'.\n\n" +
                    "Those names are reserved for active game folders.",
                    "Invalid Folder Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string requiredExe = _selectedGame == LauncherGame.Subnautica
                ? "Subnautica.exe"
                : "SubnauticaZero.exe";

            if (!File.Exists(Path.Combine(folder, requiredExe)))
            {
                MessageBox.Show(
                    $"Selected folder does not contain {requiredExe}",
                    "Invalid Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string infoFileName = _selectedGame == LauncherGame.Subnautica
                ? "Version.info"
                : "BZVersion.info";

            if (File.Exists(Path.Combine(folder, infoFileName)))
            {
                MessageBox.Show(
                    "This version is already managed by the launcher.",
                    "Already Managed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            FolderPathBox.Text = folder;
            FolderNameBox.Text = folderName;
            DisplayNameBox.Text = folderName.Length <= MaxDisplayNameLength
                ? folderName
                : folderName.Substring(0, MaxDisplayNameLength);
        }

        private static bool IsReservedActiveFolderName(string folderName)
        {
            return string.Equals(folderName, "Subnautica", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(folderName, "SubnauticaZero", StringComparison.OrdinalIgnoreCase);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathBox.Text))
            {
                MessageBox.Show("Please select a folder.");
                return;
            }

            string displayName = DisplayNameBox.Text.Trim();
            string folderName = FolderNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(folderName))
            {
                MessageBox.Show("Display name and folder name are required.");
                return;
            }

            if (displayName.Length > MaxDisplayNameLength)
            {
                MessageBox.Show(
                    $"Display name must be {MaxDisplayNameLength} characters or fewer.",
                    "Invalid Display Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (IsReservedActiveFolderName(folderName))
            {
                MessageBox.Show(
                    "Folder name cannot be 'Subnautica' or 'SubnauticaZero'.",
                    "Invalid Folder Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string infoFileName = _selectedGame == LauncherGame.Subnautica
                ? "Version.info"
                : "BZVersion.info";

            string infoPath = Path.Combine(FolderPathBox.Text, infoFileName);

            File.WriteAllLines(infoPath, new[]
            {
                $"DisplayName={displayName}",
                $"FolderName={folderName}",
                $"OriginalDownload={OriginalDownloadBox.SelectedItem}"
            });

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
