using System.Globalization;
using System.Text;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Core.Services
{
    public class SvgCutLineService
    {
        private const float MmToPt = 72f / 25.4f;

        /// <summary>
        /// Generates SVG cut line files for unique page layouts.
        /// Returns the list of generated file paths.
        /// </summary>
        public Task<List<string>> GenerateSvgAsync(ProjectModel project, string outputDirectory, string baseName)
        {
            return Task.Run(() =>
            {
                var settings = project.PageSettings;
                var printSettings = project.PrintSettings;
                var generatedFiles = new List<string>();

                int perPage = settings.CardsPerPage;
                if (perPage <= 0) return generatedFiles;

                // Determine total cards for front pages
                int totalCards = project.Cards.Sum(c => c.Quantity);
                if (totalCards == 0) return generatedFiles;

                int fullPages = totalCards / perPage;
                int remainder = totalCards % perPage;

                // Generate SVG for a full page (all slots filled)
                if (fullPages > 0)
                {
                    string fullPath = Path.Combine(outputDirectory, $"{baseName}_full.svg");
                    string svg = BuildSvg(settings, printSettings, perPage);
                    File.WriteAllText(fullPath, svg);
                    generatedFiles.Add(fullPath);
                }

                // Generate SVG for the partial last page (if different from full)
                if (remainder > 0)
                {
                    string partialPath = fullPages > 0
                        ? Path.Combine(outputDirectory, $"{baseName}_partial_{remainder}.svg")
                        : Path.Combine(outputDirectory, $"{baseName}_full.svg");

                    if (fullPages > 0 || !generatedFiles.Any())
                    {
                        string svg = BuildSvg(settings, printSettings, remainder);
                        File.WriteAllText(partialPath, svg);
                        generatedFiles.Add(partialPath);
                    }
                }

                return generatedFiles;
            });
        }

        private string BuildSvg(PageLayout settings, PrintSettings printSettings, int cardCount)
        {
            float pageW = settings.PageWidthMm * MmToPt;
            float pageH = settings.PageHeightMm * MmToPt;
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
            float radiusPt = printSettings.CornerRadiusMm * MmToPt;

            // Page size in inches for width/height attributes
            float pageWIn = settings.PageWidthMm / 25.4f;
            float pageHIn = settings.PageHeightMm / 25.4f;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine(I($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pageWIn:F4}in\" height=\"{pageHIn:F4}in\" viewBox=\"0 0 {pageW:F2} {pageH:F2}\">"));

            for (int i = 0; i < cardCount; i++)
            {
                int row = i / cols;
                int col = i % cols;

                float cellX = startX + col * strideX;
                float cellY = startY + row * strideY;

                float cardX = cellX + bleedPt;
                float cardY = cellY + bleedPt;

                sb.AppendLine(SvgRect(cardX, cardY, cardWPt, cardHPt, "none", "black", 0.5f, radiusPt));
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        private static string SvgRect(float x, float y, float w, float h, string fill, string stroke, float strokeWidth, float rx = 0)
        {
            var rxAttr = rx > 0
                ? FormattableString.Invariant($"rx=\"{rx:F2}\" ry=\"{rx:F2}\" ")
                : "";
            return FormattableString.Invariant(
                $"  <rect x=\"{x:F2}\" y=\"{y:F2}\" width=\"{w:F2}\" height=\"{h:F2}\" {rxAttr}fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth:F1}\"/>");
        }
    }
}
