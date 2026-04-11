using System;
using System.Globalization;
using System.Windows.Data;

namespace SubnauticaLauncher.Converters
{
    public class StringLengthToFontSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string ?? string.Empty;

            if (text.Length >= 24)
                return 13.0;

            if (text.Length >= 20)
                return 14.0;

            if (text.Length >= 16)
                return 15.0;

            return 17.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
