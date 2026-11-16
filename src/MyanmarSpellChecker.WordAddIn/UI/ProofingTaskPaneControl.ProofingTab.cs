using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.WordAddIn.Interop;

namespace MyanmarSpellChecker.WordAddIn.UI
{
    public sealed partial class ProofingTaskPaneControl
    {
        private const int ProofingCardBatchSize = 30;
        private const int EcoProofingCardBatchSize = 20;
        private const int MaxMountedWordCards = 90; // Hard Win32 HWND cap (max 3 batches mounted at once)
        private int _visibleWindowStart;
        private int _visibleIssueCardLimit = ProofingCardBatchSize;
        private Control? _loadPreviousCard;
        private Control? _loadMoreCard;
        private bool _isDockedChecklistMode;
        private readonly Dictionary<DocumentProofingItem, (bool Checked, string Chosen)> _dockedStateByItem =
            new Dictionary<DocumentProofingItem, (bool Checked, string Chosen)>();
        private readonly HashSet<DocumentProofingItem> _expandedDockedItems =
            new HashSet<DocumentProofingItem>();

        public bool IsDockedChecklistMode => _isDockedChecklistMode;

        private void ToggleSlimTaskPaneChrome()
        {
            _isSlimHeader = !_isSlimHeader;
            var prefs = MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Load("word");
            prefs.SlimTaskPaneHeader = _isSlimHeader;
            prefs.HasSlimHeaderPreference = true;
            MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Save("word", prefs);
            UpdateSlimButtonChrome();
            SyncCompactChromeAndDockedVisibility();
        }

        private void UpdateSlimButtonChrome()
        {
            if (_btnSlimHeader == null)
                return;
            _btnSlimHeader.Text = _isSlimHeader
                ? UiLocalizer.Pick("▾ Full", "▾ အပြည့်")
                : UiLocalizer.Pick("▴ Slim", "▴ ကျစ်လျစ်");
            _btnSlimHeader.BackColor = _isSlimHeader
                ? Color.FromArgb(224, 240, 255)
                : Color.FromArgb(244, 247, 252);
            _toolTip.SetToolTip(
                _btnSlimHeader,
                UiLocalizer.Pick(
                    "Toggle compact laptop layout (hides secondary hint rows so more cards fit on screen)",
                    "လက်ပ်တော့ မျက်နှာပြင်ငယ်များအတွက် ကျစ်လျစ်သောမြင်ကွင်း ပြောင်းရန်"));
        }

        private void SyncCompactChromeAndDockedVisibility()
        {
            if (_proofingToolbar == null)
                return;

            _proofingToolbar.SuspendLayout();
            try
            {
                _lblBrandTitle.Visible = false;
                _lblLexiconStats.Visible = !_isSlimHeader && !_isDockedChecklistMode;
                _styleGrid.Visible = !_isDockedChecklistMode;
                _lblStyleHint.Visible = !_isSlimHeader && !_isDockedChecklistMode;
                _actionStrip.Visible = !_isDockedChecklistMode;
                _summaryAndReportRow.Visible = !_isDockedChecklistMode;
                _pnlStickyDockedBar.Visible = _isDockedChecklistMode;
            }
            finally
            {
                _proofingToolbar.ResumeLayout(true);
            }
            PerformLayout();
        }

        public void EnterDockedChecklistMode(IReadOnlyList<FixPreviewEntry<DocumentProofingItem>>? seedEntries = null)
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
            SyncCompactChromeAndDockedVisibility();
            ResetProofingPagination();
            RefreshProofingCardsFull();
        }

        public void ExitDockedChecklistMode()
        {
            if (!_isDockedChecklistMode)
                return;

            _isDockedChecklistMode = false;
            _expandedDockedItems.Clear();
            var prefs = MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Load("word");
            prefs.PreferDockedInTaskPane = false;
            MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Save("word", prefs);
            SyncCompactChromeAndDockedVisibility();
            ResetProofingPagination();
            RefreshProofingCardsFull();
        }

        private (bool Checked, string Chosen) GetOrInitDockedState(DocumentProofingItem item)
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

        private int ActiveProofingBatchSize => _lastEcoActive ? EcoProofingCardBatchSize : ProofingCardBatchSize;

        private void ResetProofingPagination()
        {
            _visibleWindowStart = 0;
            _visibleIssueCardLimit = ActiveProofingBatchSize;
        }

        private void ChangeStyleMode(ProofingStyleMode mode)
        {
            if (_currentStyleMode == mode)
                return;

            _currentStyleMode = mode;
            UpdateStyleModeButtons();
            SetScanningState(true);
            RequestChangeStyleMode?.Invoke(mode);
        }

        private void UpdateStyleModeButtons()
        {
            bool isGeneral = _currentStyleMode == ProofingStyleMode.GeneralStandard;

            _btnStyleGeneral.BackColor = isGeneral ? Color.FromArgb(22, 78, 164) : Color.FromArgb(240, 243, 249);
            _btnStyleGeneral.ForeColor = isGeneral ? Color.White : Color.FromArgb(55, 65, 80);
            _btnStyleGeneral.Font = isGeneral ? _fontSmallBold : _fontSmall;
            _btnStyleGeneral.FlatAppearance.BorderColor = isGeneral ? Color.FromArgb(22, 78, 164) : Color.FromArgb(214, 220, 232);

            _btnStyleFormal.BackColor = !isGeneral ? Color.FromArgb(22, 78, 164) : Color.FromArgb(240, 243, 249);
            _btnStyleFormal.ForeColor = !isGeneral ? Color.White : Color.FromArgb(55, 65, 80);
            _btnStyleFormal.Font = !isGeneral ? _fontSmallBold : _fontSmall;
            _btnStyleFormal.FlatAppearance.BorderColor = !isGeneral ? Color.FromArgb(22, 78, 164) : Color.FromArgb(214, 220, 232);

            _lblStyleHint.Text = isGeneral
                ? UiLocalizer.Pick(
                    "General Standard — Checks everyday prose & standard Myanmar orthography",
                    "အထွေထွေ စံစနစ် — အများသုံး စကားပြေနှင့် စံသတ်ပုံများကို စစ်ဆေးသည်")
                : UiLocalizer.Pick(
                    "Official / Literary — Also checks formal register (ပါတယ် ➔ ပါသည်, တွေ ➔ များ)",
                    "စာပေ / ရုံးသုံးစနစ် — အစိုးရရုံးသုံး သဒ္ဒါ (ပါတယ် ➔ ပါသည်၊ တွေ ➔ များ) ပါ စစ်ဆေးသည်");
        }

        private void SetFilterMode(IssueFilterMode mode)
        {
            if (_currentFilter == mode)
                return;

            _currentFilter = mode;
            _stepperCursor = -1;
            ResetProofingPagination();
            UpdateFilterButtonStyles();
            RefreshProofingCardsFull();
        }

