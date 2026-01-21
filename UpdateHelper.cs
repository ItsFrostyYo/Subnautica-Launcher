using System.Diagnostics;
using System.IO;

namespace SubnauticaLauncher.Updater
{
    public static class UpdateHelper
    {
        public static void ApplyUpdate(string extractedFolder)
        {
            var updater = Path.Combine(
                AppContext.BaseDirectory,
                "SNLUpdater.exe");

            if (!File.Exists(updater))
                throw new FileNotFoundException("SNLUpdater.exe not found.");

            var currentExe = Process.GetCurrentProcess().MainModule!.FileName!;

            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                Arguments = $"\"{extractedFolder}\" \"{currentExe}\"",
                UseShellExecute = false
            });

            Environment.Exit(0);
        }
    }
}