using System.Diagnostics;
using System.IO;
using System.Net.Http;

public static class UpdateDownloader
{
    public static async Task DownloadAndApplyAsync(UpdateInfo info)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SubnauticaLauncherUpdate");
        Directory.CreateDirectory(tempDir);

        var newExe = Path.Combine(tempDir, "SubnauticaLauncher.exe");

        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(info.DownloadUrl);
        await File.WriteAllBytesAsync(newExe, bytes);

        LaunchUpdater(newExe);
    }

    private static void LaunchUpdater(string newExePath)
    {
        var updaterExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater.exe");

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterExe,
            Arguments = $"\"{newExePath}\" \"{Process.GetCurrentProcess().MainModule!.FileName}\"",
            UseShellExecute = true
        });

        Environment.Exit(0);
    }
}