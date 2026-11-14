using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.ExcelAddIn.Interop;

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
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void BeginCardsBatchUpdate()
        {
            if (_cardsPanel.IsHandleCreated)
            {
                try { SendMessage(_cardsPanel.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero); } catch { }
            }
            _cardsPanel.SuspendLayout();
        }

        private void EndCardsBatchUpdate()
        {
            _cardsPanel.ResumeLayout(true);
            if (_cardsPanel.IsHandleCreated)
            {
                try
                {
                    SendMessage(_cardsPanel.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                    _cardsPanel.Invalidate(true);
                }
                catch { }
            }
        }

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
        private readonly Label _lblBrandTitle;
        private readonly Label _lblLexiconStats;
        private readonly Button _btnLangToggle;
        private readonly Button _btnEcoMode;
        private readonly Button _btnAboutLic;
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
        private readonly Button _btnClearUnderlines;
        private readonly Button _btnFilterAll;
        private readonly Button _btnFilterSpelling;
        private readonly Button _btnFilterGrammar;
        private readonly Button _btnFilterContext;
        private readonly Button _btnPrevIssue;
        private readonly Button _btnNextIssue;
        private readonly TableLayoutPanel _pnlScanProgress;
        private readonly Label _lblScanProgress;
        private readonly Button _btnStopScan;
        private readonly Label _lblSummary;
        private readonly Panel _pnlToast;
        private readonly Label _lblToast;

        // Dictionary Toolbar Controls
        private readonly TextBox _txtDictionarySearch;
        private readonly Button _btnClearDictSearch;
        private readonly Button _btnDictBack;
        private readonly Button _btnDictForward;
        private readonly Button _btnDictSaved;
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
        private readonly Button _btnAddCustomWord;
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
        private int _lastAppliedEffectiveDpi;
        private bool _isScanning;
        private ExcelScanOutcomeMetadata? _lastScanOutcome;
        private int _undoAvailableCount;
        private string _undoLatestLabel = string.Empty;
        private bool _hasLoadedLexiconStats;
        private int _cachedWordCount;
        private int _cachedOfficialCount;
        private int _cachedRuleCount;
        private int _cachedReferenceCount;
        private int _cachedEncycCount;
        private EcoModePreference _lastEcoPref = EcoModePreference.Auto;
        private bool _lastEcoActive;
        private TaskPaneWorkspaceTab _activeTab = TaskPaneWorkspaceTab.Proofing;
        private IssueFilterMode _currentFilter = IssueFilterMode.All;
        private DictionaryCategoryFilter _dictCategoryFilter = DictionaryCategoryFilter.All;
        private readonly List<string> _dictHistory = new List<string>();
        private int _dictHistoryIndex = -1;
        private bool _suppressDictHistoryPush;
        private bool _showingSavedBookmarksView;
        private ProofingStyleMode _currentStyleMode = ProofingStyleMode.GeneralStandard;
        private SpreadsheetScanScope _currentScanScope = SpreadsheetScanScope.ActiveSheet;

        // Events wired by ExcelThisAddIn
        public event Action<SpreadsheetScanScope>? RequestScanScope;
        public event Action? RequestCancelScan;
        public event Action? RequestToggleLanguage;
        public event Action? RequestCycleEcoMode;
        public event Action<IssueFilterMode>? RequestFixAllIssues;
        public event Action? RequestUndoLastAction;
        public event Action? RequestClearUnderlines;
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
        public event Func<bool>? RequestIsDictionaryIndexReady;
        public event Func<string, List<string>>? RequestGetSynonyms;
        public event Func<string, List<string>>? RequestGetAntonyms;
        public event Func<string, List<string>>? RequestGetCollocations;
        public event Func<IEnumerable<string>, List<DictionaryEntry>>? RequestGetBookmarkedEntries;
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
            Size = new Size(430, 720);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(243, 245, 249);
            Font = _fontBody;
            Dock = DockStyle.Fill;

            _searchDebounceTimer = new Timer { Interval = 250 };
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

            // Row 0: Responsive 2-Tier Brand Header + Wrapping Quick-Action Pills
            // (Prevents narrow-pane single-character vertical text wrapping on startup or manual resize)
            var brandRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(0)
            };
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            brandRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            brandRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            brandRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblBrandTitle = new Label
            {
                Text = UiLocalizer.Pick("Myanmar Spell Checker (Excel)", "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Excel)"),
                Font = _fontBrandTitle,
                ForeColor = Color.FromArgb(16, 92, 50),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = MeasureSingleLineLabelHeight(_fontBrandTitle, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                Padding = new Padding(2, 0, 2, 0)
            };

            _lblLexiconStats = new Label
            {
                Text = UiLocalizer.Pick("Formula-Safe Excel Suite • Htet Yar Zar (KU)", "Formula-Safe Excel Suite • ထက်ရာဇာ (KU)"),
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(88, 100, 118),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = MeasureSingleLineLabelHeight(_fontBrandStats, 5),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 3),
                Padding = new Padding(2, 0, 2, 0)
            };

            // Deterministic 1-row 3-column grid (eliminates WinForms FlowLayoutPanel-in-TableLayoutPanel wrapped-height phantom gap!)
            var brandPillsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            brandPillsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29F));
            brandPillsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43F));
            brandPillsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            brandPillsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int initBrandPillH = MeasureSingleLineButtonHeight(_fontSmallBold, 10);
            _btnLangToggle = CreateActionButton(
                UiLocalizer.FormatLanguageBadge(),
                Color.FromArgb(234, 243, 255),
                Color.FromArgb(18, 72, 162),
                Color.FromArgb(182, 206, 245),
                _fontSmallBold);
            _btnLangToggle.Height = initBrandPillH;
            _btnLangToggle.Margin = new Padding(0, 1, 3, 1);
            _btnLangToggle.Click += (s, e) => RequestToggleLanguage?.Invoke();

            _btnEcoMode = CreateActionButton(
                EcoPerformanceGovernor.FormatBadgeLabel(EcoModePreference.Auto, false),
                Color.FromArgb(236, 248, 241),
                Color.FromArgb(16, 104, 54),
                Color.FromArgb(178, 218, 194),
                _fontSmallBold);
            _btnEcoMode.Height = initBrandPillH;
            _btnEcoMode.Margin = new Padding(1, 1, 3, 1);
            _btnEcoMode.Click += (s, e) => RequestCycleEcoMode?.Invoke();

            _btnAboutLic = CreateActionButton(
                UiLocalizer.Pick("ⓘ About", "အချက်အလက် ⓘ"),
                Color.FromArgb(236, 248, 241),
                Color.FromArgb(16, 104, 54),
                Color.FromArgb(178, 218, 194),
                _fontSmallBold);
            _btnAboutLic.Height = initBrandPillH;
            _btnAboutLic.Margin = new Padding(1, 1, 0, 1);
            _btnAboutLic.Click += (s, e) => RequestLicenseDialog?.Invoke();

            brandPillsGrid.Controls.Add(_btnLangToggle, 0, 0);
            brandPillsGrid.Controls.Add(_btnEcoMode, 1, 0);
            brandPillsGrid.Controls.Add(_btnAboutLic, 2, 0);

            brandRow.Controls.Add(_lblBrandTitle, 0, 0);
            brandRow.Controls.Add(_lblLexiconStats, 0, 1);
            brandRow.Controls.Add(brandPillsGrid, 0, 2);

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
                RowCount = 7,
                Margin = new Padding(0)
            };
            _proofingToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 7; i++)
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
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = MeasureSingleLineLabelHeight(_fontBrandStats, 5),
                TextAlign = ContentAlignment.MiddleLeft,
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

            // Primary Action Buttons Row: Scan | Fix All | Undo | Clear Underlines
            var actionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            actionRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnScanNow = CreateActionButton("Sheet စစ်မည်", Color.FromArgb(16, 124, 65), Color.White, Color.FromArgb(14, 102, 53), _fontSmallBold);
            _btnScanNow.Margin = new Padding(0, 0, 2, 0);
            _btnScanNow.Click += (s, e) => RequestScanScope?.Invoke(_currentScanScope);

            _btnFixAll = CreateActionButton("အားလုံးပြင်", Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(18, 72, 158), _fontSmallBold);
            _btnFixAll.Margin = new Padding(2, 0, 2, 0);
            _btnFixAll.Click += (s, e) => RequestFixAllIssues?.Invoke(_currentFilter);

            _btnUndoLast = CreateActionButton("↶ ပြန်ဆုတ်", Color.FromArgb(244, 246, 250), Color.FromArgb(55, 65, 82), Color.FromArgb(210, 218, 230), _fontSmallBold);
            _btnUndoLast.Margin = new Padding(2, 0, 2, 0);
            _btnUndoLast.Enabled = false;
            _btnUndoLast.Click += (s, e) => RequestUndoLastAction?.Invoke();

            _btnClearUnderlines = CreateActionButton("မျဉ်းရှင်း", Color.FromArgb(244, 246, 250), Color.FromArgb(55, 65, 82), Color.FromArgb(210, 218, 230), _fontSmall);
            _btnClearUnderlines.Margin = new Padding(2, 0, 0, 0);
            _btnClearUnderlines.Click += (s, e) => RequestClearUnderlines?.Invoke();
            _toolTip.SetToolTip(_btnClearUnderlines, "ဖန်သားပြင်ပေါ်ရှိ စစ်ဆေးရေး အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်းများကို ဖျောက်ရန် (Cell စာသားအရောင်နှင့် .xlsx ဖိုင်ကို လုံးဝ မပြောင်းလဲပါ)");

            actionRow.Controls.Add(_btnScanNow, 0, 0);
            actionRow.Controls.Add(_btnFixAll, 1, 0);
            actionRow.Controls.Add(_btnUndoLast, 2, 0);
            actionRow.Controls.Add(_btnClearUnderlines, 3, 0);

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
            _btnPrevIssue.Padding = new Padding(1, 0, 1, 0);
            _btnNextIssue.Padding = new Padding(1, 0, 1, 0);

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

            _pnlScanProgress = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(232, 245, 238),
                Padding = new Padding(7, 4, 6, 4),
                Margin = new Padding(0, 0, 0, 4),
                Visible = false
            };
            _pnlScanProgress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _pnlScanProgress.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pnlScanProgress.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblScanProgress = new Label
            {
                Text = "⏳ စစ်ဆေးနေသည်...",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(16, 98, 52),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Width = 260,
                MinimumSize = new Size(0, MeasureSingleLineLabelHeight(_fontSmallBold, 6)),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 1, 6, 1)
            };

            _btnStopScan = CreatePillButton(
                "■ ရပ်မည်",
                Color.FromArgb(254, 235, 235),
                Color.FromArgb(178, 28, 28),
                Color.FromArgb(238, 182, 182),
                _fontSmallBold);
            _btnStopScan.Dock = DockStyle.None;
            _btnStopScan.Anchor = AnchorStyles.Right;
            _btnStopScan.Padding = new Padding(8, 2, 8, 2);
            _btnStopScan.Margin = new Padding(0);
            _btnStopScan.Click += (s, e) => RequestCancelScan?.Invoke();
            _toolTip.SetToolTip(_btnStopScan, "လက်ရှိ Sheet/Workbook စစ်ဆေးနေမှုကို ချက်ချင်းရပ်တန့်ရန် (စက်မလေးစေရန်)");

            _pnlScanProgress.Controls.Add(_lblScanProgress, 0, 0);
            _pnlScanProgress.Controls.Add(_btnStopScan, 1, 0);

            _lblSummary = new Label
            {
                Text = "Sheet စစ်မည် ကိုနှိပ်၍ မြန်မာစာ အကွက်များကို Formula မပျက်စေဘဲ စစ်ဆေးပါ",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(16, 98, 52),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = MeasureSingleLineLabelHeight(_fontSmallBold, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(1, 0, 1, 0)
            };

            _proofingToolbar.Controls.Add(styleRow, 0, 0);
            _proofingToolbar.Controls.Add(_lblStyleHint, 0, 1);
            _proofingToolbar.Controls.Add(scopeRow, 0, 2);
            _proofingToolbar.Controls.Add(actionRow, 0, 3);
            _proofingToolbar.Controls.Add(filterRow, 0, 4);
            _proofingToolbar.Controls.Add(_pnlScanProgress, 0, 5);
            _proofingToolbar.Controls.Add(_lblSummary, 0, 6);

            // ================= TAB 2 TOOLBAR: DICTIONARY & ENCYCLOPEDIA =================
            _dictionaryToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Visible = false,
                Margin = new Padding(0)
            };
            _dictionaryToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            _txtDictionarySearch.HandleCreated += (s, e) => UpdateCueBanners();
            _txtDictionarySearch.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };

            _btnClearDictSearch = CreateActionButton(
                UiLocalizer.Pick("Clear", "ရှင်း"),
                Color.FromArgb(242, 244, 248), Color.FromArgb(65, 75, 92), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnClearDictSearch.Height = MeasureSingleLineButtonHeight(_fontSmall, 11);
            _btnClearDictSearch.Click += (s, e) =>
            {
                _showingSavedBookmarksView = false;
                _txtDictionarySearch.Clear();
                ExecuteDictionarySearchNow();
                _txtDictionarySearch.Focus();
            };
            searchBoxGrid.Controls.Add(_txtDictionarySearch, 0, 0);
            searchBoxGrid.Controls.Add(_btnClearDictSearch, 1, 0);

            // History Navigation & Shared Bookmarks Bar (← Back | Next → | ★ Saved (N))
            var dictNavGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            dictNavGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
            dictNavGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
            dictNavGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
            dictNavGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnDictBack = CreateChipButton(UiLocalizer.Pick("← Back", "← ရှေ့"));
            _btnDictBack.BackColor = Color.FromArgb(242, 244, 248);
            _btnDictBack.ForeColor = Color.FromArgb(148, 156, 168);
            _btnDictBack.FlatAppearance.BorderColor = Color.FromArgb(215, 220, 230);
            _btnDictBack.Enabled = false;
            _btnDictBack.Click += (s, e) => NavigateDictionaryHistory(-1);

            _btnDictForward = CreateChipButton(UiLocalizer.Pick("Next →", "နောက် →"));
            _btnDictForward.BackColor = Color.FromArgb(242, 244, 248);
            _btnDictForward.ForeColor = Color.FromArgb(148, 156, 168);
            _btnDictForward.FlatAppearance.BorderColor = Color.FromArgb(215, 220, 230);
            _btnDictForward.Enabled = false;
            _btnDictForward.Click += (s, e) => NavigateDictionaryHistory(+1);

            _btnDictSaved = CreateChipButton(UiLocalizer.Pick("★ Saved (0)", "★ မှတ်သား (0)"));
            _btnDictSaved.Font = _fontSmallBold;
            _btnDictSaved.BackColor = Color.FromArgb(255, 248, 230);
            _btnDictSaved.ForeColor = Color.FromArgb(148, 86, 8);
            _btnDictSaved.FlatAppearance.BorderColor = Color.FromArgb(242, 214, 152);
            _btnDictSaved.Click += (s, e) => ToggleSavedBookmarksView();

            dictNavGrid.Controls.Add(_btnDictBack, 0, 0);
            dictNavGrid.Controls.Add(_btnDictForward, 1, 0);
            dictNavGrid.Controls.Add(_btnDictSaved, 2, 0);

            // Deterministic 2-row Dictionary Category Grid (eliminates FlowLayoutPanel-in-TableLayoutPanel phantom height!)
            var dictCatGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            dictCatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            dictCatGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            dictCatGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var dictCatRowTop = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 2),
                Padding = new Padding(0)
            };
            dictCatRowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19F));
            dictCatRowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            dictCatRowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            dictCatRowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29F));
            dictCatRowTop.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var dictCatRowBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            dictCatRowBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            dictCatRowBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            dictCatRowBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            dictCatRowBottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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

            dictCatRowTop.Controls.Add(_btnDictCatAll, 0, 0);
            dictCatRowTop.Controls.Add(_btnDictCatThatpon, 1, 0);
            dictCatRowTop.Controls.Add(_btnDictCatAbidan, 2, 0);
            dictCatRowTop.Controls.Add(_btnDictCatEncyc, 3, 0);

            dictCatRowBottom.Controls.Add(_btnDictCatDomain, 0, 0);
            dictCatRowBottom.Controls.Add(_btnDictCatPropn, 1, 0);
            dictCatRowBottom.Controls.Add(_btnDictCatProverb, 2, 0);

            dictCatGrid.Controls.Add(dictCatRowTop, 0, 0);
            dictCatGrid.Controls.Add(dictCatRowBottom, 0, 1);

            int initSubSummaryH = MeasureSingleLineLabelHeight(_fontSmallBold, 5);
            _lblDictSummary = new Label
            {
                Text = "မြန်မာ-မြန်မာ အဘိဓာန်ကြီး၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၀+) နှင့် စကားပုံများ ရှာဖွေနိုင်သည်",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(24, 82, 172),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = initSubSummaryH,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _dictionaryToolbar.Controls.Add(searchBoxGrid, 0, 0);
            _dictionaryToolbar.Controls.Add(dictNavGrid, 0, 1);
            _dictionaryToolbar.Controls.Add(dictCatGrid, 0, 2);
            _dictionaryToolbar.Controls.Add(_lblDictSummary, 0, 3);

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
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = initSubSummaryH,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(1, 0, 1, 0)
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
            _txtNewCustomWord.HandleCreated += (s, e) => UpdateCueBanners();
            _txtNewCustomWord.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddNewCustomWordFromInput();
                }
            };

            _btnAddCustomWord = CreateActionButton(
                UiLocalizer.Pick("+ Add", "+ ထည့်မည်"),
                Color.FromArgb(16, 124, 65), Color.White, Color.FromArgb(14, 102, 53), _fontSmallBold);
            _btnAddCustomWord.Click += (s, e) => AddNewCustomWordFromInput();
            addWordRow.Controls.Add(_txtNewCustomWord, 0, 0);
            addWordRow.Controls.Add(_btnAddCustomWord, 1, 0);

            _lblCustomSummary = new Label
            {
                Text = "Word နှင့် Excel နှစ်မျိုးလုံးတွင် တူညီစွာ မျှဝေမှတ်သားထားသော စာလုံးများ",
                Font = _fontSmall,
                ForeColor = Color.FromArgb(72, 84, 102),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Top,
                Width = 360,
                Height = initSubSummaryH,
                TextAlign = ContentAlignment.MiddleLeft
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
                UseMnemonic = false,
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
            _cardsPanel.ControlAdded += (s, e) =>
            {
                if (e.Control != null)
                {
                    UiFontCache.DisableMnemonicsRecursive(e.Control);
                }
            };
            _cardsPanel.SizeChanged += (s, e) => ReflowCardWidthsIfNeeded();

            rootLayout.Controls.Add(headerShell, 0, 0);
            rootLayout.Controls.Add(_cardsPanel, 0, 1);
            Controls.Add(rootLayout);

            UiFontCache.DisableMnemonicsRecursive(this);
            ApplyDpiLayoutMetrics();
            ApplyStaticChromeLabelsAndTooltips();
            UpdateCueBanners();
            SetEcoModeState(EcoPerformanceGovernor.CurrentPreference, EcoPerformanceGovernor.Hardware.IsLowSpecHardware);
            SetUndoState(0, string.Empty);
            UpdateTabButtonStyles();
            UpdateStyleModeButtons();
            UpdateScopeButtons();
            UpdateFilterPillStyles();
            UpdateDictionaryCategoryStyles();
            UpdateDictionaryNavButtons();
            RenderProofingCards();
        }

        private void UpdateCueBanners()
        {
            if (_txtDictionarySearch.IsHandleCreated)
            {
                SendMessage(
                    _txtDictionarySearch.Handle,
                    EM_SETCUEBANNER,
                    (IntPtr)1,
                    UiLocalizer.Pick("Search English, Myanmar, Romanized (mingalaba), Zawgyi...", "အင်္ဂလိပ်/မြန်မာ၊ Romanized (mingalaba)၊ ဇော်ဂျီ ရိုက်ရှာပါ..."));
            }

            if (_txtNewCustomWord.IsHandleCreated)
            {
                SendMessage(
                    _txtNewCustomWord.Handle,
                    EM_SETCUEBANNER,
                    (IntPtr)1,
                    UiLocalizer.Pick("Enter custom word to save...", "မှတ်သားလိုသော စာလုံးသစ်..."));
            }
        }

        private void ApplyStaticChromeLabelsAndTooltips()
        {
            _lblBrandTitle.Text = UiLocalizer.Pick(
                "Myanmar Spell Checker (Excel)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Excel)");

            if (_hasLoadedLexiconStats)
            {
                int total = _cachedWordCount + _cachedReferenceCount;
                _lblLexiconStats.Text = UiLocalizer.Pick(
                    $"Total {total:N0} entries • {_cachedEncycCount:N0} Knowledge • Htet Yar Zar (KU)",
                    $"စုစုပေါင်း {total:N0} ပုဒ် • ဗဟုသုတ {_cachedEncycCount:N0} • ထက်ရာဇာ (KU)");
            }
            else
            {
                _lblLexiconStats.Text = UiLocalizer.Pick(
                    "Formula-Safe Excel Suite • Htet Yar Zar (KU)",
                    "Formula-Safe Excel Suite • ထက်ရာဇာ (KU)");
            }

            string authorTip = UiLocalizer.Pick(
                "Creator: Htet Yar Zar (ထက်ရာဇာ) • M.Sc. Part II Candidate in Geology, Department of Geology, Kyaukse University (Kyaukse, Myanmar)",
                "ဖန်တီးသူ - ထက်ရာဇာ (Htet Yar Zar) • M.Sc. Part II Candidate in Geology, ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ် (Kyaukse, Myanmar)");
            _toolTip.SetToolTip(_lblBrandTitle, authorTip);
            _toolTip.SetToolTip(_lblLexiconStats, authorTip);

            _btnLangToggle.Text = UiLocalizer.FormatLanguageBadge();
            _toolTip.SetToolTip(_btnLangToggle, UiLocalizer.FormatLanguageTooltip());

            _btnAboutLic.Text = UiLocalizer.Pick("About ⓘ", "အချက်အလက် ⓘ");
            _toolTip.SetToolTip(
                _btnAboutLic,
                UiLocalizer.Pick(
                    "System Info, Creator Attribution (Htet Yar Zar — Kyaukse University) & GNU GPL v3.0 Open-Source License",
                    "စနစ်အချက်အလက်၊ ဖန်တီးသူ (ထက်ရာဇာ — ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင်"));

            _btnTabProofing.Text = UiLocalizer.Pick("Proofing", "စာစစ်");
            _btnTabDictionary.Text = UiLocalizer.Pick("Dictionary", "အဘိဓာန်");
            _btnTabToolkit.Text = UiLocalizer.Pick("Toolkit", "ကိရိယာ");
            _btnTabCustom.Text = UiLocalizer.Pick("Custom", "မှတ်သားစာ");

            _btnStyleGeneral.Text = UiLocalizer.Pick("General Standard", "အထွေထွေ စံသတ်ပုံ");
            _btnStyleFormal.Text = UiLocalizer.Pick("Formal / Official", "ရုံးသုံး / စာပေဟန်");

            _btnScopeSheet.Text = UiLocalizer.Pick("Active Sheet", "လက်ရှိ Sheet");
            _btnScopeSelection.Text = UiLocalizer.Pick("Selection", "ရွေးထားကွက်");
            _btnScopeWorkbook.Text = UiLocalizer.Pick("Workbook", "Workbook လုံး");

            _btnClearUnderlines.Text = UiLocalizer.Pick("Clear Marks", "မျဉ်းရှင်း");
            _toolTip.SetToolTip(
                _btnClearUnderlines,
                UiLocalizer.Pick(
                    "Hide screen-only red/blue/amber wavy underlines (cell text color & saved .xlsx files are never modified)",
                    "ဖန်သားပြင်ပေါ်ရှိ စစ်ဆေးရေး အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်းများကို ဖျောက်ရန် (Cell စာသားအရောင်နှင့် .xlsx ဖိုင်ကို လုံးဝ မပြောင်းလဲပါ)"));

            _btnStopScan.Text = UiLocalizer.Pick("■ Stop", "■ ရပ်မည်");
            _toolTip.SetToolTip(
                _btnStopScan,
                UiLocalizer.Pick(
                    "Stop the current sheet/workbook scan immediately",
                    "လက်ရှိ Sheet/Workbook စစ်ဆေးနေမှုကို ချက်ချင်းရပ်တန့်ရန် (စက်မလေးစေရန်)"));

            _btnClearDictSearch.Text = UiLocalizer.Pick("Clear", "ရှင်း");
            UpdateDictionaryNavButtons();
            _btnDictCatAll.Text = UiLocalizer.Pick("All", "အားလုံး");
            _btnDictCatThatpon.Text = UiLocalizer.Pick("Orthography", "သတ်ပုံကျမ်း");
            _btnDictCatAbidan.Text = UiLocalizer.Pick("Lexicon", "အဘိဓာန်");
            _btnDictCatEncyc.Text = UiLocalizer.Pick("Encyclopedia", "စွယ်စုံဗဟုသုတ");
            _btnDictCatDomain.Text = UiLocalizer.Pick("Domains", "ပညာရပ်");
            _btnDictCatPropn.Text = UiLocalizer.Pick("Places/Names", "မြို့ရွာ/နိုင်ငံ");
            _btnDictCatProverb.Text = UiLocalizer.Pick("Proverbs", "စကားပုံ");

            _lblToolkitSummary.Text = UiLocalizer.Pick(
                "✓ 100% Formula-Safe: Excel formulas (=SUM, =IF, =VLOOKUP) and numeric cells are automatically protected",
                "✓ Formula (=SUM, =IF, =VLOOKUP) နှင့် ကိန်းဂဏန်းတွက်ချက်မှုများကို 100% အလိုအလျောက် ကာကွယ်ထားသည်");

            _btnAddCustomWord.Text = UiLocalizer.Pick("+ Add", "+ ထည့်မည်");
            _lblCustomSummary.Text = UiLocalizer.Pick(
                "Custom words shared automatically across both Microsoft Word and Excel",
                "Word နှင့် Excel နှစ်မျိုးလုံးတွင် တူညီစွာ မျှဝေမှတ်သားထားသော စာလုံးများ");
        }

        public void ApplyLocalization()
        {
            SuspendLayout();
            try
            {
                ApplyStaticChromeLabelsAndTooltips();
                UpdateCueBanners();
                SetEcoModeState(_lastEcoPref, _lastEcoActive);
                SetUndoState(_undoAvailableCount, _undoLatestLabel);
                UpdateTabButtonStyles();
                UpdateStyleModeButtons();
                UpdateScopeButtons();
                UpdateFilterPillStyles();
                UpdateDictionaryCategoryStyles();
            }
            finally
            {
                ResumeLayout(true);
            }

            switch (_activeTab)
            {
                case TaskPaneWorkspaceTab.Proofing:
                    RenderProofingCards();
                    break;
                case TaskPaneWorkspaceTab.Dictionary:
                    UpdateDictionaryNavButtons();
                    if (_showingSavedBookmarksView)
                        RenderSavedBookmarksView();
                    else
                        ExecuteDictionarySearchNow();
                    break;
                case TaskPaneWorkspaceTab.Toolkit:
                    RenderToolkitWorkspace();
                    break;
                case TaskPaneWorkspaceTab.CustomWords:
                    RenderCustomWordsWorkspace();
                    break;
            }
        }

        public void SetEngineWarmupState()
        {
            _lblLexiconStats.Text = UiLocalizer.Pick(
                "Loading offline dictionary & orthography...",
                "အဘိဓာန်နှင့် သတ်ပုံကျမ်း ဖွင့်လှစ်နေသည်...");
            _lblSummary.Text = UiLocalizer.Pick(
                "⏳ Preparing 118,600+ offline lexicon entries in the background...",
                "⏳ နောက်ခံ၌ ဝေါဟာရ (၁၁၈,၆၀၀+) ကို အသင့်ပြင်ဆင်နေပါသည်...");
        }

        public void SetLexiconStats(int wordCount, int officialCount, int ruleCount, int referenceCount, int encycCount)
        {
            _hasLoadedLexiconStats = true;
            _cachedWordCount = wordCount;
            _cachedOfficialCount = officialCount;
            _cachedRuleCount = ruleCount;
            _cachedReferenceCount = referenceCount;
            _cachedEncycCount = encycCount;

            int total = wordCount + referenceCount;
            _lblLexiconStats.Text = UiLocalizer.Pick(
                $"Total {total:N0} entries • {encycCount:N0} Knowledge • Htet Yar Zar (KU)",
                $"စုစုပေါင်း {total:N0} ပုဒ် • ဗဟုသုတ {encycCount:N0} • ထက်ရာဇာ (KU)");
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
            _btnUndoLast.Text = _undoAvailableCount > 0
                ? UiLocalizer.Pick($"↶ Undo ({_undoAvailableCount})", $"↶ ပြန်ဆုတ် ({_undoAvailableCount})")
                : UiLocalizer.Pick("↶ Undo", "↶ ပြန်ဆုတ်");
            _btnUndoLast.BackColor = _undoAvailableCount > 0
                ? Color.FromArgb(255, 248, 229)
                : Color.FromArgb(244, 246, 250);
            _btnUndoLast.ForeColor = _undoAvailableCount > 0
                ? Color.FromArgb(148, 86, 8)
                : Color.FromArgb(120, 128, 140);
            _toolTip.SetToolTip(
                _btnUndoLast,
                _undoAvailableCount > 0
                    ? UiLocalizer.Pick(
                        $"Revert last spreadsheet edit: {_undoLatestLabel}",
                        $"နောက်ဆုံးပြင်ဆင်ချက်ကို မူလအတိုင်း ပြန်ဆုတ်ရန်: {_undoLatestLabel}")
                    : UiLocalizer.Pick(
                        "No undo history available yet",
                        "ပြန်ဆုတ်ရန် မှတ်တမ်း မရှိသေးပါ"));
        }

        public void SetEcoModeState(EcoModePreference preference, bool currentlyActive)
        {
            _lastEcoPref = preference;
            _lastEcoActive = currentlyActive;
            _btnEcoMode.Text = EcoPerformanceGovernor.FormatBadgeLabel(preference, currentlyActive);
            if (currentlyActive)
            {
                _btnEcoMode.BackColor = Color.FromArgb(226, 247, 234);
                _btnEcoMode.ForeColor = Color.FromArgb(16, 112, 52);
                _btnEcoMode.FlatAppearance.BorderColor = Color.FromArgb(164, 220, 186);
            }
            else if (preference == EcoModePreference.Off)
            {
                _btnEcoMode.BackColor = Color.FromArgb(244, 245, 248);
                _btnEcoMode.ForeColor = Color.FromArgb(98, 108, 124);
                _btnEcoMode.FlatAppearance.BorderColor = Color.FromArgb(214, 220, 230);
            }
            else
            {
                _btnEcoMode.BackColor = Color.FromArgb(236, 248, 241);
                _btnEcoMode.ForeColor = Color.FromArgb(16, 104, 54);
                _btnEcoMode.FlatAppearance.BorderColor = Color.FromArgb(178, 218, 194);
            }
            _toolTip.SetToolTip(_btnEcoMode, EcoPerformanceGovernor.FormatTooltip(preference, currentlyActive));
        }

        public void UpdateScanProgress(int percent, string message)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            _isScanning = true;
            _lblScanProgress.Text = string.IsNullOrWhiteSpace(message)
                ? UiLocalizer.Pick($"⏳ Scanning... ({clamped}%)", $"⏳ စစ်ဆေးနေသည်... ({clamped}%)")
                : $"{message} ({clamped}%)";
            _pnlScanProgress.Visible = true;
        }

        public void SetScanOutcomeMetadata(ExcelScanOutcomeMetadata? metadata)
        {
            _lastScanOutcome = metadata;
            if (!_isScanning && _activeTab == TaskPaneWorkspaceTab.Proofing)
            {
                UpdateExcelProofingSummaryText(GetFilteredUnresolvedIssues());
            }
        }

        public void SetScanningState(bool scanning)
        {
            _isScanning = scanning;
            if (scanning)
            {
                _lblScanProgress.Text = UiLocalizer.Pick(
                    "⏳ Scanning spreadsheet cells...",
                    "⏳ Excel အကွက်များကို စတင်စစ်ဆေးနေပါသည်...");
                _pnlScanProgress.Visible = true;
                _lblSummary.Text = UiLocalizer.Pick(
                    "⏳ Scanning spreadsheet cells...",
                    "⏳ Excel အကွက်များကို အမြန်စစ်ဆေးနေပါသည်...");
                _lblSummary.ForeColor = Color.FromArgb(24, 90, 188);
            }
            else
            {
                _pnlScanProgress.Visible = false;
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
            _showingSavedBookmarksView = false;
            _dictCategoryFilter = DictionaryCategoryFilter.All;
            UpdateDictionaryCategoryStyles();
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.Dictionary);
            if (!string.IsNullOrWhiteSpace(query))
            {
                _searchDebounceTimer.Stop();
                _txtDictionarySearch.Text = query!.Trim();
                _searchDebounceTimer.Stop();
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
                    UpdateDictionaryNavButtons();
                    if (_showingSavedBookmarksView)
                    {
                        RenderSavedBookmarksView();
                        break;
                    }
                    if (string.IsNullOrWhiteSpace(_txtDictionarySearch.Text))
                    {
                        string sel = RequestGetSelectedCellText?.Invoke() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(sel) &&
                            (SpreadsheetProofingHelper.ContainsMyanmarScript(sel) || sel.Any(char.IsLetter)) &&
                            sel.Length <= 36 &&
                            sel.IndexOf('\n') < 0)
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

        private int GetTargetCardWidth(bool reserveScrollbarForMultipleCards = false)
        {
            int clientW = _cardsPanel.ClientSize.Width;
            if (clientW <= 80)
            {
                clientW = Width > 80 ? Width - 22 : 390;
            }
            else if (reserveScrollbarForMultipleCards && !_cardsPanel.VerticalScroll.Visible)
            {
                clientW -= SystemInformation.VerticalScrollBarWidth;
            }

            return Math.Max(180, clientW - _cardsPanel.Padding.Horizontal - 6);
        }

        private void ReflowCardWidthsIfNeeded()
        {
            int currentDpi = UiFontCache.GetEffectiveDpi(this);
            if (currentDpi != _lastAppliedEffectiveDpi)
            {
                ApplyDpiLayoutMetrics();
                _lastAppliedCardWidth = -1;
            }

            int targetWidth = GetTargetCardWidth();
            int ctrlCount = _cardsPanel.Controls.Count;
            if (Math.Abs(targetWidth - _lastAppliedCardWidth) <= 1 && ctrlCount == _lastAppliedControlCount)
                return;

            _lastAppliedCardWidth = targetWidth;
            _lastAppliedControlCount = ctrlCount;
            int innerWrap = Math.Max(180, targetWidth - 32);

            _cardsPanel.SuspendLayout();
            try
            {
                foreach (Control c in _cardsPanel.Controls)
                {
                    c.SuspendLayout();
                    try
                    {
                        UpdateWrapControlsRecursive(c, innerWrap);
                        if (c.MinimumSize.Width != targetWidth)
                            c.MinimumSize = new Size(targetWidth, 0);
                        if (c.MaximumSize.Width != targetWidth)
                            c.MaximumSize = new Size(targetWidth, 0);
                        if (c.Width != targetWidth)
                            c.Width = targetWidth;
                    }
                    finally
                    {
                        c.ResumeLayout(false);
                    }
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
                if (Equals(child.Tag, WrapTag) && child.MaximumSize.Width != wrapWidth)
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
            _lastAppliedCardWidth = -1;
            _lastAppliedControlCount = -1;
            int count = _cardsPanel.Controls.Count;
            if (count == 0)
                return;

            var snapshot = new Control[count];
            _cardsPanel.Controls.CopyTo(snapshot, 0);
            _cardsPanel.Controls.Clear();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i]?.Dispose();
            }
        }

        private FastCardContainer CreateStyledCardContainer(int cardWidth, Color topStripeColor, SpreadsheetCellIssue? selectableItem = null)
        {
            var card = new FastCardContainer
            {
                Width = cardWidth,
                MinimumSize = new Size(cardWidth, 0),
                MaximumSize = new Size(cardWidth, 0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(12, 10, 12, 10)
            };
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

        private int MeasureSingleLineLabelHeight(Font font, int extraLogicalPad = 6) =>
            UiFontCache.MeasureSingleLineLabelHeight(this, font, extraLogicalPad);

        private int MeasureSingleLineButtonHeight(Font font, int extraLogicalPad = 12) =>
            UiFontCache.MeasureSingleLineButtonHeight(this, font, extraLogicalPad);

        public bool NeedsDpiRefresh() => UiFontCache.GetEffectiveDpi(this) != _lastAppliedEffectiveDpi;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (NeedsDpiRefresh())
            {
                ApplyDpiLayoutMetrics();
                _lastAppliedCardWidth = -1;
            }
            ReflowCardWidthsIfNeeded();
        }

        public void RefreshDpiMetricsAndLayout()
        {
            if (NeedsDpiRefresh())
            {
                ApplyDpiLayoutMetrics();
                _lastAppliedCardWidth = -1;
            }
            ReflowCardWidthsIfNeeded();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ApplyDpiLayoutMetrics();
            _lastAppliedCardWidth = -1;
            ReflowCardWidthsIfNeeded();
            PerformLayout();
        }

        private void ApplyDpiLayoutMetrics()
        {
            if (_lblBrandTitle == null)
                return;

            _lastAppliedEffectiveDpi = UiFontCache.GetEffectiveDpi(this);
            SuspendLayout();
            try
            {
                _lblBrandTitle.Height = MeasureSingleLineLabelHeight(_fontBrandTitle, 6);
                _lblLexiconStats.Height = MeasureSingleLineLabelHeight(_fontBrandStats, 5);

                int brandPillH = MeasureSingleLineButtonHeight(_fontSmallBold, 10);
                _btnLangToggle.Height = brandPillH;
                _btnEcoMode.Height = brandPillH;
                _btnAboutLic.Height = brandPillH;

                int tabBtnH = MeasureSingleLineButtonHeight(_fontTabActive, 12);
                _btnTabProofing.Height = tabBtnH;
                _btnTabDictionary.Height = tabBtnH;
                _btnTabToolkit.Height = tabBtnH;
                _btnTabCustom.Height = tabBtnH;

                int segBtnH = MeasureSingleLineButtonHeight(_fontSmallBold, 11);
                _btnStyleGeneral.Height = segBtnH;
                _btnStyleFormal.Height = segBtnH;
                _lblStyleHint.Height = MeasureSingleLineLabelHeight(_fontBrandStats, 5);
                _btnScopeSheet.Height = segBtnH;
                _btnScopeSelection.Height = segBtnH;
                _btnScopeWorkbook.Height = segBtnH;

                int actionBtnH = MeasureSingleLineButtonHeight(_fontSmallBold, 13);
                _btnScanNow.Height = actionBtnH;
                _btnFixAll.Height = actionBtnH;
                _btnUndoLast.Height = actionBtnH;
                _btnClearUnderlines.Height = actionBtnH;

                int filterBtnH = MeasureSingleLineButtonHeight(_fontBrandStats, 10);
                _btnFilterAll.Height = filterBtnH;
                _btnFilterSpelling.Height = filterBtnH;
                _btnFilterGrammar.Height = filterBtnH;
                _btnFilterContext.Height = filterBtnH;
                _btnPrevIssue.Height = filterBtnH;
                _btnNextIssue.Height = filterBtnH;

                _lblScanProgress.MinimumSize = new Size(0, MeasureSingleLineLabelHeight(_fontSmallBold, 6));
                _lblSummary.Height = MeasureSingleLineLabelHeight(_fontSmallBold, 6);

                int auxBtnH = MeasureSingleLineButtonHeight(_fontSmall, 11);
                _btnClearDictSearch.Height = auxBtnH;
                _btnAddCustomWord.Height = auxBtnH;

                int chipBtnH = MeasureSingleLineButtonHeight(_fontSmall, 10);
                _btnDictBack.Height = chipBtnH;
                _btnDictForward.Height = chipBtnH;
                _btnDictSaved.Height = chipBtnH;
                _btnDictCatAll.Height = chipBtnH;
                _btnDictCatThatpon.Height = chipBtnH;
                _btnDictCatAbidan.Height = chipBtnH;
                _btnDictCatEncyc.Height = chipBtnH;
                _btnDictCatDomain.Height = chipBtnH;
                _btnDictCatPropn.Height = chipBtnH;
                _btnDictCatProverb.Height = chipBtnH;

                int subSummaryH = MeasureSingleLineLabelHeight(_fontSmallBold, 5);
                _lblDictSummary.Height = subSummaryH;
                _lblToolkitSummary.Height = subSummaryH;
                _lblCustomSummary.Height = subSummaryH;
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private Button CreateTabSegmentButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Height = MeasureSingleLineButtonHeight(_fontTabActive, 12),
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(1),
                Padding = new Padding(2, 0, 2, 0),
                TextAlign = ContentAlignment.MiddleCenter,
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
                AutoSize = false,
                AutoEllipsis = true,
                Height = MeasureSingleLineButtonHeight(font, 13),
                Dock = DockStyle.Fill,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Font = font,
                Padding = new Padding(3, 0, 3, 0),
                TextAlign = ContentAlignment.MiddleCenter,
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
                AutoSize = false,
                AutoEllipsis = true,
                Height = MeasureSingleLineButtonHeight(_fontBrandStats, 10),
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = _fontBrandStats,
                Margin = new Padding(1),
                Padding = new Padding(2, 0, 2, 0),
                TextAlign = ContentAlignment.MiddleCenter,
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
                AutoEllipsis = true,
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
                AutoSize = false,
                AutoEllipsis = true,
                Height = MeasureSingleLineButtonHeight(_fontSmall, 10),
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Font = _fontSmall,
                Padding = new Padding(2, 0, 2, 0),
                Margin = new Padding(2, 1, 2, 1),
                TextAlign = ContentAlignment.MiddleCenter,
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
