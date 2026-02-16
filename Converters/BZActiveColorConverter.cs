using SubnauticaLauncher.Enums;
using System;
using System.Globalization;
using System.Windows.Data;
using Brushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.Converters
{
    public sealed class BZActiveColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not BZVersionStatus status)
                return Brushes.White;

            return status switch
            {
                BZVersionStatus.Active => Brushes.LimeGreen,
                BZVersionStatus.Launched => Brushes.Red,
                BZVersionStatus.Launching => Brushes.Orange,
                BZVersionStatus.Switching => Brushes.Yellow,
                _ => Brushes.White
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
