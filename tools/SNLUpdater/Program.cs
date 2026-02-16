using System.Diagnostics;
using System.IO;
using System.Text;

internal static class Program
{
    private const int MaxReplaceAttempts = 50;
    private const int ReplaceDelayMs = 200;

    private static string LogPath = string.Empty;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                return Fail("Usage: SNLUpdater.exe <newExePath> <targetExePath> [launcherPid]");
            }

            string newExePath = Path.GetFullPath(args[0]);
            string targetExePath = Path.GetFullPath(args[1]);
            int? launcherPid = TryParsePid(args);

            string appDir = Path.GetDirectoryName(targetExePath)
                ?? AppContext.BaseDirectory;

            Directory.CreateDirectory(appDir);

            string logsDir = Path.Combine(appDir, "logs");
            Directory.CreateDirectory(logsDir);
            LogPath = Path.Combine(logsDir, "updater.log");

            Log("Updater started");
            Log($"New exe: {newExePath}");
            Log($"Target exe: {targetExePath}");

            if (!File.Exists(newExePath))
                return Fail("Downloaded launcher executable is missing.");

            WaitForLauncherToExit(launcherPid, targetExePath);

            string stagedPath = Path.Combine(appDir, "SubnauticaLauncher.staged.exe");
            string backupPath = Path.Combine(appDir, "SubnauticaLauncher.previous.exe");

            CleanupFile(stagedPath);
            CleanupFile(backupPath);

            File.Copy(newExePath, stagedPath, overwrite: true);
            if (File.Exists(targetExePath))
            {
                ReplaceWithRetry(stagedPath, targetExePath, backupPath);
                CleanupFile(backupPath);
            }
            else
            {
                File.Move(stagedPath, targetExePath, overwrite: true);
            }

            CleanupFile(stagedPath);
            CleanupFile(newExePath);

            StartUpdatedLauncher(targetExePath);
            Log("Updater completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("Updater failed: " + ex);
        }
    }

    private static int? TryParsePid(string[] args)
    {
        if (args.Length < 3)
            return null;

        return int.TryParse(args[2], out int pid) && pid > 0 ? pid : null;
    }

    private static void WaitForLauncherToExit(int? launcherPid, string targetExePath)
    {
        if (launcherPid.HasValue)
        {
            try
            {
                using Process launcher = Process.GetProcessById(launcherPid.Value);
                Log($"Waiting for launcher PID {launcherPid.Value} to exit...");

                if (!launcher.WaitForExit(15000))
                {
                    Log("Launcher PID did not exit in 15s, continuing with retry-based replacement.");
                }
            }
            catch (ArgumentException)
            {
                Log("Launcher PID not running, continuing.");
            }
            catch (Exception ex)
            {
                Log("Failed waiting by PID: " + ex.Message);
            }
        }

        // Short settle delay so file handles are fully released.
        Thread.Sleep(250);

        // Optional additional wait if a process with the same executable path is still alive.
        for (int i = 0; i < 40; i++)
        {
            bool stillRunning = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetExePath))
                .Any(p =>
                {
                    try
                    {
                        return string.Equals(
                            p.MainModule?.FileName,
                            targetExePath,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                });

            if (!stillRunning)
                return;

            Thread.Sleep(200);
        }
    }

    private static void ReplaceWithRetry(string stagedPath, string targetExePath, string backupPath)
    {
        for (int attempt = 1; attempt <= MaxReplaceAttempts; attempt++)
        {
            try
            {
                File.Replace(stagedPath, targetExePath, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException ex)
            {
                Log($"Replace attempt {attempt}/{MaxReplaceAttempts} failed: {ex.Message}");
                Thread.Sleep(ReplaceDelayMs);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log($"Replace attempt {attempt}/{MaxReplaceAttempts} denied: {ex.Message}");
                Thread.Sleep(ReplaceDelayMs);
            }
        }

        throw new IOException("Failed to replace launcher executable after multiple retries.");
    }

    private static void StartUpdatedLauncher(string targetExePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = targetExePath,
            WorkingDirectory = Path.GetDirectoryName(targetExePath) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log($"Cleanup failed for {path}: {ex.Message}");
        }
    }

    private static int Fail(string message)
    {
        Log(message);
        return 1;
    }

    private static void Log(string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LogPath))
                return;

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Updater logging must never crash updater flow.
        }
    }
}
