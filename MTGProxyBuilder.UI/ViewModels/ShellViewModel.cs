using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using Serilog;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the application shell.
    /// Manages the collection of open project tabs and global commands.
    /// </summary>
    public class ShellViewModel : INotifyPropertyChanged
    {
        private ProjectViewModel? _activeProject;
        private readonly AppSettingsService _appSettings = new();
        private readonly MpcFillSourceManager _mpcSourceManager = new();
        private readonly ImageCacheService _imageCacheService;
        private readonly ScryfallService _scryfallService;
        private readonly MpcFillService _mpcFillService;
        private BackArtLibraryService _backArtLibraryService;
        private FrontArtLibraryService _frontArtLibraryService;
        private readonly ScryfallBulkDataService _bulkDataService = new();
        private readonly UpdateCheckService _updateService = new();
        private bool _updateAvailable;
        private string _updateMessage = string.Empty;
        private string _updateDownloadUrl = string.Empty;
        private bool _isLoading;
        private string _loadingMessage = string.Empty;

        public ShellViewModel()
        {
            _imageCacheService = new ImageCacheService();
            _scryfallService = new ScryfallService(_imageCacheService);
            _mpcFillService = new MpcFillService(_imageCacheService, _mpcSourceManager);
            _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
            _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
            Projects = new ObservableCollection<ProjectViewModel>();

            NewProjectCommand = new RelayCommand(_ => NewProject());
            OpenProjectCommand = new RelayCommand(_ => OpenProject());
            CloseProjectCommand = new RelayCommand(_ => CloseActiveProject(), _ => ActiveProject != null);
            CloseAllProjectsCommand = new RelayCommand(_ => CloseAllProjects(), _ => Projects.Count > 0);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            DownloadUpdateCommand = new RelayCommand(_ => DownloadUpdate());
            DismissUpdateCommand = new RelayCommand(_ => UpdateAvailable = false);
            ManageFrontArtLibraryCommand = new RelayCommand(_ => ManageFrontArtLibrary());
            ManageBackArtLibraryCommand = new RelayCommand(_ => ManageBackArtLibrary());
            AboutCommand = new RelayCommand(_ => ShowAbout());

            _ = CheckForUpdateAsync();
            RefreshRecentFiles();
            Log.Information("ShellViewModel initialized");
        }

        public ObservableCollection<ProjectViewModel> Projects { get; }

        public ProjectViewModel? ActiveProject
        {
            get => _activeProject;
            set { SetProperty(ref _activeProject, value); OnPropertyChanged(nameof(HasActiveProject)); }
        }

        public bool HasActiveProject => _activeProject != null;

        // --- Global Commands ---
        public ICommand NewProjectCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand CloseProjectCommand { get; }
        public ICommand CloseAllProjectsCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand DownloadUpdateCommand { get; }
        public ICommand DismissUpdateCommand { get; }
        public ICommand ManageFrontArtLibraryCommand { get; }
        public ICommand ManageBackArtLibraryCommand { get; }
        public ICommand AboutCommand { get; }

        // --- Update ---
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

        public string AppVersion => MainViewModel.GetAppVersion();

        public AppSettings AppSettings => _appSettings.Settings;
        public ScryfallBulkDataService BulkDataService => _bulkDataService;

        public ObservableCollection<string> RecentFiles { get; } = new();

        public void AddRecentFile(string filePath)
        {
            var list = _appSettings.Settings.RecentFiles;
            list.Remove(filePath); // Remove if already present (to move to top)
            list.Insert(0, filePath);
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            _appSettings.Save();
            RefreshRecentFiles();
        }

        private void RefreshRecentFiles()
        {
            RecentFiles.Clear();
            foreach (var f in _appSettings.Settings.RecentFiles)
            {
                if (System.IO.File.Exists(f))
                    RecentFiles.Add(f);
            }
            OnPropertyChanged(nameof(HasRecentFiles));
        }

        public bool HasRecentFiles => RecentFiles.Count > 0;

        public void OpenRecentFile(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
            {
                _appSettings.Settings.RecentFiles.Remove(filePath);
                _appSettings.Save();
                RefreshRecentFiles();
                MessageBox.Show($"File not found:\n{filePath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            OpenProjectFromPath(filePath);
        }

        public void SaveSettings() => _appSettings.Save();

        public double SidebarFontSize
        {
            get => _appSettings.Settings.SidebarFontSize > 0 ? _appSettings.Settings.SidebarFontSize : 12;
            set
            {
                _appSettings.Settings.SidebarFontSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SidebarScale));
            }
        }

        /// <summary>Scale factor for sidebar content. 1.0 at default (12pt), scales proportionally.</summary>
        public double SidebarScale => SidebarFontSize / 12.0;

        // --- Loading ---
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set => SetProperty(ref _loadingMessage, value);
        }

        // --- Project Management ---

        public void NewProject()
        {
            Log.Information("Creating new project");
            var vm = new MainViewModel();
            vm.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
            vm.UseSharedServices(_bulkDataService);
            ApplyDefaults(vm);
            var tab = new ProjectViewModel(vm);
            Projects.Add(tab);
            ActiveProject = tab;
        }

        private void ApplyDefaults(MainViewModel vm)
        {
            var s = _appSettings.Settings;

            vm.CurrentProject.PageSettings.BleedWidthMm = s.DefaultBleedMm;

            // Set via ViewModel properties so both the model AND the ComboBox are updated
            vm.SelectedPagePreset = s.DefaultPagePreset;

            var preset = CardSizePreset.BuiltInPresets.FirstOrDefault(
                p => p.Name == s.DefaultCardSizePreset);
            if (preset != null)
                vm.SelectedCardSize = preset;

            vm.HasUnsavedChanges = false;
        }

        public async void OpenProject()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "MTG Project Files (*.mtgproj)|*.mtgproj|All Files (*.*)|*.*",
                Title = "Open Project"
            };
            if (dialog.ShowDialog() != true) return;
            await OpenProjectFromPath(dialog.FileName);
        }

        public async Task OpenProjectFromPath(string filePath)
        {
            Log.Information("Opening project {Path}", filePath);

            // Check if already open
            foreach (var p in Projects)
            {
                if (string.Equals(p.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveProject = p;
                    return;
                }
            }

            IsLoading = true;
            LoadingMessage = "Opening project...";
            try
            {
                var vm = new MainViewModel();
                vm.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
                vm.UseSharedServices(_bulkDataService);
                var serializer = new ProjectSerializationService();
                var project = await serializer.LoadProjectAsync(filePath,
                    msg => Application.Current.Dispatcher.Invoke(() => LoadingMessage = msg));
                if (project == null)
                {
                    MessageBox.Show("Failed to load project file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LoadingMessage = "Building project view...";
                vm.LoadFromProject(project, filePath);
                var tab = new ProjectViewModel(vm) { FilePath = filePath };
                Projects.Add(tab);
                ActiveProject = tab;
                AddRecentFile(filePath);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void CloseActiveProject()
        {
            if (ActiveProject == null) return;
            CloseProject(ActiveProject);
        }

        public void CloseProject(ProjectViewModel project)
        {
            if (project.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    $"Save changes to \"{project.Inner.ProjectName}\"?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    if (project.Inner.SaveProjectCommand.CanExecute(null))
                        project.Inner.SaveProjectCommand.Execute(null);
                }
                else if (result == MessageBoxResult.Cancel)
                    return;
            }

            int idx = Projects.IndexOf(project);
            Projects.Remove(project);

            if (ActiveProject == project)
            {
                if (Projects.Count > 0)
                    ActiveProject = Projects[Math.Min(idx, Projects.Count - 1)];
                else
                    ActiveProject = null;
            }
        }

        public void CloseAllProjects()
        {
            for (int i = Projects.Count - 1; i >= 0; i--)
            {
                var p = Projects[i];
                if (p.HasUnsavedChanges)
                {
                    var result = MessageBox.Show(
                        $"Save changes to \"{p.Inner.ProjectName}\"?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (p.Inner.SaveProjectCommand.CanExecute(null))
                            p.Inner.SaveProjectCommand.Execute(null);
                    }
                    else if (result == MessageBoxResult.Cancel)
                        return;
                }
                Projects.RemoveAt(i);
            }
            ActiveProject = null;
        }

        private void OpenSettings()
        {
            string? oldFrontPath = _appSettings.Settings.FrontArtLibraryPath;
            string? oldBackPath = _appSettings.Settings.BackArtLibraryPath;

            var dialog = new Dialogs.SettingsDialog(_appSettings, _mpcSourceManager, _mpcFillService);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();

            // Reload library services if paths changed, and push to all open projects
            bool changed = false;
            if (_appSettings.Settings.FrontArtLibraryPath != oldFrontPath)
            {
                _frontArtLibraryService = new FrontArtLibraryService(_appSettings.Settings.FrontArtLibraryPath);
                changed = true;
            }
            if (_appSettings.Settings.BackArtLibraryPath != oldBackPath)
            {
                _backArtLibraryService = new BackArtLibraryService(_appSettings.Settings.BackArtLibraryPath);
                changed = true;
            }
            if (changed)
            {
                foreach (var p in Projects)
                    p.Inner.UseSharedLibraries(_frontArtLibraryService, _backArtLibraryService);
            }

            // Notify UI of font size change
            OnPropertyChanged(nameof(SidebarFontSize));
            OnPropertyChanged(nameof(SidebarScale));
        }

        private void ManageFrontArtLibrary() => ManageArtLibrary(0);

        private void ManageBackArtLibrary() => ManageArtLibrary(1);

        private void ShowAbout()
        {
            var dialog = new Dialogs.AboutDialog();
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        private void ManageArtLibrary(int initialTab)
        {
            var dialog = new Dialogs.ArtLibraryDialog(
                _frontArtLibraryService, _backArtLibraryService,
                _mpcFillService, _imageCacheService, _appSettings, _scryfallService,
                initialTab);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        // --- Update Check ---

        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            try
            {
                if (!_appSettings.Settings.CheckForUpdates) return;

                string version = MainViewModel.GetAppVersion();
                var update = await _updateService.CheckForUpdateAsync(version);
                if (update?.IsUpdateAvailable == true)
                {
                    UpdateAvailable = true;
                    UpdateMessage = $"Version {update.LatestVersion} is available (you have {update.CurrentVersion})";
                    UpdateDownloadUrl = update.DownloadUrl;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Update check failed"); }
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

        /// <summary>Check all open projects for unsaved changes. Returns true if safe to close.</summary>
        public bool CanCloseApplication()
        {
            foreach (var p in Projects)
            {
                if (p.HasUnsavedChanges)
                {
                    ActiveProject = p;
                    var result = MessageBox.Show(
                        $"Save changes to \"{p.Inner.ProjectName}\"?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (p.Inner.SaveProjectCommand.CanExecute(null))
                            p.Inner.SaveProjectCommand.Execute(null);
                    }
                    else if (result == MessageBoxResult.Cancel)
                        return false;
                }
            }
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
