using System;
using System.Globalization;
using System.Windows.Data;

namespace MTGProxyBuilder.UI.Converters
{
    /// <summary>Converts bool to "Back Face" (true) or "Front Face" (false).</summary>
    public class BoolToFaceTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? "Back Face" : "Front Face";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
