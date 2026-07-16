# Card Spacing (Horizontal & Vertical) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user set a horizontal and vertical spacing (mm) that inserts a white gap between cards only — margins, page edges, and per-card bleed are unaffected.

**Architecture:** Add `HorizontalSpacingMm`/`VerticalSpacingMm` to `PageLayout` plus `CellStrideXMm`/`CellStrideYMm` helpers. The drawn cell stays `card + 2×bleed`; spacing is added only to the *stride* between cell origins. Every render site (PDF, preview, SVG, editor canvas) multiplies the column/row index by the stride instead of the cell size. Auto-fit and auto-centering account for the `(N−1)` inter-card gaps.

**Tech Stack:** C# / .NET 10 (fallback net6), WPF, PdfSharp, SkiaSharp, Newtonsoft.Json, xUnit.

## Global Constraints

- Both spacing values default to `0`. Spacing = 0 MUST reproduce current output and current test numbers exactly (backward compatible; old `.mtgproj` files load unchanged).
- mm→pt conversion is `72f / 25.4f` (already a `MmToPt` const in each render file); the editor canvas uses its existing `MmToPx`.
- Spacing is measured **between bleed edges**: each card keeps its full bleed; spacing is pure gap between adjacent cells' outer bleed edges.
- Follow the existing inline grid-math pattern; do not restructure the render loops beyond the documented edits.
- Persistence is automatic via Newtonsoft serialization of `PageSettings`; do NOT add serializer code or an app-level default in this plan.

---

## File Structure

- `MTGProxyBuilder.Core/Models/PageLayout.cs` — **modify.** New properties, stride helpers, updated auto-fit + centering. (Task 1)
- `MTGProxyBuilder.Tests/Models/PageLayoutTests.cs` — **modify.** Add spacing tests. (Task 1)
- `MTGProxyBuilder.Core/Services/PdfGeneratorService.cs` — **modify.** Stride in `AddPage` + alignment PDF + color-bar refactor. (Task 2)
- `MTGProxyBuilder.Core/Services/SvgCutLineService.cs` — **modify.** Stride in `BuildSvg`. (Task 2)
- `MTGProxyBuilder.Tests/Services/SvgCutLineSpacingTests.cs` — **create.** SVG coordinate test. (Task 2)
- `MTGProxyBuilder.UI/Services/PreviewRenderer.cs` — **modify.** Stride in `RenderPage` + color-bar refactor. (Task 3)
- `MTGProxyBuilder.UI/Controls/GridEditorCanvas.cs` — **modify.** Stride in placement, hit-test, snap. (Task 3)
- `MTGProxyBuilder.UI/MainWindow.xaml` — **modify.** Two spacing input fields. (Task 4)

---

## Task 1: PageLayout model + math

**Files:**
- Modify: `MTGProxyBuilder.Core/Models/PageLayout.cs`
- Test: `MTGProxyBuilder.Tests/Models/PageLayoutTests.cs`

**Interfaces:**
- Produces:
  - `float PageLayout.HorizontalSpacingMm { get; set; }` (grid-affecting)
  - `float PageLayout.VerticalSpacingMm { get; set; }` (grid-affecting)
  - `float PageLayout.CellStrideXMm { get; }` = `CardWidthMm + 2*BleedWidthMm + HorizontalSpacingMm`
  - `float PageLayout.CellStrideYMm { get; }` = `CardHeightMm + 2*BleedWidthMm + VerticalSpacingMm`

- [ ] **Step 1: Write the failing tests**

Add these tests to the end of `PageLayoutTests.cs` (before the final closing `}`):

