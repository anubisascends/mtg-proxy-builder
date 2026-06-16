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
        Assert.Equal(0f, profile.OffsetXMm);
        Assert.Equal(0f, profile.OffsetYMm);
    }

    [Fact]
    public void CanSetProperties()
    {
        var profile = new PrinterProfile
        {
            Name = "Canon PIXMA",
            OffsetXMm = 0.5f,
            OffsetYMm = -0.3f
        };

        Assert.Equal("Canon PIXMA", profile.Name);
        Assert.Equal(0.5f, profile.OffsetXMm);
        Assert.Equal(-0.3f, profile.OffsetYMm);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var profile = new PrinterProfile { Name = "Brother Laser" };
        Assert.Equal("Brother Laser", profile.ToString());
    }

    [Fact]
    public void JsonRoundTrip_PreservesValues()
    {
        var profile = new PrinterProfile
        {
            Name = "Test Printer",
            OffsetXMm = 1.25f,
            OffsetYMm = -0.75f
        };

        var json = JsonConvert.SerializeObject(profile);
        var deserialized = JsonConvert.DeserializeObject<PrinterProfile>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Test Printer", deserialized!.Name);
        Assert.Equal(1.25f, deserialized.OffsetXMm);
        Assert.Equal(-0.75f, deserialized.OffsetYMm);
    }
}
