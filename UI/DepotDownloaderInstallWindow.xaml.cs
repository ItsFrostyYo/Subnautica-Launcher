using SubnauticaLauncher.Core;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Settings;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.UI
{
    public partial class DepotDownloaderInstallWindow : Window
    {
        private const string DefaultBg = "Lifepod";
        private static readonly Regex PercentRegex = new(
            @"(?<!\d)(\d{1,3}(?:\.\d+)?)%",
            RegexOptions.Compiled);

        private readonly Func<DepotInstallCallbacks, CancellationToken, Task> _installAction;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<string> _pendingLogLines = new();
        private readonly object _logFilterLock = new();
        private readonly DispatcherTimer _logFlushTimer;

        private TaskCompletionSource<string?>? _promptTcs;
        private bool _promptIsSecret;
        private bool _installRunning;
        private bool _allowClose;
        private int _preallocCount;
        private int _lastRenderedPreallocCount;
        private int _pendingLogCount;
        private int _droppedLogLineCount;
        private int _logCharCount;
        private double _lastProgressUiValue = -1;
        private DateTime _lastProgressUiUpdateUtc = DateTime.MinValue;
        private double _lastLoggedProgressValue = -1;
        private DateTime _lastLoggedProgressAtUtc = DateTime.MinValue;
        private string _lastLoggedLine = "";

        private const int MaxPendingLogLines = 4000;
        private const int PreallocLogEvery = 500;
        private const int MaxLineLength = 240;
        private const int FlushMaxLinesPerTick = 40;
        private const int FlushMaxCharsPerTick = 12000;
        private const int MaxLogChars = 220000;
        private const int TrimmedLogChars = 140000;

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

            _logFlushTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(180),
                DispatcherPriority.Background,
                (_, _) => FlushPendingLogLines(),
                Dispatcher);

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
            _logFlushTimer.Start();
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
                OnStatus = message =>
                {
                    if (message.StartsWith("Pre-allocating ", StringComparison.OrdinalIgnoreCase))
                        return;

                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        StageText.Text = message;
                    });
                },
                OnOutput = EnqueueLogSafe,
                OnProgress = value =>
                {
                    double clamped = Math.Clamp(value, 0, 100);
                    var now = DateTime.UtcNow;

                    if (Math.Abs(clamped - _lastProgressUiValue) < 0.1 &&
                        (now - _lastProgressUiUpdateUtc).TotalMilliseconds < 180)
                    {
                        return;
                    }

                    _lastProgressUiValue = clamped;
                    _lastProgressUiUpdateUtc = now;

                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        if (clamped <= 0 && DownloadProgress.IsIndeterminate)
                        {
                            if (_preallocCount > 0)
                                PercentText.Text = "Preparing files (pre-allocation)...";
                            return;
                        }

                        if (DownloadProgress.IsIndeterminate)
                            DownloadProgress.IsIndeterminate = false;

                        DownloadProgress.Value = clamped;
                        PercentText.Text = $"{DownloadProgress.Value:0.0}%";
                    }, DispatcherPriority.Background);
                },
                RequestInputAsync = RequestInputAsync
            };

            try
            {
                EnqueueLogSafe("Starting install...");
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
                EnqueueLogSafe("Install completed successfully.");
                FlushPendingLogLines();

                await Task.Delay(450);
                _allowClose = true;
                DialogResult = true;
                Close();
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
                EnqueueLogSafe(FailureMessage);
            }
            catch (Exception ex)
            {
                FailureMessage = ex.Message;
                ResultText.Foreground = Brushes.OrangeRed;
                ResultText.Text = "Install failed.";
                StageText.Text = "Install failed.";
                CancelInstallButton.Visibility = Visibility.Collapsed;
                DoneButton.Visibility = Visibility.Visible;

                EnqueueLogSafe("Install failed:");
                EnqueueLogSafe(ex.Message);
                Logger.Exception(ex, "Depot install window install failure");
            }
            finally
            {
                _installRunning = false;
                PromptPanel.Visibility = Visibility.Collapsed;

                if (_promptTcs != null && !_promptTcs.Task.IsCompleted)
                    _promptTcs.TrySetResult(null);
            }
        }

        private void EnqueueLogSafe(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (line.StartsWith("Pre-allocating ", StringComparison.OrdinalIgnoreCase))
            {
                int count = Interlocked.Increment(ref _preallocCount);
                // Keep log concise while still showing activity.
                if (count % PreallocLogEvery != 0)
                    return;

                line = $"Pre-allocating files... {count:N0}";
            }

            bool isErrorLine = line.StartsWith("[stderr]", StringComparison.OrdinalIgnoreCase);

            lock (_logFilterLock)
            {
                if (!isErrorLine && TryExtractPercent(line, out double percent))
                {
                    var now = DateTime.UtcNow;
                    if (Math.Abs(percent - _lastLoggedProgressValue) < 1.0 &&
                        (now - _lastLoggedProgressAtUtc).TotalSeconds < 2)
                    {
                        return;
                    }

                    _lastLoggedProgressValue = percent;
                    _lastLoggedProgressAtUtc = now;
                    line = $"Download progress: {percent:0.00}%";
                }

                if (!isErrorLine && line.Length > MaxLineLength)
                    line = line.Substring(0, MaxLineLength - 3) + "...";

                if (string.Equals(line, _lastLoggedLine, StringComparison.Ordinal))
                    return;

                _lastLoggedLine = line;
            }

            if (Interlocked.Increment(ref _pendingLogCount) > MaxPendingLogLines)
            {
                Interlocked.Decrement(ref _pendingLogCount);
                Interlocked.Increment(ref _droppedLogLineCount);
                return;
            }

            _pendingLogLines.Enqueue(line);
        }

        private void FlushPendingLogLines()
        {
            if (_preallocCount > _lastRenderedPreallocCount &&
                DownloadProgress.IsIndeterminate)
            {
                _lastRenderedPreallocCount = _preallocCount;
                StageText.Text = $"Pre-allocating game files... ({_preallocCount:N0})";
                PercentText.Text = "Preparing files (pre-allocation)...";
            }

            int dropped = Interlocked.Exchange(ref _droppedLogLineCount, 0);
            if (_pendingLogLines.IsEmpty && dropped == 0)
                return;

            var batch = new StringBuilder();
            if (dropped > 0)
                batch.AppendLine($"[log] Skipped {dropped:N0} noisy lines to keep the UI responsive.");

            int taken = 0;
            while (taken < FlushMaxLinesPerTick &&
                   batch.Length < FlushMaxCharsPerTick &&
                   _pendingLogLines.TryDequeue(out string? line))
            {
                batch.AppendLine(line);
                Interlocked.Decrement(ref _pendingLogCount);
                taken++;
            }

            if (batch.Length == 0)
                return;

            LogText.AppendText(batch.ToString());
            _logCharCount += batch.Length;

            if (_logCharCount > MaxLogChars)
            {
                string currentText = LogText.Text;
                int trimStart = Math.Max(0, currentText.Length - TrimmedLogChars);
                if (trimStart > 0)
                    LogText.Text = currentText.Substring(trimStart);

                _logCharCount = LogText.Text.Length;
            }

            LogText.CaretIndex = LogText.Text.Length;
            LogText.ScrollToEnd();
        }

        private static bool TryExtractPercent(string line, out double percent)
        {
            Match match = PercentRegex.Match(line);
            if (match.Success &&
                double.TryParse(match.Groups[1].Value, out percent))
            {
                percent = Math.Clamp(percent, 0, 100);
                return true;
            }

            percent = 0;
            return false;
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

                EnqueueLogSafe("Authentication input requested.");
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
            {
                _logFlushTimer.Stop();
                return;
            }

            e.Cancel = true;
            CancelInstall_Click(this, new RoutedEventArgs());
        }
    }
}
