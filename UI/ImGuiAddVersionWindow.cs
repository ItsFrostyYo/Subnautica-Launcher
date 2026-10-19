using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows;
using SubnauticaLauncher.Models;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Services;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.UI;

using WpfMessageBox = System.Windows.MessageBox;

/// <summary>
/// Bridges the launcher to the native Dear ImGui add-version host. Keeping the
/// renderer out of the WPF process prevents SDL/Veldrid window behavior from
/// affecting the launcher window or this dialog's native move/resize behavior.
/// </summary>
public static class ImGuiAddVersionWindow
{
    private static readonly ConcurrentDictionary<int, Process> ActiveHosts = new();

    public static void Show(LauncherVersionStore store, IntPtr ownerHandle, Action<LauncherVersion> onAdded)
    {
        _ = RunAsync(store, ownerHandle, version => onAdded(version!), new HostRequest());
    }

    public static void ShowEdit(LauncherVersionStore store, IntPtr ownerHandle, LauncherVersion version, Action<LauncherVersion?> onChanged) =>
        _ = RunAsync(store, ownerHandle, onChanged, new HostRequest { Mode = "edit", EditingVersion = version, Folder = version.GameFolder, DisplayName = version.DisplayName, FolderName = version.FolderName, LaunchOptions = version.LaunchOptions, IconName = version.IconName });

