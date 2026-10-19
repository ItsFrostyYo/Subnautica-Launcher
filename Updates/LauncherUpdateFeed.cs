using System;

namespace SubnauticaLauncher.Updates;

public static class LauncherUpdateFeed
{
    private const string RepositoryRoot = "https://raw.githubusercontent.com/ItsFrostyYo/Subnautica-Launcher/main/";

    public static Uri ManifestUri => new(
        Environment.GetEnvironmentVariable("SNL_UPDATE_MANIFEST_URL")
        ?? RepositoryRoot + "Release/SNLUpdates.xml");

    public static Uri ContentBaseUri => new(
        Environment.GetEnvironmentVariable("SNL_UPDATE_CONTENT_BASE_URL")
        ?? RepositoryRoot);
}
