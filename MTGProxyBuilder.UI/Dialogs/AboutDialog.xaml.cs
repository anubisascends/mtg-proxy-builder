using System.Windows;
using System.Windows.Input;
using MTGProxyBuilder.UI.ViewModels;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            VersionLabel.Text = $"Version {MainViewModel.GetAppVersion()}";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }
    }
}
