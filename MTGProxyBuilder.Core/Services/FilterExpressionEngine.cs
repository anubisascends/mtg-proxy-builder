namespace MTGProxyBuilder.Core.Services
{
    // ================================================================
    //  ENUMS
    // ================================================================

    public enum FilterField
    {
        Name,
        Source,
        Dpi,
        Tag
    }

    public enum FilterOp
    {
        Eq,
        Not,
        Gt,
        Lt,
        Gte,
        Lte,
        In
    }

    public enum TokenKind
    {
        Filter,
        And,
        Or,
        OpenParen,
        CloseParen
    }

    // ================================================================
    //  TOKEN MODEL
    // ================================================================

    public class FilterToken
    {
        public TokenKind Kind { get; init; }
        public FilterField Field { get; init; }
        public FilterOp Op { get; init; }
        public string Value { get; init; } = string.Empty;
        public List<string> Values { get; init; } = [];
        public string DisplayText { get; init; } = string.Empty;
    }

    // ================================================================
    //  PARSER
    // ================================================================

    public static class FilterParser
    {
        public static IReadOnlyList<FilterToken> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            var rawTokens = Tokenize(text.Trim());
            var result = new List<FilterToken>();

            foreach (var raw in rawTokens)
            {
                var token = ParseRawToken(raw);
                if (token != null)
                    result.Add(token);
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Tokenizer — splits on whitespace, respecting in[...] brackets
        // ----------------------------------------------------------------

        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            int i = 0;

            while (i < input.Length)
            {
                if (char.IsWhiteSpace(input[i])) { i++; continue; }

                int start = i;

                // Parentheses are individual tokens
                if (input[i] == '(' || input[i] == ')')
                {
                    tokens.Add(input[i].ToString());
                    i++;
                    continue;
                }

                // Consume until whitespace or paren, but keep in[...] intact
                while (i < input.Length && !char.IsWhiteSpace(input[i]) && input[i] != '(' && input[i] != ')')
                {
                    // If we hit '[', consume until closing ']'
                    if (input[i] == '[')
                    {
                        while (i < input.Length && input[i] != ']') i++;
                        if (i < input.Length) i++; // consume ']'
                        break;
                    }
                    i++;
                }

                tokens.Add(input[start..i]);
            }

            return tokens;
        }

        /// <summary>Parses a single pill's text into one FilterToken. Does NOT split on whitespace.</summary>
        public static FilterToken ParseSingle(string text)
        {
            return ParseRawToken(text.Trim()) ?? FreeTextToken(text.Trim());
        }

        // ----------------------------------------------------------------
        //  Token parser — converts a raw string to a FilterToken
        // ----------------------------------------------------------------

        private static FilterToken? ParseRawToken(string raw)
        {
            // OR keyword or pipe
            if (raw.Equals("OR", StringComparison.OrdinalIgnoreCase) || raw == "|")
                return new FilterToken { Kind = TokenKind.Or, DisplayText = raw };

            if (raw == "(")
                return new FilterToken { Kind = TokenKind.OpenParen, DisplayText = raw };

            if (raw == ")")
                return new FilterToken { Kind = TokenKind.CloseParen, DisplayText = raw };

            // field:value syntax
            int colonIdx = raw.IndexOf(':');
            if (colonIdx > 0)
            {
                string fieldPart = raw[..colonIdx].ToLowerInvariant();
                string rest = raw[(colonIdx + 1)..];

                // Unknown field names fall through to free-text treatment
                if (!IsKnownField(fieldPart))
                    return FreeTextToken(raw);

                FilterField field = fieldPart switch
                {
                    "name"   => FilterField.Name,
                    "source" => FilterField.Source,
                    "dpi"    => FilterField.Dpi,
                    "tag"    => FilterField.Tag,
                    _        => FilterField.Name
                };

                return ParseFieldValue(field, rest, raw);
            }

            // Free text — name substring match
            return FreeTextToken(raw);
        }

        private static bool IsKnownField(string fieldName) =>
            fieldName is "name" or "source" or "dpi" or "tag";

        private static FilterToken FreeTextToken(string raw) =>
            new FilterToken
            {
                Kind = TokenKind.Filter,
                Field = FilterField.Name,
                Op = FilterOp.Eq,
                Value = raw,
                DisplayText = raw
            };

        private static FilterToken ParseFieldValue(FilterField field, string rest, string displayText)
        {
            // in[val1,val2,...] operator
            if (rest.StartsWith("in[", StringComparison.OrdinalIgnoreCase) && rest.EndsWith(']'))
            {
                var inner = rest[3..^1]; // strip "in[" and "]"
                var values = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .ToList();
                return new FilterToken
                {
                    Kind = TokenKind.Filter,
                    Field = field,
                    Op = FilterOp.In,
                    Values = values,
                    DisplayText = displayText
                };
            }

            // Comparison operators: >= <= > < ! =
            (FilterOp op, string value) = rest switch
            {
                var s when s.StartsWith(">=") => (FilterOp.Gte, s[2..]),
                var s when s.StartsWith("<=") => (FilterOp.Lte, s[2..]),
                var s when s.StartsWith(">")  => (FilterOp.Gt,  s[1..]),
                var s when s.StartsWith("<")  => (FilterOp.Lt,  s[1..]),
                var s when s.StartsWith("!")  => (FilterOp.Not, s[1..]),
                var s when s.StartsWith("=")  => (FilterOp.Eq,  s[1..]),
                _                             => (FilterOp.Eq,  rest)
            };

            return new FilterToken
            {
                Kind = TokenKind.Filter,
                Field = field,
                Op = op,
                Value = value,
                DisplayText = displayText
            };
        }
    }

    // ================================================================
    //  TILE DATA
    // ================================================================

    public record TileData(string Name, string Source, int Dpi, List<string> Tags);

    // ================================================================
    //  EVALUATOR
    // ================================================================

    public static class FilterEvaluator
    {
        public static bool Evaluate(IReadOnlyList<FilterToken> tokens, TileData tile)
        {
            if (tokens.Count == 0)
                return true;

            var ctx = new EvalContext(tokens, tile);
            return ctx.EvalOr();
        }

        // ----------------------------------------------------------------
        //  Recursive descent evaluator
        // ----------------------------------------------------------------

        private sealed class EvalContext(IReadOnlyList<FilterToken> tokens, TileData tile)
        {
            private readonly IReadOnlyList<FilterToken> _tokens = tokens;
            private readonly TileData _tile = tile;
            private int _pos = 0;

            // OR has lowest precedence
            public bool EvalOr()
            {
                bool result = EvalAnd();

                while (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Or)
                {
                    _pos++; // consume OR
                    bool right = EvalAnd();
                    result = result || right;
                }

                return result;
            }

            // AND is implicit between adjacent filter tokens / groups
            private bool EvalAnd()
            {
                bool result = EvalPrimary();

                while (_pos < _tokens.Count
                    && _tokens[_pos].Kind != TokenKind.Or
                    && _tokens[_pos].Kind != TokenKind.CloseParen)
                {
                    bool right = EvalPrimary();
                    result = result && right;
                }

                return result;
            }

            private bool EvalPrimary()
            {
                if (_pos >= _tokens.Count)
                    return true;

                var token = _tokens[_pos];

                if (token.Kind == TokenKind.OpenParen)
                {
                    _pos++; // consume '('
                    bool inner = EvalOr();
                    if (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.CloseParen)
                        _pos++; // consume ')'
                    return inner;
                }

                if (token.Kind == TokenKind.Filter)
                {
                    _pos++;
                    return MatchToken(token, _tile);
                }

                // Skip unexpected structural tokens
                _pos++;
                return true;
            }

            private static bool MatchToken(FilterToken token, TileData tile) =>
                token.Field switch
                {
                    FilterField.Name   => MatchName(token, tile),
                    FilterField.Source => MatchSource(token, tile),
                    FilterField.Dpi    => MatchDpi(token, tile),
                    FilterField.Tag    => MatchTag(token, tile),
                    _ => true
                };

            // Name: substring match (case-insensitive) for Eq, negated for Not
            private static bool MatchName(FilterToken token, TileData tile) =>
                token.Op switch
                {
                    FilterOp.Not => !tile.Name.Contains(token.Value, StringComparison.OrdinalIgnoreCase),
                    _            =>  tile.Name.Contains(token.Value, StringComparison.OrdinalIgnoreCase)
                };

            // Source: exact match (case-insensitive) for Eq/Not, any-match for In
            private static bool MatchSource(FilterToken token, TileData tile) =>
                token.Op switch
                {
                    FilterOp.Not => !tile.Source.Equals(token.Value, StringComparison.OrdinalIgnoreCase),
                    FilterOp.In  =>  token.Values.Any(v => v.Equals(tile.Source, StringComparison.OrdinalIgnoreCase)),
                    _            =>  tile.Source.Equals(token.Value, StringComparison.OrdinalIgnoreCase)
                };

            // Dpi: numeric comparison for all operators
            private static bool MatchDpi(FilterToken token, TileData tile)
            {
                if (!int.TryParse(token.Value, out int target))
                    return true;

                return token.Op switch
                {
                    FilterOp.Eq  => tile.Dpi == target,
                    FilterOp.Not => tile.Dpi != target,
                    FilterOp.Gt  => tile.Dpi > target,
                    FilterOp.Lt  => tile.Dpi < target,
                    FilterOp.Gte => tile.Dpi >= target,
                    FilterOp.Lte => tile.Dpi <= target,
                    _            => true
                };
            }

            // Tag: list membership check for Eq/Not, any-match for In
            private static bool MatchTag(FilterToken token, TileData tile) =>
                token.Op switch
                {
                    FilterOp.Not => !tile.Tags.Any(t => t.Equals(token.Value, StringComparison.OrdinalIgnoreCase)),
                    FilterOp.In  =>  token.Values.Any(v => tile.Tags.Any(t => t.Equals(v, StringComparison.OrdinalIgnoreCase))),
                    _            =>  tile.Tags.Any(t => t.Equals(token.Value, StringComparison.OrdinalIgnoreCase))
                };
        }
    }
}
