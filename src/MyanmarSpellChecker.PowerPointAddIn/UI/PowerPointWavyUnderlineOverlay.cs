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
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Presentation;
using MyanmarSpellChecker.PowerPointAddIn.Interop;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace MyanmarSpellChecker.PowerPointAddIn.UI
{
    /// <summary>
    /// 100% Zero-Formatting-Mutation, Click-Through Per-Pixel Alpha Win32 Layered Overlay for Microsoft PowerPoint.
    /// Draws crisp Red (Spelling), Blue (Grammar/Style), and Amber (Contextual Meaning) wavy squiggles directly under
    /// flagged Myanmar words on the active slide via <c>TextRange.BoundLeft/Top/Width/Height</c> + <c>PointsToScreenPixelsX/Y</c>.
    /// Automatically hides during Slide Show (F5), Slide Sorter, minimized state, or background focus — never touching .pptx styles.
    /// </summary>
    internal sealed class PowerPointWavyUnderlineOverlay : Form
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

        private readonly PowerPoint.Application _pptApp;
        private readonly PowerPointDeckScannerAndToolkit _scanner;
        private readonly Func<Rectangle?>? _getActivePopupBounds;
        private readonly Timer _viewportSyncTimer;

        private IntPtr _currentOwnerHwnd = IntPtr.Zero;
        private string _lastViewportSignature = string.Empty;
        private bool _isOverlayShown;
        private bool _disposedOverlay;

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

        public PowerPointWavyUnderlineOverlay(
            PowerPoint.Application pptApp,
            PowerPointDeckScannerAndToolkit scanner,
            Func<Rectangle?>? getActivePopupBounds = null)
        {
            _pptApp = pptApp ?? throw new ArgumentNullException(nameof(pptApp));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _getActivePopupBounds = getActivePopupBounds;

            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);

            _viewportSyncTimer = new Timer { Interval = 120 };
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
            if (_scanner.HasActiveVisualUnderlinesForActivePresentation())
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
            if (!_scanner.HasActiveVisualUnderlinesForActivePresentation())
            {
                _viewportSyncTimer.Stop();
                HideOverlayImmediate();
                return;
            }

            if (!_viewportSyncTimer.Enabled)
                _viewportSyncTimer.Start();

            SyncViewportAndRender(forceRedraw: true);
        }

        private void OnViewportSyncTimerTick(object? sender, EventArgs e)
        {
            if (_disposedOverlay)
            {
                _viewportSyncTimer.Stop();
                return;
            }

            if (!_scanner.HasActiveVisualUnderlinesForActivePresentation())
            {
                _viewportSyncTimer.Stop();
                HideOverlayImmediate();
                return;
            }

            SyncViewportAndRender(forceRedraw: false);
        }

        private void SyncViewportAndRender(bool forceRedraw)
        {
            if (_scanner.IsSlideShowRunning ||
                !PowerPointSlideGeometryHelper.IsPowerPointWindowForegroundAndNormal(_pptApp, out IntPtr docHwnd, out Rectangle canvasScreenRect))
            {
                HideOverlayImmediate();
                return;
            }

            string sig = PowerPointSlideGeometryHelper.BuildActiveViewportSignature(_pptApp, _scanner.VisualStateVersion);
            Rectangle? popupBounds = _getActivePopupBounds?.Invoke();
            if (popupBounds.HasValue)
                sig += $"|P:{popupBounds.Value.X},{popupBounds.Value.Y},{popupBounds.Value.Width},{popupBounds.Value.Height}";

            if (!forceRedraw && _isOverlayShown && string.Equals(sig, _lastViewportSignature, StringComparison.Ordinal))
                return;

            _lastViewportSignature = sig;

            IReadOnlyList<PresentationSlideIssue> allIssues = _scanner.GetCachedIssuesForActivePresentation();
            int activeSlideId = _scanner.GetActiveSlideIdSafe();
            if (activeSlideId <= 0 || allIssues.Count == 0)
            {
                HideOverlayImmediate();
                return;
            }

            var drawableSpans = new List<(Rectangle LocalRect, Color WaveColor)>(16);
            for (int i = 0; i < allIssues.Count; i++)
            {
                PresentationSlideIssue item = allIssues[i];
                if (item.IsResolved || item.SlideId != activeSlideId)
                    continue;

                if (!PowerPointSlideGeometryHelper.TryGetIssueScreenBounds(_pptApp, _scanner, item, out Rectangle sb))
                    continue;

                if (!canvasScreenRect.IntersectsWith(sb))
                    continue;

                if (popupBounds.HasValue && popupBounds.Value.IntersectsWith(sb))
                    continue;

                Rectangle clipped = Rectangle.Intersect(canvasScreenRect, sb);
                if (clipped.Width < 5 || clipped.Height < 4)
                    continue;

                Rectangle local = new Rectangle(
                    clipped.X - canvasScreenRect.X,
                    clipped.Y - canvasScreenRect.Y,
                    clipped.Width,
                    clipped.Height);

                Color color = ResolveSquiggleColor(item.Issue);
                drawableSpans.Add((local, color));
            }

            if (drawableSpans.Count == 0)
            {
                HideOverlayImmediate();
                return;
            }

            RenderLayeredWaveBitmap(docHwnd, canvasScreenRect, drawableSpans);
        }

        private static Color ResolveSquiggleColor(ProofingIssue iss)
        {
            if (iss.IsContextualWarning || iss.IsReviewNeeded)
                return Color.FromArgb(228, 214, 132, 0); // Amber
            if (iss.IsGrammarOrStyle)
                return Color.FromArgb(232, 24, 108, 224); // Blue
            return Color.FromArgb(235, 217, 42, 34); // Red
        }

        private void RenderLayeredWaveBitmap(
            IntPtr ownerHwnd,
            Rectangle screenRect,
            List<(Rectangle LocalRect, Color WaveColor)> spans)
        {
            if (!IsHandleCreated)
                CreateHandle();

            if (_currentOwnerHwnd != ownerHwnd)
            {
                SetOwnerHwndSafe(Handle, ownerHwnd);
                _currentOwnerHwnd = ownerHwnd;
            }

            int w = Math.Max(1, screenRect.Width);
            int h = Math.Max(1, screenRect.Height);

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    for (int i = 0; i < spans.Count; i++)
                    {
                        var (rect, color) = spans[i];
                        DrawWavyUnderline(g, rect.Left, rect.Right, rect.Bottom - 2, color);
                    }
                }

                IntPtr screenDc = GetDC(IntPtr.Zero);
                IntPtr memDc = CreateCompatibleDC(screenDc);
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr oldBitmap = IntPtr.Zero;
                try
                {
                    hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                    oldBitmap = SelectObject(memDc, hBitmap);

                    POINT dstPt = new POINT(screenRect.X, screenRect.Y);
                    SIZE size = new SIZE(w, h);
                    POINT srcPt = new POINT(0, 0);
                    BLENDFUNCTION blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AC_SRC_ALPHA
                    };

                    UpdateLayeredWindow(Handle, screenDc, ref dstPt, ref size, memDc, ref srcPt, 0, ref blend, ULW_ALPHA);
                    if (!_isOverlayShown)
                    {
                        ShowWindow(Handle, SW_SHOWNOACTIVATE);
                        _isOverlayShown = true;
                    }
                }
                finally
                {
                    if (oldBitmap != IntPtr.Zero)
                        SelectObject(memDc, oldBitmap);
                    if (hBitmap != IntPtr.Zero)
                        DeleteObject(hBitmap);
                    if (memDc != IntPtr.Zero)
                        DeleteDC(memDc);
                    if (screenDc != IntPtr.Zero)
                        ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        private static void DrawWavyUnderline(Graphics g, int xStart, int xEnd, int baselineY, Color color)
        {
            int width = xEnd - xStart;
            if (width < 4)
                return;

            const float step = 3.0f;
            const float amp = 1.6f;
            int count = Math.Max(2, (int)(width / step) + 1);
            var pts = new PointF[count];
            for (int i = 0; i < count; i++)
            {
                float x = Math.Min(xEnd, xStart + i * step);
                float y = baselineY + ((i & 1) == 0 ? -amp : amp);
                pts[i] = new PointF(x, y);
            }

            using (var pen = new Pen(color, 1.8f))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, pts);
            }
        }

        public void HideOverlayImmediate()
        {
            if (_isOverlayShown && IsHandleCreated)
            {
                try { ShowWindow(Handle, SW_HIDE); } catch { }
                _isOverlayShown = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposedOverlay)
            {
                _disposedOverlay = true;
                _viewportSyncTimer.Stop();
                if (disposing)
                {
                    _viewportSyncTimer.Dispose();
                }
                HideOverlayImmediate();
            }
            base.Dispose(disposing);
        }
    }
}
