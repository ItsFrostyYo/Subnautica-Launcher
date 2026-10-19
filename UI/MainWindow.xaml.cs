using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Services;
using SubnauticaLauncher.Updates;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher.UI;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsOrderingProperty = DependencyProperty.Register(
        nameof(IsOrdering),
        typeof(bool),
        typeof(MainWindow),
        new PropertyMetadata(false));

    private readonly LauncherVersionStore _versionStore = new();
    private readonly LauncherConfig _config = LauncherConfig.Load();
    private readonly ObservableCollection<LauncherVersion> _versions;
    private readonly ObservableCollection<PluginCardViewModel> _installedPlugins = [];
    private readonly ObservableCollection<PluginCardViewModel> _browsePlugins = [];
    private DateTime _lastTileClickUtc = DateTime.MinValue;
    private LauncherVersion? _selectedVersion;
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;
    private readonly LauncherUpdateService _updateService = new();
    private readonly UpdaterUpdateService _updaterUpdateService = new();
    private readonly System.Windows.Threading.DispatcherTimer _updateTimer;
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private LauncherPluginManager? _pluginManager;
    private Version? _lastPromptedLauncherUpdate;
    private bool _refreshingPlugins;
    private bool _maintainingPlugins;
    private bool _pluginActionInProgress;
    private bool _forceSteamLaunch;
    private string? _activeVersionFolder;
    private bool _showInstalledPlugins = true;
    private bool _updateHandoffStarted;

    public bool IsOrdering
    {
        get => (bool)GetValue(IsOrderingProperty);
        private set => SetValue(IsOrderingProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        Version? version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        LauncherVersionText.Text = $"v{version?.ToString(3) ?? "0.0.0"} By ItsFrostiSR";
        _versions = new ObservableCollection<LauncherVersion>(_versionStore.Load());
        _activeVersionFolder = _config.ActiveVersionFolder;
        _forceSteamLaunch = _config.ForceLaunchWithSteam;
        VersionTiles.ItemsSource = _versions;
        RefreshPluginCards();
        ApplyBackground(_config.BackgroundPreset);
        SyncThemeDropdown(_config.BackgroundPreset);
        UpdateForceSteamButton();
        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += async (_, _) =>
        {
            RefreshVersionStatus();
            if (_pluginManager is null || _maintainingPlugins) return;
            _maintainingPlugins = true;
            try { await _pluginManager.MaintainEnabledPluginsAsync(); }
            finally { _maintainingPlugins = false; }
        };
        _statusTimer.Start();
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) =>
        {
            await RunUpdateChecksIfIdleAsync();
        };
        Loaded += async (_, _) =>
        {
            BuildUpdatesView();
            _pluginManager = new LauncherPluginManager(new WindowInteropHelper(this).Handle);
            await RunUpdateChecksIfIdleAsync(updatePlugins: false);
            if (_updateHandoffStarted) return;
            await _pluginManager.UpdateInstalledPluginsAsync(() => false);
            await _pluginManager.InitializeAsync();
            await ReloadPluginCardsAsync(checkForUpdates: false);
            _updateTimer.Start();
        };
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            _updateTimer.Stop();
            ImGuiAddVersionWindow.CloseAll();
            _pluginManager?.Dispose();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void AddVersion_Click(object sender, RoutedEventArgs e)
    {
        ImGuiAddVersionWindow.Show(_versionStore, new WindowInteropHelper(this).Handle, version =>
        {
            Dispatcher.Invoke(() =>
            {
                _versions.Add(version);
                SelectVersion(version);
            });
        });
    }

    private void ChangeOrdering_Click(object sender, RoutedEventArgs e)
    {
        IsOrdering = !IsOrdering;
        ChangeOrderingButton.Content = IsOrdering ? "Done Ordering" : "Change Ordering";
    }

    private void MoveVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not LauncherVersion version ||
            !int.TryParse(button.CommandParameter?.ToString(), out int offset))
            return;

        if (!_versionStore.TryMove(version, offset, out _))
            return;

        string? selectedFolder = _selectedVersion?.GameFolder;
        _versions.Clear();
        foreach (LauncherVersion item in _versionStore.Load()) _versions.Add(item);
        SelectVersion(selectedFolder is null
            ? null
            : _versions.FirstOrDefault(item => string.Equals(item.GameFolder, selectedFolder, StringComparison.OrdinalIgnoreCase)));
    }

    private void PlayNav_Click(object sender, RoutedEventArgs e) => ShowPlayView();
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowNavigationView(SettingsView);
    private async void PluginsNav_Click(object sender, RoutedEventArgs e)
    {
        ShowNavigationView(PluginsView);
        await ReloadPluginCardsAsync(checkForUpdates: false);
    }
    private void InfoNav_Click(object sender, RoutedEventArgs e) => ShowNavigationView(InfoView);

    private void ShowPlayView()
    {
        SettingsView.Visibility = Visibility.Collapsed;
        PluginsView.Visibility = Visibility.Collapsed;
        InfoView.Visibility = Visibility.Collapsed;
        AddVersionButton.Visibility = Visibility.Visible;
        ChangeOrderingButton.Visibility = Visibility.Visible;
        PlayVersionsPanel.Visibility = Visibility.Visible;
        UpdateNavigationState(PlayNavButton);
    }

    private void ShowNavigationView(FrameworkElement view)
    {
        SettingsView.Visibility = view == SettingsView ? Visibility.Visible : Visibility.Collapsed;
        PluginsView.Visibility = view == PluginsView ? Visibility.Visible : Visibility.Collapsed;
        InfoView.Visibility = view == InfoView ? Visibility.Visible : Visibility.Collapsed;
        AddVersionButton.Visibility = Visibility.Collapsed;
        ChangeOrderingButton.Visibility = Visibility.Collapsed;
        PlayVersionsPanel.Visibility = Visibility.Collapsed;
        UpdateNavigationState(view == SettingsView ? SettingsNavButton : view == PluginsView ? PluginsNavButton : InfoNavButton);
    }

    private void UpdateNavigationState(System.Windows.Controls.Button activeButton)
    {
        foreach (System.Windows.Controls.Button button in new[] { PlayNavButton, SettingsNavButton, PluginsNavButton, InfoNavButton })
            button.Tag = ReferenceEquals(button, activeButton) ? "Active" : null;
    }

    private void InstalledPluginsTab_Click(object sender, RoutedEventArgs e)
    {
        _showInstalledPlugins = true;
        RefreshPluginCards();
    }

    private void BrowsePluginsTab_Click(object sender, RoutedEventArgs e)
    {
        _showInstalledPlugins = false;
        RefreshPluginCards();
    }

    private void PluginSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPluginCards();

    private async void PluginAction_Click(object sender, RoutedEventArgs e)
    {
        if (_pluginActionInProgress || _pluginManager is null || (sender as FrameworkElement)?.Tag is not PluginCardViewModel plugin) return;
        _pluginActionInProgress = true;
        try
        {
            if (!plugin.IsInstalled) await _pluginManager.InstallOrUpdateAsync(plugin.Id);
            else await _pluginManager.SetEnabledAsync(plugin.Id, !plugin.IsActive);
            await ReloadPluginCardsAsync(checkForUpdates: false);
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(exception.Message, "Plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            await ReloadPluginCardsAsync(checkForUpdates: false);
        }
        finally
        {
            _pluginActionInProgress = false;
        }
    }

    private async void PluginUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pluginManager is null || (sender as FrameworkElement)?.Tag is not PluginCardViewModel plugin) return;
        if (IsAnySubnauticaRunning())
        {
            WpfMessageBox.Show("Plugin updates are deferred until Subnautica is closed.", "Plugin Update", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            await _pluginManager.InstallOrUpdateAsync(plugin.Id);
            await ReloadPluginCardsAsync(checkForUpdates: false);
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(exception.Message, "Plugin Update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PluginSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_pluginManager is null || (sender as FrameworkElement)?.Tag is not PluginCardViewModel plugin) return;
        try
        {
            await ImGuiPluginSettingsWindow.ShowAsync(_pluginManager, plugin.Id, new WindowInteropHelper(this).Handle);
            await ReloadPluginCardsAsync(checkForUpdates: false);
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(exception.Message, "Plugin Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PluginRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_pluginManager is null || (sender as FrameworkElement)?.Tag is not PluginCardViewModel plugin) return;
        MessageBoxResult result = WpfMessageBox.Show(
            $"Remove {plugin.Name}?\n\nThe installed plugin files will be deleted. Its saved settings and data will be kept.",
            "Remove Plugin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _pluginManager.RemoveAsync(plugin.Id);
            await ReloadPluginCardsAsync(checkForUpdates: false);
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(exception.Message, "Remove Plugin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ReloadPluginCardsAsync(bool checkForUpdates)
    {
        if (_pluginManager is null || _refreshingPlugins) return;
        _refreshingPlugins = true;
        try
        {
            IReadOnlyList<LauncherPluginSnapshot> snapshots = await _pluginManager.GetSnapshotsAsync(checkForUpdates);
            _installedPlugins.Clear();
            _browsePlugins.Clear();
            foreach (LauncherPluginSnapshot snapshot in snapshots)
            {
                var card = new PluginCardViewModel(snapshot);
                if (snapshot.IsInstalled) _installedPlugins.Add(card);
                else _browsePlugins.Add(card);
            }
            RefreshPluginCards();
        }
        finally
        {
            _refreshingPlugins = false;
        }
    }

    private void RefreshPluginCards()
    {
        if (PluginCards is null || PluginSearchBox is null) return;

        string query = PluginSearchBox.Text.Trim();
        IEnumerable<PluginCardViewModel> source = _showInstalledPlugins ? _installedPlugins : _browsePlugins;
        PluginCardViewModel[] visible = source.Where(plugin =>
            query.Length == 0 ||
            plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            plugin.Author.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            plugin.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        PluginCards.ItemsSource = visible;
        PluginSearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstalledPluginsTabButton.Content = $"Installed ({_installedPlugins.Count})";
        InstalledPluginsTabButton.Tag = _showInstalledPlugins ? "Active" : null;
        BrowsePluginsTabButton.Tag = _showInstalledPlugins ? null : "Active";
        PluginEmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        PluginEmptyText.Text = query.Length > 0
            ? "No plugins match your search."
            : _showInstalledPlugins ? "No plugins are installed." : "No plugins are available to browse yet.";
    }

    private void VersionTile_Click(object sender, RoutedEventArgs e)
    {
        if (DateTime.UtcNow - _lastTileClickUtc < TimeSpan.FromMilliseconds(100))
            return;

        _lastTileClickUtc = DateTime.UtcNow;
        if ((sender as FrameworkElement)?.Tag is LauncherVersion version)
            SelectVersion(ReferenceEquals(_selectedVersion, version) ? null : version);
    }

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LauncherVersion version || !System.IO.Directory.Exists(version.GameFolder))
            return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = version.GameFolder,
            UseShellExecute = true
        });
    }

    private void EditVersion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LauncherVersion version)
            ImGuiAddVersionWindow.ShowEdit(_versionStore, new WindowInteropHelper(this).Handle, version, updated =>
            {
                Dispatcher.Invoke(() =>
                {
                    _versions.Clear();
                    foreach (LauncherVersion item in _versionStore.Load()) _versions.Add(item);
                    SelectVersion(updated is null ? null : _versions.FirstOrDefault(item => string.Equals(item.GameFolder, updated.GameFolder, StringComparison.OrdinalIgnoreCase)));
                });
            });
    }

    private void SelectVersion(LauncherVersion? version)
    {
        if (_selectedVersion is not null)
            _selectedVersion.IsSelected = false;

        _selectedVersion = version;
        if (_selectedVersion is not null)
            _selectedVersion.IsSelected = true;

        SelectedVersionText.Text = version?.DisplayName ?? "Select a version to enable launch.";
        SwitchButton.IsEnabled = version is not null;
        LaunchButton.IsEnabled = version is not null;
        RefreshVersionStatus();
    }

    private void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVersion is null)
            return;

        LauncherVersion? running = GetRunningVersion();
        if (ReferenceEquals(running, _selectedVersion))
            _ = RestartSelectedVersionAsync();
        else if (running is not null)
            _ = SwitchAndLaunchAsync();
        else
            SetActiveVersion(_selectedVersion);
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVersion is null)
            return;

        LauncherVersion? running = GetRunningVersion();
        if (ReferenceEquals(running, _selectedVersion))
        {
            await CloseSubnauticaAsync();
            RefreshVersionStatus();
            return;
        }

        if (running is not null)
            await CloseSubnauticaAsync();

        await LaunchSelectedVersionAsync();
    }

    private async Task SwitchAndLaunchAsync()
    {
        await CloseSubnauticaAsync();
        await LaunchSelectedVersionAsync();
    }

    private async Task LaunchSelectedVersionAsync()
    {
        if (_selectedVersion is null)
            return;

        try
        {
            string launchFolder = PrepareLaunchFolder(_selectedVersion);
            string executable = Path.Combine(launchFolder, "Subnautica.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("The selected version no longer contains Subnautica.exe.", executable);

            SetActiveVersion(_selectedVersion);
            ProcessStartInfo startInfo;
            if (SpeedrunningModDetector.IsDetected(launchFolder))
            {
                string launchCommand = SpeedrunningModDetector.GetLaunchCommandPath(launchFolder);
                startInfo = new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = string.IsNullOrWhiteSpace(_selectedVersion.LaunchOptions)
                        ? $"/c \"\"{launchCommand}\"\""
                        : $"/c \"\"{launchCommand}\" {_selectedVersion.LaunchOptions}\"",
                    WorkingDirectory = launchFolder,
                    UseShellExecute = false
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = launchFolder,
                    // Steam owns the argument flow after a forced folder switch.
                    Arguments = _forceSteamLaunch ? string.Empty : _selectedVersion.LaunchOptions,
                    UseShellExecute = false
                };
            }
            Process.Start(startInfo);
            await Task.Delay(300);
            RefreshVersionStatus();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(exception.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private LauncherVersion? GetRunningVersion()
    {
        foreach (Process process in Process.GetProcessesByName("Subnautica"))
        {
            try
            {
                string executable = process.MainModule?.FileName ?? string.Empty;
                LauncherVersion? version = _versions.FirstOrDefault(item => executable.StartsWith(Path.GetFullPath(item.GameFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (version is not null)
                    return version;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return null;
    }

    private static async Task CloseSubnauticaAsync()
    {
        foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName("Subnautica"))
        {
            try { process.CloseMainWindow(); if (!process.WaitForExit(5000)) process.Kill(true); }
            catch { }
            finally { process.Dispose(); }
        }
        await Task.Delay(200);
    }

    private async Task RestartSelectedVersionAsync()
    {
        if (_selectedVersion is null) return;
        await CloseSubnauticaAsync();
        RefreshVersionStatus();
        await LaunchSelectedVersionAsync();
    }

    private void SetActiveVersion(LauncherVersion version)
    {
        _activeVersionFolder = Path.GetFullPath(version.GameFolder);
        _config.ActiveVersionFolder = _activeVersionFolder;
        _config.Save();
        RefreshVersionStatus();
    }

    private string PrepareLaunchFolder(LauncherVersion version)
    {
        if (!_forceSteamLaunch)
        {
            SteamAppIdFileService.Ensure(version.GameFolder);
            return version.GameFolder;
        }

        string? commonPath = SteamAppIdFileService.FindSteamCommonPath(version.GameFolder);
        if (commonPath is null)
            throw new InvalidOperationException("Force to Launch With Steam requires the selected version to be inside a Steam steamapps\\common library.");

        string activePath = Path.Combine(commonPath, "Subnautica");
        if (PathsEqual(version.GameFolder, activePath))
        {
            SteamAppIdFileService.Remove(activePath);
            return activePath;
        }

        if (Directory.Exists(activePath))
        {
            LauncherVersion? activeVersion = _versions.FirstOrDefault(item => PathsEqual(item.GameFolder, activePath));
            if (activeVersion is null)
                throw new InvalidOperationException("Steam's Subnautica folder is not managed by this launcher. Move or rename it before forcing a Steam launch.");

            string restoredPath = Path.Combine(commonPath, activeVersion.FolderName);
            if (Directory.Exists(restoredPath))
                throw new InvalidOperationException($"Cannot restore the active version because '{activeVersion.FolderName}' already exists.");

            SteamAppIdFileService.Remove(activePath);
            Directory.Move(activePath, restoredPath);
            UpdateVersionFolder(activeVersion, restoredPath);
        }

        SteamAppIdFileService.Remove(version.GameFolder);
        Directory.Move(version.GameFolder, activePath);
        UpdateVersionFolder(version, activePath);
        return activePath;
    }

    private void UpdateVersionFolder(LauncherVersion version, string updatedFolder)
    {
        LauncherVersion original = new()
        {
            GameFolder = version.GameFolder,
            DisplayName = version.DisplayName,
            FolderName = version.FolderName,
            OriginalVersionId = version.OriginalVersionId,
            OriginalVersionDisplayName = version.OriginalVersionDisplayName,
            LaunchOptions = version.LaunchOptions,
            IconName = version.IconName
        };
        LauncherVersion updated = new()
        {
            GameFolder = updatedFolder,
            DisplayName = version.DisplayName,
            FolderName = version.FolderName,
            OriginalVersionId = version.OriginalVersionId,
            OriginalVersionDisplayName = version.OriginalVersionDisplayName,
            LaunchOptions = version.LaunchOptions,
            IconName = version.IconName
        };
        if (!_versionStore.TryUpdate(original, updated, out string error))
            throw new InvalidOperationException(error);

        version.GameFolder = updatedFolder;
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private void RefreshVersionStatus()
    {
        LauncherVersion? runningVersion = GetRunningVersion();
        bool running = runningVersion is not null;
        foreach (LauncherVersion version in _versions)
        {
            bool isActive = !string.IsNullOrWhiteSpace(_activeVersionFolder) &&
                string.Equals(Path.GetFullPath(version.GameFolder), _activeVersionFolder, StringComparison.OrdinalIgnoreCase);
            version.Status = ReferenceEquals(version, runningVersion) ? "Launched" : isActive ? "Active" : "Idle";
        }

        if (_selectedVersion is null)
        {
            VersionStatusText.Text = running ? "Game Running" : "No game activity.";
            VersionStatusText.Foreground = WpfBrushes.White;
            return;
        }

        SelectedVersionText.Foreground = GetStatusBrush(_selectedVersion.Status);

        if (running)
        {
            VersionStatusText.Text = "Game Running: " + runningVersion!.DisplayName;
            VersionStatusText.Foreground = GetStatusBrush(runningVersion.Status);
            LaunchButton.Content = ReferenceEquals(runningVersion, _selectedVersion) ? "Close" : "Launch";
            LaunchButton.Background = ReferenceEquals(runningVersion, _selectedVersion) ? WpfBrushes.IndianRed : (WpfBrush)FindResource("LaunchBrush");
            SwitchButton.Content = ReferenceEquals(runningVersion, _selectedVersion) ? "Restart" : "Switch";
        }
        else
        {
            LauncherVersion? active = _versions.FirstOrDefault(version => version.Status == "Active");
            VersionStatusText.Text = active is null ? "No active version." : "Active Version: " + active.DisplayName;
            VersionStatusText.Foreground = GetStatusBrush(active?.Status ?? "Idle");
            LaunchButton.Content = "Launch";
            LaunchButton.Background = (System.Windows.Media.Brush)FindResource("LaunchBrush");
            SwitchButton.Content = "Switch";
        }
    }

    private void ThemeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeDropdown.SelectedItem is ComboBoxItem { Content: string preset } && preset != "Custom")
        {
            _config.BackgroundPreset = preset;
            _config.Save();
            ApplyBackground(preset);
        }
    }

    private void ChooseCustomBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select Background Image", Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dialog.ShowDialog() != true) return;
        _config.BackgroundPreset = dialog.FileName;
        _config.Save();
        ApplyBackground(dialog.FileName);
        ThemeDropdown.SelectedItem = null;
    }

    private void SyncThemeDropdown(string preset)
    {
        foreach (ComboBoxItem item in ThemeDropdown.Items)
        {
            if (string.Equals(item.Content as string, preset, StringComparison.OrdinalIgnoreCase)) { ThemeDropdown.SelectedItem = item; return; }
        }
        ThemeDropdown.SelectedItem = null;
    }

    private void ApplyBackground(string preset)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = File.Exists(preset)
                ? new Uri(preset, UriKind.Absolute)
                : new Uri($"pack://application:,,,/Assets/Backgrounds/{preset}.png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            MainBackgroundImage.Source = image;
        }
        catch { }
    }
    private static WpfBrush GetStatusBrush(string status) => status switch
    {
        "Active" => WpfBrushes.LimeGreen,
        "Launched" => WpfBrushes.Red,
        "Launching" => WpfBrushes.Orange,
        "Switching" => WpfBrushes.Yellow,
        "Closing" => WpfBrushes.OrangeRed,
        _ => WpfBrushes.White
    };

    private void ForceSteamButton_Click(object sender, RoutedEventArgs e)
    {
        _forceSteamLaunch = !_forceSteamLaunch;
        _config.ForceLaunchWithSteam = _forceSteamLaunch;
        _config.Save();
        foreach (LauncherVersion version in _versions)
        {
            try
            {
                if (_forceSteamLaunch)
                    SteamAppIdFileService.Remove(version.GameFolder);
                else
                    SteamAppIdFileService.Ensure(version.GameFolder);
            }
            catch { }
        }
        UpdateForceSteamButton();
    }

    private void UpdateForceSteamButton()
    {
        ForceSteamButton.Content = _forceSteamLaunch ? "Enabled" : "Disabled";
        ForceSteamButton.Background = _forceSteamLaunch ? WpfBrushes.Green : (WpfBrush)FindResource("DangerBrush");
    }

    private void ManageSubnauticaSettings_Click(object sender, RoutedEventArgs e) =>
        ImGuiSubnauticaSettingsWindow.Show(_versionStore, new WindowInteropHelper(this).Handle);

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/ItsFrostyYo/Subnautica-Launcher/releases");
    private void OpenYouTube_Click(object sender, RoutedEventArgs e) => OpenUrl("https://www.youtube.com/@ItsFrostiSR");
    private void OpenDiscord_Click(object sender, RoutedEventArgs e) => OpenUrl("https://discord.com/invite/yfNYgBDcmC");

    private void BuildUpdatesView()
    {
        UpdatesPanel.Children.Clear();
        string xmlPath = Path.Combine(AppContext.BaseDirectory, "SNLUpdates.xml");
        if (!File.Exists(xmlPath)) xmlPath = Path.Combine(AppContext.BaseDirectory, "Data", "SNLUpdates.xml");
        if (!File.Exists(xmlPath)) xmlPath = Path.Combine(AppContext.BaseDirectory, "Updates", "SNLUpdates.xml");
        if (!File.Exists(xmlPath)) return;
        foreach (LauncherUpdate update in LauncherUpdateService.ParseManifest(File.ReadAllText(xmlPath)).OrderByDescending(item => item.Version))
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = $"{update.Version.ToString(3)} ({update.Title})", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = WpfBrushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 584 });
            panel.Children.Add(new TextBlock { Text = update.Date, FontSize = 12, Foreground = WpfBrushes.White, Margin = new Thickness(0, 2, 0, 6), TextWrapping = TextWrapping.Wrap, MaxWidth = 584 });
            foreach (string change in update.Changelog) panel.Children.Add(new TextBlock { Text = "- " + change, FontSize = 13, Foreground = WpfBrushes.LightGray, TextWrapping = TextWrapping.Wrap, MaxWidth = 584 });
            UpdatesPanel.Children.Add(new Border { Background = new SolidColorBrush(WpfColor.FromArgb(34, 0, 0, 0)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), MaxWidth = 608, Child = panel });
        }
    }

    private async Task RunUpdateChecksIfIdleAsync(bool updatePlugins = true)
    {
        if (IsAnySubnauticaRunning() || !await _updateCheckGate.WaitAsync(0)) return;
        UpdateProgressWindow? progressWindow = null;
        bool updateAccepted = false;
        try
        {
            LauncherUpdate? update = await _updateService.CheckForUpdateAsync();
            if (update is not null && !IsAnySubnauticaRunning() && update.Version != _lastPromptedLauncherUpdate)
            {
                _lastPromptedLauncherUpdate = update.Version;
                var prompt = new LauncherUpdatePromptWindow(update) { Owner = this };
                if (prompt.ShowDialog() == true)
                {
                    updateAccepted = true;
                    if (IsAnySubnauticaRunning()) return;
                    progressWindow = new UpdateProgressWindow(update.Version.ToString(3)) { Owner = this };
                    progressWindow.SetDetectedUpdate($"Updating to v{update.Version.ToString(3)} - {update.Title}");
                    progressWindow.Show();
                    IsEnabled = false;
                    var progress = new Progress<LauncherUpdateProgress>(progressWindow.Apply);
                    string updaterPath = await _updaterUpdateService.EnsureLatestAsync(progress);
                    if (IsAnySubnauticaRunning()) return;
                    string payloadDirectory = await _updateService.DownloadLauncherAsync(update, progress);
                    if (IsAnySubnauticaRunning())
                    {
                        try { Directory.Delete(payloadDirectory, recursive: true); } catch { }
                        return;
                    }
                    progressWindow.Apply(new LauncherUpdateProgress("Handing off to SNLUpdater...", "The launcher will close and reopen automatically.", 100));
                    _updateService.StartUpdater(payloadDirectory, update.Version, updaterPath);
                    _updateHandoffStarted = true;
                    Close();
                    return;
                }
            }

            if (updatePlugins && _pluginManager is not null && !IsAnySubnauticaRunning())
            {
                await _pluginManager.UpdateInstalledPluginsAsync(IsAnySubnauticaRunning);
                await ReloadPluginCardsAsync(checkForUpdates: false);
            }
        }
        catch (Exception exception)
        {
            if (updateAccepted)
                WpfMessageBox.Show(this, $"The launcher update could not be completed.\n\n{exception.Message}", "Launcher Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!_updateHandoffStarted)
            {
                try { progressWindow?.Close(); } catch { }
                if (IsLoaded) IsEnabled = true;
            }
            _updateCheckGate.Release();
        }
    }

    private static bool IsAnySubnauticaRunning()
    {
        Process[] processes = Process.GetProcessesByName("Subnautica");
        try { return processes.Length > 0; }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private sealed class PluginCardViewModel
    {
        public PluginCardViewModel(LauncherPluginSnapshot snapshot)
        {
            Id = snapshot.Definition.Id;
            Name = snapshot.Definition.Name;
            Author = snapshot.Definition.Author;
            Description = snapshot.Definition.Description;
            IsInstalled = snapshot.IsInstalled;
            IsEnabled = snapshot.IsEnabled;
            IsLoaded = snapshot.IsLoaded;
            IsActive = snapshot.IsEnabled && snapshot.IsLoaded;
            HasUpdate = snapshot.HasUpdate;
            Error = snapshot.Error ?? string.Empty;
            VersionLabel = snapshot.IsInstalled
                ? $"Version {snapshot.InstalledVersion?.ToString(3) ?? "Unknown"}"
                : snapshot.LatestVersion is null ? "Available" : $"Version {snapshot.LatestVersion.ToString(3)}";
            ActionText = !snapshot.IsInstalled ? "Install" : IsActive ? "Enabled" : "Disabled";
            ActionToolTip = !snapshot.IsInstalled ? "Install plugin" : IsActive ? "Click to disable plugin" : "Click to enable plugin";
            ActionBrush = !snapshot.IsInstalled || IsActive
                ? new SolidColorBrush(WpfColor.FromRgb(26, 111, 60))
                : new SolidColorBrush(WpfColor.FromRgb(139, 44, 44));
        }

        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
        public string Description { get; }
        public string VersionLabel { get; }
        public string? IconPath { get; }
        public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);
        public bool IsInstalled { get; }
        public bool IsEnabled { get; }
        public bool IsLoaded { get; }
        public bool IsActive { get; }
        public bool HasUpdate { get; }
        public string Error { get; }
        public bool HasError => Error.Length > 0;
        public string ActionText { get; }
        public string ActionToolTip { get; }
        public WpfBrush ActionBrush { get; }
    }
}
