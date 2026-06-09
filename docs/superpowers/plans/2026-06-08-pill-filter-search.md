# Pill-Based Filter Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the art selector's SearchBar with a pill-based filter bar supporting structured expressions (`dpi:>800`, `source:Chilli_Axe`), AND/OR/parentheses, and contextual autocomplete.

**Architecture:** Three layers — (1) `FilterExpressionEngine` in Core for parsing/evaluating filter expressions, fully testable with no UI deps; (2) `PillFilterBar` WPF control for the interactive pill input with autocomplete; (3) Integration into `ArtSelectorDialog` replacing the SearchBar. TileInfo gains a `Dpi` field for numeric filtering.

**Tech Stack:** WPF, C#, xUnit for engine tests

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `MTGProxyBuilder.Core/Services/FilterExpressionEngine.cs` | Create | FilterToken model, FilterParser, FilterEvaluator |
| `MTGProxyBuilder.Tests/Services/FilterExpressionEngineTests.cs` | Create | Unit tests for parser and evaluator |
| `MTGProxyBuilder.UI/Controls/PillFilterBar.xaml` | Create | XAML layout: pill WrapPanel + TextBox + autocomplete Popup |
| `MTGProxyBuilder.UI/Controls/PillFilterBar.xaml.cs` | Create | Pill management, autocomplete logic, input handling |
| `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml` | Modify | Swap SearchBar for PillFilterBar in both tabs |
| `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs` | Modify | New ApplyFilters, remove old filter code, wire PillFilterBar |
| `MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs` | Modify | Source/tag click callbacks now add pills |

---

### Task 1: Create FilterToken model and FilterParser

**Files:**
- Create: `MTGProxyBuilder.Core/Services/FilterExpressionEngine.cs`
- Create: `MTGProxyBuilder.Tests/Services/FilterExpressionEngineTests.cs`

- [ ] **Step 1: Create the FilterToken model and parser**

Create `MTGProxyBuilder.Core/Services/FilterExpressionEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGProxyBuilder.Core.Services
{
    public enum FilterField { Name, Source, Dpi, Tag }

    public enum FilterOp { Eq, Not, Gt, Lt, Gte, Lte, In }

    public enum TokenKind { Filter, And, Or, OpenParen, CloseParen }

    public class FilterToken
    {
        public TokenKind Kind { get; init; }

        // Only meaningful when Kind == Filter
        public FilterField Field { get; init; }
        public FilterOp Op { get; init; }
        public string Value { get; init; } = string.Empty;
        public List<string> Values { get; init; } = new(); // For 'in' operator

        public string DisplayText { get; init; } = string.Empty;

        public override string ToString() => DisplayText;
    }

    public static class FilterParser
    {
        /// <summary>Parses a single pill's raw text into a FilterToken.</summary>
        public static FilterToken Parse(string text)
        {
            text = text.Trim();

            // Combinators
            if (text.Equals("OR", StringComparison.OrdinalIgnoreCase) || text == "|")
                return new FilterToken { Kind = TokenKind.Or, DisplayText = "OR" };
            if (text == "(")
                return new FilterToken { Kind = TokenKind.OpenParen, DisplayText = "(" };
            if (text == ")")
                return new FilterToken { Kind = TokenKind.CloseParen, DisplayText = ")" };

            // Field:value syntax
            int colonIdx = text.IndexOf(':');
            if (colonIdx > 0)
            {
                string fieldStr = text[..colonIdx].Trim();
                string rest = text[(colonIdx + 1)..].Trim();

                if (TryParseField(fieldStr, out var field))
                    return ParseFieldValue(field, rest, text);
            }

            // Free text = name search
            return new FilterToken
            {
                Kind = TokenKind.Filter,
                Field = FilterField.Name,
                Op = FilterOp.Eq,
                Value = text,
                DisplayText = text
            };
        }

        private static bool TryParseField(string s, out FilterField field)
        {
            field = s.ToLowerInvariant() switch
            {
                "name" => FilterField.Name,
                "source" => FilterField.Source,
                "dpi" => FilterField.Dpi,
                "tag" => FilterField.Tag,
                _ => FilterField.Name
            };
            return s.ToLowerInvariant() is "name" or "source" or "dpi" or "tag";
        }

        private static FilterToken ParseFieldValue(FilterField field, string rest, string originalText)
        {
            // in[...] operator
            if (rest.StartsWith("in[", StringComparison.OrdinalIgnoreCase) && rest.EndsWith("]"))
            {
                string inner = rest[3..^1];
                var values = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                return new FilterToken
                {
                    Kind = TokenKind.Filter,
                    Field = field,
                    Op = FilterOp.In,
                    Values = values,
                    DisplayText = originalText
                };
            }

            // Operator prefix
            FilterOp op;
            string value;

            if (rest.StartsWith(">="))
            {
                op = FilterOp.Gte; value = rest[2..].Trim();
            }
            else if (rest.StartsWith("<="))
            {
                op = FilterOp.Lte; value = rest[2..].Trim();
            }
            else if (rest.StartsWith(">"))
            {
                op = FilterOp.Gt; value = rest[1..].Trim();
            }
            else if (rest.StartsWith("<"))
            {
                op = FilterOp.Lt; value = rest[1..].Trim();
            }
            else if (rest.StartsWith("!"))
            {
                op = FilterOp.Not; value = rest[1..].Trim();
            }
            else if (rest.StartsWith("="))
            {
                op = FilterOp.Eq; value = rest[1..].Trim();
            }
            else
            {
                op = FilterOp.Eq; value = rest;
            }

            return new FilterToken
            {
                Kind = TokenKind.Filter,
                Field = field,
                Op = op,
                Value = value,
                DisplayText = originalText
            };
        }
    }
}
```

