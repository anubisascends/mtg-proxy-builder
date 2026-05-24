using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class CardEditorWindow : Window
    {
        private CardEditorShellViewModel Shell => (CardEditorShellViewModel)DataContext;

        private static readonly string DockLayoutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MTGProxyBuilder", "card_editor_dock_layout.xml");

        private Dictionary<string, object>? _panelContents;

        public CardEditorWindow()
        {
            DataContext = new CardEditorShellViewModel();
            InitializeComponent();

            // Keyboard shortcuts bound to active tab
            InputBindings.Add(new KeyBinding(new RelayCommand(_ => SaveActiveTab()), Key.S, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(Shell.NewTabCommand, Key.N, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(Shell.OpenTabCommand, Key.O, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(Shell.CloseTabCommand, Key.W, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new RelayCommand(_ => DeleteActiveLayer()), Key.Delete, ModifierKeys.None));

            Shell.PropertyChanged += Shell_PropertyChanged;

            // Wire canvas layer selection to ViewModel
            EditorCanvas.LayerSelected += (_, layer) =>
            {
                if (Shell.ActiveTab != null)
                    Shell.ActiveTab.Inner.SelectedLayer = layer;
            };

            Loaded += (_, _) =>
            {
                LoadDockLayout();
                UpdatePanelsForActiveTab();
            };
        }

        private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardEditorShellViewModel.ActiveTab))
            {
                UpdatePanelsForActiveTab();
            }
        }

        private void UpdatePanelsForActiveTab()
        {
            var vm = Shell.ActiveTab?.Inner;
            if (vm != null)
            {
                PropPanel.UpdateForSelection(vm.SelectedLayer);
                EditorCanvas.QueueRedraw();

                // Listen for property changes on the active tab's ViewModel
                vm.PropertyChanged -= ActiveVm_PropertyChanged;
                vm.PropertyChanged += ActiveVm_PropertyChanged;
            }
        }

        private void ActiveVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CardEditorViewModel.SelectedLayer))
            {
                var vm = Shell.ActiveTab?.Inner;
                if (vm != null)
                    PropPanel.UpdateForSelection(vm.SelectedLayer);
                EditorCanvas.QueueRedraw();
            }
            else if (e.PropertyName == nameof(CardEditorViewModel.RefreshTrigger))
            {
                EditorCanvas.QueueRedraw();
            }
        }

        // --- Tab Bar Handlers ---

        private void OnNewTab(object sender, RoutedEventArgs e) => Shell.NewTab();

        private async void OnOpenTab(object sender, RoutedEventArgs e) => await Shell.OpenTabAsync();

        private void OnTabClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CardEditorTabViewModel tab)
                Shell.ActiveTab = tab;
        }

        private void OnTabClose(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CardEditorTabViewModel tab)
                Shell.CloseTab(tab);
        }

        // --- Layer Panel Routed Event Handlers ---

        private void OnAddImageLayer(object sender, RoutedEventArgs e) => Shell.ActiveTab?.Inner.AddImageLayer();
        private void OnAddTextLayer(object sender, RoutedEventArgs e) => Shell.ActiveTab?.Inner.AddTextLayer();
        private void OnDeleteLayer(object sender, RoutedEventArgs e) => Shell.ActiveTab?.Inner.DeleteLayer();
        private void OnMoveUp(object sender, RoutedEventArgs e) => Shell.ActiveTab?.Inner.MoveLayerUp();
        private void OnMoveDown(object sender, RoutedEventArgs e) => Shell.ActiveTab?.Inner.MoveLayerDown();

        private void OnBrowseImage(object sender, RoutedEventArgs e)
        {
            var vm = Shell.ActiveTab?.Inner;
            if (vm?.SelectedLayer is not ImageLayer imgLayer) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All Files|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            EditorCanvas.Compositor.InvalidateImage(imgLayer.ImageSource);
            imgLayer.ImageSource = dialog.FileName;
            imgLayer.ImageBytes = null;

            try
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(dialog.FileName);
                if (bmp != null)
                {
                    imgLayer.Width = bmp.Width;
                    imgLayer.Height = bmp.Height;
                }
            }
            catch { }

            vm.NotifyRefresh();
        }

        // --- Keyboard shortcut helpers ---

        private void SaveActiveTab()
        {
            if (Shell.ActiveTab != null)
                _ = Shell.ActiveTab.Inner.SaveAsync();
        }

        private void DeleteActiveLayer()
        {
            Shell.ActiveTab?.Inner.DeleteLayer();
        }

        // --- Dock Layout Persistence ---

        private void CapturePanelContents()
        {
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
                        args.Cancel = true;
                    }
                };
                serializer.Deserialize(DockLayoutPath);
            }
            catch
            {
                try { if (File.Exists(DockLayoutPath)) File.Delete(DockLayoutPath); } catch { }
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
            catch { }
        }

        // --- Window Closing ---

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!Shell.CanCloseWindow())
            {
                e.Cancel = true;
                return;
            }

            SaveDockLayout();
        }
    }
}
