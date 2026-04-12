using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Mods;

public sealed class ModDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required LauncherGame Game { get; init; }
    public required Version PackageVersion { get; init; }
    public required string BundleZipFileName { get; init; }
    public required string DownloadUrl { get; init; }
    public required IReadOnlyList<string> RemovalTargets { get; init; }
    public required IReadOnlyList<string> PreservedRelativePaths { get; init; }
}
