using System;
using System.Windows;
using SubnauticaLauncher.Gameplay;

namespace SubnauticaLauncher.UI
{
    public partial class Subnautica100TrackerOverlay : Window
    {
        public Subnautica100TrackerOverlay()
        {
            InitializeComponent();
            Left = 10;
            Top = 10;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            OverlayWindowNative.MakeClickThrough(this);
        }

        public void ApplySizePreset(Subnautica100TrackerOverlaySize size)
        {
            switch (size)
            {
                case Subnautica100TrackerOverlaySize.Small:
                    StepText.FontSize = 13;
                    ProgressText.FontSize = 11;
                    BreakdownText.FontSize = 10;
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    StepText.FontSize = 17;
                    ProgressText.FontSize = 14;
                    BreakdownText.FontSize = 13;
                    break;
                default:
                    StepText.FontSize = 15;
                    ProgressText.FontSize = 13;
                    BreakdownText.FontSize = 12;
                    break;
            }
        }

        public void SetProgress(int totalUnlocked, int totalRequired, int blueprintUnlocked, int blueprintRequired, int entriesUnlocked, int entriesRequired)
        {
            double percent = totalRequired > 0
                ? (double)totalUnlocked * 100d / totalRequired
                : 0d;

            ProgressText.Text = $"({totalUnlocked}/{totalRequired}) {Math.Round(percent)}%";
            BreakdownText.Text = $"BP's: {blueprintUnlocked}/{blueprintRequired} | Ency: {entriesUnlocked}/{entriesRequired}";
        }
    }
}
