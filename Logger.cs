using System;
using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher
{
    public static class Logger
    {
        private static readonly object _lock = new();

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsPath);

                var line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

                lock (_lock)
                {
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // Never crash the launcher because of logging
            }
        }
    }
}