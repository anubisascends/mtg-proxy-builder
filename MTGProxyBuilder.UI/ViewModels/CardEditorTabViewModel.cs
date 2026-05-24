using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.UI.ViewModels
{
    /// <summary>
    /// Wraps a CardEditorViewModel with tab metadata (title, unsaved indicator).
    /// Mirrors the ProjectViewModel pattern used in the main app.
    /// </summary>
    public class CardEditorTabViewModel : INotifyPropertyChanged
    {
        private string _tabTitle = "Untitled Card";
        private readonly CardEditorViewModel _inner;

        public CardEditorTabViewModel(CardEditorViewModel inner)
        {
            _inner = inner;
            _inner.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CardEditorViewModel.WindowTitle)
                    || e.PropertyName == nameof(CardEditorViewModel.HasUnsavedChanges))
                {
                    UpdateTabTitle();
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            };
            UpdateTabTitle();
        }

        public CardEditorViewModel Inner => _inner;

        public string TabTitle
        {
            get => _tabTitle;
            private set { _tabTitle = value; OnPropertyChanged(); }
        }

        public bool HasUnsavedChanges => _inner.HasUnsavedChanges;

        private void UpdateTabTitle()
        {
            var name = _inner.Project.ProjectName;
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            TabTitle = _inner.HasUnsavedChanges ? $"{name} *" : name;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
