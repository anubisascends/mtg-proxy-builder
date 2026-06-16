using MTGProxyBuilder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MTGProxyBuilder.Core.Services
{
    public class PdfGeneratorService
    {
        private const float MmToPt = 72f / 25.4f;
        private readonly BleedProcessor _bleedProcessor = new();

        public Task<bool> GeneratePdfAsync(ProjectModel project, string outputPath,
            float backOffsetXMm = 0, float backOffsetYMm = 0)
        {
            return Task.Run(() =>
            {
                try
                {
                    var document = new PdfDocument();
                    document.Info.Title = project.ProjectName;

                    var settings = project.PageSettings;
                    var printSettings = project.PrintSettings;

                    float backOffsetXPt = backOffsetXMm * MmToPt;
                    float backOffsetYPt = backOffsetYMm * MmToPt;

                    // Pre-process all unique images for bleed (avoids re-processing duplicates)
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
                            AddPage(document, settings, printSettings, expandedFronts, i, true, bleedCache, 0, 0);
                            AddPage(document, settings, printSettings, expandedBacks, i, false, bleedCache, backOffsetXPt, backOffsetYPt);
                        }
                    }
                    else if (printSettings.PrintMode == PrintMode.FrontsOnly)
                    {
                        int pageCount = CalcPageCount(expandedFronts.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedFronts, i, true, bleedCache, 0, 0);
                    }
                    else
                    {
                        int pageCount = CalcPageCount(expandedBacks.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedBacks, i, false, bleedCache, backOffsetXPt, backOffsetYPt);
                    }

                    if (document.PageCount == 0)
                    {
                        var page = document.AddPage();
                        SetPageSize(page, settings);
                    }

                    document.Save(outputPath);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PDF generation error: {ex.Message}");
                    return false;
                }
            });
        }

        public Task<bool> GenerateAlignmentPdfAsync(ProjectModel project, string outputPath,
            float offsetXMm, float offsetYMm)
        {
            return Task.Run(() =>
            {
                try
                {
                    var document = new PdfDocument();
                    document.Info.Title = "Printer Alignment Test";

                    var settings = project.PageSettings;
                    float bleedPt = settings.BleedWidthMm * MmToPt;
                    float cardWPt = settings.CardWidthMm * MmToPt;
                    float cardHPt = settings.CardHeightMm * MmToPt;
                    float cellW = cardWPt + 2 * bleedPt;
                    float cellH = cardHPt + 2 * bleedPt;

                    float startX = settings.MarginLeftMm * MmToPt;
                    float startY = settings.MarginTopMm * MmToPt;

                    int cols = settings.CardsPerRow;
                    int rows = settings.CardsPerPage > 0 && cols > 0
                        ? settings.CardsPerPage / cols
                        : 0;

                    float gridRight = startX + cols * cellW;
                    float gridBottom = startY + rows * cellH;

                    float centerX = (startX + gridRight) / 2;
                    float centerY = (startY + gridBottom) / 2;

                    float armLen = 8 * MmToPt;
                    var solidPen = new XPen(XColors.Black, 0.5);
                    var dashedPen = new XPen(XColors.Black, 0.5) { DashStyle = XDashStyle.Dash };
                    var font = new XFont("Arial", 8);
                    var labelFormat = new XStringFormat
                    {
                        Alignment = XStringAlignment.Near,
                        LineAlignment = XLineAlignment.Near
                    };

                    // Helper: crosshair positions and labels
                    var crosshairs = new (float X, float Y, string Label)[]
                    {
                        (startX, startY, "TL"),
                        (gridRight, startY, "TR"),
                        (startX, gridBottom, "BL"),
                        (gridRight, gridBottom, "BR"),
                        (centerX, centerY, "CENTER")
                    };

                    // --- Page 1: Front ---
                    var frontPage = document.AddPage();
                    SetPageSize(frontPage, settings);
                    using (var gfx = XGraphics.FromPdfPage(frontPage))
                    {
                        // Grid boundary rectangle
                        gfx.DrawRectangle(solidPen, startX, startY,
                            cols * cellW, rows * cellH);

                        // Crosshairs
                        foreach (var (cx, cy, label) in crosshairs)
                        {
                            gfx.DrawLine(solidPen, cx - armLen, cy, cx + armLen, cy);
                            gfx.DrawLine(solidPen, cx, cy - armLen, cx, cy + armLen);
                            gfx.DrawString(label, font, XBrushes.Black,
                                cx + 3, cy + 3, labelFormat);
                        }

                        // Info text at bottom
                        float pageHPt = settings.PageHeightMm * MmToPt;
                        gfx.DrawString("Printer Alignment Test \u2014 Front", font, XBrushes.Black,
                            startX, pageHPt - 20, labelFormat);
                        gfx.DrawString($"Offset X: {offsetXMm:F2}mm, Y: {offsetYMm:F2}mm",
                            font, XBrushes.Black, startX, pageHPt - 10, labelFormat);
                    }

                    // --- Page 2: Back ---
                    var backPage = document.AddPage();
                    SetPageSize(backPage, settings);
                    using (var gfx = XGraphics.FromPdfPage(backPage))
                    {
                        // Apply offset transform
                        gfx.TranslateTransform(offsetXMm * MmToPt, offsetYMm * MmToPt);

                        // Grid boundary rectangle (dashed)
                        gfx.DrawRectangle(dashedPen, startX, startY,
                            cols * cellW, rows * cellH);

                        // Crosshairs (dashed, same positions as front)
                        foreach (var (cx, cy, label) in crosshairs)
                        {
                            gfx.DrawLine(dashedPen, cx - armLen, cy, cx + armLen, cy);
                            gfx.DrawLine(dashedPen, cx, cy - armLen, cx, cy + armLen);
                            gfx.DrawString(label, font, XBrushes.Black,
                                cx + 3, cy + 3, labelFormat);
                        }

                        // Info text at bottom
                        float pageHPt = settings.PageHeightMm * MmToPt;
                        gfx.DrawString(
                            $"Printer Alignment Test \u2014 Back (offset X: {offsetXMm:F2}mm, Y: {offsetYMm:F2}mm)",
                            font, XBrushes.Black, startX, pageHPt - 20, labelFormat);
                    }

                    document.Save(outputPath);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Alignment PDF error: {ex.Message}");
                    return false;
                }
            });
        }

        private void AddPage(PdfDocument doc, PageLayout settings, PrintSettings printSettings,
            List<CardModel> cards, int pageIndex, bool front,
            Dictionary<string, string> bleedCache,
            float backOffsetXPt = 0, float backOffsetYPt = 0)
        {
            var page = doc.AddPage();
            SetPageSize(page, settings);

            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return;

            int startIdx = pageIndex * perPage;
            if (startIdx >= cards.Count) return;

            using var gfx = XGraphics.FromPdfPage(page);

            if (!front && (backOffsetXPt != 0 || backOffsetYPt != 0))
                gfx.TranslateTransform(backOffsetXPt, backOffsetYPt);

            float startX = settings.MarginLeftMm * MmToPt;
            float startY = settings.MarginTopMm * MmToPt;
            float bleedPt = settings.BleedWidthMm * MmToPt;
            float cardWPt = settings.CardWidthMm * MmToPt;
            float cardHPt = settings.CardHeightMm * MmToPt;
            float cellW = cardWPt + 2 * bleedPt;
            float cellH = cardHPt + 2 * bleedPt;

            int cols = settings.CardsPerRow;
            float pageWPt = settings.PageWidthMm * MmToPt;
            float pageHPt = settings.PageHeightMm * MmToPt;

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

                    DrawCutGuides(gfx, cellX, cellY, cellW, cellH, bleedPt, cardWPt, cardHPt, pageWPt, pageHPt);
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

                    DrawCropMarks(gfx, cellX, cellY, bleedPt, cardWPt, cardHPt, cropLen, cropOffset);
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
                    DrawCard(gfx, bleedImage, cellX, cellY, cellW, cellH);
                }
                else if (!string.IsNullOrEmpty(imagePath))
                {
                    DrawCard(gfx, imagePath, cellX + bleedPt, cellY + bleedPt, cardWPt, cardHPt);
                }
                else
                {
                    DrawCard(gfx, null, cellX + bleedPt, cellY + bleedPt, cardWPt, cardHPt);
                }

                // Overlay text (e.g. "TOKEN") rendered on front face only
                if (front && !string.IsNullOrEmpty(card.OverlayText))
                {
                    DrawOverlayText(gfx, card.OverlayText,
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

                    DrawCardOutline(gfx, cellX, cellY, cellW, cellH, bleedPt, cardWPt, cardHPt, printSettings);
                }
            }

            // Pass 4: Draw registration marks ON TOP of everything (front pages only)
            if (printSettings.ShowRegistrationMarks && front)
            {
                DrawRegistrationMarks(gfx, pageWPt, pageHPt, printSettings);
            }
        }

        private void DrawCardOutline(XGraphics gfx, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            PrintSettings ps)
        {
            // Parse outline color
            XColor color;
            try
            {
                string hex = ps.OutlineColor.TrimStart('#');
                int r = Convert.ToInt32(hex[..2], 16);
                int g = Convert.ToInt32(hex[2..4], 16);
                int b = Convert.ToInt32(hex[4..6], 16);
                color = XColor.FromArgb(r, g, b);
            }
            catch { color = XColor.FromArgb(0x66, 0xFF, 0x00); }

            var pen = new XPen(color, ps.LineWeight);
            if (ps.OutlineLineType == LineType.Dashed)
                pen.DashStyle = XDashStyle.Dash;

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
                // Full rounded rectangle
                if (radiusPt > 0)
                    DrawRoundedRect(gfx, pen, x, y, w, h, radiusPt);
                else
                    gfx.DrawRectangle(pen, x, y, w, h);
            }
            else // Corners only
            {
                DrawCornerMarks(gfx, pen, x, y, w, h, radiusPt, cornerLenPt);
            }
        }

        private void DrawRoundedRect(XGraphics gfx, XPen pen, float x, float y, float w, float h, float r)
        {
            r = Math.Min(r, Math.Min(w / 2, h / 2));

            var path = new XGraphicsPath();
            // Top-left arc
            path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
            // Top edge
            path.AddLine(x + r, y, x + w - r, y);
            // Top-right arc
            path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
            // Right edge
            path.AddLine(x + w, y + r, x + w, y + h - r);
            // Bottom-right arc
            path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
            // Bottom edge
            path.AddLine(x + w - r, y + h, x + r, y + h);
            // Bottom-left arc
            path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
            // Left edge
            path.AddLine(x, y + h - r, x, y + r);
            path.CloseFigure();

            gfx.DrawPath(pen, path);
        }

        private void DrawCornerMarks(XGraphics gfx, XPen pen, float x, float y, float w, float h, float r, float len)
        {
            r = Math.Min(r, Math.Min(w / 2, h / 2));
            len = Math.Min(len, Math.Min(w / 2 - r, h / 2 - r));
            if (len <= 0) len = 5;

            if (r > 0)
            {
                // Top-left corner: arc + straight stubs
                var path = new XGraphicsPath();
                path.AddLine(x, y + r + len, x, y + r);
                path.AddArc(x, y, 2 * r, 2 * r, 180, 90);
                path.AddLine(x + r, y, x + r + len, y);
                gfx.DrawPath(pen, path);

                // Top-right corner
                path = new XGraphicsPath();
                path.AddLine(x + w - r - len, y, x + w - r, y);
                path.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
                path.AddLine(x + w, y + r, x + w, y + r + len);
                gfx.DrawPath(pen, path);

                // Bottom-right corner
                path = new XGraphicsPath();
                path.AddLine(x + w, y + h - r - len, x + w, y + h - r);
                path.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
                path.AddLine(x + w - r, y + h, x + w - r - len, y + h);
                gfx.DrawPath(pen, path);

                // Bottom-left corner
                path = new XGraphicsPath();
                path.AddLine(x + r + len, y + h, x + r, y + h);
                path.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
                path.AddLine(x, y + h - r, x, y + h - r - len);
                gfx.DrawPath(pen, path);
            }
            else
            {
                // Sharp corners — just L-shaped marks
                // Top-left
                gfx.DrawLine(pen, x, y + len, x, y);
                gfx.DrawLine(pen, x, y, x + len, y);
                // Top-right
                gfx.DrawLine(pen, x + w - len, y, x + w, y);
                gfx.DrawLine(pen, x + w, y, x + w, y + len);
                // Bottom-right
                gfx.DrawLine(pen, x + w, y + h - len, x + w, y + h);
                gfx.DrawLine(pen, x + w, y + h, x + w - len, y + h);
                // Bottom-left
                gfx.DrawLine(pen, x + len, y + h, x, y + h);
                gfx.DrawLine(pen, x, y + h, x, y + h - len);
            }
        }

        private void DrawCutGuides(XGraphics gfx, float cellX, float cellY,
            float cellW, float cellH, float bleed, float cardW, float cardH,
            float pageW, float pageH)
        {
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float cardRight = cellX + bleed + cardW;
            float cardBottom = cellY + bleed + cardH;

            var pen = new XPen(XColors.Black, 0.25);

            // Vertical lines extend from card edge to top/bottom page edges
            gfx.DrawLine(pen, cardLeft, 0, cardLeft, cardTop);           // top-left vertical up
            gfx.DrawLine(pen, cardRight, 0, cardRight, cardTop);         // top-right vertical up
            gfx.DrawLine(pen, cardLeft, cardBottom, cardLeft, pageH);    // bottom-left vertical down
            gfx.DrawLine(pen, cardRight, cardBottom, cardRight, pageH);  // bottom-right vertical down

            // Horizontal lines extend from card edge to left/right page edges
            gfx.DrawLine(pen, 0, cardTop, cardLeft, cardTop);           // top-left horizontal left
            gfx.DrawLine(pen, cardRight, cardTop, pageW, cardTop);      // top-right horizontal right
            gfx.DrawLine(pen, 0, cardBottom, cardLeft, cardBottom);     // bottom-left horizontal left
            gfx.DrawLine(pen, cardRight, cardBottom, pageW, cardBottom); // bottom-right horizontal right
        }

        /// <summary>
        /// Draw professional crop marks at each corner of a card.
        /// Marks are short lines at the trim boundary (card edge), extending outward
        /// into the bleed area with a small gap from the card edge.
        /// </summary>
        private void DrawCropMarks(XGraphics gfx, float cellX, float cellY,
            float bleed, float cardW, float cardH, float markLen, float offset)
        {
            float cardLeft = cellX + bleed;
            float cardTop = cellY + bleed;
            float cardRight = cellX + bleed + cardW;
            float cardBottom = cellY + bleed + cardH;

            var pen = new XPen(XColors.Black, 0.25);

            // Top-left corner
            gfx.DrawLine(pen, cardLeft, cardTop - offset, cardLeft, cardTop - offset - markLen);         // vertical up
            gfx.DrawLine(pen, cardLeft - offset, cardTop, cardLeft - offset - markLen, cardTop);         // horizontal left

            // Top-right corner
            gfx.DrawLine(pen, cardRight, cardTop - offset, cardRight, cardTop - offset - markLen);       // vertical up
            gfx.DrawLine(pen, cardRight + offset, cardTop, cardRight + offset + markLen, cardTop);       // horizontal right

            // Bottom-left corner
            gfx.DrawLine(pen, cardLeft, cardBottom + offset, cardLeft, cardBottom + offset + markLen);   // vertical down
            gfx.DrawLine(pen, cardLeft - offset, cardBottom, cardLeft - offset - markLen, cardBottom);   // horizontal left

            // Bottom-right corner
            gfx.DrawLine(pen, cardRight, cardBottom + offset, cardRight, cardBottom + offset + markLen); // vertical down
            gfx.DrawLine(pen, cardRight + offset, cardBottom, cardRight + offset + markLen, cardBottom); // horizontal right
        }

        private const float InToPt = 72f;

        private void DrawRegistrationMarks(XGraphics gfx, float pageW, float pageH, PrintSettings ps)
        {
            float inset = ps.RegMarkInsetIn * InToPt;
            float squareSize = ps.RegMarkSquareSizeIn * InToPt;  // 5mm filled square
            float armLength = ps.RegMarkLengthIn * InToPt;       // 20mm L-shape arms
            float thickness = ps.RegMarkThicknessIn * InToPt;    // 0.3mm arm thickness

            var brush = XBrushes.Black;

            // Top-left mark: filled square (5mm x 5mm)
            gfx.DrawRectangle(brush, inset, inset, squareSize, squareSize);

            // Top-right mark: L-shape with corner at (pageW - inset, inset)
            // Horizontal bar going left
            gfx.DrawRectangle(brush, pageW - inset - armLength, inset, armLength, thickness);
            // Vertical bar going down
            gfx.DrawRectangle(brush, pageW - inset - thickness, inset + thickness, thickness, armLength - thickness);

            // Bottom-left mark: L-shape with corner at (inset, pageH - inset)
            // Vertical bar going up
            gfx.DrawRectangle(brush, inset, pageH - inset - armLength, thickness, armLength - thickness);
            // Horizontal bar going right
            gfx.DrawRectangle(brush, inset, pageH - inset - thickness, armLength, thickness);
        }

        private void DrawOverlayText(XGraphics gfx, string text, float x, float y, float w, float h)
        {
            // Semi-transparent dark banner across the bottom third of the card
            float bannerH = h * 0.15f;
            float bannerY = y + h - bannerH - h * 0.08f;

            var bannerBrush = new XSolidBrush(XColor.FromArgb(160, 0, 0, 0));
            gfx.DrawRectangle(bannerBrush, x, bannerY, w, bannerH);

            // White text centered in the banner
            var font = new XFont("Arial", Math.Max(8, bannerH * 0.6), XFontStyleEx.Bold);
            var textBrush = XBrushes.White;
            var format = new XStringFormat
            {
                Alignment = XStringAlignment.Center,
                LineAlignment = XLineAlignment.Center
            };
            gfx.DrawString(text, font, textBrush,
                new XRect(x, bannerY, w, bannerH), format);
        }

        private void DrawCard(XGraphics gfx, string? imagePath, float x, float y, float w, float h)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                gfx.DrawRectangle(XPens.LightGray, x, y, w, h);
                return;
            }

            try
            {
                using var image = XImage.FromFile(imagePath);
                gfx.DrawImage(image, x, y, w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error drawing card image: {ex.Message}");
                gfx.DrawRectangle(XPens.Red, x, y, w, h);
            }
        }

        private void SetPageSize(PdfPage page, PageLayout settings)
        {
            page.Width = new XUnit(settings.PageWidthMm * MmToPt, XGraphicsUnit.Point);
            page.Height = new XUnit(settings.PageHeightMm * MmToPt, XGraphicsUnit.Point);
        }

        private List<CardModel> ExpandCards(List<CardModel> cards)
        {
            var result = new List<CardModel>();
            foreach (var card in cards)
                for (int i = 0; i < card.Quantity; i++)
                    result.Add(card);
            return result;
        }

        private int CalcPageCount(int cardCount, PageLayout settings)
        {
            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return 0;
            return (int)Math.Ceiling((double)cardCount / perPage);
        }
    }
}
