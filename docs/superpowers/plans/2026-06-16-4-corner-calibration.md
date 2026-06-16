# 4-Corner Printer Calibration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single X/Y printer offset with 4 per-corner offsets that compute a best-fit rotation + translation correction for duplex back pages.

**Architecture:** The `PrinterProfile` model gains 8 corner offset fields (TL/TR/BL/BR × X/Y). A new `CalibrationTransform` record with a static `Compute` method derives translation + rotation from the 4 corners. PdfGeneratorService and PreviewRenderer apply the transform via TranslateTransform + RotateTransform around the page center. The Settings dialog gets a 4-corner input grid replacing the 2-field layout.

**Tech Stack:** C# / .NET 10 / WPF, PdfSharp (XGraphics transforms), SkiaSharp (SKCanvas transforms)

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `MTGProxyBuilder.Core/Models/CalibrationTransform.cs` | Record + static Compute method |
| Modify | `MTGProxyBuilder.Core/Models/PrinterProfile.cs` | Add 8 corner fields, backward compat |
| Modify | `MTGProxyBuilder.Core/Services/PdfGeneratorService.cs` | Apply rotation+translation to back pages |
| Modify | `MTGProxyBuilder.UI/Services/PreviewRenderer.cs` | Same transform for preview |
| Modify | `MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml` | 4-corner offset input grid |
| Modify | `MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml.cs` | Save/load 8 fields, summary display |
| Modify | `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs` | Update offset display + passing |
| Create | `MTGProxyBuilder.Tests/Models/CalibrationTransformTests.cs` | Unit tests for the math |
| Modify | `MTGProxyBuilder.Tests/Models/PrinterProfileTests.cs` | Tests for new fields + migration |

---

### Task 1: CalibrationTransform — The Math

**Files:**
- Create: `MTGProxyBuilder.Core/Models/CalibrationTransform.cs`
- Create: `MTGProxyBuilder.Tests/Models/CalibrationTransformTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using MTGProxyBuilder.Core.Models;
using Xunit;

namespace MTGProxyBuilder.Tests.Models;

public class CalibrationTransformTests
{
    [Fact]
    public void ZeroOffsets_ProducesIdentityTransform()
    {
        var profile = new PrinterProfile();
        var result = CalibrationTransform.Compute(profile, 200, 280);
        Assert.Equal(0, result.TranslateXPt);
        Assert.Equal(0, result.TranslateYPt);
        Assert.Equal(0, result.RotationDegrees);
    }

    [Fact]
    public void UniformOffset_ProducesTranslationOnly()
    {
        var profile = new PrinterProfile
        {
            OffsetTLXMm = 1f, OffsetTLYMm = -0.5f,
            OffsetTRXMm = 1f, OffsetTRYMm = -0.5f,
            OffsetBLXMm = 1f, OffsetBLYMm = -0.5f,
            OffsetBRXMm = 1f, OffsetBRYMm = -0.5f,
        };
        var result = CalibrationTransform.Compute(profile, 200, 280);

        float expectedX = 1f * (72f / 25.4f);
        float expectedY = -0.5f * (72f / 25.4f);
        Assert.Equal(expectedX, result.TranslateXPt, 0.01f);
        Assert.Equal(expectedY, result.TranslateYPt, 0.01f);
        Assert.Equal(0, result.RotationDegrees, 0.001f);
    }

    [Fact]
    public void RotatedOffset_ProducesNonZeroRotation()
    {
        // Right side shifted down relative to left = clockwise rotation
        var profile = new PrinterProfile
        {
            OffsetTLXMm = 0, OffsetTLYMm = 0,
            OffsetTRXMm = 0, OffsetTRYMm = 1f,
            OffsetBLXMm = 0, OffsetBLYMm = 0,
            OffsetBRXMm = 0, OffsetBRYMm = 1f,
        };
        var result = CalibrationTransform.Compute(profile, 200, 280);

        Assert.True(result.RotationDegrees > 0, "Should detect clockwise rotation");
        Assert.Equal(0.25f, result.TranslateYPt, 1f); // average Y ~0.25mm
    }

    [Fact]
    public void HasCorrection_FalseForZero()
    {
        var result = CalibrationTransform.Compute(new PrinterProfile(), 200, 280);
        Assert.False(result.HasCorrection);
    }

    [Fact]
    public void HasCorrection_TrueForNonZero()
    {
        var profile = new PrinterProfile { OffsetTLXMm = 0.5f };
        var result = CalibrationTransform.Compute(profile, 200, 280);
        Assert.True(result.HasCorrection);
    }

    [Fact]
    public void LegacyMigration_CopiesUniformOffset()
    {
        var profile = new PrinterProfile
        {
            OffsetXMm = 1.5f,
            OffsetYMm = -0.3f
        };
        profile.MigrateLegacyOffsets();

        Assert.Equal(1.5f, profile.OffsetTLXMm);
        Assert.Equal(1.5f, profile.OffsetTRXMm);
        Assert.Equal(1.5f, profile.OffsetBLXMm);
        Assert.Equal(1.5f, profile.OffsetBRXMm);
        Assert.Equal(-0.3f, profile.OffsetTLYMm);
        Assert.Equal(-0.3f, profile.OffsetTRYMm);
        Assert.Equal(-0.3f, profile.OffsetBLYMm);
        Assert.Equal(-0.3f, profile.OffsetBRYMm);
    }

    [Fact]
    public void LegacyMigration_DoesNotOverwriteCornerValues()
    {
        var profile = new PrinterProfile
        {
            OffsetXMm = 1.5f,
            OffsetYMm = -0.3f,
            OffsetTLXMm = 0.5f // corner already set
        };
        profile.MigrateLegacyOffsets();

        Assert.Equal(0.5f, profile.OffsetTLXMm); // not overwritten
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test MTGProxyBuilder.Tests --filter "CalibrationTransform" -v q`
Expected: FAIL — types don't exist

