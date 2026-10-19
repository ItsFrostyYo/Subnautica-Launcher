using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Services;

namespace SubnauticaLauncher.UI;

public static class ImGuiSubnauticaSettingsWindow
{
    public static void Show(LauncherVersionStore store, IntPtr ownerHandle) =>
        _ = RunSafeAsync(store, ownerHandle);

    private static async Task RunSafeAsync(LauncherVersionStore store, IntPtr ownerHandle)
    {
        try { await RunAsync(store, ownerHandle, string.Empty); }
        catch (Exception exception)
        {
            // A child settings window must never take down the launcher process.
            Debug.WriteLine("Subnautica settings window failed: " + exception);
        }
    }

    private static async Task RunAsync(LauncherVersionStore store, IntPtr ownerHandle, string message)
    {
        string hostPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "SubnauticaLauncher.ImGuiHost.exe");
        if (!File.Exists(hostPath)) return;

        IReadOnlyList<LauncherVersion> versions = store.Load();
        LauncherConfig config = LauncherConfig.Load();
        SubnauticaSettingsProfileInfo profile = SubnauticaSettingsProfileService.GetProfileInfo();
        IReadOnlyList<SubnauticaSettingEntry> entries;
        try { entries = SubnauticaSettingsProfileService.LoadSavedEntries(); }
        catch (Exception exception)
        {
            entries = [];
            message = "The saved settings could not be read: " + exception.Message;
        }

        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        Add(startInfo, "--owner", ownerHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--mode", "settings");
        Add(startInfo, "--background", ResolveBackgroundPath(config));
        Add(startInfo, "--settings-has-saved", profile.Exists ? "1" : "0");
        Add(startInfo, "--settings-source", profile.SourceDisplayName);
        Add(startInfo, "--settings-apply", config.ApplySavedSubnauticaSettingsToNewInstalls ? "1" : "0");
        Add(startInfo, "--settings-message", Encode(message));
        Add(startInfo, "--settings-versions", string.Join('\n', versions.Select(version =>
            Encode(version.GameFolder) + "\t" + Encode(version.DisplayName) + "\t" +
            (File.Exists(SubnauticaSettingsProfileService.GetVersionOptionsPath(version)) ? "1" : "0"))));
        Add(startInfo, "--settings-entries", string.Join('\n', entries.Select(entry =>
            Encode(entry.Category) + "\t" + Encode(entry.Name) + "\t" + Encode(entry.Value))));

        string output;
        using (Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start the settings window."))
        {
            ImGuiAddVersionWindow.RegisterHost(process);
            try
            {
                output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
            }
            finally
            {
                ImGuiAddVersionWindow.UnregisterHost(process);
            }
        }

        string? result = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(result)) return;

        string[] values = result.Split('|');
        string feedback;
        if (values is ["SETTINGS_TOGGLE", _])
        {
            config.ApplySavedSubnauticaSettingsToNewInstalls = values[1] == "1";
            config.Save();
            feedback = config.ApplySavedSubnauticaSettingsToNewInstalls
                ? "Saved settings will be applied to new version installs."
                : "Saved settings will not be applied to new version installs.";
        }
        else if (values.Length == 2 && values[0] is "SETTINGS_SAVE" or "SETTINGS_COPY")
        {
            string selectedPath = Decode(values[1]);
            LauncherVersion? version = versions.FirstOrDefault(item => PathsEqual(item.GameFolder, selectedPath));
            if (version is null)
                feedback = "The selected managed version could no longer be found.";
            else if (values[0] == "SETTINGS_SAVE")
                feedback = SubnauticaSettingsProfileService.TrySaveFromVersion(version, out string error)
                    ? $"Saved settings from {version.DisplayName}." : error;
            else
                feedback = SubnauticaSettingsProfileService.TryCopyToVersion(version, out string error)
                    ? $"Copied saved settings to {version.DisplayName}." : error;
        }
        else
        {
            return;
        }

        await RunAsync(store, ownerHandle, feedback);
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string ResolveBackgroundPath(LauncherConfig config) =>
        File.Exists(config.BackgroundPreset)
            ? config.BackgroundPreset
            : Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Backgrounds", config.BackgroundPreset + ".png");

    private static void Add(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value ?? string.Empty);
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Decode(string value)
    {
        int padding = value.Length % 4;
        if (padding != 0) value = value.PadRight(value.Length + 4 - padding, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
