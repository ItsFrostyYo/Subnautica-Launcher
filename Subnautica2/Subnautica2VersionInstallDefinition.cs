using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.Subnautica2;

public sealed class Subnautica2VersionInstallDefinition : GameVersionInstallDefinition
{
    public const int AppId = 1962700;

    public const int DepotId = 1962701;
    public long? BuildChangelist { get; }

    public Subnautica2VersionInstallDefinition(
        string id,
        string displayName,
        long manifestId,
        long? buildChangelist = null)
        : base(id, displayName, manifestId, AppId, DepotId)
    {
        BuildChangelist = buildChangelist;
    }
}
