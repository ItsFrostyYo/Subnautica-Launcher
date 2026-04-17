namespace SubnauticaLauncher.Updates;

internal sealed class BackgroundCheckCoordinator
{
    private readonly TimeSpan _launcherUpdateFreshness;
    private readonly TimeSpan _modUpdateFreshness;
    private DateTime _lastLauncherUpdateCheckUtc = DateTime.MinValue;
    private DateTime _lastModUpdateCheckUtc = DateTime.MinValue;

    public BackgroundCheckCoordinator(TimeSpan launcherUpdateFreshness, TimeSpan modUpdateFreshness)
    {
        _launcherUpdateFreshness = launcherUpdateFreshness;
        _modUpdateFreshness = modUpdateFreshness;
    }

    public bool DeferLauncherPromptsUntilRestart { get; private set; }

    public void MarkLauncherCheckStarted()
    {
        _lastLauncherUpdateCheckUtc = DateTime.UtcNow;
    }

    public void MarkModCheckStarted()
    {
        _lastModUpdateCheckUtc = DateTime.UtcNow;
    }

    public void DeferLauncherPrompts()
    {
        DeferLauncherPromptsUntilRestart = true;
    }

    public bool ShouldRunLauncherUpdateCheck(bool canPromptForUpdate)
    {
        if (DeferLauncherPromptsUntilRestart || !canPromptForUpdate)
            return false;

        return DateTime.UtcNow - _lastLauncherUpdateCheckUtc >= _launcherUpdateFreshness;
    }

    public bool ShouldRunModUpdateCheck(bool canPromptForUpdate)
    {
        if (!canPromptForUpdate)
            return false;

        return DateTime.UtcNow - _lastModUpdateCheckUtc >= _modUpdateFreshness;
    }
}
