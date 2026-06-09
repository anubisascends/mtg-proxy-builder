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

// ================================================================
//  EVALUATOR TESTS
// ================================================================

public class FilterEvaluatorTests
{
    // Test data
    private static readonly TileData HighDpiRetro  = new("Lightning Bolt (Full Art)", "Chilli_Axe", 1200, ["Retro", "Frame"]);
    private static readonly TileData LowDpiPlain   = new("Lightning Bolt", "MrTeferi", 300, []);
    private static readonly TileData ScryfallCard  = new("Lightning Bolt", "Scryfall", 0, []);

    private static bool Eval(string expr, TileData tile) =>
        FilterEvaluator.Evaluate(FilterParser.Parse(expr), tile);

    private static IReadOnlyList<FilterToken> NoTokens => [];

    // ----------------------------------------------------------------
    //  Empty filter
    // ----------------------------------------------------------------

    [Fact]
    public void EmptyTokens_MatchEverything()
    {
        Assert.True(FilterEvaluator.Evaluate(NoTokens, HighDpiRetro));
        Assert.True(FilterEvaluator.Evaluate(NoTokens, LowDpiPlain));
        Assert.True(FilterEvaluator.Evaluate(NoTokens, ScryfallCard));
    }

    // ----------------------------------------------------------------
    //  DPI comparisons
    // ----------------------------------------------------------------

    [Fact]
    public void DpiGt800_MatchesHighDpi()
    {
        Assert.True(Eval("dpi:>800", HighDpiRetro));
    }

    [Fact]
    public void DpiGt800_NoMatchLowDpi()
    {
        Assert.False(Eval("dpi:>800", LowDpiPlain));
    }

    [Fact]
    public void DpiGt800_NoMatchZeroDpi()
    {
        Assert.False(Eval("dpi:>800", ScryfallCard));
    }

    [Fact]
    public void DpiLt600_MatchesLowDpi()
    {
        Assert.True(Eval("dpi:<600", LowDpiPlain));
    }

    [Fact]
    public void DpiLt600_NoMatchHighDpi()
    {
        Assert.False(Eval("dpi:<600", HighDpiRetro));
    }

    // ----------------------------------------------------------------
    //  Source matching
    // ----------------------------------------------------------------

    [Fact]
    public void SourceEq_CaseInsensitive_Matches()
    {
        Assert.True(Eval("source:chilli_axe", HighDpiRetro));
    }

    [Fact]
    public void SourceEq_NoMatch()
    {
        Assert.False(Eval("source:Chilli_Axe", LowDpiPlain));
    }

    [Fact]
    public void SourceIn_MatchesAny()
    {
        Assert.True(Eval("source:in[Chilli_Axe,MrTeferi]", HighDpiRetro));
        Assert.True(Eval("source:in[Chilli_Axe,MrTeferi]", LowDpiPlain));
    }

    [Fact]
    public void SourceIn_NoMatch()
    {
        Assert.False(Eval("source:in[Chilli_Axe,MrTeferi]", ScryfallCard));
    }

    [Fact]
    public void SourceNot_Excludes()
    {
        Assert.False(Eval("source:!Chilli_Axe", HighDpiRetro));
        Assert.True(Eval("source:!Chilli_Axe", LowDpiPlain));
    }

    // ----------------------------------------------------------------
    //  Tag matching
    // ----------------------------------------------------------------

    [Fact]
    public void TagEq_Matches()
    {
        Assert.True(Eval("tag:Retro", HighDpiRetro));
    }

    [Fact]
    public void TagEq_NoMatch()
    {
        Assert.False(Eval("tag:Retro", LowDpiPlain));
    }

    [Fact]
    public void TagNot_ExcludesTagged()
    {
        Assert.False(Eval("tag:!Retro", HighDpiRetro));
    }

    [Fact]
    public void TagNot_PassesUntagged()
    {
        Assert.True(Eval("tag:!NSFW", HighDpiRetro));
        Assert.True(Eval("tag:!NSFW", LowDpiPlain));
    }

    // ----------------------------------------------------------------
    //  Name substring
    // ----------------------------------------------------------------

    [Fact]
    public void NameSubstring_Matches()
    {
        Assert.True(Eval("Bolt", HighDpiRetro));
        Assert.True(Eval("Bolt", LowDpiPlain));
    }

    [Fact]
    public void NameSubstring_CaseInsensitive()
    {
        Assert.True(Eval("bolt", HighDpiRetro));
    }

    [Fact]
    public void NameSubstring_NoMatch()
    {
        Assert.False(Eval("Counterspell", HighDpiRetro));
    }

    [Fact]
    public void NameNot_ExcludesMatch()
    {
        Assert.False(Eval("name:!Bolt", HighDpiRetro));
        Assert.True(Eval("name:!Counterspell", HighDpiRetro));
    }

