using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Presentation;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.Core.Typography;

namespace MyanmarSpellChecker.PowerPointAddIn.UI
{
    public sealed partial class PowerPointProofingTaskPaneControl
    {
        private const int DefaultExcelProofingCardBatchSize = 30;
        private const int EcoExcelProofingCardBatchSize = 20;
        private const int MaxMountedExcelCards = 90; // Hard Win32 HWND cap (max 3 batches mounted at once)
        private int _visibleWindowStart;
        private int _visibleCardLimit = DefaultExcelProofingCardBatchSize;
        private bool _hasRenderedProofingOnce;
        private bool _isDockedChecklistMode;
        private readonly Dictionary<PresentationSlideIssue, (bool Checked, string Chosen)> _dockedStateByItem =
            new Dictionary<PresentationSlideIssue, (bool Checked, string Chosen)>();
        private readonly HashSet<PresentationSlideIssue> _expandedDockedItems =
            new HashSet<PresentationSlideIssue>();

        public bool IsDockedChecklistMode => _isDockedChecklistMode;

        private void ToggleExcelSlimTaskPaneChrome()
        {
            _isSlimHeader = !_isSlimHeader;
            var prefs = MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Load("powerpoint");
            prefs.SlimTaskPaneHeader = _isSlimHeader;
            prefs.HasSlimHeaderPreference = true;
            MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Save("powerpoint", prefs);
            UpdateExcelSlimButtonChrome();
            SyncExcelCompactChromeAndDockedVisibility();
        }

        private void UpdateExcelSlimButtonChrome()
        {
            if (_btnSlimHeader == null)
                return;
            _btnSlimHeader.Text = _isSlimHeader
                ? UiLocalizer.Pick("▾ Full", "▾ အပြည့်")
                : UiLocalizer.Pick("▴ Slim", "▴ ကျစ်လျစ်");
            _btnSlimHeader.BackColor = _isSlimHeader
                ? Color.FromArgb(224, 244, 232)
                : Color.FromArgb(244, 248, 246);
            _toolTip.SetToolTip(
                _btnSlimHeader,
                UiLocalizer.Pick(
                    "Toggle compact laptop layout (hides secondary hint rows so more cards fit on screen)",
                    "လက်ပ်တော့ မျက်နှာပြင်ငယ်များအတွက် ကျစ်လျစ်သောမြင်ကွင်း ပြောင်းရန်"));
        }

        private void SyncExcelCompactChromeAndDockedVisibility()
        {
            if (_proofingToolbar == null)
                return;

            _proofingToolbar.SuspendLayout();
            try
            {
                _lblBrandTitle.Visible = false;
                _lblLexiconStats.Visible = !_isSlimHeader && !_isDockedChecklistMode;
                _styleRow.Visible = !_isDockedChecklistMode;
                _lblStyleHint.Visible = !_isSlimHeader && !_isDockedChecklistMode;
                _scopeRow.Visible = !_isDockedChecklistMode;
                _actionRow.Visible = !_isDockedChecklistMode;
                _summaryAndReportRow.Visible = !_isDockedChecklistMode;
                _pnlStickyDockedBar.Visible = _isDockedChecklistMode;
            }
            finally
            {
                _proofingToolbar.ResumeLayout(true);
            }
            PerformLayout();
        }

        public void EnterDockedChecklistMode(IReadOnlyList<FixPreviewEntry<PresentationSlideIssue>>? seedEntries = null)
        {
            _isDockedChecklistMode = true;
            if (seedEntries != null)
            {
                foreach (var entry in seedEntries)
                {
                    _dockedStateByItem[entry.Payload] = (entry.IsChecked, entry.ChosenReplacement);
                }
            }
            SwitchWorkspaceTab(TaskPaneWorkspaceTab.Proofing);
            SyncExcelCompactChromeAndDockedVisibility();
            _visibleWindowStart = 0;
            _visibleCardLimit = ExcelProofingCardBatchSize;
            RenderProofingCards();
        }

        public void ExitDockedChecklistMode()
        {
            if (!_isDockedChecklistMode)
                return;

            _isDockedChecklistMode = false;
            _expandedDockedItems.Clear();
            var prefs = MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Load("powerpoint");
            prefs.PreferDockedInTaskPane = false;
            MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Save("powerpoint", prefs);
            SyncExcelCompactChromeAndDockedVisibility();
            _visibleWindowStart = 0;
            _visibleCardLimit = ExcelProofingCardBatchSize;
            RenderProofingCards();
        }

        private (bool Checked, string Chosen) GetOrInitExcelDockedState(PresentationSlideIssue item)
        {
            if (_dockedStateByItem.TryGetValue(item, out var st))
                return st;

            bool defCheck = item.Issue.IsHighCertaintyPrecheck;
            string defRep = item.Issue.Suggestions != null && item.Issue.Suggestions.Count > 0
                ? item.Issue.Suggestions[0].Replacement
                : string.Empty;
            var created = (defCheck, defRep);
            _dockedStateByItem[item] = created;
            return created;
        }

        private int ExcelProofingCardBatchSize => _lastEcoActive ? EcoExcelProofingCardBatchSize : DefaultExcelProofingCardBatchSize;

        private static string BuildExcelIssuesUiSignature(IReadOnlyList<PresentationSlideIssue> activeItems)
        {
            if (activeItems.Count == 0)
                return "EMPTY";

            var sb = new System.Text.StringBuilder(Math.Min(activeItems.Count, 85) * 28);
            sb.Append(activeItems.Count);
            int cap = Math.Min(activeItems.Count, 80);
            for (int i = 0; i < cap; i++)
            {
                var it = activeItems[i];
                sb.Append('|').Append(it.SheetName).Append(':').Append(it.Row).Append(',').Append(it.Column)
                  .Append(':').Append(it.Issue.OriginalText).Append(':').Append((int)it.Issue.IssueType);
            }
            return sb.ToString();
        }

        public void RenderIssues(IReadOnlyList<PresentationSlideIssue>? items)
        {
            bool wasScanning = _isScanning;
            _isScanning = false;
            _pnlScanProgress.Visible = false;

            var prevActive = _allItems.Where(i => !i.IsResolved).ToList();
            var nextList = items != null ? items.Where(i => !i.IsResolved).ToList() : new List<PresentationSlideIssue>();

            bool unchanged = _hasRenderedProofingOnce &&
                             !wasScanning &&
                             _activeTab == TaskPaneWorkspaceTab.Proofing &&
                             _cardsPanel.Controls.Count > 0 &&
                             string.Equals(BuildExcelIssuesUiSignature(prevActive), BuildExcelIssuesUiSignature(nextList), StringComparison.Ordinal);

            _allItems = nextList;
            _selectedItem = null;
            _stepperCursor = -1;

            if (unchanged)
            {
                UpdateFilterPillStyles();
                UpdateExcelProofingSummaryText(GetFilteredUnresolvedIssues());
                return;
            }

            if (wasScanning || prevActive.Count == 0 || nextList.Count == 0)
            {
                _visibleWindowStart = 0;
                _visibleCardLimit = ExcelProofingCardBatchSize;
            }

            if (_activeTab == TaskPaneWorkspaceTab.Proofing)
            {
                _hasRenderedProofingOnce = true;
                RenderProofingCards();
            }
        }

        public void UpdateQualityReport(DocumentQualityReport? report)
        {
            _latestQualityReport = report;
            if (_activeTab == TaskPaneWorkspaceTab.Toolkit)
            {
                RenderToolkitWorkspace();
            }
        }

        private void OnUserSelectedStyleMode(ProofingStyleMode mode)
        {
            if (_currentStyleMode == mode)
                return;

            _currentStyleMode = mode;
            UpdateStyleModeButtons();
            RequestChangeStyleMode?.Invoke(mode);
        }

        private void UpdateStyleModeButtons()
        {
            bool isFormal = _currentStyleMode == ProofingStyleMode.FormalOfficial;
            StyleSegment(_btnStyleGeneral, !isFormal, Color.FromArgb(183, 71, 42));
            StyleSegment(_btnStyleFormal, isFormal, Color.FromArgb(18, 56, 128));

            _lblStyleHint.Text = isFormal
                ? UiLocalizer.Pick(
                    "Formal / Official — Checks spelling + colloquial particles (ပါတယ်➔ပါသည်, တွေ➔များ, နဲ့➔နှင့်)",
                    "ရုံးသုံး/စာပေစနစ် — စာလုံးပေါင်း + ပါတယ်➔ပါသည်၊ တွေ➔များ၊ နဲ့➔နှင့် စစ်ဆေးသည်")
                : UiLocalizer.Pick(
                    "General Standard — Checks spelling, တ/တစ်, ခုနစ်/ခုနှစ် & contextual meaning (Design-Safe)",
                    "အထွေထွေ စံသတ်ပုံ — စာလုံးပေါင်း၊ တ/တစ်၊ ခုနစ်/ခုနှစ် နှင့် အနက်သံသယ စစ်ဆေးသည်");
        }

