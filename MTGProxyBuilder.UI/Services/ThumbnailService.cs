using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using Serilog;

namespace MTGProxyBuilder.UI.Services
{
    public class ThumbnailService
    {
        private readonly string _thumbnailDirectory;
        private const int ThumbnailWidth = 200;
        private const int JpegQuality = 85;

        public ThumbnailService(string libraryDirectory)
        {
            _thumbnailDirectory = Path.Combine(libraryDirectory, "Thumbnails");
            Directory.CreateDirectory(_thumbnailDirectory);
        }

        public string GetThumbnailPath(string entryId)
            => Path.Combine(_thumbnailDirectory, $"{entryId}.jpg");

        public bool HasThumbnail(string entryId)
            => File.Exists(GetThumbnailPath(entryId));

        /// <summary>Returns existing thumbnail path or generates a new one. Safe for background threads.</summary>
        public string? GetOrCreate(string entryId, string sourceFilePath)
        {
            var thumbPath = GetThumbnailPath(entryId);
            if (File.Exists(thumbPath))
                return thumbPath;
            return Generate(entryId, sourceFilePath);
        }

        /// <summary>Generates a JPEG thumbnail from the source image. Safe for background threads.</summary>
        public string? Generate(string entryId, string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath))
                return null;

            try
            {
                var thumbPath = GetThumbnailPath(entryId);

                var source = new BitmapImage();
                source.BeginInit();
                source.UriSource = new Uri(sourceFilePath, UriKind.Absolute);
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.DecodePixelWidth = ThumbnailWidth;
                source.EndInit();
                source.Freeze();

                var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var stream = File.Create(thumbPath);
                encoder.Save(stream);

                return thumbPath;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to generate thumbnail for {EntryId} from {Path}", entryId, sourceFilePath);
                return null;
            }
        }

        /// <summary>Deletes the thumbnail for a specific entry.</summary>
        public void Delete(string entryId)
        {
            var path = GetThumbnailPath(entryId);
            if (File.Exists(path))
                try { File.Delete(path); } catch (Exception ex) { Log.Warning(ex, "Failed to delete thumbnail {Path}", path); }
        }

        /// <summary>Deletes all thumbnails in the directory.</summary>
        public void DeleteAll()
        {
            if (!Directory.Exists(_thumbnailDirectory)) return;
            foreach (var file in Directory.GetFiles(_thumbnailDirectory, "*.jpg"))
                try { File.Delete(file); } catch (Exception ex) { Log.Warning(ex, "Failed to delete thumbnail file {File}", file); }
        }

        /// <summary>Regenerates thumbnails for all provided entries. Call from a background thread.</summary>
        public int RegenerateAll(IReadOnlyList<(string Id, string FilePath)> entries,
            Action<int, int>? onProgress = null, CancellationToken ct = default)
        {
            DeleteAll();
            int generated = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (id, path) = entries[i];
                if (Generate(id, path) != null)
                    generated++;
                onProgress?.Invoke(i + 1, entries.Count);
            }
            return generated;
        }
    }
}
