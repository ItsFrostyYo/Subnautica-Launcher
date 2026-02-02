using SubnauticaLauncher.Installer;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.BelowZero;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.BelowZero
{
    public partial class BZAddVersionWindow : Window
    {       
        
            private static readonly string BgPreset =
        Path.Combine(AppPaths.DataPath, "BPreset.txt");

            private const string DefaultBg = "Lifepod";

            public BZAddVersionWindow()
        {
            InitializeComponent();
            Loaded += BZAddVersionWindow_Loaded;
            BZAvailableVersionsList.ItemsSource = BZVersionRegistry.AllVersions;
            BZAvailableVersionsList.DisplayMemberPath = "DisplayName"; // ✅ REQUIRED
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }
        private void BZAddVersionWindow_Loaded(object sender, RoutedEventArgs e)
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

                // Custom image (absolute path)
                if (File.Exists(preset))
                {
                    img.UriSource = new Uri(preset, UriKind.Absolute);
                }
                else
                {
                    // Embedded asset
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
        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (BZAvailableVersionsList.SelectedItem is not BZVersionInstallDefinition version)
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

                await BZDepotDownloaderService.BZInstallVersionAsync(
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
            var win = new BZAddUnmanagedVersionWindow
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