- [ ] **Step 3: Create CalibrationTransform.cs**

```csharp
namespace MTGProxyBuilder.Core.Models
{
    /// <summary>
    /// Computed calibration correction derived from 4-corner offset measurements.
    /// Translation is the average offset; rotation is the angular tilt.
    /// </summary>
    public record CalibrationTransform(
        float TranslateXPt,
        float TranslateYPt,
        float RotationDegrees)
    {
        private const float MmToPt = 72f / 25.4f;

        public bool HasCorrection =>
            Math.Abs(TranslateXPt) > 0.001f ||
            Math.Abs(TranslateYPt) > 0.001f ||
            Math.Abs(RotationDegrees) > 0.0001f;

        /// <summary>
        /// Compute a best-fit translation + rotation from 4-corner offset measurements.
        /// </summary>
        /// <param name="profile">Printer profile with corner offsets in mm.</param>
        /// <param name="gridWidthMm">Width of the card grid in mm (cols * cellW).</param>
        /// <param name="gridHeightMm">Height of the card grid in mm (rows * cellH).</param>
        public static CalibrationTransform Compute(PrinterProfile profile, float gridWidthMm, float gridHeightMm)
        {
            float tlx = profile.OffsetTLXMm, tly = profile.OffsetTLYMm;
            float trx = profile.OffsetTRXMm, try_ = profile.OffsetTRYMm;
            float blx = profile.OffsetBLXMm, bly = profile.OffsetBLYMm;
            float brx = profile.OffsetBRXMm, bry = profile.OffsetBRYMm;

            // Translation = average of all 4 corners
            float avgXMm = (tlx + trx + blx + brx) / 4f;
            float avgYMm = (tly + try_ + bly + bry) / 4f;

            // Rotation from horizontal tilt: how much the right side is shifted
            // vertically relative to the left side, across the grid width
            float rightAvgY = (try_ + bry) / 2f;
            float leftAvgY = (tly + bly) / 2f;
            float horizAngleRad = gridWidthMm > 0
                ? (float)Math.Atan2(rightAvgY - leftAvgY, gridWidthMm)
                : 0;

            // Rotation from vertical tilt: how much the top is shifted
            // horizontally relative to the bottom, across the grid height
            float topAvgX = (tlx + trx) / 2f;
            float bottomAvgX = (blx + brx) / 2f;
            float vertAngleRad = gridHeightMm > 0
                ? (float)Math.Atan2(topAvgX - bottomAvgX, gridHeightMm)
                : 0;

            // Average the two rotation estimates for best fit
            float angleRad = (horizAngleRad + vertAngleRad) / 2f;
            float angleDeg = angleRad * (180f / (float)Math.PI);

            return new CalibrationTransform(
                avgXMm * MmToPt,
                avgYMm * MmToPt,
                angleDeg);
        }
    }
}
```

