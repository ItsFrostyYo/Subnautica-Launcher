using SubnauticaLauncher.BelowZero;

namespace SubnauticaLauncher.Versions;

internal sealed class InstalledVersionScanSnapshot
{
    public required IReadOnlyList<InstalledVersion> SubnauticaVersions { get; init; }
    public required IReadOnlyList<BZInstalledVersion> BelowZeroVersions { get; init; }
    public bool MetadataRepaired { get; init; }
}
