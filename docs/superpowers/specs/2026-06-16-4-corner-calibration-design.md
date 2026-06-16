# 4-Corner Printer Calibration Design

## Problem

The current printer calibration only corrects uniform X/Y translation on back pages. Some printers introduce slight rotation during the duplex pass, causing the front and back to be well-aligned at one corner but misaligned at the opposite corner. A single X/Y offset cannot fix this.

## Solution

Replace the single X/Y offset with 4 per-corner offsets (TL, TR, BL, BR). From these measurements, compute a best-fit affine transform (translation + rotation) that corrects both uniform shift and angular tilt. Apply via PdfSharp's TranslateTransform + RotateTransform.

## Data Model

### PrinterProfile changes

Replace `OffsetXMm` / `OffsetYMm` with 8 per-corner fields:

```
OffsetTLXMm, OffsetTLYMm  (Top-Left)
OffsetTRXMm, OffsetTRYMm  (Top-Right)
OffsetBLXMm, OffsetBLYMm  (Bottom-Left)
OffsetBRXMm, OffsetBRYMm  (Bottom-Right)
```

Keep `OffsetXMm` / `OffsetYMm` as serialized properties for backward compatibility. On load, if only the old fields are non-zero and all corner fields are zero, copy the old values to all 4 corners (uniform offset).

### Computed values (not stored)

From the 4 corner offsets, compute at apply-time:

- **Translation X** = average of all 4 X offsets: `(TLX + TRX + BLX + BRX) / 4`
- **Translation Y** = average of all 4 Y offsets: `(TLY + TRY + BLY + BRY) / 4`
- **Rotation angle** = derived from the systematic angular difference:
  - Horizontal tilt: `atan2((TRY + BRY)/2 - (TLY + BLY)/2, gridWidth)`
  - Vertical tilt: `atan2((TLX + TRX)/2 - (BLX + BRX)/2, gridHeight)`
  - Combined angle: average of horizontal and vertical estimates

## Shared Helper

Create a static method in a helper class (or on PrinterProfile itself):

```
CalibrationTransform ComputeCalibration(PrinterProfile profile, float gridWidthMm, float gridHeightMm)
```

Returns a record:
```
record CalibrationTransform(float TranslateXPt, float TranslateYPt, float RotationDegrees)
```

This is called by PdfGeneratorService, PreviewRenderer, and the Settings dialog (for the summary display). The grid dimensions come from PageLayout (cols * cellW, rows * cellH converted to mm).

## Transform Application

Applied to back pages in this order:

1. `TranslateTransform(pageCenterX, pageCenterY)` — move origin to page center
2. `RotateTransform(angleDegrees)` — rotate around center
3. `TranslateTransform(-pageCenterX, -pageCenterY)` — restore origin
4. `TranslateTransform(translateXPt, translateYPt)` — apply translation

Applied in 3 locations:
- `PdfGeneratorService.AddPage` (PDF export back pages)
- `PdfGeneratorService.GenerateAlignmentPdfAsync` (calibration test back page)
- `PreviewRenderer.RenderPage` (print preview back pages)

Each location replaces the current simple `TranslateTransform(offsetX, offsetY)` call.

## Settings Dialog UI

Replace the two offset TextBoxes with a 4-corner grid:

```
Back-Page Offset (measure at each corner target)

          X (mm)    Y (mm)
  TL:    [______]  [______]
  TR:    [______]  [______]
  BL:    [______]  [______]
  BR:    [______]  [______]
```

Below the inputs, a read-only computed summary:
> "Computed: translation X +0.3mm, Y -0.2mm, rotation 0.12 deg"

This updates live as the user types values.

## Sidebar Display

The PRINT section offset display changes from:
> "Offset: X +0.3mm, Y -0.2mm"

To:
> "Offset: X +0.3mm, Y -0.2mm, rot 0.12 deg"

When rotation is 0.00, it shows just "Offset: X +0.3mm, Y -0.2mm" (no rotation text).

## Calibration Test PDF

No layout changes needed — the existing 4-corner + center targets with mm rulers are ideal for measuring per-corner offsets.

Text updates:
- Front page instructions: "Measure the offset between solid and dashed targets at each corner (TL, TR, BL, BR). Enter all 4 offsets in Settings > Printer Calibration."
- Back page info line: "Applied: translation X=0.30mm, Y=-0.20mm, rotation=0.12deg"

## Backward Compatibility

- Legacy profiles with only `OffsetXMm`/`OffsetYMm` auto-migrate on load: copy the uniform offset to all 4 corners
- Legacy profiles with all corner fields at 0 and old fields non-zero trigger migration
- After migration, the old fields are ignored (corner fields take precedence)
- Saved profiles always write the corner fields; old fields written as 0

## Files Changed

| File | Change |
|------|--------|
| `MTGProxyBuilder.Core/Models/PrinterProfile.cs` | Add 8 corner offset fields, keep old fields, add migration logic |
| `MTGProxyBuilder.Core/Models/CalibrationTransform.cs` | New: record + ComputeCalibration static method |
| `MTGProxyBuilder.Core/Services/PdfGeneratorService.cs` | Replace TranslateTransform with rotation+translation in AddPage and GenerateAlignmentPdfAsync, update test page text |
| `MTGProxyBuilder.UI/Services/PreviewRenderer.cs` | Same transform change in RenderPage |
| `MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml` | Replace 2 offset fields with 4-corner grid + computed summary |
| `MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml.cs` | Update save/load for 8 fields, add live summary computation |
| `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs` | Update SelectedPrinterOffsetDisplay, update ExportPdf offset passing |
