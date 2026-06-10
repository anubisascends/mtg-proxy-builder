using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Coordinates deck import and MPCFill XML import operations.
    /// Extracted from MainViewModel to isolate import logic.
    /// </summary>
    public class ImportCoordinator
    {
        private readonly SearchCoordinator _search;
        private readonly DeckImportService _deckImport;
        private readonly MpcFillXmlImportService _xmlImport;
        private readonly FrontArtLibraryService _frontLibrary;

        public ImportCoordinator(
            SearchCoordinator search,
            DeckImportService deckImport,
            MpcFillXmlImportService xmlImport,
            FrontArtLibraryService frontLibrary)
        {
            _search = search;
            _deckImport = deckImport;
            _xmlImport = xmlImport;
            _frontLibrary = frontLibrary;
        }

        private static readonly HashSet<string> BasicLands = new(StringComparer.OrdinalIgnoreCase)
            { "Plains", "Island", "Swamp", "Mountain", "Forest",
              "Wastes", "Snow-Covered Plains", "Snow-Covered Island",
              "Snow-Covered Swamp", "Snow-Covered Mountain", "Snow-Covered Forest" };

        public static bool IsBasicLand(string name) => BasicLands.Contains(name);

        // ================================================================
        //  DECK IMPORT
        // ================================================================

        public record DeckImportResult(
            List<CardModel> Cards,
            string DeckName,
            string SourceName,
            int SkippedDupes,
            int Failed);

        public async Task<(ImportedDeck? Deck, string? Error)> FetchDeckAsync(string url)
        {
            return await _deckImport.ImportAsync(url);
        }

        public async Task<DeckImportResult> ImportDeckCardsAsync(
            ImportedDeck deck,
            IEnumerable<CardModel> existingCards,
            bool ignoreDuplicates,
            bool useMpcFill,
            int minDpi,
            bool fuzzySearch,
            bool useFavoritesOnly,
            Action<string>? onProgress = null)
        {
            var importedCards = new List<CardModel>();
            int failed = 0, skippedDupes = 0;

            var existingByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (ignoreDuplicates)
            {
                foreach (var c in existingCards)
                {
                    if (existingByName.ContainsKey(c.Name))
                        existingByName[c.Name] += c.Quantity;
                    else
                        existingByName[c.Name] = c.Quantity;
                }
            }

            for (int i = 0; i < deck.Entries.Count; i++)
            {
                var entry = deck.Entries[i];

                if (ignoreDuplicates && existingByName.ContainsKey(entry.CardName))
                {
                    if (IsBasicLand(entry.CardName))
                    {
                        int have = existingByName[entry.CardName];
                        if (entry.Quantity <= have) { skippedDupes++; continue; }
                        entry = new DeckImportEntry
                        {
                            CardName = entry.CardName,
                            Quantity = entry.Quantity - have,
                            ScryfallId = entry.ScryfallId,
                            Board = entry.Board
                        };
                    }
                    else { skippedDupes++; continue; }
                }

                onProgress?.Invoke($"Looking up card {i + 1}/{deck.Entries.Count}: {entry.CardName}" +
                    (entry.Quantity > 1 ? $" (x{entry.Quantity})" : "") + "...");

                ScryfallCard? scryfallCard = null;
                if (!string.IsNullOrEmpty(entry.ScryfallId))
                    scryfallCard = await _search.Scryfall.GetCardByIdAsync(entry.ScryfallId);
                if (scryfallCard == null)
                    scryfallCard = await _search.ResolveCardAsync(entry.CardName);
                if (scryfallCard == null) { failed++; continue; }

                onProgress?.Invoke($"Downloading artwork {i + 1}/{deck.Entries.Count}: {entry.CardName}...");

                string? frontPath = null;
                string? backPath = null;

                if (useMpcFill)
                {
                    var (mpcResults, _) = await _search.SearchMpcFillForCard(
                        entry.CardName, minDpi, fuzzySearch, useFavoritesOnly);
                    var bestMatch = mpcResults.FirstOrDefault(mc =>
                        mc.Name.Contains(entry.CardName, StringComparison.OrdinalIgnoreCase));
                    if (bestMatch != null)
                        frontPath = await _search.DownloadMpcFillArtAsync(bestMatch);
                    if (frontPath == null)
                        frontPath = await _search.DownloadScryfallArtAsync(scryfallCard);
                }
                else
                {
                    frontPath = await _search.DownloadScryfallArtAsync(scryfallCard);
                }

                if (scryfallCard.GetBackImageUrl() != null)
                    backPath = await _search.DownloadScryfallArtAsync(scryfallCard, back: true);

                var card = scryfallCard.ToCardModel(frontPath ?? string.Empty, backPath);
                card.Quantity = entry.Quantity;
                importedCards.Add(card);

                if (ignoreDuplicates)
                {
                    if (existingByName.ContainsKey(entry.CardName))
                        existingByName[entry.CardName] += entry.Quantity;
                    else
                        existingByName[entry.CardName] = entry.Quantity;
                }

                await Task.Delay(100);
            }

            return new DeckImportResult(importedCards, deck.Name, "", skippedDupes, failed);
        }

        // ================================================================
        //  MPCFILL XML IMPORT
        // ================================================================

        public record XmlImportResult(List<CardModel> Cards, int Downloaded, int Failed);

        public (MpcFillXmlProject? Project, string? Error) ParseXml(string filePath)
        {
            return _xmlImport.ParseXml(filePath);
        }

        public async Task<XmlImportResult> ImportXmlCardsAsync(
            MpcFillXmlProject project,
            Action<string>? onProgress = null)
        {
            var backsBySlot = new Dictionary<int, MpcFillXmlCard>();
            foreach (var back in project.Backs)
                foreach (var slot in back.Slots)
                    backsBySlot[slot] = back;

            var importedCards = new List<CardModel>();
            int downloaded = 0, failed = 0;

            for (int i = 0; i < project.Fronts.Count; i++)
            {
                var front = project.Fronts[i];
                string cardName = MpcFillXmlImportService.CleanCardName(front);
                int quantity = front.Slots.Count;

                onProgress?.Invoke($"Downloading {i + 1}/{project.Fronts.Count}: {cardName}" +
                    (quantity > 1 ? $" (x{quantity})" : "") + "...");

                string? frontPath = null;
                if (!string.IsNullOrEmpty(front.Id))
                    frontPath = await _xmlImport.DownloadImageByIdAsync(front.Id);
                if (frontPath == null) { failed++; continue; }

                string? backPath = null;
                var firstSlot = front.Slots.FirstOrDefault();
                if (backsBySlot.TryGetValue(firstSlot, out var backCard) && !string.IsNullOrEmpty(backCard.Id))
                {
                    onProgress?.Invoke($"Downloading back for: {cardName}...");
                    backPath = await _xmlImport.DownloadImageByIdAsync(backCard.Id);
                }
                if (backPath == null && !string.IsNullOrEmpty(project.CommonCardbackId))
                    backPath = await _xmlImport.DownloadImageByIdAsync(project.CommonCardbackId);

                var card = new CardModel
                {
                    Name = cardName,
                    ArtworkPath = frontPath,
                    BackArtworkPath = backPath,
                    IncludeBack = backPath != null,
                    Quantity = quantity,
                    DateAdded = DateTime.Now
                };

                importedCards.Add(card);
                downloaded++;
                await Task.Delay(50);
            }

            return new XmlImportResult(importedCards, downloaded, failed);
        }

        // ================================================================
        //  MPCFILL CARD ADDITION
        // ================================================================

        public async Task<(CardModel? Card, string? Error)> AddMpcFillCardAsync(MpcFillCard mpcCard)
        {
            var path = await _search.DownloadMpcFillArtAsync(mpcCard);

            if (path != null)
            {
                string libName = $"{mpcCard.Name} [{mpcCard.Source}]";
                var entry = _frontLibrary.AddFromFile(path, libName, mpcCard.Source);
                if (entry != null)
                    _frontLibrary.ApplyMpcFillDefaults(entry.Id, mpcCard.Source);
            }

            var card = new CardModel
            {
                Name = mpcCard.Name.Split('(')[0].Trim(),
                ArtworkPath = path ?? string.Empty,
                Artist = mpcCard.Source,
                DateAdded = DateTime.Now
            };
            return (card, null);
        }

        public async Task<(int Updated, int Failed)> UpdateAllArtFromMpcFillAsync(
            IList<CardModel> cards, int minDpi, bool fuzzySearch, bool useFavoritesOnly,
            Action<string>? onProgress = null)
        {
            int updated = 0, failed = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                onProgress?.Invoke($"Searching MPCFill {i + 1}/{cards.Count}: {card.Name}...");

                var (results, error) = await _search.SearchMpcFillForCard(
                    card.Name, minDpi, fuzzySearch, useFavoritesOnly);
                if (error != null || results.Count == 0) { failed++; continue; }

                onProgress?.Invoke($"Downloading art {i + 1}/{cards.Count}: {card.Name}...");
                var path = await _search.DownloadMpcFillArtAsync(results[0]);
                if (path != null) { card.ArtworkPath = path; updated++; }
                else { failed++; }

                await Task.Delay(50);
            }
            return (updated, failed);
        }
    }
}
