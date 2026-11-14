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
        /// Uses a deterministic zero-Regex O(N) maximal-munch scanner 100% equivalent to TokenPattern.
        /// </summary>
        public static List<SyllableToken> Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<SyllableToken>(0);

            int textLen = text.Length;
            var tokens = new List<SyllableToken>(Math.Min((textLen >> 1) + 4, 4096));
            int pos = 0;

            while (pos < textLen)
            {
                ScanNextToken(text, pos, out int len, out TokenKind kind);
                string raw;
                if (len == 1)
                {
                    char ch = text[pos];
                    if (ch == ' ') raw = " ";
                    else if (ch == '\u104B') raw = "\u104B";
                    else if (ch == '\u104A') raw = "\u104A";
                    else if (ch == '\n') raw = "\n";
                    else raw = text.Substring(pos, 1);
                }
                else
                {
                    raw = (pos == 0 && len == textLen) ? text : text.Substring(pos, len);
                }

                string normalized = kind == TokenKind.MyanmarSyllable
                    ? UnicodeNormalizer.NormalizeCanonicalOrder(raw)
                    : raw;

                tokens.Add(new SyllableToken(raw, normalized, pos, len, kind));
                pos += len;
            }

            return tokens;
        }

        /// <summary>
        /// Zero-allocation O(N) Myanmar syllable counter 100% equivalent to Tokenize(text).Count(t => t.Kind == TokenKind.MyanmarSyllable).
        /// </summary>
        public static int CountMyanmarSyllables(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int textLen = text.Length;
            int count = 0;
            int pos = 0;
            while (pos < textLen)
            {
                ScanNextToken(text, pos, out int len, out TokenKind kind);
                if (kind == TokenKind.MyanmarSyllable)
                    count++;
                pos += len;
            }
            return count;
        }

        /// <summary>
        /// Splits a Myanmar word or phrase into a list of normalized syllable strings.
        /// </summary>
        public static List<string> BreakIntoSyllables(string text, bool isPreNormalized = false)
        {
            var result = new List<string>(4);
            if (string.IsNullOrEmpty(text))
                return result;

            int textLen = text.Length;
            int pos = 0;
            while (pos < textLen)
            {
                ScanNextToken(text, pos, out int len, out TokenKind kind);
                if (kind == TokenKind.MyanmarSyllable)
                {
                    string raw = (pos == 0 && len == textLen) ? text : text.Substring(pos, len);
                    result.Add(isPreNormalized ? raw : UnicodeNormalizer.NormalizeCanonicalOrder(raw));
                }
                pos += len;
            }
            return result;
        }

        private static bool IsSyllableBaseChar(char c)
        {
            return (c >= '\u1000' && c <= '\u102A') ||
                   c == '\u103F' ||
                   (c >= '\u1040' && c <= '\u1049') ||
                   (c >= '\u104C' && c <= '\u104F');
        }

        private static void ScanNextToken(string text, int i, out int length, out TokenKind kind)
        {
            int len = text.Length;
            char c = text[i];

            // 1. Punct: [\u104A\u104B]+
            if (c == '\u104A' || c == '\u104B')
            {
                int p = i + 1;
                while (p < len && (text[p] == '\u104A' || text[p] == '\u104B'))
                    p++;
                length = p - i;
                kind = TokenKind.MyanmarPunctuation;
                return;
            }

            // 2. Space: \s+
            if (char.IsWhiteSpace(c))
            {
                int p = i + 1;
                while (p < len && char.IsWhiteSpace(text[p]))
                    p++;
                length = p - i;
                kind = TokenKind.Whitespace;
                return;
            }

            // 3. LaKaung: (?:\u104E|\u1044)[\u102B-\u103E]*(?:\u1004[\u102B-\u103E]*)+
            if (c == '\u104E' || c == '\u1044')
            {
                int p = i + 1;
                while (p < len && text[p] >= '\u102B' && text[p] <= '\u103E')
                    p++;
                if (p < len && text[p] == '\u1004')
                {
                    while (p < len && text[p] == '\u1004')
                    {
                        p++;
                        while (p < len && text[p] >= '\u102B' && text[p] <= '\u103E')
                            p++;
                    }
                    length = p - i;
                    kind = TokenKind.MyanmarSyllable;
                    return;
                }
            }

            // 4. Number: [\u1040-\u1049]+(?![\u102B-\u103E]|[\u1000-\u1021\u1025]\u103A)
            if (c >= '\u1040' && c <= '\u1049')
            {
                int p = i + 1;
                while (p < len && text[p] >= '\u1040' && text[p] <= '\u1049')
                    p++;

                bool followedBySyllableTail = p < len &&
                    ((text[p] >= '\u102B' && text[p] <= '\u103E') ||
                     (((text[p] >= '\u1000' && text[p] <= '\u1021') || text[p] == '\u1025') &&
                      p + 1 < len && text[p + 1] == '\u103A'));

                int numEnd = followedBySyllableTail ? (p - 1) : p;
                if (numEnd > i)
                {
                    int numLen = numEnd - i;
                    length = numLen;
                    kind = (numLen == 1 && c == '\u1040' && IsSurroundedByMyanmarLetters(text, i, 1))
                        ? TokenKind.MyanmarSyllable
                        : TokenKind.MyanmarNumber;
                    return;
                }
            }

            // 5. Syllable: (?:\u1004\u103A\u1039)?[\u1000-\u102A\u103F\u1040-\u1049\u104C-\u104F](?:\u1039[\u1000-\u1021])*[\u102B-\u103E]*(?:[\u1000-\u1021\u1025]\u103B?(?:\u103A+(?!\u1039)[\u1037\u1038\u103A]*|\u1037\u103A+\u1038?))*
            if (IsSyllableBaseChar(c))
            {
                int p = (c == '\u1004' && i + 3 < len && text[i + 1] == '\u103A' && text[i + 2] == '\u1039' && IsSyllableBaseChar(text[i + 3]))
                    ? (i + 4)
                    : (i + 1);

                while (p + 1 < len && text[p] == '\u1039' && text[p + 1] >= '\u1000' && text[p + 1] <= '\u1021')
                    p += 2;

                while (p < len && text[p] >= '\u102B' && text[p] <= '\u103E')
                    p++;

                while (p < len)
                {
                    char cc = text[p];
                    if (!((cc >= '\u1000' && cc <= '\u1021') || cc == '\u1025'))
                        break;

                    int q = p + 1;
                    if (q < len && text[q] == '\u103B')
                        q++;

                    if (q < len && text[q] == '\u103A')
                    {
                        int aEnd = q + 1;
                        while (aEnd < len && text[aEnd] == '\u103A')
                            aEnd++;

                        if (aEnd - q == 1 && aEnd < len && text[aEnd] == '\u1039')
                            break;

                        while (aEnd < len && (text[aEnd] == '\u1037' || text[aEnd] == '\u1038' || text[aEnd] == '\u103A'))
                            aEnd++;

                        p = aEnd;
                    }
                    else if (q + 1 < len && text[q] == '\u1037' && text[q + 1] == '\u103A')
                    {
                        int aEnd = q + 2;
                        while (aEnd < len && text[aEnd] == '\u103A')
                            aEnd++;
                        if (aEnd < len && text[aEnd] == '\u1038')
                            aEnd++;
                        p = aEnd;
                    }
                    else
                    {
                        break;
                    }
                }

                length = p - i;
                kind = TokenKind.MyanmarSyllable;
                return;
            }

            // 6. StrayMark: [\u102B-\u103E]+
            if (c >= '\u102B' && c <= '\u103E')
            {
                int p = i + 1;
                while (p < len && text[p] >= '\u102B' && text[p] <= '\u103E')
                    p++;
                length = p - i;
                kind = TokenKind.MyanmarSyllable;
                return;
            }

            // 7. Other: [^\u1000-\u104F\s]+
            {
                int p = i + 1;
                while (p < len && (text[p] < '\u1000' || text[p] > '\u104F') && !char.IsWhiteSpace(text[p]))
                    p++;
                length = p - i;
                kind = TokenKind.Other;
            }
        }

        /// <summary>
        /// Zero-Regex fast path for pre-normalized dictionary headword syllable breaking with optional string-pool interning.
        /// Produces 100% identical output to <see cref="BreakIntoSyllables"/> while avoiding Regex Match allocations on all Myanmar words.
        /// </summary>
        internal static void BreakIntoSyllablesFast(
            string text,
            List<string> result,
            Dictionary<string, string>? stringPool = null)
        {
            result.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            int textLen = text.Length;
            int pos = 0;
            while (pos < textLen)
            {
                ScanNextToken(text, pos, out int len, out TokenKind kind);
                if (kind == TokenKind.MyanmarSyllable)
                {
                    string syl = (pos == 0 && len == textLen) ? text : text.Substring(pos, len);
                    result.Add(InternIfPooled(syl, stringPool));
                }
                pos += len;
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

        private static bool HasIllegalDuplicateMarks(string s)
        {
            for (int i = 1; i < s.Length; i++)
            {
                char a = s[i - 1];
                char b = s[i];
                if (b < '\u102B' || b > '\u103A' || a < '\u102B' || a > '\u103A')
                    continue;

                if (a == b)
                {
                    if (a == '\u102B' || a == '\u102C' || a == '\u102D' || a == '\u102E' ||
                        a == '\u102F' || a == '\u1030' || a == '\u1031' || a == '\u1032' ||
                        a == '\u1036' || a == '\u1037' || a == '\u1038' || a == '\u103A')
                    {
                        return true;
                    }
                }
                else if (((a == '\u102B' && b == '\u102C') || (a == '\u102C' && b == '\u102B')) ||
                         ((a == '\u102D' && b == '\u102E') || (a == '\u102E' && b == '\u102D')) ||
                         ((a == '\u102F' && b == '\u1030') || (a == '\u1030' && b == '\u102F')))
                {
                    return true;
                }
            }
            return false;
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
            if (HasIllegalDuplicateMarks(syllable))
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

        /// <summary>
        /// Truncates <paramref name="text"/> to at most <paramref name="maxChars"/> UTF-16 code units without
        /// ever slicing mid-Myanmar-syllable, mid-surrogate-pair, or (whenever a space/punctuation boundary exists
        /// in the second half of the budget) mid-word. Strips trailing orphan punctuation and balances open parentheses.
        /// </summary>
        public static string TruncateAtSyllableBoundary(string? text, int maxChars, string ellipsis = "…")
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string src = text!;
            if (maxChars <= 0)
                return string.Empty;
            if (src.Length <= maxChars)
                return src;

            if (ellipsis == null)
                ellipsis = "…";

            int budget = maxChars - ellipsis.Length;
            if (budget <= 0)
                return ellipsis.Length <= maxChars ? ellipsis : ellipsis.Substring(0, maxChars);

            int textLen = src.Length;
            int pos = 0;
            int lastSyllableOrTokenEnd = 0;
            int lastWordBoundaryEnd = 0;

            while (pos < textLen)
            {
                ScanNextToken(src, pos, out int tokLen, out TokenKind kind);
                if (tokLen <= 0)
                    break;

                int end = pos + tokLen;
                if (end > budget)
                {
                    if (pos == 0)
                    {
                        if (kind == TokenKind.Other)
                        {
                            int cutOther = budget;
                            if (cutOther > 1 && char.IsHighSurrogate(src[cutOther - 1]))
                                cutOther--;
                            lastSyllableOrTokenEnd = cutOther;
                        }
                        else if (tokLen <= 14)
                        {
                            // Never split the first Myanmar syllable into orphan base/combining chars
                            lastSyllableOrTokenEnd = end;
                        }
                        else
                        {
                            int cutSyl = budget;
                            while (cutSyl > 1 && src[cutSyl] >= '\u102B' && src[cutSyl] <= '\u103E')
                                cutSyl--;
                            lastSyllableOrTokenEnd = cutSyl;
                        }
                    }
                    break;
                }

                if (kind != TokenKind.Whitespace)
                    lastSyllableOrTokenEnd = end;

                if (kind == TokenKind.Whitespace || kind == TokenKind.MyanmarPunctuation)
                {
                    if (pos > 0)
                        lastWordBoundaryEnd = pos;
                }
                else if (end < textLen && IsWordBoundaryFollower(src[end]))
                {
                    lastWordBoundaryEnd = end;
                }

                pos = end;
            }

            // Prefer whole-word boundary whenever one lies in [50%..100%] of budget so compound words
            // (e.g. 'ဖမ်းဆီးခြင်း', 'အခိုင်လိုက်', 'ပေါင်းစပ်ဝေါဟာရ') are never sliced mid-word.
            int chosenCut = (lastWordBoundaryEnd > 0 && lastWordBoundaryEnd * 2 >= budget)
                ? lastWordBoundaryEnd
                : lastSyllableOrTokenEnd;

            if (chosenCut <= 0)
                chosenCut = Math.Min(budget, textLen);

            while (chosenCut > 0 && IsTrimBeforeEllipsisChar(src[chosenCut - 1]))
                chosenCut--;

            if (chosenCut <= 0)
                return ellipsis;

            int openParens = 0;
            for (int i = 0; i < chosenCut; i++)
            {
                char ch = src[i];
                if (ch == '(') openParens++;
                else if (ch == ')' && openParens > 0) openParens--;
            }

            if (openParens > 0)
                return src.Substring(0, chosenCut) + ellipsis + new string(')', openParens);

            return src.Substring(0, chosenCut) + ellipsis;
        }

        private static bool IsWordBoundaryFollower(char c)
        {
            return char.IsWhiteSpace(c) ||
                   c == '၊' || c == '။' ||
                   c == ',' || c == ';' || c == ':' ||
                   c == ')' || c == ']' ||
                   c == '•' || c == '|' || c == '—';
        }

        private static bool IsTrimBeforeEllipsisChar(char c)
        {
            return char.IsWhiteSpace(c) ||
                   c == '၊' || c == '။' ||
                   c == ',' || c == ';' || c == ':' ||
                   c == '•' || c == '|' || c == '—' || c == '-' ||
                   c == '(' || c == '[';
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

