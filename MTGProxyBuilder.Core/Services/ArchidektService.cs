using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MTGProxyBuilder.Core.Services
{
    public class ArchidektDeckEntry
    {
        public string CardName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? ScryfallId { get; set; }
        public string SetCode { get; set; } = string.Empty;
        public string CollectorNumber { get; set; } = string.Empty;
    }

    public class ArchidektDeck
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public List<ArchidektDeckEntry> Entries { get; set; } = new();
    }

    public class ArchidektService
    {
        private readonly HttpClient _httpClient;

        public ArchidektService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Extracts the numeric deck ID from an Archidekt URL.
        /// Supports: https://archidekt.com/decks/12345/deck_name
        /// </summary>
        public static string? ParseDeckId(string url)
        {
            url = url.Trim();

            // Direct numeric ID
            if (Regex.IsMatch(url, @"^\d+$"))
                return url;

            var match = Regex.Match(url, @"archidekt\.com/decks/(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Fetches deck data from the Archidekt API.</summary>
        public async Task<(ArchidektDeck? Deck, string? Error)> FetchDeckAsync(string deckId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"https://archidekt.com/api/decks/{deckId}/");

                if (!response.IsSuccessStatusCode)
                {
                    if ((int)response.StatusCode == 404)
                        return (null, "Deck not found. It may be private or the URL may be wrong.");
                    var body = await response.Content.ReadAsStringAsync();
                    return (null, $"Archidekt returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var root = JObject.Parse(json);

                // Map format enum to name
                int formatId = root["deckFormat"]?.Value<int>() ?? 0;
                string format = formatId switch
                {
                    1 => "standard",
                    2 => "modern",
                    3 => "commander",
                    4 => "legacy",
                    5 => "vintage",
                    6 => "pauper",
                    7 => "pioneer",
                    _ => "unknown"
                };

                var deck = new ArchidektDeck
                {
                    Name = root["name"]?.ToString() ?? "Imported Deck",
                    Format = format
                };

                var cardsArray = root["cards"] as JArray;
                if (cardsArray == null)
                    return (null, "No cards found in deck data.");

                foreach (var entry in cardsArray)
                {
                    var cardObj = entry["card"];
                    var oracleCard = cardObj?["oracleCard"];
                    if (oracleCard == null) continue;

                    string name = oracleCard["name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;

                    var categories = entry["categories"] as JArray;
                    string category = categories?.FirstOrDefault()?.ToString() ?? string.Empty;

                    // Skip maybeboard/sideboard tokens etc if desired - include everything for now
                    deck.Entries.Add(new ArchidektDeckEntry
                    {
                        CardName = name,
                        Quantity = entry["quantity"]?.Value<int>() ?? 1,
                        Category = category,
                        ScryfallId = cardObj?["uid"]?.ToString(),
                        SetCode = cardObj?["edition"]?["editioncode"]?.ToString() ?? string.Empty,
                        CollectorNumber = cardObj?["collectorNumber"]?.ToString() ?? string.Empty
                    });
                }

                return (deck, null);
            }
            catch (HttpRequestException ex)
            {
                return (null, $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (null, "Request timed out");
            }
            catch (Exception ex)
            {
                return (null, $"Error: {ex.Message}");
            }
        }
    }
}
