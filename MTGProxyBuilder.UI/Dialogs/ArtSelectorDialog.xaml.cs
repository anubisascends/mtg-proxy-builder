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

namespace MTGProxyBuilder.UI.Dialogs
{
    public enum ArtSelectorMode { Front, Back }

    public partial class ArtSelectorDialog : Window
    {
        private readonly CardModel _card;
        private readonly ArtSelectorMode _mode;
        private readonly ScryfallService _scryfall;
        private readonly MpcFillService _mpcFill;
        private readonly ImageCacheService _imageCache;
        private readonly BackArtLibraryService? _backLibrary;
        private readonly IList<CardModel>? _allCards;

        public string? ResultPath { get; private set; }

        /// <summary>When true, the result should be applied to all cards with matching name.</summary>
        public bool ApplyToSameName { get; private set; }

        /// <summary>When true, the result should be applied to all cards without back art.</summary>
        public bool ApplyToNoBack { get; private set; }

        public ArtSelectorDialog(
            CardModel card,
            ArtSelectorMode mode,
            ScryfallService scryfall,
            MpcFillService mpcFill,
            ImageCacheService imageCache,
            BackArtLibraryService? backLibrary = null,
            IList<CardModel>? allCards = null)
        {
            InitializeComponent();
            _card = card;
            _mode = mode;
            _scryfall = scryfall;
            _mpcFill = mpcFill;
            _imageCache = imageCache;
            _backLibrary = backLibrary;
            _allCards = allCards;

            bool isFront = mode == ArtSelectorMode.Front;
            TitleLabel.Text = isFront ? "Select Front Artwork" : "Select Card Back";
            CardNameLabel.Text = $"for: {card.Name}";

            // Set up bulk action buttons
            if (isFront && _allCards != null)
            {
                int sameNameCount = _allCards.Count(c => c.Name == card.Name);
                if (sameNameCount > 1)
                {
                    ApplySameNameChk.Content = $"Apply to all \"{card.Name}\" ({sameNameCount} cards)";
                    ApplySameNameChk.Visibility = Visibility.Visible;
                }
            }

            if (!isFront && _allCards != null)
            {
                int noBackCount = _allCards.Count(c => string.IsNullOrEmpty(c.BackArtworkPath));
                if (noBackCount > 0)
                {
                    ApplyNoBackChk.Content = $"Apply to all without back art ({noBackCount} cards)";
                    ApplyNoBackChk.Visibility = Visibility.Visible;
                }
            }

            Loaded += async (_, _) => await LoadOptionsAsync();
        }

        private async Task LoadOptionsAsync()
        {
            OptionsPanel.Children.Clear();
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool isFront = _mode == ArtSelectorMode.Front;

            // 1. Current artwork
            string? currentPath = isFront ? _card.ArtworkPath : _card.BackArtworkPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                AddOption("Current", currentPath, true, "Currently assigned");
                shown.Add(currentPath);
            }

            if (isFront)
            {
                await LoadFrontOptions(shown);
            }
            else
            {
                await LoadBackOptionsAsync(shown);
            }

            StatusLabel.Text = $"{shown.Count} option(s) found";
            SpinnerDot.Visibility = Visibility.Collapsed;
        }

