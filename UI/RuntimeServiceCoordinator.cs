using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Timer;

namespace SubnauticaLauncher.UI;

internal sealed class RuntimeServiceCoordinator
{
    private readonly Action _startStatusRefreshTimer;
    private readonly Action _stopStatusRefreshTimer;
    private bool _started;
    private int _pauseCount;

    public RuntimeServiceCoordinator(Action startStatusRefreshTimer, Action stopStatusRefreshTimer)
    {
        _startStatusRefreshTimer = startStatusRefreshTimer;
        _stopStatusRefreshTimer = stopStatusRefreshTimer;
    }

    public bool IsStarted => _started;

    public void Start(bool trackerEnabled, bool speedrunTimerEnabled)
    {
        if (_started)
            return;

        _started = true;
        _pauseCount = 0;

        GameProcessMonitor.Start();
        _startStatusRefreshTimer();
        GameEventDocumenter.Start();
        DebugTelemetryController.Start();

        if (trackerEnabled)
            Subnautica100TrackerOverlayController.Start();
        else
            Subnautica100TrackerOverlayController.Stop();

        if (speedrunTimerEnabled)
            SpeedrunTimerController.Start();
        else
            SpeedrunTimerController.Stop();
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _pauseCount = 0;

        _stopStatusRefreshTimer();
        SpeedrunTimerController.Stop();
        Subnautica100TrackerOverlayController.Stop();
        DebugTelemetryController.Stop();
        GameEventDocumenter.Stop();
        GameProcessMonitor.Stop();
    }

    public void PauseForFolderSwitch()
    {
        PauseNonCriticalWork();
    }

    public void ResumeAfterFolderSwitch()
    {
        ResumeNonCriticalWork();
    }

    public void PauseForBusyOperation()
    {
        PauseNonCriticalWork();
    }

    public void ResumeAfterBusyOperation()
    {
        ResumeNonCriticalWork();
    }

    private void PauseNonCriticalWork()
    {
        if (!_started)
            return;

        _pauseCount++;
        if (_pauseCount > 1)
            return;

        _stopStatusRefreshTimer();
        DebugTelemetryController.Stop();
        GameEventDocumenter.Stop();
    }

    private void ResumeNonCriticalWork()
    {
        if (!_started || _pauseCount <= 0)
            return;

        _pauseCount--;
        if (_pauseCount > 0)
            return;

        GameEventDocumenter.Start();
        DebugTelemetryController.Start();
        _startStatusRefreshTimer();
    }
}
