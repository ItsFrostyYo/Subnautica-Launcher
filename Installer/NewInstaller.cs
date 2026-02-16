using SubnauticaLauncher.Core;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SubnauticaLauncher
{
    public static class NewInstaller
    {
        private static readonly (string FileName, string Url)[] RequiredTools =
        {
            (
                "ExplosionResetHelper2018.exe",
                "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/0edb1f1fdefa2c87c97f16b2fb9da0d1825fadc6/ExplosionResetHelper2018.exe"
            ),
            (
                "ExplosionResetHelper2022.exe",
                "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/0edb1f1fdefa2c87c97f16b2fb9da0d1825fadc6/ExplosionResetHelper2022.exe"
            )
        };

        public static async Task RunAsync()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.ToolsPath);

                using HttpClient http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                foreach (var tool in RequiredTools)
                {
                    string targetPath = Path.Combine(AppPaths.ToolsPath, tool.FileName);

                    if (File.Exists(targetPath))
                    {
                        Logger.Log($"[Installer] Found {tool.FileName}");
                        continue;
                    }

                    Logger.Warn($"[Installer] Missing {tool.FileName}, downloading…");

                    byte[] data = await http.GetByteArrayAsync(tool.Url);

                    await File.WriteAllBytesAsync(targetPath, data);

                    Logger.Log($"[Installer] Installed {tool.FileName}");
                }

                Logger.Log("[Installer] Tool verification complete");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "[Installer] Failed to verify/install tools");
            }
        }
    }
}