using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Newtonsoft.Json;

namespace MTGFrameMapper;

public partial class MainWindow : Window
{
    private static readonly Assembly ResourceAssembly = typeof(MTGProxyBuilder.Resources.Frames.FrameProvider).Assembly;
    private const string FramesPrefix = "MTGProxyBuilder.Resources.Frames.";

    private readonly string _catalogPath;
    private FrameCatalog _catalog = new();

    // All discovered embedded frame resources, grouped by folder
    // Key = "Modern/modernFrameW.png", maps to full resource name
    private readonly Dictionary<string, string> _resourceMap = new();

    // Current frame state
    private string? _currentFrameKey;
    private int _imageWidth;
    private int _imageHeight;
    private List<FrameRegion> _regions = new();
    private int _selectedRegionIndex = -1;

    // Drawing state
    private bool _isDrawing;
    private string _drawRegionName = string.Empty;
    private Point _drawStartPx; // in image-pixel coordinates
    private Rectangle? _drawRect;


    private static readonly Dictionary<string, Color> RegionColors = new()
    {
        ["art"] = Color.FromRgb(0x4C, 0xAF, 0x50),
        ["name"] = Color.FromRgb(0x21, 0x96, 0xF3),
        ["manaCost"] = Color.FromRgb(0xFF, 0x98, 0x00),
        ["typeLine"] = Color.FromRgb(0x9C, 0x27, 0xB0),
        ["rulesText"] = Color.FromRgb(0xF4, 0x43, 0x36),
        ["pt"] = Color.FromRgb(0xFF, 0xEB, 0x3B),
        ["setSymbol"] = Color.FromRgb(0x00, 0xBC, 0xD4),
        ["collector"] = Color.FromRgb(0x79, 0x55, 0x48),
    };

    private static readonly Dictionary<string, string> RegionLabels = new()
    {
        ["art"] = "Art Box",
        ["name"] = "Name",
        ["manaCost"] = "Mana Cost",
        ["typeLine"] = "Type Line",
        ["rulesText"] = "Rules Text",
        ["pt"] = "P/T Box",
        ["setSymbol"] = "Set Symbol",
        ["collector"] = "Collector Info",
    };

