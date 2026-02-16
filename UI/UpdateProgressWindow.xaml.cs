using System;
using System.Windows;
using SubnauticaLauncher.Updates;

namespace SubnauticaLauncher.UI;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public void SetDetectedUpdate(UpdateInfo update)
    {
        Dispatcher.Invoke(() =>
        {
            string title = string.IsNullOrWhiteSpace(update.ReleaseName)
                ? $"Found update v{update.Version}"
                : $"Found update v{update.Version} - {update.ReleaseName}";

            DetectedUpdateText.Text = title;
        });
    }

    public void SetStatus(string status)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
        });
    }

    public void SetIndeterminate(string detail)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateProgressBar.IsIndeterminate = true;
            PercentText.Text = detail;
        });
    }

    public void SetProgress(double percent)
    {
        Dispatcher.Invoke(() =>
        {
            double safePercent = Math.Clamp(percent, 0, 100);
            UpdateProgressBar.IsIndeterminate = false;
            UpdateProgressBar.Value = safePercent;
            PercentText.Text = $"{safePercent:0}%";
        });
    }
}
