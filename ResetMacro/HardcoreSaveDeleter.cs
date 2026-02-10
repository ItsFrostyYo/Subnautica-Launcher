using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SubnauticaLauncher;

namespace SubnauticaLauncher.Macros
{
    public static class HardcoreSaveDeleter
    {
        private const int HardcoreGameModeValue = 2;

        public static string? GetLatestHardcoreSlotToDelete(string gameRoot, GameMode mode)
        {
            LauncherSettings.Load();

            if (!LauncherSettings.Current.HardcoreSaveDeleterEnabled)
                return null;

            if (mode != GameMode.Hardcore)
                return null;

            string savedGamesPath = Path.Combine(gameRoot, "SNAppData", "SavedGames");
            if (!Directory.Exists(savedGamesPath))
                return null;

            var latestSlot = Directory.GetDirectories(savedGamesPath, "slot*")
                .Where(IsSlotDirectory)
                .Select(path => new { Path = path, Timestamp = GetSlotTimestamp(path) })
                .OrderByDescending(x => x.Timestamp)
                .FirstOrDefault();

            if (latestSlot == null)
                return null;

            return IsHardcoreSlot(latestSlot.Path) ? latestSlot.Path : null;
        }

        public static async Task DeleteSlotAfterDelayAsync(string? slotPath, int delayMs = 1500)
        {
            if (string.IsNullOrWhiteSpace(slotPath))
                return;

            await Task.Delay(delayMs);

            try
            {
                if (Directory.Exists(slotPath))
                {
                    Directory.Delete(slotPath, recursive: true);
                    Logger.Log($"Hardcore save deleted: {slotPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Hardcore save delete failed");
            }
        }

        private static bool IsSlotDirectory(string path)
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("slot", StringComparison.OrdinalIgnoreCase))
                return false;

            string suffix = name.Substring(4);
            return suffix.Length > 0 && suffix.All(char.IsDigit);
        }

        private static DateTime GetSlotTimestamp(string slotPath)
        {
            try
            {
                string gameInfo = Path.Combine(slotPath, "gameinfo.json");
                if (File.Exists(gameInfo))
                    return File.GetLastWriteTimeUtc(gameInfo);
            }
            catch
            {
                // fallback below
            }

            try
            {
                return Directory.GetCreationTimeUtc(slotPath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static bool IsHardcoreSlot(string slotPath)
        {
            string gameInfo = Path.Combine(slotPath, "gameinfo.json");
            if (!File.Exists(gameInfo))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(gameInfo));
                if (doc.RootElement.TryGetProperty("gameMode", out var modeProp) &&
                    modeProp.TryGetInt32(out int mode))
                {
                    return mode == HardcoreGameModeValue;
                }
            }
            catch
            {
                // ignore bad json
            }

            return false;
        }
    }
}
