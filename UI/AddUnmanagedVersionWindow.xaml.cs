using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
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
        private const string DefaultBg = "Lifepod";

        private LauncherGame? _detectedGame;
        private string? _detectedOriginalDownloadId;

        public AddUnmanagedVersionWindow()
        {
            InitializeComponent();
            Loaded += AddUnmanagedVersionWindow_Loaded;
            AddButton.IsEnabled = false;
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

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            string preferredInitialDirectory =
                LauncherSettings.Current.DepotDownloaderLastInstallCommonPath;
            if (!AppPaths.TryGetContainingSteamCommonPath(preferredInitialDirectory, out string normalizedPreferredInitialDirectory))
                normalizedPreferredInitialDirectory = preferredInitialDirectory;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select game folder",
                InitialDirectory = Directory.Exists(normalizedPreferredInitialDirectory)
                    ? normalizedPreferredInitialDirectory
                    : AppPaths.SteamCommonPath,
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
            _detectedOriginalDownloadId = null;
            DetectedOriginalVersionText.Text = "Detecting version from build files...";
            FolderPathBox.Text = "";
            FolderNameBox.Text = "";
            DisplayNameBox.Text = "";
            AddButton.IsEnabled = false;

            string sourceFolderName = Path.GetFileName(folder);

            bool hasSubnauticaExe = File.Exists(Path.Combine(folder, "Subnautica.exe"));
            bool hasBelowZeroExe = File.Exists(Path.Combine(folder, "SubnauticaZero.exe"));

            if (!hasSubnauticaExe && !hasBelowZeroExe)
            {
                MessageBox.Show(
                    "Selected folder does not contain a supported game executable.",
                    "Invalid Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                DetectedOriginalVersionText.Text = "Could not detect a supported game executable in this folder.";
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
                DetectedOriginalVersionText.Text = "The selected folder must contain only one supported game.";
                return;
            }

            _detectedGame = hasSubnauticaExe ? LauncherGame.Subnautica : LauncherGame.BelowZero;

            if (LauncherGameProfiles.All.Any(profile =>
                    File.Exists(Path.Combine(folder, profile.InfoFileName))))
            {
                MessageBox.Show(
                    "This version is already managed by the launcher.",
                    "Already Managed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DetectedOriginalVersionText.Text = "This folder is already managed by the launcher.";
                return;
            }

            LauncherGameProfile profile = LauncherGameProfiles.Get(_detectedGame.Value);
            if (!VersionIdentityResolver.TryDetectOriginalVersion(
                    folder,
                    profile,
                    out GameVersionInstallDefinition? detectedVersion,
                    out _,
                    out string failureReason))
            {
                _detectedOriginalDownloadId = null;
                DetectedOriginalVersionText.Text = failureReason;
                MessageBox.Show(
                    $"{failureReason}{Environment.NewLine}{Environment.NewLine}The launcher only adds existing versions when it can match them exactly.",
                    "Version Detection Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _detectedOriginalDownloadId = detectedVersion!.Id;
            FolderPathBox.Text = folder;
            FolderNameBox.Text = BuildSuggestedManagedFolderName(folder, sourceFolderName, profile, detectedVersion);
            DisplayNameBox.Text = InstalledVersionNaming.BuildBaseDisplayName(detectedVersion.Id, detectedVersion.DisplayName);
            DetectedOriginalVersionText.Text = detectedVersion.DisplayName;
            AddButton.IsEnabled = true;
        }

        private static bool IsReservedActiveFolderName(string folderName)
        {
            return LauncherGameProfiles.IsReservedActiveFolderName(folderName);
        }

        private static string BuildSuggestedManagedFolderName(
            string folderPath,
            string sourceFolderName,
            LauncherGameProfile profile,
            GameVersionInstallDefinition detectedVersion)
        {
            string commonPath = AppPaths.GetSteamCommonPathFor(folderPath);
            string baseName = IsReservedActiveFolderName(sourceFolderName)
                ? detectedVersion.Id
                : sourceFolderName;

            if (!Directory.Exists(Path.Combine(commonPath, baseName)) ||
                string.Equals(Path.Combine(commonPath, baseName), folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return baseName;
            }

            int suffix = 2;
            while (true)
            {
                string candidate = $"{baseName}_{suffix}";
                string candidatePath = Path.Combine(commonPath, candidate);
                if (!Directory.Exists(candidatePath) ||
                    string.Equals(candidatePath, folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathBox.Text) || _detectedGame == null)
            {
                MessageBox.Show("Please select a valid game folder first.");
                return;
            }

            string displayName = InstalledVersionNaming.NormalizeSavedDisplayName(DisplayNameBox.Text);
            string folderName = FolderNameBox.Text.Trim();
            DisplayNameBox.Text = displayName;

            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(folderName))
            {
                MessageBox.Show("Display name and folder name are required.");
                return;
            }

            if (displayName.Length > InstalledVersionNaming.MaxDisplayNameLength)
            {
                MessageBox.Show(
                    $"Display name must be {InstalledVersionNaming.MaxDisplayNameLength} characters or fewer.",
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

            if (string.IsNullOrWhiteSpace(_detectedOriginalDownloadId))
            {
                MessageBox.Show(
                    "The launcher could not auto-detect the original version for this folder.",
                    "Version Detection Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            LauncherGameProfile profile = LauncherGameProfiles.Get(_detectedGame.Value);
            profile.EnsureSteamAppIdFile(FolderPathBox.Text);

            using (LauncherBusyCoordinator.Begin($"Add existing {folderName}"))
            {
                InstalledVersionFileService.WriteInfoFile(
                    FolderPathBox.Text,
                    profile,
                    displayName,
                    folderName,
                    _detectedOriginalDownloadId,
                    isModded: false);
            }

            DialogWindowHelper.Finish(this, true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogWindowHelper.Finish(this, false);
        }
    }
}
