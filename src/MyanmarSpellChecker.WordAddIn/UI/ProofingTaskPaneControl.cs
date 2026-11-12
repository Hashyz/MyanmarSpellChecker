using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.WordAddIn.Interop;

namespace MyanmarSpellChecker.WordAddIn.UI
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
    /// Enterprise High-DPI responsive Custom Task Pane UserControl for Microsoft Word.
    /// Uses 100% AutoSize rows, dynamic wrap widths, double-buffered scrolling, and native GDI Uniscribe/DirectWrite
    /// rendering (UseCompatibleTextRendering = false) so Pyidaungsu Myanmar script never clips at 100%–200% scaling.
    /// Provides a 3-Tab Workspace: (1) Proofing & Reasoning, (2) 84,500+ Dictionary & Proverbs, (3) Custom Words Manager.
    /// </summary>
    [ComVisible(true)]
    [Guid("B3A2C741-9D18-4E2A-8C55-1F7B8A9C2D33")]
    [ProgId("MyanmarSpellChecker.ProofingTaskPaneControl")]
    public sealed partial class ProofingTaskPaneControl : UserControl
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

        // Cached shared GDI fonts (disposed cleanly with UserControl to prevent GDI font handle leaks)
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
        private readonly Button _btnScanAll;
        private readonly Button _btnFixAll;
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
        private OfficialStructureAuditResult? _latestOfficialAudit;
        private NumberConversionMode _numberMode = NumberConversionMode.BankingKyatLakh;
        private string _numberInputSample = "1,500,000";
        private string _rewriterInputSample = "ဒီနေ့ အစည်းအဝေးမှာ လုပ်ငန်းတွေနဲ့ ပတ်သက်ပြီး ဆွေးနွေးနေပါတယ်။ ဒါပေမယ့် အခက်အခဲတွေ မရှိပါဘူးလို့ တင်ပြပါတယ်။";
        private DateTime _selectedCalendarDate = DateTime.Today;
        private bool _toolkitSelectionOnly;

        // Custom Words Toolbar Controls
        private readonly TextBox _txtNewCustomWord;
        private readonly Label _lblCustomSummary;

        // Main Scrollable Canvas
        private readonly BufferedFlowLayoutPanel _cardsPanel;

        // State
        private List<DocumentProofingItem> _allItems = new List<DocumentProofingItem>();
        private readonly Dictionary<DocumentProofingItem, Control> _cardByItem = new Dictionary<DocumentProofingItem, Control>();
        private DocumentProofingItem? _selectedItem;
        private int _stepperCursor = -1;
        private int _lastAppliedCardWidth = -1;
        private int _lastAppliedControlCount = -1;
        private bool _isScanning;
        private TaskPaneWorkspaceTab _activeTab = TaskPaneWorkspaceTab.Proofing;
        private IssueFilterMode _currentFilter = IssueFilterMode.All;
        private DictionaryCategoryFilter _dictCategoryFilter = DictionaryCategoryFilter.All;
        private ProofingStyleMode _currentStyleMode = ProofingStyleMode.GeneralStandard;

        // Events wired by ThisAddIn
        public event Action? RequestFullDocumentScan;
        public event Action<IssueFilterMode>? RequestFixAllIssues;
        public event Action? RequestClearUnderlines;
        public event Action? RequestLicenseDialog;
        public event Action<ProofingStyleMode>? RequestChangeStyleMode;
        public event Action<DocumentProofingItem>? RequestSelectIssueInDocument;
        public event Action<DocumentProofingItem, string>? RequestApplySuggestion;
        public event Action<DocumentProofingItem>? RequestIgnoreOnce;
        public event Action<DocumentProofingItem>? RequestAddToDictionary;
        public event Func<string, List<DictionaryEntry>>? RequestDictionarySearch;
        public event Func<string, string>? RequestHomophoneSummary;
        public event Action<string>? RequestInsertWordAtCursor;
        public event Func<IReadOnlyList<string>>? RequestGetCustomWords;
        public event Func<string, bool>? RequestAddCustomWordManual;
        public event Func<string, bool>? RequestRemoveCustomWord;

        // Flagship V11/V12/V16 Toolkit, Official Suite & Smart Knowledge Dictionary Events
        public event Action<string, string>? RequestApplyBatchSuggestion;
        public event Action<bool>? RequestRewriteToOfficialStyle;
        public event Func<string, List<string>>? RequestGetSynonyms;
        public event Func<string, List<string>>? RequestGetAntonyms;
        public event Func<string, bool, bool, List<DictionaryEntry>>? RequestSearchReferenceCorpus;
        public event Func<List<DictionaryEntry>>? RequestGetFeaturedKnowledge;
        public event Func<string>? RequestGetSelectedDocumentText;
        public event Action<bool>? RequestConvertLegacyFonts;
        public event Action<bool, bool>? RequestFormatNumerals;
        public event Action<bool>? RequestFormatPunctuation;
        public event Action<OfficialTemplateKind, DateTime>? RequestInsertOfficialTemplate;
        public event Func<DocumentQualityReport>? RequestRefreshQualityReport;
        public event Action? RequestExportAuditCertificate;
        public event Func<OfficialStructureAuditResult>? RequestValidateOfficialLetter;

        public ProofingTaskPaneControl()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
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

            // ================= COMPACT HEADER + 4-TAB WORKSPACE BAR =================
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
                Text = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း",
                Font = _fontBrandTitle,
                ForeColor = Color.FromArgb(18, 48, 102),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(6, 1, 4, 2)
            };

            _lblLexiconStats = new Label
            {
                Text = "သတ်ပုံကျမ်း + အဘိဓာန် • ထက်ရာဇာ (KU)",
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
                Color.FromArgb(240, 245, 255),
                Color.FromArgb(22, 74, 158),
                Color.FromArgb(196, 214, 245),
                _fontSmallBold);
            btnLic.Dock = DockStyle.None;
            btnLic.Anchor = AnchorStyles.Right;
            btnLic.Padding = new Padding(8, 3, 8, 3);
            btnLic.Margin = new Padding(4, 2, 0, 2);
            btnLic.Click += (s, e) => RequestLicenseDialog?.Invoke();
            _toolTip.SetToolTip(btnLic, "စနစ်အချက်အလက်၊ ဖန်တီးသူ (ထက်ရာဇာ — ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင်");

            brandRow.Controls.Add(titleStack, 0, 0);
            brandRow.Controls.Add(btnLic, 1, 0);

            // Row 1: 4-Tab Segmented Workspace Switcher (Proofing | Dictionary | Toolkit | Custom Words)
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
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            tabStrip.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnTabProofing = CreatePillButton("စစ်ဆေး (0)", Color.FromArgb(22, 76, 162), Color.White, Color.FromArgb(22, 76, 162), _fontTabActive);
            _btnTabProofing.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Proofing);

            _btnTabDictionary = CreatePillButton("အဘိဓာန်", Color.FromArgb(236, 240, 247), Color.FromArgb(55, 65, 82), Color.FromArgb(236, 240, 247), _fontTabNormal);
            _btnTabDictionary.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Dictionary);

            _btnTabToolkit = CreatePillButton("ကိရိယာ", Color.FromArgb(236, 240, 247), Color.FromArgb(55, 65, 82), Color.FromArgb(236, 240, 247), _fontTabNormal);
            _btnTabToolkit.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.Toolkit);

            _btnTabCustom = CreatePillButton("မှတ်သား", Color.FromArgb(236, 240, 247), Color.FromArgb(55, 65, 82), Color.FromArgb(236, 240, 247), _fontTabNormal);
            _btnTabCustom.Click += (s, e) => SwitchWorkspaceTab(TaskPaneWorkspaceTab.CustomWords);

            tabStrip.Controls.Add(_btnTabProofing, 0, 0);
            tabStrip.Controls.Add(_btnTabDictionary, 1, 0);
            tabStrip.Controls.Add(_btnTabToolkit, 2, 0);
            tabStrip.Controls.Add(_btnTabCustom, 3, 0);

            // Row 2: Toast Notification Banner (Hidden unless triggered)
            _pnlToast = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(228, 246, 234),
                Padding = new Padding(8, 5, 8, 5),
                Margin = new Padding(0, 0, 0, 6),
                Visible = false
            };
            _lblToast = new Label
            {
                Text = string.Empty,
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(18, 112, 50),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Tag = WrapTag
            };
            _pnlToast.Controls.Add(_lblToast);

            // ================= SUB-TOOLBAR A: PROOFING WORKSPACE =================
            _proofingToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 5,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _proofingToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int r = 0; r < 5; r++)
            {
                _proofingToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            // Style Mode Selector
            var styleGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 2)
            };
            styleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            styleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            styleGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnStyleGeneral = CreatePillButton("အထွေထွေ စံသတ်ပုံ", Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(24, 90, 188), _fontSmallBold);
            _btnStyleGeneral.Click += (s, e) => ChangeStyleMode(ProofingStyleMode.GeneralStandard);
            _toolTip.SetToolTip(_btnStyleGeneral, "အများသုံး/လုပ်ငန်းသုံး စံသတ်ပုံစနစ် (ပါတယ်၊ တွေ စသည့် အပြောစကားများကို ခွင့်ပြုသည်)");

            _btnStyleFormal = CreatePillButton("စာပေ / ရုံးသုံး", Color.FromArgb(240, 243, 249), Color.FromArgb(50, 60, 76), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnStyleFormal.Click += (s, e) => ChangeStyleMode(ProofingStyleMode.FormalOfficial);
            _toolTip.SetToolTip(_btnStyleFormal, "အစိုးရရုံးသုံးနှင့် စာပေအရေးအသားစနစ် (ပါတယ် ➔ ပါသည်၊ တွေ ➔ များ ပါ စစ်ဆေးသည်)");

            styleGrid.Controls.Add(_btnStyleGeneral, 0, 0);
            styleGrid.Controls.Add(_btnStyleFormal, 1, 0);

            _lblStyleHint = new Label
            {
                Text = "စံသတ်ပုံစနစ် — နေ့စဉ်နှင့် လုပ်ငန်းသုံး စာလုံးပေါင်းများကို စစ်ဆေးသည်",
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(95, 106, 124),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(2, 0, 2, 1)
            };

            // Primary Action Strip (Scan | Fix All | Clear Underlines)
            var actionStrip = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6)
            };
            actionStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37F));
            actionStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37F));
            actionStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            actionStrip.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnScanAll = CreatePillButton("စာတမ်းစစ်မည်", Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(20, 76, 162), _fontSmallBold);
            _btnScanAll.Click += (s, e) =>
            {
                SetScanningState(true);
                RequestFullDocumentScan?.Invoke();
            };
            _toolTip.SetToolTip(_btnScanAll, "စာကိုယ်၊ ဇယားကွက်နှင့် ခေါင်းစီး/အောက်ခြေ အားလုံးကို စစ်ဆေးရန်");

            _btnFixAll = CreatePillButton("အားလုံးပြင်မည်", Color.FromArgb(22, 132, 68), Color.White, Color.FromArgb(18, 114, 58), _fontSmallBold);
            _btnFixAll.Click += (s, e) => RequestFixAllIssues?.Invoke(_currentFilter);
            _toolTip.SetToolTip(_btnFixAll, "ရွေးချယ်ထားသော စစ်ထုတ်မှုအလိုက် အမှားများကို တစ်ပြိုင်နက် ပြင်ဆင်ရန် — အနက်သံသယ သတိပြုချက်များကို အလိုအလျောက် မပြင်ဘဲ ချန်ထားပေးသည် (Ctrl+Z တစ်ချက်တည်းဖြင့် ပြန်ပြင်နိုင်သည်)");

            var btnClearLines = CreatePillButton("မျဉ်းရှင်း", Color.FromArgb(242, 244, 248), Color.FromArgb(55, 65, 80), Color.FromArgb(215, 220, 230), _fontSmall);
            btnClearLines.Click += (s, e) => RequestClearUnderlines?.Invoke();
            _toolTip.SetToolTip(btnClearLines, "စာတမ်းပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းများကို ဖယ်ရှားရန်");

            actionStrip.Controls.Add(_btnScanAll, 0, 0);
            actionStrip.Controls.Add(_btnFixAll, 1, 0);
            actionStrip.Controls.Add(btnClearLines, 2, 0);

            // Filter Pills + Issue Stepper (All | Spelling | Grammar | Context | ↑ | ↓)
            var filterAndStepRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 6,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            filterAndStepRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21F));
            filterAndStepRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21F));
            filterAndStepRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            filterAndStepRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            filterAndStepRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            filterAndStepRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 9F));
            filterAndStepRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _btnFilterAll = CreatePillButton("အားလုံး (0)", Color.FromArgb(30, 65, 130), Color.White, Color.FromArgb(30, 65, 130), _fontSmall);
            _btnFilterAll.Click += (s, e) => SetFilterMode(IssueFilterMode.All);

            _btnFilterSpelling = CreatePillButton("သတ်ပုံ (0)", Color.FromArgb(253, 237, 236), Color.FromArgb(168, 28, 28), Color.FromArgb(242, 202, 200), _fontSmall);
            _btnFilterSpelling.Click += (s, e) => SetFilterMode(IssueFilterMode.SpellingOnly);

            _btnFilterGrammar = CreatePillButton("သဒ္ဒါ (0)", Color.FromArgb(234, 243, 255), Color.FromArgb(22, 78, 168), Color.FromArgb(196, 218, 248), _fontSmall);
            _btnFilterGrammar.Click += (s, e) => SetFilterMode(IssueFilterMode.GrammarOnly);

            _btnFilterContext = CreatePillButton("အနက် (0)", Color.FromArgb(255, 246, 226), Color.FromArgb(168, 98, 6), Color.FromArgb(245, 214, 152), _fontSmall);
            _btnFilterContext.Click += (s, e) => SetFilterMode(IssueFilterMode.ContextOnly);
            _toolTip.SetToolTip(_btnFilterContext, "အနက်သံသယ စိစစ်ရန် (ဝါကျအနက်နှင့် တိုက်ဆိုင်စစ်ဆေးရမည့် စကားစပ်သတိပြုချက်များ)");

            _btnPrevIssue = CreatePillButton("↑", Color.FromArgb(242, 244, 248), Color.FromArgb(45, 55, 72), Color.FromArgb(215, 220, 230), _fontSmallBold);
            _btnPrevIssue.Click += (s, e) => StepThroughIssues(-1);
            _toolTip.SetToolTip(_btnPrevIssue, "ရှေ့အမှားသို့ သွားရန်");

            _btnNextIssue = CreatePillButton("↓", Color.FromArgb(242, 244, 248), Color.FromArgb(45, 55, 72), Color.FromArgb(215, 220, 230), _fontSmallBold);
            _btnNextIssue.Click += (s, e) => StepThroughIssues(+1);
            _toolTip.SetToolTip(_btnNextIssue, "နောက်အမှားသို့ သွားရန်");

            filterAndStepRow.Controls.Add(_btnFilterAll, 0, 0);
            filterAndStepRow.Controls.Add(_btnFilterSpelling, 1, 0);
            filterAndStepRow.Controls.Add(_btnFilterGrammar, 2, 0);
            filterAndStepRow.Controls.Add(_btnFilterContext, 3, 0);
            filterAndStepRow.Controls.Add(_btnPrevIssue, 4, 0);
            filterAndStepRow.Controls.Add(_btnNextIssue, 5, 0);

            _lblSummary = new Label
            {
                Text = "အသင့်ရှိနေပါသည် — စာရိုက်သည်နှင့် အလိုအလျောက် စစ်ဆေးပေးပါမည်",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(22, 120, 58),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(2, 2, 2, 1)
            };

            _proofingToolbar.Controls.Add(styleGrid, 0, 0);
            _proofingToolbar.Controls.Add(_lblStyleHint, 0, 1);
            _proofingToolbar.Controls.Add(actionStrip, 0, 2);
            _proofingToolbar.Controls.Add(filterAndStepRow, 0, 3);
            _proofingToolbar.Controls.Add(_lblSummary, 0, 4);

            // ================= SUB-TOOLBAR B: DICTIONARY & PROVERBS EXPLORER =================
            _dictionaryToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false
            };
            _dictionaryToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _dictionaryToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var searchInputGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5)
            };
            searchInputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            searchInputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
            searchInputGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            searchInputGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtDictionarySearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontInput,
                Margin = new Padding(2, 3, 4, 3)
            };
            _txtDictionarySearch.HandleCreated += (s, e) =>
            {
                SendMessage(_txtDictionarySearch.Handle, EM_SETCUEBANNER, (IntPtr)1, "စာလုံးပေါင်း၊ ပညာရပ်/နိုင်ငံ (EN/MM)၊ စကားပုံ ရှာပါ...");
            };
            _txtDictionarySearch.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };

            var btnLookupSelection = CreatePillButton("စာတမ်းမှယူ", Color.FromArgb(234, 243, 255), Color.FromArgb(20, 74, 158), Color.FromArgb(190, 212, 246), _fontSmall);
            _toolTip.SetToolTip(btnLookupSelection, "စာတမ်းထဲ၌ ရွေးချယ်ထားသော (Highlight) ဝေါဟာရကို အဘိဓာန်၌ ချက်ချင်းရှာရန်");
            btnLookupSelection.Click += (s, e) =>
            {
                string selText = (RequestGetSelectedDocumentText?.Invoke() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(selText))
                {
                    ShowToastBanner("စာတမ်းထဲ၌ ရှာဖွေလိုသော ဝေါဟာရကို အရင် ရွေးချယ် (Highlight) ပေးပါ။", isSuccess: false);
                    return;
                }
                OpenDictionaryWithQuery(selText);
            };

            var btnClearSearch = CreatePillButton("ရှင်းမည်", Color.FromArgb(242, 244, 248), Color.FromArgb(50, 60, 76), Color.FromArgb(215, 220, 230), _fontSmall);
            btnClearSearch.Click += (s, e) =>
            {
                _txtDictionarySearch.Text = string.Empty;
                ExecuteDictionarySearchNow();
                _txtDictionarySearch.Focus();
            };

            searchInputGrid.Controls.Add(_txtDictionarySearch, 0, 0);
            searchInputGrid.Controls.Add(btnLookupSelection, 1, 0);
            searchInputGrid.Controls.Add(btnClearSearch, 2, 0);

            var dictCatRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };

            _btnDictCatAll = CreatePillButton("အားလုံး", Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(24, 90, 188), _fontSmall);
            _btnDictCatAll.Dock = DockStyle.None;
            _btnDictCatAll.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.All);

            _btnDictCatThatpon = CreatePillButton("သတ်ပုံကျမ်း", Color.FromArgb(240, 243, 249), Color.FromArgb(45, 55, 72), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnDictCatThatpon.Dock = DockStyle.None;
            _btnDictCatThatpon.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.OfficialThatpon);

            _btnDictCatAbidan = CreatePillButton("အဘိဓာန်", Color.FromArgb(240, 243, 249), Color.FromArgb(45, 55, 72), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnDictCatAbidan.Dock = DockStyle.None;
            _btnDictCatAbidan.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.AbidanMeaning);

            _btnDictCatEncyc = CreatePillButton("စွယ်စုံဗဟုသုတ", Color.FromArgb(240, 243, 249), Color.FromArgb(45, 55, 72), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnDictCatEncyc.Dock = DockStyle.None;
            _btnDictCatEncyc.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.EncyclopediaKnowledge);

            _btnDictCatDomain = CreatePillButton("ပညာရပ်", Color.FromArgb(240, 243, 249), Color.FromArgb(45, 55, 72), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnDictCatDomain.Dock = DockStyle.None;
            _btnDictCatDomain.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.SpecializedDomains);

            _btnDictCatPropn = CreatePillButton("နိုင်ငံ/မြို့", Color.FromArgb(240, 243, 249), Color.FromArgb(45, 55, 72), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnDictCatPropn.Dock = DockStyle.None;
            _btnDictCatPropn.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.ProperNounsAndPlaces);

            _btnDictCatProverb = CreatePillButton("စကားပုံ", Color.FromArgb(240, 243, 249), Color.FromArgb(45, 55, 72), Color.FromArgb(214, 220, 232), _fontSmall);
            _btnDictCatProverb.Dock = DockStyle.None;
            _btnDictCatProverb.Click += (s, e) => SetDictionaryCategory(DictionaryCategoryFilter.ProverbsAndSayings);

            dictCatRow.Controls.Add(_btnDictCatAll);
            dictCatRow.Controls.Add(_btnDictCatThatpon);
            dictCatRow.Controls.Add(_btnDictCatAbidan);
            dictCatRow.Controls.Add(_btnDictCatEncyc);
            dictCatRow.Controls.Add(_btnDictCatDomain);
            dictCatRow.Controls.Add(_btnDictCatPropn);
            dictCatRow.Controls.Add(_btnDictCatProverb);

            _lblDictSummary = new Label
            {
                Text = "သတ်ပုံကျမ်း၊ ပညာရပ် (၅) ရပ်၊ နိုင်ငံ/မြို့နှင့် စကားပုံများ အသင့်ရှာဖွေနိုင်သည်",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(24, 82, 172),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(2, 2, 2, 1)
            };

            _dictionaryToolbar.Controls.Add(searchInputGrid, 0, 0);
            _dictionaryToolbar.Controls.Add(dictCatRow, 0, 1);
            _dictionaryToolbar.Controls.Add(_lblDictSummary, 0, 2);

            // ================= SUB-TOOLBAR B2: FLAGSHIP DOCUMENT TOOLKIT & OFFICIAL SUITE =================
            _toolkitToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false
            };
            _toolkitToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _toolkitToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _lblToolkitSummary = new Label
            {
                Text = "ဖောင့်ပြောင်း၊ ဂဏန်း/ကျပ်ငွေ၊ မြန်မာသက္ကရာဇ်၊ ရုံးစာ နှင့် အရည်အသွေး စစ်ဆေးချက်",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(20, 62, 138),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(2, 2, 2, 1)
            };
            _toolkitToolbar.Controls.Add(_lblToolkitSummary, 0, 0);

            // ================= SUB-TOOLBAR C: CUSTOM DICTIONARY MANAGER =================
            _customToolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Visible = false
            };
            _customToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _customToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _customToolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var addCustomGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            addCustomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
            addCustomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            addCustomGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtNewCustomWord = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontInput,
                Margin = new Padding(2, 3, 5, 3)
            };
            _txtNewCustomWord.HandleCreated += (s, e) =>
            {
                SendMessage(_txtNewCustomWord.Handle, EM_SETCUEBANNER, (IntPtr)1, "ကိုယ်ပိုင်ဝေါဟာရ အသစ်ရိုက်ထည့်ပါ...");
            };
            _txtNewCustomWord.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddManualCustomWordFromInput();
                }
            };

            var btnAddCustom = CreatePillButton("+ မှတ်သားမည်", Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(20, 76, 162), _fontSmallBold);
            btnAddCustom.Click += (s, e) => AddManualCustomWordFromInput();

            addCustomGrid.Controls.Add(_txtNewCustomWord, 0, 0);
            addCustomGrid.Controls.Add(btnAddCustom, 1, 0);

            _lblCustomSummary = new Label
            {
                Text = "ကိုယ်ပိုင် / ရုံးသုံး မှတ်သားဝေါဟာရများ စာရင်း",
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(35, 65, 118),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(2, 2, 2, 1)
            };

            _customToolbar.Controls.Add(addCustomGrid, 0, 0);
            _customToolbar.Controls.Add(_lblCustomSummary, 0, 1);

            headerShell.Controls.Add(brandRow, 0, 0);
            headerShell.Controls.Add(tabStrip, 0, 1);
            headerShell.Controls.Add(_pnlToast, 0, 2);
            headerShell.Controls.Add(_proofingToolbar, 0, 3);
            headerShell.Controls.Add(_dictionaryToolbar, 0, 4);
            headerShell.Controls.Add(_toolkitToolbar, 0, 5);
            headerShell.Controls.Add(_customToolbar, 0, 6);

            // ================= DOUBLE-BUFFERED SCROLLABLE CARDS CANVAS =================
            _cardsPanel = new BufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(10, 8, 10, 12),
                BackColor = Color.FromArgb(243, 245, 249)
            };
            _cardsPanel.SizeChanged += (s, e) => ResizeCardsToFitPanel();

            rootLayout.Controls.Add(headerShell, 0, 0);
            rootLayout.Controls.Add(_cardsPanel, 0, 1);

            Controls.Add(rootLayout);
            UpdateWorkspaceTabStyles();
            UpdateStyleModeButtons();
            UpdateFilterButtonStyles();
            UpdateDictionaryCategoryStyles();
            RefreshProofingCardsFull();
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

        // ================= PUBLIC API FOR THISADDIN =================

        public void SetStyleMode(ProofingStyleMode mode)
        {
            _currentStyleMode = mode;
            UpdateStyleModeButtons();
            if (_activeTab == TaskPaneWorkspaceTab.Proofing && GetActiveFilteredItems().Count == 0)
            {
                RefreshProofingCardsFull();
            }
        }

        public void SetLexiconStats(int wordCount, int ruleCount)
        {
            _lblLexiconStats.Text = $"သတ်ပုံကျမ်း ဝေါဟာရ {wordCount:N0} • စည်းမျဉ်း {ruleCount:N0}+ (Offline)";
        }

        public void SetLexiconStats(int totalWordCount, int officialThatponCount, int ruleCount, int referenceCount = 0, int encyclopediaCount = 0)
        {
            if (encyclopediaCount > 0)
            {
                _lblLexiconStats.Text = $"သတ်ပုံကျမ်း {officialThatponCount:N0} • အဘိဓာန် {totalWordCount:N0} • ဗဟုသုတ {referenceCount:N0} • ထက်ရာဇာ (KU)";
            }
            else
            {
                _lblLexiconStats.Text = referenceCount > 0
                    ? $"သတ်ပုံကျမ်း {officialThatponCount:N0} • စုစုပေါင်း {totalWordCount:N0} • စကားပုံ {referenceCount:N0} • ထက်ရာဇာ (KU)"
                    : $"သတ်ပုံကျမ်းဝင် {officialThatponCount:N0} • စုစုပေါင်း {totalWordCount:N0} ပုဒ် • ထက်ရာဇာ (KU)";
            }
        }

        public void FocusDictionarySearchBox()
        {
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.Dictionary);
            _txtDictionarySearch.Focus();
            _txtDictionarySearch.SelectAll();
        }

        public void OpenDictionaryWithQuery(string query)
        {
            string cleaned = (query ?? string.Empty).Trim();
            _dictCategoryFilter = DictionaryCategoryFilter.All;
            UpdateDictionaryCategoryStyles();
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.Dictionary);
            _searchDebounceTimer.Stop();
            _txtDictionarySearch.Text = cleaned;
            _searchDebounceTimer.Stop();
            ExecuteDictionarySearchNow();
        }

        public void OpenCustomWordsTab()
        {
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.CustomWords);
            _txtNewCustomWord.Focus();
        }

        public void OpenToolkitTab()
        {
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.Toolkit);
        }

        public bool IsDictionaryTabActive => _activeTab == TaskPaneWorkspaceTab.Dictionary;

        public void RefreshActiveTabAfterEngineReady()
        {
            UpdateWorkspaceTabStyles();
            if (_activeTab == TaskPaneWorkspaceTab.Dictionary)
            {
                ExecuteDictionarySearchNow();
            }
            else if (_activeTab == TaskPaneWorkspaceTab.CustomWords)
            {
                RenderCustomWordsView();
            }
            else if (_activeTab == TaskPaneWorkspaceTab.Toolkit)
            {
                _latestQualityReport = null;
                RenderToolkitWorkspaceView();
            }
            else
            {
                RefreshProofingCardsFull();
            }
        }

        public void UpdateQualityReport(DocumentQualityReport? report)
        {
            _latestQualityReport = report;
            if (report != null && report.OfficialLetterAudit != null)
            {
                _latestOfficialAudit = report.OfficialLetterAudit;
            }
            if (_activeTab == TaskPaneWorkspaceTab.Toolkit)
            {
                RenderToolkitWorkspaceView();
            }
        }

        public void SetEngineWarmupState()
        {
            _lblLexiconStats.Text = "သတ်ပုံကျမ်းနှင့် စွယ်စုံအဘိဓာန် အသင့်ဖွင့်လှစ်နေပါသည်... (Offline)";
            if (_activeTab == TaskPaneWorkspaceTab.Proofing)
            {
                _lblSummary.Text = "⟳ Offline အဘိဓာန် အင်ဂျင်ကို စတင်ချိတ်ဆက်နေပါသည်...";
                _lblSummary.ForeColor = Color.FromArgb(24, 88, 182);
            }
        }

        public void SetScanningState(bool isScanning)
        {
            _isScanning = isScanning;
            if (_isScanning)
            {
                _lblSummary.Text = "⟳ စာတမ်းတစ်ခုလုံးကို အသေးစိတ် စစ်ဆေးနေပါသည်...";
                _lblSummary.ForeColor = Color.FromArgb(24, 88, 182);
            }
        }

        public void ShowToastBanner(string message, bool isSuccess = true)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _pnlToast.BackColor = isSuccess ? Color.FromArgb(226, 246, 233) : Color.FromArgb(254, 243, 224);
            _lblToast.ForeColor = isSuccess ? Color.FromArgb(18, 114, 50) : Color.FromArgb(158, 82, 0);
            _lblToast.MaximumSize = new Size(Math.Max(200, Width - 38), 0);
            _lblToast.Text = message;
            _pnlToast.Visible = true;

            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private bool _hasRenderedProofingOnce;

        private static string BuildIssuesUiSignature(IReadOnlyList<DocumentProofingItem> activeItems)
        {
            if (activeItems.Count == 0)
                return "EMPTY";

            var sb = new System.Text.StringBuilder(activeItems.Count * 24);
            sb.Append(activeItems.Count);
            int cap = Math.Min(activeItems.Count, 80);
            for (int i = 0; i < cap; i++)
            {
                var it = activeItems[i];
                sb.Append('|').Append(it.CachedStart).Append(':').Append(it.Issue.OriginalText).Append(':').Append((int)it.Issue.IssueType);
            }
            return sb.ToString();
        }

        public void RenderIssues(IReadOnlyList<DocumentProofingItem> items)
        {
            bool wasScanning = _isScanning;
            _isScanning = false;

            var prevActive = _allItems.Where(i => !i.IsResolved).ToList();
            var nextList = items != null
                ? items.Where(i => !i.IsResolved).ToList()
                : new List<DocumentProofingItem>();

            bool unchanged = _hasRenderedProofingOnce &&
                             !wasScanning &&
                             _activeTab == TaskPaneWorkspaceTab.Proofing &&
                             _cardsPanel.Controls.Count > 0 &&
                             string.Equals(BuildIssuesUiSignature(prevActive), BuildIssuesUiSignature(nextList), StringComparison.Ordinal);

            _allItems = nextList;
            UpdateWorkspaceTabStyles();
            UpdateFilterButtonStyles();

            if (unchanged)
            {
                UpdateProofingSummaryText(GetActiveFilteredItems().Count);
                return;
            }

            if (wasScanning || prevActive.Count == 0 || nextList.Count == 0)
            {
                ResetProofingPagination();
            }
            if (_activeTab == TaskPaneWorkspaceTab.Proofing)
            {
                _hasRenderedProofingOnce = true;
                RefreshProofingCardsFull();
            }
        }

        // ================= WORKSPACE TAB & TOOLBAR STATE =================

        private void SwitchWorkspaceTab(TaskPaneWorkspaceTab tab)
        {
            if (_activeTab == tab && _cardsPanel.Controls.Count > 0)
            {
                UpdateWorkspaceTabStyles();
                return;
            }

            _activeTab = tab;
            _proofingToolbar.Visible = tab == TaskPaneWorkspaceTab.Proofing;
            _dictionaryToolbar.Visible = tab == TaskPaneWorkspaceTab.Dictionary;
            _toolkitToolbar.Visible = tab == TaskPaneWorkspaceTab.Toolkit;
            _customToolbar.Visible = tab == TaskPaneWorkspaceTab.CustomWords;

            UpdateWorkspaceTabStyles();

            switch (tab)
            {
                case TaskPaneWorkspaceTab.Proofing:
                    RefreshProofingCardsFull();
                    break;
                case TaskPaneWorkspaceTab.Dictionary:
                    ExecuteDictionarySearchNow();
                    break;
                case TaskPaneWorkspaceTab.Toolkit:
                    RenderToolkitWorkspaceView();
                    break;
                case TaskPaneWorkspaceTab.CustomWords:
                    RenderCustomWordsView();
                    break;
            }
        }

        private void UpdateWorkspaceTabStyles()
        {
            int unresolvedCount = _allItems.Count(i => !i.IsResolved);
            _btnTabProofing.Text = $"စစ်ဆေး ({unresolvedCount})";

            int customCount = RequestGetCustomWords?.Invoke()?.Count ?? 0;
            _btnTabCustom.Text = customCount > 0 ? $"မှတ်သား ({customCount})" : "မှတ်သား";

            ApplySegmentTabStyle(_btnTabProofing, _activeTab == TaskPaneWorkspaceTab.Proofing);
            ApplySegmentTabStyle(_btnTabDictionary, _activeTab == TaskPaneWorkspaceTab.Dictionary);
            ApplySegmentTabStyle(_btnTabToolkit, _activeTab == TaskPaneWorkspaceTab.Toolkit);
            ApplySegmentTabStyle(_btnTabCustom, _activeTab == TaskPaneWorkspaceTab.CustomWords);
        }

        private void ApplySegmentTabStyle(Button btn, bool active)
        {
            btn.BackColor = active ? Color.FromArgb(22, 76, 162) : Color.FromArgb(236, 240, 247);
            btn.ForeColor = active ? Color.White : Color.FromArgb(52, 62, 78);
            btn.Font = active ? _fontTabActive : _fontTabNormal;
            btn.FlatAppearance.BorderColor = active ? Color.FromArgb(22, 76, 162) : Color.FromArgb(236, 240, 247);
        }

        // ================= SHARED STYLING & HIGH-DPI RESIZE HELPERS =================

        private TableLayoutPanel CreateStyledCardContainer(int cardWidth, Color leftAccentColor, DocumentProofingItem? isSelectableItem)
        {
            var card = new TableLayoutPanel
            {
                Width = cardWidth,
                MinimumSize = new Size(cardWidth, 0),
                MaximumSize = new Size(cardWidth, 0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(14, 10, 12, 10)
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            card.Paint += (s, e) =>
            {
                bool isSelected = isSelectableItem != null && ReferenceEquals(_selectedItem, isSelectableItem);

                using (var accentBrush = new SolidBrush(leftAccentColor))
                {
                    e.Graphics.FillRectangle(accentBrush, 0, 0, 4, card.Height);
                }

                Color frameColor = isSelected ? Color.FromArgb(24, 90, 188) : Color.FromArgb(218, 224, 235);
                float penWidth = isSelected ? 2F : 1F;
                using (var borderPen = new Pen(frameColor, penWidth))
                {
                    int offset = isSelected ? 1 : 0;
                    e.Graphics.DrawRectangle(borderPen, offset, offset, card.Width - 1 - (offset * 2), card.Height - 1 - (offset * 2));
                }
            };

            return card;
        }

        private static Button CreatePillButton(string text, Color backColor, Color foreColor, Color borderColor, Font font)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = font,
                Margin = new Padding(2, 2, 2, 2),
                Padding = new Padding(3, 4, 3, 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = borderColor;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        private int GetTargetCardWidth()
        {
            int w = _cardsPanel.ClientSize.Width - 24;
            return Math.Max(250, w);
        }

        private void ResizeCardsToFitPanel()
        {
            int targetWidth = GetTargetCardWidth();
            int ctrlCount = _cardsPanel.Controls.Count;
            if (targetWidth == _lastAppliedCardWidth && ctrlCount == _lastAppliedControlCount)
                return;

            _lastAppliedCardWidth = targetWidth;
            _lastAppliedControlCount = ctrlCount;
            int innerWrap = Math.Max(180, targetWidth - 32);

            _cardsPanel.SuspendLayout();
            foreach (Control ctrl in _cardsPanel.Controls)
            {
                ctrl.MinimumSize = new Size(targetWidth, 0);
                ctrl.MaximumSize = new Size(targetWidth, 0);
                ctrl.Width = targetWidth;
                UpdateDynamicWrapControls(ctrl, innerWrap);
            }
            _cardsPanel.ResumeLayout();
        }

        private static void UpdateDynamicWrapControls(Control parent, int maxInnerWidth)
        {
            foreach (Control child in parent.Controls)
            {
                if (Equals(child.Tag, WrapTag) || child is Button)
                {
                    if (Equals(child.Tag, WrapTag))
                    {
                        child.MaximumSize = new Size(maxInnerWidth, 0);
                        if (child is WordWrapChoiceButton)
                        {
                            child.PerformLayout();
                        }
                    }
                    else if (child is Button btn && btn.MaximumSize.Width > 0)
                    {
                        btn.MaximumSize = new Size(maxInnerWidth, 0);
                        if (btn is WordWrapChoiceButton)
                        {
                            btn.PerformLayout();
                        }
                    }
                }

                if (child.HasChildren)
                {
                    UpdateDynamicWrapControls(child, maxInnerWidth);
                }
            }
        }

        private void DisposeAndClearCardsPanel()
        {
            _lastAppliedCardWidth = -1;
            _lastAppliedControlCount = -1;
            while (_cardsPanel.Controls.Count > 0)
            {
                Control c = _cardsPanel.Controls[_cardsPanel.Controls.Count - 1];
                _cardsPanel.Controls.RemoveAt(_cardsPanel.Controls.Count - 1);
                c.Dispose();
            }
        }

        private void OnToastTimerTick(object? sender, EventArgs e)
        {
            _toastTimer.Stop();
            if (!_pnlToast.IsDisposed)
            {
                _pnlToast.Visible = false;
            }
        }
    }
}
