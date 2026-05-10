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

        // Scryfall search
        private string _scryfallSearchQuery = string.Empty;
        private ObservableCollection<ScryfallCard> _scryfallResults = new();
        private ScryfallCard? _selectedScryfallCard;
        private bool _isSearching;
        private bool _isBusy;
        private string _busyMessage = string.Empty;

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
        private readonly BackArtLibraryService _backArtLibraryService;
        private readonly MoxfieldService _moxfieldService;
        private readonly ArchidektService _archidektService;
        private readonly DeckImportService _deckImportService;
        private readonly MpcFillService _mpcFillService;
        private readonly UndoService _undoService = new();
        private readonly CacheManager _cacheManager = new();

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

        public MainViewModel()
        {
            _imageCacheService = new ImageCacheService();
            _serializationService = new ProjectSerializationService();
            _pdfGeneratorService = new PdfGeneratorService();
            _scryfallService = new ScryfallService(_imageCacheService);
            _backArtLibraryService = new BackArtLibraryService();
            _moxfieldService = new MoxfieldService();
            _archidektService = new ArchidektService();
            _deckImportService = new DeckImportService(_moxfieldService, _archidektService);
            MpcSourceManager = new MpcFillSourceManager();
            _mpcFillService = new MpcFillService(_imageCacheService, MpcSourceManager);
            _mpcUseFavoritesOnly = MpcSourceManager.HasFavorites; // default on if user has favorites

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
            SelectBackArtForAllCommand = new RelayCommand(_ => SelectBackArtForAll(), _ => Cards.Count > 0);

            ScryfallSearchCommand = new RelayCommand(_ => ScryfallSearch(), _ => !string.IsNullOrWhiteSpace(ScryfallSearchQuery));
            AddScryfallCardCommand = new RelayCommand(_ => AddScryfallCard(), _ => SelectedScryfallCard != null);

            ExportPdfCommand = new RelayCommand(_ => ExportPdf());

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

            // Advanced search
            BuildAdvancedQueryCommand = new RelayCommand(_ => ApplyAdvancedQuery());
            ClearAdvancedSearchCommand = new RelayCommand(_ => ClearAdvancedSearch());

            // MPCFill sources
            LoadMpcSourcesCommand = new RelayCommand(_ => LoadMpcSources());
            ToggleMpcFavoriteFromResultCommand = new RelayCommand(p => ToggleFavoriteFromResult(p));
            ManageMpcSourcesCommand = new RelayCommand(_ => ManageMpcSources());
            ClearCacheCommand = new RelayCommand(_ => ClearCache());
            ManageBackArtLibraryCommand = new RelayCommand(_ => ManageBackArtLibrary());

            // MPCFill / art source
            AddMpcFillCardCommand = new RelayCommand(_ => AddMpcFillCard(), _ => SelectedMpcFillCard != null);
            ClearAllCardsCommand = new RelayCommand(_ => ClearAllCards(), _ => Cards.Count > 0);
            UpdateAllArtFromMpcFillCommand = new RelayCommand(_ => UpdateAllArtFromMpcFill(), _ => Cards.Count > 0);

            // PrintMode values for ComboBox
            PrintModeValues = new ObservableCollection<PrintMode>(
                Enum.GetValues<PrintMode>());

            PagePresets = new ObservableCollection<string> { "A4", "A3", "Letter", "Legal", "Tabloid" };
            _selectedPagePreset = "A4";
            _selectedCardSize = CardSizePresets.First(p => p.Name == "Magic: The Gathering");

            // Load persisted back art library
            RefreshBackArtLibrary();
            ApplyFilterAndSort();

            // Startup cache cleanup
            _cacheManager.CleanupOnStartup();
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
            set => SetProperty(ref _selectedCard, value);
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
        public ICommand AddMpcFillCardCommand { get; }
        public ICommand ClearAllCardsCommand { get; }
        public ICommand UpdateAllArtFromMpcFillCommand { get; }

        public string ImportDeckUrl
        {
            get => _importDeckUrl;
            set => SetProperty(ref _importDeckUrl, value);
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
        public bool MpcUseFavoritesOnly { get => _mpcUseFavoritesOnly; set => SetProperty(ref _mpcUseFavoritesOnly, value); }
        public ObservableCollection<int> MpcDpiOptions { get; } = new() { 0, 300, 600, 800, 1200 };
        public MpcFillSourceManager MpcSourceManager { get; }
        public ObservableCollection<MpcFillSource> MpcSourceList { get; } = new();

        public ICommand LoadMpcSourcesCommand { get; private set; } = null!;
        public ICommand ToggleMpcFavoriteFromResultCommand { get; private set; } = null!;
        public ICommand ManageMpcSourcesCommand { get; private set; } = null!;
        public ICommand ClearCacheCommand { get; private set; } = null!;
        public ICommand ManageBackArtLibraryCommand { get; private set; } = null!;

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

        /// <summary>Builds a Scryfall query string from the advanced search fields.</summary>
        private string BuildAdvancedQuery()
        {
            var parts = new List<string>();

            void Add(string prefix, string value, bool quote = false)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                value = value.Trim();
                if (quote && value.Contains(' '))
                    parts.Add($"{prefix}\"{value}\"");
                else
                    parts.Add($"{prefix}{value}");
            }

            Add("", _advName); // bare name search
            if (!string.IsNullOrWhiteSpace(_advType)) Add("t:", _advType, true);
            if (!string.IsNullOrWhiteSpace(_advOracle)) Add("o:", _advOracle, true);
            if (!string.IsNullOrWhiteSpace(_advColors)) Add("c:", _advColors);
            if (!string.IsNullOrWhiteSpace(_advIdentity)) Add("id:", _advIdentity);
            if (!string.IsNullOrWhiteSpace(_advCmcValue)) parts.Add($"cmc{_advCmcOp}{_advCmcValue}");
            if (!string.IsNullOrWhiteSpace(_advRarity)) Add("r:", _advRarity);
            if (!string.IsNullOrWhiteSpace(_advSet)) Add("s:", _advSet);
            if (!string.IsNullOrWhiteSpace(_advFormat)) Add("f:", _advFormat);
            if (!string.IsNullOrWhiteSpace(_advPowValue)) parts.Add($"pow{_advPowOp}{_advPowValue}");
            if (!string.IsNullOrWhiteSpace(_advTouValue)) parts.Add($"tou{_advTouOp}{_advTouValue}");
            if (!string.IsNullOrWhiteSpace(_advArtist)) Add("a:", _advArtist, true);
            if (!string.IsNullOrWhiteSpace(_advKeyword)) Add("kw:", _advKeyword);
            if (!string.IsNullOrWhiteSpace(_advIs)) Add("is:", _advIs);

            return string.Join(" ", parts);
        }

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
                    _currentProject.PageSettings.ApplyPagePreset(value);
            }
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
        public ICommand SelectBackArtForAllCommand { get; }
        public ICommand ScryfallSearchCommand { get; }
        public ICommand AddScryfallCardCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand AddBackArtToLibraryCommand { get; }
        public ICommand RemoveBackArtFromLibraryCommand { get; }
        public ICommand ApplyBackArtToSelectedCommand { get; }
        public ICommand ApplyBackArtToAllCommand { get; }
        public ICommand ClearBackArtFromAllCommand { get; }
        public ICommand SetPagePresetCommand { get; }
        public ICommand ToggleLandscapeCommand { get; }

        // --- Undo / Redo ---

        private void PushUndo() => _undoService.SaveState(Cards);

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
            StatusText = "New project created";
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
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(SelectedPrintMode));
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

        private void ShowArtSelector(CardModel card, Dialogs.ArtSelectorMode mode)
        {
            var dialog = new Dialogs.ArtSelectorDialog(
                card, mode, _scryfallService, _mpcFillService, _imageCacheService,
                _backArtLibraryService, Cards);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ResultPath != null)
            {
                PushUndo();
                if (mode == Dialogs.ArtSelectorMode.Front)
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
                _backArtLibraryService, Cards);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true && dialog.ResultPath != null)
            {
                PushUndo();
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

            if (UseMpcFill)
            {
                await SearchMpcFill();
            }
            else
            {
                await SearchScryfall();
            }

            IsSearching = false;
            ClearBusy();
        }

        private async Task SearchScryfall()
        {
            SetBusy("Searching Scryfall...");
            try
            {
                var (results, error) = await _scryfallService.SearchCardAsync(ScryfallSearchQuery);
                if (error != null)
                {
                    ScryfallResults.Clear();
                    StatusText = error;
                    MessageBox.Show(error, "Search Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    ScryfallResults = new ObservableCollection<ScryfallCard>(results.Take(50));
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
                // Build sources array based on favorites preference
                object[][]? sources = MpcUseFavoritesOnly && MpcSourceManager.HasFavorites
                    ? MpcSourceManager.BuildFavoritesArray()
                    : null; // null = all sources

                var (results, error) = await _mpcFillService.SearchAsync(
                    ScryfallSearchQuery, 50, MpcAdvMinDpi, MpcFuzzySearch, sources);
                if (error != null)
                {
                    MpcFillResults.Clear();
                    StatusText = error;
                    MessageBox.Show(error, "MPCFill Search Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    var filtered = results.AsEnumerable();
                    if (!string.IsNullOrWhiteSpace(MpcAdvName))
                        filtered = filtered.Where(c => c.Name.Contains(MpcAdvName, StringComparison.OrdinalIgnoreCase));

                    MpcFillResults = new ObservableCollection<MpcFillCard>(filtered);
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

        private async void AddScryfallCard()
        {
            if (SelectedScryfallCard == null) return;

            SetBusy($"Downloading artwork for {SelectedScryfallCard.Name}...");

            try
            {
                var frontPath = await _scryfallService.DownloadAndCacheImageAsync(SelectedScryfallCard);
                string? backPath = null;
                if (SelectedScryfallCard.GetBackImageUrl() != null)
                    backPath = await _scryfallService.DownloadAndCacheImageAsync(SelectedScryfallCard, back: true);

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

            try
            {
                bool success = await _pdfGeneratorService.GeneratePdfAsync(_currentProject, dialog.FileName);

                if (success)
                {
                    StatusText = $"PDF exported: {Path.GetFileName(dialog.FileName)}";
                    MessageBox.Show($"PDF exported successfully!\n\n{dialog.FileName}",
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

                var dialog = new Dialogs.MpcSourceManagerDialog(MpcSourceManager);
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
                var path = await _mpcFillService.DownloadAndCacheImageAsync(SelectedMpcFillCard);
                PushUndo();
                var card = new CardModel
                {
                    Name = SelectedMpcFillCard.Name.Split('(')[0].Trim(), // strip set info from name
                    ArtworkPath = path ?? string.Empty,
                    Artist = SelectedMpcFillCard.Source,
                    DateAdded = DateTime.Now
                };
                ApplyDefaultBackArt(card);
                Cards.Add(card);
                ApplyFilterAndSort();
                StatusText = $"Added: {card.Name} (from MPCFill, {SelectedMpcFillCard.Source})";
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
            int updated = 0, failed = 0;

            try
            {
                for (int i = 0; i < Cards.Count; i++)
                {
                    var card = Cards[i];
                    BusyMessage = $"Searching MPCFill {i + 1}/{Cards.Count}: {card.Name}...";
                    await Task.Delay(10);

                    var (results, error) = await _mpcFillService.SearchAsync(card.Name, 5);
                    if (error != null || results.Count == 0) { failed++; continue; }

                    // Use the first result
                    var best = results[0];
                    BusyMessage = $"Downloading art {i + 1}/{Cards.Count}: {card.Name}...";
                    var path = await _mpcFillService.DownloadAndCacheImageAsync(best);
                    if (path != null)
                    {
                        card.ArtworkPath = path;
                        updated++;
                    }
                    else { failed++; }

                    await Task.Delay(50); // rate limiting
                }

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

        // --- Moxfield Import ---

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

            try
            {
                BusyMessage = $"Fetching deck list from {sourceName}...";
                await Task.Delay(50);

                var (deck, error) = await _deckImportService.ImportAsync(ImportDeckUrl);
                if (deck == null || error != null)
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

                // Collect cards into a batch to avoid per-card canvas redraws (prevents flashing)
                var importedCards = new List<CardModel>();
                int failed = 0;

                for (int i = 0; i < deck.Entries.Count; i++)
                {
                    var entry = deck.Entries[i];

                    BusyMessage = $"Looking up card {i + 1}/{uniqueCards}: {entry.CardName}" +
                        (entry.Quantity > 1 ? $" (x{entry.Quantity})" : "") + "...";
                    await Task.Delay(10);

                    ScryfallCard? scryfallCard = null;

                    if (!string.IsNullOrEmpty(entry.ScryfallId))
                        scryfallCard = await _scryfallService.GetCardByIdAsync(entry.ScryfallId);

                    if (scryfallCard == null)
                    {
                        BusyMessage = $"Searching Scryfall for: {entry.CardName}...";
                        scryfallCard = await _scryfallService.GetCardByNameAsync(entry.CardName);
                    }

                    if (scryfallCard == null) { failed++; continue; }

                    BusyMessage = $"Downloading artwork {i + 1}/{uniqueCards}: {entry.CardName}...";
                    await Task.Delay(10);

                    var frontPath = await _scryfallService.DownloadAndCacheImageAsync(scryfallCard);
                    string? backPath = null;
                    if (scryfallCard.GetBackImageUrl() != null)
                        backPath = await _scryfallService.DownloadAndCacheImageAsync(scryfallCard, back: true);

                    var card = scryfallCard.ToCardModel(frontPath ?? string.Empty, backPath);
                    card.Quantity = entry.Quantity;
                    ApplyDefaultBackArt(card);
                    importedCards.Add(card);

                    await Task.Delay(100);
                }

                // Batch-add all cards at once — suppresses per-card redraws
                BusyMessage = $"Adding {importedCards.Count} cards to project...";
                await Task.Delay(50);

                Cards.CollectionChanged -= OnCardsCollectionChanged;
                foreach (var c in importedCards) Cards.Add(c);
                Cards.CollectionChanged += OnCardsCollectionChanged;

                _currentProject.PageSettings.CenterGrid();
                ApplyFilterAndSort();

                ImportDeckUrl = string.Empty;

                int totalAdded = importedCards.Sum(c => c.Quantity);
                string summary = $"Imported {importedCards.Count} unique card(s) ({totalAdded} total) from \"{deck.Name}\" ({sourceName})";
                if (failed > 0) summary += $"\n{failed} card(s) could not be found on Scryfall";
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
        private void ApplyDefaultBackArt(CardModel card)
        {
            if (!string.IsNullOrEmpty(card.BackArtworkPath)) return;

            var defaultPath = _backArtLibraryService.DefaultBackArtPath;
            if (defaultPath != null)
            {
                card.BackArtworkPath = defaultPath;
                card.IncludeBack = true;
            }
        }

        private void ManageBackArtLibrary()
        {
            var dialog = new Dialogs.BackArtLibraryDialog(_backArtLibraryService, _mpcFillService);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
            RefreshBackArtLibrary();
            StatusText = $"Back art library: {_backArtLibraryService.Entries.Count} item(s)";
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
