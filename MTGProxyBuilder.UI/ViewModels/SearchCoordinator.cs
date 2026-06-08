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
    /// Extracted from MainViewModel to isolate search logic.
    /// </summary>
    public class SearchCoordinator
    {
        private readonly ScryfallService _scryfall;
        private readonly MpcFillService _mpcFill;
        private readonly AppSettingsService _appSettings;

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

        public async Task<(List<ScryfallCard> Results, string? Error)> SearchScryfallAsync(string query)
        {
            Log.Information("SearchCoordinator: Scryfall search for {Query}", query);
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
