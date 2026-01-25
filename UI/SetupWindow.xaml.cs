using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.UI
{
    public partial class SetupWindow : Window
    {
        public SetupWindow()
        {
            InitializeComponent();
            Loaded += SetupWindow_Loaded;
        }

        // =========================
        // BACKGROUND DOWNLOAD
        // =========================
        private async Task InstallBackgroundsAsync()
        {
            Directory.CreateDirectory(AppPaths.BackgroundsPath);

            var backgrounds = new Dictionary<string, string>
            {
                {
                    "Lifepod.png",
                    "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/8a7e2d94fd0affc6a96a0abc4981252c43e2fa84/backgrounds/Lifepod.png"
                },
                {
                    "GrassyPlateau.png",
                    "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/8a7e2d94fd0affc6a96a0abc4981252c43e2fa84/backgrounds/GrassyPlateau.png"
                },
                {
                    "GrandReef.png",
                    "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/8a7e2d94fd0affc6a96a0abc4981252c43e2fa84/backgrounds/GrandReef.png"
                },
                {
                    "Reaper.png",
                    "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/8a7e2d94fd0affc6a96a0abc4981252c43e2fa84/backgrounds/Reaper.png"
                },
                {
                    "LostRiver.png",
                    "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/8a7e2d94fd0affc6a96a0abc4981252c43e2fa84/backgrounds/LostRiver.png"
                }
            };

            using var client = new HttpClient();

            foreach (var bg in backgrounds)
            {
                string targetPath = Path.Combine(AppPaths.BackgroundsPath, bg.Key);

                if (File.Exists(targetPath))
                    continue;

                byte[] data = await client.GetByteArrayAsync(bg.Value);
                await File.WriteAllBytesAsync(targetPath, data);
            }
        }

        // =========================
        // SETUP FLOW
        // =========================
        private async void SetupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Creating folders...";

                Directory.CreateDirectory(AppPaths.ToolsPath);
                Directory.CreateDirectory(AppPaths.LogsPath);
                Directory.CreateDirectory(AppPaths.DataPath);
                Directory.CreateDirectory(AppPaths.BackgroundsPath);

                StatusText.Text = "Installing DepotDownloader...";
                await DepotDownloaderInstaller.EnsureInstalledAsync();

                StatusText.Text = "Installing backgrounds...";
                await InstallBackgroundsAsync();

                StatusText.Text = "Setup complete.";
                await Task.Delay(600);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Setup failed:\n{ex.Message}",
                    "Setup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Application.Current.Shutdown();
            }
        }
    }
}