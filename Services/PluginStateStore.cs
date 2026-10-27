using System.IO;
using System.Text.Json;
using SubnauticaLauncher.Core;

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

    public string GetPluginSettingsPath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "settings.xml");

    public string GetPluginDataDirectory(string pluginId)
    {
        string path = GetPluginDataPath(pluginId);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetPluginDataPath(string pluginId) => Path.Combine(AppPaths.DataPath, "Plugins", pluginId);
    private static string GetStatePath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "plugin.json");
    private static string GetLegacyStatePath(string pluginId) => Path.Combine(GetPluginDataPath(pluginId), "state.json");

    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
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
    public int? LayoutOrder { get; set; }
    public DateTime? LastChangedUtc { get; set; }
    public string? LastChangeReason { get; set; }
}
