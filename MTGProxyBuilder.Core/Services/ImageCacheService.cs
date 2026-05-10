namespace MTGProxyBuilder.Core.Services
{
    public class ImageCacheService
    {
        private readonly string _cacheDirectory;

        public ImageCacheService()
        {
            _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "ImageCache");
            Directory.CreateDirectory(_cacheDirectory);
        }

        public string CacheDirectory => _cacheDirectory;

        public async Task<string?> CacheImageFromUrlAsync(HttpClient httpClient, string imageUrl, string cardId)
        {
            try
            {
                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".jpg";

                string fileName = $"{cardId}{extension}";
                string filePath = Path.Combine(_cacheDirectory, fileName);

                if (File.Exists(filePath))
                    return filePath;

                var imageData = await httpClient.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(filePath, imageData);
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image cache error: {ex.Message}");
                return null;
            }
        }

        public bool IsImageCached(string cardId)
        {
            var files = Directory.GetFiles(_cacheDirectory, $"{cardId}.*");
            return files.Length > 0;
        }

        public string? GetCachedImagePath(string cardId)
        {
            var files = Directory.GetFiles(_cacheDirectory, $"{cardId}.*");
            return files.Length > 0 ? files[0] : null;
        }

        public void ClearCache()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                    File.Delete(file);
            }
        }
    }
}
