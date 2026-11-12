using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Spreadsheet;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public enum IssueFilterMode
    {
        All,
        SpellingOnly,
        GrammarOnly,
        ContextOnly
    }

    public enum TaskPaneWorkspaceTab
    {
        Proofing,
        Dictionary,
        Toolkit,
        CustomWords
    }

    public enum DictionaryCategoryFilter
    {
        All,
        OfficialThatpon,
        AbidanMeaning,
        EncyclopediaKnowledge,
        SpecializedDomains,
        ProperNounsAndPlaces,
        ProverbsAndSayings
    }

    /// <summary>
    /// Enterprise High-DPI responsive Custom Task Pane UserControl for Microsoft Excel.
    /// Provides a 4-Tab Workspace:
    /// (1) Formula-Safe Cell/Sheet/Workbook Proofing &amp; Reasoning + Multi-Level Undo,
    /// (2) 118,600+ Dictionary, 25,000+ Encyclopedia &amp; Proverbs Explorer,
    /// (3) Spreadsheet Typography, Kyat Lakh Accounting &amp; Myanmar Calendar Toolkit,
    /// (4) Shared Custom Words Manager (live-synced with Microsoft Word).
    /// </summary>
    [ComVisible(true)]
    [Guid("C4B3D852-AE29-4F3B-9D66-2E8C9B0D3E44")]
    [ProgId("MyanmarSpellChecker.ExcelTaskPaneControl")]
    public sealed partial class ExcelProofingTaskPaneControl : UserControl
    {
        private const string WrapTag = "DYNAMIC_WRAP";
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
        {
            public BufferedFlowLayoutPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        // Owned GDI fonts disposed cleanly with UserControl
        private readonly Font _fontBrandTitle = UiFontCache.CreateOwnedMyanmar(10.2F, FontStyle.Bold);
        private readonly Font _fontBrandStats = UiFontCache.CreateOwnedMyanmar(8.2F, FontStyle.Regular);
        private readonly Font _fontTabActive = UiFontCache.CreateOwnedMyanmar(8.8F, FontStyle.Bold);
        private readonly Font _fontTabNormal = UiFontCache.CreateOwnedMyanmar(8.8F, FontStyle.Regular);
        private readonly Font _fontCardDiff = UiFontCache.CreateOwnedMyanmar(10.5F, FontStyle.Bold);
        private readonly Font _fontCardWord = UiFontCache.CreateOwnedMyanmar(10.5F, FontStyle.Bold);
        private readonly Font _fontBody = UiFontCache.CreateOwnedMyanmar(9F, FontStyle.Regular);
        private readonly Font _fontBodyBold = UiFontCache.CreateOwnedMyanmar(9F, FontStyle.Bold);
        private readonly Font _fontSuggest = UiFontCache.CreateOwnedMyanmar(9.3F, FontStyle.Bold);
        private readonly Font _fontSmall = UiFontCache.CreateOwnedMyanmar(8.4F, FontStyle.Regular);
        private readonly Font _fontSmallBold = UiFontCache.CreateOwnedMyanmar(8.4F, FontStyle.Bold);
        private readonly Font _fontInput = UiFontCache.CreateOwnedMyanmar(9.8F, FontStyle.Regular);

        private readonly ToolTip _toolTip = new ToolTip { InitialDelay = 350, ReshowDelay = 200 };
        private readonly Timer _searchDebounceTimer;
        private readonly Timer _toastTimer;

        // Top Navigation & Brand Controls
        private readonly Label _lblLexiconStats;
        private readonly Button _btnTabProofing;
        private readonly Button _btnTabDictionary;
        private readonly Button _btnTabToolkit;
        private readonly Button _btnTabCustom;

        // Contextual Workspace Sub-Toolbars
        private readonly TableLayoutPanel _proofingToolbar;
        private readonly TableLayoutPanel _dictionaryToolbar;
        private readonly TableLayoutPanel _toolkitToolbar;
        private readonly TableLayoutPanel _customToolbar;

        // Proofing Toolbar Controls
        private readonly Button _btnStyleGeneral;
        private readonly Button _btnStyleFormal;
        private readonly Label _lblStyleHint;
        private readonly Button _btnScopeSheet;
        private readonly Button _btnScopeSelection;
        private readonly Button _btnScopeWorkbook;
        private readonly Button _btnScanNow;
        private readonly Button _btnFixAll;
        private readonly Button _btnUndoLast;
        private readonly Button _btnFilterAll;
        private readonly Button _btnFilterSpelling;
        private readonly Button _btnFilterGrammar;
        private readonly Button _btnFilterContext;
        private readonly Button _btnPrevIssue;
        private readonly Button _btnNextIssue;
        private readonly Label _lblSummary;
        private readonly Panel _pnlToast;
        private readonly Label _lblToast;

        // Dictionary Toolbar Controls
        private readonly TextBox _txtDictionarySearch;
        private readonly Button _btnDictCatAll;
        private readonly Button _btnDictCatThatpon;
        private readonly Button _btnDictCatAbidan;
        private readonly Button _btnDictCatEncyc;
        private readonly Button _btnDictCatDomain;
        private readonly Button _btnDictCatPropn;
        private readonly Button _btnDictCatProverb;
        private readonly Label _lblDictSummary;

        // Toolkit State & Controls
        private readonly Label _lblToolkitSummary;
        private DocumentQualityReport? _latestQualityReport;
        private NumberConversionMode _numberMode = NumberConversionMode.BankingKyatLakh;
        private string _numberInputSample = "1,500,000";
        private DateTime _selectedCalendarDate = DateTime.Today;
        private bool _toolkitSelectionOnly;

        // Custom Words Toolbar Controls
        private readonly TextBox _txtNewCustomWord;
        private readonly Label _lblCustomSummary;

        // Main Scrollable Canvas
        private readonly BufferedFlowLayoutPanel _cardsPanel;

        // State
        private List<SpreadsheetCellIssue> _allItems = new List<SpreadsheetCellIssue>();
        private readonly Dictionary<SpreadsheetCellIssue, Control> _cardByItem = new Dictionary<SpreadsheetCellIssue, Control>();
        private SpreadsheetCellIssue? _selectedItem;
        private int _stepperCursor = -1;
        private int _lastAppliedCardWidth = -1;
        private int _lastAppliedControlCount = -1;
        private bool _isScanning;
        private int _undoAvailableCount;
        private string _undoLatestLabel = string.Empty;
        private TaskPaneWorkspaceTab _activeTab = TaskPaneWorkspaceTab.Proofing;
        private IssueFilterMode _currentFilter = IssueFilterMode.All;
        private DictionaryCategoryFilter _dictCategoryFilter = DictionaryCategoryFilter.All;
        private ProofingStyleMode _currentStyleMode = ProofingStyleMode.GeneralStandard;
        private SpreadsheetScanScope _currentScanScope = SpreadsheetScanScope.ActiveSheet;

        // Events wired by ExcelThisAddIn
        public event Action<SpreadsheetScanScope>? RequestScanScope;
        public event Action<IssueFilterMode>? RequestFixAllIssues;
        public event Action? RequestUndoLastAction;
        public event Action? RequestLicenseDialog;
        public event Action<ProofingStyleMode>? RequestChangeStyleMode;
        public event Action<SpreadsheetCellIssue>? RequestSelectIssueInSheet;
        public event Action<SpreadsheetCellIssue, string>? RequestApplySuggestion;
        public event Action<string, string>? RequestApplyBatchSuggestion;
        public event Action<SpreadsheetCellIssue>? RequestIgnoreOnce;
        public event Action<SpreadsheetCellIssue>? RequestAddToDictionary;

        // Dictionary Events
        public event Func<string, List<DictionaryEntry>>? RequestDictionarySearch;
        public event Func<string, bool, bool, List<DictionaryEntry>>? RequestSearchReferenceCorpus;
        public event Func<List<DictionaryEntry>>? RequestGetFeaturedKnowledge;
        public event Func<string, List<string>>? RequestGetSynonyms;
        public event Func<string, List<string>>? RequestGetAntonyms;
        public event Func<string, string>? RequestHomophoneSummary;
        public event Func<string>? RequestGetSelectedCellText;
        public event Func<string>? RequestGetSelectedNumericText;
        public event Action<string>? RequestInsertTextIntoCell;

        // Custom Words Events
        public event Func<IReadOnlyList<string>>? RequestGetCustomWords;
        public event Func<string, bool>? RequestAddCustomWordManual;
        public event Func<string, bool>? RequestRemoveCustomWord;

        // Spreadsheet Toolkit Events
        public event Action<bool>? RequestConvertLegacyFonts;
        public event Action<bool, bool>? RequestFormatNumerals;
        public event Action<bool>? RequestRewriteToOfficialStyle;
        public event Action<bool>? RequestFormatPunctuation;
        public event Func<DocumentQualityReport>? RequestRefreshQualityReport;
        public event Action? RequestExportAuditWorkbook;

        public ExcelProofingTaskPaneControl()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(243, 245, 249);
            Font = _fontBody;
            Dock = DockStyle.Fill;

            _searchDebounceTimer = new Timer { Interval = 140 };
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ExecuteDictionarySearchNow();
            };

            _toastTimer = new Timer { Interval = 4500 };
            _toastTimer.Tick += OnToastTimerTick;

            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(243, 245, 249),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var headerShell = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Color.White,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0, 0, 0, 2)
            };
            headerShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int r = 0; r < 7; r++)
            {
                headerShell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            headerShell.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(220, 226, 236), 1F))
                {
                    e.Graphics.DrawLine(pen, 0, headerShell.Height - 1, headerShell.Width, headerShell.Height - 1);
                }
            };

            // Row 0: Brand Header + About/License Pill
            var brandRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5)
            };
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brandRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblTitle = new Label
            {
                Text = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Excel)",
                Font = _fontBrandTitle,
                ForeColor = Color.FromArgb(16, 92, 50),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(6, 1, 4, 2)
            };

            _lblLexiconStats = new Label
            {
                Text = "Formula-Safe Excel Suite • ထက်ရာဇာ (KU)",
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(88, 100, 118),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(6, 0, 4, 2)
            };
            const string authorCreditTip = "ဖန်တီးသူ - ထက်ရာဇာ (Htet Yar Zar) • M.Sc. Part II Candidate in Geology, ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ် (Kyaukse, Myanmar)";
            _toolTip.SetToolTip(lblTitle, authorCreditTip);
            _toolTip.SetToolTip(_lblLexiconStats, authorCreditTip);

            titleStack.Controls.Add(lblTitle, 0, 0);
            titleStack.Controls.Add(_lblLexiconStats, 0, 1);

            var btnLic = CreatePillButton(
                "အချက်အလက် ⓘ",
                Color.FromArgb(236, 248, 241),
                Color.FromArgb(16, 104, 54),
                Color.FromArgb(178, 218, 194),
                _fontSmallBold);
            btnLic.Dock = DockStyle.None;
            btnLic.Anchor = AnchorStyles.Right;
            btnLic.Padding = new Padding(8, 3, 8, 3);
            btnLic.Margin = new Padding(4, 2, 0, 2);
            btnLic.Click += (s, e) => RequestLicenseDialog?.Invoke();
            _toolTip.SetToolTip(btnLic, "စနစ်အချက်အလက်၊ ဖန်တီးသူ (ထက်ရာဇာ — ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင်");

            brandRow.Controls.Add(titleStack, 0, 0);
            brandRow.Controls.Add(btnLic, 1, 0);

            // Row 1: 4-Tab Segmented Workspace Switcher
            var tabStrip = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.FromArgb(236, 240, 247),
                Padding = new Padding(3),
                Margin = new Padding(0, 0, 0, 6)
            };
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
            tabStrip.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnTabProofing = CreateTabSegmentButton("စာစစ်");
            _btnTabDictionary = CreateTabSegmentButton("အဘိဓာန်");
            _btnTabToolkit = CreateTabSegmentButton("ကိရိယာ");
            _btnTabCustom = CreateTabSegmentButton("မှတ်သားစာ");

            _btnTabProofing.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Proofing);
            _btnTabDictionary.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Dictionary);
            _btnTabToolkit.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Toolkit);
            _btnTabCustom.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.CustomWords);

            tabStrip.Controls.Add(_btnTabProofing, 0, 0);
            tabStrip.Controls.Add(_btnTabDictionary, 1, 0);
            tabStrip.Controls.Add(_btnTabToolkit, 2, 0);
            tabStrip.Controls.Add(_btnTabCustom, 3, 0);

            // ================= TAB 1 TOOLBAR: EXCEL PROOFING =================
            _proofingToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 6,
                Margin = new Padding(0)
            };
            _proofingToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
                _proofingToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Style mode toggle row
            var styleRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(241, 244, 250),
                Padding = new Padding(2),
                Margin = new Padding(0, 0, 0, 2)
            };
            styleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            styleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            styleRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnStyleGeneral = CreateTabSegmentButton("အထွေထွေ စံသတ်ပုံ");
            _btnStyleFormal = CreateTabSegmentButton("ရုံးသုံး / စာပေဟန်");
            _btnStyleGeneral.Click += (s, e) => OnUserSelectedStyleMode(ProofingStyleMode.GeneralStandard);
            _btnStyleFormal.Click += (s, e) => OnUserSelectedStyleMode(ProofingStyleMode.FormalOfficial);
            styleRow.Controls.Add(_btnStyleGeneral, 0, 0);
            styleRow.Controls.Add(_btnStyleFormal, 1, 0);

            _lblStyleHint = new Label
            {
                Text = "စံသတ်ပုံ + Formula ကာကွယ်မှု (အပြောစကား ပါတယ်/တွေ ခွင့်ပြု)",
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(84, 96, 114),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(1, 0, 1, 4)
            };

            // 3-Scope Selector Row (Sheet | Selection | Workbook)
            var scopeRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.FromArgb(239, 246, 242),
                Padding = new Padding(2),
                Margin = new Padding(0, 0, 0, 4)
            };
            scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            scopeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            scopeRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnScopeSheet = CreateTabSegmentButton("လက်ရှိ Sheet");
            _btnScopeSelection = CreateTabSegmentButton("ရွေးထားကွက်");
            _btnScopeWorkbook = CreateTabSegmentButton("Workbook လုံး");
            _btnScopeSheet.Click += (s, e) => SelectAndScanScope(SpreadsheetScanScope.ActiveSheet);
            _btnScopeSelection.Click += (s, e) => SelectAndScanScope(SpreadsheetScanScope.Selection);
            _btnScopeWorkbook.Click += (s, e) => SelectAndScanScope(SpreadsheetScanScope.EntireWorkbook);
            scopeRow.Controls.Add(_btnScopeSheet, 0, 0);
            scopeRow.Controls.Add(_btnScopeSelection, 1, 0);
            scopeRow.Controls.Add(_btnScopeWorkbook, 2, 0);

            // Primary Action Buttons Row: Scan | Fix All | Undo
            var actionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            actionRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnScanNow = CreateActionButton("Sheet စစ်မည်", Color.FromArgb(16, 124, 65), Color.White, Color.FromArgb(14, 102, 53), _fontSmallBold);
            _btnScanNow.Margin = new Padding(0, 0, 3, 0);
            _btnScanNow.Click += (s, e) => RequestScanScope?.Invoke(_currentScanScope);

            _btnFixAll = CreateActionButton("အားလုံးပြင်", Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(18, 72, 158), _fontSmallBold);
            _btnFixAll.Margin = new Padding(2, 0, 3, 0);
            _btnFixAll.Click += (s, e) => RequestFixAllIssues?.Invoke(_currentFilter);

            _btnUndoLast = CreateActionButton("↶ ပြန်ဆုတ်", Color.FromArgb(244, 246, 250), Color.FromArgb(55, 65, 82), Color.FromArgb(210, 218, 230), _fontSmallBold);
            _btnUndoLast.Margin = new Padding(2, 0, 0, 0);
            _btnUndoLast.Enabled = false;
            _btnUndoLast.Click += (s, e) => RequestUndoLastAction?.Invoke();

            actionRow.Controls.Add(_btnScanNow, 0, 0);
            actionRow.Controls.Add(_btnFixAll, 1, 0);
            actionRow.Controls.Add(_btnUndoLast, 2, 0);

            // 4-Pill Filter + Stepper Row
            var filterRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21F));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21F));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 7F));
            filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnFilterAll = CreateFilterPill("အားလုံး (0)");
            _btnFilterSpelling = CreateFilterPill("သတ်ပုံ (0)");
            _btnFilterGrammar = CreateFilterPill("သဒ္ဒါ (0)");
            _btnFilterContext = CreateFilterPill("အနက် (0)");
            _btnPrevIssue = CreateFilterPill("↑");
            _btnNextIssue = CreateFilterPill("↓");
            _btnPrevIssue.Padding = new Padding(1, 3, 1, 3);
            _btnNextIssue.Padding = new Padding(1, 3, 1, 3);

            _btnFilterAll.Click += (s, e) => SetIssueFilter(IssueFilterMode.All);
            _btnFilterSpelling.Click += (s, e) => SetIssueFilter(IssueFilterMode.SpellingOnly);
            _btnFilterGrammar.Click += (s, e) => SetIssueFilter(IssueFilterMode.GrammarOnly);
            _btnFilterContext.Click += (s, e) => SetIssueFilter(IssueFilterMode.ContextOnly);
            _btnPrevIssue.Click += (s, e) => StepIssue(-1);
            _btnNextIssue.Click += (s, e) => StepIssue(+1);

            filterRow.Controls.Add(_btnFilterAll, 0, 0);
            filterRow.Controls.Add(_btnFilterSpelling, 1, 0);
            filterRow.Controls.Add(_btnFilterGrammar, 2, 0);
            filterRow.Controls.Add(_btnFilterContext, 3, 0);
            filterRow.Controls.Add(_btnPrevIssue, 4, 0);
            filterRow.Controls.Add(_btnNextIssue, 5, 0);

            _lblSummary = new Label
            {
                Text = "Sheet စစ်မည် ကိုနှိပ်၍ မြန်မာစာ အကွက်များကို Formula မပျက်စေဘဲ စစ်ဆေးပါ",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(16, 98, 52),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(1, 1, 1, 1)
            };

            _proofingToolbar.Controls.Add(styleRow, 0, 0);
            _proofingToolbar.Controls.Add(_lblStyleHint, 0, 1);
            _proofingToolbar.Controls.Add(scopeRow, 0, 2);
            _proofingToolbar.Controls.Add(actionRow, 0, 3);
            _proofingToolbar.Controls.Add(filterRow, 0, 4);
            _proofingToolbar.Controls.Add(_lblSummary, 0, 5);

            // ================= TAB 2 TOOLBAR: DICTIONARY & ENCYCLOPEDIA =================
            _dictionaryToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Visible = false,
                Margin = new Padding(0)
            };
            _dictionaryToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var searchBoxGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            searchBoxGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80F));
            searchBoxGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            searchBoxGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtDictionarySearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontInput,
                Margin = new Padding(0, 2, 4, 2)
            };
            _txtDictionarySearch.HandleCreated += (s, e) =>
                SendMessage(_txtDictionarySearch.Handle, EM_SETCUEBANNER, (IntPtr)1, "စာလုံး / အနက် / စွယ်စုံဗဟုသုတ ရိုက်ရှာပါ...");
            _txtDictionarySearch.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };

            var btnClearSearch = CreatePillButton("ရှင်း", Color.FromArgb(242, 244, 248), Color.FromArgb(65, 75, 92), Color.FromArgb(214, 220, 232), _fontSmall);
            btnClearSearch.Dock = DockStyle.Fill;
            btnClearSearch.Click += (s, e) =>
            {
                _txtDictionarySearch.Clear();
                _txtDictionarySearch.Focus();
            };
            searchBoxGrid.Controls.Add(_txtDictionarySearch, 0, 0);
            searchBoxGrid.Controls.Add(btnClearSearch, 1, 0);

            var dictChipFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            _btnDictCatAll = CreateChipButton("အားလုံး");
            _btnDictCatThatpon = CreateChipButton("သတ်ပုံကျမ်း");
            _btnDictCatAbidan = CreateChipButton("အဘိဓာန်");
            _btnDictCatEncyc = CreateChipButton("စွယ်စုံဗဟုသုတ");
            _btnDictCatDomain = CreateChipButton("ပညာရပ်");
            _btnDictCatPropn = CreateChipButton("မြို့ရွာ/နိုင်ငံ");
            _btnDictCatProverb = CreateChipButton("စကားပုံ");

            _btnDictCatAll.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.All);
            _btnDictCatThatpon.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.OfficialThatpon);
            _btnDictCatAbidan.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.AbidanMeaning);
            _btnDictCatEncyc.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.EncyclopediaKnowledge);
            _btnDictCatDomain.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.SpecializedDomains);
            _btnDictCatPropn.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.ProperNounsAndPlaces);
            _btnDictCatProverb.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.ProverbsAndSayings);

            dictChipFlow.Controls.AddRange(new Control[]
            {
                _btnDictCatAll, _btnDictCatThatpon, _btnDictCatAbidan, _btnDictCatEncyc,
                _btnDictCatDomain, _btnDictCatPropn, _btnDictCatProverb
            });

            _lblDictSummary = new Label
            {
                Text = "မြန်မာ-မြန်မာ အဘိဓာန်ကြီး၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) နှင့် စကားပုံများ ရှာဖွေနိုင်သည်",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(24, 82, 172),
                AutoSize = true,
                Dock = DockStyle.Fill
            };

            _dictionaryToolbar.Controls.Add(searchBoxGrid, 0, 0);
            _dictionaryToolbar.Controls.Add(dictChipFlow, 0, 1);
            _dictionaryToolbar.Controls.Add(_lblDictSummary, 0, 2);

            // ================= TAB 3 TOOLBAR: EXCEL SPREADSHEET TOOLKIT =================
            _toolkitToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 1,
                Visible = false,
                Margin = new Padding(0)
            };
            _toolkitToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolkitToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblToolkitSummary = new Label
            {
                Text = "✓ Formula (=SUM, =IF, =VLOOKUP) နှင့် ကိန်းဂဏန်းတွက်ချက်မှုများကို 100% အလိုအလျောက် ကာကွယ်ထားသည်",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(16, 108, 56),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(1, 2, 1, 2)
            };
            _toolkitToolbar.Controls.Add(_lblToolkitSummary, 0, 0);

            // ================= TAB 4 TOOLBAR: CUSTOM WORDS =================
            _customToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Visible = false,
                Margin = new Padding(0)
            };
            _customToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _customToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _customToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var addWordRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            addWordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
            addWordRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            addWordRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtNewCustomWord = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontInput,
                Margin = new Padding(0, 2, 4, 2)
            };
            _txtNewCustomWord.HandleCreated += (s, e) =>
                SendMessage(_txtNewCustomWord.Handle, EM_SETCUEBANNER, (IntPtr)1, "မှတ်သားလိုသော စာလုံးသစ်...");
            _txtNewCustomWord.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddNewCustomWordFromInput();
                }
            };

            var btnAddCustom = CreateActionButton("+ ထည့်မည်", Color.FromArgb(16, 124, 65), Color.White, Color.FromArgb(14, 102, 53), _fontSmallBold);
            btnAddCustom.Click += (s, e) => AddNewCustomWordFromInput();
            addWordRow.Controls.Add(_txtNewCustomWord, 0, 0);
            addWordRow.Controls.Add(btnAddCustom, 1, 0);

            _lblCustomSummary = new Label
            {
                Text = "Word နှင့် Excel နှစ်မျိုးလုံးတွင် တူညီစွာ မျှဝေမှတ်သားထားသော စာလုံးများ",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(72, 84, 102),
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            _customToolbar.Controls.Add(addWordRow, 0, 0);
            _customToolbar.Controls.Add(_lblCustomSummary, 0, 1);

            // Toast Notification Banner
            _pnlToast = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(230, 246, 236),
                Padding = new Padding(8, 6, 8, 6),
                Margin = new Padding(0, 4, 0, 0),
                Visible = false
            };
            _lblToast = new Label
            {
                AutoSize = true,
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(16, 102, 52),
                Dock = DockStyle.Fill
            };
            _pnlToast.Controls.Add(_lblToast);

            headerShell.Controls.Add(brandRow, 0, 0);
            headerShell.Controls.Add(tabStrip, 0, 1);
            headerShell.Controls.Add(_proofingToolbar, 0, 2);
            headerShell.Controls.Add(_dictionaryToolbar, 0, 3);
            headerShell.Controls.Add(_toolkitToolbar, 0, 4);
            headerShell.Controls.Add(_customToolbar, 0, 5);
            headerShell.Controls.Add(_pnlToast, 0, 6);

            _cardsPanel = new BufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(243, 245, 249),
                Padding = new Padding(8, 8, 10, 18)
            };
            _cardsPanel.SizeChanged += (s, e) => ReflowCardWidthsIfNeeded();

            rootLayout.Controls.Add(headerShell, 0, 0);
            rootLayout.Controls.Add(_cardsPanel, 0, 1);
            Controls.Add(rootLayout);

            UpdateTabButtonStyles();
            UpdateStyleModeButtons();
            UpdateScopeButtons();
            UpdateFilterPillStyles();
            UpdateDictionaryCategoryStyles();
            RenderProofingCards();
        }

        public void SetEngineWarmupState()
        {
            _lblLexiconStats.Text = "အဘိဓာန်နှင့် သတ်ပုံကျမ်း ဖွင့်လှစ်နေသည်...";
            _lblSummary.Text = "⏳ နောက်ခံ၌ ဝေါဟာရ (၁၁၈,၆၀၀+) ကို အသင့်ပြင်ဆင်နေပါသည်...";
        }

        public void SetLexiconStats(int wordCount, int officialCount, int ruleCount, int referenceCount, int encycCount)
        {
            int total = wordCount + referenceCount;
            _lblLexiconStats.Text = $"စုစုပေါင်း {total:N0} ပုဒ် • ဗဟုသုတ {encycCount:N0} • ထက်ရာဇာ (KU)";
        }

        public void SetStyleMode(ProofingStyleMode mode)
        {
            _currentStyleMode = mode;
            UpdateStyleModeButtons();
        }

        public void SetScanScope(SpreadsheetScanScope scope)
        {
            _currentScanScope = scope;
            UpdateScopeButtons();
        }

        public void SetUndoState(int undoCount, string latestDescription)
        {
            _undoAvailableCount = Math.Max(0, undoCount);
            _undoLatestLabel = latestDescription ?? string.Empty;
            _btnUndoLast.Enabled = _undoAvailableCount > 0;
            _btnUndoLast.Text = _undoAvailableCount > 0 ? $"↶ ပြန်ဆုတ် ({_undoAvailableCount})" : "↶ ပြန်ဆုတ်";
            _btnUndoLast.BackColor = _undoAvailableCount > 0
                ? Color.FromArgb(255, 248, 229)
                : Color.FromArgb(244, 246, 250);
            _btnUndoLast.ForeColor = _undoAvailableCount > 0
                ? Color.FromArgb(148, 86, 8)
                : Color.FromArgb(120, 128, 140);
            _toolTip.SetToolTip(
                _btnUndoLast,
                _undoAvailableCount > 0
                    ? $"နောက်ဆုံးပြင်ဆင်ချက်ကို မူလအတိုင်း ပြန်ဆုတ်ရန်: {_undoLatestLabel}"
                    : "ပြန်ဆုတ်ရန် မှတ်တမ်း မရှိသေးပါ");
        }

        public void SetScanningState(bool scanning)
        {
            _isScanning = scanning;
            if (scanning)
            {
                _lblSummary.Text = "⏳ Excel အကွက်များကို အမြန်စစ်ဆေးနေပါသည်...";
                _lblSummary.ForeColor = Color.FromArgb(24, 90, 188);
            }
        }

        public void ShowToastBanner(string message, bool isSuccess = true)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _lblToast.Text = message;
            _lblToast.MaximumSize = new Size(Math.Max(180, GetTargetCardWidth() - 20), 0);
            _pnlToast.BackColor = isSuccess ? Color.FromArgb(230, 246, 236) : Color.FromArgb(255, 244, 226);
            _lblToast.ForeColor = isSuccess ? Color.FromArgb(16, 102, 52) : Color.FromArgb(158, 84, 8);
            _pnlToast.Visible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void OnToastTimerTick(object? sender, EventArgs e)
        {
            _toastTimer.Stop();
            if (!_pnlToast.IsDisposed)
                _pnlToast.Visible = false;
        }

        public void RefreshActiveTabAfterEngineReady()
        {
            if (_activeTab == TaskPaneWorkspaceTab.Dictionary)
                ExecuteDictionarySearchNow();
            else if (_activeTab == TaskPaneWorkspaceTab.Toolkit)
                RenderToolkitWorkspace();
            else if (_activeTab == TaskPaneWorkspaceTab.CustomWords)
                RenderCustomWordsWorkspace();
        }

        public void OpenDictionaryWithQuery(string? query = null)
        {
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.Dictionary);
            if (!string.IsNullOrWhiteSpace(query))
            {
                _txtDictionarySearch.Text = query!.Trim();
                ExecuteDictionarySearchNow();
            }
            _txtDictionarySearch.Focus();
        }

        public void OpenToolkitTab() => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Toolkit);
        public void OpenCustomWordsTab() => SwitchWorkspaceTab(TaskPaneWorkspaceTab.CustomWords);

        private void SwitchWorkspaceTab(TaskPaneWorkspaceTab tab)
        {
            _activeTab = tab;
            _proofingToolbar.Visible = tab == TaskPaneWorkspaceTab.Proofing;
            _dictionaryToolbar.Visible = tab == TaskPaneWorkspaceTab.Dictionary;
            _toolkitToolbar.Visible = tab == TaskPaneWorkspaceTab.Toolkit;
            _customToolbar.Visible = tab == TaskPaneWorkspaceTab.CustomWords;
            UpdateTabButtonStyles();

            switch (tab)
            {
                case TaskPaneWorkspaceTab.Proofing:
                    RenderProofingCards();
                    break;
                case TaskPaneWorkspaceTab.Dictionary:
                    if (string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
                    {
                        string sel = RequestGetSelectedCellText?.Invoke() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(sel) && SpreadsheetProofingHelper.ContainsMyanmarScript(sel) && sel.Length <= 36)
                        {
                            _txtDictionarySearch.Text = sel;
                        }
                    }
                    ExecuteDictionarySearchNow();
                    break;
                case TaskPaneWorkspaceTab.Toolkit:
                    string numSel = RequestGetSelectedNumericText?.Invoke() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(numSel))
                        _numberInputSample = numSel;
                    RenderToolkitWorkspace();
                    break;
                case TaskPaneWorkspaceTab.CustomWords:
                    RenderCustomWordsWorkspace();
                    break;
            }
        }

        private void UpdateTabButtonStyles()
        {
            StyleSegment(_btnTabProofing, _activeTab == TaskPaneWorkspaceTab.Proofing, Color.FromArgb(16, 108, 56));
            StyleSegment(_btnTabDictionary, _activeTab == TaskPaneWorkspaceTab.Dictionary, Color.FromArgb(24, 90, 188));
            StyleSegment(_btnTabToolkit, _activeTab == TaskPaneWorkspaceTab.Toolkit, Color.FromArgb(16, 108, 56));
            StyleSegment(_btnTabCustom, _activeTab == TaskPaneWorkspaceTab.CustomWords, Color.FromArgb(24, 90, 188));
        }

        private void StyleSegment(Button btn, bool active, Color activeFore)
        {
            btn.BackColor = active ? Color.White : Color.Transparent;
            btn.ForeColor = active ? activeFore : Color.FromArgb(74, 86, 104);
            btn.Font = active ? _fontTabActive : _fontTabNormal;
            btn.FlatAppearance.BorderSize = active ? 1 : 0;
            btn.FlatAppearance.BorderColor = active ? Color.FromArgb(204, 214, 228) : Color.FromArgb(236, 240, 247);
        }

        private int GetTargetCardWidth()
        {
            int clientW = _cardsPanel.ClientSize.Width;
            if (clientW <= 80)
                clientW = Width > 80 ? Width - 22 : 390;
            return Math.Max(250, clientW - _cardsPanel.Padding.Horizontal - 6);
        }

        private void ReflowCardWidthsIfNeeded()
        {
            int targetWidth = GetTargetCardWidth();
            if (targetWidth == _lastAppliedCardWidth && _cardsPanel.Controls.Count == _lastAppliedControlCount)
                return;

            _lastAppliedCardWidth = targetWidth;
            _lastAppliedControlCount = _cardsPanel.Controls.Count;
            int innerWrap = Math.Max(180, targetWidth - 32);

            _cardsPanel.SuspendLayout();
            try
            {
                foreach (Control c in _cardsPanel.Controls)
                {
                    c.Width = targetWidth;
                    UpdateWrapControlsRecursive(c, innerWrap);
                }
            }
            finally
            {
                _cardsPanel.ResumeLayout(true);
            }
        }

        private static void UpdateWrapControlsRecursive(Control parent, int wrapWidth)
        {
            foreach (Control child in parent.Controls)
            {
                if (Equals(child.Tag, WrapTag))
                {
                    child.MaximumSize = new Size(wrapWidth, 0);
                }
                if (child.HasChildren)
                {
                    UpdateWrapControlsRecursive(child, Math.Max(140, wrapWidth - child.Padding.Horizontal));
                }
            }
        }

        private void DisposeAndClearCardsPanel()
        {
            _cardByItem.Clear();
            while (_cardsPanel.Controls.Count > 0)
            {
                Control ctrl = _cardsPanel.Controls[0];
                _cardsPanel.Controls.RemoveAt(0);
                ctrl.Dispose();
            }
            _lastAppliedControlCount = -1;
        }

        private TableLayoutPanel CreateStyledCardContainer(int cardWidth, Color topStripeColor, SpreadsheetCellIssue? selectableItem = null)
        {
            var card = new TableLayoutPanel
            {
                Width = cardWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(12, 10, 12, 10)
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.Paint += (s, e) =>
            {
                bool isSel = selectableItem != null && ReferenceEquals(selectableItem, _selectedItem);
                using (var stripe = new SolidBrush(topStripeColor))
                {
                    e.Graphics.FillRectangle(stripe, 0, 0, 4, card.Height);
                }
                using (var pen = new Pen(isSel ? topStripeColor : Color.FromArgb(218, 224, 235), isSel ? 1.6F : 1F))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };
            return card;
        }

        private Button CreateTabSegmentButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1),
                Padding = new Padding(2, 4, 2, 4),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private Button CreateActionButton(string text, Color back, Color fore, Color border, Font font)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Font = font,
                Padding = new Padding(4, 5, 4, 5),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderColor = border;
            return btn;
        }

        private Button CreateFilterPill(string text)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = _fontBrandStats,
                Margin = new Padding(1),
                Padding = new Padding(2, 3, 2, 3),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            return btn;
        }

        private Button CreatePillButton(string text, Color back, Color fore, Color border, Font font)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = font,
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(2),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderColor = border;
            return btn;
        }

        private Button CreateChipButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlatStyle = FlatStyle.Flat,
                Font = _fontSmall,
                Padding = new Padding(5, 2, 5, 2),
                Margin = new Padding(0, 0, 4, 4),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            return btn;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchDebounceTimer.Dispose();
                _toastTimer.Dispose();
                _toolTip.Dispose();

                _fontBrandTitle.Dispose();
                _fontBrandStats.Dispose();
                _fontTabActive.Dispose();
                _fontTabNormal.Dispose();
                _fontCardDiff.Dispose();
                _fontCardWord.Dispose();
                _fontBody.Dispose();
                _fontBodyBold.Dispose();
                _fontSuggest.Dispose();
                _fontSmall.Dispose();
                _fontSmallBold.Dispose();
                _fontInput.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
