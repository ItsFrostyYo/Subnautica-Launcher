using SubnauticaLauncher.Core;
using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Enums;
using System.Linq;

namespace SubnauticaLauncher.Versions;

internal sealed class InstalledVersionScanSnapshot
{
    public required LauncherGameProfile SubnauticaProfile { get; init; }
    public required LauncherGameProfile BelowZeroProfile { get; init; }
    public required IReadOnlyList<InstalledVersion> SubnauticaVersions { get; init; }
    public required IReadOnlyList<BZInstalledVersion> BelowZeroVersions { get; init; }
    public bool MetadataRepaired { get; init; }

    public LauncherGameProfile GetProfile(LauncherGame game)
    {
        return game == LauncherGame.BelowZero
            ? BelowZeroProfile
            : SubnauticaProfile;
    }

    public IReadOnlyList<InstalledVersion> GetVersions(LauncherGame game)
    {
        return game == LauncherGame.BelowZero
            ? BelowZeroVersions.Cast<InstalledVersion>().ToList()
            : SubnauticaVersions;
    }
}
