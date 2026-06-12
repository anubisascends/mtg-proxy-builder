# Riftbound / Piltover Archive Import — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import Riftbound TCG decks from piltoverarchive.com — both via URL and via clipboard-pasted deck screenshots — adding each card as a proxy-ready image in the project.

**Architecture:** A new `PiltoverArchiveService` parses deck data from the site's server-rendered HTML (React Server Components payload) and downloads card art from BunnyCDN (`cdn.piltoverarchive.com`). The existing `ImportCoordinator` gains a `ImportRiftboundCardsAsync` method that mirrors the Moxfield/Archidekt flow. The `DeckImportService` is extended to detect `piltoverarchive.com` URLs. Ctrl+V in `MainWindow` is enhanced to auto-detect piltoverarchive URLs on the clipboard and trigger import. A separate image-based import path crops individual card cells from deck screenshot grids for use as proxy art.

**Tech Stack:** C# / .NET 10 / WPF, HttpClient, Regex (RSC payload parsing), System.Text.Json, System.Windows.Media.Imaging (image cropping)

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `MTGProxyBuilder.Core/Models/RiftboundModels.cs` | Data models for Piltover Archive deck/card JSON |
| Create | `MTGProxyBuilder.Core/Services/PiltoverArchiveService.cs` | Fetch deck HTML, parse RSC payload, download card images |
| Modify | `MTGProxyBuilder.Core/Services/DeckImportService.cs` | Add `PiltoverArchive` to `DeckSource` enum and URL detection |
| Modify | `MTGProxyBuilder.UI/ViewModels/ImportCoordinator.cs` | Add `ImportRiftboundCardsAsync` method |
| Modify | `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs` | Add Riftbound import command + wire UI |
| Modify | `MTGProxyBuilder.UI/MainWindow.xaml` | Import sidebar section + Cards menu item |
| Modify | `MTGProxyBuilder.UI/MainWindow.xaml.cs` | Enhance Ctrl+V to detect piltoverarchive URLs |
| Create | `MTGProxyBuilder.Tests/Services/PiltoverArchiveServiceTests.cs` | Unit tests for HTML parsing + model deserialization |
| Create | `MTGProxyBuilder.Tests/Services/RiftboundImportTests.cs` | Integration tests for full import flow |

---

### Task 1: Riftbound Data Models

**Files:**
- Create: `MTGProxyBuilder.Core/Models/RiftboundModels.cs`

- [ ] **Step 1: Create the Riftbound model classes**

These map directly to the JSON structure found in the piltoverarchive.com RSC payload.

```csharp
using System.Text.Json.Serialization;

namespace MTGProxyBuilder.Core.Models
{
    public class RiftboundDeck
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("authorName")]
        public string AuthorName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("legend")]
        public RiftboundDeckCard? Legend { get; set; }

        [JsonPropertyName("champions")]
        public List<RiftboundDeckCard> Champions { get; set; } = new();

        [JsonPropertyName("battlefields")]
        public List<RiftboundDeckCard> Battlefields { get; set; } = new();

        [JsonPropertyName("runes")]
        public List<RiftboundDeckCard> Runes { get; set; } = new();

        [JsonPropertyName("maindeck")]
        public List<RiftboundDeckCard> Maindeck { get; set; } = new();

        [JsonPropertyName("sideboard")]
        public List<RiftboundDeckCard> Sideboard { get; set; } = new();

        [JsonPropertyName("bench")]
        public List<RiftboundDeckCard> Bench { get; set; } = new();

        /// <summary>Returns all deck cards across all sections.</summary>
        public IEnumerable<RiftboundDeckCard> AllCards()
        {
            if (Legend != null) yield return Legend;
            foreach (var c in Champions) yield return c;
            foreach (var c in Battlefields) yield return c;
            foreach (var c in Runes) yield return c;
            foreach (var c in Maindeck) yield return c;
            foreach (var c in Sideboard) yield return c;
            foreach (var c in Bench) yield return c;
        }
    }

    public class RiftboundDeckCard
    {
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;

        [JsonPropertyName("card")]
        public RiftboundCard Card { get; set; } = new();

        [JsonPropertyName("variantId")]
        public string VariantId { get; set; } = string.Empty;
    }

    public class RiftboundCard
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("super")]
        public string? Super { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("energy")]
        public int Energy { get; set; }

        [JsonPropertyName("might")]
        public int Might { get; set; }

        [JsonPropertyName("power")]
        public int Power { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("cardVariants")]
        public List<RiftboundCardVariant> CardVariants { get; set; } = new();

        [JsonPropertyName("cardColors")]
        public List<RiftboundCardColor> CardColors { get; set; } = new();
    }

    public class RiftboundCardVariant
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("variantNumber")]
        public string VariantNumber { get; set; } = string.Empty;

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = string.Empty;

        [JsonPropertyName("rarity")]
        public string Rarity { get; set; } = string.Empty;

        [JsonPropertyName("flavorText")]
        public string? FlavorText { get; set; }

        [JsonPropertyName("artist")]
        public string? Artist { get; set; }
    }

    public class RiftboundCardColor
    {
        [JsonPropertyName("color")]
        public RiftboundColor Color { get; set; } = new();
    }

    public class RiftboundColor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build MTGProxyBuilder.Core`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Models/RiftboundModels.cs
