using SubnauticaLauncher.Core;
using SubnauticaLauncher.Settings;
using System;
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
        private const string DefaultBackground = "Lifepod";

        public SetupWindow()
        {
            InitializeComponent();
            Loaded += SetupWindow_Loaded;
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private static string GetSetupBackgroundPreset()
        {
            try
            {
                LauncherSettings.Load();
                string preset = LauncherSettings.Current.BackgroundPreset;
                return string.IsNullOrWhiteSpace(preset) ? DefaultBackground : preset;
            }
            catch
            {
                return DefaultBackground;
            }
        }

        private void ApplySetupBackground()
        {
            string preset = GetSetupBackgroundPreset();

            try
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();

                if (System.IO.File.Exists(preset))
                {
                    img.UriSource = new Uri(preset, UriKind.Absolute);
                }
                else
                {
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
                try
                {
                    GetBackgroundBrush().ImageSource = new BitmapImage(new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{DefaultBackground}.png",
                        UriKind.Absolute));
                }
                catch
                {
                    // leave transparent if everything fails
                }
            }
        }

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

        private async void SetupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplySetupBackground();

                IProgress<string> statusProgress = new Progress<string>(msg => StatusText.Text = msg);
                statusProgress.Report("Checking launcher runtime...");

                await NewInstaller.RunAsync(statusProgress, throwOnFailure: true);

                StatusText.Text = "Finalizing setup...";
                await Task.Delay(400);

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
