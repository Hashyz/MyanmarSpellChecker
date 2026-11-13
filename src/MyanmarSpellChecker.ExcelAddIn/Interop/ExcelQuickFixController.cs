// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.ExcelAddIn.UI;
using Excel = Microsoft.Office.Interop.Excel;
using Office = Microsoft.Office.Core;

namespace MyanmarSpellChecker.ExcelAddIn.Interop
{
    /// <summary>
    /// Coordinates Word-style on-cursor Quick-Fix Popup cards and native Excel "Cell" right-click CommandBar
    /// suggestions when the user selects or right-clicks a flagged cell in Microsoft Excel.
    /// </summary>
    internal sealed class ExcelQuickFixController : IDisposable
    {
        private const string ContextMenuTag = "MM_EXCEL_CTX_ITEM";
        private static readonly string[] s_targetCellMenuNames = { "Cell", "List Range Popup" };

        private readonly Excel.Application _excelApp;
        private readonly ExcelWorkbookScannerAndToolkit _scanner;
        private readonly Action<SpreadsheetCellIssue, string> _onApplySingleFix;
        private readonly Action<string, string> _onApplyBatchSameWord;
        private readonly Action<SpreadsheetCellIssue> _onIgnoreIssue;
        private readonly Action<SpreadsheetCellIssue> _onAddToDictionary;
        private readonly Action<SpreadsheetCellIssue?>? _onHighlightInTaskPane;
        private readonly Action? _onPopupVisibilityChanged;

        // Strong RCW list so GC never collects CommandBarButton click delegates before the user clicks them
        private readonly List<Office.CommandBarButton> _pinnedMenuButtons = new List<Office.CommandBarButton>();
        private readonly Timer _selectionPopupTimer;

        private ExcelQuickFixPopup? _activePopup;
        private string _activePopupIdentity = string.Empty;
        private int _pendingIssueIndexInCell;
        private bool _disposed;

        public IntPtr ActivePopupHwnd =>
            (_activePopup != null && !_activePopup.IsDisposed && _activePopup.Visible && _activePopup.IsHandleCreated)
                ? _activePopup.Handle
                : IntPtr.Zero;

        public Rectangle? ActivePopupScreenBounds =>
            (_activePopup != null && !_activePopup.IsDisposed) ? _activePopup.CurrentScreenBounds : null;

        public ExcelQuickFixController(
            Excel.Application excelApp,
            ExcelWorkbookScannerAndToolkit scanner,
            Action<SpreadsheetCellIssue, string> onApplySingleFix,
            Action<string, string> onApplyBatchSameWord,
            Action<SpreadsheetCellIssue> onIgnoreIssue,
            Action<SpreadsheetCellIssue> onAddToDictionary,
            Action<SpreadsheetCellIssue?>? onHighlightInTaskPane = null,
            Action? onPopupVisibilityChanged = null)
        {
            _excelApp = excelApp ?? throw new ArgumentNullException(nameof(excelApp));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _onApplySingleFix = onApplySingleFix ?? throw new ArgumentNullException(nameof(onApplySingleFix));
            _onApplyBatchSameWord = onApplyBatchSameWord ?? throw new ArgumentNullException(nameof(onApplyBatchSameWord));
            _onIgnoreIssue = onIgnoreIssue ?? throw new ArgumentNullException(nameof(onIgnoreIssue));
            _onAddToDictionary = onAddToDictionary ?? throw new ArgumentNullException(nameof(onAddToDictionary));
            _onHighlightInTaskPane = onHighlightInTaskPane;
            _onPopupVisibilityChanged = onPopupVisibilityChanged;

            _selectionPopupTimer = new Timer { Interval = 48 };
            _selectionPopupTimer.Tick += OnSelectionPopupTimerTick;
        }