        private async Task LoadFrontOptions(HashSet<string> shown)
        {
            // Search Scryfall for exact name matches (all printings)
            if (!string.IsNullOrEmpty(_card.Name))
            {
                StatusLabel.Text = $"Searching Scryfall for \"{_card.Name}\"...";
                await Task.Delay(10);

                try
                {
                    var (results, _) = await _scryfall.SearchCardAsync($"!\"{_card.Name}\"");
                    int scryfallCount = 0;
                    foreach (var sc in results.Take(20))
                    {
                        string? imgUrl = sc.GetImageUrl();
                        if (imgUrl == null) continue;

                        StatusLabel.Text = $"Downloading Scryfall art {++scryfallCount}...";
                        await Task.Delay(5);

                        string cacheKey = sc.Id;
                        var cached = _imageCache.GetCachedImagePath(cacheKey);
                        if (cached == null)
                        {
                            using var http = new System.Net.Http.HttpClient();
                            http.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
                            http.DefaultRequestHeaders.Add("Accept", "application/json");
                            cached = await _imageCache.CacheImageFromUrlAsync(http, imgUrl, cacheKey);
                        }

                        if (cached != null && !shown.Contains(cached))
                        {
                            string label = $"{sc.SetName} #{sc.CollectorNumber}";
                            AddOption(label, cached, false, $"Scryfall | {sc.Artist ?? ""}");
                            shown.Add(cached);
                        }
                        await Task.Delay(80);
                    }
                }
                catch { }
            }

            // Search MPCFill for matching front art
            if (!string.IsNullOrEmpty(_card.Name))
            {
                StatusLabel.Text = $"Searching MPCFill for \"{_card.Name}\"...";
                await Task.Delay(10);

                try
                {
                    var (results, _) = await _mpcFill.SearchAsync(_card.Name, 50);
                    var filtered = results
                        .Where(mc => mc.Name.Contains(_card.Name, StringComparison.OrdinalIgnoreCase))
                        .Take(20);
                    int mpcCount = 0;
                    foreach (var mc in filtered)
                    {
                        StatusLabel.Text = $"Downloading MPCFill art {++mpcCount}...";
                        var cached = await _mpcFill.DownloadAndCacheImageAsync(mc);
                        if (cached != null && !shown.Contains(cached))
                        {
                            AddOption(mc.Name, cached, false, $"MPCFill | {mc.Source} | {mc.Dpi} DPI");
                            shown.Add(cached);
                        }
                        await Task.Delay(30);
                    }
                }
                catch { }
            }

            AddActionTile("Browse File...", OnBrowseFile);
        }

        private async Task LoadBackOptionsAsync(HashSet<string> shown)
        {
            // Original Scryfall back (if card was double-faced)
            if (!string.IsNullOrEmpty(_card.OriginalBackArtworkPath)
                && File.Exists(_card.OriginalBackArtworkPath)
                && !shown.Contains(_card.OriginalBackArtworkPath))
            {
                AddOption("Original (Scryfall)", _card.OriginalBackArtworkPath, false, "From Scryfall import");
                shown.Add(_card.OriginalBackArtworkPath);
            }

            // Library entries — build tiles instantly with deferred image loading
            var deferredImages = new List<(Image img, string path)>();

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
                        Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                        CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                        BorderThickness = new Thickness(2),
                        BorderBrush = isDefault ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : Brushes.Transparent,
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
                    deferredImages.Add((img, entry.FilePath));
                    stack.Children.Add(imgBorder);

                    var lbl = new TextBlock
                    {
                        Text = (isDefault ? "\u2605 " : "") + entry.Name,
                        Foreground = isDefault
                            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                            : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                        FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 4, 3, 0)
                    };
                    stack.Children.Add(lbl);

