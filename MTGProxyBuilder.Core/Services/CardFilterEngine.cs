namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Evaluates filter expressions against CardModel data.
    /// Supported fields: name, type, oracle, color, identity, rarity, set, artist, cmc, power, toughness, keyword, dfc
    /// </summary>
    public static class CardFilterEngine
    {
        private static readonly Dictionary<string, int> RarityOrder = new(StringComparer.OrdinalIgnoreCase)
        {
            ["common"] = 0, ["uncommon"] = 1, ["rare"] = 2, ["mythic"] = 3, ["special"] = 4, ["bonus"] = 5
        };

        /// <summary>Returns true if the card matches all filter tokens (AND logic, with OR/parens support).</summary>
        public static bool Matches(IReadOnlyList<FilterToken> filters, Models.CardModel card)
        {
            if (filters.Count == 0) return true;

            // Reuse FilterEvaluator's recursive descent, but with CardModel field extraction
            int pos = 0;
            return EvalOr(filters, card, ref pos);
        }

        private static bool EvalOr(IReadOnlyList<FilterToken> tokens, Models.CardModel card, ref int pos)
        {
            bool left = EvalAnd(tokens, card, ref pos);
            while (pos < tokens.Count && tokens[pos].Kind == TokenKind.Or)
            {
                pos++;
                bool right = EvalAnd(tokens, card, ref pos);
                left = left || right;
            }
            return left;
        }

        private static bool EvalAnd(IReadOnlyList<FilterToken> tokens, Models.CardModel card, ref int pos)
        {
            bool left = EvalPrimary(tokens, card, ref pos);
            while (pos < tokens.Count && tokens[pos].Kind != TokenKind.Or && tokens[pos].Kind != TokenKind.CloseParen)
            {
                bool right = EvalPrimary(tokens, card, ref pos);
                left = left && right;
            }
            return left;
        }

        private static bool EvalPrimary(IReadOnlyList<FilterToken> tokens, Models.CardModel card, ref int pos)
        {
            if (pos >= tokens.Count) return true;
            var token = tokens[pos];

            if (token.Kind == TokenKind.OpenParen)
            {
                pos++;
                bool result = EvalOr(tokens, card, ref pos);
                if (pos < tokens.Count && tokens[pos].Kind == TokenKind.CloseParen) pos++;
                return result;
            }

            if (token.Kind == TokenKind.Filter)
            {
                pos++;
                return EvalFilter(token, card);
            }

            pos++;
            return true;
        }

        private static bool EvalFilter(FilterToken token, Models.CardModel card)
        {
            return token.Field switch
            {
                FilterField.Name => MatchString(token, card.Name, substring: true),
                FilterField.Source => token.Value.ToLowerInvariant() switch
                {
                    // Map "source" field to various card text fields for flexibility
                    _ => MatchString(token, card.SetName, substring: true)
                        || MatchString(token, card.SetCode, substring: false)
                        || MatchString(token, card.Artist, substring: true)
                },
                FilterField.Tag => EvalCardTag(token, card),
                FilterField.Dpi => false, // Cards don't have DPI

                // Extended fields via value prefix parsing
                _ => EvalExtendedField(token, card)
            };
        }

        private static bool EvalCardTag(FilterToken token, Models.CardModel card)
        {
            // "tag" on cards maps to: type, rarity, color, keyword, dfc status
            string val = token.Value;
            return token.Op switch
            {
                FilterOp.Eq => TagMatches(val, card),
                FilterOp.Not => !TagMatches(val, card),
                _ => false
            };
        }

        private static bool TagMatches(string tag, Models.CardModel card)
        {
            string lower = tag.ToLowerInvariant();

            // Rarity
            if (card.Rarity.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;

            // Color names
            if (lower is "white" or "w") return card.Colors.Contains("W");
            if (lower is "blue" or "u") return card.Colors.Contains("U");
            if (lower is "black" or "b") return card.Colors.Contains("B");
            if (lower is "red" or "r") return card.Colors.Contains("R");
            if (lower is "green" or "g") return card.Colors.Contains("G");
            if (lower is "colorless") return string.IsNullOrEmpty(card.Colors);
            if (lower is "multicolor" or "multi") return card.Colors.Count(ch => ch == ',') >= 1;

            // Card type
            if (card.TypeLine.Contains(tag, StringComparison.OrdinalIgnoreCase)) return true;

            // Keywords
            if (card.Keywords.Contains(tag, StringComparison.OrdinalIgnoreCase)) return true;

            // Double-faced
            if (lower is "dfc" or "double-faced" or "doublefaced") return card.IsDoubleFaced;

            return false;
        }

        private static bool EvalExtendedField(FilterToken token, Models.CardModel card)
        {
            // Free text (no field prefix) — search across multiple fields
            string val = token.Value;
            if (token.Op == FilterOp.Eq)
            {
                return card.Name.Contains(val, StringComparison.OrdinalIgnoreCase)
                    || card.TypeLine.Contains(val, StringComparison.OrdinalIgnoreCase)
                    || card.OracleText.Contains(val, StringComparison.OrdinalIgnoreCase)
                    || card.SetName.Contains(val, StringComparison.OrdinalIgnoreCase)
                    || card.Artist.Contains(val, StringComparison.OrdinalIgnoreCase)
                    || card.Keywords.Contains(val, StringComparison.OrdinalIgnoreCase);
            }
            if (token.Op == FilterOp.Not)
            {
                return !card.Name.Contains(val, StringComparison.OrdinalIgnoreCase)
                    && !card.TypeLine.Contains(val, StringComparison.OrdinalIgnoreCase)
                    && !card.OracleText.Contains(val, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private static bool MatchString(FilterToken token, string actual, bool substring)
        {
            return token.Op switch
            {
                FilterOp.Eq => substring
                    ? actual.Contains(token.Value, StringComparison.OrdinalIgnoreCase)
                    : actual.Equals(token.Value, StringComparison.OrdinalIgnoreCase),
                FilterOp.Not => substring
                    ? !actual.Contains(token.Value, StringComparison.OrdinalIgnoreCase)
                    : !actual.Equals(token.Value, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        // ================================================================
        //  SORT
        // ================================================================

        /// <summary>
        /// Applies a chain of sort pills to a card sequence. Each pill is a "then by".
        /// Sort pill format: "field" or "field:desc" for descending.
        /// </summary>
        public static IEnumerable<Models.CardModel> ApplySort(IEnumerable<Models.CardModel> source, IReadOnlyList<SortPill> pills)
        {
            if (pills.Count == 0) return source;

            IOrderedEnumerable<Models.CardModel>? ordered = null;

            for (int i = 0; i < pills.Count; i++)
            {
                var pill = pills[i];
                Func<Models.CardModel, object> keySelector = GetSortKey(pill.Field);

                if (i == 0)
                    ordered = pill.Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector);
                else
                    ordered = pill.Descending ? ordered!.ThenByDescending(keySelector) : ordered!.ThenBy(keySelector);
            }

            return ordered ?? source;
        }

        private static Func<Models.CardModel, object> GetSortKey(string field)
        {
            return field.ToLowerInvariant() switch
            {
                "name" => c => c.Name,
                "cmc" or "mv" or "mana value" => c => c.CMC,
                "rarity" => c => RarityOrder.GetValueOrDefault(c.Rarity, -1),
                "color" or "colors" => c => c.Colors,
                "type" or "type line" => c => c.TypeLine,
                "set" => c => c.SetName + c.CollectorNumber.PadLeft(5, '0'),
                "artist" => c => c.Artist,
                "collector" or "collector #" or "number" => c => c.SetCode + (int.TryParse(c.CollectorNumber, out var n) ? n : 9999).ToString("D5"),
                "power" or "pow" => c => float.TryParse(c.Power, out var p) ? p : -1f,
                "toughness" or "tou" => c => float.TryParse(c.Toughness, out var t) ? t : -1f,
                "date" or "date added" => c => c.DateAdded,
                _ => c => c.Name
            };
        }
    }

    public class SortPill
    {
        public string Field { get; init; } = string.Empty;
        public bool Descending { get; init; }
        public string DisplayText => Descending ? $"{Field} ↓" : $"{Field} ↑";

        /// <summary>Parses "field" or "field:desc" into a SortPill.</summary>
        public static SortPill Parse(string text)
        {
            text = text.Trim();
            bool desc = false;

            if (text.EndsWith(":desc", StringComparison.OrdinalIgnoreCase) || text.EndsWith(":d", StringComparison.OrdinalIgnoreCase))
            {
                desc = true;
                text = text[..text.LastIndexOf(':')].Trim();
            }
            else if (text.EndsWith(":asc", StringComparison.OrdinalIgnoreCase) || text.EndsWith(":a", StringComparison.OrdinalIgnoreCase))
            {
                text = text[..text.LastIndexOf(':')].Trim();
            }

            return new SortPill { Field = text, Descending = desc };
        }
    }
}
