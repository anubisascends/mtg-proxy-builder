namespace MTGProxyBuilder.Core.Services
{
    public enum DeckSource { Unknown, Moxfield, Archidekt }

    public class DeckImportEntry
    {
        public string CardName { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string? ScryfallId { get; set; }
        public string Board { get; set; } = string.Empty;
    }

    public class ImportedDeck
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public DeckSource Source { get; set; }
        public List<DeckImportEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Auto-detects the deck source from a URL and fetches the deck list.
    /// </summary>
    public class DeckImportService
    {
        private readonly MoxfieldService _moxfield;
        private readonly ArchidektService _archidekt;

        public DeckImportService(MoxfieldService moxfield, ArchidektService archidekt)
        {
            _moxfield = moxfield;
            _archidekt = archidekt;
        }

        public static DeckSource DetectSource(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return DeckSource.Unknown;
            url = url.Trim().ToLowerInvariant();

            if (url.Contains("moxfield.com")) return DeckSource.Moxfield;
            if (url.Contains("archidekt.com")) return DeckSource.Archidekt;

            return DeckSource.Unknown;
        }

        public async Task<(ImportedDeck? Deck, string? Error)> ImportAsync(string url)
        {
            var source = DetectSource(url);

            switch (source)
            {
                case DeckSource.Moxfield:
                {
                    string? deckId = MoxfieldService.ParseDeckId(url);
                    if (string.IsNullOrEmpty(deckId))
                        return (null, "Could not extract deck ID from Moxfield URL.");

                    var (deck, error) = await _moxfield.FetchDeckAsync(deckId);
                    if (deck == null) return (null, error);

                    return (new ImportedDeck
                    {
                        Name = deck.Name,
                        Format = deck.Format,
                        Source = DeckSource.Moxfield,
                        Entries = deck.Entries.Select(e => new DeckImportEntry
                        {
                            CardName = e.CardName,
                            Quantity = e.Quantity,
                            ScryfallId = e.ScryfallId,
                            Board = e.Board
                        }).ToList()
                    }, null);
                }

                case DeckSource.Archidekt:
                {
                    string? deckId = ArchidektService.ParseDeckId(url);
                    if (string.IsNullOrEmpty(deckId))
                        return (null, "Could not extract deck ID from Archidekt URL.");

                    var (deck, error) = await _archidekt.FetchDeckAsync(deckId);
                    if (deck == null) return (null, error);

                    return (new ImportedDeck
                    {
                        Name = deck.Name,
                        Format = deck.Format,
                        Source = DeckSource.Archidekt,
                        Entries = deck.Entries.Select(e => new DeckImportEntry
                        {
                            CardName = e.CardName,
                            Quantity = e.Quantity,
                            ScryfallId = e.ScryfallId,
                            Board = e.Category
                        }).ToList()
                    }, null);
                }

                default:
                    return (null, "Unrecognized URL. Paste a deck URL from Moxfield or Archidekt.");
            }
        }
    }
}
