// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Presentation;
using MyanmarSpellChecker.PowerPointAddIn.UI;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace MyanmarSpellChecker.PowerPointAddIn.Interop
{
    internal sealed class PowerPointQuickFixController : IDisposable
    {
        private readonly PowerPoint.Application _pptApp;
        private readonly PowerPointDeckScannerAndToolkit _scanner;
        private readonly Action<PresentationSlideIssue, string> _onApplySingleFix;
        private readonly Action<string, string> _onApplyBatchSameWord;
        private readonly Action<PresentationSlideIssue> _onIgnoreOnce;
        private readonly Action<PresentationSlideIssue> _onAddToDictionary;
        private readonly Action<PresentationSlideIssue>? _onHighlightInPane;
        private readonly Action? _onPopupBoundsChanged;

        private readonly Timer _selectionDebounceTimer;
        private PowerPointQuickFixPopup? _activePopup;
        private string _lastTargetKey = string.Empty;
        private int _containerIssueCursor;
        private bool _disposed;

        public Rectangle? ActivePopupScreenBounds =>
            (_activePopup != null && !_activePopup.IsDisposed) ? _activePopup.CurrentScreenBounds : null;

        public PowerPointQuickFixController(
            PowerPoint.Application pptApp,
            PowerPointDeckScannerAndToolkit scanner,
            Action<PresentationSlideIssue, string> onApplySingleFix,
            Action<string, string> onApplyBatchSameWord,
            Action<PresentationSlideIssue> onIgnoreOnce,
            Action<PresentationSlideIssue> onAddToDictionary,
            Action<PresentationSlideIssue>? onHighlightInPane = null,
            Action? onPopupBoundsChanged = null)
        {
            _pptApp = pptApp ?? throw new ArgumentNullException(nameof(pptApp));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _onApplySingleFix = onApplySingleFix;
            _onApplyBatchSameWord = onApplyBatchSameWord;
            _onIgnoreOnce = onIgnoreOnce;
            _onAddToDictionary = onAddToDictionary;
            _onHighlightInPane = onHighlightInPane;
            _onPopupBoundsChanged = onPopupBoundsChanged;

            _selectionDebounceTimer = new Timer { Interval = 160 };
            _selectionDebounceTimer.Tick += OnSelectionDebounceTick;
        }

        public void ScheduleCheckForActiveTextSelection(int delayMs = 160)
        {
            if (_disposed || _scanner.IsSlideShowRunning)
            {
                CloseActivePopup();
                return;
            }

            _selectionDebounceTimer.Stop();
            _selectionDebounceTimer.Interval = Math.Max(40, delayMs);
            _selectionDebounceTimer.Start();
        }

        public void CloseActivePopup()
        {
            var popup = _activePopup;
            _activePopup = null;
            if (popup != null && !popup.IsDisposed)
            {
                try { popup.CloseSafe(); } catch { try { popup.Close(); } catch { } }
                try { popup.Dispose(); } catch { }
                _onPopupBoundsChanged?.Invoke();
            }
        }

        private void OnSelectionDebounceTick(object? sender, EventArgs e)
        {
            _selectionDebounceTimer.Stop();
            if (_disposed || _scanner.IsSlideShowRunning || !PowerPointDeckScannerAndToolkit.IsPowerPointReadyForInterop(_pptApp))
            {
                CloseActivePopup();
                return;
            }

            try
            {
                PowerPoint.DocumentWindow win = _pptApp.ActiveWindow;
                PowerPoint.Selection? sel = null;
                try { sel = win?.Selection; } catch { }

                if (sel == null || sel.Type != PowerPoint.PpSelectionType.ppSelectionText)
                {
                    CloseActivePopup();
                    return;
                }

                PowerPoint.Slide? curSlide = PowerPointDeckScannerAndToolkit.TryResolveActiveSlide(_pptApp);
                if (curSlide == null)
                {
                    CloseActivePopup();
                    return;
                }

                PowerPoint.ShapeRange? sr = null;
                try { sr = sel.HasChildShapeRange ? sel.ChildShapeRange : sel.ShapeRange; } catch { }
                if (sr == null || sr.Count < 1)
                {
                    CloseActivePopup();
                    return;
                }

                int shapeId = sr[1].Id;
                int selStartZero = 0;
                int selLen = 0;
                try
                {
                    PowerPoint.TextRange selRange = sel.TextRange;
                    if (selRange != null)
                    {
                        selStartZero = Math.Max(0, selRange.Start - 1);
                        selLen = Math.Max(0, selRange.Length);
                    }
                }
                catch { }

                IReadOnlyList<PresentationSlideIssue> allIssues = _scanner.GetCachedIssuesForActivePresentation();
                var matchingShapeIssues = new List<PresentationSlideIssue>();
                int caretHitIndex = -1;

                for (int i = 0; i < allIssues.Count; i++)
                {
                    var item = allIssues[i];
                    if (item.IsResolved || item.SlideId != curSlide.SlideID)
                        continue;

                    if (item.ShapeId == shapeId ||
                        (item.ContainerKind == PresentationContainerKind.TableCell && sr[1].Id == item.ShapeId))
                    {
                        int idxInList = matchingShapeIssues.Count;
                        matchingShapeIssues.Add(item);

                        int iStart = item.Issue.StartIndex;
                        int iEnd = iStart + item.Issue.Length;
                        int cEnd = selStartZero + Math.Max(1, selLen);
                        if (selStartZero >= iStart && selStartZero <= iEnd ||
                            Math.Max(selStartZero, iStart) < Math.Min(cEnd, iEnd))
                        {
                            caretHitIndex = idxInList;
                        }
                    }
                }

                // Only pop up Quick-Fix when the user actually clicked inside or selected an issue span
                if (caretHitIndex < 0 || matchingShapeIssues.Count == 0)
                {
                    CloseActivePopup();
                    return;
                }

                PresentationSlideIssue chosen = matchingShapeIssues[caretHitIndex];
                string tKey = chosen.Target.BuildTargetKey();
                if (!string.Equals(tKey, _lastTargetKey, StringComparison.Ordinal))
                {
                    _lastTargetKey = tKey;
                }
                _containerIssueCursor = caretHitIndex;

                ShowPopupForIssue(chosen, matchingShapeIssues);
            }
            catch
            {
                CloseActivePopup();
            }
        }

        private void ShowPopupForIssue(PresentationSlideIssue item, List<PresentationSlideIssue> siblingIssues)
        {
            if (!PowerPointSlideGeometryHelper.TryGetIssueScreenBounds(_pptApp, _scanner, item, out Rectangle anchorRect))
            {
                CloseActivePopup();
                return;
            }

            CloseActivePopup();
            _onHighlightInPane?.Invoke(item);

            var allActive = _scanner.GetCachedIssuesForActivePresentation();
            int dupCount = allActive.Count(x =>
                !x.IsResolved &&
                string.Equals(x.Issue.OriginalText, item.Issue.OriginalText, StringComparison.Ordinal));

            var popup = new PowerPointQuickFixPopup(
                item,
                onSelectReplacement: rep =>
                {
                    CloseActivePopup();
                    _onApplySingleFix(item, rep);
                },
                onIgnoreOnce: () =>
                {
                    CloseActivePopup();
                    _onIgnoreOnce(item);
                },
                onAddToDictionary: () =>
                {
                    CloseActivePopup();
                    _onAddToDictionary(item);
                },
                duplicateCount: dupCount,
                onBatchReplaceAllMatching: rep =>
                {
                    CloseActivePopup();
                    _onApplyBatchSameWord(item.Issue.OriginalText, rep);
                },
                cellIssueIndex: _containerIssueCursor,
                totalCellIssues: siblingIssues.Count,
                onStepCellIssue: delta =>
                {
                    if (siblingIssues.Count <= 1)
                        return;
                    _containerIssueCursor = (_containerIssueCursor + delta + siblingIssues.Count) % siblingIssues.Count;
                    ShowPopupForIssue(siblingIssues[_containerIssueCursor], siblingIssues);
                });

            popup.PopupClosedSafe += () =>
            {
                if (ReferenceEquals(_activePopup, popup))
                    _activePopup = null;
                _onPopupBoundsChanged?.Invoke();
            };

            _activePopup = popup;
            popup.ShowNearCell(anchorRect);
            _onPopupBoundsChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _selectionDebounceTimer.Stop();
            _selectionDebounceTimer.Dispose();
            CloseActivePopup();
        }
    }
}
