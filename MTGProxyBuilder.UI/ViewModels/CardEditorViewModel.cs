using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;

namespace MTGProxyBuilder.UI.ViewModels
{
    public class CardEditorViewModel : ViewModelBase
    {
        private readonly CardCompositor _compositor = new();
        private readonly CustomCardSerializationService _serialization = new();

        private CustomCardProject _project;
        private ObservableCollection<LayerBase> _layers;
        private LayerBase? _selectedLayer;
        private string? _filePath;
        private bool _hasUnsavedChanges;
        private string _statusText = "Ready";
        private int _refreshTrigger;

        public CardEditorViewModel()
        {
            _project = new CustomCardProject();
            _layers = new ObservableCollection<LayerBase>();
            SubscribeToProject(_project);

            AddImageLayerCommand = new RelayCommand(_ => AddImageLayer());
            AddTextLayerCommand = new RelayCommand(_ => AddTextLayer());
            DeleteLayerCommand = new RelayCommand(_ => DeleteLayer(), _ => SelectedLayer != null);
            MoveLayerUpCommand = new RelayCommand(_ => MoveLayerUp(), _ => CanMoveUp());
            MoveLayerDownCommand = new RelayCommand(_ => MoveLayerDown(), _ => CanMoveDown());
            DuplicateLayerCommand = new RelayCommand(_ => DuplicateLayer(), _ => SelectedLayer != null);
            SaveCommand = new RelayCommand(_ => _ = SaveAsync());
            SaveAsCommand = new RelayCommand(_ => _ = SaveAsAsync());
            ExportImageCommand = new RelayCommand(_ => ExportImage());
            AddFrameLayerCommand = new RelayCommand(_ => AddFrameLayer());
        }

        // --- Properties ---

        public CustomCardProject Project
        {
            get => _project;
            private set
            {
                if (_project != null)
                    _project.PropertyChanged -= OnProjectPropertyChanged;
                SetProperty(ref _project, value);
                if (value != null)
                    SubscribeToProject(value);
            }
        }

        private void SubscribeToProject(CustomCardProject project)
        {
            project.PropertyChanged += OnProjectPropertyChanged;
        }

