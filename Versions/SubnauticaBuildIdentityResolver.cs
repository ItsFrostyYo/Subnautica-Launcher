using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Versions;

public static class SubnauticaBuildIdentityResolver
{
    private static readonly Regex MonthYearRegex = new(@"\b(?<month>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s*,?\s*(?<year>\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryDetect(string gameFolder, out GameVersionDefinition? definition, out string error)
    {
        definition = null;
        error = string.Empty;
        if (File.Exists(gameFolder))
            gameFolder = Path.GetDirectoryName(Path.GetFullPath(gameFolder)) ?? gameFolder;

        foreach (string candidate in GetCandidateFiles(gameFolder))
        {
            if (!File.Exists(candidate))
                continue;

            try
            {
                if (!DateTime.TryParse(File.ReadAllText(candidate).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out DateTime buildTime) &&
                    !DateTime.TryParse(File.ReadAllText(candidate).Trim(), out buildTime))
                    continue;

                GameVersionDefinition[] matches = GameVersionRegistry.Subnautica.All
                    .Where(item => TryParseMonthYear(item.DisplayName, out int month, out int year) && month == buildTime.Month && year == buildTime.Year)
                    .ToArray();
                if (matches.Length == 1)
                {
                    definition = matches[0];
                    return true;
                }

                error = matches.Length == 0
                    ? $"No known launcher version matches the build date {buildTime:MMM yyyy}."
                    : $"More than one launcher version matches {buildTime:MMM yyyy}.";
                return false;
            }
            catch
            {
                // Try the other supported build-time location.
            }
        }

        error = "Could not read a valid __buildtime.txt from this Subnautica folder.";
        return false;
    }

    public static IEnumerable<string> GetCandidateFiles(string gameFolder)
    {
        yield return Path.Combine(gameFolder, "__buildtime.txt");
        yield return Path.Combine(gameFolder, "Subnautica_Data", "StreamingAssets", "__buildtime.txt");
    }

    private static bool TryParseMonthYear(string displayName, out int month, out int year)
    {
        month = 0;
        year = 0;
        Match match = MonthYearRegex.Match(displayName);
        if (!match.Success)
            return false;

        month = DateTime.ParseExact(match.Groups["month"].Value[..3], "MMM", CultureInfo.InvariantCulture).Month;
        return int.TryParse(match.Groups["year"].Value, out year);
    }
}
