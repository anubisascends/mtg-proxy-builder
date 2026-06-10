using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Converters;
using MTGProxyBuilder.UI.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class ArtLibraryDialog : Window
    {
        private readonly FrontArtLibraryService _frontLibrary;
        private readonly BackArtLibraryService _backLibrary;
        private readonly MpcFillService _mpcFill;
        private readonly ImageCacheService? _imageCache;
        private readonly AppSettingsService? _appSettings;
        private readonly ScryfallService? _scryfall;

        private ThumbnailService _frontThumbnails;
        private ThumbnailService _backThumbnails;
        private bool _isFrontActive = true;

        public ArtLibraryDialog(
            FrontArtLibraryService frontLibrary,
            BackArtLibraryService backLibrary,
            MpcFillService mpcFill,
            ImageCacheService? imageCache = null,
            AppSettingsService? appSettings = null,
            ScryfallService? scryfall = null,
            int initialTab = 0)
        {
            InitializeComponent();
            _frontLibrary = frontLibrary;
            _backLibrary = backLibrary;
            _mpcFill = mpcFill;
            _imageCache = imageCache;
            _appSettings = appSettings;
            _scryfall = scryfall;

            _frontThumbnails = new ThumbnailService(frontLibrary.LibraryDirectory);
            _backThumbnails = new ThumbnailService(backLibrary.LibraryDirectory);

            FrontImportCacheBtn.Visibility = _imageCache != null ? Visibility.Visible : Visibility.Collapsed;

            // Wire filter bars
            FrontFilterBar.FilterChanged += (_, _) => RefreshFrontGrid();
            BackFilterBar.FilterChanged += (_, _) => RefreshBackGrid();

            // Initial setup
            ThumbnailConverter.SetThumbnailService(_frontThumbnails);
            PopulateFrontAutocomplete();
            RefreshFrontGrid();

            if (initialTab == 1)
            {
                LibraryTabControl.SelectedIndex = 1;
            }
        }

        // ================================================================
        //  TAB SWITCHING
        // ================================================================

        private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LibraryTabControl == null || e.Source != LibraryTabControl) return;
            bool isFront = LibraryTabControl.SelectedIndex == 0;
            if (isFront == _isFrontActive) return;
            _isFrontActive = isFront;

            if (isFront)
            {
                ThumbnailConverter.SetThumbnailService(_frontThumbnails);
                PopulateFrontAutocomplete();
                RefreshFrontGrid();
            }
            else
            {
                ThumbnailConverter.SetThumbnailService(_backThumbnails);
                PopulateBackAutocomplete();
                RefreshBackGrid();
            }

            PreviewPanel.Clear();
        }

        // ================================================================
        //  FRONT ART — SEARCH & GRID
        // ================================================================

        private void PopulateFrontAutocomplete()
        {
            var sources = _frontLibrary.Entries
                .Select(e => e.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            FrontFilterBar.SetAutocompleteData(sources, Enumerable.Empty<string>(), Enumerable.Empty<int>());
        }

        private void RefreshFrontGrid()
        {
            var entries = _frontLibrary.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            var filters = FrontFilterBar.Filters;
            if (filters.Count > 0)
            {
                entries = entries.Where(e =>
                    FilterEvaluator.Evaluate(filters, new TileData(e.Name, e.Source, 0, new List<string>())));
            }

            var filteredEntries = entries.ToList();
            FrontListBox.ItemsSource = filteredEntries;
            FrontRemoveBtn.IsEnabled = false;
            FrontRemoveBtn.Content = "Remove Selected";

            int totalCount = _frontLibrary.Entries.Count(e => File.Exists(e.FilePath));
            string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
            CountLabel.Text = $"Front Art: {totalCount} item(s){filterInfo}";
            StatusLabel.Text = "";
        }

        // ================================================================
        //  BACK ART — SEARCH & GRID
        // ================================================================

        private void PopulateBackAutocomplete()
        {
            var sources = _backLibrary.Entries
                .Select(e => e.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            BackFilterBar.SetAutocompleteData(sources, Enumerable.Empty<string>(), Enumerable.Empty<int>());
        }

        private void RefreshBackGrid()
        {
            var entries = _backLibrary.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            var filters = BackFilterBar.Filters;
            if (filters.Count > 0)
            {
                entries = entries.Where(e =>
                    FilterEvaluator.Evaluate(filters, new TileData(e.Name, e.Source, 0, new List<string>())));
            }

            var filteredEntries = entries.ToList();
            BackListBox.ItemsSource = filteredEntries;
            BackRemoveBtn.IsEnabled = false;
            BackRemoveBtn.Content = "Remove Selected";
            BackDefaultBtn.IsEnabled = false;

            var defaultEntry = _backLibrary.DefaultEntryId != null ? _backLibrary.GetById(_backLibrary.DefaultEntryId) : null;
            string defaultInfo = defaultEntry != null ? $" | Default: {defaultEntry.Name}" : "";
            int totalCount = _backLibrary.Entries.Count(e => File.Exists(e.FilePath));
            string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
            CountLabel.Text = $"Back Art: {totalCount} item(s){filterInfo}{defaultInfo}";
            StatusLabel.Text = "";
        }

        // ================================================================
        //  FRONT ART — SELECTION
        // ================================================================

        private void OnFrontSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = FrontListBox.SelectedItems.Cast<BackArtEntry>().ToList();
            FrontRemoveBtn.IsEnabled = selected.Count > 0;
            FrontRemoveBtn.Content = selected.Count > 1 ? $"Remove Selected ({selected.Count})" : "Remove Selected";
            ShowEntryPreview(selected.LastOrDefault());
        }

        private void OnFrontDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FrontListBox.SelectedItem is BackArtEntry entry && File.Exists(entry.FilePath))
            {
                var preview = new ImagePreviewDialog(entry.FilePath, entry.Name);
                preview.Owner = this;
                preview.ShowDialog();
            }
        }

        // ================================================================
        //  BACK ART — SELECTION
        // ================================================================

        private void OnBackSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = BackListBox.SelectedItems.Cast<BackArtEntry>().ToList();
            BackRemoveBtn.IsEnabled = selected.Count > 0;
            BackRemoveBtn.Content = selected.Count > 1 ? $"Remove Selected ({selected.Count})" : "Remove Selected";
            BackDefaultBtn.IsEnabled = selected.Count > 0;
            ShowEntryPreview(selected.LastOrDefault());
        }

        private void OnBackDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BackListBox.SelectedItem is BackArtEntry entry && File.Exists(entry.FilePath))
            {
                var preview = new ImagePreviewDialog(entry.FilePath, entry.Name);
                preview.Owner = this;
                preview.ShowDialog();
            }
        }

        // ================================================================
        //  SHARED PREVIEW
        // ================================================================

        private void ShowEntryPreview(BackArtEntry? entry)
        {
            if (entry != null && File.Exists(entry.FilePath))
            {
                string sourceInfo = !string.IsNullOrEmpty(entry.Source) && entry.Source != "Local"
                    ? $"Source: {entry.Source}\n" : "";
                string detail = $"{sourceInfo}{Path.GetFileName(entry.FilePath)}";
                PreviewPanel.ShowImage(entry.FilePath, entry.Name, detail);
            }
        }

        // ================================================================
        //  FRONT ART — ACTIONS
        // ================================================================

        private void OnFrontAddFromFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Front Art Library",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            foreach (var file in dialog.FileNames)
                _frontLibrary.AddFromFile(file);
            PopulateFrontAutocomplete();
            RefreshFrontGrid();
        }

        private async void OnFrontImportFromCache(object sender, RoutedEventArgs e)
        {
            if (_imageCache == null) return;

            var cached = _imageCache.GetCachedByPrefix("mpc_");
            if (cached.Count == 0)
            {
                StatusLabel.Text = "No downloaded MPCFill art found in cache.";
                return;
            }

            FrontImportCacheBtn.IsEnabled = false;
            StatusLabel.Text = $"Importing from {cached.Count} cached file(s)...";

            int added = 0, skipped = 0;
            var newEntries = new List<(string Id, string FilePath)>();
            var importedCacheKeys = new List<string>();
            _frontLibrary.BeginBatch();
            try
            {
                foreach (var (key, path, name, source) in cached)
                {
                    if (!File.Exists(path)) { skipped++; continue; }
                    string displayName = !string.IsNullOrEmpty(source)
                        ? $"{name} [{source}]" : name;
                    var entry = _frontLibrary.AddFromFile(path, displayName, source);
                    if (entry != null)
                    {
                        added++;
                        newEntries.Add((entry.Id, entry.FilePath));
                        importedCacheKeys.Add(key);
                    }
                    else skipped++;
                }
            }
            finally { _frontLibrary.EndBatch(); }

            // Populate metadata from Scryfall
            if (newEntries.Count > 0 && _scryfall != null)
            {
                var scryfallCache = new Dictionary<string, ScryfallCard?>(StringComparer.OrdinalIgnoreCase);
                int looked = 0;
                for (int i = 0; i < newEntries.Count; i++)
                {
                    var entry = _frontLibrary.GetById(newEntries[i].Id);
                    if (entry == null || !string.IsNullOrEmpty(entry.TypeLine)) continue;

                    string cardName = entry.Name;
                    int bracketIdx = cardName.LastIndexOf('[');
                    if (bracketIdx > 0) cardName = cardName[..bracketIdx].Trim();

                    if (!scryfallCache.TryGetValue(cardName, out var sc))
                    {
                        StatusLabel.Text = $"Looking up metadata {++looked}: {cardName}...";
                        try { sc = await _scryfall.GetCardByNameAsync(cardName); }
                        catch { sc = null; }
                        scryfallCache[cardName] = sc;
                        await Task.Delay(100);
                    }

                    if (sc != null)
                        _frontLibrary.ApplyMetadata(entry.Id, sc);
                }
            }

            // Apply MPCFill defaults
            foreach (var (id, _) in newEntries)
            {
                var entry = _frontLibrary.GetById(id);
                if (entry != null)
                    _frontLibrary.ApplyMpcFillDefaults(id, entry.Source);
            }

            // Generate thumbnails
            if (newEntries.Count > 0)
            {
                StatusLabel.Text = $"Generating thumbnails for {newEntries.Count} new image(s)...";
                var thumbSvc = _frontThumbnails;
                await Task.Run(() => thumbSvc.RegenerateAll(newEntries,
                    onProgress: (done, total) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Generating thumbnails {done}/{total}...")));
                ThumbnailConverter.ClearCache();
            }

            foreach (var key in importedCacheKeys)
                _imageCache.Remove(key);

            if (added > 0)
            {
                PopulateFrontAutocomplete();
                RefreshFrontGrid();
            }
            FrontImportCacheBtn.IsEnabled = true;
        }

        private void OnFrontRemoveSelected(object sender, RoutedEventArgs e)
        {
            var selected = FrontListBox.SelectedItems.Cast<BackArtEntry>().ToList();
            if (selected.Count == 0) return;
            string message = selected.Count == 1
                ? $"Remove \"{selected[0].Name}\" from the library?"
                : $"Remove {selected.Count} items from the library?";
            if (MessageBox.Show(message, "Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            foreach (var entry in selected)
            {
                _frontThumbnails.Delete(entry.Id);
                _frontLibrary.Remove(entry.Id);
            }
            PopulateFrontAutocomplete();
            RefreshFrontGrid();
        }

        private async void OnFrontRegenerateThumbnails(object sender, RoutedEventArgs e)
        {
            FrontRegenThumbBtn.IsEnabled = false;
            StatusLabel.Text = "Regenerating thumbnails...";
            var entries = _frontLibrary.Entries.Where(en => File.Exists(en.FilePath)).Select(en => (en.Id, en.FilePath)).ToList();
            var thumbSvc = _frontThumbnails;
            await Task.Run(() => thumbSvc.RegenerateAll(entries,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Regenerating thumbnails {done}/{total}...")));
            ThumbnailConverter.ClearCache();
            FrontRegenThumbBtn.IsEnabled = true;
            RefreshFrontGrid();
        }

        private async void OnFrontMoveLibrary(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select new location for the front art library" };
            if (dialog.ShowDialog() != true) return;
            string newDir = Path.Combine(dialog.FolderName, "FrontArtLibrary");
            if (string.Equals(newDir, _frontLibrary.LibraryDirectory, StringComparison.OrdinalIgnoreCase)) return;
            if (MessageBox.Show($"Move {_frontLibrary.Entries.Count} image(s) to:\n{newDir}\n\nThis will move all files and delete the old location.",
                "Move Library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            StatusLabel.Text = "Moving library...";
            List<string>? newEntryIds = null;
            await Task.Run(() => newEntryIds = _frontLibrary.MoveToDirectory(newDir,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Moving {done}/{total}...")));

            _frontThumbnails = new ThumbnailService(_frontLibrary.LibraryDirectory);
            ThumbnailConverter.SetThumbnailService(_frontThumbnails);
            ThumbnailConverter.ClearCache();

            if (newEntryIds is { Count: > 0 })
            {
                var toGenerate = _frontLibrary.Entries.Where(en => newEntryIds.Contains(en.Id) && File.Exists(en.FilePath)).Select(en => (en.Id, en.FilePath)).ToList();
                if (toGenerate.Count > 0) await Task.Run(() => _frontThumbnails.RegenerateAll(toGenerate));
            }

            if (_appSettings != null) { _appSettings.Settings.FrontArtLibraryPath = newDir; _appSettings.Save(); }
            RefreshFrontGrid();
        }

        private async void OnFrontExportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "ZIP Archive (*.zip)|*.zip", Title = "Export Front Art Library", FileName = "FrontArtLibrary.zip" };
            if (dialog.ShowDialog() != true) return;
            StatusLabel.Text = "Exporting library...";
            await Task.Run(() => _frontLibrary.ExportToZip(dialog.FileName,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Compressing {done}/{total}...")));
            StatusLabel.Text = "";
        }

        private async void OnFrontImportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "ZIP Archive (*.zip)|*.zip", Title = "Import Front Art Library from ZIP" };
            if (dialog.ShowDialog() != true) return;
            StatusLabel.Text = "Importing from ZIP...";
            await Task.Run(() => _frontLibrary.ImportFromZip(dialog.FileName,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Importing {done}/{total}...")));
            PopulateFrontAutocomplete();
            RefreshFrontGrid();
        }

        // ================================================================
        //  BACK ART — ACTIONS
        // ================================================================

        private void OnBackAddFromFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Back Art Library",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;
            foreach (var file in dialog.FileNames)
                _backLibrary.AddFromFile(file);
            PopulateBackAutocomplete();
            RefreshBackGrid();
        }

        private void OnBackSetDefault(object sender, RoutedEventArgs e)
        {
            if (BackListBox.SelectedItem is BackArtEntry entry)
            {
                _backLibrary.SetDefault(entry.Id);
                RefreshBackGrid();
            }
        }

        private void OnBackClearDefault(object sender, RoutedEventArgs e)
        {
            _backLibrary.SetDefault(null);
            RefreshBackGrid();
        }

        private void OnBackRemoveSelected(object sender, RoutedEventArgs e)
        {
            var selected = BackListBox.SelectedItems.Cast<BackArtEntry>().ToList();
            if (selected.Count == 0) return;
            string message = selected.Count == 1
                ? $"Remove \"{selected[0].Name}\" from the library?"
                : $"Remove {selected.Count} items from the library?";
            if (MessageBox.Show(message, "Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            foreach (var entry in selected)
            {
                _backThumbnails.Delete(entry.Id);
                _backLibrary.Remove(entry.Id);
            }
            PopulateBackAutocomplete();
            RefreshBackGrid();
        }

        private async void OnBackRegenerateThumbnails(object sender, RoutedEventArgs e)
        {
            BackRegenThumbBtn.IsEnabled = false;
            StatusLabel.Text = "Regenerating thumbnails...";
            var entries = _backLibrary.Entries.Where(en => File.Exists(en.FilePath)).Select(en => (en.Id, en.FilePath)).ToList();
            var thumbSvc = _backThumbnails;
            await Task.Run(() => thumbSvc.RegenerateAll(entries,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Regenerating thumbnails {done}/{total}...")));
            ThumbnailConverter.ClearCache();
            BackRegenThumbBtn.IsEnabled = true;
            RefreshBackGrid();
        }

        private async void OnBackDownloadMpcFill(object sender, RoutedEventArgs e)
        {
            BackDownloadBtn.IsEnabled = false;
            StatusLabel.Text = "Fetching card back list from MPCFill...";

            try
            {
                var (cardbacks, error) = await _mpcFill.SearchCardbacksAsync(500);
                if (error != null || cardbacks.Count == 0)
                {
                    StatusLabel.Text = error ?? "No card backs found.";
                    BackDownloadBtn.IsEnabled = true;
                    return;
                }

                StatusLabel.Text = $"Downloading {cardbacks.Count} card backs...";
                var results = await _mpcFill.DownloadAndCacheImagesAsync(
                    cardbacks,
                    maxConcurrency: 8,
                    onProgress: (done, total, name) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading {done}/{total}: {name}..."));

                int added = 0, skipped = 0;
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

                PopulateBackAutocomplete();
                RefreshBackGrid();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                BackDownloadBtn.IsEnabled = true;
            }
        }

        private async void OnBackMoveLibrary(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select new location for the back art library" };
            if (dialog.ShowDialog() != true) return;
            string newDir = Path.Combine(dialog.FolderName, "BackArtLibrary");
            if (string.Equals(newDir, _backLibrary.LibraryDirectory, StringComparison.OrdinalIgnoreCase)) return;
            if (MessageBox.Show($"Move {_backLibrary.Entries.Count} image(s) to:\n{newDir}\n\nThis will move all files and delete the old location.",
                "Move Library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            StatusLabel.Text = "Moving library...";
            List<string>? newEntryIds = null;
            await Task.Run(() => newEntryIds = _backLibrary.MoveToDirectory(newDir,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Moving {done}/{total}...")));

            _backThumbnails = new ThumbnailService(_backLibrary.LibraryDirectory);
            ThumbnailConverter.SetThumbnailService(_backThumbnails);
            ThumbnailConverter.ClearCache();

            if (newEntryIds is { Count: > 0 })
            {
                var toGenerate = _backLibrary.Entries.Where(en => newEntryIds.Contains(en.Id) && File.Exists(en.FilePath)).Select(en => (en.Id, en.FilePath)).ToList();
                if (toGenerate.Count > 0) await Task.Run(() => _backThumbnails.RegenerateAll(toGenerate));
            }

            if (_appSettings != null) { _appSettings.Settings.BackArtLibraryPath = newDir; _appSettings.Save(); }
            RefreshBackGrid();
        }

        private async void OnBackExportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "ZIP Archive (*.zip)|*.zip", Title = "Export Back Art Library", FileName = "BackArtLibrary.zip" };
            if (dialog.ShowDialog() != true) return;
            StatusLabel.Text = "Exporting library...";
            await Task.Run(() => _backLibrary.ExportToZip(dialog.FileName,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Compressing {done}/{total}...")));
            StatusLabel.Text = "";
        }

        private async void OnBackImportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "ZIP Archive (*.zip)|*.zip", Title = "Import Back Art Library from ZIP" };
            if (dialog.ShowDialog() != true) return;
            StatusLabel.Text = "Importing from ZIP...";
            await Task.Run(() => _backLibrary.ImportFromZip(dialog.FileName,
                onProgress: (done, total) => Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Importing {done}/{total}...")));
            PopulateBackAutocomplete();
            RefreshBackGrid();
        }

        // ================================================================
        //  CLOSE
        // ================================================================

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
