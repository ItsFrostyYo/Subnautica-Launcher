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

    public async Task<string> DownloadLauncherAsync(
        LauncherUpdate update,
        IProgress<LauncherUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LauncherUpdateFile[] files = update.Files
            .Where(file => !string.Equals(file.Status, "removed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!files.Any(file => string.Equals(Path.GetFileName(file.Path), LauncherExecutableName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The update manifest does not contain SubnauticaLauncher.exe.");

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"SubnauticaLauncher.{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            progress?.Report(new LauncherUpdateProgress("Preparing download...", "Calculating update size...", null));
            long? totalBytes = await TryGetTotalSizeAsync(files, cancellationToken);
            long completedBytes = 0;
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                LauncherUpdateFile updateFile = files[fileIndex];
                string relativePath = GetPayloadRelativePath(updateFile.Path);
                string destinationPath = Path.GetFullPath(Path.Combine(temporaryDirectory, relativePath));
                if (!destinationPath.StartsWith(temporaryDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update payload path cannot leave the temporary update directory.");

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                using var request = new HttpRequestMessage(HttpMethod.Get, ResolvePayloadUri(updateFile.Path));
                using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                long? fileBytes = response.Content.Headers.ContentLength;
                long downloadedFileBytes = 0;
                await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    int read = await sourceStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloadedFileBytes += read;
                    double percent = totalBytes is > 0
                        ? (completedBytes + downloadedFileBytes) * 100d / totalBytes.Value
                        : (fileIndex + (fileBytes is > 0 ? downloadedFileBytes / (double)fileBytes.Value : 0d)) * 100d / files.Length;
                    progress?.Report(new LauncherUpdateProgress(
                        "Downloading launcher update...",
                        $"{Path.GetFileName(updateFile.Path)} ({FormatBytes(completedBytes + downloadedFileBytes)} of {FormatBytes(totalBytes)})",
                        percent));
                }
                completedBytes += downloadedFileBytes;
            }

            progress?.Report(new LauncherUpdateProgress("Download complete", "Preparing updater handoff...", 100));
            return temporaryDirectory;
        }
        catch
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
            throw;
        }
    }

    public void StartUpdater(string downloadedPayloadDirectory, Version updateVersion, string? verifiedUpdaterPath = null)
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
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(updateVersion.ToString(3));

        Process.Start(startInfo);
    }

    private static async Task<long?> TryGetTotalSizeAsync(
        IReadOnlyList<LauncherUpdateFile> files,
        CancellationToken cancellationToken)
    {
        long total = 0;
        try
        {
            foreach (LauncherUpdateFile file in files)
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, ResolvePayloadUri(file.Path));
                using HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is not long length || length < 0) return null;
                total += length;
            }
            return total;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null) return "unknown";
        double value = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.0} {units[unit]}";
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
