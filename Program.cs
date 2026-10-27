using SubnauticaLauncher.Services;

namespace SubnauticaLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (PluginSettingsHostMode.IsRequested(args))
            return PluginSettingsHostMode.Run(args);

        if (PluginHostMode.IsRequested(args))
            return PluginHostMode.Run(args);

        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }
}
