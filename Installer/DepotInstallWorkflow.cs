using SubnauticaLauncher.Core;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Installer;

internal static class DepotInstallWorkflow
{
    private static readonly Regex PercentRegex = new(
        @"(?<!\d)(\d{1,3}(?:\.\d+)?)%",
        RegexOptions.Compiled);

    private static readonly string[] PromptMarkers =
    {
        "enter steam guard code",
        "enter your steam guard code",
        "enter two-factor code",
        "enter authentication code",
        "enter auth code",
        "enter email code",
        "enter device code",
        "enter account password",
        "password:"
    };

    public static async Task InstallAsync(
        GameVersionInstallDefinition version,
        string username,
        string password,
        string installDir,
        string infoFileName,
        string launcherMarker)
    {
        // Legacy behavior: keep native console flow for existing call sites.
        ValidateBasicAuth(username, password);
        Directory.CreateDirectory(installDir);

        string args =
            $"-app {version.SteamAppId} -depot {version.SteamDepotId} -manifest {version.ManifestId} -username \"{username}\" -password \"{password}\" -dir \"{installDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName = DepotDownloaderInstaller.DepotDownloaderExe,
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start DepotDownloader.");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception("DepotDownloader failed. Check console output.");

        CleanupDepotDownloaderFolders(installDir);
        WriteVersionInfo(version, installDir, infoFileName, launcherMarker);
    }

    public static async Task InstallAsync(
        GameVersionInstallDefinition version,
        DepotInstallAuthOptions auth,
        string installDir,
        string infoFileName,
        string launcherMarker,
        DepotInstallCallbacks? callbacks,
        CancellationToken cancellationToken)
    {
        ValidateAuthOptions(auth);

        Directory.CreateDirectory(installDir);

        callbacks?.OnStatus?.Invoke("Preparing DepotDownloader...");

        var psi = new ProcessStartInfo
        {
            FileName = DepotDownloaderInstaller.DepotDownloaderExe,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-app");
        psi.ArgumentList.Add(version.SteamAppId.ToString());
        psi.ArgumentList.Add("-depot");
        psi.ArgumentList.Add(version.SteamDepotId.ToString());
        psi.ArgumentList.Add("-manifest");
        psi.ArgumentList.Add(version.ManifestId.ToString());
        psi.ArgumentList.Add("-username");
        psi.ArgumentList.Add(auth.Username);

        if (!string.IsNullOrWhiteSpace(auth.Password))
        {
            psi.ArgumentList.Add("-password");
            psi.ArgumentList.Add(auth.Password);
        }

        if (auth.RememberPassword)
            psi.ArgumentList.Add("-remember-password");

        if (auth.PreferTwoFactorCode)
            psi.ArgumentList.Add("-no-mobile");

        psi.ArgumentList.Add("-dir");
        psi.ArgumentList.Add(installDir);

        using var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        if (!process.Start())
            throw new Exception("Failed to start DepotDownloader.");

        callbacks?.OnOutput?.Invoke($"DepotDownloader started (PID {process.Id}).");
        callbacks?.OnStatus?.Invoke("DepotDownloader started.");

        using var cancelRegistration = cancellationToken.Register(() =>
        {
            TryKillProcess(process);
        });

        var recentLines = new List<string>();
        string lastPromptKey = "";
        DateTime lastPromptAtUtc = DateTime.MinValue;
        var promptLock = new SemaphoreSlim(1, 1);

        Task outputTask = PumpStreamAsync(
            process.StandardOutput,
            isError: false,
            callbacks,
            process,
            recentLines,
            promptLock,
            () => lastPromptKey,
            value => lastPromptKey = value,
            () => lastPromptAtUtc,
            value => lastPromptAtUtc = value,
            cancellationToken);

        Task errorTask = PumpStreamAsync(
            process.StandardError,
            isError: true,
            callbacks,
            process,
            recentLines,
            promptLock,
            () => lastPromptKey,
            value => lastPromptKey = value,
            () => lastPromptAtUtc,
            value => lastPromptAtUtc = value,
            cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            string detail = recentLines.Count == 0
                ? "No detailed output was captured."
                : string.Join(Environment.NewLine, recentLines);

            throw new Exception(
                $"DepotDownloader failed (exit code {process.ExitCode}).{Environment.NewLine}{detail}");
        }

        callbacks?.OnStatus?.Invoke("Finalizing install files...");
        CleanupDepotDownloaderFolders(installDir);
        WriteVersionInfo(version, installDir, infoFileName, launcherMarker);
        callbacks?.OnProgress?.Invoke(100);
        callbacks?.OnStatus?.Invoke("Install complete.");
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        bool isError,
        DepotInstallCallbacks? callbacks,
        Process process,
        List<string> recentLines,
        SemaphoreSlim promptLock,
        Func<string> getLastPromptKey,
        Action<string> setLastPromptKey,
        Func<DateTime> getLastPromptAtUtc,
        Action<DateTime> setLastPromptAtUtc,
        CancellationToken cancellationToken)
    {
        var buffer = new char[256];
        var lineBuilder = new StringBuilder();

        while (true)
        {
            int read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0)
                break;

            for (int i = 0; i < read; i++)
            {
                char ch = buffer[i];

                if (ch == '\r')
                {
                    string? emittedLine = EmitLine(lineBuilder, isError, callbacks, recentLines);
                    if (!string.IsNullOrWhiteSpace(emittedLine))
                    {
                        await TryHandlePromptAsync(
                            process,
                            callbacks,
                            emittedLine,
                            promptLock,
                            getLastPromptKey,
                            setLastPromptKey,
                            getLastPromptAtUtc,
                            setLastPromptAtUtc,
                            cancellationToken);
                    }
                    continue;
                }

                if (ch == '\n')
                {
                    string? emittedLine = EmitLine(lineBuilder, isError, callbacks, recentLines);
                    if (!string.IsNullOrWhiteSpace(emittedLine))
                    {
                        await TryHandlePromptAsync(
                            process,
                            callbacks,
                            emittedLine,
                            promptLock,
                            getLastPromptKey,
                            setLastPromptKey,
                            getLastPromptAtUtc,
                            setLastPromptAtUtc,
                            cancellationToken);
                    }
                    continue;
                }

                lineBuilder.Append(ch);
            }
        }

        string? finalLine = EmitLine(lineBuilder, isError, callbacks, recentLines);
        if (!string.IsNullOrWhiteSpace(finalLine))
        {
            await TryHandlePromptAsync(
                process,
                callbacks,
                finalLine,
                promptLock,
                getLastPromptKey,
                setLastPromptKey,
                getLastPromptAtUtc,
                setLastPromptAtUtc,
                cancellationToken);
        }
    }

