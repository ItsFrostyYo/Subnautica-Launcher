using System.Reflection;

namespace SubnauticaLauncher.Updates;

public static class Updates
{
    public static readonly string CurrentVersion = GetAssemblyVersion();
    public static readonly string DisplayVersion = $"v{CurrentVersion}";

    private static string GetAssemblyVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
            return TrimInformationalVersion(info.InformationalVersion);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version == null)
            return "0.0.0";

        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string TrimInformationalVersion(string value)
    {
        // Strip SemVer build metadata like "1.1.0+githash" so UI shows only major.minor.build
        int plusIndex = value.IndexOf('+');
        return plusIndex >= 0 ? value[..plusIndex] : value;
    }

    public static readonly UpdateEntry[] History =
    {
          new UpdateEntry
    {
        Version = "2.2.2",
        Title = "Code Update",
        Date = "Feb 16, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Code Cleanup and Updates",
            "Small UI Updates",
            "Improved Memory Reading for Improved Gameplay Reading"
        }
    },
          
          new UpdateEntry
    {
        Version = "2.2.1",
        Title = "Biome Tracker Hotfix",
        Date = "Feb 15, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {
            "Fixed All Biomes Databanks and Blueprints",
            "Updated Biome Tracker UI"
        }
    },
          
          new UpdateEntry
    {
        Version = "2.2.0",
        Title = "100% Tracker Additions",
        Date = "Feb 14, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {
            "Tracks Unlocked Blueprints Per Biome",
            "Full Customization for the 100% Tracker"
        }
    },

         new UpdateEntry
    {
        Version = "2.1.1",
        Title = "Small Hotfixes",
        Date = "Feb 13, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {
            "Hotfixed 100% Speedrun Tracker",
            "Fixes some UI and cleaned up some code"
        }
    },

        new UpdateEntry
    {
        Version = "2.1.0",
        Title = "Speedrun Tracking",
        Date = "Feb 13, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {            
            "Added a 100% Speedrun Tracker for tracking Blueprints and Entries (493 Total)",
            "100% Speedrun Tracker Overlay Toggle"        
        }
    },
         
         new UpdateEntry
    {
        Version = "2.0.1",
        Title = "Explosion Overlay fix",
        Date = "Feb 13, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {            
            "Explosion Reset Overlay updated to a full real overlay for subnautica"        
        }
    },
         
         new UpdateEntry
    {
        Version = "2.0.0",
        Title = "Fully Combined Subnautica and Below Zero Launcher",
        Date = "Feb 11, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Subnuatica and Below Zero Fully Supported and Combined into a single Launcher",
            "All Original Managing, Launching, Downloading of Version Stays the Same",
            "All Tools Stay the Same and have the Improvements of Past Updates"        
        }
    },
         
         new UpdateEntry
    {
        Version = "1.7.0",
        Title = "Explosion Reset Macro Full Implementations",
        Date = "Feb 11, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {
            "Explosion Reset Macro can do 2018, 2021, 2023 and 2025/all relevant versions",
            "Explosion Reset Macro uses Autosplitter Explosion Time Code"        
        }
    },
         
         new UpdateEntry
    {
        Version = "1.6.2",
        Title = "Delete Saves Button",
        Date = "Feb 10, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Added Delete All Hardcore Saves Button",
            "Added Delete All Hardcore Saves Window"
        }
    },

        new UpdateEntry
    {
        Version = "1.6.1",
        Title = "Hardcore Save Deleter Patch",
        Date = "Feb 10, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Now Only Deletes a Save File after one was made, prevents deleting non speedrun saves."       
        }
    },
         
         new UpdateEntry
    {
        Version = "1.6.0",
        Title = "Hardcore Save Deleter",
        Date = "Feb 10, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Added Hardcore Save Deleter for Subnautica and Below Zero",
            "Deletes and Recent Hardcore Saves after use of Reset Macro with Harcore Gamemode Selected",
            "Small UI Updates"
        }
    },
         
         new UpdateEntry
    {
        Version = "1.5.1",
        Title = "Internal Code Cleanup",
        Date = "Feb 10, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Unified all settings into one file",
            "Internal Workings slight change/cleanup"
        }
    },

        new UpdateEntry
    {
        Version = "1.5.0",
        Title = "Folder Renaming Full Fix",
        Date = "Feb 8, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Added back folder renaming on launcher close",
            "Added new toggle in settings",
            "Prevents Steam from replacing manually launched versions"
        }
    },

    new UpdateEntry
    {
        Version = "1.4.2",
        Title = "Folder Renaming Hotfix",
        Date = "Feb 8, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Temporarily removed folder renaming",
            "Steam overwrite prevention"
        }
    },

    new UpdateEntry
    {
        Version = "1.4.1",
        Title = "ExploTime Macro Hotfix",
        Date = "Feb 6, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Explosion Time reset macro consistency fix",
            "Toggleable macro UI and logging",
            "Replaced Updates tab with Info tab",
            "Small UI updates"
        }
    },

    new UpdateEntry
    {
        Version = "1.4.0",
        Title = "Below Zero Support",
        Date = "Feb 2, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Full Below Zero support",
            "Below Zero backgrounds",
            "Below Zero reset macros",
            "All Below Zero versions supported"
        }
    },

    new UpdateEntry
    {
        Version = "1.3.0",
        Title = "Explosion Time Tools",
        Date = "Feb 1, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {
            "Explosion Time reset support",
            "Improved version folder handling",
            "Additional logging",
            "UI improvements"
        }
    },

    new UpdateEntry
    {
        Version = "1.2.1",
        Title = "Reset Macro Hotfix",
        Date = "Jan 27, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Fixed folder management",
            "Finalized reset macros",
            "Launcher logging"
        }
    },

    new UpdateEntry
    {
        Version = "1.2.0",
        Title = "UI Overhaul & Reset Macros",
        Date = "Jan 27, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
        Changes = new[]
        {
            "Advanced speedrunning reset macros",
            "Reset macro UI",
            "Resizable window",
            "Custom title bar",
            "Icon added"
        }
    },

    new UpdateEntry
    {
        Version = "1.1.0",
        Title = "Version Management Update",
        Date = "Jan 25, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Updates tab",
            "Improved UI",
            "All versions downloadable"
        }
    },

    new UpdateEntry
    {
        Version = "1.0.1",
        Title = "General Improvements",
        Date = "Jan 23, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Launcher auto updates",
            "Add existing versions",
            "UI cleanup"
        }
    },

    new UpdateEntry
    {
        Version = "1.0.0",
        Title = "Initial Release",
        Date = "Jan 20, 2026 | Contributors (1) - ItsFrosti.",
        Changes = new[]
        {
            "Basic installation and management",
            "Background customization",
            "Initial UI"
        }
    }
};
}
