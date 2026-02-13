using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using SubnauticaLauncher.Versions;

namespace SubnauticaLauncher
{
    public class ActiveColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not VersionStatus status)
                return Brushes.White;

            return status switch
            {
                VersionStatus.Active => Brushes.LimeGreen,
                VersionStatus.Launched => Brushes.OrangeRed,
                VersionStatus.Launching => Brushes.OrangeRed,
                VersionStatus.Switching => Brushes.Gold,
                _ => Brushes.White
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
