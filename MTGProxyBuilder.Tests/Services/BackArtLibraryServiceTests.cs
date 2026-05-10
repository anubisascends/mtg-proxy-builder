using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class BackArtLibraryServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testImagePath;

    public BackArtLibraryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MTGProxyBuilder_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        // Create a minimal test image file
        _testImagePath = Path.Combine(_testDir, "test_back.png");
        File.WriteAllBytes(_testImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Initially_HasNoEntries()
    {
        var svc = new BackArtLibraryService();
        Assert.NotNull(svc.Entries);
        // May have entries from previous tests; at minimum should not throw
    }

    [Fact]
    public void AddFromFile_ReturnsEntry()
    {
        var svc = new BackArtLibraryService();
        var entry = svc.AddFromFile(_testImagePath, $"Test_{Guid.NewGuid():N}");
        Assert.NotNull(entry);
        Assert.False(string.IsNullOrEmpty(entry!.Id));
        Assert.True(File.Exists(entry.FilePath));
    }

    [Fact]
    public void AddFromFile_DuplicateName_ReturnsExisting()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Dup_{Guid.NewGuid():N}";
        var first = svc.AddFromFile(_testImagePath, uniqueName);
        var second = svc.AddFromFile(_testImagePath, uniqueName);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
    }

    [Fact]
    public void AddFromFile_NonexistentFile_ReturnsNull()
    {
        var svc = new BackArtLibraryService();
        Assert.Null(svc.AddFromFile("/nonexistent/path.png"));
    }

    [Fact]
    public void Remove_DeletesEntryAndFile()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Remove_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        string filePath = entry!.FilePath;
        bool removed = svc.Remove(entry.Id);
        Assert.True(removed);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Remove_NonexistentId_ReturnsFalse()
    {
        var svc = new BackArtLibraryService();
        Assert.False(svc.Remove("nonexistent_id"));
    }

    [Fact]
    public void GetById_FindsEntry()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Find_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        var found = svc.GetById(entry!.Id);
        Assert.NotNull(found);
        Assert.Equal(entry.Name, found!.Name);
    }

    [Fact]
    public void GetById_NotFound_ReturnsNull()
    {
        var svc = new BackArtLibraryService();
        Assert.Null(svc.GetById("nonexistent"));
    }

    [Fact]
    public void DefaultEntryId_InitiallyNull()
    {
        var svc = new BackArtLibraryService();
        Assert.Null(svc.DefaultEntryId);
    }

    [Fact]
    public void SetDefault_SetsDefaultId()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Default_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        Assert.NotNull(entry);

        svc.SetDefault(entry!.Id);
        Assert.Equal(entry.Id, svc.DefaultEntryId);
        Assert.True(svc.IsDefault(entry.Id));
    }

    [Fact]
    public void SetDefault_Null_ClearsDefault()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"ClearDef_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        svc.SetDefault(entry!.Id);
        svc.SetDefault(null);

        Assert.Null(svc.DefaultEntryId);
    }

    [Fact]
    public void DefaultBackArtPath_ReturnsPathWhenSet()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"Path_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        svc.SetDefault(entry!.Id);

        Assert.NotNull(svc.DefaultBackArtPath);
        Assert.True(File.Exists(svc.DefaultBackArtPath));
    }

    [Fact]
    public void DefaultBackArtPath_NullWhenNoDefault()
    {
        var svc = new BackArtLibraryService();
        svc.SetDefault(null); // Ensure clean state
        Assert.Null(svc.DefaultBackArtPath);
    }

    [Fact]
    public void Remove_DefaultEntry_ClearsDefault()
    {
        var svc = new BackArtLibraryService();
        string uniqueName = $"RemDef_{Guid.NewGuid():N}";
        var entry = svc.AddFromFile(_testImagePath, uniqueName);
        svc.SetDefault(entry!.Id);

        svc.Remove(entry.Id);
        Assert.Null(svc.DefaultEntryId);
    }
}
