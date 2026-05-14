using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.Subnautica2;

public sealed class Subnautica2VersionInstallDefinition : GameVersionInstallDefinition
{
    public const int AppId = 1962700;

    // TODO: Replace with the real depot id when public depot/version downloading is wired.
    public const int DepotId = 1962702;

    public Subnautica2VersionInstallDefinition(string id, string displayName, long manifestId)
        : base(id, displayName, manifestId, AppId, DepotId)
    {
    }
}
