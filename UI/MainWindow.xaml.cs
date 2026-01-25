using SubnauticaLauncher.Installer;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using UpdatesData = SubnauticaLauncher.Updates.Updates;

namespace SubnauticaLauncher.UI
{
    public partial class MainWindow : Window
    {
        private const string ACTIVE = "Subnautica";
        private const string UNMANAGED = "SubnauticaUnmanagedVersion";

        private static readonly string BgPreset =
        Path.Combine(AppPaths.DataPath, "BPreset.txt");

        private const string DefaultBg = "Grassy Plateau";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // ================= TITLE BAR =================

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
            // 1️⃣ RUN STARTUP INSTALLER FIRST (THIS WAS MISSING)
            if (!Directory.Exists(AppPaths.ToolsPath) ||
                !File.Exists(DepotDownloaderInstaller.DepotDownloaderExe))
            {
                var setup = new SetupWindow();
                setup.Owner = this;

                bool? result = setup.ShowDialog();
                if (result != true)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }

            // 2️⃣ CHECK FOR APP UPDATES (AS BEFORE)
            await CheckForUpdatesOnStartup();

            Directory.CreateDirectory(AppPaths.DataPath);

            if (!File.Exists(BgPreset))
                File.WriteAllText(BgPreset, DefaultBg);

            string bg = File.ReadAllText(BgPreset).Trim();
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            ApplyBackground(bg);
            SyncThemeDropdown(bg);

            LoadInstalledVersions();
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
                SetStatus(target, VersionStatus.Switching);

                bool wasRunning = await CloseGameIfRunning();
                if (wasRunning)
                    await Task.Delay(1000);

                await RestoreUntilGone(common);

                if (Directory.Exists(activePath))
                    throw new IOException("Subnautica folder still exists after restore.");

                Directory.Move(target.HomeFolder, activePath);
                await Task.Delay(250);

                SetStatus(target, VersionStatus.Launching);

                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(activePath, "Subnautica.exe"),
                    WorkingDirectory = activePath
                });

                SetStatus(target, VersionStatus.Active);
                LoadInstalledVersions();
            }
            catch (Exception ex)
            {
                SetStatus(target, VersionStatus.Idle);
                MessageBox.Show(ex.Message, "Launch Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async void MainWindow_Closing(object? s, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                await RestoreUntilGone(AppPaths.SteamCommonPath);
            }
            catch { }
        }
    }
}