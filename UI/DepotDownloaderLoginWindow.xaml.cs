using SubnauticaLauncher.Core;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SubnauticaLauncher.UI
{
    public partial class DepotDownloaderLoginWindow : Window
    {
        private const string DefaultBg = "GrassyPlateau";

        public string Username => UsernameBox.Text;
        public string Password => PasswordBox.Password;

        public DepotDownloaderLoginWindow()
        {
            InitializeComponent();
            Loaded += DepotDownloaderLoginWindow_Loaded;
        }

        // ================= BACKGROUND =================

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private void DepotDownloaderLoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Load();
            string bg = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;
            Logger.Log("Login Window has been Opened Successfully");
            Logger.Log("Login Window Background Applied Successfully");
            ApplyBackground(bg);
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
            Logger.Log("Login Window Minimized");
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Login Window Maximized");
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Login Window Closed");
            Close();
        }

        // ================= ACTIONS =================

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username) ||
                string.IsNullOrWhiteSpace(Password))
            {
                StatusText.Text = "Username and Password required.";
                Logger.Log("Failed to Login (Username and Password not Given)");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Logger.Log("User Canceled Login");
            Close();
        }
    }
}