- [ ] **Step 2: Write parser tests**

Create `MTGProxyBuilder.Tests/Services/FilterExpressionEngineTests.cs`:

```csharp
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services
{
    public class FilterParserTests
    {
        [Fact]
        public void Parse_FreeText_BecomesNameFilter()
        {
            var token = FilterParser.Parse("Lightning");
            Assert.Equal(TokenKind.Filter, token.Kind);
            Assert.Equal(FilterField.Name, token.Field);
            Assert.Equal(FilterOp.Eq, token.Op);
            Assert.Equal("Lightning", token.Value);
        }

        [Fact]
        public void Parse_DpiGreaterThan()
        {
            var token = FilterParser.Parse("dpi:>800");
            Assert.Equal(FilterField.Dpi, token.Field);
            Assert.Equal(FilterOp.Gt, token.Op);
            Assert.Equal("800", token.Value);
        }

        [Fact]
        public void Parse_DpiGreaterThanOrEqual()
        {
            var token = FilterParser.Parse("dpi:>=1200");
            Assert.Equal(FilterField.Dpi, token.Field);
            Assert.Equal(FilterOp.Gte, token.Op);
            Assert.Equal("1200", token.Value);
        }

        [Fact]
        public void Parse_DpiEquals_Shorthand()
        {
            var token = FilterParser.Parse("dpi:600");
            Assert.Equal(FilterField.Dpi, token.Field);
            Assert.Equal(FilterOp.Eq, token.Op);
            Assert.Equal("600", token.Value);
        }

        [Fact]
        public void Parse_DpiNot()
        {
            var token = FilterParser.Parse("dpi:!600");
            Assert.Equal(FilterField.Dpi, token.Field);
            Assert.Equal(FilterOp.Not, token.Op);
            Assert.Equal("600", token.Value);
        }

        [Fact]
        public void Parse_SourceEquals()
        {
            var token = FilterParser.Parse("source:Chilli_Axe");
            Assert.Equal(FilterField.Source, token.Field);
            Assert.Equal(FilterOp.Eq, token.Op);
            Assert.Equal("Chilli_Axe", token.Value);
        }

        [Fact]
        public void Parse_SourceNot()
        {
            var token = FilterParser.Parse("source:!Chilli_Axe");
            Assert.Equal(FilterField.Source, token.Field);
            Assert.Equal(FilterOp.Not, token.Op);
            Assert.Equal("Chilli_Axe", token.Value);
        }

        [Fact]
        public void Parse_SourceIn()
        {
            var token = FilterParser.Parse("source:in[Chilli_Axe,Psilosx]");
            Assert.Equal(FilterField.Source, token.Field);
            Assert.Equal(FilterOp.In, token.Op);
            Assert.Equal(new List<string> { "Chilli_Axe", "Psilosx" }, token.Values);
        }

        [Fact]
        public void Parse_TagEquals()
        {
            var token = FilterParser.Parse("tag:Retro");
            Assert.Equal(FilterField.Tag, token.Field);
            Assert.Equal(FilterOp.Eq, token.Op);
            Assert.Equal("Retro", token.Value);
        }

        [Fact]
        public void Parse_TagNot()
        {
            var token = FilterParser.Parse("tag:!NSFW");
            Assert.Equal(FilterField.Tag, token.Field);
            Assert.Equal(FilterOp.Not, token.Op);
            Assert.Equal("NSFW", token.Value);
        }

        [Fact]
        public void Parse_NameContains()
        {
            var token = FilterParser.Parse("name:Bolt");
            Assert.Equal(FilterField.Name, token.Field);
            Assert.Equal(FilterOp.Eq, token.Op);
            Assert.Equal("Bolt", token.Value);
        }

        [Fact]
        public void Parse_OR()
        {
            var token = FilterParser.Parse("OR");
            Assert.Equal(TokenKind.Or, token.Kind);
        }

        [Fact]
        public void Parse_Pipe_As_OR()
        {
            var token = FilterParser.Parse("|");
            Assert.Equal(TokenKind.Or, token.Kind);
        }

        [Fact]
        public void Parse_OpenParen()
        {
            var token = FilterParser.Parse("(");
            Assert.Equal(TokenKind.OpenParen, token.Kind);
        }

        [Fact]
        public void Parse_CloseParen()
        {
            var token = FilterParser.Parse(")");
            Assert.Equal(TokenKind.CloseParen, token.Kind);
        }

        [Fact]
        public void Parse_DpiLessThan()
        {
            var token = FilterParser.Parse("dpi:<600");
            Assert.Equal(FilterOp.Lt, token.Op);
            Assert.Equal("600", token.Value);
        }

        [Fact]
        public void Parse_DpiLessThanOrEqual()
        {
            var token = FilterParser.Parse("dpi:<=800");
            Assert.Equal(FilterOp.Lte, token.Op);
            Assert.Equal("800", token.Value);
        }

        [Fact]
        public void Parse_TagIn()
        {
            var token = FilterParser.Parse("tag:in[Retro,Frame,Extended-Art]");
            Assert.Equal(FilterOp.In, token.Op);
            Assert.Equal(3, token.Values.Count);
            Assert.Contains("Retro", token.Values);
            Assert.Contains("Frame", token.Values);
            Assert.Contains("Extended-Art", token.Values);
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~FilterParserTests"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.Core/Services/FilterExpressionEngine.cs MTGProxyBuilder.Tests/Services/FilterExpressionEngineTests.cs
git commit -m "feat: add FilterToken model and FilterParser with tests"
```

