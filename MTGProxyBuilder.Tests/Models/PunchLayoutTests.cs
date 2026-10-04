using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Tests.Models;

public class PunchLayoutTests
{
    private const float Letter_W = 215.9f;
    private const float Letter_H = 279.4f;

    private static PageLayout LetterPortrait(float bleed = 1.5f) => new()
    {
        PageWidthMm = Letter_W,
        PageHeightMm = Letter_H,
        CardWidthMm = 63,
        CardHeightMm = 88,
        BleedWidthMm = bleed
    };

    [Fact]
    public void ShortSide_OnPortrait_RunsAcrossColumns()
    {
        var layout = LetterPortrait();
        layout.PunchSide = PunchSide.Short;
        Assert.True(layout.IsPunchAxisHorizontal);

        layout.PunchSide = PunchSide.Long;
        Assert.False(layout.IsPunchAxisHorizontal);
    }

    [Fact]
    public void LongSide_OnLandscape_RunsAcrossColumns()
    {
        var layout = LetterPortrait();
        layout.IsLandscape = true;
        layout.PunchSide = PunchSide.Long;
        Assert.True(layout.IsPunchAxisHorizontal);
    }

    [Fact]
    public void Enable_SetsColumnsAndSpacingSoCardsAreCentredInStrips()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 70;
        layout.PunchSide = PunchSide.Short;
        layout.IsPunchModeEnabled = true;

        Assert.True(layout.IsPunchActive);
        Assert.Equal(3, layout.ColumnsOverride);
        Assert.Equal(4f, layout.HorizontalSpacingMm, 3); // 70 - 63 - 2*1.5

        var cuts = layout.GetPunchCutPositionsMm();
        Assert.Equal(new[] { 2.95f, 72.95f, 142.95f, 212.95f }, cuts.Select(c => MathF.Round(c, 2)));

