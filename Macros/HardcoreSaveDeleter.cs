using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

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

            return GetSavedGamesPaths(gameRoot)
                .SelectMany(savedGamesPath => Directory.GetDirectories(savedGamesPath, "slot*")
                    .Where(IsSlotDirectory)
                    .Select(path => new { Path = path, Timestamp = GetSlotTimestamp(path) }))
                .OrderByDescending(x => x.Timestamp)
                .Select(x => x.Path)
                .FirstOrDefault(IsHardcoreSlot);
        }

        public static async Task DeleteSlotAfterDelayAsync(
            string? slotPath,
            int delayMs = 1500,
            ResetMacroLogChannel? logChannel = null)
        {
            if (string.IsNullOrWhiteSpace(slotPath))
                return;

            await Task.Delay(delayMs);

            try
            {
                if (Directory.Exists(slotPath))
                {
                    Directory.Delete(slotPath, recursive: true);
                    LogInfo(logChannel, $"Hardcore save deleted: {slotPath}");
                }
            }
            catch (Exception ex)
            {
                LogException(logChannel, ex, "Hardcore save delete failed");
            }
        }

        public static int DeleteAllHardcoreSaves(IEnumerable<string> gameRoots)
        {
            int deleted = 0;

            foreach (var root in gameRoots)
            {
                foreach (var slot in GetHardcoreSlots(root))
                {
                    try
                    {
                        if (Directory.Exists(slot))
                        {
                            Directory.Delete(slot, recursive: true);
                            deleted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Exception(ex, $"Hardcore save delete failed: {slot}");
                    }
                }
            }

            return deleted;
        }

        private static IEnumerable<string> GetHardcoreSlots(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                return Enumerable.Empty<string>();

            return GetSavedGamesPaths(gameRoot)
                .SelectMany(savedGamesPath => Directory.GetDirectories(savedGamesPath, "slot*"))
                .Where(IsSlotDirectory)
                .Where(IsHardcoreSlot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<string> GetSavedGamesPaths(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                return Enumerable.Empty<string>();

            LauncherGameProfile? detectedProfile = LauncherGameProfiles.DetectFromFolder(gameRoot);
            string primaryFolder = detectedProfile?.SaveDataFolderName
                ?? LauncherGameProfiles.Subnautica.SaveDataFolderName;

            var candidates = new List<string>
            {
                Path.Combine(gameRoot, primaryFolder, "SavedGames")
            };

            candidates.AddRange(
                LauncherGameProfiles.All
                    .Select(profile => Path.Combine(gameRoot, profile.SaveDataFolderName, "SavedGames")));

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists)
                .ToArray();
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

        private static void LogInfo(ResetMacroLogChannel? channel, string message)
        {
            if (channel.HasValue)
                ResetMacroLogger.Info(channel.Value, message);
            else
                Logger.Log(message);
        }

        private static void LogException(ResetMacroLogChannel? channel, Exception ex, string context)
        {
            if (channel.HasValue)
                ResetMacroLogger.Exception(channel.Value, ex, context);
            else
                Logger.Exception(ex, context);
        }
    }
}
