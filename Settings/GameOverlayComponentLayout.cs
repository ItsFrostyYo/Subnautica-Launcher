using SubnauticaLauncher.Enums;

namespace SubnauticaLauncher.Settings;

public sealed class GameOverlayComponentLayout
{
    public GameOverlayComponentType Type { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public bool ShowLabels { get; set; } = true;
    public double? PanelOpacity { get; set; }
}
