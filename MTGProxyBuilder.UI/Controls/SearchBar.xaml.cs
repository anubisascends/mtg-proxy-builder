using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class SearchBar : UserControl
    {
        public SearchBar()
        {
            InitializeComponent();
        }

        // ================================================================
        //  EVENTS
        // ================================================================

        /// <summary>Fired when the user presses Enter or clicks the Search button.</summary>
        public event EventHandler? SearchRequested;

        /// <summary>Fired when the source filter dropdown selection changes.</summary>
        public event EventHandler? SourceChanged;

        // ================================================================
        //  DEPENDENCY PROPERTIES
        // ================================================================

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchBar),
                new PropertyMetadata("Search...", OnPlaceholderChanged));

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SearchBar bar)
                bar.PlaceholderBlock.Text = (string)e.NewValue;
        }

        public static readonly DependencyProperty ShowSourceFilterProperty =
            DependencyProperty.Register(nameof(ShowSourceFilter), typeof(bool), typeof(SearchBar),
                new PropertyMetadata(true, OnShowSourceFilterChanged));

        public bool ShowSourceFilter
        {
            get => (bool)GetValue(ShowSourceFilterProperty);
            set => SetValue(ShowSourceFilterProperty, value);
        }

        private static void OnShowSourceFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SearchBar bar)
                bar.SourceCombo.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================================================================
        //  PUBLIC API
        // ================================================================

        public string SearchText => SearchTextBox.Text?.Trim() ?? "";

        public string SelectedSource
        {
            get
            {
                if (SourceCombo.SelectedItem is string s) return s;
                if (SourceCombo.SelectedItem is ComboBoxItem item) return item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
                return "";
            }
        }

        /// <summary>Populate the source filter dropdown with a list of source names. Includes "All" as the first item.</summary>
        public void SetSources(IEnumerable<string> sources, string allLabel = "All Sources")
        {
            SourceCombo.SelectionChanged -= OnSourceSelectionChanged;
            string current = SelectedSource;

            SourceCombo.Items.Clear();
            SourceCombo.Items.Add(allLabel);
            SourceCombo.SelectedIndex = 0;

            foreach (var s in sources)
            {
                SourceCombo.Items.Add(s);
                if (s.Equals(current, StringComparison.OrdinalIgnoreCase))
                    SourceCombo.SelectedItem = s;
            }

            SourceCombo.SelectionChanged += OnSourceSelectionChanged;
        }

        /// <summary>Returns true if "All" is selected in the source filter.</summary>
        public bool IsAllSourcesSelected => SourceCombo.SelectedIndex == 0;

        public void SelectSource(string sourceName)
        {
            for (int i = 0; i < SourceCombo.Items.Count; i++)
            {
                if (SourceCombo.Items[i] is string s && s.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
                {
                    SourceCombo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>Clears the search text.</summary>
        public void Clear()
        {
            SearchTextBox.Text = string.Empty;
        }

        // ================================================================
        //  EVENT HANDLERS
        // ================================================================

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderBlock.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            SearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
