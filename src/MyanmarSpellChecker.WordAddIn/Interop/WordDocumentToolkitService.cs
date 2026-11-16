using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Typography;
using Word = Microsoft.Office.Interop.Word;

namespace MyanmarSpellChecker.WordAddIn.Interop
{
    public sealed class ToolkitActionSummary
    {
        public bool Success { get; set; }
        public int ChangedCount { get; set; }
        public string UserMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Flagship One-Click Document Toolkit &amp; Official Ministry Writing Service for Microsoft Word:
    /// 1. Font-Aware Legacy Font Converter (WinInnwa / WinResearcher / CE / Zawgyi-One -> Pyidaungsu)
    ///    across Body, Tables, and Headers/Footers while preserving English runs and Bold/Italic/Size/Color
    ///    inside a single Ctrl+Z Word.UndoRecord.
    /// 2. Context-Smart Numeral Converter (0-9 &lt;-&gt; ၀-၉) protecting URLs, emails, ISO/COVID/5G/A4 codes.
    /// 3. Official Punctuation (၊ / ။) &amp; Invisible Zero-Width Character Cleaner.
    /// 4. Official Ministry Templates (ရုံးစာ / အမိန့်ကြော်ငြာစာ / အစည်းအဝေးမှတ်တမ်း) &amp; Printable Audit Certificate.
    /// </summary>
    public sealed class WordDocumentToolkitService
    {
        private const string TargetUnicodeFontName = "Pyidaungsu";

        public ToolkitActionSummary ConvertLegacyFontsInDocument(
            Word.Application app,
            Word.Document? doc,
            bool selectionOnly = false,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("No active Word document found.", "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။")
                };
            }

            Word.UndoRecord? undo = null;
            bool startedUndo = false;
            bool prevScreenUpdating = true;
            int convertedRuns = 0;

            try
            {
                try { prevScreenUpdating = app.ScreenUpdating; app.ScreenUpdating = false; } catch { }
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord(UiLocalizer.Pick("Convert Legacy Fonts to Pyidaungsu Unicode", "မြန်မာ ယူနီကုဒ် (Pyidaungsu) ပြောင်းလဲခြင်း"));
                        startedUndo = true;
                    }
                }
                catch { }