```csharp
    [Fact]
    public void CellStride_IncludesSpacing()
    {
        var layout = new PageLayout
        {
            CardWidthMm = 63,
            CardHeightMm = 88,
            BleedWidthMm = 3,
            HorizontalSpacingMm = 5,
            VerticalSpacingMm = 4
        };
        // 63 + 2*3 + 5 = 74 ; 88 + 2*3 + 4 = 98
        Assert.Equal(74f, layout.CellStrideXMm);
        Assert.Equal(98f, layout.CellStrideYMm);
    }

    [Fact]
    public void CellStride_ZeroSpacing_EqualsCardPlusBleed()
    {
        var layout = new PageLayout { CardWidthMm = 63, CardHeightMm = 88, BleedWidthMm = 3 };
        Assert.Equal(69f, layout.CellStrideXMm);
        Assert.Equal(94f, layout.CellStrideYMm);
    }

    [Fact]
    public void AutoCardsPerRow_SpacingReducesCount()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            CardWidthMm = 63,
            BleedWidthMm = 0,
            HorizontalSpacingMm = 30
        };
        // stride 93: 2 cards = 2*63 + 1*30 = 156 <= 210 ; 3 cards = 249 > 210 -> 2
        Assert.Equal(2, layout.AutoCardsPerRow);
    }

    [Fact]
    public void AutoCardsPerColumn_SpacingReducesCount()
    {
        var layout = new PageLayout
        {
            PageHeightMm = 297,
            CardHeightMm = 88,
            BleedWidthMm = 0,
            VerticalSpacingMm = 40
        };
        // stride 128: 2 rows = 2*88 + 1*40 = 216 <= 297 ; 3 rows = 344 > 297 -> 2
        Assert.Equal(2, layout.AutoCardsPerColumn);
    }

    [Fact]
    public void AutoCardsPerRow_ZeroSpacing_UnchangedFromBleedOnly()
    {
        var layout = new PageLayout { PageWidthMm = 210, CardWidthMm = 63, BleedWidthMm = 3 };
        // Regression: same as AutoCardsPerRow_WithBleed -> 3
        Assert.Equal(3, layout.AutoCardsPerRow);
    }

    [Fact]
    public void CenterGrid_AccountsForInterCardGaps()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            CardWidthMm = 60,
            CardHeightMm = 60,
            BleedWidthMm = 0,
            ColumnsOverride = 2,
            RowsOverride = 2,
            HorizontalSpacingMm = 10,
            VerticalSpacingMm = 10
        };
        layout.CenterGrid();

        // gridWidth = 2*60 + 1*10 = 130 ; hMargin = (210-130)/2 = 40
        Assert.Equal(40f, layout.MarginLeftMm, 1);
        Assert.Equal(40f, layout.MarginRightMm, 1);
        // gridHeight = 2*60 + 1*10 = 130 ; vMargin = (297-130)/2 = 83.5
        Assert.Equal(83.5f, layout.MarginTopMm, 1);
        Assert.Equal(83.5f, layout.MarginBottomMm, 1);
    }

    [Fact]
    public void PropertyChanged_FiresOnHorizontalSpacingChange()
    {
        var layout = new PageLayout();
        var changedProps = new List<string>();
        layout.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        layout.HorizontalSpacingMm = 5;

        Assert.Contains("HorizontalSpacingMm", changedProps);
        Assert.Contains("CardsPerRow", changedProps);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test MTGProxyBuilder.Tests/MTGProxyBuilder.Tests.csproj --filter "FullyQualifiedName~PageLayoutTests"`
Expected: FAIL — compile error, `HorizontalSpacingMm` / `CellStrideXMm` do not exist.

- [ ] **Step 3: Add the backing fields**

In `PageLayout.cs`, after the existing `private bool _isCentering;` field (line ~20), add:

```csharp
        private float _horizontalSpacingMm;
        private float _verticalSpacingMm;
```

- [ ] **Step 4: Add the spacing properties**

In `PageLayout.cs`, immediately after the `BleedWidthMm` property (ends ~line 44), add:

```csharp
        /// <summary>Horizontal gap (mm) inserted between adjacent cells' bleed edges. 0 = cards touch.</summary>
        public float HorizontalSpacingMm
        {
            get => _horizontalSpacingMm;
            set { _horizontalSpacingMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        /// <summary>Vertical gap (mm) inserted between adjacent cells' bleed edges. 0 = cards touch.</summary>
        public float VerticalSpacingMm
        {
            get => _verticalSpacingMm;
            set { _verticalSpacingMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }
```

- [ ] **Step 5: Add the stride helpers**

In `PageLayout.cs`, under the `// --- Computed properties ---` comment (line ~117), before `AutoCardsPerRow`, add:

```csharp
        /// <summary>Distance (mm) from one cell's left edge to the next: card + both bleeds + horizontal spacing.</summary>
        public float CellStrideXMm => CardWidthMm + 2 * BleedWidthMm + HorizontalSpacingMm;

        /// <summary>Distance (mm) from one cell's top edge to the next: card + both bleeds + vertical spacing.</summary>
        public float CellStrideYMm => CardHeightMm + 2 * BleedWidthMm + VerticalSpacingMm;
```

