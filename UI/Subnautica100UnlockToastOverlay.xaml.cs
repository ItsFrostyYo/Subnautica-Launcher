using System;
using System.Windows;

namespace SubnauticaLauncher.UI
{
    public partial class Subnautica100UnlockToastOverlay : Window
    {
        public Subnautica100UnlockToastOverlay()
        {
            InitializeComponent();
            Left = 10;
            Top = 100;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowNative.MakeClickThrough(this);
        }

        public void SetMessage(string text)
        {
            UnlockText.Text = text;
        }
    }
}
