using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Versions;

public class InstalledVersion
{
    public string DisplayName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string OriginalDownload { get; set; } = "";
    public string HomeFolder { get; set; } = "";

    public VersionStatus Status { get; set; } = VersionStatus.Idle;

    public bool IsActive => Status == VersionStatus.Active;

    public string DisplayLabel => Status switch
    {
        VersionStatus.Switching => "Switching → " + DisplayName,
        VersionStatus.Launching => "Launching → " + DisplayName,
        VersionStatus.Active => "Active → " + DisplayName,
        _ => DisplayName
    };

    public static InstalledVersion? FromInfo(string folderPath, string infoPath)
    {
        var v = new InstalledVersion
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