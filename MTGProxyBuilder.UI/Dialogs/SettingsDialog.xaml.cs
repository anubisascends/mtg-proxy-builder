using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettingsService _settingsService;
        private readonly MpcFillSourceManager _sourceManager;
        private readonly MpcFillService? _mpcFillService;

        public SettingsDialog(AppSettingsService settingsService, MpcFillSourceManager sourceManager,
            MpcFillService? mpcFillService = null)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _sourceManager = sourceManager;
            _mpcFillService = mpcFillService;

            var s = settingsService.Settings;
            TokenTextBox.Text = s.DefaultTokenText;
            BleedBox.Text = s.DefaultBleedMm.ToString();
            UpdateCheckBox.IsChecked = s.CheckForUpdates;
            UseFavoritesCheckBox.IsChecked = s.MpcFillUseFavoritesOnly;

            // Select the matching page preset
            foreach (ComboBoxItem item in PagePresetBox.Items)
            {
                if (item.Content.ToString() == s.DefaultPagePreset)
                {
                    PagePresetBox.SelectedItem = item;
                    break;
                }
            }
            if (PagePresetBox.SelectedItem == null)
                PagePresetBox.SelectedIndex = 0;

            // MPCFill search defaults
            SelectByTag(SortByBox, s.MpcFillDefaultSortBy);
            SelectByTag(DefaultMinDpiBox, s.MpcFillDefaultMinDpi.ToString());
            SelectByTag(DefaultMaxDpiBox, s.MpcFillDefaultMaxDpi.ToString());
            MaxSizeBox.Text = s.MpcFillMaximumSize.ToString();
            DefaultFuzzySearchBox.IsChecked = s.MpcFillDefaultFuzzySearch;
            FilterCardbacksBox.IsChecked = s.MpcFillFilterCardbacks;

            // Card types
            CardTypeCard.IsChecked = s.MpcFillCardTypes.Contains("CARD");
            CardTypeToken.IsChecked = s.MpcFillCardTypes.Contains("TOKEN");
            CardTypeCardback.IsChecked = s.MpcFillCardTypes.Contains("CARDBACK");

            // Languages
            var langs = s.MpcFillLanguages;
            LangEN.IsChecked = langs.Contains("EN");
            LangJA.IsChecked = langs.Contains("JA");
            LangFR.IsChecked = langs.Contains("FR");
            LangDE.IsChecked = langs.Contains("DE");
            LangES.IsChecked = langs.Contains("ES");
            LangIT.IsChecked = langs.Contains("IT");
            LangPT.IsChecked = langs.Contains("PT");
            LangZH.IsChecked = langs.Contains("ZH");
            LangRU.IsChecked = langs.Contains("RU");
            LangAR.IsChecked = langs.Contains("AR");
            LangSA.IsChecked = langs.Contains("SA");

            // Content filters
            ExcludeNsfwBox.IsChecked = s.MpcFillExcludeNsfw;
            ExcludeAiArtBox.IsChecked = s.MpcFillExcludeAiArt;

            // Library paths
            FrontLibPathBox.Text = s.FrontArtLibraryPath ?? "(default)";
            BackLibPathBox.Text = s.BackArtLibraryPath ?? "(default)";

            // UI settings
            FontSizeSlider.Value = s.SidebarFontSize > 0 ? s.SidebarFontSize : 12;
            FontSizeLabel.Text = $"{FontSizeSlider.Value:0} pt";

            UpdateFavoritesInfo();
        }

        private void OnFontSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeLabel != null)
                FontSizeLabel.Text = $"{e.NewValue:0} pt";
        }

        private void OnNavChanged(object sender, RoutedEventArgs e)
        {
            if (PageGeneral == null) return; // designer guard

            PageGeneral.Visibility = NavGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageLibraries.Visibility = NavLibraries.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageMpcFill.Visibility = NavMpcFill.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageLanguages.Visibility = NavLanguages.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PageFilters.Visibility = NavFilters.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateFavoritesInfo()
        {
            int favCount = _sourceManager.FavoritePks.Count;
            if (_sourceManager.IsLoaded)
            {
                FavoritesInfoLabel.Text = favCount > 0
                    ? $"{favCount} favorite source(s) selected out of {_sourceManager.AllSources.Count}"
                    : $"{_sourceManager.AllSources.Count} sources available — no favorites set (all sources will be used)";
            }
            else
            {
                FavoritesInfoLabel.Text = favCount > 0
                    ? $"{favCount} favorite source(s) saved"
                    : "No favorites set (all sources will be used)";
            }
        }

        private void OnManageSources(object sender, RoutedEventArgs e)
        {
            var dialog = new MpcSourceManagerDialog(_sourceManager, _mpcFillService);
            dialog.Owner = this;
            dialog.ShowDialog();
            UpdateFavoritesInfo();
        }

        private static void SelectByTag(ComboBox box, string tagValue)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                if (item.Tag?.ToString() == tagValue)
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            box.SelectedIndex = box.Items.Count - 1;
        }

        private static int ParseTagAsInt(ComboBox box, int fallback = 0)
        {
            var tag = (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return int.TryParse(tag, out var v) ? v : fallback;
        }

        private void OnBrowseFrontLibPath(object sender, RoutedEventArgs e)
        {
            var path = BrowseForCatalog("Select front art library catalog.json");
            if (path != null) FrontLibPathBox.Text = path;
        }

        private void OnBrowseBackLibPath(object sender, RoutedEventArgs e)
        {
            var path = BrowseForCatalog("Select back art library catalog.json");
            if (path != null) BackLibPathBox.Text = path;
        }

        private void OnResetFrontLibPath(object sender, RoutedEventArgs e)
        {
            FrontLibPathBox.Text = "(default)";
        }

        private void OnResetBackLibPath(object sender, RoutedEventArgs e)
        {
            BackLibPathBox.Text = "(default)";
        }

        private static string? BrowseForCatalog(string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Library Catalog (catalog.json)|catalog.json",
                Title = title
            };
            if (dialog.ShowDialog() != true) return null;
            return Path.GetDirectoryName(dialog.FileName);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var s = _settingsService.Settings;
            s.DefaultTokenText = TokenTextBox.Text;
            s.DefaultPagePreset = (PagePresetBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "A4";
            s.CheckForUpdates = UpdateCheckBox.IsChecked == true;
            s.MpcFillUseFavoritesOnly = UseFavoritesCheckBox.IsChecked == true;

            if (float.TryParse(BleedBox.Text, out var bleed))
                s.DefaultBleedMm = bleed;

            // MPCFill search defaults
            s.MpcFillDefaultSortBy = (SortByBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "nameAscending";
            s.MpcFillDefaultMinDpi = ParseTagAsInt(DefaultMinDpiBox);
            s.MpcFillDefaultMaxDpi = ParseTagAsInt(DefaultMaxDpiBox, 1500);
            if (int.TryParse(MaxSizeBox.Text, out var maxSize) && maxSize > 0)
                s.MpcFillMaximumSize = maxSize;
            s.MpcFillDefaultFuzzySearch = DefaultFuzzySearchBox.IsChecked == true;
            s.MpcFillFilterCardbacks = FilterCardbacksBox.IsChecked == true;

            // Card types (at least one required)
            var cardTypes = new List<string>();
            if (CardTypeCard.IsChecked == true) cardTypes.Add("CARD");
            if (CardTypeToken.IsChecked == true) cardTypes.Add("TOKEN");
            if (CardTypeCardback.IsChecked == true) cardTypes.Add("CARDBACK");
            if (cardTypes.Count == 0) cardTypes.Add("CARD");
            s.MpcFillCardTypes = cardTypes;

            // Languages
            var selectedLangs = new List<string>();
            if (LangEN.IsChecked == true) selectedLangs.Add("EN");
            if (LangJA.IsChecked == true) selectedLangs.Add("JA");
            if (LangFR.IsChecked == true) selectedLangs.Add("FR");
            if (LangDE.IsChecked == true) selectedLangs.Add("DE");
            if (LangES.IsChecked == true) selectedLangs.Add("ES");
            if (LangIT.IsChecked == true) selectedLangs.Add("IT");
            if (LangPT.IsChecked == true) selectedLangs.Add("PT");
            if (LangZH.IsChecked == true) selectedLangs.Add("ZH");
            if (LangRU.IsChecked == true) selectedLangs.Add("RU");
            if (LangAR.IsChecked == true) selectedLangs.Add("AR");
            if (LangSA.IsChecked == true) selectedLangs.Add("SA");
            s.MpcFillLanguages = selectedLangs;

            // Content filters
            s.MpcFillExcludeNsfw = ExcludeNsfwBox.IsChecked == true;
            s.MpcFillExcludeAiArt = ExcludeAiArtBox.IsChecked == true;

            // Library paths
            s.FrontArtLibraryPath = FrontLibPathBox.Text == "(default)" ? null : FrontLibPathBox.Text;
            s.BackArtLibraryPath = BackLibPathBox.Text == "(default)" ? null : BackLibPathBox.Text;

            // UI settings
            s.SidebarFontSize = FontSizeSlider.Value;

            _settingsService.Save();
            DialogResult = true;
        }
    }
}
