using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class FrameBrowserDialog : Window
    {
        private static readonly Assembly ResourceAssembly =
            typeof(MTGProxyBuilder.Resources.Frames.FrameProvider).Assembly;
        private const string FramesPrefix = "MTGProxyBuilder.Resources.Frames.";

        private readonly Dictionary<string, string> _resourceMap = new();
        private FrameCatalogData _catalog = new();
        private string? _selectedFrameKey;
        private readonly Dictionary<string, TextBox> _fieldInputs = new();

        private static readonly Dictionary<string, string> RegionLabels = new()
        {
            ["art"] = "Art Box",
            ["name"] = "Card Name",
            ["manaCost"] = "Mana Cost",
            ["typeLine"] = "Type Line",
            ["rulesText"] = "Rules Text",
            ["pt"] = "P/T",
            ["setSymbol"] = "Set Symbol",
            ["collector"] = "Collector Info",
        };

        public FrameBrowserDialog()
        {
            InitializeComponent();
            DiscoverFrames();
            LoadCatalog();
            BuildTree();
        }

        /// <summary>The selected frame resource key (e.g. "Modern/modernFrameW.png").</summary>
        public string? SelectedFrameKey => _selectedFrameKey;

        /// <summary>The raw image bytes of the selected frame.</summary>
        public byte[]? SelectedFrameBytes { get; private set; }

        /// <summary>Image dimensions.</summary>
        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }

        /// <summary>Field values the user entered for mapped regions (regionName -> value).</summary>
        public Dictionary<string, string> FieldValues { get; } = new();

        /// <summary>Region rects from the catalog for the selected frame.</summary>
        public List<FrameRegionData> FrameRegions { get; private set; } = new();

        // ==================== Resource Discovery ====================

        private void DiscoverFrames()
        {
            var allNames = ResourceAssembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(FramesPrefix))
                .OrderBy(n => n);

            foreach (var fullName in allNames)
            {
                var relative = fullName[FramesPrefix.Length..];

                var ext = "";
                if (relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ext = ".png";
                else if (relative.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) ext = ".jpg";
                else continue;

                var withoutExt = relative[..^ext.Length];
                var dotIdx = withoutExt.IndexOf('.');
                if (dotIdx < 0) continue;

                var folder = withoutExt[..dotIdx];
                var fileName = withoutExt[(dotIdx + 1)..] + ext;
                var key = folder + "/" + fileName;

                _resourceMap[key] = fullName;
            }
        }

        // ==================== Catalog ====================

        private void LoadCatalog()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "frame_catalog.json");

            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                _catalog = JsonConvert.DeserializeObject<FrameCatalogData>(json) ?? new FrameCatalogData();
            }
            catch { }
        }

        // ==================== Tree ====================

        private void BuildTree()
        {
            FrameTree.Items.Clear();

            var folders = _resourceMap.Keys
                .GroupBy(k => k.Split('/')[0])
                .OrderBy(g => g.Key);

            foreach (var folder in folders)
            {
                var files = folder.OrderBy(f => f).ToList();
                var hasMappings = files.Any(f =>
                    _catalog.Frames.TryGetValue(f, out var m) && m.Regions.Count > 0);

                var folderNode = new TreeViewItem
                {
                    Header = folder.Key,
                    Tag = "folder:" + folder.Key,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
                };

                foreach (var key in files)
                {
                    var fileName = key.Split('/')[1];
                    var isMapped = _catalog.Frames.TryGetValue(key, out var meta) && meta.Regions.Count > 0;

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
            if (!_resourceMap.TryGetValue(key, out var fullName)) return;

            using var stream = ResourceAssembly.GetManifestResourceStream(fullName);
            if (stream == null) return;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            SelectedFrameBytes = ms.ToArray();

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(SelectedFrameBytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            FrameWidth = bitmap.PixelWidth;
            FrameHeight = bitmap.PixelHeight;
            _selectedFrameKey = key;
            AddButton.IsEnabled = true;

            // Build field inputs from catalog regions
            BuildFieldInputs(key);

            StatusLabel.Text = $"{key}  |  {FrameWidth}x{FrameHeight}";
        }

        // ==================== Field Inputs ====================

        private void BuildFieldInputs(string frameKey)
        {
            FieldsPanel.Children.Clear();
            _fieldInputs.Clear();
            FrameRegions.Clear();

            if (!_catalog.Frames.TryGetValue(frameKey, out var meta) || meta.Regions.Count == 0)
            {
                FieldsPanel.Children.Add(new TextBlock
                {
                    Text = "No region mappings for this frame.\n\nUse the Frame Mapper to define regions.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            FrameRegions = meta.Regions.ToList();

            foreach (var region in meta.Regions)
            {
                var label = RegionLabels.TryGetValue(region.Name, out var l) ? l : region.Name;

                var labelBlock = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    FontSize = 10,
                    Margin = new Thickness(0, 6, 0, 2)
                };
                FieldsPanel.Children.Add(labelBlock);

                // Art box gets a file browse instead of text input
                if (region.Name == "art")
                {
                    var browseGrid = new Grid();
                    browseGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    browseGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var pathBox = new TextBox
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        CaretBrush = Brushes.White,
                        Padding = new Thickness(4, 3, 4, 3),
                        FontSize = 11,
                        IsReadOnly = true
                    };
                    Grid.SetColumn(pathBox, 0);

                    var browseBtn = new Button
                    {
                        Content = "Browse",
                        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(4, 0, 0, 0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        FontSize = 11
                    };
                    browseBtn.Click += (_, _) =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog
                        {
                            Title = "Select Art Image",
                            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*"
                        };
                        if (dlg.ShowDialog() == true)
                            pathBox.Text = dlg.FileName;
                    };
                    Grid.SetColumn(browseBtn, 1);

                    browseGrid.Children.Add(pathBox);
                    browseGrid.Children.Add(browseBtn);
                    FieldsPanel.Children.Add(browseGrid);
                    _fieldInputs[region.Name] = pathBox;
                }
                else
                {
                    var textBox = new TextBox
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        CaretBrush = Brushes.White,
                        Padding = new Thickness(4, 3, 4, 3),
                        FontSize = 11,
                        AcceptsReturn = region.Name == "rulesText",
                        TextWrapping = region.Name == "rulesText" ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        MinHeight = region.Name == "rulesText" ? 60 : 0
                    };
                    FieldsPanel.Children.Add(textBox);
                    _fieldInputs[region.Name] = textBox;
                }

                // Show region coordinates
                var coordBlock = new TextBlock
                {
                    Text = $"  {region.X},{region.Y}  {region.Width}x{region.Height}px",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 9
                };
                FieldsPanel.Children.Add(coordBlock);
            }
        }

        // ==================== Actions ====================

        private void OnAddToCard(object sender, RoutedEventArgs e)
        {
            // Collect field values
            FieldValues.Clear();
            foreach (var (name, textBox) in _fieldInputs)
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                    FieldValues[name] = textBox.Text;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // ==================== Catalog Models (local, matches FrameMapper) ====================

    public class FrameRegionData
    {
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class FrameMetadataData
    {
        public string FramePath { get; set; } = string.Empty;
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public List<FrameRegionData> Regions { get; set; } = new();
    }

    public class FrameCatalogData
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, FrameMetadataData> Frames { get; set; } = new();
    }
}
