using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class BackArtLibraryCatalog
    {
        [JsonProperty("entries")]
        public List<BackArtEntry> Entries { get; set; } = new();

        [JsonProperty("defaultEntryId")]
        public string? DefaultEntryId { get; set; }
    }

    public class BackArtLibraryService
    {
        private readonly string _libraryDirectory;
        private readonly string _catalogPath;
        private List<BackArtEntry> _entries = new();
        private string? _defaultEntryId;

        public BackArtLibraryService()
        {
            _libraryDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "BackArtLibrary");
            Directory.CreateDirectory(_libraryDirectory);
            _catalogPath = Path.Combine(_libraryDirectory, "catalog.json");
            Load();
        }

        public IReadOnlyList<BackArtEntry> Entries => _entries.AsReadOnly();

        public string? DefaultEntryId => _defaultEntryId;

        /// <summary>Returns the default back art file path, or null if none is set.</summary>
        public string? DefaultBackArtPath
        {
            get
            {
                if (_defaultEntryId == null) return null;
                var entry = _entries.FirstOrDefault(e => e.Id == _defaultEntryId);
                return entry != null && File.Exists(entry.FilePath) ? entry.FilePath : null;
            }
        }

        public void SetDefault(string? entryId)
        {
            _defaultEntryId = entryId;
            Save();
        }

        public bool IsDefault(string entryId) => _defaultEntryId == entryId;

        public BackArtEntry? AddFromFile(string sourceFilePath, string? displayName = null)
        {
            if (!File.Exists(sourceFilePath))
                return null;

            string name = displayName ?? Path.GetFileNameWithoutExtension(sourceFilePath);

            var existing = _entries.FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            string id = Guid.NewGuid().ToString("N")[..12];
            string ext = Path.GetExtension(sourceFilePath);
            string destFileName = $"{id}{ext}";
            string destPath = Path.Combine(_libraryDirectory, destFileName);

            File.Copy(sourceFilePath, destPath, overwrite: true);

            var entry = new BackArtEntry
            {
                Id = id,
                Name = name,
                FilePath = destPath,
                AddedDate = DateTime.Now
            };

            _entries.Add(entry);
            Save();
            return entry;
        }

        public bool Remove(string entryId)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return false;

            if (File.Exists(entry.FilePath))
            {
                try { File.Delete(entry.FilePath); }
                catch { }
            }

            _entries.Remove(entry);
            if (_defaultEntryId == entryId)
                _defaultEntryId = null;
            Save();
            return true;
        }

        public BackArtEntry? GetById(string id)
        {
            return _entries.FirstOrDefault(e => e.Id == id);
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_catalogPath))
                {
                    string json = File.ReadAllText(_catalogPath);

                    // Try new format first
                    var catalog = JsonConvert.DeserializeObject<BackArtLibraryCatalog>(json);
                    if (catalog?.Entries != null && catalog.Entries.Count > 0)
                    {
                        _entries = catalog.Entries;
                        _defaultEntryId = catalog.DefaultEntryId;
                    }
                    else
                    {
                        // Fall back to old format (just a list)
                        _entries = JsonConvert.DeserializeObject<List<BackArtEntry>>(json) ?? new();
                    }

                    _entries.RemoveAll(e => !File.Exists(e.FilePath));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back art library load error: {ex.Message}");
                _entries = new();
            }
        }

        private void Save()
        {
            try
            {
                var catalog = new BackArtLibraryCatalog
                {
                    Entries = _entries,
                    DefaultEntryId = _defaultEntryId
                };
                string json = JsonConvert.SerializeObject(catalog, Formatting.Indented);
                File.WriteAllText(_catalogPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Back art library save error: {ex.Message}");
            }
        }
    }
}
