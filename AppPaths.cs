using System;
using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher
{
    public static class AppPaths
    {
        // =========================
        // BASE
        // =========================
        public static readonly string BasePath =
            AppContext.BaseDirectory;

        // =========================
        // TOOLS (DepotDownloader)
        // =========================
        public static readonly string ToolsPath =
            Path.Combine(BasePath, "tools");

        public static readonly string DepotDownloaderExe =
            Path.Combine(ToolsPath, "DepotDownloader.exe");

        // =========================
        // STEAM
        // =========================
        public static readonly string SteamCommonPath =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Steam",
        "steamapps",
        "common"
    );

        // =========================
        // LAUNCHER DATA
        // =========================
        public static readonly string DataPath =
            Path.Combine(BasePath, "data");

        public static readonly string BackgroundsPath =
            Path.Combine(DataPath, "backgrounds");

        public static readonly string LogsPath =
            Path.Combine(BasePath, "logs");

        public static readonly string LogFile =
            Path.Combine(LogsPath, "launcher.log");
    }
}