    private static string? EmitLine(
        StringBuilder lineBuilder,
        bool isError,
        DepotInstallCallbacks? callbacks,
        List<string> recentLines)
    {
        if (lineBuilder.Length == 0)
            return null;

        string line = lineBuilder.ToString().Trim();
        lineBuilder.Clear();

        if (string.IsNullOrWhiteSpace(line))
            return null;

        if (isError)
            line = "[stderr] " + line;

        callbacks?.OnOutput?.Invoke(line);

        foreach (Match match in PercentRegex.Matches(line))
        {
            if (!double.TryParse(match.Groups[1].Value, out double percent))
                continue;

            percent = Math.Clamp(percent, 0, 100);
            callbacks?.OnProgress?.Invoke(percent);
        }

        if (!line.StartsWith("Pre-allocating ", StringComparison.OrdinalIgnoreCase))
        {
            lock (recentLines)
            {
                recentLines.Add(line);
                if (recentLines.Count > 20)
                    recentLines.RemoveAt(0);
            }
        }

        return line;
    }

    private static async Task TryHandlePromptAsync(
        Process process,
        DepotInstallCallbacks? callbacks,
        string lineText,
        SemaphoreSlim promptLock,
        Func<string> getLastPromptKey,
        Action<string> setLastPromptKey,
        Func<DateTime> getLastPromptAtUtc,
        Action<DateTime> setLastPromptAtUtc,
        CancellationToken cancellationToken)
    {
        if (callbacks?.RequestInputAsync == null)
            return;

        if (!TryClassifyPrompt(lineText, out string promptMessage, out bool isSecret))
            return;

        string promptKey = promptMessage.Trim().ToLowerInvariant();
        DateTime now = DateTime.UtcNow;

        if (promptKey == getLastPromptKey() &&
            (now - getLastPromptAtUtc()).TotalSeconds < 2)
        {
            return;
        }

        await promptLock.WaitAsync(cancellationToken);
        try
        {
            if (promptKey == getLastPromptKey() &&
                (DateTime.UtcNow - getLastPromptAtUtc()).TotalSeconds < 2)
            {
                return;
            }

            setLastPromptKey(promptKey);
            setLastPromptAtUtc(DateTime.UtcNow);

            callbacks.OnStatus?.Invoke(promptMessage);

            string? response = await callbacks.RequestInputAsync(new DepotInstallPromptRequest
            {
                Prompt = promptMessage,
                IsSecret = isSecret
            });

            if (string.IsNullOrWhiteSpace(response))
                throw new OperationCanceledException("Authentication prompt was cancelled.");

            await process.StandardInput.WriteLineAsync(response);
            await process.StandardInput.FlushAsync();
            callbacks.OnOutput?.Invoke("[input submitted]");
            callbacks.OnStatus?.Invoke("Input submitted. Waiting for DepotDownloader...");
        }
        finally
        {
            promptLock.Release();
        }
    }

