using SubnauticaLauncher.Core;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Updates
{
    public static class UpdaterChecker
    {
        private const string Owner = "ItsFrostyYo";
        private const string Repo = "Subnautica-Launcher";
        private const string UpdaterExeName = "SNLUpdater.exe";

        public static async Task EnsureUpdaterAsync()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory; // ✅ NEVER NULL
                string updaterPath = Path.Combine(baseDir, UpdaterExeName);

                using HttpClient http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");

                Logger.Log("[UpdaterChecker] Checking for updater");

                string json = await http.GetStringAsync(
                    $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

                using var doc = JsonDocument.Parse(json);

                foreach (var asset in doc.RootElement
                    .GetProperty("assets")
                    .EnumerateArray())
                {
                    string? name = asset.GetProperty("name").GetString();

                    if (name != UpdaterExeName)
                        continue;

                    string url =
                        asset.GetProperty("browser_download_url").GetString()!;

                    Logger.Log("[UpdaterChecker] Downloading updater");

                    byte[] data = await http.GetByteArrayAsync(url);

                    await File.WriteAllBytesAsync(updaterPath, data);

                    Logger.Log("[UpdaterChecker] Updater ready");
                    return;
                }

                Logger.Warn("[UpdaterChecker] Updater asset not found in release");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[UpdaterChecker] Failed to verify updater");
            }
        }
    }
}