- [ ] **Step 4: Update PrinterProfile with corner fields and migration**

In `MTGProxyBuilder.Core/Models/PrinterProfile.cs`, add the 8 corner fields and migration method:

```csharp
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    public class PrinterProfile
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "Default";

        // Legacy fields (kept for backward compatibility with saved profiles)
        [JsonProperty("offsetXMm")]
        public float OffsetXMm { get; set; }

        [JsonProperty("offsetYMm")]
        public float OffsetYMm { get; set; }

        // 4-corner offset fields
        [JsonProperty("offsetTLXMm")]
        public float OffsetTLXMm { get; set; }

        [JsonProperty("offsetTLYMm")]
        public float OffsetTLYMm { get; set; }

        [JsonProperty("offsetTRXMm")]
        public float OffsetTRXMm { get; set; }

        [JsonProperty("offsetTRYMm")]
        public float OffsetTRYMm { get; set; }

        [JsonProperty("offsetBLXMm")]
        public float OffsetBLXMm { get; set; }

        [JsonProperty("offsetBLYMm")]
        public float OffsetBLYMm { get; set; }

        [JsonProperty("offsetBRXMm")]
        public float OffsetBRXMm { get; set; }

        [JsonProperty("offsetBRYMm")]
        public float OffsetBRYMm { get; set; }

        /// <summary>
        /// Migrates legacy OffsetXMm/OffsetYMm to all 4 corners if corner fields are all zero.
        /// Call after deserialization to handle old profile format.
        /// </summary>
        public void MigrateLegacyOffsets()
        {
            bool allCornersZero = OffsetTLXMm == 0 && OffsetTLYMm == 0
                && OffsetTRXMm == 0 && OffsetTRYMm == 0
                && OffsetBLXMm == 0 && OffsetBLYMm == 0
                && OffsetBRXMm == 0 && OffsetBRYMm == 0;

            if (allCornersZero && (OffsetXMm != 0 || OffsetYMm != 0))
            {
                OffsetTLXMm = OffsetTRXMm = OffsetBLXMm = OffsetBRXMm = OffsetXMm;
                OffsetTLYMm = OffsetTRYMm = OffsetBLYMm = OffsetBRYMm = OffsetYMm;
            }
        }

        public override string ToString() => Name;
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test MTGProxyBuilder.Tests --filter "CalibrationTransform|PrinterProfile" -v q`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add MTGProxyBuilder.Core/Models/CalibrationTransform.cs MTGProxyBuilder.Core/Models/PrinterProfile.cs MTGProxyBuilder.Tests/Models/CalibrationTransformTests.cs
git commit -m "feat: add CalibrationTransform with 4-corner offset math and legacy migration"
```

---

### Task 2: Apply Transform in PDF and Preview Rendering

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/PdfGeneratorService.cs`
- Modify: `MTGProxyBuilder.UI/Services/PreviewRenderer.cs`

- [ ] **Step 1: Update PdfGeneratorService.GeneratePdfAsync signature**

Change the method signature from taking raw floats to taking a `CalibrationTransform`:

