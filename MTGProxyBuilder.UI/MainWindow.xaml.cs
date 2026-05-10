using System;
using System.Windows;
using System.Windows.Input;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private const double ZoomMin = 0.15;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        ScryfallSearchBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.ScryfallSearchCommand.CanExecute(null))
                vm.ScryfallSearchCommand.Execute(null);
        };

        DeckImportUrlBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.ImportDeckCommand.CanExecute(null))
                vm.ImportDeckCommand.Execute(null);
        };

        // Double-click card on canvas → open art selector
        GridCanvas.CardDoubleClicked += (card, isShowingBack) =>
        {
            if (DataContext is MainViewModel vm)
                vm.OpenArtSelectorForCard(card, isShowingBack);
        };

        // Ctrl+Z / Ctrl+Y for undo/redo
        KeyDown += (s, e) =>
        {
            if (DataContext is not MainViewModel vm) return;
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (vm.UndoCommand.CanExecute(null)) vm.UndoCommand.Execute(null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
            {
                if (vm.RedoCommand.CanExecute(null)) vm.RedoCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private void ArtSourceChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.UseMpcFill = false;
    }

    private void ArtSourceMpcChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.UseMpcFill = true;
    }

    // --- Right-click on MPCFill result ---

    private void OnMpcFillResultRightClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedMpcFillCard == null) return;

        var card = vm.SelectedMpcFillCard;
        bool isFav = vm.MpcSourceManager.IsFavorite(card.SourceId);

        var menu = new System.Windows.Controls.ContextMenu();

        var favItem = new System.Windows.Controls.MenuItem
        {
            Header = isFav ? $"Remove \"{card.Source}\" from favorites" : $"Add \"{card.Source}\" to favorites"
        };
        favItem.Click += (_, _) => vm.ToggleMpcFavoriteFromResultCommand.Execute(card);
        menu.Items.Add(favItem);

        menu.IsOpen = true;
        e.Handled = true;
    }

    // --- Double-click to add ---

    private void OnScryfallDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.AddScryfallCardCommand.CanExecute(null))
            vm.AddScryfallCardCommand.Execute(null);
    }

    private void OnMpcFillDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.AddMpcFillCardCommand.CanExecute(null))
            vm.AddMpcFillCardCommand.Execute(null);
    }

    // --- Zoom ---

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
            // Finer steps at low zoom
            if (_zoom < 0.5) delta *= 0.5;
            SetZoom(_zoom + delta);
            e.Handled = true;
        }
    }

    private void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom + ZoomStep);
    private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
    private void ZoomReset(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomFit(object sender, RoutedEventArgs e)
    {
        // Fit the page width into the scroll viewer viewport
        if (GridCanvas.Width > 0 && CanvasScrollViewer.ViewportWidth > 0)
        {
            double fitZoom = (CanvasScrollViewer.ViewportWidth - 80) / GridCanvas.Width; // 80 = padding
            SetZoom(fitZoom);
        }
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
        CanvasScale.ScaleX = _zoom;
        CanvasScale.ScaleY = _zoom;
        ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
    }
}