        private void SelectAndScanScope(PresentationScanScope scope)
        {
            _currentScanScope = scope;
            UpdateScopeButtons();
            RequestScanScope?.Invoke(scope);
        }

        private void UpdateScopeButtons()
        {
            StyleSegment(_btnScopeSheet, _currentScanScope == PresentationScanScope.ActiveSlide, Color.FromArgb(183, 71, 42));
            StyleSegment(_btnScopeSelection, _currentScanScope == PresentationScanScope.Selection, Color.FromArgb(183, 71, 42));
            StyleSegment(_btnScopeWorkbook, _currentScanScope == PresentationScanScope.EntirePresentation, Color.FromArgb(183, 71, 42));

            _btnScanNow.Text = _currentScanScope switch
            {
                PresentationScanScope.Selection => UiLocalizer.Pick("Scan Selection", "ရွေးကွက် စစ်မည်"),
                PresentationScanScope.EntirePresentation => UiLocalizer.Pick("Scan All Slides", "Slide အားလုံးစစ်"),
                _ => UiLocalizer.Pick("Scan Slide", "Slide စစ်မည်")
            };
        }

        private void SetIssueFilter(IssueFilterMode mode)
        {
            _currentFilter = mode;
            _stepperCursor = -1;
            _visibleWindowStart = 0;
            _visibleCardLimit = ExcelProofingCardBatchSize;
            UpdateFilterPillStyles();
            RenderProofingCards();
        }

        private void UpdateFilterPillStyles()
        {
            int unresolvedTotal = _allItems.Count(i => !i.IsResolved);
            int spellingCount = _allItems.Count(i => !i.IsResolved && !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning);
            int grammarCount = _allItems.Count(i => !i.IsResolved && i.Issue.IsGrammarOrStyle);
            int contextCount = _allItems.Count(i => !i.IsResolved && i.Issue.IsContextualWarning);

            _btnFilterAll.Text = UiLocalizer.Pick($"All ({unresolvedTotal:N0})", $"အားလုံး ({unresolvedTotal:N0})");
            _btnFilterSpelling.Text = UiLocalizer.Pick($"Spelling ({spellingCount:N0})", $"သတ်ပုံ ({spellingCount:N0})");
            _btnFilterGrammar.Text = UiLocalizer.Pick($"Grammar ({grammarCount:N0})", $"သဒ္ဒါ ({grammarCount:N0})");
            _btnFilterContext.Text = UiLocalizer.Pick($"Meaning ({contextCount:N0})", $"အနက် ({contextCount:N0})");

            ApplyFilterPillAppearance(_btnFilterAll, _currentFilter == IssueFilterMode.All, Color.FromArgb(24, 58, 118), Color.FromArgb(238, 242, 250), Color.FromArgb(45, 58, 78));
            ApplyFilterPillAppearance(_btnFilterSpelling, _currentFilter == IssueFilterMode.SpellingOnly, Color.FromArgb(196, 36, 36), Color.FromArgb(253, 238, 238), Color.FromArgb(158, 28, 28));
            ApplyFilterPillAppearance(_btnFilterGrammar, _currentFilter == IssueFilterMode.GrammarOnly, Color.FromArgb(24, 90, 188), Color.FromArgb(236, 244, 255), Color.FromArgb(20, 74, 158));
            ApplyFilterPillAppearance(_btnFilterContext, _currentFilter == IssueFilterMode.ContextOnly, Color.FromArgb(196, 118, 12), Color.FromArgb(255, 246, 224), Color.FromArgb(152, 88, 8));

            _btnPrevIssue.BackColor = Color.FromArgb(242, 245, 250);
            _btnPrevIssue.ForeColor = Color.FromArgb(48, 58, 74);
            _btnPrevIssue.FlatAppearance.BorderColor = Color.FromArgb(214, 220, 232);
            _btnNextIssue.BackColor = Color.FromArgb(242, 245, 250);
            _btnNextIssue.ForeColor = Color.FromArgb(48, 58, 74);
            _btnNextIssue.FlatAppearance.BorderColor = Color.FromArgb(214, 220, 232);

            UpdateFixAllButtonState(spellingCount, grammarCount, contextCount);
        }

        private void UpdateFixAllButtonState(int spellingCount, int grammarCount, int contextCount)
        {
            _ = spellingCount;
            _ = grammarCount;
            _ = contextCount;

            static bool HasChoices(PresentationSlideIssue i) =>
                !i.IsResolved && i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0;

            int reviewableCount = _currentFilter switch
            {
                IssueFilterMode.SpellingOnly => _allItems.Count(i => HasChoices(i) && !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning),
                IssueFilterMode.GrammarOnly => _allItems.Count(i => HasChoices(i) && i.Issue.IsGrammarOrStyle),
                IssueFilterMode.ContextOnly => _allItems.Count(i => HasChoices(i) && i.Issue.IsContextualWarning),
                _ => _allItems.Count(HasChoices)
            };

            _btnFixAll.Enabled = reviewableCount > 0;
            _btnFixAll.Text = reviewableCount > 0
                ? UiLocalizer.Pick($"Fix All ({reviewableCount:N0})", $"စိစစ်ပြင် ({reviewableCount:N0})")
                : UiLocalizer.Pick("Fix All", "စိစစ်ပြင်မည်");
            _btnFixAll.BackColor = reviewableCount > 0 ? Color.FromArgb(24, 90, 188) : Color.FromArgb(204, 214, 228);
            _btnFixAll.ForeColor = Color.White;
        }

        private static void ApplyFilterPillAppearance(Button btn, bool active, Color activeBg, Color idleBg, Color idleFg)
        {
            btn.BackColor = active ? activeBg : idleBg;
            btn.ForeColor = active ? Color.White : idleFg;
            btn.FlatAppearance.BorderColor = active ? activeBg : Color.FromArgb(218, 224, 234);
        }

        private List<PresentationSlideIssue> GetFilteredUnresolvedIssues()
        {
            return _allItems.Where(i =>
            {
                if (i.IsResolved) return false;
                return _currentFilter switch
                {
                    IssueFilterMode.SpellingOnly => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning,
                    IssueFilterMode.GrammarOnly => i.Issue.IsGrammarOrStyle,
                    IssueFilterMode.ContextOnly => i.Issue.IsContextualWarning,
                    _ => true
                };
            }).ToList();
        }

        private void StepIssue(int delta)
        {
            var visible = GetFilteredUnresolvedIssues();
            if (visible.Count == 0)
                return;

            if (_stepperCursor < 0 || _stepperCursor >= visible.Count)
                _stepperCursor = delta >= 0 ? 0 : visible.Count - 1;
            else
                _stepperCursor = (_stepperCursor + delta + visible.Count) % visible.Count;

            PresentationSlideIssue target = visible[_stepperCursor];
            _selectedItem = target;
            RequestSelectIssueInSheet?.Invoke(target);

            if (!_cardByItem.ContainsKey(target))
            {
                _visibleWindowStart = (_stepperCursor / ExcelProofingCardBatchSize) * ExcelProofingCardBatchSize;
                _visibleCardLimit = Math.Min(MaxMountedExcelCards, ExcelProofingCardBatchSize * 2);
                RenderProofingCards();
            }

            if (_cardByItem.TryGetValue(target, out var cardCtrl))
            {
                _cardsPanel.ScrollControlIntoView(cardCtrl);
                foreach (var kv in _cardByItem.Values)
                    kv.Invalidate();
            }
        }

