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

            _ = CheckForUpdateAsync();
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

        public void SaveSettings() => _appSettings.Save();

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
            Log.Information("Opening project {Path}", dialog.FileName);

            // Check if already open
            foreach (var p in Projects)
            {
                if (string.Equals(p.FilePath, dialog.FileName, StringComparison.OrdinalIgnoreCase))
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
                var serializer = new ProjectSerializationService();
                var project = await serializer.LoadProjectAsync(dialog.FileName,
                    msg => Application.Current.Dispatcher.Invoke(() => LoadingMessage = msg));
                if (project == null)
                {
                    MessageBox.Show("Failed to load project file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LoadingMessage = "Building project view...";
                vm.LoadFromProject(project, dialog.FileName);
                var tab = new ProjectViewModel(vm) { FilePath = dialog.FileName };
                Projects.Add(tab);
                ActiveProject = tab;
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
        }

        private void ManageFrontArtLibrary() => ManageArtLibrary(0);

        private void ManageBackArtLibrary() => ManageArtLibrary(1);

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
