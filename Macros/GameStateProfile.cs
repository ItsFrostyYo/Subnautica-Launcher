using System.Drawing;

namespace SubnauticaLauncher.Macros
{
    public sealed class GameStateProfile
    {
        public Point MainMenuPixel { get; init; }
        public Color MainMenuColor { get; init; }

        public Point InGamePixel { get; init; }
        public Color InGameColor { get; init; }

        public Point BlackPixel { get; init; }
        public int ColorTolerance { get; init; } = 6;
    }
}