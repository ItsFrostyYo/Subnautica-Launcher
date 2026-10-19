using Microsoft.Win32;
using System.IO;

namespace SubnauticaLauncher.Services;

internal static class SteamLaunchOptionsDetector
{
    private const string SubnauticaAppId = "264710";

    public static bool TryDetectSubnautica(out string launchOptions)
    {
        launchOptions = string.Empty;
        foreach (string root in GetSteamRoots())
        {
            string userData = Path.Combine(root, "userdata");
            if (!Directory.Exists(userData)) continue;
            foreach (string config in Directory.EnumerateDirectories(userData)
                         .Select(folder => Path.Combine(folder, "config", "localconfig.vdf"))
                         .Where(File.Exists)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (TryReadLaunchOptions(config, out launchOptions)) return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? candidate in new string?[]
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) roots.Add(candidate);
        }
        return roots;
    }

    private static bool TryReadLaunchOptions(string path, out string launchOptions)
    {
        launchOptions = string.Empty;
        Stack<string> scope = new();
        string? pendingKey = null;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            List<string> tokens = ExtractQuotedTokens(line);
            bool opens = line.Contains('{');
            if (tokens.Count >= 2 && scope.TryPeek(out string? key) && key.Equals(SubnauticaAppId, StringComparison.OrdinalIgnoreCase) && tokens[0].Equals("LaunchOptions", StringComparison.OrdinalIgnoreCase))
            {
                launchOptions = tokens[1].Trim();
                return launchOptions.Length > 0;
            }
            if (tokens.Count == 1) pendingKey = tokens[0];
            if (opens && pendingKey is not null) { scope.Push(pendingKey); pendingKey = null; }
            int closes = line.Count(character => character == '}');
            while (closes-- > 0 && scope.Count > 0) scope.Pop();
        }
        return false;
    }

    private static List<string> ExtractQuotedTokens(string line)
    {
        List<string> tokens = [];
        for (int position = 0; ;)
        {
            int start = line.IndexOf('"', position);
            if (start < 0) break;
            int end = line.IndexOf('"', start + 1);
            if (end < 0) break;
            tokens.Add(line[(start + 1)..end]);
            position = end + 1;
        }
        return tokens;
    }
}