        private void UpdateFilterButtonStyles()
        {
            int total = _allItems.Count(i => !i.IsResolved);
            int spellingCount = _allItems.Count(i => !i.IsResolved && !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning);
            int grammarCount = _allItems.Count(i => !i.IsResolved && i.Issue.IsGrammarOrStyle);
            int contextCount = _allItems.Count(i => !i.IsResolved && i.Issue.IsContextualWarning);

            static bool HasChoices(DocumentProofingItem i) =>
                !i.IsResolved && i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0;

            int fixableCount = _currentFilter switch
            {
                IssueFilterMode.SpellingOnly => _allItems.Count(i => HasChoices(i) && !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning),
                IssueFilterMode.GrammarOnly => _allItems.Count(i => HasChoices(i) && i.Issue.IsGrammarOrStyle),
                IssueFilterMode.ContextOnly => _allItems.Count(i => HasChoices(i) && i.Issue.IsContextualWarning),
                _ => _allItems.Count(HasChoices)
            };

            _btnFilterAll.Text = UiLocalizer.Pick($"All ({total})", $"အားလုံး ({total})");
            _btnFilterSpelling.Text = UiLocalizer.Pick($"Spelling ({spellingCount})", $"သတ်ပုံ ({spellingCount})");
            _btnFilterGrammar.Text = UiLocalizer.Pick($"Grammar ({grammarCount})", $"သဒ္ဒါ ({grammarCount})");
            _btnFilterContext.Text = UiLocalizer.Pick($"Context ({contextCount})", $"အနက် ({contextCount})");

            _btnFilterAll.BackColor = _currentFilter == IssueFilterMode.All
                ? Color.FromArgb(30, 65, 130) : Color.FromArgb(236, 240, 247);
            _btnFilterAll.ForeColor = _currentFilter == IssueFilterMode.All
                ? Color.White : Color.FromArgb(40, 50, 65);
            _btnFilterAll.Font = _currentFilter == IssueFilterMode.All ? _fontSmallBold : _fontSmall;

            _btnFilterSpelling.BackColor = _currentFilter == IssueFilterMode.SpellingOnly
                ? Color.FromArgb(192, 34, 34) : Color.FromArgb(253, 237, 236);
            _btnFilterSpelling.ForeColor = _currentFilter == IssueFilterMode.SpellingOnly
                ? Color.White : Color.FromArgb(168, 28, 28);
            _btnFilterSpelling.Font = _currentFilter == IssueFilterMode.SpellingOnly ? _fontSmallBold : _fontSmall;

            _btnFilterGrammar.BackColor = _currentFilter == IssueFilterMode.GrammarOnly
                ? Color.FromArgb(22, 86, 186) : Color.FromArgb(234, 243, 255);
            _btnFilterGrammar.ForeColor = _currentFilter == IssueFilterMode.GrammarOnly
                ? Color.White : Color.FromArgb(22, 78, 168);
            _btnFilterGrammar.Font = _currentFilter == IssueFilterMode.GrammarOnly ? _fontSmallBold : _fontSmall;

            _btnFilterContext.BackColor = _currentFilter == IssueFilterMode.ContextOnly
                ? Color.FromArgb(212, 132, 12) : Color.FromArgb(255, 246, 226);
            _btnFilterContext.ForeColor = _currentFilter == IssueFilterMode.ContextOnly
                ? Color.White : Color.FromArgb(168, 98, 6);
            _btnFilterContext.Font = _currentFilter == IssueFilterMode.ContextOnly ? _fontSmallBold : _fontSmall;

            _btnFixAll.Enabled = fixableCount > 0;
            if (_currentFilter == IssueFilterMode.ContextOnly)
            {
                _btnFixAll.Text = fixableCount > 0
                    ? UiLocalizer.Pick($"Review & Fix ({fixableCount})", $"စိစစ်ပြင် ({fixableCount})")
                    : UiLocalizer.Pick("Review & Fix", "စိစစ်ပြင်မည်");
            }
            else if (_currentFilter == IssueFilterMode.SpellingOnly)
            {
                _btnFixAll.Text = fixableCount > 0
                    ? UiLocalizer.Pick($"Review & Fix ({fixableCount})", $"စိစစ်ပြင် ({fixableCount})")
                    : UiLocalizer.Pick("Fix Spelling", "သတ်ပုံပြင်မည်");
            }
            else if (_currentFilter == IssueFilterMode.GrammarOnly)
            {
                _btnFixAll.Text = fixableCount > 0
                    ? UiLocalizer.Pick($"Review & Fix ({fixableCount})", $"စိစစ်ပြင် ({fixableCount})")
                    : UiLocalizer.Pick("Fix Grammar", "သဒ္ဒါပြင်မည်");
            }
            else
            {
                _btnFixAll.Text = fixableCount > 0
                    ? UiLocalizer.Pick($"Review & Fix ({fixableCount})", $"စိစစ်ပြင် ({fixableCount})")
                    : UiLocalizer.Pick("Review & Fix", "စိစစ်ပြင်မည်");
            }
            _btnFixAll.BackColor = fixableCount > 0 ? Color.FromArgb(22, 132, 68) : Color.FromArgb(224, 230, 238);
            _btnFixAll.ForeColor = fixableCount > 0 ? Color.White : Color.FromArgb(120, 128, 140);
            _btnFixAll.FlatAppearance.BorderColor = fixableCount > 0 ? Color.FromArgb(18, 114, 58) : Color.FromArgb(210, 216, 226);

            bool hasActive = GetActiveFilteredItems().Count > 0;
            _btnPrevIssue.Enabled = hasActive;
            _btnNextIssue.Enabled = hasActive;
        }

        private List<DocumentProofingItem> GetActiveFilteredItems()
        {
            var active = _allItems.Where(i => !i.IsResolved);
            if (_currentFilter == IssueFilterMode.SpellingOnly)
                active = active.Where(i => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning);
            else if (_currentFilter == IssueFilterMode.GrammarOnly)
                active = active.Where(i => i.Issue.IsGrammarOrStyle);
            else if (_currentFilter == IssueFilterMode.ContextOnly)
                active = active.Where(i => i.Issue.IsContextualWarning);
            return active.ToList();
        }

        private void StepThroughIssues(int delta)
        {
            var active = GetActiveFilteredItems();
            if (active.Count == 0)
                return;

            if (_stepperCursor < 0 || _stepperCursor >= active.Count)
            {
                _stepperCursor = delta >= 0 ? 0 : active.Count - 1;
            }
            else
            {
                _stepperCursor = (_stepperCursor + delta + active.Count) % active.Count;
            }

            DocumentProofingItem target = active[_stepperCursor];
            EnsureCardsRenderedThroughIndex(active, _stepperCursor);
            HighlightSelectedCard(target, scrollIntoView: true);
            RequestSelectIssueInDocument?.Invoke(target);
        }

        public void HighlightIssueFromDocumentCursor(DocumentProofingItem? target)
        {
            if (_activeTab != TaskPaneWorkspaceTab.Proofing)
                return;

            if (target != null && !_cardByItem.ContainsKey(target))
            {
                var active = GetActiveFilteredItems();
                int idx = active.IndexOf(target);
                if (idx >= 0)
                {
                    EnsureCardsRenderedThroughIndex(active, idx);
                }
            }

            HighlightSelectedCard(target, scrollIntoView: target != null);
        }

        private void HighlightSelectedCard(DocumentProofingItem? target, bool scrollIntoView)
        {
            DocumentProofingItem? previous = _selectedItem;
            _selectedItem = target;

            if (previous != null && _cardByItem.TryGetValue(previous, out var prevCtrl) && !prevCtrl.IsDisposed)
            {
                prevCtrl.Invalidate();
            }

            if (target != null && _cardByItem.TryGetValue(target, out var nextCtrl) && !nextCtrl.IsDisposed)
            {
                nextCtrl.Invalidate();
                if (scrollIntoView)
                {
                    _cardsPanel.ScrollControlIntoView(nextCtrl);
                }
            }
        }

        // ================= PROOFING CARDS & IN-PLACE RESOLUTION =================

