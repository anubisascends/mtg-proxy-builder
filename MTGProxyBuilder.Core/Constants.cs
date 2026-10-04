namespace MTGProxyBuilder.Core
{
    public static class Constants
    {
        public const float DefaultCardWidthMm = 63f;
        public const float DefaultCardHeightMm = 88f;
        public const float DefaultBleedMm = 1.5f;
        public const int DefaultDpi = 300;

        /// <summary>Typical unprintable border (mm) at each page edge on desktop printers.</summary>
        public const float NoPrintZoneMm = 4f;

        public const int MmToDpiConversion = 25; // Approximate conversion for DPI
    }
}