        public void OnSheetSelectionChange(object sh, Excel.Range target)
        {
            if (_disposed)
                return;

            _pendingIssueIndexInCell = 0;
            _selectionPopupTimer.Stop();

            if (!TryNormalizeToSingleLogicalCell(target, out Excel.Range? anchorCell) || anchorCell == null)
            {
                CloseActivePopup();
                _onHighlightInTaskPane?.Invoke(null);
                return;
            }

            // Close previous cell's popup immediately when moving to a different cell, then debounce 48ms for rapid arrow-key repeats
            try
            {
                if (sh is Excel.Worksheet ws)
                {
                    Excel.Workbook? wb = ws.Parent as Excel.Workbook;
                    string wbName = wb != null ? ExcelWorkbookScannerAndToolkit.SafeGetWorkbookName(wb) : string.Empty;
                    string sheetName = ExcelWorkbookScannerAndToolkit.SafeGetSheetName(ws);
                    string cellPrefix = $"{wbName}|{sheetName}|{anchorCell.Row},{anchorCell.Column}|";
                    if (!_activePopupIdentity.StartsWith(cellPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        CloseActivePopup();
                    }
                }
            }
            catch
            {
                CloseActivePopup();
            }

            _selectionPopupTimer.Interval = 48;
            _selectionPopupTimer.Start();
        }

        public void ScheduleRefreshForActiveCell(int delayMs = 65)
        {
            if (_disposed)
                return;

            _selectionPopupTimer.Stop();
            _selectionPopupTimer.Interval = Math.Max(25, delayMs);
            _selectionPopupTimer.Start();
        }

        public void OnSheetBeforeDoubleClick(object sh, Excel.Range target, ref bool cancel)
        {
            _selectionPopupTimer.Stop();
            CloseActivePopup();
        }

        public void OnSheetBeforeRightClick(object sh, Excel.Range target, ref bool cancel)
        {
            _selectionPopupTimer.Stop();
            CloseActivePopup();
            ClearCustomContextMenuItems();

            if (_disposed || !(sh is Excel.Worksheet ws))
                return;

            if (!TryNormalizeToSingleLogicalCell(target, out Excel.Range? anchorCell) || anchorCell == null)
                return;

            try
            {
                Excel.Workbook? wb = ws.Parent as Excel.Workbook ?? _excelApp.ActiveWorkbook;
                if (wb == null)
                    return;

                string wbName = ExcelWorkbookScannerAndToolkit.SafeGetWorkbookName(wb);
                string sheetName = ExcelWorkbookScannerAndToolkit.SafeGetSheetName(ws);
                List<SpreadsheetCellIssue> cellIssues = _scanner.GetUnresolvedIssuesForSingleCell(
                    wbName,
                    sheetName,
                    anchorCell.Row,
                    anchorCell.Column,
                    ws,
                    anchorCell);

                if (cellIssues.Count == 0)
                    return;

                SpreadsheetCellIssue primaryItem = cellIssues[0];
                _onHighlightInTaskPane?.Invoke(primaryItem);
                PopulateNativeCellContextMenu(primaryItem, cellIssues.Count);
            }
            catch
            {
                // Never interfere with Excel's native context menu on error
            }
        }

        private void OnSelectionPopupTimerTick(object? sender, EventArgs e)
        {
            _selectionPopupTimer.Stop();
            if (_disposed || !ExcelWorkbookScannerAndToolkit.IsExcelReadyForInterop(_excelApp))
            {
                CloseActivePopup();
                return;
            }

            try
            {
                if (!ExcelCellGeometryHelper.TryGetExcelGridViewport(_excelApp, out IntPtr xlMainHwnd, out _, out _, out _, out _))
                {
                    CloseActivePopup();
                    return;
                }

                if (!ExcelCellGeometryHelper.IsWorksheetUnobstructedInForeground(xlMainHwnd, ActivePopupHwnd, out bool isFg) || !isFg)
                {
                    CloseActivePopup();
                    return;
                }

                Excel.Workbook? wb = _excelApp.ActiveWorkbook;
                Excel.Worksheet? ws = wb?.ActiveSheet as Excel.Worksheet;
                Excel.Range? selection = _excelApp.Selection as Excel.Range;
                if (wb == null || ws == null || selection == null)
                {
                    CloseActivePopup();
                    return;
                }

                if (!TryNormalizeToSingleLogicalCell(selection, out Excel.Range? anchorCell) || anchorCell == null)
                {
                    CloseActivePopup();
                    _onHighlightInTaskPane?.Invoke(null);
                    return;
                }

                string wbName = ExcelWorkbookScannerAndToolkit.SafeGetWorkbookName(wb);
                string sheetName = ExcelWorkbookScannerAndToolkit.SafeGetSheetName(ws);
                List<SpreadsheetCellIssue> cellIssues = _scanner.GetUnresolvedIssuesForSingleCell(
                    wbName,
                    sheetName,
                    anchorCell.Row,
                    anchorCell.Column,
                    ws,
                    anchorCell);

                if (cellIssues.Count == 0)
                {
                    CloseActivePopup();
                    _onHighlightInTaskPane?.Invoke(null);
                    return;
                }

                int idx = (_pendingIssueIndexInCell >= 0 && _pendingIssueIndexInCell < cellIssues.Count)
                    ? _pendingIssueIndexInCell
                    : 0;
                SpreadsheetCellIssue targetIssue = cellIssues[idx];
                _onHighlightInTaskPane?.Invoke(targetIssue);

                string identity = $"{wbName}|{sheetName}|{anchorCell.Row},{anchorCell.Column}|{idx}|{targetIssue.Issue.OriginalText}";
                if (_activePopup != null && !_activePopup.IsDisposed && _activePopup.Visible &&
                    string.Equals(_activePopupIdentity, identity, StringComparison.Ordinal))
                {
                    return;
                }

                if (!ExcelCellGeometryHelper.TryGetCellScreenRectangle(_excelApp, anchorCell, out Rectangle cellScreenRect, out _, out _, out _))
                {
                    CloseActivePopup();
                    return;
                }

                ShowFloatingQuickFixPopup(targetIssue, idx, cellIssues.Count, cellScreenRect, identity);
            }
            catch
            {
                CloseActivePopup();
            }
        }

        private void ShowFloatingQuickFixPopup(
            SpreadsheetCellIssue targetIssue,
            int cellIssueIndex,
            int totalCellIssues,
            Rectangle cellScreenRect,
            string identity)
        {
            CloseActivePopup();

            int dupCount = _scanner.CountMatchingUnresolvedOccurrencesInWorkbook(targetIssue.Issue.OriginalText);
            string origText = targetIssue.Issue.OriginalText;

            var popup = new ExcelQuickFixPopup(
                targetIssue,
                onSelectReplacement: replacement =>
                {
                    _activePopupIdentity = string.Empty;
                    _onApplySingleFix(targetIssue, replacement);
                    // If the cell had multiple issues, automatically surface the next remaining issue in this cell!
                    _pendingIssueIndexInCell = 0;
                    ScheduleRefreshForActiveCell(55);
                },
                onIgnoreOnce: () =>
                {
                    _activePopupIdentity = string.Empty;
                    _onIgnoreIssue(targetIssue);
                    _pendingIssueIndexInCell = 0;
                    ScheduleRefreshForActiveCell(55);
                },
                onAddToDictionary: () =>
                {
                    _activePopupIdentity = string.Empty;
                    _onAddToDictionary(targetIssue);
                    _pendingIssueIndexInCell = 0;
                    ScheduleRefreshForActiveCell(55);
                },
                duplicateCount: dupCount,
                onBatchReplaceAllMatching: replacement =>
                {
                    _activePopupIdentity = string.Empty;
                    _onApplyBatchSameWord(origText, replacement);
                    _pendingIssueIndexInCell = 0;
                    ScheduleRefreshForActiveCell(55);
                },
                cellIssueIndex: cellIssueIndex,
                totalCellIssues: totalCellIssues,
                onStepCellIssue: nextIdx =>
                {
                    _activePopupIdentity = string.Empty;
                    _pendingIssueIndexInCell = nextIdx;
                    ScheduleRefreshForActiveCell(30);
                });

            popup.PopupClosedSafe += () =>
            {
                if (ReferenceEquals(_activePopup, popup))
                {
                    _activePopup = null;
                    _activePopupIdentity = string.Empty;
                }
                _onPopupVisibilityChanged?.Invoke();
            };

            _activePopup = popup;
            _activePopupIdentity = identity;
            popup.ShowNearCellOrCursor(cellScreenRect, focusPopup: false);
            _onPopupVisibilityChanged?.Invoke();
        }

        private void PopulateNativeCellContextMenu(SpreadsheetCellIssue item, int totalInCell)
        {
            _pinnedMenuButtons.Clear();
            ProofingIssue issue = item.Issue;
            bool canAddDict = !issue.IsGrammarOrStyle &&
                              !issue.IsContextualWarning &&
                              issue.IssueType == ProofingIssueType.SpellingError;

            foreach (string barName in s_targetCellMenuNames)
            {
                Office.CommandBar? cellBar = null;
                try { cellBar = _excelApp.CommandBars[barName]; } catch { }
                if (cellBar == null)
                    continue;

                try
                {
                    Office.CommandBarControls controls = cellBar.Controls;
                    int insertPos = 1;

                    // Header info disabled item or top suggestions directly at the top of the right-click menu
                    int maxSug = Math.Min(4, issue.Suggestions.Count);
                    if (maxSug > 0)
                    {
                        for (int i = 0; i < maxSug; i++)
                        {
                            SuggestionCandidate cand = issue.Suggestions[i];
                            string replacement = cand.Replacement;
                            var btnFix = (Office.CommandBarButton)controls.Add(
                                Office.MsoControlType.msoControlButton,
                                Temporary: true,
                                Before: insertPos++);
                            btnFix.Tag = ContextMenuTag;
                            btnFix.Style = Office.MsoButtonStyle.msoButtonIconAndCaption;
                            btnFix.FaceId = 1087; // Checkmark icon
                            btnFix.Caption = $"✓ \"{issue.OriginalText}\" ➔ {cand.DisplayLabel}";
                            btnFix.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                            {
                                _onApplySingleFix(item, replacement);
                            };
                            _pinnedMenuButtons.Add(btnFix);
                        }
                    }
                    else
                    {
                        var btnNotice = (Office.CommandBarButton)controls.Add(
                            Office.MsoControlType.msoControlButton,
                            Temporary: true,
                            Before: insertPos++);
                        btnNotice.Tag = ContextMenuTag;
                        btnNotice.Style = Office.MsoButtonStyle.msoButtonCaption;
                        btnNotice.Enabled = false;
                        btnNotice.Caption = $"⚠ {issue.OriginalText} — {issue.Message}";
                        _pinnedMenuButtons.Add(btnNotice);
                    }

                    var btnIgnore = (Office.CommandBarButton)controls.Add(
                        Office.MsoControlType.msoControlButton,
                        Temporary: true,
                        Before: insertPos++);
                    btnIgnore.Tag = ContextMenuTag;
                    btnIgnore.Style = Office.MsoButtonStyle.msoButtonCaption;
                    btnIgnore.BeginGroup = true;
                    btnIgnore.Caption = totalInCell > 1
                        ? UiLocalizer.Pick(
                            $"Ignore \"{issue.OriginalText}\" ({totalInCell} issues in cell)",
                            $"\"{issue.OriginalText}\" ကို ကျော်မည် (ဤအကွက်၌ {totalInCell} ခု)")
                        : UiLocalizer.Pick(
                            $"Ignore \"{issue.OriginalText}\" Once",
                            $"\"{issue.OriginalText}\" ကို ယခုစာအုပ်၌ ကျော်မည်");
                    btnIgnore.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                    {
                        _onIgnoreIssue(item);
                    };
                    _pinnedMenuButtons.Add(btnIgnore);

                    if (canAddDict)
                    {
                        var btnAddDict = (Office.CommandBarButton)controls.Add(
                            Office.MsoControlType.msoControlButton,
                            Temporary: true,
                            Before: insertPos++);
                        btnAddDict.Tag = ContextMenuTag;
                        btnAddDict.Style = Office.MsoButtonStyle.msoButtonCaption;
                        btnAddDict.Caption = UiLocalizer.Pick(
                            $"+ Add \"{issue.OriginalText}\" to Myanmar Dictionary",
                            $"+ \"{issue.OriginalText}\" ကို ကိုယ်ပိုင်အဘိဓာန်သို့ မှတ်မည်");
                        btnAddDict.Click += (Office.CommandBarButton ctrl, ref bool cancelDefault) =>
                        {
                            _onAddToDictionary(item);
                        };
                        _pinnedMenuButtons.Add(btnAddDict);
                    }

                    // Separate our items cleanly from Excel's native Cut / Copy / Paste / Format Cells items below
                    if (controls.Count >= insertPos)
                    {
                        try { controls[insertPos].BeginGroup = true; } catch { }
                    }
                }
                catch { }
            }
        }

        public void ClearCustomContextMenuItems()
        {
            _pinnedMenuButtons.Clear();
            foreach (string barName in s_targetCellMenuNames)
            {
                try
                {
                    Office.CommandBar? bar = _excelApp.CommandBars[barName];
                    if (bar == null)
                        continue;

                    Office.CommandBarControls controls = bar.Controls;
                    for (int i = controls.Count; i >= 1; i--)
                    {
                        try
                        {
                            Office.CommandBarControl c = controls[i];
                            if (string.Equals(c.Tag, ContextMenuTag, StringComparison.Ordinal))
                            {
                                c.Delete(Temporary: false);
                            }
                        }
                        catch { }
                    }

                    if (controls.Count >= 1)
                    {
                        try { controls[1].BeginGroup = false; } catch { }
                    }
                }
                catch { }
            }
        }

        public void CloseActivePopup()
        {
            var popup = _activePopup;
            _activePopup = null;
            _activePopupIdentity = string.Empty;
            if (popup != null && !popup.IsDisposed)
            {
                popup.ClosePopupSafely();
            }
        }

        private static bool TryNormalizeToSingleLogicalCell(Excel.Range? selection, out Excel.Range? anchorCell)
        {
            anchorCell = null;
            if (selection == null)
                return false;

            try
            {
                if (selection.Areas.Count != 1)
                    return false;

                long count = ExcelWorkbookScannerAndToolkit.SafeGetCountLarge(selection);
                if (count == 1)
                {
                    anchorCell = selection;
                    return true;
                }

                // Support merged cells (e.g., A1:C1 merged behaves as 1 logical cell when clicked!)
                if (count > 1 && count <= 256 && selection.MergeCells is bool isMerged && isMerged)
                {
                    Excel.Range mergeArea = selection.MergeArea;
                    if (mergeArea.Row == selection.Row &&
                        mergeArea.Column == selection.Column &&
                        mergeArea.Rows.Count == selection.Rows.Count &&
                        mergeArea.Columns.Count == selection.Columns.Count)
                    {
                        anchorCell = (Excel.Range)selection.Cells[1, 1];
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _selectionPopupTimer.Stop();
            _selectionPopupTimer.Dispose();
            CloseActivePopup();
            ClearCustomContextMenuItems();
        }
    }
}
