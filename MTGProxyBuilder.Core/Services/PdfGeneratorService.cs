using MTGProxyBuilder.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MTGProxyBuilder.Core.Services
{
    public class PdfGeneratorService
    {
        private const float MmToPt = 72f / 25.4f;
        private readonly BleedProcessor _bleedProcessor = new();

        public Task<bool> GeneratePdfAsync(ProjectModel project, string outputPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var document = new PdfDocument();
                    document.Info.Title = project.ProjectName;

                    var settings = project.PageSettings;
                    var printSettings = project.PrintSettings;
                    var expandedFronts = ExpandCards(project.Cards);
                    var expandedBacks = ExpandCards(project.Cards.Where(c => c.IncludeBack).ToList());

                    if (printSettings.PrintMode == PrintMode.Duplex)
                    {
                        int frontPageCount = CalcPageCount(expandedFronts.Count, settings);
                        int backPageCount = CalcPageCount(expandedBacks.Count, settings);
                        int totalPages = Math.Max(frontPageCount, backPageCount);

                        for (int i = 0; i < totalPages; i++)
                        {
                            AddPage(document, settings, printSettings, expandedFronts, i, true);
                            AddPage(document, settings, printSettings, expandedBacks, i, false);
                        }
                    }
                    else if (printSettings.PrintMode == PrintMode.FrontsOnly)
                    {
                        int pageCount = CalcPageCount(expandedFronts.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedFronts, i, true);
                    }
                    else
                    {
                        int pageCount = CalcPageCount(expandedBacks.Count, settings);
                        for (int i = 0; i < pageCount; i++)
                            AddPage(document, settings, printSettings, expandedBacks, i, false);
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

        private void AddPage(PdfDocument doc, PageLayout settings, PrintSettings printSettings,
            List<CardModel> cards, int pageIndex, bool front)
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

            int cols = settings.CardsPerRow;

            // Calculate bleed in pixels for image processing (based on card image resolution)
            int bleedPx = Math.Max(1, (int)(settings.BleedWidthMm / settings.CardWidthMm * 600));

            for (int i = 0; i < perPage && (startIdx + i) < cards.Count; i++)
            {
                var card = cards[startIdx + i];

                int row = i / cols;
                int col = front ? (i % cols) : (cols - 1 - (i % cols));

                float cellX = startX + col * cellW;
                float cellY = startY + row * cellH;

                string imagePath = front ? card.ArtworkPath : (card.BackArtworkPath ?? card.ArtworkPath);

                if (settings.BleedWidthMm > 0 && !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    // Process image with edge-extended bleed
                    var bleedImage = _bleedProcessor.GetBleedExtendedImage(imagePath, bleedPx);
                    DrawCard(gfx, bleedImage ?? imagePath, cellX, cellY, cellW, cellH);
                }
                else
                {
                    // No bleed: draw card image at card size, centered in cell
                    DrawCard(gfx, imagePath, cellX + bleedPt, cellY + bleedPt, cardWPt, cardHPt);
                }

                // Cut guides — extend from card edge to page edge
                if (printSettings.ShowCutGuides)
                {
                    float pageWPt = settings.PageWidthMm * MmToPt;
                    float pageHPt = settings.PageHeightMm * MmToPt;
                    DrawCutGuides(gfx, cellX, cellY, cellW, cellH, bleedPt, cardWPt, cardHPt, pageWPt, pageHPt);
                }
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
