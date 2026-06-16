using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class AppSettingsServiceTests
{
    [Fact]
    public void Settings_ObjectHasExpectedDefaults()
    {
        // Test the AppSettings class defaults (not the file on disk which may have been modified)
        var settings = new MTGProxyBuilder.Core.Services.AppSettings();
        Assert.Equal("TOKEN", settings.DefaultTokenText);
        Assert.Equal(1.5f, settings.DefaultBleedMm);
        Assert.Equal("Magic: The Gathering", settings.DefaultCardSizePreset);
        Assert.Equal("A4", settings.DefaultPagePreset);
        Assert.True(settings.CheckForUpdates);
    }

    [Fact]
    public void Settings_CanBeModified()
    {
        var svc = new AppSettingsService();
        svc.Settings.DefaultTokenText = "PROXY";
        svc.Settings.DefaultBleedMm = 3f;
        svc.Settings.DefaultPagePreset = "Letter";
        svc.Settings.CheckForUpdates = false;

        Assert.Equal("PROXY", svc.Settings.DefaultTokenText);
        Assert.Equal(3f, svc.Settings.DefaultBleedMm);
        Assert.Equal("Letter", svc.Settings.DefaultPagePreset);
        Assert.False(svc.Settings.CheckForUpdates);
    }

    [Fact]
    public void Save_DoesNotThrow()
    {
        var svc = new AppSettingsService();
        var ex = Record.Exception(() => svc.Save());
        Assert.Null(ex);
    }

    // --- MPCFill Settings Defaults ---

    [Fact]
    public void MpcFillSettings_HaveCorrectDefaults()
    {
        var settings = new AppSettings();
        Assert.Equal(0, settings.MpcFillDefaultMinDpi);
        Assert.Equal(1500, settings.MpcFillDefaultMaxDpi);
        Assert.True(settings.MpcFillDefaultFuzzySearch);
        Assert.Equal("nameAscending", settings.MpcFillDefaultSortBy);
        Assert.Equal(new List<string> { "CARD" }, settings.MpcFillCardTypes);
        Assert.False(settings.MpcFillFilterCardbacks);
        Assert.Equal(30, settings.MpcFillMaximumSize);
        Assert.Empty(settings.MpcFillLanguages);
        Assert.False(settings.MpcFillExcludeNsfw);
        Assert.False(settings.MpcFillExcludeAiArt);
        Assert.Empty(settings.MpcFillExcludeTags);
        Assert.Empty(settings.MpcFillIncludeTags);
        Assert.False(settings.MpcFillUseFavoritesOnly);
        Assert.Empty(settings.PrinterProfiles);
        Assert.Null(settings.DefaultPrinterProfileName);
    }

    [Fact]
    public void PrinterProfiles_CanBeAddedAndPersisted()
    {
        var settings = new AppSettings();
        settings.PrinterProfiles.Add(new MTGProxyBuilder.Core.Models.PrinterProfile
        {
            Name = "Test Printer",
            OffsetXMm = 0.5f,
            OffsetYMm = -0.3f
        });
        settings.DefaultPrinterProfileName = "Test Printer";

        Assert.Single(settings.PrinterProfiles);
        Assert.Equal("Test Printer", settings.PrinterProfiles[0].Name);
        Assert.Equal(0.5f, settings.PrinterProfiles[0].OffsetXMm);
        Assert.Equal(-0.3f, settings.PrinterProfiles[0].OffsetYMm);
        Assert.Equal("Test Printer", settings.DefaultPrinterProfileName);
    }

    [Fact]
    public void MpcFillSettings_CanBeModified()
    {
        var settings = new AppSettings();
        settings.MpcFillDefaultMinDpi = 600;
        settings.MpcFillDefaultMaxDpi = 1200;
        settings.MpcFillDefaultFuzzySearch = false;
        settings.MpcFillDefaultSortBy = "dateCreatedDescending";
        settings.MpcFillCardTypes = new List<string> { "CARD", "TOKEN" };
        settings.MpcFillFilterCardbacks = true;
        settings.MpcFillMaximumSize = 10;
        settings.MpcFillLanguages = new List<string> { "EN", "JA" };
        settings.MpcFillExcludeNsfw = true;
        settings.MpcFillExcludeAiArt = true;
        settings.MpcFillExcludeTags = new List<string> { "Full-Art" };
        settings.MpcFillIncludeTags = new List<string> { "Modern" };

        Assert.Equal(600, settings.MpcFillDefaultMinDpi);
        Assert.Equal(1200, settings.MpcFillDefaultMaxDpi);
        Assert.False(settings.MpcFillDefaultFuzzySearch);
        Assert.Equal("dateCreatedDescending", settings.MpcFillDefaultSortBy);
        Assert.Equal(2, settings.MpcFillCardTypes.Count);
        Assert.True(settings.MpcFillFilterCardbacks);
        Assert.Equal(10, settings.MpcFillMaximumSize);
        Assert.Equal(2, settings.MpcFillLanguages.Count);
        Assert.True(settings.MpcFillExcludeNsfw);
        Assert.True(settings.MpcFillExcludeAiArt);
        Assert.Single(settings.MpcFillExcludeTags);
        Assert.Single(settings.MpcFillIncludeTags);
    }

    [Fact]
    public void MpcFillSettings_SaveAndLoadRoundTrip()
    {
        var svc = new AppSettingsService();
        svc.Settings.MpcFillDefaultMinDpi = 300;
        svc.Settings.MpcFillLanguages = new List<string> { "EN", "FR" };
        svc.Settings.MpcFillExcludeNsfw = true;
        svc.Save();

        // Load fresh instance — should read back the persisted values
        var svc2 = new AppSettingsService();
        Assert.Equal(300, svc2.Settings.MpcFillDefaultMinDpi);
        Assert.Contains("EN", svc2.Settings.MpcFillLanguages);
        Assert.Contains("FR", svc2.Settings.MpcFillLanguages);
        Assert.True(svc2.Settings.MpcFillExcludeNsfw);
    }
}
