using System.Diagnostics;

internal static class Program
{
    private const int ReplaceAttempts = 50;
    private const int RetryDelayMs = 200;

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            WaitForLauncherExit(options.ProcessId);
            ApplyPayload(options.SourceDirectory, options.TargetDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(options.TargetDirectory, "SubnauticaLauncher.exe"),
                WorkingDirectory = options.TargetDirectory,
                UseShellExecute = true
            });
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static UpdateArguments ParseArguments(string[] args)
    {
        string? source = ReadOption(args, "--source-dir");
        string? target = ReadOption(args, "--target-dir");
        string? pidText = ReadOption(args, "--pid");

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) ||
            !int.TryParse(pidText, out int processId) || processId <= 0)
        {
            throw new ArgumentException("Usage: --source-dir <downloaded-update-directory> --target-dir <launcher-directory> --pid <launcher-pid>");
        }

        string sourcePath = Path.GetFullPath(source);
        string targetPath = Path.GetFullPath(target);
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException("Downloaded update directory was not found: " + sourcePath);
        if (!File.Exists(Path.Combine(sourcePath, "SubnauticaLauncher.exe")))
            throw new FileNotFoundException("Downloaded update does not contain SubnauticaLauncher.exe.");

        return new UpdateArguments(sourcePath, targetPath, processId);
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
            launcher.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            // The launcher has already exited.
        }
    }

    private static void ApplyPayload(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        try
        {
            foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
                if (relativePath.StartsWith("Dependencies" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                string targetPath = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
                if (!targetPath.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update payload path cannot leave the launcher directory.");

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                string stagedPath = targetPath + ".staged";
                File.Copy(sourcePath, stagedPath, overwrite: true);
                for (int attempt = 1; attempt <= ReplaceAttempts; attempt++)
                {
                    try
                    {
                        File.Move(stagedPath, targetPath, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
                    catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts) { Thread.Sleep(RetryDelayMs); }
                }
                if (File.Exists(stagedPath))
                    throw new IOException("Could not replace update file after repeated attempts: " + relativePath);
            }

            string sourceDependencies = Path.Combine(sourceDirectory, "Dependencies");
            if (Directory.Exists(sourceDependencies))
                ReplaceDirectory(sourceDependencies, Path.Combine(targetDirectory, "Dependencies"));
        }
        finally
        {
            TryDeleteDirectory(sourceDirectory);
        }
    }

    private static void ReplaceDirectory(string sourceDirectory, string targetDirectory)
    {
        string staged = targetDirectory + ".update-staged";
        string backup = targetDirectory + ".update-backup";
        DeleteDirectoryWithRetry(staged);
        DeleteDirectoryWithRetry(backup);
        CopyDirectory(sourceDirectory, staged);

        try
        {
            if (Directory.Exists(targetDirectory)) MoveDirectoryWithRetry(targetDirectory, backup);
            MoveDirectoryWithRetry(staged, targetDirectory);
            DeleteDirectoryWithRetry(backup);
        }
        catch
        {
            if (!Directory.Exists(targetDirectory) && Directory.Exists(backup))
                MoveDirectoryWithRetry(backup, targetDirectory);
            throw;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
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
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary-file cleanup must not stop the launcher from reopening.
        }
    }

    private sealed record UpdateArguments(string SourceDirectory, string TargetDirectory, int ProcessId);
}