Replace line 13:
```csharp
float backOffsetXMm = 0, float backOffsetYMm = 0)
```
With:
```csharp
CalibrationTransform? backCalibration = null)
```

Replace lines 25-26 (the offset conversion) with:
```csharp
var cal = backCalibration ?? new CalibrationTransform(0, 0, 0);
```

Replace lines 60 and 73 (the AddPage calls for back pages) — change `backOffsetXPt, backOffsetYPt` to `cal`.

- [ ] **Step 2: Update AddPage to accept CalibrationTransform**

Change the AddPage signature from:
```csharp
float backOffsetXPt = 0, float backOffsetYPt = 0)
```
To:
```csharp
CalibrationTransform? calibration = null)
```

Replace the transform block (lines 391-392):
```csharp
if (!front && (backOffsetXPt != 0 || backOffsetYPt != 0))
    gfx.TranslateTransform(backOffsetXPt, backOffsetYPt);
```
With:
```csharp
if (!front && calibration != null && calibration.HasCorrection)
{
    float pageCenterX = pageWPt / 2;
    float pageCenterY = pageHPt / 2;
    // Rotate around page center, then translate
    gfx.TranslateTransform(pageCenterX, pageCenterY);
    gfx.RotateTransform(calibration.RotationDegrees);
    gfx.TranslateTransform(-pageCenterX, -pageCenterY);
    gfx.TranslateTransform(calibration.TranslateXPt, calibration.TranslateYPt);
}
```

- [ ] **Step 3: Update GenerateAlignmentPdfAsync back page transform**

Replace line 203:
```csharp
gfx.TranslateTransform(offsetXMm * MmToPt, offsetYMm * MmToPt);
```
With:
```csharp
var cal = CalibrationTransform.Compute(profile, gridWMm, gridHMm);
if (cal.HasCorrection)
{
    float pageCenterX = pageWPt / 2;
    float pageCenterY = pageHPt / 2;
    gfx.TranslateTransform(pageCenterX, pageCenterY);
    gfx.RotateTransform(cal.RotationDegrees);
    gfx.TranslateTransform(-pageCenterX, -pageCenterY);
    gfx.TranslateTransform(cal.TranslateXPt, cal.TranslateYPt);
}
```

Update the method signature to accept `PrinterProfile profile` instead of `float offsetXMm, float offsetYMm`. The grid dimensions in mm are: `gridWMm = cols * (settings.CardWidthMm + 2 * settings.BleedWidthMm)`, `gridHMm = rows * (settings.CardHeightMm + 2 * settings.BleedWidthMm)`.

Update the back page info text to show the computed calibration:
```csharp
gfx.DrawString($"Applied: translation X={cal.TranslateXPt / MmToPt:F2}mm, Y={cal.TranslateYPt / MmToPt:F2}mm, rotation={cal.RotationDegrees:F3}deg", ...);
```

Also update the front page instructions text:
```
"Measure the offset between solid and dashed targets at each corner (TL, TR, BL, BR). Enter all 4 offsets in Settings > Printer Calibration."
```

- [ ] **Step 4: Update PreviewRenderer with same transform**

In `PreviewRenderer.RenderAllPagesAsync`, change `backOffsetXMm`/`backOffsetYMm` parameters to `CalibrationTransform? backCalibration = null`.

In `RenderPage`, change parameters from `float backOffsetXPt, float backOffsetYPt` to `CalibrationTransform? calibration = null`.

Replace the translate block (lines 112-113):
```csharp
if (!front && (backOffsetXPt != 0 || backOffsetYPt != 0))
    canvas.Translate(backOffsetXPt, backOffsetYPt);
```
With:
```csharp
if (!front && calibration != null && calibration.HasCorrection)
{
    float pageCenterX = pageWPt / 2;
    float pageCenterY = pageHPt / 2;
    canvas.Translate(pageCenterX, pageCenterY);
    canvas.RotateRadians(calibration.RotationDegrees * (float)(Math.PI / 180));
    canvas.Translate(-pageCenterX, -pageCenterY);
    canvas.Translate(calibration.TranslateXPt, calibration.TranslateYPt);
}
```

