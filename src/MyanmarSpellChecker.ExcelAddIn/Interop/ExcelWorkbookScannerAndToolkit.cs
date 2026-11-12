using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.Core.Typography;
using Excel = Microsoft.Office.Interop.Excel;

namespace MyanmarSpellChecker.ExcelAddIn.Interop
{
    public sealed class ExcelToolkitOutcome
    {
        public bool Success { get; set; }
        public int CellsModified { get; set; }
        public int TotalChanges { get; set; }
        public string UserMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Formula-safe, Undo-safe, bulk 2D SAFEARRAY scanner and spreadsheet toolkit for Microsoft Excel.
    /// Rules enforced at all times:
    /// 1. Scanning is 100% READ-ONLY (zero Range/Font writes during scan), so Excel's native Ctrl+Z Undo stack
    ///    and Workbook.Saved flag are never dirtied by scanning.
    /// 2. Formula cells (HasFormula == true or Formula starting with '=') and numeric calculation cells are
    ///    NEVER modified by spellcheck fixes, Zawgyi conversion, numeral conversion, or style rewrites.
    /// 3. In-Cell Edit Mode / Formula Bar typing is guarded via IsExcelReadyForInterop + HRESULT shields
    ///    (0x800A03EC, 0x800AC472, 0x80010001) so Excel never throws or lags while a user is typing.
    /// 4. Every add-in cell modification is recorded in SpreadsheetUndoManager for instant 1-click Undo.
    /// </summary>
    public sealed class ExcelWorkbookScannerAndToolkit
    {
        private const int MaxRowsPerChunk = 2000;
        private const int MaxColsPerChunk = 256;
        private const int MaxCellsPerFullScan = 250_000;
        private const int MaxCellsPerLiveEditScan = 5_000;

        private readonly Excel.Application _excelApp;
        private readonly MyanmarProofingEngine _engine;
        private readonly SpreadsheetUndoManager _undoManager = new SpreadsheetUndoManager(maxBatches: 30);

        private readonly Dictionary<string, List<SpreadsheetCellIssue>> _issuesByWorkbook =
            new Dictionary<string, List<SpreadsheetCellIssue>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DocumentQualityReport> _qualityByWorkbook =
            new Dictionary<string, DocumentQualityReport>(StringComparer.OrdinalIgnoreCase);

        private int _suppressSheetChangeEvents;

        public bool IsLiveAutoScanEnabled { get; set; } = true;
        public SpreadsheetScanScope CurrentScope { get; set; } = SpreadsheetScanScope.ActiveSheet;
        public SpreadsheetUndoManager UndoManager => _undoManager;

        public event Action? ScanStarted;
        public event Action<IReadOnlyList<SpreadsheetCellIssue>>? ScanCompleted;
        public event Action? UndoStateChanged;

