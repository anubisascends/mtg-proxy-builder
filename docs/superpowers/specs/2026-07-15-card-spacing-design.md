# Card Spacing (Horizontal & Vertical) — Design

**Date:** 2026-07-15
**Status:** Approved

## Goal

Let the user specify a horizontal and a vertical spacing, in millimeters, that
adds a gap **between cards only** — it does not change the page margins or the
distance from the grid to the sheet edges. Each card keeps its full bleed; the
spacing is pure white space inserted between adjacent cards' bleed edges.

## Semantics (confirmed with user)

Spacing is measured **between bleed edges** ("gap between bleed edges"):

- Each card's cell remains `card + 2×bleed` and is drawn exactly as today.
- Spacing is added only as the **stride** from one cell to the next.
- Example: 3mm bleed + 5mm horizontal spacing ⇒ 11mm between the two cards'
  trim (art) edges, and 5mm of white between their outer bleed edges.
- As spacing grows, fewer cards auto-fit per page (correct behavior).

Both values default to `0`, so existing projects and current output are
unchanged, and older `.mtgproj` files load with no visual difference.

## Current architecture (context)

`PageLayout` is the single source of truth for page/card/grid geometry. Cards
currently pack edge-to-edge: `cell = card + 2×bleed`, placed at
`cellX = startX + col×cellW`, `cellY = startY + row×cellH`. This grid math is
duplicated inline at five render sites:

1. `MTGProxyBuilder.Core/Services/PdfGeneratorService.cs` — `AddPage` (5 passes)
   and `GenerateAlignmentPdfAsync`.
2. `MTGProxyBuilder.UI/Services/PreviewRenderer.cs` — `RenderPage` + color bars.
3. `MTGProxyBuilder.Core/Services/SvgCutLineService.cs` — `BuildSvg`.
4. `MTGProxyBuilder.UI/Controls/GridEditorCanvas.cs` — cell placement,
   hit-testing, and drag-ghost positioning.
5. `MTGProxyBuilder.Core/Models/PageLayout.cs` — `AutoCardsPerRow/Column`
   (auto-fit) and `CenterGrid` (auto-centering).

## Design

### 1. Data model — `PageLayout.cs`

Two new serializable properties, defaulting to `0`:

```csharp
private float _horizontalSpacingMm;   // gap between adjacent cells, left↔right
private float _verticalSpacingMm;     // gap between adjacent cells, top↕bottom
```

Both are **grid-affecting**: their setters call `OnGridAffectingChange()` (like
bleed and card size), so changing them re-runs auto-centering and refreshes
computed properties.

Add two helper properties so the offset math lives in one place rather than
being re-derived at each render site:

```csharp
public float CellStrideXMm => CardWidthMm + 2 * BleedWidthMm + HorizontalSpacingMm;
public float CellStrideYMm => CardHeightMm + 2 * BleedWidthMm + VerticalSpacingMm;
```

**Auto-fit** — N cells with N−1 gaps must fit the page. Solving
`N×cellW + (N−1)×spacing ≤ PageSize` gives:

```
AutoCardsPerRow    = max(1, floor((PageWidthMm  + HorizontalSpacingMm) / CellStrideXMm))
AutoCardsPerColumn = max(1, floor((PageHeightMm + VerticalSpacingMm)   / CellStrideYMm))
```

(guard against a zero/negative stride, as the existing code guards `cellW > 0`).

**Centering** — `CenterGrid` computes grid extent including the inter-card gaps:

```
gridWidth  = cols × (CardWidthMm  + 2×BleedWidthMm) + (cols − 1) × HorizontalSpacingMm
gridHeight = rows × (CardHeightMm + 2×BleedWidthMm) + (rows − 1) × VerticalSpacingMm
```

The remaining centering logic (split leftover space in half, clamp at 0, round
to 1 decimal) is unchanged.

### 2. Rendering — the five sites

The transformation is uniform: wherever a site currently computes
`cellX = startX + col × cellW`, it becomes `startX + col × strideX`, and
`cellY = startY + row × cellH` becomes `startY + row × strideY`, where
`strideX = CellStrideXMm × MmToPt` (or `× MmToPx` in the canvas) and likewise
`strideY`.

`cellW`/`cellH` remain `card + 2×bleed` and continue to size the card image,
bleed-extended image, outline, crop marks, and cut guides. Because every
per-card decoration is computed **relative to `cellX`/`cellY`**, they all follow
the new positions automatically — no per-decoration changes needed.

Site-specific notes:

- **PdfGeneratorService.AddPage** — the five passes (cut guides, crop marks,
  card art, outlines, color bars) each use the new stride for `cellX/cellY`.
- **PdfGeneratorService.GenerateAlignmentPdfAsync** — the grid-boundary
  rectangle, corner/center targets, and the `gridWidthMm`/`gridHeightMm` fed to
  `CalibrationTransform.Compute` must include `(cols−1)×hSpacing` /
  `(rows−1)×vSpacing`.
- **PreviewRenderer.RenderPage** — same passes as the PDF generator.
- **Color bars** (both renderers) — `gridRight`/`gridBottom`/`gridWidth`/
  `gridHeight` add the inter-card gaps so the bars sit just past the true grid
  extent.
- **SvgCutLineService.BuildSvg** — cell origin uses stride; the per-card rect
  offset (`+ bleedPt`) is unchanged.
- **GridEditorCanvas** — cache the stride separately from the drawn cell size.
  Keep `_cellW`/`_cellH` as the drawn cell size (`card + 2×bleed`) for the card
  visual, but position cells and do hit-testing/`GetSlotAt`/drag-ghost snapping
  using the stride. Column index from a point: `(localX − marginL) / strideX`;
  the in-bounds test uses `marginL + cols×strideX − hSpacing` as the right edge.

### 3. UI — `MainWindow.xaml`

Add an "H spacing (mm)" / "V spacing (mm)" input row directly beneath the
existing Width / Height / Bleed row in the **CARD SIZE** section, bound to
`CurrentProject.PageSettings.HorizontalSpacingMm` and `.VerticalSpacingMm` with
`UpdateSourceTrigger=LostFocus` (matching the neighboring inputs). The INFO
block already displays auto columns/rows and cards-per-page, so it reflects the
change live.

### 4. Persistence

Automatic. `PageSettings` is serialized whole by Newtonsoft.Json inside the
`.mtgproj` archive, so the two new public properties round-trip with no
serializer changes. Files saved before this feature deserialize with the
properties at their `0` default.

No app-level default is added in this iteration (spacing is per-project and
defaults to 0). A `DefaultHorizontalSpacingMm`/`DefaultVerticalSpacingMm` on
`AppSettings` could be added later if a global default is wanted, mirroring
`DefaultBleedMm`.

### 5. Tests — `PageLayoutTests.cs`

- Spacing = 0 reproduces current cell origins, auto-fit, and margins exactly
  (regression guard).
- Non-zero spacing shifts the Nth cell origin by `N × stride`.
- Auto-fit drops a column/row once spacing pushes the grid past the page.
- `CenterGrid` accounts for `(cols−1)`/`(rows−1)` gaps when computing margins.

## Decisions (not open questions)

- **Per-project only, no global default.** Spacing lives in the project model
  and defaults to 0.
- **Spacing re-centers the grid.** Like bleed, changing spacing is a
  grid-affecting change that re-runs auto-centering; it does not fight manually
  edited margins any differently than bleed/card-size changes do today.

## Out of scope

- Global/app-level default spacing.
- Independent per-gap spacing (only uniform horizontal and uniform vertical).
- Any change to how bleed itself is generated or how cut lines are drawn.
