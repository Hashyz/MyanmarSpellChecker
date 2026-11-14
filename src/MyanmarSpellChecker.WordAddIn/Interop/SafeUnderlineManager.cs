using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using Word = Microsoft.Office.Interop.Word;

namespace MyanmarSpellChecker.WordAddIn.Interop
{
    public sealed class DocumentProofingItem
    {
        public string StoryName { get; set; } = UiLocalizer.Pick("Body", "စာကိုယ် (Body)");
        public Word.WdStoryType StoryType { get; set; } = Word.WdStoryType.wdMainTextStory;
        public int ParagraphIndex { get; set; }
        public string ParagraphSnippet { get; set; } = string.Empty;

        /// <summary>
        /// Live Word.Range COM object that automatically shifts when earlier text in the document is edited.
        /// May be resolved lazily on demand for issues beyond <see cref="SafeUnderlineManager.MaxLiveWavyUnderlinesPerDocument"/>
        /// on massive 100+ page books so Word never pins thousands of COM RCWs.
        /// </summary>
        public Word.Range? TargetRange { get; set; }

        /// <summary>
        /// Managed cached bounds updated on bind and cursor near-hits so arrow-key navigation avoids O(N) live COM calls.
        /// </summary>
        public int CachedStart { get; set; }
        public int CachedEnd { get; set; }

        public int FallbackStartOffset { get; set; }
        public int FallbackParagraphStart { get; set; } = -1;
        public int FallbackParagraphEnd { get; set; } = -1;
        public Word.WdUnderline OriginalUnderline { get; set; } = Word.WdUnderline.wdUnderlineNone;
        public Word.WdColor OriginalUnderlineColor { get; set; } = Word.WdColor.wdColorAutomatic;
        public ProofingIssue Issue { get; set; } = new ProofingIssue();
        public bool IsResolved { get; set; }
    }

    /// <summary>
    /// Manages Red (Spelling/Lookalike/Zawgyi) and Blue (Grammar/ရုံးသုံးမြန်မာစာ) wavy underlines
    /// using live Word.Range objects. Snapshots & restores original user underlines (e.g. wdUnderlineSingle on 'အကြောင်းအရာ ။')
    /// and automatically strips/restores wavy underlines across Save & Print events.
    /// </summary>
    public sealed class SafeUnderlineManager : IDisposable
    {
        public const int MaxLiveWavyUnderlinesPerDocument = 450;
        private const Word.WdColor WdColorAmberContextWarning = (Word.WdColor)0x00128AD9; // Warm Amber-Gold (#D98A12 in BGR)

        private static Word.WdColor GetUnderlineColorForIssue(ProofingIssue issue)
        {
            if (issue.IsContextualWarning)
                return WdColorAmberContextWarning;
            if (issue.IsGrammarOrStyle)
                return Word.WdColor.wdColorBlue;
            return Word.WdColor.wdColorRed;
        }

        private sealed class TrackedRangeState
        {
            public Word.Document Document { get; set; } = null!;
            public Word.Range Range { get; set; } = null!;
            public int CachedStart { get; set; }
            public int CachedEnd { get; set; }
            public Word.WdUnderline OriginalUnderline { get; set; }
            public Word.WdColor OriginalUnderlineColor { get; set; }
            public Word.WdColor AppliedUnderlineColor { get; set; }
        }

        private readonly Word.Application _app;
        private readonly List<TrackedRangeState> _trackedRanges = new List<TrackedRangeState>();
        private readonly Timer _postSaveRestoreTimer;
        private Word.Document? _pendingRestoreDoc;

        public SafeUnderlineManager(Word.Application app)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _postSaveRestoreTimer = new Timer { Interval = 450 };
            _postSaveRestoreTimer.Tick += OnPostSaveRestoreTimerTick;
        }

