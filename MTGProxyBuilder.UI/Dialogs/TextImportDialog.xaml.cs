using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.ViewModels;
using Serilog;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class TextImportDialog : Window
    {
        private readonly ScryfallService _scryfall;
        private readonly SearchCoordinator _searchCoordinator;
        private readonly ScryfallBulkDataService _bulkData;
        private readonly int _refreshDays;
        private List<CardModel> _importedCards = new();
        private List<string> _notFoundNames = new();

        public IReadOnlyList<CardModel> ImportedCards => _importedCards;
        public IReadOnlyList<string> NotFoundNames => _notFoundNames;

        public TextImportDialog(ScryfallService scryfall, SearchCoordinator searchCoordinator,
            ScryfallBulkDataService bulkData, int refreshDays = 1)
        {
            InitializeComponent();
            _scryfall = scryfall;
            _searchCoordinator = searchCoordinator;
            _bulkData = bulkData;
            _refreshDays = refreshDays;
        }

        private async void OnImport(object sender, RoutedEventArgs e)
        {
            string text = CardListBox.Text;
            var entries = TextCardListParser.Parse(text);

            if (entries.Count == 0)
            {
                StatusLabel.Text = "No valid card entries found.";
                return;
            }

            ImportBtn.IsEnabled = false;
            CardListBox.IsEnabled = false;
            NotFoundPanel.Visibility = Visibility.Collapsed;
            _importedCards.Clear();
            _notFoundNames.Clear();

            // Ensure bulk data is loaded
            if (!_bulkData.IsLoaded)
            {
                bool loaded = await _bulkData.EnsureLoadedAsync(_refreshDays,
                    msg => StatusLabel.Text = msg);
                if (!loaded)
                {
                    StatusLabel.Text = "Bulk data unavailable — falling back to API lookups (slower).";
                    await Task.Delay(1000);
                }
            }

            int total = entries.Count;
            int found = 0;
            int failed = 0;

            Log.Information("Text import: {Count} entries to look up (bulk data: {BulkLoaded})", total, _bulkData.IsLoaded);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                StatusLabel.Text = $"Looking up {i + 1}/{total}: {entry.Name}...";

                try
                {
                    ScryfallCard? sc = null;

                    // Try bulk data first (instant, no API call)
                    if (_bulkData.IsLoaded)
                    {
                        var bulkEntry = _bulkData.FindCard(entry.Name, entry.SetCode, entry.CollectorNumber);
                        if (bulkEntry != null)
                        {
                            // We have the Scryfall ID — fetch full card data with one API call
                            sc = await _scryfall.GetCardByIdAsync(bulkEntry.Id);
                            await Task.Delay(50); // Light rate limit for ID lookups
                        }
                    }

                    // Fallback: direct API lookup
                    if (sc == null)
                    {
                        sc = await _scryfall.GetCardByNameAsync(entry.Name);
                        await Task.Delay(75);

                        // Try specific set if needed
                        if (sc != null && !string.IsNullOrEmpty(entry.SetCode) &&
                            !sc.SetCode.Equals(entry.SetCode, StringComparison.OrdinalIgnoreCase))
                        {
                            string query = TextCardListParser.BuildScryfallQuery(entry);
                            var (results, _) = await _scryfall.SearchCardAsync(query);
                            if (results?.Count > 0) sc = results[0];
                            await Task.Delay(75);
                        }
                    }

                    if (sc != null)
                    {
                        StatusLabel.Text = $"Downloading artwork {i + 1}/{total}: {entry.Name}...";
                        var frontPath = await _searchCoordinator.DownloadScryfallArtAsync(sc);
                        string? backPath = null;
                        if (sc.GetBackImageUrl() != null)
                            backPath = await _searchCoordinator.DownloadScryfallArtAsync(sc, back: true);

                        var card = sc.ToCardModel(frontPath ?? string.Empty, backPath);
                        card.Quantity = 1;
                        for (int q = 0; q < entry.Quantity; q++)
                        {
                            if (q == 0)
                                _importedCards.Add(card);
                            else
                            {
                                var copy = new CardModel
                                {
                                    Name = card.Name, ArtworkPath = card.ArtworkPath,
                                    BackArtworkPath = card.BackArtworkPath,
                                    OriginalBackArtworkPath = card.OriginalBackArtworkPath,
                                    ScryfallId = card.ScryfallId, Quantity = 1,
                                    IncludeBack = card.IncludeBack, IsDoubleFaced = card.IsDoubleFaced,
                                    ManaCost = card.ManaCost, CMC = card.CMC,
                                    TypeLine = card.TypeLine, OracleText = card.OracleText,
                                    Rarity = card.Rarity, Colors = card.Colors,
                                    ColorIdentity = card.ColorIdentity,
                                    SetCode = card.SetCode, SetName = card.SetName,
                                    CollectorNumber = card.CollectorNumber, Artist = card.Artist,
                                    Power = card.Power, Toughness = card.Toughness,
                                    Loyalty = card.Loyalty, Keywords = card.Keywords,
                                    BackName = card.BackName, BackManaCost = card.BackManaCost,
                                    BackTypeLine = card.BackTypeLine, BackOracleText = card.BackOracleText,
                                    BackPower = card.BackPower, BackToughness = card.BackToughness,
                                    BackLoyalty = card.BackLoyalty, DateAdded = DateTime.Now
                                };
                                _importedCards.Add(copy);
                            }
                        }
                        found++;
                    }
                    else
                    {
                        _notFoundNames.Add(entry.Name);
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to look up card: {Name}", entry.Name);
                    _notFoundNames.Add(entry.Name);
                    failed++;
                }
            }

            StatusLabel.Text = $"Found {found} card(s)" + (failed > 0 ? $", {failed} not found" : "");

            if (_notFoundNames.Count > 0)
            {
                NotFoundPanel.Visibility = Visibility.Visible;
                NotFoundLabel.Text = $"{_notFoundNames.Count} card(s) not found:";
                NotFoundList.Text = string.Join(Environment.NewLine, _notFoundNames);
            }

            ImportBtn.IsEnabled = true;
            CardListBox.IsEnabled = true;

            if (_importedCards.Count > 0)
            {
                ImportBtn.Content = $"Add {_importedCards.Count} Card(s)";
                ImportBtn.Click -= OnImport;
                ImportBtn.Click += (_, _) => { DialogResult = true; };
            }
        }

        private void OnCopyNotFound(object sender, RoutedEventArgs e)
        {
            if (_notFoundNames.Count > 0)
            {
                Clipboard.SetText(string.Join(Environment.NewLine, _notFoundNames));
                StatusLabel.Text = "Not-found names copied to clipboard.";
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
