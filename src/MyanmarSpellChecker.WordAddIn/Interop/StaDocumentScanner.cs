using System;
using System.Collections.Generic;
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
    /// Coordinates STA-safe extraction of Word text across Main Body, Word Tables, and Headers/Footers (StoryRanges),
    /// runs NLP &amp; DocumentQualityAnalyzer asynchronously on a background thread with generation cancellation,
    /// and binds results to live Word.Range objects in non-blocking STA chunks so Word never freezes on large documents.
    /// </summary>
    public sealed class StaDocumentScanner : IDisposable
    {
        private const int ImmediateUnderlineParagraphThreshold = 18;
        private const int UnderlineParagraphsPerChunk = 10;

        private sealed class ParagraphSnapshot
        {
            public string StoryName { get; set; } = string.Empty;
            public int ParagraphIndex { get; set; }
            public Word.Range ParagraphRange { get; set; } = null!;
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
            public string RawText { get; set; } = string.Empty;
            public bool NeedsNlpCheck { get; set; } = true;
        }

        private sealed class PendingFullUnderlineJob
        {
            public int Generation { get; set; }
            public Word.Document Document { get; set; } = null!;
            public List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)> Analyzed { get; set; } = null!;
            public DocumentQualityReport QualityReport { get; set; } = null!;
            public List<DocumentProofingItem> BoundItems { get; } = new List<DocumentProofingItem>();
            public Dictionary<int, string> DoneMap { get; set; } = null!;
            public int NextIndex { get; set; }
        }

        private readonly Word.Application _app;
        private readonly MyanmarProofingEngine _engine;
        private readonly SafeUnderlineManager _underlineManager;
        private readonly Control _staMarshaller;
        private readonly WinFormsTimer _debounceTimer;
        private readonly WinFormsTimer _pasteDebounceTimer;
        private readonly WinFormsTimer _liveTypingWatchTimer;
        private readonly WinFormsTimer _underlineChunkTimer;

        private int _scanGeneration;
        private PendingFullUnderlineJob? _activeUnderlineJob;

        private Word.Document? _watchedDoc;
        private int _watchedDocLength = -1;
        private int _watchedActiveParaStart = -1;
        private int _lastScannedDocLength = -1;
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

            _debounceTimer = new WinFormsTimer { Interval = 650 };
            _debounceTimer.Tick += OnDebounceTimerTick;

            // Fast paste / bulk-change debounce (~220ms): underlines newly pasted paragraphs almost instantaneously.
            _pasteDebounceTimer = new WinFormsTimer { Interval = 220 };
            _pasteDebounceTimer.Tick += OnPasteDebounceTimerTick;

            // Chunked STA underline pump: applies at most 10 paragraphs per 15ms UI slice so Word stays 60 FPS responsive.
            _underlineChunkTimer = new WinFormsTimer { Interval = 15 };
            _underlineChunkTimer.Tick += OnUnderlineChunkTimerTick;

            // Live keystroke idle watcher: observes O(1) document end + active paragraph text without O(Doc) Paragraphs.Count.
            _liveTypingWatchTimer = new WinFormsTimer { Interval = 650 };
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
            CancelActiveUnderlineJob();
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

            if (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc))
            {
                Interlocked.Increment(ref _scanGeneration);
                CancelActiveUnderlineJob();
            }

            if (ReferenceEquals(_watchedDoc, doc))
            {
                _watchedDoc = null;
                _watchedDocLength = -1;
                _watchedActiveParaStart = -1;
                _watchedActiveParaText = string.Empty;
                _hasPendingTypingChanges = false;
                _hasPendingMultiParagraphPaste = false;
                _hadMultiCharSelection = false;
            }

            _documentIssuesCache.Remove(doc);
            _lastParagraphTextByStart.Remove(doc);
            _documentQualityCache.Remove(doc);
        }

        private void CancelActiveUnderlineJob()
        {
            _underlineChunkTimer.Stop();
            _activeUnderlineJob = null;
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
                    _hadMultiCharSelection = (sel.Range.End - sel.Range.Start) >= 2;
                    Word.Range probe = sel.Range.Duplicate;
                    probe.SetRange(sel.Range.Start, sel.Range.Start);
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
        /// immediately without waiting for two slow idle-timer ticks.
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
                    _watchedActiveParaText = string.Empty;
                    _hadMultiCharSelection = isMultiCharSel;
                    if (docLen > 1 && !_documentIssuesCache.ContainsKey(doc))
                    {
                        _pasteDebounceTimer.Stop();
                        _pasteDebounceTimer.Start();
                    }
                    return;
                }

                int lenDelta = Math.Abs(docLen - _watchedDocLength);
                bool replacedSelection = _hadMultiCharSelection && !isMultiCharSel;
                _hadMultiCharSelection = isMultiCharSel;

                // Paste / bulk-edit signature: document length jumped by >= 4 chars in a single event,
                // OR user had a multi-char selection (e.g. Ctrl+A) that just collapsed via paste/replace.
                if (lenDelta >= 4 || replacedSelection)
                {
                    if (replacedSelection && lenDelta == 0)
                    {
                        // Verify whether the text at/before caret actually changed (distinguishes Ctrl+A -> Right-Arrow from equal-length Ctrl+A -> Ctrl+V!)
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

                    if (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc))
                    {
                        Interlocked.Increment(ref _scanGeneration);
                        CancelActiveUnderlineJob();
                    }

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

            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (doc != null)
                {
                    _watchedDoc = doc;
                    _watchedDocLength = doc.Content.End;
                    _lastScannedDocLength = _watchedDocLength;
                    _hasPendingTypingChanges = false;
                    _hasPendingMultiParagraphPaste = false;
                }
            }
            catch { }

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
                int activeParaStart = -1;
                string activeParaText = string.Empty;

                try
                {
                    int caretStart = sel.Range.Start;
                    Word.Range probe = sel.Range.Duplicate;
                    probe.SetRange(caretStart, caretStart);
                    if (probe.Paragraphs.Count > 0)
                    {
                        Word.Paragraph p1 = probe.Paragraphs[1];
                        activeParaStart = p1.Range.Start;
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
                }

                if (docLen != _watchedDocLength || !string.Equals(activeParaText, _watchedActiveParaText, StringComparison.Ordinal))
                {
                    int lenDelta = Math.Abs(docLen - _watchedDocLength);
                    if (lenDelta >= 10 ||
                        (_lastScannedDocLength >= 0 && Math.Abs(docLen - _lastScannedDocLength) > 48) ||
                        (activeParaStart != _watchedActiveParaStart && lenDelta >= 4))
                    {
                        _hasPendingMultiParagraphPaste = true;
                    }

                    // If a background chunked full-doc underline job was running, cancel it immediately so typing has zero contention
                    if (_activeUnderlineJob != null && ReferenceEquals(_activeUnderlineJob.Document, doc))
                    {
                        Interlocked.Increment(ref _scanGeneration);
                        CancelActiveUnderlineJob();
                    }

                    _watchedDocLength = docLen;
                    _watchedActiveParaStart = activeParaStart;
                    _watchedActiveParaText = activeParaText;
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

                    // User is actively typing single keystrokes; wait until keystrokes pause for 1 tick (~650ms)
                    _hasPendingTypingChanges = true;
                    return;
                }

                if (_hasPendingTypingChanges)
                {
                    bool fullScan = _hasPendingMultiParagraphPaste;
                    _hasPendingTypingChanges = false;
                    _hasPendingMultiParagraphPaste = false;
                    _lastScannedDocLength = docLen;
                    if (fullScan)
                    {
                        ScanFullDocumentAsync();
                    }
                    else
                    {
                        ScanActiveParagraphAsync();
                    }
                }
            }
            catch
            {
                // Ignore transient COM busy errors during modal dialogs or IME composition
            }
        }

        /// <summary>
        /// Scans the currently edited paragraph (and recent preceding paragraphs across &lt;Enter&gt; newline transitions)
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
                Word.Range caretProbe = sel.Range.Duplicate;
                caretProbe.SetRange(caretStart, caretStart);
                Word.Paragraphs paras = caretProbe.Paragraphs;
                if (paras.Count == 0)
                    return;

                Word.Paragraph activePara = paras[1];
                var candidateParas = new List<Word.Paragraph>(6);
                try
                {
                    Word.Paragraph? cursor = activePara;
                    var prevChain = new List<Word.Paragraph>(5);
                    for (int step = 0; step < 5; step++)
                    {
                        cursor = cursor?.Previous();
                        if (cursor == null)
                            break;
                        prevChain.Add(cursor);
                    }
                    for (int i = prevChain.Count - 1; i >= 0; i--)
                    {
                        candidateParas.Add(prevChain[i]);
                    }
                }
                catch { }
                candidateParas.Add(activePara);

                bool docMayHaveTables = false;
                try { docMayHaveTables = doc.Tables.Count > 0; } catch { }

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
                    string rawText = CleanWordControlChars(paraRange.Text ?? string.Empty);

                    if (string.IsNullOrWhiteSpace(rawText))
                        continue;

                    // Skip re-scanning & re-underlining if this paragraph's text has not changed AND any expected wavy underlines are still live on screen
                    // (Prevents polluting Word's Ctrl+Z Undo stack when user moves the cursor, while re-underlining if underlines were undone/cleared/pasted over)
                    if (updateMap.TryGetValue(startOffset, out string? prevText) &&
                        string.Equals(prevText, rawText, StringComparison.Ordinal))
                    {
                        bool expectsUnderlines = ContainsMyanmarOrZawgyiCandidate(rawText) && _engine.CheckText(rawText).Count > 0;
                        if (!expectsUnderlines || _underlineManager.HasLiveWavyUnderlineInSpan(doc, startOffset, endOffset))
                        {
                            continue;
                        }
                    }

                    bool inTable = false;
                    if (docMayHaveTables)
                    {
                        try { inTable = (bool)paraRange.Information[Word.WdInformation.wdWithInTable]; } catch { }
                    }

                    pendingSnapshots.Add(new ParagraphSnapshot
                    {
                        StoryName = GetFriendlyStoryName(paraRange.StoryType, inTable),
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
                    var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)>(pendingSnapshots.Count);
                    foreach (var snap in pendingSnapshots)
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        List<ProofingIssue> issues = snap.NeedsNlpCheck
                            ? _engine.CheckText(snap.RawText)
                            : new List<ProofingIssue>();
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
                                    foreach (var (snap, issues) in analyzed)
                                    {
                                        List<DocumentProofingItem> bound = _underlineManager.BindAndUnderlineRangeIssues(
                                            doc,
                                            snap.ParagraphRange,
                                            snap.StoryName,
                                            snap.ParagraphIndex,
                                            issues);

                                        doneMap[snap.StartOffset] = snap.RawText;

                                        const int DriftWindow = 640;
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
        /// with generation cancellation, background Quality Report computation, and non-blocking STA chunked underlining.
        /// </summary>
        public void ScanFullDocumentAsync()
        {
            try
            {
                Word.Document? doc = _app.ActiveDocument;
                if (doc == null)
                    return;

                int generation = Interlocked.Increment(ref _scanGeneration);
                CancelActiveUnderlineJob();
                ScanStarted?.Invoke(doc);

                // Stage 1: Collect lightweight paragraph snapshots across Main Body, Tables, and Headers/Footers on STA Thread
                var snapshots = CollectStoryParagraphSnapshots(doc);

                // Stage 2: Execute NLP + DocumentQualityAnalyzer off the UI thread
                Task.Run(() =>
                {
                    var analyzed = new List<(ParagraphSnapshot Snap, List<ProofingIssue> Issues)>(snapshots.Count);
                    var allIssuesFlat = new List<ProofingIssue>(Math.Min(512, snapshots.Count * 2));
                    var plainTextBuilder = new StringBuilder(snapshots.Count * 64);

                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        if (generation != Volatile.Read(ref _scanGeneration))
                            return;

                        var snap = snapshots[i];
                        plainTextBuilder.AppendLine(snap.RawText);

                        List<ProofingIssue> issues = snap.NeedsNlpCheck
                            ? _engine.CheckText(snap.RawText)
                            : new List<ProofingIssue>();

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

                    // Stage 3: Bind to live Word.Range objects & render wavy underlines on STA Thread (chunked for large docs!)
                    if (_staMarshaller.IsHandleCreated && !_staMarshaller.IsDisposed)
                    {
                        _staMarshaller.BeginInvoke(new Action(() =>
                        {
                            if (generation != Volatile.Read(ref _scanGeneration))
                                return;

                            var job = new PendingFullUnderlineJob
                            {
                                Generation = generation,
                                Document = doc,
                                Analyzed = analyzed,
                                QualityReport = qualityReport,
                                DoneMap = new Dictionary<int, string>(analyzed.Count),
                                NextIndex = 0
                            };

                            if (analyzed.Count <= ImmediateUnderlineParagraphThreshold)
                            {
                                ProcessUnderlineJobChunk(job, analyzed.Count);
                                FinalizeCompletedUnderlineJob(job);
                            }
                            else
                            {
                                _activeUnderlineJob = job;
                                ProcessUnderlineJobChunk(job, UnderlineParagraphsPerChunk);
                                if (job.NextIndex >= job.Analyzed.Count)
                                {
                                    CancelActiveUnderlineJob();
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
            catch
            {
                // Ignore
            }
        }

        private void OnUnderlineChunkTimerTick(object? sender, EventArgs e)
        {
            var job = _activeUnderlineJob;
            if (job == null || job.Generation != Volatile.Read(ref _scanGeneration))
            {
                CancelActiveUnderlineJob();
                return;
            }

            try
            {
                ProcessUnderlineJobChunk(job, UnderlineParagraphsPerChunk);
                if (job.NextIndex >= job.Analyzed.Count)
                {
                    CancelActiveUnderlineJob();
                    FinalizeCompletedUnderlineJob(job);
                }
            }
            catch
            {
                CancelActiveUnderlineJob();
            }
        }

        private void ProcessUnderlineJobChunk(PendingFullUnderlineJob job, int maxParagraphs)
        {
            int endExclusive = Math.Min(job.Analyzed.Count, job.NextIndex + maxParagraphs);
            if (endExclusive <= job.NextIndex)
                return;

            _underlineManager.ExecuteInSharedProofingBatch(job.Document, () =>
            {
                for (int i = job.NextIndex; i < endExclusive; i++)
                {
                    if (job.Generation != Volatile.Read(ref _scanGeneration))
                        return;

                    var (snap, issues) = job.Analyzed[i];
                    if (issues.Count > 0 || snap.NeedsNlpCheck)
                    {
                        var bound = _underlineManager.BindAndUnderlineRangeIssues(
                            job.Document,
                            snap.ParagraphRange,
                            snap.StoryName,
                            snap.ParagraphIndex,
                            issues);
                        if (bound.Count > 0)
                            job.BoundItems.AddRange(bound);
                    }
                    job.DoneMap[snap.StartOffset] = snap.RawText;
                }
            });

            job.NextIndex = endExclusive;
        }

        private void FinalizeCompletedUnderlineJob(PendingFullUnderlineJob job)
        {
            if (job.Generation != Volatile.Read(ref _scanGeneration))
                return;

            _lastParagraphTextByStart[job.Document] = job.DoneMap;
            _documentIssuesCache[job.Document] = job.BoundItems;
            _documentQualityCache[job.Document] = job.QualityReport;
            ScanCompleted?.Invoke(job.Document, job.BoundItems);
        }

        private static List<ParagraphSnapshot> CollectStoryParagraphSnapshots(Word.Document doc)
        {
            var snapshots = new List<ParagraphSnapshot>();
            Word.WdStoryType[] targetStories =
            {
                Word.WdStoryType.wdMainTextStory,
                Word.WdStoryType.wdPrimaryHeaderStory,
                Word.WdStoryType.wdFirstPageHeaderStory,
                Word.WdStoryType.wdPrimaryFooterStory
            };

            foreach (var storyType in targetStories)
            {
                try
                {
                    Word.Range? storyRange = null;
                    try
                    {
                        storyRange = doc.StoryRanges[storyType];
                    }
                    catch
                    {
                        continue;
                    }

                    while (storyRange != null)
                    {
                        // Query Tables.Count ONCE per StoryRange rather than calling slow layout-dependent wdWithInTable per paragraph!
                        bool storyHasTables = false;
                        try { storyHasTables = storyRange.Tables.Count > 0; } catch { }

                        int pIndex = 1;
                        foreach (Word.Paragraph para in storyRange.Paragraphs)
                        {
                            Word.Range r = para.Range;
                            string raw = CleanWordControlChars(r.Text ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                bool hasMyanmarCandidate = ContainsMyanmarOrZawgyiCandidate(raw);
                                bool inTable = false;
                                if (storyHasTables && hasMyanmarCandidate)
                                {
                                    try { inTable = (bool)r.Information[Word.WdInformation.wdWithInTable]; } catch { }
                                }

                                snapshots.Add(new ParagraphSnapshot
                                {
                                    StoryName = GetFriendlyStoryName(storyType, inTable),
                                    ParagraphIndex = pIndex,
                                    ParagraphRange = r,
                                    StartOffset = r.Start,
                                    EndOffset = r.End,
                                    RawText = raw,
                                    NeedsNlpCheck = hasMyanmarCandidate
                                });
                            }
                            pIndex++;
                        }

                        storyRange = storyRange.NextStoryRange;
                    }
                }
                catch
                {
                    // Continue with next story
                }
            }

            return snapshots;
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
            _underlineChunkTimer.Stop();
            _underlineChunkTimer.Dispose();
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
