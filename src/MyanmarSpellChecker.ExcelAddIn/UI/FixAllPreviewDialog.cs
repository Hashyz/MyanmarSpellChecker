using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
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

    /// <summary>
    /// High-DPI Safe Fix-All Preview Checklist Dialog for Microsoft Excel.
    /// Pre-checks (☑) 100% deterministic orthography/lookalike/grammar rules while leaving unknown names,
    /// fuzzy guesses, and contextual meaning warnings unchecked (☐) by default so user data is never blindly ruined.
    /// </summary>
    internal sealed class FixAllPreviewDialog<TPayload> : Form where TPayload : class
    {
        private readonly List<FixPreviewEntry<TPayload>> _allEntries;
        private List<FixPreviewEntry<TPayload>> _visibleEntries;
        private readonly Action<TPayload>? _onNavigateToItem;

        private readonly Font _fontTitle;
        private readonly Font _fontSubtitle;
        private readonly Font _fontBody;
        private readonly Font _fontBodyBold;
        private readonly Font _fontBadge;
        private readonly Font _fontMono;

        private DataGridView _grid = null!;
        private TextBox _txtSearch = null!;
        private Label _lblContextPreview = null!;
        private Label _lblExplanation = null!;
        private FlowLayoutPanel _choicesPanel = null!;
        private Label _lblFooterSummary = null!;
        private Button _btnConfirm = null!;
        private Button _btnJump = null!;
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

            _fontTitle = UiFontCache.GetMyanmar(11.5f, FontStyle.Bold);
            _fontSubtitle = UiFontCache.GetMyanmar(9.0f, FontStyle.Regular);
            _fontBody = UiFontCache.GetMyanmar(9.5f, FontStyle.Regular);
            _fontBodyBold = UiFontCache.GetMyanmar(9.5f, FontStyle.Bold);
            _fontBadge = UiFontCache.GetMyanmar(8.75f, FontStyle.Bold);
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeDialogUi(string scopeDisplayTitle)
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = $"အားလုံးမပြင်မီ စိစစ်အတည်ပြုရန် — {scopeDisplayTitle}";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowIcon = false;
            KeyPreview = true;
            BackColor = Color.FromArgb(245, 248, 252);
            ClientSize = new Size(920, 640);
            MinimumSize = new Size(740, 500);

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

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(14, 12, 14, 12),
                BackColor = Color.FromArgb(245, 248, 252)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Header banner
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Filter & selection bar
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 2: Checklist grid
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3: Selected row inspector
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: Footer buttons

            // 0. Header Banner Card
            var headerCard = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0, 0, 0, 8)
            };
            headerCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(212, 224, 238)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, headerCard.Width - 1, headerCard.Height - 1);
                }
                using (var accent = new SolidBrush(Color.FromArgb(20, 122, 75)))
                {
                    e.Graphics.FillRectangle(accent, 0, 0, 5, headerCard.Height);
                }
            };

            var headerStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            headerStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var lblTitle = new Label
            {
                Text = $"အားလုံးမပြင်မီ စာရင်းဇယားဖြင့် စိစစ်အတည်ပြုရန် ({scopeDisplayTitle})",
                Font = _fontTitle,
                ForeColor = Color.FromArgb(16, 42, 72),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 0, 4)
            };

            var lblGuide = new Label
            {
                Text = $"✓ သေချာသော စံသတ်ပုံ/သဒ္ဒါ ({safeCount}) ခုကို အမှန်ခြစ်ပေးထားပြီး (☑)၊ လူအမည်/ဒေသအမည် သို့မဟုတ် စစ်ပြီးသားဖြစ်နိုင်သော ခန့်မှန်းချက်/အနက်သံသယ ({reviewCount}) ခုကို မှားမပြင်မိစေရန် အမှန်ခြစ်ဖြုတ်ထားပါသည် (☐)။",
                Font = _fontSubtitle,
                ForeColor = Color.FromArgb(52, 72, 96),
                AutoSize = true,
                MaximumSize = new Size(850, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 0, 0)
            };
            headerCard.Resize += (s, e) =>
            {
                int w = Math.Max(320, headerCard.ClientSize.Width - 36);
                lblGuide.MaximumSize = new Size(w, 0);
            };

            headerStack.Controls.Add(lblTitle, 0, 0);
            headerStack.Controls.Add(lblGuide, 0, 1);
            headerCard.Controls.Add(headerStack);
            root.Controls.Add(headerCard, 0, 0);

            // 1. Selection & Search Toolbar
            var toolRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.Transparent
            };
            toolRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            toolRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));

            var btnSafeOnly = CreateToolbarButton($"☑ သေချာသည်များသာ ({safeCount})", Color.FromArgb(224, 245, 233), Color.FromArgb(16, 102, 54), Color.FromArgb(148, 208, 172));
            btnSafeOnly.Click += (s, e) =>
            {
                foreach (var item in _allEntries)
                    item.IsChecked = item.IsHighCertaintyDefault;
                RefreshCheckboxColumnValues();
            };

            var btnCheckAll = CreateToolbarButton($"☑ အားလုံးရွေး ({totalCount})", Color.White, Color.FromArgb(28, 56, 92), Color.FromArgb(196, 210, 228));
            btnCheckAll.Click += (s, e) =>
            {
                foreach (var item in _visibleEntries)
                    item.IsChecked = true;
                RefreshCheckboxColumnValues();
            };

            var btnUncheckAll = CreateToolbarButton("☐ အားလုံးဖြုတ်", Color.White, Color.FromArgb(92, 104, 120), Color.FromArgb(204, 214, 226));
            btnUncheckAll.Click += (s, e) =>
            {
                foreach (var item in _visibleEntries)
                    item.IsChecked = false;
                RefreshCheckboxColumnValues();
            };

            var lblSearchHint = new Label
            {
                Text = "ရှာဖွေရန် —",
                Font = _fontBadge,
                ForeColor = Color.FromArgb(72, 90, 112),
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(8, 0, 6, 0)
            };

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontBody,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 2, 0, 2)
            };
            _txtSearch.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };

            toolRow.Controls.Add(btnSafeOnly, 0, 0);
            toolRow.Controls.Add(btnCheckAll, 1, 0);
            toolRow.Controls.Add(btnUncheckAll, 2, 0);
            toolRow.Controls.Add(lblSearchHint, 3, 0);
            toolRow.Controls.Add(_txtSearch, 4, 0);
            root.Controls.Add(toolRow, 0, 1);

            // 2. Virtualized Checklist DataGridView (O(1) instant open & zero GDI bloat even on 10,000+ issues)
            int measuredLineHeight = TextRenderer.MeasureText("မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း", _fontBody).Height;
            int safeRowHeight = Math.Max(40, measuredLineHeight + 16);
            int safeHeaderHeight = Math.Max(38, measuredLineHeight + 14);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
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
                Font = _fontBody,
                Margin = new Padding(0, 0, 0, 8)
            };
            _grid.RowTemplate.Height = safeRowHeight;
            EnableDoubleBuffering(_grid);

            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(236, 242, 250),
                ForeColor = Color.FromArgb(20, 44, 76),
                Font = _fontBadge,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(6, 4, 6, 4),
                WrapMode = DataGridViewTriState.False
            };

            _grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(24, 36, 52),
                SelectionBackColor = Color.FromArgb(222, 238, 255),
                SelectionForeColor = Color.FromArgb(12, 32, 62),
                Font = _fontBody,
                Padding = new Padding(6, 2, 6, 2)
            };

            var colCheck = new DataGridViewTextBoxColumn
            {
                Name = "ColCheck",
                HeaderText = "ပြင်မည်",
                Width = 74,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = _fontBodyBold
                }
            };

            var colLoc = new DataGridViewTextBoxColumn
            {
                Name = "ColLocation",
                HeaderText = "နေရာ (Cell)",
                Width = 122,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontMono,
                    ForeColor = Color.FromArgb(32, 82, 136)
                }
            };

            var colOrig = new DataGridViewTextBoxColumn
            {
                Name = "ColOriginal",
                HeaderText = "မူလစာသား (အမှား/သံသယ)",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 34f
            };

            var colRep = new DataGridViewTextBoxColumn
            {
                Name = "ColReplacement",
                HeaderText = "ပြင်ဆင်မည့်စာသား",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 38f,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontBodyBold,
                    ForeColor = Color.FromArgb(16, 108, 56)
                }
            };

            var colCert = new DataGridViewTextBoxColumn
            {
                Name = "ColCertainty",
                HeaderText = "စိတ်ချရမှု အဆင့်",
                Width = 225,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = _fontBadge,
                    Alignment = DataGridViewContentAlignment.MiddleLeft
                }
            };

            _grid.Columns.AddRange(colCheck, colLoc, colOrig, colRep, colCert);

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

            root.Controls.Add(_grid, 0, 2);

            // 3. Selected Row Context & Alternative Choices Inspector Card
            var detailCard = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(0, 0, 0, 10)
            };
            detailCard.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(214, 224, 238)))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, detailCard.Width - 1, detailCard.Height - 1);
                }
            };

            var detailLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblContextPreview = new Label
            {
                Text = "စာရင်းမှ အတန်းတစ်တန်းကို နှိပ်၍ မူရင်းအကွက်စာသားနှင့် အခြားရွေးချယ်စရာများကို ကြည့်ရှုနိုင်ပါသည်။",
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(24, 48, 78),
                AutoSize = true,
                MaximumSize = new Size(720, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 4)
            };

            _btnJump = CreateToolbarButton("📍 အကွက်သို့ ကြည့်မည်", Color.FromArgb(238, 245, 255), Color.FromArgb(24, 82, 152), Color.FromArgb(172, 202, 240));
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
                MaximumSize = new Size(840, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 4)
            };

            _choicesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };

            detailCard.Resize += (s, e) =>
            {
                int w = Math.Max(300, detailCard.ClientSize.Width - 32);
                _lblContextPreview.MaximumSize = new Size(Math.Max(260, w - (_btnJump.Visible ? 160 : 0)), 0);
                _lblExplanation.MaximumSize = new Size(w, 0);
            };

            detailLayout.Controls.Add(_lblContextPreview, 0, 0);
            detailLayout.Controls.Add(_btnJump, 1, 0);
            detailLayout.Controls.Add(_lblExplanation, 0, 1);
            detailLayout.SetColumnSpan(_lblExplanation, 2);
            detailLayout.Controls.Add(_choicesPanel, 0, 2);
            detailLayout.SetColumnSpan(_choicesPanel, 2);
            detailCard.Controls.Add(detailLayout);
            root.Controls.Add(detailCard, 0, 3);

            // 4. Footer Bar
            var footerBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblFooterSummary = new Label
            {
                Text = string.Empty,
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(28, 52, 82),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(2, 0, 10, 0)
            };

            var btnCancel = new Button
            {
                Text = "မပြင်တော့ပါ (Esc)",
                Font = _fontBody,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 6, 14, 6),
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(62, 76, 94),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(192, 204, 218);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _btnConfirm = new Button
            {
                Text = "✓ ရွေးချယ်ထားသည်များကို ပြင်ဆင်မည်",
                Font = _fontBodyBold,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(18, 6, 18, 6),
                Margin = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 122, 75),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnConfirm.FlatAppearance.BorderColor = Color.FromArgb(14, 98, 58);
            _btnConfirm.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 104, 64);
            _btnConfirm.Click += OnConfirmClicked;

            footerBar.Controls.Add(_lblFooterSummary, 0, 0);
            footerBar.Controls.Add(btnCancel, 1, 0);
            footerBar.Controls.Add(_btnConfirm, 2, 0);
            root.Controls.Add(footerBar, 0, 4);

            Controls.Add(root);
        }

        private Button CreateToolbarButton(string text, Color bg, Color fg, Color border)
        {
            var btn = new Button
            {
                Text = text,
                Font = _fontBadge,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 4, 10, 4),
                Margin = new Padding(0, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = fg,
                Cursor = Cursors.Hand
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
                return $"➔  {entry.ChosenReplacement}   ({sugCount} မျိုး)";
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
                _choicesPanel.Controls.Clear();
                int selectedRowIdx = GetSelectedRowIndex();
                if (selectedRowIdx < 0)
                {
                    _selectedEntry = null;
                    _btnJump.Enabled = false;
                    _lblContextPreview.Text = "ပြင်ဆင်မည့် အချက်အလက် မရှိပါ။";
                    _lblExplanation.Text = string.Empty;
                    return;
                }

                FixPreviewEntry<TPayload> entry = _visibleEntries[selectedRowIdx];
                _selectedEntry = entry;
                _btnJump.Enabled = _onNavigateToItem != null;

                string cleanContext = (entry.ContextSnippet ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                if (cleanContext.Length > 180)
                    cleanContext = cleanContext.Substring(0, 180) + "…";

                _lblContextPreview.Text = $"[{entry.LocationLabel}] မူရင်းစာသား — {cleanContext}";
                _lblExplanation.Text = entry.Issue.Message ?? string.Empty;

                var sugs = entry.Issue.Suggestions;
                if (sugs != null && sugs.Count > 0)
                {
                    int maxChoices = Math.Min(sugs.Count, 5);
                    var lblPick = new Label
                    {
                        Text = maxChoices > 1 ? "ရွေးချယ်ပြင်ဆင်ရန် —" : "အကြံပြုချက် —",
                        Font = _fontBadge,
                        ForeColor = Color.FromArgb(44, 68, 96),
                        AutoSize = true,
                        Margin = new Padding(0, 6, 8, 2)
                    };
                    _choicesPanel.Controls.Add(lblPick);

                    for (int i = 0; i < maxChoices; i++)
                    {
                        SuggestionCandidate cand = sugs[i];
                        bool isActiveChoice = string.Equals(entry.ChosenReplacement, cand.Replacement, StringComparison.Ordinal);
                        string cueShort = string.IsNullOrWhiteSpace(cand.MeaningCue)
                            ? string.Empty
                            : $" [{TruncateCue(cand.MeaningCue, 34)}]";

                        var btnOpt = new Button
                        {
                            Text = $"{(isActiveChoice ? "✓ " : string.Empty)}{cand.Replacement}{cueShort}",
                            Font = isActiveChoice ? _fontBodyBold : _fontSubtitle,
                            AutoSize = true,
                            AutoSizeMode = AutoSizeMode.GrowAndShrink,
                            Padding = new Padding(8, 3, 8, 3),
                            Margin = new Padding(0, 2, 6, 2),
                            FlatStyle = FlatStyle.Flat,
                            BackColor = isActiveChoice ? Color.FromArgb(218, 245, 228) : Color.FromArgb(246, 249, 253),
                            ForeColor = isActiveChoice ? Color.FromArgb(14, 96, 48) : Color.FromArgb(42, 58, 78),
                            Cursor = Cursors.Hand
                        };
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

                        _choicesPanel.Controls.Add(btnOpt);
                    }
                }
            }
            finally
            {
                _choicesPanel.ResumeLayout();
            }
        }

        private void UpdateSummaryAndButtons()
        {
            int total = _allEntries.Count;
            int checkedCount = _allEntries.Count(x => x.IsChecked && !string.IsNullOrEmpty(x.ChosenReplacement));
            int skippedCount = total - checkedCount;

            _lblFooterSummary.Text = $"စုစုပေါင်း ({total}) ခု  •  ပြင်ဆင်မည် ({checkedCount}) ခု  •  ချန်လှပ်ထားသည် ({skippedCount}) ခု";
            _btnConfirm.Text = checkedCount > 0
                ? $"✓ ရွေးချယ်ထားသော ({checkedCount}) ခုကို ပြင်ဆင်မည်"
                : "ရွေးချယ်ထားသော အချက်မရှိပါ";
            _btnConfirm.Enabled = checkedCount > 0;
            _btnConfirm.BackColor = checkedCount > 0
                ? Color.FromArgb(20, 122, 75)
                : Color.FromArgb(172, 182, 194);
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
            return clean.Length <= maxChars ? clean : clean.Substring(0, maxChars) + "…";
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

