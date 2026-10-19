using System.IO;
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
    private readonly Dictionary<string, LiveSplitPluginProcessAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Version> _latestVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginUpdateDescriptor> _latestUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _nextRestartUtc = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private bool _initialized;

    public LauncherPluginManager(IntPtr ownerHandle)
    {
        Directory.CreateDirectory(AppPaths.PluginsPath);
        Directory.CreateDirectory(AppPaths.LogsPath);
        MigrateLegacyLog("SubnauticaMarco.log");
    }

    public async Task InitializeAsync()
    {
        await _operations.WaitAsync();
        try
        {
            foreach (LauncherPluginDefinition definition in LauncherPluginCatalog.All)
            {
                TryMigrateLegacyInstall(definition);
                PluginPersistentState state = _store.Load(definition.Id);
                if (state.Enabled && File.Exists(GetAssemblyPath(definition)))
                {
                    try { StartAdapter(definition); }
                    catch { }
                }
            }
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

    public SubnauticaMacroPluginSettings LoadMacroSettings(string pluginId) => _store.LoadMacroSettings(pluginId);

    public async Task InstallOrUpdateAsync(string pluginId)
    {
        LauncherPluginDefinition definition = Find(pluginId);
        await _operations.WaitAsync();
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
                PluginUpdateDescriptor update = await GetLatestUpdateAsync(definition);
                byte[] payload = await Http.GetByteArrayAsync(update.PayloadUri);
                await File.WriteAllBytesAsync(temporary, payload);
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
                if (state.Enabled && (_initialized || wasRunning)) StartAdapter(definition);
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
                if (hadInstalledAssembly && wasRunning && File.Exists(destination)) StartAdapter(definition);
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

    private void SetEnabledCore(LauncherPluginDefinition definition, bool enabled)
    {
        if (!File.Exists(GetAssemblyPath(definition)))
            throw new FileNotFoundException("Install the plugin before enabling it.");

        PluginPersistentState state = _store.Load(definition.Id);
        state.Enabled = enabled;
        _store.Save(definition.Id, state, enabled ? "Enabled by the user." : "Disabled by the user.");
        _nextRestartUtc.Remove(definition.Id);
        if (enabled) StartAdapter(definition);
        else StopAdapter(definition.Id);
    }

    public async Task MaintainEnabledPluginsAsync()
    {
        if (_disposed || !await _operations.WaitAsync(0)) return;
        try
        {
            DateTime now = DateTime.UtcNow;
            foreach (LauncherPluginDefinition definition in LauncherPluginCatalog.All)
            {
                PluginPersistentState state = _store.Load(definition.Id);
                if (!state.Enabled || !File.Exists(GetAssemblyPath(definition)))
                {
                    if (!state.Enabled) StopAdapter(definition.Id);
                    continue;
                }

                if (_adapters.TryGetValue(definition.Id, out LiveSplitPluginProcessAdapter? adapter) && adapter.IsRunning)
                    continue;
                if (_nextRestartUtc.TryGetValue(definition.Id, out DateTime retryAt) && now < retryAt)
                    continue;

                StopAdapter(definition.Id);
                try
                {
                    StartAdapter(definition);
                    _nextRestartUtc.Remove(definition.Id);
                }
                catch (Exception exception)
                {
                    _errors[definition.Id] = $"Plugin host stopped and could not restart: {exception.Message}";
                    _nextRestartUtc[definition.Id] = now.AddSeconds(5);
                }
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task SaveMacroSettingsAsync(string pluginId, SubnauticaMacroPluginSettings settings)
    {
        settings.GameMode = Math.Clamp(settings.GameMode, 0, 6);
        settings.ExplosionPreset = Math.Clamp(settings.ExplosionPreset, 0, 6);
        settings.CustomMinimumSeconds = Math.Max(0, settings.CustomMinimumSeconds);
        settings.CustomMaximumSeconds = Math.Max(settings.CustomMinimumSeconds, settings.CustomMaximumSeconds);
        await _operations.WaitAsync();
        try
        {
            _store.SaveMacroSettings(pluginId, settings);
            PluginPersistentState state = _store.Load(pluginId);
            LauncherPluginDefinition definition = Find(pluginId);
            if (!state.Enabled || !File.Exists(GetAssemblyPath(definition))) return;
            if (_adapters.TryGetValue(pluginId, out LiveSplitPluginProcessAdapter? adapter))
            {
                adapter.NotifySettingsChanged();
            }
            else
            {
                StartAdapter(definition);
            }
        }
        finally
        {
            _operations.Release();
        }
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
        }
        finally
        {
            _operations.Release();
        }
    }

    private async Task RefreshLatestVersionsAsync()
    {
        foreach (LauncherPluginDefinition definition in LauncherPluginCatalog.All)
        {
            try
            {
                PluginUpdateDescriptor update = await GetLatestUpdateAsync(definition);
                _latestVersions[definition.Id] = update.Version;
                _latestUpdates[definition.Id] = update;
            }
            catch (Exception exception)
            {
                _errors[definition.Id] = $"Could not check for plugin updates: {exception.Message}";
            }
        }
    }

    private async Task<PluginUpdateDescriptor> GetLatestUpdateAsync(LauncherPluginDefinition definition)
    {
        string xml = await Http.GetStringAsync(definition.UpdateManifestUri);
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

        if (_adapters.TryGetValue(definition.Id, out LiveSplitPluginProcessAdapter? adapter) && adapter.LastError is not null)
            error = adapter.LastError;

        return new LauncherPluginSnapshot(
            definition,
            File.Exists(assemblyPath) && installedVersion is not null,
            _store.Load(definition.Id).Enabled,
            _adapters.TryGetValue(definition.Id, out LiveSplitPluginProcessAdapter? loadedAdapter) && loadedAdapter.IsRunning,
            installedVersion,
            _latestVersions.GetValueOrDefault(definition.Id),
            error);
    }

    private void StartAdapter(LauncherPluginDefinition definition)
    {
        StopAdapter(definition.Id);
        try
        {
            string settingsPath = definition.Id == LauncherPluginCatalog.SubnauticaResetMacroId
                ? _store.EnsureLiveSplitSettings(definition.Id)
                : _store.GetLiveSplitSettingsPath(definition.Id);
            _adapters[definition.Id] = new LiveSplitPluginProcessAdapter(
                definition,
                GetAssemblyPath(definition),
                settingsPath,
                _store.GetPluginDataDirectory(definition.Id));
            _errors.Remove(definition.Id);
            _nextRestartUtc.Remove(definition.Id);
        }
        catch (Exception exception)
        {
            _errors[definition.Id] = exception.Message;
            throw;
        }
    }

    private bool StopAdapter(string pluginId)
    {
        if (!_adapters.Remove(pluginId, out LiveSplitPluginProcessAdapter? adapter)) return false;
        adapter.Dispose();
        return true;
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

    private static void MigrateLegacyLog(string fileName)
    {
        string legacy = Path.Combine(AppPaths.BasePath, fileName);
        if (!File.Exists(legacy)) return;
        try
        {
            string destination = Path.Combine(AppPaths.LogsPath, fileName);
            if (File.Exists(destination))
            {
                File.AppendAllText(destination, Environment.NewLine + File.ReadAllText(legacy));
                File.Delete(legacy);
            }
            else
            {
                File.Move(legacy, destination);
            }
        }
        catch
        {
            // Log migration is best-effort and must not block plugin startup.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (LiveSplitPluginProcessAdapter adapter in _adapters.Values) adapter.Dispose();
        _adapters.Clear();
        _operations.Dispose();
    }

    private sealed record PluginUpdateDescriptor(Version Version, Uri PayloadUri);
}
