using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using SubnauticaLauncher.Core;

namespace SubnauticaLauncher.Updates;

public static class UpdaterChecker
{
    private const string UpdaterExeName = "SNLUpdater.exe";

    public static async Task<string> EnsureUpdaterAsync(
        UpdateInfo update,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        string baseDir = AppContext.BaseDirectory;
        string updaterPath = Path.Combine(baseDir, UpdaterExeName);

        status?.Report("Checking updater...");

        if (IsUpdaterCurrent(updaterPath, update))
        {
            Logger.Log("[UpdaterChecker] Existing updater matches latest release asset");
            return updaterPath;
        }

        status?.Report("Downloading latest updater...");

        using HttpClient http = new();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");

        byte[] data = await http.GetByteArrayAsync(update.UpdaterDownloadUrl, cancellationToken);

        string tempPath = Path.Combine(Path.GetTempPath(), $"SNLUpdater.{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(tempPath, data, cancellationToken);

        try
        {
            ValidateUpdaterFile(tempPath, update);

            Directory.CreateDirectory(baseDir);
            File.Copy(tempPath, updaterPath, overwrite: true);

            ValidateUpdaterFile(updaterPath, update);

            Logger.Log("[UpdaterChecker] Updater downloaded and verified");
            status?.Report("Updater verified.");
            return updaterPath;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[UpdaterChecker] Failed to clean temporary updater file");
            }
        }
    }

    private static bool IsUpdaterCurrent(string updaterPath, UpdateInfo update)
    {
        if (!File.Exists(updaterPath))
            return false;

        // If GitHub does not expose a digest for this release asset, force a fresh
        // updater download so we never rely on a potentially stale local updater.
        if (string.IsNullOrWhiteSpace(update.UpdaterSha256))
            return false;

        try
        {
            ValidateUpdaterFile(updaterPath, update);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateUpdaterFile(string updaterPath, UpdateInfo update)
    {
        if (!File.Exists(updaterPath))
            throw new FileNotFoundException("Updater executable is missing.", updaterPath);

        long fileSize = new FileInfo(updaterPath).Length;
        if (update.UpdaterAssetSize > 0 && fileSize != update.UpdaterAssetSize)
            throw new InvalidOperationException(
                $"Updater size mismatch. Expected {update.UpdaterAssetSize}, got {fileSize}.");

        if (!string.IsNullOrWhiteSpace(update.UpdaterSha256))
        {
            string fileSha256 = ComputeSha256(updaterPath);
            if (!string.Equals(fileSha256, update.UpdaterSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Updater checksum mismatch. Downloaded updater is not the expected latest asset.");
            }
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
