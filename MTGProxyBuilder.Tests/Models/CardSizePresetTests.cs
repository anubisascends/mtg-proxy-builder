using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class CardSizePresetTests
{
    [Fact]
    public void BuiltInPresets_IsNotEmpty()
    {
        Assert.NotEmpty(CardSizePreset.BuiltInPresets);
    }

    [Fact]
    public void BuiltInPresets_ContainsMtg()
    {
        var mtg = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.Name == "Magic: The Gathering");
        Assert.NotNull(mtg);
        Assert.Equal(63f, mtg.WidthMm);
        Assert.Equal(88f, mtg.HeightMm);
    }

    [Fact]
    public void BuiltInPresets_ContainsYugioh()
    {
        var ygo = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.Name == "Yu-Gi-Oh!");
        Assert.NotNull(ygo);
        Assert.Equal(59f, ygo.WidthMm);
        Assert.Equal(86f, ygo.HeightMm);
    }

    [Fact]
    public void BuiltInPresets_ContainsPokemon()
    {
        var poke = CardSizePreset.BuiltInPresets.FirstOrDefault(p => p.Name == "Pokemon TCG");
        Assert.NotNull(poke);
        Assert.Equal(63f, poke.WidthMm);
        Assert.Equal(88f, poke.HeightMm);
    }

    [Fact]
    public void BuiltInPresets_AllHavePositiveDimensions()
    {
        foreach (var preset in CardSizePreset.BuiltInPresets)
        {
            Assert.True(preset.WidthMm > 0, $"{preset.Name} has zero/negative width");
            Assert.True(preset.HeightMm > 0, $"{preset.Name} has zero/negative height");
        }
    }

    [Fact]
    public void BuiltInPresets_AllHaveNames()
    {
        foreach (var preset in CardSizePreset.BuiltInPresets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Name), "Preset has empty name");
        }
    }

    [Fact]
    public void ToString_ContainsNameAndDimensions()
    {
        var preset = new CardSizePreset("Test Game", 50f, 70f);
        var str = preset.ToString();
        Assert.Contains("Test Game", str);
        Assert.Contains("50", str);
        Assert.Contains("70", str);
    }
}
