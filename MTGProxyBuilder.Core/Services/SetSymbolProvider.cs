using System.Reflection;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Provides access to embedded set symbol SVG files.
    /// Files are named {setCode}-{rarity}.svg (e.g. "3ed-r.svg").
    /// All lookups are case-insensitive.
    /// </summary>
    public static class SetSymbolProvider
    {
        private static readonly Assembly ResourceAssembly = typeof(SetSymbolProvider).Assembly;
        private static readonly string ResourcePrefix = "MTGProxyBuilder.Core.SetSymbols.";
        private static readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> RarityMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = "c",
            ["uncommon"] = "u",
            ["rare"] = "r",
            ["mythic"] = "m",
            ["special"] = "t",
            ["bonus"] = "t",
            ["timeshifted"] = "t",
            ["c"] = "c",
            ["u"] = "u",
            ["r"] = "r",
            ["m"] = "m",
            ["s"] = "t",
            ["t"] = "t"
        };

        /// <summary>
        /// Returns the SVG content for a set symbol given a set code and rarity.
        /// Falls back to common if the specific rarity is not found.
        /// Returns null if no symbol exists for the set.
        /// </summary>
        public static string? GetSvgContent(string setCode, string rarity)
        {
            if (string.IsNullOrEmpty(setCode)) return null;

            string rarityCode = RarityMap.GetValueOrDefault(rarity?.Trim() ?? "", "c");
            string key = $"{setCode.ToLowerInvariant()}-{rarityCode}";

            if (_cache.TryGetValue(key, out var cached))
                return cached;

            // Try exact rarity
            string setLower = setCode.ToLowerInvariant();
            string? svg = LoadResource(key);

            // Fallback chain: try alternate special code (t↔s), then common
            if (svg == null && rarityCode != "c")
            {
                // Try alternate special variant
                if (rarityCode == "t")
                    svg = LoadResource($"{setLower}-s");
                else if (rarityCode == "s")
                    svg = LoadResource($"{setLower}-t");

                // Fall back to common
                if (svg == null)
                    svg = LoadResource($"{setLower}-c");

                if (svg != null)
                {
                    _cache[key] = svg;
                    return svg;
                }
            }

            if (svg != null)
                _cache[key] = svg;

            return svg;
        }

        /// <summary>Returns true if a set symbol exists for the given set code (any rarity).</summary>
        public static bool HasSymbol(string setCode)
        {
            if (string.IsNullOrEmpty(setCode)) return false;
            string prefix = $"{ResourcePrefix}{setCode.ToLowerInvariant()}-";
            return ResourceAssembly.GetManifestResourceNames()
                .Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static string? LoadResource(string key)
        {
            string resourceName = $"{ResourcePrefix}{key}.svg";
            using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
