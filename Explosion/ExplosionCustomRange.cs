using System;

namespace SubnauticaLauncher.Explosion
{
    public static class ExplosionCustomRange
    {
        public const int MinimumAllowedSeconds = 46 * 60;
        public const int MaximumAllowedSeconds = (1 * 60 * 60) + (19 * 60) + 59;

        public static int Clamp(int totalSeconds)
        {
            return Math.Clamp(totalSeconds, MinimumAllowedSeconds, MaximumAllowedSeconds);
        }

        public static string FormatSeconds(int totalSeconds)
        {
            totalSeconds = Clamp(totalSeconds);
            TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
            return totalSeconds >= 3600
                ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
        }

        public static bool TryParseSeconds(string? text, out int totalSeconds)
        {
            totalSeconds = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] parts = text.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is not 2 and not 3)
                return false;

            if (!int.TryParse(parts[^1], out int seconds) || seconds < 0 || seconds > 59)
                return false;

            if (!int.TryParse(parts[^2], out int minutes) || minutes < 0)
                return false;

            int hours = 0;
            if (parts.Length == 3)
            {
                if (minutes > 59)
                    return false;

                if (!int.TryParse(parts[0], out hours) || hours < 0)
                    return false;
            }

            totalSeconds = (hours * 3600) + (minutes * 60) + seconds;
            return true;
        }

        public static bool TryParseAndValidate(
            string? minimumText,
            string? maximumText,
            out int minimumSeconds,
            out int maximumSeconds,
            out string errorMessage)
        {
            minimumSeconds = 0;
            maximumSeconds = 0;
            errorMessage = string.Empty;

            if (!TryParseSeconds(minimumText, out minimumSeconds))
            {
                errorMessage = "Minimum must be a valid time like 46:00 or 1:19:59.";
                return false;
            }

            if (!TryParseSeconds(maximumText, out maximumSeconds))
            {
                errorMessage = "Maximum must be a valid time like 46:30 or 1:19:59.";
                return false;
            }

            if (minimumSeconds < MinimumAllowedSeconds || minimumSeconds > MaximumAllowedSeconds)
            {
                errorMessage = "Minimum must stay between 46:00 and 1:19:59.";
                return false;
            }

            if (maximumSeconds < MinimumAllowedSeconds || maximumSeconds > MaximumAllowedSeconds)
            {
                errorMessage = "Maximum must stay between 46:00 and 1:19:59.";
                return false;
            }

            if (minimumSeconds > maximumSeconds)
            {
                errorMessage = "Minimum cannot be greater than maximum.";
                return false;
            }

            return true;
        }
    }
}
