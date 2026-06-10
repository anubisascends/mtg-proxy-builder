using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using Serilog;

namespace MTGProxyBuilder.UI.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
    }

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class MainViewModel : ViewModelBase
    {
        private ProjectModel _currentProject;
        private ObservableCollection<CardModel> _cards;
        private CardModel? _selectedCard;
        private string _statusText = "Ready";
        private string? _currentFilePath;

        // Scryfall lookup (card panel)
        private string _scryfallLookupText = string.Empty;
        private string _scryfallLookupStatus = string.Empty;

        // Scryfall search
        private string _scryfallSearchQuery = string.Empty;
        private ObservableCollection<ScryfallCard> _scryfallResults = new();
        private ScryfallCard? _selectedScryfallCard;
        private bool _isSearching;
        private bool _isBusy;
        private string _busyMessage = string.Empty;
        private bool _hasUnsavedChanges;

        // Back art library
        private ObservableCollection<BackArtEntry> _backArtLibrary = new();
        private BackArtEntry? _selectedBackArt;

        // Sort and filter
        private string _filterText = string.Empty;
        private string _filterRarity = "All";
        private string _filterColor = "All";
        private string _sortBy = "Date Added";
        private bool _sortDescending;
        private ObservableCollection<CardModel> _filteredCards = new();

        // Page presets
        private string _selectedPagePreset = "A4";
        private CardSizePreset? _selectedCardSize;

        private readonly ProjectSerializationService _serializationService;
        private readonly PdfGeneratorService _pdfGeneratorService;
        private readonly ScryfallService _scryfallService;
        private readonly ImageCacheService _imageCacheService;
        private BackArtLibraryService _backArtLibraryService;
        private FrontArtLibraryService _frontArtLibraryService;
        private readonly MoxfieldService _moxfieldService;
        private readonly ArchidektService _archidektService;
        private readonly DeckImportService _deckImportService;
        private readonly MpcFillService _mpcFillService;
        private readonly MpcFillXmlImportService _mpcXmlImportService;
        private readonly SearchCoordinator _searchCoordinator;
        private readonly ImportCoordinator _importCoordinator;
        private readonly UndoService _undoService = new();
        private readonly CacheManager _cacheManager = new();
        private readonly UpdateCheckService _updateService = new();
        private readonly AppSettingsService _appSettings = new();
        private bool _updateAvailable;
        private string _updateMessage = string.Empty;
        private string _updateDownloadUrl = string.Empty;

        // Art source toggle
        private bool _useMpcFill;
        private ObservableCollection<MpcFillCard> _mpcFillResults = new();
        private MpcFillCard? _selectedMpcFillCard;
        private string? _searchPreviewUrl;

        // MPCFill advanced search
        private string _mpcAdvName = string.Empty;
        private int _mpcAdvMinDpi;
        private bool _mpcFuzzySearch = true;
        private bool _mpcUseFavoritesOnly;

        // Advanced search
        private bool _showAdvancedSearch;
        private string _advName = string.Empty;
        private string _advType = string.Empty;
        private string _advOracle = string.Empty;
        private string _advColors = string.Empty;
        private string _advIdentity = string.Empty;
        private string _advCmcOp = "=";
        private string _advCmcValue = string.Empty;
        private string _advRarity = string.Empty;
        private string _advSet = string.Empty;
        private string _advFormat = string.Empty;
        private string _advPowOp = "=";
        private string _advPowValue = string.Empty;
        private string _advTouOp = "=";
        private string _advTouValue = string.Empty;
        private string _advArtist = string.Empty;
        private string _advKeyword = string.Empty;
        private string _advIs = string.Empty;

        // Moxfield import
        private string _importDeckUrl = string.Empty;
        private bool _ignoreDuplicates = true;

        public MainViewModel()
        {
            _imageCacheService = new ImageCacheService();
            _serializationService = new ProjectSerializationService();
            _pdfGeneratorService = new PdfGeneratorService();
            _scryfallService = new ScryfallService(_imageCacheService);
            _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
            _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
            _moxfieldService = new MoxfieldService();
            _archidektService = new ArchidektService();
            _deckImportService = new DeckImportService(_moxfieldService, _archidektService);
            MpcSourceManager = new MpcFillSourceManager();
            _mpcFillService = new MpcFillService(_imageCacheService, MpcSourceManager);
            _mpcXmlImportService = new MpcFillXmlImportService(_mpcFillService, _imageCacheService);
            _searchCoordinator = new SearchCoordinator(_scryfallService, _mpcFillService, _appSettings, MpcSourceManager);
            _importCoordinator = new ImportCoordinator(_searchCoordinator, _deckImportService, _mpcXmlImportService, _frontArtLibraryService);
            _mpcUseFavoritesOnly = _appSettings.Settings.MpcFillUseFavoritesOnly;
            _mpcAdvMinDpi = _appSettings.Settings.MpcFillDefaultMinDpi;
            _mpcFuzzySearch = _appSettings.Settings.MpcFillDefaultFuzzySearch;

            // Pre-fetch MPCFill sources in the background so they're ready when needed
            _ = _mpcFillService.EnsureSourcesLoadedAsync();

            _currentProject = new ProjectModel();
            _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
            _cards = new ObservableCollection<CardModel>();
            _cards.CollectionChanged += OnCardsCollectionChanged;

            NewProjectCommand = new RelayCommand(_ => NewProject());
            OpenProjectCommand = new RelayCommand(_ => OpenProject());
            SaveProjectCommand = new RelayCommand(_ => SaveProject());
            SaveProjectAsCommand = new RelayCommand(_ => SaveProjectAs());
            UndoCommand = new RelayCommand(_ => Undo(), _ => _undoService.CanUndo);
            RedoCommand = new RelayCommand(_ => Redo(), _ => _undoService.CanRedo);
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());

            AddCardFromFileCommand = new RelayCommand(_ => AddCardFromFile());
            RemoveCardCommand = new RelayCommand(_ => RemoveCard(), _ => SelectedCard != null);
            BrowseFrontArtworkCommand = new RelayCommand(_ => BrowseFrontArtwork(), _ => SelectedCard != null);
            BrowseBackArtworkCommand = new RelayCommand(_ => BrowseBackArtwork(), _ => SelectedCard != null);
            FetchScryfallDataCommand = new RelayCommand(async _ => await FetchScryfallData(), _ => SelectedCard != null);
            SelectBackArtForAllCommand = new RelayCommand(_ => SelectBackArtForAll(), _ => Cards.Count > 0);

            ScryfallSearchCommand = new RelayCommand(_ => ScryfallSearch(), _ => !string.IsNullOrWhiteSpace(ScryfallSearchQuery));
            AddScryfallCardCommand = new RelayCommand(_ => AddScryfallCard(), _ => SelectedScryfallCard != null);

            ExportPdfCommand = new RelayCommand(_ => ExportPdf());
            ExportSvgCommand = new RelayCommand(_ => ExportSvgOnly());

            // Back art library commands
            AddBackArtToLibraryCommand = new RelayCommand(_ => AddBackArtToLibrary());
            RemoveBackArtFromLibraryCommand = new RelayCommand(_ => RemoveBackArtFromLibrary(), _ => SelectedBackArt != null);
            ApplyBackArtToSelectedCommand = new RelayCommand(_ => ApplyBackArtToSelected(), _ => SelectedBackArt != null && SelectedCard != null);
            ApplyBackArtToAllCommand = new RelayCommand(_ => ApplyBackArtToAll(), _ => SelectedBackArt != null && Cards.Count > 0);
            ClearBackArtFromAllCommand = new RelayCommand(_ => ClearBackArtFromAll(), _ => Cards.Count > 0);

            // Page layout commands
            SetPagePresetCommand = new RelayCommand(p => SetPagePreset(p as string));
            ToggleLandscapeCommand = new RelayCommand(_ => ToggleLandscape());

            // Sort/filter commands
            ApplySortToProjectCommand = new RelayCommand(_ => ApplySortToProject());
            ClearFilterCommand = new RelayCommand(_ => ClearFilter());

            // Moxfield import
            ImportDeckCommand = new RelayCommand(_ => ImportDeck(), _ => !string.IsNullOrWhiteSpace(ImportDeckUrl));
            RefreshDeckCommand = new RelayCommand(_ => RefreshDeck(), _ => !string.IsNullOrEmpty(_currentProject.DeckImportUrl));

            // Advanced search
            BuildAdvancedQueryCommand = new RelayCommand(_ => ApplyAdvancedQuery());
            ClearAdvancedSearchCommand = new RelayCommand(_ => ClearAdvancedSearch());

            // MPCFill sources
            LoadMpcSourcesCommand = new RelayCommand(_ => LoadMpcSources());
            ToggleMpcFavoriteFromResultCommand = new RelayCommand(p => ToggleFavoriteFromResult(p));
            ManageMpcSourcesCommand = new RelayCommand(_ => ManageMpcSources());
            ImportMpcFillXmlCommand = new RelayCommand(_ => ImportMpcFillXml());
            ClearCacheCommand = new RelayCommand(_ => ClearCache());
            ManageBackArtLibraryCommand = new RelayCommand(_ => ManageBackArtLibrary());
            ManageFrontArtLibraryCommand = new RelayCommand(_ => ManageFrontArtLibrary());
            DownloadUpdateCommand = new RelayCommand(_ => DownloadUpdate());
            DismissUpdateCommand = new RelayCommand(_ => UpdateAvailable = false);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());

            // MPCFill / art source
            AddMpcFillCardCommand = new RelayCommand(_ => AddMpcFillCard(), _ => SelectedMpcFillCard != null);
            ClearAllCardsCommand = new RelayCommand(_ => ClearAllCards(), _ => Cards.Count > 0);
            UpdateAllArtFromMpcFillCommand = new RelayCommand(_ => UpdateAllArtFromMpcFill(), _ => Cards.Count > 0);

            // PrintMode values for ComboBox
            PrintModeValues = new ObservableCollection<PrintMode>(
                Enum.GetValues<PrintMode>());

            PagePresets = new ObservableCollection<string> { "A1", "A2", "A3", "A4", "Letter", "Legal", "Tabloid", "Custom" };
            _selectedPagePreset = "A4";
            _selectedCardSize = CardSizePresets.First(p => p.Name == "Magic: The Gathering");

            // Load persisted back art library
            RefreshBackArtLibrary();
            ApplyFilterAndSort();

            // Startup cache cleanup
            _cacheManager.CleanupOnStartup();

        }

        /// <summary>
        /// Replaces the library services with shared instances from the ShellViewModel.
        /// Call after construction to ensure all ViewModels use the same library data.
        /// </summary>
        public void UseSharedLibraries(FrontArtLibraryService frontLibrary, BackArtLibraryService backLibrary)
        {
            _frontArtLibraryService = frontLibrary;
            _backArtLibraryService = backLibrary;
            RefreshBackArtLibrary();
        }

        private async Task CheckForUpdateAsync()
        {
            try
            {
                string currentVersion = GetAppVersion();

                var update = await _updateService.CheckForUpdateAsync(currentVersion);
                if (update?.IsUpdateAvailable == true)
                {
                    UpdateAvailable = true;
                    UpdateMessage = $"Version {update.LatestVersion} is available (you have {update.CurrentVersion})";
                    UpdateDownloadUrl = update.DownloadUrl;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Update check failed"); }
        }

        private void OpenSettings()
        {
            string? oldFrontPath = _appSettings.Settings.FrontArtLibraryPath;
            string? oldBackPath = _appSettings.Settings.BackArtLibraryPath;

            var dialog = new Dialogs.SettingsDialog(_appSettings, MpcSourceManager, _mpcFillService);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                MpcUseFavoritesOnly = _appSettings.Settings.MpcFillUseFavoritesOnly;
                MpcAdvMinDpi = _appSettings.Settings.MpcFillDefaultMinDpi;
                MpcFuzzySearch = _appSettings.Settings.MpcFillDefaultFuzzySearch;
            }

            // Reload library services if paths changed
            if (_appSettings.Settings.FrontArtLibraryPath != oldFrontPath)
                _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
            if (_appSettings.Settings.BackArtLibraryPath != oldBackPath)
            {
                _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
                RefreshBackArtLibrary();
            }
        }

        private void DownloadUpdate()
        {
            if (!string.IsNullOrEmpty(UpdateDownloadUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = UpdateDownloadUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { Log.Warning(ex, "Failed to open update URL {Url}", UpdateDownloadUrl); }
            }
        }

        // --- Properties ---

        public ProjectModel CurrentProject
        {
            get => _currentProject;
            set { SetProperty(ref _currentProject, value); OnPropertyChanged(nameof(ProjectName)); }
        }

        public ObservableCollection<CardModel> Cards
        {
            get => _cards;
            set
            {
                if (_cards != null)
                    _cards.CollectionChanged -= OnCardsCollectionChanged;
                SetProperty(ref _cards, value!);
                if (_cards != null)
                    _cards.CollectionChanged += OnCardsCollectionChanged;
            }
        }

        private void OnCardsCollectionChanged(object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
        }

        private System.Windows.Threading.DispatcherTimer? _layoutBusyTimer;

        private void OnPageSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Show spinner for layout-affecting changes (the canvas redraw is debounced at 80ms)
            if (!IsBusy)
            {
                BusyMessage = "Updating layout...";
                IsBusy = true;
            }

            // Reset a timer that clears busy after the canvas has had time to redraw
            _layoutBusyTimer?.Stop();
            _layoutBusyTimer ??= new System.Windows.Threading.DispatcherTimer();
            _layoutBusyTimer.Interval = TimeSpan.FromMilliseconds(200);
            _layoutBusyTimer.Tick += (_, _) => { _layoutBusyTimer.Stop(); ClearBusy(); };
            _layoutBusyTimer.Start();
        }

        public CardModel? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (SetProperty(ref _selectedCard, value))
                {
                    ScryfallLookupText = value?.Name ?? string.Empty;
                    ScryfallLookupStatus = string.Empty;
                }
            }
        }

        public string ScryfallLookupText
        {
            get => _scryfallLookupText;
            set => SetProperty(ref _scryfallLookupText, value);
        }

        public string ScryfallLookupStatus
        {
            get => _scryfallLookupStatus;
            set => SetProperty(ref _scryfallLookupStatus, value);
        }

        public string ProjectName
        {
            get => _currentProject.ProjectName;
            set { _currentProject.ProjectName = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ObservableCollection<PrintMode> PrintModeValues { get; }

        public PrintMode SelectedPrintMode
        {
            get => _currentProject.PrintSettings.PrintMode;
            set { _currentProject.PrintSettings.PrintMode = value; OnPropertyChanged(); }
        }

        // Card outline enum bindings
        public ObservableCollection<OutlineAlignment> OutlineAlignmentOptions { get; } = new(Enum.GetValues<OutlineAlignment>());
        public ObservableCollection<OutlineType> OutlineTypeOptions { get; } = new(Enum.GetValues<OutlineType>());
        public ObservableCollection<LineType> LineTypeOptions { get; } = new(Enum.GetValues<LineType>());

        public OutlineAlignment SelectedOutlineAlignment
        {
            get => _currentProject.PrintSettings.OutlineAlignment;
            set { _currentProject.PrintSettings.OutlineAlignment = value; OnPropertyChanged(); }
        }

        public OutlineType SelectedOutlineType
        {
            get => _currentProject.PrintSettings.OutlineType;
            set { _currentProject.PrintSettings.OutlineType = value; OnPropertyChanged(); }
        }

        public LineType SelectedLineType
        {
            get => _currentProject.PrintSettings.OutlineLineType;
            set { _currentProject.PrintSettings.OutlineLineType = value; OnPropertyChanged(); }
        }

        // Scryfall
        public string ScryfallSearchQuery
        {
            get => _scryfallSearchQuery;
            set => SetProperty(ref _scryfallSearchQuery, value);
        }

        public ObservableCollection<ScryfallCard> ScryfallResults
        {
            get => _scryfallResults;
            set => SetProperty(ref _scryfallResults, value);
        }

        public ScryfallCard? SelectedScryfallCard
        {
            get => _selectedScryfallCard;
            set { SetProperty(ref _selectedScryfallCard, value); SearchPreviewUrl = value?.GetImageUrl("normal"); }
        }

        public bool IsSearching
        {
            get => _isSearching;
            set => SetProperty(ref _isSearching, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string BusyMessage
        {
            get => _busyMessage;
            set => SetProperty(ref _busyMessage, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        private void MarkDirty() => HasUnsavedChanges = true;

        // --- Update check ---
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set => SetProperty(ref _updateAvailable, value);
        }

        public string UpdateMessage
        {
            get => _updateMessage;
            set => SetProperty(ref _updateMessage, value);
        }

        public string UpdateDownloadUrl
        {
            get => _updateDownloadUrl;
            set => SetProperty(ref _updateDownloadUrl, value);
        }

        public ICommand DownloadUpdateCommand { get; private set; } = null!;
        public ICommand DismissUpdateCommand { get; private set; } = null!;
        public ICommand OpenSettingsCommand { get; private set; } = null!;

        public string AppVersion { get; } = GetAppVersion();

        public static string GetAppVersion()
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            if (asm == null) return "dev";
            var attrs = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            if (attrs.Length > 0 && attrs[0] is System.Reflection.AssemblyInformationalVersionAttribute attr)
                return attr.InformationalVersion?.Split('+')[0] ?? "dev";
            return asm.GetName().Version?.ToString(3) ?? "dev";
        }

        private int _refreshTrigger;
        public int RefreshTrigger
        {
            get => _refreshTrigger;
            set => SetProperty(ref _refreshTrigger, value);
        }

        /// <summary>Forces the canvas to re-render.</summary>
        private void RefreshCanvas() => RefreshTrigger++;

        // Back art library
        public ObservableCollection<BackArtEntry> BackArtLibrary
        {
            get => _backArtLibrary;
            set => SetProperty(ref _backArtLibrary, value);
        }

        public BackArtEntry? SelectedBackArt
        {
            get => _selectedBackArt;
            set => SetProperty(ref _selectedBackArt, value);
        }

        // --- Sort and Filter ---

        public ObservableCollection<CardModel> FilteredCards
        {
            get => _filteredCards;
            set => SetProperty(ref _filteredCards, value);
        }

        public string FilterText
        {
            get => _filterText;
            set { if (SetProperty(ref _filterText, value)) ApplyFilterAndSort(); }
        }

        public string FilterRarity
        {
            get => _filterRarity;
            set { if (SetProperty(ref _filterRarity, value)) ApplyFilterAndSort(); }
        }

        public string FilterColor
        {
            get => _filterColor;
            set { if (SetProperty(ref _filterColor, value)) ApplyFilterAndSort(); }
        }

        public string SortBy
        {
            get => _sortBy;
            set { if (SetProperty(ref _sortBy, value)) ApplyFilterAndSort(); }
        }

        public bool SortDescending
        {
            get => _sortDescending;
            set { if (SetProperty(ref _sortDescending, value)) ApplyFilterAndSort(); }
        }

        public ObservableCollection<string> SortOptions { get; } = new()
        {
            "Date Added", "Name", "CMC", "Rarity", "Color", "Type", "Set", "Artist", "Collector #"
        };

        public ObservableCollection<string> RarityOptions { get; } = new()
        {
            "All", "common", "uncommon", "rare", "mythic"
        };

        public ObservableCollection<string> ColorOptions { get; } = new()
        {
            "All", "White", "Blue", "Black", "Red", "Green", "Colorless", "Multicolor"
        };

        public ICommand ApplySortToProjectCommand { get; private set; } = null!;
        public ICommand ClearFilterCommand { get; private set; } = null!;
        public ICommand ImportDeckCommand { get; }
        public ICommand RefreshDeckCommand { get; }
        public ICommand AddMpcFillCardCommand { get; }
        public ICommand ClearAllCardsCommand { get; }
        public ICommand UpdateAllArtFromMpcFillCommand { get; }

        public string ImportDeckUrl
        {
            get => _importDeckUrl;
            set => SetProperty(ref _importDeckUrl, value);
        }

        public bool IgnoreDuplicates
        {
            get => _ignoreDuplicates;
            set => SetProperty(ref _ignoreDuplicates, value);
        }

        // Art source toggle
        public bool UseMpcFill
        {
            get => _useMpcFill;
            set => SetProperty(ref _useMpcFill, value);
        }

        public ObservableCollection<MpcFillCard> MpcFillResults
        {
            get => _mpcFillResults;
            set => SetProperty(ref _mpcFillResults, value);
        }

        public MpcFillCard? SelectedMpcFillCard
        {
            get => _selectedMpcFillCard;
            set { SetProperty(ref _selectedMpcFillCard, value); SearchPreviewUrl = value?.MediumThumbnailUrl; }
        }

        public string? SearchPreviewUrl
        {
            get => _searchPreviewUrl;
            set => SetProperty(ref _searchPreviewUrl, value);
        }

        // MPCFill advanced search
        public string MpcAdvName { get => _mpcAdvName; set => SetProperty(ref _mpcAdvName, value); }
        public int MpcAdvMinDpi { get => _mpcAdvMinDpi; set => SetProperty(ref _mpcAdvMinDpi, value); }
        public bool MpcFuzzySearch { get => _mpcFuzzySearch; set => SetProperty(ref _mpcFuzzySearch, value); }
        public bool MpcUseFavoritesOnly
        {
            get => _mpcUseFavoritesOnly;
            set
            {
                if (SetProperty(ref _mpcUseFavoritesOnly, value))
                {
                    _appSettings.Settings.MpcFillUseFavoritesOnly = value;
                    _appSettings.Save();
                }
            }
        }
        public ObservableCollection<int> MpcDpiOptions { get; } = new() { 0, 300, 600, 800, 1200 };
        public MpcFillSourceManager MpcSourceManager { get; }
        public ObservableCollection<MpcFillSource> MpcSourceList { get; } = new();

        public ICommand LoadMpcSourcesCommand { get; private set; } = null!;
        public ICommand ToggleMpcFavoriteFromResultCommand { get; private set; } = null!;
        public ICommand ManageMpcSourcesCommand { get; private set; } = null!;
        public ICommand ImportMpcFillXmlCommand { get; private set; } = null!;
        public ICommand ClearCacheCommand { get; private set; } = null!;
        public ICommand ManageBackArtLibraryCommand { get; private set; } = null!;
        public ICommand ManageFrontArtLibraryCommand { get; private set; } = null!;

        public string CacheSizeText
        {
            get
            {
                var size = _cacheManager.GetTotalCacheSizeBytes();
                return $"Cache: {CacheManager.FormatBytes(size)}";
            }
        }

        // --- Advanced Search ---
        public bool ShowAdvancedSearch { get => _showAdvancedSearch; set => SetProperty(ref _showAdvancedSearch, value); }
        public string AdvName { get => _advName; set => SetProperty(ref _advName, value); }
        public string AdvType { get => _advType; set => SetProperty(ref _advType, value); }
        public string AdvOracle { get => _advOracle; set => SetProperty(ref _advOracle, value); }
        public string AdvColors { get => _advColors; set => SetProperty(ref _advColors, value); }
        public string AdvIdentity { get => _advIdentity; set => SetProperty(ref _advIdentity, value); }
        public string AdvCmcOp { get => _advCmcOp; set => SetProperty(ref _advCmcOp, value); }
        public string AdvCmcValue { get => _advCmcValue; set => SetProperty(ref _advCmcValue, value); }
        public string AdvRarity { get => _advRarity; set => SetProperty(ref _advRarity, value); }
        public string AdvSet { get => _advSet; set => SetProperty(ref _advSet, value); }
        public string AdvFormat { get => _advFormat; set => SetProperty(ref _advFormat, value); }
        public string AdvPowOp { get => _advPowOp; set => SetProperty(ref _advPowOp, value); }
        public string AdvPowValue { get => _advPowValue; set => SetProperty(ref _advPowValue, value); }
        public string AdvTouOp { get => _advTouOp; set => SetProperty(ref _advTouOp, value); }
        public string AdvTouValue { get => _advTouValue; set => SetProperty(ref _advTouValue, value); }
        public string AdvArtist { get => _advArtist; set => SetProperty(ref _advArtist, value); }
        public string AdvKeyword { get => _advKeyword; set => SetProperty(ref _advKeyword, value); }
        public string AdvIs { get => _advIs; set => SetProperty(ref _advIs, value); }

        public ObservableCollection<string> ComparisonOps { get; } = new() { "=", "!=", "<", ">", "<=", ">=" };
        public ObservableCollection<string> RarityAdvOptions { get; } = new() { "", "common", "uncommon", "rare", "mythic" };
        public ObservableCollection<string> FormatOptions { get; } = new()
        {
            "", "standard", "pioneer", "modern", "legacy", "vintage", "pauper",
            "commander", "brawl", "historic", "explorer", "timeless", "oathbreaker"
        };
        public ObservableCollection<string> IsOptions { get; } = new()
        {
            "", "reprint", "full", "foil", "etched", "promo", "booster",
            "commander", "companion", "reserved", "vanilla", "funny",
            "transform", "mdfc", "split", "flip", "dfc",
            "fetchland", "shockland", "dual", "checkland", "painland"
        };

        public ICommand BuildAdvancedQueryCommand { get; private set; } = null!;
        public ICommand ClearAdvancedSearchCommand { get; private set; } = null!;

        private string BuildAdvancedQuery() => AdvancedQueryBuilder.Build(
            _advName, _advType, _advOracle, _advColors, _advIdentity,
            _advCmcOp, _advCmcValue, _advRarity, _advSet, _advFormat,
            _advPowOp, _advPowValue, _advTouOp, _advTouValue,
            _advArtist, _advKeyword, _advIs);

        private void ApplyAdvancedQuery()
        {
            ScryfallSearchQuery = BuildAdvancedQuery();
            if (!string.IsNullOrWhiteSpace(ScryfallSearchQuery) && ScryfallSearchCommand.CanExecute(null))
                ScryfallSearchCommand.Execute(null);
        }

        private void ClearAdvancedSearch()
        {
            AdvName = AdvType = AdvOracle = AdvColors = AdvIdentity = string.Empty;
            AdvCmcOp = AdvPowOp = AdvTouOp = "=";
            AdvCmcValue = AdvRarity = AdvSet = AdvFormat = string.Empty;
            AdvPowValue = AdvTouValue = AdvArtist = AdvKeyword = AdvIs = string.Empty;
            ScryfallSearchQuery = string.Empty;
        }

        // Card size presets
        public ObservableCollection<CardSizePreset> CardSizePresets { get; } =
            new(CardSizePreset.BuiltInPresets);

        public CardSizePreset? SelectedCardSize
        {
            get => _selectedCardSize;
            set
            {
                if (SetProperty(ref _selectedCardSize, value) && value != null)
                {
                    _currentProject.PageSettings.CardWidthMm = value.WidthMm;
                    _currentProject.PageSettings.CardHeightMm = value.HeightMm;
                    StatusText = $"Card size: {value.Name} ({value.WidthMm} x {value.HeightMm} mm)";
                }
            }
        }

        // Page layout
        public ObservableCollection<string> PagePresets { get; }

        public string SelectedPagePreset
        {
            get => _selectedPagePreset;
            set
            {
                if (SetProperty(ref _selectedPagePreset, value) && value != null)
                {
                    _currentProject.PageSettings.ApplyPagePreset(value);
                    OnPropertyChanged(nameof(IsCustomPageSize));
                    if (value == "Custom")
                    {
                        // Initialize custom fields with current dimensions
                        _customPageWidth = $"{_currentProject.PageSettings.PageWidthMm:0.#} mm";
                        _customPageHeight = $"{_currentProject.PageSettings.PageHeightMm:0.#} mm";
                        OnPropertyChanged(nameof(CustomPageWidth));
                        OnPropertyChanged(nameof(CustomPageHeight));
                    }
                }
            }
        }

        public bool IsCustomPageSize => _selectedPagePreset == "Custom";

        private string _customPageWidth = "";
        private string _customPageHeight = "";

        public string CustomPageWidth
        {
            get => _customPageWidth;
            set
            {
                if (SetProperty(ref _customPageWidth, value))
                    ApplyCustomDimension(value, isWidth: true);
            }
        }

        public string CustomPageHeight
        {
            get => _customPageHeight;
            set
            {
                if (SetProperty(ref _customPageHeight, value))
                    ApplyCustomDimension(value, isWidth: false);
            }
        }

        /// <summary>
        /// Parses a dimension string with optional unit suffix and applies it.
        /// Supports: "210 mm", "210mm", "8.5\"", "8.5 in", "8.5in", or plain number (treated as mm).
        /// </summary>
        private void ApplyCustomDimension(string input, bool isWidth)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            input = input.Trim();

            float valueMm;
            if (input.EndsWith("\"") || input.EndsWith("in", StringComparison.OrdinalIgnoreCase))
            {
                // Inches
                string numPart = input.TrimEnd('"').TrimEnd();
                if (numPart.EndsWith("in", StringComparison.OrdinalIgnoreCase))
                    numPart = numPart[..^2].TrimEnd();
                if (!float.TryParse(numPart, System.Globalization.CultureInfo.InvariantCulture, out float inches)) return;
                valueMm = inches * 25.4f;
            }
            else
            {
                // Millimeters (with or without "mm" suffix)
                string numPart = input;
                if (numPart.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                    numPart = numPart[..^2].TrimEnd();
                if (!float.TryParse(numPart, System.Globalization.CultureInfo.InvariantCulture, out valueMm)) return;
            }

            if (valueMm <= 0) return;

            if (isWidth)
                _currentProject.PageSettings.PageWidthMm = valueMm;
            else
                _currentProject.PageSettings.PageHeightMm = valueMm;
        }

        // --- Commands ---

        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public UndoService UndoServiceInstance => _undoService;
        public ICommand SaveProjectAsCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand AddCardFromFileCommand { get; }
        public ICommand RemoveCardCommand { get; }
        public ICommand BrowseFrontArtworkCommand { get; }
        public ICommand BrowseBackArtworkCommand { get; }
        public ICommand FetchScryfallDataCommand { get; }
        public ICommand SelectBackArtForAllCommand { get; }
        public ICommand ScryfallSearchCommand { get; }
        public ICommand AddScryfallCardCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportSvgCommand { get; }
        public ICommand AddBackArtToLibraryCommand { get; }
        public ICommand RemoveBackArtFromLibraryCommand { get; }
        public ICommand ApplyBackArtToSelectedCommand { get; }
        public ICommand ApplyBackArtToAllCommand { get; }
        public ICommand ClearBackArtFromAllCommand { get; }
        public ICommand SetPagePresetCommand { get; }
        public ICommand ToggleLandscapeCommand { get; }

        // --- Undo / Redo ---

        private void PushUndo() { _undoService.SaveState(Cards); MarkDirty(); }

        private void Undo()
        {
            var restored = _undoService.Undo(Cards);
            if (restored != null) RestoreCards(restored);
        }

        private void Redo()
        {
            var restored = _undoService.Redo(Cards);
            if (restored != null) RestoreCards(restored);
        }

        private void RestoreCards(List<CardModel> cards)
        {
            Cards.CollectionChanged -= OnCardsCollectionChanged;
            Cards.Clear();
            foreach (var c in cards) Cards.Add(c);
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
            SelectedCard = null;
            StatusText = "Undo/Redo applied";
        }

        // --- Command Implementations ---

        private void NewProject()
        {
            PushUndo();
            _undoService.Clear();
            _currentProject = new ProjectModel();
            _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
            Cards.Clear();
            _currentFilePath = null;
            SelectedCard = null;
            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(SelectedPrintMode));
            HasUnsavedChanges = false;
            StatusText = "New project created";
        }

        /// <summary>Load a pre-parsed project into this ViewModel (used by ShellViewModel).</summary>
        public void LoadFromProject(ProjectModel project, string filePath)
        {
            _currentProject = project;
            _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
            _currentFilePath = filePath;
            Cards = new ObservableCollection<CardModel>(project.Cards);
            SelectedCard = null;
            _selectedPagePreset = DetectPagePreset(project.PageSettings);
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(ProjectName));
            OnPropertyChanged(nameof(SelectedPagePreset));
            OnPropertyChanged(nameof(SelectedPrintMode));
            OnPropertyChanged(nameof(SelectedOutlineAlignment));
            OnPropertyChanged(nameof(SelectedOutlineType));
            OnPropertyChanged(nameof(SelectedLineType));
        }

        private async void OpenProject()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "MTG Project Files (*.mtgproj)|*.mtgproj|All Files (*.*)|*.*",
                Title = "Open Project"
            };

            if (dialog.ShowDialog() != true) return;

            SetBusy("Opening project...");
            try
            {
                var project = await _serializationService.LoadProjectAsync(dialog.FileName);
                if (project == null)
                {
                    MessageBox.Show("Failed to load project file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _currentProject = project;
                _currentProject.PageSettings.PropertyChanged += OnPageSettingsChanged;
                _currentFilePath = dialog.FileName;
                Cards = new ObservableCollection<CardModel>(project.Cards);
                SelectedCard = null;
                _selectedPagePreset = DetectPagePreset(project.PageSettings);
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(SelectedPagePreset));
                OnPropertyChanged(nameof(SelectedPrintMode));
                HasUnsavedChanges = false;
                StatusText = $"Opened: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening project:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void SaveProject()
        {
            if (_currentFilePath == null)
            {
                SaveProjectAs();
                return;
            }

            SyncCardsToProject();
            SetBusy("Saving project...");
            try
            {
                bool success = await _serializationService.SaveProjectAsync(_currentProject, _currentFilePath);
                if (success) HasUnsavedChanges = false;
                StatusText = success ? "Project saved" : "Failed to save project";
            }
            finally { ClearBusy(); }
        }

        private async void SaveProjectAs()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "MTG Project Files (*.mtgproj)|*.mtgproj",
                Title = "Save Project As",
                FileName = $"{ProjectName}.mtgproj"
            };

            if (dialog.ShowDialog() != true) return;

            _currentFilePath = dialog.FileName;
            SyncCardsToProject();
            SetBusy("Saving project...");
            try
            {
                bool success = await _serializationService.SaveProjectAsync(_currentProject, _currentFilePath);
                if (success) HasUnsavedChanges = false;
                StatusText = success ? $"Saved: {Path.GetFileName(dialog.FileName)}" : "Failed to save project";
            }
            finally { ClearBusy(); }
        }

        private async void AddCardFromFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Select Card Artwork",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true) return;

            PushUndo();
            int count = dialog.FileNames.Length;
            SetBusy($"Loading {count} image(s)...");
            await Task.Delay(50);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            foreach (var filePath in dialog.FileNames)
            {
                var card = new CardModel
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    ArtworkPath = filePath
                };
                ApplyDefaultBackArt(card);
                Cards.Add(card);
            }
            Cards.CollectionChanged += OnCardsCollectionChanged;
            _currentProject.PageSettings.CenterGrid();
            ApplyFilterAndSort();
            StatusText = $"Added {count} card(s)";
            ClearBusy();
        }

        private void RemoveCard()
        {
            if (SelectedCard == null) return;
            PushUndo();
            Cards.Remove(SelectedCard);
            SelectedCard = null;
            StatusText = "Card removed";
        }

        private async Task FetchScryfallData()
        {
            if (SelectedCard == null) return;
            var searchName = string.IsNullOrWhiteSpace(ScryfallLookupText) ? SelectedCard.Name : ScryfallLookupText.Trim();
            if (string.IsNullOrWhiteSpace(searchName))
            {
                ScryfallLookupStatus = "Enter a card name to search.";
                return;
            }

            ScryfallLookupStatus = "Searching Scryfall...";
            var sc = await _scryfallService.GetCardByNameAsync(searchName);
            if (sc == null)
            {
                ScryfallLookupStatus = $"No card found for \"{searchName}\".";
                return;
            }

            var card = SelectedCard;
            card.ScryfallId = sc.Id;
            card.Name = sc.Name;
            card.ManaCost = sc.ManaCost ?? sc.CardFaces?.FirstOrDefault()?.ManaCost ?? string.Empty;
            card.CMC = sc.CMC;
            card.TypeLine = sc.TypeLine ?? sc.CardFaces?.FirstOrDefault()?.TypeLine ?? string.Empty;
            card.OracleText = sc.OracleText ?? sc.CardFaces?.FirstOrDefault()?.OracleText ?? string.Empty;
            card.Rarity = sc.Rarity ?? string.Empty;
            card.Colors = sc.Colors != null ? string.Join(",", sc.Colors) : string.Empty;
            card.ColorIdentity = sc.ColorIdentity != null ? string.Join(",", sc.ColorIdentity) : string.Empty;
            card.SetCode = sc.SetCode;
            card.SetName = sc.SetName;
            card.CollectorNumber = sc.CollectorNumber;
            card.Artist = sc.Artist ?? string.Empty;
            card.Power = sc.Power ?? sc.CardFaces?.FirstOrDefault()?.Power ?? string.Empty;
            card.Toughness = sc.Toughness ?? sc.CardFaces?.FirstOrDefault()?.Toughness ?? string.Empty;
            card.Loyalty = sc.Loyalty ?? sc.CardFaces?.FirstOrDefault()?.Loyalty ?? string.Empty;
            card.Keywords = sc.Keywords != null ? string.Join(",", sc.Keywords) : string.Empty;
            card.IsDoubleFaced = sc.GetBackImageUrl() != null;

            ScryfallLookupStatus = $"Found: {sc.Name} ({sc.SetName})";
            StatusText = $"Scryfall data loaded for {sc.Name}";
        }

        private void BrowseFrontArtwork()
        {
            if (SelectedCard == null) return;
            ShowArtSelector(SelectedCard, Dialogs.ArtSelectorMode.Front);
        }

        private void BrowseBackArtwork()
        {
            if (SelectedCard == null) return;
            ShowArtSelector(SelectedCard, Dialogs.ArtSelectorMode.Back);
        }

        public void OpenArtSelectorForCard(CardModel card, bool isShowingBack)
        {
            SelectedCard = card;
            ShowArtSelector(card, isShowingBack ? Dialogs.ArtSelectorMode.Back : Dialogs.ArtSelectorMode.Front);
        }

        public void SelectFrontArtForCards(List<int> cardIndices)
        {
            var targets = cardIndices
                .Where(i => i >= 0 && i < Cards.Count)
                .Select(i => Cards[i])
                .Distinct()
                .ToList();
            if (targets.Count == 0) return;

            // Use the first card for the art selector dialog
            var dialog = new Dialogs.ArtSelectorDialog(
                targets.First(), Dialogs.ArtSelectorMode.Front,
                _scryfallService, _mpcFillService, _imageCacheService,
                _backArtLibraryService, Cards, GetMpcFillSources(), BuildMpcFillSearchOptions(),
                _frontArtLibraryService);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ResultPath != null)
            {
                PushUndo();
                if (dialog.ResultMode == Dialogs.ArtSelectorMode.Front)
                {
                    foreach (var c in targets)
                        c.ArtworkPath = dialog.ResultPath;
                    StatusText = $"Front art updated for {targets.Count} card(s)";
                }
                else
                {
                    foreach (var c in targets)
                    {
                        c.BackArtworkPath = dialog.ResultPath;
                        c.IncludeBack = true;
                    }
                    StatusText = $"Back art applied to {targets.Count} card(s)";
                    RefreshBackArtLibrary();
                }
                RefreshCanvas();
            }
        }

        public void SelectBackArtForCards(List<int> cardIndices)
        {
            var targets = cardIndices
                .Where(i => i >= 0 && i < Cards.Count)
                .Select(i => Cards[i])
                .Distinct()
                .ToList();
            if (targets.Count == 0) return;

            var dialog = new Dialogs.ArtSelectorDialog(
                targets.First(), Dialogs.ArtSelectorMode.Back,
                _scryfallService, _mpcFillService, _imageCacheService,
                _backArtLibraryService, Cards, GetMpcFillSources(), BuildMpcFillSearchOptions(),
                _frontArtLibraryService);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ResultPath != null)
            {
                PushUndo();
                if (dialog.ResultMode == Dialogs.ArtSelectorMode.Back)
                {
                    foreach (var c in targets)
                    {
                        c.BackArtworkPath = dialog.ResultPath;
                        c.IncludeBack = true;
                    }
                    StatusText = $"Back art applied to {targets.Count} card(s)";
                    RefreshBackArtLibrary();
                }
                else
                {
                    foreach (var c in targets)
                        c.ArtworkPath = dialog.ResultPath;
                    StatusText = $"Front art updated for {targets.Count} card(s)";
                }
                RefreshCanvas();
            }
        }

        public void ApplyMajorityBackToCards(List<int> cardIndices)
        {
            var mostCommon = GetMostCommonBackArt();
            if (mostCommon == null)
            {
                MessageBox.Show("No cards in the project have back art assigned.", "No Back Art",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PushUndo();
            int count = 0;
            foreach (var idx in cardIndices)
            {
                if (idx >= 0 && idx < Cards.Count)
                {
                    Cards[idx].BackArtworkPath = mostCommon;
                    Cards[idx].IncludeBack = true;
                    count++;
                }
            }
            StatusText = $"Applied back art to {count} card(s)";
            RefreshCanvas();
        }

        public void CreateTokenFromCard(CardModel sourceCard)
        {
            CreateTokensFromCards(new List<CardModel> { sourceCard });
        }

        public void CreateTokensFromCards(List<CardModel> sourceCards)
        {
            string? commonBack = GetMostCommonBackArt();
            var eligibleCards = sourceCards.Where(c => IsEligibleForToken(c, commonBack)).ToList();

            if (eligibleCards.Count == 0)
            {
                MessageBox.Show("None of the selected cards have unique back artwork different from the project's common back.",
                    "No Tokens to Create", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PushUndo();
            string overlayText = _appSettings.Settings.DefaultTokenText;

            foreach (var source in eligibleCards)
            {
                var token = new CardModel
                {
                    Name = source.Name + " (Token)",
                    ArtworkPath = source.ArtworkPath,
                    Quantity = 1,
                    OverlayText = overlayText,
                    ManaCost = source.ManaCost,
                    TypeLine = source.TypeLine,
                    SetCode = source.SetCode,
                    SetName = source.SetName,
                    DateAdded = DateTime.Now
                };

                if (commonBack != null)
                {
                    token.BackArtworkPath = commonBack;
                    token.IncludeBack = true;
                }
                else
                {
                    ApplyDefaultBackArt(token);
                }

                Cards.Add(token);
            }

            ApplyFilterAndSort();
            StatusText = $"Created {eligibleCards.Count} token card(s)";
        }

        /// <summary>
        /// A card is eligible for token creation if it has back art that differs
        /// from the project's most common back art (i.e. it's a dual-faced card).
        /// </summary>
        private bool IsEligibleForToken(CardModel card, string? commonBack)
        {
            // Must have some back art
            string? back = card.BackArtworkPath ?? card.OriginalBackArtworkPath;
            if (string.IsNullOrEmpty(back)) return false;

            // Back art must differ from the project's common back
            if (commonBack != null && string.Equals(back, commonBack, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Finds the most frequently used back art path across all cards in the project.
        /// Returns null if no cards have back art assigned.
        /// </summary>
        private string? GetMostCommonBackArt()
        {
            var backPaths = Cards
                .Where(c => !string.IsNullOrEmpty(c.BackArtworkPath))
                .GroupBy(c => c.BackArtworkPath!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(c => c.Quantity))
                .FirstOrDefault();

            return backPaths?.Key;
        }

        private void ShowArtSelector(CardModel card, Dialogs.ArtSelectorMode initialMode)
        {
            Log.Information("Art selector opened for {CardName} ({Mode})", card.Name, initialMode);
            var dialog = new Dialogs.ArtSelectorDialog(
                card, initialMode, _scryfallService, _mpcFillService, _imageCacheService,
                _backArtLibraryService, Cards, GetMpcFillSources(), BuildMpcFillSearchOptions(),
                _frontArtLibraryService);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ResultPath != null)
            {
                PushUndo();
                if (dialog.ResultMode == Dialogs.ArtSelectorMode.Front)
                {
                    if (dialog.ApplyToSameName)
                    {
                        int count = 0;
                        foreach (var c in Cards.Where(c => c.Name == card.Name))
                        {
                            c.ArtworkPath = dialog.ResultPath;
                            count++;
                        }
                        StatusText = $"Front art updated for {count} \"{card.Name}\" card(s)";
                    }
                    else
                    {
                        card.ArtworkPath = dialog.ResultPath;
                        StatusText = $"Front art updated for {card.Name}";
                    }
                }
                else
                {
                    if (dialog.ApplyToNoBack)
                    {
                        int count = 0;
                        foreach (var c in Cards.Where(c => string.IsNullOrEmpty(c.BackArtworkPath)))
                        {
                            c.BackArtworkPath = dialog.ResultPath;
                            c.IncludeBack = true;
                            count++;
                        }
                        StatusText = $"Back art applied to {count} card(s) without back art";
                    }
                    else
                    {
                        card.BackArtworkPath = dialog.ResultPath;
                        card.IncludeBack = true;
                        StatusText = $"Back art updated for {card.Name}";
                    }
                }
                RefreshBackArtLibrary();
                RefreshCanvas();
            }
        }

        private void SelectBackArtForAll()
        {
            if (Cards.Count == 0) return;
            ShowBackArtSelector(Cards.ToList());
        }

        private void ShowBackArtSelector(List<CardModel> targetCards)
        {
            var dialog = new Dialogs.ArtSelectorDialog(
                targetCards.First(), Dialogs.ArtSelectorMode.Back,
                _scryfallService, _mpcFillService, _imageCacheService,
                _backArtLibraryService, Cards, GetMpcFillSources(), BuildMpcFillSearchOptions(),
                _frontArtLibraryService);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ResultPath != null)
            {
                PushUndo();
                if (dialog.ResultMode == Dialogs.ArtSelectorMode.Back)
                {
                    var targets = dialog.ApplyToNoBack
                        ? Cards.Where(c => string.IsNullOrEmpty(c.BackArtworkPath)).ToList()
                        : targetCards;

                    foreach (var c in targets)
                    {
                        c.BackArtworkPath = dialog.ResultPath;
                        c.IncludeBack = true;
                    }
                    StatusText = $"Back art applied to {targets.Count} card(s)";
                    RefreshBackArtLibrary();
                }
                else
                {
                    foreach (var c in targetCards)
                        c.ArtworkPath = dialog.ResultPath;
                    StatusText = $"Front art updated for {targetCards.Count} card(s)";
                }
                RefreshCanvas();
            }
        }

        private string? BrowseImageFile(string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = title
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private async void ScryfallSearch()
        {
            if (string.IsNullOrWhiteSpace(ScryfallSearchQuery)) return;

            IsSearching = true;
            await SearchScryfall();
            IsSearching = false;
            ClearBusy();
        }

        private async Task SearchScryfall()
        {
            SetBusy("Searching Scryfall...");
            try
            {
                var (results, error) = await _searchCoordinator.SearchScryfallAsync(ScryfallSearchQuery);
                if (error != null)
                {
                    ScryfallResults.Clear();
                    StatusText = error;
                    MessageBox.Show(error, "Search Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    ScryfallResults = new ObservableCollection<ScryfallCard>(results);
                    MpcFillResults.Clear();
                    StatusText = $"Found {results.Count} result(s) on Scryfall";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Search failed: {ex.Message}";
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SearchMpcFill()
        {
            SetBusy("Searching MPCFill...");
            try
            {
                var (results, error) = await _searchCoordinator.SearchMpcFillAsync(
                    ScryfallSearchQuery, MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly, MpcAdvName);
                if (error != null)
                {
                    MpcFillResults.Clear();
                    StatusText = error;
                    MessageBox.Show(error, "MPCFill Search Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MpcFillResults = new ObservableCollection<MpcFillCard>(results);
                    ScryfallResults.Clear();

                    string favInfo = MpcUseFavoritesOnly && MpcSourceManager.HasFavorites
                        ? " (favorites only)" : "";
                    StatusText = $"Found {MpcFillResults.Count} art version(s) on MPCFill{favInfo}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Search failed: {ex.Message}";
            }
        }

        private MpcFillSearchOptions BuildMpcFillSearchOptions()
            => _searchCoordinator.BuildSearchOptions(MpcAdvMinDpi, MpcFuzzySearch);

        private object[][]? GetMpcFillSources()
            => _searchCoordinator.GetSources(MpcUseFavoritesOnly);

        private async void AddScryfallCard()
        {
            if (SelectedScryfallCard == null) return;

            SetBusy($"Downloading artwork for {SelectedScryfallCard.Name}...");
            Log.Information("Adding Scryfall card {Name} ({Set})", SelectedScryfallCard.Name, SelectedScryfallCard.SetName);

            try
            {
                var frontPath = await _searchCoordinator.DownloadScryfallArtAsync(SelectedScryfallCard);
                string? backPath = null;
                if (SelectedScryfallCard.GetBackImageUrl() != null)
                    backPath = await _searchCoordinator.DownloadScryfallArtAsync(SelectedScryfallCard, back: true);

                PushUndo();
                var card = SelectedScryfallCard.ToCardModel(frontPath ?? string.Empty, backPath);
                ApplyDefaultBackArt(card);
                Cards.Add(card);
                ApplyFilterAndSort();
                StatusText = $"Added: {card.Name} ({card.SetName})";
            }
            catch (Exception ex)
            {
                StatusText = $"Download failed: {ex.Message}";
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void ExportPdf()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Export PDF",
                FileName = $"{ProjectName}.pdf"
            };

            if (dialog.ShowDialog() != true) return;

            SyncCardsToProject();
            SetBusy("Generating PDF...");
            Log.Information("Exporting PDF to {Path} ({CardCount} cards)", dialog.FileName, Cards.Count);

            try
            {
                bool success = await _pdfGeneratorService.GeneratePdfAsync(_currentProject, dialog.FileName);

                if (success)
                {
                    string svgInfo = "";
                    if (_currentProject.PrintSettings.ExportSvgCutLines)
                    {
                        var svgService = new SvgCutLineService();
                        string outputDir = Path.GetDirectoryName(dialog.FileName) ?? ".";
                        string baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
                        var svgFiles = await svgService.GenerateSvgAsync(_currentProject, outputDir, baseName);
                        svgInfo = svgFiles.Count > 0
                            ? $"\n\nSVG cut files ({svgFiles.Count}):\n" + string.Join("\n", svgFiles.Select(Path.GetFileName))
                            : "";
                    }

                    StatusText = $"PDF exported: {Path.GetFileName(dialog.FileName)}";
                    MessageBox.Show($"PDF exported successfully!\n\n{dialog.FileName}{svgInfo}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusText = "PDF export failed";
                    MessageBox.Show("Failed to generate PDF. Check that card images exist.",
                        "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"PDF export failed: {ex.Message}";
                MessageBox.Show($"PDF generation error:\n{ex.Message}", "Export Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void ExportSvgOnly()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export SVG Cut Lines",
                Filter = "SVG Files|*.svg",
                FileName = $"{_currentProject.ProjectName}_cutlines"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                SetBusy("Generating SVG...");

                var svgService = new SvgCutLineService();
                string outputDir = Path.GetDirectoryName(dialog.FileName) ?? ".";
                string baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
                var svgFiles = await svgService.GenerateSvgAsync(_currentProject, outputDir, baseName);

                if (svgFiles.Count > 0)
                {
                    StatusText = $"SVG exported: {string.Join(", ", svgFiles.Select(Path.GetFileName))}";
                    MessageBox.Show($"SVG cut lines exported!\n\n{string.Join("\n", svgFiles)}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusText = "No SVG files generated";
                    MessageBox.Show("No cards to generate cut lines for.",
                        "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"SVG export failed: {ex.Message}";
                MessageBox.Show($"SVG generation error:\n{ex.Message}", "Export Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ClearBusy();
            }
        }

        // --- Back Art Library ---

        private void RefreshBackArtLibrary()
        {
            BackArtLibrary = new ObservableCollection<BackArtEntry>(_backArtLibraryService.Entries);
        }

        private async void AddBackArtToLibrary()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Title = "Add Back Art to Library"
            };

            if (dialog.ShowDialog() != true) return;

            SetBusy("Adding to back art library...");
            await Task.Delay(50);

            var entry = _backArtLibraryService.AddFromFile(dialog.FileName);
            if (entry != null)
            {
                RefreshBackArtLibrary();
                SelectedBackArt = BackArtLibrary.FirstOrDefault(e => e.Id == entry.Id);
                StatusText = $"Added '{entry.Name}' to back art library";
            }
            ClearBusy();
        }

        private void RemoveBackArtFromLibrary()
        {
            if (SelectedBackArt == null) return;

            var result = MessageBox.Show(
                $"Remove '{SelectedBackArt.Name}' from the library?\n\nThis will not remove it from cards that already use it.",
                "Remove Back Art", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _backArtLibraryService.Remove(SelectedBackArt.Id);
            RefreshBackArtLibrary();
            SelectedBackArt = null;
            StatusText = "Back art removed from library";
        }

        private void ApplyBackArtToSelected()
        {
            if (SelectedBackArt == null || SelectedCard == null) return;

            SelectedCard.BackArtworkPath = SelectedBackArt.FilePath;
            SelectedCard.IncludeBack = true;
            StatusText = $"Applied '{SelectedBackArt.Name}' to {SelectedCard.Name}";
        }

        private async void ApplyBackArtToAll()
        {
            if (SelectedBackArt == null || Cards.Count == 0) return;

            SetBusy($"Applying back art to {Cards.Count} card(s)...");
            await Task.Delay(50);

            foreach (var card in Cards)
            {
                card.BackArtworkPath = SelectedBackArt.FilePath;
                card.IncludeBack = true;
            }
            StatusText = $"Applied '{SelectedBackArt.Name}' to all {Cards.Count} card(s)";
            ClearBusy();
        }

        private async void ClearBackArtFromAll()
        {
            if (Cards.Count == 0) return;

            SetBusy("Clearing back art...");
            await Task.Delay(50);

            foreach (var card in Cards)
            {
                card.BackArtworkPath = null;
                card.IncludeBack = false;
            }
            StatusText = $"Cleared back art from all {Cards.Count} card(s)";
            ClearBusy();
        }

        // --- MPCFill ---

        private async void ManageMpcSources()
        {
            if (IsBusy) return; // prevent re-entry

            SetBusy("Loading MPCFill sources...");
            try
            {
                var error = await _mpcFillService.EnsureSourcesLoadedAsync();
                ClearBusy();

                if (error != null)
                {
                    MessageBox.Show($"Could not load sources from MPCFill:\n\n{error}",
                        "MPCFill Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusText = error;
                    return;
                }

                if (MpcSourceManager.AllSources.Count == 0)
                {
                    MessageBox.Show("No sources were returned from MPCFill.",
                        "MPCFill Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new Dialogs.MpcSourceManagerDialog(MpcSourceManager, _mpcFillService);
                dialog.Owner = Application.Current.MainWindow;
                dialog.ShowDialog();

                StatusText = $"MPCFill: {MpcSourceManager.FavoritePks.Count} favorite source(s)";
            }
            catch (Exception ex)
            {
                ClearBusy();
                MessageBox.Show($"Unexpected error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadMpcSources()
        {
            if (IsBusy) return;
            SetBusy("Loading MPCFill sources...");
            var error = await _mpcFillService.EnsureSourcesLoadedAsync();
            MpcSourceList.Clear();
            foreach (var s in MpcSourceManager.AllSources)
                MpcSourceList.Add(s);
            ClearBusy();
            StatusText = error ?? $"Loaded {MpcSourceList.Count} MPCFill sources ({MpcSourceManager.FavoritePks.Count} favorites)";
        }

        private void ToggleFavoriteFromResult(object? param)
        {
            // Can be called from result context menu with the source name
            string? sourceName = null;
            int sourcePk = -1;

            if (param is MpcFillCard card)
            {
                sourceName = card.Source;
                sourcePk = card.SourceId;
            }
            else if (param is string name)
            {
                sourceName = name;
                var src = MpcSourceManager.GetByName(name);
                if (src != null) sourcePk = src.Pk;
            }

            if (sourcePk <= 0) return;

            MpcSourceManager.ToggleFavorite(sourcePk);
            bool isFav = MpcSourceManager.IsFavorite(sourcePk);
            StatusText = isFav
                ? $"Added '{sourceName}' to MPCFill favorites"
                : $"Removed '{sourceName}' from MPCFill favorites";

            // Refresh the list to update star indicators
            foreach (var s in MpcSourceList)
                s.IsFavorite = MpcSourceManager.IsFavorite(s.Pk);
        }

        private async void AddMpcFillCard()
        {
            if (SelectedMpcFillCard == null) return;

            SetBusy($"Downloading art: {SelectedMpcFillCard.Name}...");
            try
            {
                var (card, _) = await _importCoordinator.AddMpcFillCardAsync(SelectedMpcFillCard);
                if (card != null)
                {
                    PushUndo();
                    ApplyDefaultBackArt(card);
                    Cards.Add(card);
                    ApplyFilterAndSort();
                    StatusText = $"Added: {card.Name} (from MPCFill, {SelectedMpcFillCard.Source})";
                }
            }
            catch (Exception ex) { StatusText = $"Download failed: {ex.Message}"; }
            finally { ClearBusy(); }
        }

        private void ClearAllCards()
        {
            if (Cards.Count == 0) return;
            var result = MessageBox.Show(
                $"Remove all {Cards.Count} card(s) from the project?",
                "Clear All Cards", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            PushUndo();
            Cards.Clear();
            ApplyFilterAndSort();
            StatusText = "All cards removed";
        }

        private async void UpdateAllArtFromMpcFill()
        {
            if (Cards.Count == 0) return;

            var result = MessageBox.Show(
                $"Search MPCFill for matching art for all {Cards.Count} card(s) and replace their front artwork?\n\n" +
                "This will use the first available MPCFill result for each card.",
                "Update All Art from MPCFill", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            PushUndo();
            SetBusy("Updating card art from MPCFill...");

            try
            {
                var (updated, failed) = await _importCoordinator.UpdateAllArtFromMpcFillAsync(
                    Cards, MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                    onProgress: msg => BusyMessage = msg);

                StatusText = $"Updated {updated} card(s) with MPCFill art" + (failed > 0 ? $", {failed} not found" : "");
                MessageBox.Show(
                    $"Updated {updated} card(s) with MPCFill art.\n{(failed > 0 ? $"{failed} card(s) had no matching art." : "")}",
                    "Update Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
            finally { ClearBusy(); }
        }

        // --- MPCFill XML Import ---

        private async void ImportMpcFillXml()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "MPCFill XML (*.xml)|*.xml|All Files (*.*)|*.*",
                Title = "Import MPCFill Project (cards.xml)"
            };
            if (dialog.ShowDialog() != true) return;

            SetBusy("Parsing MPCFill XML...");
            Log.Information("Importing MPCFill XML from {Path}", dialog.FileName);

            try
            {
                var (project, parseError) = _importCoordinator.ParseXml(dialog.FileName);
                if (project == null || parseError != null)
                {
                    ClearBusy();
                    MessageBox.Show($"Failed to parse XML:\n{parseError}", "Import Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int totalSlots = project.Fronts.Sum(c => c.Slots.Count);
                BusyMessage = $"Found {project.Fronts.Count} unique card(s), {totalSlots} total slots";
                await Task.Delay(500);

                PushUndo();

                var result = await _importCoordinator.ImportXmlCardsAsync(project,
                    onProgress: msg => BusyMessage = msg);

                // Apply default back art and batch-add
                BusyMessage = $"Adding {result.Cards.Count} cards to project...";
                await Task.Delay(50);

                Cards.CollectionChanged -= OnCardsCollectionChanged;
                foreach (var c in result.Cards)
                {
                    ApplyDefaultBackArt(c);
                    Cards.Add(c);
                }
                Cards.CollectionChanged += OnCardsCollectionChanged;

                _currentProject.PageSettings.CenterGrid();
                ApplyFilterAndSort();

                int totalAdded = result.Cards.Sum(c => c.Quantity);
                string summary = $"Imported {result.Downloaded} card(s) ({totalAdded} total) from MPCFill XML";
                if (result.Failed > 0) summary += $"\n{result.Failed} image(s) failed to download";
                StatusText = summary;

                MessageBox.Show(summary, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
                MessageBox.Show($"Import error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ClearBusy();
            }
        }

        // --- Deck Import (Moxfield / Archidekt) ---

        private async void ImportDeck()
        {
            var source = DeckImportService.DetectSource(ImportDeckUrl);
            if (source == DeckSource.Unknown)
            {
                MessageBox.Show(
                    "Unrecognized URL. Paste a deck URL from:\n\n" +
                    "- Moxfield (moxfield.com/decks/...)\n" +
                    "- Archidekt (archidekt.com/decks/...)",
                    "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sourceName = source.ToString();
            SetBusy($"Connecting to {sourceName}...");
            Log.Information("Importing deck from {Url} (source: {Source})", ImportDeckUrl, sourceName);

            try
            {
                BusyMessage = $"Fetching deck list from {sourceName}...";
                await Task.Delay(50);

                var (fetchedDeck, error) = await _importCoordinator.FetchDeckAsync(ImportDeckUrl);
                if (fetchedDeck is not { } deck || error != null)
                {
                    ClearBusy();
                    MessageBox.Show($"Failed to fetch deck:\n{error}", $"{sourceName} Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PushUndo();
                int uniqueCards = deck.Entries.Count;
                int totalQty = deck.Entries.Sum(e => e.Quantity);
                BusyMessage = $"Found deck: {deck.Name}\n{uniqueCards} unique cards, {totalQty} total ({deck.Format})";
                await Task.Delay(800);

                var result = await _importCoordinator.ImportDeckCardsAsync(
                    deck, Cards, IgnoreDuplicates, UseMpcFill,
                    MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                    onProgress: msg => BusyMessage = msg);

                // Batch-add with default back art
                BusyMessage = $"Adding {result.Cards.Count} cards to project...";
                await Task.Delay(50);

                Cards.CollectionChanged -= OnCardsCollectionChanged;
                foreach (var c in result.Cards)
                {
                    ApplyDefaultBackArt(c);
                    Cards.Add(c);
                }
                Cards.CollectionChanged += OnCardsCollectionChanged;

                _currentProject.PageSettings.CenterGrid();
                ApplyFilterAndSort();

                _currentProject.DeckImportUrl = ImportDeckUrl;
                ImportDeckUrl = string.Empty;

                int totalAdded = result.Cards.Sum(c => c.Quantity);
                string summary = $"Imported {result.Cards.Count} unique card(s) ({totalAdded} total) from \"{deck.Name}\" ({sourceName})";
                if (result.SkippedDupes > 0) summary += $"\n{result.SkippedDupes} duplicate(s) skipped";
                if (result.Failed > 0) summary += $"\n{result.Failed} card(s) could not be found on Scryfall";
                StatusText = summary;

                MessageBox.Show(summary, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
                MessageBox.Show($"Import error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void RefreshDeck()
        {
            string? url = _currentProject.DeckImportUrl;
            if (string.IsNullOrEmpty(url)) return;

            var source = DeckImportService.DetectSource(url);
            if (source == DeckSource.Unknown)
            {
                StatusText = "Stored deck URL is not recognized.";
                return;
            }

            var result = MessageBox.Show(
                $"Re-import deck from {source}?\n\nThis will clear all current cards and re-download from:\n{url}",
                "Refresh Deck", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            string sourceName = source.ToString();
            SetBusy($"Refreshing deck from {sourceName}...");
            Log.Information("Refreshing deck from {Url} (source: {Source})", url, sourceName);

            try
            {
                BusyMessage = $"Fetching deck list from {sourceName}...";
                var (fetchedDeck, error) = await _importCoordinator.FetchDeckAsync(url);
                if (fetchedDeck is not { } deck || error != null)
                {
                    ClearBusy();
                    MessageBox.Show($"Failed to fetch deck:\n{error}", $"{sourceName} Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PushUndo();

                Cards.CollectionChanged -= OnCardsCollectionChanged;
                Cards.Clear();

                var importResult = await _importCoordinator.ImportDeckCardsAsync(
                    deck, Cards, false, UseMpcFill,
                    MpcAdvMinDpi, MpcFuzzySearch, MpcUseFavoritesOnly,
                    onProgress: msg => BusyMessage = msg);

                BusyMessage = $"Adding {importResult.Cards.Count} cards...";
                foreach (var c in importResult.Cards)
                {
                    ApplyDefaultBackArt(c);
                    Cards.Add(c);
                }
                Cards.CollectionChanged += OnCardsCollectionChanged;

                _currentProject.PageSettings.CenterGrid();
                ApplyFilterAndSort();

                int totalAdded = importResult.Cards.Sum(c => c.Quantity);
                StatusText = $"Refreshed: {importResult.Cards.Count} card(s) ({totalAdded} total) from \"{deck.Name}\"";
            }
            catch (Exception ex)
            {
                StatusText = $"Refresh failed: {ex.Message}";
            }
            finally
            {
                ClearBusy();
            }
        }

        public bool HasDeckImportUrl => !string.IsNullOrEmpty(_currentProject.DeckImportUrl);
        public string DeckImportUrlDisplay => _currentProject.DeckImportUrl ?? "";

        // --- Sort and Filter ---

        private void ApplyFilterAndSort()
        {
            var source = Cards.AsEnumerable();

            // Text filter (searches name, type, oracle text, set)
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                string ft = FilterText.Trim();
                source = source.Where(c =>
                    c.Name.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                    c.TypeLine.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                    c.OracleText.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                    c.SetName.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                    c.Artist.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                    c.Keywords.Contains(ft, StringComparison.OrdinalIgnoreCase));
            }

            // Rarity filter
            if (FilterRarity != "All")
            {
                source = source.Where(c =>
                    c.Rarity.Equals(FilterRarity, StringComparison.OrdinalIgnoreCase));
            }

            // Color filter
            if (FilterColor != "All")
            {
                source = FilterColor switch
                {
                    "White" => source.Where(c => c.Colors.Contains("W")),
                    "Blue" => source.Where(c => c.Colors.Contains("U")),
                    "Black" => source.Where(c => c.Colors.Contains("B")),
                    "Red" => source.Where(c => c.Colors.Contains("R")),
                    "Green" => source.Where(c => c.Colors.Contains("G")),
                    "Colorless" => source.Where(c => string.IsNullOrEmpty(c.Colors)),
                    "Multicolor" => source.Where(c => c.Colors.Count(ch => ch == ',') >= 1),
                    _ => source
                };
            }

            // Sort
            var rarityOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["common"] = 0, ["uncommon"] = 1, ["rare"] = 2, ["mythic"] = 3
            };

            source = SortBy switch
            {
                "Name" => SortDescending ? source.OrderByDescending(c => c.Name) : source.OrderBy(c => c.Name),
                "CMC" => SortDescending ? source.OrderByDescending(c => c.CMC) : source.OrderBy(c => c.CMC),
                "Rarity" => SortDescending
                    ? source.OrderByDescending(c => rarityOrder.GetValueOrDefault(c.Rarity, -1))
                    : source.OrderBy(c => rarityOrder.GetValueOrDefault(c.Rarity, -1)),
                "Color" => SortDescending ? source.OrderByDescending(c => c.Colors) : source.OrderBy(c => c.Colors),
                "Type" => SortDescending ? source.OrderByDescending(c => c.TypeLine) : source.OrderBy(c => c.TypeLine),
                "Set" => SortDescending
                    ? source.OrderByDescending(c => c.SetName).ThenByDescending(c => c.CollectorNumber)
                    : source.OrderBy(c => c.SetName).ThenBy(c => c.CollectorNumber),
                "Artist" => SortDescending ? source.OrderByDescending(c => c.Artist) : source.OrderBy(c => c.Artist),
                "Collector #" => SortDescending
                    ? source.OrderByDescending(c => c.SetCode).ThenByDescending(c => int.TryParse(c.CollectorNumber, out var n) ? n : 9999)
                    : source.OrderBy(c => c.SetCode).ThenBy(c => int.TryParse(c.CollectorNumber, out var n) ? n : 9999),
                _ => SortDescending ? source.OrderByDescending(c => c.DateAdded) : source.OrderBy(c => c.DateAdded), // Date Added
            };

            FilteredCards = new ObservableCollection<CardModel>(source);
        }

        /// <summary>
        /// Permanently reorders the Cards collection to match the current sort.
        /// This changes the print order.
        /// </summary>
        private void ApplySortToProject()
        {
            if (FilteredCards.Count == 0) return;

            PushUndo();
            // Rebuild Cards in FilteredCards order (only includes visible cards,
            // but also keep any that were filtered out at the end)
            var ordered = FilteredCards.ToList();
            var hidden = Cards.Except(ordered).ToList();
            ordered.AddRange(hidden);

            Cards.CollectionChanged -= OnCardsCollectionChanged;
            Cards.Clear();
            foreach (var c in ordered)
                Cards.Add(c);
            Cards.CollectionChanged += OnCardsCollectionChanged;

            _currentProject.PageSettings.CenterGrid();
            StatusText = $"Project reordered by {SortBy}";
        }

        private void ClearFilter()
        {
            FilterText = string.Empty;
            FilterRarity = "All";
            FilterColor = "All";
            SortBy = "Date Added";
            SortDescending = false;
        }

        // --- Page Layout ---

        private static string DetectPagePreset(PageLayout settings)
        {
            float w = settings.PageWidthMm;
            float h = settings.PageHeightMm;
            // Normalize to portrait for comparison
            float pw = Math.Min(w, h);
            float ph = Math.Max(w, h);

            bool Match(float presetW, float presetH) =>
                Math.Abs(pw - presetW) < 1f && Math.Abs(ph - presetH) < 1f;

            if (Match(594f, 841f)) return "A1";
            if (Match(420f, 594f)) return "A2";
            if (Match(297f, 420f)) return "A3";
            if (Match(210f, 297f)) return "A4";
            if (Match(215.9f, 279.4f)) return "Letter";
            if (Match(215.9f, 355.6f)) return "Legal";
            if (Match(279.4f, 431.8f)) return "Tabloid";
            return "Custom";
        }

        private void SetPagePreset(string? preset)
        {
            if (string.IsNullOrEmpty(preset)) return;
            _currentProject.PageSettings.ApplyPagePreset(preset);
            StatusText = $"Page size set to {preset}";
        }

        private void ToggleLandscape()
        {
            _currentProject.PageSettings.IsLandscape = !_currentProject.PageSettings.IsLandscape;
            StatusText = _currentProject.PageSettings.IsLandscape ? "Landscape orientation" : "Portrait orientation";
        }

        private void SyncCardsToProject()
        {
            _currentProject.Cards = Cards.ToList();
            _currentProject.LastModified = DateTime.Now;
        }

        /// <summary>
        /// Applies the default back art from the library to a card,
        /// but only if the card doesn't already have back art assigned.
        /// </summary>
        private static readonly HashSet<string> BasicLandNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Plains", "Island", "Swamp", "Mountain", "Forest",
            "Snow-Covered Plains", "Snow-Covered Island", "Snow-Covered Swamp",
            "Snow-Covered Mountain", "Snow-Covered Forest", "Wastes"
        };

        private static bool IsBasicLand(string cardName) => BasicLandNames.Contains(cardName);

        private void ApplyDefaultBackArt(CardModel card)
        {
            if (!string.IsNullOrEmpty(card.BackArtworkPath)) return;

            // First: use whatever back art the majority of existing cards use
            var mostCommon = GetMostCommonBackArt();
            if (mostCommon != null)
            {
                card.BackArtworkPath = mostCommon;
                card.IncludeBack = true;
                return;
            }

            // Second: fall back to the library default
            var defaultPath = _backArtLibraryService.DefaultBackArtPath;
            if (defaultPath != null)
            {
                card.BackArtworkPath = defaultPath;
                card.IncludeBack = true;
            }
        }

        private void ManageBackArtLibrary() => ManageArtLibrary(1);

        private void ManageFrontArtLibrary() => ManageArtLibrary(0);

        private void ManageArtLibrary(int initialTab = 0)
        {
            var dialog = new Dialogs.ArtLibraryDialog(
                _frontArtLibraryService, _backArtLibraryService,
                _mpcFillService, _imageCacheService, _appSettings, _scryfallService,
                initialTab);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
            RefreshBackArtLibrary();
            StatusText = $"Front: {_frontArtLibraryService.Entries.Count}, Back: {_backArtLibraryService.Entries.Count} item(s)";
        }

        private void ClearCache()
        {
            var size = _cacheManager.GetTotalCacheSizeBytes();
            var result = MessageBox.Show(
                $"Clear all cached files?\n\n" +
                $"This will free {CacheManager.FormatBytes(size)} of disk space.\n" +
                $"Downloaded card images will need to be re-downloaded.\n\n" +
                $"Your projects, back art library, and favorites are not affected.",
                "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            var (files, bytes) = _cacheManager.ClearAllCaches();
            OnPropertyChanged(nameof(CacheSizeText));
            StatusText = $"Cleared {files} cached file(s), freed {CacheManager.FormatBytes(bytes)}";
        }

        private void SetBusy(string message)
        {
            BusyMessage = message;
            StatusText = message;
            IsBusy = true;
        }

        private void ClearBusy()
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }
}
