using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    internal sealed class FixPreviewEntry<TPayload> where TPayload : class
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

    internal static class FixPreviewNativeMethods
    {
        public const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>
    /// High-DPI Safe Fix-All Preview Checklist Dialog for Microsoft Excel (100%–250% DPI deterministic layout).
    /// Pre-checks (☑) 100% deterministic orthography/lookalike/grammar rules while leaving unknown names,
    /// fuzzy guesses, and contextual meaning warnings unchecked (☐) by default so user data is never blindly ruined.
    /// Uses single-pass O(1) deterministic section bounds so the checklist DataGridView never collapses or overlaps at high DPI.
    /// </summary>
    internal sealed class FixAllPreviewDialog<TPayload> : Form where TPayload : class
    {
        private readonly List<FixPreviewEntry<TPayload>> _allEntries;
        private List<FixPreviewEntry<TPayload>> _visibleEntries;
        private readonly Action<TPayload>? _onNavigateToItem;

        private int _dpi;
        private bool _inSectionLayout;

        private readonly Font _fontTitle;
        private readonly Font _fontSubtitle;
        private readonly Font _fontBody;
        private readonly Font _fontBodyBold;
        private readonly Font _fontBadge;
        private readonly Font _fontMono;

        private Panel _headerCard = null!;
        private Label _lblTitle = null!;
        private Label _lblGuide = null!;

        private Panel _toolBarPanel = null!;
        private Button _btnSafeOnly = null!;
        private Button _btnCheckAll = null!;
        private Button _btnUncheckAll = null!;
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
        private Label _lblExplanation = null!;
        private FlowLayoutPanel _choicesPanel = null!;
        private Button _btnJump = null!;

        private Panel _footerPanel = null!;
        private Label _lblFooterSummary = null!;
        private Button _btnCancel = null!;
        private Button _btnConfirm = null!;

        private FixPreviewEntry<TPayload>? _selectedEntry;
        private readonly Timer _searchDebounceTimer;
        private bool _suppressSelectionChanged;

        public Dictionary<TPayload, string> ApprovedReplacements { get; } = new Dictionary<TPayload, string>();

        public FixAllPreviewDialog(
            IReadOnlyList<FixPreviewEntry<TPayload>> entries,
            string scopeDisplayTitle,
            Action<TPayload>? onNavigateToItem = null)
        {
            _allEntries = entries?.ToList() ?? new List<FixPreviewEntry<TPayload>>();
            _visibleEntries = new List<FixPreviewEntry<TPayload>>(_allEntries);
            _onNavigateToItem = onNavigateToItem;
            _dpi = UiFontCache.GetEffectiveDpi(this);

            _fontTitle = UiFontCache.CreateOwnedMyanmar(11.2f, FontStyle.Bold);
            _fontSubtitle = UiFontCache.CreateOwnedMyanmar(9.0f, FontStyle.Regular);
            _fontBody = UiFontCache.CreateOwnedMyanmar(9.5f, FontStyle.Regular);
            _fontBodyBold = UiFontCache.CreateOwnedMyanmar(9.5f, FontStyle.Bold);
            _fontBadge = UiFontCache.CreateOwnedMyanmar(8.75f, FontStyle.Bold);
            _fontMono = UiFontCache.GetMonospace(9.0f, FontStyle.Bold);

            _searchDebounceTimer = new Timer { Interval = 150 };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ApplySearchFilter();
            };

            InitializeDialogUi(scopeDisplayTitle);
            PopulateGridRows();
            UpdateSummaryAndButtons();
            LayoutDialogSections();
        }

        private int S(int logicalPx) => UiFontCache.ScaleLogicalPxForDpi(_dpi, logicalPx);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeAndClearChoicesPanel();
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

            var snapshot = new Control[_choicesPanel.Controls.Count];
            _choicesPanel.Controls.CopyTo(snapshot, 0);
            _choicesPanel.Controls.Clear();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i]?.Dispose();
            }
        }

        private void InitializeDialogUi(string scopeDisplayTitle)
        {
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Text = UiLocalizer.Pick(
                $"Safe Fix-All Review Checklist — {scopeDisplayTitle}",
                $"အားလုံးမပြင်မီ စိစစ်အတည်ပြုရန် — {scopeDisplayTitle}");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowIcon = false;
            KeyPreview = true;
            BackColor = Color.FromArgb(245, 248, 252);

            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int minW = Math.Min(S(720), Math.Max(S(480), (int)(workArea.Width * 0.72)));
            int minH = Math.Min(S(500), Math.Max(S(380), (int)(workArea.Height * 0.70)));
            int targetW = Math.Max(minW, Math.Min(S(980), (int)(workArea.Width * 0.86)));
            int targetH = Math.Max(minH, Math.Min(S(680), (int)(workArea.Height * 0.85)));

            MinimumSize = new Size(minW, minH);
            ClientSize = new Size(targetW, targetH);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            int totalCount = _allEntries.Count;
            int safeCount = _allEntries.Count(x => x.IsHighCertaintyDefault);
            int reviewCount = totalCount - safeCount;

            // 0. Header Banner Card
            _headerCard = new Panel
            {
                BackColor = Color.White
            };
            EnableDoubleBuffering(_headerCard);
            _headerCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(212, 224, 238)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _headerCard.Width - 1, _headerCard.Height - 1);
                }
                using (var accent = new SolidBrush(Color.FromArgb(20, 122, 75)))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, Math.Max(4, S(5)), _headerCard.Height);
                }
            };

            _lblTitle = new Label
            {
                Text = UiLocalizer.Pick(
                    $"Review & Approve Replacements Before Applying ({scopeDisplayTitle})",
                    $"အားလုံးမပြင်မီ စာရင်းဇယားဖြင့် စိစစ်အတည်ပြုရန် ({scopeDisplayTitle})"),
                Font = _fontTitle,
                ForeColor = Color.FromArgb(16, 42, 72),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _lblGuide = new Label
            {
                Text = UiLocalizer.Pick(
                    $"✓ High-certainty standard rules ({safeCount}) are pre-checked (☑), while proper names, guesses, and contextual meaning warnings ({reviewCount}) are left unchecked (☐) to protect already-verified cells.",
                    $"✓ သေချာသော စံသတ်ပုံ/သဒ္ဒါ ({safeCount}) ခုကို အမှန်ခြစ်ပေးထားပြီး (☑)၊ လူအမည်/ဒေသအမည် သို့မဟုတ် စစ်ပြီးသားဖြစ်နိုင်သော ခန့်မှန်းချက်/အနက်သံသယ ({reviewCount}) ခုကို မှားမပြင်မိစေရန် အမှန်ခြစ်ဖြုတ်ထားပါသည် (☐)။"),
                Font = _fontSubtitle,
                ForeColor = Color.FromArgb(52, 72, 96),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _headerCard.Controls.Add(_lblTitle);
            _headerCard.Controls.Add(_lblGuide);

            // 1. Selection & Search Toolbar
            _toolBarPanel = new Panel
            {
                BackColor = Color.Transparent
            };

            _btnSafeOnly = CreateToolbarButton(
                UiLocalizer.Pick($"☑ Sure Only ({safeCount})", $"☑ သေချာသည်များသာ ({safeCount})"),
                Color.FromArgb(224, 245, 233), Color.FromArgb(16, 102, 54), Color.FromArgb(148, 208, 172));
            _btnSafeOnly.Click += (s, e) =>
            {
                foreach (var item in _allEntries)
                    item.IsChecked = item.IsHighCertaintyDefault;
                RefreshCheckboxColumnValues();
            };

            _btnCheckAll = CreateToolbarButton(
                UiLocalizer.Pick($"☑ Select All ({totalCount})", $"☑ အားလုံးရွေး ({totalCount})"),
                Color.White, Color.FromArgb(28, 56, 92), Color.FromArgb(196, 210, 228));
            _btnCheckAll.Click += (s, e) =>
            {
                foreach (var item in _visibleEntries)
                    item.IsChecked = true;
                RefreshCheckboxColumnValues();
            };

            _btnUncheckAll = CreateToolbarButton(
                UiLocalizer.Pick("☐ Clear All", "☐ အားလုံးဖြုတ်"),
                Color.White, Color.FromArgb(92, 104, 120), Color.FromArgb(204, 214, 226));
            _btnUncheckAll.Click += (s, e) =>
            {
                foreach (var item in _visibleEntries)
                    item.IsChecked = false;
                RefreshCheckboxColumnValues();
            };

            _lblSearchHint = new Label
            {
                Text = UiLocalizer.Pick("Filter:", "ရှာဖွေရန် —"),
                Font = _fontBadge,
                ForeColor = Color.FromArgb(72, 90, 112),
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
                    UiLocalizer.Pick("Search word or cell...", "စာလုံး သို့မဟုတ် အကွက် ရှာရန်..."));
            _txtSearch.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };

            _toolBarPanel.Controls.Add(_btnSafeOnly);
            _toolBarPanel.Controls.Add(_btnCheckAll);
            _toolBarPanel.Controls.Add(_btnUncheckAll);
            _toolBarPanel.Controls.Add(_lblSearchHint);
            _toolBarPanel.Controls.Add(_txtSearch);

            // 2. Virtualized Checklist DataGridView (O(1) instant open & zero GDI bloat even on 10,000+ issues)
            int safeRowHeight = Math.Max(S(38), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBody, 14));
            int safeHeaderHeight = Math.Max(S(36), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBadge, 12));

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
                GridColor = Color.FromArgb(228, 234, 242),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = safeHeaderHeight,
                Font = _fontBody
            };
            _grid.RowTemplate.Height = safeRowHeight;
            EnableDoubleBuffering(_grid);

            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(236, 242, 250),
                ForeColor = Color.FromArgb(20, 44, 76),
                Font = _fontBadge,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(S(6), S(4), S(6), S(4)),
                WrapMode = DataGridViewTriState.False
            };

            _grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(24, 36, 52),
                SelectionBackColor = Color.FromArgb(222, 238, 255),
                SelectionForeColor = Color.FromArgb(12, 32, 62),
                Font = _fontBody,
                Padding = new Padding(S(6), S(2), S(6), S(2))
            };

            _colCheck = new DataGridViewTextBoxColumn
            {
                Name = "ColCheck",
                HeaderText = UiLocalizer.Pick("Apply", "ပြင်မည်"),
                Width = S(72),
                MinimumWidth = S(58),
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
                HeaderText = UiLocalizer.Pick("Cell", "နေရာ (Cell)"),
                Width = S(132),
                MinimumWidth = S(96),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontMono,
                    ForeColor = Color.FromArgb(32, 82, 136)
                }
            };

            _colOrig = new DataGridViewTextBoxColumn
            {
                Name = "ColOriginal",
                HeaderText = UiLocalizer.Pick("Original Text", "မူလစာသား (အမှား/သံသယ)"),
                MinimumWidth = S(110),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 36f
            };

            _colRep = new DataGridViewTextBoxColumn
            {
                Name = "ColReplacement",
                HeaderText = UiLocalizer.Pick("Replacement", "ပြင်ဆင်မည့်စာသား"),
                MinimumWidth = S(130),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 44f,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontBodyBold,
                    ForeColor = Color.FromArgb(16, 108, 56)
                }
            };

            _colCert = new DataGridViewTextBoxColumn
            {
                Name = "ColCertainty",
                HeaderText = UiLocalizer.Pick("Certainty Level", "စိတ်ချရမှု အဆင့်"),
                Width = S(224),
                MinimumWidth = S(148),
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
                    UpdateSelectedEntryInspector();
            };

            // 3. Selected Row Context & Alternative Choices Inspector Card
            _detailCard = new Panel
            {
                BackColor = Color.White,
                AutoScroll = true
            };
            EnableDoubleBuffering(_detailCard);
            _detailCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(214, 224, 238)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _detailCard.Width - 1, _detailCard.Height - 1);
                }
            };

            _lblContextPreview = new Label
            {
                Text = UiLocalizer.Pick(
                    "Select a row above to inspect surrounding cell text and alternative replacement choices.",
                    "စာရင်းမှ အတန်းတစ်တန်းကို နှိပ်၍ မူရင်းအကွက်စာသားနှင့် အခြားရွေးချယ်စရာများကို ကြည့်ရှုနိုင်ပါသည်။"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(24, 48, 78),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _btnJump = CreateToolbarButton(
                UiLocalizer.Pick("▸ Jump to Cell", "▸ အကွက်သို့ ကြည့်မည်"),
                Color.FromArgb(238, 245, 255), Color.FromArgb(24, 82, 152), Color.FromArgb(172, 202, 240));
            _btnJump.Visible = _onNavigateToItem != null;
            _btnJump.Enabled = false;
            _btnJump.Click += (s, e) =>
            {
                if (_selectedEntry != null)
                {
                    _onNavigateToItem?.Invoke(_selectedEntry.Payload);
                }
            };

            _lblExplanation = new Label
            {
                Text = string.Empty,
                Font = _fontSubtitle,
                ForeColor = Color.FromArgb(72, 88, 108),
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

            _detailCard.Controls.Add(_lblContextPreview);
            _detailCard.Controls.Add(_btnJump);
            _detailCard.Controls.Add(_lblExplanation);
            _detailCard.Controls.Add(_choicesPanel);

            // 4. Footer Bar
            _footerPanel = new Panel
            {
                BackColor = Color.Transparent
            };

            _lblFooterSummary = new Label
            {
                Text = string.Empty,
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(28, 52, 82),
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            _btnCancel = new Button
            {
                Text = UiLocalizer.Pick("Cancel (Esc)", "မပြင်တော့ပါ (Esc)"),
                Font = _fontBody,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(62, 76, 94),
                Cursor = Cursors.Hand
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(192, 204, 218);
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
                BackColor = Color.FromArgb(20, 122, 75),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnConfirm.FlatAppearance.BorderColor = Color.FromArgb(14, 98, 58);
            _btnConfirm.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 104, 64);
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

            UiFontCache.DisableMnemonicsRecursive(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshDpiIfNeededAndLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_headerCard != null)
            {
                LayoutDialogSections();
            }
        }

        private void RefreshDpiIfNeededAndLayout()
        {
            int liveDpi = UiFontCache.GetEffectiveDpi(this);
            if (liveDpi != _dpi && liveDpi > 0)
            {
                _dpi = liveDpi;
                int safeRowHeight = Math.Max(S(38), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBody, 14));
                int safeHeaderHeight = Math.Max(S(36), UiFontCache.MeasureSingleLineButtonHeightForDpi(_dpi, _fontBadge, 12));
                _grid.ColumnHeadersHeight = safeHeaderHeight;
                _grid.RowTemplate.Height = safeRowHeight;
                _colCheck.Width = S(72);
                _colLoc.Width = S(132);
                _colCert.Width = S(224);
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
            if (_inSectionLayout || _headerCard == null || ClientSize.Width <= 40 || ClientSize.Height <= 40)
                return;

            _inSectionLayout = true;
            SuspendLayout();
            try
            {
                int cw = ClientSize.Width;
                int ch = ClientSize.Height;
                int padX = S(14);
                int padY = S(12);
                int gap = S(8);
                int innerW = Math.Max(S(260), cw - padX * 2);

                // 0. Header Banner Card
                int headerPadL = S(16);
                int headerPadR = S(14);
                int headerPadV = S(9);
                int headerTextW = Math.Max(S(200), innerW - headerPadL - headerPadR);

                _lblTitle.MaximumSize = new Size(headerTextW, 0);
                _lblTitle.Location = new Point(headerPadL, headerPadV);
                Size titlePref = _lblTitle.GetPreferredSize(new Size(headerTextW, 0));
                _lblTitle.Size = titlePref;

                _lblGuide.MaximumSize = new Size(headerTextW, 0);
                _lblGuide.Location = new Point(headerPadL, _lblTitle.Bottom + S(3));
                Size guidePref = _lblGuide.GetPreferredSize(new Size(headerTextW, 0));
                _lblGuide.Size = guidePref;

                int headerH = Math.Min((int)(ch * 0.24), _lblGuide.Bottom + headerPadV);
                _headerCard.SetBounds(padX, padY, innerW, Math.Max(S(48), headerH));

                // 1. Toolbar Row
                Size safeSz = MeasureSingleLineButtonSize(_btnSafeOnly.Text, _fontBadge, 22, 9);
                Size allSz = MeasureSingleLineButtonSize(_btnCheckAll.Text, _fontBadge, 22, 9);
                Size clrSz = MeasureSingleLineButtonSize(_btnUncheckAll.Text, _fontBadge, 22, 9);
                int toolBtnH = Math.Max(safeSz.Height, Math.Max(allSz.Height, clrSz.Height));
                int btnGap = S(6);

                _btnSafeOnly.SetBounds(0, 0, safeSz.Width, toolBtnH);
                _btnCheckAll.SetBounds(_btnSafeOnly.Right + btnGap, 0, allSz.Width, toolBtnH);
                _btnUncheckAll.SetBounds(_btnCheckAll.Right + btnGap, 0, clrSz.Width, toolBtnH);

                Size hintTextSz = TextRenderer.MeasureText(
                    _lblSearchHint.Text,
                    _fontBadge,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                int hintW = hintTextSz.Width + S(8);
                int buttonsRight = _btnUncheckAll.Right;
                int availRight = innerW - buttonsRight - S(12);

                int toolH;
                if (availRight >= hintW + S(140))
                {
                    int searchW = Math.Min(S(240), availRight - hintW - S(6));
                    int txtH = _txtSearch.PreferredHeight;
                    int txtY = Math.Max(0, (toolBtnH - txtH) / 2);
                    _txtSearch.SetBounds(innerW - searchW, txtY, searchW, txtH);
                    _lblSearchHint.SetBounds(_txtSearch.Left - hintW - S(4), 0, hintW, toolBtnH);
                    toolH = Math.Max(toolBtnH, txtH);
                }
                else
                {
                    int row2Y = toolBtnH + S(6);
                    int txtH = _txtSearch.PreferredHeight;
                    int row2H = Math.Max(txtH, S(26));
                    _lblSearchHint.SetBounds(0, row2Y, hintW, row2H);
                    int searchX = hintW + S(6);
                    _txtSearch.SetBounds(searchX, row2Y + Math.Max(0, (row2H - txtH) / 2), Math.Max(S(120), innerW - searchX), txtH);
                    toolH = row2Y + row2H;
                }
                _toolBarPanel.SetBounds(padX, _headerCard.Bottom + gap, innerW, toolH);

                // 4. Footer Bar (anchored at bottom)
                Size cancelSz = MeasureSingleLineButtonSize(_btnCancel.Text, _fontBody, 28, 12);
                Size confirmSz = MeasureSingleLineButtonSize(_btnConfirm.Text, _fontBodyBold, 34, 12);
                int footerH = Math.Max(cancelSz.Height, confirmSz.Height);

                _btnConfirm.SetBounds(innerW - confirmSz.Width, 0, confirmSz.Width, footerH);
                _btnCancel.SetBounds(_btnConfirm.Left - S(8) - cancelSz.Width, 0, cancelSz.Width, footerH);
                int summaryAvailW = Math.Max(S(80), _btnCancel.Left - S(12));
                _lblFooterSummary.SetBounds(S(2), 0, summaryAvailW, footerH);

                int footerTop = ch - padY - footerH;
                _footerPanel.SetBounds(padX, footerTop, innerW, footerH);

                // Middle Workspace = [_toolBarPanel.Bottom + gap .. footerTop - gap]
                int middleTop = _toolBarPanel.Bottom + gap;
                int middleBottom = footerTop - gap;
                int middleAvailH = Math.Max(S(140), middleBottom - middleTop);

                // 3. Detail Inspector Card (measured & capped at <= 42% of middle workspace so _grid stays prominent)
                int desiredDetailH = LayoutDetailCardChildren(innerW, reserveScrollbar: false);
                int maxDetailH = Math.Max(S(88), (int)(middleAvailH * 0.42));
                int finalDetailH;
                if (desiredDetailH > maxDetailH)
                {
                    LayoutDetailCardChildren(innerW, reserveScrollbar: true);
                    finalDetailH = maxDetailH;
                }
                else
                {
                    finalDetailH = Math.Max(S(78), desiredDetailH);
                }

                int detailTop = middleBottom - finalDetailH;
                _detailCard.SetBounds(padX, detailTop, innerW, finalDetailH);

                // 2. Checklist DataGridView (receives the entire remaining upper middle area >= 58%)
                int gridH = Math.Max(S(100), (detailTop - gap) - middleTop);
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
            int padH = S(12);
            int padV = S(8);
            int scrollW = reserveScrollbar ? SystemInformation.VerticalScrollBarWidth + S(4) : 0;
            int contentW = Math.Max(S(180), cardWidth - padH * 2 - scrollW);

            int topRowBottom = padV;
            int contextMaxW = contentW;

            if (_btnJump.Visible)
            {
                Size jumpSz = MeasureSingleLineButtonSize(_btnJump.Text, _fontBadge, 20, 8);
                int jumpX = padH + contentW - jumpSz.Width;
                _btnJump.SetBounds(jumpX, padV, jumpSz.Width, jumpSz.Height);
                contextMaxW = Math.Max(S(140), contentW - jumpSz.Width - S(10));
                topRowBottom = Math.Max(topRowBottom, _btnJump.Bottom);
            }

            _lblContextPreview.MaximumSize = new Size(contextMaxW, 0);
            _lblContextPreview.Location = new Point(padH, padV);
            Size ctxPref = _lblContextPreview.GetPreferredSize(new Size(contextMaxW, 0));
            _lblContextPreview.Size = ctxPref;
            topRowBottom = Math.Max(topRowBottom, _lblContextPreview.Bottom);

            int curY = topRowBottom + S(4);
            if (!string.IsNullOrEmpty(_lblExplanation.Text))
            {
                _lblExplanation.Visible = true;
                _lblExplanation.MaximumSize = new Size(contentW, 0);
                _lblExplanation.Location = new Point(padH, curY);
                Size expPref = _lblExplanation.GetPreferredSize(new Size(contentW, 0));
                _lblExplanation.Size = expPref;
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
                curY = _choicesPanel.Bottom;
            }
            else
            {
                _choicesPanel.Visible = false;
            }

            return curY + padV + S(2);
        }

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

        private void ApplySearchFilter()
        {
            string q = (_txtSearch.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q))
            {
                _visibleEntries = new List<FixPreviewEntry<TPayload>>(_allEntries);
            }
            else
            {
                _visibleEntries = _allEntries
                    .Where(x =>
                        x.LocationLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.Issue.OriginalText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.ChosenReplacement.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.ContextSnippet.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            PopulateGridRows();
            UpdateSummaryAndButtons();
        }

        private void PopulateGridRows()
        {
            _suppressSelectionChanged = true;
            _grid.SuspendLayout();
            try
            {
                _grid.CurrentCell = null;
                _grid.RowCount = 0;
                _grid.RowCount = _visibleEntries.Count;

                if (_visibleEntries.Count > 0)
                {
                    _grid.Rows[0].Selected = true;
                    try { _grid.CurrentCell = _grid.Rows[0].Cells[0]; } catch { }
                }
            }
            finally
            {
                _grid.ResumeLayout();
                _suppressSelectionChanged = false;
            }

            _grid.Invalidate();
            UpdateSelectedEntryInspector();
        }

        private static string FormatCheckGlyph(bool isChecked) => isChecked ? "☑" : "☐";

        private static string FormatReplacementCellText(FixPreviewEntry<TPayload> entry)
        {
            int sugCount = entry.Issue.Suggestions?.Count ?? 0;
            if (sugCount > 1)
            {
                return UiLocalizer.Pick(
                    $"➔  {entry.ChosenReplacement}   ({sugCount} options)",
                    $"➔  {entry.ChosenReplacement}   ({sugCount} မျိုး)");
            }
            return $"➔  {entry.ChosenReplacement}";
        }

        private void OnGridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleEntries.Count)
                return;

            var entry = _visibleEntries[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => FormatCheckGlyph(entry.IsChecked),
                1 => entry.LocationLabel,
                2 => entry.Issue.OriginalText,
                3 => FormatReplacementCellText(entry),
                4 => entry.Issue.CertaintyBadgeText,
                _ => string.Empty
            };
        }

        private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleEntries.Count || e.CellStyle == null)
                return;

            var entry = _visibleEntries[e.RowIndex];
            if (e.ColumnIndex == 0)
            {
                e.CellStyle.ForeColor = entry.IsChecked
                    ? Color.FromArgb(16, 118, 64)
                    : Color.FromArgb(132, 144, 160);
            }
            else if (e.ColumnIndex == 3)
            {
                e.CellStyle.ForeColor = entry.IsChecked
                    ? Color.FromArgb(16, 108, 56)
                    : Color.FromArgb(110, 122, 138);
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
                if (idx >= 0 && idx < _visibleEntries.Count)
                    return idx;
            }

            int curIdx = _grid.CurrentCell?.RowIndex ?? -1;
            return (curIdx >= 0 && curIdx < _visibleEntries.Count) ? curIdx : -1;
        }

        private void OnGridCellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleEntries.Count)
                return;

            if (e.ColumnIndex == 0)
            {
                var entry = _visibleEntries[e.RowIndex];
                entry.IsChecked = !entry.IsChecked;
                _grid.InvalidateRow(e.RowIndex);
                UpdateSummaryAndButtons();
            }
        }

        private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _visibleEntries.Count)
                return;

            if (e.ColumnIndex != 0)
            {
                var entry = _visibleEntries[e.RowIndex];
                entry.IsChecked = !entry.IsChecked;
                _grid.InvalidateRow(e.RowIndex);
                UpdateSummaryAndButtons();
            }
        }

        private void OnGridKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                int rowIdx = GetSelectedRowIndex();
                if (rowIdx >= 0)
                {
                    var entry = _visibleEntries[rowIdx];
                    entry.IsChecked = !entry.IsChecked;
                    _grid.InvalidateRow(rowIdx);
                    UpdateSummaryAndButtons();
                    e.Handled = true;
                }
            }
        }

        private void UpdateSelectedEntryInspector()
        {
            _choicesPanel.SuspendLayout();
            try
            {
                DisposeAndClearChoicesPanel();
                int selectedRowIdx = GetSelectedRowIndex();
                if (selectedRowIdx < 0)
                {
                    _selectedEntry = null;
                    _btnJump.Enabled = false;
                    _lblContextPreview.Text = UiLocalizer.Pick(
                        "No matching items to display.",
                        "ပြင်ဆင်မည့် အချက်အလက် မရှိပါ။");
                    _lblExplanation.Text = string.Empty;
                    return;
                }

                FixPreviewEntry<TPayload> entry = _visibleEntries[selectedRowIdx];
                _selectedEntry = entry;
                _btnJump.Enabled = _onNavigateToItem != null;

                string cleanContext = (entry.ContextSnippet ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                if (cleanContext.Length > 150)
                    cleanContext = MyanmarSpellChecker.Core.Nlp.MyanmarSyllableBreaker.TruncateAtSyllableBoundary(cleanContext, 150, "…");

                _lblContextPreview.Text = UiLocalizer.Pick(
                    $"[{entry.LocationLabel}] Cell Context — {cleanContext}",
                    $"[{entry.LocationLabel}] မူရင်းစာသား — {cleanContext}");
                _lblExplanation.Text = entry.Issue.Message ?? string.Empty;

                var sugs = entry.Issue.Suggestions;
                if (sugs != null && sugs.Count > 0)
                {
                    int maxChoices = Math.Min(sugs.Count, 5);
                    var lblPick = new Label
                    {
                        Text = maxChoices > 1
                            ? UiLocalizer.Pick("Choose Replacement —", "ရွေးချယ်ပြင်ဆင်ရန် —")
                            : UiLocalizer.Pick("Suggested —", "အကြံပြုချက် —"),
                        Font = _fontBadge,
                        ForeColor = Color.FromArgb(44, 68, 96),
                        AutoSize = true,
                        UseMnemonic = false,
                        Margin = new Padding(0, S(5), S(8), S(2))
                    };
                    _choicesPanel.Controls.Add(lblPick);

                    int maxChoiceW = Math.Max(S(240), ClientSize.Width - S(64));
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
                                BackColor = isActiveChoice ? Color.FromArgb(218, 245, 228) : Color.FromArgb(246, 249, 253),
                                ForeColor = isActiveChoice ? Color.FromArgb(14, 96, 48) : Color.FromArgb(42, 58, 78),
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
                                BackColor = isActiveChoice ? Color.FromArgb(218, 245, 228) : Color.FromArgb(246, 249, 253),
                                ForeColor = isActiveChoice ? Color.FromArgb(14, 96, 48) : Color.FromArgb(42, 58, 78),
                                Cursor = Cursors.Hand
                            };
                        }
                        btnOpt.FlatAppearance.BorderColor = isActiveChoice
                            ? Color.FromArgb(92, 184, 128)
                            : Color.FromArgb(204, 214, 228);

                        string pickedWord = cand.Replacement;
                        btnOpt.Click += (s, e) =>
                        {
                            entry.ChosenReplacement = pickedWord;
                            entry.IsChecked = true;
                            int rIdx = GetSelectedRowIndex();
                            if (rIdx >= 0)
                                _grid.InvalidateRow(rIdx);
                            else
                                _grid.Invalidate();
                            UpdateSummaryAndButtons();
                            UpdateSelectedEntryInspector();
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
                $"Total ({total})  •  Selected ({checkedCount})  •  Skipped ({skippedCount})",
                $"စုစုပေါင်း ({total}) ခု  •  ပြင်ဆင်မည် ({checkedCount}) ခု  •  ချန်လှပ်ထားသည် ({skippedCount}) ခု");
            _btnConfirm.Text = checkedCount > 0
                ? UiLocalizer.Pick(
                    $"✓ Apply ({checkedCount}) Selected Fixes",
                    $"✓ ရွေးချယ်ထားသော ({checkedCount}) ခုကို ပြင်ဆင်မည်")
                : UiLocalizer.Pick(
                    "No Fixes Selected",
                    "ရွေးချယ်ထားသော အချက်မရှိပါ");
            _btnConfirm.Enabled = checkedCount > 0;
            _btnConfirm.BackColor = checkedCount > 0
                ? Color.FromArgb(20, 122, 75)
                : Color.FromArgb(172, 182, 194);

            if (_footerPanel != null && _footerPanel.Width > 40)
            {
                Size cancelSz = MeasureSingleLineButtonSize(_btnCancel.Text, _fontBody, 28, 12);
                Size confirmSz = MeasureSingleLineButtonSize(_btnConfirm.Text, _fontBodyBold, 34, 12);
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
            foreach (var entry in _allEntries)
            {
                if (entry.IsChecked && !string.IsNullOrEmpty(entry.ChosenReplacement))
                {
                    ApprovedReplacements[entry.Payload] = entry.ChosenReplacement;
                }
            }

            if (ApprovedReplacements.Count == 0)
                return;

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string TruncateCue(string text, int maxChars)
        {
            string clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
            int limit = Math.Max(maxChars, 38);
            return clean.Length <= limit
                ? clean
                : MyanmarSpellChecker.Core.Nlp.MyanmarSyllableBreaker.TruncateAtSyllableBoundary(clean, limit, "…");
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
