using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Versions;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SubnauticaLauncher.BelowZero
{
    public enum BZDeleteChoice
    {
        Cancel,
        RemoveFromLauncher,
        DeleteGame
    }

    public partial class BZDeleteVersionDialog : Window
    {
        private static readonly string BgPreset =
            Path.Combine(AppPaths.DataPath, "BPreset.txt");

        private const string DefaultBg = "GrassyPlateau";

        public BZDeleteChoice Choice { get; private set; } = BZDeleteChoice.Cancel;

        public BZDeleteVersionDialog()
        {
            InitializeComponent();
            Loaded += BZDeleteVersionDialog_Loaded;
        }

        // ================= BACKGROUND =================

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private void BZDeleteVersionDialog_Loaded(object sender, RoutedEventArgs e)
        {
            string bg = DefaultBg;
            Logger.Log("Delete Version Window Successfully Opened");
            if (File.Exists(BgPreset))
            {
                bg = File.ReadAllText(BgPreset).Trim();
                if (string.IsNullOrWhiteSpace(bg))
                    bg = DefaultBg;
            }
            
            Logger.Log("Delete Version Window Background Applied Successfully");
            ApplyBackground(bg);
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

        // ================= BUTTONS =================

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Choice = BZDeleteChoice.Cancel;
            Close();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            Choice = BZDeleteChoice.RemoveFromLauncher;
            Close();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            Choice = BZDeleteChoice.DeleteGame;
            Close();
        }
    }
}