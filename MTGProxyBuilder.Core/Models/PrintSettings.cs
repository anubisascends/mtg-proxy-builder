using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public enum PrintMode
    {
        Duplex,
        FrontsOnly,
        BacksOnly
    }

    public class PrintSettings : INotifyPropertyChanged
    {
        private PrintMode _printMode = PrintMode.Duplex;
        private int _dpi = Constants.DefaultDpi;
        private bool _showCutGuides = true;

        public PrintMode PrintMode
        {
            get => _printMode;
            set { _printMode = value; OnPropertyChanged(); }
        }

        public int DPI
        {
            get => _dpi;
            set { _dpi = value; OnPropertyChanged(); }
        }

        public bool ShowCutGuides
        {
            get => _showCutGuides;
            set { _showCutGuides = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
