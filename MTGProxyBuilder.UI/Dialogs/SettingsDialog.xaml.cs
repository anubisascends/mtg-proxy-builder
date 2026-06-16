using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettingsService _settingsService;
        private readonly MpcFillSourceManager _sourceManager;
        private readonly MpcFillService? _mpcFillService;
        private readonly ProjectModel? _activeProject;

        public SettingsDialog(AppSettingsService settingsService, MpcFillSourceManager sourceManager,
            MpcFillService? mpcFillService = null, ProjectModel? activeProject = null)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _sourceManager = sourceManager;
            _mpcFillService = mpcFillService;
            _activeProject = activeProject;

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

            // Application settings
            BulkRefreshDaysBox.Text = s.BulkDataRefreshDays.ToString();

            // UI settings
            FontSizeSlider.Value = s.SidebarFontSize > 0 ? s.SidebarFontSize : 12;
            FontSizeLabel.Text = $"{FontSizeSlider.Value:0} pt";

            UpdateFavoritesInfo();

            // Printer profiles
            LoadPrinterProfiles();
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
            PagePrinter.Visibility = NavPrinter.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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

        // ==================== Printer Calibration ====================

        private void LoadPrinterProfiles()
        {
            PrinterProfileBox.Items.Clear();
            foreach (var profile in _settingsService.Settings.PrinterProfiles)
            {
                profile.MigrateLegacyOffsets();
                PrinterProfileBox.Items.Add(profile);
            }

            // Select the profile matching the saved name
            var selected = _settingsService.Settings.PrinterProfiles
                .FirstOrDefault(p => p.Name == _settingsService.Settings.DefaultPrinterProfileName);
            if (selected != null)
                PrinterProfileBox.SelectedItem = selected;
            else if (PrinterProfileBox.Items.Count > 0)
                PrinterProfileBox.SelectedIndex = 0;

            UpdatePrinterUI();
        }

        private void UpdatePrinterUI()
        {
            bool hasSelection = PrinterProfileBox.SelectedItem is PrinterProfile;
            ProfileNameBox.IsEnabled = hasSelection;
            OffsetTLXBox.IsEnabled = hasSelection;
            OffsetTLYBox.IsEnabled = hasSelection;
            OffsetTRXBox.IsEnabled = hasSelection;
            OffsetTRYBox.IsEnabled = hasSelection;
            OffsetBLXBox.IsEnabled = hasSelection;
            OffsetBLYBox.IsEnabled = hasSelection;
            OffsetBRXBox.IsEnabled = hasSelection;
            OffsetBRYBox.IsEnabled = hasSelection;
            CalibrationSummaryLabel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            ExportAlignmentBtn.IsEnabled = hasSelection;
            DefaultProfileCheck.IsEnabled = hasSelection;

            if (hasSelection && PrinterProfileBox.SelectedItem is PrinterProfile p)
                DefaultProfileCheck.IsChecked = p.Name == _settingsService.Settings.DefaultPrinterProfileName;
            else
                DefaultProfileCheck.IsChecked = false;
        }

        private void OnDefaultProfileChanged(object sender, RoutedEventArgs e)
        {
            if (PrinterProfileBox.SelectedItem is PrinterProfile profile)
            {
                _settingsService.Settings.DefaultPrinterProfileName =
                    DefaultProfileCheck.IsChecked == true ? profile.Name : null;
            }
        }

        private void OnPrinterProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrinterProfileBox.SelectedItem is PrinterProfile profile)
            {
                ProfileNameBox.Text = profile.Name;
                OffsetTLXBox.Text = profile.OffsetTLXMm.ToString(CultureInfo.InvariantCulture);
                OffsetTLYBox.Text = profile.OffsetTLYMm.ToString(CultureInfo.InvariantCulture);
                OffsetTRXBox.Text = profile.OffsetTRXMm.ToString(CultureInfo.InvariantCulture);
                OffsetTRYBox.Text = profile.OffsetTRYMm.ToString(CultureInfo.InvariantCulture);
                OffsetBLXBox.Text = profile.OffsetBLXMm.ToString(CultureInfo.InvariantCulture);
                OffsetBLYBox.Text = profile.OffsetBLYMm.ToString(CultureInfo.InvariantCulture);
                OffsetBRXBox.Text = profile.OffsetBRXMm.ToString(CultureInfo.InvariantCulture);
                OffsetBRYBox.Text = profile.OffsetBRYMm.ToString(CultureInfo.InvariantCulture);
                DefaultProfileCheck.IsChecked = profile.Name == _settingsService.Settings.DefaultPrinterProfileName;
                UpdateCalibrationSummary();
            }
            else
            {
                ProfileNameBox.Text = "";
                OffsetTLXBox.Text = "";
                OffsetTLYBox.Text = "";
                OffsetTRXBox.Text = "";
                OffsetTRYBox.Text = "";
                OffsetBLXBox.Text = "";
                OffsetBLYBox.Text = "";
                OffsetBRXBox.Text = "";
                OffsetBRYBox.Text = "";
                CalibrationSummaryLabel.Text = "";
            }
            UpdatePrinterUI();
        }

        private void OnProfileNameChanged(object sender, RoutedEventArgs e)
        {
            if (PrinterProfileBox.SelectedItem is not PrinterProfile profile) return;

            var newName = ProfileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName) || newName == profile.Name) return;

            // Ensure unique name
            bool duplicate = _settingsService.Settings.PrinterProfiles
                .Any(p => p != profile && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                MessageBox.Show($"A profile named \"{newName}\" already exists.", "Duplicate Name",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ProfileNameBox.Text = profile.Name;
                return;
            }

            profile.Name = newName;

            // Refresh the ComboBox display
            int idx = PrinterProfileBox.SelectedIndex;
            PrinterProfileBox.Items.Refresh();
            PrinterProfileBox.SelectedIndex = idx;
        }

        private void OnNewPrinterProfile(object sender, RoutedEventArgs e)
        {
            // Generate a unique default name
            int n = _settingsService.Settings.PrinterProfiles.Count + 1;
            string name = $"Printer {n}";
            while (_settingsService.Settings.PrinterProfiles.Any(
                p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                n++;
                name = $"Printer {n}";
            }

            var profile = new PrinterProfile { Name = name };
            _settingsService.Settings.PrinterProfiles.Add(profile);
            PrinterProfileBox.Items.Add(profile);
            PrinterProfileBox.SelectedItem = profile;
        }

        private void OnDeletePrinterProfile(object sender, RoutedEventArgs e)
        {
            if (PrinterProfileBox.SelectedItem is not PrinterProfile profile) return;

            var result = MessageBox.Show($"Delete profile \"{profile.Name}\"?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _settingsService.Settings.PrinterProfiles.Remove(profile);
            PrinterProfileBox.Items.Remove(profile);

            if (_settingsService.Settings.DefaultPrinterProfileName == profile.Name)
                _settingsService.Settings.DefaultPrinterProfileName = null;

            if (PrinterProfileBox.Items.Count > 0)
                PrinterProfileBox.SelectedIndex = 0;
            else
                OnPrinterProfileChanged(this, null!);
        }

        private void SaveCurrentProfileOffsets()
        {
            if (PrinterProfileBox.SelectedItem is not PrinterProfile profile) return;

            if (float.TryParse(OffsetTLXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tlx))
                profile.OffsetTLXMm = tlx;
            if (float.TryParse(OffsetTLYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tly))
                profile.OffsetTLYMm = tly;
            if (float.TryParse(OffsetTRXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var trx))
                profile.OffsetTRXMm = trx;
            if (float.TryParse(OffsetTRYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var try_))
                profile.OffsetTRYMm = try_;
            if (float.TryParse(OffsetBLXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var blx))
                profile.OffsetBLXMm = blx;
            if (float.TryParse(OffsetBLYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bly))
                profile.OffsetBLYMm = bly;
            if (float.TryParse(OffsetBRXBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var brx))
                profile.OffsetBRXMm = brx;
            if (float.TryParse(OffsetBRYBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bry))
                profile.OffsetBRYMm = bry;

            // Clear legacy fields
            profile.OffsetXMm = 0;
            profile.OffsetYMm = 0;
        }

        private void UpdateCalibrationSummary()
        {
            if (PrinterProfileBox.SelectedItem is not PrinterProfile profile)
            {
                CalibrationSummaryLabel.Text = "";
                return;
            }

            SaveCurrentProfileOffsets();

            float gridW = 200, gridH = 280; // defaults
            if (_activeProject != null)
            {
                var s = _activeProject.PageSettings;
                float cellW = s.CardWidthMm + 2 * s.BleedWidthMm;
                float cellH = s.CardHeightMm + 2 * s.BleedWidthMm;
                int cols = s.CardsPerRow;
                int rows = cols > 0 && s.CardsPerPage > 0 ? s.CardsPerPage / cols : 0;
                if (cols > 0 && rows > 0)
                {
                    gridW = cols * cellW;
                    gridH = rows * cellH;
                }
            }

            var cal = CalibrationTransform.Compute(profile, gridW, gridH);
            float xMm = cal.TranslateXPt / (72f / 25.4f);
            float yMm = cal.TranslateYPt / (72f / 25.4f);

            if (cal.HasCorrection)
            {
                string rot = Math.Abs(cal.RotationDegrees) > 0.001f
                    ? $", rotation {cal.RotationDegrees:+0.000;-0.000;0}deg" : "";
                CalibrationSummaryLabel.Text =
                    $"Computed: translation X {xMm:+0.00;-0.00;0}mm, Y {yMm:+0.00;-0.00;0}mm{rot}";
            }
            else
            {
                CalibrationSummaryLabel.Text = "No correction (all offsets zero)";
            }
        }

        private async void OnExportAlignmentPdf(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileOffsets();

            var profile = PrinterProfileBox.SelectedItem as PrinterProfile;
            if (profile == null) return;

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Export Alignment Test PDF",
                FileName = "alignment_test.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            var project = _activeProject ?? new ProjectModel();
            var pdfService = new PdfGeneratorService();
            bool ok = await pdfService.GenerateAlignmentPdfAsync(
                project, dlg.FileName, profile);

            if (ok)
                MessageBox.Show("Alignment test PDF exported successfully.", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show("Failed to generate alignment test PDF.", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Application settings
            if (int.TryParse(BulkRefreshDaysBox.Text, out var bulkDays) && bulkDays > 0)
                s.BulkDataRefreshDays = bulkDays;

            // UI settings
            s.SidebarFontSize = FontSizeSlider.Value;

            // Printer profiles — write current offsets back to the selected profile
            SaveCurrentProfileOffsets();

            _settingsService.Save();
            DialogResult = true;
        }
    }
}
