using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public class ProjectModel : INotifyPropertyChanged
    {
        private string _projectId = Guid.NewGuid().ToString();
        private string _projectName = "Untitled Project";
        private List<CardModel> _cards = new();
        private PageLayout _pageSettings = new();
        private PrintSettings _printSettings = new();
        private DateTime _createdDate = DateTime.Now;
        private DateTime _lastModified = DateTime.Now;

        public string ProjectId
        {
            get => _projectId;
            set { _projectId = value; OnPropertyChanged(); }
        }

        public string ProjectName
        {
            get => _projectName;
            set { _projectName = value; OnPropertyChanged(); }
        }

        public List<CardModel> Cards
        {
            get => _cards;
            set { _cards = value; OnPropertyChanged(); }
        }

        public PageLayout PageSettings
        {
            get => _pageSettings;
            set { _pageSettings = value; OnPropertyChanged(); }
        }

        public PrintSettings PrintSettings
        {
            get => _printSettings;
            set { _printSettings = value; OnPropertyChanged(); }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set { _createdDate = value; OnPropertyChanged(); }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set { _lastModified = value; OnPropertyChanged(); }
        }

        public int TotalCards => Cards.Sum(c => c.Quantity);

        public int TotalPages
        {
            get
            {
                int cardsPerPage = PageSettings.CardsPerPage;
                if (cardsPerPage <= 0) return 0;
                return (int)Math.Ceiling((double)TotalCards / cardsPerPage);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
