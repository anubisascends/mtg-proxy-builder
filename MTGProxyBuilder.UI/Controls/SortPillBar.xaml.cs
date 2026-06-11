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
    /// <summary>
    /// A pill bar for sort criteria. Each pill represents a sort field, ordered as "then by".
    /// Format: "Name", "CMC:desc", "Rarity:asc"
    /// </summary>
    public partial class SortPillBar : UserControl
    {
        private readonly List<SortPill> _pills = new();
        private readonly List<Border> _pillBorders = new();

        private static readonly string[] SortFields =
        {
            "Name", "CMC", "Rarity", "Color", "Type", "Set",
            "Artist", "Collector #", "Power", "Toughness", "Date Added"
        };

        public SortPillBar()
        {
            InitializeComponent();
            InputBox.KeyDown += OnInputKeyDown;
            InputBox.PreviewKeyDown += OnInputPreviewKeyDown;
            InputBox.TextChanged += OnInputTextChanged;
            // Mouse click on suggestion handled via XAML PreviewMouseLeftButtonDown
            InputBox.GotFocus += (_, _) => ShowInitialSuggestions();
            InputBox.LostFocus += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!InputBox.IsFocused && !InputBox.IsKeyboardFocusWithin)
                        AutocompletePopup.IsOpen = false;
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            InputBox.TextChanged += (_, _) => UpdatePlaceholder();
            UpdatePlaceholder();
        }

        private void UpdatePlaceholder()
        {
            if (PlaceholderText != null)
                PlaceholderText.Visibility = _pills.Count == 0 && string.IsNullOrEmpty(InputBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        public event EventHandler? SortChanged;

        public IReadOnlyList<SortPill> Pills => _pills.AsReadOnly();

        public void Clear()
        {
            foreach (var b in _pillBorders)
                PillContainer.Children.Remove(b);
            _pillBorders.Clear();
            _pills.Clear();
            InputBox.Text = "";
            SortChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddPill(SortPill pill)
        {
            _pills.Add(pill);

            Border? border = null;
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = pill.DisplayText,
                Foreground = AppBrushes.TextSecondary,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });

            var closeBtn = new TextBlock
            {
                Text = "\u00D7",
                Foreground = AppBrushes.TextMuted,
                FontSize = 10,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeBtn.MouseLeftButtonUp += (_, e) =>
            {
                int idx = _pillBorders.IndexOf(border!);
                if (idx >= 0) RemovePill(idx);
                e.Handled = true;
            };
            stack.Children.Add(closeBtn);

            border = new Border
            {
                Background = AppBrushes.TileBg,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 6, 2),
                Margin = new Thickness(2, 1, 2, 1),
                Cursor = Cursors.Arrow,
                Child = stack,
                ToolTip = _pills.Count == 1 ? "Sort by" : $"Then by (priority {_pills.Count})"
            };

            _pillBorders.Add(border);
            PillContainer.Children.Insert(PillContainer.Children.Count - 1, border);
        }

        private void RemovePill(int index)
        {
            if (index < 0 || index >= _pillBorders.Count) return;
            PillContainer.Children.Remove(_pillBorders[index]);
            _pillBorders.RemoveAt(index);
            _pills.RemoveAt(index);
            UpdatePlaceholder();
            SortChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CommitText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var pill = SortPill.Parse(text);
            AddPill(pill);
            InputBox.Text = "";
            UpdatePlaceholder();
            AutocompletePopup.IsOpen = false;
            SortChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                e.Handled = true;
                if (AutocompletePopup.IsOpen && SuggestionList.SelectedItem is string selected)
                {
                    CommitText(selected);
                    return;
                }
                CommitText(InputBox.Text);
            }
        }

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

        private void ShowInitialSuggestions()
        {
            if (!string.IsNullOrEmpty(InputBox.Text?.Trim()))
            {
                // Let OnInputTextChanged handle it
                return;
            }

            var usedFields = _pills.Select(p => p.Field.ToLowerInvariant()).ToHashSet();
            var suggestions = new List<string>();
            foreach (var field in SortFields)
            {
                if (usedFields.Contains(field.ToLowerInvariant())) continue;
                suggestions.Add(field);
                suggestions.Add($"{field}:desc");
            }

            if (suggestions.Count == 0) return;

            SuggestionList.Items.Clear();
            foreach (var s in suggestions.Take(16))
                SuggestionList.Items.Add(s);
            SuggestionList.SelectedIndex = 0;
            AutocompletePopup.IsOpen = true;
        }

        private void OnInputTextChanged(object sender, TextChangedEventArgs e)
        {
            string text = InputBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text))
            {
                if (InputBox.IsFocused)
                    ShowInitialSuggestions();
                else
                    AutocompletePopup.IsOpen = false;
                return;
            }

            var suggestions = new List<string>();
            string lower = text.ToLowerInvariant();

            // Already-added fields (don't suggest duplicates)
            var usedFields = _pills.Select(p => p.Field.ToLowerInvariant()).ToHashSet();

            foreach (var field in SortFields)
            {
                if (usedFields.Contains(field.ToLowerInvariant())) continue;
                if (field.StartsWith(text, StringComparison.OrdinalIgnoreCase) || field.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    suggestions.Add(field);
                    suggestions.Add($"{field}:desc");
                }
            }

            if (suggestions.Count == 0)
            {
                AutocompletePopup.IsOpen = false;
                return;
            }

            SuggestionList.Items.Clear();
            foreach (var s in suggestions.Take(12))
                SuggestionList.Items.Add(s);
            SuggestionList.SelectedIndex = 0;
            AutocompletePopup.IsOpen = true;
        }

        private void OnSuggestionMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var item = FindAncestor<ListBoxItem>(source);
                if (item?.Content is string selected)
                {
                    e.Handled = true;
                    CommitText(selected);
                    InputBox.Focus();
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T target) return target;
                obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
            }
            return null;
        }
    }
}
