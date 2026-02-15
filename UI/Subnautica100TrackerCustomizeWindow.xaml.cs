using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SubnauticaLauncher.Gameplay;

namespace SubnauticaLauncher.UI
{
    public partial class Subnautica100TrackerCustomizeWindow : Window
    {
        public Subnautica100TrackerCustomizeWindow(
            Subnautica100TrackerOverlaySize currentSize,
            bool unlockPopupEnabled)
        {
            InitializeComponent();

            SelectSize(currentSize);
            UnlockPopupCheckBox.IsChecked = unlockPopupEnabled;
        }

        public Subnautica100TrackerOverlaySize SelectedSize { get; private set; } = Subnautica100TrackerOverlaySize.Medium;
        public bool UnlockPopupEnabled { get; private set; } = true;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SelectedSize = ReadSelectedSize();
            UnlockPopupEnabled = UnlockPopupCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void SelectSize(Subnautica100TrackerOverlaySize size)
        {
            string targetTag = size.ToString();
            ComboBoxItem? item = SizeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(comboItem =>
                    comboItem.Tag is string tag &&
                    tag.Equals(targetTag, StringComparison.OrdinalIgnoreCase));

            if (item != null)
            {
                SizeComboBox.SelectedItem = item;
                return;
            }

            SizeComboBox.SelectedIndex = 1;
        }

        private Subnautica100TrackerOverlaySize ReadSelectedSize()
        {
            if (SizeComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse(tag, ignoreCase: true, out Subnautica100TrackerOverlaySize parsed))
            {
                return parsed;
            }

            return Subnautica100TrackerOverlaySize.Medium;
        }
    }
}
