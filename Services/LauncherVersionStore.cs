using System.Text.Json;
using System.IO;
using System.Linq;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

public sealed class LauncherVersionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(AppPaths.DataPath, "versions.json");

    public IReadOnlyList<LauncherVersion> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            return JsonSerializer.Deserialize<List<LauncherVersion>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public bool TryAdd(LauncherVersion version, out string error)
    {
        List<LauncherVersion> versions = Load().ToList();
        if (string.IsNullOrWhiteSpace(version.DisplayName))
        {
            error = "Enter a display name for the launcher version.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(version.FolderName))
        {
            error = "Enter a launcher folder name.";
            return false;
        }

        string newExecutable = Path.GetFullPath(Path.Combine(version.GameFolder, "Subnautica.exe"));
        if (!File.Exists(newExecutable))
        {
            error = "The selected folder must contain Subnautica.exe.";
            return false;
        }

        if (versions.Any(item => string.Equals(item.FolderName, version.FolderName, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A launcher version already uses that folder name.";
            return false;
        }

        if (versions.Any(item => string.Equals(item.DisplayName, version.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A launcher version already uses that display name.";
            return false;
        }

        if (versions.Any(item => string.Equals(item.GameFolder, version.GameFolder, StringComparison.OrdinalIgnoreCase)))
        {
            error = "That Subnautica folder is already added to the launcher.";
            return false;
        }

        if (versions.Any(item => string.Equals(Path.GetFullPath(Path.Combine(item.GameFolder, "Subnautica.exe")), newExecutable, StringComparison.OrdinalIgnoreCase)))
        {
            error = "That Subnautica executable is already added to the launcher.";
            return false;
        }

        Directory.CreateDirectory(AppPaths.DataPath);
        versions.Add(version);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(versions, JsonOptions));
        File.Move(temporaryPath, FilePath, true);
        error = string.Empty;
        return true;
    }

    public bool TryUpdate(LauncherVersion original, LauncherVersion updated, out string error)
    {
        List<LauncherVersion> versions = Load().ToList();
        int index = versions.FindIndex(item => string.Equals(item.GameFolder, original.GameFolder, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            error = "The version could no longer be found in the launcher.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(updated.DisplayName) || string.IsNullOrWhiteSpace(updated.FolderName))
        {
            error = "Display name and folder name are required.";
            return false;
        }

        if (versions.Where((_, itemIndex) => itemIndex != index).Any(item => string.Equals(item.DisplayName, updated.DisplayName, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A launcher version already uses that display name.";
            return false;
        }

        if (versions.Where((_, itemIndex) => itemIndex != index).Any(item => string.Equals(item.FolderName, updated.FolderName, StringComparison.OrdinalIgnoreCase)))
        {
            error = "A launcher version already uses that folder name.";
            return false;
        }

        versions[index] = updated;
        SaveAll(versions);
        error = string.Empty;
        return true;
    }

    public bool TryRemove(LauncherVersion version, out string error)
    {
        List<LauncherVersion> versions = Load().ToList();
        int removed = versions.RemoveAll(item => string.Equals(item.GameFolder, version.GameFolder, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            error = "The version could no longer be found in the launcher.";
            return false;
        }

        SaveAll(versions);
        error = string.Empty;
        return true;
    }

    public bool TryMove(LauncherVersion version, int offset, out string error)
    {
        List<LauncherVersion> versions = Load().ToList();
        int currentIndex = versions.FindIndex(item => string.Equals(item.GameFolder, version.GameFolder, StringComparison.OrdinalIgnoreCase));
        int targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= versions.Count)
        {
            error = "That version cannot be moved further in this direction.";
            return false;
        }

        (versions[currentIndex], versions[targetIndex]) = (versions[targetIndex], versions[currentIndex]);
        SaveAll(versions);
        error = string.Empty;
        return true;
    }

    private static void SaveAll(List<LauncherVersion> versions)
    {
        Directory.CreateDirectory(AppPaths.DataPath);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(versions, JsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }
}