        private void UpdateProofingSummaryText(int activeCount)
        {
            if (_isScanning)
                return;

            if (_lastScanOutcome != null && _lastScanOutcome.WasCancelled)
            {
                _lblSummary.Text = activeCount > 0
                    ? UiLocalizer.Pick(
                        $"⏹ Scan stopped — showing {activeCount} issue(s) found so far",
                        $"⏹ စစ်ဆေးမှုကို ရပ်တန့်ထားသည် — တွေ့ရှိပြီးသား ({activeCount}) ခု")
                    : UiLocalizer.Pick(
                        "⏹ Scan stopped — click [Scan Document] to scan again",
                        "⏹ စစ်ဆေးမှုကို ရပ်တန့်ထားသည် — ထပ်စစ်ရန် [စာတမ်းစစ်မည်] ကိုနှိပ်ပါ");
                _lblSummary.ForeColor = Color.FromArgb(158, 86, 6);
                return;
            }

            if (_lastScanOutcome != null && _lastScanOutcome.WasIssueCapReached && activeCount > 0)
            {
                _lblSummary.Text = UiLocalizer.Pick(
                    $"⚡ Capped at {activeCount:N0} issues to keep Word responsive",
                    $"⚡ စက်မလေးစေရန် အမှားအများဆုံး ({activeCount:N0}) ခုအထိသာ ကန့်သတ်ပြသထားသည်");
                _lblSummary.ForeColor = Color.FromArgb(168, 82, 8);
                return;
            }

            if (_lastScanOutcome != null && _lastScanOutcome.WasPartialAutoScan)
            {
                if (activeCount == 0)
                {
                    _lblSummary.Text = UiLocalizer.Pick(
                        $"⚡ Nearby {_lastScanOutcome.ScannedParagraphCount} paras clean — click [Scan Doc] for all",
                        $"⚡ အနီး ({_lastScanOutcome.ScannedParagraphCount}) ပိုဒ် သန့်သည် — အားလုံးစစ်ရန် [စာတမ်းစစ်]");
                    _lblSummary.ForeColor = Color.FromArgb(20, 118, 62);
                }
                else
                {
                    _lblSummary.Text = UiLocalizer.Pick(
                        $"⚡ {activeCount} nearby — click [Scan Doc] for full scan",
                        $"⚡ အနီး၌ ({activeCount}) ခု — တစ်ဖိုင်လုံးစစ်ရန် [စာတမ်းစစ်]");
                    _lblSummary.ForeColor = Color.FromArgb(182, 42, 24);
                }
                return;
            }

            if (activeCount == 0)
            {
                string modeLabel = _currentStyleMode == ProofingStyleMode.GeneralStandard
                    ? UiLocalizer.Pick("General Standard", "အထွေထွေ စံစနစ်")
                    : UiLocalizer.Pick("Official / Literary", "စာပေ / ရုံးသုံးစနစ်");
                _lblSummary.Text = UiLocalizer.Pick(
                    $"✓ No issues found ({modeLabel})",
                    $"✓ အမှားအယွင်း မတွေ့ရှိပါ ({modeLabel})");
                _lblSummary.ForeColor = Color.FromArgb(20, 124, 58);
            }
            else
            {
                bool hasConfirmedRed = _allItems.Any(i => !i.IsResolved && !i.Issue.IsReviewNeeded && !i.Issue.IsGrammarOrStyle);
                _lblSummary.Text = UiLocalizer.Pick(
                    $"Found {activeCount} issue(s) — click any card to jump directly in document",
                    $"တွေ့ရှိချက် ({activeCount}) ခု — ကတ်ကိုနှိပ်၍ စာတမ်း၌ တိုက်ရိုက်ကြည့်ရှုပြင်ဆင်နိုင်သည်");
                _lblSummary.ForeColor = hasConfirmedRed
                    ? Color.FromArgb(186, 34, 34)
                    : Color.FromArgb(146, 100, 0);
            }
        }

        private Dictionary<string, int> BuildSameErrorCountsMap()
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

