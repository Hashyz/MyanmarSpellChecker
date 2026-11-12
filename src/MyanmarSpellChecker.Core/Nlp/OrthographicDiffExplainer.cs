using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Dynamically compares any arbitrary user misspelling against its target "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း"
    /// headword and produces a human-readable Burmese orthographic breakdown using traditional letter & diacritic names.
    /// Works on ANY user input — zero predefined WrongForm list required.
    /// </summary>
    public static class OrthographicDiffExplainer
    {
        public static string FormatSyllableBreakdown(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            List<string> syls = MyanmarSyllableBreaker.BreakIntoSyllables(word);
            if (syls.Count == 0)
                return word;

            return $"{string.Join(" • ", syls)} ({syls.Count} ဝဏ္ဏ)";
        }

        public static string ExplainDifference(string wrongText, string correctText)
        {
            if (string.IsNullOrWhiteSpace(wrongText) || string.IsNullOrWhiteSpace(correctText) || wrongText == correctText)
                return string.Empty;

            // 1. Check lookalike character explanation first
            if (UnicodeNormalizer.HasLookalikeError(wrongText, out string fixedLookalike, out string lookalikeReason) &&
                fixedLookalike == UnicodeNormalizer.NormalizeCanonicalOrder(correctText))
            {
                return lookalikeReason;
            }

            string normWrong = UnicodeNormalizer.NormalizeCanonicalOrder(wrongText.Trim());
            string normCorrect = UnicodeNormalizer.NormalizeCanonicalOrder(correctText.Trim());

            List<string> wrongSyls = MyanmarSyllableBreaker.BreakIntoSyllables(normWrong);
            List<string> correctSyls = MyanmarSyllableBreaker.BreakIntoSyllables(normCorrect);

            // 2. Consonant stacking / gemination difference across syllable boundary (e.g. ပြသနာ -> ပြဿနာ, ပုဂိုလ် -> ပုဂ္ဂိုလ်, မေတာ -> မေတ္တာ)
            if (normCorrect.Contains("\u103F") && !normWrong.Contains("\u103F"))
            {
                return $"သကြီး ('ဿ') အသုံး — '{wrongText}' အစား သကြီးဖြင့် '{correctText}' ဟု ရေးသားရမည်။";
            }

            if (normCorrect.Contains("\u1039") && !normWrong.Contains("\u1039"))
            {
                return $"ဗျည်းဆင့် စာလုံးပေါင်း — ပါဠိသက်/သတ်ပုံကျမ်း စံနှုန်းအရ '{wrongText}' အစား ဗျည်းဆင့်ဖြင့် '{correctText}' ဟု ရေးသားရမည်။";
            }

            // 3. Same syllable count: explain each differing syllable precisely!
            if (wrongSyls.Count == correctSyls.Count && wrongSyls.Count > 0)
            {
                var diffs = new List<string>();
                for (int i = 0; i < wrongSyls.Count; i++)
                {
                    if (wrongSyls[i] != correctSyls[i])
                    {
                        diffs.Add(ExplainSingleSyllableDiff(wrongSyls[i], correctSyls[i]));
                    }
                }

                if (diffs.Count > 0)
                {
                    return string.Join(" ၊ ", diffs.Take(2));
                }
            }

            // 4. Different syllable count (e.g. numeral တ -> တစ်, stacked Pali, or dropped syllable)
            if (correctText.StartsWith("တစ်", StringComparison.Ordinal) && wrongText.StartsWith("တ", StringComparison.Ordinal) && !wrongText.StartsWith("တစ်", StringComparison.Ordinal))
            {
                return $"ဂဏန်း (၁) အနက်ဆောင်သဖြင့် 'တ' အစား စလုံးသတ်ဖြင့် 'တစ်' ('{correctText}') ဟု ရေးသားရမည်။";
            }

            return $"သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်မှာ '{correctText}' ({FormatSyllableBreakdown(correctText)}) ဖြစ်ပါသည်။";
        }

        private static string ExplainSingleSyllableDiff(string wSyl, string cSyl)
        {
            // A. Medial Ya (ျ) vs Medial Ra (ြ)
            if (wSyl.Replace('\u103B', '\u103C') == cSyl)
                return $"ဗျည်းတွဲအမှား — '{wSyl}' (ယပင့် 'ျ') အစား ရစ်သံ ('ြ') ဖြင့် '{cSyl}' ဟု ပေါင်းရမည်";
            if (wSyl.Replace('\u103C', '\u103B') == cSyl)
                return $"ဗျည်းတွဲအမှား — '{wSyl}' (ရရစ် 'ြ') အစား ပင့်သံ ('ျ') ဖြင့် '{cSyl}' ဟု ပေါင်းရမည်";

            // B. Missing or extra Ha-hto (ှ)
            if (StripChar(cSyl, '\u103E') == wSyl)
                return $"ဟထိုး ('ှ') လိုအပ်ချက် — '{wSyl}' အစား ဟထိုးထည့်၍ '{cSyl}' ဟု ပေါင်းရမည်";
            if (StripChar(wSyl, '\u103E') == cSyl)
                return $"ဟထိုး ('ှ') ပိုနေခြင်း — '{wSyl}' အစား ဟထိုးမပါဘဲ '{cSyl}' ဟု ပေါင်းရမည်";

            // C. Missing or extra Wa-hswe (ွ)
            if (StripChar(cSyl, '\u103D') == wSyl)
                return $"ဝဆွဲ ('ွ') လိုအပ်ချက် — '{wSyl}' အစား ဝဆွဲထည့်၍ '{cSyl}' ဟု ပေါင်းရမည်";
            if (StripChar(wSyl, '\u103D') == cSyl)
                return $"ဝဆွဲ ('ွ') ပိုနေခြင်း — '{wSyl}' အစား ဝဆွဲမပါဘဲ '{cSyl}' ဟု ပေါင်းရမည်";

            // D. Tone marks: Dot Below (့) and Visarga (း)
            if (StripTones(wSyl) == StripTones(cSyl))
            {
                bool cHasVisarga = cSyl.Contains('\u1038');
                bool cHasDot = cSyl.Contains('\u1037');
                if (cHasVisarga && !wSyl.Contains('\u1038'))
                    return $"သံစဉ်အမှား — '{wSyl}' အစား ဝစ္စပေါက် ('း') ထည့်၍ '{cSyl}' ဟု ရေးရမည်";
                if (cHasDot && !wSyl.Contains('\u1037'))
                    return $"သံစဉ်အမှား — '{wSyl}' အစား အောက်မြစ် ('့') ထည့်၍ '{cSyl}' ဟု ရေးရမည်";
                return $"သံစဉ်အမှတ်အသား — '{wSyl}' အစား '{cSyl}' ဟု ရေးရမည်";
            }

            // E. Coda (အသတ် / သေးသေးတင်) differences (e.g. ရမ် -> ရန်, သတ် -> သက်, ချည် -> ချဉ်)
            string wCoda = ExtractCoda(wSyl);
            string cCoda = ExtractCoda(cSyl);
            if (!string.IsNullOrEmpty(cCoda) && wCoda != cCoda)
            {
                string cCodaName = GetCodaName(cCoda);
                if (!string.IsNullOrEmpty(wCoda))
                {
                    string wCodaName = GetCodaName(wCoda);
                    return $"အသတ်ခွဲထားချက် — '{wSyl}' ({wCodaName}) အစား {cCodaName}ဖြင့် '{cSyl}' ဟု ရေးရမည်";
                }
                return $"အသတ်ခွဲထားချက် — '{wSyl}' အစား {cCodaName}ဖြင့် '{cSyl}' ဟု ရေးရမည်";
            }

            // F. Vowel length / rhyme differences (ိ vs ီ, ု vs ူ, ေ vs ည်)
            if ((wSyl.Contains('\u102D') && cSyl.Contains('\u102E')) || (wSyl.Contains('\u102E') && cSyl.Contains('\u102D')))
            {
                string targetVowel = cSyl.Contains('\u102E') ? "လုံးကြီးတင်ဆန်ခတ် ('ီ')" : "လုံးကြီးတင် ('ိ')";
                return $"သရအတို/အရှည် — '{wSyl}' အစား {targetVowel} ဖြင့် '{cSyl}' ဟု ရေးရမည်";
            }
            if ((wSyl.Contains('\u102F') && cSyl.Contains('\u1030')) || (wSyl.Contains('\u1030') && cSyl.Contains('\u102F')))
            {
                string targetVowel = cSyl.Contains('\u1030') ? "နှစ်ချောင်းငင် ('ူ')" : "တစ်ချောင်းငင် ('ု')";
                return $"သရအတို/အရှည် — '{wSyl}' အစား {targetVowel} ဖြင့် '{cSyl}' ဟု ရေးရမည်";
            }

            // G. Base consonant difference (e.g. ယ vs ရ, ဖ vs ဘ, က vs ဂ, ဌ vs ဋ္ဌ, ဏ္ဍ vs ဏ္ဌ)
            if (wSyl.Length > 0 && cSyl.Length > 0 && wSyl[0] != cSyl[0])
            {
                string wCons = GetConsonantName(wSyl[0]);
                string cCons = GetConsonantName(cSyl[0]);
                return $"ဗျည်းအက္ခရာ ခွဲထားချက် — '{wSyl}' ({wCons}) အစား {cCons}ဖြင့် '{cSyl}' ဟု ရေးရမည်";
            }

            if (cSyl.Contains("\u1039"))
            {
                return $"ဗျည်းဆင့် စာလုံးပေါင်း — '{wSyl}' အစား '{cSyl}' ဟု ရေးရမည်";
            }

            return $"'{wSyl}' အစား '{cSyl}' ဟု ပေါင်းရမည်";
        }

        private static string StripChar(string s, char target)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != target) sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static string StripTones(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\u1037' && s[i] != '\u1038')
                    sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static string ExtractCoda(string syl)
        {
            string s = StripTones(syl);
            if (s.EndsWith("\u1036", StringComparison.Ordinal))
                return "\u1036";
            if (s.Length >= 2 && s[s.Length - 1] == '\u103A')
                return s.Substring(s.Length - 2, 2);
            return string.Empty;
        }

        private static string GetCodaName(string coda)
        {
            switch (coda)
            {
                case "\u1000\u103A": return "ကကြီးသတ် ('က်') ";
                case "\u1004\u103A": return "ငသတ် ('င်') ";
                case "\u1005\u103A": return "စလုံးသတ် ('စ်') ";
                case "\u1009\u103A": return "ဉကလေးသတ် ('ဉ်') ";
                case "\u100A\u103A": return "ညကြီးသတ် ('ည်') ";
                case "\u100F\u103A": return "ဏကြီးသတ် ('ဏ်') ";
                case "\u1010\u103A": return "တဝမ်းပူသတ် ('တ်') ";
                case "\u1014\u103A": return "နငယ်သတ် ('န်') ";
                case "\u1015\u103A": return "ပစောက်သတ် ('ပ်') ";
                case "\u1019\u103A": return "မသတ် ('မ်') ";
                case "\u101A\u103A": return "ယပက်လက်သတ် ('ယ်') ";
                case "\u101D\u103A": return "ဝသတ် ('ဝ်') ";
                case "\u1036": return "သေးသေးတင် ('ံ') ";
                default: return $"'{coda}' ";
            }
        }

        private static string GetConsonantName(char c)
        {
            switch (c)
            {
                case '\u1000': return "ကကြီး ('က') ";
                case '\u1001': return "ခကွေး ('ခ') ";
                case '\u1002': return "ဂငယ် ('ဂ') ";
                case '\u1003': return "ဃကြီး ('ဃ') ";
                case '\u1004': return "င ('င') ";
                case '\u1005': return "စလုံး ('စ') ";
                case '\u1006': return "ဆလိမ် ('ဆ') ";
                case '\u1007': return "ဇကွဲ ('ဇ') ";
                case '\u1008': return "ဈမျဉ်းဆွဲ ('ဈ') ";
                case '\u1009': return "ဉကလေး ('ဉ') ";
                case '\u100A': return "ညကြီး ('ည') ";
                case '\u100B': return "ဋသံလျင်းချိတ် ('ဋ') ";
                case '\u100C': return "ဌဝမ်းဘဲ ('ဌ') ";
                case '\u100D': return "ဍရင်ကောက် ('ဍ') ";
                case '\u100E': return "ဎရေမှုတ် ('ဎ') ";
                case '\u100F': return "ဏကြီး ('ဏ') ";
                case '\u1010': return "တဝမ်းပူ ('တ') ";
                case '\u1011': return "ထဆင်ထူး ('ထ') ";
                case '\u1012': return "ဒထွေး ('ဒ') ";
                case '\u1013': return "ဓအောက်ခြိုက် ('ဓ') ";
                case '\u1014': return "နငယ် ('န') ";
                case '\u1015': return "ပစောက် ('ပ') ";
                case '\u1016': return "ဖဦးထုပ် ('ဖ') ";
                case '\u1017': return "ဗထက်ခြိုက် ('ဗ') ";
                case '\u1018': return "ဘကုန်း ('ဘ') ";
                case '\u1019': return "မ ('မ') ";
                case '\u101A': return "ယပက်လက် ('ယ') ";
                case '\u101B': return "ရကောက် ('ရ') ";
                case '\u101C': return "လ ('လ') ";
                case '\u101D': return "ဝ ('ဝ') ";
                case '\u101E': return "သ ('သ') ";
                case '\u101F': return "ဟ ('ဟ') ";
                case '\u1020': return "ဠကြီး ('ဠ') ";
                case '\u1021': return "အ ('အ') ";
                default: return $"'{c}' ";
            }
        }
    }
}

