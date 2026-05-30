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
        private readonly List<InstalledVersion> _subnautica2InstalledVersions = new();
        private const string DefaultBg = "Lifepod";
        private static LauncherGameProfile SubnauticaProfile => LauncherGameProfiles.Subnautica;
        private static LauncherGameProfile BelowZeroProfile => LauncherGameProfiles.BelowZero;
        private static LauncherGameProfile Subnautica2Profile => LauncherGameProfiles.Subnautica2;

        private const int HotkeyIdReset = 9001;
        private const int HotkeyIdOverlayToggle = 9002;
        private const int WM_HOTKEY = 0x0312;

        private Key _resetKey = Key.None;
        private Key _overlayToggleKey = Key.Tab;
        private ModifierKeys _overlayToggleModifiers = ModifierKeys.Control | ModifierKeys.Shift;
        private bool _macroEnabled;
        private const bool RenameFolderSafetyEnabled = true;
        private bool _overlayStartupMode;
        private bool _gameOverlayEnabled;
        private bool _isCapturingOverlayHotkey;
        private bool _syncingOverlayOpacityControl;
        private double _overlayOpacity = 0.5;
        private DispatcherTimer? _statusRefreshTimer;
        private DispatcherTimer? _updateCheckTimer;
        private LauncherOverlayWindow? _launcherOverlayWindow;
        private bool _updatePromptRunning;
        private string? _directLaunchedSubnauticaFolder;
        private string? _directLaunchedBelowZeroFolder;
        private string? _directLaunchedSubnautica2Folder;
        private string? _steamLaunchPinnedSubnauticaFolder;
        private string? _steamLaunchPinnedBelowZeroFolder;
        private string? _steamLaunchPinnedSubnautica2Folder;
        private DateTime _steamLaunchPinnedSubnauticaUntilUtc = DateTime.MinValue;
        private DateTime _steamLaunchPinnedBelowZeroUntilUtc = DateTime.MinValue;
        private DateTime _steamLaunchPinnedSubnautica2UntilUtc = DateTime.MinValue;
        private readonly object _versionReloadSync = new();
        private readonly SemaphoreSlim _versionReloadGate = new(1, 1);
        private readonly SemaphoreSlim _backgroundCheckGate = new(1, 1);
        private Task? _versionReloadWorker;
        private int _pendingVersionReloadRequests;
        private bool _pendingVersionReloadRepair;
        private bool _startupStagesCompleted;
        private bool _syncingExplosionCustomRangeInputs;
        private bool _syncingPlayViewSelectors;
        private bool _syncingToolsTabVisibleGamesUi;
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
        private static readonly TimeSpan SteamLaunchPinGracePeriod = TimeSpan.FromSeconds(90);

        public MainWindow()
        {
            Logger.Log("MainWindow constructor");

            InitializeComponent();
            _runtimeServices = new RuntimeServiceCoordinator(StartStatusRefreshTimer, StopStatusRefreshTimer);
            Subnautica100TrackerOverlayController.WarmupCaptureWindow();
            LauncherBusyCoordinator.BusyStateChanged += LauncherBusyCoordinator_BusyStateChanged;
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

            await Dispatcher.Yield(DispatcherPriority.Background);
            _ = RunDeferredStartupStagesAsync();
        }

        private void ApplyInitialUiState()
        {
            InitializeTrayIcon();
            LoadOverlayModeSettings();
            LoadPlayTabViewSettings();
            LoadToolsTabViewSettings();
            ApplyConfiguredBackground();
            LoadMacroSettings();
            ApplyExplosionResetVisualState();
            UpdateGameOverlayVisualState();
            UpdateForceLaunchWithoutSteamVisualState();
            UpdateHardcoreSaveDeleterVisualState();
            UpdateSubnautica100TrackerVisualState();
            UpdateSidebarState();
        }

        private void LoadPlayTabViewSettings()
        {
            NormalizePlayTabViewSettings();
            ApplyPlayTabViewSettingsToUi();
        }

        private void LoadToolsTabViewSettings()
        {
            NormalizeToolsTabViewSettings();
            RefreshToolsTabGameSelectorUi();
            RefreshToolsTabSectionVisibility();
        }

        private void NormalizePlayTabViewSettings()
        {
            var settings = LauncherSettings.Current;

            if (settings.PlayTabGame1 == settings.PlayTabGame2 &&
                settings.PlayTabGame1 != PlayTabGameViewOption.None)
            {
                settings.PlayTabGame2 = PlayTabGameViewOption.None;
            }
        }

        private void ApplyPlayTabViewSettingsToUi()
        {
            _syncingPlayViewSelectors = true;
            try
            {
                SelectPlayTabGameChoice(Game1ViewDropdown, LauncherSettings.Current.PlayTabGame1);
                SelectPlayTabGameChoice(Game2ViewDropdown, LauncherSettings.Current.PlayTabGame2);
                SelectPlayTabListViewChoice(ListViewDropdown, LauncherSettings.Current.PlayTabListView);
            }
            finally
            {
                _syncingPlayViewSelectors = false;
            }
        }

        private void NormalizeToolsTabViewSettings()
        {
            var settings = LauncherSettings.Current;
            if (settings.ToolsTabVisibleGames is null)
            {
                settings.ToolsTabVisibleGames = new List<ToolsTabGameOption>
                {
                    ToolsTabGameOption.Subnautica,
                    ToolsTabGameOption.BelowZero,
                    ToolsTabGameOption.Subnautica2
                };
                return;
            }

            var normalized = new List<ToolsTabGameOption>();

            foreach (ToolsTabGameOption option in settings.ToolsTabVisibleGames)
            {
                if (!normalized.Contains(option))
                    normalized.Add(option);
            }

            settings.ToolsTabVisibleGames = normalized;
        }

        private void RefreshToolsTabGameSelectorUi()
        {
            _syncingToolsTabVisibleGamesUi = true;
            try
            {
                ToolsVisibleGamesPanel.Children.Clear();

                foreach (ToolsTabGameOption option in LauncherSettings.Current.ToolsTabVisibleGames)
                    ToolsVisibleGamesPanel.Children.Add(CreateToolsGameChip(option));

                ToolsAddGameButton.Visibility = GetAvailableToolsTabGameOptions().Any()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            finally
            {
                _syncingToolsTabVisibleGamesUi = false;
            }
        }

        private Border CreateToolsGameChip(ToolsTabGameOption option)
        {
            var removeButton = new System.Windows.Controls.Button
            {
                Content = "×",
                Width = 28,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(0),
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = Visibility.Visible,
                ToolTip = $"Remove {GetToolsTabGameLabel(option)}"
            };
            removeButton.Click += (_, _) => RemoveToolsTabGame(option);

            var textBlock = new TextBlock
            {
                Text = GetToolsTabGameLabel(option),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };

            var content = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(textBlock);
            content.Children.Add(removeButton);

            var chip = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 30, 42, 65)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 109, 136, 168)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 6, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Child = content
            };

            chip.MouseEnter += (_, _) => removeButton.Opacity = 1.0;
            chip.MouseLeave += (_, _) => removeButton.Opacity = 0.8;
            removeButton.Opacity = 0.8;

            return chip;
        }

        private void RefreshToolsTabSectionVisibility()
        {
            List<ToolsTabGameOption> visibleGames = LauncherSettings.Current.ToolsTabVisibleGames;
            bool showSubnauticaTools = visibleGames.Contains(ToolsTabGameOption.Subnautica);
            bool showBelowZeroTools = visibleGames.Contains(ToolsTabGameOption.BelowZero);
            bool showSubnautica2Tools = visibleGames.Contains(ToolsTabGameOption.Subnautica2);

            bool showResetMacro = showSubnauticaTools || showBelowZeroTools || showSubnautica2Tools;
            bool showExplosionReset = showSubnauticaTools;
            bool showHardcoreSaveDeleter = showSubnauticaTools || showBelowZeroTools;
            bool showTrackersAndTimers = showSubnauticaTools;

            ResetMacroSectionPanel.Visibility = showResetMacro ? Visibility.Visible : Visibility.Collapsed;
            ExplosionResetSectionPanel.Visibility = showExplosionReset ? Visibility.Visible : Visibility.Collapsed;
            ResetExplosionSeparator.Visibility =
                showResetMacro && showExplosionReset ? Visibility.Visible : Visibility.Collapsed;

            HardcoreSaveDeleterSectionPanel.Visibility =
                showHardcoreSaveDeleter ? Visibility.Visible : Visibility.Collapsed;
            TrackersAndTimersSectionPanel.Visibility =
                showTrackersAndTimers ? Visibility.Visible : Visibility.Collapsed;
            HardcoreTrackerSeparator.Visibility =
                showHardcoreSaveDeleter && showTrackersAndTimers ? Visibility.Visible : Visibility.Collapsed;

            bool showLeftColumn = showResetMacro || showExplosionReset;
            bool showRightColumn = showHardcoreSaveDeleter || showTrackersAndTimers;

            LeftToolsColumnBorder.Visibility = showLeftColumn ? Visibility.Visible : Visibility.Collapsed;
            RightToolsColumnBorder.Visibility = showRightColumn ? Visibility.Visible : Visibility.Collapsed;
            ToolsEmptyStateText.Visibility = showLeftColumn || showRightColumn
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private IEnumerable<ToolsTabGameOption> GetAvailableToolsTabGameOptions()
        {
            ToolsTabGameOption[] allOptions =
            [
                ToolsTabGameOption.Subnautica,
                ToolsTabGameOption.BelowZero,
                ToolsTabGameOption.Subnautica2
            ];

            return allOptions.Where(option => !LauncherSettings.Current.ToolsTabVisibleGames.Contains(option));
        }

        private static string GetToolsTabGameLabel(ToolsTabGameOption option)
        {
            return option switch
            {
                ToolsTabGameOption.Subnautica => "Subnautica",
                ToolsTabGameOption.BelowZero => "Below Zero",
                ToolsTabGameOption.Subnautica2 => "Subnautica 2",
                _ => option.ToString()
            };
        }

        private void AddToolsTabGame(ToolsTabGameOption option)
        {
            if (LauncherSettings.Current.ToolsTabVisibleGames.Contains(option))
                return;

            LauncherSettings.Current.ToolsTabVisibleGames.Add(option);
            NormalizeToolsTabViewSettings();
            LauncherSettings.Save();
            RefreshToolsTabGameSelectorUi();
            RefreshToolsTabSectionVisibility();
        }

        private void RemoveToolsTabGame(ToolsTabGameOption option)
        {
            LauncherSettings.Current.ToolsTabVisibleGames.Remove(option);
            NormalizeToolsTabViewSettings();
            LauncherSettings.Save();
            RefreshToolsTabGameSelectorUi();
            RefreshToolsTabSectionVisibility();
        }

        private static void SelectPlayTabGameChoice(System.Windows.Controls.ComboBox comboBox, PlayTabGameViewOption choice)
        {
            comboBox.SelectedItem = comboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, choice.ToString(), StringComparison.Ordinal));
        }

        private static void SelectPlayTabListViewChoice(System.Windows.Controls.ComboBox comboBox, PlayTabListViewMode choice)
        {
            comboBox.SelectedItem = comboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, choice.ToString(), StringComparison.Ordinal));
        }

        private static PlayTabGameViewOption GetPlayTabGameChoice(System.Windows.Controls.ComboBox comboBox, PlayTabGameViewOption fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, out PlayTabGameViewOption parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static PlayTabListViewMode GetPlayTabListViewChoice(System.Windows.Controls.ComboBox comboBox, PlayTabListViewMode fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, out PlayTabListViewMode parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private void LauncherBusyCoordinator_BusyStateChanged(object? sender, bool isBusy)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (isBusy)
                {
                    _runtimeServices.PauseForBusyOperation();
                    return;
                }

                _runtimeServices.ResumeAfterBusyOperation();

                TryStartInstalledVersionReloadWorker();
                await RunBackgroundChecksIfIdleAsync();
            });
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

            RefreshExplosionCustomRangeUi();

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

                if (!LauncherSettings.Current.ForceLaunchWithoutSteam)
                    await CleanupSteamAppIdFilesForAllGamesAsync();

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

        private void UpdateForceLaunchWithoutSteamVisualState()
        {
            bool enabled = LauncherSettings.Current.ForceLaunchWithoutSteam;
            ForceLaunchWithoutSteamToggleButton.Content = enabled ? "Enabled" : "Disabled";
            ForceLaunchWithoutSteamToggleButton.Background = enabled ? Brushes.Green : Brushes.DarkRed;
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
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

            if (InstalledVersionsList.SelectedItem is InstalledVersion leftVersion)
            {
                hasSelection = true;
                selectionText = leftVersion.DisplayLabel;
                selectionBrush = GetStatusBrush(leftVersion.Status);
                statusText = BuildSidebarStatusText(leftVersion.Status, leftVersion.DisplayLabel);
                statusBrush = GetStatusBrush(leftVersion.Status);
            }
            else if (BZInstalledVersionsList.SelectedItem is InstalledVersion rightVersion)
            {
                hasSelection = true;
                selectionText = rightVersion.DisplayLabel;
                selectionBrush = GetStatusBrush(rightVersion.Status);
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
                    InstalledVersionsList.SelectedItem is InstalledVersion left ? left.Status :
                    BZInstalledVersionsList.SelectedItem is InstalledVersion right ? right.Status :
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
                .Concat(_subnautica2InstalledVersions)
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

        private static LauncherGameProfile GetProfileForVersion(InstalledVersion version)
        {
            if (version is BZInstalledVersion)
                return BelowZeroProfile;

            return LauncherGameProfiles.DetectFromFolder(version.HomeFolder) ?? SubnauticaProfile;
        }

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

            if (!_gameOverlayEnabled || _overlayToggleKey == Key.None)
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
            ShowInTaskbar = true;
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

            var updates = await ModUpdateService.GetAvailableUpdatesAsync(
                _subnauticaInstalledVersions,
                _belowZeroInstalledVersions,
                _subnautica2InstalledVersions);
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

            await LoadInstalledVersionsAsync(repairMetadata: true);
        }

        private void StartUpdateCheckTimer()
        {
            _updateCheckTimer?.Stop();
            _updateCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _updateCheckTimer.Tick += async (_, _) =>
            {
                await RefreshInstalledVersionsIfIdleAsync();
                await RunBackgroundChecksIfIdleAsync();
            };
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

                statusProgress.Report("Closing launcher and continuing in updater...");
                progressWindow.SetIndeterminate("Handing off to updater...");

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
            if (LauncherBusyCoordinator.IsBusy)
                return;

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

            if (LauncherBusyCoordinator.IsBusy)
                return false;

            if (IsAnyGameProcessRunning())
                return false;

            return OwnedWindows.Cast<Window>().All(window => !window.IsVisible);
        }

        private async Task RefreshInstalledVersionsIfIdleAsync(bool repairMetadata = false)
        {
            if (LauncherBusyCoordinator.IsBusy || !_startupStagesCompleted)
                return;

            await LoadInstalledVersionsAsync(repairMetadata);
        }

        private async Task NotifyActionCompletedAsync(bool reloadVersions = true)
        {
            if (reloadVersions)
                await LoadInstalledVersionsAsync(repairMetadata: true);
            else
                UpdateSidebarState();

            await RunBackgroundChecksIfIdleAsync();
        }

        private void NotifyActionCompleted(bool reloadVersions = true)
        {
            _ = NotifyActionCompletedAsync(reloadVersions);
        }

        private async Task EnsureSteamVisibleActiveFoldersAndRefreshAsync()
        {
            if (await EnsureSteamVisibleActiveFoldersAsync(
                    _subnauticaInstalledVersions,
                    _belowZeroInstalledVersions,
                    _subnautica2InstalledVersions))
            {
                LoadInstalledVersions(repairMetadata: false);
            }
        }

        private async Task<bool> EnsureSteamVisibleActiveFoldersAsync(
            IEnumerable<InstalledVersion> subnauticaInstalled,
            IEnumerable<InstalledVersion> belowZeroInstalled,
            IEnumerable<InstalledVersion> subnautica2Installed)
        {
            bool changed = false;
            changed |= await EnsureSteamVisibleActiveFolderAsync(SubnauticaProfile, subnauticaInstalled);
            changed |= await EnsureSteamVisibleActiveFolderAsync(BelowZeroProfile, belowZeroInstalled);
            changed |= await EnsureSteamVisibleActiveFolderAsync(Subnautica2Profile, subnautica2Installed);
            return changed;
        }

        private async Task<bool> EnsureSteamVisibleActiveFolderAsync(
            LauncherGameProfile profile,
            IEnumerable<InstalledVersion> allInstalled)
        {
            if (!RenameFolderSafetyEnabled || IsProcessRunning(profile.ProcessName))
                return false;

            bool changed = false;
            var commonPaths = AppPaths.SteamCommonPaths;
            if (commonPaths.Count == 0)
                commonPaths = new List<string> { AppPaths.SteamCommonPath };

            foreach (string common in commonPaths)
            {
                string activePath = profile.GetActiveFolderPath(common);
                if (ShouldKeepPinnedSteamLaunchFolder(profile, activePath))
                {
                    Logger.Log($"[SteamFolderPolicy] Keeping pinned Steam-visible {profile.DisplayName} folder '{activePath}' because it was chosen for a recent manual launch.");
                    continue;
                }

                List<InstalledVersion> candidates = allInstalled
                    .Where(v =>
                        string.Equals(AppPaths.GetSteamCommonPathFor(v.HomeFolder), common, StringComparison.OrdinalIgnoreCase) &&
                        HasUsableExecutable(v.HomeFolder, profile))
                    .OrderBy(v => GetInstallDefinitionPriority(profile, v))
                    .ThenBy(v => v.IsModded)
                    .ThenByDescending(v => GetInstalledVersionTimestampUtc(profile, v))
                    .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                InstalledVersion? preferred = candidates.FirstOrDefault();
                if (preferred == null)
                {
                    if (HasUsableExecutable(activePath, profile))
                        Logger.Log($"[SteamFolderPolicy] Keeping existing Steam-visible {profile.DisplayName} folder '{activePath}' because no better managed candidate was found.");

                    continue;
                }

                if (PathsAreEqual(preferred.HomeFolder, activePath))
                {
                    Logger.Log($"[SteamFolderPolicy] Keeping '{activePath}' as the Steam-visible {profile.DisplayName} folder because it is the preferred installed version.");
                    continue;
                }

                try
                {
                    Logger.Log($"[SteamFolderPolicy] Promoting preferred {profile.DisplayName} version '{preferred.HomeFolder}' to '{activePath}'.");

                    if (Directory.Exists(activePath))
                    {
                        await LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                            common,
                            profile.ActiveFolderName,
                            profile.UnmanagedReservedFolderName,
                            profile.InfoFileName,
                            (activeFolder, infoPath) => profile.FromInfo(activeFolder, infoPath)?.FolderName);
                    }

                    await LaunchCoordinator.MoveFolderWithRetryAsync(preferred.HomeFolder, activePath, timeoutMs: 4000);
                    changed = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.Warn($"[SteamFolderPolicy] Failed to promote preferred version '{preferred.HomeFolder}' to '{activePath}' for {profile.DisplayName}. Error='{ex.Message}'");
                }
            }

            return changed;
        }

        private static int GetInstallDefinitionPriority(LauncherGameProfile profile, InstalledVersion version)
        {
            for (int index = 0; index < profile.InstallDefinitions.Count; index++)
            {
                if (string.Equals(
                        profile.InstallDefinitions[index].Id,
                        version.OriginalDownload,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        private static DateTime GetInstalledVersionTimestampUtc(LauncherGameProfile profile, InstalledVersion version)
        {
            try
            {
                string infoPath = Path.Combine(version.HomeFolder, profile.InfoFileName);
                if (File.Exists(infoPath))
                    return File.GetLastWriteTimeUtc(infoPath);

                if (Directory.Exists(version.HomeFolder))
                    return Directory.GetLastWriteTimeUtc(version.HomeFolder);
            }
            catch
            {
                // Fall back below.
            }

            return DateTime.MinValue;
        }

        private static bool HasUsableExecutable(string folderPath, LauncherGameProfile profile)
        {
            return !string.IsNullOrWhiteSpace(folderPath) &&
                   Directory.Exists(folderPath) &&
                   profile.HasExpectedExecutable(folderPath);
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
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 760
                });

                panel.Children.Add(new TextBlock
                {
                    Text = update.Date,
                    FontSize = 12,
                    FontWeight = FontWeights.ExtraLight,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 2, 0, 6),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 760
                });

                foreach (var change in update.Changes)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "- " + change,
                        FontSize = 13,
                        Foreground = Brushes.LightGray,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 760
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
            RefreshExplosionCustomRangeUi();

            Logger.Log($"Explosion reset enabled = {ExplosionResetSettings.Enabled}");
        }

        private void ExplosionPresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExplosionPresetDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                ExplosionResetSettings.Preset = Enum.Parse<Enums.ExplosionResetPreset>(tag);
                ExplosionResetSettings.Save();
                RefreshExplosionCustomRangeUi();
                _launcherOverlayWindow?.RefreshFromMain();

                Logger.Log($"Explosion reset preset set to {ExplosionResetSettings.Preset}");
            }
        }

        private void RefreshExplosionCustomRangeUi()
        {
            bool isCustom = ExplosionResetSettings.Preset == ExplosionResetPreset.Custom;
            bool isEnabled = ExplosionResetSettings.Enabled && isCustom;

            ExplosionCustomRangePanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            ExplosionCustomMinBox.IsEnabled = isEnabled;
            ExplosionCustomMaxBox.IsEnabled = isEnabled;
            SyncExplosionCustomRangeTextBoxes();
        }

        private void SyncExplosionCustomRangeTextBoxes()
        {
            _syncingExplosionCustomRangeInputs = true;
            try
            {
                string minText = ExplosionCustomRange.FormatSeconds(ExplosionResetSettings.CustomMinSeconds);
                string maxText = ExplosionCustomRange.FormatSeconds(ExplosionResetSettings.CustomMaxSeconds);

                if (!string.Equals(ExplosionCustomMinBox.Text, minText, StringComparison.Ordinal))
                    ExplosionCustomMinBox.Text = minText;

                if (!string.Equals(ExplosionCustomMaxBox.Text, maxText, StringComparison.Ordinal))
                    ExplosionCustomMaxBox.Text = maxText;
            }
            finally
            {
                _syncingExplosionCustomRangeInputs = false;
            }
        }

        private bool TryApplyExplosionCustomRange(string minimumText, string maximumText, bool showError, Window? ownerWindow = null)
        {
            if (!ExplosionCustomRange.TryParseAndValidate(
                    minimumText,
                    maximumText,
                    out int minimumSeconds,
                    out int maximumSeconds,
                    out string errorMessage))
            {
                if (showError)
                {
                    SyncExplosionCustomRangeTextBoxes();
                    MessageBox.Show(
                        ownerWindow ?? this,
                        errorMessage,
                        "Invalid Custom Explosion Range",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return false;
            }

            if (ExplosionResetSettings.CustomMinSeconds == minimumSeconds &&
                ExplosionResetSettings.CustomMaxSeconds == maximumSeconds)
            {
                return true;
            }

            ExplosionResetSettings.SetCustomRange(minimumSeconds, maximumSeconds);
            ExplosionResetSettings.Save();
            _launcherOverlayWindow?.RefreshFromMain();
            Logger.Log(
                $"Explosion reset custom range set to {ExplosionCustomRange.FormatSeconds(minimumSeconds)} -> {ExplosionCustomRange.FormatSeconds(maximumSeconds)}");
            return true;
        }

        private void ExplosionCustomRangeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingExplosionCustomRangeInputs)
                return;

            TryApplyExplosionCustomRange(ExplosionCustomMinBox.Text, ExplosionCustomMaxBox.Text, showError: false);
        }

        private void ExplosionCustomRangeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncingExplosionCustomRangeInputs)
                return;

            TryApplyExplosionCustomRange(ExplosionCustomMinBox.Text, ExplosionCustomMaxBox.Text, showError: true, ownerWindow: this);
        }

        private void ExplosionCustomRangeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            TryApplyExplosionCustomRange(ExplosionCustomMinBox.Text, ExplosionCustomMaxBox.Text, showError: true, ownerWindow: this);
            Keyboard.ClearFocus();
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
            if (InstalledVersionsList.SelectedItem is InstalledVersion leftVersion)
            {
                await LaunchVersionAsync(leftVersion, GetProfileForVersion(leftVersion));
                return;
            }

            if (BZInstalledVersionsList.SelectedItem is InstalledVersion rightVersion)
            {
                await LaunchVersionAsync(rightVersion, GetProfileForVersion(rightVersion));
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

        private async Task LaunchVersionAsync<TVersion>(TVersion target, LauncherGameProfile profile)
            where TVersion : InstalledVersion
        {
            string common = AppPaths.GetSteamCommonPathFor(target.HomeFolder);
            string activePath = profile.GetActiveFolderPath(common);
            string launchFolder = target.HomeFolder;
            string targetExe = profile.GetLaunchExecutablePath(launchFolder);
            string statusFolder = target.HomeFolder;
            bool launchWithoutSteam = LauncherSettings.Current.ForceLaunchWithoutSteam;
            using IDisposable busyOperation = LauncherBusyCoordinator.Begin($"Launch {target.FolderName}");

            PauseFolderSwitchServices();
            try
            {
                SetStatus(target, VersionStatus.Switching);

                bool wasRunning = await LaunchCoordinator.CloseAllGameProcessesAsync();

                if (wasRunning)
                {
                    await Task.Delay(1000);

                    if (profile.Game == LauncherGame.Subnautica)
                    {
                        int yearGroup = BuildYearResolver.ResolveGroupedYear(target.HomeFolder);
                        if (yearGroup >= 2022)
                            await Task.Delay(1500);
                    }
                }

                if (launchWithoutSteam)
                {
                    Logger.Log($"[DirectLaunch] Force-launching {profile.DisplayName} from '{target.HomeFolder}' without Steam.");
                    profile.EnsureSteamAppIdFile(launchFolder);
                    SetTrackedDirectLaunchFolder(profile, target.HomeFolder);
                }
                else
                {
                    try
                    {
                        launchFolder = await ActivateVersionFolderForSteamLaunchAsync(target, profile, common, activePath);
                        statusFolder = launchFolder;
                        targetExe = profile.GetLaunchExecutablePath(launchFolder);

                        await RefreshInstalledVersionsAfterFolderMoveAsync(target.HomeFolder, launchFolder);
                        await CleanupSteamAppIdFilesForGameAsync(profile);

                        SetTrackedDirectLaunchFolder(profile, null);
                        SetPinnedSteamLaunchFolder(profile, launchFolder);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.Warn($"[SteamLaunch] Failed to switch '{target.HomeFolder}' into '{activePath}'. Error='{ex.Message}'");

                        MessageBoxResult fallbackChoice = MessageBox.Show(
                            "The launcher could not rename the game folders for a normal Steam launch." +
                            Environment.NewLine + Environment.NewLine +
                            ex.Message +
                            Environment.NewLine + Environment.NewLine +
                            "Do you want to launch without Steam instead? This will use steam_appid.txt for this launch.",
                            "Normal Launch Failed",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning);

                        if (fallbackChoice != MessageBoxResult.Yes)
                        {
                            SetStatus(target, VersionStatus.Idle);
                            return;
                        }

                        launchWithoutSteam = true;
                        launchFolder = target.HomeFolder;
                        statusFolder = target.HomeFolder;
                        targetExe = profile.GetLaunchExecutablePath(launchFolder);

                        Logger.Log($"[DirectLaunch] Falling back to steamless launch for {profile.DisplayName} from '{target.HomeFolder}'.");
                        profile.EnsureSteamAppIdFile(launchFolder);
                        SetTrackedDirectLaunchFolder(profile, target.HomeFolder);
                    }
                }

                if (!File.Exists(targetExe))
                    throw new FileNotFoundException($"{Path.GetFileName(targetExe)} not found in the selected version folder.", targetExe);

                SetStatusForFolder(profile, statusFolder, VersionStatus.Launching);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = profile.GetLaunchWorkingDirectory(launchFolder),
                    Arguments = launchWithoutSteam
                        ? (target.LaunchOptions ?? string.Empty).Trim()
                        : string.Empty,
                    UseShellExecute = false
                });

                if (process == null)
                    throw new InvalidOperationException($"Failed to launch {profile.DisplayName}.");

                DebugTelemetryController.ShowForGameLaunch();

                bool launched = await WaitForLaunchedAsync(process);
                SetStatusForFolder(
                    profile,
                    statusFolder,
                    launched ? VersionStatus.Launched : GetNonRunningStatus(statusFolder, activePath));
                RefreshRunningStatusIndicators();
            }
            catch (Exception ex)
            {
                SetStatusForFolder(profile, statusFolder, GetNonRunningStatus(statusFolder, activePath));
                RefreshRunningStatusIndicators();

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

        private async Task<string> ActivateVersionFolderForSteamLaunchAsync<TVersion>(
            TVersion target,
            LauncherGameProfile profile,
            string common,
            string activePath)
            where TVersion : InstalledVersion
        {
            bool isAlreadyActive =
                Directory.Exists(activePath) &&
                PathsAreEqual(target.HomeFolder, activePath);

            if (isAlreadyActive)
                return activePath;

            if (Directory.Exists(activePath))
            {
                await LaunchCoordinator.RestoreActiveFolderUntilGoneAsync(
                    common,
                    profile.ActiveFolderName,
                    profile.UnmanagedReservedFolderName,
                    profile.InfoFileName,
                    (activeFolder, infoPath) => profile.FromInfo(activeFolder, infoPath)?.FolderName);
            }

            await LaunchCoordinator.MoveFolderWithRetryAsync(target.HomeFolder, activePath);
            Logger.Log($"[SteamLaunch] Activated '{target.HomeFolder}' as '{activePath}' for {profile.DisplayName}.");
            return activePath;
        }

        private async Task RefreshInstalledVersionsAfterFolderMoveAsync(string sourceFolder, string destinationFolder)
        {
            (string? selectedSnFolder, string? selectedBzFolder) = await Dispatcher.InvokeAsync(() => (
                (InstalledVersionsList.SelectedItem as InstalledVersion)?.HomeFolder,
                (BZInstalledVersionsList.SelectedItem as InstalledVersion)?.HomeFolder));

            if (!string.IsNullOrWhiteSpace(selectedSnFolder) && PathsAreEqual(selectedSnFolder, sourceFolder))
                selectedSnFolder = destinationFolder;

            if (!string.IsNullOrWhiteSpace(selectedBzFolder) && PathsAreEqual(selectedBzFolder, sourceFolder))
                selectedBzFolder = destinationFolder;

            InstalledVersionScanSnapshot snapshot = await InstalledVersionScanService.ScanAsync(repairMetadata: false);
            await Dispatcher.InvokeAsync(() =>
            {
                ApplyInstalledVersionSnapshot(snapshot, selectedSnFolder, selectedBzFolder);
            });
        }

        private IEnumerable<InstalledVersion> GetInstalledVersionsForProfile(LauncherGameProfile profile)
        {
            return profile.Game switch
            {
                LauncherGame.BelowZero => _belowZeroInstalledVersions.Cast<InstalledVersion>(),
                LauncherGame.Subnautica2 => _subnautica2InstalledVersions,
                _ => _subnauticaInstalledVersions
            };
        }

        private IEnumerable<string> GetKnownFoldersForProfile(LauncherGameProfile profile)
        {
            HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);

            foreach (InstalledVersion version in GetInstalledVersionsForProfile(profile))
            {
                if (!string.IsNullOrWhiteSpace(version.HomeFolder))
                    folders.Add(version.HomeFolder);
            }

            IReadOnlyList<string> commonPaths = AppPaths.SteamCommonPaths;
            if (commonPaths.Count == 0)
                commonPaths = new List<string> { AppPaths.SteamCommonPath };

            foreach (string commonPath in commonPaths)
                folders.Add(profile.GetActiveFolderPath(commonPath));

            return folders;
        }

        private Task CleanupSteamAppIdFilesForAllGamesAsync()
        {
            return Task.WhenAll(LauncherGameProfiles.All.Select(CleanupSteamAppIdFilesForGameAsync));
        }

        private Task CleanupSteamAppIdFilesForGameAsync(LauncherGameProfile profile)
        {
            string[] folders = GetKnownFoldersForProfile(profile).ToArray();
            return Task.Run(() =>
            {
                foreach (string folder in folders)
                {
                    if (!Directory.Exists(folder))
                        continue;

                    profile.RemoveSteamAppIdFiles(folder);
                }
            });
        }

        private InstalledVersion? FindInstalledVersionByFolder(LauncherGameProfile profile, string folderPath)
        {
            return GetInstalledVersionsForProfile(profile)
                .FirstOrDefault(version =>
                    !string.IsNullOrWhiteSpace(version.HomeFolder) &&
                    PathsAreEqual(version.HomeFolder, folderPath));
        }

        private void SetStatusForFolder(LauncherGameProfile profile, string folderPath, VersionStatus status)
        {
            InstalledVersion? version = FindInstalledVersionByFolder(profile, folderPath);
            if (version is BZInstalledVersion belowZeroVersion)
            {
                SetStatus(belowZeroVersion, status);
                return;
            }

            if (version != null)
            {
                SetStatus(version, status);
                return;
            }

            RefreshVersionStatusUi(refreshSnList: true, refreshBzList: true);
        }

        private static VersionStatus GetNonRunningStatus(string versionFolder, string activeFolder)
        {
            return PathsAreEqual(versionFolder, activeFolder)
                ? VersionStatus.Active
                : VersionStatus.Idle;
        }

        private string? GetTrackedDirectLaunchFolder(LauncherGameProfile profile)
        {
            return profile.Game switch
            {
                LauncherGame.BelowZero => _directLaunchedBelowZeroFolder,
                LauncherGame.Subnautica2 => _directLaunchedSubnautica2Folder,
                _ => _directLaunchedSubnauticaFolder
            };
        }

        private void SetTrackedDirectLaunchFolder(LauncherGameProfile profile, string? folderPath)
        {
            switch (profile.Game)
            {
                case LauncherGame.BelowZero:
                    _directLaunchedBelowZeroFolder = folderPath;
                    break;
                case LauncherGame.Subnautica2:
                    _directLaunchedSubnautica2Folder = folderPath;
                    break;
                default:
                    _directLaunchedSubnauticaFolder = folderPath;
                    break;
            }
        }

        private string? GetPinnedSteamLaunchFolder(LauncherGameProfile profile)
        {
            return profile.Game switch
            {
                LauncherGame.BelowZero => _steamLaunchPinnedBelowZeroFolder,
                LauncherGame.Subnautica2 => _steamLaunchPinnedSubnautica2Folder,
                _ => _steamLaunchPinnedSubnauticaFolder
            };
        }

        private DateTime GetPinnedSteamLaunchUntilUtc(LauncherGameProfile profile)
        {
            return profile.Game switch
            {
                LauncherGame.BelowZero => _steamLaunchPinnedBelowZeroUntilUtc,
                LauncherGame.Subnautica2 => _steamLaunchPinnedSubnautica2UntilUtc,
                _ => _steamLaunchPinnedSubnauticaUntilUtc
            };
        }

        private void SetPinnedSteamLaunchFolder(LauncherGameProfile profile, string? folderPath, DateTime? untilUtc = null)
        {
            DateTime expires = untilUtc ?? DateTime.UtcNow.Add(SteamLaunchPinGracePeriod);
            switch (profile.Game)
            {
                case LauncherGame.BelowZero:
                    _steamLaunchPinnedBelowZeroFolder = folderPath;
                    _steamLaunchPinnedBelowZeroUntilUtc = string.IsNullOrWhiteSpace(folderPath) ? DateTime.MinValue : expires;
                    break;
                case LauncherGame.Subnautica2:
                    _steamLaunchPinnedSubnautica2Folder = folderPath;
                    _steamLaunchPinnedSubnautica2UntilUtc = string.IsNullOrWhiteSpace(folderPath) ? DateTime.MinValue : expires;
                    break;
                default:
                    _steamLaunchPinnedSubnauticaFolder = folderPath;
                    _steamLaunchPinnedSubnauticaUntilUtc = string.IsNullOrWhiteSpace(folderPath) ? DateTime.MinValue : expires;
                    break;
            }
        }

        private bool ShouldKeepPinnedSteamLaunchFolder(LauncherGameProfile profile, string activePath)
        {
            string? pinnedFolder = GetPinnedSteamLaunchFolder(profile);
            if (string.IsNullOrWhiteSpace(pinnedFolder))
                return false;

            if (!PathsAreEqual(pinnedFolder, activePath))
                return false;

            if (DateTime.UtcNow <= GetPinnedSteamLaunchUntilUtc(profile))
                return true;

            SetPinnedSteamLaunchFolder(profile, null, DateTime.MinValue);
            return false;
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

            if (LauncherBusyCoordinator.IsBusy)
                return;

            TryStartInstalledVersionReloadWorker();
        }

        private void TryStartInstalledVersionReloadWorker()
        {
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
                    if (LauncherBusyCoordinator.IsBusy)
                    {
                        Interlocked.Increment(ref _pendingVersionReloadRequests);
                        return;
                    }

                    (string? selectedSnFolder, string? selectedBzFolder) = await Dispatcher.InvokeAsync(() => (
                        (InstalledVersionsList.SelectedItem as InstalledVersion)?.HomeFolder,
                        (BZInstalledVersionsList.SelectedItem as InstalledVersion)?.HomeFolder));

                    bool repairMetadata = _pendingVersionReloadRepair;
                    _pendingVersionReloadRepair = false;

                    InstalledVersionScanSnapshot snapshot = await InstalledVersionScanService.ScanAsync(repairMetadata);

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
                    if (!LauncherBusyCoordinator.IsBusy &&
                        Volatile.Read(ref _pendingVersionReloadRequests) > 0)
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
            string? runningSnFolder = processSnapshot.Subnautica.FolderPath ?? GetTrackedDirectLaunchFolder(SubnauticaProfile);
            string? runningBzFolder = processSnapshot.BelowZero.FolderPath ?? GetTrackedDirectLaunchFolder(BelowZeroProfile);
            string? runningSn2Folder = processSnapshot.Subnautica2.FolderPath ?? GetTrackedDirectLaunchFolder(Subnautica2Profile);

            _subnauticaInstalledVersions.Clear();
            _subnauticaInstalledVersions.AddRange(snapshot.SubnauticaVersions);
            ApplySnapshotStatuses(_subnauticaInstalledVersions, SubnauticaProfile, runningSnFolder);

            _belowZeroInstalledVersions.Clear();
            _belowZeroInstalledVersions.AddRange(snapshot.BelowZeroVersions);
            ApplySnapshotStatuses(_belowZeroInstalledVersions, BelowZeroProfile, runningBzFolder);

            _subnautica2InstalledVersions.Clear();
            _subnautica2InstalledVersions.AddRange(snapshot.Subnautica2Versions);
            ApplySnapshotStatuses(_subnautica2InstalledVersions, Subnautica2Profile, runningSn2Folder);

            RefreshPlayTabLists(selectedSnFolder, selectedBzFolder);

            RefreshRunningStatusIndicators();
            UpdateSidebarState();
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void RefreshPlayTabLists(string? selectedLeftFolder = null, string? selectedRightFolder = null)
        {
            PlayTabListViewMode listView = LauncherSettings.Current.PlayTabListView;
            BindVersionsForDisplay(InstalledVersionsList, GetVersionsForPlayTabChoice(LauncherSettings.Current.PlayTabGame1), listView);
            BindVersionsForDisplay(BZInstalledVersionsList, GetVersionsForPlayTabChoice(LauncherSettings.Current.PlayTabGame2), listView);

            RestoreSelectionForList(InstalledVersionsList, selectedLeftFolder);
            RestoreSelectionForList(BZInstalledVersionsList, selectedRightFolder);
        }

        private IReadOnlyList<InstalledVersion> GetVersionsForPlayTabChoice(PlayTabGameViewOption choice)
        {
            return choice switch
            {
                PlayTabGameViewOption.Subnautica => _subnauticaInstalledVersions.Cast<InstalledVersion>().ToList(),
                PlayTabGameViewOption.BelowZero => _belowZeroInstalledVersions.Cast<InstalledVersion>().ToList(),
                PlayTabGameViewOption.Subnautica2 => _subnautica2InstalledVersions.Cast<InstalledVersion>().ToList(),
                _ => Array.Empty<InstalledVersion>()
            };
        }

        private static void RestoreSelectionForList(System.Windows.Controls.ListBox listBox, string? selectedFolder)
        {
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                listBox.SelectedItem = null;
                return;
            }

            listBox.SelectedItem = listBox.Items
                .OfType<InstalledVersion>()
                .FirstOrDefault(v => PathsAreEqual(v.HomeFolder, selectedFolder));
        }

        private static void BindVersionsForDisplay(System.Windows.Controls.ListBox listBox, IReadOnlyList<InstalledVersion> versions, PlayTabListViewMode listView)
        {
            listBox.ItemsSource = null;

            var displayItems = versions.ToList();
            var view = new ListCollectionView(displayItems);
            view.GroupDescriptions.Clear();
            view.SortDescriptions.Clear();

            bool useLabels = listView == PlayTabListViewMode.Labeled && displayItems.Any(v => v.IsModded);
            if (useLabels)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InstalledVersion.GroupLabel)));
                view.SortDescriptions.Add(new SortDescription(nameof(InstalledVersion.IsModded), ListSortDirection.Descending));
            }

            view.SortDescriptions.Add(new SortDescription(nameof(InstalledVersion.DisplayName), ListSortDirection.Ascending));
            listBox.ItemsSource = view;
            listBox.Items.Refresh();
        }

        private static void ApplySnapshotStatuses<TVersion>(
            IEnumerable<TVersion> versions,
            LauncherGameProfile profile,
            string? runningFolder)
            where TVersion : InstalledVersion
        {
            foreach (TVersion version in versions)
            {
                string common = AppPaths.GetSteamCommonPathFor(version.HomeFolder);
                string active = profile.GetActiveFolderPath(common);
                version.Status = GetVersionStatus(version.HomeFolder, active, runningFolder);
            }
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
            bool sn2Changed = false;

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

            if (_subnautica2InstalledVersions.Count > 0)
            {
                foreach (var v in _subnautica2InstalledVersions)
                {
                    if (v.Status is VersionStatus.Launched or VersionStatus.Active)
                    {
                        v.Status = VersionStatus.Closing;
                        sn2Changed = true;
                    }
                }
            }

            if (snChanged || bzChanged || sn2Changed)
                RefreshVersionStatusUi(refreshSnList: true, refreshBzList: true);
        }

        private void ClearClosingStatus()
        {
            bool snChanged = false;
            bool bzChanged = false;
            bool sn2Changed = false;

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

            if (_subnautica2InstalledVersions.Count > 0)
            {
                foreach (var v in _subnautica2InstalledVersions)
                {
                    if (v.Status == VersionStatus.Closing)
                    {
                        v.Status = VersionStatus.Idle;
                        sn2Changed = true;
                    }
                }
            }

            if (snChanged || bzChanged || sn2Changed)
                RefreshVersionStatusUi(refreshSnList: true, refreshBzList: true);
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
            bool sn2Running = processSnapshot.Subnautica2.IsRunning;
            if (!snRunning)
            {
                SetTrackedDirectLaunchFolder(SubnauticaProfile, null);
                if (DateTime.UtcNow > _steamLaunchPinnedSubnauticaUntilUtc)
                    SetPinnedSteamLaunchFolder(SubnauticaProfile, null, DateTime.MinValue);
            }
            if (!bzRunning)
            {
                SetTrackedDirectLaunchFolder(BelowZeroProfile, null);
                if (DateTime.UtcNow > _steamLaunchPinnedBelowZeroUntilUtc)
                    SetPinnedSteamLaunchFolder(BelowZeroProfile, null, DateTime.MinValue);
            }
            if (!sn2Running)
            {
                SetTrackedDirectLaunchFolder(Subnautica2Profile, null);
                if (DateTime.UtcNow > _steamLaunchPinnedSubnautica2UntilUtc)
                    SetPinnedSteamLaunchFolder(Subnautica2Profile, null, DateTime.MinValue);
            }

            string? runningSnFolder = snRunning ? processSnapshot.Subnautica.FolderPath ?? GetTrackedDirectLaunchFolder(SubnauticaProfile) : null;
            string? runningBzFolder = bzRunning ? processSnapshot.BelowZero.FolderPath ?? GetTrackedDirectLaunchFolder(BelowZeroProfile) : null;
            string? runningSn2Folder = sn2Running ? processSnapshot.Subnautica2.FolderPath ?? GetTrackedDirectLaunchFolder(Subnautica2Profile) : null;
            bool snChanged = RefreshStatusesForProfile(_subnauticaInstalledVersions, SubnauticaProfile, snRunning, runningSnFolder);
            bool bzChanged = RefreshStatusesForProfile(_belowZeroInstalledVersions, BelowZeroProfile, bzRunning, runningBzFolder);
            bool sn2Changed = RefreshStatusesForProfile(_subnautica2InstalledVersions, Subnautica2Profile, sn2Running, runningSn2Folder);
            bool anyRunning = snRunning || bzRunning || sn2Running;

            if (snChanged || bzChanged || sn2Changed)
                RefreshVersionStatusUi(refreshSnList: true, refreshBzList: true);
        }

        private static bool RefreshStatusesForProfile<TVersion>(
            IEnumerable<TVersion> versions,
            LauncherGameProfile profile,
            bool gameRunning,
            string? runningFolder)
            where TVersion : InstalledVersion
        {
            bool changed = false;

            foreach (TVersion version in versions)
            {
                if (version.Status is VersionStatus.Switching or VersionStatus.Launching)
                    continue;
                if (version.Status == VersionStatus.Closing && gameRunning)
                    continue;

                string common = AppPaths.GetSteamCommonPathFor(version.HomeFolder);
                string active = profile.GetActiveFolderPath(common);
                VersionStatus next = GetVersionStatus(version.HomeFolder, active, runningFolder);

                if (version.Status != next)
                {
                    version.Status = next;
                    changed = true;
                }
            }

            return changed;
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
            {
                SetTrackedDirectLaunchFolder(SubnauticaProfile, null);
                SetPinnedSteamLaunchFolder(SubnauticaProfile, null, DateTime.MinValue);
            }
            if (!processSnapshot.BelowZero.IsRunning)
            {
                SetTrackedDirectLaunchFolder(BelowZeroProfile, null);
                SetPinnedSteamLaunchFolder(BelowZeroProfile, null, DateTime.MinValue);
            }
            if (!processSnapshot.Subnautica2.IsRunning)
            {
                SetTrackedDirectLaunchFolder(Subnautica2Profile, null);
                SetPinnedSteamLaunchFolder(Subnautica2Profile, null, DateTime.MinValue);
            }
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

        private void GameViewDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPlayViewSelectors)
                return;

            var settings = LauncherSettings.Current;
            settings.PlayTabGame1 = GetPlayTabGameChoice(Game1ViewDropdown, settings.PlayTabGame1);
            settings.PlayTabGame2 = GetPlayTabGameChoice(Game2ViewDropdown, settings.PlayTabGame2);

            if (settings.PlayTabGame1 == settings.PlayTabGame2 &&
                settings.PlayTabGame1 != PlayTabGameViewOption.None)
            {
                if (ReferenceEquals(sender, Game1ViewDropdown))
                {
                    settings.PlayTabGame2 = PlayTabGameViewOption.None;
                    _syncingPlayViewSelectors = true;
                    try
                    {
                        SelectPlayTabGameChoice(Game2ViewDropdown, settings.PlayTabGame2);
                    }
                    finally
                    {
                        _syncingPlayViewSelectors = false;
                    }
                }
                else
                {
                    settings.PlayTabGame1 = PlayTabGameViewOption.None;
                    _syncingPlayViewSelectors = true;
                    try
                    {
                        SelectPlayTabGameChoice(Game1ViewDropdown, settings.PlayTabGame1);
                    }
                    finally
                    {
                        _syncingPlayViewSelectors = false;
                    }
                }
            }

            LauncherSettings.Save();
            RefreshPlayTabLists();
            UpdateSidebarState();
        }

        private void ListViewDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPlayViewSelectors)
                return;

            LauncherSettings.Current.PlayTabListView =
                GetPlayTabListViewChoice(ListViewDropdown, LauncherSettings.Current.PlayTabListView);
            LauncherSettings.Save();
            RefreshPlayTabLists();
            UpdateSidebarState();
        }

        private bool SelectVisibleListForVersion(InstalledVersion version)
        {
            if (ListContainsVersion(InstalledVersionsList, version))
            {
                InstalledVersionsList.SelectedItem = version;
                BZInstalledVersionsList.SelectedItem = null;
                return true;
            }

            if (ListContainsVersion(BZInstalledVersionsList, version))
            {
                BZInstalledVersionsList.SelectedItem = version;
                InstalledVersionsList.SelectedItem = null;
                return true;
            }

            return false;
        }

        private static bool ListContainsVersion(System.Windows.Controls.ListBox listBox, InstalledVersion version)
        {
            return listBox.Items
                .OfType<InstalledVersion>()
                .Any(item => ReferenceEquals(item, version) || PathsAreEqual(item.HomeFolder, version.HomeFolder));
        }

        private async void InstallVersion_Click(object sender, RoutedEventArgs e)
        {
            bool? result = await DialogWindowHelper.ShowModelessAsync(this, new AddVersionWindow());
            await NotifyActionCompletedAsync(reloadVersions: result == true);
        }

        private void OpenInstallFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenInstallFolderForVersion(
                InstalledVersionsList.SelectedItem as InstalledVersion ??
                BZInstalledVersionsList.SelectedItem as InstalledVersion);
        }

        private async void EditVersion_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is InstalledVersion leftVersion)
            {
                await EditVersionInternalAsync(leftVersion, GetProfileForVersion(leftVersion));
                return;
            }

            if (BZInstalledVersionsList.SelectedItem is InstalledVersion rightVersion)
            {
                await EditVersionInternalAsync(rightVersion, GetProfileForVersion(rightVersion));
            }
        }

        private void OpenVersionFolderRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not InstalledVersion version)
                return;

            SelectVisibleListForVersion(version);

            OpenInstallFolderForVersion(version);
        }

        private async void EditVersionRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not InstalledVersion version)
                return;

            SelectVisibleListForVersion(version);
            await EditVersionInternalAsync(version, GetProfileForVersion(version));
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

        private async Task EditVersionInternalAsync(InstalledVersion version, LauncherGameProfile profile, Window? owner = null)
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

            bool? result = await DialogWindowHelper.ShowModelessAsync(owner ?? this, editWindow);
            await NotifyActionCompletedAsync(reloadVersions: result == true);
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
            if (runningState == RunningGameState.Multiple)
            {
                Logger.Warn("Reset macro blocked: multiple supported games are running.");
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

            if (runningState == RunningGameState.Subnautica2Only)
            {
                try
                {
                    await Subnautica2ResetMacroService.RunAsync(mode);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "SN2 reset macro failed");
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
            Subnautica2Only,
            Multiple
        }

        private static RunningGameState GetRunningGameState()
        {
            GameProcessMonitor.RefreshNow();
            GameProcessSnapshot snapshot = GameProcessMonitor.GetSnapshot();
            bool snRunning = snapshot.Subnautica.IsRunning;
            bool bzRunning = snapshot.BelowZero.IsRunning;
            bool sn2Running = snapshot.Subnautica2.IsRunning;

            int runningCount = (snRunning ? 1 : 0) + (bzRunning ? 1 : 0) + (sn2Running ? 1 : 0);
            if (runningCount > 1)
                return RunningGameState.Multiple;

            if (snRunning)
                return RunningGameState.SubnauticaOnly;

            if (bzRunning)
                return RunningGameState.BelowZeroOnly;

            if (sn2Running)
                return RunningGameState.Subnautica2Only;

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

        private async void ForceLaunchWithoutSteamToggle_Click(object sender, RoutedEventArgs e)
        {
            await SetForceLaunchWithoutSteamAsync(!LauncherSettings.Current.ForceLaunchWithoutSteam);
        }

        private async Task SetForceLaunchWithoutSteamAsync(bool enabled)
        {
            LauncherSettings.Current.ForceLaunchWithoutSteam = enabled;
            LauncherSettings.Save();

            UpdateForceLaunchWithoutSteamVisualState();

            if (!enabled)
            {
                try
                {
                    await CleanupSteamAppIdFilesForAllGamesAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "steam_appid Cleanup Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
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
                    ? FindActiveRoots(SubnauticaProfile)
                    : _subnauticaInstalledVersions
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            if (game == HardcoreSaveTargetGame.BelowZero)
            {
                return activeOnly
                    ? FindActiveRoots(BelowZeroProfile)
                    : _belowZeroInstalledVersions
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            var roots = new List<string>();

            if (activeOnly)
            {
                roots.AddRange(FindActiveRoots(SubnauticaProfile));
                roots.AddRange(FindActiveRoots(BelowZeroProfile));
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

        private static string[] FindActiveRoots(LauncherGameProfile profile)
        {
            return AppPaths.SteamCommonPaths
                .Select(profile.GetActiveFolderPath)
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
        }

        private void ApplyStartupMode(bool startupAsOverlay)
        {
            _overlayStartupMode = false;
            LauncherSettings.Current.StartupMode = LauncherStartupMode.Window;
            LauncherSettings.Save();

            RegisterOverlayToggleHotkey();

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

            _overlayStartupMode = false;
            settings.StartupMode = LauncherStartupMode.Window;
            _gameOverlayEnabled = settings.GameOverlayEnabled;
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

            UpdateGameOverlayVisualState();
            UpdateOverlayHotkeyDisplay();
            ApplyOverlayOpacity(overlayOpacity);
            RegisterOverlayToggleHotkey();
        }

        private void SyncStartupModeDropdown()
        {
        }

        private void UpdateOverlayHotkeyDisplay()
        {
            OverlayHotkeyBox.Text = FormatHotkey(_overlayToggleModifiers, _overlayToggleKey);
            _launcherOverlayWindow?.RefreshFromMain();
        }

        private void UpdateGameOverlayVisualState()
        {
            GameOverlayToggleButton.Content = _gameOverlayEnabled ? "Enabled" : "Disabled";
            GameOverlayToggleButton.Background = _gameOverlayEnabled ? Brushes.Green : Brushes.DarkRed;
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
                ShowInTaskbar = true;
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
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
            if (!_gameOverlayEnabled)
                return;

            EnsureOverlayWindow();
            _launcherOverlayWindow!.ApplyOverlayOpacity(_overlayOpacity);
            _launcherOverlayWindow.RefreshFromMain();

            if (!_launcherOverlayWindow.IsVisible)
                _launcherOverlayWindow.Show();
            _launcherOverlayWindow.Activate();
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
            if (!_gameOverlayEnabled)
                return;

            EnsureOverlayWindow();
            if (_launcherOverlayWindow!.IsVisible)
            {
                _launcherOverlayWindow.Hide();
                return;
            }

            ShowOverlayWindow();
        }

        private void GameOverlayToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _gameOverlayEnabled = !_gameOverlayEnabled;
            LauncherSettings.Current.GameOverlayEnabled = _gameOverlayEnabled;
            LauncherSettings.Save();

            UpdateGameOverlayVisualState();
            RegisterOverlayToggleHotkey();

            if (!_gameOverlayEnabled)
                _launcherOverlayWindow?.Hide();
        }

        internal IEnumerable<InstalledVersion> GetSubnauticaVersionsForOverlay() =>
            _subnauticaInstalledVersions;

        internal IEnumerable<BZInstalledVersion> GetBelowZeroVersionsForOverlay() =>
            _belowZeroInstalledVersions;

        internal IReadOnlyList<InstalledVersion> GetVersionsForOverlay(LauncherGame game)
        {
            return game switch
            {
                LauncherGame.BelowZero => _belowZeroInstalledVersions.Cast<InstalledVersion>().ToList(),
                LauncherGame.Subnautica2 => _subnautica2InstalledVersions,
                _ => _subnauticaInstalledVersions
            };
        }

        internal InstalledVersion? GetActiveVersionForOverlay(LauncherGame game)
        {
            return GetVersionsForOverlay(game)
                .OrderByDescending(v => GetStatusPriority(v.Status))
                .FirstOrDefault(v => v.Status != VersionStatus.Idle);
        }

        internal bool IsAnyGameRunningForOverlay() => GameProcessMonitor.GetSnapshot().AnyRunning;

        internal void RefreshRunningStateForOverlay()
        {
            GameProcessMonitor.RefreshNow();

            GameProcessSnapshot processSnapshot = GameProcessMonitor.GetSnapshot();
            bool snRunning = processSnapshot.Subnautica.IsRunning;
            bool bzRunning = processSnapshot.BelowZero.IsRunning;
            bool sn2Running = processSnapshot.Subnautica2.IsRunning;

            if (!snRunning)
                SetTrackedDirectLaunchFolder(SubnauticaProfile, null);
            if (!bzRunning)
                SetTrackedDirectLaunchFolder(BelowZeroProfile, null);
            if (!sn2Running)
                SetTrackedDirectLaunchFolder(Subnautica2Profile, null);

            string? runningSnFolder = snRunning ? processSnapshot.Subnautica.FolderPath ?? GetTrackedDirectLaunchFolder(SubnauticaProfile) : null;
            string? runningBzFolder = bzRunning ? processSnapshot.BelowZero.FolderPath ?? GetTrackedDirectLaunchFolder(BelowZeroProfile) : null;
            string? runningSn2Folder = sn2Running ? processSnapshot.Subnautica2.FolderPath ?? GetTrackedDirectLaunchFolder(Subnautica2Profile) : null;

            bool snChanged = RefreshStatusesForProfile(_subnauticaInstalledVersions, SubnauticaProfile, snRunning, runningSnFolder);
            bool bzChanged = RefreshStatusesForProfile(_belowZeroInstalledVersions, BelowZeroProfile, bzRunning, runningBzFolder);
            bool sn2Changed = RefreshStatusesForProfile(_subnautica2InstalledVersions, Subnautica2Profile, sn2Running, runningSn2Folder);

            if (snChanged)
                InstalledVersionsList.Items.Refresh();
            if (bzChanged || sn2Changed)
                BZInstalledVersionsList.Items.Refresh();

            UpdateSidebarState();
        }

        internal bool IsGameRunningForOverlay(LauncherGame game)
        {
            GameProcessSnapshot snapshot = GameProcessMonitor.GetSnapshot();
            return game switch
            {
                LauncherGame.BelowZero => snapshot.BelowZero.IsRunning,
                LauncherGame.Subnautica2 => snapshot.Subnautica2.IsRunning,
                _ => snapshot.Subnautica.IsRunning
            };
        }

        internal bool IsVersionRunningForOverlay(InstalledVersion version)
        {
            LauncherGameProfile profile = GetProfileForVersion(version);
            GameProcessSnapshot snapshot = GameProcessMonitor.GetSnapshot();
            string? runningFolder = profile.Game switch
            {
                LauncherGame.BelowZero => snapshot.BelowZero.FolderPath ?? GetTrackedDirectLaunchFolder(BelowZeroProfile),
                LauncherGame.Subnautica2 => snapshot.Subnautica2.FolderPath ?? GetTrackedDirectLaunchFolder(Subnautica2Profile),
                _ => snapshot.Subnautica.FolderPath ?? GetTrackedDirectLaunchFolder(SubnauticaProfile)
            };

            return !string.IsNullOrWhiteSpace(runningFolder) &&
                   PathsAreEqual(version.HomeFolder, runningFolder);
        }

        internal void OpenVersionFolderFromOverlay(InstalledVersion version)
        {
            if (version == null || string.IsNullOrWhiteSpace(version.HomeFolder) || !Directory.Exists(version.HomeFolder))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = version.HomeFolder,
                UseShellExecute = true
            });
        }

        internal async Task EditVersionFromOverlayAsync(InstalledVersion version)
        {
            if (version == null)
                return;

            await EditVersionInternalAsync(version, GetProfileForVersion(version));
        }

        internal async Task EditVersionFromOverlayAsync(Window owner, InstalledVersion version)
        {
            if (version == null)
                return;

            await EditVersionInternalAsync(version, GetProfileForVersion(version), owner);
        }

        internal async Task LaunchVersionFromOverlayAsync(InstalledVersion version)
        {
            if (version == null)
                return;

            await LaunchVersionAsync(version, GetProfileForVersion(version));
        }

        internal async Task CloseGameForOverlayAsync()
        {
            await CloseRunningGamesAsync();
        }

        internal InstalledVersion? GetSelectedVersionForOverlay() =>
            InstalledVersionsList.SelectedItem as InstalledVersion
            ?? BZInstalledVersionsList.SelectedItem as InstalledVersion;

        internal LauncherGame GetGameForVersionOverlay(InstalledVersion version) =>
            GetProfileForVersion(version).Game;

        internal InstalledVersion? GetSelectedSubnauticaVersionForOverlay()
        {
            if (InstalledVersionsList.SelectedItem is InstalledVersion leftVersion &&
                leftVersion is not BZInstalledVersion &&
                GetProfileForVersion(leftVersion).Game == LauncherGame.Subnautica)
            {
                return leftVersion;
            }

            if (BZInstalledVersionsList.SelectedItem is InstalledVersion rightVersion &&
                rightVersion is not BZInstalledVersion &&
                GetProfileForVersion(rightVersion).Game == LauncherGame.Subnautica)
            {
                return rightVersion;
            }

            return null;
        }

        internal BZInstalledVersion? GetSelectedBelowZeroVersionForOverlay() =>
            InstalledVersionsList.SelectedItem as BZInstalledVersion
            ?? BZInstalledVersionsList.SelectedItem as BZInstalledVersion;

        internal void SetSelectedVersionsFromOverlay(InstalledVersion? snVersion, BZInstalledVersion? bzVersion)
        {
            if (snVersion != null)
            {
                SelectVisibleListForVersion(snVersion);
            }

            if (bzVersion != null)
            {
                SelectVisibleListForVersion(bzVersion);
            }
        }

        internal void SetSelectedVersionFromOverlay(InstalledVersion? version)
        {
            if (version == null)
            {
                InstalledVersionsList.SelectedItem = null;
                BZInstalledVersionsList.SelectedItem = null;
                return;
            }

            if (!SelectVisibleListForVersion(version))
            {
                InstalledVersionsList.SelectedItem = null;
                BZInstalledVersionsList.SelectedItem = null;
            }
        }

        internal bool IsOverlayStartupModeForOverlay() => _overlayStartupMode;
        internal string GetOverlayHotkeyTextForOverlay() => FormatHotkey(_overlayToggleModifiers, _overlayToggleKey);
        internal double GetOverlayOpacityForOverlay() => _overlayOpacity;
        internal bool IsGameOverlayEnabledForOverlay() => _gameOverlayEnabled;
        internal bool IsExplosionOverlayEnabledForOverlay() => ExplosionResetSettings.OverlayEnabled;
        internal bool IsExplosionTrackingEnabledForOverlay() => ExplosionResetSettings.TrackResets;
        internal bool IsForceLaunchWithoutSteamForOverlay() => LauncherSettings.Current.ForceLaunchWithoutSteam;
        internal bool IsResetMacroEnabledForOverlay() => _macroEnabled;
        internal Key GetResetHotkeyForOverlay() => _resetKey;
        internal GameMode GetResetGameModeForOverlay() => GetSelectedGameMode(ResetGamemodeDropdown, LauncherSettings.Current.ResetGameMode);
        internal bool IsExplosionResetEnabledForOverlay() => ExplosionResetSettings.Enabled;
        internal ExplosionResetPreset GetExplosionPresetForOverlay() => ExplosionResetSettings.Preset;
        internal string GetExplosionCustomMinTextForOverlay() => ExplosionCustomRange.FormatSeconds(ExplosionResetSettings.CustomMinSeconds);
        internal string GetExplosionCustomMaxTextForOverlay() => ExplosionCustomRange.FormatSeconds(ExplosionResetSettings.CustomMaxSeconds);
        internal bool IsExplosionCustomPresetForOverlay() => ExplosionResetSettings.Preset == ExplosionResetPreset.Custom;
        internal bool IsHardcoreSaveDeleterEnabledForOverlay() => LauncherSettings.Current.HardcoreSaveDeleterEnabled;
        internal string GetBackgroundPresetForOverlay() => LauncherSettings.Current.BackgroundPreset;
        internal IReadOnlyList<string> GetBackgroundPresetOptionsForOverlay() =>
        [
            "Lifepod",
            "Safe Shallows",
            "Kelp Forest",
            "Grassy Plateau",
            "Lost River",
            "Cove Tree",
            "Floating Island",
            "Jellyshroom Caves",
            "Grand Reef",
            "Reaper Leviathan",
            "Ghost Leviathan",
            "Sea Dragon Leviathan",
            "Twisty Bridges",
            "Aurora Borealis",
            "Snowfox",
            "Icy Land",
            "Arcitect Facility",
            "Red Crystals",
            "Snow Stalker",
            "Squid Shark",
            "Shadow Leviathan",
            "Custom"
        ];

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
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void ChooseCustomBackgroundFromOverlay()
        {
            ChooseCustomBackground_Click(this, new RoutedEventArgs());
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void ChooseCustomBackgroundFromOverlay(Window owner)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
            };

            if (dlg.ShowDialog(owner) != true)
                return;

            LauncherSettings.Current.BackgroundPreset = dlg.FileName;
            LauncherSettings.Save();
            ApplyBackground(dlg.FileName);
            ThemeDropdown.SelectedItem = null;
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
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
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
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
            RefreshExplosionCustomRangeUi();
        }

        internal bool TrySetExplosionCustomRangeFromOverlay(string minimumText, string maximumText)
        {
            bool applied = TryApplyExplosionCustomRange(minimumText, maximumText, showError: false);
            if (applied)
                RefreshExplosionCustomRangeUi();
            return applied;
        }

        internal void CommitExplosionCustomRangeFromOverlay(string minimumText, string maximumText)
        {
            TryApplyExplosionCustomRange(minimumText, maximumText, showError: true, ownerWindow: _launcherOverlayWindow);
            RefreshExplosionCustomRangeUi();
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

        internal void ToggleExplosionOverlayFromOverlay()
        {
            ExplosionResetSettings.OverlayEnabled = !ExplosionResetSettings.OverlayEnabled;
            ExplosionResetSettings.Save();
            ExplosionDisplayToggleButton.Content = ExplosionResetSettings.OverlayEnabled ? "Enabled" : "Disabled";
            ExplosionDisplayToggleButton.Background = ExplosionResetSettings.OverlayEnabled ? Brushes.Green : Brushes.DarkRed;
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void ToggleExplosionTrackingFromOverlay()
        {
            ExplosionResetSettings.TrackResets = !ExplosionResetSettings.TrackResets;
            ExplosionResetSettings.Save();
            ExplosionTrackToggleButton.Content = ExplosionResetSettings.TrackResets ? "Enabled" : "Disabled";
            ExplosionTrackToggleButton.Background = ExplosionResetSettings.TrackResets ? Brushes.Green : Brushes.DarkRed;
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal async void ToggleForceLaunchWithoutSteamFromOverlay()
        {
            await SetForceLaunchWithoutSteamAsync(!LauncherSettings.Current.ForceLaunchWithoutSteam);
        }

        internal void ToggleResetMacroFromOverlay()
        {
            _macroEnabled = !_macroEnabled;
            UpdateResetMacroVisualState();
            SaveMacroSettings();
            RegisterResetHotkey();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void ToggleExplosionResetFromOverlay()
        {
            ExplosionResetSettings.Enabled = !ExplosionResetSettings.Enabled;
            ExplosionResetSettings.Save();
            ExplosionResetToggleButton.Content = ExplosionResetSettings.Enabled ? "Enabled" : "Disabled";
            ExplosionResetToggleButton.Background = ExplosionResetSettings.Enabled ? Brushes.Green : Brushes.DarkRed;
            ExplosionPresetDropdown.IsEnabled = ExplosionResetSettings.Enabled;
            RefreshExplosionCustomRangeUi();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void ToggleHardcoreSaveDeleterFromOverlay()
        {
            LauncherSettings.Current.HardcoreSaveDeleterEnabled = !LauncherSettings.Current.HardcoreSaveDeleterEnabled;
            LauncherSettings.Save();
            UpdateHardcoreSaveDeleterVisualState();
            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }
        internal void OpenTrackerCustomizeFromOverlay() => Subnautica100TrackerCustomize_Click(this, new RoutedEventArgs());
        internal void OpenSpeedrunTimerCustomizeFromOverlay() => SpeedrunTimerCustomize_Click(this, new RoutedEventArgs());
        internal void OpenHardcorePurgeFromOverlay() => HardcoreSaveDeleterPurge_Click(this, new RoutedEventArgs());
        internal void OpenTrackerCustomizeFromOverlay(Window owner)
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

            if (DialogWindowHelper.ShowDialog(owner, window) != true)
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

            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void OpenSpeedrunTimerCustomizeFromOverlay(Window owner)
        {
            var settings = LauncherSettings.Current;
            var window = new SpeedrunTimerEditWindow(
                settings.SpeedrunTimerEnabled,
                settings.SpeedrunGamemode,
                settings.SpeedrunCategory,
                settings.SpeedrunRunType);

            if (DialogWindowHelper.ShowDialog(owner, window) != true)
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

            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }

        internal void OpenHardcorePurgeFromOverlay(Window owner)
        {
            var win = new HardcoreSaveDeleterWindow();
            if (DialogWindowHelper.ShowDialog(owner, win) != true)
                return;

            string[] roots = GetTargetRoots(win.SelectedGame, win.SelectedScope);

            if (roots.Length == 0)
            {
                MessageBox.Show(
                    owner,
                    "No matching game folders were found.",
                    "No Targets",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int deleted = HardcoreSaveDeleter.DeleteAllHardcoreSaves(roots);

            MessageBox.Show(
                owner,
                deleted == 1 ? "Deleted 1 hardcore save." : $"Deleted {deleted} hardcore saves.",
                "Purge Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _launcherOverlayWindow?.RefreshVersionStatusOnly();
        }
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
            RefreshToolsTabSectionVisibility();
            ShowView(ToolsView);
        }

        private void ToolsAddGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (_syncingToolsTabVisibleGamesUi)
                return;

            var availableOptions = GetAvailableToolsTabGameOptions().ToList();
            if (availableOptions.Count == 0)
                return;

            var menu = new ContextMenu();

            foreach (ToolsTabGameOption option in availableOptions)
            {
                var menuItem = new MenuItem
                {
                    Header = GetToolsTabGameLabel(option),
                    Tag = option
                };
                menuItem.Click += ToolsAddGameMenuItem_Click;
                menu.Items.Add(menuItem);
            }

            ToolsAddGameButton.ContextMenu = menu;
            menu.PlacementTarget = ToolsAddGameButton;
            menu.IsOpen = true;
        }

        private void ToolsAddGameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: ToolsTabGameOption option })
                AddToolsTabGame(option);
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
            LauncherBusyCoordinator.BusyStateChanged -= LauncherBusyCoordinator_BusyStateChanged;
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

            try
            {
                if (!IsAnyGameProcessRunning())
                {
                    Logger.Log("[SteamFolderPolicy] Applying preferred Steam-visible folders during launcher shutdown.");
                    await EnsureSteamVisibleActiveFoldersAndRefreshAsync();
                }
                else
                {
                    Logger.Log("[SteamFolderPolicy] Skipping shutdown folder policy because a supported game is still running.");
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[SteamFolderPolicy] Failed while applying shutdown folder policy");
            }

            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyIdReset);
            UnregisterHotKey(handle, HotkeyIdOverlayToggle);

            if (_launcherOverlayWindow != null)
            {
                _launcherOverlayWindow.AllowClose();
                _launcherOverlayWindow.Close();
                _launcherOverlayWindow = null;
            }

            Logger.Log("Launcher shutdown complete");
        }
    }
}
