using MTGProxyBuilder.Core;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class PageLayoutTests
{
    [Fact]
    public void DefaultValues_AreA4WithMtgCards()
    {
        var layout = new PageLayout();
        Assert.Equal(210f, layout.PageWidthMm);
        Assert.Equal(297f, layout.PageHeightMm);
        Assert.Equal(Constants.DefaultCardWidthMm, layout.CardWidthMm);
        Assert.Equal(Constants.DefaultCardHeightMm, layout.CardHeightMm);
        Assert.Equal(Constants.DefaultBleedMm, layout.BleedWidthMm);
        Assert.False(layout.IsLandscape);
    }

    [Fact]
    public void AutoCardsPerRow_CalculatesCorrectly()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            CardWidthMm = 63,
            BleedWidthMm = 0
        };
        // 210 / 63 = 3.33 → 3
        Assert.Equal(3, layout.AutoCardsPerRow);
    }

    [Fact]
    public void AutoCardsPerRow_WithBleed()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            CardWidthMm = 63,
            BleedWidthMm = 3
        };
        // 210 / (63 + 6) = 210 / 69 = 3.04 → 3
        Assert.Equal(3, layout.AutoCardsPerRow);
    }

    [Fact]
    public void AutoCardsPerColumn_CalculatesCorrectly()
    {
        var layout = new PageLayout
        {
            PageHeightMm = 297,
            CardHeightMm = 88,
            BleedWidthMm = 0
        };
        // 297 / 88 = 3.37 → 3
        Assert.Equal(3, layout.AutoCardsPerColumn);
    }

    [Fact]
    public void CardsPerPage_IsProductOfRowsAndColumns()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            CardWidthMm = 63,
            CardHeightMm = 88,
            BleedWidthMm = 0
        };
        Assert.Equal(layout.AutoCardsPerRow * layout.AutoCardsPerColumn, layout.CardsPerPage);
    }

    [Fact]
    public void ColumnsOverride_TakesPrecedence()
    {
        var layout = new PageLayout { ColumnsOverride = 5 };
        Assert.Equal(5, layout.CardsPerRow);
    }

    [Fact]
    public void RowsOverride_TakesPrecedence()
    {
        var layout = new PageLayout { RowsOverride = 7 };
        Assert.Equal(7, layout.CardsPerColumn);
    }

    [Fact]
    public void ColumnsOverride_Null_UsesAuto()
    {
        var layout = new PageLayout { ColumnsOverride = 5 };
        layout.ColumnsOverride = null;
        Assert.Equal(layout.AutoCardsPerRow, layout.CardsPerRow);
    }

    [Fact]
    public void IsLandscape_SwapsDimensions()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            PageHeightMm = 297
        };
        layout.IsLandscape = true;
        Assert.Equal(297f, layout.PageWidthMm);
        Assert.Equal(210f, layout.PageHeightMm);
    }

    [Fact]
    public void IsLandscape_ToggleBackRestores()
    {
        var layout = new PageLayout();
        float origW = layout.PageWidthMm;
        float origH = layout.PageHeightMm;

        layout.IsLandscape = true;
        layout.IsLandscape = false;

        Assert.Equal(origW, layout.PageWidthMm);
        Assert.Equal(origH, layout.PageHeightMm);
    }

    [Theory]
    [InlineData("A4", 210, 297)]
    [InlineData("A3", 297, 420)]
    [InlineData("Letter", 215.9f, 279.4f)]
    [InlineData("Legal", 215.9f, 355.6f)]
    [InlineData("Tabloid", 279.4f, 431.8f)]
    public void ApplyPagePreset_SetsCorrectDimensions(string preset, float expectedW, float expectedH)
    {
        var layout = new PageLayout();
        layout.ApplyPagePreset(preset);
        Assert.Equal(expectedW, layout.PageWidthMm);
        Assert.Equal(expectedH, layout.PageHeightMm);
    }

    [Fact]
    public void ApplyPagePreset_RespectsLandscape()
    {
        var layout = new PageLayout { IsLandscape = true };
        layout.ApplyPagePreset("A4");
        Assert.Equal(297f, layout.PageWidthMm);
        Assert.Equal(210f, layout.PageHeightMm);
    }

    [Fact]
    public void CenterGrid_CentersMarginsSymmetrically()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            CardWidthMm = 63,
            CardHeightMm = 88,
            BleedWidthMm = 0
        };
        layout.CenterGrid();

        Assert.Equal(layout.MarginLeftMm, layout.MarginRightMm);
        Assert.Equal(layout.MarginTopMm, layout.MarginBottomMm);
    }

    [Fact]
    public void CenterGrid_MarginsAreNonNegative()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 50, // Very small page
            CardWidthMm = 63, // Card wider than page
            CardHeightMm = 88,
            BleedWidthMm = 0
        };
        layout.CenterGrid();

        Assert.True(layout.MarginLeftMm >= 0);
        Assert.True(layout.MarginTopMm >= 0);
    }

    [Fact]
    public void CenterGrid_GridFitsInPage()
    {
        var layout = new PageLayout();
        layout.CenterGrid();

        float gridW = layout.CardsPerRow * (layout.CardWidthMm + 2 * layout.BleedWidthMm);
        float gridH = layout.CardsPerColumn * (layout.CardHeightMm + 2 * layout.BleedWidthMm);
        float totalW = gridW + layout.MarginLeftMm + layout.MarginRightMm;
        float totalH = gridH + layout.MarginTopMm + layout.MarginBottomMm;

        // Total should be approximately equal to page size (within rounding)
        Assert.InRange(totalW, layout.PageWidthMm - 1, layout.PageWidthMm + 1);
        Assert.InRange(totalH, layout.PageHeightMm - 1, layout.PageHeightMm + 1);
    }

    [Fact]
    public void AutoCardsPerRow_MinimumIsOne()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 10,
            CardWidthMm = 100, // Card much wider than page
            BleedWidthMm = 0
        };
        Assert.Equal(1, layout.AutoCardsPerRow);
    }

    [Fact]
    public void PropertyChanged_FiresOnCardWidthChange()
    {
        var layout = new PageLayout();
        var changedProps = new List<string>();
        layout.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        layout.CardWidthMm = 59;

        Assert.Contains("CardWidthMm", changedProps);
        Assert.Contains("CardsPerRow", changedProps);
        Assert.Contains("CardsPerPage", changedProps);
    }

    [Fact]
    public void GetUsableWidthMm_SubtractsMargins()
    {
        var layout = new PageLayout();
        float usable = layout.GetUsableWidthMm();
        Assert.Equal(layout.PageWidthMm - layout.MarginLeftMm - layout.MarginRightMm, usable);
    }
}
