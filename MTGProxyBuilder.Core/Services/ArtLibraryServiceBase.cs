using System.IO.Compression;
using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;
using Serilog;

namespace MTGProxyBuilder.Core.Services
{
    /// <summary>Catalog wrapper for JSON serialization. Subclasses extend with additional fields.</summary>
    public class ArtLibraryCatalog
    {
        [JsonProperty("entries")]
        public List<BackArtEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Base class for art library services. Provides CRUD, batch operations,
    /// move/export/import, and persistence. Subclasses provide catalog type
    /// specialization and any additional domain-specific features.
    /// </summary>
    public abstract class ArtLibraryServiceBase<TCatalog> where TCatalog : ArtLibraryCatalog, new()
    {
        protected string _libraryDirectory;
        protected string _catalogPath;
        protected List<BackArtEntry> _entries = new();
        private bool _batchMode;
        private HashSet<string>? _batchNameIndex;

        protected ArtLibraryServiceBase(string defaultSubfolder, string? customDirectory)
        {
            _libraryDirectory = customDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", defaultSubfolder);
            Directory.CreateDirectory(_libraryDirectory);
            _catalogPath = Path.Combine(_libraryDirectory, "catalog.json");
            Load();
        }

        public string LibraryDirectory => _libraryDirectory;

        public IReadOnlyList<BackArtEntry> Entries => _entries.AsReadOnly();

        // ================================================================
        //  BATCH OPERATIONS
        // ================================================================

