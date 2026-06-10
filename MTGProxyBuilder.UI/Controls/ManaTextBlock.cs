using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;
using Svg.Skia;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// A TextBlock-like control that renders mana symbols inline.
    /// Text like "{2}{W}{U}" renders as inline images for each symbol.
    /// Plain text and unrecognized symbols render as normal text.
    /// </summary>
    public class ManaTextBlock : TextBlock
    {
        private static readonly Dictionary<string, BitmapSource> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);

        public static readonly DependencyProperty ManaTextProperty =
            DependencyProperty.Register(nameof(ManaText), typeof(string), typeof(ManaTextBlock),
                new PropertyMetadata(null, OnManaTextChanged));

        /// <summary>
        /// The symbol size in pixels (height of each inline mana icon).
        /// Defaults to matching the FontSize.
        /// </summary>
        public static readonly DependencyProperty SymbolSizeProperty =
            DependencyProperty.Register(nameof(SymbolSize), typeof(double), typeof(ManaTextBlock),
                new PropertyMetadata(0.0, OnManaTextChanged));

        public string? ManaText
        {
            get => (string?)GetValue(ManaTextProperty);
            set => SetValue(ManaTextProperty, value);
        }

        public double SymbolSize
        {
            get => (double)GetValue(SymbolSizeProperty);
            set => SetValue(SymbolSizeProperty, value);
        }

        private static void OnManaTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ManaTextBlock mtb)
                mtb.UpdateInlines();
        }

        private void UpdateInlines()
        {
            Inlines.Clear();

            string? text = ManaText;
            if (string.IsNullOrEmpty(text))
                return;

            double size = SymbolSize > 0 ? SymbolSize : FontSize;
            int renderSize = Math.Max(16, (int)(size * 1.5)); // Render at 1.5x for crispness

            var segments = ManaSymbolProvider.ParseManaText(text);

            foreach (var segment in segments)
            {
                if (!segment.IsSymbol)
                {
                    Inlines.Add(new Run(segment.Value));
                    continue;
                }

                var bitmap = GetOrRenderSymbol(segment.Value, renderSize);
                if (bitmap != null)
                {
                    var image = new Image
                    {
                        Source = bitmap,
                        Width = size,
                        Height = size,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(1, 0, 1, 0)
                    };

                    // Align the image with the text baseline
                    image.SetValue(ToolTipProperty, $"{{{segment.Value}}}");
                    Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
                }
                else
                {
                    // Unknown symbol — render as text
                    Inlines.Add(new Run($"{{{segment.Value}}}") { Foreground = Brushes.Gray });
                }
            }
        }

        private static BitmapSource? GetOrRenderSymbol(string symbolName, int renderSize)
        {
            string cacheKey = $"{symbolName}_{renderSize}";
            if (_bitmapCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var svgContent = ManaSymbolProvider.GetSvgContent(symbolName);
            if (svgContent == null) return null;

            try
            {
                var svg = new SKSvg();
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
                svg.Load(stream);

                if (svg.Picture == null) return null;

                var bounds = svg.Picture.CullRect;
                float scale = renderSize / Math.Max(bounds.Width, bounds.Height);

                int width = (int)(bounds.Width * scale);
                int height = (int)(bounds.Height * scale);
                if (width <= 0 || height <= 0) return null;

                using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);
                canvas.DrawPicture(svg.Picture);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(data.ToArray());
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                _bitmapCache[cacheKey] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