    private static bool TryClassifyPrompt(string rawText, out string promptMessage, out bool isSecret)
    {
        string lower = rawText.Trim().ToLowerInvariant();

        // Mobile-app confirmation is informational and does not require input.
        if (lower.Contains("use the steam mobile app to confirm your sign in", StringComparison.Ordinal))
        {
            promptMessage = "";
            isSecret = false;
            return false;
        }

        foreach (string marker in PromptMarkers)
        {
            if (!lower.Contains(marker))
                continue;

            isSecret = marker.Contains("password", StringComparison.Ordinal);

            if (marker.Contains("steam guard", StringComparison.Ordinal) ||
                marker.Contains("two-factor", StringComparison.Ordinal) ||
                marker.Contains("auth code", StringComparison.Ordinal) ||
                marker.Contains("authentication code", StringComparison.Ordinal) ||
                marker.Contains("email code", StringComparison.Ordinal) ||
                marker.Contains("device code", StringComparison.Ordinal))
            {
                promptMessage = "Steam authentication code required. Enter the code to continue.";
                return true;
            }

            if (marker.Contains("password", StringComparison.Ordinal))
            {
                promptMessage = "Steam password input required by DepotDownloader. Enter password to continue.";
                return true;
            }
        }

        promptMessage = "";
        isSecret = false;
        return false;
    }

    private static void ValidateBasicAuth(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.");
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.");
    }

    private static void ValidateAuthOptions(DepotInstallAuthOptions auth)
    {
        if (string.IsNullOrWhiteSpace(auth.Username))
            throw new ArgumentException("Username is required.");

        if (!auth.UseRememberedLoginOnly && string.IsNullOrWhiteSpace(auth.Password))
            throw new ArgumentException("Password is required unless using remembered login.");
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "Failed to stop DepotDownloader process.");
        }
    }

    private static void CleanupDepotDownloaderFolders(string installDir)
    {
        string hiddenDepotDir = Path.Combine(installDir, ".DepotDownloader");
        if (Directory.Exists(hiddenDepotDir))
            Directory.Delete(hiddenDepotDir, true);

        foreach (var depotFolder in Directory.GetDirectories(installDir, "depot_*"))
        {
            foreach (var entry in Directory.GetFileSystemEntries(depotFolder))
            {
                string targetPath = Path.Combine(installDir, Path.GetFileName(entry));

                if (Directory.Exists(entry))
                {
                    if (!Directory.Exists(targetPath))
                        Directory.Move(entry, targetPath);
                }
                else
                {
                    File.Move(entry, targetPath, true);
                }
            }

            Directory.Delete(depotFolder, true);
        }
    }

    private static void WriteVersionInfo(
        GameVersionInstallDefinition version,
        string installDir,
        string infoFileName,
        string launcherMarker)
    {
        string infoPath = Path.Combine(installDir, infoFileName);

        File.WriteAllText(infoPath,
$@"{launcherMarker}=true
DisplayName={version.DisplayName}
FolderName={version.Id}
OriginalDownload={version.Id}
Manifest={version.ManifestId}
");
    }
}
