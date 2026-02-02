namespace SubnauticaLauncher.Versions;

public interface IVersionInstallDefinition
{
    string Id { get; }
    string DisplayName { get; }
    long ManifestId { get; }

    int AppId { get; }
    int DepotId { get; }
}