- [ ] **Step 6: Update auto-fit to account for gaps**

In `PageLayout.cs`, replace the `AutoCardsPerRow` and `AutoCardsPerColumn` getters:

```csharp
        /// <summary>Max columns that fit using the full page width (ignoring margins), accounting for inter-card spacing.</summary>
        public int AutoCardsPerRow
        {
            get
            {
                float stride = CellStrideXMm;
                return stride > 0 ? Math.Max(1, (int)((PageWidthMm + HorizontalSpacingMm) / stride)) : 0;
            }
        }

        /// <summary>Max rows that fit using the full page height (ignoring margins), accounting for inter-card spacing.</summary>
        public int AutoCardsPerColumn
        {
            get
            {
                float stride = CellStrideYMm;
                return stride > 0 ? Math.Max(1, (int)((PageHeightMm + VerticalSpacingMm) / stride)) : 0;
            }
        }
```

- [ ] **Step 7: Update centering to include gaps**

In `PageLayout.cs`, inside `CenterGrid()`, replace the two grid-size lines:

```csharp
            float gridWidth = cols * (CardWidthMm + 2 * BleedWidthMm);
            float gridHeight = rows * (CardHeightMm + 2 * BleedWidthMm);
```

with:

```csharp
            float gridWidth = cols * (CardWidthMm + 2 * BleedWidthMm) + Math.Max(0, cols - 1) * HorizontalSpacingMm;
            float gridHeight = rows * (CardHeightMm + 2 * BleedWidthMm) + Math.Max(0, rows - 1) * VerticalSpacingMm;
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test MTGProxyBuilder.Tests/MTGProxyBuilder.Tests.csproj --filter "FullyQualifiedName~PageLayoutTests"`
Expected: PASS — all `PageLayoutTests` (new + existing regression cases) green.

- [ ] **Step 9: Commit**

```bash
git add MTGProxyBuilder.Core/Models/PageLayout.cs MTGProxyBuilder.Tests/Models/PageLayoutTests.cs
git commit -m "feat: add card spacing to PageLayout model with stride helpers"
```

---

