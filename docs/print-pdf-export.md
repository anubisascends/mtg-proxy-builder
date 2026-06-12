# Print & PDF Export

## Exporting PDF

1. Click "Export PDF" in the toolbar (or Ctrl+E)
2. Choose a save location
3. The PDF includes bleed-extended images, cutting guides, card outlines, overlay text, and registration marks (if enabled)

## Page Layout

- **Page size presets** — A4, A3, Letter, Legal, Tabloid with landscape toggle; custom dimensions in mm or inches
- **Card size presets** — built-in sizes for 25+ TCGs (see [Supported Card Games](#supported-card-games)) plus custom dimensions
- **Grid** — leave blank for auto-fit, or enter specific column/row counts
- **Auto-centering** — margins automatically adjust to center the card grid when dimensions change

## Print Modes

- **Duplex** — interleaved front/back pages with mirrored columns for double-sided printing
- **Fronts Only** — front faces only
- **Backs Only** — back faces only

## Bleed Extension

Edge pixels are stretched outward (not just image resize) for clean cutting. Bleed width is configurable per project.

## Cutting Guides

Thin crop marks extending from card edges to page edges, drawn behind card art so they never show through light-colored artwork.

## Card Outlines

Precision outlines showing the exact card shape with full control:

| Setting | Options |
|---------|---------|
| Enable/disable | Toggle on/off |
| Color | Visual color picker (30 presets + RGB sliders + hex input) |
| Alignment | Center (on the edge), Inside (inset), Outside (outset) |
| Corner radius | 0mm for sharp corners, 3mm default for standard MTG rounded corners |
| Outline type | Full (complete rounded rectangle) or Corners (corner marks with arcs) |
| Line type | Solid or Dashed |
| Corner length | Length of corner marks in mm (Corners mode) |
| Line weight | Thickness in points |

## Silhouette Cameo Support

Built-in print-and-cut workflow for Silhouette Cameo cutters:

- **Registration marks** — Cameo Type 1 three-mark system (filled square top-left, L-shapes top-right and bottom-left) drawn on front pages only to save ink
- **SVG cut line export** — generates SVG files alongside the PDF with rounded rectangle cut lines matching card positions and corner radius; one SVG per unique layout
- **Configurable mark dimensions** — length, thickness, and inset in inches with Silhouette defaults (0.35", 0.039", 0.394")
- **Automatic mode switching** — when registration marks are enabled, bleed extension, cutting guides, and card outlines are automatically suppressed

## Supported Card Games

| Game | Card Size (mm) |
|------|---------------|
| Magic: The Gathering | 63 x 88 |
| Pokemon TCG | 63 x 88 |
| Lorcana | 63 x 88 |
| Flesh and Blood | 63 x 88 |
| KeyForge | 63 x 88 |
| Star Wars: Unlimited | 63 x 88 |
| One Piece Card Game | 63 x 88 |
| Dragon Ball Super TCG | 63 x 88 |
| Digimon Card Game | 63 x 88 |
| Marvel Champions | 63 x 88 |
| Arkham Horror LCG | 63 x 88 |
| Riftbound | 63 x 88 |
| Altered TCG | 63 x 88 |
| Sorcery: Contested Realm | 63 x 88 |
| Grand Archive | 63 x 88 |
| Yu-Gi-Oh! | 59 x 86 |
| Cardfight!! Vanguard | 59 x 86 |
| Weiss Schwarz | 59 x 86 |
| Bushiroad Standard | 59 x 86 |
| Bridge Size | 57 x 89 |
| Mini American (board games) | 41 x 63 |
| Mini European (board games) | 44 x 68 |
| Tarot Size | 70 x 120 |
| Oversized MTG / Commander | 89 x 127 |
