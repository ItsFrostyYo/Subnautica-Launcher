public sealed class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0, 0);
    public string DownloadUrl { get; init; } = "";
}