using System;
using System.IO;

namespace SubnauticaLauncher.Core;

public static class AppPaths
{
    public static string BasePath => AppContext.BaseDirectory;
    public static string DataPath => Path.Combine(BasePath, "data");
    public static string PluginsPath => Path.Combine(BasePath, "Plugins");
    public static string LogsPath => Path.Combine(BasePath, "Logs");
    public static string ArtifactsPath => Path.Combine(BasePath, "Artifacts");
}
