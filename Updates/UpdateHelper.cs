using System;
using System.Diagnostics;
using System.IO;

namespace SubnauticaLauncher.Updates;

public static class UpdateHelper
{
    public static void ApplyUpdate(string newExe, string updaterExePath)
    {
        if (!File.Exists(newExe))
            throw new FileNotFoundException("Downloaded update executable was not found.", newExe);

        if (!File.Exists(updaterExePath))
            throw new FileNotFoundException("Updater executable was not found.", updaterExePath);

        string? currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
            throw new InvalidOperationException("Unable to resolve current launcher executable path.");

        int currentPid = Process.GetCurrentProcess().Id;

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterExePath,
            Arguments = $"\"{newExe}\" \"{currentExe}\" {currentPid}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Environment.Exit(0);
    }
}
