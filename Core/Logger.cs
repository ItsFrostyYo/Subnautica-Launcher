using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SubnauticaLauncher.Core
{
    public static class Logger
    {
        private static readonly object WriteLock = new();
        private static readonly object ThrottleLock = new();
        private static readonly Dictionary<string, DateTime> LastWriteByThrottleKey =
            new(StringComparer.Ordinal);
        private static StreamWriter? _cachedWriter;
        private static string? _cachedWriterPath;

        private static string LogDirectory => AppPaths.LogsPath;
        private static string DefaultLogFile => AppPaths.LogFile;

        public static void TruncateOnStartup()
        {
            try
            {
                lock (WriteLock)
                {
                    _cachedWriter?.Dispose();
                    _cachedWriter = null;
                    _cachedWriterPath = null;

                    string logFile = DefaultLogFile;
                    if (File.Exists(logFile))
                        File.WriteAllText(logFile, string.Empty);
                }
            }
            catch
            {
                // Must never crash the app.
            }
        }

        public static void Log(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void LogTo(string relativeFilePath, string message) =>
            WriteToFile(relativeFilePath, "INFO", message);

        public static void WarnTo(string relativeFilePath, string message) =>
            WriteToFile(relativeFilePath, "WARN", message);

        public static void ErrorTo(string relativeFilePath, string message) =>
            WriteToFile(relativeFilePath, "ERROR", message);

        public static void LogThrottled(string throttleKey, string message, TimeSpan minInterval)
        {
            if (ShouldWriteThrottled(throttleKey, minInterval))
                Write("INFO", message);
        }

        public static void WarnThrottled(string throttleKey, string message, TimeSpan minInterval)
        {
            if (ShouldWriteThrottled(throttleKey, minInterval))
                Write("WARN", message);
        }

        public static void Exception(Exception ex, string? context = null)
        {
            Write("EXCEPTION", BuildExceptionMessage(ex, context));
        }

        public static void ExceptionTo(string relativeFilePath, Exception ex, string? context = null)
        {
            WriteToFile(relativeFilePath, "EXCEPTION", BuildExceptionMessage(ex, context));
        }

        private static string BuildExceptionMessage(Exception ex, string? context)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(context))
                sb.AppendLine(context);

            sb.AppendLine(ex.ToString());
            return sb.ToString();
        }

        private static void Write(string level, string message)
        {
            WriteToFile("launcher.log", level, message);
        }

        private static void WriteToFile(string relativeFilePath, string level, string message)
        {
            try
            {
                string targetLogFile = ResolveLogPath(relativeFilePath);
                string? targetDirectory = Path.GetDirectoryName(targetLogFile);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                    targetDirectory = LogDirectory;

                Directory.CreateDirectory(targetDirectory);

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

                lock (WriteLock)
                {
                    var writer = GetOrCreateWriter(targetLogFile);
                    writer.WriteLine(line);
                    writer.Flush();
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }

        private static StreamWriter GetOrCreateWriter(string filePath)
        {
            if (_cachedWriter != null && string.Equals(_cachedWriterPath, filePath, StringComparison.OrdinalIgnoreCase))
                return _cachedWriter;

            _cachedWriter?.Dispose();
            _cachedWriter = new StreamWriter(filePath, append: true, Encoding.UTF8) { AutoFlush = false };
            _cachedWriterPath = filePath;
            return _cachedWriter;
        }

        private static string ResolveLogPath(string relativeFilePath)
        {
            if (string.IsNullOrWhiteSpace(relativeFilePath))
                return DefaultLogFile;

            string normalized = relativeFilePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            string fullBase = Path.GetFullPath(LogDirectory);
            string combined = Path.GetFullPath(Path.Combine(fullBase, normalized));

            if (!combined.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                return DefaultLogFile;

            return combined;
        }

        private static bool ShouldWriteThrottled(string throttleKey, TimeSpan minInterval)
        {
            if (string.IsNullOrWhiteSpace(throttleKey))
                return true;

            DateTime now = DateTime.UtcNow;
            lock (ThrottleLock)
            {
                if (LastWriteByThrottleKey.TryGetValue(throttleKey, out DateTime lastUtc))
                {
                    if (now - lastUtc < minInterval)
                        return false;
                }

                LastWriteByThrottleKey[throttleKey] = now;
                return true;
            }
        }
    }
}
