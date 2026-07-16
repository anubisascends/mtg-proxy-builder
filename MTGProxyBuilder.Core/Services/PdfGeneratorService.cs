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
            CalibrationTransform? backCalibration = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    var document = new PdfDocument();
                    document.Info.Title = project.ProjectName;

                    var settings = project.PageSettings;
                    var printSettings = project.PrintSettings;

                    // Pre-process all unique images for bleed using the configured DPI
                    int bleedPx = settings.BleedWidthMm > 0
                        ? Math.Max(1, (int)(settings.BleedWidthMm / settings.CardWidthMm * printSettings.DPI))
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
                            AddPage(document, settings, printSettings, expandedFronts, i, true, bleedCache);
                            AddPage(document, settings, printSettings, expandedBacks, i, false, bleedCache, backCalibration);
                        }
                    }
                    else if (printSettings.PrintMode == PrintMode.FrontsOnly)
                    {
                        int pageCount = CalcPageCount(expandedFronts.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedFronts, i, true, bleedCache);
                    }
                    else
                    {
                        int pageCount = CalcPageCount(expandedBacks.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedBacks, i, false, bleedCache, backCalibration);
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
            PrinterProfile profile)
        {
            return Task.Run(() =>
            {
                try
                {
                    var document = new PdfDocument();
                    document.Info.Title = "Printer Calibration Test";

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

                    float strideX = settings.CellStrideXMm * MmToPt;
                    float strideY = settings.CellStrideYMm * MmToPt;
                    float gridW = cols > 0 ? (cols - 1) * strideX + cellW : 0;
                    float gridH = rows > 0 ? (rows - 1) * strideY + cellH : 0;
                    float gridRight = startX + gridW;
                    float gridBottom = startY + gridH;

                    float centerX = (startX + gridRight) / 2;
                    float centerY = (startY + gridBottom) / 2;

                    float pageWPt = settings.PageWidthMm * MmToPt;
                    float pageHPt = settings.PageHeightMm * MmToPt;

                    // Compute calibration transform from profile + grid dimensions
                    float gridWidthMm = cols > 0
                        ? cols * (settings.CardWidthMm + 2 * settings.BleedWidthMm) + (cols - 1) * settings.HorizontalSpacingMm
                        : 0;
                    float gridHeightMm = rows > 0
                        ? rows * (settings.CardHeightMm + 2 * settings.BleedWidthMm) + (rows - 1) * settings.VerticalSpacingMm
                        : 0;
                    var calibration = CalibrationTransform.Compute(profile, gridWidthMm, gridHeightMm);

                    // Fonts
                    var titleFont = new XFont("Arial", 10, XFontStyleEx.Bold);
                    var infoFont = new XFont("Arial", 8);
                    var instructionFont = new XFont("Arial", 6);

                    var leftFormat = new XStringFormat
                    {
                        Alignment = XStringAlignment.Near,
                        LineAlignment = XLineAlignment.Near
                    };
                    var centerFormat = new XStringFormat
                    {
                        Alignment = XStringAlignment.Center,
                        LineAlignment = XLineAlignment.Center
                    };

                    // Target positions and labels
                    var targets = new (float X, float Y, string Label)[]
                    {
                        (startX, startY, "TL"),
                        (gridRight, startY, "TR"),
                        (startX, gridBottom, "BL"),
                        (gridRight, gridBottom, "BR"),
                        (centerX, centerY, "C")
                    };

                    // ===== Page 1: Front (Reference) =====
                    var frontPage = document.AddPage();
                    SetPageSize(frontPage, settings);
                    using (var gfx = XGraphics.FromPdfPage(frontPage))
                    {
                        // Title bar
                        float titleY = Math.Min(startY - 24, 10);
                        gfx.DrawString("PRINTER CALIBRATION TEST \u2014 FRONT", titleFont, XBrushes.Black,
                            startX, titleY, leftFormat);
                        string settingsInfo = $"{settings.PageWidthMm}x{settings.PageHeightMm}mm page | " +
                            $"{settings.CardWidthMm}x{settings.CardHeightMm}mm card | " +
                            $"{cols}x{rows} grid | {DateTime.Now:yyyy-MM-dd}";
                        gfx.DrawString(settingsInfo, infoFont, XBrushes.Black,
                            startX, titleY + 12, leftFormat);

                        // Grid boundary rectangle (solid)
                        var gridPen = new XPen(XColors.Black, 0.5);
                        gfx.DrawRectangle(gridPen, startX, startY, gridW, gridH);

                        // Alignment targets at each corner + center
                        foreach (var (tx, ty, label) in targets)
                            DrawAlignmentTarget(gfx, tx, ty, label, false);

                        // Measurement rulers along top and left grid edges
                        float rulerOffset = 3 * MmToPt; // 3mm outside the grid
                        DrawRuler(gfx, startX, startY - rulerOffset, gridW, true, false);
                        DrawRuler(gfx, startX - rulerOffset, startY, gridH, false, false);

                        // CMYK color bars (full graduated density + grayscale)
                        DrawColorBars(gfx, startX, startY, gridW, gridH, pageWPt, pageHPt);

                        // Current calibration info
                        float txMm = calibration.TranslateXPt / MmToPt;
                        float tyMm = calibration.TranslateYPt / MmToPt;
                        gfx.DrawString(
                            $"Applied: translation X={txMm:F2}mm, Y={tyMm:F2}mm, rotation={calibration.RotationDegrees:F3}deg",
                            infoFont, XBrushes.Black, startX, pageHPt - 28, leftFormat);

                        // Instructions at very bottom
                        gfx.DrawString(
                            "Print this page duplex (flip on long edge). Hold up to light. Measure offset between " +
                            "solid (front) and dashed (back) targets at each corner. Enter offsets in Settings > Printer Calibration.",
                            instructionFont, XBrushes.DarkGray, startX, pageHPt - 16, leftFormat);
                    }

                    // ===== Page 2: Back (Offset) =====
                    var backPage = document.AddPage();
                    SetPageSize(backPage, settings);
                    using (var gfx = XGraphics.FromPdfPage(backPage))
                    {
                        // Apply calibration transform (rotation about page center + translation)
                        if (calibration.HasCorrection)
                        {
                            float pageCenterX = pageWPt / 2;
                            float pageCenterY = pageHPt / 2;
                            gfx.TranslateTransform(pageCenterX, pageCenterY);
                            gfx.RotateTransform(calibration.RotationDegrees);
                            gfx.TranslateTransform(-pageCenterX, -pageCenterY);
                            gfx.TranslateTransform(calibration.TranslateXPt, calibration.TranslateYPt);
                        }

                        // Title bar
                        float titleY = Math.Min(startY - 24, 10);
                        gfx.DrawString("PRINTER CALIBRATION TEST \u2014 BACK", titleFont, XBrushes.Black,
                            startX, titleY, leftFormat);
                        float txMm = calibration.TranslateXPt / MmToPt;
                        float tyMm = calibration.TranslateYPt / MmToPt;
                        gfx.DrawString(
                            $"Applied: translation X={txMm:F2}mm, Y={tyMm:F2}mm, rotation={calibration.RotationDegrees:F3}deg",
                            infoFont, XBrushes.Black, startX, titleY + 12, leftFormat);

                        // Grid boundary rectangle (dashed)
                        var dashedGridPen = new XPen(XColors.Black, 0.5) { DashStyle = XDashStyle.Dash };
                        gfx.DrawRectangle(dashedGridPen, startX, startY, gridW, gridH);

                        // Alignment targets at each corner + center (dashed)
                        foreach (var (tx, ty, label) in targets)
                            DrawAlignmentTarget(gfx, tx, ty, label, true);

                        // Measurement rulers (dashed)
                        float rulerOffset = 3 * MmToPt;
                        DrawRuler(gfx, startX, startY - rulerOffset, gridW, true, true);
                        DrawRuler(gfx, startX - rulerOffset, startY, gridH, false, true);

                        // No color bars on back (saves ink)
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

        /// <summary>
        /// Draws a precision alignment target with concentric circles, graduated crosshair, and label.
        /// </summary>
        private void DrawAlignmentTarget(XGraphics gfx, float cx, float cy, string label, bool dashed)
        {
            // Pens
            var finePen = new XPen(XColors.Black, 0.25);
            var medPen = new XPen(XColors.Black, 0.5);
            if (dashed)
            {
                finePen.DashStyle = XDashStyle.Dash;
                medPen.DashStyle = XDashStyle.Dash;
            }

            // Concentric circles at 2mm, 4mm, 6mm radius
            float[] radiiMm = { 2f, 4f, 6f };
            foreach (float rMm in radiiMm)
            {
                float r = rMm * MmToPt;
                gfx.DrawEllipse(finePen, cx - r, cy - r, 2 * r, 2 * r);
            }

            // Crosshair arms extending 8mm in each direction
            float armLen = 8 * MmToPt;
            gfx.DrawLine(finePen, cx - armLen, cy, cx + armLen, cy);
            gfx.DrawLine(finePen, cx, cy - armLen, cx, cy + armLen);

            // Graduated ruler marks along crosshair arms (1mm ticks, longer at 5mm, labels at 5mm)
            var tickPen = new XPen(XColors.Black, 0.25);
            if (dashed) tickPen.DashStyle = XDashStyle.Dash;
            var tickLabelFont = new XFont("Arial", 4);
            float shortTick = 0.5f * MmToPt;
            float longTick = 1.0f * MmToPt;

            for (int mm = 1; mm <= 8; mm++)
            {
                float d = mm * MmToPt;
                bool isMajor = (mm % 5 == 0);
                float tickLen = isMajor ? longTick : shortTick;

                // Right arm
                gfx.DrawLine(tickPen, cx + d, cy - tickLen, cx + d, cy + tickLen);
                // Left arm
                gfx.DrawLine(tickPen, cx - d, cy - tickLen, cx - d, cy + tickLen);
                // Down arm
                gfx.DrawLine(tickPen, cx - tickLen, cy + d, cx + tickLen, cy + d);
                // Up arm
                gfx.DrawLine(tickPen, cx - tickLen, cy - d, cx + tickLen, cy - d);

                if (isMajor)
                {
                    string numLabel = mm.ToString();
                    gfx.DrawString(numLabel, tickLabelFont, XBrushes.Black,
                        cx + d, cy + tickLen + 1, new XStringFormat
                        {
                            Alignment = XStringAlignment.Center,
                            LineAlignment = XLineAlignment.Near
                        });
                }
            }

            // Position label (TL, TR, BL, BR, C)
            var labelFont = new XFont("Arial", 5, XFontStyleEx.Bold);
            float labelOffset = 7 * MmToPt;
            gfx.DrawString(label, labelFont, XBrushes.Black,
                cx + labelOffset, cy - labelOffset, new XStringFormat
                {
                    Alignment = XStringAlignment.Near,
                    LineAlignment = XLineAlignment.Far
                });
        }

        /// <summary>
        /// Draws a precision measurement ruler with mm graduations.
        /// Short ticks every 1mm, medium ticks every 5mm, tall ticks every 10mm with number labels.
        /// </summary>
        private void DrawRuler(XGraphics gfx, float originX, float originY, float length, bool horizontal, bool dashed)
        {
            var pen = new XPen(XColors.Black, 0.25);
            if (dashed) pen.DashStyle = XDashStyle.Dash;

            var labelFont = new XFont("Arial", 5);
            float totalMm = length / MmToPt;
            int totalTicks = (int)Math.Floor(totalMm);

            float shortTick = 1.0f * MmToPt;
            float medTick = 1.5f * MmToPt;
            float tallTick = 2.5f * MmToPt;

            // Draw baseline
            if (horizontal)
                gfx.DrawLine(pen, originX, originY, originX + length, originY);
            else
                gfx.DrawLine(pen, originX, originY, originX, originY + length);

            for (int mm = 0; mm <= totalTicks; mm++)
            {
                float d = mm * MmToPt;
                float tickLen;
                if (mm % 10 == 0) tickLen = tallTick;
                else if (mm % 5 == 0) tickLen = medTick;
                else tickLen = shortTick;

                if (horizontal)
                {
                    float x = originX + d;
                    gfx.DrawLine(pen, x, originY, x, originY + tickLen);

                    if (mm % 10 == 0 && mm > 0)
                    {
                        gfx.DrawString(mm.ToString(), labelFont, XBrushes.Black,
                            x, originY + tallTick + 1, new XStringFormat
                            {
                                Alignment = XStringAlignment.Center,
                                LineAlignment = XLineAlignment.Near
                            });
                    }
                }
                else
                {
                    float y = originY + d;
                    gfx.DrawLine(pen, originX, y, originX + tickLen, y);

                    if (mm % 10 == 0 && mm > 0)
                    {
                        gfx.DrawString(mm.ToString(), labelFont, XBrushes.Black,
                            originX + tallTick + 1, y, new XStringFormat
                            {
                                Alignment = XStringAlignment.Near,
                                LineAlignment = XLineAlignment.Center
                            });
                    }
                }
            }
        }

        private void AddPage(PdfDocument doc, PageLayout settings, PrintSettings printSettings,
            List<CardModel> cards, int pageIndex, bool front,
            Dictionary<string, string> bleedCache,
            CalibrationTransform? calibration = null)
        {
            var page = doc.AddPage();
            SetPageSize(page, settings);

            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return;

            int startIdx = pageIndex * perPage;
            if (startIdx >= cards.Count) return;

            using var gfx = XGraphics.FromPdfPage(page);

            float startX = settings.MarginLeftMm * MmToPt;
            float startY = settings.MarginTopMm * MmToPt;
            float bleedPt = settings.BleedWidthMm * MmToPt;
            float cardWPt = settings.CardWidthMm * MmToPt;
            float cardHPt = settings.CardHeightMm * MmToPt;
            float cellW = cardWPt + 2 * bleedPt;
            float cellH = cardHPt + 2 * bleedPt;
            float strideX = settings.CellStrideXMm * MmToPt;
            float strideY = settings.CellStrideYMm * MmToPt;

            int cols = settings.CardsPerRow;
            float pageWPt = settings.PageWidthMm * MmToPt;
            float pageHPt = settings.PageHeightMm * MmToPt;

            if (!front && calibration != null && calibration.HasCorrection)
            {
                float pageCenterX = pageWPt / 2;
                float pageCenterY = pageHPt / 2;
                gfx.TranslateTransform(pageCenterX, pageCenterY);
                gfx.RotateTransform(calibration.RotationDegrees);
                gfx.TranslateTransform(-pageCenterX, -pageCenterY);
                gfx.TranslateTransform(calibration.TranslateXPt, calibration.TranslateYPt);
            }

            // When registration marks are active, suppress bleed, cut guides, and outlines
            bool useBleed = bleedCache.Count > 0 && !printSettings.ShowRegistrationMarks;

            // Pass 1a: Draw cut guides BEHIND card art (disabled with registration marks)
            if (printSettings.ShowCutGuides && !printSettings.ShowRegistrationMarks)
            {
                for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
                {
                    int row = i / cols;
                    int col = front ? (i % cols) : (cols - 1 - (i % cols));
                    float cellX = startX + col * strideX;
                    float cellY = startY + row * strideY;

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
                    float cellX = startX + col * strideX;
                    float cellY = startY + row * strideY;

                    DrawCropMarks(gfx, cellX, cellY, bleedPt, cardWPt, cardHPt, cropLen, cropOffset);
                }
            }

            // Pass 2: Draw card images ON TOP of cut guides
            for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
            {
                var card = cards[startIdx + i];

                int row = i / cols;
                int col = front ? (i % cols) : (cols - 1 - (i % cols));

                float cellX = startX + col * strideX;
                float cellY = startY + row * strideY;

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
                    float cellX = startX + col * strideX;
                    float cellY = startY + row * strideY;

                    DrawCardOutline(gfx, cellX, cellY, cellW, cellH, bleedPt, cardWPt, cardHPt, printSettings);
                }
            }

            // Pass 4: Draw registration marks ON TOP of everything (front pages only)
            if (printSettings.ShowRegistrationMarks && front)
            {
                DrawRegistrationMarks(gfx, pageWPt, pageHPt, printSettings);
            }

            // Pass 5: Draw CMYK color bars in the margin
            if (printSettings.ShowColorBars)
            {
                int rows = cols > 0 ? perPage / cols : 0;
                float gridWidth = cols > 0 ? (cols - 1) * strideX + cellW : 0;
                float gridHeight = rows > 0 ? (rows - 1) * strideY + cellH : 0;
                DrawColorBars(gfx, startX, startY, gridWidth, gridHeight, pageWPt, pageHPt);
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

        /// <summary>
        /// Draw CMYK density bars in available margin space.
        /// Tries bottom margin first (horizontal), then right margin (vertical).
        /// </summary>
        private void DrawColorBars(XGraphics gfx, float startX, float startY,
            float gridWidth, float gridHeight, float pageW, float pageH)
        {
            float gridRight = startX + gridWidth;
            float gridBottom = startY + gridHeight;
            float barThickness = 4 * MmToPt;
            float gap = 2 * MmToPt;
            float minClearance = 3 * MmToPt;

            bool fitsBottom = gridBottom + gap + barThickness <= pageH - minClearance;
            bool fitsRight = gridRight + gap + barThickness <= pageW - minClearance;

            if (!fitsBottom && !fitsRight) return;

            if (fitsBottom)
                DrawColorBarStrip(gfx, startX, gridBottom + gap, gridWidth, barThickness, false);
            else
                DrawColorBarStrip(gfx, gridRight + gap, startY, gridHeight, barThickness, true);
        }

        private void DrawColorBarStrip(XGraphics gfx, float originX, float originY,
            float stripLength, float stripThickness, bool vertical)
        {
            var colors = new (string Label, int R, int G, int B)[]
            {
                ("C", 0, 174, 239), ("M", 236, 0, 140), ("Y", 255, 242, 0),
                ("K", 0, 0, 0), ("R", 237, 28, 36), ("G", 0, 166, 81), ("B", 46, 49, 146),
            };

            int totalPatches = colors.Length * 4 + 8;
            float patchSize = stripLength / totalPatches;
            float pos = 0;

            var labelFont = new XFont("Arial", 5);
            var labelFormat = new XStringFormat
            {
                Alignment = XStringAlignment.Center,
                LineAlignment = XLineAlignment.Far
            };

            foreach (var (label, cr, cg, cb) in colors)
            {
                float[] densities = { 0.25f, 0.50f, 0.75f, 1.0f };
                foreach (float d in densities)
                {
                    int r = (int)(255 + (cr - 255) * d);
                    int g = (int)(255 + (cg - 255) * d);
                    int b = (int)(255 + (cb - 255) * d);
                    var brush = new XSolidBrush(XColor.FromArgb(r, g, b));

                    if (vertical)
                        gfx.DrawRectangle(brush, originX, originY + pos, stripThickness, patchSize);
                    else
                        gfx.DrawRectangle(brush, originX + pos, originY, patchSize, stripThickness);
                    pos += patchSize;
                }

                float labelPos = pos - patchSize * 2;
                if (vertical)
                    gfx.DrawString(label, labelFont, XBrushes.Black,
                        originX + stripThickness + 2, originY + labelPos + patchSize / 2, labelFormat);
                else
                    gfx.DrawString(label, labelFont, XBrushes.Black,
                        originX + labelPos, originY - 1, labelFormat);
            }

            for (int i = 0; i < 8; i++)
            {
                int v = 255 - (int)(255 * i / 7.0);
                var brush = new XSolidBrush(XColor.FromArgb(v, v, v));
                if (vertical)
                    gfx.DrawRectangle(brush, originX, originY + pos, stripThickness, patchSize);
                else
                    gfx.DrawRectangle(brush, originX + pos, originY, patchSize, stripThickness);
                pos += patchSize;
            }

            if (vertical)
                gfx.DrawRectangle(new XPen(XColors.Black, 0.25),
                    originX, originY, stripThickness, stripLength);
            else
                gfx.DrawRectangle(new XPen(XColors.Black, 0.25),
                    originX, originY, stripLength, stripThickness);
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

        /// <summary>Each card in the list is one slot on the page — no quantity expansion.</summary>
        private List<CardModel> ExpandCards(List<CardModel> cards) => cards;

        private int CalcPageCount(int cardCount, PageLayout settings)
        {
            int perPage = settings.CardsPerPage;
            if (perPage <= 0) return 0;
            return (int)Math.Ceiling((double)cardCount / perPage);
        }
    }
}
