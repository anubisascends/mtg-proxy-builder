using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MTGProxyBuilder.Core.Services
{
    public class MpcFillCard
    {
        public string Identifier { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public int Dpi { get; set; }
        public string Language { get; set; } = string.Empty;
        public string SmallThumbnailUrl { get; set; } = string.Empty;
        public string MediumThumbnailUrl { get; set; } = string.Empty;
        public string DownloadLink { get; set; } = string.Empty;

        public override string ToString() => $"{Name} [{Source}] ({Dpi} DPI)";
    }

    public class MpcFillService
    {
        private readonly HttpClient _httpClient;
        private readonly ImageCacheService _imageCache;
        private readonly MpcFillSourceManager _sourceManager;
        private bool _sourcesLoaded;

        public MpcFillService(ImageCacheService imageCache, MpcFillSourceManager sourceManager)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _imageCache = imageCache;
            _sourceManager = sourceManager;
        }

        public MpcFillSourceManager SourceManager => _sourceManager;

        /// <summary>Ensures sources are loaded from the API. Call before first search.</summary>
        /// <summary>Loads sources from the API. Returns error message on failure, null on success.</summary>
        public async Task<string?> EnsureSourcesLoadedAsync()
        {
            if (_sourcesLoaded) return null;

            try
            {
                var response = await _httpClient.GetAsync("https://mpcfill.com/2/sources/");
                if (!response.IsSuccessStatusCode)
                    return $"MPCFill returned {(int)response.StatusCode} when fetching sources.";

                var json = await response.Content.ReadAsStringAsync();
                var root = JObject.Parse(json);
                var results = root["results"] as JObject;
                if (results == null)
                    return "MPCFill returned unexpected data format.";

                var rawSources = new Dictionary<string, (string name, string description)>();
                foreach (var prop in results.Properties())
                {
                    var obj = prop.Value;
                    rawSources[prop.Name] = (
                        obj["name"]?.ToString() ?? $"Source {prop.Name}",
                        obj["description"]?.ToString() ?? ""
                    );
                }

                _sourceManager.SetSources(rawSources);
                _sourcesLoaded = true;
                return null;
            }
            catch (HttpRequestException ex)
            {
                return $"Network error: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                return "Request timed out.";
            }
            catch (Exception ex)
            {
                return $"Error loading sources: {ex.Message}";
            }
        }

        /// <summary>Search MPCFill for card art.</summary>
        public async Task<(List<MpcFillCard> Cards, string? Error)> SearchAsync(
            string query, int pageSize = 30, int minimumDpi = 0,
            bool fuzzySearch = true, object[][]? sourcesOverride = null)
        {
            try
            {
                await EnsureSourcesLoadedAsync();
                // null = all sources enabled; caller passes favorites array explicitly when wanted
                var sources = sourcesOverride ?? _sourceManager.BuildSourcesArray();

                var payload = new
                {
                    query,
                    cardTypes = new[] { "CARD" },
                    sortBy = "nameAscending",
                    pageStart = 0,
                    pageSize,
                    searchSettings = new
                    {
                        searchTypeSettings = new { fuzzySearch, filterCardbacks = false },
                        filterSettings = new
                        {
                            languages = Array.Empty<string>(),
                            includesTags = Array.Empty<string>(),
                            excludesTags = Array.Empty<string>(),
                            minimumDPI = minimumDpi,
                            maximumDPI = 1500,
                            maximumSize = 30
                        },
                        sourceSettings = new { sources }
                    }
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://mpcfill.com/2/exploreSearch/", content);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    return (new(), $"MPCFill returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var root = JObject.Parse(responseJson);
                var cards = new List<MpcFillCard>();

                var cardsArray = root["cards"] as JArray;
                if (cardsArray != null)
                {
                    foreach (var c in cardsArray)
                    {
                        cards.Add(new MpcFillCard
                        {
                            Identifier = c["identifier"]?.ToString() ?? string.Empty,
                            Name = c["name"]?.ToString() ?? string.Empty,
                            Source = c["source"]?.ToString() ?? string.Empty,
                            SourceId = c["sourceId"]?.Value<int>() ?? 0,
                            Dpi = c["dpi"]?.Value<int>() ?? 0,
                            Language = c["language"]?.ToString() ?? "EN",
                            SmallThumbnailUrl = c["smallThumbnailUrl"]?.ToString() ?? string.Empty,
                            MediumThumbnailUrl = c["mediumThumbnailUrl"]?.ToString() ?? string.Empty,
                            DownloadLink = c["downloadLink"]?.ToString() ?? string.Empty
                        });
                    }
                }

                return (cards, null);
            }
            catch (HttpRequestException ex) { return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { return (new(), "Request timed out"); }
            catch (Exception ex) { return (new(), $"Error: {ex.Message}"); }
        }

        /// <summary>Download and cache a card image from MPCFill.</summary>
        public async Task<string?> DownloadAndCacheImageAsync(MpcFillCard card)
        {
            var cached = _imageCache.GetCachedImagePath($"mpc_{card.Identifier}");
            if (cached != null) return cached;

            string url = card.DownloadLink;
            if (string.IsNullOrEmpty(url)) return null;

            return await _imageCache.CacheImageFromUrlAsync(_httpClient, url, $"mpc_{card.Identifier}");
        }

        /// <summary>Fetches all cardback art from MPCFill using the /cardbacks/ + /cards/ endpoints.</summary>
        public async Task<(List<MpcFillCard> Cards, string? Error)> SearchCardbacksAsync(int pageSize = 500)
        {
            try
            {
                await EnsureSourcesLoadedAsync();
                var sources = _sourceManager.BuildSourcesArray();

                // Step 1: Get cardback identifiers from /2/cardbacks/
                var searchPayload = new
                {
                    searchSettings = new
                    {
                        searchTypeSettings = new { fuzzySearch = true, filterCardbacks = false },
                        filterSettings = new
                        {
                            languages = Array.Empty<string>(),
                            includesTags = Array.Empty<string>(),
                            excludesTags = Array.Empty<string>(),
                            minimumDPI = 0,
                            maximumDPI = 1500,
                            maximumSize = 30
                        },
                        sourceSettings = new { sources }
                    }
                };

                var json = JsonConvert.SerializeObject(searchPayload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://mpcfill.com/2/cardbacks/", content);

                if (!response.IsSuccessStatusCode)
                    return (new(), $"MPCFill cardbacks endpoint returned {(int)response.StatusCode}");

                var responseJson = await response.Content.ReadAsStringAsync();
                var root = JObject.Parse(responseJson);
                var identifiers = root["cardbacks"]?.ToObject<List<string>>() ?? new();

                if (identifiers.Count == 0)
                    return (new(), "No card backs found on MPCFill.");

                // Step 2: Fetch card details in batches via /2/cards/
                var cards = new List<MpcFillCard>();
                const int batchSize = 50;

                for (int i = 0; i < identifiers.Count; i += batchSize)
                {
                    var batch = identifiers.Skip(i).Take(batchSize).ToList();
                    var detailPayload = new { cardIdentifiers = batch };
                    var detailJson = JsonConvert.SerializeObject(detailPayload);
                    var detailContent = new StringContent(detailJson, System.Text.Encoding.UTF8, "application/json");

                    var detailResponse = await _httpClient.PostAsync("https://mpcfill.com/2/cards/", detailContent);
                    if (!detailResponse.IsSuccessStatusCode) continue;

                    var detailResponseJson = await detailResponse.Content.ReadAsStringAsync();
                    var detailRoot = JObject.Parse(detailResponseJson);
                    var results = detailRoot["results"] as JObject;
                    if (results == null) continue;

                    foreach (var prop in results.Properties())
                    {
                        var c = prop.Value;
                        cards.Add(new MpcFillCard
                        {
                            Identifier = prop.Name,
                            Name = c["name"]?.ToString() ?? string.Empty,
                            Source = c["source"]?.ToString() ?? string.Empty,
                            SourceId = c["sourceId"]?.Value<int>() ?? 0,
                            Dpi = c["dpi"]?.Value<int>() ?? 0,
                            Language = c["language"]?.ToString() ?? "EN",
                            SmallThumbnailUrl = c["smallThumbnailUrl"]?.ToString() ?? string.Empty,
                            MediumThumbnailUrl = c["mediumThumbnailUrl"]?.ToString() ?? string.Empty,
                            DownloadLink = c["downloadLink"]?.ToString() ?? string.Empty
                        });
                    }
                }

                return (cards, null);
            }
            catch (HttpRequestException ex) { return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { return (new(), "Request timed out"); }
            catch (Exception ex) { return (new(), $"Error: {ex.Message}"); }
        }
    }
}
