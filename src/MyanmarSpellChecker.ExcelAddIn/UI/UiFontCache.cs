using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MyanmarSpellChecker.ExcelAddIn.UI
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
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int maxW = MaximumSize.Width > 0
                ? MaximumSize.Width
                : (proposedSize.Width > 0 && proposedSize.Width < int.MaxValue ? proposedSize.Width : Width);

            if (maxW <= 48 || string.IsNullOrEmpty(Text))
                return base.GetPreferredSize(proposedSize);

            int horizontalChrome = Padding.Horizontal + 16;
            int availTextWidth = Math.Max(64, maxW - horizontalChrome);
            Size measured = TextRenderer.MeasureText(
                Text,
                Font,
                new Size(availTextWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);

            int targetHeight = Math.Max(34, measured.Height + Padding.Vertical + 10);
            return new Size(maxW, targetHeight);
        }
    }

    /// <summary>
    /// Shared GDI font resolver &amp; cache for Excel WinForms Task Pane and dialogs.
    /// </summary>
    internal static class UiFontCache
    {
        private static readonly Lazy<string> s_resolvedMyanmarFamily = new Lazy<string>(ResolveBestMyanmarFontFamily);
        private static readonly ConcurrentDictionary<string, Font> s_sharedFonts = new ConcurrentDictionary<string, Font>(StringComparer.Ordinal);

        public static string MyanmarFontFamily => s_resolvedMyanmarFamily.Value;

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

