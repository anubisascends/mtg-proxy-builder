using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class MpcFillSource
    {
        public int Pk { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsFavorite { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Manages MPCFill sources list and persists favorite sources across sessions.
    /// </summary>
    public class MpcFillSourceManager
    {
        private readonly string _favoritesPath;
        private List<MpcFillSource> _allSources = new();
        private HashSet<int> _favoritePks = new();
        private bool _loaded;

        public MpcFillSourceManager()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder");
            Directory.CreateDirectory(dir);
            _favoritesPath = Path.Combine(dir, "mpcfill_favorite_sources.json");
            LoadFavorites();
        }

        public IReadOnlyList<MpcFillSource> AllSources => _allSources;
        public IReadOnlySet<int> FavoritePks => _favoritePks;
        public bool HasFavorites => _favoritePks.Count > 0;

        /// <summary>Populates the sources list from raw API data. Call once after fetching from /2/sources/.</summary>
        public void SetSources(Dictionary<string, (string name, string description)> rawSources)
        {
            _allSources = rawSources.Select(kv => new MpcFillSource
            {
                Pk = int.Parse(kv.Key),
                Name = kv.Value.name,
                Description = kv.Value.description,
                IsFavorite = _favoritePks.Contains(int.Parse(kv.Key))
            })
            .OrderBy(s => s.Name)
            .ToList();
            _loaded = true;
        }

        public bool IsLoaded => _loaded;

        public void AddFavorite(int pk)
        {
            _favoritePks.Add(pk);
            var src = _allSources.FirstOrDefault(s => s.Pk == pk);
            if (src != null) src.IsFavorite = true;
            SaveFavorites();
        }

        public void RemoveFavorite(int pk)
        {
            _favoritePks.Remove(pk);
            var src = _allSources.FirstOrDefault(s => s.Pk == pk);
            if (src != null) src.IsFavorite = false;
            SaveFavorites();
        }

        public void ToggleFavorite(int pk)
        {
            if (_favoritePks.Contains(pk)) RemoveFavorite(pk);
            else AddFavorite(pk);
        }

        public bool IsFavorite(int pk) => _favoritePks.Contains(pk);

        public MpcFillSource? GetByName(string name) =>
            _allSources.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Build the sources array for the API, using only selected PKs or all if none selected.</summary>
        public object[][] BuildSourcesArray(IEnumerable<int>? selectedPks = null)
        {
            var pks = selectedPks?.ToHashSet();
            if (pks == null || pks.Count == 0)
                return _allSources.Select(s => new object[] { s.Pk, true }).ToArray();

            return _allSources
                .Select(s => new object[] { s.Pk, pks.Contains(s.Pk) })
                .ToArray();
        }

        /// <summary>Build the sources array using only favorites (or all if no favorites).</summary>
        public object[][] BuildFavoritesArray()
        {
            if (_favoritePks.Count == 0)
                return _allSources.Select(s => new object[] { s.Pk, true }).ToArray();

            return _allSources
                .Select(s => new object[] { s.Pk, _favoritePks.Contains(s.Pk) })
                .ToArray();
        }

        private void LoadFavorites()
        {
            try
            {
                if (File.Exists(_favoritesPath))
                {
                    var json = File.ReadAllText(_favoritesPath);
                    _favoritePks = JsonConvert.DeserializeObject<HashSet<int>>(json) ?? new();
                }
            }
            catch { _favoritePks = new(); }
        }

        private void SaveFavorites()
        {
            try
            {
                File.WriteAllText(_favoritesPath, JsonConvert.SerializeObject(_favoritePks.ToList()));
            }
            catch { }
        }
    }
}
