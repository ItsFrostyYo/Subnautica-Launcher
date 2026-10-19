using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SubnauticaLauncher.Updates;

public sealed class LauncherUpdateService
{
    private const string LauncherExecutableName = "SubnauticaLauncher.exe";
    private const string UpdaterExecutableName = "SNLUpdater.exe";
    private static readonly HttpClient HttpClient = new();

    public async Task<LauncherUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        string xml = await HttpClient.GetStringAsync(LauncherUpdateFeed.ManifestUri, cancellationToken);
        IReadOnlyList<LauncherUpdate> updates = ParseManifest(xml);
        Version currentVersion = GetCurrentVersion();

        return updates
            .Where(update => update.Version > currentVersion)
            .OrderByDescending(update => update.Version)
            .FirstOrDefault();
    }

    public async Task<string> DownloadLauncherAsync(LauncherUpdate update, CancellationToken cancellationToken = default)
    {
        if (!update.Files.Any(file => string.Equals(Path.GetFileName(file.Path), LauncherExecutableName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The update manifest does not contain SubnauticaLauncher.exe.");

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"SubnauticaLauncher.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            foreach (LauncherUpdateFile updateFile in update.Files)
            {
                string relativePath = GetPayloadRelativePath(updateFile.Path);
                string destinationPath = Path.GetFullPath(Path.Combine(temporaryDirectory, relativePath));
                if (!destinationPath.StartsWith(temporaryDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update payload path cannot leave the temporary update directory.");

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using Stream sourceStream = await HttpClient.GetStreamAsync(ResolvePayloadUri(updateFile.Path), cancellationToken);
                await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await sourceStream.CopyToAsync(destination, cancellationToken);
            }

            return temporaryDirectory;
        }
        catch
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
            throw;
        }
    }

    public void StartUpdater(string downloadedPayloadDirectory, string? verifiedUpdaterPath = null)
    {
        string installDirectory = AppContext.BaseDirectory;
        // The update feed can ship a newer updater. Use it for the handoff so an
        // old installed updater never controls a newer launcher update.
        string updaterPath = verifiedUpdaterPath ?? Path.Combine(installDirectory, UpdaterExecutableName);
        if (!File.Exists(updaterPath))
            throw new FileNotFoundException("The launcher updater is missing.", updaterPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            WorkingDirectory = installDirectory,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--source-dir");
        startInfo.ArgumentList.Add(downloadedPayloadDirectory);
        startInfo.ArgumentList.Add("--target-dir");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        Process.Start(startInfo);
    }

    public static IReadOnlyList<LauncherUpdate> ParseManifest(string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("Update manifest has no root element.");
        if (!string.Equals(root.Name.LocalName, "updates", StringComparison.Ordinal))
            throw new InvalidDataException("Update manifest root must be <updates>.");

        var updates = new List<LauncherUpdate>();
        foreach (XElement entry in root.Elements("update"))
        {
            if (!Version.TryParse(entry.Attribute("version")?.Value, out Version? version))
                continue;

            var files = entry.Element("files")?.Elements("file")
                .Select(file => new LauncherUpdateFile(
                    file.Attribute("path")?.Value ?? string.Empty,
                    file.Attribute("status")?.Value ?? string.Empty))
                .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                .ToArray() ?? Array.Empty<LauncherUpdateFile>();

            var changelog = entry.Element("changelog")?.Elements("change")
                .Select(change => change.Value.Trim())
                .Where(change => !string.IsNullOrWhiteSpace(change))
                .ToArray() ?? Array.Empty<string>();

            updates.Add(new LauncherUpdate(
                version,
                entry.Attribute("title")?.Value ?? string.Empty,
                entry.Attribute("date")?.Value ?? string.Empty,
                files,
                changelog));
        }

        return updates;
    }

    private static Version GetCurrentVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version
            ?? Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(0, 0, 0, 0);
    }

    private static Uri ResolvePayloadUri(string relativePath)
    {
        string normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalizedPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Update payload path cannot leave the content root.");

        return new Uri(LauncherUpdateFeed.ContentBaseUri, normalizedPath);
    }

    private static string GetPayloadRelativePath(string manifestPath)
    {
        string normalizedPath = manifestPath.Replace('\\', '/').TrimStart('/');
        const string releasePrefix = "Release/";
        if (normalizedPath.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase))
            normalizedPath = normalizedPath[releasePrefix.Length..];

        if (string.IsNullOrWhiteSpace(normalizedPath) || normalizedPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Update payload path cannot leave the temporary update directory.");

        return normalizedPath.Replace('/', Path.DirectorySeparatorChar);
    }
}
