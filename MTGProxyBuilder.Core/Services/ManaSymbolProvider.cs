using System.Reflection;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Provides access to embedded mana symbol SVG files.
    /// Symbol names are case-insensitive: "W", "w", "2B", "2b" all work.
    /// </summary>
    public static class ManaSymbolProvider
    {
        private static readonly Assembly ResourceAssembly = typeof(ManaSymbolProvider).Assembly;
        private static readonly string ResourcePrefix = "MTGProxyBuilder.Core.ManaSymbols.";
        private static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string>? _availableSymbols;

        /// <summary>Returns the SVG content for a mana symbol name (without extension). Returns null if not found.</summary>
        public static string? GetSvgContent(string symbolName)
        {
            if (string.IsNullOrEmpty(symbolName)) return null;

            if (_cache.TryGetValue(symbolName, out var cached))
                return cached;

            // Try exact name, then lowercase (files are lowercase)
            string[] candidates = { symbolName, symbolName.ToLowerInvariant() };
            foreach (var candidate in candidates)
            {
                string resourceName = $"{ResourcePrefix}{candidate}.svg";
                using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var svg = reader.ReadToEnd();
                    _cache[symbolName] = svg;
                    return svg;
                }
            }

            return null;
        }

        /// <summary>Returns the raw SVG bytes for a mana symbol. Returns null if not found.</summary>
        public static byte[]? GetSvgBytes(string symbolName)
        {
            if (string.IsNullOrEmpty(symbolName)) return null;

            string[] candidates = { symbolName, symbolName.ToLowerInvariant() };
            foreach (var candidate in candidates)
            {
                string resourceName = $"{ResourcePrefix}{candidate}.svg";
                using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            return null;
        }

        /// <summary>Returns true if a mana symbol with this name exists.</summary>
        public static bool HasSymbol(string symbolName)
        {
            return GetAvailableSymbols().Contains(symbolName);
        }

        /// <summary>Returns all available mana symbol names (without extension).</summary>
        public static IReadOnlySet<string> GetAvailableSymbols()
        {
            if (_availableSymbols != null) return _availableSymbols;

            _availableSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ResourceAssembly.GetManifestResourceNames())
            {
                if (name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".svg"))
                {
                    string symbol = name[ResourcePrefix.Length..^4]; // strip prefix and .svg
                    _availableSymbols.Add(symbol);
                }
            }
            return _availableSymbols;
        }

        /// <summary>
        /// Parses a text string and extracts segments of plain text and mana symbol references.
        /// For example, "{2}{W}{U}" yields: Symbol("2"), Symbol("W"), Symbol("U").
        /// "Pay {2}{R}" yields: Text("Pay "), Symbol("2"), Symbol("R").
        /// </summary>
        public static List<ManaTextSegment> ParseManaText(string text)
        {
            var segments = new List<ManaTextSegment>();
            if (string.IsNullOrEmpty(text)) return segments;

            int i = 0;
            int textStart = 0;

            while (i < text.Length)
            {
                if (text[i] == '{')
                {
                    // Emit any plain text before this
                    if (i > textStart)
                        segments.Add(new ManaTextSegment(text[textStart..i], false));

                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        string symbol = text[(i + 1)..close];
                        segments.Add(new ManaTextSegment(symbol, true));
                        i = close + 1;
                        textStart = i;
                    }
                    else
                    {
                        // No closing brace — treat as plain text
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }

            // Remaining plain text
            if (textStart < text.Length)
                segments.Add(new ManaTextSegment(text[textStart..], false));

            return segments;
        }
    }

    public record ManaTextSegment(string Value, bool IsSymbol);
}
