using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.Core.Typography;
using Excel = Microsoft.Office.Interop.Excel;

namespace MyanmarSpellChecker.ExcelAddIn.Interop
{
    public sealed class ExcelScanOutcomeMetadata
    {
        public bool IsEcoActive { get; set; }
        public bool WasPartialAutoScan { get; set; }
        public bool WasIssueCapReached { get; set; }
        public bool WasCancelled { get; set; }
        public long ScannedCellCount { get; set; }
        public long TotalScopeCells { get; set; }
    }

    public sealed class ExcelToolkitOutcome
    {
        public bool Success { get; set; }
        public int CellsModified { get; set; }
        public int TotalChanges { get; set; }
        public string UserMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Formula-safe, Undo-safe, bulk 2D SAFEARRAY scanner, Save/Print-safe In-Cell Underline Manager,
    /// and spreadsheet toolkit for Microsoft Excel.
    /// Rules enforced at all times:
    /// 1. Bulk scanning reads 2D SAFEARRAYs; when visual underlines are enabled, only plain-text non-formula cells
    ///    with active issues (or cells being cleaned) have per-word Red/Blue/Amber underlines applied, and all
    ///    temporary proofing underlines are automatically stripped before Save (Ctrl+S) and Print (Ctrl+P).
    /// 2. Formula cells (HasFormula == true or Formula starting with '=') and numeric calculation cells are
    ///    NEVER modified by spellcheck fixes, underlines, Zawgyi conversion, numeral conversion, or style rewrites.
    /// 3. In-Cell Edit Mode / Formula Bar typing is guarded via IsExcelReadyForInterop + HRESULT shields
    ///    (0x800A03EC, 0x800AC472, 0x80010001) so Excel never throws or lags while a user is typing.
    /// 4. Every add-in cell modification is recorded in SpreadsheetUndoManager for instant 1-click Undo.
    /// </summary>
    public sealed class ExcelWorkbookScannerAndToolkit : IDisposable
    {
        private const int MaxRowsPerChunk = 2000;
        private const int MaxColsPerChunk = 64;
        private const int MaxCellsPerFullScan = 250_000;
        private const int MaxCellsPerLiveEditScan = 5_000;
        private const int MaxUnderlinedCellsPerScan = 120;
        private const int ExtractSliceBudgetMs = 10;
        private const int UnderlineSliceBudgetMs = 9;

        // OLE BGR colors for Excel Range.Characters.Font.Color
        private const int OleColorSpellingRed = 0x001C1CD2;  // RGB(210, 28, 28)
        private const int OleColorGrammarBlue = 0x00C05814;  // RGB(20, 88, 192)
        private const int OleColorContextAmber = 0x000866BA; // RGB(186, 102, 8)

        private sealed class MarkedCellBaseline
        {
            public string SheetName { get; set; } = string.Empty;
            public int Row { get; set; }
            public int Column { get; set; }
            public bool WasColorAutomatic { get; set; } = true;
            public int OriginalOleColor { get; set; }
            public int OriginalUnderline { get; set; } = (int)Excel.XlUnderlineStyle.xlUnderlineStyleNone;
            public string AppliedSignature { get; set; } = string.Empty;
        }

        private readonly struct PendingChunkSpec
        {
            public readonly string SheetName;
            public readonly int BaseRow;
            public readonly int BaseCol;
            public readonly int Rows;
            public readonly int Cols;

            public PendingChunkSpec(string sheetName, int baseRow, int baseCol, int rows, int cols)
            {
                SheetName = sheetName;
                BaseRow = baseRow;
                BaseCol = baseCol;
                Rows = rows;
                Cols = cols;
            }
        }

        private sealed class ExtractedExcelBlock
        {
            public string SheetName { get; set; } = string.Empty;
            public int BaseRow { get; set; }
            public int BaseCol { get; set; }
            public int Rows { get; set; }
            public int Cols { get; set; }
            public object? Val2 { get; set; }
            public object? Formula { get; set; }
            public bool? HasFormula { get; set; }
        }

        private sealed class PendingExtractionState
        {
            public const int MaxBufferedBlocksInRam = 3;

            public int Generation { get; set; }
            public string WorkbookName { get; set; } = string.Empty;
            public Queue<PendingChunkSpec> Chunks { get; } = new Queue<PendingChunkSpec>();
            public int TotalChunksPlanned { get; set; }
            public int ChunksExtractedCount { get; set; }
            public int ChunksProcessedCount { get; set; }
            public long TotalScopeCells { get; set; }
            public long ScannedCells { get; set; }
            public bool WasPartialAutoScan { get; set; }
            public EcoRuntimeBudgets Budgets { get; set; }
            public object StreamSync { get; } = new object();
            public Queue<ExtractedExcelBlock> ReadyBlocks { get; } = new Queue<ExtractedExcelBlock>(4);
            public bool ExtractionFinished { get; set; }
            public bool ConsumerRunning { get; set; }
            public volatile bool StopRequestedByIssueCap;
            public List<SpreadsheetCellIssue> CollectedIssues { get; } = new List<SpreadsheetCellIssue>(128);
            public Dictionary<string, IReadOnlyList<ProofingIssue>> SharedMemo { get; } =
                new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            public CancellationToken Token { get; set; }
        }

        private sealed class PendingUnderlineOp
        {
            public bool IsRestoreOnly { get; set; }
            public string CellKey { get; set; } = string.Empty;
            public string SheetName { get; set; } = string.Empty;
            public int Row { get; set; }
            public int Column { get; set; }
            public MarkedCellBaseline? BaselineToRestore { get; set; }
            public List<SpreadsheetCellIssue>? IssuesToApply { get; set; }
            public string Signature { get; set; } = string.Empty;
        }

        private sealed class PendingUnderlineJob
        {
            public int Generation { get; set; }
            public string WorkbookName { get; set; } = string.Empty;
            public bool PreserveSavedState { get; set; }
            public bool InitialWorkbookSaved { get; set; }
            public Queue<PendingUnderlineOp> Operations { get; } = new Queue<PendingUnderlineOp>();
        }

        private readonly Excel.Application _excelApp;
        private readonly MyanmarProofingEngine _engine;
        private readonly SpreadsheetUndoManager _undoManager = new SpreadsheetUndoManager(maxBatches: 30);

        private readonly Dictionary<string, List<SpreadsheetCellIssue>> _issuesByWorkbook =
            new Dictionary<string, List<SpreadsheetCellIssue>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DocumentQualityReport> _qualityByWorkbook =
            new Dictionary<string, DocumentQualityReport>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Dictionary<string, MarkedCellBaseline>> _markedCellsByWorkbook =
            new Dictionary<string, Dictionary<string, MarkedCellBaseline>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ExcelScanOutcomeMetadata> _scanMetaByWorkbook =
            new Dictionary<string, ExcelScanOutcomeMetadata>(StringComparer.OrdinalIgnoreCase);

        private readonly Control _staMarshaller;
        private readonly System.Windows.Forms.Timer _postSaveRestoreTimer;
        private readonly System.Windows.Forms.Timer _deferredScanTimer;
        private readonly System.Windows.Forms.Timer _extractChunkTimer;
        private readonly System.Windows.Forms.Timer _underlineChunkTimer;
        private SpreadsheetScanScope _deferredScanScope = SpreadsheetScanScope.ActiveSheet;
        private bool _deferredAllowPartialAutoScan = true;
        private int _scanGeneration;
        private CancellationTokenSource? _activeScanCts;
        private PendingExtractionState? _activeExtractState;
        private PendingUnderlineJob? _activeUnderlineJob;
        private string? _pendingRestoreWorkbookName;
        private int _suppressSheetChangeEvents;
        private bool _lastEcoActive;
        private bool _disposed;

        public bool IsLiveAutoScanEnabled { get; set; } = true;
        public bool IsVisualUnderlineEnabled { get; set; } = true;
        public bool IsEcoActive => _lastEcoActive;
        public SpreadsheetScanScope CurrentScope { get; set; } = SpreadsheetScanScope.ActiveSheet;
        public SpreadsheetUndoManager UndoManager => _undoManager;

        public event Action? ScanStarted;
        public event Action<int, string>? ScanProgressChanged;
        public event Action<bool>? EcoActiveChanged;
        public event Action<IReadOnlyList<SpreadsheetCellIssue>>? ScanCompleted;
        public event Action? UndoStateChanged;

        public ExcelWorkbookScannerAndToolkit(Excel.Application excelApp, MyanmarProofingEngine engine)
        {
            _excelApp = excelApp ?? throw new ArgumentNullException(nameof(excelApp));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            _staMarshaller = new Control();
            var _ = _staMarshaller.Handle;

            _postSaveRestoreTimer = new System.Windows.Forms.Timer { Interval = 450 };
            _postSaveRestoreTimer.Tick += OnPostSaveRestoreTimerTick;

            _deferredScanTimer = new System.Windows.Forms.Timer { Interval = 220 };
            _deferredScanTimer.Tick += OnDeferredScanTimerTick;

            _extractChunkTimer = new System.Windows.Forms.Timer { Interval = 14 };
            _extractChunkTimer.Tick += OnExtractChunkTimerTick;

            _underlineChunkTimer = new System.Windows.Forms.Timer { Interval = 20 };
            _underlineChunkTimer.Tick += OnUnderlineChunkTimerTick;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            CancelActiveAsyncPipeline();
            _postSaveRestoreTimer.Stop();
            _postSaveRestoreTimer.Dispose();
            _deferredScanTimer.Stop();
            _deferredScanTimer.Dispose();
            _extractChunkTimer.Stop();
            _extractChunkTimer.Dispose();
            _underlineChunkTimer.Stop();
            _underlineChunkTimer.Dispose();
            if (!_staMarshaller.IsDisposed)
                _staMarshaller.Dispose();
        }

        private void CancelActiveAsyncPipeline()
        {
            _scanGeneration++;
            _deferredScanTimer.Stop();
            _extractChunkTimer.Stop();
            _underlineChunkTimer.Stop();
            _activeExtractState = null;
            _activeUnderlineJob = null;

            if (_activeScanCts != null)
            {
                try { _activeScanCts.Cancel(); } catch { }
                try { _activeScanCts.Dispose(); } catch { }
                _activeScanCts = null;
            }
        }

        private void StopActiveUnderlineJobOnly()
        {
            _underlineChunkTimer.Stop();
            _activeUnderlineJob = null;
        }

