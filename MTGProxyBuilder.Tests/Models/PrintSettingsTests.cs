using MTGProxyBuilder.Core;
using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class PrintSettingsTests
{
    [Fact]
    public void Defaults_AreDuplexWith300Dpi()
    {
        var settings = new PrintSettings();
        Assert.Equal(PrintMode.Duplex, settings.PrintMode);
        Assert.Equal(Constants.DefaultDpi, settings.DPI);
        Assert.True(settings.ShowCutGuides);
    }

    [Fact]
    public void PrintMode_CanBeChanged()
    {
        var settings = new PrintSettings();
        settings.PrintMode = PrintMode.FrontsOnly;
        Assert.Equal(PrintMode.FrontsOnly, settings.PrintMode);
    }

    [Fact]
    public void PropertyChanged_FiresOnPrintModeChange()
    {
        var settings = new PrintSettings();
        string? changed = null;
        settings.PropertyChanged += (_, e) => changed = e.PropertyName;

        settings.PrintMode = PrintMode.BacksOnly;
        Assert.Equal("PrintMode", changed);
    }

    // --- Card Outline Defaults ---

    [Fact]
    public void CardOutline_DefaultsAreCorrect()
    {
        var settings = new PrintSettings();
        Assert.True(settings.ShowCardOutline);
        Assert.Equal("#66FF00", settings.OutlineColor);
        Assert.Equal(OutlineAlignment.Outside, settings.OutlineAlignment);
        Assert.Equal(3f, settings.CornerRadiusMm);
        Assert.Equal(OutlineType.Corners, settings.OutlineType);
        Assert.Equal(LineType.Solid, settings.OutlineLineType);
        Assert.Equal(5f, settings.CornerLengthMm);
        Assert.Equal(2f, settings.LineWeight);
    }

    [Fact]
    public void ShowCardOutline_CanBeToggled()
    {
        var settings = new PrintSettings();
        settings.ShowCardOutline = false;
        Assert.False(settings.ShowCardOutline);
        settings.ShowCardOutline = true;
        Assert.True(settings.ShowCardOutline);
    }

    [Fact]
    public void OutlineColor_CanBeChanged()
    {
        var settings = new PrintSettings();
        settings.OutlineColor = "#FF0000";
        Assert.Equal("#FF0000", settings.OutlineColor);
    }

    [Fact]
    public void OutlineAlignment_AllValues()
    {
        var settings = new PrintSettings();
        foreach (var val in Enum.GetValues<OutlineAlignment>())
        {
            settings.OutlineAlignment = val;
            Assert.Equal(val, settings.OutlineAlignment);
        }
    }

    [Fact]
    public void OutlineType_AllValues()
    {
        var settings = new PrintSettings();
        foreach (var val in Enum.GetValues<OutlineType>())
        {
            settings.OutlineType = val;
            Assert.Equal(val, settings.OutlineType);
        }
    }

    [Fact]
    public void LineType_AllValues()
    {
        var settings = new PrintSettings();
        foreach (var val in Enum.GetValues<LineType>())
        {
            settings.OutlineLineType = val;
            Assert.Equal(val, settings.OutlineLineType);
        }
    }

    [Fact]
    public void CornerRadiusMm_CanBeZero()
    {
        var settings = new PrintSettings();
        settings.CornerRadiusMm = 0;
        Assert.Equal(0f, settings.CornerRadiusMm);
    }

    [Fact]
    public void CornerLengthMm_CanBeChanged()
    {
        var settings = new PrintSettings();
        settings.CornerLengthMm = 10f;
        Assert.Equal(10f, settings.CornerLengthMm);
    }

    [Fact]
    public void LineWeight_CanBeChanged()
    {
        var settings = new PrintSettings();
        settings.LineWeight = 4f;
        Assert.Equal(4f, settings.LineWeight);
    }

    [Fact]
    public void PropertyChanged_FiresOnOutlineProperties()
    {
        var settings = new PrintSettings();
        var changed = new List<string>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        settings.ShowCardOutline = false;
        settings.OutlineColor = "#000000";
        settings.OutlineAlignment = OutlineAlignment.Inside;
        settings.CornerRadiusMm = 5f;
        settings.OutlineType = OutlineType.Full;
        settings.OutlineLineType = LineType.Dashed;
        settings.CornerLengthMm = 8f;
        settings.LineWeight = 3f;

        Assert.Contains("ShowCardOutline", changed);
        Assert.Contains("OutlineColor", changed);
        Assert.Contains("OutlineAlignment", changed);
        Assert.Contains("CornerRadiusMm", changed);
        Assert.Contains("OutlineType", changed);
        Assert.Contains("OutlineLineType", changed);
        Assert.Contains("CornerLengthMm", changed);
        Assert.Contains("LineWeight", changed);
    }

    // --- Silhouette Cameo Defaults ---

    [Fact]
    public void SilhouetteCameo_DefaultsAreCorrect()
    {
        var settings = new PrintSettings();
        Assert.False(settings.ShowRegistrationMarks);
        Assert.False(settings.ExportSvgCutLines);
        Assert.Equal(0.197f, settings.RegMarkSquareSizeIn);
        Assert.Equal(0.787f, settings.RegMarkLengthIn);
        Assert.Equal(0.012f, settings.RegMarkThicknessIn);
        Assert.Equal(0.394f, settings.RegMarkInsetIn);
    }

    [Fact]
    public void SilhouetteCameo_PropertiesCanBeChanged()
    {
        var settings = new PrintSettings();
        settings.ShowRegistrationMarks = true;
        settings.ExportSvgCutLines = true;
        settings.RegMarkLengthIn = 0.5f;
        settings.RegMarkThicknessIn = 0.06f;
        settings.RegMarkInsetIn = 0.5f;

        Assert.True(settings.ShowRegistrationMarks);
        Assert.True(settings.ExportSvgCutLines);
        Assert.Equal(0.5f, settings.RegMarkLengthIn);
        Assert.Equal(0.06f, settings.RegMarkThicknessIn);
        Assert.Equal(0.5f, settings.RegMarkInsetIn);
    }

    [Fact]
    public void PropertyChanged_FiresOnSilhouetteProperties()
    {
        var settings = new PrintSettings();
        var changed = new List<string>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        settings.ShowRegistrationMarks = true;
        settings.ExportSvgCutLines = true;
        settings.RegMarkLengthIn = 0.4f;
        settings.RegMarkThicknessIn = 0.05f;
        settings.RegMarkInsetIn = 0.5f;

        Assert.Contains("ShowRegistrationMarks", changed);
        Assert.Contains("ExportSvgCutLines", changed);
        Assert.Contains("RegMarkLengthIn", changed);
        Assert.Contains("RegMarkThicknessIn", changed);
        Assert.Contains("RegMarkInsetIn", changed);
    }
}
