using System.Diagnostics;
using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Updates;

public static class UpdateHelper
{
    public static void ApplyUpdate(string newExe)
    {
        var currentExe = Process.GetCurrentProcess().MainModule!.FileName!;
        var updater = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SNLUpdater.exe");

        Process.Start(new ProcessStartInfo
        {
            FileName = updater,
            Arguments = $"\"{newExe}\" \"{currentExe}\"",
            UseShellExecute = false
        });

        Environment.Exit(0);
    }
}