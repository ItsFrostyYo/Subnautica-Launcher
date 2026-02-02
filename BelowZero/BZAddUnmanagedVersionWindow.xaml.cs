using Microsoft.Win32;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SubnauticaLauncher.BelowZero
{
    public partial class BZAddUnmanagedVersionWindow : Window
    {
        private static readonly string BgPreset =
    Path.Combine(AppPaths.DataPath, "BPreset.txt");

        private const string DefaultBg = "Lifepod";

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        public BZAddUnmanagedVersionWindow()
        {
            InitializeComponent();
            Loaded += BZAddUnmanagedVersionWindow_Loaded;
            LoadOriginalDownloads();
        }

        private void BZAddUnmanagedVersionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string bg = DefaultBg;

            if (File.Exists(BgPreset))
            {
                bg = File.ReadAllText(BgPreset).Trim();
                if (string.IsNullOrWhiteSpace(bg))
                    bg = DefaultBg;
            }

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
                        UriKind.Absolute
                    );
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

        // ================= TITLE BAR =================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            else
                DragMove();
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

        

        // ================= Rest =================
        private void LoadOriginalDownloads()
        {
            BZOriginalDownloadBox.ItemsSource = BZVersionRegistry.AllVersions
                .Select(v => v.Id)
                .ToList();

            BZOriginalDownloadBox.SelectedIndex = 0;
        }
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Below Zero folder",
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

            // ❌ HARD BLOCK: Subnautica folder name
            if (string.Equals(folderName, "SubnauticaZero", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "The folder name cannot be 'SubnauticaZero'.\n\n" +
                    "This name is reserved for the active game folder.\n" +
                    "Please rename the folder before adding it.",
                    "Invalid Folder Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            if (!File.Exists(Path.Combine(folder, "SubnauticaZero.exe")))
            {
                MessageBox.Show(
                    "Selected folder does not contain SubnauticaZero.exe",
                    "Invalid Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            if (File.Exists(Path.Combine(folder, "BZVersion.info")))
            {
                MessageBox.Show(
                    "This version is already managed by the launcher.",
                    "Already Managed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            FolderPathBox.Text = folder;
            FolderNameBox.Text = folderName;
            DisplayNameBox.Text = folderName;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathBox.Text))
            {
                MessageBox.Show("Please select a folder.");
                return;
            }

            string infoPath = Path.Combine(FolderPathBox.Text, "BZVersion.info");

            File.WriteAllLines(infoPath, new[]
            {
                $"DisplayName={DisplayNameBox.Text}",
                $"FolderName={FolderNameBox.Text}",
                $"OriginalDownload={BZOriginalDownloadBox.SelectedItem}"
            });

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}