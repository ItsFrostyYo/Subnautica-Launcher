using SubnauticaLauncher.Core;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Settings;
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

        public DepotInstallAuthOptions? AuthOptions { get; private set; }

        public DepotDownloaderLoginWindow()
        {
            InitializeComponent();
            Loaded += DepotDownloaderLoginWindow_Loaded;
        }

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

            ApplyBackground(bg);

            UsernameBox.Text = LauncherSettings.Current.DepotDownloaderLastUsername;
            RememberPasswordCheck.IsChecked = LauncherSettings.Current.DepotDownloaderRememberPassword;
            UseRememberedLoginCheck.IsChecked = LauncherSettings.Current.DepotDownloaderUseRememberedLoginOnly;
            PreferTwoFactorCodeCheck.IsChecked = LauncherSettings.Current.DepotDownloaderPreferTwoFactorCode;

            SyncPasswordState();
            Logger.Log("DepotDownloader login window opened.");
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
                        UriKind.Absolute);
                }

                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                GetBackgroundBrush().ImageSource = img;
            }
            catch
            {
                GetBackgroundBrush().ImageSource = new BitmapImage(new Uri(
                    $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                    UriKind.Absolute));
            }
        }

        private void SyncPasswordState()
        {
            bool usingRemembered = UseRememberedLoginCheck.IsChecked == true;
            PasswordBox.IsEnabled = true;
            PasswordHintText.Visibility = usingRemembered ? Visibility.Visible : Visibility.Collapsed;
        }

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

        private void UseRememberedLoginCheck_Click(object sender, RoutedEventArgs e)
        {
            SyncPasswordState();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameBox.Text.Trim();
            bool useRemembered = UseRememberedLoginCheck.IsChecked == true;
            string password = PasswordBox.Password;
            bool hasPassword = !string.IsNullOrWhiteSpace(password);

            if (string.IsNullOrWhiteSpace(username))
            {
                StatusText.Text = "Steam username is required.";
                return;
            }

            if (!useRemembered && !hasPassword)
            {
                StatusText.Text = "Steam password is required unless using remembered login.";
                return;
            }

            AuthOptions = new DepotInstallAuthOptions
            {
                Username = username,
                Password = password,
                RememberPassword = RememberPasswordCheck.IsChecked == true,
                UseRememberedLoginOnly = useRemembered,
                PreferTwoFactorCode = PreferTwoFactorCodeCheck.IsChecked == true
            };

            LauncherSettings.Current.DepotDownloaderLastUsername = username;
            LauncherSettings.Current.DepotDownloaderRememberPassword = AuthOptions.RememberPassword;
            LauncherSettings.Current.DepotDownloaderUseRememberedLoginOnly = useRemembered;
            LauncherSettings.Current.DepotDownloaderPreferTwoFactorCode = AuthOptions.PreferTwoFactorCode;
            LauncherSettings.Save();

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
