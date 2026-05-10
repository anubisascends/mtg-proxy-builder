using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class ScryfallCardSearchResult
    {
        [JsonProperty("data")]
        public List<ScryfallCard>? Data { get; set; }

        [JsonProperty("has_more")]
        public bool HasMore { get; set; }

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

        [JsonProperty("image_uris")]
        public Dictionary<string, string>? ImageUris { get; set; }

        [JsonProperty("card_faces")]
        public List<CardFace>? CardFaces { get; set; }

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

            // For double-faced cards, gather text from both faces
            string manaCost = ManaCost ?? CardFaces?.FirstOrDefault()?.ManaCost ?? string.Empty;
            string typeLine = TypeLine ?? CardFaces?.FirstOrDefault()?.TypeLine ?? string.Empty;
            string oracleText = OracleText ?? CardFaces?.FirstOrDefault()?.OracleText ?? string.Empty;
            string power = Power ?? CardFaces?.FirstOrDefault()?.Power ?? string.Empty;
            string toughness = Toughness ?? CardFaces?.FirstOrDefault()?.Toughness ?? string.Empty;
            string loyalty = Loyalty ?? CardFaces?.FirstOrDefault()?.Loyalty ?? string.Empty;

            return new CardModel
            {
                Name = Name,
                ScryfallId = Id,
                ArtworkPath = artworkPath,
                BackArtworkPath = backArtworkPath,
                OriginalBackArtworkPath = backArtworkPath,
                IncludeBack = hasBack,
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
                string encoded = System.Net.WebUtility.UrlEncode(cardName);
                var response = await _httpClient.GetAsync(
                    $"https://api.scryfall.com/cards/search?q={encoded}");

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    return (new(), $"Scryfall returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ScryfallCardSearchResult>(content);
                var cards = result?.Data ?? new();
                return (cards, null);
            }
            catch (HttpRequestException ex)
            {
                return (new(), $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (new(), "Request timed out");
            }
            catch (Exception ex)
            {
                return (new(), $"Error: {ex.Message}");
            }
        }

        public async Task<string?> DownloadAndCacheImageAsync(ScryfallCard card, bool back = false)
        {
            string cacheKey = back ? $"{card.Id}_back" : card.Id;

            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null) return cached;

            string? imageUrl = back ? card.GetBackImageUrl() : card.GetImageUrl();
            if (imageUrl == null) return null;

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
            catch
            {
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
            catch
            {
                return null;
            }
        }
    }
}
