namespace SubnauticaLauncher.Versions;

public class GameVersionInstallDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public long ManifestId { get; }
    public int SteamAppId { get; }
    public int SteamDepotId { get; }

    public GameVersionInstallDefinition(
        string id,
        string displayName,
        long manifestId,
        int steamAppId,
        int steamDepotId)
    {
        Id = id;
        DisplayName = displayName;
        ManifestId = manifestId;
        SteamAppId = steamAppId;
        SteamDepotId = steamDepotId;
    }
}
