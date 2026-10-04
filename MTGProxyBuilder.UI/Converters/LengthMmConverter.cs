using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MTGProxyBuilder.Core;

namespace MTGProxyBuilder.UI.Converters
{
    /// <summary>
    /// Shows a mm value as a plain number (blank for 0) and parses input with an
    /// optional unit (e.g. "70", "70mm", "7cm", "2.75in", 2.75") back to mm.
    /// </summary>
    public class LengthMmConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is float mm && mm != 0
                ? mm.ToString("0.###", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return 0f;
            return LengthParser.TryParseMm(text, out float mm) && mm >= 0
                ? mm
                : DependencyProperty.UnsetValue;
        }
    }
}
