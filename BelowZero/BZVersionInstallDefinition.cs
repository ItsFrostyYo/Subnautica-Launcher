using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.BelowZero;

public sealed class BZVersionInstallDefinition : GameVersionInstallDefinition
{
    public const int AppId = 848450;
    public const int DepotId = 848452;

    public BZVersionInstallDefinition(string id, string displayName, long manifestId)
        : base(id, displayName, manifestId, AppId, DepotId)
    {
    }
}
