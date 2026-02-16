using SubnauticaLauncher.Enums;
using System;
using System.Windows;

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
                    StepText.FontSize = 12;
                    ProgressText.FontSize = 10;
                    BreakdownText.FontSize = 9;
                    break;
                case Subnautica100TrackerOverlaySize.Large:
                    StepText.FontSize = 18;
                    ProgressText.FontSize = 15;
                    BreakdownText.FontSize = 14;
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
