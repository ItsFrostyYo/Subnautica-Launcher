using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Versions;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Mods;

public static class ModInstallerService
{
    private static readonly HttpClient Http = BuildClient();
    private static readonly Regex ModVersionRegex = new(@"v(?<version>\d+\.\d+\.\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string VersionMarkerRelativePath = @"BepInEx\plugins\Assembly-CheatSharp\Presets\version.txt";

    public static IReadOnlyList<ModDefinition> GetAvailableModsForVersion(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        ModCatalog.EnsureLoaded();

        var mods = new List<ModDefinition>(1);
        if (SupportsLegacySpeedrunRng(game, originalDownload, displayName, folderName))
        {
            ModDefinition? legacy = ModCatalog.GetById("SpeedrunRng");
            if (legacy != null)
                mods.Add(legacy);
        }
        else if (SupportsModernSpeedrunRng(game, originalDownload, displayName, folderName))
        {
            ModDefinition? modern = ModCatalog.GetById("SpeedrunRng20Plus");
            if (modern != null)
                mods.Add(modern);
        }

        return mods;
    }

    public static bool SupportsLegacySpeedrunRng(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        if (game != LauncherGame.Subnautica)
            return false;

        if (!string.IsNullOrWhiteSpace(originalDownload))
            return originalDownload.Contains("2018", StringComparison.OrdinalIgnoreCase);

        return Contains2018(displayName) || Contains2018(folderName);
    }

    public static bool SupportsModernSpeedrunRng(
        LauncherGame game,
        string? originalDownload,
        string? displayName,
        string? folderName)
    {
        if (game != LauncherGame.Subnautica)
            return false;

        return MatchesYearRange(originalDownload, 2022, 2025) ||
               MatchesYearRange(displayName, 2022, 2025) ||
               MatchesYearRange(folderName, 2022, 2025);
    }

    public static async Task InstallBundleAsync(
        ModDefinition mod,
        LauncherGame game,
        string targetFolder,
        DepotInstallCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        callbacks?.OnStatus?.Invoke($"Downloading {mod.DisplayName}...");
        callbacks?.OnOutput?.Invoke($"Downloading mod bundle from {mod.DownloadUrl}");

        string tempRoot = Path.Combine(Path.GetTempPath(), "SubnauticaLauncher", "mods", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string zipPath = Path.Combine(tempRoot, mod.BundleZipFileName);
        string extractDir = Path.Combine(tempRoot, "extract");

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                mod.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream destination = File.Create(zipPath))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;

                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;

                    if (contentLength is > 0)
                    {
                        double percent = Math.Clamp((double)totalRead * 100d / contentLength.Value, 0, 100);
                        callbacks?.OnProgress?.Invoke(percent);
                    }
                }
            }

            callbacks?.OnStatus?.Invoke($"Extracting {mod.DisplayName}...");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            string contentRoot = ResolveContentRoot(extractDir);
            callbacks?.OnStatus?.Invoke($"Installing {mod.DisplayName}...");
            callbacks?.OnOutput?.Invoke($"Copying mod bundle into {targetFolder}");

            CopyDirectoryContents(contentRoot, targetFolder, mod.PreservedRelativePaths);

            if (game == LauncherGame.Subnautica)
                SteamAppIdFileHelper.EnsureSubnauticaSteamAppIdFile(targetFolder);

            callbacks?.OnProgress?.Invoke(100);
            callbacks?.OnStatus?.Invoke($"{mod.DisplayName} installed.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    public static void RemoveManagedMod(
        InstalledVersion version,
        LauncherGame game)
    {
        ModDefinition? mod = ModCatalog.GetById(version.InstalledModId);
        if (mod == null)
            return;

        foreach (string relativePath in mod.RemovalTargets)
        {
            string fullPath = Path.Combine(version.HomeFolder, relativePath);
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            else if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        version.IsModded = false;
        version.InstalledModId = string.Empty;

        if (game == LauncherGame.Subnautica)
            SteamAppIdFileHelper.EnsureSubnauticaSteamAppIdFile(version.HomeFolder);
    }

    public static string BuildModdedDisplayName(string baseDisplayName, int instanceNumber = 1)
    {
        return InstalledVersionNaming.BuildModdedDisplayName(baseDisplayName, instanceNumber);
    }

    public static string BuildModdedFolderName(string baseFolderName, int instanceNumber = 1)
    {
        string suffix = instanceNumber <= 1 ? "_RNGMod" : $"_RNGMod{instanceNumber}";
        return baseFolderName + suffix;
    }

    private static bool Contains2018(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("2018", StringComparison.OrdinalIgnoreCase);
    }

    public static ModDefinition? GetInstalledModDefinition(LauncherGame game, InstalledVersion version)
    {
        ModCatalog.EnsureLoaded();

        if (!string.IsNullOrWhiteSpace(version.InstalledModId))
        {
            ModDefinition? explicitMatch = ModCatalog.GetById(version.InstalledModId);
            if (explicitMatch != null)
                return explicitMatch;
        }

        IReadOnlyList<ModDefinition> available = GetAvailableModsForVersion(
            game,
            version.OriginalDownload,
            version.DisplayName,
            version.FolderName);

        return available.FirstOrDefault();
    }

    public static Version? TryReadInstalledModVersion(InstalledVersion version)
    {
        string versionFilePath = Path.Combine(version.HomeFolder, VersionMarkerRelativePath);
        if (!File.Exists(versionFilePath))
            return null;

        string text;
        try
        {
            text = File.ReadAllText(versionFilePath).Trim();
        }
        catch
        {
            return null;
        }

        Match match = ModVersionRegex.Match(text);
        if (!match.Success)
            return null;

        return Version.TryParse(match.Groups["version"].Value, out Version? versionValue)
            ? versionValue
            : null;
    }

    private static HttpClient BuildClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
        return client;
    }

    private static string ResolveContentRoot(string extractDir)
    {
        string[] directories = Directory.GetDirectories(extractDir);
        string[] files = Directory.GetFiles(extractDir);

        if (directories.Length == 1 && files.Length == 0)
            return directories[0];

        return extractDir;
    }

    private static void CopyDirectoryContents(
        string sourceDir,
        string targetDir,
        IReadOnlyList<string> preservedRelativePaths)
    {
        Directory.CreateDirectory(targetDir);

        var preserved = new HashSet<string>(
            preservedRelativePaths.Select(NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string destination = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (preserved.Contains(NormalizeRelativePath(relative)) && File.Exists(destination))
                continue;

            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool MatchesYearRange(string? value, int minYear, int maxYear)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        for (int year = minYear; year <= maxYear; year++)
        {
            if (value.Contains(year.ToString(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('/', '\\').TrimStart('\\');
    }
}
