using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Tests.Models;

public class PrinterProfileTests
{
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
            Name = "Canon PIXMA",
            OffsetTLXMm = 0.5f, OffsetTLYMm = -0.3f,
            OffsetTRXMm = 0.7f, OffsetTRYMm = -0.1f,
            OffsetBLXMm = 0.4f, OffsetBLYMm = -0.4f,
            OffsetBRXMm = 0.6f, OffsetBRYMm = -0.2f,
        };

        Assert.Equal("Canon PIXMA", profile.Name);
        Assert.Equal(0.5f, profile.OffsetTLXMm);
        Assert.Equal(-0.1f, profile.OffsetTRYMm);
        Assert.Equal(0.4f, profile.OffsetBLXMm);
        Assert.Equal(-0.2f, profile.OffsetBRYMm);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var profile = new PrinterProfile { Name = "Brother Laser" };
        Assert.Equal("Brother Laser", profile.ToString());
    }

    [Fact]
    public void JsonRoundTrip_PreservesCornerValues()
    {
        var profile = new PrinterProfile
        {
            Name = "Test Printer",
            OffsetTLXMm = 1.25f, OffsetTLYMm = -0.75f,
            OffsetTRXMm = 0.5f, OffsetTRYMm = 0.3f,
            OffsetBLXMm = -0.2f, OffsetBLYMm = 0.1f,
            OffsetBRXMm = 0.8f, OffsetBRYMm = -0.4f,
        };

        var json = JsonConvert.SerializeObject(profile);
        var deserialized = JsonConvert.DeserializeObject<PrinterProfile>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Test Printer", deserialized!.Name);
        Assert.Equal(1.25f, deserialized.OffsetTLXMm);
        Assert.Equal(-0.75f, deserialized.OffsetTLYMm);
        Assert.Equal(0.5f, deserialized.OffsetTRXMm);
        Assert.Equal(0.3f, deserialized.OffsetTRYMm);
        Assert.Equal(-0.2f, deserialized.OffsetBLXMm);
        Assert.Equal(0.1f, deserialized.OffsetBLYMm);
        Assert.Equal(0.8f, deserialized.OffsetBRXMm);
        Assert.Equal(-0.4f, deserialized.OffsetBRYMm);
    }

    [Fact]
    public void JsonRoundTrip_PreservesLegacyFields()
    {
        // Simulates loading an old profile that only has OffsetXMm/OffsetYMm
        var json = """{"name":"Old Printer","offsetXMm":1.5,"offsetYMm":-0.3}""";
        var profile = JsonConvert.DeserializeObject<PrinterProfile>(json);

        Assert.NotNull(profile);
        Assert.Equal(1.5f, profile!.OffsetXMm);
        Assert.Equal(-0.3f, profile.OffsetYMm);
        Assert.Equal(0f, profile.OffsetTLXMm); // corners default to 0
    }

    [Fact]
    public void MigrateLegacyOffsets_CopiesUniformOffset()
    {
        var profile = new PrinterProfile { OffsetXMm = 1.5f, OffsetYMm = -0.3f };
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
    public void MigrateLegacyOffsets_DoesNotOverwriteCornerValues()
    {
        var profile = new PrinterProfile
        {
            OffsetXMm = 1.5f, OffsetYMm = -0.3f,
            OffsetTLXMm = 0.5f // corner already set
        };
        profile.MigrateLegacyOffsets();

        Assert.Equal(0.5f, profile.OffsetTLXMm); // not overwritten
    }
}
