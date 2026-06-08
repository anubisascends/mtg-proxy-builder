using Newtonsoft.Json;
using Serilog;

namespace MTGProxyBuilder.Core.Services
{
    public class ImageCacheService
    {
        private readonly string _cacheDirectory;
        private readonly string _metadataPath;
        // cardId -> full path; avoids Directory.GetFiles per lookup
        private readonly Dictionary<string, string> _fileIndex = new(StringComparer.OrdinalIgnoreCase);
        // cardId -> (displayName, source) for resolving cache entries back to meaningful names
        private Dictionary<string, CachedImageMeta> _metaIndex = new(StringComparer.OrdinalIgnoreCase);

        public ImageCacheService()
        {
            _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "ImageCache");
            Directory.CreateDirectory(_cacheDirectory);
            _metadataPath = Path.Combine(_cacheDirectory, "metadata.json");
            RebuildIndex();
            LoadMetadata();
        }

        public string CacheDirectory => _cacheDirectory;

        private void RebuildIndex()
        {
            _fileIndex.Clear();
            foreach (var file in Directory.GetFiles(_cacheDirectory))
                _fileIndex[Path.GetFileNameWithoutExtension(file)] = file;
        }

        public async Task<string?> CacheImageFromUrlAsync(HttpClient httpClient, string imageUrl, string cardId)
        {
            try
            {
                if (_fileIndex.TryGetValue(cardId, out var existing))
                    return existing;

                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".jpg";

                string fileName = $"{cardId}{extension}";
                string filePath = Path.Combine(_cacheDirectory, fileName);

                var imageData = await httpClient.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(filePath, imageData);
                _fileIndex[cardId] = filePath;
                return filePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to cache image from {Url} for {CardId}", imageUrl, cardId);
                return null;
            }
        }

        public bool IsImageCached(string cardId)
        {
            return _fileIndex.ContainsKey(cardId);
        }

        public string? GetCachedImagePath(string cardId)
        {
            return _fileIndex.TryGetValue(cardId, out var path) ? path : null;
        }

        /// <summary>Store display metadata for a cached image.</summary>
        public void SetMetadata(string cardId, string displayName, string source)
        {
            _metaIndex[cardId] = new CachedImageMeta { Name = displayName, Source = source };
            SaveMetadata();
        }

        /// <summary>Returns all cached file paths whose key starts with the given prefix, with metadata.</summary>
        public List<(string Key, string Path, string Name, string Source)> GetCachedByPrefix(string prefix)
        {
            return _fileIndex
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(kv =>
                {
                    _metaIndex.TryGetValue(kv.Key, out var meta);
                    return (kv.Key, kv.Value,
                            Name: meta?.Name ?? Path.GetFileNameWithoutExtension(kv.Value),
                            Source: meta?.Source ?? "");
                })
                .ToList();
        }

        private void LoadMetadata()
        {
            try
            {
                if (File.Exists(_metadataPath))
                {
                    var json = File.ReadAllText(_metadataPath);
                    _metaIndex = JsonConvert.DeserializeObject<Dictionary<string, CachedImageMeta>>(json)
                        ?? new(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load image cache metadata from {Path}", _metadataPath);
                _metaIndex = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SaveMetadata()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_metaIndex, Formatting.Indented);
                File.WriteAllText(_metadataPath, json);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to save image cache metadata"); }
        }

        /// <summary>Removes a single cached image by its card ID key.</summary>
        public bool Remove(string cardId)
        {
            if (!_fileIndex.TryGetValue(cardId, out var path))
                return false;

            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete cached file {Path}", path); return false; }
            }

            _fileIndex.Remove(cardId);
            if (_metaIndex.Remove(cardId))
                SaveMetadata();
            return true;
        }

        public void ClearCache()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { Log.Warning(ex, "Failed to delete cache file {File}", file); }
                }
            }
            _fileIndex.Clear();
            _metaIndex.Clear();
            SaveMetadata();
        }
    }

    public class CachedImageMeta
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("source")]
        public string Source { get; set; } = string.Empty;
    }
}
