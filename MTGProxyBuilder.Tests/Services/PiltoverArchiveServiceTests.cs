using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.Core.Models;
using Xunit;

namespace MTGProxyBuilder.Tests.Services
{
    public class PiltoverArchiveServiceTests
    {
        [Theory]
        [InlineData("https://piltoverarchive.com/decks/view/0741d662-e31b-4999-b1f8-96d89d085423",
                     "0741d662-e31b-4999-b1f8-96d89d085423")]
        [InlineData("https://piltoverarchive.com/decks/view/abc-123/",
                     "abc-123")]
        [InlineData("https://piltoverarchive.com/decks/view/abc-123?tab=overview",
                     "abc-123")]
        public void ParseDeckId_ExtractsId(string url, string expected)
        {
            Assert.Equal(expected, PiltoverArchiveService.ParseDeckId(url));
        }

        [Theory]
        [InlineData("https://moxfield.com/decks/abc")]
        [InlineData("not a url")]
        [InlineData("")]
        public void ParseDeckId_ReturnsNull_ForInvalidUrls(string url)
        {
            Assert.Null(PiltoverArchiveService.ParseDeckId(url));
        }

        [Fact]
        public void ExtractDeckJson_ParsesRscPayload()
        {
            // Mimics the real RSC format: prop passed as ,{\"deck\":{...}}
            string html = "<html><body>" +
                "<script>self.__next_f.push([1,\"[\\\"$\\\",\\\"$L9d\\\",null,{\\\"deck\\\":{\\\"id\\\":\\\"test-id\\\",\\\"name\\\":\\\"Test Deck\\\",\\\"authorName\\\":\\\"tester\\\",\\\"description\\\":\\\"\\\",\\\"legend\\\":null,\\\"champions\\\":[],\\\"battlefields\\\":[],\\\"runes\\\":[],\\\"maindeck\\\":[{\\\"quantity\\\":3,\\\"variantId\\\":\\\"v1\\\",\\\"card\\\":{\\\"id\\\":\\\"c1\\\",\\\"name\\\":\\\"Test Card\\\",\\\"type\\\":\\\"Unit\\\",\\\"super\\\":null,\\\"description\\\":\\\"Does stuff\\\",\\\"energy\\\":2,\\\"might\\\":1,\\\"power\\\":1,\\\"tags\\\":null,\\\"cardVariants\\\":[{\\\"id\\\":\\\"v1\\\",\\\"variantNumber\\\":\\\"OGN-001\\\",\\\"imageUrl\\\":\\\"https://cdn.piltoverarchive.com/cards/OGN-001.webp\\\",\\\"rarity\\\":\\\"Common\\\",\\\"flavorText\\\":null,\\\"artist\\\":null}],\\\"cardColors\\\":[]}}],\\\"sideboard\\\":[],\\\"bench\\\":[]}}]\"])</script>" +
                "</body></html>";

            var deck = PiltoverArchiveService.ExtractDeckFromHtml(html);

            Assert.NotNull(deck);
            Assert.Equal("test-id", deck!.Id);
            Assert.Equal("Test Deck", deck.Name);
            Assert.Single(deck.Maindeck);
            Assert.Equal("Test Card", deck.Maindeck[0].Card.Name);
            Assert.Equal(3, deck.Maindeck[0].Quantity);
            Assert.Equal("OGN-001", deck.Maindeck[0].Card.CardVariants[0].VariantNumber);
        }

        [Fact]
        public void ExtractDeckJson_ReturnsNull_WhenNoDeckData()
        {
            string html = "<html><body><p>No deck here</p></body></html>";
            Assert.Null(PiltoverArchiveService.ExtractDeckFromHtml(html));
        }

        [Fact]
        public void GetCardImageUrl_ReturnsVariantUrl()
        {
            var card = new RiftboundDeckCard
            {
                VariantId = "v1",
                Card = new RiftboundCard
                {
                    CardVariants = new()
                    {
                        new RiftboundCardVariant
                        {
                            Id = "v1",
                            ImageUrl = "https://cdn.piltoverarchive.com/cards/OGN-042.webp"
                        }
                    }
                }
            };

            Assert.Equal("https://cdn.piltoverarchive.com/cards/OGN-042.webp",
                PiltoverArchiveService.GetCardImageUrl(card));
        }
    }
}