        public void BeginBatch()
        {
            _batchMode = true;
            _batchNameIndex = new HashSet<string>(
                _entries.Select(e => e.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        public void EndBatch()
        {
            _batchMode = false;
            _batchNameIndex = null;
            Save();
        }

        // ================================================================
        //  CRUD
        // ================================================================

        public BackArtEntry? AddFromFile(string sourceFilePath, string? displayName = null, string? contributor = null)
        {
            if (!File.Exists(sourceFilePath))
                return null;

            string name = displayName ?? Path.GetFileNameWithoutExtension(sourceFilePath);

            if (_batchNameIndex != null)
            {
                if (!_batchNameIndex.Add(name))
                    return _entries.FirstOrDefault(e =>
                        string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var existing = _entries.FirstOrDefault(e =>
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    return existing;
            }

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
                Source = contributor ?? "Local",
                AddedDate = DateTime.Now
            };

            _entries.Add(entry);
            if (!_batchMode)
                Save();
            return entry;
        }

        public virtual bool Remove(string entryId)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return false;

            if (File.Exists(entry.FilePath))
            {
                try { File.Delete(entry.FilePath); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete library file {Path}", entry.FilePath); }
            }

            _entries.Remove(entry);
            Save();
            return true;
        }

        public BackArtEntry? GetById(string id)
        {
            return _entries.FirstOrDefault(e => e.Id == id);
        }

        /// <summary>Populates card metadata on an existing entry. Call after AddFromFile when Scryfall data is available.</summary>
        public void SetMetadata(string entryId, string? typeLine = null, string? oracleText = null,
            string? manaCost = null, float? cmc = null, string? rarity = null,
            string? colors = null, string? colorIdentity = null,
            string? setCode = null, string? setName = null, string? artist = null,
            string? power = null, string? toughness = null, string? loyalty = null,
            string? keywords = null, string? collectorNumber = null)
        {
            var entry = GetById(entryId);
            if (entry == null) return;

            if (typeLine != null) entry.TypeLine = typeLine;
            if (oracleText != null) entry.OracleText = oracleText;
            if (manaCost != null) entry.ManaCost = manaCost;
            if (cmc.HasValue) entry.CMC = cmc.Value;
            if (rarity != null) entry.Rarity = rarity;
            if (colors != null) entry.Colors = colors;
            if (colorIdentity != null) entry.ColorIdentity = colorIdentity;
            if (setCode != null) entry.SetCode = setCode;
            if (setName != null) entry.SetName = setName;
            if (artist != null) entry.Artist = artist;
            if (power != null) entry.Power = power;
            if (toughness != null) entry.Toughness = toughness;
            if (loyalty != null) entry.Loyalty = loyalty;
            if (keywords != null) entry.Keywords = keywords;
            if (collectorNumber != null) entry.CollectorNumber = collectorNumber;

            if (!_batchMode) Save();
        }

        // ================================================================
        //  LIBRARY MANAGEMENT
        // ================================================================

        /// <summary>
        /// Moves all library files to a new directory and updates all paths.
        /// If the destination already contains a catalog.json, new entries are merged in.
        /// Returns the list of entry IDs that were newly added (for thumbnail generation).
        /// </summary>
        public List<string> MoveToDirectory(string newDirectory, Action<int, int>? onProgress = null)
        {
            Directory.CreateDirectory(newDirectory);
            string oldDirectory = _libraryDirectory;
            var newEntryIds = new List<string>();

            string destCatalogPath = Path.Combine(newDirectory, "catalog.json");
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(destCatalogPath))
            {
                try
                {
                    string json = File.ReadAllText(destCatalogPath);
                    var existingCatalog = JsonConvert.DeserializeObject<TCatalog>(json);
                    if (existingCatalog?.Entries != null)
                    {
                        foreach (var e in existingCatalog.Entries)
                            existingNames.Add(e.Name);
                        _entries.InsertRange(0, existingCatalog.Entries.Where(e => File.Exists(e.FilePath)));
                        OnMergeExistingCatalog(existingCatalog);
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "Failed to parse existing catalog at {Path}", destCatalogPath); }
            }

            int total = _entries.Count;
            for (int i = 0; i < total; i++)
            {
                var entry = _entries[i];
                if (File.Exists(entry.FilePath) && entry.FilePath.StartsWith(oldDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(entry.FilePath);
                    string destPath = Path.Combine(newDirectory, fileName);
                    File.Copy(entry.FilePath, destPath, overwrite: true);
                    entry.FilePath = destPath;

                    if (!existingNames.Contains(entry.Name))
                        newEntryIds.Add(entry.Id);
                }
                onProgress?.Invoke(i + 1, total);
            }

            var oldThumbDir = Path.Combine(oldDirectory, "Thumbnails");
            if (Directory.Exists(oldThumbDir))
            {
                var newThumbDir = Path.Combine(newDirectory, "Thumbnails");
                Directory.CreateDirectory(newThumbDir);
                foreach (var file in Directory.GetFiles(oldThumbDir))
                    File.Copy(file, Path.Combine(newThumbDir, Path.GetFileName(file)), overwrite: true);
            }

            _libraryDirectory = newDirectory;
            _catalogPath = destCatalogPath;
            Save();

            try
            {
                if (Directory.Exists(oldDirectory) && !string.Equals(oldDirectory, newDirectory, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(oldDirectory, recursive: true);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to delete old library directory {Dir}", oldDirectory); }

            return newEntryIds;
        }

        public void ExportToZip(string zipFilePath, Action<int, int>? onProgress = null)
        {
            if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

            using var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);

            if (File.Exists(_catalogPath))
                zip.CreateEntryFromFile(_catalogPath, "catalog.json", CompressionLevel.Optimal);

            var imageFiles = _entries.Where(e => File.Exists(e.FilePath)).ToList();
            for (int i = 0; i < imageFiles.Count; i++)
            {
                string fileName = Path.GetFileName(imageFiles[i].FilePath);
                zip.CreateEntryFromFile(imageFiles[i].FilePath, fileName, CompressionLevel.Optimal);
                onProgress?.Invoke(i + 1, imageFiles.Count);
            }
        }

        public int ImportFromZip(string zipFilePath, Action<int, int>? onProgress = null)
        {
            using var zip = ZipFile.OpenRead(zipFilePath);

            var catalogEntry = zip.GetEntry("catalog.json");
            if (catalogEntry == null) return 0;

            TCatalog? importedCatalog;
            using (var stream = catalogEntry.Open())
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                importedCatalog = JsonConvert.DeserializeObject<TCatalog>(json);
            }
            if (importedCatalog?.Entries == null) return 0;

            var imageEntries = importedCatalog.Entries;
            int countBefore = _entries.Count;
            BeginBatch();
            try
            {
                for (int i = 0; i < imageEntries.Count; i++)
                {
                    var importEntry = imageEntries[i];
                    string fileName = Path.GetFileName(importEntry.FilePath);
                    var zipImageEntry = zip.GetEntry(fileName);
                    if (zipImageEntry == null) continue;

                    string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                    try
                    {
                        zipImageEntry.ExtractToFile(tempPath, overwrite: true);
                        AddFromFile(tempPath, importEntry.Name, importEntry.Source);
                    }
                    finally
                    {
                        try { File.Delete(tempPath); } catch (Exception ex) { Log.Warning(ex, "Failed to clean up temp file {Path}", tempPath); }
                    }
                    onProgress?.Invoke(i + 1, imageEntries.Count);
                }
            }
            finally { EndBatch(); }

            return _entries.Count - countBefore;
        }

        // ================================================================
        //  PERSISTENCE (subclasses customize via overrides)
        // ================================================================

        /// <summary>Called during MoveToDirectory when the destination has an existing catalog. Override to merge extra fields (e.g. default entry ID).</summary>
        protected virtual void OnMergeExistingCatalog(TCatalog existingCatalog) { }

        /// <summary>Populate catalog fields beyond Entries before saving.</summary>
        protected virtual void OnSaveCatalog(TCatalog catalog) { }

        /// <summary>Read catalog fields beyond Entries after loading.</summary>
        protected virtual void OnLoadCatalog(TCatalog catalog) { }

        protected void Load()
        {
            try
            {
                if (File.Exists(_catalogPath))
                {
                    string json = File.ReadAllText(_catalogPath);
                    var catalog = JsonConvert.DeserializeObject<TCatalog>(json);
                    if (catalog?.Entries != null)
                    {
                        _entries = catalog.Entries;
                        OnLoadCatalog(catalog);
                    }

                    _entries.RemoveAll(e => !File.Exists(e.FilePath));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load art library catalog from {Path}", _catalogPath);
                _entries = new();
            }
        }

        protected void Save()
        {
            try
            {
                var catalog = new TCatalog { Entries = _entries };
                OnSaveCatalog(catalog);
                string json = JsonConvert.SerializeObject(catalog, Formatting.Indented);
                File.WriteAllText(_catalogPath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save art library catalog to {Path}", _catalogPath);
            }
        }
    }
}
