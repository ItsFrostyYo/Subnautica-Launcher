using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.BelowZero;

namespace SubnauticaLauncher.BelowZero;

public class BZInstalledVersion
{
    private const int MaxDisplayNameLength = 25;

    public string DisplayName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string OriginalDownload { get; set; } = "";
    public string HomeFolder { get; set; } = "";

    public BZVersionStatus Status { get; set; } = BZVersionStatus.Idle;

    public bool IsActive => Status == BZVersionStatus.Active;

    public string DisplayLabel => Status switch
    {
        BZVersionStatus.Switching => "Switching → " + GetTrimmedDisplayName(DisplayName),
        BZVersionStatus.Launching => "Launching → " + GetTrimmedDisplayName(DisplayName),
        BZVersionStatus.Active => "Active → " + GetTrimmedDisplayName(DisplayName),
        _ => GetTrimmedDisplayName(DisplayName)
    };

    private static string GetTrimmedDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= MaxDisplayNameLength
            ? value
            : value.Substring(0, MaxDisplayNameLength);
    }

    public static BZInstalledVersion? FromInfo(string folderPath, string infoPath)
    {
        var v = new BZInstalledVersion
        {
            HomeFolder = folderPath
        };

        foreach (var line in File.ReadAllLines(infoPath))
        {
            if (line.StartsWith("DisplayName="))
                v.DisplayName = line["DisplayName=".Length..];
            else if (line.StartsWith("FolderName="))
                v.FolderName = line["FolderName=".Length..];
            else if (line.StartsWith("OriginalDownload="))
                v.OriginalDownload = line["OriginalDownload=".Length..];
        }

        if (string.IsNullOrWhiteSpace(v.DisplayName))
            v.DisplayName = Path.GetFileName(folderPath);

        if (string.IsNullOrWhiteSpace(v.FolderName))
            v.FolderName = Path.GetFileName(folderPath);

        return v;
    }
}
