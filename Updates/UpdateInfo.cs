using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

public sealed class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0, 0);
    public string DownloadUrl { get; init; } = "";
}