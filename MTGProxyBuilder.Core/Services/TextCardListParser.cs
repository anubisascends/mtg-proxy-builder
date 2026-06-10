using System.Text.RegularExpressions;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Parses a multi-line card list in the format:
    /// [qty] [name] ([set code]) [card number]
    /// All parts except name are optional. Default qty is 1.
    /// </summary>
    public static class TextCardListParser
    {
        // Matches: optional qty, card name, optional (SET), optional collector number
        // Examples:
        //   4 Lightning Bolt (3ED) 123
        //   Lightning Bolt (3ED)
        //   2 Lightning Bolt
        //   Lightning Bolt
        //   4 Lightning Bolt (MH2) 290
        private static readonly Regex LinePattern = new(
            @"^(?:(\d+)\s+)?(.+?)(?:\s+\(([A-Za-z0-9]+)\)(?:\s+(\S+))?)?$",
            RegexOptions.Compiled);

        public static List<CardListEntry> Parse(string text)
        {
            var entries = new List<CardListEntry>();
            if (string.IsNullOrWhiteSpace(text)) return entries;

            foreach (var rawLine in text.Split('\n', '\r'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("//") || line.StartsWith("#")) continue; // comments

                var match = LinePattern.Match(line);
                if (!match.Success) continue;

                int qty = 1;
                if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var parsedQty) && parsedQty > 0)
                    qty = parsedQty;

                string name = match.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                string? setCode = match.Groups[3].Success ? match.Groups[3].Value : null;
                string? collectorNumber = match.Groups[4].Success ? match.Groups[4].Value : null;

                entries.Add(new CardListEntry
                {
                    Quantity = qty,
                    Name = name,
                    SetCode = setCode,
                    CollectorNumber = collectorNumber
                });
            }

            return entries;
        }

        /// <summary>Builds a Scryfall search query for a card list entry.</summary>
        public static string BuildScryfallQuery(CardListEntry entry)
        {
            string query = $"!\"{entry.Name}\"";
            if (!string.IsNullOrEmpty(entry.SetCode))
                query += $" set:{entry.SetCode}";
            if (!string.IsNullOrEmpty(entry.CollectorNumber))
                query += $" number:{entry.CollectorNumber}";
            return query;
        }
    }

    public class CardListEntry
    {
        public int Quantity { get; init; } = 1;
        public string Name { get; init; } = string.Empty;
        public string? SetCode { get; init; }
        public string? CollectorNumber { get; init; }
    }
}
