using System.Collections.Generic;

namespace SubnauticaLauncher.Gameplay
{
    public sealed record GameplayUnlockSnapshot(
        HashSet<int> BlueprintTechTypes,
        HashSet<string> DatabankEntries);
}
