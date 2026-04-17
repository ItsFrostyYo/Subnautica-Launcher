using System.Diagnostics;
using System.IO;

namespace SubnauticaLauncher.Gameplay;

public static class GameProcessMonitor
{
    private static readonly object Sync = new();
    private static CancellationTokenSource? _cts;
    private static Task? _loopTask;
    private static GameProcessSnapshot _snapshot = GameProcessSnapshot.Empty;

    public static void Start()
    {
        lock (Sync)
        {
            if (_loopTask != null)
                return;

            _snapshot = CaptureSnapshot();
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => PollLoopAsync(_cts.Token));
        }
    }

    public static void Stop()
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (Sync)
        {
            cts = _cts;
            loopTask = _loopTask;
            _cts = null;
            _loopTask = null;
            _snapshot = GameProcessSnapshot.Empty;
        }

        if (cts == null)
            return;

        try
        {
            cts.Cancel();
            loopTask?.Wait(1000);
        }
        catch
        {
            // best-effort shutdown
        }
        finally
        {
            cts.Dispose();
        }
    }

    public static void RefreshNow()
    {
        lock (Sync)
        {
            _snapshot = CaptureSnapshot();
        }
    }

    public static GameProcessSnapshot GetSnapshot()
    {
        lock (Sync)
        {
            return _snapshot;
        }
    }

    public static bool TryOpenRunningProcess(string processName, out Process? process)
    {
        RefreshNow();
        GameProcessInfo info = GetSnapshot().Get(processName);
        if (!info.IsRunning || info.ProcessId is not int pid)
        {
            process = null;
            return false;
        }

        try
        {
            process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                process.Dispose();
                process = null;
                return false;
            }

            return true;
        }
        catch
        {
            process = null;
            return false;
        }
    }

    private static async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                GameProcessSnapshot snapshot = CaptureSnapshot();
                lock (Sync)
                {
                    _snapshot = snapshot;
                }
            }
            catch
            {
                // monitoring must stay resilient
            }

            try
            {
                await Task.Delay(750, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static GameProcessSnapshot CaptureSnapshot()
    {
        return new GameProcessSnapshot(
            CaptureProcess("Subnautica"),
            CaptureProcess("SubnauticaZero"));
    }

    private static GameProcessInfo CaptureProcess(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    string? exePath = null;
                    string? folderPath = null;
                    try
                    {
                        exePath = process.MainModule?.FileName;
                        folderPath = string.IsNullOrWhiteSpace(exePath)
                            ? null
                            : Path.GetDirectoryName(exePath);
                    }
                    catch
                    {
                        // Access to MainModule can fail even when the process is valid.
                    }

                    return new GameProcessInfo(
                        processName,
                        true,
                        process.Id,
                        exePath,
                        folderPath);
                }
                catch
                {
                    // Process raced out; try the next one.
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }

        return new GameProcessInfo(processName, false, null, null, null);
    }
}

public sealed record GameProcessInfo(
    string ProcessName,
    bool IsRunning,
    int? ProcessId,
    string? ExecutablePath,
    string? FolderPath);

public sealed record GameProcessSnapshot(
    GameProcessInfo Subnautica,
    GameProcessInfo BelowZero)
{
    public static GameProcessSnapshot Empty { get; } = new(
        new GameProcessInfo("Subnautica", false, null, null, null),
        new GameProcessInfo("SubnauticaZero", false, null, null, null));

    public bool AnyRunning => Subnautica.IsRunning || BelowZero.IsRunning;

    public GameProcessInfo Get(string processName)
    {
        return processName.Equals("SubnauticaZero", StringComparison.OrdinalIgnoreCase)
            ? BelowZero
            : Subnautica;
    }
}