        public int GetTrackedCountForDocument(Word.Document doc)
        {
            if (doc == null || _trackedRanges.Count == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < _trackedRanges.Count; i++)
            {
                if (ReferenceEquals(_trackedRanges[i].Document, doc))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Zero-COM-allocation builder for issues beyond <see cref="MaxLiveWavyUnderlinesPerDocument"/> on massive documents.
        /// Keeps all issues 100% clickable, previewable, and batch-fixable without pinning thousands of live Word.Range RCWs.
        /// </summary>
        public static List<DocumentProofingItem> CreateUnboundProofingItems(
            string storyName,
            Word.WdStoryType storyType,
            int paragraphIndex,
            int paragraphStartOffset,
            int paragraphEndOffset,
            string cleanedParagraphText,
            IReadOnlyList<ProofingIssue> issues)
        {
            var list = new List<DocumentProofingItem>(issues.Count);
            string snippet = BuildTrimmedSnippet(cleanedParagraphText);
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                int estStart = paragraphStartOffset + Math.Max(0, issue.StartIndex);
                int estEnd = estStart + Math.Max(1, issue.Length);
                list.Add(new DocumentProofingItem
                {
                    StoryName = storyName,
                    StoryType = storyType,
                    ParagraphIndex = paragraphIndex,
                    ParagraphSnippet = snippet,
                    TargetRange = null,
                    CachedStart = estStart,
                    CachedEnd = estEnd,
                    FallbackStartOffset = estStart,
                    FallbackParagraphStart = paragraphStartOffset,
                    FallbackParagraphEnd = paragraphEndOffset,
                    OriginalUnderline = Word.WdUnderline.wdUnderlineNone,
                    OriginalUnderlineColor = Word.WdColor.wdColorAutomatic,
                    Issue = issue
                });
            }
            return list;
        }

        private static string BuildTrimmedSnippet(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string trimmed = text!.Trim();
            return trimmed.Length <= 240
                ? trimmed
                : MyanmarSpellChecker.Core.Nlp.MyanmarSyllableBreaker.TruncateAtSyllableBoundary(trimmed, 240, "…");
        }

        /// <summary>
        /// Binds issues in a story/paragraph range to live Word.Range objects, snapshots their original user underline
        /// formatting (such as wdUnderlineSingle on 'အကြောင်းအရာ ။'), and applies Red or Blue wavy underlines.
        /// Must run on the Word STA thread.
        /// </summary>
        public List<DocumentProofingItem> BindAndUnderlineRangeIssues(
            Word.Document doc,
            Word.Range containerRange,
            string storyName,
            int paragraphIndex,
            IReadOnlyList<ProofingIssue> issues,
            bool insideSharedBatch = false,
            bool applyVisualUnderline = true,
            string? preloadedCleanText = null,
            Word.WdStoryType storyType = Word.WdStoryType.wdMainTextStory,
            bool? knownDocHasNoUnderlines = null,
            int maxLiveUnderlines = MaxLiveWavyUnderlinesPerDocument)
        {
            var boundItems = new List<DocumentProofingItem>(issues.Count);
            if (doc == null || containerRange == null)
                return boundItems;

            bool wasSaved = !insideSharedBatch && doc.Saved;
            int containerStart = containerRange.Start;
            int containerEnd = containerRange.End;
            string cleanedContainerText = preloadedCleanText ?? CleanWordControlChars(containerRange.Text ?? string.Empty);
            string snippet = issues.Count > 0 ? BuildTrimmedSnippet(cleanedContainerText) : string.Empty;
            bool hasMyanmar = ContainsMyanmarCharacters(cleanedContainerText);
            bool hasExistingTracked = HasTrackedInSpan(doc, containerStart, containerEnd);
            bool hasPotentialWavy = false;
            if (knownDocHasNoUnderlines != true)
            {
                try
                {
                    Word.WdUnderline cu = containerRange.Font.Underline;
                    hasPotentialWavy = cu == Word.WdUnderline.wdUnderlineWavy || (int)cu == 9999999;
                }
                catch { }
            }

            // Fast no-op path: nothing to strip, no stray wavy underline, and no issues -> touch zero COM properties (preserves Ctrl+Z!)
            if (issues.Count == 0 && !hasExistingTracked && !hasPotentialWavy)
            {
                if (hasMyanmar && !insideSharedBatch)
                {
                    try
                    {
                        if (containerRange.NoProofing != 1)
                        {
                            containerRange.NoProofing = 1;
                            if (wasSaved) doc.Saved = true;
                        }
                    }
                    catch { }
                }
                return boundItems;
            }

            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;

            try
            {
                if (!insideSharedBatch)
                {
                    startedOwnUndo = TryBeginOwnUndoRecord(
                        UiLocalizer.Pick("Myanmar Spell Check", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးခြင်း"),
                        out undoRecord);
                }

                // Suppress MS Word's built-in English spell-checker ("No suggestions available") on Myanmar paragraphs
                if (hasMyanmar)
                {
                    try
                    {
                        if (containerRange.NoProofing != 1)
                            containerRange.NoProofing = 1;
                    }
                    catch
                    {
                        // Ignore if range is locked
                    }
                }

                // 1. Restore & remove any previously tracked wavy underlines inside this container range
                if (hasExistingTracked)
                {
                    RestoreAndRemoveTrackedInSpan(doc, containerStart, containerEnd);
                }

                // 1b. Scrub any residual/inherited wdUnderlineWavy (e.g. from pasting over wavy text or typing spaces/numbers)
                // while 100% preserving user underlines such as wdUnderlineSingle on 'အကြောင်းအရာ ။' and hyperlinks
                if (hasPotentialWavy)
                {
                    ScrubResidualWavyUnderlines(containerRange);
                }

                if (!applyVisualUnderline && issues.Count > 0)
                {
                    return CreateUnboundProofingItems(
                        storyName,
                        storyType,
                        paragraphIndex,
                        containerStart,
                        containerEnd,
                        cleanedContainerText,
                        issues);
                }

                int currentDocTracked = GetTrackedCountForDocument(doc);

                int effectiveUnderlineCap = Math.Max(10, maxLiveUnderlines);

                // 2. Bind each issue to an exact live Word.Range (immune to hidden HYPERLINK/citation field-code offset shifts!),
                //    snapshot original underline, and apply Red or Blue wavy underline.
                List<VisibleCharSpan>? lazyCharMap = null;
                foreach (var issue in issues)
                {
                    if (currentDocTracked >= effectiveUnderlineCap)
                    {
                        int estStart = containerStart + Math.Max(0, issue.StartIndex);
                        boundItems.Add(new DocumentProofingItem
                        {
                            StoryName = storyName,
                            StoryType = storyType,
                            ParagraphIndex = paragraphIndex,
                            ParagraphSnippet = snippet,
                            TargetRange = null,
                            CachedStart = estStart,
                            CachedEnd = estStart + Math.Max(1, issue.Length),
                            FallbackStartOffset = estStart,
                            FallbackParagraphStart = containerStart,
                            FallbackParagraphEnd = containerEnd,
                            OriginalUnderline = Word.WdUnderline.wdUnderlineNone,
                            OriginalUnderlineColor = Word.WdColor.wdColorAutomatic,
                            Issue = issue
                        });
                        continue;
                    }

                    try
                    {
                        Word.Range? issueRange = ResolveIssueRange(
                            containerRange,
                            cleanedContainerText,
                            issue.StartIndex,
                            issue.Length,
                            issue.OriginalText,
                            ref lazyCharMap);

                        if (issueRange == null)
                            continue;

                        int resolvedStart = issueRange.Start;
                        int resolvedEnd = issueRange.End;
                        Word.WdUnderline origUnderline = Word.WdUnderline.wdUnderlineNone;
                        Word.WdColor origColor = Word.WdColor.wdColorAutomatic;
                        if (knownDocHasNoUnderlines != true)
                        {
                            try
                            {
                                origUnderline = issueRange.Font.Underline;
                                origColor = issueRange.Font.UnderlineColor;
                            }
                            catch { }

                            // Guard against undefined or previously applied wavy underline
                            if (origUnderline == Word.WdUnderline.wdUnderlineWavy ||
                                (int)origUnderline == 9999999)
                            {
                                origUnderline = Word.WdUnderline.wdUnderlineNone;
                                origColor = Word.WdColor.wdColorAutomatic;
                            }
                        }

                        Word.WdColor appliedColor = GetUnderlineColorForIssue(issue);
                        issueRange.Font.Underline = Word.WdUnderline.wdUnderlineWavy;
                        issueRange.Font.UnderlineColor = appliedColor;

                        _trackedRanges.Add(new TrackedRangeState
                        {
                            Document = doc,
                            Range = issueRange,
                            CachedStart = resolvedStart,
                            CachedEnd = resolvedEnd,
                            OriginalUnderline = origUnderline,
                            OriginalUnderlineColor = origColor,
                            AppliedUnderlineColor = appliedColor
                        });
                        currentDocTracked++;

                        boundItems.Add(new DocumentProofingItem
                        {
                            StoryName = storyName,
                            StoryType = storyType,
                            ParagraphIndex = paragraphIndex,
                            ParagraphSnippet = snippet,
                            TargetRange = issueRange,
                            CachedStart = resolvedStart,
                            CachedEnd = resolvedEnd,
                            FallbackStartOffset = resolvedStart,
                            FallbackParagraphStart = containerStart,
                            FallbackParagraphEnd = containerEnd,
                            OriginalUnderline = origUnderline,
                            OriginalUnderlineColor = origColor,
                            Issue = issue
                        });
                    }
                    catch
                    {
                        // Never let a single locked/protected range abort underlining the remaining issues in the paragraph
                    }
                }

                // Prevent collapsed typing caret right at the end of a flagged word from inheriting wavy underline on subsequent keystrokes,
                // ONLY when the caret does NOT sit on/inside any newly bound issue span (otherwise Word's word-at-IP rule strips the last pasted word's wavy underline!).
                if (!insideSharedBatch)
                {
                    try
                    {
                        Word.Selection? sel = _app.Selection;
                        if (sel != null && sel.Start == sel.End && sel.Font.Underline == Word.WdUnderline.wdUnderlineWavy)
                        {
                            int selPos = sel.Start;
                            bool touchesBoundIssue = false;
                            for (int bi = 0; bi < boundItems.Count; bi++)
                            {
                                if (selPos >= boundItems[bi].CachedStart && selPos <= boundItems[bi].CachedEnd)
                                {
                                    touchesBoundIssue = true;
                                    break;
                                }
                            }

                            if (!touchesBoundIssue)
                            {
                                sel.Font.Underline = Word.WdUnderline.wdUnderlineNone;
                                sel.Font.UnderlineColor = Word.WdColor.wdColorAutomatic;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                // Ignore transient COM range boundary errors during rapid user edits
            }
            finally
            {
                if (!insideSharedBatch)
                {
                    EndOwnUndoAndPreserveSaved(undoRecord, startedOwnUndo, doc, wasSaved);
                }
            }

            return boundItems;
        }

        /// <summary>
        /// Executes an STA chunk of paragraph underline bindings inside a single shared UndoRecord slice,
        /// batching ScreenUpdating so newly applied wavy underlines repaint crisply and immediately while preserving Document.Saved state.
        /// </summary>
        public void ExecuteInSharedProofingBatch(Word.Document doc, Action batchAction)
        {
            if (doc == null || batchAction == null)
                return;

            bool wasSaved = false;
            try { wasSaved = doc.Saved; } catch { }

            bool prevScreenUpdating = true;
            bool toggledScreenUpdating = false;
            try
            {
                prevScreenUpdating = _app.ScreenUpdating;
                if (prevScreenUpdating)
                {
                    _app.ScreenUpdating = false;
                    toggledScreenUpdating = true;
                }
            }
            catch { }

            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            try
            {
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick("Myanmar Spell Check", "မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးခြင်း"),
                    out undoRecord);
                batchAction();
            }
            catch
            {
                // Ignore transient COM errors during rapid user edits
            }
            finally
            {
                EndOwnUndoAndPreserveSaved(undoRecord, startedOwnUndo, doc, wasSaved);
                if (toggledScreenUpdating)
                {
                    try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
                }
            }
        }

        /// <summary>
        /// Releases all tracked COM Range references associated with a closing or closed document.
        /// </summary>
        public void PurgeClosedDocument(Word.Document? doc)
        {
            if (doc != null && ReferenceEquals(_pendingRestoreDoc, doc))
            {
                _postSaveRestoreTimer.Stop();
                _pendingRestoreDoc = null;
            }

            for (int i = _trackedRanges.Count - 1; i >= 0; i--)
            {
                var tracked = _trackedRanges[i];
                if (doc != null && ReferenceEquals(tracked.Document, doc))
                {
                    _trackedRanges.RemoveAt(i);
                    continue;
                }

                // Also prune any orphaned range whose underlying COM document has already disconnected
                try
                {
                    _ = tracked.Range.Start;
                }
                catch
                {
                    _trackedRanges.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Lazily resolves a <see cref="Word.Range"/> for an unbound issue (beyond <see cref="MaxLiveWavyUnderlinesPerDocument"/>)
        /// on demand when the user clicks to jump to it or approves it for replacement.
        /// </summary>
        public Word.Range? EnsureTargetRangeResolved(Word.Document? doc, DocumentProofingItem? item)
        {
            if (item == null)
                return null;
            if (item.TargetRange != null)
                return item.TargetRange;

            Word.Document? targetDoc = doc;
            if (targetDoc == null)
            {
                try { targetDoc = _app.ActiveDocument; } catch { }
            }
            if (targetDoc == null)
                return null;

            try
            {
                Word.Range? storyRange = null;
                try { storyRange = targetDoc.StoryRanges[item.StoryType]; } catch { }
                storyRange ??= targetDoc.Content;
                if (storyRange == null)
                    return null;

                int storyStart = storyRange.Start;
                int storyEnd = storyRange.End;
                string expected = item.Issue.OriginalText ?? string.Empty;
                if (string.IsNullOrEmpty(expected) || storyEnd <= storyStart)
                    return null;

                // 1. Direct offset fast-path
                int directStart = item.FallbackStartOffset;
                int directEnd = directStart + item.Issue.Length;
                if (directStart >= storyStart && directEnd <= storyEnd)
                {
                    Word.Range probe = storyRange.Duplicate;
                    probe.SetRange(directStart, directEnd);
                    if (string.Equals(CleanWordControlChars(probe.Text ?? string.Empty), CleanWordControlChars(expected), StringComparison.Ordinal))
                    {
                        item.TargetRange = probe;
                        item.CachedStart = directStart;
                        item.CachedEnd = directEnd;
                        return probe;
                    }
                }

                // 2. Windowed search around fallback paragraph / offset (absorbs any earlier edit shifts)
                int winStart = item.FallbackParagraphStart >= 0
                    ? Math.Max(storyStart, item.FallbackParagraphStart - 512)
                    : Math.Max(storyStart, item.FallbackStartOffset - 512);
                int winEnd = item.FallbackParagraphEnd > item.FallbackParagraphStart
                    ? Math.Min(storyEnd, item.FallbackParagraphEnd + 512)
                    : Math.Min(storyEnd, item.FallbackStartOffset + expected.Length + 512);

                if (winEnd > winStart)
                {
                    Word.Range winRange = storyRange.Duplicate;
                    winRange.SetRange(winStart, winEnd);
                    string winText = CleanWordControlChars(winRange.Text ?? string.Empty);
                    int approxLocal = Math.Max(0, Math.Min(winText.Length, item.FallbackStartOffset - winStart));
                    int bestIdx = FindNearestExactSubstring(winText, CleanWordControlChars(expected), approxLocal);
                    if (bestIdx >= 0)
                    {
                        Word.Range? resolved = ResolveIssueRange(winRange, winText, bestIdx, item.Issue.Length, expected);
                        if (resolved != null)
                        {
                            item.TargetRange = resolved;
                            item.CachedStart = resolved.Start;
                            item.CachedEnd = resolved.End;
                            return resolved;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static int FindNearestExactSubstring(string haystack, string needle, int preferredIndex)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return -1;

            int bestIdx = -1;
            int bestDist = int.MaxValue;
            int pos = 0;
            while (pos <= haystack.Length - needle.Length)
            {
                int hit = haystack.IndexOf(needle, pos, StringComparison.Ordinal);
                if (hit < 0)
                    break;

                int dist = Math.Abs(hit - preferredIndex);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = hit;
                    if (dist == 0)
                        break;
                }
                pos = hit + 1;
            }
            return bestIdx;
        }

        /// <summary>
        /// Scrolls the Word window to the issue's live Word.Range and selects it in the document.
        /// </summary>
        public void SelectAndScrollToIssue(DocumentProofingItem item)
        {
            Word.Range? range = EnsureTargetRangeResolved(null, item);
            if (range == null)
                return;

            try
            {
                range.Select();
                _app.ActiveWindow?.ScrollIntoView(range, true);
            }
            catch
            {
                // Ignore if range is inside an inactive header/footer pane
            }
        }

        /// <summary>
        /// Replaces a single issue using its live Word.Range and restores its original user underline style
        /// (e.g., preserving wdUnderlineSingle on 'အကြောင်းအရာ ။').
        /// </summary>
        public void ReplaceIssueInDocument(Word.Document doc, DocumentProofingItem item, string replacementText)
        {
            if (doc == null || item == null)
                return;

            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            try
            {
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick($"Myanmar Fix ({replacementText})", $"သတ်ပုံပြင်ဆင်ခြင်း ({replacementText})"),
                    out undoRecord);

                Word.Range? r = EnsureTargetRangeResolved(doc, item);
                if (r != null)
                {
                    r.Font.Underline = item.OriginalUnderline;
                    r.Font.UnderlineColor = item.OriginalUnderlineColor;
                    r.Text = replacementText;
                    // Re-apply after r.Text assignment in case Word inherited adjacent wavy underline
                    r.Font.Underline = item.OriginalUnderline;
                    r.Font.UnderlineColor = item.OriginalUnderlineColor;
                    item.IsResolved = true;

                    for (int i = _trackedRanges.Count - 1; i >= 0; i--)
                    {
                        if (ReferenceEquals(_trackedRanges[i].Range, r))
                        {
                            _trackedRanges.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            finally
            {
                EndOwnUndoRecord(undoRecord, startedOwnUndo);
            }
        }

        /// <summary>
        /// Replaces all unresolved issues in the document sharing the exact same <paramref name="originalText"/>
        /// in reverse document order inside a SINGLE UndoRecord (Ctrl+Z undoes all at once).
        /// </summary>
        public int ReplaceMatchingIssuesInDocument(
            Word.Document doc,
            IReadOnlyList<DocumentProofingItem> items,
            string originalText,
            string replacementText)
        {
            if (doc == null || items == null || items.Count == 0 || string.IsNullOrEmpty(originalText))
                return 0;

            string targetNorm = UnicodeNormalizer.NormalizeCanonicalOrder(originalText.Trim());
            int replacedCount = 0;
            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            bool prevScreenUpdating = true;

            try
            {
                try { prevScreenUpdating = _app.ScreenUpdating; _app.ScreenUpdating = false; } catch { }
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick(
                        $"Batch Fix Matching ({originalText} -> {replacementText})",
                        $"တူညီသောအမှားများ တစ်ပြိုင်နက်ပြင်ဆင်ခြင်း ({originalText} -> {replacementText})"),
                    out undoRecord);

                var matching = items
                    .Where(i => !i.IsResolved &&
                                UnicodeNormalizer.NormalizeCanonicalOrder((i.Issue.OriginalText ?? string.Empty).Trim()) == targetNorm)
                    .OrderByDescending(GetSortOffsetSafe)
                    .ToList();

                var resolvedRanges = new HashSet<Word.Range>();
                foreach (var item in matching)
                {
                    try
                    {
                        Word.Range? r = EnsureTargetRangeResolved(doc, item);
                        if (r == null)
                            continue;

                        r.Font.Underline = item.OriginalUnderline;
                        r.Font.UnderlineColor = item.OriginalUnderlineColor;
                        r.Text = replacementText;
                        r.Font.Underline = item.OriginalUnderline;
                        r.Font.UnderlineColor = item.OriginalUnderlineColor;
                        item.IsResolved = true;
                        resolvedRanges.Add(r);
                        replacedCount++;
                    }
                    catch
                    {
                        // Continue with remaining occurrences
                    }
                }

                if (resolvedRanges.Count > 0)
                {
                    _trackedRanges.RemoveAll(t => resolvedRanges.Contains(t.Range));
                }
            }
            catch
            {
                // Ignore COM errors
            }
            finally
            {
                EndOwnUndoRecord(undoRecord, startedOwnUndo);
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }

            return replacedCount;
        }

        /// <summary>
        /// Replaces all unresolved issues that have suggestions in reverse document order inside a SINGLE UndoRecord
        /// so pressing Ctrl+Z once undoes the entire batch.
        /// </summary>
        public int ReplaceAllIssuesInDocument(
            Word.Document doc,
            IReadOnlyList<DocumentProofingItem> items,
            Func<DocumentProofingItem, bool>? filterPredicate = null,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (doc == null || items == null || items.Count == 0)
                return 0;

            int replacedCount = 0;
            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            bool prevScreenUpdating = true;

            try
            {
                try { prevScreenUpdating = _app.ScreenUpdating; _app.ScreenUpdating = false; } catch { }
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick("Myanmar Fix All", "မြန်မာစာ အမှားအားလုံး တစ်ပြိုင်နက် ပြင်ဆင်ခြင်း"),
                    out undoRecord);

                // Sort in reverse order by live Range.Start so earlier replacements never affect later ranges.
                // Always skip Amber 'အနက်သံသယ (Context Check)' warnings so ambiguous meanings require explicit human approval.
                var ordered = items
                    .Where(i => !i.IsResolved &&
                                i.Issue.IsAutoFixSafe &&
                                (filterPredicate == null || filterPredicate(i)))
                    .OrderByDescending(GetSortOffsetSafe)
                    .ToList();

                var resolvedRanges = new HashSet<Word.Range>();
                var touchedMainIntervals = new List<(int Start, int End)>(Math.Min(ordered.Count, 256));
                var pumpWatch = System.Diagnostics.Stopwatch.StartNew();

                for (int idx = 0; idx < ordered.Count; idx++)
                {
                    if (isCancelled != null && isCancelled())
                        break;

                    var item = ordered[idx];
                    try
                    {
                        Word.Range? r = EnsureTargetRangeResolved(doc, item);
                        if (r == null)
                            continue;

                        string topReplacement = item.Issue.Suggestions[0].Replacement;
                        r.Font.Underline = item.OriginalUnderline;
                        r.Font.UnderlineColor = item.OriginalUnderlineColor;
                        r.Text = topReplacement;
                        r.Font.Underline = item.OriginalUnderline;
                        r.Font.UnderlineColor = item.OriginalUnderlineColor;
                        item.IsResolved = true;
                        resolvedRanges.Add(r);
                        replacedCount++;

                        if (item.StoryType == Word.WdStoryType.wdMainTextStory)
                        {
                            try { touchedMainIntervals.Add((r.Start, r.End)); } catch { }
                        }
                    }
                    catch
                    {
                        // Continue with remaining items
                    }

                    if (pumpWatch.ElapsedMilliseconds >= 110)
                    {
                        pumpWatch.Restart();
                        int pct = ((idx + 1) * 95) / Math.Max(1, ordered.Count);
                        onProgress?.Invoke(
                            pct,
                            UiLocalizer.Pick(
                                $"⏳ Fixing... {idx + 1:N0}/{ordered.Count:N0}",
                                $"⏳ ပြင်ဆင်နေသည်... {idx + 1:N0}/{ordered.Count:N0}"));
                        Application.DoEvents();
                    }
                }

                if (resolvedRanges.Count > 0)
                {
                    _trackedRanges.RemoveAll(t => resolvedRanges.Contains(t.Range));
                }

                ScrubCoalescedMainStoryIntervals(doc, touchedMainIntervals);

                foreach (var rem in items.Where(i => !i.IsResolved && i.TargetRange != null))
                {
                    try
                    {
                        rem.TargetRange!.Font.Underline = Word.WdUnderline.wdUnderlineWavy;
                        rem.TargetRange!.Font.UnderlineColor = GetUnderlineColorForIssue(rem.Issue);
                    }
                    catch { }
                }
            }
            finally
            {
                EndOwnUndoRecord(undoRecord, startedOwnUndo);
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }

            return replacedCount;
        }

        /// <summary>
        /// Replaces only the exact <see cref="DocumentProofingItem"/> spans approved (☑) by the user in the
        /// Safe Fix-All Preview Checklist Dialog, wrapped inside a single Word UndoRecord so Ctrl+Z undoes the batch cleanly.
        /// </summary>
        public int ReplaceApprovedIssuesInDocument(
            Word.Document doc,
            IEnumerable<DocumentProofingItem> items,
            IReadOnlyDictionary<DocumentProofingItem, string> approvedReplacements,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (doc == null || items == null || approvedReplacements == null || approvedReplacements.Count == 0)
                return 0;

            int replacedCount = 0;
            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            bool prevScreenUpdating = true;

            try
            {
                try { prevScreenUpdating = _app.ScreenUpdating; _app.ScreenUpdating = false; } catch { }
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick("Myanmar Fix Approved Issues", "မြန်မာစာ စိစစ်ရွေးချယ်ထားသော အမှားများ ပြင်ဆင်ခြင်း"),
                    out undoRecord);

                var allList = items as IReadOnlyList<DocumentProofingItem> ?? items.ToList();
                var ordered = allList
                    .Where(i => !i.IsResolved &&
                                approvedReplacements.TryGetValue(i, out string? rep) &&
                                !string.IsNullOrEmpty(rep))
                    .OrderByDescending(GetSortOffsetSafe)
                    .ToList();

                var resolvedRanges = new HashSet<Word.Range>();
                var touchedMainIntervals = new List<(int Start, int End)>(Math.Min(ordered.Count, 256));
                var pumpWatch = System.Diagnostics.Stopwatch.StartNew();

                for (int idx = 0; idx < ordered.Count; idx++)
                {
                    if (isCancelled != null && isCancelled())
                        break;

                    var item = ordered[idx];
                    if (!approvedReplacements.TryGetValue(item, out string? chosenReplacement) || string.IsNullOrEmpty(chosenReplacement))
                        continue;

                    try
                    {
                        Word.Range? r = EnsureTargetRangeResolved(doc, item);
                        if (r == null)
                            continue;

                        r.Font.Underline = item.OriginalUnderline;
                        r.Font.UnderlineColor = item.OriginalUnderlineColor;
                        r.Text = chosenReplacement;
                        r.Font.Underline = item.OriginalUnderline;
                        r.Font.UnderlineColor = item.OriginalUnderlineColor;
                        item.IsResolved = true;
                        resolvedRanges.Add(r);
                        replacedCount++;

                        if (item.StoryType == Word.WdStoryType.wdMainTextStory)
                        {
                            try { touchedMainIntervals.Add((r.Start, r.End)); } catch { }
                        }
                    }
                    catch
                    {
                        // Continue with remaining items
                    }

                    if (pumpWatch.ElapsedMilliseconds >= 110)
                    {
                        pumpWatch.Restart();
                        int pct = ((idx + 1) * 95) / Math.Max(1, ordered.Count);
                        onProgress?.Invoke(
                            pct,
                            UiLocalizer.Pick(
                                $"⏳ Fixing... {idx + 1:N0}/{ordered.Count:N0}",
                                $"⏳ ပြင်ဆင်နေသည်... {idx + 1:N0}/{ordered.Count:N0}"));
                        Application.DoEvents();
                    }
                }

                if (resolvedRanges.Count > 0)
                {
                    _trackedRanges.RemoveAll(t => resolvedRanges.Contains(t.Range));
                }

                ScrubCoalescedMainStoryIntervals(doc, touchedMainIntervals);

                foreach (var rem in allList.Where(i => !i.IsResolved && i.TargetRange != null))
                {
                    try
                    {
                        rem.TargetRange!.Font.Underline = Word.WdUnderline.wdUnderlineWavy;
                        rem.TargetRange!.Font.UnderlineColor = GetUnderlineColorForIssue(rem.Issue);
                    }
                    catch { }
                }
            }
            finally
            {
                EndOwnUndoRecord(undoRecord, startedOwnUndo);
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }

            return replacedCount;
        }

        private static int GetSortOffsetSafe(DocumentProofingItem item)
        {
            if (item.TargetRange != null)
            {
                try { return item.TargetRange.Start; } catch { }
            }
            return item.FallbackStartOffset;
        }

        private static void ScrubCoalescedMainStoryIntervals(Word.Document doc, List<(int Start, int End)> intervals)
        {
            if (intervals.Count == 0)
                return;

            try
            {
                Word.Range content = doc.Content;
                int docEnd = content.End;
                intervals.Sort((a, b) => a.Start.CompareTo(b.Start));

                int curStart = Math.Max(0, intervals[0].Start - 48);
                int curEnd = Math.Min(docEnd, intervals[0].End + 48);

                for (int i = 1; i < intervals.Count; i++)
                {
                    int nextStart = Math.Max(0, intervals[i].Start - 48);
                    int nextEnd = Math.Min(docEnd, intervals[i].End + 48);
                    if (nextStart <= curEnd + 512)
                    {
                        if (nextEnd > curEnd)
                            curEnd = nextEnd;
                    }
                    else
                    {
                        if (curEnd > curStart)
                        {
                            Word.Range span = doc.Range(curStart, curEnd);
                            ScrubResidualWavyUnderlines(span);
                        }
                        curStart = nextStart;
                        curEnd = nextEnd;
                    }
                }

                if (curEnd > curStart)
                {
                    Word.Range span = doc.Range(curStart, curEnd);
                    ScrubResidualWavyUnderlines(span);
                }
            }
            catch { }
        }

        /// <summary>
        /// Strips all temporary Red/Blue wavy underlines and restores original user underlines (such as wdUnderlineSingle).
        /// </summary>
        public void StripAllProofingUnderlines(Word.Document doc, bool clearTrackingList = true)
        {
            if (doc == null) return;

            bool wasSaved = false;
            try { wasSaved = doc.Saved; } catch { }
            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            bool prevScreenUpdating = true;

            try
            {
                try { prevScreenUpdating = _app.ScreenUpdating; _app.ScreenUpdating = false; } catch { }
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick("Clear Myanmar Proofing Underlines", "မြန်မာစာ မျဉ်းလှိုင်းများ ဖယ်ရှားခြင်း"),
                    out undoRecord);

                for (int i = _trackedRanges.Count - 1; i >= 0; i--)
                {
                    var tracked = _trackedRanges[i];
                    if (!ReferenceEquals(tracked.Document, doc))
                        continue;

                    try
                    {
                        tracked.Range.Font.Underline = tracked.OriginalUnderline;
                        tracked.Range.Font.UnderlineColor = tracked.OriginalUnderlineColor;
                    }
                    catch
                    {
                        // Ignore deleted range
                    }

                    if (clearTrackingList)
                    {
                        _trackedRanges.RemoveAt(i);
                    }
                }

                if (clearTrackingList)
                {
                    ScrubResidualWavyUnderlines(doc.Content);
                }
            }
            catch
            {
                // Ignore
            }
            finally
            {
                EndOwnUndoAndPreserveSaved(undoRecord, startedOwnUndo, doc, wasSaved);
                try { _app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        /// <summary>
        /// Called on DocumentBeforeSave and DocumentBeforePrint:
        /// Strips temporary wavy underlines (restoring original user underlines) before Word saves/prints,
        /// and schedules automatic restoration of wavy underlines on remaining unfixed errors 450ms later
        /// while keeping Document.Saved = true.
        /// </summary>
        public void TemporarilyStripForSaveOrPrint(Word.Document doc)
        {
            if (doc == null) return;

            StripAllProofingUnderlines(doc, clearTrackingList: false);
            _pendingRestoreDoc = doc;
            _postSaveRestoreTimer.Stop();
            _postSaveRestoreTimer.Start();
        }

        private void OnPostSaveRestoreTimerTick(object? sender, EventArgs e)
        {
            _postSaveRestoreTimer.Stop();
            Word.Document? doc = _pendingRestoreDoc;
            _pendingRestoreDoc = null;

            if (doc == null) return;

            Word.UndoRecord? undoRecord = null;
            bool startedOwnUndo = false;
            try
            {
                startedOwnUndo = TryBeginOwnUndoRecord(
                    UiLocalizer.Pick("Restore Myanmar Proofing Underlines", "မြန်မာစာ မျဉ်းလှိုင်းများ ပြန်လည်ဖော်ပြခြင်း"),
                    out undoRecord);

                foreach (var tracked in _trackedRanges)
                {
                    if (!ReferenceEquals(tracked.Document, doc))
                        continue;

                    try
                    {
                        tracked.Range.Font.Underline = Word.WdUnderline.wdUnderlineWavy;
                        tracked.Range.Font.UnderlineColor = tracked.AppliedUnderlineColor;
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
            catch
            {
                // Ignore
            }
            finally
            {
                // Always keep doc.Saved = true after post-save/post-print restoration
                EndOwnUndoAndPreserveSaved(undoRecord, startedOwnUndo, doc, wasSaved: true);
            }
        }

        private struct VisibleCharSpan
        {
            public int VisibleStart;
            public int VisibleLength;
            public int StoryStart;
            public int StoryEnd;
        }

        /// <summary>
        /// Resolves the exact live <see cref="Word.Range"/> for a proofing issue inside <paramref name="containerRange"/>,
        /// accurately compensating for hidden field codes (such as HYPERLINK URLs, Wikipedia [1] citations, footnotes,
        /// TOC/cross-reference fields, or inline shapes) where <c>Range.Start</c> includes hidden field characters
        /// while <c>Range.Text</c> contains only visible display text.
        /// </summary>
        public Word.Range? ResolveIssueRange(
            Word.Range containerRange,
            string rawOrCleanedContainerText,
            int issueStartIndex,
            int issueLength,
            string expectedText)
        {
            List<VisibleCharSpan>? lazyMap = null;
            return ResolveIssueRange(
                containerRange,
                CleanWordControlChars(rawOrCleanedContainerText ?? string.Empty),
                issueStartIndex,
                issueLength,
                expectedText,
                ref lazyMap);
        }

        private static Word.Range? ResolveIssueRange(
            Word.Range containerRange,
            string cleanedContainerText,
            int issueStartIndex,
            int issueLength,
            string expectedText,
            ref List<VisibleCharSpan>? lazyCharMap)
        {
            if (containerRange == null || issueLength <= 0 || issueStartIndex < 0)
                return null;

            string cleanExpected = CleanWordControlChars(expectedText ?? string.Empty);
            if (string.IsNullOrEmpty(cleanExpected))
                return null;

            int containerStart = containerRange.Start;
            int containerEnd = containerRange.End;
            if (containerEnd <= containerStart)
                return null;

            // Tier 1: Fast O(1) 1:1 Story Offset Path (handles all normal plain-text paragraphs with zero extra COM overhead)
            int directStart = containerStart + issueStartIndex;
            int directEnd = directStart + issueLength;
            if (directStart >= containerStart && directEnd <= containerEnd)
            {
                try
                {
                    Word.Range direct = containerRange.Duplicate;
                    direct.SetRange(directStart, directEnd);
                    string directText = CleanWordControlChars(direct.Text ?? string.Empty);
                    if (string.Equals(directText, cleanExpected, StringComparison.Ordinal))
                    {
                        return direct;
                    }
                }
                catch { }
            }

            // Tier 2: Hyperlink / Field-Aware Exact Nth-Occurrence Word.Find inside containerRange
            int targetOccurrence = CountOccurrencesUpTo(cleanedContainerText, cleanExpected, issueStartIndex);
            string nonNullExpected = expectedText ?? string.Empty;
            if (nonNullExpected.Length > 0 && nonNullExpected.IndexOfAny(new[] { '^', '\r', '\n', '\a', '\v', '\f' }) < 0)
            {
                try
                {
                    Word.Range findScope = containerRange.Duplicate;
                    findScope.SetRange(containerStart, containerEnd);
                    Word.Find find = findScope.Find;
                    find.ClearFormatting();
                    find.Format = false;
                    find.MatchCase = true;
                    find.MatchWholeWord = false;
                    find.MatchWildcards = false;
                    find.MatchSoundsLike = false;
                    find.MatchAllWordForms = false;
                    try { find.MatchDiacritics = true; } catch { }
                    try { find.MatchControl = true; } catch { }

                    int seenExact = 0;
                    int guard = 0;
                    while (guard++ < 256)
                    {
                        find.Text = expectedText;
                        find.Forward = true;
                        find.Wrap = Word.WdFindWrap.wdFindStop;
                        if (!find.Execute())
                            break;

                        int hitStart = findScope.Start;
                        int hitEnd = findScope.End;
                        if (hitStart < containerStart || hitEnd > containerEnd)
                            break;

                        string hitText = CleanWordControlChars(findScope.Text ?? string.Empty);
                        if (string.Equals(hitText, cleanExpected, StringComparison.Ordinal))
                        {
                            seenExact++;
                            if (seenExact >= targetOccurrence)
                            {
                                Word.Range exactHit = containerRange.Duplicate;
                                exactHit.SetRange(hitStart, hitEnd);
                                return exactHit;
                            }
                        }

                        int nextPos = hitStart + 1;
                        if (nextPos >= containerEnd)
                            break;
                        findScope.SetRange(nextPos, containerEnd);
                    }
                }
                catch { }
            }

            // Tier 3: Visible-Character Subrange Mapper Fallback
            // Handles malformed Indic combining clusters (such as shifted Kinzi 'သကေင်္တ' inside 'သကေင်္တများဖြင့်')
            // where Word.Find's Indic cluster matcher may decline a partial-word match inside a hyperlinked paragraph.
            try
            {
                if (lazyCharMap == null)
                {
                    lazyCharMap = BuildVisibleCharacterMap(containerRange, containerStart, containerEnd);
                }

                return ResolveFromVisibleCharacterMap(
                    containerRange,
                    lazyCharMap,
                    containerStart,
                    containerEnd,
                    issueStartIndex,
                    issueLength,
                    cleanExpected);
            }
            catch
            {
                return null;
            }
        }

        private static int CountOccurrencesUpTo(string text, string pattern, int targetStartIndex)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
                return 1;

            int count = 0;
            int searchFrom = 0;
            while (searchFrom <= targetStartIndex && searchFrom < text.Length)
            {
                int idx = text.IndexOf(pattern, searchFrom, StringComparison.Ordinal);
                if (idx < 0 || idx > targetStartIndex)
                    break;
                count++;
                searchFrom = idx + 1;
            }
            return Math.Max(1, count);
        }

        private static List<VisibleCharSpan> BuildVisibleCharacterMap(Word.Range containerRange, int containerStart, int containerEnd)
        {
            var map = new List<VisibleCharSpan>(128);
            Word.Range iterRange = containerRange.Duplicate;
            try
            {
                iterRange.TextRetrievalMode.IncludeFieldCodes = false;
                iterRange.TextRetrievalMode.IncludeHiddenText = false;
            }
            catch { }

            int visiblePos = 0;
            foreach (Word.Range ch in iterRange.Characters)
            {
                string rawCh = ch.Text ?? string.Empty;
                if (rawCh.Length == 0)
                    continue;

                int sStart = ch.Start;
                int sEnd = ch.End;
                int cleanStart = sStart;
                int cleanEnd = sEnd;

                // If a character immediately adjacent to a HYPERLINK field includes hidden field-code offsets in ch.Start..ch.End,
                // narrow cleanStart..cleanEnd to the exact visible character positions so hyperlinks are never overwritten.
                if (sEnd - sStart > rawCh.Length)
                {
                    try
                    {
                        Word.Range tailProbe = containerRange.Duplicate;
                        tailProbe.SetRange(Math.Max(containerStart, sEnd - rawCh.Length), sEnd);
                        if (string.Equals(tailProbe.Text, rawCh, StringComparison.Ordinal))
                        {
                            cleanStart = sEnd - rawCh.Length;
                            cleanEnd = sEnd;
                        }
                        else
                        {
                            Word.Range headProbe = containerRange.Duplicate;
                            headProbe.SetRange(sStart, Math.Min(containerEnd, sStart + rawCh.Length));
                            if (string.Equals(headProbe.Text, rawCh, StringComparison.Ordinal))
                            {
                                cleanStart = sStart;
                                cleanEnd = sStart + rawCh.Length;
                            }
                        }
                    }
                    catch { }
                }

                map.Add(new VisibleCharSpan
                {
                    VisibleStart = visiblePos,
                    VisibleLength = rawCh.Length,
                    StoryStart = cleanStart,
                    StoryEnd = cleanEnd
                });
                visiblePos += rawCh.Length;
            }

            return map;
        }

        private static Word.Range? ResolveFromVisibleCharacterMap(
            Word.Range containerRange,
            List<VisibleCharSpan> map,
            int containerStart,
            int containerEnd,
            int issueStart,
            int issueLength,
            string cleanExpected)
        {
            if (map.Count == 0)
                return null;

            int issueEnd = issueStart + issueLength;
            int resolvedStoryStart = -1;
            int resolvedStoryEnd = -1;

            for (int i = 0; i < map.Count; i++)
            {
                VisibleCharSpan sp = map[i];
                int spVisEnd = sp.VisibleStart + sp.VisibleLength;

                if (resolvedStoryStart < 0 && issueStart >= sp.VisibleStart && issueStart < spVisEnd)
                {
                    int delta = issueStart - sp.VisibleStart;
                    resolvedStoryStart = sp.StoryStart + delta;
                }

                if (issueEnd > sp.VisibleStart && issueEnd <= spVisEnd)
                {
                    int deltaFromStart = issueEnd - sp.VisibleStart;
                    resolvedStoryEnd = (sp.StoryEnd - sp.StoryStart == sp.VisibleLength)
                        ? sp.StoryStart + deltaFromStart
                        : sp.StoryEnd - (spVisEnd - issueEnd);
                    break;
                }
            }

            if (resolvedStoryStart >= containerStart && resolvedStoryEnd > resolvedStoryStart && resolvedStoryEnd <= containerEnd)
            {
                Word.Range candidate = containerRange.Duplicate;
                candidate.SetRange(resolvedStoryStart, resolvedStoryEnd);
                string candText = CleanWordControlChars(candidate.Text ?? string.Empty);
                string strippedExpected = StripInvisibleFormatChars(cleanExpected);
                if (string.Equals(candText, cleanExpected, StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(strippedExpected) &&
                     string.Equals(StripInvisibleFormatChars(candText), strippedExpected, StringComparison.Ordinal)))
                {
                    return candidate;
                }

                // Micro-window probe (±4 story offsets) in case an adjacent field-end marker (U+0015) shifted by 1 position
                for (int shift = -4; shift <= 4; shift++)
                {
                    int pStart = resolvedStoryStart + shift;
                    int pEnd = pStart + issueLength;
                    if (pStart < containerStart || pEnd > containerEnd)
                        continue;

                    Word.Range probe = containerRange.Duplicate;
                    probe.SetRange(pStart, pEnd);
                    string probeCleaned = CleanWordControlChars(probe.Text ?? string.Empty);
                    if (string.Equals(probeCleaned, cleanExpected, StringComparison.Ordinal) ||
                        (!string.IsNullOrEmpty(strippedExpected) &&
                         string.Equals(StripInvisibleFormatChars(probeCleaned), strippedExpected, StringComparison.Ordinal)))
                    {
                        return probe;
                    }
                }
            }

            return null;
        }

        private static string StripInvisibleFormatChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF' || c == '\u2060' || c == '\u00AD')
                    continue;
                sb.Append((c == '\u00A0' || c == '\u202F' || c == '\u2007') ? ' ' : c);
            }
            return sb.ToString();
        }

        private static string CleanWordControlChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '\a' || chars[i] == '\r' || chars[i] == '\v' || chars[i] == '\f')
                {
                    chars[i] = ' ';
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Verifies whether at least one tracked issue range inside <c>[startOffset, endOffset]</c>
        /// still carries a live <see cref="Word.WdUnderline.wdUnderlineWavy"/> on screen.
        /// </summary>
        public bool HasLiveWavyUnderlineInSpan(Word.Document doc, int startOffset, int endOffset)
        {
            const int DriftWindow = 640;
            for (int i = 0; i < _trackedRanges.Count; i++)
            {
                var tracked = _trackedRanges[i];
                if (!ReferenceEquals(tracked.Document, doc))
                    continue;
                if (tracked.CachedEnd < startOffset - DriftWindow || tracked.CachedStart > endOffset + DriftWindow)
                    continue;

                try
                {
                    int rStart = tracked.Range.Start;
                    int rEnd = tracked.Range.End;
                    tracked.CachedStart = rStart;
                    tracked.CachedEnd = rEnd;
                    if (rStart >= startOffset && rStart < endOffset && rEnd <= endOffset &&
                        tracked.Range.Font.Underline == Word.WdUnderline.wdUnderlineWavy)
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private bool HasTrackedInSpan(Word.Document doc, int startOffset, int endOffset)
        {
            const int DriftWindow = 640;
            for (int i = 0; i < _trackedRanges.Count; i++)
            {
                var tracked = _trackedRanges[i];
                if (!ReferenceEquals(tracked.Document, doc))
                    continue;
                if (tracked.CachedEnd < startOffset - DriftWindow || tracked.CachedStart > endOffset + DriftWindow)
                    continue;

                try
                {
                    int rStart = tracked.Range.Start;
                    int rEnd = tracked.Range.End;
                    tracked.CachedStart = rStart;
                    tracked.CachedEnd = rEnd;
                    if (rStart >= startOffset && rStart < endOffset && rEnd <= endOffset)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private void RestoreAndRemoveTrackedInSpan(Word.Document doc, int startOffset, int endOffset)
        {
            const int DriftWindow = 640;
            for (int i = _trackedRanges.Count - 1; i >= 0; i--)
            {
                var tracked = _trackedRanges[i];
                if (!ReferenceEquals(tracked.Document, doc))
                    continue;
                if (tracked.CachedEnd < startOffset - DriftWindow || tracked.CachedStart > endOffset + DriftWindow)
                    continue;

                try
                {
                    int rStart = tracked.Range.Start;
                    int rEnd = tracked.Range.End;
                    tracked.CachedStart = rStart;
                    tracked.CachedEnd = rEnd;
                    if (rStart >= startOffset && rStart < endOffset && rEnd <= endOffset)
                    {
                        if (tracked.Range.Font.Underline == Word.WdUnderline.wdUnderlineWavy)
                        {
                            tracked.Range.Font.Underline = tracked.OriginalUnderline;
                            tracked.Range.Font.UnderlineColor = tracked.OriginalUnderlineColor;
                        }
                        _trackedRanges.RemoveAt(i);
                    }
                }
                catch
                {
                    _trackedRanges.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Clears any stray or inherited wdUnderlineWavy inside containerRange (e.g. from pasting over wavy text
        /// or Word inheriting wavy underline onto numbers/spaces/punctuation) using Word's native C++ formatted Find
        /// instead of O(Words x Characters) COM iteration, while 100% preserving wdUnderlineSingle and hyperlinks.
        /// </summary>
        private static void ScrubResidualWavyUnderlines(Word.Range containerRange)
        {
            try
            {
                Word.WdUnderline u = containerRange.Font.Underline;
                if (u == Word.WdUnderline.wdUnderlineNone || u == Word.WdUnderline.wdUnderlineSingle || u == Word.WdUnderline.wdUnderlineDouble)
                    return;

                if (u == Word.WdUnderline.wdUnderlineWavy)
                {
                    containerRange.Font.Underline = Word.WdUnderline.wdUnderlineNone;
                    containerRange.Font.UnderlineColor = Word.WdColor.wdColorAutomatic;
                    return;
                }

                // wdUndefined (9999999) means mixed underline styles exist inside containerRange (e.g. hyperlinks + plain text, or wavy + plain text).
                // Jump directly to wdUnderlineWavy runs via Word's native C++ Find engine (1 COM call when clean, zero character loops!).
                if ((int)u == 9999999)
                {
                    int origStart = containerRange.Start;
                    int origEnd = containerRange.End;
                    if (origEnd <= origStart)
                        return;

                    Word.Range findScope = containerRange.Duplicate;
                    findScope.SetRange(origStart, origEnd);
                    Word.Find find = findScope.Find;
                    find.ClearFormatting();
                    find.Font.Underline = Word.WdUnderline.wdUnderlineWavy;
                    find.Text = string.Empty;
                    find.Forward = true;
                    find.Wrap = Word.WdFindWrap.wdFindStop;
                    find.Format = true;
                    find.MatchCase = false;
                    find.MatchWholeWord = false;
                    find.MatchWildcards = false;
                    find.MatchSoundsLike = false;
                    find.MatchAllWordForms = false;

                    int guard = 0;
                    while (guard++ < 1024 && find.Execute())
                    {
                        int hitStart = findScope.Start;
                        int hitEnd = findScope.End;
                        if (hitStart < origStart || hitEnd > origEnd)
                            break;

                        findScope.Font.Underline = Word.WdUnderline.wdUnderlineNone;
                        findScope.Font.UnderlineColor = Word.WdColor.wdColorAutomatic;

                        int nextPos = Math.Max(hitStart + 1, hitEnd);
                        if (nextPos >= origEnd)
                            break;
                        findScope.SetRange(nextPos, origEnd);
                    }
                }
            }
            catch
            {
                // Ignore locked/protected range errors
            }
        }

        private static bool ContainsMyanmarCharacters(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '\u1000' && c <= '\u109F')
                    return true;
            }
            return false;
        }

        private bool TryBeginOwnUndoRecord(string label, out Word.UndoRecord? undoRecord)
        {
            undoRecord = null;
            try
            {
                undoRecord = _app.UndoRecord;
                if (undoRecord != null && !undoRecord.IsRecordingCustomRecord)
                {
                    undoRecord.StartCustomRecord(label);
                    return true;
                }
            }
            catch
            {
                // Ignore if Word is inside an internal modal or nested undo operation
            }
            return false;
        }

        private static void EndOwnUndoRecord(Word.UndoRecord? undoRecord, bool startedOwnUndo)
        {
            if (!startedOwnUndo || undoRecord == null)
                return;

            try
            {
                if (undoRecord.IsRecordingCustomRecord)
                {
                    undoRecord.EndCustomRecord();
                }
            }
            catch
            {
                // Ignore
            }
        }

        private static void EndOwnUndoAndPreserveSaved(Word.UndoRecord? undoRecord, bool startedOwnUndo, Word.Document doc, bool wasSaved)
        {
            try
            {
                if (startedOwnUndo && undoRecord != null && undoRecord.IsRecordingCustomRecord)
                {
                    undoRecord.EndCustomRecord();
                }
                if (wasSaved)
                {
                    doc.Saved = true;
                }
            }
            catch
            {
                // Ignore
            }
        }

        public void Dispose()
        {
            try
            {
                _postSaveRestoreTimer.Stop();
                _pendingRestoreDoc = null;

                // Strip all temporary proofing wavy underlines across any still-open documents
                // if the add-in is disabled/disconnected while Word remains open.
                var distinctDocs = new List<Word.Document>();
                for (int i = 0; i < _trackedRanges.Count; i++)
                {
                    Word.Document? doc = _trackedRanges[i].Document;
                    if (doc != null && !distinctDocs.Any(d => ReferenceEquals(d, doc)))
                    {
                        distinctDocs.Add(doc);
                    }
                }

                for (int i = 0; i < distinctDocs.Count; i++)
                {
                    try
                    {
                        StripAllProofingUnderlines(distinctDocs[i], clearTrackingList: true);
                    }
                    catch { }
                }

                _trackedRanges.Clear();
            }
            catch
            {
                // Ignore disconnect COM teardown exceptions
            }
            finally
            {
                _postSaveRestoreTimer.Dispose();
            }
        }
    }
}
