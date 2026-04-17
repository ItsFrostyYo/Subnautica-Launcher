using SubnauticaLauncher.Core;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    private static readonly string[] CodePromptMarkers =
    {
        "enter steam guard code",
        "enter your steam guard code",
        "enter two-factor code",
        "enter two factor code",
        "enter your 2 factor auth code",
        "2 factor auth code from your authenticator app",
        "enter authentication code",
        "enter auth code",
        "enter email code",
        "auth code sent to the email",
        "authentication code sent to your email",
        "check your email for a code",
        "code from your email",
        "code sent to your email",
        "enter the code from your email",
        "enter the code sent to your email",
        "enter security code",
        "steam guard code",
        "enter device code"
    };

    private static readonly string[] PasswordPromptMarkers =
    {
        "enter account password",
        "enter your password",
        "please enter your password",
        "password:"
    };

    private static readonly string[] InfoPromptMarkers =
    {
        "this account is protected by steam guard",
        "use the steam mobile app to confirm your sign in",
        "confirm your sign in using the steam mobile app",
        "confirm your sign in from the steam mobile app",
        "steam mobile app to confirm your sign in",
        "steam guard",
        "check your email"
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
        EnsureLaunchSupportFiles(version, installDir);
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
        int loginId = CreateUniqueLoginId();

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

        psi.ArgumentList.Add("-loginid");
        psi.ArgumentList.Add(loginId.ToString());
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
        callbacks?.OnOutput?.Invoke($"Using Steam LoginID {loginId} for this install session.");
        callbacks?.OnStatus?.Invoke("DepotDownloader started.");

        using var cancelRegistration = cancellationToken.Register(() =>
        {
            TryKillProcess(process);
        });

        var recentLines = new List<string>();
        string lastPromptKey = "";
        DateTime lastPromptAtUtc = DateTime.MinValue;
        DateTime lastOutputAtUtc = DateTime.UtcNow;
        bool steamLoginPending = false;
        bool fallbackPromptShown = false;
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
            () => lastOutputAtUtc,
            value => lastOutputAtUtc = value,
            () => steamLoginPending,
            value => steamLoginPending = value,
            () => fallbackPromptShown,
            value => fallbackPromptShown = value,
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
            () => lastOutputAtUtc,
            value => lastOutputAtUtc = value,
            () => steamLoginPending,
            value => steamLoginPending = value,
            () => fallbackPromptShown,
            value => fallbackPromptShown = value,
            cancellationToken);

        Task loginMonitorTask = MonitorSteamLoginStallAsync(
            process,
            callbacks,
            promptLock,
            () => lastPromptKey,
            value => lastPromptKey = value,
            () => lastPromptAtUtc,
            value => lastPromptAtUtc = value,
            () => lastOutputAtUtc,
            value => lastOutputAtUtc = value,
            () => steamLoginPending,
            value => steamLoginPending = value,
            () => fallbackPromptShown,
            value => fallbackPromptShown = value,
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

        await Task.WhenAll(outputTask, errorTask, loginMonitorTask);

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
        EnsureLaunchSupportFiles(version, installDir);
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
        Func<DateTime> getLastOutputAtUtc,
        Action<DateTime> setLastOutputAtUtc,
        Func<bool> getSteamLoginPending,
        Action<bool> setSteamLoginPending,
        Func<bool> getFallbackPromptShown,
        Action<bool> setFallbackPromptShown,
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
                        setLastOutputAtUtc(DateTime.UtcNow);
                        UpdateSteamLoginState(emittedLine, getSteamLoginPending, setSteamLoginPending, setFallbackPromptShown);
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
                        setLastOutputAtUtc(DateTime.UtcNow);
                        UpdateSteamLoginState(emittedLine, getSteamLoginPending, setSteamLoginPending, setFallbackPromptShown);
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

                if (!char.IsControl(ch) && ShouldInspectPartialPrompt(lineBuilder, ch))
                {
                    string partialLine = lineBuilder.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(partialLine))
                    {
                        await TryHandlePromptAsync(
                            process,
                            callbacks,
                            partialLine,
                            promptLock,
                            getLastPromptKey,
                            setLastPromptKey,
                            getLastPromptAtUtc,
                            setLastPromptAtUtc,
                            cancellationToken);
                    }
                }
            }
        }

        string? finalLine = EmitLine(lineBuilder, isError, callbacks, recentLines);
        if (!string.IsNullOrWhiteSpace(finalLine))
        {
            setLastOutputAtUtc(DateTime.UtcNow);
            UpdateSteamLoginState(finalLine, getSteamLoginPending, setSteamLoginPending, setFallbackPromptShown);
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

        if (!TryClassifyPrompt(lineText, out string promptMessage, out bool isSecret, out bool requiresInput))
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

            callbacks.OnOutput?.Invoke("[auth] " + promptMessage);
            callbacks.OnStatus?.Invoke(promptMessage);

            if (!requiresInput)
                return;

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

    private static bool TryClassifyPrompt(
        string rawText,
        out string promptMessage,
        out bool isSecret,
        out bool requiresInput)
    {
        string lower = rawText.Trim().ToLowerInvariant();

        foreach (string marker in CodePromptMarkers)
        {
            if (!lower.Contains(marker))
                continue;

            promptMessage = "Steam authentication code required. Enter the code from Steam Guard, your email, or your authenticator app to continue.";
            isSecret = false;
            requiresInput = true;
            return true;
        }

        foreach (string marker in PasswordPromptMarkers)
        {
            if (!lower.Contains(marker))
                continue;

            promptMessage = "Steam password input required by DepotDownloader. Enter your Steam password to continue.";
            isSecret = true;
            requiresInput = true;
            return true;
        }

        foreach (string marker in InfoPromptMarkers)
        {
            if (!lower.Contains(marker))
                continue;

            if (lower.Contains("mobile app", StringComparison.Ordinal))
            {
                promptMessage = "Steam mobile confirmation required. Approve the sign-in in the Steam mobile app. If Steam sends an email or authenticator code instead, enter that code when prompted.";
            }
            else if (lower.Contains("check your email", StringComparison.Ordinal))
            {
                promptMessage = "Steam may have sent a verification code to your email. Check your email for the code and enter it when prompted.";
            }
            else
            {
                promptMessage = "Steam Guard protection detected. A Steam Guard, email, or authenticator code may be required to finish logging in.";
            }

            isSecret = false;
            requiresInput = false;
            return true;
        }

        promptMessage = "";
        isSecret = false;
        requiresInput = false;
        return false;
    }

    private static void UpdateSteamLoginState(
        string line,
        Func<bool> getSteamLoginPending,
        Action<bool> setSteamLoginPending,
        Action<bool> setFallbackPromptShown)
    {
        string lower = line.Trim().ToLowerInvariant();

        if (lower.Contains("logging ", StringComparison.Ordinal) &&
            lower.Contains("into steam3", StringComparison.Ordinal))
        {
            if (!getSteamLoginPending())
                setSteamLoginPending(true);

            setFallbackPromptShown(false);
            return;
        }

        if (lower.Contains("connected to steam3", StringComparison.Ordinal) ||
            lower.Contains("done!", StringComparison.Ordinal) ||
            lower.Contains("got appinfo", StringComparison.Ordinal) ||
            lower.Contains("got depot key", StringComparison.Ordinal) ||
            lower.Contains("downloaded manifest", StringComparison.Ordinal))
        {
            setSteamLoginPending(false);
            setFallbackPromptShown(false);
        }
    }

    private static async Task MonitorSteamLoginStallAsync(
        Process process,
        DepotInstallCallbacks? callbacks,
        SemaphoreSlim promptLock,
        Func<string> getLastPromptKey,
        Action<string> setLastPromptKey,
        Func<DateTime> getLastPromptAtUtc,
        Action<DateTime> setLastPromptAtUtc,
        Func<DateTime> getLastOutputAtUtc,
        Action<DateTime> setLastOutputAtUtc,
        Func<bool> getSteamLoginPending,
        Action<bool> setSteamLoginPending,
        Func<bool> getFallbackPromptShown,
        Action<bool> setFallbackPromptShown,
        CancellationToken cancellationToken)
    {
        if (callbacks?.RequestInputAsync == null)
            return;

        while (!cancellationToken.IsCancellationRequested && !process.HasExited)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            if (!getSteamLoginPending() || getFallbackPromptShown())
                continue;

            if ((DateTime.UtcNow - getLastOutputAtUtc()).TotalSeconds < 8)
                continue;

            string promptKey = "steam login stalled waiting for authentication";
            if (promptKey == getLastPromptKey() &&
                (DateTime.UtcNow - getLastPromptAtUtc()).TotalSeconds < 10)
            {
                continue;
            }

            await promptLock.WaitAsync(cancellationToken);
            try
            {
                if (process.HasExited)
                    return;

                if (!getSteamLoginPending() || getFallbackPromptShown())
                    continue;

                setFallbackPromptShown(true);
                setLastPromptKey(promptKey);
                setLastPromptAtUtc(DateTime.UtcNow);

                string promptMessage =
                    "Steam login appears to be waiting for authentication. Enter a Steam Guard, email, or authenticator code if Steam asked for one. If Steam is asking for mobile approval instead, approve it in the Steam app and then press Cancel Prompt here.";

                callbacks.OnOutput?.Invoke("[auth] Login stalled at Steam3. Offering fallback authentication prompt.");
                callbacks.OnStatus?.Invoke(promptMessage);

                string? response = await callbacks.RequestInputAsync(new DepotInstallPromptRequest
                {
                    Prompt = promptMessage,
                    IsSecret = false
                });

                if (!string.IsNullOrWhiteSpace(response))
                {
                    await process.StandardInput.WriteLineAsync(response.Trim());
                    await process.StandardInput.FlushAsync();
                    callbacks.OnOutput?.Invoke("[input submitted]");
                    callbacks.OnStatus?.Invoke("Fallback authentication input submitted. Waiting for DepotDownloader...");
                    setLastOutputAtUtc(DateTime.UtcNow);
                }
            }
            finally
            {
                promptLock.Release();
            }
        }
    }

    private static bool ShouldInspectPartialPrompt(StringBuilder lineBuilder, char latestChar)
    {
        if (lineBuilder.Length < 8 || lineBuilder.Length > 220)
            return false;

        if (latestChar == ':' || latestChar == '?' || latestChar == '>' || latestChar == ')')
            return true;

        return lineBuilder.Length % 12 == 0;
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

    private static int CreateUniqueLoginId()
    {
        int processId = Environment.ProcessId;
        int tickSeed = Environment.TickCount & 0x3FFFFFFF;
        int loginId = ((processId & 0x7FFF) << 16) ^ tickSeed;
        return loginId == 0 ? 1 : Math.Abs(loginId);
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

    private static void EnsureLaunchSupportFiles(
        GameVersionInstallDefinition version,
        string installDir)
    {
        LauncherGameProfiles.GetBySteamAppId(version.SteamAppId).EnsureSteamAppIdFile(installDir);
    }

    private static void WriteVersionInfo(
        GameVersionInstallDefinition version,
        string installDir,
        string infoFileName,
        string launcherMarker)
    {
        LauncherGameProfile profile = LauncherGameProfiles.All.FirstOrDefault(p =>
            string.Equals(p.InfoFileName, infoFileName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.LauncherMarker, launcherMarker, StringComparison.Ordinal))
            ?? LauncherGameProfiles.GetBySteamAppId(version.SteamAppId);

        InstalledVersionFileService.WriteInfoFile(
            installDir,
            profile,
            InstalledVersionNaming.BuildInstalledDisplayName(version.Id, version.DisplayName),
            version.Id,
            version.Id,
            isModded: false,
            installedModId: string.Empty,
            manifestId: version.ManifestId);
    }
}