Note: SkiaSharp uses `RotateRadians` or `RotateDegrees` — use `RotateDegrees` for consistency:
```csharp
canvas.RotateDegrees(calibration.RotationDegrees);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build`
Expected: Build will fail because callers (MainViewModel, SettingsDialog, PrintPreviewDialog) still pass the old parameters. That's fine — we fix them in Tasks 3-4.

- [ ] **Step 6: Commit**

```bash
git add MTGProxyBuilder.Core/Services/PdfGeneratorService.cs MTGProxyBuilder.UI/Services/PreviewRenderer.cs
git commit -m "feat: apply rotation+translation calibration transform to back pages"
```

---

### Task 3: Update Callers — MainViewModel, PrintPreviewDialog

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs`
- Modify: `MTGProxyBuilder.UI/Dialogs/PrintPreviewDialog.xaml.cs`

- [ ] **Step 1: Add helper to MainViewModel for getting calibration**

Add a private helper that looks up the profile and computes the transform:

```csharp
private CalibrationTransform? GetCurrentCalibration()
{
    var printerName = _currentProject.PrinterProfileName;
    if (string.IsNullOrEmpty(printerName)) return null;

    var profile = _appSettings.Settings.PrinterProfiles
        .FirstOrDefault(p => p.Name == printerName);
    if (profile == null) return null;

    var settings = _currentProject.PageSettings;
    float cellWMm = settings.CardWidthMm + 2 * settings.BleedWidthMm;
    float cellHMm = settings.CardHeightMm + 2 * settings.BleedWidthMm;
    float gridWMm = settings.CardsPerRow * cellWMm;
    int rows = settings.CardsPerRow > 0 ? settings.CardsPerPage / settings.CardsPerRow : 0;
    float gridHMm = rows * cellHMm;

    return CalibrationTransform.Compute(profile, gridWMm, gridHMm);
}
```

- [ ] **Step 2: Update ExportPdf**

Replace the offset lookup block in `ExportPdf()` (lines ~1958-1971):

```csharp
var calibration = GetCurrentCalibration();
bool success = await _pdfGeneratorService.GeneratePdfAsync(
    _currentProject, dialog.FileName, calibration);
```

- [ ] **Step 3: Update PreviewPdf**

Replace the offset lookup block in `PreviewPdf()` (lines ~1900-1912):

```csharp
var calibration = GetCurrentCalibration();
var renderer = new PreviewRenderer();
var pages = await renderer.RenderAllPagesAsync(_currentProject, calibration);
```

- [ ] **Step 4: Update SelectedPrinterOffsetDisplay**

Replace the display property to show rotation when non-zero:

```csharp
public string SelectedPrinterOffsetDisplay
{
    get
    {
        var profile = _appSettings.Settings.PrinterProfiles
            .FirstOrDefault(p => p.Name == _selectedPrinterProfileName);
        if (profile == null) return "No offset applied";

        var cal = GetCurrentCalibration();
        if (cal == null || !cal.HasCorrection) return "No offset applied";

        float xMm = cal.TranslateXPt / (72f / 25.4f);
        float yMm = cal.TranslateYPt / (72f / 25.4f);

        if (Math.Abs(cal.RotationDegrees) > 0.001f)
            return $"Offset: X {xMm:+0.0;-0.0;0}mm, Y {yMm:+0.0;-0.0;0}mm, rot {cal.RotationDegrees:+0.00;-0.00;0}deg";
        return $"Offset: X {xMm:+0.0;-0.0;0}mm, Y {yMm:+0.0;-0.0;0}mm";
    }
}
```

- [ ] **Step 5: Update PrintPreviewDialog**

In `PrintPreviewDialog.xaml.cs`, update both `OnPrintClick` and `OnExportPdfClick` to use `CalibrationTransform` instead of raw offsets. Replace the offset lookup blocks with the same pattern — look up profile, compute grid dimensions, call `CalibrationTransform.Compute()`, pass to the service.

- [ ] **Step 6: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/MainViewModel.cs MTGProxyBuilder.UI/Dialogs/PrintPreviewDialog.xaml.cs
git commit -m "feat: update all callers to use CalibrationTransform"
```

