using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Resources;
using MTGProxyBuilder.UI.Dialogs;

namespace MTGProxyBuilder.UI.Controls
{
    public partial class PropertyPanel : UserControl
    {
        public PropertyPanel()
        {
            InitializeComponent();
            LoadFonts();
            DataContextChanged += OnDataContextChanged;
        }

        private void LoadFonts()
        {
            var fonts = FontProvider.GetAllFontNames();
            var allFonts = new List<string> { "Arial", "Segoe UI", "Times New Roman" };
            foreach (var f in fonts)
            {
                if (!allFonts.Contains(f))
                    allFonts.Add(f);
            }
            FontComboBox.ItemsSource = allFonts;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateVisibility();
        }

        public void UpdateForSelection(LayerBase? layer)
        {
            if (layer == null)
            {
                NoSelectionText.Visibility = Visibility.Visible;
                PropertiesPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoSelectionText.Visibility = Visibility.Collapsed;
                PropertiesPanel.Visibility = Visibility.Visible;
                ImageProperties.Visibility = layer is ImageLayer ? Visibility.Visible : Visibility.Collapsed;
                TextProperties.Visibility = layer is TextLayer ? Visibility.Visible : Visibility.Collapsed;

                if (layer is TextLayer textLayer)
                {
                    UpdateColorSwatches(textLayer);
                    textLayer.PropertyChanged += OnTextLayerPropertyChanged;
                }
            }
        }

        private void OnTextLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is TextLayer tl && (e.PropertyName == nameof(TextLayer.FontColor) || e.PropertyName == nameof(TextLayer.StrokeColor)))
                UpdateColorSwatches(tl);
        }

        private void UpdateColorSwatches(TextLayer layer)
        {
            FontColorSwatch.Background = ParseBrush(layer.FontColor);
            StrokeColorSwatch.Background = ParseBrush(layer.StrokeColor);
        }

        private static SolidColorBrush ParseBrush(string? hex)
        {
            if (string.IsNullOrEmpty(hex)) return new SolidColorBrush(Colors.Transparent);
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        private void OnPickFontColor(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as dynamic;
            if (vm?.SelectedLayer is not TextLayer textLayer) return;

            var dialog = new ColorPickerDialog(textLayer.FontColor);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                textLayer.FontColor = dialog.SelectedHexColor;
                FontColorBox.Text = dialog.SelectedHexColor;
                UpdateColorSwatches(textLayer);
            }
        }

        private void OnPickStrokeColor(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as dynamic;
            if (vm?.SelectedLayer is not TextLayer textLayer) return;

            var dialog = new ColorPickerDialog(textLayer.StrokeColor ?? "#000000");
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                textLayer.StrokeColor = dialog.SelectedHexColor;
                StrokeColorBox.Text = dialog.SelectedHexColor;
                UpdateColorSwatches(textLayer);
            }
        }

        private void UpdateVisibility()
        {
        }

        // --- Routed Events ---

        public static readonly RoutedEvent BrowseImageRequestedEvent =
            EventManager.RegisterRoutedEvent("BrowseImageRequested", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(PropertyPanel));

        public event RoutedEventHandler BrowseImageRequested
        {
            add => AddHandler(BrowseImageRequestedEvent, value);
            remove => RemoveHandler(BrowseImageRequestedEvent, value);
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
            => RaiseEvent(new RoutedEventArgs(BrowseImageRequestedEvent));
    }
}