        private void RefreshProofingCardsFull()
        {
            SyncCompactChromeAndDockedVisibility();
            UpdateWorkspaceTabStyles();
            UpdateFilterButtonStyles();

            var active = GetActiveFilteredItems();
            UpdateProofingSummaryText(active.Count);
            if (_isDockedChecklistMode)
            {
                UpdateStickyDockedHeaderCounts(active);
            }

            BeginCardsBatchUpdate();
            try
            {
                DisposeAndClearCardsPanel();
                _cardByItem.Clear();
                _loadPreviousCard = null;
                _loadMoreCard = null;

                int cardWidth = GetTargetCardWidth(reserveScrollbarForMultipleCards: active.Count >= 3);
                if (active.Count == 0)
                {
                    _visibleWindowStart = 0;
                    Control emptyCard = BuildEmptyStateCard(cardWidth);
                    _cardsPanel.Controls.Add(emptyCard);
                    _lastAppliedCardWidth = cardWidth;
                    _lastAppliedControlCount = _cardsPanel.Controls.Count;
                    return;
                }

                int batchSize = ActiveProofingBatchSize;
                if (_visibleWindowStart >= active.Count)
                {
                    _visibleWindowStart = Math.Max(0, ((active.Count - 1) / batchSize) * batchSize);
                }

                var batchControls = new List<Control>(Math.Min(MaxMountedWordCards + 3, active.Count + 3));
                if (_currentStyleMode == ProofingStyleMode.FormalOfficial && _currentFilter != IssueFilterMode.ContextOnly)
                {
                    int colloqStyleCount = _allItems.Count(x => !x.IsResolved && x.Issue.IssueType == ProofingIssueType.OfficialStyleGrammarError);
                    int hardSpellCount = _allItems.Count(x => !x.IsResolved && !x.Issue.IsGrammarOrStyle && !x.Issue.IsContextualWarning);
                    if (colloqStyleCount >= 8 && colloqStyleCount >= hardSpellCount * 4)
                    {
                        batchControls.Add(BuildColloquialStoryHintCard(cardWidth, colloqStyleCount));
                    }
                }

                if (_visibleWindowStart > 0)
                {
                    _loadPreviousCard = BuildLoadPreviousIssuesCard(_visibleWindowStart, cardWidth);
                    batchControls.Add(_loadPreviousCard);
                }

                int windowCount = Math.Min(MaxMountedWordCards, Math.Max(batchSize, _visibleIssueCardLimit));
                int endExclusive = Math.Min(active.Count, _visibleWindowStart + windowCount);

                if (_isDockedChecklistMode)
                {
                    for (int i = _visibleWindowStart; i < endExclusive; i++)
                    {
                        DocumentProofingItem item = active[i];
                        if (item.Issue.Suggestions == null || item.Issue.Suggestions.Count == 0)
                            continue;
                        Control rowCard = BuildDockedChecklistRowCard(item, cardWidth);
                        _cardByItem[item] = rowCard;
                        batchControls.Add(rowCard);
                    }
                }
                else
                {
                    var sameErrorCounts = BuildSameErrorCountsMap();
                    for (int i = _visibleWindowStart; i < endExclusive; i++)
                    {
                        DocumentProofingItem item = active[i];
                        Control card = BuildIssueCard(item, i + 1, cardWidth, sameErrorCounts);
                        _cardByItem[item] = card;
                        batchControls.Add(card);
                    }
                }

                if (active.Count > endExclusive)
                {
                    _loadMoreCard = BuildLoadMoreIssuesCard(active.Count - endExclusive, active.Count, cardWidth);
                    batchControls.Add(_loadMoreCard);
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

        private void EnsureCardsRenderedThroughIndex(List<DocumentProofingItem> active, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= active.Count)
                return;
            if (_cardByItem.ContainsKey(active[targetIndex]))
                return;

            int batchSize = ActiveProofingBatchSize;
            // If targetIndex is within current window's 3-batch cap, append in place; otherwise slide the bounded window!
            if (targetIndex >= _visibleWindowStart && (targetIndex - _visibleWindowStart) < MaxMountedWordCards)
            {
                int desiredWindowCount = Math.Min(
                    MaxMountedWordCards,
                    (((targetIndex - _visibleWindowStart) / batchSize) + 1) * batchSize);
                AppendCardsUpToWindowCount(active, desiredWindowCount);
            }
            else
            {
                _visibleWindowStart = (targetIndex / batchSize) * batchSize;
                _visibleIssueCardLimit = batchSize * 2;
                RefreshProofingCardsFull();
            }
        }

        private void AppendCardsUpToWindowCount(List<DocumentProofingItem> active, int desiredWindowCount)
        {
            int cappedWindow = Math.Min(MaxMountedWordCards, desiredWindowCount);
            if (cappedWindow <= _cardByItem.Count)
                return;

            _visibleIssueCardLimit = cappedWindow;
            int cardWidth = GetTargetCardWidth(reserveScrollbarForMultipleCards: active.Count >= 3);
            var sameErrorCounts = BuildSameErrorCountsMap();

            BeginCardsBatchUpdate();
            try
            {
                if (_loadMoreCard != null)
                {
                    _cardsPanel.Controls.Remove(_loadMoreCard);
                    _loadMoreCard.Dispose();
                    _loadMoreCard = null;
                }

                int endExclusive = Math.Min(active.Count, _visibleWindowStart + _visibleIssueCardLimit);
                var addedCards = new List<Control>(endExclusive - _visibleWindowStart + 1);
                for (int i = _visibleWindowStart; i < endExclusive; i++)
                {
                    DocumentProofingItem item = active[i];
                    if (_cardByItem.ContainsKey(item))
                        continue;

                    Control card = BuildIssueCard(item, i + 1, cardWidth, sameErrorCounts);
                    _cardByItem[item] = card;
                    addedCards.Add(card);
                }

                if (active.Count > endExclusive)
                {
                    _loadMoreCard = BuildLoadMoreIssuesCard(active.Count - endExclusive, active.Count, cardWidth);
                    addedCards.Add(_loadMoreCard);
                }

                if (addedCards.Count > 0)
                {
                    _cardsPanel.Controls.AddRange(addedCards.ToArray());
                }
                _lastAppliedCardWidth = cardWidth;
                _lastAppliedControlCount = _cardsPanel.Controls.Count;
            }
            finally
            {
                EndCardsBatchUpdate();
            }
        }

        private Control BuildColloquialStoryHintCard(int cardWidth, int colloqCount)
        {
            int innerWrap = Math.Max(180, cardWidth - 32);
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 98, 188), isSelectableItem: null);
            var lblInfo = new Label
            {
                Text = UiLocalizer.Pick(
                    $"ⓘ This document appears to use conversational prose (story/article) — switch to General Standard if you want to hide {colloqCount} blue formal-style marks.",
                    $"ⓘ ဤစာမူသည် အပြောစကားဟန် (ဝတ္ထု / ဆောင်းပါး) ဖြစ်နေပုံရပါသည် — ရုံးသုံးဟန် မျဉ်းပြာ ({colloqCount}) ခု မပြလိုပါက အထွေထွေ စံ သို့ ပြောင်းနိုင်ပါသည်။"),
                Font = _fontSmallBold,
                ForeColor = Color.FromArgb(24, 58, 112),
                BackColor = Color.FromArgb(236, 245, 255),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Padding = new Padding(7, 5, 7, 5),
                Margin = new Padding(0, 0, 0, 6)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblInfo, 0, 0);

            var btnSwitch = CreatePillButton(
                UiLocalizer.Pick("✓ Switch to General Standard Mode", "✓ အထွေထွေ စံ (စကားပြေစနစ်) သို့ ပြောင်းမည်"),
                Color.FromArgb(24, 90, 188),
                Color.White,
                Color.FromArgb(20, 76, 162),
                _fontSmallBold);
            btnSwitch.Click += (s, e) => ChangeStyleMode(ProofingStyleMode.GeneralStandard);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnSwitch, 0, 1);
            return card;
        }

        private Control BuildLoadPreviousIssuesCard(int previousCount, int cardWidth)
        {
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
            var btnPrev = CreatePillButton(
                UiLocalizer.Pick(
                    $"▴ Show Earlier Issues (#1 to #{previousCount})",
                    $"▴ ရှေ့ပိုင်း တွေ့ရှိချက် (#1 မှ #{previousCount}) ပြန်ကြည့်မည်"),
                Color.FromArgb(236, 244, 255),
                Color.FromArgb(18, 68, 152),
                Color.FromArgb(186, 210, 246),
                _fontSmallBold);
            btnPrev.Click += (s, e) =>
            {
                int batchSize = ActiveProofingBatchSize;
                _visibleWindowStart = Math.Max(0, _visibleWindowStart - batchSize);
                _visibleIssueCardLimit = Math.Min(MaxMountedWordCards, batchSize * 2);
                RefreshProofingCardsFull();
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnPrev, 0, 0);
            return card;
        }

        private Control BuildLoadMoreIssuesCard(int remainingCount, int totalCount, int cardWidth)
        {
            var card = CreateStyledCardContainer(cardWidth, Color.FromArgb(24, 90, 188), isSelectableItem: null);
            int batchSize = ActiveProofingBatchSize;
            int nextBatch = Math.Min(batchSize, remainingCount);
            var btnMore = CreatePillButton(
                UiLocalizer.Pick(
                    $"▾ Show Next {nextBatch} Issue(s)  •  Remaining ({remainingCount} / {totalCount})",
                    $"▾ နောက်ထပ် တွေ့ရှိချက် ({nextBatch}) ခု ဆက်လက်ဖော်ပြမည်  •  ကျန်ရှိ ({remainingCount} / {totalCount})"),
                Color.FromArgb(236, 244, 255),
                Color.FromArgb(18, 68, 152),
                Color.FromArgb(186, 210, 246),
                _fontSmallBold);
            btnMore.Click += (s, e) =>
            {
                int step = ActiveProofingBatchSize;
                var active = GetActiveFilteredItems();
                if (_cardByItem.Count + step <= MaxMountedWordCards)
                {
                    AppendCardsUpToWindowCount(active, _visibleIssueCardLimit + step);
                }
                else
                {
                    _visibleWindowStart = Math.Min(
                        Math.Max(0, active.Count - step),
                        _visibleWindowStart + step);
                    _visibleIssueCardLimit = MaxMountedWordCards;
                    RefreshProofingCardsFull();
                }
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(btnMore, 0, 0);
            return card;
        }

        /// <summary>
        /// Smoothly removes a single resolved card in-place without resetting scroll position or flickering remaining cards.
        /// </summary>
        private void ResolveSingleCardSmoothly(DocumentProofingItem item, Control card)
        {
            item.IsResolved = true;
            _cardByItem.Remove(item);
            if (ReferenceEquals(_selectedItem, item))
                _selectedItem = null;

            _cardsPanel.SuspendLayout();
            _cardsPanel.Controls.Remove(card);
            card.Dispose();
            _cardsPanel.ResumeLayout();

            UpdateWorkspaceTabStyles();
            UpdateFilterButtonStyles();

            var remaining = GetActiveFilteredItems();
            UpdateProofingSummaryText(remaining.Count);
            if (_isDockedChecklistMode)
            {
                UpdateStickyDockedHeaderCounts(remaining);
            }
            if (remaining.Count == 0)
            {
                RefreshProofingCardsFull();
            }
            else if (_cardByItem.Count == 0 && remaining.Count > 0)
            {
                RefreshProofingCardsFull();
            }
        }

        private Control BuildEmptyStateCard(int cardWidth)
        {
            int innerWrap = Math.Max(180, cardWidth - 32);
            Color emerald = Color.FromArgb(22, 132, 68);

            var card = CreateStyledCardContainer(cardWidth, emerald, isSelectableItem: null);
            int row = 0;

            var lblHeadline = new Label
            {
                Text = UiLocalizer.Pick(
                    "✓ Spelling & Grammar Check Passed",
                    "✓ စာလုံးပေါင်းနှင့် သဒ္ဒါ စစ်ဆေးချက် ကောင်းမွန်ပါသည်"),
                Font = _fontBodyBold,
                ForeColor = emerald,
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(2, 2, 2, 2)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblHeadline, 0, row++);

            string modeDesc = _currentStyleMode == ProofingStyleMode.GeneralStandard
                ? UiLocalizer.Pick(
                    "Active Mode: General Standard — Live background proofing automatically checks as you type.",
                    "လက်ရှိစနစ်: အထွေထွေ စံသတ်ပုံစနစ် — စာရိုက်နေစဉ် အလိုအလျောက် ဆက်လက်စစ်ဆေးပေးနေပါသည်။")
                : UiLocalizer.Pick(
                    "Active Mode: Official / Literary — Orthography and formal ministry register have been checked.",
                    "လက်ရှိစနစ်: စာပေ / ရုံးသုံး အရေးစကားစနစ် — စာလုံးပေါင်းနှင့် ရုံးသုံးသဒ္ဒါအားလုံး စစ်ဆေးပြီးဖြစ်ပါသည်။");

            var lblBody = new Label
            {
                Text = modeDesc,
                Font = _fontSmall,
                ForeColor = Color.FromArgb(65, 76, 92),
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(2, 1, 2, 2)
            };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblBody, 0, row++);

            var quickActions = new FastPercentRowPanel(52F, 48F)
            {
                Margin = new Padding(0)
            };

            var btnRescan = CreatePillButton(
                UiLocalizer.Pick("Scan Full Document", "စာတမ်းတစ်ခုလုံး ပြန်စစ်မည်"),
                Color.FromArgb(236, 244, 255), Color.FromArgb(20, 74, 158), Color.FromArgb(190, 212, 246), _fontSmallBold);
            btnRescan.Click += (s, e) =>
            {
                SetScanningState(true);
                RequestFullDocumentScan?.Invoke();
            };

            var btnGoDict = CreatePillButton(
                UiLocalizer.Pick("Open Dictionary ➔", "အဘိဓာန် ရှာဖွေရန် ➔"),
                Color.FromArgb(242, 245, 250), Color.FromArgb(48, 58, 74), Color.FromArgb(215, 222, 232), _fontSmall);
            btnGoDict.Click += (s, e) => FocusDictionarySearchBox();

            quickActions.Controls.Add(btnRescan);
            quickActions.Controls.Add(btnGoDict);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(quickActions, 0, row++);

            return card;
        }

        private Control BuildIssueCard(
            DocumentProofingItem item,
            int displayIndex,
            int cardWidth,
            Dictionary<string, int>? sameErrorCounts = null)
        {
            ProofingIssue issue = item.Issue;
            bool isGrammar = issue.IsGrammarOrStyle;
            bool isContextWarning = issue.IsContextualWarning;
            bool isReviewNeeded = issue.IsReviewNeeded;
            bool isEncoding = issue.IssueType == ProofingIssueType.ZawgyiEncodingError ||
                              issue.IssueType == ProofingIssueType.LookalikeCharacterError;
            int innerWrap = Math.Max(180, cardWidth - 32);

            Color accentColor = isReviewNeeded
                ? Color.FromArgb(218, 162, 0)
                : (isEncoding
                    ? Color.FromArgb(217, 138, 18)
                    : (isGrammar ? Color.FromArgb(26, 115, 232) : Color.FromArgb(217, 48, 37)));

            Color badgeTextColor = isReviewNeeded
                ? Color.FromArgb(138, 94, 0)
                : accentColor;

            var card = CreateStyledCardContainer(cardWidth, accentColor, isSelectableItem: item);
            int row = 0;

            Action jumpAction = () =>
            {
                HighlightSelectedCard(item, scrollIntoView: false);
                RequestSelectIssueInDocument?.Invoke(item);
            };

            // 1. Header Row: Category + Location Badge + #Index
            var topMetaRow = new FastPercentRowPanel(82F, 18F)
            {
                Margin = new Padding(0, 0, 0, 4)
            };

            string cardLoc = item.HasResolvedFriendlyLocation
                ? item.FormatFriendlyLocation()
                : $"{item.FormatSectionAndParagraphLabel()}, {item.FormatLineLabel()}";

            string categoryText = isContextWarning
                ? UiLocalizer.Pick($"⚠ Context Check  •  {cardLoc}", $"⚠ အနက်သံသယ  •  {cardLoc}")
                : (isReviewNeeded
                    ? UiLocalizer.Pick($"⚠ Review Needed  •  {cardLoc}", $"⚠ စိစစ်ရန်  •  {cardLoc}")
                    : (isEncoding
                        ? UiLocalizer.Pick($"● Unicode Standard  •  {cardLoc}", $"● ယူနီကုဒ် စံနှုန်း  •  {cardLoc}")
                        : (isGrammar
                            ? UiLocalizer.Pick($"● Grammar & Style  •  {cardLoc}", $"● သဒ္ဒါ / အရေးအသား  •  {cardLoc}")
                            : UiLocalizer.Pick($"● Spelling  •  {cardLoc}", $"● စာလုံးပေါင်းသတ်ပုံ  •  {cardLoc}"))));

            var lblBadge = new Label
            {
                Text = categoryText,
                ForeColor = badgeTextColor,
                Font = _fontSmallBold,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(2, 1, 2, 1),
                Cursor = Cursors.Hand
            };
            lblBadge.Click += (s, e) => jumpAction();

            var lblIndex = new Label
            {
                Text = $"#{displayIndex}",
                ForeColor = Color.FromArgb(128, 138, 152),
                Font = _fontBrandStats,
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = true,
                Dock = DockStyle.Right,
                Margin = new Padding(0),
                Padding = new Padding(0, 1, 2, 1),
                Cursor = Cursors.Hand
            };
            lblIndex.Click += (s, e) => jumpAction();

            topMetaRow.Controls.Add(lblBadge);
            topMetaRow.Controls.Add(lblIndex);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(topMetaRow, 0, row++);

            // 2. Highlighted Diff Banner: "Original" -> "Top Suggestion"
            SuggestionCandidate? topCand = issue.Suggestions.Count > 0 ? issue.Suggestions[0] : null;
            string visOrig = issue.FormatVisualOriginalText(topCand?.Replacement);
            string topReplacementPreview = topCand != null
                ? $"\"{visOrig}\"   ➔   \"{topCand.Replacement}\""
                : UiLocalizer.Pick($"Original: \"{visOrig}\"", $"မူရင်းစာသား: \"{visOrig}\"");

            Color diffBg = isReviewNeeded
                ? Color.FromArgb(254, 249, 195)
                : (isEncoding
                    ? Color.FromArgb(255, 248, 228)
                    : (isGrammar ? Color.FromArgb(240, 246, 255) : Color.FromArgb(254, 243, 242)));

            var lblOriginal = new Label
            {
                Text = topReplacementPreview,
                Font = _fontCardDiff,
                ForeColor = Color.FromArgb(22, 28, 40),
                BackColor = diffBg,
                AutoSize = true,
                MaximumSize = new Size(innerWrap, 0),
                Tag = WrapTag,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(8, 5, 8, 5),
                Cursor = Cursors.Hand
            };
            lblOriginal.Click += (s, e) => jumpAction();
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(lblOriginal, 0, row++);

            // 3. Selectable Replacement Choice Buttons FIRST (right below original -> replacement header!)
            if (issue.Suggestions.Count > 0)
            {
                var lblChooseHeader = new Label
                {
                    Text = issue.Suggestions.Count > 1
                        ? UiLocalizer.Pick($"Choose replacement ({issue.Suggestions.Count} options) —", $"ရွေးချယ်ပြင်ဆင်ရန် ({issue.Suggestions.Count} မျိုး) —")
                        : UiLocalizer.Pick("Suggested Correction —", "အကြံပြုထားသော စာလုံးပေါင်းအမှန် —"),
                    Font = _fontSmallBold,
                    ForeColor = isGrammar ? Color.FromArgb(20, 78, 168) : Color.FromArgb(20, 102, 54),
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 3),
                    Padding = new Padding(2, 0, 2, 0)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblChooseHeader, 0, row++);

                for (int sIdx = 0; sIdx < issue.Suggestions.Count; sIdx++)
                {
                    var candidate = issue.Suggestions[sIdx];
                    bool isPrimary = sIdx == 0;
                    Color baseBg = isGrammar
                        ? (isPrimary ? Color.FromArgb(228, 241, 255) : Color.FromArgb(242, 248, 255))
                        : (isPrimary ? Color.FromArgb(224, 246, 232) : Color.FromArgb(241, 250, 244));
                    Color hoverBg = isGrammar ? Color.FromArgb(210, 230, 255) : Color.FromArgb(202, 238, 216);
                    Color borderClr = isGrammar
                        ? (isPrimary ? Color.FromArgb(142, 186, 244) : Color.FromArgb(184, 210, 246))
                        : (isPrimary ? Color.FromArgb(118, 198, 148) : Color.FromArgb(168, 218, 186));

                    var btnFix = new WordWrapChoiceButton
                    {
                        Text = UiLocalizer.Pick($"✓ Apply  ➔  {candidate.DisplayLabel}", $"✓ ပြင်မည်  ➔  {candidate.DisplayLabel}"),
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        MaximumSize = new Size(innerWrap, 0),
                        Tag = WrapTag,
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding = new Padding(8, 5, 8, 5),
                        Margin = new Padding(0, 0, 0, 5),
                        BackColor = baseBg,
                        ForeColor = Color.FromArgb(18, 38, 62),
                        FlatStyle = FlatStyle.Flat,
                        Font = _fontSuggest,
                        Cursor = Cursors.Hand
                    };
                    btnFix.FlatAppearance.BorderColor = borderClr;
                    btnFix.FlatAppearance.MouseOverBackColor = hoverBg;

                    string replacement = candidate.Replacement;
                    btnFix.Click += (s, e) =>
                    {
                        RequestApplySuggestion?.Invoke(item, replacement);
                        ResolveSingleCardSmoothly(item, card);
                    };

                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(btnFix, 0, row++);
                }

                string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder((issue.OriginalText ?? string.Empty).Trim());
                int sameErrorCount = 0;
                if (!string.IsNullOrEmpty(origNorm))
                {
                    if (sameErrorCounts != null)
                        sameErrorCounts.TryGetValue(origNorm, out sameErrorCount);
                    else
                        sameErrorCount = _allItems.Count(x => !x.IsResolved && UnicodeNormalizer.NormalizeCanonicalOrder((x.Issue.OriginalText ?? string.Empty).Trim()) == origNorm);
                }

                if (sameErrorCount >= 2 && topCand != null && !string.IsNullOrEmpty(topCand.Replacement))
                {
                    string batchOrig = issue.OriginalText ?? string.Empty;
                    string batchRepl = topCand.Replacement;
                    var btnBatchSame = new WordWrapChoiceButton
                    {
                        Text = UiLocalizer.Pick(
                            $"✓✓ Fix all {sameErrorCount} matching occurrences  ➔  {batchRepl}",
                            $"✓✓ တူညီသောအမှား ({sameErrorCount}) ခုလုံး ပြင်မည်  ➔  {batchRepl}"),
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
                    _toolTip.SetToolTip(
                        btnBatchSame,
                        UiLocalizer.Pick(
                            "Replace all identical occurrences across the document in a single Ctrl+Z step",
                            "စာတမ်းတစ်ခုလုံးရှိ တူညီသော အမှားအားလုံးကို တစ်ချက်တည်းဖြင့် အစားထိုးပေးသည် (Ctrl+Z ပြန်ဆုတ်နိုင်သည်)"));
                    btnBatchSame.Click += (s, e) =>
                    {
                        RequestApplyBatchSuggestion?.Invoke(batchOrig, batchRepl);
                    };

                    card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    card.Controls.Add(btnBatchSame, 0, row++);
                }
            }

            // 4. Concise Issue Summary Message
            if (!string.IsNullOrWhiteSpace(issue.Message))
            {
                var lblMessage = new Label
                {
                    Text = issue.Message,
                    Font = _fontBody,
                    ForeColor = Color.FromArgb(52, 62, 78),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 0, 5),
                    Padding = new Padding(2, 2, 2, 2),
                    Cursor = Cursors.Hand
                };
                lblMessage.Click += (s, e) => jumpAction();
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblMessage, 0, row++);
            }

            // 5. Compact Orthographic/Abidan Note + Expandable Full Deep Reasoning
            var quickNoteLines = new List<string>(2);
            var deepReasoningLines = new List<string>(5);

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.DiagnosticDetail))
            {
                quickNoteLines.Add(UiLocalizer.Pick(
                    $"• Character/Syllable Diff: {topCand.DiagnosticDetail}",
                    $"• အက္ခရာ/ဝဏ္ဏ ကွာခြားချက်: {topCand.DiagnosticDetail}"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.AbidanSummary))
            {
                quickNoteLines.Add(UiLocalizer.Pick(
                    $"• Dictionary Note: {topCand.AbidanSummary}",
                    $"• အဘိဓာန်မှတ်ချက်: {topCand.AbidanSummary}"));
            }

            if (!string.IsNullOrWhiteSpace(issue.GrammaticalReasoning))
            {
                deepReasoningLines.Add(UiLocalizer.Pick(
                    $"• Analysis: {issue.GrammaticalReasoning}",
                    $"• သုံးသပ်ချက်: {issue.GrammaticalReasoning}"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.SyllableBreakdown))
            {
                string posText = DictionaryEntry.FormatLocalizedPosTag(topCand.PosTag);
                string authText = topCand.IsOfficialThatpon
                    ? UiLocalizer.Pick("Official Thatpon ✓", "သတ်ပုံကျမ်းဝင် ✓")
                    : UiLocalizer.Pick("Standard Usage", "ခေတ်သုံး စံသတ်ပုံ");
                deepReasoningLines.Add(UiLocalizer.Pick(
                    $"• Syllables: {topCand.SyllableBreakdown}  |  {posText}  |  {authText}",
                    $"• ဝဏ္ဏဖွဲ့စည်းပုံ: {topCand.SyllableBreakdown}  |  {posText}  |  {authText}"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.HomophoneSiblings))
            {
                deepReasoningLines.Add(UiLocalizer.Pick(
                    $"• Homophones: {topCand.HomophoneSiblings}",
                    $"• အသံတူသတိပြုရန်: {topCand.HomophoneSiblings}"));
            }

            if (!string.IsNullOrWhiteSpace(issue.AuthoritativeSource))
            {
                deepReasoningLines.Add(UiLocalizer.Pick(
                    $"• Reference: {issue.AuthoritativeSource}",
                    $"• ကျမ်းကိုးစံနှုန်း: {issue.AuthoritativeSource}"));
            }

            if (quickNoteLines.Count > 0)
            {
                var lblQuickNote = new Label
                {
                    Text = string.Join("\n", quickNoteLines),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(32, 52, 82),
                    BackColor = Color.FromArgb(244, 248, 254),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, deepReasoningLines.Count > 0 ? 3 : 6),
                    Padding = new Padding(8, 5, 8, 5)
                };
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblQuickNote, 0, row++);
            }

            if (deepReasoningLines.Count > 0)
            {
                var lblTechDetails = new Label
                {
                    Text = string.Join("\n", deepReasoningLines),
                    Font = _fontSmall,
                    ForeColor = Color.FromArgb(42, 58, 82),
                    BackColor = Color.FromArgb(248, 250, 253),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrap, 0),
                    Tag = WrapTag,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 6),
                    Padding = new Padding(8, 5, 8, 5),
                    Visible = false
                };

                var btnToggleTech = new Label
                {
                    Text = UiLocalizer.Pick("▾ Show Analysis & Reference Details", "▾ သုံးသပ်ချက်နှင့် ကျမ်းကိုး အသေးစိတ်ကြည့်ရန်"),
                    Font = _fontSmallBold,
                    ForeColor = Color.FromArgb(24, 88, 178),
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 5),
                    Padding = new Padding(2, 1, 2, 2),
                    Cursor = Cursors.Hand
                };
                btnToggleTech.Click += (s, e) =>
                {
                    bool expand = !lblTechDetails.Visible;
                    _cardsPanel.SuspendLayout();
                    lblTechDetails.Visible = expand;
                    btnToggleTech.Text = expand
                        ? UiLocalizer.Pick("▴ Hide Details", "▴ အသေးစိတ် သုံးသပ်ချက် ပြန်ဖျောက်ရန်")
                        : UiLocalizer.Pick("▾ Show Analysis & Reference Details", "▾ သုံးသပ်ချက်နှင့် ကျမ်းကိုး အသေးစိတ်ကြည့်ရန်");
                    _cardsPanel.ResumeLayout();
                };

                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(btnToggleTech, 0, row++);
                card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                card.Controls.Add(lblTechDetails, 0, row++);
            }

            // 6. Card Footer Actions (Jump | Ignore | +Custom Dictionary)
            bool canAddToCustomDict = issue.CanAddToCustomDictionary;
            var bottomActions = canAddToCustomDict
                ? new FastPercentRowPanel(34F, 31F, 35F) { Margin = new Padding(0, 2, 0, 0) }
                : new FastPercentRowPanel(50F, 50F) { Margin = new Padding(0, 2, 0, 0) };

            var btnJump = CreatePillButton(
                UiLocalizer.Pick("Locate", "စာတမ်း၌ပြရန်"),
                Color.FromArgb(243, 245, 249), Color.FromArgb(48, 58, 74), Color.FromArgb(216, 222, 232), _fontSmall);
            btnJump.Click += (s, e) => jumpAction();

            var btnIgnore = CreatePillButton(
                UiLocalizer.Pick("Ignore", "ကျော်မည်"),
                Color.FromArgb(243, 245, 249), Color.FromArgb(85, 92, 105), Color.FromArgb(216, 222, 232), _fontSmall);
            btnIgnore.Click += (s, e) =>
            {
                RequestIgnoreOnce?.Invoke(item);
                ResolveSingleCardSmoothly(item, card);
            };

            bottomActions.Controls.Add(btnJump);
            bottomActions.Controls.Add(btnIgnore);

            if (canAddToCustomDict)
            {
                var btnAddDict = CreatePillButton(
                    UiLocalizer.Pick("+Add Word", "+အဘိဓာန်မှတ်"),
                    Color.FromArgb(236, 244, 255), Color.FromArgb(20, 72, 155), Color.FromArgb(190, 212, 246), _fontSmall);
                btnAddDict.Click += (s, e) =>
                {
                    string confirmMsg = item.Issue.BuildAddCustomWordConfirmationMessage(out string confirmTitle, out bool isKnownErr);
                    DialogResult confirmRes = MessageBox.Show(
                        this,
                        confirmMsg,
                        confirmTitle,
                        MessageBoxButtons.YesNo,
                        isKnownErr ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);
                    if (confirmRes != DialogResult.Yes)
                        return;

                    string wordSaved = item.Issue.OriginalText;
                    RequestAddToDictionary?.Invoke(item);
                    ShowToastBanner(UiLocalizer.Pick(
                        $"✓ Added \"{wordSaved}\" to your Custom Dictionary.",
                        $"✓ \"{wordSaved}\" ကို မှတ်သားစာလုံးစာရင်းသို့ ထည့်သွင်းပြီးပါပြီ။"));
                };
                bottomActions.Controls.Add(btnAddDict);
            }

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(bottomActions, 0, row++);

            return card;
        }

        private void UpdateStickyDockedHeaderCounts(List<DocumentProofingItem>? activeOverride = null)
        {
            if (_lblDockedTitle == null || _btnDockedApply == null)
                return;

            var active = activeOverride ?? GetActiveFilteredItems();
            var fixable = active.Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0).ToList();
            int checkedCount = 0;
            foreach (var it in fixable)
            {
                var st = GetOrInitDockedState(it);
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
            _btnDockedApply.BackColor = checkedCount > 0 ? Color.FromArgb(22, 132, 68) : Color.FromArgb(184, 194, 206);
            _btnDockedApply.FlatAppearance.BorderColor = checkedCount > 0 ? Color.FromArgb(18, 110, 56) : Color.FromArgb(172, 182, 194);
        }

        private void SetAllDockedCheckStates(bool sureOnly, bool checkAll)
        {
            var fixable = GetActiveFilteredItems()
                .Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0)
                .ToList();
            foreach (var it in fixable)
            {
                var st = GetOrInitDockedState(it);
                bool nextCheck = sureOnly ? it.Issue.IsHighCertaintyPrecheck : checkAll;
                _dockedStateByItem[it] = (nextCheck, st.Chosen);
            }
            RefreshProofingCardsFull();
        }

        private void ApplyDockedBatchFromStickyBar()
        {
            var fixable = GetActiveFilteredItems()
                .Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0)
                .ToList();
            var approved = new Dictionary<DocumentProofingItem, string>();
            foreach (var it in fixable)
            {
                var st = GetOrInitDockedState(it);
                if (st.Checked && !string.IsNullOrEmpty(st.Chosen))
                    approved[it] = st.Chosen;
            }
            if (approved.Count > 0)
            {
                RequestApplyDockedBatch?.Invoke(approved);
            }
        }

        private void PopOutFromStickyDockedBar()
        {
            var fixable = GetActiveFilteredItems()
                .Where(i => i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0)
                .ToList();
            var snapshot = fixable.Select(it =>
            {
                var st = GetOrInitDockedState(it);
                string loc = it.HasResolvedFriendlyLocation
                    ? it.FormatFriendlyLocation()
                    : $"{it.FormatSectionAndParagraphLabel()}, {it.FormatLineLabel()}";
                return new FixPreviewEntry<DocumentProofingItem>(
                    it,
                    loc,
                    it.ParagraphSnippet ?? it.Issue.OriginalText,
                    it.Issue)
                {
                    IsChecked = st.Checked,
                    ChosenReplacement = st.Chosen
                };
            }).ToList();

            ExitDockedChecklistMode();
            RequestPopOutDockedChecklist?.Invoke(snapshot);
        }

        private void RebuildSingleDockedCardInPlace(DocumentProofingItem item)
        {
            if (!_cardByItem.TryGetValue(item, out var oldCard) || oldCard.IsDisposed)
            {
                RefreshProofingCardsFull();
                return;
            }

            int idx = _cardsPanel.Controls.GetChildIndex(oldCard, throwException: false);
            if (idx < 0)
            {
                RefreshProofingCardsFull();
                return;
            }

            int cardWidth = oldCard.Width > 80 ? oldCard.Width : GetTargetCardWidth(reserveScrollbarForMultipleCards: true);
            BeginCardsBatchUpdate();
            try
            {
                Control newCard = BuildDockedChecklistRowCard(item, cardWidth);
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

            UpdateStickyDockedHeaderCounts();
        }

        private Control BuildDockedChecklistRowCard(DocumentProofingItem item, int cardWidth)
        {
            int innerWrap = Math.Max(160, cardWidth - 26);
            var st = GetOrInitDockedState(item);
            bool isReview = item.Issue.IsReviewNeeded;
            bool isExpanded = _expandedDockedItems.Contains(item);
            int sugCount = item.Issue.Suggestions?.Count ?? 0;

            Color accent = st.Checked
                ? Color.FromArgb(22, 132, 68)
                : (isReview ? Color.FromArgb(218, 162, 0) : Color.FromArgb(156, 168, 184));
            var card = CreateStyledCardContainer(cardWidth, accent, isSelectableItem: item);
            card.Margin = new Padding(0, 0, 0, 6);
            card.Padding = new Padding(10, 7, 9, 7);
            int r = 0;

            Action jumpToDoc = () =>
            {
                HighlightSelectedCard(item, scrollIntoView: false);
                RequestSelectIssueInDocument?.Invoke(item);
            };

            // Row 1: Clean Location Header + Certainty Pill
            var metaRow = new FastPercentRowPanel(74F, 26F)
            {
                Margin = new Padding(0, 0, 0, 4)
            };
            string dockLoc = item.HasResolvedFriendlyLocation
                ? item.FormatFriendlyLocation()
                : $"{item.FormatSectionAndParagraphLabel()}, {item.FormatLineLabel()}";

            var lblLoc = new Label
            {
                Text = dockLoc,
                Font = _fontBrandStats,
                ForeColor = Color.FromArgb(62, 76, 96),
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                Dock = DockStyle.Fill,
                Height = MeasureSingleLineLabelHeight(_fontBrandStats, 4),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            lblLoc.Click += (s, e) => jumpToDoc();

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
            lblCert.Click += (s, e) => jumpToDoc();

            metaRow.Controls.Add(lblLoc);
            metaRow.Controls.Add(lblCert);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(metaRow, 0, r++);

            // Row 2: Highlighted Visual Diff Box (Click to Jump!)
            string visOrig = item.Issue.FormatVisualOriginalText(st.Chosen);
            Color diffBg = st.Checked
                ? Color.FromArgb(236, 250, 241)
                : (isReview ? Color.FromArgb(254, 249, 195) : Color.FromArgb(245, 247, 250));

            var lblDiff = new Label
            {
                Text = $"{(st.Checked ? "☑" : "☐")}  \"{visOrig}\"   ➔   \"{st.Chosen}\"",
                Font = _fontSmallBold,
                ForeColor = st.Checked
                    ? Color.FromArgb(14, 58, 34)
                    : (isReview ? Color.FromArgb(124, 82, 0) : Color.FromArgb(72, 82, 96)),
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
            lblDiff.Click += (s, e) => jumpToDoc();
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
                st.Checked ? Color.FromArgb(16, 102, 54) : Color.FromArgb(68, 80, 96),
                Color.FromArgb(182, 206, 192),
                _fontSmall);
            btnToggle.AutoSize = true;
            btnToggle.Dock = DockStyle.None;
            btnToggle.Click += (s, e) =>
            {
                var cur = GetOrInitDockedState(item);
                _dockedStateByItem[item] = (!cur.Checked, cur.Chosen);
                RebuildSingleDockedCardInPlace(item);
            };

            var btnFixOne = CreatePillButton(
                UiLocalizer.Pick("✓ Fix", "✓ ပြင်မည်"),
                Color.FromArgb(22, 132, 68), Color.White, Color.FromArgb(18, 110, 56), _fontSmallBold);
            btnFixOne.AutoSize = true;
            btnFixOne.Dock = DockStyle.None;
            btnFixOne.Click += (s, e) =>
            {
                var cur = GetOrInitDockedState(item);
                if (!string.IsNullOrEmpty(cur.Chosen))
                {
                    RequestApplySuggestion?.Invoke(item, cur.Chosen);
                    ResolveSingleCardSmoothly(item, card);
                }
            };

            var btnIgnore = CreatePillButton(
                UiLocalizer.Pick("Ignore", "ကျော်"),
                Color.FromArgb(245, 247, 250), Color.FromArgb(82, 92, 104), Color.FromArgb(212, 218, 228), _fontSmall);
            btnIgnore.AutoSize = true;
            btnIgnore.Dock = DockStyle.None;
            btnIgnore.Click += (s, e) =>
            {
                RequestIgnoreOnce?.Invoke(item);
                ResolveSingleCardSmoothly(item, card);
            };

            string adjustLabel = sugCount > 1
                ? UiLocalizer.Pick(isExpanded ? $"▴ Adjust ({sugCount})" : $"✎ Adjust ({sugCount}) ▾", isExpanded ? $"▴ ပြင်ရွေး ({sugCount})" : $"✎ ပြင်ရွေး ({sugCount}) ▾")
                : UiLocalizer.Pick(isExpanded ? "▴ Adjust" : "✎ Adjust ▾", isExpanded ? "▴ ပြင်ရွေး" : "✎ ပြင်ရွေး ▾");

            var btnAdjust = CreatePillButton(
                adjustLabel,
                isExpanded ? Color.FromArgb(224, 238, 255) : Color.FromArgb(242, 246, 253),
                Color.FromArgb(20, 68, 148),
                Color.FromArgb(182, 206, 240),
                _fontSmall);
            btnAdjust.AutoSize = true;
            btnAdjust.Dock = DockStyle.None;
            btnAdjust.Click += (s, e) =>
            {
                if (_expandedDockedItems.Contains(item))
                    _expandedDockedItems.Remove(item);
                else
                    _expandedDockedItems.Add(item);
                RebuildSingleDockedCardInPlace(item);
            };

            rowBtns.Controls.AddRange(new Control[] { btnToggle, btnFixOne, btnIgnore, btnAdjust });
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(rowBtns, 0, r++);

            // Row 4: Expandable Inline Adjustment Drawer (Choices + Custom TextBox + Context/Rule)
            if (isExpanded)
            {
                var drawer = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 1,
                    BackColor = Color.FromArgb(248, 250, 254),
                    Padding = new Padding(7, 6, 7, 6),
                    Margin = new Padding(0, 5, 0, 0)
                };
                drawer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                int dr = 0;
                int drawerWrap = Math.Max(140, innerWrap - 16);

                // Alternative suggestion pills
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
                            isCur ? Color.FromArgb(14, 96, 48) : Color.FromArgb(32, 52, 82),
                            isCur ? Color.FromArgb(132, 202, 162) : Color.FromArgb(196, 208, 226),
                            isCur ? _fontSmallBold : _fontSmall);
                        btnOpt.AutoSize = true;
                        btnOpt.Dock = DockStyle.None;
                        string repCopy = cand.Replacement;
                        btnOpt.Click += (s, e) =>
                        {
                            _dockedStateByItem[item] = (true, repCopy);
                            RebuildSingleDockedCardInPlace(item);
                        };
                        optFlow.Controls.Add(btnOpt);
                    }

                    drawer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    drawer.Controls.Add(optFlow, 0, dr++);
                }

                // Inline Custom Replacement Box + [Set]
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
                    Color.FromArgb(24, 90, 188), Color.White, Color.FromArgb(20, 76, 162), _fontSmallBold);
                btnSetCustom.Click += (s, e) =>
                {
                    string typed = (txtCustom.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(typed))
                    {
                        _dockedStateByItem[item] = (true, typed);
                        RebuildSingleDockedCardInPlace(item);
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

                // Context & Rule Note
                string ctxSnippet = (item.ParagraphSnippet ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                if (ctxSnippet.Length > 130)
                    ctxSnippet = MyanmarSyllableBreaker.TruncateAtSyllableBoundary(ctxSnippet, 130, "…");
                string noteText = !string.IsNullOrWhiteSpace(ctxSnippet)
                    ? $"{ctxSnippet}\n• {item.Issue.Message}"
                    : (item.Issue.Message ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(noteText))
                {
                    var lblNote = new Label
                    {
                        Text = noteText,
                        Font = _fontBrandStats,
                        ForeColor = Color.FromArgb(72, 84, 102),
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
