using System.Windows.Media;

namespace MTGProxyBuilder.UI
{
    /// <summary>
    /// Frozen static brushes for code-behind use. Matches Theme.xaml palette.
    /// All brushes are created once and frozen for thread-safe, allocation-free reuse.
    /// </summary>
    public static class AppBrushes
    {
        // Backgrounds
        public static readonly SolidColorBrush WindowBg = Freeze(0x1E, 0x1E, 0x1E);
        public static readonly SolidColorBrush PanelBg = Freeze(0x25, 0x25, 0x26);
        public static readonly SolidColorBrush TileBg = Freeze(0x3E, 0x3E, 0x42);
        public static readonly SolidColorBrush ActionTileBg = Freeze(0x33, 0x33, 0x38);

        // Text
        public static readonly SolidColorBrush TextPrimary = Freeze(0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush TextSecondary = Freeze(0xCC, 0xCC, 0xCC);
        public static readonly SolidColorBrush TextMuted = Freeze(0x88, 0x88, 0x88);
        public static readonly SolidColorBrush TextDim = Freeze(0x66, 0x66, 0x66);
        public static readonly SolidColorBrush Label = Freeze(0xAA, 0xAA, 0xAA);

        // Accents
        public static readonly SolidColorBrush AccentBlue = Freeze(0x00, 0x78, 0xD4);
        public static readonly SolidColorBrush AccentGreen = Freeze(0x4C, 0xAF, 0x50);
        public static readonly SolidColorBrush AccentRed = Freeze(0xE0, 0x60, 0x60);
        public static readonly SolidColorBrush AccentCyan = Freeze(0x4F, 0xC3, 0xF7);
        public static readonly SolidColorBrush SelectionBlue = Freeze(0x1E, 0x90, 0xFF);

        // Borders
        public static readonly SolidColorBrush Border = Freeze(0x55, 0x55, 0x55);
        public static readonly SolidColorBrush BorderSubtle = Freeze(0x33, 0x33, 0x33);

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
