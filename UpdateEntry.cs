namespace SubnauticaLauncher.Updates
{
    public class UpdateEntry
    {
        public string Version { get; init; } = "";
        public string Title { get; init; } = "";
        public string Date { get; init; } = "";
        public string[] Changes { get; init; } = System.Array.Empty<string>();
    }
}