using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed class FixPreviewEntry<TPayload> where TPayload : class
    {
        public TPayload Payload { get; }
        public string LocationLabel { get; }
        public string ContextSnippet { get; }
        public ProofingIssue Issue { get; }
        public bool IsHighCertaintyDefault { get; }
        public bool IsChecked { get; set; }
        public string ChosenReplacement { get; set; }

        public FixPreviewEntry(
            TPayload payload,
            string locationLabel,
            string contextSnippet,
            ProofingIssue issue)
        {
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            LocationLabel = locationLabel ?? string.Empty;
            ContextSnippet = contextSnippet ?? string.Empty;
            Issue = issue ?? throw new ArgumentNullException(nameof(issue));
            IsHighCertaintyDefault = issue.IsHighCertaintyPrecheck;
            IsChecked = IsHighCertaintyDefault;
            ChosenReplacement = (issue.Suggestions != null && issue.Suggestions.Count > 0)
                ? issue.Suggestions[0].Replacement
                : string.Empty;
        }
    }

    internal sealed class FixPreviewStudioCallbacks<TPayload> where TPayload : class
    {
        public string HostTag { get; set; } = "excel";
        public Action<TPayload>? OnNavigateToItem { get; set; }
        public Func<IReadOnlyDictionary<TPayload, string>, int>? OnApplyBatch { get; set; }
        public Action<IReadOnlyList<TPayload>>? OnIgnoreItems { get; set; }
        public Action<string, IReadOnlyList<TPayload>>? OnAddWordToDictionary { get; set; }
        public Func<IReadOnlyList<FixPreviewEntry<TPayload>>>? OnRefreshEntries { get; set; }
        public Action? OnUndoLastFix { get; set; }
        public Action<IReadOnlyList<FixPreviewEntry<TPayload>>>? OnDockInTaskPane { get; set; }
        public Action<ProofingReportExportFormat>? OnExportReport { get; set; }
    }

    internal static class FixPreviewNativeMethods
    {
        public const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>
    /// Non-blocking Modeless High-DPI Safe Review &amp; Fix Studio for Microsoft Excel (100%–250% DPI deterministic layout).
    /// Keeps Excel 100% unlocked so users can freely scroll, select, and edit cells/sheets, minimize the window,
    /// roll up into a compact mini-bar, pin/unpin on top, dock directly into the right Task Pane, group repeated words,
    /// inspect invisible/lookalike diff badges, type custom edits, or apply formula-safe fixes one-by-one or in batch.
    /// </summary>
    internal sealed class FixAllPreviewDialog<TPayload> : Form where TPayload : class
    {
        private sealed class DisplayRow
        {
            public FixPreviewEntry<TPayload> Primary { get; }
            public List<FixPreviewEntry<TPayload>> Members { get; }

            public DisplayRow(FixPreviewEntry<TPayload> primary, List<FixPreviewEntry<TPayload>> members)
            {
                Primary = primary;
                Members = members;
            }

            public int Count => Members.Count;
            public bool IsChecked => Members.Any(m => m.IsChecked);
        }

        private readonly FixPreviewStudioCallbacks<TPayload> _callbacks;
        private readonly List<FixPreviewEntry<TPayload>> _allEntries;
        private List<DisplayRow> _visibleRows = new List<DisplayRow>();
        private string _scopeDisplayTitle;

        private int _dpi;
        private bool _inSectionLayout;
        private bool _isCompactMiniBar;
        private int _expandedClientHeight;
        private Size _expandedMinimumSize;
        private bool _isPinnedTopMost = true;
        private bool _isGroupedByWord;
        private bool _boundsLoaded;
        private bool _suppressSelectionChanged;
        private bool _keepGridFocusOnNextNavigate;

        private readonly Font _fontTitle;
        private readonly Font _fontSubtitle;
        private readonly Font _fontBody;
        private readonly Font _fontBodyBold;
        private readonly Font _fontBadge;

        private Panel _headerCard = null!;
        private Label _lblTitle = null!;
        private Label _lblGuide = null!;
        private FlowLayoutPanel _windowControlStrip = null!;
        private Button _btnPinTop = null!;
        private Button _btnCompact = null!;
        private Button _btnDockPane = null!;
        private Button _btnReportMenu = null!;
        private ContextMenuStrip _reportMenu = null!;
        private Button _btnRefresh = null!;
        private Button _btnUndo = null!;

        private Panel _toolBarPanel = null!;
        private Button _btnSafeOnly = null!;
        private Button _btnCheckAll = null!;
        private Button _btnUncheckAll = null!;
        private Button _btnGroupToggle = null!;
        private Label _lblSearchHint = null!;
        private TextBox _txtSearch = null!;

        private DataGridView _grid = null!;
        private DataGridViewTextBoxColumn _colCheck = null!;
        private DataGridViewTextBoxColumn _colLoc = null!;
        private DataGridViewTextBoxColumn _colOrig = null!;
        private DataGridViewTextBoxColumn _colRep = null!;
        private DataGridViewTextBoxColumn _colCert = null!;

        private Panel _detailCard = null!;
        private Label _lblContextPreview = null!;
        private FlowLayoutPanel _itemActionStrip = null!;
        private Button _btnFixNow = null!;
        private Button _btnIgnoreNow = null!;
        private Button _btnAddDict = null!;
        private Button _btnJump = null!;
        private Label _lblExplanation = null!;
        private FlowLayoutPanel _choicesPanel = null!;
        private Panel _customEditRow = null!;
        private Label _lblCustomEdit = null!;
        private TextBox _txtCustomReplacement = null!;
        private Button _btnSetCustom = null!;
        private Button _btnSetAndFix = null!;

        private Panel _footerPanel = null!;
        private Label _lblFooterSummary = null!;
        private Button _btnCancel = null!;
        private Button _btnConfirm = null!;

        private DisplayRow? _selectedRow;
        private readonly Timer _searchDebounceTimer;

        public Dictionary<TPayload, string> ApprovedReplacements { get; } = new Dictionary<TPayload, string>();

        public FixAllPreviewDialog(
            IReadOnlyList<FixPreviewEntry<TPayload>> entries,
            string scopeDisplayTitle,
            Action<TPayload>? onNavigateToItem = null,
            FixPreviewStudioCallbacks<TPayload>? callbacks = null)
        {
            _callbacks = callbacks ?? new FixPreviewStudioCallbacks<TPayload>
            {
                HostTag = "excel",
                OnNavigateToItem = onNavigateToItem
            };
            if (_callbacks.OnNavigateToItem == null && onNavigateToItem != null)
            {
                _callbacks.OnNavigateToItem = onNavigateToItem;
            }

            _scopeDisplayTitle = scopeDisplayTitle ?? "Excel";
            _allEntries = entries?.ToList() ?? new List<FixPreviewEntry<TPayload>>();
            _dpi = UiFontCache.GetEffectiveDpi(this);

            var savedPrefs = ReviewWindowPreferencesStore.Load(_callbacks.HostTag);
            _isPinnedTopMost = savedPrefs.IsPinnedTopMost;
            _isGroupedByWord = savedPrefs.IsGroupedByWord;

            _fontTitle = UiFontCache.CreateOwnedMyanmar(10.8f, FontStyle.Bold);
            _fontSubtitle = UiFontCache.CreateOwnedMyanmar(8.8f, FontStyle.Regular);
            _fontBody = UiFontCache.CreateOwnedMyanmar(9.4f, FontStyle.Regular);
            _fontBodyBold = UiFontCache.CreateOwnedMyanmar(9.4f, FontStyle.Bold);
            _fontBadge = UiFontCache.CreateOwnedMyanmar(8.5f, FontStyle.Bold);

            _searchDebounceTimer = new Timer { Interval = 140 };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                RebuildVisibleRowsAndPopulate(autoNavigate: false);
            };

            InitializeDialogUi(savedPrefs);
            RebuildVisibleRowsAndPopulate(autoNavigate: false);
            UpdateSummaryAndButtons();
            LayoutDialogSections();
        }

        private int S(int logicalPx) => UiFontCache.ScaleLogicalPxForDpi(_dpi, logicalPx);

        public void ReloadEntriesFromHost(IReadOnlyList<FixPreviewEntry<TPayload>> freshEntries, string? updatedScopeTitle = null)
        {
            if (IsDisposed)
                return;

            if (!string.IsNullOrEmpty(updatedScopeTitle))
            {
                _scopeDisplayTitle = updatedScopeTitle!;
            }

            var byPayload = new Dictionary<TPayload, (bool Checked, string Chosen)>();
            var byTextKey = new Dictionary<string, (bool Checked, string Chosen)>(StringComparer.Ordinal);
            foreach (var old in _allEntries)
            {
                byPayload[old.Payload] = (old.IsChecked, old.ChosenReplacement);
                string tKey = $"{old.LocationLabel}|{old.Issue.OriginalText}";
                if (!byTextKey.ContainsKey(tKey))
                    byTextKey[tKey] = (old.IsChecked, old.ChosenReplacement);
            }

            _allEntries.Clear();
            if (freshEntries != null)
            {
                foreach (var fe in freshEntries)
                {
                    if (byPayload.TryGetValue(fe.Payload, out var savedState) ||
                        byTextKey.TryGetValue($"{fe.LocationLabel}|{fe.Issue.OriginalText}", out savedState))
                    {
                        fe.IsChecked = savedState.Checked;
                        if (!string.IsNullOrEmpty(savedState.Chosen))
                            fe.ChosenReplacement = savedState.Chosen;
                    }
                    _allEntries.Add(fe);
                }
            }

            RefreshHeaderBannerTexts();
            RebuildVisibleRowsAndPopulate(autoNavigate: false);
            UpdateSummaryAndButtons();
            LayoutDialogSections();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SaveCurrentWindowPreferences(preferDockedOverride: null);
                DisposeAndClearChoicesPanel();
                _reportMenu?.Dispose();
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Dispose();
                _fontTitle.Dispose();
                _fontSubtitle.Dispose();
                _fontBody.Dispose();
                _fontBodyBold.Dispose();
                _fontBadge.Dispose();
            }
            base.Dispose(disposing);
        }

        private void DisposeAndClearChoicesPanel()
        {
            if (_choicesPanel == null || _choicesPanel.IsDisposed)
                return;

            while (_choicesPanel.Controls.Count > 0)
            {
                Control ctrl = _choicesPanel.Controls[0];
                _choicesPanel.Controls.RemoveAt(0);
                ctrl?.Dispose();
            }
        }

        private void SaveCurrentWindowPreferences(bool? preferDockedOverride)
        {
            try
            {
                var existing = ReviewWindowPreferencesStore.Load(_callbacks.HostTag);
                existing.IsPinnedTopMost = _isPinnedTopMost;
                existing.IsGroupedByWord = _isGroupedByWord;
                if (preferDockedOverride.HasValue)
                {
                    existing.PreferDockedInTaskPane = preferDockedOverride.Value;
                }

                if (_boundsLoaded && !_isCompactMiniBar && WindowState == FormWindowState.Normal && Width >= 320 && Height >= 260)
                {
                    existing.HasSavedBounds = true;
                    existing.X = DesktopBounds.X;
                    existing.Y = DesktopBounds.Y;
                    existing.Width = DesktopBounds.Width;
                    existing.Height = DesktopBounds.Height;
                }

                ReviewWindowPreferencesStore.Save(_callbacks.HostTag, existing);
            }
            catch { }
        }

        private void InitializeDialogUi(ReviewWindowPreferences savedPrefs)
        {
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = true;
            ShowIcon = true;
            TopMost = _isPinnedTopMost;
            KeyPreview = true;
            BackColor = Color.FromArgb(244, 249, 246);

            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int minW = Math.Min(S(640), Math.Max(S(440), (int)(workArea.Width * 0.44)));
            int minH = Math.Min(S(440), Math.Max(S(340), (int)(workArea.Height * 0.48)));
            int defaultW = Math.Max(minW, Math.Min(S(820), (int)(workArea.Width * 0.58)));
            int defaultH = Math.Max(minH, Math.Min(S(600), (int)(workArea.Height * 0.72)));

            _expandedMinimumSize = new Size(minW, minH);
            MinimumSize = _expandedMinimumSize;

            if (savedPrefs.HasSavedBounds && savedPrefs.Width >= minW && savedPrefs.Height >= minH)
            {
                StartPosition = FormStartPosition.Manual;
                Rectangle candidate = new Rectangle(savedPrefs.X, savedPrefs.Y, savedPrefs.Width, savedPrefs.Height);
                Rectangle screenArea = Screen.FromRectangle(candidate).WorkingArea;
                int clampedW = Math.Min(candidate.Width, screenArea.Width);
                int clampedH = Math.Min(candidate.Height, screenArea.Height);
                int clampedX = Math.Max(screenArea.Left, Math.Min(candidate.X, screenArea.Right - clampedW));
                int clampedY = Math.Max(screenArea.Top, Math.Min(candidate.Y, screenArea.Bottom - clampedH));
                Bounds = new Rectangle(clampedX, clampedY, clampedW, clampedH);
            }
            else
            {
                StartPosition = FormStartPosition.Manual;
                ClientSize = new Size(defaultW, defaultH);
                int leftX = workArea.Left + S(22);
                int topY = workArea.Top + Math.Max(S(28), (workArea.Height - Height) / 2);
                Location = new Point(leftX, topY);
            }
            _expandedClientHeight = ClientSize.Height;

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && !_txtSearch.Focused && !_txtCustomReplacement.Focused)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    e.Handled = true;
                }
            };

            // 0. Header Banner Card + Live Window Controls Strip
            _headerCard = new Panel
            {
                BackColor = Color.White
            };
            EnableDoubleBuffering(_headerCard);
            _headerCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(204, 226, 214)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _headerCard.Width - 1, _headerCard.Height - 1);
                }
                using (var accent = new SolidBrush(Color.FromArgb(16, 108, 56)))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, Math.Max(4, S(5)), _headerCard.Height);
                }
            };

            _lblTitle = new Label
            {
                Font = _fontTitle,
                ForeColor = Color.FromArgb(14, 54, 32),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _lblGuide = new Label
            {
                Font = _fontSubtitle,
                ForeColor = Color.FromArgb(48, 72, 58),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _windowControlStrip = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            _btnPinTop = CreateHeaderMiniButton(string.Empty);
            _btnPinTop.Click += (s, e) =>
            {
                _isPinnedTopMost = !_isPinnedTopMost;
                TopMost = _isPinnedTopMost;
                UpdatePinAndGroupButtonStyles();
                SaveCurrentWindowPreferences(preferDockedOverride: null);
            };

            _btnCompact = CreateHeaderMiniButton(UiLocalizer.Pick("▴ Compact", "▴ ခေါက်သိမ်း"));
            _btnCompact.Click += (s, e) => ToggleCompactMiniBar();

            _btnDockPane = CreateHeaderMiniButton(UiLocalizer.Pick("⇥ Dock in Pane", "⇥ ဘေးကွက်သို့ပို့"));
            _btnDockPane.Visible = _callbacks.OnDockInTaskPane != null;
            _btnDockPane.Click += (s, e) =>
            {
                if (_callbacks.OnDockInTaskPane != null)
                {
                    SaveCurrentWindowPreferences(preferDockedOverride: true);
                    var snapshot = _allEntries.ToList();
                    _callbacks.OnDockInTaskPane.Invoke(snapshot);
                    Close();
                }
            };

            _reportMenu = new ContextMenuStrip { Font = _fontBody };
            _reportMenu.Items.Add(
                UiLocalizer.Pick("▤ Excel Audit Workbook (4 Sheets)", "▤ Excel အစီရင်ခံစာ ဇယား (.xlsx)"),
                null,
                (s, e) => _callbacks.OnExportReport?.Invoke(ProofingReportExportFormat.NativeOfficeDocument));
            _reportMenu.Items.Add(
                UiLocalizer.Pick("◈ Print-Ready HTML / Save PDF", "◈ HTML / PDF ပုံနှိပ်အစီရင်ခံစာ"),
                null,
                (s, e) => _callbacks.OnExportReport?.Invoke(ProofingReportExportFormat.InteractiveHtmlAndPdf));
            _reportMenu.Items.Add(
                UiLocalizer.Pick("⎘ Copy Summary to Clipboard", "⎘ အနှစ်ချုပ် Clipboard ကူးယူမည်"),
                null,
                (s, e) => _callbacks.OnExportReport?.Invoke(ProofingReportExportFormat.CopySummaryToClipboard));

            _btnReportMenu = CreateToolbarButton(
                UiLocalizer.Pick("≡ Report ▾", "≡ အစီရင်ခံစာ ▾"),
                Color.FromArgb(228, 246, 235), Color.FromArgb(14, 88, 46), Color.FromArgb(156, 210, 178));
            _btnReportMenu.Visible = _callbacks.OnExportReport != null;
            _btnReportMenu.Click += (s, e) => _reportMenu.Show(_btnReportMenu, new Point(0, _btnReportMenu.Height));

            _btnRefresh = CreateHeaderMiniButton(UiLocalizer.Pick("↻ Refresh", "↻ ပြန်စစ်"));
            _btnRefresh.Visible = _callbacks.OnRefreshEntries != null;
            _btnRefresh.Click += (s, e) => TriggerLiveRefresh();

            _btnUndo = CreateHeaderMiniButton(UiLocalizer.Pick("↶ Undo", "↶ ပြန်ဆုတ်"));
            _btnUndo.Visible = _callbacks.OnUndoLastFix != null;
            _btnUndo.Click += (s, e) =>
            {
                _callbacks.OnUndoLastFix?.Invoke();
                TriggerLiveRefresh();
            };

            _windowControlStrip.Controls.AddRange(new Control[]
            {
                _btnPinTop,
                _btnCompact,
                _btnDockPane,
                _btnReportMenu,
                _btnRefresh,
                _btnUndo
            });

            _headerCard.Controls.Add(_lblTitle);
            _headerCard.Controls.Add(_windowControlStrip);
            _headerCard.Controls.Add(_lblGuide);

            // 1. Selection, Grouping & Search Toolbar
            _toolBarPanel = new Panel
            {
                BackColor = Color.Transparent
            };

            _btnSafeOnly = CreateToolbarButton(
                string.Empty,
                Color.FromArgb(224, 245, 233), Color.FromArgb(16, 102, 54), Color.FromArgb(148, 208, 172));
            _btnSafeOnly.Click += (s, e) =>
            {
                foreach (var item in _allEntries)
                    item.IsChecked = item.IsHighCertaintyDefault;
                RefreshCheckboxColumnValues();
            };

            _btnCheckAll = CreateToolbarButton(
                string.Empty,
                Color.White, Color.FromArgb(20, 62, 42), Color.FromArgb(188, 214, 200));
            _btnCheckAll.Click += (s, e) =>
            {
                foreach (var row in _visibleRows)
                    foreach (var m in row.Members)
                        m.IsChecked = true;
                RefreshCheckboxColumnValues();
            };

            _btnUncheckAll = CreateToolbarButton(
                UiLocalizer.Pick("☐ Clear All", "☐ အားလုံးဖြုတ်"),
                Color.White, Color.FromArgb(88, 102, 94), Color.FromArgb(204, 216, 208));
            _btnUncheckAll.Click += (s, e) =>
            {
                foreach (var row in _visibleRows)
                    foreach (var m in row.Members)
                        m.IsChecked = false;
                RefreshCheckboxColumnValues();
            };

            _btnGroupToggle = CreateToolbarButton(
                string.Empty,
                Color.White, Color.FromArgb(18, 86, 48), Color.FromArgb(172, 212, 188));
            _btnGroupToggle.Click += (s, e) =>
            {
                _isGroupedByWord = !_isGroupedByWord;
                UpdatePinAndGroupButtonStyles();
                SaveCurrentWindowPreferences(preferDockedOverride: null);
                RebuildVisibleRowsAndPopulate(autoNavigate: false);
                LayoutDialogSections();
            };

            _lblSearchHint = new Label
            {
                Text = UiLocalizer.Pick("Filter:", "ရှာရန် —"),
                Font = _fontBadge,
                ForeColor = Color.FromArgb(62, 84, 72),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };

            _txtSearch = new TextBox
            {
                Font = _fontBody
            };
            _txtSearch.HandleCreated += (s, e) =>
                FixPreviewNativeMethods.SendMessage(
                    _txtSearch.Handle,
                    FixPreviewNativeMethods.EM_SETCUEBANNER,
                    (IntPtr)1,
                    UiLocalizer.Pick("Filter cell or word...", "အကွက် သို့မဟုတ် စာလုံး ရှာရန်..."));
            _txtSearch.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };

            _toolBarPanel.Controls.AddRange(new Control[]
            {
                _btnSafeOnly,
                _btnCheckAll,
                _btnUncheckAll,
                _btnGroupToggle,
                _lblSearchHint,
                _txtSearch
            });

            // 2. Virtualized Checklist DataGridView
            int safeRowHeight = Math.Max(S(36), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBody, 13));
            int safeHeaderHeight = Math.Max(S(34), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBadge, 11));

            _grid = new DataGridView
            {
                VirtualMode = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(224, 236, 228),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = safeHeaderHeight,
                Font = _fontBody
            };
            _grid.RowTemplate.Height = safeRowHeight;
            EnableDoubleBuffering(_grid);

            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(232, 245, 238),
                ForeColor = Color.FromArgb(16, 54, 34),
                Font = _fontBadge,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(S(6), S(4), S(6), S(4)),
                WrapMode = DataGridViewTriState.False
            };

            _grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(24, 36, 30),
                SelectionBackColor = Color.FromArgb(218, 242, 228),
                SelectionForeColor = Color.FromArgb(10, 44, 24),
                Font = _fontBody,
                Padding = new Padding(S(6), S(2), S(6), S(2))
            };

            _colCheck = new DataGridViewTextBoxColumn
            {
                Name = "ColCheck",
                HeaderText = UiLocalizer.Pick("Apply", "ပြင်မည်"),
                Width = S(64),
                MinimumWidth = S(54),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = _fontBodyBold
                }
            };

            _colLoc = new DataGridViewTextBoxColumn
            {
                Name = "ColLocation",
                HeaderText = UiLocalizer.Pick("Cell", "အကွက်"),
                Width = S(142),
                MinimumWidth = S(98),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontBadge,
                    ForeColor = Color.FromArgb(18, 92, 52)
                }
            };

            _colOrig = new DataGridViewTextBoxColumn
            {
                Name = "ColOriginal",
                HeaderText = UiLocalizer.Pick("Original Text", "မူလစာသား"),
                MinimumWidth = S(110),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 42f
            };

            _colRep = new DataGridViewTextBoxColumn
            {
                Name = "ColReplacement",
                HeaderText = UiLocalizer.Pick("Replacement", "ပြင်ဆင်မည့်စာသား"),
                MinimumWidth = S(120),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 42f,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontBodyBold,
                    ForeColor = Color.FromArgb(16, 108, 56)
                }
            };

            _colCert = new DataGridViewTextBoxColumn
            {
                Name = "ColCertainty",
                HeaderText = UiLocalizer.Pick("Certainty", "စိတ်ချရမှု"),
                Width = S(186),
                MinimumWidth = S(130),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontBadge,
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                }
            };

            _grid.Columns.AddRange(_colCheck, _colLoc, _colOrig, _colRep, _colCert);

            _grid.CellValueNeeded += OnGridCellValueNeeded;
            _grid.CellFormatting += OnGridCellFormatting;
            _grid.CellClick += OnGridCellClick;
            _grid.CellDoubleClick += OnGridCellDoubleClick;
            _grid.KeyDown += OnGridKeyDown;
            _grid.SelectionChanged += (s, e) =>
            {
                if (!_suppressSelectionChanged)
                {
                    UpdateSelectedEntryInspector(navigateInHost: true);
                }
            };

            // 3. Selected Row Inspector + Per-Item Action Pills + Inline Custom Replacement
            _detailCard = new Panel
            {
                BackColor = Color.White,
                AutoScroll = true
            };
            EnableDoubleBuffering(_detailCard);
            _detailCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(208, 228, 216)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _detailCard.Width - 1, _detailCard.Height - 1);
                }
            };

            _lblContextPreview = new Label
            {
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(18, 52, 34),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _itemActionStrip = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            _btnFixNow = CreateActionPillButton(
                UiLocalizer.Pick("✓ Fix Now (Enter)", "✓ ယခုပြင်မည် (Enter)"),
                Color.FromArgb(16, 108, 56), Color.White, Color.FromArgb(12, 88, 44));
            _btnFixNow.Visible = _callbacks.OnApplyBatch != null;
            _btnFixNow.Click += (s, e) => ApplySelectedRowNow();

            _btnIgnoreNow = CreateActionPillButton(
                UiLocalizer.Pick("Ignore (Del)", "ကျော်မည် (Del)"),
                Color.FromArgb(245, 248, 246), Color.FromArgb(68, 82, 74), Color.FromArgb(202, 216, 208));
            _btnIgnoreNow.Visible = _callbacks.OnIgnoreItems != null;
            _btnIgnoreNow.Click += (s, e) => IgnoreSelectedRowNow();

            _btnAddDict = CreateActionPillButton(
                UiLocalizer.Pick("+ Add Word", "+ အဘိဓာန်ထည့်"),
                Color.FromArgb(234, 247, 239), Color.FromArgb(16, 96, 50), Color.FromArgb(168, 212, 184));
            _btnAddDict.Visible = _callbacks.OnAddWordToDictionary != null;
            _btnAddDict.Click += (s, e) => AddSelectedWordToCustomDictionary();

            _btnJump = CreateActionPillButton(
                UiLocalizer.Pick("▸ Jump", "▸ အကွက်သို့"),
                Color.FromArgb(240, 247, 243), Color.FromArgb(22, 82, 48), Color.FromArgb(186, 214, 198));
            _btnJump.Visible = _callbacks.OnNavigateToItem != null;
            _btnJump.Click += (s, e) =>
            {
                if (_selectedRow != null)
                {
                    _callbacks.OnNavigateToItem?.Invoke(_selectedRow.Primary.Payload);
                }
            };

            _itemActionStrip.Controls.AddRange(new Control[]
            {
                _btnFixNow,
                _btnIgnoreNow,
                _btnAddDict,
                _btnJump
            });

            _lblExplanation = new Label
            {
                Text = string.Empty,
                Font = _fontSubtitle,
                ForeColor = Color.FromArgb(64, 84, 72),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _choicesPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            _customEditRow = new Panel
            {
                BackColor = Color.Transparent
            };

            _lblCustomEdit = new Label
            {
                Text = UiLocalizer.Pick("✎ Custom:", "✎ စိတ်ကြိုက်ပြင်ရန် —"),
                Font = _fontBadge,
                ForeColor = Color.FromArgb(28, 68, 44),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _txtCustomReplacement = new TextBox
            {
                Font = _fontBody
            };
            _txtCustomReplacement.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    ApplyCustomTextToSelectedRow(fixImmediately: _callbacks.OnApplyBatch != null);
                }
            };

            _btnSetCustom = CreateActionPillButton(
                UiLocalizer.Pick("Set", "သတ်မှတ်"),
                Color.White, Color.FromArgb(20, 72, 42), Color.FromArgb(182, 210, 194));
            _btnSetCustom.Click += (s, e) => ApplyCustomTextToSelectedRow(fixImmediately: false);

            _btnSetAndFix = CreateActionPillButton(
                UiLocalizer.Pick("✓ Set & Fix", "✓ သတ်မှတ်ပြီးပြင်"),
                Color.FromArgb(224, 245, 233), Color.FromArgb(16, 102, 54), Color.FromArgb(148, 208, 172));
            _btnSetAndFix.Visible = _callbacks.OnApplyBatch != null;
            _btnSetAndFix.Click += (s, e) => ApplyCustomTextToSelectedRow(fixImmediately: true);

            _customEditRow.Controls.AddRange(new Control[]
            {
                _lblCustomEdit,
                _txtCustomReplacement,
                _btnSetCustom,
                _btnSetAndFix
            });

            _detailCard.Controls.AddRange(new Control[]
            {
                _lblContextPreview,
                _itemActionStrip,
                _lblExplanation,
                _choicesPanel,
                _customEditRow
            });

            // 4. Footer Bar
            _footerPanel = new Panel
            {
                BackColor = Color.Transparent
            };

            _lblFooterSummary = new Label
            {
                Text = string.Empty,
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(20, 56, 36),
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            _btnCancel = new Button
            {
                Text = UiLocalizer.Pick("Close (Esc)", "ပိတ်မည် (Esc)"),
                Font = _fontBody,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(62, 76, 68),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(192, 208, 198);
            _btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _btnConfirm = new Button
            {
                Text = UiLocalizer.Pick("✓ Apply Selected Fixes", "✓ ရွေးချယ်ထားသည်များကို ပြင်ဆင်မည်"),
                Font = _fontBodyBold,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 108, 56),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnConfirm.FlatAppearance.BorderColor = Color.FromArgb(12, 86, 44);
            _btnConfirm.FlatAppearance.MouseOverBackColor = Color.FromArgb(14, 96, 48);
            _btnConfirm.Click += OnConfirmClicked;

            _footerPanel.Controls.Add(_lblFooterSummary);
            _footerPanel.Controls.Add(_btnCancel);
            _footerPanel.Controls.Add(_btnConfirm);

            Controls.AddRange(new Control[]
            {
                _headerCard,
                _toolBarPanel,
                _grid,
                _detailCard,
                _footerPanel
            });

            RefreshHeaderBannerTexts();
            UpdatePinAndGroupButtonStyles();
            UiFontCache.DisableMnemonicsRecursive(this);
        }

        private void RefreshHeaderBannerTexts()
        {
            int totalCount = _allEntries.Count;
            int safeCount = _allEntries.Count(x => x.IsHighCertaintyDefault);
            int reviewCount = totalCount - safeCount;

            Text = UiLocalizer.Pick(
                $"Review & Fix — {_scopeDisplayTitle} ({totalCount})",
                $"စိစစ်ပြင်ဆင်ရန် — {_scopeDisplayTitle} ({totalCount})");

            if (_isCompactMiniBar)
            {
                int checkedCount = _allEntries.Count(x => x.IsChecked && !string.IsNullOrEmpty(x.ChosenReplacement));
                _lblTitle.Text = UiLocalizer.Pick(
                    $"Review & Fix • {totalCount} remaining ({checkedCount} checked) — Sheet is unlocked",
                    $"စိစစ်ပြင်ဆင်ရန် • ({totalCount}) ခုကျန် ({checkedCount} ခုရွေးထားသည်) — အကွက်များကို လွတ်လပ်စွာ ရေးသားနိုင်သည်");
            }
            else
            {
                _lblTitle.Text = UiLocalizer.Pick(
                    $"Live Review & Fix ({_scopeDisplayTitle})",
                    $"တိုက်ရိုက် စိစစ်ပြင်ဆင်ရန် ({_scopeDisplayTitle})");
            }

            _lblGuide.Text = UiLocalizer.Pick(
                $"✓ Excel stays unlocked (Formula-Safe). High-certainty ({safeCount}) pre-checked (☑); review items ({reviewCount}) unchecked (☐). Keys: Enter = Fix Now, Space = Toggle, Del = Ignore.",
                $"✓ နောက်ခံ Excel ဇယားကို လွတ်လပ်စွာ ကြည့်ရှုပြင်ဆင်နိုင်သည် (Formula မပျက်ပါ)။ သေချာသော ({safeCount}) ခုကို (☑) ရွေးပေးထားပြီး စိစစ်ရန် ({reviewCount}) ခုကို (☐) ချန်ထားသည်။");

            _btnSafeOnly.Text = UiLocalizer.Pick($"☑ Sure Only ({safeCount})", $"☑ သေချာသည်သာ ({safeCount})");
            _btnCheckAll.Text = UiLocalizer.Pick($"☑ All ({totalCount})", $"☑ အားလုံး ({totalCount})");
        }

        private void UpdatePinAndGroupButtonStyles()
        {
            _btnPinTop.Text = _isPinnedTopMost
                ? UiLocalizer.Pick("◈ Pin: ON", "◈ အပေါ်တင်: ON")
                : UiLocalizer.Pick("◇ Pin: OFF", "◇ အပေါ်တင်: OFF");
            _btnPinTop.BackColor = _isPinnedTopMost
                ? Color.FromArgb(222, 244, 231)
                : Color.FromArgb(244, 248, 246);
            _btnPinTop.ForeColor = _isPinnedTopMost
                ? Color.FromArgb(14, 88, 46)
                : Color.FromArgb(78, 92, 84);
            _btnPinTop.FlatAppearance.BorderColor = _isPinnedTopMost
                ? Color.FromArgb(142, 204, 168)
                : Color.FromArgb(204, 216, 208);

            _btnGroupToggle.Text = _isGroupedByWord
                ? UiLocalizer.Pick("⊞ Grouped: ON", "⊞ စုစည်းမှု: ON")
                : UiLocalizer.Pick("⊞ Group Same", "⊞ တူရာစုမည်");
            _btnGroupToggle.BackColor = _isGroupedByWord
                ? Color.FromArgb(222, 244, 231)
                : Color.White;
            _btnGroupToggle.ForeColor = _isGroupedByWord
                ? Color.FromArgb(14, 88, 46)
                : Color.FromArgb(18, 86, 48);
            _btnGroupToggle.FlatAppearance.BorderColor = _isGroupedByWord
                ? Color.FromArgb(142, 204, 168)
                : Color.FromArgb(172, 212, 188);
        }

        private void ToggleCompactMiniBar()
        {
            _isCompactMiniBar = !_isCompactMiniBar;
            if (_isCompactMiniBar)
            {
                if (WindowState == FormWindowState.Maximized)
                {
                    WindowState = FormWindowState.Normal;
                }
                _expandedClientHeight = Math.Max(S(360), ClientSize.Height);
                _btnCompact.Text = UiLocalizer.Pick("▾ Expand", "▾ ပြန်ချဲ့မည်");
                _lblGuide.Visible = false;
                _toolBarPanel.Visible = false;
                _grid.Visible = false;
                _detailCard.Visible = false;
                _footerPanel.Visible = false;
                RefreshHeaderBannerTexts();
                LayoutDialogSections();
                int miniH = _headerCard.Bottom + S(10);
                MinimumSize = new Size(S(420), miniH);
                ClientSize = new Size(ClientSize.Width, miniH);
            }
            else
            {
                _btnCompact.Text = UiLocalizer.Pick("▴ Compact", "▴ ခေါက်သိမ်း");
                _lblGuide.Visible = true;
                _toolBarPanel.Visible = true;
                _grid.Visible = true;
                _detailCard.Visible = true;
                _footerPanel.Visible = true;
                MinimumSize = _expandedMinimumSize;
                ClientSize = new Size(ClientSize.Width, Math.Max(_expandedMinimumSize.Height, _expandedClientHeight));
                RefreshHeaderBannerTexts();
                LayoutDialogSections();
            }
        }

        private void TriggerLiveRefresh()
        {
            if (_callbacks.OnRefreshEntries == null)
                return;

            var fresh = _callbacks.OnRefreshEntries.Invoke();
            ReloadEntriesFromHost(fresh);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _boundsLoaded = true;
            RefreshDpiIfNeededAndLayout();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (_boundsLoaded && !_isCompactMiniBar && WindowState == FormWindowState.Normal)
            {
                SaveCurrentWindowPreferences(preferDockedOverride: null);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_headerCard != null)
            {
                LayoutDialogSections();
                if (_boundsLoaded && !_isCompactMiniBar && WindowState == FormWindowState.Normal)
                {
                    SaveCurrentWindowPreferences(preferDockedOverride: null);
                }
            }
        }

        private void RefreshDpiIfNeededAndLayout()
        {
            int liveDpi = UiFontCache.GetEffectiveDpi(this);
            if (liveDpi != _dpi && liveDpi > 0)
            {
                _dpi = liveDpi;
                int safeRowHeight = Math.Max(S(36), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBody, 13));
                int safeHeaderHeight = Math.Max(S(34), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBadge, 11));
                _grid.ColumnHeadersHeight = safeHeaderHeight;
                _grid.RowTemplate.Height = safeRowHeight;
                _colCheck.Width = S(64);
                _colLoc.Width = S(142);
                _colCert.Width = S(186);
            }
            LayoutDialogSections();
        }

        private Size MeasureSingleLineButtonSize(string text, Font font, int padHorizLogical, int padVertLogical)
        {
            Size textSz = TextRenderer.MeasureText(
                text ?? string.Empty,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            int h = UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, font, padVertLogical);
            int w = textSz.Width + S(padHorizLogical);
            return new Size(w, h);
        }

        private void LayoutDialogSections()
        {
            if (_inSectionLayout || _headerCard == null || ClientSize.Width <= 40 || ClientSize.Height <= 30)
                return;

            _inSectionLayout = true;
            SuspendLayout();
            try
            {
                int cw = ClientSize.Width;
                int ch = ClientSize.Height;
                int padX = S(12);
                int padY = S(10);
                int gap = S(7);
                int innerW = Math.Max(S(240), cw - padX * 2);

                int headerPadL = S(14);
                int headerPadR = S(10);
                int headerPadV = S(8);
                int headerFullW = Math.Max(S(200), innerW - headerPadL - headerPadR);

                foreach (Control c in _windowControlStrip.Controls)
                {
                    if (c is Button hb && hb.Visible)
                    {
                        hb.Size = MeasureSingleLineButtonSize(hb.Text, _fontBadge, 14, 6);
                        hb.Margin = new Padding(0, 0, S(4), S(2));
                    }
                }

                Size stripPref = _windowControlStrip.GetPreferredSize(new Size(headerFullW, 0));
                bool singleHeaderTopRow = stripPref.Width + S(220) <= headerFullW;
                int headerBottom;

                if (singleHeaderTopRow)
                {
                    int titleMaxW = Math.Max(S(160), headerFullW - stripPref.Width - S(8));
                    _lblTitle.MaximumSize = new Size(titleMaxW, 0);
                    _lblTitle.Location = new Point(headerPadL, headerPadV);
                    _lblTitle.Size = _lblTitle.GetPreferredSize(new Size(titleMaxW, 0));

                    _windowControlStrip.Location = new Point(innerW - headerPadR - stripPref.Width, headerPadV - S(1));
                    _windowControlStrip.Size = stripPref;
                    headerBottom = Math.Max(_lblTitle.Bottom, _windowControlStrip.Bottom);
                }
                else
                {
                    _lblTitle.MaximumSize = new Size(headerFullW, 0);
                    _lblTitle.Location = new Point(headerPadL, headerPadV);
                    _lblTitle.Size = _lblTitle.GetPreferredSize(new Size(headerFullW, 0));

                    _windowControlStrip.MaximumSize = new Size(headerFullW, 0);
                    _windowControlStrip.Location = new Point(headerPadL, _lblTitle.Bottom + S(3));
                    _windowControlStrip.Size = _windowControlStrip.GetPreferredSize(new Size(headerFullW, 0));
                    headerBottom = _windowControlStrip.Bottom;
                }

                if (_lblGuide.Visible)
                {
                    _lblGuide.MaximumSize = new Size(headerFullW, 0);
                    _lblGuide.Location = new Point(headerPadL, headerBottom + S(3));
                    _lblGuide.Size = _lblGuide.GetPreferredSize(new Size(headerFullW, 0));
                    headerBottom = _lblGuide.Bottom;
                }

                int headerH = headerBottom + headerPadV;
                if (!_isCompactMiniBar)
                {
                    headerH = Math.Min((int)(ch * 0.25), headerH);
                }
                _headerCard.SetBounds(padX, padY, innerW, Math.Max(S(40), headerH));

                if (_isCompactMiniBar)
                    return;

                Size safeSz = MeasureSingleLineButtonSize(_btnSafeOnly.Text, _fontBadge, 18, 8);
                Size allSz = MeasureSingleLineButtonSize(_btnCheckAll.Text, _fontBadge, 18, 8);
                Size clrSz = MeasureSingleLineButtonSize(_btnUncheckAll.Text, _fontBadge, 18, 8);
                Size grpSz = MeasureSingleLineButtonSize(_btnGroupToggle.Text, _fontBadge, 18, 8);
                int toolBtnH = Math.Max(Math.Max(safeSz.Height, allSz.Height), Math.Max(clrSz.Height, grpSz.Height));
                int btnGap = S(5);

                _btnSafeOnly.SetBounds(0, 0, safeSz.Width, toolBtnH);
                _btnCheckAll.SetBounds(_btnSafeOnly.Right + btnGap, 0, allSz.Width, toolBtnH);
                _btnUncheckAll.SetBounds(_btnCheckAll.Right + btnGap, 0, clrSz.Width, toolBtnH);
                _btnGroupToggle.SetBounds(_btnUncheckAll.Right + btnGap, 0, grpSz.Width, toolBtnH);

                Size hintTextSz = TextRenderer.MeasureText(
                    _lblSearchHint.Text,
                    _fontBadge,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                int hintW = hintTextSz.Width + S(6);
                int buttonsRight = _btnGroupToggle.Right;
                int availRight = innerW - buttonsRight - S(10);

                int toolH;
                if (availRight >= hintW + S(120))
                {
                    int searchW = Math.Min(S(210), availRight - hintW - S(4));
                    int txtH = _txtSearch.PreferredHeight;
                    int txtY = Math.Max(0, (toolBtnH - txtH) / 2);
                    _txtSearch.SetBounds(innerW - searchW, txtY, searchW, txtH);
                    _lblSearchHint.SetBounds(_txtSearch.Left - hintW - S(4), 0, hintW, toolBtnH);
                    toolH = Math.Max(toolBtnH, txtH);
                }
                else
                {
                    int row2Y = toolBtnH + S(5);
                    int txtH = _txtSearch.PreferredHeight;
                    int row2H = Math.Max(txtH, S(24));
                    _lblSearchHint.SetBounds(0, row2Y, hintW, row2H);
                    int searchX = hintW + S(6);
                    _txtSearch.SetBounds(searchX, row2Y + Math.Max(0, (row2H - txtH) / 2), Math.Max(S(110), innerW - searchX), txtH);
                    toolH = row2Y + row2H;
                }
                _toolBarPanel.SetBounds(padX, _headerCard.Bottom + gap, innerW, toolH);

                Size cancelSz = MeasureSingleLineButtonSize(_btnCancel.Text, _fontBody, 24, 10);
                Size confirmSz = MeasureSingleLineButtonSize(_btnConfirm.Text, _fontBodyBold, 30, 10);
                int footerH = Math.Max(cancelSz.Height, confirmSz.Height);

                _btnConfirm.SetBounds(innerW - confirmSz.Width, 0, confirmSz.Width, footerH);
                _btnCancel.SetBounds(_btnConfirm.Left - S(8) - cancelSz.Width, 0, cancelSz.Width, footerH);
                int summaryAvailW = Math.Max(S(80), _btnCancel.Left - S(12));
                _lblFooterSummary.SetBounds(S(2), 0, summaryAvailW, footerH);

                int footerTop = ch - padY - footerH;
                _footerPanel.SetBounds(padX, footerTop, innerW, footerH);

                int middleTop = _toolBarPanel.Bottom + gap;
                int middleBottom = footerTop - gap;
                int middleAvailH = Math.Max(S(140), middleBottom - middleTop);

                int desiredDetailH = LayoutDetailCardChildren(innerW, reserveScrollbar: false);
                int maxDetailH = Math.Max(S(96), (int)(middleAvailH * 0.46));
                int finalDetailH;
                if (desiredDetailH > maxDetailH)
                {
                    LayoutDetailCardChildren(innerW, reserveScrollbar: true);
                    finalDetailH = maxDetailH;
                }
                else
                {
                    finalDetailH = Math.Max(S(88), desiredDetailH);
                }

                int detailTop = middleBottom - finalDetailH;
                _detailCard.SetBounds(padX, detailTop, innerW, finalDetailH);

                int gridH = Math.Max(S(92), (detailTop - gap) - middleTop);
                _grid.SetBounds(padX, middleTop, innerW, gridH);
            }
            finally
            {
                ResumeLayout(false);
                _inSectionLayout = false;
            }
        }

        private int LayoutDetailCardChildren(int cardWidth, bool reserveScrollbar)
        {
            int padH = S(10);
            int padV = S(7);
            int scrollW = reserveScrollbar ? SystemInformation.VerticalScrollBarWidth + S(4) : 0;
            int contentW = Math.Max(S(180), cardWidth - padH * 2 - scrollW);

            foreach (Control c in _itemActionStrip.Controls)
            {
                if (c is Button ab && ab.Visible)
                {
                    ab.Size = MeasureSingleLineButtonSize(ab.Text, _fontBadge, 16, 6);
                    ab.Margin = new Padding(0, 0, S(5), S(2));
                }
            }

            Size actionStripPref = _itemActionStrip.GetPreferredSize(new Size(contentW, 0));
            bool singleTopRow = actionStripPref.Width + S(200) <= contentW;
            int curY;

            if (singleTopRow)
            {
                int ctxMaxW = Math.Max(S(140), contentW - actionStripPref.Width - S(8));
                _lblContextPreview.MaximumSize = new Size(ctxMaxW, 0);
                _lblContextPreview.Location = new Point(padH, padV);
                _lblContextPreview.Size = _lblContextPreview.GetPreferredSize(new Size(ctxMaxW, 0));

                _itemActionStrip.Location = new Point(padH + contentW - actionStripPref.Width, padV - S(1));
                _itemActionStrip.Size = actionStripPref;
                curY = Math.Max(_lblContextPreview.Bottom, _itemActionStrip.Bottom) + S(4);
            }
            else
            {
                _lblContextPreview.MaximumSize = new Size(contentW, 0);
                _lblContextPreview.Location = new Point(padH, padV);
                _lblContextPreview.Size = _lblContextPreview.GetPreferredSize(new Size(contentW, 0));

                _itemActionStrip.MaximumSize = new Size(contentW, 0);
                _itemActionStrip.Location = new Point(padH, _lblContextPreview.Bottom + S(3));
                _itemActionStrip.Size = _itemActionStrip.GetPreferredSize(new Size(contentW, 0));
                curY = _itemActionStrip.Bottom + S(4);
            }

            if (!string.IsNullOrEmpty(_lblExplanation.Text))
            {
                _lblExplanation.Visible = true;
                _lblExplanation.MaximumSize = new Size(contentW, 0);
                _lblExplanation.Location = new Point(padH, curY);
                _lblExplanation.Size = _lblExplanation.GetPreferredSize(new Size(contentW, 0));
                curY = _lblExplanation.Bottom + S(4);
            }
            else
            {
                _lblExplanation.Visible = false;
            }

            if (_choicesPanel.Controls.Count > 0)
            {
                _choicesPanel.Visible = true;
                int maxBtnW = Math.Max(S(140), contentW - S(4));
                foreach (Control child in _choicesPanel.Controls)
                {
                    if (child is WordWrapChoiceButton wrapBtn)
                    {
                        wrapBtn.MaximumSize = new Size(maxBtnW, 0);
                    }
                }
                _choicesPanel.MaximumSize = new Size(contentW, 0);
                _choicesPanel.Location = new Point(padH, curY);
                Size choicesPref = _choicesPanel.GetPreferredSize(new Size(contentW, 0));
                _choicesPanel.Size = new Size(contentW, choicesPref.Height);
                curY = _choicesPanel.Bottom + S(4);
            }
            else
            {
                _choicesPanel.Visible = false;
            }

            if (_selectedRow != null)
            {
                _customEditRow.Visible = true;
                Size lblSz = _lblCustomEdit.GetPreferredSize(new Size(contentW, 0));
                Size setSz = MeasureSingleLineButtonSize(_btnSetCustom.Text, _fontBadge, 16, 6);
                Size setFixSz = _btnSetAndFix.Visible
                    ? MeasureSingleLineButtonSize(_btnSetAndFix.Text, _fontBadge, 16, 6)
                    : Size.Empty;
                int txtH = _txtCustomReplacement.PreferredHeight;
                int rowH = Math.Max(txtH, Math.Max(lblSz.Height, Math.Max(setSz.Height, setFixSz.Height)));

                _lblCustomEdit.Location = new Point(0, Math.Max(0, (rowH - lblSz.Height) / 2));
                int rightButtonsW = setSz.Width + (_btnSetAndFix.Visible ? S(5) + setFixSz.Width : 0);
                int txtLeft = _lblCustomEdit.Right + S(6);
                int txtAvailW = Math.Max(S(90), Math.Min(S(280), contentW - txtLeft - rightButtonsW - S(8)));

                _txtCustomReplacement.SetBounds(txtLeft, Math.Max(0, (rowH - txtH) / 2), txtAvailW, txtH);
                _btnSetCustom.SetBounds(_txtCustomReplacement.Right + S(6), Math.Max(0, (rowH - setSz.Height) / 2), setSz.Width, setSz.Height);
                if (_btnSetAndFix.Visible)
                {
                    _btnSetAndFix.SetBounds(_btnSetCustom.Right + S(5), Math.Max(0, (rowH - setFixSz.Height) / 2), setFixSz.Width, setFixSz.Height);
                }

                _customEditRow.SetBounds(padH, curY, contentW, rowH);
                curY = _customEditRow.Bottom;
            }
            else
            {
                _customEditRow.Visible = false;
            }

            return curY + padV + S(2);
        }

        private Button CreateHeaderMiniButton(string text) =>
            CreateToolbarButton(text, Color.FromArgb(242, 248, 244), Color.FromArgb(20, 68, 40), Color.FromArgb(196, 218, 204));

        private Button CreateActionPillButton(string text, Color bg, Color fg, Color border) =>
            CreateToolbarButton(text, bg, fg, border);

        private Button CreateToolbarButton(string text, Color bg, Color fg, Color border)
        {
            var btn = new Button
            {
                Text = text,
                Font = _fontBadge,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderColor = border;
            return btn;
        }

        private void RebuildVisibleRowsAndPopulate(bool autoNavigate, int preferredRowIndex = 0)
        {
            string q = (_txtSearch?.Text ?? string.Empty).Trim();
            IEnumerable<FixPreviewEntry<TPayload>> filtered = _allEntries;
            if (!string.IsNullOrEmpty(q))
            {
                filtered = _allEntries.Where(x =>
                    x.LocationLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.Issue.OriginalText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.ChosenReplacement.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.ContextSnippet.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_isGroupedByWord)
            {
                _visibleRows = filtered
                    .GroupBy(x => UnicodeNormalizer.NormalizeCanonicalOrder((x.Issue.OriginalText ?? string.Empty).Trim()), StringComparer.Ordinal)
                    .Select(g =>
                    {
                        var list = g.ToList();
                        return new DisplayRow(list[0], list);
                    })
                    .ToList();
            }
            else
            {
                _visibleRows = filtered
                    .Select(x => new DisplayRow(x, new List<FixPreviewEntry<TPayload>>(1) { x }))
                    .ToList();
            }

            PopulateGridRows(preferredRowIndex, autoNavigate);
            UpdateSummaryAndButtons();
        }

        private void PopulateGridRows(int preferredRowIndex = 0, bool autoNavigate = false)
        {
            _suppressSelectionChanged = true;
            _grid.SuspendLayout();
            try
            {
                _grid.CurrentCell = null;
                _grid.RowCount = 0;
                _grid.RowCount = _visibleRows.Count;

                if (_visibleRows.Count > 0)
                {
                    int targetIdx = Math.Max(0, Math.Min(_visibleRows.Count - 1, preferredRowIndex));
                    _grid.Rows[targetIdx].Selected = true;
                    try { _grid.CurrentCell = _grid.Rows[targetIdx].Cells[0]; } catch { }
                }
            }
            finally
            {
                _grid.ResumeLayout();
                _suppressSelectionChanged = false;
            }

            _grid.Invalidate();
            UpdateSelectedEntryInspector(navigateInHost: autoNavigate);
        }

        private static string FormatCheckGlyph(bool isChecked) => isChecked ? "☑" : "☐";

        private static string FormatLocationCellText(DisplayRow row) =>
            row.Count > 1
                ? $"{row.Primary.LocationLabel} (+{row.Count - 1})"
                : row.Primary.LocationLabel;

        private static string FormatOriginalCellText(DisplayRow row)
        {
            string vis = row.Primary.Issue.FormatVisualOriginalText(row.Primary.ChosenReplacement);
            return row.Count > 1 ? $"{vis}  [×{row.Count}]" : vis;
        }

        private static string FormatReplacementCellText(DisplayRow row)
        {
            var entry = row.Primary;
            int sugCount = entry.Issue.Suggestions?.Count ?? 0;
            if (sugCount > 1)
                return UiLocalizer.Pick(
                    $"➔  {entry.ChosenReplacement}   ({sugCount} options)",
                    $"➔  {entry.ChosenReplacement}   ({sugCount} မျိုး)");
            return $"➔  {entry.ChosenReplacement}";
        }

        private void OnGridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleRows.Count)
                return;

            var row = _visibleRows[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => FormatCheckGlyph(row.IsChecked),
                1 => FormatLocationCellText(row),
                2 => FormatOriginalCellText(row),
                3 => FormatReplacementCellText(row),
                4 => row.Primary.Issue.CertaintyBadgeText,
                _ => string.Empty
            };
        }

        private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleRows.Count || e.CellStyle == null)
                return;

            var row = _visibleRows[e.RowIndex];
            var entry = row.Primary;
            if (e.ColumnIndex == 0)
            {
                e.CellStyle.ForeColor = row.IsChecked
                    ? Color.FromArgb(16, 118, 64)
                    : Color.FromArgb(132, 144, 138);
            }
            else if (e.ColumnIndex == 3)
            {
                e.CellStyle.ForeColor = row.IsChecked
                    ? Color.FromArgb(16, 108, 56)
                    : Color.FromArgb(110, 122, 116);
            }
            else if (e.ColumnIndex == 4)
            {
                if (entry.Issue.IsContextualWarning)
                {
                    e.CellStyle.BackColor = Color.FromArgb(255, 246, 216);
                    e.CellStyle.ForeColor = Color.FromArgb(142, 84, 6);
                    e.CellStyle.SelectionBackColor = Color.FromArgb(248, 232, 184);
                    e.CellStyle.SelectionForeColor = Color.FromArgb(122, 68, 4);
                }
                else if (entry.IsHighCertaintyDefault)
                {
                    e.CellStyle.BackColor = Color.FromArgb(228, 247, 235);
                    e.CellStyle.ForeColor = Color.FromArgb(16, 104, 52);
                    e.CellStyle.SelectionBackColor = Color.FromArgb(204, 238, 218);
                    e.CellStyle.SelectionForeColor = Color.FromArgb(12, 86, 42);
                }
                else
                {
                    e.CellStyle.BackColor = Color.FromArgb(255, 242, 224);
                    e.CellStyle.ForeColor = Color.FromArgb(148, 78, 12);
                    e.CellStyle.SelectionBackColor = Color.FromArgb(248, 226, 196);
                    e.CellStyle.SelectionForeColor = Color.FromArgb(126, 62, 8);
                }
            }
        }

        private void RefreshCheckboxColumnValues()
        {
            _grid.Invalidate();
            UpdateSummaryAndButtons();
        }

        private int GetSelectedRowIndex()
        {
            if (_grid.SelectedRows.Count > 0)
            {
                int idx = _grid.SelectedRows[0].Index;
                if (idx >= 0 && idx < _visibleRows.Count)
                    return idx;
            }

            int curIdx = _grid.CurrentCell?.RowIndex ?? -1;
            return (curIdx >= 0 && curIdx < _visibleRows.Count) ? curIdx : -1;
        }

        private void ToggleRowCheck(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _visibleRows.Count)
                return;

            var row = _visibleRows[rowIndex];
            bool nextState = !row.IsChecked;
            foreach (var m in row.Members)
                m.IsChecked = nextState;

            _grid.InvalidateRow(rowIndex);
            UpdateSummaryAndButtons();
        }

        private void OnGridCellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleRows.Count)
                return;

            if (e.ColumnIndex == 0)
            {
                ToggleRowCheck(e.RowIndex);
            }
            else
            {
                _callbacks.OnNavigateToItem?.Invoke(_visibleRows[e.RowIndex].Primary.Payload);
            }
        }

        private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleRows.Count)
                return;

            if (e.ColumnIndex != 0)
            {
                ToggleRowCheck(e.RowIndex);
            }
        }

        private void OnGridKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown)
            {
                _keepGridFocusOnNextNavigate = true;
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                int rowIdx = GetSelectedRowIndex();
                if (rowIdx >= 0)
                {
                    ToggleRowCheck(rowIdx);
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                ApplySelectedRowNow();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                IgnoreSelectedRowNow();
            }
        }

        private void ApplyCustomTextToSelectedRow(bool fixImmediately)
        {
            if (_selectedRow == null)
                return;

            string customText = (_txtCustomReplacement.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(customText))
                return;

            foreach (var m in _selectedRow.Members)
            {
                m.ChosenReplacement = customText;
                m.IsChecked = true;
            }

            int rIdx = GetSelectedRowIndex();
            if (rIdx >= 0)
                _grid.InvalidateRow(rIdx);
            else
                _grid.Invalidate();

            UpdateSummaryAndButtons();
            if (fixImmediately && _callbacks.OnApplyBatch != null)
            {
                ApplySelectedRowNow();
            }
            else
            {
                UpdateSelectedEntryInspector(navigateInHost: false);
            }
        }

        private void ApplySelectedRowNow()
        {
            if (_selectedRow == null)
                return;

            string chosen = _selectedRow.Primary.ChosenReplacement;
            if (string.IsNullOrEmpty(chosen))
                return;

            if (_callbacks.OnApplyBatch == null)
                return;

            int currentRowIdx = GetSelectedRowIndex();
            var singleBatch = new Dictionary<TPayload, string>(_selectedRow.Members.Count);
            foreach (var m in _selectedRow.Members)
            {
                singleBatch[m.Payload] = string.IsNullOrEmpty(m.ChosenReplacement) ? chosen : m.ChosenReplacement;
            }

            int applied = _callbacks.OnApplyBatch.Invoke(singleBatch);
            if (applied <= 0)
                return; // E.g. Excel cell was actively in edit mode — keep row in list so user can press Esc & retry!

            foreach (var m in _selectedRow.Members)
            {
                _allEntries.Remove(m);
            }

            if (_allEntries.Count == 0)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            RefreshHeaderBannerTexts();
            _keepGridFocusOnNextNavigate = true;
            RebuildVisibleRowsAndPopulate(autoNavigate: true, preferredRowIndex: currentRowIdx);
            LayoutDialogSections();
        }

        private void IgnoreSelectedRowNow()
        {
            if (_selectedRow == null)
                return;

            int currentRowIdx = GetSelectedRowIndex();
            var payloads = _selectedRow.Members.Select(m => m.Payload).ToList();
            _callbacks.OnIgnoreItems?.Invoke(payloads);

            foreach (var m in _selectedRow.Members)
            {
                _allEntries.Remove(m);
            }

            if (_allEntries.Count == 0)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            RefreshHeaderBannerTexts();
            _keepGridFocusOnNextNavigate = true;
            RebuildVisibleRowsAndPopulate(autoNavigate: true, preferredRowIndex: currentRowIdx);
            LayoutDialogSections();
        }

        private void AddSelectedWordToCustomDictionary()
        {
            if (_selectedRow == null || _callbacks.OnAddWordToDictionary == null)
                return;

            string word = (_selectedRow.Primary.Issue.OriginalText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(word))
                return;

            int currentRowIdx = GetSelectedRowIndex();
            string normWord = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            var matchingEntries = _allEntries
                .Where(e => UnicodeNormalizer.NormalizeCanonicalOrder((e.Issue.OriginalText ?? string.Empty).Trim()) == normWord)
                .ToList();

            var payloads = matchingEntries.Select(m => m.Payload).ToList();
            _callbacks.OnAddWordToDictionary.Invoke(word, payloads);

            foreach (var m in matchingEntries)
            {
                _allEntries.Remove(m);
            }

            if (_allEntries.Count == 0)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            RefreshHeaderBannerTexts();
            RebuildVisibleRowsAndPopulate(autoNavigate: true, preferredRowIndex: currentRowIdx);
            LayoutDialogSections();
        }

        private void UpdateSelectedEntryInspector(bool navigateInHost)
        {
            _choicesPanel.SuspendLayout();
            try
            {
                DisposeAndClearChoicesPanel();
                int selectedRowIdx = GetSelectedRowIndex();
                if (selectedRowIdx < 0)
                {
                    _selectedRow = null;
                    _btnFixNow.Enabled = false;
                    _btnIgnoreNow.Enabled = false;
                    _btnAddDict.Enabled = false;
                    _btnJump.Enabled = false;
                    _lblContextPreview.Text = UiLocalizer.Pick("No matching items remaining.", "ပြင်ဆင်ရန် အချက်အလက် မကျန်တော့ပါ။");
                    _lblExplanation.Text = string.Empty;
                    return;
                }

                DisplayRow row = _visibleRows[selectedRowIdx];
                FixPreviewEntry<TPayload> entry = row.Primary;
                _selectedRow = row;

                _btnFixNow.Enabled = !string.IsNullOrEmpty(entry.ChosenReplacement);
                _btnIgnoreNow.Enabled = true;
                _btnAddDict.Enabled = !entry.Issue.IsGrammarOrStyle && (entry.Issue.OriginalText ?? string.Empty).IndexOf(' ') < 0;
                _btnJump.Enabled = _callbacks.OnNavigateToItem != null;
                _txtCustomReplacement.Text = entry.ChosenReplacement;

                if (navigateInHost && _callbacks.OnNavigateToItem != null)
                {
                    bool restoreFocus = _keepGridFocusOnNextNavigate && _grid.Focused;
                    _keepGridFocusOnNextNavigate = false;
                    _callbacks.OnNavigateToItem.Invoke(entry.Payload);
                    if (restoreFocus && IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed && Visible && WindowState != FormWindowState.Minimized)
                            {
                                try { Activate(); _grid.Focus(); } catch { }
                            }
                        }));
                    }
                }
                else
                {
                    _keepGridFocusOnNextNavigate = false;
                }

                string cleanContext = (entry.ContextSnippet ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                if (cleanContext.Length > 150)
                    cleanContext = MyanmarSyllableBreaker.TruncateAtSyllableBoundary(cleanContext, 150, "…");

                string grpNote = row.Count > 1
                    ? UiLocalizer.Pick($" ({row.Count} cells)", $" ({row.Count} ကွက်)")
                    : string.Empty;

                _lblContextPreview.Text = UiLocalizer.Pick(
                    $"[{entry.LocationLabel}{grpNote}] Cell — {cleanContext}",
                    $"[{entry.LocationLabel}{grpNote}] အကွက်စာသား — {cleanContext}");
                _lblExplanation.Text = entry.Issue.Message ?? string.Empty;

                var sugs = entry.Issue.Suggestions;
                if (sugs != null && sugs.Count > 0)
                {
                    int maxChoices = Math.Min(sugs.Count, 5);
                    var lblPick = new Label
                    {
                        Text = maxChoices > 1
                            ? UiLocalizer.Pick("Choose —", "ရွေးချယ်ရန် —")
                            : UiLocalizer.Pick("Fix —", "အကြံပြုချက် —"),
                        Font = _fontBadge,
                        ForeColor = Color.FromArgb(24, 68, 42),
                        AutoSize = true,
                        UseMnemonic = false,
                        Margin = new Padding(0, S(5), S(8), S(2))
                    };
                    _choicesPanel.Controls.Add(lblPick);

                    int maxChoiceW = Math.Max(S(220), ClientSize.Width - S(60));
                    var newControls = new Control[maxChoices];
                    for (int i = 0; i < maxChoices; i++)
                    {
                        SuggestionCandidate cand = sugs[i];
                        bool isActiveChoice = string.Equals(entry.ChosenReplacement, cand.Replacement, StringComparison.Ordinal);
                        string cueShort = string.IsNullOrWhiteSpace(cand.MeaningCue)
                            ? string.Empty
                            : $" [{TruncateCue(cand.MeaningCue, 34)}]";

                        string btnLabel = $"{(isActiveChoice ? "✓ " : string.Empty)}{cand.Replacement}{cueShort}";
                        Font optFont = isActiveChoice ? _fontBodyBold : _fontSubtitle;
                        Size singleMeasure = TextRenderer.MeasureText(
                            btnLabel,
                            optFont,
                            new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                        Button btnOpt;
                        if (singleMeasure.Width + S(24) <= maxChoiceW)
                        {
                            btnOpt = new Button
                            {
                                Text = btnLabel,
                                Font = optFont,
                                AutoSize = false,
                                Size = new Size(
                                    singleMeasure.Width + S(20),
                                    UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, optFont, 8)),
                                Margin = new Padding(0, S(2), S(6), S(2)),
                                FlatStyle = FlatStyle.Flat,
                                BackColor = isActiveChoice ? Color.FromArgb(218, 245, 228) : Color.FromArgb(246, 250, 248),
                                ForeColor = isActiveChoice ? Color.FromArgb(14, 96, 48) : Color.FromArgb(36, 56, 44),
                                Cursor = Cursors.Hand,
                                UseMnemonic = false
                            };
                        }
                        else
                        {
                            btnOpt = new WordWrapChoiceButton
                            {
                                Text = btnLabel,
                                Font = optFont,
                                MaximumSize = new Size(maxChoiceW, 0),
                                Padding = new Padding(S(8), S(3), S(8), S(3)),
                                Margin = new Padding(0, S(2), S(6), S(2)),
                                FlatStyle = FlatStyle.Flat,
                                TextAlign = ContentAlignment.MiddleLeft,
                                BackColor = isActiveChoice ? Color.FromArgb(218, 245, 228) : Color.FromArgb(246, 250, 248),
                                ForeColor = isActiveChoice ? Color.FromArgb(14, 96, 48) : Color.FromArgb(36, 56, 44),
                                Cursor = Cursors.Hand
                            };
                        }
                        btnOpt.FlatAppearance.BorderColor = isActiveChoice
                            ? Color.FromArgb(92, 184, 128)
                            : Color.FromArgb(196, 216, 204);

                        string pickedWord = cand.Replacement;
                        btnOpt.Click += (s, e) =>
                        {
                            foreach (var m in row.Members)
                            {
                                m.ChosenReplacement = pickedWord;
                                m.IsChecked = true;
                            }
                            _txtCustomReplacement.Text = pickedWord;
                            int rIdx = GetSelectedRowIndex();
                            if (rIdx >= 0)
                                _grid.InvalidateRow(rIdx);
                            else
                                _grid.Invalidate();
                            UpdateSummaryAndButtons();
                            UpdateSelectedEntryInspector(navigateInHost: false);
                        };

                        newControls[i] = btnOpt;
                    }
                    _choicesPanel.Controls.AddRange(newControls);
                }
            }
            finally
            {
                _choicesPanel.ResumeLayout(true);
            }

            LayoutDialogSections();
        }

        private void UpdateSummaryAndButtons()
        {
            int total = _allEntries.Count;
            int checkedCount = _allEntries.Count(x => x.IsChecked && !string.IsNullOrEmpty(x.ChosenReplacement));
            int skippedCount = total - checkedCount;

            _lblFooterSummary.Text = UiLocalizer.Pick(
                $"Total: {total}  •  Selected: {checkedCount}  •  Skipped: {skippedCount}",
                $"စုစုပေါင်း ({total}) ခု  •  ပြင်ဆင်မည် ({checkedCount}) ခု  •  ချန်လှပ် ({skippedCount}) ခု");
            _btnConfirm.Text = checkedCount > 0
                ? UiLocalizer.Pick($"✓ Apply {checkedCount} Selected Fix(es)", $"✓ ရွေးချယ်ထားသော ({checkedCount}) ခုကို ပြင်ဆင်မည်")
                : UiLocalizer.Pick("No Fixes Selected", "ရွေးချယ်ထားသော အချက်မရှိပါ");
            _btnConfirm.Enabled = checkedCount > 0;
            _btnConfirm.BackColor = checkedCount > 0
                ? Color.FromArgb(16, 108, 56)
                : Color.FromArgb(172, 184, 178);

            if (_isCompactMiniBar)
            {
                RefreshHeaderBannerTexts();
            }

            if (_footerPanel != null && _footerPanel.Width > 40)
            {
                Size cancelSz = MeasureSingleLineButtonSize(_btnCancel.Text, _fontBody, 24, 10);
                Size confirmSz = MeasureSingleLineButtonSize(_btnConfirm.Text, _fontBodyBold, 30, 10);
                int footerH = _footerPanel.Height;
                int innerW = _footerPanel.Width;
                _btnConfirm.SetBounds(innerW - confirmSz.Width, 0, confirmSz.Width, footerH);
                _btnCancel.SetBounds(_btnConfirm.Left - S(8) - cancelSz.Width, 0, cancelSz.Width, footerH);
                _lblFooterSummary.SetBounds(S(2), 0, Math.Max(S(80), _btnCancel.Left - S(12)), footerH);
            }
        }

        private void OnConfirmClicked(object? sender, EventArgs e)
        {
            ApprovedReplacements.Clear();
            var appliedEntries = new List<FixPreviewEntry<TPayload>>();
            foreach (var entry in _allEntries)
            {
                if (entry.IsChecked && !string.IsNullOrEmpty(entry.ChosenReplacement))
                {
                    ApprovedReplacements[entry.Payload] = entry.ChosenReplacement;
                    appliedEntries.Add(entry);
                }
            }

            if (ApprovedReplacements.Count == 0)
                return;

            if (_callbacks.OnApplyBatch != null)
            {
                int applied = _callbacks.OnApplyBatch.Invoke(ApprovedReplacements);
                if (applied <= 0)
                    return;

                foreach (var done in appliedEntries)
                {
                    _allEntries.Remove(done);
                }

                if (_allEntries.Count == 0)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                RefreshHeaderBannerTexts();
                RebuildVisibleRowsAndPopulate(autoNavigate: false);
                LayoutDialogSections();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string TruncateCue(string text, int maxChars)
        {
            string clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
            int limit = Math.Max(maxChars, 38);
            return clean.Length <= limit
                ? clean
                : MyanmarSyllableBreaker.TruncateAtSyllableBoundary(clean, limit, "…");
        }

        private static void EnableDoubleBuffering(Control ctrl)
        {
            try
            {
                PropertyInfo? prop = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
                prop?.SetValue(ctrl, true, null);
            }
            catch { }
        }
    }
}
