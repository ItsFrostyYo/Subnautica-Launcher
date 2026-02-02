using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.BelowZero;

namespace SubnauticaLauncher.BelowZero;

public class BZVersionInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long BuildId { get; set; }
    public string InstallPath { get; set; } = "";
    public bool IsInstalled { get; set; }
    public long WinDepotManifestId { get; set; }
    public long VcRedistManifestId { get; set; }
    public override string ToString()
    {
        return DisplayName;
    }
}