    public MainWindow()
    {
        InitializeComponent();

        InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnCopyMappings(this, new RoutedEventArgs())),
            Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnPasteMappings(this, new RoutedEventArgs())),
            Key.V, ModifierKeys.Control | ModifierKeys.Shift));

        _catalogPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MTGProxyBuilder", "frame_catalog.json");

        DiscoverEmbeddedFrames();
        LoadCatalog();
        BuildTree();
        SetStatus($"Loaded {_resourceMap.Count} embedded frame images");
    }

    // ==================== Embedded Resource Discovery ====================

    private void DiscoverEmbeddedFrames()
    {
        _resourceMap.Clear();

        var allNames = ResourceAssembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(FramesPrefix))
            .OrderBy(n => n);

        foreach (var fullName in allNames)
        {
            // fullName example: "MTGProxyBuilder.Resources.Frames.Modern.modernFrameW.png"
            // Strip prefix to get: "Modern.modernFrameW.png"
            var relative = fullName[FramesPrefix.Length..];

            // Split into folder and filename
            // The folder is the first segment, the rest is the filename
            var dotIndex = relative.IndexOf('.');
            if (dotIndex < 0) continue;

            // Find the extension (.png, .jpg, etc.)
            var ext = "";
            if (relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ext = ".png";
            else if (relative.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) ext = ".jpg";
            else if (relative.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) ext = ".jpeg";
            else continue; // Not an image

            // Everything before ext is "Folder.filename"
            var withoutExt = relative[..^ext.Length];
            var folderDotIdx = withoutExt.IndexOf('.');
            if (folderDotIdx < 0) continue;

            var folder = withoutExt[..folderDotIdx];
            var fileName = withoutExt[(folderDotIdx + 1)..] + ext;
            var key = folder + "/" + fileName;

            _resourceMap[key] = fullName;
        }
    }

    private BitmapImage? LoadEmbeddedImage(string key)
    {
        if (!_resourceMap.TryGetValue(key, out var fullName)) return null;

        using var stream = ResourceAssembly.GetManifestResourceStream(fullName);
        if (stream == null) return null;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // ==================== Catalog ====================

    private void LoadCatalog()
    {
        if (File.Exists(_catalogPath))
        {
            try
            {
                var json = File.ReadAllText(_catalogPath);
                _catalog = JsonConvert.DeserializeObject<FrameCatalog>(json) ?? new FrameCatalog();
            }
            catch { _catalog = new FrameCatalog(); }
        }
        else
        {
            _catalog = new FrameCatalog();
        }
    }

    private void SaveCatalog()
    {
        var dir = System.IO.Path.GetDirectoryName(_catalogPath);
        if (dir != null) Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(_catalog, Formatting.Indented);
        File.WriteAllText(_catalogPath, json);
    }

    // ==================== TreeView ====================

    private void BuildTree()
    {
        FrameTree.Items.Clear();

        // Group by folder
        var folders = _resourceMap.Keys
            .GroupBy(k => k.Split('/')[0])
            .OrderBy(g => g.Key);

        int totalFiles = 0;
        int totalMapped = 0;

        foreach (var folder in folders)
        {
            var files = folder.OrderBy(f => f).ToList();
            var mapped = files.Count(f => IsMapped(f));
            totalFiles += files.Count;
            totalMapped += mapped;

            var folderNode = new TreeViewItem
            {
                Header = $"{folder.Key}  ({mapped}/{files.Count})",
                Tag = "folder:" + folder.Key,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
            };

            foreach (var key in files)
            {
                var fileName = key.Split('/')[1];
                var isMapped = IsMapped(key);

                var fileNode = new TreeViewItem
                {
                    Header = (isMapped ? "\u2713 " : "   ") + fileName,
                    Tag = key,
                    Foreground = new SolidColorBrush(isMapped
                        ? Color.FromRgb(0x4C, 0xAF, 0x50)
                        : Color.FromRgb(0xAA, 0xAA, 0xAA))
                };
                folderNode.Items.Add(fileNode);
            }

            FrameTree.Items.Add(folderNode);
        }

        StatsLabel.Text = $"{totalFiles} images | {totalMapped} mapped";
    }

    private bool IsMapped(string key)
    {
        return _catalog.Frames.TryGetValue(key, out var meta) && meta.Regions.Count > 0;
    }

    /// <summary>
    /// Updates the tree in-place for the given keys without collapsing anything.
    /// </summary>
    private void RefreshTreeItems(params string[] keys)
    {
        var foldersToUpdate = new HashSet<string>();

        foreach (var key in keys)
        {
            var folder = key.Split('/')[0];
            foldersToUpdate.Add(folder);

            // Find and update the file node
            var folderNode = FindFolderNode(folder);
            if (folderNode == null) continue;

            foreach (TreeViewItem fileNode in folderNode.Items)
            {
                if (fileNode.Tag as string == key)
                {
                    var fileName = key.Split('/')[1];
                    var isMapped = IsMapped(key);
                    fileNode.Header = (isMapped ? "\u2713 " : "   ") + fileName;
                    fileNode.Foreground = new SolidColorBrush(isMapped
                        ? Color.FromRgb(0x4C, 0xAF, 0x50)
                        : Color.FromRgb(0xAA, 0xAA, 0xAA));
                    break;
                }
            }
        }

        // Update folder headers with new mapped counts
        foreach (var folder in foldersToUpdate)
        {
            var folderNode = FindFolderNode(folder);
            if (folderNode == null) continue;

            var total = folderNode.Items.Count;
            int mapped = 0;
            foreach (TreeViewItem fileNode in folderNode.Items)
            {
                if (fileNode.Tag is string tag && IsMapped(tag))
                    mapped++;
            }
            folderNode.Header = $"{folder}  ({mapped}/{total})";
        }

        // Update stats
        int totalFiles = 0, totalMapped = 0;
        foreach (TreeViewItem folderNode in FrameTree.Items)
        {
            totalFiles += folderNode.Items.Count;
            foreach (TreeViewItem fileNode in folderNode.Items)
            {
                if (fileNode.Tag is string tag && IsMapped(tag))
                    totalMapped++;
            }
        }
        StatsLabel.Text = $"{totalFiles} images | {totalMapped} mapped";
    }

    private TreeViewItem? FindFolderNode(string folder)
    {
        foreach (TreeViewItem node in FrameTree.Items)
        {
            if (node.Tag as string == "folder:" + folder)
                return node;
        }
        return null;
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item) return;

        var tag = item.Tag as string;
        if (string.IsNullOrEmpty(tag) || tag.StartsWith("folder:")) return;

        LoadFrame(tag);
    }

    // ==================== Frame Loading ====================

    private void LoadFrame(string key)
    {
        var bitmap = LoadEmbeddedImage(key);
        if (bitmap == null) { SetStatus($"Could not load: {key}"); return; }

        _currentFrameKey = key;
        _imageWidth = bitmap.PixelWidth;
        _imageHeight = bitmap.PixelHeight;

        FrameImage.Source = bitmap;

        // Load existing regions
        if (_catalog.Frames.TryGetValue(key, out var meta))
            _regions = meta.Regions.Select(r => r.Clone()).ToList();
        else
            _regions = new List<FrameRegion>();

        _selectedRegionIndex = -1;

        // Update overlay after layout settles (image needs to compute its rendered size)
        Dispatcher.InvokeAsync(() =>
        {
            SyncOverlaySize();
            RenderOverlay();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        RenderRegionList();

        FrameLabel.Text = $"{key}  |  {_imageWidth} x {_imageHeight}";
        SetStatus($"Loaded {key} ({_regions.Count} regions)");
    }

    // ==================== Drawing ====================

    private void OnDrawRegion(object sender, RoutedEventArgs e)
    {
        if (_currentFrameKey == null) { SetStatus("Load a frame first"); return; }

        var selected = RegionPreset.SelectedItem as ComboBoxItem;
        if (selected?.Tag is not string tag) return;

        _drawRegionName = tag;
        _isDrawing = true;
        OverlayCanvas.Cursor = Cursors.Cross;
        SetStatus($"Draw the {GetRegionLabel(tag)} region on the frame");
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var px = ScreenToPixel(e.GetPosition(OverlayCanvas));

        if (!_isDrawing)
        {
            // Hit test for selection in image-pixel space
            for (int i = _regions.Count - 1; i >= 0; i--)
            {
                var r = _regions[i];
                if (px.X >= r.X && px.X <= r.X + r.Width && px.Y >= r.Y && px.Y <= r.Y + r.Height)
                {
                    _selectedRegionIndex = i;
                    RegionList.SelectedIndex = i;
                    RenderOverlay();
                    return;
                }
            }
            _selectedRegionIndex = -1;
            RegionList.SelectedIndex = -1;
            RenderOverlay();
            return;
        }

        _drawStartPx = px;
        var screenStart = e.GetPosition(OverlayCanvas);
        _drawRect = new Rectangle
        {
            Stroke = new SolidColorBrush(GetRegionColor(_drawRegionName)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x33,
                GetRegionColor(_drawRegionName).R,
                GetRegionColor(_drawRegionName).G,
                GetRegionColor(_drawRegionName).B))
        };
        Canvas.SetLeft(_drawRect, screenStart.X);
        Canvas.SetTop(_drawRect, screenStart.Y);
        OverlayCanvas.Children.Add(_drawRect);
        OverlayCanvas.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _drawRect == null) return;

        var screenPos = e.GetPosition(OverlayCanvas);
        var screenStart = PixelToScreen(_drawStartPx);
        var x = Math.Min(screenStart.X, screenPos.X);
        var y = Math.Min(screenStart.Y, screenPos.Y);
        var w = Math.Abs(screenPos.X - screenStart.X);
        var h = Math.Abs(screenPos.Y - screenStart.Y);

        Canvas.SetLeft(_drawRect, x);
        Canvas.SetTop(_drawRect, y);
        _drawRect.Width = w;
        _drawRect.Height = h;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing || _drawRect == null) return;

        OverlayCanvas.ReleaseMouseCapture();

        var endPx = ScreenToPixel(e.GetPosition(OverlayCanvas));
        var x = Math.Min(_drawStartPx.X, endPx.X);
        var y = Math.Min(_drawStartPx.Y, endPx.Y);
        var w = Math.Abs(endPx.X - _drawStartPx.X);
        var h = Math.Abs(endPx.Y - _drawStartPx.Y);

        if (w > 3 && h > 3)
        {
            _regions.Add(new FrameRegion
            {
                Name = _drawRegionName,
                X = (int)Math.Round(x),
                Y = (int)Math.Round(y),
                Width = (int)Math.Round(w),
                Height = (int)Math.Round(h)
            });
            _selectedRegionIndex = _regions.Count - 1;
            SetStatus($"Added {GetRegionLabel(_drawRegionName)} region");
        }

        _isDrawing = false;
        _drawRect = null;
        OverlayCanvas.Cursor = Cursors.Arrow;
        RenderOverlay();
        RenderRegionList();
    }

    // ==================== Overlay Rendering ====================

    /// <summary>
    /// Syncs the overlay canvas size to match the image's actual rendered size.
    /// </summary>
    private void SyncOverlaySize()
    {
        OverlayCanvas.Width = FrameImage.ActualWidth;
        OverlayCanvas.Height = FrameImage.ActualHeight;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // When the viewport resizes, the Uniform-stretched image changes size
        Dispatcher.InvokeAsync(() =>
        {
            SyncOverlaySize();
            RenderOverlay();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RenderOverlay()
    {
        OverlayCanvas.Children.Clear();
        if (_imageWidth == 0 || _imageHeight == 0) return;

        for (int i = 0; i < _regions.Count; i++)
        {
            var r = _regions[i];
            var color = GetRegionColor(r.Name);
            var isSelected = i == _selectedRegionIndex;

            // Convert image-pixel rect to screen coordinates
            var topLeft = PixelToScreen(new Point(r.X, r.Y));
            var bottomRight = PixelToScreen(new Point(r.X + r.Width, r.Y + r.Height));
            double sx = topLeft.X;
            double sy = topLeft.Y;
            double sw = bottomRight.X - topLeft.X;
            double sh = bottomRight.Y - topLeft.Y;

            var rect = new Rectangle
            {
                Width = sw,
                Height = sh,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = isSelected ? 3 : 2,
                StrokeDashArray = isSelected ? null : new DoubleCollection { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B))
            };
            Canvas.SetLeft(rect, sx);
            Canvas.SetTop(rect, sy);
            OverlayCanvas.Children.Add(rect);

            // Label
            var label = GetRegionLabel(r.Name);
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            labelText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelW = labelText.DesiredSize.Width + 8;

            var labelBg = new Rectangle
            {
                Width = labelW,
                Height = 16,
                Fill = new SolidColorBrush(Color.FromArgb(0xCC, color.R, color.G, color.B)),
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(labelBg, sx);
            Canvas.SetTop(labelBg, sy - 18);
            OverlayCanvas.Children.Add(labelBg);

            Canvas.SetLeft(labelText, sx + 4);
            Canvas.SetTop(labelText, sy - 18);
            OverlayCanvas.Children.Add(labelText);
        }
    }

    // ==================== Region List ====================

    private void RenderRegionList()
    {
        RegionList.Items.Clear();

        for (int i = 0; i < _regions.Count; i++)
        {
            var r = _regions[i];
            var color = GetRegionColor(r.Name);
            var label = GetRegionLabel(r.Name);
            int idx = i;

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            var swatch = new Border
            {
                Width = 12, Height = 12,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(swatch);

            var nameBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 80
            };
            sp.Children.Add(nameBlock);

            var coordBlock = new TextBlock
            {
                Text = $"{r.X},{r.Y} {r.Width}x{r.Height}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            sp.Children.Add(coordBlock);

            var delBtn = new Button
            {
                Content = "\u2715",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x60)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            delBtn.Click += (_, _) =>
            {
                _regions.RemoveAt(idx);
                if (_selectedRegionIndex >= _regions.Count) _selectedRegionIndex = _regions.Count - 1;
                RenderOverlay();
                RenderRegionList();
            };
            sp.Children.Add(delBtn);

            RegionList.Items.Add(sp);
        }

        if (_selectedRegionIndex >= 0 && _selectedRegionIndex < RegionList.Items.Count)
            RegionList.SelectedIndex = _selectedRegionIndex;
    }

    private void OnRegionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedRegionIndex = RegionList.SelectedIndex;
        RenderOverlay();
    }

    // ==================== Save / Copy ====================

    private void OnCopyMappings(object sender, RoutedEventArgs e)
    {
        if (_regions.Count == 0) { SetStatus("No regions to copy"); return; }

        var json = JsonConvert.SerializeObject(_regions, Formatting.Indented);
        Clipboard.SetText(json);
        SetStatus($"Copied {_regions.Count} regions to clipboard");
    }

    private void OnPasteMappings(object sender, RoutedEventArgs e)
    {
        if (_currentFrameKey == null) { SetStatus("Load a frame first"); return; }

        if (!Clipboard.ContainsText()) { SetStatus("Clipboard is empty"); return; }

        try
        {
            var pasted = JsonConvert.DeserializeObject<List<FrameRegion>>(Clipboard.GetText());
            if (pasted == null || pasted.Count == 0) { SetStatus("No valid regions on clipboard"); return; }

            _regions = pasted.Select(r => r.Clone()).ToList();
            _selectedRegionIndex = -1;
            RenderOverlay();
            RenderRegionList();
            SetStatus($"Pasted {_regions.Count} regions from clipboard");
        }
        catch
        {
            SetStatus("Clipboard does not contain valid region data");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_currentFrameKey == null) { SetStatus("Nothing to save"); return; }

        _catalog.Frames[_currentFrameKey] = new FrameMetadataEntry
        {
            FramePath = _currentFrameKey,
            ImageWidth = _imageWidth,
            ImageHeight = _imageHeight,
            Regions = _regions.Select(r => r.Clone()).ToList()
        };

        SaveCatalog();
        RefreshTreeItems(_currentFrameKey);
        SetStatus($"Saved {_currentFrameKey} ({_regions.Count} regions)");
    }

    private void OnCopyToFolder(object sender, RoutedEventArgs e)
    {
        if (_currentFrameKey == null || _regions.Count == 0) { SetStatus("Map regions first"); return; }

        var folder = _currentFrameKey.Split('/')[0];

        // Find all other frames in the same folder
        var folderFrames = _resourceMap.Keys
            .Where(k => k.StartsWith(folder + "/") && k != _currentFrameKey)
            .ToList();

        if (folderFrames.Count == 0) { SetStatus("No other frames in this folder"); return; }

        var unmapped = folderFrames.Where(k => !IsMapped(k)).ToList();

        var result = MessageBox.Show(
            $"Copy {_regions.Count} regions to {unmapped.Count} unmapped frames in \"{folder}\"?",
            "Copy Regions", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        foreach (var key in unmapped)
        {
            _catalog.Frames[key] = new FrameMetadataEntry
            {
                FramePath = key,
                ImageWidth = _imageWidth,
                ImageHeight = _imageHeight,
                Regions = _regions.Select(r => r.Clone()).ToList()
            };
        }

        SaveCatalog();
        RefreshTreeItems(unmapped.ToArray());
        SetStatus($"Copied regions to {unmapped.Count} frames in {folder}");
    }

    // ==================== Coordinate Conversion ====================

    /// <summary>
    /// Returns the scale factor from image pixels to displayed screen pixels.
    /// The Image uses Stretch=Uniform, so WPF scales it to fit the available space.
    /// </summary>
    private double GetDisplayScale()
    {
        if (_imageWidth == 0 || FrameImage.ActualWidth < 1) return 1;
        return FrameImage.ActualWidth / _imageWidth;
    }

    /// <summary>
    /// Converts a screen position (relative to the overlay canvas) to image-pixel coordinates.
    /// </summary>
    private Point ScreenToPixel(Point screen)
    {
        double scale = GetDisplayScale();
        return new Point(screen.X / scale, screen.Y / scale);
    }

    /// <summary>
    /// Converts image-pixel coordinates to screen position (relative to the overlay canvas).
    /// </summary>
    private Point PixelToScreen(Point pixel)
    {
        double scale = GetDisplayScale();
        return new Point(pixel.X * scale, pixel.Y * scale);
    }

    // ==================== Helpers ====================

    private static Color GetRegionColor(string name)
    {
        return RegionColors.TryGetValue(name, out var c) ? c : Color.FromRgb(0x60, 0x7D, 0x8B);
    }

    private static string GetRegionLabel(string name)
    {
        return RegionLabels.TryGetValue(name, out var l) ? l : name;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

}

// ==================== Models ====================

public class FrameRegion
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public FrameRegion Clone() => new()
    {
        Name = Name, X = X, Y = Y, Width = Width, Height = Height
    };
}

public class FrameMetadataEntry
{
    public string FramePath { get; set; } = string.Empty;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public List<FrameRegion> Regions { get; set; } = new();
}

public class FrameCatalog
{
    public int Version { get; set; } = 1;
    public Dictionary<string, FrameMetadataEntry> Frames { get; set; } = new();
}


public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    public RelayCommand(Action<object?> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
}
