using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class DeckImportTests
{
    // --- DeckImportService.DetectSource ---

    [Theory]
    [InlineData("https://www.moxfield.com/decks/abc123", DeckSource.Moxfield)]
    [InlineData("https://moxfield.com/decks/abc123", DeckSource.Moxfield)]
    [InlineData("http://moxfield.com/decks/xyz", DeckSource.Moxfield)]
    public void DetectSource_Moxfield(string url, DeckSource expected)
    {
        Assert.Equal(expected, DeckImportService.DetectSource(url));
    }

    [Theory]
    [InlineData("https://archidekt.com/decks/12345/my-deck", DeckSource.Archidekt)]
    [InlineData("https://www.archidekt.com/decks/99999", DeckSource.Archidekt)]
    [InlineData("http://archidekt.com/decks/1", DeckSource.Archidekt)]
    public void DetectSource_Archidekt(string url, DeckSource expected)
    {
        Assert.Equal(expected, DeckImportService.DetectSource(url));
    }

    [Theory]
    [InlineData("https://google.com")]
    [InlineData("https://scryfall.com/card/m21/1/foo")]
    [InlineData("just some text")]
    [InlineData("")]
    public void DetectSource_Unknown(string url)
    {
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource(url));
    }

    [Fact]
    public void DetectSource_NullOrWhitespace_ReturnsUnknown()
    {
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource(""));
        Assert.Equal(DeckSource.Unknown, DeckImportService.DetectSource("   "));
    }

    // --- MoxfieldService.ParseDeckId ---

    [Theory]
    [InlineData("https://www.moxfield.com/decks/oEWXWHM5eEGMmopExLWRCA", "oEWXWHM5eEGMmopExLWRCA")]
    [InlineData("https://moxfield.com/decks/abc123", "abc123")]
    [InlineData("https://moxfield.com/decks/abc123/primer", "abc123")]
    public void MoxfieldParseDeckId_ValidUrls(string url, string expectedId)
    {
        Assert.Equal(expectedId, MoxfieldService.ParseDeckId(url));
    }

    [Fact]
    public void MoxfieldParseDeckId_DirectId()
    {
        Assert.Equal("oEWXWHM5eEGMmopExLWRCA", MoxfieldService.ParseDeckId("oEWXWHM5eEGMmopExLWRCA"));
    }

    [Theory]
    [InlineData("https://moxfield.com/")]
    [InlineData("https://moxfield.com/decks")]
    [InlineData("https://moxfield.com/decks/")]
    public void MoxfieldParseDeckId_InvalidUrls_ReturnsNull(string url)
    {
        var result = MoxfieldService.ParseDeckId(url);
        // Either null or empty string for edge cases
        Assert.True(result == null || result == "", $"Expected null/empty but got '{result}'");
    }

    // --- ArchidektService.ParseDeckId ---

    [Theory]
    [InlineData("https://archidekt.com/decks/12345/my-deck-name", "12345")]
    [InlineData("https://archidekt.com/decks/99999", "99999")]
    [InlineData("https://www.archidekt.com/decks/1/test", "1")]
    public void ArchidektParseDeckId_ValidUrls(string url, string expectedId)
    {
        Assert.Equal(expectedId, ArchidektService.ParseDeckId(url));
    }

    [Fact]
    public void ArchidektParseDeckId_DirectNumericId()
    {
        Assert.Equal("12345", ArchidektService.ParseDeckId("12345"));
    }

    [Theory]
    [InlineData("https://archidekt.com/")]
    [InlineData("https://archidekt.com/decks")]
    [InlineData("not a url")]
    public void ArchidektParseDeckId_InvalidUrls_ReturnsNull(string url)
    {
        Assert.Null(ArchidektService.ParseDeckId(url));
    }
}
