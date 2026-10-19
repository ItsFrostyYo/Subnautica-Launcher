using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Services;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.UI;

internal static class ImGuiDepotInstallFlow
{
    private static readonly Regex PercentRegex = new(@"(?<!\d)(\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled);
    internal sealed record LoginResult(DepotInstallCredentials? Credentials, bool GoBack);
    private sealed record PendingPrompt(TaskCompletionSource<string?> Completion);

    public static async Task<LoginResult> PromptLoginAsync(IntPtr ownerHandle)
    {
        LauncherConfig config = LauncherConfig.Load();
        IReadOnlyList<string> libraries = SteamLibraryDiscovery.FindCommonPaths();
        using Process process = StartHost(ownerHandle, "login", startInfo =>
        {
            Add(startInfo, "--username", config.DepotDownloaderUsername);
            Add(startInfo, "--libraries", string.Join('\n', libraries));
            Add(startInfo, "--selected-library", config.DepotDownloaderInstallCommonPath);
            Add(startInfo, "--use-remembered", config.DepotDownloaderUseRememberedLogin ? "1" : "0");
            Add(startInfo, "--remembered-seeded", config.DepotDownloaderRememberedLoginSeeded ? "1" : "0");
            Add(startInfo, "--remember-password", config.DepotDownloaderRememberPassword ? "1" : "0");
            Add(startInfo, "--prefer-code", config.DepotDownloaderPreferTwoFactorCode ? "1" : "0");
        }, redirectInput: false);

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        string? result = LastProtocolLine(output);
        if (result == "BACK") return new LoginResult(null, true);
        if (result is null || !result.StartsWith("LOGIN|", StringComparison.Ordinal)) return new LoginResult(null, false);

        string[] values = result.Split('|');
        if (values.Length != 7) return new LoginResult(null, false);
        string username = Decode(values[1]).Trim();
        string password = Decode(values[2]);
        string library = Decode(values[3]);
        bool rememberPassword = values[4] == "1";
        bool preferCode = values[5] == "1";
        bool useRemembered = values[6] == "1";

        config.DepotDownloaderUsername = username;
        config.DepotDownloaderInstallCommonPath = library;
        config.DepotDownloaderRememberPassword = !useRemembered && rememberPassword;
        config.DepotDownloaderPreferTwoFactorCode = preferCode;
        config.DepotDownloaderUseRememberedLogin = useRemembered;
        config.Save();
        return new LoginResult(new DepotInstallCredentials(username, password, library, rememberPassword, preferCode), false);
    }

    public static async Task<LauncherVersion?> InstallAsync(IntPtr ownerHandle, SubnauticaVersionDefinition definition, DepotInstallCredentials credentials, LauncherVersionStore store)
    {
        using Process host = StartHost(ownerHandle, "progress", startInfo => Add(startInfo, "--install-name", definition.DisplayName), redirectInput: true);
        using var cancellation = new CancellationTokenSource();
        var pendingPrompts = new ConcurrentDictionary<string, PendingPrompt>();
        object writerLock = new();
        object logFilterLock = new();
        int nextPromptId = 0;
        bool installFinished = false;
        bool installAttemptStarted = false;
        int preallocationCount = 0;
        double lastLoggedProgress = -1;
        DateTime lastLoggedProgressUtc = DateTime.MinValue;
        double lastProgressUiValue = -1;
        DateTime lastProgressUiUtc = DateTime.MinValue;
        var etaEstimator = new DownloadEtaEstimator();
        double highestProgress = 0;
        string lastLoggedLine = string.Empty;

        void Send(string line)
        {
            lock (writerLock)
            {
                if (host.HasExited) return;
                host.StandardInput.WriteLine(line);
                host.StandardInput.Flush();
            }
        }

        void ResolveAcceptedAuthentication(string line)
        {
            string lower = line.Trim().ToLowerInvariant();
            if (!lower.Contains("connected to steam3") && !lower.Contains("got appinfo") &&
                !lower.Contains("got depot key") && !lower.Contains("downloaded manifest") &&
                !lower.Contains("done!")) return;

            bool resolvedPrompt = false;
            foreach ((string id, PendingPrompt prompt) in pendingPrompts.ToArray())
            {
                if (!pendingPrompts.TryRemove(id, out PendingPrompt? accepted)) continue;
                accepted.Completion.TrySetResult(DepotDownloaderInstallService.AuthenticationAcceptedResponse);
                Send("DISMISS|" + id);
                resolvedPrompt = true;
            }
            if (resolvedPrompt) Send("STATUS|" + Encode("Steam authentication accepted. Preparing download..."));
        }

        void ForwardOutput(string line)
        {
            ResolveAcceptedAuthentication(line);
            if (string.IsNullOrWhiteSpace(line)) return;

            lock (logFilterLock)
            {
                if (line.StartsWith("Pre-allocating ", StringComparison.OrdinalIgnoreCase))
                {
                    int count = ++preallocationCount;
                    if (count % 500 != 0) return;
                    line = $"Pre-allocating files... {count:N0}";
                    Send("STATUS|" + Encode(line));
                }

                bool isError = line.StartsWith("[stderr]", StringComparison.OrdinalIgnoreCase);
                if (!isError && PercentRegex.Match(line) is { Success: true } match &&
                    double.TryParse(match.Groups[1].Value, out double percent))
                {
                    DateTime now = DateTime.UtcNow;
                    if (Math.Abs(percent - lastLoggedProgress) < 1.0 && now - lastLoggedProgressUtc < TimeSpan.FromSeconds(2)) return;
                    lastLoggedProgress = percent;
                    lastLoggedProgressUtc = now;
                    line = $"Download progress: {Math.Clamp(percent, 0, 100):0.00}%";
                }

                if (!isError && line.Length > 240) line = line[..237] + "...";
                if (string.Equals(line, lastLoggedLine, StringComparison.Ordinal)) return;
                lastLoggedLine = line;
            }

            Send("LOG|" + Encode(line));
        }

        void ForwardProgress(double value)
        {
            double clamped = Math.Max(highestProgress, Math.Clamp(value, 0, 100));
            highestProgress = clamped;
            DateTime now = DateTime.UtcNow;
            string? etaText;
            lock (logFilterLock)
            {
                if (Math.Abs(clamped - lastProgressUiValue) < 0.1 && now - lastProgressUiUtc < TimeSpan.FromMilliseconds(180)) return;
                lastProgressUiValue = clamped;
                lastProgressUiUtc = now;
                etaText = etaEstimator.Update(clamped, now);
            }
            Send("PROGRESS|" + clamped.ToString("0.##", CultureInfo.InvariantCulture));
            Send("STATUS|" + Encode("Downloading game files..."));
            if (etaText is not null) Send("ETA|" + Encode(etaText));
        }

        async Task PumpCommandsAsync()
        {
            while (await host.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line == "CANCEL")
                {
                    cancellation.Cancel();
                    continue;
                }

                string[] values = line.Split('|');
                if (values.Length == 3 && values[0] == "INPUT" && pendingPrompts.TryRemove(values[1], out PendingPrompt? completion))
                    completion.Completion.TrySetResult(Decode(values[2]));
                else if (values.Length == 2 && values[0] == "SKIP" && pendingPrompts.TryRemove(values[1], out PendingPrompt? skipped))
                    skipped.Completion.TrySetResult(null);
            }
        }

        Task commandPump = PumpCommandsAsync();
        _ = host.WaitForExitAsync().ContinueWith(_ =>
        {
            if (!installFinished) cancellation.Cancel();
            foreach (PendingPrompt prompt in pendingPrompts.Values) prompt.Completion.TrySetCanceled();
        }, TaskScheduler.Default);

        async Task<string?> RequestInputAsync(string prompt, bool secret, bool allowWaiting)
        {
            string id = Interlocked.Increment(ref nextPromptId).ToString(CultureInfo.InvariantCulture);
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingPrompts[id] = new PendingPrompt(completion);
            Send($"PROMPT|{id}|{(secret ? 1 : 0)}|{(allowWaiting ? 1 : 0)}|{Encode(prompt)}");
            using CancellationTokenRegistration registration = cancellation.Token.Register(() => completion.TrySetCanceled(cancellation.Token));
            return await completion.Task;
        }

        LauncherVersion? installed = null;
        try
        {
            string generatedDisplayName = LauncherVersionNaming.BuildDisplayName(definition.Id, definition.DisplayName);
            if (store.Load().Any(item =>
                    string.Equals(item.FolderName, definition.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.DisplayName, generatedDisplayName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("That Subnautica version is already managed by this launcher.");

            Send("LOG|" + Encode("Starting install..."));
            installAttemptStarted = true;
            await DepotDownloaderInstallService.InstallAsync(
                definition,
                credentials,
                message => Send("STATUS|" + Encode(message)),
                ForwardOutput,
                ForwardProgress,
                RequestInputAsync,
                cancellation.Token);

            string folder = Path.Combine(credentials.InstallCommonPath, definition.Id);
            var installedVersion = new LauncherVersion
            {
                GameFolder = folder,
                FolderName = definition.Id,
                DisplayName = generatedDisplayName,
                OriginalVersionId = definition.Id,
                OriginalVersionDisplayName = definition.DisplayName
            };
            SteamAppIdFileService.Ensure(folder);
            LauncherConfig installConfig = LauncherConfig.Load();
            if (installConfig.ApplySavedSubnauticaSettingsToNewInstalls && SubnauticaSettingsProfileService.GetProfileInfo().Exists)
            {
                if (SubnauticaSettingsProfileService.TryApplyToNewInstall(folder, out string settingsError))
                    Send("LOG|" + Encode("Applied saved Subnautica settings to the new install."));
                else if (!string.IsNullOrWhiteSpace(settingsError))
                    Send("LOG|" + Encode("Warning: " + settingsError));
            }
            if (!store.TryAdd(installedVersion, out string error)) throw new InvalidOperationException(error);
            LauncherConfig completedConfig = LauncherConfig.Load();
            completedConfig.DepotDownloaderRememberedLoginSeeded |= credentials.RememberPassword;
            completedConfig.DepotDownloaderUseRememberedLogin &= completedConfig.DepotDownloaderRememberedLoginSeeded;
            completedConfig.Save();
            installed = installedVersion;
            installFinished = true;
            Send("LOG|" + Encode("Install completed successfully."));
            Send("COMPLETE|" + Encode("Install complete."));
        }
        catch (OperationCanceledException)
        {
            installFinished = true;
            string cleanupResult = installAttemptStarted
                ? await DeletePartialInstallAsync(credentials.InstallCommonPath, definition.Id)
                : "Install cancelled.";
            Send("LOG|" + Encode(cleanupResult));
            Send("CANCELLED|" + Encode(cleanupResult));
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            installFinished = true;
            string cleanupResult = installAttemptStarted
                ? await DeletePartialInstallAsync(credentials.InstallCommonPath, definition.Id)
                : "Install cancelled.";
            Send("LOG|" + Encode(cleanupResult));
            Send("CANCELLED|" + Encode(cleanupResult));
        }
        catch (Exception exception)
        {
            installFinished = true;
            Send("LOG|" + Encode("Install failed: " + exception.Message));
            Send("FAILED|" + Encode("Install failed. Review the output above for details."));
        }

        if (!host.HasExited) await host.WaitForExitAsync();
        await commandPump;
        return installed;
    }

    private sealed class DownloadEtaEstimator
    {
        private readonly Queue<(DateTime Time, double Percent)> _samples = new();
        private DateTime _lastSampleUtc = DateTime.MinValue;
        private DateTime _nextPublishUtc = DateTime.MinValue;
        private double? _smoothedSeconds;
        private string _lastPublished = string.Empty;

        public string? Update(double percent, DateTime now)
        {
            if (percent >= 100) return null;
            if (_samples.Count > 0 && (now - _lastSampleUtc < TimeSpan.FromSeconds(1) || percent <= _samples.Last().Percent))
                return null;

            _lastSampleUtc = now;
            _samples.Enqueue((now, percent));
            while (_samples.Count > 0 && now - _samples.Peek().Time > TimeSpan.FromSeconds(120))
                _samples.Dequeue();
            if (_samples.Count < 8 || (now - _samples.Peek().Time).TotalSeconds < 12 || percent - _samples.Peek().Percent < 1)
                return null;

            double origin = _samples.Peek().Time.Ticks;
            double meanTime = _samples.Average(sample => (sample.Time.Ticks - origin) / TimeSpan.TicksPerSecond);
            double meanProgress = _samples.Average(sample => sample.Percent);
            double numerator = 0;
            double denominator = 0;
            foreach ((DateTime time, double samplePercent) in _samples)
            {
                double timeOffset = (time.Ticks - origin) / TimeSpan.TicksPerSecond - meanTime;
                numerator += timeOffset * (samplePercent - meanProgress);
                denominator += timeOffset * timeOffset;
            }

            double percentPerSecond = denominator > 0 ? numerator / denominator : 0;
            double rawSeconds = percentPerSecond > 0 ? (100 - percent) / percentPerSecond : double.NaN;
            if (!double.IsFinite(rawSeconds) || rawSeconds <= 0 || rawSeconds > TimeSpan.FromDays(1).TotalSeconds)
                return null;

            if (_smoothedSeconds is null)
                _smoothedSeconds = rawSeconds;
            else
            {
                double bounded = Math.Clamp(rawSeconds, _smoothedSeconds.Value * 0.65, _smoothedSeconds.Value * 1.35);
                _smoothedSeconds = _smoothedSeconds.Value * 0.8 + bounded * 0.2;
            }

            if (now < _nextPublishUtc) return null;
            _nextPublishUtc = now + TimeSpan.FromSeconds(5);
            string formatted = FormatRounded(_smoothedSeconds.Value);
            if (formatted == _lastPublished) return null;
            _lastPublished = formatted;
            return "Estimated time remaining: " + formatted;
        }

        private static string FormatRounded(double seconds)
        {
            double quantum = seconds >= 3600 ? 300 : seconds >= 600 ? 60 : seconds >= 120 ? 30 : seconds >= 60 ? 15 : 5;
            int roundedSeconds = Math.Max((int)quantum, (int)(Math.Round(seconds / quantum) * quantum));
            TimeSpan remaining = TimeSpan.FromSeconds(roundedSeconds);
            return remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                : remaining.TotalMinutes >= 1
                    ? $"{(int)remaining.TotalMinutes}m{(remaining.Seconds > 0 ? $" {remaining.Seconds}s" : string.Empty)}"
                    : $"{remaining.Seconds}s";
        }
    }

    private static async Task<string> DeletePartialInstallAsync(string commonPath, string folderName)
    {
        string common = Path.GetFullPath(commonPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string destination = Path.GetFullPath(Path.Combine(common, folderName));
        if (!string.Equals(Path.GetDirectoryName(destination), common, StringComparison.OrdinalIgnoreCase))
            return "Install cancelled. The partial folder was not removed because its path was invalid.";
        if (!Directory.Exists(destination)) return "Install cancelled. No partial game files remained.";

        Exception? lastError = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(destination, recursive: true);
                return "Install cancelled. Partial game files were removed.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                await Task.Delay(250);
            }
        }

        return "Install cancelled, but the partial folder could not be fully removed: " + lastError?.Message;
    }

    private static Process StartHost(IntPtr ownerHandle, string mode, Action<ProcessStartInfo> configure, bool redirectInput)
    {
        string hostPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "SubnauticaLauncher.ImGuiHost.exe");
        if (!File.Exists(hostPath)) throw new FileNotFoundException("The native Dear ImGui host is missing.", hostPath);
        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = redirectInput,
            CreateNoWindow = true
        };
        Add(startInfo, "--owner", ownerHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
        Add(startInfo, "--mode", mode);
        Add(startInfo, "--background", ResolveBackgroundPath());
        configure(startInfo);
        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start the installer window.");
        ImGuiAddVersionWindow.RegisterHost(process);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => ImGuiAddVersionWindow.UnregisterHost(process);
        return process;
    }

    private static void Add(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value ?? string.Empty);
    }

    private static string ResolveBackgroundPath()
    {
        LauncherConfig config = LauncherConfig.Load();
        if (File.Exists(config.BackgroundPreset)) return config.BackgroundPreset;
        return Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Backgrounds", config.BackgroundPreset + ".png");
    }

    private static string? LastProtocolLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string Decode(string value)
    {
        int padding = value.Length % 4;
        if (padding != 0) value = value.PadRight(value.Length + 4 - padding, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
