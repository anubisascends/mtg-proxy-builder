using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;
using MTGProxyBuilder.UI.Services;
using Serilog;

namespace MTGProxyBuilder.UI.Dialogs
{
    public partial class PrintPreviewDialog : Window
    {
        private readonly List<BitmapSource> _pages;
        private readonly ProjectModel _project;
        private readonly PdfGeneratorService _pdfService;
        private readonly AppSettingsService _appSettings;

        private int _currentPageIndex;
        private double _zoomLevel = 1.0;
        private const double ZoomStep = 0.1;
        private const double MinZoom = 0.1;
        private const double MaxZoom = 5.0;

        // Panning state
        private bool _isPanning;
        private Point _panStart;
        private double _panStartHOffset;
        private double _panStartVOffset;

        public PrintPreviewDialog(
            List<BitmapSource> pages,
            ProjectModel project,
            PdfGeneratorService pdfService,
            AppSettingsService appSettings)
        {
            InitializeComponent();

            _pages = pages ?? throw new ArgumentNullException(nameof(pages));
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_pages.Count == 0)
            {
                PageTotalLabel.Text = "No pages";
                return;
            }

            _currentPageIndex = 0;
            ShowCurrentPage();

            // Defer fit-to-viewport until layout is complete
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                FitToViewport();
            }));
        }

        // ============================================================
        //  PAGE DISPLAY
        // ============================================================

        private void ShowCurrentPage()
        {
            if (_pages.Count == 0) return;

            var page = _pages[_currentPageIndex];
            PageImage.Source = page;

            UpdatePageLabel();
            UpdateInfoBar();
            UpdateNavigationButtons();
            ApplyZoom();
        }

        private void UpdatePageLabel()
        {
            int total = _pages.Count;
            int display = _currentPageIndex + 1;
            var mode = _project.PrintSettings.PrintMode;

            PageNumberBox.Text = display.ToString();
            PageTotalLabel.Text = $"of {total}";

            if (mode == PrintMode.Duplex)
            {
                bool isFront = _currentPageIndex % 2 == 0;
                int sheetNum = (_currentPageIndex / 2) + 1;
                PageSideLabel.Text = isFront ? $"(Front {sheetNum})" : $"(Back {sheetNum})";
            }
            else if (mode == PrintMode.BacksOnly)
            {
                PageSideLabel.Text = $"(Back {display})";
            }
            else
            {
                PageSideLabel.Text = "";
            }
        }

        private void GoToPage(int pageNumber)
        {
            int index = Math.Clamp(pageNumber - 1, 0, _pages.Count - 1);
            if (index != _currentPageIndex)
            {
                _currentPageIndex = index;
                ShowCurrentPage();
            }
            else
            {
                // Still update the textbox in case the user typed an out-of-range value
                PageNumberBox.Text = (_currentPageIndex + 1).ToString();
            }
        }

        private void OnPageNumberKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (int.TryParse(PageNumberBox.Text, out int num))
                    GoToPage(num);
                else
                    PageNumberBox.Text = (_currentPageIndex + 1).ToString();
                e.Handled = true;
                // Move focus away from the textbox
                PageScrollViewer.Focus();
            }
        }

        private void OnPageNumberLostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PageNumberBox.Text, out int num))
                GoToPage(num);
            else
                PageNumberBox.Text = (_currentPageIndex + 1).ToString();
        }

        private void UpdateInfoBar()
        {
            var settings = _project.PageSettings;
            var printSettings = _project.PrintSettings;
            int cardCount = _project.Cards.Sum(c => c.Quantity);

            string pageSize = $"{settings.PageWidthMm:F0} x {settings.PageHeightMm:F0} mm";
            string modeText = printSettings.PrintMode switch
            {
                PrintMode.Duplex => "Duplex",
                PrintMode.FrontsOnly => "Fronts Only",
                PrintMode.BacksOnly => "Backs Only",
                _ => printSettings.PrintMode.ToString()
            };
            string dpi = $"{printSettings.DPI} DPI";

            InfoLabel.Text = $"{pageSize}  |  {modeText}  |  {dpi}  |  {cardCount} card{(cardCount == 1 ? "" : "s")}  |  {_pages.Count} page{(_pages.Count == 1 ? "" : "s")}";
        }

        private void UpdateNavigationButtons()
        {
            PrevPageBtn.IsEnabled = _currentPageIndex > 0;
            NextPageBtn.IsEnabled = _currentPageIndex < _pages.Count - 1;
        }

        // ============================================================
        //  PAGE NAVIGATION
        // ============================================================

        private void OnPrevPageClick(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex--;
                ShowCurrentPage();
            }
        }

        private void OnNextPageClick(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex < _pages.Count - 1)
            {
                _currentPageIndex++;
                ShowCurrentPage();
            }
        }

        // ============================================================
        //  ZOOM
        // ============================================================

        private void ApplyZoom()
        {
            PageScale.ScaleX = _zoomLevel;
            PageScale.ScaleY = _zoomLevel;
            ZoomLabel.Text = $"{(int)Math.Round(_zoomLevel * 100)}%";
        }

        private void FitToViewport()
        {
            if (_pages.Count == 0) return;

            var page = _pages[_currentPageIndex];
            double viewW = PageScrollViewer.ViewportWidth - 40; // account for margin
            double viewH = PageScrollViewer.ViewportHeight - 40;

            if (viewW <= 0 || viewH <= 0) return;

            double scaleX = viewW / page.PixelWidth;
            double scaleY = viewH / page.PixelHeight;
            _zoomLevel = Math.Min(scaleX, scaleY);
            _zoomLevel = Math.Max(MinZoom, Math.Min(MaxZoom, _zoomLevel));

            ApplyZoom();
        }

        private void OnFitClick(object sender, RoutedEventArgs e)
        {
            FitToViewport();
        }

        private void OnActualSizeClick(object sender, RoutedEventArgs e)
        {
            _zoomLevel = 1.0;
            ApplyZoom();
        }

        // ============================================================
        //  KEYBOARD
        // ============================================================

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                OnPrevPageClick(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                OnNextPageClick(sender, e);
                e.Handled = true;
            }
        }

        // ============================================================
        //  MOUSE WHEEL ZOOM (Ctrl+Wheel)
        // ============================================================

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            double oldZoom = _zoomLevel;
            if (e.Delta > 0)
                _zoomLevel = Math.Min(MaxZoom, _zoomLevel + ZoomStep);
            else
                _zoomLevel = Math.Max(MinZoom, _zoomLevel - ZoomStep);

            if (Math.Abs(oldZoom - _zoomLevel) > 0.001)
                ApplyZoom();

            e.Handled = true;
        }

        // ============================================================
        //  PANNING (Middle mouse button drag)
        // ============================================================

        private void OnScrollViewerMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _isPanning = true;
                _panStart = e.GetPosition(PageScrollViewer);
                _panStartHOffset = PageScrollViewer.HorizontalOffset;
                _panStartVOffset = PageScrollViewer.VerticalOffset;
                PageScrollViewer.Cursor = Cursors.ScrollAll;
                PageScrollViewer.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnScrollViewerMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;

            var pos = e.GetPosition(PageScrollViewer);
            double dx = _panStart.X - pos.X;
            double dy = _panStart.Y - pos.Y;
            PageScrollViewer.ScrollToHorizontalOffset(_panStartHOffset + dx);
            PageScrollViewer.ScrollToVerticalOffset(_panStartVOffset + dy);
            e.Handled = true;
        }

        private void OnScrollViewerMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                PageScrollViewer.Cursor = Cursors.Arrow;
                PageScrollViewer.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // ============================================================
        //  EXPORT PDF
        // ============================================================

        private async void OnExportPdfClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Export PDF",
                FileName = $"{_project.ProjectName}.pdf"
            };

            if (dialog.ShowDialog(this) != true) return;

            ExportPdfBtn.IsEnabled = false;
            PrintBtn.IsEnabled = false;
            ShowBusy("Exporting PDF...", dialog.FileName);

            try
            {
                float offsetX = 0, offsetY = 0;
                var printerName = _project.PrinterProfileName;
                if (!string.IsNullOrEmpty(printerName))
                {
                    var profile = _appSettings.Settings.PrinterProfiles
                        .FirstOrDefault(p => p.Name == printerName);
                    if (profile != null)
                    {
                        offsetX = profile.OffsetXMm;
                        offsetY = profile.OffsetYMm;
                    }
                }

                bool success = await _pdfService.GeneratePdfAsync(
                    _project, dialog.FileName, offsetX, offsetY);

                if (success)
                {
                    MessageBox.Show(this,
                        $"PDF exported successfully to:\n{dialog.FileName}",
                        "Export Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(this,
                        "PDF export failed. Check the log for details.",
                        "Export Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PDF export from preview failed");
                MessageBox.Show(this,
                    $"Export failed: {ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusy();
                ExportPdfBtn.IsEnabled = true;
                PrintBtn.IsEnabled = true;
            }
        }

        // ============================================================
        //  PRINT
        // ============================================================

        private void ShowBusy(string message, string detail = "")
        {
            BusyText.Text = message;
            BusyDetail.Text = detail;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        private void HideBusy()
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
        }

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            var printDialog = new PrintDialog();

            if (printDialog.ShowDialog() != true) return;

            PrintBtn.IsEnabled = false;
            ExportPdfBtn.IsEnabled = false;

            try
            {
                // Get the printer's DPI from the print ticket
                float printerDpi = 300; // safe default
                var ticket = printDialog.PrintTicket;
                if (ticket?.PageResolution?.X > 0)
                    printerDpi = (float)ticket.PageResolution.X.Value;

                ShowBusy("Rendering pages...",
                    $"Re-rendering {_pages.Count} page(s) at {printerDpi} DPI for print");

                // Re-render all pages at the printer's DPI
                float offsetX = 0, offsetY = 0;
                var printerName = _project.PrinterProfileName;
                if (!string.IsNullOrEmpty(printerName))
                {
                    var profile = _appSettings.Settings.PrinterProfiles
                        .FirstOrDefault(p => p.Name == printerName);
                    if (profile != null)
                    {
                        offsetX = profile.OffsetXMm;
                        offsetY = profile.OffsetYMm;
                    }
                }

                var renderer = new PreviewRenderer();
                var hiResPages = await renderer.RenderAllPagesAsync(
                    _project, offsetX, offsetY, printerDpi);

                if (hiResPages.Count == 0)
                {
                    HideBusy();
                    MessageBox.Show(this, "No pages to print.", "Print",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ShowBusy("Sending to printer...",
                    $"{hiResPages.Count} page(s) at {printerDpi} DPI");

                // Print each page as a DrawingVisual at the correct physical size
                var paginator = new BitmapPagePaginator(hiResPages, _project.PageSettings, printerDpi);
                printDialog.PrintDocument(paginator, $"TCG Proxy Builder - {_project.ProjectName}");

                HideBusy();
                MessageBox.Show(this,
                    $"Sent {hiResPages.Count} page(s) to printer.",
                    "Print Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Print failed");
                HideBusy();
                MessageBox.Show(this, $"Print failed: {ex.Message}", "Print Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                HideBusy();
                PrintBtn.IsEnabled = true;
                ExportPdfBtn.IsEnabled = true;
            }
        }

        /// <summary>
        /// DocumentPaginator that prints pre-rendered bitmap pages at the correct physical size.
        /// </summary>
        private class BitmapPagePaginator : DocumentPaginator
        {
            private readonly List<BitmapSource> _pages;
            private readonly Size _pageSize;
            private readonly float _dpi;

            public BitmapPagePaginator(List<BitmapSource> pages, PageLayout layout, float dpi)
            {
                _pages = pages;
                _dpi = dpi;
                // WPF page size in device-independent pixels (96 DPI)
                _pageSize = new Size(
                    layout.PageWidthMm / 25.4 * 96,
                    layout.PageHeightMm / 25.4 * 96);
            }

            public override bool IsPageCountValid => true;
            public override int PageCount => _pages.Count;
            public override Size PageSize
            {
                get => _pageSize;
                set { }
            }
            public override IDocumentPaginatorSource? Source => null;

            public override DocumentPage GetPage(int pageNumber)
            {
                if (pageNumber < 0 || pageNumber >= _pages.Count)
                    return DocumentPage.Missing;

                var bmp = _pages[pageNumber];

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // Draw the bitmap scaled to fill the page at 96 DPI (WPF's device-independent unit)
                    dc.DrawImage(bmp, new Rect(0, 0, _pageSize.Width, _pageSize.Height));
                }

                return new DocumentPage(visual, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
            }
        }
    }
}
