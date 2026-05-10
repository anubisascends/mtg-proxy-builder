using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class MpcFillSourceManagerTests : IDisposable
{
    private readonly string _origFavPath;
    private readonly string _backupPath;

    public MpcFillSourceManagerTests()
    {
        // Back up the real favorites file so tests don't pollute it
        _origFavPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MTGProxyBuilder", "mpcfill_favorite_sources.json");
        _backupPath = _origFavPath + ".test_backup";
        if (File.Exists(_origFavPath))
            File.Copy(_origFavPath, _backupPath, true);
        // Clear for clean test state
        if (File.Exists(_origFavPath))
            File.Delete(_origFavPath);
    }

    public void Dispose()
    {
        // Restore original
        if (File.Exists(_backupPath))
        {
            File.Copy(_backupPath, _origFavPath, true);
            File.Delete(_backupPath);
        }
    }

    private MpcFillSourceManager CreateWithSources()
    {
        var mgr = new MpcFillSourceManager();
        mgr.SetSources(new Dictionary<string, (string name, string description)>
        {
            ["1"] = ("MrTeferi", "High quality Proxyshop library"),
            ["2"] = ("JohnPrime", "Borderless versions"),
            ["3"] = ("Chilli_Axe", "Original expansive library"),
            ["10"] = ("TestSource", "Test description")
        });
        return mgr;
    }

    [Fact]
    public void Initially_NotLoaded()
    {
        var mgr = new MpcFillSourceManager();
        Assert.False(mgr.IsLoaded);
        Assert.Empty(mgr.AllSources);
        Assert.False(mgr.HasFavorites);
    }

    [Fact]
    public void SetSources_PopulatesAllSources()
    {
        var mgr = CreateWithSources();
        Assert.True(mgr.IsLoaded);
        Assert.Equal(4, mgr.AllSources.Count);
    }

    [Fact]
    public void SetSources_SortsByName()
    {
        var mgr = CreateWithSources();
        var names = mgr.AllSources.Select(s => s.Name).ToList();
        var sorted = names.OrderBy(n => n).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void AddFavorite_MarksAsFavorite()
    {
        var mgr = CreateWithSources();
        mgr.AddFavorite(1);
        Assert.True(mgr.IsFavorite(1));
        Assert.True(mgr.HasFavorites);
    }

    [Fact]
    public void RemoveFavorite_UnmarksAsFavorite()
    {
        var mgr = CreateWithSources();
        mgr.AddFavorite(1);
        mgr.RemoveFavorite(1);
        Assert.False(mgr.IsFavorite(1));
    }

    [Fact]
    public void ToggleFavorite_Toggles()
    {
        var mgr = CreateWithSources();
        mgr.ToggleFavorite(2);
        Assert.True(mgr.IsFavorite(2));
        mgr.ToggleFavorite(2);
        Assert.False(mgr.IsFavorite(2));
    }

    [Fact]
    public void GetByName_FindsSource()
    {
        var mgr = CreateWithSources();
        var src = mgr.GetByName("MrTeferi");
        Assert.NotNull(src);
        Assert.Equal(1, src!.Pk);
    }

    [Fact]
    public void GetByName_CaseInsensitive()
    {
        var mgr = CreateWithSources();
        var src = mgr.GetByName("mrteferi");
        Assert.NotNull(src);
    }

    [Fact]
    public void GetByName_NotFound_ReturnsNull()
    {
        var mgr = CreateWithSources();
        Assert.Null(mgr.GetByName("NonExistent"));
    }

    [Fact]
    public void BuildSourcesArray_AllEnabled_WhenNoSelection()
    {
        var mgr = CreateWithSources();
        var arr = mgr.BuildSourcesArray();

        Assert.Equal(4, arr.Length);
        foreach (var entry in arr)
        {
            Assert.Equal(2, entry.Length);
            Assert.IsType<int>(entry[0]);
            Assert.Equal(true, entry[1]);
        }
    }

    [Fact]
    public void BuildSourcesArray_WithSelection_OnlySelectedEnabled()
    {
        var mgr = CreateWithSources();
        var arr = mgr.BuildSourcesArray(new[] { 1, 3 });

        var enabledPks = arr.Where(e => (bool)e[1]).Select(e => (int)e[0]).ToList();
        Assert.Contains(1, enabledPks);
        Assert.Contains(3, enabledPks);
        Assert.DoesNotContain(2, enabledPks);
    }

    [Fact]
    public void BuildFavoritesArray_NoFavorites_AllEnabled()
    {
        var mgr = CreateWithSources();
        var arr = mgr.BuildFavoritesArray();

        Assert.All(arr, entry => Assert.Equal(true, entry[1]));
    }

    [Fact]
    public void BuildFavoritesArray_WithFavorites_OnlyFavoritesEnabled()
    {
        var mgr = CreateWithSources();
        mgr.AddFavorite(2);
        mgr.AddFavorite(10);

        var arr = mgr.BuildFavoritesArray();
        var enabled = arr.Where(e => (bool)e[1]).Select(e => (int)e[0]).ToHashSet();
        Assert.Contains(2, enabled);
        Assert.Contains(10, enabled);
        Assert.DoesNotContain(1, enabled);
        Assert.DoesNotContain(3, enabled);
    }

    [Fact]
    public void FavoritePks_Count_Correct()
    {
        var mgr = CreateWithSources();
        mgr.AddFavorite(1);
        mgr.AddFavorite(3);
        Assert.Equal(2, mgr.FavoritePks.Count);
    }

    [Fact]
    public void AddFavorite_Duplicate_DoesNotDoubleCount()
    {
        var mgr = CreateWithSources();
        mgr.AddFavorite(1);
        mgr.AddFavorite(1);
        Assert.Equal(1, mgr.FavoritePks.Count);
    }
}
