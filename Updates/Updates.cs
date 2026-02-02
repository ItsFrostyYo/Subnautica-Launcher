using System.Collections.Generic;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Updates;

public static class Updates
{
    public static readonly string CurrentVersion = "1.0.5";

    public static readonly UpdateEntry[] History =
    {
        new UpdateEntry
        {
            Version = "1.0.5",
            Title = "Cleanup and Explosion Time Addition",
            Date = "Jan 31, 2026 | Contributors (2) - ItsFrosti, Sprinter_31.",
            Changes = new[]
            {
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