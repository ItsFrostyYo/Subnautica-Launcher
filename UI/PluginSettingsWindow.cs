using SubnauticaLauncher.Services;

namespace SubnauticaLauncher.UI;

public static class PluginSettingsWindow
{
    public static Task ShowAsync(LauncherPluginManager manager, string pluginId, IntPtr ownerHandle) =>
        manager.ShowSettingsAsync(pluginId, ownerHandle);
}