---

### Task 2: Create FilterEvaluator

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/FilterExpressionEngine.cs`
- Modify: `MTGProxyBuilder.Tests/Services/FilterExpressionEngineTests.cs`

- [ ] **Step 1: Define TileData record for evaluation input**

Add to `FilterExpressionEngine.cs` after the `FilterParser` class:

```csharp
    /// <summary>Flattened tile data used for filter evaluation.</summary>
    public record TileData(string Name, string Source, int Dpi, List<string> Tags);

    public static class FilterEvaluator
    {
        /// <summary>Evaluates a list of filter tokens against tile data. Returns true if the tile matches.</summary>
        public static bool Evaluate(IReadOnlyList<FilterToken> tokens, TileData tile)
        {
            if (tokens.Count == 0) return true;

            // Build expression tree from tokens, then evaluate
            int pos = 0;
            return EvalOr(tokens, tile, ref pos);
        }

        // OR has lowest precedence
        private static bool EvalOr(IReadOnlyList<FilterToken> tokens, TileData tile, ref int pos)
        {
            bool left = EvalAnd(tokens, tile, ref pos);

            while (pos < tokens.Count && tokens[pos].Kind == TokenKind.Or)
            {
                pos++; // skip OR
                bool right = EvalAnd(tokens, tile, ref pos);
                left = left || right;
            }

            return left;
        }

        // AND has higher precedence (implicit between filters)
        private static bool EvalAnd(IReadOnlyList<FilterToken> tokens, TileData tile, ref int pos)
        {
            bool left = EvalPrimary(tokens, tile, ref pos);

            // Implicit AND: next token is a filter or open paren (not OR, not close paren, not end)
            while (pos < tokens.Count
                && tokens[pos].Kind != TokenKind.Or
                && tokens[pos].Kind != TokenKind.CloseParen)
            {
                bool right = EvalPrimary(tokens, tile, ref pos);
                left = left && right;
            }

            return left;
        }

        private static bool EvalPrimary(IReadOnlyList<FilterToken> tokens, TileData tile, ref int pos)
        {
            if (pos >= tokens.Count) return true;

            var token = tokens[pos];

            if (token.Kind == TokenKind.OpenParen)
            {
                pos++; // skip (
                bool result = EvalOr(tokens, tile, ref pos);
                if (pos < tokens.Count && tokens[pos].Kind == TokenKind.CloseParen)
                    pos++; // skip )
                return result;
            }

            if (token.Kind == TokenKind.Filter)
            {
                pos++;
                return EvalFilter(token, tile);
            }

            // Skip unexpected tokens
            pos++;
            return true;
        }

        private static bool EvalFilter(FilterToken token, TileData tile)
        {
            return token.Field switch
            {
                FilterField.Name => EvalString(token, tile.Name, substring: true),
                FilterField.Source => token.Op == FilterOp.In
                    ? token.Values.Any(v => v.Equals(tile.Source, StringComparison.OrdinalIgnoreCase))
                    : EvalString(token, tile.Source, substring: false),
                FilterField.Dpi => EvalNumeric(token, tile.Dpi),
                FilterField.Tag => EvalTag(token, tile.Tags),
                _ => true
            };
        }

        private static bool EvalString(FilterToken token, string actual, bool substring)
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

        private static bool EvalNumeric(FilterToken token, int actual)
        {
            if (!int.TryParse(token.Value, out int target)) return true;
            return token.Op switch
            {
                FilterOp.Eq => actual == target,
                FilterOp.Not => actual != target,
                FilterOp.Gt => actual > target,
                FilterOp.Lt => actual < target,
                FilterOp.Gte => actual >= target,
                FilterOp.Lte => actual <= target,
                _ => true
            };
        }

        private static bool EvalTag(FilterToken token, List<string> tags)
        {
            return token.Op switch
            {
                FilterOp.Eq => tags.Any(t => t.Equals(token.Value, StringComparison.OrdinalIgnoreCase)),
                FilterOp.Not => !tags.Any(t => t.Equals(token.Value, StringComparison.OrdinalIgnoreCase)),
                FilterOp.In => token.Values.Any(v => tags.Any(t => t.Equals(v, StringComparison.OrdinalIgnoreCase))),
                _ => true
            };
        }
    }
