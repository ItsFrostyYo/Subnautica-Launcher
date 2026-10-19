using System.Diagnostics;
using System.Globalization;
using System.IO;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Services;

namespace SubnauticaLauncher.UI;

public static class ImGuiPluginSettingsWindow
{
    public static async Task ShowAsync(LauncherPluginManager manager, string pluginId, IntPtr ownerHandle)
    {
        if (pluginId != LauncherPluginCatalog.SubnauticaResetMacroId) return;
        string hostPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "SubnauticaLauncher.ImGuiHost.exe");
        if (!File.Exists(hostPath)) throw new FileNotFoundException("The launcher ImGui host is missing.", hostPath);

        SubnauticaMacroPluginSettings settings = manager.LoadMacroSettings(pluginId);
        LauncherConfig config = LauncherConfig.Load();
        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        Add(startInfo, "--owner", ownerHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--mode", "plugin-settings");
        Add(startInfo, "--background", ResolveBackgroundPath(config));
        Add(startInfo, "--macro-enabled", settings.MacroEnabled ? "1" : "0");
        Add(startInfo, "--plugin-hotkey", settings.Hotkey.ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--plugin-game-mode", settings.GameMode.ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--plugin-explosion", settings.ExplosionReset ? "1" : "0");
        Add(startInfo, "--plugin-explosion-preset", settings.ExplosionPreset.ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--plugin-custom-min", FormatTime(settings.CustomMinimumSeconds));
        Add(startInfo, "--plugin-custom-max", FormatTime(settings.CustomMaximumSeconds));

        string output;
        using (Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The plugin settings window could not start."))
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
        string[] values = result?.Split('|') ?? [];
        if (values.Length != 8 || values[0] != "PLUGIN_SETTINGS") return;
        if (!int.TryParse(values[2], out int hotkey) ||
            !int.TryParse(values[3], out int gameMode) ||
            !int.TryParse(values[5], out int preset) ||
            !TryParseTime(values[6], out int minimum) ||
            !TryParseTime(values[7], out int maximum))
            throw new InvalidDataException("The plugin settings window returned invalid values.");

        await manager.SaveMacroSettingsAsync(pluginId, new SubnauticaMacroPluginSettings
        {
            MacroEnabled = values[1] == "1",
            Hotkey = hotkey,
            GameMode = gameMode,
            ExplosionReset = values[4] == "1",
            ExplosionPreset = preset,
            CustomMinimumSeconds = minimum,
            CustomMaximumSeconds = maximum
        });
    }

    private static string ResolveBackgroundPath(LauncherConfig config) =>
        File.Exists(config.BackgroundPreset)
            ? config.BackgroundPreset
            : Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Backgrounds", config.BackgroundPreset + ".png");

    private static string FormatTime(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    private static bool TryParseTime(string value, out int seconds)
    {
        seconds = 0;
        string[] parts = value.Trim().Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int remainder) &&
            minutes >= 0 && remainder is >= 0 and < 60 && (seconds = minutes * 60 + remainder) >= 0;
    }

    private static void Add(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }
}