        public static bool IsExcelReadyForInterop(Excel.Application? app)
        {
            if (app == null)
                return false;

            try
            {
                if (!app.Ready)
                    return false;

                // When the user is actively editing inside a cell or modal dialog, Interactive/ActiveWorkbook access may fail
                if (!app.Interactive)
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

        public static bool IsTransientExcelBusyComError(COMException ex)
        {
            uint hr = (uint)ex.ErrorCode;
            // 0x800A03EC = VBA_E_IGNORE / general Range busy error during In-Cell Edit Mode
            // 0x800AC472 = Excel UI busy / dragging fill handle / Formula bar active
            // 0x80010001 = RPC_E_CALL_REJECTED
            // 0x8001010A = RPC_E_SERVERCALL_RETRYLATER
            return hr == 0x800A03EC || hr == 0x800AC472 || hr == 0x80010001 || hr == 0x8001010A;
        }

        public IReadOnlyList<SpreadsheetCellIssue> GetCachedIssuesForActiveWorkbook()
        {
            string wbKey = GetActiveWorkbookKey();
            if (!string.IsNullOrEmpty(wbKey) && _issuesByWorkbook.TryGetValue(wbKey, out var list))
                return list;

            return Array.Empty<SpreadsheetCellIssue>();
        }

        public ExcelScanOutcomeMetadata? GetLastScanMetadataForActiveWorkbook()
        {
            string wbKey = GetActiveWorkbookKey();
            if (!string.IsNullOrEmpty(wbKey) && _scanMetaByWorkbook.TryGetValue(wbKey, out var meta))
                return meta;

            return null;
        }

        public void RefreshEcoActiveState()
        {
            long scopeCells = 0;
            try
            {
                if (_excelApp.ActiveWorkbook?.ActiveSheet is Excel.Worksheet ws)
                {
                    Excel.Range? used = SafeGetUsedRange(ws);
                    if (used != null)
                        scopeCells = SafeGetCountLarge(used);
                }
            }
            catch { }

            var budgets = EcoPerformanceGovernor.ComputeBudgets(EcoPerformanceGovernor.IsLargeExcelScope(scopeCells));
            UpdateEcoActiveState(budgets.IsEcoActive);
        }

        private void UpdateEcoActiveState(bool ecoActive)
        {
            if (_lastEcoActive != ecoActive)
            {
                _lastEcoActive = ecoActive;
                EcoActiveChanged?.Invoke(ecoActive);
            }
        }

        public void CancelUserScan()
        {
            CancelActiveAsyncPipeline();
            string wbKey = GetActiveWorkbookKey();
            if (!string.IsNullOrEmpty(wbKey))
            {
                _scanMetaByWorkbook[wbKey] = new ExcelScanOutcomeMetadata
                {
                    IsEcoActive = _lastEcoActive,
                    WasCancelled = true
                };
            }
            ScanCompleted?.Invoke(GetCachedIssuesForActiveWorkbook());
        }

        public void ClearCachedIssuesForActiveWorkbook()
        {
            CancelActiveAsyncPipeline();
            string wbKey = GetActiveWorkbookKey();
            if (!string.IsNullOrEmpty(wbKey))
            {
                ClearAllVisualUnderlinesInActiveWorkbook();
                _issuesByWorkbook.Remove(wbKey);
                _qualityByWorkbook.Remove(wbKey);
                _scanMetaByWorkbook.Remove(wbKey);
            }
        }

        public void PurgeClosedWorkbook(string? workbookName)
        {
            if (string.IsNullOrEmpty(workbookName))
                return;

            if (_activeExtractState != null &&
                string.Equals(_activeExtractState.WorkbookName, workbookName, StringComparison.OrdinalIgnoreCase))
            {
                CancelActiveAsyncPipeline();
            }
            if (_activeUnderlineJob != null &&
                string.Equals(_activeUnderlineJob.WorkbookName, workbookName, StringComparison.OrdinalIgnoreCase))
            {
                StopActiveUnderlineJobOnly();
            }

            bool wasLarge = _scanMetaByWorkbook.TryGetValue(workbookName!, out var meta) &&
                            (meta.TotalScopeCells >= EcoPerformanceGovernor.LargeExcelCellThreshold || meta.IsEcoActive);

            _issuesByWorkbook.Remove(workbookName!);
            _qualityByWorkbook.Remove(workbookName!);
            _markedCellsByWorkbook.Remove(workbookName!);
            _scanMetaByWorkbook.Remove(workbookName!);
            if (string.Equals(_pendingRestoreWorkbookName, workbookName, StringComparison.OrdinalIgnoreCase))
            {
                _pendingRestoreWorkbookName = null;
                _postSaveRestoreTimer.Stop();
            }
            _undoManager.ClearForWorkbook(workbookName!);
            if (wasLarge || _issuesByWorkbook.Count == 0)
            {
                _engine.TrimMemoryCaches();
            }
            UndoStateChanged?.Invoke();
        }

        /// <summary>
        /// Schedules a debounced non-blocking scan (ideal for WorkbookOpen and rapid Sheet tab switching).
        /// </summary>
        public void ScheduleDeferredScanScope(SpreadsheetScanScope scope, int delayMs = 220, bool allowSmartPartialAutoScan = true)
        {
            if (_disposed)
                return;

            CancelActiveAsyncPipeline();
            CurrentScope = scope;
            _deferredScanScope = scope;
            _deferredAllowPartialAutoScan = allowSmartPartialAutoScan;
            _deferredScanTimer.Interval = Math.Max(60, delayMs);
            _deferredScanTimer.Start();
        }

        private void OnDeferredScanTimerTick(object? sender, EventArgs e)
        {
            _deferredScanTimer.Stop();
            if (_disposed)
                return;

            ScanScopeAsync(_deferredScanScope, _deferredAllowPartialAutoScan);
        }

        /// <summary>
        /// Non-blocking, bounded-RAM streaming spreadsheet scan pipeline for large sheets and multi-sheet workbooks:
        /// Stage 1: Time-sliced STA 2D SAFEARRAY extraction (&lt;= 10ms per tick, max 3 blocks buffered in RAM at once!).
        /// Stage 2: Gentle hardware-throttled streaming NLP consumer freeing each 2D array immediately after scan.
        /// Stage 3: Viewport-prioritized, Eco-capped, incremental STA cell underlining (&lt;= 9ms per tick).
        /// </summary>
        public void ScanScopeAsync(SpreadsheetScanScope scope, bool allowSmartPartialAutoScan = false)
        {
            if (_disposed)
                return;

            CancelActiveAsyncPipeline();
            CurrentScope = scope;

            if (!IsExcelReadyForInterop(_excelApp))
                return;

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
            {
                ScanCompleted?.Invoke(Array.Empty<SpreadsheetCellIssue>());
                return;
            }

            string wbName = SafeGetWorkbookName(wb);
            int generation = _scanGeneration;
            var cts = new CancellationTokenSource();
            _activeScanCts = cts;

            ScanStarted?.Invoke();

            var state = new PendingExtractionState
            {
                Generation = generation,
                WorkbookName = wbName,
                Token = cts.Token
            };

            try
            {
                EnqueueScopeChunkSpecs(wb, scope, state, allowSmartPartialAutoScan);
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                ScanCompleted?.Invoke(GetCachedIssuesForActiveWorkbook());
                return;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ScanScopeAsync.Enqueue", ex);
                ScanCompleted?.Invoke(GetCachedIssuesForActiveWorkbook());
                return;
            }

            UpdateEcoActiveState(state.Budgets.IsEcoActive);

            if (state.Chunks.Count == 0)
            {
                _issuesByWorkbook[wbName] = new List<SpreadsheetCellIssue>();
                _qualityByWorkbook.Remove(wbName);
                _scanMetaByWorkbook[wbName] = new ExcelScanOutcomeMetadata
                {
                    IsEcoActive = state.Budgets.IsEcoActive,
                    WasPartialAutoScan = state.WasPartialAutoScan,
                    ScannedCellCount = 0,
                    TotalScopeCells = 0
                };
                ScanCompleted?.Invoke(Array.Empty<SpreadsheetCellIssue>());
                StartIncrementalUnderlineSync(wb, wbName, generation, preserveSavedState: true);
                return;
            }

            state.TotalChunksPlanned = state.Chunks.Count;
            if (!state.WasPartialAutoScan && state.TotalChunksPlanned > 1)
            {
                ScanProgressChanged?.Invoke(
                    5,
                    UiLocalizer.Pick("⏳ Reading spreadsheet cells...", "⏳ အကွက်များ ဖတ်ယူစစ်ဆေးနေသည်..."));
            }

            _activeExtractState = state;
            ProcessExtractionSlice();
        }

        private void EnqueueScopeChunkSpecs(
            Excel.Workbook wb,
            SpreadsheetScanScope scope,
            PendingExtractionState state,
            bool allowSmartPartialAutoScan)
        {
            long estimatedTotalCells = EstimateTotalScopeCells(wb, scope);
            bool isLargeScope = EcoPerformanceGovernor.IsLargeExcelScope(estimatedTotalCells);
            EcoRuntimeBudgets budgets = EcoPerformanceGovernor.ComputeBudgets(isLargeScope);
            state.TotalScopeCells = estimatedTotalCells;
            state.Budgets = budgets;

            // Smart Partial Auto-Scan on Open / Sheet Switch: protect weak ("potato") PCs & huge sheets!
            if (allowSmartPartialAutoScan &&
                scope != SpreadsheetScanScope.Selection &&
                (isLargeScope || (budgets.IsEcoActive && estimatedTotalCells >= 3_000)))
            {
                if (TryEnqueuePartialViewportChunks(wb, state.Chunks, budgets.MaxAutoOpenExcelCells, out long partialCells))
                {
                    state.WasPartialAutoScan = true;
                    state.ScannedCells = partialCells;
                    return;
                }
            }

            int chunkRowSize = budgets.IsEcoActive ? 800 : MaxRowsPerChunk;
            int chunkColSize = budgets.IsEcoActive ? 32 : MaxColsPerChunk;
            int budgetRemaining = MaxCellsPerFullScan;
            long plannedCells = 0;

            void EnqueueWorksheetRange(Excel.Worksheet ws, Excel.Range rangeToScan, bool alreadyTrimmedUsed)
            {
                if (budgetRemaining <= 0)
                    return;

                Excel.Range? effective = rangeToScan;
                if (!alreadyTrimmedUsed)
                {
                    Excel.Range? used = SafeGetUsedRange(ws);
                    if (used == null)
                        return;
                    effective = SafeIntersect(rangeToScan, used);
                    if (effective == null)
                        return;
                }

                string sheetName = SafeGetSheetName(ws);
                foreach (Excel.Range area in effective.Areas)
                {
                    if (budgetRemaining <= 0)
                        break;

                    int areaRow = area.Row;
                    int areaCol = area.Column;
                    int areaRows = area.Rows.Count;
                    int areaCols = area.Columns.Count;

                    for (int rOffset = 0; rOffset < areaRows && budgetRemaining > 0; rOffset += chunkRowSize)
                    {
                        int chunkRows = Math.Min(chunkRowSize, areaRows - rOffset);
                        for (int cOffset = 0; cOffset < areaCols && budgetRemaining > 0; cOffset += chunkColSize)
                        {
                            int chunkCols = Math.Min(chunkColSize, areaCols - cOffset);
                            int cellSlice = chunkRows * chunkCols;
                            budgetRemaining -= cellSlice;
                            plannedCells += cellSlice;
                            state.Chunks.Enqueue(new PendingChunkSpec(sheetName, areaRow + rOffset, areaCol + cOffset, chunkRows, chunkCols));
                        }
                    }
                }
            }

            if (scope == SpreadsheetScanScope.Selection)
            {
                Excel.Range? selRange = null;
                try { selRange = _excelApp.Selection as Excel.Range; } catch { }
                if (selRange?.Worksheet is Excel.Worksheet ws)
                {
                    EnqueueWorksheetRange(ws, selRange, alreadyTrimmedUsed: false);
                }
            }
            else if (scope == SpreadsheetScanScope.EntireWorkbook)
            {
                foreach (object rawSheet in wb.Worksheets)
                {
                    if (budgetRemaining <= 0)
                        break;

                    if (rawSheet is Excel.Worksheet ws)
                    {
                        bool visible = true;
                        try { visible = ws.Visible == Excel.XlSheetVisibility.xlSheetVisible; } catch { }
                        if (!visible)
                            continue;

                        Excel.Range? used = SafeGetUsedRange(ws);
                        if (used != null)
                        {
                            EnqueueWorksheetRange(ws, used, alreadyTrimmedUsed: true);
                        }
                    }
                }
            }
            else
            {
                if (wb.ActiveSheet is Excel.Worksheet activeSheet)
                {
                    Excel.Range? used = SafeGetUsedRange(activeSheet);
                    if (used != null)
                    {
                        EnqueueWorksheetRange(activeSheet, used, alreadyTrimmedUsed: true);
                    }
                }
            }

            state.ScannedCells = plannedCells;
        }

        private long EstimateTotalScopeCells(Excel.Workbook wb, SpreadsheetScanScope scope)
        {
            long total = 0;
            try
            {
                if (scope == SpreadsheetScanScope.Selection)
                {
                    if (_excelApp.Selection is Excel.Range sel && sel.Worksheet is Excel.Worksheet sws)
                    {
                        Excel.Range? used = SafeGetUsedRange(sws);
                        Excel.Range? eff = used != null ? SafeIntersect(sel, used) : sel;
                        if (eff != null) total = SafeGetCountLarge(eff);
                    }
                }
                else if (scope == SpreadsheetScanScope.EntireWorkbook)
                {
                    foreach (object rawSheet in wb.Worksheets)
                    {
                        if (rawSheet is Excel.Worksheet ws)
                        {
                            bool visible = true;
                            try { visible = ws.Visible == Excel.XlSheetVisibility.xlSheetVisible; } catch { }
                            if (!visible) continue;
                            Excel.Range? used = SafeGetUsedRange(ws);
                            if (used != null) total += SafeGetCountLarge(used);
                        }
                    }
                }
                else if (wb.ActiveSheet is Excel.Worksheet aws)
                {
                    Excel.Range? used = SafeGetUsedRange(aws);
                    if (used != null) total = SafeGetCountLarge(used);
                }
            }
            catch { }
            return total;
        }

        private bool TryEnqueuePartialViewportChunks(
            Excel.Workbook wb,
            Queue<PendingChunkSpec> queue,
            int maxAutoCells,
            out long plannedCells)
        {
            plannedCells = 0;
            try
            {
                if (!(wb.ActiveSheet is Excel.Worksheet ws))
                    return false;

                Excel.Range? used = SafeGetUsedRange(ws);
                if (used == null)
                    return false;

                int usedR1 = used.Row;
                int usedC1 = used.Column;
                int usedR2 = usedR1 + used.Rows.Count - 1;
                int usedC2 = usedC1 + used.Columns.Count - 1;

                int anchorRow = usedR1;
                int anchorCol = usedC1;
                try
                {
                    Excel.Range? vis = _excelApp.ActiveWindow?.VisibleRange;
                    if (vis != null)
                    {
                        anchorRow = Math.Max(usedR1, Math.Min(usedR2, vis.Row));
                        anchorCol = Math.Max(usedC1, Math.Min(usedC2, vis.Column));
                    }
                }
                catch { }

                int colsToTake = Math.Min(25, usedC2 - anchorCol + 1);
                if (colsToTake <= 0)
                {
                    anchorCol = usedC1;
                    colsToTake = Math.Min(25, usedC2 - usedC1 + 1);
                }
                int maxRows = Math.Max(20, maxAutoCells / Math.Max(1, colsToTake));
                int startRow = Math.Max(usedR1, anchorRow - 10);
                int rowsToTake = Math.Min(maxRows, usedR2 - startRow + 1);
                if (rowsToTake <= 0)
                    return false;

                string sheetName = SafeGetSheetName(ws);
                queue.Enqueue(new PendingChunkSpec(sheetName, startRow, anchorCol, rowsToTake, colsToTake));
                plannedCells = (long)rowsToTake * colsToTake;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnExtractChunkTimerTick(object? sender, EventArgs e)
        {
            _extractChunkTimer.Stop();
            ProcessExtractionSlice();
        }

        private void ProcessExtractionSlice()
        {
            PendingExtractionState? state = _activeExtractState;
            if (_disposed || state == null || state.Generation != _scanGeneration || state.Token.IsCancellationRequested)
            {
                _activeExtractState = null;
                return;
            }

            if (state.StopRequestedByIssueCap)
            {
                lock (state.StreamSync)
                {
                    state.Chunks.Clear();
                    state.ExtractionFinished = true;
                }
                _activeExtractState = null;
                EnsureStreamingConsumerRunning(state);
                return;
            }

            // Backpressure guard: never buffer more than 3 raw 2D SAFEARRAY blocks in RAM at once!
            lock (state.StreamSync)
            {
                if (state.ReadyBlocks.Count >= PendingExtractionState.MaxBufferedBlocksInRam)
                {
                    EnsureStreamingConsumerRunning(state);
                    _extractChunkTimer.Start();
                    return;
                }
            }

            if (!IsExcelReadyForInterop(_excelApp))
            {
                // User started typing in a cell or dragging; pause briefly and retry without blocking UI
                _extractChunkTimer.Start();
                return;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null || !string.Equals(SafeGetWorkbookName(wb), state.WorkbookName, StringComparison.OrdinalIgnoreCase))
            {
                _activeExtractState = null;
                ScanCompleted?.Invoke(GetCachedIssuesForActiveWorkbook());
                return;
            }

            var wsMap = BuildWorksheetMap(wb);
            var sw = Stopwatch.StartNew();
            int sliceBudget = state.Budgets.IsEcoActive ? 7 : ExtractSliceBudgetMs;

            try
            {
                while (state.Chunks.Count > 0 && !state.StopRequestedByIssueCap)
                {
                    int bufferedNow;
                    lock (state.StreamSync)
                    {
                        bufferedNow = state.ReadyBlocks.Count;
                    }
                    if (bufferedNow >= PendingExtractionState.MaxBufferedBlocksInRam)
                        break;

                    PendingChunkSpec spec = state.Chunks.Dequeue();
                    state.ChunksExtractedCount++;

                    if (wsMap.TryGetValue(spec.SheetName, out Excel.Worksheet? ws) && ws != null)
                    {
                        Excel.Range chunk = (spec.Rows == 1 && spec.Cols == 1)
                            ? (Excel.Range)ws.Cells[spec.BaseRow, spec.BaseCol]
                            : ws.Range[ws.Cells[spec.BaseRow, spec.BaseCol], ws.Cells[spec.BaseRow + spec.Rows - 1, spec.BaseCol + spec.Cols - 1]];

                        bool? hasFormula = ReadRangeHasFormulaTriState(chunk);
                        if (hasFormula != true)
                        {
                            object? val2 = chunk.Value2;
                            if (val2 != null)
                            {
                                object? fml = hasFormula == null ? chunk.Formula : null;
                                var block = new ExtractedExcelBlock
                                {
                                    SheetName = spec.SheetName,
                                    BaseRow = spec.BaseRow,
                                    BaseCol = spec.BaseCol,
                                    Rows = spec.Rows,
                                    Cols = spec.Cols,
                                    Val2 = val2,
                                    Formula = fml,
                                    HasFormula = hasFormula
                                };
                                lock (state.StreamSync)
                                {
                                    state.ReadyBlocks.Enqueue(block);
                                }
                                EnsureStreamingConsumerRunning(state);
                            }
                        }
                    }

                    if (sw.ElapsedMilliseconds >= sliceBudget && state.Chunks.Count > 0)
                    {
                        EnsureStreamingConsumerRunning(state);
                        _extractChunkTimer.Start();
                        return;
                    }
                }
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                _activeExtractState = null;
                ScanCompleted?.Invoke(GetCachedIssuesForActiveWorkbook());
                return;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ProcessExtractionSlice", ex);
                _activeExtractState = null;
                ScanCompleted?.Invoke(GetCachedIssuesForActiveWorkbook());
                return;
            }

            if (state.Chunks.Count > 0 && !state.StopRequestedByIssueCap)
            {
                EnsureStreamingConsumerRunning(state);
                _extractChunkTimer.Start();
                return;
            }

            lock (state.StreamSync)
            {
                state.ExtractionFinished = true;
            }
            _activeExtractState = null;
            EnsureStreamingConsumerRunning(state);
        }

        private void EnsureStreamingConsumerRunning(PendingExtractionState state)
        {
            lock (state.StreamSync)
            {
                if (state.ConsumerRunning)
                    return;
                state.ConsumerRunning = true;
            }

            Task.Run(() =>
            {
                long lastProgressTick = Environment.TickCount;
                int maxIssues = state.Budgets.MaxIssuesPerFullScan;

                EcoPerformanceGovernor.ExecuteWithGentlePriority(state.Budgets.UseLowPriorityThreads, () =>
                {
                    while (!state.Token.IsCancellationRequested && state.Generation == Volatile.Read(ref _scanGeneration))
                    {
                        ExtractedExcelBlock? blk = null;
                        bool allDone = false;

                        lock (state.StreamSync)
                        {
                            if (state.ReadyBlocks.Count > 0)
                            {
                                blk = state.ReadyBlocks.Dequeue();
                            }
                            else if (state.ExtractionFinished || state.StopRequestedByIssueCap)
                            {
                                state.ConsumerRunning = false;
                                allDone = true;
                            }
                            else
                            {
                                // Producer is waiting on next WinForms timer slice; exit worker cleanly and let next enqueue wake a worker
                                state.ConsumerRunning = false;
                                return;
                            }
                        }

                        if (allDone)
                        {
                            FinalizeStreamingScanToSta(state);
                            return;
                        }

                        if (blk != null)
                        {
                            int remainingCap = maxIssues - state.CollectedIssues.Count;
                            if (remainingCap > 0)
                            {
                                var hits = SpreadsheetProofingHelper.ScanBlockValues(
                                    _engine,
                                    state.WorkbookName,
                                    blk.SheetName,
                                    blk.BaseRow,
                                    blk.BaseCol,
                                    blk.Rows,
                                    blk.Cols,
                                    blk.Val2,
                                    blk.Formula,
                                    blk.HasFormula,
                                    state.SharedMemo,
                                    state.Token,
                                    remainingCap,
                                    state.Budgets);

                                if (hits.Count > 0)
                                {
                                    state.CollectedIssues.AddRange(hits);
                                }

                                if (state.CollectedIssues.Count >= maxIssues)
                                {
                                    state.StopRequestedByIssueCap = true;
                                    lock (state.StreamSync)
                                    {
                                        state.ReadyBlocks.Clear();
                                    }
                                }
                            }

                            // Immediately release 2D SAFEARRAY references so Gen0/LOH reclaims memory!
                            blk.Val2 = null;
                            blk.Formula = null;
                            state.ChunksProcessedCount++;

                            long nowTick = Environment.TickCount;
                            if (!state.WasPartialAutoScan && state.TotalChunksPlanned > 1 && nowTick - lastProgressTick >= 120)
                            {
                                lastProgressTick = nowTick;
                                int pct = Math.Min(92, 8 + (state.ChunksProcessedCount * 84) / Math.Max(1, state.TotalChunksPlanned));
                                PostExcelProgressFromBackground(
                                    state.Generation,
                                    pct,
                                    UiLocalizer.Pick(
                                        $"⏳ Checking cells... {Math.Min(99, (state.ChunksProcessedCount * 100) / Math.Max(1, state.TotalChunksPlanned))}%",
                                        $"⏳ အကွက်များ စစ်ဆေးနေသည်... {Math.Min(99, (state.ChunksProcessedCount * 100) / Math.Max(1, state.TotalChunksPlanned))}%"));
                            }
                        }
                    }
                });
            }, state.Token);
        }

        private void PostExcelProgressFromBackground(int generation, int percent, string statusText)
        {
            if (_disposed || generation != Volatile.Read(ref _scanGeneration) || _staMarshaller.IsDisposed || !_staMarshaller.IsHandleCreated)
                return;

            try
            {
                _staMarshaller.BeginInvoke(new Action(() =>
                {
                    if (!_disposed && generation == _scanGeneration)
                    {
                        ScanProgressChanged?.Invoke(percent, statusText);
                    }
                }));
            }
            catch { }
        }

        private void FinalizeStreamingScanToSta(PendingExtractionState state)
        {
            if (state.Token.IsCancellationRequested || _disposed || _staMarshaller.IsDisposed || !_staMarshaller.IsHandleCreated)
                return;

            try
            {
                _staMarshaller.BeginInvoke(new Action(() =>
                {
                    if (_disposed || state.Generation != _scanGeneration || state.Token.IsCancellationRequested)
                        return;

                    state.SharedMemo.Clear();
                    _issuesByWorkbook[state.WorkbookName] = state.CollectedIssues;
                    _qualityByWorkbook.Remove(state.WorkbookName);
                    _scanMetaByWorkbook[state.WorkbookName] = new ExcelScanOutcomeMetadata
                    {
                        IsEcoActive = state.Budgets.IsEcoActive,
                        WasPartialAutoScan = state.WasPartialAutoScan,
                        WasIssueCapReached = state.StopRequestedByIssueCap,
                        WasCancelled = false,
                        ScannedCellCount = state.ScannedCells,
                        TotalScopeCells = state.TotalScopeCells
                    };

                    ScanCompleted?.Invoke(state.CollectedIssues);

                    if (IsExcelReadyForInterop(_excelApp))
                    {
                        Excel.Workbook? liveWb = null;
                        try { liveWb = _excelApp.ActiveWorkbook; } catch { }
                        if (liveWb != null && string.Equals(SafeGetWorkbookName(liveWb), state.WorkbookName, StringComparison.OrdinalIgnoreCase))
                        {
                            StartIncrementalUnderlineSync(liveWb, state.WorkbookName, state.Generation, preserveSavedState: true);
                        }
                    }
                }));
            }
            catch { }
        }

        /// <summary>
        /// Performs a synchronous read-only bulk 2D scan across the requested scope (kept for callers needing immediate return).
        /// </summary>
        public IReadOnlyList<SpreadsheetCellIssue> ScanScope(SpreadsheetScanScope scope)
        {
            CancelActiveAsyncPipeline();
            CurrentScope = scope;
            if (!IsExcelReadyForInterop(_excelApp))
                return GetCachedIssuesForActiveWorkbook();

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
                return Array.Empty<SpreadsheetCellIssue>();

            string wbName = SafeGetWorkbookName(wb);
            ScanStarted?.Invoke();

            var collected = new List<SpreadsheetCellIssue>();
            var sharedMemo = new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            int budgetRemaining = MaxCellsPerFullScan;

            try
            {
                if (scope == SpreadsheetScanScope.Selection)
                {
                    Excel.Range? selRange = null;
                    try { selRange = _excelApp.Selection as Excel.Range; } catch { }
                    if (selRange != null)
                    {
                        Excel.Worksheet? ws = selRange.Worksheet;
                        if (ws != null)
                        {
                            ScanWorksheetRangeReadOnly(wbName, ws, selRange, sharedMemo, collected, ref budgetRemaining);
                        }
                    }
                }
                else if (scope == SpreadsheetScanScope.EntireWorkbook)
                {
                    foreach (object rawSheet in wb.Worksheets)
                    {
                        if (budgetRemaining <= 0)
                            break;

                        if (rawSheet is Excel.Worksheet ws)
                        {
                            bool visible = true;
                            try { visible = ws.Visible == Excel.XlSheetVisibility.xlSheetVisible; } catch { }
                            if (!visible)
                                continue;

                            Excel.Range? used = SafeGetUsedRange(ws);
                            if (used != null)
                            {
                                ScanWorksheetRangeReadOnly(wbName, ws, used, sharedMemo, collected, ref budgetRemaining);
                            }
                        }
                    }
                }
                else
                {
                    // Default: ActiveSheet
                    Excel.Worksheet? activeSheet = null;
                    try { activeSheet = wb.ActiveSheet as Excel.Worksheet; } catch { }
                    if (activeSheet != null)
                    {
                        Excel.Range? used = SafeGetUsedRange(activeSheet);
                        if (used != null)
                        {
                            ScanWorksheetRangeReadOnly(wbName, activeSheet, used, sharedMemo, collected, ref budgetRemaining);
                        }
                    }
                }

                _issuesByWorkbook[wbName] = collected;
                _qualityByWorkbook.Remove(wbName);
                SyncVisualUnderlinesForWorkbook(wb, wbName, preserveSavedState: true);
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                // User entered edit mode mid-scan; return cached gracefully
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ScanScope", ex);
            }

            IReadOnlyList<SpreadsheetCellIssue> finalList = _issuesByWorkbook.TryGetValue(wbName, out var stored)
                ? stored
                : collected;

            ScanCompleted?.Invoke(finalList);
            return finalList;
        }

        /// <summary>
        /// Incremental cell re-check triggered after a user finishes editing cell(s) and presses Enter/Tab.
        /// Updates per-word Red/Blue/Amber underlines on affected cells when visual underlines are enabled.
        /// </summary>
        public void OnWorkbookSheetChange(object sh, Excel.Range target)
        {
            if (!IsLiveAutoScanEnabled || _suppressSheetChangeEvents > 0)
                return;

            if (!(sh is Excel.Worksheet ws) || target == null || !IsExcelReadyForInterop(_excelApp))
                return;

            try
            {
                Excel.Workbook? wb = ws.Parent as Excel.Workbook ?? _excelApp.ActiveWorkbook;
                if (wb == null)
                    return;

                string wbName = SafeGetWorkbookName(wb);
                string sheetName = SafeGetSheetName(ws);

                Excel.Range? used = SafeGetUsedRange(ws);
                Excel.Range? effective = used != null ? SafeIntersect(target, used) ?? target : target;
                if (effective == null)
                    return;

                long countLarge = SafeGetCountLarge(effective);
                if (countLarge <= 0 || countLarge > MaxCellsPerLiveEditScan)
                    return;

                if (!_issuesByWorkbook.TryGetValue(wbName, out var existingList))
                {
                    existingList = new List<SpreadsheetCellIssue>();
                    _issuesByWorkbook[wbName] = existingList;
                }

                // Remove stale cached issues inside the edited Areas
                int removedCount = 0;
                foreach (Excel.Range area in effective.Areas)
                {
                    int rStart = area.Row;
                    int cStart = area.Column;
                    int rEnd = rStart + area.Rows.Count - 1;
                    int cEnd = cStart + area.Columns.Count - 1;

                    removedCount += existingList.RemoveAll(item =>
                        string.Equals(item.SheetName, sheetName, StringComparison.OrdinalIgnoreCase) &&
                        item.Row >= rStart && item.Row <= rEnd &&
                        item.Column >= cStart && item.Column <= cEnd);
                }

                var freshIssues = new List<SpreadsheetCellIssue>();
                var memo = new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
                int budget = MaxCellsPerLiveEditScan;
                ScanWorksheetRangeReadOnly(wbName, ws, effective, memo, freshIssues, ref budget);

                if (freshIssues.Count > 0)
                {
                    existingList.AddRange(freshIssues);
                    existingList.Sort(CompareIssuesByLocation);
                }

                _qualityByWorkbook.Remove(wbName);
                SyncVisualUnderlinesForEditedRange(wb, ws, wbName, sheetName, effective, freshIssues);

                // Avoid tearing down and recreating WinForms Task Pane cards when a clean cell stays clean (0 -> 0 delta)
                if (removedCount > 0 || freshIssues.Count > 0)
                {
                    ScanCompleted?.Invoke(existingList);
                }
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                // Ignore busy state
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("OnWorkbookSheetChange", ex);
            }
        }

        public bool SelectAndScrollToIssue(SpreadsheetCellIssue? item)
        {
            if (item == null || !IsExcelReadyForInterop(_excelApp))
                return false;

            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                if (wb == null)
                    return false;

                Excel.Worksheet? ws = FindWorksheetByName(wb, item.SheetName);
                if (ws == null)
                    return false;

                try
                {
                    if (!ReferenceEquals(wb.ActiveSheet, ws))
                    {
                        ((Excel._Worksheet)ws).Activate();
                    }
                }
                catch { }

                Excel.Range cell = (Excel.Range)ws.Cells[item.Row, item.Column];
                cell.Select();

                try
                {
                    Excel.Window? win = _excelApp.ActiveWindow;
                    if (win != null)
                    {
                        Excel.Range? vis = win.VisibleRange;
                        if (vis != null)
                        {
                            int topRow = vis.Row;
                            int botRow = topRow + Math.Max(1, vis.Rows.Count - 2);
                            int leftCol = vis.Column;
                            int rightCol = leftCol + Math.Max(1, vis.Columns.Count - 2);

                            if (item.Row < topRow || item.Row > botRow)
                                win.ScrollRow = Math.Max(1, item.Row - 3);
                            if (item.Column < leftCol || item.Column > rightCol)
                                win.ScrollColumn = Math.Max(1, item.Column - 1);
                        }
                    }
                }
                catch { }

                return true;
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                return false;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("SelectAndScrollToIssue", ex);
                return false;
            }
        }

        public bool ApplySingleFix(SpreadsheetCellIssue? item, string replacement, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (item == null || item.IsResolved || string.IsNullOrEmpty(replacement))
                return false;

            if (!IsExcelReadyForInterop(_excelApp))
            {
                errorMessage = UiLocalizer.Pick(
                    "⚠ Cannot apply fix while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return false;
            }

            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                if (wb == null)
                    return false;

                Excel.Worksheet? ws = FindWorksheetByName(wb, item.SheetName);
                if (ws == null)
                    return false;

                Excel.Range cell = (Excel.Range)ws.Cells[item.Row, item.Column];
                if (IsSingleCellFormula(cell))
                {
                    errorMessage = UiLocalizer.Pick(
                        $"⚠ {item.DisplayLocation} is a Formula cell and is automatically protected.",
                        $"⚠ {item.DisplayLocation} သည် Formula အကွက်ဖြစ်သဖြင့် တွက်ချက်မှုမပျက်စေရန် အလိုအလျောက် ကာကွယ်ထားပါသည်။");
                    return false;
                }

                string? liveText = cell.Value2 as string;
                if (string.IsNullOrEmpty(liveText))
                {
                    errorMessage = UiLocalizer.Pick(
                        $"⚠ Cell text in {item.DisplayLocation} has changed — please re-scan.",
                        $"⚠ {item.DisplayLocation} ရှိ စာသား ပြောင်းလဲသွားပြီဖြစ်သဖြင့် ပြန်လည်စစ်ဆေးပေးပါ။");
                    return false;
                }

                string wbName = SafeGetWorkbookName(wb);
                _issuesByWorkbook.TryGetValue(wbName, out var allItems);

                if (!SpreadsheetProofingHelper.ApplySingleIssueReplacement(
                        liveText!,
                        item,
                        replacement,
                        allItems,
                        out string updatedText))
                {
                    errorMessage = UiLocalizer.Pick(
                        $"⚠ Could not locate \"{item.Issue.OriginalText}\" inside {item.DisplayLocation}.",
                        $"⚠ {item.DisplayLocation} အတွင်း \"{item.Issue.OriginalText}\" နေရာကို ရှာမတွေ့တော့ပါ။");
                    return false;
                }

                StopActiveUnderlineJobOnly();
                WithExcelWriteGuard(() =>
                {
                    RestoreAndUntrackSingleCellIfMarked(wbName, cell, item.SheetName, item.Row, item.Column);
                    WriteSafePlainStringToCell(cell, updatedText);
                    SyncVisualUnderlinesForSingleCell(wbName, ws, cell, item.SheetName, item.Row, item.Column, updatedText, allItems);
                });

                _undoManager.PushBatch(new SpreadsheetUndoBatch
                {
                    Description = $"{item.DisplayLocation}: \"{item.Issue.OriginalText}\" ➔ \"{replacement}\"",
                    Entries = new List<CellUndoEntry>
                    {
                        new CellUndoEntry
                        {
                            WorkbookName = wbName,
                            SheetName = item.SheetName,
                            Row = item.Row,
                            Column = item.Column,
                            PreviousText = liveText!,
                            NewText = updatedText
                        }
                    }
                });

                _qualityByWorkbook.Remove(wbName);
                UndoStateChanged?.Invoke();
                return true;
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                errorMessage = UiLocalizer.Pick(
                    "⚠ Cannot apply fix while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return false;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ApplySingleFix", ex);
                errorMessage = UiLocalizer.Pick(
                    "⚠ Could not modify the cell (please check if the worksheet is protected).",
                    "⚠ အကွက်ပြင်ဆင်စဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည် (Sheet Lock / Protected Sheet ရှိမရှိ စစ်ဆေးပါ)။");
                return false;
            }
        }

        public int ApplyBatchFixAll(Func<SpreadsheetCellIssue, bool>? filterPredicate, string descriptionLabel, out string busyWarning)
        {
            busyWarning = string.Empty;
            if (!IsExcelReadyForInterop(_excelApp))
            {
                busyWarning = UiLocalizer.Pick(
                    "⚠ Cannot apply fixes while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return 0;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
                return 0;

            string wbName = SafeGetWorkbookName(wb);
            if (!_issuesByWorkbook.TryGetValue(wbName, out var allItems) || allItems.Count == 0)
                return 0;

            var candidates = filterPredicate != null
                ? allItems.Where(i => !i.IsResolved && filterPredicate(i)).ToList()
                : allItems.Where(i => !i.IsResolved).ToList();
            if (candidates.Count == 0)
                return 0;

            var cellReader = CreateFastLivePlainCellReader(wb, candidates);
            var ops = SpreadsheetProofingHelper.BuildBatchReplacementsByCell(
                allItems,
                filterPredicate,
                anchor => cellReader(anchor.SheetName, anchor.Row, anchor.Column));

            if (ops.Count == 0)
                return 0;

            int totalFixedIssues = ApplyCellWriteOperations(wb, wbName, ops, descriptionLabel, out busyWarning);
            if (totalFixedIssues > 0)
            {
                StartIncrementalUnderlineSync(wb, wbName, _scanGeneration, preserveSavedState: false);
            }
            return totalFixedIssues;
        }

        public int ApplyApprovedFixes(
            IReadOnlyDictionary<SpreadsheetCellIssue, string> approvedReplacements,
            string descriptionLabel,
            out string busyWarning)
        {
            busyWarning = string.Empty;
            if (approvedReplacements == null || approvedReplacements.Count == 0)
                return 0;

            if (!IsExcelReadyForInterop(_excelApp))
            {
                busyWarning = UiLocalizer.Pick(
                    "⚠ Cannot apply fixes while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return 0;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
                return 0;

            string wbName = SafeGetWorkbookName(wb);
            if (!_issuesByWorkbook.TryGetValue(wbName, out var allItems) || allItems.Count == 0)
                return 0;

            var cellReader = CreateFastLivePlainCellReader(wb, approvedReplacements.Keys);
            var ops = SpreadsheetProofingHelper.BuildApprovedReplacementsByCell(
                allItems,
                approvedReplacements,
                anchor => cellReader(anchor.SheetName, anchor.Row, anchor.Column));

            if (ops.Count == 0)
                return 0;

            int totalFixedIssues = ApplyCellWriteOperations(wb, wbName, ops, descriptionLabel, out busyWarning);
            if (totalFixedIssues > 0)
            {
                StartIncrementalUnderlineSync(wb, wbName, _scanGeneration, preserveSavedState: false);
            }
            return totalFixedIssues;
        }

        public int ApplySameWordBatchFix(string originalText, string replacementText, out string busyWarning)
        {
            busyWarning = string.Empty;
            if (!IsExcelReadyForInterop(_excelApp))
            {
                busyWarning = UiLocalizer.Pick(
                    "⚠ Cannot apply fixes while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return 0;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
                return 0;

            string wbName = SafeGetWorkbookName(wb);
            if (!_issuesByWorkbook.TryGetValue(wbName, out var allItems) || allItems.Count == 0)
                return 0;

            if (string.IsNullOrEmpty(originalText) || string.IsNullOrEmpty(replacementText))
                return 0;

            string targetNorm = UnicodeNormalizer.NormalizeCanonicalOrder(originalText.Trim());
            var matchingAnchors = allItems
                .Where(i => !i.IsResolved &&
                            string.Equals(UnicodeNormalizer.NormalizeCanonicalOrder((i.Issue.OriginalText ?? string.Empty).Trim()), targetNorm, StringComparison.Ordinal))
                .ToList();
            if (matchingAnchors.Count == 0)
                return 0;

            var cellReader = CreateFastLivePlainCellReader(wb, matchingAnchors);
            var ops = SpreadsheetProofingHelper.BuildSameWordBatchReplacementsByCell(
                allItems,
                originalText,
                replacementText,
                anchor => cellReader(anchor.SheetName, anchor.Row, anchor.Column));

            if (ops.Count == 0)
                return 0;

            int count = ApplyCellWriteOperations(
                wb,
                wbName,
                ops,
                UiLocalizer.Pick($"Same word \"{originalText}\" ➔ \"{replacementText}\"", $"တူညီအမှား \"{originalText}\" ➔ \"{replacementText}\""),
                out busyWarning);
            if (count > 0)
            {
                StartIncrementalUnderlineSync(wb, wbName, _scanGeneration, preserveSavedState: false);
            }
            return count;
        }

        public bool UndoLastAction(out string statusMessage)
        {
            statusMessage = string.Empty;
            if (!_undoManager.CanUndo)
            {
                statusMessage = UiLocalizer.Pick(
                    "No undo history available yet.",
                    "ပြန်ဆုတ်ရန် (Undo) မှတ်တမ်း မရှိသေးပါ။");
                return false;
            }

            if (!IsExcelReadyForInterop(_excelApp))
            {
                statusMessage = UiLocalizer.Pick(
                    "⚠ Cannot undo while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြန်ဆုတ်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return false;
            }

            SpreadsheetUndoBatch? batch = _undoManager.PopUndoBatch();
            if (batch == null || batch.Entries.Count == 0)
                return false;

            int restoredCells = 0;
            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                if (wb == null)
                    return false;

                StopActiveUnderlineJobOnly();
                var wsMap = BuildWorksheetMap(wb);

                WithExcelWriteGuard(() =>
                {
                    foreach (var group in batch.Entries.GroupBy(e => e.SheetName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!wsMap.TryGetValue(group.Key, out Excel.Worksheet? ws) || ws == null)
                            continue;

                        foreach (var entry in group)
                        {
                            try
                            {
                                Excel.Range cell = (Excel.Range)ws.Cells[entry.Row, entry.Column];
                                if (IsSingleCellFormula(cell))
                                    continue;

                                RestoreAndUntrackSingleCellIfMarked(entry.WorkbookName, cell, entry.SheetName, entry.Row, entry.Column);
                                WriteSafePlainStringToCell(cell, entry.PreviousText);
                                if (!string.IsNullOrEmpty(entry.PreviousFontName))
                                {
                                    try { cell.Font.Name = entry.PreviousFontName; } catch { }
                                }
                                restoredCells++;
                            }
                            catch { }
                        }
                    }
                }, suspendCalculation: batch.Entries.Count >= 4);

                UndoStateChanged?.Invoke();
                ScanScopeAsync(CurrentScope);
                statusMessage = UiLocalizer.Pick(
                    $"✓ Reverted changes ({restoredCells} cells — {batch.Description}).",
                    $"✓ မူလအတိုင်း ပြန်ဆုတ်ပြီးပါပြီ ({restoredCells} ကွက် — {batch.Description})။");
                return restoredCells > 0;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("UndoLastAction", ex);
                UndoStateChanged?.Invoke();
                statusMessage = UiLocalizer.Pick(
                    "⚠ An error occurred while reverting changes.",
                    "⚠ ပြန်ဆုတ်စဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။");
                return false;
            }
        }

        // ================= SPREADSHEET TOOLKIT OPERATIONS (FORMULA-SAFE + UNDOABLE) =================

        public ExcelToolkitOutcome ConvertLegacyFontsInScope(bool selectionOnly)
        {
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                "WinInnwa/Zawgyi ➔ Pyidaungsu",
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                {
                    // Inspect range-level Font.Name once if uniform; if null/DBNull (mixed fonts across cells), read cell font lazily only when needed
                    string? uniformFont = null;
                    bool fontIsMixed = false;
                    try
                    {
                        Excel.Range sub = ws.Range[ws.Cells[baseR, baseC], ws.Cells[baseR + rows - 1, baseC + cols - 1]];
                        object? rawFontName = sub.Font.Name;
                        if (rawFontName is string fn)
                            uniformFont = fn;
                        else
                            fontIsMixed = true;
                    }
                    catch
                    {
                        fontIsMixed = true;
                    }

                    return SpreadsheetProofingHelper.TransformBlockLegacyFontsToPyidaungsu(
                        wbName,
                        sheetName,
                        baseR,
                        baseC,
                        rows,
                        cols,
                        val2,
                        fml,
                        hasFml,
                        (r, c) =>
                        {
                            if (!fontIsMixed)
                                return uniformFont;
                            try
                            {
                                Excel.Range cell = (Excel.Range)ws.Cells[baseR + r, baseC + c];
                                return cell.Font.Name as string;
                            }
                            catch
                            {
                                return null;
                            }
                        });
                },
                (cells, changes) => cells > 0
                    ? UiLocalizer.Pick(
                        $"✓ Converted ({cells}) text cells to Pyidaungsu Unicode without touching formulas (↶ Undo available).",
                        $"✓ အကွက် ({cells}) ကွက်ကို Pyidaungsu Unicode သို့ Formula မထိခိုက်ဘဲ ပြောင်းလဲပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။")
                    : UiLocalizer.Pick(
                        "✓ No WinInnwa / Zawgyi text cells found (Formula cells automatically protected).",
                        "✓ ပြောင်းလဲရန် WinInnwa / Zawgyi စာသားကွက် မတွေ့ရှိပါ (Formula ကွက်များကို အလိုအလျောက် ချန်လှပ်ထားသည်)။"));
        }

        public ExcelToolkitOutcome FormatNumeralsInScope(bool toMyanmarDigits, bool selectionOnly)
        {
            string label = toMyanmarDigits
                ? UiLocalizer.Pick("0-9 ➔ ၀-၉ (Text Cells)", "0-9 ➔ ၀-၉ (စာသားကွက်)")
                : UiLocalizer.Pick("၀-၉ ➔ 0-9 (Text Cells)", "၀-၉ ➔ 0-9 (စာသားကွက်)");
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                label,
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                    SpreadsheetProofingHelper.TransformBlockNumerals(
                        wbName, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml, toMyanmarDigits),
                (cells, changes) => cells > 0
                    ? UiLocalizer.Pick(
                        $"✓ Converted ({changes}) digits across ({cells}) text cells — numeric & formula cells untouched (↶ Undo available).",
                        $"✓ စာသားကွက် ({cells}) ကွက်ရှိ ဂဏန်း ({changes}) လုံးကို ပြောင်းလဲပြီးပါပြီ — တွက်ချက်မှု ဂဏန်းကွက်နှင့် Formula များကို မထိခိုက်ပါ (↶ ပြန်ဆုတ်နိုင်သည်)။")
                    : UiLocalizer.Pick(
                        "✓ No Myanmar text cells with convertible digits found (numeric & formula cells protected).",
                        "✓ ပြောင်းလဲရန် မြန်မာစာသားပါ ဂဏန်းကွက် မတွေ့ရှိပါ (တွက်ချက်မှု ကိန်းဂဏန်းနှင့် Formula ကွက်များကို ကာကွယ်ထားသည်)။"));
        }

        public ExcelToolkitOutcome RewriteOfficialProseInScope(bool selectionOnly)
        {
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                UiLocalizer.Pick("Formal / Official Style Rewrite", "ရုံးသုံးစာပေဟန် ပြောင်းလဲမှု"),
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                    SpreadsheetProofingHelper.TransformBlockOfficialProse(
                        wbName, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml),
                (cells, changes) => cells > 0
                    ? UiLocalizer.Pick(
                        $"✓ Rewrote ({changes}) colloquial expressions across ({cells}) cells into formal/official style (↶ Undo available).",
                        $"✓ အကွက် ({cells}) ကွက်ရှိ စကားပြောဟန် ({changes}) နေရာကို ရုံးသုံး/စာပေဟန်သို့ ပြောင်းလဲပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။")
                    : UiLocalizer.Pick(
                        "✓ No colloquial expressions found — cells are already in formal/official style.",
                        "✓ ပြောင်းလဲရန် စကားပြောဟန် အသုံးအနှုန်း မတွေ့ရှိပါ (ရုံးသုံးစာပေဟန် အသင့်ဖြစ်ပြီးဖြစ်သည်)။"));
        }

        public ExcelToolkitOutcome FormatPunctuationInScope(bool selectionOnly)
        {
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                UiLocalizer.Pick("Punctuation ၊ / ။ & ZWSP Cleanup", "ပုဒ်ဖြတ် ၊ / ။ နှင့် ZWSP ရှင်းလင်းမှု"),
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                    SpreadsheetProofingHelper.TransformBlockPunctuation(
                        wbName, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml),
                (cells, changes) => cells > 0
                    ? UiLocalizer.Pick(
                        $"✓ Cleaned ({changes}) punctuation/ZWSP occurrences across ({cells}) cells (↶ Undo available).",
                        $"✓ အကွက် ({cells}) ကွက်ရှိ ပုဒ်ဖြတ်ပုဒ်ရပ်နှင့် ZWSP ({changes}) နေရာကို သန့်စင်ညှိနှိုင်းပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။")
                    : UiLocalizer.Pick(
                        "✓ No Myanmar punctuation (၊ / ။) or stray ZWSP issues found.",
                        "✓ ပြင်ဆင်ရန် ပုဒ်ဖြတ်ပုဒ်ရပ် (၊ / ။) သို့မဟုတ် ZWSP အမှား မတွေ့ရှိပါ။"));
        }

        public DocumentQualityReport BuildActiveWorkbookQualityReport(bool forceRefresh = false)
        {
            string wbKey = GetActiveWorkbookKey();
            if (!forceRefresh && !string.IsNullOrEmpty(wbKey) && _qualityByWorkbook.TryGetValue(wbKey, out var cached))
                return cached;

            string plainText = ExtractMyanmarCellsPlainTextFromActiveSheetOrWorkbook();
            IReadOnlyList<ProofingIssue>? unresolved = null;
            if (!string.IsNullOrEmpty(wbKey) && _issuesByWorkbook.TryGetValue(wbKey, out var cachedIssues))
            {
                unresolved = cachedIssues.Where(i => !i.IsResolved).Select(i => i.Issue).ToList();
            }

            DocumentQualityReport report = DocumentQualityAnalyzer.Analyze(plainText, unresolved, _engine);
            if (!string.IsNullOrEmpty(wbKey))
            {
                _qualityByWorkbook[wbKey] = report;
            }
            return report;
        }

        public bool GenerateAuditReportWorkbook(DocumentQualityReport report)
        {
            if (!IsExcelReadyForInterop(_excelApp))
                return false;

            try
            {
                string sourceName = GetActiveWorkbookKey();
                if (string.IsNullOrEmpty(sourceName))
                    sourceName = "Excel Workbook";

                IReadOnlyList<SpreadsheetCellIssue> cachedIssues = GetCachedIssuesForActiveWorkbook()
                    .Where(i => !i.IsResolved)
                    .Take(50)
                    .ToList();

                Excel.Workbook reportWb = _excelApp.Workbooks.Add();
                Excel.Worksheet ws = (Excel.Worksheet)reportWb.ActiveSheet;
                try { ws.Name = "မြန်မာစာ_စစ်ဆေးချက်"; } catch { }

                MyanmarDateInfo mmDate = MyanmarTraditionalCalendar.FromGregorian(DateTime.Today);

                var rows = new List<object?[]>
                {
                    new object?[] { "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံနှင့် အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Excel Edition)", "", "", "" },
                    new object?[] { "စစ်ဆေးသည့် ဖိုင်အမည်", sourceName, "စစ်ဆေးသည့် ရက်စွဲ", $"{mmDate.OfficialGregorianDateText} ({mmDate.OfficialMyanmarDateText})" },
                    new object?[] { "အရည်အသွေး အဆင့်သတ်မှတ်ချက်", $"{report.GradeLabel} ({report.OverallScore}%)", "Unicode စံနှုန်းသန့်စင်မှု", $"{report.UnicodePurityPercent:0.#}%" },
                    new object?[] { "ရုံးသုံးစာပေဟန် ကိုက်ညီမှု", $"{report.FormalRegisterPercent:0.#}%", "မြန်မာဝဏ္ဏ / စာလုံးရေ", $"{report.MyanmarSyllableCount:N0} ဝဏ္ဏ (~{report.WordCount:N0} လုံး)" },
                    new object?[] { "သတ်ပုံအမှား စုစုပေါင်း", report.SpellingIssueCount + report.EncodingIssueCount, "သဒ္ဒါ / အနက်သံသယ", $"{report.GrammarIssueCount} / {report.ContextWarningCount}" },
                    new object?[] { "", "", "", "" },
                    new object?[] { "အကွက်နေရာ (Sheet!Cell)", "အမျိုးအစား", "မူလစာသား", "အကြံပြုချက် နှင့် သတ်ပုံ/သဒ္ဒါ ရှင်းလင်းချက်" }
                };

                if (cachedIssues.Count == 0)
                {
                    rows.Add(new object?[] { "✓ အမှားကင်းစင်", "စံချိန်မီ", "—", "စစ်ဆေးထားသော အကွက်များတွင် သတ်ပုံနှင့် သဒ္ဒါ အမှားအယွင်း မတွေ့ရှိပါ။" });
                }
                else
                {
                    foreach (var item in cachedIssues)
                    {
                        string cat = item.Issue.IsContextualWarning
                            ? "အနက်သံသယ"
                            : (item.Issue.IsGrammarOrStyle ? "သဒ္ဒါ/စာဟန်" : "သတ်ပုံ");
                        string topSug = item.Issue.Suggestions.Count > 0 ? item.Issue.Suggestions[0].Replacement : "—";
                        rows.Add(new object?[]
                        {
                            item.DisplayLocation,
                            cat,
                            item.Issue.OriginalText,
                            $"➔ {topSug}  |  {item.Issue.Message}"
                        });
                    }
                }

                rows.Add(new object?[] { "", "", "", "" });
                rows.Add(new object?[]
                {
                    "စနစ်ဖန်တီးသူ မှတ်တမ်း",
                    "ထက်ရာဇာ (Htet Yar Zar)",
                    "ကျောက်ဆည်တက္ကသိုလ် (ဘူမိဗေဒဌာန)",
                    "M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Kyaukse, Mandalay Region, Myanmar • GNU GPL v3.0"
                });

                var grid = new object?[rows.Count, 4];
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        grid[r, c] = rows[r][c];
                    }
                }

                Excel.Range fullRange = ws.Range[ws.Cells[1, 1], ws.Cells[rows.Count, 4]];
                fullRange.Value2 = grid;
                fullRange.Font.Name = "Pyidaungsu";
                fullRange.Font.Size = 11;

                Excel.Range titleRange = ws.Range[ws.Cells[1, 1], ws.Cells[1, 4]];
                titleRange.Merge();
                titleRange.Font.Bold = true;
                titleRange.Font.Size = 13;

                Excel.Range headerRange = ws.Range[ws.Cells[7, 1], ws.Cells[7, 4]];
                headerRange.Font.Bold = true;

                ((Excel.Range)ws.Columns[1]).ColumnWidth = 22;
                ((Excel.Range)ws.Columns[2]).ColumnWidth = 20;
                ((Excel.Range)ws.Columns[3]).ColumnWidth = 24;
                ((Excel.Range)ws.Columns[4]).ColumnWidth = 62;
                fullRange.WrapText = true;

                return true;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("GenerateAuditReportWorkbook", ex);
                return false;
            }
        }

        public string TryGetSelectedCellText(int maxChars = 80)
        {
            if (!IsExcelReadyForInterop(_excelApp))
                return string.Empty;

            try
            {
                Excel.Range? cell = _excelApp.ActiveCell;
                if (cell == null)
                    return string.Empty;

                string? raw = cell.Value2 as string;
                if (string.IsNullOrWhiteSpace(raw))
                    return string.Empty;

                string cleaned = raw!.Trim(' ', '\t', '\r', '\n', '၊', '။', '"', '\'', '“', '”', '(', ')');
                return cleaned.Length <= maxChars ? cleaned : cleaned.Substring(0, maxChars).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public string TryGetSelectedNumericText()
        {
            if (!IsExcelReadyForInterop(_excelApp))
                return string.Empty;

            try
            {
                Excel.Range? cell = _excelApp.ActiveCell;
                if (cell == null)
                    return string.Empty;

                object? v = cell.Value2;
                if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                {
                    return Math.Abs(d % 1) < 0.000001
                        ? ((long)Math.Round(d)).ToString("N0", CultureInfo.InvariantCulture)
                        : d.ToString("N2", CultureInfo.InvariantCulture);
                }

                if (v is string s && MyanmarNumberToWordsConverter.TryExtractNumericSelection(s, out string tok))
                {
                    return tok;
                }
            }
            catch { }

            return string.Empty;
        }

        public bool TryInsertTextIntoActiveCell(string text, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (string.IsNullOrEmpty(text))
                return false;

            if (!IsExcelReadyForInterop(_excelApp))
            {
                statusMessage = UiLocalizer.Pick(
                    "⚠ Cannot insert text while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ထည့်သွင်း၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return false;
            }

            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                Excel.Range? cell = _excelApp.ActiveCell;
                if (wb == null || cell == null)
                    return false;

                if (IsSingleCellFormula(cell))
                {
                    statusMessage = UiLocalizer.Pick(
                        "⚠ Active cell contains a Formula and is protected (please select an empty/text cell).",
                        "⚠ လက်ရှိအကွက်သည် Formula အကွက်ဖြစ်သဖြင့် မပျက်စီးစေရန် ကာကွယ်ထားပါသည် (အခြားအကွက်လွတ် ရွေးချယ်ပါ)။");
                    return false;
                }

                string wbName = SafeGetWorkbookName(wb);
                string sheetName = SafeGetSheetName(cell.Worksheet);
                int row = cell.Row;
                int col = cell.Column;
                string prevText = Convert.ToString(cell.Value2, CultureInfo.InvariantCulture) ?? string.Empty;
                string newText = string.IsNullOrWhiteSpace(prevText) ? text : (prevText + " " + text);

                WithExcelWriteGuard(() =>
                {
                    WriteSafePlainStringToCell(cell, newText);
                    try { cell.Font.Name = "Pyidaungsu"; } catch { }
                });

                _undoManager.PushBatch(new SpreadsheetUndoBatch
                {
                    Description = UiLocalizer.Pick(
                        $"Insert text into {sheetName}!{SpreadsheetProofingHelper.ColumnIndexToLetters(col)}{row}",
                        $"{sheetName}!{SpreadsheetProofingHelper.ColumnIndexToLetters(col)}{row} သို့ စာသားထည့်သွင်းမှု"),
                    Entries = new List<CellUndoEntry>
                    {
                        new CellUndoEntry
                        {
                            WorkbookName = wbName,
                            SheetName = sheetName,
                            Row = row,
                            Column = col,
                            PreviousText = prevText,
                            NewText = newText
                        }
                    }
                });

                UndoStateChanged?.Invoke();
                statusMessage = UiLocalizer.Pick(
                    $"✓ Inserted into {sheetName}!{SpreadsheetProofingHelper.ColumnIndexToLetters(col)}{row} (↶ Undo available).",
                    $"✓ {sheetName}!{SpreadsheetProofingHelper.ColumnIndexToLetters(col)}{row} အကွက်သို့ ထည့်သွင်းပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။");
                return true;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("TryInsertTextIntoActiveCell", ex);
                statusMessage = UiLocalizer.Pick(
                    "⚠ Could not insert text into the active cell.",
                    "⚠ အကွက်အတွင်း ထည့်သွင်းစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။");
                return false;
            }
        }

        // ================= PRIVATE BULK 2D INTEROP HELPERS =================

        private ExcelToolkitOutcome ExecuteBlockToolkitTransform(
            bool selectionOnly,
            string undoTitle,
            Func<string, Excel.Worksheet, string, int, int, int, int, object?, object?, bool?, List<CellWriteOperation>> blockProcessor,
            Func<int, int, string> messageFormatter)
        {
            if (!IsExcelReadyForInterop(_excelApp))
            {
                return new ExcelToolkitOutcome
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick(
                        "⚠ Cannot run toolkit while editing an Excel cell — press Enter or Esc first.",
                        "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် အသုံးပြု၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။")
                };
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
            {
                return new ExcelToolkitOutcome
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick(
                        "⚠ No active Excel workbook is currently open.",
                        "⚠ လက်ရှိ ဖွင့်ထားသော Excel Workbook မရှိသေးပါ။")
                };
            }

            string wbName = SafeGetWorkbookName(wb);
            var allOps = new List<CellWriteOperation>();

            try
            {
                Excel.Range? targetRange = null;
                Excel.Worksheet? ws = null;

                if (selectionOnly)
                {
                    targetRange = _excelApp.Selection as Excel.Range;
                    ws = targetRange?.Worksheet;
                }
                else
                {
                    ws = wb.ActiveSheet as Excel.Worksheet;
                    targetRange = ws != null ? SafeGetUsedRange(ws) : null;
                }

                if (ws == null || targetRange == null)
                {
                    return new ExcelToolkitOutcome
                    {
                        Success = true,
                        UserMessage = messageFormatter(0, 0)
                    };
                }

                Excel.Range? used = SafeGetUsedRange(ws);
                Excel.Range? effective = used != null ? SafeIntersect(targetRange, used) : targetRange;
                if (effective == null)
                {
                    return new ExcelToolkitOutcome
                    {
                        Success = true,
                        UserMessage = messageFormatter(0, 0)
                    };
                }

                string sheetName = SafeGetSheetName(ws);
                int budget = MaxCellsPerFullScan;

                foreach (Excel.Range area in effective.Areas)
                {
                    if (budget <= 0)
                        break;

                    int areaRow = area.Row;
                    int areaCol = area.Column;
                    int areaRows = area.Rows.Count;
                    int areaCols = area.Columns.Count;

                    for (int rOffset = 0; rOffset < areaRows && budget > 0; rOffset += MaxRowsPerChunk)
                    {
                        int chunkRows = Math.Min(MaxRowsPerChunk, areaRows - rOffset);
                        for (int cOffset = 0; cOffset < areaCols && budget > 0; cOffset += MaxColsPerChunk)
                        {
                            int chunkCols = Math.Min(MaxColsPerChunk, areaCols - cOffset);
                            budget -= chunkRows * chunkCols;

                            int baseR = areaRow + rOffset;
                            int baseC = areaCol + cOffset;
                            Excel.Range chunk = (chunkRows == areaRows && chunkCols == areaCols)
                                ? area
                                : ws.Range[ws.Cells[baseR, baseC], ws.Cells[baseR + chunkRows - 1, baseC + chunkCols - 1]];

                            bool? hasFormula = ReadRangeHasFormulaTriState(chunk);
                            if (hasFormula == true)
                                continue;

                            object? val2 = chunk.Value2;
                            if (val2 == null)
                                continue;

                            object? fml = hasFormula == null ? chunk.Formula : null;
                            var chunkOps = blockProcessor(wbName, ws, sheetName, baseR, baseC, chunkRows, chunkCols, val2, fml, hasFormula);
                            if (chunkOps.Count > 0)
                            {
                                allOps.AddRange(chunkOps);
                            }
                        }
                    }
                }

                if (allOps.Count == 0)
                {
                    return new ExcelToolkitOutcome
                    {
                        Success = true,
                        CellsModified = 0,
                        TotalChanges = 0,
                        UserMessage = messageFormatter(0, 0)
                    };
                }

                int totalChanges = ApplyCellWriteOperations(wb, wbName, allOps, undoTitle, out string busyWarn);
                if (!string.IsNullOrEmpty(busyWarn))
                {
                    return new ExcelToolkitOutcome
                    {
                        Success = false,
                        UserMessage = busyWarn
                    };
                }

                ScanScopeAsync(CurrentScope);
                return new ExcelToolkitOutcome
                {
                    Success = true,
                    CellsModified = allOps.Count,
                    TotalChanges = totalChanges,
                    UserMessage = messageFormatter(allOps.Count, totalChanges)
                };
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                return new ExcelToolkitOutcome
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick(
                        "⚠ Cannot run toolkit while editing an Excel cell — press Enter or Esc first.",
                        "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် အသုံးပြု၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။")
                };
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ExecuteBlockToolkitTransform", ex);
                return new ExcelToolkitOutcome
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick(
                        "⚠ Could not apply transformation (please check if the worksheet is protected).",
                        "⚠ ပြောင်းလဲစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည် (Protected Sheet ဖြစ်နေခြင်း ရှိမရှိ စစ်ဆေးပါ)။")
                };
            }
        }

        private static bool IsSafeForBulk2DValueWrite(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            char first = text![0];
            if (first == '=' || first == '+' || first == '-' || first == '@' || first == '\'' || first == '0')
                return false;

            // Ensure strings containing Myanmar script aren't parsed as numbers/dates by Excel Value2
            return SpreadsheetProofingHelper.ContainsMyanmarScript(text);
        }

        private int ApplyCellWriteOperations(
            Excel.Workbook wb,
            string wbName,
            List<CellWriteOperation> ops,
            string undoDescription,
            out string busyWarning)
        {
            busyWarning = string.Empty;
            var executedOps = new List<CellWriteOperation>(ops.Count);
            int totalChanges = 0;

            try
            {
                StopActiveUnderlineJobOnly();
                var wsMap = BuildWorksheetMap(wb);
                var pumpWatch = Stopwatch.StartNew();
                int processedSoFar = 0;

                WithExcelWriteGuard(() =>
                {
                    foreach (var sheetGroup in ops.GroupBy(o => o.SheetName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!wsMap.TryGetValue(sheetGroup.Key, out Excel.Worksheet? ws) || ws == null)
                            continue;

                        bool isProtected = false;
                        try { isProtected = ws.ProtectContents; } catch { }

                        var sheetOps = sheetGroup
                            .OrderBy(o => o.Row)
                            .ThenBy(o => o.Column)
                            .ToList();

                        // Restore any underlined cells among target ops upfront (bounded to <= 25..120 marked cells)
                        if (_markedCellsByWorkbook.TryGetValue(wbName, out var sheetMarks) && sheetMarks.Count > 0)
                        {
                            for (int m = 0; m < sheetOps.Count; m++)
                            {
                                var mop = sheetOps[m];
                                if (sheetMarks.ContainsKey(MakeCellKey(mop.SheetName, mop.Row, mop.Column)))
                                {
                                    try
                                    {
                                        Excel.Range markedCell = (Excel.Range)ws.Cells[mop.Row, mop.Column];
                                        RestoreAndUntrackSingleCellIfMarked(wbName, markedCell, mop.SheetName, mop.Row, mop.Column);
                                    }
                                    catch { }
                                }
                            }
                        }

                        const int BandRowHeight = 200;
                        int idx = 0;
                        while (idx < sheetOps.Count)
                        {
                            int startRow = sheetOps[idx].Row;
                            int endIdx = idx;
                            int minR = startRow, maxR = startRow;
                            int minC = sheetOps[idx].Column, maxC = sheetOps[idx].Column;
                            bool allBulkSafe = !isProtected;
                            string? sharedFont = sheetOps[idx].TargetFontName;
                            bool uniformFont = true;

                            while (endIdx < sheetOps.Count && (sheetOps[endIdx].Row - startRow) < BandRowHeight)
                            {
                                var cur = sheetOps[endIdx];
                                if (cur.Row < minR) minR = cur.Row;
                                if (cur.Row > maxR) maxR = cur.Row;
                                if (cur.Column < minC) minC = cur.Column;
                                if (cur.Column > maxC) maxC = cur.Column;
                                if (!IsSafeForBulk2DValueWrite(cur.UpdatedText))
                                    allBulkSafe = false;
                                if (!string.Equals(cur.TargetFontName ?? string.Empty, sharedFont ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                    uniformFont = false;
                                endIdx++;
                            }

                            int bandCount = endIdx - idx;
                            int boxRows = maxR - minR + 1;
                            int boxCols = maxC - minC + 1;
                            long boxArea = (long)boxRows * boxCols;
                            bool fontSafeForBox = string.IsNullOrEmpty(sharedFont) || (uniformFont && boxArea == bandCount);
                            bool bulkApplied = false;

                            // Fast 1-COM-Call Bulk 2D SAFEARRAY Write when the entire band is 100% formula-free!
                            if (allBulkSafe &&
                                fontSafeForBox &&
                                bandCount >= 8 &&
                                (boxRows > 1 || boxCols > 1) &&
                                boxArea <= (long)bandCount * 24 &&
                                boxArea <= 32_000)
                            {
                                try
                                {
                                    Excel.Range bandRange = ws.Range[ws.Cells[minR, minC], ws.Cells[maxR, maxC]];
                                    if (ReadRangeHasFormulaTriState(bandRange) == false &&
                                        bandRange.Value2 is object?[,] bandArr)
                                    {
                                        int rLb = bandArr.GetLowerBound(0);
                                        int cLb = bandArr.GetLowerBound(1);
                                        for (int k = idx; k < endIdx; k++)
                                        {
                                            var bop = sheetOps[k];
                                            bandArr[rLb + (bop.Row - minR), cLb + (bop.Column - minC)] = bop.UpdatedText;
                                        }

                                        bandRange.Value2 = bandArr;
                                        if (!string.IsNullOrEmpty(sharedFont) && boxArea == bandCount)
                                        {
                                            try { bandRange.Font.Name = sharedFont; } catch { }
                                        }

                                        for (int k = idx; k < endIdx; k++)
                                        {
                                            var bop = sheetOps[k];
                                            executedOps.Add(bop);
                                            totalChanges += Math.Max(1, bop.ChangesInCell);
                                        }
                                        processedSoFar += bandCount;
                                        bulkApplied = true;
                                    }
                                }
                                catch
                                {
                                    bulkApplied = false;
                                }
                            }

                            if (!bulkApplied)
                            {
                                for (int k = idx; k < endIdx; k++)
                                {
                                    var op = sheetOps[k];
                                    Excel.Range cell = (Excel.Range)ws.Cells[op.Row, op.Column];
                                    // Final safety gate: never overwrite a formula cell!
                                    if (IsSingleCellFormula(cell))
                                        continue;

                                    RestoreAndUntrackSingleCellIfMarked(wbName, cell, op.SheetName, op.Row, op.Column);
                                    WriteSafePlainStringToCell(cell, op.UpdatedText);
                                    if (!string.IsNullOrEmpty(op.TargetFontName))
                                    {
                                        try { cell.Font.Name = op.TargetFontName; } catch { }
                                    }

                                    executedOps.Add(op);
                                    totalChanges += Math.Max(1, op.ChangesInCell);
                                    processedSoFar++;

                                    if (pumpWatch.ElapsedMilliseconds >= 110)
                                    {
                                        pumpWatch.Restart();
                                        int pct = Math.Min(98, (processedSoFar * 95) / Math.Max(1, ops.Count));
                                        ScanProgressChanged?.Invoke(
                                            pct,
                                            UiLocalizer.Pick(
                                                $"⏳ Updating cells... {processedSoFar:N0}/{ops.Count:N0}",
                                                $"⏳ အကွက်များ ပြင်ဆင်နေသည်... {processedSoFar:N0}/{ops.Count:N0}"));
                                        Application.DoEvents();
                                    }
                                }
                            }
                            else if (pumpWatch.ElapsedMilliseconds >= 110)
                            {
                                pumpWatch.Restart();
                                int pct = Math.Min(98, (processedSoFar * 95) / Math.Max(1, ops.Count));
                                ScanProgressChanged?.Invoke(
                                    pct,
                                    UiLocalizer.Pick(
                                        $"⏳ Updating cells... {processedSoFar:N0}/{ops.Count:N0}",
                                        $"⏳ အကွက်များ ပြင်ဆင်နေသည်... {processedSoFar:N0}/{ops.Count:N0}"));
                                Application.DoEvents();
                            }

                            idx = endIdx;
                        }
                    }
                }, suspendCalculation: ops.Count >= 4);

                if (executedOps.Count > 0)
                {
                    _undoManager.PushOperations(
                        UiLocalizer.Pick($"{undoDescription} ({executedOps.Count} cells)", $"{undoDescription} ({executedOps.Count} ကွက်)"),
                        executedOps);
                    _qualityByWorkbook.Remove(wbName);
                    UndoStateChanged?.Invoke();
                }

                return totalChanges;
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                busyWarning = UiLocalizer.Pick(
                    "⚠ Cannot apply fixes while editing an Excel cell — press Enter or Esc first.",
                    "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။");
                return totalChanges;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ApplyCellWriteOperations", ex);
                busyWarning = UiLocalizer.Pick(
                    "⚠ Could not update cells (please check if the worksheet is protected).",
                    "⚠ အကွက်များ ပြင်ဆင်စဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည် (Protected Sheet ရှိမရှိ စစ်ဆေးပါ)။");
                return totalChanges;
            }
        }

        private void ScanWorksheetRangeReadOnly(
            string wbName,
            Excel.Worksheet ws,
            Excel.Range scopeRange,
            Dictionary<string, IReadOnlyList<ProofingIssue>> sharedMemo,
            List<SpreadsheetCellIssue> output,
            ref int cellBudgetRemaining)
        {
            Excel.Range? used = SafeGetUsedRange(ws);
            if (used == null)
                return;

            Excel.Range? effective = SafeIntersect(scopeRange, used);
            if (effective == null)
                return;

            string sheetName = SafeGetSheetName(ws);

            foreach (Excel.Range area in effective.Areas)
            {
                if (cellBudgetRemaining <= 0)
                    break;

                int areaRow = area.Row;
                int areaCol = area.Column;
                int areaRows = area.Rows.Count;
                int areaCols = area.Columns.Count;

                for (int rOffset = 0; rOffset < areaRows && cellBudgetRemaining > 0; rOffset += MaxRowsPerChunk)
                {
                    int chunkRows = Math.Min(MaxRowsPerChunk, areaRows - rOffset);
                    for (int cOffset = 0; cOffset < areaCols && cellBudgetRemaining > 0; cOffset += MaxColsPerChunk)
                    {
                        int chunkCols = Math.Min(MaxColsPerChunk, areaCols - cOffset);
                        cellBudgetRemaining -= chunkRows * chunkCols;

                        int baseR = areaRow + rOffset;
                        int baseC = areaCol + cOffset;
                        Excel.Range chunk = (chunkRows == areaRows && chunkCols == areaCols)
                            ? area
                            : ws.Range[ws.Cells[baseR, baseC], ws.Cells[baseR + chunkRows - 1, baseC + chunkCols - 1]];

                        bool? hasFormula = ReadRangeHasFormulaTriState(chunk);
                        if (hasFormula == true)
                            continue; // 100% formula block — skip immediately in 1 COM call!

                        object? val2 = chunk.Value2;
                        if (val2 == null)
                            continue;

                        // Read chunk.Formula ONLY when chunk has mixed formulas + literals (hasFormula == null)
                        object? fml = hasFormula == null ? chunk.Formula : null;

                        var hits = SpreadsheetProofingHelper.ScanBlockValues(
                            _engine,
                            wbName,
                            sheetName,
                            baseR,
                            baseC,
                            chunkRows,
                            chunkCols,
                            val2,
                            fml,
                            hasFormula,
                            sharedMemo);

                        if (hits.Count > 0)
                        {
                            output.AddRange(hits);
                        }
                    }
                }
            }
        }

        private string ExtractMyanmarCellsPlainTextFromActiveSheetOrWorkbook()
        {
            if (!IsExcelReadyForInterop(_excelApp))
                return string.Empty;

            var sb = new StringBuilder(4096);
            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                if (wb == null)
                    return string.Empty;

                var sheets = new List<Excel.Worksheet>();
                if (CurrentScope == SpreadsheetScanScope.EntireWorkbook)
                {
                    foreach (object raw in wb.Worksheets)
                    {
                        if (raw is Excel.Worksheet s)
                            sheets.Add(s);
                    }
                }
                else if (wb.ActiveSheet is Excel.Worksheet activeWs)
                {
                    sheets.Add(activeWs);
                }

                int budget = 50_000;
                foreach (var ws in sheets)
                {
                    if (budget <= 0)
                        break;

                    Excel.Range? used = SafeGetUsedRange(ws);
                    if (used == null)
                        continue;

                    int rows = Math.Min(MaxRowsPerChunk, used.Rows.Count);
                    int cols = Math.Min(MaxColsPerChunk, used.Columns.Count);
                    budget -= rows * cols;

                    Excel.Range sub = (rows == used.Rows.Count && cols == used.Columns.Count)
                        ? used
                        : ws.Range[ws.Cells[used.Row, used.Column], ws.Cells[used.Row + rows - 1, used.Column + cols - 1]];

                    bool? hasFml = ReadRangeHasFormulaTriState(sub);
                    if (hasFml == true)
                        continue;

                    object? val2 = sub.Value2;
                    if (val2 is string scalarStr)
                    {
                        if (SpreadsheetProofingHelper.ContainsMyanmarScript(scalarStr))
                            sb.AppendLine(scalarStr);
                    }
                    else if (val2 is object?[,] arr)
                    {
                        int rLb = arr.GetLowerBound(0);
                        int cLb = arr.GetLowerBound(1);
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                if (arr[rLb + r, cLb + c] is string s && SpreadsheetProofingHelper.ContainsMyanmarScript(s))
                                {
                                    sb.AppendLine(s);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return sb.ToString();
        }

        private static Dictionary<string, Excel.Worksheet> BuildWorksheetMap(Excel.Workbook wb)
        {
            var map = new Dictionary<string, Excel.Worksheet>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (object raw in wb.Worksheets)
                {
                    if (raw is Excel.Worksheet ws)
                    {
                        string name = SafeGetSheetName(ws);
                        if (!string.IsNullOrEmpty(name) && !map.ContainsKey(name))
                            map[name] = ws;
                    }
                }
            }
            catch { }
            return map;
        }

        private static Func<string, int, int, string?> CreateFastLivePlainCellReader(
            Excel.Workbook wb,
            IEnumerable<SpreadsheetCellIssue> candidateAnchors)
        {
            var wsMap = BuildWorksheetMap(wb);
            var prefetched = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            // Group unique target cell coordinates by sheet so dense sheets are prefetched in 2D SAFEARRAY blocks!
            foreach (var sheetGroup in candidateAnchors
                .Select(a => (Sheet: a.SheetName ?? string.Empty, a.Row, a.Column))
                .Distinct()
                .GroupBy(x => x.Sheet, StringComparer.OrdinalIgnoreCase))
            {
                if (!wsMap.TryGetValue(sheetGroup.Key, out Excel.Worksheet? ws) || ws == null)
                    continue;

                var coords = sheetGroup.ToList();
                if (coords.Count >= 16)
                {
                    int minR = int.MaxValue, maxR = int.MinValue;
                    int minC = int.MaxValue, maxC = int.MinValue;
                    for (int i = 0; i < coords.Count; i++)
                    {
                        var c = coords[i];
                        if (c.Row < minR) minR = c.Row;
                        if (c.Row > maxR) maxR = c.Row;
                        if (c.Column < minC) minC = c.Column;
                        if (c.Column > maxC) maxC = c.Column;
                    }

                    long boxArea = (long)(maxR - minR + 1) * (maxC - minC + 1);
                    if (boxArea > 0 && boxArea <= 250_000 && boxArea <= (long)coords.Count * 80)
                    {
                        try
                        {
                            int rows = maxR - minR + 1;
                            int cols = maxC - minC + 1;
                            Excel.Range box = (rows == 1 && cols == 1)
                                ? (Excel.Range)ws.Cells[minR, minC]
                                : ws.Range[ws.Cells[minR, minC], ws.Cells[maxR, maxC]];

                            bool? hasFml = ReadRangeHasFormulaTriState(box);
                            if (hasFml == true)
                            {
                                for (int i = 0; i < coords.Count; i++)
                                    prefetched[MakeCellKey(sheetGroup.Key, coords[i].Row, coords[i].Column)] = null;
                                continue;
                            }

                            object? val2 = box.Value2;
                            object? fml = hasFml == null ? box.Formula : null;

                            if (rows == 1 && cols == 1)
                            {
                                bool isFml = fml is string fs && fs.StartsWith("=", StringComparison.Ordinal);
                                prefetched[MakeCellKey(sheetGroup.Key, minR, minC)] = isFml ? null : (val2 as string);
                                continue;
                            }

                            if (val2 is object?[,] vArr)
                            {
                                object?[,]? fArr = fml as object?[,];
                                int vRLb = vArr.GetLowerBound(0), vCLb = vArr.GetLowerBound(1);
                                int fRLb = fArr != null ? fArr.GetLowerBound(0) : 0;
                                int fCLb = fArr != null ? fArr.GetLowerBound(1) : 0;

                                for (int i = 0; i < coords.Count; i++)
                                {
                                    int rOff = coords[i].Row - minR;
                                    int cOff = coords[i].Column - minC;
                                    bool cellHasFml = fArr != null &&
                                                      fArr[fRLb + rOff, fCLb + cOff] is string fs &&
                                                      fs.StartsWith("=", StringComparison.Ordinal);
                                    string? plain = cellHasFml ? null : (vArr[vRLb + rOff, vCLb + cOff] as string);
                                    prefetched[MakeCellKey(sheetGroup.Key, coords[i].Row, coords[i].Column)] = plain;
                                }
                                continue;
                            }
                        }
                        catch
                        {
                            // Fall back to cached single-cell reads below
                        }
                    }
                }
            }

            return (sheetName, row, col) =>
            {
                string key = MakeCellKey(sheetName, row, col);
                if (prefetched.TryGetValue(key, out string? cachedVal))
                    return cachedVal;

                string? val = null;
                try
                {
                    if (wsMap.TryGetValue(sheetName ?? string.Empty, out Excel.Worksheet? ws) && ws != null)
                    {
                        Excel.Range cell = (Excel.Range)ws.Cells[row, col];
                        if (!IsSingleCellFormula(cell))
                            val = cell.Value2 as string;
                    }
                }
                catch { }

                prefetched[key] = val;
                return val;
            };
        }

        private void WithExcelWriteGuard(Action writeAction, bool suspendCalculation = false)
        {
            bool prevScreenUpdating = true;
            bool prevEnableEvents = true;
            bool capturedState = false;
            Excel.XlCalculation prevCalculation = Excel.XlCalculation.xlCalculationAutomatic;
            bool changedCalculation = false;

            _suppressSheetChangeEvents++;
            try
            {
                try
                {
                    prevScreenUpdating = _excelApp.ScreenUpdating;
                    prevEnableEvents = _excelApp.EnableEvents;
                    capturedState = true;
                    _excelApp.ScreenUpdating = false;
                    _excelApp.EnableEvents = false;

                    if (suspendCalculation)
                    {
                        prevCalculation = _excelApp.Calculation;
                        if (prevCalculation != Excel.XlCalculation.xlCalculationManual)
                        {
                            _excelApp.Calculation = Excel.XlCalculation.xlCalculationManual;
                            changedCalculation = true;
                        }
                    }
                }
                catch { }

                writeAction();
            }
            finally
            {
                if (changedCalculation)
                {
                    try { _excelApp.Calculation = prevCalculation; } catch { }
                }
                if (capturedState)
                {
                    try { _excelApp.EnableEvents = prevEnableEvents; } catch { }
                    try { _excelApp.ScreenUpdating = prevScreenUpdating; } catch { }
                }
                _suppressSheetChangeEvents = Math.Max(0, _suppressSheetChangeEvents - 1);
            }
        }

        private static void WriteSafePlainStringToCell(Excel.Range cell, string text)
        {
            if (text == null)
                text = string.Empty;

            // Guard against accidental Excel formula interpretation if edited text begins with '=', '+', '-', or '@'
            if (text.Length > 0 && (text[0] == '=' || text[0] == '+' || text[0] == '-' || text[0] == '@'))
            {
                cell.Value2 = "'" + text;
            }
            else
            {
                cell.Value2 = text;
            }
        }

        private static bool? ReadRangeHasFormulaTriState(Excel.Range range)
        {
            try
            {
                object? hf = range.HasFormula;
                if (hf is bool b)
                    return b;
                return null; // DBNull.Value or null indicates mixed formula + non-formula cells in range
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSingleCellFormula(Excel.Range cell)
        {
            try
            {
                object? hf = cell.HasFormula;
                if (hf is bool b)
                    return b;

                if (cell.Formula is string f && f.StartsWith("=", StringComparison.Ordinal))
                    return true;
            }
            catch
            {
                return true; // Fail closed: if Excel cannot confirm non-formula, never overwrite!
            }
            return false;
        }

        private static Excel.Worksheet? FindWorksheetByName(Excel.Workbook wb, string sheetName)
        {
            if (string.IsNullOrEmpty(sheetName))
                return wb.ActiveSheet as Excel.Worksheet;

            try
            {
                foreach (object raw in wb.Worksheets)
                {
                    if (raw is Excel.Worksheet ws &&
                        string.Equals(SafeGetSheetName(ws), sheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return ws;
                    }
                }
            }
            catch { }

            return null;
        }

        private Excel.Range? SafeIntersect(Excel.Range a, Excel.Range b)
        {
            try
            {
                return _excelApp.Intersect(a, b);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the worksheet's UsedRange clamped to actual last non-blank formula/value row and column
        /// so full-column/full-row formatting (1,048,576 empty rows) never triggers million-cell scans!
        /// </summary>
        private static Excel.Range? SafeGetUsedRange(Excel.Worksheet ws)
        {
            Excel.Range? used;
            try { used = ws.UsedRange; } catch { return null; }
            if (used == null)
                return null;

            long rawCells = SafeGetCountLarge(used);
            if (rawCells <= 2_500)
                return used;

            try
            {
                object missing = Type.Missing;
                Excel.Range anchor = (Excel.Range)ws.Cells[1, 1];
                Excel.Range? lastRowCell = ws.Cells.Find(
                    What: "*",
                    After: anchor,
                    LookIn: Excel.XlFindLookIn.xlFormulas,
                    LookAt: Excel.XlLookAt.xlPart,
                    SearchOrder: Excel.XlSearchOrder.xlByRows,
                    SearchDirection: Excel.XlSearchDirection.xlPrevious,
                    MatchCase: false,
                    MatchByte: false,
                    SearchFormat: missing);

                if (lastRowCell == null)
                    return null; // 100% empty formatted sheet!

                Excel.Range? lastColCell = ws.Cells.Find(
                    What: "*",
                    After: anchor,
                    LookIn: Excel.XlFindLookIn.xlFormulas,
                    LookAt: Excel.XlLookAt.xlPart,
                    SearchOrder: Excel.XlSearchOrder.xlByColumns,
                    SearchDirection: Excel.XlSearchDirection.xlPrevious,
                    MatchCase: false,
                    MatchByte: false,
                    SearchFormat: missing);

                if (lastColCell == null)
                    return null;

                int startR = used.Row;
                int startC = used.Column;
                int endR = Math.Min(startR + used.Rows.Count - 1, lastRowCell.Row);
                int endC = Math.Min(startC + used.Columns.Count - 1, lastColCell.Column);

                if (endR < startR || endC < startC)
                    return null;

                if (endR < startR + used.Rows.Count - 1 || endC < startC + used.Columns.Count - 1)
                {
                    return ws.Range[ws.Cells[startR, startC], ws.Cells[endR, endC]];
                }
            }
            catch
            {
                // Protected / special sheet where Cells.Find is disallowed — fall back to UsedRange
            }

            return used;
        }

        private static long SafeGetCountLarge(Excel.Range range)
        {
            try
            {
                object? cl = range.CountLarge;
                if (cl is long l) return l;
                if (cl is int i) return i;
                if (cl is double d) return (long)d;
                return Convert.ToInt64(cl, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private string GetActiveWorkbookKey()
        {
            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                return wb != null ? SafeGetWorkbookName(wb) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeGetWorkbookName(Excel.Workbook wb)
        {
            try { return wb.Name ?? "Workbook"; } catch { return "Workbook"; }
        }

        private static string SafeGetSheetName(Excel.Worksheet ws)
        {
            try { return ws.Name ?? "Sheet1"; } catch { return "Sheet1"; }
        }

        private static int CompareIssuesByLocation(SpreadsheetCellIssue a, SpreadsheetCellIssue b)
        {
            int cmpSheet = string.Compare(a.SheetName, b.SheetName, StringComparison.OrdinalIgnoreCase);
            if (cmpSheet != 0) return cmpSheet;
            int cmpRow = a.Row.CompareTo(b.Row);
            if (cmpRow != 0) return cmpRow;
            int cmpCol = a.Column.CompareTo(b.Column);
            if (cmpCol != 0) return cmpCol;
            return a.Issue.StartIndex.CompareTo(b.Issue.StartIndex);
        }

        // ================= SAVE/PRINT-SAFE IN-CELL VISUAL UNDERLINE MANAGER =================

        public void RefreshVisualUnderlinesForActiveWorkbook()
        {
            if (!IsExcelReadyForInterop(_excelApp))
                return;

            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                if (wb == null)
                    return;

                string wbName = SafeGetWorkbookName(wb);
                StartIncrementalUnderlineSync(wb, wbName, _scanGeneration, preserveSavedState: true);
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                // Ignore busy state
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("RefreshVisualUnderlinesForActiveWorkbook", ex);
            }
        }

        public int ClearAllVisualUnderlinesInActiveWorkbook()
        {
            StopActiveUnderlineJobOnly();
            if (!IsExcelReadyForInterop(_excelApp))
                return 0;

            try
            {
                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                if (wb == null)
                    return 0;

                return StripAllUnderlinesForWorkbook(wb, preserveSavedState: true);
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                return 0;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ClearAllVisualUnderlinesInActiveWorkbook", ex);
                return 0;
            }
        }

        public void TemporarilyStripUnderlinesForSaveOrPrint(Excel.Workbook? wb)
        {
            StopActiveUnderlineJobOnly();
            if (wb == null)
                return;

            try
            {
                string wbName = SafeGetWorkbookName(wb);
                int stripped = StripAllUnderlinesForWorkbook(wb, preserveSavedState: false);
                if (stripped > 0 && IsVisualUnderlineEnabled)
                {
                    _pendingRestoreWorkbookName = wbName;
                    _postSaveRestoreTimer.Stop();
                    _postSaveRestoreTimer.Start();
                }
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("TemporarilyStripUnderlinesForSaveOrPrint", ex);
            }
        }

        private void OnPostSaveRestoreTimerTick(object? sender, EventArgs e)
        {
            _postSaveRestoreTimer.Stop();
            string? targetWbName = _pendingRestoreWorkbookName;
            _pendingRestoreWorkbookName = null;

            if (string.IsNullOrEmpty(targetWbName) || !IsVisualUnderlineEnabled || !IsExcelReadyForInterop(_excelApp))
                return;

            try
            {
                foreach (Excel.Workbook wb in _excelApp.Workbooks)
                {
                    if (string.Equals(SafeGetWorkbookName(wb), targetWbName, StringComparison.OrdinalIgnoreCase))
                    {
                        bool wasSavedAfterSave = false;
                        try { wasSavedAfterSave = wb.Saved; } catch { }
                        StartIncrementalUnderlineSync(wb, targetWbName!, _scanGeneration, preserveSavedState: wasSavedAfterSave);
                        break;
                    }
                }
            }
            catch
            {
                // Workbook may have closed right after saving — safe no-op
            }
        }

        private int StripAllUnderlinesForWorkbook(Excel.Workbook wb, bool preserveSavedState)
        {
            StopActiveUnderlineJobOnly();
            string wbName = SafeGetWorkbookName(wb);
            if (!_markedCellsByWorkbook.TryGetValue(wbName, out var markedMap) || markedMap.Count == 0)
                return 0;

            bool wasSaved = false;
            try { wasSaved = wb.Saved; } catch { }

            var wsMap = BuildWorksheetMap(wb);
            int restoredCount = 0;
            WithExcelWriteGuard(() =>
            {
                foreach (var sheetGroup in markedMap.Values.GroupBy(m => m.SheetName, StringComparer.OrdinalIgnoreCase))
                {
                    if (!wsMap.TryGetValue(sheetGroup.Key, out Excel.Worksheet? ws) || ws == null)
                        continue;

                    bool isProtected = false;
                    try { isProtected = ws.ProtectContents; } catch { }

                    foreach (var mark in sheetGroup)
                    {
                        try
                        {
                            Excel.Range cell = (Excel.Range)ws.Cells[mark.Row, mark.Column];
                            if (isProtected && IsCellLockedSafe(cell))
                                continue;

                            RestoreCellBaselineFormatting(cell, mark);
                            restoredCount++;
                        }
                        catch { }
                    }
                }
            });

            markedMap.Clear();
            if (preserveSavedState && wasSaved)
            {
                try { wb.Saved = true; } catch { }
            }

            return restoredCount;
        }

        private void SyncVisualUnderlinesForEditedRange(
            Excel.Workbook wb,
            Excel.Worksheet ws,
            string wbName,
            string sheetName,
            Excel.Range effective,
            List<SpreadsheetCellIssue> freshIssues)
        {
            if (!IsVisualUnderlineEnabled)
                return;

            _markedCellsByWorkbook.TryGetValue(wbName, out var existingMarks);

            // Collect any previously marked cells inside the edited areas that may need restoration/update
            var staleKeysInEdit = new List<string>();
            if (existingMarks != null && existingMarks.Count > 0)
            {
                foreach (Excel.Range area in effective.Areas)
                {
                    int rStart = area.Row;
                    int cStart = area.Column;
                    int rEnd = rStart + area.Rows.Count - 1;
                    int cEnd = cStart + area.Columns.Count - 1;

                    foreach (var kv in existingMarks)
                    {
                        if (string.Equals(kv.Value.SheetName, sheetName, StringComparison.OrdinalIgnoreCase) &&
                            kv.Value.Row >= rStart && kv.Value.Row <= rEnd &&
                            kv.Value.Column >= cStart && kv.Value.Column <= cEnd)
                        {
                            staleKeysInEdit.Add(kv.Key);
                        }
                    }
                }
            }

            // Fast zero-COM-write exit when edited cell(s) have 0 fresh issues and had no prior underlines!
            if (freshIssues.Count == 0 && staleKeysInEdit.Count == 0)
                return;

            if (existingMarks == null)
            {
                existingMarks = new Dictionary<string, MarkedCellBaseline>(StringComparer.OrdinalIgnoreCase);
                _markedCellsByWorkbook[wbName] = existingMarks;
            }

            var freshByCellKey = new Dictionary<string, List<SpreadsheetCellIssue>>(StringComparer.OrdinalIgnoreCase);
            foreach (var issue in freshIssues)
            {
                if (issue.IsResolved)
                    continue;
                string key = MakeCellKey(issue.SheetName, issue.Row, issue.Column);
                if (!freshByCellKey.TryGetValue(key, out var list))
                {
                    list = new List<SpreadsheetCellIssue>(2);
                    freshByCellKey[key] = list;
                }
                list.Add(issue);
            }

            bool isProtected = false;
            try { isProtected = ws.ProtectContents; } catch { }

            WithExcelWriteGuard(() =>
            {
                // 1. Restore any previously marked cell in the edited area that now has 0 issues
                foreach (string staleKey in staleKeysInEdit)
                {
                    if (freshByCellKey.ContainsKey(staleKey))
                        continue;

                    if (existingMarks.TryGetValue(staleKey, out var mark))
                    {
                        try
                        {
                            Excel.Range cell = (Excel.Range)ws.Cells[mark.Row, mark.Column];
                            if (!(isProtected && IsCellLockedSafe(cell)))
                            {
                                RestoreCellBaselineFormatting(cell, mark);
                            }
                        }
                        catch { }
                        existingMarks.Remove(staleKey);
                    }
                }

                // 2. Apply underlines only to the newly flagged edited cell(s)
                foreach (var kv in freshByCellKey)
                {
                    string cellKey = kv.Key;
                    List<SpreadsheetCellIssue> cellIssues = kv.Value;
                    int row = cellIssues[0].Row;
                    int col = cellIssues[0].Column;

                    try
                    {
                        Excel.Range cell = (Excel.Range)ws.Cells[row, col];
                        if ((isProtected && IsCellLockedSafe(cell)) || IsSingleCellFormula(cell))
                            continue;

                        string? liveText = cell.Value2 as string;
                        if (string.IsNullOrEmpty(liveText))
                            continue;

                        string sig = BuildCellUnderlineSignature(cellIssues);
                        if (existingMarks.TryGetValue(cellKey, out var mark) &&
                            string.Equals(mark.AppliedSignature, sig, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (mark == null)
                        {
                            mark = CaptureCellBaseline(cell, sheetName, row, col);
                            existingMarks[cellKey] = mark;
                        }
                        else
                        {
                            RestoreCellBaselineFormatting(cell, mark);
                        }

                        ApplyUnderlinesToSingleCell(cell, liveText!, cellIssues);
                        mark.AppliedSignature = sig;
                    }
                    catch { }
                }
            });
        }

        private void SyncVisualUnderlinesForSingleCell(
            string wbName,
            Excel.Worksheet ws,
            Excel.Range cell,
            string sheetName,
            int row,
            int col,
            string updatedText,
            List<SpreadsheetCellIssue>? allWorkbookItems)
        {
            if (!IsVisualUnderlineEnabled || allWorkbookItems == null || string.IsNullOrEmpty(updatedText))
                return;

            var remainingInCell = new List<SpreadsheetCellIssue>();
            for (int i = 0; i < allWorkbookItems.Count; i++)
            {
                var it = allWorkbookItems[i];
                if (!it.IsResolved &&
                    it.Row == row &&
                    it.Column == col &&
                    string.Equals(it.SheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    remainingInCell.Add(it);
                }
            }

            if (remainingInCell.Count == 0)
                return;

            try
            {
                bool isProtected = false;
                try { isProtected = ws.ProtectContents; } catch { }
                if ((isProtected && IsCellLockedSafe(cell)) || IsSingleCellFormula(cell))
                    return;

                if (!_markedCellsByWorkbook.TryGetValue(wbName, out var marks))
                {
                    marks = new Dictionary<string, MarkedCellBaseline>(StringComparer.OrdinalIgnoreCase);
                    _markedCellsByWorkbook[wbName] = marks;
                }

                string cellKey = MakeCellKey(sheetName, row, col);
                MarkedCellBaseline mark = CaptureCellBaseline(cell, sheetName, row, col);
                marks[cellKey] = mark;
                ApplyUnderlinesToSingleCell(cell, updatedText, remainingInCell);
                mark.AppliedSignature = BuildCellUnderlineSignature(remainingInCell);
            }
            catch { }
        }

        private static string BuildCellUnderlineSignature(List<SpreadsheetCellIssue> cellIssues)
        {
            if (cellIssues.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(cellIssues.Count * 16);
            for (int i = 0; i < cellIssues.Count; i++)
            {
                var it = cellIssues[i];
                if (it.IsResolved)
                    continue;
                sb.Append(it.Issue.StartIndex).Append(':')
                  .Append(it.Issue.Length).Append(':')
                  .Append((int)it.Issue.IssueType).Append('|');
            }
            return sb.ToString();
        }

        private void StartIncrementalUnderlineSync(
            Excel.Workbook wb,
            string wbName,
            int generation,
            bool preserveSavedState)
        {
            StopActiveUnderlineJobOnly();

            if (!IsVisualUnderlineEnabled)
            {
                StripAllUnderlinesForWorkbook(wb, preserveSavedState);
                return;
            }

            _issuesByWorkbook.TryGetValue(wbName, out var allIssues);

            // Group unresolved issues by cell, then prioritize ActiveSheet + VisibleRange proximity up to MaxUnderlinedCellsPerScan!
            var groupedByCell = new Dictionary<string, List<SpreadsheetCellIssue>>(StringComparer.OrdinalIgnoreCase);
            if (allIssues != null && allIssues.Count > 0)
            {
                for (int i = 0; i < allIssues.Count; i++)
                {
                    var issue = allIssues[i];
                    if (issue.IsResolved)
                        continue;

                    string key = MakeCellKey(issue.SheetName, issue.Row, issue.Column);
                    if (!groupedByCell.TryGetValue(key, out var list))
                    {
                        list = new List<SpreadsheetCellIssue>(2);
                        groupedByCell[key] = list;
                    }
                    list.Add(issue);
                }
            }

            string activeSheetName = string.Empty;
            int visMidRow = 15;
            try
            {
                if (wb.ActiveSheet is Excel.Worksheet aws)
                    activeSheetName = SafeGetSheetName(aws);
                Excel.Range? vis = _excelApp.ActiveWindow?.VisibleRange;
                if (vis != null)
                    visMidRow = vis.Row + (vis.Rows.Count / 2);
            }
            catch { }

            long knownScopeCells = _scanMetaByWorkbook.TryGetValue(wbName, out var wbMeta) ? wbMeta.TotalScopeCells : 0;
            EcoRuntimeBudgets underlineBudgets = EcoPerformanceGovernor.ComputeBudgets(
                EcoPerformanceGovernor.IsLargeExcelScope(knownScopeCells) || groupedByCell.Count >= 80);
            int maxUnderlineCap = underlineBudgets.ExcelVisualUnderlineCap;

            var selectedCells = new Dictionary<string, List<SpreadsheetCellIssue>>(StringComparer.OrdinalIgnoreCase);
            if (groupedByCell.Count <= maxUnderlineCap)
            {
                foreach (var kv in groupedByCell)
                    selectedCells[kv.Key] = kv.Value;
            }
            else
            {
                foreach (var kv in groupedByCell
                    .OrderBy(kv => string.Equals(kv.Value[0].SheetName, activeSheetName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(kv => Math.Abs(kv.Value[0].Row - visMidRow))
                    .ThenBy(kv => kv.Value[0].Column)
                    .Take(maxUnderlineCap))
                {
                    selectedCells[kv.Key] = kv.Value;
                }
            }

            _markedCellsByWorkbook.TryGetValue(wbName, out var existingMarks);
            bool hasExistingMarks = existingMarks != null && existingMarks.Count > 0;

            if (selectedCells.Count == 0 && !hasExistingMarks)
                return;

            if (existingMarks == null)
            {
                existingMarks = new Dictionary<string, MarkedCellBaseline>(StringComparer.OrdinalIgnoreCase);
                _markedCellsByWorkbook[wbName] = existingMarks;
            }

            var job = new PendingUnderlineJob
            {
                Generation = generation,
                WorkbookName = wbName,
                PreserveSavedState = preserveSavedState
            };
            try { job.InitialWorkbookSaved = wb.Saved; } catch { }

            // 1. Enqueue restore ops for previously underlined cells that are no longer active
            if (existingMarks.Count > 0)
            {
                foreach (var kv in existingMarks)
                {
                    if (!selectedCells.ContainsKey(kv.Key))
                    {
                        job.Operations.Enqueue(new PendingUnderlineOp
                        {
                            IsRestoreOnly = true,
                            CellKey = kv.Key,
                            SheetName = kv.Value.SheetName,
                            Row = kv.Value.Row,
                            Column = kv.Value.Column,
                            BaselineToRestore = kv.Value
                        });
                    }
                }
            }

            // 2. Enqueue apply ops ONLY for cells whose underline signature changed!
            foreach (var kv in selectedCells
                .OrderBy(kv => string.Equals(kv.Value[0].SheetName, activeSheetName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(kv => Math.Abs(kv.Value[0].Row - visMidRow)))
            {
                string sig = BuildCellUnderlineSignature(kv.Value);
                if (existingMarks.TryGetValue(kv.Key, out var existingMark) &&
                    string.Equals(existingMark.AppliedSignature, sig, StringComparison.Ordinal))
                {
                    // Already underlined with exact same character offsets & types — 0 COM calls needed!
                    continue;
                }

                job.Operations.Enqueue(new PendingUnderlineOp
                {
                    IsRestoreOnly = false,
                    CellKey = kv.Key,
                    SheetName = kv.Value[0].SheetName,
                    Row = kv.Value[0].Row,
                    Column = kv.Value[0].Column,
                    IssuesToApply = kv.Value,
                    Signature = sig
                });
            }

            if (job.Operations.Count == 0)
                return;

            _activeUnderlineJob = job;
            ProcessUnderlineJobSlice();
        }

        private void OnUnderlineChunkTimerTick(object? sender, EventArgs e)
        {
            _underlineChunkTimer.Stop();
            ProcessUnderlineJobSlice();
        }

        private void ProcessUnderlineJobSlice()
        {
            PendingUnderlineJob? job = _activeUnderlineJob;
            if (_disposed || job == null || job.Generation != _scanGeneration || !IsVisualUnderlineEnabled)
            {
                _activeUnderlineJob = null;
                return;
            }

            if (!IsExcelReadyForInterop(_excelApp))
            {
                // User is actively typing or editing formula bar — yield immediately and retry smoothly later
                _underlineChunkTimer.Start();
                return;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null || !string.Equals(SafeGetWorkbookName(wb), job.WorkbookName, StringComparison.OrdinalIgnoreCase))
            {
                _activeUnderlineJob = null;
                return;
            }

            if (!_markedCellsByWorkbook.TryGetValue(job.WorkbookName, out var existingMarks))
            {
                existingMarks = new Dictionary<string, MarkedCellBaseline>(StringComparer.OrdinalIgnoreCase);
                _markedCellsByWorkbook[job.WorkbookName] = existingMarks;
            }

            var wsMap = BuildWorksheetMap(wb);
            var protectedCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var sw = Stopwatch.StartNew();

            try
            {
                WithExcelWriteGuard(() =>
                {
                    while (job.Operations.Count > 0)
                    {
                        PendingUnderlineOp op = job.Operations.Dequeue();
                        if (wsMap.TryGetValue(op.SheetName, out Excel.Worksheet? ws) && ws != null)
                        {
                            if (!protectedCache.TryGetValue(op.SheetName, out bool isProtected))
                            {
                                try { isProtected = ws.ProtectContents; } catch { isProtected = false; }
                                protectedCache[op.SheetName] = isProtected;
                            }

                            try
                            {
                                Excel.Range cell = (Excel.Range)ws.Cells[op.Row, op.Column];
                                if (op.IsRestoreOnly)
                                {
                                    if (op.BaselineToRestore != null && !(isProtected && IsCellLockedSafe(cell)))
                                    {
                                        RestoreCellBaselineFormatting(cell, op.BaselineToRestore);
                                    }
                                    existingMarks.Remove(op.CellKey);
                                }
                                else if (op.IssuesToApply != null)
                                {
                                    if (!(isProtected && IsCellLockedSafe(cell)) && !IsSingleCellFormula(cell))
                                    {
                                        string? liveText = cell.Value2 as string;
                                        if (!string.IsNullOrEmpty(liveText))
                                        {
                                            if (!existingMarks.TryGetValue(op.CellKey, out var mark))
                                            {
                                                mark = CaptureCellBaseline(cell, op.SheetName, op.Row, op.Column);
                                                existingMarks[op.CellKey] = mark;
                                            }
                                            else
                                            {
                                                RestoreCellBaselineFormatting(cell, mark);
                                            }

                                            ApplyUnderlinesToSingleCell(cell, liveText!, op.IssuesToApply);
                                            mark.AppliedSignature = op.Signature;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        else if (op.IsRestoreOnly)
                        {
                            existingMarks.Remove(op.CellKey);
                        }

                        if (sw.ElapsedMilliseconds >= UnderlineSliceBudgetMs && job.Operations.Count > 0)
                        {
                            break;
                        }
                    }
                });
            }
            catch
            {
                _activeUnderlineJob = null;
                return;
            }

            if (job.Operations.Count > 0)
            {
                _underlineChunkTimer.Start();
            }
            else
            {
                _activeUnderlineJob = null;
                if (job.PreserveSavedState && job.InitialWorkbookSaved)
                {
                    try { wb.Saved = true; } catch { }
                }
            }
        }

        private void SyncVisualUnderlinesForWorkbook(Excel.Workbook wb, string wbName, bool preserveSavedState)
        {
            StartIncrementalUnderlineSync(wb, wbName, _scanGeneration, preserveSavedState);
        }

        private void RestoreAndUntrackSingleCellIfMarked(string wbName, Excel.Range cell, string sheetName, int row, int col)
        {
            if (string.IsNullOrEmpty(wbName))
                return;

            if (_markedCellsByWorkbook.TryGetValue(wbName, out var marks))
            {
                string key = MakeCellKey(sheetName, row, col);
                if (marks.TryGetValue(key, out var baseline))
                {
                    RestoreCellBaselineFormatting(cell, baseline);
                    marks.Remove(key);
                }
            }
        }

        private static MarkedCellBaseline CaptureCellBaseline(Excel.Range cell, string sheetName, int row, int col)
        {
            var baseline = new MarkedCellBaseline
            {
                SheetName = sheetName,
                Row = row,
                Column = col,
                WasColorAutomatic = true,
                OriginalUnderline = (int)Excel.XlUnderlineStyle.xlUnderlineStyleNone
            };

            try
            {
                object? rawColorIdx = cell.Font.ColorIndex;
                object? rawColor = cell.Font.Color;
                int oleColor = 0;
                bool hasConcreteColor = false;

                if (rawColor is double d && !double.IsNaN(d))
                {
                    oleColor = Convert.ToInt32(d);
                    hasConcreteColor = true;
                }
                else if (rawColor is int i)
                {
                    oleColor = i;
                    hasConcreteColor = true;
                }

                bool isOurProofingColor = hasConcreteColor &&
                    (oleColor == OleColorSpellingRed || oleColor == OleColorGrammarBlue || oleColor == OleColorContextAmber);

                if (!isOurProofingColor && rawColorIdx is int ci && ci != (int)Excel.XlColorIndex.xlColorIndexAutomatic && hasConcreteColor)
                {
                    baseline.WasColorAutomatic = false;
                    baseline.OriginalOleColor = oleColor;
                }

                if (!isOurProofingColor && cell.Font.Underline is int ul && ul != 9999999)
                {
                    baseline.OriginalUnderline = ul;
                }
            }
            catch { }

            return baseline;
        }

        private static void RestoreCellBaselineFormatting(Excel.Range cell, MarkedCellBaseline baseline)
        {
            if (IsSingleCellFormula(cell))
                return;

            try { cell.Font.Underline = baseline.OriginalUnderline; } catch { }
            try
            {
                if (baseline.WasColorAutomatic)
                    cell.Font.ColorIndex = Excel.XlColorIndex.xlColorIndexAutomatic;
                else
                    cell.Font.Color = baseline.OriginalOleColor;
            }
            catch { }
        }

        private static void ApplyUnderlinesToSingleCell(Excel.Range cell, string liveText, List<SpreadsheetCellIssue> cellIssues)
        {
            foreach (var item in cellIssues)
            {
                if (item.IsResolved)
                    continue;

                int start0 = item.Issue.StartIndex;
                int len = item.Issue.Length;
                if (start0 < 0 || len <= 0 || start0 + len > liveText.Length)
                    continue;

                int oleColor = item.Issue.IsContextualWarning
                    ? OleColorContextAmber
                    : (item.Issue.IsGrammarOrStyle ? OleColorGrammarBlue : OleColorSpellingRed);

                Excel.XlUnderlineStyle ulStyle = item.Issue.IsGrammarOrStyle
                    ? Excel.XlUnderlineStyle.xlUnderlineStyleDouble
                    : Excel.XlUnderlineStyle.xlUnderlineStyleSingle;

                try
                {
                    Excel.Characters chars = cell.Characters[start0 + 1, len];
                    chars.Font.Color = oleColor;
                    chars.Font.Underline = ulStyle;
                }
                catch { }
            }
        }

        private static bool IsCellLockedSafe(Excel.Range cell)
        {
            try
            {
                return cell.Locked is bool b && b;
            }
            catch
            {
                return true;
            }
        }

        private static string MakeCellKey(string sheetName, int row, int col)
        {
            return string.Concat(sheetName ?? string.Empty, "\u001F", row.ToString(CultureInfo.InvariantCulture), "\u001F", col.ToString(CultureInfo.InvariantCulture));
        }
    }
}
