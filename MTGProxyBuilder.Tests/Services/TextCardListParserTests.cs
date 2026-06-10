using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class TextCardListParserTests
{
    [Fact]
    public void Parse_NameOnly()
    {
        var entries = TextCardListParser.Parse("Lightning Bolt");
        Assert.Single(entries);
        Assert.Equal("Lightning Bolt", entries[0].Name);
        Assert.Equal(1, entries[0].Quantity);
        Assert.Null(entries[0].SetCode);
        Assert.Null(entries[0].CollectorNumber);
    }

    [Fact]
    public void Parse_QtyAndName()
    {
        var entries = TextCardListParser.Parse("4 Lightning Bolt");
        Assert.Single(entries);
        Assert.Equal(4, entries[0].Quantity);
        Assert.Equal("Lightning Bolt", entries[0].Name);
    }

    [Fact]
    public void Parse_QtyNameSet()
    {
        var entries = TextCardListParser.Parse("2 Counterspell (MH2)");
        Assert.Single(entries);
        Assert.Equal(2, entries[0].Quantity);
        Assert.Equal("Counterspell", entries[0].Name);
        Assert.Equal("MH2", entries[0].SetCode);
        Assert.Null(entries[0].CollectorNumber);
    }

    [Fact]
    public void Parse_FullFormat()
    {
        var entries = TextCardListParser.Parse("4 Lightning Bolt (3ED) 152");
        Assert.Single(entries);
        Assert.Equal(4, entries[0].Quantity);
        Assert.Equal("Lightning Bolt", entries[0].Name);
        Assert.Equal("3ED", entries[0].SetCode);
        Assert.Equal("152", entries[0].CollectorNumber);
    }

    [Fact]
    public void Parse_MultipleLines()
    {
        var text = "4 Lightning Bolt\n2 Counterspell (MH2)\n1 Sol Ring";
        var entries = TextCardListParser.Parse(text);
        Assert.Equal(3, entries.Count);
        Assert.Equal("Lightning Bolt", entries[0].Name);
        Assert.Equal("Counterspell", entries[1].Name);
        Assert.Equal("Sol Ring", entries[2].Name);
    }

    [Fact]
    public void Parse_SkipsEmptyLines()
    {
        var text = "Lightning Bolt\n\n\nSol Ring\n";
        var entries = TextCardListParser.Parse(text);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Parse_SkipsComments()
    {
        var text = "// This is a comment\n# Another comment\nLightning Bolt";
        var entries = TextCardListParser.Parse(text);
        Assert.Single(entries);
        Assert.Equal("Lightning Bolt", entries[0].Name);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        Assert.Empty(TextCardListParser.Parse(""));
        Assert.Empty(TextCardListParser.Parse(null!));
    }

    [Fact]
    public void Parse_DefaultQuantityIsOne()
    {
        var entries = TextCardListParser.Parse("Sol Ring");
        Assert.Equal(1, entries[0].Quantity);
    }

    [Fact]
    public void Parse_CardNameWithNumbers()
    {
        var entries = TextCardListParser.Parse("2 Kozilek, the Great Distortion (OGW) 4");
        Assert.Single(entries);
        Assert.Equal("Kozilek, the Great Distortion", entries[0].Name);
        Assert.Equal("OGW", entries[0].SetCode);
        Assert.Equal("4", entries[0].CollectorNumber);
    }

    [Fact]
    public void Parse_CardNameWithApostrophe()
    {
        var entries = TextCardListParser.Parse("1 Thalia's Lieutenant");
        Assert.Single(entries);
        Assert.Equal("Thalia's Lieutenant", entries[0].Name);
    }

    [Fact]
    public void Parse_WindowsLineEndings()
    {
        var text = "Lightning Bolt\r\nCounterspell\r\nSol Ring";
        var entries = TextCardListParser.Parse(text);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void BuildScryfallQuery_NameOnly()
    {
        var entry = new CardListEntry { Name = "Lightning Bolt" };
        Assert.Equal("!\"Lightning Bolt\"", TextCardListParser.BuildScryfallQuery(entry));
    }

    [Fact]
    public void BuildScryfallQuery_WithSet()
    {
        var entry = new CardListEntry { Name = "Lightning Bolt", SetCode = "3ED" };
        Assert.Equal("!\"Lightning Bolt\" set:3ED", TextCardListParser.BuildScryfallQuery(entry));
    }

    [Fact]
    public void BuildScryfallQuery_WithSetAndNumber()
    {
        var entry = new CardListEntry { Name = "Lightning Bolt", SetCode = "3ED", CollectorNumber = "152" };
        Assert.Equal("!\"Lightning Bolt\" set:3ED number:152", TextCardListParser.BuildScryfallQuery(entry));
    }
}
