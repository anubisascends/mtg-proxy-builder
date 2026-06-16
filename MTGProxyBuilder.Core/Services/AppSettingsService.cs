using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class AppSettings
    {
        [JsonProperty("defaultTokenText")]
        public string DefaultTokenText { get; set; } = "TOKEN";

        [JsonProperty("defaultBleedMm")]
        public float DefaultBleedMm { get; set; } = 1.5f;

        [JsonProperty("defaultDpi")]
        public int DefaultDpi { get; set; } = 300;

        [JsonProperty("defaultCardSizePreset")]
        public string DefaultCardSizePreset { get; set; } = "Magic: The Gathering";

        [JsonProperty("defaultPagePreset")]
        public string DefaultPagePreset { get; set; } = "A4";

        [JsonProperty("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = true;

        [JsonProperty("mpcFillUseFavoritesOnly")]
        public bool MpcFillUseFavoritesOnly { get; set; }

        [JsonProperty("mpcFillDefaultMinDpi")]
        public int MpcFillDefaultMinDpi { get; set; }

        [JsonProperty("mpcFillDefaultMaxDpi")]
        public int MpcFillDefaultMaxDpi { get; set; } = 1500;

        [JsonProperty("mpcFillDefaultFuzzySearch")]
        public bool MpcFillDefaultFuzzySearch { get; set; } = true;

        [JsonProperty("mpcFillDefaultSortBy")]
        public string MpcFillDefaultSortBy { get; set; } = "nameAscending";

        [JsonProperty("mpcFillCardTypes")]
        public List<string> MpcFillCardTypes { get; set; } = new() { "CARD" };

        [JsonProperty("mpcFillFilterCardbacks")]
        public bool MpcFillFilterCardbacks { get; set; }

        [JsonProperty("mpcFillMaximumSize")]
        public int MpcFillMaximumSize { get; set; } = 30;

        [JsonProperty("mpcFillLanguages")]
        public List<string> MpcFillLanguages { get; set; } = new();

        [JsonProperty("mpcFillExcludeNsfw")]
        public bool MpcFillExcludeNsfw { get; set; }

        [JsonProperty("mpcFillExcludeAiArt")]
        public bool MpcFillExcludeAiArt { get; set; }

        [JsonProperty("mpcFillExcludeTags")]
        public List<string> MpcFillExcludeTags { get; set; } = new();

        [JsonProperty("mpcFillIncludeTags")]
        public List<string> MpcFillIncludeTags { get; set; } = new();

        [JsonProperty("frontArtLibraryPath")]
        public string? FrontArtLibraryPath { get; set; }

        [JsonProperty("backArtLibraryPath")]
        public string? BackArtLibraryPath { get; set; }

        [JsonProperty("sidebarSearchExpanded")]
        public bool SidebarSearchExpanded { get; set; } = true;

        [JsonProperty("sidebarImportExpanded")]
        public bool SidebarImportExpanded { get; set; }

        [JsonProperty("sidebarCardDetailsExpanded")]
        public bool SidebarCardDetailsExpanded { get; set; }

        [JsonProperty("sidebarLayoutExpanded")]
        public bool SidebarLayoutExpanded { get; set; }

        [JsonProperty("sidebarStorageExpanded")]
        public bool SidebarStorageExpanded { get; set; }

        [JsonProperty("bulkDataRefreshDays")]
        public int BulkDataRefreshDays { get; set; } = 1;

        [JsonProperty("recentFiles")]
        public List<string> RecentFiles { get; set; } = new();

        [JsonProperty("sidebarWidth")]
        public double SidebarWidth { get; set; } = 300;

        [JsonProperty("sidebarFontSize")]
        public double SidebarFontSize { get; set; } = 12;

        [JsonProperty("customCardSizePresets")]
        public List<CardSizePreset> CustomCardSizePresets { get; set; } = new();

        [JsonProperty("printerProfiles")]
        public List<PrinterProfile> PrinterProfiles { get; set; } = new();

        [JsonProperty("defaultPrinterProfileName")]
        public string? DefaultPrinterProfileName { get; set; }
    }

    public class AppSettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings;

        public AppSettingsService()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "app_settings.json");
            _settings = Load();
        }

        public AppSettings Settings => _settings;

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsPath, json);
            }
            catch { }
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonConvert.DeserializeObject<AppSettings>(json) ?? new();
                }
            }
            catch { }
            return new AppSettings();
        }
    }
}
