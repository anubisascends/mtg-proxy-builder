using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MTGProxyBuilder.Core.Services;
using SkiaSharp;
using Svg.Skia;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// Displays a set symbol SVG as an image, given a SetCode and Rarity.
    /// </summary>
    public class SetSymbolImage : Image
    {
        private static readonly Dictionary<string, BitmapSource> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);

        public static readonly DependencyProperty SetCodeProperty =
            DependencyProperty.Register(nameof(SetCode), typeof(string), typeof(SetSymbolImage),
                new PropertyMetadata(null, OnPropertyChanged));

        public static readonly DependencyProperty RarityProperty =
            DependencyProperty.Register(nameof(Rarity), typeof(string), typeof(SetSymbolImage),
                new PropertyMetadata(null, OnPropertyChanged));

        public static readonly DependencyProperty SymbolSizeProperty =
            DependencyProperty.Register(nameof(SymbolSize), typeof(double), typeof(SetSymbolImage),
                new PropertyMetadata(16.0, OnPropertyChanged));

        public string? SetCode
        {
            get => (string?)GetValue(SetCodeProperty);
            set => SetValue(SetCodeProperty, value);
        }

        public string? Rarity
        {
            get => (string?)GetValue(RarityProperty);
            set => SetValue(RarityProperty, value);
        }

        public double SymbolSize
        {
            get => (double)GetValue(SymbolSizeProperty);
            set => SetValue(SymbolSizeProperty, value);
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SetSymbolImage img)
                img.UpdateImage();
        }

        private void UpdateImage()
        {
            string? setCode = SetCode;
            string? rarity = Rarity;

            if (string.IsNullOrEmpty(setCode))
            {
                Source = null;
                Visibility = Visibility.Collapsed;
                return;
            }

            double size = SymbolSize;
            int renderSize = Math.Max(16, (int)(size * 2)); // Render at 2x for crispness

            string cacheKey = $"set_{setCode}_{rarity}_{renderSize}";
            if (_bitmapCache.TryGetValue(cacheKey, out var cached))
            {
                Source = cached;
                Width = size;
                Height = size;
                Visibility = Visibility.Visible;
                return;
            }

            var svgContent = SetSymbolProvider.GetSvgContent(setCode, rarity ?? "common");
            if (svgContent == null)
            {
                Source = null;
                Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var svg = new SKSvg();
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent));
                svg.Load(stream);

                if (svg.Picture == null)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                var bounds = svg.Picture.CullRect;
                float scale = renderSize / Math.Max(bounds.Width, bounds.Height);

                int w = (int)(bounds.Width * scale);
                int h = (int)(bounds.Height * scale);
                if (w <= 0 || h <= 0) { Visibility = Visibility.Collapsed; return; }

                using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
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
                Source = bitmap;
                Width = size;
                Height = size;
                Visibility = Visibility.Visible;
            }
            catch
            {
                Source = null;
                Visibility = Visibility.Collapsed;
            }
        }
    }
}
