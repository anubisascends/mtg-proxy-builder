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
        // ================================================================
        //  STATE
        // ================================================================

        private readonly List<FilterToken> _filters = [];
        private readonly List<Border> _pillBorders = [];

        private List<string> _knownSources = [];
        private List<string> _knownTags = [];
        private List<int> _knownDpis = [];

        // ================================================================
        //  EVENTS & PUBLIC API
        // ================================================================

        /// <summary>Fired whenever pills are added or removed.</summary>
        public event EventHandler? FilterChanged;

        /// <summary>Current filter tokens.</summary>
        public IReadOnlyList<FilterToken> Filters => _filters.AsReadOnly();

        public PillFilterBar()
        {
            InitializeComponent();
            UpdatePlaceholder();
        }

        /// <summary>Set the available autocomplete data for sources, tags, and DPI values.</summary>
        public void SetAutocompleteData(IEnumerable<string> sources, IEnumerable<string> tags, IEnumerable<int> dpis)
        {
            _knownSources = sources.ToList();
            _knownTags = tags.ToList();
            _knownDpis = dpis.OrderByDescending(d => d).ToList();
        }

        /// <summary>Programmatically add a filter pill from text (e.g. "source:Chilli_Axe").</summary>
        public void AddFilter(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            CommitText(text.Trim());
        }

        /// <summary>Remove all pills and clear input.</summary>
        public void Clear()
        {
            foreach (var pill in _pillBorders)
                PillContainer.Children.Remove(pill);

            _pillBorders.Clear();
            _filters.Clear();
            InputBox.Text = string.Empty;
            UpdatePlaceholder();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        // ================================================================
        //  PILL CREATION
        // ================================================================

        private void CommitText(string text)
        {
            var token = FilterParser.ParseSingle(text);
            AddPill(token);

            InputBox.Text = string.Empty;
            CloseAutocomplete();
            UpdatePlaceholder();
            FilterChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddPill(FilterToken token)
        {
            _filters.Add(token);

            // Determine pill styling based on token kind
            Brush pillBg;
            Brush pillFg;
            Brush pillBorderBrush;

            switch (token.Kind)
            {
                case TokenKind.Or:
                    pillBg = AppBrushes.AccentRed;
                    pillFg = Brushes.White;
                    pillBorderBrush = AppBrushes.AccentRed;
                    break;

                case TokenKind.OpenParen:
                case TokenKind.CloseParen:
                    pillBg = Brushes.Transparent;
                    pillFg = AppBrushes.TextMuted;
                    pillBorderBrush = AppBrushes.Border;
                    break;

                default: // Filter, And
                    pillBg = AppBrushes.TileBg;
                    pillFg = AppBrushes.TextSecondary;
                    pillBorderBrush = AppBrushes.TileBg;
                    break;
            }

            // Use the closure pattern: declare border first, create close button referencing it,
            // then assign border.
            Border? border = null;

            var closeBtn = new Button
            {
                Content = "\u00D7",
                FontSize = 10,
                Foreground = pillFg,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };

            closeBtn.Click += (_, _) => RemovePill(border!);

            var textBlock = new TextBlock
            {
                Text = token.DisplayText,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = pillFg,
                FontSize = 12
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { textBlock, closeBtn }
            };

            border = new Border
            {
                Background = pillBg,
                BorderBrush = pillBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 4, 2),
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = panel
            };

            _pillBorders.Add(border);

            // Insert before the InputBox (which is always the last child)
            int insertIndex = PillContainer.Children.Count - 1;
            PillContainer.Children.Insert(insertIndex, border);
        }

        private void RemovePill(Border pill)
        {
            int index = _pillBorders.IndexOf(pill);
            if (index < 0) return;

            _pillBorders.RemoveAt(index);
            _filters.RemoveAt(index);
            PillContainer.Children.Remove(pill);
            UpdatePlaceholder();
            FilterChanged?.Invoke(this, EventArgs.Empty);
            InputBox.Focus();
        }

        // ================================================================
        //  INPUT HANDLING
        // ================================================================

        private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Back:
                    if (string.IsNullOrEmpty(InputBox.Text) && _pillBorders.Count > 0)
                    {
                        RemovePill(_pillBorders[^1]);
                        e.Handled = true;
                    }
                    break;

                case Key.Down:
                    if (AutocompletePopup.IsOpen && SuggestionList.Items.Count > 0)
                    {
                        int idx = SuggestionList.SelectedIndex;
                        SuggestionList.SelectedIndex = Math.Min(idx + 1, SuggestionList.Items.Count - 1);
                        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                        e.Handled = true;
                    }
                    break;

                case Key.Up:
                    if (AutocompletePopup.IsOpen && SuggestionList.Items.Count > 0)
                    {
                        int idx = SuggestionList.SelectedIndex;
                        SuggestionList.SelectedIndex = Math.Max(idx - 1, 0);
                        SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;

                if (AutocompletePopup.IsOpen && SuggestionList.SelectedItem is string selected)
                {
                    ApplySuggestion(selected);
                }
                else if (!string.IsNullOrWhiteSpace(InputBox.Text))
                {
                    CommitText(InputBox.Text.Trim());
                }
            }
            else if (e.Key == Key.Escape)
            {
                CloseAutocomplete();
                e.Handled = true;
            }
        }

        private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            string text = InputBox.Text;

            // Handle parentheses: extract and commit immediately
            if (text.Contains('(') || text.Contains(')'))
            {
                string remaining = "";
                foreach (char c in text)
                {
                    if (c == '(' || c == ')')
                    {
                        // Commit any accumulated text before the paren
                        if (!string.IsNullOrWhiteSpace(remaining))
                        {
                            AddPill(FilterParser.ParseSingle(remaining.Trim()));
                            remaining = "";
                        }
                        // Commit the paren itself
                        AddPill(FilterParser.ParseSingle(c.ToString()));
                    }
                    else
                    {
                        remaining += c;
                    }
                }

                InputBox.TextChanged -= OnInputTextChanged;
                InputBox.Text = remaining;
                InputBox.CaretIndex = remaining.Length;
                InputBox.TextChanged += OnInputTextChanged;

                if (remaining != text)
                {
                    UpdatePlaceholder();
                    FilterChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            UpdatePlaceholder();
            UpdateAutocomplete();
        }

        // ================================================================
        //  AUTOCOMPLETE
        // ================================================================

        private void UpdateAutocomplete()
        {
            string text = InputBox.Text;
            if (string.IsNullOrEmpty(text))
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

            SuggestionList.ItemsSource = suggestions;
            SuggestionList.SelectedIndex = 0;
            AutocompletePopup.IsOpen = true;
        }

        private List<string> GetSuggestions(string text)
        {
            int colonIdx = text.IndexOf(':');

            // No colon — suggest field names and OR
            if (colonIdx < 0)
            {
                var fields = new List<string> { "name:", "source:", "dpi:", "tag:" };
                var result = fields
                    .Where(f => f.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (_filters.Count > 0 && "OR".StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    result.Add("OR");

                return result.Take(8).ToList();
            }

            string fieldPart = text[..colonIdx].ToLowerInvariant();
            string valuePart = text[(colonIdx + 1)..];

            // Extract operator prefix for source/tag (e.g., "!" in "source:!val")
            string opPrefix = "";
            string searchPart = valuePart;

            if (fieldPart == "source" || fieldPart == "tag")
            {
                if (valuePart.StartsWith("!"))
                {
                    opPrefix = "!";
                    searchPart = valuePart[1..];
                }
                else if (valuePart.StartsWith("="))
                {
                    opPrefix = "=";
                    searchPart = valuePart[1..];
                }

                // Handle in[...] syntax
                if (valuePart.StartsWith("in[", StringComparison.OrdinalIgnoreCase))
                {
                    return GetInBracketSuggestions(fieldPart, valuePart, text[..(colonIdx + 1)]);
                }
            }

            return fieldPart switch
            {
                "source" => GetValueSuggestions(_knownSources, searchPart, $"{fieldPart}:{opPrefix}"),
                "tag" => GetValueSuggestions(_knownTags, searchPart, $"{fieldPart}:{opPrefix}"),
                "dpi" => GetDpiSuggestions(valuePart, fieldPart),
                "name" => [],  // No autocomplete for name — free text
                _ => []
            };
        }

        private List<string> GetValueSuggestions(List<string> known, string search, string prefix)
        {
            return known
                .Where(v => v.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => !v.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(v => $"{prefix}{v}")
                .ToList();
        }

        private List<string> GetDpiSuggestions(string valuePart, string fieldPart)
        {
            // If empty after colon, suggest operators
            if (string.IsNullOrEmpty(valuePart))
            {
                return [">", ">=", "<", "<=", "=", "!"];
            }

            // If only an operator, suggest DPI values with that operator
            string[] ops = [">=", "<=", ">", "<", "!", "="];
            string matchedOp = ops.FirstOrDefault(o => valuePart == o) ?? "";

            if (!string.IsNullOrEmpty(matchedOp))
            {
                return _knownDpis
                    .Take(8)
                    .Select(d => $"{fieldPart}:{matchedOp}{d}")
                    .ToList();
            }

            // Otherwise suggest DPI values matching partial input
            string numPart = valuePart.TrimStart('>', '<', '=', '!');
            string opPart = valuePart[..^numPart.Length];

            return _knownDpis
                .Where(d => d.ToString().StartsWith(numPart))
                .Take(8)
                .Select(d => $"{fieldPart}:{opPart}{d}")
                .ToList();
        }

        private List<string> GetInBracketSuggestions(string fieldPart, string valuePart, string fieldPrefix)
        {
            // e.g. valuePart = "in[val1,val2," — extract already-entered values
            string inner = valuePart[3..]; // strip "in["
            var parts = inner.Split(',');
            string currentPart = parts[^1]; // the current partial value
            var alreadyEntered = parts[..^1].Select(p => p.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var known = fieldPart == "source" ? _knownSources : _knownTags;

            return known
                .Where(v => !alreadyEntered.Contains(v))
                .Where(v => v.Contains(currentPart, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => !v.StartsWith(currentPart, StringComparison.OrdinalIgnoreCase))
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(v =>
                {
                    // Rebuild the full expression with this value appended
                    var existingCsv = alreadyEntered.Count > 0
                        ? string.Join(",", alreadyEntered) + ","
                        : "";
                    return $"{fieldPrefix}in[{existingCsv}{v},";
                })
                .ToList();
        }

        private void ApplySuggestion(string suggestion)
        {
            // If suggestion ends with ':' or is a bare operator, fill input but don't commit
            if (suggestion.EndsWith(':') || suggestion is ">" or ">=" or "<" or "<=" or "=" or "!")
            {
                InputBox.Text = suggestion;
                InputBox.CaretIndex = suggestion.Length;
                CloseAutocomplete();
                return;
            }

            // If suggestion is an in[...] with trailing comma, keep building
            if (suggestion.Contains("in[") && suggestion.EndsWith(','))
            {
                InputBox.Text = suggestion;
                InputBox.CaretIndex = suggestion.Length;
                UpdateAutocomplete();
                return;
            }

            // Complete value — close in[...] if needed and commit as pill
            string commitText = suggestion;
            if (commitText.Contains("in[") && commitText.EndsWith(','))
            {
                // Remove trailing comma, close bracket
                commitText = commitText.TrimEnd(',') + "]";
            }
            else if (commitText.Contains("in[") && !commitText.EndsWith(']'))
            {
                commitText += "]";
            }

            CommitText(commitText);
        }

        private void OnSuggestionClick(object sender, MouseButtonEventArgs e)
        {
            if (SuggestionList.SelectedItem is string selected)
            {
                ApplySuggestion(selected);
                InputBox.Focus();
            }
        }

        private void OnSuggestionDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Handled by single click already
        }

        private void CloseAutocomplete()
        {
            AutocompletePopup.IsOpen = false;
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        private void UpdatePlaceholder()
        {
            PlaceholderText.Visibility =
                _pillBorders.Count == 0 && string.IsNullOrEmpty(InputBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
}
