using SubnauticaLauncher.Versions;
using System;
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
        private readonly InstalledVersion _version;

        private static readonly string BgPreset =
            Path.Combine(AppPaths.DataPath, "BPreset.txt");

        private const string DefaultBg = "GrassyPlateau";

        public EditVersionWindow(InstalledVersion version)
        {
            InitializeComponent();

            _version = version;

            // ✅ SET CUSTOM TITLE BAR TEXT
            TitleBarText.Text = $"Editing Version \"{version.DisplayLabel}\"";

            // Populate fields (same behavior as RenameVersionWindow)
            DisplayNameBox.Text = version.DisplayName;
            FolderNameBox.Text = version.FolderName;

            Loaded += EditVersionWindow_Loaded;
        }

        // ================= BACKGROUND =================

        private void EditVersionWindow_Loaded(object sender, RoutedEventArgs e)
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
                GetBackgroundBrush().ImageSource =
                    new BitmapImage(new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                        UriKind.Absolute));
            }
        }

        // ================= TITLE BAR =================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ================= SAVE (RENAME LOGIC) =================

        private void SaveRename_Click(object sender, RoutedEventArgs e)
        {
            if (_version.IsActive)
            {
                MessageBox.Show(
                    "Cannot edit the active version.",
                    "Edit Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string newDisplay = DisplayNameBox.Text.Trim();
            string newFolder = FolderNameBox.Text.Trim();

            // 🔥 SAME HARD BLOCK AS RenameVersionWindow
            if (string.Equals(newFolder, "Subnautica", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "The folder name cannot be 'Subnautica'.\n\n" +
                    "This name is reserved for the active game folder.",
                    "Invalid Folder Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool displayChanged = newDisplay != _version.DisplayName;
            bool folderChanged = newFolder != _version.FolderName;

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
                        MessageBoxImage.Warning);
                    return;
                }

                Directory.Move(_version.HomeFolder, newPath);
                _version.HomeFolder = newPath;
                _version.FolderName = newFolder;
            }

            // ================= DISPLAY NAME =================
            if (displayChanged)
            {
                _version.DisplayName = newDisplay;
            }

            VersionLoader.Save(_version);

            DialogResult = true;
            Close();
        }

        // ================= DELETE =================

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_version.IsActive)
            {
                MessageBox.Show(
                    "You cannot delete the currently active version.",
                    "Delete Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new DeleteVersionDialog
            {
                Owner = this
            };

            dialog.ShowDialog();

            try
            {
                switch (dialog.Choice)
                {
                    case DeleteChoice.Cancel:
                        return;

                    case DeleteChoice.RemoveFromLauncher:
                        string infoPath = Path.Combine(_version.HomeFolder, "Version.info");
                        if (File.Exists(infoPath))
                            File.Delete(infoPath);
                        break;

                    case DeleteChoice.DeleteGame:
                        Directory.Delete(_version.HomeFolder, true);
                        break;
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

        // ================= CANCEL =================

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}