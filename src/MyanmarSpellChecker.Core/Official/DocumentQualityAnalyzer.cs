using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Official
{
    public sealed class DocumentQualityReport
    {
        public int OverallScore { get; set; } = 100;
        public string GradeCode { get; set; } = "A+";
        public string GradeLabel { get; set; } = "A+ စံချိန်မီ အထူးကောင်းမွန်";

        public int MyanmarSyllableCount { get; set; }
        public int WordCount { get; set; }
        public int SentenceCount { get; set; }
        public int ParagraphCount { get; set; }
        public double EstimatedReadingMinutes { get; set; }

        public double UnicodePurityPercent { get; set; } = 100.0;
        public double FormalRegisterPercent { get; set; } = 100.0;

        public int SpellingIssueCount { get; set; }
        public int GrammarIssueCount { get; set; }
        public int ContextWarningCount { get; set; }
        public int EncodingIssueCount { get; set; }
        public int TotalIssueCount { get; set; }

        public OfficialStructureAuditResult OfficialLetterAudit { get; set; } = new OfficialStructureAuditResult();
        public List<ProofingIssue> TopIssuesSample { get; set; } = new List<ProofingIssue>();
    }

    /// <summary>
    /// Computes an enterprise/ministry 0–100% Orthography, Unicode Purity & Official Style Quality Scorecard
    /// and formats a printable Burmese Audit Certificate report (100% local/offline).
    /// </summary>
    public static class DocumentQualityAnalyzer
    {
        private static bool IsAsciiAlnum(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

        private static bool IsLatinConnector(char c) =>
            c == '-' || c == '_' || c == '.' || c == '/';

        private static bool IsRegisterBoundary(string text, int endIdx)
        {
            if (endIdx >= text.Length)
                return true;
            char ch = text[endIdx];
            return char.IsWhiteSpace(ch) || ch == '\u104A' || ch == '\u104B';
        }

        private static bool TryMatchTokenAt(string text, string literal, ref int idx)
        {
            int mLen = literal.Length;
            if (idx + mLen <= text.Length &&
                string.CompareOrdinal(text, idx, literal, 0, mLen) == 0 &&
                IsRegisterBoundary(text, idx + mLen))
            {
                idx += mLen - 1;
                return true;
            }
            return false;
        }

        private static void ScanDocumentMetrics(
            string text,
            out int paragraphCount,
            out int sentenceHits,
            out int latinWords,
            out int formalHits,
            out int colloqHits)
        {
            int len = text.Length;
            int paras = 0;
            int sents = 0;
            bool lineHasNonWs = false;
            bool inSentEnd = false;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (lineHasNonWs) { paras++; lineHasNonWs = false; }
                    if (i + 1 < len && text[i + 1] == '\n') i++;
                    inSentEnd = false;
                    continue;
                }
                if (c == '\n')
                {
                    if (lineHasNonWs) { paras++; lineHasNonWs = false; }
                    inSentEnd = false;
                    continue;
                }

                if (!char.IsWhiteSpace(c))
                    lineHasNonWs = true;

                if (c == '\u104B' || c == '.' || c == '!' || c == '?')
                {
                    if (!inSentEnd) { sents++; inSentEnd = true; }
                }
                else
                {
                    inSentEnd = false;
                }
            }
            if (lineHasNonWs)
                paras++;

            int lwCount = 0;
            int lw = 0;
            while (lw < len)
            {
                if (!IsAsciiAlnum(text[lw]))
                {
                    lw++;
                    continue;
                }
                lwCount++;
                lw++;
                while (lw < len && IsAsciiAlnum(text[lw]))
                    lw++;
                while (lw + 1 < len && IsLatinConnector(text[lw]) && IsAsciiAlnum(text[lw + 1]))
                {
                    lw += 2;
                    while (lw < len && IsAsciiAlnum(text[lw]))
                        lw++;
                }
            }

            int fHits = 0;
            int cHits = 0;
            for (int i = 0; i < len; i++)
            {
                switch (text[i])
                {
                    case '\u1015': // ပ: ပါသည် | ပါတယ် | ပါမယ်
                        if (TryMatchTokenAt(text, "ပါသည်", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "ပါတယ်", ref i) || TryMatchTokenAt(text, "ပါမယ်", ref i)) cHits++;
                        break;
                    case '\u1016': // ဖ: ဖြစ်သည် | ဖြစ်တယ်
                        if (TryMatchTokenAt(text, "ဖြစ်သည်", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "ဖြစ်တယ်", ref i)) cHits++;
                        break;
                    case '\u101B': // ရ: ရှိသည် | ရမည် | ရှိတယ် | ရမယ်
                        if (TryMatchTokenAt(text, "ရှိသည်", ref i) || TryMatchTokenAt(text, "ရမည်", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "ရှိတယ်", ref i) || TryMatchTokenAt(text, "ရမယ်", ref i)) cHits++;
                        break;
                    case '\u1014': // န: နိုင်သည် | နှင့်အညီ | နဲ့အတူ
                        if (TryMatchTokenAt(text, "နိုင်သည်", ref i) || TryMatchTokenAt(text, "နှင့်အညီ", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "နဲ့အတူ", ref i)) cHits++;
                        break;
                    case '\u101E': // သ: သဖြင့် | သောကြောင့် | သွားတယ်
                        if (TryMatchTokenAt(text, "သဖြင့်", ref i) || TryMatchTokenAt(text, "သောကြောင့်", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "သွားတယ်", ref i)) cHits++;
                        break;
                    case '\u101C': // လ: လျက် | လာတယ်
                        if (TryMatchTokenAt(text, "လျက်", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "လာတယ်", ref i)) cHits++;
                        break;
                    case '\u1021': // အ: အလို့ငှာ | အဲဒါ
                        if (TryMatchTokenAt(text, "အလို့ငှာ", ref i)) fHits++;
                        else if (TryMatchTokenAt(text, "အဲဒါ", ref i)) cHits++;
                        break;
                    case '\u104E': // ၏
                    case '\u104C': // ၌
                        if (IsRegisterBoundary(text, i + 1)) fHits++;
                        break;
                    case '\u1019': // မ: မဟုတ်ဘူး
                        if (TryMatchTokenAt(text, "မဟုတ်ဘူး", ref i)) cHits++;
                        break;
                    case '\u1012': // ဒ: ဒါပေမဲ့
                        if (TryMatchTokenAt(text, "ဒါပေမဲ့", ref i)) cHits++;
                        break;
                    case '\u1010': // တ: တွေကို
                        if (TryMatchTokenAt(text, "တွေကို", ref i)) cHits++;
                        break;
                }
            }

            paragraphCount = paras;
            sentenceHits = sents;
            latinWords = lwCount;
            formalHits = fHits;
            colloqHits = cHits;
        }

        public static DocumentQualityReport Analyze(
            string? documentText,
            IReadOnlyList<ProofingIssue>? existingIssues = null,
            MyanmarProofingEngine? engine = null)
        {
            var report = new DocumentQualityReport();
            string text = documentText ?? string.Empty;

            IReadOnlyList<ProofingIssue> issues = existingIssues
                ?? (engine != null && !string.IsNullOrWhiteSpace(text)
                    ? engine.CheckText(text)
                    : Array.Empty<ProofingIssue>());

            if (string.IsNullOrWhiteSpace(text))
            {
                report.OfficialLetterAudit = OfficialLetterSuite.ValidateOfficialLetterStructure(string.Empty);
                return report;
            }

            // 1. Paragraphs, Sentences, Latin Words & Register Markers in zero-allocation passes
            ScanDocumentMetrics(
                text,
                out int paraCount,
                out int sentHits,
                out int latinWords,
                out int formalHits,
                out int colloqHits);

            report.ParagraphCount = paraCount;
            report.SentenceCount = Math.Max(sentHits, paraCount > 0 ? 1 : 0);

            // 2. Myanmar Syllables & Estimated Words (zero-allocation O(N) syllable counter)
            int mmSyllables = MyanmarSyllableBreaker.CountMyanmarSyllables(text);
            report.MyanmarSyllableCount = mmSyllables;
            int estimatedMmWords = mmSyllables == 0 ? 0 : Math.Max(1, (int)Math.Round(mmSyllables / 2.2));
            report.WordCount = estimatedMmWords + latinWords;

            // Typical formal Burmese reading speed ~ 260 syllables/min (or ~180 Latin words/min)
            double readMin = (mmSyllables / 260.0) + (latinWords / 180.0);
            report.EstimatedReadingMinutes = readMin <= 0.0 ? 0.0 : Math.Round(Math.Max(0.2, readMin), 1);

            // 3. Issue Counts by Category
            int encodingCount = 0;
            int grammarCount = 0;
            int contextWarningCount = 0;
            int spellingCount = 0;

            foreach (var iss in issues)
            {
                if (iss.IssueType == ProofingIssueType.ZawgyiEncodingError ||
                    iss.IssueType == ProofingIssueType.LookalikeCharacterError)
                {
                    encodingCount++;
                }
                else if (iss.IsGrammarOrStyle)
                {
                    grammarCount++;
                }
                else if (iss.IsContextualWarning)
                {
                    contextWarningCount++;
                }
                else
                {
                    spellingCount++;
                }
            }

            report.EncodingIssueCount = encodingCount;
            report.GrammarIssueCount = grammarCount;
            report.ContextWarningCount = contextWarningCount;
            report.SpellingIssueCount = spellingCount;
            report.TotalIssueCount = issues.Count;
            report.TopIssuesSample = issues.Take(15).ToList();

            // 4. Unicode Purity %
            int zawgyiSpans = (existingIssues != null && engine != null)
                ? issues.Count(x => x.IssueType == ProofingIssueType.ZawgyiEncodingError)
                : UnicodeNormalizer.FindZawgyiSpans(text).Count;
            int totalEncodingFaults = Math.Max(encodingCount, zawgyiSpans);
            int denomSyl = Math.Max(mmSyllables, 10);
            double purity = 100.0 - Math.Min(100.0, (totalEncodingFaults * 100.0) / denomSyl * 2.5);
            if (totalEncodingFaults == 0)
                purity = 100.0;
            report.UnicodePurityPercent = Math.Round(Math.Max(0.0, purity), 1);

            // 5. Formal Literary Register %
            if (formalHits == 0 && colloqHits == 0)
            {
                report.FormalRegisterPercent = 100.0;
            }
            else
            {
                double formalRatio = (formalHits * 100.0) / (formalHits + colloqHits * 1.5);
                report.FormalRegisterPercent = Math.Round(Math.Max(0.0, Math.Min(100.0, formalRatio)), 1);
            }

            // 6. Official Letter Structure Audit
            report.OfficialLetterAudit = OfficialLetterSuite.ValidateOfficialLetterStructure(text);

            // 7. Composite 0–100 Enterprise Quality Score
            // Normalized per-100-syllable error density with gentle floor for short snippets
            double scaleBasis = Math.Max(25.0, mmSyllables);
            double weightedErrors = (spellingCount * 3.2) + (encodingCount * 3.8) + (grammarCount * 1.8);
            double densityPenalty = (weightedErrors / scaleBasis) * 35.0;
            // Direct per-issue cap so a 1-word test snippet with 1 error drops noticeably (e.g. ~82) without hitting 0
            double directPenalty = Math.Min(55.0, (spellingCount * 6.0) + (encodingCount * 8.0) + (grammarCount * 3.5));
            double blendedPenalty = (densityPenalty * 0.55) + (directPenalty * 0.45);

            if (report.FormalRegisterPercent < 80.0)
            {
                blendedPenalty += (80.0 - report.FormalRegisterPercent) * 0.12;
            }

            int score = (int)Math.Round(Math.Max(12.0, Math.Min(100.0, 100.0 - blendedPenalty)));
            if (issues.Count == 0 && totalEncodingFaults == 0 && colloqHits == 0)
            {
                score = 100;
            }
            else if (issues.Count > 0 && score == 100)
            {
                score = 97;
            }

            report.OverallScore = score;
            PopulateGrade(report);
            return report;
        }

        private static void PopulateGrade(DocumentQualityReport report)
        {
            int s = report.OverallScore;
            if (s >= 96)
            {
                report.GradeCode = "A+";
                report.GradeLabel = "A+ စံချိန်မီ အထူးကောင်းမွန်";
            }
            else if (s >= 88)
            {
                report.GradeCode = "A";
                report.GradeLabel = "A ကောင်းမွန် (ရုံးသုံးအဆင့်မီ)";
            }
            else if (s >= 75)
            {
                report.GradeCode = "B";
                report.GradeLabel = "B သင့်တင့် (အနည်းငယ် ပြင်ဆင်ရန်လို)";
            }
            else if (s >= 60)
            {
                report.GradeCode = "C";
                report.GradeLabel = "C ပြန်လည်စိစစ် ပြင်ဆင်သင့်သည်";
            }
            else
            {
                report.GradeCode = "D";
                report.GradeLabel = "D စာလုံးပေါင်းနှင့် ဖောင့်စနစ် အထူးပြင်ဆင်ရန်လို";
            }
        }

        public static string BuildAuditReportDocumentText(
            string? documentTitle,
            DocumentQualityReport report,
            DateTime? generatedAt = null)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            DateTime now = generatedAt ?? DateTime.Now;
            MyanmarDateInfo mmDate = MyanmarTraditionalCalendar.FromGregorian(now);
            string title = string.IsNullOrWhiteSpace(documentTitle) ? "လက်ရှိ မြန်မာစာတမ်း (Active Document)" : documentTitle!.Trim();

            var sb = new StringBuilder();
            sb.AppendLine("မြန်မာစာ စာလုံးပေါင်းသတ်ပုံနှင့် ရုံးသုံးစံနှုန်း စစ်ဆေးချက် အစီရင်ခံစာ");
            sb.AppendLine("(Myanmar Orthography, Unicode & Official Style Audit Certificate)");
            sb.AppendLine(new string('=', 68));
            sb.AppendLine($"စာတမ်းအမည်       : {title}");
            sb.AppendLine($"စစ်ဆေးသည့်ရက်စွဲ : {mmDate.OfficialGregorianDateText} ({mmDate.OfficialMyanmarDateText})");
            sb.AppendLine($"အရည်အသွေးရမှတ်  : {MyanmarTraditionalCalendar.ToMyanmarDigits(report.OverallScore)}% ({report.GradeLabel})");
            sb.AppendLine(new string('-', 68));
            sb.AppendLine();

            sb.AppendLine("၁။ စာတမ်း အချက်အလက်နှင့် စံနှုန်း အနှစ်ချုပ်");
            sb.AppendLine($"   - မြန်မာစာ ဝဏ္ဏ (Syllables) အရေအတွက် : {MyanmarTraditionalCalendar.ToMyanmarDigits(report.MyanmarSyllableCount)} လုံး");
            sb.AppendLine($"   - ခန့်မှန်း စကားလုံး (Words) အရေအတွက်  : {MyanmarTraditionalCalendar.ToMyanmarDigits(report.WordCount)} လုံး");
            sb.AppendLine($"   - ဝါကျ / စာပိုဒ် အရေအတွက်              : ဝါကျ {MyanmarTraditionalCalendar.ToMyanmarDigits(report.SentenceCount)} ကြောင်း / စာပိုဒ် {MyanmarTraditionalCalendar.ToMyanmarDigits(report.ParagraphCount)} ပိုဒ်");
            sb.AppendLine($"   - ခန့်မှန်း ဖတ်ရှုချိန်                 : {report.EstimatedReadingMinutes:0.0} မိနစ်");
            sb.AppendLine($"   - ပြည်ထောင်စုယူနီကုဒ် သန့်စင်မှုနှုန်း  : {report.UnicodePurityPercent:0.0}%");
            sb.AppendLine($"   - ရုံးသုံးစာပေဟန်ပန် ညီညွတ်မှုနှုန်း     : {report.FormalRegisterPercent:0.0}%");
            sb.AppendLine($"   - စုစုပေါင်း ပြင်ဆင်ရန် တွေ့ရှိချက်     : {MyanmarTraditionalCalendar.ToMyanmarDigits(report.TotalIssueCount)} ချက် (သတ်ပုံ {MyanmarTraditionalCalendar.ToMyanmarDigits(report.SpellingIssueCount)}၊ သဒ္ဒါ/ဟန်ပန် {MyanmarTraditionalCalendar.ToMyanmarDigits(report.GrammarIssueCount)}၊ ဖောင့်/အက္ခရာ {MyanmarTraditionalCalendar.ToMyanmarDigits(report.EncodingIssueCount)})");
            sb.AppendLine();

            sb.AppendLine("၂။ ရုံးစာ ဖွဲ့စည်းပုံ စံနှုန်း စစ်ဆေးချက် (Official Letter Structure Audit)");
            if (report.OfficialLetterAudit.Items.Count == 0)
            {
                sb.AppendLine("   - စာသား မပါဝင်သေးပါ။");
            }
            else
            {
                foreach (var item in report.OfficialLetterAudit.Items)
                {
                    string mark = item.Passed ? "[✓ ကိုက်ညီ]" : (item.IsAdvisory ? "[! အကြံပြုချက်]" : "[✗ လိုအပ်ချက်]");
                    sb.AppendLine($"   {mark} {item.CriterionName} — {item.Detail}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("၃။ အဓိက ပြင်ဆင်ရန် စာလုံးပေါင်းနှင့် သဒ္ဒါ တွေ့ရှိချက်များ");
            if (report.TopIssuesSample.Count == 0)
            {
                sb.AppendLine("   ✓ စာလုံးပေါင်းသတ်ပုံနှင့် ရုံးသုံးသဒ္ဒါ အမှားအယွင်း လုံးဝမတွေ့ရှိရပါ။ မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း စံနှုန်းနှင့် ၁၀၀% ကိုက်ညီပါသည်။");
            }
            else
            {
                int idx = 1;
                foreach (var iss in report.TopIssuesSample)
                {
                    string bestFix = iss.Suggestions.FirstOrDefault()?.Replacement ?? "(ပြန်လည်စိစစ်ရန်)";
                    sb.AppendLine($"   ({MyanmarTraditionalCalendar.ToMyanmarDigits(idx)}) '{iss.OriginalText}' ➔ '{bestFix}' [{iss.IssueType}]");
                    if (!string.IsNullOrWhiteSpace(iss.Message))
                    {
                        sb.AppendLine($"       ရှင်းလင်းချက် - {iss.Message}");
                    }
                    idx++;
                }
            }

            sb.AppendLine();
            sb.AppendLine("စံနှုန်းကိုးကားချက် - ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် မြန်မာစာအဖွဲ့ 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' နှင့် 'မြန်မာအဘိဓာန်' (100% Offline Verification)");
            sb.AppendLine("စစ်ဆေးရေးစနစ် ဖန်တီးသူ - ထက်ရာဇာ (Htet Yar Zar — M.Sc. Part II Candidate in Geology, Department of Geology, Kyaukse University, Kyaukse, Myanmar)");
            return sb.ToString();
        }
    }
}

