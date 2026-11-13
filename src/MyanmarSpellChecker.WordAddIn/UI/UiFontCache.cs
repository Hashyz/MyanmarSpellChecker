using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyanmarSpellChecker.WordAddIn.UI
{
    /// <summary>
    /// Auto-wrapping WinForms Button whose preferred height expands cleanly across 2+ lines for long Myanmar
    /// replacement + meaning-cue labels so bracketed glosses never clip at narrow Task Pane widths or high DPI.
    /// </summary>
    internal sealed class WordWrapChoiceButton : Button
    {
        public WordWrapChoiceButton()
        {
            UseMnemonic = false;
            UseCompatibleTextRendering = false;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int maxW = MaximumSize.Width > 0
                ? MaximumSize.Width
                : (proposedSize.Width > 0 && proposedSize.Width < int.MaxValue ? proposedSize.Width : Width);

            if (maxW <= 48 || string.IsNullOrEmpty(Text))
                return base.GetPreferredSize(proposedSize);

            int horizontalChrome = Padding.Horizontal + UiFontCache.ScaleLogicalPx(this, 16);
            int availTextWidth = Math.Max(64, maxW - horizontalChrome);
            Size measured;
            if (IsHandleCreated)
            {
                using (Graphics g = CreateGraphics())
                {
                    measured = TextRenderer.MeasureText(
                        g,
                        Text,
                        Font,
                        new Size(availTextWidth, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                measured = TextRenderer.MeasureText(
                    Text,
                    Font,
                    new Size(availTextWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            }

            int minH = UiFontCache.MeasureSingleLineButtonHeight(this, Font, 10);
            int targetHeight = Math.Max(minH, measured.Height + Padding.Vertical + UiFontCache.ScaleLogicalPx(this, 10));
            return new Size(maxW, targetHeight);
        }
    }

    /// <summary>
    /// Shared GDI font resolver, High-DPI metric calculator &amp; cache for WinForms popups and dialogs.
    /// Eliminates per-popup GDI HFONT handle leaks and gracefully falls back to "Myanmar Text" / "Segoe UI"
    /// if "Pyidaungsu" is not yet installed on a client workstation.
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
        private static readonly ConcurrentDictionary<string, Font> s_sharedFonts = new ConcurrentDictionary<string, Font>(StringComparer.Ordinal);

        public static string MyanmarFontFamily => s_resolvedMyanmarFamily.Value;

        public static int GetWindowDpi(IntPtr hwnd)
        {
            int bestDpi = 0;
            if (hwnd != IntPtr.Zero)
            {
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
            }

            if (bestDpi >= 96)
                return bestDpi;

            return GetSystemScreenDpi();
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
            int sysDpi = GetSystemScreenDpi();
            if (control != null)
            {
                if (control.IsHandleCreated)
                {
                    int hwndDpi = GetWindowDpi(control.Handle);
                    if (hwndDpi >= 96)
                        return Math.Max(hwndDpi, sysDpi > 96 && hwndDpi == 96 && control.Parent == null ? sysDpi : hwndDpi);
                }

                try
                {
                    if (control.DeviceDpi > 96)
                        return Math.Max(control.DeviceDpi, sysDpi);
                }
                catch { }
            }

            return sysDpi;
        }

        private static int GetSystemScreenDpi()
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

        public static int ScaleLogicalPx(Control? control, int logicalPx)
        {
            int dpi = GetEffectiveDpi(control);
            return (int)Math.Ceiling(logicalPx * (dpi / 96.0));
        }

        public static int MeasureSingleLineTextPixelHeight(Control? control, Font font)
        {
            int dpi = GetEffectiveDpi(control);
            double emHeight = Math.Max(1, font.FontFamily.GetEmHeight(font.Style));
            double lineSpacing = font.FontFamily.GetLineSpacing(font.Style);
            double ratio = Math.Max(1.48, lineSpacing / emHeight);
            int metricPx = (int)Math.Ceiling(font.SizeInPoints * (dpi / 72.0) * ratio);

            int gdiPx = 0;
            if (control != null && control.IsHandleCreated)
            {
                try
                {
                    using (Graphics g = control.CreateGraphics())
                    {
                        gdiPx = TextRenderer.MeasureText(
                            g,
                            "မြန်မာစာ Ag",
                            font,
                            new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    gdiPx = TextRenderer.MeasureText(
                        "မြန်မာစာ Ag",
                        font,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
                }
                catch { }
            }

            return Math.Max(metricPx, gdiPx);
        }

        public static int MeasureSingleLineLabelHeight(Control? control, Font font, int extraLogicalPad = 6) =>
            MeasureSingleLineTextPixelHeight(control, font) + ScaleLogicalPx(control, extraLogicalPad);

        public static int MeasureSingleLineButtonHeight(Control? control, Font font, int extraLogicalPad = 12) =>
            MeasureSingleLineTextPixelHeight(control, font) + ScaleLogicalPx(control, extraLogicalPad);

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

