using SubnauticaLauncher.Settings;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SubnauticaLauncher.UI;

public enum LauncherDiscontinuedChoice
{
    Close,
    UpdateToNewLauncher
}

public partial class LauncherDiscontinuedWindow : Window
{
    private const string DefaultBackground = "Lifepod";
    public const string NewLauncherReleaseUrl = "https://github.com/ItsFrostyYo/Subnautica-Launcher/releases/tag/v4.0.0%2B";

    public LauncherDiscontinuedChoice Choice { get; private set; } = LauncherDiscontinuedChoice.Close;

    public LauncherDiscontinuedWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyBackground();
    }

    private void ApplyBackground()
    {
        string preset = LauncherSettings.Current.BackgroundPreset;
        if (string.IsNullOrWhiteSpace(preset))
            preset = DefaultBackground;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = File.Exists(preset)
                ? new Uri(preset, UriKind.Absolute)
                : new Uri($"pack://application:,,,/Assets/Backgrounds/{preset}.png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            ((ImageBrush)Resources["BackgroundBrush"]).ImageSource = image;
        }
        catch
        {
            ((ImageBrush)Resources["BackgroundBrush"]).ImageSource = new BitmapImage(
                new Uri($"pack://application:,,,/Assets/Backgrounds/{DefaultBackground}.png", UriKind.Absolute));
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void InstallSeparately_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = NewLauncherReleaseUrl,
            UseShellExecute = true
        });
        Close();
    }

    private void UpdateLauncher_Click(object sender, RoutedEventArgs e)
    {
        Choice = LauncherDiscontinuedChoice.UpdateToNewLauncher;
        Close();
    }
}
