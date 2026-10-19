using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Services;

internal static class SteamLibraryDiscovery
{
    public static IReadOnlyList<string> FindCommonPaths()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSteamRoot(roots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
        AddSteamRoot(roots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.Fixed && item.IsReady))
        {
            AddSteamRoot(roots, Path.Combine(drive.RootDirectory.FullName, "Steam"));
            AddSteamRoot(roots, Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"));
        }

        var commonPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            AddCommon(commonPaths, Path.Combine(root, "steamapps", "common"));
            string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries)) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                AddCommon(commonPaths, Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common"));
        }
        return commonPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddSteamRoot(HashSet<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
    }

    private static void AddCommon(HashSet<string> paths, string path)
    {
        if (Directory.Exists(path)) paths.Add(Path.GetFullPath(path));
    }
}
