using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Net.Http;
using System.Xml.Linq;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

public sealed class LauncherPluginManager : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly PluginStateStore _store = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly Dictionary<string, PluginProcessAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Version> _latestVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginUpdateDescriptor> _latestUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _nextRestartUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private bool _disposed;
    private bool _initialized;
    private readonly object _shutdownSync = new();
    private Task? _shutdownTask;

    public LauncherPluginManager()
    {
        Directory.CreateDirectory(AppPaths.PluginsPath);
        Directory.CreateDirectory(AppPaths.LogsPath);
    }

    public async Task InitializeAsync()
    {
        await _operations.WaitAsync();
        try
        {
            foreach (LauncherPluginDefinition definition in LauncherPluginCatalog.All)
            {
                TryMigrateLegacyInstall(definition);
            }
            foreach (string group in LauncherPluginCatalog.All.Select(definition => definition.HostGroup).Distinct(StringComparer.OrdinalIgnoreCase))
                try { RestartGroup(group); } catch { }
            _initialized = true;
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task<IReadOnlyList<LauncherPluginSnapshot>> GetSnapshotsAsync(bool checkForUpdates)
    {
        if (checkForUpdates) await RefreshLatestVersionsAsync();
        return LauncherPluginCatalog.All.Select(CreateSnapshot).ToArray();
    }

    public async Task UpdateInstalledPluginsAsync(Func<bool> isGameRunning)
    {
        if (isGameRunning()) return;
        await RefreshLatestVersionsAsync();
        LauncherPluginDefinition[] updates = LauncherPluginCatalog.All
            .Where(definition => CreateSnapshot(definition).HasUpdate)
            .ToArray();

        foreach (LauncherPluginDefinition definition in updates)
        {
            if (isGameRunning()) return;
            try { await InstallOrUpdateAsync(definition.Id); }
            catch (Exception exception) { _errors[definition.Id] = $"Automatic update failed: {exception.Message}"; }
        }
    }

    public async Task ShowSettingsAsync(string pluginId, IntPtr ownerHandle)
    {
        LauncherPluginDefinition definition = Find(pluginId);
        string assemblyPath;
        string settingsPath;
        string dataDirectory;

        await _operations.WaitAsync();
        try
        {
            if (_disposed) return;
            assemblyPath = GetAssemblyPath(definition);
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Install the plugin before opening its settings.", assemblyPath);
            settingsPath = _store.GetPluginSettingsPath(definition.Id);
            dataDirectory = _store.GetPluginDataDirectory(definition.Id);
        }
        finally
        {
            _operations.Release();
        }

        Exception? failure = null;
        try
        {
            string launcherPath = File.Exists(Path.Combine(AppContext.BaseDirectory, "SubnauticaLauncher.exe"))
                ? Path.Combine(AppContext.BaseDirectory, "SubnauticaLauncher.exe")
                : Environment.ProcessPath ?? throw new InvalidOperationException("The launcher executable path could not be resolved.");
            var startInfo = new ProcessStartInfo(launcherPath)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(PluginSettingsHostMode.Command);
            AddArgument(startInfo, "--assembly", assemblyPath);
            AddArgument(startInfo, "--component", definition.ComponentName);
            AddArgument(startInfo, "--settings", settingsPath);
            AddArgument(startInfo, "--data", dataDirectory);
            AddArgument(startInfo, "--logs", AppPaths.LogsPath);
            AddArgument(startInfo, "--title", definition.Name + " Settings");
            AddArgument(startInfo, "--owner", ownerHandle.ToInt64().ToString(CultureInfo.InvariantCulture));
            AddArgument(startInfo, "--parent", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not open settings for {definition.Name}.");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(_shutdownCancellation.Token);
            string output = await outputTask;
            string error = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"{definition.Name} settings closed with exit code {process.ExitCode}."
                    : error.Trim());
            if (!output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(line => line is "SAVED" or "CANCELLED"))
                throw new InvalidOperationException($"{definition.Name} settings did not return a valid result.");
        }
        catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
        {
            // Launcher shutdown owns cancellation; no user-facing error is needed.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        if (failure is not null) throw failure;
    }

    public async Task InstallOrUpdateAsync(string pluginId)
    {
        LauncherPluginDefinition definition = Find(pluginId);
        CancellationToken cancellationToken = _shutdownCancellation.Token;
        await _operations.WaitAsync(cancellationToken);
        try
        {
            PluginPersistentState originalState = _store.Load(pluginId);
            bool wasRunning = false;
            string directory = GetResolvedInstallDirectory(definition);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, definition.AssemblyFileName);
            string temporary = destination + ".download";
            string backup = destination + ".backup";
            bool hadInstalledAssembly = File.Exists(destination);
            try
            {
                PluginUpdateDescriptor update = await GetLatestUpdateAsync(definition, cancellationToken);
                byte[] payload = await Http.GetByteArrayAsync(update.PayloadUri, cancellationToken);
                await File.WriteAllBytesAsync(temporary, payload, cancellationToken);
                Version downloadedVersion = ValidateAssembly(definition, temporary);
                if (downloadedVersion < update.Version)
                    throw new InvalidDataException($"The downloaded plugin is v{downloadedVersion}, but v{update.Version} is required.");

                wasRunning = StopAdapter(pluginId);
                TryDelete(backup);
                if (hadInstalledAssembly) File.Copy(destination, backup, overwrite: true);
                File.Move(temporary, destination, true);
                PluginPersistentState state = _store.Load(pluginId);
                state.Enabled = hadInstalledAssembly ? originalState.Enabled : true;
                _store.Save(pluginId, state, hadInstalledAssembly ? "Plugin updated; loader state preserved." : "Plugin installed and enabled.");
                _errors.Remove(pluginId);
                if (_initialized || wasRunning) RestartGroup(definition.HostGroup);
                TryDelete(backup);
            }
            catch
            {
                TryDelete(temporary);
                if (File.Exists(backup))
                {
                    File.Move(backup, destination, true);
                    _store.Save(pluginId, originalState, "Plugin update failed; previous loader state restored.");
                }
                else if (!hadInstalledAssembly)
                {
                    TryDelete(destination);
                    PluginPersistentState state = _store.Load(pluginId);
                    state.Enabled = false;
                    _store.Save(pluginId, state, "Plugin installation failed before a usable assembly was installed.");
                }
                if (wasRunning || _initialized) RestartGroup(definition.HostGroup);
                throw;
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled)
    {
        LauncherPluginDefinition definition = Find(pluginId);
        await _operations.WaitAsync();
        try
        {
            SetEnabledCore(definition, enabled);
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task MoveComponentAsync(string pluginId, int direction)
    {
        if (direction == 0) return;
        LauncherPluginDefinition definition = Find(pluginId);
        await _operations.WaitAsync();
        try
        {
            LauncherPluginDefinition[] ordered = LauncherPluginCatalog.All
                .Where(candidate => string.Equals(candidate.HostGroup, definition.HostGroup, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(GetAssemblyPath(candidate)))
                .OrderBy(GetLayoutOrder)
                .ToArray();
            _adapters.TryGetValue(definition.HostGroup, out PluginProcessAdapter? runningAdapter);
            string[] visualOrderBefore = runningAdapter?.VisualComponentIds is { Count: > 0 } visualIds
                ? ordered.Where(candidate => visualIds.Contains(candidate.Id)).Select(candidate => candidate.Id).ToArray()
                : [];
            int index = Array.FindIndex(ordered, candidate => string.Equals(candidate.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            int target = Math.Clamp(index + Math.Sign(direction), 0, ordered.Length - 1);
            if (target == index) return;
            (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
            for (int order = 0; order < ordered.Length; order++)
            {
                PluginPersistentState state = _store.Load(ordered[order].Id);
                state.LayoutOrder = order * 100;
                _store.Save(ordered[order].Id, state, "Plugin layout order changed by the user.");
            }
            string[] visualOrderAfter = runningAdapter?.VisualComponentIds is { Count: > 0 } updatedVisualIds
                ? ordered.Where(candidate => updatedVisualIds.Contains(candidate.Id)).Select(candidate => candidate.Id).ToArray()
                : [];
            if (!visualOrderBefore.SequenceEqual(visualOrderAfter, StringComparer.OrdinalIgnoreCase))
                RestartGroup(definition.HostGroup);
        }
        finally
        {
            _operations.Release();
        }
    }

    private void SetEnabledCore(LauncherPluginDefinition definition, bool enabled)
    {
        if (!File.Exists(GetAssemblyPath(definition)))
            throw new FileNotFoundException("Install the plugin before enabling it.");

        PluginPersistentState state = _store.Load(definition.Id);
        state.Enabled = enabled;
        _store.Save(definition.Id, state, enabled ? "Enabled by the user." : "Disabled by the user.");
        _nextRestartUtc.Remove(definition.Id);
        RestartGroup(definition.HostGroup);
    }

    public async Task<bool> MaintainEnabledPluginsAsync()
    {
        if (_disposed || !await _operations.WaitAsync(0)) return false;
        bool stateChanged = false;
        try
        {
            DateTime now = DateTime.UtcNow;
            foreach (PluginProcessAdapter adapter in _adapters.Values.ToArray())
            {
                foreach (string pluginId in adapter.TakeDisableRequests())
                {
                    LauncherPluginDefinition requested = Find(pluginId);
                    PluginPersistentState requestedState = _store.Load(pluginId);
                    requestedState.Enabled = false;
                    _store.Save(pluginId, requestedState, "Disabled from the Launcher Plugins window.");
                    _nextRestartUtc.Remove(pluginId);
                    stateChanged = true;
                }
            }
            foreach (IGrouping<string, LauncherPluginDefinition> group in LauncherPluginCatalog.All.GroupBy(definition => definition.HostGroup, StringComparer.OrdinalIgnoreCase))
            {
                LauncherPluginDefinition[] desired = group
                    .Where(definition => _store.Load(definition.Id).Enabled && File.Exists(GetAssemblyPath(definition)))
                    .ToArray();
                if (desired.Length == 0)
                {
                    StopGroup(group.Key);
                    continue;
                }
                if (_adapters.TryGetValue(group.Key, out PluginProcessAdapter? adapter) && adapter.IsRunning &&
                    adapter.ComponentIds.SetEquals(desired.Select(definition => definition.Id)))
                    continue;
                if (desired.Any(definition => _nextRestartUtc.TryGetValue(definition.Id, out DateTime retryAt) && now < retryAt))
                    continue;
                try
                {
                    RestartGroup(group.Key);
                    foreach (LauncherPluginDefinition definition in desired) _nextRestartUtc.Remove(definition.Id);
                }
                catch (Exception exception)
                {
                    foreach (LauncherPluginDefinition definition in desired)
                    {
                        _errors[definition.Id] = $"Plugin host stopped and could not restart: {exception.Message}";
                        _nextRestartUtc[definition.Id] = now.AddSeconds(5);
                    }
                }
            }
        }
        finally
        {
            _operations.Release();
        }
        return stateChanged;
    }

    public async Task RemoveAsync(string pluginId)
    {
        LauncherPluginDefinition definition = Find(pluginId);
        await _operations.WaitAsync();
        try
        {
            StopAdapter(pluginId);
            string pluginsRoot = Path.GetFullPath(AppPaths.PluginsPath).TrimEnd(Path.DirectorySeparatorChar);
            foreach (string directory in GetInstallDirectories(definition).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string fullDirectory = Path.GetFullPath(directory);
                if (!fullDirectory.StartsWith(pluginsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The plugin directory is outside the launcher plugin folder.");
                if (Directory.Exists(fullDirectory)) Directory.Delete(fullDirectory, recursive: true);
            }
            _errors.Remove(pluginId);
            RestartGroup(definition.HostGroup);
        }
        finally
        {
            _operations.Release();
        }
    }

    private async Task RefreshLatestVersionsAsync()
    {
        CancellationToken cancellationToken = _shutdownCancellation.Token;
        foreach (LauncherPluginDefinition definition in LauncherPluginCatalog.All)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                PluginUpdateDescriptor update = await GetLatestUpdateAsync(definition, cancellationToken);
                _latestVersions[definition.Id] = update.Version;
                _latestUpdates[definition.Id] = update;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _errors[definition.Id] = $"Could not check for plugin updates: {exception.Message}";
            }
        }
    }

    private async Task<PluginUpdateDescriptor> GetLatestUpdateAsync(
        LauncherPluginDefinition definition,
        CancellationToken cancellationToken = default)
    {
        string xml = await Http.GetStringAsync(definition.UpdateManifestUri, cancellationToken);
        XDocument document = XDocument.Parse(xml, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("The plugin update manifest has no root element.");
        if (!string.Equals(root.Name.LocalName, "updates", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The plugin update manifest root must be <updates>.");

        foreach (XElement entry in root.Elements().Where(element =>
                     string.Equals(element.Name.LocalName, "update", StringComparison.OrdinalIgnoreCase))
                 .OrderByDescending(element => ParseVersion(element.Attribute("version")?.Value)))
        {
            Version? version = ParseVersion(entry.Attribute("version")?.Value);
            if (version is null) continue;
            XElement? file = entry.Elements().FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "files", StringComparison.OrdinalIgnoreCase))?
                .Elements().FirstOrDefault(element =>
                    string.Equals(element.Name.LocalName, "file", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(element.Attribute("status")?.Value, "removed", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        Path.GetFileName(element.Attribute("path")?.Value),
                        definition.AssemblyFileName,
                        StringComparison.OrdinalIgnoreCase));
            string? path = file?.Attribute("path")?.Value;
            if (string.IsNullOrWhiteSpace(path)) continue;
            string normalizedPath = path.Replace('\\', '/').TrimStart('/');
            if (normalizedPath.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("A plugin update path leaves its update root.");

            var result = new PluginUpdateDescriptor(version, new Uri(definition.UpdateBaseUri, normalizedPath));
            _latestVersions[definition.Id] = version;
            _latestUpdates[definition.Id] = result;
            return result;
        }

        throw new InvalidDataException($"The update manifest contains no payload for {definition.AssemblyFileName}.");
    }

    private static Version? ParseVersion(string? value) =>
        Version.TryParse(value, out Version? version) ? version : null;

    private LauncherPluginSnapshot CreateSnapshot(LauncherPluginDefinition definition)
    {
        string assemblyPath = GetAssemblyPath(definition);
        Version? installedVersion = null;
        string? error = _errors.GetValueOrDefault(definition.Id);
        if (File.Exists(assemblyPath))
        {
            try { installedVersion = ValidateAssembly(definition, assemblyPath); }
            catch (Exception exception) { error = exception.Message; }
        }

        if (_adapters.TryGetValue(definition.HostGroup, out PluginProcessAdapter? adapter) && adapter.LastError is not null)
            error = adapter.LastError;

        return new LauncherPluginSnapshot(
            definition,
            File.Exists(assemblyPath) && installedVersion is not null,
            _store.Load(definition.Id).Enabled,
            _adapters.TryGetValue(definition.HostGroup, out PluginProcessAdapter? loadedAdapter) && loadedAdapter.IsRunning && loadedAdapter.ComponentIds.Contains(definition.Id),
            installedVersion,
            _latestVersions.GetValueOrDefault(definition.Id),
            error,
            GetLayoutOrder(definition));
    }

    private void RestartGroup(string hostGroup)
    {
        if (_disposed) return;
        StopGroup(hostGroup);
        try
        {
            LauncherPluginDefinition[] enabled = LauncherPluginCatalog.All
                .Where(definition => string.Equals(definition.HostGroup, hostGroup, StringComparison.OrdinalIgnoreCase))
                .Where(definition => _store.Load(definition.Id).Enabled && File.Exists(GetAssemblyPath(definition)))
                .OrderBy(GetLayoutOrder)
                .ToArray();
            if (enabled.Length == 0) return;
            PluginHostedComponent[] components = enabled.Select(definition => new PluginHostedComponent
            {
                Id = definition.Id,
                AssemblyPath = GetAssemblyPath(definition),
                ComponentName = definition.ComponentName,
                SettingsPath = _store.GetPluginSettingsPath(definition.Id),
                LayoutOrder = GetLayoutOrder(definition)
            }).ToArray();
            const string title = "Launcher Plugins";
            _adapters[hostGroup] = new PluginProcessAdapter(
                title,
                components,
                GetGroupDataDirectory(hostGroup));
            foreach (LauncherPluginDefinition definition in enabled)
            {
                _errors.Remove(definition.Id);
                _nextRestartUtc.Remove(definition.Id);
            }
        }
        catch (Exception exception)
        {
            foreach (LauncherPluginDefinition definition in LauncherPluginCatalog.All.Where(definition =>
                         string.Equals(definition.HostGroup, hostGroup, StringComparison.OrdinalIgnoreCase)))
                _errors[definition.Id] = exception.Message;
            throw;
        }
    }

    private bool StopAdapter(string pluginId)
    {
        return StopGroup(Find(pluginId).HostGroup);
    }

    private bool StopGroup(string hostGroup)
    {
        if (!_adapters.Remove(hostGroup, out PluginProcessAdapter? adapter)) return false;
        adapter.Dispose();
        return true;
    }

    private int GetLayoutOrder(LauncherPluginDefinition definition) =>
        _store.Load(definition.Id).LayoutOrder ?? definition.DefaultLayoutOrder;

    private static string GetGroupDataDirectory(string hostGroup)
    {
        string safeName = string.Concat(hostGroup.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        string path = Path.Combine(AppPaths.DataPath, "PluginGroups", safeName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Version ValidateAssembly(LauncherPluginDefinition definition, string path)
    {
        AssemblyName assembly = AssemblyName.GetAssemblyName(path);
        string expectedName = Path.GetFileNameWithoutExtension(definition.AssemblyFileName);
        if (!string.Equals(assembly.Name, expectedName, StringComparison.Ordinal))
            throw new InvalidDataException($"The downloaded file is {assembly.Name}, not {expectedName}.");
        return assembly.Version ?? new Version();
    }

    private static LauncherPluginDefinition Find(string pluginId) =>
        LauncherPluginCatalog.All.FirstOrDefault(plugin => string.Equals(plugin.Id, pluginId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown launcher plugin: {pluginId}");

    private static IEnumerable<string> GetInstallDirectories(LauncherPluginDefinition definition)
    {
        yield return Path.Combine(AppPaths.PluginsPath, definition.InstallDirectoryName);
        foreach (string legacyName in definition.LegacyInstallDirectoryNames)
        {
            if (!string.IsNullOrWhiteSpace(legacyName))
                yield return Path.Combine(AppPaths.PluginsPath, legacyName);
        }
    }

    private static string GetResolvedInstallDirectory(LauncherPluginDefinition definition) =>
        GetInstallDirectories(definition).FirstOrDefault(directory =>
            File.Exists(Path.Combine(directory, definition.AssemblyFileName)))
        ?? Path.Combine(AppPaths.PluginsPath, definition.InstallDirectoryName);

    private static string GetAssemblyPath(LauncherPluginDefinition definition) =>
        Path.Combine(GetResolvedInstallDirectory(definition), definition.AssemblyFileName);

    private static void TryMigrateLegacyInstall(LauncherPluginDefinition definition)
    {
        string canonical = Path.Combine(AppPaths.PluginsPath, definition.InstallDirectoryName);
        if (File.Exists(Path.Combine(canonical, definition.AssemblyFileName))) return;

        foreach (string legacy in GetInstallDirectories(definition).Skip(1))
        {
            if (!File.Exists(Path.Combine(legacy, definition.AssemblyFileName))) continue;
            try
            {
                if (!Directory.Exists(canonical)) Directory.Move(legacy, canonical);
            }
            catch
            {
                // The resolver continues using the legacy location when migration is unavailable.
            }
            return;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    public Task ShutdownAsync()
    {
        lock (_shutdownSync)
            return _shutdownTask ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        _disposed = true;
        _shutdownCancellation.Cancel();
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            PluginProcessAdapter[] adapters = _adapters.Values.ToArray();
            _adapters.Clear();
            await Task.WhenAll(adapters.Select(adapter => Task.Run(adapter.Dispose))).ConfigureAwait(false);
        }
        finally
        {
            _operations.Release();
        }
    }

    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();

    private sealed record PluginUpdateDescriptor(Version Version, Uri PayloadUri);
}
