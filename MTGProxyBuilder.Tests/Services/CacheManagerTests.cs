using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class CacheManagerTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, CacheManager.FormatBytes(bytes));
    }

    [Fact]
    public void CleanupOnStartup_DoesNotThrow()
    {
        var mgr = new CacheManager();
        var ex = Record.Exception(() => mgr.CleanupOnStartup());
        Assert.Null(ex);
    }

    [Fact]
    public void GetTotalCacheSizeBytes_ReturnsNonNegative()
    {
        var mgr = new CacheManager();
        Assert.True(mgr.GetTotalCacheSizeBytes() >= 0);
    }

    [Fact]
    public void ClearAllCaches_ReturnsNonNegativeCounts()
    {
        var mgr = new CacheManager();
        var (files, bytes) = mgr.ClearAllCaches();
        Assert.True(files >= 0);
        Assert.True(bytes >= 0);
    }
}
