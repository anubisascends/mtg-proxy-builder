namespace MTGProxyBuilder.Core.Models
{
    public class CardSizePreset
    {
        public string Name { get; }
        public float WidthMm { get; }
        public float HeightMm { get; }

        public CardSizePreset(string name, float widthMm, float heightMm)
        {
            Name = name;
            WidthMm = widthMm;
            HeightMm = heightMm;
        }

        public override string ToString() => $"{Name}  ({WidthMm} x {HeightMm} mm)";

        /// <summary>
        /// Built-in presets for popular trading card games and formats.
        /// Width = horizontal, Height = vertical (portrait orientation).
        /// </summary>
        public static readonly List<CardSizePreset> BuiltInPresets = new()
        {
            // Standard / Poker size (63 x 88 mm) — most common
            new("Magic: The Gathering",        63f, 88f),
            new("Pokemon TCG",                 63f, 88f),
            new("Lorcana",                     63f, 88f),
            new("Flesh and Blood",             63f, 88f),
            new("KeyForge",                    63f, 88f),
            new("Star Wars: Unlimited",        63f, 88f),
            new("One Piece Card Game",         63f, 88f),
            new("Dragon Ball Super TCG",       63f, 88f),
            new("Digimon Card Game",           63f, 88f),
            new("Marvel Champions",            63f, 88f),
            new("Arkham Horror LCG",           63f, 88f),
            new("Riftbound",                   63f, 88f),
            new("Altered TCG",                 63f, 88f),
            new("Sorcery: Contested Realm",    63f, 88f),
            new("Grand Archive",               63f, 88f),
            new("Standard / Poker Size",       63f, 88f),

            // Japanese size (59 x 86 mm)
            new("Yu-Gi-Oh!",                   59f, 86f),
            new("Cardfight!! Vanguard",        59f, 86f),
            new("Weiss Schwarz",               59f, 86f),
            new("Bushiroad Standard",          59f, 86f),
            new("Japanese Size",               59f, 86f),

            // Bridge size (57 x 89 mm)
            new("Bridge Size",                 57f, 89f),

            // Mini / Small cards
            new("Mini American (board games)", 41f, 63f),
            new("Mini European (board games)", 44f, 68f),

            // Large format
            new("Tarot Size",                  70f, 120f),
            new("Oversized MTG / Commander",   89f, 127f),
        };
    }
}
