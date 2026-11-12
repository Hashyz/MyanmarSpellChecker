using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using Word = Microsoft.Office.Interop.Word;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace MyanmarSpellChecker.WordAddIn.Interop
{
    /// <summary>
    /// Coordinates STA-safe, non-blocking extraction of Word text across Main Body, Word Tables, and Headers/Footers (StoryRanges),
    /// runs multi-core NLP &amp; DocumentQualityAnalyzer asynchronously on background threads with generation cancellation,
    /// and binds results to live Word.Range objects in wall-clock-budgeted STA chunks so Word never freezes on 500+ page documents.
    /// </summary>
    public sealed class StaDocumentScanner : IDisposable
    {
        private const int ImmediateUnderlineParagraphThreshold = 16;
        private const int UnderlineParagraphsPerChunk = 24;
        private const int MaxSnapshotChunkWallMs = 10;
        private const int MaxUnderlineChunkWallMs = 10;
        private const int LargeDocTargetedPasteThresholdChars = 8_000;
        private const int MaxDirtyRangeParagraphs = 80;

        private static readonly Word.WdStoryType[] TargetStoryTypes =
        {
            Word.WdStoryType.wdMainTextStory,
            Word.WdStoryType.wdPrimaryHeaderStory,
            Word.WdStoryType.wdFirstPageHeaderStory,
            Word.WdStoryType.wdPrimaryFooterStory
        };

        private sealed class ParagraphSnapshot
        {
            public Word.WdStoryType StoryType { get; set; } = Word.WdStoryType.wdMainTextStory;
            public string StoryName { get; set; } = string.Empty;
            public int ParagraphIndex { get; set; }
            public Word.Range? ParagraphRange { get; set; }
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
            public string RawText { get; set; } = string.Empty;
            public bool NeedsNlpCheck { get; set; } = true;
        }

        private sealed class PendingSnapshotCollectionJob
        {
            public int Generation { get; set; }
            public Word.Document Document { get; set; } = null!;
            public int CaretFocusOffset { get; set; }
            public List<ParagraphSnapshot> Snapshots { get; } = new List<ParagraphSnapshot>(256);
            public int StoryTypeIndex { get; set; }
            public Word.Range? CurrentStoryRange { get; set; }
            public Word.Paragraphs? CurrentParagraphs { get; set; }
            public int CurrentParagraphCount { get; set; }
            public int NextParagraphOneBased { get; set; } = 1;
        }

        private sealed class PendingFullUnderlineJob
        {
            public int Generation { get; set; }
            public Word.Document Document { get; set; } = null!;
            public List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)> Analyzed { get; set; } = null!;
            public int[] ProcessingOrder { get; set; } = Array.Empty<int>();
            public List<DocumentProofingItem>?[] BoundByParagraph { get; set; } = Array.Empty<List<DocumentProofingItem>?>();
            public DocumentQualityReport QualityReport { get; set; } = null!;
            public Dictionary<int, string> DoneMap { get; set; } = null!;
            public Dictionary<Word.WdStoryType, Word.Range> StoryBaseRanges { get; } = new Dictionary<Word.WdStoryType, Word.Range>();
            public bool DocHadZeroUnderlinesAtStart { get; set; }
            public int NextOrderCursor { get; set; }
        }

        private readonly Word.Application _app;
        private readonly MyanmarProofingEngine _engine;
        private readonly SafeUnderlineManager _underlineManager;
        private readonly Control _staMarshaller;
        private readonly WinFormsTimer _debounceTimer;
        private readonly WinFormsTimer _pasteDebounceTimer;
        private readonly WinFormsTimer _deferredFullScanTimer;
        private readonly WinFormsTimer _liveTypingWatchTimer;
        private readonly WinFormsTimer _snapshotChunkTimer;
        private readonly WinFormsTimer _underlineChunkTimer;

        private int _scanGeneration;
        private PendingSnapshotCollectionJob? _activeSnapshotJob;
        private PendingFullUnderlineJob? _activeUnderlineJob;

        private Word.Document? _watchedDoc;
        private int _watchedDocLength = -1;
        private int _watchedActiveParaStart = -1;
        private int _lastScannedDocLength = -1;
        private int _lastObservedSelectionStart;
        private int _lastObservedSelectionEnd;
        private int _pendingDirtyMinOffset = -1;
        private int _pendingDirtyMaxOffset = -1;
        private string _watchedActiveParaText = string.Empty;
        private bool _hasPendingTypingChanges;
        private bool _hasPendingMultiParagraphPaste;
        private bool _hadMultiCharSelection;

        private readonly Dictionary<Word.Document, List<DocumentProofingItem>> _documentIssuesCache =
            new Dictionary<Word.Document, List<DocumentProofingItem>>();
        private readonly Dictionary<Word.Document, Dictionary<int, string>> _lastParagraphTextByStart =
            new Dictionary<Word.Document, Dictionary<int, string>>();
        private readonly Dictionary<Word.Document, DocumentQualityReport> _documentQualityCache =
            new Dictionary<Word.Document, DocumentQualityReport>();

        public bool IsLiveAutoScanEnabled { get; set; } = true;
        public bool HasPendingTypingChanges => _hasPendingTypingChanges;

        public event Action<Word.Document>? ScanStarted;
        public event Action<Word.Document, IReadOnlyList<DocumentProofingItem>>? ScanCompleted;
        public event Action? UserTypingDetected;

        public StaDocumentScanner(
            Word.Application app,
            MyanmarProofingEngine engine,
            SafeUnderlineManager underlineManager)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _underlineManager = underlineManager ?? throw new ArgumentNullException(nameof(underlineManager));

            _staMarshaller = new Control();
            IntPtr _ = _staMarshaller.Handle;

            _debounceTimer = new WinFormsTimer { Interval = 950 };
            _debounceTimer.Tick += OnDebounceTimerTick;

            // Fast paste / bulk-change debounce (~220ms): underlines newly pasted paragraphs almost instantaneously.
            _pasteDebounceTimer = new WinFormsTimer { Interval = 220 };
            _pasteDebounceTimer.Tick += OnPasteDebounceTimerTick;

            // Deferred post-open timer so Word renders a newly opened document window before background scan begins.
            _deferredFullScanTimer = new WinFormsTimer { Interval = 320 };
            _deferredFullScanTimer.Tick += OnDeferredFullScanTimerTick;

            // Time-budgeted STA paragraph snapshot collector (<= 10ms per 14ms tick) for huge table/hyperlinked documents.
            _snapshotChunkTimer = new WinFormsTimer { Interval = 14 };
            _snapshotChunkTimer.Tick += OnSnapshotChunkTimerTick;

            // Wall-clock-budgeted STA underline pump (<= 10ms per 22ms UI slice) so Word stays 60 FPS responsive.
            _underlineChunkTimer = new WinFormsTimer { Interval = 22 };
            _underlineChunkTimer.Tick += OnUnderlineChunkTimerTick;

            // Live keystroke idle watcher: observes O(1) document end + active paragraph text without O(Doc) Paragraphs.Count.
            _liveTypingWatchTimer = new WinFormsTimer { Interval = 950 };
            _liveTypingWatchTimer.Tick += OnLiveTypingWatchTick;

            TryInitializeWatchedDocumentBaseline();
            _liveTypingWatchTimer.Start();
        }

        public IReadOnlyList<DocumentProofingItem> GetCachedIssues(Word.Document? doc)
        {
            if (doc != null && _documentIssuesCache.TryGetValue(doc, out var list))
            {
                return list.Where(i => !i.IsResolved).ToList();
            }
            return Array.Empty<DocumentProofingItem>();
        }

        /// <summary>
        /// Zero-allocation access to the underlying issue cache for high-frequency cursor hit-testing.
        /// Callers must skip items with <c>IsResolved == true</c>.
        /// </summary>
        public IReadOnlyList<DocumentProofingItem> GetCachedIssuesRaw(Word.Document? doc)
        {
            if (doc != null && _documentIssuesCache.TryGetValue(doc, out var list))
            {
                return list;
            }
            return Array.Empty<DocumentProofingItem>();
        }

        public DocumentQualityReport? GetCachedQualityReport(Word.Document? doc)
        {
            if (doc != null && _documentQualityCache.TryGetValue(doc, out var report))
            {
                return report;
            }
            return null;
        }

        public void ClearCachedIssues(Word.Document? doc)
        {
            Interlocked.Increment(ref _scanGeneration);
            CancelActiveJobs();
            if (doc != null)
            {
                _documentIssuesCache.Remove(doc);
                _lastParagraphTextByStart.Remove(doc);
                _documentQualityCache.Remove(doc);
                ScanCompleted?.Invoke(doc, Array.Empty<DocumentProofingItem>());
            }
        }

        public void InvalidateParagraphCache(Word.Document? doc)
        {
            if (doc != null)
            {
                _lastParagraphTextByStart.Remove(doc);
                _documentQualityCache.Remove(doc);
            }
        }

        public void PurgeDocument(Word.Document? doc)
        {
            if (doc == null)
                return;

            if ((_activeSnapshotJob != null && ReferenceEquals(_activeSnapshotJob.Document, doc)) ||
                (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc)))
            {
                Interlocked.Increment(ref _scanGeneration);
                CancelActiveJobs();
            }

            if (ReferenceEquals(_watchedDoc, doc))
            {
                _watchedDoc = null;
                _watchedDocLength = -1;
                _watchedActiveParaStart = -1;
                _lastObservedSelectionStart = 0;
                _lastObservedSelectionEnd = 0;
                _pendingDirtyMinOffset = -1;
                _pendingDirtyMaxOffset = -1;
                _watchedActiveParaText = string.Empty;
                _hasPendingTypingChanges = false;
                _hasPendingMultiParagraphPaste = false;
                _hadMultiCharSelection = false;
            }

            _documentIssuesCache.Remove(doc);
            _lastParagraphTextByStart.Remove(doc);
            _documentQualityCache.Remove(doc);
        }

        private void CancelActiveJobs()
        {
            _snapshotChunkTimer.Stop();
            _activeSnapshotJob = null;
            _underlineChunkTimer.Stop();
            _activeUnderlineJob = null;
        }

        public void ScheduleDeferredFullDocumentScan(int delayMs = 320)
        {
            _deferredFullScanTimer.Stop();
            _deferredFullScanTimer.Interval = Math.Max(60, delayMs);
            _deferredFullScanTimer.Start();
        }

        private void OnDeferredFullScanTimerTick(object? sender, EventArgs e)
        {
            _deferredFullScanTimer.Stop();
            ScanFullDocumentAsync();
        }

        private void RecordDirtySpan(int startOffset, int endOffset)
        {
            int lo = Math.Max(0, Math.Min(startOffset, endOffset));
            int hi = Math.Max(lo, Math.Max(startOffset, endOffset));
            if (_pendingDirtyMinOffset < 0 || lo < _pendingDirtyMinOffset)
                _pendingDirtyMinOffset = lo;
            if (_pendingDirtyMaxOffset < 0 || hi > _pendingDirtyMaxOffset)
                _pendingDirtyMaxOffset = hi;
        }

        private void ResetDirtySpan()
        {
            _pendingDirtyMinOffset = -1;
            _pendingDirtyMaxOffset = -1;
        }

        private void TryInitializeWatchedDocumentBaseline()
        {
            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (doc == null)
                    return;

                _watchedDoc = doc;
                _watchedDocLength = doc.Content.End;
                _lastScannedDocLength = _watchedDocLength;

                Word.Selection? sel = _app.Selection;
                if (sel?.Range != null)
                {
                    _lastObservedSelectionStart = sel.Range.Start;
                    _lastObservedSelectionEnd = sel.Range.End;
                    _hadMultiCharSelection = (_lastObservedSelectionEnd - _lastObservedSelectionStart) >= 2;
                    Word.Range probe = sel.Range.Duplicate;
                    probe.SetRange(_lastObservedSelectionStart, _lastObservedSelectionStart);
                    if (probe.Paragraphs.Count > 0)
                    {
                        Word.Paragraph p1 = probe.Paragraphs[1];
                        _watchedActiveParaStart = p1.Range.Start;
                        _watchedActiveParaText = BuildActiveParagraphFingerprint(p1);
                    }
                }
            }
            catch { }
        }

        private static string BuildActiveParagraphFingerprint(Word.Paragraph activePara)
        {
            string cur = activePara.Range.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cur))
            {
                try
                {
                    Word.Paragraph? prev = activePara.Previous();
                    if (prev != null)
                    {
                        return (prev.Range.Text ?? string.Empty) + "\n" + cur;
                    }
                }
                catch { }
            }
            return cur;
        }

        /// <summary>
        /// O(1) selection-activity hook invoked from <c>WindowSelectionChange</c>.
        /// Detects clipboard pastes (<c>Ctrl+V</c>, Right-Click Paste, Ribbon Paste, and <c>Ctrl+A</c> overwrite pastes)
        /// immediately and tracks the exact dirty character span so pasting into a 300-page book scans only the pasted region.
        /// </summary>
        public void NotifySelectionActivity(Word.Selection? sel)
        {
            if (!IsLiveAutoScanEnabled || sel == null)
                return;

            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (doc == null)
                    return;

                int selStart = sel.Start;
                int selEnd = sel.End;
                bool isMultiCharSel = (selEnd - selStart) >= 2;
                int docLen = doc.Content.End;

                if (!ReferenceEquals(doc, _watchedDoc))
                {
                    _watchedDoc = doc;
                    _watchedDocLength = docLen;
                    _lastScannedDocLength = docLen;
                    _watchedActiveParaStart = -1;
                    _lastObservedSelectionStart = selStart;
                    _lastObservedSelectionEnd = selEnd;
                    ResetDirtySpan();
                    _watchedActiveParaText = string.Empty;
                    _hadMultiCharSelection = isMultiCharSel;
                    if (docLen > 1 && !_documentIssuesCache.ContainsKey(doc))
                    {
                        _pasteDebounceTimer.Stop();
                        _pasteDebounceTimer.Start();
                    }
                    return;
                }

                int prevSelStart = _lastObservedSelectionStart;
                int prevSelEnd = _lastObservedSelectionEnd;
                _lastObservedSelectionStart = selStart;
                _lastObservedSelectionEnd = selEnd;

                int lenDelta = Math.Abs(docLen - _watchedDocLength);
                bool replacedSelection = _hadMultiCharSelection && !isMultiCharSel;
                _hadMultiCharSelection = isMultiCharSel;

                // Paste / bulk-edit signature: document length jumped by >= 4 chars in a single event,
                // OR user had a multi-char selection (e.g. Ctrl+A) that just collapsed via paste/replace.
                if (lenDelta >= 4 || replacedSelection)
                {
                    if (replacedSelection && lenDelta == 0)
                    {
                        string curFingerprint = _watchedActiveParaText;
                        try
                        {
                            Word.Range? sRange = sel.Range;
                            if (sRange != null)
                            {
                                Word.Range probe = sRange.Duplicate;
                                probe.SetRange(selStart, selStart);
                                if (probe.Paragraphs.Count > 0)
                                {
                                    curFingerprint = BuildActiveParagraphFingerprint(probe.Paragraphs[1]);
                                }
                            }
                        }
                        catch { }

                        if (string.Equals(curFingerprint, _watchedActiveParaText, StringComparison.Ordinal))
                            return;

                        _watchedActiveParaText = curFingerprint;
                    }

                    if ((_activeSnapshotJob != null && ReferenceEquals(_activeSnapshotJob.Document, doc)) ||
                        (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc)))
                    {
                        Interlocked.Increment(ref _scanGeneration);
                        CancelActiveJobs();
                    }

                    int pad = Math.Max(320, lenDelta + 160);
                    int dirtyLo = Math.Max(0, Math.Min(prevSelStart, selStart) - pad);
                    int dirtyHi = Math.Min(docLen, Math.Max(prevSelEnd, selEnd) + pad);
                    RecordDirtySpan(dirtyLo, dirtyHi);

                    _watchedDocLength = docLen;
                    _lastScannedDocLength = docLen;
                    _hasPendingTypingChanges = false;
                    _hasPendingMultiParagraphPaste = false;
                    UserTypingDetected?.Invoke();

                    _debounceTimer.Stop();
                    _pasteDebounceTimer.Stop();
                    _pasteDebounceTimer.Start();
                }
            }
            catch
            {
                // Ignore transient COM busy states
            }
        }

        private void OnPasteDebounceTimerTick(object? sender, EventArgs e)
        {
            _pasteDebounceTimer.Stop();
            if (!IsLiveAutoScanEnabled)
                return;

            Word.Document? doc = null;
            int docLen = 0;
            try
            {
                doc = _app.ActiveDocument;
                if (doc != null)
                {
                    docLen = doc.Content.End;
                    _watchedDoc = doc;
                    _watchedDocLength = docLen;
                    _lastScannedDocLength = docLen;
                    _hasPendingTypingChanges = false;
                    _hasPendingMultiParagraphPaste = false;
                }
            }
            catch { }

            if (doc == null)
                return;

            int dirtyMin = _pendingDirtyMinOffset;
            int dirtyMax = _pendingDirtyMaxOffset;
            ResetDirtySpan();

            // On large documents that have already been scanned once, scan ONLY the edited/pasted range instead of all 300 pages!
            if (_documentIssuesCache.ContainsKey(doc) &&
                docLen >= LargeDocTargetedPasteThresholdChars &&
                dirtyMin >= 0 &&
                dirtyMax > dirtyMin &&
                (dirtyMax - dirtyMin) <= (int)(docLen * 0.55))
            {
                if (TryScanDirtyRangeAsync(doc, dirtyMin, Math.Min(docLen, dirtyMax)))
                    return;
            }

            ScanFullDocumentAsync();
        }

        public void ScheduleActiveParagraphScan()
        {
            if (!IsLiveAutoScanEnabled)
                return;

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (IsLiveAutoScanEnabled)
            {
                ScanActiveParagraphAsync();
            }
        }

        private void OnLiveTypingWatchTick(object? sender, EventArgs e)
        {
            if (!IsLiveAutoScanEnabled)
                return;

            try
            {
                Word.Document? doc = _app.ActiveDocument;
                Word.Selection? sel = _app.Selection;
                if (doc == null || sel == null || sel.Range == null)
                    return;

                // Strictly O(1) document length & collapsed caret paragraph lookup (never calls O(Doc) doc.Paragraphs.Count!)
                int docLen = doc.Content.End;
                int caretStart = sel.Range.Start;
                int caretEnd = sel.Range.End;
                int activeParaStart = -1;
                int activeParaEnd = -1;
                string activeParaText = string.Empty;

                try
                {
                    Word.Range probe = sel.Range.Duplicate;
                    probe.SetRange(caretStart, caretStart);
                    if (probe.Paragraphs.Count > 0)
                    {
                        Word.Paragraph p1 = probe.Paragraphs[1];
                        Word.Range pr = p1.Range;
                        activeParaStart = pr.Start;
                        activeParaEnd = pr.End;
                        activeParaText = BuildActiveParagraphFingerprint(p1);
                    }
                }
                catch { }

                if (!ReferenceEquals(doc, _watchedDoc))
                {
                    bool needsInitialScan = docLen > 1 && !_documentIssuesCache.ContainsKey(doc);
                    _watchedDoc = doc;
                    _watchedDocLength = docLen;
                    _watchedActiveParaStart = activeParaStart;
                    _lastObservedSelectionStart = caretStart;
                    _lastObservedSelectionEnd = caretEnd;
                    ResetDirtySpan();
                    _lastScannedDocLength = needsInitialScan ? -1 : docLen;
                    _watchedActiveParaText = activeParaText;
                    _hasPendingTypingChanges = needsInitialScan;
                    _hasPendingMultiParagraphPaste = needsInitialScan;
                    if (needsInitialScan)
                    {
                        _pasteDebounceTimer.Stop();
                        _pasteDebounceTimer.Start();
                    }
                    return;
                }

                // Pure cursor movement (same docLen, different paragraph): update watched paragraph baseline without triggering a scan
                if (docLen == _watchedDocLength && activeParaStart != _watchedActiveParaStart)
                {
                    _watchedActiveParaStart = activeParaStart;
                    _watchedActiveParaText = activeParaText;
                    _lastObservedSelectionStart = caretStart;
                    _lastObservedSelectionEnd = caretEnd;
                }

                if (docLen != _watchedDocLength || !string.Equals(activeParaText, _watchedActiveParaText, StringComparison.Ordinal))
                {
                    int lenDelta = Math.Abs(docLen - _watchedDocLength);
                    if (lenDelta >= 10 ||
                        (_lastScannedDocLength >= 0 && Math.Abs(docLen - _lastScannedDocLength) > 48) ||
                        (activeParaStart != _watchedActiveParaStart && lenDelta >= 4))
                    {
                        _hasPendingMultiParagraphPaste = true;
                        int lo = Math.Max(0, Math.Min(_lastObservedSelectionStart, activeParaStart >= 0 ? activeParaStart : caretStart) - Math.Max(320, lenDelta + 160));
                        int hi = Math.Min(docLen, Math.Max(_lastObservedSelectionEnd, activeParaEnd > 0 ? activeParaEnd : caretEnd) + Math.Max(320, lenDelta + 160));
                        RecordDirtySpan(lo, hi);
                    }

                    // If a background chunked full-doc snapshot/underline job was running, cancel it immediately so typing has zero contention
                    if ((_activeSnapshotJob != null && ReferenceEquals(_activeSnapshotJob.Document, doc)) ||
                        (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc)))
                    {
                        Interlocked.Increment(ref _scanGeneration);
                        CancelActiveJobs();
                    }

                    _watchedDocLength = docLen;
                    _watchedActiveParaStart = activeParaStart;
                    _watchedActiveParaText = activeParaText;
                    _lastObservedSelectionStart = caretStart;
                    _lastObservedSelectionEnd = caretEnd;
                    UserTypingDetected?.Invoke();

                    // If this was a multi-word/paragraph paste that didn't fire SelectionChange, underline rapidly via 220ms paste timer!
                    if (lenDelta >= 10)
                    {
                        _hasPendingTypingChanges = false;
                        _hasPendingMultiParagraphPaste = false;
                        _lastScannedDocLength = docLen;
                        _pasteDebounceTimer.Stop();
                        _pasteDebounceTimer.Start();
                        return;
                    }

                    // User is actively typing single keystrokes; wait until keystrokes pause for 1 tick
                    _hasPendingTypingChanges = true;
                    return;
                }

                if (_hasPendingTypingChanges)
                {
                    bool multiParaEdit = _hasPendingMultiParagraphPaste;
                    _hasPendingTypingChanges = false;
                    _hasPendingMultiParagraphPaste = false;
                    _lastScannedDocLength = docLen;
                    if (multiParaEdit)
                    {
                        int dirtyMin = _pendingDirtyMinOffset;
                        int dirtyMax = _pendingDirtyMaxOffset;
                        ResetDirtySpan();

                        if (_documentIssuesCache.ContainsKey(doc) &&
                            docLen >= LargeDocTargetedPasteThresholdChars &&
                            dirtyMin >= 0 &&
                            dirtyMax > dirtyMin &&
                            (dirtyMax - dirtyMin) <= (int)(docLen * 0.55) &&
                            TryScanDirtyRangeAsync(doc, dirtyMin, Math.Min(docLen, dirtyMax)))
                        {
                            return;
                        }

                        ScanFullDocumentAsync();
                    }
                    else
                    {
                        ResetDirtySpan();
                        ScanActiveParagraphAsync();
                    }
                }
            }
            catch
            {
                // Ignore transient COM busy errors during modal dialogs or IME composition
            }
        }

        private static bool IsIncompleteTypingCluster(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            string t = text!.Trim();
            if (t.Length <= 1)
                return true;

            bool hasBase = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if ((c >= '\u1000' && c <= '\u102A') || c == '\u103F' || (c >= '\u104C' && c <= '\u104F'))
                {
                    hasBase = true;
                    break;
                }
            }
            if (!hasBase)
                return true;

            char last = t[t.Length - 1];
            return last == '\u1039' || last == '\u1031' || (last >= '\u103B' && last <= '\u103E');
        }

        /// <summary>
        /// Scans ONLY the paragraphs overlapping <c>[dirtyStart, dirtyEnd]</c> when pasting or deleting inside a large document,
        /// avoiding expensive full-book re-scans. Returns <c>false</c> if the dirty window exceeds <see cref="MaxDirtyRangeParagraphs"/>.
        /// </summary>
        private bool TryScanDirtyRangeAsync(Word.Document doc, int dirtyStart, int dirtyEnd)
        {
            try
            {
                int docEnd = doc.Content.End;
                int clampedStart = Math.Max(0, Math.Min(dirtyStart, docEnd));
                int clampedEnd = Math.Max(clampedStart, Math.Min(dirtyEnd, docEnd));
                if (clampedEnd <= clampedStart)
                    return false;

                Word.Range dirtyScope = doc.Range(clampedStart, clampedEnd);
                Word.Paragraphs paras = dirtyScope.Paragraphs;
                int pCount = paras.Count;
                if (pCount <= 0 || pCount > MaxDirtyRangeParagraphs)
                    return false;

                var pendingSnapshots = new List<ParagraphSnapshot>(pCount);
                int actualWindowStart = int.MaxValue;
                int actualWindowEnd = -1;

                for (int i = 1; i <= pCount; i++)
                {
                    Word.Range paraRange = paras[i].Range;
                    int s = paraRange.Start;
                    int e = paraRange.End;
                    if (s < actualWindowStart) actualWindowStart = s;
                    if (e > actualWindowEnd) actualWindowEnd = e;

                    string rawBeforeClean = paraRange.Text ?? string.Empty;
                    string rawText = CleanWordControlChars(rawBeforeClean);
                    if (string.IsNullOrWhiteSpace(rawText))
                        continue;

                    bool inTable = rawBeforeClean.IndexOf('\a') >= 0;
                    Word.WdStoryType st = Word.WdStoryType.wdMainTextStory;
                    try { st = paraRange.StoryType; } catch { }

                    pendingSnapshots.Add(new ParagraphSnapshot
                    {
                        StoryType = st,
                        StoryName = GetFriendlyStoryName(st, inTable),
                        ParagraphIndex = i,
                        ParagraphRange = paraRange,
                        StartOffset = s,
                        EndOffset = e,
                        RawText = rawText,
                        NeedsNlpCheck = ContainsMyanmarOrZawgyiCandidate(rawText)
                    });
                }

                if (actualWindowEnd <= actualWindowStart)
                    return true;

                int generation = Volatile.Read(ref _scanGeneration);
                Task.Run(() =>
                {
                    var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)>(pendingSnapshots.Count);
                    for (int i = 0; i < pendingSnapshots.Count; i++)
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        var snap = pendingSnapshots[i];
                        List<ProofingIssue> issues = snap.NeedsNlpCheck
                            ? _engine.CheckText(snap.RawText)
                            : new List<ProofingIssue>(0);
                        analyzed.Add((snap, issues));
                    }

                    if (_staMarshaller.IsHandleCreated && !_staMarshaller.IsDisposed)
                    {
                        _staMarshaller.BeginInvoke(new Action(() =>
                        {
                            if (generation != Volatile.Read(ref _scanGeneration))
                                return;

                            try
                            {
                                if (!_documentIssuesCache.TryGetValue(doc, out var docList))
                                {
                                    docList = new List<DocumentProofingItem>();
                                    _documentIssuesCache[doc] = docList;
                                }
                                if (!_lastParagraphTextByStart.TryGetValue(doc, out var doneMap))
                                {
                                    doneMap = new Dictionary<int, string>();
                                    _lastParagraphTextByStart[doc] = doneMap;
                                }

                                _underlineManager.ExecuteInSharedProofingBatch(doc, () =>
                                {
                                    docList.RemoveAll(existing =>
                                    {
                                        if (existing.IsResolved)
                                            return true;
                                        if (existing.CachedEnd < actualWindowStart - 640 || existing.CachedStart > actualWindowEnd + 640)
                                            return false;

                                        try
                                        {
                                            int s = existing.TargetRange != null ? existing.TargetRange.Start : existing.FallbackStartOffset;
                                            if (existing.TargetRange != null)
                                            {
                                                existing.CachedStart = s;
                                                existing.CachedEnd = existing.TargetRange.End;
                                            }
                                            return s >= actualWindowStart && s < actualWindowEnd;
                                        }
                                        catch
                                        {
                                            return true;
                                        }
                                    });

                                    foreach (var (snap, issues) in analyzed)
                                    {
                                        if (snap.ParagraphRange == null)
                                            continue;

                                        var bound = _underlineManager.BindAndUnderlineRangeIssues(
                                            doc,
                                            snap.ParagraphRange,
                                            snap.StoryName,
                                            snap.ParagraphIndex,
                                            issues,
                                            insideSharedBatch: true,
                                            applyVisualUnderline: true,
                                            preloadedCleanText: snap.RawText,
                                            storyType: snap.StoryType);

                                        doneMap[snap.StartOffset] = snap.RawText;
                                        if (bound.Count > 0)
                                            docList.AddRange(bound);
                                    }
                                });

                                docList.Sort((a, b) => a.CachedStart.CompareTo(b.CachedStart));
                                _documentQualityCache.Remove(doc);
                                ScanCompleted?.Invoke(doc, docList.Where(i => !i.IsResolved).ToList());
                            }
                            catch { }
                        }));
                    }
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Scans the currently edited paragraph (and immediate preceding paragraph across &lt;Enter&gt; newline transitions)
        /// across Main Body, Table cells, or Headers/Footers, merging updated live Word.Range issues into the document cache.
        /// </summary>
        public void ScanActiveParagraphAsync()
        {
            try
            {
                Word.Document? doc = _app.ActiveDocument;
                Word.Selection? sel = _app.Selection;
                if (doc == null || sel == null || sel.Range == null)
                    return;

                int caretStart = sel.Range.Start;
                bool isCollapsedCaret = caretStart == sel.Range.End;
                Word.Range caretProbe = sel.Range.Duplicate;
                caretProbe.SetRange(caretStart, caretStart);
                Word.Paragraphs paras = caretProbe.Paragraphs;
                if (paras.Count == 0)
                    return;

                Word.Paragraph activePara = paras[1];
                var candidateParas = new List<Word.Paragraph>(2);
                try
                {
                    Word.Paragraph? prev = activePara.Previous();
                    if (prev != null)
                    {
                        candidateParas.Add(prev);
                    }
                }
                catch { }
                candidateParas.Add(activePara);

                var pendingSnapshots = new List<ParagraphSnapshot>(2);
                if (!_lastParagraphTextByStart.TryGetValue(doc, out var updateMap))
                {
                    updateMap = new Dictionary<int, string>();
                    _lastParagraphTextByStart[doc] = updateMap;
                }

                foreach (var para in candidateParas)
                {
                    Word.Range paraRange = para.Range;
                    int startOffset = paraRange.Start;
                    int endOffset = paraRange.End;
                    string rawBeforeClean = paraRange.Text ?? string.Empty;
                    string rawText = CleanWordControlChars(rawBeforeClean);

                    if (string.IsNullOrWhiteSpace(rawText))
                        continue;

                    if (updateMap.TryGetValue(startOffset, out string? prevText) &&
                        string.Equals(prevText, rawText, StringComparison.Ordinal))
                    {
                        bool expectsUnderlines = ContainsMyanmarOrZawgyiCandidate(rawText) && _engine.CheckText(rawText).Count > 0;
                        if (!expectsUnderlines || _underlineManager.HasLiveWavyUnderlineInSpan(doc, startOffset, endOffset))
                        {
                            continue;
                        }
                    }

                    // O(1) pure C# table cell detection (\a cell marker) — avoids layout-forcing wdWithInTable COM call!
                    bool inTable = rawBeforeClean.IndexOf('\a') >= 0;
                    Word.WdStoryType st = Word.WdStoryType.wdMainTextStory;
                    try { st = paraRange.StoryType; } catch { }

                    pendingSnapshots.Add(new ParagraphSnapshot
                    {
                        StoryType = st,
                        StoryName = GetFriendlyStoryName(st, inTable),
                        ParagraphIndex = 1,
                        ParagraphRange = paraRange,
                        StartOffset = startOffset,
                        EndOffset = endOffset,
                        RawText = rawText,
                        NeedsNlpCheck = ContainsMyanmarOrZawgyiCandidate(rawText)
                    });
                }

                if (pendingSnapshots.Count == 0)
                    return;

                int generation = Volatile.Read(ref _scanGeneration);

                Task.Run(() =>
                {
                    var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues, bool DeferredIncomplete)>(pendingSnapshots.Count);
                    foreach (var snap in pendingSnapshots)
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        List<ProofingIssue> issues = snap.NeedsNlpCheck
                            ? _engine.CheckText(snap.RawText)
                            : new List<ProofingIssue>();

                        bool deferredIncomplete = false;
                        if (isCollapsedCaret && issues.Count > 0 && caretStart >= snap.StartOffset && caretStart <= snap.EndOffset)
                        {
                            int caretRel = Math.Max(0, Math.Min(snap.RawText.Length, caretStart - snap.StartOffset));
                            int removed = issues.RemoveAll(iss =>
                                caretRel >= iss.StartIndex &&
                                caretRel <= iss.StartIndex + iss.Length &&
                                IsIncompleteTypingCluster(iss.OriginalText));
                            if (removed > 0)
                            {
                                deferredIncomplete = true;
                            }
                        }

                        analyzed.Add((snap, issues, deferredIncomplete));
                    }

                    if (_staMarshaller.IsHandleCreated && !_staMarshaller.IsDisposed)
                    {
                        _staMarshaller.BeginInvoke(new Action(() =>
                        {
                            if (generation != Volatile.Read(ref _scanGeneration))
                                return;

                            try
                            {
                                if (!_documentIssuesCache.TryGetValue(doc, out var docList))
                                {
                                    docList = new List<DocumentProofingItem>();
                                    _documentIssuesCache[doc] = docList;
                                }
                                if (!_lastParagraphTextByStart.TryGetValue(doc, out var doneMap))
                                {
                                    doneMap = new Dictionary<int, string>();
                                    _lastParagraphTextByStart[doc] = doneMap;
                                }

                                const int DriftWindow = 640;
                                bool needsComWrite = false;
                                foreach (var (snap, issues, _) in analyzed)
                                {
                                    if (issues.Count > 0)
                                    {
                                        needsComWrite = true;
                                        break;
                                    }

                                    bool hasCachedNear = docList.Any(existing =>
                                        !existing.IsResolved &&
                                        existing.CachedEnd >= snap.StartOffset - DriftWindow &&
                                        existing.CachedStart <= snap.EndOffset + DriftWindow);
                                    if (hasCachedNear || _underlineManager.HasLiveWavyUnderlineInSpan(doc, snap.StartOffset, snap.EndOffset))
                                    {
                                        needsComWrite = true;
                                        break;
                                    }

                                    if (snap.ParagraphRange != null)
                                    {
                                        try
                                        {
                                            Word.WdUnderline pu = snap.ParagraphRange.Font.Underline;
                                            if (pu == Word.WdUnderline.wdUnderlineWavy || (int)pu == 9999999)
                                            {
                                                needsComWrite = true;
                                                break;
                                            }
                                        }
                                        catch { }
                                    }
                                }

                                // Zero-COM-Write Fast Path: when typing clean text with no underlines to add or clear,
                                // never toggle Word.ScreenUpdating, never open an UndoRecord, and never rebuild WinForms cards!
                                if (!needsComWrite)
                                {
                                    foreach (var (snap, _, deferredIncomplete) in analyzed)
                                    {
                                        if (!deferredIncomplete)
                                            doneMap[snap.StartOffset] = snap.RawText;
                                        else
                                            doneMap.Remove(snap.StartOffset);
                                    }
                                    _documentQualityCache.Remove(doc);
                                    return;
                                }

                                _underlineManager.ExecuteInSharedProofingBatch(doc, () =>
                                {
                                    foreach (var (snap, issues, deferredIncomplete) in analyzed)
                                    {
                                        if (snap.ParagraphRange == null)
                                            continue;

                                        List<DocumentProofingItem> bound = _underlineManager.BindAndUnderlineRangeIssues(
                                            doc,
                                            snap.ParagraphRange,
                                            snap.StoryName,
                                            snap.ParagraphIndex,
                                            issues,
                                            insideSharedBatch: true,
                                            applyVisualUnderline: true,
                                            preloadedCleanText: snap.RawText,
                                            storyType: snap.StoryType);

                                        if (!deferredIncomplete)
                                            doneMap[snap.StartOffset] = snap.RawText;
                                        else
                                            doneMap.Remove(snap.StartOffset);

                                        docList.RemoveAll(existing =>
                                        {
                                            if (existing.IsResolved)
                                                return true;
                                            if (existing.CachedEnd < snap.StartOffset - DriftWindow ||
                                                existing.CachedStart > snap.EndOffset + DriftWindow)
                                            {
                                                return false;
                                            }
                                            try
                                            {
                                                int s = existing.TargetRange != null ? existing.TargetRange.Start : existing.FallbackStartOffset;
                                                if (existing.TargetRange != null)
                                                {
                                                    existing.CachedStart = s;
                                                    existing.CachedEnd = existing.TargetRange.End;
                                                }
                                                return s >= snap.StartOffset && s < snap.EndOffset;
                                            }
                                            catch
                                            {
                                                return true;
                                            }
                                        });

                                        docList.AddRange(bound);
                                    }
                                });

                                _documentQualityCache.Remove(doc);
                                ScanCompleted?.Invoke(doc, docList.Where(i => !i.IsResolved).ToList());
                            }
                            catch
                            {
                                // Ignore
                            }
                        }));
                    }
                });
            }
            catch
            {
                // Ignore transient COM errors
            }
        }

        /// <summary>
        /// Scans Main Body, Tables, and Headers/Footers (StoryRanges) across the entire active document
        /// using non-blocking whole-story / time-sliced STA extraction, multi-core background NLP,
        /// and wall-clock-budgeted chunked underlining so Word never freezes on 50–500+ page documents.
        /// </summary>
        public void ScanFullDocumentAsync()
        {
            try
            {
                _deferredFullScanTimer.Stop();
                Word.Document? doc = _app.ActiveDocument;
                if (doc == null)
                    return;

                int generation = Interlocked.Increment(ref _scanGeneration);
                CancelActiveJobs();
                ResetDirtySpan();
                ScanStarted?.Invoke(doc);

                int caretOffset = 0;
                try { caretOffset = _app.Selection?.Start ?? 0; } catch { }

                var snapshotJob = new PendingSnapshotCollectionJob
                {
                    Generation = generation,
                    Document = doc,
                    CaretFocusOffset = Math.Max(0, caretOffset),
                    StoryTypeIndex = 0
                };

                if (ProcessSnapshotCollectionSlice(snapshotJob, MaxSnapshotChunkWallMs + 2))
                {
                    LaunchBackgroundNlpAndUnderline(snapshotJob.Document, snapshotJob.Generation, snapshotJob.Snapshots, snapshotJob.CaretFocusOffset);
                }
                else
                {
                    _activeSnapshotJob = snapshotJob;
                    _snapshotChunkTimer.Start();
                }
            }
            catch
            {
                // Ignore
            }
        }

        private void OnSnapshotChunkTimerTick(object? sender, EventArgs e)
        {
            var job = _activeSnapshotJob;
            if (job == null || job.Generation != Volatile.Read(ref _scanGeneration))
            {
                _snapshotChunkTimer.Stop();
                _activeSnapshotJob = null;
                return;
            }

            try
            {
                if (ProcessSnapshotCollectionSlice(job, MaxSnapshotChunkWallMs))
                {
                    _snapshotChunkTimer.Stop();
                    _activeSnapshotJob = null;
                    LaunchBackgroundNlpAndUnderline(job.Document, job.Generation, job.Snapshots, job.CaretFocusOffset);
                }
            }
            catch
            {
                _snapshotChunkTimer.Stop();
                _activeSnapshotJob = null;
            }
        }

        /// <summary>
        /// Collects paragraph snapshots within a strict wall-clock millisecond slice (<paramref name="budgetMs"/>).
        /// Uses a 1-call whole-story read (<c>storyRange.Text</c>) whenever a story has 1:1 plain-text offsets
        /// (extracting 5,000+ paragraphs in &lt; 4ms with zero per-paragraph COM calls!), and falls back to
        /// time-sliced chunked paragraph indexing without ever calling layout-blocking <c>wdWithInTable</c>.
        /// Returns <c>true</c> when all stories are collected.
        /// </summary>
        private static bool ProcessSnapshotCollectionSlice(PendingSnapshotCollectionJob job, int budgetMs)
        {
            var sw = Stopwatch.StartNew();

            while (job.StoryTypeIndex < TargetStoryTypes.Length)
            {
                Word.WdStoryType storyType = TargetStoryTypes[job.StoryTypeIndex];

                if (job.CurrentStoryRange == null)
                {
                    try
                    {
                        job.CurrentStoryRange = job.Document.StoryRanges[storyType];
                    }
                    catch
                    {
                        job.StoryTypeIndex++;
                        continue;
                    }

                    if (job.CurrentStoryRange == null)
                    {
                        job.StoryTypeIndex++;
                        continue;
                    }
                    job.CurrentParagraphs = null;
                    job.NextParagraphOneBased = 1;
                }

                while (job.CurrentStoryRange != null)
                {
                    // Tier 1: Ultra-Fast Whole-Story Single-Read Path (zero per-paragraph COM calls!)
                    if (job.CurrentParagraphs == null)
                    {
                        if (TryCollectPlainStoryFast(job.CurrentStoryRange, storyType, job.Snapshots))
                        {
                            try { job.CurrentStoryRange = job.CurrentStoryRange.NextStoryRange; }
                            catch { job.CurrentStoryRange = null; }
                            job.CurrentParagraphs = null;
                            job.NextParagraphOneBased = 1;

                            if (sw.ElapsedMilliseconds >= budgetMs)
                                return false;
                            continue;
                        }

                        // Tier 2: Story contains Tables, Hyperlinks, Fields, Footnotes, or Shapes — prepare chunked indexing
                        try
                        {
                            job.CurrentParagraphs = job.CurrentStoryRange.Paragraphs;
                            job.CurrentParagraphCount = job.CurrentParagraphs.Count;
                            job.NextParagraphOneBased = 1;
                        }
                        catch
                        {
                            try { job.CurrentStoryRange = job.CurrentStoryRange.NextStoryRange; }
                            catch { job.CurrentStoryRange = null; }
                            job.CurrentParagraphs = null;
                            continue;
                        }
                    }

                    Word.Paragraphs paras = job.CurrentParagraphs!;
                    while (job.NextParagraphOneBased <= job.CurrentParagraphCount)
                    {
                        int pIndex = job.NextParagraphOneBased++;
                        try
                        {
                            Word.Range r = paras[pIndex].Range;
                            string rawBeforeClean = r.Text ?? string.Empty;
                            string raw = CleanWordControlChars(rawBeforeClean);
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                bool hasMyanmar = ContainsMyanmarOrZawgyiCandidate(raw);
                                bool inTable = rawBeforeClean.IndexOf('\a') >= 0;

                                job.Snapshots.Add(new ParagraphSnapshot
                                {
                                    StoryType = storyType,
                                    StoryName = GetFriendlyStoryName(storyType, inTable),
                                    ParagraphIndex = pIndex,
                                    ParagraphRange = null, // Never pin thousands of live COM Range RCWs across threads!
                                    StartOffset = r.Start,
                                    EndOffset = r.End,
                                    RawText = raw,
                                    NeedsNlpCheck = hasMyanmar
                                });
                            }
                        }
                        catch { }

                        if ((pIndex & 7) == 0 && sw.ElapsedMilliseconds >= budgetMs)
                        {
                            return false;
                        }
                    }

                    try { job.CurrentStoryRange = job.CurrentStoryRange.NextStoryRange; }
                    catch { job.CurrentStoryRange = null; }
                    job.CurrentParagraphs = null;
                    job.NextParagraphOneBased = 1;

                    if (sw.ElapsedMilliseconds >= budgetMs)
                        return false;
                }

                job.StoryTypeIndex++;
            }

            return true;
        }

        private static bool TryCollectPlainStoryFast(
            Word.Range storyRange,
            Word.WdStoryType storyType,
            List<ParagraphSnapshot> output)
        {
            try
            {
                if (storyRange.Tables.Count > 0 ||
                    storyRange.Fields.Count > 0 ||
                    storyRange.Hyperlinks.Count > 0 ||
                    storyRange.Footnotes.Count > 0 ||
                    storyRange.Endnotes.Count > 0 ||
                    storyRange.InlineShapes.Count > 0)
                {
                    return false;
                }

                int storyStart = storyRange.Start;
                int storyEnd = storyRange.End;
                string fullText = storyRange.Text ?? string.Empty;

                // Confirm exact 1:1 story-character-to-UTF16 alignment before using pure C# paragraph splitting
                if (fullText.Length == 0)
                    return true;
                if ((storyEnd - storyStart) != fullText.Length || fullText.IndexOf('\a') >= 0)
                    return false;

                if (ContainsMyanmarOrZawgyiCandidate(fullText))
                {
                    try
                    {
                        if (storyRange.NoProofing != 1)
                        {
                            bool wasSaved = storyRange.Document.Saved;
                            storyRange.NoProofing = 1;
                            if (wasSaved) storyRange.Document.Saved = true;
                        }
                    }
                    catch { }
                }

                string storyLabel = GetFriendlyStoryName(storyType, inTable: false);
                int pIndex = 1;
                int segStart = 0;
                int len = fullText.Length;

                for (int i = 0; i < len; i++)
                {
                    if (fullText[i] == '\r')
                    {
                        int segEndExclusive = i + 1; // Include trailing '\r' to match Word Paragraph.Range.End
                        AppendFastParagraphSlice(fullText, storyStart, segStart, segEndExclusive, storyType, storyLabel, pIndex, output);
                        pIndex++;
                        segStart = segEndExclusive;
                    }
                }

                if (segStart < len)
                {
                    AppendFastParagraphSlice(fullText, storyStart, segStart, len, storyType, storyLabel, pIndex, output);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AppendFastParagraphSlice(
            string fullStoryText,
            int storyStartOffset,
            int sliceStart,
            int sliceEndExclusive,
            Word.WdStoryType storyType,
            string storyLabel,
            int paragraphIndex,
            List<ParagraphSnapshot> output)
        {
            int sliceLen = sliceEndExclusive - sliceStart;
            if (sliceLen <= 0)
                return;

            // Quick non-allocating whitespace check before slicing
            bool hasNonWs = false;
            bool hasMyanmar = false;
            for (int k = sliceStart; k < sliceEndExclusive; k++)
            {
                char ch = fullStoryText[k];
                if (ch != '\r' && ch != '\n' && ch != '\v' && ch != '\f' && !char.IsWhiteSpace(ch))
                {
                    hasNonWs = true;
                    if ((ch >= '\u1000' && ch <= '\u109F') || (ch >= '\uAA60' && ch <= '\uAA7F'))
                    {
                        hasMyanmar = true;
                        break;
                    }
                }
            }

            if (!hasNonWs)
                return;

            if (!hasMyanmar)
            {
                // Finish scanning rest of slice for Myanmar script if we broke early on ASCII
                for (int k = sliceStart; k < sliceEndExclusive; k++)
                {
                    char ch = fullStoryText[k];
                    if ((ch >= '\u1000' && ch <= '\u109F') || (ch >= '\uAA60' && ch <= '\uAA7F'))
                    {
                        hasMyanmar = true;
                        break;
                    }
                }
            }

            string rawSlice = CleanWordControlChars(fullStoryText.Substring(sliceStart, sliceLen));
            output.Add(new ParagraphSnapshot
            {
                StoryType = storyType,
                StoryName = storyLabel,
                ParagraphIndex = paragraphIndex,
                ParagraphRange = null,
                StartOffset = storyStartOffset + sliceStart,
                EndOffset = storyStartOffset + sliceEndExclusive,
                RawText = rawSlice,
                NeedsNlpCheck = hasMyanmar
            });
        }

        private void LaunchBackgroundNlpAndUnderline(
            Word.Document doc,
            int generation,
            List<ParagraphSnapshot> snapshots,
            int caretFocusOffset)
        {
            Task.Run(() =>
            {
                if (generation != Volatile.Read(ref _scanGeneration))
                    return;

                int count = snapshots.Count;
                var issuesPerParagraph = new List<ProofingIssue>[count];

                // Multi-core parallel NLP evaluation on large documents (>= 48 paragraphs)
                if (count >= 48 && Environment.ProcessorCount > 1)
                {
                    int maxDegree = Math.Min(Environment.ProcessorCount, 6);
                    Parallel.For(
                        0,
                        count,
                        new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
                        (idx, loopState) =>
                        {
                            if (generation != Volatile.Read(ref _scanGeneration))
                            {
                                loopState.Stop();
                                return;
                            }

                            var snap = snapshots[idx];
                            issuesPerParagraph[idx] = snap.NeedsNlpCheck
                                ? _engine.CheckText(snap.RawText)
                                : new List<ProofingIssue>(0);
                        });
                }
                else
                {
                    for (int idx = 0; idx < count; idx++)
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        var snap = snapshots[idx];
                        issuesPerParagraph[idx] = snap.NeedsNlpCheck
                            ? _engine.CheckText(snap.RawText)
                            : new List<ProofingIssue>(0);
                    }
                }

                if (generation != Volatile.Read(ref _scanGeneration))
                    return;

                var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)>(count);
                var allIssuesFlat = new List<ProofingIssue>(Math.Min(1024, count * 2));
                var plainTextBuilder = new StringBuilder(Math.Min(512_000, count * 64));

                for (int i = 0; i < count; i++)
                {
                    var snap = snapshots[i];
                    var issues = issuesPerParagraph[i] ?? new List<ProofingIssue>(0);
                    plainTextBuilder.AppendLine(snap.RawText);
                    if (issues.Count > 0)
                        allIssuesFlat.AddRange(issues);
                    analyzed.Add((snap, issues));
                }

                if (generation != Volatile.Read(ref _scanGeneration))
                    return;

                DocumentQualityReport qualityReport = DocumentQualityAnalyzer.Analyze(
                    plainTextBuilder.ToString(),
                    allIssuesFlat,
                    _engine);

                // Prioritize paragraphs closest to the user's viewport/caret first so visible errors get underlined immediately!
                int[] order = new int[count];
                for (int i = 0; i < count; i++)
                    order[i] = i;

                if (count > SafeUnderlineManager.MaxLiveWavyUnderlinesPerDocument / 2 && caretFocusOffset > 0)
                {
                    Array.Sort(order, (a, b) =>
                    {
                        var sa = analyzed[a].Snap;
                        var sb = analyzed[b].Snap;
                        int pa = sa.StoryType == Word.WdStoryType.wdMainTextStory ? Math.Abs(sa.StartOffset - caretFocusOffset) : 1_000_000 + a;
                        int pb = sb.StoryType == Word.WdStoryType.wdMainTextStory ? Math.Abs(sb.StartOffset - caretFocusOffset) : 1_000_000 + b;
                        int cmp = pa.CompareTo(pb);
                        return cmp != 0 ? cmp : a.CompareTo(b);
                    });
                }

                if (_staMarshaller.IsHandleCreated && !_staMarshaller.IsDisposed)
                {
                    _staMarshaller.BeginInvoke(new Action(() =>
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        bool docHadZeroUnderlines = false;
                        try
                        {
                            if (_underlineManager.GetTrackedCountForDocument(doc) == 0)
                            {
                                Word.WdUnderline docUl = doc.Content.Font.Underline;
                                docHadZeroUnderlines = docUl == Word.WdUnderline.wdUnderlineNone;
                            }
                        }
                        catch { }

                        var job = new PendingFullUnderlineJob
                        {
                            Generation = generation,
                            Document = doc,
                            Analyzed = analyzed,
                            ProcessingOrder = order,
                            BoundByParagraph = new List<DocumentProofingItem>?[count],
                            QualityReport = qualityReport,
                            DoneMap = new Dictionary<int, string>(count),
                            DocHadZeroUnderlinesAtStart = docHadZeroUnderlines,
                            NextOrderCursor = 0
                        };

                        if (analyzed.Count <= ImmediateUnderlineParagraphThreshold)
                        {
                            ProcessUnderlineJobChunk(job, analyzed.Count, budgetMs: 25);
                            FinalizeCompletedUnderlineJob(job);
                        }
                        else
                        {
                            _activeUnderlineJob = job;
                            ProcessUnderlineJobChunk(job, UnderlineParagraphsPerChunk, MaxUnderlineChunkWallMs);
                            if (job.NextOrderCursor >= job.ProcessingOrder.Length)
                            {
                                CancelActiveJobs();
                                FinalizeCompletedUnderlineJob(job);
                            }
                            else
                            {
                                _underlineChunkTimer.Start();
                            }
                        }
                    }));
                }
            });
        }

        private void OnUnderlineChunkTimerTick(object? sender, EventArgs e)
        {
            var job = _activeUnderlineJob;
            if (job == null || job.Generation != Volatile.Read(ref _scanGeneration))
            {
                _underlineChunkTimer.Stop();
                _activeUnderlineJob = null;
                return;
            }

            try
            {
                ProcessUnderlineJobChunk(job, UnderlineParagraphsPerChunk, MaxUnderlineChunkWallMs);
                if (job.NextOrderCursor >= job.ProcessingOrder.Length)
                {
                    _underlineChunkTimer.Stop();
                    _activeUnderlineJob = null;
                    FinalizeCompletedUnderlineJob(job);
                }
            }
            catch
            {
                _underlineChunkTimer.Stop();
                _activeUnderlineJob = null;
            }
        }

        private void ProcessUnderlineJobChunk(PendingFullUnderlineJob job, int maxComParagraphs, int budgetMs)
        {
            if (job.NextOrderCursor >= job.ProcessingOrder.Length)
                return;

            var sw = Stopwatch.StartNew();
            int comTouched = 0;

            _underlineManager.ExecuteInSharedProofingBatch(job.Document, () =>
            {
                while (job.NextOrderCursor < job.ProcessingOrder.Length)
                {
                    if (job.Generation != Volatile.Read(ref _scanGeneration))
                        return;

                    int paraIdx = job.ProcessingOrder[job.NextOrderCursor];
                    var (snap, issues) = job.Analyzed[paraIdx];
                    job.DoneMap[snap.StartOffset] = snap.RawText;

                    // Fast zero-COM skip for clean paragraphs when document had zero prior underlines!
                    if (issues.Count == 0 &&
                        (job.DocHadZeroUnderlinesAtStart || !_underlineManager.HasLiveWavyUnderlineInSpan(job.Document, snap.StartOffset, snap.EndOffset)))
                    {
                        if (job.DocHadZeroUnderlinesAtStart)
                        {
                            job.NextOrderCursor++;
                            continue;
                        }
                    }

                    // Fast zero-COM unbound creation once document hits the 450 live wavy underline cap!
                    if (issues.Count > 0 &&
                        job.DocHadZeroUnderlinesAtStart &&
                        _underlineManager.GetTrackedCountForDocument(job.Document) >= SafeUnderlineManager.MaxLiveWavyUnderlinesPerDocument)
                    {
                        job.BoundByParagraph[paraIdx] = SafeUnderlineManager.CreateUnboundProofingItems(
                            snap.StoryName,
                            snap.StoryType,
                            snap.ParagraphIndex,
                            snap.StartOffset,
                            snap.EndOffset,
                            snap.RawText,
                            issues);
                        job.NextOrderCursor++;
                        continue;
                    }

                    if (issues.Count > 0 || snap.NeedsNlpCheck)
                    {
                        Word.Range? paraRange = snap.ParagraphRange ?? TrySliceStoryRange(job, snap.StoryType, snap.StartOffset, snap.EndOffset);
                        if (paraRange != null)
                        {
                            bool canUnderline = _underlineManager.GetTrackedCountForDocument(job.Document) < SafeUnderlineManager.MaxLiveWavyUnderlinesPerDocument;
                            var bound = _underlineManager.BindAndUnderlineRangeIssues(
                                job.Document,
                                paraRange,
                                snap.StoryName,
                                snap.ParagraphIndex,
                                issues,
                                insideSharedBatch: true,
                                applyVisualUnderline: canUnderline,
                                preloadedCleanText: snap.RawText,
                                storyType: snap.StoryType,
                                knownDocHasNoUnderlines: job.DocHadZeroUnderlinesAtStart ? true : (bool?)null);

                            if (bound.Count > 0)
                            {
                                job.BoundByParagraph[paraIdx] = bound;
                            }
                            comTouched++;
                        }
                    }

                    job.NextOrderCursor++;

                    if (comTouched >= maxComParagraphs || sw.ElapsedMilliseconds >= budgetMs)
                        break;
                }
            });
        }

        private static Word.Range? TrySliceStoryRange(
            PendingFullUnderlineJob job,
            Word.WdStoryType storyType,
            int startOffset,
            int endOffset)
        {
            if (endOffset <= startOffset)
                return null;

            try
            {
                if (!job.StoryBaseRanges.TryGetValue(storyType, out var baseRange))
                {
                    baseRange = storyType == Word.WdStoryType.wdMainTextStory
                        ? job.Document.Content
                        : job.Document.StoryRanges[storyType];
                    if (baseRange != null)
                    {
                        job.StoryBaseRanges[storyType] = baseRange;
                    }
                }

                if (baseRange == null)
                    return null;

                Word.Range slice = baseRange.Duplicate;
                slice.SetRange(startOffset, endOffset);
                return slice;
            }
            catch
            {
                return null;
            }
        }

        private void FinalizeCompletedUnderlineJob(PendingFullUnderlineJob job)
        {
            if (job.Generation != Volatile.Read(ref _scanGeneration))
                return;

            var orderedBound = new List<DocumentProofingItem>(256);
            for (int i = 0; i < job.BoundByParagraph.Length; i++)
            {
                var list = job.BoundByParagraph[i];
                if (list != null && list.Count > 0)
                {
                    orderedBound.AddRange(list);
                }
            }

            _lastParagraphTextByStart[job.Document] = job.DoneMap;
            _documentIssuesCache[job.Document] = orderedBound;
            _documentQualityCache[job.Document] = job.QualityReport;
            ScanCompleted?.Invoke(job.Document, orderedBound);
        }

        private static bool ContainsMyanmarOrZawgyiCandidate(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u1000' && c <= '\u109F') || (c >= '\uAA60' && c <= '\uAA7F'))
                    return true;
            }
            return false;
        }

        private static string GetFriendlyStoryName(Word.WdStoryType storyType, bool inTable)
        {
            if (inTable)
                return "ဇယားကွက် (Table)";

            switch (storyType)
            {
                case Word.WdStoryType.wdPrimaryHeaderStory:
                case Word.WdStoryType.wdFirstPageHeaderStory:
                case Word.WdStoryType.wdEvenPagesHeaderStory:
                    return "စာမျက်နှာထိပ်စီး (Header)";
                case Word.WdStoryType.wdPrimaryFooterStory:
                case Word.WdStoryType.wdFirstPageFooterStory:
                case Word.WdStoryType.wdEvenPagesFooterStory:
                    return "စာမျက်နှာအောက်ခြေ (Footer)";
                default:
                    return "စာကိုယ် (Body)";
            }
        }

        /// <summary>
        /// Replaces Word's end-of-cell marker (\r\a) with spaces of identical length so character offsets remain 1:1.
        /// </summary>
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

        public void Dispose()
        {
            Interlocked.Increment(ref _scanGeneration);
            _snapshotChunkTimer.Stop();
            _snapshotChunkTimer.Dispose();
            _underlineChunkTimer.Stop();
            _underlineChunkTimer.Dispose();
            _deferredFullScanTimer.Stop();
            _deferredFullScanTimer.Dispose();
            _liveTypingWatchTimer.Stop();
            _liveTypingWatchTimer.Dispose();
            _pasteDebounceTimer.Stop();
            _pasteDebounceTimer.Dispose();
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
            _staMarshaller.Dispose();
        }
    }
}