```

- [ ] **Step 2: Write evaluator tests**

Add to `FilterExpressionEngineTests.cs`:

```csharp
    public class FilterEvaluatorTests
    {
        private static readonly TileData HighDpiRetro = new("Lightning Bolt (Full Art)", "Chilli_Axe", 1200, new List<string> { "Retro", "Frame" });
        private static readonly TileData LowDpiPlain = new("Lightning Bolt", "MrTeferi", 300, new List<string>());
        private static readonly TileData ScryfallCard = new("Lightning Bolt", "Scryfall", 0, new List<string>());

        [Fact]
        public void EmptyFilters_MatchesEverything()
        {
            Assert.True(FilterEvaluator.Evaluate(new List<FilterToken>(), HighDpiRetro));
        }

        [Fact]
        public void DpiGreaterThan_Matches()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("dpi:>800") };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.False(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
        }

        [Fact]
        public void SourceEquals_CaseInsensitive()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("source:chilli_axe") };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.False(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
        }

        [Fact]
        public void SourceIn_MatchesAny()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("source:in[Chilli_Axe,MrTeferi]") };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.True(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
            Assert.False(FilterEvaluator.Evaluate(tokens, ScryfallCard));
        }

        [Fact]
        public void TagEquals_Matches()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("tag:Retro") };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.False(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
        }

        [Fact]
        public void TagNot_Excludes()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("tag:!Retro") };
            Assert.False(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.True(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
        }

        [Fact]
        public void NameSubstring_Matches()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("Bolt") };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.True(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
        }

        [Fact]
        public void NameSubstring_NoMatch()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("Counterspell") };
            Assert.False(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
        }

        [Fact]
        public void ImplicitAND_BothMustMatch()
        {
            var tokens = new List<FilterToken>
            {
                FilterParser.Parse("dpi:>800"),
                FilterParser.Parse("source:Chilli_Axe")
            };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.False(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
        }

        [Fact]
        public void OR_EitherCanMatch()
        {
            var tokens = new List<FilterToken>
            {
                FilterParser.Parse("source:Chilli_Axe"),
                FilterParser.Parse("OR"),
                FilterParser.Parse("source:MrTeferi")
            };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.True(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
            Assert.False(FilterEvaluator.Evaluate(tokens, ScryfallCard));
        }

        [Fact]
        public void Parentheses_GroupWithOR()
        {
            // dpi:>800 OR (source:MrTeferi tag:Retro)
            // Should match: HighDpiRetro (dpi>800), NOT LowDpiPlain (300 dpi, MrTeferi but no Retro tag)
            var tokens = new List<FilterToken>
            {
                FilterParser.Parse("dpi:>800"),
                FilterParser.Parse("OR"),
                FilterParser.Parse("("),
                FilterParser.Parse("source:MrTeferi"),
                FilterParser.Parse("tag:Retro"),
                FilterParser.Parse(")")
            };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.False(FilterEvaluator.Evaluate(tokens, LowDpiPlain)); // MrTeferi but no Retro tag
        }

        [Fact]
        public void SourceNot_Excludes()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("source:!Scryfall") };
            Assert.True(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
            Assert.False(FilterEvaluator.Evaluate(tokens, ScryfallCard));
        }

        [Fact]
        public void DpiLessThan()
        {
            var tokens = new List<FilterToken> { FilterParser.Parse("dpi:<600") };
            Assert.True(FilterEvaluator.Evaluate(tokens, LowDpiPlain));
            Assert.False(FilterEvaluator.Evaluate(tokens, HighDpiRetro));
        }
    }
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~FilterEvaluatorTests"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.Core/Services/FilterExpressionEngine.cs MTGProxyBuilder.Tests/Services/FilterExpressionEngineTests.cs
git commit -m "feat: add FilterEvaluator with AND/OR/parentheses support and tests"
```

---

### Task 3: Create PillFilterBar WPF control

**Files:**
- Create: `MTGProxyBuilder.UI/Controls/PillFilterBar.xaml`
- Create: `MTGProxyBuilder.UI/Controls/PillFilterBar.xaml.cs`

This is the biggest task. The PillFilterBar is a custom control containing a WrapPanel of pill borders, a TextBox for input, and a Popup for autocomplete.

- [ ] **Step 1: Create XAML**

Create `MTGProxyBuilder.UI/Controls/PillFilterBar.xaml`:

```xml
<UserControl x:Class="MTGProxyBuilder.UI.Controls.PillFilterBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Border Background="#2D2D30" BorderBrush="#555" BorderThickness="1" CornerRadius="4" Padding="4,2">
            <ScrollViewer VerticalScrollBarVisibility="Disabled" HorizontalScrollBarVisibility="Auto">
                <WrapPanel x:Name="PillContainer" Orientation="Horizontal">
                    <TextBox x:Name="InputBox" MinWidth="80" MaxWidth="300"
                             Background="Transparent" Foreground="White" BorderThickness="0"
                             FontSize="11" VerticalAlignment="Center" Margin="2,1"
                             CaretBrush="White"/>
                </WrapPanel>
            </ScrollViewer>
        </Border>
        <Popup x:Name="AutocompletePopup" PlacementTarget="{Binding ElementName=InputBox}"
               Placement="Bottom" StaysOpen="False" AllowsTransparency="True"
               PopupAnimation="Fade">
            <Border Background="#252526" BorderBrush="#555" BorderThickness="1"
                    CornerRadius="4" Padding="2" MaxHeight="200">
                <ListBox x:Name="SuggestionList" Background="Transparent" BorderThickness="0"
                         Foreground="#CCC" FontSize="11" SelectionMode="Single">
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Padding" Value="8,4"/>
                            <Setter Property="Cursor" Value="Hand"/>
                            <Style.Triggers>
                                <Trigger Property="IsSelected" Value="True">
                                    <Setter Property="Background" Value="#0078D4"/>
                                    <Setter Property="Foreground" Value="White"/>
                                </Trigger>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter Property="Background" Value="#3E3E42"/>
                                </Trigger>
                            </Style.Triggers>
                        </Style>
                    </ListBox.ItemContainerStyle>
                </ListBox>
            </Border>
        </Popup>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create code-behind**

