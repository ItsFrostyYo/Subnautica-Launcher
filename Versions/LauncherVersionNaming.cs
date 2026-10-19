using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Versions;

internal static class LauncherVersionNaming
{
    private static readonly Regex VersionId = new(@"^Subnautica_?(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(\d{4})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MonthYear = new(@"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s*,?\s*(\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string BuildDisplayName(string id, string fallback)
    {
        Match match = VersionId.Match(id);
        if (!match.Success)
            match = MonthYear.Match(fallback);

        if (match.Success)
            return $"{NormalizeMonth(match.Groups[1].Value)} {match.Groups[2].Value}";

        return fallback.Replace("*", string.Empty).Split('(')[0].Trim().TrimEnd(',');
    }

    private static string NormalizeMonth(string month) => month[..Math.Min(3, month.Length)].ToLowerInvariant() switch
    {
        "jan" => "Jan", "feb" => "Feb", "mar" => "Mar", "apr" => "Apr", "may" => "May", "jun" => "Jun",
        "jul" => "Jul", "aug" => "Aug", "sep" => "Sep", "oct" => "Oct", "nov" => "Nov", "dec" => "Dec", _ => month
    };
}
