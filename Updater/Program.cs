using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using SubnauticaLauncher.Updates;

internal static class Program
{
    private const int ReplaceAttempts = 50;
    private const int RetryDelayMs = 200;
    private static string _logPath = string.Empty;

    [STAThread]
    private static int Main(string[] args)
    {
        UpdateProgressWindow? window = null;
        Application? application = null;
        UpdateArguments? options = null;
        try
        {
            options = ParseArguments(args);
            Directory.CreateDirectory(options.TargetDirectory);
            string logsDirectory = Path.Combine(options.TargetDirectory, "Logs");
            Directory.CreateDirectory(logsDirectory);
            _logPath = Path.Combine(logsDirectory, "SNLUpdater.log");
            Log($"{(options.IsRepair ? "Repair" : "Update")} started for launcher v{options.Version}.");

            WaitForLauncherExit(options.ProcessId);
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            window = new UpdateProgressWindow(options.Version);
            window.SetDetectedUpdate(options.IsRepair
                ? $"Repairing launcher v{options.Version}"
                : $"Updating to v{options.Version}");
            window.Show();
            window.Apply(new LauncherUpdateProgress("Launcher closed", "Waiting for launcher helpers to finish...", 5));
            window.PumpUi();
            WaitForLauncherHelpers(options.TargetDirectory, window);

            ApplyPayload(options.SourceDirectory, options.TargetDirectory, window);
            ValidateInstallation(options.TargetDirectory, options.Version, window);
            window.Apply(new LauncherUpdateProgress(
                options.IsRepair ? "Repair complete" : "Update complete",
                "Reopening Subnautica Launcher...",
                100));
            window.PumpUi();
            StartLauncher(options.TargetDirectory);
            Log("Update completed successfully.");
            Thread.Sleep(500);
            return 0;
        }
        catch (Exception exception)
        {
            Log("Update failed: " + exception);
            if (window is not null)
            {
                window.Apply(new LauncherUpdateProgress("Update failed", exception.Message, 100));
                window.PumpUi();
                try { window.Close(); } catch { }
                window = null;
            }
            MessageBox.Show(
                $"The launcher update could not be completed.\n\n{exception.Message}",
                "SNL Updater",
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                MessageBoxResult.OK,
                MessageBoxOptions.DefaultDesktopOnly);
            if (options is not null)
            {
                try
                {
                    if (File.Exists(Path.Combine(options.TargetDirectory, "SubnauticaLauncher.exe")))
                        StartLauncher(options.TargetDirectory);
                }
                catch (Exception restartException)
                {
                    Log("Could not reopen the launcher after the failure: " + restartException.Message);
                }
            }
            return 1;
        }
        finally
        {
            try { window?.Close(); } catch { }
            try { application?.Shutdown(); } catch { }
        }
    }