        /// <summary>
        /// Passively highlights the matching issue card in the Task Pane when the user moves the Excel cursor
        /// onto a flagged cell (without re-triggering sheet selection or stealing focus from the worksheet).
        /// </summary>
        public void HighlightCellIssueFromSheet(PresentationSlideIssue? item)
        {
            if (ReferenceEquals(_selectedItem, item))
                return;

            _selectedItem = item;
            if (item == null)
            {
                foreach (var kv in _cardByItem.Values)
                    kv.Invalidate();
                return;
            }

            if (_activeTab != TaskPaneWorkspaceTab.Proofing)
                return;

            var visible = GetFilteredUnresolvedIssues();
            int idx = visible.IndexOf(item);
            if (idx >= 0)
            {
                _stepperCursor = idx;
                if (!_cardByItem.ContainsKey(item))
                {
                    _visibleWindowStart = (idx / ExcelProofingCardBatchSize) * ExcelProofingCardBatchSize;
                    _visibleCardLimit = Math.Min(MaxMountedExcelCards, ExcelProofingCardBatchSize * 2);
                    RenderProofingCards();
                }
            }

            if (_cardByItem.TryGetValue(item, out var cardCtrl))
            {
                try { _cardsPanel.ScrollControlIntoView(cardCtrl); } catch { }
            }

            foreach (var kv in _cardByItem.Values)
                kv.Invalidate();
        }

