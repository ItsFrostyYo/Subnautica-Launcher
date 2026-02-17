using SubnauticaLauncher.Core;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Settings;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.UI
{
    public partial class DepotDownloaderInstallWindow : Window
    {
        private const string DefaultBg = "Lifepod";

        private readonly Func<DepotInstallCallbacks, CancellationToken, Task> _installAction;
        private readonly CancellationTokenSource _cts = new();

        private TaskCompletionSource<string?>? _promptTcs;
        private bool _promptIsSecret;
        private bool _installRunning;
        private bool _allowClose;

        public bool WasCancelled { get; private set; }
        public bool Succeeded { get; private set; }
        public string? FailureMessage { get; private set; }

        public DepotDownloaderInstallWindow(
            string displayName,
            Func<DepotInstallCallbacks, CancellationToken, Task> installAction)
        {
            InitializeComponent();
            _installAction = installAction;
            TitleText.Text = $"Installing {displayName}";
            Loaded += DepotDownloaderInstallWindow_Loaded;
            Closing += DepotDownloaderInstallWindow_Closing;
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private async void DepotDownloaderInstallWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyBackgroundFromSettings();
            await RunInstallAsync();
        }

        private void ApplyBackgroundFromSettings()
        {
            LauncherSettings.Load();
            string preset = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(preset))
                preset = DefaultBg;

            try
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();

                if (File.Exists(preset))
                    img.UriSource = new Uri(preset, UriKind.Absolute);
                else
                    img.UriSource = new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{preset}.png",
                        UriKind.Absolute);

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

        private async Task RunInstallAsync()
        {
            _installRunning = true;

            var callbacks = new DepotInstallCallbacks
            {
                OnStatus = message => Dispatcher.Invoke(() =>
                {
                    StageText.Text = message;
                }),
                OnOutput = AppendLogSafe,
                OnProgress = value => Dispatcher.Invoke(() =>
                {
                    if (DownloadProgress.IsIndeterminate)
                        DownloadProgress.IsIndeterminate = false;

                    DownloadProgress.Value = Math.Clamp(value, 0, 100);
                    PercentText.Text = $"{DownloadProgress.Value:0.0}%";
                }),
                RequestInputAsync = RequestInputAsync
            };

            try
            {
                AppendLogSafe("Starting install...");
                await _installAction(callbacks, _cts.Token);

                Succeeded = true;
                ResultText.Foreground = Brushes.LightGreen;
                ResultText.Text = "Install succeeded.";
                StageText.Text = "Install complete.";

                if (DownloadProgress.IsIndeterminate)
                    DownloadProgress.IsIndeterminate = false;

                DownloadProgress.Value = 100;
                PercentText.Text = "100%";
                CancelInstallButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;
                AppendLogSafe("Install completed successfully.");
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                FailureMessage = "Install cancelled.";
                ResultText.Foreground = Brushes.Orange;
                ResultText.Text = FailureMessage;
                StageText.Text = "Install cancelled.";
                CancelInstallButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;
                AppendLogSafe(FailureMessage);
            }
            catch (Exception ex)
            {
                FailureMessage = ex.Message;
                ResultText.Foreground = Brushes.OrangeRed;
                ResultText.Text = "Install failed.";
                StageText.Text = "Install failed.";
                CancelInstallButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;

                AppendLogSafe("Install failed:");
                AppendLogSafe(ex.Message);
                Logger.Exception(ex, "Depot install window install failure");
            }
            finally
            {
                _installRunning = false;

                if (_promptTcs != null && !_promptTcs.Task.IsCompleted)
                    _promptTcs.TrySetResult(null);
            }
        }

        private void AppendLogSafe(string line)
        {
            Dispatcher.Invoke(() => AppendLog(line));
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var sb = new StringBuilder(LogText.Text);
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(line);

            LogText.Text = sb.ToString();
            LogText.CaretIndex = LogText.Text.Length;
            LogText.ScrollToEnd();
        }

        private Task<string?> RequestInputAsync(DepotInstallPromptRequest request)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                _promptIsSecret = request.IsSecret;
                PromptText.Text = request.Prompt;
                PromptPanel.Visibility = Visibility.Visible;

                PromptInputTextBox.Text = "";
                PromptInputPasswordBox.Password = "";

                if (_promptIsSecret)
                {
                    PromptInputPasswordBox.Visibility = Visibility.Visible;
                    PromptInputTextBox.Visibility = Visibility.Collapsed;
                    PromptInputPasswordBox.Focus();
                }
                else
                {
                    PromptInputTextBox.Visibility = Visibility.Visible;
                    PromptInputPasswordBox.Visibility = Visibility.Collapsed;
                    PromptInputTextBox.Focus();
                }

                _promptTcs = new TaskCompletionSource<string?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                AppendLog("Authentication input requested.");
                return _promptTcs.Task;
            }).Task.Unwrap();
        }

        private void SubmitPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_promptTcs == null)
                return;

            string value = _promptIsSecret
                ? PromptInputPasswordBox.Password
                : PromptInputTextBox.Text;

            if (string.IsNullOrWhiteSpace(value))
            {
                ResultText.Foreground = Brushes.Orange;
                ResultText.Text = "Input cannot be empty.";
                return;
            }

            PromptPanel.Visibility = Visibility.Collapsed;
            ResultText.Text = "";
            _promptTcs.TrySetResult(value.Trim());
            _promptTcs = null;
        }

        private void CancelPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_promptTcs == null)
                return;

            PromptPanel.Visibility = Visibility.Collapsed;
            _promptTcs.TrySetResult(null);
            _promptTcs = null;
        }

        private void CancelInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!_installRunning)
                return;

            WasCancelled = true;
            StageText.Text = "Cancelling install...";
            ResultText.Foreground = Brushes.Orange;
            ResultText.Text = "Cancellation requested...";
            CancelInstallButton.IsEnabled = false;
            _cts.Cancel();

            if (_promptTcs != null && !_promptTcs.Task.IsCompleted)
                _promptTcs.TrySetResult(null);
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            _allowClose = true;
            DialogResult = Succeeded;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_installRunning)
            {
                CancelInstall_Click(sender, e);
                return;
            }

            _allowClose = true;
            Close();
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

        private void DepotDownloaderInstallWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose || !_installRunning)
                return;

            e.Cancel = true;
            CancelInstall_Click(this, new RoutedEventArgs());
        }
    }
}
