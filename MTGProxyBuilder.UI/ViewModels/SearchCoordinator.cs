using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using Serilog;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Coordinates Scryfall and MPCFill search operations.
    /// Uses bulk data for name resolution when available, falling back to API.
    /// </summary>
    public class SearchCoordinator
    {
        private readonly ScryfallService _scryfall;
        private readonly MpcFillService _mpcFill;
        private readonly AppSettingsService _appSettings;
        private ScryfallBulkDataService? _bulkData;

        public MpcFillSourceManager SourceManager { get; }

        public SearchCoordinator(
            ScryfallService scryfall,
            MpcFillService mpcFill,
            AppSettingsService appSettings,
            MpcFillSourceManager sourceManager)
        {
            _scryfall = scryfall;
            _mpcFill = mpcFill;
            _appSettings = appSettings;
            SourceManager = sourceManager;
        }

        public void SetBulkDataService(ScryfallBulkDataService bulkData)
        {
            _bulkData = bulkData;
        }

        public MpcFillSearchOptions BuildSearchOptions(int minDpi, bool fuzzySearch)
        {
            var opts = MpcFillSearchOptions.FromSettings(_appSettings.Settings);
            opts.MinimumDpi = minDpi;
            opts.FuzzySearch = fuzzySearch;
            return opts;
        }

        public object[][]? GetSources(bool useFavoritesOnly)
        {
            return useFavoritesOnly && SourceManager.HasFavorites
                ? SourceManager.BuildFavoritesArray()
                : null;
        }

        /// <summary>
        /// Resolves a card by name, optionally with set code and collector number.
        /// Uses bulk data index first (instant, no API call), falls back to API.
        /// </summary>
        public async Task<ScryfallCard?> ResolveCardAsync(string name, string? setCode = null, string? collectorNumber = null)
        {
            // Try bulk data first
            if (_bulkData?.IsLoaded == true)
            {
                var bulkEntry = _bulkData.FindCard(name, setCode, collectorNumber);
                if (bulkEntry != null)
                {
                    var card = await _scryfall.GetCardByIdAsync(bulkEntry.Id);
                    if (card != null) return card;
                }
            }

            // Fallback: API name lookup
            var sc = await _scryfall.GetCardByNameAsync(name);
            return sc;
        }

        public async Task<(List<ScryfallCard> Results, string? Error)> SearchScryfallAsync(string query)
        {
            Log.Information("SearchCoordinator: Scryfall search for {Query}", query);

            // For exact name searches (!"name"), try bulk data first to get the ID
            if (_bulkData?.IsLoaded == true && query.StartsWith("!\"") && query.EndsWith("\""))
            {
                string name = query[2..^1];
                var entries = _bulkData.SearchByName(name, 50)
                    .Where(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (entries.Count > 0)
                {
                    // Fetch full card data for each unique entry via ID lookups
                    var cards = new List<ScryfallCard>();
                    foreach (var entry in entries)
                    {
                        var card = await _scryfall.GetCardByIdAsync(entry.Id);
                        if (card != null) cards.Add(card);
                        if (cards.Count >= 50) break;
                        await Task.Delay(50);
                    }
                    if (cards.Count > 0)
                        return (cards, null);
                }
            }

            // Fallback: standard API search
            var (results, error) = await _scryfall.SearchCardAsync(query);
            return (results?.Take(50).ToList() ?? new(), error);
        }

        public async Task<(List<MpcFillCard> Results, string? Error)> SearchMpcFillAsync(
            string query, int minDpi, bool fuzzySearch, bool useFavoritesOnly,
            string? nameFilter = null)
        {
            Log.Information("SearchCoordinator: MPCFill search for {Query} (minDpi={MinDpi}, fuzzy={Fuzzy})", query, minDpi, fuzzySearch);
            var opts = BuildSearchOptions(minDpi, fuzzySearch);
            var sources = GetSources(useFavoritesOnly);
            var (results, error) = await _mpcFill.SearchAsync(
                query, 50, minDpi, fuzzySearch, sources, maxResults: 50, options: opts);

            if (error != null)
                return (new(), error);

            var filtered = results.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(nameFilter))
                filtered = filtered.Where(c => c.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

            return (filtered.ToList(), null);
        }

        public async Task<string?> DownloadScryfallArtAsync(ScryfallCard card, bool back = false)
        {
            Log.Information("Downloading Scryfall art for {CardName} (back={Back})", card.Name, back);
            return await _scryfall.DownloadAndCacheImageAsync(card, back: back);
        }

        public async Task<string?> DownloadMpcFillArtAsync(MpcFillCard card)
        {
            return await _mpcFill.DownloadAndCacheImageAsync(card);
        }

        public async Task<(List<MpcFillCard> Results, string? Error)> SearchMpcFillForCard(
            string cardName, int minDpi, bool fuzzySearch, bool useFavoritesOnly)
        {
            var opts = BuildSearchOptions(minDpi, fuzzySearch);
            var sources = GetSources(useFavoritesOnly);
            return await _mpcFill.SearchAsync(
                cardName, 10, minDpi, fuzzySearch, sources, maxResults: 10, options: opts);
        }

        public ScryfallService Scryfall => _scryfall;
        public MpcFillService MpcFill => _mpcFill;
    }
}
