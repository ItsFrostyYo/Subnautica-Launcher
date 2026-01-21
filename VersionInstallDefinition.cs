namespace SubnauticaLauncher
{
    public sealed class VersionInstallDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public long ManifestId { get; }

        public const int AppId = 264710;
        public const int DepotId = 264712;

        public VersionInstallDefinition(string id, string displayName, long manifestId)
        {
            Id = id;
            DisplayName = displayName;
            ManifestId = manifestId;
        }
    }
}