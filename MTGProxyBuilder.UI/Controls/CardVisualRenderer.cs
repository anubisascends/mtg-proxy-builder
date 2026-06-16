using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// Renders individual card visuals on a Canvas: images, placeholders,
    /// selection highlights, cut guides, overlay text, and card outlines.
    /// Extracted from GridEditorCanvas to isolate rendering logic.
    /// </summary>
    public static class CardVisualRenderer
    {
        private const float MmToPx = 96f / 25.4f;

        public static void PlaceCard(Canvas canvas, CardModel card, BitmapImage? bmp,
            float x, float y, float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageTop, float pageW, float pageH, bool flipped, bool selected,
            bool showCutGuides, PrintSettings? printSettings)
        {
            bool hasBackArt = !string.IsNullOrEmpty(card.BackArtworkPath);
            bool showNoBackPlaceholder = flipped && !hasBackArt;

            if (showNoBackPlaceholder)
            {
                DrawNoBackPlaceholder(canvas, x + bleed, y + bleed, cardW, cardH, card.Name);
            }
            else if (bmp != null)
            {
                var image = new Image { Source = bmp, Width = cardW, Height = cardH, Stretch = Stretch.Fill };
                Canvas.SetLeft(image, x + bleed); Canvas.SetTop(image, y + bleed); canvas.Children.Add(image);

                bool regMarksActive = printSettings?.ShowRegistrationMarks == true;
                if (bleed > 0 && !regMarksActive)
                {
                    var overlay = new Path
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), IsHitTestVisible = false
                    };
                    overlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                        new RectangleGeometry(new Rect(0, 0, cellW, cellH)),
                        new RectangleGeometry(new Rect(bleed, bleed, cardW, cardH)));
                    Canvas.SetLeft(overlay, x); Canvas.SetTop(overlay, y); canvas.Children.Add(overlay);
                }
            }
            else
            {
                DrawPlaceholder(canvas, card, x + bleed, y + bleed, cardW, cardH, flipped);
            }

            // Selection overlays are managed separately by GridEditorCanvas.UpdateSelectionOverlays()

            bool regMarksOn = printSettings?.ShowRegistrationMarks == true;
            if (showCutGuides && !regMarksOn)
                DrawCutGuides(canvas, x, y, bleed, cardW, cardH, pageTop, pageW, pageH);

            if (printSettings is { ShowCropMarks: true, ShowRegistrationMarks: false })
                DrawCropMarks(canvas, x, y, bleed, cardW, cardH, printSettings);

            if (!flipped && !string.IsNullOrEmpty(card.OverlayText))
                DrawOverlayText(canvas, card.OverlayText, x + bleed, y + bleed, cardW, cardH);

            if (printSettings is { ShowCardOutline: true, ShowRegistrationMarks: false })
                DrawOutline(canvas, x, y, cellW, cellH, bleed, cardW, cardH, printSettings);
        }

        public static void DrawNoBackPlaceholder(Canvas canvas, float x, float y, float w, float h, string cardName)
        {
            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 90, 90, 90)),
                StrokeThickness = 1, RadiusX = 4, RadiusY = 4
            };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); canvas.Children.Add(rect);

            var title = new TextBlock
            {
                Text = "No Back Art\nAssigned",
                Foreground = Brushes.White, FontSize = Math.Max(11, w / 12),
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Width = w - 10
            };
            Canvas.SetLeft(title, x + 5); Canvas.SetTop(title, y + h / 3 - 10); canvas.Children.Add(title);

            var name = new TextBlock
            {
                Text = cardName, Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                FontSize = Math.Max(8, w / 18),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Width = w - 10
            };
            Canvas.SetLeft(name, x + 5); Canvas.SetTop(name, y + h / 3 + 30); canvas.Children.Add(name);
        }

        public static void DrawPlaceholder(Canvas canvas, CardModel card, float x, float y, float w, float h, bool flipped)
        {
            var rect = new Rectangle
            {
                Width = w, Height = h,
                Fill = new SolidColorBrush(flipped ? Color.FromArgb(255, 80, 50, 50) : Color.FromArgb(255, 60, 60, 80)),
                Stroke = Brushes.Gray, StrokeThickness = 1, RadiusX = 4, RadiusY = 4
            };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); canvas.Children.Add(rect);

            var nb = new TextBlock
            {
                Text = flipped ? "(Back)\n" + card.Name : (string.IsNullOrEmpty(card.Name) ? "No Image" : card.Name),
                Foreground = Brushes.White, FontSize = Math.Max(10, w / 15),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Width = w - 10
            };
            Canvas.SetLeft(nb, x + 5); Canvas.SetTop(nb, y + h / 3); canvas.Children.Add(nb);
        }

        private static void DrawCutGuides(Canvas canvas, float x, float y, float bleed,
            float cardW, float cardH, float pageTop, float pageW, float pageH)
        {
            float cardLeft = x + bleed, cardTopY = y + bleed;
            float cardRight = cardLeft + cardW, cardBottomY = cardTopY + cardH;
            float pgBottom = pageTop + pageH;

            var pen = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            void Line(float x1, float y1, float x2, float y2)
            {
                var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = pen, StrokeThickness = 0.5, IsHitTestVisible = false };
                canvas.Children.Add(l);
            }
            Line(cardLeft, pageTop, cardLeft, cardTopY);
            Line(cardRight, pageTop, cardRight, cardTopY);
            Line(cardLeft, cardBottomY, cardLeft, pgBottom);
            Line(cardRight, cardBottomY, cardRight, pgBottom);
            Line(0, cardTopY, cardLeft, cardTopY);
            Line(cardRight, cardTopY, pageW, cardTopY);
            Line(0, cardBottomY, cardLeft, cardBottomY);
            Line(cardRight, cardBottomY, pageW, cardBottomY);
        }

        private static void DrawCropMarks(Canvas canvas, float x, float y, float bleed,
            float cardW, float cardH, PrintSettings ps)
        {
            float cardLeft = x + bleed, cardTop = y + bleed;
            float cardRight = cardLeft + cardW, cardBottom = cardTop + cardH;
            float markLen = ps.CropMarkLengthMm * MmToPx;
            float offset = ps.CropMarkOffsetMm * MmToPx;

            var pen = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
            void Mark(float x1, float y1, float x2, float y2)
            {
                var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = pen, StrokeThickness = 0.5, IsHitTestVisible = false };
                canvas.Children.Add(l);
            }

            // Top-left
            Mark(cardLeft, cardTop - offset, cardLeft, cardTop - offset - markLen);
            Mark(cardLeft - offset, cardTop, cardLeft - offset - markLen, cardTop);
            // Top-right
            Mark(cardRight, cardTop - offset, cardRight, cardTop - offset - markLen);
            Mark(cardRight + offset, cardTop, cardRight + offset + markLen, cardTop);
            // Bottom-left
            Mark(cardLeft, cardBottom + offset, cardLeft, cardBottom + offset + markLen);
            Mark(cardLeft - offset, cardBottom, cardLeft - offset - markLen, cardBottom);
            // Bottom-right
            Mark(cardRight, cardBottom + offset, cardRight, cardBottom + offset + markLen);
            Mark(cardRight + offset, cardBottom, cardRight + offset + markLen, cardBottom);
        }

        private static void DrawOverlayText(Canvas canvas, string text, float x, float y, float cardW, float cardH)
        {
            float bannerH = cardH * 0.15f;
            float bannerY = y + cardH - bannerH - cardH * 0.08f;

            var banner = new Rectangle
            {
                Width = cardW, Height = bannerH,
                Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(banner, x); Canvas.SetTop(banner, bannerY); canvas.Children.Add(banner);

            var overlayTb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = Math.Max(8, bannerH * 0.55),
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Width = cardW,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(overlayTb, x);
            Canvas.SetTop(overlayTb, bannerY + (bannerH - overlayTb.FontSize) / 2);
            canvas.Children.Add(overlayTb);
        }

        public static void DrawOutline(Canvas canvas, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH, PrintSettings ps)
        {
            SolidColorBrush brush;
            try
            {
                string hex = ps.OutlineColor.TrimStart('#');
                byte r = Convert.ToByte(hex[..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            catch { brush = new SolidColorBrush(Color.FromRgb(0x66, 0xFF, 0x00)); }
            brush.Freeze();

            float weight = ps.LineWeight * 0.5f;
            float radiusPx = ps.CornerRadiusMm * MmToPx;
            float cornerLenPx = ps.CornerLengthMm * MmToPx;

            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float offset = weight / 2;

            float ox, oy, ow, oh;
            switch (ps.OutlineAlignment)
            {
                case OutlineAlignment.Inside:
                    ox = cardLeft + offset; oy = cardTop + offset;
                    ow = cardW - 2 * offset; oh = cardH - 2 * offset;
                    break;
                case OutlineAlignment.Outside:
                    ox = cardLeft - offset; oy = cardTop - offset;
                    ow = cardW + 2 * offset; oh = cardH + 2 * offset;
                    break;
                default:
                    ox = cardLeft; oy = cardTop; ow = cardW; oh = cardH;
                    break;
            }

            if (ps.OutlineType == OutlineType.Full)
            {
                var rect = new Rectangle
                {
                    Width = ow, Height = oh,
                    Stroke = brush, StrokeThickness = weight,
                    RadiusX = radiusPx, RadiusY = radiusPx,
                    Fill = Brushes.Transparent, IsHitTestVisible = false
                };
                if (ps.OutlineLineType == LineType.Dashed)
                    rect.StrokeDashArray = new DoubleCollection { 4, 2 };
                Canvas.SetLeft(rect, ox); Canvas.SetTop(rect, oy);
                canvas.Children.Add(rect);
            }
            else // Corners
            {
                float rr = Math.Min(radiusPx, Math.Min(ow / 2, oh / 2));
                float len = Math.Min(cornerLenPx, Math.Min(ow / 2 - rr, oh / 2 - rr));
                if (len <= 0) len = 5;

                DoubleCollection? dashArray = ps.OutlineLineType == LineType.Dashed
                    ? new DoubleCollection { 4, 2 } : null;

                void AddLine(float x1, float y1, float x2, float y2)
                {
                    var l = new Line
                    {
                        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                        Stroke = brush, StrokeThickness = weight, IsHitTestVisible = false
                    };
                    if (dashArray != null) l.StrokeDashArray = dashArray;
                    canvas.Children.Add(l);
                }

                if (rr > 0)
                {
                    void AddCornerPath(Point lineStart, Point arcStart, Point arcEnd, Point lineEnd, SweepDirection sweep)
                    {
                        var fig = new PathFigure { StartPoint = lineStart, IsClosed = false, IsFilled = false };
                        fig.Segments.Add(new LineSegment(arcStart, true));
                        fig.Segments.Add(new ArcSegment(arcEnd, new Size(rr, rr), 0, false, sweep, true));
                        fig.Segments.Add(new LineSegment(lineEnd, true));
                        var geom = new PathGeometry(new[] { fig });
                        var path = new Path
                        {
                            Data = geom, Stroke = brush, StrokeThickness = weight, IsHitTestVisible = false
                        };
                        if (dashArray != null) path.StrokeDashArray = dashArray;
                        canvas.Children.Add(path);
                    }

                    AddCornerPath(new Point(ox, oy + rr + len), new Point(ox, oy + rr),
                        new Point(ox + rr, oy), new Point(ox + rr + len, oy), SweepDirection.Clockwise);
                    AddCornerPath(new Point(ox + ow - rr - len, oy), new Point(ox + ow - rr, oy),
                        new Point(ox + ow, oy + rr), new Point(ox + ow, oy + rr + len), SweepDirection.Clockwise);
                    AddCornerPath(new Point(ox + ow, oy + oh - rr - len), new Point(ox + ow, oy + oh - rr),
                        new Point(ox + ow - rr, oy + oh), new Point(ox + ow - rr - len, oy + oh), SweepDirection.Clockwise);
                    AddCornerPath(new Point(ox + rr + len, oy + oh), new Point(ox + rr, oy + oh),
                        new Point(ox, oy + oh - rr), new Point(ox, oy + oh - rr - len), SweepDirection.Clockwise);
                }
                else
                {
                    AddLine(ox, oy + len, ox, oy); AddLine(ox, oy, ox + len, oy);
                    AddLine(ox + ow - len, oy, ox + ow, oy); AddLine(ox + ow, oy, ox + ow, oy + len);
                    AddLine(ox + ow, oy + oh - len, ox + ow, oy + oh); AddLine(ox + ow, oy + oh, ox + ow - len, oy + oh);
                    AddLine(ox + len, oy + oh, ox, oy + oh); AddLine(ox, oy + oh, ox, oy + oh - len);
                }
            }
        }
    }
}
