using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Mods;

public sealed class ModBundleDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string BundleZipFileName { get; init; }
    public required string DownloadUrl { get; init; }
    public required string InstallRelativePath { get; init; }
    public required bool PreserveTopLevelDirectory { get; init; }
    public required IReadOnlyList<string> RequiredRelativePaths { get; init; }
}

public sealed class ModDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required LauncherGame Game { get; init; }
    public required Version PackageVersion { get; init; }
    public required string RuntimeDisplayName { get; init; }
    public required string InstallRootRelativePath { get; init; }
    public required string RuntimeRootRelativePath { get; init; }
    public required IReadOnlyList<string> DetectionRelativePaths { get; init; }
    public required string VersionMarkerRelativePath { get; init; }
    public required IReadOnlyList<ModBundleDefinition> BundleParts { get; init; }
    public required IReadOnlyList<string> RemovalTargets { get; init; }
    public required IReadOnlyList<string> PreservedRelativePaths { get; init; }
    public required IReadOnlyList<string> StaleCleanupRelativeRoots { get; init; }
    public required IReadOnlyList<string> DetectedDisplayNames { get; init; }
    public required IReadOnlyList<string> ManagedModFolderNames { get; init; }
}
