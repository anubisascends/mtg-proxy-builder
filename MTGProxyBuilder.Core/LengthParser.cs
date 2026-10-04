using System.Globalization;
using System.Text.RegularExpressions;

namespace MTGProxyBuilder.Core
{
    /// <summary>
    /// Parses user-entered lengths with an optional unit suffix into millimetres.
    /// A bare number is treated as mm. Accepts mm, cm, in / inch / inches / ".
    /// </summary>
    public static class LengthParser
    {
        private static readonly Regex LengthPattern = new(
            @"^\s*(?<num>[+-]?(\d+(\.\d*)?|\.\d+))\s*(?<unit>mm|cm|inches|inch|in|"")?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParseMm(string? text, out float mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var match = LengthPattern.Match(text);
            if (!match.Success) return false;

            if (!float.TryParse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return false;

            string unit = match.Groups["unit"].Value.ToLowerInvariant();
            mm = unit switch
            {
                "cm" => value * 10f,
                "in" or "inch" or "inches" or "\"" => value * 25.4f,
                _ => value
            };
            return true;
        }
    }
}
