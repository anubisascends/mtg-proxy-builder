using System;
using System.Globalization;
using System.Windows.Data;

namespace MTGProxyBuilder.UI.Converters
{
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is not true;
    }
}
