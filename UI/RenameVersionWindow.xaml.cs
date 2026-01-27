using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.UI
{
    public partial class RenameVersionWindow : Window
    {
        public InstalledVersion Version { get; }

        private static readonly string BgPreset =
            Path.Combine(AppPaths.DataPath, "BPreset.txt");

        private const string DefaultBg = "GrassyPlateau";

        public RenameVersionWindow(InstalledVersion version)
        {
            InitializeComponent();

            Version = version;

            DisplayNameBox.Text = version.DisplayName;
            FolderNameBox.Text = version.FolderName;

            Loaded += RenameVersionWindow_Loaded;
        }

        // ================= BACKGROUND =================

        private void RenameVersionWindow_Loaded(object sender, RoutedEventArgs e)
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

                // Custom image path
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
                // Safe fallback
                GetBackgroundBrush().ImageSource =
                    new BitmapImage(new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                        UriKind.Absolute));
            }
        }

        // ================= TITLE BAR =================

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

        // ================= ACTIONS =================

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string newDisplay = DisplayNameBox.Text.Trim();
            string newFolder = FolderNameBox.Text.Trim();

            // 🔥 HARD BLOCK: Subnautica folder name
            if (string.Equals(newFolder, "Subnautica", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "The folder name cannot be 'Subnautica'.\n\n" +
                    "This name is reserved for the active game folder.\n" +
                    "Please choose a different name.",
                    "Invalid Folder Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            bool displayChanged = newDisplay != Version.DisplayName;
            bool folderChanged = newFolder != Version.FolderName;

            if (!displayChanged && !folderChanged)
            {
                DialogResult = false;
                Close();
                return;
            }

            // ================= RENAME FOLDER =================
            if (folderChanged)
            {
                string newPath = Path.Combine(
                    AppPaths.SteamCommonPath,
                    newFolder
                );

                if (Directory.Exists(newPath))
                {
                    MessageBox.Show(
                        "A folder with that name already exists.",
                        "Rename Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                Directory.Move(Version.HomeFolder, newPath);
                Version.HomeFolder = newPath;
                Version.FolderName = newFolder;
            }

            // ================= DISPLAY NAME =================
            if (displayChanged)
            {
                Version.DisplayName = newDisplay;
            }

            VersionLoader.Save(Version);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}