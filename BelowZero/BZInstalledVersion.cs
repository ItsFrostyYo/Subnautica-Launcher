using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;
using SubnauticaLauncher.BelowZero;

namespace SubnauticaLauncher.BelowZero;

public class BZInstalledVersion
{
    public string DisplayName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string OriginalDownload { get; set; } = "";
    public string HomeFolder { get; set; } = "";

    public BZVersionStatus Status { get; set; } = BZVersionStatus.Idle;

    public bool IsActive => Status == BZVersionStatus.Active;

    public string DisplayLabel => Status switch
    {
        BZVersionStatus.Switching => "Switching → " + DisplayName,
        BZVersionStatus.Launching => "Launching → " + DisplayName,
        BZVersionStatus.Active => "Active → " + DisplayName,
        _ => DisplayName
    };

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