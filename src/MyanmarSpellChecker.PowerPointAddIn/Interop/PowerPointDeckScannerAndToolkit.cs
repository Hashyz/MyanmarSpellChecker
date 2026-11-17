// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Presentation;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.Core.Typography;
using Office = Microsoft.Office.Core;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace MyanmarSpellChecker.PowerPointAddIn.Interop
{
    public sealed class PowerPointScanOutcomeMetadata
    {
        public bool IsEcoActive { get; set; }
        public bool WasCancelled { get; set; }
        public int ScannedContainerCount { get; set; }
        public int ScannedSlideCount { get; set; }
        public bool IncludedSlideMasters { get; set; }
        public long ScannedCellCount => ScannedContainerCount;
        public long TotalScopeCells => ScannedContainerCount;
        public bool WasPartialAutoScan => false;
        public bool WasIssueCapReached => false;
    }

    public sealed class PowerPointToolkitOutcome
    {
        public bool Success { get; set; }
        public int ContainersModified { get; set; }
        public int TotalChanges { get; set; }
        public string UserMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Zero-Formatting-Mutation Slide/Table/Group/Notes Scanner &amp; Formatting-Safe Presentation Toolkit for Microsoft PowerPoint.
    /// Enforces:
    /// 1. 100% non-destructive scanning: never modifies slide Font.Underline or Font.Color to draw squiggles;
    ///    all visual wavy underlines are rendered on a transparent Win32 layered overlay via TextRange screen bounds.
    /// 2. Auto-suspended during Slide Show (F5 / Presenter View) and transient shape-dragging operations.
    /// 3. Surgical 1-based <c>TextRange.Characters(start + 1, length).Text</c> edits in descending offset order
    ///    wrapped in <c>StartNewUndoEntry()</c> + <c>PresentationUndoManager</c> so bold, size, RGB color, hyperlinks,
    ///    bullet levels, and slide animations remain 100% intact.
    /// </summary>
    public sealed class PowerPointDeckScannerAndToolkit : IDisposable
    {
        private readonly PowerPoint.Application _pptApp;
        private readonly MyanmarProofingEngine _engine;
        private readonly PresentationUndoManager _undoManager = new PresentationUndoManager(maxBatches: 30);

        private readonly Dictionary<string, List<PresentationSlideIssue>> _issuesByPresentation =
            new Dictionary<string, List<PresentationSlideIssue>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DocumentQualityReport> _qualityByPresentation =
            new Dictionary<string, DocumentQualityReport>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, PowerPointScanOutcomeMetadata> _scanMetaByPresentation =
            new Dictionary<string, PowerPointScanOutcomeMetadata>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _suppressedOverlayPresentations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _noProofSyncedTextByTarget =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly System.Windows.Forms.Timer _deferredScanTimer;
        private readonly System.Windows.Forms.Timer _liveTypingPollTimer;
        private string _lastPolledActiveShapeKey = string.Empty;
        private string _lastPolledActiveTextSnapshot = string.Empty;
        private PresentationScanScope _deferredScope = PresentationScanScope.ActiveSlide;
        private CancellationTokenSource? _activeScanCts;
        private int _suppressEventsDepth;
        private bool _lastEcoActive;
        private bool _isSlideShowRunning;
        private bool _disposed;

        public bool IsLiveAutoScanEnabled { get; set; } = true;
        public bool IsVisualUnderlineEnabled { get; set; } = true;
        public bool IncludeSlideMasters { get; set; } = false;
        public bool IsEcoActive => _lastEcoActive;
        public bool IsScanning => _activeScanCts != null;
        public bool IsSlideShowRunning => _isSlideShowRunning || IsSlideShowActive(_pptApp);
        public int VisualStateVersion { get; private set; }
        public PresentationScanScope CurrentScope { get; set; } = PresentationScanScope.ActiveSlide;
        public PresentationUndoManager UndoManager => _undoManager;

        public event Action? ScanStarted;
        public event Action<int, string>? ScanProgressChanged;
        public event Action<bool>? EcoActiveChanged;
        public event Action<IReadOnlyList<PresentationSlideIssue>>? ScanCompleted;
        public event Action? VisualUnderlinesInvalidated;
        public event Action? UndoStateChanged;

        public PowerPointDeckScannerAndToolkit(PowerPoint.Application pptApp, MyanmarProofingEngine engine)
        {
            _pptApp = pptApp ?? throw new ArgumentNullException(nameof(pptApp));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            // 450 ms idle debounce for live Active-Slide auto-scan (smooth 60 FPS shape dragging & typing)
            _deferredScanTimer = new System.Windows.Forms.Timer { Interval = 450 };
            _deferredScanTimer.Tick += OnDeferredScanTimerTick;

            // Lightweight foreground-only active-TextBox typing watcher (PowerPoint does not fire WindowSelectionChange per keystroke)
            _liveTypingPollTimer = new System.Windows.Forms.Timer { Interval = 650 };
            _liveTypingPollTimer.Tick += OnLiveTypingPollTick;
            _liveTypingPollTimer.Start();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            CancelActiveScan();
            _liveTypingPollTimer.Stop();
            _liveTypingPollTimer.Dispose();
            _deferredScanTimer.Stop();
            _deferredScanTimer.Dispose();
        }

        public void SetSlideShowState(bool running)
        {
            _isSlideShowRunning = running;
            if (running)
            {
                CancelActiveScan();
                _deferredScanTimer.Stop();
            }
            NotifyVisualUnderlinesInvalidated();
        }

        public void CancelActiveScan()
        {
            _deferredScanTimer.Stop();
            if (_activeScanCts != null)
            {
                try { _activeScanCts.Cancel(); } catch { }
                try { _activeScanCts.Dispose(); } catch { }
                _activeScanCts = null;
            }
        }

        public void NotifyVisualUnderlinesInvalidated()
        {
            unchecked { VisualStateVersion++; }
            VisualUnderlinesInvalidated?.Invoke();
        }

        public static bool IsSlideShowActive(PowerPoint.Application? app)
        {
            if (app == null)
                return false;
            try
            {
                return app.SlideShowWindows != null && app.SlideShowWindows.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPowerPointReadyForInterop(PowerPoint.Application? app)
        {
            if (app == null)
                return false;

            try
            {
                if (IsSlideShowActive(app))
                    return false;
                if (app.Presentations == null || app.Presentations.Count == 0)
                    return false;
                if (app.Windows == null || app.Windows.Count == 0)
                    return false;
                return true;
            }
            catch (COMException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsTransientBusyComError(COMException ex)
        {
            uint hr = (uint)ex.ErrorCode;
            return hr == 0x80010001 || hr == 0x8001010A || hr == 0x80048240;
        }

        public string GetActivePresentationKey()
        {
            try
            {
                if (!IsPowerPointReadyForInterop(_pptApp))
                    return string.Empty;
                PowerPoint.Presentation pres = _pptApp.ActivePresentation;
                if (pres == null)
                    return string.Empty;
                return !string.IsNullOrWhiteSpace(pres.FullName) ? pres.FullName : pres.Name;
            }
            catch
            {
                return string.Empty;
            }
        }

        public IReadOnlyList<PresentationSlideIssue> GetCachedIssuesForActivePresentation()
        {
            string key = GetActivePresentationKey();
            if (!string.IsNullOrEmpty(key) && _issuesByPresentation.TryGetValue(key, out var list))
                return list;
            return Array.Empty<PresentationSlideIssue>();
        }

        public DocumentQualityReport? GetCachedQualityForActivePresentation()
        {
            string key = GetActivePresentationKey();
            if (!string.IsNullOrEmpty(key) && _qualityByPresentation.TryGetValue(key, out var rep))
                return rep;
            return null;
        }

        public PowerPointScanOutcomeMetadata? GetLastScanMetadataForActivePresentation()
        {
            string key = GetActivePresentationKey();
            if (!string.IsNullOrEmpty(key) && _scanMetaByPresentation.TryGetValue(key, out var meta))
                return meta;
            return null;
        }

        public bool HasActiveVisualUnderlinesForActivePresentation()
        {
            if (!IsVisualUnderlineEnabled || IsSlideShowRunning)
                return false;

            string key = GetActivePresentationKey();
            if (string.IsNullOrEmpty(key) || _suppressedOverlayPresentations.Contains(key))
                return false;

            if (!_issuesByPresentation.TryGetValue(key, out var list) || list.Count == 0)
                return false;

            int activeSlideId = GetActiveSlideIdSafe();
            if (activeSlideId <= 0)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (!item.IsResolved &&
                    item.SlideId == activeSlideId &&
                    item.ContainerKind != PresentationContainerKind.SpeakerNotes &&
                    item.ContainerKind != PresentationContainerKind.SlideMasterShape)
                {
                    return true;
                }
            }
            return false;
        }

        public void SetVisualOverlaySuppressedForActivePresentation(bool suppressed)
        {
            string key = GetActivePresentationKey();
            if (string.IsNullOrEmpty(key))
                return;

            if (suppressed)
                _suppressedOverlayPresentations.Add(key);
            else
                _suppressedOverlayPresentations.Remove(key);

            NotifyVisualUnderlinesInvalidated();
        }

        public void ClearCachedPresentationState(string presentationKey)
        {
            if (string.IsNullOrEmpty(presentationKey))
                return;
            _issuesByPresentation.Remove(presentationKey);
            _qualityByPresentation.Remove(presentationKey);
            _scanMetaByPresentation.Remove(presentationKey);
            _suppressedOverlayPresentations.Remove(presentationKey);
            var staleKeys = _noProofSyncedTextByTarget.Keys
                .Where(k => k.StartsWith(presentationKey + "|", StringComparison.OrdinalIgnoreCase))
                .ToList();
            for (int i = 0; i < staleKeys.Count; i++)
                _noProofSyncedTextByTarget.Remove(staleKeys[i]);
            _undoManager.ClearForPresentation(presentationKey);
            NotifyVisualUnderlinesInvalidated();
        }

        private void OnLiveTypingPollTick(object? sender, EventArgs e)
        {
            if (_disposed || !IsLiveAutoScanEnabled || _suppressEventsDepth > 0 || IsScanning || IsSlideShowRunning)
                return;

            // Zero COM calls when PowerPoint is not the active foreground window
            if (!PowerPointSlideGeometryHelper.IsPowerPointWindowForegroundAndNormal(_pptApp, out _, out _))
                return;

            try
            {
                PowerPoint.DocumentWindow win = _pptApp.ActiveWindow;
                PowerPoint.Selection? sel = null;
                try { sel = win?.Selection; } catch { }
                if (sel == null || sel.Type != PowerPoint.PpSelectionType.ppSelectionText)
                {
                    _lastPolledActiveShapeKey = string.Empty;
                    _lastPolledActiveTextSnapshot = string.Empty;
                    return;
                }

                int slideId = (win?.View?.Slide is PowerPoint.Slide curSlide) ? curSlide.SlideID : 0;
                PowerPoint.ShapeRange? sr = null;
                try { sr = sel.HasChildShapeRange ? sel.ChildShapeRange : sel.ShapeRange; } catch { }
                if (sr == null || sr.Count < 1)
                    return;

                PowerPoint.Shape sh = sr[1];
                if (sh == null || sh.HasTextFrame != Office.MsoTriState.msoTrue)
                    return;

                PowerPoint.TextRange tr = sh.TextFrame.TextRange;
                string liveTxt = tr?.Text ?? string.Empty;
                string shapeKey = $"{GetActivePresentationKey()}|LIVE|{slideId}|{sh.Id}";

                if (!string.Equals(shapeKey, _lastPolledActiveShapeKey, StringComparison.Ordinal))
                {
                    _lastPolledActiveShapeKey = shapeKey;
                    _lastPolledActiveTextSnapshot = liveTxt;
                    if (tr != null && SpreadsheetProofingHelper.ContainsMyanmarScript(liveTxt))
                    {
                        SuppressNativeEnglishSpellcheckOnMyanmarRange(tr, string.Empty, liveTxt);
                    }
                    return;
                }

                if (!string.Equals(liveTxt, _lastPolledActiveTextSnapshot, StringComparison.Ordinal))
                {
                    _lastPolledActiveTextSnapshot = liveTxt;
                    if (tr != null && SpreadsheetProofingHelper.ContainsMyanmarScript(liveTxt))
                    {
                        SuppressNativeEnglishSpellcheckOnMyanmarRange(tr, string.Empty, liveTxt);
                    }
                    QueueLiveActiveSlideScan();
                }
            }
            catch
            {
                // Ignore transient COM states during slide editing
            }
        }

        private static bool IsMyanmarScriptCodePoint(char c) =>
            (c >= '\u1000' && c <= '\u109F') ||
            (c >= '\uAA60' && c <= '\uAA7F') ||
            (c >= '\uA9E0' && c <= '\uA9FF');

        /// <summary>
        /// Marks Myanmar script spans with <see cref="Office.MsoLanguageID.msoLanguageIDNoProofing"/> (<c>noProof="1"</c>)
        /// so PowerPoint's built-in English spellchecker never draws false red squiggles under valid Burmese words
        /// (e.g. "စားပြီးပြီ"), while keeping English text in the same slide/shape checked by PowerPoint's English dictionary.
        /// </summary>
        private void SuppressNativeEnglishSpellcheckOnMyanmarRange(PowerPoint.TextRange? tr, string targetKey, string rawText)
        {
            if (tr == null || string.IsNullOrEmpty(rawText) || !SpreadsheetProofingHelper.ContainsMyanmarScript(rawText))
                return;

            if (!string.IsNullOrEmpty(targetKey) &&
                _noProofSyncedTextByTarget.TryGetValue(targetKey, out string? prevSynced) &&
                string.Equals(prevSynced, rawText, StringComparison.Ordinal))
            {
                return;
            }

            _suppressEventsDepth++;
            try
            {
                if (!HasAsciiLetters(rawText))
                {
                    if (tr.LanguageID != Office.MsoLanguageID.msoLanguageIDNoProofing)
                    {
                        tr.LanguageID = Office.MsoLanguageID.msoLanguageIDNoProofing;
                    }
                }
                else
                {
                    int n = rawText.Length;
                    int i = 0;
                    while (i < n)
                    {
                        if (!IsMyanmarScriptCodePoint(rawText[i]))
                        {
                            i++;
                            continue;
                        }

                        int spanStart = i;
                        i++;
                        while (i < n)
                        {
                            char ch = rawText[i];
                            if (IsMyanmarScriptCodePoint(ch) || ch == '\u200B' || ch == '\u200C')
                            {
                                i++;
                                continue;
                            }
                            if (ch == ' ')
                            {
                                int peek = i + 1;
                                while (peek < n && rawText[peek] == ' ')
                                    peek++;
                                if (peek < n && IsMyanmarScriptCodePoint(rawText[peek]))
                                {
                                    i = peek;
                                    continue;
                                }
                            }
                            break;
                        }

                        int spanLen = i - spanStart;
                        PowerPoint.TextRange sub = tr.Characters(spanStart + 1, spanLen);
                        if (sub != null && sub.LanguageID != Office.MsoLanguageID.msoLanguageIDNoProofing)
                        {
                            sub.LanguageID = Office.MsoLanguageID.msoLanguageIDNoProofing;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(targetKey))
                {
                    if (_noProofSyncedTextByTarget.Count > 4096)
                        _noProofSyncedTextByTarget.Clear();
                    _noProofSyncedTextByTarget[targetKey] = rawText;
                }
            }
            catch
            {
                // Ignore locked/read-only placeholders
            }
            finally
            {
                _suppressEventsDepth--;
            }
        }

        public void QueueLiveActiveSlideScan()
        {
            if (_disposed || _suppressEventsDepth > 0 || !IsLiveAutoScanEnabled || IsSlideShowRunning)
                return;

            _deferredScope = PresentationScanScope.ActiveSlide;
            _deferredScanTimer.Stop();
            _deferredScanTimer.Start();
        }

        private void OnDeferredScanTimerTick(object? sender, EventArgs e)
        {
            _deferredScanTimer.Stop();
            if (_disposed || _suppressEventsDepth > 0 || !IsLiveAutoScanEnabled || IsSlideShowRunning)
                return;

            if (!IsPowerPointReadyForInterop(_pptApp))
                return;

            RunScan(_deferredScope, userInitiated: false);
        }

        public IReadOnlyList<PresentationSlideIssue> RunScan(PresentationScanScope scope, bool userInitiated = true)
        {
            if (_disposed || IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return Array.Empty<PresentationSlideIssue>();

            CancelActiveScan();
            CurrentScope = scope;

            string presKey = GetActivePresentationKey();
            if (string.IsNullOrEmpty(presKey))
                return Array.Empty<PresentationSlideIssue>();

            if (userInitiated)
                _suppressedOverlayPresentations.Remove(presKey);

            var cts = new CancellationTokenSource();
            _activeScanCts = cts;

            try
            {
                PowerPoint.Presentation pres = _pptApp.ActivePresentation;
                bool isLargeDeck = pres.Slides != null && pres.Slides.Count >= 40;
                EcoRuntimeBudgets budgets = EcoPerformanceGovernor.ComputeBudgets(isLargeDeck);
                if (budgets.IsEcoActive != _lastEcoActive)
                {
                    _lastEcoActive = budgets.IsEcoActive;
                    EcoActiveChanged?.Invoke(_lastEcoActive);
                }

                ScanStarted?.Invoke();
                ScanProgressChanged?.Invoke(10, UiLocalizer.Pick("Reading slide containers...", "ဆလိုက်စာသားများ ဖတ်ရှုနေသည်..."));

                List<PresentationTextTarget> targets = ExtractTargetsForScope(pres, presKey, scope, IncludeSlideMasters);

                ScanProgressChanged?.Invoke(55, UiLocalizer.Pick("Checking Myanmar spelling & style...", "မြန်မာစာလုံးပေါင်းနှင့် သဒ္ဒါ စစ်ဆေးနေသည်..."));

                bool checkConsistency = scope == PresentationScanScope.EntirePresentation;
                PresentationScanResult result = PresentationProofingHelper.ScanTargets(
                    _engine,
                    targets,
                    includeCrossSlideConsistency: checkConsistency,
                    cancellationToken: cts.Token);

                _issuesByPresentation[presKey] = result.Issues;
                _qualityByPresentation[presKey] = result.QualityReport;
                _scanMetaByPresentation[presKey] = new PowerPointScanOutcomeMetadata
                {
                    IsEcoActive = budgets.IsEcoActive,
                    WasCancelled = false,
                    ScannedContainerCount = result.ScannedContainerCount,
                    ScannedSlideCount = result.ScannedSlideCount,
                    IncludedSlideMasters = IncludeSlideMasters && scope == PresentationScanScope.EntirePresentation
                };

                ScanProgressChanged?.Invoke(100, UiLocalizer.Pick("Scan complete.", "စစ်ဆေးပြီးပါပြီ။"));
                ScanCompleted?.Invoke(result.Issues);
                NotifyVisualUnderlinesInvalidated();
                return result.Issues;
            }
            catch (OperationCanceledException)
            {
                return GetCachedIssuesForActivePresentation();
            }
            catch (COMException ex) when (IsTransientBusyComError(ex))
            {
                return GetCachedIssuesForActivePresentation();
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("RunScan", ex);
                return GetCachedIssuesForActivePresentation();
            }
            finally
            {
                if (ReferenceEquals(_activeScanCts, cts))
                    _activeScanCts = null;
                cts.Dispose();
            }
        }

        public int GetActiveSlideIdSafe()
        {
            try
            {
                if (!IsPowerPointReadyForInterop(_pptApp))
                    return 0;
                PowerPoint.DocumentWindow win = _pptApp.ActiveWindow;
                if (win == null || win.View == null)
                    return 0;
                if (win.View.Slide is PowerPoint.Slide slide)
                    return slide.SlideID;
            }
            catch { }
            return 0;
        }

        public List<PresentationTextTarget> ExtractTargetsForScope(
            PowerPoint.Presentation pres,
            string presKey,
            PresentationScanScope scope,
            bool includeMasters)
        {
            var targets = new List<PresentationTextTarget>(64);
            if (pres == null)
                return targets;

            try
            {
                if (scope == PresentationScanScope.Selection)
                {
                    PowerPoint.DocumentWindow? win = null;
                    try { win = _pptApp.ActiveWindow; } catch { }
                    PowerPoint.Selection? sel = null;
                    try { sel = win?.Selection; } catch { }

                    if (sel != null)
                    {
                        PowerPoint.PpSelectionType selType = PowerPoint.PpSelectionType.ppSelectionNone;
                        try { selType = sel.Type; } catch { }

                        if ((selType == PowerPoint.PpSelectionType.ppSelectionText ||
                             selType == PowerPoint.PpSelectionType.ppSelectionShapes) &&
                            win?.View?.Slide is PowerPoint.Slide curSlide)
                        {
                            string slideTitle = GetSlideTitleSafe(curSlide);
                            PowerPoint.ShapeRange? sr = null;
                            try { sr = sel.HasChildShapeRange ? sel.ChildShapeRange : sel.ShapeRange; } catch { }
                            if (sr != null && sr.Count > 0)
                            {
                                for (int i = 1; i <= sr.Count; i++)
                                {
                                    ExtractFromShapeRecursive(
                                        sr[i],
                                        presKey,
                                        curSlide.SlideID,
                                        curSlide.SlideIndex,
                                        slideTitle,
                                        PresentationContainerKind.SlideShape,
                                        string.Empty,
                                        Array.Empty<int>(),
                                        targets);
                                }
                                if (targets.Count > 0)
                                    return targets;
                            }
                        }
                        else if (selType == PowerPoint.PpSelectionType.ppSelectionSlides)
                        {
                            PowerPoint.SlideRange? slRange = null;
                            try { slRange = sel.SlideRange; } catch { }
                            if (slRange != null && slRange.Count > 0)
                            {
                                for (int s = 1; s <= slRange.Count; s++)
                                {
                                    ExtractAllFromSlide(slRange[s], presKey, targets);
                                }
                                return targets;
                            }
                        }
                    }

                    // Fallback to ActiveSlide when no explicit shape/slide selection is active
                    scope = PresentationScanScope.ActiveSlide;
                }

                if (scope == PresentationScanScope.ActiveSlide)
                {
                    if (_pptApp.ActiveWindow?.View?.Slide is PowerPoint.Slide activeSlide)
                    {
                        ExtractAllFromSlide(activeSlide, presKey, targets);
                    }
                    return targets;
                }

                // EntirePresentation: all slides + speaker notes (+ optional Slide Masters & Custom Layouts)
                int slideCount = pres.Slides != null ? pres.Slides.Count : 0;
                for (int s = 1; s <= slideCount; s++)
                {
                    ExtractAllFromSlide(pres.Slides![s], presKey, targets);
                }

                if (includeMasters)
                {
                    ExtractSlideMastersAndLayouts(pres, presKey, targets);
                }
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ExtractTargetsForScope", ex);
            }

            return targets;
        }

        private static string GetSlideTitleSafe(PowerPoint.Slide slide)
        {
            try
            {
                if (slide.Shapes.HasTitle == Office.MsoTriState.msoTrue &&
                    slide.Shapes.Title != null &&
                    slide.Shapes.Title.HasTextFrame == Office.MsoTriState.msoTrue &&
                    slide.Shapes.Title.TextFrame.HasText == Office.MsoTriState.msoTrue)
                {
                    string raw = slide.Shapes.Title.TextFrame.TextRange.Text ?? string.Empty;
                    return raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\v', ' ').Trim();
                }
            }
            catch { }
            return string.Empty;
        }

        private void ExtractAllFromSlide(
            PowerPoint.Slide slide,
            string presKey,
            List<PresentationTextTarget> targets)
        {
            if (slide == null)
                return;

            int slideId = slide.SlideID;
            int slideNumber = slide.SlideIndex;
            string slideTitle = GetSlideTitleSafe(slide);

            // 1. Visible slide shapes, placeholders, tables, and nested grouped shapes
            PowerPoint.Shapes shapes = slide.Shapes;
            int count = shapes != null ? shapes.Count : 0;
            for (int i = 1; i <= count; i++)
            {
                ExtractFromShapeRecursive(
                    shapes![i],
                    presKey,
                    slideId,
                    slideNumber,
                    slideTitle,
                    PresentationContainerKind.SlideShape,
                    string.Empty,
                    Array.Empty<int>(),
                    targets);
            }

            // 2. Speaker Notes (skip slide-number placeholders that hold only digits)
            try
            {
                if (slide.HasNotesPage == Office.MsoTriState.msoTrue && slide.NotesPage != null)
                {
                    PowerPoint.SlideRange notesRange = slide.NotesPage;
                    if (notesRange.Count >= 1)
                    {
                        PowerPoint.Shapes noteShapes = notesRange[1].Shapes;
                        int nCount = noteShapes != null ? noteShapes.Count : 0;
                        for (int n = 1; n <= nCount; n++)
                        {
                            PowerPoint.Shape ns = noteShapes![n];
                            if (ns.HasTextFrame == Office.MsoTriState.msoTrue &&
                                ns.TextFrame.HasText == Office.MsoTriState.msoTrue)
                            {
                                PowerPoint.TextRange noteTr = ns.TextFrame.TextRange;
                                string txt = noteTr.Text ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(txt))
                                {
                                    string fontName = GetPrimaryFontNameSafe(noteTr);
                                    var noteTarget = new PresentationTextTarget
                                    {
                                        PresentationName = presKey,
                                        SlideId = slideId,
                                        SlideNumber = slideNumber,
                                        SlideTitle = slideTitle,
                                        ContainerKind = PresentationContainerKind.SpeakerNotes,
                                        ShapeId = ns.Id,
                                        ShapeName = SafeGetShapeName(ns),
                                        TextSnapshot = txt,
                                        PrimaryFontName = fontName
                                    };
                                    SuppressNativeEnglishSpellcheckOnMyanmarRange(noteTr, noteTarget.BuildTargetKey(), txt);
                                    targets.Add(noteTarget);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void ExtractSlideMastersAndLayouts(
            PowerPoint.Presentation pres,
            string presKey,
            List<PresentationTextTarget> targets)
        {
            try
            {
                int dCount = pres.Designs != null ? pres.Designs.Count : 0;
                for (int d = 1; d <= dCount; d++)
                {
                    PowerPoint.Design design = pres.Designs![d];
                    PowerPoint.Master master = design.SlideMaster;
                    if (master != null)
                    {
                        string masterTag = $"Master:{design.Name}";
                        int mShapes = master.Shapes != null ? master.Shapes.Count : 0;
                        for (int i = 1; i <= mShapes; i++)
                        {
                            ExtractFromShapeRecursive(
                                master.Shapes![i],
                                presKey,
                                0,
                                0,
                                masterTag,
                                PresentationContainerKind.SlideMasterShape,
                                masterTag,
                                Array.Empty<int>(),
                                targets);
                        }

                        int lCount = master.CustomLayouts != null ? master.CustomLayouts.Count : 0;
                        for (int l = 1; l <= lCount; l++)
                        {
                            PowerPoint.CustomLayout layout = master.CustomLayouts![l];
                            string layoutTag = $"Layout:{design.Name}/{layout.Name}";
                            int lShapes = layout.Shapes != null ? layout.Shapes.Count : 0;
                            for (int s = 1; s <= lShapes; s++)
                            {
                                ExtractFromShapeRecursive(
                                    layout.Shapes![s],
                                    presKey,
                                    0,
                                    0,
                                    layoutTag,
                                    PresentationContainerKind.SlideMasterShape,
                                    layoutTag,
                                    Array.Empty<int>(),
                                    targets);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void ExtractFromShapeRecursive(
            PowerPoint.Shape shape,
            string presKey,
            int slideId,
            int slideNumber,
            string slideTitle,
            PresentationContainerKind baseKind,
            string masterOrLayoutName,
            int[] currentGroupPath,
            List<PresentationTextTarget> targets)
        {
            if (shape == null)
                return;

            try
            {
                // 1. Recursive Grouped Shapes (MsoShapeType.msoGroup)
                if (shape.Type == Office.MsoShapeType.msoGroup)
                {
                    PowerPoint.GroupShapes grp = shape.GroupItems;
                    int gCount = grp != null ? grp.Count : 0;
                    if (gCount > 0)
                    {
                        int[] nextPath = new int[currentGroupPath.Length + 1];
                        if (currentGroupPath.Length > 0)
                            Array.Copy(currentGroupPath, nextPath, currentGroupPath.Length);
                        nextPath[currentGroupPath.Length] = shape.Id;

                        // Note: PowerPoint GroupItems flattens nested leaf items in some versions, while others nest;
                        // guarding seen (ShapeId) inside the same group prevents duplicate extraction!
                        var seenInGroup = new HashSet<int>();
                        for (int g = 1; g <= gCount; g++)
                        {
                            PowerPoint.Shape child = grp![g];
                            if (child == null || !seenInGroup.Add(child.Id))
                                continue;

                            PresentationContainerKind childKind = baseKind == PresentationContainerKind.SlideMasterShape
                                ? PresentationContainerKind.SlideMasterShape
                                : PresentationContainerKind.GroupItemShape;

                            ExtractFromShapeRecursive(
                                child,
                                presKey,
                                slideId,
                                slideNumber,
                                slideTitle,
                                childKind,
                                masterOrLayoutName,
                                nextPath,
                                targets);
                        }
                    }
                    return;
                }

                // 2. Slide Tables (HasTable == msoTrue)
                if (shape.HasTable == Office.MsoTriState.msoTrue && baseKind != PresentationContainerKind.SlideMasterShape)
                {
                    PowerPoint.Table tbl = shape.Table;
                    int rows = tbl.Rows.Count;
                    int cols = tbl.Columns.Count;
                    string tblName = SafeGetShapeName(shape);

                    for (int r = 1; r <= rows; r++)
                    {
                        for (int c = 1; c <= cols; c++)
                        {
                            try
                            {
                                PowerPoint.Shape cellShape = tbl.Cell(r, c).Shape;
                                if (cellShape != null &&
                                    cellShape.HasTextFrame == Office.MsoTriState.msoTrue &&
                                    cellShape.TextFrame.HasText == Office.MsoTriState.msoTrue)
                                {
                                    PowerPoint.TextRange cellTr = cellShape.TextFrame.TextRange;
                                    string cellTxt = cellTr.Text ?? string.Empty;
                                    if (!string.IsNullOrWhiteSpace(cellTxt))
                                    {
                                        var cellTarget = new PresentationTextTarget
                                        {
                                            PresentationName = presKey,
                                            SlideId = slideId,
                                            SlideNumber = slideNumber,
                                            SlideTitle = slideTitle,
                                            ContainerKind = PresentationContainerKind.TableCell,
                                            ShapeId = shape.Id,
                                            ShapeName = tblName,
                                            GroupPathIds = currentGroupPath,
                                            TableRow = r,
                                            TableColumn = c,
                                            TextSnapshot = cellTxt,
                                            PrimaryFontName = GetPrimaryFontNameSafe(cellTr)
                                        };
                                        SuppressNativeEnglishSpellcheckOnMyanmarRange(cellTr, cellTarget.BuildTargetKey(), cellTxt);
                                        targets.Add(cellTarget);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    return;
                }

                // 3. Standard TextFrame (TextBox, Title, Subtitle, Body Bullet Placeholder, Callout, AutoShape)
                if (shape.HasTextFrame == Office.MsoTriState.msoTrue &&
                    shape.TextFrame.HasText == Office.MsoTriState.msoTrue)
                {
                    PowerPoint.TextRange shapeTr = shape.TextFrame.TextRange;
                    string txt = shapeTr.Text ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(txt))
                    {
                        var shapeTarget = new PresentationTextTarget
                        {
                            PresentationName = presKey,
                            SlideId = slideId,
                            SlideNumber = slideNumber,
                            SlideTitle = slideTitle,
                            ContainerKind = baseKind,
                            ShapeId = shape.Id,
                            ShapeName = SafeGetShapeName(shape),
                            GroupPathIds = currentGroupPath,
                            MasterOrLayoutName = masterOrLayoutName,
                            TextSnapshot = txt,
                            PrimaryFontName = GetPrimaryFontNameSafe(shapeTr)
                        };
                        SuppressNativeEnglishSpellcheckOnMyanmarRange(shapeTr, shapeTarget.BuildTargetKey(), txt);
                        targets.Add(shapeTarget);
                    }
                }
            }
            catch { }
        }

        private static string SafeGetShapeName(PowerPoint.Shape shape)
        {
            try { return shape.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string GetPrimaryFontNameSafe(PowerPoint.TextRange tr)
        {
            try
            {
                string f = tr.Font.NameOther;
                if (!string.IsNullOrWhiteSpace(f))
                    return f;
            }
            catch { }
            try
            {
                string f = tr.Font.Name;
                if (!string.IsNullOrWhiteSpace(f))
                    return f;
            }
            catch { }
            return string.Empty;
        }

        public bool TryResolveLiveTextRange(
            PresentationTextTarget target,
            out PowerPoint.Slide? resolvedSlide,
            out PowerPoint.Shape? resolvedShape,
            out PowerPoint.TextRange? resolvedRange)
        {
            resolvedSlide = null;
            resolvedShape = null;
            resolvedRange = null;

            if (target == null || !IsPowerPointReadyForInterop(_pptApp))
                return false;

            try
            {
                PowerPoint.Presentation pres = _pptApp.ActivePresentation;
                if (pres == null)
                    return false;

                if (target.ContainerKind == PresentationContainerKind.SlideMasterShape)
                {
                    return TryResolveMasterTextRange(pres, target, out resolvedShape, out resolvedRange);
                }

                PowerPoint.Slide? slide = null;
                try { slide = pres.Slides.FindBySlideID(target.SlideId); } catch { }
                if (slide == null && target.SlideNumber >= 1 && target.SlideNumber <= pres.Slides.Count)
                {
                    try { slide = pres.Slides[target.SlideNumber]; } catch { }
                }
                if (slide == null)
                    return false;

                resolvedSlide = slide;

                if (target.ContainerKind == PresentationContainerKind.SpeakerNotes)
                {
                    if (slide.HasNotesPage == Office.MsoTriState.msoTrue && slide.NotesPage.Count >= 1)
                    {
                        PowerPoint.Shape? ns = FindShapeById(slide.NotesPage[1].Shapes, target.ShapeId);
                        if (ns != null && ns.HasTextFrame == Office.MsoTriState.msoTrue)
                        {
                            resolvedShape = ns;
                            resolvedRange = ns.TextFrame.TextRange;
                            return resolvedRange != null;
                        }
                    }
                    return false;
                }

                PowerPoint.Shape? rootShape = FindShapeInsideOptionalGroups(slide.Shapes, target.GroupPathIds, target.ShapeId);
                if (rootShape == null)
                    return false;

                if (target.ContainerKind == PresentationContainerKind.TableCell)
                {
                    if (rootShape.HasTable == Office.MsoTriState.msoTrue &&
                        target.TableRow >= 1 && target.TableRow <= rootShape.Table.Rows.Count &&
                        target.TableColumn >= 1 && target.TableColumn <= rootShape.Table.Columns.Count)
                    {
                        PowerPoint.Shape cellShape = rootShape.Table.Cell(target.TableRow, target.TableColumn).Shape;
                        if (cellShape != null && cellShape.HasTextFrame == Office.MsoTriState.msoTrue)
                        {
                            resolvedShape = cellShape;
                            resolvedRange = cellShape.TextFrame.TextRange;
                            return resolvedRange != null;
                        }
                    }
                    return false;
                }

                if (rootShape.HasTextFrame == Office.MsoTriState.msoTrue)
                {
                    resolvedShape = rootShape;
                    resolvedRange = rootShape.TextFrame.TextRange;
                    return resolvedRange != null;
                }
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("TryResolveLiveTextRange", ex);
            }

            return false;
        }

        private static bool TryResolveMasterTextRange(
            PowerPoint.Presentation pres,
            PresentationTextTarget target,
            out PowerPoint.Shape? resolvedShape,
            out PowerPoint.TextRange? resolvedRange)
        {
            resolvedShape = null;
            resolvedRange = null;
            try
            {
                int dCount = pres.Designs != null ? pres.Designs.Count : 0;
                for (int d = 1; d <= dCount; d++)
                {
                    PowerPoint.Design design = pres.Designs![d];
                    PowerPoint.Master master = design.SlideMaster;
                    if (master == null)
                        continue;

                    if (string.Equals(target.MasterOrLayoutName, $"Master:{design.Name}", StringComparison.Ordinal))
                    {
                        PowerPoint.Shape? ms = FindShapeInsideOptionalGroups(master.Shapes, target.GroupPathIds, target.ShapeId);
                        if (ms != null && ms.HasTextFrame == Office.MsoTriState.msoTrue)
                        {
                            resolvedShape = ms;
                            resolvedRange = ms.TextFrame.TextRange;
                            return resolvedRange != null;
                        }
                    }

                    int lCount = master.CustomLayouts != null ? master.CustomLayouts.Count : 0;
                    for (int l = 1; l <= lCount; l++)
                    {
                        PowerPoint.CustomLayout layout = master.CustomLayouts![l];
                        if (string.Equals(target.MasterOrLayoutName, $"Layout:{design.Name}/{layout.Name}", StringComparison.Ordinal))
                        {
                            PowerPoint.Shape? ls = FindShapeInsideOptionalGroups(layout.Shapes, target.GroupPathIds, target.ShapeId);
                            if (ls != null && ls.HasTextFrame == Office.MsoTriState.msoTrue)
                            {
                                resolvedShape = ls;
                                resolvedRange = ls.TextFrame.TextRange;
                                return resolvedRange != null;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static PowerPoint.Shape? FindShapeById(PowerPoint.Shapes? shapes, int shapeId)
        {
            if (shapes == null)
                return null;
            int count = shapes.Count;
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    PowerPoint.Shape s = shapes[i];
                    if (s != null && s.Id == shapeId)
                        return s;
                }
                catch { }
            }
            return null;
        }

        private static PowerPoint.Shape? FindShapeInsideOptionalGroups(PowerPoint.Shapes? rootShapes, int[]? groupPathIds, int leafShapeId)
        {
            if (rootShapes == null)
                return null;

            if (groupPathIds == null || groupPathIds.Length == 0)
                return FindShapeById(rootShapes, leafShapeId);

            PowerPoint.Shape? curGroup = FindShapeById(rootShapes, groupPathIds[0]);
            for (int p = 1; p < groupPathIds.Length && curGroup != null; p++)
            {
                curGroup = FindGroupChildById(curGroup, groupPathIds[p]);
            }

            if (curGroup != null)
            {
                PowerPoint.Shape? found = FindGroupChildById(curGroup, leafShapeId);
                if (found != null)
                    return found;
            }

            // Fallback recursive search across all groups on the slide
            int rootCount = rootShapes.Count;
            for (int i = 1; i <= rootCount; i++)
            {
                try
                {
                    PowerPoint.Shape s = rootShapes[i];
                    if (s != null && s.Type == Office.MsoShapeType.msoGroup)
                    {
                        PowerPoint.Shape? deep = FindGroupChildById(s, leafShapeId);
                        if (deep != null)
                            return deep;
                    }
                }
                catch { }
            }
            return null;
        }

        private static PowerPoint.Shape? FindGroupChildById(PowerPoint.Shape groupShape, int targetId)
        {
            try
            {
                PowerPoint.GroupShapes items = groupShape.GroupItems;
                int count = items != null ? items.Count : 0;
                for (int i = 1; i <= count; i++)
                {
                    PowerPoint.Shape item = items![i];
                    if (item == null)
                        continue;
                    if (item.Id == targetId)
                        return item;
                    if (item.Type == Office.MsoShapeType.msoGroup)
                    {
                        PowerPoint.Shape? nested = FindGroupChildById(item, targetId);
                        if (nested != null)
                            return nested;
                    }
                }
            }
            catch { }
            return null;
        }

        public bool NavigateToIssue(PresentationSlideIssue item)
        {
            if (item == null || IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return false;

            _suppressEventsDepth++;
            try
            {
                if (!TryResolveLiveTextRange(item.Target, out var slide, out _, out var tr) || tr == null)
                    return false;

                PowerPoint.DocumentWindow win = _pptApp.ActiveWindow;
                if (win != null && slide != null)
                {
                    try
                    {
                        if (item.ContainerKind == PresentationContainerKind.SpeakerNotes)
                        {
                            if (win.ViewType != PowerPoint.PpViewType.ppViewNormal &&
                                win.ViewType != PowerPoint.PpViewType.ppViewNotesPage)
                            {
                                win.ViewType = PowerPoint.PpViewType.ppViewNormal;
                            }
                            win.View.GotoSlide(slide.SlideIndex);
                            // Activate Notes pane in Normal view if present (Pane 3)
                            if (win.ViewType == PowerPoint.PpViewType.ppViewNormal && win.Panes != null && win.Panes.Count >= 3)
                            {
                                try { win.Panes[3].Activate(); } catch { }
                            }
                        }
                        else
                        {
                            if (win.ViewType != PowerPoint.PpViewType.ppViewNormal &&
                                win.ViewType != PowerPoint.PpViewType.ppViewSlide)
                            {
                                win.ViewType = PowerPoint.PpViewType.ppViewNormal;
                            }
                            win.View.GotoSlide(slide.SlideIndex);
                            if (win.ViewType == PowerPoint.PpViewType.ppViewNormal && win.Panes != null && win.Panes.Count >= 2)
                            {
                                try { win.Panes[2].Activate(); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                string liveText = tr.Text ?? string.Empty;
                int startZero = item.Issue.StartIndex;
                int len = Math.Max(1, item.Issue.Length);
                if (startZero < 0 || startZero + len > liveText.Length)
                {
                    int fallbackIdx = !string.IsNullOrEmpty(item.Issue.OriginalText)
                        ? liveText.IndexOf(item.Issue.OriginalText, StringComparison.Ordinal)
                        : -1;
                    if (fallbackIdx >= 0)
                    {
                        startZero = fallbackIdx;
                        len = item.Issue.OriginalText.Length;
                    }
                    else
                    {
                        startZero = 0;
                        len = Math.Min(Math.Max(1, liveText.Length), 1);
                    }
                }

                if (liveText.Length > 0)
                {
                    try { tr.Characters(startZero + 1, len).Select(); } catch { }
                }

                NotifyVisualUnderlinesInvalidated();
                return true;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("NavigateToIssue", ex);
                return false;
            }
            finally
            {
                _suppressEventsDepth--;
            }
        }

        public bool ApplySingleSuggestion(PresentationSlideIssue item, string replacement)
        {
            if (item == null || item.IsResolved || replacement == null || IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return false;

            _suppressEventsDepth++;
            try
            {
                if (!TryResolveLiveTextRange(item.Target, out _, out _, out var tr) || tr == null)
                    return false;

                string liveText = tr.Text ?? string.Empty;
                int startZero = item.Issue.StartIndex;
                int oldLen = item.Issue.Length;

                if (startZero < 0 || oldLen <= 0 || startZero + oldLen > liveText.Length ||
                    !string.Equals(liveText.Substring(startZero, oldLen), item.Issue.OriginalText, StringComparison.Ordinal))
                {
                    int found = !string.IsNullOrEmpty(item.Issue.OriginalText)
                        ? liveText.IndexOf(item.Issue.OriginalText, Math.Max(0, Math.Min(startZero, liveText.Length)), StringComparison.Ordinal)
                        : -1;
                    if (found < 0 && !string.IsNullOrEmpty(item.Issue.OriginalText))
                        found = liveText.IndexOf(item.Issue.OriginalText, StringComparison.Ordinal);
                    if (found < 0)
                        return false;
                    startZero = found;
                    oldLen = item.Issue.OriginalText.Length;
                }

                try { _pptApp.StartNewUndoEntry(); } catch { }

                // Surgical 1-based character replacement preserves bold, italic, size, RGB color, hyperlinks & animations!
                PowerPoint.TextRange editedChars = tr.Characters(startZero + 1, oldLen);
                editedChars.Text = replacement;
                try
                {
                    if (replacement.Length > 0 && SpreadsheetProofingHelper.ContainsMyanmarScript(replacement))
                        tr.Characters(startZero + 1, replacement.Length).LanguageID = Office.MsoLanguageID.msoLanguageIDNoProofing;
                }
                catch { }
                string updatedText = tr.Text ?? (liveText.Substring(0, startZero) + replacement + liveText.Substring(startZero + oldLen));

                var writeOp = new SlideTextWriteOperation
                {
                    Target = item.Target.CloneWithText(liveText),
                    PreviousText = liveText,
                    UpdatedText = updatedText,
                    ChangesInContainer = 1,
                    DescendingSpanEdits = new[] { new PresentationSpanEdit(startZero, oldLen, replacement) }
                };
                _undoManager.PushOperations(
                    UiLocalizer.Pick($"Fix '{item.Issue.OriginalText}' -> '{replacement}'", $"'{item.Issue.OriginalText}' -> '{replacement}' ပြင်ဆင်ချက်"),
                    new[] { writeOp });

                string presKey = GetActivePresentationKey();
                if (!string.IsNullOrEmpty(presKey) && _issuesByPresentation.TryGetValue(presKey, out var list))
                {
                    PresentationProofingHelper.ShiftSiblingIssueOffsetsAfterEdit(
                        list,
                        item.Target,
                        startZero,
                        oldLen,
                        replacement.Length,
                        updatedText);
                }
                else
                {
                    item.IsResolved = true;
                }

                UndoStateChanged?.Invoke();
                NotifyVisualUnderlinesInvalidated();
                return true;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ApplySingleSuggestion", ex);
                return false;
            }
            finally
            {
                _suppressEventsDepth--;
            }
        }

        public int ApplyBatchSuggestionAcrossPresentation(string originalWord, string replacement)
        {
            var list = GetCachedIssuesForActivePresentation();
            if (list.Count == 0 || string.IsNullOrEmpty(originalWord) || replacement == null)
                return 0;

            var dict = new Dictionary<PresentationSlideIssue, string>();
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (!item.IsResolved && string.Equals(item.Issue.OriginalText, originalWord, StringComparison.Ordinal))
                {
                    dict[item] = replacement;
                }
            }

            return ApplyBatchSelectedFixes(dict);
        }

        public int ApplyBatchSelectedFixes(IReadOnlyDictionary<PresentationSlideIssue, string> selectedFixes)
        {
            if (selectedFixes == null || selectedFixes.Count == 0 || IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return 0;

            List<SlideTextWriteOperation> ops = PresentationProofingHelper.PlanBatchIssueFixes(selectedFixes);
            if (ops.Count == 0)
                return 0;

            int appliedSpans = ExecuteSlideWriteOperations(
                ops,
                UiLocalizer.Pick($"Batch Fix ({ops.Sum(o => o.ChangesInContainer)} issues)", $"အစုလိုက် ပြင်ဆင်ချက် ({ops.Sum(o => o.ChangesInContainer)} ချက်)"));

            if (appliedSpans > 0)
            {
                foreach (var kv in selectedFixes)
                {
                    if (kv.Key != null)
                        kv.Key.IsResolved = true;
                }
                RunScan(CurrentScope, userInitiated: false);
            }

            return appliedSpans;
        }

        public bool UndoLastAction(out string restoredSummary)
        {
            restoredSummary = string.Empty;
            if (!_undoManager.CanUndo || IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return false;

            PresentationUndoBatch? batch = _undoManager.PopUndoBatch();
            if (batch == null || batch.Entries.Count == 0)
                return false;

            _suppressEventsDepth++;
            int restoredContainers = 0;
            try
            {
                try { _pptApp.StartNewUndoEntry(); } catch { }

                for (int i = batch.Entries.Count - 1; i >= 0; i--)
                {
                    SlideTextUndoEntry entry = batch.Entries[i];
                    if (TryResolveLiveTextRange(entry.Target, out _, out _, out var tr) && tr != null)
                    {
                        tr.Text = entry.PreviousText;
                        if (!string.IsNullOrWhiteSpace(entry.PreviousFontName) &&
                            !string.Equals(entry.PreviousFontName, entry.NewFontName, StringComparison.OrdinalIgnoreCase))
                        {
                            try { tr.Font.Name = entry.PreviousFontName; } catch { }
                            try { tr.Font.NameOther = entry.PreviousFontName; } catch { }
                        }
                        restoredContainers++;
                    }
                }
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("UndoLastAction", ex);
            }
            finally
            {
                _suppressEventsDepth--;
            }

            UndoStateChanged?.Invoke();
            if (restoredContainers > 0)
            {
                restoredSummary = batch.Description;
                RunScan(CurrentScope, userInitiated: false);
                return true;
            }
            return false;
        }

        public PowerPointToolkitOutcome ConvertLegacyFontsAndUnifyPyidaungsu(bool selectionOnly, bool convertLegacyEncoding)
        {
            var outcome = new PowerPointToolkitOutcome();
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return outcome;

            string presKey = GetActivePresentationKey();
            PowerPoint.Presentation pres = _pptApp.ActivePresentation;
            var scope = selectionOnly ? PresentationScanScope.Selection : PresentationScanScope.EntirePresentation;
            List<PresentationTextTarget> targets = ExtractTargetsForScope(pres, presKey, scope, IncludeSlideMasters);

            _suppressEventsDepth++;
            var recordedOps = new List<SlideTextWriteOperation>();
            int modifiedContainers = 0;
            int totalRunsUpdated = 0;

            try
            {
                try { _pptApp.StartNewUndoEntry(); } catch { }

                for (int i = 0; i < targets.Count; i++)
                {
                    var target = targets[i];
                    if (!TryResolveLiveTextRange(target, out _, out _, out var tr) || tr == null)
                        continue;

                    string beforeText = tr.Text ?? string.Empty;
                    string beforeFont = GetPrimaryFontNameSafe(tr);
                    bool containerTouched = false;

                    // Walk runs in reverse (1-based) so run text length changes never shift earlier runs
                    PowerPoint.TextRange runsCollection = tr.Runs();
                    int runCount = runsCollection != null ? runsCollection.Count : 0;
                    for (int r = runCount; r >= 1; r--)
                    {
                        PowerPoint.TextRange run = tr.Runs(r, 1);
                        if (run == null)
                            continue;

                        string runText = run.Text ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(runText))
                            continue;

                        string runFont = GetPrimaryFontNameSafe(run);
                        string finalRunText = runText;
                        bool convertedText = false;

                        if (convertLegacyEncoding)
                        {
                            finalRunText = LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu(runText, runFont, out convertedText);
                            if (convertedText)
                            {
                                run.Text = finalRunText;
                                containerTouched = true;
                                totalRunsUpdated++;
                            }
                        }

                        if (convertedText || SpreadsheetProofingHelper.ContainsMyanmarScript(finalRunText))
                        {
                            bool changedFont = false;
                            try
                            {
                                if (!string.Equals(run.Font.NameOther, "Pyidaungsu", StringComparison.OrdinalIgnoreCase))
                                {
                                    run.Font.NameOther = "Pyidaungsu";
                                    changedFont = true;
                                }
                            }
                            catch { }
                            try
                            {
                                // Only set Font.Name to Pyidaungsu if the run has no ASCII Latin letters, preserving mixed English brand fonts
                                if (!HasAsciiLetters(finalRunText) &&
                                    !string.Equals(run.Font.Name, "Pyidaungsu", StringComparison.OrdinalIgnoreCase))
                                {
                                    run.Font.Name = "Pyidaungsu";
                                    changedFont = true;
                                }
                            }
                            catch { }
                            try
                            {
                                dynamic dynFont = run.Font;
                                dynFont.NameComplexScript = "Pyidaungsu";
                            }
                            catch { }

                            if (changedFont && !convertedText)
                            {
                                containerTouched = true;
                                totalRunsUpdated++;
                            }
                        }
                    }

                    if (containerTouched)
                    {
                        modifiedContainers++;
                        string afterText = tr.Text ?? beforeText;
                        recordedOps.Add(new SlideTextWriteOperation
                        {
                            Target = target,
                            PreviousText = beforeText,
                            UpdatedText = afterText,
                            ChangesInContainer = 1,
                            PreviousFontName = beforeFont,
                            TargetFontName = "Pyidaungsu"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ConvertLegacyFontsAndUnifyPyidaungsu", ex);
            }
            finally
            {
                _suppressEventsDepth--;
            }

            if (recordedOps.Count > 0)
            {
                _undoManager.PushOperations(
                    convertLegacyEncoding
                        ? UiLocalizer.Pick("Convert Zawgyi/WinInnwa -> Pyidaungsu Unicode", "ဇော်ဂျီ/WinInnwa -> Pyidaungsu Unicode ပြောင်းလဲချက်")
                        : UiLocalizer.Pick("Unify Myanmar Slide Fonts -> Pyidaungsu", "မြန်မာဆလိုက်ဖောင့် Pyidaungsu တစ်ပြေးညီညှိချက်"),
                    recordedOps);
                UndoStateChanged?.Invoke();
                RunScan(CurrentScope, userInitiated: false);
            }

            outcome.Success = true;
            outcome.ContainersModified = modifiedContainers;
            outcome.TotalChanges = totalRunsUpdated;
            outcome.UserMessage = modifiedContainers > 0
                ? UiLocalizer.Pick(
                    $"Updated {modifiedContainers} slide container(s) ({totalRunsUpdated} text run(s)) to Pyidaungsu Unicode.",
                    $"ဆလိုက်စာသားကွက် {modifiedContainers} ခု ({totalRunsUpdated} ပိုင်း) ကို Pyidaungsu Unicode သို့ ပြောင်းလဲပြီးပါပြီ။")
                : UiLocalizer.Pick(
                    "All Myanmar slide runs already use clean Unicode & Pyidaungsu.",
                    "မြန်မာစာသားအားလုံး စံ Unicode နှင့် Pyidaungsu ဖြစ်ပြီးသား ဖြစ်ပါသည်။");
            return outcome;
        }

        public PowerPointToolkitOutcome ApplySmartSlideLineWrapZwsp(bool selectionOnly, bool stripAllInsteadOfInject)
        {
            var outcome = new PowerPointToolkitOutcome();
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return outcome;

            string presKey = GetActivePresentationKey();
            var scope = selectionOnly ? PresentationScanScope.Selection : PresentationScanScope.EntirePresentation;
            var targets = ExtractTargetsForScope(_pptApp.ActivePresentation, presKey, scope, IncludeSlideMasters);

            var ops = PresentationProofingHelper.PlanSmartSlideZwsp(targets, _engine.Trie, stripAllInsteadOfInject, out int zwDelta);
            int applied = ExecuteSlideWriteOperations(
                ops,
                stripAllInsteadOfInject
                    ? UiLocalizer.Pick("Strip Zero-Width Characters", "Zero-Width (ZWSP) ရှင်းလင်းချက်")
                    : UiLocalizer.Pick("Smart Slide Word-Boundary ZWSP", "ဆလိုက်စာကြောင်းဆင်း Smart-ZWSP ထည့်သွင်းချက်"));

            if (applied > 0)
                RunScan(CurrentScope, userInitiated: false);

            outcome.Success = true;
            outcome.ContainersModified = ops.Count;
            outcome.TotalChanges = zwDelta;
            outcome.UserMessage = ops.Count > 0
                ? (stripAllInsteadOfInject
                    ? UiLocalizer.Pick($"Removed {zwDelta} zero-width char(s) across {ops.Count} slide box(es).", $"ဆလိုက်ကွက် {ops.Count} ခုမှ ZWSP {zwDelta} လုံး ဖယ်ရှားပြီးပါပြီ။")
                    : UiLocalizer.Pick($"Applied Smart Word-Boundary ZWSP across {ops.Count} slide box(es) ({zwDelta} boundary points).", $"ဆလိုက်ကွက် {ops.Count} ခုတွင် စာလုံးမပြတ်စေသော Smart-ZWSP ({zwDelta} နေရာ) ထည့်သွင်းပြီးပါပြီ။"))
                : UiLocalizer.Pick("No ZWSP line-wrap changes needed.", "ပြောင်းလဲရန် မလိုအပ်ပါ။");
            return outcome;
        }

        public PowerPointToolkitOutcome ApplyOfficialSlideTypography(bool selectionOnly, bool convertDigitsToMyanmar)
        {
            var outcome = new PowerPointToolkitOutcome();
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return outcome;

            string presKey = GetActivePresentationKey();
            var scope = selectionOnly ? PresentationScanScope.Selection : PresentationScanScope.EntirePresentation;
            var targets = ExtractTargetsForScope(_pptApp.ActivePresentation, presKey, scope, IncludeSlideMasters);

            var ops = PresentationProofingHelper.PlanOfficialSlideTypography(targets, convertDigitsToMyanmar, out int totalChanges);
            int applied = ExecuteSlideWriteOperations(
                ops,
                UiLocalizer.Pick("Official Slide Punctuation & Typography", "ဆလိုက်ပုဒ်ဖြတ်ပုဒ်ရပ် စံညှိချက်"));

            if (applied > 0)
                RunScan(CurrentScope, userInitiated: false);

            outcome.Success = true;
            outcome.ContainersModified = ops.Count;
            outcome.TotalChanges = totalChanges;
            outcome.UserMessage = ops.Count > 0
                ? UiLocalizer.Pick($"Formatted {totalChanges} typography item(s) across {ops.Count} slide box(es).", $"ဆလိုက်ကွက် {ops.Count} ခုတွင် ပုဒ်ဖြတ်ပုဒ်ရပ် {totalChanges} ချက် စံညှိပြီးပါပြီ။")
                : UiLocalizer.Pick("Slide punctuation and spacing are already clean.", "ဆလိုက် ပုဒ်ဖြတ်ပုဒ်ရပ်များ စနစ်တကျ ရှိပြီးဖြစ်ပါသည်။");
            return outcome;
        }

        public PowerPointToolkitOutcome ApplySlideNumeralStyle(bool selectionOnly, bool toMyanmarDigits)
        {
            var outcome = new PowerPointToolkitOutcome();
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return outcome;

            string presKey = GetActivePresentationKey();
            var scope = selectionOnly ? PresentationScanScope.Selection : PresentationScanScope.EntirePresentation;
            var targets = ExtractTargetsForScope(_pptApp.ActivePresentation, presKey, scope, IncludeSlideMasters);

            var ops = PresentationProofingHelper.PlanNumeralConversion(targets, toMyanmarDigits, out int converted);
            int applied = ExecuteSlideWriteOperations(
                ops,
                toMyanmarDigits
                    ? UiLocalizer.Pick("Convert Numerals (0-9 -> ၀-၉)", "မြန်မာဂဏန်း (၀-၉) ပြောင်းလဲချက်")
                    : UiLocalizer.Pick("Convert Numerals (၀-၉ -> 0-9)", "အင်္ဂလိပ်ဂဏန်း (0-9) ပြောင်းလဲချက်"));

            if (applied > 0)
                RunScan(CurrentScope, userInitiated: false);

            outcome.Success = true;
            outcome.ContainersModified = ops.Count;
            outcome.TotalChanges = converted;
            outcome.UserMessage = ops.Count > 0
                ? UiLocalizer.Pick($"Converted {converted} digit(s) across {ops.Count} slide box(es).", $"ဆလိုက်ကွက် {ops.Count} ခုတွင် ဂဏန်း {converted} လုံး ပြောင်းလဲပြီးပါပြီ။")
                : UiLocalizer.Pick("No convertible digits found in scope.", "ပြောင်းလဲရန် ဂဏန်း မတွေ့ရှိပါ။");
            return outcome;
        }

        public bool InsertTextIntoActiveSlideSelection(string textToInsert)
        {
            if (string.IsNullOrEmpty(textToInsert) || IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return false;

            _suppressEventsDepth++;
            try
            {
                PowerPoint.Selection sel = _pptApp.ActiveWindow.Selection;
                if (sel != null && sel.Type == PowerPoint.PpSelectionType.ppSelectionText && sel.TextRange != null)
                {
                    try { _pptApp.StartNewUndoEntry(); } catch { }
                    sel.TextRange.Text = textToInsert;
                    return true;
                }
            }
            catch { }
            finally
            {
                _suppressEventsDepth--;
            }

            try
            {
                Clipboard.SetText(textToInsert, TextDataFormat.UnicodeText);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int ExecuteSlideWriteOperations(List<SlideTextWriteOperation> ops, string undoDescription)
        {
            if (ops == null || ops.Count == 0)
                return 0;

            _suppressEventsDepth++;
            var succeededOps = new List<SlideTextWriteOperation>(ops.Count);
            int totalApplied = 0;

            try
            {
                try { _pptApp.StartNewUndoEntry(); } catch { }

                for (int i = 0; i < ops.Count; i++)
                {
                    SlideTextWriteOperation op = ops[i];
                    if (!TryResolveLiveTextRange(op.Target, out _, out _, out var tr) || tr == null)
                        continue;

                    string liveBefore = tr.Text ?? string.Empty;
                    if (op.DescendingSpanEdits != null &&
                        op.DescendingSpanEdits.Count > 0 &&
                        !(op.DescendingSpanEdits.Count == 1 &&
                          op.DescendingSpanEdits[0].StartZeroBased == 0 &&
                          op.DescendingSpanEdits[0].Length == op.PreviousText.Length))
                    {
                        // Surgical 1-based range edits in strictly descending offset order preserve run formatting!
                        bool allValid = string.Equals(liveBefore, op.PreviousText, StringComparison.Ordinal);
                        if (allValid)
                        {
                            for (int e = 0; e < op.DescendingSpanEdits.Count; e++)
                            {
                                PresentationSpanEdit ed = op.DescendingSpanEdits[e];
                                tr.Characters(ed.StartZeroBased + 1, ed.Length).Text = ed.Replacement;
                                totalApplied++;
                            }
                        }
                        else
                        {
                            tr.Text = op.UpdatedText;
                            totalApplied += op.ChangesInContainer;
                        }
                    }
                    else
                    {
                        // Whole-container text transform: if single uniform font run, preserve character run formatting by replacing Characters(1, len)
                        if (liveBefore.Length > 0)
                            tr.Characters(1, liveBefore.Length).Text = op.UpdatedText;
                        else
                            tr.Text = op.UpdatedText;
                        totalApplied += Math.Max(1, op.ChangesInContainer);
                    }

                    if (!string.IsNullOrWhiteSpace(op.TargetFontName))
                    {
                        try { tr.Font.NameOther = op.TargetFontName; } catch { }
                        if (!HasAsciiLetters(op.UpdatedText))
                        {
                            try { tr.Font.Name = op.TargetFontName; } catch { }
                        }
                    }

                    SuppressNativeEnglishSpellcheckOnMyanmarRange(tr, op.Target.BuildTargetKey(), op.UpdatedText);
                    succeededOps.Add(op);
                }
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ExecuteSlideWriteOperations", ex);
            }
            finally
            {
                _suppressEventsDepth--;
            }

            if (succeededOps.Count > 0)
            {
                _undoManager.PushOperations(undoDescription, succeededOps);
                UndoStateChanged?.Invoke();
                NotifyVisualUnderlinesInvalidated();
            }

            return totalApplied;
        }

        public void RefreshEcoActiveState()
        {
            bool isLargeDeck = false;
            try
            {
                if (IsPowerPointReadyForInterop(_pptApp) && _pptApp.ActivePresentation?.Slides != null)
                {
                    isLargeDeck = _pptApp.ActivePresentation.Slides.Count >= 40;
                }
            }
            catch { }

            bool nextEco = EcoPerformanceGovernor.ComputeBudgets(isLargeDeck).IsEcoActive;
            if (nextEco != _lastEcoActive)
            {
                _lastEcoActive = nextEco;
                EcoActiveChanged?.Invoke(_lastEcoActive);
            }
        }

        public int ClearAllVisualUnderlinesInActivePresentation()
        {
            var issues = GetCachedIssuesForActivePresentation();
            int count = issues.Count(i => !i.IsResolved);
            SetVisualOverlaySuppressedForActivePresentation(true);
            return count;
        }

        public DocumentQualityReport BuildActivePresentationQualityReport(bool forceRefresh = false)
        {
            string presKey = GetActivePresentationKey();
            if (!forceRefresh && !string.IsNullOrEmpty(presKey) && _qualityByPresentation.TryGetValue(presKey, out var cached))
                return cached;

            if (!IsPowerPointReadyForInterop(_pptApp))
                return new DocumentQualityReport();

            try
            {
                var targets = ExtractTargetsForScope(_pptApp.ActivePresentation, presKey, PresentationScanScope.EntirePresentation, IncludeSlideMasters);
                var sb = new System.Text.StringBuilder(targets.Count * 64);
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(targets[i].TextSnapshot))
                    {
                        if (sb.Length > 0)
                            sb.Append('\n');
                        sb.Append(PresentationProofingHelper.NormalizePowerPointBreaksPreservingLength(targets[i].TextSnapshot));
                    }
                }

                IReadOnlyList<ProofingIssue>? unresolved = null;
                if (!string.IsNullOrEmpty(presKey) && _issuesByPresentation.TryGetValue(presKey, out var list))
                {
                    unresolved = list.Where(x => !x.IsResolved).Select(x => x.Issue).ToList();
                }

                DocumentQualityReport report = DocumentQualityAnalyzer.Analyze(sb.ToString(), unresolved, _engine);
                if (!string.IsNullOrEmpty(presKey))
                    _qualityByPresentation[presKey] = report;
                return report;
            }
            catch
            {
                return new DocumentQualityReport();
            }
        }

        public PowerPointToolkitOutcome RepairBrokenPdfInScope(bool selectionOnly)
        {
            var outcome = new PowerPointToolkitOutcome();
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return outcome;

            string presKey = GetActivePresentationKey();
            var scope = selectionOnly ? PresentationScanScope.Selection : PresentationScanScope.EntirePresentation;
            var targets = ExtractTargetsForScope(_pptApp.ActivePresentation, presKey, scope, IncludeSlideMasters);

            var ops = new List<SlideTextWriteOperation>();
            int totalRepaired = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                string raw = t.TextSnapshot ?? string.Empty;
                if (!SpreadsheetProofingHelper.ContainsMyanmarScript(raw) || !UnicodeNormalizer.IsBrokenPdfFontCMapText(raw))
                    continue;

                string norm = PresentationProofingHelper.NormalizePowerPointBreaksPreservingLength(raw);
                string repaired = BrokenPdfCMapReconstructor.Reconstruct(norm, _engine.Trie, out int lines);
                if (lines > 0 && !string.Equals(repaired, norm, StringComparison.Ordinal))
                {
                    totalRepaired += lines;
                    ops.Add(new SlideTextWriteOperation
                    {
                        Target = t,
                        PreviousText = raw,
                        UpdatedText = repaired,
                        ChangesInContainer = lines,
                        PreviousFontName = t.PrimaryFontName,
                        TargetFontName = "Pyidaungsu"
                    });
                }
            }

            int applied = ExecuteSlideWriteOperations(
                ops,
                UiLocalizer.Pick("Repair Garbled PDF Slide Text -> Pyidaungsu", "PDF ဆလိုက်စာသားပျက် ပြန်လည်တည်ဆောက်ချက်"));
            if (applied > 0)
                RunScan(CurrentScope, userInitiated: false);

            outcome.Success = true;
            outcome.ContainersModified = ops.Count;
            outcome.TotalChanges = totalRepaired;
            outcome.UserMessage = ops.Count > 0
                ? UiLocalizer.Pick($"✓ Repaired ({totalRepaired}) garbled PDF line(s) across ({ops.Count}) slide container(s) into Pyidaungsu.", $"✓ ဆလိုက်ကွက် ({ops.Count}) ခုရှိ PDF စာသားပျက် ({totalRepaired}) ကြောင်းကို Pyidaungsu သို့ ပြန်လည်တည်ဆောက်ပြီးပါပြီ။")
                : UiLocalizer.Pick("✓ No garbled Burmese PDF CMap text detected on slides.", "✓ ဆလိုက်များတွင် ပြုပြင်ရန် PDF စာသားပျက် မတွေ့ရှိပါ။");
            return outcome;
        }

        public PowerPointToolkitOutcome RewriteOfficialProseInScope(bool selectionOnly)
        {
            var outcome = new PowerPointToolkitOutcome();
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return outcome;

            string presKey = GetActivePresentationKey();
            var scope = selectionOnly ? PresentationScanScope.Selection : PresentationScanScope.EntirePresentation;
            var targets = ExtractTargetsForScope(_pptApp.ActivePresentation, presKey, scope, IncludeSlideMasters);

            var ops = new List<SlideTextWriteOperation>();
            int totalRewritten = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                string raw = t.TextSnapshot ?? string.Empty;
                if (!SpreadsheetProofingHelper.ContainsMyanmarScript(raw))
                    continue;

                OfficialRewriteResult res = MyanmarOfficialRewriter.ConvertToOfficialProse(raw);
                if (res.ReplacementCount > 0 && !string.Equals(res.RewrittenText, raw, StringComparison.Ordinal))
                {
                    totalRewritten += res.ReplacementCount;
                    ops.Add(new SlideTextWriteOperation
                    {
                        Target = t,
                        PreviousText = raw,
                        UpdatedText = res.RewrittenText,
                        ChangesInContainer = res.ReplacementCount
                    });
                }
            }

            int applied = ExecuteSlideWriteOperations(
                ops,
                UiLocalizer.Pick("Rewrite Slides to Formal / Official Style", "ဆလိုက်စာသား ရုံးသုံးစာပေဟန် ပြောင်းလဲချက်"));
            if (applied > 0)
                RunScan(CurrentScope, userInitiated: false);

            outcome.Success = true;
            outcome.ContainersModified = ops.Count;
            outcome.TotalChanges = totalRewritten;
            outcome.UserMessage = ops.Count > 0
                ? UiLocalizer.Pick($"✓ Rewrote ({totalRewritten}) colloquial expression(s) across ({ops.Count}) slide container(s) into formal style.", $"✓ ဆလိုက်ကွက် ({ops.Count}) ခုရှိ စကားပြောဟန် ({totalRewritten}) နေရာကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းလဲပြီးပါပြီ။")
                : UiLocalizer.Pick("✓ Slide text is already in formal/official style.", "✓ ဆလိုက်စာသားများ ရုံးသုံးစာပေဟန် ဖြစ်ပြီးသား ဖြစ်ပါသည်။");
            return outcome;
        }

        public PowerPointToolkitOutcome PasteAndRepairClipboardIntoActiveSlide()
        {
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
            {
                return new PowerPointToolkitOutcome
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("⚠ Please open a slide in Normal edit view first.", "⚠ ကျေးဇူးပြု၍ ဆလိုက်ကို ပြင်ဆင်သည့်မြင်ကွင်း (Normal View) တွင် အရင်ဖွင့်ပါ။")
                };
            }

            string rawClip = string.Empty;
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                    rawClip = Clipboard.GetText(TextDataFormat.UnicodeText);
                else if (Clipboard.ContainsText())
                    rawClip = Clipboard.GetText();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(rawClip))
            {
                return new PowerPointToolkitOutcome
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("⚠ Clipboard does not contain any text to clean & paste.", "⚠ Clipboard ထဲတွင် ကူးထည့်ရန် စာသား မရှိပါ။")
                };
            }

            string cleaned = _engine.RepairClipboardOrPastedText(rawClip, out int repairedSpans);
            bool inserted = InsertTextIntoActiveSlideSelection(cleaned);
            if (inserted)
            {
                QueueLiveActiveSlideScan();
            }

            return new PowerPointToolkitOutcome
            {
                Success = inserted,
                ContainersModified = inserted ? 1 : 0,
                TotalChanges = Math.Max(1, repairedSpans),
                UserMessage = inserted
                    ? (repairedSpans > 0
                        ? UiLocalizer.Pick($"✓ Repaired ({repairedSpans}) garbled PDF/Zawgyi span(s) and inserted clean Unicode text.", $"✓ Clipboard မှ စာသားပျက်/ဇော်ဂျီ ({repairedSpans}) ခုကို သန့်စင်၍ ဆလိုက်အတွင်း ထည့်သွင်းပြီးပါပြီ။")
                        : UiLocalizer.Pick("✓ Inserted clean Myanmar Unicode text (or copied to Clipboard if no text box was active).", "✓ သန့်စင်သော မြန်မာယူနီကုဒ်စာသား ထည့်သွင်းပြီးပါပြီ။"))
                    : UiLocalizer.Pick("⚠ Could not insert text into the active slide.", "⚠ ဆလိုက်အတွင်း စာသားထည့်သွင်း၍ မရပါ။")
            };
        }

        public string TryGetSelectedSlideText(int maxChars = 80)
        {
            if (IsSlideShowRunning || !IsPowerPointReadyForInterop(_pptApp))
                return string.Empty;

            try
            {
                PowerPoint.Selection sel = _pptApp.ActiveWindow.Selection;
                if (sel != null)
                {
                    if (sel.Type == PowerPoint.PpSelectionType.ppSelectionText && sel.TextRange != null)
                    {
                        string raw = (sel.TextRange.Text ?? string.Empty).Replace('\r', ' ').Replace('\v', ' ').Replace('\n', ' ').Trim(' ', '\t', '၊', '။', '"', '\'', '“', '”', '(', ')');
                        if (!string.IsNullOrWhiteSpace(raw))
                            return raw.Length <= maxChars ? raw : raw.Substring(0, maxChars).Trim();
                    }
                    else if (sel.Type == PowerPoint.PpSelectionType.ppSelectionShapes && sel.ShapeRange != null && sel.ShapeRange.Count >= 1)
                    {
                        PowerPoint.Shape sh = sel.HasChildShapeRange ? sel.ChildShapeRange[1] : sel.ShapeRange[1];
                        if (sh != null && sh.HasTextFrame == Office.MsoTriState.msoTrue && sh.TextFrame.HasText == Office.MsoTriState.msoTrue)
                        {
                            string raw = (sh.TextFrame.TextRange.Text ?? string.Empty).Replace('\r', ' ').Replace('\v', ' ').Replace('\n', ' ').Trim(' ', '\t', '၊', '။', '"', '\'', '“', '”', '(', ')');
                            if (!string.IsNullOrWhiteSpace(raw))
                                return raw.Length <= maxChars ? raw : raw.Substring(0, maxChars).Trim();
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public string TryGetSelectedNumericText()
        {
            string sel = TryGetSelectedSlideText(60);
            if (string.IsNullOrWhiteSpace(sel))
                return string.Empty;

            var sb = new System.Text.StringBuilder(sel.Length);
            for (int i = 0; i < sel.Length; i++)
            {
                char c = sel[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == ',' || c == '-')
                    sb.Append(c);
                else if (c >= '\u1040' && c <= '\u1049')
                    sb.Append((char)('0' + (c - '\u1040')));
            }
            return sb.Length > 0 ? sb.ToString().Trim(',', ' ') : string.Empty;
        }

        private static bool HasAsciiLetters(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return true;
            }
            return false;
        }
    }
}
