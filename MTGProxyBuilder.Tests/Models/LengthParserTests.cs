using MTGProxyBuilder.Core;

namespace MTGProxyBuilder.Tests.Models;

public class LengthParserTests
{
    [Theory]
    [InlineData("70", 70f)]
    [InlineData("70mm", 70f)]
    [InlineData(" 70 MM ", 70f)]
    [InlineData("7cm", 70f)]
    [InlineData("2.75in", 69.85f)]
    [InlineData("2.75 inches", 69.85f)]
    [InlineData("2.75\"", 69.85f)]
    [InlineData(".5in", 12.7f)]
    public void TryParseMm_ConvertsUnits(string text, float expected)
    {
        Assert.True(LengthParser.TryParseMm(text, out float mm));
        Assert.Equal(expected, mm, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("70 ft")]
    [InlineData("2.75in mm")]
    public void TryParseMm_RejectsInvalid(string? text)
    {
        Assert.False(LengthParser.TryParseMm(text, out _));
    }
}
