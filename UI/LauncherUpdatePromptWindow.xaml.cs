using System.Windows;
using System.Windows.Controls;
using SubnauticaLauncher.Updates;
using WpfBrushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.UI;

public partial class LauncherUpdatePromptWindow : Window
{
    public LauncherUpdatePromptWindow(LauncherUpdate update)
    {
        InitializeComponent();
        HeadingText.Text = $"There is a new Update Available v{update.Version.ToString(3)}";
        ReleaseTitleText.Text = update.Title;
        ReleaseDateText.Text = update.Date;

        IReadOnlyList<string> changes = update.Changelog.Count == 0
            ? ["General launcher improvements."]
            : update.Changelog;
        foreach (string change in changes)
        {
            ChangelogPanel.Children.Add(new TextBlock
            {
                Text = "- " + change,
                Margin = new Thickness(0, 0, 0, 7),
                FontSize = 13,
                Foreground = WpfBrushes.White,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Update_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
