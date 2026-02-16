using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using SubnauticaLauncher.Core;

namespace SubnauticaLauncher.Updates;

public static class UpdateChecker
{
    private const string Owner = "ItsFrostyYo";
    private const string Repo = "Subnautica-Launcher";
    private const string LauncherExeName = "SubnauticaLauncher.exe";
    private const string UpdaterExeName = "SNLUpdater.exe";

    private static readonly HttpClient http = new();

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        EnsureUserAgent();

        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        using var response = await http.GetAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString()?.Trim() ?? "";
        if (!TryParseVersion(tag, out var latest))
        {
            Logger.Warn($"[UpdateChecker] Invalid release tag: {tag}");
            return null;
        }

        if (latest <= current)
        {
            Logger.Log($"[UpdateChecker] No update needed. Current={current}, Latest={latest}");
            return null;
        }

        if (!TryFindAsset(root, LauncherExeName, out string launcherUrl, out _, out _))
        {
            Logger.Warn("[UpdateChecker] Launcher asset not found in latest release");
            return null;
        }

        if (!TryFindAsset(root, UpdaterExeName, out string updaterUrl, out long updaterSize, out string? updaterSha256))
        {
            Logger.Warn("[UpdateChecker] Updater asset not found in latest release");
            return null;
        }

        var releaseName = root.TryGetProperty("name", out var nameProp)
            ? (nameProp.GetString() ?? "")
            : "";

        return new UpdateInfo
        {
            Version = latest,
            ReleaseTag = tag,
            ReleaseName = string.IsNullOrWhiteSpace(releaseName) ? $"v{latest}" : releaseName,
            LauncherDownloadUrl = launcherUrl,
            UpdaterDownloadUrl = updaterUrl,
            UpdaterAssetSize = updaterSize,
            UpdaterSha256 = updaterSha256
        };
    }

    private static void EnsureUserAgent()
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
    }

    private static bool TryParseVersion(string rawTag, out Version version)
    {
        string normalized = rawTag.Trim();

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        int plusIndex = normalized.IndexOf('+');
        if (plusIndex >= 0)
            normalized = normalized[..plusIndex];

        int dashIndex = normalized.IndexOf('-');
        if (dashIndex >= 0)
            normalized = normalized[..dashIndex];

        return Version.TryParse(normalized, out version!);
    }

    private static bool TryFindAsset(
        JsonElement releaseRoot,
        string assetName,
        out string downloadUrl,
        out long size,
        out string? sha256)
    {
        foreach (var asset in releaseRoot.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
                continue;

            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            size = asset.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;

            sha256 = null;
            if (asset.TryGetProperty("digest", out var digestProp))
            {
                string digest = digestProp.GetString() ?? "";
                if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha256 = digest["sha256:".Length..].Trim().ToLowerInvariant();
            }

            return !string.IsNullOrWhiteSpace(downloadUrl);
        }

        downloadUrl = "";
        size = 0;
        sha256 = null;
        return false;
    }
}
