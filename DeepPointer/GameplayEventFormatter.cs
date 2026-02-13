using System;
using System.Globalization;
using System.Text;

namespace SubnauticaLauncher.Gameplay
{
    internal static class GameplayEventFormatter
    {
        public static GameplayEvent FormatForOutput(GameplayEvent evt)
        {
            string formattedKey = FormatKey(evt.Type, evt.Key);
            if (formattedKey == evt.Key)
                return evt;

            return new GameplayEvent
            {
                TimestampUtc = evt.TimestampUtc,
                Game = evt.Game,
                ProcessId = evt.ProcessId,
                Type = evt.Type,
                Key = formattedKey,
                Delta = evt.Delta,
                Source = evt.Source
            };
        }

        private static string FormatKey(GameplayEventType type, string rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
                return rawKey;

            if (type is GameplayEventType.BlueprintUnlocked
                or GameplayEventType.ItemCrafted
                or GameplayEventType.ItemPickedUp
                or GameplayEventType.ItemDropped)
            {
                return FormatTechType(rawKey);
            }

            if (type == GameplayEventType.DatabankEntryUnlocked)
            {
                if (int.TryParse(rawKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    return FormatTechType(rawKey);

                return HumanizeIdentifier(rawKey);
            }

            return rawKey;
        }

        private static string FormatTechType(string rawKey)
        {
            if (!int.TryParse(rawKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int techType))
                return rawKey;

            if (TechTypeNames.TryGetName(techType, out string enumName))
                return $"{HumanizeIdentifier(enumName)} ({techType})";

            return $"TechType {techType}";
        }

        private static string HumanizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            var cleaned = value.Replace('_', ' ').Replace('-', ' ').Trim();
            if (cleaned.Length == 0)
                return value;

            var sb = new StringBuilder(cleaned.Length + 8);
            char previous = '\0';

            foreach (char current in cleaned)
            {
                if (sb.Length > 0 && ShouldInsertSpace(previous, current))
                    sb.Append(' ');

                sb.Append(current);
                previous = current;
            }

            return sb.ToString();
        }

        private static bool ShouldInsertSpace(char previous, char current)
        {
            if (previous == '\0' || previous == ' ')
                return false;

            if (char.IsDigit(previous) && char.IsLetter(current))
                return true;

            if (char.IsLetter(previous) && char.IsDigit(current))
                return true;

            return char.IsLower(previous) && char.IsUpper(current);
        }
    }
}
