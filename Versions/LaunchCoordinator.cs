using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Versions
{
    public static class LaunchCoordinator
    {
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
                    Directory.Delete(targetPath, true);

                Directory.Move(activePath, targetPath);
                await Task.Delay(100);

                if ((DateTime.UtcNow - start).TotalMilliseconds > 5000)
                    throw new IOException($"Timed out restoring {activeFolderName}.");
            }
        }

        public static async Task MoveFolderWithRetryAsync(
            string sourcePath,
            string destinationPath,
            int timeoutMs = 10000)
        {
            var start = DateTime.UtcNow;
            while (true)
            {
                try
                {
                    Directory.Move(sourcePath, destinationPath);
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

            await Task.Delay(250);
            return true;
        }
    }
}
