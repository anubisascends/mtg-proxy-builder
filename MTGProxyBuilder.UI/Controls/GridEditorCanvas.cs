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
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _selectedSlots.Count > 0)
            {
                _selectedSlots.Clear();
                SyncSelectedCard();
                _ = RedrawAsync();
                e.Handled = true;
            }
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

        public static readonly DependencyProperty SelectedCardProperty =
            DependencyProperty.Register("SelectedCard", typeof(CardModel), typeof(GridEditorCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty UndoServiceProperty =
            DependencyProperty.Register("UndoSvc", typeof(UndoService), typeof(GridEditorCanvas));

        public UndoService? UndoSvc
        {
            get => (UndoService?)GetValue(UndoServiceProperty);
            set => SetValue(UndoServiceProperty, value);
        }

        /// <summary>Fired when the user double-clicks a card on the canvas.</summary>
        public event Action<CardModel, bool>? CardDoubleClicked; // (card, isShowingBack)

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
                await Task.Run(() =>
                {
                    foreach (var path in pathsToLoad)
                    {
                        if (token.IsCancellationRequested) return;
                        LoadImageToCache(path, (int)cellW * 2);
                    }
                }, token);
            }

            if (token.IsCancellationRequested) return;

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

            for (int page = 0; page < _totalPages; page++)
            {
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
                            PlaceCardVisual(es.Card, x, y, cellW, cellH, bleed, cardW, cardH, pageTop, pageW, pageH, flipped, selected);
                        }
                    }
                }

                var pn = new TextBlock { Text = $"{page + 1}", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), FontWeight = FontWeights.Bold };
                SetLeft(pn, pageW - 30); SetTop(pn, pageTop + pageH - 25); Children.Add(pn);
            }
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

                // Ctrl+Click: toggle slot selection
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (_selectedSlots.Contains(flatSlot))
                        _selectedSlots.Remove(flatSlot);
                    else
                        _selectedSlots.Add(flatSlot);
                    SyncSelectedCard();
                    _ = RedrawAsync();
                    e.Handled = true;
                    return;
                }

                // Start potential drag
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
                    SyncSelectedCard();
                    _ = RedrawAsync();
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
                SyncSelectedCard();
                _ = RedrawAsync();
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
            }

            var flipAllItem = new MenuItem { Header = _allFlipped ? "Unflip All Cards" : "Flip All Cards" };
            flipAllItem.Click += (_, _) => FlipAll();
            menu.Items.Add(flipAllItem);

            if (hasSelection)
            {
                menu.Items.Add(new Separator());
                var clearSel = new MenuItem { Header = "Clear Selection" };
                clearSel.Click += (_, _) => { _selectedSlots.Clear(); SyncSelectedCard(); _ = RedrawAsync(); };
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
            var cards = CardsSource;
            if (cards == null) return;
            UndoSvc?.SaveState(cards);

            // Sort descending so insertions don't shift later indices
            foreach (var idx in cardIndices.OrderByDescending(i => i))
            {
                if (idx < 0 || idx >= cards.Count) continue;
                var original = cards[idx];
                var copy = new CardModel
                {
                    Name = original.Name,
                    ArtworkPath = original.ArtworkPath,
                    BackArtworkPath = original.BackArtworkPath,
                    OriginalBackArtworkPath = original.OriginalBackArtworkPath,
                    ScryfallId = original.ScryfallId,
                    Quantity = original.Quantity,
                    IncludeBack = original.IncludeBack,
                    ManaCost = original.ManaCost,
                    CMC = original.CMC,
                    TypeLine = original.TypeLine,
                    OracleText = original.OracleText,
                    Rarity = original.Rarity,
                    Colors = original.Colors,
                    ColorIdentity = original.ColorIdentity,
                    SetCode = original.SetCode,
                    SetName = original.SetName,
                    CollectorNumber = original.CollectorNumber,
                    Artist = original.Artist,
                    Power = original.Power,
                    Toughness = original.Toughness,
                    Loyalty = original.Loyalty,
                    Keywords = original.Keywords,
                    DateAdded = DateTime.Now
                };
                cards.Insert(idx + 1, copy);
            }
            _selectedSlots.Clear();
            SyncSelectedCard();
        }

        private void DeleteCards(List<int> cardIndices)
        {
            var cards = CardsSource;
            if (cards == null) return;
            UndoSvc?.SaveState(cards);

            foreach (var idx in cardIndices.OrderByDescending(i => i))
            {
                if (idx >= 0 && idx < cards.Count)
                    cards.RemoveAt(idx);
            }
            _selectedSlots.Clear();
            SyncSelectedCard();
        }

        private void FlipCards(List<int> cardIndices)
        {
            foreach (var idx in cardIndices)
            {
                if (_flippedCardIndices.Contains(idx))
                    _flippedCardIndices.Remove(idx);
                else
                    _flippedCardIndices.Add(idx);
            }
            _ = RedrawAsync();
        }

        private void FlipAll()
        {
            _allFlipped = !_allFlipped;
            _flippedCardIndices.Clear();
            _ = RedrawAsync();
        }

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
        //  CARD VISUAL RENDERING
        // ================================================================

        private void PlaceCardVisual(CardModel card, float x, float y,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageTop, float pageW, float pageH, bool flipped, bool selected)
        {
            // Choose front or back image
            bool hasBackArt = !string.IsNullOrEmpty(card.BackArtworkPath);
            bool showNoBackPlaceholder = flipped && !hasBackArt;

            string? imagePath = flipped
                ? (hasBackArt ? card.BackArtworkPath : null)
                : card.ArtworkPath;

            var bmp = showNoBackPlaceholder ? null : GetCachedImage(imagePath);
            if (showNoBackPlaceholder)
            {
                DrawNoBackPlaceholder(x + bleed, y + bleed, cardW, cardH, card.Name);
            }
            else if (bmp != null)
            {
                var image = new Image { Source = bmp, Width = cardW, Height = cardH, Stretch = Stretch.Fill };
                SetLeft(image, x + bleed); SetTop(image, y + bleed); Children.Add(image);

                if (bleed > 0)
                {
                    var overlay = new System.Windows.Shapes.Path
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), IsHitTestVisible = false
                    };
                    overlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                        new RectangleGeometry(new Rect(0, 0, cellW, cellH)),
                        new RectangleGeometry(new Rect(bleed, bleed, cardW, cardH)));
                    SetLeft(overlay, x); SetTop(overlay, y); Children.Add(overlay);
                }
            }
            else
            {
                DrawPlaceholderCard(card, x + bleed, y + bleed, cardW, cardH, flipped);
            }

            // Selection highlight
            if (selected)
            {
                var selRect = new Rectangle
                {
                    Width = cellW, Height = cellH,
                    Fill = Brushes.Transparent,
                    Stroke = Brushes.DodgerBlue, StrokeThickness = 3,
                    RadiusX = 2, RadiusY = 2, IsHitTestVisible = false
                };
                SetLeft(selRect, x); SetTop(selRect, y); Children.Add(selRect);
            }

            // Cut guides
            if (ShowCutGuides)
            {
                float cardLeft = x + bleed, cardTopY = y + bleed;
                float cardRight = cardLeft + cardW, cardBottomY = cardTopY + cardH;
                float pgTop = pageTop, pgBottom = pageTop + pageH;

                var pen = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
                void Line(float x1, float y1, float x2, float y2)
                {
                    var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = pen, StrokeThickness = 0.5, IsHitTestVisible = false };
                    Children.Add(l);
                }
                Line(cardLeft, pgTop, cardLeft, cardTopY);
                Line(cardRight, pgTop, cardRight, cardTopY);
                Line(cardLeft, cardBottomY, cardLeft, pgBottom);
                Line(cardRight, cardBottomY, cardRight, pgBottom);
                Line(0, cardTopY, cardLeft, cardTopY);
                Line(cardRight, cardTopY, pageW, cardTopY);
                Line(0, cardBottomY, cardLeft, cardBottomY);
                Line(cardRight, cardBottomY, pageW, cardBottomY);
            }

        }

        private void DrawNoBackPlaceholder(float x, float y, float w, float h, string cardName)
        {
            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 90, 90, 90)),
                StrokeThickness = 1, RadiusX = 4, RadiusY = 4
            };
            SetLeft(rect, x); SetTop(rect, y); Children.Add(rect);

            var title = new TextBlock
            {
                Text = "No Back Art\nAssigned",
                Foreground = Brushes.White, FontSize = Math.Max(11, w / 12),
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Width = w - 10
            };
            SetLeft(title, x + 5); SetTop(title, y + h / 3 - 10); Children.Add(title);

            var name = new TextBlock
            {
                Text = cardName, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                FontSize = Math.Max(8, w / 18),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Width = w - 10
            };
            SetLeft(name, x + 5); SetTop(name, y + h / 3 + 30); Children.Add(name);
        }

        private void DrawPlaceholderCard(CardModel card, float x, float y, float w, float h, bool flipped)
        {
            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = new SolidColorBrush(flipped ? Color.FromArgb(255, 80, 50, 50) : Color.FromArgb(255, 60, 60, 80)),
                Stroke = Brushes.Gray, StrokeThickness = 1, RadiusX = 4, RadiusY = 4
            };
            SetLeft(rect, x); SetTop(rect, y); Children.Add(rect);

            var nb = new TextBlock
            {
                Text = flipped ? "(Back)\n" + card.Name : (string.IsNullOrEmpty(card.Name) ? "No Image" : card.Name),
                Foreground = Brushes.White, FontSize = Math.Max(10, w / 15),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Width = w - 10
            };
            SetLeft(nb, x + 5); SetTop(nb, y + h / 3); Children.Add(nb);
        }
    }
}
