using SubnauticaLauncher.Core;
using System;
using System.IO;
using System.Reflection;

namespace SubnauticaLauncher.Installer;

public static class Subnautica2ToolInstaller
{
    private const string Uhara10ResourceName =
        "SubnauticaLauncher.Tools.uhara10";

    public static bool IsInstalled()
    {
        return File.Exists(AppPaths.Uhara10Path);
    }

    public static void EnsureInstalled()
    {
        Directory.CreateDirectory(AppPaths.ToolsPath);

        if (File.Exists(AppPaths.Uhara10Path))
            return;

        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Uhara10ResourceName);
        if (stream == null)
        {
            throw new FileNotFoundException(
                $"Embedded Subnautica 2 tool resource '{Uhara10ResourceName}' was not found.");
        }

        using FileStream destination = File.Create(AppPaths.Uhara10Path);
        stream.CopyTo(destination);
    }
}
