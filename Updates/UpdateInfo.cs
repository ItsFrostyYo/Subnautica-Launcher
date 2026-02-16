namespace SubnauticaLauncher.Updates;

public sealed class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0, 0);
    public string ReleaseTag { get; init; } = "";
    public string ReleaseName { get; init; } = "";
    public string LauncherDownloadUrl { get; init; } = "";
    public string UpdaterDownloadUrl { get; init; } = "";
    public long UpdaterAssetSize { get; init; }
    public string? UpdaterSha256 { get; init; }
}
