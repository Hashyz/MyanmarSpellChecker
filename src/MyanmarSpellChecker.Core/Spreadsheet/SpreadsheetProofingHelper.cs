// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Typography;

namespace MyanmarSpellChecker.Core.Spreadsheet
{
    public enum SpreadsheetScanScope
    {
        ActiveSheet = 0,
        Selection = 1,
        EntireWorkbook = 2
    }

    /// <summary>
    /// Represents a single proofing issue anchored to a specific worksheet cell coordinate (1-based Row/Column),
    /// completely decoupled from Excel COM objects so scanning is 100% read-only and never wipes Excel's Undo stack.
    /// </summary>
    public sealed class SpreadsheetCellIssue
    {
        public string WorkbookName { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public int Row { get; set; }
        public int Column { get; set; }
        public string CellSnapshotText { get; set; } = string.Empty;
        public ProofingIssue Issue { get; set; } = new ProofingIssue();
        public bool IsResolved { get; set; }

        public string CellAddress =>
            SpreadsheetProofingHelper.ColumnIndexToLetters(Column) + Row.ToString(CultureInfo.InvariantCulture);

        public string DisplayLocation =>
            string.IsNullOrEmpty(SheetName) ? CellAddress : $"{SheetName}!{CellAddress}";

        public string BuildContextSnippet(int radius = 24)
        {
            if (string.IsNullOrEmpty(CellSnapshotText))
                return Issue?.OriginalText ?? string.Empty;

            string clean = CellSnapshotText.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            int start = Math.Max(0, Math.Min(Issue.StartIndex, clean.Length));
            int len = Math.Max(0, Math.Min(Issue.Length, clean.Length - start));

            int left = Math.Max(0, start - radius);
            int right = Math.Min(clean.Length, start + len + radius);

            string prefix = left > 0 ? "…" + clean.Substring(left, start - left) : clean.Substring(0, start);
            string focus = len > 0 ? clean.Substring(start, len) : Issue.OriginalText;
            string suffix = right < clean.Length
                ? clean.Substring(start + len, right - (start + len)) + "…"
                : clean.Substring(start + len);

            return $"{prefix}【{focus}】{suffix}";
        }
    }

    public sealed class CellWriteOperation
    {
        public string WorkbookName { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public int Row { get; set; }
        public int Column { get; set; }
        public string PreviousText { get; set; } = string.Empty;
        public string UpdatedText { get; set; } = string.Empty;
        public int ChangesInCell { get; set; }
        public string? PreviousFontName { get; set; }
        public string? TargetFontName { get; set; }
    }

    public sealed class CellUndoEntry
    {
        public string WorkbookName { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
        public int Row { get; set; }
        public int Column { get; set; }
        public string PreviousText { get; set; } = string.Empty;
        public string NewText { get; set; } = string.Empty;
        public string? PreviousFontName { get; set; }
        public string? NewFontName { get; set; }
    }

    public sealed class SpreadsheetUndoBatch
    {
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<CellUndoEntry> Entries { get; set; } = new List<CellUndoEntry>();
    }

    /// <summary>
    /// Bounded multi-level Undo history for Excel Add-in write operations.
    /// Because any COM write to Excel.Range clears Excel's native Ctrl+Z stack, this manager preserves
    /// exact cell values and font names so users can always revert 1-click fixes and batch transforms.
    /// </summary>
    public sealed class SpreadsheetUndoManager
    {
        private readonly int _maxBatches;
        private readonly LinkedList<SpreadsheetUndoBatch> _history = new LinkedList<SpreadsheetUndoBatch>();

        public SpreadsheetUndoManager(int maxBatches = 30)
        {
            _maxBatches = Math.Max(1, maxBatches);
        }

        public int UndoCount => _history.Count;
        public bool CanUndo => _history.Count > 0;
        public string LatestDescription => _history.Last?.Value.Description ?? string.Empty;

        public void PushBatch(SpreadsheetUndoBatch batch)
        {
            if (batch == null || batch.Entries == null || batch.Entries.Count == 0)
                return;

            while (_history.Count >= _maxBatches)
            {
                _history.RemoveFirst();
            }
            _history.AddLast(batch);
        }

        public void PushOperations(string description, IEnumerable<CellWriteOperation> operations)
        {
            if (operations == null)
                return;

            var entries = new List<CellUndoEntry>();
            foreach (var op in operations)
            {
                entries.Add(new CellUndoEntry
                {
                    WorkbookName = op.WorkbookName,
                    SheetName = op.SheetName,
                    Row = op.Row,
                    Column = op.Column,
                    PreviousText = op.PreviousText,
                    NewText = op.UpdatedText,
                    PreviousFontName = op.PreviousFontName,
                    NewFontName = op.TargetFontName
                });
            }

            if (entries.Count > 0)
            {
                PushBatch(new SpreadsheetUndoBatch
                {
                    Description = description,
                    Entries = entries
                });
            }
        }