        public ExcelWorkbookScannerAndToolkit(Excel.Application excelApp, MyanmarProofingEngine engine)
        {
            _excelApp = excelApp ?? throw new ArgumentNullException(nameof(excelApp));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
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

        public void ClearCachedIssuesForActiveWorkbook()
        {
            string wbKey = GetActiveWorkbookKey();
            if (!string.IsNullOrEmpty(wbKey))
            {
                _issuesByWorkbook.Remove(wbKey);
                _qualityByWorkbook.Remove(wbKey);
            }
        }

        public void PurgeClosedWorkbook(string? workbookName)
        {
            if (string.IsNullOrEmpty(workbookName))
                return;

            _issuesByWorkbook.Remove(workbookName!);
            _qualityByWorkbook.Remove(workbookName!);
            _undoManager.ClearForWorkbook(workbookName!);
            UndoStateChanged?.Invoke();
        }

        /// <summary>
        /// Performs a 100% read-only bulk 2D scan across the requested scope (ActiveSheet, Selection, or EntireWorkbook).
        /// </summary>
        public IReadOnlyList<SpreadsheetCellIssue> ScanScope(SpreadsheetScanScope scope)
        {
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
        /// Incremental read-only cell re-check triggered after a user finishes editing cell(s) and presses Enter/Tab.
        /// Never writes to Excel COM, so Excel's native Ctrl+Z Undo history remains 100% preserved!
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
                foreach (Excel.Range area in effective.Areas)
                {
                    int rStart = area.Row;
                    int cStart = area.Column;
                    int rEnd = rStart + area.Rows.Count - 1;
                    int cEnd = cStart + area.Columns.Count - 1;

                    existingList.RemoveAll(item =>
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
                ScanCompleted?.Invoke(existingList);
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
                errorMessage = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
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
                    errorMessage = $"⚠ {item.DisplayLocation} သည် Formula အကွက်ဖြစ်သဖြင့် တွက်ချက်မှုမပျက်စေရန် အလိုအလျောက် ကာကွယ်ထားပါသည်။";
                    return false;
                }

                string? liveText = cell.Value2 as string;
                if (string.IsNullOrEmpty(liveText))
                {
                    errorMessage = $"⚠ {item.DisplayLocation} ရှိ စာသား ပြောင်းလဲသွားပြီဖြစ်သဖြင့် ပြန်လည်စစ်ဆေးပေးပါ။";
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
                    errorMessage = $"⚠ {item.DisplayLocation} အတွင်း \"{item.Issue.OriginalText}\" နေရာကို ရှာမတွေ့တော့ပါ။";
                    return false;
                }

                WithExcelWriteGuard(() =>
                {
                    WriteSafePlainStringToCell(cell, updatedText);
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
                errorMessage = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
                return false;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ApplySingleFix", ex);
                errorMessage = "⚠ အကွက်ပြင်ဆင်စဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည် (Sheet Lock / Protected Sheet ရှိမရှိ စစ်ဆေးပါ)။";
                return false;
            }
        }

        public int ApplyBatchFixAll(Func<SpreadsheetCellIssue, bool>? filterPredicate, string descriptionLabel, out string busyWarning)
        {
            busyWarning = string.Empty;
            if (!IsExcelReadyForInterop(_excelApp))
            {
                busyWarning = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
                return 0;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
                return 0;

            string wbName = SafeGetWorkbookName(wb);
            if (!_issuesByWorkbook.TryGetValue(wbName, out var allItems) || allItems.Count == 0)
                return 0;

            var ops = SpreadsheetProofingHelper.BuildBatchReplacementsByCell(
                allItems,
                filterPredicate,
                anchor => TryReadLivePlainCellString(wb, anchor.SheetName, anchor.Row, anchor.Column));

            if (ops.Count == 0)
                return 0;

            int totalFixedIssues = ApplyCellWriteOperations(wb, wbName, ops, descriptionLabel, out busyWarning);
            return totalFixedIssues;
        }

        public int ApplySameWordBatchFix(string originalText, string replacementText, out string busyWarning)
        {
            busyWarning = string.Empty;
            if (!IsExcelReadyForInterop(_excelApp))
            {
                busyWarning = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
                return 0;
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
                return 0;

            string wbName = SafeGetWorkbookName(wb);
            if (!_issuesByWorkbook.TryGetValue(wbName, out var allItems) || allItems.Count == 0)
                return 0;

            var ops = SpreadsheetProofingHelper.BuildSameWordBatchReplacementsByCell(
                allItems,
                originalText,
                replacementText,
                anchor => TryReadLivePlainCellString(wb, anchor.SheetName, anchor.Row, anchor.Column));

            if (ops.Count == 0)
                return 0;

            return ApplyCellWriteOperations(
                wb,
                wbName,
                ops,
                $"တူညီအမှား \"{originalText}\" ➔ \"{replacementText}\"",
                out busyWarning);
        }

        public bool UndoLastAction(out string statusMessage)
        {
            statusMessage = string.Empty;
            if (!_undoManager.CanUndo)
            {
                statusMessage = "ပြန်ဆုတ်ရန် (Undo) မှတ်တမ်း မရှိသေးပါ။";
                return false;
            }

            if (!IsExcelReadyForInterop(_excelApp))
            {
                statusMessage = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြန်ဆုတ်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
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

                WithExcelWriteGuard(() =>
                {
                    foreach (var group in batch.Entries.GroupBy(e => e.SheetName, StringComparer.OrdinalIgnoreCase))
                    {
                        Excel.Worksheet? ws = FindWorksheetByName(wb, group.Key);
                        if (ws == null)
                            continue;

                        foreach (var entry in group)
                        {
                            try
                            {
                                Excel.Range cell = (Excel.Range)ws.Cells[entry.Row, entry.Column];
                                if (IsSingleCellFormula(cell))
                                    continue;

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
                });

                UndoStateChanged?.Invoke();
                ScanScope(CurrentScope);
                statusMessage = $"✓ မူလအတိုင်း ပြန်ဆုတ်ပြီးပါပြီ ({restoredCells} ကွက် — {batch.Description})။";
                return restoredCells > 0;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("UndoLastAction", ex);
                UndoStateChanged?.Invoke();
                statusMessage = "⚠ ပြန်ဆုတ်စဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။";
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
                    ? $"✓ အကွက် ({cells}) ကွက်ကို Pyidaungsu Unicode သို့ Formula မထိခိုက်ဘဲ ပြောင်းလဲပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"
                    : "✓ ပြောင်းလဲရန် WinInnwa / Zawgyi စာသားကွက် မတွေ့ရှိပါ (Formula ကွက်များကို အလိုအလျောက် ချန်လှပ်ထားသည်)။");
        }

        public ExcelToolkitOutcome FormatNumeralsInScope(bool toMyanmarDigits, bool selectionOnly)
        {
            string label = toMyanmarDigits ? "0-9 ➔ ၀-၉ (စာသားကွက်)" : "၀-၉ ➔ 0-9 (စာသားကွက်)";
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                label,
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                    SpreadsheetProofingHelper.TransformBlockNumerals(
                        wbName, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml, toMyanmarDigits),
                (cells, changes) => cells > 0
                    ? $"✓ စာသားကွက် ({cells}) ကွက်ရှိ ဂဏန်း ({changes}) လုံးကို ပြောင်းလဲပြီးပါပြီ — တွက်ချက်မှု ဂဏန်းကွက်နှင့် Formula များကို မထိခိုက်ပါ (↶ ပြန်ဆုတ်နိုင်သည်)။"
                    : "✓ ပြောင်းလဲရန် မြန်မာစာသားပါ ဂဏန်းကွက် မတွေ့ရှိပါ (တွက်ချက်မှု ကိန်းဂဏန်းနှင့် Formula ကွက်များကို ကာကွယ်ထားသည်)။");
        }

        public ExcelToolkitOutcome RewriteOfficialProseInScope(bool selectionOnly)
        {
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                "ရုံးသုံးစာပေဟန် ပြောင်းလဲမှု",
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                    SpreadsheetProofingHelper.TransformBlockOfficialProse(
                        wbName, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml),
                (cells, changes) => cells > 0
                    ? $"✓ အကွက် ({cells}) ကွက်ရှိ စကားပြောဟန် ({changes}) နေရာကို ရုံးသုံး/စာပေဟန်သို့ ပြောင်းလဲပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"
                    : "✓ ပြောင်းလဲရန် စကားပြောဟန် အသုံးအနှုန်း မတွေ့ရှိပါ (ရုံးသုံးစာပေဟန် အသင့်ဖြစ်ပြီးဖြစ်သည်)။");
        }

        public ExcelToolkitOutcome FormatPunctuationInScope(bool selectionOnly)
        {
            return ExecuteBlockToolkitTransform(
                selectionOnly,
                "ပုဒ်ဖြတ် ၊ / ။ နှင့် ZWSP ရှင်းလင်းမှု",
                (wbName, ws, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml) =>
                    SpreadsheetProofingHelper.TransformBlockPunctuation(
                        wbName, sheetName, baseR, baseC, rows, cols, val2, fml, hasFml),
                (cells, changes) => cells > 0
                    ? $"✓ အကွက် ({cells}) ကွက်ရှိ ပုဒ်ဖြတ်ပုဒ်ရပ်နှင့် ZWSP ({changes}) နေရာကို သန့်စင်ညှိနှိုင်းပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။"
                    : "✓ ပြင်ဆင်ရန် ပုဒ်ဖြတ်ပုဒ်ရပ် (၊ / ။) သို့မဟုတ် ZWSP အမှား မတွေ့ရှိပါ။");
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
                statusMessage = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ထည့်သွင်း၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
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
                    statusMessage = "⚠ လက်ရှိအကွက်သည် Formula အကွက်ဖြစ်သဖြင့် မပျက်စီးစေရန် ကာကွယ်ထားပါသည် (အခြားအကွက်လွတ် ရွေးချယ်ပါ)။";
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
                    Description = $"{sheetName}!{SpreadsheetProofingHelper.ColumnIndexToLetters(col)}{row} သို့ စာသားထည့်သွင်းမှု",
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
                statusMessage = $"✓ {sheetName}!{SpreadsheetProofingHelper.ColumnIndexToLetters(col)}{row} အကွက်သို့ ထည့်သွင်းပြီးပါပြီ (↶ ပြန်ဆုတ်နိုင်သည်)။";
                return true;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("TryInsertTextIntoActiveCell", ex);
                statusMessage = "⚠ အကွက်အတွင်း ထည့်သွင်းစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။";
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
                    UserMessage = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် အသုံးပြု၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။"
                };
            }

            Excel.Workbook? wb = null;
            try { wb = _excelApp.ActiveWorkbook; } catch { }
            if (wb == null)
            {
                return new ExcelToolkitOutcome
                {
                    Success = false,
                    UserMessage = "⚠ လက်ရှိ ဖွင့်ထားသော Excel Workbook မရှိသေးပါ။"
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

                ScanScope(CurrentScope);
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
                    UserMessage = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် အသုံးပြု၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။"
                };
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ExecuteBlockToolkitTransform", ex);
                return new ExcelToolkitOutcome
                {
                    Success = false,
                    UserMessage = "⚠ ပြောင်းလဲစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည် (Protected Sheet ဖြစ်နေခြင်း ရှိမရှိ စစ်ဆေးပါ)။"
                };
            }
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
                WithExcelWriteGuard(() =>
                {
                    foreach (var sheetGroup in ops.GroupBy(o => o.SheetName, StringComparer.OrdinalIgnoreCase))
                    {
                        Excel.Worksheet? ws = FindWorksheetByName(wb, sheetGroup.Key);
                        if (ws == null)
                            continue;

                        foreach (var op in sheetGroup)
                        {
                            Excel.Range cell = (Excel.Range)ws.Cells[op.Row, op.Column];
                            // Final safety gate: never overwrite a formula cell!
                            if (IsSingleCellFormula(cell))
                                continue;

                            WriteSafePlainStringToCell(cell, op.UpdatedText);
                            if (!string.IsNullOrEmpty(op.TargetFontName))
                            {
                                try { cell.Font.Name = op.TargetFontName; } catch { }
                            }

                            executedOps.Add(op);
                            totalChanges += Math.Max(1, op.ChangesInCell);
                        }
                    }
                });

                if (executedOps.Count > 0)
                {
                    _undoManager.PushOperations($"{undoDescription} ({executedOps.Count} ကွက်)", executedOps);
                    _qualityByWorkbook.Remove(wbName);
                    UndoStateChanged?.Invoke();
                }

                return totalChanges;
            }
            catch (COMException ex) when (IsTransientExcelBusyComError(ex))
            {
                busyWarning = "⚠ Excel အကွက်အတွင်း စာရိုက်နေစဉ် ပြင်ဆင်၍မရပါ — Enter သို့မဟုတ် Esc နှိပ်ပြီးမှ ထပ်မံနှိပ်ပါ။";
                return totalChanges;
            }
            catch (Exception ex)
            {
                AddInDiagnostics.LogException("ApplyCellWriteOperations", ex);
                busyWarning = "⚠ အကွက်များ ပြင်ဆင်စဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည် (Protected Sheet ရှိမရှိ စစ်ဆေးပါ)။";
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

        private static string? TryReadLivePlainCellString(Excel.Workbook wb, string sheetName, int row, int col)
        {
            try
            {
                Excel.Worksheet? ws = FindWorksheetByName(wb, sheetName);
                if (ws == null)
                    return null;

                Excel.Range cell = (Excel.Range)ws.Cells[row, col];
                if (IsSingleCellFormula(cell))
                    return null;

                return cell.Value2 as string;
            }
            catch
            {
                return null;
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

        private void WithExcelWriteGuard(Action writeAction)
        {
            bool prevScreenUpdating = true;
            bool prevEnableEvents = true;
            bool capturedState = false;

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
                }
                catch { }

                writeAction();
            }
            finally
            {
                if (capturedState)
                {
                    try { _excelApp.EnableEvents = prevEnableEvents; } catch { }
                    try { _excelApp.ScreenUpdating = prevScreenUpdating; } catch { }
                }
                _suppressSheetChangeEvents = Math.Max(0, _suppressSheetChangeEvents - 1);
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

        private static Excel.Range? SafeGetUsedRange(Excel.Worksheet ws)
        {
            try { return ws.UsedRange; } catch { return null; }
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
    }
}
