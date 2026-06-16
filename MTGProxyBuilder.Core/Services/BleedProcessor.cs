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
                string outputPath = Path.Combine(_cacheDir, $"{hash}.jpg");

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

                // Extend edges using multi-pixel weighted sampling for smooth gradients.
                // Sample several rows/columns from the edge and blend them with decreasing
                // weight, then apply a gaussian blur to the bleed region for a natural look.
                int sampleDepth = Math.Min(8, Math.Min(srcW, srcH) / 2);

                // Top edge: blend multiple top rows, fade outward
                FillEdge(output, source, bleedPixels, sampleDepth,
                    EdgeSide.Top, srcW, srcH, bleedPixels);

                // Bottom edge
                FillEdge(output, source, bleedPixels, sampleDepth,
                    EdgeSide.Bottom, srcW, srcH, bleedPixels);

                // Left edge
                FillEdge(output, source, bleedPixels, sampleDepth,
                    EdgeSide.Left, srcW, srcH, bleedPixels);

                // Right edge
                FillEdge(output, source, bleedPixels, sampleDepth,
                    EdgeSide.Right, srcW, srcH, bleedPixels);

                // Corners: sample from the corner region and fill with averaged color
                FillCorner(output, source, 0, 0, bleedPixels, bleedPixels, sampleDepth, 0, 0);
                FillCorner(output, source, bleedPixels + srcW, 0, bleedPixels, bleedPixels, sampleDepth, srcW - 1, 0);
                FillCorner(output, source, 0, bleedPixels + srcH, bleedPixels, bleedPixels, sampleDepth, 0, srcH - 1);
                FillCorner(output, source, bleedPixels + srcW, bleedPixels + srcH, bleedPixels, bleedPixels, sampleDepth, srcW - 1, srcH - 1);

                // Apply gaussian blur to just the bleed regions for a smooth finish.
                // We do this by rendering the output through a blur filter, then
                // re-compositing the sharp original on top.
                float blurSigma = bleedPixels * 0.4f;
                using var blurred = new SKBitmap(outW, outH);
                using var blurCanvas = new SKCanvas(blurred);
                using var blurPaint = new SKPaint();
                blurPaint.ImageFilter = SKImageFilter.CreateBlur(blurSigma, blurSigma);
                blurCanvas.DrawBitmap(output, 0, 0, blurPaint);

                // Composite: replace center with the sharp original (blur only affects bleed)
                blurCanvas.DrawBitmap(source, bleedPixels, bleedPixels);

                // Save as JPEG (much faster than PNG, fine for print)
                using var stream = File.OpenWrite(outputPath);
                blurred.Encode(stream, SKEncodedImageFormat.Jpeg, 95);

                _processedCache[cacheKey] = outputPath;
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bleed processing error: {ex.Message}");
                return sourcePath; // Fall back to original
            }
        }

        private enum EdgeSide { Top, Bottom, Left, Right }

        /// <summary>
        /// Fill a bleed edge by sampling multiple rows/columns from the source and
        /// blending them with a weighted average that fades toward the outer edge.
        /// This produces a smooth gradient instead of hard banding from single-pixel stretch.
        /// </summary>
        private static void FillEdge(SKBitmap output, SKBitmap source, int bleedPx, int sampleDepth,
            EdgeSide side, int srcW, int srcH, int offset)
        {
            bool horizontal = side == EdgeSide.Top || side == EdgeSide.Bottom;
            int length = horizontal ? srcW : srcH;

            for (int pos = 0; pos < length; pos++)
            {
                // Sample several pixels from the edge inward and compute weighted average
                float totalWeight = 0;
                float r = 0, g = 0, b = 0, a = 0;
                for (int d = 0; d < sampleDepth; d++)
                {
                    // Weight decreases with distance from edge: 1.0, 0.5, 0.25, ...
                    float weight = 1f / (1 + d);
                    SKColor pixel = side switch
                    {
                        EdgeSide.Top => source.GetPixel(pos, d),
                        EdgeSide.Bottom => source.GetPixel(pos, srcH - 1 - d),
                        EdgeSide.Left => source.GetPixel(d, pos),
                        EdgeSide.Right => source.GetPixel(srcW - 1 - d, pos),
                        _ => SKColors.Black
                    };
                    r += pixel.Red * weight;
                    g += pixel.Green * weight;
                    b += pixel.Blue * weight;
                    a += pixel.Alpha * weight;
                    totalWeight += weight;
                }

                var blendedColor = new SKColor(
                    (byte)(r / totalWeight),
                    (byte)(g / totalWeight),
                    (byte)(b / totalWeight),
                    (byte)(a / totalWeight));

                // Fill the bleed strip with the blended color
                for (int bp = 0; bp < bleedPx; bp++)
                {
                    int outX, outY;
                    switch (side)
                    {
                        case EdgeSide.Top:
                            outX = offset + pos;
                            outY = bleedPx - 1 - bp;
                            break;
                        case EdgeSide.Bottom:
                            outX = offset + pos;
                            outY = offset + srcH + bp;
                            break;
                        case EdgeSide.Left:
                            outX = bleedPx - 1 - bp;
                            outY = offset + pos;
                            break;
                        default: // Right
                            outX = offset + srcW + bp;
                            outY = offset + pos;
                            break;
                    }
                    output.SetPixel(outX, outY, blendedColor);
                }
            }
        }

        /// <summary>
        /// Fill a corner bleed region by averaging pixels from the corner area of the source.
        /// </summary>
        private static void FillCorner(SKBitmap output, SKBitmap source,
            int destX, int destY, int w, int h, int sampleDepth, int cornerX, int cornerY)
        {
            int srcW = source.Width;
            int srcH = source.Height;

            // Average color from the corner region
            float totalWeight = 0;
            float r = 0, g = 0, b = 0, a = 0;
            for (int dy = 0; dy < sampleDepth; dy++)
            {
                for (int dx = 0; dx < sampleDepth; dx++)
                {
                    int sx = Math.Clamp(cornerX + (cornerX == 0 ? dx : -dx), 0, srcW - 1);
                    int sy = Math.Clamp(cornerY + (cornerY == 0 ? dy : -dy), 0, srcH - 1);
                    float weight = 1f / (1 + dx + dy);
                    var pixel = source.GetPixel(sx, sy);
                    r += pixel.Red * weight;
                    g += pixel.Green * weight;
                    b += pixel.Blue * weight;
                    a += pixel.Alpha * weight;
                    totalWeight += weight;
                }
            }

            var color = new SKColor(
                (byte)(r / totalWeight),
                (byte)(g / totalWeight),
                (byte)(b / totalWeight),
                (byte)(a / totalWeight));

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    output.SetPixel(destX + x, destY + y, color);
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
