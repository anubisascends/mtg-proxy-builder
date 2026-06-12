using System.Text.Json.Serialization;

namespace MTGProxyBuilder.Core.Models
{
    public class RiftboundDeck
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("authorName")]
        public string AuthorName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("legend")]
        public RiftboundDeckCard? Legend { get; set; }

        [JsonPropertyName("champions")]
        public List<RiftboundDeckCard> Champions { get; set; } = new();

        [JsonPropertyName("battlefields")]
        public List<RiftboundDeckCard> Battlefields { get; set; } = new();

        [JsonPropertyName("runes")]
        public List<RiftboundDeckCard> Runes { get; set; } = new();

        [JsonPropertyName("maindeck")]
        public List<RiftboundDeckCard> Maindeck { get; set; } = new();

        [JsonPropertyName("sideboard")]
        public List<RiftboundDeckCard> Sideboard { get; set; } = new();

        [JsonPropertyName("bench")]
        public List<RiftboundDeckCard> Bench { get; set; } = new();

        /// <summary>Returns all deck cards across all sections.</summary>
        public IEnumerable<RiftboundDeckCard> AllCards()
        {
            if (Legend != null) yield return Legend;
            foreach (var c in Champions) yield return c;
            foreach (var c in Battlefields) yield return c;
            foreach (var c in Runes) yield return c;
            foreach (var c in Maindeck) yield return c;
            foreach (var c in Sideboard) yield return c;
            foreach (var c in Bench) yield return c;
        }
    }

    public class RiftboundDeckCard
    {
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;

        [JsonPropertyName("card")]
        public RiftboundCard Card { get; set; } = new();

        [JsonPropertyName("variantId")]
        public string VariantId { get; set; } = string.Empty;
    }

    public class RiftboundCard
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("super")]
        public string? Super { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("energy")]
        public int? Energy { get; set; }

        [JsonPropertyName("might")]
        public int? Might { get; set; }

        [JsonPropertyName("power")]
        public int? Power { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("cardVariants")]
        public List<RiftboundCardVariant> CardVariants { get; set; } = new();

        [JsonPropertyName("cardColors")]
        public List<RiftboundCardColor> CardColors { get; set; } = new();
    }

    public class RiftboundCardVariant
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("variantNumber")]
        public string VariantNumber { get; set; } = string.Empty;

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = string.Empty;

        [JsonPropertyName("rarity")]
        public string Rarity { get; set; } = string.Empty;

        [JsonPropertyName("flavorText")]
        public string? FlavorText { get; set; }

        [JsonPropertyName("artist")]
        public string? Artist { get; set; }
    }

    public class RiftboundCardColor
    {
        [JsonPropertyName("color")]
        public RiftboundColor Color { get; set; } = new();
    }

    public class RiftboundColor
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; } = string.Empty;
    }
}
