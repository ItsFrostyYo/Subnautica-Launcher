using System.Collections.Generic;

namespace SubnauticaLauncher.Updates
{
    public static class Updates
    {
        public static readonly string CurrentVersion = "1.0.2";

        public static readonly UpdateEntry[] History =
        {
            new UpdateEntry
            {
                Version = "1.0.2",
                Title = "General Improvements Update",
                Date = "Jan 23, 2026",
                Changes = new[]
                {
                    "Update Logging",
                    "Reset Macros UI",
                    "All Downloadable Versions"
                }
            },

            new UpdateEntry
            {
                Version = "1.0.1",
                Title = "UI Update",
                Date = "Jan 23, 2026",
                Changes = new[]
                {
                    "Launcher Auto Updates",
                    "Can Add Existing Versions",
                    "General UI polish and layout cleanup"
                }
            },

            new UpdateEntry
            {
                Version = "1.0.0",
                Title = "Initial Release",
                Date = "Jan 22, 2026",
                Changes = new[]
                {
                    "Initial launcher release",
                    "Version installation and management",
                    "Basic UI framework",
                }
            }
        };
    }
}