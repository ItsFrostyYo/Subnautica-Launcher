using System.Diagnostics;
using System.Globalization;
using System.IO;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

internal sealed class LiveSplitPluginProcessAdapter : IDisposable
{
    private readonly Process _process;
    private readonly string _stopPath;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _errorSync = new();
    private string? _lastError;
    private bool _disposed;

    public LiveSplitPluginProcessAdapter(
        LauncherPluginDefinition definition,
        string assemblyPath,
        string settingsPath,
        string dataDirectory)
    {
        string launcherPath = Path.Combine(AppContext.BaseDirectory, "SubnauticaLauncher.exe");
        if (!File.Exists(launcherPath))
        {
            launcherPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The launcher executable path could not be resolved.");
        }

        _stopPath = Path.Combine(dataDirectory, "stop.signal");
        TryDelete(_stopPath);
        TryDelete(Path.Combine(dataDirectory, "error.log"));
        TryDelete(Path.Combine(dataDirectory, "status.txt"));
        Directory.CreateDirectory(AppPaths.LogsPath);

        var startInfo = new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(LiveSplitPluginHostMode.Command);
        Add(startInfo, "--assembly", assemblyPath);
        Add(startInfo, "--component", definition.ComponentName);
        Add(startInfo, "--settings", settingsPath);
        Add(startInfo, "--data", dataDirectory);
        Add(startInfo, "--stop", _stopPath);
        Add(startInfo, "--logs", AppPaths.LogsPath);
        Add(startInfo, "--parent", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data?.StartsWith("Running|", StringComparison.Ordinal) == true) _ready.Set();
        };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data)) return;
            lock (_errorSync) _lastError = eventArgs.Data;
        };
        if (!_process.Start()) throw new InvalidOperationException($"Could not start {definition.Name}.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        if (!WaitUntilReady(TimeSpan.FromSeconds(5)))
        {
            string message = ReadError()
                ?? (_process.HasExited
                    ? $"{definition.Name} stopped during startup (exit code {_process.ExitCode})."
                    : $"{definition.Name} did not finish starting within 5 seconds.");
            try { if (!_process.HasExited) _process.Kill(true); } catch { }
            _process.Dispose();
            _ready.Dispose();
            throw new InvalidOperationException(message);
        }
    }

    public string? LastError
    {
        get
        {
            if (ReadError() is string error) return error;
            if (_process.HasExited && !_disposed) return $"The plugin host stopped unexpectedly (exit code {_process.ExitCode}).";
            return null;
        }
    }

    public bool IsRunning
    {
        get
        {
            if (_disposed || _process.HasExited) return false;
            return _ready.IsSet;
        }
    }

    public void NotifySettingsChanged()
    {
        // The host watches settings.xml and applies it through IComponent.SetSettings.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stopPath)!);
            File.WriteAllText(_stopPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            if (!_process.HasExited && !_process.WaitForExit(1000))
            {
                _process.Kill(true);
                _process.WaitForExit(750);
            }
        }
        catch
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                    _process.WaitForExit(750);
                }
            }
            catch { }
        }
        finally
        {
            _process.Dispose();
            _ready.Dispose();
            TryDelete(_stopPath);
        }
    }

    private string? ReadError()
    {
        try
        {
            lock (_errorSync) return _lastError;
        }
        catch { return null; }
    }

    private bool WaitUntilReady(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (_process.HasExited || ReadError() is not null) return false;
            try
            {
                if (_ready.IsSet) return true;
            }
            catch
            {
                // The host can finish exiting while readiness is being observed.
            }
            Thread.Sleep(50);
        }
        return false;
    }

    private static void Add(ProcessStartInfo info, string name, string value)
    {
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(value);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
