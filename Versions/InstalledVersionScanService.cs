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

            return new InstalledVersionScanSnapshot
            {
                SubnauticaProfile = subnauticaProfile,
                BelowZeroProfile = belowZeroProfile,
                SubnauticaVersions = subnautica,
                BelowZeroVersions = belowZero,
                MetadataRepaired = repairMetadata
            };
        }, cancellationToken);
    }
}
