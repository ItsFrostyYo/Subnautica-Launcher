using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

internal sealed class PluginStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PluginPersistentState Load(string pluginId)
    {
        try
        {
            string path = GetStatePath(pluginId);
            string legacyPath = GetLegacyStatePath(pluginId);
            string source = File.Exists(path) ? path : legacyPath;
            PluginPersistentState state = File.Exists(source)
                ? JsonSerializer.Deserialize<PluginPersistentState>(File.ReadAllText(source)) ?? new PluginPersistentState()
                : new PluginPersistentState();
            if (source == legacyPath)
            {
                WriteAtomic(path, state);
                TryDelete(legacyPath);
            }
            return state;
        }
        catch
        {
            return new PluginPersistentState();
        }
    }

    public void Save(string pluginId, PluginPersistentState state, string reason)
    {
        state.LastChangedUtc = DateTime.UtcNow;
        state.LastChangeReason = reason;
        WriteAtomic(GetStatePath(pluginId), state);
        TryDelete(GetLegacyStatePath(pluginId));
    }

    public SubnauticaMacroPluginSettings LoadMacroSettings(string pluginId)
    {
        try
        {
            string xmlPath = GetLiveSplitSettingsPath(pluginId);
            if (File.Exists(xmlPath))
            {
                TryDelete(GetLegacySettingsPath(pluginId));
                SubnauticaMacroPluginSettings parsedSettings = ParseMacroSettings(xmlPath);
                if (NormalizeHotkey(parsedSettings))
                    SaveMacroSettingsFiles(pluginId, parsedSettings);
                return parsedSettings;
            }

            string legacyPath = GetLegacySettingsPath(pluginId);
            SubnauticaMacroPluginSettings settings = File.Exists(legacyPath)
                ? JsonSerializer.Deserialize<SubnauticaMacroPluginSettings>(File.ReadAllText(legacyPath)) ?? new SubnauticaMacroPluginSettings()
                : new SubnauticaMacroPluginSettings();
            SaveMacroSettingsFiles(pluginId, settings);
            return settings;
        }
        catch
        {
            return new SubnauticaMacroPluginSettings();
        }
    }

    public void SaveMacroSettings(string pluginId, SubnauticaMacroPluginSettings settings) =>
        SaveMacroSettingsFiles(pluginId, settings);

    public string EnsureLiveSplitSettings(string pluginId)
    {
        string path = GetLiveSplitSettingsPath(pluginId);
        _ = LoadMacroSettings(pluginId);
        return path;
    }

    public string GetLiveSplitSettingsPath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "settings.xml");

    public string GetPluginDataDirectory(string pluginId)
    {
        string path = GetPluginDataPath(pluginId);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetPluginDataPath(string pluginId) => Path.Combine(AppPaths.DataPath, "Plugins", pluginId);
    private static string GetStatePath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "plugin.json");
    private static string GetLegacyStatePath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "state.json");
    private static string GetLegacySettingsPath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "settings.json");
    private void SaveMacroSettingsFiles(string pluginId, SubnauticaMacroPluginSettings settings)
    {
        NormalizeHotkey(settings);
        string[] gameModes = ["Survival", "Hardcore", "Creative", "Freedom", "BetterRngSurvival", "BetterRngHardcore", "BetterRngCreative"];
        string[] presets = ["Minutes46To46_30", "Minutes46To47", "Minutes46To48", "Minutes46To50", "UnderOneHour", "OverOneHour", "Custom"];
        var document = new XDocument(
            new XElement("Settings",
                new XElement("Version", "1.2"),
                new XElement("Enabled", settings.MacroEnabled),
                new XElement("Hotkey", settings.Hotkey),
                new XElement("GameMode", gameModes[Math.Clamp(settings.GameMode, 0, gameModes.Length - 1)]),
                new XElement("ExplosionReset", settings.ExplosionReset),
                new XElement("ExplosionPreset", presets[Math.Clamp(settings.ExplosionPreset, 0, presets.Length - 1)]),
                new XElement("CustomMinimumSeconds", settings.CustomMinimumSeconds),
                new XElement("CustomMaximumSeconds", settings.CustomMaximumSeconds),
                new XElement("HardcoreSaveDeleter", settings.HardcoreSaveDeleter),
                new XElement("SaveDeletionGameMode", settings.SaveDeletionGameMode == 1 ? "AllGamemodes" : "Hardcore"),
                new XElement("SaveDeletionTiming", settings.SaveDeletionTiming == 1 ? "AutomaticallyDeleteAllSaves" : "AfterResetCompletes")));
        WriteTextAtomic(GetLiveSplitSettingsPath(pluginId), document.ToString(SaveOptions.DisableFormatting));
        TryDelete(GetLegacySettingsPath(pluginId));
    }

    private static bool NormalizeHotkey(SubnauticaMacroPluginSettings settings)
    {
        const int keyCodeMask = 0xFFFF;
        const int shiftModifier = 0x10000;
        const int controlModifier = 0x20000;
        const int altModifier = 0x40000;
        int original = settings.Hotkey;
        int keyCode = original & keyCodeMask;
        int normalized = original;

        if (keyCode is 0x10 or 0xA0 or 0xA1)
            normalized &= ~shiftModifier;
        if (keyCode is 0x11 or 0xA2 or 0xA3)
            normalized &= ~controlModifier;
        if (keyCode is 0x12 or 0xA4 or 0xA5)
            normalized &= ~altModifier;

        settings.Hotkey = normalized;
        return normalized != original;
    }

    private static SubnauticaMacroPluginSettings ParseMacroSettings(string path)
    {
        string[] gameModes = ["Survival", "Hardcore", "Creative", "Freedom", "BetterRngSurvival", "BetterRngHardcore", "BetterRngCreative"];
        string[] presets = ["Minutes46To46_30", "Minutes46To47", "Minutes46To48", "Minutes46To50", "UnderOneHour", "OverOneHour", "Custom"];
        XElement root = XDocument.Load(path).Root ?? throw new InvalidDataException("The plugin settings XML has no root element.");
        int gameMode = Array.FindIndex(gameModes, value => string.Equals(value, root.Element("GameMode")?.Value, StringComparison.OrdinalIgnoreCase));
        int preset = Array.FindIndex(presets, value => string.Equals(value, root.Element("ExplosionPreset")?.Value, StringComparison.OrdinalIgnoreCase));
        return new SubnauticaMacroPluginSettings
        {
            MacroEnabled = bool.TryParse(root.Element("Enabled")?.Value, out bool enabled) && enabled,
            Hotkey = int.TryParse(root.Element("Hotkey")?.Value, out int hotkey) ? hotkey : 116,
            GameMode = Math.Max(0, gameMode),
            ExplosionReset = bool.TryParse(root.Element("ExplosionReset")?.Value, out bool explosion) && explosion,
            ExplosionPreset = Math.Max(0, preset),
            CustomMinimumSeconds = int.TryParse(root.Element("CustomMinimumSeconds")?.Value, out int minimum) ? minimum : 2760,
            CustomMaximumSeconds = int.TryParse(root.Element("CustomMaximumSeconds")?.Value, out int maximum) ? maximum : 2790,
            HardcoreSaveDeleter = bool.TryParse(root.Element("HardcoreSaveDeleter")?.Value, out bool deleterEnabled) && deleterEnabled,
            SaveDeletionGameMode = string.Equals(root.Element("SaveDeletionGameMode")?.Value, "AllGamemodes", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            SaveDeletionTiming = string.Equals(root.Element("SaveDeletionTiming")?.Value, "AutomaticallyDeleteAllSaves", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
        };
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static void WriteTextAtomic(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, value);
        File.Move(temporary, path, true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}

internal sealed class PluginPersistentState
{
    public bool Enabled { get; set; }
    public DateTime? LastChangedUtc { get; set; }
    public string? LastChangeReason { get; set; }
}
