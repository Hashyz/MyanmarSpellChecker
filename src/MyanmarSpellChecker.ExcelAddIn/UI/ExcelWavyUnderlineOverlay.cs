// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.ExcelAddIn.Interop;
using Excel = Microsoft.Office.Interop.Excel;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    /// <summary>
    /// 100% Non-Destructive, Click-Through Per-Pixel Alpha Win32 Layered Overlay for Microsoft Excel.
    /// Renders Word-style Red (Spelling), Blue (Grammar/Style), and Amber (Contextual Meaning) wavy underlines
    /// purely on screen above visible flagged cells — NEVER modifying cell Font.Color, Font.Underline, Undo stack,
    /// Clipboard rich-text, or saved .xlsx files.
    /// </summary>
    internal sealed class ExcelWavyUnderlineOverlay : Form
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        private const int GWLP_HWNDPARENT = -8;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int Width;
            public int Height;
            public SIZE(int w, int h) { Width = w; Height = h; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            uint crKey,
            [In] ref BLENDFUNCTION pblend,
            uint dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static void SetOwnerHwndSafe(IntPtr childHwnd, IntPtr ownerHwnd)
        {
            if (childHwnd == IntPtr.Zero)
                return;

            try
            {
                if (IntPtr.Size == 8)
                    SetWindowLongPtr64(childHwnd, GWLP_HWNDPARENT, ownerHwnd);
                else
                    SetWindowLong32(childHwnd, GWLP_HWNDPARENT, ownerHwnd.ToInt32());
            }
            catch { }
        }

        private readonly Excel.Application _excelApp;
        private readonly ExcelWorkbookScannerAndToolkit _scanner;
        private readonly Func<IntPtr>? _getActivePopupHwnd;
        private readonly Func<Rectangle?>? _getActivePopupBounds;
        private readonly Timer _viewportSyncTimer;
        private readonly Dictionary<string, Font> _measureFontCache = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        private IntPtr _currentOwnerHwnd = IntPtr.Zero;
        private string _lastViewportSignature = string.Empty;
        private string _lastScrollAnchorKey = string.Empty;
        private bool _isOverlayShown;
        private bool _disposedOverlay;

        public event Action? ViewportScrolledOrResized;
        public event Action? ExcelDeactivatedOrEditing;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public ExcelWavyUnderlineOverlay(
            Excel.Application excelApp,
            ExcelWorkbookScannerAndToolkit scanner,
            Func<IntPtr>? getActivePopupHwnd = null,
            Func<Rectangle?>? getActivePopupBounds = null)
        {
            _excelApp = excelApp ?? throw new ArgumentNullException(nameof(excelApp));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _getActivePopupHwnd = getActivePopupHwnd;
            _getActivePopupBounds = getActivePopupBounds;

            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);

            _viewportSyncTimer = new Timer { Interval = 95 };
            _viewportSyncTimer.Tick += OnViewportSyncTimerTick;

            _scanner.VisualUnderlinesInvalidated += OnVisualUnderlinesInvalidated;
            _scanner.ScanCompleted += _ => OnVisualUnderlinesInvalidated();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = new IntPtr(HTTRANSPARENT);
                return;
            }
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }

        public void NotifyPopupBoundsChanged()
        {
            if (_disposedOverlay)
                return;

            _lastViewportSignature = string.Empty;
            if (_scanner.IsVisualUnderlineEnabled && _scanner.HasActiveVisualUnderlinesForActiveWorkbook())
            {
                if (!_viewportSyncTimer.Enabled)
                    _viewportSyncTimer.Start();
                SyncViewportAndRender(forceRedraw: true);
            }
        }

        private void OnVisualUnderlinesInvalidated()
        {
            if (_disposedOverlay)
                return;

            _lastViewportSignature = string.Empty;
            if (!_scanner.IsVisualUnderlineEnabled || !_scanner.HasActiveVisualUnderlinesForActiveWorkbook())
            {
                _viewportSyncTimer.Stop();
                HideOverlayImmediate();
                return;
            }

            if (!_viewportSyncTimer.Enabled)
            {
                _viewportSyncTimer.Start();
            }

            SyncViewportAndRender(forceRedraw: true);
        }

        private void OnViewportSyncTimerTick(object? sender, EventArgs e)
        {
            if (_disposedOverlay)
            {
                _viewportSyncTimer.Stop();
                return;
            }

            if (!_scanner.IsVisualUnderlineEnabled || !_scanner.HasActiveVisualUnderlinesForActiveWorkbook())
            {
                _viewportSyncTimer.Stop();
                HideOverlayImmediate();
                return;
            }

            SyncViewportAndRender(forceRedraw: false);
        }

        private void SyncViewportAndRender(bool forceRedraw)
        {
            if (!ExcelWorkbookScannerAndToolkit.IsExcelReadyForInterop(_excelApp))
            {
                HideOverlayImmediate();
                ExcelDeactivatedOrEditing?.Invoke();
                return;
            }

            if (!ExcelCellGeometryHelper.TryGetExcelGridViewport(
                    _excelApp,
                    out IntPtr xlMainHwnd,
                    out _,
                    out Rectangle gridBounds,
                    out double zoomRatio,
                    out double dpiScale))
            {
                HideOverlayImmediate();
                ExcelDeactivatedOrEditing?.Invoke();
                return;
            }

            IntPtr popupHwnd = _getActivePopupHwnd?.Invoke() ?? IntPtr.Zero;
            if (!ExcelCellGeometryHelper.IsWorksheetUnobstructedInForeground(
                    xlMainHwnd,
                    popupHwnd,
                    out bool isExcelOrPopupForeground))
            {
                HideOverlayImmediate();
                if (!isExcelOrPopupForeground)
                {
                    ExcelDeactivatedOrEditing?.Invoke();
                }
                return;
            }

            Excel.Workbook? wb = null;
            Excel.Worksheet? ws = null;
            Excel.Window? win = null;
            try
            {
                wb = _excelApp.ActiveWorkbook;
                ws = wb?.ActiveSheet as Excel.Worksheet;
                win = _excelApp.ActiveWindow;
            }
            catch { }

            if (wb == null || ws == null || win == null)
            {
                HideOverlayImmediate();
                return;
            }

            string wbName = ExcelWorkbookScannerAndToolkit.SafeGetWorkbookName(wb);
            string sheetName = ExcelWorkbookScannerAndToolkit.SafeGetSheetName(ws);
            if (string.IsNullOrEmpty(wbName) || string.IsNullOrEmpty(sheetName))
            {
                HideOverlayImmediate();
                return;
            }

            int scrollRow = 1;
            int scrollCol = 1;
            int minVisRow = int.MaxValue, maxVisRow = 0;
            int minVisCol = int.MaxValue, maxVisCol = 0;

            try
            {
                scrollRow = win.ScrollRow;
                scrollCol = win.ScrollColumn;
                Excel.Panes panes = win.Panes;
                int pCount = panes.Count;
                for (int p = 1; p <= pCount; p++)
                {
                    Excel.Range? vis = panes[p].VisibleRange;
                    if (vis == null)
                        continue;

                    int r1 = vis.Row;
                    int c1 = vis.Column;
                    int r2 = r1 + Math.Max(1, vis.Rows.Count) - 1;
                    int c2 = c1 + Math.Max(1, vis.Columns.Count) - 1;
                    if (r1 < minVisRow) minVisRow = r1;
                    if (r2 > maxVisRow) maxVisRow = r2;
                    if (c1 < minVisCol) minVisCol = c1;
                    if (c2 > maxVisCol) maxVisCol = c2;
                }
            }
            catch
            {
                HideOverlayImmediate();
                return;
            }

            if (maxVisRow < minVisRow || maxVisCol < minVisCol)
            {
                HideOverlayImmediate();
                return;
            }

            string currentScrollAnchor = $"{wbName}|{sheetName}|{scrollRow},{scrollCol}|{(int)Math.Round(zoomRatio * 100)}|{gridBounds.X},{gridBounds.Y},{gridBounds.Width},{gridBounds.Height}";
            if (_lastScrollAnchorKey.Length > 0 && !string.Equals(_lastScrollAnchorKey, currentScrollAnchor, StringComparison.Ordinal))
            {
                ViewportScrolledOrResized?.Invoke();
            }
            _lastScrollAnchorKey = currentScrollAnchor;

            Rectangle? popupBounds = _getActivePopupBounds?.Invoke();
            string popupKey = popupBounds.HasValue
                ? $"{popupBounds.Value.X},{popupBounds.Value.Y},{popupBounds.Value.Width},{popupBounds.Value.Height}"
                : "NONE";

            string viewportSig = $"{currentScrollAnchor}|v{_scanner.VisualStateVersion}|{popupKey}";
            if (!forceRedraw && _isOverlayShown && string.Equals(_lastViewportSignature, viewportSig, StringComparison.Ordinal))
            {
                return;
            }

            EcoRuntimeBudgets budgets = EcoPerformanceGovernor.ComputeBudgets(_scanner.IsEcoActive);
            int maxVisibleCells = Math.Max(20, Math.Min(80, budgets.ExcelVisualUnderlineCap));

            var visibleCellGroups = _scanner.GetVisibleSheetUnresolvedCellGroups(
                wbName,
                sheetName,
                minVisRow,
                maxVisRow,
                minVisCol,
                maxVisCol,
                maxVisibleCells);

            if (visibleCellGroups.Count == 0)
            {
                _lastViewportSignature = viewportSig;
                HideOverlayImmediate();
                return;
            }

            EnsureOwnerWindow(xlMainHwnd);
            RenderWavyUnderlinesToLayeredBitmap(ws, gridBounds, zoomRatio, dpiScale, popupBounds, visibleCellGroups);
            _lastViewportSignature = viewportSig;
        }

        private void EnsureOwnerWindow(IntPtr xlMainHwnd)
        {
            IntPtr handle = Handle;
            if (_currentOwnerHwnd != xlMainHwnd && xlMainHwnd != IntPtr.Zero)
            {
                SetOwnerHwndSafe(handle, xlMainHwnd);
                _currentOwnerHwnd = xlMainHwnd;
            }
        }

        private void RenderWavyUnderlinesToLayeredBitmap(
            Excel.Worksheet ws,
            Rectangle gridScreenBounds,
            double zoomRatio,
            double dpiScale,
            Rectangle? popupScreenBounds,
            List<List<SpreadsheetCellIssue>> visibleCellGroups)
        {
            int bmpW = Math.Max(16, Math.Min(3840, gridScreenBounds.Width));
            int bmpH = Math.Max(16, Math.Min(2160, gridScreenBounds.Height));
            int drawnWaves = 0;

            using (var bmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    if (popupScreenBounds.HasValue && popupScreenBounds.Value.Width > 0 && popupScreenBounds.Value.Height > 0)
                    {
                        Rectangle localPopup = new Rectangle(
                            popupScreenBounds.Value.X - gridScreenBounds.X - 3,
                            popupScreenBounds.Value.Y - gridScreenBounds.Y - 3,
                            popupScreenBounds.Value.Width + 6,
                            popupScreenBounds.Value.Height + 6);
                        g.ExcludeClip(localPopup);
                    }

                    float penWidth = Math.Max(1.25f, (float)(1.35 * dpiScale));
                    using (var penRed = CreateWavePen(Color.FromArgb(236, 217, 48, 37), penWidth))
                    using (var penBlue = CreateWavePen(Color.FromArgb(236, 26, 115, 232), penWidth))
                    using (var penAmber = CreateWavePen(Color.FromArgb(238, 217, 138, 18), penWidth))
                    {
                        for (int cIdx = 0; cIdx < visibleCellGroups.Count; cIdx++)
                        {
                            List<SpreadsheetCellIssue> cellIssues = visibleCellGroups[cIdx];
                            if (cellIssues.Count == 0)
                                continue;

                            SpreadsheetCellIssue first = cellIssues[0];
                            try
                            {
                                Excel.Range cell = (Excel.Range)ws.Cells[first.Row, first.Column];
                                if (!ExcelCellGeometryHelper.TryGetCellScreenRectangle(
                                        _excelApp,
                                        cell,
                                        out Rectangle cellScreenRect,
                                        out Rectangle paneClipScreenRect,
                                        out _,
                                        out _))
                                {
                                    continue;
                                }

                                string? liveText = cell.Value2 as string;
                                if (string.IsNullOrEmpty(liveText))
                                    continue;

                                string fontFamily = UiFontCache.MyanmarFontFamily;
                                double fontSizePts = 11.0;
                                bool wrapText = false;
                                int hAlign = (int)Excel.XlHAlign.xlHAlignGeneral;
                                int vAlign = (int)Excel.XlVAlign.xlVAlignBottom;

                                try
                                {
                                    if (cell.Font.Name is string fn && !string.IsNullOrWhiteSpace(fn))
                                        fontFamily = fn;
                                    if (cell.Font.Size is double fs && fs >= 5.0 && fs <= 96.0)
                                        fontSizePts = fs;
                                    if (cell.WrapText is bool wt)
                                        wrapText = wt;
                                    if (cell.HorizontalAlignment is int ha)
                                        hAlign = ha;
                                    if (cell.VerticalAlignment is int va)
                                        vAlign = va;
                                }
                                catch { }

                                Font measureFont = GetOrCreateMeasureFont(fontFamily, (float)(fontSizePts * zoomRatio));
                                Rectangle localCellRect = new Rectangle(
                                    cellScreenRect.X - gridScreenBounds.X,
                                    cellScreenRect.Y - gridScreenBounds.Y,
                                    cellScreenRect.Width,
                                    cellScreenRect.Height);

                                Rectangle localPaneClip = new Rectangle(
                                    paneClipScreenRect.X - gridScreenBounds.X,
                                    paneClipScreenRect.Y - gridScreenBounds.Y,
                                    paneClipScreenRect.Width,
                                    paneClipScreenRect.Height);

                                Rectangle cellDrawClip = Rectangle.Intersect(
                                    new Rectangle(0, 0, bmpW, bmpH),
                                    Rectangle.Intersect(localPaneClip, localCellRect));

                                if (cellDrawClip.Width < 5 || cellDrawClip.Height < 5)
                                    continue;

                                for (int i = 0; i < cellIssues.Count; i++)
                                {
                                    SpreadsheetCellIssue item = cellIssues[i];
                                    if (item.IsResolved)
                                        continue;

                                    if (!TryComputeIssueWaveSegment(
                                            g,
                                            liveText!,
                                            item,
                                            measureFont,
                                            localCellRect,
                                            wrapText,
                                            hAlign,
                                            vAlign,
                                            zoomRatio,
                                            dpiScale,
                                            out float x1,
                                            out float x2,
                                            out float waveY))
                                    {
                                        continue;
                                    }

                                    float clippedX1 = Math.Max(cellDrawClip.Left + 1.5f, x1);
                                    float clippedX2 = Math.Min(cellDrawClip.Right - 1.5f, x2);
                                    if (clippedX2 - clippedX1 < 4.0f || waveY < cellDrawClip.Top + 2 || waveY > cellDrawClip.Bottom - 1)
                                        continue;

                                    Pen activePen = item.Issue.IsContextualWarning
                                        ? penAmber
                                        : (item.Issue.IsGrammarOrStyle ? penBlue : penRed);

                                    DrawCrispWavyLine(g, activePen, clippedX1, clippedX2, waveY, dpiScale);
                                    drawnWaves++;
                                }
                            }
                            catch
                            {
                                // Ignore transient COM busy state on individual cell
                            }
                        }
                    }
                }

                if (drawnWaves == 0)
                {
                    HideOverlayImmediate();
                    return;
                }

                ApplyBitmapToLayeredWindow(bmp, gridScreenBounds.Location);
            }
        }

        private static bool TryComputeIssueWaveSegment(
            Graphics g,
            string liveText,
            SpreadsheetCellIssue item,
            Font measureFont,
            Rectangle localCellRect,
            bool wrapText,
            int hAlign,
            int vAlign,
            double zoomRatio,
            double dpiScale,
            out float waveX1,
            out float waveX2,
            out float waveY)
        {
            waveX1 = 0;
            waveX2 = 0;
            waveY = 0;

            string expected = item.Issue.OriginalText ?? string.Empty;
            if (string.IsNullOrEmpty(expected))
                return false;

            int startIdx = item.Issue.StartIndex;
            int len = item.Issue.Length;
            if (startIdx < 0 || len <= 0 || startIdx + len > liveText.Length ||
                !string.Equals(liveText.Substring(startIdx, len), expected, StringComparison.Ordinal))
            {
                int found = liveText.IndexOf(expected, StringComparison.Ordinal);
                if (found < 0)
                    return false;
                startIdx = found;
                len = expected.Length;
            }

            int padX = Math.Max(2, (int)Math.Round(3.0 * zoomRatio * dpiScale));
            int padY = Math.Max(2, (int)Math.Round(2.2 * dpiScale));
            Size sampleHeightSize = MeasureSingleLineNoPadding(g, "မြန်မာ", measureFont);
            int lineHeight = Math.Max(12, sampleHeightSize.Height);

            // Extract line containing startIdx if cell contains explicit line breaks (\n)
            string[] explicitLines = liveText.Replace("\r\n", "\n").Split('\n');
            int lineIndex = 0;
            int lineStartOffset = 0;
            string activeLineText = liveText;

            if (explicitLines.Length > 1)
            {
                int running = 0;
                for (int l = 0; l < explicitLines.Length; l++)
                {
                    int nextEnd = running + explicitLines[l].Length;
                    if (startIdx <= nextEnd || l == explicitLines.Length - 1)
                    {
                        lineIndex = l;
                        lineStartOffset = running;
                        activeLineText = explicitLines[l];
                        break;
                    }
                    running = nextEnd + 1;
                }
            }

            int localStartInLine = Math.Max(0, Math.Min(activeLineText.Length, startIdx - lineStartOffset));
            int localLenInLine = Math.Max(1, Math.Min(activeLineText.Length - localStartInLine, len));

            // Handle soft-wrapped lines inside wide paragraph cells when WrapText is enabled
            int totalVisualLines = explicitLines.Length;
            if (wrapText && localCellRect.Width > padX * 2 + 24)
            {
                int availWidth = localCellRect.Width - padX * 2;
                int fullLineW = MeasureSingleLineNoPadding(g, activeLineText, measureFont).Width;
                if (fullLineW > availWidth && availWidth > 24)
                {
                    int prefixFullW = localStartInLine > 0
                        ? MeasureSingleLineNoPadding(g, activeLineText.Substring(0, localStartInLine), measureFont).Width
                        : 0;
                    int softRow = prefixFullW / availWidth;
                    int approxTotalSoftRows = Math.Max(1, (fullLineW + availWidth - 1) / availWidth);
                    totalVisualLines = Math.Max(totalVisualLines, approxTotalSoftRows);
                    lineIndex += softRow;

                    float remPrefix = prefixFullW % availWidth;
                    int targetW = Math.Max(
                        8,
                        MeasureSingleLineNoPadding(g, activeLineText.Substring(localStartInLine, localLenInLine), measureFont).Width);

                    waveX1 = localCellRect.Left + padX + remPrefix;
                    waveX2 = Math.Min(localCellRect.Right - 2, waveX1 + targetW);
                    waveY = ComputeLineWaveY(localCellRect, padY, lineHeight, lineIndex, totalVisualLines, vAlign);
                    return waveX2 > waveX1 + 3;
                }
            }

            string prefix = localStartInLine > 0 ? activeLineText.Substring(0, localStartInLine) : string.Empty;
            string prefixPlusSpan = activeLineText.Substring(0, localStartInLine + localLenInLine);

            int prefixW = prefix.Length > 0 ? MeasureSingleLineNoPadding(g, prefix, measureFont).Width : 0;
            int prefixPlusSpanW = MeasureSingleLineNoPadding(g, prefixPlusSpan, measureFont).Width;
            int spanW = Math.Max(7, prefixPlusSpanW - prefixW);
            int totalLineW = MeasureSingleLineNoPadding(g, activeLineText, measureFont).Width;

            float textStartX = localCellRect.Left + padX;
            if (hAlign == (int)Excel.XlHAlign.xlHAlignCenter)
            {
                textStartX = localCellRect.Left + (localCellRect.Width - totalLineW) * 0.5f;
            }
            else if (hAlign == (int)Excel.XlHAlign.xlHAlignRight)
            {
                textStartX = localCellRect.Right - padX - totalLineW;
            }

            waveX1 = textStartX + prefixW;
            waveX2 = waveX1 + spanW;
            waveY = ComputeLineWaveY(localCellRect, padY, lineHeight, lineIndex, totalVisualLines, vAlign);
            return true;
        }

        private static float ComputeLineWaveY(
            Rectangle localCellRect,
            int padY,
            int lineHeight,
            int lineIndex,
            int totalLines,
            int vAlign)
        {
            int totalBlockH = Math.Max(lineHeight, totalLines * lineHeight);
            float blockTop;
            if (vAlign == (int)Excel.XlVAlign.xlVAlignTop)
            {
                blockTop = localCellRect.Top + padY;
            }
            else if (vAlign == (int)Excel.XlVAlign.xlVAlignCenter)
            {
                blockTop = localCellRect.Top + (localCellRect.Height - totalBlockH) * 0.5f;
            }
            else
            {
                // Default Excel cell vertical alignment is xlVAlignBottom
                blockTop = localCellRect.Bottom - padY - totalBlockH;
            }

            float targetY = blockTop + (lineIndex + 1) * lineHeight - 1.0f;
            return Math.Max(localCellRect.Top + 5.0f, Math.Min(localCellRect.Bottom - 2.0f, targetY));
        }

        private static Size MeasureSingleLineNoPadding(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
                return Size.Empty;

            return TextRenderer.MeasureText(
                g,
                text,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private static Pen CreateWavePen(Color color, float width)
        {
            return new Pen(color, width)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
        }

        private static void DrawCrispWavyLine(Graphics g, Pen pen, float x1, float x2, float centerY, double dpiScale)
        {
            float halfWave = Math.Max(2.3f, (float)(2.75 * dpiScale));
            float amplitude = Math.Max(1.15f, (float)(1.35 * dpiScale));
            int steps = Math.Max(2, (int)Math.Ceiling((x2 - x1) / halfWave));

            var pts = new PointF[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float x = Math.Min(x2, x1 + i * halfWave);
                float y = centerY + ((i % 2 == 0) ? -amplitude * 0.45f : amplitude * 0.65f);
                pts[i] = new PointF(x, y);
            }

            g.DrawLines(pen, pts);
        }

        private void ApplyBitmapToLayeredWindow(Bitmap bitmap, Point screenLocation)
        {
            IntPtr handle = Handle;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                screenDc = GetDC(IntPtr.Zero);
                memDc = CreateCompatibleDC(screenDc);
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                POINT topPos = new POINT(screenLocation.X, screenLocation.Y);
                SIZE winSize = new SIZE(bitmap.Width, bitmap.Height);
                POINT srcPos = new POINT(0, 0);
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(handle, screenDc, ref topPos, ref winSize, memDc, ref srcPos, 0, ref blend, ULW_ALPHA);

                if (!_isOverlayShown)
                {
                    ShowWindow(handle, SW_SHOWNOACTIVATE);
                    SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    _isOverlayShown = true;
                }
            }
            catch
            {
                // Ignore transient Win32 display errors
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero)
                    SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero)
                    DeleteDC(memDc);
                if (screenDc != IntPtr.Zero)
                    ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public void HideOverlayImmediate()
        {
            if (!_isOverlayShown)
                return;

            _isOverlayShown = false;
            _lastViewportSignature = string.Empty;
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    ShowWindow(Handle, SW_HIDE);
                }
            }
            catch { }
        }

        private Font GetOrCreateMeasureFont(string familyName, float scaledPoints)
        {
            float roundedPts = (float)(Math.Round(Math.Max(6.0f, Math.Min(72.0f, scaledPoints)) * 2.0) / 2.0);
            string key = $"{familyName}|{roundedPts:0.0}";
            if (_measureFontCache.TryGetValue(key, out Font? cached))
                return cached;

            if (_measureFontCache.Count >= 24)
            {
                foreach (var old in _measureFontCache.Values)
                {
                    try { old.Dispose(); } catch { }
                }
                _measureFontCache.Clear();
            }

            Font created;
            try
            {
                created = new Font(familyName, roundedPts, FontStyle.Regular);
            }
            catch
            {
                created = UiFontCache.CreateOwnedMyanmar(roundedPts, FontStyle.Regular);
            }

            _measureFontCache[key] = created;
            return created;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposedOverlay)
            {
                _disposedOverlay = true;
                if (disposing)
                {
                    _viewportSyncTimer.Stop();
                    _viewportSyncTimer.Dispose();
                    foreach (var f in _measureFontCache.Values)
                    {
                        try { f.Dispose(); } catch { }
                    }
                    _measureFontCache.Clear();
                }
                HideOverlayImmediate();
            }
            base.Dispose(disposing);
        }
    }
}
