using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Models
{
    /// <summary>Which side of the paper the punch strips are laid out along.</summary>
    public enum PunchSide
    {
        Long,
        Short
    }

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
        private float _punchOpeningMm;
        private PunchSide _punchSide = PunchSide.Short;
        private bool _isPunchModeEnabled;
        // Axis punch mode last managed (true = horizontal). Not persisted; used to release
        // the old axis when an orientation change moves the punch axis.
        private bool? _punchAxisHorizontal;

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

        // --- Punch alignment ---

        /// <summary>Width (mm) of the punch slot each cut strip slides into.</summary>
        public float PunchOpeningMm
        {
            get => _punchOpeningMm;
            set { _punchOpeningMm = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        /// <summary>Which side of the paper the strips are laid out along.</summary>
        public PunchSide PunchSide
        {
            get => _punchSide;
            set { _punchSide = value; OnPropertyChanged(); OnGridAffectingChange(); }
        }

        /// <summary>
        /// When on (and the punch settings are valid), the punch axis's spacing and
        /// column/row count are managed so every card sits centred in a strip exactly
        /// one opening wide, and cut guides are drawn at the strip boundaries.
        /// </summary>
        public bool IsPunchModeEnabled
        {
            get => _isPunchModeEnabled;
            set
            {
                if (_isPunchModeEnabled && !value)
                    ReleasePunchAxis(clearCount: false);
                _isPunchModeEnabled = value;
                OnPropertyChanged();
                OnGridAffectingChange();
            }
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

        /// <summary>Total grid width (mm) across all columns, including inter-card horizontal spacing.</summary>
        public float GridWidthMm => CardsPerRow * (CardWidthMm + 2 * BleedWidthMm) + Math.Max(0, CardsPerRow - 1) * HorizontalSpacingMm;

        /// <summary>Total grid height (mm) across all rows, including inter-card vertical spacing.</summary>
        public float GridHeightMm => CardsPerColumn * (CardHeightMm + 2 * BleedWidthMm) + Math.Max(0, CardsPerColumn - 1) * VerticalSpacingMm;

        public float GetUsableWidthMm() => PageWidthMm - MarginLeftMm - MarginRightMm;
        public float GetUsableHeightMm() => PageHeightMm - MarginTopMm - MarginBottomMm;

        // --- Punch alignment (computed) ---

        /// <summary>True when the punch strips run across the page width (columns); false for rows.</summary>
        [JsonIgnore]
        public bool IsPunchAxisHorizontal => (PunchSide == PunchSide.Long) == (PageWidthMm >= PageHeightMm);

        private float PunchCardDimMm => IsPunchAxisHorizontal ? CardWidthMm : CardHeightMm;
        private float PunchPageDimMm => IsPunchAxisHorizontal ? PageWidthMm : PageHeightMm;

        /// <summary>Why the current punch settings can't be applied, or null when they can.</summary>
        [JsonIgnore]
        public string? PunchValidationError
        {
            get
            {
                if (PunchOpeningMm <= 0)
                    return "Enter the punch opening size.";
                if (PunchOpeningMm <= PunchCardDimMm)
                    return $"Opening must be larger than the card {(IsPunchAxisHorizontal ? "width" : "height")} ({FormatMm(PunchCardDimMm)}).";
                if (PunchOpeningMm > PunchPageDimMm)
                    return $"Opening is larger than the paper's {(PunchSide == PunchSide.Long ? "long" : "short")} side ({FormatMm(PunchPageDimMm)}).";
                return null;
            }
        }

        /// <summary>Punch mode is on and its settings are valid, so it is driving the layout.</summary>
        [JsonIgnore]
        public bool IsPunchActive => IsPunchModeEnabled && PunchValidationError == null;

        /// <summary>Validation error to surface while punch mode is switched on, or null.</summary>
        [JsonIgnore]
        public string? PunchModeError => IsPunchModeEnabled ? PunchValidationError : null;

        [JsonIgnore]
        public bool IsPunchControllingColumns => IsPunchActive && IsPunchAxisHorizontal;

        [JsonIgnore]
        public bool IsPunchControllingRows => IsPunchActive && !IsPunchAxisHorizontal;

        /// <summary>Number of full-width strips that fit along the punch axis.</summary>
        [JsonIgnore]
        public int PunchStripCount => PunchOpeningMm > 0
            ? Math.Max(1, (int)((PunchPageDimMm + 0.001f) / PunchOpeningMm))
            : 0;

        /// <summary>Position (mm, from the left/top page edge) of the first strip boundary.</summary>
        private float PunchFirstCutMm =>
            (IsPunchAxisHorizontal ? MarginLeftMm : MarginTopMm) + BleedWidthMm + PunchCardDimMm / 2f - PunchOpeningMm / 2f;

        /// <summary>
        /// Strip boundary positions (mm) along the punch axis, measured from the left edge
        /// (horizontal axis) or top edge (vertical axis). Empty when punch mode isn't active.
        /// </summary>
        public float[] GetPunchCutPositionsMm()
        {
            if (!IsPunchActive) return Array.Empty<float>();
            int strips = IsPunchAxisHorizontal ? CardsPerRow : CardsPerColumn;
            float first = PunchFirstCutMm;
            var cuts = new float[strips + 1];
            for (int k = 0; k <= strips; k++)
                cuts[k] = first + k * PunchOpeningMm;
            return cuts;
        }

        /// <summary>
        /// Clip rectangle (mm) for the card at grid position (col, row) in punch mode: its
        /// strip along the punch axis, the full page across it. Bleed past the cut line is clipped.
        /// </summary>
        public (float X, float Y, float W, float H) GetPunchClipRectMm(int col, int row)
        {
            float first = PunchFirstCutMm;
            return IsPunchAxisHorizontal
                ? (first + col * PunchOpeningMm, 0, PunchOpeningMm, PageHeightMm)
                : (0, first + row * PunchOpeningMm, PageWidthMm, PunchOpeningMm);
        }

        /// <summary>Human-readable description of the active punch layout, or null.</summary>
        [JsonIgnore]
        public string? PunchSummary
        {
            get
            {
                var cuts = GetPunchCutPositionsMm();
                if (cuts.Length == 0) return null;
                string list = string.Join(", ", cuts.Select(c => c.ToString("0.##", CultureInfo.InvariantCulture)));
                return $"{cuts.Length - 1} strips × {FormatMm(PunchOpeningMm)}. Cuts at {list} mm from the {(IsPunchAxisHorizontal ? "left" : "top")} edge.";
            }
        }

        /// <summary>Warning when the outer cards fall inside the printer's no-print zone, or null.</summary>
        [JsonIgnore]
        public string? PunchNoPrintWarning
        {
            get
            {
                if (!IsPunchActive) return null;
                bool horizontal = IsPunchAxisHorizontal;
                int strips = horizontal ? CardsPerRow : CardsPerColumn;
                float page = PunchPageDimMm;
                float margin = horizontal ? MarginLeftMm : MarginTopMm;
                float first = PunchFirstCutMm;
                float last = first + strips * PunchOpeningMm;

                // Distance from the nearer paper edge to the outer trim edge, and to the outer
                // printed edge (bleed, clipped at the strip boundary).
                float trimNear = margin + BleedWidthMm;
                float trimFar = page - (trimNear + (strips - 1) * PunchOpeningMm + PunchCardDimMm);
                float trimDist = Math.Min(trimNear, trimFar);
                float printedNear = Math.Max(first, margin);
                float printedFar = page - Math.Min(last, margin + (strips - 1) * PunchOpeningMm + PunchCardDimMm + 2 * BleedWidthMm);
                float printedDist = Math.Min(printedNear, printedFar);

                string zone = FormatMm(Constants.NoPrintZoneMm);
                if (trimDist < Constants.NoPrintZoneMm)
                    return $"Outer cards are {FormatMm(trimDist)} from the paper edge, inside the ~{zone} no-print zone. The printer may clip the card itself.";
                if (printedDist < Constants.NoPrintZoneMm)
                    return $"Outer card bleed starts {FormatMm(printedDist)} from the paper edge, inside the ~{zone} no-print zone. The printer may clip the bleed.";
                return null;
            }
        }

        private static string FormatMm(float mm) => mm.ToString("0.##", CultureInfo.InvariantCulture) + " mm";

        /// <summary>
        /// Sets the punch axis's count to the number of full strips and its spacing so adjacent
        /// cards' trim edges are (opening − card) apart, which centres every card in its strip.
        /// Spacing goes negative when the bleed is wider than half the gap; that bleed is clipped
        /// at the cut line by the renderers.
        /// </summary>
        private void ApplyPunchLayout()
        {
            if (!IsPunchActive) return;

            bool horizontal = IsPunchAxisHorizontal;
            if (_punchAxisHorizontal is bool previous && previous != horizontal)
                ReleasePunchAxis(clearCount: true);
            _punchAxisHorizontal = horizontal;

            int strips = PunchStripCount;
            float spacing = PunchOpeningMm - PunchCardDimMm - 2 * BleedWidthMm;
            if (horizontal)
            {
                _columnsOverride = strips;
                _horizontalSpacingMm = spacing;
                OnPropertyChanged(nameof(ColumnsOverride));
                OnPropertyChanged(nameof(HorizontalSpacingMm));
            }
            else
            {
                _rowsOverride = strips;
                _verticalSpacingMm = spacing;
                OnPropertyChanged(nameof(RowsOverride));
                OnPropertyChanged(nameof(VerticalSpacingMm));
            }
        }

        /// <summary>
        /// Stops managing the axis punch mode last controlled. Negative spacing is clamped to 0;
        /// with <paramref name="clearCount"/> the axis also returns to auto-fit with no spacing.
        /// </summary>
        private void ReleasePunchAxis(bool clearCount)
        {
            if (_punchAxisHorizontal is not bool horizontal) return;
            _punchAxisHorizontal = null;

            if (horizontal)
            {
                if (clearCount) { _columnsOverride = null; _horizontalSpacingMm = 0; }
                else _horizontalSpacingMm = Math.Max(0, _horizontalSpacingMm);
                OnPropertyChanged(nameof(ColumnsOverride));
                OnPropertyChanged(nameof(HorizontalSpacingMm));
            }
            else
            {
                if (clearCount) { _rowsOverride = null; _verticalSpacingMm = 0; }
                else _verticalSpacingMm = Math.Max(0, _verticalSpacingMm);
                OnPropertyChanged(nameof(RowsOverride));
                OnPropertyChanged(nameof(VerticalSpacingMm));
            }
        }

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

            // Split remaining space evenly. If negative (overflow), use 0, rounded to 1 decimal
            // for cleaner display. The punch axis keeps the exact value, which may be negative
            // when clipped bleed overhangs the page, so strips stay centred on the paper.
            float hMargin = IsPunchControllingColumns ? hSpace / 2f : MathF.Round(Math.Max(0, hSpace / 2f), 1);
            float vMargin = IsPunchControllingRows ? vSpace / 2f : MathF.Round(Math.Max(0, vSpace / 2f), 1);

            _marginLeftMm = hMargin;
            _marginRightMm = hMargin;
            _marginTopMm = vMargin;
            _marginBottomMm = vMargin;

            OnPropertyChanged(nameof(MarginLeftMm));
            OnPropertyChanged(nameof(MarginRightMm));
            OnPropertyChanged(nameof(MarginTopMm));
            OnPropertyChanged(nameof(MarginBottomMm));

            _isCentering = false;
        }

        /// <summary>
        /// Called when a property that affects the grid layout changes.
        /// Re-applies punch alignment, re-centers margins, then notifies computed properties.
        /// </summary>
        private void OnGridAffectingChange()
        {
            ApplyPunchLayout();
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
            OnPropertyChanged(nameof(IsPunchAxisHorizontal));
            OnPropertyChanged(nameof(PunchValidationError));
            OnPropertyChanged(nameof(IsPunchActive));
            OnPropertyChanged(nameof(PunchModeError));
            OnPropertyChanged(nameof(IsPunchControllingColumns));
            OnPropertyChanged(nameof(IsPunchControllingRows));
            OnPropertyChanged(nameof(PunchStripCount));
            OnPropertyChanged(nameof(PunchSummary));
            OnPropertyChanged(nameof(PunchNoPrintWarning));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
