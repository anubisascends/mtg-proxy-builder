using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using Xunit;

namespace MTGProxyBuilder.Tests.Services
{
    public class RiftboundImportTests
    {
        [Fact]
        public void DeckSource_DetectsPiltoverArchive()
        {
            Assert.Equal(DeckSource.PiltoverArchive,
                DeckImportService.DetectSource("https://piltoverarchive.com/decks/view/abc-123"));
        }

        [Fact]
        public void RiftboundDeck_AllCards_IncludesAllSections()
        {
            var deck = new RiftboundDeck
            {
                Legend = new RiftboundDeckCard { Quantity = 1, Card = new RiftboundCard { Name = "Legend" } },
                Champions = new() { new RiftboundDeckCard { Quantity = 2, Card = new RiftboundCard { Name = "Champ" } } },
                Battlefields = new() { new RiftboundDeckCard { Quantity = 1, Card = new RiftboundCard { Name = "Field" } } },
                Runes = new() { new RiftboundDeckCard { Quantity = 1, Card = new RiftboundCard { Name = "Rune" } } },
                Maindeck = new() { new RiftboundDeckCard { Quantity = 3, Card = new RiftboundCard { Name = "Card1" } } },
                Sideboard = new(),
                Bench = new()
            };

            var all = deck.AllCards().ToList();
            Assert.Equal(5, all.Count);
            Assert.Equal(8, all.Sum(c => c.Quantity));
        }

        [Fact]
        public void RiftboundDeck_AllCards_EmptyDeck()
        {
            var deck = new RiftboundDeck();
            Assert.Empty(deck.AllCards());
        }

        [Fact]
        public void GetCardImageUrl_MatchesVariantId()
        {
            var deckCard = new RiftboundDeckCard
            {
                VariantId = "variant-2",
                Card = new RiftboundCard
                {
                    CardVariants = new()
                    {
                        new RiftboundCardVariant { Id = "variant-1", ImageUrl = "https://cdn/v1.webp" },
                        new RiftboundCardVariant { Id = "variant-2", ImageUrl = "https://cdn/v2.webp" }
                    }
                }
            };

            Assert.Equal("https://cdn/v2.webp", PiltoverArchiveService.GetCardImageUrl(deckCard));
        }

        [Fact]
        public void GetCardImageUrl_FallsBackToFirstVariant()
        {
            var deckCard = new RiftboundDeckCard
            {
                VariantId = "missing",
                Card = new RiftboundCard
                {
                    CardVariants = new()
                    {
                        new RiftboundCardVariant { Id = "variant-1", ImageUrl = "https://cdn/v1.webp" }
                    }
                }
            };

            Assert.Equal("https://cdn/v1.webp", PiltoverArchiveService.GetCardImageUrl(deckCard));
        }

        [Fact]
        public void GetCardImageUrl_ReturnsNull_WhenNoVariants()
        {
            var deckCard = new RiftboundDeckCard
            {
                Card = new RiftboundCard { CardVariants = new() }
            };

            Assert.Null(PiltoverArchiveService.GetCardImageUrl(deckCard));
        }
    }
}
