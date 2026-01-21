using System;
using System.Windows;

namespace SubnauticaLauncher
{
    public partial class InstallProgressWindow : Window
    {
        public InstallProgressWindow(string versionName)
        {
            InitializeComponent();
            TitleText.Text = $"Installing {versionName}";
        }

        public void AppendLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText(line + Environment.NewLine);
                LogBox.ScrollToEnd();
            });
        }

        public void MarkFinished(bool success, string? error = null)
        {
            Dispatcher.Invoke(() =>
            {
                AppendLog(success
                    ? "Installation completed successfully."
                    : $"Installation failed:\n{error}");

                CloseButton.IsEnabled = true;
            });
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}