using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Application = System.Windows.Application;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher
{
    public partial class AddUnmanagedVersionWindow : Window
    {
        public AddUnmanagedVersionWindow()
        {
            InitializeComponent();
            LoadOriginalDownloads();
        }

        private void LoadOriginalDownloads()
        {
            OriginalDownloadBox.ItemsSource = VersionRegistry.AllVersions
                .Select(v => v.Id)
                .ToList();

            OriginalDownloadBox.SelectedIndex = 0;
        }
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Subnautica folder",
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
            if (!File.Exists(Path.Combine(folder, "Subnautica.exe")))
            {
                MessageBox.Show("Selected folder does not contain Subnautica.exe");
                return;
            }

            if (File.Exists(Path.Combine(folder, "Version.info")))
            {
                MessageBox.Show("This version is already managed by the launcher.");
                return;
            }

            FolderPathBox.Text = folder;
            FolderNameBox.Text = Path.GetFileName(folder);
            DisplayNameBox.Text = Path.GetFileName(folder);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathBox.Text))
            {
                MessageBox.Show("Please select a folder.");
                return;
            }

            string infoPath = Path.Combine(FolderPathBox.Text, "Version.info");

            File.WriteAllLines(infoPath, new[]
            {
                $"DisplayName={DisplayNameBox.Text}",
                $"FolderName={FolderNameBox.Text}",
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