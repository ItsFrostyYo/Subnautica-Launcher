using System.Diagnostics;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.Installer;

internal sealed record DepotInstallCredentials(string Username, string Password, string InstallCommonPath, bool RememberPassword, bool PreferTwoFactorCode);

internal static class DepotDownloaderInstallService
{
    internal const string AuthenticationAcceptedResponse = "\u0001STEAM_AUTHENTICATION_ACCEPTED";
    private const string ProcessExitedResponse = "\u0001DEPOT_DOWNLOADER_EXITED";
    private const string DownloadUrl = "https://github.com/SteamRE/DepotDownloader/releases/latest/download/DepotDownloader-windows-x64.zip";
    private static readonly TimeSpan StaleToolAge = TimeSpan.FromDays(90);
    private static readonly Regex Percent = new(@"(?<!\d)(\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled);
    private static string ToolRoot => Path.Combine(AppPaths.BasePath, "Dependencies", "DepotDownloader");
    private static string Executable => Path.Combine(ToolRoot, "DepotDownloader.exe");

    private static readonly string[] CodePromptMarkers =
    [
        "enter steam guard code", "enter your steam guard code", "steam guard code required",
        "enter two-factor code", "enter two factor code", "enter your 2 factor auth code",
        "2 factor auth code from your authenticator app", "enter authentication code",
        "enter auth code", "enter email code", "auth code sent to the email",
        "authentication code sent to your email", "check your email for a code", "code from your email", "code sent to your email",
        "enter the code from your email", "enter the code sent to your email",
        "enter security code", "steam guard code", "enter device code"
    ];
    private static readonly string[] PasswordPromptMarkers =
    [
        "enter account password", "enter your password", "please enter your password", "password required", "password:"
    ];
    private static readonly string[] InfoPromptMarkers =
    [
        "this account is protected by steam guard", "use the steam mobile app to confirm your sign in",
        "confirm your sign in using the steam mobile app", "confirm your sign in from the steam mobile app",
        "steam mobile app to confirm your sign in", "steam guard", "check your email"
    ];

    public static async Task InstallAsync(GameVersionDefinition version, DepotInstallCredentials credentials, Action<string> status, Action<string> output, Action<double> progress, Func<string, bool, bool, Task<string?>> requestInput, CancellationToken cancellationToken)
    {
        await EnsureToolAsync(status, cancellationToken);
        string destination = Path.Combine(credentials.InstallCommonPath, version.Id);
        if (File.Exists(Path.Combine(destination, "Subnautica.exe")))
            throw new InvalidOperationException($"'{version.Id}' already exists in the selected Steam library.");

        Directory.CreateDirectory(destination);
        int loginId = CreateUniqueLoginId();
        var startInfo = new ProcessStartInfo { FileName = Executable, WorkingDirectory = ToolRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        startInfo.ArgumentList.Add("-app"); startInfo.ArgumentList.Add(GameVersionRegistry.Subnautica.SteamAppId.ToString());
        startInfo.ArgumentList.Add("-depot"); startInfo.ArgumentList.Add(GameVersionRegistry.Subnautica.SteamDepotId.ToString());
        startInfo.ArgumentList.Add("-manifest"); startInfo.ArgumentList.Add(version.ManifestId.ToString());
        startInfo.ArgumentList.Add("-username"); startInfo.ArgumentList.Add(credentials.Username);
        if (!string.IsNullOrWhiteSpace(credentials.Password)) { startInfo.ArgumentList.Add("-password"); startInfo.ArgumentList.Add(credentials.Password); }
        if (credentials.RememberPassword) startInfo.ArgumentList.Add("-remember-password");
        if (credentials.PreferTwoFactorCode) startInfo.ArgumentList.Add("-no-mobile");
        startInfo.ArgumentList.Add("-loginid"); startInfo.ArgumentList.Add(loginId.ToString());
        startInfo.ArgumentList.Add("-dir"); startInfo.ArgumentList.Add(destination);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("DepotDownloader could not be started.");
        using var cancellationRegistration = cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var promptGate = new SemaphoreSlim(1, 1);
        string lastPrompt = string.Empty;
        DateTime lastPromptUtc = DateTime.MinValue;
        DateTime lastOutputUtc = DateTime.UtcNow;
        bool steamLoginPending = false;
        bool fallbackPromptShown = false;
        output($"DepotDownloader started (PID {process.Id}).");
        output($"Using Steam LoginID {loginId} for this install session.");
        status("DepotDownloader started.");
        async Task ReadAsync(StreamReader reader, bool isError)
        {
            var buffer = new char[256];
            var pending = new System.Text.StringBuilder();
            while (true)
            {
                int count = await reader.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                for (int index = 0; index < count; index++)
                {
                    char character = buffer[index];
                    if (character is '\r' or '\n')
                    {
                        if (pending.Length == 0) continue;
                        string line = pending.ToString().Trim();
                        pending.Clear();
                        if (!string.IsNullOrWhiteSpace(line)) await HandleLineAsync(isError ? "[stderr] " + line : line);
                        continue;
                    }

                    pending.Append(character);
                    if (!char.IsControl(character) && ShouldInspectPartialPrompt(pending, character))
                        await TryHandlePromptAsync(pending.ToString().Trim());
                }
            }
            if (pending.Length > 0)
            {
                string line = pending.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(line)) await HandleLineAsync(isError ? "[stderr] " + line : line);
            }
        }
        async Task HandleLineAsync(string line)
        {
            Match match = Percent.Match(line);
            if (match.Success && double.TryParse(match.Groups[1].Value, out double value)) progress(Math.Clamp(value, 0, 100));
            output(line);
            string lower = line.ToLowerInvariant();
            lastOutputUtc = DateTime.UtcNow;
            if (lower.Contains("logging ") && lower.Contains("into steam3"))
            {
                steamLoginPending = true;
                fallbackPromptShown = false;
            }
            else if (lower.Contains("connected to steam3") || lower.Contains("done!") || lower.Contains("got appinfo") || lower.Contains("got depot key") || lower.Contains("downloaded manifest"))
            {
                steamLoginPending = false;
                fallbackPromptShown = false;
            }
            await TryHandlePromptAsync(line);
        }
        async Task TryHandlePromptAsync(string line)
        {
            if (!TryClassifyPrompt(line, out string prompt, out bool secret, out bool requiresInput)) return;
            string promptKey = prompt.Trim().ToLowerInvariant();
            if (lastPrompt == promptKey && DateTime.UtcNow - lastPromptUtc < TimeSpan.FromSeconds(2)) return;
            await promptGate.WaitAsync(cancellationToken);
            try
            {
                if (lastPrompt == promptKey && DateTime.UtcNow - lastPromptUtc < TimeSpan.FromSeconds(2)) return;
                lastPrompt = promptKey; lastPromptUtc = DateTime.UtcNow;
                output("[auth] " + prompt);
                status(prompt);
                if (!requiresInput) return;
                string? answer = await RequestInputOrProcessExitAsync(prompt, secret, false);
                if (string.Equals(answer, ProcessExitedResponse, StringComparison.Ordinal)) return;
                if (string.Equals(answer, AuthenticationAcceptedResponse, StringComparison.Ordinal))
                {
                    status("Steam authentication accepted.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(answer)) throw new OperationCanceledException("Steam authentication was cancelled.");
                await process.StandardInput.WriteLineAsync(answer.Trim());
                await process.StandardInput.FlushAsync();
                output("[input submitted]");
                status("Input submitted. Waiting for DepotDownloader...");
            }
            finally { promptGate.Release(); }
        }
        async Task MonitorLoginAsync()
        {
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (!steamLoginPending || fallbackPromptShown || DateTime.UtcNow - lastOutputUtc < TimeSpan.FromSeconds(8)) continue;
                const string promptKey = "steam login stalled waiting for authentication";
                if (lastPrompt == promptKey && DateTime.UtcNow - lastPromptUtc < TimeSpan.FromSeconds(10)) continue;
                await promptGate.WaitAsync(cancellationToken);
                try
                {
                    if (process.HasExited || !steamLoginPending || fallbackPromptShown) continue;
                    fallbackPromptShown = true;
                    lastPrompt = promptKey;
                    lastPromptUtc = DateTime.UtcNow;
                    const string prompt = "Steam login appears to be waiting for authentication. Enter a Steam Guard, email, or authenticator code if Steam asked for one. If Steam is asking for mobile approval instead, approve it in the Steam app and then press Continue Waiting here.";
                    status(prompt);
                    output("[auth] Login stalled at Steam3. Offering fallback authentication prompt.");
                    string? answer = await RequestInputOrProcessExitAsync(prompt, false, true);
                    if (string.Equals(answer, ProcessExitedResponse, StringComparison.Ordinal)) return;
                    if (string.Equals(answer, AuthenticationAcceptedResponse, StringComparison.Ordinal))
                    {
                        status("Steam authentication accepted.");
                    }
                    else if (!string.IsNullOrWhiteSpace(answer))
                    {
                        await process.StandardInput.WriteLineAsync(answer.Trim());
                        await process.StandardInput.FlushAsync();
                        output("[input submitted]");
                        status("Fallback authentication input submitted. Waiting for DepotDownloader...");
                        lastOutputUtc = DateTime.UtcNow;
                    }
                }
                finally { promptGate.Release(); }
            }
        }
        async Task<string?> RequestInputOrProcessExitAsync(string prompt, bool secret, bool allowWaiting)
        {
            Task<string?> inputTask = requestInput(prompt, secret, allowWaiting);
            Task exitTask = process.WaitForExitAsync(cancellationToken);
            Task completed = await Task.WhenAny(inputTask, exitTask);
            return completed == inputTask ? await inputTask : ProcessExitedResponse;
        }
        await Task.WhenAll(ReadAsync(process.StandardOutput, false), ReadAsync(process.StandardError, true), MonitorLoginAsync(), process.WaitForExitAsync(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException("DepotDownloader failed. Check the install output and Steam Guard requirements.");
        if (!File.Exists(Path.Combine(destination, "Subnautica.exe"))) throw new InvalidOperationException("DepotDownloader finished without Subnautica.exe.");
        CleanupInstall(destination);
    }

    private static bool TryClassifyPrompt(string rawText, out string prompt, out bool secret, out bool requiresInput)
    {
        string lower = rawText.Trim().ToLowerInvariant();
        if (CodePromptMarkers.Any(lower.Contains))
        {
            prompt = "Steam authentication code required. Enter the code from Steam Guard, your email, or your authenticator app to continue.";
            secret = false;
            requiresInput = true;
            return true;
        }
        if (PasswordPromptMarkers.Any(lower.Contains))
        {
            prompt = "Steam password input required by DepotDownloader. Enter your Steam password to continue.";
            secret = true;
            requiresInput = true;
            return true;
        }
        if (InfoPromptMarkers.Any(lower.Contains))
        {
            prompt = lower.Contains("mobile app")
                ? "Steam mobile confirmation required. Approve the sign-in in the Steam mobile app. If Steam sends an email or authenticator code instead, enter that code when prompted."
                : lower.Contains("check your email")
                    ? "Steam may have sent a verification code to your email. Check your email for the code and enter it when prompted."
                    : "Steam Guard protection detected. A Steam Guard, email, or authenticator code may be required to finish logging in.";
            secret = false;
            requiresInput = false;
            return true;
        }
        prompt = string.Empty;
        secret = false;
        requiresInput = false;
        return false;
    }

    private static bool ShouldInspectPartialPrompt(System.Text.StringBuilder line, char latestCharacter)
    {
        if (line.Length is < 8 or > 220) return false;
        return latestCharacter is ':' or '?' or '>' or ')' || line.Length % 12 == 0;
    }

    private static int CreateUniqueLoginId()
    {
        int loginId = ((Environment.ProcessId & 0x7FFF) << 16) ^ (Environment.TickCount & 0x3FFFFFFF);
        return loginId == 0 ? 1 : Math.Abs(loginId);
    }

    private static void CleanupInstall(string installDirectory)
    {
        string metadata = Path.Combine(installDirectory, ".DepotDownloader");
        if (Directory.Exists(metadata)) Directory.Delete(metadata, true);
        foreach (string depotFolder in Directory.GetDirectories(installDirectory, "depot_*"))
        {
            foreach (string entry in Directory.GetFileSystemEntries(depotFolder))
            {
                string destination = Path.Combine(installDirectory, Path.GetFileName(entry));
                if (Directory.Exists(entry)) { if (!Directory.Exists(destination)) Directory.Move(entry, destination); }
                else File.Move(entry, destination, true);
            }
            Directory.Delete(depotFolder, true);
        }
    }

    private static async Task EnsureToolAsync(Action<string> status, CancellationToken cancellationToken)
    {
        if (File.Exists(Executable) && DateTime.UtcNow - File.GetLastWriteTimeUtc(Executable) <= StaleToolAge) return;
        status(File.Exists(Executable) ? "Refreshing DepotDownloader..." : "Downloading DepotDownloader...");
        Directory.CreateDirectory(ToolRoot);
        string zip = Path.Combine(ToolRoot, "DepotDownloader.zip");
        try
        {
            if (File.Exists(zip)) File.Delete(zip);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");
            await File.WriteAllBytesAsync(zip, await client.GetByteArrayAsync(DownloadUrl, cancellationToken), cancellationToken);
            ZipFile.ExtractToDirectory(zip, ToolRoot, true);
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
        }
        if (!File.Exists(Executable)) throw new FileNotFoundException("DepotDownloader.exe was missing after extraction.", Executable);
    }
}