Create `MTGProxyBuilder.UI/Controls/PillFilterBar.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class PillFilterBar : UserControl
    {
        private readonly List<FilterToken> _filters = new();
        private readonly List<Border> _pillBorders = new();

        // Autocomplete data
        private List<string> _knownSources = new();
        private List<string> _knownTags = new();
        private List<int> _knownDpis = new();

        public PillFilterBar()
        {
            InitializeComponent();
            InputBox.TextChanged += OnInputTextChanged;
            InputBox.KeyDown += OnInputKeyDown;
            InputBox.PreviewKeyDown += OnInputPreviewKeyDown;
            SuggestionList.PreviewMouseLeftButtonUp += OnSuggestionClicked;
        }

        // ================================================================
        //  PUBLIC API
        // ================================================================

        public event EventHandler? FilterChanged;

        public IReadOnlyList<FilterToken> Filters => _filters.AsReadOnly();

        /// <summary>Set the available autocomplete data from current tile set.</summary>
        public void SetAutocompleteData(IEnumerable<string> sources, IEnumerable<string> tags, IEnumerable<int> dpis)
        {
            _knownSources = sources.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
            _knownTags = tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
            _knownDpis = dpis.Where(d => d > 0).Distinct().OrderByDescending(d => d).ToList();
        }

        /// <summary>Programmatically add a filter pill (e.g. from source click on a tile).</summary>
        public void AddFilter(string text)
        {
            var token = FilterParser.Parse(text);
            AddPill(token);
            InputBox.Text = "";
            CloseAutocomplete();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Clear all filter pills.</summary>
        public void Clear()
        {
            foreach (var border in _pillBorders)
                PillContainer.Children.Remove(border);
            _pillBorders.Clear();
            _filters.Clear();
            InputBox.Text = "";
            CloseAutocomplete();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ================================================================
        //  PILL MANAGEMENT
        // ================================================================

        private void AddPill(FilterToken token)
        {
            _filters.Add(token);

            var pill = CreatePillBorder(token);
            _pillBorders.Add(pill);

            // Insert before the TextBox (which is always the last child)
            int insertIdx = PillContainer.Children.Count - 1;
            PillContainer.Children.Insert(insertIdx, pill);
        }

        private void RemovePill(int index)
        {
            if (index < 0 || index >= _pillBorders.Count) return;
            PillContainer.Children.Remove(_pillBorders[index]);
            _pillBorders.RemoveAt(index);
            _filters.RemoveAt(index);
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private Border CreatePillBorder(FilterToken token)
        {
            Brush bg;
            Brush fg = Brushes.White;
            Brush borderBrush = Brushes.Transparent;

            switch (token.Kind)
            {
                case TokenKind.Or:
                    bg = AppBrushes.AccentRed;
                    break;
                case TokenKind.OpenParen:
                case TokenKind.CloseParen:
                    bg = Brushes.Transparent;
                    borderBrush = AppBrushes.Border;
                    fg = AppBrushes.TextMuted;
                    break;
                default:
                    bg = AppBrushes.TileBg;
                    fg = AppBrushes.TextSecondary;
                    break;
            }

            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            stack.Children.Add(new TextBlock
            {
                Text = token.DisplayText,
                Foreground = fg,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });

            var closeBtn = new TextBlock
            {
                Text = "\u00D7", // ×
                Foreground = AppBrushes.TextMuted,
                FontSize = 10,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            int capturedIndex = _pillBorders.Count; // capture at creation time
            closeBtn.MouseLeftButtonUp += (_, e) =>
            {
                int idx = _pillBorders.IndexOf(border!);
                if (idx >= 0) RemovePill(idx);
                e.Handled = true;
            };
            stack.Children.Add(closeBtn);

            var border = new Border
            {
                Background = bg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 6, 2),
                Margin = new Thickness(2, 1, 2, 1),
                Cursor = Cursors.Arrow,
                Child = stack
            };

            // Fix closure — closeBtn needs the border reference
            closeBtn.MouseLeftButtonUp -= null!; // remove placeholder
            closeBtn.MouseLeftButtonUp += (_, e) =>
            {
                int idx = _pillBorders.IndexOf(border);
                if (idx >= 0) RemovePill(idx);
                e.Handled = true;
            };

            return border;
        }

        // ================================================================
        //  INPUT HANDLING
        // ================================================================

        private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back && string.IsNullOrEmpty(InputBox.Text) && _pillBorders.Count > 0)
            {
                RemovePill(_pillBorders.Count - 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && AutocompletePopup.IsOpen)
            {
                if (SuggestionList.SelectedIndex < SuggestionList.Items.Count - 1)
                    SuggestionList.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.Key == Key.Up && AutocompletePopup.IsOpen)
            {
                if (SuggestionList.SelectedIndex > 0)
                    SuggestionList.SelectedIndex--;
                e.Handled = true;
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;

                // If autocomplete is open and has selection, use it
                if (AutocompletePopup.IsOpen && SuggestionList.SelectedItem is string selected)
                {
                    ApplyAutocomplete(selected);
                    return;
                }

                // Otherwise commit current text as a pill
                CommitCurrentText();
            }
        }

        private void CommitCurrentText()
        {
            string text = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var token = FilterParser.Parse(text);
            AddPill(token);
            InputBox.Text = "";
            CloseAutocomplete();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ================================================================
        //  AUTOCOMPLETE
        // ================================================================

        private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            string text = InputBox.Text;

            // Immediate pill creation for parens
            if (text == "(" || text == ")")
            {
                var token = FilterParser.Parse(text);
                AddPill(token);
                InputBox.Text = "";
                FilterChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            UpdateAutocomplete(text);
        }

        private void UpdateAutocomplete(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                CloseAutocomplete();
                return;
            }

            var suggestions = GetSuggestions(text);
            if (suggestions.Count == 0)
            {
                CloseAutocomplete();
                return;
            }

            SuggestionList.Items.Clear();
            foreach (var s in suggestions.Take(8))
                SuggestionList.Items.Add(s);

            SuggestionList.SelectedIndex = 0;
            AutocompletePopup.IsOpen = true;
        }

        private List<string> GetSuggestions(string text)
        {
            var results = new List<string>();
            string lower = text.ToLowerInvariant();

            // Check if we're in field:value mode
            int colonIdx = text.IndexOf(':');
            if (colonIdx > 0)
            {
                string field = text[..colonIdx].ToLowerInvariant();
                string rest = text[(colonIdx + 1)..];

                if (field == "source")
                {
                    // After "in[" suggest values
                    if (rest.StartsWith("in[", StringComparison.OrdinalIgnoreCase))
                    {
                        string partial = rest.Contains(',') ? rest[(rest.LastIndexOf(',') + 1)..].Trim() : rest[3..].Trim();
                        results.AddRange(_knownSources
                            .Where(s => s.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                            .Select(s => text[..(text.Length - partial.Length)] + s));
                    }
                    else if (string.IsNullOrEmpty(rest) || rest == "!" || rest == "=")
                    {
                        // Suggest known sources
                        results.AddRange(_knownSources.Select(s => $"source:{rest}{s}"));
                    }
                    else
                    {
                        string prefix = rest.StartsWith("!") ? rest[1..] : rest;
                        string opPrefix = rest.StartsWith("!") ? "!" : "";
                        results.AddRange(_knownSources
                            .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            .Select(s => $"source:{opPrefix}{s}"));
                    }
                }
                else if (field == "tag")
                {
                    if (rest.StartsWith("in[", StringComparison.OrdinalIgnoreCase))
                    {
                        string partial = rest.Contains(',') ? rest[(rest.LastIndexOf(',') + 1)..].Trim() : rest[3..].Trim();
                        results.AddRange(_knownTags
                            .Where(t => t.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                            .Select(t => text[..(text.Length - partial.Length)] + t));
                    }
                    else if (string.IsNullOrEmpty(rest) || rest == "!" || rest == "=")
                    {
                        results.AddRange(_knownTags.Select(t => $"tag:{rest}{t}"));
                    }
                    else
                    {
                        string prefix = rest.StartsWith("!") ? rest[1..] : rest;
                        string opPrefix = rest.StartsWith("!") ? "!" : "";
                        results.AddRange(_knownTags
                            .Where(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            .Select(t => $"tag:{opPrefix}{t}"));
                    }
                }
                else if (field == "dpi")
                {
                    if (string.IsNullOrEmpty(rest))
                    {
                        results.AddRange(new[] { "dpi:>", "dpi:>=", "dpi:<", "dpi:<=", "dpi:=", "dpi:!" });
                        results.AddRange(_knownDpis.Select(d => $"dpi:{d}"));
                    }
                    else if (rest is ">" or ">=" or "<" or "<=" or "!" or "=")
                    {
                        results.AddRange(_knownDpis.Select(d => $"dpi:{rest}{d}"));
                    }
                }

                return results;
            }

            // No colon — suggest field names or treat as free text
            string[] fields = { "name:", "source:", "dpi:", "tag:" };
            results.AddRange(fields.Where(f => f.StartsWith(lower)));

            // Also suggest OR if it looks like they're typing it
            if ("or".StartsWith(lower) && _filters.Count > 0)
                results.Add("OR");

            return results;
        }

        private void ApplyAutocomplete(string selected)
        {
            // If the suggestion ends with ':' or an operator, just fill the input — don't create pill yet
            if (selected.EndsWith(':') || selected.EndsWith('>') || selected.EndsWith('<')
                || selected.EndsWith(">=") || selected.EndsWith("<=") || selected.EndsWith('!')
                || selected.EndsWith('='))
            {
                InputBox.Text = selected;
                InputBox.CaretIndex = InputBox.Text.Length;
                CloseAutocomplete();
                return;
            }

            // If inside an in[...] and not closed, fill but don't commit
            if (selected.Contains("in[") && !selected.EndsWith(']'))
            {
                InputBox.Text = selected + ",";
                InputBox.CaretIndex = InputBox.Text.Length;
                UpdateAutocomplete(InputBox.Text);
                return;
            }

            // Full expression — create pill
            var token = FilterParser.Parse(selected);
            AddPill(token);
            InputBox.Text = "";
            CloseAutocomplete();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSuggestionClicked(object sender, MouseButtonEventArgs e)
        {
            if (SuggestionList.SelectedItem is string selected)
            {
                ApplyAutocomplete(selected);
                InputBox.Focus();
            }
        }

        private void CloseAutocomplete()
        {
            AutocompletePopup.IsOpen = false;
            SuggestionList.Items.Clear();
        }
    }
}
```

