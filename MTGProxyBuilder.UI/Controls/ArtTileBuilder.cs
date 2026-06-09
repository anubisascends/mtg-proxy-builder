using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        /// <summary>Creates an art option tile with image, source/DPI info panel, and optional tags button.</summary>
        public static Border CreateOptionTile(string name, string imagePath, bool isCurrent,
            string source, int dpi = 0, List<string>? tags = null,
            Action<string>? onSourceClick = null, Action<string>? onTagClick = null,
            int decodePixelWidth = 220)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = isCurrent ? Brushes.DodgerBlue : Brushes.Transparent,
                ToolTip = name
            };

            var outerGrid = new Grid();

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

            stack.Children.Add(BuildInfoPanel(source, dpi, onSourceClick));

            outerGrid.Children.Add(stack);

            if (tags != null && tags.Count > 0)
                outerGrid.Children.Add(BuildTagsButton(tags, onTagClick));

            border.Child = outerGrid;
            return border;
        }

        /// <summary>Creates an option tile with deferred image loading (Image control returned for later assignment).</summary>
        public static (Border Border, Image ImageControl) CreateDeferredTile(string name,
            string source, int dpi = 0, List<string>? tags = null,
            Brush? borderBrush = null, Brush? sourceForeground = null,
            Action<string>? onSourceClick = null, Action<string>? onTagClick = null)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = borderBrush ?? Brushes.Transparent,
                ToolTip = name
            };

            var outerGrid = new Grid();

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            imgBorder.Child = img;
            stack.Children.Add(imgBorder);

            stack.Children.Add(BuildInfoPanel(source, dpi, onSourceClick, sourceForeground));

            outerGrid.Children.Add(stack);

            if (tags != null && tags.Count > 0)
                outerGrid.Children.Add(BuildTagsButton(tags, onTagClick));

            border.Child = outerGrid;
            return (border, img);
        }

        /// <summary>Creates a placeholder tile with a "Loading..." indicator and empty Image for later assignment.</summary>
        public static (Border Border, Image ImageControl) CreatePlaceholderTile(string name,
            string source, int dpi = 0, List<string>? tags = null,
            Action<string>? onSourceClick = null, Action<string>? onTagClick = null)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                ToolTip = name
            };

            var outerGrid = new Grid();

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var imgGrid = new Grid();
            var loadingText = new TextBlock
            {
                Text = "Loading...", Foreground = Brushes.Gray, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            imgGrid.Children.Add(loadingText);
            var img = new Image { Stretch = Stretch.UniformToFill };
            imgGrid.Children.Add(img);
            imgBorder.Child = imgGrid;
            stack.Children.Add(imgBorder);

            stack.Children.Add(BuildInfoPanel(source, dpi, onSourceClick));

            outerGrid.Children.Add(stack);

            if (tags != null && tags.Count > 0)
                outerGrid.Children.Add(BuildTagsButton(tags, onTagClick));

            border.Child = outerGrid;
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

        // ================================================================
        //  HELPERS
        // ================================================================

        private static StackPanel BuildInfoPanel(string source, int dpi,
            Action<string>? onSourceClick, Brush? sourceForeground = null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(3, 2, 3, 2)
            };

            var sourceBlock = new TextBlock
            {
                Text = source,
                Foreground = sourceForeground ?? AppBrushes.AccentCyan,
                FontSize = 8,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = dpi > 0 ? 62 : 100,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (onSourceClick != null)
            {
                sourceBlock.TextDecorations = TextDecorations.Underline;
                sourceBlock.Cursor = Cursors.Hand;
                sourceBlock.MouseLeftButtonUp += (_, e) =>
                {
                    onSourceClick(source);
                    e.Handled = true;
                };
            }
            panel.Children.Add(sourceBlock);

            if (dpi > 0)
            {
                var dpiBlock = new TextBlock
                {
                    Text = $"{dpi} DPI",
                    Foreground = AppBrushes.TextMuted,
                    FontSize = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0)
                };
                panel.Children.Add(dpiBlock);
            }

            return panel;
        }

        private static Border BuildTagsButton(List<string> tags, Action<string>? onTagClick)
        {
            var button = new Border
            {
                Width = 16, Height = 16,
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 3, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Tags: " + string.Join(", ", tags)
            };

            button.Child = new TextBlock
            {
                Text = "\u25BC",
                Foreground = Brushes.LightGray,
                FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.MouseLeftButtonUp += (sender, e) =>
            {
                ShowTagsPopup(sender as UIElement, tags, onTagClick);
                e.Handled = true;
            };

            return button;
        }

        private static void ShowTagsPopup(UIElement? placementTarget, List<string> tags, Action<string>? onTagClick)
        {
            var popup = new Popup
            {
                Placement = PlacementMode.Bottom,
                PlacementTarget = placementTarget,
                StaysOpen = false,
                AllowsTransparency = true
            };

            var outerBorder = new Border
            {
                Background = AppBrushes.PanelBg,
                BorderBrush = AppBrushes.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4)
            };

            var wrap = new WrapPanel { MaxWidth = 200 };

            foreach (var tag in tags)
            {
                var pill = new Border
                {
                    Background = AppBrushes.TileBg,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(2),
                    Cursor = onTagClick != null ? Cursors.Hand : Cursors.Arrow,
                    ToolTip = $"Filter by: {tag}"
                };

                pill.Child = new TextBlock
                {
                    Text = tag,
                    Foreground = AppBrushes.TextSecondary,
                    FontSize = 9
                };

                if (onTagClick != null)
                {
                    string capturedTag = tag;
                    pill.MouseLeftButtonUp += (_, ev) =>
                    {
                        onTagClick(capturedTag);
                        ev.Handled = true;
                    };
                }

                wrap.Children.Add(pill);
            }

            outerBorder.Child = wrap;
            popup.Child = outerBorder;
            popup.IsOpen = true;
        }
    }
}
