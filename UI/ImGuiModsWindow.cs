using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Mods;
using SubnauticaLauncher.Services;
using SubnauticaLauncher.Updates;

namespace SubnauticaLauncher.UI;

using WpfMessageBox = System.Windows.MessageBox;

public static class ImGuiModsWindow
{
    private static readonly SubnauticaModService ModService = new();

    public static async Task ShowAsync(LauncherVersionStore store, LauncherVersion? initialVersion, IntPtr ownerHandle)
    {
        LauncherVersion[] versions = store.Load()
            .Where(version => Directory.Exists(version.GameFolder) && File.Exists(Path.Combine(version.GameFolder, "Subnautica.exe")))
            .ToArray();
        if (versions.Length == 0)
        {
            WpfMessageBox.Show("Add a Subnautica version before managing mods.", "Manage Mods", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LauncherVersion selected = versions.FirstOrDefault(version =>
            string.Equals(version.GameFolder, initialVersion?.GameFolder, StringComparison.OrdinalIgnoreCase)) ?? versions[0];

        ModInstallationSnapshot snapshot;
        try
        {
            snapshot = await ModService.ScanAsync(selected);
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show("The selected installation could not be inspected.\n\n" + exception.Message, "Manage Mods", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await RunHostAsync(versions, selected, snapshot, ownerHandle);
    }

    private static async Task InstallAsync(string loaderId, LauncherVersion version, IntPtr ownerHandle)
    {
        var progressWindow = new UpdateProgressWindow("Mods");
        Window? owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(window => new System.Windows.Interop.WindowInteropHelper(window).Handle == ownerHandle);
        if (owner is not null)
        {
            progressWindow.Owner = owner;
            progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        progressWindow.SetDetectedUpdate("Installing Mod Loader");
        progressWindow.Show();
        try
        {
            var progress = new Progress<string>(message => progressWindow.Apply(new LauncherUpdateProgress("Managing mods...", message, null)));
            await ModService.InstallAsync(loaderId, version, progress);
            progressWindow.Close();
            WpfMessageBox.Show("The mod package was installed successfully.", "Manage Mods", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch
        {
            try { progressWindow.Close(); } catch { }
            throw;
        }
    }

    private static async Task RunHostAsync(
        IReadOnlyList<LauncherVersion> versions,
        LauncherVersion selected,
        ModInstallationSnapshot snapshot,
        IntPtr ownerHandle)
    {
        string hostPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "SubnauticaLauncher.ImGuiHost.exe");
        if (!File.Exists(hostPath)) throw new FileNotFoundException("The launcher ImGui host is missing.", hostPath);

        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        Add(startInfo, "--owner", ownerHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--mode", "mods");
        Add(startInfo, "--background", ResolveBackgroundPath());
        Add(startInfo, "--mod-versions", string.Join('\n', versions.Select(version => Encode(version.GameFolder) + "\t" + Encode(version.DisplayName))));
        Add(startInfo, "--mod-selected", Encode(selected.GameFolder));
        Add(startInfo, "--mod-status", Encode(snapshot.Status));
        Add(startInfo, "--mod-warning", Encode(snapshot.Warning));
        Add(startInfo, "--mod-installed", SerializeInstalled(snapshot.InstalledMods));
        Add(startInfo, "--mod-loaders", SerializeLoaders(snapshot.Loaders));
        Add(startInfo, "--mod-folder-enabled", snapshot.HasBepInEx && !snapshot.HasSpeedrunningMod ? "1" : "0");

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The Manage Mods window could not start.");
        ImGuiAddVersionWindow.RegisterHost(process);
        try
        {
            LauncherVersion currentVersion = selected;
            ModInstallationSnapshot currentSnapshot = snapshot;
            while (!process.HasExited)
            {
                string? result = await process.StandardOutput.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(result) || result == "CLOSE") break;

                string[] values = result.Split('|');
                string command = values[0];
                try
                {
                    if (command == "MOD_SELECT" && values.Length == 2)
                    {
                        string selectedPath = Decode(values[1]);
                        currentVersion = versions.FirstOrDefault(version => string.Equals(version.GameFolder, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? currentVersion;
                    }
                    else if (command == "MOD_OPEN_FOLDER")
                    {
                        SubnauticaModService.OpenBepInExPluginsFolder(currentSnapshot.BepInExPluginsFolder);
                    }
                    else
                    {
                        if (IsSubnauticaRunning())
                            throw new InvalidOperationException("Close Subnautica before installing or removing mods.");

                        if (command == "MOD_REMOVE" && values.Length == 2)
                            ModService.RemoveInstalledMod(currentSnapshot, Decode(values[1]));
                        else if (command == "MOD_REMOVE_LOADER" && values.Length == 2)
                            ModService.RemoveLoader(currentSnapshot, Decode(values[1]));
                        else if (command == "MOD_REMOVE_INCOMPATIBLE")
                            ModService.RemoveIncompatibleLoaders(currentSnapshot);
                        else if (command == "MOD_INSTALL" && values.Length == 2)
                            await InstallAsync(Decode(values[1]), currentVersion, ownerHandle);
                        else
                            continue;
                    }

                    currentSnapshot = await ModService.ScanAsync(currentVersion);
                    await SendSnapshotAsync(process, currentVersion, currentSnapshot);
                }
                catch (Exception exception)
                {
                    await SendProtocolAsync(process, "MOD_ERROR|" + Encode(exception.Message));
                    WpfMessageBox.Show(exception.Message, "Manage Mods", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            await process.WaitForExitAsync();
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            ImGuiAddVersionWindow.UnregisterHost(process);
        }
    }

    private static Task SendSnapshotAsync(Process process, LauncherVersion selected, ModInstallationSnapshot snapshot) => SendProtocolAsync(process, string.Join('|',
        "MOD_DATA",
        Encode(selected.GameFolder),
        Encode(snapshot.Status),
        Encode(snapshot.Warning),
        Encode(SerializeInstalled(snapshot.InstalledMods)),
        Encode(SerializeLoaders(snapshot.Loaders)),
        snapshot.HasBepInEx && !snapshot.HasSpeedrunningMod ? "1" : "0"));

    private static async Task SendProtocolAsync(Process process, string message)
    {
        if (process.HasExited) return;
        await process.StandardInput.WriteLineAsync(message);
        await process.StandardInput.FlushAsync();
    }

    private static string SerializeInstalled(IEnumerable<InstalledModItem> items) => string.Join('\n', items.Select(item => string.Join('\t',
        Encode(item.Id),
        Encode(item.Name),
        Encode(item.Author),
        Encode(item.Version),
        Encode(item.Description),
        Encode("Remove"))));

    private static string SerializeLoaders(IEnumerable<ModLoaderItem> items) => string.Join('\n', items.Select(item => string.Join('\t',
        Encode(item.Id),
        Encode(item.Name),
        Encode(item.Author),
        Encode(item.Version),
        Encode(item.Description),
        Encode(item.IsInstalled ? "Remove" : "Install"),
        item.IsInstalled ? "1" : "0",
        item.IsInstalled || item.CanInstall ? "1" : "0",
        Encode(item.UnavailableReason))));

    private static bool IsSubnauticaRunning() => Process.GetProcessesByName("Subnautica").Length > 0;

    private static string ResolveBackgroundPath()
    {
        LauncherConfig config = LauncherConfig.Load();
        return File.Exists(config.BackgroundPreset)
            ? config.BackgroundPreset
            : Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Backgrounds", config.BackgroundPreset + ".png");
    }

    private static void Add(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Decode(string value)
    {
        int missingPadding = value.Length % 4;
        if (missingPadding != 0) value = value.PadRight(value.Length + 4 - missingPadding, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
