using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.Mods;
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
using System.Windows.Data;
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
        private readonly List<InstalledVersion> _subnauticaInstalledVersions = new();
        private readonly List<BZInstalledVersion> _belowZeroInstalledVersions = new();
        private const string DefaultBg = "Lifepod";
        private static LauncherGameProfile SubnauticaProfile => LauncherGameProfiles.Subnautica;
        private static LauncherGameProfile BelowZeroProfile => LauncherGameProfiles.BelowZero;

        private const int HotkeyIdReset = 9001;
        private const int HotkeyIdOverlayToggle = 9002;
        private const int WM_HOTKEY = 0x0312;

        private Key _resetKey = Key.None;
        private Key _overlayToggleKey = Key.Tab;
        private ModifierKeys _overlayToggleModifiers = ModifierKeys.Control | ModifierKeys.Shift;
        private bool _macroEnabled;
        private const bool RenameFolderSafetyEnabled = true;
        private bool _overlayStartupMode;
        private bool _isCapturingOverlayHotkey;
        private bool _syncingStartupModeSelection;
        private bool _syncingOverlayOpacityControl;
        private double _overlayOpacity = 0.5;
        private DispatcherTimer? _statusRefreshTimer;
        private DispatcherTimer? _updateCheckTimer;
        private LauncherOverlayWindow? _launcherOverlayWindow;
        private bool _updatePromptRunning;
        private string? _directLaunchedSubnauticaFolder;
        private string? _directLaunchedBelowZeroFolder;
        private bool _subnauticaFolderSwapPerformedThisSession = false;
        private bool _belowZeroFolderSwapPerformedThisSession = false;
        private readonly object _versionReloadSync = new();
        private readonly SemaphoreSlim _versionReloadGate = new(1, 1);
        private readonly SemaphoreSlim _backgroundCheckGate = new(1, 1);
        private Task? _versionReloadWorker;
        private int _pendingVersionReloadRequests;
        private bool _pendingVersionReloadRepair;
        private bool _startupStagesCompleted;
        private readonly RuntimeServiceCoordinator _runtimeServices;
        private readonly BackgroundCheckCoordinator _backgroundChecks =
            new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

        private static CancellationTokenSource? _explosionCts;
        private static bool _explosionRunning;

        private System.Windows.Forms.NotifyIcon? _trayIcon;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public MainWindow()
        {
            Logger.Log("MainWindow constructor");

            InitializeComponent();
            _runtimeServices = new RuntimeServiceCoordinator(StartStatusRefreshTimer, StopStatusRefreshTimer);
            Subnautica100TrackerOverlayController.WarmupCaptureWindow();
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

            TryExcludeMainWindowFromCapture();

            RegisterResetHotkey();
            RegisterOverlayToggleHotkey();
        }

        private void TryExcludeMainWindowFromCapture()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                    SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            }
            catch
            {
                // Best-effort: unsupported OS/driver combinations can fail here.
            }
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

                var setup = new SetupWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                bool? result = setup.ShowDialog();

                if (result != true)
                {
                    Logger.Warn("Setup cancelled, shutting down");
                    Application.Current.Shutdown();
                    return;
                }
            }

            Directory.CreateDirectory(AppPaths.DataPath);
            OldRemover.Run();
            LauncherSettings.Load();
            ApplyInitialUiState();
            ShowView(InstallsView);

            if (_overlayStartupMode)
                ShowOverlayWindow();

            await Dispatcher.Yield(DispatcherPriority.Background);
            _ = RunDeferredStartupStagesAsync();
        }

        private void ApplyInitialUiState()
        {
            InitializeTrayIcon();
            LoadOverlayModeSettings();
            ApplyConfiguredBackground();
            LoadMacroSettings();
            ApplyExplosionResetVisualState();
            UpdateHardcoreSaveDeleterVisualState();
            UpdateSubnautica100TrackerVisualState();
            UpdateSidebarState();
        }

        private void ApplyConfiguredBackground()
        {
            var settings = LauncherSettings.Current;
            string bg = settings.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
            {
                bg = DefaultBg;
                settings.BackgroundPreset = bg;
                LauncherSettings.Save();
            }

            ApplyBackground(bg);
            SyncThemeDropdown(bg);
        }

        private void ApplyExplosionResetVisualState()
        {
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
        }

        private async Task RunDeferredStartupStagesAsync()
        {
            var startupTimer = Stopwatch.StartNew();

            try
            {
                Logger.Log("[Startup] Stage 2: scanning installed versions");
                await LoadInstalledVersionsAsync(repairMetadata: true);
                Logger.Log($"[Startup] Stage 2 complete in {startupTimer.ElapsedMilliseconds}ms");

                Logger.Log("[Startup] Stage 3: starting runtime services");
                StartRuntimeServices();
                Logger.Log($"[Startup] Stage 3 complete in {startupTimer.ElapsedMilliseconds}ms");

                Logger.Log("[Startup] Stage 4: verifying runtime tools");
                await NewInstaller.RunAsync();
                _startupStagesCompleted = true;
                StartUpdateCheckTimer();
                await RunBackgroundChecksIfIdleAsync(forceLauncherCheck: true, forceModCheck: true);

                Logger.Log($"Startup complete in {startupTimer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Deferred startup failed");
            }
        }

        private void StartRuntimeServices()
        {
            _runtimeServices.Start(
                LauncherSettings.Current.Subnautica100TrackerEnabled,
                LauncherSettings.Current.SpeedrunTimerEnabled);
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

        private void UpdateSidebarState()
        {
            UpdateNavButtonState(PlayNavButton, InstallsView.Visibility == Visibility.Visible);
            UpdateNavButtonState(SettingsNavButton, SettingsView.Visibility == Visibility.Visible);
            UpdateNavButtonState(ToolsNavButton, ToolsView.Visibility == Visibility.Visible);
            UpdateNavButtonState(LauncherInfoNavButton, InfoView.Visibility == Visibility.Visible);
            UpdateLaunchButtonState();
        }

        private void UpdateNavButtonState(System.Windows.Controls.Button button, bool isActive)
        {
            button.Tag = isActive ? "Active" : null;
            button.Background = isActive
                ? (System.Windows.Media.Brush)FindResource("MainShellSidebarActiveBrush")
                : Brushes.Transparent;
        }

        private void UpdateLaunchButtonState()
        {
            bool hasSelection = false;
            string selectionText = "Select a version to enable launch.";
            System.Windows.Media.Brush selectionBrush = Brushes.White;
            string statusText = "No game activity.";
            System.Windows.Media.Brush statusBrush = Brushes.White;
            bool anyGameRunning = IsAnyGameProcessRunning();

            if (InstalledVersionsList.SelectedItem is InstalledVersion snVersion)
            {
                hasSelection = true;
                selectionText = snVersion.DisplayLabel;
                selectionBrush = GetStatusBrush(snVersion.Status);
                statusText = BuildSidebarStatusText(snVersion.Status, snVersion.DisplayLabel);
                statusBrush = GetStatusBrush(snVersion.Status);
            }
            else if (BZInstalledVersionsList.SelectedItem is BZInstalledVersion bzVersion)
            {
                hasSelection = true;
                selectionText = bzVersion.DisplayLabel;
                selectionBrush = GetStatusBrush(bzVersion.Status);
            }

            InstalledVersion? statusVersion = GetMostRelevantStatusVersion();
            if (statusVersion != null)
            {
                statusText = BuildSidebarStatusText(statusVersion.Status, statusVersion.DisplayLabel);
                statusBrush = GetStatusBrush(statusVersion.Status);
            }
            else if (hasSelection)
            {
                statusText = BuildSidebarStatusText(
                    InstalledVersionsList.SelectedItem is InstalledVersion sn ? sn.Status :
                    BZInstalledVersionsList.SelectedItem is BZInstalledVersion bz ? bz.Status :
                    VersionStatus.Idle,
                    selectionText);
                statusBrush = selectionBrush;
            }

            LaunchButton.IsEnabled = hasSelection || anyGameRunning;
            LaunchButton.Content = anyGameRunning ? "Close Game" : "Launch";
            LaunchButton.Background = anyGameRunning
                ? (System.Windows.Media.Brush)FindResource("WarningOrangeBrush")
                : (System.Windows.Media.Brush)FindResource("LaunchAccentBrush");
            SwitchButton.IsEnabled = hasSelection && anyGameRunning;
            SidebarSelectionTextBlock.Text = selectionText;
            SidebarSelectionTextBlock.Foreground = selectionBrush;
            SidebarStatusTextBlock.Text = statusText;
            SidebarStatusTextBlock.Foreground = statusBrush;
        }

        private static string BuildSidebarStatusText(VersionStatus status, string displayLabel)
        {
            if (string.IsNullOrWhiteSpace(displayLabel))
                return "No game activity.";

            return status switch
            {
                VersionStatus.Launched => $"Game Running: {displayLabel}",
                VersionStatus.Launching => $"Launching Game: {displayLabel}",
                VersionStatus.Active => $"Active Version: {displayLabel}",
                VersionStatus.Closing => $"Closing Game: {displayLabel}",
                VersionStatus.Switching => $"Switching Version: {displayLabel}",
                _ => $"Ready: {displayLabel}"
            };
        }

        private InstalledVersion? GetMostRelevantStatusVersion()
        {
            return _subnauticaInstalledVersions
                .Cast<InstalledVersion>()
                .Concat(_belowZeroInstalledVersions)
                .Where(version => version.Status != VersionStatus.Idle)
                .OrderByDescending(version => GetStatusPriority(version.Status))
                .FirstOrDefault();
        }

        private static int GetStatusPriority(VersionStatus status)
        {
            return status switch
            {
                VersionStatus.Launching => 5,
                VersionStatus.Launched => 4,
                VersionStatus.Closing => 3,
                VersionStatus.Switching => 2,
                VersionStatus.Active => 1,
                _ => 0
            };
        }

        private static bool IsAnyGameProcessRunning() => GameProcessMonitor.GetSnapshot().AnyRunning;

        private static System.Windows.Media.Brush GetStatusBrush(VersionStatus status)
        {
            return status switch
            {
                VersionStatus.Active => Brushes.LimeGreen,
                VersionStatus.Launched => Brushes.Red,
                VersionStatus.Launching => Brushes.Orange,
                VersionStatus.Switching => Brushes.Yellow,
                VersionStatus.Closing => Brushes.OrangeRed,
                _ => Brushes.White
            };
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
            if (_overlayStartupMode)
            {
                ShowInTaskbar = true;
                WindowState = WindowState.Minimized;
                return;
            }

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

        private async Task CheckForModUpdatesIfIdleAsync()
        {
            _backgroundChecks.MarkModCheckStarted();

            try
            {
                await ModCatalog.RefreshAsync();
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Mod catalog refresh failed on startup");
                return;
            }

            var updates = await ModUpdateService.GetAvailableUpdatesAsync(_subnauticaInstalledVersions, _belowZeroInstalledVersions);
            if (updates.Count == 0 || !CanPromptForUpdate())
                return;

            string summary = updates.Count == 1
                ? $"{updates[0].Mod.DisplayName} update {updates[0].InstalledVersion} -> {updates[0].LatestVersion} is available for {updates[0].Version.DisplayName}.{Environment.NewLine}{Environment.NewLine}Update now?"
                : $"{updates.Count} mod updates are available.{Environment.NewLine}{Environment.NewLine}Update them now?";

            MessageBoxResult choice = MessageBox.Show(
                summary,
                "Mod Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (choice != MessageBoxResult.Yes)
                return;

            await ModUpdateService.ApplyUpdatesAsync(
                updates,
                (title, installAction) =>
                {
                    var window = new DepotDownloaderInstallWindow(title, installAction);
                    return DialogWindowHelper.ShowDialog(this, window);
                });

            LoadInstalledVersions(repairMetadata: true);
        }

        private void StartUpdateCheckTimer()
        {
            _updateCheckTimer?.Stop();
            _updateCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _updateCheckTimer.Tick += async (_, _) => await RunBackgroundChecksIfIdleAsync();
            _updateCheckTimer.Start();
        }

        private async Task CheckForUpdatesIfIdleAsync(bool startupPrompt)
        {
            if (!startupPrompt && _backgroundChecks.DeferLauncherPromptsUntilRestart)
                return;

            if (!CanPromptForUpdate())
                return;

            _backgroundChecks.MarkLauncherCheckStarted();

            UpdateInfo? update;
            try
            {
                update = await UpdateChecker.CheckForUpdateAsync();
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, startupPrompt ? "Startup update check failed" : "Background update check failed");
                return;
            }

            if (update == null || !CanPromptForUpdate())
                return;

            _updatePromptRunning = true;
            try
            {
                MessageBoxResult choice = MessageBox.Show(
                    $"Launcher update v{update.Version} is available.{Environment.NewLine}{Environment.NewLine}Update now?",
                    "Launcher Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (choice != MessageBoxResult.Yes)
                {
                    _backgroundChecks.DeferLauncherPrompts();
                    return;
                }

                await RunUpdateAsync(update);
            }
            finally
            {
                _updatePromptRunning = false;
            }
        }

        private async Task RunUpdateAsync(UpdateInfo update)
        {
            UpdateProgressWindow? progressWindow = null;

            try
            {
                progressWindow = new UpdateProgressWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
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

        private async Task RunBackgroundChecksIfIdleAsync(bool forceLauncherCheck = false, bool forceModCheck = false)
        {
            if (!await _backgroundCheckGate.WaitAsync(0))
                return;

            try
            {
                if (forceLauncherCheck || ShouldRunLauncherUpdateCheck())
                    await CheckForUpdatesIfIdleAsync(startupPrompt: forceLauncherCheck);

                if (forceModCheck || ShouldRunModUpdateCheck())
                    await CheckForModUpdatesIfIdleAsync();
            }
            finally
            {
                _backgroundCheckGate.Release();
            }
        }

        private bool ShouldRunLauncherUpdateCheck()
        {
            return _backgroundChecks.ShouldRunLauncherUpdateCheck(CanPromptForUpdate());
        }

        private bool ShouldRunModUpdateCheck()
        {
            return _backgroundChecks.ShouldRunModUpdateCheck(CanPromptForUpdate());
        }

        private bool CanPromptForUpdate()
        {
            if (_updatePromptRunning || !_startupStagesCompleted || !IsLoaded || !IsVisible || !IsEnabled)
                return false;

            if (IsAnyGameProcessRunning())
                return false;

            return OwnedWindows.Cast<Window>().All(window => !window.IsVisible);
        }

        private void NotifyActionCompleted(bool reloadVersions = true)
        {
            if (reloadVersions)
                LoadInstalledVersions(repairMetadata: true);
            else
            {
                UpdateSidebarState();
                _ = EnsureSteamVisibleSubnauticaFolderAndRefreshAsync();
            }

            _ = RunBackgroundChecksIfIdleAsync();
        }

        private async Task EnsureSteamVisibleSubnauticaFolderAndRefreshAsync()
        {
            if (await EnsureSteamVisibleSubnauticaFolderAsync(_subnauticaInstalledVersions))
                LoadInstalledVersions(repairMetadata: false);
        }

        private async Task<bool> EnsureSteamVisibleSubnauticaFolderAsync(IEnumerable<InstalledVersion> allInstalled)
        {
            if (!RenameFolderSafetyEnabled || IsProcessRunning(SubnauticaProfile.ProcessName))
                return false;

            bool changed = false;
            var commonPaths = AppPaths.SteamCommonPaths;
            if (commonPaths.Count == 0)
                commonPaths = new List<string> { AppPaths.SteamCommonPath };

            foreach (string common in commonPaths)
            {
                string activePath = Path.Combine(common, SubnauticaProfile.ActiveFolderName);
                if (HasUsableSubnauticaExecutable(activePath))
                {
                    Logger.Log($"[SteamFolderPolicy] Keeping existing Steam-visible Subnautica folder '{activePath}'.");
                    continue;
                }

                var candidates = allInstalled
                    .Where(v =>
                        string.Equals(AppPaths.GetSteamCommonPathFor(v.HomeFolder), common, StringComparison.OrdinalIgnoreCase) &&
                        !PathsAreEqual(v.HomeFolder, activePath) &&
                        HasUsableSubnauticaExecutable(v.HomeFolder))
                    .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (InstalledVersion candidate in candidates)
                {
                    try
                    {
                        Logger.Log($"[SteamFolderPolicy] No usable '{SubnauticaProfile.ActiveFolderName}' folder exists. Promoting '{candidate.HomeFolder}' to '{activePath}'.");
                        await LaunchCoordinator.MoveFolderWithRetryAsync(candidate.HomeFolder, activePath, timeoutMs: 4000);
                        _subnauticaFolderSwapPerformedThisSession = true;
                        changed = true;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.Warn($"[SteamFolderPolicy] Failed to promote '{candidate.HomeFolder}' to '{activePath}'. Error='{ex.Message}'");
                    }
                }
            }

            return changed;
        }

        private static bool HasUsableSubnauticaExecutable(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(folderPath) &&
                   Directory.Exists(folderPath) &&
                   File.Exists(Path.Combine(folderPath, "Subnautica.exe"));
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
            NotifyActionCompleted();
        }

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (IsAnyGameProcessRunning())
            {
                await CloseRunningGamesAsync();
                return;
            }

            await LaunchSelectedVersionAsync();
        }

        private async void SwitchButton_Click(object sender, RoutedEventArgs e)
        {
            await LaunchSelectedVersionAsync();
        }

        private async Task LaunchSelectedVersionAsync()
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
            await CloseRunningGamesAsync();
        }

        private async Task CloseRunningGamesAsync()
        {
            PauseFolderSwitchServices();
            try
            {
                SetClosingOnActiveVersions();
                UpdateSidebarState();
                _launcherOverlayWindow?.RefreshVersionStatusOnly();
                await LaunchCoordinator.CloseAllGameProcessesAsync();
                ClearTrackedDirectLaunchFoldersIfGamesClosed();
                ClearClosingStatus();
                RefreshRunningStatusIndicators();
                UpdateSidebarState();
                _launcherOverlayWindow?.RefreshVersionStatusOnly();
                _ = RunBackgroundChecksIfIdleAsync();
            }
            finally
            {
                ResumeFolderSwitchServices();
            }
        }

        private async Task LaunchSubnauticaVersionAsync(InstalledVersion target)
        {
            LauncherGameProfile profile = SubnauticaProfile;
            string common = AppPaths.GetSteamCommonPathFor(target.HomeFolder);
            string activePath = Path.Combine(common, profile.ActiveFolderName);
            string launchFolder = target.HomeFolder;
            string targetExe = Path.Combine(launchFolder, profile.ExecutableName);

            PauseFolderSwitchServices();
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

                if (isAlreadyActive)
                {
                    launchFolder = activePath;
                    targetExe = Path.Combine(launchFolder, profile.ExecutableName);
                    _directLaunchedSubnauticaFolder = null;
                }
                else
                {
                    Logger.Log($"[DirectLaunch] Launching Subnautica directly from '{target.HomeFolder}' without renaming the Steam folder.");
                    _directLaunchedSubnauticaFolder = target.HomeFolder;
                }

                if (!File.Exists(targetExe))
                    throw new FileNotFoundException($"{profile.ExecutableName} not found in the selected version folder.", targetExe);

                profile.EnsureSteamAppIdFile(launchFolder);

                SetStatus(target, VersionStatus.Launching);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = launchFolder,
                    UseShellExecute = false
                });

                if (process == null)
                    throw new InvalidOperationException($"Failed to launch {profile.DisplayName}.");

                bool launched = await WaitForLaunchedAsync(process);
                SetStatus(target, launched ? VersionStatus.Launched : VersionStatus.Active);
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
            finally
            {
                ResumeFolderSwitchServices();
            }
        }

        private async Task LaunchBelowZeroVersionAsync(BZInstalledVersion target)
        {
            LauncherGameProfile profile = BelowZeroProfile;
            string common = AppPaths.GetSteamCommonPathFor(target.HomeFolder);
            string activePath = Path.Combine(common, profile.ActiveFolderName);
            string launchFolder = target.HomeFolder;
            string targetExe = Path.Combine(launchFolder, profile.ExecutableName);

            PauseFolderSwitchServices();
            try
            {
                bool isAlreadyActive =
                    Directory.Exists(activePath) &&
                    PathsAreEqual(target.HomeFolder, activePath);

                SetStatus(target, VersionStatus.Switching);

                bool wasRunning = await LaunchCoordinator.CloseAllGameProcessesAsync();

                if (wasRunning)
                    await Task.Delay(1000);

                if (isAlreadyActive)
                {
                    launchFolder = activePath;
                    targetExe = Path.Combine(launchFolder, profile.ExecutableName);
                    _directLaunchedBelowZeroFolder = null;
                }
                else
                {
                    Logger.Log($"[DirectLaunch] Launching Below Zero directly from '{target.HomeFolder}' without renaming the Steam folder.");
                    _directLaunchedBelowZeroFolder = target.HomeFolder;
                }

                if (!File.Exists(targetExe))
                    throw new FileNotFoundException($"{profile.ExecutableName} not found in the selected version folder.", targetExe);

                profile.EnsureSteamAppIdFile(launchFolder);

                SetStatus(target, VersionStatus.Launching);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = launchFolder,
                    UseShellExecute = false
                });

                if (process == null)
                    throw new InvalidOperationException($"Failed to launch {profile.DisplayName}.");

                bool launched = await WaitForLaunchedAsync(process);
                SetStatus(target, launched ? VersionStatus.Launched : VersionStatus.Active);
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
            finally
            {
                ResumeFolderSwitchServices();
            }
        }

        private static bool PathsAreEqual(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private void LoadInstalledVersions(bool repairMetadata = false)
        {
            RequestInstalledVersionReload(repairMetadata);
        }

        private Task LoadInstalledVersionsAsync(bool repairMetadata = false)
        {
            RequestInstalledVersionReload(repairMetadata);
            lock (_versionReloadSync)
            {
                return _versionReloadWorker ?? Task.CompletedTask;
            }
        }

        private void RequestInstalledVersionReload(bool repairMetadata = false)
        {
            Interlocked.Increment(ref _pendingVersionReloadRequests);

            if (repairMetadata)
                _pendingVersionReloadRepair = true;

            lock (_versionReloadSync)
            {
                _versionReloadWorker ??= ProcessInstalledVersionReloadQueueAsync();
            }
        }

        private async Task ProcessInstalledVersionReloadQueueAsync()
        {
            await _versionReloadGate.WaitAsync();
            try
            {
                while (Interlocked.Exchange(ref _pendingVersionReloadRequests, 0) > 0)
                {
                    (string? selectedSnFolder, string? selectedBzFolder) = await Dispatcher.InvokeAsync(() => (
                        (InstalledVersionsList.SelectedItem as InstalledVersion)?.HomeFolder,
                        (BZInstalledVersionsList.SelectedItem as BZInstalledVersion)?.HomeFolder));

                    bool repairMetadata = _pendingVersionReloadRepair;
                    _pendingVersionReloadRepair = false;

                    InstalledVersionScanSnapshot snapshot = await InstalledVersionScanService.ScanAsync(repairMetadata);
                    if (await EnsureSteamVisibleSubnauticaFolderAsync(snapshot.SubnauticaVersions))
                        snapshot = await InstalledVersionScanService.ScanAsync(repairMetadata: false);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ApplyInstalledVersionSnapshot(snapshot, selectedSnFolder, selectedBzFolder);
                    });
                }
            }
            finally
            {
                _versionReloadGate.Release();
                lock (_versionReloadSync)
                {
                    _versionReloadWorker = null;
                    if (Volatile.Read(ref _pendingVersionReloadRequests) > 0)
                        _versionReloadWorker = ProcessInstalledVersionReloadQueueAsync();
                }
            }
        }

        private void ApplyInstalledVersionSnapshot(
            InstalledVersionScanSnapshot snapshot,
            string? selectedSnFolder,
            string? selectedBzFolder)
        {
            GameProcessMonitor.RefreshNow();
            GameProcessSnapshot processSnapshot = GameProcessMonitor.GetSnapshot();
            string? runningSnFolder = processSnapshot.Subnautica.FolderPath ?? _directLaunchedSubnauticaFolder;
            string? runningBzFolder = processSnapshot.BelowZero.FolderPath ?? _directLaunchedBelowZeroFolder;

            _subnauticaInstalledVersions.Clear();
            _subnauticaInstalledVersions.AddRange(snapshot.SubnauticaVersions);
            foreach (var v in _subnauticaInstalledVersions)
            {
                string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                string active = Path.Combine(common, SubnauticaProfile.ActiveFolderName);
                v.Status = GetVersionStatus(v.HomeFolder, active, runningSnFolder);
            }

            _belowZeroInstalledVersions.Clear();
            _belowZeroInstalledVersions.AddRange(snapshot.BelowZeroVersions);
            foreach (var v in _belowZeroInstalledVersions)
            {
                string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                string active = Path.Combine(common, BelowZeroProfile.ActiveFolderName);
                v.Status = GetVersionStatus(v.HomeFolder, active, runningBzFolder);
            }

            BindGroupedVersions(InstalledVersionsList, _subnauticaInstalledVersions);
            BindGroupedVersions(BZInstalledVersionsList, _belowZeroInstalledVersions);

            if (!string.IsNullOrWhiteSpace(selectedSnFolder))
            {
                InstalledVersionsList.SelectedItem = _subnauticaInstalledVersions
                    .FirstOrDefault(v => PathsAreEqual(v.HomeFolder, selectedSnFolder));
            }

            if (!string.IsNullOrWhiteSpace(selectedBzFolder))
            {
                BZInstalledVersionsList.SelectedItem = _belowZeroInstalledVersions
                    .FirstOrDefault(v => PathsAreEqual(v.HomeFolder, selectedBzFolder));
            }

            RefreshRunningStatusIndicators();
            UpdateSidebarState();
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private static void BindGroupedVersions<TVersion>(System.Windows.Controls.ListBox listBox, List<TVersion> versions)
            where TVersion : InstalledVersion
        {
            listBox.ItemsSource = null;

            var displayItems = versions.Cast<InstalledVersion>().ToList();
            var view = new ListCollectionView(displayItems);
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();
            if (displayItems.Any(v => v.IsModded))
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InstalledVersion.GroupLabel)));
                view.SortDescriptions.Add(new SortDescription(nameof(InstalledVersion.IsModded), ListSortDirection.Descending));
            }
            view.SortDescriptions.Add(new SortDescription(nameof(InstalledVersion.DisplayName), ListSortDirection.Ascending));
            listBox.ItemsSource = view;
            listBox.Items.Refresh();
        }

        private void SetStatus(InstalledVersion version, VersionStatus status)
        {
            if (version is BZInstalledVersion bzVersion)
            {
                SetStatus(bzVersion, status);
                return;
            }

            version.Status = status;
            RefreshVersionStatusUi(refreshSnList: true);
        }

        private void SetStatus(BZInstalledVersion version, VersionStatus status)
        {
            version.Status = status;
            RefreshVersionStatusUi(refreshBzList: true);
        }

        private void SetClosingOnActiveVersions()
        {
            bool snChanged = false;
            bool bzChanged = false;

            if (_subnauticaInstalledVersions.Count > 0)
            {
                foreach (var v in _subnauticaInstalledVersions)
                {
                    if (v.Status is VersionStatus.Launched or VersionStatus.Active)
                    {
                        v.Status = VersionStatus.Closing;
                        snChanged = true;
                    }
                }
            }

            if (_belowZeroInstalledVersions.Count > 0)
            {
                foreach (var v in _belowZeroInstalledVersions)
                {
                    if (v.Status is VersionStatus.Launched or VersionStatus.Active)
                    {
                        v.Status = VersionStatus.Closing;
                        bzChanged = true;
                    }
                }
            }

            if (snChanged || bzChanged)
                RefreshVersionStatusUi(refreshSnList: snChanged, refreshBzList: bzChanged);
        }

        private void ClearClosingStatus()
        {
            bool snChanged = false;
            bool bzChanged = false;

            if (_subnauticaInstalledVersions.Count > 0)
            {
                foreach (var v in _subnauticaInstalledVersions)
                {
                    if (v.Status == VersionStatus.Closing)
                    {
                        v.Status = VersionStatus.Idle;
                        snChanged = true;
                    }
                }
            }

            if (_belowZeroInstalledVersions.Count > 0)
            {
                foreach (var v in _belowZeroInstalledVersions)
                {
                    if (v.Status == VersionStatus.Closing)
                    {
                        v.Status = VersionStatus.Idle;
                        bzChanged = true;
                    }
                }
            }

            if (snChanged || bzChanged)
                RefreshVersionStatusUi(refreshSnList: snChanged, refreshBzList: bzChanged);
        }

        private static bool IsProcessRunning(string processName)
        {
            GameProcessMonitor.RefreshNow();
            return GameProcessMonitor.GetSnapshot().Get(processName).IsRunning;
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

        private void StopStatusRefreshTimer()
        {
            _statusRefreshTimer?.Stop();
        }

        private void PauseFolderSwitchServices()
        {
            _runtimeServices.PauseForFolderSwitch();
        }

        private void ResumeFolderSwitchServices()
        {
            _runtimeServices.ResumeAfterFolderSwitch();
        }

        private void RefreshRunningStatusIndicators()
        {
            GameProcessSnapshot processSnapshot = GameProcessMonitor.GetSnapshot();
            bool snRunning = processSnapshot.Subnautica.IsRunning;
            bool bzRunning = processSnapshot.BelowZero.IsRunning;
            if (!snRunning)
                _directLaunchedSubnauticaFolder = null;
            if (!bzRunning)
                _directLaunchedBelowZeroFolder = null;

            string? runningSnFolder = snRunning ? processSnapshot.Subnautica.FolderPath ?? _directLaunchedSubnauticaFolder : null;
            string? runningBzFolder = bzRunning ? processSnapshot.BelowZero.FolderPath ?? _directLaunchedBelowZeroFolder : null;
            bool snChanged = false;
            bool bzChanged = false;

            if (_subnauticaInstalledVersions.Count > 0)
            {
                foreach (var v in _subnauticaInstalledVersions)
                {
                    if (v.Status is VersionStatus.Switching or VersionStatus.Launching)
                        continue;
                    if (v.Status == VersionStatus.Closing && snRunning)
                        continue;

                    string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                    string active = Path.Combine(common, SubnauticaProfile.ActiveFolderName);
                    VersionStatus next = GetVersionStatus(v.HomeFolder, active, runningSnFolder);

                    if (v.Status != next)
                    {
                        v.Status = next;
                        snChanged = true;
                    }
                }
            }

            if (_belowZeroInstalledVersions.Count > 0)
            {
                foreach (var v in _belowZeroInstalledVersions)
                {
                    if (v.Status is VersionStatus.Switching or VersionStatus.Launching)
                        continue;
                    if (v.Status == VersionStatus.Closing && bzRunning)
                        continue;

                    string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                    string active = Path.Combine(common, BelowZeroProfile.ActiveFolderName);
                    VersionStatus next = GetVersionStatus(v.HomeFolder, active, runningBzFolder);

                    if (v.Status != next)
                    {
                        v.Status = next;
                        bzChanged = true;
                    }
                }
            }

            if (snChanged || bzChanged)
                RefreshVersionStatusUi(refreshSnList: snChanged, refreshBzList: bzChanged);
        }

        private void RefreshVersionStatusUi(
            bool refreshSnList = false,
            bool refreshBzList = false,
            bool refreshOverlayFull = false)
        {
            if (refreshSnList)
                InstalledVersionsList.Items.Refresh();
            if (refreshBzList)
                BZInstalledVersionsList.Items.Refresh();

            UpdateSidebarState();

            if (refreshOverlayFull)
                _launcherOverlayWindow?.RefreshFromMain();
            else
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

        private void ClearTrackedDirectLaunchFoldersIfGamesClosed()
        {
            GameProcessMonitor.RefreshNow();
            GameProcessSnapshot processSnapshot = GameProcessMonitor.GetSnapshot();
            if (!processSnapshot.Subnautica.IsRunning)
                _directLaunchedSubnauticaFolder = null;
            if (!processSnapshot.BelowZero.IsRunning)
                _directLaunchedBelowZeroFolder = null;
        }

        private static VersionStatus GetVersionStatus(string versionFolder, string activeFolder, string? runningFolder)
        {
            if (!string.IsNullOrWhiteSpace(runningFolder))
            {
                return PathsAreEqual(versionFolder, runningFolder)
                    ? VersionStatus.Launched
                    : VersionStatus.Idle;
            }

            return PathsAreEqual(versionFolder, activeFolder)
                ? VersionStatus.Active
                : VersionStatus.Idle;
        }


        private void InstalledVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem != null && BZInstalledVersionsList.SelectedItem != null)
                BZInstalledVersionsList.SelectedItem = null;

            UpdateLaunchButtonState();
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void BZInstalledVersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BZInstalledVersionsList.SelectedItem != null && InstalledVersionsList.SelectedItem != null)
                InstalledVersionsList.SelectedItem = null;

            UpdateLaunchButtonState();
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void InstallVersion_Click(object sender, RoutedEventArgs e)
        {
            DialogWindowHelper.ShowDialog(this, new AddVersionWindow());
            NotifyActionCompleted(reloadVersions: true);
        }

        private void OpenInstallFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenInstallFolderForVersion(
                InstalledVersionsList.SelectedItem as InstalledVersion ??
                BZInstalledVersionsList.SelectedItem as InstalledVersion);
        }

        private void EditVersion_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is InstalledVersion snVersion)
            {
                EditVersionInternal(snVersion, SubnauticaProfile);
                return;
            }

            if (BZInstalledVersionsList.SelectedItem is BZInstalledVersion bzVersion)
            {
                EditVersionInternal(bzVersion, BelowZeroProfile);
            }
        }

        private void OpenVersionFolderRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not InstalledVersion version)
                return;

            if (version is BZInstalledVersion bzVersion)
                BZInstalledVersionsList.SelectedItem = bzVersion;
            else
                InstalledVersionsList.SelectedItem = version;

            OpenInstallFolderForVersion(version);
        }

        private void EditVersionRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not InstalledVersion version)
                return;

            if (version is BZInstalledVersion bzVersion)
            {
                BZInstalledVersionsList.SelectedItem = bzVersion;
                EditVersionInternal(bzVersion, BelowZeroProfile);
                return;
            }

            InstalledVersionsList.SelectedItem = version;
            EditVersionInternal(version, SubnauticaProfile);
        }

        private void OpenInstallFolderForVersion(InstalledVersion? version)
        {
            string selectedFolder = version != null && Directory.Exists(version.HomeFolder)
                ? version.HomeFolder
                : AppPaths.SteamCommonPath;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = selectedFolder,
                UseShellExecute = true
            });
        }

        private void EditVersionInternal(InstalledVersion version, LauncherGameProfile profile)
        {
            if (GameProcessMonitor.GetSnapshot().Get(profile.ProcessName).IsRunning)
            {
                MessageBox.Show(
                    $"{profile.DisplayName} is currently running.\n\nClose the game before editing versions.",
                    "Edit Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Window editWindow = version is BZInstalledVersion bzVersion
                ? new EditVersionWindow(bzVersion)
                : new EditVersionWindow(version);

            DialogWindowHelper.ShowDialog(this, editWindow);
            NotifyActionCompleted(reloadVersions: true);
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
            GameProcessMonitor.RefreshNow();
            GameProcessSnapshot snapshot = GameProcessMonitor.GetSnapshot();
            bool snRunning = snapshot.Subnautica.IsRunning;
            bool bzRunning = snapshot.BelowZero.IsRunning;

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
            NotifyActionCompleted();
        }

        private void ExplosionDisplayToggle_Click(object sender, RoutedEventArgs e)
        {
            ExplosionResetSettings.OverlayEnabled = !ExplosionResetSettings.OverlayEnabled;
            ExplosionResetSettings.Save();

            ExplosionDisplayToggleButton.Content =
                ExplosionResetSettings.OverlayEnabled ? "Enabled" : "Disabled";

            ExplosionDisplayToggleButton.Background =
                ExplosionResetSettings.OverlayEnabled ? Brushes.Green : Brushes.DarkRed;
            NotifyActionCompleted();
        }

        private void ExplosionTrackToggle_Click(object sender, RoutedEventArgs e)
        {
            ExplosionResetSettings.TrackResets = !ExplosionResetSettings.TrackResets;
            ExplosionResetSettings.Save();

            ExplosionTrackToggleButton.Content =
                ExplosionResetSettings.TrackResets ? "Enabled" : "Disabled";

            ExplosionTrackToggleButton.Background =
                ExplosionResetSettings.TrackResets ? Brushes.Green : Brushes.DarkRed;
            NotifyActionCompleted();
        }

        private void HardcoreSaveDeleterToggle_Click(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Current.HardcoreSaveDeleterEnabled =
                !LauncherSettings.Current.HardcoreSaveDeleterEnabled;
            LauncherSettings.Save();

            UpdateHardcoreSaveDeleterVisualState();
            NotifyActionCompleted();
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

            NotifyActionCompleted();
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
                settings.SubnauticaBiomeTrackerScrollSpeed);

            if (DialogWindowHelper.ShowDialog(this, window) != true)
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

            NotifyActionCompleted();
        }

        private void SpeedrunTimerCustomize_Click(object sender, RoutedEventArgs e)
        {
            var settings = LauncherSettings.Current;
            var window = new SpeedrunTimerEditWindow(
                settings.SpeedrunTimerEnabled,
                settings.SpeedrunGamemode,
                settings.SpeedrunCategory,
                settings.SpeedrunRunType);

            if (DialogWindowHelper.ShowDialog(this, window) != true)
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

            NotifyActionCompleted();
        }

        private void HardcoreSaveDeleterPurge_Click(object sender, RoutedEventArgs e)
        {
            var win = new HardcoreSaveDeleterWindow();
            if (DialogWindowHelper.ShowDialog(this, win) != true)
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

            NotifyActionCompleted(reloadVersions: true);
        }

        private string[] GetTargetRoots(
            HardcoreSaveTargetGame game,
            HardcoreSaveTargetScope scope)
        {
            bool activeOnly = scope == HardcoreSaveTargetScope.ActiveOnly;

            if (game == HardcoreSaveTargetGame.Subnautica)
            {
                return activeOnly
                    ? FindActiveRoots(SubnauticaProfile.ActiveFolderName)
                    : _subnauticaInstalledVersions
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            if (game == HardcoreSaveTargetGame.BelowZero)
            {
                return activeOnly
                    ? FindActiveRoots(BelowZeroProfile.ActiveFolderName)
                    : _belowZeroInstalledVersions
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            var roots = new List<string>();

            if (activeOnly)
            {
                roots.AddRange(FindActiveRoots(SubnauticaProfile.ActiveFolderName));
                roots.AddRange(FindActiveRoots(BelowZeroProfile.ActiveFolderName));
            }
            else
            {
                roots.AddRange(_subnauticaInstalledVersions.Select(v => v.HomeFolder));
                roots.AddRange(_belowZeroInstalledVersions.Select(v => v.HomeFolder));
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
            if (WindowState == WindowState.Minimized && !_overlayStartupMode)
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
                KeepMainWindowInTaskbar();
        }

        private void KeepMainWindowInTaskbar()
        {
            ShowInTaskbar = true;
            if (!IsVisible)
                Show();

            if (WindowState != WindowState.Minimized)
                WindowState = WindowState.Minimized;
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
            _subnauticaInstalledVersions;

        internal IEnumerable<BZInstalledVersion> GetBelowZeroVersionsForOverlay() =>
            _belowZeroInstalledVersions;

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

        internal void LaunchSelectedFromOverlay() => _ = LaunchSelectedVersionAsync();
        internal void AddVersionFromOverlay() => InstallVersion_Click(this, new RoutedEventArgs());
        internal void EditVersionFromOverlay() => EditVersion_Click(this, new RoutedEventArgs());
        internal void OpenInstallFolderFromOverlay() => OpenInstallFolder_Click(this, new RoutedEventArgs());

        internal async Task CloseGameFromOverlayAsync()
        {
            await CloseRunningGamesAsync();
        }

        internal void ExitLauncherFromOverlay()
        {
            Close();
        }

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
            LaunchButton.Visibility = Visibility.Visible;
            UpdateSidebarState();
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/ItsFrostyYo/Subnautica-Launcher/releases",
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
            _updateCheckTimer?.Stop();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            ExplosionResetDisplayController.ForceClose();
            _runtimeServices.Stop();

            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyIdReset);
            UnregisterHotKey(handle, HotkeyIdOverlayToggle);

            if (_launcherOverlayWindow != null)
            {
                _launcherOverlayWindow.AllowClose();
                _launcherOverlayWindow.Close();
                _launcherOverlayWindow = null;
            }

            if (RenameFolderSafetyEnabled &&
                (_subnauticaFolderSwapPerformedThisSession || _belowZeroFolderSwapPerformedThisSession))
            {
                try
                {
                    var commonPaths = AppPaths.SteamCommonPaths;
                    if (commonPaths.Count == 0)
                        commonPaths = new List<string> { AppPaths.SteamCommonPath };

                    var restoreTasks = new List<Task>();
                    foreach (var common in commonPaths)
                    {
                        if (_subnauticaFolderSwapPerformedThisSession)
                        {
                            restoreTasks.Add(LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                                common,
                                SubnauticaProfile.ActiveFolderName,
                                SubnauticaProfile.UnmanagedReservedFolderName,
                                "Version.info",
                                static (active, info) => InstalledVersion.FromInfo(active, info)?.FolderName));
                        }

                        if (_belowZeroFolderSwapPerformedThisSession)
                        {
                            restoreTasks.Add(LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                                common,
                                BelowZeroProfile.ActiveFolderName,
                                BelowZeroProfile.UnmanagedReservedFolderName,
                                "BZVersion.info",
                                static (active, info) => BZInstalledVersion.FromInfo(active, info)?.FolderName));
                        }
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
