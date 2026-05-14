using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Subnautica2;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text.Json;

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

        if (profile.Game == LauncherGame.Subnautica2 &&
            TryMatchSubnautica2ByBuildIdentity(versionFolder, profile, out definition, out buildTimeUtc, out failureReason))
        {
            return true;
        }

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

        if (profile.Game == LauncherGame.Subnautica2 &&
            TryReadSubnautica2BuildIdentity(versionFolder, out _, out DateTime sn2BuildTimeUtc, out _))
        {
            buildTimeUtc = sn2BuildTimeUtc;
            return true;
        }

        foreach (string candidatePath in GetBuildTimeCandidates(versionFolder, profile))
        {
            if (!File.Exists(candidatePath))
                continue;

            try
            {
                string raw = File.ReadAllText(candidatePath).Trim();
                if (TryParseRawBuildTime(raw, out DateTime parsedBuildTime))
                {
                    buildTimeUtc = parsedBuildTime;
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
            profile.Game == LauncherGame.Subnautica2
                ? $"Could not read a valid build identity from version.json or version.txt for {profile.DisplayName}."
                : $"Could not read a valid build date from __buildtime.txt for {profile.DisplayName}.";
        return false;
    }

    private static IEnumerable<string> GetBuildTimeCandidates(string versionFolder, LauncherGameProfile profile)
    {
        string exeStem = Path.GetFileNameWithoutExtension(profile.ExecutableName);

        yield return Path.Combine(versionFolder, "__buildtime.txt");
        yield return Path.Combine(versionFolder, $"{exeStem}_Data", "StreamingAssets", "__buildtime.txt");

        if (profile.Game == LauncherGame.Subnautica2)
        {
            yield return Path.Combine(versionFolder, "version.json");
            yield return Path.Combine(versionFolder, "version.txt");
        }
    }

    private static bool TryMatchSubnautica2ByBuildIdentity(
        string versionFolder,
        LauncherGameProfile profile,
        out GameVersionInstallDefinition? definition,
        out DateTime buildTimeUtc,
        out string failureReason)
    {
        definition = null;
        buildTimeUtc = default;
        failureReason = string.Empty;

        if (!TryReadSubnautica2BuildIdentity(versionFolder, out long changelist, out buildTimeUtc, out _))
            return false;

        List<GameVersionInstallDefinition> exactMatches = profile.InstallDefinitions
            .OfType<Subnautica2VersionInstallDefinition>()
            .Where(def => def.BuildChangelist.HasValue && def.BuildChangelist.Value == changelist)
            .Cast<GameVersionInstallDefinition>()
            .ToList();

        if (exactMatches.Count == 1)
        {
            definition = exactMatches[0];
            return true;
        }

        if (exactMatches.Count > 1)
        {
            failureReason =
                $"Multiple known {profile.DisplayName} versions match build changelist {changelist}.";
        }

        return false;
    }

    private static bool TryReadSubnautica2BuildIdentity(
        string versionFolder,
        out long changelist,
        out DateTime buildTimeUtc,
        out string failureReason)
    {
        changelist = 0;
        buildTimeUtc = default;
        failureReason = string.Empty;

        string versionJsonPath = Path.Combine(versionFolder, "version.json");
        if (File.Exists(versionJsonPath))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(versionJsonPath));
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("changelist", out JsonElement changelistProp) &&
                    changelistProp.TryGetInt64(out long parsedChangelist) &&
                    root.TryGetProperty("timestamp", out JsonElement timestampProp) &&
                    TryParseRawBuildTime(timestampProp.GetString() ?? string.Empty, out DateTime parsedBuildTime))
                {
                    changelist = parsedChangelist;
                    buildTimeUtc = parsedBuildTime;
                    return true;
                }
            }
            catch
            {
                // Fall back to version.txt below.
            }
        }

        string versionTxtPath = Path.Combine(versionFolder, "version.txt");
        if (File.Exists(versionTxtPath))
        {
            try
            {
                string raw = File.ReadAllText(versionTxtPath).Trim();
                string[] parts = raw.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    long.TryParse(parts[0], out long parsedChangelist) &&
                    TryParseRawBuildTime(parts[1], out DateTime parsedBuildTime))
                {
                    changelist = parsedChangelist;
                    buildTimeUtc = parsedBuildTime;
                    return true;
                }
            }
            catch
            {
                // Fall through to failure below.
            }
        }

        failureReason = "Could not read a valid Subnautica 2 build changelist or timestamp.";
        return false;
    }

    private static bool TryParseRawBuildTime(string raw, out DateTime parsed)
    {
        if (DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out parsed) ||
            DateTime.TryParse(raw, out parsed))
        {
            return true;
        }

        string? token = raw
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.Contains('T') && part.Contains('-') && part.Contains(':'));

        if (!string.IsNullOrWhiteSpace(token) &&
            (DateTime.TryParse(
                 token,
                 CultureInfo.InvariantCulture,
                 DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                 out parsed) ||
             DateTime.TryParse(token, out parsed)))
        {
            return true;
        }

        parsed = default;
        return false;
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
