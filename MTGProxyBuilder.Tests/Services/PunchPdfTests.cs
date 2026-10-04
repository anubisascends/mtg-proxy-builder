using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class PunchPdfTests
{
    [Fact]
    public async Task GeneratePdf_PunchModeDuplex_WithClippedBleed_Succeeds()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210, PageHeightMm = 297,
            CardWidthMm = 63, CardHeightMm = 88,
            BleedWidthMm = 5,          // wider than half the gap: negative spacing, bleed clipped
            PunchOpeningMm = 70
        };
        layout.IsPunchModeEnabled = true;

        var project = new ProjectModel { ProjectName = "Punch", PageSettings = layout };
        for (int i = 0; i < 4; i++)
            project.Cards.Add(new CardModel { Name = $"Card {i}", IncludeBack = true });
        project.PrintSettings.PrintMode = PrintMode.Duplex;
        project.PrintSettings.ShowCutGuides = true;

        string path = Path.Combine(Path.GetTempPath(), $"mtg_punch_{Guid.NewGuid():N}.pdf");
        try
        {
            bool ok = await new PdfGeneratorService().GeneratePdfAsync(project, path);
            Assert.True(ok);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
