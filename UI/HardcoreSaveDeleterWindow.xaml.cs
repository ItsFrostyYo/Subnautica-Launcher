using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Settings;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SubnauticaLauncher.UI
{
    public partial class HardcoreSaveDeleterWindow : Window
    {
        private const string DefaultBg = "Lifepod";

        public HardcoreSaveTargetGame SelectedGame { get; private set; } = HardcoreSaveTargetGame.Subnautica;
        public HardcoreSaveTargetScope SelectedScope { get; private set; } = HardcoreSaveTargetScope.ActiveOnly;

        public HardcoreSaveDeleterWindow()
        {
            InitializeComponent();
            Loaded += HardcoreSaveDeleterWindow_Loaded;
        }

        private ImageBrush GetBackgroundBrush()
        {
            return (ImageBrush)Resources["BackgroundBrush"];
        }

        private void HardcoreSaveDeleterWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LauncherSettings.Load();
            string bg = LauncherSettings.Current.BackgroundPreset;
            if (string.IsNullOrWhiteSpace(bg))
                bg = DefaultBg;

            ApplyBackground(bg);

            GameChoiceDropdown.SelectedIndex = 0;
            ScopeChoiceDropdown.SelectedIndex = 0;
        }

        private void ApplyBackground(string preset)
        {
            try
            {
                BitmapImage img = new BitmapImage();
                img.BeginInit();

                if (File.Exists(preset))
                {
                    img.UriSource = new Uri(preset, UriKind.Absolute);
                }
                else
                {
                    img.UriSource = new Uri(
                        $"pack://application:,,,/Assets/Backgrounds/{preset}.png",
                        UriKind.Absolute
                    );
                }

                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();

                GetBackgroundBrush().ImageSource = img;
            }
            catch
            {
                GetBackgroundBrush().ImageSource = new BitmapImage(new Uri(
                    $"pack://application:,,,/Assets/Backgrounds/{DefaultBg}.png",
                    UriKind.Absolute));
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                return;

            DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ConfirmCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            DeleteButton.IsEnabled = ConfirmCheckBox.IsChecked == true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            SelectedGame = GameChoiceDropdown.SelectedIndex switch
            {
                1 => HardcoreSaveTargetGame.BelowZero,
                2 => HardcoreSaveTargetGame.Both,
                _ => HardcoreSaveTargetGame.Subnautica
            };

            SelectedScope = ScopeChoiceDropdown.SelectedIndex == 1
                ? HardcoreSaveTargetScope.AllVersions
                : HardcoreSaveTargetScope.ActiveOnly;

            DialogResult = true;
            Close();
        }
    }
}
