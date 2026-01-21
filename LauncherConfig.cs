using System.IO;
using System.Text.Json;

namespace SubnauticaLauncher
{
    public class LauncherConfig
    {
        public bool IsInitialized { get; set; }

        private static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_config.json");

        public static LauncherConfig Load()
        {
            if (!File.Exists(ConfigPath))
                return new LauncherConfig { IsInitialized = false };

            return JsonSerializer.Deserialize<LauncherConfig>(
                File.ReadAllText(ConfigPath))!;
        }

        public void Save()
        {
            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}