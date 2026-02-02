using SubnauticaLauncher.BelowZero;
using System;
using System;
using System.Globalization;
using System.Windows.Data;
using Brushes = System.Windows.Media.Brushes;

namespace SubnauticaLauncher.UI
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
                BZVersionStatus.Switching => Brushes.Orange,
                BZVersionStatus.Launching => Brushes.Yellow,
                _ => Brushes.White
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}