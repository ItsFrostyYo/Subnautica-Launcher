namespace SubnauticaLauncher.Core;

internal static class LauncherBusyCoordinator
{
    private static readonly object Sync = new();
    private static int _busyCount;

    public static event EventHandler<bool>? BusyStateChanged;

    public static bool IsBusy
    {
        get
        {
            lock (Sync)
                return _busyCount > 0;
        }
    }

    public static IDisposable Begin(string operationName)
    {
        bool raiseEvent = false;
        int newCount;

        lock (Sync)
        {
            _busyCount++;
            newCount = _busyCount;
            if (_busyCount == 1)
                raiseEvent = true;
        }

        Logger.Log($"[Busy] Begin '{operationName}'. Count={newCount}");
        if (raiseEvent)
            BusyStateChanged?.Invoke(null, true);

        return new BusyScope(operationName);
    }

    private static void End(string operationName)
    {
        bool raiseEvent = false;
        int newCount;

        lock (Sync)
        {
            _busyCount = Math.Max(0, _busyCount - 1);
            newCount = _busyCount;
            if (_busyCount == 0)
                raiseEvent = true;
        }

        Logger.Log($"[Busy] End '{operationName}'. Count={newCount}");
        if (raiseEvent)
            BusyStateChanged?.Invoke(null, false);
    }

    private sealed class BusyScope : IDisposable
    {
        private readonly string _operationName;
        private bool _disposed;

        public BusyScope(string operationName)
        {
            _operationName = operationName;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            End(_operationName);
        }
    }
}
