namespace SubnauticaLauncher.Mods;

public enum SubnauticaModEnvironment
{
    Vanilla,
    Unknown,
    BepInEx,
    QModManager,
    SpeedrunningMod
}

public enum ModItemKind
{
    BepInExPlugin,
    QMod,
    UnknownFolder,
    SpeedrunningMod
}

public sealed record InstalledModItem(
    string Id,
    ModItemKind Kind,
    string Name,
    string Author,
    string Version,
    string Description,
    string RemovalPath);

public sealed record ModLoaderItem(
    string Id,
    string Name,
    string Author,
    string Version,
    string Description,
    bool IsInstalled,
    bool CanInstall,
    string UnavailableReason);

public sealed record ModInstallationSnapshot(
    string GameFolder,
    SubnauticaModEnvironment Environment,
    string Status,
    IReadOnlyList<InstalledModItem> InstalledMods,
    IReadOnlyList<ModLoaderItem> Loaders,
    bool HasBepInEx,
    bool HasQModManager,
    bool HasSpeedrunningMod,
    bool HasUnknownModFiles,
    string Warning,
    string BepInExPluginsFolder);

internal sealed record DownloadableModPackage(
    string Id,
    string Name,
    string Version,
    string DownloadUrl,
    IReadOnlyList<string> RequiredPaths);
