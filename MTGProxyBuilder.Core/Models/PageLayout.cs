using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MTGProxyBuilder.Core.Models
{
    public class PageLayout : INotifyPropertyChanged
    {
        private float _pageWidthMm = 210; // A4
        private float _pageHeightMm = 297; // A4
        private float _bleedWidthMm = Constants.DefaultBleedMm;
        private float _marginLeftMm;
        private float _marginTopMm;
        private float _marginRightMm;
        private float _marginBottomMm;
        private float _cardWidthMm = Constants.DefaultCardWidthMm;
        private float _cardHeightMm = Constants.DefaultCardHeightMm;
        private bool _isLandscape;
        private int? _columnsOverride;
        private int? _rowsOverride;
        private bool _isCentering;
        private float _horizontalSpacingMm;
        private float _verticalSpacingMm;

        public PageLayout()
        {
            // Run initial centering so margins start correct
            CenterGrid();
        }

        public float PageWidthMm
        {
            get => _pageWidthMm;
            set { _pageWidthMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        public float PageHeightMm
        {
            get => _pageHeightMm;
            set { _pageHeightMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        public float BleedWidthMm
        {
            get => _bleedWidthMm;
            set { _bleedWidthMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        /// <summary>Horizontal gap (mm) inserted between adjacent cells' bleed edges. 0 = cards touch.</summary>
        public float HorizontalSpacingMm
        {
            get => _horizontalSpacingMm;
            set { _horizontalSpacingMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        /// <summary>Vertical gap (mm) inserted between adjacent cells' bleed edges. 0 = cards touch.</summary>
        public float VerticalSpacingMm
        {
            get => _verticalSpacingMm;
            set { _verticalSpacingMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        // Margins: user can still edit these manually. They won't trigger re-centering.
        public float MarginLeftMm
        {
            get => _marginLeftMm;
            set { _marginLeftMm = value; OnPropertyChanged(); NotifyLayoutComputed(); }
        }

        public float MarginTopMm
        {
            get => _marginTopMm;
            set { _marginTopMm = value; OnPropertyChanged(); NotifyLayoutComputed(); }
        }

        public float MarginRightMm
        {
            get => _marginRightMm;
            set { _marginRightMm = value; OnPropertyChanged(); NotifyLayoutComputed(); }
        }

        public float MarginBottomMm
        {
            get => _marginBottomMm;
            set { _marginBottomMm = value; OnPropertyChanged(); NotifyLayoutComputed(); }
        }

        public float CardWidthMm
        {
            get => _cardWidthMm;
            set { _cardWidthMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        public float CardHeightMm
        {
            get => _cardHeightMm;
            set { _cardHeightMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        public bool IsLandscape
        {
            get => _isLandscape;
            set
            {
                if (_isLandscape == value) return;
                _isLandscape = value;

                // Only swap if the dimensions don't already match the orientation.
                // During deserialization, width/height may already be set to landscape
                // values, so swapping again would revert them to portrait.
                bool isCurrentlyWide = _pageWidthMm > _pageHeightMm;
                if (value != isCurrentlyWide)
                    (_pageWidthMm, _pageHeightMm) = (_pageHeightMm, _pageWidthMm);

                OnPropertyChanged();
                OnPropertyChanged(nameof(PageWidthMm));
                OnPropertyChanged(nameof(PageHeightMm));
                OnGridAffectingChange();
            }
        }

        public int? ColumnsOverride
        {
            get => _columnsOverride;
            set { _columnsOverride = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        public int? RowsOverride
        {
            get => _rowsOverride;
            set { _rowsOverride = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        // --- Computed properties ---

        /// <summary>Distance (mm) from one cell's left edge to the next: card + both bleeds + horizontal spacing.</summary>
        public float CellStrideXMm => CardWidthMm + 2 * BleedWidthMm + HorizontalSpacingMm;

        /// <summary>Distance (mm) from one cell's top edge to the next: card + both bleeds + vertical spacing.</summary>
        public float CellStrideYMm => CardHeightMm + 2 * BleedWidthMm + VerticalSpacingMm;

        /// <summary>Max columns that fit using the full page width (ignoring margins), accounting for inter-card spacing.</summary>
        public int AutoCardsPerRow
        {
            get
            {
                float stride = CellStrideXMm;
                return stride > 0 ? Math.Max(1, (int)((PageWidthMm + HorizontalSpacingMm) / stride)) : 0;
            }
        }

        /// <summary>Max rows that fit using the full page height (ignoring margins), accounting for inter-card spacing.</summary>
        public int AutoCardsPerColumn
        {
            get
            {
                float stride = CellStrideYMm;
                return stride > 0 ? Math.Max(1, (int)((PageHeightMm + VerticalSpacingMm) / stride)) : 0;
            }
        }

        public int CardsPerRow => ColumnsOverride ?? AutoCardsPerRow;
        public int CardsPerColumn => RowsOverride ?? AutoCardsPerColumn;
        public int CardsPerPage => CardsPerRow * CardsPerColumn;

        public float GetUsableWidthMm() => PageWidthMm - MarginLeftMm - MarginRightMm;
        public float GetUsableHeightMm() => PageHeightMm - MarginTopMm - MarginBottomMm;

        // --- Page presets ---

        public void ApplyPagePreset(string presetName)
        {
            if (presetName == "Custom") return; // Custom dimensions set directly

            var (w, h) = presetName switch
            {
                "A1" => (594f, 841f),
                "A2" => (420f, 594f),
                "A3" => (297f, 420f),
                "A4" => (210f, 297f),
                "Letter" => (215.9f, 279.4f),
                "Legal" => (215.9f, 355.6f),
                "Tabloid" => (279.4f, 431.8f),
                _ => (210f, 297f)
            };

            if (_isLandscape)
                (w, h) = (h, w);

            _pageWidthMm = w;
            _pageHeightMm = h;
            OnPropertyChanged(nameof(PageWidthMm));
            OnPropertyChanged(nameof(PageHeightMm));
            OnGridAffectingChange();
        }

        // --- Auto-centering ---

        /// <summary>
        /// Recalculates margins to center the card grid on the page.
        /// Called automatically when card size, page size, bleed, or grid overrides change.
        /// </summary>
        public void CenterGrid()
        {
            if (_isCentering) return;
            _isCentering = true;

            int cols = CardsPerRow;
            int rows = CardsPerColumn;

            if (cols <= 0) cols = 1;
            if (rows <= 0) rows = 1;

            float gridWidth = cols * (CardWidthMm + 2 * BleedWidthMm) + Math.Max(0, cols - 1) * HorizontalSpacingMm;
            float gridHeight = rows * (CardHeightMm + 2 * BleedWidthMm) + Math.Max(0, rows - 1) * VerticalSpacingMm;

            float hSpace = PageWidthMm - gridWidth;
            float vSpace = PageHeightMm - gridHeight;

            // Split remaining space evenly. If negative (overflow), use 0.
            float hMargin = Math.Max(0, hSpace / 2f);
            float vMargin = Math.Max(0, vSpace / 2f);

            // Round to 1 decimal for cleaner display
            _marginLeftMm = MathF.Round(hMargin, 1);
            _marginRightMm = MathF.Round(hMargin, 1);
            _marginTopMm = MathF.Round(vMargin, 1);
            _marginBottomMm = MathF.Round(vMargin, 1);

            OnPropertyChanged(nameof(MarginLeftMm));
            OnPropertyChanged(nameof(MarginRightMm));
            OnPropertyChanged(nameof(MarginTopMm));
            OnPropertyChanged(nameof(MarginBottomMm));

            _isCentering = false;
        }

        /// <summary>
        /// Called when a property that affects the grid layout changes.
        /// Re-centers margins, then notifies computed properties.
        /// </summary>
        private void OnGridAffectingChange()
        {
            CenterGrid();
            NotifyLayoutComputed();
        }

        private void NotifyLayoutComputed()
        {
            OnPropertyChanged(nameof(AutoCardsPerRow));
            OnPropertyChanged(nameof(AutoCardsPerColumn));
            OnPropertyChanged(nameof(CardsPerRow));
            OnPropertyChanged(nameof(CardsPerColumn));
            OnPropertyChanged(nameof(CardsPerPage));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