git commit -m "feat: add Riftbound data models for Piltover Archive deck import"
```

---

### Task 2: PiltoverArchiveService — Fetch & Parse

**Files:**
- Create: `MTGProxyBuilder.Core/Services/PiltoverArchiveService.cs`
- Create: `MTGProxyBuilder.Tests/Services/PiltoverArchiveServiceTests.cs`

- [ ] **Step 1: Write tests for URL parsing and RSC payload extraction**

```csharp
using MTGProxyBuilder.Core.Services;
using Xunit;

namespace MTGProxyBuilder.Tests.Services
{
    public class PiltoverArchiveServiceTests
    {
        [Theory]
        [InlineData("https://piltoverarchive.com/decks/view/0741d662-e31b-4999-b1f8-96d89d085423",
                     "0741d662-e31b-4999-b1f8-96d89d085423")]
        [InlineData("https://piltoverarchive.com/decks/view/abc-123/",
                     "abc-123")]
        [InlineData("https://piltoverarchive.com/decks/view/abc-123?tab=overview",
                     "abc-123")]
        public void ParseDeckId_ExtractsId(string url, string expected)
        {
            Assert.Equal(expected, PiltoverArchiveService.ParseDeckId(url));
        }

        [Theory]
        [InlineData("https://moxfield.com/decks/abc")]
        [InlineData("not a url")]
        [InlineData("")]
        public void ParseDeckId_ReturnsNull_ForInvalidUrls(string url)
        {
            Assert.Null(PiltoverArchiveService.ParseDeckId(url));
        }

        [Fact]
        public void ExtractDeckJson_ParsesRscPayload()
        {
            // Minimal RSC-style HTML with embedded deck data
            string html = """
                <html><body>
                <script>self.__next_f.push([1,"99:{\"deck\":{\"id\":\"test-id\",\"name\":\"Test Deck\",\"authorName\":\"tester\",\"description\":\"\",\"legend\":null,\"champions\":[],\"battlefields\":[],\"runes\":[],\"maindeck\":[{\"quantity\":3,\"variantId\":\"v1\",\"card\":{\"id\":\"c1\",\"name\":\"Test Card\",\"type\":\"Unit\",\"super\":null,\"description\":\"Does stuff\",\"energy\":2,\"might\":1,\"power\":1,\"tags\":null,\"cardVariants\":[{\"id\":\"v1\",\"variantNumber\":\"OGN-001\",\"imageUrl\":\"https://cdn.piltoverarchive.com/cards/OGN-001.webp\",\"rarity\":\"Common\",\"flavorText\":null,\"artist\":null}],\"cardColors\":[]}}],\"sideboard\":[],\"bench\":[]}}\n"])</script>
                </body></html>
                """;

            var deck = PiltoverArchiveService.ExtractDeckFromHtml(html);

            Assert.NotNull(deck);
            Assert.Equal("test-id", deck!.Id);
            Assert.Equal("Test Deck", deck.Name);
            Assert.Single(deck.Maindeck);
            Assert.Equal("Test Card", deck.Maindeck[0].Card.Name);
            Assert.Equal(3, deck.Maindeck[0].Quantity);
            Assert.Equal("OGN-001", deck.Maindeck[0].Card.CardVariants[0].VariantNumber);
        }

