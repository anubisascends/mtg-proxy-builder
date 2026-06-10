using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;
using Serilog;

namespace MTGProxyBuilder.Core.Services
{
    public class ScryfallCardSearchResult
    {
        [JsonProperty("data")]
        public List<ScryfallCard>? Data { get; set; }

        [JsonProperty("has_more")]
        public bool HasMore { get; set; }

        [JsonProperty("next_page")]
        public string? NextPage { get; set; }

        [JsonProperty("total_cards")]
        public int TotalCards { get; set; }
    }

    public class ScryfallCard
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("mana_cost")]
        public string? ManaCost { get; set; }

        [JsonProperty("cmc")]
        public float CMC { get; set; }

        [JsonProperty("type_line")]
        public string? TypeLine { get; set; }

        [JsonProperty("oracle_text")]
        public string? OracleText { get; set; }

        [JsonProperty("colors")]
        public List<string>? Colors { get; set; }

        [JsonProperty("color_identity")]
        public List<string>? ColorIdentity { get; set; }

        [JsonProperty("keywords")]
        public List<string>? Keywords { get; set; }

        [JsonProperty("power")]
        public string? Power { get; set; }

        [JsonProperty("toughness")]
        public string? Toughness { get; set; }

        [JsonProperty("loyalty")]
        public string? Loyalty { get; set; }

        [JsonProperty("rarity")]
        public string? Rarity { get; set; }

        [JsonProperty("artist")]
        public string? Artist { get; set; }

        [JsonProperty("set")]
        public string SetCode { get; set; } = string.Empty;

        [JsonProperty("set_name")]
        public string SetName { get; set; } = string.Empty;

        [JsonProperty("collector_number")]
        public string CollectorNumber { get; set; } = string.Empty;

        [JsonProperty("released_at")]
        public string? ReleasedAt { get; set; }

        [JsonProperty("layout")]
        public string Layout { get; set; } = string.Empty;

        [JsonProperty("image_uris")]
        public Dictionary<string, string>? ImageUris { get; set; }

        [JsonProperty("card_faces")]
        public List<CardFace>? CardFaces { get; set; }

        private static readonly HashSet<string> DfcLayouts = new(StringComparer.OrdinalIgnoreCase)
        {
            "transform", "modal_dfc", "double_faced_token", "reversible_card", "art_series"
        };

        /// <summary>True if the card's layout is a double-faced type (transform, MDFC, etc.).</summary>
        public bool IsDfcLayout => DfcLayouts.Contains(Layout);

        public string? GetImageUrl(string size = "large")
        {
            if (ImageUris != null && ImageUris.TryGetValue(size, out var url))
                return url;
            if (CardFaces?.Count > 0 && CardFaces[0].ImageUris != null)
                return CardFaces[0].ImageUris!.TryGetValue(size, out var faceUrl) ? faceUrl : null;
            return null;
        }

        public string? GetBackImageUrl(string size = "large")
        {
            if (CardFaces?.Count > 1 && CardFaces[1].ImageUris != null)
                return CardFaces[1].ImageUris!.TryGetValue(size, out var url) ? url : null;
            return null;
        }

        /// <summary>Populate a CardModel with all available Scryfall metadata.</summary>
        public CardModel ToCardModel(string artworkPath, string? backArtworkPath)
        {
            bool hasBack = backArtworkPath != null;

            // For double-faced cards, gather text from front face
            var frontFace = CardFaces?.Count > 0 ? CardFaces[0] : null;
            var backFace = CardFaces?.Count > 1 ? CardFaces[1] : null;

            string manaCost = ManaCost ?? frontFace?.ManaCost ?? string.Empty;
            string typeLine = TypeLine ?? frontFace?.TypeLine ?? string.Empty;
            string oracleText = OracleText ?? frontFace?.OracleText ?? string.Empty;
            string power = Power ?? frontFace?.Power ?? string.Empty;
            string toughness = Toughness ?? frontFace?.Toughness ?? string.Empty;
            string loyalty = Loyalty ?? frontFace?.Loyalty ?? string.Empty;

            return new CardModel
            {
                Name = Name,
                ScryfallId = Id,
                ArtworkPath = artworkPath,
                BackArtworkPath = backArtworkPath,
                OriginalBackArtworkPath = backArtworkPath,
                IncludeBack = hasBack,
                IsDoubleFaced = IsDfcLayout,
                ManaCost = manaCost,
                CMC = CMC,
                TypeLine = typeLine,
                OracleText = oracleText,
                Rarity = Rarity ?? string.Empty,
                Colors = Colors != null ? string.Join(",", Colors) : string.Empty,
                ColorIdentity = ColorIdentity != null ? string.Join(",", ColorIdentity) : string.Empty,
                SetCode = SetCode,
                SetName = SetName,
                CollectorNumber = CollectorNumber,
                Artist = Artist ?? string.Empty,
                Power = power,
                Toughness = toughness,
                Loyalty = loyalty,
                Keywords = Keywords != null ? string.Join(",", Keywords) : string.Empty,
                BackName = backFace?.Name ?? string.Empty,
                BackManaCost = backFace?.ManaCost ?? string.Empty,
                BackTypeLine = backFace?.TypeLine ?? string.Empty,
                BackOracleText = backFace?.OracleText ?? string.Empty,
                BackPower = backFace?.Power ?? string.Empty,
                BackToughness = backFace?.Toughness ?? string.Empty,
                BackLoyalty = backFace?.Loyalty ?? string.Empty,
                DateAdded = DateTime.Now
            };
        }

        public override string ToString() => $"{Name} ({SetName} #{CollectorNumber})";
    }

    public class CardFace
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("mana_cost")]
        public string? ManaCost { get; set; }

        [JsonProperty("type_line")]
        public string? TypeLine { get; set; }

        [JsonProperty("oracle_text")]
        public string? OracleText { get; set; }

        [JsonProperty("power")]
        public string? Power { get; set; }

        [JsonProperty("toughness")]
        public string? Toughness { get; set; }

        [JsonProperty("loyalty")]
        public string? Loyalty { get; set; }

        [JsonProperty("image_uris")]
        public Dictionary<string, string>? ImageUris { get; set; }
    }

    public class ScryfallService
    {
        private readonly HttpClient _httpClient;
        private readonly ImageCacheService _imageCache;

        public ScryfallService(ImageCacheService imageCache)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _imageCache = imageCache;
        }

        public async Task<(List<ScryfallCard> Cards, string? Error)> SearchCardAsync(string cardName)
        {
            try
            {
                Log.Information("Scryfall search: {Query}", cardName);
                string encoded = System.Net.WebUtility.UrlEncode(cardName);
                string? url = $"https://api.scryfall.com/cards/search?q={encoded}";
                var allCards = new List<ScryfallCard>();

                while (url != null)
                {
                    var response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync();
                        if (allCards.Count > 0) break; // return what we have from prior pages
                        return (new(), $"Scryfall returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<ScryfallCardSearchResult>(content);
                    if (result?.Data != null)
                        allCards.AddRange(result.Data);

                    // Scryfall requires 50-100ms between requests
                    url = result is { HasMore: true, NextPage: not null } ? result.NextPage : null;
                    if (url != null)
                        await Task.Delay(100);
                }

                return (allCards, null);
            }
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "Scryfall network error searching for {Query}", cardName);
                return (new(), $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Log.Warning("Scryfall search timed out for {Query}", cardName);
                return (new(), "Request timed out");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Scryfall search failed for {Query}", cardName);
                return (new(), $"Error: {ex.Message}");
            }
        }

        public async Task<string?> DownloadAndCacheImageAsync(ScryfallCard card, bool back = false, string size = "large")
        {
            string sizeSuffix = size == "large" ? "" : $"_{size}";
            string cacheKey = back ? $"{card.Id}_back{sizeSuffix}" : $"{card.Id}{sizeSuffix}";

            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null) return cached;

            string? imageUrl = back ? card.GetBackImageUrl(size) : card.GetImageUrl(size);
            if (imageUrl == null) return null;

            Log.Information("Downloading Scryfall image {CardId} ({Size}{Back})", card.Id, size, back ? ", back" : "");
            return await _imageCache.CacheImageFromUrlAsync(_httpClient, imageUrl, cacheKey);
        }

        /// <summary>Fetch a single card by Scryfall ID.</summary>
        public async Task<ScryfallCard?> GetCardByIdAsync(string scryfallId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"https://api.scryfall.com/cards/{scryfallId}");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ScryfallCard>(json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch Scryfall card by ID {Id}", scryfallId);
                return null;
            }
        }

        /// <summary>Fetch a card by name (exact match).</summary>
        public async Task<ScryfallCard?> GetCardByNameAsync(string cardName)
        {
            try
            {
                string encoded = System.Net.WebUtility.UrlEncode(cardName);
                var response = await _httpClient.GetAsync(
                    $"https://api.scryfall.com/cards/named?fuzzy={encoded}");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<ScryfallCard>(json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch Scryfall card by name {Name}", cardName);
                return null;
            }
        }
    }
}
