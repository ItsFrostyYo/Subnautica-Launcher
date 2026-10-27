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
    Uri UpdateBaseUri,
    string HostGroup,
    int DefaultLayoutOrder);

public sealed record LauncherPluginSnapshot(
    LauncherPluginDefinition Definition,
    bool IsInstalled,
    bool IsEnabled,
    bool IsLoaded,
    Version? InstalledVersion,
    Version? LatestVersion,
    string? Error,
    int LayoutOrder)
{
    public bool HasUpdate => IsInstalled && InstalledVersion is not null && LatestVersion is not null && LatestVersion > InstalledVersion;
}
