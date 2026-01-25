using SubnauticaLauncher.Installer;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI
{
    public partial class SetupWindow : Window
    {
        public SetupWindow()
        {
            InitializeComponent();
            Loaded += SetupWindow_Loaded;
        }

        private void EnsureBackgroundPreset()
        {
            Directory.CreateDirectory(AppPaths.DataPath);

            string presetPath = Path.Combine(AppPaths.DataPath, "BPreset.txt");

            if (!File.Exists(presetPath))
            {
                File.WriteAllText(presetPath, "Grassy Plateau");
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

                StatusText.Text = "Installing DepotDownloader...";
                await DepotDownloaderInstaller.EnsureInstalledAsync();

                StatusText.Text = "Writing background presets...";
                EnsureBackgroundPreset();

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