        // Every card's trim centre sits exactly in the middle of its strip
        for (int col = 0; col < 3; col++)
        {
            float cardLeft = layout.MarginLeftMm + col * layout.CellStrideXMm + layout.BleedWidthMm;
            float cardCentre = cardLeft + layout.CardWidthMm / 2f;
            float stripCentre = (cuts[col] + cuts[col + 1]) / 2f;
            Assert.Equal(stripCentre, cardCentre, 3);
        }
    }

    [Fact]
    public void Enable_CutsAreSymmetricOnThePage_SoDuplexMirroringLinesUp()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 70;
        layout.IsPunchModeEnabled = true;

        var cuts = layout.GetPunchCutPositionsMm();
        Assert.Equal(cuts[0], Letter_W - cuts[^1], 3);
    }

    [Fact]
    public void LongSide_OnPortrait_ControlsRows()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 95;
        layout.PunchSide = PunchSide.Long;
        layout.IsPunchModeEnabled = true;

        Assert.True(layout.IsPunchControllingRows);
        Assert.False(layout.IsPunchControllingColumns);
        Assert.Equal(2, layout.RowsOverride);           // floor(279.4 / 95)
        Assert.Equal(4f, layout.VerticalSpacingMm, 3);  // 95 - 88 - 3
        Assert.Null(layout.ColumnsOverride);
    }

    [Fact]
    public void OpeningNotLargerThanCard_IsInvalid_AndLeavesLayoutAlone()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 63;
        layout.IsPunchModeEnabled = true;

        Assert.NotNull(layout.PunchValidationError);
        Assert.NotNull(layout.PunchModeError);
        Assert.False(layout.IsPunchActive);
        Assert.Null(layout.ColumnsOverride);
        Assert.Equal(0f, layout.HorizontalSpacingMm);
        Assert.Empty(layout.GetPunchCutPositionsMm());
    }

    [Fact]
    public void OpeningLargerThanPage_IsInvalid()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 300;
        Assert.NotNull(layout.PunchValidationError);
    }

    [Fact]
    public void PunchModeError_IsNullWhileModeIsOff()
    {
        var layout = LetterPortrait();
        Assert.NotNull(layout.PunchValidationError); // no opening entered
        Assert.Null(layout.PunchModeError);
    }

    [Fact]
    public void WideBleed_GivesNegativeSpacing_AndUnclampedMarginKeepsStripsCentred()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210, PageHeightMm = 297,
            CardWidthMm = 63, CardHeightMm = 88,
            BleedWidthMm = 5,
            PunchOpeningMm = 70
        };
        layout.IsPunchModeEnabled = true;

        Assert.Equal(-3f, layout.HorizontalSpacingMm, 3); // 70 - 63 - 10
        Assert.Equal(-1.5f, layout.MarginLeftMm, 3);       // grid is 213mm on a 210mm page
        var cuts = layout.GetPunchCutPositionsMm();
        Assert.Equal(0f, cuts[0], 3);
        Assert.Equal(210f, cuts[^1], 3);

        var clip = layout.GetPunchClipRectMm(1, 0);
        Assert.Equal((70f, 0f, 70f, 297f), (MathF.Round(clip.X, 3), clip.Y, clip.W, clip.H));
    }

    [Fact]
    public void Disable_KeepsCount_ClampsNegativeSpacing()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210, PageHeightMm = 297,
            CardWidthMm = 63, CardHeightMm = 88,
            BleedWidthMm = 5,
            PunchOpeningMm = 70
        };
        layout.IsPunchModeEnabled = true;
        layout.IsPunchModeEnabled = false;

        Assert.False(layout.IsPunchActive);
        Assert.Equal(3, layout.ColumnsOverride);
        Assert.Equal(0f, layout.HorizontalSpacingMm);
        Assert.True(layout.MarginLeftMm >= 0);
    }

    [Fact]
    public void ChangingBleed_ReappliesWhileActive()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 70;
        layout.IsPunchModeEnabled = true;

        layout.BleedWidthMm = 2;
        Assert.Equal(3f, layout.HorizontalSpacingMm, 3); // 70 - 63 - 4
    }

    [Fact]
    public void OrientationChange_MovesPunchAxis_AndReleasesOldAxis()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 95;
        layout.PunchSide = PunchSide.Long;  // portrait: rows
        layout.IsPunchModeEnabled = true;
        Assert.Equal(2, layout.RowsOverride);

        layout.IsLandscape = true;          // long side is now the width: columns
        Assert.True(layout.IsPunchControllingColumns);
        Assert.Null(layout.RowsOverride);
        Assert.Equal(0f, layout.VerticalSpacingMm);
        Assert.Equal(2, layout.ColumnsOverride);            // floor(279.4 / 95)
        Assert.Equal(95f - 63f - 3f, layout.HorizontalSpacingMm, 3);
    }

    [Fact]
    public void NoPrintWarning_WhenOuterCardsAreNearTheEdge()
    {
        // A4 width 210 with three 70mm strips: no room left, trim edge only 3.5mm in
        var tight = new PageLayout { PageWidthMm = 210, PageHeightMm = 297, BleedWidthMm = 1.5f, PunchOpeningMm = 70 };
        tight.IsPunchModeEnabled = true;
        Assert.Contains("card itself", tight.PunchNoPrintWarning);

        // Letter: trim 6.45mm in, bleed 4.95mm in
        var roomy = LetterPortrait();
        roomy.PunchOpeningMm = 70;
        roomy.IsPunchModeEnabled = true;
        Assert.Null(roomy.PunchNoPrintWarning);
    }

    [Fact]
    public void NoPrintWarning_BleedOnly()
    {
        // Letter, opening 71, bleed 3: first cut = (215.9 - 213) / 2 = 1.45,
        // card trim = 1.45 + (71 - 63) / 2 = 5.45 (clear), bleed edge = 2.45 (inside the zone)
        var layout = LetterPortrait(bleed: 3);
        layout.PunchOpeningMm = 71;
        layout.IsPunchModeEnabled = true;
        Assert.Contains("bleed", layout.PunchNoPrintWarning);
    }

    [Fact]
    public void Summary_ListsStripsAndCuts()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 70;
        Assert.Null(layout.PunchSummary);

        layout.IsPunchModeEnabled = true;
        Assert.Equal("3 strips × 70 mm. Cuts at 2.95, 72.95, 142.95, 212.95 mm from the left edge.", layout.PunchSummary);
    }

    [Fact]
    public void JsonRoundTrip_PreservesPunchLayout()
    {
        var layout = LetterPortrait();
        layout.PunchOpeningMm = 70;
        layout.PunchSide = PunchSide.Short;
        layout.IsPunchModeEnabled = true;

        string json = JsonConvert.SerializeObject(layout);
        var loaded = JsonConvert.DeserializeObject<PageLayout>(json)!;

        Assert.True(loaded.IsPunchActive);
        Assert.Equal(70f, loaded.PunchOpeningMm);
        Assert.Equal(PunchSide.Short, loaded.PunchSide);
        Assert.Equal(layout.ColumnsOverride, loaded.ColumnsOverride);
        Assert.Equal(layout.HorizontalSpacingMm, loaded.HorizontalSpacingMm, 3);
        Assert.Equal(layout.GetPunchCutPositionsMm(), loaded.GetPunchCutPositionsMm());
        Assert.DoesNotContain("PunchSummary", json);
    }
}
