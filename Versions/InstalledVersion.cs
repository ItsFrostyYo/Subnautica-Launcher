using SubnauticaLauncher.Enums;
using System.IO;

namespace SubnauticaLauncher.Versions;

public class InstalledVersion
{
    private const int MaxDisplayNameLength = 25;

    public string DisplayName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string OriginalDownload { get; set; } = "";
    public string HomeFolder { get; set; } = "";

    public VersionStatus Status { get; set; } = VersionStatus.Idle;

    public bool IsActive => Status == VersionStatus.Active;

    public string DisplayLabel => Status switch
    {
        VersionStatus.Switching => "Switching -> " + GetTrimmedDisplayName(DisplayName),
        VersionStatus.Launching => "Launching -> " + GetTrimmedDisplayName(DisplayName),
        VersionStatus.Launched => "Launched -> " + GetTrimmedDisplayName(DisplayName),
        VersionStatus.Active => "Active -> " + GetTrimmedDisplayName(DisplayName),
        _ => GetTrimmedDisplayName(DisplayName)
    };

    protected static T? ParseFromInfo<T>(string folderPath, string infoPath)
        where T : InstalledVersion, new()
    {
        var version = new T
        {
            HomeFolder = folderPath
        };

        foreach (var line in File.ReadAllLines(infoPath))
        {
            if (line.StartsWith("DisplayName="))
                version.DisplayName = line["DisplayName=".Length..];
            else if (line.StartsWith("FolderName="))
                version.FolderName = line["FolderName=".Length..];
            else if (line.StartsWith("OriginalDownload="))
                version.OriginalDownload = line["OriginalDownload=".Length..];
        }

        if (string.IsNullOrWhiteSpace(version.DisplayName))
            version.DisplayName = Path.GetFileName(folderPath);

        if (string.IsNullOrWhiteSpace(version.FolderName))
            version.FolderName = Path.GetFileName(folderPath);

        return version;
    }

    public static InstalledVersion? FromInfo(string folderPath, string infoPath)
        => ParseFromInfo<InstalledVersion>(folderPath, infoPath);

    private static string GetTrimmedDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= MaxDisplayNameLength
            ? value
            : value.Substring(0, MaxDisplayNameLength);
    }
}
