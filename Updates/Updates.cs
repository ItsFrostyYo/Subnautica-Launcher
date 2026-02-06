using System.Collections.Generic;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Updates;

public static class Updates
{
    public static readonly string CurrentVersion = "1.0.7";

    public static readonly UpdateEntry[] History =
    {
        new UpdateEntry
        {
            Version = "1.0.7",
            Title = "ExploTime Macro and UI Hotfix",
            Date = "Feb 6, 2026 | Contributors (1) - ItsFrosti.",
            Changes = new[]
            {
                "Fixed Explosion Time Reset Macro Consistancy (Still 2018 and 2023 Only)",                                      
                "Toggleable Explosion Time Reset Macro Display",                                      
                "Toggleable Explosion Time Macro Time+Resets Logger",                                                                          
                "Replaced Updates Tab with Info Tab (Updates and Additional Info)",
                "Small UI Updates"
            }
        },

        new UpdateEntry
        {
            Version = "1.0.6",
            Title = "Below Zero Support",
            Date = "Feb 2, 2026 | Contributors (1) - ItsFrosti.",
            Changes = new[]
            {              
                "Below Zero Full Support",
                "Below Zero Backgrounds",
                "Below Zero Reset Macro",
                "All Below Zero Versions",
                "Only Basic Tools and UI for Below Zero"
            }
        },

        new UpdateEntry
        {
            Version = "1.0.5",
            Title = "Cleanup and Explosion Time Addition",
            Date = "Feb 1, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
            Changes = new[]
            {
                "Ensures Needed Files are Always Installed and Available",
                "Small Fixes and UI Updates",
                "Added Edit Button (Delete and Rename Buttons Replaced)",
                "Open Folder now opens the Selected Version",
                "Option to Reset for Explosion Times (2018 and 2023 ONLY)",
                "Press Reset Hotkey 2nd Time to Cancel Explosion Resetting",
                "+1 Backgrounds",
                "More Logging (Launcher.log)"

            }
        },

        new UpdateEntry
        {
            Version = "1.0.4",
            Title = "Reset Macro and Folder Management Hotfix",
            Date = "Jan 27, 2026 | Contributors (1) - ItsFrosti.",
            Changes = new[]
            {
                "Fixed Folder Management",
                "Finalized Reset Macros (EA Only for 2017)",
                "Added Logging (Launcher.log)"               

            }
        },

        new UpdateEntry
        {
            Version = "1.0.3",
            Title = "UI Overhall and Reset Macros Update",
            Date = "Jan 27, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
            Changes = new[]
            {
                "Added the Advanced Speedrunning Reset Macros",
                "Reset Macros UI",                
                "UI Updated to Prioritize Readability",
                "Window is now Resizable",
                "Background Folder Removed (Now Fetches Backgrounds)",
                "More Background Options (Still Custom Option)",
                "Cutom Title Bar",
                "Added Icon"

            }
        },

        new UpdateEntry
        {
            Version = "1.0.2",
            Title = "Versions Update",
            Date = "Jan 25, 2026 | Contributors (1) - ItsFrosti.",
            Changes = new[]
            {
                "Updates Tab",
                "Improved UI",
                "All Versions Now Downloadable (Most Stable Per Month, Per Year)"
            }
        },

        new UpdateEntry
        {
            Version = "1.0.1",
            Title = "General Improvements",
            Date = "Jan 23, 2026 | Contributors (1) - ItsFrosti.",
            Changes = new[]
            {
                "Launcher Auto Updates (Next to SNLUpdater.exe)",
                "Can Add Existing Versions",
                "General UI Cleanup"
            }
        },

        new UpdateEntry
        {
            Version = "1.0.0",
            Title = "Initial Release",
            Date = "Jan 20, 2026 | Contributors (1) - ItsFrosti.",
            Changes = new[]
            {
                "Basic Background Customizations",
                "Installation and Management",
                "Basic UI",
            }
        }
    };
}