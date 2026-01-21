namespace SubnauticaLauncher.Updater
{
    public sealed class UpdateInfo
    {
        public Version Version { get; init; } = new Version(0, 0, 0);
        public string ZipUrl { get; init; } = string.Empty;
    }
}