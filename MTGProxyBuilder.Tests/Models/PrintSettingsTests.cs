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
}
