using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Spreadsheet;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    public sealed partial class ExcelProofingTaskPaneControl
    {
        private const int ExcelProofingCardBatchSize = 30;
        private const int MaxMountedExcelCards = 90; // Hard Win32 HWND cap (max 3 batches mounted at once)
        private int _visibleWindowStart;
        private int _visibleCardLimit = ExcelProofingCardBatchSize;
        private bool _hasRenderedProofingOnce;

        private static string BuildExcelIssuesUiSignature(IReadOnlyList<SpreadsheetCellIssue> activeItems)
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

        public void RenderIssues(IReadOnlyList<SpreadsheetCellIssue>? items)
        {
            bool wasScanning = _isScanning;
            _isScanning = false;

            var prevActive = _allItems.Where(i => !i.IsResolved).ToList();
            var nextList = items != null ? items.Where(i => !i.IsResolved).ToList() : new List<SpreadsheetCellIssue>();

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
            StyleSegment(_btnStyleGeneral, !isFormal, Color.FromArgb(16, 108, 56));
            StyleSegment(_btnStyleFormal, isFormal, Color.FromArgb(18, 56, 128));

            _lblStyleHint.Text = isFormal
                ? "ရုံးသုံး/စာပေစနစ် — စာလုံးပေါင်း + ပါတယ်➔ပါသည်၊ တွေ➔များ၊ နဲ့➔နှင့် စစ်ဆေးသည်"
                : "အထွေထွေ စံသတ်ပုံ — စာလုံးပေါင်း၊ တ/တစ်၊ ခုနစ်/ခုနှစ် နှင့် အနက်သံသယ စစ်ဆေးသည်";
        }

        private void SelectAndScanScope(SpreadsheetScanScope scope)
        {
            _currentScanScope = scope;
            UpdateScopeButtons();
            RequestScanScope?.Invoke(scope);
        }

        private void UpdateScopeButtons()
        {
            StyleSegment(_btnScopeSheet, _currentScanScope == SpreadsheetScanScope.ActiveSheet, Color.FromArgb(16, 108, 56));
            StyleSegment(_btnScopeSelection, _currentScanScope == SpreadsheetScanScope.Selection, Color.FromArgb(16, 108, 56));
            StyleSegment(_btnScopeWorkbook, _currentScanScope == SpreadsheetScanScope.EntireWorkbook, Color.FromArgb(16, 108, 56));

            _btnScanNow.Text = _currentScanScope switch
            {
                SpreadsheetScanScope.Selection => "ရွေးကွက် စစ်မည်",
                SpreadsheetScanScope.EntireWorkbook => "Workbook စစ်မည်",
                _ => "Sheet စစ်မည်"
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

            _btnFilterAll.Text = $"အားလုံး ({unresolvedTotal})";
            _btnFilterSpelling.Text = $"သတ်ပုံ ({spellingCount})";
            _btnFilterGrammar.Text = $"သဒ္ဒါ ({grammarCount})";
            _btnFilterContext.Text = $"အနက် ({contextCount})";

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
            static bool HasChoices(SpreadsheetCellIssue i) =>
                !i.IsResolved && i.Issue.Suggestions != null && i.Issue.Suggestions.Count > 0;

            int reviewableCount = _currentFilter switch
            {
                IssueFilterMode.SpellingOnly => _allItems.Count(i => HasChoices(i) && !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning),
                IssueFilterMode.GrammarOnly => _allItems.Count(i => HasChoices(i) && i.Issue.IsGrammarOrStyle),
                IssueFilterMode.ContextOnly => _allItems.Count(i => HasChoices(i) && i.Issue.IsContextualWarning),
                _ => _allItems.Count(HasChoices)
            };

            _btnFixAll.Enabled = reviewableCount > 0;
            _btnFixAll.Text = reviewableCount > 0 ? $"စိစစ်ပြင် ({reviewableCount})" : "စိစစ်ပြင်မည်";
            _btnFixAll.BackColor = reviewableCount > 0 ? Color.FromArgb(24, 90, 188) : Color.FromArgb(204, 214, 228);
            _btnFixAll.ForeColor = Color.White;
        }

        private static void ApplyFilterPillAppearance(Button btn, bool active, Color activeBg, Color idleBg, Color idleFg)
        {
            btn.BackColor = active ? activeBg : idleBg;
            btn.ForeColor = active ? Color.White : idleFg;
            btn.FlatAppearance.BorderColor = active ? activeBg : Color.FromArgb(218, 224, 234);
        }

        private List<SpreadsheetCellIssue> GetFilteredUnresolvedIssues()
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

            SpreadsheetCellIssue target = visible[_stepperCursor];
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

        private void RenderProofingCards()
        {
            UpdateFilterPillStyles();
            var filtered = GetFilteredUnresolvedIssues();

            _cardsPanel.SuspendLayout();
            DisposeAndClearCardsPanel();

            int cardWidth = GetTargetCardWidth();
            int innerWrap = Math.Max(180, cardWidth - 32);

            if (filtered.Count == 0)
            {
                _visibleWindowStart = 0;
                int totalUnresolved = _allItems.Count(i => !i.IsResolved);
                _lblSummary.Text = totalUnresolved == 0
                    ? "✓ စစ်ဆေးပြီးသော မြန်မာစာ အကွက်များတွင် အမှားအယွင်း မတွေ့ရှိပါ"
                    : $"ဤအမျိုးအစားတွင် တွေ့ရှိချက် မရှိပါ (အခြားအမျိုးအစား၌ {totalUnresolved} ခု ရှိသည်)";
                _lblSummary.ForeColor = Color.FromArgb(16, 108, 56);

                var cleanCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 124, 65));
                int r = 0;

                var lblOkTitle = new Label
                {
                    Text = "✓ မြန်မာစာ စာလုံးပေါင်းနှင့် သဒ္ဒါ ကောင်းမွန်ပါသည် (Formula-Safe)",
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
                    Text = "• လက်ရှိ Sheet၊ ရွေးထားသော အကွက်များ သို့မဟုတ် Workbook တစ်ခုလုံးကို ရွေးချယ်စစ်ဆေးနိုင်ပါသည်။\n• အမှားတွေ့ရှိသော စာသားကွက်များတွင် သတ်ပုံ (အနီမျဉ်း)၊ သဒ္ဒါ (အပြာမျဉ်းနှစ်ထပ်)၊ အနက်သံသယ (လိမ္မော်မျဉ်း) ပြသပေးပြီး Save (Ctrl+S) / Print (Ctrl+P) လုပ်ချိန်၌ အလိုအလျောက် ဖယ်ရှားပေးပါသည်။\n• Excel Formula အကွက်များ (=SUM, =IF, =VLOOKUP) နှင့် ကိန်းဂဏန်းတွက်ချက်မှုများကို 100% အလိုအလျောက် ကာကွယ်ချန်လှပ်ထားပါသည်။",
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
                _cardsPanel.ResumeLayout(true);
                return;
            }

            if (_visibleWindowStart >= filtered.Count)
            {
                _visibleWindowStart = Math.Max(0, ((filtered.Count - 1) / ExcelProofingCardBatchSize) * ExcelProofingCardBatchSize);
            }

            _lblSummary.Text = $"တွေ့ရှိချက် ({filtered.Count}) ခု — ကတ်ကိုနှိပ်၍ သက်ဆိုင်ရာ အကွက် (Cell) သို့ တိုက်ရိုက်သွားနိုင်သည်";
            _lblSummary.ForeColor = filtered.Any(x => !x.Issue.IsGrammarOrStyle && !x.Issue.IsContextualWarning)
                ? Color.FromArgb(182, 32, 32)
                : Color.FromArgb(158, 92, 12);

            if (_visibleWindowStart > 0)
            {
                var prevCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 108, 56));
                var btnPrevBatch = CreatePillButton(
                    $"▴ ရှေ့ပိုင်း တွေ့ရှိချက် (#1 မှ #{_visibleWindowStart}) ပြန်ကြည့်မည်",
                    Color.FromArgb(234, 246, 239),
                    Color.FromArgb(14, 96, 50),
                    Color.FromArgb(182, 220, 196),
                    _fontSmallBold);
                btnPrevBatch.Click += (s, e) =>
                {
                    _visibleWindowStart = Math.Max(0, _visibleWindowStart - ExcelProofingCardBatchSize);
                    _visibleCardLimit = Math.Min(MaxMountedExcelCards, ExcelProofingCardBatchSize * 2);
                    RenderProofingCards();
                };
                prevCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                prevCard.Controls.Add(btnPrevBatch, 0, 0);
                _cardsPanel.Controls.Add(prevCard);
            }

            var sameWordCounts = BuildExcelSameWordCountsMap();
            int windowCount = Math.Min(MaxMountedExcelCards, Math.Max(ExcelProofingCardBatchSize, _visibleCardLimit));
            int endExclusive = Math.Min(filtered.Count, _visibleWindowStart + windowCount);

            for (int idx = _visibleWindowStart; idx < endExclusive; idx++)
            {
                SpreadsheetCellIssue item = filtered[idx];
                string normOrig = UnicodeNormalizer.NormalizeCanonicalOrder((item.Issue.OriginalText ?? string.Empty).Trim());
                int dupCount = sameWordCounts.TryGetValue(normOrig, out int cnt) ? cnt : 1;

                Control card = BuildExcelIssueCard(item, idx + 1, cardWidth, innerWrap, dupCount);
                _cardByItem[item] = card;
                _cardsPanel.Controls.Add(card);
            }

            if (filtered.Count > endExclusive)
            {
                int remaining = filtered.Count - endExclusive;
                int nextBatch = Math.Min(ExcelProofingCardBatchSize, remaining);
                var moreCard = CreateStyledCardContainer(cardWidth, Color.FromArgb(16, 108, 56));
                var btnMoreBatch = CreatePillButton(
                    $"▾ နောက်ထပ် တွေ့ရှိချက် ({nextBatch}) ခု ဆက်လက်ဖော်ပြမည်  •  ကျန်ရှိ ({remaining} / {filtered.Count})",
                    Color.FromArgb(234, 246, 239),
                    Color.FromArgb(14, 96, 50),
                    Color.FromArgb(182, 220, 196),
                    _fontSmallBold);
                btnMoreBatch.Click += (s, e) =>
                {
                    if (_cardByItem.Count + ExcelProofingCardBatchSize <= MaxMountedExcelCards)
                    {
                        _visibleCardLimit += ExcelProofingCardBatchSize;
                    }
                    else
                    {
                        _visibleWindowStart = Math.Min(
                            Math.Max(0, filtered.Count - ExcelProofingCardBatchSize),
                            _visibleWindowStart + ExcelProofingCardBatchSize);
                        _visibleCardLimit = MaxMountedExcelCards;
                    }
                    RenderProofingCards();
                };
                moreCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                moreCard.Controls.Add(btnMoreBatch, 0, 0);
                _cardsPanel.Controls.Add(moreCard);
            }

            _cardsPanel.ResumeLayout(true);
        }

        private Control BuildExcelIssueCard(
            SpreadsheetCellIssue item,
            int displayIndex,
            int cardWidth,
            int innerWrap,
            int duplicateWordCount)
        {
            ProofingIssue issue = item.Issue;
            Color accentColor = issue.IsContextualWarning
                ? Color.FromArgb(217, 138, 18)
                : (issue.IsGrammarOrStyle ? Color.FromArgb(24, 90, 188) : Color.FromArgb(204, 38, 38));

            Color diffBannerBg = issue.IsContextualWarning
                ? Color.FromArgb(255, 248, 230)
                : (issue.IsGrammarOrStyle ? Color.FromArgb(238, 245, 255) : Color.FromArgb(254, 239, 239));

            string categoryText = issue.IssueType switch
            {
                ProofingIssueType.ContextualMeaningWarning => "● အနက်သံသယ (Context Check)",
                ProofingIssueType.NumeralClassifierError => "● ဂဏန်းနှင့် မျိုးပြပစ္စည်း",
                ProofingIssueType.OfficialStyleGrammarError => "● ရုံးသုံး/စာပေ သဒ္ဒါ",
                ProofingIssueType.PunctuationError => "● ပုဒ်ဖြတ်ပုဒ်ရပ် (၊ ။)",
                ProofingIssueType.ZawgyiEncodingError => "● ဇော်ဂျီ ဖောင့်ကုဒ်အမှား",
                ProofingIssueType.LookalikeCharacterError => "● ဆင်တူအက္ခရာ (၀/ဝ ၊ စဉ်/စဥ်)",
                _ => "● စာလုံးပေါင်းသတ်ပုံ"
            };

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
            var topBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            topBar.Click += (s, e) => JumpToCell();

            var lblCat = new Label
            {
                Text = categoryText,
                Font = _fontSmallBold,
                ForeColor = accentColor,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 2, 0, 0),
                Cursor = Cursors.Hand
            };
            lblCat.Click += (s, e) => JumpToCell();

            var btnCellLoc = CreatePillButton(
                $"📍 {item.DisplayLocation}  #{displayIndex}",
                Color.FromArgb(235, 246, 239),
                Color.FromArgb(16, 98, 52),
                Color.FromArgb(186, 222, 198),
                _fontBrandStats);
            btnCellLoc.Dock = DockStyle.Right;
            btnCellLoc.Click += (s, e) => JumpToCell();
            _toolTip.SetToolTip(btnCellLoc, $"{item.DisplayLocation} အကွက်သို့ တိုက်ရိုက်သွားရန် နှိပ်ပါ");

            topBar.Controls.Add(lblCat, 0, 0);
            topBar.Controls.Add(btnCellLoc, 1, 0);
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(topBar, 0, row++);

            // Row 1: Diff Banner ("အမှား" -> "အမှန်")
            string topSuggested = issue.Suggestions.Count > 0 ? issue.Suggestions[0].Replacement : "စိစစ်ရန်";
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

            var lblDiff = new Label
            {
                Text = $"\"{issue.OriginalText}\"   ➔   \"{topSuggested}\"",
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
                Text = $"အကွက်စာသား — {item.BuildContextSnippet(24)}",
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
                        Text = $"ရွေးချယ်ပြင်ဆင်ရန် ({maxSug} မျိုး) —",
                        Font = _fontBrandStats,
                        ForeColor = issue.IsContextualWarning ? Color.FromArgb(148, 86, 8) : Color.FromArgb(16, 108, 56),
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

                    string prefixIcon = issue.IsContextualWarning ? "စိစစ်ရွေးမည်  ➔  " : "✓ ပြင်မည်  ➔  ";
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
                        Text = $"✓✓ တူညီသောအမှား ({duplicateWordCount}) ကွက်လုံး ပြင်မည်  ➔  {topRep}",
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
                    : $"{fullExplanation}\n• သဒ္ဒါ/အနက် အကြောင်းပြချက် — {issue.GrammaticalReasoning}";
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
                    Text = $"ကိုးကားကျမ်း — {issue.AuthoritativeSource}",
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
            var footerGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 0)
            };
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            footerGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnIgnore = CreatePillButton("လျစ်လျူရှု", Color.FromArgb(245, 247, 250), Color.FromArgb(74, 84, 98), Color.FromArgb(216, 222, 232), _fontBrandStats);
            btnIgnore.Dock = DockStyle.Fill;
            btnIgnore.Click += (s, e) => RequestIgnoreOnce?.Invoke(item);

            var btnAddDict = CreatePillButton("+ မှတ်သားမည်", Color.FromArgb(245, 247, 250), Color.FromArgb(22, 74, 156), Color.FromArgb(204, 218, 242), _fontBrandStats);
            btnAddDict.Dock = DockStyle.Fill;
            btnAddDict.Enabled = !issue.IsContextualWarning && !issue.IsGrammarOrStyle;
            btnAddDict.Click += (s, e) => RequestAddToDictionary?.Invoke(item);

            var btnLookup = CreatePillButton("အဘိဓာန်ကြည့်", Color.FromArgb(245, 247, 250), Color.FromArgb(16, 102, 52), Color.FromArgb(196, 226, 206), _fontBrandStats);
            btnLookup.Dock = DockStyle.Fill;
            btnLookup.Click += (s, e) => OpenDictionaryWithQuery(topSuggested != "စိစစ်ရန်" ? topSuggested : issue.OriginalText);

            footerGrid.Controls.Add(btnIgnore, 0, 0);
            footerGrid.Controls.Add(btnAddDict, 1, 0);
            footerGrid.Controls.Add(btnLookup, 2, 0);

            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(footerGrid, 0, row++);

            return card;
        }

        private static string TruncateGloss(string gloss, int maxChars)
        {
            string clean = gloss.Trim();
            if (clean.Length <= maxChars)
                return clean;
            return clean.Substring(0, maxChars - 1).TrimEnd() + "…";
        }
    }
}
