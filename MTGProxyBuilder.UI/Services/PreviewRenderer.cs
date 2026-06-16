using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Services
{
    public class PreviewRenderer
    {
        private const float MmToPt = 72f / 25.4f;
        private const float InToPt = 72f;
        private const float DefaultDpi = 150f;
        private float _dpi = DefaultDpi;
        private float Scale => _dpi / 72f;
        private readonly BleedProcessor _bleedProcessor = new();

        public Task<List<BitmapSource>> RenderAllPagesAsync(ProjectModel project,
            float backOffsetXMm = 0, float backOffsetYMm = 0, float dpi = DefaultDpi)
        {
            return Task.Run(() =>
            {
                _dpi = dpi;
                var pages = new List<BitmapSource>();

                var settings = project.PageSettings;
                var printSettings = project.PrintSettings;

                float backOffsetXPt = backOffsetXMm * MmToPt;
                float backOffsetYPt = backOffsetYMm * MmToPt;

                // Pre-process bleed cache (same logic as PdfGeneratorService)
                int bleedPx = settings.BleedWidthMm > 0
                    ? Math.Max(1, (int)(settings.BleedWidthMm / settings.CardWidthMm * 600))
                    : 0;
                var bleedCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (bleedPx > 0)
                {
                    var uniquePaths = project.Cards
                        .SelectMany(c => new[] { c.ArtworkPath, c.BackArtworkPath })
                        .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var path in uniquePaths)
                    {
                        var result = _bleedProcessor.GetBleedExtendedImage(path!, bleedPx);
                        if (result != null)
                            bleedCache[path!] = result;
                    }
                }

                var expandedFronts = ExpandCards(project.Cards);
                var expandedBacks = ExpandCards(project.Cards.Where(c => c.IncludeBack).ToList());

                if (printSettings.PrintMode == PrintMode.Duplex)
                {
                    int frontPageCount = CalcPageCount(expandedFronts.Count, settings);
                    int backPageCount = CalcPageCount(expandedBacks.Count, settings);
                    int totalPages = Math.Max(frontPageCount, backPageCount);

                    for (int i = 0; i < totalPages; i++)
                    {
                        pages.Add(RenderPage(settings, printSettings, expandedFronts, i, true, bleedCache, 0, 0));
                        pages.Add(RenderPage(settings, printSettings, expandedBacks, i, false, bleedCache, backOffsetXPt, backOffsetYPt));
                    }
                }
                else if (printSettings.PrintMode == PrintMode.FrontsOnly)
                {
                    int pageCount = CalcPageCount(expandedFronts.Count, settings);
                    for (int i = 0; i < pageCount; i++)
                        pages.Add(RenderPage(settings, printSettings, expandedFronts, i, true, bleedCache, 0, 0));
                }
                else // BacksOnly
                {
                    int pageCount = CalcPageCount(expandedBacks.Count, settings);
                    for (int i = 0; i < pageCount; i++)
                        pages.Add(RenderPage(settings, printSettings, expandedBacks, i, false, bleedCache, backOffsetXPt, backOffsetYPt));
                }

                // If no pages were generated, produce a blank page
                if (pages.Count == 0)
                {
                    float pageWPt = settings.PageWidthMm * MmToPt;
                    float pageHPt = settings.PageHeightMm * MmToPt;
                    using var bitmap = new SKBitmap((int)(pageWPt * Scale), (int)(pageHPt * Scale));
                    using var canvas = new SKCanvas(bitmap);
                    canvas.Clear(SKColors.White);
                    pages.Add(ConvertToBitmapSource(bitmap));
                }

                return pages;
            });
        }

        private BitmapSource RenderPage(PageLayout settings, PrintSettings printSettings,
            List<CardModel> cards, int pageIndex, bool front,
            Dictionary<string, string> bleedCache,
            float backOffsetXPt, float backOffsetYPt)
        {
            float pageWPt = settings.PageWidthMm * MmToPt;
            float pageHPt = settings.PageHeightMm * MmToPt;

            int bitmapW = (int)(pageWPt * Scale);
            int bitmapH = (int)(pageHPt * Scale);

            using var bitmap = new SKBitmap(bitmapW, bitmapH);
            using var canvas = new SKCanvas(bitmap);

            canvas.Clear(SKColors.White);
            canvas.Scale(Scale, Scale);

            if (!front && (backOffsetXPt != 0 || backOffsetYPt != 0))
                canvas.Translate(backOffsetXPt, backOffsetYPt);

            int perPage = settings.CardsPerPage;
            if (perPage <= 0)
                return ConvertToBitmapSource(bitmap);

            int startIdx = pageIndex * perPage;
            if (startIdx >= cards.Count)
                return ConvertToBitmapSource(bitmap);

            float startX = settings.MarginLeftMm * MmToPt;
            float startY = settings.MarginTopMm * MmToPt;
            float bleedPt = settings.BleedWidthMm * MmToPt;
            float cardWPt = settings.CardWidthMm * MmToPt;
            float cardHPt = settings.CardHeightMm * MmToPt;
            float cellW = cardWPt + 2 * bleedPt;
            float cellH = cardHPt + 2 * bleedPt;

            int cols = settings.CardsPerRow;

            // When registration marks are active, suppress bleed, cut guides, and outlines
            bool useBleed = bleedCache.Count > 0 && !printSettings.ShowRegistrationMarks;

            // Pass 1a: Draw cut guides BEHIND card art (disabled with registration marks)
            if (printSettings.ShowCutGuides && !printSettings.ShowRegistrationMarks)
            {
                for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
                {
                    int row = i / cols;
                    int col = front ? (i % cols) : (cols - 1 - (i % cols));
                    float cellX = startX + col * cellW;
                    float cellY = startY + row * cellH;

                    DrawCutGuides(canvas, cellX, cellY, cellW, cellH, bleedPt, cardWPt, cardHPt, pageWPt, pageHPt);
                }
            }

            // Pass 1b: Draw crop marks BEHIND card art (disabled with registration marks)
            if (printSettings.ShowCropMarks && !printSettings.ShowRegistrationMarks)
            {
                float cropLen = printSettings.CropMarkLengthMm * MmToPt;
                float cropOffset = printSettings.CropMarkOffsetMm * MmToPt;
                for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
                {
                    int row = i / cols;
                    int col = front ? (i % cols) : (cols - 1 - (i % cols));
                    float cellX = startX + col * cellW;
                    float cellY = startY + row * cellH;

                    DrawCropMarks(canvas, cellX, cellY, bleedPt, cardWPt, cardHPt, cropLen, cropOffset);
                }
            }

            // Pass 2: Draw card images ON TOP of cut guides
            for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
            {
                var card = cards[startIdx + i];

                int row = i / cols;
                int col = front ? (i % cols) : (cols - 1 - (i % cols));

                float cellX = startX + col * cellW;
                float cellY = startY + row * cellH;

                string imagePath = front ? card.ArtworkPath : (card.BackArtworkPath ?? card.ArtworkPath);

                if (useBleed && !string.IsNullOrEmpty(imagePath) && bleedCache.TryGetValue(imagePath, out var bleedImage))
                {
                    DrawCard(canvas, bleedImage, cellX, cellY, cellW, cellH);
                }
                else if (!string.IsNullOrEmpty(imagePath))
                {
                    DrawCard(canvas, imagePath, cellX + bleedPt, cellY + bleedPt, cardWPt, cardHPt);
                }
                else
                {
                    DrawCard(canvas, null, cellX + bleedPt, cellY + bleedPt, cardWPt, cardHPt);
                }

                // Overlay text (e.g. "TOKEN") rendered on front face only
                if (front && !string.IsNullOrEmpty(card.OverlayText))
                {
                    DrawOverlayText(canvas, card.OverlayText,
                        cellX + bleedPt, cellY + bleedPt, cardWPt, cardHPt);
                }
            }

            // Pass 3: Draw card outlines ON TOP of card art (disabled with registration marks)
            if (printSettings.ShowCardOutline && !printSettings.ShowRegistrationMarks)
            {
                for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
                {
                    int row = i / cols;
                    int col = front ? (i % cols) : (cols - 1 - (i % cols));
                    float cellX = startX + col * cellW;
                    float cellY = startY + row * cellH;

                    DrawCardOutline(canvas, cellX, cellY, cellW, cellH, bleedPt, cardWPt, cardHPt, printSettings);
                }
            }

            // Pass 4: Draw registration marks ON TOP of everything (front pages only)
            if (printSettings.ShowRegistrationMarks && front)
            {
                DrawRegistrationMarks(canvas, pageWPt, pageHPt, printSettings);
            }

            // Pass 5: Draw CMYK color bars in the margin
            if (printSettings.ShowColorBars)
            {
                int rows = cols > 0 ? perPage / cols : 0;
                DrawColorBars(canvas, startX, startY, cols, rows, cellW, cellH, pageWPt, pageHPt);
            }

            return ConvertToBitmapSource(bitmap);
        }

        private void DrawCutGuides(SKCanvas canvas, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageW, float pageH)
        {
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float cardRight = cellX + bleed + cardW;
            float cardBottom = cellY + bleed + cardH;

            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                StrokeWidth = 0.25f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            // Vertical lines extend from card edge to top/bottom page edges
            canvas.DrawLine(cardLeft, 0, cardLeft, cardTop, paint);
            canvas.DrawLine(cardRight, 0, cardRight, cardTop, paint);
            canvas.DrawLine(cardLeft, cardBottom, cardLeft, pageH, paint);
            canvas.DrawLine(cardRight, cardBottom, cardRight, pageH, paint);

            // Horizontal lines extend from card edge to left/right page edges
            canvas.DrawLine(0, cardTop, cardLeft, cardTop, paint);
            canvas.DrawLine(cardRight, cardTop, pageW, cardTop, paint);
            canvas.DrawLine(0, cardBottom, cardLeft, cardBottom, paint);
            canvas.DrawLine(cardRight, cardBottom, pageW, cardBottom, paint);
        }

        private void DrawCropMarks(SKCanvas canvas, float cellX, float cellY,
            float bleed, float cardW, float cardH, float markLen, float offset)
        {
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float cardRight = cellX + bleed + cardW;
            float cardBottom = cellY + bleed + cardH;

            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                StrokeWidth = 0.25f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            // Top-left corner
            canvas.DrawLine(cardLeft, cardTop - offset, cardLeft, cardTop - offset - markLen, paint);
            canvas.DrawLine(cardLeft - offset, cardTop, cardLeft - offset - markLen, cardTop, paint);

            // Top-right corner
            canvas.DrawLine(cardRight, cardTop - offset, cardRight, cardTop - offset - markLen, paint);
            canvas.DrawLine(cardRight + offset, cardTop, cardRight + offset + markLen, cardTop, paint);

            // Bottom-left corner
            canvas.DrawLine(cardLeft, cardBottom + offset, cardLeft, cardBottom + offset + markLen, paint);
            canvas.DrawLine(cardLeft - offset, cardBottom, cardLeft - offset - markLen, cardBottom, paint);

            // Bottom-right corner
            canvas.DrawLine(cardRight, cardBottom + offset, cardRight, cardBottom + offset + markLen, paint);
            canvas.DrawLine(cardRight + offset, cardBottom, cardRight + offset + markLen, cardBottom, paint);
        }

        private void DrawCard(SKCanvas canvas, string? imagePath, float x, float y, float w, float h)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.LightGray,
                    StrokeWidth = 1f,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawRect(x, y, w, h, paint);
                return;
            }

            try
            {
                using var bmp = SKBitmap.Decode(imagePath);
                if (bmp != null)
                {
                    canvas.DrawBitmap(bmp, SKRect.Create(x, y, w, h));
                }
                else
                {
                    using var paint = new SKPaint
                    {
                        Color = SKColors.Red,
                        StrokeWidth = 1f,
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true
                    };
                    canvas.DrawRect(x, y, w, h, paint);
                }
            }
            catch
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.Red,
                    StrokeWidth = 1f,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawRect(x, y, w, h, paint);
            }
        }

        private void DrawCardOutline(SKCanvas canvas, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            PrintSettings ps)
        {
            // Parse outline color
            SKColor color;
            if (!SKColor.TryParse(ps.OutlineColor, out color))
                color = new SKColor(0x66, 0xFF, 0x00);

            using var paint = new SKPaint
            {
                Color = color,
                StrokeWidth = ps.LineWeight,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            if (ps.OutlineLineType == LineType.Dashed)
                paint.PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0);

            float radiusPt = ps.CornerRadiusMm * MmToPt;
            float cornerLenPt = ps.CornerLengthMm * MmToPt;

            // Calculate card rect position based on alignment
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float offset = ps.LineWeight / 2; // half the line weight for alignment

            float x, y, w, h;
            switch (ps.OutlineAlignment)
            {
                case OutlineAlignment.Inside:
                    x = cardLeft + offset;
                    y = cardTop + offset;
                    w = cardW - 2 * offset;
                    h = cardH - 2 * offset;
                    break;
                case OutlineAlignment.Outside:
                    x = cardLeft - offset;
                    y = cardTop - offset;
                    w = cardW + 2 * offset;
                    h = cardH + 2 * offset;
                    break;
                default: // Center
                    x = cardLeft;
                    y = cardTop;
                    w = cardW;
                    h = cardH;
                    break;
            }

            if (ps.OutlineType == OutlineType.Full)
            {
                if (radiusPt > 0)
                    DrawRoundedRect(canvas, paint, x, y, w, h, radiusPt);
                else
                    canvas.DrawRect(x, y, w, h, paint);
            }
            else // Corners only
            {
                DrawCornerMarks(canvas, paint, x, y, w, h, radiusPt, cornerLenPt);
            }
        }

        private void DrawRoundedRect(SKCanvas canvas, SKPaint paint, float x, float y, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w / 2, h / 2));

            using var path = new SKPath();
            // Top-left arc
            path.ArcTo(new SKRect(x, y, x + 2 * r, y + 2 * r), 180, 90, false);
            // Top edge
            path.LineTo(x + w - r, y);
            // Top-right arc
            path.ArcTo(new SKRect(x + w - 2 * r, y, x + w, y + 2 * r), 270, 90, false);
            // Right edge
            path.LineTo(x + w, y + h - r);
            // Bottom-right arc
            path.ArcTo(new SKRect(x + w - 2 * r, y + h - 2 * r, x + w, y + h), 0, 90, false);
            // Bottom edge
            path.LineTo(x + r, y + h);
            // Bottom-left arc
            path.ArcTo(new SKRect(x, y + h - 2 * r, x + 2 * r, y + h), 90, 90, false);
            // Left edge
            path.LineTo(x, y + r);
            path.Close();

            canvas.DrawPath(path, paint);
        }

        private void DrawCornerMarks(SKCanvas canvas, SKPaint paint, float x, float y, float w, float h, float r, float len)
        {
            r = Math.Min(r, Math.Min(w / 2, h / 2));
            len = Math.Min(len, Math.Min(w / 2 - r, h / 2 - r));
            if (len <= 0) len = 5;

            if (r > 0)
            {
                // Top-left corner: arc + straight stubs
                using (var path = new SKPath())
                {
                    path.MoveTo(x, y + r + len);
                    path.LineTo(x, y + r);
                    path.ArcTo(new SKRect(x, y, x + 2 * r, y + 2 * r), 180, 90, false);
                    path.LineTo(x + r + len, y);
                    canvas.DrawPath(path, paint);
                }

                // Top-right corner
                using (var path = new SKPath())
                {
                    path.MoveTo(x + w - r - len, y);
                    path.LineTo(x + w - r, y);
                    path.ArcTo(new SKRect(x + w - 2 * r, y, x + w, y + 2 * r), 270, 90, false);
                    path.LineTo(x + w, y + r + len);
                    canvas.DrawPath(path, paint);
                }

                // Bottom-right corner
                using (var path = new SKPath())
                {
                    path.MoveTo(x + w, y + h - r - len);
                    path.LineTo(x + w, y + h - r);
                    path.ArcTo(new SKRect(x + w - 2 * r, y + h - 2 * r, x + w, y + h), 0, 90, false);
                    path.LineTo(x + w - r - len, y + h);
                    canvas.DrawPath(path, paint);
                }

                // Bottom-left corner
                using (var path = new SKPath())
                {
                    path.MoveTo(x + r + len, y + h);
                    path.LineTo(x + r, y + h);
                    path.ArcTo(new SKRect(x, y + h - 2 * r, x + 2 * r, y + h), 90, 90, false);
                    path.LineTo(x, y + h - r - len);
                    canvas.DrawPath(path, paint);
                }
            }
            else
            {
                // Sharp corners - just L-shaped marks
                // Top-left
                canvas.DrawLine(x, y + len, x, y, paint);
                canvas.DrawLine(x, y, x + len, y, paint);
                // Top-right
                canvas.DrawLine(x + w - len, y, x + w, y, paint);
                canvas.DrawLine(x + w, y, x + w, y + len, paint);
                // Bottom-right
                canvas.DrawLine(x + w, y + h - len, x + w, y + h, paint);
                canvas.DrawLine(x + w, y + h, x + w - len, y + h, paint);
                // Bottom-left
                canvas.DrawLine(x + len, y + h, x, y + h, paint);
                canvas.DrawLine(x, y + h, x, y + h - len, paint);
            }
        }

        private void DrawRegistrationMarks(SKCanvas canvas, float pageW, float pageH, PrintSettings ps)
        {
            float inset = ps.RegMarkInsetIn * InToPt;
            float squareSize = ps.RegMarkSquareSizeIn * InToPt;
            float armLength = ps.RegMarkLengthIn * InToPt;
            float thickness = ps.RegMarkThicknessIn * InToPt;

            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            // Top-left mark: filled square
            canvas.DrawRect(inset, inset, squareSize, squareSize, paint);

            // Top-right mark: L-shape with corner at (pageW - inset, inset)
            // Horizontal bar going left
            canvas.DrawRect(pageW - inset - armLength, inset, armLength, thickness, paint);
            // Vertical bar going down
            canvas.DrawRect(pageW - inset - thickness, inset + thickness, thickness, armLength - thickness, paint);

            // Bottom-left mark: L-shape with corner at (inset, pageH - inset)
            // Vertical bar going up
            canvas.DrawRect(inset, pageH - inset - armLength, thickness, armLength - thickness, paint);
            // Horizontal bar going right
            canvas.DrawRect(inset, pageH - inset - thickness, armLength, thickness, paint);
        }

        private void DrawOverlayText(SKCanvas canvas, string text, float x, float y, float w, float h)
        {
            // Semi-transparent dark banner across the lower portion of the card
            float bannerH = h * 0.15f;
            float bannerY = y + h - bannerH - h * 0.08f;

            using var bannerPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 160),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(x, bannerY, w, bannerH, bannerPaint);

            // White text centered in the banner
            float textSize = Math.Max(8, bannerH * 0.6f);
            using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
            using var font = new SKFont(typeface, textSize);
            using var textPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            // Vertically center the text in the banner
            float textX = x + w / 2;
            float textY = bannerY + bannerH / 2 + textSize / 3; // approximate vertical centering
            canvas.DrawText(text, textX, textY, SKTextAlign.Center, font, textPaint);
        }

        private static BitmapSource ConvertToBitmapSource(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            var stream = new MemoryStream(data.ToArray());
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        /// <summary>Each card in the list is one slot on the page — no quantity expansion.</summary>
        private void DrawColorBars(SKCanvas canvas, float startX, float startY,
            int cols, int rows, float cellW, float cellH, float pageW, float pageH)
        {
            float gridRight = startX + cols * cellW;
            float gridBottom = startY + rows * cellH;
            float gridWidth = cols * cellW;
            float gridHeight = rows * cellH;
            float barThickness = 4 * MmToPt;
            float gap = 2 * MmToPt;
            float minClearance = 3 * MmToPt;

            bool fitsBottom = gridBottom + gap + barThickness <= pageH - minClearance;
            bool fitsRight = gridRight + gap + barThickness <= pageW - minClearance;

            if (!fitsBottom && !fitsRight) return;

            if (fitsBottom)
                DrawColorBarStrip(canvas, startX, gridBottom + gap, gridWidth, barThickness, false);
            else
                DrawColorBarStrip(canvas, gridRight + gap, startY, gridHeight, barThickness, true);
        }

        private void DrawColorBarStrip(SKCanvas canvas, float originX, float originY,
            float stripLength, float stripThickness, bool vertical)
        {
            var colorDefs = new (string Label, byte R, byte G, byte B)[]
            {
                ("C", 0, 174, 239), ("M", 236, 0, 140), ("Y", 255, 242, 0),
                ("K", 0, 0, 0), ("R", 237, 28, 36), ("G", 0, 166, 81), ("B", 46, 49, 146),
            };

            int totalPatches = colorDefs.Length * 4 + 8;
            float patchSize = stripLength / totalPatches;
            float pos = 0;

            using var labelTypeface = SKTypeface.FromFamilyName("Arial");
            using var labelFont = new SKFont(labelTypeface, 5);
            using var labelPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

            foreach (var (label, cr, cg, cb) in colorDefs)
            {
                float[] densities = { 0.25f, 0.50f, 0.75f, 1.0f };
                foreach (float d in densities)
                {
                    byte r = (byte)(255 + (cr - 255) * d);
                    byte g = (byte)(255 + (cg - 255) * d);
                    byte b = (byte)(255 + (cb - 255) * d);
                    using var paint = new SKPaint { Color = new SKColor(r, g, b) };
                    if (vertical)
                        canvas.DrawRect(originX, originY + pos, stripThickness, patchSize, paint);
                    else
                        canvas.DrawRect(originX + pos, originY, patchSize, stripThickness, paint);
                    pos += patchSize;
                }
                float labelPos = pos - patchSize * 2;
                if (vertical)
                    canvas.DrawText(label, originX + stripThickness + 2,
                        originY + labelPos + patchSize / 2 + 2, labelFont, labelPaint);
                else
                    canvas.DrawText(label, originX + labelPos,
                        originY - 1, labelFont, labelPaint);
            }

            for (int i = 0; i < 8; i++)
            {
                byte v = (byte)(255 - (int)(255 * i / 7.0));
                using var paint = new SKPaint { Color = new SKColor(v, v, v) };
                if (vertical)
                    canvas.DrawRect(originX, originY + pos, stripThickness, patchSize, paint);
                else
                    canvas.DrawRect(originX + pos, originY, patchSize, stripThickness, paint);
                pos += patchSize;
            }

            using var borderPaint = new SKPaint
            {
                Color = SKColors.Black, StrokeWidth = 0.25f,
                Style = SKPaintStyle.Stroke, IsAntialias = true
            };
            if (vertical)
                canvas.DrawRect(originX, originY, stripThickness, stripLength, borderPaint);
            else
                canvas.DrawRect(originX, originY, stripLength, stripThickness, borderPaint);
        }

        private static List<CardModel> ExpandCards(List<CardModel> cards) => cards;

        private static int CalcPageCount(int cardCount, PageLayout settings)
        {
            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return 0;
            return (int)Math.Ceiling((double)cardCount / perPage);
        }
    }
}
