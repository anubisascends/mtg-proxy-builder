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

        public static string? ParseDeckId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var match = Regex.Match(url.Trim(),
                @"piltoverarchive\.com/decks/view/([a-f0-9\-]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

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
        /// The deck data is embedded inside self.__next_f.push() script blocks as a
        /// JS-escaped string. We search for the escaped deck prop pattern directly in
        /// the raw HTML, then unescape and deserialize.
        /// </summary>
        public static RiftboundDeck? ExtractDeckFromHtml(string html)
        {
            // The deck prop appears in the raw HTML as escaped JSON inside a JS string:
            //   ...{\"deck\":{\"id\":\"...\",\"authorId\":\"...
            // The JSON-LD block also contains a doubly-escaped version (\\\"deck\\\")
            // so we use a specific marker that only matches the RSC prop format.
            // The prop is preceded by: ,{\"deck\":{\"id\":\"  (component prop assignment)
            const string marker = ",{\\\"deck\\\":{\\\"id\\\":";
            int markerIdx = html.IndexOf(marker);
            if (markerIdx < 0) return null;

            // The deck JSON object starts at the '{' after \"deck\":
            // Skip past ,{\"deck\": to reach the inner { of the deck value
            int objectStart = markerIdx + ",{\\\"deck\\\":".Length;

            // Extract from objectStart to the end of the enclosing push() call,
            // then unescape and find the balanced JSON object.
            // Take a generous substring — deck JSON is typically 20-100KB.
            int remaining = Math.Min(html.Length - objectStart, 300_000);
            string raw = html.Substring(objectStart, remaining);

            // Unescape the JS string encoding
            string unescaped = raw
                .Replace("\\\"", "\"")
                .Replace("\\/", "/")
                .Replace("\\\\", "\\");

            // Find the balanced end of the deck JSON object by tracking brace depth
            int depth = 0;
            int endIdx = -1;
            for (int i = 0; i < unescaped.Length; i++)
            {
                char c = unescaped[i];
                if (c == '"')
                {
                    // Skip string contents
                    i++;
                    while (i < unescaped.Length)
                    {
                        if (unescaped[i] == '\\') { i++; } // skip escaped char
                        else if (unescaped[i] == '"') break;
                        i++;
                    }
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) { endIdx = i; break; }
                }
            }

            if (endIdx < 0) return null;

            string deckJson = unescaped.Substring(0, endIdx + 1);

            try
            {
                return JsonSerializer.Deserialize<RiftboundDeck>(deckJson);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to deserialize Riftbound deck JSON");
                return null;
            }
        }

        public static string? GetCardImageUrl(RiftboundDeckCard deckCard)
        {
            var variant = deckCard.Card.CardVariants
                .FirstOrDefault(v => v.Id == deckCard.VariantId)
                ?? deckCard.Card.CardVariants.FirstOrDefault();

            return variant?.ImageUrl;
        }

        public async Task<string?> DownloadCardImageAsync(RiftboundDeckCard deckCard)
        {
            string? imageUrl = GetCardImageUrl(deckCard);
            if (string.IsNullOrEmpty(imageUrl)) return null;

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
