using SubnauticaLauncher.BelowZero;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.Mods;

public static class ModUpdateService
{
    public sealed record ModUpdateCandidate(
        InstalledVersion Version,
        LauncherGame Game,
        ModDefinition Mod,
        Version InstalledVersion,
        Version LatestVersion);

    public static async Task<IReadOnlyList<ModUpdateCandidate>> GetAvailableUpdatesAsync(
        IEnumerable<InstalledVersion> subnauticaVersions,
        IEnumerable<BZInstalledVersion> belowZeroVersions,
        CancellationToken cancellationToken = default)
    {
        await ModCatalog.EnsureLoadedAsync(cancellationToken);

        var updates = new List<ModUpdateCandidate>();

        foreach (InstalledVersion version in subnauticaVersions.Where(v => v.IsModded))
        {
            ModDefinition? mod = ModInstallerService.GetInstalledModDefinition(LauncherGame.Subnautica, version);
            Version? installedVersion = ModInstallerService.TryReadInstalledModVersion(version);
            if (mod == null || installedVersion == null)
                continue;

            if (installedVersion < mod.PackageVersion)
            {
                updates.Add(new ModUpdateCandidate(
                    version,
                    LauncherGame.Subnautica,
                    mod,
                    installedVersion,
                    mod.PackageVersion));
            }
        }

        foreach (BZInstalledVersion version in belowZeroVersions.Where(v => v.IsModded))
        {
            ModDefinition? mod = ModInstallerService.GetInstalledModDefinition(LauncherGame.BelowZero, version);
            Version? installedVersion = ModInstallerService.TryReadInstalledModVersion(version);
            if (mod == null || installedVersion == null)
                continue;

            if (installedVersion < mod.PackageVersion)
            {
                updates.Add(new ModUpdateCandidate(
                    version,
                    LauncherGame.BelowZero,
                    mod,
                    installedVersion,
                    mod.PackageVersion));
            }
        }

        return updates;
    }

    public static Task ApplyUpdatesAsync(
        IReadOnlyList<ModUpdateCandidate> updates,
        Func<string, Func<DepotInstallCallbacks, CancellationToken, Task>, bool?> showProgressDialog,
        CancellationToken cancellationToken = default)
    {
        foreach (ModUpdateCandidate update in updates)
        {
            string title = $"{update.Version.DisplayName} ({update.Mod.DisplayName})";

            bool? result = showProgressDialog(
                title,
                (callbacks, token) => ModInstallerService.InstallBundleAsync(
                    update.Mod,
                    update.Game,
                    update.Version.HomeFolder,
                    callbacks,
                    token));

            cancellationToken.ThrowIfCancellationRequested();

            if (result != true)
                break;

            if (update.Game == LauncherGame.Subnautica)
                VersionLoader.Save(update.Version);
            else if (update.Version is BZInstalledVersion bzVersion)
                BZVersionLoader.Save(bzVersion);
        }

        return Task.CompletedTask;
    }
}