---

### Task 4: Settings Dialog — 4-Corner Input Grid

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml`
- Modify: `MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml.cs`

- [ ] **Step 1: Replace offset XAML**

Replace the 2-field offset section (lines ~410-436 in SettingsDialog.xaml) with a 4-corner grid:

```xml
<!-- Offset -->
<Border Style="{StaticResource SectionCard}">
    <StackPanel>
        <TextBlock Text="Back-Page Offset" Style="{StaticResource SectionHeader}"/>
        <TextBlock Text="Measure offset at each corner target on the calibration test page. Enter the X/Y displacement in mm (+ right/down)."
                   Foreground="#999" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,10"/>

        <!-- Header row -->
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="40"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="1" Text="X (mm)" Foreground="#888" FontSize="9" HorizontalAlignment="Center"/>
            <TextBlock Grid.Column="3" Text="Y (mm)" Foreground="#888" FontSize="9" HorizontalAlignment="Center"/>
        </Grid>

        <!-- TL row -->
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="40"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="TL:" Foreground="#CCC" FontSize="11" VerticalAlignment="Center"/>
            <TextBox x:Name="OffsetTLXBox" Grid.Column="1" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
            <TextBox x:Name="OffsetTLYBox" Grid.Column="3" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
        </Grid>

        <!-- TR row -->
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="40"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="TR:" Foreground="#CCC" FontSize="11" VerticalAlignment="Center"/>
            <TextBox x:Name="OffsetTRXBox" Grid.Column="1" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
            <TextBox x:Name="OffsetTRYBox" Grid.Column="3" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
        </Grid>

        <!-- BL row -->
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="40"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="BL:" Foreground="#CCC" FontSize="11" VerticalAlignment="Center"/>
            <TextBox x:Name="OffsetBLXBox" Grid.Column="1" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
            <TextBox x:Name="OffsetBLYBox" Grid.Column="3" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
        </Grid>

        <!-- BR row -->
        <Grid Margin="0,0,0,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="40"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="8"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="BR:" Foreground="#CCC" FontSize="11" VerticalAlignment="Center"/>
            <TextBox x:Name="OffsetBRXBox" Grid.Column="1" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
            <TextBox x:Name="OffsetBRYBox" Grid.Column="3" Padding="4,3" FontSize="11"
                     Background="#3E3E42" Foreground="White" BorderBrush="#555" CaretBrush="White"
                     HorizontalContentAlignment="Center"/>
        </Grid>

        <!-- Computed summary -->
        <TextBlock x:Name="CalibrationSummaryLabel" Text="" Foreground="#0078D4" FontSize="10"
                   Margin="0,6,0,0" TextWrapping="Wrap"/>
    </StackPanel>
