// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Reconstructs garbled Myanmar text copied from legacy-exported Pyidaungsu / OpenType PDFs whose
    /// <c>ToUnicode</c> CMap tables are corrupted (e.g. statutory &amp; ministry PDFs where visarga <c>း</c> extracts as <c>ေး</c>,
    /// pre-base <c>ေ</c> extracts before the consonant as <c>ရ</c>, <c>ဒ</c> extracts as <c>ေ</c>, medial <c>ြ</c> extracts as <c>ပ</c>/<c>က</c>,
    /// <c>မ</c> extracts as <c>မ္</c>, Pali stacks lose virama <c>္</c>, and unmapped glyphs <c>ထ/ရ/ျ/ွ/ဲ/ူ/့</c> collapse into spaces).
    /// Uses a conservative two-stage CMap-inverse + 96K <see cref="MyanmarTrie"/> &amp; 110K Bigram Viterbi decoder so
    /// numbers, English tokens, punctuation, and unknown names are never corrupted.
    /// </summary>
    public static class BrokenPdfCMapReconstructor
    {
        private const char LogicalESentinel = '\uFFFE';

        // 1. Exact OpenType CMap multi-codepoint ligature inversions
        private static readonly Regex CMapMraHaPattern = new Regex(@"\u1015\u1019\u1039\u103C\u103E", RegexOptions.Compiled);
        private static readonly Regex CMapFwunAukmyitPattern = new Regex(@"(?<=[\u1000-\u1021]) ?\u1036\u103D\u1032\u1037", RegexOptions.Compiled);
        private static readonly Regex CMapShounVisargaPattern = new Regex(@"\u101B\u1036\u103E\u102F(?:\u1031\u1038|\u1038)", RegexOptions.Compiled);
        private static readonly Regex CMapShounBarePattern = new Regex(@"\u101B\u1036\u103E\u102F", RegexOptions.Compiled);
        private static readonly Regex CMapDoubledOiAukmyitPattern = new Regex(@"\u102D\u102F\u102D\u102F\u1037", RegexOptions.Compiled);
        private static readonly Regex CMapShiftedAukmyitBeforeAsatPattern = new Regex(
            @"(?<=[\u1000-\u1021][\u103B-\u103E]*)\u102D\u102F\u1037\u103A",
            RegexOptions.Compiled);
        private static readonly Regex CMapVisualEAukmyitPattern = new Regex(
            @"\u101B(?<C>[\u1000-\u1021][\u103B-\u103E]*)\u102D\u102F\u1037",
            RegexOptions.Compiled);
        private static readonly Regex CMapSpaceBeforeAsatPattern = new Regex(
            @"(?<=[\u1000-\u1039\u103B-\u103E]) \u103A",
            RegexOptions.Compiled);
        private static readonly Regex CMapVisualEConsEVisargaPattern = new Regex(
            @"\u101B(?<C>[\u1000-\u1021][\u103B-\u103E]*)\u1031\u1038",
            RegexOptions.Compiled);
        private static readonly Regex CMapVisualEConsAwAsatPattern = new Regex(
            @"\u101B(?<C>[\u1000-\u1021][\u103B-\u103E]*)\u1031(?<Tail>\u102B\u103A|\u102C\u103A)",
            RegexOptions.Compiled);
        private static readonly Regex CMapIllegalEVisargaAfterMarkPattern = new Regex(
            @"(?<=[\u1023-\u1032\u1036\u1037\u103A ])\u1031\u1038",
            RegexOptions.Compiled);
        private static readonly Regex CMapSpuriousMaViramaPattern = new Regex(
            @"\u1019\u1039(?![\u1015-\u1019](?![\u1039\u103E]))",
            RegexOptions.Compiled);
        private static readonly Regex CMapSpuriousGaViramaPattern = new Regex(
            @"\u1002\u1039(?![\u1002\u1003])",
            RegexOptions.Compiled);

        private static readonly HashSet<string> CommonGrammarWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "သည်", "သည့်", "သော", "မည်", "မည့်", "၏", "ကို", "အား", "မှ", "က", "တွင်", "၌",
            "နှင့်", "ဖြင့်", "၍", "ကြောင့်", "သဖြင့်", "လျှင်", "သော်လည်း", "သို့", "တို့", "များ",
            "ပါ", "ရန်", "ရေး", "ရာ", "ရှိ", "မရှိ", "ဖြစ်", "ပြု", "ရ", "စေ", "ထား", "ပေး",
            "အရ", "အတွက်", "အပေါ်", "အောက်", "အတွင်း", "အပြင်", "အဖြစ်", "အတိုင်း", "အညီ", "နှင့်အညီ",
            "သို့မဟုတ်", "ထို့ပြင်", "သို့ရာတွင်", "ဆိုသည်မှာ", "ဆိုသည်", "ဖြစ်သည်", "ပါဝင်သည်", "ရမည်", "စေရမည်",
            "ပုဒ်မခွဲ", "ကျောင်းခွဲ", "တန်းခွဲ", "ရုံးခွဲ", "ဌာနခွဲ", "မရှိဘဲ", "မပါဘဲ", "မဟုတ်ဘဲ", "မပြုဘဲ",
            "ချိတ်ဆွဲ", "စောင့်သိ", "ခွင့်ရှိ", "ခွင့်ရှိသည်", "ခွင့်ပြု", "လေ့ကျင့်ပေး"
        };

        private readonly struct SylAtom
        {
            public readonly int StartCharIndex;
            public readonly int NextCharIndex;
            public readonly string CleanSyllable;
            public readonly bool HasLeadingSpace;
            public readonly double TransformPenalty;
            public readonly bool RequiresLexiconSupport;
            public readonly bool RequiresMultiSyllableCompound;

            public SylAtom(
                int startCharIndex,
                int nextCharIndex,
                string cleanSyllable,
                bool hasLeadingSpace,
                double transformPenalty,
                bool requiresLexiconSupport,
                bool requiresMultiSyllableCompound = false)
            {
                StartCharIndex = startCharIndex;
                NextCharIndex = nextCharIndex;
                CleanSyllable = cleanSyllable;
                HasLeadingSpace = hasLeadingSpace;
                TransformPenalty = transformPenalty;
                RequiresLexiconSupport = requiresLexiconSupport;
                RequiresMultiSyllableCompound = requiresMultiSyllableCompound;
            }
        }

        private readonly struct WordTransition
        {
            public readonly int NextCharIndex;
            public readonly string WordText;
            public readonly bool HasLeadingSpace;
            public readonly double Score;

            public WordTransition(int nextCharIndex, string wordText, bool hasLeadingSpace, double score)
            {
                NextCharIndex = nextCharIndex;
                WordText = wordText;
                HasLeadingSpace = hasLeadingSpace;
                Score = score;
            }
        }

        /// <summary>
        /// Reconstructs broken Pyidaungsu / OpenType PDF CMap text across a single paragraph or multi-line string.
        /// Clean Unicode lines are returned untouched in O(N) time with zero allocations.
        /// </summary>
        public static string Reconstruct(string text, MyanmarTrie? trie, out int repairedLineCount)
        {
            repairedLineCount = 0;
            if (string.IsNullOrEmpty(text) || text.Length < 3)
                return text ?? string.Empty;

            // Single-line fast path (common in Word ParagraphCheck and single-line Excel cells)
            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
            {
                if (!UnicodeNormalizer.IsBrokenPdfFontCMapText(text))
                    return text;

                string rebuilt = ReconstructBrokenLine(text, trie);
                if (!string.Equals(rebuilt, text, StringComparison.Ordinal))
                    repairedLineCount = 1;
                return rebuilt;
            }

            // Multi-line text (selections, multi-line cells, clipboard pastes): evaluate non-Zawgyi lines independently
            // while still repairing short hard-wrapped PDF continuation lines when a sibling line is confirmed Broken-PDF!
            var lines = new List<(string Line, string Delim, bool IsZawgyi, bool IsBrokenPdf)>();
            int i = 0;
            int n = text.Length;
            bool anyLineConfirmedBrokenPdf = false;

            while (i < n)
            {
                int lineStart = i;
                while (i < n && text[i] != '\r' && text[i] != '\n')
                    i++;
                string line = text.Substring(lineStart, i - lineStart);

                int delimStart = i;
                while (i < n && (text[i] == '\r' || text[i] == '\n'))
                    i++;
                string delim = text.Substring(delimStart, i - delimStart);

                bool isZg = line.Length >= 2 && UnicodeNormalizer.IsZawgyiText(line);
                bool isPdf = !isZg && line.Length >= 3 && UnicodeNormalizer.IsBrokenPdfFontCMapText(line);
                if (isPdf)
                    anyLineConfirmedBrokenPdf = true;

                lines.Add((line, delim, isZg, isPdf));
            }

            if (!anyLineConfirmedBrokenPdf)
                return text;

            var sb = new StringBuilder(text.Length + 16);
            bool anyChanged = false;

            for (int idx = 0; idx < lines.Count; idx++)
            {
                var item = lines[idx];
                if (!item.IsZawgyi && item.Line.Length >= 3 &&
                    (item.IsBrokenPdf || HasBrokenLineFragmentSignal(item.Line)))
                {
                    string rebuiltLine = ReconstructBrokenLine(item.Line, trie);
                    if (!string.Equals(rebuiltLine, item.Line, StringComparison.Ordinal))
                    {
                        repairedLineCount++;
                        anyChanged = true;
                    }
                    sb.Append(rebuiltLine);
                }
                else
                {
                    sb.Append(item.Line);
                }

                sb.Append(item.Delim);
            }

            return anyChanged ? sb.ToString() : text;
        }

        /// <summary>
        /// Reconstructs a plain-text sub-span coming from a paragraph already confirmed as Broken-PDF CMap text
        /// (e.g. a segment separated by a Word tab, footnote/endnote reference mark, or soft line break).
        /// </summary>
        public static string ReconstructConfirmedSpan(string spanText, MyanmarTrie? trie, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(spanText) || spanText.Length < 3 || UnicodeNormalizer.IsZawgyiText(spanText))
                return spanText ?? string.Empty;

            if (!UnicodeNormalizer.IsBrokenPdfFontCMapText(spanText) && !HasBrokenLineFragmentSignal(spanText))
                return spanText;

            string rebuilt = ReconstructBrokenLine(spanText, trie);
            changed = !string.Equals(rebuilt, spanText, StringComparison.Ordinal);
            return changed ? rebuilt : spanText;
        }

        /// <summary>
        /// Smart clipboard / pasted-text cleaner: repairs Broken-PDF CMap mojibake, Zawgyi-One spans, and stray mid-word ZWSP
        /// in a single pass so clipboard pastes insert clean Pyidaungsu Unicode.
        /// </summary>
        public static string RepairClipboardOrPastedText(string rawClipboardText, MyanmarTrie? trie, out int changedLinesOrSpans)
        {
            changedLinesOrSpans = 0;
            if (string.IsNullOrEmpty(rawClipboardText))
                return string.Empty;

            string working = rawClipboardText;

            // 1. Repair Broken-PDF CMap lines first (before Zawgyi detector could misread broken CMap clusters)
            string afterPdf = Reconstruct(working, trie, out int pdfRepaired);
            if (pdfRepaired > 0)
            {
                working = afterPdf;
                changedLinesOrSpans += pdfRepaired;
            }

            // 2. Convert any Zawgyi-One spans safely without touching clean Unicode or English
            var zawgyiSpans = UnicodeNormalizer.FindZawgyiSpans(working);
            if (zawgyiSpans.Count > 0)
            {
                var zb = new StringBuilder(working.Length);
                int cursor = 0;
                for (int z = 0; z < zawgyiSpans.Count; z++)
                {
                    var sp = zawgyiSpans[z];
                    if (sp.StartIndex > cursor)
                        zb.Append(working, cursor, sp.StartIndex - cursor);
                    zb.Append(sp.ConvertedUnicode);
                    cursor = sp.StartIndex + sp.Length;
                    changedLinesOrSpans++;
                }
                if (cursor < working.Length)
                    zb.Append(working, cursor, working.Length - cursor);
                working = zb.ToString();
            }

            return working;
        }

        private static bool HasBrokenLineFragmentSignal(string line)
        {
            for (int i = 0; i < line.Length - 1; i++)
            {
                char c = line[i];
                char nx = line[i + 1];
                if (c == '\u1031' && nx == '\u1038')
                    return true;
                if (c == '\u1019' && nx == '\u1039' && (i + 2 >= line.Length || line[i + 2] < '\u1015' || line[i + 2] > '\u1019'))
                    return true;
                if (c == ' ' && nx >= '\u102B' && nx <= '\u103E')
                    return true;
                if (c == '\u101B' && nx >= '\u1000' && nx <= '\u1021')
                    return true;
            }
            return false;
        }

        private static string ReconstructBrokenLine(string line, MyanmarTrie? trie)
        {
            bool visualECMap = DetectVisualECMapMode(line);
            string pre = NormalizeDeterministicCMapLayer(line, visualECMap);

            // Partition into [Myanmar + single-internal-space] runs vs [ASCII/Digits/Punctuation/Multi-space] runs
            var outSb = new StringBuilder(pre.Length + 16);
            int i = 0;
            int n = pre.Length;

            while (i < n)
            {
                if (!IsMyanmarCodepoint(pre[i]))
                {
                    // Check for leading dropped initial consonant (' ' + orphan combining mark OR bare killed coda like '၉ က် -> ၉ ရက်', 'သည်။ ို့ပပင် -> သည်။ ထို့ပြင်')
                    if (pre[i] == ' ' && i + 1 < n &&
                        (IsOrphanCombiningStart(pre[i + 1]) || StartsWithBareKilledCodaInLine(pre, i + 1)))
                    {
                        bool afterDigit = i > 0 && IsMyanmarOrAsciiDigit(pre[i - 1]);
                        outSb.Append(' ');
                        i++;
                        int chunkStart = i;
                        i = ScanEndOfMyanmarChunk(pre, chunkStart);
                        string orphanChunk = " " + pre.Substring(chunkStart, i - chunkStart);
                        string decodedOrphan = DecodeMyanmarChunkViterbi(orphanChunk, trie, afterDigit).TrimStart(' ');
                        outSb.Append(decodedOrphan);
                        continue;
                    }

                    outSb.Append(pre[i]);
                    i++;
                    continue;
                }

                int start = i;
                i = ScanEndOfMyanmarChunk(pre, start);
                string chunk = pre.Substring(start, i - start);
                string decoded = DecodeMyanmarChunkViterbi(chunk, trie, startsAfterDigit: false);
                outSb.Append(decoded);
            }

            return UnicodeNormalizer.FixLookalikeCharacters(UnicodeNormalizer.NormalizeCanonicalOrder(outSb.ToString()));
        }

        private static int ScanEndOfMyanmarChunk(string s, int start)
        {
            int i = start;
            int n = s.Length;
            while (i < n)
            {
                if (IsMyanmarCodepoint(s[i]))
                {
                    i++;
                    continue;
                }

                // Include a single ASCII space inside the chunk so Viterbi can decide between real word space vs dropped PDF glyph (ထ/ရ/ျ/ွ/ဲ/ူ)
                if (s[i] == ' ' && i + 1 < n && IsMyanmarCodepoint(s[i + 1]))
                {
                    i++;
                    continue;
                }

                // Trailing dropped 'ရ' right before punctuation or end-of-line (e.g. 'အစိုး ၊' / 'ဥပဒေအ ')
                break;
            }
            return i;
        }

        private static bool DetectVisualECMapMode(string text)
        {
            // In broken Pyidaungsu CMap, pre-base 'ေ' extracts before the consonant as 'ရ' while '\u1031' (outside 'ေး'/'ေါ်') represents 'ဒ'
            if (text.IndexOf("\u1031\u1038", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("\u1031\u103A", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("\u1025\u1015\u101B\u1031", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            for (int i = 0; i < text.Length - 2; i++)
            {
                if (text[i] == '\u101B' && IsMyanmarConsonant(text[i + 1]) &&
                    (text[i + 2] == '\u102B' || text[i + 2] == '\u102C' || text[i + 2] == ' '))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeDeterministicCMapLayer(string s, bool visualECMap)
        {
            s = CMapMraHaPattern.Replace(s, "\u1019\u103C\u103E");
            s = CMapSpuriousMaViramaPattern.Replace(s, "\u1019");
            s = CMapSpuriousGaViramaPattern.Replace(s, "\u1002");
            s = CMapFwunAukmyitPattern.Replace(s, "\u103D\u1036\u1037");
            s = CMapShounVisargaPattern.Replace(s, "\u101B\u103E\u102F\u1036\u1038");
            s = CMapShounBarePattern.Replace(s, "\u101B\u103E\u102F\u1036");
            s = CMapDoubledOiAukmyitPattern.Replace(s, "\u102D\u102F\u1037");
            s = CMapShiftedAukmyitBeforeAsatPattern.Replace(s, "\u103A\u1037");
            s = CMapVisualEAukmyitPattern.Replace(s, "${C}" + LogicalESentinel + "\u1037");
            s = CMapSpaceBeforeAsatPattern.Replace(s, "\u103A\u1037");

            // Protect 'ရ+Cons+ေး' -> 'Cons+ေး' and 'ရ+Cons+ေါ်' -> 'Cons+ေါ်' with LogicalESentinel before mapping raw '\u1031' -> 'ဒ'
            s = CMapVisualEConsEVisargaPattern.Replace(s, "${C}" + LogicalESentinel + "\u1038");
            s = CMapVisualEConsAwAsatPattern.Replace(s, "${C}" + LogicalESentinel + "${Tail}");
            s = CMapIllegalEVisargaAfterMarkPattern.Replace(s, "\u1038");

            if (visualECMap && s.IndexOf('\u1031') >= 0)
            {
                // Any remaining raw '\u1031' not immediately following a consonant+medial (or following visual 'ရ' as in 'ဥပရေ'/'ရေသ') is CMap 'ဒ' (\u1012)
                var db = new StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '\u1031')
                    {
                        bool prevIsNonRaConsOrMedial =
                            i > 0 &&
                            ((IsMyanmarConsonant(s[i - 1]) && s[i - 1] != '\u101B') ||
                             (s[i - 1] >= '\u103B' && s[i - 1] <= '\u103E'));

                        // Even after 'လှ ' ('လှ ေါန်း' -> 'လှူဒါန်း'), i-1 is ' ' so prevIsNonRaConsOrMedial is false -> maps to 'ဒ'!
                        db.Append(prevIsNonRaConsOrMedial ? '\u1031' : '\u1012');
                    }
                    else
                    {
                        db.Append(s[i]);
                    }
                }
                s = db.ToString();
            }

            if (s.IndexOf(LogicalESentinel) >= 0)
                s = s.Replace(LogicalESentinel, '\u1031');

            // Reorder long-stem '\u102D\u103B\u102F' ('ိျု' as in 'အမျိုး'/'အကျိုး'/'ကြို') into canonical order markers handled by atom builder
            s = s.Replace("\u1036\u102F", "\u102F\u1036");
            return s;
        }

        private static string DecodeMyanmarChunkViterbi(string chunk, MyanmarTrie? trie, bool startsAfterDigit)
        {
            int n = chunk.Length;
            if (n == 0)
                return chunk;

            // Step A: Build candidate syllable atoms at every character offset 0..n-1
            var atomsAt = new List<SylAtom>?[n];
            for (int pos = 0; pos < n; pos++)
            {
                var list = GenerateSyllableAtomsAt(chunk, pos);
                if (list.Count > 0)
                    atomsAt[pos] = list;
            }

            // Step B: Noisy-channel Viterbi segmentation (every word transition has strictly negative log-probability < 0
            // so 1 real compound word ALWAYS beats splitting into 2..4 single-letter dictionary headwords!)
            var bestScore = new double[n + 1];
            var prevPos = new int[n + 1];
            var prevWord = new string?[n + 1];
            var prevSpace = new bool[n + 1];

            for (int i = 1; i <= n; i++)
            {
                bestScore[i] = double.NegativeInfinity;
                prevPos[i] = -1;
            }
            bestScore[0] = 0.0;

            var transitionsBuffer = new List<WordTransition>(32);
            var sylPath = new SylAtom[5];

            for (int pos = 0; pos < n; pos++)
            {
                double baseScore = bestScore[pos];
                if (double.IsNegativeInfinity(baseScore))
                    continue;

                transitionsBuffer.Clear();
                CollectWordTransitions(chunk, pos, atomsAt, trie, sylPath, 0, pos, 0.0, false, transitionsBuffer);

                // Fallback 1-char step if an unexpected broken cluster had zero transitions
                if (transitionsBuffer.Count == 0)
                {
                    bool sp = chunk[pos] == ' ';
                    if (sp && pos + 1 < n)
                    {
                        if (baseScore - 1.5 > bestScore[pos + 1])
                        {
                            bestScore[pos + 1] = baseScore - 1.5;
                            prevPos[pos + 1] = pos;
                            prevWord[pos + 1] = string.Empty;
                            prevSpace[pos + 1] = true;
                        }
                    }
                    else
                    {
                        string rawCh = chunk[pos].ToString();
                        if (baseScore - 28.0 > bestScore[pos + 1])
                        {
                            bestScore[pos + 1] = baseScore - 28.0;
                            prevPos[pos + 1] = pos;
                            prevWord[pos + 1] = rawCh;
                            prevSpace[pos + 1] = false;
                        }
                    }
                    continue;
                }

                string? leftWord = prevWord[pos];
                for (int t = 0; t < transitionsBuffer.Count; t++)
                {
                    WordTransition tr = transitionsBuffer[t];
                    double wordStepLogP = tr.Score;

                    // Calendar date prior: right after Myanmar/ASCII digits ('၉ က်', '၁၂ က်'), 'ရက်' is overwhelmingly preferred over 'ထက်'
                    if (pos == 0 && startsAfterDigit && tr.WordText == "\u101B\u1000\u103A")
                    {
                        wordStepLogP += 2.6;
                    }

                    // Bigram conditioning log P(w | leftWord), strictly clamped below -2.2 so extra word splits never gain net positive score
                    if (trie != null && !string.IsNullOrEmpty(leftWord) && !string.IsNullOrEmpty(tr.WordText) &&
                        !IsNonParticleBareSingleConsonant(leftWord!) && !IsNonParticleBareSingleConsonant(tr.WordText))
                    {
                        int bg = trie.GetBigramCount(leftWord!, tr.WordText);
                        if (bg > 0)
                        {
                            double lift = Math.Min(3.1, 0.75 + Math.Log(bg + 1.0) * 0.42);
                            wordStepLogP = Math.Min(-2.2, wordStepLogP + lift);
                        }
                    }

                    double spaceKeepCost = (chunk[pos] == ' ' && tr.HasLeadingSpace) ? 0.25 : 0.0;
                    double candScore = baseScore + wordStepLogP - spaceKeepCost;
                    if (candScore > bestScore[tr.NextCharIndex])
                    {
                        bestScore[tr.NextCharIndex] = candScore;
                        prevPos[tr.NextCharIndex] = pos;
                        prevWord[tr.NextCharIndex] = tr.WordText;
                        prevSpace[tr.NextCharIndex] = tr.HasLeadingSpace;
                    }
                }
            }

            if (double.IsNegativeInfinity(bestScore[n]))
                return chunk;

            var stack = new List<(string Word, bool Space)>(16);
            int cur = n;
            while (cur > 0)
            {
                int p = prevPos[cur];
                if (p < 0)
                    return chunk;
                stack.Add((prevWord[cur] ?? string.Empty, prevSpace[cur]));
                cur = p;
            }

            var res = new StringBuilder(chunk.Length + 8);
            for (int idx = stack.Count - 1; idx >= 0; idx--)
            {
                var item = stack[idx];
                if (item.Space && res.Length > 0 && res[res.Length - 1] != ' ')
                    res.Append(' ');
                res.Append(item.Word);
            }
            return res.ToString();
        }

        private static void CollectWordTransitions(
            string chunk,
            int rootStartPos,
            List<SylAtom>?[] atomsAt,
            MyanmarTrie? trie,
            SylAtom[] sylPath,
            int depth,
            int curPos,
            double accPenalty,
            bool anyRequiresLexicon,
            List<WordTransition> output)
        {
            if (curPos >= chunk.Length || depth >= 5)
                return;

            var atoms = atomsAt[curPos];
            if (atoms == null)
                return;

            for (int i = 0; i < atoms.Count; i++)
            {
                SylAtom atom = atoms[i];
                // Only the first syllable of a multi-syllable word may carry a leading word-boundary space!
                if (depth > 0 && atom.HasLeadingSpace)
                    continue;

                sylPath[depth] = atom;
                int newDepth = depth + 1;
                double newPenalty = accPenalty + atom.TransformPenalty;
                bool newReqLex = anyRequiresLexicon || atom.RequiresLexiconSupport;
                bool reqMulti = atom.RequiresMultiSyllableCompound;
                for (int d = 0; d < depth && !reqMulti; d++)
                {
                    if (sylPath[d].RequiresMultiSyllableCompound)
                        reqMulti = true;
                }

                string candidateWord = JoinSyllables(sylPath, newDepth);
                bool leadSpace = sylPath[0].HasLeadingSpace;
                int consumedChars = Math.Max(1, atom.NextCharIndex - (leadSpace && chunk[rootStartPos] == ' ' ? rootStartPos + 1 : rootStartPos));

                if (!reqMulti || newDepth >= 2)
                {
                    EvaluateAndAddCandidateWord(candidateWord, consumedChars, atom.NextCharIndex, leadSpace, newDepth, newPenalty, newReqLex, trie, output);
                }

                // Also evaluate 1-syllable nasal/glide-asat aukmyit recovery inside compounds (e.g. Variant-B 'ရစာင်သိ' -> 'စောင့်သိ')
                if (newDepth == 2 && trie != null && EndsWithPlainNasalOrGlideAsat(sylPath[0].CleanSyllable))
                {
                    string withAukmyit = UnicodeNormalizer.NormalizeCanonicalOrder(sylPath[0].CleanSyllable + "\u1037") + sylPath[1].CleanSyllable;
                    if (trie.ContainsExactWord(withAukmyit) || CommonGrammarWords.Contains(withAukmyit))
                    {
                        int freq = trie.GetWordFrequency(withAukmyit);
                        double eff = freq > 0 ? Math.Max(freq, 120.0) : 120.0;
                        double score = Math.Log((eff + 0.5) / 2500000.0) - newPenalty - 0.35;
                        output.Add(new WordTransition(atom.NextCharIndex, withAukmyit, leadSpace, score));
                    }
                }

                // Recurse to longer compounds (up to 5 syllables)
                if (newDepth < 5 && atom.NextCharIndex < chunk.Length)
                {
                    CollectWordTransitions(
                        chunk,
                        rootStartPos,
                        atomsAt,
                        trie,
                        sylPath,
                        newDepth,
                        atom.NextCharIndex,
                        newPenalty,
                        newReqLex,
                        output);
                }
            }
        }

        private static void EvaluateAndAddCandidateWord(
            string word,
            int consumedSourceChars,
            int nextCharIndex,
            bool leadSpace,
            int sylCount,
            double penalty,
            bool requiresLexiconSupport,
            MyanmarTrie? trie,
            List<WordTransition> output)
        {
            if (string.IsNullOrEmpty(word))
                return;

            bool inCommonGrammar = CommonGrammarWords.Contains(word);
            bool inTrie = trie != null && trie.ContainsExactWord(word);

            if (inTrie || inCommonGrammar)
            {
                // Single bare consonant alphabet headwords ('ပ', 'ဒ', 'သ', 'စ', 'ဖ', etc.) are almost never standalone words in prose
                if (IsNonParticleBareSingleConsonant(word))
                {
                    double bareConsScore = (word[0] == '\u1019' || word[0] == '\u1021') ? -11.8 : -16.2;
                    output.Add(new WordTransition(nextCharIndex, word, leadSpace, bareConsScore - penalty));
                    return;
                }

                int freq = trie != null ? trie.GetWordFrequency(word) : 0;
                double effectiveFreq;
                if (freq > 0)
                {
                    effectiveFreq = freq;
                    if (sylCount >= 3 && effectiveFreq < 30.0)
                        effectiveFreq = 30.0;
                    else if (sylCount == 2 && effectiveFreq < 10.0)
                        effectiveFreq = 10.0;
                }
                else if (inCommonGrammar)
                {
                    effectiveFreq = 1200.0;
                }
                else
                {
                    effectiveFreq = sylCount >= 3 ? 35.0 : (sylCount == 2 ? 12.0 : 3.0);
                }

                // Pure negative log-probability log P(w) < 0 minus CMap channel distortion penalty
                double logUnigram = Math.Log((effectiveFreq + 0.5) / 2500000.0);
                double score = logUnigram - penalty;
                output.Add(new WordTransition(nextCharIndex, word, leadSpace, score));
                return;
            }

            // Conservative safety gate: hypotheses that invent dropped consonants/vowels across spaces or Pali stacks
            // are NEVER emitted unless verified by the 96K MyanmarTrie or CommonGrammarWords!
            if (requiresLexiconSupport || sylCount > 1)
                return;

            // Single-syllable fallback for unknown proper names (e.g. rare personal/village names) using safe CMap inversions only
            bool validSyl = trie == null ? IsWellFormedMyanmarSyllable(word) : trie.IsValidSingleSyllable(word);
            double fallbackScore = (validSyl ? -18.5 : -29.5) - penalty;
            output.Add(new WordTransition(nextCharIndex, word, leadSpace, fallbackScore));
        }

        private static bool IsNonParticleBareSingleConsonant(string word)
        {
            if (word.Length != 1)
                return false;
            char c = word[0];
            if (!IsMyanmarConsonant(c))
                return false;
            // Only 'က' (subject marker) and 'ရ' (auxiliary verb) regularly stand alone as 1-codepoint words in formal Burmese
            return c != '\u1000' && c != '\u101B';
        }

        private static bool EndsWithPlainNasalOrGlideAsat(string syl)
        {
            if (syl.Length < 2 || syl[syl.Length - 1] != '\u103A')
                return false;
            char c = syl[syl.Length - 2];
            return c == '\u1004' || c == '\u1014' || c == '\u1019' || c == '\u100A' || c == '\u101A';
        }

        private static string JoinSyllables(SylAtom[] path, int count)
        {
            if (count == 1)
                return path[0].CleanSyllable;
            var sb = new StringBuilder(count * 5);
            for (int i = 0; i < count; i++)
                sb.Append(path[i].CleanSyllable);
            return sb.ToString();
        }

        private static List<SylAtom> GenerateSyllableAtomsAt(string s, int pos)
        {
            var results = new List<SylAtom>(10);
            int n = s.Length;
            if (pos >= n)
                return results;

            // Case 1: Position starts at a space ' '
            if (s[pos] == ' ')
            {
                if (pos + 1 >= n)
                    return results;

                // 1a. Real word-boundary space followed by normal syllable(s) at pos + 1
                var afterSpace = GenerateSyllableAtomsNoLeadSpace(s, pos + 1);
                for (int i = 0; i < afterSpace.Count; i++)
                {
                    var a = afterSpace[i];
                    results.Add(new SylAtom(pos, a.NextCharIndex, a.CleanSyllable, hasLeadingSpace: true, a.TransformPenalty, a.RequiresLexiconSupport, a.RequiresMultiSyllableCompound));
                }

                // 1b. Space is a dropped initial consonant: 'ရ' (\u101B), 'ထ' (\u1011), or 'ထွ' (\u1011\u103D)
                AddDroppedInitialConsonantHypotheses(s, pos, pos + 1, results);

                // 1c. Bare open 'ရ' dropped into ' ' immediately before auxiliary 'မည်'/'မည့်' or inside multi-syllable compounds ('ရစ မည် -> စေရမည်', 'ဆောင်ရွက် မည် -> ဆောင်ရွက်ရမည်')
                if (pos > 0 && IsMyanmarConsonant(s[pos + 1]))
                {
                    results.Add(new SylAtom(pos, pos + 1, "\u101B", hasLeadingSpace: false, transformPenalty: 1.25, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                }
                return results;
            }

            return GenerateSyllableAtomsNoLeadSpace(s, pos);
        }

        private static void AddDroppedInitialConsonantHypotheses(string s, int spacePos, int afterSpacePos, List<SylAtom> results)
        {
            if (afterSpacePos >= s.Length)
                return;

            char first = s[afterSpacePos];
            bool isOrphanMark = IsOrphanCombiningStart(first);
            int tailEnd;

            if (isOrphanMark)
            {
                tailEnd = ScanCombiningAndCodaEnd(s, afterSpacePos);
            }
            else if (IsMyanmarConsonant(first))
            {
                int cEnd = ScanSingleRawSyllableEnd(s, afterSpacePos);
                if (!IsBareKilledCoda(s, afterSpacePos, cEnd))
                    return;
                tailEnd = cEnd;
            }
            else
            {
                return;
            }

            // If spacePos immediately follows a single bare consonant (like 'မ ား', 'တ င်', 'ဖ ွဲ့'), medial ျ/ွ attachment is much more likely
            // than splitting into bare-consonant + dropped ထ/ရ unless part of a real compound ('ဆ ာ -> ဆရာ', 'အ ည် -> အရည်')!
            bool precededByBareOpenConsonant = spacePos >= 1 && IsMyanmarConsonant(s[spacePos - 1]) &&
                                               (spacePos == 1 || !IsCombiningMark(s[spacePos - 1]));

            string tail = NormalizeLongStemU(s.Substring(afterSpacePos, tailEnd - afterSpacePos), preferPlainU: false);
            string[] droppedPrefixes = { "\u101B", "\u1011", "\u1011\u103D" };
            for (int p = 0; p < droppedPrefixes.Length; p++)
            {
                string pref = droppedPrefixes[p];
                if (pref.Length == 2 && tail.Length > 0 && tail[0] == '\u103D')
                    continue;

                string built = BuildCanonicalSyllable(pref, tail, hasPreBaseE: false);
                if (!IsWellFormedMyanmarSyllable(built))
                    continue;

                double pen = isOrphanMark ? 0.65 : 1.35;
                if (pref.Length == 2) pen += 0.35;
                if (precededByBareOpenConsonant) pen += 1.45;

                // Hypothesis without space (e.g. 'ဆ ာ -> ဆရာ', 'အကောင်အ ည် -> အကောင်အထည်', 'ပေါ် က် -> ပေါ်ထွက်', 'ဖွဲ့စည်း ားသော -> ဖွဲ့စည်းထားသော')
                results.Add(new SylAtom(spacePos, tailEnd, built, hasLeadingSpace: false, pen, requiresLexiconSupport: true));
                // Hypothesis with preserved word space (e.g. '၉ က် -> ၉ ရက်', 'သည်။ ို့ပြင် -> သည်။ ထို့ပြင်', 'တာဝန် မ်းဆောင် -> တာဝန် ထမ်းဆောင်')
                results.Add(new SylAtom(spacePos, tailEnd, built, hasLeadingSpace: true, pen + 0.30, requiresLexiconSupport: true));
            }
        }

        private static List<SylAtom> GenerateSyllableAtomsNoLeadSpace(string s, int pos)
        {
            var list = new List<SylAtom>(10);
            int n = s.Length;
            if (pos >= n || !IsMyanmarCodepoint(s[pos]))
                return list;

            // 0. Independent vowels (ဣ, ဤ, ဥ, ဦ, ဧ, ဩ, ဪ) & standalone literary symbols (၌, ၍, ၎င်း, ၏)
            if (!IsMyanmarConsonant(s[pos]))
            {
                if (s[pos] == '\u104E' && pos + 3 < n && s[pos + 1] == '\u1004' && s[pos + 2] == '\u103A' && s[pos + 3] == '\u1038')
                {
                    list.Add(new SylAtom(pos, pos + 4, "\u104E\u1004\u103A\u1038", false, 0.0, false));
                    return list;
                }

                int ivEnd = ScanSingleRawSyllableEnd(s, pos);
                string ivSyl = UnicodeNormalizer.NormalizeCanonicalOrder(s.Substring(pos, ivEnd - pos));
                list.Add(new SylAtom(pos, ivEnd, ivSyl, false, 0.0, false));
                return list;
            }

            // 1. Visual Pre-Base 'ေ' ('ရ' \u101B before another consonant OR before ' ' + orphan marks as in 'ရ း'/'ရ ာင်'/'ရ ာက်'/'ရ ှာက်')
            if (s[pos] == '\u101B' && pos + 1 < n)
            {
                // 1a. 'ရ' + ' ' + tail -> pre-base 'ေ' + dropped 'ရ' or 'ထ' (e.g. 'ရ း -> ရေး', 'ရ ာင် -> ထောင်', 'ရ ာက် -> ရောက်/ထောက်', 'ရ ှာက် -> ရှောက်')
                if (s[pos + 1] == ' ' && pos + 2 < n)
                {
                    int afterSp = pos + 2;
                    int tEnd = -1;
                    if (IsOrphanCombiningStart(s[afterSp]))
                        tEnd = ScanCombiningAndCodaEnd(s, afterSp);
                    else if (IsMyanmarConsonant(s[afterSp]))
                    {
                        int cEnd = ScanSingleRawSyllableEnd(s, afterSp);
                        if (IsBareKilledCoda(s, afterSp, cEnd))
                            tEnd = cEnd;
                    }

                    if (tEnd > afterSp)
                    {
                        string tail = NormalizeLongStemU(s.Substring(afterSp, tEnd - afterSp), preferPlainU: false);
                        string asRe = BuildCanonicalSyllable("\u101B", tail, hasPreBaseE: true);
                        if (IsWellFormedMyanmarSyllable(asRe))
                            list.Add(new SylAtom(pos, tEnd, asRe, false, 0.35, requiresLexiconSupport: (tail != "\u1038")));

                        string asHta = BuildCanonicalSyllable("\u1011", tail, hasPreBaseE: true);
                        if (IsWellFormedMyanmarSyllable(asHta))
                            list.Add(new SylAtom(pos, tEnd, asHta, false, 0.45, requiresLexiconSupport: true));
                    }
                }

                // 1b. 'ရ' + Consonant cluster -> pre-base 'ေ' attached to that consonant cluster (including 'ရကကာင်း -> ကြောင်း', 'ရပခ -> ခြေ', 'ရက ာင်း -> ကျောင်း', 'ရမ း -> မွေး')
                if (IsMyanmarConsonant(s[pos + 1]))
                {
                    AddBaseClusterAtoms(s, pos, pos + 1, hasPreBaseE: true, basePenalty: 0.18, list);
                }
            }

            // 2. Normal base cluster starting directly at pos (also covers literal 'ရ' as in 'ရရှိ', 'ရတနာ', 'ရမည်'!)
            AddBaseClusterAtoms(s, pos, pos, hasPreBaseE: false, basePenalty: 0.0, list);

            return list;
        }

        private static void AddBaseClusterAtoms(string s, int atomStartPos, int cPos, bool hasPreBaseE, double basePenalty, List<SylAtom> list)
        {
            int n = s.Length;
            if (cPos >= n || !IsMyanmarConsonant(s[cPos]))
                return;

            char c1 = s[cPos];

            // Branch A: Visual Medial Ra ('ပ' \u1015 or 'က' \u1000 before base consonant c2, e.g. 'ပပည်->ပြည်', 'ပဖစ်->ဖြစ်', 'ကကီး->ကြီး', 'အရကကာင်း->အကြောင်း')
            if ((c1 == '\u1015' || c1 == '\u1000') && cPos + 1 < n && IsMyanmarConsonant(s[cPos + 1]))
            {
                char c2 = s[cPos + 1];
                if (CanTakeMedialRa(c2))
                {
                    EmitClusterTailVariants(s, atomStartPos, cPos + 1, c2.ToString() + "\u103C", hasPreBaseE, basePenalty + 0.20, requiresLexicon: false, isMedialRaOrStack: true, list);
                }
            }

            // Branch B: Direct consonant at cPos
            EmitClusterTailVariants(s, atomStartPos, cPos, c1.ToString(), hasPreBaseE, basePenalty, requiresLexicon: false, isMedialRaOrStack: false, list);

            // Branch C: Unstacked Pali Geminate / Homorganic Stack across cPos and next syllable (e.g. 'သစစာ -> သစ္စာ', 'အဓိပပာယ် -> အဓိပ္ပာယ်', 'တကကသိုလ် -> တက္ကသိုလ်')
            if (!hasPreBaseE)
            {
                int firstEnd = ScanSingleRawSyllableEnd(s, cPos);
                if (firstEnd == cPos + 1 && firstEnd < n && IsMyanmarConsonant(s[firstEnd]))
                {
                    char c2 = s[firstEnd];
                    if (IsValidPaliStackPair(c1, c2))
                    {
                        string onset = (c1 == '\u101E' && c2 == '\u101E')
                            ? "\u103F"
                            : (c1.ToString() + "\u1039" + c2.ToString());
                        EmitClusterTailVariants(s, atomStartPos, firstEnd, onset, hasPreBaseE: false, basePenalty + 0.55, requiresLexicon: true, isMedialRaOrStack: true, list);
                    }
                }
            }
        }

        private static void EmitClusterTailVariants(
            string s,
            int atomStartPos,
            int baseConsPos,
            string onsetCluster,
            bool hasPreBaseE,
            double penalty,
            bool requiresLexicon,
            bool isMedialRaOrStack,
            List<SylAtom> list)
        {
            int n = s.Length;
            int rawEnd = ScanSingleRawSyllableEnd(s, baseConsPos);
            string rawMarksAndCoda = s.Substring(baseConsPos + 1, rawEnd - (baseConsPos + 1));

            bool hasStack = onsetCluster.IndexOf('\u1039') >= 0 || rawMarksAndCoda.IndexOf('\u1039') >= 0;

            // 1. Standard syllable at [baseConsPos..rawEnd]
            if (rawMarksAndCoda.IndexOf("\u103B\u102F", StringComparison.Ordinal) >= 0 ||
                rawMarksAndCoda.IndexOf("\u102D\u103B\u102F", StringComparison.Ordinal) >= 0)
            {
                string plainU = NormalizeLongStemU(rawMarksAndCoda, preferPlainU: true);
                string builtPlainU = BuildCanonicalSyllable(onsetCluster, plainU, hasPreBaseE);
                if (IsWellFormedMyanmarSyllable(builtPlainU))
                {
                    double uPen = (isMedialRaOrStack || hasStack) ? penalty + 0.15 : penalty + 0.85;
                    list.Add(new SylAtom(atomStartPos, rawEnd, builtPlainU, false, uPen, requiresLexicon || (!isMedialRaOrStack && !hasStack)));
                }

                if (!onsetCluster.EndsWith("\u103C", StringComparison.Ordinal))
                {
                    string keepYaU = NormalizeLongStemU(rawMarksAndCoda, preferPlainU: false);
                    string builtYaU = BuildCanonicalSyllable(onsetCluster, keepYaU, hasPreBaseE);
                    if (IsWellFormedMyanmarSyllable(builtYaU) && !string.Equals(builtYaU, builtPlainU, StringComparison.Ordinal))
                        list.Add(new SylAtom(atomStartPos, rawEnd, builtYaU, false, penalty, requiresLexicon));
                }
            }
            else
            {
                string built = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda, hasPreBaseE);
                if (IsWellFormedMyanmarSyllable(built))
                {
                    list.Add(new SylAtom(atomStartPos, rawEnd, built, false, penalty, requiresLexicon));

                    // Ambiguous CMap 'ဖ ံွဲ့' -> both 'ဖွံ့' (ဖွံ့ဖြိုး) and 'ဖွဲ့' (ဖွဲ့စည်းပုံ) resolved by 96K MyanmarTrie compounds
                    if (built == "\u1016\u103D\u1036\u1037")
                    {
                        list.Add(new SylAtom(atomStartPos, rawEnd, "\u1016\u103D\u1032\u1037", false, penalty + 0.35, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                    }

                    // Dropped aukmyit (\u1037) turned into trailing space on nasal/glide coda ('ရလကျင် ရပေး -> လေ့ကျင့်ပေး')
                    if (rawEnd < n && s[rawEnd] == ' ' && rawEnd + 1 < n && IsMyanmarCodepoint(s[rawEnd + 1]) && EndsWithPlainNasalOrGlideAsat(built))
                    {
                        string withDot = UnicodeNormalizer.NormalizeCanonicalOrder(built + "\u1037");
                        list.Add(new SylAtom(atomStartPos, rawEnd + 1, withDot, false, penalty + 0.55, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                    }
                    else if (hasPreBaseE && string.IsNullOrEmpty(rawMarksAndCoda))
                    {
                        string eDot = UnicodeNormalizer.NormalizeCanonicalOrder(built + "\u1037");
                        list.Add(new SylAtom(atomStartPos, rawEnd, eDot, false, penalty + 0.85, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                    }
                }
            }

            // 2. Intra-syllable space-dropped medials/vowels right after an open base cluster
            bool baseHasOnlyMedials = IsOnlyMedials(rawMarksAndCoda);
            if (baseHasOnlyMedials && rawEnd < n && s[rawEnd] == ' ')
            {
                int afterSp = rawEnd + 1;
                if (afterSp < n)
                {
                    // 2a. Space followed by orphan combining marks ('ာင်း', 'ား', 'ျုပ်', 'ွဲ့', 'ိျုး', 'း')
                    if (IsOrphanCombiningStart(s[afterSp]))
                    {
                        int mEnd = ScanCombiningAndCodaEnd(s, afterSp);
                        string mTail = NormalizeLongStemU(s.Substring(afterSp, mEnd - afterSp), preferPlainU: false);

                        if (mTail.Length > 0 && (mTail[0] == '\u103B' || mTail[0] == '\u103D'))
                        {
                            string joined = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + mTail, hasPreBaseE);
                            if (IsWellFormedMyanmarSyllable(joined))
                                list.Add(new SylAtom(atomStartPos, mEnd, joined, false, penalty + 0.20, requiresLexicon));
                        }

                        if (!onsetCluster.EndsWith("\u103C", StringComparison.Ordinal) && mTail.IndexOf('\u103B') < 0)
                        {
                            string withYa = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103B" + mTail, hasPreBaseE);
                            if (IsWellFormedMyanmarSyllable(withYa))
                                list.Add(new SylAtom(atomStartPos, mEnd, withYa, false, penalty + 0.30, requiresLexiconSupport: true));

                            if (rawMarksAndCoda.IndexOf('\u103E') < 0 && mTail.IndexOf('\u103E') < 0)
                            {
                                string withYaHa = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103B\u103E" + mTail, hasPreBaseE);
                                if (IsWellFormedMyanmarSyllable(withYaHa))
                                    list.Add(new SylAtom(atomStartPos, mEnd, withYaHa, false, penalty + 0.42, requiresLexiconSupport: true));
                            }
                        }
                        else if (mTail.IndexOf('\u103B') >= 0)
                        {
                            string withYa = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + mTail, hasPreBaseE);
                            if (IsWellFormedMyanmarSyllable(withYa))
                                list.Add(new SylAtom(atomStartPos, mEnd, withYa, false, penalty + 0.25, requiresLexiconSupport: true));
                        }

                        if (mTail.IndexOf('\u103D') < 0)
                        {
                            string withWa = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103D" + mTail, hasPreBaseE);
                            if (IsWellFormedMyanmarSyllable(withWa))
                                list.Add(new SylAtom(atomStartPos, mEnd, withWa, false, penalty + 0.30, requiresLexiconSupport: true));

                            if (rawMarksAndCoda.IndexOf('\u103E') < 0 && mTail.IndexOf('\u103E') < 0)
                            {
                                string withWaHa = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103D\u103E" + mTail, hasPreBaseE);
                                if (IsWellFormedMyanmarSyllable(withWaHa))
                                    list.Add(new SylAtom(atomStartPos, mEnd, withWaHa, false, penalty + 0.42, requiresLexiconSupport: true));
                            }
                        }

                        if (rawMarksAndCoda.IndexOf('\u103E') >= 0 && !hasPreBaseE)
                        {
                            string withUu = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u1030" + mTail, hasPreBaseE: false);
                            if (IsWellFormedMyanmarSyllable(withUu))
                                list.Add(new SylAtom(atomStartPos, mEnd, withUu, false, penalty + 0.40, requiresLexiconSupport: true));
                        }
                        else if (!hasPreBaseE && mTail == "\u1038")
                        {
                            // e.g. 'က းလွန် -> ကျူးလွန်', 'က းပြောင်း -> ကူးပြောင်း', 'ထ းခြား -> ထူးခြား'
                            string withUu = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u1030\u1038", hasPreBaseE: false);
                            if (IsWellFormedMyanmarSyllable(withUu))
                                list.Add(new SylAtom(atomStartPos, mEnd, withUu, false, penalty + 0.70, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));

                            if (!onsetCluster.EndsWith("\u103C", StringComparison.Ordinal) && rawMarksAndCoda.IndexOf('\u103B') < 0)
                            {
                                string withYaUu = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103B\u1030\u1038", hasPreBaseE: false);
                                if (IsWellFormedMyanmarSyllable(withYaUu))
                                    list.Add(new SylAtom(atomStartPos, mEnd, withYaUu, false, penalty + 0.75, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                            }
                        }
                    }
                    // 2b. Space followed by bare killed coda or next Myanmar codepoint ('တ င် -> တွင်', 'ခ က် -> ချက်', 'တည်ဆ ဥပဒေ -> တည်ဆဲ ဥပဒေ', 'ရင နှင့် -> ငွေ နှင့်')
                    else if (IsMyanmarCodepoint(s[afterSp]))
                    {
                        if (IsMyanmarConsonant(s[afterSp]))
                        {
                            int cEnd = ScanSingleRawSyllableEnd(s, afterSp);
                            if (IsBareKilledCoda(s, afterSp, cEnd))
                            {
                                string coda = s.Substring(afterSp, cEnd - afterSp);
                                string[] medials = { "\u103B", "\u103D", "\u103B\u103E", "\u103D\u103E" };
                                for (int m = 0; m < medials.Length; m++)
                                {
                                    if (onsetCluster.EndsWith("\u103C", StringComparison.Ordinal) && medials[m].IndexOf('\u103B') >= 0)
                                        continue;
                                    string merged = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + medials[m] + coda, hasPreBaseE);
                                    if (IsWellFormedMyanmarSyllable(merged))
                                    {
                                        double mPen = penalty + 0.38 + (m >= 2 ? 0.25 : 0.0);
                                        list.Add(new SylAtom(atomStartPos, cEnd, merged, false, mPen, requiresLexiconSupport: true));
                                        if (EndsWithPlainNasalOrGlideAsat(merged))
                                        {
                                            string mergedDot = UnicodeNormalizer.NormalizeCanonicalOrder(merged + "\u1037");
                                            list.Add(new SylAtom(atomStartPos, cEnd, mergedDot, false, mPen + 0.55, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                                            if (cEnd < n && s[cEnd] == ' ' && cEnd + 1 < n && IsMyanmarCodepoint(s[cEnd + 1]))
                                            {
                                                list.Add(new SylAtom(atomStartPos, cEnd + 1, mergedDot, false, mPen + 0.45, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // 2c. Space is a dropped open-syllable vowel/medial ('ဲ' \u1032, 'ွဲ' \u103D\u1032, 'ျ' \u103B, 'ူ' \u1030, '့' \u1037, 'ွေ' \u103D+\u1031) before the NEXT syllable!
                        if (hasPreBaseE)
                        {
                            // e.g. 'ရလ က င့် -> လေ့ကျင့်', 'ရလ လာ -> လေ့လာ'
                            string eAukmyit = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u1037", hasPreBaseE: true);
                            list.Add(new SylAtom(atomStartPos, afterSp, eAukmyit, false, penalty + 0.85, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));

                            // e.g. 'အခရကကေးရင နှင့် -> အခကြေးငွေ နှင့်', 'ရင ကြေး -> ငွေကြေး'
                            if (rawMarksAndCoda.IndexOf('\u103D') < 0)
                            {
                                string eWa = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103D", hasPreBaseE: true);
                                list.Add(new SylAtom(atomStartPos, afterSp, eWa, false, penalty + 0.75, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                                list.Add(new SylAtom(atomStartPos, rawEnd, eWa, false, penalty + 0.85, requiresLexiconSupport: true));
                            }
                        }
                        else
                        {
                            string[] openVowels = { "\u1032", "\u103D\u1032", "\u103B", "\u1037" };
                            for (int v = 0; v < openVowels.Length; v++)
                            {
                                if (onsetCluster.EndsWith("\u103C", StringComparison.Ordinal) && openVowels[v] == "\u103B")
                                    continue;
                                string openSyl = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + openVowels[v], hasPreBaseE: false);
                                // Consume the dropped-vowel space (afterSp) inside compounds ('အမြဲတမ်း', 'သုံးစွဲ', 'တွဲဖက်', 'ချမှတ်', 'ကျောင်းခွဲများ')
                                list.Add(new SylAtom(atomStartPos, afterSp, openSyl, false, penalty + 0.90, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                                // Or keep word space after 'မရှိဘဲ' / 'ခန့်ခွဲ' when 'ဘဲ'/'ခွဲ' closes a multi-syllable word!
                                if (openVowels[v] == "\u1032" || openVowels[v] == "\u103D\u1032")
                                    list.Add(new SylAtom(atomStartPos, rawEnd, openSyl, false, penalty + 1.05, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                            }

                            if (rawMarksAndCoda.IndexOf('\u103E') >= 0)
                            {
                                string uuSyl = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u1030", hasPreBaseE: false);
                                list.Add(new SylAtom(atomStartPos, afterSp, uuSyl, false, penalty + 0.80, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                            }
                        }
                    }
                }
            }

            // 2d. Trailing dropped 'ဲ' / 'ွဲ' / 'ွေ' / 'ရ' at end-of-chunk OR right before a word-boundary space ('အစိုး ပညာရေး -> အစိုးရ ပညာရေး', '၄၁၉ အ ဤဥပဒေ -> ၄၁၉ အရ ဤဥပဒေ', 'ပုဒ်မခွဲ (က)', 'ဒဏ်ရင -> ဒဏ်ငွေ')
            if (rawEnd == n && baseHasOnlyMedials)
            {
                if (!hasPreBaseE)
                {
                    list.Add(new SylAtom(atomStartPos, rawEnd, BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u1032", false), false, penalty + 1.15, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                    list.Add(new SylAtom(atomStartPos, rawEnd, BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103D\u1032", false), false, penalty + 1.15, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                }
                else if (rawMarksAndCoda.IndexOf('\u103D') < 0)
                {
                    list.Add(new SylAtom(atomStartPos, rawEnd, BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda + "\u103D", true), false, penalty + 0.95, requiresLexiconSupport: true, requiresMultiSyllableCompound: true));
                }
            }
            if (rawEnd == n || (rawEnd < n && s[rawEnd] == ' '))
            {
                string baseSyl = BuildCanonicalSyllable(onsetCluster, rawMarksAndCoda, hasPreBaseE);
                if (IsWellFormedMyanmarSyllable(baseSyl))
                {
                    bool isAh = baseSyl == "\u1021";
                    list.Add(new SylAtom(atomStartPos, rawEnd, baseSyl + "\u101B", false, penalty + (isAh ? 1.55 : 1.35), requiresLexiconSupport: true, requiresMultiSyllableCompound: !isAh));
                }
            }
        }

        private static string BuildCanonicalSyllable(string onsetCluster, string marksAndCoda, bool hasPreBaseE)
        {
            if (!hasPreBaseE)
                return UnicodeNormalizer.FixLookalikeCharacters(UnicodeNormalizer.NormalizeCanonicalOrder(onsetCluster + marksAndCoda));

            // Insert '\u1031' right after subscript + medials (\u103B..\u103E) and BEFORE vowels/anusvara/tones/killed-coda!
            int idx = 0;
            while (idx < marksAndCoda.Length)
            {
                char c = marksAndCoda[idx];
                if (c == '\u1039' && idx + 1 < marksAndCoda.Length && IsMyanmarConsonant(marksAndCoda[idx + 1]))
                {
                    idx += 2;
                    continue;
                }
                if (c >= '\u103B' && c <= '\u103E')
                {
                    idx++;
                    continue;
                }
                break;
            }

            string combined = onsetCluster + marksAndCoda.Substring(0, idx) + "\u1031" + marksAndCoda.Substring(idx);
            return UnicodeNormalizer.FixLookalikeCharacters(UnicodeNormalizer.NormalizeCanonicalOrder(combined));
        }

        private static bool StartsWithBareKilledCodaInLine(string s, int pos)
        {
            if (pos >= s.Length || !IsMyanmarConsonant(s[pos]))
                return false;
            int end = ScanSingleRawSyllableEnd(s, pos);
            return IsBareKilledCoda(s, pos, end);
        }

        private static string NormalizeLongStemU(string marks, bool preferPlainU)
        {
            if (marks.IndexOf("\u102D\u103B\u102F", StringComparison.Ordinal) >= 0)
            {
                return preferPlainU
                    ? marks.Replace("\u102D\u103B\u102F", "\u102D\u102F")
                    : marks.Replace("\u102D\u103B\u102F", "\u103B\u102D\u102F");
            }
            if (marks.IndexOf("\u103B\u102F", StringComparison.Ordinal) >= 0 && preferPlainU)
            {
                return marks.Replace("\u103B\u102F", "\u102F");
            }
            return marks;
        }

        private static int ScanSingleRawSyllableEnd(string s, int consPos)
        {
            int n = s.Length;
            int i = consPos + 1;

            // 1. Subscript consonants & medials & vowels/marks on base consonant
            while (i < n)
            {
                char c = s[i];
                if (c == '\u1039' && i + 1 < n && IsMyanmarConsonant(s[i + 1]))
                {
                    i += 2;
                    continue;
                }
                if (IsCombiningMark(c))
                {
                    i++;
                    continue;
                }
                break;
            }

            // 2. Optional killed coda consonant(s) (Consonant + [\u1039 Cons]? + \u103A + [\u1037\u1038]*)
            //    HOWEVER, never swallow Kinzi (င်္ + Cons) if it opens the next syllable!
            if (i + 1 < n && IsMyanmarConsonant(s[i]))
            {
                // Kinzi check: Cons + \u103A + \u1039 + NextCons -> belongs to NEXT syllable, UNLESS i == consPos (impossible)
                if (i + 2 < n && s[i + 1] == '\u103A' && s[i + 2] == '\u1039')
                {
                    // Attach Kinzi to next syllable? In MyanmarSyllableBreaker, 'မင်္ဂလာ' breaks as ['မင်္', 'ဂ', 'လာ'] or ['မင်', 'ဂလာ']?
                    // Wait: in MyanmarTrie, 'နိုင်ငံ' breaks as ['နိုင်', 'ငံ'] (no \u1039), while 'သင်္ဘော' has '\u1004\u103A\u1039\u1018\u1031\u102C'!
                    // Include Cons+\u103A+\u1039+SubCons+Marks together when Kinzi occurs!
                    int k = i + 3;
                    if (k < n && IsMyanmarConsonant(s[k]))
                    {
                        k++;
                        while (k < n && IsCombiningMark(s[k]))
                            k++;
                        // Check if that stacked syllable also has a killed coda (e.g. 'သင်္ဂြိုဟ်')
                        if (k + 1 < n && IsMyanmarConsonant(s[k]) && s[k + 1] == '\u103A')
                        {
                            k += 2;
                            while (k < n && (s[k] == '\u1037' || s[k] == '\u1038'))
                                k++;
                        }
                        return k;
                    }
                }

                // Normal killed coda: Cons + (\u103B)? + \u103A + [\u1037\u1038]*
                int codaIdx = i + 1;
                if (codaIdx < n && s[codaIdx] == '\u103B')
                    codaIdx++;
                if (codaIdx < n && s[codaIdx] == '\u103A')
                {
                    codaIdx++;
                    while (codaIdx < n && (s[codaIdx] == '\u1037' || s[codaIdx] == '\u1038'))
                        codaIdx++;
                    return codaIdx;
                }
            }

            return i;
        }

        private static int ScanCombiningAndCodaEnd(string s, int markStart)
        {
            int n = s.Length;
            int i = markStart;
            while (i < n && IsCombiningMark(s[i]))
                i++;

            if (i + 1 < n && IsMyanmarConsonant(s[i]))
            {
                int codaIdx = i + 1;
                if (codaIdx < n && s[codaIdx] == '\u103B')
                    codaIdx++;
                if (codaIdx < n && s[codaIdx] == '\u103A')
                {
                    codaIdx++;
                    while (codaIdx < n && (s[codaIdx] == '\u1037' || s[codaIdx] == '\u1038'))
                        codaIdx++;
                    return codaIdx;
                }
            }

            return i;
        }

        private static bool IsBareKilledCoda(string s, int consPos, int endPos)
        {
            int len = endPos - consPos;
            if (len < 2 || len > 4 || !IsMyanmarConsonant(s[consPos]))
                return false;

            int idx = consPos + 1;
            if (idx < endPos && s[idx] == '\u103B')
                idx++;
            if (idx >= endPos || s[idx] != '\u103A')
                return false;
            idx++;
            while (idx < endPos)
            {
                if (s[idx] != '\u1037' && s[idx] != '\u1038')
                    return false;
                idx++;
            }
            return true;
        }

        private static bool IsOnlyMedials(string marks)
        {
            for (int i = 0; i < marks.Length; i++)
            {
                char c = marks[i];
                if (c < '\u103B' || c > '\u103E')
                    return false;
            }
            return true;
        }

        private static bool CanTakeMedialRa(char c)
        {
            return c == '\u1000' || c == '\u1001' || c == '\u1002' || c == '\u1004' ||
                   c == '\u1005' || c == '\u1012' || c == '\u1015' || c == '\u1016' ||
                   c == '\u1017' || c == '\u1018' || c == '\u1019';
        }

        private static bool IsValidPaliStackPair(char c1, char c2)
        {
            if (c1 == c2)
                return true;
            // Homorganic aspirated & nasal-stop Pali clusters
            return (c1 == '\u1000' && c2 == '\u1001') ||
                   (c1 == '\u1002' && c2 == '\u1003') ||
                   (c1 == '\u1005' && c2 == '\u1006') ||
                   (c1 == '\u1007' && c2 == '\u1008') ||
                   (c1 == '\u1009' && (c2 >= '\u1005' && c2 <= '\u1008')) ||
                   (c1 == '\u100B' && c2 == '\u100C') ||
                   (c1 == '\u100D' && c2 == '\u100E') ||
                   (c1 == '\u100F' && (c2 >= '\u100B' && c2 <= '\u100E')) ||
                   (c1 == '\u1010' && c2 == '\u1011') ||
                   (c1 == '\u1012' && c2 == '\u1013') ||
                   (c1 == '\u1014' && (c2 >= '\u1010' && c2 <= '\u1013')) ||
                   (c1 == '\u1015' && c2 == '\u1016') ||
                   (c1 == '\u1017' && c2 == '\u1018') ||
                   (c1 == '\u1019' && (c2 >= '\u1015' && c2 <= '\u1018'));
        }

        private static bool IsWellFormedMyanmarSyllable(string syl)
        {
            if (string.IsNullOrEmpty(syl))
                return false;

            char first = syl[0];
            if (!IsMyanmarConsonant(first) && !(first >= '\u1023' && first <= '\u102A') && first != '\u103F' && !(first >= '\u104C' && first <= '\u104F'))
                return false;

            // Reject duplicate vowels/medials that could create dotted circles
            int eCount = 0, uCount = 0, iCount = 0, aaCount = 0;
            for (int k = 1; k < syl.Length; k++)
            {
                char c = syl[k];
                if (c == '\u1031') eCount++;
                else if (c == '\u102F' || c == '\u1030') uCount++;
                else if (c == '\u102D' || c == '\u102E') iCount++;
                else if (c == '\u102B' || c == '\u102C') aaCount++;
            }

            return eCount <= 1 && uCount <= 1 && iCount <= 1 && aaCount <= 1;
        }

        private static bool IsMyanmarCodepoint(char c) =>
            (c >= '\u1000' && c <= '\u103F') || (c >= '\u104C' && c <= '\u1059');

        private static bool IsMyanmarOrAsciiDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= '\u1040' && c <= '\u1049');

        private static bool IsMyanmarConsonant(char c) =>
            c >= '\u1000' && c <= '\u1021';

        private static bool IsOrphanCombiningStart(char c) =>
            c >= '\u102B' && c <= '\u103E';

        private static bool IsCombiningMark(char c) =>
            c >= '\u102B' && c <= '\u103E' && c != '\u1039';
    }
}
