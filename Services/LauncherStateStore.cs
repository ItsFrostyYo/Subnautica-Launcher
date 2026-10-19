using System.Text.Json;
using System.IO;
using SubnauticaLauncher.Core;

namespace SubnauticaLauncher.Services;

public sealed class LauncherStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(AppPaths.DataPath, "launcher-state.json");

    public string? LoadActiveVersionFolder()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(FilePath), JsonOptions)?.ActiveVersionFolder;
        }
        catch
        {
            return null;
        }
    }

    public void SaveActiveVersionFolder(string? folder)
    {
        Directory.CreateDirectory(AppPaths.DataPath);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new LauncherState { ActiveVersionFolder = folder }, JsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }

    private sealed class LauncherState
    {
        public string? ActiveVersionFolder { get; set; }
    }
}