        private void OnProjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CustomCardProject.ProjectName))
                OnPropertyChanged(nameof(WindowTitle));
        }

        public ObservableCollection<LayerBase> Layers
        {
            get => _layers;
            private set => SetProperty(ref _layers, value);
        }

        public LayerBase? SelectedLayer
        {
            get => _selectedLayer;
            set
            {
                if (SetProperty(ref _selectedLayer, value))
                    OnPropertyChanged(nameof(SelectedLayer));
            }
        }

        public string? FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public int RefreshTrigger
        {
            get => _refreshTrigger;
            set => SetProperty(ref _refreshTrigger, value);
        }

        public string WindowTitle
        {
            get
            {
                var name = Project.ProjectName;
                var changed = HasUnsavedChanges ? " *" : "";
                return $"{name}{changed} — Card Editor";
            }
        }

        // --- Commands ---

        public ICommand AddImageLayerCommand { get; }
        public ICommand AddTextLayerCommand { get; }
        public ICommand DeleteLayerCommand { get; }
        public ICommand MoveLayerUpCommand { get; }
        public ICommand MoveLayerDownCommand { get; }
        public ICommand DuplicateLayerCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand ExportImageCommand { get; }
        public ICommand AddFrameLayerCommand { get; }

        // --- Layer Operations ---

        public void AddImageLayer()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            var layer = new ImageLayer
            {
                Name = Path.GetFileNameWithoutExtension(dialog.FileName),
                ImageSource = dialog.FileName,
                Width = 744,
                Height = 1039,
                ZOrder = GetNextZOrder()
            };

            // Try to get actual image dimensions
            try
            {
                using var bmp = SKBitmap.Decode(dialog.FileName);
                if (bmp != null)
                {
                    layer.Width = bmp.Width;
                    layer.Height = bmp.Height;
                }
            }
            catch { }

            AddLayer(layer);
        }

        public void AddTextLayer()
        {
            var layer = new TextLayer
            {
                Name = "Text Layer",
                Text = "Text",
                FontSize = 60,
                Width = 800,
                Height = 100,
                X = (Project.CardWidthPx - 800) / 2f,
                Y = Project.CardHeightPx / 2f,
                ZOrder = GetNextZOrder()
            };

            AddLayer(layer);
        }

        public void AddFrameLayer()
        {
            var dialog = new Dialogs.FrameBrowserDialog();
            dialog.Owner = System.Windows.Application.Current.Windows
                .OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);

            if (dialog.ShowDialog() != true || dialog.SelectedFrameBytes == null) return;

            // Save frame bytes to a temp file so the compositor can load it
            var tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "FrameCache");
            Directory.CreateDirectory(tempDir);
            var fileName = dialog.SelectedFrameKey!.Replace('/', '_');
            var tempPath = Path.Combine(tempDir, fileName);
            File.WriteAllBytes(tempPath, dialog.SelectedFrameBytes);

            // Build all layers from mapped regions first (art below frame, text above)
            // Layer order from bottom to top: art image → frame → text layers

            int baseZ = GetNextZOrder();

            // 1. Art region: add placeholder image layer (user fills in later or via dialog)
            var artRegion = dialog.FrameRegions.FirstOrDefault(r => r.Name == "art");
            if (artRegion != null)
            {
                var artPath = dialog.FieldValues.GetValueOrDefault("art");
                var artLayer = new ImageLayer
                {
                    Name = "Card Art",
                    ImageSource = artPath ?? string.Empty,
                    X = artRegion.X,
                    Y = artRegion.Y,
                    Width = artRegion.Width,
                    Height = artRegion.Height,
                    ZOrder = baseZ++
                };
                AddLayer(artLayer);
            }

            // 2. Frame image layer (renders on top of art)
            var frameLayer = new ImageLayer
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                ImageSource = tempPath,
                ImageBytes = dialog.SelectedFrameBytes,
                Width = dialog.FrameWidth,
                Height = dialog.FrameHeight,
                X = 0,
                Y = 0,
                ZOrder = baseZ++
            };
            AddLayer(frameLayer);

            // 3. Text layers for every mapped region (on top of frame)
            foreach (var region in dialog.FrameRegions)
            {
                if (region.Name == "art") continue; // Already handled above

                var userValue = dialog.FieldValues.GetValueOrDefault(region.Name) ?? string.Empty;

                var textLayer = new TextLayer
                {
                    Name = GetRegionDisplayName(region.Name),
                    Text = userValue,
                    X = region.X,
                    Y = region.Y,
                    Width = region.Width,
                    Height = region.Height,
                    FontSize = Math.Max(10, region.Height * 0.7f),
                    FontColor = "#FFFFFF",
                    ZOrder = baseZ++
                };
                AddLayer(textLayer);
            }
        }

        private static string GetRegionDisplayName(string regionName)
        {
            return regionName switch
            {
                "name" => "Card Name",
                "manaCost" => "Mana Cost",
                "typeLine" => "Type Line",
                "rulesText" => "Rules Text",
                "pt" => "P/T",
                "setSymbol" => "Set Symbol",
                "collector" => "Collector Info",
                _ => regionName
            };
        }

        private void AddLayer(LayerBase layer)
        {
            Project.Layers.Add(layer);
            Layers.Add(layer);
            SelectedLayer = layer;
            MarkChanged();
        }

        public void DeleteLayer()
        {
            if (SelectedLayer == null) return;

            var layer = SelectedLayer;
            Project.Layers.Remove(layer);
            Layers.Remove(layer);
            SelectedLayer = Layers.LastOrDefault();
            MarkChanged();
        }

        public void MoveLayerUp()
        {
            if (SelectedLayer == null) return;

            var idx = Layers.IndexOf(SelectedLayer);
            if (idx >= Layers.Count - 1) return;

            // Swap ZOrders
            var current = SelectedLayer;
            var above = Layers[idx + 1];
            (current.ZOrder, above.ZOrder) = (above.ZOrder, current.ZOrder);

            // Swap in collections
            Layers.Move(idx, idx + 1);
            var projIdx = Project.Layers.IndexOf(current);
            var projAboveIdx = Project.Layers.IndexOf(above);
            if (projIdx >= 0 && projAboveIdx >= 0)
            {
                Project.Layers[projIdx] = above;
                Project.Layers[projAboveIdx] = current;
            }

            MarkChanged();
        }

        public void MoveLayerDown()
        {
            if (SelectedLayer == null) return;

            var idx = Layers.IndexOf(SelectedLayer);
            if (idx <= 0) return;

            var current = SelectedLayer;
            var below = Layers[idx - 1];
            (current.ZOrder, below.ZOrder) = (below.ZOrder, current.ZOrder);

            Layers.Move(idx, idx - 1);
            var projIdx = Project.Layers.IndexOf(current);
            var projBelowIdx = Project.Layers.IndexOf(below);
            if (projIdx >= 0 && projBelowIdx >= 0)
            {
                Project.Layers[projIdx] = below;
                Project.Layers[projBelowIdx] = current;
            }

            MarkChanged();
        }

        private void DuplicateLayer()
        {
            if (SelectedLayer == null) return;

            LayerBase copy;
            if (SelectedLayer is ImageLayer img)
            {
                copy = new ImageLayer
                {
                    Name = img.Name + " (copy)",
                    ImageSource = img.ImageSource,
                    MaskEnabled = img.MaskEnabled,
                    MaskPath = img.MaskPath,
                    X = img.X + 20,
                    Y = img.Y + 20,
                    Width = img.Width,
                    Height = img.Height,
                    Rotation = img.Rotation,
                    Opacity = img.Opacity,
                    ZOrder = GetNextZOrder()
                };
            }
            else if (SelectedLayer is TextLayer txt)
            {
                copy = new TextLayer
                {
                    Name = txt.Name + " (copy)",
                    Text = txt.Text,
                    FontFamily = txt.FontFamily,
                    FontSize = txt.FontSize,
                    FontColor = txt.FontColor,
                    IsBold = txt.IsBold,
                    IsItalic = txt.IsItalic,
                    TextAlignment = txt.TextAlignment,
                    LineSpacing = txt.LineSpacing,
                    StrokeColor = txt.StrokeColor,
                    StrokeWidth = txt.StrokeWidth,
                    X = txt.X + 20,
                    Y = txt.Y + 20,
                    Width = txt.Width,
                    Height = txt.Height,
                    Rotation = txt.Rotation,
                    Opacity = txt.Opacity,
                    ZOrder = GetNextZOrder()
                };
            }
            else return;

            AddLayer(copy);
        }

        private bool CanMoveUp() => SelectedLayer != null && Layers.IndexOf(SelectedLayer) < Layers.Count - 1;
        private bool CanMoveDown() => SelectedLayer != null && Layers.IndexOf(SelectedLayer) > 0;
        private int GetNextZOrder() => Layers.Count > 0 ? Layers.Max(l => l.ZOrder) + 1 : 0;

        // --- File Operations ---

        /// <summary>
        /// Loads an existing project into this ViewModel (used by CardEditorShellViewModel).
        /// </summary>
        public void LoadProject(Core.Models.CustomCardProject project, string filePath)
        {
            _compositor.ClearCaches();
            Project = project;
            Layers = new ObservableCollection<LayerBase>(project.Layers.OrderBy(l => l.ZOrder));
            SelectedLayer = Layers.FirstOrDefault();
            FilePath = filePath;
            HasUnsavedChanges = false;
            StatusText = $"Opened {Path.GetFileName(filePath)}";
            OnPropertyChanged(nameof(WindowTitle));
            NotifyRefresh();
        }

        public async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                await SaveAsAsync();
                return;
            }

            SyncLayersToProject();
            await _serialization.SaveProjectAsync(Project, FilePath);
            HasUnsavedChanges = false;
            StatusText = "Saved";
            OnPropertyChanged(nameof(WindowTitle));
        }

        public async Task SaveAsAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Custom Card Project",
                Filter = "Custom Card Project|*.ccproj",
                FileName = Project.ProjectName
            };

            if (dialog.ShowDialog() != true) return;

            FilePath = dialog.FileName;
            SyncLayersToProject();
            await _serialization.SaveProjectAsync(Project, FilePath);
            HasUnsavedChanges = false;
            StatusText = $"Saved to {Path.GetFileName(FilePath)}";
            OnPropertyChanged(nameof(WindowTitle));
        }

        public void ExportImage()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Card Image",
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                FileName = Project.ProjectName
            };

            if (dialog.ShowDialog() != true) return;

            SyncLayersToProject();
            using var bitmap = _compositor.RenderCard(Project);

            var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() == ".jpg"
                ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            var quality = format == SKEncodedImageFormat.Jpeg ? 95 : 100;

            using var data = bitmap.Encode(format, quality);
            using var stream = File.OpenWrite(dialog.FileName);
            data.SaveTo(stream);

            StatusText = $"Exported to {Path.GetFileName(dialog.FileName)}";
        }

        // --- Helpers ---

        private void SyncLayersToProject()
        {
            Project.Layers = Layers.ToList();
        }

        private void MarkChanged()
        {
            HasUnsavedChanges = true;
            OnPropertyChanged(nameof(WindowTitle));
            NotifyRefresh();
        }

        /// <summary>
        /// Triggers a canvas re-render by incrementing the refresh trigger.
        /// </summary>
        public void NotifyRefresh()
        {
            RefreshTrigger++;
        }

        public CardCompositor Compositor => _compositor;
    }
}
