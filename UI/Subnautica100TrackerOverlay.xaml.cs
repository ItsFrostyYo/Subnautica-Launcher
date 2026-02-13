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

        public void SetProgress(int totalUnlocked, int totalRequired, int blueprintUnlocked, int blueprintRequired, int entriesUnlocked, int entriesRequired)
        {
            double percent = totalRequired > 0
                ? (double)totalUnlocked * 100d / totalRequired
                : 0d;

            ProgressText.Text = $"({totalUnlocked}/{totalRequired}) {Math.Round(percent)}%";
            BreakdownText.Text = $"Blueprints: {blueprintUnlocked}/{blueprintRequired} | Entries: {entriesUnlocked}/{entriesRequired}";
        }
    }
}
