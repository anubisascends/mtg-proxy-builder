using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Top-level ViewModel for the Card Editor window.
    /// Manages multiple open card editor tabs and global commands.
    /// </summary>
    public class CardEditorShellViewModel : INotifyPropertyChanged
    {
        private CardEditorTabViewModel? _activeTab;

        public CardEditorShellViewModel()
        {
            Tabs = new ObservableCollection<CardEditorTabViewModel>();

            NewTabCommand = new RelayCommand(_ => NewTab());
            OpenTabCommand = new RelayCommand(_ => _ = OpenTabAsync());
            CloseTabCommand = new RelayCommand(_ => CloseTab(_activeTab), _ => _activeTab != null);

            // Start with one empty tab
            NewTab();
        }

        public ObservableCollection<CardEditorTabViewModel> Tabs { get; }

        public CardEditorTabViewModel? ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetProperty(ref _activeTab, value))
                {
                    OnPropertyChanged(nameof(HasActiveTab));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public bool HasActiveTab => _activeTab != null;

        public string StatusText => _activeTab?.Inner.StatusText ?? "Ready";

        // --- Commands ---

        public ICommand NewTabCommand { get; }
        public ICommand OpenTabCommand { get; }
        public ICommand CloseTabCommand { get; }

        // --- Tab Management ---

        public void NewTab()
        {
            var vm = new CardEditorViewModel();
            var tab = new CardEditorTabViewModel(vm);
            Tabs.Add(tab);
            ActiveTab = tab;
        }

        public async Task OpenTabAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Custom Card Project",
                Filter = "Custom Card Project|*.ccproj"
            };

            if (dialog.ShowDialog() != true) return;

            var serialization = new CustomCardSerializationService();
            var project = await serialization.LoadProjectAsync(dialog.FileName);
            if (project == null)
            {
                MessageBox.Show("Failed to load project.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var vm = new CardEditorViewModel();
            vm.LoadProject(project, dialog.FileName);
            var tab = new CardEditorTabViewModel(vm);
            Tabs.Add(tab);
            ActiveTab = tab;
        }

        public void CloseTab(CardEditorTabViewModel? tab)
        {
            if (tab == null) return;

            if (tab.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    $"Save changes to \"{tab.Inner.Project.ProjectName}\"?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        _ = tab.Inner.SaveAsync();
                        break;
                    case MessageBoxResult.Cancel:
                        return;
                }
            }

            int idx = Tabs.IndexOf(tab);
            Tabs.Remove(tab);

            if (ActiveTab == tab)
            {
                if (Tabs.Count > 0)
                    ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
                else
                    ActiveTab = null;
            }
        }

        /// <summary>Check all tabs for unsaved changes. Returns true if safe to close.</summary>
        public bool CanCloseWindow()
        {
            foreach (var tab in Tabs.ToList())
            {
                if (tab.HasUnsavedChanges)
                {
                    ActiveTab = tab;
                    var result = MessageBox.Show(
                        $"Save changes to \"{tab.Inner.Project.ProjectName}\"?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                        _ = tab.Inner.SaveAsync();
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