    private static UpdateArguments ParseArguments(string[] args)
    {
        string? source = ReadOption(args, "--source-dir");
        string? target = ReadOption(args, "--target-dir");
        string? pidText = ReadOption(args, "--pid");
        string? requestedVersion = ReadOption(args, "--version");
        bool isRepair = args.Any(arg => string.Equals(arg, "--repair", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) ||
            !int.TryParse(pidText, out int processId) || processId <= 0)
            throw new ArgumentException("Usage: --source-dir <directory> --target-dir <directory> --pid <launcher-pid> [--version <version>]");

        string sourcePath = Path.GetFullPath(source);
        string targetPath = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(sourcePath)) throw new DirectoryNotFoundException("Downloaded update directory was not found: " + sourcePath);
        string launcherPayload = Path.Combine(sourcePath, "SubnauticaLauncher.exe");
        if (!isRepair && !File.Exists(launcherPayload))
            throw new FileNotFoundException("Downloaded update does not contain SubnauticaLauncher.exe.");
        string version = ResolveUpdateVersion(requestedVersion, launcherPayload);
        return new UpdateArguments(sourcePath, targetPath, processId, version, isRepair);
    }

    private static string ResolveUpdateVersion(string? requestedVersion, string launcherPayload)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion)) return requestedVersion;
        if (!File.Exists(launcherPayload))
            throw new ArgumentException("A version is required when a repair payload does not contain SubnauticaLauncher.exe.");
        string? fileVersion = FileVersionInfo.GetVersionInfo(launcherPayload).FileVersion;
        return Version.TryParse(fileVersion, out Version? version) ? version.ToString() : "Unknown";
    }

    private static string? ReadOption(string[] args, string name)
    {
        int index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void WaitForLauncherExit(int processId)
    {
        try
        {
            using Process launcher = Process.GetProcessById(processId);
            Log($"Waiting for launcher PID {processId} to close.");
            if (!launcher.WaitForExit(30000))
                throw new TimeoutException("The launcher did not close within 30 seconds.");
        }
        catch (ArgumentException)
        {
            Log("Launcher was already closed.");
        }
    }

    private static void WaitForLauncherHelpers(string targetDirectory, UpdateProgressWindow window)
    {
        var gracefulWait = Stopwatch.StartNew();
        while (gracefulWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            using ProcessCollection helpers = FindLauncherHelpers(targetDirectory);
            if (helpers.Count == 0) return;
            window.Apply(new LauncherUpdateProgress(
                "Closing launcher helpers...",
                $"Waiting for {helpers.Count} launcher helper process{(helpers.Count == 1 ? string.Empty : "es")}...",
                7));
            window.PumpUi();
            Thread.Sleep(150);
        }

        using (ProcessCollection helpers = FindLauncherHelpers(targetDirectory))
        {
            foreach (Process helper in helpers.Processes)
            {
                try
                {
                    Log($"Stopping launcher helper PID {helper.Id}: {helper.ProcessName}.");
                    helper.Kill(entireProcessTree: true);
                    helper.WaitForExit(5000);
                }
                catch (Exception exception)
                {
                    Log($"Could not stop launcher helper PID {helper.Id}: {exception.Message}");
                }
            }
        }

        using ProcessCollection remaining = FindLauncherHelpers(targetDirectory);
        if (remaining.Count > 0)
            throw new IOException("Launcher helper processes are still using update files. Close them and try again.");
    }

    private static ProcessCollection FindLauncherHelpers(string targetDirectory)
    {
        string launcherPath = Path.Combine(targetDirectory, "SubnauticaLauncher.exe");
        string imGuiHostPath = Path.Combine(targetDirectory, "Dependencies", "SubnauticaLauncher.ImGuiHost.exe");
        var matches = new List<Process>();
        foreach (Process process in Process.GetProcesses())
        {
            if (process.Id == Environment.ProcessId)
            {
                process.Dispose();
                continue;
            }

            try
            {
                string? executablePath = process.MainModule?.FileName;
                if (string.Equals(executablePath, launcherPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(executablePath, imGuiHostPath, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(process);
                    continue;
                }
            }
            catch
            {
                // Protected and already-exited processes cannot be launcher helpers at these paths.
            }

            process.Dispose();
        }
        return new ProcessCollection(matches);
    }

    private static void ApplyPayload(string sourceDirectory, string targetDirectory, UpdateProgressWindow window)
    {
        Directory.CreateDirectory(targetDirectory);
        string[] ordinaryFiles = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                string relative = Path.GetRelativePath(sourceDirectory, path);
                return !relative.StartsWith("Dependencies" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(relative, "Dependencies.zip", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => string.Equals(Path.GetFileName(path), "SubnauticaLauncher.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();

        string dependencyArchive = Path.Combine(sourceDirectory, "Dependencies.zip");
        string dependencyDirectory = Path.Combine(sourceDirectory, "Dependencies");
        if (File.Exists(dependencyArchive))
        {
            window.Apply(new LauncherUpdateProgress("Updating dependencies...", "Extracting dependency bundle...", 15));
            window.PumpUi();
            ReplaceDependencyArchive(dependencyArchive, Path.Combine(targetDirectory, "Dependencies"));
        }
        else if (Directory.Exists(dependencyDirectory))
        {
            window.Apply(new LauncherUpdateProgress("Updating dependencies...", "Replacing dependency files...", 15));
            window.PumpUi();
            ReplaceDirectoryFromDirectory(dependencyDirectory, Path.Combine(targetDirectory, "Dependencies"));
        }

        for (int index = 0; index < ordinaryFiles.Length; index++)
        {
            string sourcePath = ordinaryFiles[index];
            string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            string targetPath = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
            if (!targetPath.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update payload path cannot leave the launcher directory.");

            window.Apply(new LauncherUpdateProgress(
                "Replacing launcher files...",
                Path.GetFileName(relativePath),
                60 + ((index + 1d) / Math.Max(ordinaryFiles.Length, 1) * 30)));
            window.PumpUi();
            ReplaceFile(sourcePath, targetPath);
        }

        window.Apply(new LauncherUpdateProgress("Cleaning up...", "Removing temporary update files...", 92));
        window.PumpUi();
        TryDeleteDirectory(sourceDirectory);
    }

    private static void ValidateInstallation(string targetDirectory, string requestedVersion, UpdateProgressWindow window)
    {
        window.Apply(new LauncherUpdateProgress("Verifying installed files...", "Checking the repaired launcher installation...", 95));
        window.PumpUi();

        string manifestPath = Path.Combine(targetDirectory, "Data", "IntegrityManifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The repaired installation does not contain Data/IntegrityManifest.json.", manifestPath);

        IntegrityManifest manifest = JsonSerializer.Deserialize<IntegrityManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("The installed integrity manifest is invalid.");
        if (!Version.TryParse(manifest.Version, out Version? manifestVersion) || manifest.Files.Count == 0)
            throw new InvalidDataException("The installed integrity manifest is incomplete.");
        if (Version.TryParse(requestedVersion, out Version? expectedVersion) && manifestVersion != expectedVersion)
            throw new InvalidDataException($"The installed integrity manifest is for v{manifestVersion}, not the requested v{expectedVersion}.");

        string root = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        for (int index = 0; index < manifest.Files.Count; index++)
        {
            IntegrityManifestFile expected = manifest.Files[index];
            string fullPath = Path.GetFullPath(Path.Combine(root, expected.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installed integrity manifest contains an unsafe path.");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"A launcher file is still missing after repair: {expected.Path}", fullPath);

            var info = new FileInfo(fullPath);
            if (info.Length != expected.Size)
                throw new InvalidDataException($"A launcher file still has the wrong size after repair: {expected.Path}");
            using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"A launcher file still has the wrong checksum after repair: {expected.Path}");

            window.Apply(new LauncherUpdateProgress(
                "Verifying installed files...",
                expected.Path,
                95 + ((index + 1d) / manifest.Files.Count * 4)));
            if (index % 8 == 0) window.PumpUi();
        }
    }

    private static void ReplaceFile(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string stagedPath = targetPath + ".staged";
        File.Copy(sourcePath, stagedPath, overwrite: true);
        for (int attempt = 1; attempt <= ReplaceAttempts; attempt++)
        {
            try { File.Move(stagedPath, targetPath, overwrite: true); return; }
            catch (IOException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
        }
        throw new IOException("Could not replace update file after repeated attempts: " + Path.GetFileName(targetPath));
    }

    private static void ReplaceDependencyArchive(string archivePath, string targetDirectory)
    {
        string staged = targetDirectory + ".update-staged";
        DeleteDirectoryWithRetry(staged);
        Directory.CreateDirectory(staged);
        ZipFile.ExtractToDirectory(archivePath, staged, overwriteFiles: true);
        SwapDirectory(staged, targetDirectory);
    }

    private static void ReplaceDirectoryFromDirectory(string sourceDirectory, string targetDirectory)
    {
        string staged = targetDirectory + ".update-staged";
        DeleteDirectoryWithRetry(staged);
        CopyDirectory(sourceDirectory, staged);
        SwapDirectory(staged, targetDirectory);
    }

    private static void SwapDirectory(string staged, string target)
    {
        string backup = target + ".update-backup";
        DeleteDirectoryWithRetry(backup);
        try
        {
            if (Directory.Exists(target)) MoveDirectoryWithRetry(target, backup);
            MoveDirectoryWithRetry(staged, target);
            DeleteDirectoryWithRetry(backup);
        }
        catch
        {
            if (!Directory.Exists(target) && Directory.Exists(backup)) MoveDirectoryWithRetry(backup, target);
            throw;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destinationPath = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void MoveDirectoryWithRetry(string source, string destination)
    {
        for (int attempt = 1; attempt <= ReplaceAttempts; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
        }
        throw new IOException("Could not replace launcher dependencies after repeated attempts.");
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (int attempt = 1; attempt <= ReplaceAttempts && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
            catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
        }
        if (Directory.Exists(path)) throw new IOException("Could not remove stale update directory: " + path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) { Log("Temporary update cleanup failed: " + exception.Message); }
    }

    private static void StartLauncher(string targetDirectory) => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(targetDirectory, "SubnauticaLauncher.exe"),
        WorkingDirectory = targetDirectory,
        UseShellExecute = true
    });

    private static void Log(string message)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_logPath))
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    private sealed class ProcessCollection(List<Process> processes) : IDisposable
    {
        public IReadOnlyList<Process> Processes { get; } = processes;
        public int Count => Processes.Count;

        public void Dispose()
        {
            foreach (Process process in Processes) process.Dispose();
        }
    }

    private sealed record UpdateArguments(string SourceDirectory, string TargetDirectory, int ProcessId, string Version, bool IsRepair);

    private sealed class IntegrityManifest
    {
        public string Version { get; set; } = string.Empty;
        public List<IntegrityManifestFile> Files { get; set; } = [];
    }

    private sealed class IntegrityManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}
