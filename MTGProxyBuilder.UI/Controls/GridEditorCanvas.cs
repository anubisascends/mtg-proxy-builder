using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Controls
{
    public class GridEditorCanvas : Canvas
    {
        private const float MmToPx = 96f / 25.4f;
        private const float PageGapPx = 30f;

        // Drag state
        private bool _isDragging;
        private int _dragSourceCardIndex = -1;
        private int _pendingSelectSlot = -1;
        private Point _dragStart;
        private UIElement? _dragGhost;
        private Rectangle? _dropHighlight;

        // Selection state — tracks selected CardModel indices in the Cards collection
        private readonly HashSet<int> _selectedSlots = new();
        private int _lastSelectedSlot = -1; // anchor for Shift+Click range selection

        // Flip state — tracks which cards show back artwork
        private readonly HashSet<int> _flippedCardIndices = new();
        private bool _allFlipped;

        // Cached layout info for hit testing
        private float _pageW, _pageH, _cellW, _cellH, _marginL, _marginT;
        private int _cols, _rows, _perPage, _totalPages;
        private List<ExpandedSlot> _expandedSlots = new();

        // Image cache
        private static readonly ConcurrentDictionary<string, BitmapImage?> _imageCache = new();

        // Debounce + async redraw
        private DispatcherTimer? _redrawTimer;
        private CancellationTokenSource? _redrawCts;

        private record ExpandedSlot(CardModel Card, int CardIndex);

        public GridEditorCanvas()
        {
            Background = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
            ClipToBounds = true;

            _redrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _redrawTimer.Tick += (_, _) => { _redrawTimer.Stop(); _ = RedrawAsync(); };

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMoveHandler;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseRightButtonUp += OnMouseRightButtonUp;
            KeyDown += OnKeyDown;
            Focusable = true;

            // Prevent the parent ScrollViewer from scrolling when Focus() is
            // called — this caused the canvas to shift on first click.
            RequestBringIntoView += (_, e) => e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _selectedSlots.Count > 0)
            {
                _selectedSlots.Clear();
                SyncSelectedCard();
                UpdateSelectionOverlays();
                e.Handled = true;
                return;
            }

            // Arrow key navigation
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && _expandedSlots.Count > 0)
            {
                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                int current = _lastSelectedSlot >= 0 && _lastSelectedSlot < _expandedSlots.Count
                    ? _lastSelectedSlot : 0;
                int target;

                if (ctrl)
                {
                    int currentPage = _perPage > 0 ? current / _perPage : 0;
                    target = e.Key switch
                    {
                        Key.Right or Key.Down => Math.Min((currentPage + 1) * _perPage, _expandedSlots.Count - 1),
                        Key.Left or Key.Up => Math.Max((currentPage - 1) * _perPage, 0),
                        _ => current
                    };
                }
                else
                {
                    int delta = e.Key switch
                    {
                        Key.Left => -1,
                        Key.Right => 1,
                        Key.Up => -_cols,
                        Key.Down => _cols,
                        _ => 0
                    };
                    target = Math.Clamp(current + delta, 0, _expandedSlots.Count - 1);
                }

                if (target == current && !shift) { e.Handled = true; return; }

                if (shift)
                {
                    int from = Math.Min(current, target);
                    int to = Math.Max(current, target);
                    for (int i = from; i <= to; i++)
                        _selectedSlots.Add(i);
                }
                else
                {
                    _selectedSlots.Clear();
                    _selectedSlots.Add(target);
                }

                _lastSelectedSlot = target;
                SyncSelectedCard();
                UpdateSelectionOverlays();
                ScrollSlotIntoView(target);
                e.Handled = true;
            }
        }

        /// <summary>Selects all card slots on the canvas.</summary>
        public void SelectAll()
        {
            _selectedSlots.Clear();
            for (int i = 0; i < _expandedSlots.Count; i++)
                _selectedSlots.Add(i);
            if (_expandedSlots.Count > 0)
                _lastSelectedSlot = _expandedSlots.Count - 1;
            SyncSelectedCard();
            UpdateSelectionOverlays();
        }

        /// <summary>Inverts the current selection — selected become deselected and vice versa.</summary>
        public void InvertSelection()
        {
            var newSelection = new HashSet<int>();
            for (int i = 0; i < _expandedSlots.Count; i++)
            {
                if (!_selectedSlots.Contains(i))
                    newSelection.Add(i);
            }
            _selectedSlots.Clear();
            foreach (var i in newSelection)
                _selectedSlots.Add(i);
            SyncSelectedCard();
            UpdateSelectionOverlays();
        }

        /// <summary>Deselects all card slots.</summary>
        public void DeselectAll()
        {
            _selectedSlots.Clear();
            _lastSelectedSlot = -1;
            SyncSelectedCard();
            UpdateSelectionOverlays();
        }

        /// <summary>Returns the card indices of all currently selected slots.</summary>
        public List<int> GetSelectedCardIndices()
        {
            return _selectedSlots
                .Where(s => s >= 0 && s < _expandedSlots.Count)
                .Select(s => _expandedSlots[s].CardIndex)
                .Distinct()
                .ToList();
        }

        // --- Dependency properties ---

        public static readonly DependencyProperty PageSettingsProperty =
            DependencyProperty.Register("PageSettings", typeof(PageLayout), typeof(GridEditorCanvas),
                new PropertyMetadata(null, OnSettingsChanged));

        public static readonly DependencyProperty CardsSourceProperty =
            DependencyProperty.Register("CardsSource", typeof(ObservableCollection<CardModel>), typeof(GridEditorCanvas),
                new PropertyMetadata(null, OnCardsSourceChanged));

        public static readonly DependencyProperty ShowCutGuidesProperty =
            DependencyProperty.Register("ShowCutGuides", typeof(bool), typeof(GridEditorCanvas),
                new PropertyMetadata(true, (d, _) => ((GridEditorCanvas)d).ScheduleRedraw()));

        public static readonly DependencyProperty PrintSettingsSourceProperty =
            DependencyProperty.Register("PrintSettingsSource", typeof(PrintSettings), typeof(GridEditorCanvas),
                new PropertyMetadata(null, OnPrintSettingsChanged));

        public static readonly DependencyProperty RenderProgressProperty =
            DependencyProperty.Register("RenderProgress", typeof(string), typeof(GridEditorCanvas),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsRenderingProperty =
            DependencyProperty.Register("IsRendering", typeof(bool), typeof(GridEditorCanvas),
                new PropertyMetadata(false));

        public string? RenderProgress
        {
            get => (string?)GetValue(RenderProgressProperty);
            set => SetValue(RenderProgressProperty, value);
        }

        public bool IsRendering
        {
            get => (bool)GetValue(IsRenderingProperty);
            set => SetValue(IsRenderingProperty, value);
        }

        public PrintSettings? PrintSettingsSource
        {
            get => (PrintSettings?)GetValue(PrintSettingsSourceProperty);
            set => SetValue(PrintSettingsSourceProperty, value);
        }

        private static void OnPrintSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GridEditorCanvas canvas)
            {
                if (e.OldValue is PrintSettings old) old.PropertyChanged -= canvas.OnPrintSettingsPropChanged;
                if (e.NewValue is PrintSettings nw) nw.PropertyChanged += canvas.OnPrintSettingsPropChanged;
                canvas.ScheduleRedraw();
            }
        }

        private void OnPrintSettingsPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleRedraw();

        public static readonly DependencyProperty SelectedCardProperty =
            DependencyProperty.Register("SelectedCard", typeof(CardModel), typeof(GridEditorCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty RefreshTriggerProperty =
            DependencyProperty.Register("RefreshTrigger", typeof(int), typeof(GridEditorCanvas),
                new PropertyMetadata(0, (d, _) => ((GridEditorCanvas)d).ScheduleRedraw()));

        public int RefreshTrigger
        {
            get => (int)GetValue(RefreshTriggerProperty);
            set => SetValue(RefreshTriggerProperty, value);
        }

        public static readonly DependencyProperty UndoServiceProperty =
            DependencyProperty.Register("UndoSvc", typeof(UndoService), typeof(GridEditorCanvas));

        public UndoService? UndoSvc
        {
            get => (UndoService?)GetValue(UndoServiceProperty);
            set => SetValue(UndoServiceProperty, value);
        }

        /// <summary>Fired when the user double-clicks a card on the canvas.</summary>
        public event Action<CardModel, bool>? CardDoubleClicked; // (card, isShowingBack)
        public event Action<CardModel>? CreateTokenRequested; // (sourceCard)
        public event Action<List<CardModel>>? CreateTokensFromCardsRequested; // (sourceCards)
        public event Action<List<int>>? ApplyMajorityBackRequested; // (cardIndices)
        public event Action<List<int>>? SelectFrontArtRequested; // (cardIndices)
        public event Action<List<int>>? SelectBackArtRequested; // (cardIndices)
        public event Action<int, bool>? CardFlipStateChanged; // (cardIndex, isShowingBack)

        public PageLayout? PageSettings
        {
            get => (PageLayout?)GetValue(PageSettingsProperty);
            set => SetValue(PageSettingsProperty, value);
        }

        public ObservableCollection<CardModel>? CardsSource
        {
            get => (ObservableCollection<CardModel>?)GetValue(CardsSourceProperty);
            set => SetValue(CardsSourceProperty, value);
        }

        public bool ShowCutGuides
        {
            get => (bool)GetValue(ShowCutGuidesProperty);
            set => SetValue(ShowCutGuidesProperty, value);
        }

        public CardModel? SelectedCard
        {
            get => (CardModel?)GetValue(SelectedCardProperty);
            set => SetValue(SelectedCardProperty, value);
        }

        private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GridEditorCanvas canvas)
            {
                if (e.OldValue is PageLayout old) old.PropertyChanged -= canvas.OnSettingsPropChanged;
                if (e.NewValue is PageLayout nw) nw.PropertyChanged += canvas.OnSettingsPropChanged;
                canvas.ScheduleRedraw();
            }
        }

        private void OnSettingsPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => ScheduleRedraw();

        private static void OnCardsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GridEditorCanvas canvas)
            {
                if (e.OldValue is ObservableCollection<CardModel> oldC) oldC.CollectionChanged -= canvas.OnCollectionChanged;
                if (e.NewValue is ObservableCollection<CardModel> newC) newC.CollectionChanged += canvas.OnCollectionChanged;
                canvas._selectedSlots.Clear();
                canvas.ScheduleRedraw();
            }
        }

        private void OnCollectionChanged(object? s,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => ScheduleRedraw();

        private void ScheduleRedraw()
        {
            if (_redrawTimer == null) return;
            _redrawTimer.Stop();
            _redrawTimer.Start();
        }

        // ================================================================
        //  ASYNC RENDERING
        // ================================================================

        private async Task RedrawAsync()
        {
            _redrawCts?.Cancel();
            _redrawCts = new CancellationTokenSource();
            var token = _redrawCts.Token;

            var settings = PageSettings;
            if (settings == null) { Children.Clear(); return; }

            float pageW = settings.PageWidthMm * MmToPx;
            float pageH = settings.PageHeightMm * MmToPx;
            float marginL = settings.MarginLeftMm * MmToPx;
            float marginT = settings.MarginTopMm * MmToPx;
            float marginR = settings.MarginRightMm * MmToPx;
            float marginB = settings.MarginBottomMm * MmToPx;
            float cellW = (settings.CardWidthMm + 2 * settings.BleedWidthMm) * MmToPx;
            float cellH = (settings.CardHeightMm + 2 * settings.BleedWidthMm) * MmToPx;
            float bleed = settings.BleedWidthMm * MmToPx;
            float cardW = settings.CardWidthMm * MmToPx;
            float cardH = settings.CardHeightMm * MmToPx;
            int cols = settings.CardsPerRow;
            int rows = settings.CardsPerColumn;
            int perPage = settings.CardsPerPage;

            if (cols <= 0 || rows <= 0 || perPage <= 0) { Children.Clear(); return; }

            var slots = new List<ExpandedSlot>();
            var cards = CardsSource;
            if (cards != null)
                for (int i = 0; i < cards.Count; i++)
                    for (int q = 0; q < cards[i].Quantity; q++)
                        slots.Add(new ExpandedSlot(cards[i], i));

            // Collect image paths to preload (include back images for flipped cards)
            var pathsToLoad = new HashSet<string>();
            foreach (var s in slots)
            {
                bool showBack = IsCardFlipped(s.CardIndex);
                string? path = showBack ? (s.Card.BackArtworkPath ?? s.Card.ArtworkPath) : s.Card.ArtworkPath;
                if (!string.IsNullOrEmpty(path) && !_imageCache.ContainsKey(path))
                    pathsToLoad.Add(path);
            }

            if (pathsToLoad.Count > 0)
            {
                IsRendering = true;
                int loaded = 0;
                int total = pathsToLoad.Count;
                foreach (var path in pathsToLoad)
                {
                    if (token.IsCancellationRequested) { IsRendering = false; return; }
                    await Task.Run(() => LoadImageToCache(path, (int)cellW * 2), token);
                    loaded++;
                    RenderProgress = $"Loading images ({loaded}/{total})...";
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                }
            }

            if (token.IsCancellationRequested) { IsRendering = false; return; }

            _pageW = pageW; _pageH = pageH; _cellW = cellW; _cellH = cellH;
            _marginL = marginL; _marginT = marginT;
            _cols = cols; _rows = rows; _perPage = perPage;
            _expandedSlots = slots;

            int totalSlots = slots.Count;
            _totalPages = totalSlots > 0 ? (int)Math.Ceiling((double)totalSlots / perPage) : 1;
            if (_totalPages < 1) _totalPages = 1;

            Children.Clear();
            _isDragging = false; _dragGhost = null; _dropHighlight = null;

            Width = pageW;
            Height = _totalPages * pageH + (_totalPages - 1) * PageGapPx;

            IsRendering = true;
            for (int page = 0; page < _totalPages; page++)
            {
                if (token.IsCancellationRequested) { IsRendering = false; return; }
                RenderProgress = _totalPages > 1
                    ? $"Rendering page {page + 1} of {_totalPages}..."
                    : "Rendering page...";
                float pageTop = page * (pageH + PageGapPx);

                if (page > 0)
                {
                    var lbl = new TextBlock { Text = $"Page {page + 1} of {_totalPages}", FontSize = 12, Foreground = Brushes.Gray, FontWeight = FontWeights.SemiBold };
                    SetLeft(lbl, 4); SetTop(lbl, pageTop - 18); Children.Add(lbl);
                }

                var bg = new Rectangle { Width = pageW, Height = pageH, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 0.5 };
                SetLeft(bg, 0); SetTop(bg, pageTop); Children.Add(bg);

                var mr = new Rectangle
                {
                    Width = pageW - marginL - marginR, Height = pageH - marginT - marginB,
                    Stroke = Brushes.LightBlue, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                SetLeft(mr, marginL); SetTop(mr, pageTop + marginT); Children.Add(mr);

                int pageStart = page * perPage;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int flat = pageStart + r * cols + c;
                        float x = marginL + c * cellW;
                        float y = pageTop + marginT + r * cellH;

                        var slot = new Rectangle
                        {
                            Width = cellW, Height = cellH,
                            Fill = new SolidColorBrush(Color.FromArgb(15, 0, 0, 0)),
                            Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                            StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 2, 2 }
                        };
                        SetLeft(slot, x); SetTop(slot, y); Children.Add(slot);

                        if (flat < slots.Count)
                        {
                            var es = slots[flat];
                            bool flipped = IsCardFlipped(es.CardIndex);
                            bool selected = _selectedSlots.Contains(flat);
                            PlaceCardVisual(es.Card, es.CardIndex, x, y, cellW, cellH, bleed, cardW, cardH, pageTop, pageW, pageH, flipped, selected);
                        }
                    }
                }

                var pn = new TextBlock { Text = $"{page + 1}", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), FontWeight = FontWeights.Bold };
                SetLeft(pn, pageW - 30); SetTop(pn, pageTop + pageH - 25); Children.Add(pn);

                // Registration marks
                var regPs = PrintSettingsSource;
                if (regPs != null && regPs.ShowRegistrationMarks)
                {
                    DrawRegistrationMarksPreview(pageTop, pageW, pageH, regPs);
                }

                // Yield to UI thread between pages so the progress overlay updates
                if (_totalPages > 1)
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            }

            IsRendering = false;
            RenderProgress = null;

            // Re-apply selection overlays after full render
            UpdateSelectionOverlays();
        }

        private const float InToPx = 96f; // 1 inch = 96 WPF pixels

        private void DrawRegistrationMarksPreview(float pageTop, float pageW, float pageH, PrintSettings ps)
        {
            float inset = ps.RegMarkInsetIn * InToPx;
            float length = ps.RegMarkLengthIn * InToPx;
            float thickness = ps.RegMarkThicknessIn * InToPx;
            var brush = Brushes.Black;

            void AddMark(float rx, float ry, float rw, float rh)
            {
                var r = new Rectangle { Width = rw, Height = rh, Fill = brush, IsHitTestVisible = false };
                SetLeft(r, rx); SetTop(r, ry); Children.Add(r);
            }

            // Top-left: filled square
            AddMark(inset, pageTop + inset, length, length);

            // Top-right L: horizontal bar left + vertical bar down
            AddMark(pageW - inset - length, pageTop + inset, length, thickness);
            AddMark(pageW - inset - thickness, pageTop + inset + thickness, thickness, length - thickness);

            // Bottom-left L: vertical bar up + horizontal bar right
            AddMark(inset, pageTop + pageH - inset - length, thickness, length - thickness);
            AddMark(inset, pageTop + pageH - inset - thickness, length, thickness);
        }

        private bool IsCardFlipped(int cardIndex) => _allFlipped ^ _flippedCardIndices.Contains(cardIndex);

        // ================================================================
        //  IMAGE CACHE
        // ================================================================

        private static void LoadImageToCache(string path, int decodeWidth)
        {
            if (_imageCache.ContainsKey(path)) return;
            try
            {
                if (!File.Exists(path)) { _imageCache[path] = null; return; }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                _imageCache[path] = bmp;
            }
            catch { _imageCache[path] = null; }
        }

        private static BitmapImage? GetCachedImage(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return _imageCache.TryGetValue(path, out var bmp) ? bmp : null;
        }

        // ================================================================
        //  MOUSE — left click = select/drag, right click = context menu
        // ================================================================

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            var pos = e.GetPosition(this);
            int flatSlot = HitTestSlot(pos);

            if (flatSlot >= 0 && flatSlot < _expandedSlots.Count)
            {
                int cardIdx = _expandedSlots[flatSlot].CardIndex;

                // Double-click: open art selector
                if (e.ClickCount == 2)
                {
                    var card = _expandedSlots[flatSlot].Card;
                    bool showingBack = IsCardFlipped(cardIdx);
                    CardDoubleClicked?.Invoke(card, showingBack);
                    e.Handled = true;
                    return;
                }

                // Ctrl+Click: toggle individual slot in selection
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (_selectedSlots.Contains(flatSlot))
                        _selectedSlots.Remove(flatSlot);
                    else
                        _selectedSlots.Add(flatSlot);
                    _lastSelectedSlot = flatSlot;
                    SyncSelectedCard();
                    UpdateSelectionOverlays();
                    e.Handled = true;
                    return;
                }

                // Shift+Click: range select from last selected to current
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _lastSelectedSlot >= 0)
                {
                    int from = Math.Min(_lastSelectedSlot, flatSlot);
                    int to = Math.Max(_lastSelectedSlot, flatSlot);
                    for (int s = from; s <= to; s++)
                    {
                        if (s < _expandedSlots.Count)
                            _selectedSlots.Add(s);
                    }
                    SyncSelectedCard();
                    UpdateSelectionOverlays();
                    e.Handled = true;
                    return;
                }

                // Start potential drag (plain click selects on mouse-up)
                _dragSourceCardIndex = cardIdx;
                _dragStart = pos;
                _isDragging = false;
                _pendingSelectSlot = flatSlot;
                CaptureMouse();
            }
            else
            {
                // Click empty area: clear selection
                if (_selectedSlots.Count > 0)
                {
                    _selectedSlots.Clear();
                    _lastSelectedSlot = -1;
                    SyncSelectedCard();
                    UpdateSelectionOverlays();
                }
            }
            e.Handled = true;
        }

        private void OnMouseMoveHandler(object sender, MouseEventArgs e)
        {
            if (_dragSourceCardIndex < 0 || !IsMouseCaptured) return;
            var pos = e.GetPosition(this);
            var delta = pos - _dragStart;
            if (!_isDragging)
            {
                if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;
                _isDragging = true;
                CreateDragGhost();
            }
            if (_dragGhost != null) { SetLeft(_dragGhost, pos.X - _cellW / 2); SetTop(_dragGhost, pos.Y - _cellH / 2); }
            UpdateDropHighlight(pos);
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) { ReleaseMouseCapture(); return; }
            ReleaseMouseCapture();

            if (_isDragging && _dragSourceCardIndex >= 0)
            {
                PerformDrop(HitTestSlot(e.GetPosition(this)));
            }
            else if (_pendingSelectSlot >= 0 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Simple click without drag — single select this slot
                _selectedSlots.Clear();
                _selectedSlots.Add(_pendingSelectSlot);
                _lastSelectedSlot = _pendingSelectSlot;
                SyncSelectedCard();
                UpdateSelectionOverlays();
            }

            if (_dragGhost != null) Children.Remove(_dragGhost);
            if (_dropHighlight != null) Children.Remove(_dropHighlight);
            _dragGhost = null; _dropHighlight = null;
            _isDragging = false; _dragSourceCardIndex = -1; _pendingSelectSlot = -1;
        }

        // ================================================================
        //  RIGHT-CLICK CONTEXT MENU
        // ================================================================

        /// <summary>Convert selected flat slots to unique card indices.</summary>
        private List<int> SelectedCardIndices()
        {
            return _selectedSlots
                .Where(s => s >= 0 && s < _expandedSlots.Count)
                .Select(s => _expandedSlots[s].CardIndex)
                .Distinct()
                .ToList();
        }

        private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            int flatSlot = HitTestSlot(pos);
            int hoverCardIdx = (flatSlot >= 0 && flatSlot < _expandedSlots.Count) ? _expandedSlots[flatSlot].CardIndex : -1;

            var menu = new ContextMenu();

            bool hasSelection = _selectedSlots.Count > 0;
            bool hasHover = hoverCardIdx >= 0;
            string target = hasSelection ? $" ({_selectedSlots.Count} selected)" : "";

            if (hasHover || hasSelection)
            {
                var cardIndices = hasSelection ? SelectedCardIndices() : new List<int> { hoverCardIdx };

                var dupItem = new MenuItem { Header = hasSelection ? $"Duplicate Selected{target}" : "Duplicate Card" };
                dupItem.Click += (_, _) => DuplicateCards(cardIndices);
                menu.Items.Add(dupItem);

                var delItem = new MenuItem { Header = hasSelection ? $"Delete Selected{target}" : "Delete Card" };
                delItem.Click += (_, _) => DeleteCards(cardIndices);
                menu.Items.Add(delItem);

                menu.Items.Add(new Separator());

                var flipItem = new MenuItem { Header = hasSelection ? $"Flip Selected{target}" : "Flip Card" };
                flipItem.Click += (_, _) => FlipCards(cardIndices);
                menu.Items.Add(flipItem);

                // "Match Back Art" — apply the most common back art to selected/hovered cards
                var matchBackItem = new MenuItem { Header = hasSelection ? $"Match Back Art{target}" : "Match Back Art" };
                matchBackItem.Click += (_, _) => ApplyMajorityBackRequested?.Invoke(cardIndices);
                menu.Items.Add(matchBackItem);

                menu.Items.Add(new Separator());

                // Art selection
                var selectFrontItem = new MenuItem { Header = hasSelection ? $"Select Front Art{target}" : "Select Front Art..." };
                selectFrontItem.Click += (_, _) => SelectFrontArtRequested?.Invoke(cardIndices);
                menu.Items.Add(selectFrontItem);

                var selectBackItem = new MenuItem { Header = hasSelection ? $"Select Card Back{target}" : "Select Card Back..." };
                selectBackItem.Click += (_, _) => SelectBackArtRequested?.Invoke(cardIndices);
                menu.Items.Add(selectBackItem);

                // "Create Token" — for cards with unique (non-common) back art
                menu.Items.Add(new Separator());
                if (hasSelection)
                {
                    var tokenItem = new MenuItem { Header = $"Create Token(s) from Selected{target}" };
                    tokenItem.Click += (_, _) => CreateTokensFromCardsRequested?.Invoke(
                        cardIndices.Where(i => i >= 0 && i < CardsSource!.Count).Select(i => CardsSource![i]).ToList());
                    menu.Items.Add(tokenItem);
                }
                else if (hasHover)
                {
                    var hoverCard = CardsSource![hoverCardIdx];
                    var tokenItem = new MenuItem { Header = "Create Token Card" };
                    tokenItem.Click += (_, _) => CreateTokenRequested?.Invoke(hoverCard);
                    menu.Items.Add(tokenItem);
                }
            }

            var flipAllItem = new MenuItem { Header = _allFlipped ? "Unflip All Cards" : "Flip All Cards" };
            flipAllItem.Click += (_, _) => FlipAll();
            menu.Items.Add(flipAllItem);

            if (hasSelection)
            {
                menu.Items.Add(new Separator());
                var clearSel = new MenuItem { Header = "Clear Selection" };
                clearSel.Click += (_, _) => { _selectedSlots.Clear(); SyncSelectedCard(); UpdateSelectionOverlays(); };
                menu.Items.Add(clearSel);
            }

            menu.IsOpen = true;
            e.Handled = true;
        }

        // ================================================================
        //  CARD OPERATIONS — duplicate, delete, flip
        // ================================================================

        private void DuplicateCards(List<int> cardIndices)
        {
            if (CardsSource == null) return;
            CanvasOperations.DuplicateCards(CardsSource, cardIndices, UndoSvc);
            _selectedSlots.Clear();
            SyncSelectedCard();
        }

        private void DeleteCards(List<int> cardIndices)
        {
            if (CardsSource == null) return;
            CanvasOperations.DeleteCards(CardsSource, cardIndices, UndoSvc);
            _selectedSlots.Clear();
            SyncSelectedCard();
        }

        private void FlipButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int cardIndex)
            {
                FlipCards(new List<int> { cardIndex });
                e.Handled = true;
            }
        }

        private void FlipCards(List<int> cardIndices)
        {
            CanvasOperations.FlipCards(_flippedCardIndices, cardIndices);
            _ = RedrawAsync();
            foreach (var idx in cardIndices)
                CardFlipStateChanged?.Invoke(idx, IsCardFlipped(idx));
        }

        private void FlipAll()
        {
            CanvasOperations.FlipAll(ref _allFlipped, _flippedCardIndices);
            _ = RedrawAsync();
        }

        /// <summary>Returns whether a card at the given index is currently showing its back face.</summary>
        public bool IsShowingBack(int cardIndex) => IsCardFlipped(cardIndex);

        // ================================================================
        //  DRAG AND DROP
        // ================================================================

        private void CreateDragGhost()
        {
            if (CardsSource == null || _dragSourceCardIndex < 0) return;
            var card = CardsSource[_dragSourceCardIndex];
            var ghost = new Border
            {
                Width = _cellW, Height = _cellH, Opacity = 0.7,
                Background = new SolidColorBrush(Color.FromArgb(180, 70, 130, 200)),
                BorderBrush = Brushes.DodgerBlue, BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4), IsHitTestVisible = false
            };
            var bmp = GetCachedImage(card.ArtworkPath);
            if (bmp != null) ghost.Background = new ImageBrush(bmp) { Stretch = Stretch.Fill, Opacity = 0.8 };
            _dragGhost = ghost; Children.Add(_dragGhost);
        }

        private void SyncSelectedCard()
        {
            if (_selectedSlots.Count == 0 || _expandedSlots.Count == 0)
            {
                SelectedCard = null;
                return;
            }
            int slot = _selectedSlots.First();
            SelectedCard = (slot >= 0 && slot < _expandedSlots.Count) ? _expandedSlots[slot].Card : null;
        }

        private void UpdateDropHighlight(Point pos)
        {
            if (_dropHighlight != null) Children.Remove(_dropHighlight);
            int slot = HitTestSlot(pos);
            if (slot < 0) return;
            var (x, y) = SlotToPosition(slot);
            _dropHighlight = new Rectangle
            {
                Width = _cellW, Height = _cellH,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 120, 255)),
                Stroke = Brushes.DodgerBlue, StrokeThickness = 2,
                RadiusX = 3, RadiusY = 3, IsHitTestVisible = false
            };
            SetLeft(_dropHighlight, x); SetTop(_dropHighlight, y); Children.Add(_dropHighlight);
        }

        private void PerformDrop(int targetFlatSlot)
        {
            var cards = CardsSource;
            if (cards == null || _dragSourceCardIndex < 0) return;
            UndoSvc?.SaveState(cards);
            int targetCardIndex;
            if (targetFlatSlot >= 0 && targetFlatSlot < _expandedSlots.Count)
                targetCardIndex = _expandedSlots[targetFlatSlot].CardIndex;
            else
                targetCardIndex = cards.Count - 1;
            if (targetCardIndex == _dragSourceCardIndex) return;
            cards.Move(_dragSourceCardIndex, targetCardIndex);
        }

        // ================================================================
        //  HIT TESTING
        // ================================================================

        private int HitTestSlot(Point pos)
        {
            if (_perPage <= 0 || _cols <= 0 || _rows <= 0) return -1;
            float pageStride = _pageH + PageGapPx;
            int page = Math.Clamp((int)(pos.Y / pageStride), 0, _totalPages - 1);
            float pageTop = page * pageStride;
            float localY = (float)pos.Y - pageTop;
            float localX = (float)pos.X;
            if (localY < _marginT || localY >= _marginT + _rows * _cellH) return -1;
            if (localX < _marginL || localX >= _marginL + _cols * _cellW) return -1;
            int col = (int)((localX - _marginL) / _cellW);
            int row = (int)((localY - _marginT) / _cellH);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return -1;
            return page * _perPage + row * _cols + col;
        }

        private (float x, float y) SlotToPosition(int flatSlot)
        {
            int page = flatSlot / _perPage;
            int slotOnPage = flatSlot % _perPage;
            float pageTop = page * (_pageH + PageGapPx);
            return (_marginL + (slotOnPage % _cols) * _cellW, pageTop + _marginT + (slotOnPage / _cols) * _cellH);
        }

        // ================================================================
        //  SELECTION OVERLAYS (lightweight, no re-render)
        // ================================================================

        private const string SelectionOverlayTag = "__sel__";

        /// <summary>Updates only the selection highlight rectangles without re-rendering card images.</summary>
        private void UpdateSelectionOverlays()
        {
            // Remove existing selection overlays
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] is Rectangle r && Equals(r.Tag, SelectionOverlayTag))
                    Children.RemoveAt(i);
            }

            if (_cellW <= 0 || _cellH <= 0) return;

            // Add new selection overlays
            foreach (int slot in _selectedSlots)
            {
                if (slot < 0 || slot >= _expandedSlots.Count) continue;
                var (x, y) = SlotToPosition(slot);
                var selRect = new Rectangle
                {
                    Width = _cellW, Height = _cellH,
                    Fill = Brushes.Transparent,
                    Stroke = Brushes.DodgerBlue, StrokeThickness = 3,
                    RadiusX = 2, RadiusY = 2, IsHitTestVisible = false,
                    Tag = SelectionOverlayTag
                };
                SetLeft(selRect, x); SetTop(selRect, y);
                Children.Add(selRect);
            }
        }

        /// <summary>Scrolls the parent ScrollViewer so that the given slot is visible.</summary>
        private void ScrollSlotIntoView(int slot)
        {
            if (slot < 0 || slot >= _expandedSlots.Count) return;
            var (x, y) = SlotToPosition(slot);

            if (Parent is FrameworkElement fe)
            {
                // Walk up to find the ScrollViewer
                DependencyObject current = fe;
                while (current != null)
                {
                    if (current is ScrollViewer sv)
                    {
                        // Ensure the slot's vertical position is visible
                        double slotBottom = y + _cellH;
                        if (y < sv.VerticalOffset)
                            sv.ScrollToVerticalOffset(y - 20);
                        else if (slotBottom > sv.VerticalOffset + sv.ViewportHeight)
                            sv.ScrollToVerticalOffset(slotBottom - sv.ViewportHeight + 20);
                        break;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
            }
        }

        // ================================================================
        //  CARD VISUAL RENDERING
        // ================================================================

        private void PlaceCardVisual(CardModel card, int cardIndex, float x, float y,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageTop, float pageW, float pageH, bool flipped, bool selected)
        {
            bool hasBackArt = !string.IsNullOrEmpty(card.BackArtworkPath);
            bool showNoBackPlaceholder = flipped && !hasBackArt;
            string? imagePath = flipped ? (hasBackArt ? card.BackArtworkPath : null) : card.ArtworkPath;
            var bmp = showNoBackPlaceholder ? null : GetCachedImage(imagePath);

            CardVisualRenderer.PlaceCard(this, card, bmp,
                x, y, cellW, cellH, bleed, cardW, cardH,
                pageTop, pageW, pageH, flipped, selected,
                ShowCutGuides, PrintSettingsSource);

            // Flip button overlay
            float btnSize = Math.Max(27, cardW * 0.135f);
            float btnMargin = 4f;
            var flipBtn = new Button
            {
                Width = btnSize, Height = btnSize,
                Content = "\u21BB", // ↻
                FontSize = btnSize * 0.6,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(200, 80, 80, 80)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = flipped ? "Show Front" : "Show Back",
                Tag = cardIndex
            };
            flipBtn.Click += FlipButton_Click;
            SetLeft(flipBtn, x + bleed + cardW - btnSize - btnMargin);
            SetTop(flipBtn, y + bleed + btnMargin);
            Children.Add(flipBtn);
        }
    }
}
