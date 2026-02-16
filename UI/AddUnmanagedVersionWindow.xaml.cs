using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Versions;
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
    public partial class AddUnmanagedVersionWindow : Window
    {
        private const int MaxDisplayNameLength = 25;
        private const string DefaultBg = "Lifepod";

        private LauncherGame? _detectedGame;

        public AddUnmanagedVersionWindow()
        {
            InitializeComponent();
            Loaded += AddUnmanagedVersionWindow_Loaded;
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

        private void LoadOriginalDownloads()
        {
            if (_detectedGame == LauncherGame.Subnautica)
            {
                OriginalDownloadBox.ItemsSource = VersionRegistry.AllVersions
                    .Select(v => v.Id)
                    .ToList();
            }
            else if (_detectedGame == LauncherGame.BelowZero)
            {
                OriginalDownloadBox.ItemsSource = BZVersionRegistry.AllVersions
                    .Select(v => v.Id)
                    .ToList();
            }
            else
            {
                OriginalDownloadBox.ItemsSource = Array.Empty<string>();
            }

            OriginalDownloadBox.SelectedIndex = OriginalDownloadBox.Items.Count > 0 ? 0 : -1;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select game folder",
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

            bool hasSubnauticaExe = File.Exists(Path.Combine(folder, "Subnautica.exe"));
            bool hasBelowZeroExe = File.Exists(Path.Combine(folder, "SubnauticaZero.exe"));

            if (!hasSubnauticaExe && !hasBelowZeroExe)
            {
                MessageBox.Show(
                    "Selected folder does not contain a supported game executable.",
                    "Invalid Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (hasSubnauticaExe && hasBelowZeroExe)
            {
                MessageBox.Show(
                    "Selected folder contains both Subnautica.exe and SubnauticaZero.exe.\n\n" +
                    "Please choose a folder for one game only.",
                    "Invalid Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _detectedGame = hasSubnauticaExe ? LauncherGame.Subnautica : LauncherGame.BelowZero;

            if (File.Exists(Path.Combine(folder, "Version.info")) ||
                File.Exists(Path.Combine(folder, "BZVersion.info")))
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

            LoadOriginalDownloads();
        }

        private static bool IsReservedActiveFolderName(string folderName)
        {
            return string.Equals(folderName, "Subnautica", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(folderName, "SubnauticaZero", StringComparison.OrdinalIgnoreCase);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathBox.Text) || _detectedGame == null)
            {
                MessageBox.Show("Please select a valid game folder first.");
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

            if (OriginalDownloadBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an original version.");
                return;
            }

            string infoFileName = _detectedGame == LauncherGame.Subnautica
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
