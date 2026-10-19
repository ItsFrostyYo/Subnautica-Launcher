using System.IO;

namespace SubnauticaLauncher.Services;

internal static class SteamAppIdFileService
{
    private const string AppId = "264710";
    private const string FileName = "steam_appid.txt";

    public static void Ensure(string gameFolder)
    {
        string filePath = Path.Combine(gameFolder, FileName);
        if (File.Exists(filePath) && string.Equals(File.ReadAllText(filePath).Trim(), AppId, StringComparison.Ordinal))
            return;

        File.WriteAllText(filePath, AppId);
    }

    public static void Remove(string gameFolder)
    {
        string filePath = Path.Combine(gameFolder, FileName);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public static string? FindSteamCommonPath(string gameFolder)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(gameFolder)); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "common", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(directory.Parent?.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                return directory.FullName;
        }

        return null;
    }
}
