using System.Globalization;
using System.Windows.Data;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// MultiValueConverter that returns true if both values are the same object reference.
    /// Used for tab active-state highlighting.
    /// </summary>
    public class ReferenceEqualsConverter : IMultiValueConverter
    {
        public static readonly ReferenceEqualsConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return false;
            return ReferenceEquals(values[0], values[1]);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
