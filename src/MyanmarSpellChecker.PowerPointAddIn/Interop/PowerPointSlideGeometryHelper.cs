// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using MyanmarSpellChecker.Core.Presentation;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace MyanmarSpellChecker.PowerPointAddIn.Interop
{
    internal static class PowerPointSlideGeometryHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        public static bool IsPowerPointWindowForegroundAndNormal(PowerPoint.Application app, out IntPtr docWindowHwnd, out Rectangle windowScreenRect)
        {
            docWindowHwnd = IntPtr.Zero;
            windowScreenRect = Rectangle.Empty;

            if (!PowerPointDeckScannerAndToolkit.IsPowerPointReadyForInterop(app))
                return false;

            try
            {
                PowerPoint.DocumentWindow win = app.ActiveWindow;
                if (win == null)
                    return false;

                if (win.WindowState == PowerPoint.PpWindowState.ppWindowMinimized)
                    return false;

                // Only draw slide squiggles in Normal View or Slide View (never Slide Sorter, Outline, or Slide Show)
                PowerPoint.PpViewType vt = win.ViewType;
                if (vt != PowerPoint.PpViewType.ppViewNormal && vt != PowerPoint.PpViewType.ppViewSlide)
                    return false;

                docWindowHwnd = new IntPtr(win.HWND);
                if (docWindowHwnd == IntPtr.Zero || IsIconic(docWindowHwnd))
                    return false;

                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero)
                    return false;

                GetWindowThreadProcessId(fg, out uint fgPid);
                GetWindowThreadProcessId(docWindowHwnd, out uint pptPid);
                if (fgPid != pptPid && fgPid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                    return false;

                if (!GetWindowRect(docWindowHwnd, out RECT r))
                    return false;

                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w < 120 || h < 120)
                    return false;

                // Inset top (Ribbon ~135px) and left/bottom chrome slightly so squiggles never bleed outside the slide canvas
                int insetTop = Math.Min(140, h / 5);
                int insetBottom = Math.Min(34, h / 12);
                windowScreenRect = new Rectangle(r.Left + 8, r.Top + insetTop, Math.Max(40, w - 16), Math.Max(40, h - insetTop - insetBottom));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetIssueScreenBounds(
            PowerPoint.Application app,
            PowerPointDeckScannerAndToolkit scanner,
            PresentationSlideIssue item,
            out Rectangle screenBounds)
        {
            screenBounds = Rectangle.Empty;
            if (item == null || item.IsResolved || !PowerPointDeckScannerAndToolkit.IsPowerPointReadyForInterop(app))
                return false;

            if (item.ContainerKind == PresentationContainerKind.SpeakerNotes ||
                item.ContainerKind == PresentationContainerKind.SlideMasterShape)
            {
                return false;
            }

            try
            {
                PowerPoint.DocumentWindow win = app.ActiveWindow;
                if (win == null || !(win.View?.Slide is PowerPoint.Slide curSlide) || curSlide.SlideID != item.SlideId)
                    return false;

                if (!scanner.TryResolveLiveTextRange(item.Target, out _, out _, out var tr) || tr == null)
                    return false;

                string live = tr.Text ?? string.Empty;
                int startZero = item.Issue.StartIndex;
                int len = Math.Max(1, item.Issue.Length);
                if (startZero < 0 || startZero + len > live.Length)
                    return false;

                PowerPoint.TextRange spanRange = tr.Characters(startZero + 1, len);
                float bLeft = spanRange.BoundLeft;
                float bTop = spanRange.BoundTop;
                float bWidth = Math.Max(4f, spanRange.BoundWidth);
                float bHeight = Math.Max(8f, spanRange.BoundHeight);

                int x1 = win.PointsToScreenPixelsX(bLeft);
                int y1 = win.PointsToScreenPixelsY(bTop);
                int x2 = win.PointsToScreenPixelsX(bLeft + bWidth);
                int y2 = win.PointsToScreenPixelsY(bTop + bHeight);

                int pxW = Math.Max(8, x2 - x1);
                int pxH = Math.Max(8, y2 - y1);
                screenBounds = new Rectangle(x1, y1, pxW, pxH);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string BuildActiveViewportSignature(PowerPoint.Application app, int visualVersion)
        {
            try
            {
                PowerPoint.DocumentWindow win = app.ActiveWindow;
                if (win == null)
                    return string.Empty;

                IntPtr hwnd = new IntPtr(win.HWND);
                GetWindowRect(hwnd, out RECT r);
                int slideId = (win.View?.Slide is PowerPoint.Slide sl) ? sl.SlideID : 0;
                int zoom = 100;
                try { if (win.View != null) zoom = win.View.Zoom; } catch { }
                int pxOriginX = 0, pxOriginY = 0;
                try
                {
                    pxOriginX = win.PointsToScreenPixelsX(0f);
                    pxOriginY = win.PointsToScreenPixelsY(0f);
                }
                catch { }

                return $"{visualVersion}|{hwnd}|{slideId}|{(int)win.ViewType}|{zoom}|{pxOriginX},{pxOriginY}|{r.Left},{r.Top},{r.Right},{r.Bottom}";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
