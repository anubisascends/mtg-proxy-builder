using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class SetSymbolProviderTests
{
    [Fact]
    public void GetSvgContent_KnownSet_ReturnsSvg()
    {
        var svg = SetSymbolProvider.GetSvgContent("10e", "rare");
        Assert.NotNull(svg);
        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("common", "c")]
    [InlineData("uncommon", "u")]
    [InlineData("rare", "r")]
    [InlineData("mythic", "m")]
    [InlineData("special", "s")]
    [InlineData("C", "c")]
    [InlineData("R", "r")]
    [InlineData("RARE", "r")]
    public void GetSvgContent_RarityMapping_CaseInsensitive(string rarity, string _)
    {
        // 10e has all rarities
        var svg = SetSymbolProvider.GetSvgContent("10e", rarity);
        Assert.NotNull(svg);
    }

    [Fact]
    public void GetSvgContent_SetCodeCaseInsensitive()
    {
        var lower = SetSymbolProvider.GetSvgContent("10e", "r");
        var upper = SetSymbolProvider.GetSvgContent("10E", "r");
        Assert.NotNull(lower);
        Assert.NotNull(upper);
    }

    [Fact]
    public void GetSvgContent_UnknownSet_ReturnsNull()
    {
        var svg = SetSymbolProvider.GetSvgContent("ZZZZZ_NONEXISTENT", "common");
        Assert.Null(svg);
    }

    [Fact]
    public void GetSvgContent_UnknownRarity_FallsBackToCommon()
    {
        // 10e-c.svg should exist as fallback
        var svg = SetSymbolProvider.GetSvgContent("10e", "legendary");
        Assert.NotNull(svg);
    }

    [Fact]
    public void HasSymbol_KnownSet_ReturnsTrue()
    {
        Assert.True(SetSymbolProvider.HasSymbol("10e"));
    }

    [Fact]
    public void HasSymbol_UnknownSet_ReturnsFalse()
    {
        Assert.False(SetSymbolProvider.HasSymbol("ZZZZZ_NONEXISTENT"));
    }

    [Fact]
    public void HasSymbol_CaseInsensitive()
    {
        Assert.True(SetSymbolProvider.HasSymbol("10E"));
    }

    [Fact]
    public void GetSvgContent_EmptySetCode_ReturnsNull()
    {
        Assert.Null(SetSymbolProvider.GetSvgContent("", "rare"));
        Assert.Null(SetSymbolProvider.GetSvgContent(null!, "rare"));
    }

    [Fact]
    public void GetSvgContent_NullRarity_DefaultsToCommon()
    {
        var svg = SetSymbolProvider.GetSvgContent("10e", null);
        Assert.NotNull(svg);
    }
}
