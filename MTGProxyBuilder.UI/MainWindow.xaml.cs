using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MTGProxyBuilder.UI.ViewModels;
using Serilog;

namespace MTGProxyBuilder.UI;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private const double ZoomMin = 0.15;
    private const double ZoomMax = 3.0;
    private const double ZoomStep = 0.1;

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
        Closing += OnWindowClosing;
        Loaded += (_, _) =>
        {
            var s = Shell.AppSettings;
            SearchSection.IsExpanded = s.SidebarSearchExpanded;
            ImportSection.IsExpanded = s.SidebarImportExpanded;
            CardDetailsSection.IsExpanded = s.SidebarCardDetailsExpanded;
            LayoutSection.IsExpanded = s.SidebarLayoutExpanded;
            StorageSection.IsExpanded = s.SidebarStorageExpanded;

            // Restore sidebar width
            if (s.SidebarWidth > 0)
                SidebarColumn.Width = new GridLength(Math.Clamp(s.SidebarWidth, 200, 600));
        };

        // Wire GridCanvas events once (they route to whichever project is active)
        // Keyboard shortcuts
        KeyDown += (s, e) =>
        {
            if (Shell?.ActiveProject?.Inner is not MainViewModel vm) return;
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
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                if (vm.SaveProjectCommand.CanExecute(null)) vm.SaveProjectCommand.Execute(null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
            {
                Shell.NewProject();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                Shell.OpenProject();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                Shell.CloseActiveProject();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
            {
                if (vm.ExportPdfCommand.CanExecute(null)) vm.ExportPdfCommand.Execute(null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                GridCanvas.SelectAll();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
            {
                GridCanvas.DeselectAll();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.I)
            {
                GridCanvas.InvertSelection();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
            {
                // Delete selected cards — get unique card indices from canvas selection
                var indices = GridCanvas.GetSelectedCardIndices();
                if (indices.Count > 0 && vm.Cards.Count > 0)
                {
                    // Remove in reverse order to preserve indices
                    foreach (var idx in indices.OrderByDescending(i => i))
                    {
                        if (idx >= 0 && idx < vm.Cards.Count)
                            vm.Cards.RemoveAt(idx);
                    }
                    GridCanvas.DeselectAll();
                    vm.StatusText = $"Removed {indices.Count} card(s)";
                }
                e.Handled = true;
            }
        };

        // Wire GridCanvas events after Loaded
        Loaded += (_, _) =>
        {
            GridCanvas.CardDoubleClicked += (card, isShowingBack) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm)
                    vm.OpenArtSelectorForCard(card, isShowingBack);
            };

            GridCanvas.CreateTokenRequested += (sourceCard) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm)
                    vm.CreateTokenFromCard(sourceCard);
            };

            GridCanvas.CreateTokensFromCardsRequested += (sourceCards) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm)
                    vm.CreateTokensFromCards(sourceCards);
            };

            GridCanvas.ApplyMajorityBackRequested += (cardIndices) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm)
                    vm.ApplyMajorityBackToCards(cardIndices);
            };

            GridCanvas.SelectFrontArtRequested += (cardIndices) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm)
                    vm.SelectFrontArtForCards(cardIndices);
            };

            GridCanvas.CardFlipStateChanged += (cardIndex, isShowingBack) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm
                    && vm.SelectedCard != null
                    && vm.Cards.IndexOf(vm.SelectedCard) == cardIndex)
                {
                    vm.IsShowingBackFace = isShowingBack;
                }
            };

            GridCanvas.SelectBackArtRequested += (cardIndices) =>
            {
                if (Shell?.ActiveProject?.Inner is MainViewModel vm)
                    vm.SelectBackArtForCards(cardIndices);
            };
        };
    }

    // --- Tab bar ---

    private void OnTabClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ProjectViewModel tab)
        {
            Shell.ActiveProject = tab;
        }
    }

    private void OnTabClose(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ProjectViewModel tab)
        {
            Shell.CloseProject(tab);
        }
    }

    // --- Double-click to add ---

    private void OnScryfallDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Shell?.ActiveProject?.Inner is MainViewModel vm && vm.AddScryfallCardCommand.CanExecute(null))
            vm.AddScryfallCardCommand.Execute(null);
    }

    // --- Color picker ---

    private void OnOutlineColorClick(object sender, MouseButtonEventArgs e)
    {
        if (Shell?.ActiveProject?.Inner is not MainViewModel vm) return;
        var dialog = new Dialogs.ColorPickerDialog(vm.CurrentProject.PrintSettings.OutlineColor);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            vm.CurrentProject.PrintSettings.OutlineColor = dialog.SelectedHexColor;
    }

    // --- Scroll & Pan ---

    private bool _isPanning;
    private Point _panStart;
    private double _panStartH, _panStartV;

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+Scroll = zoom
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
            if (_zoom < 0.5) delta *= 0.5;
            SetZoom(_zoom + delta);
            e.Handled = true;
            return;
        }

        // Shift+Scroll = horizontal scroll
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            CanvasScrollViewer.ScrollToHorizontalOffset(
                CanvasScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        // Plain scroll = vertical (default behavior, let ScrollViewer handle it)
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle-click = start panning
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panStart = e.GetPosition(CanvasScrollViewer);
            _panStartH = CanvasScrollViewer.HorizontalOffset;
            _panStartV = CanvasScrollViewer.VerticalOffset;
            CanvasScrollViewer.Cursor = Cursors.ScrollAll;
            CanvasScrollViewer.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            CanvasScrollViewer.Cursor = null;
            CanvasScrollViewer.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var pos = e.GetPosition(CanvasScrollViewer);
            double dx = _panStart.X - pos.X;
            double dy = _panStart.Y - pos.Y;
            CanvasScrollViewer.ScrollToHorizontalOffset(_panStartH + dx);
            CanvasScrollViewer.ScrollToVerticalOffset(_panStartV + dy);
            e.Handled = true;
        }
    }

    private void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom + ZoomStep);
    private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
    private void ZoomReset(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void ZoomFit(object sender, RoutedEventArgs e)
    {
        if (GridCanvas.Width > 0 && CanvasScrollViewer.ViewportWidth > 0)
        {
            double fitZoom = (CanvasScrollViewer.ViewportWidth - 80) / GridCanvas.Width;
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

    // --- Unsaved changes prompt ---

    private void OnExitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Shell.CanCloseApplication())
        {
            e.Cancel = true;
            return;
        }
        var s = Shell.AppSettings;
        s.SidebarSearchExpanded = SearchSection.IsExpanded;
        s.SidebarImportExpanded = ImportSection.IsExpanded;
        s.SidebarCardDetailsExpanded = CardDetailsSection.IsExpanded;
        s.SidebarLayoutExpanded = LayoutSection.IsExpanded;
        s.SidebarStorageExpanded = StorageSection.IsExpanded;
        s.SidebarWidth = SidebarColumn.Width.Value;
        Shell.SaveSettings();
    }
}
