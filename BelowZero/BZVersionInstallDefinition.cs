public sealed class BZVersionInstallDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public long ManifestId { get; }

    public const int AppId = 848450;
    public const int DepotId = 848452;

    public BZVersionInstallDefinition(string id, string displayName, long manifestId)
    {
        Id = id;
        DisplayName = displayName;
        ManifestId = manifestId;
    }
}