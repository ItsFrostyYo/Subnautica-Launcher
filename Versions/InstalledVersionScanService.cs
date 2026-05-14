using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using System.Linq;

namespace SubnauticaLauncher.Versions;

internal static class InstalledVersionScanService
{
    public static Task<InstalledVersionScanSnapshot> ScanAsync(
        bool repairMetadata = false,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<string> commonPaths = AppPaths.SteamCommonPaths;
            if (commonPaths.Count == 0)
                commonPaths = new List<string> { AppPaths.SteamCommonPath };

            if (repairMetadata)
                InstalledVersionFileService.RepairMisplacedInfoFiles(commonPaths);

            LauncherGameProfile subnauticaProfile = LauncherGameProfiles.Get(LauncherGame.Subnautica);
            LauncherGameProfile belowZeroProfile = LauncherGameProfiles.Get(LauncherGame.BelowZero);
            LauncherGameProfile subnautica2Profile = LauncherGameProfiles.Get(LauncherGame.Subnautica2);

            List<InstalledVersion> subnautica = InstalledVersionStore.LoadInstalledFromRoots(
                commonPaths,
                subnauticaProfile);
            cancellationToken.ThrowIfCancellationRequested();

            List<BZInstalledVersion> belowZero = InstalledVersionStore.LoadInstalledFromRoots(
                commonPaths,
                belowZeroProfile)
                .OfType<BZInstalledVersion>()
                .ToList();
            cancellationToken.ThrowIfCancellationRequested();

            List<InstalledVersion> subnautica2 = InstalledVersionStore.LoadInstalledFromRoots(
                commonPaths,
                subnautica2Profile);
            cancellationToken.ThrowIfCancellationRequested();

            return new InstalledVersionScanSnapshot
            {
                SubnauticaProfile = subnauticaProfile,
                BelowZeroProfile = belowZeroProfile,
                Subnautica2Profile = subnautica2Profile,
                SubnauticaVersions = subnautica,
                BelowZeroVersions = belowZero,
                Subnautica2Versions = subnautica2,
                MetadataRepaired = repairMetadata
            };
        }, cancellationToken);
    }
}
