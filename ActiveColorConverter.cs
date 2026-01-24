using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SubnauticaLauncher.Models;
using Brushes = System.Windows.Media.Brushes;

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
                VersionStatus.Launching => Brushes.OrangeRed,
                VersionStatus.Switching => Brushes.Gold,
                _ => Brushes.White
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}