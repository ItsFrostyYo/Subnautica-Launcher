using SubnauticaLauncher.Models;
using SubnauticaLauncher.Updater;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SubnauticaLauncher
{
    public partial class MainWindow : Window
    {
        private const string ACTIVE = "Subnautica";
        private const string UNMANAGED = "SubnauticaUnmanagedVersion";

        private static readonly string BgPath =
            Path.Combine(AppPaths.DataPath, "backgrounds");

        private static readonly string BgPreset =
            Path.Combine(BgPath, "BPreset.txt");

        private const string DefaultBg = "GrassyPlateau";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // ================= STARTUP =================

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesOnStartup();

            Directory.CreateDirectory(BgPath);

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

        // ================= BACKGROUND =================

        private void ApplyBackground(string value)
        {
            string path;

            if (File.Exists(value))
            {
                path = value;
            }
            else
            {
                path = Path.Combine(BgPath, $"{value}.png");
                if (!File.Exists(path))
                    return;
            }

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();

            BackgroundBrush.ImageSource = img;
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
                MessageBox.Show("Cannot delete active version.");
                return;
            }

            Directory.Delete(v.HomeFolder, true);
            LoadInstalledVersions();
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
        private void InfoTab_Click(object s, RoutedEventArgs e) => ShowView(InfoView);

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