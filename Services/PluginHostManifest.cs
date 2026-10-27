using System.Text.Json;
using System.IO;

namespace SubnauticaLauncher.Services;

internal sealed class PluginHostManifest
{
    public string Title { get; set; } = "Plugin Components";
    public List<PluginHostedComponent> Components { get; set; } = [];

    public static PluginHostManifest Load(string path) =>
        JsonSerializer.Deserialize<PluginHostManifest>(File.ReadAllText(path))
        ?? throw new InvalidDataException("The Plugin host manifest is empty.");

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}

internal sealed class PluginHostedComponent
{
    public required string Id { get; set; }
    public required string AssemblyPath { get; set; }
    public required string ComponentName { get; set; }
    public required string SettingsPath { get; set; }
    public int LayoutOrder { get; set; }
}
