using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Controls;
using MTGProxyBuilder.UI.Services;
using Serilog;

namespace MTGProxyBuilder.UI.Dialogs
{
    public enum ArtSelectorMode { Front, Back }

    public partial class ArtSelectorDialog : Window
    {
        private readonly CardModel _card;
        private readonly ScryfallService _scryfall;
        private readonly MpcFillService _mpcFill;
        private readonly ImageCacheService _imageCache;
        private readonly BackArtLibraryService? _backLibrary;
        private readonly IList<CardModel>? _allCards;
        private readonly object[][]? _mpcSourcesOverride;
        private readonly FrontArtLibraryService? _frontArtLibrary;
        private readonly ThumbnailService? _frontThumbnails;
        private readonly ThumbnailService? _backThumbnails;
        private MpcFillSearchOptions _mpcSearchOptions;

        public string? ResultPath { get; private set; }
        public ArtSelectorMode ResultMode { get; private set; }

        /// <summary>When true, the result should be applied to all cards with matching name.</summary>
        public bool ApplyToSameName { get; private set; }

        /// <summary>When true, the result should be applied to all cards without back art.</summary>
        public bool ApplyToNoBack { get; private set; }

        // Tile tracking for search/filter
        private record TileInfo(Border Tile, string Name, string Source, string Detail, bool IsAction = false);

        // Per-tab state
        private class TabState
        {
            public required ArtSelectorMode Mode { get; init; }
            public required WrapPanel OptionsPanel { get; init; }
            public required SearchBar SearchBar { get; init; }
            public List<TileInfo> AllTiles { get; } = new();
            public Dictionary<string, ScryfallCard> ScryfallCardsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, MpcFillCard> MpcFillCardsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? ResultPath { get; set; }
            public string? SelectedLabel { get; set; }
            public string? SelectedDetail { get; set; }
            public Border? SelectedBorder { get; set; }
            public bool IsLoaded { get; set; }
        }

        private readonly TabState _frontTab;
        private readonly TabState _backTab;
        private TabState _activeTab;
        private bool _initializing = true;

        // Cached checkbox content (set once in constructor)
        private readonly string? _sameNameContent;
        private readonly string? _noBackContent;

        public ArtSelectorDialog(
            CardModel card,
            ArtSelectorMode initialMode,
            ScryfallService scryfall,
            MpcFillService mpcFill,
            ImageCacheService imageCache,
            BackArtLibraryService? backLibrary = null,
            IList<CardModel>? allCards = null,
            object[][]? mpcSourcesOverride = null,
            MpcFillSearchOptions? mpcSearchOptions = null,
            FrontArtLibraryService? frontArtLibrary = null)
        {
            InitializeComponent();
            _card = card;
            _scryfall = scryfall;
            _mpcFill = mpcFill;
            _imageCache = imageCache;
            _backLibrary = backLibrary;
            _allCards = allCards;
            _mpcSourcesOverride = mpcSourcesOverride;
            _mpcSearchOptions = mpcSearchOptions ?? new MpcFillSearchOptions();
            _frontArtLibrary = frontArtLibrary;
            _frontThumbnails = frontArtLibrary != null ? new ThumbnailService(frontArtLibrary.LibraryDirectory) : null;
            _backThumbnails = backLibrary != null ? new ThumbnailService(backLibrary.LibraryDirectory) : null;

            TitleLabel.Text = "Select Artwork";
            Log.Information("Art selector dialog opened for {CardName} ({Mode})", card.Name, initialMode);
            CardNameLabel.Text = $"for: {card.Name}";

            // Initialize tab state
            _frontTab = new TabState
            {
                Mode = ArtSelectorMode.Front,
                OptionsPanel = FrontOptionsPanel,
                SearchBar = FrontSearchBar
            };
            _backTab = new TabState
            {
                Mode = ArtSelectorMode.Back,
                OptionsPanel = BackOptionsPanel,
                SearchBar = BackSearchBar
            };
            _activeTab = initialMode == ArtSelectorMode.Front ? _frontTab : _backTab;

            // Wire search bars
            FrontSearchBar.SearchRequested += (_, _) => ApplyFilters(_frontTab);
            FrontSearchBar.SourceChanged += (_, _) => ApplyFilters(_frontTab);
            BackSearchBar.SearchRequested += (_, _) => ApplyFilters(_backTab);
            BackSearchBar.SourceChanged += (_, _) => ApplyFilters(_backTab);

            // Set up bulk action checkbox content
            if (_allCards != null)
            {
                int sameNameCount = _allCards.Count(c => c.Name == card.Name);
                if (sameNameCount > 1)
                {
                    _sameNameContent = $"Apply to all \"{card.Name}\" ({sameNameCount} cards)";
                    ApplySameNameChk.Content = _sameNameContent;
                }

                int noBackCount = _allCards.Count(c => string.IsNullOrEmpty(c.BackArtworkPath));
                if (noBackCount > 0)
                {
                    _noBackContent = $"Apply to all without back art ({noBackCount} cards)";
                    ApplyNoBackChk.Content = _noBackContent;
                }
            }

            // Show front actions bar if library available
            if (_frontArtLibrary != null)
                FrontActionsBar.Visibility = Visibility.Visible;

            LoadFilterControls(_mpcSearchOptions);

            // Set initial tab
            ArtTabControl.SelectedIndex = initialMode == ArtSelectorMode.Front ? 0 : 1;
            UpdateFooterForActiveTab();

            Loaded += async (_, _) =>
            {
                _initializing = false;
                _activeTab.IsLoaded = true;
                await LoadTabContentAsync(_activeTab);
            };
        }