    // ----------------------------------------------------------------
    //  Implicit AND
    // ----------------------------------------------------------------

    [Fact]
    public void ImplicitAnd_BothMustMatch()
    {
        // High DPI and Retro tag — only HighDpiRetro has both
        Assert.True(Eval("dpi:>800 tag:Retro", HighDpiRetro));
        Assert.False(Eval("dpi:>800 tag:Retro", LowDpiPlain));
    }

    [Fact]
    public void ImplicitAnd_FirstMatchSecondFails()
    {
        Assert.False(Eval("source:Chilli_Axe tag:NSFW", HighDpiRetro));
    }

    // ----------------------------------------------------------------
    //  Explicit OR
    // ----------------------------------------------------------------

    [Fact]
    public void ExplicitOr_EitherMatches()
    {
        // source:Chilli_Axe OR source:MrTeferi
        Assert.True(Eval("source:Chilli_Axe OR source:MrTeferi", HighDpiRetro));
        Assert.True(Eval("source:Chilli_Axe OR source:MrTeferi", LowDpiPlain));
        Assert.False(Eval("source:Chilli_Axe OR source:MrTeferi", ScryfallCard));
    }

    [Fact]
    public void PipeOr_WorksLikeOrKeyword()
    {
        Assert.True(Eval("source:Chilli_Axe | source:Scryfall", ScryfallCard));
    }

    // ----------------------------------------------------------------
    //  Parentheses grouping
    // ----------------------------------------------------------------

    [Fact]
    public void Parentheses_GroupingRespected()
    {
        // dpi:>800 OR (source:MrTeferi tag:Retro)
        // HighDpiRetro  -> dpi:>800 = true  -> true
        // LowDpiPlain   -> dpi:>800 = false, source:MrTeferi=true but tag:Retro=false -> false
        // ScryfallCard  -> dpi:>800 = false, source:MrTeferi=false -> false
        Assert.True(Eval("dpi:>800 OR (source:MrTeferi tag:Retro)", HighDpiRetro));
        Assert.False(Eval("dpi:>800 OR (source:MrTeferi tag:Retro)", LowDpiPlain));
        Assert.False(Eval("dpi:>800 OR (source:MrTeferi tag:Retro)", ScryfallCard));
    }

    [Fact]
    public void Parentheses_OrInsideParens()
    {
        // source:Chilli_Axe AND (tag:Retro OR tag:Frame)
        // HighDpiRetro has source=Chilli_Axe and tags Retro,Frame -> true
        Assert.True(Eval("source:Chilli_Axe (tag:Retro OR tag:Frame)", HighDpiRetro));
        // LowDpiPlain has source=MrTeferi -> false
        Assert.False(Eval("source:Chilli_Axe (tag:Retro OR tag:Frame)", LowDpiPlain));
    }
}

// ================================================================
//  PARSE SINGLE TESTS (values with spaces)
// ================================================================

public class FilterParseSingleTests
{
    [Fact]
    public void ParseSingle_TagWithSpaces_SingleToken()
    {
        var token = FilterParser.ParseSingle("tag:AI Art");
        Assert.Equal(TokenKind.Filter, token.Kind);
        Assert.Equal(FilterField.Tag, token.Field);
        Assert.Equal(FilterOp.Eq, token.Op);
        Assert.Equal("AI Art", token.Value);
    }

    [Fact]
    public void ParseSingle_TagWithMultipleSpaces()
    {
        var token = FilterParser.ParseSingle("tag:Non-Black Border");
        Assert.Equal(FilterField.Tag, token.Field);
        Assert.Equal("Non-Black Border", token.Value);
    }

    [Fact]
    public void ParseSingle_SourceWithSpaces()
    {
        var token = FilterParser.ParseSingle("source:Some Artist Name");
        Assert.Equal(FilterField.Source, token.Field);
        Assert.Equal(FilterOp.Eq, token.Op);
        Assert.Equal("Some Artist Name", token.Value);
    }

    [Fact]
    public void ParseSingle_TagNotWithSpaces()
    {
        var token = FilterParser.ParseSingle("tag:!AI Art");
        Assert.Equal(FilterField.Tag, token.Field);
        Assert.Equal(FilterOp.Not, token.Op);
        Assert.Equal("AI Art", token.Value);
    }

    [Fact]
    public void ParseSingle_FreeTextWithSpaces()
    {
        var token = FilterParser.ParseSingle("Lightning Bolt");
        Assert.Equal(FilterField.Name, token.Field);
        Assert.Equal(FilterOp.Eq, token.Op);
        Assert.Equal("Lightning Bolt", token.Value);
    }

    [Fact]
    public void ParseSingle_OR_Keyword()
    {
        var token = FilterParser.ParseSingle("OR");
        Assert.Equal(TokenKind.Or, token.Kind);
    }

