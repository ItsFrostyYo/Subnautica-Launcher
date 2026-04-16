using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Versions;

internal static class InstalledVersionNaming
{
    public const int MaxDisplayNameLength = 25;

    private static readonly Regex InstalledNameFromIdRegex = new(
        @"^(SubnauticaZero|Subnautica)_?(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(\d{4})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MonthYearRegex = new(
        @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s*,?\s*(\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StarMarkerRegex = new(@"\*[^*]+\*", RegexOptions.Compiled);
    private static readonly Regex ParentheticalRegex = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingGameNameRegex = new(
        @"^(Subnautica|Below Zero)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StatusSuffixRegex = new(
        @"\s*,?\s*(Vanilla|Modded(?:\s+\d+)?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string BuildInstalledDisplayName(string versionId, string fallbackDisplayName)
    {
        string baseName = BuildBaseDisplayName(versionId, fallbackDisplayName);
        return AppendStatusSuffix(baseName, "Vanilla");
    }

    public static string BuildBaseDisplayName(string versionId, string fallbackDisplayName)
    {
        Match match = InstalledNameFromIdRegex.Match(versionId);
        if (match.Success)
        {
            string month = NormalizeMonth(match.Groups[2].Value);
            string year = match.Groups[3].Value;
            return TrimToMaxLength($"{month} {year}");
        }

        string result = SanitizeAutoName(fallbackDisplayName);
        Match monthYearMatch = MonthYearRegex.Match(result);
        if (monthYearMatch.Success)
        {
            string month = NormalizeMonth(monthYearMatch.Groups[1].Value);
            string year = monthYearMatch.Groups[2].Value;
            return TrimToMaxLength($"{month} {year}");
        }

        result = LeadingGameNameRegex.Replace(result, "");
        result = StatusSuffixRegex.Replace(result, "");
        result = MultiWhitespaceRegex.Replace(result, " ").Trim(' ', ',', '-', '_');
        return TrimToMaxLength(result);
    }

    public static string SanitizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string result = StarMarkerRegex.Replace(value, " ");
        result = ParentheticalRegex.Replace(result, " ");
        result = result.Replace(",", " ");
        result = MultiWhitespaceRegex.Replace(result, " ").Trim();
        return NormalizeSavedDisplayName(result);
    }

    public static string NormalizeSavedDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string result = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        result = MultiWhitespaceRegex.Replace(result, " ").Trim();

        return result.Length <= MaxDisplayNameLength
            ? result
            : result.Substring(0, MaxDisplayNameLength);
    }

    public static string BuildModdedDisplayName(string baseDisplayName, int instanceNumber = 1)
    {
        string normalized = BuildBaseDisplayName(string.Empty, baseDisplayName);
        string suffix = instanceNumber <= 1 ? "Modded" : $"Modded {instanceNumber}";
        return AppendStatusSuffix(normalized, suffix);
    }

    public static string TrimToMaxLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= MaxDisplayNameLength
            ? value
            : value.Substring(0, MaxDisplayNameLength - 3) + "...";
    }

    private static string SanitizeAutoName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string result = StarMarkerRegex.Replace(value, " ");
        result = ParentheticalRegex.Replace(result, " ");
        result = result.Replace(",", " ");
        result = MultiWhitespaceRegex.Replace(result, " ").Trim();
        return NormalizeSavedDisplayName(result);
    }

    private static string AppendStatusSuffix(string baseName, string suffix)
    {
        baseName = NormalizeSavedDisplayName(baseName);
        suffix = NormalizeSavedDisplayName(suffix);

        if (string.IsNullOrWhiteSpace(baseName))
            return TrimToMaxLength(suffix);

        string separator = ", ";
        string combined = baseName + separator + suffix;
        if (combined.Length <= MaxDisplayNameLength)
            return combined;

        int reserved = separator.Length + suffix.Length;
        if (reserved >= MaxDisplayNameLength)
            return TrimToMaxLength(suffix);

        int maxBaseLength = MaxDisplayNameLength - reserved;
        string trimmedBase = baseName.Length <= maxBaseLength
            ? baseName
            : baseName.Substring(0, Math.Max(1, maxBaseLength - 3)) + "...";

        return trimmedBase + separator + suffix;
    }

    private static string NormalizeMonth(string month)
    {
        return month.ToLowerInvariant() switch
        {
            "january" => "Jan",
            "february" => "Feb",
            "march" => "Mar",
            "april" => "Apr",
            "june" => "Jun",
            "july" => "Jul",
            "august" => "Aug",
            "september" => "Sep",
            "october" => "Oct",
            "november" => "Nov",
            "december" => "Dec",
            _ => month.Length <= 3
                ? char.ToUpperInvariant(month[0]) + month[1..].ToLowerInvariant()
                : char.ToUpperInvariant(month[0]) + month[1..3].ToLowerInvariant()
        };
    }
}