</Border>
```

- [ ] **Step 2: Update code-behind — load profile offsets**

In `OnPrinterProfileChanged`, replace the 2-field load with 8 fields:

```csharp
if (PrinterProfileBox.SelectedItem is PrinterProfile profile)
{
    ProfileNameBox.Text = profile.Name;
    OffsetTLXBox.Text = profile.OffsetTLXMm.ToString(CultureInfo.InvariantCulture);
    OffsetTLYBox.Text = profile.OffsetTLYMm.ToString(CultureInfo.InvariantCulture);
    OffsetTRXBox.Text = profile.OffsetTRXMm.ToString(CultureInfo.InvariantCulture);
    OffsetTRYBox.Text = profile.OffsetTRYMm.ToString(CultureInfo.InvariantCulture);
    OffsetBLXBox.Text = profile.OffsetBLXMm.ToString(CultureInfo.InvariantCulture);
    OffsetBLYBox.Text = profile.OffsetBLYMm.ToString(CultureInfo.InvariantCulture);
    OffsetBRXBox.Text = profile.OffsetBRXMm.ToString(CultureInfo.InvariantCulture);
    OffsetBRYBox.Text = profile.OffsetBRYMm.ToString(CultureInfo.InvariantCulture);
    UpdateCalibrationSummary();
    // ...
}
```

- [ ] **Step 3: Update SaveCurrentProfileOffsets**

```csharp
private void SaveCurrentProfileOffsets()
{
    if (PrinterProfileBox.SelectedItem is not PrinterProfile profile) return;

    if (float.TryParse(OffsetTLXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tlx))
        profile.OffsetTLXMm = tlx;
    if (float.TryParse(OffsetTLYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tly))
        profile.OffsetTLYMm = tly;
    if (float.TryParse(OffsetTRXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var trx))
        profile.OffsetTRXMm = trx;
    if (float.TryParse(OffsetTRYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var try_))
        profile.OffsetTRYMm = try_;
    if (float.TryParse(OffsetBLXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var blx))
        profile.OffsetBLXMm = blx;
    if (float.TryParse(OffsetBLYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bly))
        profile.OffsetBLYMm = bly;
    if (float.TryParse(OffsetBRXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var brx))
        profile.OffsetBRXMm = brx;
    if (float.TryParse(OffsetBRYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bry))
        profile.OffsetBRYMm = bry;

    // Clear legacy fields
    profile.OffsetXMm = 0;
    profile.OffsetYMm = 0;
}
```

- [ ] **Step 4: Add computed summary display**

```csharp
private void UpdateCalibrationSummary()
{
    if (PrinterProfileBox.SelectedItem is not PrinterProfile profile)
    {
        CalibrationSummaryLabel.Text = "";
        return;
    }

    // Use default grid dimensions from active project, or A4/MTG defaults
    float gridW = 200, gridH = 280; // approximate defaults
    if (_activeProject != null)
    {
        var s = _activeProject.PageSettings;
        float cellW = s.CardWidthMm + 2 * s.BleedWidthMm;
        float cellH = s.CardHeightMm + 2 * s.BleedWidthMm;
        int cols = s.CardsPerRow;
        int rows = cols > 0 && s.CardsPerPage > 0 ? s.CardsPerPage / cols : 0;
        if (cols > 0 && rows > 0)
        {
            gridW = cols * cellW;
            gridH = rows * cellH;
        }
    }

    SaveCurrentProfileOffsets();
    var cal = CalibrationTransform.Compute(profile, gridW, gridH);
    float xMm = cal.TranslateXPt / (72f / 25.4f);
    float yMm = cal.TranslateYPt / (72f / 25.4f);

    if (cal.HasCorrection)
    {
        string rot = Math.Abs(cal.RotationDegrees) > 0.001f
            ? $", rotation {cal.RotationDegrees:+0.000;-0.000;0}deg"
            : "";
        CalibrationSummaryLabel.Text = $"Computed: translation X {xMm:+0.00;-0.00;0}mm, Y {yMm:+0.00;-0.00;0}mm{rot}";
    }
    else
    {
        CalibrationSummaryLabel.Text = "No correction (all offsets zero)";
    }
}
```

Wire `TextChanged` events on all 8 TextBoxes to call `UpdateCalibrationSummary()` for live feedback.

- [ ] **Step 5: Update UpdatePrinterUI to enable/disable all 8 boxes**

- [ ] **Step 6: Update OnExportAlignmentPdf to pass the profile**

Replace the offset-based call with profile-based:

```csharp
var pdfService = new PdfGeneratorService();
await pdfService.GenerateAlignmentPdfAsync(project, dialog.FileName, profile);
```

Where `profile` is the currently selected `PrinterProfile`.

- [ ] **Step 7: Migrate profiles on load**

In `LoadPrinterProfiles()`, call `MigrateLegacyOffsets()` on each profile:

```csharp
foreach (var profile in _settingsService.Settings.PrinterProfiles)
{
    profile.MigrateLegacyOffsets();
    PrinterProfileBox.Items.Add(profile);
}
```

- [ ] **Step 8: Build and test**

Run: `dotnet build`
Expected: 0 errors

Run: `dotnet test MTGProxyBuilder.Tests --filter "CalibrationTransform|PrinterProfile" -v q`
Expected: All PASS

- [ ] **Step 9: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml MTGProxyBuilder.UI/Dialogs/SettingsDialog.xaml.cs
git commit -m "feat: 4-corner offset input grid with live calibration summary"
```