    [Fact]
    public void ParseSingle_Parens()
    {
        var open = FilterParser.ParseSingle("(");
        Assert.Equal(TokenKind.OpenParen, open.Kind);
        var close = FilterParser.ParseSingle(")");
        Assert.Equal(TokenKind.CloseParen, close.Kind);
    }

    [Fact]
    public void ParseSingle_DpiOperator()
    {
        var token = FilterParser.ParseSingle("dpi:>=1200");
        Assert.Equal(FilterField.Dpi, token.Field);
        Assert.Equal(FilterOp.Gte, token.Op);
        Assert.Equal("1200", token.Value);
    }

    [Fact]
    public void ParseSingle_InWithSpacesInValues()
    {
        var token = FilterParser.ParseSingle("tag:in[AI Art,Extended-Art]");
        Assert.Equal(FilterOp.In, token.Op);
        Assert.Equal(2, token.Values.Count);
        Assert.Contains("AI Art", token.Values);
        Assert.Contains("Extended-Art", token.Values);
    }

    [Fact]
    public void ParseSingle_WhitespaceIsTrimmed()
    {
        var token = FilterParser.ParseSingle("  dpi:>800  ");
        Assert.Equal(FilterField.Dpi, token.Field);
        Assert.Equal(FilterOp.Gt, token.Op);
        Assert.Equal("800", token.Value);
    }
}

// ================================================================
//  EVALUATOR EDGE CASE TESTS
// ================================================================

public class FilterEvaluatorEdgeCaseTests
{
    private static readonly TileData TaggedCard = new("Lightning Bolt (f)", "WillieTanner", 800, new List<string> { "Extended-Art", "Frame" });
    private static readonly TileData AiArtCard = new("Lightning Bolt", "SomeSource", 600, new List<string> { "AI Art", "Misc" });
    private static readonly TileData NoTagsCard = new("Plains", "Scryfall", 0, new List<string>());

    private static bool Eval(string expression, TileData tile) =>
        FilterEvaluator.Evaluate(FilterParser.Parse(expression), tile);

    [Fact]
    public void TagWithSpaces_MatchesViaParseSingle()
    {
        // Simulate what happens when PillFilterBar commits "tag:AI Art" via ParseSingle
        var token = FilterParser.ParseSingle("tag:AI Art");
        var tokens = new List<FilterToken> { token };
        Assert.True(FilterEvaluator.Evaluate(tokens, AiArtCard));
        Assert.False(FilterEvaluator.Evaluate(tokens, TaggedCard));
    }

    [Fact]
    public void TagNot_WithSpaces_Excludes()
    {
        var token = FilterParser.ParseSingle("tag:!AI Art");
        var tokens = new List<FilterToken> { token };
        Assert.False(FilterEvaluator.Evaluate(tokens, AiArtCard));
        Assert.True(FilterEvaluator.Evaluate(tokens, TaggedCard));
    }

    [Fact]
    public void TagIn_WithSpacedValues()
    {
        var token = FilterParser.ParseSingle("tag:in[AI Art,Extended-Art]");
        var tokens = new List<FilterToken> { token };
        Assert.True(FilterEvaluator.Evaluate(tokens, AiArtCard));
        Assert.True(FilterEvaluator.Evaluate(tokens, TaggedCard));
        Assert.False(FilterEvaluator.Evaluate(tokens, NoTagsCard));
    }

    [Fact]
    public void ZeroDpi_DpiFilter_NoFalsePositive()
    {
        // Scryfall cards have DPI=0, dpi:>0 should not match
        Assert.False(Eval("dpi:>0", NoTagsCard));
        Assert.True(Eval("dpi:>0", TaggedCard));
    }

    [Fact]
    public void EmptyTags_TagFilter_NoMatch()
    {
        Assert.False(Eval("tag:Retro", NoTagsCard));
    }

    [Fact]
    public void EmptyTags_TagNot_AlwaysTrue()
    {
        // Card with no tags passes "not has tag X"
        Assert.True(Eval("tag:!NSFW", NoTagsCard));
    }

    [Fact]
    public void NameSearch_SubstringCaseInsensitive()
    {
        Assert.True(Eval("bolt", TaggedCard));
        Assert.True(Eval("BOLT", TaggedCard));
        Assert.True(Eval("lightning", TaggedCard));
    }

    [Fact]
    public void ComplexExpression_MultipleAndOr()
    {
        // (dpi:>700 tag:Frame) OR source:Scryfall
        Assert.True(Eval("(dpi:>700 tag:Frame) OR source:Scryfall", TaggedCard));
        Assert.True(Eval("(dpi:>700 tag:Frame) OR source:Scryfall", NoTagsCard));
        Assert.False(Eval("(dpi:>700 tag:Frame) OR source:Scryfall", AiArtCard));
    }
}
