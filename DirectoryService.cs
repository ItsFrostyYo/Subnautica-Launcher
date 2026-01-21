using System;
using System.IO;

namespace SubnauticaLauncher
{
    public static class DirectoryService
    {
        public static string BaseDir =>
            AppDomain.CurrentDomain.BaseDirectory;

        public static string DataDir =>
            Path.Combine(BaseDir, "data");

        public static string SteamCmdDir =>
            Path.Combine(BaseDir, "steamcmd");

        public static string VersionsDir =>
            Path.Combine(BaseDir, "versions");

        public static string ModsDir =>
            Path.Combine(BaseDir, "mods");

        public static void Initialize()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(SteamCmdDir);
            Directory.CreateDirectory(VersionsDir);
            Directory.CreateDirectory(ModsDir);

            Logger.Log("Folders initialized.");
        }
    }
}