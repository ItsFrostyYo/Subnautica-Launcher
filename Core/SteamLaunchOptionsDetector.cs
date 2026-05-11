using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SubnauticaLauncher.Core;

internal static class SteamLaunchOptionsDetector
{
    public static bool TryDetect(string steamAppId, out string launchOptions, out string sourcePath)
    {
        launchOptions = string.Empty;
        sourcePath = string.Empty;

        if (string.IsNullOrWhiteSpace(steamAppId))
            return false;

        foreach (string configPath in GetCandidateConfigPaths())
        {
            if (!File.Exists(configPath))
                continue;

            if (!TryReadLaunchOptions(configPath, steamAppId, out string detected))
                continue;

            if (string.IsNullOrWhiteSpace(detected))
                continue;

            launchOptions = detected.Trim();
            sourcePath = configPath;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> GetCandidateConfigPaths()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steamRoot in GetCandidateSteamRoots())
        {
            string userDataPath = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userDataPath))
                continue;

            IEnumerable<string> userFolders;
            try
            {
                userFolders = Directory.EnumerateDirectories(userDataPath);
            }
            catch
            {
                continue;
            }

            foreach (string userFolder in userFolders.OrderByDescending(GetSafeLastWriteTimeUtc))
            {
                string configPath = Path.Combine(userFolder, "config", "localconfig.vdf");
                if (!File.Exists(configPath) || !yielded.Add(configPath))
                    continue;

                yield return configPath;
            }
        }
    }

    private static IEnumerable<string> GetCandidateSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commonPaths = AppPaths.SteamCommonPaths
            .Append(AppPaths.SteamCommonPath)
            .Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (string commonPath in commonPaths)
        {
            DirectoryInfo? commonDirectory;
            try
            {
                commonDirectory = new DirectoryInfo(commonPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch
            {
                continue;
            }

            DirectoryInfo? steamAppsDirectory = commonDirectory.Parent;
            DirectoryInfo? steamRootDirectory = steamAppsDirectory?.Parent;
            if (steamRootDirectory != null)
                roots.Add(steamRootDirectory.FullName);
        }

        return roots;
    }

    private static DateTime GetSafeLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(Path.Combine(path, "config", "localconfig.vdf"));
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool TryReadLaunchOptions(string configPath, string steamAppId, out string launchOptions)
    {
        launchOptions = string.Empty;
        var scope = new Stack<string>();
        string? pendingKey = null;

        foreach (string rawLine in File.ReadLines(configPath))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("//", StringComparison.Ordinal))
                continue;

            List<string> tokens = ExtractQuotedTokens(line);
            bool opensBlock = line.Contains('{');
            bool closesBlock = line.Contains('}');

            if (tokens.Count >= 2)
            {
                if (IsInsideTargetApp(scope, steamAppId) &&
                    tokens[0].Equals("LaunchOptions", StringComparison.OrdinalIgnoreCase))
                {
                    launchOptions = tokens[1];
                    return true;
                }

                pendingKey = null;
            }
            else if (tokens.Count == 1)
            {
                if (opensBlock)
                {
                    scope.Push(tokens[0]);
                    pendingKey = null;
                }
                else
                {
                    pendingKey = tokens[0];
                }
            }

            if (opensBlock && pendingKey != null)
            {
                scope.Push(pendingKey);
                pendingKey = null;
            }

            if (closesBlock)
            {
                int closeCount = line.Count(ch => ch == '}');
                for (int i = 0; i < closeCount && scope.Count > 0; i++)
                    scope.Pop();

                pendingKey = null;
            }
        }

        return false;
    }

    private static bool IsInsideTargetApp(Stack<string> scope, string steamAppId)
    {
        return scope.Count > 0 &&
               scope.Peek().Equals(steamAppId, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExtractQuotedTokens(string line)
    {
        var tokens = new List<string>();
        int index = 0;

        while (index < line.Length)
        {
            int start = line.IndexOf('"', index);
            if (start < 0)
                break;

            int end = line.IndexOf('"', start + 1);
            if (end < 0)
                break;

            tokens.Add(line.Substring(start + 1, end - start - 1));
            index = end + 1;
        }

        return tokens;
    }
}
