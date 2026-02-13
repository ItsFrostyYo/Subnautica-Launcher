using System.Windows;

namespace SubnauticaLauncher.Explosion
{
    public partial class ExplosionResetDisplay : Window
    {
        public ExplosionResetDisplay()
        {
            InitializeComponent();

            Left = 10;
            Top = 10;
        }

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            UI.OverlayWindowNative.MakeClickThrough(this);
        }

        public void SetStep(string text)
        {
            StepText.Text = text;
        }

        public void SetExplosionTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            ExplosionTimeText.Text =
                $"Explosion Time: {(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        public void SetResetCount(int count)
        {
            ResetCountText.Text = $"Resets This Session: {count}";
        }
    }
}
