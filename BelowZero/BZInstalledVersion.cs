using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher.BelowZero;

public class BZInstalledVersion : InstalledVersion
{
    public static new BZInstalledVersion? FromInfo(string folderPath, string infoPath)
        => ParseFromInfo<BZInstalledVersion>(folderPath, infoPath);
}
