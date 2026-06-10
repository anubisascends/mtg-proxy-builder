using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Downloads and indexes Scryfall bulk data for fast local card lookups.
    /// Avoids hitting API rate limits by resolving card names/sets locally.
    /// The bulk data is refreshed based on user-configured interval (default: daily).
    /// </summary>
    public class ScryfallBulkDataService
    {
        private readonly string _cacheDir;
        private readonly string _indexPath;
        private readonly string _metaPath;
        private readonly HttpClient _httpClient;

        // In-memory index: name (lowercase) -> list of entries
        private Dictionary<string, List<BulkCardEntry>>? _nameIndex;
        // Set+number index: "set:number" (lowercase) -> entry
        private Dictionary<string, BulkCardEntry>? _setNumberIndex;

        private bool _isLoaded;

        public bool IsLoaded => _isLoaded;

        public ScryfallBulkDataService()
        {
            _cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "BulkData");
            Directory.CreateDirectory(_cacheDir);
            _indexPath = Path.Combine(_cacheDir, "card_index.json");
            _metaPath = Path.Combine(_cacheDir, "bulk_meta.json");

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MTGProxyBuilder/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        /// <summary>
        /// Ensures bulk data is loaded and up-to-date. Downloads if missing or stale.
        /// Returns true if data is ready for queries.
        /// </summary>
        public async Task<bool> EnsureLoadedAsync(int refreshDays = 1, Action<string>? onProgress = null)
        {
            if (_isLoaded && _nameIndex != null)
                return true;

            // Check if cached index exists and is fresh enough
            if (File.Exists(_indexPath) && File.Exists(_metaPath))
            {
                try
                {
                    var meta = JsonConvert.DeserializeObject<BulkDataMeta>(File.ReadAllText(_metaPath));
                    if (meta != null && (DateTime.UtcNow - meta.DownloadedAt).TotalDays < refreshDays)
                    {
                        onProgress?.Invoke("Loading cached card data...");
                        LoadIndex();
                        if (_isLoaded) return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to read bulk data cache metadata");
                }
            }

            // Need to download fresh data
            return await DownloadAndBuildIndexAsync(onProgress);
        }

        /// <summary>
        /// Looks up a card by name. Returns the best match (most recent printing).
        /// If setCode is provided, prefers that set. If collectorNumber is also provided, matches exactly.
        /// </summary>
        public BulkCardEntry? FindCard(string name, string? setCode = null, string? collectorNumber = null)
        {
            if (_nameIndex == null || string.IsNullOrEmpty(name))
                return null;

            // Try exact set+number first
            if (!string.IsNullOrEmpty(setCode) && !string.IsNullOrEmpty(collectorNumber))
            {
                string key = $"{setCode.ToLowerInvariant()}:{collectorNumber.ToLowerInvariant()}";
                if (_setNumberIndex != null && _setNumberIndex.TryGetValue(key, out var exact))
                    return exact;
            }

            // Find by name
            if (!_nameIndex.TryGetValue(name.ToLowerInvariant(), out var entries) || entries.Count == 0)
                return null;

            // If set is specified, prefer that set
            if (!string.IsNullOrEmpty(setCode))
            {
                var setMatch = entries.FirstOrDefault(e =>
                    e.SetCode.Equals(setCode, StringComparison.OrdinalIgnoreCase));
                if (setMatch != null) return setMatch;
            }

            // Return first (most recent printing — bulk data is ordered by release date desc)
            return entries[0];
        }

        /// <summary>Searches for cards whose name contains the query string.</summary>
        public List<BulkCardEntry> SearchByName(string query, int maxResults = 50)
        {
            if (_nameIndex == null || string.IsNullOrEmpty(query))
                return new();

            string lowerQuery = query.ToLowerInvariant();
            var results = new List<BulkCardEntry>();

            foreach (var (name, entries) in _nameIndex)
            {
                if (name.Contains(lowerQuery) && entries.Count > 0)
                {
                    results.Add(entries[0]); // One per unique name
                    if (results.Count >= maxResults) break;
                }
            }

            return results;
        }

        private async Task<bool> DownloadAndBuildIndexAsync(Action<string>? onProgress = null)
        {
            try
            {
                // Step 1: Get the bulk data download URL
                onProgress?.Invoke("Fetching bulk data info from Scryfall...");
                var response = await _httpClient.GetStringAsync("https://api.scryfall.com/bulk-data");
                var bulkData = JObject.Parse(response);
                var defaultCards = bulkData["data"]?.FirstOrDefault(d => d["type"]?.ToString() == "default_cards");
                if (defaultCards == null)
                {
                    Log.Error("Could not find default_cards in bulk data response");
                    return false;
                }

                string downloadUrl = defaultCards["download_uri"]!.ToString();
                long totalSize = defaultCards["size"]?.Value<long>() ?? 0;
                string sizeStr = totalSize > 0 ? $" ({totalSize / 1024 / 1024}MB)" : "";

                // Step 2: Download and stream-parse the JSON (don't load entire file into memory)
                onProgress?.Invoke($"Downloading Scryfall card database{sizeStr}... (You can change the refresh frequency in Settings)");
                Log.Information("Downloading Scryfall bulk data from {Url} ({Size} bytes)", downloadUrl, totalSize);

                using var dataStream = await _httpClient.GetStreamAsync(downloadUrl);
                using var streamReader = new StreamReader(dataStream);
                using var jsonReader = new JsonTextReader(streamReader);

                var nameIndex = new Dictionary<string, List<BulkCardEntry>>(StringComparer.OrdinalIgnoreCase);
                var setNumberIndex = new Dictionary<string, BulkCardEntry>(StringComparer.OrdinalIgnoreCase);
                int cardCount = 0;

                onProgress?.Invoke("Processing card data...");

                // Stream-parse the JSON array
                if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.StartArray)
                {
                    Log.Error("Bulk data is not a JSON array");
                    return false;
                }

                var serializer = new JsonSerializer();
                while (jsonReader.Read())
                {
                    if (jsonReader.TokenType == JsonToken.EndArray) break;
                    if (jsonReader.TokenType != JsonToken.StartObject) continue;

                    var obj = serializer.Deserialize<JObject>(jsonReader);
                    if (obj == null) continue;

                    // Skip non-English, tokens, art series, etc.
                    string? lang = obj["lang"]?.ToString();
                    if (lang != null && lang != "en") continue;

                    string? layout = obj["layout"]?.ToString();
                    if (layout is "token" or "art_series" or "double_faced_token" or "emblem") continue;

                    string? id = obj["id"]?.ToString();
                    string? name = obj["name"]?.ToString();
                    string? setCode = obj["set"]?.ToString();
                    string? collectorNumber = obj["collector_number"]?.ToString();
                    string? rarity = obj["rarity"]?.ToString();

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

                    var entry = new BulkCardEntry
                    {
                        Id = id,
                        Name = name,
                        SetCode = setCode ?? string.Empty,
                        CollectorNumber = collectorNumber ?? string.Empty,
                        Rarity = rarity ?? string.Empty
                    };

                    // Add to name index (group by name)
                    string nameKey = name.ToLowerInvariant();
                    if (!nameIndex.TryGetValue(nameKey, out var list))
                    {
                        list = new List<BulkCardEntry>();
                        nameIndex[nameKey] = list;
                    }
                    list.Add(entry);

                    // Add to set+number index
                    if (!string.IsNullOrEmpty(setCode) && !string.IsNullOrEmpty(collectorNumber))
                    {
                        string snKey = $"{setCode.ToLowerInvariant()}:{collectorNumber.ToLowerInvariant()}";
                        setNumberIndex.TryAdd(snKey, entry);
                    }

                    cardCount++;
                    if (cardCount % 10000 == 0)
                        onProgress?.Invoke($"Processing card data... ({cardCount:N0} cards)");
                }

                Log.Information("Built bulk data index: {Cards} cards, {Names} unique names", cardCount, nameIndex.Count);
                onProgress?.Invoke($"Indexed {cardCount:N0} cards ({nameIndex.Count:N0} unique names)");

                // Step 3: Save index to disk for next startup
                onProgress?.Invoke("Saving card index...");
                var indexData = nameIndex.Values.SelectMany(l => l).ToList();
                string indexJson = JsonConvert.SerializeObject(indexData);
                await File.WriteAllTextAsync(_indexPath, indexJson);

                var meta = new BulkDataMeta { DownloadedAt = DateTime.UtcNow, CardCount = cardCount };
                await File.WriteAllTextAsync(_metaPath, JsonConvert.SerializeObject(meta));

                _nameIndex = nameIndex;
                _setNumberIndex = setNumberIndex;
                _isLoaded = true;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to download/process Scryfall bulk data");
                onProgress?.Invoke($"Bulk data download failed: {ex.Message}");
                return false;
            }
        }

        private void LoadIndex()
        {
            try
            {
                var json = File.ReadAllText(_indexPath);
                var entries = JsonConvert.DeserializeObject<List<BulkCardEntry>>(json);
                if (entries == null || entries.Count == 0) return;

                _nameIndex = new Dictionary<string, List<BulkCardEntry>>(StringComparer.OrdinalIgnoreCase);
                _setNumberIndex = new Dictionary<string, BulkCardEntry>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    string nameKey = entry.Name.ToLowerInvariant();
                    if (!_nameIndex.TryGetValue(nameKey, out var list))
                    {
                        list = new List<BulkCardEntry>();
                        _nameIndex[nameKey] = list;
                    }
                    list.Add(entry);

                    if (!string.IsNullOrEmpty(entry.SetCode) && !string.IsNullOrEmpty(entry.CollectorNumber))
                    {
                        string snKey = $"{entry.SetCode.ToLowerInvariant()}:{entry.CollectorNumber.ToLowerInvariant()}";
                        _setNumberIndex.TryAdd(snKey, entry);
                    }
                }

                _isLoaded = true;
                Log.Information("Loaded bulk data index from cache: {Count} entries, {Names} unique names",
                    entries.Count, _nameIndex.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load bulk data index from cache");
                _isLoaded = false;
            }
        }
    }

    public class BulkCardEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("set")]
        public string SetCode { get; set; } = string.Empty;

        [JsonProperty("cn")]
        public string CollectorNumber { get; set; } = string.Empty;

        [JsonProperty("r")]
        public string Rarity { get; set; } = string.Empty;
    }

    public class BulkDataMeta
    {
        [JsonProperty("downloadedAt")]
        public DateTime DownloadedAt { get; set; }

        [JsonProperty("cardCount")]
        public int CardCount { get; set; }
    }
}
