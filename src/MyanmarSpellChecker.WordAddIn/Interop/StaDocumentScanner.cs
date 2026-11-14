using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using Word = Microsoft.Office.Interop.Word;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace MyanmarSpellChecker.WordAddIn.Interop
{
    public sealed class WordScanOutcomeMetadata
    {
        public bool IsEcoActive { get; set; }
        public bool WasPartialAutoScan { get; set; }
        public bool WasIssueCapReached { get; set; }
        public bool WasCancelled { get; set; }
        public int ScannedParagraphCount { get; set; }
        public int TotalDocumentChars { get; set; }
    }

    /// <summary>
    /// Coordinates STA-safe, non-blocking extraction of Word text across Main Body, Word Tables, and Headers/Footers (StoryRanges),
    /// runs multi-core NLP &amp; DocumentQualityAnalyzer asynchronously on background threads with generation cancellation,
    /// and binds results to live Word.Range objects in wall-clock-budgeted STA chunks so Word never freezes on 500+ page documents.
    /// </summary>
    public sealed class StaDocumentScanner : IDisposable
    {
        private const int ImmediateUnderlineParagraphThreshold = 8;
        private const int UnderlineParagraphsPerChunk = 10;
        private const int MaxSnapshotChunkWallMs = 8;
        private const int MaxUnderlineChunkWallMs = 8;
        private const int MaxStoryChainHops = 16;
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
            public int TotalDocumentChars { get; set; }
            public bool AllowSmartPartialAutoScan { get; set; }
            public bool CheckedSmartPartial { get; set; }
            public List<ParagraphSnapshot> Snapshots { get; } = new List<ParagraphSnapshot>(256);
            public HashSet<long> VisitedStoryKeys { get; } = new HashSet<long>();
            public int StoryTypeIndex { get; set; }
            public int StoryHopCount { get; set; }
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
            public int VisualUnderlineCap { get; set; } = SafeUnderlineManager.MaxLiveWavyUnderlinesPerDocument;
            public WordScanOutcomeMetadata OutcomeMetadata { get; set; } = new WordScanOutcomeMetadata();
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
        private int _activeBackgroundNlpGeneration;
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
        private readonly Dictionary<Word.Document, WordScanOutcomeMetadata> _documentScanMeta =
            new Dictionary<Word.Document, WordScanOutcomeMetadata>();
        private bool _lastEcoActive;

        public bool IsLiveAutoScanEnabled { get; set; } = true;
        public bool HasPendingTypingChanges => _hasPendingTypingChanges;
        public bool IsEcoActive => _lastEcoActive;

        public event Action<Word.Document>? ScanStarted;
        public event Action<int, string>? ScanProgressChanged;
        public event Action<bool>? EcoActiveChanged;
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

            // Deferred post-open timer so Word finishes laying out newly opened pages & CustomTaskPane before scan begins.
            _deferredFullScanTimer = new WinFormsTimer { Interval = 480 };
            _deferredFullScanTimer.Tick += OnDeferredFullScanTimerTick;

            // Time-budgeted STA paragraph snapshot collector (<= 8ms per 24ms tick) so WM_PAINT and layout run freely.
            _snapshotChunkTimer = new WinFormsTimer { Interval = 24 };
            _snapshotChunkTimer.Tick += OnSnapshotChunkTimerTick;

            // Wall-clock-budgeted STA underline pump (<= 8ms per 28ms UI slice) so Word stays 60 FPS responsive.
            _underlineChunkTimer = new WinFormsTimer { Interval = 28 };
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

        public WordScanOutcomeMetadata? GetLastScanMetadata(Word.Document? doc)
        {
            if (doc != null && _documentScanMeta.TryGetValue(doc, out var meta))
            {
                return meta;
            }
            return null;
        }

        public void RefreshEcoActiveStateForActiveDocument()
        {
            try
            {
                Word.Document? doc = _app.ActiveDocument;
                int charLen = doc != null ? doc.Content.End : 0;
                var budgets = EcoPerformanceGovernor.ComputeBudgets(EcoPerformanceGovernor.IsLargeWordDocument(0, charLen));
                UpdateEcoActiveState(budgets.IsEcoActive);
            }
            catch
            {
                var budgets = EcoPerformanceGovernor.ComputeBudgets(isLargeFile: false);
                UpdateEcoActiveState(budgets.IsEcoActive);
            }
        }

        private void UpdateEcoActiveState(bool ecoActive)
        {
            if (_lastEcoActive != ecoActive)
            {
                _lastEcoActive = ecoActive;
                EcoActiveChanged?.Invoke(ecoActive);
            }
            int targetInterval = ecoActive ? 1350 : 950;
            if (_liveTypingWatchTimer.Interval != targetInterval)
            {
                _liveTypingWatchTimer.Interval = targetInterval;
            }
        }

        public void CancelUserScan()
        {
            Interlocked.Increment(ref _scanGeneration);
            CancelActiveJobs();
            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (doc != null)
                {
                    _documentScanMeta[doc] = new WordScanOutcomeMetadata
                    {
                        IsEcoActive = _lastEcoActive,
                        WasCancelled = true
                    };
                    ScanCompleted?.Invoke(doc, GetCachedIssues(doc));
                }
            }
            catch { }
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
                _documentScanMeta.Remove(doc);
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

            bool wasLarge = _documentScanMeta.TryGetValue(doc, out var meta) &&
                            (meta.TotalDocumentChars >= EcoPerformanceGovernor.LargeWordCharacterThreshold || meta.IsEcoActive);

            _documentIssuesCache.Remove(doc);
            _lastParagraphTextByStart.Remove(doc);
            _documentQualityCache.Remove(doc);
            _documentScanMeta.Remove(doc);

            if (wasLarge || _documentIssuesCache.Count == 0)
            {
                _engine.TrimMemoryCaches();
            }
        }

        private void CancelActiveJobs()
        {
            _snapshotChunkTimer.Stop();
            _activeSnapshotJob = null;
            _underlineChunkTimer.Stop();
            _activeUnderlineJob = null;
            Interlocked.Exchange(ref _activeBackgroundNlpGeneration, 0);
        }

        private bool IsFullScanScheduledOrActiveFor(Word.Document doc)
        {
            if (_deferredFullScanTimer.Enabled || _pasteDebounceTimer.Enabled)
                return true;
            if (_activeSnapshotJob != null && ReferenceEquals(_activeSnapshotJob.Document, doc))
                return true;
            if (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc))
                return true;
            return Volatile.Read(ref _activeBackgroundNlpGeneration) != 0;
        }

        public void ScheduleDeferredFullDocumentScan(int delayMs = 480)
        {
            _deferredFullScanTimer.Stop();
            _pasteDebounceTimer.Stop();
            _debounceTimer.Stop();
            Interlocked.Increment(ref _scanGeneration);
            CancelActiveJobs();
            ResetDirtySpan();
            _hasPendingTypingChanges = false;
            _hasPendingMultiParagraphPaste = false;

            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (doc != null)
                {
                    _watchedDoc = doc;
                    _watchedDocLength = doc.Content.End;
                    _lastScannedDocLength = _watchedDocLength;
                }
            }
            catch { }

            _deferredFullScanTimer.Interval = Math.Max(120, delayMs);
            _deferredFullScanTimer.Start();
        }

        private void OnDeferredFullScanTimerTick(object? sender, EventArgs e)
        {
            _deferredFullScanTimer.Stop();
            ScanFullDocumentAsync(allowSmartPartialAutoScan: true);
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
                    if (docLen > 1 && !_documentIssuesCache.ContainsKey(doc) && !IsFullScanScheduledOrActiveFor(doc))
                    {
                        ScheduleDeferredFullDocumentScan(480);
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

            ScanFullDocumentAsync(allowSmartPartialAutoScan: true);
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
            if (!IsLiveAutoScanEnabled || _deferredFullScanTimer.Enabled)
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
                    bool alreadyScheduled = IsFullScanScheduledOrActiveFor(doc);
                    bool needsInitialScan = docLen > 1 && !_documentIssuesCache.ContainsKey(doc) && !alreadyScheduled;
                    _watchedDoc = doc;
                    _watchedDocLength = docLen;
                    _watchedActiveParaStart = activeParaStart;
                    _lastObservedSelectionStart = caretStart;
                    _lastObservedSelectionEnd = caretEnd;
                    ResetDirtySpan();
                    _lastScannedDocLength = needsInitialScan ? -1 : docLen;
                    _watchedActiveParaText = activeParaText;
                    _hasPendingTypingChanges = false;
                    _hasPendingMultiParagraphPaste = false;
                    if (needsInitialScan)
                    {
                        ScheduleDeferredFullDocumentScan(480);
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
                    try
                    {
                        var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)>(pendingSnapshots.Count);
                        for (int i = 0; i < pendingSnapshots.Count; i++)
                        {
                            if (generation != Volatile.Read(ref _scanGeneration))
                                return;

                            var snap = pendingSnapshots[i];
                            List<ProofingIssue> issues = snap.NeedsNlpCheck
                                ? CheckTextCrashSafe(snap.RawText)
                                : new List<ProofingIssue>(0);
                            analyzed.Add((snap, issues));
                        }

                        SafeBeginInvokeOnSta(() =>
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
                        });
                    }
                    catch { }
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
                            ? CheckTextCrashSafe(snap.RawText)
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

                    SafeBeginInvokeOnSta(() =>
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

                                if (_underlineManager.HasEverAppliedWavyUnderlines(doc) && snap.ParagraphRange != null)
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
                    });
                });
            }
            catch
            {
                // Ignore transient COM errors
            }
        }

        /// <summary>
        /// Scans Main Body, Tables, and Headers/Footers (StoryRanges) across the entire active document
        /// using non-blocking whole-story / time-sliced STA extraction, hardware-throttled background NLP,
        /// and wall-clock-budgeted chunked underlining so Word never freezes on 50–500+ page documents.
        /// When <paramref name="allowSmartPartialAutoScan"/> is true (e.g. automatic DocumentOpen trigger),
        /// huge documents only quick-scan the active/visible paragraph window so weak ("potato") PCs stay at ~0% idle load.
        /// </summary>
        public void ScanFullDocumentAsync(bool allowSmartPartialAutoScan = false)
        {
            try
            {
                _deferredFullScanTimer.Stop();
                _pasteDebounceTimer.Stop();
                _debounceTimer.Stop();
                Word.Document? doc = _app.ActiveDocument;
                if (doc == null)
                    return;

                int generation = Interlocked.Increment(ref _scanGeneration);
                CancelActiveJobs();
                Interlocked.Exchange(ref _activeBackgroundNlpGeneration, generation);
                ResetDirtySpan();

                int docCharLen = 0;
                try { docCharLen = doc.Content.End; } catch { }
                int caretOffset = 0;
                try { caretOffset = _app.Selection?.Start ?? 0; } catch { }

                _watchedDoc = doc;
                _watchedDocLength = docCharLen;
                _lastScannedDocLength = docCharLen;

                bool isLargeDoc = EcoPerformanceGovernor.IsLargeWordDocument(0, docCharLen);
                EcoRuntimeBudgets openBudgets = EcoPerformanceGovernor.ComputeBudgets(isLargeDoc);
                UpdateEcoActiveState(openBudgets.IsEcoActive);

                ScanStarted?.Invoke(doc);
                ScanProgressChanged?.Invoke(5, UiLocalizer.Pick("⏳ Reading document text...", "⏳ စာသားဖတ်ယူနေသည်..."));

                // Always yield one WinForms message-pump turn via _snapshotChunkTimer so Task Pane progress UI & [Stop] button
                // complete layout and WM_PAINT before any COM StoryRanges / Paragraphs calls execute on STA!
                var snapshotJob = new PendingSnapshotCollectionJob
                {
                    Generation = generation,
                    Document = doc,
                    CaretFocusOffset = Math.Max(0, caretOffset),
                    TotalDocumentChars = docCharLen,
                    AllowSmartPartialAutoScan = allowSmartPartialAutoScan,
                    CheckedSmartPartial = false,
                    StoryTypeIndex = 0
                };

                _activeSnapshotJob = snapshotJob;
                _snapshotChunkTimer.Start();
            }
            catch
            {
                // Ignore
            }
        }

        private bool TryStartSmartPartialViewportScan(
            Word.Document doc,
            int generation,
            int caretOffset,
            int docCharLen,
            EcoRuntimeBudgets budgets)
        {
            try
            {
                int halfSpanBefore = 6_000;
                int spanAfter = Math.Max(12_000, budgets.MaxAutoOpenWordChars - halfSpanBefore);
                int winStart = Math.Max(0, caretOffset - halfSpanBefore);
                int winEnd = Math.Min(docCharLen, winStart + budgets.MaxAutoOpenWordChars);
                if (winEnd - winStart < budgets.MaxAutoOpenWordChars && winStart > 0)
                {
                    winStart = Math.Max(0, winEnd - budgets.MaxAutoOpenWordChars);
                }

                Word.Range windowRange = doc.Range(winStart, winEnd);
                Word.Paragraphs paras = windowRange.Paragraphs;
                int pCount = paras.Count;
                if (pCount <= 0)
                    return false;

                try
                {
                    windowRange.Start = paras[1].Range.Start;
                    windowRange.End = paras[pCount].Range.End;
                }
                catch { }

                var partialSnapshots = new List<ParagraphSnapshot>(Math.Min(pCount, budgets.MaxAutoOpenWordParagraphs));
                if (!TryCollectPlainStoryFast(windowRange, Word.WdStoryType.wdMainTextStory, partialSnapshots))
                {
                    int limit = Math.Min(pCount, budgets.MaxAutoOpenWordParagraphs);
                    for (int i = 1; i <= limit; i++)
                    {
                        Word.Range? pr = null;
                        try { pr = paras[i].Range; } catch { continue; }
                        if (pr == null) continue;
                        string raw = pr.Text ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        bool inTbl = raw.IndexOf('\a') >= 0;
                        string clean = CleanWordControlChars(raw);
                        partialSnapshots.Add(new ParagraphSnapshot
                        {
                            StoryType = Word.WdStoryType.wdMainTextStory,
                            StoryName = GetFriendlyStoryName(Word.WdStoryType.wdMainTextStory, inTbl),
                            ParagraphIndex = i,
                            ParagraphRange = pr,
                            StartOffset = pr.Start,
                            EndOffset = pr.End,
                            RawText = clean,
                            NeedsNlpCheck = ContainsMyanmarOrZawgyiCandidate(clean)
                        });
                    }
                }
                else if (partialSnapshots.Count > budgets.MaxAutoOpenWordParagraphs)
                {
                    partialSnapshots.RemoveRange(budgets.MaxAutoOpenWordParagraphs, partialSnapshots.Count - budgets.MaxAutoOpenWordParagraphs);
                }

                LaunchBackgroundNlpAndUnderline(doc, generation, partialSnapshots, caretOffset, wasPartialAutoScan: true, docCharLen);
                return true;
            }
            catch
            {
                return false;
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
                if (!job.CheckedSmartPartial)
                {
                    job.CheckedSmartPartial = true;
                    if (job.AllowSmartPartialAutoScan)
                    {
                        bool isLargeDoc = EcoPerformanceGovernor.IsLargeWordDocument(0, job.TotalDocumentChars);
                        EcoRuntimeBudgets openBudgets = EcoPerformanceGovernor.ComputeBudgets(isLargeDoc);
                        if (isLargeDoc || (openBudgets.IsEcoActive && job.TotalDocumentChars >= 16_000))
                        {
                            if (TryStartSmartPartialViewportScan(job.Document, job.Generation, job.CaretFocusOffset, job.TotalDocumentChars, openBudgets))
                            {
                                _snapshotChunkTimer.Stop();
                                _activeSnapshotJob = null;
                                return;
                            }
                        }
                    }
                }

                if (ProcessSnapshotCollectionSlice(job, MaxSnapshotChunkWallMs))
                {
                    _snapshotChunkTimer.Stop();
                    _activeSnapshotJob = null;
                    int docChars = job.TotalDocumentChars;
                    if (docChars <= 0)
                    {
                        try { docChars = job.Document.Content.End; } catch { }
                    }
                    LaunchBackgroundNlpAndUnderline(job.Document, job.Generation, job.Snapshots, job.CaretFocusOffset, wasPartialAutoScan: false, docChars);
                }
                else if (job.CurrentParagraphCount > 0)
                {
                    int pct = Math.Min(18, 4 + (job.NextParagraphOneBased * 14) / Math.Max(1, job.CurrentParagraphCount));
                    ScanProgressChanged?.Invoke(pct, UiLocalizer.Pick($"⏳ Reading paragraphs... ({job.Snapshots.Count:N0})", $"⏳ စာပိုဒ်များ ဖတ်ယူနေသည်... ({job.Snapshots.Count:N0})"));
                }
            }
            catch
            {
                _snapshotChunkTimer.Stop();
                _activeSnapshotJob = null;
                Interlocked.CompareExchange(ref _activeBackgroundNlpGeneration, 0, job.Generation);
            }
        }

        private static void AdvanceNextStoryRangeSafe(PendingSnapshotCollectionJob job, Word.WdStoryType storyType)
        {
            job.CurrentParagraphs = null;
            job.NextParagraphOneBased = 1;

            if (job.CurrentStoryRange == null || job.StoryHopCount >= MaxStoryChainHops)
            {
                job.CurrentStoryRange = null;
                return;
            }

            try
            {
                Word.Range? next = job.CurrentStoryRange.NextStoryRange;
                if (next == null)
                {
                    job.CurrentStoryRange = null;
                    return;
                }

                job.StoryHopCount++;
                long key = (((long)(int)storyType) << 48) ^ (((long)(uint)next.Start) << 24) ^ (uint)next.End;
                if (!job.VisitedStoryKeys.Add(key))
                {
                    job.CurrentStoryRange = null;
                    return;
                }

                job.CurrentStoryRange = next;
            }
            catch
            {
                job.CurrentStoryRange = null;
            }
        }

        /// <summary>
        /// Collects paragraph snapshots within a strict wall-clock millisecond slice (<paramref name="budgetMs"/>).
        /// Strictly 100% READ-ONLY (never mutates <c>NoProofing</c>, <c>Font</c>, or <c>Saved</c> during collection!).
        /// Uses a 1-call whole-story read (<c>storyRange.Text</c>) whenever a story has 1:1 plain-text offsets,
        /// skips non-Myanmar header/footer/shape stories in 1 COM read without enumerating floating-anchor <c>Paragraphs</c>,
        /// and falls back to time-sliced chunked paragraph indexing without ever calling layout-blocking <c>wdWithInTable</c>.
        /// Returns <c>true</c> when all stories are collected.
        /// </summary>
        private bool ProcessSnapshotCollectionSlice(PendingSnapshotCollectionJob job, int budgetMs)
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

                    job.StoryHopCount = 0;
                    job.CurrentParagraphs = null;
                    job.NextParagraphOneBased = 1;
                    try
                    {
                        long firstKey = (((long)(int)storyType) << 48) ^ (((long)(uint)job.CurrentStoryRange.Start) << 24) ^ (uint)job.CurrentStoryRange.End;
                        job.VisitedStoryKeys.Add(firstKey);
                    }
                    catch { }
                }

                while (job.CurrentStoryRange != null)
                {
                    // Tier 1: Ultra-Fast Whole-Story Single-Read Path (zero per-paragraph COM calls!)
                    if (job.CurrentParagraphs == null)
                    {
                        if (TryCollectPlainStoryFast(job.CurrentStoryRange, storyType, job.Snapshots))
                        {
                            AdvanceNextStoryRangeSafe(job, storyType);

                            if (sw.ElapsedMilliseconds >= budgetMs)
                                return false;
                            continue;
                        }

                        // Whole-story non-Myanmar fast skip before Tier 2:
                        // Avoids enumerating header/footer Paragraphs when headers contain floating <wp:anchor> graphics or English hyperlinks!
                        try
                        {
                            string storyProbe = job.CurrentStoryRange.Text ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(storyProbe) ||
                                (!ContainsMyanmarOrZawgyiCandidate(storyProbe) &&
                                 !_underlineManager.HasEverAppliedWavyUnderlines(job.Document)))
                            {
                                AdvanceNextStoryRangeSafe(job, storyType);
                                if (sw.ElapsedMilliseconds >= budgetMs)
                                    return false;
                                continue;
                            }
                        }
                        catch { }

                        // Tier 2: Story contains Myanmar text plus Tables, Hyperlinks, Fields, Footnotes, or Shapes — prepare chunked indexing
                        try
                        {
                            job.CurrentParagraphs = job.CurrentStoryRange.Paragraphs;
                            job.CurrentParagraphCount = job.CurrentParagraphs.Count;
                            job.NextParagraphOneBased = 1;
                        }
                        catch
                        {
                            AdvanceNextStoryRangeSafe(job, storyType);
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

                    AdvanceNextStoryRangeSafe(job, storyType);

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

        private void SafeBeginInvokeOnSta(Action action)
        {
            if (_staMarshaller == null || _staMarshaller.IsDisposed || !_staMarshaller.IsHandleCreated)
                return;

            try
            {
                _staMarshaller.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private List<ProofingIssue> CheckTextCrashSafe(string rawText)
        {
            try
            {
                return _engine.CheckText(rawText);
            }
            catch (OutOfMemoryException)
            {
                _engine.TrimMemoryCaches(aggressive: true);
                GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                return new List<ProofingIssue>(0);
            }
        }

        private void PostProgressFromBackground(int generation, int percent, string statusText)
        {
            if (generation != Volatile.Read(ref _scanGeneration))
                return;

            SafeBeginInvokeOnSta(() =>
            {
                if (generation == Volatile.Read(ref _scanGeneration))
                {
                    ScanProgressChanged?.Invoke(percent, statusText);
                }
            });
        }

        private void LaunchBackgroundNlpAndUnderline(
            Word.Document doc,
            int generation,
            List<ParagraphSnapshot> snapshots,
            int caretFocusOffset,
            bool wasPartialAutoScan,
            int totalDocChars)
        {
            Task.Run(() =>
            {
                try
                {
                    if (generation != Volatile.Read(ref _scanGeneration))
                        return;

                    EcoPerformanceGovernor.MaybeTrimUnderMemoryPressure(() => _engine.TrimMemoryCaches(aggressive: true));

                    int count = snapshots.Count;
                    bool isLargeFile = EcoPerformanceGovernor.IsLargeWordDocument(count, totalDocChars);
                    EcoRuntimeBudgets budgets = EcoPerformanceGovernor.ComputeBudgets(isLargeFile);
                    int maxIssueCap = budgets.MaxIssuesPerFullScan;
                    bool wasIssueCapReached = false;
                    var issuesPerParagraph = new List<ProofingIssue>[count];

                    EcoPerformanceGovernor.ExecuteWithGentlePriority(budgets.UseLowPriorityThreads, () =>
                    {
                        long lastProgressTick = Environment.TickCount;
                        int runningIssues = 0;

                        if (count >= 48 && budgets.MaxBackgroundWorkers > 1 && !wasPartialAutoScan)
                        {
                            int completedCount = 0;
                            Parallel.For(
                                0,
                                count,
                                new ParallelOptions { MaxDegreeOfParallelism = budgets.MaxBackgroundWorkers },
                                (idx, loopState) =>
                                {
                                    if (generation != Volatile.Read(ref _scanGeneration))
                                    {
                                        loopState.Stop();
                                        return;
                                    }
                                    if (Volatile.Read(ref runningIssues) >= maxIssueCap)
                                    {
                                        wasIssueCapReached = true;
                                        loopState.Stop();
                                        return;
                                    }

                                    var snap = snapshots[idx];
                                    var found = snap.NeedsNlpCheck
                                        ? CheckTextCrashSafe(snap.RawText)
                                        : new List<ProofingIssue>(0);
                                    issuesPerParagraph[idx] = found;

                                    if (found.Count > 0)
                                    {
                                        int totalNow = Interlocked.Add(ref runningIssues, found.Count);
                                        if (totalNow >= maxIssueCap)
                                        {
                                            wasIssueCapReached = true;
                                        }
                                    }

                                    int done = Interlocked.Increment(ref completedCount);
                                    if ((done & 255) == 0)
                                    {
                                        EcoPerformanceGovernor.MaybeTrimUnderMemoryPressure(() => _engine.TrimMemoryCaches(aggressive: true));
                                    }

                                    long nowTick = Environment.TickCount;
                                    long prevTick = Interlocked.Read(ref lastProgressTick);
                                    if (nowTick - prevTick >= 120 && Interlocked.CompareExchange(ref lastProgressTick, nowTick, prevTick) == prevTick)
                                    {
                                        int pct = 18 + (done * 60) / Math.Max(1, count);
                                        int dispPct = Math.Min(99, (done * 100) / Math.Max(1, count));
                                        PostProgressFromBackground(generation, Math.Min(78, pct), UiLocalizer.Pick($"⏳ Checking spelling & grammar... {dispPct}%", $"⏳ စာလုံးပေါင်း စစ်ဆေးနေသည်... {dispPct}%"));
                                    }
                                });
                        }
                        else
                        {
                            int nlpChecked = 0;
                            for (int idx = 0; idx < count; idx++)
                            {
                                if (generation != Volatile.Read(ref _scanGeneration))
                                    return;

                                if (runningIssues >= maxIssueCap)
                                {
                                    wasIssueCapReached = true;
                                    break;
                                }

                                var snap = snapshots[idx];
                                if (snap.NeedsNlpCheck)
                                {
                                    nlpChecked++;
                                    EcoPerformanceGovernor.MaybeMicroYield(nlpChecked, budgets.MicroYieldEveryItems);
                                    if ((nlpChecked & 255) == 0)
                                    {
                                        EcoPerformanceGovernor.MaybeTrimUnderMemoryPressure(() => _engine.TrimMemoryCaches(aggressive: true));
                                    }
                                    var found = CheckTextCrashSafe(snap.RawText);
                                    issuesPerParagraph[idx] = found;
                                    runningIssues += found.Count;
                                }
                                else
                                {
                                    issuesPerParagraph[idx] = new List<ProofingIssue>(0);
                                }

                                long nowTick = Environment.TickCount;
                                if (!wasPartialAutoScan && count >= 32 && nowTick - lastProgressTick >= 120)
                                {
                                    lastProgressTick = nowTick;
                                    int pct = 18 + ((idx + 1) * 60) / Math.Max(1, count);
                                    int dispPct = ((idx + 1) * 100) / Math.Max(1, count);
                                    PostProgressFromBackground(generation, Math.Min(78, pct), UiLocalizer.Pick($"⏳ Checking spelling & grammar... {dispPct}%", $"⏳ စာလုံးပေါင်း စစ်ဆေးနေသည်... {dispPct}%"));
                                }
                            }
                        }
                    });

                    if (generation != Volatile.Read(ref _scanGeneration))
                        return;

                    var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)>(count);
                    var allIssuesFlat = new List<ProofingIssue>(Math.Min(1024, count * 2));
                    // Bound quality-analyzer text builder on massive documents so weak PCs never allocate giant strings on LOH
                    int maxQualityTextChars = budgets.IsEcoActive ? 120_000 : 380_000;
                    var plainTextBuilder = new StringBuilder(Math.Min(maxQualityTextChars, count * 64));
                    int collectedCapRemaining = maxIssueCap;

                    for (int i = 0; i < count; i++)
                    {
                        var snap = snapshots[i];
                        var rawIssues = issuesPerParagraph[i] ?? new List<ProofingIssue>(0);
                        List<ProofingIssue> issues;
                        if (rawIssues.Count > collectedCapRemaining)
                        {
                            issues = rawIssues.GetRange(0, Math.Max(0, collectedCapRemaining));
                            collectedCapRemaining = 0;
                            wasIssueCapReached = true;
                        }
                        else
                        {
                            issues = rawIssues;
                            collectedCapRemaining -= rawIssues.Count;
                        }

                        if (plainTextBuilder.Length < maxQualityTextChars)
                        {
                            plainTextBuilder.AppendLine(snap.RawText);
                        }
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

                    int underlineCap = budgets.WordVisualUnderlineCap;

                    // Prioritize paragraphs closest to the user's viewport/caret first so visible errors get underlined immediately!
                    int[] order = new int[count];
                    for (int i = 0; i < count; i++)
                        order[i] = i;

                    if (count > underlineCap / 2 && caretFocusOffset > 0)
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

                    var outcomeMeta = new WordScanOutcomeMetadata
                    {
                        IsEcoActive = budgets.IsEcoActive,
                        WasPartialAutoScan = wasPartialAutoScan,
                        WasIssueCapReached = wasIssueCapReached,
                        WasCancelled = false,
                        ScannedParagraphCount = count,
                        TotalDocumentChars = totalDocChars
                    };

                    SafeBeginInvokeOnSta(() =>
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        UpdateEcoActiveState(budgets.IsEcoActive);

                        // Pure O(1) managed check — immune to user wdUnderlineSingle headings or hyperlinks!
                        bool docHadZeroUnderlines = !_underlineManager.HasEverAppliedWavyUnderlines(doc);

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
                            VisualUnderlineCap = underlineCap,
                            OutcomeMetadata = outcomeMeta,
                            NextOrderCursor = 0
                        };

                        if (analyzed.Count <= ImmediateUnderlineParagraphThreshold)
                        {
                            ProcessUnderlineJobChunk(job, analyzed.Count, budgetMs: 12);
                            FinalizeCompletedUnderlineJob(job);
                        }
                        else
                        {
                            _activeUnderlineJob = job;
                            int sliceMs = budgets.IsEcoActive ? 6 : MaxUnderlineChunkWallMs;
                            ProcessUnderlineJobChunk(job, UnderlineParagraphsPerChunk, sliceMs);
                            if (job.NextOrderCursor >= job.ProcessingOrder.Length)
                            {
                                CancelActiveJobs();
                                FinalizeCompletedUnderlineJob(job);
                            }
                            else
                            {
                                ScanProgressChanged?.Invoke(84, UiLocalizer.Pick("⏳ Applying proofing underlines...", "⏳ မျဉ်းတားမှတ်သားနေသည်..."));
                                _underlineChunkTimer.Start();
                            }
                        }
                    });
                }
                catch (OutOfMemoryException)
                {
                    Interlocked.CompareExchange(ref _activeBackgroundNlpGeneration, 0, generation);
                    _engine.TrimMemoryCaches(aggressive: true);
                    GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                }
                catch
                {
                    Interlocked.CompareExchange(ref _activeBackgroundNlpGeneration, 0, generation);
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
                int sliceMs = job.OutcomeMetadata.IsEcoActive ? 6 : MaxUnderlineChunkWallMs;
                ProcessUnderlineJobChunk(job, UnderlineParagraphsPerChunk, sliceMs);
                if (job.NextOrderCursor >= job.ProcessingOrder.Length)
                {
                    _underlineChunkTimer.Stop();
                    _activeUnderlineJob = null;
                    FinalizeCompletedUnderlineJob(job);
                }
                else
                {
                    int pct = 80 + (job.NextOrderCursor * 19) / Math.Max(1, job.ProcessingOrder.Length);
                    int cappedPct = Math.Min(99, pct);
                    ScanProgressChanged?.Invoke(cappedPct, UiLocalizer.Pick($"⏳ Applying proofing underlines... {cappedPct}%", $"⏳ မျဉ်းတားမှတ်သားနေသည်... {cappedPct}%"));
                }
            }
            catch
            {
                _underlineChunkTimer.Stop();
                _activeUnderlineJob = null;
                Interlocked.CompareExchange(ref _activeBackgroundNlpGeneration, 0, job.Generation);
            }
        }

        private bool TrySkipZeroComParagraph(PendingFullUnderlineJob job, int paraIdx, ParagraphSnapshot snap, List<ProofingIssue> issues, int underlineCap)
        {
            bool hasTrackedInPara = !job.DocHadZeroUnderlinesAtStart &&
                                    _underlineManager.HasTrackedInSpanForDocument(job.Document, snap.StartOffset, snap.EndOffset);

            // Fast zero-COM skip for clean paragraphs whenever no prior add-in wavy underline exists in this paragraph!
            if (issues.Count == 0 && !hasTrackedInPara)
            {
                return true;
            }

            // Fast zero-COM unbound creation once document hits the live wavy underline cap (80 in Eco Mode, 450 standard)!
            if (issues.Count > 0 &&
                !hasTrackedInPara &&
                _underlineManager.GetTrackedCountForDocument(job.Document) >= underlineCap)
            {
                job.BoundByParagraph[paraIdx] = SafeUnderlineManager.CreateUnboundProofingItems(
                    snap.StoryName,
                    snap.StoryType,
                    snap.ParagraphIndex,
                    snap.StartOffset,
                    snap.EndOffset,
                    snap.RawText,
                    issues);
                return true;
            }

            return false;
        }

        private void ProcessUnderlineJobChunk(PendingFullUnderlineJob job, int maxComParagraphs, int budgetMs)
        {
            if (job.NextOrderCursor >= job.ProcessingOrder.Length)
                return;

            int underlineCap = Math.Max(10, job.VisualUnderlineCap);

            // Pre-drain clean / capped paragraphs in pure C# so 100% clean chunks never toggle ScreenUpdating or open UndoRecord!
            while (job.NextOrderCursor < job.ProcessingOrder.Length)
            {
                if (job.Generation != Volatile.Read(ref _scanGeneration))
                    return;

                int peekParaIdx = job.ProcessingOrder[job.NextOrderCursor];
                var (peekSnap, peekIssues) = job.Analyzed[peekParaIdx];
                if (TrySkipZeroComParagraph(job, peekParaIdx, peekSnap, peekIssues, underlineCap))
                {
                    job.DoneMap[peekSnap.StartOffset] = peekSnap.RawText;
                    job.NextOrderCursor++;
                    continue;
                }
                break;
            }

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

                    if (TrySkipZeroComParagraph(job, paraIdx, snap, issues, underlineCap))
                    {
                        job.NextOrderCursor++;
                        continue;
                    }

                    Word.Range? paraRange = snap.ParagraphRange ?? TrySliceStoryRange(job, snap.StoryType, snap.StartOffset, snap.EndOffset);
                    if (paraRange != null)
                    {
                        bool canUnderline = _underlineManager.GetTrackedCountForDocument(job.Document) < underlineCap;
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
                            knownDocHasNoUnderlines: job.DocHadZeroUnderlinesAtStart ? true : (bool?)null,
                            maxLiveUnderlines: underlineCap,
                            allowResidualWavyScrub: false);

                        if (bound.Count > 0)
                        {
                            job.BoundByParagraph[paraIdx] = bound;
                        }
                        comTouched++;
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

            Interlocked.CompareExchange(ref _activeBackgroundNlpGeneration, 0, job.Generation);

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
            _documentScanMeta[job.Document] = job.OutcomeMetadata;
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
                return UiLocalizer.Pick("Table", "ဇယားကွက် (Table)");

            switch (storyType)
            {
                case Word.WdStoryType.wdPrimaryHeaderStory:
                case Word.WdStoryType.wdFirstPageHeaderStory:
                case Word.WdStoryType.wdEvenPagesHeaderStory:
                    return UiLocalizer.Pick("Header", "စာမျက်နှာထိပ်စီး (Header)");
                case Word.WdStoryType.wdPrimaryFooterStory:
                case Word.WdStoryType.wdFirstPageFooterStory:
                case Word.WdStoryType.wdEvenPagesFooterStory:
                    return UiLocalizer.Pick("Footer", "စာမျက်နှာအောက်ခြေ (Footer)");
                default:
                    return UiLocalizer.Pick("Body", "စာကိုယ် (Body)");
            }
        }

        /// <summary>
        /// Replaces Word's end-of-cell marker (\r\a) with spaces of identical length so character offsets remain 1:1.
        /// </summary>
        private static string CleanWordControlChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int firstCtrl = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\a' || c == '\r' || c == '\v' || c == '\f')
                {
                    firstCtrl = i;
                    break;
                }
            }
            if (firstCtrl < 0)
                return text;

            char[] chars = text.ToCharArray();
            for (int i = firstCtrl; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '\a' || c == '\r' || c == '\v' || c == '\f')
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
