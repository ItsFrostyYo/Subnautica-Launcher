using SubnauticaLauncher.Models;

namespace SubnauticaLauncher.Services;

public static class LauncherPluginCatalog
{
    public const string SubnauticaComponentGroup = "subnautica-components";
    public const string SubnauticaResetMacroId = "subnautica-reset-macro";
    public const string SubnauticaTrackerId = "subnautica-tracker";

    // Add compatible components here. Runtime hosting, native settings, updates,
    // visual layout composition, and ordering are all driven by this list.
    public static IReadOnlyList<LauncherPluginDefinition> All { get; } =
    [
        new(
            SubnauticaResetMacroId,
            "Subnautica Reset Macro",
            "Author: ItsFrostiSR",
            "Fully Configurable Subnautica Macro for Faster and Automated Resetting for Speedrunning. Supports Subnautica 2018, 2021, 2023 and 2025 Versions and Speedrunning Mod, also has support for Explosion Time Resetting.",
            "SubnauticaMarco",
            "LiveSplit.SubnauticaMarco.dll",
            "Subnautica Reset Macro",
            ["SubnauticaResetMacro"],
            new Uri("https://raw.githubusercontent.com/ItsFrostyYo/Subnautica_Autosplitter/LiveSplit.Subnautica/Components/SubnauticaMarco.Updates.xml"),
            new Uri("https://raw.githubusercontent.com/ItsFrostyYo/Subnautica_Autosplitter/LiveSplit.Subnautica/"),
            SubnauticaComponentGroup,
            0),
        new(
            SubnauticaTrackerId,
            "Subnautica Tracker",
            "Author: ItsFrostiSR",
            "Configurable Subnautica Speedrunning Tracker for Progression, Damage Taken, Aurora Explosion Time, and Lifepod 4 Transmission, with manual Missing Entry Reports. Supports 2018, 2021, 2023 and 2025 Versions.",
            "SubnauticaTracker",
            "LiveSplit.SubnauticaTracker.dll",
            "Subnautica Tracker",
            [],
            new Uri("https://raw.githubusercontent.com/ItsFrostyYo/Subnautica_Autosplitter/LiveSplit.Subnautica/Components/SubnauticaTracker.Updates.xml"),
            new Uri("https://raw.githubusercontent.com/ItsFrostyYo/Subnautica_Autosplitter/LiveSplit.Subnautica/"),
            SubnauticaComponentGroup,
            100)
    ];
}
