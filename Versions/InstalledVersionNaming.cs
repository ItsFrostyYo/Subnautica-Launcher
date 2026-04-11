using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Versions;

internal static class InstalledVersionNaming
{
    public const int MaxDisplayNameLength = 25;

    private static readonly Regex InstalledNameFromIdRegex = new(
        @"^(SubnauticaZero|Subnautica)_?(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(\d{4})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StarMarkerRegex = new(@"\*[^*]+\*", RegexOptions.Compiled);
    private static readonly Regex ParentheticalRegex = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string BuildInstalledDisplayName(string versionId, string fallbackDisplayName)
    {
        Match match = InstalledNameFromIdRegex.Match(versionId);
        if (match.Success)
        {
            string gameName = match.Groups[1].Value.Equals("SubnauticaZero", StringComparison.OrdinalIgnoreCase)
                ? "Below Zero"
                : "Subnautica";
            string month = NormalizeMonth(match.Groups[2].Value);
            string year = match.Groups[3].Value;
            return TrimToMaxLength($"{gameName} {month} {year}");
        }

        return SanitizeDisplayName(fallbackDisplayName);
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
        string normalized = SanitizeDisplayName(baseDisplayName);
        string suffix = instanceNumber <= 1 ? " Modded" : $" Modded {instanceNumber}";
        return TrimToMaxLength(normalized + suffix);
    }

    public static string TrimToMaxLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= MaxDisplayNameLength
            ? value
            : value.Substring(0, MaxDisplayNameLength - 3) + "...";
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
