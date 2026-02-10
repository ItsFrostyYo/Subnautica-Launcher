using SubnauticaLauncher;
using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.Memory;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.BelowZero;
using System.Collections.Generic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
        private const string ACTIVE = "Subnautica";
        private const string UNMANAGED = "SubnauticaUnmanagedVersion";
        private Key _resetKey = Key.None;
        private bool _macroEnabled;
        private const int HOTKEY_ID = 9001;
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(
    IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;

        private const string DefaultBg = "Lifepod";
        private static CancellationTokenSource? _explosionCts;
        private static bool _explosionRunning;
        private bool _renameOnCloseEnabled = true; // ✅ enabled by default

        public MainWindow()
        {
            Logger.Log("MainWindow constructor");

            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // ================= TITLE BAR =================
        private void GameToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var bz = new BZMainWindow();
            bz.Show();
            Close();
        }

        private void UpdateResetMacroVisualState()
        {
            ResetMacroToggleButton.Content =
                _macroEnabled ? "Enabled" : "Disabled";

            ResetMacroToggleButton.Background =
                _macroEnabled ? Brushes.Green : Brushes.DarkRed;
        }

        private void UpdateHardcoreSaveDeleterVisualState()
        {
            bool enabled = LauncherSettings.Current.HardcoreSaveDeleterEnabled;
            HardcoreSaveDeleterToggleButton.Content = enabled ? "Enabled" : "Disabled";
            HardcoreSaveDeleterToggleButton.Background = enabled ? Brushes.Green : Brushes.DarkRed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            Logger.Log("Window source initialized");

            var source = HwndSource.FromHwnd(
                new WindowInteropHelper(this).Handle);

            source.AddHook(WndProc);

            RegisterResetHotkey();
        }
        [SupportedOSPlatform("windows")]
        private void RegisterResetHotkey()
        {
            UnregisterHotKey(
                new WindowInteropHelper(this).Handle,
                HOTKEY_ID);

            if (!_macroEnabled || _resetKey == Key.None)
            {
                Logger.Log("Reset hotkey not registered (disabled or no key)");
                return;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(_resetKey);

            bool ok = RegisterHotKey(
                new WindowInteropHelper(this).Handle,
                HOTKEY_ID,
                0,
                vk);

            Logger.Log($"Reset hotkey registered: Key={_resetKey}, Success={ok}");
        }

        private IntPtr WndProc(
    IntPtr hwnd,
    int msg,
    IntPtr wParam,
    IntPtr lParam,
    ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Logger.Log("Reset Macro Hotkey Pressed");

                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await OnResetHotkeyPressed();
                    }
                    catch (Exception ex)
                    {
                        Logger.Exception(ex, "Reset Macro Failed to Execute");
                    }
                });

                handled = true;
            }

            return IntPtr.Zero;
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

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
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

        // ================= STARTUP =================

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Log("Launcher UI Loaded Successfully");

            if (!Directory.Exists(AppPaths.ToolsPath) ||
                !File.Exists(DepotDownloaderInstaller.DepotDownloaderExe))
            {
                Logger.Warn("Required tools missing, opening setup window");

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

            Logger.Log("Launcher data directory prepared");

            LauncherSettings.Load();
            var settings = LauncherSettings.Current;

            string bg = settings.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
            {
                bg = DefaultBg;
                settings.BackgroundPreset = bg;
                LauncherSettings.Save();
            }

            Logger.Log($"Applying background: {bg}");

            ApplyBackground(bg);
            SyncThemeDropdown(bg);

            LoadInstalledVersions();
            LoadMacroSettings();

            ExplosionResetSettings.Load();

            ExplosionResetToggleButton.Content =
                ExplosionResetSettings.Enabled ? "Enabled" : "Disabled";

            ExplosionResetToggleButton.Background =
                ExplosionResetSettings.Enabled ? Brushes.Green : Brushes.DarkRed;

            ExplosionPresetDropdown.IsEnabled =
                ExplosionResetSettings.Enabled;

            ExplosionPresetDropdown.SelectedItem =
                ExplosionPresetDropdown.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(i =>
                        (string)i.Tag == ExplosionResetSettings.Preset.ToString());

            ExplosionDisplayToggleButton.Content =
    ExplosionResetSettings.OverlayEnabled ? "Enabled" : "Disabled";
            ExplosionDisplayToggleButton.Background =
                ExplosionResetSettings.OverlayEnabled ? Brushes.Green : Brushes.DarkRed;

            ExplosionTrackToggleButton.Content =
                ExplosionResetSettings.TrackResets ? "Enabled" : "Disabled";
            ExplosionTrackToggleButton.Background =
                ExplosionResetSettings.TrackResets ? Brushes.Green : Brushes.DarkRed;

            RenameOnCloseButton.Content =
    _renameOnCloseEnabled ? "Enabled" : "Disabled";

            RenameOnCloseButton.Background =
                _renameOnCloseEnabled ? Brushes.Green : Brushes.DarkRed;

            UpdateHardcoreSaveDeleterVisualState();

            Logger.Log("Startup Complete");
            ShowView(InstallsView);
        }

        private void ApplyBackground(string preset)
        {
            try
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();

                // Custom image (absolute path)
                if (File.Exists(preset))
                {
                    img.UriSource = new Uri(preset, UriKind.Absolute);
                }
                else
                {
                    // Built-in asset
                    var uri = new Uri(
    $"pack://application:,,,/Assets/Backgrounds/{preset}.png",
    UriKind.Absolute
);

                    Application.GetResourceStream(uri); // validate first
                    img.UriSource = uri;
                }

                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                BackgroundBrush.ImageSource = img;
            }
            catch
            {
                // Fallback to default asset
                BackgroundBrush.ImageSource =
                    new BitmapImage(new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                        UriKind.Absolute));
            }
        }

        private async Task CheckForUpdatesOnStartup()
        {
            try
            {
                var update = await UpdateChecker.CheckForUpdateAsync();
                if (update == null)
                    return;

                ShowUpdatingUI();
                await UpdaterChecker.EnsureUpdaterAsync();

                var newExe = await UpdateDownloader.DownloadAsync(update.DownloadUrl);
                UpdateHelper.ApplyUpdate(newExe);
            }
            catch
            {
                // silent fail by design
            }
        }

        private void ShowUpdatingUI()
        {
            Dispatcher.Invoke(() =>
            {
                Title = "Subnautica Launcher – Updating…";
                IsEnabled = false;
            });
        }

        private void BuildUpdatesView()
        {
            UpdatesPanel.Children.Clear();

            foreach (var update in UpdatesData.History)
            {
                var border = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(34, 0, 0, 0)
                    ),
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
                    Foreground = System.Windows.Media.Brushes.White
                });

                panel.Children.Add(new TextBlock
                {
                    Text = update.Date,
                    FontSize = 12,
                    FontWeight = FontWeights.ExtraLight,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 2, 0, 6)
                });

                foreach (var change in update.Changes)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "• " + change,
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.LightGray
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

            ExplosionPresetDropdown.IsEnabled =
                ExplosionResetSettings.Enabled;

            Logger.Log($"Explosion reset enabled = {ExplosionResetSettings.Enabled}");
        }

        private void ExplosionPresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExplosionPresetDropdown.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                ExplosionResetSettings.Preset =
                    Enum.Parse<ExplosionResetPreset>(tag);

                ExplosionResetSettings.Save();

                Logger.Log($"Explosion reset preset set to {ExplosionResetSettings.Preset}");
            }
        }

        // ================= BACKGROUND =================

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

        // ================= LAUNCH =================

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is not InstalledVersion target)
                return;

            string common = AppPaths.GetSteamCommonPathFor(target.HomeFolder);
            string activePath = Path.Combine(common, ACTIVE);

            try
            {
                bool isGameRunning =
                    Process.GetProcessesByName("Subnautica").Length > 0;

                bool isAlreadyActive =
                    Directory.Exists(activePath) &&
                    Path.GetFullPath(target.HomeFolder)
                        .Equals(Path.GetFullPath(activePath),
                                StringComparison.OrdinalIgnoreCase);

                // 🚫 Same version + running → BLOCK
                if (isAlreadyActive && isGameRunning)
                {
                    MessageBox.Show(
                        "This Subnautica version is already running.",
                        "Launch Blocked",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    SetStatus(target, VersionStatus.Active);
                    return;
                }

                // ✅ Same version + not running → just launch
                if (isAlreadyActive && !isGameRunning)
                {
                    SetStatus(target, VersionStatus.Launching);

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(activePath, "Subnautica.exe"),
                        WorkingDirectory = activePath,
                        UseShellExecute = true
                    });

                    SetStatus(target, VersionStatus.Active);
                    return;
                }

                // 🔁 Different version → switch required
                SetStatus(target, VersionStatus.Switching);

                bool wasRunning = await CloseGameIfRunning();

                if (wasRunning)
                {
                    await Task.Delay(1000);

                    int yearGroup = BuildYearResolver.ResolveGroupedYear(target.HomeFolder);
                    if (yearGroup >= 2022)
                        await Task.Delay(1500);
                }

                await RestoreUntilGone(common);

                if (Directory.Exists(activePath))
                    throw new IOException("Subnautica folder still exists after restore.");

                // Safe swap (Unity file-lock tolerant)
                var start = DateTime.UtcNow;
                while (true)
                {
                    try
                    {
                        Directory.Move(target.HomeFolder, activePath);
                        break;
                    }
                    catch (IOException)
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > 10000)
                            throw;

                        await Task.Delay(250);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if ((DateTime.UtcNow - start).TotalMilliseconds > 10000)
                            throw;

                        await Task.Delay(250);
                    }
                }

                await Task.Delay(250);

                // 🚀 LAUNCH
                SetStatus(target, VersionStatus.Launching);

                // allow UI to repaint
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(activePath, "Subnautica.exe"),
                    WorkingDirectory = activePath,
                    UseShellExecute = true
                });
                await Task.Delay(250);
                SetStatus(target, VersionStatus.Active);
                LoadInstalledVersions();

            }
            catch (Exception ex)
            {
                SetStatus(target, VersionStatus.Idle);

                MessageBox.Show(
                    ex.Message,
                    "Launch Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // ================= CORE SAFETY =================

        private static async Task RestoreUntilGone(string common)
        {
            string active = Path.Combine(common, ACTIVE);
            if (!Directory.Exists(active))
                return;

            var start = DateTime.UtcNow;

            while (Directory.Exists(active))
            {
                string info = Path.Combine(active, "Version.info");
                string target;

                if (File.Exists(info))
                {
                    var v = InstalledVersion.FromInfo(active, info)
                        ?? throw new IOException("Invalid Version.info");

                    target = Path.Combine(common, v.FolderName);
                }
                else
                {
                    target = Path.Combine(common, UNMANAGED);
                }

                if (Directory.Exists(target))
                    Directory.Delete(target, true);

                Directory.Move(active, target);
                await Task.Delay(100);

                if ((DateTime.UtcNow - start).TotalMilliseconds > 5000)
                    throw new IOException("Timed out restoring Subnautica.");
            }
        }

        private static async Task<bool> CloseGameIfRunning()
        {
            bool closedAnything = false;

            // Subnautica (Original)
            closedAnything |= await CloseProcessAsync("Subnautica");

            // Subnautica: Below Zero
            closedAnything |= await CloseProcessAsync("SubnauticaZero");

            return closedAnything;
        }

        private static async Task<bool> CloseProcessAsync(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return false;

            var p = processes[0];

            try
            {
                p.CloseMainWindow();

                if (!p.WaitForExit(10_000))
                    p.Kill(true);
            }
            catch
            {
                try { p.Kill(true); } catch { }
            }

            await Task.Delay(500);
            return true;
        }

        // ================= VERSION LIST =================

        private void LoadInstalledVersions()
        {
            var list = VersionLoader.LoadInstalled();

            foreach (var v in list)
            {
                string common = AppPaths.GetSteamCommonPathFor(v.HomeFolder);
                string active = Path.Combine(common, ACTIVE);
                bool isActive = Path.GetFullPath(v.HomeFolder)
                    .Equals(Path.GetFullPath(active),
                        StringComparison.OrdinalIgnoreCase);

                v.Status = isActive ? VersionStatus.Active : VersionStatus.Idle;
            }

            InstalledVersionsList.ItemsSource = list;
            InstalledVersionsList.Items.Refresh();
        }

        private void SetStatus(InstalledVersion v, VersionStatus status)
        {
            v.Status = status;
            InstalledVersionsList.Items.Refresh();
        }

        // ================= BUTTONS =================

        private void InstallVersion_Click(object s, RoutedEventArgs e)
        {
            new AddVersionWindow { Owner = this }.ShowDialog();
            LoadInstalledVersions();
        }

        private void OpenInstallFolder_Click(object s, RoutedEventArgs e)
        {
            // If a version is selected, open its folder
            if (InstalledVersionsList.SelectedItem is InstalledVersion v &&
                Directory.Exists(v.HomeFolder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = v.HomeFolder,
                    UseShellExecute = true
                });

                return;
            }

            // Fallback: open Steam common directory
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = AppPaths.SteamCommonPath,
                UseShellExecute = true
            });
        }

        private void EditVersion_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is not InstalledVersion v)
                return;

            var win = new EditVersionWindow(v) { Owner = this };
            if (win.ShowDialog() == true)
                LoadInstalledVersions();
        }

        // ================= NAV =================        
        [SupportedOSPlatform("windows")]
        private async Task OnResetHotkeyPressed()
        {
            if (!_macroEnabled)
                return;

            // 🔴 Abort if already running
            if (_explosionRunning)
            {
                Logger.Warn("[ExplosionReset] ABORT requested");

                _explosionCts?.Cancel();
                _explosionCts = null;
                _explosionRunning = false;

                ExplosionResetService.Abort();
                return;
            }

            if (ResetGamemodeDropdown.SelectedItem is not ComboBoxItem item)
                return;

            var mode = Enum.Parse<GameMode>((string)item.Content);

            // ❌ Explosion reset disabled → normal reset
            if (!ExplosionResetSettings.Enabled)
            {
                await ResetMacroService.RunAsync(mode);
                return;
            }

            // ================= BUILD YEAR CHECK (INLINE COPY) =================
            var proc = Process.GetProcessesByName("Subnautica").FirstOrDefault();
            if (proc == null)
            {
                await ResetMacroService.RunAsync(mode);
                return;
            }

            string root = Path.GetDirectoryName(proc.MainModule!.FileName!)!;
            int buildYear = -1;

            string[] paths =
            {
        Path.Combine(root, "__buildtime.txt"),
        Path.Combine(root, "Subnautica_Data", "StreamingAssets", "__buildtime.txt")
    };

            foreach (var p in paths)
            {
                if (!File.Exists(p)) continue;

                if (DateTime.TryParse(File.ReadAllText(p), out var dt))
                {
                    buildYear = dt.Year;
                    break;
                }
            }

            // ✅ ONLY 2018 OR 2023 USE EXPLOSION RESET
            bool canUseExplosionReset = buildYear == 2018 || buildYear == 2023;

            if (!canUseExplosionReset)
            {
                Logger.Warn($"[ExplosionReset] Unsupported build year {buildYear}, falling back");
                await ResetMacroService.RunAsync(mode);
                return;
            }

            // ================= RUN EXPLOSION RESET =================
            _explosionCts = new CancellationTokenSource();
            _explosionRunning = true;

            try
            {
                await ExplosionResetService.RunAsync(
                    mode,
                    ExplosionResetSettings.Preset,
                    _explosionCts.Token
                );
            }
            finally
            {
                _explosionRunning = false;
                _explosionCts = null;
            }
        }


        [SupportedOSPlatform("windows")]
        private void ResetMacroToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _macroEnabled = !_macroEnabled;

            Logger.Log($"Reset Macro Enabled={_macroEnabled}");

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

            Logger.Log($"Explosion reset overlay enabled = {ExplosionResetSettings.OverlayEnabled}");
        }

        private void ExplosionTrackToggle_Click(object sender, RoutedEventArgs e)
        {
            ExplosionResetSettings.TrackResets = !ExplosionResetSettings.TrackResets;
            ExplosionResetSettings.Save();

            ExplosionTrackToggleButton.Content =
                ExplosionResetSettings.TrackResets ? "Enabled" : "Disabled";

            ExplosionTrackToggleButton.Background =
                ExplosionResetSettings.TrackResets ? Brushes.Green : Brushes.DarkRed;

            Logger.Log($"Track explosion resets = {ExplosionResetSettings.TrackResets}");
        }

        private void HardcoreSaveDeleterToggle_Click(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Current.HardcoreSaveDeleterEnabled =
                !LauncherSettings.Current.HardcoreSaveDeleterEnabled;
            LauncherSettings.Save();

            UpdateHardcoreSaveDeleterVisualState();
            Logger.Log($"Hardcore save deleter enabled = {LauncherSettings.Current.HardcoreSaveDeleterEnabled}");
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
                return activeOnly
                    ? FindActiveRoots("Subnautica")
                    : VersionLoader.LoadInstalled()
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

            if (game == HardcoreSaveTargetGame.BelowZero)
                return activeOnly
                    ? FindActiveRoots("SubnauticaZero")
                    : BZVersionLoader.LoadInstalled()
                        .Select(v => v.HomeFolder)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

            var roots = new List<string>();

            if (activeOnly)
            {
                roots.AddRange(FindActiveRoots("Subnautica"));
                roots.AddRange(FindActiveRoots("SubnauticaZero"));
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

            RenameOnCloseButton.Content =
                _renameOnCloseEnabled ? "Enabled" : "Disabled";

            RenameOnCloseButton.Background =
                _renameOnCloseEnabled ? Brushes.Green : Brushes.DarkRed;

            SaveMacroSettings();

            Logger.Log($"Rename on close enabled = {_renameOnCloseEnabled}");
        }

        private void SetResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            ResetHotkeyBox.Text = "Press a key...";
            PreviewKeyDown += CaptureResetKey;
        }
        [SupportedOSPlatform("windows")]
        private void CaptureResetKey(object sender, KeyEventArgs e)
        {
            _resetKey = e.Key;
            ResetHotkeyBox.Text = _resetKey.ToString();

            PreviewKeyDown -= CaptureResetKey;

            SaveMacroSettings();
            RegisterResetHotkey(); // 🔥 THIS WAS MISSING
        }

        private void SaveMacroSettings()
        {
            ComboBoxItem? item = ResetGamemodeDropdown.SelectedItem as ComboBoxItem;

            var mode = item != null
                ? Enum.Parse<GameMode>((string)item.Content)
                : GameMode.Survival; // or your default

            LauncherSettings.Current.ResetMacroEnabled = _macroEnabled;
            LauncherSettings.Current.ResetHotkey = _resetKey;
            LauncherSettings.Current.ResetGameMode = mode;
            LauncherSettings.Current.RenameOnCloseEnabled = _renameOnCloseEnabled;
            LauncherSettings.Save();
        }

        [SupportedOSPlatform("windows")]
        private void LoadMacroSettings()
        {
            LauncherSettings.Load();
            var settings = LauncherSettings.Current;

            _macroEnabled = settings.ResetMacroEnabled;
            _resetKey = settings.ResetHotkey;
            var mode = settings.ResetGameMode;
            _renameOnCloseEnabled = settings.RenameOnCloseEnabled;

            Logger.Log($"Reset Macro Settings Loaded Successfully. Enabled={_macroEnabled}, Hotkey={_resetKey}, Mode={mode}");

            ResetHotkeyBox.Text = _resetKey.ToString();
            ResetGamemodeDropdown.SelectedItem =
                ResetGamemodeDropdown.Items
                    .Cast<ComboBoxItem>()
                    .FirstOrDefault(i => (string)i.Content == mode.ToString());

            UpdateResetMacroVisualState();
            RegisterResetHotkey();
        }

        private void ResetGamemodeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveMacroSettings();
        }
        private void ShowView(UIElement view)
        {
            InstallsView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            ToolsView.Visibility = Visibility.Collapsed;
            InfoView.Visibility = Visibility.Collapsed;

            view.Visibility = Visibility.Visible;
            // ✅ Launch button ONLY visible on Versions List
            LaunchButton.Visibility =
                view == InstallsView ? Visibility.Visible : Visibility.Hidden;
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


        // ================= SHUTDOWN =================

        private async void MainWindow_Closing(object? s, CancelEventArgs e)
        {
            await Task.Yield(); // 👈 satisfies compiler, zero behavior change
            Logger.Log("Launcher is now closing");

            ExplosionResetDisplayController.ForceClose(); // 🔥 REQUIRED

            UnregisterHotKey(
                new WindowInteropHelper(this).Handle,
                HOTKEY_ID);

            if (!_renameOnCloseEnabled)
            {
                try
                {
                    var commonPaths = AppPaths.SteamCommonPaths;
                    if (commonPaths.Count == 0)
                        commonPaths = new List<string> { AppPaths.SteamCommonPath };

                    foreach (var common in commonPaths)
                        await RestoreUntilGone(common);
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "Failed to Restore Original Folder Names");
                }
            }

            Logger.Log("Launcher Successfully Shutdown");
        }
    }
}
