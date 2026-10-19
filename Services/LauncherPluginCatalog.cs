using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

public static class LauncherPluginCatalog
{
    public const string SubnauticaResetMacroId = "subnautica-reset-macro";

    public static IReadOnlyList<LauncherPluginDefinition> All { get; } =
    [
        new(
            SubnauticaResetMacroId,
            "Subnautica Marco",
            "ItsFrostiSR",
            "Fully Configurable Subnautica Macro for Faster and Automated Resetting for Speedrunning. Supports Subnautica 2018, 2021, 2023 and 2025 Versions and Speedrunning Mod, also has support for Explosion Time Resetting.",
            "SubnauticaMarco",
            "LiveSplit.SubnauticaMarco.dll",
            "Subnautica Reset Macro",
            ["SubnauticaResetMacro"],
            new Uri("https://raw.githubusercontent.com/ItsFrostyYo/Subnautica_Autosplitter/LiveSplit.Subnautica/Components/SubnauticaMarco.Updates.xml"),
            new Uri("https://raw.githubusercontent.com/ItsFrostyYo/Subnautica_Autosplitter/LiveSplit.Subnautica/"))
    ];
}
