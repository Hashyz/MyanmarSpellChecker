using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.WordAddIn.UI;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MyanmarSpellChecker.WordAddIn.Interop
{
    /// <summary>
    /// Displays the High-DPI Myanmar Quick-Fix Choice Card directly beneath the underlined word when the user
    /// places the cursor on or right-clicks a flagged Myanmar word, and injects "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း"
    /// suggestions into MS Word's context menus ("Text", "Table Text", "Spelling", "Grammar", "Grammar (2)").
    /// </summary>
    public sealed class WordContextMenuHandler
    {
        private const string MenuTag = "MM_SPELL_CTX_ITEM";

        private readonly Word.Application _app;
        private readonly MyanmarProofingEngine _engine;
        private readonly SafeUnderlineManager _underlineManager;
        private readonly StaDocumentScanner _scanner;
        private readonly Action _onDocumentReplacedCallback;
        private readonly string? _customDictionaryFilePath;
        private readonly Action<DocumentProofingItem?>? _onCursorIssueChanged;
        private readonly Action<string>? _onLookupWordInTaskPane;

        private readonly List<Office.CommandBarButton> _activeButtons = new List<Office.CommandBarButton>();
        private MyanmarQuickFixPopup? _activePopup;
        private DocumentProofingItem? _activePopupItem;
        private int _lastPopupDocOffset = -1;

        public WordContextMenuHandler(
            Word.Application app,
            MyanmarProofingEngine engine,
            SafeUnderlineManager underlineManager,
            StaDocumentScanner scanner,
            Action onDocumentReplacedCallback,
            string? customDictionaryFilePath = null,
            Action<DocumentProofingItem?>? onCursorIssueChanged = null,
            Action<string>? onLookupWordInTaskPane = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _underlineManager = underlineManager ?? throw new ArgumentNullException(nameof(underlineManager));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _onDocumentReplacedCallback = onDocumentReplacedCallback;
            _customDictionaryFilePath = customDictionaryFilePath;
            _onCursorIssueChanged = onCursorIssueChanged;
            _onLookupWordInTaskPane = onLookupWordInTaskPane;
        }

        /// <summary>
        /// When the user places the cursor on an underlined Myanmar word (including start boundary 'စား|ပီးပီ',
        /// single-syllable cluster boundary 'မဟုတ်|ပုလေ', or interior syllable boundary), immediately display
        /// the non-focus-stealing Myanmar Quick-Fix Choice Card right beneath the word and sync the Task Pane.
        /// </summary>
        public void OnWindowSelectionChange(Word.Selection sel)
        {
            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (sel == null || sel.Range == null || doc == null)
                    return;

                // Only trigger on a single caret placement (not drag-selection)
                if (sel.Range.Start != sel.Range.End)
                {
                    CloseActivePopup();
                    _onCursorIssueChanged?.Invoke(null);
                    return;
                }

                int clickDocPos = sel.Range.Start;
                DocumentProofingItem? targetItem = FindCachedItemAtCursorPosition(doc, clickDocPos);
                if (targetItem == null)
                {
                    CloseActivePopup();
                    _lastPopupDocOffset = -1;
                    _onCursorIssueChanged?.Invoke(null);
                    return;
                }

                _onCursorIssueChanged?.Invoke(targetItem);

                if (ReferenceEquals(_activePopupItem, targetItem) && _activePopup != null && !_activePopup.IsDisposed)
                    return;

                _lastPopupDocOffset = clickDocPos;
                ShowQuickFixPopupForItem(doc, targetItem, focusPopup: false);
            }
            catch
            {
                // Ignore selection change errors
            }
        }

        /// <summary>
        /// Called after a background/idle scan completes so if the user's cursor is already resting on a newly-underlined
        /// wrong word (e.g. 'စား|ပီးပီ'), the selectable choices card appears immediately without requiring an extra click.
        /// </summary>
        public void RefreshAtCurrentSelectionIfOnIssue()
        {
            try
            {
                Word.Selection? sel = _app.Selection;
                Word.Document? doc = _app.ActiveDocument;
                if (sel == null || sel.Range == null || doc == null || sel.Range.Start != sel.Range.End)
                    return;

                int caretPos = sel.Range.Start;
                DocumentProofingItem? targetItem = FindCachedItemAtCursorPosition(doc, caretPos);
                if (targetItem != null)
                {
                    _onCursorIssueChanged?.Invoke(targetItem);
                    if (_activePopup == null || _activePopup.IsDisposed || !ReferenceEquals(_activePopupItem, targetItem))
                    {
                        _lastPopupDocOffset = caretPos;
                        ShowQuickFixPopupForItem(doc, targetItem, focusPopup: false);
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        public void OnWindowBeforeRightClick(Word.Selection sel, ref bool cancel)
        {
            try
            {
                ClearCustomContextMenuItems();

                Word.Document? doc = _app.ActiveDocument;
                if (sel == null || sel.Range == null || doc == null)
                    return;

                int clickDocPos = sel.Range.Start;
                DocumentProofingItem? targetItem = FindCachedItemAtPosition(doc, clickDocPos);

                if (targetItem == null)
                {
                    targetItem = AnalyzeParagraphAtSelectionOnTheFly(doc, sel, clickDocPos);
                }

                if (targetItem == null && sel.Range.Start != sel.Range.End)
                {
                    targetItem = TryBuildNumericConversionPopupItem(sel);
                }

                if (targetItem == null)
                {
                    if (sel.Range.Start != sel.Range.End)
                    {
                        TryInjectCleanSelectionHelperMenu(doc, sel);
                    }
                    return;
                }

                _onCursorIssueChanged?.Invoke(targetItem);

                // Also inject into classic CommandBars as fallback
                InjectMenuIntoCommandBar(doc, "Text", targetItem);
                InjectMenuIntoCommandBar(doc, "Table Text", targetItem);
                InjectMenuIntoCommandBar(doc, "Spelling", targetItem);
                InjectMenuIntoCommandBar(doc, "Grammar", targetItem);
                InjectMenuIntoCommandBar(doc, "Grammar (2)", targetItem);

                // Cancel Word's built-in English "No suggestions available" flyout and show our Myanmar Quick-Fix Card
                cancel = true;
                ShowQuickFixPopupForItem(doc, targetItem, focusPopup: true);
            }
            catch
            {
                // Ignore context menu hook errors
            }
        }

        private void ShowQuickFixPopupForItem(Word.Document doc, DocumentProofingItem targetItem, bool focusPopup)
        {
            CloseActivePopup();
            _activePopupItem = targetItem;

            Rectangle? wordAnchorRect = TryGetWordRangeScreenRectangle(targetItem.TargetRange);
            string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder((targetItem.Issue.OriginalText ?? string.Empty).Trim());
            var cachedList = _scanner.GetCachedIssues(doc);
            int dupCount = cachedList.Count(x => !x.IsResolved &&
                UnicodeNormalizer.NormalizeCanonicalOrder((x.Issue.OriginalText ?? string.Empty).Trim()) == origNorm);
            if (dupCount < 1) dupCount = 1;

            _activePopup = new MyanmarQuickFixPopup(
                targetItem,
                replacement =>
                {
                    _underlineManager.ReplaceIssueInDocument(doc, targetItem, replacement);
                    _onDocumentReplacedCallback?.Invoke();
                },
                () =>
                {
                    _engine.IgnoreWordInSession(targetItem.Issue.OriginalText ?? string.Empty);
                    RestoreItemUnderlineAndResolve(targetItem);
                    _onDocumentReplacedCallback?.Invoke();
                },
                onOpenStandardMenu: null,
                onAddToDictionary: () =>
                {
                    string origTxt = targetItem.Issue.OriginalText ?? string.Empty;
                    _engine.AddCustomWord(origTxt, _customDictionaryFilePath);
                    string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(origTxt.Trim());
                    foreach (var cached in _scanner.GetCachedIssues(doc))
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder((cached.Issue.OriginalText ?? string.Empty).Trim()) == normAdded)
                        {
                            RestoreItemUnderlineAndResolve(cached);
                        }
                    }
                    RestoreItemUnderlineAndResolve(targetItem);
                    _onDocumentReplacedCallback?.Invoke();
                },
                duplicateCount: dupCount,
                onBatchReplaceAllMatching: batchReplacement =>
                {
                    _underlineManager.ReplaceMatchingIssuesInDocument(doc, _scanner.GetCachedIssues(doc), targetItem.Issue.OriginalText ?? string.Empty, batchReplacement);
                    _onDocumentReplacedCallback?.Invoke();
                });

            _activePopup.ShowNearWordRangeOrCursor(wordAnchorRect, focusPopup);
        }

        private Rectangle? TryGetWordRangeScreenRectangle(Word.Range? range)
        {
            if (range == null)
                return null;

            try
            {
                Word.Window? activeWin = _app.ActiveWindow;
                if (activeWin == null)
                    return null;

                activeWin.GetPoint(out int left, out int top, out int width, out int height, range);
                if (height > 0)
                {
                    return new Rectangle(left, top, Math.Max(12, width), height);
                }
            }
            catch
            {
                // Range off-screen or window busy; popup will fall back cleanly to Cursor.Position
            }

            return null;
        }

        private static void RestoreItemUnderlineAndResolve(DocumentProofingItem item)
        {
            if (item.TargetRange != null)
            {
                try
                {
                    item.TargetRange.Font.Underline = item.OriginalUnderline;
                    item.TargetRange.Font.UnderlineColor = item.OriginalUnderlineColor;
                }
                catch { }
            }
            item.IsResolved = true;
        }

        public void CloseActivePopup()
        {
            _activePopupItem = null;
            if (_activePopup != null)
            {
                try
                {
                    if (!_activePopup.IsDisposed)
                        _activePopup.Close();
                }
                catch { }
                _activePopup = null;
            }
        }

        private DocumentProofingItem? FindCachedItemAtCursorPosition(Word.Document doc, int caretDocPos)
        {
            const int DriftTolerance = 640;
            IReadOnlyList<DocumentProofingItem> cached = _scanner.GetCachedIssuesRaw(doc);
            DocumentProofingItem? rightEdgeFallback = null;

            for (int i = 0; i < cached.Count; i++)
            {
                var item = cached[i];
                if (item.IsResolved || item.TargetRange == null)
                    continue;

                // Fast managed integer pre-filter: skip far-away issues without touching COM Range.Start/End on every arrow keystroke!
                if (item.CachedEnd > 0 &&
                    (caretDocPos < item.CachedStart - DriftTolerance || caretDocPos > item.CachedEnd + DriftTolerance))
                {
                    continue;
                }

                try
                {
                    int start = item.TargetRange.Start;
                    int end = item.TargetRange.End;
                    item.CachedStart = start;
                    item.CachedEnd = end;

                    // Priority 1: Start boundary ('စား|ပီးပီ', 'မဟုတ်|ပုလေ', 'ဘယ်လို|ကျီးလဲ') or interior syllable boundary ('စားပီး|ပီ')
                    // Essential for Myanmar script where Uniscribe places the caret at syllable-cluster boundaries!
                    if (caretDocPos >= start && caretDocPos < end)
                    {
                        return item;
                    }

                    // Priority 2: Right edge ('စားပီးပီ|') when not actively typing new text
                    if (!_scanner.HasPendingTypingChanges && caretDocPos == end && rightEdgeFallback == null)
                    {
                        rightEdgeFallback = item;
                    }
                }
                catch
                {
                    // Ignore invalid range
                }
            }

            return rightEdgeFallback;
        }

        private DocumentProofingItem? FindCachedItemAtPosition(Word.Document doc, int clickDocPos)
        {
            const int DriftTolerance = 640;
            IReadOnlyList<DocumentProofingItem> cached = _scanner.GetCachedIssuesRaw(doc);
            for (int i = 0; i < cached.Count; i++)
            {
                var item = cached[i];
                if (item.IsResolved || item.TargetRange == null)
                    continue;

                if (item.CachedEnd > 0 &&
                    (clickDocPos < item.CachedStart - DriftTolerance || clickDocPos > item.CachedEnd + DriftTolerance))
                {
                    continue;
                }

                try
                {
                    int start = item.TargetRange.Start;
                    int end = item.TargetRange.End;
                    item.CachedStart = start;
                    item.CachedEnd = end;
                    if (clickDocPos >= start && clickDocPos <= end)
                    {
                        return item;
                    }
                }
                catch
                {
                    // Ignore invalid range
                }
            }
            return null;
        }

        private DocumentProofingItem? AnalyzeParagraphAtSelectionOnTheFly(Word.Document doc, Word.Selection sel, int clickDocPos)
        {
            Word.Paragraphs paras = sel.Range.Paragraphs;
            if (paras.Count == 0)
                return null;

            Word.Range paraRange = paras[1].Range;
            string paraText = paraRange.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(paraText))
                return null;

            List<ProofingIssue> issues = _engine.CheckText(paraText);

            foreach (var issue in issues)
            {
                Word.Range? issueRange = _underlineManager.ResolveIssueRange(
                    paraRange,
                    paraText,
                    issue.StartIndex,
                    issue.Length,
                    issue.OriginalText);

                if (issueRange == null)
                    continue;

                int resolvedStart = issueRange.Start;
                int resolvedEnd = issueRange.End;
                if (clickDocPos >= resolvedStart && clickDocPos <= resolvedEnd)
                {
                    Word.WdUnderline origUnderline = Word.WdUnderline.wdUnderlineNone;
                    Word.WdColor origColor = Word.WdColor.wdColorAutomatic;
                    try
                    {
                        origUnderline = issueRange.Font.Underline;
                        origColor = issueRange.Font.UnderlineColor;
                    }
                    catch { }

                    if (origUnderline == Word.WdUnderline.wdUnderlineWavy || (int)origUnderline == 9999999)
                    {
                        origUnderline = Word.WdUnderline.wdUnderlineNone;
                        origColor = Word.WdColor.wdColorAutomatic;
                    }

                    return new DocumentProofingItem
                    {
                        ParagraphIndex = 1,
                        TargetRange = issueRange,
                        CachedStart = resolvedStart,
                        CachedEnd = resolvedEnd,
                        FallbackStartOffset = resolvedStart,
                        OriginalUnderline = origUnderline,
                        OriginalUnderlineColor = origColor,
                        Issue = issue
                    };
                }
            }

            return null;
        }

        private static DocumentProofingItem? TryBuildNumericConversionPopupItem(Word.Selection sel)
        {
            if (sel?.Range == null || sel.Range.Start == sel.Range.End)
                return null;

            string rawSel = sel.Range.Text ?? string.Empty;
            if (!MyanmarNumberToWordsConverter.TryExtractNumericSelection(rawSel, out string cleanedToken))
                return null;

            NumberConversionResult bankRes = MyanmarNumberToWordsConverter.Convert(cleanedToken, NumberConversionMode.BankingKyatLakh);
            if (!bankRes.Success)
                return null;

            NumberConversionResult cardRes = MyanmarNumberToWordsConverter.Convert(cleanedToken, NumberConversionMode.GeneralCardinal);

            Word.Range numRange = sel.Range.Duplicate;
            string trimmedCheck = rawSel.TrimEnd('\r', '\n', '\a', ' ');
            int trimDiff = rawSel.Length - trimmedCheck.Length;
            if (trimDiff > 0 && numRange.End - trimDiff > numRange.Start)
            {
                numRange.End -= trimDiff;
            }

            Word.WdUnderline origUnderline = Word.WdUnderline.wdUnderlineNone;
            Word.WdColor origColor = Word.WdColor.wdColorAutomatic;
            try
            {
                origUnderline = numRange.Font.Underline;
                origColor = numRange.Font.UnderlineColor;
                if (origUnderline == Word.WdUnderline.wdUnderlineWavy || (int)origUnderline == 9999999)
                {
                    origUnderline = Word.WdUnderline.wdUnderlineNone;
                    origColor = Word.WdColor.wdColorAutomatic;
                }
            }
            catch { }

            var candidates = new List<SuggestionCandidate>
            {
                new SuggestionCandidate
                {
                    Replacement = bankRes.BurmeseWords,
                    MeaningCue = "ဘဏ်/ငွေစာရင်း ကျပ်သိန်း စံနှုန်း",
                    Score = 1.0,
                    PosTag = "NUM",
                    IsOfficialThatpon = true
                },
                new SuggestionCandidate
                {
                    Replacement = bankRes.VoucherInlineText,
                    MeaningCue = "စာချုပ်/ဘောင်ချာ (ဂဏန်း + စာသားတွဲ)",
                    Score = 0.98,
                    PosTag = "NUM",
                    IsOfficialThatpon = true
                }
            };

            if (cardRes.Success && !string.Equals(cardRes.BurmeseWords, bankRes.BurmeseWords, StringComparison.Ordinal))
            {
                candidates.Add(new SuggestionCandidate
                {
                    Replacement = cardRes.BurmeseWords,
                    MeaningCue = "အထွေထွေ ကိန်းဂဏန်း (သန်း စံနှုန်း)",
                    Score = 0.95,
                    PosTag = "NUM",
                    IsOfficialThatpon = true
                });
            }

            var syntheticIssue = new ProofingIssue
            {
                StartIndex = 0,
                Length = cleanedToken.Length,
                OriginalText = cleanedToken,
                IssueType = ProofingIssueType.OfficialStyleGrammarError,
                Message = "ရွေးချယ်ထားသော ကိန်းဂဏန်းကို မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အောက်မြစ်သံပြောင်းစည်းမျဉ်း (ဆယ့်/ရာ့/ထောင့်) နှင့် ဘဏ်သုံးကျပ်သိန်း စံနှုန်းအတိုင်း စာသားပြောင်းလဲနိုင်ပါသည်။",
                GrammaticalReasoning = "မြန်မာသဒ္ဒါ ကိန်းဂဏန်းဆက်စပ်ထုံးအရ နောက်၌ ဂဏန်းငယ်ဆက်ပါက 'ဆယ်/ရာ/ထောင်' တို့ကို အောက်မြစ်သံပြောင်း၍ 'ဆယ့်/ရာ့/ထောင့်' ဟု ရေးသားရပြီး၊ ဘဏ်နှင့် ငွေစာရင်းရုံးစာများ၌ သိန်းဂဏန်းစံနှုန်း သုံးစွဲရသည်။",
                AuthoritativeSource = "မြန်မာစာအဖွဲ့ 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' နှင့် ရုံးသုံးငွေစာရင်း စံနှုန်း",
                Suggestions = candidates
            };

            int numStart = numRange.Start;
            int numEnd = numRange.End;
            return new DocumentProofingItem
            {
                ParagraphIndex = 1,
                TargetRange = numRange,
                CachedStart = numStart,
                CachedEnd = numEnd,
                FallbackStartOffset = numStart,
                OriginalUnderline = origUnderline,
                OriginalUnderlineColor = origColor,
                Issue = syntheticIssue
            };
        }

        private void InjectMenuIntoCommandBar(Word.Document doc, string commandBarName, DocumentProofingItem targetItem)
        {
            Office.CommandBar? bar = null;
            try { bar = _app.CommandBars[commandBarName]; } catch { return; }
            if (bar == null) return;

            int insertPos = 1;
            ProofingIssue hitIssue = targetItem.Issue;
            string originalWord = hitIssue.OriginalText;

            if (hitIssue.Suggestions.Count > 0)
            {
                foreach (var suggestion in hitIssue.Suggestions.Take(5))
                {
                    var btn = (Office.CommandBarButton)bar.Controls.Add(
                        Office.MsoControlType.msoControlButton,
                        Before: insertPos++,
                        Temporary: true);

                    btn.Tag = MenuTag;
                    btn.Style = Office.MsoButtonStyle.msoButtonCaption;
                    btn.Caption = UiLocalizer.Pick($"Myanmar Fix: {suggestion.DisplayLabel}", $"သတ်ပုံအမှန်: {suggestion.DisplayLabel}");

                    string replacementCopy = suggestion.Replacement;
                    btn.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                    {
                        _underlineManager.ReplaceIssueInDocument(doc, targetItem, replacementCopy);
                        _onDocumentReplacedCallback?.Invoke();
                    };

                    _activeButtons.Add(btn);
                }
            }
            else
            {
                var infoBtn = (Office.CommandBarButton)bar.Controls.Add(
                    Office.MsoControlType.msoControlButton,
                    Before: insertPos++,
                    Temporary: true);

                infoBtn.Tag = MenuTag;
                infoBtn.Style = Office.MsoButtonStyle.msoButtonCaption;
                infoBtn.Caption = UiLocalizer.Pick($"Spelling Notice: {hitIssue.OriginalText}", $"သတ်ပုံသတိပေးချက်: {hitIssue.OriginalText}");
                infoBtn.Enabled = false;
                _activeButtons.Add(infoBtn);
            }

            var ignoreBtn = (Office.CommandBarButton)bar.Controls.Add(
                Office.MsoControlType.msoControlButton,
                Before: insertPos,
                Temporary: true);

            ignoreBtn.Tag = MenuTag;
            ignoreBtn.Style = Office.MsoButtonStyle.msoButtonCaption;
            ignoreBtn.Caption = UiLocalizer.Pick("Ignore Once (This Document)", "ယခုစာတမ်းအတွက်သာ ကျော်မည် (Ignore Once)");
            ignoreBtn.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
            {
                _engine.IgnoreWordInSession(originalWord);
                _onDocumentReplacedCallback?.Invoke();
            };

            _activeButtons.Add(ignoreBtn);
        }

        /// <summary>
        /// Non-blocking clean-selection helper: keeps Word's native Copy/Cut/Paste menu open (cancel remains false!)
        /// while injecting Dictionary Lookup, Synonym (ကြောင်းတူသံကွဲ) swap, Bilingual term swap, and Official Style rewrite.
        /// </summary>
        private void TryInjectCleanSelectionHelperMenu(Word.Document doc, Word.Selection sel)
        {
            string rawSel = sel.Range?.Text ?? string.Empty;
            string trimmed = rawSel.Trim(' ', '\t', '\r', '\n', '\a', '"', '“', '”', '\'', '‘', '’');
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 200)
                return;

            Word.Range targetRange = sel.Range!.Duplicate;
            while (targetRange.End > targetRange.Start)
            {
                string tail = doc.Range(targetRange.End - 1, targetRange.End).Text ?? string.Empty;
                if (tail == "\r" || tail == "\a" || tail == "\n" || tail == " ")
                    targetRange.End--;
                else
                    break;
            }

            List<string> synonyms = trimmed.Length <= 40 ? _engine.GetSynonyms(trimmed, 3) : new List<string>();
            OfficialRewriteResult rewrite = MyanmarOfficialRewriter.ConvertToOfficialProse(trimmed);

            string? bilingualMyanmarMatch = null;
            bool isAsciiQuery = trimmed.Length >= 2 && trimmed.Length <= 40 && trimmed.All(c => c <= 127);
            if (isAsciiQuery)
            {
                var hits = _engine.SearchDictionary(trimmed, 2);
                if (hits.Count > 0)
                    bilingualMyanmarMatch = hits[0].Word;
            }

            foreach (string barName in new[] { "Text", "Table Text" })
            {
                Office.CommandBar? bar = null;
                try { bar = _app.CommandBars[barName]; } catch { continue; }
                if (bar == null) continue;

                int pos = 1;

                if (trimmed.Length <= 45 && _onLookupWordInTaskPane != null)
                {
                    string preview = trimmed.Length > 22 ? trimmed.Substring(0, 20) + "…" : trimmed;
                    var lookupBtn = (Office.CommandBarButton)bar.Controls.Add(
                        Office.MsoControlType.msoControlButton, Before: pos++, Temporary: true);
                    lookupBtn.Tag = MenuTag;
                    lookupBtn.Style = Office.MsoButtonStyle.msoButtonCaption;
                    lookupBtn.Caption = UiLocalizer.Pick($"Look Up in Myanmar Dictionary: \"{preview}\"", $"အဘိဓာန်နှင့် သတ်ပုံကျမ်း ကြည့်မည်: \"{preview}\"");
                    string qCopy = trimmed;
                    lookupBtn.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                    {
                        _onLookupWordInTaskPane?.Invoke(qCopy);
                    };
                    _activeButtons.Add(lookupBtn);
                }

                if (!string.IsNullOrEmpty(bilingualMyanmarMatch))
                {
                    string mmTarget = bilingualMyanmarMatch!;
                    var biBtn = (Office.CommandBarButton)bar.Controls.Add(
                        Office.MsoControlType.msoControlButton, Before: pos++, Temporary: true);
                    biBtn.Tag = MenuTag;
                    biBtn.Style = Office.MsoButtonStyle.msoButtonCaption;
                    biBtn.Caption = UiLocalizer.Pick($"Replace with Myanmar Term ➔ {mmTarget}", $"မြန်မာဝေါဟာရ လဲမည် ➔ {mmTarget}");
                    biBtn.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                    {
                        ReplaceRangeWithUndo(targetRange, mmTarget, UiLocalizer.Pick("Replace English with Myanmar Term", "အင်္ဂလိပ်မှ မြန်မာဝေါဟာရသို့ လဲလှယ်ခြင်း"));
                    };
                    _activeButtons.Add(biBtn);
                }

                for (int s = 0; s < synonyms.Count; s++)
                {
                    string syn = synonyms[s];
                    var synBtn = (Office.CommandBarButton)bar.Controls.Add(
                        Office.MsoControlType.msoControlButton, Before: pos++, Temporary: true);
                    synBtn.Tag = MenuTag;
                    synBtn.Style = Office.MsoButtonStyle.msoButtonCaption;
                    synBtn.Caption = UiLocalizer.Pick($"Replace with Synonym ➔ {syn}", $"ကြောင်းတူသံကွဲ လဲမည် ➔ {syn}");
                    synBtn.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                    {
                        ReplaceRangeWithUndo(targetRange, syn, UiLocalizer.Pick($"Replace Synonym ({syn})", $"ကြောင်းတူသံကွဲ လဲလှယ်ခြင်း ({syn})"));
                    };
                    _activeButtons.Add(synBtn);
                }

                if (rewrite.ReplacementCount > 0 && rewrite.RewrittenText != trimmed)
                {
                    string rwText = rewrite.RewrittenText;
                    string rwPreview = rwText.Length > 28 ? rwText.Substring(0, 26) + "…" : rwText;
                    var rwBtn = (Office.CommandBarButton)bar.Controls.Add(
                        Office.MsoControlType.msoControlButton, Before: pos++, Temporary: true);
                    rwBtn.Tag = MenuTag;
                    rwBtn.Style = Office.MsoButtonStyle.msoButtonCaption;
                    rwBtn.Caption = UiLocalizer.Pick(
                        $"Rewrite to Official Style ({rewrite.ReplacementCount} change(s)): {rwPreview}",
                        $"ရုံးသုံးစာပေဟန်သို့ ပြောင်းမည် ({rewrite.ReplacementCount} ချက်): {rwPreview}");
                    rwBtn.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                    {
                        ReplaceRangeWithUndo(targetRange, rwText, UiLocalizer.Pick("Rewrite to Official Style", "ရုံးသုံးစာပေဟန်သို့ ပြောင်းလဲခြင်း"));
                    };
                    _activeButtons.Add(rwBtn);
                }
            }
        }

        private void ReplaceRangeWithUndo(Word.Range range, string replacement, string undoLabel)
        {
            Word.UndoRecord? undo = null;
            bool startedOwnUndo = false;
            try
            {
                undo = _app.UndoRecord;
                if (undo != null && !undo.IsRecordingCustomRecord)
                {
                    undo.StartCustomRecord(undoLabel);
                    startedOwnUndo = true;
                }

                range.Text = replacement;
                _onDocumentReplacedCallback?.Invoke();
            }
            catch { }
            finally
            {
                if (startedOwnUndo && undo != null)
                {
                    try
                    {
                        if (undo.IsRecordingCustomRecord)
                            undo.EndCustomRecord();
                    }
                    catch { }
                }
            }
        }

        public void ClearCustomContextMenuItems()
        {
            try
            {
                _activeButtons.Clear();
                RemoveTaggedControlsFromBar("Text");
                RemoveTaggedControlsFromBar("Table Text");
                RemoveTaggedControlsFromBar("Spelling");
                RemoveTaggedControlsFromBar("Grammar");
                RemoveTaggedControlsFromBar("Grammar (2)");
            }
            catch
            {
                // Ignore
            }
        }

        private void RemoveTaggedControlsFromBar(string barName)
        {
            Office.CommandBar? bar = null;
            try { bar = _app.CommandBars[barName]; } catch { return; }
            if (bar == null) return;

            for (int i = bar.Controls.Count; i >= 1; i--)
            {
                Office.CommandBarControl ctrl = bar.Controls[i];
                if (ctrl.Tag == MenuTag)
                {
                    ctrl.Delete(Temporary: true);
                }
            }
        }
    }
}
