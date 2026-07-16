using System.Text.RegularExpressions;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class SvgCutLineSpacingTests
{
    private const float MmToPt = 72f / 25.4f;

    [Fact]
    public async Task BuildSvg_HorizontalSpacing_ShiftsSecondColumnByStride()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            CardWidthMm = 63,
            CardHeightMm = 88,
            BleedWidthMm = 0,
            ColumnsOverride = 3,
            RowsOverride = 3,
            HorizontalSpacingMm = 10
        };
        var project = new ProjectModel
        {
            PageSettings = layout,
            Cards = new List<CardModel> { new(), new() } // two slots, row 0 col 0 and col 1
        };

        var dir = Path.Combine(Path.GetTempPath(), "mtg_svg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = await new SvgCutLineService().GenerateSvgAsync(project, dir, "test");
            Assert.NotEmpty(files);

            string svg = await File.ReadAllTextAsync(files[0]);
            var xs = Regex.Matches(svg, "<rect x=\"([0-9.]+)\"")
                          .Select(m => float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                          .ToList();
            Assert.True(xs.Count >= 2, "expected at least two card rects");

            float deltaX = xs[1] - xs[0];
            float expected = layout.CellStrideXMm * MmToPt; // (63 + 10) * MmToPt
            Assert.Equal(expected, deltaX, 1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