        public SpreadsheetUndoBatch? PopUndoBatch()
        {
            if (_history.Last == null)
                return null;

            SpreadsheetUndoBatch top = _history.Last.Value;
            _history.RemoveLast();
            return top;
        }

        public void ClearForWorkbook(string workbookName)
        {
            if (string.IsNullOrEmpty(workbookName))
                return;

            var node = _history.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.Entries.All(e => string.Equals(e.WorkbookName, workbookName, StringComparison.OrdinalIgnoreCase)))
                {
                    _history.Remove(node);
                }
                node = next;
            }
        }

        public void ClearAll() => _history.Clear();
    }

    /// <summary>
    /// High-performance, COM-free 2D spreadsheet proofing, formula-shielding, multi-issue offset shifting,
    /// and batch cell transformation algorithms shared by MyanmarSpellChecker.ExcelAddIn and unit tests.
    /// </summary>
    public static class SpreadsheetProofingHelper
    {
        /// <summary>
        /// Converts a 1-based Excel column number to A1 column letters (1 -> A, 26 -> Z, 27 -> AA, 16384 -> XFD).
        /// </summary>
        public static string ColumnIndexToLetters(int columnOneBased)
        {
            if (columnOneBased <= 0)
                return "A";

            var chars = new Stack<char>(4);
            int dividend = columnOneBased;
            while (dividend > 0)
            {
                int modulo = (dividend - 1) % 26;
                chars.Push((char)('A' + modulo));
                dividend = (dividend - modulo) / 26;
            }

            return new string(chars.ToArray());
        }

        /// <summary>
        /// Determines whether a cell contains an Excel formula and MUST NEVER be overwritten by spellcheck or typography tools.
        /// </summary>
        public static bool IsFormulaCell(object? rawValue2, object? rawFormula, bool? blockHasFormula)
        {
            if (blockHasFormula == true)
                return true;

            if (blockHasFormula == false)
            {
                // Extra defensive check in case caller supplied rawFormula anyway
                if (rawFormula is string explicitFormula && explicitFormula.StartsWith("=", StringComparison.Ordinal))
                    return true;
                return false;
            }

            // Mixed block (Range.HasFormula returned null / DBNull.Value): inspect cell's Formula string
            if (rawFormula is string formulaStr && formulaStr.StartsWith("=", StringComparison.Ordinal))
                return true;

            // Defensive fallback if rawFormula was omitted and rawValue2 itself looks like an unevaluated formula
            if (rawFormula == null && rawValue2 is string valStr && valStr.StartsWith("=", StringComparison.Ordinal))
                return true;

            return false;
        }

        /// <summary>
        /// Fast character-level scan for Myanmar Unicode (U+1000..U+109F, U+AA60..U+AA7F, U+A9E0..U+A9FF).
        /// Skips pure English, numeric, date, boolean, and error cells in nanoseconds with zero allocations.
        /// </summary>
        public static bool ContainsMyanmarScript(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            string s = text!;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u1000' && c <= '\u109F') ||
                    (c >= '\uAA60' && c <= '\uAA7F') ||
                    (c >= '\uA9E0' && c <= '\uA9FF'))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool ShouldInspectCellText(object? rawValue2, out string text)
        {
            if (rawValue2 is string s && !string.IsNullOrWhiteSpace(s) && ContainsMyanmarScript(s))
            {
                text = s;
                return true;
            }

            text = string.Empty;
            return false;
        }

        /// <summary>
        /// Scans a 2D Value2 + Formula block (supporting both 1-cell scalars, 0-based C# 2D arrays, and 1-based Excel COM SAFEARRAYs)
        /// while strictly skipping formula cells, numbers, dates, booleans, and non-Myanmar strings.
        /// Uses per-scan cell string memoization so sheets with thousands of repeated status/location cells finish in milliseconds.
        /// </summary>
        public static List<SpreadsheetCellIssue> ScanBlockValues(
            MyanmarProofingEngine engine,
            string workbookName,
            string sheetName,
            int baseRowOneBased,
            int baseColOneBased,
            int rowCount,
            int colCount,
            object? rawValue2Block,
            object? rawFormulaBlock,
            bool? blockHasFormula,
            Dictionary<string, IReadOnlyList<ProofingIssue>>? sharedCellMemo = null,
            CancellationToken cancellationToken = default,
            int maxIssuesToCollect = int.MaxValue,
            EcoRuntimeBudgets? explicitBudgets = null)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            var results = new List<SpreadsheetCellIssue>();
            if (rowCount <= 0 || colCount <= 0 || maxIssuesToCollect <= 0 || blockHasFormula == true || rawValue2Block == null || cancellationToken.IsCancellationRequested)
                return results;

