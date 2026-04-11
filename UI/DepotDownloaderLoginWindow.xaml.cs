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
        private bool _hasRememberedLoginSeeded;

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
            _hasRememberedLoginSeeded = LauncherSettings.Current.DepotDownloaderRememberedLoginSeeded;
            UseRememberedLoginCheck.IsChecked = _hasRememberedLoginSeeded &&
                                               LauncherSettings.Current.DepotDownloaderUseRememberedLoginOnly;
            PreferTwoFactorCodeCheck.IsChecked = LauncherSettings.Current.DepotDownloaderPreferTwoFactorCode;

            UpdateRememberedLoginUi();
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
            PasswordBox.IsEnabled = !usingRemembered;
            PasswordBox.Opacity = usingRemembered ? 0.65 : 1.0;

            if (usingRemembered)
            {
                PasswordHintText.Text = "Using remembered login for this install. Password entry is disabled unless you turn that option off.";
            }
            else if (_hasRememberedLoginSeeded)
            {
                PasswordHintText.Text = "Enter your password if you want to sign in normally and refresh the remembered login cache.";
            }
            else
            {
                PasswordHintText.Text = "Enter your password once to seed remembered login for future installs.";
            }
        }

        private void UpdateRememberedLoginUi()
        {
            if (_hasRememberedLoginSeeded)
            {
                RememberedLoginStateText.Text = "Remembered Steam login is available on this PC. You can use it now or sign in normally again.";
                UseRememberedLoginCheck.Visibility = Visibility.Visible;
            }
            else
            {
                RememberedLoginStateText.Text = "Remembered Steam login is not saved yet. Complete one normal sign-in first, then you can use remembered login later.";
                UseRememberedLoginCheck.Visibility = Visibility.Collapsed;
                UseRememberedLoginCheck.IsChecked = false;
            }
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
            string password = useRemembered ? "" : PasswordBox.Password;
            bool hasPassword = !string.IsNullOrWhiteSpace(password);

            if (string.IsNullOrWhiteSpace(username))
            {
                StatusText.Text = "Steam username is required.";
                return;
            }

            if (useRemembered && !_hasRememberedLoginSeeded)
            {
                StatusText.Text = "Remembered login is not ready yet. Sign in once with your password first.";
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
                RememberPassword = true,
                UseRememberedLoginOnly = useRemembered,
                PreferTwoFactorCode = PreferTwoFactorCodeCheck.IsChecked == true
            };

            LauncherSettings.Current.DepotDownloaderLastUsername = username;
            LauncherSettings.Current.DepotDownloaderRememberPassword = true;
            LauncherSettings.Current.DepotDownloaderUseRememberedLoginOnly = useRemembered;
            LauncherSettings.Current.DepotDownloaderPreferTwoFactorCode = AuthOptions.PreferTwoFactorCode;
            LauncherSettings.Save();

            DialogWindowHelper.Finish(this, true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogWindowHelper.Finish(this, false);
        }
    }
}
