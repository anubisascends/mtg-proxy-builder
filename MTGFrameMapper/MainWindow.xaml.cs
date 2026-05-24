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
    private Point _drawStart;
    private Rectangle? _drawRect;

    // Zoom state
    private double _zoom = 1.0;
    private const double ZoomMin = 0.05;
    private const double ZoomMax = 5.0;
    private const double ZoomStep = 0.1;

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
        FrameImage.Width = _imageWidth;
        FrameImage.Height = _imageHeight;
        OverlayCanvas.Width = _imageWidth;
        OverlayCanvas.Height = _imageHeight;

        // Load existing regions
        if (_catalog.Frames.TryGetValue(key, out var meta))
            _regions = meta.Regions.Select(r => r.Clone()).ToList();
        else
            _regions = new List<FrameRegion>();

        _selectedRegionIndex = -1;
        RenderOverlay();
        RenderRegionList();

        FrameLabel.Text = $"{key}  |  {_imageWidth} x {_imageHeight}";
        SetStatus($"Loaded {key} ({_regions.Count} regions)");

        // Zoom to fit after layout has updated
        Dispatcher.InvokeAsync(ZoomToFit, System.Windows.Threading.DispatcherPriority.Loaded);
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
        if (!_isDrawing)
        {
            // Hit test for selection
            var pos = e.GetPosition(OverlayCanvas);
            for (int i = _regions.Count - 1; i >= 0; i--)
            {
                var r = _regions[i];
                if (pos.X >= r.X && pos.X <= r.X + r.Width && pos.Y >= r.Y && pos.Y <= r.Y + r.Height)
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

        _drawStart = e.GetPosition(OverlayCanvas);
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
        Canvas.SetLeft(_drawRect, _drawStart.X);
        Canvas.SetTop(_drawRect, _drawStart.Y);
        OverlayCanvas.Children.Add(_drawRect);
        OverlayCanvas.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _drawRect == null) return;

        var pos = e.GetPosition(OverlayCanvas);
        var x = Math.Min(_drawStart.X, pos.X);
        var y = Math.Min(_drawStart.Y, pos.Y);
        var w = Math.Abs(pos.X - _drawStart.X);
        var h = Math.Abs(pos.Y - _drawStart.Y);

        Canvas.SetLeft(_drawRect, x);
        Canvas.SetTop(_drawRect, y);
        _drawRect.Width = w;
        _drawRect.Height = h;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing || _drawRect == null) return;

        OverlayCanvas.ReleaseMouseCapture();

        var pos = e.GetPosition(OverlayCanvas);
        var x = Math.Min(_drawStart.X, pos.X);
        var y = Math.Min(_drawStart.Y, pos.Y);
        var w = Math.Abs(pos.X - _drawStart.X);
        var h = Math.Abs(pos.Y - _drawStart.Y);

        if (w > 5 && h > 5)
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

    private void RenderOverlay()
    {
        OverlayCanvas.Children.Clear();

        for (int i = 0; i < _regions.Count; i++)
        {
            var r = _regions[i];
            var color = GetRegionColor(r.Name);
            var isSelected = i == _selectedRegionIndex;

            // Region rectangle
            var rect = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = isSelected ? 3 : 2,
                StrokeDashArray = isSelected ? null : new DoubleCollection { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B))
            };
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            OverlayCanvas.Children.Add(rect);

            // Label background
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
            Canvas.SetLeft(labelBg, r.X);
            Canvas.SetTop(labelBg, r.Y - 18);
            OverlayCanvas.Children.Add(labelBg);

            Canvas.SetLeft(labelText, r.X + 4);
            Canvas.SetTop(labelText, r.Y - 18);
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
        BuildTree();
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
        BuildTree();
        SetStatus($"Copied regions to {unmapped.Count} frames in {folder}");
    }

    // ==================== Zoom ====================

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, ZoomMin, ZoomMax);
        CanvasScale.ScaleX = _zoom;
        CanvasScale.ScaleY = _zoom;
        ZoomLabel.Text = $"{_zoom * 100:F0}%";
    }

    private void ZoomToFit()
    {
        if (_imageWidth == 0 || _imageHeight == 0) return;

        var viewW = CanvasScroll.ActualWidth - 40;
        var viewH = CanvasScroll.ActualHeight - 40;
        if (viewW < 1 || viewH < 1) return;

        double scaleX = viewW / _imageWidth;
        double scaleY = viewH / _imageHeight;
        SetZoom(Math.Min(scaleX, scaleY));
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoom + ZoomStep);
    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoom - ZoomStep);
    private void OnZoomFit(object sender, RoutedEventArgs e) => ZoomToFit();
    private void OnZoomReset(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
        if (_zoom < 0.3) delta *= 0.5;
        SetZoom(_zoom + delta);
        e.Handled = true;
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
