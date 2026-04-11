using SubnauticaLauncher.Enums;
using System.IO;

namespace SubnauticaLauncher.Versions;

public class InstalledVersion
{
    public string DisplayName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string OriginalDownload { get; set; } = "";
    public bool IsModded { get; set; }
    public string InstalledModId { get; set; } = "";
    public string HomeFolder { get; set; } = "";

    public VersionStatus Status { get; set; } = VersionStatus.Idle;

    public bool IsActive => Status == VersionStatus.Active;
    public string GroupLabel => IsModded ? "Modded" : "Vanilla";
    public string InstalledModDisplayName => SubnauticaLauncher.Mods.ModCatalog.GetDisplayName(InstalledModId);

    public string DisplayLabel => GetTrimmedDisplayName(DisplayName);

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
            else if (line.StartsWith("Modded="))
                version.IsModded = bool.TryParse(line["Modded=".Length..], out bool isModded) && isModded;
            else if (line.StartsWith("InstalledMod="))
                version.InstalledModId = line["InstalledMod=".Length..];
        }

        if (string.IsNullOrWhiteSpace(version.DisplayName))
            version.DisplayName = Path.GetFileName(folderPath);
        else
            version.DisplayName = InstalledVersionNaming.NormalizeSavedDisplayName(version.DisplayName);

        if (string.IsNullOrWhiteSpace(version.FolderName))
            version.FolderName = Path.GetFileName(folderPath);

        return version;
    }

    public static InstalledVersion? FromInfo(string folderPath, string infoPath)
        => ParseFromInfo<InstalledVersion>(folderPath, infoPath);

    private static string GetTrimmedDisplayName(string value)
    {
        return InstalledVersionNaming.TrimToMaxLength(value);
    }
}
