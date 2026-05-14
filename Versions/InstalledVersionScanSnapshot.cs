using SubnauticaLauncher.Core;
using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Enums;
using System.Linq;

namespace SubnauticaLauncher.Versions;

internal sealed class InstalledVersionScanSnapshot
{
    public required LauncherGameProfile SubnauticaProfile { get; init; }
    public required LauncherGameProfile BelowZeroProfile { get; init; }
    public required LauncherGameProfile Subnautica2Profile { get; init; }
    public required IReadOnlyList<InstalledVersion> SubnauticaVersions { get; init; }
    public required IReadOnlyList<BZInstalledVersion> BelowZeroVersions { get; init; }
    public required IReadOnlyList<InstalledVersion> Subnautica2Versions { get; init; }
    public bool MetadataRepaired { get; init; }

    public LauncherGameProfile GetProfile(LauncherGame game)
    {
        return game switch
        {
            LauncherGame.BelowZero => BelowZeroProfile,
            LauncherGame.Subnautica2 => Subnautica2Profile,
            _ => SubnauticaProfile
        };
    }

    public IReadOnlyList<InstalledVersion> GetVersions(LauncherGame game)
    {
        return game switch
        {
            LauncherGame.BelowZero => BelowZeroVersions.Cast<InstalledVersion>().ToList(),
            LauncherGame.Subnautica2 => Subnautica2Versions,
            _ => SubnauticaVersions
        };
    }
}
