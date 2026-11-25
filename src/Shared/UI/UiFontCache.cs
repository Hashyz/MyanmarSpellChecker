// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

#if HOST_EXCEL
namespace MyanmarSpellChecker.ExcelAddIn.UI
#elif HOST_POWERPOINT
namespace MyanmarSpellChecker.PowerPointAddIn.UI
#else
namespace MyanmarSpellChecker.WordAddIn.UI
#endif
{
    /// <summary>
    /// Auto-wrapping WinForms Button whose preferred height expands cleanly across 2+ lines for long Myanmar
    /// replacement + meaning-cue labels so bracketed glosses never clip at narrow Task Pane widths or high DPI.
    /// Memoizes measurements per (Text, maxW, DPI, Font) and uses WinForms' shared screen measurement DC
    /// (zero CreateGraphics() GDI+ handle churn and zero redundant Uniscribe shaping passes).
    /// </summary>
    internal sealed class WordWrapChoiceButton : Button
    {
        private string _cachedMeasureText = string.Empty;
        private int _cachedMaxW = -1;
        private int _cachedDpi = -1;
        private Font? _cachedFont;
        private Size _cachedPreferredSize;

        public WordWrapChoiceButton()
        {
            UseMnemonic = false;
            UseCompatibleTextRendering = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            _cachedMaxW = -1;
            base.OnTextChanged(e);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            _cachedMaxW = -1;
            base.OnFontChanged(e);
        }

        protected override void OnPaddingChanged(EventArgs e)
        {
            _cachedMaxW = -1;
            base.OnPaddingChanged(e);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            string text = Text;
            int maxW = MaximumSize.Width > 0
                ? MaximumSize.Width
                : (proposedSize.Width > 0 && proposedSize.Width < int.MaxValue ? proposedSize.Width : Width);

            if (maxW <= 48 || string.IsNullOrEmpty(text))
                return base.GetPreferredSize(proposedSize);

            Font font = Font;
            int dpi = UiFontCache.GetEffectiveDpi(this);
            if (maxW == _cachedMaxW &&
                dpi == _cachedDpi &&
                ReferenceEquals(font, _cachedFont) &&
                string.Equals(text, _cachedMeasureText, StringComparison.Ordinal))
            {
                return _cachedPreferredSize;
            }

            int horizontalChrome = Padding.Horizontal + UiFontCache.ScaleLogicalPxForDpi(dpi, 16);
            int availTextWidth = Math.Max(64, maxW - horizontalChrome);
            Size measured = TextRenderer.MeasureText(
                text,
                font,
                new Size(availTextWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            int minH = UiFontCache.MeasureSingleLineButtonHeightForDpi(dpi, font, 10);
            int targetHeight = Math.Max(minH, measured.Height + Padding.Vertical + UiFontCache.ScaleLogicalPxForDpi(dpi, 10));
            var result = new Size(maxW, targetHeight);

            _cachedMeasureText = text;
            _cachedMaxW = maxW;
            _cachedDpi = dpi;
            _cachedFont = font;
            _cachedPreferredSize = result;
            return result;
        }
    }

    internal sealed class FastCardStyleStub
    {
        public static readonly FastCardStyleStub Instance = new FastCardStyleStub();
        public int Add(TableLayoutStyle style) => 0;
        public void Clear() { }
    }

    internal sealed class FastCardControlCollection : Control.ControlCollection
    {
        public FastCardControlCollection(Control owner) : base(owner) { }

        public void Add(Control value, int column, int row) => base.Add(value);
    }

    /// <summary>
    /// Ultra-fast single-pass O(N) vertical card container replacing WinForms TableLayoutPanel for Task Pane cards.
    /// Skips layout completely while detached (Parent == null) during card construction and eliminates
    /// TableLayoutPanel's O(rows * cols) multi-query constraint solver.
    /// </summary>
    internal sealed class FastCardContainer : Panel
    {
        private bool _inLayout;

        public FastCardContainer()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public new FastCardControlCollection Controls => (FastCardControlCollection)base.Controls;

        public FastCardStyleStub RowStyles => FastCardStyleStub.Instance;

        public FastCardStyleStub ColumnStyles => FastCardStyleStub.Instance;

        protected override Control.ControlCollection CreateControlsInstance() => new FastCardControlCollection(this);

        public override Size GetPreferredSize(Size proposedSize)
        {
            int targetW = MaximumSize.Width > 0
                ? MaximumSize.Width
                : (Width > 20 ? Width : (proposedSize.Width > 20 && proposedSize.Width < int.MaxValue ? proposedSize.Width : 340));
            int h = ComputeStackLayout(targetW, applyBounds: false);
            return new Size(targetW, h);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (_inLayout || Parent == null || Width <= 20)
                return;

            _inLayout = true;
            try
            {
                int desiredH = ComputeStackLayout(Width, applyBounds: true);
                if (AutoSize && Height != desiredH)
                {
                    SetBounds(Left, Top, Width, desiredH, BoundsSpecified.Height);
                }
            }
            finally
            {
                _inLayout = false;
            }
        }

        private int ComputeStackLayout(int cardWidth, bool applyBounds)
        {
            int innerWidth = Math.Max(60, cardWidth - Padding.Horizontal);
            int y = Padding.Top;
            var controls = base.Controls;
            int count = controls.Count;

            for (int i = 0; i < count; i++)
            {
                Control c = controls[i];
                if (!c.Visible)
                    continue;

                Padding m = c.Margin;
                y += m.Top;
                int childAvailW = Math.Max(40, innerWidth - m.Horizontal);
                int childW = childAvailW;
                int childH;

                if (c is TextBox tb)
                {
                    childH = tb.Multiline ? tb.Height : tb.PreferredHeight;
                }
                else if (c is DateTimePicker dtp)
                {
                    childH = dtp.PreferredHeight;
                }
                else if (c.AutoSize)
                {
                    int constrainW = c.MaximumSize.Width > 0 ? Math.Min(childAvailW, c.MaximumSize.Width) : childAvailW;
                    Size pref = c.GetPreferredSize(new Size(constrainW, 0));
                    childH = pref.Height;
                    if (c.Dock == DockStyle.None && !(c is Panel) && !(c is TableLayoutPanel) && !(c is WordWrapChoiceButton))
                    {
                        childW = Math.Min(childAvailW, Math.Max(pref.Width, c.MinimumSize.Width));
                    }
                    else if (c.MaximumSize.Width > 0 && c.MaximumSize.Width < childW)
                    {
                        childW = c.MaximumSize.Width;
                    }
                }
                else
                {
                    childH = c.Height;
                    if (c.Dock == DockStyle.None)
                    {
                        childW = Math.Min(childAvailW, c.Width);
                    }
                }

                if (applyBounds)
                {
                    int childX = c.Dock == DockStyle.Right
                        ? Padding.Left + innerWidth - m.Right - childW
                        : Padding.Left + m.Left;

                    if (c.Left != childX || c.Top != y || c.Width != childW || c.Height != childH)
                    {
                        c.SetBounds(childX, y, childW, childH);
                    }

                    if (c.AutoSize && (c is FlowLayoutPanel || c is TableLayoutPanel))
                    {
                        int postH = c.GetPreferredSize(new Size(childW, 0)).Height;
                        if (postH != childH && postH > 0)
                        {
                            childH = postH;
                            c.SetBounds(childX, y, childW, childH);
                        }
                    }
                }

                y += childH + m.Bottom;
            }

            return Math.Max(Padding.Vertical + 4, y + Padding.Bottom);
        }
    }

    /// <summary>
    /// Lightweight single-row proportional column layout panel replacing nested 1-row TableLayoutPanels inside issue cards.
    /// </summary>
    internal sealed class FastPercentRowPanel : Panel
    {
        private readonly float[] _weights;
        private readonly float _weightSum;
        private bool _inLayout;

        public FastPercentRowPanel(params float[] columnWeights)
        {
            _weights = columnWeights != null && columnWeights.Length > 0 ? columnWeights : new[] { 100f };
            float sum = 0f;
            for (int i = 0; i < _weights.Length; i++)
                sum += Math.Max(1f, _weights[i]);
            _weightSum = Math.Max(1f, sum);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = proposedSize.Width > 20 && proposedSize.Width < int.MaxValue ? proposedSize.Width : (Width > 20 ? Width : 320);
            int h = ComputeRowLayout(w, applyBounds: false);
            return new Size(w, h);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (_inLayout || Parent == null || Width <= 20)
                return;
            if (Parent is FastCardContainer && Parent.Parent == null)
                return;

            _inLayout = true;
            try
            {
                int h = ComputeRowLayout(Width, applyBounds: true);
                if (AutoSize && Height != h)
                {
                    SetBounds(Left, Top, Width, h, BoundsSpecified.Height);
                }
            }
            finally
            {
                _inLayout = false;
            }
        }

        private int ComputeRowLayout(int totalWidth, bool applyBounds)
        {
            int availW = Math.Max(40, totalWidth - Padding.Horizontal);
            var controls = Controls;
            int count = Math.Min(controls.Count, _weights.Length);
            if (count == 0)
                return Padding.Vertical + 4;

            int maxRowContentH = 0;
            int usedW = 0;
            for (int i = 0; i < count; i++)
            {
                Control c = controls[i];
                int slotW = (i == count - 1)
                    ? Math.Max(16, availW - usedW)
                    : Math.Max(16, (int)Math.Round(availW * (Math.Max(1f, _weights[i]) / _weightSum)));
                usedW += slotW;

                if (!c.Visible)
                    continue;

                int childAvailW = Math.Max(12, slotW - c.Margin.Horizontal);
                int ch = c.AutoSize ? c.GetPreferredSize(new Size(childAvailW, 0)).Height : c.Height;
                int totalCh = ch + c.Margin.Vertical;
                if (totalCh > maxRowContentH)
                    maxRowContentH = totalCh;
            }

            if (applyBounds)
            {
                int xCursor = Padding.Left;
                usedW = 0;
                for (int i = 0; i < count; i++)
                {
                    Control c = controls[i];
                    int slotW = (i == count - 1)
                        ? Math.Max(16, availW - usedW)
                        : Math.Max(16, (int)Math.Round(availW * (Math.Max(1f, _weights[i]) / _weightSum)));
                    usedW += slotW;

                    if (c.Visible)
                    {
                        Padding m = c.Margin;
                        int cellContentW = Math.Max(12, slotW - m.Horizontal);
                        int cellContentH = Math.Max(12, maxRowContentH - m.Vertical);
                        int cw = cellContentW;
                        int cx = xCursor + m.Left;
                        if (c.Dock == DockStyle.Right)
                        {
                            int prefW = c.AutoSize ? c.GetPreferredSize(new Size(cellContentW, 0)).Width : c.Width;
                            cw = Math.Min(cellContentW, Math.Max(12, prefW));
                            cx = xCursor + slotW - m.Right - cw;
                        }
                        int cy = Padding.Top + m.Top;
                        if (c.Left != cx || c.Top != cy || c.Width != cw || c.Height != cellContentH)
                        {
                            c.SetBounds(cx, cy, cw, cellContentH);
                        }
                    }

                    xCursor += slotW;
                }
            }

            return Math.Max(Padding.Vertical + 4, maxRowContentH + Padding.Vertical);
        }
    }

    /// <summary>
    /// Shared GDI font resolver, O(1) memoized High-DPI metric calculator &amp; cache for WinForms Task Panes and dialogs.
    /// Eliminates per-layout GetDC / GetDpiForWindow / CreateGraphics / FontFamily GDI+ kernel syscall storms.
    /// </summary>
    internal static class UiFontCache
    {
        private const int LOGPIXELSY = 90;
        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private static readonly Lazy<string> s_resolvedMyanmarFamily = new Lazy<string>(ResolveBestMyanmarFontFamily);
        private static readonly Lazy<int> s_systemScreenDpi = new Lazy<int>(QuerySystemScreenDpiOnce);
        private static readonly ConcurrentDictionary<string, Font> s_sharedFonts = new ConcurrentDictionary<string, Font>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, int> s_singleLineHeightCache = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<IntPtr, (int Dpi, int Tick)> s_hwndDpiCache = new ConcurrentDictionary<IntPtr, (int Dpi, int Tick)>();

        public static string MyanmarFontFamily => s_resolvedMyanmarFamily.Value;

        public static int GetWindowDpi(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return s_systemScreenDpi.Value;

            int nowTick = Environment.TickCount;
            if (s_hwndDpiCache.TryGetValue(hwnd, out var cached) && unchecked(nowTick - cached.Tick) < 2000)
                return cached.Dpi;

            int bestDpi = 0;
            try
            {
                uint winDpi = GetDpiForWindow(hwnd);
                if (winDpi >= 96)
                    bestDpi = (int)winDpi;
            }
            catch { }

            try
            {
                IntPtr root = GetAncestor(hwnd, GA_ROOT);
                if (root != IntPtr.Zero && root != hwnd)
                {
                    uint rootDpi = GetDpiForWindow(root);
                    if (rootDpi > bestDpi)
                        bestDpi = (int)rootDpi;
                }
            }
            catch { }

            int resolved = bestDpi >= 96 ? bestDpi : s_systemScreenDpi.Value;
            if (s_hwndDpiCache.Count > 128)
                s_hwndDpiCache.Clear();
            s_hwndDpiCache[hwnd] = (resolved, nowTick);
            return resolved;
        }

        public static int GetWindowWidthPx(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    if (GetWindowRect(hwnd, out RECT rc))
                    {
                        int w = rc.Right - rc.Left;
                        if (w >= 320)
                            return w;
                    }
                }
                catch { }
            }

            try
            {
                return Screen.PrimaryScreen?.WorkingArea.Width ?? 1920;
            }
            catch
            {
                return 1920;
            }
        }

        public static int GetEffectiveDpi(Control? control)
        {
            int sysDpi = s_systemScreenDpi.Value;
            if (control != null)
            {
                try
                {
                    int devDpi = control.DeviceDpi;
                    if (devDpi > 96)
                        return Math.Max(devDpi, sysDpi);
                }
                catch { }

                if (control.IsHandleCreated)
                {
                    // Walk up to top-most managed parent inside Task Pane so all child buttons share 1 cached HWND lookup!
                    Control topCtrl = control;
                    while (topCtrl.Parent != null && topCtrl.Parent.IsHandleCreated)
                        topCtrl = topCtrl.Parent;

                    int hwndDpi = GetWindowDpi(topCtrl.Handle);
                    if (hwndDpi >= 96)
                        return Math.Max(hwndDpi, sysDpi > 96 && hwndDpi == 96 && control.Parent == null ? sysDpi : hwndDpi);
                }
            }

            return sysDpi;
        }

        private static int QuerySystemScreenDpiOnce()
        {
            IntPtr screenDc = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc != IntPtr.Zero)
                {
                    int sysDpi = GetDeviceCaps(screenDc, LOGPIXELSY);
                    if (sysDpi >= 96)
                        return sysDpi;
                }
            }
            catch { }
            finally
            {
                if (screenDc != IntPtr.Zero)
                {
                    try { ReleaseDC(IntPtr.Zero, screenDc); } catch { }
                }
            }

            return 96;
        }

        public static int ScaleLogicalPxForDpi(int dpi, int logicalPx) =>
            (int)Math.Ceiling(logicalPx * (Math.Max(96, dpi) / 96.0));

        public static int ScaleLogicalPx(Control? control, int logicalPx) =>
            ScaleLogicalPxForDpi(GetEffectiveDpi(control), logicalPx);

        public static int MeasureSingleLineTextPixelHeightForDpi(int dpi, Font font)
        {
            int safeDpi = Math.Max(96, dpi);
            string cacheKey = $"{font.Name}|{font.SizeInPoints:0.##}|{(int)font.Style}|{safeDpi}";
            return s_singleLineHeightCache.GetOrAdd(cacheKey, _ =>
            {
                double ratio = 1.52;
                try
                {
                    double emHeight = Math.Max(1, font.FontFamily.GetEmHeight(font.Style));
                    double lineSpacing = font.FontFamily.GetLineSpacing(font.Style);
                    ratio = Math.Max(1.48, lineSpacing / emHeight);
                }
                catch { }

                int metricPx = (int)Math.Ceiling(font.SizeInPoints * (safeDpi / 72.0) * ratio);
                int gdiPx = 0;
                try
                {
                    gdiPx = TextRenderer.MeasureText(
                        "မြန်မာစာ Ag",
                        font,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
                }
                catch { }

                return Math.Max(metricPx, gdiPx);
            });
        }

        public static int MeasureSingleLineTextPixelHeight(Control? control, Font font) =>
            MeasureSingleLineTextPixelHeightForDpi(GetEffectiveDpi(control), font);

        public static int MeasureSingleLineLabelHeight(Control? control, Font font, int extraLogicalPad = 6)
        {
            int dpi = GetEffectiveDpi(control);
            return MeasureSingleLineTextPixelHeightForDpi(dpi, font) + ScaleLogicalPxForDpi(dpi, extraLogicalPad);
        }

        public static int MeasureSingleLineButtonHeightForDpi(int dpi, Font font, int extraLogicalPad = 12) =>
            MeasureSingleLineTextPixelHeightForDpi(dpi, font) + ScaleLogicalPxForDpi(dpi, extraLogicalPad);

        public static int MeasureSingleLineButtonHeight(Control? control, Font font, int extraLogicalPad = 12)
        {
            int dpi = GetEffectiveDpi(control);
            return MeasureSingleLineButtonHeightForDpi(dpi, font, extraLogicalPad);
        }

        public static void DisableMnemonicsRecursive(Control? root)
        {
            if (root == null)
                return;

            if (root is Label lbl)
            {
                lbl.UseMnemonic = false;
            }
            else if (root is ButtonBase btn)
            {
                btn.UseMnemonic = false;
            }

            if (root.HasChildren)
            {
                foreach (Control child in root.Controls)
                {
                    DisableMnemonicsRecursive(child);
                }
            }
        }

        public static Font GetMyanmar(float sizeInPoints, FontStyle style = FontStyle.Regular)
        {
            string key = $"MM|{sizeInPoints:0.##}|{(int)style}";
            return s_sharedFonts.GetOrAdd(key, _ => CreateOwnedMyanmar(sizeInPoints, style));
        }

        public static Font GetMonospace(float sizeInPoints, FontStyle style = FontStyle.Regular)
        {
            string key = $"MONO|{sizeInPoints:0.##}|{(int)style}";
            return s_sharedFonts.GetOrAdd(key, _ => CreateSafeFont("Consolas", sizeInPoints, style));
        }

        public static Font CreateOwnedMyanmar(float sizeInPoints, FontStyle style = FontStyle.Regular)
        {
            return CreateSafeFont(MyanmarFontFamily, sizeInPoints, style);
        }

        private static Font CreateSafeFont(string preferredFamily, float sizeInPoints, FontStyle style)
        {
            try
            {
                return new Font(preferredFamily, sizeInPoints, style);
            }
            catch
            {
                try
                {
                    return new Font("Myanmar Text", sizeInPoints, style);
                }
                catch
                {
                    return new Font(SystemFonts.MessageBoxFont.FontFamily, sizeInPoints, style);
                }
            }
        }

        private static readonly ConcurrentDictionary<int, Bitmap> s_brandLogoCache = new ConcurrentDictionary<int, Bitmap>();
        private static Icon? s_sharedBrandIcon;

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon GetSharedBrandIcon()
        {
            if (s_sharedBrandIcon != null)
                return s_sharedBrandIcon;

            using (var bmp = CreateFreshBrandLogoBitmap(32))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    s_sharedBrandIcon = (Icon)Icon.FromHandle(hIcon).Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            return s_sharedBrandIcon;
        }

        public static Bitmap GetBrandLogoBitmap(int sizePx)
        {
            int clamped = Math.Max(16, Math.Min(256, sizePx));
            return s_brandLogoCache.GetOrAdd(clamped, CreateFreshBrandLogoBitmap);
        }

        private static Bitmap CreateFreshBrandLogoBitmap(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float s = size / 512f;
                float pad = Math.Max(1f, 22f * s);
                float tileW = size - 2f * pad;
                float r = 108f * s;
                using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    float d = r * 2f;
                    gp.AddArc(pad, pad, d, d, 180, 90);
                    gp.AddArc(pad + tileW - d, pad, d, d, 270, 90);
                    gp.AddArc(pad + tileW - d, pad + tileW - d, d, d, 0, 90);
                    gp.AddArc(pad, pad + tileW - d, d, d, 90, 90);
                    gp.CloseFigure();
                    using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new PointF(0, 0), new PointF(size, size),
                        Color.FromArgb(11, 46, 107), Color.FromArgb(34, 116, 224)))
                    {
                        g.FillPath(bg, gp);
                    }
                    if (size >= 32)
                    {
                        using (var rim = new Pen(Color.FromArgb(150, 178, 218, 255), Math.Max(1f, 6f * s)))
                        {
                            g.DrawPath(rim, gp);
                        }
                    }
                }

                if (size >= 44)
                {
                    using (var goldPage = new SolidBrush(Color.FromArgb(238, 255, 218, 106)))
                        g.FillRectangle(goldPage, 132f * s, 346f * s, 248f * s, 26f * s);
                    using (var whitePage = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                        g.FillRectangle(whitePage, 144f * s, 330f * s, 224f * s, 20f * s);
                }

                float glyphPx = size < 32 ? 215f * s : 188f * s;
                using (var kaFont = new Font(MyanmarFontFamily, Math.Max(7f, glyphPx), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    float kx = size < 32 ? 242f * s : 252f * s;
                    float ky = size < 44 ? 248f * s : 222f * s;
                    g.DrawString("\u1000", kaFont, Brushes.White, new PointF(kx, ky), sf);
                }

                float cx = size < 32 ? 392f * s : 388f * s;
                float cy = size < 32 ? 392f * s : 388f * s;
                float ri = size < 32 ? 86f * s : 74f * s;
                float ro = size < 32 ? 98f * s : 84f * s;
                using (var halo = new SolidBrush(Color.FromArgb(11, 46, 107)))
                    g.FillEllipse(halo, cx - ro, cy - ro, ro * 2f, ro * 2f);

                var chkRect = new RectangleF(cx - ri, cy - ri, ri * 2f, ri * 2f);
                using (var chkBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    chkRect, Color.FromArgb(18, 150, 79), Color.FromArgb(34, 197, 107), 45f))
                {
                    g.FillEllipse(chkBrush, chkRect);
                }
                using (var ringPen = new Pen(Color.White, Math.Max(1.2f, 9f * s)))
                    g.DrawEllipse(ringPen, chkRect);

                using (var tickPen = new Pen(Color.White, Math.Max(1.6f, 18f * s))
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                })
                {
                    g.DrawLines(tickPen, new[]
                    {
                        new PointF(cx - 35f * s, cy + 2f * s),
                        new PointF(cx - 10f * s, cy + 26f * s),
                        new PointF(cx + 38f * s, cy - 25f * s)
                    });
                }
            }
            return bmp;
        }

        private static string ResolveBestMyanmarFontFamily()
        {
            string[] candidates = { "Pyidaungsu", "Myanmar Text", "Padauk", "Segoe UI" };
            try
            {
                using (var installed = new InstalledFontCollection())
                {
                    var families = installed.Families;
                    foreach (string cand in candidates)
                    {
                        for (int i = 0; i < families.Length; i++)
                        {
                            if (string.Equals(families[i].Name, cand, StringComparison.OrdinalIgnoreCase))
                            {
                                return families[i].Name;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore GDI enumeration errors
            }

            return "Pyidaungsu";
        }
    }
}
