namespace SubnauticaLauncher.Models;

public sealed record LauncherPluginDefinition(
    string Id,
    string Name,
    string Author,
    string Description,
    string InstallDirectoryName,
    string AssemblyFileName,
    string ComponentName,
    IReadOnlyList<string> LegacyInstallDirectoryNames,
    Uri UpdateManifestUri,
    Uri UpdateBaseUri);

public sealed record LauncherPluginSnapshot(
    LauncherPluginDefinition Definition,
    bool IsInstalled,
    bool IsEnabled,
    bool IsLoaded,
    Version? InstalledVersion,
    Version? LatestVersion,
    string? Error)
{
    public bool HasUpdate => IsInstalled && InstalledVersion is not null && LatestVersion is not null && LatestVersion > InstalledVersion;
}

public sealed class SubnauticaMacroPluginSettings
{
    public bool MacroEnabled { get; set; }
    public int Hotkey { get; set; } = 116;
    public int GameMode { get; set; }
    public bool ExplosionReset { get; set; }
    public int ExplosionPreset { get; set; }
    public int CustomMinimumSeconds { get; set; } = 2760;
    public int CustomMaximumSeconds { get; set; } = 2790;
}