            long blockCells = (long)rowCount * colCount;
            EcoRuntimeBudgets budgets = explicitBudgets ?? EcoPerformanceGovernor.ComputeBudgets(EcoPerformanceGovernor.IsLargeExcelScope(blockCells));
            int maxMemoEntries = Math.Max(1024, budgets.MaxSpreadsheetCellCacheEntries);
            var memo = sharedCellMemo ?? new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            if (memo.Count > maxMemoEntries)
            {
                memo.Clear();
            }

            var valArray = rawValue2Block as object?[,];
            var fmlArray = rawFormulaBlock as object?[,];

            int valRowLb = valArray?.GetLowerBound(0) ?? 0;
            int valColLb = valArray?.GetLowerBound(1) ?? 0;
            int fmlRowLb = fmlArray?.GetLowerBound(0) ?? 0;
            int fmlColLb = fmlArray?.GetLowerBound(1) ?? 0;

            // Multi-core distinct-string prewarm for large 2D blocks when hardware governor permits >= 2 workers
            if (valArray != null && blockCells >= 512 && budgets.MaxBackgroundWorkers > 1)
            {
                HashSet<string>? distinctSet = null;
                List<string>? unseenList = null;

                for (int r = 0; r < rowCount; r++)
                {
                    if ((r & 63) == 0 && cancellationToken.IsCancellationRequested)
                        return results;

                    for (int c = 0; c < colCount; c++)
                    {
                        object? cellVal = valArray[valRowLb + r, valColLb + c];
                        if (!(cellVal is string s) || s.Length == 0 || s.Length > 800 || memo.ContainsKey(s))
                            continue;

                        if (!ShouldInspectCellText(s, out string cellText))
                            continue;

                        object? cellFml = fmlArray != null ? fmlArray[fmlRowLb + r, fmlColLb + c] : null;
                        if (IsFormulaCell(cellVal, cellFml, blockHasFormula))
                            continue;

                        distinctSet ??= new HashSet<string>(StringComparer.Ordinal);
                        if (distinctSet.Add(cellText))
                        {
                            unseenList ??= new List<string>(128);
                            unseenList.Add(cellText);
                        }
                    }
                }

                if (unseenList != null && unseenList.Count >= 32)
                {
                    var computed = new IReadOnlyList<ProofingIssue>[unseenList.Count];
                    int maxDegree = budgets.MaxBackgroundWorkers;
                    Parallel.For(
                        0,
                        unseenList.Count,
                        new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
                        (idx, loopState) =>
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                loopState.Stop();
                                return;
                            }
                            computed[idx] = engine.CheckText(unseenList[idx]);
                        });

                    if (cancellationToken.IsCancellationRequested)
                        return results;

                    for (int idx = 0; idx < unseenList.Count && memo.Count < maxMemoEntries; idx++)
                    {
                        if (computed[idx] != null)
                        {
                            memo[unseenList[idx]] = computed[idx];
                        }
                    }
                }
            }

