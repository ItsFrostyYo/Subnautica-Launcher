using SubnauticaLauncher; // Logger
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Macros;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        private static readonly string BgPreset =
        Path.Combine(AppPaths.DataPath, "BPreset.txt");

        private const string DefaultBg = "Grassy Plateau";      

        public MainWindow()
        {
            Logger.Log("MainWindow constructor");

            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // ================= TITLE BAR =================

        private void UpdateResetMacroVisualState()
        {
            ResetMacroToggleButton.Content =
                _macroEnabled ? "Enabled" : "Disabled";

            ResetMacroToggleButton.Background =
                _macroEnabled ? Brushes.Green : Brushes.DarkRed;
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

            Logger.Log("Launcher data directory prepared");

            if (!File.Exists(BgPreset))
                File.WriteAllText(BgPreset, DefaultBg);

            string bg = File.ReadAllText(BgPreset).Trim();
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            Logger.Log($"Applying background: {bg}");

            ApplyBackground(bg);
            SyncThemeDropdown(bg);

            LoadInstalledVersions();
            LoadMacroSettings();

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
        private void InfoTab_Click(object s, RoutedEventArgs e)
        {
            ShowView(InfoView);
            BuildUpdatesView();
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
                File.WriteAllText(BgPreset, name);
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

            File.WriteAllText(BgPreset, dlg.FileName);
            ApplyBackground(dlg.FileName);
            ThemeDropdown.SelectedItem = null;
        }

        // ================= LAUNCH =================

        private async void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is not InstalledVersion target)
                return;

            string common = AppPaths.SteamCommonPath;
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
            var p = Process.GetProcessesByName("Subnautica");
            if (p.Length == 0)
                return false;

            p[0].CloseMainWindow();

            if (!p[0].WaitForExit(10_000))
                p[0].Kill(true);

            await Task.Delay(500);
            return true;
        }

        // ================= VERSION LIST =================

        private void LoadInstalledVersions()
        {
            var list = VersionLoader.LoadInstalled();
            string active = Path.Combine(AppPaths.SteamCommonPath, ACTIVE);

            foreach (var v in list)
            {
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
            Process.Start("explorer.exe", AppPaths.SteamCommonPath);
        }

        private void RenameVersion_Click(object s, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is not InstalledVersion v)
                return;

            if (v.IsActive)
            {
                MessageBox.Show("Cannot rename active version.");
                return;
            }

            new RenameVersionWindow(v) { Owner = this }.ShowDialog();
            LoadInstalledVersions();
        }

        private void DeleteVersion_Click(object s, RoutedEventArgs e)
        {
            if (InstalledVersionsList.SelectedItem is not InstalledVersion v)
                return;

            if (v.IsActive)
            {
                MessageBox.Show(
                    "You cannot delete the currently active version.",
                    "Delete Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new DeleteVersionDialog
            {
                Owner = this
            };

            dialog.ShowDialog();

            try
            {
                switch (dialog.Choice)
                {
                    case DeleteChoice.Cancel:
                        return;

                    case DeleteChoice.RemoveFromLauncher:
                        string infoPath = Path.Combine(v.HomeFolder, "Version.info");
                        if (File.Exists(infoPath))
                            File.Delete(infoPath);
                        break;

                    case DeleteChoice.DeleteGame:
                        Directory.Delete(v.HomeFolder, true);
                        break;
                }

                LoadInstalledVersions();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Delete Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ================= NAV =================        

        private async Task OnResetHotkeyPressed()
        {
            if (ResetGamemodeDropdown.SelectedItem is not ComboBoxItem item)
            {
                Logger.Warn("Macro Reset Hotkey Pressed, Macro Failed - No Gamemode Selected");
                return;
            }

            if (item.Content is not string modeText)
                return;

            var mode = Enum.Parse<GameMode>(modeText);

            Logger.Log($"Running Reset Macro Successfully: Gamemode={mode}");

            await ResetMacroService.RunAsync(mode);
        }

        private void ResetMacroToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _macroEnabled = !_macroEnabled;

            Logger.Log($"Reset Macro Enabled={_macroEnabled}");

            UpdateResetMacroVisualState();
            SaveMacroSettings();
            RegisterResetHotkey();
        }

        private void SetResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            ResetHotkeyBox.Text = "Press a key...";
            PreviewKeyDown += CaptureResetKey;
        }

        private void CaptureResetKey(object sender, KeyEventArgs e)
        {
            _resetKey = e.Key;
            ResetHotkeyBox.Text = _resetKey.ToString();

            PreviewKeyDown -= CaptureResetKey;

            SaveMacroSettings();
            RegisterResetHotkey(); // 🔥 THIS WAS MISSING
        }

        private static readonly string SettingsPath =
    Path.Combine(AppPaths.DataPath, "Settings.info");

        private void SaveMacroSettings()
        {
            if (ResetGamemodeDropdown.SelectedItem is not ComboBoxItem item)
                return;

            if (item.Content is not string modeText)
                return;

            var mode = Enum.Parse<GameMode>(modeText);

            File.WriteAllLines(SettingsPath, new[]
            {
        $"Enabled={_macroEnabled}",
        $"Hotkey={_resetKey}",
        $"Mode={mode}"
    });

            Logger.Log($"Reset Macro Settings Saved Successfully. Enabled={_macroEnabled}, Hotkey={_resetKey}, Gamemode={mode}");
        }

        private void LoadMacroSettings()
        {
            if (!File.Exists(SettingsPath))
            {
                Logger.Log("Reset Macro Settings could not be found Setting to Default.");
                UpdateResetMacroVisualState();
                return;
            }

            var dict = File.ReadAllLines(SettingsPath)
                .Select(l => l.Split('='))
                .ToDictionary(x => x[0], x => x[1]);

            _macroEnabled = bool.Parse(dict["Enabled"]);
            _resetKey = Enum.Parse<Key>(dict["Hotkey"]);
            var mode = Enum.Parse<GameMode>(dict["Mode"]);

            Logger.Log($"Reset Macro Settings Loaded Successfully. Enabled={_macroEnabled}, Hotkey={_resetKey}, Mode={mode}");

            ResetHotkeyBox.Text = _resetKey.ToString();
            ResetGamemodeDropdown.SelectedItem =
                ResetGamemodeDropdown.Items
                    .Cast<ComboBoxItem>()
                    .First(i => (string)i.Content == mode.ToString());

            UpdateResetMacroVisualState();
            RegisterResetHotkey();
        }

        private void ResetGamemodeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveMacroSettings();
        }
        private void ShowView(UIElement v)
        {
            InstallsView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            InfoView.Visibility = Visibility.Collapsed;
            v.Visibility = Visibility.Visible;
        }

        private void InstallsTab_Click(object s, RoutedEventArgs e) => ShowView(InstallsView);
        private void SettingsTab_Click(object s, RoutedEventArgs e) => ShowView(SettingsView);

        // ================= SHUTDOWN =================

        private async void MainWindow_Closing(object? s, CancelEventArgs e)
        {
            Logger.Log("Launcher is now closing");

            UnregisterHotKey(
                new WindowInteropHelper(this).Handle,
                HOTKEY_ID);

            try
            {
                await RestoreUntilGone(AppPaths.SteamCommonPath);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Failed to Restore Original Folder Names");                
            }

            Logger.Log("Launcher Successfully Shutdown");
        }
    }
}