## Task 2: Core services — PDF generator + SVG cut lines

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/PdfGeneratorService.cs`
- Modify: `MTGProxyBuilder.Core/Services/SvgCutLineService.cs`
- Test: `MTGProxyBuilder.Tests/Services/SvgCutLineSpacingTests.cs` (create)

**Interfaces:**
- Consumes: `PageLayout.CellStrideXMm`, `PageLayout.CellStrideYMm` (Task 1).
- Produces: `DrawColorBars` in `PdfGeneratorService` now takes `(XGraphics gfx, float startX, float startY, float gridWidth, float gridHeight, float pageW, float pageH)` instead of `(… int cols, int rows, float cellW, float cellH …)`.

- [ ] **Step 1: Write the failing SVG test**

Create `MTGProxyBuilder.Tests/Services/SvgCutLineSpacingTests.cs`:

```csharp
using System.Text.RegularExpressions;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class SvgCutLineSpacingTests
{
    private const float MmToPt = 72f / 25.4f;

    [Fact]
    public async Task BuildSvg_HorizontalSpacing_ShiftsSecondColumnByStride()
    {
        var layout = new PageLayout
        {
            PageWidthMm = 210,
            PageHeightMm = 297,
            CardWidthMm = 63,
            CardHeightMm = 88,
            BleedWidthMm = 0,
            ColumnsOverride = 3,
            RowsOverride = 3,
            HorizontalSpacingMm = 10
        };
        var project = new ProjectModel
        {
            PageSettings = layout,
            Cards = new List<CardModel> { new(), new() } // two slots, row 0 col 0 and col 1
        };

        var dir = Path.Combine(Path.GetTempPath(), "mtg_svg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = await new SvgCutLineService().GenerateSvgAsync(project, dir, "test");
            Assert.NotEmpty(files);

            string svg = await File.ReadAllTextAsync(files[0]);
            var xs = Regex.Matches(svg, "<rect x=\"([0-9.]+)\"")
                          .Select(m => float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                          .ToList();
            Assert.True(xs.Count >= 2, "expected at least two card rects");

            float deltaX = xs[1] - xs[0];
            float expected = layout.CellStrideXMm * MmToPt; // (63 + 10) * MmToPt
            Assert.Equal(expected, deltaX, 1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **Step 2: Run the SVG test to verify it fails**

Run: `dotnet test MTGProxyBuilder.Tests/MTGProxyBuilder.Tests.csproj --filter "FullyQualifiedName~SvgCutLineSpacingTests"`
Expected: FAIL — `deltaX` equals `63*MmToPt` (no spacing applied yet), not `73*MmToPt`.

- [ ] **Step 3: Apply stride in SvgCutLineService.BuildSvg**

In `SvgCutLineService.cs`, in `BuildSvg`, after the `float cellH = cardHPt + 2 * bleedPt;` line, add:

```csharp
            float strideX = settings.CellStrideXMm * MmToPt;
            float strideY = settings.CellStrideYMm * MmToPt;
```

Then in the `for` loop, replace:

```csharp
                float cellX = startX + col * cellW;
                float cellY = startY + row * cellH;
```

with:

```csharp
                float cellX = startX + col * strideX;
                float cellY = startY + row * strideY;
```

- [ ] **Step 4: Run the SVG test to verify it passes**

Run: `dotnet test MTGProxyBuilder.Tests/MTGProxyBuilder.Tests.csproj --filter "FullyQualifiedName~SvgCutLineSpacingTests"`
Expected: PASS.

- [ ] **Step 5: Apply stride in PdfGeneratorService.AddPage**

In `PdfGeneratorService.cs` → `AddPage`, after `float cellH = cardHPt + 2 * bleedPt;` (line ~413) add:

```csharp
            float strideX = settings.CellStrideXMm * MmToPt;
            float strideY = settings.CellStrideYMm * MmToPt;
```

Then in **each of the four** per-card loops (Pass 1a cut guides, Pass 1b crop marks, Pass 2 card art, Pass 3 outlines), replace every occurrence of:

```csharp
                    float cellX = startX + col * cellW;
                    float cellY = startY + row * cellH;
```

with:

```csharp
                    float cellX = startX + col * strideX;
                    float cellY = startY + row * strideY;
```

(In Pass 2 the variables are declared with the same `float cellX = startX + col * cellW;` / `float cellY = startY + row * cellH;` shape — apply the identical replacement there too.)

- [ ] **Step 6: Refactor DrawColorBars signature (PdfGeneratorService)**

In `PdfGeneratorService.cs`, replace the `DrawColorBars` method header and its first four lines:

```csharp
        private void DrawColorBars(XGraphics gfx, float startX, float startY,
            int cols, int rows, float cellW, float cellH, float pageW, float pageH)
        {
            float gridRight = startX + cols * cellW;
            float gridBottom = startY + rows * cellH;
            float gridWidth = cols * cellW;
            float gridHeight = rows * cellH;
```

with:

```csharp
        private void DrawColorBars(XGraphics gfx, float startX, float startY,
            float gridWidth, float gridHeight, float pageW, float pageH)
        {
            float gridRight = startX + gridWidth;
            float gridBottom = startY + gridHeight;
```

(Leave the rest of the method body — `barThickness`, `fitsBottom`, etc. — unchanged.)

- [ ] **Step 7: Update the AddPage color-bar call site**

In `PdfGeneratorService.cs` → `AddPage`, Pass 5, replace:

```csharp
            if (printSettings.ShowColorBars)
            {
                int rows = cols > 0 ? perPage / cols : 0;
                DrawColorBars(gfx, startX, startY, cols, rows, cellW, cellH, pageWPt, pageHPt);
            }
```

with:

```csharp
            if (printSettings.ShowColorBars)
            {
                int rows = cols > 0 ? perPage / cols : 0;
                float gridWidth = cols > 0 ? (cols - 1) * strideX + cellW : 0;
                float gridHeight = rows > 0 ? (rows - 1) * strideY + cellH : 0;
                DrawColorBars(gfx, startX, startY, gridWidth, gridHeight, pageWPt, pageHPt);
            }
```

- [ ] **Step 8: Update GenerateAlignmentPdfAsync grid math**

In `PdfGeneratorService.cs` → `GenerateAlignmentPdfAsync`, replace:

```csharp
                    float gridW = cols * cellW;
                    float gridH = rows * cellH;
```

with:

```csharp
                    float strideX = settings.CellStrideXMm * MmToPt;
                    float strideY = settings.CellStrideYMm * MmToPt;
                    float gridW = cols > 0 ? (cols - 1) * strideX + cellW : 0;
                    float gridH = rows > 0 ? (rows - 1) * strideY + cellH : 0;
```

Then replace the calibration grid-size lines:

```csharp
                    float gridWidthMm = cols * (settings.CardWidthMm + 2 * settings.BleedWidthMm);
                    float gridHeightMm = rows * (settings.CardHeightMm + 2 * settings.BleedWidthMm);
```

with:

```csharp
                    float gridWidthMm = cols > 0
                        ? cols * (settings.CardWidthMm + 2 * settings.BleedWidthMm) + (cols - 1) * settings.HorizontalSpacingMm
                        : 0;
                    float gridHeightMm = rows > 0
                        ? rows * (settings.CardHeightMm + 2 * settings.BleedWidthMm) + (rows - 1) * settings.VerticalSpacingMm
                        : 0;
```

Then update the color-bar call in the same method, replacing:

```csharp
                        DrawColorBars(gfx, startX, startY, cols, rows, cellW, cellH, pageWPt, pageHPt);
```

with:

```csharp
                        DrawColorBars(gfx, startX, startY, gridW, gridH, pageWPt, pageHPt);
```

- [ ] **Step 9: Build Core + Tests to verify everything compiles and passes**

Run: `dotnet test MTGProxyBuilder.Tests/MTGProxyBuilder.Tests.csproj`
Expected: PASS — all tests green, no build errors in Core.

- [ ] **Step 10: Commit**

```bash
git add MTGProxyBuilder.Core/Services/PdfGeneratorService.cs MTGProxyBuilder.Core/Services/SvgCutLineService.cs MTGProxyBuilder.Tests/Services/SvgCutLineSpacingTests.cs
git commit -m "feat: apply card spacing to PDF generation and SVG cut lines"
```

---

## Task 3: UI renderers — preview + editor canvas

**Files:**
- Modify: `MTGProxyBuilder.UI/Services/PreviewRenderer.cs`
- Modify: `MTGProxyBuilder.UI/Controls/GridEditorCanvas.cs`

**Interfaces:**
- Consumes: `PageLayout.CellStrideXMm`, `PageLayout.CellStrideYMm` (Task 1).
- Note: `PreviewRenderer.DrawColorBars` is refactored to `(SKCanvas, float startX, float startY, float gridWidth, float gridHeight, float pageW, float pageH)`, mirroring the PDF change.

- [ ] **Step 1: Apply stride in PreviewRenderer.RenderPage**

In `PreviewRenderer.cs` → `RenderPage`, after `float cellH = cardHPt + 2 * bleedPt;` (line ~133) add:

```csharp
            float strideX = settings.CellStrideXMm * MmToPt;
            float strideY = settings.CellStrideYMm * MmToPt;
```

Then in **each of the four** per-card loops (cut guides, crop marks, card art, outlines), replace:

```csharp
                    float cellX = startX + col * cellW;
                    float cellY = startY + row * cellH;
```

with:

```csharp
                    float cellX = startX + col * strideX;
                    float cellY = startY + row * strideY;
```

(In the card-art pass the same two lines are declared without extra indentation — apply the identical replacement.)

- [ ] **Step 2: Refactor PreviewRenderer.DrawColorBars signature**

In `PreviewRenderer.cs`, replace the `DrawColorBars` header and first four lines:

```csharp
        private void DrawColorBars(SKCanvas canvas, float startX, float startY,
            int cols, int rows, float cellW, float cellH, float pageW, float pageH)
        {
            float gridRight = startX + cols * cellW;
            float gridBottom = startY + rows * cellH;
            float gridWidth = cols * cellW;
            float gridHeight = rows * cellH;
```

with:

```csharp
        private void DrawColorBars(SKCanvas canvas, float startX, float startY,
            float gridWidth, float gridHeight, float pageW, float pageH)
        {
            float gridRight = startX + gridWidth;
            float gridBottom = startY + gridHeight;
```

- [ ] **Step 3: Update the PreviewRenderer color-bar call site**

In `PreviewRenderer.cs` → `RenderPage`, Pass 5, replace:

```csharp
            if (printSettings.ShowColorBars)
            {
                int rows = cols > 0 ? perPage / cols : 0;
                DrawColorBars(canvas, startX, startY, cols, rows, cellW, cellH, pageWPt, pageHPt);
            }
```

with:

```csharp
            if (printSettings.ShowColorBars)
            {
                int rows = cols > 0 ? perPage / cols : 0;
                float gridWidth = cols > 0 ? (cols - 1) * strideX + cellW : 0;
                float gridHeight = rows > 0 ? (rows - 1) * strideY + cellH : 0;
                DrawColorBars(canvas, startX, startY, gridWidth, gridHeight, pageWPt, pageHPt);
            }
```

- [ ] **Step 4: Add stride fields to GridEditorCanvas**

In `GridEditorCanvas.cs`, replace the cached-geometry field declaration:

```csharp
        private float _pageW, _pageH, _cellW, _cellH, _marginL, _marginT;
```

with:

```csharp
        private float _pageW, _pageH, _cellW, _cellH, _marginL, _marginT, _strideX, _strideY;
```

- [ ] **Step 5: Compute and cache stride in the render method**

In `GridEditorCanvas.cs`, after the lines that compute `cellW`/`cellH` (`float cellH = (settings.CardHeightMm + 2 * settings.BleedWidthMm) * MmToPx;`), add:

```csharp
            float strideX = settings.CellStrideXMm * MmToPx;
            float strideY = settings.CellStrideYMm * MmToPx;
```

Then in the cache-assignment block, change:

```csharp
            _pageW = pageW; _pageH = pageH; _cellW = cellW; _cellH = cellH;
            _marginL = marginL; _marginT = marginT;
```

to:

```csharp
            _pageW = pageW; _pageH = pageH; _cellW = cellW; _cellH = cellH;
            _marginL = marginL; _marginT = marginT;
            _strideX = strideX; _strideY = strideY;
```

- [ ] **Step 6: Use stride for cell placement**

In `GridEditorCanvas.cs`, in the render nested `for` loops, replace:

```csharp
                        float x = marginL + c * cellW;
                        float y = pageTop + marginT + r * cellH;
```

with:

```csharp
                        float x = marginL + c * strideX;
                        float y = pageTop + marginT + r * strideY;
```

- [ ] **Step 7: Use stride in HitTestSlot**

In `GridEditorCanvas.cs`, replace the body of `HitTestSlot` from the bounds checks through the row/col computation:

```csharp
            if (localY < _marginT || localY >= _marginT + _rows * _cellH) return -1;
            if (localX < _marginL || localX >= _marginL + _cols * _cellW) return -1;
            int col = (int)((localX - _marginL) / _cellW);
            int row = (int)((localY - _marginT) / _cellH);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return -1;
```

with:

```csharp
            if (_strideX <= 0 || _strideY <= 0) return -1;
            float gridRight = _marginL + (_cols - 1) * _strideX + _cellW;
            float gridBottom = _marginT + (_rows - 1) * _strideY + _cellH;
            if (localY < _marginT || localY >= gridBottom) return -1;
            if (localX < _marginL || localX >= gridRight) return -1;
            int col = Math.Min((int)((localX - _marginL) / _strideX), _cols - 1);
            int row = Math.Min((int)((localY - _marginT) / _strideY), _rows - 1);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return -1;
```

- [ ] **Step 8: Use stride in SlotToPosition**

In `GridEditorCanvas.cs`, replace the `SlotToPosition` return line:

```csharp
            return (_marginL + (slotOnPage % _cols) * _cellW, pageTop + _marginT + (slotOnPage / _cols) * _cellH);
```

with:

```csharp
            return (_marginL + (slotOnPage % _cols) * _strideX, pageTop + _marginT + (slotOnPage / _cols) * _strideY);
```

- [ ] **Step 9: Build the UI project to verify it compiles**

Run: `dotnet build MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Manual verification (no automated test — WPF/bitmap rendering)**

Launch the app, open/create a project with several cards, and in the layout sidebar set H spacing and V spacing to a nonzero value (e.g. 8mm each) once Task 4 is done — OR temporarily set them in code. Confirm in the grid editor and Print Preview that: gaps appear between cards, each card still shows its full bleed, the grid re-centers, and dragging a card drops it into the correct slot. (This step is a placeholder if run before Task 4; the definitive manual check happens after Task 4.)

- [ ] **Step 11: Commit**

```bash
git add MTGProxyBuilder.UI/Services/PreviewRenderer.cs MTGProxyBuilder.UI/Controls/GridEditorCanvas.cs
git commit -m "feat: apply card spacing to preview renderer and grid editor canvas"
```

---

## Task 4: UI inputs — spacing fields in MainWindow

**Files:**
- Modify: `MTGProxyBuilder.UI/MainWindow.xaml`

**Interfaces:**
- Consumes: `PageLayout.HorizontalSpacingMm`, `PageLayout.VerticalSpacingMm` (Task 1) via `CurrentProject.PageSettings`.

- [ ] **Step 1: Add the spacing input row**

In `MainWindow.xaml`, in the CARD SIZE section, locate the Width/Height/Bleed grid that ends with the `Bleed (mm)` `StackPanel` and its closing `</Grid>` (the grid begins `<Grid Margin="0,0,0,4">` around line 1143). Immediately after that `</Grid>`, insert:

```xml
                                    <Grid Margin="0,4,0,0">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="8"/>
                                            <ColumnDefinition Width="*"/>
                                        </Grid.ColumnDefinitions>
                                        <StackPanel Grid.Column="0">
                                            <TextBlock Text="H spacing (mm)" Foreground="#999" FontSize="9"/>
                                            <TextBox Text="{Binding CurrentProject.PageSettings.HorizontalSpacingMm, UpdateSourceTrigger=LostFocus}" Padding="4" FontSize="11"/>
                                        </StackPanel>
                                        <StackPanel Grid.Column="2">
                                            <TextBlock Text="V spacing (mm)" Foreground="#999" FontSize="9"/>
                                            <TextBox Text="{Binding CurrentProject.PageSettings.VerticalSpacingMm, UpdateSourceTrigger=LostFocus}" Padding="4" FontSize="11"/>
                                        </StackPanel>
                                    </Grid>
```

- [ ] **Step 2: Build the UI project**

Run: `dotnet build MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj -c Debug`
Expected: Build succeeded, 0 errors (XAML binding compiles).

- [ ] **Step 3: Manual verification (end-to-end)**

Launch the app: `dotnet run --project MTGProxyBuilder.UI`. Create/open a project with 6+ cards. In the layout sidebar CARD SIZE section:
1. Set **H spacing** = 8, **V spacing** = 8 (tab/click out to commit on LostFocus).
2. Confirm the grid editor immediately shows white gaps between cards; each card keeps its bleed; the INFO block's Grid/×/page count updates and the grid stays centered.
3. Open **Print Preview** — confirm the same gaps render and cut guides/outlines sit correctly per card.
4. Set spacing back to 0 — confirm cards return to touching (identical to previous behavior).
5. **Export PDF** and open it — verify the gaps appear in the output.
6. Save the project, reopen it — confirm the spacing values persisted.

- [ ] **Step 4: Commit**

```bash
git add MTGProxyBuilder.UI/MainWindow.xaml
git commit -m "feat: add horizontal and vertical card spacing inputs to layout sidebar"
```

---

## Self-Review

**Spec coverage:**
- Data model (properties, stride helpers, auto-fit, centering) → Task 1. ✓
- Rendering across all five sites → PageLayout (Task 1); PdfGeneratorService + SvgCutLineService (Task 2); PreviewRenderer + GridEditorCanvas (Task 3). ✓
- Alignment PDF + color-bar grid extents → Task 2 (steps 6–8) and Task 3 (steps 2–3). ✓
- UI inputs → Task 4. ✓
- Persistence (automatic, default 0, backward compatible) → verified by Task 4 step 3.6 (reopen) and the spacing=0 regression assertions in Task 1. ✓
- Tests → Task 1 (PageLayout math) and Task 2 (deterministic SVG coordinates). WPF/PdfSharp/Skia rendering is verified by build + documented manual checks, as it is not unit-testable. ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code. The Task 3 step 10 "placeholder if run before Task 4" note refers to UI-state timing, not missing plan content — the definitive manual check is Task 4 step 3. ✓

**Type consistency:** `CellStrideXMm`/`CellStrideYMm`, `HorizontalSpacingMm`/`VerticalSpacingMm` used identically across Tasks 1–4. `DrawColorBars` refactored to the same `(…, float gridWidth, float gridHeight, …)` signature in both `PdfGeneratorService` (Task 2) and `PreviewRenderer` (Task 3), and all three call sites (AddPage, GenerateAlignmentPdfAsync, RenderPage) updated to match. ✓
