using System;
using System.Collections.Generic;
using System.Text;
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

        public ToolkitActionSummary ConvertLegacyFontsInDocument(Word.Application app, Word.Document? doc, bool selectionOnly = false)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary { Success = false, UserMessage = "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။" };
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
                        undo.StartCustomRecord("မြန်မာ ယူနီကုဒ် (Pyidaungsu) ပြောင်းလဲခြင်း");
                        startedUndo = true;
                    }
                }
                catch { }

                if (selectionOnly && app.Selection != null && app.Selection.Range != null &&
                    app.Selection.Range.Start != app.Selection.Range.End)
                {
                    convertedRuns += ConvertLegacyFontsInParagraphs(app.Selection.Range.Paragraphs);
                }
                else
                {
                    foreach (Word.Range story in EnumerateDocumentStories(doc))
                    {
                        convertedRuns += ConvertLegacyFontsInParagraphs(story.Paragraphs);
                    }
                }

                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = convertedRuns,
                    UserMessage = convertedRuns > 0
                        ? $"✓ Legacy/Zawgyi စာသားအပိုင်း ({convertedRuns}) ခုကို Pyidaungsu ယူနီကုဒ်သို့ မူလစာလုံးအလှမပျက် ပြောင်းလဲပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။"
                        : "✓ ဤစာတမ်းသည် မြန်မာယူနီကုဒ် စံနှုန်းနှင့် ကိုက်ညီပြီးဖြစ်ပါသည် (ပြောင်းလဲရန် WinInnwa / Zawgyi မတွေ့ရှိပါ)။"
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary
                {
                    Success = false,
                    ChangedCount = convertedRuns,
                    UserMessage = $"⚠ ဖောင့်ပြောင်းလဲရာတွင် အခက်အခဲရှိပါသည်: {ex.Message}"
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

        private static int ConvertLegacyFontsInParagraphs(Word.Paragraphs? paragraphs)
        {
            if (paragraphs == null)
                return 0;

            int changed = 0;
            int count = paragraphs.Count;
            for (int i = 1; i <= count; i++)
            {
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

                    Word.Range? bodyRange = TrimTrailingParagraphAndCellMarkers(paraRange);
                    if (bodyRange == null || bodyRange.Start >= bodyRange.End)
                        continue;

                    string bodyRaw = bodyRange.Text ?? string.Empty;
                    string converted = isWinLegacy
                        ? LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode(bodyRaw)
                        : LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu(bodyRaw, paraFont, out _);

                    if (!string.Equals(converted, bodyRaw, StringComparison.Ordinal) || isWinLegacy || LegacyMyanmarFontConverter.IsZawgyiFont(paraFont))
                    {
                        if (!string.Equals(converted, bodyRaw, StringComparison.Ordinal))
                        {
                            bodyRange.Text = converted;
                        }
                        try { bodyRange.Font.Name = TargetUnicodeFontName; } catch { }
                        try { bodyRange.Font.NameBi = TargetUnicodeFontName; } catch { }
                        changed++;
                    }
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

        public ToolkitActionSummary FormatNumeralsInDocument(Word.Application app, Word.Document? doc, bool toMyanmarDigits, bool selectionOnly = false)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary { Success = false, UserMessage = "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။" };
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
                        undo.StartCustomRecord(toMyanmarDigits ? "မြန်မာဂဏန်း (၀-၉) ပြောင်းလဲခြင်း" : "အင်္ဂလိပ်ဂဏန်း (0-9) ပြောင်းလဲခြင်း");
                        startedUndo = true;
                    }
                }
                catch { }

                IEnumerable<Word.Paragraphs> targets = (selectionOnly && app.Selection?.Range != null && app.Selection.Range.Start != app.Selection.Range.End)
                    ? new[] { app.Selection.Range.Paragraphs }
                    : EnumerateStoryParagraphCollections(doc);

                foreach (var paras in targets)
                {
                    int pCount = paras.Count;
                    for (int i = 1; i <= pCount; i++)
                    {
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
                            Word.Range? body = TrimTrailingParagraphAndCellMarkers(pRange);
                            if (body != null && body.Start < body.End)
                            {
                                body.Text = formatted;
                                totalDigitsChanged += digitsConverted;
                            }
                        }
                    }
                }

                string dirLabel = toMyanmarDigits ? "မြန်မာဂဏန်း (၀-၉)" : "အင်္ဂလိပ်ဂဏန်း (0-9)";
                return new ToolkitActionSummary
                {
                    Success = true,
                    ChangedCount = totalDigitsChanged,
                    UserMessage = totalDigitsChanged > 0
                        ? $"✓ ဂဏန်းလုံးရေ ({totalDigitsChanged}) လုံးကို {dirLabel} သို့ စနစ်တကျ ပြောင်းလဲပြီးပါပြီ (URL/ISO/COVID ကုဒ်များကို မထိခိုက်စေဘဲ ထိန်းသိမ်းထားသည်)။"
                        : $"✓ ပြောင်းလဲရန် ဂဏန်း မတွေ့ရှိပါ ({dirLabel} အသင့်ဖြစ်ပြီးဖြစ်သည်)။"
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary { Success = false, UserMessage = $"⚠ ဂဏန်းပြောင်းလဲစဉ် အမှားဖြစ်ပေါ်သည်: {ex.Message}" };
            }
            finally
            {
                if (startedUndo && undo != null) { try { undo.EndCustomRecord(); } catch { } }
                try { app.ScreenUpdating = prevScreenUpdating; } catch { }
            }
        }

        public ToolkitActionSummary FormatOfficialPunctuationInDocument(Word.Application app, Word.Document? doc, bool selectionOnly = false)
        {
            if (app == null || doc == null)
            {
                return new ToolkitActionSummary { Success = false, UserMessage = "လက်ရှိဖွင့်ထားသော Word စာတမ်း မတွေ့ရှိပါ။" };
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
                        undo.StartCustomRecord("ပုဒ်ဖြတ် ၊ / ။ နှင့် ZWSP သန့်စင်ခြင်း");
                        startedUndo = true;
                    }
                }
                catch { }

                IEnumerable<Word.Paragraphs> targets = (selectionOnly && app.Selection?.Range != null && app.Selection.Range.Start != app.Selection.Range.End)
                    ? new[] { app.Selection.Range.Paragraphs }
                    : EnumerateStoryParagraphCollections(doc);

                foreach (var paras in targets)
                {
                    int pCount = paras.Count;
                    for (int i = 1; i <= pCount; i++)
                    {
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
                            Word.Range? body = TrimTrailingParagraphAndCellMarkers(pRange);
                            if (body != null && body.Start < body.End)
                            {
                                body.Text = cleaned;
                                totalPunctFixes += pFixes;
                                totalZwRemoved += zwFixes;
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
                        ? $"✓ ပုဒ်ဖြတ်အကွာအဝေး ({totalPunctFixes}) နေရာ နှင့် မမြင်ရသော ZWSP ({totalZwRemoved}) လုံးကို သန့်စင်ညှိနှိုင်းပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။"
                        : "✓ ပုဒ်ဖြတ် (၊ / ။) အကွာအဝေး နှင့် ZWSP များ သန့်စင်မှန်ကန်ပြီး ဖြစ်ပါသည်။"
                };
            }
            catch (Exception ex)
            {
                return new ToolkitActionSummary { Success = false, UserMessage = $"⚠ ပုဒ်ဖြတ်ညှိနှိုင်းစဉ် အမှားဖြစ်ပေါ်သည်: {ex.Message}" };
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
        /// Converts colloquial Burmese prose (ပါတယ်/တွေ/နဲ့/တဲ့/ဒါပေမဲ့/မ...ဘူး) into formal official/literary prose
        /// (ပါသည်/များ/နှင့်/သည့်/သို့ရာတွင်/မ...ပါ) across either the current selection or the entire document
        /// inside a single Ctrl+Z UndoRecord while preserving paragraph formatting.
        /// </summary>
        public int RewriteProseToOfficialStyle(Word.Application app, Word.Document? doc, bool selectionOnly = false)
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
                        undo.StartCustomRecord("စကားပြောဟန်မှ ရုံးသုံး/စာပေဟန်သို့ ပြောင်းလဲခြင်း");
                        startedUndo = true;
                    }
                }
                catch { }

                if (selectionOnly)
                {
                    Word.Selection? sel = app.Selection;
                    if (sel?.Range != null && sel.Range.Start != sel.Range.End)
                    {
                        Word.Range? cleanSel = TrimTrailingParagraphAndCellMarkers(sel.Range);
                        if (cleanSel != null)
                        {
                            string raw = cleanSel.Text ?? string.Empty;
                            var rw = MyanmarOfficialRewriter.ConvertToOfficialProse(raw);
                            if (rw.ReplacementCount > 0 && rw.RewrittenText != raw)
                            {
                                cleanSel.Text = rw.RewrittenText;
                                totalReplacements += rw.ReplacementCount;
                            }
                        }
                        return totalReplacements;
                    }
                }

                foreach (Word.Paragraphs paras in EnumerateStoryParagraphCollections(doc))
                {
                    int pCount = paras.Count;
                    for (int i = pCount; i >= 1; i--)
                    {
                        try
                        {
                            Word.Range? pRange = paras[i].Range;
                            if (pRange == null)
                                continue;

                            string raw = TrimTrailingMarkersString(pRange.Text ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(raw))
                                continue;

                            var rw = MyanmarOfficialRewriter.ConvertToOfficialProse(raw);
                            if (rw.ReplacementCount > 0 && rw.RewrittenText != raw)
                            {
                                Word.Range? cleanPara = TrimTrailingParagraphAndCellMarkers(pRange);
                                if (cleanPara != null && cleanPara.Start < cleanPara.End)
                                {
                                    cleanPara.Text = rw.RewrittenText;
                                    totalReplacements += rw.ReplacementCount;
                                }
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
                int trimChars = 0;
                for (int idx = txt.Length - 1; idx >= 0; idx--)
                {
                    char ch = txt[idx];
                    if (ch == '\r' || ch == '\a' || ch == '\n' || ch == '\f' || ch == '\v')
                        trimChars++;
                    else
                        break;
                }

                if (trimChars > 0 && clean.End - trimChars >= clean.Start)
                {
                    clean.End -= trimChars;
                }
                return clean;
            }
            catch
            {
                return null;
            }
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
            Word.StoryRanges? stories = null;
            try { stories = doc.StoryRanges; } catch { yield break; }
            if (stories == null)
                yield break;

            foreach (Word.Range firstStory in stories)
            {
                Word.Range? current = firstStory;
                while (current != null)
                {
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
                        yield return current;
                    }

                    try { current = current.NextStoryRange; }
                    catch { current = null; }
                }
            }
        }
    }
}
