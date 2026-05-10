using SkiaSharp;
using System.Collections.Concurrent;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>
    /// Generates bleed-extended images by stretching edge pixels outward.
    /// Results are cached to disk so each source image is only processed once per bleed size.
    /// </summary>
    public class BleedProcessor
    {
        private readonly string _cacheDir;
        private static readonly ConcurrentDictionary<string, string> _processedCache = new();

        public BleedProcessor()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "BleedCache");
            Directory.CreateDirectory(_cacheDir);
        }

        /// <summary>
        /// Returns the path to a bleed-extended version of the source image.
        /// The bleed area is filled by stretching the outermost edge pixels outward.
        /// </summary>
        /// <param name="sourcePath">Path to the original card image.</param>
        /// <param name="bleedPixels">How many pixels of bleed to add on each side.</param>
        public string? GetBleedExtendedImage(string sourcePath, int bleedPixels)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || bleedPixels <= 0)
                return sourcePath; // No bleed needed, return original

            string cacheKey = $"{sourcePath}|{bleedPixels}";
            if (_processedCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                return cached;

            try
            {
                string hash = $"{Path.GetFileNameWithoutExtension(sourcePath)}_{sourcePath.GetHashCode():X8}_b{bleedPixels}";
                string outputPath = Path.Combine(_cacheDir, $"{hash}.png");

                if (File.Exists(outputPath))
                {
                    _processedCache[cacheKey] = outputPath;
                    return outputPath;
                }

                using var source = SKBitmap.Decode(sourcePath);
                if (source == null) return sourcePath;

                int srcW = source.Width;
                int srcH = source.Height;
                int outW = srcW + 2 * bleedPixels;
                int outH = srcH + 2 * bleedPixels;

                using var output = new SKBitmap(outW, outH);
                using var canvas = new SKCanvas(output);

                // Draw original image centered
                canvas.DrawBitmap(source, bleedPixels, bleedPixels);

                // Extend edges: take a 1-pixel strip from each edge and stretch it outward

                // Top edge: stretch top row upward
                using (var topStrip = new SKBitmap(srcW, 1))
                {
                    for (int x = 0; x < srcW; x++)
                        topStrip.SetPixel(x, 0, source.GetPixel(x, 0));
                    canvas.DrawBitmap(topStrip, new SKRect(0, 0, srcW, 1),
                        new SKRect(bleedPixels, 0, bleedPixels + srcW, bleedPixels));
                }

                // Bottom edge: stretch bottom row downward
                using (var bottomStrip = new SKBitmap(srcW, 1))
                {
                    for (int x = 0; x < srcW; x++)
                        bottomStrip.SetPixel(x, 0, source.GetPixel(x, srcH - 1));
                    canvas.DrawBitmap(bottomStrip, new SKRect(0, 0, srcW, 1),
                        new SKRect(bleedPixels, bleedPixels + srcH, bleedPixels + srcW, outH));
                }

                // Left edge: stretch left column leftward
                using (var leftStrip = new SKBitmap(1, srcH))
                {
                    for (int y = 0; y < srcH; y++)
                        leftStrip.SetPixel(0, y, source.GetPixel(0, y));
                    canvas.DrawBitmap(leftStrip, new SKRect(0, 0, 1, srcH),
                        new SKRect(0, bleedPixels, bleedPixels, bleedPixels + srcH));
                }

                // Right edge: stretch right column rightward
                using (var rightStrip = new SKBitmap(1, srcH))
                {
                    for (int y = 0; y < srcH; y++)
                        rightStrip.SetPixel(0, y, source.GetPixel(srcW - 1, y));
                    canvas.DrawBitmap(rightStrip, new SKRect(0, 0, 1, srcH),
                        new SKRect(bleedPixels + srcW, bleedPixels, outW, bleedPixels + srcH));
                }

                // Corners: fill with the corner pixel color
                var topLeft = source.GetPixel(0, 0);
                var topRight = source.GetPixel(srcW - 1, 0);
                var bottomLeft = source.GetPixel(0, srcH - 1);
                var bottomRight = source.GetPixel(srcW - 1, srcH - 1);

                using var paint = new SKPaint();
                paint.Color = topLeft;
                canvas.DrawRect(0, 0, bleedPixels, bleedPixels, paint);
                paint.Color = topRight;
                canvas.DrawRect(bleedPixels + srcW, 0, bleedPixels, bleedPixels, paint);
                paint.Color = bottomLeft;
                canvas.DrawRect(0, bleedPixels + srcH, bleedPixels, bleedPixels, paint);
                paint.Color = bottomRight;
                canvas.DrawRect(bleedPixels + srcW, bleedPixels + srcH, bleedPixels, bleedPixels, paint);

                // Save
                using var stream = File.OpenWrite(outputPath);
                output.Encode(stream, SKEncodedImageFormat.Png, 95);

                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bleed processing error: {ex.Message}");
                return sourcePath; // Fall back to original
            }
        }

        public void ClearCache()
        {
            _processedCache.Clear();
            if (Directory.Exists(_cacheDir))
                foreach (var f in Directory.GetFiles(_cacheDir))
                    try { File.Delete(f); } catch { }
        }
    }
}
