using SubnauticaLauncher.Services;

namespace SubnauticaLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (LiveSplitPluginHostMode.IsRequested(args))
            return LiveSplitPluginHostMode.Run(args);

        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }
}
