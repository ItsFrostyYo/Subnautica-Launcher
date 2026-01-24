using SubnauticaLauncher.Models;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace SubnauticaLauncher
{
    public partial class RenameVersionWindow : Window
    {
        public InstalledVersion Version { get; }

        public RenameVersionWindow(InstalledVersion version)
        {
            InitializeComponent();
            Version = version;

            DisplayNameBox.Text = version.DisplayName;
            FolderNameBox.Text = version.FolderName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string newDisplay = DisplayNameBox.Text.Trim();
            string newFolder = FolderNameBox.Text.Trim();

            bool displayChanged = newDisplay != Version.DisplayName;
            bool folderChanged = newFolder != Version.FolderName;

            if (!displayChanged && !folderChanged)
            {
                DialogResult = false;
                Close();
                return;
            }

            // Folder rename (IMMEDIATE)
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

            // Display name change
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