using SubnauticaLauncher.Core;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SubnauticaLauncher.Versions;

internal static class VersionIdentityResolver
{
    private static readonly Regex MonthYearRegex = new(
        @"\b(?<month>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s*,?\s*(?<year>\d{4})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryDetectOriginalVersion(
        string versionFolder,
        LauncherGameProfile profile,
        out GameVersionInstallDefinition? definition,
        out DateTime buildTimeUtc,
        out string failureReason)
    {
        definition = null;
        buildTimeUtc = default;
        failureReason = string.Empty;

        if (!TryReadBuildTime(versionFolder, profile, out buildTimeUtc, out failureReason))
            return false;

        int buildYear = buildTimeUtc.Year;
        int buildMonth = buildTimeUtc.Month;

        List<GameVersionInstallDefinition> matches = profile.InstallDefinitions
            .Where(def => TryParseMonthYear(def.DisplayName, out int defMonth, out int defYear) &&
                          defMonth == buildMonth &&
                          defYear == buildYear)
            .ToList();

        if (matches.Count == 1)
        {
            definition = matches[0];
            failureReason = string.Empty;
            return true;
        }

        if (matches.Count == 0)
        {
            failureReason =
                $"No known {profile.DisplayName} launcher version matches build date {buildTimeUtc:MMM yyyy}.";
            return false;
        }

        failureReason =
            $"Multiple known {profile.DisplayName} versions match build date {buildTimeUtc:MMM yyyy}.";
        return false;
    }

    public static bool TryReadBuildTime(
        string versionFolder,
        LauncherGameProfile profile,
        out DateTime buildTimeUtc,
        out string failureReason)
    {
        buildTimeUtc = default;
        failureReason = string.Empty;

        foreach (string candidatePath in GetBuildTimeCandidates(versionFolder, profile))
        {
            if (!File.Exists(candidatePath))
                continue;

            try
            {
                string raw = File.ReadAllText(candidatePath).Trim();
                if (DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                        out DateTime parsed) ||
                    DateTime.TryParse(raw, out parsed))
                {
                    buildTimeUtc = parsed;
                    failureReason = string.Empty;
                    return true;
                }
            }
            catch
            {
                // Keep searching other candidate files.
            }
        }

        failureReason =
            $"Could not read a valid build date from __buildtime.txt for {profile.DisplayName}.";
        return false;
    }

    private static IEnumerable<string> GetBuildTimeCandidates(string versionFolder, LauncherGameProfile profile)
    {
        string exeStem = Path.GetFileNameWithoutExtension(profile.ExecutableName);

        yield return Path.Combine(versionFolder, "__buildtime.txt");
        yield return Path.Combine(versionFolder, $"{exeStem}_Data", "StreamingAssets", "__buildtime.txt");
    }

    private static bool TryParseMonthYear(string displayName, out int month, out int year)
    {
        month = 0;
        year = 0;

        Match match = MonthYearRegex.Match(displayName);
        if (!match.Success)
            return false;

        string token = match.Groups["month"].Value[..3];
        month = token.ToLowerInvariant() switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => 0
        };

        return month > 0 && int.TryParse(match.Groups["year"].Value, out year);
    }
}