---

### Task 5: Update Tests and Documentation

**Files:**
- Modify: `MTGProxyBuilder.Tests/Models/PrinterProfileTests.cs`
- Modify: `docs/print-pdf-export.md`

- [ ] **Step 1: Update PrinterProfile tests**

Replace existing tests for old 2-field offsets with corner field tests:

```csharp
[Fact]
public void Defaults_AreCorrect()
{
    var profile = new PrinterProfile();
    Assert.Equal("Default", profile.Name);
    Assert.Equal(0f, profile.OffsetTLXMm);
    Assert.Equal(0f, profile.OffsetTLYMm);
    Assert.Equal(0f, profile.OffsetTRXMm);
    Assert.Equal(0f, profile.OffsetTRYMm);
    Assert.Equal(0f, profile.OffsetBLXMm);
    Assert.Equal(0f, profile.OffsetBLYMm);
    Assert.Equal(0f, profile.OffsetBRXMm);
    Assert.Equal(0f, profile.OffsetBRYMm);
}

[Fact]
public void CornerOffsets_CanBeSet()
{
    var profile = new PrinterProfile
    {
        OffsetTLXMm = 0.5f, OffsetTLYMm = -0.3f,
        OffsetTRXMm = 0.7f, OffsetTRYMm = -0.1f,
        OffsetBLXMm = 0.4f, OffsetBLYMm = -0.4f,
        OffsetBRXMm = 0.6f, OffsetBRYMm = -0.2f,
    };

    Assert.Equal(0.5f, profile.OffsetTLXMm);
    Assert.Equal(-0.1f, profile.OffsetTRYMm);
    Assert.Equal(0.4f, profile.OffsetBLXMm);
    Assert.Equal(-0.2f, profile.OffsetBRYMm);
}

[Fact]
public void JsonRoundTrip_PreservesCornerValues()
{
    var profile = new PrinterProfile
    {
        Name = "Test",
        OffsetTLXMm = 1.25f, OffsetTLYMm = -0.75f,
        OffsetBRXMm = 0.5f, OffsetBRYMm = 0.3f,
    };

    var json = JsonConvert.SerializeObject(profile);
    var deserialized = JsonConvert.DeserializeObject<PrinterProfile>(json);

    Assert.NotNull(deserialized);
    Assert.Equal(1.25f, deserialized!.OffsetTLXMm);
    Assert.Equal(-0.75f, deserialized.OffsetTLYMm);
    Assert.Equal(0.5f, deserialized.OffsetBRXMm);
    Assert.Equal(0.3f, deserialized.OffsetBRYMm);
}
```

- [ ] **Step 2: Update docs/print-pdf-export.md**

In the Printer Calibration section, update the workflow to mention 4-corner measurement:

- Step 4 changes from "measure the gap and enter X/Y offsets" to "measure the offset at each corner target (TL, TR, BL, BR) and enter all 4 X/Y values"
- Add note: "The app computes the best-fit translation and rotation correction from your 4 measurements. If all corners have the same offset, only translation is applied (no rotation)."

- [ ] **Step 3: Full build and test**

Run: `dotnet build`
Expected: 0 errors

Run: `dotnet test MTGProxyBuilder.Tests -v q`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: complete 4-corner printer calibration with rotation correction"
```
