using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher
{
    public static class SteamSession
    {
        private static readonly string SessionFile =
            Path.Combine(AppPaths.DataPath, "steam_session.txt");

        public static bool IsLoggedIn { get; private set; }
        public static string Username { get; private set; } = string.Empty;

        public static void SetLoggedIn(string username)
        {
            IsLoggedIn = true;
            Username = username ?? string.Empty;

            Directory.CreateDirectory(AppPaths.DataPath);
            File.WriteAllText(SessionFile, Username);
        }

        public static void LoadFromDisk()
        {
            if (!File.Exists(SessionFile))
                return;

            Username = File.ReadAllText(SessionFile).Trim();
            IsLoggedIn = !string.IsNullOrWhiteSpace(Username);
        }

        public static void Clear()
        {
            IsLoggedIn = false;
            Username = string.Empty;

            if (File.Exists(SessionFile))
                File.Delete(SessionFile);
        }
    }
}