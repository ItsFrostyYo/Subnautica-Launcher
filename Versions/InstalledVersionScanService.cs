using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Core;

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

            List<InstalledVersion> subnautica = InstalledVersionFileService.LoadInstalledFromRoots(
                commonPaths,
                "Version.info",
                "IsSubnauticaLauncherVersion",
                InstalledVersion.FromInfo);
            cancellationToken.ThrowIfCancellationRequested();

            List<BZInstalledVersion> belowZero = InstalledVersionFileService.LoadInstalledFromRoots(
                commonPaths,
                "BZVersion.info",
                "IsBelowZeroLauncherVersion",
                BZInstalledVersion.FromInfo);
            cancellationToken.ThrowIfCancellationRequested();

            return new InstalledVersionScanSnapshot
            {
                SubnauticaVersions = subnautica,
                BelowZeroVersions = belowZero,
                MetadataRepaired = repairMetadata
            };
        }, cancellationToken);
    }
}
