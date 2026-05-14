using System.Collections.Generic;

namespace SubnauticaLauncher.Subnautica2;

public static class Subnautica2VersionRegistry
{
    public static IReadOnlyList<Subnautica2VersionInstallDefinition> AllVersions { get; } =
        new List<Subnautica2VersionInstallDefinition>
        {
            new(
                "Subnautica2_May2026",
                "*First Release* May, 2026 (Early Access Launch)",
                0)
        };
}
