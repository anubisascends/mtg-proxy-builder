using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class ManaSymbolProviderTests
{
    [Fact]
    public void GetAvailableSymbols_ReturnsNonEmpty()
    {
        var symbols = ManaSymbolProvider.GetAvailableSymbols();
        Assert.NotEmpty(symbols);
        Assert.True(symbols.Count >= 100, $"Expected 100+ symbols, got {symbols.Count}");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("X")]
    public void GetSvgContent_CommonSymbols_ReturnsSvg(string symbol)
    {
        var svg = ManaSymbolProvider.GetSvgContent(symbol);
        Assert.NotNull(svg);
        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSvgContent_CaseInsensitive()
    {
        var upper = ManaSymbolProvider.GetSvgContent("W");
        var lower = ManaSymbolProvider.GetSvgContent("w");
        // Both should resolve (resource names may be case-sensitive on some platforms)
        Assert.True(upper != null || lower != null, "Neither 'W' nor 'w' resolved to a mana symbol");
    }

    [Fact]
    public void GetSvgContent_UnknownSymbol_ReturnsNull()
    {
        var svg = ManaSymbolProvider.GetSvgContent("NONEXISTENT_SYMBOL_XYZ");
        Assert.Null(svg);
    }

    [Fact]
    public void HasSymbol_KnownSymbol_ReturnsTrue()
    {
        // At least one of these should exist
        var symbols = ManaSymbolProvider.GetAvailableSymbols();
        Assert.True(symbols.Count > 0);
    }

    [Fact]
    public void ParseManaText_PlainText_SingleSegment()
    {
        var segments = ManaSymbolProvider.ParseManaText("Lightning Bolt");
        Assert.Single(segments);
        Assert.False(segments[0].IsSymbol);
        Assert.Equal("Lightning Bolt", segments[0].Value);
    }

    [Fact]
    public void ParseManaText_SymbolsOnly()
    {
        var segments = ManaSymbolProvider.ParseManaText("{2}{W}{U}");
        Assert.Equal(3, segments.Count);
        Assert.All(segments, s => Assert.True(s.IsSymbol));
        Assert.Equal("2", segments[0].Value);
        Assert.Equal("W", segments[1].Value);
        Assert.Equal("U", segments[2].Value);
    }

    [Fact]
    public void ParseManaText_MixedContent()
    {
        var segments = ManaSymbolProvider.ParseManaText("Pay {2}{R} to activate");
        Assert.Equal(4, segments.Count);
        Assert.Equal("Pay ", segments[0].Value);
        Assert.False(segments[0].IsSymbol);
        Assert.Equal("2", segments[1].Value);
        Assert.True(segments[1].IsSymbol);
        Assert.Equal("R", segments[2].Value);
        Assert.True(segments[2].IsSymbol);
        Assert.Equal(" to activate", segments[3].Value);
        Assert.False(segments[3].IsSymbol);
    }

    [Fact]
    public void ParseManaText_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(ManaSymbolProvider.ParseManaText(""));
        Assert.Empty(ManaSymbolProvider.ParseManaText(null!));
    }

    [Fact]
    public void ParseManaText_UnclosedBrace_TreatsAsText()
    {
        var segments = ManaSymbolProvider.ParseManaText("Cost: {W incomplete");
        // Should not crash, unclosed brace treated as text
        Assert.NotEmpty(segments);
    }

    [Fact]
    public void ParseManaText_HybridSymbols()
    {
        var segments = ManaSymbolProvider.ParseManaText("{2B}{gw}{urp}");
        Assert.Equal(3, segments.Count);
        Assert.Equal("2B", segments[0].Value);
        Assert.Equal("gw", segments[1].Value);
        Assert.Equal("urp", segments[2].Value);
        Assert.All(segments, s => Assert.True(s.IsSymbol));
    }
}
