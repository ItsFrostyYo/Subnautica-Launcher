using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Timer;

namespace SubnauticaLauncher.UI;

internal sealed class RuntimeServiceCoordinator
{
    private readonly Action _startStatusRefreshTimer;
    private readonly Action _stopStatusRefreshTimer;
    private bool _started;
    private bool _pausedForFolderSwitch;

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
        _pausedForFolderSwitch = false;

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
        _pausedForFolderSwitch = false;

        _stopStatusRefreshTimer();
        SpeedrunTimerController.Stop();
        Subnautica100TrackerOverlayController.Stop();
        DebugTelemetryController.Stop();
        GameEventDocumenter.Stop();
        GameProcessMonitor.Stop();
    }

    public void PauseForFolderSwitch()
    {
        if (!_started || _pausedForFolderSwitch)
            return;

        _pausedForFolderSwitch = true;
        _stopStatusRefreshTimer();
        DebugTelemetryController.Stop();
        GameEventDocumenter.Stop();
    }

    public void ResumeAfterFolderSwitch()
    {
        if (!_started || !_pausedForFolderSwitch)
            return;

        _pausedForFolderSwitch = false;
        GameEventDocumenter.Start();
        DebugTelemetryController.Start();
        _startStatusRefreshTimer();
    }
}
