using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

// ================================================================
//  PARSER TESTS
// ================================================================

public class FilterParserTests
{
    private static FilterToken Single(string text)
    {
        var tokens = FilterParser.Parse(text);
        Assert.Single(tokens);
        return tokens[0];
    }

    // ----------------------------------------------------------------
    //  Free text
    // ----------------------------------------------------------------

    [Fact]
    public void FreeText_BecomesNameEqToken()
    {
        var t = Single("Bolt");
        Assert.Equal(TokenKind.Filter, t.Kind);
        Assert.Equal(FilterField.Name, t.Field);
        Assert.Equal(FilterOp.Eq, t.Op);
        Assert.Equal("Bolt", t.Value);
    }

    // ----------------------------------------------------------------
    //  DPI filters
    // ----------------------------------------------------------------

    [Fact]
    public void Dpi_GreaterThan()
    {
        var t = Single("dpi:>800");
        Assert.Equal(FilterField.Dpi, t.Field);
        Assert.Equal(FilterOp.Gt, t.Op);
        Assert.Equal("800", t.Value);
    }

    [Fact]
    public void Dpi_GreaterThanOrEqual()
    {
        var t = Single("dpi:>=1200");
        Assert.Equal(FilterField.Dpi, t.Field);
        Assert.Equal(FilterOp.Gte, t.Op);
        Assert.Equal("1200", t.Value);
    }

    [Fact]
    public void Dpi_Exact()
    {
        var t = Single("dpi:600");
        Assert.Equal(FilterField.Dpi, t.Field);
        Assert.Equal(FilterOp.Eq, t.Op);
        Assert.Equal("600", t.Value);
    }

    [Fact]
    public void Dpi_Not()
    {
        var t = Single("dpi:!600");
        Assert.Equal(FilterField.Dpi, t.Field);
        Assert.Equal(FilterOp.Not, t.Op);
        Assert.Equal("600", t.Value);
    }

    [Fact]
    public void Dpi_LessThan()
    {
        var t = Single("dpi:<600");
        Assert.Equal(FilterField.Dpi, t.Field);
        Assert.Equal(FilterOp.Lt, t.Op);
        Assert.Equal("600", t.Value);
    }

    [Fact]
    public void Dpi_LessThanOrEqual()
    {
        var t = Single("dpi:<=800");
        Assert.Equal(FilterField.Dpi, t.Field);
        Assert.Equal(FilterOp.Lte, t.Op);
        Assert.Equal("800", t.Value);
    }

    // ----------------------------------------------------------------
    //  Source filters
    // ----------------------------------------------------------------

    [Fact]
    public void Source_Exact()
    {
        var t = Single("source:Chilli_Axe");
        Assert.Equal(FilterField.Source, t.Field);
        Assert.Equal(FilterOp.Eq, t.Op);
        Assert.Equal("Chilli_Axe", t.Value);
    }

    [Fact]
    public void Source_Not()
    {
        var t = Single("source:!Chilli_Axe");
        Assert.Equal(FilterField.Source, t.Field);
        Assert.Equal(FilterOp.Not, t.Op);
        Assert.Equal("Chilli_Axe", t.Value);
    }

    [Fact]
    public void Source_In()
    {
        var t = Single("source:in[Chilli_Axe,Psilosx]");
        Assert.Equal(FilterField.Source, t.Field);
        Assert.Equal(FilterOp.In, t.Op);
        Assert.Equal(2, t.Values.Count);
        Assert.Contains("Chilli_Axe", t.Values);
        Assert.Contains("Psilosx", t.Values);
    }

    // ----------------------------------------------------------------
    //  Tag filters
    // ----------------------------------------------------------------

    [Fact]
    public void Tag_Exact()
    {
        var t = Single("tag:Retro");
        Assert.Equal(FilterField.Tag, t.Field);
        Assert.Equal(FilterOp.Eq, t.Op);
        Assert.Equal("Retro", t.Value);
    }

    [Fact]
    public void Tag_Not()
    {
        var t = Single("tag:!NSFW");
        Assert.Equal(FilterField.Tag, t.Field);
        Assert.Equal(FilterOp.Not, t.Op);
        Assert.Equal("NSFW", t.Value);
    }

    [Fact]
    public void Tag_In()
    {
        var t = Single("tag:in[Retro,Frame,Extended-Art]");
        Assert.Equal(FilterField.Tag, t.Field);
        Assert.Equal(FilterOp.In, t.Op);
        Assert.Equal(3, t.Values.Count);
        Assert.Contains("Retro", t.Values);
        Assert.Contains("Frame", t.Values);
        Assert.Contains("Extended-Art", t.Values);
    }

    // ----------------------------------------------------------------
    //  Name filters
    // ----------------------------------------------------------------

    [Fact]
    public void Name_Explicit()
    {
        var t = Single("name:Bolt");
        Assert.Equal(FilterField.Name, t.Field);
        Assert.Equal(FilterOp.Eq, t.Op);
        Assert.Equal("Bolt", t.Value);
    }

    [Fact]
    public void Name_Not()
    {
        var t = Single("name:!Token");
        Assert.Equal(FilterField.Name, t.Field);
        Assert.Equal(FilterOp.Not, t.Op);
        Assert.Equal("Token", t.Value);
    }

    // ----------------------------------------------------------------
    //  Structural tokens
    // ----------------------------------------------------------------

    [Fact]
    public void OrKeyword_ProducesOrToken()
    {
        var tokens = FilterParser.Parse("dpi:>800 OR source:Chilli_Axe");
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.Or, tokens[1].Kind);
    }

    [Fact]
    public void Pipe_ProducesOrToken()
    {
        var tokens = FilterParser.Parse("dpi:>800 | source:Chilli_Axe");
        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.Or, tokens[1].Kind);
    }

    [Fact]
    public void OpenParen_ProducesOpenParenToken()
    {
        var tokens = FilterParser.Parse("(");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.OpenParen, tokens[0].Kind);
    }

    [Fact]
    public void CloseParen_ProducesCloseParenToken()
    {
        var tokens = FilterParser.Parse(")");
        Assert.Single(tokens);
        Assert.Equal(TokenKind.CloseParen, tokens[0].Kind);
    }

    // ----------------------------------------------------------------
    //  DisplayText preservation
    // ----------------------------------------------------------------

    [Fact]
    public void DisplayText_PreservesOriginalRawText()
    {
        var t = Single("dpi:>800");
        Assert.Equal("dpi:>800", t.DisplayText);
    }

    [Fact]
    public void DisplayText_FreeTextPreserved()
    {
        var t = Single("Bolt");
        Assert.Equal("Bolt", t.DisplayText);
    }
}