        [Fact]
        public void ExtractDeckJson_ReturnsNull_WhenNoDeckData()
        {
            string html = "<html><body><p>No deck here</p></body></html>";
            Assert.Null(PiltoverArchiveService.ExtractDeckFromHtml(html));
        }

        [Fact]
        public void GetCardImageUrl_ReturnsVariantUrl()
        {
            var card = new Models.RiftboundDeckCard
            {
                VariantId = "v1",
                Card = new Models.RiftboundCard
                {
                    CardVariants = new()
                    {
                        new Models.RiftboundCardVariant
                        {
                            Id = "v1",
                            ImageUrl = "https://cdn.piltoverarchive.com/cards/OGN-042.webp"
                        }
                    }
                }
            };

            Assert.Equal("https://cdn.piltoverarchive.com/cards/OGN-042.webp",
                PiltoverArchiveService.GetCardImageUrl(card));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test MTGProxyBuilder.Tests --filter "FullyQualifiedName~PiltoverArchiveServiceTests" -v q`
Expected: FAIL — `PiltoverArchiveService` does not exist

- [ ] **Step 3: Implement PiltoverArchiveService**

```csharp
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MTGProxyBuilder.Core.Models;
using Serilog;

namespace MTGProxyBuilder.Core.Services
{
    public class PiltoverArchiveService
    {
        private readonly HttpClient _httpClient;
        private readonly ImageCacheService _imageCache;

        public PiltoverArchiveService(ImageCacheService imageCache)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _imageCache = imageCache;
        }

        /// <summary>Extract deck UUID from a piltoverarchive.com URL.</summary>
        public static string? ParseDeckId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var match = Regex.Match(url.Trim(),
                @"piltoverarchive\.com/decks/view/([a-f0-9\-]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Fetch the deck page and parse the embedded deck data.</summary>
        public async Task<(RiftboundDeck? Deck, string? Error)> FetchDeckAsync(string url)
        {
            string? deckId = ParseDeckId(url);
            if (deckId == null)
                return (null, "Could not extract deck ID from Piltover Archive URL.");

            try
            {
                string pageUrl = $"https://piltoverarchive.com/decks/view/{deckId}";
                var html = await _httpClient.GetStringAsync(pageUrl);

                var deck = ExtractDeckFromHtml(html);
                if (deck == null)
                    return (null, "Could not parse deck data from page. The page format may have changed.");

                return (deck, null);
            }
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "Failed to fetch Piltover Archive deck {DeckId}", deckId);
                return (null, $"HTTP error fetching deck: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse the React Server Components payload from the HTML to extract deck JSON.
        /// The deck data is serialized inside self.__next_f.push() script blocks.
        /// </summary>
        public static RiftboundDeck? ExtractDeckFromHtml(string html)
        {
            // Collect all RSC chunks from self.__next_f.push([1,"..."]) calls
            var chunkPattern = new Regex(
                @"self\.__next_f\.push\(\[1,\s*""(.*?)""\]\)",
                RegexOptions.Singleline);

            var sb = new System.Text.StringBuilder();
            foreach (Match m in chunkPattern.Matches(html))
            {
                // Unescape the JS string (the content is JSON-escaped inside a JS string)
                string chunk = m.Groups[1].Value
                    .Replace("\\n", "\n")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\/", "/");
                sb.Append(chunk);
            }

            string payload = sb.ToString();

            // Find the deck JSON object — look for "deck":{ pattern and extract the object
            var deckMatch = Regex.Match(payload,
                @"""deck""\s*:\s*(\{.*?""bench""\s*:\s*\[.*?\]\s*\})",
                RegexOptions.Singleline);
            if (!deckMatch.Success) return null;

            try
            {
                string deckJson = deckMatch.Groups[1].Value;
                return JsonSerializer.Deserialize<RiftboundDeck>(deckJson);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to deserialize Riftbound deck JSON");
                return null;
            }
        }

        /// <summary>Get the image URL for a deck card entry, matching on variantId.</summary>
        public static string? GetCardImageUrl(RiftboundDeckCard deckCard)
        {
            // Prefer the specific variant selected in the deck
            var variant = deckCard.Card.CardVariants
                .FirstOrDefault(v => v.Id == deckCard.VariantId)
                ?? deckCard.Card.CardVariants.FirstOrDefault();

            return variant?.ImageUrl;
        }

        /// <summary>Download a card image to the local cache. Returns the cached file path.</summary>
        public async Task<string?> DownloadCardImageAsync(RiftboundDeckCard deckCard)
        {
            string? imageUrl = GetCardImageUrl(deckCard);
            if (string.IsNullOrEmpty(imageUrl)) return null;

            // Use variant number as cache key (e.g. "OGN-042")
            var variant = deckCard.Card.CardVariants
                .FirstOrDefault(v => v.Id == deckCard.VariantId)
                ?? deckCard.Card.CardVariants.FirstOrDefault();
            string cacheKey = $"rb_{variant?.VariantNumber ?? deckCard.Card.Id}";

            var existing = _imageCache.GetCachedImagePath(cacheKey);
            if (existing != null) return existing;

            return await _imageCache.CacheImageFromUrlAsync(_httpClient, imageUrl, cacheKey);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test MTGProxyBuilder.Tests --filter "FullyQualifiedName~PiltoverArchiveServiceTests" -v q`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add MTGProxyBuilder.Core/Services/PiltoverArchiveService.cs MTGProxyBuilder.Tests/Services/PiltoverArchiveServiceTests.cs
git commit -m "feat: add PiltoverArchiveService for Riftbound deck fetching and parsing"
```

---

### Task 3: Extend DeckImportService with Piltover Archive Detection

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/DeckImportService.cs`
- Modify: `MTGProxyBuilder.Tests/Services/DeckImportTests.cs`

- [ ] **Step 1: Write test for URL detection**

Add to the existing `DeckImportTests.cs` (or create `RiftboundImportTests.cs`):

```csharp
[Theory]
[InlineData("https://piltoverarchive.com/decks/view/0741d662-e31b-4999-b1f8-96d89d085423")]
[InlineData("https://www.piltoverarchive.com/decks/view/abc-123")]
public void DetectSource_Riftbound(string url)
{
    Assert.Equal(DeckSource.PiltoverArchive, DeckImportService.DetectSource(url));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test MTGProxyBuilder.Tests --filter "DetectSource_Riftbound" -v q`
Expected: FAIL — `DeckSource.PiltoverArchive` does not exist

- [ ] **Step 3: Add PiltoverArchive to DeckSource and DetectSource**

In `DeckImportService.cs`:

```csharp
public enum DeckSource { Unknown, Moxfield, Archidekt, PiltoverArchive }
```

In `DetectSource()`, add before the `return DeckSource.Unknown;`:

```csharp
if (url.Contains("piltoverarchive.com")) return DeckSource.PiltoverArchive;
```

Update the error message in `ImportAsync` default case:

```csharp
default:
    return (null, "Unrecognized URL. Paste a deck URL from Moxfield, Archidekt, or Piltover Archive.");
```

Note: The existing `ImportAsync` method returns `ImportedDeck` which is Scryfall-centric. Riftbound import will bypass this method entirely and go through `ImportCoordinator.ImportRiftboundCardsAsync()` directly, since Riftbound cards don't exist on Scryfall. `DetectSource()` is still used by the UI to route the import.

- [ ] **Step 4: Run tests**

Run: `dotnet test MTGProxyBuilder.Tests --filter "DetectSource" -v q`
Expected: All PASS (existing + new)

- [ ] **Step 5: Commit**

```bash
git add MTGProxyBuilder.Core/Services/DeckImportService.cs MTGProxyBuilder.Tests/Services/DeckImportTests.cs
git commit -m "feat: add PiltoverArchive to DeckSource URL detection"
```

---

### Task 4: ImportCoordinator — Riftbound Import Method

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/ImportCoordinator.cs`

- [ ] **Step 1: Add PiltoverArchiveService field and constructor parameter**

Add to the fields at the top of ImportCoordinator:

```csharp
private readonly PiltoverArchiveService _piltoverArchive;
```

Update the constructor signature and body:

```csharp
public ImportCoordinator(
    SearchCoordinator search,
    DeckImportService deckImport,
    MpcFillXmlImportService xmlImport,
    FrontArtLibraryService frontLibrary,
    PiltoverArchiveService piltoverArchive)
{
    _search = search;
    _deckImport = deckImport;
    _xmlImport = xmlImport;
    _frontLibrary = frontLibrary;
    _piltoverArchive = piltoverArchive;
}
```

- [ ] **Step 2: Add FetchRiftboundDeckAsync and ImportRiftboundCardsAsync methods**

Add a new section after the `MPCFILL CARD ADDITION` section:

```csharp
// ================================================================
//  RIFTBOUND / PILTOVER ARCHIVE IMPORT
// ================================================================

public record RiftboundImportResult(
    List<CardModel> Cards,
    string DeckName,
    int Downloaded,
    int Failed);

public async Task<(RiftboundDeck? Deck, string? Error)> FetchRiftboundDeckAsync(string url)
{
    return await _piltoverArchive.FetchDeckAsync(url);
}

public async Task<RiftboundImportResult> ImportRiftboundCardsAsync(
    RiftboundDeck deck,
    Action<string>? onProgress = null)
{
    var importedCards = new List<CardModel>();
    int downloaded = 0, failed = 0;

    var allCards = deck.AllCards().ToList();

    for (int i = 0; i < allCards.Count; i++)
    {
        var entry = allCards[i];
        string cardName = entry.Card.Name;

        onProgress?.Invoke($"Downloading {i + 1}/{allCards.Count}: {cardName}" +
            (entry.Quantity > 1 ? $" (x{entry.Quantity})" : "") + "...");

        string? artPath = await _piltoverArchive.DownloadCardImageAsync(entry);
        if (artPath == null)
        {
            failed++;
            continue;
        }

        string colors = string.Join(", ",
            entry.Card.CardColors?.Select(cc => cc.Color.Name) ?? Enumerable.Empty<string>());
        string tags = string.Join(", ", entry.Card.Tags ?? new List<string>());

        var variant = entry.Card.CardVariants
            .FirstOrDefault(v => v.Id == entry.VariantId)
            ?? entry.Card.CardVariants.FirstOrDefault();

        var card = new CardModel
        {
            Name = cardName,
            ArtworkPath = artPath,
            Quantity = entry.Quantity,
            TypeLine = entry.Card.Super != null
                ? $"{entry.Card.Super} {entry.Card.Type}"
                : entry.Card.Type,
            OracleText = entry.Card.Description,
            Rarity = variant?.Rarity ?? string.Empty,
            Artist = variant?.Artist ?? string.Empty,
            Colors = colors,
            Keywords = tags,
            CollectorNumber = variant?.VariantNumber ?? string.Empty,
            DateAdded = DateTime.Now
        };

        importedCards.Add(card);
        downloaded++;
        await Task.Delay(50);
    }

    return new RiftboundImportResult(importedCards, deck.Name, downloaded, failed);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: May fail due to constructor change — that's fine, we fix callers in Task 6.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/ImportCoordinator.cs
git commit -m "feat: add Riftbound import flow to ImportCoordinator"
```

---

### Task 5: MainViewModel — Import Commands and Wiring

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add PiltoverArchiveService field and update constructor**

Find where `_importCoordinator` is constructed (around line 160). Add a field:

```csharp
private readonly PiltoverArchiveService _piltoverArchiveService;
```

Initialize it alongside the other services (after `_imageCacheService = new ImageCacheService()`):

```csharp
_piltoverArchiveService = new PiltoverArchiveService(_imageCacheService);
```

Update the `_importCoordinator` construction to pass it:

```csharp
_importCoordinator = new ImportCoordinator(
    _searchCoordinator, _deckImportService, _mpcXmlImportService,
    _frontArtLibraryService, _piltoverArchiveService);
```

- [ ] **Step 2: Add the ImportRiftboundDeck command and property**

Near the other import command properties, add:

```csharp
public ICommand ImportRiftboundDeckCommand { get; private set; } = null!;
```

Near the other import URL properties, add:

```csharp
private string _riftboundImportUrl = string.Empty;
public string RiftboundImportUrl
{
    get => _riftboundImportUrl;
    set { _riftboundImportUrl = value; OnPropertyChanged(); }
}
```

In the constructor where commands are initialized (search for `ImportMpcFillXmlCommand = new RelayCommand`), add:

```csharp
ImportRiftboundDeckCommand = new RelayCommand(_ => ImportRiftboundDeck());
```

- [ ] **Step 3: Add ImportRiftboundDeck method**

Add this near the existing `ImportDeck()` method:

```csharp
// --- Riftbound / Piltover Archive Import ---

private async void ImportRiftboundDeck(string? urlOverride = null)
{
    string url = urlOverride ?? RiftboundImportUrl;
    if (string.IsNullOrWhiteSpace(url))
    {
        MessageBox.Show("Paste a Piltover Archive deck URL first.",
            "No URL", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    var deckId = PiltoverArchiveService.ParseDeckId(url);
    if (deckId == null)
    {
        MessageBox.Show(
            "Unrecognized URL. Paste a deck URL from:\n\n" +
            "- Piltover Archive (piltoverarchive.com/decks/view/...)",
            "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    SetBusy("Connecting to Piltover Archive...");
    Log.Information("Importing Riftbound deck from {Url}", url);

    try
    {
        BusyMessage = "Fetching deck from Piltover Archive...";
        await Task.Delay(50);

        var (deck, error) = await _importCoordinator.FetchRiftboundDeckAsync(url);
        if (deck == null || error != null)
        {
            ClearBusy();
            MessageBox.Show($"Failed to fetch deck:\n{error}", "Piltover Archive Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        PushUndo();
        var allCards = deck.AllCards().ToList();
        int totalQty = allCards.Sum(c => c.Quantity);
        BusyMessage = $"Found deck: {deck.Name}\n{allCards.Count} unique cards, {totalQty} total";
        await Task.Delay(800);

        var result = await _importCoordinator.ImportRiftboundCardsAsync(
            deck, onProgress: msg => BusyMessage = msg);

        BusyMessage = $"Adding {result.Cards.Count} cards to project...";
        await Task.Delay(50);

        Cards.CollectionChanged -= OnCardsCollectionChanged;
        foreach (var c in result.Cards)
        {
            ApplyDefaultBackArt(c);
            Cards.Add(c);
        }
        Cards.CollectionChanged += OnCardsCollectionChanged;

        _currentProject.PageSettings.CenterGrid();
        ApplyFilterAndSort();

        _currentProject.DeckImportUrl = url;
        RiftboundImportUrl = string.Empty;

        int totalAdded = result.Cards.Sum(c => c.Quantity);
        string summary = $"Imported {result.Cards.Count} unique card(s) ({totalAdded} total) from \"{deck.Name}\" (Piltover Archive)";
        if (result.Failed > 0) summary += $"\n{result.Failed} card(s) could not be downloaded";
        StatusText = summary;

        MessageBox.Show(summary, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        StatusText = $"Import failed: {ex.Message}";
        MessageBox.Show($"Import error:\n{ex.Message}", "Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
    finally
    {
        ClearBusy();
    }
}

/// <summary>Called from clipboard paste when a piltoverarchive.com URL is detected.</summary>
public void ImportRiftboundFromUrl(string url)
{
    ImportRiftboundDeck(urlOverride: url);
}
```

- [ ] **Step 4: Update existing ImportDeck to route PiltoverArchive URLs**

In the existing `ImportDeck()` method, update the source detection block at the top:

```csharp
private async void ImportDeck()
{
    var source = DeckImportService.DetectSource(ImportDeckUrl);

    // Route Piltover Archive URLs to the dedicated handler
    if (source == DeckSource.PiltoverArchive)
    {
        RiftboundImportUrl = ImportDeckUrl;
        ImportDeckUrl = string.Empty;
        ImportRiftboundDeck();
        return;
    }

    if (source == DeckSource.Unknown)
    {
        // ... existing error message, updated to include Piltover Archive ...
```

Update the existing error message to mention Piltover Archive:

```csharp
MessageBox.Show(
    "Unrecognized URL. Paste a deck URL from:\n\n" +
    "- Moxfield (moxfield.com/decks/...)\n" +
    "- Archidekt (archidekt.com/decks/...)\n" +
    "- Piltover Archive (piltoverarchive.com/decks/view/...)",
    "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/MainViewModel.cs
git commit -m "feat: add Riftbound deck import command and Piltover Archive URL routing"
```

---

### Task 6: UI — Sidebar Import Section & File Menu

**Files:**
- Modify: `MTGProxyBuilder.UI/MainWindow.xaml`

- [ ] **Step 1: Add Riftbound import section to the Import sidebar**

In `MainWindow.xaml`, find the Import sidebar section (the `<controls:SidebarSection x:Name="ImportSection">`). Before the "Import Text List" subsection, add:

```xml
<!-- SECTION: Riftbound Import -->
<TextBlock Text="Import Riftbound Deck" FontWeight="SemiBold" Foreground="#CCC" Margin="0,16,0,6" FontSize="12"/>
<TextBlock Text="Paste a URL from Piltover Archive" Foreground="#666" FontSize="10" Margin="0,0,0,4"/>
<Grid Margin="0,0,0,4">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <TextBox Text="{Binding RiftboundImportUrl, UpdateSourceTrigger=PropertyChanged}"
             Padding="6,5" FontSize="11"
             Background="#3E3E42" Foreground="White" BorderBrush="#555" BorderThickness="1"
             CaretBrush="White">
        <TextBox.InputBindings>
            <KeyBinding Key="Enter" Command="{Binding ImportRiftboundDeckCommand}"/>
        </TextBox.InputBindings>
    </TextBox>
    <Button Grid.Column="1" Style="{StaticResource AccentBtn}"
            Command="{Binding ImportRiftboundDeckCommand}" Content="Import" Margin="4,0,0,0" Padding="10,5"/>
</Grid>
```

- [ ] **Step 2: Add menu item to the Cards menu**

Find the `<MenuItem Header="_Cards">` section. After the existing import items, add:

```xml
<MenuItem Header="Import _Riftbound Deck" Command="{Binding ActiveProject.Inner.ImportRiftboundDeckCommand}"/>
```

- [ ] **Step 3: Build to verify XAML compiles**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/MainWindow.xaml
git commit -m "feat: add Riftbound import UI in sidebar and Cards menu"
```

---

### Task 7: Clipboard URL Auto-Detection

**Files:**
- Modify: `MTGProxyBuilder.UI/MainWindow.xaml.cs`

- [ ] **Step 1: Enhance the Ctrl+V handler**

In `MainWindow.xaml.cs`, find the existing Ctrl+V handler (added in the clipboard paste feature). Replace it to check for a piltoverarchive URL first:

```csharp
else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
{
    // Check for Piltover Archive URL on clipboard first
    if (Clipboard.ContainsText())
    {
        string text = Clipboard.GetText().Trim();
        if (DeckImportService.DetectSource(text) == DeckSource.PiltoverArchive)
        {
            vm.ImportRiftboundFromUrl(text);
            e.Handled = true;
            return;
        }
    }
    // Fall back to image paste
    vm.PasteImageFromClipboard();
    e.Handled = true;
}
```

Add the using at the top of the file if not already present:

```csharp
using MTGProxyBuilder.Core.Services;
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/MainWindow.xaml.cs
git commit -m "feat: auto-detect Piltover Archive URLs on clipboard paste"
```

---

### Task 8: Update Existing DeckImportService URL Hint & RefreshDeck Support

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Update RefreshDeck to support Piltover Archive**

Find the `RefreshDeck()` method. Add a branch for PiltoverArchive before the existing source check:

```csharp
private async void RefreshDeck()
{
    string? url = _currentProject.DeckImportUrl;
    if (string.IsNullOrEmpty(url)) return;

    var source = DeckImportService.DetectSource(url);

    // Route Piltover Archive refreshes to dedicated handler
    if (source == DeckSource.PiltoverArchive)
    {
        var confirm = MessageBox.Show(
            $"Re-import deck from Piltover Archive?\n\nThis will clear all current cards and re-download from:\n{url}",
            "Refresh Deck", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        PushUndo();
        Cards.Clear();
        ImportRiftboundDeck(urlOverride: url);
        return;
    }

    if (source == DeckSource.Unknown)
    // ... rest of existing method unchanged ...
```

- [ ] **Step 2: Update the existing Import sidebar help text**

In `MainWindow.xaml`, find the text that says `"Paste a URL from Moxfield or Archidekt"` and change it to:

```xml
<TextBlock Text="Paste a URL from Moxfield, Archidekt, or Piltover Archive" Foreground="#666" FontSize="10" Margin="0,0,0,4"/>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/MainViewModel.cs MTGProxyBuilder.UI/MainWindow.xaml
git commit -m "feat: support Piltover Archive in deck refresh and update help text"
```

---

### Task 9: Integration Test — Full Import Pipeline

**Files:**
- Create: `MTGProxyBuilder.Tests/Services/RiftboundImportTests.cs`

- [ ] **Step 1: Write integration test for the full deck parse + model creation flow**

```csharp
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using Xunit;

namespace MTGProxyBuilder.Tests.Services
{
    public class RiftboundImportTests
    {
        [Fact]
        public void DeckSource_DetectsPiltoverArchive()
        {
            Assert.Equal(DeckSource.PiltoverArchive,
                DeckImportService.DetectSource("https://piltoverarchive.com/decks/view/abc-123"));
        }

        [Fact]
        public void RiftboundDeck_AllCards_IncludesAllSections()
        {
            var deck = new RiftboundDeck
            {
                Legend = new RiftboundDeckCard { Quantity = 1, Card = new RiftboundCard { Name = "Legend" } },
                Champions = new() { new RiftboundDeckCard { Quantity = 2, Card = new RiftboundCard { Name = "Champ" } } },
                Battlefields = new() { new RiftboundDeckCard { Quantity = 1, Card = new RiftboundCard { Name = "Field" } } },
                Runes = new() { new RiftboundDeckCard { Quantity = 1, Card = new RiftboundCard { Name = "Rune" } } },
                Maindeck = new() { new RiftboundDeckCard { Quantity = 3, Card = new RiftboundCard { Name = "Card1" } } },
                Sideboard = new(),
                Bench = new()
            };

            var all = deck.AllCards().ToList();
            Assert.Equal(5, all.Count);
            Assert.Equal(8, all.Sum(c => c.Quantity));
        }

        [Fact]
        public void GetCardImageUrl_MatchesVariantId()
        {
            var deckCard = new RiftboundDeckCard
            {
                VariantId = "variant-2",
                Card = new RiftboundCard
                {
                    CardVariants = new()
                    {
                        new RiftboundCardVariant { Id = "variant-1", ImageUrl = "https://cdn/v1.webp" },
                        new RiftboundCardVariant { Id = "variant-2", ImageUrl = "https://cdn/v2.webp" }
                    }
                }
            };

            Assert.Equal("https://cdn/v2.webp", PiltoverArchiveService.GetCardImageUrl(deckCard));
        }

        [Fact]
        public void GetCardImageUrl_FallsBackToFirstVariant()
        {
            var deckCard = new RiftboundDeckCard
            {
                VariantId = "missing",
                Card = new RiftboundCard
                {
                    CardVariants = new()
                    {
                        new RiftboundCardVariant { Id = "variant-1", ImageUrl = "https://cdn/v1.webp" }
                    }
                }
            };

            Assert.Equal("https://cdn/v1.webp", PiltoverArchiveService.GetCardImageUrl(deckCard));
        }

        [Fact]
        public void GetCardImageUrl_ReturnsNull_WhenNoVariants()
        {
            var deckCard = new RiftboundDeckCard
            {
                Card = new RiftboundCard { CardVariants = new() }
            };

            Assert.Null(PiltoverArchiveService.GetCardImageUrl(deckCard));
        }
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test MTGProxyBuilder.Tests --filter "FullyQualifiedName~RiftboundImportTests" -v q`
Expected: All PASS

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Tests/Services/RiftboundImportTests.cs
git commit -m "test: add Riftbound import integration tests"
```

---

### Task 10: Final Build & Manual Verification

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test MTGProxyBuilder.Tests -v q`
Expected: All existing tests pass, plus new Riftbound tests pass

- [ ] **Step 3: Manual test checklist**

1. Launch the app, create a new project
2. In the Import sidebar, paste `https://piltoverarchive.com/decks/view/0741d662-e31b-4999-b1f8-96d89d085423` into the Riftbound Import URL field, click Import
3. Verify: Progress messages appear, cards are downloaded, deck appears in grid
4. Verify: Card details sidebar shows name, type, description, rarity for imported cards
5. Copy `https://piltoverarchive.com/decks/view/0741d662-e31b-4999-b1f8-96d89d085423` to clipboard, press Ctrl+V — should auto-detect and start import
6. Paste the URL into the existing Moxfield/Archidekt URL field — should route to Riftbound handler
7. Verify: "Refresh Deck" works for a Riftbound-imported deck
8. Cards menu shows "Import Riftbound Deck" item

- [ ] **Step 4: Commit everything remaining**

```bash
git add -A
git commit -m "feat: complete Riftbound / Piltover Archive deck import feature"
```
