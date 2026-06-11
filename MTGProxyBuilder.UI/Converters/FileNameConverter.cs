using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace MTGProxyBuilder.UI.Converters
{
    /// <summary>Extracts the filename without extension from a full file path.</summary>
    public class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string path ? Path.GetFileNameWithoutExtension(path) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