**NOTE on the CreatePillBorder closure issue:** The code above has a closure problem with `closeBtn` referencing `border` before it's assigned. The implementer should fix this by declaring `Border? border = null;` first, wiring the event with the closure referencing `border`, then assigning `border = new Border { ... }`. The pattern:

```csharp
Border? border = null;
// ... create closeBtn, wire with: int idx = _pillBorders.IndexOf(border!);
border = new Border { ... Child = stack };
return border;
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/Controls/PillFilterBar.xaml MTGProxyBuilder.UI/Controls/PillFilterBar.xaml.cs
git commit -m "feat: create PillFilterBar control with autocomplete"
```

---

### Task 4: Add Dpi to TileInfo and integrate PillFilterBar into ArtSelectorDialog

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml`
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

- [ ] **Step 1: Update XAML — replace SearchBar with PillFilterBar**

In `ArtSelectorDialog.xaml`, add the PillFilterBar namespace if not already there (the `controls` namespace is already declared).

Replace the front SearchBar (line ~90):
```xml
                        <controls:SearchBar x:Name="FrontSearchBar" Grid.Row="1" Margin="0,0,0,6"
                                            Placeholder="Search art (name:, t:, s:, r:, c:, a:, source:...)"/>
```
with:
```xml
                        <controls:PillFilterBar x:Name="FrontFilterBar" Grid.Row="1" Margin="0,0,0,6"/>
