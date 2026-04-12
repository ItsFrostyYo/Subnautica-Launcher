using SubnauticaLauncher.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Versions
{
    public static class LaunchCoordinator
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);

        private const int MOVEFILE_REPLACE_EXISTING = 0x1;
        private const int MOVEFILE_COPY_ALLOWED = 0x2;
        private const int MOVEFILE_WRITE_THROUGH = 0x8;

        public static async Task<bool> CloseAllGameProcessesAsync()
        {
            bool closedAnything = false;
            closedAnything |= await CloseProcessAsync("Subnautica");
            closedAnything |= await CloseProcessAsync("SubnauticaZero");
            return closedAnything;
        }

        public static async Task RestoreActiveFolderUntilGoneAsync(
            string commonPath,
            string activeFolderName,
            string unmanagedFolderName,
            string infoFileName,
            Func<string, string, string?> managedFolderNameResolver)
        {
            string activePath = Path.Combine(commonPath, activeFolderName);
            if (!Directory.Exists(activePath))
                return;

            var start = DateTime.UtcNow;

            while (Directory.Exists(activePath))
            {
                string infoPath = Path.Combine(activePath, infoFileName);
                string targetPath;

                if (File.Exists(infoPath))
                {
                    string? folderName = managedFolderNameResolver(activePath, infoPath);
                    if (string.IsNullOrWhiteSpace(folderName))
                        throw new IOException($"Invalid {infoFileName}");

                    targetPath = Path.Combine(commonPath, folderName);
                }
                else
                {
                    targetPath = Path.Combine(commonPath, unmanagedFolderName);
                }

                if (Directory.Exists(targetPath))
                    await DeleteDirectoryWithRetryAsync(targetPath);

                await MoveDirectoryRobustAsync(activePath, targetPath, timeoutMs: 10000);
                await Task.Delay(100);

                if ((DateTime.UtcNow - start).TotalMilliseconds > 5000)
                    throw new IOException($"Timed out restoring {activeFolderName}.");
            }
        }

        public static async Task MoveFolderWithRetryAsync(
            string sourcePath,
            string destinationPath,
            int timeoutMs = 10000)
            => await MoveDirectoryRobustAsync(sourcePath, destinationPath, timeoutMs);

        private static async Task MoveDirectoryRobustAsync(
            string sourcePath,
            string destinationPath,
            int timeoutMs)
        {
            var start = DateTime.UtcNow;
            int attempts = 0;
            while (true)
            {
                attempts++;
                try
                {
                    NormalizeDirectoryAttributes(sourcePath);
                    if (TryNativeDirectoryMove(sourcePath, destinationPath))
                        return;

                    Directory.Move(sourcePath, destinationPath);
                    return;
                }
                catch (IOException ex)
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        throw new IOException(
                            $"Failed to switch folders after waiting for file locks to clear.{Environment.NewLine}" +
                            $"Source: {sourcePath}{Environment.NewLine}" +
                            $"Destination: {destinationPath}{Environment.NewLine}" +
                            $"Details: {ex.Message}",
                            ex);

                    if (attempts == 1 || attempts % 10 == 0)
                        Logger.Warn($"[LaunchCoordinator] Directory.Move retry due to IO error. Source='{sourcePath}', Dest='{destinationPath}', Attempt={attempts}, Error='{ex.Message}'");

                    TryCloseKnownLockingProcesses(sourcePath, destinationPath);

                    await Task.Delay(500);
                }
                catch (UnauthorizedAccessException ex)
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        throw new UnauthorizedAccessException(
                            $"Access stayed locked while switching folders.{Environment.NewLine}" +
                            $"Source: {sourcePath}{Environment.NewLine}" +
                            $"Destination: {destinationPath}{Environment.NewLine}" +
                            $"Details: {ex.Message}",
                            ex);

                    if (attempts == 1 || attempts % 10 == 0)
                        Logger.Warn($"[LaunchCoordinator] Directory.Move retry due to access denied. Source='{sourcePath}', Dest='{destinationPath}', Attempt={attempts}, Error='{ex.Message}'");

                    TryCloseKnownLockingProcesses(sourcePath, destinationPath);

                    await Task.Delay(500);
                }
            }
        }

        private static async Task<bool> CloseProcessAsync(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return false;

            foreach (var p in processes)
            {
                try
                {
                    p.CloseMainWindow();

                    if (!p.WaitForExit(10_000))
                        p.Kill(true);
                }
                catch
                {
                    try { p.Kill(true); } catch { }
                }
            }

            var waitStart = DateTime.UtcNow;
            while (Process.GetProcessesByName(processName).Length > 0)
            {
                if ((DateTime.UtcNow - waitStart).TotalMilliseconds > 10000)
                    break;
                await Task.Delay(100);
            }

            await Task.Delay(1500);
            return true;
        }

        private static async Task DeleteDirectoryWithRetryAsync(string directoryPath, int timeoutMs = 10000)
        {
            if (!Directory.Exists(directoryPath))
                return;

            var start = DateTime.UtcNow;
            while (Directory.Exists(directoryPath))
            {
                try
                {
                    NormalizeDirectoryAttributes(directoryPath);
                    Directory.Delete(directoryPath, recursive: true);
                    return;
                }
                catch (IOException)
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        throw;

                    await Task.Delay(250);
                }
                catch (UnauthorizedAccessException)
                {
                    if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                        throw;

                    await Task.Delay(250);
                }
            }
        }

        private static void TryCloseKnownLockingProcesses(string sourcePath, string destinationPath)
        {
            string[] candidateNames =
            {
                "Subnautica",
                "SubnauticaZero",
                "UnityCrashHandler64",
                "UnityCrashHandler32"
            };

            foreach (string processName in candidateNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                foreach (Process process in processes)
                {
                    try
                    {
                        string? processPath = null;
                        try
                        {
                            processPath = process.MainModule?.FileName;
                        }
                        catch
                        {
                            // Access to MainModule can fail for some processes.
                        }

                        bool isLikelyLocker =
                            string.Equals(process.ProcessName, "Subnautica", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(process.ProcessName, "SubnauticaZero", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(processPath) &&
                             (processPath.StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase) ||
                              processPath.StartsWith(destinationPath, StringComparison.OrdinalIgnoreCase)));

                        if (!isLikelyLocker)
                            continue;

                        process.Kill(true);
                    }
                    catch
                    {
                        // Best effort only.
                    }
                }
            }
        }

        private static void NormalizeDirectoryAttributes(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch { }
                }

                foreach (string directory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories).Reverse())
                {
                    try
                    {
                        File.SetAttributes(directory, FileAttributes.Normal);
                    }
                    catch { }
                }

                File.SetAttributes(directoryPath, FileAttributes.Normal);
            }
            catch
            {
                // Best effort only.
            }
        }

        private static bool TryNativeDirectoryMove(string sourcePath, string destinationPath)
        {
            try
            {
                return MoveFileEx(
                    sourcePath,
                    destinationPath,
                    MOVEFILE_COPY_ALLOWED | MOVEFILE_WRITE_THROUGH);
            }
            catch
            {
                return false;
            }
        }
    }
}
