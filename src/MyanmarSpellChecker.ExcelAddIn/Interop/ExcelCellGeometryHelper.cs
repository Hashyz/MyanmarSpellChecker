// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Excel = Microsoft.Office.Interop.Excel;

namespace MyanmarSpellChecker.ExcelAddIn.Interop
{
    /// <summary>
    /// High-DPI, Zoom-aware, Freeze-Pane-aware Excel worksheet cell-to-screen coordinate mapper and Win32 window state helper.
    /// Converts Excel worksheet cell coordinates into exact screen pixel rectangles without mutating any cell properties.
    /// </summary>
    internal static class ExcelCellGeometryHelper
    {
        private static readonly uint s_currentProcessId = (uint)Process.GetCurrentProcess().Id;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
            public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public static uint GetWindowDpiSafe(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi >= 72 && dpi <= 480)
                        return dpi;
                }
                catch
                {
                    // Older Windows 10 / Windows 8.1 fallback below
                }

                try
                {
                    using (Graphics g = Graphics.FromHwnd(hwnd))
                    {
                        if (g.DpiX >= 72f && g.DpiX <= 480f)
                            return (uint)Math.Round(g.DpiX);
                    }
                }
                catch { }
            }

            return 96;
        }

        /// <summary>
        /// Returns true only when Excel's main worksheet window (or our own allowed Quick-Fix popup) is the active foreground window
        /// and no Win32 context menu (#32768), Ribbon dropdown, or modal dialog is covering the worksheet grid.
        /// </summary>
        public static bool IsWorksheetUnobstructedInForeground(IntPtr xlMainHwnd, IntPtr allowedPopupHwnd, out bool isExcelOrPopupForeground)
        {
            isExcelOrPopupForeground = false;
            if (xlMainHwnd == IntPtr.Zero || !IsWindowVisible(xlMainHwnd) || IsIconic(xlMainHwnd))
                return false;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return false;

            if (allowedPopupHwnd != IntPtr.Zero && fg == allowedPopupHwnd)
            {
                isExcelOrPopupForeground = true;
            }
            else
            {
                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid != s_currentProcessId)
                    return false;

                isExcelOrPopupForeground = true;

                // If foreground window inside Excel is a modal dialog (e.g. bosa_sdm_XL9, NUIDialog, #32770) or another SDI workbook window, hide grid overlay
                if (fg != xlMainHwnd)
                {
                    string fgClass = GetWindowClassNameSafe(fg);
                    if (!string.Equals(fgClass, "EXCEL7", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(fgClass, "XLDESK", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            // Hide grid overlay whenever a native Win32 popup context menu (#32768) or Office NetUI dropdown popup is visible
            IntPtr menuHwnd = FindWindowExW(IntPtr.Zero, IntPtr.Zero, "#32768", null);
            if (menuHwnd != IntPtr.Zero && IsWindowVisible(menuHwnd))
                return false;

            IntPtr netUiPopup = IntPtr.Zero;
            for (int guard = 0; guard < 12; guard++)
            {
                netUiPopup = FindWindowExW(IntPtr.Zero, netUiPopup, "NetUIHWND", null);
                if (netUiPopup == IntPtr.Zero)
                    break;

                if (IsWindowVisible(netUiPopup))
                {
                    GetWindowThreadProcessId(netUiPopup, out uint popupPid);
                    if (popupPid == s_currentProcessId && GetWindowRect(netUiPopup, out RECT pr) && pr.Width > 20 && pr.Height > 20)
                    {
                        // Top-level visible NetUIHWND (such as Ribbon gallery dropdown, AutoFilter menu, or CommandBar popup)
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool TryGetExcelGridViewport(
            Excel.Application? app,
            out IntPtr xlMainHwnd,
            out IntPtr excel7Hwnd,
            out Rectangle gridScreenBounds,
            out double zoomRatio,
            out double dpiScale)
        {
            xlMainHwnd = IntPtr.Zero;
            excel7Hwnd = IntPtr.Zero;
            gridScreenBounds = Rectangle.Empty;
            zoomRatio = 1.0;
            dpiScale = 1.0;

            if (app == null || !ExcelWorkbookScannerAndToolkit.IsExcelReadyForInterop(app))
                return false;

            try
            {
                Excel.Window? win = app.ActiveWindow;
                if (win == null || win.Hwnd == 0)
                    return false;

                xlMainHwnd = new IntPtr(win.Hwnd);
                if (!IsWindowVisible(xlMainHwnd) || IsIconic(xlMainHwnd))
                    return false;

                // Locate the visible EXCEL7 worksheet child window (automatically hidden when File Backstage is open)
                excel7Hwnd = FindVisibleChildByClass(xlMainHwnd, "EXCEL7", maxDepth: 4);
                if (excel7Hwnd == IntPtr.Zero || !GetWindowRect(excel7Hwnd, out RECT excel7Rect))
                    return false;

                if (excel7Rect.Width <= 40 || excel7Rect.Height <= 40)
                    return false;

                uint dpi = GetWindowDpiSafe(excel7Hwnd);
                dpiScale = dpi / 96.0;

                try
                {
                    double rawZoom = Convert.ToDouble(win.Zoom);
                    if (rawZoom >= 10.0 && rawZoom <= 400.0)
                        zoomRatio = rawZoom / 100.0;
                }
                catch { }

                int rightScrollbarMargin = (int)Math.Round(18.0 * dpiScale);
                int bottomTabsMargin = (int)Math.Round(26.0 * dpiScale);

                int leftBound = excel7Rect.Left;
                int topBound = excel7Rect.Top;
                int rightBound = Math.Max(leftBound + 20, excel7Rect.Right - rightScrollbarMargin);
                int bottomBound = Math.Max(topBound + 20, excel7Rect.Bottom - bottomTabsMargin);

                // Clamp Left/Top to the top-left pane's cell origin so Row Headers (1,2,3) & Column Headers (A,B,C) are excluded
                try
                {
                    Excel.Panes? panes = win.Panes;
                    if (panes != null && panes.Count >= 1)
                    {
                        Excel.Pane topPane = panes[1];
                        Excel.Range? vis = topPane.VisibleRange;
                        if (vis != null)
                        {
                            Excel.Range firstCell = (Excel.Range)vis.Cells[1, 1];
                            int refPtsX = (int)Math.Round(Convert.ToDouble(firstCell.Left));
                            int refPtsY = (int)Math.Round(Convert.ToDouble(firstCell.Top));
                            int paneOriginX = topPane.PointsToScreenPixelsX(refPtsX);
                            int paneOriginY = topPane.PointsToScreenPixelsY(refPtsY);

                            if (paneOriginX >= excel7Rect.Left && paneOriginX < rightBound - 20)
                                leftBound = paneOriginX;
                            if (paneOriginY >= excel7Rect.Top && paneOriginY < bottomBound - 20)
                                topBound = paneOriginY;
                        }
                    }
                }
                catch { }

                gridScreenBounds = Rectangle.FromLTRB(leftBound, topBound, rightBound, bottomBound);
                return gridScreenBounds.Width > 16 && gridScreenBounds.Height > 16;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetCellScreenRectangle(
            Excel.Application? app,
            Excel.Range? cell,
            out Rectangle cellScreenRect,
            out Rectangle paneClipScreenRect,
            out double zoomRatio,
            out double dpiScale)
        {
            cellScreenRect = Rectangle.Empty;
            paneClipScreenRect = Rectangle.Empty;
            zoomRatio = 1.0;
            dpiScale = 1.0;

            if (app == null || cell == null)
                return false;

            if (!TryGetExcelGridViewport(app, out _, out _, out Rectangle gridBounds, out zoomRatio, out dpiScale))
                return false;

            try
            {
                Excel.Window? win = app.ActiveWindow;
                Excel.Panes? panes = win?.Panes;
                if (win == null || panes == null)
                    return false;

                Excel.Range visualRange = cell;
                try
                {
                    if (cell.MergeCells is bool isMerged && isMerged)
                    {
                        visualRange = cell.MergeArea;
                    }
                }
                catch { }

                int targetRow = visualRange.Row;
                int targetCol = visualRange.Column;
                int targetRowEnd = targetRow + Math.Max(1, visualRange.Rows.Count) - 1;
                int targetColEnd = targetCol + Math.Max(1, visualRange.Columns.Count) - 1;

                int paneCount = panes.Count;
                for (int p = 1; p <= paneCount; p++)
                {
                    Excel.Pane pane = panes[p];
                    Excel.Range? vis = null;
                    try { vis = pane.VisibleRange; } catch { }
                    if (vis == null)
                        continue;

                    int visRowStart = vis.Row;
                    int visColStart = vis.Column;
                    int visRowEnd = visRowStart + Math.Max(1, vis.Rows.Count) - 1;
                    int visColEnd = visColStart + Math.Max(1, vis.Columns.Count) - 1;

                    bool rowOverlaps = targetRow <= visRowEnd && targetRowEnd >= visRowStart;
                    bool colOverlaps = targetCol <= visColEnd && targetColEnd >= visColStart;
                    if (!rowOverlaps || !colOverlaps)
                        continue;

                    Excel.Range refCell = (Excel.Range)vis.Cells[1, 1];
                    int refPtsX = (int)Math.Round(Convert.ToDouble(refCell.Left));
                    int refPtsY = (int)Math.Round(Convert.ToDouble(refCell.Top));

                    int originScreenX = pane.PointsToScreenPixelsX(refPtsX);
                    int originScreenY = pane.PointsToScreenPixelsY(refPtsY);

                    double fallbackPxPerPt = (96.0 * dpiScale / 72.0) * zoomRatio;
                    double pxPerPtX = fallbackPxPerPt;
                    double pxPerPtY = fallbackPxPerPt;

                    try
                    {
                        int probeX = pane.PointsToScreenPixelsX(refPtsX + 720);
                        int probeY = pane.PointsToScreenPixelsY(refPtsY + 720);
                        if (Math.Abs(probeX - originScreenX) > 60)
                            pxPerPtX = (probeX - originScreenX) / 720.0;
                        if (Math.Abs(probeY - originScreenY) > 60)
                            pxPerPtY = (probeY - originScreenY) / 720.0;
                    }
                    catch { }

                    double cellLeftPts = Convert.ToDouble(visualRange.Left);
                    double cellTopPts = Convert.ToDouble(visualRange.Top);
                    double cellWidthPts = Convert.ToDouble(visualRange.Width);
                    double cellHeightPts = Convert.ToDouble(visualRange.Height);

                    int x1 = originScreenX + (int)Math.Round((cellLeftPts - refPtsX) * pxPerPtX);
                    int x2 = originScreenX + (int)Math.Round((cellLeftPts + cellWidthPts - refPtsX) * pxPerPtX);
                    int leftPx = Math.Min(x1, x2);
                    int widthPx = Math.Max(4, Math.Abs(x2 - x1));

                    int topPx = originScreenY + (int)Math.Round((cellTopPts - refPtsY) * pxPerPtY);
                    int heightPx = Math.Max(4, (int)Math.Round(cellHeightPts * Math.Abs(pxPerPtY)));

                    cellScreenRect = new Rectangle(leftPx, topPx, widthPx, heightPx);

                    int paneLeftClip = Math.Max(gridBounds.Left, Math.Min(originScreenX, x1));
                    int paneTopClip = Math.Max(gridBounds.Top, originScreenY);
                    paneClipScreenRect = Rectangle.FromLTRB(
                        paneLeftClip,
                        paneTopClip,
                        Math.Max(paneLeftClip + 4, gridBounds.Right),
                        Math.Max(paneTopClip + 4, gridBounds.Bottom));

                    return cellScreenRect.IntersectsWith(paneClipScreenRect);
                }
            }
            catch
            {
                // Transient Excel busy state
            }

            return false;
        }

        private static IntPtr FindVisibleChildByClass(IntPtr parentHwnd, string targetClass, int maxDepth)
        {
            if (parentHwnd == IntPtr.Zero || maxDepth <= 0)
                return IntPtr.Zero;

            IntPtr child = IntPtr.Zero;
            for (int guard = 0; guard < 64; guard++)
            {
                child = FindWindowExW(parentHwnd, child, null, null);
                if (child == IntPtr.Zero)
                    break;

                if (!IsWindowVisible(child))
                    continue;

                string cls = GetWindowClassNameSafe(child);
                if (string.Equals(cls, targetClass, StringComparison.OrdinalIgnoreCase))
                    return child;

                if (maxDepth > 1)
                {
                    IntPtr nested = FindVisibleChildByClass(child, targetClass, maxDepth - 1);
                    if (nested != IntPtr.Zero)
                        return nested;
                }
            }

            return IntPtr.Zero;
        }

        private static string GetWindowClassNameSafe(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return string.Empty;

            var sb = new StringBuilder(64);
            int len = GetClassNameW(hWnd, sb, sb.Capacity);
            return len > 0 ? sb.ToString(0, len) : string.Empty;
        }
    }
}
