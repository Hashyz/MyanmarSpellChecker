using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MyanmarSpellChecker.Core.Nlp
{
    public enum TokenKind
    {
        MyanmarSyllable,
        MyanmarNumber,
        MyanmarPunctuation,
        Whitespace,
        Other
    }

    public readonly struct SyllableToken
    {
        public string Text { get; }
        public string NormalizedText { get; }
        public int StartIndex { get; }
        public int Length { get; }
        public TokenKind Kind { get; }

        public SyllableToken(string text, string normalizedText, int startIndex, int length, TokenKind kind)
        {
            Text = text;
            NormalizedText = normalizedText;
            StartIndex = startIndex;
            Length = length;
            Kind = kind;
        }

        public override string ToString() => $"{Text} [{StartIndex}..{StartIndex + Length}] ({Kind})";
    }

    /// <summary>
    /// High-performance Myanmar Unicode 5.1+ Syllable Breaker & Structural Orthography Validator.
    /// Splits Myanmar text into orthographic syllables while preserving exact character offsets for Word Range underlining.
    /// </summary>
    public static class MyanmarSyllableBreaker
    {
        // Matches a complete Myanmar orthographic syllable, number, punctuation, whitespace, or other token:
        // 1. Optional Kinzi prefix: \u1004\u103A\u1039
        // 2. Base consonant / independent vowel / symbol (including \u1040 or \u1044 when misused with combining marks/coda)
        // 3. Optional subscript consonants: (\u1039[\u1000-\u1021])*
        // 4. Optional medials, vowels, anusvara, tones, and asat (for \u1031\u102C\u103A)
        // 5. Optional coda consonant with asat ([\u1000-\u1021]\u103A(?!\u1039)[\u1037\u1038]*) or stacked Pali coda
        private static readonly Regex TokenPattern = new Regex(
            @"(?<Punct>[\u104A\u104B]+)|" +
            @"(?<Space>\s+)|" +
            @"(?<LaKaung>(?:\u104E|\u1044)[\u102B-\u103E]*(?:\u1004[\u102B-\u103E]*)+)|" +
            @"(?<Number>[\u1040-\u1049]+(?![\u102B-\u103E]|[\u1000-\u1021\u1025]\u103A))|" +
            @"(?<Syllable>" +
                @"(?:\u1004\u103A\u1039)?" +
                @"[\u1000-\u102A\u103F\u1040-\u1049\u104C-\u104F]" +
                @"(?:\u1039[\u1000-\u1021])*" +
                @"[\u102B-\u103E]*" +
                @"(?:[\u1000-\u1021\u1025]\u103B?(?:\u103A+(?!\u1039)[\u1037\u1038\u103A]*|\u1037\u103A+\u1038?))*" +
            @")|" +
            @"(?<StrayMark>[\u102B-\u103E]+)|" +
            @"(?<Other>[^\u1000-\u104F\s]+)",
            RegexOptions.Compiled);

        // Detects illegal repeated dependent vowels or tone marks inside a single syllable (e.g., ာာ, ိီ, းး, ့့)
        private static readonly Regex IllegalDuplicateMarksPattern = new Regex(
            @"(\u102B\u102B|\u102C\u102C|\u102B\u102C|\u102C\u102B|[\u102D\u102E]{2,}|[\u102F\u1030]{2,}|\u1031{2,}|\u1032{2,}|\u1036{2,}|\u1037{2,}|\u1038{2,}|\u103A{2,})",
            RegexOptions.Compiled);

        // Matches non-Kinzi virama consonant stacks (upper + \u1039 + lower) for Pali/Myanmar pat-sint validation
        private static readonly Regex PatSintStackPattern = new Regex(
            @"(?<!\u1004\u103A)([\u1000-\u102A\u103F])\u1039([\u1000-\u102A\u103F])",
            RegexOptions.Compiled);

        // Authoritative Pali & Myanmar pat-sint pairs verified against the 462-page မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း & ကစ္စည်းသဒ္ဒါ
        private static readonly HashSet<string> ValidPatSintPairs = new HashSet<string>(StringComparer.Ordinal)
        {
            // 1. Homorganic gemination
            "\u1000\u1000", "\u1002\u1002", "\u1005\u1005", "\u1007\u1007",
            "\u100B\u100B", "\u100D\u100D", "\u100F\u100F", "\u1010\u1010",
            "\u1012\u1012", "\u1014\u1014", "\u1015\u1015", "\u1017\u1017",
            "\u1019\u1019", "\u101A\u101A", "\u101C\u101C", "\u1020\u1020",
            // 2. Unaspirated + Aspirated of same vagga
            "\u1000\u1001", "\u1002\u1003", "\u1005\u1006", "\u1007\u1008",
            "\u100B\u100C", "\u100D\u100E", "\u1010\u1011", "\u1012\u1013",
            "\u1015\u1016", "\u1017\u1018",
            // 3. Homorganic nasal + vagga stop
            "\u1009\u1005", "\u1009\u1006", "\u1009\u1007", "\u1009\u1008",
            "\u100F\u100B", "\u100F\u100C", "\u100F\u100D", "\u100F\u100E",
            "\u1014\u1010", "\u1014\u1011", "\u1014\u1012", "\u1014\u1013",
            "\u1019\u1015", "\u1019\u1016", "\u1019\u1017", "\u1019\u1018",
            // 4. Classical Pali/Sanskrit clusters
            "\u101F\u1019", "\u101E\u1019", "\u101E\u1010", "\u101E\u1011",
            "\u1012\u1017", "\u1012\u1018", "\u1000\u1010", "\u1015\u1010",
            "\u101C\u1015"
        };

        /// <summary>
        /// Segments text into tokens (Syllables, Numbers, Punctuation, Whitespace, Other) with exact character offsets.
        /// </summary>
        public static List<SyllableToken> Tokenize(string text)
        {
            var tokens = new List<SyllableToken>();
            if (string.IsNullOrEmpty(text))
                return tokens;

            MatchCollection matches = TokenPattern.Matches(text);
            foreach (Match match in matches)
            {
                if (!match.Success || match.Length == 0)
                    continue;

                string raw = match.Value;
                int start = match.Index;
                int len = match.Length;

                if (match.Groups["Space"].Success)
                {
                    tokens.Add(new SyllableToken(raw, raw, start, len, TokenKind.Whitespace));
                }
                else if (match.Groups["Punct"].Success)
                {
                    tokens.Add(new SyllableToken(raw, raw, start, len, TokenKind.MyanmarPunctuation));
                }
                else if (match.Groups["LaKaung"].Success || match.Groups["Syllable"].Success || match.Groups["StrayMark"].Success)
                {
                    string normalized = UnicodeNormalizer.NormalizeCanonicalOrder(raw);
                    tokens.Add(new SyllableToken(raw, normalized, start, len, TokenKind.MyanmarSyllable));
                }
                else if (match.Groups["Number"].Success)
                {
                    // Check if a single '၀' is actually adjacent to Myanmar syllables (e.g. "တာ၀န်" where '၀' has no vowel/coda)
                    if (raw == "\u1040" && IsSurroundedByMyanmarLetters(text, start, len))
                    {
                        tokens.Add(new SyllableToken(raw, raw, start, len, TokenKind.MyanmarSyllable));
                    }
                    else
                    {
                        tokens.Add(new SyllableToken(raw, raw, start, len, TokenKind.MyanmarNumber));
                    }
                }
                else
                {
                    tokens.Add(new SyllableToken(raw, raw, start, len, TokenKind.Other));
                }
            }

            return tokens;
        }

        /// <summary>
        /// Splits a Myanmar word or phrase into a list of normalized syllable strings.
        /// </summary>
        public static List<string> BreakIntoSyllables(string text, bool isPreNormalized = false)
        {
            var result = new List<string>(4);
            if (string.IsNullOrEmpty(text))
                return result;

            for (Match match = TokenPattern.Match(text); match.Success; match = match.NextMatch())
            {
                if (match.Length == 0)
                    continue;

                if (match.Groups["LaKaung"].Success || match.Groups["Syllable"].Success || match.Groups["StrayMark"].Success)
                {
                    string raw = match.Value;
                    result.Add(isPreNormalized ? raw : UnicodeNormalizer.NormalizeCanonicalOrder(raw));
                }
                else if (match.Groups["Number"].Success && match.Length == 1 && text[match.Index] == '\u1040' &&
                         IsSurroundedByMyanmarLetters(text, match.Index, 1))
                {
                    result.Add("\u1040");
                }
            }
            return result;
        }

        /// <summary>
        /// Zero-Regex fast path for pre-normalized dictionary headword syllable breaking with optional string-pool interning.
        /// Produces 100% identical output to <see cref="BreakIntoSyllables"/> while avoiding Regex Match allocations on standard Myanmar words.
        /// </summary>
        internal static void BreakIntoSyllablesFast(
            string text,
            List<string> result,
            Dictionary<string, string>? stringPool = null)
        {
            result.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            int len = text.Length;
            bool canUseFastScanner = ((text[0] >= '\u1000' && text[0] <= '\u102A') || text[0] == '\u103F');
            if (canUseFastScanner)
            {
                for (int k = 0; k < len; k++)
                {
                    char ch = text[k];
                    if (ch < '\u1000' || ch > '\u103F' || ch == '\u1039')
                    {
                        canUseFastScanner = false;
                        break;
                    }
                }
            }

            if (!canUseFastScanner)
            {
                for (Match match = TokenPattern.Match(text); match.Success; match = match.NextMatch())
                {
                    if (match.Length == 0)
                        continue;

                    if (match.Groups["LaKaung"].Success || match.Groups["Syllable"].Success || match.Groups["StrayMark"].Success)
                    {
                        string raw = match.Value;
                        result.Add(InternIfPooled(raw, stringPool));
                    }
                    else if (match.Groups["Number"].Success && match.Length == 1 && text[match.Index] == '\u1040' &&
                             IsSurroundedByMyanmarLetters(text, match.Index, 1))
                    {
                        result.Add(InternIfPooled("\u1040", stringPool));
                    }
                }
                return;
            }

            int i = 0;
            while (i < len)
            {
                int start = i;
                char c0 = text[i];
                if ((c0 >= '\u1000' && c0 <= '\u102A') || c0 == '\u103F')
                {
                    i++;
                    while (i < len && text[i] >= '\u102B' && text[i] <= '\u103E')
                        i++;

                    while (i < len)
                    {
                        char cc = text[i];
                        if (!((cc >= '\u1000' && cc <= '\u1021') || cc == '\u1025'))
                            break;

                        int p = i + 1;
                        if (p < len && text[p] == '\u103B')
                            p++;

                        if (p < len && text[p] == '\u103A')
                        {
                            while (p < len && (text[p] == '\u103A' || text[p] == '\u1037' || text[p] == '\u1038'))
                                p++;
                            i = p;
                        }
                        else if (p + 1 < len && text[p] == '\u1037' && text[p + 1] == '\u103A')
                        {
                            p += 2;
                            while (p < len && text[p] == '\u103A')
                                p++;
                            if (p < len && text[p] == '\u1038')
                                p++;
                            i = p;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    while (i < len && text[i] >= '\u102B' && text[i] <= '\u103E')
                        i++;
                }

                string syl = (start == 0 && i == len) ? text : text.Substring(start, i - start);
                result.Add(InternIfPooled(syl, stringPool));
            }
        }

        private static string InternIfPooled(string s, Dictionary<string, string>? pool)
        {
            if (pool == null || s.Length == 0)
                return s;
            if (pool.TryGetValue(s, out string? existing))
                return existing;
            pool[s] = s;
            return s;
        }

        /// <summary>
        /// Validates whether a single Myanmar syllable obeys Unicode 5.1+ orthographic rules.
        /// </summary>
        public static bool IsStructurallyValidSyllable(string syllable, out string errorReason)
        {
            errorReason = string.Empty;
            if (string.IsNullOrEmpty(syllable))
                return false;

            // 1. Stray combining mark with no base consonant
            char first = syllable[0];
            if (first >= '\u102B' && first <= '\u103E')
            {
                errorReason = "အခြေခံဗျည်းမပါဘဲ သရ သို့မဟုတ် ဗျည်းတွဲသင်္ကေတ သီးသန့်ဖြစ်နေပါသည်။";
                return false;
            }

            // 2. Duplicate vowels or tone marks (e.g., ာာ, ိီ, းး)
            if (IllegalDuplicateMarksPattern.IsMatch(syllable))
            {
                errorReason = "သရ သို့မဟုတ် အသံထွက်သင်္ကေတ နှစ်ထပ်မှားယွင်းရိုက်နှိပ်ထားပါသည်။";
                return false;
            }

            // 3. Validate consonant stacking (ပါဌ်ဆင့်) against authentic Pali/Myanmar vagga rules
            if (syllable.IndexOf('\u1039') >= 0)
            {
                foreach (Match m in PatSintStackPattern.Matches(syllable))
                {
                    string pair = m.Groups[1].Value + m.Groups[2].Value;
                    if (!ValidPatSintPairs.Contains(pair))
                    {
                        errorReason = $"'{m.Groups[1].Value}္{m.Groups[2].Value}' သည် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် ပါဠိသဒ္ဒါ စည်းကမ်းဝင် ပါဌ်ဆင့်အတွဲ မဟုတ်ပါ။";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsSurroundedByMyanmarLetters(string text, int start, int length)
        {
            bool prevIsLetter = start > 0 && IsMyanmarLetterOrMark(text[start - 1]);
            int nextIdx = start + length;
            bool nextIsLetter = nextIdx < text.Length && IsMyanmarLetterOrMark(text[nextIdx]);
            return prevIsLetter || nextIsLetter;
        }

        private static bool IsMyanmarLetterOrMark(char c)
        {
            return (c >= '\u1000' && c <= '\u103F') || (c >= '\u104C' && c <= '\u104F');
        }
    }
}

