using System;
using System.IO;
using System.Text;

namespace SubnauticaLauncher
{
    public static class Logger
    {
        private static readonly object _lock = new();

        private static string LogDirectory =>
            AppPaths.LogsPath;

        private static string LogFile =>
            AppPaths.LogFile;

        public static void Log(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Exception(Exception ex, string? context = null)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(context))
                sb.AppendLine(context);

            sb.AppendLine(ex.ToString());

            Write("EXCEPTION", sb.ToString());
        }

        private static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                var line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

                lock (_lock)
                {
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
            }
            catch
            {
                // 🔒 Logging must NEVER crash the app
            }
        }
    }
}