using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
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

    private static readonly string DockLayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MTGProxyBuilder", "dock_layout.xml");

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
        Closing += OnWindowClosing;
        Loaded += (_, _) => LoadDockLayout();

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
        };

        // Wire GridCanvas events and search box Enter keys after Loaded
        Loaded += (_, _) =>
        {
            ScryfallSearchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Shell?.ActiveProject?.Inner is MainViewModel vm
                    && vm.ScryfallSearchCommand.CanExecute(null))
                    vm.ScryfallSearchCommand.Execute(null);
            };

            DeckImportUrlBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Shell?.ActiveProject?.Inner is MainViewModel vm
                    && vm.ImportDeckCommand.CanExecute(null))
                    vm.ImportDeckCommand.Execute(null);
            };

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

    // --- Dock layout persistence ---

    // Map ContentId → the XAML-defined content that was in each panel before serialization
    private Dictionary<string, object>? _panelContents;

    private void CapturePanelContents()
    {
        // Save references to the XAML content of each anchorable and the document
        // so we can reconnect them after layout deserialization
        _panelContents = new Dictionary<string, object>();

        foreach (var anc in DockManager.Layout.Descendents().OfType<LayoutAnchorable>())
        {
            if (!string.IsNullOrEmpty(anc.ContentId) && anc.Content != null)
                _panelContents[anc.ContentId] = anc.Content;
        }

        foreach (var doc in DockManager.Layout.Descendents().OfType<LayoutDocument>())
        {
            if (!string.IsNullOrEmpty(doc.ContentId) && doc.Content != null)
                _panelContents[doc.ContentId] = doc.Content;
        }
    }

    private void LoadDockLayout()
    {
        try
        {
            if (!File.Exists(DockLayoutPath)) return;

            // Capture the XAML-defined content before deserialization destroys it
            CapturePanelContents();

            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.LayoutSerializationCallback += (s, args) =>
            {
                if (args.Model.ContentId != null && _panelContents != null
                    && _panelContents.TryGetValue(args.Model.ContentId, out var content))
                {
                    args.Content = content;
                }
                else
                {
                    // Unknown panel or missing content — cancel to avoid blank panels
                    args.Cancel = true;
                }
            };
            serializer.Deserialize(DockLayoutPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load dock layout from {Path}, resetting", DockLayoutPath);
            try { if (File.Exists(DockLayoutPath)) File.Delete(DockLayoutPath); }
            catch (Exception deleteEx) { Log.Warning(deleteEx, "Failed to delete corrupt dock layout file"); }
        }
    }

    private void SaveDockLayout()
    {
        try
        {
            var dir = Path.GetDirectoryName(DockLayoutPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.Serialize(DockLayoutPath);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to save dock layout"); }
    }

    // --- Unsaved changes prompt ---

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Shell.CanCloseApplication())
        {
            e.Cancel = true;
            return;
        }
        SaveDockLayout();
    }
}