                if (selectionOnly && app.Selection != null && app.Selection.Range != null &&
                    app.Selection.Range.Start != app.Selection.Range.End)
                {
                    convertedRuns += ConvertLegacyFontsInParagraphs(app.Selection.Range.Paragraphs, onProgress, isCancelled);
                }
                else
                {
                    foreach (Word.Range story in EnumerateDocumentStories(doc))
                    {
                        if (isCancelled != null && isCancelled())
                            break;

                        // Whole-story O(1) fast skip: when an entire 500-page story already uses a uniform Unicode/Latin font
                        // and contains zero Zawgyi sequences, skip 15,000+ per-paragraph COM calls in < 2 ms!
                        try
                        {
                            string? storyFont = story.Font.Name;
                            if (!string.IsNullOrEmpty(storyFont) &&
                                !LegacyMyanmarFontConverter.IsLegacyAsciiMyanmarFont(storyFont!) &&
                                !LegacyMyanmarFontConverter.IsZawgyiFont(storyFont!))
                            {
                                string storyTxt = story.Text ?? string.Empty;
                                if (!UnicodeNormalizer.IsZawgyiText(storyTxt))
                                    continue;
                            }
                        }
                        catch { }

                        convertedRuns += ConvertLegacyFontsInParagraphs(story.Paragraphs, onProgress, isCancelled);
                    }
                }

                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = convertedRuns,
                    UserMessage = convertedRuns > 0
                        ? UiLocalizer.Pick(
                            $"✓ Converted {convertedRuns} Legacy/Zawgyi text span(s) to Pyidaungsu Unicode with formatting preserved (Ctrl+Z to undo).",
                            $"✓ Legacy/Zawgyi စာသားအပိုင်း ({convertedRuns}) ခုကို Pyidaungsu ယူနီကုဒ်သို့ မူလစာလုံးအလှမပျက် ပြောင်းလဲပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။")
                        : UiLocalizer.Pick(
                            "✓ Document is already 100% Myanmar Unicode compliant (no WinInnwa / Zawgyi text found).",
                            "✓ ဤစာတမ်းသည် မြန်မာယူနီကုဒ် စံနှုန်းနှင့် ကိုက်ညီပြီးဖြစ်ပါသည် (ပြောင်းလဲရန် WinInnwa / Zawgyi မတွေ့ရှိပါ)။")
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    ChangedCount = convertedRuns,
                    UserMessage = UiLocalizer.Pick($"⚠ Font conversion error: {ex.Message}", $"⚠ ဖောင့်ပြောင်းလဲရာတွင် အခက်အခဲရှိပါသည်: {ex.Message}")
                };
            }
            finally
            {
                if (startedUndo && undo != null)
                {
                    try { undo.EndCustomRecord(); } catch { }
                }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        public ToolkitActionSummary RepairBrokenPdfTextInDocument(
            Word.Application app,
            Word.Document? doc,
            MyanmarProofingEngine? engine,
            bool selectionOnly = false,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("No active Word document found.", "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။")
                };
            }

            Word.UndoRecord? undo = null;
            bool startedUndo = false;
            bool prevScreenUpdating = true;
            int repairedSpans = 0;

            try
            {
                try { prevScreenUpdating = app.ScreenUpdating; app.ScreenUpdating = false; } catch { }
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord(UiLocalizer.Pick(
                            "Repair Garbled PDF Burmese Text",
                            "PDF စာသားပျက် ပြန်လည်တည်ဆောက်ခြင်း"));
                        startedUndo = true;
                    }
                }
                catch { }

                if (selectionOnly && app.Selection != null && app.Selection.Range != null &&
                    app.Selection.Range.Start != app.Selection.Range.End)
                {
                    Word.Range selRange = app.Selection.Range;
                    int selParaCount = 0;
                    try { selParaCount = selRange.Paragraphs?.Count ?? 0; } catch { }

                    if (selParaCount <= 1)
                    {
                        string selRawWithMarkers = selRange.Text ?? string.Empty;
                        string selRaw = TrimTrailingMarkersString(selRawWithMarkers);
                        if (!string.IsNullOrWhiteSpace(selRaw) && UnicodeNormalizer.IsBrokenPdfFontCMapText(selRaw))
                        {
                            bool applied = TryTransformParagraphSegmentsSafely(
                                selRange,
                                selRawWithMarkers,
                                seg =>
                                {
                                    bool chg;
                                    string reb = engine != null
                                        ? engine.ReconstructBrokenPdfConfirmedSpan(seg, out chg)
                                        : BrokenPdfCMapReconstructor.ReconstructConfirmedSpan(seg, null, out chg);
                                    return (chg, reb);
                                },
                                TargetUnicodeFontName);
                            if (applied)
                                repairedSpans++;
                        }
                    }
                    else
                    {
                        repairedSpans += RepairBrokenPdfInParagraphs(selRange.Paragraphs, engine, onProgress, isCancelled);
                    }
                }
                else
                {
                    foreach (Word.Range story in EnumerateDocumentStories(doc))
                    {
                        if (isCancelled != null && isCancelled())
                            break;

                        try
                        {
                            string storyTxt = story.Text ?? string.Empty;
                            if (!UnicodeNormalizer.IsBrokenPdfFontCMapText(storyTxt))
                                continue;
                        }
                        catch { }

                        repairedSpans += RepairBrokenPdfInParagraphs(story.Paragraphs, engine, onProgress, isCancelled);
                    }
                }

                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = repairedSpans,
                    UserMessage = repairedSpans > 0
                        ? UiLocalizer.Pick(
                            $"✓ Reconstructed {repairedSpans} garbled PDF Burmese paragraph(s) into clean Pyidaungsu Unicode (Ctrl+Z to undo).",
                            $"✓ PDF စာသားပျက် အပိုဒ် ({repairedSpans}) ခုကို Pyidaungsu ယူနီကုဒ်အမှန်သို့ ပြန်လည်တည်ဆောက်ပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။")
                        : UiLocalizer.Pick(
                            "✓ No garbled PDF CMap Burmese text detected in the target scope.",
                            "✓ စစ်ဆေးသည့်အပိုင်းအတွင်း PDF ဖောင့်ပျက် မြန်မာစာသား မတွေ့ရှိပါ။")
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    ChangedCount = repairedSpans,
                    UserMessage = UiLocalizer.Pick(
                        $"⚠ Broken-PDF repair error: {ex.Message}",
                        $"⚠ PDF စာသားပျက် ပြင်ဆင်ရာတွင် အခက်အခဲရှိပါသည်: {ex.Message}")
                };
            }
            finally
            {
                if (startedUndo && undo != null)
                {
                    try { undo.EndCustomRecord(); } catch { }
                }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        private static int RepairBrokenPdfInParagraphs(
            Word.Paragraphs? paragraphs,
            MyanmarProofingEngine? engine,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (paragraphs == null)
                return 0;

            int changed = 0;
            int count = 0;
            try { count = paragraphs.Count; } catch { return 0; }
            if (count <= 0)
                return 0;

            var pumpWatch = Stopwatch.StartNew();
            int done = 0;
            for (int i = count; i >= 1; i--)
            {
                if (isCancelled != null && isCancelled())
                    break;

                done++;
                if (pumpWatch.ElapsedMilliseconds >= 110)
                {
                    pumpWatch.Restart();
                    onProgress?.Invoke(
                        (done * 95) / Math.Max(1, count),
                        UiLocalizer.Pick(
                            $"⏳ Reconstructing garbled PDF text... {done:N0}/{count:N0}",
                            $"⏳ PDF စာသားပျက် ပြန်လည်တည်ဆောက်နေသည်... {done:N0}/{count:N0}"));
                    Application.DoEvents();
                }

                Word.Range? paraRange = null;
                try { paraRange = paragraphs[i].Range; } catch { continue; }
                if (paraRange == null)
                    continue;

                string rawWithMarkers;
                try { rawWithMarkers = paraRange.Text ?? string.Empty; } catch { continue; }
                string rawText = TrimTrailingMarkersString(rawWithMarkers);
                if (string.IsNullOrWhiteSpace(rawText) || !UnicodeNormalizer.IsBrokenPdfFontCMapText(rawText))
                    continue;

                try
                {
                    bool applied = TryTransformParagraphSegmentsSafely(
                        paraRange,
                        rawWithMarkers,
                        seg =>
                        {
                            bool chg;
                            string reb = engine != null
                                ? engine.ReconstructBrokenPdfConfirmedSpan(seg, out chg)
                                : BrokenPdfCMapReconstructor.ReconstructConfirmedSpan(seg, null, out chg);
                            return (chg, reb);
                        },
                        TargetUnicodeFontName);
                    if (applied)
                        changed++;
                }
                catch
                {
                    // Skip protected/table-boundary ranges safely
                }
            }

            return changed;
        }

        public ToolkitActionSummary PasteAndRepairFromClipboard(
            Word.Application app,
            Word.Document? doc,
            MyanmarProofingEngine? engine,
            string? explicitClipboardText = null)
        {
            if (app == null || doc == null || app.Selection?.Range == null)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("No active Word cursor position found.", "စာထည့်သွင်းရန် Word Cursor နေရာ မတွေ့ရှိပါ။")
                };
            }

            string rawClip = explicitClipboardText ?? string.Empty;
            if (explicitClipboardText == null)
            {
                try
                {
                    if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                        rawClip = Clipboard.GetText(TextDataFormat.UnicodeText);
                    else if (Clipboard.ContainsText())
                        rawClip = Clipboard.GetText();
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(rawClip))
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick(
                        "⚠ Clipboard does not contain any text to paste.",
                        "⚠ Clipboard ထဲတွင် ကူးထည့်ရန် စာသား မတွေ့ရှိပါ။")
                };
            }

            int changedSpans;
            string cleaned = engine != null
                ? engine.RepairClipboardOrPastedText(rawClip, out changedSpans)
                : BrokenPdfCMapReconstructor.RepairClipboardOrPastedText(rawClip, null, out changedSpans);

            string wordReadyText = cleaned.Replace("\r\n", "\r").Replace('\n', '\r');

            Word.UndoRecord? undo = null;
            bool startedUndo = false;
            try
            {
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord(UiLocalizer.Pick(
                            "Paste Clean Pyidaungsu Unicode",
                            "သန့်စင်ပြီး Pyidaungsu ယူနီကုဒ် ကူးထည့်ခြင်း"));
                        startedUndo = true;
                    }
                }
                catch { }

                Word.Range insertRange = app.Selection.Range.Duplicate;
                insertRange.Text = wordReadyText;
                try
                {
                    insertRange.Font.Name = TargetUnicodeFontName;
                    insertRange.Font.NameBi = TargetUnicodeFontName;
                }
                catch { }
                insertRange.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                insertRange.Select();

                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = Math.Max(1, changedSpans),
                    UserMessage = changedSpans > 0
                        ? UiLocalizer.Pick(
                            $"✓ Repaired {changedSpans} garbled PDF / Zawgyi line(s) from Clipboard and pasted in Pyidaungsu (Ctrl+Z to undo).",
                            $"✓ Clipboard မှ စာသားပျက်/ဇော်ဂျီ ({changedSpans}) ကြောင်းကို သန့်စင်ပြုပြင်၍ Pyidaungsu ဖောင့်ဖြင့် ကူးထည့်ပြီးပါပြီ (Ctrl+Z ပြန်ဆုတ်နိုင်သည်)။")
                        : UiLocalizer.Pick(
                            "✓ Pasted clean Myanmar Unicode text in Pyidaungsu font (Ctrl+Z to undo).",
                            "✓ သန့်စင်သော မြန်မာယူနီကုဒ်စာသားကို Pyidaungsu ဖောင့်ဖြင့် ကူးထည့်ပြီးပါပြီ (Ctrl+Z ပြန်ဆုတ်နိုင်သည်)။")
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick(
                        $"⚠ Clean paste error: {ex.Message}",
                        $"⚠ စာသား ကူးထည့်ရာတွင် အခက်အခဲရှိပါသည်: {ex.Message}")
                };
            }
            finally
            {
                if (startedUndo && undo != null)
                {
                    try { undo.EndCustomRecord(); } catch { }
                }
            }
        }

        private static int ConvertLegacyFontsInParagraphs(
            Word.Paragraphs? paragraphs,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (paragraphs == null)
                return 0;

            int changed = 0;
            int count = paragraphs.Count;
            var pumpWatch = Stopwatch.StartNew();
            for (int i = 1; i <= count; i++)
            {
                if (isCancelled != null && isCancelled())
                    break;

                if (pumpWatch.ElapsedMilliseconds >= 110)
                {
                    pumpWatch.Restart();
                    onProgress?.Invoke((i * 95) / Math.Max(1, count), UiLocalizer.Pick($"⏳ Converting fonts... {i:N0}/{count:N0}", $"⏳ ဖောင့်ပြောင်းလဲနေသည်... {i:N0}/{count:N0}"));
                    Application.DoEvents();
                }
                Word.Range? paraRange = null;
                try { paraRange = paragraphs[i].Range; } catch { continue; }
                if (paraRange == null)
                    continue;

                string rawWithMarkers;
                try { rawWithMarkers = paraRange.Text ?? string.Empty; } catch { continue; }
                string rawText = TrimTrailingMarkersString(rawWithMarkers);
                if (string.IsNullOrWhiteSpace(rawText))
                    continue;

                string paraFont = string.Empty;
                try { paraFont = paraRange.Font.Name ?? string.Empty; } catch { }

                if (!string.IsNullOrEmpty(paraFont))
                {
                    // Uniform font across the entire paragraph — check in pure C# BEFORE allocating Range.Duplicate!
                    bool isWinLegacy = LegacyMyanmarFontConverter.IsLegacyAsciiMyanmarFont(paraFont);
                    bool isZawgyi = !isWinLegacy && (LegacyMyanmarFontConverter.IsZawgyiFont(paraFont) || UnicodeNormalizer.IsZawgyiText(rawText));
                    if (!isWinLegacy && !isZawgyi)
                        continue;

                    bool forceFont = isWinLegacy || LegacyMyanmarFontConverter.IsZawgyiFont(paraFont);
                    bool applied = TryTransformParagraphSegmentsSafely(
                        paraRange,
                        rawWithMarkers,
                        seg =>
                        {
                            string conv = isWinLegacy
                                ? LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode(seg)
                                : LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu(seg, paraFont, out _);
                            return (!string.Equals(conv, seg, StringComparison.Ordinal), conv);
                        },
                        TargetUnicodeFontName,
                        forceApplyFontEvenIfUnchanged: forceFont);
                    if (applied)
                        changed++;
                }
                else
                {
                    // Mixed fonts inside the paragraph (e.g. Calibri English + WinInnwa/Zawgyi Burmese).
                    // Collect contiguous font-uniform spans via Words first (falling back to Characters only on mixed-font words)
                    // and apply conversions from END to START so earlier range offsets never shift!
                    Word.Range? bodyRange = TrimTrailingParagraphAndCellMarkers(paraRange);
                    if (bodyRange != null && bodyRange.Start < bodyRange.End)
                    {
                        changed += ConvertMixedFontParagraphSpansBackwards(bodyRange);
                    }
                }
            }

            return changed;
        }

        private static int ConvertMixedFontParagraphSpansBackwards(Word.Range bodyRange)
        {
            var spans = new List<(int Start, int End, string FontName)>();
            int curStart = -1;
            int curEnd = -1;
            string curFont = string.Empty;

            void AppendUniformSpan(int s, int e, string fn)
            {
                if (e <= s)
                    return;

                if (curStart < 0)
                {
                    curStart = s;
                    curEnd = e;
                    curFont = fn;
                }
                else if (string.Equals(fn, curFont, StringComparison.OrdinalIgnoreCase) && s == curEnd)
                {
                    curEnd = e;
                }
                else
                {
                    spans.Add((curStart, curEnd, curFont));
                    curStart = s;
                    curEnd = e;
                    curFont = fn;
                }
            }

            bool collectedViaWords = false;
            try
            {
                Word.Words? words = bodyRange.Words;
                if (words != null)
                {
                    int bodyEnd = bodyRange.End;
                    int wCount = words.Count;
                    for (int w = 1; w <= wCount; w++)
                    {
                        Word.Range? wRange = null;
                        try { wRange = words[w]; } catch { continue; }
                        if (wRange == null)
                            continue;

                        int ws = wRange.Start;
                        int we = Math.Min(wRange.End, bodyEnd);
                        if (we <= ws)
                            continue;

                        string? wFont = null;
                        try { wFont = wRange.Font.Name; } catch { wFont = string.Empty; }

                        if (!string.IsNullOrEmpty(wFont))
                        {
                            AppendUniformSpan(ws, we, wFont!);
                        }
                        else
                        {
                            // Sub-word mixed font — inspect characters only inside this word
                            Word.Characters? subChars = null;
                            try { subChars = wRange.Characters; } catch { }
                            if (subChars != null)
                            {
                                int scCount = subChars.Count;
                                for (int c = 1; c <= scCount; c++)
                                {
                                    Word.Range? chRange = null;
                                    try { chRange = subChars[c]; } catch { continue; }
                                    if (chRange == null || chRange.Start >= bodyEnd)
                                        continue;
                                    string fn = string.Empty;
                                    try { fn = chRange.Font.Name ?? string.Empty; } catch { }
                                    AppendUniformSpan(chRange.Start, Math.Min(chRange.End, bodyEnd), fn);
                                }
                            }
                        }
                    }
                    collectedViaWords = true;
                }
            }
            catch
            {
                spans.Clear();
                curStart = -1;
                curEnd = -1;
                curFont = string.Empty;
                collectedViaWords = false;
            }

            if (!collectedViaWords)
            {
                Word.Characters? chars = null;
                try { chars = bodyRange.Characters; } catch { return 0; }
                if (chars == null)
                    return 0;

                int charCount = chars.Count;
                if (charCount <= 0)
                    return 0;

                for (int c = 1; c <= charCount; c++)
                {
                    Word.Range? chRange = null;
                    try { chRange = chars[c]; } catch { continue; }
                    if (chRange == null)
                        continue;

                    string fn = string.Empty;
                    try { fn = chRange.Font.Name ?? string.Empty; } catch { }
                    AppendUniformSpan(chRange.Start, chRange.End, fn);
                }
            }

            if (curStart >= 0 && curEnd > curStart)
            {
                spans.Add((curStart, curEnd, curFont));
            }

            int changed = 0;
            for (int s = spans.Count - 1; s >= 0; s--)
            {
                var span = spans[s];
                Word.Range sub = bodyRange.Duplicate;
                sub.SetRange(span.Start, span.End);
                string subText = sub.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(subText))
                    continue;

                if (span.End - span.Start > subText.Length)
                {
                    Word.Range narrowed = bodyRange.Duplicate;
                    narrowed.SetRange(span.End - subText.Length, span.End);
                    if (string.Equals(narrowed.Text ?? string.Empty, subText, StringComparison.Ordinal))
                        sub = narrowed;
                }

                bool isWinLegacy = LegacyMyanmarFontConverter.IsLegacyAsciiMyanmarFont(span.FontName);
                bool isZawgyi = !isWinLegacy && (LegacyMyanmarFontConverter.IsZawgyiFont(span.FontName) || UnicodeNormalizer.IsZawgyiText(subText));
                if (!isWinLegacy && !isZawgyi)
                    continue;

                string converted = isWinLegacy
                    ? LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode(subText)
                    : LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu(subText, span.FontName, out _);

                if (!string.Equals(converted, subText, StringComparison.Ordinal) || isWinLegacy || LegacyMyanmarFontConverter.IsZawgyiFont(span.FontName))
                {
                    if (!string.Equals(converted, subText, StringComparison.Ordinal))
                    {
                        sub.Text = converted;
                    }
                    try { sub.Font.Name = TargetUnicodeFontName; } catch { }
                    try { sub.Font.NameBi = TargetUnicodeFontName; } catch { }
                    changed++;
                }
            }

            return changed;
        }

        public ToolkitActionSummary FormatNumeralsInDocument(
            Word.Application app,
            Word.Document? doc,
            bool toMyanmarDigits,
            bool selectionOnly = false,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("No active Word document found.", "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။")
                };
            }

            Word.UndoRecord? undo = null;
            bool startedUndo = false;
            bool prevScreenUpdating = true;
            int totalDigitsChanged = 0;

            try
            {
                try { prevScreenUpdating = app.ScreenUpdating; app.ScreenUpdating = false; } catch { }
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord(toMyanmarDigits
                            ? UiLocalizer.Pick("Convert to Myanmar Digits (၀-၉)", "မြန်မာဂဏန်း (၀-၉) ပြောင်းလဲခြင်း")
                            : UiLocalizer.Pick("Convert to ASCII Digits (0-9)", "အင်္ဂလိပ်ဂဏန်း (0-9) ပြောင်းလဲခြင်း"));
                        startedUndo = true;
                    }
                }
                catch { }

                IEnumerable<Word.Paragraphs> targets = (selectionOnly && app.Selection?.Range != null && app.Selection.Range.Start != app.Selection.Range.End)
                    ? new[] { app.Selection.Range.Paragraphs }
                    : EnumerateStoryParagraphCollections(doc);

                var pumpWatch = Stopwatch.StartNew();
                foreach (var paras in targets)
                {
                    int pCount = paras.Count;
                    for (int i = 1; i <= pCount; i++)
                    {
                        if (isCancelled != null && isCancelled())
                            break;

                        if (pumpWatch.ElapsedMilliseconds >= 110)
                        {
                            pumpWatch.Restart();
                            onProgress?.Invoke((i * 95) / Math.Max(1, pCount), UiLocalizer.Pick($"⏳ Converting digits... {i:N0}/{pCount:N0}", $"⏳ ဂဏန်းပြောင်းလဲနေသည်... {i:N0}/{pCount:N0}"));
                            Application.DoEvents();
                        }
                        Word.Range? pRange = null;
                        try { pRange = paras[i].Range; } catch { continue; }
                        if (pRange == null)
                            continue;

                        string rawWithMarkers;
                        try { rawWithMarkers = pRange.Text ?? string.Empty; } catch { continue; }
                        string raw = TrimTrailingMarkersString(rawWithMarkers);
                        if (string.IsNullOrEmpty(raw))
                            continue;

                        string formatted = MyanmarTypographyFormatter.ConvertNumeralsSmart(raw, toMyanmarDigits, out int digitsConverted);
                        if (digitsConverted > 0 && !string.Equals(formatted, raw, StringComparison.Ordinal))
                        {
                            int segDigits = 0;
                            bool applied = TryTransformParagraphSegmentsSafely(
                                pRange,
                                rawWithMarkers,
                                seg =>
                                {
                                    string fmt = MyanmarTypographyFormatter.ConvertNumeralsSmart(seg, toMyanmarDigits, out int d);
                                    segDigits += d;
                                    return (d > 0 && !string.Equals(fmt, seg, StringComparison.Ordinal), fmt);
                                });
                            if (applied)
                            {
                                totalDigitsChanged += Math.Max(1, segDigits);
                            }
                        }
                    }
                }

                string dirLabel = toMyanmarDigits
                    ? UiLocalizer.Pick("Myanmar digits (၀-၉)", "မြန်မာဂဏန်း (၀-၉)")
                    : UiLocalizer.Pick("ASCII digits (0-9)", "အင်္ဂလိပ်ဂဏန်း (0-9)");
                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = totalDigitsChanged,
                    UserMessage = totalDigitsChanged > 0
                        ? UiLocalizer.Pick(
                            $"✓ Converted {totalDigitsChanged} digit(s) to {dirLabel} (URLs, emails & ISO/COVID codes preserved).",
                            $"✓ ဂဏန်းလုံးရေ ({totalDigitsChanged}) လုံးကို {dirLabel} သို့ စနစ်တကျ ပြောင်းလဲပြီးပါပြီ (URL/ISO/COVID ကုဒ်များကို မထိခိုက်စေဘဲ ထိန်းသိမ်းထားသည်)။")
                        : UiLocalizer.Pick(
                            $"✓ No digits needed conversion (already formatted as {dirLabel}).",
                            $"✓ ပြောင်းလဲရန် ဂဏန်း မတွေ့ရှိပါ ({dirLabel} အသင့်ဖြစ်ပြီးဖြစ်သည်)။")
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick($"⚠ Digit conversion error: {ex.Message}", $"⚠ ဂဏန်းပြောင်းလဲစဉ် အမှားဖြစ်ပေါ်သည်: {ex.Message}")
                };
            }
            finally
            {
                if (startedUndo && undo != null) { try { undo.EndCustomRecord(); } catch { } }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        public ToolkitActionSummary FormatOfficialPunctuationInDocument(
            Word.Application app,
            Word.Document? doc,
            bool selectionOnly = false,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick("No active Word document found.", "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။")
                };
            }

            Word.UndoRecord? undo = null;
            bool startedUndo = false;
            bool prevScreenUpdating = true;
            int totalPunctFixes = 0;
            int totalZwRemoved = 0;

            try
            {
                try { prevScreenUpdating = app.ScreenUpdating; app.ScreenUpdating = false; } catch { }
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord(UiLocalizer.Pick("Clean Punctuation (၊ / ။) & ZWSP", "ပုဒ်ဖြတ် ၊ / ။ နှင့် ZWSP သန့်စင်ခြင်း"));
                        startedUndo = true;
                    }
                }
                catch { }

                IEnumerable<Word.Paragraphs> targets = (selectionOnly && app.Selection?.Range != null && app.Selection.Range.Start != app.Selection.Range.End)
                    ? new[] { app.Selection.Range.Paragraphs }
                    : EnumerateStoryParagraphCollections(doc);

                var pumpWatch = Stopwatch.StartNew();
                foreach (var paras in targets)
                {
                    int pCount = paras.Count;
                    for (int i = 1; i <= pCount; i++)
                    {
                        if (isCancelled != null && isCancelled())
                            break;

                        if (pumpWatch.ElapsedMilliseconds >= 110)
                        {
                            pumpWatch.Restart();
                            onProgress?.Invoke((i * 95) / Math.Max(1, pCount), UiLocalizer.Pick($"⏳ Cleaning punctuation & ZWSP... {i:N0}/{pCount:N0}", $"⏳ ပုဒ်ဖြတ်သန့်စင်နေသည်... {i:N0}/{pCount:N0}"));
                            Application.DoEvents();
                        }
                        Word.Range? pRange = null;
                        try { pRange = paras[i].Range; } catch { continue; }
                        if (pRange == null)
                            continue;

                        string rawWithMarkers;
                        try { rawWithMarkers = pRange.Text ?? string.Empty; } catch { continue; }
                        string raw = TrimTrailingMarkersString(rawWithMarkers);
                        if (string.IsNullOrEmpty(raw))
                            continue;

                        string cleaned = MyanmarTypographyFormatter.FormatPunctuationAndCleanInvisible(raw, out int pFixes, out int zwFixes);
                        if ((pFixes > 0 || zwFixes > 0) && !string.Equals(cleaned, raw, StringComparison.Ordinal))
                        {
                            int segP = 0;
                            int segZw = 0;
                            bool applied = TryTransformParagraphSegmentsSafely(
                                pRange,
                                rawWithMarkers,
                                seg =>
                                {
                                    string cln = MyanmarTypographyFormatter.FormatPunctuationAndCleanInvisible(seg, out int pf, out int zf);
                                    segP += pf;
                                    segZw += zf;
                                    return ((pf > 0 || zf > 0) && !string.Equals(cln, seg, StringComparison.Ordinal), cln);
                                });
                            if (applied)
                            {
                                totalPunctFixes += segP;
                                totalZwRemoved += segZw;
                            }
                        }
                    }
                }

                int combined = totalPunctFixes + totalZwRemoved;
                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = combined,
                    UserMessage = combined > 0
                        ? UiLocalizer.Pick(
                            $"✓ Normalized {totalPunctFixes} punctuation spacing(s) and stripped {totalZwRemoved} invisible ZWSP character(s) (Ctrl+Z to undo).",
                            $"✓ ပုဒ်ဖြတ်အကွာအဝေး ({totalPunctFixes}) နေရာ နှင့် မမြင်ရသော ZWSP ({totalZwRemoved}) လုံးကို သန့်စင်ညှိနှိုင်းပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။")
                        : UiLocalizer.Pick(
                            "✓ Punctuation spacing (၊ / ။) and invisible ZWSP characters are already clean.",
                            "✓ ပုဒ်ဖြတ် (၊ / ။) အကွာအဝေး နှင့် ZWSP များ သန့်စင်မှန်ကန်ပြီး ဖြစ်ပါသည်။")
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    UserMessage = UiLocalizer.Pick($"⚠ Punctuation cleanup error: {ex.Message}", $"⚠ ပုဒ်ဖြတ်ညှိနှိုင်းစဉ် အမှားဖြစ်ပေါ်သည်: {ex.Message}")
                };
            }
            finally
            {
                if (startedUndo && undo != null) { try { undo.EndCustomRecord(); } catch { } }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        public string ExtractFullDocumentPlainText(Word.Document? doc)
        {
            if (doc == null)
                return string.Empty;

            var sb = new StringBuilder();
            try
            {
                foreach (Word.Range story in EnumerateDocumentStories(doc))
                {
                    string t = story.Text ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        sb.AppendLine(t.Replace("\a", " ").TrimEnd('\r', '\n'));
                    }
                }
            }
            catch
            {
                try { return doc.Content?.Text ?? string.Empty; } catch { }
            }

            return sb.ToString();
        }

        public void InsertOfficialTemplateAtCursor(Word.Application app, OfficialTemplateKind kind, DateTime referenceDate)
        {
            if (app?.Selection == null)
                return;

            string templateText = OfficialLetterSuite.BuildTemplateText(kind, referenceDate);
            Word.UndoRecord? undo = null;
            bool started = false;

            try
            {
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord("ရုံးစာ ပုံစံကြမ်း ထည့်သွင်းခြင်း");
                        started = true;
                    }
                }
                catch { }

                Word.Range insertRange = app.Selection.Range.Duplicate;
                insertRange.Text = templateText;
                try
                {
                    insertRange.Font.Name = TargetUnicodeFontName;
                    insertRange.Font.NameBi = TargetUnicodeFontName;
                    insertRange.Font.Size = 12F;
                }
                catch { }
                insertRange.Collapse(Word.WdCollapseDirection.wdCollapseEnd);
                insertRange.Select();
            }
            finally
            {
                if (started && undo != null)
                {
                    try { undo.EndCustomRecord(); } catch { }
                }
            }
        }

        public bool GeneratePrintableAuditCertificateDocument(
            Word.Application app,
            Word.Document? sourceDoc,
            DocumentQualityReport report)
        {
            if (app == null || report == null)
                return false;

            try
            {
                string docTitle = "အမည်မသိ စာတမ်း (Untitled Document)";
                try
                {
                    if (sourceDoc != null && !string.IsNullOrWhiteSpace(sourceDoc.Name))
                        docTitle = sourceDoc.Name;
                }
                catch { }

                string certBody = DocumentQualityAnalyzer.BuildAuditReportDocumentText(docTitle, report, DateTime.Now);

                object missing = Type.Missing;
                Word.Document certDoc = app.Documents.Add(ref missing, ref missing, ref missing, ref missing);
                Word.Range content = certDoc.Content;
                content.Text = certBody;
                content.Font.Name = TargetUnicodeFontName;
                content.Font.NameBi = TargetUnicodeFontName;
                content.Font.Size = 11.5F;
                content.ParagraphFormat.LineSpacingRule = Word.WdLineSpacing.wdLineSpace1pt5;

                // Highlight title header paragraphs in Ministry Navy Bold
                if (certDoc.Paragraphs.Count >= 3)
                {
                    for (int p = 1; p <= Math.Min(3, certDoc.Paragraphs.Count); p++)
                    {
                        Word.Range pr = certDoc.Paragraphs[p].Range;
                        pr.Font.Bold = 1;
                        pr.Font.Size = p == 2 ? 14F : 11F;
                        pr.Font.Color = (Word.WdColor)System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.FromArgb(18, 48, 102));
                        pr.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphCenter;
                    }
                }

                certDoc.Activate();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a rich formatted Word Audit Report document (.docx) with bordered tables for:
        /// (1) 4-Part Word Health Scorecard, (2) Top Repeated Findings (×Count),
        /// (3) Confirmed Wrong — How It Should Be, (4) Suspicious / Human Review Needed, and (5) Already Fixed in Session.
        /// Uses fast bulk tab-delimited ConvertToTable with ScreenUpdating=false so 400+ rows render in &lt; 250 ms.
        /// </summary>
        public bool GenerateRichAuditReportDocument(
            Word.Application app,
            ProofingAuditReportModel model)
        {
            if (app == null || model == null)
                return false;

            bool prevScreenUpdating = true;
            try
            {
                object missing = Type.Missing;
                Word.Document doc = app.Documents.Add(ref missing, ref missing, ref missing, ref missing);
                try { prevScreenUpdating = app.ScreenUpdating; app.ScreenUpdating = false; } catch { }

                try
                {
                    doc.PageSetup.Orientation = Word.WdOrientation.wdOrientLandscape;
                    doc.PageSetup.TopMargin = 36F;
                    doc.PageSetup.BottomMargin = 36F;
                    doc.PageSetup.LeftMargin = 36F;
                    doc.PageSetup.RightMargin = 36F;
                }
                catch { }

                doc.Content.Font.Name = TargetUnicodeFontName;
                doc.Content.Font.NameBi = TargetUnicodeFontName;
                doc.Content.Font.Size = 10.5F;

                static string CleanCell(string? s)
                {
                    if (string.IsNullOrEmpty(s)) return "—";
                    return s!.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
                }

                void AppendHeading(string text, float ptSize, System.Drawing.Color color, bool centered = false)
                {
                    Word.Paragraph p = doc.Content.Paragraphs.Add(ref missing);
                    p.Range.Text = text + "\r";
                    Word.Range r = doc.Paragraphs[doc.Paragraphs.Count - 1].Range;
                    r.Font.Name = TargetUnicodeFontName;
                    r.Font.NameBi = TargetUnicodeFontName;
                    r.Font.Size = ptSize;
                    r.Font.Bold = 1;
                    r.Font.Color = (Word.WdColor)System.Drawing.ColorTranslator.ToOle(color);
                    r.ParagraphFormat.Alignment = centered
                        ? Word.WdParagraphAlignment.wdAlignParagraphCenter
                        : Word.WdParagraphAlignment.wdAlignParagraphLeft;
                    r.ParagraphFormat.SpaceBefore = 6F;
                    r.ParagraphFormat.SpaceAfter = 4F;
                }

                void AppendNoteLine(string text, bool bold = false)
                {
                    Word.Paragraph p = doc.Content.Paragraphs.Add(ref missing);
                    p.Range.Text = text + "\r";
                    Word.Range r = doc.Paragraphs[doc.Paragraphs.Count - 1].Range;
                    r.Font.Name = TargetUnicodeFontName;
                    r.Font.NameBi = TargetUnicodeFontName;
                    r.Font.Size = 10F;
                    r.Font.Bold = bold ? 1 : 0;
                    r.ParagraphFormat.Alignment = Word.WdParagraphAlignment.wdAlignParagraphLeft;
                    r.ParagraphFormat.SpaceBefore = 2F;
                    r.ParagraphFormat.SpaceAfter = 4F;
                }

                void AppendTabTable(string[] headers, IReadOnlyList<string[]> rows, System.Drawing.Color headerBg, System.Drawing.Color headerFg)
                {
                    var sb = new System.Text.StringBuilder((rows.Count + 1) * 96);
                    sb.AppendLine(string.Join("\t", headers.Select(CleanCell)));
                    for (int i = 0; i < rows.Count; i++)
                    {
                        sb.AppendLine(string.Join("\t", rows[i].Select(CleanCell)));
                    }

                    Word.Paragraph p = doc.Content.Paragraphs.Add(ref missing);
                    int insertStart = p.Range.Start;
                    p.Range.Text = sb.ToString();
                    int insertEnd = p.Range.End - 1;
                    Word.Range tblRange = doc.Range(insertStart, insertEnd);

                    object sep = Word.WdTableFieldSeparator.wdSeparateByTabs;
                    object numCols = headers.Length;
                    object numRows = rows.Count + 1;
                    object autoFit = Word.WdAutoFitBehavior.wdAutoFitWindow;
                    Word.Table tbl = tblRange.ConvertToTable(
                        ref sep, ref numRows, ref numCols, ref missing, ref missing,
                        ref missing, ref missing, ref missing, ref missing, ref missing,
                        ref missing, ref missing, ref missing, ref autoFit, ref missing, ref missing);

                    tbl.Borders.Enable = 1;
                    tbl.Borders.InsideLineStyle = Word.WdLineStyle.wdLineStyleSingle;
                    tbl.Borders.OutsideLineStyle = Word.WdLineStyle.wdLineStyleSingle;
                    tbl.Borders.InsideColor = (Word.WdColor)System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.FromArgb(204, 214, 228));
                    tbl.Borders.OutsideColor = (Word.WdColor)System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.FromArgb(164, 182, 206));

                    tbl.Range.Font.Name = TargetUnicodeFontName;
                    tbl.Range.Font.NameBi = TargetUnicodeFontName;
                    tbl.Range.Font.Size = 10F;
                    try
                    {
                        tbl.TopPadding = 4.5F;
                        tbl.BottomPadding = 4.5F;
                        tbl.LeftPadding = 6F;
                        tbl.RightPadding = 6F;
                    }
                    catch { }

                    Word.Row hdrRow = tbl.Rows[1];
                    hdrRow.HeadingFormat = -1;
                    hdrRow.Shading.BackgroundPatternColor = (Word.WdColor)System.Drawing.ColorTranslator.ToOle(headerBg);
                    hdrRow.Range.Font.Bold = 1;
                    hdrRow.Range.Font.Color = (Word.WdColor)System.Drawing.ColorTranslator.ToOle(headerFg);

                    Word.Paragraph spacer = doc.Content.Paragraphs.Add(ref missing);
                    spacer.Range.Text = "\r";
                }

                // Title Header
                AppendHeading(
                    UiLocalizer.Pick(
                        "Myanmar Proofing & Orthography Audit Report\vမြန်မာစာ စာလုံးပေါင်းသတ်ပုံနှင့် သဒ္ဒါ စစ်ဆေးချက် အစီရင်ခံစာ",
                        "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံနှင့် သဒ္ဒါ စစ်ဆေးချက် အစီရင်ခံစာ\vMyanmar Proofing & Orthography Audit Report"),
                    13.5F,
                    System.Drawing.Color.FromArgb(16, 42, 72),
                    centered: true);

                AppendNoteLine(
                    $"{UiLocalizer.Pick("Document", "စာတမ်းအမည်")}: {model.DocumentTitle}   |   {UiLocalizer.Pick("Mode", "စစ်ဆေးမှုစနစ်")}: {model.StyleModeLabel}\v{UiLocalizer.Pick("Checked At", "စစ်ဆေးချိန်")}: {model.OfficialDateHeader}");

                // 1. 4-Part Scorecard Table
                AppendHeading(
                    UiLocalizer.Pick("1. 4-Part Word Health Scorecard (စကားလုံး အရည်အသွေး အနှစ်ချုပ်)", "၁။ စကားလုံး အရည်အသွေး ၄-ပိုင်းခွဲ အနှစ်ချုပ် (4-Part Scorecard)"),
                    11.5F,
                    System.Drawing.Color.FromArgb(18, 72, 148));

                var kpiRows = new List<string[]>
                {
                    new[] { UiLocalizer.Pick("Total Words / Syllables", "စုစုပေါင်း စကားလုံး / ဝဏ္ဏ"), $"{model.TotalWords:N0} words ({model.MyanmarSyllables:N0} syllables)", UiLocalizer.Pick("Quality Grade", "အရည်အသွေးအဆင့်"), $"{model.OverallScore}% — {model.LocalizedGradeLabel}" },
                    new[] { UiLocalizer.Pick("Clean / Valid Words", "အမှားကင်းစင်မှုနှုန်း"), $"{model.CleanAccuracyPercent:0.##}% (~{model.EstimatedCleanWords:N0} words)", UiLocalizer.Pick("Unicode / Formal Style", "ယူနီကုဒ် / ရုံးသုံးဟန်"), $"Unicode {model.UnicodePurityPercent:0.#}% • Formal {model.FormalRegisterPercent:0.#}%" },
                    new[] { UiLocalizer.Pick("Confirmed Wrong (Red)", "သေချာသော အမှားစစ်စစ် (အနီ)"), $"{model.ConfirmedWrongCount:N0} ({model.ConfirmedWrongRatePercent:0.##}%)", UiLocalizer.Pick("Wrong Breakdown", "အမှားအမျိုးအစားခွဲ"), $"Spelling: {model.ConfirmedSpellingCount} | Unicode/Lookalike: {model.ConfirmedEncodingAndLookalikeCount} | Grammar: {model.ConfirmedGrammarAndStyleCount}" },
                    new[] { UiLocalizer.Pick("Review Needed (Yellow)", "စိစစ်ရန် သံသယရှိ (အဝါ)"), $"{model.SuspiciousReviewCount:N0} ({model.SuspiciousRatePercent:0.##}%)", UiLocalizer.Pick("Review Breakdown", "စိစစ်ရန် အမျိုးအစားခွဲ"), $"Names/Guesses: {model.SuspiciousNameOrOovCount} | Meaning: {model.SuspiciousContextMeaningCount} | Consistency: {model.ConsistencyWarningCount}" },
                    new[] { UiLocalizer.Pick("Already Fixed in Session", "ယခုအကြိမ် ပြင်ဆင်ပြီးစီးမှု"), $"{model.SessionFixedCount:N0}", UiLocalizer.Pick("Consistency Notes", "ရှေ့နောက်ညီညွတ်မှု"), model.ConsistencySummaries.Count > 0 ? string.Join(" ; ", model.ConsistencySummaries) : "✓ Consistent" }
                };
                AppendTabTable(
                    new[] { "Metric • တိုင်းတာချက်", "Value • ရလဒ်", "Detail • အသေးစိတ်", "Notes • မှတ်ချက်" },
                    kpiRows,
                    System.Drawing.Color.FromArgb(232, 240, 252),
                    System.Drawing.Color.FromArgb(16, 42, 72));

                const int MaxWordTableRows = 400;

                // 2. Friendly Unified Reading-Order Checklist (Wide 4-Column Landscape Layout)
                AppendHeading(
                    UiLocalizer.Pick(
                        $"2. Friendly Reading-Order Checklist ({model.AllItemsInDocumentOrder.Count}) • စာမျက်နှာ၊ စာပိုဒ်နှင့် လိုင်းအလိုက် စစ်ဆေးချက်ဇယား",
                        $"၂။ စာမျက်နှာ၊ စာပိုဒ်နှင့် လိုင်းအလိုက် ဖတ်ရှုစစ်ဆေးရန် ဇယား ({model.AllItemsInDocumentOrder.Count})"),
                    11.5F,
                    System.Drawing.Color.FromArgb(16, 42, 72));

                if (model.AllItemsInDocumentOrder.Count == 0)
                {
                    AppendNoteLine(UiLocalizer.Pick("✓ Zero unresolved issues found!", "✓ ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိပါ။"), bold: true);
                }
                else
                {
                    var allFriendlyRows = model.AllItemsInDocumentOrder
                        .Take(MaxWordTableRows)
                        .Select(r =>
                        {
                            string locCol = $"#{r.DocumentOrderIndex}  •  {r.PageOrSheetLabel}\v{r.ParagraphOrCellLabel}  •  {r.LineLabel}";
                            string statusCol = $"{r.StatusBadge}\v[{r.CategoryLabel}]";
                            string fixCol = $"\"{r.VisualOriginal}\"  ➔  \"{r.HowItShouldBeDisplay}\"";
                            string ctx = !string.IsNullOrEmpty(r.MarkedContextSnippet) ? r.MarkedContextSnippet : r.ContextSnippet;
                            string noteCol = string.IsNullOrWhiteSpace(r.Explanation)
                                ? ctx
                                : $"{ctx}\v• {r.Explanation}";
                            return new[] { locCol, statusCol, fixCol, noteCol };
                        })
                        .ToList();

                    AppendTabTable(
                        new[]
                        {
                            "Page • Paragraph & Line\vစာမျက်နှာ • စာပိုဒ်နှင့် လိုင်း",
                            "Status & Category\vအခြေအနေနှင့် အမျိုးအစား",
                            "Found  ➔  How It Should Be\vတွေ့ရှိစာသား ➔ အမှန်ရေးရန်",
                            "Sentence Context (〖Word〗) & Simple Rule\vဝါကျအတွင်း တည်နေရာနှင့် ရှင်းလင်းချက်"
                        },
                        allFriendlyRows,
                        System.Drawing.Color.FromArgb(27, 59, 100),
                        System.Drawing.Color.White);
                }

                // 3. Top Repeated Words Summary (Wide 4-Column Layout)
                if (model.TopRepeatedGroups.Count > 0)
                {
                    AppendHeading(
                        UiLocalizer.Pick($"3. Top Repeated Words ({model.TopRepeatedGroups.Count}) • ထပ်ခါတလဲလဲ အများဆုံးတွေ့ရှိချက်များ", $"၃။ ထပ်ခါတလဲလဲ အများဆုံးတွေ့ရှိရသော စာလုံးများ ({model.TopRepeatedGroups.Count})"),
                        11.5F,
                        System.Drawing.Color.FromArgb(18, 72, 148));

                    var repRows = model.TopRepeatedGroups
                        .Select((g, i) => new[]
                        {
                            $"#{i + 1}  (×{g.Count})",
                            g.StatusBadge,
                            $"\"{g.VisualOriginal}\"  ➔  \"{g.RecommendedFix}\"",
                            g.SampleLocations
                        })
                        .ToList();
                    AppendTabTable(
                        new[] { "# & Count", "Status • အခြေအနေ", "Found  ➔  How It Should Be", "Sample Locations (Page • Para, Line)" },
                        repRows,
                        System.Drawing.Color.FromArgb(236, 242, 250),
                        System.Drawing.Color.FromArgb(16, 42, 72));
                }

                // 4. Already Fixed in Session (Wide 4-Column Layout)
                if (model.SessionFixedRecords.Count > 0)
                {
                    AppendHeading(
                        UiLocalizer.Pick($"4. Already Fixed in This Session ({model.SessionFixedCount}) • ယခုပြင်ဆင်ပြီးစီးခဲ့သော စာလုံးများ", $"၄။ ယခုအကြိမ် ပြင်ဆင်ပြီးစီးခဲ့သော စာလုံးများ ({model.SessionFixedCount})"),
                        11.5F,
                        System.Drawing.Color.FromArgb(16, 108, 56));

                    var fixRows = model.SessionFixedRecords
                        .Take(MaxWordTableRows)
                        .Select((f, idx) => new[]
                        {
                            (idx + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            f.LocationLabel,
                            $"\"{f.VisualOriginalText}\"  ➔  \"✓ {f.AppliedReplacement}\"",
                            f.AppliedAtLocal.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                        })
                        .ToList();
                    AppendTabTable(
                        new[] { "#", "Location (Page • Para, Line)", "Original  ➔  Applied Fix", "Time" },
                        fixRows,
                        System.Drawing.Color.FromArgb(228, 247, 235),
                        System.Drawing.Color.FromArgb(14, 92, 46));
                }

                AppendNoteLine("Reference: Myanmar Language Commission (မြန်မာစာအဖွဲ့) 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' & 'မြန်မာအဘိဓာန်' (100% Offline) • Creator: Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University.");

                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
                doc.Activate();
                return true;
            }
            catch
            {
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
                return false;
            }
        }

        /// <summary>
        /// Converts colloquial Burmese prose (ပါတယ်/တွေ/နဲ့/တဲ့/ဒါပေမဲ့/မ...ဘူး) into formal official/literary prose
        /// (ပါသည်/များ/နှင့်/သည့်/သို့ရာတွင်/မ...ပါ) across either the current selection or the entire document
        /// inside a single Ctrl+Z UndoRecord while preserving paragraph formatting.
        /// </summary>
        public int RewriteProseToOfficialStyle(
            Word.Application app,
            Word.Document? doc,
            bool selectionOnly = false,
            Action<int, string>? onProgress = null,
            Func<bool>? isCancelled = null)
        {
            if (app == null || doc == null)
                return 0;

            int totalReplacements = 0;
            Word.UndoRecord? undo = null;
            bool startedUndo = false;
            bool prevScreenUpdating = true;

            try
            {
                try { prevScreenUpdating = app.ScreenUpdating; app.ScreenUpdating = false; } catch { }
                try
                {
                    undo = app.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord(UiLocalizer.Pick("Rewrite to Official / Literary Style", "စကားပြောဟန်မှ ရုံးသုံး/စာပေဟန်သို့ ပြောင်းလဲခြင်း"));
                        startedUndo = true;
                    }
                }
                catch { }

                if (selectionOnly)
                {
                    Word.Selection? sel = app.Selection;
                    if (sel?.Range != null && sel.Range.Start != sel.Range.End)
                    {
                        string selRawWithMarkers = sel.Range.Text ?? string.Empty;
                        int selCount = 0;
                        TryTransformParagraphSegmentsSafely(
                            sel.Range,
                            selRawWithMarkers,
                            seg =>
                            {
                                var rw = MyanmarOfficialRewriter.ConvertToOfficialProse(seg);
                                if (rw.ReplacementCount > 0 && !string.Equals(rw.RewrittenText, seg, StringComparison.Ordinal))
                                {
                                    selCount += rw.ReplacementCount;
                                    return (true, rw.RewrittenText);
                                }
                                return (false, seg);
                            });
                        totalReplacements += selCount;
                        return totalReplacements;
                    }
                }

                var pumpWatch = Stopwatch.StartNew();
                foreach (Word.Paragraphs paras in EnumerateStoryParagraphCollections(doc))
                {
                    int pCount = paras.Count;
                    int done = 0;
                    for (int i = pCount; i >= 1; i--)
                    {
                        if (isCancelled != null && isCancelled())
                            break;

                        done++;
                        if (pumpWatch.ElapsedMilliseconds >= 110)
                        {
                            pumpWatch.Restart();
                            onProgress?.Invoke((done * 95) / Math.Max(1, pCount), UiLocalizer.Pick($"⏳ Rewriting to official style... {done:N0}/{pCount:N0}", $"⏳ ရုံးသုံးဟန် ပြောင်းနေသည်... {done:N0}/{pCount:N0}"));
                            Application.DoEvents();
                        }
                        try
                        {
                            Word.Range? pRange = paras[i].Range;
                            if (pRange == null)
                                continue;

                            string rawWithMarkers = pRange.Text ?? string.Empty;
                            string raw = TrimTrailingMarkersString(rawWithMarkers);
                            if (string.IsNullOrWhiteSpace(raw))
                                continue;

                            var rwPreview = MyanmarOfficialRewriter.ConvertToOfficialProse(raw);
                            if (rwPreview.ReplacementCount > 0 && !string.Equals(rwPreview.RewrittenText, raw, StringComparison.Ordinal))
                            {
                                int segCount = 0;
                                bool applied = TryTransformParagraphSegmentsSafely(
                                    pRange,
                                    rawWithMarkers,
                                    seg =>
                                    {
                                        var rw = MyanmarOfficialRewriter.ConvertToOfficialProse(seg);
                                        if (rw.ReplacementCount > 0 && !string.Equals(rw.RewrittenText, seg, StringComparison.Ordinal))
                                        {
                                            segCount += rw.ReplacementCount;
                                            return (true, rw.RewrittenText);
                                        }
                                        return (false, seg);
                                    });
                                if (applied)
                                    totalReplacements += Math.Max(1, segCount);
                            }
                        }
                        catch
                        {
                            // Skip locked/table boundary paragraphs
                        }
                    }
                }
            }
            catch
            {
                // Ignore COM errors
            }
            finally
            {
                if (startedUndo && undo != null)
                {
                    try
                    {
                        if (undo.IsRecordingCustomRecord)
                            undo.EndCustomRecord();
                    }
                    catch { }
                }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
            }

            return totalReplacements;
        }

        private static string TrimTrailingMarkersString(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int len = text.Length;
            while (len > 0)
            {
                char ch = text[len - 1];
                if (ch == '\r' || ch == '\a' || ch == '\n' || ch == '\f' || ch == '\v')
                    len--;
                else
                    break;
            }

            return len == text.Length ? text : (len == 0 ? string.Empty : text.Substring(0, len));
        }

        private static Word.Range? TrimTrailingParagraphAndCellMarkers(Word.Range? range)
        {
            if (range == null)
                return null;

            try
            {
                Word.Range clean = range.Duplicate;
                string txt = clean.Text ?? string.Empty;
                int trimComUnits = 0;
                int idx = txt.Length - 1;
                while (idx >= 0)
                {
                    // In Word COM, a table cell's trailing "\r\a" (2 .NET chars) occupies a single 1-char COM position!
                    if (idx >= 1 && txt[idx] == '\a' && txt[idx - 1] == '\r')
                    {
                        trimComUnits++;
                        idx -= 2;
                        continue;
                    }

                    char ch = txt[idx];
                    if (ch == '\r' || ch == '\a' || ch == '\n' || ch == '\f' || ch == '\v')
                    {
                        trimComUnits++;
                        idx--;
                    }
                    else
                    {
                        break;
                    }
                }

                if (trimComUnits > 0 && clean.End - trimComUnits >= clean.Start)
                {
                    clean.End -= trimComUnits;
                }
                return clean;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Replaces plain-text segments (<c>c &gt; '\u001F'</c>) inside <paramref name="range"/> from END to START while strictly
        /// preserving: (1) leading invisible floating <see cref="Word.Shape"/> anchors at <c>range.Start..textBaseStart</c>,
        /// (2) inline Footnote/Endnote (<c>\u0002</c>), InlineShape (<c>\u0001</c>), Comment (<c>\u0005</c>), Tab (<c>\t</c>),
        /// and soft line-break (<c>\v</c>) markers, and (3) table-cell <c>\r\a</c> boundaries.
        /// </summary>
        private static bool TryTransformParagraphSegmentsSafely(
            Word.Range? range,
            string rawWithMarkers,
            Func<string, (bool ShouldChangeText, string NewText)> transformSegment,
            string? applyFontName = null,
            bool forceApplyFontEvenIfUnchanged = false)
        {
            if (range == null || string.IsNullOrEmpty(rawWithMarkers))
                return false;

            int trimmedLen = rawWithMarkers.Length;
            while (trimmedLen > 0)
            {
                char ch = rawWithMarkers[trimmedLen - 1];
                if (ch == '\r' || ch == '\a' || ch == '\n' || ch == '\f' || ch == '\v')
                    trimmedLen--;
                else
                    break;
            }
            if (trimmedLen <= 0)
                return false;

            var pending = new List<(int SegStart, int SegEnd, string OldText, bool ShouldChangeText, string NewText)>(2);
            int pos = 0;
            while (pos < trimmedLen)
            {
                while (pos < trimmedLen && rawWithMarkers[pos] <= '\u001F')
                    pos++;
                if (pos >= trimmedLen)
                    break;

                int segStart = pos;
                while (pos < trimmedLen && rawWithMarkers[pos] > '\u001F')
                    pos++;
                int segEnd = pos;

                if (segEnd <= segStart)
                    continue;

                string oldSeg = rawWithMarkers.Substring(segStart, segEnd - segStart);
                var (shouldChange, newSeg) = transformSegment(oldSeg);
                bool willChangeText = shouldChange && !string.Equals(newSeg, oldSeg, StringComparison.Ordinal);
                bool willChangeFont = forceApplyFontEvenIfUnchanged && !string.IsNullOrEmpty(applyFontName);

                if (willChangeText || willChangeFont)
                {
                    pending.Add((segStart, segEnd, oldSeg, willChangeText, newSeg));
                }
            }

            if (pending.Count == 0)
                return false;

            int initialStart;
            int initialEnd;
            try
            {
                initialStart = range.Start;
                initialEnd = range.End;
            }
            catch
            {
                return false;
            }

            if (initialEnd <= initialStart)
                return false;

            // Compute exact COM text base BEFORE mutating any segment:
            // - trailing "\r\a" in table cells is 2 .NET chars for 1 COM unit
            // - K floating shapes anchored to the paragraph occupy initialStart .. initialStart + K
            int effectiveUnits = rawWithMarkers.EndsWith("\r\a", StringComparison.Ordinal)
                ? rawWithMarkers.Length - 1
                : rawWithMarkers.Length;
            int initialTextBase = initialEnd - effectiveUnits;

            bool anyApplied = false;
            for (int s = pending.Count - 1; s >= 0; s--)
            {
                var item = pending[s];
                Word.Range? segRange = ResolvePlainSegmentRange(
                    range,
                    initialStart,
                    initialEnd,
                    initialTextBase,
                    rawWithMarkers,
                    item.SegStart,
                    item.OldText);

                if (segRange == null)
                    continue;

                try
                {
                    if (item.ShouldChangeText)
                    {
                        segRange.Text = item.NewText;
                    }
                    if (!string.IsNullOrEmpty(applyFontName))
                    {
                        try { segRange.Font.Name = applyFontName; } catch { }
                        try { segRange.Font.NameBi = applyFontName; } catch { }
                    }
                    anyApplied = true;
                }
                catch
                {
                    // Skip protected/locked sub-ranges safely
                }
            }

            return anyApplied;
        }

        private static Word.Range? ResolvePlainSegmentRange(
            Word.Range container,
            int initialStart,
            int initialEnd,
            int initialTextBase,
            string fullRaw,
            int segStart,
            string expectedText)
        {
            int len = expectedText.Length;
            if (len <= 0)
                return null;

            // 1. Exact O(1) TextBase probe (solves normal paragraphs, table cells, footnotes, and K floating shape anchors in 1 probe!)
            int candStart = initialTextBase + segStart;
            int candEnd = candStart + len;
            if (candStart >= initialStart && candEnd <= initialEnd)
            {
                try
                {
                    Word.Range probe = container.Duplicate;
                    probe.SetRange(candStart, candEnd);
                    if (string.Equals(probe.Text ?? string.Empty, expectedText, StringComparison.Ordinal))
                        return probe;
                }
                catch { }
            }

            // 2. Secondary O(1) probe around initialStart + segStart (handles partial mid-paragraph user selections)
            int directBase = initialStart + segStart;
            for (int delta = 0; delta <= 6; delta++)
            {
                for (int sign = 1; sign >= (delta == 0 ? 1 : -1); sign -= 2)
                {
                    int pStart = directBase + (delta * sign);
                    int pEnd = pStart + len;
                    if (pStart == candStart || pStart < initialStart || pEnd > initialEnd)
                        continue;

                    try
                    {
                        Word.Range probe = container.Duplicate;
                        probe.SetRange(pStart, pEnd);
                        if (string.Equals(probe.Text ?? string.Empty, expectedText, StringComparison.Ordinal))
                            return probe;
                    }
                    catch { }
                }
            }

            // 3. Field/Hyperlink-safe Word.Find fallback (when an earlier field code inside the same paragraph shifted COM offsets)
            if (len <= 250 && expectedText.IndexOf('^') < 0)
            {
                int targetOccurrence = 0;
                int searchIdx = 0;
                while (searchIdx <= segStart && searchIdx < fullRaw.Length)
                {
                    int hit = fullRaw.IndexOf(expectedText, searchIdx, StringComparison.Ordinal);
                    if (hit < 0 || hit > segStart)
                        break;
                    targetOccurrence++;
                    searchIdx = hit + 1;
                }
                if (targetOccurrence < 1)
                    targetOccurrence = 1;

                try
                {
                    Word.Range findScope = container.Duplicate;
                    findScope.SetRange(initialStart, initialEnd);
                    Word.Find find = findScope.Find;
                    find.ClearFormatting();
                    find.Format = false;
                    find.MatchCase = true;
                    find.MatchWholeWord = false;
                    find.MatchWildcards = false;
                    find.MatchSoundsLike = false;
                    find.MatchAllWordForms = false;
                    try { find.MatchDiacritics = true; } catch { }
                    try { find.MatchControl = true; } catch { }

                    int seen = 0;
                    int guard = 0;
                    int prevNext = -1;
                    while (guard++ < 32)
                    {
                        find.Text = expectedText;
                        find.Forward = true;
                        find.Wrap = Word.WdFindWrap.wdFindStop;
                        if (!find.Execute())
                            break;

                        int hStart = findScope.Start;
                        int hEnd = findScope.End;
                        if (hStart < initialStart || hEnd > initialEnd)
                            break;

                        // Require (hEnd - hStart) == len so we never overwrite a hidden HYPERLINK field inside expectedText!
                        if ((hEnd - hStart) == len &&
                            string.Equals(findScope.Text ?? string.Empty, expectedText, StringComparison.Ordinal))
                        {
                            seen++;
                            if (seen >= targetOccurrence)
                            {
                                Word.Range exact = container.Duplicate;
                                exact.SetRange(hStart, hEnd);
                                return exact;
                            }
                        }

                        int nextPos = hStart + 1;
                        if (nextPos <= prevNext || nextPos >= initialEnd)
                            break;
                        prevNext = nextPos;
                        findScope.SetRange(nextPos, initialEnd);
                    }
                }
                catch { }
            }

            return null;
        }

        private static IEnumerable<Word.Paragraphs> EnumerateStoryParagraphCollections(Word.Document doc)
        {
            foreach (Word.Range story in EnumerateDocumentStories(doc))
            {
                Word.Paragraphs? paras = null;
                try { paras = story.Paragraphs; } catch { }
                if (paras != null)
                    yield return paras;
            }
        }

        private static IEnumerable<Word.Range> EnumerateDocumentStories(Word.Document doc)
        {
            var collected = new List<Word.Range>();

            try
            {
                Word.StoryRanges? stories = doc.StoryRanges;
                if (stories != null)
                {
                    foreach (Word.Range firstStory in stories)
                    {
                        Word.Range? current = firstStory;
                        int hops = 0;
                        while (current != null && hops < 256)
                        {
                            hops++;
                            Word.WdStoryType st = Word.WdStoryType.wdMainTextStory;
                            try { st = current.StoryType; } catch { }

                            if (st == Word.WdStoryType.wdMainTextStory ||
                                st == Word.WdStoryType.wdPrimaryHeaderStory ||
                                st == Word.WdStoryType.wdFirstPageHeaderStory ||
                                st == Word.WdStoryType.wdEvenPagesHeaderStory ||
                                st == Word.WdStoryType.wdPrimaryFooterStory ||
                                st == Word.WdStoryType.wdFirstPageFooterStory ||
                                st == Word.WdStoryType.wdEvenPagesFooterStory ||
                                st == Word.WdStoryType.wdTextFrameStory ||
                                st == Word.WdStoryType.wdFootnotesStory ||
                                st == Word.WdStoryType.wdEndnotesStory)
                            {
                                if (!IsDuplicateStoryOrShapeRange(collected, current))
                                {
                                    collected.Add(current);
                                }
                            }

                            try { current = current.NextStoryRange; }
                            catch { current = null; }
                        }
                    }
                }
            }
            catch { }

            // Phase 2: Modern DrawingML TextBoxes, Callouts, and Grouped Shapes in Body + Headers/Footers
            int shapeBudget = 300;
            try
            {
                CollectDrawingShapeTextRanges(doc.Shapes, collected, ref shapeBudget, depth: 0);
            }
            catch { }

            try
            {
                Word.Sections? sections = doc.Sections;
                if (sections != null && shapeBudget > 0)
                {
                    int secCount = sections.Count;
                    var hfIndices = new[]
                    {
                        Word.WdHeaderFooterIndex.wdHeaderFooterPrimary,
                        Word.WdHeaderFooterIndex.wdHeaderFooterFirstPage,
                        Word.WdHeaderFooterIndex.wdHeaderFooterEvenPages
                    };

                    for (int s = 1; s <= secCount && shapeBudget > 0; s++)
                    {
                        Word.Section? sec = null;
                        try { sec = sections[s]; } catch { continue; }
                        if (sec == null) continue;

                        foreach (var hfIdx in hfIndices)
                        {
                            if (shapeBudget <= 0) break;
                            try
                            {
                                Word.HeaderFooter? hdr = sec.Headers[hfIdx];
                                if (hdr != null && hdr.Exists)
                                {
                                    CollectDrawingShapeTextRanges(hdr.Shapes, collected, ref shapeBudget, depth: 0);
                                }
                            }
                            catch { }

                            if (shapeBudget <= 0) break;
                            try
                            {
                                Word.HeaderFooter? ftr = sec.Footers[hfIdx];
                                if (ftr != null && ftr.Exists)
                                {
                                    CollectDrawingShapeTextRanges(ftr.Shapes, collected, ref shapeBudget, depth: 0);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            return collected;
        }

        private static void CollectDrawingShapeTextRanges(
            Word.Shapes? shapes,
            List<Word.Range> collected,
            ref int remainingBudget,
            int depth)
        {
            if (shapes == null || remainingBudget <= 0 || depth > 5)
                return;

            int count = 0;
            try { count = shapes.Count; } catch { return; }
            for (int i = 1; i <= count && remainingBudget > 0; i++)
            {
                Word.Shape? shape = null;
                try { shape = shapes[i]; } catch { continue; }
                if (shape == null) continue;

                CollectSingleShapeOrGroupRanges(shape, collected, ref remainingBudget, depth);
            }
        }

        private static void CollectSingleShapeOrGroupRanges(
            Word.Shape shape,
            List<Word.Range> collected,
            ref int remainingBudget,
            int depth)
        {
            if (remainingBudget <= 0 || depth > 5)
                return;

            remainingBudget--;

            int shapeType = 0;
            try { shapeType = (int)shape.Type; } catch { }
            if (shapeType == 6) // msoGroup
            {
                try
                {
                    Word.GroupShapes? groupItems = shape.GroupItems;
                    if (groupItems != null)
                    {
                        int gCount = groupItems.Count;
                        for (int g = 1; g <= gCount && remainingBudget > 0; g++)
                        {
                            Word.Shape? child = null;
                            try { child = groupItems[g]; } catch { continue; }
                            if (child != null)
                            {
                                CollectSingleShapeOrGroupRanges(child, collected, ref remainingBudget, depth + 1);
                            }
                        }
                    }
                }
                catch { }
                return;
            }
            if (shapeType == 20) // msoCanvas
            {
                try
                {
                    Word.CanvasShapes? canvasItems = shape.CanvasItems;
                    if (canvasItems != null)
                    {
                        int cCount = canvasItems.Count;
                        for (int c = 1; c <= cCount && remainingBudget > 0; c++)
                        {
                            Word.Shape? child = null;
                            try { child = canvasItems[c]; } catch { continue; }
                            if (child != null)
                            {
                                CollectSingleShapeOrGroupRanges(child, collected, ref remainingBudget, depth + 1);
                            }
                        }
                    }
                }
                catch { }
                return;
            }

            try
            {
                Word.TextFrame? tf = shape.TextFrame;
                if (tf != null && tf.HasText != 0)
                {
                    Word.Range? tr = tf.TextRange;
                    if (tr != null && !IsDuplicateStoryOrShapeRange(collected, tr))
                    {
                        collected.Add(tr);
                    }
                }
            }
            catch { }
        }

        private static bool IsDuplicateStoryOrShapeRange(List<Word.Range> existingRanges, Word.Range candidate)
        {
            for (int i = 0; i < existingRanges.Count; i++)
            {
                try
                {
                    if (existingRanges[i].IsEqual(candidate))
                        return true;
                }
                catch { }
            }
            return false;
        }
    }
}
