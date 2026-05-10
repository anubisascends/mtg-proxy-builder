using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class CardModelTests
{
    [Fact]
    public void NewCard_HasUniqueId()
    {
        var card1 = new CardModel();
        var card2 = new CardModel();
        Assert.NotEqual(card1.CardId, card2.CardId);
    }

    [Fact]
    public void NewCard_HasDefaultValues()
    {
        var card = new CardModel();
        Assert.Equal(string.Empty, card.Name);
        Assert.Equal(string.Empty, card.ArtworkPath);
        Assert.Null(card.BackArtworkPath);
        Assert.Null(card.ScryfallId);
        Assert.Equal(1, card.Quantity);
        Assert.False(card.IncludeBack);
        Assert.Equal(string.Empty, card.ManaCost);
        Assert.Equal(0f, card.CMC);
    }

    [Theory]
    [InlineData("Creature — Human Wizard", "Creature")]
    [InlineData("Legendary Creature — Dragon", "Creature")]
    [InlineData("Instant", "Instant")]
    [InlineData("Sorcery", "Sorcery")]
    [InlineData("Artifact — Equipment", "Artifact")]
    [InlineData("Enchantment — Aura", "Enchantment")]
    [InlineData("Land", "Land")]
    [InlineData("Legendary Planeswalker — Jace", "Planeswalker")]
    [InlineData("", "")]
    public void PrimaryType_ExtractsCorrectly(string typeLine, string expected)
    {
        var card = new CardModel { TypeLine = typeLine };
        Assert.Equal(expected, card.PrimaryType);
    }

    [Fact]
    public void PropertyChanged_FiresOnNameChange()
    {
        var card = new CardModel();
        string? changedProp = null;
        card.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        card.Name = "Lightning Bolt";
        Assert.Equal("Name", changedProp);
    }

    [Fact]
    public void PropertyChanged_FiresOnQuantityChange()
    {
        var card = new CardModel();
        string? changedProp = null;
        card.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        card.Quantity = 4;
        Assert.Equal("Quantity", changedProp);
    }

    [Fact]
    public void DateAdded_DefaultsToNow()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var card = new CardModel();
        var after = DateTime.Now.AddSeconds(1);

        Assert.InRange(card.DateAdded, before, after);
    }
}
