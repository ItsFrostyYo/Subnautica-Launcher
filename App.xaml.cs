using System.Windows;
using SubnauticaLauncher.Services;
using SubnauticaLauncher.UI;

namespace SubnauticaLauncher;

public partial class App : System.Windows.Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        ImGuiAddVersionWindow.CloseAll();
        base.OnExit(e);
    }
}