        // ================================================================
        //  TAB SWITCHING
        // ================================================================

        private async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing || ArtTabControl == null) return;

            var newTab = ArtTabControl.SelectedIndex == 0 ? _frontTab : _backTab;
            if (newTab == _activeTab) return;
            _activeTab = newTab;

            if (!newTab.IsLoaded)
            {
                newTab.IsLoaded = true;
                SpinnerDot.Visibility = Visibility.Visible;
                StatusLabel.Text = "Loading...";
                await LoadTabContentAsync(newTab);
            }

            // Restore preview state for this tab
            if (newTab.ResultPath != null)
            {
                PreviewPanel.ShowImage(newTab.ResultPath, newTab.SelectedLabel ?? "", newTab.SelectedDetail);
                OkBtn.IsEnabled = true;
            }
            else
            {
                PreviewPanel.Clear();
                OkBtn.IsEnabled = false;
            }

            UpdateFooterForActiveTab();
        }

        private void UpdateFooterForActiveTab()
        {
            bool isFront = _activeTab.Mode == ArtSelectorMode.Front;
            ApplySameNameChk.Visibility = isFront && _sameNameContent != null
                ? Visibility.Visible : Visibility.Collapsed;
            ApplyNoBackChk.Visibility = !isFront && _noBackContent != null
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================================================================
        //  TAB CONTENT LOADING
        // ================================================================

        private async Task LoadTabContentAsync(TabState tab)
        {
            tab.OptionsPanel.Children.Clear();
            tab.AllTiles.Clear();
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Current artwork for this mode
            string? currentPath = tab.Mode == ArtSelectorMode.Front ? _card.ArtworkPath : _card.BackArtworkPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                AddOption(tab, "Current", currentPath, true, "Currently assigned");
                shown.Add(currentPath);
            }

            if (tab.Mode == ArtSelectorMode.Front)
                await LoadFrontOptions(tab, shown);
            else
                await LoadBackOptionsAsync(tab, shown);

            StatusLabel.Text = $"{shown.Count} option(s) found";
            SpinnerDot.Visibility = Visibility.Collapsed;
            PopulateSourceFilter(tab);
            ApplyFilters(tab);
        }

        private async Task LoadFrontOptions(TabState tab, HashSet<string> shown)
        {
            if (string.IsNullOrEmpty(_card.Name))
            {
                AddActionTile(tab, "Browse File...", OnBrowseFile);
                return;
            }

            // 1. Show local library matches first (instant, no network)
            var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_frontArtLibrary != null)
            {
                var libraryMatches = _frontArtLibrary.SearchByCardName(_card.Name);
                if (libraryMatches.Count > 0)
                {
                    StatusLabel.Text = $"Found {libraryMatches.Count} in library, searching online...";
                    foreach (var m in libraryMatches)
                        libraryNames.Add(m.Name);
                    var deferredImages = new List<(Image img, string entryId, string path)>();
                    foreach (var entry in libraryMatches)
                    {
                        if (shown.Contains(entry.FilePath)) continue;
                        shown.Add(entry.FilePath);

                        var border = new Border
                        {
                            Width = 110, Height = 165, Margin = new Thickness(4),
                            Background = AppBrushes.TileBg,
                            CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                            BorderThickness = new Thickness(2),
                            BorderBrush = AppBrushes.AccentGreen,
                            ToolTip = $"{entry.Name}\nLibrary | {entry.Source}"
                        };
                        var stack = new StackPanel();
                        var imgBorder = new Border
                        {
                            Height = 125, Background = Brushes.Black,
                            CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
                        };
                        var img = new Image { Stretch = Stretch.UniformToFill };
                        imgBorder.Child = img;
                        deferredImages.Add((img, entry.Id, entry.FilePath));
                        stack.Children.Add(imgBorder);

                        var lbl = new TextBlock
                        {
                            Text = "\u2605 " + entry.Name,
                            Foreground = AppBrushes.AccentGreen,
                            FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(3, 4, 3, 0)
                        };
                        stack.Children.Add(lbl);
                        var detailLbl = new TextBlock
                        {
                            Text = $"Library | {entry.Source}",
                            Foreground = AppBrushes.TextMuted,
                            FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(3, 0, 3, 2)
                        };
                        stack.Children.Add(detailLbl);
                        border.Child = stack;

                        string path = entry.FilePath;
                        string capturedName = entry.Name;
                        string detail = $"Library | {entry.Source}";
                        border.MouseLeftButtonUp += (_, _) => SelectOption(tab, capturedName, path, detail, border);
                        border.MouseLeftButtonDown += (_, ev) =>
                        {
                            if (ev.ClickCount == 2) { SelectOption(tab, capturedName, path, detail, border); OkClick(null!, null!); }
                        };

                        tab.OptionsPanel.Children.Add(border);
                        tab.AllTiles.Add(new TileInfo(border, entry.Name, entry.Source, $"Library | {entry.Source}"));
                    }

                    // Load library thumbnails progressively
                    if (deferredImages.Count > 0)
                    {
                        const int batchSize = 20;
                        for (int i = 0; i < deferredImages.Count; i += batchSize)
                        {
                            var batch = deferredImages.Skip(i).Take(batchSize).ToList();
                            var thumbSvc = _frontThumbnails;
                            var bitmaps = await Task.Run(() =>
                            {
                                var results = new List<BitmapImage?>();
                                foreach (var (_, entryId, path) in batch)
                                {
                                    try
                                    {
                                        var loadPath = thumbSvc?.GetOrCreate(entryId, path) ?? path;
                                        var bmp = new BitmapImage();
                                        bmp.BeginInit();
                                        bmp.UriSource = new Uri(loadPath, UriKind.Absolute);
                                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                                        bmp.DecodePixelWidth = 150;
                                        bmp.EndInit();
                                        bmp.Freeze();
                                        results.Add(bmp);
                                    }
                                    catch { results.Add(null); }
                                }
                                return results;
                            });
                            for (int j = 0; j < batch.Count && j < bitmaps.Count; j++)
                                if (bitmaps[j] != null) batch[j].img.Source = bitmaps[j];
                        }
                    }
                }
            }

            // 2. Kick off API searches concurrently
            StatusLabel.Text = $"Searching for \"{_card.Name}\"...";
            Log.Information("Loading front art options for {CardName}", _card.Name);
            var mpcOpts = BuildSearchOptionsFromControls();
            mpcOpts.FuzzySearch = false;
            var scryfallTask = Task.Run(async () =>
            {
                try { return (await _scryfall.SearchCardAsync($"!\"{_card.Name}\"")).Cards; }
                catch (Exception ex) { Log.Warning(ex, "Scryfall search failed in art selector"); return new List<ScryfallCard>(); }
            });
            var mpcTask = Task.Run(async () =>
            {
                try
                {
                    var (results, _) = await _mpcFill.SearchAsync(
                        _card.Name, fuzzySearch: false, sourcesOverride: _mpcSourcesOverride,
                        options: mpcOpts);
                    return results
                        .Where(mc => mc.Name.Contains(_card.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                catch { return new List<MpcFillCard>(); }
            });

            await Task.WhenAll(scryfallTask, mpcTask);
            var scryfallResults = scryfallTask.Result;
            var mpcResults = mpcTask.Result;

            // Skip MPCFill results that are already in the local library
            if (libraryNames.Count > 0)
                mpcResults = mpcResults
                    .Where(mc => !libraryNames.Contains($"{mc.Name} [{mc.Source}]"))
                    .ToList();

            int totalImages = scryfallResults.Count + mpcResults.Count;

            // Warn the user if there are a lot of results to cache
            if (totalImages > 200)
            {
                var answer = MessageBox.Show(
                    $"Found {totalImages} art options ({scryfallResults.Count} Scryfall, {mpcResults.Count} MPCFill).\n\n" +
                    "Downloading and caching all of these may take a while. Continue?",
                    "Large Number of Results",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusLabel.Text = $"{totalImages} results found (download skipped)";
                    SpinnerDot.Visibility = Visibility.Collapsed;
                    AddActionTile(tab, "Browse File...", OnBrowseFile);
                    return;
                }
            }

            // Download all images in parallel
            int completed = 0;
            var semaphore = new System.Threading.SemaphoreSlim(8);

            var scryfallDownloads = scryfallResults
                .Where(sc => sc.GetImageUrl() != null)
                .Select(async sc =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var cached = await _scryfall.DownloadAndCacheImageAsync(sc, size: "small");
                        var done = System.Threading.Interlocked.Increment(ref completed);
                        _ = Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading art {done}/{totalImages}...");
                        if (cached != null)
                            return (Label: $"{sc.SetName} #{sc.CollectorNumber}",
                                    Path: cached,
                                    Detail: $"Scryfall | {sc.Artist ?? ""}",
                                    ScryfallCard: (ScryfallCard?)sc,
                                    MpcSource: (string?)null,
                                    MpcCard: (MpcFillCard?)null);
                        return default;
                    }
                    finally { semaphore.Release(); }
                }).ToList();

            var mpcDownloads = mpcResults.Select(async mc =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var cached = await _mpcFill.DownloadAndCacheImageAsync(mc, thumbnail: true);
                    var done = System.Threading.Interlocked.Increment(ref completed);
                    _ = Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading art {done}/{totalImages}...");
                    if (cached != null)
                        return (Label: mc.Name,
                                Path: cached,
                                Detail: $"MPCFill | {mc.Source} | {mc.Dpi} DPI",
                                ScryfallCard: (ScryfallCard?)null,
                                MpcSource: (string?)mc.Source,
                                MpcCard: (MpcFillCard?)mc);
                    return default;
                }
                finally { semaphore.Release(); }
            }).ToList();

            var allDownloads = scryfallDownloads.Concat(mpcDownloads).ToList();
            var downloadResults = await Task.WhenAll(allDownloads);

            // Add tiles (Scryfall first, then MPCFill)
            foreach (var result in downloadResults)
            {
                if (result.Path != null && !shown.Contains(result.Path))
                {
                    AddOption(tab, result.Label, result.Path, false, result.Detail, result.MpcSource);
                    shown.Add(result.Path);
                    if (result.ScryfallCard != null)
                        tab.ScryfallCardsByPath[result.Path] = result.ScryfallCard;
                    if (result.MpcCard != null)
                        tab.MpcFillCardsByPath[result.Path] = result.MpcCard;
                }
            }

            // "Browse File" action tile only shown when no actions bar (back mode)
            if (_frontArtLibrary == null)
                AddActionTile(tab, "Browse File...", OnBrowseFile);
        }

        private async Task LoadBackOptionsAsync(TabState tab, HashSet<string> shown)
        {
            // Original Scryfall back (if card was double-faced)
            if (!string.IsNullOrEmpty(_card.OriginalBackArtworkPath)
                && File.Exists(_card.OriginalBackArtworkPath)
                && !shown.Contains(_card.OriginalBackArtworkPath))
            {
                AddOption(tab, "Original (Scryfall)", _card.OriginalBackArtworkPath, false, "From Scryfall import");
                shown.Add(_card.OriginalBackArtworkPath);
            }

            // Search Scryfall for back face (MDFCs, transform cards, etc.)
            if (_scryfall != null && !string.IsNullOrEmpty(_card.Name)
                && (_card.IsDoubleFaced || !string.IsNullOrEmpty(_card.OriginalBackArtworkPath)))
            {
                StatusLabel.Text = "Searching Scryfall for back face...";
                try
                {
                    var sc = await _scryfall.GetCardByNameAsync(_card.Name);
                    if (sc?.GetBackImageUrl() != null)
                    {
                        var cachedBack = await _scryfall.DownloadAndCacheImageAsync(sc, back: true, size: "small");
                        if (cachedBack != null && !shown.Contains(cachedBack))
                        {
                            string label = sc.CardFaces?.Count > 1
                                ? $"{sc.CardFaces[1].Name} (Scryfall)"
                                : "Back Face (Scryfall)";
                            AddOption(tab, label, cachedBack, false,
                                $"Scryfall | {sc.SetName} #{sc.CollectorNumber}");
                            shown.Add(cachedBack);
                            tab.ScryfallCardsByPath[cachedBack] = sc;
                        }
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "Scryfall back face lookup failed for {CardName}", _card.Name); }
            }

            // Library entries — build tiles instantly with deferred image loading
            var deferredImages = new List<(Image img, string entryId, string path)>();

            if (_backLibrary != null)
            {
                var entries = _backLibrary.Entries.Where(e => File.Exists(e.FilePath) && !shown.Contains(e.FilePath)).ToList();
                StatusLabel.Text = $"Loading {entries.Count} library entries...";

                foreach (var entry in entries)
                {
                    shown.Add(entry.FilePath);

                    bool isDefault = _backLibrary.IsDefault(entry.Id);

                    var border = new Border
                    {
                        Width = 110, Height = 165, Margin = new Thickness(4),
                        Background = AppBrushes.TileBg,
                        CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                        BorderThickness = new Thickness(2),
                        BorderBrush = isDefault ? AppBrushes.AccentGreen : Brushes.Transparent,
                        ToolTip = $"{entry.Name}\n{(isDefault ? "DEFAULT\n" : "")}From library"
                    };

                    var stack = new StackPanel();

                    var imgBorder = new Border
                    {
                        Height = 125, Background = Brushes.Black,
                        CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
                    };
                    var img = new Image { Stretch = Stretch.UniformToFill };
                    imgBorder.Child = img;
                    deferredImages.Add((img, entry.Id, entry.FilePath));
                    stack.Children.Add(imgBorder);

                    var lbl = new TextBlock
                    {
                        Text = (isDefault ? "\u2605 " : "") + entry.Name,
                        Foreground = isDefault
                            ? AppBrushes.AccentGreen
                            : AppBrushes.TextSecondary,
                        FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 4, 3, 0)
                    };
                    stack.Children.Add(lbl);

                    var detailLbl = new TextBlock
                    {
                        Text = "From library",
                        Foreground = AppBrushes.TextMuted,
                        FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 2)
                    };
                    stack.Children.Add(detailLbl);

                    border.Child = stack;

                    string path = entry.FilePath;
                    string capturedName = entry.Name;
                    border.MouseLeftButtonUp += (_, _) => SelectOption(tab, capturedName, path, "From library", border);
                    border.MouseLeftButtonDown += (_, ev) =>
                    {
                        if (ev.ClickCount == 2) { SelectOption(tab, capturedName, path, "From library", border); OkClick(null!, null!); }
                    };
                    border.MouseRightButtonUp += (_, ev) =>
                    {
                        var menu = new System.Windows.Controls.ContextMenu();
                        var previewItem = new System.Windows.Controls.MenuItem { Header = "Preview Full Size" };
                        previewItem.Click += (_, _) =>
                        {
                            var preview = new ImagePreviewDialog(path, capturedName);
                            preview.Owner = this;
                            preview.ShowDialog();
                        };
                        menu.Items.Add(previewItem);
                        menu.IsOpen = true;
                        ev.Handled = true;
                    };

                    tab.OptionsPanel.Children.Add(border);
                    tab.AllTiles.Add(new TileInfo(border, entry.Name, entry.Source, "From library"));
                }
            }

            StatusLabel.Text = $"{shown.Count} option(s) found";

            // Load thumbnails progressively on background thread
            if (deferredImages.Count > 0)
            {
                StatusLabel.Text = $"{shown.Count} option(s) — loading thumbnails...";
                const int batchSize = 20;
                for (int i = 0; i < deferredImages.Count; i += batchSize)
                {
                    var batch = deferredImages.Skip(i).Take(batchSize).ToList();
                    var thumbSvc = _backThumbnails;
                    var bitmaps = await Task.Run(() =>
                    {
                        var results = new List<BitmapImage?>();
                        foreach (var (_, entryId, path) in batch)
                        {
                            try
                            {
                                var loadPath = thumbSvc?.GetOrCreate(entryId, path) ?? path;
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(loadPath, UriKind.Absolute);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.DecodePixelWidth = 150;
                                bmp.EndInit();
                                bmp.Freeze();
                                results.Add(bmp);
                            }
                            catch { results.Add(null); }
                        }
                        return results;
                    });

                    for (int j = 0; j < batch.Count && j < bitmaps.Count; j++)
                        if (bitmaps[j] != null) batch[j].img.Source = bitmaps[j];
                }
                StatusLabel.Text = $"{shown.Count} option(s) found";
            }
        }

        private async void OnDownloadMpcFillBacks()
        {
            if (_backLibrary == null) return;

            StatusLabel.Text = "Fetching card back list from MPCFill...";
            SpinnerDot.Visibility = Visibility.Visible;

            try
            {
                var (cardbacks, error) = await _mpcFill.SearchCardbacksAsync(500);
                if (error != null || cardbacks.Count == 0)
                {
                    StatusLabel.Text = error ?? "No card backs found on MPCFill.";
                    SpinnerDot.Visibility = Visibility.Collapsed;
                    return;
                }

                StatusLabel.Text = $"Downloading {cardbacks.Count} card backs...";
                var results = await _mpcFill.DownloadAndCacheImagesAsync(
                    cardbacks,
                    maxConcurrency: 8,
                    onProgress: (done, total, name) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading card back {done}/{total}: {name}..."));

                int added = 0;
                int skipped = 0;
                _backLibrary.BeginBatch();
                try
                {
                    foreach (var (cb, cached) in results)
                    {
                        if (cached == null) { skipped++; continue; }
                        string displayName = $"{cb.Name} [{cb.Source}]";
                        var entry = _backLibrary.AddFromFile(cached, displayName, cb.Source);
                        if (entry != null)
                        {
                            _backLibrary.ApplyMpcFillDefaults(entry.Id, cb.Source);
                            added++;
                        }
                        else skipped++;
                    }
                }
                finally { _backLibrary.EndBatch(); }

                StatusLabel.Text = $"Added {added} card back(s) to library ({skipped} already existed or failed)";

                // Rebuild the back tab to show the new library entries
                await LoadTabContentAsync(_backTab);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                SpinnerDot.Visibility = Visibility.Collapsed;
            }
        }

        // ================================================================
        //  TILE BUILDERS
        // ================================================================

        private void AddOption(TabState tab, string label, string imagePath, bool isCurrent, string detail, string? mpcSource = null)
        {
            var border = Controls.ArtTileBuilder.CreateOptionTile(label, imagePath, isCurrent, detail);

            string path = imagePath;
            string capturedLabel = label;
            string capturedDetail = detail;
            border.MouseLeftButtonUp += (_, _) => SelectOption(tab, capturedLabel, path, capturedDetail, border);
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SelectOption(tab, capturedLabel, path, capturedDetail, border);
                    OkClick(null!, null!);
                }
            };
            border.MouseRightButtonUp += (_, e) =>
            {
                var menu = new System.Windows.Controls.ContextMenu();
                var previewItem = new System.Windows.Controls.MenuItem { Header = "Preview Full Size" };
                previewItem.Click += (_, _) =>
                {
                    var preview = new ImagePreviewDialog(path, capturedLabel);
                    preview.Owner = this;
                    preview.ShowDialog();
                };
                menu.Items.Add(previewItem);

                if (_frontArtLibrary != null && mpcSource != null)
                {
                    var saveItem = new System.Windows.Controls.MenuItem { Header = "Save to Library" };
                    saveItem.Click += async (_, _) =>
                    {
                        // Download full resolution if browsing with thumbnail
                        string savePath = path;
                        if (tab.MpcFillCardsByPath.TryGetValue(path, out var mc))
                        {
                            StatusLabel.Text = "Downloading full resolution...";
                            var fullPath = await _mpcFill.DownloadAndCacheImageAsync(mc);
                            if (fullPath != null) savePath = fullPath;
                        }
                        string libName = $"{capturedLabel} [{mpcSource}]";
                        var entry = _frontArtLibrary.AddFromFile(savePath, libName, mpcSource);
                        if (entry != null)
                        {
                            var sc = tab.ScryfallCardsByPath.TryGetValue(path, out var exact) ? exact
                                   : tab.ScryfallCardsByPath.Values.FirstOrDefault();
                            if (sc != null)
                                _frontArtLibrary.ApplyMetadata(entry.Id, sc);
                            _frontArtLibrary.ApplyMpcFillDefaults(entry.Id, mpcSource);
                        }
                        StatusLabel.Text = entry != null
                            ? $"Saved \"{libName}\" to front art library"
                            : $"\"{libName}\" already in library";
                    };
                    menu.Items.Add(saveItem);
                }

                menu.IsOpen = true;
                e.Handled = true;
            };

            tab.OptionsPanel.Children.Add(border);

            string trackSource;
            if (!string.IsNullOrEmpty(mpcSource))
                trackSource = mpcSource;
            else if (detail.Contains("Scryfall", StringComparison.OrdinalIgnoreCase))
                trackSource = "Scryfall";
            else if (detail.Contains("Library", StringComparison.OrdinalIgnoreCase))
                trackSource = "Library";
            else
                trackSource = "";
            tab.AllTiles.Add(new TileInfo(border, label, trackSource, detail));
        }

        private void AddActionTile(TabState tab, string label, Action action)
        {
            var border = Controls.ArtTileBuilder.CreateActionTile(label);
            border.MouseLeftButtonUp += (_, _) => action();
            tab.OptionsPanel.Children.Add(border);
            tab.AllTiles.Add(new TileInfo(border, label, "", "", IsAction: true));
        }

        // ================================================================
        //  SELECTION
        // ================================================================

        private void SelectOption(TabState tab, string label, string path, string detail, Border selectedBorder)
        {
            foreach (var child in tab.OptionsPanel.Children)
                if (child is Border b) b.BorderBrush = Brushes.Transparent;
            selectedBorder.BorderBrush = Brushes.DodgerBlue;

            tab.ResultPath = path;
            tab.SelectedLabel = label;
            tab.SelectedDetail = detail;
            tab.SelectedBorder = selectedBorder;
            OkBtn.IsEnabled = true;

            PreviewPanel.ShowImage(path, label, detail);
        }

        // ================================================================
        //  ACTIONS
        // ================================================================

        private void OnImportCacheToLibrary(object sender, RoutedEventArgs e) => OnImportCacheToLibrary();
        private void OnAddToFrontLibraryClick(object sender, RoutedEventArgs e) => OnAddToFrontLibrary();
        private void OnAddToBackLibraryClick(object sender, RoutedEventArgs e) => OnAddToLibrary();
        private void OnDownloadMpcFillBacksClick(object sender, RoutedEventArgs e) => OnDownloadMpcFillBacks();
        private void OnBrowseFileClick(object sender, RoutedEventArgs e) => OnBrowseFile();

        private async void OnImportCacheToLibrary()
        {
            if (_frontArtLibrary == null) return;

            var cached = _imageCache.GetCachedByPrefix("mpc_");
            if (cached.Count == 0)
            {
                StatusLabel.Text = "No downloaded MPCFill art found in cache.";
                return;
            }

            // Use the Scryfall data we already have for this card (all entries are proxies of the same card)
            var scryfallCard = _frontTab.ScryfallCardsByPath.Values.FirstOrDefault();

            int added = 0, skipped = 0;
            var newEntries = new List<(string Id, string FilePath)>();
            var importedCacheKeys = new List<string>();
            _frontArtLibrary.BeginBatch();
            try
            {
                foreach (var (key, path, name, source) in cached)
                {
                    if (!File.Exists(path)) { skipped++; continue; }
                    string displayName = !string.IsNullOrEmpty(source)
                        ? $"{name} [{source}]" : name;
                    var entry = _frontArtLibrary.AddFromFile(path, displayName, source);
                    if (entry != null)
                    {
                        added++;
                        newEntries.Add((entry.Id, entry.FilePath));
                        importedCacheKeys.Add(key);

                        if (scryfallCard != null)
                            _frontArtLibrary.ApplyMetadata(entry.Id, scryfallCard);
                        _frontArtLibrary.ApplyMpcFillDefaults(entry.Id, source);
                    }
                    else skipped++;
                }
            }
            finally { _frontArtLibrary.EndBatch(); }

            // Generate thumbnails for newly added entries
            if (newEntries.Count > 0 && _frontThumbnails != null)
            {
                StatusLabel.Text = $"Generating thumbnails for {newEntries.Count} new image(s)...";
                var thumbSvc = _frontThumbnails;
                await Task.Run(() => thumbSvc.RegenerateAll(newEntries,
                    onProgress: (done, total) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Generating thumbnails {done}/{total}...")));
            }

            // Remove imported items from cache
            foreach (var key in importedCacheKeys)
                _imageCache.Remove(key);

            StatusLabel.Text = $"Imported {added} image(s) to library ({skipped} already existed or skipped)";
            if (added > 0)
                _ = LoadTabContentAsync(_frontTab);
        }

        private void OnAddToFrontLibrary()
        {
            if (_frontArtLibrary == null) return;
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Front Art Library",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            int added = 0;
            foreach (var file in dialog.FileNames)
            {
                if (_frontArtLibrary.AddFromFile(file) != null) added++;
            }
            StatusLabel.Text = $"Added {added} image(s) to front art library";
            _ = LoadTabContentAsync(_frontTab);
        }

        private void OnAddToLibrary()
        {
            if (_backLibrary == null) return;
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Back Art Library"
            };
            if (dialog.ShowDialog() != true) return;
            _backLibrary.AddFromFile(dialog.FileName);
            _ = LoadTabContentAsync(_backTab);
        }

        private void OnBrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Artwork"
            };
            if (dialog.ShowDialog() != true) return;

            _activeTab.ResultPath = dialog.FileName;
            _activeTab.SelectedLabel = Path.GetFileName(dialog.FileName);
            _activeTab.SelectedDetail = "Local file";
            OkBtn.IsEnabled = true;
            PreviewPanel.ShowImage(dialog.FileName, Path.GetFileName(dialog.FileName), "Local file");
        }

        // ================================================================
        //  SEARCH & SOURCE FILTER
        // ================================================================

        private void ApplyFilters(TabState tab)
        {
            if (tab.AllTiles.Count == 0) return;

            string searchText = tab.SearchBar?.SearchText ?? "";
            string sourceFilter = tab.SearchBar?.IsAllSourcesSelected == false ? tab.SearchBar.SelectedSource : "";

            int visible = 0;
            int total = 0;

            foreach (var tile in tab.AllTiles)
            {
                if (tile.IsAction)
                {
                    tile.Tile.Visibility = Visibility.Visible;
                    continue;
                }

                total++;

                bool matchesSearch = string.IsNullOrEmpty(searchText) ||
                    tile.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    tile.Source.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    tile.Detail.Contains(searchText, StringComparison.OrdinalIgnoreCase);

                bool matchesSource = string.IsNullOrEmpty(sourceFilter) ||
                    tile.Source.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase);

                bool show = matchesSearch && matchesSource;
                tile.Tile.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) visible++;
            }

            if (!string.IsNullOrEmpty(searchText) || !string.IsNullOrEmpty(sourceFilter))
                StatusLabel.Text = $"Showing {visible} of {total} option(s)";
        }

        private void PopulateSourceFilter(TabState tab)
        {
            var sources = tab.AllTiles
                .Where(t => !t.IsAction && !string.IsNullOrEmpty(t.Source))
                .Select(t => t.Source)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s);

            tab.SearchBar.SetSources(sources, "All Sources");
        }

        // ================================================================
        //  MPCFILL FILTER PANEL
        // ================================================================

        private void LoadFilterControls(MpcFillSearchOptions opts)
        {
            SelectByTag(FilterSortByBox, opts.SortBy);
            SelectByTag(FilterMinDpiBox, opts.MinimumDpi.ToString());
            SelectByTag(FilterMaxDpiBox, opts.MaximumDpi.ToString());
            FilterMaxSizeBox.Text = opts.MaximumSize.ToString();
            FilterFuzzyBox.IsChecked = opts.FuzzySearch;
            FilterCardbacksBox.IsChecked = opts.FilterCardbacks;

            FilterTypeCard.IsChecked = opts.CardTypes.Contains("CARD");
            FilterTypeToken.IsChecked = opts.CardTypes.Contains("TOKEN");
            FilterTypeCardback.IsChecked = opts.CardTypes.Contains("CARDBACK");

            var langs = opts.Languages;
            FilterLangEN.IsChecked = langs.Contains("EN");
            FilterLangJA.IsChecked = langs.Contains("JA");
            FilterLangFR.IsChecked = langs.Contains("FR");
            FilterLangDE.IsChecked = langs.Contains("DE");
            FilterLangES.IsChecked = langs.Contains("ES");
            FilterLangIT.IsChecked = langs.Contains("IT");
            FilterLangPT.IsChecked = langs.Contains("PT");
            FilterLangZH.IsChecked = langs.Contains("ZH");
            FilterLangRU.IsChecked = langs.Contains("RU");
            FilterLangAR.IsChecked = langs.Contains("AR");
            FilterLangSA.IsChecked = langs.Contains("SA");

            FilterExcludeNsfw.IsChecked = opts.ExcludesTags.Contains("NSFW");
            FilterExcludeAiArt.IsChecked = opts.ExcludesTags.Contains("AI Art");
        }

        private MpcFillSearchOptions BuildSearchOptionsFromControls()
        {
            var cardTypes = new List<string>();
            if (FilterTypeCard.IsChecked == true) cardTypes.Add("CARD");
            if (FilterTypeToken.IsChecked == true) cardTypes.Add("TOKEN");
            if (FilterTypeCardback.IsChecked == true) cardTypes.Add("CARDBACK");
            if (cardTypes.Count == 0) cardTypes.Add("CARD");

            var languages = new List<string>();
            if (FilterLangEN.IsChecked == true) languages.Add("EN");
            if (FilterLangJA.IsChecked == true) languages.Add("JA");
            if (FilterLangFR.IsChecked == true) languages.Add("FR");
            if (FilterLangDE.IsChecked == true) languages.Add("DE");
            if (FilterLangES.IsChecked == true) languages.Add("ES");
            if (FilterLangIT.IsChecked == true) languages.Add("IT");
            if (FilterLangPT.IsChecked == true) languages.Add("PT");
            if (FilterLangZH.IsChecked == true) languages.Add("ZH");
            if (FilterLangRU.IsChecked == true) languages.Add("RU");
            if (FilterLangAR.IsChecked == true) languages.Add("AR");
            if (FilterLangSA.IsChecked == true) languages.Add("SA");

            var excludeTags = new List<string>();
            if (FilterExcludeNsfw.IsChecked == true) excludeTags.Add("NSFW");
            if (FilterExcludeAiArt.IsChecked == true) excludeTags.Add("AI Art");

            return new MpcFillSearchOptions
            {
                CardTypes = cardTypes.ToArray(),
                SortBy = (FilterSortByBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "nameAscending",
                MinimumDpi = int.TryParse((FilterMinDpiBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var minD) ? minD : 0,
                MaximumDpi = int.TryParse((FilterMaxDpiBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var maxD) ? maxD : 1500,
                MaximumSize = int.TryParse(FilterMaxSizeBox.Text, out var ms) && ms > 0 ? ms : 30,
                FuzzySearch = FilterFuzzyBox.IsChecked == true,
                FilterCardbacks = FilterCardbacksBox.IsChecked == true,
                Languages = languages.ToArray(),
                IncludesTags = Array.Empty<string>(),
                ExcludesTags = excludeTags.ToArray()
            };
        }

        private static void SelectByTag(ComboBox box, string tagValue)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                if (item.Tag?.ToString() == tagValue)
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            box.SelectedIndex = box.Items.Count - 1;
        }

        private void OnClearFilters(object sender, RoutedEventArgs e)
        {
            LoadFilterControls(new MpcFillSearchOptions());
        }

        private async void OnResearchMpcFill(object sender, RoutedEventArgs e)
        {
            _mpcSearchOptions = BuildSearchOptionsFromControls();
            _frontTab.ScryfallCardsByPath.Clear();
            _frontTab.ResultPath = null;
            _frontTab.SelectedBorder = null;
            _frontTab.SelectedLabel = null;
            _frontTab.SelectedDetail = null;
            OkBtn.IsEnabled = false;
            PreviewPanel.Clear();
            SpinnerDot.Visibility = Visibility.Visible;
            await LoadTabContentAsync(_frontTab);
        }

        // ================================================================
        //  OK / CANCEL
        // ================================================================

        private async void OkClick(object sender, RoutedEventArgs e)
        {
            ResultMode = _activeTab.Mode;
            ResultPath = _activeTab.ResultPath;
            Log.Information("Art selected: {Mode} for {Path}", ResultMode, ResultPath);

            // If the selected path is a normal-size Scryfall thumbnail, upgrade to full-size
            if (ResultMode == ArtSelectorMode.Front && ResultPath != null
                && _frontTab.ScryfallCardsByPath.TryGetValue(ResultPath, out var sc))
            {
                OkBtn.IsEnabled = false;
                StatusLabel.Text = "Downloading full resolution...";
                var fullPath = await _scryfall.DownloadAndCacheImageAsync(sc, size: "large");
                if (fullPath != null)
                    ResultPath = fullPath;
                OkBtn.IsEnabled = true;
            }

            // Front tab: upgrade MPCFill thumbnail to full resolution
            if (ResultMode == ArtSelectorMode.Front && ResultPath != null
                && _frontTab.MpcFillCardsByPath.TryGetValue(ResultPath, out var mpcCard))
            {
                OkBtn.IsEnabled = false;
                StatusLabel.Text = "Downloading full resolution...";
                var fullPath = await _mpcFill.DownloadAndCacheImageAsync(mpcCard);
                if (fullPath != null)
                    ResultPath = fullPath;
                OkBtn.IsEnabled = true;
            }

            // Back tab: upgrade Scryfall back face to full-size
            if (ResultMode == ArtSelectorMode.Back && ResultPath != null
                && _backTab.ScryfallCardsByPath.TryGetValue(ResultPath, out var backSc))
            {
                OkBtn.IsEnabled = false;
                StatusLabel.Text = "Downloading full resolution...";
                var fullPath = await _scryfall.DownloadAndCacheImageAsync(backSc, back: true, size: "large");
                if (fullPath != null)
                    ResultPath = fullPath;
                OkBtn.IsEnabled = true;
            }

            ApplyToSameName = ApplySameNameChk.IsChecked == true;
            ApplyToNoBack = ApplyNoBackChk.IsChecked == true;
            DialogResult = true;
        }
    }
}
