using System;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.UI
{
    public partial class AddVersionWindow : Window
    {
        public AddVersionWindow()
        {
            InitializeComponent();

            AvailableVersionsList.ItemsSource = VersionRegistry.AllVersions;
            AvailableVersionsList.DisplayMemberPath = "DisplayName"; // ✅ REQUIRED
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableVersionsList.SelectedItem is not VersionInstallDefinition version)
                return;

            // 🔐 Prompt login ONLY when installing
            var login = new DepotDownloaderLoginWindow { Owner = this };
            bool? result = login.ShowDialog();

            if (result != true)
                return;

            try
            {
                InstallButton.IsEnabled = false;

                string installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Steam",
                    "steamapps",
                    "common",
                    version.Id
                );

                await DepotDownloaderService.InstallVersionAsync(
                    version,
                    login.Username,
                    login.Password,
                    installDir
                );

                MessageBox.Show(
                    "Installation complete.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Install Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                InstallButton.IsEnabled = true;
            }
        }

        private void AddUnmanaged_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddUnmanagedVersionWindow
            {
                Owner = this
            };

            if (win.ShowDialog() == true)
            {
                // Close this window so MainWindow refreshes once
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}