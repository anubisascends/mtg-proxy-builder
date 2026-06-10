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
    public partial class BackArtLibraryDialog : Window
    {
        private readonly BackArtLibraryService _library;
        private readonly MpcFillService _mpcFill;
        private readonly AppSettingsService? _appSettings;
        private ThumbnailService _thumbnails;

        public BackArtLibraryDialog(BackArtLibraryService library, MpcFillService mpcFill, AppSettingsService? appSettings = null)
        {
            InitializeComponent();
            _library = library;
            _mpcFill = mpcFill;
            _appSettings = appSettings;
            _thumbnails = new ThumbnailService(library.LibraryDirectory);
            ThumbnailConverter.SetThumbnailService(_thumbnails);
            FilterBar.FilterChanged += (_, _) => RefreshGrid();
            PopulateAutocomplete();
            RefreshGrid();
        }

        // ================================================================
        //  SEARCH & FILTER
        // ================================================================

        private void PopulateAutocomplete()
        {
            var sources = _library.Entries
                .Select(e => e.Source)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            FilterBar.SetAutocompleteData(sources, Enumerable.Empty<string>(), Enumerable.Empty<int>());
        }

        private void RefreshGrid()
        {
            var entries = _library.Entries.Where(e => File.Exists(e.FilePath)).AsEnumerable();

            var filters = FilterBar.Filters;
            if (filters.Count > 0)
            {
                entries = entries.Where(e =>
                    FilterEvaluator.Evaluate(filters, new TileData(e.Name, e.Source, 0, new List<string>())));
            }

            var filteredEntries = entries.ToList();
            LibraryListBox.ItemsSource = filteredEntries;

            RemoveBtn.IsEnabled = false;
            RemoveBtn.Content = "Remove Selected";
            DefaultBtn.IsEnabled = false;

            var defaultEntry = _library.DefaultEntryId != null ? _library.GetById(_library.DefaultEntryId) : null;
            string defaultInfo = defaultEntry != null ? $" | Default: {defaultEntry.Name}" : "";
            int totalCount = _library.Entries.Count(e => File.Exists(e.FilePath));
            string filterInfo = filteredEntries.Count < totalCount ? $" (showing {filteredEntries.Count} of {totalCount})" : "";
            CountLabel.Text = $"{totalCount} item(s) in library{filterInfo}{defaultInfo}";
            StatusLabel.Text = "";
        }

        // ================================================================
        //  SELECTION
        // ================================================================

        private void OnListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = LibraryListBox.SelectedItems.Cast<BackArtEntry>().ToList();
            RemoveBtn.IsEnabled = selected.Count > 0;
            RemoveBtn.Content = selected.Count > 1 ? $"Remove Selected ({selected.Count})" : "Remove Selected";
            DefaultBtn.IsEnabled = selected.Count > 0;

            var entry = selected.LastOrDefault();
            if (entry != null && File.Exists(entry.FilePath))
            {
                string sourceInfo = !string.IsNullOrEmpty(entry.Source) && entry.Source != "Local"
                    ? $"Source: {entry.Source}\n" : "";
                string detail = $"{sourceInfo}{Path.GetFileName(entry.FilePath)}";
                PreviewPanel.ShowImage(entry.FilePath, entry.Name, detail);
            }
        }

        private void OnListBoxDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LibraryListBox.SelectedItem is BackArtEntry entry && File.Exists(entry.FilePath))
            {
                var preview = new ImagePreviewDialog(entry.FilePath, entry.Name);
                preview.Owner = this;
                preview.ShowDialog();
            }
        }

        // ================================================================
        //  ACTIONS
        // ================================================================

        private void OnAddFromFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Image to Back Art Library",
                Multiselect = true
            };
            if (dialog.ShowDialog() != true) return;

            foreach (var file in dialog.FileNames)
                _library.AddFromFile(file);
            RefreshGrid();
        }

        private void OnSetDefault(object sender, RoutedEventArgs e)
        {
            if (LibraryListBox.SelectedItem is BackArtEntry entry)
            {
                _library.SetDefault(entry.Id);
                RefreshGrid();
            }
        }

        private void OnClearDefault(object sender, RoutedEventArgs e)
        {
            _library.SetDefault(null);
            RefreshGrid();
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            var selected = LibraryListBox.SelectedItems.Cast<BackArtEntry>().ToList();
            if (selected.Count == 0) return;

            string message = selected.Count == 1
                ? $"Remove \"{selected[0].Name}\" from the library?"
                : $"Remove {selected.Count} items from the library?";

            if (MessageBox.Show(message, "Remove", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            foreach (var entry in selected)
            {
                _thumbnails.Delete(entry.Id);
                _library.Remove(entry.Id);
            }
            PopulateAutocomplete();
            RefreshGrid();
        }

        private async void OnRegenerateThumbnails(object sender, RoutedEventArgs e)
        {
            RegenThumbBtn.IsEnabled = false;
            StatusLabel.Text = "Regenerating thumbnails...";

            var entries = _library.Entries
                .Where(en => File.Exists(en.FilePath))
                .Select(en => (en.Id, en.FilePath))
                .ToList();

            await Task.Run(() =>
                _thumbnails.RegenerateAll(entries,
                    onProgress: (done, total) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Regenerating thumbnails {done}/{total}...")));

            ThumbnailConverter.ClearCache();
            RegenThumbBtn.IsEnabled = true;
            RefreshGrid();
        }

        private async void OnDownloadMpcFill(object sender, RoutedEventArgs e)
        {
            DownloadBtn.IsEnabled = false;
            StatusLabel.Text = "Fetching card back list from MPCFill...";

            try
            {
                var (cardbacks, error) = await _mpcFill.SearchCardbacksAsync(500);
                if (error != null || cardbacks.Count == 0)
                {
                    StatusLabel.Text = error ?? "No card backs found.";
                    DownloadBtn.IsEnabled = true;
                    return;
                }

                StatusLabel.Text = $"Downloading {cardbacks.Count} card backs...";
                var results = await _mpcFill.DownloadAndCacheImagesAsync(
                    cardbacks,
                    maxConcurrency: 8,
                    onProgress: (done, total, name) =>
                        Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloading {done}/{total}: {name}..."));

                int added = 0, skipped = 0;
                _library.BeginBatch();
                try
                {
                    foreach (var (cb, cached) in results)
                    {
                        if (cached == null) { skipped++; continue; }
                        string displayName = $"{cb.Name} [{cb.Source}]";
                        var entry = _library.AddFromFile(cached, displayName, cb.Source);
                        if (entry != null)
                        {
                            _library.ApplyMpcFillDefaults(entry.Id, cb.Source);
                            added++;
                        }
                        else skipped++;
                    }
                }
                finally { _library.EndBatch(); }

                PopulateAutocomplete();
                RefreshGrid();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                DownloadBtn.IsEnabled = true;
            }
        }

        // ================================================================
        //  LIBRARY MANAGEMENT
        // ================================================================

        private async void OnMoveLibrary(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select new location for the back art library"
            };
            if (dialog.ShowDialog() != true) return;

            string newDir = Path.Combine(dialog.FolderName, "BackArtLibrary");
            if (string.Equals(newDir, _library.LibraryDirectory, StringComparison.OrdinalIgnoreCase))
                return;

            if (MessageBox.Show(
                $"Move {_library.Entries.Count} image(s) to:\n{newDir}\n\nThis will move all files and delete the old location.",
                "Move Library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            StatusLabel.Text = "Moving library...";

            List<string>? newEntryIds = null;
            await Task.Run(() => newEntryIds = _library.MoveToDirectory(newDir,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Moving {done}/{total}...")));

            _thumbnails = new ThumbnailService(_library.LibraryDirectory);
            ThumbnailConverter.SetThumbnailService(_thumbnails);
            ThumbnailConverter.ClearCache();

            if (newEntryIds is { Count: > 0 })
            {
                var toGenerate = _library.Entries
                    .Where(en => newEntryIds.Contains(en.Id) && File.Exists(en.FilePath))
                    .Select(en => (en.Id, en.FilePath)).ToList();
                if (toGenerate.Count > 0)
                    await Task.Run(() => _thumbnails.RegenerateAll(toGenerate));
            }

            if (_appSettings != null)
            {
                _appSettings.Settings.BackArtLibraryPath = newDir;
                _appSettings.Save();
            }

            RefreshGrid();
        }

        private async void OnExportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Export Back Art Library",
                FileName = "BackArtLibrary.zip"
            };
            if (dialog.ShowDialog() != true) return;

            StatusLabel.Text = "Exporting library...";
            await Task.Run(() => _library.ExportToZip(dialog.FileName,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Compressing {done}/{total}...")));
            StatusLabel.Text = "";
        }

        private async void OnImportZip(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ZIP Archive (*.zip)|*.zip",
                Title = "Import Back Art Library from ZIP"
            };
            if (dialog.ShowDialog() != true) return;

            StatusLabel.Text = "Importing from ZIP...";
            await Task.Run(() => _library.ImportFromZip(dialog.FileName,
                onProgress: (done, total) =>
                    Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Importing {done}/{total}...")));

            PopulateAutocomplete();
            RefreshGrid();
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
