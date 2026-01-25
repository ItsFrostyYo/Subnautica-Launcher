using SubnauticaLauncher.Installer;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI
{
    public partial class SetupWindow : Window
    {
        private const string SetupBackground = "GrassyPlateau";

        public SetupWindow()
        {
            InitializeComponent();
            Loaded += SetupWindow_Loaded;
        }

        // ================= BACKGROUND =================

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private void ApplySetupBackground()
        {
            try
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();

                img.UriSource = new Uri(
                    $"pack://application:,,,/Assets/Backgrounds/{SetupBackground}.png",
                    UriKind.Absolute
                );

                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                GetBackgroundBrush().ImageSource = img;
            }
            catch
            {
                // If even this fails, just leave background blank
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

        // ================= SETUP FLOW =================

        private async void SetupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // ✅ Background FIRST (no dependencies)
                ApplySetupBackground();

                StatusText.Text = "Creating folders...";

                Directory.CreateDirectory(AppPaths.ToolsPath);
                Directory.CreateDirectory(AppPaths.LogsPath);
                Directory.CreateDirectory(AppPaths.DataPath);

                StatusText.Text = "Installing DepotDownloader...";
                await DepotDownloaderInstaller.EnsureInstalledAsync();

                StatusText.Text = "Finalizing setup...";
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