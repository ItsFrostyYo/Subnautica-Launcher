using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Timer;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using UpdatesData = SubnauticaLauncher.Updates.Updates;

namespace SubnauticaLauncher.UI
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private const string SnActiveFolder = "Subnautica";
        private const string SnUnmanagedFolder = "SubnauticaUnmanagedVersion";
        private const string BzActiveFolder = "SubnauticaZero";
        private const string BzUnmanagedFolder = "SubnauticaZeroUnmanagedVersion";
        private const string DefaultBg = "Lifepod";

        private const int HotkeyIdReset = 9001;
        private const int HotkeyIdOverlayToggle = 9002;
        private const int WM_HOTKEY = 0x0312;

        private Key _resetKey = Key.None;
        private Key _overlayToggleKey = Key.Tab;
        private ModifierKeys _overlayToggleModifiers = ModifierKeys.Control | ModifierKeys.Shift;
        private bool _macroEnabled;
        private bool _renameOnCloseEnabled = true;
        private bool _overlayStartupMode;
        private bool _isCapturingOverlayHotkey;
        private bool _syncingStartupModeSelection;
        private bool _syncingOverlayOpacityControl;
        private double _overlayOpacity = 0.5;
        private DispatcherTimer? _statusRefreshTimer;
        private LauncherOverlayWindow? _launcherOverlayWindow;

        private static CancellationTokenSource? _explosionCts;
        private static bool _explosionRunning;

        private System.Windows.Forms.NotifyIcon? _trayIcon;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            Logger.Log("MainWindow constructor");

            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            Logger.Log("Window source initialized");

            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WndProc);

            RegisterResetHotkey();
            RegisterOverlayToggleHotkey();
        }

        private IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();

                if (hotkeyId == HotkeyIdReset)
                {
                    _ = Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await OnResetHotkeyPressed();
                        }
                        catch (Exception ex)
                        {
                            Logger.Exception(ex, "Reset macro failed");
                        }
                    });

                    handled = true;
                }
                else if (hotkeyId == HotkeyIdOverlayToggle)
                {
                    _ = Dispatcher.InvokeAsync(ToggleOverlayVisibility);
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Log("Launcher UI loaded");

            if (NewInstaller.IsBootstrapRequired())
            {
                Logger.Warn("Runtime bootstrap required, opening setup window");

                var setup = new SetupWindow { Owner = this };
                bool? result = setup.ShowDialog();

                if (result != true)
                {
                    Logger.Warn("Setup cancelled, shutting down");
                    Application.Current.Shutdown();
                    return;
                }
            }

            await CheckForUpdatesOnStartup();

            Directory.CreateDirectory(AppPaths.DataPath);
            OldRemover.Run();
            await NewInstaller.RunAsync();

            InitializeTrayIcon();

            LauncherSettings.Load();
            var settings = LauncherSettings.Current;
            LoadOverlayModeSettings();

            string bg = settings.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
            {
                bg = DefaultBg;
                settings.BackgroundPreset = bg;
                LauncherSettings.Save();
            }

            ApplyBackground(bg);
            SyncThemeDropdown(bg);

            LoadInstalledVersions();
            StartStatusRefreshTimer();
            LoadMacroSettings();

            ExplosionResetSettings.Load();

            ExplosionResetToggleButton.Content =
                ExplosionResetSettings.Enabled ? "Enabled" : "Disabled";

            ExplosionResetToggleButton.Background =
                ExplosionResetSettings.Enabled ? Brushes.Green : Brushes.DarkRed;

            ExplosionPresetDropdown.IsEnabled = ExplosionResetSettings.Enabled;

            ExplosionPresetDropdown.SelectedItem =
                ExplosionPresetDropdown.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(i => (string)i.Tag == ExplosionResetSettings.Preset.ToString());

            ExplosionDisplayToggleButton.Content =
                ExplosionResetSettings.OverlayEnabled ? "Enabled" : "Disabled";
            ExplosionDisplayToggleButton.Background =
                ExplosionResetSettings.OverlayEnabled ? Brushes.Green : Brushes.DarkRed;

            ExplosionTrackToggleButton.Content =
                ExplosionResetSettings.TrackResets ? "Enabled" : "Disabled";
            ExplosionTrackToggleButton.Background =
                ExplosionResetSettings.TrackResets ? Brushes.Green : Brushes.DarkRed;

            RenameOnCloseButton.Content = _renameOnCloseEnabled ? "Enabled" : "Disabled";
            RenameOnCloseButton.Background = _renameOnCloseEnabled ? Brushes.Green : Brushes.DarkRed;

            UpdateHardcoreSaveDeleterVisualState();
            UpdateSubnautica100TrackerVisualState();

            GameEventDocumenter.Start();
            DebugTelemetryController.Start();

            if (LauncherSettings.Current.Subnautica100TrackerEnabled)
                Subnautica100TrackerOverlayController.Start();
            else
                Subnautica100TrackerOverlayController.Stop();

            if (LauncherSettings.Current.SpeedrunTimerEnabled)
                SpeedrunTimerController.Start();

            Logger.Log("Startup complete");
            ShowView(InstallsView);

            if (_overlayStartupMode)
                ShowOverlayWindow();
        }

        private void UpdateResetMacroVisualState()
        {
            ResetMacroToggleButton.Content = _macroEnabled ? "Enabled" : "Disabled";
            ResetMacroToggleButton.Background = _macroEnabled ? Brushes.Green : Brushes.DarkRed;
        }

        private void UpdateHardcoreSaveDeleterVisualState()
        {
            bool enabled = LauncherSettings.Current.HardcoreSaveDeleterEnabled;
            HardcoreSaveDeleterToggleButton.Content = enabled ? "Enabled" : "Disabled";
            HardcoreSaveDeleterToggleButton.Background = enabled ? Brushes.Green : Brushes.DarkRed;
        }

        private void UpdateSubnautica100TrackerVisualState()
        {
            // Tracker enable/disable is configured in the tracker customization window.
        }

        private void RegisterResetHotkey()
        {
            var handle = new WindowInteropHelper(this).Handle;

            UnregisterHotKey(handle, HotkeyIdReset);
            RegisterHotkey(handle, HotkeyIdReset, _macroEnabled, _resetKey, "Reset Macro");
        }

        private static void RegisterHotkey(
            IntPtr handle,
            int hotkeyId,
            bool enabled,
            Key key,
            string game)
        {
            if (!enabled || key == Key.None)
            {
                Logger.Log($"{game} hotkey not registered (disabled or no key)");
                return;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            bool ok = RegisterHotKey(handle, hotkeyId, 0, vk);
            Logger.Log($"{game} hotkey registered: Key={key}, Success={ok}");
        }

        private static uint ToRegisterHotkeyModifiers(ModifierKeys modifiers)
        {
            const uint MOD_ALT = 0x0001;
            const uint MOD_CONTROL = 0x0002;
            const uint MOD_SHIFT = 0x0004;
            const uint MOD_WIN = 0x0008;

            uint value = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt))
                value |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control))
                value |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift))
                value |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows))
                value |= MOD_WIN;

            return value;
        }

        private void RegisterOverlayToggleHotkey()
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyIdOverlayToggle);

            if (!_overlayStartupMode || _overlayToggleKey == Key.None)
                return;

            uint modifiers = ToRegisterHotkeyModifiers(_overlayToggleModifiers);
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(_overlayToggleKey);
            bool ok = RegisterHotKey(handle, HotkeyIdOverlayToggle, modifiers, vk);
            Logger.Log($"Overlay hotkey registered: {FormatHotkey(_overlayToggleModifiers, _overlayToggleKey)}, Success={ok}");
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
            Hide();
            ShowInTaskbar = false;
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
                    var uri = new Uri($"pack://application:,,,/Assets/Backgrounds/{preset}.png", UriKind.Absolute);
                    Application.GetResourceStream(uri);
                    img.UriSource = uri;
                }

                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                BackgroundBrush.ImageSource = img;
            }
            catch
            {
                BackgroundBrush.ImageSource = new BitmapImage(new Uri(
                    $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                    UriKind.Absolute));
            }
        }

        private async Task CheckForUpdatesOnStartup()
        {
            UpdateProgressWindow? progressWindow = null;

            try
            {
                var update = await UpdateChecker.CheckForUpdateAsync();
                if (update == null)
                    return;

                progressWindow = new UpdateProgressWindow
                {
                    Owner = this
                };

                progressWindow.SetDetectedUpdate(update);
                progressWindow.SetStatus("Preparing update...");
                progressWindow.SetIndeterminate("Starting...");
                progressWindow.Show();

                IsEnabled = false;

                IProgress<string> statusProgress = new Progress<string>(progressWindow.SetStatus);
                statusProgress.Report("Verifying latest updater...");

                string updaterPath = await UpdaterChecker.EnsureUpdaterAsync(update, statusProgress);

                statusProgress.Report($"Downloading launcher v{update.Version}...");

                var downloadProgress = new Progress<double>(progressWindow.SetProgress);
                string newExe = await UpdateDownloader.DownloadAsync(
                    update.LauncherDownloadUrl,
                    "SubnauticaLauncher.new.exe",
                    downloadProgress);

                statusProgress.Report("Launching updater...");
                progressWindow.SetIndeterminate("Finalizing...");

                await Task.Delay(200);
                UpdateHelper.ApplyUpdate(newExe, updaterPath);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Automatic update flow failed");

                if (progressWindow != null)
                {
                    progressWindow.SetStatus("Update failed. Continuing startup...");
                    progressWindow.SetIndeterminate("Using current launcher version.");
                    await Task.Delay(1400);
                }
            }
            finally
            {
                if (progressWindow != null)
                    progressWindow.Close();

                IsEnabled = true;
            }
        }

        private void BuildUpdatesView()
        {
            UpdatesPanel.Children.Clear();

            foreach (var update in UpdatesData.History)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(34, 0, 0, 0)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var panel = new StackPanel();

                panel.Children.Add(new TextBlock
                {
                    Text = $"{update.Version} ({update.Title})",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                });

                panel.Children.Add(new TextBlock
                {
                    Text = update.Date,
                    FontSize = 12,
                    FontWeight = FontWeights.ExtraLight,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 2, 0, 6)
                });

                foreach (var change in update.Changes)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "• " + change,
                        FontSize = 13,
                        Foreground = Brushes.LightGray
                    });
                }

                border.Child = panel;
                UpdatesPanel.Children.Add(border);
            }
        }

        private void ExplosionResetToggle_Click(object sender, RoutedEventArgs e)
        {
            ExplosionResetSettings.Enabled = !ExplosionResetSettings.Enabled;
            ExplosionResetSettings.Save();

            ExplosionResetToggleButton.Content =
                ExplosionResetSettings.Enabled ? "Enabled" : "Disabled";

            ExplosionResetToggleButton.Background =
                ExplosionResetSettings.Enabled ? Brushes.Green : Brushes.DarkRed;

            ExplosionPresetDropdown.IsEnabled = ExplosionResetSettings.Enabled;

            Logger.Log($"Explosion reset enabled = {ExplosionResetSettings.Enabled}");
        }

        private void ExplosionPresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExplosionPresetDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                ExplosionResetSettings.Preset = Enum.Parse<Enums.ExplosionResetPreset>(tag);
                ExplosionResetSettings.Save();

                Logger.Log($"Explosion reset preset set to {ExplosionResetSettings.Preset}");
            }
        }

        private void SyncThemeDropdown(string bg)
        {
            foreach (ComboBoxItem i in ThemeDropdown.Items)
            {
                if (i.Content is string text && text == bg)
                {
                    ThemeDropdown.SelectedItem = i;
                    return;
                }
            }

            ThemeDropdown.SelectedItem = null;
        }

        private void ThemeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeDropdown.SelectedItem is ComboBoxItem i &&
                i.Content is string name)
            {
                LauncherSettings.Current.BackgroundPreset = name;
                LauncherSettings.Save();
                ApplyBackground(name);
            }
        }

        private void ChooseCustomBackground_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
            };

            if (dlg.ShowDialog() != true)
                return;

            LauncherSettings.Current.BackgroundPreset = dlg.FileName;
            LauncherSettings.Save();
            ApplyBackground(dlg.FileName);
            ThemeDropdown.SelectedItem = null;
        }

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is InstalledVersion snVersion)
            {
                await LaunchSubnauticaVersionAsync(snVersion);
                return;
            }

            if (BZInstalledVersionsList.SelectedItem is BZInstalledVersion bzVersion)
            {
                await LaunchBelowZeroVersionAsync(bzVersion);
                return;
            }
        }

        private async void CloseGame_Click(object sender, RoutedEventArgs e)
        {
            SetClosingOnActiveVersions();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
            await LaunchCoordinator.CloseAllGameProcessesAsync();
            ClearClosingStatus();
            RefreshRunningStatusIndicators();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        private async Task LaunchSubnauticaVersionAsync(InstalledVersion target)
        {
            string common = AppPaths.GetSteamCommonPathFor(target.HomeFolder);
            string activePath = Path.Combine(common, SnActiveFolder);
            string targetExe = Path.Combine(activePath, "Subnautica.exe");

            try
            {
                bool isAlreadyActive =
                    Directory.Exists(activePath) &&
                    PathsAreEqual(target.HomeFolder, activePath);

                SetStatus(target, VersionStatus.Switching);

                bool wasRunning = await LaunchCoordinator.CloseAllGameProcessesAsync();

                if (wasRunning)
                {
                    await Task.Delay(1000);

                    int yearGroup = BuildYearResolver.ResolveGroupedYear(target.HomeFolder);
                    if (yearGroup >= 2022)
                        await Task.Delay(1500);
                }

                if (!isAlreadyActive)
                {
                    await LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                        common,
                        SnActiveFolder,
                        SnUnmanagedFolder,
                        "Version.info",
                        static (active, info) => InstalledVersion.FromInfo(active, info)?.FolderName);

                    if (Directory.Exists(activePath))
                        throw new IOException("Subnautica folder still exists after restore.");

                    await LaunchCoordinator.MoveFolderWithRetryAsync(target.HomeFolder, activePath);
                    await Task.Delay(250);
                }

                if (!File.Exists(targetExe))
                    throw new FileNotFoundException("Subnautica.exe not found in active folder.", targetExe);

                SetStatus(target, VersionStatus.Launching);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = activePath,
                    UseShellExecute = true
                });

                if (process == null)
                    throw new InvalidOperationException("Failed to launch Subnautica.");

                bool launched = await WaitForLaunchedAsync(process);
                SetStatus(target, launched ? VersionStatus.Launched : VersionStatus.Active);
                LoadInstalledVersions();
                RefreshRunningStatusIndicators();
            }
            catch (Exception ex)
            {
                SetStatus(target, VersionStatus.Idle);

                MessageBox.Show(
                    ex.Message,
                    "Launch Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task LaunchBelowZeroVersionAsync(BZInstalledVersion target)
        {
            string common = AppPaths.GetSteamCommonPathFor(target.HomeFolder);
            string activePath = Path.Combine(common, BzActiveFolder);
            string targetExe = Path.Combine(activePath, "SubnauticaZero.exe");

            try
            {
                bool isAlreadyActive =
                    Directory.Exists(activePath) &&
                    PathsAreEqual(target.HomeFolder, activePath);

                SetStatus(target, VersionStatus.Switching);

                bool wasRunning = await LaunchCoordinator.CloseAllGameProcessesAsync();

                if (wasRunning)
                    await Task.Delay(1000);

                if (!isAlreadyActive)
                {
                    await LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                        common,
                        BzActiveFolder,
                        BzUnmanagedFolder,
                        "BZVersion.info",
                        static (active, info) => BZInstalledVersion.FromInfo(active, info)?.FolderName);

                    if (Directory.Exists(activePath))
                        throw new IOException("Below Zero folder still exists after restore.");

                    await LaunchCoordinator.MoveFolderWithRetryAsync(target.HomeFolder, activePath);
                    await Task.Delay(250);
                }

                if (!File.Exists(targetExe))
                    throw new FileNotFoundException("SubnauticaZero.exe not found in active folder.", targetExe);

                SetStatus(target, VersionStatus.Launching);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = activePath,
                    UseShellExecute = true
                });

                if (process == null)
                    throw new InvalidOperationException("Failed to launch Below Zero.");

                bool launched = await WaitForLaunchedAsync(process);
                SetStatus(target, launched ? VersionStatus.Launched : VersionStatus.Active);
                LoadInstalledVersions();
                RefreshRunningStatusIndicators();
            }
            catch (Exception ex)
            {
                SetStatus(target, VersionStatus.Idle);

                MessageBox.Show(
                    ex.Message,
                    "Launch Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool PathsAreEqual(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private void LoadInstalledVersions()
        {
            var snList = VersionLoader.LoadInstalled();
            foreach (var v in snList)
            {
                string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                string active = Path.Combine(common, SnActiveFolder);
                v.Status = PathsAreEqual(v.HomeFolder, active)
                    ? VersionStatus.Active
                    : VersionStatus.Idle;
            }

            InstalledVersionsList.ItemsSource = snList;
            InstalledVersionsList.Items.Refresh();

            var bzList = BZVersionLoader.LoadInstalled();
            foreach (var v in bzList)
            {
                string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                string active = Path.Combine(common, BzActiveFolder);
                v.Status = PathsAreEqual(v.HomeFolder, active)
                    ? VersionStatus.Active
                    : VersionStatus.Idle;
            }

            BZInstalledVersionsList.ItemsSource = bzList;
            BZInstalledVersionsList.Items.Refresh();
            RefreshRunningStatusIndicators();
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void SetStatus(InstalledVersion version, VersionStatus status)
        {
            version.Status = status;
            InstalledVersionsList.Items.Refresh();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        private void SetStatus(BZInstalledVersion version, VersionStatus status)
        {
            version.Status = status;
            BZInstalledVersionsList.Items.Refresh();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        private void SetClosingOnActiveVersions()
        {
            bool snChanged = false;
            bool bzChanged = false;

            if (InstalledVersionsList.ItemsSource is IEnumerable<InstalledVersion> snVersions)
            {
                foreach (var v in snVersions)
                {
                    if (v.Status is VersionStatus.Launched or VersionStatus.Active)
                    {
                        v.Status = VersionStatus.Closing;
                        snChanged = true;
                    }
                }
            }

            if (BZInstalledVersionsList.ItemsSource is IEnumerable<BZInstalledVersion> bzVersions)
            {
                foreach (var v in bzVersions)
                {
                    if (v.Status is VersionStatus.Launched or VersionStatus.Active)
                    {
                        v.Status = VersionStatus.Closing;
                        bzChanged = true;
                    }
                }
            }

            if (snChanged) InstalledVersionsList.Items.Refresh();
            if (bzChanged) BZInstalledVersionsList.Items.Refresh();
        }

        private void ClearClosingStatus()
        {
            bool snChanged = false;
            bool bzChanged = false;

            if (InstalledVersionsList.ItemsSource is IEnumerable<InstalledVersion> snVersions)
            {
                foreach (var v in snVersions)
                {
                    if (v.Status == VersionStatus.Closing)
                    {
                        v.Status = VersionStatus.Idle;
                        snChanged = true;
                    }
                }
            }

            if (BZInstalledVersionsList.ItemsSource is IEnumerable<BZInstalledVersion> bzVersions)
            {
                foreach (var v in bzVersions)
                {
                    if (v.Status == VersionStatus.Closing)
                    {
                        v.Status = VersionStatus.Idle;
                        bzChanged = true;
                    }
                }
            }

            if (snChanged) InstalledVersionsList.Items.Refresh();
            if (bzChanged) BZInstalledVersionsList.Items.Refresh();
        }

        private static bool IsProcessRunning(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (var proc in processes)
                {
                    if (!proc.HasExited)
                        return true;
                }

                return false;
            }
            finally
            {
                foreach (var proc in processes)
                    proc.Dispose();
            }
        }

        private void StartStatusRefreshTimer()
        {
            _statusRefreshTimer?.Stop();
            _statusRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _statusRefreshTimer.Tick += (_, _) => RefreshRunningStatusIndicators();
            _statusRefreshTimer.Start();
        }

        private void RefreshRunningStatusIndicators()
        {
            bool snRunning = IsProcessRunning("Subnautica");
            bool bzRunning = IsProcessRunning("SubnauticaZero");
            bool snChanged = false;
            bool bzChanged = false;

            if (InstalledVersionsList.ItemsSource is IEnumerable<InstalledVersion> snVersions)
            {
                foreach (var v in snVersions)
                {
                    if (v.Status is VersionStatus.Switching or VersionStatus.Launching)
                        continue;
                    if (v.Status == VersionStatus.Closing && snRunning)
                        continue;

                    string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                    string active = Path.Combine(common, SnActiveFolder);
                    VersionStatus next = PathsAreEqual(v.HomeFolder, active)
                        ? (snRunning ? VersionStatus.Launched : VersionStatus.Active)
                        : VersionStatus.Idle;

                    if (v.Status != next)
                    {
                        v.Status = next;
                        snChanged = true;
                    }
                }
            }

            if (BZInstalledVersionsList.ItemsSource is IEnumerable<BZInstalledVersion> bzVersions)
            {
                foreach (var v in bzVersions)
                {
                    if (v.Status is VersionStatus.Switching or VersionStatus.Launching)
                        continue;
                    if (v.Status == VersionStatus.Closing && bzRunning)
                        continue;

                    string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                    string active = Path.Combine(common, BzActiveFolder);
                    VersionStatus next = PathsAreEqual(v.HomeFolder, active)
                        ? (bzRunning ? VersionStatus.Launched : VersionStatus.Active)
                        : VersionStatus.Idle;

                    if (v.Status != next)
                    {
                        v.Status = next;
                        bzChanged = true;
                    }
                }
            }

            if (snChanged)
                InstalledVersionsList.Items.Refresh();
            if (bzChanged)
                BZInstalledVersionsList.Items.Refresh();

            if (snChanged || bzChanged)
                _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        private static async Task<bool> WaitForLaunchedAsync(Process process, int timeoutMs = 10000)
        {
            int waited = 0;
            const int stepMs = 100;

            while (waited < timeoutMs)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited)
                        return false;

                    if (process.MainWindowHandle != IntPtr.Zero)
                        return true;
                }
                catch
                {
                    return false;
                }

                await Task.Delay(stepMs);
                waited += stepMs;
            }

            try
            {
                process.Refresh();
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void InstalledVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem != null && BZInstalledVersionsList.SelectedItem != null)
                BZInstalledVersionsList.SelectedItem = null;
        }

        private void BZInstalledVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BZInstalledVersionsList.SelectedItem != null && InstalledVersionsList.SelectedItem != null)
                InstalledVersionsList.SelectedItem = null;
        }

        private void InstallVersion_Click(object sender, RoutedEventArgs e)
        {
            new AddVersionWindow { Owner = this }.ShowDialog();
            LoadInstalledVersions();
        }

        private void OpenInstallFolder_Click(object sender, RoutedEventArgs e)
        {
            string? selectedFolder = null;

            if (InstalledVersionsList.SelectedItem is InstalledVersion sn && Directory.Exists(sn.HomeFolder))
                selectedFolder = sn.HomeFolder;
            else if (BZInstalledVersionsList.SelectedItem is BZInstalledVersion bz && Directory.Exists(bz.HomeFolder))
                selectedFolder = bz.HomeFolder;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = selectedFolder ?? AppPaths.SteamCommonPath,
                UseShellExecute = true
            });
        }

        private void EditVersion_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is InstalledVersion snVersion)
            {
                if (Process.GetProcessesByName("Subnautica").Length > 0)
                {
                    MessageBox.Show(
                        "Subnautica is currently running.\n\nClose the game before editing versions.",
                        "Edit Blocked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var win = new EditVersionWindow(snVersion) { Owner = this };
                if (win.ShowDialog() == true)
                    LoadInstalledVersions();

                return;
            }

            if (BZInstalledVersionsList.SelectedItem is BZInstalledVersion bzVersion)
            {
                if (Process.GetProcessesByName("SubnauticaZero").Length > 0)
                {
                    MessageBox.Show(
                        "Below Zero is currently running.\n\nClose the game before editing versions.",
                        "Edit Blocked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var win = new EditVersionWindow(bzVersion) { Owner = this };
                if (win.ShowDialog() == true)
                    LoadInstalledVersions();
            }
        }

        private async Task OnResetHotkeyPressed()
        {
            if (!_macroEnabled)
                return;

            if (_explosionRunning)
            {
                Logger.Warn("[ExplosionReset] Abort requested");

                _explosionCts?.Cancel();
                _explosionCts = null;
                _explosionRunning = false;

                ExplosionResetService.Abort();
                return;
            }

            var runningState = GetRunningGameState();
            if (runningState == RunningGameState.Both)
            {
                Logger.Warn("Reset macro blocked: both Subnautica and Below Zero are running.");
                return;
            }

            if (ResetGamemodeDropdown.SelectedItem is not ComboBoxItem item)
                return;

            var mode = Enum.Parse<GameMode>((string)item.Content);

            if (runningState == RunningGameState.BelowZeroOnly)
            {
                try
                {
                    await BZResetMacroService.RunAsync(mode);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "BZ reset macro failed");
                }

                return;
            }

            if (runningState != RunningGameState.SubnauticaOnly)
                return;

            if (!ExplosionResetSettings.Enabled)
            {
                await ResetMacroService.RunAsync(mode);
                return;
            }

            _explosionCts = new CancellationTokenSource();
            _explosionRunning = true;

            try
            {
                await ExplosionResetService.RunAsync(
                    mode,
                    ExplosionResetSettings.Preset,
                    _explosionCts.Token);
            }
            finally
            {
                _explosionRunning = false;
                _explosionCts = null;
            }
        }

        private enum RunningGameState
        {
            None,
            SubnauticaOnly,
            BelowZeroOnly,
            Both
        }

        private static RunningGameState GetRunningGameState()
        {
            bool snRunning = Process.GetProcessesByName("Subnautica").Length > 0;
            bool bzRunning = Process.GetProcessesByName("SubnauticaZero").Length > 0;

            if (snRunning && bzRunning)
                return RunningGameState.Both;

            if (snRunning)
                return RunningGameState.SubnauticaOnly;

            if (bzRunning)
                return RunningGameState.BelowZeroOnly;

            return RunningGameState.None;
        }

        private void ResetMacroToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _macroEnabled = !_macroEnabled;

            UpdateResetMacroVisualState();
            SaveMacroSettings();
            RegisterResetHotkey();
        }

        private void ExplosionDisplayToggle_Click(object sender, RoutedEventArgs e)
        {
            ExplosionResetSettings.OverlayEnabled = !ExplosionResetSettings.OverlayEnabled;
            ExplosionResetSettings.Save();

            ExplosionDisplayToggleButton.Content =
                ExplosionResetSettings.OverlayEnabled ? "Enabled" : "Disabled";

            ExplosionDisplayToggleButton.Background =
                ExplosionResetSettings.OverlayEnabled ? Brushes.Green : Brushes.DarkRed;
        }

        private void ExplosionTrackToggle_Click(object sender, RoutedEventArgs e)
        {
            ExplosionResetSettings.TrackResets = !ExplosionResetSettings.TrackResets;
            ExplosionResetSettings.Save();

            ExplosionTrackToggleButton.Content =
                ExplosionResetSettings.TrackResets ? "Enabled" : "Disabled";

            ExplosionTrackToggleButton.Background =
                ExplosionResetSettings.TrackResets ? Brushes.Green : Brushes.DarkRed;
        }

        private void HardcoreSaveDeleterToggle_Click(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Current.HardcoreSaveDeleterEnabled =
                !LauncherSettings.Current.HardcoreSaveDeleterEnabled;
            LauncherSettings.Save();

            UpdateHardcoreSaveDeleterVisualState();
        }

        private void Subnautica100TrackerToggle_Click(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Current.Subnautica100TrackerEnabled =
                !LauncherSettings.Current.Subnautica100TrackerEnabled;
            LauncherSettings.Save();

            UpdateSubnautica100TrackerVisualState();

            if (LauncherSettings.Current.Subnautica100TrackerEnabled)
                Subnautica100TrackerOverlayController.Start();
            else
                Subnautica100TrackerOverlayController.Stop();
        }

        private void Subnautica100TrackerCustomize_Click(object sender, RoutedEventArgs e)
        {
            var settings = LauncherSettings.Current;
            var window = new Subnautica100TrackerCustomizeWindow(
                settings.Subnautica100TrackerEnabled,
                settings.Subnautica100TrackerSize,
                settings.Subnautica100TrackerUnlockPopupEnabled,
                settings.SpeedrunGamemode,
                settings.SubnauticaBiomeTrackerEnabled,
                settings.SubnauticaBiomeTrackerCycleMode,
                settings.SubnauticaBiomeTrackerScrollSpeed)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            settings.Subnautica100TrackerEnabled = window.TrackerEnabled;
            settings.Subnautica100TrackerSize = window.SelectedSize;
            settings.Subnautica100TrackerUnlockPopupEnabled = window.UnlockPopupEnabled;
            settings.SpeedrunGamemode = window.GamemodeSelection;
            settings.SubnauticaBiomeTrackerEnabled = window.BiomeTrackerEnabled;
            settings.SubnauticaBiomeTrackerCycleMode = window.BiomeCycleMode;
            settings.SubnauticaBiomeTrackerScrollSpeed = window.BiomeScrollSpeed;
            LauncherSettings.Save();

            UpdateSubnautica100TrackerVisualState();
            if (settings.Subnautica100TrackerEnabled)
                Subnautica100TrackerOverlayController.Start();
            else
                Subnautica100TrackerOverlayController.Stop();
        }

        private void SpeedrunTimerCustomize_Click(object sender, RoutedEventArgs e)
        {
            var settings = LauncherSettings.Current;
            var window = new SpeedrunTimerEditWindow(
                settings.SpeedrunTimerEnabled,
                settings.SpeedrunGamemode,
                settings.SpeedrunCategory,
                settings.SpeedrunRunType)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
                return;

            settings.SpeedrunTimerEnabled = window.TimerEnabled;
            settings.SpeedrunGamemode = window.GamemodeSelection;
            settings.SpeedrunCategory = window.CategorySelection;
            settings.SpeedrunRunType = window.RunTypeSelection;
            LauncherSettings.Save();

            if (settings.SpeedrunTimerEnabled)
                SpeedrunTimerController.Start();
            else
                SpeedrunTimerController.Stop();
        }

        private void HardcoreSaveDeleterPurge_Click(object sender, RoutedEventArgs e)
        {
            var win = new HardcoreSaveDeleterWindow { Owner = this };
            if (win.ShowDialog() != true)
                return;

            string[] roots = GetTargetRoots(win.SelectedGame, win.SelectedScope);

            if (roots.Length == 0)
            {
                MessageBox.Show(
                    "No matching game folders were found.",
                    "No Targets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int deleted = HardcoreSaveDeleter.DeleteAllHardcoreSaves(roots);

            MessageBox.Show(
                deleted > 0
                    ? $"Deleted {deleted} Hardcore save folder(s)."
                    : "No Hardcore saves were found.",
                "Hardcore Save Deleter",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private string[] GetTargetRoots(
            HardcoreSaveTargetGame game,
            HardcoreSaveTargetScope scope)
        {
            bool activeOnly = scope == HardcoreSaveTargetScope.ActiveOnly;

            if (game == HardcoreSaveTargetGame.Subnautica)
            {
                return activeOnly
                    ? FindActiveRoots(SnActiveFolder)
                    : VersionLoader.LoadInstalled()
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            if (game == HardcoreSaveTargetGame.BelowZero)
            {
                return activeOnly
                    ? FindActiveRoots(BzActiveFolder)
                    : BZVersionLoader.LoadInstalled()
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            var roots = new List<string>();

            if (activeOnly)
            {
                roots.AddRange(FindActiveRoots(SnActiveFolder));
                roots.AddRange(FindActiveRoots(BzActiveFolder));
            }
            else
            {
                roots.AddRange(VersionLoader.LoadInstalled().Select(v => v.HomeFolder));
                roots.AddRange(BZVersionLoader.LoadInstalled().Select(v => v.HomeFolder));
            }

            return roots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] FindActiveRoots(string activeFolderName)
        {
            return AppPaths.SteamCommonPaths
                .Select(p => Path.Combine(p, activeFolderName))
                .Where(Directory.Exists)
                .ToArray();
        }

        private void RenameOnCloseButton_Click(object sender, RoutedEventArgs e)
        {
            _renameOnCloseEnabled = !_renameOnCloseEnabled;

            RenameOnCloseButton.Content = _renameOnCloseEnabled ? "Enabled" : "Disabled";
            RenameOnCloseButton.Background = _renameOnCloseEnabled ? Brushes.Green : Brushes.DarkRed;

            LauncherSettings.Current.RenameOnCloseEnabled = _renameOnCloseEnabled;
            LauncherSettings.Save();
        }

        private void SetResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            ResetHotkeyBox.Text = "Press a key...";
            PreviewKeyDown -= CaptureResetKey;
            PreviewKeyDown += CaptureResetKey;
        }

        private void CaptureResetKey(object sender, KeyEventArgs e)
        {
            _resetKey = e.Key;
            ResetHotkeyBox.Text = _resetKey.ToString();

            PreviewKeyDown -= CaptureResetKey;

            SaveMacroSettings();
            RegisterResetHotkey();
            e.Handled = true;
        }

        private void StartupModeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingStartupModeSelection)
                return;

            ApplyStartupMode(StartupModeDropdown.SelectedIndex == 1);
        }

        private void ApplyStartupMode(bool startupAsOverlay)
        {
            _overlayStartupMode = startupAsOverlay;
            LauncherSettings.Current.StartupMode =
                _overlayStartupMode ? LauncherStartupMode.Overlay : LauncherStartupMode.Window;
            LauncherSettings.Save();

            SyncStartupModeDropdown();
            RegisterOverlayToggleHotkey();

            if (_overlayStartupMode)
                ShowOverlayWindow();
            else
            {
                _launcherOverlayWindow?.Hide();
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void SetOverlayHotkey_Click(object sender, RoutedEventArgs e)
        {
            _isCapturingOverlayHotkey = true;
            OverlayHotkeyBox.Text = "Press new combo...";

            PreviewKeyDown -= CaptureOverlayHotkey;
            PreviewKeyDown += CaptureOverlayHotkey;
        }

        private void CaptureOverlayHotkey(object sender, KeyEventArgs e)
        {
            if (!_isCapturingOverlayHotkey)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftShift or Key.RightShift or
                Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or
                Key.LWin or Key.RWin)
            {
                return;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None)
                modifiers = ModifierKeys.Control | ModifierKeys.Shift;

            _overlayToggleKey = key;
            _overlayToggleModifiers = modifiers;

            LauncherSettings.Current.OverlayToggleKey = _overlayToggleKey;
            LauncherSettings.Current.OverlayToggleModifiers = _overlayToggleModifiers;
            LauncherSettings.Save();

            UpdateOverlayHotkeyDisplay();
            RegisterOverlayToggleHotkey();

            _isCapturingOverlayHotkey = false;
            PreviewKeyDown -= CaptureOverlayHotkey;
            e.Handled = true;
        }

        private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _syncingOverlayOpacityControl)
                return;

            double value = Math.Clamp(e.NewValue, 0, 1);
            ApplyOverlayOpacity(value);
            LauncherSettings.Current.OverlayPanelOpacity = value;
            LauncherSettings.Save();
        }

        private void SaveMacroSettings()
        {
            LauncherSettings.Current.ResetMacroEnabled = _macroEnabled;
            LauncherSettings.Current.ResetHotkey = _resetKey;
            LauncherSettings.Current.ResetGameMode = GetSelectedGameMode(ResetGamemodeDropdown, GameMode.Survival);
            LauncherSettings.Current.RenameOnCloseEnabled = _renameOnCloseEnabled;
            LauncherSettings.Save();
        }

        private static GameMode GetSelectedGameMode(System.Windows.Controls.ComboBox comboBox, GameMode fallback)
        {
            if (comboBox.SelectedItem is not ComboBoxItem item || item.Content is not string text)
                return fallback;

            return Enum.Parse<GameMode>(text);
        }

        private void LoadMacroSettings()
        {
            LauncherSettings.Load();
            var settings = LauncherSettings.Current;

            _macroEnabled = settings.ResetMacroEnabled;
            _resetKey = settings.ResetHotkey;

            _renameOnCloseEnabled = settings.RenameOnCloseEnabled;

            ResetHotkeyBox.Text = _resetKey.ToString();

            SelectGameMode(ResetGamemodeDropdown, settings.ResetGameMode);

            UpdateResetMacroVisualState();
            RegisterResetHotkey();
        }

        private static void SelectGameMode(System.Windows.Controls.ComboBox comboBox, GameMode mode)
        {
            comboBox.SelectedItem = comboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Content, mode.ToString(), StringComparison.Ordinal));

            if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private void ResetGamemodeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveMacroSettings();
        }

        private static string FormatHotkey(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>(4);

            if (modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");

            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private void LoadOverlayModeSettings()
        {
            var settings = LauncherSettings.Current;

            _overlayStartupMode = settings.StartupMode == LauncherStartupMode.Overlay;
            _overlayToggleKey = settings.OverlayToggleKey == Key.None ? Key.Tab : settings.OverlayToggleKey;
            _overlayToggleModifiers = settings.OverlayToggleModifiers == ModifierKeys.None
                ? (ModifierKeys.Control | ModifierKeys.Shift)
                : settings.OverlayToggleModifiers;

            double overlayOpacity = settings.OverlayPanelOpacity;
            if (double.IsNaN(overlayOpacity) || overlayOpacity < 0 || overlayOpacity > 1)
                overlayOpacity = 0.5;

            _overlayOpacity = overlayOpacity;
            settings.OverlayPanelOpacity = overlayOpacity;
            LauncherSettings.Save();

            SyncStartupModeDropdown();
            UpdateOverlayHotkeyDisplay();
            ApplyOverlayOpacity(overlayOpacity);
            RegisterOverlayToggleHotkey();
        }

        private void SyncStartupModeDropdown()
        {
            _syncingStartupModeSelection = true;
            try
            {
                StartupModeDropdown.SelectedIndex = _overlayStartupMode ? 1 : 0;
            }
            finally
            {
                _syncingStartupModeSelection = false;
            }
        }

        private void UpdateOverlayHotkeyDisplay()
        {
            OverlayHotkeyBox.Text = FormatHotkey(_overlayToggleModifiers, _overlayToggleKey);
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void ApplyOverlayOpacity(double value)
        {
            _overlayOpacity = Math.Clamp(value, 0, 1);
            _syncingOverlayOpacityControl = true;
            try
            {
                OverlayOpacitySlider.Value = _overlayOpacity;
                OverlayOpacityText.Text = $"{(int)Math.Round(_overlayOpacity * 100)}%";
            }
            finally
            {
                _syncingOverlayOpacityControl = false;
            }

            _launcherOverlayWindow?.ApplyOverlayOpacity(_overlayOpacity);
        }

        private void InitializeTrayIcon()
        {
            System.Drawing.Icon? icon = null;
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null && File.Exists(exePath))
                    icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Launcher", null, (_, _) => Dispatcher.InvokeAsync(RestoreFromTray));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Dispatcher.InvokeAsync(Close));

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon ?? System.Drawing.SystemIcons.Application,
                Text = "Subnautica Launcher",
                Visible = true,
                ContextMenuStrip = menu
            };

            _trayIcon.DoubleClick += (_, _) => Dispatcher.InvokeAsync(RestoreFromTray);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        private void RestoreFromTray()
        {
            if (_overlayStartupMode)
            {
                ShowOverlayWindow();
            }
            else
            {
                ShowInTaskbar = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        private void EnsureOverlayWindow()
        {
            if (_launcherOverlayWindow != null)
                return;

            _launcherOverlayWindow = new LauncherOverlayWindow(this);
            _launcherOverlayWindow.ApplyOverlayOpacity(_overlayOpacity);
        }

        private void ShowOverlayWindow()
        {
            EnsureOverlayWindow();
            _launcherOverlayWindow!.ApplyOverlaySizing();
            _launcherOverlayWindow.ApplyOverlayOpacity(_overlayOpacity);
            _launcherOverlayWindow.RefreshFromMain();

            if (!_launcherOverlayWindow.IsVisible)
                _launcherOverlayWindow.Show();
            _launcherOverlayWindow.Activate();

            if (_overlayStartupMode)
                Hide();
        }

        private void ToggleOverlayVisibility()
        {
            if (!_overlayStartupMode)
                return;

            EnsureOverlayWindow();
            if (_launcherOverlayWindow!.IsVisible)
            {
                _launcherOverlayWindow.Hide();
                // In overlay startup mode the main window stays hidden — never surface it via hotkey.
                return;
            }

            ShowOverlayWindow();
        }

        internal IEnumerable<InstalledVersion> GetSubnauticaVersionsForOverlay() =>
            InstalledVersionsList.ItemsSource as IEnumerable<InstalledVersion> ??
            Array.Empty<InstalledVersion>();

        internal IEnumerable<BZInstalledVersion> GetBelowZeroVersionsForOverlay() =>
            BZInstalledVersionsList.ItemsSource as IEnumerable<BZInstalledVersion> ??
            Array.Empty<BZInstalledVersion>();

        internal InstalledVersion? GetSelectedSubnauticaVersionForOverlay() =>
            InstalledVersionsList.SelectedItem as InstalledVersion;

        internal BZInstalledVersion? GetSelectedBelowZeroVersionForOverlay() =>
            BZInstalledVersionsList.SelectedItem as BZInstalledVersion;

        internal void SetSelectedVersionsFromOverlay(InstalledVersion? snVersion, BZInstalledVersion? bzVersion)
        {
            if (snVersion != null)
            {
                InstalledVersionsList.SelectedItem = snVersion;
                BZInstalledVersionsList.SelectedItem = null;
            }

            if (bzVersion != null)
            {
                BZInstalledVersionsList.SelectedItem = bzVersion;
                InstalledVersionsList.SelectedItem = null;
            }
        }

        internal bool IsOverlayStartupModeForOverlay() => _overlayStartupMode;
        internal string GetOverlayHotkeyTextForOverlay() => FormatHotkey(_overlayToggleModifiers, _overlayToggleKey);
        internal double GetOverlayOpacityForOverlay() => _overlayOpacity;
        internal bool IsRenameOnCloseEnabledForOverlay() => _renameOnCloseEnabled;
        internal bool IsExplosionOverlayEnabledForOverlay() => ExplosionResetSettings.OverlayEnabled;
        internal bool IsExplosionTrackingEnabledForOverlay() => ExplosionResetSettings.TrackResets;
        internal bool IsResetMacroEnabledForOverlay() => _macroEnabled;
        internal Key GetResetHotkeyForOverlay() => _resetKey;
        internal GameMode GetResetGameModeForOverlay() => GetSelectedGameMode(ResetGamemodeDropdown, LauncherSettings.Current.ResetGameMode);
        internal bool IsExplosionResetEnabledForOverlay() => ExplosionResetSettings.Enabled;
        internal ExplosionResetPreset GetExplosionPresetForOverlay() => ExplosionResetSettings.Preset;
        internal bool IsHardcoreSaveDeleterEnabledForOverlay() => LauncherSettings.Current.HardcoreSaveDeleterEnabled;
        internal string GetBackgroundPresetForOverlay() => LauncherSettings.Current.BackgroundPreset;

        internal void SetStartupModeFromOverlay(bool startupAsOverlay)
        {
            ApplyStartupMode(startupAsOverlay);
        }

        internal void SetBackgroundPresetFromOverlay(string preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
                return;

            LauncherSettings.Current.BackgroundPreset = preset;
            LauncherSettings.Save();
            ApplyBackground(preset);
            SyncThemeDropdown(preset);
            _launcherOverlayWindow?.RefreshFromMain();
        }

        internal void ChooseCustomBackgroundFromOverlay()
        {
            ChooseCustomBackground_Click(this, new RoutedEventArgs());
            _launcherOverlayWindow?.RefreshFromMain();
        }

        internal void SetOverlayHotkeyFromOverlay(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None)
                return;

            if (modifiers == ModifierKeys.None)
                modifiers = ModifierKeys.Control | ModifierKeys.Shift;

            _overlayToggleKey = key;
            _overlayToggleModifiers = modifiers;

            LauncherSettings.Current.OverlayToggleKey = _overlayToggleKey;
            LauncherSettings.Current.OverlayToggleModifiers = _overlayToggleModifiers;
            LauncherSettings.Save();

            UpdateOverlayHotkeyDisplay();
            RegisterOverlayToggleHotkey();
            _launcherOverlayWindow?.RefreshFromMain();
        }

        internal void SetOverlayOpacityFromOverlay(double value)
        {
            value = Math.Clamp(value, 0, 1);
            ApplyOverlayOpacity(value);
            LauncherSettings.Current.OverlayPanelOpacity = value;
            LauncherSettings.Save();
        }

        internal void SetResetHotkeyFromOverlay(Key key)
        {
            if (key == Key.None)
                return;

            _resetKey = key;
            ResetHotkeyBox.Text = _resetKey.ToString();
            SaveMacroSettings();
            RegisterResetHotkey();
        }

        internal void SetResetGameModeFromOverlay(GameMode mode)
        {
            SelectGameMode(ResetGamemodeDropdown, mode);
            SaveMacroSettings();
        }

        internal void SetExplosionPresetFromOverlay(ExplosionResetPreset preset)
        {
            ExplosionResetSettings.Preset = preset;
            ExplosionResetSettings.Save();

            ExplosionPresetDropdown.SelectedItem = ExplosionPresetDropdown.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Tag == preset.ToString());
        }

        internal void LaunchSelectedFromOverlay() => Launch_Click(this, new RoutedEventArgs());
        internal void AddVersionFromOverlay() => InstallVersion_Click(this, new RoutedEventArgs());
        internal void EditVersionFromOverlay() => EditVersion_Click(this, new RoutedEventArgs());
        internal void OpenInstallFolderFromOverlay() => OpenInstallFolder_Click(this, new RoutedEventArgs());

        internal async Task CloseGameFromOverlayAsync()
        {
            SetClosingOnActiveVersions();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
            await LaunchCoordinator.CloseAllGameProcessesAsync();
            ClearClosingStatus();
            RefreshRunningStatusIndicators();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void ExitLauncherFromOverlay()
        {
            Close();
        }

        internal void ToggleRenameOnCloseFromOverlay() => RenameOnCloseButton_Click(this, new RoutedEventArgs());
        internal void ToggleExplosionOverlayFromOverlay() => ExplosionDisplayToggle_Click(this, new RoutedEventArgs());
        internal void ToggleExplosionTrackingFromOverlay() => ExplosionTrackToggle_Click(this, new RoutedEventArgs());
        internal void ToggleResetMacroFromOverlay() => ResetMacroToggleButton_Click(this, new RoutedEventArgs());
        internal void ToggleExplosionResetFromOverlay() => ExplosionResetToggle_Click(this, new RoutedEventArgs());
        internal void ToggleHardcoreSaveDeleterFromOverlay() => HardcoreSaveDeleterToggle_Click(this, new RoutedEventArgs());
        internal void OpenTrackerCustomizeFromOverlay() => Subnautica100TrackerCustomize_Click(this, new RoutedEventArgs());
        internal void OpenSpeedrunTimerCustomizeFromOverlay() => SpeedrunTimerCustomize_Click(this, new RoutedEventArgs());
        internal void OpenHardcorePurgeFromOverlay() => HardcoreSaveDeleterPurge_Click(this, new RoutedEventArgs());
        internal void OpenGitHubFromOverlay() => OpenGitHub_Click(this, new RoutedEventArgs());
        internal void OpenYouTubeFromOverlay() => OpenYouTube_Click(this, new RoutedEventArgs());
        internal void OpenDiscordFromOverlay() => OpenDiscord_Click(this, new RoutedEventArgs());

        private void ShowView(UIElement view)
        {
            InstallsView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            ToolsView.Visibility = Visibility.Collapsed;
            InfoView.Visibility = Visibility.Collapsed;

            view.Visibility = Visibility.Visible;
            LaunchButton.Visibility = view == InstallsView ? Visibility.Visible : Visibility.Hidden;
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/ItsFrostyYo/Subnautica-Launcher/releases/tag/v1.0.6",
                UseShellExecute = true
            });
        }

        private void OpenYouTube_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.youtube.com/@ItsFrostiSR",
                UseShellExecute = true
            });
        }

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.com/invite/yfNYgBDcmC",
                UseShellExecute = true
            });
        }

        private void InstallsTab_Click(object sender, RoutedEventArgs e)
        {
            ShowView(InstallsView);
        }

        private void SettingsTab_Click(object sender, RoutedEventArgs e)
        {
            ShowView(SettingsView);
        }

        private void ToolsTab_Click(object sender, RoutedEventArgs e)
        {
            ShowView(ToolsView);
        }

        private void InfoTab_Click(object sender, RoutedEventArgs e)
        {
            ShowView(InfoView);
            BuildUpdatesView();
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            await Task.Yield();
            Logger.Log("Launcher is now closing");
            _statusRefreshTimer?.Stop();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            ExplosionResetDisplayController.ForceClose();
            Subnautica100TrackerOverlayController.Stop();
            SpeedrunTimerController.Stop();
            DebugTelemetryController.Stop();
            GameEventDocumenter.Stop();

            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyIdReset);
            UnregisterHotKey(handle, HotkeyIdOverlayToggle);

            if (_launcherOverlayWindow != null)
            {
                _launcherOverlayWindow.AllowClose();
                _launcherOverlayWindow.Close();
                _launcherOverlayWindow = null;
            }

            if (!_renameOnCloseEnabled)
            {
                try
                {
                    var commonPaths = AppPaths.SteamCommonPaths;
                    if (commonPaths.Count == 0)
                        commonPaths = new List<string> { AppPaths.SteamCommonPath };

                    var restoreTasks = new List<Task>();
                    foreach (var common in commonPaths)
                    {
                        restoreTasks.Add(LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                            common,
                            SnActiveFolder,
                            SnUnmanagedFolder,
                            "Version.info",
                            static (active, info) => InstalledVersion.FromInfo(active, info)?.FolderName));

                        restoreTasks.Add(LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                            common,
                            BzActiveFolder,
                            BzUnmanagedFolder,
                            "BZVersion.info",
                            static (active, info) => BZInstalledVersion.FromInfo(active, info)?.FolderName));
                    }

                    await Task.WhenAll(restoreTasks);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "Failed to restore original folder names");
                }
            }

            Logger.Log("Launcher shutdown complete");
        }
    }
}

