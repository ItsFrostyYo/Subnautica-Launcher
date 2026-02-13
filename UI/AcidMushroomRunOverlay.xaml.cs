using System.Windows;

namespace SubnauticaLauncher.UI
{
    public partial class AcidMushroomRunOverlay : Window
    {
        public AcidMushroomRunOverlay()
        {
            InitializeComponent();
            Left = 10;
            Top = 10;
        }

        public void SetTotal(int total)
        {
            CountText.Text = $"Total Picked Up: {total}";
        }
    }
}