        private Dictionary<string, int> BuildExcelSameWordCountsMap()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _allItems.Count; i++)
            {
                var x = _allItems[i];
                if (x.IsResolved)
                    continue;
                string norm = UnicodeNormalizer.NormalizeCanonicalOrder((x.Issue.OriginalText ?? string.Empty).Trim());
                if (string.IsNullOrEmpty(norm))
                    continue;
                counts.TryGetValue(norm, out int prev);
                counts[norm] = prev + 1;
            }
            return counts;
        }

        private void UpdateExcelProofingSummaryText(IReadOnlyList<PresentationSlideIssue> filtered)
        {
            if (_isScanning)
                return;

            int activeCount = filtered.Count;
            if (_lastScanOutcome != null && _lastScanOutcome.WasCancelled)
            {
                _lblSummary.Text = activeCount > 0
                    ? UiLocalizer.Pick(
                        $"⏹ Scan stopped — showing {activeCount:N0} issue(s) found so far",
                        $"⏹ စစ်ဆေးမှုကို ရပ်တန့်ထားသည် — တွေ့ရှိပြီးသား ({activeCount:N0}) ခု")
                    : UiLocalizer.Pick(
                        "⏹ Scan stopped — click [Scan] to resume checking",
                        "⏹ စစ်ဆေးမှုကို ရပ်တန့်ထားသည် — ထပ်စစ်ရန် [စစ်မည်] ကိုနှိပ်ပါ");
                _lblSummary.ForeColor = Color.FromArgb(158, 86, 6);
                return;
            }

            if (_lastScanOutcome != null && _lastScanOutcome.WasIssueCapReached && activeCount > 0)
            {
                _lblSummary.Text = UiLocalizer.Pick(
                    $"⚡ Capped at {activeCount:N0} issues to keep Excel responsive",
                    $"⚡ စက်မလေးစေရန် အမှားအများဆုံး ({activeCount:N0}) ခုအထိသာ ကန့်သတ်ပြသထားသည်");
                _lblSummary.ForeColor = Color.FromArgb(168, 82, 8);
                return;
            }

            if (_lastScanOutcome != null && _lastScanOutcome.WasPartialAutoScan)
            {
                if (activeCount == 0)
                {
                    _lblSummary.Text = UiLocalizer.Pick(
                        $"⚡ {_lastScanOutcome.ScannedCellCount:N0} nearby clean — click [Scan] for full",
                        $"⚡ အနီးဝန်းကျင် ({_lastScanOutcome.ScannedCellCount:N0}) ကွက် အမှားကင်း — [စစ်မည်] နှိပ်ပါ");
                    _lblSummary.ForeColor = Color.FromArgb(183, 71, 42);
                }
                else
                {
                    _lblSummary.Text = UiLocalizer.Pick(
                        $"⚡ {activeCount:N0} nearby — click [Scan] for full sheet",
                        $"⚡ အနီးဝန်းကျင်၌ ({activeCount:N0}) ခု — [စစ်မည်] နှိပ်ပါ");
                    _lblSummary.ForeColor = Color.FromArgb(182, 32, 32);
                }
                return;
            }

            if (activeCount == 0)
            {
                int totalUnresolved = _allItems.Count(i => !i.IsResolved);
                _lblSummary.Text = totalUnresolved == 0
                    ? UiLocalizer.Pick(
                        "✓ No Myanmar spelling or grammar issues found in scanned slides & notes",
                        "✓ စစ်ဆေးပြီးသော မြန်မာစာ ဆလိုက်များတွင် အမှားအယွင်း မတွေ့ရှိပါ")
                    : UiLocalizer.Pick(
                        $"No issues in this filter ({totalUnresolved:N0} in other categories)",
                        $"ဤအမျိုးအစားတွင် တွေ့ရှိချက် မရှိပါ (အခြားအမျိုးအစား၌ {totalUnresolved:N0} ခု ရှိသည်)");
                _lblSummary.ForeColor = Color.FromArgb(183, 71, 42);
            }
            else
            {
                _lblSummary.Text = UiLocalizer.Pick(
                    $"{activeCount:N0} issue(s) found — click any card to jump directly to its slide & text span",
                    $"တွေ့ရှိချက် ({activeCount:N0}) ခု — ကတ်ကိုနှိပ်၍ သက်ဆိုင်ရာ ဆလိုက်စာသားသို့ တိုက်ရိုက်သွားနိုင်သည်");
                _lblSummary.ForeColor = filtered.Any(x => !x.Issue.IsReviewNeeded && !x.Issue.IsGrammarOrStyle)
                    ? Color.FromArgb(182, 32, 32)
                    : Color.FromArgb(146, 100, 0);
            }
        }

        private void RenderProofingCards()
        {
            SyncExcelCompactChromeAndDockedVisibility();
            UpdateFilterPillStyles();
            var filtered = GetFilteredUnresolvedIssues();
            UpdateExcelProofingSummaryText(filtered);
            if (_isDockedChecklistMode)
            {
                UpdateExcelStickyDockedHeaderCounts(filtered);
            }

            BeginCardsBatchUpdate();
            try
            {
                DisposeAndClearCardsPanel();

                int cardWidth = GetTargetCardWidth(reserveScrollbarForMultipleCards: filtered.Count >= 3);
                int innerWrap = Math.Max(180, cardWidth - 32);

                if (filtered.Count == 0)
                {
                    _visibleWindowStart = 0;

                    if (_isScanning)
                    {
                        var scanCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188));
                        int sr = 0;

                        var lblScanTitle = new Label
                        {
                            Text = UiLocalizer.Pick(
                                "⟳ Scanning Presentation Slides...",
                                "⟳ PowerPoint ဆလိုက်များကို အမြန်စစ်ဆေးနေပါသည်..."),
                            Font = _fontBodyBold,
                            ForeColor = Color.FromArgb(24, 90, 188),
                            AutoSize = true,
                            MaximumSize = new Size(innerWrap, 0),
                            Tag = WrapTag,
                            Dock = DockStyle.Fill,
                            Margin = new Padding(0, 0, 0, 5)
                        };
                        scanCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        scanCard.Controls.Add(lblScanTitle, 0, sr++);

                        var lblScanBody = new Label
                        {
                            Text = UiLocalizer.Pick(
                                "Checking Slide TextBoxes, Tables, Grouped Shapes & Speaker Notes with zero mutation to your .pptx design.",
                                "ဆလိုက်ဒီဇိုင်းကို လုံးဝမထိခိုက်စေဘဲ TextBox၊ ဇယား၊ Group ပုံ နှင့် Speaker Notes များကို စစ်ဆေးနေပါသည်။"),
                            Font = _fontSmall,
                            ForeColor = Color.FromArgb(62, 74, 92),
                            AutoSize = true,
                            MaximumSize = new Size(innerWrap, 0),
                            Tag = WrapTag,
                            Dock = DockStyle.Fill,
                            Margin = new Padding(0, 0, 0, 8)
                        };
                        scanCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        scanCard.Controls.Add(lblScanBody, 0, sr++);

                        var btnStopCard = CreatePillButton(
                            UiLocalizer.Pick("■ Stop Scan", "■ စစ်ဆေးမှု ရပ်မည်"),
                            Color.FromArgb(254, 236, 236), Color.FromArgb(178, 28, 28), Color.FromArgb(238, 182, 182), _fontSmallBold);
                        btnStopCard.Click += (s, e) => RequestCancelScan?.Invoke();
                        scanCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        scanCard.Controls.Add(btnStopCard, 0, sr++);

                        _cardsPanel.Controls.Add(scanCard);
                        _lastAppliedCardWidth = cardWidth;
                        _lastAppliedControlCount = _cardsPanel.Controls.Count;
                        return;
                    }

                    var cleanCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 124, 65));
                    int r = 0;

                    var lblOkTitle = new Label
                    {
                        Text = UiLocalizer.Pick(
                            "✓ Myanmar Spelling & Grammar Look Great (Design-Safe)",
                            "✓ မြန်မာစာ စာလုံးပေါင်းနှင့် သဒ္ဒါ ကောင်းမွန်ပါသည် (Design-Safe)"),
                        Font = _fontBodyBold,
                        ForeColor = Color.FromArgb(16, 102, 52),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0, 0, 0, 5)
                    };
                    cleanCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    cleanCard.Controls.Add(lblOkTitle, 0, r++);

                    var lblOkBody = new Label
                    {
                        Text = UiLocalizer.Pick(
                            "• Scan the Active Sheet, Selected Cells, or Entire Workbook anytime.\n• Flagged cells display screen-only red (spelling), blue (grammar), or amber (meaning) wavy underlines — cell text color & saved .xlsx files are never altered.\n• Excel formulas (=SUM, =IF, =VLOOKUP) and numeric calculations are 100% protected and untouched.",
                            "• လက်ရှိ Sheet၊ ရွေးထားသော အကွက်များ သို့မဟုတ် Workbook တစ်ခုလုံးကို ရွေးချယ်စစ်ဆေးနိုင်ပါသည်။\n• အမှားတွေ့ရှိသော အကွက်များတွင် ဖန်သားပြင်ပေါ်၌သာ အနီ/အပြာ/လိမ္မော် မျဉ်းလှိုင်းများ ပြသပြီး Cell စာသားအရောင်နှင့် .xlsx ဖိုင်ကို လုံးဝ မပြောင်းလဲပါ။\n• Excel Formula အကွက်များ (=SUM, =IF, =VLOOKUP) နှင့် ကိန်းဂဏန်းတွက်ချက်မှုများကို 100% အလိုအလျောက် ကာကွယ်ချန်လှပ်ထားပါသည်။"),
                        Font = _fontSmall,
                        ForeColor = Color.FromArgb(62, 74, 92),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill
                    };
                    cleanCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    cleanCard.Controls.Add(lblOkBody, 0, r++);

                    _cardsPanel.Controls.Add(cleanCard);
                    _lastAppliedCardWidth = cardWidth;
                    _lastAppliedControlCount = _cardsPanel.Controls.Count;
                    return;
                }

                int batchSize = ExcelProofingCardBatchSize;
                if (_visibleWindowStart >= filtered.Count)
                {
                    _visibleWindowStart = Math.Max(0, ((filtered.Count - 1) / batchSize) * batchSize);
                }

                var batchControls = new List<Control>(Math.Min(MaxMountedExcelCards + 4, filtered.Count + 4));
                if (!_isDockedChecklistMode && !_isScanning)
                {
                    int zwspCellIssues = filtered.Count(i => MyanmarZwspToolkit.ContainsAnyZeroWidthChar(i.Issue.OriginalText));
                    if (zwspCellIssues >= 2)
                    {
                        batchControls.Add(BuildExcelHiddenZwspBannerCard(cardWidth, innerWrap, zwspCellIssues));
                    }
                }
                if (_visibleWindowStart > 0)
                {
                    var prevCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(183, 71, 42));
                    var btnPrevBatch = CreatePillButton(
                        UiLocalizer.Pick(
                            $"▴ View Previous Issues (#1 to #{_visibleWindowStart:N0})",
                            $"▴ ရှေ့ပိုင်း တွေ့ရှိချက် (#1 မှ #{_visibleWindowStart:N0}) ပြန်ကြည့်မည်"),
                        Color.FromArgb(234, 246, 239),
                        Color.FromArgb(14, 96, 50),
                        Color.FromArgb(182, 220, 196),
                        _fontSmallBold);
                    btnPrevBatch.Click += (s, e) =>
                    {
                        int step = ExcelProofingCardBatchSize;
                        _visibleWindowStart = Math.Max(0, _visibleWindowStart - step);
                        _visibleCardLimit = Math.Min(MaxMountedExcelCards, step * 2);
                        RenderProofingCards();
                    };
                    prevCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    prevCard.Controls.Add(btnPrevBatch, 0, 0);
                    batchControls.Add(prevCard);
                }
                else if (!_isDockedChecklistMode && filtered.Count >= 4)
                {
                    Control? repeatedCard = TryBuildRepeatedIssuesBatchFixCard(filtered, cardWidth, innerWrap);
                    if (repeatedCard != null)
                    {
                        batchControls.Add(repeatedCard);
                    }
                }

                int windowCount = Math.Min(MaxMountedExcelCards, Math.Max(batchSize, _visibleCardLimit));
                int endExclusive = Math.Min(filtered.Count, _visibleWindowStart + windowCount);

                if (_isDockedChecklistMode)
                {
                    for (int idx = _visibleWindowStart; idx < endExclusive; idx++)
                    {
                        PresentationSlideIssue item = filtered[idx];
                        if (item.Issue.Suggestions == null || item.Issue.Suggestions.Count == 0)
                            continue;
                        Control rowCard = BuildExcelDockedChecklistRowCard(item, cardWidth);
                        _cardByItem[item] = rowCard;
                        batchControls.Add(rowCard);
                    }
                }
                else
                {
                    var sameWordCounts = BuildExcelSameWordCountsMap();
                    for (int idx = _visibleWindowStart; idx < endExclusive; idx++)
                    {
                        PresentationSlideIssue item = filtered[idx];
                        string normOrig = UnicodeNormalizer.NormalizeCanonicalOrder((item.Issue.OriginalText ?? string.Empty).Trim());
                        int dupCount = sameWordCounts.TryGetValue(normOrig, out int cnt) ? cnt : 1;

                        Control card = BuildExcelIssueCard(item, idx + 1, cardWidth, innerWrap, dupCount);
                        _cardByItem[item] = card;
                        batchControls.Add(card);
                    }
                }

                if (filtered.Count > endExclusive)
                {
                    int remaining = filtered.Count - endExclusive;
                    int nextBatch = Math.Min(batchSize, remaining);
                    var moreCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(183, 71, 42));
                    var btnMoreBatch = CreatePillButton(
                        UiLocalizer.Pick(
                            $"▾ Load Next {nextBatch:N0} Issue(s)  •  {remaining:N0} / {filtered.Count:N0} Remaining",
                            $"▾ နောက်ထပ် တွေ့ရှိချက် ({nextBatch:N0}) ခု ဆက်လက်ဖော်ပြမည်  •  ကျန်ရှိ ({remaining:N0} / {filtered.Count:N0})"),
                        Color.FromArgb(234, 246, 239),
                        Color.FromArgb(14, 96, 50),
                        Color.FromArgb(182, 220, 196),
                        _fontSmallBold);
                    btnMoreBatch.Click += (s, e) =>
                    {
                        int step = ExcelProofingCardBatchSize;
                        if (_cardByItem.Count + step <= MaxMountedExcelCards)
                        {
                            _visibleCardLimit += step;
                        }
                        else
                        {
                            _visibleWindowStart = Math.Min(
                                Math.Max(0, filtered.Count - step),
                                _visibleWindowStart + step);
                            _visibleCardLimit = MaxMountedExcelCards;
                        }
                        RenderProofingCards();
                    };
                    moreCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    moreCard.Controls.Add(btnMoreBatch, 0, 0);
                    batchControls.Add(moreCard);
                }

                _cardsPanel.Controls.AddRange(batchControls.ToArray());
                _lastAppliedCardWidth = cardWidth;
                _lastAppliedControlCount = _cardsPanel.Controls.Count;
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private Control BuildExcelHiddenZwspBannerCard(int cardWidth, int innerWrap, int zwspCellIssues)
        {
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(180, 114, 8));
            int r = 0;

            var lblInfo = new Label
            {
                Text = UiLocalizer.Pick(
                    $"✨ {zwspCellIssues:N0} cell issue(s) contain hidden Zero-Width Space (ZWSP) characters — silently breaks VLOOKUP, XLOOKUP, SUMIF & PivotTable grouping!",
                    $"✨ အကွက် ({zwspCellIssues:N0}) နေရာ၌ မမြင်ရသော ZWSP ပါဝင်နေပါသည် — VLOOKUP, XLOOKUP, SUMIF နှင့် PivotTable တွက်ချက်မှုများကို အမှားဖြစ်စေပါသည်!"),
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(113, 63, 18),
                BackColor = Color.FromArgb(254, 249, 195),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Padding = new Padding(7, 5, 7, 5),
                Margin = new Padding(0, 0, 0, 6)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblInfo, 0, r++);

            var btnStrip = CreatePillButton(
                UiLocalizer.Pick(
                    "✨ Strip All Hidden ZWSP Across Sheet (Protect VLOOKUP)",
                    "✨ ဇယားတစ်ခုလုံးရှိ မမြင်ရသော ZWSP အားလုံး ချက်ချင်းဖယ်ရှားမည်"),
                Color.FromArgb(16, 118, 62),
                Color.White,
                Color.FromArgb(14, 98, 50),
                _fontSmallBold);
            btnStrip.Click += (s, e) => RequestStripAllZwsp?.Invoke(false);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnStrip, 0, r++);
            return card;
        }

        private Control? TryBuildRepeatedIssuesBatchFixCard(
            IReadOnlyList<PresentationSlideIssue> filtered,
            int cardWidth,
            int innerWrap)
        {
            var groups = new Dictionary<string, (string RawOrig, string TopReplacement, int Count)>(StringComparer.Ordinal);
            for (int i = 0; i < filtered.Count; i++)
            {
                PresentationSlideIssue item = filtered[i];
                if (item.IsResolved ||
                    item.Issue.IsContextualWarning ||
                    item.Issue.Suggestions == null ||
                    item.Issue.Suggestions.Count == 0)
                {
                    continue;
                }

                string rawOrig = (item.Issue.OriginalText ?? string.Empty).Trim();
                string topRep = (item.Issue.Suggestions[0].Replacement ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(rawOrig) || string.IsNullOrEmpty(topRep))
                    continue;

                string normOrig = UnicodeNormalizer.NormalizeCanonicalOrder(rawOrig);
                if (groups.TryGetValue(normOrig, out var existing))
                {
                    groups[normOrig] = (existing.RawOrig, existing.TopReplacement, existing.Count + 1);
                }
                else
                {
                    groups[normOrig] = (rawOrig, topRep, 1);
                }
            }

            var topRepeated = groups.Values
                .Where(g => g.Count >= 3)
                .OrderByDescending(g => g.Count)
                .Take(6)
                .ToList();

            if (topRepeated.Count == 0)
                return null;

            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 118, 84));
            int r = 0;

            var lblTitle = new Label
            {
                Text = UiLocalizer.Pick(
                    "⚡ Repeated Issues — 1-Click Batch Fix",
                    "⚡ ထပ်ခါတလဲလဲ အမှားများ — တစ်ချက်နှိပ် အစုလိုက်ပြင်ရန်"),
                Font = _fontBodyBold,
                ForeColor = Color.FromArgb(14, 98, 68),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, 3)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblTitle, 0, r++);

            var lblSub = new Label
            {
                Text = UiLocalizer.Pick(
                    "Fix identical typos across the entire scanned sheet in 1 click (formulas stay 100% untouched):",
                    "ဇယားတစ်ခုလုံးရှိ တူညီသောအမှားများကို တစ်ချက်နှိပ်ရုံဖြင့် အမြန်ပြင်နိုင်သည် (Formula များ မထိခိုက်ပါ) -"),
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(72, 86, 104),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, 6)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblSub, 0, r++);

            for (int i = 0; i < topRepeated.Count; i++)
            {
                var grp = topRepeated[i];
                var btn = new WordWrapChoiceButton
                {
                    Text = UiLocalizer.Pick(
                        $"✓✓ \"{grp.RawOrig}\"  ➔  \"{grp.TopReplacement}\"   ({grp.Count:N0} cells)",
                        $"✓✓ \"{grp.RawOrig}\"  ➔  \"{grp.TopReplacement}\"   ({grp.Count:N0} ကွက်)"),
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    UseMnemonic = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 5, 8, 5),
                    Margin = new Padding(0, 0, 0, i == topRepeated.Count - 1 ? 0 : 4),
                    BackColor = Color.FromArgb(232, 248, 239),
                    ForeColor = Color.FromArgb(14, 78, 54),
                    FlatStyle = FlatStyle.Flat,
                    Font = _fontSmallBold,
                    Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(148, 212, 176);
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(208, 240, 222);

                string origCapture = grp.RawOrig;
                string repCapture = grp.TopReplacement;
                btn.Click += (s, e) => RequestApplyBatchSuggestion?.Invoke(origCapture, repCapture);

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(btn, 0, r++);
            }

            return card;
        }

        private Control BuildExcelIssueCard(
            PresentationSlideIssue item,
            int displayIndex,
            int cardWidth,
            int innerWrap,
            int duplicateWordCount)
        {
            ProofingIssue issue = item.Issue;
            bool isReviewNeeded = issue.IsReviewNeeded;
            Color accentColor = isReviewNeeded
                ? Color.FromArgb(218, 162, 0)
                : (issue.IsGrammarOrStyle ? Color.FromArgb(24, 90, 188) : Color.FromArgb(204, 38, 38));

            Color badgeTextColor = isReviewNeeded
                ? Color.FromArgb(138, 94, 0)
                : accentColor;

            Color diffBannerBg = isReviewNeeded
                ? Color.FromArgb(254, 249, 195)
                : (issue.IsGrammarOrStyle ? Color.FromArgb(238, 245, 255) : Color.FromArgb(254, 239, 239));

            string categoryText = issue.IsContextualWarning
                ? UiLocalizer.Pick("⚠ Meaning / Context Check", "⚠ အနက်သံသယ (Context Check)")
                : (isReviewNeeded
                    ? UiLocalizer.Pick("⚠ Review Needed (Name/Guess)", "⚠ စိစစ်ရန် (အမည်/ခန့်မှန်းချက်)")
                    : (issue.IssueType switch
                    {
                        ProofingIssueType.NumeralClassifierError => UiLocalizer.Pick("● Numeral & Classifier", "● ဂဏန်းနှင့် မျိုးပြပစ္စည်း"),
                        ProofingIssueType.OfficialStyleGrammarError => UiLocalizer.Pick("● Formal / Literary Grammar", "● ရုံးသုံး/စာပေ သဒ္ဒါ"),
                        ProofingIssueType.PunctuationError => UiLocalizer.Pick("● Punctuation (၊ ။)", "● ပုဒ်ဖြတ်ပုဒ်ရပ် (၊ ။)"),
                        ProofingIssueType.ZawgyiEncodingError => UiLocalizer.Pick("● Zawgyi Encoding Error", "● ဇော်ဂျီ ဖောင့်ကုဒ်အမှား"),
                        ProofingIssueType.LookalikeCharacterError => UiLocalizer.Pick("● Lookalike Character (၀/ဝ, စဉ်/စဥ်)", "● ဆင်တူအက္ခရာ (၀/ဝ ၊ စဉ်/စဥ်)"),
                        _ => UiLocalizer.Pick("● Spelling Issue", "● စာလုံးပေါင်းသတ်ပုံ")
                    }));

            var card = CreateStyledCardContainer(cardWidth, accentColor, item);
            int row = 0;

            void JumpToCell()
            {
                _selectedItem = item;
                RequestSelectIssueInSheet?.Invoke(item);
                foreach (var kv in _cardByItem.Values)
                    kv.Invalidate();
            }
            card.Click += (s, e) => JumpToCell();

            // Row 0: Category Label + Cell Coordinate Pill (e.g., Sheet1!C14) + #Index
            var topBar = new FastPercentRowPanel(68F, 32F)
            {
                Margin = new Padding(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            topBar.Click += (s, e) => JumpToCell();

            var lblCat = new Label
            {
                Text = categoryText,
                Font = _fontSmallBold,
                ForeColor = badgeTextColor,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 2, 0, 0),
                Cursor = Cursors.Hand
            };
            lblCat.Click += (s, e) => JumpToCell();

            var btnCellLoc = CreatePillButton(
                $"▸ {item.DisplayLocation}  #{displayIndex}",
                Color.FromArgb(235, 246, 239),
                Color.FromArgb(16, 98, 52),
                Color.FromArgb(186, 222, 198),
                _fontBrandStats);
            btnCellLoc.Dock = DockStyle.Right;
            btnCellLoc.Click += (s, e) => JumpToCell();
            _toolTip.SetToolTip(btnCellLoc, UiLocalizer.Pick(
                $"Click to jump directly to cell {item.DisplayLocation}",
                $"{item.DisplayLocation} အကွက်သို့ တိုက်ရိုက်သွားရန် နှိပ်ပါ"));

            topBar.Controls.Add(lblCat);
            topBar.Controls.Add(btnCellLoc);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(topBar, 0, row++);

            // Row 1: Diff Banner ("အမှား" -> "အမှန်")
            bool hasSuggestion = issue.Suggestions.Count > 0;
            string topSuggested = hasSuggestion ? issue.Suggestions[0].Replacement : UiLocalizer.Pick("Review", "စိစစ်ရန်");
            var pnlDiff = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = diffBannerBg,
                Padding = new Padding(8, 6, 8, 6),
                Margin = new Padding(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            pnlDiff.Click += (s, e) => JumpToCell();

            string visOrig = issue.FormatVisualOriginalText(hasSuggestion ? topSuggested : null);
            var lblDiff = new Label
            {
                Text = $"\"{visOrig}\"   ➔   \"{topSuggested}\"",
                Font = _fontCardDiff,
                ForeColor = Color.FromArgb(24, 32, 46),
                AutoSize = true,
                MaximumSize = new Size(innerWrap - 16, 0),
                Tag = WrapTag,
                Cursor = Cursors.Hand
            };
            lblDiff.Click += (s, e) => JumpToCell();
            pnlDiff.Controls.Add(lblDiff);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(pnlDiff, 0, row++);

            // Row 2: In-Cell Context Excerpt so user sees surrounding cell text
            var lblCellSnippet = new Label
            {
                Text = UiLocalizer.Pick(
                    $"Cell Text — {item.BuildContextSnippet(24)}",
                    $"အကွက်စာသား — {item.BuildContextSnippet(24)}"),
                Font = _fontSmall,
                ForeColor = Color.FromArgb(72, 84, 102),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
            lblCellSnippet.Click += (s, e) => JumpToCell();
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblCellSnippet, 0, row++);

            // Row 3: Ranked Suggestion Buttons
            if (issue.Suggestions != null && issue.Suggestions.Count > 0)
            {
                int maxSug = Math.Min(issue.Suggestions.Count, 5);
                if (maxSug > 1)
                {
                    var lblChoicesHeader = new Label
                    {
                        Text = UiLocalizer.Pick(
                            $"Choose Replacement ({maxSug} options) —",
                            $"ရွေးချယ်ပြင်ဆင်ရန် ({maxSug} မျိုး) —"),
                        Font = _fontBrandStats,
                        ForeColor = issue.IsContextualWarning ? Color.FromArgb(148, 86, 8) : Color.FromArgb(183, 71, 42),
                        AutoSize = true,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0, 0, 0, 3)
                    };
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(lblChoicesHeader, 0, row++);
                }

                for (int sIdx = 0; sIdx < maxSug; sIdx++)
                {
                    SuggestionCandidate sug = issue.Suggestions[sIdx];
                    bool isPrimary = sIdx == 0;
                    string cuePart = !string.IsNullOrWhiteSpace(sug.MeaningCue)
                        ? $"  [{TruncateGloss(sug.MeaningCue, 56)}]"
                        : string.Empty;

                    Color baseBg = issue.IsContextualWarning
                        ? (isPrimary ? Color.FromArgb(255, 246, 224) : Color.FromArgb(255, 251, 240))
                        : (issue.IsGrammarOrStyle
                            ? (isPrimary ? Color.FromArgb(228, 241, 255) : Color.FromArgb(242, 248, 255))
                            : (isPrimary ? Color.FromArgb(224, 246, 232) : Color.FromArgb(241, 250, 244)));

                    Color hoverBg = issue.IsContextualWarning
                        ? Color.FromArgb(252, 236, 198)
                        : (issue.IsGrammarOrStyle ? Color.FromArgb(210, 230, 255) : Color.FromArgb(202, 238, 216));

                    Color borderClr = issue.IsContextualWarning
                        ? Color.FromArgb(235, 192, 112)
                        : (issue.IsGrammarOrStyle
                            ? (isPrimary ? Color.FromArgb(142, 186, 244) : Color.FromArgb(184, 210, 246))
                            : (isPrimary ? Color.FromArgb(118, 198, 148) : Color.FromArgb(168, 218, 186)));

                    string prefixIcon = issue.IsContextualWarning
                        ? UiLocalizer.Pick("Review  ➔  ", "စိစစ်ရွေးမည်  ➔  ")
                        : UiLocalizer.Pick("✓ Apply  ➔  ", "✓ ပြင်မည်  ➔  ");
                    var btnChoice = new WordWrapChoiceButton
                    {
                        Text = $"{prefixIcon}{sug.Replacement}{cuePart}",
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        Font = isPrimary ? _fontSuggest : _fontBody,
                        FlatStyle = FlatStyle.Flat,
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = baseBg,
                        ForeColor = Color.FromArgb(18, 38, 62),
                        Padding = new Padding(8, 5, 8, 5),
                        Margin = new Padding(0, 0, 0, 5),
                        Cursor = Cursors.Hand
                    };
                    btnChoice.FlatAppearance.BorderColor = borderClr;
                    btnChoice.FlatAppearance.MouseOverBackColor = hoverBg;

                    string chosenReplacement = sug.Replacement;
                    btnChoice.Click += (s, e) => RequestApplySuggestion?.Invoke(item, chosenReplacement);

                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(btnChoice, 0, row++);
                }

                // If the same typo appears in 2+ cells across the sheet/column, offer 1-click batch column fix!
                if (duplicateWordCount > 1 && !issue.IsContextualWarning)
                {
                    string topRep = issue.Suggestions[0].Replacement;
                    var btnBatchSame = new WordWrapChoiceButton
                    {
                        Text = UiLocalizer.Pick(
                            $"✓✓ Fix All ({duplicateWordCount}) Matching Cells  ➔  {topRep}",
                            $"✓✓ တူညီသောအမှား ({duplicateWordCount}) ကွက်လုံး ပြင်မည်  ➔  {topRep}"),
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(8, 5, 8, 5),
                        Margin = new Padding(0, 0, 0, 6),
                        BackColor = Color.FromArgb(20, 118, 86),
                        ForeColor = Color.White,
                        FlatStyle = FlatStyle.Flat,
                        Font = _fontSmallBold,
                        Cursor = Cursors.Hand
                    };
                    btnBatchSame.FlatAppearance.BorderColor = Color.FromArgb(14, 96, 70);
                    btnBatchSame.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 102, 74);
                    btnBatchSame.Click += (s, e) => RequestApplyBatchSuggestion?.Invoke(issue.OriginalText, topRep);
                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(btnBatchSame, 0, row++);
                }
            }

            // Row 4: Explanation + Grammatical Reasoning
            string fullExplanation = issue.Message ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(issue.GrammaticalReasoning) &&
                !fullExplanation.Contains(issue.GrammaticalReasoning))
            {
                fullExplanation = string.IsNullOrWhiteSpace(fullExplanation)
                    ? issue.GrammaticalReasoning
                    : UiLocalizer.Pick(
                        $"{fullExplanation}\n• Grammar / Context Note — {issue.GrammaticalReasoning}",
                        $"{fullExplanation}\n• သဒ္ဒါ/အနက် အကြောင်းပြချက် — {issue.GrammaticalReasoning}");
            }

            if (!string.IsNullOrWhiteSpace(fullExplanation))
            {
                var lblMsg = new Label
                {
                    Text = fullExplanation,
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(54, 64, 80),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 2, 0, 4)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblMsg, 0, row++);
            }

            if (!string.IsNullOrWhiteSpace(issue.AuthoritativeSource))
            {
                var lblAuth = new Label
                {
                    Text = UiLocalizer.Pick(
                        $"Reference — {issue.AuthoritativeSource}",
                        $"ကိုးကားကျမ်း — {issue.AuthoritativeSource}"),
                    Font = _fontBrandStats,
                    ForeColor = Color.FromArgb(98, 110, 128),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 5)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblAuth, 0, row++);
            }

            // Row 5: Footer Action Buttons (Ignore | Add to Dictionary | Lookup in Dictionary)
            var footerGrid = new FastPercentRowPanel(32F, 38F, 30F)
            {
                Margin = new Padding(0, 2, 0, 0)
            };

            var btnIgnore = CreatePillButton(
                UiLocalizer.Pick("Ignore", "လျစ်လျူရှု"),
                Color.FromArgb(245, 247, 250),
                Color.FromArgb(74, 84, 98),
                Color.FromArgb(216, 222, 232),
                _fontBrandStats);
            btnIgnore.Dock = DockStyle.Fill;
            btnIgnore.Click += (s, e) => RequestIgnoreOnce?.Invoke(item);

            var btnAddDict = CreatePillButton(
                UiLocalizer.Pick("+ Add Word", "+ မှတ်သားမည်"),
                Color.FromArgb(245, 247, 250),
                Color.FromArgb(22, 74, 156),
                Color.FromArgb(204, 218, 242),
                _fontBrandStats);
            btnAddDict.Dock = DockStyle.Fill;
            btnAddDict.Enabled = issue.CanAddToCustomDictionary;
            btnAddDict.Click += (s, e) => RequestAddToDictionary?.Invoke(item);

            var btnLookup = CreatePillButton(
                UiLocalizer.Pick("Dictionary", "အဘိဓာန်ကြည့်"),
                Color.FromArgb(245, 247, 250),
                Color.FromArgb(16, 102, 52),
                Color.FromArgb(196, 226, 206),
                _fontBrandStats);
            btnLookup.Dock = DockStyle.Fill;
            btnLookup.Click += (s, e) => OpenDictionaryWithQuery(hasSuggestion ? topSuggested : issue.OriginalText);

            footerGrid.Controls.Add(btnIgnore);
            footerGrid.Controls.Add(btnAddDict);
            footerGrid.Controls.Add(btnLookup);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(footerGrid, 0, row++);

            return card;
        }

        private static string TruncateGloss(string gloss, int maxChars)
        {
            string clean = gloss.Trim();
            if (clean.Length <= maxChars)
                return clean;
            return MyanmarSpellChecker.Core.Nlp.MyanmarSyllableBreaker.TruncateAtSyllableBoundary(clean, maxChars, "…");
        }

        private void UpdateExcelStickyDockedHeaderCounts(List<PresentationSlideIssue>? filteredOverride = null)
        {
            if (_lblDockedTitle == null || _btnDockedApply == null)
                return;

            var filtered = filteredOverride ?? GetFilteredUnresolvedIssues();
            var fixable = filtered.Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0).ToList();
            int checkedCount = 0;
            foreach (var it in fixable)
            {
                var st = GetOrInitExcelDockedState(it);
                if (st.Checked && !string.IsNullOrEmpty(st.Chosen))
                    checkedCount++;
            }

            _lblDockedTitle.Text = UiLocalizer.Pick(
                $"⇥ Docked Review ({checkedCount}/{fixable.Count})",
                $"⇥ ဘေးကွက် စိစစ်မှု ({checkedCount}/{fixable.Count})");

            _btnDockedApply.Enabled = checkedCount > 0;
            _btnDockedApply.Text = checkedCount > 0
                ? UiLocalizer.Pick($"✓ Apply {checkedCount} Selected Fix(es)", $"✓ ရွေးထားသော ({checkedCount}) ခုကို ပြင်ဆင်မည်")
                : UiLocalizer.Pick("Select items below to apply", "အောက်ပါစာရင်းမှ ရွေးချယ်ပါ");
            _btnDockedApply.BackColor = checkedCount > 0 ? Color.FromArgb(183, 71, 42) : Color.FromArgb(182, 194, 186);
            _btnDockedApply.FlatAppearance.BorderColor = checkedCount > 0 ? Color.FromArgb(12, 86, 44) : Color.FromArgb(170, 182, 174);
        }

        private void SetAllExcelDockedCheckStates(bool sureOnly, bool checkAll)
        {
            var fixable = GetFilteredUnresolvedIssues()
                .Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0)
                .ToList();
            foreach (var it in fixable)
            {
                var st = GetOrInitExcelDockedState(it);
                bool nextCheck = sureOnly ? it.Issue.IsHighCertaintyPrecheck : checkAll;
                _dockedStateByItem[it] = (nextCheck, st.Chosen);
            }
            RenderProofingCards();
        }

        private void ApplyExcelDockedBatchFromStickyBar()
        {
            var fixable = GetFilteredUnresolvedIssues()
                .Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0)
                .ToList();
            var approved = new Dictionary<PresentationSlideIssue, string>();
            foreach (var it in fixable)
            {
                var st = GetOrInitExcelDockedState(it);
                if (st.Checked && !string.IsNullOrEmpty(st.Chosen))
                    approved[it] = st.Chosen;
            }
            if (approved.Count > 0)
            {
                RequestApplyDockedBatch?.Invoke(approved);
            }
        }

        private void PopOutFromExcelStickyDockedBar()
        {
            var fixable = GetFilteredUnresolvedIssues()
                .Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0)
                .ToList();
            var snapshot = fixable.Select(it =>
            {
                var st = GetOrInitExcelDockedState(it);
                return new FixPreviewEntry<PresentationSlideIssue>(
                    it,
                    it.DisplayLocation,
                    it.CellSnapshotText,
                    it.Issue)
                {
                    IsChecked = st.Checked,
                    ChosenReplacement = st.Chosen
                };
            }).ToList();

            ExitDockedChecklistMode();
            RequestPopOutDockedChecklist?.Invoke(snapshot);
        }

        private void RebuildSingleExcelDockedCardInPlace(PresentationSlideIssue item)
        {
            if (!_cardByItem.TryGetValue(item, out var oldCard) || oldCard.IsDisposed)
            {
                RenderProofingCards();
                return;
            }

            int idx = _cardsPanel.Controls.GetChildIndex(oldCard, throwException: false);
            if (idx < 0)
            {
                RenderProofingCards();
                return;
            }

            int cardWidth = oldCard.Width > 80 ? oldCard.Width : GetTargetCardWidth(reserveScrollbarForMultipleCards: true);
            BeginCardsBatchUpdate();
            try
            {
                Control newCard = BuildExcelDockedChecklistRowCard(item, cardWidth);
                _cardsPanel.Controls.Add(newCard);
                _cardsPanel.Controls.SetChildIndex(newCard, idx);
                _cardsPanel.Controls.Remove(oldCard);
                oldCard.Dispose();
                _cardByItem[item] = newCard;
            }
            finally
            {
                EndCardsBatchUpdate();
            }

            UpdateExcelStickyDockedHeaderCounts();
        }

        private Control BuildExcelDockedChecklistRowCard(PresentationSlideIssue item, int cardWidth)
        {
            int innerWrap = Math.Max(160, cardWidth - 26);
            var st = GetOrInitExcelDockedState(item);
            bool isReview = item.Issue.IsReviewNeeded;
            bool isExpanded = _expandedDockedItems.Contains(item);
            int sugCount = item.Issue.Suggestions?.Count ?? 0;

            Color accent = st.Checked
                ? Color.FromArgb(183, 71, 42)
                : (isReview ? Color.FromArgb(218, 162, 0) : Color.FromArgb(164, 178, 170));
            var card = CreateStyledCardContainer(cardWidth, accent, item);
            card.Margin = new Padding(0, 0, 0, 6);
            card.Padding = new Padding(10, 7, 9, 7);
            int r = 0;

            Action jumpToCell = () =>
            {
                _selectedItem = item;
                RequestSelectIssueInSheet?.Invoke(item);
                foreach (var kv in _cardByItem.Values)
                    kv.Invalidate();
            };

            // Row 1: Clean Cell Location Header + Certainty Pill
            var metaRow = new FastPercentRowPanel(74F, 26F)
            {
                Margin = new Padding(0, 0, 0, 4)
            };

            var lblLoc = new Label
            {
                Text = $"▸ {item.DisplayLocation}",
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(16, 86, 48),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Fill,
                Height = MeasureSingleLineLabelHeight(_fontBrandStats, 4),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            lblLoc.Click += (s, e) => jumpToCell();

            var lblCert = new Label
            {
                Text = isReview
                    ? UiLocalizer.Pick("⚠ Review", "⚠ စိစစ်ရန်")
                    : UiLocalizer.Pick("✓ Sure", "✓ သေချာ"),
                Font = _fontBrandStats,
                ForeColor = isReview ? Color.FromArgb(133, 90, 0) : Color.FromArgb(16, 102, 52),
                BackColor = isReview ? Color.FromArgb(254, 249, 195) : Color.FromArgb(228, 247, 235),
                AutoSize = true,
                Dock = DockStyle.Right,
                Padding = new Padding(5, 1, 5, 1),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            lblCert.Click += (s, e) => jumpToCell();

            metaRow.Controls.Add(lblLoc);
            metaRow.Controls.Add(lblCert);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(metaRow, 0, r++);

            // Row 2: Highlighted Visual Diff Box (Click to Jump!)
            string visOrig = item.Issue.FormatVisualOriginalText(st.Chosen);
            Color diffBg = st.Checked
                ? Color.FromArgb(236, 250, 241)
                : (isReview ? Color.FromArgb(254, 249, 195) : Color.FromArgb(245, 248, 246));

            var lblDiff = new Label
            {
                Text = $"{(st.Checked ? "☑" : "☐")}  \"{visOrig}\"   ➔   \"{st.Chosen}\"",
                Font = _fontSmallBold,
                ForeColor = st.Checked
                    ? Color.FromArgb(14, 58, 34)
                    : (isReview ? Color.FromArgb(124, 82, 0) : Color.FromArgb(72, 84, 78)),
                BackColor = diffBg,
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Padding = new Padding(7, 5, 7, 5),
                Margin = new Padding(0, 0, 0, 5),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            lblDiff.Click += (s, e) => jumpToCell();
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblDiff, 0, r++);

            // Row 3: Action & Adjust Strip
            var rowBtns = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            var btnToggle = CreatePillButton(
                st.Checked ? UiLocalizer.Pick("☑ Selected", "☑ ရွေးထား") : UiLocalizer.Pick("☐ Select", "☐ ရွေးမည်"),
                st.Checked ? Color.FromArgb(224, 245, 233) : Color.White,
                st.Checked ? Color.FromArgb(16, 102, 54) : Color.FromArgb(68, 80, 74),
                Color.FromArgb(182, 206, 192),
                _fontSmall);
            btnToggle.AutoSize = true;
            btnToggle.Dock = DockStyle.None;
            btnToggle.Click += (s, e) =>
            {
                var cur = GetOrInitExcelDockedState(item);
                _dockedStateByItem[item] = (!cur.Checked, cur.Chosen);
                RebuildSingleExcelDockedCardInPlace(item);
            };

            var btnFixOne = CreatePillButton(
                UiLocalizer.Pick("✓ Fix", "✓ ပြင်မည်"),
                Color.FromArgb(183, 71, 42), Color.White, Color.FromArgb(12, 88, 44), _fontSmallBold);
            btnFixOne.AutoSize = true;
            btnFixOne.Dock = DockStyle.None;
            btnFixOne.Click += (s, e) =>
            {
                var cur = GetOrInitExcelDockedState(item);
                if (!string.IsNullOrEmpty(cur.Chosen))
                {
                    RequestApplySuggestion?.Invoke(item, cur.Chosen);
                }
            };

            var btnIgnore = CreatePillButton(
                UiLocalizer.Pick("Ignore", "ကျော်"),
                Color.FromArgb(245, 248, 246), Color.FromArgb(82, 94, 86), Color.FromArgb(208, 218, 212), _fontSmall);
            btnIgnore.AutoSize = true;
            btnIgnore.Dock = DockStyle.None;
            btnIgnore.Click += (s, e) => RequestIgnoreOnce?.Invoke(item);

            string adjustLabel = sugCount > 1
                ? UiLocalizer.Pick(isExpanded ? $"▴ Adjust ({sugCount})" : $"✎ Adjust ({sugCount}) ▾", isExpanded ? $"▴ ပြင်ရွေး ({sugCount})" : $"✎ ပြင်ရွေး ({sugCount}) ▾")
                : UiLocalizer.Pick(isExpanded ? "▴ Adjust" : "✎ Adjust ▾", isExpanded ? "▴ ပြင်ရွေး" : "✎ ပြင်ရွေး ▾");

            var btnAdjust = CreatePillButton(
                adjustLabel,
                isExpanded ? Color.FromArgb(224, 244, 232) : Color.FromArgb(242, 248, 244),
                Color.FromArgb(16, 92, 50),
                Color.FromArgb(176, 214, 190),
                _fontSmall);
            btnAdjust.AutoSize = true;
            btnAdjust.Dock = DockStyle.None;
            btnAdjust.Click += (s, e) =>
            {
                if (_expandedDockedItems.Contains(item))
                    _expandedDockedItems.Remove(item);
                else
                    _expandedDockedItems.Add(item);
                RebuildSingleExcelDockedCardInPlace(item);
            };

            rowBtns.Controls.AddRange(new Control[] { btnToggle, btnFixOne, btnIgnore, btnAdjust });
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(rowBtns, 0, r++);

            // Row 4: Expandable Inline Adjustment Drawer (Choices + Custom TextBox + Cell Context/Rule)
            if (isExpanded)
            {
                var drawer = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    BackColor = Color.FromArgb(246, 251, 248),
                    Padding = new Padding(7, 6, 7, 6),
                    Margin = new Padding(0, 5, 0, 0)
                };
                drawer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                int dr = 0;
                int drawerWrap = Math.Max(140, innerWrap - 16);

                if (item.Issue.Suggestions != null && item.Issue.Suggestions.Count > 0)
                {
                    var optFlow = new FlowLayoutPanel
                    {
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        FlowDirection = FlowDirection.LeftToRight,
                        WrapContents = true,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0, 0, 0, 4)
                    };

                    int maxOpt = Math.Min(6, item.Issue.Suggestions.Count);
                    for (int oi = 0; oi < maxOpt; oi++)
                    {
                        var cand = item.Issue.Suggestions[oi];
                        bool isCur = string.Equals(st.Chosen, cand.Replacement, StringComparison.Ordinal);
                        var btnOpt = CreatePillButton(
                            isCur ? $"✓ {cand.Replacement}" : cand.Replacement,
                            isCur ? Color.FromArgb(220, 246, 231) : Color.White,
                            isCur ? Color.FromArgb(14, 96, 48) : Color.FromArgb(24, 58, 40),
                            isCur ? Color.FromArgb(132, 202, 162) : Color.FromArgb(196, 214, 204),
                            isCur ? _fontSmallBold : _fontSmall);
                        btnOpt.AutoSize = true;
                        btnOpt.Dock = DockStyle.None;
                        string repCopy = cand.Replacement;
                        btnOpt.Click += (s, e) =>
                        {
                            _dockedStateByItem[item] = (true, repCopy);
                            RebuildSingleExcelDockedCardInPlace(item);
                        };
                        optFlow.Controls.Add(btnOpt);
                    }

                    drawer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    drawer.Controls.Add(optFlow, 0, dr++);
                }

                // Inline Custom Replacement Box + [Set] (Formula-Safe cell value edit)
                var customGrid = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = new Padding(0, 0, 0, 4)
                };
                customGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74F));
                customGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
                customGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var txtCustom = new TextBox
                {
                    Text = st.Chosen,
                    Font = _fontSmall,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1)
                };
                var btnSetCustom = CreatePillButton(
                    UiLocalizer.Pick("✓ Set", "✓ သုံးမည်"),
                    Color.FromArgb(183, 71, 42), Color.White, Color.FromArgb(12, 88, 44), _fontSmallBold);
                btnSetCustom.Click += (s, e) =>
                {
                    string typed = (txtCustom.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(typed))
                    {
                        _dockedStateByItem[item] = (true, typed);
                        RebuildSingleExcelDockedCardInPlace(item);
                    }
                };
                txtCustom.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        btnSetCustom.PerformClick();
                    }
                };
                customGrid.Controls.Add(txtCustom, 0, 0);
                customGrid.Controls.Add(btnSetCustom, 1, 0);
                drawer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                drawer.Controls.Add(customGrid, 0, dr++);

                string ctxSnippet = item.BuildContextSnippet(28).Replace("\r", " ").Replace("\n", " ").Trim();
                string noteText = !string.IsNullOrWhiteSpace(ctxSnippet)
                    ? $"{ctxSnippet}\n• {item.Issue.Message}"
                    : (item.Issue.Message ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(noteText))
                {
                    var lblNote = new Label
                    {
                        Text = noteText,
                        Font = _fontBrandStats,
                        ForeColor = Color.FromArgb(62, 78, 70),
                        AutoSize = true,
                        MaximumSize = new Size(drawerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0),
                        UseMnemonic = false
                    };
                    drawer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    drawer.Controls.Add(lblNote, 0, dr++);
                }

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(drawer, 0, r++);
            }

            return card;
        }
    }
}
