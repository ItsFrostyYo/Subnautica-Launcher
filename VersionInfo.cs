namespace SubnauticaLauncher
{
    public class VersionInfo
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
}