```

Replace the back SearchBar (line ~125):
```xml
                        <controls:SearchBar x:Name="BackSearchBar" Grid.Row="1" Margin="0,0,0,6"
                                            Placeholder="Search back art..."/>
```
with:
```xml
                        <controls:PillFilterBar x:Name="BackFilterBar" Grid.Row="1" Margin="0,0,0,6"/>
```

- [ ] **Step 2: Update TileInfo to include Dpi**

In `ArtSelectorDialog.xaml.cs`, replace the TileInfo record:
```csharp
private record TileInfo(Border Tile, string Name, string Source, string Detail, List<string> Tags, bool IsAction = false);
```
with:
```csharp
private record TileInfo(Border Tile, string Name, string Source, int Dpi, List<string> Tags, bool IsAction = false);
```

- [ ] **Step 3: Update TabState — remove SearchBar and ActiveTagFilters, add PillFilterBar**

Replace:
```csharp
public required SearchBar SearchBar { get; init; }
```
with:
```csharp
public required PillFilterBar FilterBar { get; init; }
```

Remove:
```csharp
public HashSet<string> ActiveTagFilters { get; } = new(StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 4: Update constructor — wire PillFilterBar**

Replace tab state initialization:
```csharp
_frontTab = new TabState
{
    Mode = ArtSelectorMode.Front,
    OptionsPanel = FrontOptionsPanel,
    SearchBar = FrontSearchBar
};
_backTab = new TabState
{
    Mode = ArtSelectorMode.Back,
    OptionsPanel = BackOptionsPanel,
    SearchBar = BackSearchBar
};
```
with:
```csharp
_frontTab = new TabState
{
    Mode = ArtSelectorMode.Front,
    OptionsPanel = FrontOptionsPanel,
    FilterBar = FrontFilterBar
};
_backTab = new TabState
{
    Mode = ArtSelectorMode.Back,
    OptionsPanel = BackOptionsPanel,
    FilterBar = BackFilterBar
};
```

Replace search bar wiring:
```csharp
FrontSearchBar.SearchRequested += (_, _) => ApplyFilters(_frontTab);
FrontSearchBar.SourceChanged += (_, _) => ApplyFilters(_frontTab);
BackSearchBar.SearchRequested += (_, _) => ApplyFilters(_backTab);
BackSearchBar.SourceChanged += (_, _) => ApplyFilters(_backTab);
```
with:
```csharp
FrontFilterBar.FilterChanged += (_, _) => ApplyFilters(_frontTab);
BackFilterBar.FilterChanged += (_, _) => ApplyFilters(_backTab);
```

- [ ] **Step 5: Replace ApplyFilters**

Replace the entire `ApplyFilters` method and remove `PopulateSourceFilter`, `OnSourceClickFilter`, `OnTagClickFilter`:

```csharp
        private void ApplyFilters(TabState tab)
        {
            if (tab.AllTiles.Count == 0) return;

            var filters = tab.FilterBar.Filters;
            int visible = 0;
            int total = 0;

            foreach (var tile in tab.AllTiles)
            {
                if (tile.IsAction)
                {
                    tile.Tile.Visibility = Visibility.Visible;
                    continue;
                }

                total++;
                var tileData = new TileData(tile.Name, tile.Source, tile.Dpi, tile.Tags);
                bool show = FilterEvaluator.Evaluate(filters, tileData);
                tile.Tile.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) visible++;
            }

            if (filters.Count > 0)
                StatusLabel.Text = $"Showing {visible} of {total} option(s)";
        }
```

Add a helper to populate autocomplete data (replaces `PopulateSourceFilter`):

```csharp
        private void PopulateAutocompleteData(TabState tab)
        {
            var sources = tab.AllTiles.Where(t => !t.IsAction && !string.IsNullOrEmpty(t.Source))
                .Select(t => t.Source).Distinct(StringComparer.OrdinalIgnoreCase);
            var tags = tab.AllTiles.Where(t => !t.IsAction)
                .SelectMany(t => t.Tags).Distinct(StringComparer.OrdinalIgnoreCase);
            var dpis = tab.AllTiles.Where(t => !t.IsAction && t.Dpi > 0)
                .Select(t => t.Dpi).Distinct();
            tab.FilterBar.SetAutocompleteData(sources, tags, dpis);
        }
```

- [ ] **Step 6: Replace OnSourceClickFilter and OnTagClickFilter**

Replace both methods with:

```csharp
        private void OnSourceClickFilter(TabState tab, string source)
        {
            tab.FilterBar.AddFilter($"source:{source}");
        }

        private void OnTagClickFilter(TabState tab, string tag)
        {
            tab.FilterBar.AddFilter($"tag:{tag}");
        }
```

- [ ] **Step 7: Update all PopulateSourceFilter calls to PopulateAutocompleteData**

Search for `PopulateSourceFilter(tab)` and replace with `PopulateAutocompleteData(tab)`.

- [ ] **Step 8: Update all TileInfo constructor calls — add Dpi field, remove Detail**

Every `new TileInfo(...)` call needs updating. The `Detail` parameter is removed, replaced with `Dpi`.

For Scryfall tiles: `Dpi` = 0
For MPCFill tiles: `Dpi` = mc.Dpi
For Library tiles: `Dpi` = 0
For Action tiles: `Dpi` = 0

Find and replace all ~8 TileInfo call sites.

- [ ] **Step 9: Update AddOption method**

The `AddOption` method builds a `detail` string — remove that since TileInfo no longer has Detail. Keep using it for `SelectOption` display but don't store it.

- [ ] **Step 10: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 11: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "feat: integrate PillFilterBar into ArtSelectorDialog with FilterEvaluator"
```

---

### Task 5: Update ArtTileBuilder source/tag click callbacks

**Files:**
- Modify: `MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs`

The `onSourceClick` and `onTagClick` callbacks already pass `Action<string>`. The ArtSelectorDialog now wires them to create pills instead of setting dropdowns. No signature change needed — the wiring was updated in Task 4. However, verify and adjust if the `isTagActive` callback is still needed.

- [ ] **Step 1: Remove isTagActive from tile methods**

Since tag filtering is now handled by the pill bar (not by highlighting active tags on tiles), remove the `Func<string, bool>? isTagActive` parameter from `CreateOptionTile`, `CreateDeferredTile`, `CreatePlaceholderTile`, `BuildTagsButton`, and `ShowTagsPopup`.

In `ShowTagsPopup`, all pills are styled uniformly (no active/inactive distinction — the pill bar shows what's filtered):

```csharp
var pill = new Border
{
    Background = AppBrushes.TileBg,
    BorderBrush = Brushes.Transparent,
    BorderThickness = new Thickness(1),
    CornerRadius = new CornerRadius(8),
    Padding = new Thickness(6, 2, 6, 2),
    Margin = new Thickness(2),
    Cursor = Cursors.Hand,
    ToolTip = $"Filter by: {tag}"
};
pill.Child = new TextBlock
{
    Text = tag,
    Foreground = AppBrushes.TextSecondary,
    FontSize = 9
};
```

- [ ] **Step 2: Remove isTagActive from ArtSelectorDialog call sites**

In `ArtSelectorDialog.xaml.cs`, remove `isTagActive:` parameter from all `CreateOptionTile`, `CreatePlaceholderTile`, and `CreateDeferredTile` calls.

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "feat: simplify tag popup styling, remove isTagActive parameter"
```

---

### Task 6: Final build, test, and commit docs

- [ ] **Step 1: Full rebuild**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test`
Expected: FilterParser and FilterEvaluator tests all pass. 430+ existing tests pass. 5 pre-existing UI smoke failures.

- [ ] **Step 3: Commit plan doc**

```bash
git add docs/
git commit -m "docs: add pill-based filter search implementation plan"
```
