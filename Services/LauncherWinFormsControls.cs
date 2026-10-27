using System.Drawing;
using Forms = System.Windows.Forms;

namespace SubnauticaLauncher.Services;

internal sealed class LauncherCloseButton : Forms.Control
{
    private bool _hovered;

    public LauncherCloseButton()
    {
        Size = new Size(19, 19);
        Cursor = Forms.Cursors.Hand;
        SetStyle(Forms.ControlStyles.AllPaintingInWmPaint |
                 Forms.ControlStyles.OptimizedDoubleBuffer |
                 Forms.ControlStyles.SupportsTransparentBackColor |
                 Forms.ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnPaint(Forms.PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color color = _hovered ? Color.FromArgb(255, 110, 115) : Color.FromArgb(245, 75, 85);
        using var pen = new Pen(color, 2.1F) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        PointF center = new(Width / 2F, Height / 2F);
        eventArgs.Graphics.DrawLine(pen, center.X - 4.3F, center.Y - 4.3F, center.X + 4.3F, center.Y + 4.3F);
        eventArgs.Graphics.DrawLine(pen, center.X + 4.3F, center.Y - 4.3F, center.X - 4.3F, center.Y + 4.3F);
    }
}
