using SubnauticaLauncher.Enums;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.Converters
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
                VersionStatus.Launched => Brushes.Red,
                VersionStatus.Launching => Brushes.Orange,
                VersionStatus.Switching => Brushes.Yellow,
                VersionStatus.Closing => Brushes.OrangeRed,
                _ => Brushes.White
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
