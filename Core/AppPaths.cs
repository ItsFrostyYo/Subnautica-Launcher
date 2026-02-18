using Microsoft.Win32;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Core
{
    [SupportedOSPlatform("windows")]
    public static class AppPaths
    {
        // =========================
        // BASE
        // =========================
        public static readonly string BasePath =
            AppContext.BaseDirectory;

        // =========================
        // TOOLS (DepotDownloader)
        // =========================
        public static readonly string ToolsPath =
            Path.Combine(BasePath, "tools");

        public static readonly string DepotDownloaderExe =
            Path.Combine(ToolsPath, "DepotDownloader.exe");

        // =========================
        // STEAM
        // =========================
        private static readonly string DefaultSteamRootX86 =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam");

        private static readonly string DefaultSteamRoot =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Steam");

        public static IReadOnlyList<string> SteamCommonPaths =>
            GetSteamCommonPathsCached();

        private static readonly object SteamPathsCacheLock = new();
        private static IReadOnlyList<string>? _cachedSteamCommonPaths;
        private static DateTime _steamPathsCacheExpiresUtc = DateTime.MinValue;
        private static readonly TimeSpan SteamPathsCacheDuration = TimeSpan.FromSeconds(20);

        public static string SteamCommonPath =>
            SteamCommonPaths.FirstOrDefault()
            ?? Path.Combine(DefaultSteamRootX86, "steamapps", "common");

        public static void InvalidateSteamCommonPathsCache()
        {
            lock (SteamPathsCacheLock)
            {
                _cachedSteamCommonPaths = null;
                _steamPathsCacheExpiresUtc = DateTime.MinValue;
            }
        }

        public static string GetSteamCommonPathFor(string versionFolder)
        {
            if (!string.IsNullOrWhiteSpace(versionFolder))
            {
                var parent = Directory.GetParent(versionFolder);
                if (parent != null)
                    return parent.FullName;
            }

            return SteamCommonPath;
        }
        [SupportedOSPlatform("windows")]
        private static IReadOnlyList<string> GetSteamCommonPathsCached()
        {
            lock (SteamPathsCacheLock)
            {
                if (_cachedSteamCommonPaths != null &&
                    DateTime.UtcNow < _steamPathsCacheExpiresUtc)
                {
                    return _cachedSteamCommonPaths;
                }

                _cachedSteamCommonPaths = GetSteamCommonPaths();
                _steamPathsCacheExpiresUtc = DateTime.UtcNow.Add(SteamPathsCacheDuration);
                return _cachedSteamCommonPaths;
            }
        }

        [SupportedOSPlatform("windows")]
        private static IReadOnlyList<string> GetSteamCommonPaths()
        {
            var paths = new List<string>();
            string? registryRoot = GetSteamRootFromRegistry();

            AddCommonPath(paths, DefaultSteamRootX86);
            AddCommonPath(paths, DefaultSteamRoot);
            if (!string.IsNullOrWhiteSpace(registryRoot))
                AddCommonPath(paths, registryRoot);

            string? libraryFile = FindLibraryFoldersFile();
            if (libraryFile != null)
            {
                foreach (var line in File.ReadLines(libraryFile))
                {
                    string? libraryPath = TryParseLibraryPath(line);
                    if (string.IsNullOrWhiteSpace(libraryPath))
                        continue;

                    AddCommonPath(paths, libraryPath);
                }
            }

            var unique = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

            return unique
                .Where(Directory.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddCommonPath(List<string> paths, string steamRoot)
        {
            if (string.IsNullOrWhiteSpace(steamRoot))
                return;

            string common = Path.Combine(steamRoot, "steamapps", "common");
            paths.Add(common);
        }

        private static string? FindLibraryFoldersFile()
        {
            var candidateRoots = new List<string>
            {
                DefaultSteamRootX86,
                DefaultSteamRoot
            };

            string? registryRoot = GetSteamRootFromRegistry();
            if (!string.IsNullOrWhiteSpace(registryRoot))
                candidateRoots.Add(registryRoot);

            foreach (var root in candidateRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                string candidate = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
        [SupportedOSPlatform("windows")]
        private static string? GetSteamRootFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key == null)
                    return null;

                var steamPath = key.GetValue("SteamPath") as string
                    ?? key.GetValue("InstallPath") as string;

                if (string.IsNullOrWhiteSpace(steamPath))
                    return null;

                return steamPath.Replace("/", "\\");
            }
            catch
            {
                return null;
            }
        }

        private static string? TryParseLibraryPath(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            int pathIndex = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
            if (pathIndex < 0)
                return null;

            int firstQuote = line.IndexOf('"', pathIndex + 6);
            if (firstQuote < 0)
                return null;

            int secondQuote = line.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0)
                return null;

            string raw = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return raw.Replace("\\\\", "\\");
        }

        // =========================
        // LAUNCHER DATA
        // =========================
        public static readonly string DataPath =
            Path.Combine(BasePath, "data");

        public static readonly string LogsPath =
            Path.Combine(BasePath, "logs");

        public static readonly string LogFile =
            Path.Combine(LogsPath, "launcher.log");
    }
}