                    var detailLbl = new TextBlock
                    {
                        Text = "From library",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 2)
                    };
                    stack.Children.Add(detailLbl);

                    border.Child = stack;

                    string path = entry.FilePath;
                    string capturedName = entry.Name;
                    border.MouseLeftButtonUp += (_, _) => SelectOption(capturedName, path, "From library", border);
                    border.MouseLeftButtonDown += (_, ev) =>
                    {
                        if (ev.ClickCount == 2) { SelectOption(capturedName, path, "From library", border); OkClick(null!, null!); }
                    };

                    OptionsPanel.Children.Add(border);
                }
            }

            // Action tiles
            AddActionTile("Download MPCFill\nCard Backs", OnDownloadMpcFillBacks);
            if (_backLibrary != null)
                AddActionTile("+ Add to Library", OnAddToLibrary);
            AddActionTile("Browse File...", OnBrowseFile);

            StatusLabel.Text = $"{shown.Count} option(s) found";

            // Load thumbnails progressively on background thread
            if (deferredImages.Count > 0)
            {
                StatusLabel.Text = $"{shown.Count} option(s) — loading thumbnails...";
                const int batchSize = 20;
                for (int i = 0; i < deferredImages.Count; i += batchSize)
                {
                    var batch = deferredImages.Skip(i).Take(batchSize).ToList();
                    var bitmaps = await Task.Run(() =>
                    {
                        var results = new List<BitmapImage?>();
                        foreach (var (_, path) in batch)
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(path, UriKind.Absolute);
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

                int added = 0;
                int skipped = 0;
                for (int i = 0; i < cardbacks.Count; i++)
                {
                    var cb = cardbacks[i];
                    StatusLabel.Text = $"Downloading card back {i + 1}/{cardbacks.Count}: {cb.Name}...";
                    await Task.Delay(5);

                    // Download the image
                    var cached = await _mpcFill.DownloadAndCacheImageAsync(cb);
                    if (cached == null) { skipped++; continue; }

                    // Add to library (service handles deduplication by name)
                    string displayName = $"{cb.Name} [{cb.Source}]";
                    var entry = _backLibrary.AddFromFile(cached, displayName);
                    if (entry != null) added++;
                    else skipped++;

                    await Task.Delay(20);
                }

                StatusLabel.Text = $"Added {added} card back(s) to library ({skipped} already existed or failed)";

                // Rebuild the dialog options to show the new library entries
                var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                OptionsPanel.Children.Clear();

                string? currentPath = _card.BackArtworkPath;
                if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                {
                    AddOption("Current", currentPath, true, "Currently assigned");
                    shown.Add(currentPath);
                }
                await LoadBackOptionsAsync(shown);
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

        private void AddOption(string label, string imagePath, bool isCurrent, string detail)
        {
            var border = new Border
            {
                Width = 110, Height = 165, Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = isCurrent ? Brushes.DodgerBlue : Brushes.Transparent,
                ToolTip = $"{label}\n{detail}"
            };

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = 125, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 220;
                bmp.EndInit();
                bmp.Freeze();
                imgBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
            catch
            {
                imgBorder.Child = new TextBlock
                {
                    Text = "?", Foreground = Brushes.Gray, FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label + (isCurrent ? " *" : ""),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 9.5, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;

            string path = imagePath;
            string capturedLabel = label;
            string capturedDetail = detail;
            border.MouseLeftButtonUp += (_, _) => SelectOption(capturedLabel, path, capturedDetail, border);
            border.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SelectOption(capturedLabel, path, capturedDetail, border);
                    OkClick(null!, null!);
                }
            };

            OptionsPanel.Children.Add(border);
        }

        private void AddActionTile(string label, Action action)
        {
            var border = new Border
            {
                Width = 110, Height = 165, Margin = new Thickness(4),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)),
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "+", FontSize = 28, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            border.Child = stack;
            border.MouseLeftButtonUp += (_, _) => action();
            OptionsPanel.Children.Add(border);
        }

        // ================================================================
        //  SELECTION
        // ================================================================

        private void SelectOption(string label, string path, string detail, Border selectedBorder)
        {
            foreach (var child in OptionsPanel.Children)
                if (child is Border b) b.BorderBrush = Brushes.Transparent;
            selectedBorder.BorderBrush = Brushes.DodgerBlue;

            ResultPath = path;
            SelectedLabel.Text = label;
            SelectedDetailLabel.Text = detail;
            OkBtn.IsEnabled = true;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 100;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
            }
            catch { PreviewImage.Source = null; }
        }

        // ================================================================
        //  ACTIONS
        // ================================================================

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
            _ = LoadOptionsAsync(); // rebuild
        }

        private void OnBrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Artwork"
            };
            if (dialog.ShowDialog() != true) return;

            ResultPath = dialog.FileName;
            SelectedLabel.Text = Path.GetFileName(dialog.FileName);
            SelectedDetailLabel.Text = dialog.FileName;
            OkBtn.IsEnabled = true;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 100;
                bmp.EndInit();
                bmp.Freeze();
                PreviewImage.Source = bmp;
            }
            catch { PreviewImage.Source = null; }
        }

        private void OkClick(object sender, RoutedEventArgs e)
        {
            ApplyToSameName = ApplySameNameChk.IsChecked == true;
            ApplyToNoBack = ApplyNoBackChk.IsChecked == true;
            DialogResult = true;
        }
    }
}
