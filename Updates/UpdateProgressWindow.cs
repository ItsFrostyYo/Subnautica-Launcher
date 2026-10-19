using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage = System.Windows.Controls.Image;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace SubnauticaLauncher.Updates;

public sealed class UpdateProgressWindow : Window
{
    private readonly TextBlock _detectedUpdateText;
    private readonly TextBlock _statusText;
    private readonly WpfProgressBar _progressBar;
    private readonly TextBlock _detailText;

    public UpdateProgressWindow(string version)
    {
        Title = "Updating Subnautica Launcher";
        Width = 520;
        Height = 250;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = ResolveFontFamily();

        var content = new StackPanel();
        var header = new StackPanel { Orientation = WpfOrientation.Horizontal };
        WpfImage? icon = TryCreateIcon();
        if (icon is not null) header.Children.Add(icon);
        header.Children.Add(new TextBlock
        {
            Text = "Subnautica Launcher Update",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = WpfBrushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(header);

        _detectedUpdateText = new TextBlock
        {
            Text = $"Updating to v{version}",
            Margin = new Thickness(0, 14, 0, 0),
            FontSize = 14,
            Foreground = Brush("#FFD8F0FF"),
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(_detectedUpdateText);

        _statusText = new TextBlock
        {
            Text = "Preparing update...",
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 13,
            Foreground = Brush("#FFFFD58A"),
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(_statusText);

        _progressBar = new WpfProgressBar
        {
            Height = 13,
            Margin = new Thickness(0, 16, 0, 0),
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true,
            Foreground = Brush("#FF4FD38B"),
            Background = Brush("#55000000")
        };
        content.Children.Add(_progressBar);

        _detailText = new TextBlock
        {
            Text = "Starting...",
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 12,
            Foreground = Brush("#FFC7DCEB"),
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(_detailText);

        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderBrush = Brush("#66FFFFFF"),
            BorderThickness = new Thickness(1),
            Background = Brush("#F20A1827"),
            Padding = new Thickness(20),
            Child = content
        };
    }

    public void Apply(LauncherUpdateProgress progress)
    {
        void Update()
        {
            _statusText.Text = progress.Status;
            _detailText.Text = progress.Detail;
            if (progress.Percent.HasValue)
            {
                double percent = Math.Clamp(progress.Percent.Value, 0, 100);
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = percent;
            }
            else
            {
                _progressBar.IsIndeterminate = true;
            }
        }

        if (Dispatcher.CheckAccess()) Update();
        else Dispatcher.Invoke(Update);
    }

    public void SetDetectedUpdate(string text)
    {
        if (Dispatcher.CheckAccess()) _detectedUpdateText.Text = text;
        else Dispatcher.Invoke(() => _detectedUpdateText.Text = text);
    }

    public void PumpUi() => Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static WpfFontFamily ResolveFontFamily()
    {
        try
        {
            string fonts = Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Fonts") + Path.DirectorySeparatorChar;
            if (File.Exists(Path.Combine(fonts, "aller.regular.ttf")))
                return new WpfFontFamily(new Uri(fonts, UriKind.Absolute), "./#Aller");
        }
        catch { }
        return new WpfFontFamily("Segoe UI");
    }

    private static WpfImage? TryCreateIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Icons", "SNLIcon.png");
            if (!File.Exists(path)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return new WpfImage
            {
                Source = bitmap,
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 10, 0),
                Stretch = Stretch.Uniform
            };
        }
        catch { return null; }
    }

    private static SolidColorBrush Brush(string value) =>
        new((WpfColor)WpfColorConverter.ConvertFromString(value));
}
