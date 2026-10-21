using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Updates;

public sealed record LauncherIntegrityIssue(string Path, string Reason);

public sealed record LauncherIntegrityResult(
    Version Version,
    IReadOnlyList<LauncherIntegrityIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public sealed class LauncherIntegrityService
{
    public const string ManifestFileName = "IntegrityManifest.json";
    private static readonly HttpClient HttpClient = new();
    private static Uri ManifestUri => new(LauncherUpdateFeed.ContentBaseUri, "Release/Data/" + ManifestFileName);

    public async Task<LauncherIntegrityResult> VerifyAsync(
        IProgress<LauncherUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new LauncherUpdateProgress(
            "Checking launcher integrity...",
            "Downloading the official launcher file manifest...",
            null));

        byte[] manifestBytes = await HttpClient.GetByteArrayAsync(ManifestUri, cancellationToken);
        IntegrityManifest manifest = JsonSerializer.Deserialize<IntegrityManifest>(manifestBytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("The launcher integrity manifest is invalid.");

        if (!Version.TryParse(manifest.Version, out Version? version) || manifest.Files.Count == 0)
            throw new InvalidDataException("The launcher integrity manifest is incomplete.");

        string installRoot = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var issues = new List<LauncherIntegrityIssue>();

        string localManifestPath = Path.Combine(installRoot, "Data", ManifestFileName);
        if (!File.Exists(localManifestPath))
        {
            issues.Add(new LauncherIntegrityIssue("Data/" + ManifestFileName, "Missing"));
        }
        else
        {
            byte[] localManifestBytes = await File.ReadAllBytesAsync(localManifestPath, cancellationToken);
            if (!SHA256.HashData(localManifestBytes).SequenceEqual(SHA256.HashData(manifestBytes)))
                issues.Add(new LauncherIntegrityIssue("Data/" + ManifestFileName, "Does not match the current release"));
        }

        for (int index = 0; index < manifest.Files.Count; index++)
        {
            IntegrityManifestFile expected = manifest.Files[index];
            string normalized = expected.Path.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(installRoot, normalized));
            if (!fullPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The launcher integrity manifest contains an unsafe path.");

            progress?.Report(new LauncherUpdateProgress(
                "Checking launcher integrity...",
                expected.Path,
                5 + ((index + 1d) / manifest.Files.Count * 55)));

            if (!File.Exists(fullPath))
            {
                issues.Add(new LauncherIntegrityIssue(expected.Path, "Missing"));
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length != expected.Size)
            {
                issues.Add(new LauncherIntegrityIssue(expected.Path, "Incorrect size"));
                continue;
            }

            await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!string.Equals(Convert.ToHexString(actualHash), expected.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new LauncherIntegrityIssue(expected.Path, "Checksum mismatch"));
        }

        progress?.Report(new LauncherUpdateProgress(
            issues.Count == 0 ? "Integrity check complete" : "Repair required",
            issues.Count == 0
                ? "All launcher files are present and valid."
                : $"Found {issues.Count} missing or damaged launcher file{(issues.Count == 1 ? string.Empty : "s")}.",
            60));

        return new LauncherIntegrityResult(version, issues);
    }

    public static bool IsDevelopmentWorkspace()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.EnumerateFiles(directory.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                Directory.EnumerateFiles(directory.FullName, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return true;
        }
        return false;
    }

    private sealed class IntegrityManifest
    {
        public string Version { get; set; } = string.Empty;
        public List<IntegrityManifestFile> Files { get; set; } = [];
    }

    private sealed class IntegrityManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
