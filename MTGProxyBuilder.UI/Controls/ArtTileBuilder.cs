using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// Builds art option tiles and action tiles used across art selector and library dialogs.
    /// Centralizes tile sizing, colors, and layout for visual consistency.
    /// </summary>
    public static class ArtTileBuilder
    {
        public const double TileWidth = 110;
        public const double TileHeight = 165;
        public const double ImageHeight = 125;
        public const double LabelFontSize = 9.5;
        public const double DetailFontSize = 8;

        /// <summary>Creates an art option tile with image, label, and detail text.</summary>
        public static Border CreateOptionTile(string label, string imagePath, bool isCurrent, string detail,
            int decodePixelWidth = 220)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = isCurrent ? Brushes.DodgerBlue : Brushes.Transparent,
                ToolTip = $"{label}\n{detail}"
            };

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.EndInit();
                bmp.Freeze();
                imgBorder.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
            catch
            {
                imgBorder.Child = new TextBlock
                {
                    Text = "?", Foreground = Brushes.Gray, FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label + (isCurrent ? " *" : ""),
                Foreground = AppBrushes.TextSecondary,
                FontSize = LabelFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = AppBrushes.TextMuted,
                FontSize = DetailFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;
            return border;
        }

        /// <summary>Creates an option tile with deferred image loading (Image control returned for later assignment).</summary>
        public static (Border Border, Image ImageControl) CreateDeferredTile(string label, string detail,
            Brush? labelForeground = null, Brush? borderBrush = null)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = borderBrush ?? Brushes.Transparent,
                ToolTip = $"{label}\n{detail}"
            };

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            imgBorder.Child = img;
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = labelForeground ?? AppBrushes.TextSecondary,
                FontSize = LabelFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = AppBrushes.TextMuted,
                FontSize = DetailFontSize, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;
            return (border, img);
        }

        /// <summary>Creates a placeholder tile with a "Loading..." indicator and empty Image for later assignment.</summary>
        public static (Border Border, Image ImageControl) CreatePlaceholderTile(string label, string detail)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                ToolTip = $"{label}\n{detail}"
            };

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var grid = new System.Windows.Controls.Grid();
            var loadingText = new TextBlock
            {
                Text = "Loading...", Foreground = Brushes.Gray, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(loadingText);
            var img = new Image { Stretch = Stretch.UniformToFill };
            grid.Children.Add(img);
            imgBorder.Child = grid;
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = AppBrushes.TextSecondary,
                FontSize = LabelFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = AppBrushes.TextMuted,
                FontSize = DetailFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;
            return (border, img);
        }

        /// <summary>Creates an action tile ("+Add to Library", "Browse File...", etc.).</summary>
        public static Border CreateActionTile(string label)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.ActionTileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = AppBrushes.Border
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(new TextBlock
            {
                Text = "+", FontSize = 28, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10,
                Foreground = AppBrushes.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            border.Child = stack;
            return border;
        }
    }
}