    private static async Task RunAsync(LauncherVersionStore store, IntPtr ownerHandle, Action<LauncherVersion?> onChanged, HostRequest request)
    {
        string hostPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "SubnauticaLauncher.ImGuiHost.exe");
        if (!File.Exists(hostPath))
        {
            WpfMessageBox.Show("The native Dear ImGui Add Version host is missing. Run Scripts\\Build.ps1 to rebuild the launcher.", "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProcessStartInfo startInfo = new(hostPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--owner");
        startInfo.ArgumentList.Add(ownerHandle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(request.Mode);
        AddArgument(startInfo, "--folder", request.Folder);
        AddArgument(startInfo, "--display", request.DisplayName);
        AddArgument(startInfo, "--name", request.FolderName);
        AddArgument(startInfo, "--launch", request.LaunchOptions);
        AddArgument(startInfo, "--icon", request.IconName);
        AddArgument(startInfo, "--left", request.WindowLeft?.ToString() ?? string.Empty);
        AddArgument(startInfo, "--top", request.WindowTop?.ToString() ?? string.Empty);
        AddArgument(startInfo, "--id", request.VersionId);
        AddArgument(startInfo, "--detected", request.DetectedDisplayName);
        AddArgument(startInfo, "--versions", string.Join('\n', SubnauticaVersionRegistry.All.Select(version => version.Id + "\t" + version.DisplayName)));
        AddArgument(startInfo, "--background", ResolveBackgroundPath());

        string output;
        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows could not start the Add Version window.");
            ActiveHosts.TryAdd(process.Id, process);
            try
            {
                output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
            }
            finally
            {
                ActiveHosts.TryRemove(process.Id, out _);
            }
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show("The Add Version window could not start.\n\n" + exception.Message, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string? result = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        WriteBridgeLog("Native result: " + (result ?? "<empty>"));
        if (string.IsNullOrEmpty(result))
            return;

        try
        {
            string[] values = result.Split('|');
            if (values[0] == "CHOICE" && values.Length == 2 && values[1] == "existing")
            {
                await RunAsync(store, ownerHandle, onChanged, new HostRequest { Mode = "existing" });
                return;
            }

            if (values[0] == "INSTALL" && values.Length == 2)
            {
                string selectedId = Decode(values[1]);
                SubnauticaVersionDefinition? definition = SubnauticaVersionRegistry.All.FirstOrDefault(version => version.Id == selectedId);
                if (definition is null) throw new InvalidOperationException("The selected install version was not found in the registry.");
                ImGuiDepotInstallFlow.LoginResult login = await ImGuiDepotInstallFlow.PromptLoginAsync(ownerHandle);
                if (login.GoBack) { await RunAsync(store, ownerHandle, onChanged, new HostRequest { Mode = "install" }); return; }
                if (login.Credentials is null) return;
                LauncherVersion? installed = await ImGuiDepotInstallFlow.InstallAsync(ownerHandle, definition, login.Credentials, store);
                if (installed is not null) onChanged(installed);
                return;
            }

            if (values[0] == "EDIT" && values.Length == 5 && request.EditingVersion is not null)
            {
                LauncherVersion updated = new()
                {
                    GameFolder = request.EditingVersion.GameFolder,
                    DisplayName = Decode(values[1]).Trim(),
                    FolderName = Decode(values[2]).Trim(),
                    LaunchOptions = Decode(values[3]).Trim(),
                    IconName = Decode(values[4]),
                    OriginalVersionId = request.EditingVersion.OriginalVersionId,
                    OriginalVersionDisplayName = request.EditingVersion.OriginalVersionDisplayName
                };
                if (store.TryUpdate(request.EditingVersion, updated, out string updateError)) onChanged(updated);
                else WpfMessageBox.Show(updateError, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (values[0] == "STEAMOPTIONS" && values.Length == 3 && request.EditingVersion is not null)
            {
                if (SteamLaunchOptionsDetector.TryDetectSubnautica(out string launchOptions))
                {
                    await RunAsync(store, ownerHandle, onChanged, new HostRequest
                    {
                        Mode = "edit",
                        EditingVersion = request.EditingVersion,
                        Folder = request.EditingVersion.GameFolder,
                        DisplayName = request.EditingVersion.DisplayName,
                        FolderName = request.EditingVersion.FolderName,
                        LaunchOptions = launchOptions,
                        IconName = request.EditingVersion.IconName,
                        WindowLeft = int.TryParse(values[1], out int left) ? left : null,
                        WindowTop = int.TryParse(values[2], out int top) ? top : null
                    });
                }
                else
                {
                    WpfMessageBox.Show("No Steam launch options were found for Subnautica.", "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RunAsync(store, ownerHandle, onChanged, request);
                }
                return;
            }

            if (values[0] == "DELETE" && values.Length == 2 && request.EditingVersion is not null)
            {
                try
                {
                    if (values[1] == "game") Directory.Delete(request.EditingVersion.GameFolder, true);
                    if (store.TryRemove(request.EditingVersion, out string removeError)) onChanged(null);
                    else WpfMessageBox.Show(removeError, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception exception)
                {
                    WpfMessageBox.Show("The version could not be deleted.\n\n" + exception.Message, "Delete Version", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            if (values[0] == "DETECT" && values.Length == 2)
            {
            string selectedPath = Decode(values[1]);
            WriteBridgeLog("Selected path: " + selectedPath);
            if (!VersionEntryValidator.TryNormalizeGameFolder(selectedPath, out string gameFolder, out string error))
            {
                WpfMessageBox.Show(error, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                await RunAsync(store, ownerHandle, onChanged, new HostRequest { Mode = "existing", Folder = selectedPath });
                return;
            }

            WriteBridgeLog("Normalized game folder: " + gameFolder);
            if (store.Load().Any(item => string.Equals(Path.GetFullPath(item.GameFolder), Path.GetFullPath(gameFolder), StringComparison.OrdinalIgnoreCase)))
            {
                WpfMessageBox.Show("That Subnautica version is already managed by this launcher.", "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            WriteBridgeLog("Build-time candidates: " + string.Join(" | ", SubnauticaBuildIdentityResolver.GetCandidateFiles(gameFolder)));

            if (!SubnauticaBuildIdentityResolver.TryDetect(gameFolder, out SubnauticaVersionDefinition? detected, out error))
            {
                WriteBridgeLog("Detection failed: " + error);
                WpfMessageBox.Show(error, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                await RunAsync(store, ownerHandle, onChanged, new HostRequest { Mode = "existing", Folder = gameFolder });
                return;
            }

            SubnauticaVersionDefinition identifiedVersion = detected ?? throw new InvalidOperationException("Version detection returned no result.");
            bool isSpeedrunningMod = SpeedrunningModDetector.IsDetected(gameFolder);
            await RunAsync(store, ownerHandle, onChanged, new HostRequest
            {
                Mode = "existing",
                Folder = gameFolder,
                DisplayName = isSpeedrunningMod ? SpeedrunningModDetector.DisplayName : LauncherVersionNaming.BuildDisplayName(identifiedVersion.Id, identifiedVersion.DisplayName),
                FolderName = isSpeedrunningMod ? SpeedrunningModDetector.DefaultFolderName : BuildSuggestedFolderName(identifiedVersion.Id),
                VersionId = identifiedVersion.Id,
                DetectedDisplayName = isSpeedrunningMod ? $"{SpeedrunningModDetector.DisplayName} ({identifiedVersion.DisplayName})" : identifiedVersion.DisplayName
            });
            WriteBridgeLog("Detected version: " + identifiedVersion.Id);
                return;
            }

            if (values[0] == "ADD" && values.Length == 6)
            {
            string selectedPath = Decode(values[1]);
            if (!VersionEntryValidator.TryNormalizeGameFolder(selectedPath, out string gameFolder, out string folderError))
            {
                WpfMessageBox.Show(folderError, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!SubnauticaBuildIdentityResolver.TryDetect(gameFolder, out SubnauticaVersionDefinition? detected, out string detectionError))
            {
                WpfMessageBox.Show(detectionError, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LauncherVersion version = new()
            {
                GameFolder = gameFolder,
                DisplayName = Decode(values[2]),
                FolderName = Decode(values[3]),
                OriginalVersionId = detected!.Id,
                OriginalVersionDisplayName = detected.DisplayName
            };

            if (!VersionEntryValidator.IsValidFolderName(version.FolderName, out string validationError))
            {
                WpfMessageBox.Show(validationError, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

                if (store.TryAdd(version, out string error))
                    onChanged(version);
                else
                    WpfMessageBox.Show(error, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show("The Add Version window returned an invalid result.\n\n" + exception.Message, "Subnautica Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void CloseAll()
    {
        foreach ((int id, Process process) in ActiveHosts)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            finally
            {
                ActiveHosts.TryRemove(id, out _);
            }
        }
    }

    internal static void RegisterHost(Process process) => ActiveHosts.TryAdd(process.Id, process);

    internal static void UnregisterHost(Process process) => ActiveHosts.TryRemove(process.Id, out _);

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value ?? string.Empty);
    }

    private static string ResolveBackgroundPath()
    {
        LauncherConfig config = LauncherConfig.Load();
        if (File.Exists(config.BackgroundPreset))
            return config.BackgroundPreset;

        return Path.Combine(AppContext.BaseDirectory, "Dependencies", "Assets", "Backgrounds", config.BackgroundPreset + ".png");
    }

    private static string BuildSuggestedFolderName(string id)
    {
        return id[..Math.Min(30, id.Length)];
    }

    private static string Decode(string value)
    {
        // CryptBinaryToString may omit trailing '=' padding. .NET's decoder requires it.
        int missingPadding = value.Length % 4;
        if (missingPadding != 0)
            value = value.PadRight(value.Length + (4 - missingPadding), '=');

        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static void WriteBridgeLog(string message)
    {
        try
        {
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Subnautica Launcher", "logs");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(Path.Combine(logDirectory, "add-version-bridge.log"), $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never disrupt the add-version flow.
        }
    }

    private sealed class HostRequest
    {
        public string Mode { get; init; } = "choice";
        public string Folder { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string FolderName { get; init; } = string.Empty;
        public string VersionId { get; init; } = string.Empty;
        public string DetectedDisplayName { get; init; } = string.Empty;
        public string LaunchOptions { get; init; } = string.Empty;
        public string IconName { get; init; } = "SNLIcon.png";
        public int? WindowLeft { get; init; }
        public int? WindowTop { get; init; }
        public LauncherVersion? EditingVersion { get; init; }
    }

}
