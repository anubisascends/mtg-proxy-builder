using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MTGProxyBuilder.Core.Services
{
    public class MpcFillSearchOptions
    {
        public string[] CardTypes { get; set; } = new[] { "CARD" };
        public string SortBy { get; set; } = "nameAscending";
        public int MinimumDpi { get; set; }
        public int MaximumDpi { get; set; } = 1500;
        public int MaximumSize { get; set; } = 30;
        public bool FuzzySearch { get; set; } = true;
        public bool FilterCardbacks { get; set; }
        public string[] Languages { get; set; } = Array.Empty<string>();
        public string[] IncludesTags { get; set; } = Array.Empty<string>();
        public string[] ExcludesTags { get; set; } = Array.Empty<string>();

        public static MpcFillSearchOptions FromSettings(AppSettings settings)
        {
            var rawExclude = settings.MpcFillExcludeTags ?? new List<string>();
            var excludeTags = new List<string>(rawExclude);
            if (settings.MpcFillExcludeNsfw && !excludeTags.Contains("NSFW"))
                excludeTags.Add("NSFW");
            if (settings.MpcFillExcludeAiArt && !excludeTags.Contains("AI Art"))
                excludeTags.Add("AI Art");

            var cardTypes = settings.MpcFillCardTypes ?? new List<string>();
            var languages = settings.MpcFillLanguages ?? new List<string>();
            var includeTags = settings.MpcFillIncludeTags ?? new List<string>();

            return new MpcFillSearchOptions
            {
                CardTypes = cardTypes.Count > 0 ? cardTypes.ToArray() : new[] { "CARD" },
                SortBy = settings.MpcFillDefaultSortBy ?? "nameAscending",
                MinimumDpi = settings.MpcFillDefaultMinDpi,
                MaximumDpi = settings.MpcFillDefaultMaxDpi > 0 ? settings.MpcFillDefaultMaxDpi : 1500,
                MaximumSize = settings.MpcFillMaximumSize > 0 ? settings.MpcFillMaximumSize : 30,
                FuzzySearch = settings.MpcFillDefaultFuzzySearch,
                FilterCardbacks = settings.MpcFillFilterCardbacks,
                Languages = languages.ToArray(),
                IncludesTags = includeTags.ToArray(),
                ExcludesTags = excludeTags.ToArray()
            };
        }
    }

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

        /// <summary>Loads sources from the API. Returns error message on failure, null on success.</summary>
        /// <param name="forceReload">When true, re-fetches even if previously loaded.</param>
        public async Task<string?> EnsureSourcesLoadedAsync(bool forceReload = false)
        {
            if (_sourcesLoaded && !forceReload) return null;

            try
            {
                Log.Information("Loading MPCFill sources");
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
                Log.Error(ex, "MPCFill network error loading sources");
                return $"Network error: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                Log.Warning("MPCFill source loading timed out");
                return "Request timed out.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load MPCFill sources");
                return $"Error loading sources: {ex.Message}";
            }
        }

        /// <summary>Search MPCFill for card art. Paginates automatically to fetch all results.</summary>
        /// <param name="maxResults">Maximum total results to return. 0 = unlimited.</param>
        public async Task<(List<MpcFillCard> Cards, string? Error)> SearchAsync(
            string query, int pageSize = 60, int minimumDpi = 0,
            bool fuzzySearch = true, object[][]? sourcesOverride = null,
            int maxResults = 0, MpcFillSearchOptions? options = null)
        {
            try
            {
                await EnsureSourcesLoadedAsync();
                // Rebuild sources after ensuring they're loaded — sourcesOverride may have been
                // captured before sources finished loading, producing a stale empty array.
                var sources = (sourcesOverride != null && sourcesOverride.Length > 0)
                    ? sourcesOverride
                    : _sourceManager.BuildSourcesArray();

                var opts = options ?? new MpcFillSearchOptions();
                Log.Information("MPCFill search: {Query}", query);
                // Per-call overrides take precedence when explicitly passed
                if (options == null)
                {
                    opts.MinimumDpi = minimumDpi;
                    opts.FuzzySearch = fuzzySearch;
                }

                var allCards = new List<MpcFillCard>();
                int pageStart = 0;

                while (true)
                {
                    var payload = new
                    {
                        query,
                        cardTypes = opts.CardTypes,
                        sortBy = opts.SortBy,
                        pageStart,
                        pageSize,
                        searchSettings = new
                        {
                            searchTypeSettings = new { fuzzySearch = opts.FuzzySearch, filterCardbacks = opts.FilterCardbacks },
                            filterSettings = new
                            {
                                languages = opts.Languages,
                                includesTags = opts.IncludesTags,
                                excludesTags = opts.ExcludesTags,
                                minimumDPI = opts.MinimumDpi,
                                maximumDPI = opts.MaximumDpi,
                                maximumSize = opts.MaximumSize
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
                        if (allCards.Count > 0) break; // return what we have from prior pages
                        return (new(), $"MPCFill returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
                    }

                    var responseJson = await response.Content.ReadAsStringAsync();
                    var root = JObject.Parse(responseJson);

                    var cardsArray = root["cards"] as JArray;
                    if (cardsArray == null || cardsArray.Count == 0) break;

                    foreach (var c in cardsArray)
                    {
                        allCards.Add(new MpcFillCard
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

                    // Stop if we hit the limit or got fewer than pageSize results (no more pages)
                    if (maxResults > 0 && allCards.Count >= maxResults) break;
                    if (cardsArray.Count < pageSize) break;

                    pageStart += cardsArray.Count;
                }

                return (allCards, null);
            }
            catch (HttpRequestException ex) { Log.Error(ex, "MPCFill network error searching {Query}", query); return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { Log.Warning("MPCFill search timed out for {Query}", query); return (new(), "Request timed out"); }
            catch (Exception ex) { Log.Error(ex, "MPCFill search failed for {Query}", query); return (new(), $"Error: {ex.Message}"); }
        }

        /// <summary>Download and cache a card image from MPCFill.</summary>
        /// <param name="thumbnail">When true, downloads the small thumbnail instead of the full image.</param>
        public async Task<string?> DownloadAndCacheImageAsync(MpcFillCard card, bool thumbnail = false)
        {
            // Fall back to full download if no thumbnail URL available
            if (thumbnail && string.IsNullOrEmpty(card.SmallThumbnailUrl))
                thumbnail = false;

            string cacheKey = thumbnail ? $"thumb_{card.Identifier}" : $"mpc_{card.Identifier}";
            var cached = _imageCache.GetCachedImagePath(cacheKey);
            if (cached != null)
            {
                if (!thumbnail)
                    _imageCache.SetMetadata(cacheKey, card.Name, card.Source);
                return cached;
            }

            string url = thumbnail ? card.SmallThumbnailUrl : card.DownloadLink;
            if (string.IsNullOrEmpty(url)) return null;

            Log.Information("Downloading MPCFill image {Identifier} ({Mode})", card.Identifier, thumbnail ? "thumbnail" : "full");
            var result = await _imageCache.CacheImageFromUrlAsync(_httpClient, url, cacheKey);
            if (result != null && !thumbnail)
                _imageCache.SetMetadata(cacheKey, card.Name, card.Source);
            return result;
        }

        /// <summary>Download and cache multiple card images in parallel with bounded concurrency.</summary>
        /// <param name="cards">Cards to download.</param>
        /// <param name="maxConcurrency">Max simultaneous downloads.</param>
        /// <param name="onProgress">Called on each completion with (completed, total, card name).</param>
        /// <returns>List of (card, cachedPath) pairs. cachedPath is null on failure.</returns>
        public async Task<List<(MpcFillCard Card, string? CachedPath)>> DownloadAndCacheImagesAsync(
            IReadOnlyList<MpcFillCard> cards,
            int maxConcurrency = 8,
            Action<int, int, string>? onProgress = null)
        {
            var results = new (MpcFillCard Card, string? CachedPath)[cards.Count];
            int completed = 0;
            var semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = cards.Select(async (card, index) =>
            {
                await semaphore.WaitAsync();
                try
                {
                    results[index] = (card, await DownloadAndCacheImageAsync(card));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to download MPCFill image {Identifier}", card.Identifier);
                    results[index] = (card, null);
                }
                finally
                {
                    semaphore.Release();
                    var count = Interlocked.Increment(ref completed);
                    onProgress?.Invoke(count, cards.Count, card.Name);
                }
            });

            await Task.WhenAll(tasks);
            return results.ToList();
        }

        /// <summary>Fetches all cardback art from MPCFill using the /cardbacks/ + /cards/ endpoints.</summary>
        public async Task<(List<MpcFillCard> Cards, string? Error)> SearchCardbacksAsync(
            int pageSize = 500, MpcFillSearchOptions? options = null)
        {
            try
            {
                await EnsureSourcesLoadedAsync();
                var sources = _sourceManager.BuildSourcesArray();
                var opts = options ?? new MpcFillSearchOptions();

                // Step 1: Get cardback identifiers from /2/cardbacks/
                var searchPayload = new
                {
                    searchSettings = new
                    {
                        searchTypeSettings = new { fuzzySearch = opts.FuzzySearch, filterCardbacks = opts.FilterCardbacks },
                        filterSettings = new
                        {
                            languages = opts.Languages,
                            includesTags = opts.IncludesTags,
                            excludesTags = opts.ExcludesTags,
                            minimumDPI = opts.MinimumDpi,
                            maximumDPI = opts.MaximumDpi,
                            maximumSize = opts.MaximumSize
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

                // Step 2: Fetch card details in parallel batches via /2/cards/
                const int batchSize = 50;
                var batches = new List<List<string>>();
                for (int i = 0; i < identifiers.Count; i += batchSize)
                    batches.Add(identifiers.Skip(i).Take(batchSize).ToList());

                var batchTasks = batches.Select(async batch =>
                {
                    var detailPayload = new { cardIdentifiers = batch };
                    var detailJson = JsonConvert.SerializeObject(detailPayload);
                    var detailContent = new StringContent(detailJson, System.Text.Encoding.UTF8, "application/json");

                    var detailResponse = await _httpClient.PostAsync("https://mpcfill.com/2/cards/", detailContent);
                    if (!detailResponse.IsSuccessStatusCode) return new List<MpcFillCard>();

                    var detailResponseJson = await detailResponse.Content.ReadAsStringAsync();
                    var detailRoot = JObject.Parse(detailResponseJson);
                    var results = detailRoot["results"] as JObject;
                    if (results == null) return new List<MpcFillCard>();

                    var batchCards = new List<MpcFillCard>();
                    foreach (var prop in results.Properties())
                    {
                        var c = prop.Value;
                        batchCards.Add(new MpcFillCard
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
                    return batchCards;
                });

                var batchResults = await Task.WhenAll(batchTasks);
                var cards = batchResults.SelectMany(b => b).ToList();

                return (cards, null);
            }
            catch (HttpRequestException ex) { Log.Error(ex, "MPCFill network error fetching cardbacks"); return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { Log.Warning("MPCFill cardback search timed out"); return (new(), "Request timed out"); }
            catch (Exception ex) { Log.Error(ex, "MPCFill cardback search failed"); return (new(), $"Error: {ex.Message}"); }
        }
    }
}