            int nlpEvaluations = 0;
            for (int r = 0; r < rowCount; r++)
            {
                if ((r & 31) == 0 && cancellationToken.IsCancellationRequested)
                    return results;

                for (int c = 0; c < colCount; c++)
                {
                    object? cellVal = valArray != null
                        ? valArray[valRowLb + r, valColLb + c]
                        : (r == 0 && c == 0 ? rawValue2Block : null);

                    if (!ShouldInspectCellText(cellVal, out string cellText))
                        continue;

                    object? cellFml = fmlArray != null
                        ? fmlArray[fmlRowLb + r, fmlColLb + c]
                        : (r == 0 && c == 0 ? rawFormulaBlock : null);

                    if (IsFormulaCell(cellVal, cellFml, blockHasFormula))
                        continue;

                    IReadOnlyList<ProofingIssue> cellIssues;
                    if (cellText.Length <= 800)
                    {
                        if (!memo.TryGetValue(cellText, out cellIssues!))
                        {
                            nlpEvaluations++;
                            EcoPerformanceGovernor.MaybeMicroYield(nlpEvaluations, budgets.MicroYieldEveryItems);
                            cellIssues = engine.CheckText(cellText);
                            if (memo.Count < maxMemoEntries)
                            {
                                memo[cellText] = cellIssues;
                            }
                        }
                    }
                    else
                    {
                        nlpEvaluations++;
                        EcoPerformanceGovernor.MaybeMicroYield(nlpEvaluations, budgets.MicroYieldEveryItems);
                        cellIssues = engine.CheckText(cellText);
                    }

                    if (cellIssues == null || cellIssues.Count == 0)
                        continue;

                    int sheetRow = baseRowOneBased + r;
                    int sheetCol = baseColOneBased + c;

                    for (int k = 0; k < cellIssues.Count; k++)
                    {
                        results.Add(new SpreadsheetCellIssue
                        {
                            WorkbookName = workbookName ?? string.Empty,
                            SheetName = sheetName ?? string.Empty,
                            Row = sheetRow,
                            Column = sheetCol,
                            CellSnapshotText = cellText,
                            Issue = CloneIssue(cellIssues[k]),
                            IsResolved = false
                        });

                        if (results.Count >= maxIssuesToCollect)
                            return results;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Replaces a single flagged issue inside its cell's current text and shifts character offsets (StartIndex)
        /// for all remaining unresolved sibling issues in the exact same cell so subsequent clicks remain 100% accurate.
        /// </summary>
        public static bool ApplySingleIssueReplacement(
            string currentCellText,
            SpreadsheetCellIssue targetItem,
            string replacement,
            IEnumerable<SpreadsheetCellIssue>? allCachedItems,
            out string updatedCellText)
        {
            updatedCellText = currentCellText ?? string.Empty;
            if (targetItem == null || targetItem.IsResolved || string.IsNullOrEmpty(currentCellText) || replacement == null)
                return false;

            string liveText = currentCellText!;
            if (!TryLocateIssueSpan(liveText, targetItem.Issue, out int matchStart, out int matchLen))
                return false;

            updatedCellText =
                liveText.Substring(0, matchStart) +
                replacement +
                liveText.Substring(matchStart + matchLen);

            int delta = replacement.Length - matchLen;
            targetItem.IsResolved = true;
            targetItem.CellSnapshotText = updatedCellText;

            if (allCachedItems != null)
            {
                foreach (var sibling in allCachedItems)
                {
                    if (ReferenceEquals(sibling, targetItem))
                        continue;

                    if (!IsSameCell(sibling, targetItem))
                        continue;

                    sibling.CellSnapshotText = updatedCellText;
                    if (sibling.IsResolved)
                        continue;

                    int sibStart = sibling.Issue.StartIndex;
                    int sibEnd = sibStart + sibling.Issue.Length;

                    if (sibStart >= matchStart + matchLen)
                    {
                        sibling.Issue.StartIndex = sibStart + delta;
                    }
                    else if (sibEnd > matchStart)
                    {
                        // Overlapping span in the same cell was superseded by this edit
                        sibling.IsResolved = true;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Groups eligible auto-fixable issues by cell, applies all non-overlapping replacements in descending StartIndex order
        /// (1 COM write per cell!), skips Amber ContextualMeaningWarning items, and shifts offsets on any remaining unfixed
        /// issues in modified cells so their positions stay aligned with the updated cell text.
        /// </summary>
        public static List<CellWriteOperation> BuildBatchReplacementsByCell(
            IReadOnlyList<SpreadsheetCellIssue> allItems,
            Func<SpreadsheetCellIssue, bool>? filterPredicate = null,
            Func<SpreadsheetCellIssue, string?>? resolveLiveCellText = null)
        {
            var operations = new List<CellWriteOperation>();
            if (allItems == null || allItems.Count == 0)
                return operations;

            var byCell = allItems
                .Where(i => !i.IsResolved)
                .GroupBy(i => (
                    Wb: i.WorkbookName ?? string.Empty,
                    Sh: i.SheetName ?? string.Empty,
                    i.Row,
                    i.Column));

            foreach (var cellGroup in byCell)
            {
                List<SpreadsheetCellIssue> cellItems = cellGroup.ToList();
                var candidates = cellItems
                    .Where(i =>
                        !i.IsResolved &&
                        i.Issue.IsAutoFixSafe &&
                        i.Issue.Suggestions != null &&
                        i.Issue.Suggestions.Count > 0 &&
                        (filterPredicate == null || filterPredicate(i)))
                    .OrderByDescending(i => i.Issue.StartIndex)
                    .ThenByDescending(i => i.Issue.Length)
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                SpreadsheetCellIssue anchor = candidates[0];
                string originalCellText = resolveLiveCellText?.Invoke(anchor) ?? anchor.CellSnapshotText;
                if (string.IsNullOrEmpty(originalCellText))
                    continue;

                string working = originalCellText;
                int lastReplacedStart = int.MaxValue;
                int appliedInCell = 0;
                var editsAsc = new List<(int OrigStart, int OrigLen, int Delta)>(candidates.Count);

                foreach (var item in candidates)
                {
                    string topReplacement = item.Issue.Suggestions[0].Replacement;
                    if (string.IsNullOrEmpty(topReplacement))
                        continue;

                    int st = item.Issue.StartIndex;
                    int ln = item.Issue.Length;
                    if (st < 0 || ln <= 0 || st + ln > working.Length || st + ln > lastReplacedStart)
                        continue;

                    string slice = working.Substring(st, ln);
                    if (!StringsMatchCanonical(slice, item.Issue.OriginalText))
                        continue;

                    working = working.Substring(0, st) + topReplacement + working.Substring(st + ln);
                    lastReplacedStart = st;
                    appliedInCell++;
                    item.IsResolved = true;
                    editsAsc.Add((st, ln, topReplacement.Length - ln));
                }

                if (appliedInCell == 0 || string.Equals(working, originalCellText, StringComparison.Ordinal))
                    continue;

                editsAsc.Reverse(); // Now ascending by OrigStart
                ShiftRemainingCellSiblings(cellItems, working, editsAsc);

                operations.Add(new CellWriteOperation
                {
                    WorkbookName = anchor.WorkbookName,
                    SheetName = anchor.SheetName,
                    Row = anchor.Row,
                    Column = anchor.Column,
                    PreviousText = originalCellText,
                    UpdatedText = working,
                    ChangesInCell = appliedInCell
                });
            }

            return operations;
        }

        /// <summary>
        /// Applies only the exact issues explicitly approved (☑) by the user in the Safe Fix-All Preview Checklist Dialog,
        /// using each item's user-confirmed replacement string (even if the user opted in to an Amber warning or chose a secondary candidate),
        /// while leaving all unchecked (☐) items untouched and shifting their offsets cleanly.
        /// </summary>
        public static List<CellWriteOperation> BuildApprovedReplacementsByCell(
            IReadOnlyList<SpreadsheetCellIssue> allItems,
            IReadOnlyDictionary<SpreadsheetCellIssue, string> approvedReplacements,
            Func<SpreadsheetCellIssue, string?>? resolveLiveCellText = null)
        {
            var operations = new List<CellWriteOperation>();
            if (allItems == null || allItems.Count == 0 || approvedReplacements == null || approvedReplacements.Count == 0)
                return operations;

            var byCell = allItems
                .Where(i => !i.IsResolved)
                .GroupBy(i => (
                    Wb: i.WorkbookName ?? string.Empty,
                    Sh: i.SheetName ?? string.Empty,
                    i.Row,
                    i.Column));

            foreach (var cellGroup in byCell)
            {
                List<SpreadsheetCellIssue> cellItems = cellGroup.ToList();
                var candidates = cellItems
                    .Where(i =>
                        !i.IsResolved &&
                        approvedReplacements.TryGetValue(i, out string? rep) &&
                        !string.IsNullOrEmpty(rep))
                    .OrderByDescending(i => i.Issue.StartIndex)
                    .ThenByDescending(i => i.Issue.Length)
                    .ToList();

                if (candidates.Count == 0)
                    continue;

                SpreadsheetCellIssue anchor = candidates[0];
                string originalCellText = resolveLiveCellText?.Invoke(anchor) ?? anchor.CellSnapshotText;
                if (string.IsNullOrEmpty(originalCellText))
                    continue;

                string working = originalCellText;
                int lastReplacedStart = int.MaxValue;
                int appliedInCell = 0;
                var editsAsc = new List<(int OrigStart, int OrigLen, int Delta)>(candidates.Count);

                foreach (var item in candidates)
                {
                    if (!approvedReplacements.TryGetValue(item, out string? chosenReplacement) || string.IsNullOrEmpty(chosenReplacement))
                        continue;

                    int st = item.Issue.StartIndex;
                    int ln = item.Issue.Length;
                    if (st < 0 || ln <= 0 || st + ln > working.Length || st + ln > lastReplacedStart)
                        continue;

                    string slice = working.Substring(st, ln);
                    if (!StringsMatchCanonical(slice, item.Issue.OriginalText))
                        continue;

                    working = working.Substring(0, st) + chosenReplacement + working.Substring(st + ln);
                    lastReplacedStart = st;
                    appliedInCell++;
                    item.IsResolved = true;
                    editsAsc.Add((st, ln, chosenReplacement.Length - ln));
                }

                if (appliedInCell == 0 || string.Equals(working, originalCellText, StringComparison.Ordinal))
                    continue;

                editsAsc.Reverse();
                ShiftRemainingCellSiblings(cellItems, working, editsAsc);

                operations.Add(new CellWriteOperation
                {
                    WorkbookName = anchor.WorkbookName,
                    SheetName = anchor.SheetName,
                    Row = anchor.Row,
                    Column = anchor.Column,
                    PreviousText = originalCellText,
                    UpdatedText = working,
                    ChangesInCell = appliedInCell
                });
            }

            return operations;
        }

        /// <summary>
        /// Replaces all occurrences of a specific flagged originalText -> replacementText across all cells in one batch.
        /// </summary>
        public static List<CellWriteOperation> BuildSameWordBatchReplacementsByCell(
            IReadOnlyList<SpreadsheetCellIssue> allItems,
            string originalText,
            string replacementText,
            Func<SpreadsheetCellIssue, string?>? resolveLiveCellText = null)
        {
            var operations = new List<CellWriteOperation>();
            if (allItems == null || allItems.Count == 0 || string.IsNullOrWhiteSpace(originalText) || replacementText == null)
                return operations;

            string normTarget = UnicodeNormalizer.NormalizeCanonicalOrder(originalText.Trim());

            var byCell = allItems
                .Where(i => !i.IsResolved)
                .GroupBy(i => (
                    Wb: i.WorkbookName ?? string.Empty,
                    Sh: i.SheetName ?? string.Empty,
                    i.Row,
                    i.Column));

            foreach (var cellGroup in byCell)
            {
                List<SpreadsheetCellIssue> cellItems = cellGroup.ToList();
                var matching = cellItems
                    .Where(i =>
                        !i.IsResolved &&
                        string.Equals(
                            UnicodeNormalizer.NormalizeCanonicalOrder(i.Issue.OriginalText.Trim()),
                            normTarget,
                            StringComparison.Ordinal))
                    .OrderByDescending(i => i.Issue.StartIndex)
                    .ToList();

                if (matching.Count == 0)
                    continue;

                SpreadsheetCellIssue anchor = matching[0];
                string originalCellText = resolveLiveCellText?.Invoke(anchor) ?? anchor.CellSnapshotText;
                if (string.IsNullOrEmpty(originalCellText))
                    continue;

                string working = originalCellText;
                int lastReplacedStart = int.MaxValue;
                int appliedInCell = 0;
                var editsAsc = new List<(int OrigStart, int OrigLen, int Delta)>(matching.Count);

                foreach (var item in matching)
                {
                    int st = item.Issue.StartIndex;
                    int ln = item.Issue.Length;
                    if (st < 0 || ln <= 0 || st + ln > working.Length || st + ln > lastReplacedStart)
                        continue;

                    string slice = working.Substring(st, ln);
                    if (!StringsMatchCanonical(slice, item.Issue.OriginalText))
                        continue;

                    working = working.Substring(0, st) + replacementText + working.Substring(st + ln);
                    lastReplacedStart = st;
                    appliedInCell++;
                    item.IsResolved = true;
                    editsAsc.Add((st, ln, replacementText.Length - ln));
                }

                if (appliedInCell == 0 || string.Equals(working, originalCellText, StringComparison.Ordinal))
                    continue;

                editsAsc.Reverse();
                ShiftRemainingCellSiblings(cellItems, working, editsAsc);

                operations.Add(new CellWriteOperation
                {
                    WorkbookName = anchor.WorkbookName,
                    SheetName = anchor.SheetName,
                    Row = anchor.Row,
                    Column = anchor.Column,
                    PreviousText = originalCellText,
                    UpdatedText = working,
                    ChangesInCell = appliedInCell
                });
            }

            return operations;
        }

        /// <summary>
        /// Transforms legacy Zawgyi-One or WinInnwa/CE text cells in a 2D block into canonical Pyidaungsu Unicode 5.1+,
        /// strictly skipping formula cells and non-text numeric/date cells.
        /// </summary>
        public static List<CellWriteOperation> TransformBlockLegacyFontsToPyidaungsu(
            string workbookName,
            string sheetName,
            int baseRowOneBased,
            int baseColOneBased,
            int rowCount,
            int colCount,
            object? rawValue2Block,
            object? rawFormulaBlock,
            bool? blockHasFormula,
            Func<int, int, string?>? getCellFontName = null)
        {
            return TransformTextCellsCore(
                workbookName,
                sheetName,
                baseRowOneBased,
                baseColOneBased,
                rowCount,
                colCount,
                rawValue2Block,
                rawFormulaBlock,
                blockHasFormula,
                (r, c, text) =>
                {
                    string? fontName = getCellFontName?.Invoke(r, c);
                    string converted = LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu(text, fontName, out bool wasConverted);
                    bool wasZawgyiFont = LegacyMyanmarFontConverter.IsZawgyiFont(fontName) || LegacyMyanmarFontConverter.IsLegacyAsciiMyanmarFont(fontName);
                    if (!wasConverted && !wasZawgyiFont)
                        return (text, 0, fontName, null);

                    int changes = wasConverted ? 1 : 0;
                    return (converted, Math.Max(1, changes), fontName, "Pyidaungsu");
                });
        }

        /// <summary>
        /// Smart 0-9 <-> ၀-၉ numeral conversion on string text cells ONLY.
        /// Never touches numeric cells (double/decimal) or formula cells so Excel SUM/AVERAGE/VLOOKUP math never breaks!
        /// </summary>
        public static List<CellWriteOperation> TransformBlockNumerals(
            string workbookName,
            string sheetName,
            int baseRowOneBased,
            int baseColOneBased,
            int rowCount,
            int colCount,
            object? rawValue2Block,
            object? rawFormulaBlock,
            bool? blockHasFormula,
            bool toMyanmarDigits)
        {
            return TransformTextCellsCore(
                workbookName,
                sheetName,
                baseRowOneBased,
                baseColOneBased,
                rowCount,
                colCount,
                rawValue2Block,
                rawFormulaBlock,
                blockHasFormula,
                (r, c, text) =>
                {
                    // When converting ASCII 0-9 -> Myanmar ၀-၉ in spreadsheets, only convert text cells that actually contain
                    // Myanmar prose (so pure English/SKU/serial text columns like "INV-2026-001" or "'00125" stay untouched!)
                    if (toMyanmarDigits && !ContainsMyanmarScript(text))
                        return (text, 0, null, null);

                    string converted = MyanmarTypographyFormatter.ConvertNumeralsSmart(text, toMyanmarDigits, out int count);
                    return (converted, count, null, null);
                });
        }

        /// <summary>
        /// Rewrites colloquial Burmese (ပါတယ်, တွေ, နဲ့, ဒီနေ့...) into formal official prose on text cells, skipping formulas.
        /// </summary>
        public static List<CellWriteOperation> TransformBlockOfficialProse(
            string workbookName,
            string sheetName,
            int baseRowOneBased,
            int baseColOneBased,
            int rowCount,
            int colCount,
            object? rawValue2Block,
            object? rawFormulaBlock,
            bool? blockHasFormula)
        {
            return TransformTextCellsCore(
                workbookName,
                sheetName,
                baseRowOneBased,
                baseColOneBased,
                rowCount,
                colCount,
                rawValue2Block,
                rawFormulaBlock,
                blockHasFormula,
                (r, c, text) =>
                {
                    if (!ContainsMyanmarScript(text))
                        return (text, 0, null, null);

                    OfficialRewriteResult res = MyanmarOfficialRewriter.ConvertToOfficialProse(text);
                    return (res.RewrittenText, res.ReplacementCount, null, null);
                });
        }

        /// <summary>
        /// Normalizes Burmese punctuation (၊ / ။ spacing) and strips broken mid-word ZWSP on text cells, skipping formulas.
        /// </summary>
        public static List<CellWriteOperation> TransformBlockPunctuation(
            string workbookName,
            string sheetName,
            int baseRowOneBased,
            int baseColOneBased,
            int rowCount,
            int colCount,
            object? rawValue2Block,
            object? rawFormulaBlock,
            bool? blockHasFormula)
        {
            return TransformTextCellsCore(
                workbookName,
                sheetName,
                baseRowOneBased,
                baseColOneBased,
                rowCount,
                colCount,
                rawValue2Block,
                rawFormulaBlock,
                blockHasFormula,
                (r, c, text) =>
                {
                    if (!ContainsMyanmarScript(text))
                        return (text, 0, null, null);

                    string cleaned = MyanmarTypographyFormatter.FormatPunctuationAndCleanInvisible(text, out int pFixes, out int zwFixes);
                    int total = pFixes + zwFixes + (string.Equals(cleaned, text, StringComparison.Ordinal) ? 0 : 1);
                    return (cleaned, total, null, null);
                });
        }

        private static List<CellWriteOperation> TransformTextCellsCore(
            string workbookName,
            string sheetName,
            int baseRowOneBased,
            int baseColOneBased,
            int rowCount,
            int colCount,
            object? rawValue2Block,
            object? rawFormulaBlock,
            bool? blockHasFormula,
            Func<int, int, string, (string Updated, int ChangeCount, string? PrevFont, string? TargetFont)> transformer)
        {
            var ops = new List<CellWriteOperation>();
            if (rowCount <= 0 || colCount <= 0 || blockHasFormula == true || rawValue2Block == null)
                return ops;

            var valArray = rawValue2Block as object?[,];
            var fmlArray = rawFormulaBlock as object?[,];

            int valRowLb = valArray?.GetLowerBound(0) ?? 0;
            int valColLb = valArray?.GetLowerBound(1) ?? 0;
            int fmlRowLb = fmlArray?.GetLowerBound(0) ?? 0;
            int fmlColLb = fmlArray?.GetLowerBound(1) ?? 0;

            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    object? cellVal = valArray != null
                        ? valArray[valRowLb + r, valColLb + c]
                        : (r == 0 && c == 0 ? rawValue2Block : null);

                    if (!(cellVal is string cellText) || string.IsNullOrWhiteSpace(cellText))
                        continue;

                    object? cellFml = fmlArray != null
                        ? fmlArray[fmlRowLb + r, fmlColLb + c]
                        : (r == 0 && c == 0 ? rawFormulaBlock : null);

                    if (IsFormulaCell(cellVal, cellFml, blockHasFormula))
                        continue;

                    var (updated, changeCount, prevFont, targetFont) = transformer(r, c, cellText);
                    bool textChanged = !string.Equals(updated, cellText, StringComparison.Ordinal);
                    bool fontChanged = !string.IsNullOrEmpty(targetFont) &&
                                       !string.Equals(prevFont, targetFont, StringComparison.OrdinalIgnoreCase);

                    if (!textChanged && !fontChanged)
                        continue;

                    ops.Add(new CellWriteOperation
                    {
                        WorkbookName = workbookName ?? string.Empty,
                        SheetName = sheetName ?? string.Empty,
                        Row = baseRowOneBased + r,
                        Column = baseColOneBased + c,
                        PreviousText = cellText,
                        UpdatedText = updated,
                        ChangesInCell = Math.Max(1, changeCount),
                        PreviousFontName = prevFont,
                        TargetFontName = targetFont
                    });
                }
            }

            return ops;
        }

        private static void ShiftRemainingCellSiblings(
            List<SpreadsheetCellIssue> cellItems,
            string updatedCellText,
            List<(int OrigStart, int OrigLen, int Delta)> editsAsc)
        {
            foreach (var sib in cellItems)
            {
                sib.CellSnapshotText = updatedCellText;
                if (sib.IsResolved)
                    continue;

                int origStart = sib.Issue.StartIndex;
                int origEnd = origStart + sib.Issue.Length;
                int cumulativeDelta = 0;
                bool overlapped = false;

                for (int i = 0; i < editsAsc.Count; i++)
                {
                    var ed = editsAsc[i];
                    int edEnd = ed.OrigStart + ed.OrigLen;
                    if (edEnd <= origStart)
                    {
                        cumulativeDelta += ed.Delta;
                    }
                    else if (ed.OrigStart < origEnd)
                    {
                        overlapped = true;
                        break;
                    }
                }

                if (overlapped)
                {
                    sib.IsResolved = true;
                }
                else if (cumulativeDelta != 0)
                {
                    sib.Issue.StartIndex = origStart + cumulativeDelta;
                }
            }
        }

        private static bool TryLocateIssueSpan(string cellText, ProofingIssue issue, out int matchStart, out int matchLen)
        {
            matchStart = -1;
            matchLen = 0;
            if (issue == null || string.IsNullOrEmpty(issue.OriginalText))
                return false;

            int st = issue.StartIndex;
            int ln = issue.Length > 0 ? issue.Length : issue.OriginalText.Length;

            if (st >= 0 && st + ln <= cellText.Length)
            {
                string slice = cellText.Substring(st, ln);
                if (StringsMatchCanonical(slice, issue.OriginalText))
                {
                    matchStart = st;
                    matchLen = ln;
                    return true;
                }
            }

            // Fallback: locate nearest exact occurrence of OriginalText to issue.StartIndex
            int bestIdx = -1;
            int bestDist = int.MaxValue;
            int searchFrom = 0;
            while (searchFrom < cellText.Length)
            {
                int idx = cellText.IndexOf(issue.OriginalText, searchFrom, StringComparison.Ordinal);
                if (idx < 0)
                    break;

                int dist = Math.Abs(idx - Math.Max(0, st));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = idx;
                }
                searchFrom = idx + 1;
            }

            if (bestIdx >= 0)
            {
                matchStart = bestIdx;
                matchLen = issue.OriginalText.Length;
                return true;
            }

            return false;
        }

        private static bool IsSameCell(SpreadsheetCellIssue a, SpreadsheetCellIssue b)
        {
            return a.Row == b.Row &&
                   a.Column == b.Column &&
                   string.Equals(a.SheetName, b.SheetName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(a.WorkbookName, b.WorkbookName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StringsMatchCanonical(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal))
                return true;

            return string.Equals(
                UnicodeNormalizer.NormalizeCanonicalOrder(a),
                UnicodeNormalizer.NormalizeCanonicalOrder(b),
                StringComparison.Ordinal);
        }

        private static ProofingIssue CloneIssue(ProofingIssue src)
        {
            var copy = new ProofingIssue
            {
                StartIndex = src.StartIndex,
                Length = src.Length,
                OriginalText = src.OriginalText,
                IssueType = src.IssueType,
                Message = src.Message,
                GrammaticalReasoning = src.GrammaticalReasoning,
                AuthoritativeSource = src.AuthoritativeSource,
                RequiresReviewBeforeBatchFix = src.RequiresReviewBeforeBatchFix,
                Suggestions = new List<SuggestionCandidate>(src.Suggestions.Count)
            };

            for (int i = 0; i < src.Suggestions.Count; i++)
            {
                var s = src.Suggestions[i];
                copy.Suggestions.Add(new SuggestionCandidate
                {
                    Replacement = s.Replacement,
                    MeaningCue = s.MeaningCue,
                    Score = s.Score,
                    PosTag = s.PosTag,
                    IsOfficialThatpon = s.IsOfficialThatpon,
                    SyllableBreakdown = s.SyllableBreakdown,
                    DiagnosticDetail = s.DiagnosticDetail,
                    HomophoneSiblings = s.HomophoneSiblings,
                    AbidanSummary = s.AbidanSummary
                });
            }

            return copy;
        }
    }
}
