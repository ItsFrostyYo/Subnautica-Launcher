namespace SubnauticaLauncher.Versions;

public sealed class VersionInstallDefinition : GameVersionInstallDefinition
{
    public const int AppId = 264710;
    public const int DepotId = 264712;

    public VersionInstallDefinition(string id, string displayName, long manifestId)
        : base(id, displayName, manifestId, AppId, DepotId)
    {
    }
}
