using System.Drawing;
using System.Runtime.Versioning;

namespace SubnauticaLauncher.Display
{
    [SupportedOSPlatform("windows")]
    public static class PixelScaler
    {
        public static Point Scale(Point p, DisplayInfo display)
        {
            return new Point(
                (int)(p.X * display.ScaleX),
                (int)(p.Y * display.ScaleY)
            );
        }

        public static Rectangle Scale(Rectangle r, DisplayInfo display)
        {
            return new Rectangle(
                (int)(r.X * display.ScaleX),
                (int)(r.Y * display.ScaleY),
                (int)(r.Width * display.ScaleX),
                (int)(r.Height * display.ScaleY)
            );
        }
    }
}