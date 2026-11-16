// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Typography
{
    public sealed class ZwspDiagnosticSummary
    {
        public int TotalZeroWidthCount { get; set; }
        public int BrokenSyllableCount { get; set; }
        public int MidWordOrOrphanParticleCount { get; set; }
        public int LeadingOrTrailingCount { get; set; }
        public int CleanBoundaryCount { get; set; }

        public int BadCount => BrokenSyllableCount + MidWordOrOrphanParticleCount + LeadingOrTrailingCount;
    }

    /// <summary>
    /// Host-smart Zero-Width Space (<c>U+200B</c> / <c>U+200C</c> / <c>U+200D</c> / <c>U+FEFF</c> / <c>U+2060</c>) diagnostic,
    /// full cleaner (for pure <c>Ctrl+F</c> search and Excel <c>VLOOKUP</c> / <c>PivotTable</c> integrity),
    /// and 96,266-word <see cref="MyanmarTrie"/>-guided Smart Word-Boundary Line-Wrap Injector for Word tables &amp; justified prose.
    /// </summary>
    public static class MyanmarZwspToolkit
    {
        private static readonly char[] InvisibleZwChars = { '\u200B', '\u200C', '\u200D', '\uFEFF', '\u2060' };

        private static readonly Regex ProtectedSpanRegex = new Regex(
            @"(?:https?://\S+|www\.\S+|\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b|[၀-၉0-9]{1,2}[ \t]*/[ \t]*[\u1000-\u1021\u1025]{2,4}[ \t]*[\(（][^\)\n\r]{1,8}[\)）][ \t]*[၀-၉0-9]{5,7})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ContiguousMyanmarRunRegex = new Regex(
            @"[\u1000-\u103F\u200B-\u200D\uFEFF\u2060]{2,}",
            RegexOptions.Compiled);

        // Bound postpositional particles, case markers, plural/aspect suffixes, and nominalizers that should NEVER
        // wrap onto a new line by themselves (must stay glued to the preceding noun/verb!)
        private static readonly HashSet<string> BoundTrailingParticles = new HashSet<string>(StringComparer.Ordinal)
        {
            "ကို", "အား", "သည်", "၏", "၌", "တွင်", "မှာ", "မှ", "က", "နှင့်", "နဲ့",
            "များ", "တို့", "တွေ", "ပါ", "ကြ", "ခဲ့", "ပြီ", "မည်", "မယ်", "တယ်",
            "မှု", "ခြင်း", "ချက်", "ရေး", "သော", "သည့်", "မည့်", "လျက်", "စွာ",
            "သာ", "လည်း", "ပင်", "တည်း", "ဘဲ", "လား", "လဲ", "နည်း", "တော့", "ဦး", "အောင်"
        };

        public static bool IsZeroWidthChar(char c) =>
            c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF' || c == '\u2060';

        public static bool ContainsAnyZeroWidthChar(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return text!.IndexOfAny(InvisibleZwChars) >= 0;
        }

        public static int CountZeroWidthChars(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            string s = text!;
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsZeroWidthChar(s[i]))
                    count++;
            }
            return count;
        }

        private static bool IsMyanmarCombiningOrDependentMark(char c) =>
            (c >= '\u102B' && c <= '\u103E') || (c >= '\u1056' && c <= '\u1059') || (c >= '\u105E' && c <= '\u1060');

        /// <summary>
        /// Determines whether a Myanmar token containing zero-width characters has a broken syllable (dotted circle)
        /// or a mid-word / orphan-particle split (breaking <c>Ctrl+F</c> search), versus a clean phrase/word boundary
        /// (<c>ဝန်ကြီးဌာန[ZWSP]ဆောင်ရွက်ချက်</c>, <c>မှတ်ပုံတင်ထားခြင်း[ZWSP]အတွက်</c>) used for line wrapping in Word.
        /// </summary>
        public static bool IsMidWordOrBrokenWordZwsp(string tokenWithZwsp, MyanmarTrie? trie, out bool isBrokenSyllable)
        {
            isBrokenSyllable = false;
            if (string.IsNullOrEmpty(tokenWithZwsp))
                return false;

            int len = tokenWithZwsp.Length;

            // 1. Leading or trailing ZWSP on a token is always stray
            if (IsZeroWidthChar(tokenWithZwsp[0]) || IsZeroWidthChar(tokenWithZwsp[len - 1]))
                return true;

            // 2. Broken syllable check: ZWSP right before a combining mark or right after virama (U+1039)
            for (int i = 0; i < len; i++)
            {
                if (!IsZeroWidthChar(tokenWithZwsp[i]))
                    continue;

                if (i > 0 && tokenWithZwsp[i - 1] == '\u1039')
                {
                    isBrokenSyllable = true;
                    return true;
                }

                int nextNonZw = i + 1;
                while (nextNonZw < len && IsZeroWidthChar(tokenWithZwsp[nextNonZw]))
                    nextNonZw++;

                if (nextNonZw >= len)
                    return true;

                if (IsMyanmarCombiningOrDependentMark(tokenWithZwsp[nextNonZw]))
                {
                    isBrokenSyllable = true;
                    return true;
                }
            }

            if (trie == null)
                return true;

            // 3. Split into clean segments separated by ZWSP and inspect every boundary
            var segments = SplitByZeroWidth(tokenWithZwsp);
            if (segments.Count <= 1)
                return true;

            var segSyllables = new List<List<string>>(segments.Count);
            for (int s = 0; s < segments.Count; s++)
            {
                string normSeg = UnicodeNormalizer.NormalizeCanonicalOrder(segments[s]);
                var syls = MyanmarSyllableBreaker.BreakIntoSyllables(normSeg);
                if (syls.Count == 0)
                    return true;
                segSyllables.Add(syls);
            }

            for (int b = 0; b < segSyllables.Count - 1; b++)
            {
                var leftSyls = segSyllables[b];
                var rightSyls = segSyllables[b + 1];

                // 3a. Never allow a ZWSP right before a bound grammatical particle / suffix (e.g. စာအုပ်[ZWSP]ကို, ငြိမ်းချမ်း[ZWSP]မှု)
                string firstRightSyl = rightSyls[0];
                if (BoundTrailingParticles.Contains(firstRightSyl))
                    return true;

                // Also check 2-syllable bound endings if right segment itself is just a bound suffix like ပါသည် / နိုင်သည်
                if (rightSyls.Count <= 2)
                {
                    string wholeRight = string.Concat(rightSyls);
                    if (wholeRight == "ပါသည်" || wholeRight == "ပါတယ်" || wholeRight == "ခဲ့သည်" || wholeRight == "ကြသည်")
                        return true;
                }

                // 3b. Check whether ANY lexical headword in MyanmarTrie spans across this ZWSP boundary!
                //     e.g. "ငြိမ်း[ZWSP]ချမ်းမှု" -> "ငြိမ်းချမ်း" is in Trie -> Mid-Word Split!
                //     e.g. "မှတ်ပုံ[ZWSP]တင်" -> "မှတ်ပုံတင်" is in Trie -> Mid-Word Split!
                //     e.g. "မှတ်ပုံတင်ထားခြင်း[ZWSP]အတွက်" -> no Trie headword crosses "ခြင်း|အ" -> Clean Phrase Boundary!
                int maxLeft = Math.Min(3, leftSyls.Count);
                int maxRight = Math.Min(3, rightSyls.Count);
                for (int lTake = 1; lTake <= maxLeft; lTake++)
                {
                    var sbLeft = new StringBuilder(16);
                    for (int k = leftSyls.Count - lTake; k < leftSyls.Count; k++)
                        sbLeft.Append(leftSyls[k]);
                    string leftTail = sbLeft.ToString();

                    var sbSpan = new StringBuilder(leftTail, 32);
                    for (int rTake = 1; rTake <= maxRight; rTake++)
                    {
                        sbSpan.Append(rightSyls[rTake - 1]);
                        if (trie.ContainsExactWord(sbSpan.ToString()))
                        {
                            return true;
                        }
                    }
                }

                // 3c. Single-syllable + single-syllable where either side isn't a standalone word
                if (leftSyls.Count == 1 && rightSyls.Count == 1)
                {
                    if (!trie.IsStandaloneWord(leftSyls[0]) || !trie.IsStandaloneWord(rightSyls[0]))
                        return true;
                }
            }

            return false;
        }

        private static List<string> SplitByZeroWidth(string s)
        {
            var list = new List<string>(4);
            int segStart = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsZeroWidthChar(s[i]))
                {
                    if (i > segStart)
                        list.Add(s.Substring(segStart, i - segStart));
                    while (i + 1 < s.Length && IsZeroWidthChar(s[i + 1]))
                        i++;
                    segStart = i + 1;
                }
            }
            if (segStart < s.Length)
                list.Add(s.Substring(segStart));
            return list;
        }

        /// <summary>
        /// Classifies every zero-width occurrence in <paramref name="text"/> into Broken Syllable, Mid-Word/Orphan,
        /// Leading/Trailing, or Clean Word-Boundary wrap hint.
        /// </summary>
        public static ZwspDiagnosticSummary AnalyzeZeroWidthUsage(string? text, MyanmarTrie? trie)
        {
            var summary = new ZwspDiagnosticSummary();
            if (string.IsNullOrEmpty(text) || !ContainsAnyZeroWidthChar(text))
                return summary;

            string s = text!;
            summary.TotalZeroWidthCount = CountZeroWidthChars(s);

            bool[] covered = new bool[s.Length];
            foreach (Match m in ContiguousMyanmarRunRegex.Matches(s))
            {
                string run = m.Value;
                if (!ContainsAnyZeroWidthChar(run))
                    continue;

                int runZwCount = CountZeroWidthChars(run);
                for (int k = m.Index; k < m.Index + m.Length && k < covered.Length; k++)
                    covered[k] = true;

                if (IsZeroWidthChar(run[0]) || IsZeroWidthChar(run[run.Length - 1]))
                {
                    summary.LeadingOrTrailingCount += runZwCount;
                }
                else if (IsMidWordOrBrokenWordZwsp(run, trie, out bool brokenSyl))
                {
                    if (brokenSyl)
                        summary.BrokenSyllableCount += runZwCount;
                    else
                        summary.MidWordOrOrphanParticleCount += runZwCount;
                }
                else
                {
                    summary.CleanBoundaryCount += runZwCount;
                }
            }

            for (int i = 0; i < s.Length; i++)
            {
                if (!covered[i] && IsZeroWidthChar(s[i]))
                {
                    summary.LeadingOrTrailingCount++;
                }
            }

            return summary;
        }

        /// <summary>
        /// Strips 100% of invisible zero-width characters (<c>U+200B</c>, <c>U+200C</c>, <c>U+200D</c>, <c>U+FEFF</c>, <c>U+2060</c>)
        /// across the entire string (leading, trailing, mid-word, and boundary) so Word <c>Ctrl+F</c> and Excel <c>VLOOKUP</c>
        /// match 100% reliably.
        /// </summary>
        public static string StripAllZeroWidthCharacters(string? input, out int removedCount)
        {
            removedCount = 0;
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            string s = input!;
            if (!ContainsAnyZeroWidthChar(s))
                return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (IsZeroWidthChar(c))
                {
                    removedCount++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return UnicodeNormalizer.NormalizeCanonicalOrder(sb.ToString());
        }

        /// <summary>
        /// Cleans any stray/mid-word zero-width characters first, then inserts clean <c>U+200B</c> (<c>ZWSP</c>)
        /// strictly at lexical word/phrase boundaries inside long Myanmar runs (&gt;= 4 syllables) using 96,266-word
        /// <see cref="MyanmarTrie"/> longest-match segmentation while keeping bound particles attached.
        /// Ideal for narrow Word table cells and Justified (<c>Ctrl+J</c>) official letters.
        /// </summary>
        public static string InsertSmartWordBoundaryZwsp(
            string? input,
            MyanmarTrie? trie,
            out int insertedCount,
            out int cleanedExistingCount)
        {
            insertedCount = 0;
            cleanedExistingCount = 0;
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            string stripped = StripAllZeroWidthCharacters(input, out cleanedExistingCount);
            if (trie == null || stripped.Length < 4)
                return stripped;

            // Protect URLs, emails, and NRC spans
            bool[] isProtected = new bool[stripped.Length];
            foreach (Match pm in ProtectedSpanRegex.Matches(stripped))
            {
                for (int k = pm.Index; k < pm.Index + pm.Length && k < isProtected.Length; k++)
                    isProtected[k] = true;
            }

            int added = 0;
            string result = ContiguousMyanmarRunRegex.Replace(stripped, m =>
            {
                for (int k = m.Index; k < m.Index + m.Length && k < isProtected.Length; k++)
                {
                    if (isProtected[k])
                        return m.Value;
                }

                var syls = MyanmarSyllableBreaker.BreakIntoSyllables(m.Value);
                if (syls.Count < 4)
                    return m.Value;

                // 1. Greedy longest-match lexical word segmentation (up to 5 syllables per word)
                var rawWords = new List<string>(syls.Count);
                int idx = 0;
                while (idx < syls.Count)
                {
                    int bestTake = 1;
                    int maxTake = Math.Min(5, syls.Count - idx);
                    for (int take = maxTake; take >= 2; take--)
                    {
                        var sbCand = new StringBuilder(20);
                        for (int t = 0; t < take; t++)
                            sbCand.Append(syls[idx + t]);
                        if (trie.ContainsExactWord(sbCand.ToString()))
                        {
                            bestTake = take;
                            break;
                        }
                    }

                    var sbWord = new StringBuilder(16);
                    for (int t = 0; t < bestTake; t++)
                        sbWord.Append(syls[idx + t]);
                    rawWords.Add(sbWord.ToString());
                    idx += bestTake;
                }

                if (rawWords.Count <= 1)
                    return m.Value;

                // 2. Glue bound grammatical particles / suffixes onto the preceding content word
                //    so particles like ကို / သည် / များ / မှု / ခြင်း never wrap onto a new line alone!
                var chunks = new List<string>(rawWords.Count);
                for (int w = 0; w < rawWords.Count; w++)
                {
                    string cur = rawWords[w];
                    if (chunks.Count > 0 && IsBoundSuffixChunk(cur))
                    {
                        chunks[chunks.Count - 1] = chunks[chunks.Count - 1] + cur;
                    }
                    else
                    {
                        chunks.Add(cur);
                    }
                }

                if (chunks.Count <= 1)
                    return m.Value;

                added += chunks.Count - 1;
                return string.Join("\u200B", chunks);
            });

            insertedCount = added;
            return result;
        }

        private static bool IsBoundSuffixChunk(string word)
        {
            if (BoundTrailingParticles.Contains(word))
                return true;

            return word == "ပါသည်" || word == "ပါတယ်" || word == "ခဲ့သည်" || word == "ကြသည်" ||
                   word == "နိုင်သည်" || word == "ရမည်" || word == "ဖြစ်သည်" || word == "လျက်ရှိသည်";
        }
    }
}
