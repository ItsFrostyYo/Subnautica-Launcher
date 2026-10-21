using SubnauticaLauncher.Core;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Updates;

public static class LegacyLauncherMigrationService
{
    public const string NewLauncherPackageUrl =
        "https://github.com/ItsFrostyYo/Subnautica-Launcher/releases/download/v4.0.0%2B/SubnauticaLauncher.zip";

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task DownloadAndStartAsync(
        IProgress<string>? status,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        RefuseDevelopmentWorkspaceMigration();

        string installedUpdater = Path.Combine(AppPaths.BasePath, "SNLUpdater.exe");
        if (!File.Exists(installedUpdater))
            throw new FileNotFoundException("SNLUpdater.exe is missing from the launcher installation.", installedUpdater);

        string workspace = Path.Combine(
            Path.GetTempPath(),
            "SubnauticaLauncherMigration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        string packagePath = Path.Combine(workspace, "SubnauticaLauncher.zip");
        string migrationUpdater = Path.Combine(workspace, "SNLUpdater.exe");

        try
        {
            status?.Report("Downloading the new Subnautica Launcher...");
            await DownloadAsync(NewLauncherPackageUrl, packagePath, progress, cancellationToken);

            status?.Report("Validating the new launcher package...");
            ValidatePackage(packagePath);

            File.Copy(installedUpdater, migrationUpdater, overwrite: true);

            status?.Report("Closing the discontinued launcher and starting migration...");
            var startInfo = new ProcessStartInfo
            {
                FileName = migrationUpdater,
                WorkingDirectory = workspace,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--migrate");
            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add(AppPaths.BasePath);
            startInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString());

            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The launcher migration updater could not be started.");

            Environment.Exit(0);
        }
        catch
        {
            TryDeleteDirectory(workspace);
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher-LegacyMigration/3.4");
        return client;
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            131072,
            useAsync: true);

        byte[] buffer = new byte[131072];
        long downloaded = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            if (totalBytes is > 0)
                progress?.Report(downloaded * 100d / totalBytes.Value);
        }
    }

    private static void ValidatePackage(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        bool hasLauncher = false;
        bool hasUpdater = false;
        bool hasDependencies = false;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') ||
                normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
                throw new InvalidDataException($"The new launcher package contains an unsafe path: {entry.FullName}");

            hasLauncher |= string.Equals(normalized, "SubnauticaLauncher.exe", StringComparison.OrdinalIgnoreCase);
            hasUpdater |= string.Equals(normalized, "SNLUpdater.exe", StringComparison.OrdinalIgnoreCase);
            hasDependencies |= normalized.StartsWith("Dependencies/", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrEmpty(entry.Name);
        }

        if (!hasLauncher || !hasUpdater || !hasDependencies)
            throw new InvalidDataException(
                "The downloaded package is not a complete new Subnautica Launcher installation.");
    }

    private static void RefuseDevelopmentWorkspaceMigration()
    {
        if (Directory.Exists(Path.Combine(AppPaths.BasePath, ".git")) ||
            Directory.EnumerateFiles(AppPaths.BasePath, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
            Directory.EnumerateFiles(AppPaths.BasePath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
        {
            throw new InvalidOperationException(
                "Migration is disabled when the launcher is running directly from a development workspace. " +
                "Test it from a packaged launcher installation instead.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary migration files can be cleaned by the operating system later.
        }
    }
}
