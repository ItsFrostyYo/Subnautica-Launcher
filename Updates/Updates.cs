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
            Date = "Jan 31, 2026",
            Changes = new[]
            {
                "Small Fixes and UI Updates",
                "Added Edit Button instead of Delete and Rename",
                "Open Folder now opens the Selected Version",
                "Added Explosion Time Reset Macro",
                "More Backgrounds",
                "More Logging"

            }
        },

        new UpdateEntry
        {
            Version = "1.0.4",
            Title = "Reset Macro and Folder Management Hotfix",
            Date = "Jan 27, 2026",
            Changes = new[]
            {
                "Fixed Folder Management",
                "Finalized Reset Macros (EA Only for 2017)",
                "Added Logging"               

            }
        },

        new UpdateEntry
        {
            Version = "1.0.3",
            Title = "UI Overhall and Reset Macros Update",
            Date = "Jan 27, 2026",
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
            Date = "Jan 25, 2026",
            Changes = new[]
            {
                "Updates Tab",
                "Improved UI",
                "All Downloadable Versions"
            }
        },

        new UpdateEntry
        {
            Version = "1.0.1",
            Title = "General Improvements",
            Date = "Jan 23, 2026",
            Changes = new[]
            {
                "Launcher Auto Updates",
                "Can Add Existing Versions",
                "General UI Cleanup"
            }
        },

        new UpdateEntry
        {
            Version = "1.0.0",
            Title = "Initial Release",
            Date = "Jan 20, 2026",
            Changes = new[]
            {
                "Basic Background Customizations",
                "Installation and Management",
                "Basic UI",
            }
        }
    };
}