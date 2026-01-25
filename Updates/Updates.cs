using System.Collections.Generic;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Updates;

public static class Updates
{
    public static readonly string CurrentVersion = "1.0.3";

    public static readonly UpdateEntry[] History =
    {
        new UpdateEntry
        {
            Version = "1.0.3",
            Title = "Reset Macros Update",
            Date = "Jan 25, 2026",
            Changes = new[]
            {
                "Added Advanced Reset Macros",
                "Slight UI Updates",
                "Window Now Resizable"

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
            Date = "Jan 22, 2026",
            Changes = new[]
            {
                "Basic Background Customizations",
                "Installation and Management",
                "Basic UI",
            }
        }
    };
}