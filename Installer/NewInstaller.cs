using SubnauticaLauncher.Core;
using SubnauticaLauncher.Installer;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SubnauticaLauncher
{
    public static class NewInstaller
    {
        private static readonly (string FileName, string Url)[] RequiredHelperTools =
        {
            (
                "ExplosionResetHelper2018.exe",
                "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/main/tools/ExplosionResetHelper2018.exe"
            ),
            (
                "ExplosionResetHelper2022.exe",
                "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/main/tools/ExplosionResetHelper2022.exe"
            )
        };

        public static bool IsBootstrapRequired()
        {
            if (!Directory.Exists(AppPaths.ToolsPath) ||
                !Directory.Exists(AppPaths.DataPath) ||
                !Directory.Exists(AppPaths.LogsPath))
            {
                return true;
            }

            foreach (var tool in RequiredHelperTools)
            {
                string targetPath = Path.Combine(AppPaths.ToolsPath, tool.FileName);
                if (!File.Exists(targetPath))
                    return true;
            }

            if (!File.Exists(DepotDownloaderInstaller.DepotDownloaderExe))
                return true;

            return false;
        }

        public static async Task RunAsync(
            IProgress<string>? status = null,
            bool throwOnFailure = false)
        {
            try
            {
                status?.Report("Creating runtime folders...");

                Directory.CreateDirectory(AppPaths.ToolsPath);
                Directory.CreateDirectory(AppPaths.DataPath);
                Directory.CreateDirectory(AppPaths.LogsPath);

                bool missingExplosionHelpers = RequiredHelperTools.Any(tool =>
                    !File.Exists(Path.Combine(AppPaths.ToolsPath, tool.FileName)));

                if (missingExplosionHelpers)
                {
                    using HttpClient http = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(45)
                    };

                    http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
                    await EnsureExplosionHelpersAsync(http, status);
                }

                status?.Report("Checking DepotDownloader...");
                await DepotDownloaderInstaller.EnsureInstalledAsync();

                status?.Report("Runtime setup verified.");
                Logger.Log("[Installer] Runtime tool/folder verification complete");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[Installer] Failed to verify/install tools");

                if (throwOnFailure)
                    throw;
            }
        }

        private static async Task EnsureExplosionHelpersAsync(
            HttpClient http,
            IProgress<string>? status)
        {
            foreach (var tool in RequiredHelperTools)
            {
                string targetPath = Path.Combine(AppPaths.ToolsPath, tool.FileName);

                if (File.Exists(targetPath))
                {
                    Logger.Log($"[Installer] Found {tool.FileName}");
                    continue;
                }

                status?.Report($"Downloading {tool.FileName}...");
                Logger.Warn($"[Installer] Missing {tool.FileName}, downloading...");

                byte[] data = await http.GetByteArrayAsync(tool.Url);
                await File.WriteAllBytesAsync(targetPath, data);

                Logger.Log($"[Installer] Installed {tool.FileName}");
            }
        }
    }
}
