using SubnauticaLauncher.Core;
using SubnauticaLauncher.Installer;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher
{
    public static class NewInstaller
    {
        public static bool IsBootstrapRequired()
        {
            if (!Directory.Exists(AppPaths.ToolsPath) ||
                !Directory.Exists(AppPaths.DataPath) ||
                !Directory.Exists(AppPaths.LogsPath))
            {
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
    }
}
