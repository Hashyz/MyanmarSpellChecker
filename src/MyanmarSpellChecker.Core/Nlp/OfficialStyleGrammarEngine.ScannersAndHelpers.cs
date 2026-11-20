// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    public sealed partial class OfficialStyleGrammarEngine
    {
        private static bool HasAnySubstring(string text, ulong mmMask, string[] triggers)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                if (ContainsMm(text, mmMask, triggers[i]))
                    return true;
            }
            return false;
        }

        private static bool MayContainDuplicatedFunctionWord(string text, ulong mmMask)
        {
            int len = text.Length;
            for (int i = 0; i < DuplicatedFunctionWordCandidates.Length; i++)
            {
                string w = DuplicatedFunctionWordCandidates[i];
                if (!ContainsMm(text, mmMask, w))
                    continue;
                int searchPos = 0;
                while (searchPos < len)
                {
                    int hit = text.IndexOf(w, searchPos, StringComparison.Ordinal);
                    if (hit < 0)
                        break;
                    int after = hit + w.Length;
                    if (after < len && (text[after] == ' ' || text[after] == '\t'))
                    {
                        int k = after + 1;
                        while (k < len && (text[k] == ' ' || text[k] == '\t'))
                            k++;
                        if (k + w.Length <= len && text[k] == w[0] && string.CompareOrdinal(text, k, w, 0, w.Length) == 0)
                            return true;
                    }
                    searchPos = hit + 1;
                }
            }
            return false;
        }

        /// <summary>
        /// Ultra-fast O(1) validator returning <c>true</c> ONLY when <paramref name="text"/> is a 100% canonical,
        /// spotless standalone Myanmar NRC cell (State 1..14 + '/' + verified MOIP Township in <paramref name="trie"/>
        /// + canonical card type + 6 clean digits with zero lookalike/zero-width corruptions).
        /// Any questionable or malformed NRC returns <c>false</c> so full <c>CheckText</c> diagnostics run unchanged.
        /// </summary>
        internal static bool IsCleanCanonicalStandaloneNrc(string text, MyanmarTrie trie)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 10 || text.Length > 28)
                return false;

            for (int k = 0; k < text.Length; k++)
            {
                char ch = text[k];
                if (ch == '\u00A0' || ch == '\u202F' || ch == '\u2007' ||
                    (ch >= '\u200B' && ch <= '\u200D') || ch == '\uFEFF' || ch == '\u2060' || ch == '\u00AD' || ch == '\u25CC')
                {
                    return false;
                }
            }

            int p = 0;
            int end = text.Length;
            while (p < end && (text[p] == ' ' || text[p] == '\t'))
                p++;
            while (end > p && (text[end - 1] == ' ' || text[end - 1] == '\t'))
                end--;
            if (end - p < 10)
                return false;

            // 1. State number 1..14 (no leading zero, pure Myanmar or pure ASCII digits)
            char d0 = text[p];
            bool mmState = d0 >= '\u1041' && d0 <= '\u1049';
            bool ascState = d0 >= '1' && d0 <= '9';
            if (!mmState && !ascState)
                return false;

            int stateVal = mmState ? (d0 - '\u1040') : (d0 - '0');
            p++;
            if (p < end)
            {
                char d1 = text[p];
                if (mmState && d1 >= '\u1040' && d1 <= '\u1049')
                {
                    stateVal = stateVal * 10 + (d1 - '\u1040');
                    p++;
                }
                else if (ascState && d1 >= '0' && d1 <= '9')
                {
                    stateVal = stateVal * 10 + (d1 - '0');
                    p++;
                }
            }
            if (stateVal < 1 || stateVal > 14)
                return false;

            while (p < end && text[p] == ' ')
                p++;
            if (p >= end || text[p] != '/')
                return false;
            p++;
            while (p < end && text[p] == ' ')
                p++;

            // 2. Township code: 2..4 bare Myanmar letters in trie (excluding \u1009 lookalike)
            int twStart = p;
            while (p < end)
            {
                char tc = text[p];
                if ((tc >= '\u1000' && tc <= '\u1021' && tc != '\u1009') || tc == '\u1025')
                    p++;
                else
                    break;
            }
            int twLen = p - twStart;
            if (twLen < 2 || twLen > 4)
                return false;

            string township = text.Substring(twStart, twLen);
            if (!trie.ContainsExactWord(township) || trie.TryGetWrongFormRule(township, out _))
                return false;

            while (p < end && text[p] == ' ')
                p++;
            if (p >= end || text[p] != '(')
                return false;
            p++;
            while (p < end && text[p] == ' ')
                p++;

            // 3. Canonical card type inside (...)
            int closeParen = text.IndexOf(')', p);
            if (closeParen < 0 || closeParen >= end)
                return false;
            int ctEnd = closeParen;
            while (ctEnd > p && text[ctEnd - 1] == ' ')
                ctEnd--;
            int ctLen = ctEnd - p;
            bool validCardType =
                MatchesSliceOrdinal(text, p, ctLen, "နိုင်") ||
                MatchesSliceOrdinal(text, p, ctLen, "ဧည့်") ||
                MatchesSliceOrdinal(text, p, ctLen, "ပြု") ||
                MatchesSliceOrdinal(text, p, ctLen, "သာသနာ") ||
                MatchesSliceOrdinal(text, p, ctLen, "ယာယီ") ||
                (ctLen == 1 && (text[p] == 'စ' || text[p] == 'န' || text[p] == 'သ')) ||
                MatchesSliceOrdinal(text, p, ctLen, "သာ");

            if (!validCardType)
                return false;

            p = closeParen + 1;
            while (p < end && text[p] == ' ')
                p++;

            // 4. Exactly 6 clean digits (pure Myanmar or pure ASCII, zero consonant Wa \u101D)
            if (end - p != 6)
                return false;

            char s0 = text[p];
            bool mmSer = s0 >= '\u1040' && s0 <= '\u1049';
            bool ascSer = s0 >= '0' && s0 <= '9';
            if (!mmSer && !ascSer)
                return false;

            for (int si = 1; si < 6; si++)
            {
                char sc = text[p + si];
                if (mmSer)
                {
                    if (sc < '\u1040' || sc > '\u1049')
                        return false;
                }
                else
                {
                    if (sc < '0' || sc > '9')
                        return false;
                }
            }

            return true;
        }

        private static bool MatchesSliceOrdinal(string text, int start, int length, string literal)
        {
            return length == literal.Length && string.CompareOrdinal(text, start, literal, 0, literal.Length) == 0;
        }

        private static bool HasStandaloneMhaParticle(string text)
        {
            int pos = 0;
            int len = text.Length;
            while (pos < len)
            {
                int hit = text.IndexOf("မှ", pos, StringComparison.Ordinal);
                if (hit < 0)
                    return false;

                int after = hit + 2;
                if (after >= len)
                    return true;

                char next = text[after];
                // Skip open vowel/combining syllables like မှာ, မှီ, မှု, မှူး, မှေ, မှဲ, မှံ
                if (next >= '\u102B' && next <= '\u103A')
                {
                    pos = after;
                    continue;
                }

                // Skip closed coda syllables like မှတ်, မှန်, မှက်, မှပ်, မှယ် (where next is C and next+1 is Asat \u103A)
                if (next >= '\u1000' && next <= '\u1021' && after + 1 < len && text[after + 1] == '\u103A')
                {
                    pos = after;
                    continue;
                }

                return true;
            }
            return false;
        }

        private static bool HasAnyMyanmarNumeralCore(string text, ulong mmMask, bool hasNonZeroMyanmarDigit)
        {
            if (hasNonZeroMyanmarDigit)
                return true;

            return ContainsMm(text, mmMask, "တစ်") ||
                   ContainsMm(text, mmMask, "နှစ်") ||
                   ContainsMm(text, mmMask, "သုံး") ||
                   ContainsMm(text, mmMask, "လေး") ||
                   ContainsMm(text, mmMask, "ငါး") ||
                   ContainsMm(text, mmMask, "ခြောက်") ||
                   ContainsMm(text, mmMask, "ခုနစ်") ||
                   ContainsMm(text, mmMask, "ရှစ်") ||
                   ContainsMm(text, mmMask, "ကိုး") ||
                   ContainsMm(text, mmMask, "ဆယ်");
        }

        private static bool OverlapsExistingIssue(List<ProofingIssue> issues, int index, int length)
        {
            int end = index + length;
            for (int i = 0; i < issues.Count; i++)
            {
                ProofingIssue ex = issues[i];
                if (index < ex.StartIndex + ex.Length && end > ex.StartIndex)
                    return true;
            }
            return false;
        }

        private static bool IsMyanmarRunLetterOrMark(char c)
        {
            return c >= '\u1000' && c <= '\u103F';
        }

        private static bool IsBoundaryParticleBeforeProperStem(string syl)
        {
            return syl == "က" || syl == "ကို" || syl == "မှ" || syl == "သို့" ||
                   syl == "၌" || syl == "တွင်" || syl == "မှာ" || syl == "၏" ||
                   syl == "နှင့်" || syl == "နှင့်" || syl == "ဖြင့်" || syl == "ဖြင့်";
        }

        private static string ConcatSyllableRange(List<string> syls, int startIdx, int endIdxInclusive)
        {
            if (startIdx == endIdxInclusive)
                return UnicodeNormalizer.NormalizeCanonicalOrder(syls[startIdx]);

            var sb = new System.Text.StringBuilder(24);
            for (int i = startIdx; i <= endIdxInclusive; i++)
                sb.Append(syls[i]);
            return UnicodeNormalizer.NormalizeCanonicalOrder(sb.ToString());
        }

        private static bool IsShieldedLexicalTaStem(string text, int taIndex, int matchLength, MyanmarTrie? trie)
        {
            if (trie == null)
                return false;

            int runStart = taIndex;
            while (runStart > 0 && IsMyanmarRunLetterOrMark(text[runStart - 1]))
                runStart--;

            int runEnd = taIndex + matchLength;
            while (runEnd < text.Length && IsMyanmarRunLetterOrMark(text[runEnd]))
                runEnd++;

            string runRaw = text.Substring(runStart, runEnd - runStart);
            List<string> syls = MyanmarSyllableBreaker.BreakIntoSyllables(runRaw, isPreNormalized: true);
            if (syls.Count < 2)
                return false;

            int relTa = taIndex - runStart;
            int relEnd = relTa + matchLength;
            int cursor = 0;
            int taSylIdx = -1;
            int endSylIdx = -1;

            for (int i = 0; i < syls.Count; i++)
            {
                if (cursor == relTa)
                    taSylIdx = i;
                cursor += syls[i].Length;
                if (cursor == relEnd)
                    endSylIdx = i;
            }

            if (taSylIdx < 0 || endSylIdx < taSylIdx)
                return false;

            // 1. Rightward multi-syllable lexical stem starting at 'တ' (e.g. 'တလဟတ်', 'တလလ', 'တလဖက်', 'တလပါ', 'တလန', 'တအုပ်တအာ')
            for (int r = endSylIdx + 1; r <= Math.Min(syls.Count - 1, taSylIdx + 5); r++)
            {
                string rightStem = ConcatSyllableRange(syls, taSylIdx, r);
                if (trie.ContainsExactWord(rightStem) && !trie.TryGetWrongFormRule(rightStem, out _))
                    return true;
            }

            // 2. Multi-syllable stem spanning across 'တ + Classifier' (e.g. 'မဟီတလ', 'ယတလ', 'သွတ်တပတ်')
            //    or preceding proper/Pali stem ending in 'တ' before Classifier (e.g. 'နီးဂတ' + 'ခရိုင်', 'ယမဂတ' + 'ခရိုင်', 'အခိတ' + 'ခရိုင်')
            for (int l = 0; l < taSylIdx; l++)
            {
                if (l > 0 && !IsBoundaryParticleBeforeProperStem(UnicodeNormalizer.NormalizeCanonicalOrder(syls[l - 1])))
                    continue;

                string leftBeforeTa = ConcatSyllableRange(syls, l, taSylIdx - 1);
                bool leftIsStandalone = trie.ContainsExactWord(leftBeforeTa);

                for (int r = endSylIdx; r < syls.Count; r++)
                {
                    string spanStem = ConcatSyllableRange(syls, l, r);
                    if (trie.ContainsExactWord(spanStem) && !trie.TryGetWrongFormRule(spanStem, out _))
                    {
                        return true;
                    }
                }

                if (taSylIdx - l >= 1)
                {
                    string stemEndingTa = ConcatSyllableRange(syls, l, taSylIdx);
                    if (trie.TryGetExactEntries(stemEndingTa, out var tEntries) &&
                        tEntries != null && tEntries.Count > 0 &&
                        !trie.TryGetWrongFormRule(stemEndingTa, out _) &&
                        (!leftIsStandalone || (taSylIdx - l >= 2 && (tEntries[0].PosTag == "PROPN" || tEntries[0].IsOfficialThatpon))))
                    {
                        return true;
                    }
                    if (!leftIsStandalone)
                    {
                        var taHomos = trie.LookupStrictHomophones(stemEndingTa);
                        for (int hi = 0; hi < taHomos.Count; hi++)
                        {
                            if (taHomos[hi].IsOfficialThatpon && taHomos[hi].Word.EndsWith("တ", StringComparison.Ordinal))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsShieldedLexicalPiWord(string text, int piIndex, int piLength, MyanmarTrie? trie)
        {
            if (trie == null)
                return false;

            int runStart = piIndex;
            while (runStart > 0 && IsMyanmarRunLetterOrMark(text[runStart - 1]))
                runStart--;

            int runEnd = piIndex + piLength;
            while (runEnd < text.Length && IsMyanmarRunLetterOrMark(text[runEnd]))
                runEnd++;

            if (runEnd - runStart <= piLength)
                return false;

            string wordEndingAtPi = UnicodeNormalizer.NormalizeCanonicalOrder(
                text.Substring(runStart, (piIndex + piLength) - runStart));
            if (wordEndingAtPi.Length > piLength &&
                trie.ContainsExactWord(wordEndingAtPi) &&
                !trie.TryGetWrongFormRule(wordEndingAtPi, out _))
            {
                return true;
            }

            if (runEnd > piIndex + piLength)
            {
                string fullWord = UnicodeNormalizer.NormalizeCanonicalOrder(
                    text.Substring(runStart, runEnd - runStart));
                if (trie.ContainsExactWord(fullWord) && !trie.TryGetWrongFormRule(fullWord, out _))
                    return true;
            }

            return false;
        }

        private static bool IsShieldedOfficialWordSpan(string text, int matchIndex, int matchLength, MyanmarTrie? trie)
        {
            if (trie == null)
                return false;

            int runStart = matchIndex;
            while (runStart > 0 && IsMyanmarRunLetterOrMark(text[runStart - 1]))
                runStart--;

            int runEnd = matchIndex + matchLength;
            while (runEnd < text.Length && IsMyanmarRunLetterOrMark(text[runEnd]))
                runEnd++;

            if (runEnd - runStart <= matchLength)
                return false;

            string fullWord = UnicodeNormalizer.NormalizeCanonicalOrder(
                text.Substring(runStart, runEnd - runStart));
            return trie.TryGetExactEntries(fullWord, out var entries) &&
                   entries != null && entries.Count > 0 &&
                   entries[0].IsOfficialThatpon &&
                   !trie.TryGetWrongFormRule(fullWord, out _);
        }

        private static void AddGrammarOrSpellingIssue(
            List<ProofingIssue> issues,
            int index,
            int length,
            string rawWrong,
            string correct,
            ProofingIssueType issueType,
            string message,
            string meaningCue,
            string posTag,
            bool isOfficialThatpon,
            string reasoning = "",
            string authoritativeSource = "",
            bool requiresReviewBeforeBatchFix = false)
        {
            issues.Add(new ProofingIssue
            {
                StartIndex = index,
                Length = length,
                OriginalText = rawWrong,
                IssueType = issueType,
                Message = message,
                GrammaticalReasoning = reasoning,
                AuthoritativeSource = authoritativeSource,
                RequiresReviewBeforeBatchFix = requiresReviewBeforeBatchFix,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate
                    {
                        Replacement = correct,
                        MeaningCue = meaningCue,
                        PosTag = posTag,
                        IsOfficialThatpon = isOfficialThatpon,
                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(correct),
                        DiagnosticDetail = !string.IsNullOrEmpty(reasoning)
                            ? reasoning
                            : OrthographicDiffExplainer.ExplainDifference(rawWrong, correct),
                        Score = 100.0
                    }
                }
            });
        }

        private static void ScanMisusedKyaungNominalizer(string text, MyanmarTrie? trie, List<ProofingIssue> issues)
        {
            const string target = "ကျောင်း";
            int len = text.Length;
            int searchPos = 0;
            while (searchPos < len)
            {
                int hit = text.IndexOf(target, searchPos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                searchPos = hit + 7;

                int after = hit + 7;
                bool isBoundaryAfter = after >= len || char.IsWhiteSpace(text[after]) || text[after] == '\u104A' || text[after] == '\u104B';
                bool hasReportingPred = HasReportingPredicateAfterKyaung(text, after);
                bool startsWithHuOrPyaaw = MatchesSliceAt(text, after, "ဟု") || MatchesSliceAt(text, after, "ပြော");
                if (!isBoundaryAfter && !hasReportingPred && !startsWithHuOrPyaaw)
                    continue;

                int stemStart = hit;
                while (stemStart > 0 && IsMyanmarRunLetterOrMark(text[stemStart - 1]))
                    stemStart--;
                int stemLen = hit - stemStart;
                if (stemLen < 4 || stemLen > 40)
                    continue;

                if (OverlapsExistingIssue(issues, hit, 7) || IsShieldedOfficialWordSpan(text, hit, 7, trie))
                    continue;

                string stem = text.Substring(stemStart, stemLen);

                bool matchedAspect = false;
                for (int i = 0; i < VerbalAspectBeforeKyaungSuffixes.Length; i++)
                {
                    string sfx = VerbalAspectBeforeKyaungSuffixes[i];
                    int minLen = (sfx == "မရှိ" || sfx == "မဟုတ်") ? sfx.Length : sfx.Length + 2;
                    if (stemLen >= minLen && stem.EndsWith(sfx, StringComparison.Ordinal))
                    {
                        matchedAspect = true;
                        break;
                    }
                }

                if (matchedAspect)
                {
                    if (!HasAnyKnownLexiconSuffixWithEnding(trie, stem, target))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, hit, 7, target, "ကြောင်း",
                            ProofingIssueType.OfficialStyleGrammarError,
                            "ကြိယာနောက်ဆက် အကြောင်းအရာပြပစ္စည်းတွင် နာမ် 'ကျောင်း' (ယပင့်) အစား 'ကြောင်း' (ရရစ်) ကို သုံးနှုန်းရမည်။",
                            "အကြောင်းအရာပြ နာမ်ပြုပစ္စည်း ('~ကြောင်း')", "PART", true,
                            $"ရှေ့တွင် ကြိယာထောက်ပစ္စည်း ('{stem}-') ရှိနေသဖြင့် စာသင်ကျောင်း/ဘုန်းကြီးကျောင်း အနက်မဟုတ်ဘဲ ဖြစ်စဉ်အကြောင်းအရာကို ညွှန်းဆိုသော နာမ်ပြုပစ္စည်း 'ကြောင်း' (ကကြီးရရစ်) ဖြစ်ရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                    }
                    continue;
                }

                if (trie != null && hasReportingPred && stemLen <= 28 &&
                    TryExtractTrailingPureVerbStem(trie, stem, target, checkDirectionalFallGuard: false, out string matchedVerb))
                {
                    AddGrammarOrSpellingIssue(
                        issues, hit, 7, target, "ကြောင်း",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာ '{matchedVerb}-' နောက်တွင် အဆို/သတင်းပြကြိယာ လိုက်သဖြင့် 'ကျောင်း' အစား အကြောင်းအရာပြပစ္စည်း 'ကြောင်း' ဟု ရေးသားရမည်။",
                        $"နာမ်ပြုပစ္စည်း ({matchedVerb}ကြောင်း)", "PART", true,
                        $"'{matchedVerb}-' သည် ကြိယာပုဒ်ဖြစ်ပြီး နောက်တွင် သတင်းပို့/ပြောကြားခြင်းပြ ကြိယာလိုက်နေသဖြင့် 'ကြောင်း' (ရရစ်) ကိုသာ သုံးနှုန်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }
        }

        private static bool HasReportingPredicateAfterKyaung(string text, int afterIdx)
        {
            int len = text.Length;
            int p = afterIdx;
            while (p < len && char.IsWhiteSpace(text[p]))
                p++;
            if (MatchesSliceAt(text, p, "ဟု"))
            {
                p += 2;
                while (p < len && char.IsWhiteSpace(text[p]))
                    p++;
            }
            if (p >= len)
                return false;

            for (int i = 0; i < ReportingPredicatesAfterKyaung.Length; i++)
            {
                if (MatchesSliceAt(text, p, ReportingPredicatesAfterKyaung[i]))
                    return true;
            }
            return false;
        }

        private static void ScanMisusedChinNominalizer(string text, MyanmarTrie trie, List<ProofingIssue> issues)
        {
            const string target = "ချင်း";
            int len = text.Length;
            int searchPos = 0;
            while (searchPos < len)
            {
                int hit = text.IndexOf(target, searchPos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                searchPos = hit + 5;

                int after = hit + 5;
                if (!HasNominalPostpositionAfterChin(text, after))
                    continue;

                int stemStart = hit;
                while (stemStart > 0 && IsMyanmarRunLetterOrMark(text[stemStart - 1]))
                    stemStart--;
                int stemLen = hit - stemStart;
                if (stemLen < 4 || stemLen > 28)
                    continue;

                if (OverlapsExistingIssue(issues, hit, 5) || IsShieldedOfficialWordSpan(text, hit, 5, trie))
                    continue;

                string stem = text.Substring(stemStart, stemLen);
                if (IsReduplicatedTemporalVerbStem(stem))
                    continue;

                if (TryExtractTrailingPureVerbStem(trie, stem, target, checkDirectionalFallGuard: false, out string matchedVerb))
                {
                    AddGrammarOrSpellingIssue(
                        issues, hit, 5, target, "ခြင်း",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာ '{matchedVerb}-' ကို နာမ်ပုဒ်အဖြစ် ပြောင်းလဲရာတွင် 'ချင်း' (ယပင့်) အစား နာမ်ပြုပစ္စည်း 'ခြင်း' (ရရစ် — '{matchedVerb}ခြင်း') ကို သုံးနှုန်းရမည်။",
                        $"ကြိယာနာမ်ပြုပစ္စည်း ({matchedVerb}ခြင်း)", "PART", true,
                        $"နှစ်ခါထပ် ကြိယာ ('ရောက်ရောက်ချင်း/သိသိချင်း') မဟုတ်ဘဲ ကြိယာ '{matchedVerb}-' နောက်၌ ဝိဘတ်/ဖြစ်ခြင်းပြပုဒ် လိုက်နေသဖြင့် ပြုမူဆောင်ရွက်မှုပြ နာမ်ပြုပစ္စည်း 'ခြင်း' (ခကွေးရရစ်) ဖြစ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }
        }

        private static bool HasNominalPostpositionAfterChin(string text, int afterIdx)
        {
            int len = text.Length;
            int p = afterIdx;
            while (p < len && (text[p] == ' ' || text[p] == '\t'))
                p++;
            if (p >= len)
                return false;

            for (int i = 0; i < NominalPostpositionsAfterChin.Length; i++)
            {
                string post = NominalPostpositionsAfterChin[i];
                if (MatchesSliceAt(text, p, post))
                {
                    int end = p + post.Length;
                    if (end >= len)
                        return true;
                    char next = text[end];
                    if (char.IsWhiteSpace(next) || next == '\u104A' || next == '\u104B' ||
                        next == 'ပ' || next == 'သ' || next == 'တ' || next == 'မ' || next == 'ဘ')
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ScanMisusedKyaPluralVerb(string text, MyanmarTrie trie, List<ProofingIssue> issues)
        {
            const string target = "ကျ";
            int len = text.Length;
            int searchPos = 0;
            while (searchPos < len)
            {
                int hit = text.IndexOf(target, searchPos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                searchPos = hit + 2;

                int after = hit + 2;
                if (after >= len)
                    continue;

                // O(1) single-char pre-filter rejects 99.9% of 'ကျ-' words ('ကျင်းပ', 'ကျောင်း', 'ကျေးရွာ', 'ကျော်', 'ကျပ်') in 1 ns
                char cAfter = text[after];
                if (cAfter != 'ပ' && cAfter != 'သ' && cAfter != 'တ' && cAfter != 'မ' && cAfter != '၍' && cAfter != 'က')
                    continue;

                if (!HasPluralVerbCloserAt(text, after))
                    continue;

                int stemStart = hit;
                while (stemStart > 0 && IsMyanmarRunLetterOrMark(text[stemStart - 1]))
                    stemStart--;
                int stemLen = hit - stemStart;
                if (stemLen < 4 || stemLen > 28)
                    continue;

                if (OverlapsExistingIssue(issues, hit, 2) || IsShieldedOfficialWordSpan(text, hit, 2, trie))
                    continue;

                string stem = text.Substring(stemStart, stemLen);
                if (TryExtractTrailingPureVerbStem(trie, stem, target, checkDirectionalFallGuard: true, out string matchedVerb))
                {
                    AddGrammarOrSpellingIssue(
                        issues, hit, 2, target, "ကြ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာ '{matchedVerb}-' နောက်ရှိ အများပြ ကြိယာထောက်ပစ္စည်းတွင် 'ကျ' (ယပင့်) အစား 'ကြ' (ရရစ် — '{matchedVerb}ကြ...') ဟု ရေးသားရမည်။",
                        $"အများပြ ကြိယာထောက်ပစ္စည်း ({matchedVerb}ကြ~)", "PART", true,
                        $"'{matchedVerb}-' သည် အပြုကြိယာပုဒ်ဖြစ်ပြီး နောက်တွင် ဝါကျအဆုံးသတ်ပစ္စည်း လိုက်နေသဖြင့် အများပြပစ္စည်း 'ကြ' (ကကြီးရရစ်) ကိုသာ သုံးနှုန်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }
        }

        private static bool HasPluralVerbCloserAt(string text, int afterIdx)
        {
            int len = text.Length;
            for (int i = 0; i < PluralVerbClosersAfterKya.Length; i++)
            {
                string closer = PluralVerbClosersAfterKya[i];
                if (MatchesSliceAt(text, afterIdx, closer))
                {
                    int end = afterIdx + closer.Length;
                    if (end >= len)
                        return true;
                    char next = text[end];
                    if (char.IsWhiteSpace(next) || next == '\u104A' || next == '\u104B')
                        return true;
                }
            }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool MatchesSliceAt(string text, int start, string literal)
        {
            return start + literal.Length <= text.Length &&
                   text[start] == literal[0] &&
                   string.CompareOrdinal(text, start, literal, 0, literal.Length) == 0;
        }

        private static bool HasAnyKnownLexiconSuffixWithEnding(MyanmarTrie? trie, string rawStem, string ending)
        {
            if (trie == null || string.IsNullOrEmpty(rawStem))
                return false;

            string normFull = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem + ending);
            if (trie.ContainsExactWord(normFull))
                return true;

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(normFull, isPreNormalized: true);
            for (int start = 1; start <= syls.Count - 2; start++)
            {
                string sub = string.Concat(syls.GetRange(start, syls.Count - start));
                if (trie.ContainsExactWord(sub))
                    return true;
            }
            return false;
        }

        private static bool IsPureMultiSyllableVerbInTrie(MyanmarTrie trie, string normCandidate)
        {
            if (string.IsNullOrEmpty(normCandidate) || !trie.TryGetExactEntries(normCandidate, out var entries) || entries == null || entries.Count == 0)
                return false;

            bool hasVerb = false;
            for (int i = 0; i < entries.Count; i++)
            {
                string pos = (entries[i].PosTag ?? string.Empty).Trim().ToUpperInvariant();
                if (pos == "ENCYC" || pos == "PROVERB")
                    continue;
                if (pos == "N" || pos == "N/V" || pos == "PROPER" || pos == "LOAN" || pos == "PART" || pos == "PRON" || pos == "ABB" || pos == "NUM")
                    return false;
                if (pos == "V")
                    hasVerb = true;
            }
            return hasVerb;
        }

        private static bool TryExtractTrailingPureVerbStem(
            MyanmarTrie trie,
            string rawStem,
            string wrongEnding,
            bool checkDirectionalFallGuard,
            out string matchedVerb)
        {
            matchedVerb = string.Empty;
            if (string.IsNullOrEmpty(rawStem))
                return false;

            if (HasAnyKnownLexiconSuffixWithEnding(trie, rawStem, wrongEnding))
                return false;

            string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem);
            string strippedStem = normStem;
            for (int i = 0; i < VerbalAuxStripSuffixes.Length; i++)
            {
                string aux = VerbalAuxStripSuffixes[i];
                if (strippedStem.Length > aux.Length + 3 && strippedStem.EndsWith(aux, StringComparison.Ordinal))
                {
                    strippedStem = strippedStem.Substring(0, strippedStem.Length - aux.Length);
                    break;
                }
            }

            if (checkDirectionalFallGuard)
            {
                for (int i = 0; i < DirectionalFallVerbEndings.Length; i++)
                {
                    if (normStem.EndsWith(DirectionalFallVerbEndings[i], StringComparison.Ordinal) ||
                        strippedStem.EndsWith(DirectionalFallVerbEndings[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(strippedStem, isPreNormalized: true);
            if (syls.Count < 2)
                return false;

            // 1. Direct match on full stripped stem (>= 2 syllables)
            if (IsPureMultiSyllableVerbInTrie(trie, strippedStem))
            {
                matchedVerb = strippedStem;
                return true;
            }

            // 2. Unspaced [Known Lexicon Object/Prefix] + [2-or-3-syllable Pure Verb] (e.g. 'အစည်းအဝေးကျင်းပကျောင်း')
            for (int vLen = Math.Min(3, syls.Count - 1); vLen >= 2; vLen--)
            {
                int split = syls.Count - vLen;
                string prefix = string.Concat(syls.GetRange(0, split));
                string verbTail = string.Concat(syls.GetRange(split, vLen));
                if (trie.ContainsExactWord(prefix) && IsPureMultiSyllableVerbInTrie(trie, verbTail))
                {
                    matchedVerb = verbTail;
                    return true;
                }
            }

            return false;
        }

        private static bool IsReduplicatedTemporalVerbStem(string rawStem)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem);
            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(norm, isPreNormalized: true);
            int n = syls.Count;
            if (n >= 2 && syls[n - 1] == syls[n - 2])
                return true;
            if (n >= 4 && syls[n - 4] == syls[n - 2] && syls[n - 3] == syls[n - 1])
                return true;
            return false;
        }

        private static bool MayContainAdjacentDuplicateMyanmarWord(string text)
        {
            int len = text.Length;
            int prevStart = -1;
            int prevLen = 0;
            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if (c >= '\u1000' && c <= '\u103F')
                {
                    int start = i;
                    i++;
                    while (i < len && text[i] >= '\u1000' && text[i] <= '\u103F')
                        i++;
                    int wLen = i - start;
                    if (prevLen >= 4 && wLen >= prevLen && text[start] == text[prevStart] &&
                        string.CompareOrdinal(text, prevStart, text, start, prevLen) == 0)
                    {
                        return true;
                    }
                    prevStart = start;
                    prevLen = wLen;
                }
                else if (c == ' ' || c == '\t')
                {
                    i++;
                }
                else
                {
                    prevStart = -1;
                    prevLen = 0;
                    i++;
                }
            }
            return false;
        }

        private static bool IsAccidentalDuplicateMyanmarWord(string rawWord, MyanmarTrie? trie)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(rawWord);
            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(norm, isPreNormalized: true);
            if (syls.Count < 2)
                return false;

            // Exempt reduplicated adverbs/ideophones ('ကောင်းကောင်း', 'ဖြည်းဖြည်း') & distributive 'တစ်...' ('တစ်ခု တစ်ခု', 'တစ်ဦး တစ်ဦး')
            if (syls.Count == 2 && syls[0] == syls[1])
                return false;
            if (norm == "တကယ်" || norm == "အမှန်" || norm == "မကြာ" ||
                norm.StartsWith("တစ်", StringComparison.Ordinal) ||
                norm.StartsWith("အသီး", StringComparison.Ordinal) ||
                norm.StartsWith("အမျိုး", StringComparison.Ordinal))
            {
                return false;
            }

            // Exempt 4-syllable lexical reduplications typed with a space (e.g. 'ခဏ ခဏ' -> 'ခဏခဏ')
            if (trie != null && trie.ContainsExactWord(norm + norm))
                return false;

            return true;
        }

        private static bool MayContainAdjacentDuplicateAsciiWord(string text)
        {
            int len = text.Length;
            int prevStart = -1;
            int prevLen = 0;
            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    int start = i;
                    i++;
                    while (i < len && ((text[i] >= 'A' && text[i] <= 'Z') || (text[i] >= 'a' && text[i] <= 'z')))
                        i++;
                    int wLen = i - start;
                    if (wLen >= 2 && wLen <= 4 && wLen == prevLen &&
                        string.Compare(text, prevStart, text, start, wLen, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }
                    prevStart = start;
                    prevLen = wLen;
                }
                else if (c == ' ' || c == '\t')
                {
                    i++;
                }
                else
                {
                    prevStart = -1;
                    prevLen = 0;
                    i++;
                }
            }
            return false;
        }

        private static bool IsClassicalPaliNissayaHeadBeforeComma(string text, int spaceStartIndex, MyanmarTrie? trie)
        {
            if (spaceStartIndex <= 0)
                return false;

            int tokEnd = spaceStartIndex;
            int tokStart = tokEnd;
            while (tokStart > 0 && text[tokStart - 1] >= '\u1000' && text[tokStart - 1] <= '\u103F')
                tokStart--;

            int tokLen = tokEnd - tokStart;
            if (tokLen < 2)
                return false;

            // Must stand at start of paragraph/line or right after sentence boundary (Pali Nissaya '<PaliWord> ၊ <Gloss>')
            int before = tokStart - 1;
            while (before >= 0 && (text[before] == ' ' || text[before] == '\t'))
                before--;
            if (before >= 0 && text[before] != '\u104B' && text[before] != '\r' && text[before] != '\n')
                return false;

            // Pali Nissaya lemmas contain zero Burmese non-kinzi killed codas (\u103A) and zero Burmese creaky/visarga tones (\u1037/\u1038)
            for (int k = tokStart; k < tokEnd; k++)
            {
                char ch = text[k];
                if (ch == '\u1037' || ch == '\u1038')
                    return false;
                if (ch == '\u103A' && (k + 1 >= tokEnd || text[k + 1] != '\u1039'))
                    return false;
            }

            string normTok = UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(tokStart, tokLen));
            if (trie != null && !trie.ContainsExactWord(normTok))
                return true;

            // Also shield classical Pali Nissaya lemmas that exist in the lexicon (e.g. 'ပတိရူပေ ၊ သင့်တင့်သော ဂုဏ်သဘော၌။')
            char last = normTok[normTok.Length - 1];
            if (last == '\u1036' || last == '\u1031')
                return true;
            for (int k = 0; k < normTok.Length; k++)
            {
                char c = normTok[k];
                if (c == '\u1039' || (c >= '\u100B' && c <= '\u100F') || c == '\u1020' || c == '\u103F')
                    return true;
            }
            return trie == null && (last == '\u102D' || last == '\u102F');
        }

        internal static bool IsPrecededByEnglishOrTechnicalToken(string text, int numIndex)
        {
            // Guard paragraph-leading numbered list items like '1. ...' or '1) ...'
            int k = numIndex - 1;
            int openParensSkipped = 0;
            while (k >= 0 && (text[k] == ' ' || text[k] == '\t' || ((text[k] == '(' || text[k] == '（') && openParensSkipped < 1)))
            {
                if (text[k] == '(' || text[k] == '（')
                    openParensSkipped++;
                k--;
            }

            if (k < 0)
                return false;

            char prev = text[k];
            if ((prev >= 'A' && prev <= 'Z') || (prev >= 'a' && prev <= 'z') ||
                (prev >= '0' && prev <= '9') ||
                prev == '-' || prev == '–' || prev == '/' || prev == '.' || prev == ':' || prev == '@' || prev == '#' || prev == '$')
            {
                return true;
            }

            // Walk through immediately preceding non-space token to verify it contains no ASCII Latin/digit characters (e.g. 'Post (2) ခု', 'MP4 3 ခု')
            while (k >= 0 && !char.IsWhiteSpace(text[k]) && text[k] != '\u104A' && text[k] != '\u104B')
            {
                char c = text[k];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return true;
                k--;
            }

            return false;
        }

        internal static string ConvertAsciiDigitsToMyanmar(string asciiNum)
        {
            char[] buf = asciiNum.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] >= '0' && buf[i] <= '9')
                    buf[i] = (char)('\u1040' + (buf[i] - '0'));
            }
            return new string(buf);
        }

        [ThreadStatic]
        private static (char Ch, int Index)[]? t_bracketStack;

        private static void AnalyzeUnpairedBracketsAndQuotes(string text, List<ProofingIssue> issues)
        {
            int len = text.Length;
            var bracketStack = t_bracketStack ??= new (char Ch, int Index)[32];
            int stackCount = 0;

            int i = 0;
            while (i < len)
            {
                char c = text[i];

                // Skip URLs ('http://...', 'https://...', 'www....') so Wikipedia/web URLs with parens never false-trigger
                if ((c == 'h' || c == 'H' || c == 'w' || c == 'W') && IsUrlStartAt(text, i, out int urlEnd))
                {
                    i = urlEnd;
                    continue;
                }

                if (c == '(' || c == '[' || c == '（' || c == '【')
                {
                    if (stackCount < bracketStack.Length)
                    {
                        bracketStack[stackCount++] = (c, i);
                    }
                }
                else if (c == ')' || c == ']' || c == '）' || c == '】')
                {
                    if (stackCount == 0)
                    {
                        if ((c == ')' || c == '）') && IsStandaloneListMarkerOrEmoticonCloseParen(text, i))
                        {
                            i++;
                            continue;
                        }

                        if (!OverlapsExistingIssue(issues, i, 1))
                        {
                            EmitUnmatchedCloseBracketIssue(text, i, c, issues);
                        }
                    }
                    else
                    {
                        var (openCh, openIdx) = bracketStack[--stackCount];
                        if (!AreMatchingBrackets(openCh, c) && !IsHalfOpenMathInterval(text, openIdx, i, openCh, c))
                        {
                            char expectedClose = GetMatchingCloseBracket(openCh);
                            if (!OverlapsExistingIssue(issues, i, 1))
                            {
                                AddGrammarOrSpellingIssue(
                                    issues, i, 1, c.ToString(), expectedClose.ToString(),
                                    ProofingIssueType.PunctuationError,
                                    $"လက်သည်းကွင်း အဖွင့် '{openCh}' နှင့် အပိတ် '{c}' မကိုက်ညီပါ — '{expectedClose}' ဖြင့် ပိတ်ရမည်။",
                                    $"ကိုက်ညီသော အပိတ်သင်္ကေတ ('{expectedClose}')", "PUNCT", true,
                                    $"ရှေ့တွင် '{openCh}' ဖြင့် ဖွင့်ထားသဖြင့် တူညီသော အပိတ်သင်္ကေတ '{expectedClose}' ကိုသာ သုံးနှုန်းရမည်။",
                                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                    requiresReviewBeforeBatchFix: true);
                            }
                        }
                    }
                }
                i++;
            }

            for (int s = 0; s < stackCount; s++)
            {
                var (openCh, openIdx) = bracketStack[s];
                if (!OverlapsExistingIssue(issues, openIdx, 1))
                {
                    EmitUnclosedOpenBracketIssue(text, openIdx, openCh, issues);
                }
            }

            AnalyzeCurlyAndMismatchedQuotes(text, issues);
        }

        private static void EmitUnmatchedCloseBracketIssue(string text, int closeIdx, char closeCh, List<ProofingIssue> issues)
        {
            char expectedOpen = GetMatchingOpenBracket(closeCh);
            if (closeIdx > 0 && text[closeIdx - 1] == closeCh && !OverlapsExistingIssue(issues, closeIdx - 1, 2))
            {
                string dup = new string(closeCh, 2);
                AddGrammarOrSpellingIssue(
                    issues, closeIdx - 1, 2, dup, closeCh.ToString(),
                    ProofingIssueType.PunctuationError,
                    $"အပိတ်လက်သည်းကွင်း '{dup}' နှစ်ထပ်ရိုက်မိနေပါသည် — '{closeCh}' တစ်ခုသာ ထားရှိရမည်။",
                    $"နှစ်ထပ် '{closeCh}' ဖယ်ရှားရန်", "PUNCT", true,
                    $"အဖွင့် '{expectedOpen}' တစ်ခုတည်းသာရှိသဖြင့် အပိတ် '{closeCh}' တစ်ခုပိုနေပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
                return;
            }

            if (closeIdx > 0 && !char.IsWhiteSpace(text[closeIdx - 1]) && !OverlapsExistingIssue(issues, closeIdx - 1, 2))
            {
                string span = text.Substring(closeIdx - 1, 2);
                string withoutClose = text[closeIdx - 1].ToString();
                AddGrammarOrSpellingIssue(
                    issues, closeIdx - 1, 2, span, withoutClose,
                    ProofingIssueType.PunctuationError,
                    $"ရှေ့တွင် အဖွင့် '{expectedOpen}' မပါဘဲ အပိတ် '{closeCh}' တစ်ခုတည်း ဖြစ်နေပါသည် — စိစစ်ပြင်ဆင်ပါ။",
                    $"ပိုနေသော '{closeCh}' ဖယ်ရှားရန်", "PUNCT", true,
                    $"ဤစာပိုဒ်အတွင်း အဖွင့်လက်သည်းကွင်း '{expectedOpen}' မတွေ့ရှိရဘဲ အပိတ် '{closeCh}' ပါဝင်နေပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
        }

        private static bool EndsWithEllipsisTruncation(string text, int limitExclusive)
        {
            int idx = Math.Min(limitExclusive, text.Length);
            while (idx > 0 && (char.IsWhiteSpace(text[idx - 1]) || text[idx - 1] == '\u104B' || text[idx - 1] == '\uFFFD'))
                idx--;

            if (idx <= 0)
                return false;

            if (text[idx - 1] == '\u2026')
                return true;

            if (idx >= 2 && text[idx - 1] == '.' && text[idx - 2] == '.')
                return true;

            return false;
        }

        private static bool HasEllipsisTruncationAfterOpen(string text, int openIdx, int limitExclusive)
        {
            if (EndsWithEllipsisTruncation(text, limitExclusive))
                return true;

            int searchLen = limitExclusive - (openIdx + 1);
            if (searchLen >= 1 && text.IndexOf('\u2026', openIdx + 1, searchLen) >= 0)
                return true;

            if (searchLen >= 2 && text.IndexOf("..", openIdx + 1, searchLen, StringComparison.Ordinal) >= 0)
                return true;

            return false;
        }

        private static void EmitUnclosedOpenBracketIssue(string text, int openIdx, char openCh, List<ProofingIssue> issues)
        {
            if (HasEllipsisTruncationAfterOpen(text, openIdx, text.Length))
                return;

            char expectedClose = GetMatchingCloseBracket(openCh);
            int len = text.Length;

            // Find clean natural end before trailing ။ or newline so user gets a 1-click close option while staying unchecked by default
            int endLimit = len;
            while (endLimit > openIdx + 1 && (char.IsWhiteSpace(text[endLimit - 1]) || text[endLimit - 1] == '\u104B'))
                endLimit--;

            int spanLen = endLimit - openIdx;
            if (spanLen >= 2 && spanLen <= 48 && !OverlapsExistingIssue(issues, openIdx, spanLen))
            {
                string rawSpan = text.Substring(openIdx, spanLen);
                string closedSpan = rawSpan + expectedClose;
                AddGrammarOrSpellingIssue(
                    issues, openIdx, spanLen, rawSpan, closedSpan,
                    ProofingIssueType.PunctuationError,
                    $"လက်သည်းကွင်း အဖွင့် '{openCh}' ကို ပြန်မပိတ်ရသေးပါ — အပိတ် '{expectedClose}' ထည့်သွင်းရန် စိစစ်ပါ။",
                    $"အပိတ် '{expectedClose}' ထည့်သွင်းရန်", "PUNCT", true,
                    $"ဤစာပိုဒ်အတွင်း လက်သည်းကွင်း '{openCh}' ဖွင့်ထားသော်လည်း သက်ဆိုင်ရာ အပိတ် '{expectedClose}' ကျန်ခဲ့ပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
            else if (openIdx + 1 < len && !char.IsWhiteSpace(text[openIdx + 1]) && !OverlapsExistingIssue(issues, openIdx, 2))
            {
                string rawPair = text.Substring(openIdx, 2);
                string stripped = text[openIdx + 1].ToString();
                AddGrammarOrSpellingIssue(
                    issues, openIdx, 2, rawPair, stripped,
                    ProofingIssueType.PunctuationError,
                    $"လက်သည်းကွင်း အဖွင့် '{openCh}' အတွက် အပိတ် '{expectedClose}' မတွေ့ရှိရပါ — စိစစ်ပြင်ဆင်ပါ။",
                    $"ပိုနေသော '{openCh}' ဖယ်ရှားရန် (သို့မဟုတ် '{expectedClose}' ပိတ်ရန်)", "PUNCT", true,
                    $"ဤစာပိုဒ်အတွင်း လက်သည်းကွင်း '{openCh}' ဖွင့်ထားသော်လည်း အပိတ် '{expectedClose}' မပါရှိသေးပါ။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
        }

        private static void AnalyzeCurlyAndMismatchedQuotes(string text, List<ProofingIssue> issues)
        {
            int len = text.Length;
            var openCurlyStack = new List<int>(4);
            int openDoubleSingleIdx = -1;
            int openStraightBeforeMmIdx = -1;

            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                {
                    openCurlyStack.Clear();
                    openDoubleSingleIdx = -1;
                    openStraightBeforeMmIdx = -1;
                    i++;
                    continue;
                }

                // Recognize double single-quotes ‘‘...’’ typed instead of “...”
                if (c == '\u2018' && i + 1 < len && text[i + 1] == '\u2018')
                {
                    openDoubleSingleIdx = i;
                    i += 2;
                    continue;
                }
                if (c == '\u2019' && i + 1 < len && text[i + 1] == '\u2019')
                {
                    openDoubleSingleIdx = -1;
                    if (openCurlyStack.Count > 0)
                        openCurlyStack.RemoveAt(openCurlyStack.Count - 1);
                    i += 2;
                    continue;
                }

                if (c == '\u201C') // “ (supports nested quotes “... “...” ...” cleanly)
                {
                    openCurlyStack.Add(i);
                    openStraightBeforeMmIdx = -1;
                }
                else if (c == '\u201D') // ”
                {
                    if (openCurlyStack.Count > 0)
                    {
                        openCurlyStack.RemoveAt(openCurlyStack.Count - 1);
                    }
                    else if (openDoubleSingleIdx >= 0)
                    {
                        openDoubleSingleIdx = -1;
                    }
                    else if (openStraightBeforeMmIdx >= 0)
                    {
                        // Opened with straight ASCII " and closed with curly ” -> offer converting opening " to “
                        int sIdx = openStraightBeforeMmIdx;
                        openStraightBeforeMmIdx = -1;
                        if (!OverlapsExistingIssue(issues, sIdx, 1))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, sIdx, 1, "\"", "\u201C",
                                ProofingIssueType.PunctuationError,
                                "မျက်တောင်အဖွင့် '\"' နှင့် အပိတ် '”' ပုံစံမတူဖြစ်နေပါသည် — အဖွင့် '“' ဖြင့် တစ်ပြေးညီ ရေးသားသင့်ပါသည်။",
                                "အဖွင့်မျက်တောင် ('“')", "PUNCT", true,
                                "အပိတ်တွင် ကွေးသောမျက်တောင် ('”' U+201D) သုံးထားသဖြင့် အဖွင့်၌လည်း '“' (U+201C) ကို တစ်ပြေးညီ သုံးနှုန်းရမည်။",
                                "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                requiresReviewBeforeBatchFix: true);
                        }
                    }
                    else if (!IsInchOrArcsecondSymbol(text, i))
                    {
                        if (i > 0 && !char.IsWhiteSpace(text[i - 1]) && !OverlapsExistingIssue(issues, i - 1, 2))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, i - 1, 2, text.Substring(i - 1, 2), text[i - 1].ToString(),
                                ProofingIssueType.PunctuationError,
                                "ရှေ့တွင် အဖွင့်မျက်တောင် '“' မပါဘဲ အပိတ်မျက်တောင် '”' တစ်ခုတည်း ဖြစ်နေပါသည် — စိစစ်ပြင်ဆင်ပါ။",
                                "ပိုနေသော '”' ဖယ်ရှားရန်", "PUNCT", true,
                                "ဤစာပိုဒ်အတွင်း အဖွင့်မျက်တောင် '“' မပါရှိဘဲ အပိတ်မျက်တောင် '”' သီးသန့်ဖြစ်နေပါသည်။",
                                "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                requiresReviewBeforeBatchFix: true);
                        }
                    }
                }
                else if (c == '"')
                {
                    int topCurlyIdx = openCurlyStack.Count > 0 ? openCurlyStack[openCurlyStack.Count - 1] : -1;
                    if (topCurlyIdx >= 0 && i > topCurlyIdx + 1 && !char.IsWhiteSpace(text[i - 1]) && !IsInchOrArcsecondSymbol(text, i))
                    {
                        // Opened with curly “ and closed with straight ASCII " -> offer converting " to ”
                        openCurlyStack.RemoveAt(openCurlyStack.Count - 1);
                        if (!OverlapsExistingIssue(issues, i, 1))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, i, 1, "\"", "\u201D",
                                ProofingIssueType.PunctuationError,
                                "အဖွင့်မျက်တောင် '“' နှင့် ကိုက်ညီစေရန် အပိတ်၌ '\"' အစား '”' (အပိတ်မျက်တောင်) သုံးနှုန်းရမည်။",
                                "အပိတ်မျက်တောင် ('”')", "PUNCT", true,
                                "ရှေ့တွင် ကွေးသောအဖွင့်မျက်တောင် ('“' U+201C) သုံးထားသဖြင့် အပိတ်၌လည်း '”' (U+201D) ဖြင့် ပိတ်ရမည်။",
                                "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                requiresReviewBeforeBatchFix: true);
                        }
                    }
                    else if (openStraightBeforeMmIdx >= 0)
                    {
                        // Balanced straight quotes "..." -> close cleanly
                        openStraightBeforeMmIdx = -1;
                    }
                    else if (openCurlyStack.Count == 0 &&
                             !IsInchOrArcsecondSymbol(text, i) &&
                             (i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '(' || text[i - 1] == '[' ||
                              text[i - 1] == '\u104A' || text[i - 1] == '\u104B' || text[i - 1] == ':') &&
                             i + 1 < len &&
                             (IsMyanmarRunLetterOrMark(text[i + 1]) || char.IsLetterOrDigit(text[i + 1])))
                    {
                        openStraightBeforeMmIdx = i;
                    }
                }

                i++;
            }

            for (int q = 0; q < openCurlyStack.Count; q++)
            {
                EmitUnclosedOpenQuoteIssue(text, openCurlyStack[q], len, issues);
            }
        }

        private static void EmitUnclosedOpenQuoteIssue(string text, int openIdx, int limitExclusive, List<ProofingIssue> issues)
        {
            if (HasEllipsisTruncationAfterOpen(text, openIdx, limitExclusive))
                return;

            int endPos = limitExclusive;
            while (endPos > openIdx + 1 && char.IsWhiteSpace(text[endPos - 1]))
                endPos--;

            // Place closing ” right before trailing ။ if sentence ends with ။, or at endPos
            int insertBeforePaukMa = endPos;
            if (insertBeforePaukMa > openIdx + 1 && text[insertBeforePaukMa - 1] == '\u104B')
                insertBeforePaukMa--;

            int spanLen = insertBeforePaukMa - openIdx;
            if (spanLen >= 2 && spanLen <= 64 && !OverlapsExistingIssue(issues, openIdx, spanLen))
            {
                string rawSpan = text.Substring(openIdx, spanLen);
                string closedSpan = rawSpan + "\u201D";
                AddGrammarOrSpellingIssue(
                    issues, openIdx, spanLen, rawSpan, closedSpan,
                    ProofingIssueType.PunctuationError,
                    "အဖွင့်မျက်တောင် '“' ကို ပြန်မပိတ်ရသေးပါ — အပိတ်မျက်တောင် '”' ထည့်သွင်းရန် စိစစ်ပါ။",
                    "အပိတ်မျက်တောင် ('”') ထည့်သွင်းရန်", "PUNCT", true,
                    "ဤစာပိုဒ်အတွင်း မျက်တောင်အဖွင့် ('“' U+201C) ရှိသော်လည်း အပိတ်မျက်တောင် ('”' U+201D) ကျန်ခဲ့ပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
            else if (openIdx + 1 < text.Length && !char.IsWhiteSpace(text[openIdx + 1]) && !OverlapsExistingIssue(issues, openIdx, 2))
            {
                AddGrammarOrSpellingIssue(
                    issues, openIdx, 2, text.Substring(openIdx, 2), text[openIdx + 1].ToString(),
                    ProofingIssueType.PunctuationError,
                    "အဖွင့်မျက်တောင် '“' အတွက် အပိတ်မျက်တောင် '”' မတွေ့ရှိရပါ — စိစစ်ပြင်ဆင်ပါ။",
                    "ပိုနေသော '“' ဖယ်ရှားရန် (သို့မဟုတ် '”' ပိတ်ရန်)", "PUNCT", true,
                    "ဤစာပိုဒ်အတွင်း မျက်တောင်အဖွင့် ('“') ရှိသော်လည်း အပိတ်မျက်တောင် ('”') မပါရှိသေးပါ။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
        }

        private static bool IsInchOrArcsecondSymbol(string text, int quoteIdx)
        {
            if (quoteIdx <= 0)
                return false;
            char prev = text[quoteIdx - 1];
            return (prev >= '0' && prev <= '9') || (prev >= '\u1040' && prev <= '\u1049');
        }

        private static bool IsUrlStartAt(string text, int idx, out int urlEnd)
        {
            urlEnd = idx;
            int rem = text.Length - idx;
            if (rem < 8)
                return false;

            bool isUrl = string.Compare(text, idx, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(text, idx, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(text, idx, "www.", 0, 4, StringComparison.OrdinalIgnoreCase) == 0;
            if (!isUrl)
                return false;

            int k = idx + 4;
            while (k < text.Length && !char.IsWhiteSpace(text[k]) && text[k] != '\u104A' && text[k] != '\u104B')
                k++;
            urlEnd = k;
            return true;
        }

        private static bool AreMatchingBrackets(char open, char close)
        {
            return (open == '(' && (close == ')' || close == '）')) ||
                   (open == '（' && (close == '）' || close == ')')) ||
                   (open == '[' && close == ']') ||
                   (open == '【' && close == '】');
        }

        private static char GetMatchingCloseBracket(char open) => open switch
        {
            '(' => ')',
            '（' => '）',
            '[' => ']',
            '【' => '】',
            _ => ')'
        };

        private static char GetMatchingOpenBracket(char close) => close switch
        {
            ')' => '(',
            '）' => '（',
            ']' => '[',
            '】' => '【',
            _ => '('
        };

        private static bool IsHalfOpenMathInterval(string text, int openIdx, int closeIdx, char openCh, char closeCh)
        {
            if (!((openCh == '[' && closeCh == ')') || (openCh == '(' && closeCh == ']')))
                return false;
            int len = closeIdx - openIdx - 1;
            if (len < 3 || len > 24 || text.IndexOf(',', openIdx + 1, len) < 0)
                return false;
            for (int k = openIdx + 1; k < closeIdx; k++)
            {
                char ch = text[k];
                if (!((ch >= '0' && ch <= '9') || (ch >= '\u1040' && ch <= '\u1049') ||
                      ch == ',' || ch == '.' || ch == '-' || ch == '+' || ch == ' ' || ch == '∞'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsStandaloneListMarkerOrEmoticonCloseParen(string text, int closeIdx)
        {
            int k = closeIdx - 1;
            if (k < 0)
                return false;

            char prev = text[k];
            // Emoticons ':)', ';)', '=)'
            if (prev == ':' || prev == ';' || prev == '=')
                return true;

            // Digits 1) .. 999) or ၁) .. ၉၉၉)
            if ((prev >= '0' && prev <= '9') || (prev >= '\u1040' && prev <= '\u1049'))
            {
                int start = k;
                while (start > 0 && (k - start) < 3 &&
                       ((text[start - 1] >= '0' && text[start - 1] <= '9') || (text[start - 1] >= '\u1040' && text[start - 1] <= '\u1049')))
                {
                    start--;
                }
                return IsListMarkerLeftBoundary(text, start);
            }

            // Single ASCII letter a), b) or short Roman numeral i), ii), iii), iv), v), vi)..x)
            if ((prev >= 'A' && prev <= 'Z') || (prev >= 'a' && prev <= 'z'))
            {
                int start = k;
                while (start > 0 && (k - start) < 3 && IsRomanNumeralChar(text[start - 1]))
                {
                    start--;
                }
                if (start == k)
                    return IsListMarkerLeftBoundary(text, start);

                string roman = text.Substring(start, k - start + 1).ToLowerInvariant();
                if (roman == "ii" || roman == "iii" || roman == "iv" || roman == "vi" || roman == "vii" || roman == "viii" || roman == "ix")
                    return IsListMarkerLeftBoundary(text, start);
                return false;
            }

            // Single Myanmar consonant list marker က), ခ), ဂ), ဃ), င)
            if (prev >= '\u1000' && prev <= '\u1021')
            {
                return IsListMarkerLeftBoundary(text, k);
            }

            return false;
        }

        private static bool IsRomanNumeralChar(char c)
        {
            char lo = char.ToLowerInvariant(c);
            return lo == 'i' || lo == 'v' || lo == 'x';
        }

        private static bool IsListMarkerLeftBoundary(string text, int markerStartIdx)
        {
            if (markerStartIdx <= 0)
                return true;
            char left = text[markerStartIdx - 1];
            return char.IsWhiteSpace(left) || left == '\u104A' || left == '\u104B' || left == ':' || left == ';' || left == '—';
        }

        private static List<(int Start, int End)>? ComputeBalancedQuoteRanges(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            bool hasAnyQuoteChar = false;
            int len = text.Length;
            for (int k = 0; k < len; k++)
            {
                char c = text[k];
                if (c == '\u201C' || c == '"' || c == '\u2018')
                {
                    hasAnyQuoteChar = true;
                    break;
                }
            }
            if (!hasAnyQuoteChar)
                return null;

            List<(int Start, int End)>? ranges = null;
            int i = 0;
            while (i < len)
            {
                char ch = text[i];
                char expectedClose = '\0';
                int maxSpan = 0;
                if (ch == '\u201C') { expectedClose = '\u201D'; maxSpan = 800; }
                else if (ch == '"') { expectedClose = '"'; maxSpan = 600; }
                else if (ch == '\u2018') { expectedClose = '\u2019'; maxSpan = 400; }

                if (expectedClose != '\0')
                {
                    int limit = Math.Min(len, i + maxSpan);
                    int closeIdx = -1;
                    for (int j = i + 1; j < limit; j++)
                    {
                        char cj = text[j];
                        if (cj == '\r' || cj == '\n')
                            break;
                        if (cj == expectedClose)
                        {
                            closeIdx = j;
                            break;
                        }
                    }

                    if (closeIdx > i + 1)
                    {
                        ranges ??= new List<(int Start, int End)>(4);
                        ranges.Add((i + 1, closeIdx));
                        i = closeIdx + 1;
                        continue;
                    }
                }
                i++;
            }

            return ranges;
        }

        private static bool IsInsideAnyRange(List<(int Start, int End)>? ranges, int index, int length)
        {
            if (ranges == null)
                return false;
            int end = index + length;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (index >= r.Start && end <= r.End)
                    return true;
            }
            return false;
        }

        private static readonly string[] ClauseChainLinkers =
        {
            "သောကြောင့်", "သဖြင့်", "သော်လည်း", "ပေမဲ့", "ပေမယ့်", "ပြီးနောက်", "ပြီးမှ", "ပြီးလျှင်", "ပြီးတော့", "ပြီး", "လို့", "၍", "ရင်", "လျှင်"
        };

        private static string TrimClauseAfterConjunction(string clause)
        {
            if (string.IsNullOrEmpty(clause))
                return string.Empty;

            int cut = -1;
            for (int i = 0; i < ClauseChainLinkers.Length; i++)
            {
                string lk = ClauseChainLinkers[i];
                int hit = clause.LastIndexOf(lk, StringComparison.Ordinal);
                if (hit >= 0)
                {
                    int after = hit + lk.Length;
                    if (after >= clause.Length || char.IsWhiteSpace(clause[after]))
                    {
                        if (after > cut)
                            cut = after;
                    }
                }
            }
            return cut > 0 ? clause.Substring(cut) : clause;
        }

        private static bool IsKnownStemOrSuffixedWord(string stem, MyanmarTrie? trie)
        {
            if (string.IsNullOrEmpty(stem) || stem.Length < 2)
                return false;
            if (trie == null)
                return true;
            if (trie.ContainsExactWord(stem))
                return true;

            string[] pluralSuffixes = { "များ", "တွေ", "တို့" };
            for (int i = 0; i < pluralSuffixes.Length; i++)
            {
                string sfx = pluralSuffixes[i];
                if (stem.Length > sfx.Length + 1 && stem.EndsWith(sfx, StringComparison.Ordinal))
                {
                    string root = stem.Substring(0, stem.Length - sfx.Length);
                    if (trie.ContainsExactWord(root))
                        return true;
                }
            }
            return false;
        }

        private static bool IsKnownVerbOrAuxStem(string stem, MyanmarTrie? trie)
        {
            if (string.IsNullOrEmpty(stem))
                return false;
            string[] auxEndings = { "နေ", "ခဲ့", "ပြီး", "နိုင်", "ချင်", "တတ်", "သင့်", "ရ", "ပါ", "သွား", "လာ", "ထား", "ပေး", "များ" };
            for (int i = 0; i < auxEndings.Length; i++)
            {
                string aux = auxEndings[i];
                if (stem.Length > aux.Length && stem.EndsWith(aux, StringComparison.Ordinal))
                {
                    string baseVerb = stem.Substring(0, stem.Length - aux.Length);
                    if (trie == null || trie.ContainsExactWord(baseVerb) || baseVerb.StartsWith("မ", StringComparison.Ordinal))
                        return true;
                }
            }
            if (trie != null)
            {
                return trie.HasPosTag(stem, "V") || trie.HasPosTag(stem, "ADJ");
            }
            return true;
        }

        private void ScanUnspacedAttachedParticleTypos(
            string text,
            ulong mmMask,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie,
            List<ProofingIssue> issues)
        {
            bool isFormal = styleMode == ProofingStyleMode.FormalOfficial;

            // 1. '[Valid Noun/Place/Pronoun] + မာ' -> 'တွင် / မှာ' (FormalOfficial) or 'မှာ / တွင်' (GeneralStandard)
            if (ContainsMm(text, mmMask, "မာ"))
            {
                foreach (Match m in UnspacedAttachedMarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "မာ";
                    if (trie != null && trie.ContainsExactWord(fullJoined))
                        continue;
                    if (!IsKnownStemOrSuffixedWord(stem, trie))
                        continue;

                    string p1 = isFormal ? "တွင်" : "မှာ";
                    string p2 = isFormal ? "မှာ" : "တွင်";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"တည်နေရာပြဝိဘတ်တွင် ဟထိုး ('ှ') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ တည်နေရာပြဝိဘတ် ('~တွင်')" : "တည်နေရာပြဝိဘတ် စာလုံးပေါင်းအမှန် ('~မှာ')",
                        isFormal ? "အပြောစကား တည်နေရာပြဝိဘတ် ('~မှာ')" : "ရုံးသုံး/စာပေ တည်နေရာပြဝိဘတ် ('~တွင်')");
                }
            }

            // 2. '[Valid Noun/Pronoun] + နဲ' -> 'နှင့် / နဲ့' (FormalOfficial) or 'နဲ့ / နှင့်' (GeneralStandard)
            if (ContainsMm(text, mmMask, "နဲ"))
            {
                foreach (Match m in UnspacedAttachedNaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "နဲ";
                    if (stem.EndsWith("ခ", StringComparison.Ordinal) ||
                        stem.EndsWith("မ", StringComparison.Ordinal) ||
                        (trie != null && trie.ContainsExactWord(fullJoined)))
                    {
                        continue;
                    }
                    if (!IsKnownStemOrSuffixedWord(stem, trie))
                        continue;

                    string p1 = isFormal ? "နှင့်" : "နဲ့";
                    string p2 = isFormal ? "နဲ့" : "နှင့်";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"အတူပြ/သမ္ဗန္ဓဝိဘတ်တွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ သမ္ဗန္ဓဝိဘတ် ('~နှင့်')" : "အပြောစကား သမ္ဗန္ဓဝိဘတ်အမှန် ('~နဲ့')",
                        isFormal ? "အပြောစကား သမ္ဗန္ဓဝိဘတ်အမှန် ('~နဲ့')" : "ရုံးသုံး/စာပေ သမ္ဗန္ဓဝိဘတ် ('~နှင့်')");
                }
            }

            // 3. '[Valid Verb/Aux] + တဲ' before Noun -> 'သည့် / တဲ့' (FormalOfficial) or 'တဲ့ / သည့်' (GeneralStandard)
            if (ContainsMm(text, mmMask, "တဲ"))
            {
                foreach (Match m in UnspacedAttachedTaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "တဲ";
                    if (trie != null && trie.ContainsExactWord(fullJoined))
                        continue;
                    if (!IsKnownVerbOrAuxStem(stem, trie))
                        continue;

                    string p1 = isFormal ? "သည့်" : "တဲ့";
                    string p2 = isFormal ? "တဲ့" : "သည့်";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"နာမဝိသေသနပြုပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ နာမဝိသေသနပစ္စည်း ('~သည့်')" : "အပြောစကား နာမဝိသေသနပစ္စည်းအမှန် ('~တဲ့')",
                        isFormal ? "အပြောစကား နာမဝိသေသနပစ္စည်းအမှန် ('~တဲ့')" : "ရုံးသုံး/စာပေ နာမဝိသေသနပစ္စည်း ('~သည့်')");
                }
            }

            // 4. '[Multi-Syllable Noun / Plural] + ရဲ' before Noun -> '၏ / ရဲ့' (FormalOfficial) or 'ရဲ့ / ၏' (GeneralStandard)
            if (ContainsMm(text, mmMask, "ရဲ"))
            {
                foreach (Match m in UnspacedAttachedYaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "ရဲ";
                    if (stem.EndsWith("နိုင်ငံ", StringComparison.Ordinal) ||
                        stem.EndsWith("ပြည်နယ်", StringComparison.Ordinal) ||
                        stem.EndsWith("မြို့နယ်", StringComparison.Ordinal) ||
                        (trie != null && trie.ContainsExactWord(fullJoined)))
                    {
                        continue;
                    }
                    bool hasPlural = stem.EndsWith("များ", StringComparison.Ordinal) ||
                                     stem.EndsWith("တွေ", StringComparison.Ordinal) ||
                                     stem.EndsWith("တို့", StringComparison.Ordinal);
                    if (!hasPlural && (trie == null || !trie.HasPosTag(stem, "N") || MyanmarSyllableBreaker.BreakIntoSyllables(stem, isPreNormalized: true).Count < 2))
                        continue;

                    string p1 = isFormal ? "၏" : "ရဲ့";
                    string p2 = isFormal ? "ရဲ့" : "၏";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"ပိုင်ဆိုင်ပြဝိဘတ်တွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ ပိုင်ဆိုင်ပြဝိဘတ် ('~၏')" : "အပြောစကား ပိုင်ဆိုင်ပြဝိဘတ်အမှန် ('~ရဲ့')",
                        isFormal ? "အပြောစကား ပိုင်ဆိုင်ပြဝိဘတ်အမှန် ('~ရဲ့')" : "ရုံးသုံး/စာပေ ပိုင်ဆိုင်ပြဝိဘတ် ('~၏')");
                }
            }
        }

        private static void AddModeAwareAttachedParticleIssue(
            List<ProofingIssue> issues,
            int index,
            int length,
            string raw,
            string primary,
            string secondary,
            bool isFormal,
            string message,
            string primaryCue,
            string secondaryCue)
        {
            issues.Add(new ProofingIssue
            {
                StartIndex = index,
                Length = length,
                OriginalText = raw,
                IssueType = ProofingIssueType.SpellingError,
                Message = message,
                GrammaticalReasoning = $"ရှေ့နာမ်/ကြိယာပုဒ်နောက်တွင် တစ်ဆက်တည်း တွဲရိုက်ထားသော ဝိဘတ် '{raw}' စာလုံးပေါင်း ချို့ယွင်းနေသဖြင့် '{primary}' (သို့မဟုတ် '{secondary}') ဖြင့် ရေးသားရမည်။",
                AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် စာလုံးပေါင်းစံနှုန်း)",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate
                    {
                        Replacement = primary,
                        MeaningCue = primaryCue,
                        PosTag = "PART",
                        IsOfficialThatpon = isFormal,
                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primary),
                        Score = 100.0
                    },
                    new SuggestionCandidate
                    {
                        Replacement = secondary,
                        MeaningCue = secondaryCue,
                        PosTag = "PART",
                        IsOfficialThatpon = !isFormal,
                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(secondary),
                        Score = 95.0
                    }
                }
            });
        }

        private static readonly Regex OrphanSpaceDetachedCodaPattern = new Regex(
            @"(?<Prefix>[\u1000-\u103F]{0,14}?)(?<OpenSyl>[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032\u1036]*)[ \t]+(?<Coda>[\u1000-\u1021]\u103A[\u1037\u1038]?|[\u1004\u1014\u1019\u100A\u101A]\u1037\u103A)(?![\u102B-\u1039\u103B-\u103E])",
            RegexOptions.Compiled);

        private static readonly Regex BrokenViramaStackAcrossSpacePattern = new Regex(
            @"(?<Left>[\u1000-\u103F]*?[\u1000-\u1021]\u1039)[ \t]+(?<Right>[\u1000-\u1021][\u1000-\u103F]{0,14})");

        private static readonly Regex ImpossibleSyllableVowelMedialPattern = new Regex(
            @"(?:(?<ShortIVisarga>[\u1000-\u1021][\u103B-\u103E]*\u102D\u1038)|(?<DoubleMedial>[\u1000-\u1021](?:\u103B\u103C|\u103C\u103B)[\u102B-\u103E]*[\u1000-\u1021]?\u103A?[\u1037\u1038]?)|(?<MissingAsatVisarga>[\u1000-\u1021][\u103B-\u103E]*[\u1004\u1014\u1019\u101A]\u1038)|(?<=နိုင်ငံ|ကမ္ဘာ့|ဒေသ|ပြည်နယ်|မြို့နယ်)(?<TaWun>တဝန်း)(?=ရှိ|၌|တွင်|မှာ|လုံး))");

        private static readonly Regex RedundantDoubleToneSyllablePattern = new Regex(
            @"(?<Syl>[\u1000-\u102A][\u103B-\u103E]*[\u102B-\u1036]*(?:[\u1000-\u1021]\u103A)?(?:\u1038\u1037|\u1037\u1038))");

        private static readonly Regex CorruptedZawgyiAwNasalCodaPattern = new Regex(
            @"(?<Syl>[\u1000-\u1021][\u103B-\u103E]*\u1031[\u102B\u102C]\u1037[\u1004\u1014\u1019]\u103A\u1038)");

        private static readonly Regex CorruptedGluedMultiAsciiAPrefixPattern = new Regex(
            @"(?<![A-Za-z0-9\u1000-\u103F])A(?:[ \t]+[\u1000-\u1021][\u1000-\u103F]*|[\u102B-\u103E][\u1000-\u103F]*)(?:A(?:[ \t]+[\u1000-\u1021][\u1000-\u103F]*|[\u102B-\u103E][\u1000-\u103F]*))+(?![A-Za-z])");

        private static readonly Regex ReplaceAsciiASegmentRegex = new Regex(
            @"A(?:[ \t]+(?=[\u1000-\u1021])|(?=[\u102B-\u103E]))");

        private static readonly Regex ShieldedEnglishLabelBeforeARegex = new Regex(
            @"(?:ဗီတာမင်|အဆင့်|အဆင့်|အုပ်စု|အတန်း|အခန်း|အမှတ်|သွေး|ပုံစံ|နံပါတ်)[ \t]+$");

        private static readonly Regex CorruptedAsciiAPrefixPattern = new Regex(
            @"(?<![A-Za-z0-9])(?:(?<Target>A[\u102B-\u103E]+)|(?<Target2>A[ \t]+(?<Stem>[\u1000-\u1021][\u1000-\u103F]{1,16})))(?![A-Za-z])");

        private static readonly Regex AffirmativeEmphaticBaePattern = new Regex(
            @"(?:(?:^|[\u104A\u104B]\s*)(?![^\u104A\u104B\r\n]*?မ(?![\u102B-\u103E\u103A])[\u1000-\u1021])[^\u104A\u104B\r\n]{1,64}?(?<!မ)(?:ပါ|တာ|သလို|ရုံ)(?<Target>ဘဲ)(?=[\s\u104A\u104B]|$)|(?<!မ(?![\u102B-\u103E\u103A])[\u1000-\u103F]{0,14})ပါ[ \t]*(?<Target2>ဘဲ)(?=[\s\u104A\u104B]|$)|(?:သလို|ဖို့|မို့|တာ)[ \t]*(?<Target3>ဘဲ)(?=[\s\u104A\u104B]|$))");

        private static readonly Regex SpacedVerbalParticleTypoPattern = new Regex(
            @"(?:(?<=(?:သွား|လာ|လုပ်|စား|သောက်|ပြော|ကြည့်|ဖတ်|ရေး|နေ|ဝယ်|ပေး|ယူ|တွေ့|ရောက်)[ \t]+)(?<Khae>ခဲ)(?=[ \t]*(?:တယ်|သည်|ပါ))|(?<=(?:သွား|လာ|လုပ်|စား|သောက်|ပြော|ပေး|ယူ)[ \t]+)(?<Mae>မဲ)(?=[ \t]*(?:လို့|ဟု))|(?<=(?:ဆောက်|လုပ်|သွား|လာ|စား|ပြော|ဖတ်|ရေး|ရှောင်|သင်ယူ|ကြိုးစား))(?<Pho>ဖို)(?=\s+(?:ခက်ခဲ|လွယ်ကူ|လိုအပ်|ကောင်း|သင့်|အတွက်))|(?<=(?:ရှောင်|လုပ်|စား|သွား|ပြော|သတိထား|လိုက်နာ|ထည့်ပေး))(?<Thing>သင်)(?=တယ်|သည်|ပါတယ်|ပါသည်)|(?<KyoKa>ကြိုက)(?=တယ်|သည်|ပါ)|(?<=(?:စို|ကျ|ပျက်|မော|သေ|ရှုံး|နောက်ကျ))(?<IrrealisMar>မာ)(?=ပဲ|လား|လဲ|စိုး)|(?<=[\u1000-\u103F])(?<PaTaYa>ပါတယ)(?=[\s\u104A\u104B]|$))");

        private static readonly Regex FormalCabinetBrokenCompoundPattern = new Regex(
            @"အစိုးရ[ \t]+အဖွဲ့(?!ဝင်|အသီးသီး|အစည်း)");

        private static readonly Regex VerbStemMissingCreakyPhoPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{2,36}?)(?<!မီး|အုတ်|ထုံး|ပန်းပဲ|မီးသွေး|လူပျို)(?<Pho2>ဖို)(?=က[\s\u1000-\u1021]|နဲ့|နှင့်|နှင့်|အတွက်|ပဲ|လည်း|\s*(?:လိုအပ်|လိုအဘ်|လိုပါ|လိုတယ်|လိုသည်|ခက်ခဲ|ခက်ထန်|လွယ်ကူ|ကောင်း|သင့်|သင့်|အတွက်|တာဝန်|အထောက်အကူ|အရေးကြီး|အဓိက|ကြိုးစား|ဆောင်ရွက်|လေ့ကျင့်|လေ့ကျင့်|ညွှန်ကြား))");

        private static readonly Regex LexiconVerbGerundChinPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{2,32}?)(?<GerundChin2>ချင်း)(?=က[\s\u1000-\u1021]|သည်[\s\u104A\u104B]|ဟာ[\s\u1000-\u1021]|ကြောင့်|ကြောင့်|ဖြင့်|ဖြင့်|ဖြင်\s|ဖျင်\s)");

        private static readonly Regex ExtendedParticleAndMorphoSyntaxPattern = new Regex(
            @"(?:(?<!နွား|ကျွဲ|ကုလားအုတ်)(?<Bho>ဘို့)(?=\s*(?:အတွက်|လို|က\s|ပါ|ပဲ|မဟုတ်|ဖြစ်))|(?<=(?:မှု|ခြင်း|ချက်|များ|တွေ))(?<Nhae>နှဲ့)(?=[\s\u1000-\u102A])|(?<=(?:ကြ|ပါ))(?<FinalTae>တဲ)(?=\s*။)|(?<=(?:ဆေးလိပ်သောက်|အရက်သောက်|လမ်းလျှောက်|စာဖတ်|အားကစားလုပ်|အိပ်ရေးပျက်))(?<GerundChin>ချင်း)(?=က|သည်|ဟာ|ကြောင့်|ဖြင့်)|(?<=(?:ထိရောက်|သေချာ|ပွင့်လင်းမြင်သာ|အောင်မြင်|ကောင်းမွန်))(?<AttrThaw>သော)(?=\s+(?:ဖြေရှင်း|ဆောင်ရွက်|လုပ်ဆောင်|ပြောဆို|ကာကွယ်|ကူညီ|အသုံးပြု)[\u1000-\u103F]*(?:နိုင်|ရ|ခဲ့|ပါ|သည်|တယ်|မည်|မယ်))|(?<=[\u1000-\u103F]{2,20})(?<!တစ်ဦး|တစ်ယောက်|လူ|သူ|မိသားစု)(?<LocKo>ကို)(?=\s+နေထိုင်(?:ပါ)?(?<LocEnd>သည်|တယ်))|(?<=ပြင်ဆင်|ဆောင်ရွက်|ကြိုးပမ်း|ထိန်းသိမ်း)(?<ContKar>ကာ)(?=\s+ရှိ(?:ပါ)?သည်))");

        // SOV Clause-Final Verb Restorer: detects English-calqued SVO clauses where a finite non-creaky main verb precedes postpositional NP arguments right before '။' / '.' / 'ဟု' / 'ဆိုတာ'
        private static readonly Regex SovVerbInversionPattern = new Regex(
            @"(?<=(?:က|မှ|ကို|အား|နှင့်|နှင့်|နဲ့|ဖြင့်|ဖြင့်|မှာ|တွင်|၌|ဖို့|ဖို|စွာ|လည်း|ဘဲ|ပဲ|အစ်ကို|အစ်မ|ညီမ|သူ|ကျွန်တော်|ကျွန်မ|တဲ့|သော|သည့်|သည့်)\s+)(?<Verb>[\u1000-\u103F]{2,26}?(?:ခဲ့ကြပါသည်|ခဲ့ပါသည်|ခဲ့ကြသည်|ခဲ့သည်|ချင်ကြတယ်|စေပါတယ်|အနားယူတယ်|နားယူတယ်))\s+(?<Args>(?!(?:ဟု|ဆိုတာ|လို့|သော်လည်း|ပေမဲ့|ပေမယ့်|ပြီး|၍)(?:[\s\u104A\u104B]|$))[^\u104A\u104B""“”.\r\n]{2,60}?(?:ကို|အား|သို့|ဖြင့်|ဖြင့်|တွင်|မှာ)(?:\s+(?!(?:ဟု|ဆိုတာ|လို့|သိရ|ဖော်ပြ|တွေ့ရ|ပါရှိ)(?:[\s\u104A\u104B]|$))[\u1000-\u103F]{2,20})?)(?=\s*(?:။|\.(?!\.|[0-9A-Za-z])|ဟု(?:\s|$)|ဆိုတာ(?:\s|$)))");

        private static readonly Regex NightmareParticleAndCaseCollisionPattern = new Regex(
            @"(?:(?<=(?:ပေး|လုပ်|သွား|လာ|စား|သောက်|ပြော|ဆို|ကြည့်|ဖတ်|ရေး|ကူညီ|စောင့်ရှောက်|စောင့်လျှောက်|ဆောင်ရွက်|ကြိုးစား))(?<KyaPlural>ကြာ)(?=ပါ(?:တယ်|သည်|မယ်|မည်))|(?<=(?:ကြ|နေ|ခဲ့|နိုင်|တတ်))(?<PatPolite>ပတ်)(?=တယ်|သည်)|(?<=လို့)(?<YaPot>ရာ)(?=တယ်|သည်|ပါတယ်|ပါသည်)|(?<=(?:ကမ္ဘာကြီး|နိုင်ငံတော်|ပြည်သူများ|ပြည်သူတွေ|ကျောင်းသားများ|သားသမီးများ|ကျန်းမာရေး|ပညာရေး|အနာဂတ်|မိသားစု|အဲဒီ|ဒီ|ဤ|ထို))(?<Ahtwet>အထွက်)(?=[\s\u104A\u104B])|(?<=(?:တစ်ခု|တစ်ယောက်|တစ်ဦး|တစ်ကောင်|နိုင်ငံ|အစိုးရ|မိသားစု|ကုမ္ပဏီ|ကျောင်းသား))(?<UnspacedYae>ရဲ)(?=\s+(?!(?:တပ်ဖွဲ့|စခန်း|အရာရှိ|မှူး|ဘော်|မေ|ဝံ့|ရင့်|စွမ်း|တိုက်|ကင်း|သား|ချုပ်))[\u1000-\u102A])|(?<=(?:လဲ|မလဲ|သလဲ|တယ်|မယ်|ဘူး|ပြီ))(?<QuotLo>လို)(?=\s+(?:[\u1000-\u103F]{1,18}(?:က|သည်|ဟာ)\s+)?(?:ယုံကြည်|ထင်|ပြော|မေး|ဆို|သိ|မှတ်|ခေါ်|ကြား))|(?:(?<=(?:ဖို့|စရာ|အစားအသောက်))(?<KaungVis1>ကောင်)(?=တယ်|သည်|ပါတယ်|ပါသည်|သော|တဲ့|တွေ)|(?<![\u1000-\u103F])မ(?<KaungVis2>ကောင်)(?=ဘူး|ပါဘူး|သေးဘူး|တဲ့|သော))|(?<=(?:လာ|သွား|ဖြစ်|ရ|သိ|နားလည်|တိုးတက်|ကောင်းမွန်))(?<AungSpur>အောင်း)(?=(?:လည်း|ပဲ)?\s*(?:[\u1000-\u103F]{1,18}(?:က|သည်|ဟာ)\s+)?(?:ကူညီ|လုပ်|ဆောင်ရွက်|ကြိုးစား|ပြုလုပ်|ပေး))|(?<=(?:သူ|လူ|ကလေး|ဝန်ထမ်း|ကျောင်းသား|ပြည်သူ|မိဘ|ဆရာ))(?<TwePlural>တွေး)(?=ကို[\s\u104A\u104B])|(?:^|[\u104A\u104B]\s*|မှာ\s+|တွင်\s+)(?<SubjKo>(?:ကျွန်တော်|ကျွန်မ|ကျွန်ုပ်|သူ|ငါ|မင်း)ကို)(?=\s+[^\u104A\u104B]{2,28}?ကို\s+[^\u104A\u104B]{0,20}?(?:ကြိုက်|နှစ်သက်|ပေး|ပြော|ပြ|ပို့|ဝယ်|စား|ဖတ်))|(?<=(?:သည့်|သည့်|သော|တဲ့)\s+[\u1000-\u103F]{2,18})(?<CopKo>ကို\s+(?<CopVerb>ဖြစ်(?:ပါ)?(?:သည်|တယ်)))(?=\s*။)|(?<=\s)(?<IrrInv>မှာ(?<IrrVerb>[\u1000-\u103F]{2,14}?)(?<IrrNeg>မဟုတ်(?:ပါ)?(?:ဘူး|ချေ|ပေ)))(?=[\s\u104A\u104B]|$))");

        private static readonly Regex MyanmarSentenceFinalAsciiDotPattern = new Regex(
            @"(?<=(?:ပါတယ်|ပါသည်|ပါမည်|ပါမယ်|ခဲ့သည်|ခဲ့တယ်|ဖြစ်သည်|ရှိသည်|ရမည်|ပါပြီ|ပါပီ|ပါတယ|တယ်|သည်|မယ်|မည်|ပြီ|ပီ|ဘူး|ဖူး|များသို့|ချုပ်သို့|ရုံးသို့))(?<Dot>\.)(?!\.|[0-9A-Za-z])(?=\s|$)");

        private static readonly Regex V64MorphoAndCaseCollisionPattern = new Regex(
            @"(?:(?<=(?:လွတ်လပ်|ခံစား|အရေးယူ|ဝင်ရောက်|ပြောဆို|လုပ်ပိုင်))(?<Khwin>ခွင်)(?=ပေး|ပြု|ရ|ရှိ|တောင်း|မရှိ|တွေ|များ)|(?<=(?:သင်ခန်းစာယူ|ဝယ်ယူ|စားသောက်|လုပ်ကိုင်|ကျောင်း))(?<YuPi>ပီ)(?=ရင်|\s+[\u1000-\u1021])|(?<=(?:ပူဇော်|ကန်တော့|ဆုတောင်း))(?<PuzawPyi>ပြီ)(?=\s+[\u1000-\u1021])|(?<=(?:သောက်|စား|ကြည့်|ကြည့်|နားထောင်|ဖတ်))(?<LoKaung>လို)(?=ကောင်း(?:တယ်|သည်|ပါ|ပြီး))|(?<=(?:စတင်တာ|ခွင့်မူချက်|ခွင့်ပြုချက်|အချက်|တစ်ခု))(?<TaPae>ပယ်)(?=\s*။)|(?<=(?:အိမ်စာတွေ|အလုပ်တွေ)\s+[^\u104A\u104B]{0,18}?)(?<LokeYae>လုပ်ရယ်)(?=\s*။)|(?<=(?:ရင်|စိတ်|အိမ်|ကျောင်း|အခန်း))(?<HtaeMa>ထဲမာ)(?=\s)|(?<=(?:ခရစ္စမတ်|မွေး|ဥပုသ်|လပြည့်|လပြည့်|နှစ်သစ်ကူး))(?<XmasNay>နေမှာ)|(?<=(?:အရမ်း|သိပ်|အလွန်))(?<LhaCreaky>လှ့)(?=တယ်|သည်|ပါ)|(?<KyawntawCreaky>ကျွန်တော်)(?=မှာ\s+[^\u104A\u104B]{2,35}?(?:ရှိ|စကား)|ရဲ့\s)|(?<KyawntawNoAsat>ကျွန်တော)(?=\s*အတွက်)|(?<=(?:လူမျိုးတွေ|သူတို့|ကျွန်တော်တို့|ဝန်ထမ်းတွေ|ကလေးတွေ|မိဘတွေ))(?<TweYa>ရ)(?=\s+အ[\u1000-\u103F]+ဆုံး)|(?<=(?:သွား|လာ|ပြု|လုပ်|ဝယ်|သုံး|တက်))(?<MaeThu>မယ်)(?=သူ(?:တွေ|များ))|(?<=(?:မနက်|နံနက်|နေ့လယ်|ညနေ|ည))(?<NyanayPaing>ပိုင်)(?=မှာ|တွင်|၌)|(?<=(?:ထုတ်လုပ်|စီးပွား|ပညာ|ကျန်းမာ|လူမှု))(?<YeLupan>ရေ)(?=လုပ်ငန်း|စနစ်|ကဏ္ဍ)|(?<PataeLar>ပါတယ်လား)(?=\s*[။?]|$)|(?<PasayLar>ပါစေလား)(?=\s*[။?]|$)|(?<=ခြင်း)(?<ChinnTae>တည်)(?=\s)|(?<=(?:တွေ|များ))(?<TweGa>ဂ)(?=\s)|(?<=လုပ်ငန်း)(?<LupanHtwin>ထွင်)(?=\s)|(?<=မရှိ)(?<MashiHaw>ဟော)(?=\s+[\u1000-\u103F]+လော)|(?<=ဟု\s+[\u1000-\u103F]{2,18}?)(?<HuHo>ဟို့)(?=\s+ပြော)|(?<=(?:မှာ|ပြော|သွား|စား|လုပ်|ပေး|ဝယ်|မေး|သိ))(?<DesidChin>ခြင်)(?=တယ်|သည်|ပါတယ်|ပါသည်|ဘူး)|(?<=(?:စက်|ဆပ်ပြာ|စနစ်|နည်းပညာ|သတင်း|ခြင်း|ချင်း|မှု)က\s+)(?<CausPat>[\u1000-\u103F]{2,18}?(?:တွေ|များ)က)(?=\s+မ?[\u1000-\u103F]{2,18}?စေ(?:ပါ)?(?:တယ်|သည်|ပြီး)))",
            RegexOptions.Compiled);

        private static readonly Regex GluedPhraseBoundarySplitPattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])(?<Left>တွေ)(?<Right>အများကြီး)|(?<Left>လို့)(?<Right>တောင်းဆို|ပြောတာက)|(?<=[\u1000-\u103F]{2})(?<Left>တွေနဲ့|တွေရဲ့)(?<Right>[\u1000-\u103F]{6,}တွေ)|(?<=[\u1000-\u103F]{3})(?<Left>နဲ့)(?<Right>ပရောဂျက်တွေ)|(?<Left>လုံခြုံစွာ)(?<Right>သိမ်းဆည်း)|(?<=[\u1000-\u103F]{3})(?<Left>က)(?<Right>သန့်ရှင်းသပ်ရပ်|အရေးပါပါ|စိတ်အပန်းပြေ)|(?<=စွမ်းအင်)(?<Left>မျိုး)(?<Right>ဖြစ်တယ်)|(?<=ပျော်ရွှင်မှုတွေ\s+)(?<Left>အမြဲ)(?<Right>ရှိနေပါစေ)|(?<=ကိုယ်\s+)(?<Left>ယုံကြည်မှု)(?<Right>ရှိဖို့)|(?<=ကလေးဘဝက\s+)(?<Left>အမှတ်တရတွေ)(?<Right>ရှိ(?=တယ်))|^(?<Left>ဒါ)(?<Right>က(?=မြန်မာလူမျိုး))|^(?<Left>လှေနဲ့)(?<Right>ကူးတို့(?=ဟာ))|(?<Left>ချက်ပြုတ်နည်း)(?<Right>အမျိုးမျိုး)|(?<=တာတွေ)(?<Left>လည်း)(?<Right>လုပ်(?=ကြ)))");

        private static readonly Regex DanglingConjunctionBeforeToPattern = new Regex(
            @"(?<Target>(?:နှင့်|နှင့်)[ \t]+တို့)(?=တွင်|၌|ကို|အား|သည်|မှ)");

        private static readonly Regex SpuriousTweAndPhinMorphoPattern = new Regex(
            @"(?:(?<=(?:ဆေးရုံ|စက်ရုပ်|ရည်မှန်းချက်|အစားအသောက်|ကျောင်းသား|ပစ္စည်း|အမှား|ဘုရားပွဲ|အခက်အခဲ))(?<SpurTwe>တွေ့)(?=မှာ|ဟာ|က|ကနေ|\s+အများကြီး)|(?<=(?:ခြင်း|ချင်း))(?<MisPhin>ဖြင်|ဖျင်)(?=\s)|(?<=ခြင်း)(?<MisIiPoss>ဤ)(?=\s+အခြေခံ))");

        private static readonly Regex V65CaseAndMorphoPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])ဘာ(?<BaMa>မ)(?=မဟုတ်)|(?:သ[ \t]*လို|ပါ[ \t]*ဝင်[ \t]*ဖို့)[ \t]+(?<SpacedBae>ဘဲ)(?=[\s၊။]|$)|(?:ရဟန်း|ပုဂ္ဂိုလ်|သူ)တို့(?<ToThint>သည့်|သည့်)(?=မြတ်စွာဘုရားအား)|(?:စိုက်ပျိုးရေး|ခရီးသွားလုပ်ငန်း|ပညာရေး|ကျန်းမာရေး)(?<BenefKoHa>ကို)(?=\s+[^\u104A\u104B]{2,22}?အတွက်\s+အရေး(?:ပါ|ကြီး|ကြီ))|လုပ်တာ(?<TaKoKa>ကို)(?=\s+ကျန်းမာရေးအတွက်\s+ကောင်း)|[\u1000-\u103F]{2}ခြင်း(?<GerundKoKa>ကို)(?=\s+(?:[\u1000-\u103F]+ကို\s+)?[\u1000-\u103F]{2,14}?စေ(?:တယ်|သည်|ပါ))|(?:နည်းပညာ|သတင်း|စနစ်)(?:တွေ|များ)(?<DoubleKoKa>ကို)(?=\s+[\u1000-\u103F]{2,18}?ကို\s+[\u1000-\u103F]{2,20}?(?:တိုးမြှင့်ပေး|ဖြစ်စေ))|(?:အာမခံ|ပစ္စည်းမျိုးစုံ)(?<ChooseKaKo>က)(?=\s+ရွေးချယ်နိုင်)|(?:ဆိုးကျိုး|အန္တရာယ်)(?:တွေ|များ)(?<NoticeKaKo>က)(?=\s+သတိပြုသင့်)|ရေဒီယို(?<RadioKaKo>က)(?=\s+နားထောင်)|နွမ်းပါးသူ(?:တွေ|များ)(?<HelpKaKo>က)(?=\s+ကူညီပေး)|ရှိသလို\s+တာဝန်(?:တွေ|များ)(?<PaeLae>ပဲ)(?=ရှိ)|[\u1000-\u103F]{2}(?:တွေ|များ)(?<AffLae1>လဲ)(?=ရှိပါ)|အတွင်းမှာ(?<AffLae2>လဲ)(?=\s)|(?:စေတီတော်|ဘုရား|ကျောင်း)တွေ(?<CopTweKoKa>ကို)(?=\s+[\u1000-\u103F]+ရဲ့\s+ယဉ်ကျေးမှု)|အနုပညာတို့(?<CopToKoHa>ကို)(?=\s+[\u1000-\u103F]+ရဲ့\s+ဥပမာ)|(?:သတ္တုဓာတ်|အာဟာရဓာတ်)တွေ(?<VitMhaKa>မှာ)(?=\s+ခန္ဓာကိုယ်အတွက်\s+မရှိမဖြစ်လိုအပ်)|ဖွံ့ဖြိုးတိုးတက်ရေး(?<DevKoMha>ကို)(?=\s+အရေးပါပါ)|(?<=(?:တွေ|များ|တို့)က)(?<TopicThaw>သော)(?=\s+)|(?<=အရာ(?:လေး)?(?:တွေ|များ))(?<StartKo>ကို)(?=\s+စတင်နိုင်)|(?<PurpPayPo>ပေးပို့)(?=\s+မျှော်လင့်|\s+မျှော်လင့်))",
            RegexOptions.Compiled);

        private static readonly Regex IntraSentenceRegisterClashV63Pattern = new Regex(
            @"(?:(?:စာမေးပွဲခန်း|အစည်းအဝေးခန်း|တရားရုံးတော်|စာကြည့်တိုက်)(?:တွင်|၌)\s+(?:[^\u104A\u104B\r\n]{0,20}?\s+)?(?<AeSay>အေးဆေး)(?=\s+နေရမည်)|(?:^|[\u104A\u104B]\s*)[^\u104A\u104B\r\n]{0,28}?မှာ\s+[^\u104A\u104B\r\n]{2,36}?(?<PayEi>ပေး၏)(?=\s*(?:။|လို့))|(?:ပါမောက္ခချုပ်ကြီး|ပါမောက္ခကြီး|ဝန်ကြီးချုပ်|သမ္မတကြီး|ပြီးပါသည်၊)[^\u104A\u104B\r\n]{2,48}?(?<CasualNegBya>(?<NegStem>နားမလည်|မသိ)ဘူး[ \t]*ဗျာ?)(?=\s*။|$)|(?:ခဲ့ပါသည်|ပြီးပါသည်|ထားပါသည်)၊\s*(?:ဒါပေမဲ့|ဒါကြောင့်|ထို့ကြောင့်|ပြီးတော့|တပည့်တော်တို့လည်း)?\s*[^\u104A\u104B\r\n]{2,36}?(?<Clause2Colloq>ဈေးကြီးတယ်|နာယူမှတ်သားခဲ့ကြတယ်)(?=\s*။|$)|လွှတ်တော်အစည်းအဝေးတွင်\s+(?<ParlDi>ဒီ)(?=အကြောင်း)|တရားရုံးက\s+(?<CourtDiLu>ဒီလူ)(?=ကို)|သုတေသနအဖွဲ့က\s+(?<ResHoTaNay>ဟိုတစ်နေ့က)|နိုင်ငံတော်သမ္မတက\s+[^\u104A\u104B\r\n]{2,36}?(?<PresPyaw>ပြောခဲ့ပါတယ်)(?=\s*။)|အစိုးရ၏\s+[^\u104A\u104B\r\n]{2,28}?\s+(?<GovKhuMha>ခုမှ)(?=\s+))");

        private static bool HasSpaceBeforeDetachedCodaTrigger(string text)
        {
            int limit = text.Length - 2;
            for (int i = 0; i < limit; i++)
            {
                char c = text[i];
                if (c == ' ' || c == '\t')
                {
                    char next = text[i + 1];
                    if (next >= '\u1000' && next <= '\u1021')
                    {
                        char mark = text[i + 2];
                        if (mark == '\u103A' || mark == '\u1037')
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool HasImpossibleSyllableOrTaWunTrigger(string text, ulong mmMask)
        {
            if (ContainsMm(text, mmMask, "တဝန်း"))
                return true;

            int limit = text.Length - 1;
            for (int i = 0; i < limit; i++)
            {
                char c = text[i];
                char n = text[i + 1];
                if (n == '\u1038')
                {
                    if (c == '\u102D' || c == '\u1004' || c == '\u1014' || c == '\u1019' || c == '\u101A')
                        return true;
                }
                else if ((c == '\u103B' && n == '\u103C') || (c == '\u103C' && n == '\u103B'))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ScanStructuralSyllableAndBoundaryRepairs(
            string text,
            ulong mmMask,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie,
            List<ProofingIssue> issues)
        {
            // 1. Space-detached intra-syllable killed coda ('လာမ ည့် -> လာမည့်', 'ပီကု တ် -> ပီကုတ်', 'ကို ယ် -> ကိုယ်', 'ပို င်း -> ပိုင်း') & broken virama stack ('စစ်တက္ ကသိုလ် -> စစ်တက္ကသိုလ်')
            if (text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0)
            {
                if (text.IndexOf('\u1039') >= 0)
                {
                    foreach (Match mv in BrokenViramaStackAcrossSpacePattern.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, mv.Index, mv.Length))
                            continue;

                        string leftVal = mv.Groups["Left"].Value;
                        string rightVal = mv.Groups["Right"].Value;

                        if (trie == null)
                        {
                            string joinedFallback = UnicodeNormalizer.NormalizeCanonicalOrder(leftVal + rightVal);
                            AddGrammarOrSpellingIssue(
                                issues, mv.Index, mv.Length, mv.Value, joinedFallback,
                                ProofingIssueType.SpellingError,
                                $"ပါဋ္ဌ်ဆင့်သင်္ကေတ ('္') နောက်၌ နေရာလွတ် (Space) မှားခြားမိနေပါသည် — '{mv.Value}' အစား '{joinedFallback}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                                "ပါဋ္ဌ်ဆင့်စာလုံး တစ်ဆက်တည်းရေးရန်", "N", true);
                            continue;
                        }

                        // 1a-i. Check if Left + [1..4 syllables of Right] forms a verified lexicon pat-sint word (e.g. 'စစ်တက္ ကသိုလ် -> စစ်တက္ကသိုလ်', 'ဝတ္ ထု -> ဝတ္ထု')
                        var rightSyls = MyanmarSyllableBreaker.BreakIntoSyllables(rightVal);
                        int matchedRightChars = 0;
                        string? validJoinedStack = null;
                        int accumR = 0;
                        for (int rCnt = 1; rCnt <= Math.Min(4, rightSyls.Count); rCnt++)
                        {
                            accumR += rightSyls[rCnt - 1].Length;
                            if (accumR <= rightVal.Length)
                            {
                                string tryStack = UnicodeNormalizer.NormalizeCanonicalOrder(leftVal + rightVal.Substring(0, accumR));
                                if (trie.ContainsExactWord(tryStack))
                                {
                                    validJoinedStack = tryStack;
                                    matchedRightChars = accumR;
                                }
                            }
                        }

                        if (validJoinedStack != null && matchedRightChars > 0)
                        {
                            int stackSpanLen = (mv.Groups["Right"].Index - mv.Index) + matchedRightChars;
                            if (!OverlapsExistingIssue(issues, mv.Index, stackSpanLen))
                            {
                                string rawSlice = text.Substring(mv.Index, stackSpanLen);
                                AddGrammarOrSpellingIssue(
                                    issues, mv.Index, stackSpanLen, rawSlice, validJoinedStack,
                                    ProofingIssueType.SpellingError,
                                    $"ပါဋ္ဌ်ဆင့်သင်္ကေတ ('္') နောက်၌ နေရာလွတ် (Space) မှားခြားမိနေပါသည် — '{rawSlice}' အစား '{validJoinedStack}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                                    "ပါဋ္ဌ်ဆင့်စာလုံး တစ်ဆက်တည်းရေးရန်", "N", true);
                            }
                            continue;
                        }

                        // 1a-ii. Otherwise trailing '\u1039' before space is Zawgyi / Shift+F mistyped Asat '\u103A' (e.g. 'အပြိုင်အဆိုင္ -> အပြိုင်အဆိုင်', 'ကျန်းမာရေးအတြက္ -> အတွက်')
                        if (leftVal.Length >= 2 && leftVal[leftVal.Length - 1] == '\u1039')
                        {
                            string leftAsat = leftVal.Substring(0, leftVal.Length - 1) + "\u103A";
                            var leftSyls = MyanmarSyllableBreaker.BreakIntoSyllables(leftAsat);
                            int rawTailChars = 0;
                            for (int tCnt = 1; tCnt <= Math.Min(4, leftSyls.Count); tCnt++)
                            {
                                rawTailChars += leftSyls[leftSyls.Count - tCnt].Length;
                                if (rawTailChars <= leftVal.Length)
                                {
                                    int subStart = mv.Index + (leftVal.Length - rawTailChars);
                                    string rawWord = text.Substring(subStart, rawTailChars);
                                    string candAsat = UnicodeNormalizer.NormalizeCanonicalOrder(rawWord.Substring(0, rawWord.Length - 1) + "\u103A");
                                    string candZg = candAsat.IndexOf('\u103C') >= 0
                                        ? UnicodeNormalizer.NormalizeCanonicalOrder(candAsat.Replace('\u103C', '\u103D'))
                                        : candAsat;

                                    string? repairedWord = trie.ContainsExactWord(candAsat)
                                        ? candAsat
                                        : (candZg != candAsat && trie.ContainsExactWord(candZg) ? candZg : null);

                                    if (repairedWord != null && !OverlapsExistingIssue(issues, subStart, rawTailChars))
                                    {
                                        AddGrammarOrSpellingIssue(
                                            issues, subStart, rawTailChars, rawWord, repairedWord,
                                            ProofingIssueType.LookalikeCharacterError,
                                            $"စာလုံးအဆုံး၌ ပါဋ္ဌ်ဆင့်သင်္ကေတ ('္') မှားရိုက်မိနေပါသည် — အသတ် ('်') ဖြင့် '{repairedWord}' ဟု ရေးသားရမည်။",
                                            "ယူနီကုဒ် အသတ်စာလုံးပေါင်းအမှန်", "WORD", true);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (HasSpaceBeforeDetachedCodaTrigger(text))
                {
                    foreach (Match m in OrphanSpaceDetachedCodaPattern.Matches(text))
                    {
                        string openSyl = m.Groups["OpenSyl"].Value;
                        string coda = m.Groups["Coda"].Value;
                        string joinedSyl = UnicodeNormalizer.NormalizeCanonicalOrder(openSyl + coda);
                        if (trie != null && !trie.IsValidSingleSyllable(joinedSyl) && !trie.ContainsExactWord(joinedSyl))
                            continue;

                        string prefix = m.Groups["Prefix"].Value;
                        string fullJoined = UnicodeNormalizer.NormalizeCanonicalOrder(prefix + openSyl + coda);
                        bool useFullPrefix = prefix.Length > 0 && trie != null && trie.ContainsExactWord(fullJoined);

                        int startIdx = useFullPrefix ? m.Index : m.Groups["OpenSyl"].Index;
                        int spanLen = (m.Index + m.Length) - startIdx;
                        if (OverlapsExistingIssue(issues, startIdx, spanLen))
                            continue;

                        string rawSpan = text.Substring(startIdx, spanLen);
                        string targetRep = useFullPrefix ? fullJoined : joinedSyl;
                        AddGrammarOrSpellingIssue(
                            issues, startIdx, spanLen, rawSpan, targetRep,
                            ProofingIssueType.SpellingError,
                            $"ဝဏ္ဏအတွင်း အသတ်ရှေ့၌ နေရာလွတ် (Space) မှားခြားမိနေပါသည် — '{rawSpan}' အစား '{targetRep}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            "အသတ်နှင့် ရှေ့ဗျည်း တစ်ဆက်တည်းရေးရန်", "N", true);
                    }
                }
            }

            // 1a. Corrupted Zawgyi/WinInnwa ASCII 'A' for Burmese 'အ' ('Aား -> အား', 'A မျိုးသား -> အမျိုးသား', 'A မျိုးသားဒီမိုကရေစီA ဖွဲ့ချုပ်Aား -> အမျိုးသားဒီမိုကရေစီအဖွဲ့ချုပ်အား')
            //     Must run BEFORE Layer-1 MyanmarSyllableValidator so 'Aား' is repaired to 'အား' rather than stripped as an orphan diacritic!
            if (text.IndexOf('A') >= 0)
            {
                foreach (Match mg in CorruptedGluedMultiAsciiAPrefixPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, mg.Index, mg.Length))
                        continue;
                    string rawMulti = mg.Value;
                    string repairedMulti = UnicodeNormalizer.NormalizeCanonicalOrder(
                        ReplaceAsciiASegmentRegex.Replace(rawMulti, "\u1021"));
                    if (repairedMulti != rawMulti)
                    {
                        AddGrammarOrSpellingIssue(
                            issues, mg.Index, mg.Length, rawMulti, repairedMulti,
                            ProofingIssueType.SpellingError,
                            $"အင်္ဂလိပ်အက္ခရာ 'A' များ ရောနှောနေပါသည် — '{rawMulti}' အစား '{repairedMulti}' ဟု ရေးသားရမည်။",
                            "မြန်မာအက္ခရာ 'အ' ပေါင်းစပ်ပုဒ်အမှန်", "N", true);
                    }
                }

                foreach (Match m in CorruptedAsciiAPrefixPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    if (m.Groups["Target"].Success)
                    {
                        string raw = m.Groups["Target"].Value;
                        string repaired = UnicodeNormalizer.NormalizeCanonicalOrder("\u1021" + raw.Substring(1));
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, raw, repaired,
                            ProofingIssueType.SpellingError,
                            $"အင်္ဂလိပ်အက္ခရာ 'A' ရောနှောနေပါသည် — '{raw}' အစား မြန်မာအက္ခရာ 'အ' ဖြင့် '{repaired}' ဟု ရေးသားရမည်။",
                            "မြန်မာအက္ခရာ 'အ' အမှန်", "PART", true);
                    }
                    else if (m.Groups["Target2"].Success && trie != null)
                    {
                        if (m.Index >= 2)
                        {
                            int ctxStart = Math.Max(0, m.Index - 20);
                            string leftCtx = text.Substring(ctxStart, m.Index - ctxStart);
                            if (ShieldedEnglishLabelBeforeARegex.IsMatch(leftCtx))
                                continue;
                        }

                        Group stemGrp = m.Groups["Stem"];
                        string rawStem = stemGrp.Value;
                        if (!rawStem.StartsWith("\u1021", StringComparison.Ordinal))
                        {
                            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(rawStem);
                            string? bestPrefix = null;
                            int bestRawLen = 0;
                            int accumRaw = 0;
                            for (int sCnt = 1; sCnt <= Math.Min(4, syls.Count); sCnt++)
                            {
                                accumRaw += syls[sCnt - 1].Length;
                                if (accumRaw <= rawStem.Length)
                                {
                                    // Never detach a 1-syllable prefix (e.g. 'ရ' -> 'အရ') from the front of an intact multi-syllable word ('ရရှိ')!
                                    if (sCnt == 1 && syls.Count >= 2)
                                    {
                                        string twoSylNorm = UnicodeNormalizer.NormalizeCanonicalOrder(syls[0] + syls[1]);
                                        if (trie.ContainsExactWord(twoSylNorm) && !trie.ContainsExactWord("\u1021" + twoSylNorm))
                                            continue;
                                    }

                                    string subNorm = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem.Substring(0, accumRaw));
                                    string tryCand = "\u1021" + subNorm;
                                    if (trie.TryGetExactEntries(tryCand, out var aEntries) &&
                                        aEntries != null && aEntries.Count > 0 && aEntries[0].IsOfficialThatpon)
                                    {
                                        bestPrefix = tryCand;
                                        bestRawLen = (stemGrp.Index - m.Index) + accumRaw;
                                    }
                                }
                            }
                            if (bestPrefix != null && bestRawLen > 0 && !OverlapsExistingIssue(issues, m.Index, bestRawLen))
                            {
                                string rawSlice = text.Substring(m.Index, bestRawLen);
                                AddGrammarOrSpellingIssue(
                                    issues, m.Index, bestRawLen, rawSlice, bestPrefix,
                                    ProofingIssueType.SpellingError,
                                    $"အင်္ဂလိပ်အက္ခရာ 'A' ရောနှောနေပါသည် — '{rawSlice}' အစား '{bestPrefix}' ဟု ရေးသားရမည်။",
                                    "သတ်ပုံကျမ်းဝင် 'အ~' ပေါင်းစပ်ပုဒ်", "N", true);
                            }
                        }
                    }
                }
            }

            // 1b. Layer-1 UTN #11 & Burmese Phonotactic Cross-Token Shattered Syllable Span Repairs
            MyanmarSyllableValidator.ScanMalformedSyllableSpans(text, trie, issues);

            // 2. Impossible double tone mark on the same syllable ('ကောင်း့ -> ကောင်း')
            if ((text.IndexOf("\u1038\u1037", StringComparison.Ordinal) >= 0) ||
                (text.IndexOf("\u1037\u1038", StringComparison.Ordinal) >= 0))
            {
                foreach (Match m in RedundantDoubleToneSyllablePattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string raw = m.Value;
                    string keepVisarga = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u1037", string.Empty));
                    string keepCreaky = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u1038", string.Empty));
                    string best = (trie != null && !trie.ContainsExactWord(keepVisarga) && trie.ContainsExactWord(keepCreaky))
                        ? keepCreaky
                        : keepVisarga;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, raw, best,
                        ProofingIssueType.SpellingError,
                        $"ဝဏ္ဏတစ်ခုတည်း၌ ဝစ္စပေါက် ('း') နှင့် အောက်မြစ် ('့') နှစ်ခုတွဲ မသုံးရပါ — '{raw}' အစား '{best}' ဟု ရေးသားရမည်။",
                        "ပိုနေသော အသံပြောင်းသင်္ကေတ ဖယ်ရှားရန်", "V", true);
                }
            }

            // 2b. Impossible syllable vowel/medial/missing-asat combinations ('အရေးကြိး -> ကြီး', 'ကျြန်တော် -> ကျွန်', 'သက်တမး -> တမ်း', 'နိုင်ငံတဝန်း -> တစ်ဝန်း')
            if (HasImpossibleSyllableOrTaWunTrigger(text, mmMask))
            foreach (Match mImp in ImpossibleSyllableVowelMedialPattern.Matches(text))
            {
                if (OverlapsExistingIssue(issues, mImp.Index, mImp.Length))
                    continue;
                if (mImp.Groups["ShortIVisarga"].Success)
                {
                    string raw = mImp.Value;
                    string rep = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u102D\u1038", "\u102E\u1038"));
                    if (trie == null || trie.IsValidSingleSyllable(rep) || trie.ContainsExactWord(rep))
                    {
                        AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, raw, rep, ProofingIssueType.SpellingError,
                            $"ဝစ္စပေါက် ('း') နှင့်တွဲရာတွင် လုံးကြီးတင်ဆန်ခတ် ('ီး') ဖြင့် '{rep}' ဟု ရေးသားရမည်။", "စံဝဏ္ဏဖွဲ့စည်းပုံအမှန်", "ADJ", true);
                    }
                }
                else if (mImp.Groups["DoubleMedial"].Success)
                {
                    string raw = mImp.Value;
                    string srcMed = raw.IndexOf("\u103B\u103C", StringComparison.Ordinal) >= 0 ? "\u103B\u103C" : "\u103C\u103B";
                    string repYw = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace(srcMed, "\u103B\u103D"));
                    string repRw = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace(srcMed, "\u103C\u103D"));
                    string? chosen = (trie == null || trie.IsValidSingleSyllable(repYw) || trie.ContainsExactWord(repYw))
                        ? repYw
                        : ((trie.IsValidSingleSyllable(repRw) || trie.ContainsExactWord(repRw)) ? repRw : null);
                    if (chosen != null)
                    {
                        AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, raw, chosen, ProofingIssueType.SpellingError,
                            $"ဗျည်းတွဲ ယပင့်နှင့် ရရစ် ('ျြ') တွဲမသုံးရပါ — '{raw}' အစား '{chosen}' ဟု ရေးသားရမည်။", "ဗျည်းတွဲ စာလုံးပေါင်းအမှန်", "N", true);
                    }
                }
                else if (mImp.Groups["MissingAsatVisarga"].Success)
                {
                    string raw = mImp.Value;
                    string rep = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Substring(0, raw.Length - 1) + "\u103A\u1038");
                    if (trie == null || trie.IsValidSingleSyllable(rep) || trie.ContainsExactWord(rep))
                    {
                        AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, raw, rep, ProofingIssueType.SpellingError,
                            $"အသတ် ('်') ကျန်ခဲ့ပါသည် — '{raw}' အစား '{rep}' ဟု ရေးသားရမည်။", "အသတ်နှင့် ဝစ္စပေါက် အမှန်", "N", true);
                    }
                }
                else if (mImp.Groups["TaWun"].Success)
                {
                    AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, mImp.Value, "တစ်ဝန်း", ProofingIssueType.NumeralClassifierError,
                        "သတ်ပုံကျမ်းစံနှုန်းအရ 'တစ်ဝန်း' ဟု စသတ်ဖြင့် ရေးသားရမည်။", "တစ်ဝန်း (နိုင်ငံ~ရှိ)", "N", true);
                }
            }

            // 3. Corrupted Zawgyi '-ော့င်း' conversion artifact ('ပြော့င်း -> ပြောင်း')
            if (text.IndexOf("\u1037", StringComparison.Ordinal) >= 0 && text.IndexOf("\u1038", StringComparison.Ordinal) >= 0)
            {
                foreach (Match m in CorruptedZawgyiAwNasalCodaPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string raw = m.Value;
                    string repaired = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u1037", string.Empty));
                    if (trie != null && (trie.ContainsExactWord(raw) || (!trie.IsValidSingleSyllable(repaired) && !trie.ContainsExactWord(repaired))))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, raw, repaired,
                        ProofingIssueType.SpellingError,
                        $"စာလုံးဖွဲ့စည်းပုံ မှားယွင်းနေပါသည် — '{raw}' အစား အောက်မြစ်မပါဘဲ '{repaired}' ဟု ရေးသားရမည်။",
                        "စံယူနီကုဒ် ဝဏ္ဏအမှန်", "V", true);
                }
            }

            // 5. Affirmative emphatic 'ဘဲ' -> 'ပဲ' (when not negated by 'မ-')
            if (ContainsMm(text, mmMask, "ဘဲ"))
            {
                foreach (Match m in AffirmativeEmphaticBaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"]
                            : m.Groups["Target2"].Success ? m.Groups["Target2"]
                            : m.Groups["Target3"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပဲ",
                        ProofingIssueType.SpellingError,
                        "အဟုတ်အမှန် အလေးအနက်ပြပစ္စည်းကို ပစောက်ဖြင့် 'ပဲ' ဟု ရေးသားရမည် ('ဘဲ' ကို 'မ...ဘဲ' အငြင်းပစ္စည်း၌သာ သုံးသည်)။",
                        "အလေးအနက်ပြပစ္စည်း ('~ပါပဲ / ~သလိုပဲ')", "PART", true);
                }
            }

            // 6. Spaced/unspaced verbal particle & modal typos ('သွား ခဲ တယ် -> ခဲ့', 'ပေး မဲ လို့ -> မယ်', '~ဖို -> ~ဖို့', '~သင်တယ် -> ~သင့်', 'ကြိုကတယ် -> ကြိုက်')
            if (ContainsMm(text, mmMask, "ခဲ") ||
                ContainsMm(text, mmMask, "မဲ") ||
                ContainsMm(text, mmMask, "ဖို") ||
                ContainsMm(text, mmMask, "သင်") ||
                ContainsMm(text, mmMask, "ကြိုက") ||
                ContainsMm(text, mmMask, "မာ") ||
                ContainsMm(text, mmMask, "ပါတယ"))
            foreach (Match m in SpacedVerbalParticleTypoPattern.Matches(text))
            {
                if (m.Groups["Khae"].Success && !OverlapsExistingIssue(issues, m.Groups["Khae"].Index, m.Groups["Khae"].Length))
                {
                    Group g = m.Groups["Khae"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ခဲ့", ProofingIssueType.SpellingError,
                        "အတိတ်ပြ ကြိယာထောက်ပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ခဲ့' ဟု ရေးသားရမည်။", "အတိတ်ပြပစ္စည်း ('~ခဲ့')", "PART", true);
                }
                else if (m.Groups["Mae"].Success && !OverlapsExistingIssue(issues, m.Groups["Mae"].Index, m.Groups["Mae"].Length))
                {
                    Group g = m.Groups["Mae"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မယ်", ProofingIssueType.SpellingError,
                        "အနာဂတ်ပြ ကြိယာပိတ်ပစ္စည်းကို ယပက်လက် အသတ်ဖြင့် 'မယ်' ဟု ရေးသားရမည်။", "အနာဂတ်ပြပစ္စည်း ('~မယ်')", "PART", false);
                }
                else if (m.Groups["Pho"].Success && !OverlapsExistingIssue(issues, m.Groups["Pho"].Index, m.Groups["Pho"].Length))
                {
                    Group g = m.Groups["Pho"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဖို့", ProofingIssueType.SpellingError,
                        "ရည်ရွယ်ချက်ပြပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ဖို့' ဟု ရေးသားရမည်။", "ရည်ရွယ်ချက်ပြပစ္စည်း ('~ဖို့')", "PART", true);
                }
                else if (m.Groups["Thing"].Success && !OverlapsExistingIssue(issues, m.Groups["Thing"].Index, m.Groups["Thing"].Length))
                {
                    Group g = m.Groups["Thing"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သင့်", ProofingIssueType.SpellingError,
                        "ကြိယာထောက်ပစ္စည်း '~သင့်' တွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'သင့်' ဟု ရေးသားရမည်။", "သင့်လျော်ကြောင်းပြပစ္စည်း ('~သင့်')", "PART", true);
                }
                else if (m.Groups["KyoKa"].Success && !OverlapsExistingIssue(issues, m.Groups["KyoKa"].Index, m.Groups["KyoKa"].Length))
                {
                    Group g = m.Groups["KyoKa"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကြိုက်", ProofingIssueType.SpellingError,
                        "အသတ် ('်') ကျန်ခဲ့ပါသည် — 'ကြိုက' အစား 'ကြိုက်' ဟု ရေးသားရမည်။", "နှစ်သက်သည် ('ကြိုက်')", "V", true);
                }
                else if (m.Groups["IrrealisMar"].Success && !OverlapsExistingIssue(issues, m.Groups["IrrealisMar"].Index, m.Groups["IrrealisMar"].Length))
                {
                    Group g = m.Groups["IrrealisMar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မှာ", ProofingIssueType.SpellingError,
                        "ဟထိုး ('ှ') ကျန်ခဲ့ပါသည် — 'မာ' အစား 'မှာ' ဟု ရေးသားရမည်။", "ဖြစ်လတ္တံ့ပြပစ္စည်း ('~မှာပဲ / ~မှာလား')", "PART", false);
                }
                else if (m.Groups["PaTaYa"].Success && !OverlapsExistingIssue(issues, m.Groups["PaTaYa"].Index, m.Groups["PaTaYa"].Length))
                {
                    Group g = m.Groups["PaTaYa"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "ပါသည်" : "ပါတယ်";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"အသတ် ('်') ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည်။", "ကြိယာအဆုံးသတ်ပစ္စည်း", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
            }

            // 6b. Lexicon-backed VerbStem + 'ဖို' -> 'ဖို့' before purposive/modal predicates ('ကာကွယ်ဖိုက -> ကာကွယ်ဖို့က', 'ကျန်းမာရေးကောင်းဖို -> ဖို့')
            if (trie != null && ContainsMm(text, mmMask, "ဖို") && HasBarePhoWithoutCreaky(text))
            {
                foreach (Match mPho in VerbStemMissingCreakyPhoPattern.Matches(text))
                {
                    Group gPho = mPho.Groups["Pho2"];
                    if (OverlapsExistingIssue(issues, gPho.Index, gPho.Length))
                        continue;
                    string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(mPho.Groups["Stem"].Value);
                    if (HasTrailingLexiconVerbOrAdj(trie, normStem, allowAdj: true))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, gPho.Index, gPho.Length, gPho.Value, "ဖို့",
                            ProofingIssueType.SpellingError,
                            "ရည်ရွယ်ချက်ပြပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ဖို' အစား 'ဖို့' ဟု ရေးသားရမည်။",
                            "ရည်ရွယ်ချက်ပြပစ္စည်း ('~ဖို့')", "PART", true);
                    }
                }
            }

            // 6b-ii. Lexicon-backed VerbStem + 'ချင်း' -> 'ခြင်း' before nominal postpositions ('ရှောင်ရှားချင်းက -> ခြင်း', 'စောင့်ရှောက်ချင်းက -> ခြင်း')
            if (trie != null && ContainsMm(text, mmMask, "ချင်း"))
            {
                foreach (Match mGer in LexiconVerbGerundChinPattern.Matches(text))
                {
                    Group gGer = mGer.Groups["GerundChin2"];
                    if (OverlapsExistingIssue(issues, gGer.Index, gGer.Length))
                        continue;
                    string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(mGer.Groups["Stem"].Value);
                    if (!IsReciprocalOrImmediateChinStem(normStem) && HasTrailingLexiconVerbOrAdj(trie, normStem, allowAdj: false))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, gGer.Index, gGer.Length, gGer.Value, "ခြင်း",
                            ProofingIssueType.SpellingError,
                            "ကြိယာမှ နာမ်ပြောင်းရာတွင် ရရစ် ('ြ') ဖြင့် 'ခြင်း' ဟု ရေးသားရမည် ('ချင်း' သည် အချင်းချင်း/တစ်ဦးချင်း၌သာ သုံးသည်)။",
                            "ကြိယာနာမ်ပြုပစ္စည်း ('~ခြင်း')", "PART", true);
                    }
                }
            }

            // 6c. Extended particle, nominalizer, adverbializer & case-frame confusions ('ဘို့ -> ဖို့', '~မှုနှဲ့ -> နဲ့', '~ချင်း -> ~ခြင်း', '~သော -> ~စွာ', '~ကို နေထိုင် -> တွင်/မှာ', '~ကာ ရှိသည် -> လျက်')
            if (ContainsMm(text, mmMask, "ဘို့") ||
                ContainsMm(text, mmMask, "နှဲ့") ||
                ContainsMm(text, mmMask, "ကြတဲ") || ContainsMm(text, mmMask, "ပါတဲ") ||
                ContainsMm(text, mmMask, "ချင်း") ||
                ContainsMm(text, mmMask, "သော ") || ContainsMm(text, mmMask, "သော\t") ||
                ContainsMm(text, mmMask, "နေထိုင်") ||
                ContainsMm(text, mmMask, "ကာ ရှိ") || ContainsMm(text, mmMask, "ကာ\tရှိ"))
            foreach (Match mExt in ExtendedParticleAndMorphoSyntaxPattern.Matches(text))
            {
                if (mExt.Groups["Bho"].Success && !OverlapsExistingIssue(issues, mExt.Groups["Bho"].Index, mExt.Groups["Bho"].Length))
                {
                    Group g = mExt.Groups["Bho"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဖို့", ProofingIssueType.SpellingError,
                        "ရည်ရွယ်ချက်ပြပစ္စည်းကို ဖဦးထုပ်ဖြင့် 'ဖို့' ဟု ရေးသားရမည် ('ဘို့' သည် 'နွားဘို့' နာမ်၌သာ သုံးသည်)။",
                        "ရည်ရွယ်ချက်ပြပစ္စည်း ('~ဖို့')", "PART", true);
                }
                else if (mExt.Groups["Nhae"].Success && !OverlapsExistingIssue(issues, mExt.Groups["Nhae"].Index, mExt.Groups["Nhae"].Length))
                {
                    Group g = mExt.Groups["Nhae"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "နှင့်" : "နဲ့";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"အတူပြဝိဘတ်တွင် '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည် ('နှဲ့' သည် လှုပ်နှဲ့သည့် ကြိယာဖြစ်သည်)။",
                        "အတူပြ နာမ်ဝိဘတ်", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
                else if (mExt.Groups["FinalTae"].Success && !OverlapsExistingIssue(issues, mExt.Groups["FinalTae"].Index, mExt.Groups["FinalTae"].Length))
                {
                    Group g = mExt.Groups["FinalTae"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "သည်" : "တယ်";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"ဝါကျအဆုံး ကြိယာဝိဘတ်တွင် '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည်။",
                        "ကြိယာအဆုံးသတ်ပစ္စည်း", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
                else if (mExt.Groups["GerundChin"].Success && !OverlapsExistingIssue(issues, mExt.Groups["GerundChin"].Index, mExt.Groups["GerundChin"].Length))
                {
                    Group g = mExt.Groups["GerundChin"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ခြင်း", ProofingIssueType.SpellingError,
                        "ကြိယာမှ နာမ်ပြောင်းရာတွင် ရရစ် ('ြ') ဖြင့် 'ခြင်း' ဟု ရေးသားရမည်။",
                        "ကြိယာနာမ်ပြုပစ္စည်း ('~ခြင်း')", "PART", true);
                }
                else if (mExt.Groups["AttrThaw"].Success && !OverlapsExistingIssue(issues, mExt.Groups["AttrThaw"].Index, mExt.Groups["AttrThaw"].Length))
                {
                    Group g = mExt.Groups["AttrThaw"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "စွာ", ProofingIssueType.OfficialStyleGrammarError,
                        "နောက်မှ ကြိယာကို အထူးပြုရာတွင် နာမဝိသေသနပြ 'သော' အစား ကြိယာဝိသေသနပြ 'စွာ' ကို သုံးနှုန်းရမည်။",
                        "ကြိယာဝိသေသနပြုပစ္စည်း ('~စွာ')", "PART", true,
                        "နောက်၌ နာမ်ပုဒ်မလိုက်ဘဲ ကြိယာတိုက်ရိုက်လိုက်နေသဖြင့် နာမ်အထူးပြု 'သော' အစား ကြိယာဝိသေသနပြုပစ္စည်း 'စွာ' ဖြင့် တွဲစပ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိသေသနပြုပစ္စည်း 'စွာ')");
                }
                else if (mExt.Groups["LocKo"].Success && !OverlapsExistingIssue(issues, mExt.Groups["LocKo"].Index, mExt.Groups["LocKo"].Length))
                {
                    Group g = mExt.Groups["LocKo"];
                    bool formalSentence = styleMode == ProofingStyleMode.FormalOfficial || mExt.Groups["LocEnd"].Value == "သည်";
                    string primary = formalSentence ? "တွင်" : "မှာ";
                    string secondary = formalSentence ? "မှာ" : "တွင်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"'နေထိုင်' ကြိယာရှေ့ နေရာပြဝိဘတ်တွင် 'ကို' အစား '{primary}' ကို သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = "'နေထိုင်သည်/နေထိုင်တယ်' သည် တည်နေရာပြ ကြိယာဖြစ်သဖြင့် ကံပုဒ်ပြ/ရှေးရှုရာပြ 'ကို' မသုံးရဘဲ နေရာပြဝိဘတ် 'တွင်/မှာ' ကို သုံးရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နေရာပြ နာမ်ဝိဘတ် 'တွင်/၌/မှာ')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate { Replacement = primary, MeaningCue = "နေရာပြ နာမ်ဝိဘတ်", PosTag = "PART", IsOfficialThatpon = formalSentence, Score = 100.0 },
                            new SuggestionCandidate { Replacement = secondary, MeaningCue = "နေရာပြ ဝိဘတ်ပုံစံကွဲ", PosTag = "PART", IsOfficialThatpon = !formalSentence, Score = 95.0 }
                        }
                    });
                }
                else if (mExt.Groups["ContKar"].Success && !OverlapsExistingIssue(issues, mExt.Groups["ContKar"].Index, mExt.Groups["ContKar"].Length))
                {
                    Group g = mExt.Groups["ContKar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လျက်", ProofingIssueType.OfficialStyleGrammarError,
                        "'~ရှိသည်' ရှေ့ ဆောင်ရွက်ဆဲပြရာတွင် 'ကာ' အစား 'လျက်' ('~လျက် ရှိသည်') ကို သုံးနှုန်းရမည်။",
                        "ဖြစ်ပေါ်ဆဲပြပစ္စည်း ('~လျက် ရှိသည်')", "PART", true,
                        "အကူကြိယာ 'ရှိသည်' နှင့်တွဲ၍ ဖြစ်ပေါ်ဆဲအခြေအနေကို ဖော်ပြရာတွင် 'လျက် ရှိသည်' ဟူသော စံတွဲထုံးကို သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိသေသနပြုပစ္စည်း 'လျက်')");
                }
            }

            // 6c-2. Nightmare particle, case-collision & inverted irrealis repairs
            if (ContainsMm(text, mmMask, "ကြာပါ") ||
                ContainsMm(text, mmMask, "ပတ်တ") || ContainsMm(text, mmMask, "ပတ်သ") ||
                ContainsMm(text, mmMask, "လို့ရာ") ||
                ContainsMm(text, mmMask, "အထွက်") ||
                ContainsMm(text, mmMask, "ရဲ ") || ContainsMm(text, mmMask, "ရဲ\t") ||
                ContainsMm(text, mmMask, "လို ") || ContainsMm(text, mmMask, "လို\t") ||
                ContainsMm(text, mmMask, "ကောင်") ||
                ContainsMm(text, mmMask, "အောင်း") ||
                ContainsMm(text, mmMask, "တွေးကို") ||
                ContainsMm(text, mmMask, "ကို ဖြစ်") || ContainsMm(text, mmMask, "ကို\tဖြစ်") ||
                ContainsMm(text, mmMask, "ကျွန်တော်ကို") || ContainsMm(text, mmMask, "ကျွန်မကို") || ContainsMm(text, mmMask, "ကျွန်ုပ်ကို") ||
                ContainsMm(text, mmMask, "သူကို ") || ContainsMm(text, mmMask, "ငါကို ") || ContainsMm(text, mmMask, "မင်းကို ") ||
                ((ContainsMm(text, mmMask, " မှာ") || ContainsMm(text, mmMask, "\tမှာ")) && ContainsMm(text, mmMask, "မဟုတ်")))
            foreach (Match mNm in NightmareParticleAndCaseCollisionPattern.Matches(text))
            {
                if (mNm.Groups["KyaPlural"].Success && !OverlapsExistingIssue(issues, mNm.Groups["KyaPlural"].Index, mNm.Groups["KyaPlural"].Length))
                {
                    Group g = mNm.Groups["KyaPlural"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကြ", ProofingIssueType.SpellingError,
                        "အများပြ ကြိယာထောက်ပစ္စည်းတွင် ရေးချ ('ာ') မပါရပါ — 'ကြာ' အစား 'ကြ' ဟု ရေးသားရမည်။",
                        "အများပြ ကြိယာထောက်ပစ္စည်း ('~ကြ')", "PART", true);
                }
                else if (mNm.Groups["PatPolite"].Success && !OverlapsExistingIssue(issues, mNm.Groups["PatPolite"].Index, mNm.Groups["PatPolite"].Length))
                {
                    Group g = mNm.Groups["PatPolite"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပါ", ProofingIssueType.SpellingError,
                        "ယဉ်ကျေးမှုပြ ကြိယာထောက်ပစ္စည်းကို 'ပါ' ('~ပါတယ် / ~ပါသည်') ဟု ရေးသားရမည်။",
                        "ယဉ်ကျေးမှုပြ ကြိယာထောက်ပစ္စည်း ('~ပါ')", "PART", true);
                }
                else if (mNm.Groups["YaPot"].Success && !OverlapsExistingIssue(issues, mNm.Groups["YaPot"].Index, mNm.Groups["YaPot"].Length))
                {
                    Group g = mNm.Groups["YaPot"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ရ", ProofingIssueType.SpellingError,
                        "'~လို့ရတယ်' ပုံစံတွင် ရေးချမပါဘဲ 'ရ' ဟု ရေးသားရမည်။",
                        "ဖြစ်နိုင်စွမ်းပြ ကြိယာထောက်ပစ္စည်း ('~ရ')", "PART", true);
                }
                else if (mNm.Groups["Ahtwet"].Success && !OverlapsExistingIssue(issues, mNm.Groups["Ahtwet"].Index, mNm.Groups["Ahtwet"].Length))
                {
                    Group g = mNm.Groups["Ahtwet"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "အတွက်", ProofingIssueType.SpellingError,
                        "ရည်စူးချက်ပြ နာမ်ဝိဘတ်ကို တဝမ်းပူ ဝဆွဲဖြင့် 'အတွက်' ဟု ရေးသားရမည် ('အထွက်' သည် ထွက်ရှိမှု အနက်ဖြစ်သည်)။",
                        "ရည်စူးချက်ပြ နာမ်ဝိဘတ် ('~အတွက်')", "PART", true);
                }
                else if (mNm.Groups["UnspacedYae"].Success && !OverlapsExistingIssue(issues, mNm.Groups["UnspacedYae"].Index, mNm.Groups["UnspacedYae"].Length))
                {
                    Group g = mNm.Groups["UnspacedYae"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "၏" : "ရဲ့";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"ပိုင်ဆိုင်မှုပြဝိဘတ်တွင် အောက်မြစ်ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည်။",
                        "ပိုင်ဆိုင်မှုပြ နာမ်ဝိဘတ်", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
                else if (mNm.Groups["QuotLo"].Success && !OverlapsExistingIssue(issues, mNm.Groups["QuotLo"].Index, mNm.Groups["QuotLo"].Length))
                {
                    Group g = mNm.Groups["QuotLo"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လို့", ProofingIssueType.SpellingError,
                        "ပြောဆို/ထင်မြင်ချက်ပြ နောက်ဆက်ပစ္စည်းတွင် အောက်မြစ်ဖြင့် 'လို့' ဟု ရေးသားရမည် ('လို' သည် နှိုင်းယှဉ်ပြပစ္စည်း ဖြစ်သည်)။",
                        "ဆိုလိုချက်ပြပစ္စည်း ('~လို့')", "PART", false);
                }
                else if ((mNm.Groups["KaungVis1"].Success || mNm.Groups["KaungVis2"].Success))
                {
                    Group g = mNm.Groups["KaungVis1"].Success ? mNm.Groups["KaungVis1"] : mNm.Groups["KaungVis2"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကောင်း", ProofingIssueType.SpellingError,
                            "ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — 'ကောင်' အစား 'ကောင်း' ဟု ရေးသားရမည်။",
                            "ကောင်းမွန်ခြင်းပြ နာမဝိသေသန/ကြိယာ ('ကောင်း')", "ADJ", true);
                    }
                }
                else if (mNm.Groups["AungSpur"].Success && !OverlapsExistingIssue(issues, mNm.Groups["AungSpur"].Index, mNm.Groups["AungSpur"].Length))
                {
                    Group g = mNm.Groups["AungSpur"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "အောင်", ProofingIssueType.SpellingError,
                        "ရည်ရွယ်ချက်/အကျိုးဆက်ပြပစ္စည်းတွင် ဝစ္စပေါက်မပါဘဲ 'အောင်' ဟု ရေးသားရမည်။",
                        "အကျိုးဆက်ပြ ကြိယာပစ္စည်း ('~အောင်')", "PART", true);
                }
                else if (mNm.Groups["TwePlural"].Success && !OverlapsExistingIssue(issues, mNm.Groups["TwePlural"].Index, mNm.Groups["TwePlural"].Length))
                {
                    Group g = mNm.Groups["TwePlural"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တွေ", ProofingIssueType.SpellingError,
                        "အများပြပစ္စည်းတွင် ဝစ္စပေါက်မပါရပါ — 'တွေး' (စဉ်းစားတွေးတောသည်) အစား 'တွေ' ဟု ရေးသားရမည်။",
                        "အများပြ နာမ်ပစ္စည်း ('~တွေ')", "PART", false);
                }
                else if (mNm.Groups["SubjKo"].Success && !OverlapsExistingIssue(issues, mNm.Groups["SubjKo"].Index, mNm.Groups["SubjKo"].Length))
                {
                    Group g = mNm.Groups["SubjKo"];
                    string subjRoot = g.Value.Substring(0, g.Value.Length - 3);
                    string correctSubj = subjRoot + "က";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, correctSubj, ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျအစ ကတ္တားပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား ကတ္တားဝိဘတ် 'က' ဖြင့် '{correctSubj}' ဟု သုံးနှုန်းရမည်။",
                        "ကတ္တားဝိဘတ် ('~က')", "PART", true,
                        "နောက်၌ ကံပုဒ် ('~ကို') နှင့် ကံလိုကြိယာ ပါရှိနေသဖြင့် ပြုလုပ်သူ/ခံစားသူ ကတ္တားနာမ်စားနောက်တွင် 'က' ဝိဘတ်ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ကံဝိဘတ် ခွဲခြားပုံ)");
                }
                else if (mNm.Groups["CopKo"].Success && !OverlapsExistingIssue(issues, mNm.Groups["CopKo"].Index, mNm.Groups["CopKo"].Length))
                {
                    Group g = mNm.Groups["CopKo"];
                    string copVerb = mNm.Groups["CopVerb"].Value;
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, copVerb, ProofingIssueType.OfficialStyleGrammarError,
                        $"'ဖြစ်သည်/ဖြစ်တယ်' ကြိယာရှေ့ အမည်နာမ်ပုဒ်နောက်တွင် ကံဝိဘတ် 'ကို' မထည့်ရပါ — '{copVerb}' ဟုသာ ရေးသားရမည်။",
                        "ဖြစ်ခြင်းပြကြိယာ ('ဖြစ်သည်')", "V", true,
                        "'ဖြစ်သည်' သည် ကံမလိုသော ဖြစ်ခြင်းပြကြိယာ (Copula) ဖြစ်သဖြင့် ရှေ့ရှိ နာမ်ပုဒ်နောက်၌ ကံဝိဘတ် 'ကို' မလိုက်ရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဖြစ်ခြင်းပြကြိယာ နှင့် ဝိဘတ်စည်းမျဉ်း)");
                }
                else if (mNm.Groups["IrrInv"].Success && !OverlapsExistingIssue(issues, mNm.Groups["IrrInv"].Index, mNm.Groups["IrrInv"].Length))
                {
                    Group g = mNm.Groups["IrrInv"];
                    string fixedIrr = mNm.Groups["IrrVerb"].Value + "မှာ" + mNm.Groups["IrrNeg"].Value;
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, fixedIrr, ProofingIssueType.OfficialStyleGrammarError,
                        $"အနာဂတ်အငြင်းပုဒ် အစီအစဉ် လွဲမှားနေပါသည် — '{g.Value}' အစား '{fixedIrr}' ဟု ရေးသားရမည်။",
                        "အနာဂတ်အငြင်းပုဒ် အစီအစဉ်မှန် ('~မှာမဟုတ်ဘူး')", "SYNTAX", false,
                        "ကြိယာနာမ်ပြုပစ္စည်း 'မှာ' သည် ပင်မကြိယာ၏ နောက်၌ ကပ်လျက် တည်ရှိရပြီး ထို့နောက်မှ 'မဟုတ်ဘူး/မဟုတ်ပါ' လိုက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဝါကျဖွဲ့ထုံးနှင့် ပစ္စည်းအစီအစဉ်)");
                }
            }

            // 6d. SOV Clause-Final Verb Restorer (repairs English-calqued SVO clauses where finite main verb precedes postpositional NP arguments)
            if ((text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0) &&
                (ContainsMm(text, mmMask, "ခဲ့သည် ") || ContainsMm(text, mmMask, "ခဲ့ပါသည် ") ||
                 ContainsMm(text, mmMask, "ခဲ့ကြသည် ") || ContainsMm(text, mmMask, "ခဲ့ကြပါသည် ") ||
                 ContainsMm(text, mmMask, "ချင်ကြတယ် ") || ContainsMm(text, mmMask, "စေပါတယ် ") ||
                 ContainsMm(text, mmMask, "နားယူတယ် ") ||
                 ContainsMm(text, mmMask, "ခဲ့သည်\t") || ContainsMm(text, mmMask, "ခဲ့ပါသည်\t") ||
                 ContainsMm(text, mmMask, "ခဲ့ကြသည်\t") || ContainsMm(text, mmMask, "ခဲ့ကြပါသည်\t") ||
                 ContainsMm(text, mmMask, "ချင်ကြတယ်\t") || ContainsMm(text, mmMask, "စေပါတယ်\t") ||
                 ContainsMm(text, mmMask, "နားယူတယ်\t")))
            {
                foreach (Match mSov in SovVerbInversionPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, mSov.Index, mSov.Length))
                        continue;
                    string verbPhrase = mSov.Groups["Verb"].Value.Trim();
                    string argsPhrase = mSov.Groups["Args"].Value.Trim();
                    if (argsPhrase.IndexOf(' ') >= 0 || argsPhrase.IndexOf('\t') >= 0)
                    {
                        argsPhrase = CuratedBrokenCompoundPattern.Replace(argsPhrase, mComp =>
                        {
                            string candJoin = UnicodeNormalizer.NormalizeCanonicalOrder(mComp.Groups["Left"].Value + mComp.Groups["Right"].Value);
                            return VerifiedMandatoryCompounds.Contains(candJoin) ? candJoin : mComp.Value;
                        });
                        argsPhrase = BoundStemBrokenCompoundPattern.Replace(argsPhrase, mComp =>
                        {
                            string candJoin = UnicodeNormalizer.NormalizeCanonicalOrder(mComp.Groups["Left"].Value + mComp.Groups["Right"].Value);
                            return VerifiedMandatoryCompounds.Contains(candJoin) ? candJoin : mComp.Value;
                        });
                    }
                    string restoredSov = $"{argsPhrase} {verbPhrase}";
                    AddGrammarOrSpellingIssue(
                        issues, mSov.Index, mSov.Length, mSov.Value, restoredSov,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"မြန်မာဝါကျဖွဲ့ထုံး (SOV) အရ ပင်မကြိယာ '{verbPhrase}' ကို ဝါကျအဆုံး၌ ထားရှိရမည် — '{restoredSov}' ဟု ပြန်လည်စီစဉ်သင့်ပါသည်။",
                        "မြန်မာစံဝါကျအစီအစဉ် (ကတ္တား + ကံ/နေရာပြပုဒ် + ကြိယာ)", "SYNTAX", true,
                        $"မြန်မာဘာသာစကား၏ စံဝါကျတည်ဆောက်ပုံ (Subject-Object-Verb) တွင် ကံပုဒ်နှင့် ဝိဘတ်တွဲပုဒ်များ ('{argsPhrase}') သည် အဆုံးသတ်ပင်မကြိယာ ('{verbPhrase}') ၏ ရှေ့တွင် တည်ရှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဝါကျဖွဲ့ထုံးနှင့် ပုဒ်အစီအစဉ်)");
                }
            }

            // 6e. Glued Phrase-Boundary Splits ('တွေအများကြီး -> တွေ အများကြီး', 'လို့တောင်းဆို -> လို့ တောင်းဆို', 'တွေနဲ့စားသောက်ဆိုင်တွေ -> တွေနဲ့ စားသောက်ဆိုင်တွေ', 'လုံခြုံစွာသိမ်းဆည်း -> လုံခြုံစွာ သိမ်းဆည်း')
            if (ContainsMm(text, mmMask, "တွေအများကြီး") ||
                ContainsMm(text, mmMask, "တွေနဲ့") ||
                ContainsMm(text, mmMask, "တွေရဲ့") ||
                ContainsMm(text, mmMask, "လို့တောင်းဆို") ||
                ContainsMm(text, mmMask, "လို့ပြောတာက") ||
                ContainsMm(text, mmMask, "နဲ့ပရောဂျက်") ||
                ContainsMm(text, mmMask, "လုံခြုံစွာသိမ်း") ||
                ContainsMm(text, mmMask, "ကသန့်ရှင်း") ||
                ContainsMm(text, mmMask, "ကအရေးပါပါ") ||
                ContainsMm(text, mmMask, "ကစိတ်အပန်းပြေ") ||
                ContainsMm(text, mmMask, "မျိုးဖြစ်တယ်") ||
                ContainsMm(text, mmMask, "အမြဲရှိနေပါစေ") ||
                ContainsMm(text, mmMask, "ယုံကြည်မှုရှိဖို့") ||
                ContainsMm(text, mmMask, "အမှတ်တရတွေရှိ") ||
                ContainsMm(text, mmMask, "ဒါကမြန်မာ") ||
                ContainsMm(text, mmMask, "လှေနဲ့ကူးတို့") ||
                ContainsMm(text, mmMask, "ချက်ပြုတ်နည်းအမျိုးမျိုး") ||
                ContainsMm(text, mmMask, "တာတွေလည်းလုပ်"))
            {
                foreach (Match mGlue in GluedPhraseBoundarySplitPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, mGlue.Index, mGlue.Length))
                        continue;
                    string spacedFix = mGlue.Groups["Left"].Value + " " + mGlue.Groups["Right"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, mGlue.Index, mGlue.Length, mGlue.Value, spacedFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ပုဒ်အဖြတ်အတောက် ရှင်းလင်းစေရန် ဝိဘတ်/နောက်ဆက်ပစ္စည်းနောက်၌ Space ခြားသင့်ပါသည် — '{spacedFix}' ဟု ရေးသားရမည်။",
                        "ပုဒ်အဖြတ်အတောက် မှန်ကန်သော အရေးအသား", "SYNTAX", true,
                        "သီးခြားပုဒ်စုနှစ်ခု ဆက်စပ်ရာတွင် ဝိဘတ် သို့မဟုတ် ကြိယာဝိသေသနပြုပစ္စည်းနောက်၌ နေရာလွတ် (Space) ခြားရေးခြင်းဖြင့် ဝါကျဖတ်ရှုမှု ပိုမိုရှင်းလင်းမှန်ကန်စေသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာ ပုဒ်အဖြတ်အတောက်စံနှုန်း");
                }
            }

            // 6f. Dangling Conjunction Before Plural Pronoun ('နှင့် တို့တွင် -> တို့တွင်')
            if (ContainsMm(text, mmMask, "တို့"))
            {
                foreach (Match mDang in DanglingConjunctionBeforeToPattern.Matches(text))
                {
                    Group g = mDang.Groups["Target"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "တို့",
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"ဒုတိယနာမ်ပုဒ် မပါရှိဘဲ သမ္ဗန္ဓ 'နှင့်' ပိုနေပါသည် — '{g.Value}' အစား 'တို့' ဟု ရေးသားရမည်။",
                            "အများပြပစ္စည်း ('~တို့')", "PART", true,
                            "ယှဉ်တွဲပြသမ္ဗန္ဓ 'နှင့်' နောက်တွင် ဒုတိယနာမ်ပုဒ် မပါဘဲ အများပြပစ္စည်း 'တို့' တိုက်ရိုက်မလိုက်နိုင်ပါ။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သမ္ဗန္ဓပစ္စည်း နှင့် နာမ်ပစ္စည်း)");
                    }
                }
            }

            // 6g. Spurious Creaky 'တွေ့ -> တွေ', Dropped-Creaky 'ခြင်းဖြင် -> ခြင်းဖြင့်', & Demonstrative/Possessive 'ခြင်းဤ -> ခြင်း၏'
            if (ContainsMm(text, mmMask, "တွေ့") ||
                ContainsMm(text, mmMask, "ဖြင်") ||
                ContainsMm(text, mmMask, "ဖျင်") ||
                ContainsMm(text, mmMask, "ခြင်းဤ"))
            {
                foreach (Match mMorph in SpuriousTweAndPhinMorphoPattern.Matches(text))
                {
                    if (mMorph.Groups["SpurTwe"].Success && !OverlapsExistingIssue(issues, mMorph.Groups["SpurTwe"].Index, mMorph.Groups["SpurTwe"].Length))
                    {
                        Group g = mMorph.Groups["SpurTwe"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တွေ", ProofingIssueType.SpellingError,
                            "အများပြ နာမ်ပစ္စည်းတွင် အောက်မြစ်မပါရပါ — 'တွေ့' (ကြုံတွေ့သည်) အစား 'တွေ' ဟု ရေးသားရမည်။",
                            "အများပြပစ္စည်း ('~တွေ')", "PART", false);
                    }
                    else if (mMorph.Groups["MisPhin"].Success && !OverlapsExistingIssue(issues, mMorph.Groups["MisPhin"].Index, mMorph.Groups["MisPhin"].Length))
                    {
                        Group g = mMorph.Groups["MisPhin"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဖြင့်", ProofingIssueType.SpellingError,
                            $"အသုံးခံပြ နာမ်ဝိဘတ်တွင် '{g.Value}' အစား 'ဖြင့်' (ဖဦးထုပ် ရရစ် ငသတ် အောက်မြစ်) ဟု ရေးသားရမည်။",
                            "အသုံးခံပြဝိဘတ် ('~ဖြင့်')", "PART", true);
                    }
                    else if (mMorph.Groups["MisIiPoss"].Success && !OverlapsExistingIssue(issues, mMorph.Groups["MisIiPoss"].Index, mMorph.Groups["MisIiPoss"].Length))
                    {
                        Group g = mMorph.Groups["MisIiPoss"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "၏", ProofingIssueType.SpellingError,
                            "ပိုင်ဆိုင်မှုပြ နာမ်ဝိဘတ်တွင် ညွှန်ပြနာမ်စား 'ဤ' အစား '၏' ကို သုံးနှုန်းရမည်။",
                            "ပိုင်ဆိုင်မှုပြဝိဘတ် ('~၏')", "PART", true);
                    }
                }
            }

            // 6h. Intra-Sentence Register-Clash Detector (catches stark colloquial<->formal mixing within the same clause/sentence)
            if (ContainsMm(text, mmMask, "အေးဆေး") ||
                ContainsMm(text, mmMask, "ပေး၏") ||
                ContainsMm(text, mmMask, "ဘူး") ||
                ContainsMm(text, mmMask, "ပါသည်၊") ||
                ContainsMm(text, mmMask, "လွှတ်တော်") ||
                ContainsMm(text, mmMask, "တရားရုံးက") ||
                ContainsMm(text, mmMask, "ဟိုတစ်နေ့က") ||
                ContainsMm(text, mmMask, "သမ္မတက") ||
                ContainsMm(text, mmMask, "ခုမှ"))
            {
                foreach (Match mReg in IntraSentenceRegisterClashV63Pattern.Matches(text))
                {
                    if (mReg.Groups["AeSay"].Success && !OverlapsExistingIssue(issues, mReg.Groups["AeSay"].Index, mReg.Groups["AeSay"].Length))
                    {
                        Group g = mReg.Groups["AeSay"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တိတ်တဆိတ်", ProofingIssueType.OfficialStyleGrammarError,
                            "စည်းကမ်း/စာပေဝါကျ ('~ခန်းတွင် ... နေရမည်') အတွင်း အပြောစကား 'အေးဆေး' အစား 'တိတ်တဆိတ်' (သို့မဟုတ် 'ငြိမ်သက်စွာ') ကို သုံးနှုန်းရမည်။",
                            "စာပေ/ရုံးသုံး ကြိယာဝိသေသန ('တိတ်တဆိတ်')", "ADV", true,
                            "စာပေဟန် 'တွင် ... နေရမည်' ဝါကျဘောင်အတွင်း အရပ်သုံးစကား 'အေးဆေး' ရောနှောနေသဖြင့် စကားအဆင့်ညီညွတ်သော 'တိတ်တဆိတ်' ဖြင့် အစားထိုးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                    }
                    else if (mReg.Groups["PayEi"].Success && !OverlapsExistingIssue(issues, mReg.Groups["PayEi"].Index, mReg.Groups["PayEi"].Length))
                    {
                        Group g = mReg.Groups["PayEi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပေးလိုက်တယ်", ProofingIssueType.OfficialStyleGrammarError,
                            "အပြောစကားဝါကျ ('~မှာ ...') အဆုံး၌ စာပေဟန် 'ပေး၏' ရောထွေးနေပါသည် — 'ပေးလိုက်တယ်' (သို့မဟုတ် 'ပေးတယ်') ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                            "အပြောစကား ကြိယာအဆုံးသတ် ('ပေးလိုက်တယ်')", "V", false,
                            "ဝါကျရှေ့ပိုင်း၌ အပြောစကားဝိဘတ် 'မှာ' သုံးထားသဖြင့် အဆုံးသတ်၌ စာပေဝိဘတ် '၏' မသုံးရဘဲ အပြောစကားနှင့် ညီညွတ်အောင် ရေးသားရမည်။",
                            "မောင်ခင်မင်(ဓနုဖြူ) — အပြောစကားနှင့် အရေးစကား ညီညွတ်မှုစည်းမျဉ်း");
                    }
                    else if (mReg.Groups["CasualNegBya"].Success && !OverlapsExistingIssue(issues, mReg.Groups["CasualNegBya"].Index, mReg.Groups["CasualNegBya"].Length))
                    {
                        Group g = mReg.Groups["CasualNegBya"];
                        string politeNeg = mReg.Groups["NegStem"].Value + "ပါ";
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, politeNeg, ProofingIssueType.OfficialStyleGrammarError,
                            $"လေးစားဂါရဝပြု/စာပေ ဝါကျအတွင်း အရပ်သုံး '{g.Value}' ရောနှောနေပါသည် — '{politeNeg}' ဟု ယဉ်ကျေးညီညွတ်စွာ သုံးနှုန်းရမည်။",
                            $"ယဉ်ကျေး/စာပေ အငြင်းပုဒ် ('{politeNeg}')", "V", true,
                            "ရှေ့ဝါကျပိုင်း၏ ဂါရဝပြု/စာပေ အဆင့်အတန်းနှင့် ကိုက်ညီစေရန် 'ဘူးဗျ/ဘူးဗျာ' အစား 'ပါ' ဖြင့် ပိတ်ရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာ လမ်းညွှန်");
                    }
                    else if (mReg.Groups["Clause2Colloq"].Success && !OverlapsExistingIssue(issues, mReg.Groups["Clause2Colloq"].Index, mReg.Groups["Clause2Colloq"].Length))
                    {
                        Group g = mReg.Groups["Clause2Colloq"];
                        string formalFix = g.Value.Substring(0, g.Value.Length - 3) + "ပါသည်";
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, formalFix, ProofingIssueType.OfficialStyleGrammarError,
                            $"ပထမဝါကျခွဲ ('~ပါသည်၊') နှင့် စကားအဆင့် ညီညွတ်စေရန် '{g.Value}' အစား '{formalFix}' ဟု သုံးနှုန်းရမည်။",
                            $"စကားအဆင့်ညီညွတ်သော ဝါကျအဆုံးသတ် ('{formalFix}')", "V", true,
                            "တစ်ဝါကျတည်းအတွင်း ရှေ့ဝါကျခွဲကို 'ပါသည်' ဖြင့် ရေးသားထားပြီး နောက်ဝါကျခွဲကို 'တယ်' ဖြင့် အဆုံးသတ်ခြင်းသည် စကားအဆင့် မညီညွတ်ပါ။",
                            "မောင်ခင်မင်(ဓနုဖြူ) — အပြောစကားနှင့် အရေးစကား ညီညွတ်မှုစည်းမျဉ်း");
                    }
                    else if (mReg.Groups["ParlDi"].Success && !OverlapsExistingIssue(issues, mReg.Groups["ParlDi"].Index, mReg.Groups["ParlDi"].Length))
                    {
                        Group g = mReg.Groups["ParlDi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယင်း", ProofingIssueType.OfficialStyleGrammarError,
                            "လွှတ်တော်/ရုံးသုံး အရေးစကားအတွင်း အပြောစကား 'ဒီ' အစား 'ယင်း' (သို့မဟုတ် 'ဤ') ကို သုံးနှုန်းရမည်။",
                            "ရုံးသုံး ညွှန်းပြနာမ်စား ('ယင်း')", "PRON", true);
                    }
                    else if (mReg.Groups["CourtDiLu"].Success && !OverlapsExistingIssue(issues, mReg.Groups["CourtDiLu"].Index, mReg.Groups["CourtDiLu"].Length))
                    {
                        Group g = mReg.Groups["CourtDiLu"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယင်းတရားခံ", ProofingIssueType.OfficialStyleGrammarError,
                            "တရားရုံး စာပေအရေးအသားအတွင်း အပြောစကား 'ဒီလူ' အစား 'ယင်းတရားခံ' (သို့မဟုတ် 'အဆိုပါပုဂ္ဂိုလ်') ကို သုံးနှုန်းရမည်။",
                            "တရားရုံး/ရုံးသုံး အသုံးအနှုန်း ('ယင်းတရားခံ')", "N", true);
                    }
                    else if (mReg.Groups["ResHoTaNay"].Success && !OverlapsExistingIssue(issues, mReg.Groups["ResHoTaNay"].Index, mReg.Groups["ResHoTaNay"].Length))
                    {
                        Group g = mReg.Groups["ResHoTaNay"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယခင်က", ProofingIssueType.OfficialStyleGrammarError,
                            "သုတေသန/စာပေ အရေးစကားအတွင်း အပြောစကား 'ဟိုတစ်နေ့က' အစား 'ယခင်က' (သို့မဟုတ် 'ပြီးခဲ့သည့်ရက်က') ကို သုံးနှုန်းရမည်။",
                            "စာပေသုံး အချိန်ပြပုဒ် ('ယခင်က')", "ADV", true);
                    }
                    else if (mReg.Groups["PresPyaw"].Success && !OverlapsExistingIssue(issues, mReg.Groups["PresPyaw"].Index, mReg.Groups["PresPyaw"].Length))
                    {
                        Group g = mReg.Groups["PresPyaw"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မိန့်ကြားခဲ့သည်", ProofingIssueType.OfficialStyleGrammarError,
                            "နိုင်ငံတော်အကြီးအကဲဆိုင်ရာ စာပေသတင်းဝါကျတွင် အပြောစကား 'ပြောခဲ့ပါတယ်' အစား 'မိန့်ကြားခဲ့သည်' (သို့မဟုတ် 'ပြောကြားခဲ့သည်') ကို သုံးနှုန်းရမည်။",
                            "ရုံးသုံး/သတင်းစာပေ ကြိယာ ('မိန့်ကြားခဲ့သည်')", "V", true);
                    }
                    else if (mReg.Groups["GovKhuMha"].Success && !OverlapsExistingIssue(issues, mReg.Groups["GovKhuMha"].Index, mReg.Groups["GovKhuMha"].Length))
                    {
                        Group g = mReg.Groups["GovKhuMha"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယခုမှ", ProofingIssueType.OfficialStyleGrammarError,
                            "စာပေအရေးအသား ('အစိုးရ၏ ...') အတွင်း အပြောစကား 'ခုမှ' အစား 'ယခုမှ' ကို သုံးနှုန်းရမည်။",
                            "စာပေသုံး အချိန်ပြပုဒ် ('ယခုမှ')", "ADV", true);
                    }
                }
            }

            // 6i. V64 Bound Morphological Tone/Coda, Desiderative '-ချင်', Interrogative Hybrid & Causative Double-'က' Repairs
            bool mayHaveV64Morpho =
                ContainsMm(text, mmMask, "လပ်ခွင်") ||
                ContainsMm(text, mmMask, "စားခွင်") ||
                ContainsMm(text, mmMask, "ယူခွင်") ||
                ContainsMm(text, mmMask, "ရောက်ခွင်") ||
                ContainsMm(text, mmMask, "ဆိုခွင်") ||
                ContainsMm(text, mmMask, "ပိုင်ခွင်") ||
                ContainsMm(text, mmMask, "ယူပီ") ||
                ContainsMm(text, mmMask, "သောက်ပီ") ||
                ContainsMm(text, mmMask, "ကိုင်ပီ") ||
                ContainsMm(text, mmMask, "ကျောင်းပီ") ||
                ContainsMm(text, mmMask, "ပူဇော်ပြီ") ||
                ContainsMm(text, mmMask, "ကန်တော့ပြီ") ||
                ContainsMm(text, mmMask, "ဆုတောင်းပြီ") ||
                ContainsMm(text, mmMask, "လိုကောင်း") ||
                ContainsMm(text, mmMask, "ပယ်") ||
                ContainsMm(text, mmMask, "လုပ်ရယ်") ||
                ContainsMm(text, mmMask, "ထဲမာ") ||
                ContainsMm(text, mmMask, "နေမှာ") ||
                ContainsMm(text, mmMask, "လှ့") ||
                ContainsMm(text, mmMask, "ကျွန်တော်မှာ") ||
                ContainsMm(text, mmMask, "ကျွန်တော်ရဲ့") ||
                ContainsMm(text, mmMask, "ကျွန်တော ") ||
                ContainsMm(text, mmMask, "ကျွန်တောအ") ||
                ContainsMm(text, mmMask, "တွေရ ") ||
                ContainsMm(text, mmMask, "တို့ရ ") ||
                ContainsMm(text, mmMask, "မယ်သူ") ||
                ContainsMm(text, mmMask, "ပိုင်မှာ") ||
                ContainsMm(text, mmMask, "ပိုင်တွင်") ||
                ContainsMm(text, mmMask, "ပိုင်၌") ||
                ContainsMm(text, mmMask, "ရေလုပ်") ||
                ContainsMm(text, mmMask, "ရေစနစ်") ||
                ContainsMm(text, mmMask, "ရေကဏ္ဍ") ||
                ContainsMm(text, mmMask, "ပါတယ်လား") ||
                ContainsMm(text, mmMask, "ပါစေလား") ||
                ContainsMm(text, mmMask, "ခြင်းတည်") ||
                ContainsMm(text, mmMask, "တွေဂ") ||
                ContainsMm(text, mmMask, "များဂ") ||
                ContainsMm(text, mmMask, "လုပ်ငန်းထွင်") ||
                ContainsMm(text, mmMask, "မရှိဟော") ||
                ContainsMm(text, mmMask, "ဟို့") ||
                ContainsMm(text, mmMask, "ခြင်တယ်") ||
                ContainsMm(text, mmMask, "ခြင်သည်") ||
                ContainsMm(text, mmMask, "ခြင်ပါ") ||
                ContainsMm(text, mmMask, "ခြင်ဘူး") ||
                ((ContainsMm(text, mmMask, "တွေက ") || ContainsMm(text, mmMask, "များက ")) &&
                 (ContainsMm(text, mmMask, "စေတယ်") || ContainsMm(text, mmMask, "စေသည်") || ContainsMm(text, mmMask, "စေပါ") || ContainsMm(text, mmMask, "စေပြီး")));

            if (mayHaveV64Morpho)
            {
                foreach (Match m64 in V64MorphoAndCaseCollisionPattern.Matches(text))
                {
                    if (m64.Groups["Khwin"].Success && !OverlapsExistingIssue(issues, m64.Groups["Khwin"].Index, m64.Groups["Khwin"].Length))
                    {
                        Group g = m64.Groups["Khwin"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ခွင့်", ProofingIssueType.SpellingError,
                            "အခွင့်အရေး/ခွင့်ပြုချက်ပြ နောက်ဆက်ပုဒ်တွင် အောက်မြစ်ဖြင့် 'ခွင့်' ဟု ရေးသားရမည်။", "ခွင့်ပြုချက်ပြပုဒ် ('~ခွင့်')", "N", true);
                    }
                    else if (m64.Groups["YuPi"].Success && !OverlapsExistingIssue(issues, m64.Groups["YuPi"].Index, m64.Groups["YuPi"].Length))
                    {
                        Group g = m64.Groups["YuPi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပြီး", ProofingIssueType.SpellingError,
                            "ပြီးဆုံး/ဆက်စပ်ပြ ကြိယာထောက်ပစ္စည်းကို ရရစ် ဝစ္စပေါက်ဖြင့် 'ပြီး' ဟု ရေးသားရမည်။", "ကြိယာနောက်ဆက် ('~ပြီး')", "PART", true);
                    }
                    else if (m64.Groups["PuzawPyi"].Success && !OverlapsExistingIssue(issues, m64.Groups["PuzawPyi"].Index, m64.Groups["PuzawPyi"].Length))
                    {
                        Group g = m64.Groups["PuzawPyi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပြီး", ProofingIssueType.SpellingError,
                            "ရှေ့ကြိယာနှင့် နောက်ကြိယာ ဆက်စပ်ရာတွင် ဝစ္စပေါက်ဖြင့် 'ပြီး' ဟု ရေးသားရမည်။", "ကြိယာဆက်ပစ္စည်း ('~ပြီး')", "PART", true);
                    }
                    else if (m64.Groups["LoKaung"].Success && !OverlapsExistingIssue(issues, m64.Groups["LoKaung"].Index, m64.Groups["LoKaung"].Length))
                    {
                        Group g = m64.Groups["LoKaung"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လို့", ProofingIssueType.SpellingError,
                            "'~လို့ကောင်းသည်' ပုံစံတွင် အောက်မြစ်ဖြင့် 'လို့' ဟု ရေးသားရမည်။", "ကြိယာဆက်ပစ္စည်း ('~လို့ကောင်း')", "PART", false);
                    }
                    else if (m64.Groups["TaPae"].Success && !OverlapsExistingIssue(issues, m64.Groups["TaPae"].Index, m64.Groups["TaPae"].Length))
                    {
                        Group g = m64.Groups["TaPae"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပဲ", ProofingIssueType.SpellingError,
                            "အလေးအနက်ပြပစ္စည်းကို နောက်ပစ်ဖြင့် 'ပဲ' ဟု ရေးသားရမည် ('ပယ်' သည် ပယ်ဖျက်သည် ကြိယာဖြစ်သည်)။", "အလေးအနက်ပြပစ္စည်း ('~ပဲ')", "PART", true);
                    }
                    else if (m64.Groups["LokeYae"].Success && !OverlapsExistingIssue(issues, m64.Groups["LokeYae"].Index, m64.Groups["LokeYae"].Length))
                    {
                        Group g = m64.Groups["LokeYae"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လုပ်ရတယ်", ProofingIssueType.SpellingError,
                            $"ကြိယာအဆုံးသတ် ချို့ယွင်းနေပါသည် — '{g.Value}' အစား 'လုပ်ရတယ်' ဟု ရေးသားရမည်။", "ကြိယာအဆုံးသတ် ('လုပ်ရတယ်')", "V", false);
                    }
                    else if (m64.Groups["HtaeMa"].Success && !OverlapsExistingIssue(issues, m64.Groups["HtaeMa"].Index, m64.Groups["HtaeMa"].Length))
                    {
                        Group g = m64.Groups["HtaeMa"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ထဲမှာ", ProofingIssueType.SpellingError,
                            "နေရာပြ နာမ်ဝိဘတ်တွင် ဟထိုးဖြင့် 'ထဲမှာ' ဟု ရေးသားရမည်။", "နေရာပြဝိဘတ် ('~ထဲမှာ')", "PART", false);
                    }
                    else if (m64.Groups["XmasNay"].Success && !OverlapsExistingIssue(issues, m64.Groups["XmasNay"].Index, m64.Groups["XmasNay"].Length))
                    {
                        Group g = m64.Groups["XmasNay"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "နေ့မှာ", ProofingIssueType.SpellingError,
                            "ရက်/နေ့ရက် အနက်တွင် အောက်မြစ်ဖြင့် 'နေ့မှာ' ဟု ရေးသားရမည်။", "အချိန်ပြနာမ် ('~နေ့မှာ')", "N", false);
                    }
                    else if (m64.Groups["LhaCreaky"].Success && !OverlapsExistingIssue(issues, m64.Groups["LhaCreaky"].Index, m64.Groups["LhaCreaky"].Length))
                    {
                        Group g = m64.Groups["LhaCreaky"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လှ", ProofingIssueType.SpellingError,
                            "နာမဝိသေသန 'လှ' တွင် အောက်မြစ်မပါရပါ — 'လှ' ဟု ရေးသားရမည်။", "နာမဝိသေသန ('လှ')", "ADJ", true);
                    }
                else if ((m64.Groups["KyawntawCreaky"].Success || m64.Groups["KyawntawNoAsat"].Success))
                {
                    Group g = m64.Groups["KyawntawCreaky"].Success ? m64.Groups["KyawntawCreaky"] : m64.Groups["KyawntawNoAsat"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကျွန်တော့်", ProofingIssueType.SpellingError,
                            $"ဝိဘတ်ရှေ့ နာမ်စားတွင် အောက်မြစ်ဖြင့် 'ကျွန်တော့်' ဟု ရေးသားရမည်။", "နာမ်စား ('ကျွန်တော့်')", "PRON", true);
                    }
                }
                else if (m64.Groups["TweYa"].Success && !OverlapsExistingIssue(issues, m64.Groups["TweYa"].Index, m64.Groups["TweYa"].Length))
                {
                    Group g = m64.Groups["TweYa"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ရဲ့", ProofingIssueType.SpellingError,
                        "ပိုင်ဆိုင်မှုပြဝိဘတ်တွင် 'ရဲ့' ဟု ရေးသားရမည်။", "ပိုင်ဆိုင်မှုပြဝိဘတ် ('~ရဲ့')", "PART", false);
                }
                else if (m64.Groups["MaeThu"].Success && !OverlapsExistingIssue(issues, m64.Groups["MaeThu"].Index, m64.Groups["MaeThu"].Length))
                {
                    Group g = m64.Groups["MaeThu"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မယ့်", ProofingIssueType.SpellingError,
                        "နောက်လိုက်နာမ် ('သူ') ကို အထူးပြုရာတွင် အောက်မြစ်ဖြင့် 'မယ့်' ဟု ရေးသားရမည်။", "နာမဝိသေသနပြုပစ္စည်း ('~မယ့်')", "PART", false);
                }
                else if (m64.Groups["NyanayPaing"].Success && !OverlapsExistingIssue(issues, m64.Groups["NyanayPaing"].Index, m64.Groups["NyanayPaing"].Length))
                {
                    Group g = m64.Groups["NyanayPaing"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပိုင်း", ProofingIssueType.SpellingError,
                        "အချိန်အပိုင်းအခြားပြ ပစ္စည်းတွင် ဝစ္စပေါက်ဖြင့် 'ပိုင်း' ဟု ရေးသားရမည်။", "အချိန်ပိုင်းပြပုဒ် ('~ပိုင်း')", "N", true);
                }
                else if (m64.Groups["YeLupan"].Success && !OverlapsExistingIssue(issues, m64.Groups["YeLupan"].Index, m64.Groups["YeLupan"].Length))
                {
                    Group g = m64.Groups["YeLupan"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ရေး", ProofingIssueType.SpellingError,
                        "ကဏ္ဍ/လုပ်ငန်းအမည် ပေါင်းစပ်ပုဒ်တွင် ဝစ္စပေါက်ဖြင့် 'ရေး' ဟု ရေးသားရမည်။", "ပေါင်းစပ်နာမ်ပစ္စည်း ('~ရေး')", "PART", true,
                        "ကြိယာ/နာမ်ပေါင်းစပ်ပုဒ်နောက်ဆက် '~ရေး' ကို ဝစ္စပေါက်ဖြင့် ရေးသားရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (အဆက်တွဲဝိဘတ် နှင့် ပေါင်းစပ်ပုဒ်)");
                }
                else if (m64.Groups["PataeLar"].Success && !OverlapsExistingIssue(issues, m64.Groups["PataeLar"].Index, m64.Groups["PataeLar"].Length))
                {
                    Group g = m64.Groups["PataeLar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပါသလား", ProofingIssueType.OfficialStyleGrammarError,
                        "အမေးဝါကျအဆုံး၌ ဟုတ်မှန်ပြ 'တယ်' နှင့် 'လား' ရောထွေးနေပါသည် — 'ပါသလား' ဟု သုံးနှုန်းရမည်။", "အမေးဝါကျအဆုံးသတ် ('~ပါသလား')", "PART", true);
                }
                else if (m64.Groups["PasayLar"].Success && !OverlapsExistingIssue(issues, m64.Groups["PasayLar"].Index, m64.Groups["PasayLar"].Length))
                {
                    Group g = m64.Groups["PasayLar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပါစေ", ProofingIssueType.OfficialStyleGrammarError,
                        "ဆုတောင်းဝါကျ ('~ပါစေ') နောက်တွင် အမေးပစ္စည်း 'လား' မထည့်ရပါ — 'ပါစေ' ဟုသာ ရေးသားရမည်။", "ဆုတောင်းပစ္စည်း ('~ပါစေ')", "PART", true);
                }
                else if (m64.Groups["ChinnTae"].Success && !OverlapsExistingIssue(issues, m64.Groups["ChinnTae"].Index, m64.Groups["ChinnTae"].Length))
                {
                    Group g = m64.Groups["ChinnTae"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သည်", ProofingIssueType.SpellingError,
                        "ကတ္တားဝိဘတ်ကို သကြီးဖြင့် 'သည်' ဟု ရေးသားရမည်။", "ကတ္တားဝိဘတ် ('~သည်')", "PART", true);
                }
                else if (m64.Groups["TweGa"].Success && !OverlapsExistingIssue(issues, m64.Groups["TweGa"].Index, m64.Groups["TweGa"].Length))
                {
                    Group g = m64.Groups["TweGa"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "က", ProofingIssueType.SpellingError,
                        "ကတ္တားဝိဘတ်ကို ကကြီးဖြင့် 'က' ဟု ရေးသားရမည်။", "ကတ္တားဝိဘတ် ('~က')", "PART", true);
                }
                else if (m64.Groups["LupanHtwin"].Success && !OverlapsExistingIssue(issues, m64.Groups["LupanHtwin"].Index, m64.Groups["LupanHtwin"].Length))
                {
                    Group g = m64.Groups["LupanHtwin"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တွင်", ProofingIssueType.SpellingError,
                        "နေရာပြ နာမ်ဝိဘတ်ကို တဝမ်းပူဖြင့် 'တွင်' ဟု ရေးသားရမည်။", "နေရာပြဝိဘတ် ('~တွင်')", "PART", true);
                }
                else if (m64.Groups["MashiHaw"].Success && !OverlapsExistingIssue(issues, m64.Groups["MashiHaw"].Index, m64.Groups["MashiHaw"].Length))
                {
                    Group g = m64.Groups["MashiHaw"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သော", ProofingIssueType.SpellingError,
                        "နာမဝိသေသနပြုပစ္စည်းကို သကြီးဖြင့် 'သော' ဟု ရေးသားရမည်။", "နာမဝိသေသနပြုပစ္စည်း ('~သော')", "PART", true);
                }
                else if (m64.Groups["HuHo"].Success && !OverlapsExistingIssue(issues, m64.Groups["HuHo"].Index, m64.Groups["HuHo"].Length))
                {
                    Group g = m64.Groups["HuHo"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သို့", ProofingIssueType.SpellingError,
                        "ရှေးရှုရာပြ နာမ်ဝိဘတ်ကို သကြီးဖြင့် 'သို့' ဟု ရေးသားရမည်။", "ရှေးရှုရာပြဝိဘတ် ('~သို့')", "PART", true);
                }
                else if (m64.Groups["DesidChin"].Success && !OverlapsExistingIssue(issues, m64.Groups["DesidChin"].Index, m64.Groups["DesidChin"].Length))
                {
                    Group g = m64.Groups["DesidChin"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ချင်", ProofingIssueType.SpellingError,
                        "ဆန္ဒပြ ကြိယာထောက်ပစ္စည်းကို ယပင့်ဖြင့် 'ချင်' ဟု ရေးသားရမည် ('ခြင်' သည် ပိုးခြင် ဖြစ်သည်)။", "ဆန္ဒပြ ကြိယာထောက်ပစ္စည်း ('~ချင်')", "PART", true);
                }
                else if (m64.Groups["CausPat"].Success && !OverlapsExistingIssue(issues, m64.Groups["CausPat"].Index, m64.Groups["CausPat"].Length))
                {
                    Group g = m64.Groups["CausPat"];
                    string fixedKo = g.Value.Substring(0, g.Value.Length - 1) + "ကို";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, fixedKo, ProofingIssueType.OfficialStyleGrammarError,
                        $"စေခိုင်း/ဖြစ်စေကြောင်းပြ ('~စေ') ဝါကျတွင် ရှေ့၌ ကတ္တား ('~က') ပါရှိပြီးဖြစ်သဖြင့် ခံရသူပုဒ်၌ ကံဝိဘတ် '{fixedKo}' ကို သုံးနှုန်းရမည်။",
                        $"ကံဝိဘတ် ('{fixedKo}')", "PART", true,
                        "ပြုလုပ်စေတတ်သော ကတ္တားပုဒ် ('~က') ပါရှိပြီးဖြစ်၍ အကျိုးသက်ရောက်ခံရသော ဒုတိယနာမ်ပုဒ်တွင် ကတ္တား 'က' ထပ်မသုံးရဘဲ ကံဝိဘတ် 'ကို' သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ကံဝိဘတ် စည်းမျဉ်း)");
                }
                }
            }

            // 6j. V65 Case-Role Collision, Causative Gerund Subject & Polarity/Additive Particle Repairs
            bool mayHaveV65Morpho =
                ContainsMm(text, mmMask, "ဘာမမဟုတ်") ||
                ContainsMm(text, mmMask, " ဘဲ") ||
                ContainsMm(text, mmMask, "မြတ်စွာဘုရားအား") ||
                ContainsMm(text, mmMask, "အရေးပါ") ||
                ContainsMm(text, mmMask, "အရေးကြီ") ||
                ContainsMm(text, mmMask, "လုပ်တာကို ") ||
                ContainsMm(text, mmMask, "ခြင်းကို ") ||
                ContainsMm(text, mmMask, "တိုးမြှင့်ပေး") ||
                ContainsMm(text, mmMask, "ဖြစ်စေ") ||
                ContainsMm(text, mmMask, "ရွေးချယ်နိုင်") ||
                ContainsMm(text, mmMask, "သတိပြုသင့်") ||
                ContainsMm(text, mmMask, "ရေဒီယိုက ") ||
                ContainsMm(text, mmMask, "ကူညီပေး") ||
                ContainsMm(text, mmMask, "ရှိသလို ") ||
                ContainsMm(text, mmMask, "လဲရှိပါ") ||
                ContainsMm(text, mmMask, "အတွင်းမှာလဲ") ||
                ContainsMm(text, mmMask, "ယဉ်ကျေးမှု") ||
                ContainsMm(text, mmMask, "ဥပမာ") ||
                ContainsMm(text, mmMask, "မရှိမဖြစ်လိုအပ်") ||
                ContainsMm(text, mmMask, "ကသော ") ||
                ContainsMm(text, mmMask, "စတင်နိုင်") ||
                ContainsMm(text, mmMask, "ပေးပို့ ");

            if (mayHaveV65Morpho)
            {
                const string v65Auth = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် နှင့် ပစ္စည်းစည်းမျဉ်း)";
                foreach (Match m65 in V65CaseAndMorphoPattern.Matches(text))
                {
                    if (m65.Groups["BaMa"].Success && !OverlapsExistingIssue(issues, m65.Groups["BaMa"].Index, m65.Groups["BaMa"].Length))
                    {
                        Group g = m65.Groups["BaMa"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မှ", ProofingIssueType.SpellingError,
                            "'ဘာမှမဟုတ်' ပုံစံတွင် ဟထိုးဖြင့် 'မှ' ဟု ရေးသားရမည်။", "အလေးအနက်ပြပစ္စည်း ('ဘာမှ~')", "PART", false,
                            "အငြင်းကြိယာရှေ့ အလေးအနက်ပြပစ္စည်းတွင် 'ဘာမှ' ဟု ဟထိုးဖြင့် ရေးသားရမည်။", v65Auth);
                    }
                    else if (m65.Groups["SpacedBae"].Success && !OverlapsExistingIssue(issues, m65.Groups["SpacedBae"].Index, m65.Groups["SpacedBae"].Length))
                    {
                        Group g = m65.Groups["SpacedBae"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပဲ", ProofingIssueType.OfficialStyleGrammarError,
                            "ဟုတ်မှန်/အလေးအနက်ပြ ပစ္စည်းတွင် 'ဘဲ' အစား 'ပဲ' ဟု ရေးသားရမည်။", "အလေးအနက်ပြပစ္စည်း ('~ပဲ')", "PART", false,
                            "အငြင်းကြိယာ ('မ~') မဟုတ်သဖြင့် အလေးအနက်ပြပစ္စည်း 'ပဲ' ကို သုံးနှုန်းရမည်။", v65Auth);
                    }
                    else if (m65.Groups["ToThint"].Success && !OverlapsExistingIssue(issues, m65.Groups["ToThint"].Index, m65.Groups["ToThint"].Length))
                    {
                        Group g = m65.Groups["ToThint"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သည် ", ProofingIssueType.OfficialStyleGrammarError,
                            "ကတ္တားနာမ်ပုဒ်နောက်တွင် အောက်မြစ်မပါဘဲ ကတ္တားဝိဘတ် 'သည် ' ဟု ပုဒ်ဖြတ်ရေးသားရမည်။", "ကတ္တားဝိဘတ် ('~သည်')", "PART", true,
                            "နောက်လိုက်ပုဒ်သည် ကံပုဒ် ('မြတ်စွာဘုရားအား') ဖြစ်၍ နာမဝိသေသနပြု 'သည့်' အစား ကတ္တားဝိဘတ် 'သည်' သုံးရမည်။", v65Auth);
                    }
                    else if ((m65.Groups["BenefKoHa"].Success || m65.Groups["CopToKoHa"].Success))
                    {
                        Group g = m65.Groups["BenefKoHa"].Success ? m65.Groups["BenefKoHa"] : m65.Groups["CopToKoHa"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဟာ", ProofingIssueType.OfficialStyleGrammarError,
                                "ကတ္တားပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား ကတ္တားဝိဘတ် 'ဟာ' (သို့မဟုတ် 'သည်') ကို သုံးနှုန်းရမည်။", "ကတ္တားဝိဘတ် ('~ဟာ')", "PART", false,
                                "ဖြစ်ခြင်း/အရေးပါခြင်းပြ ကြိယာ၏ ကတ္တားပုဒ်ဖြစ်၍ ကံဝိဘတ် 'ကို' မသုံးရဘဲ ကတ္တားဝိဘတ် 'ဟာ' သုံးရမည်။", v65Auth);
                        }
                    }
                    else if ((m65.Groups["TaKoKa"].Success || m65.Groups["GerundKoKa"].Success || m65.Groups["DoubleKoKa"].Success || m65.Groups["CopTweKoKa"].Success || m65.Groups["VitMhaKa"].Success))
                    {
                        Group g = m65.Groups["TaKoKa"].Success ? m65.Groups["TaKoKa"]
                                : m65.Groups["GerundKoKa"].Success ? m65.Groups["GerundKoKa"]
                                : m65.Groups["DoubleKoKa"].Success ? m65.Groups["DoubleKoKa"]
                                : m65.Groups["CopTweKoKa"].Success ? m65.Groups["CopTweKoKa"]
                                : m65.Groups["VitMhaKa"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "က", ProofingIssueType.OfficialStyleGrammarError,
                                $"ကတ္တားပုဒ်တွင် '{g.Value}' အစား ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည်။", "ကတ္တားဝိဘတ် ('~က')", "PART", false,
                                "ဝါကျ၏ ပြုလုပ်သူ/အကြောင်းရင်းခံ ကတ္တားပုဒ်ဖြစ်၍ ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည်။", v65Auth);
                        }
                    }
                    else if ((m65.Groups["ChooseKaKo"].Success || m65.Groups["NoticeKaKo"].Success || m65.Groups["RadioKaKo"].Success || m65.Groups["HelpKaKo"].Success))
                    {
                        Group g = m65.Groups["ChooseKaKo"].Success ? m65.Groups["ChooseKaKo"]
                                : m65.Groups["NoticeKaKo"].Success ? m65.Groups["NoticeKaKo"]
                                : m65.Groups["RadioKaKo"].Success ? m65.Groups["RadioKaKo"]
                                : m65.Groups["HelpKaKo"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကို", ProofingIssueType.OfficialStyleGrammarError,
                                "ကံပုဒ်တွင် ကတ္တားဝိဘတ် 'က' အစား ကံဝိဘတ် 'ကို' ကို သုံးနှုန်းရမည်။", "ကံဝိဘတ် ('~ကို')", "PART", true,
                                "ကံလိုကြိယာ၏ ခံရသူ ကံပုဒ်ဖြစ်သဖြင့် ကတ္တားဝိဘတ် 'က' အစား ကံဝိဘတ် 'ကို' သုံးရမည်။", v65Auth);
                        }
                    }
                    else if ((m65.Groups["PaeLae"].Success || m65.Groups["AffLae1"].Success || m65.Groups["AffLae2"].Success))
                    {
                        Group g = m65.Groups["PaeLae"].Success ? m65.Groups["PaeLae"]
                                : m65.Groups["AffLae1"].Success ? m65.Groups["AffLae1"]
                                : m65.Groups["AffLae2"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လည်း", ProofingIssueType.OfficialStyleGrammarError,
                                $"အပေါင်းအစုပြ ပစ္စည်းတွင် '{g.Value}' အစား 'လည်း' ဟု ရေးသားရမည်။", "အပေါင်းအစုပြပစ္စည်း ('~လည်း')", "PART", true,
                                "ရှေ့အချက်အပြင် ထပ်လောင်းပါဝင်ကြောင်းပြသဖြင့် အပေါင်းအစုပြပစ္စည်း 'လည်း' ကို သုံးနှုန်းရမည်။", v65Auth);
                        }
                    }
                    else if (m65.Groups["DevKoMha"].Success && !OverlapsExistingIssue(issues, m65.Groups["DevKoMha"].Index, m65.Groups["DevKoMha"].Length))
                    {
                        Group g = m65.Groups["DevKoMha"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မှာ", ProofingIssueType.OfficialStyleGrammarError,
                            "ကဏ္ဍ/နေရာပြပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား နေရာပြဝိဘတ် 'မှာ' (သို့မဟုတ် 'တွင်') ကို သုံးနှုန်းရမည်။", "နေရာပြဝိဘတ် ('~မှာ')", "PART", false,
                            "'အရေးပါသည်' ကြိယာနှင့် တွဲဖက်ရာတွင် နေရာ/ကဏ္ဍပြဝိဘတ် 'မှာ/တွင်' သုံးနှုန်းရမည်။", v65Auth);
                    }
                    else if (m65.Groups["TopicThaw"].Success && !OverlapsExistingIssue(issues, m65.Groups["TopicThaw"].Index, m65.Groups["TopicThaw"].Length))
                    {
                        Group g = m65.Groups["TopicThaw"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တော့", ProofingIssueType.SpellingError,
                            "အကြောင်းအရာခွဲခြားပြပစ္စည်းတွင် '~ကသော' အစား '~ကတော့' ဟု ရေးသားရမည် ('သော' သည် ကြိယာ/နာမဝိသေသနနောက်၌သာ လိုက်သည်)။",
                            "အကြောင်းအရာအညွှန်းပစ္စည်း ('~ကတော့')", "PART", false,
                            "ကတ္တားဝိဘတ် 'က' နောက်တွင် နာမဝိသေသနပြု 'သော' မလိုက်နိုင်ဘဲ အကြောင်းအရာအသားပေးပစ္စည်း 'တော့' ('~ကတော့') ဖြစ်ရမည်။", v65Auth);
                    }
                    else if (m65.Groups["StartKo"].Success && !OverlapsExistingIssue(issues, m65.Groups["StartKo"].Index, m65.Groups["StartKo"].Length))
                    {
                        Group g = m65.Groups["StartKo"];
                        string srcPost = styleMode == ProofingStyleMode.FormalOfficial ? "မှ" : "ကနေ";
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, srcPost, ProofingIssueType.OfficialStyleGrammarError,
                            $"'စတင်နိုင်-' ရှေ့ အရင်းခံပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား စတင်ထွက်ခွာရာပြဝိဘတ် '{srcPost}' ကို သုံးနှုန်းရမည်။",
                            $"စတင်ရာပြဝိဘတ် ('~{srcPost}')", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                            "စတင်ဖြစ်ပေါ်ရာ အရင်းအမြစ်ပုဒ်ဖြစ်သဖြင့် ကံဝိဘတ် 'ကို' အစား ထွက်ခွာရာ/စတင်ရာပြဝိဘတ် 'ကနေ/မှ' ကို သုံးရမည်။", v65Auth);
                    }
                    else if (m65.Groups["PurpPayPo"].Success && !OverlapsExistingIssue(issues, m65.Groups["PurpPayPo"].Index, m65.Groups["PurpPayPo"].Length))
                    {
                        Group g = m65.Groups["PurpPayPo"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပေးဖို့", ProofingIssueType.SpellingError,
                            "'မျှော်လင့်-' ရှေ့ ရည်ရွယ်ချက်ပြပစ္စည်းတွင် 'ပေးပို့' အစား 'ပေးဖို့' (ဖဦးထုပ်) ဟု ရေးသားရမည်။",
                            "ရည်ရွယ်ချက်ပြ ကြိယာနောက်ဆက် ('~ပေးဖို့')", "PART", false,
                            "နောက်၌ 'မျှော်လင့်-' လိုက်နေသဖြင့် ပို့ဆောင်ခြင်းပြ 'ပို့' မဟုတ်ဘဲ ရည်ရွယ်ချက်ပြပစ္စည်း 'ဖို့' ('~ပေးဖို့') ဖြစ်ရမည်။", v65Auth);
                    }
                }
            }

            // 6j. v71 Hybrid SOV Clause-Final Extraposition Parser, Phrase-Order Syntax & Majority-Register Intra-Clause Concord
            ScanHybridSovClauseAndRegisterConcord(text, mmMask, styleMode, trie, issues);

            // 6k. V66 Bidirectional Intra-Sentence Register Harmony, Case-Role & Tense-Aspect Repairs
            ScanV66RegisterAndCaseHarmony(text, mmMask, issues);

            // 7. Trailing ASCII period '.' right after Myanmar sentence-final verb closer ('ရှိပါတယ်. -> ရှိပါတယ်။')
            if (text.IndexOf('.') >= 0)
            {
                foreach (Match m in MyanmarSentenceFinalAsciiDotPattern.Matches(text))
                {
                    Group g = m.Groups["Dot"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, ".", "။",
                            ProofingIssueType.PunctuationError,
                            "မြန်မာဝါကျအဆုံးတွင် အင်္ဂလိပ် Full Stop ('.') အစား မြန်မာပုဒ်မ ('။') ကို သုံးနှုန်းရမည်။",
                            "မြန်မာပုဒ်မ ('။')", "PUNCT", true);
                    }
                }
            }
        }

        private static readonly (string Trigger, Regex Pattern, string Replacement, ProofingIssueType Type, string Cue, string Reason)[] V66HarmonyRules =
        {
            ("စီရင်ချက်ကို", new Regex(@"တရားရုံးတော်မှ\s+ချမှတ်သော\s+စီရင်ချက်ကို\s+(?<T>မကြိုက်ဘူး\s+ဘာဖြစ်လို့လဲ\s+ဆိုတော့\s+မတရားလို့ပဲ)", RegexOptions.CultureInvariant), "သဘောမတူကြောင်းနှင့် မတရားမှုရှိကြောင်း ထုတ်ဖော်ပြောဆိုကြသည်", ProofingIssueType.OfficialStyleGrammarError, "တရားရုံး/သတင်းစာပေ အရေးအသား", "တရားရုံးတော်ဆိုင်ရာ စာပေဝါကျတွင် အရပ်သုံးစကားအစား စာပေသတင်းအရေးအသားဖြင့် ညီညွတ်စွာ ရေးသားရမည်။"),
            ("ကိုယ်စားလှယ်က", new Regex(@"လွှတ်တော်ကိုယ်စားလှယ်က\s+ပြောတာ\s+(?<T>ဟုတ်ဘူး\s+ဟယ်\s+ဗျ\s+သူပြောတာ\s+ညာသွားတာ)", RegexOptions.CultureInvariant), "မှန်ကန်မှုမရှိကြောင်း ဝေဖန်ခဲ့ကြသည်", ProofingIssueType.OfficialStyleGrammarError, "လွှတ်တော်/သတင်းစာပေ အရေးအသား", "လွှတ်တော်ဆိုင်ရာ သတင်းဝါကျတွင် အရပ်သုံးဗန်းစကားအစား စာပေအရေးအသားဖြင့် ညီညွတ်စွာ ရေးသားရမည်။"),
            ("ခဲ့ပါသည်၊", new Regex(@"(?:ပြီးတော့)\s+[\u1000-\u103F]{2,12}?(?<T>ခဲ့ပါသည်)(?=၊\s*ဒါပေမဲ့\s+[^\u104B]+?တယ်။)", RegexOptions.CultureInvariant), "ခဲ့တယ်", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ကြိယာအဆုံးသတ် ('~ခဲ့တယ်')", "ရှေ့နောက်ဝါကျခွဲနှစ်ခုလုံး အပြောစကား ('ပြီးတော့ ... ဒါပေမဲ့ ... တယ်') ဖြစ်သဖြင့် 'ခဲ့တယ်' ဟု စကားအဆင့် ညီညွတ်စွာ သုံးရမည်။"),
            ("ခဲ့ပါသည်၊", new Regex(@"(?:လို့|သူငယ်ချင်းတွေနဲ့)\s+[\u1000-\u103F]{2,14}?(?<T>ခဲ့ပါသည်)(?=၊\s*(?:ဒါပေမဲ့\s+[^\u104B]+?ဘူး|အရမ်း[^\u104B]+?တယ်။))", RegexOptions.CultureInvariant), "ခဲ့ပါတယ်", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ကြိယာအဆုံးသတ် ('~ခဲ့ပါတယ်')", "တစ်ဝါကျတည်းအတွင်း အပြောစကားနှင့် စကားအဆင့် ညီညွတ်စေရန် 'ခဲ့ပါတယ်' ဟု သုံးနှုန်းရမည်။"),
            ("ပြီးပါသည်၊", new Regex(@"ဤ[\u1000-\u103F]+ကို\s+[^\u104A\u104B]+?ပြီးပါသည်၊\s*ထို့ကြောင့်\s+အောင်မြင်(?<T>မှာပါ။)", RegexOptions.CultureInvariant), "မည်ဟု ယူဆပါသည်။", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် ဝါကျအဆုံးသတ်", "ရှေ့ဝါကျခွဲတွင် 'ဤ ... ပြီးပါသည်၊ ထို့ကြောင့်' ဟု စာပေဟန်သုံးထားသဖြင့် အဆုံးသတ်၌လည်း စာပေဟန်ဖြင့် ညီညွတ်စွာ ပိတ်ရမည်။"),
            ("ခဲ့ပါသည်၊", new Regex(@"ခဲ့ပါသည်၊\s*ပြီးတော့\s+(?<T>ကောင်းမှုတွေ\s+လုပ်ခဲ့တယ်)(?=။)", RegexOptions.CultureInvariant), "ကောင်းမှုများ ပြုလုပ်ခဲ့ပါသည်", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် ဝါကျခွဲ ('ကောင်းမှုများ ပြုလုပ်ခဲ့ပါသည်')", "ပထမဝါကျခွဲ ('~ခဲ့ပါသည်၊') နှင့် စကားအဆင့် ညီညွတ်စေရန် စာပေဟန်ဖြင့် ရေးသားရမည်။"),
            ("များသည် ", new Regex(@"များနှင့်\s+[\u1000-\u103F]+များသည်\s+[^\u104A\u104B]{4,40}?(?<T>တယ်)(?=။)", RegexOptions.CultureInvariant), "သည်", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် ကြိယာအဆုံးသတ် ('~သည်')", "စာပေဟန် ကတ္တားပုဒ် ('~များနှင့် ~များသည်') နှင့် ညီညွတ်စေရန် အဆုံးသတ်၌ 'သည်' ကို သုံးနှုန်းရမည်။"),
            ("လူတိုင်းအတွက်", new Regex(@"ဆိုတာ\s+လူတိုင်းအတွက်\s+[^\u104A\u104B]{4,25}?(?<T>ရမည်။)", RegexOptions.CultureInvariant), "ရမယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ကြိယာအဆုံးသတ် ('~ရမယ်')", "အပြောစကား 'ဆိုတာ' နှင့် ညီညွတ်စေရန် 'ရမယ်။' ဟု သုံးနှုန်းရမည်။"),
            ("ဥပဒေဆိုတာ", new Regex(@"ဥပဒေဆိုတာ\s+[^\u104A\u104B]{4,25}?(?<T>ထိန်းသိမ်းပေးပါသည်)$", RegexOptions.CultureInvariant), "ထိန်းသိမ်းပေးတယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ကြိယာအဆုံးသတ်", "အပြောစကား 'ဆိုတာ' နှင့် ညီညွတ်စေရန် 'ထိန်းသိမ်းပေးတယ်။' ဟု သုံးနှုန်းရမည်။"),
            ("ကျွန်ုပ်၏", new Regex(@"တွေက\s+(?<T>ကျွန်ုပ်၏)(?=\s+[\u1000-\u103F]+တွေကို)", RegexOptions.CultureInvariant), "ကျွန်တော့်ရဲ့", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား နာမ်စား ('ကျွန်တော့်ရဲ့')", "အပြောစကားဝါကျ ('~တွေက ... ~တွေကို ... တယ်') အတွင်း 'ကျွန်ုပ်၏' အစား 'ကျွန်တော့်ရဲ့' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။"),
            ("ကောင်းသည်။", new Regex(@"(?:စားရတာ|ခံစားရတာ)\s+(?:ပျော်စရာ|အရမ်း)ကောင်း(?<T>သည်။)", RegexOptions.CultureInvariant), "တယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား အဆုံးသတ် ('~တယ်။')", "အပြောစကား '~ရတာ / အရမ်း' နှင့် ညီညွတ်စေရန် 'တယ်။' ဟု အဆုံးသတ်ရမည်။"),
            ("အရသာရှိ", new Regex(@"ရှမ်းခေါက်ဆွဲက\s+အရမ်းအရသာ(?<T>ရှိသည်။|ရှိမည်(?=။))", RegexOptions.CultureInvariant), "ရှိတယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ပစ္စုပ္ပန်ပြ ('ရှိတယ်')", "အပြောစကား 'အရမ်း' နှင့် ပုံမှန်အရသာဖော်ပြချက်အရ 'ရှိတယ်' ဟု သုံးနှုန်းရမည်။"),
            ("ပါသည်။", new Regex(@"(?:(?:လူငယ်တွေ|ဆေးမြီးတိုတွေက)\s+[^\u104A\u104B]{6,35}?(?:သင့်|စေ)|အရက်သောက်တာက\s+[^\u104A\u104B]{6,25}?နိုင်|သူငယ်ချင်းတွေနဲ့အတူ\s+ကြည့်ရတာ\s+ပိုပျော်)(?<T>ပါသည်။)", RegexOptions.CultureInvariant), "တယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား အဆုံးသတ် ('~တယ်။')", "ရှေ့ပိုင်း အပြောစကားအသုံးအနှုန်း ('~တွေ / ~တာက / ~နဲ့အတူ') နှင့် ညီညွတ်စေရန် 'တယ်။' ဟု အဆုံးသတ်ရမည်။"),
            ("အခါ၌", new Regex(@"ဟင်းချက်(?<T>အခါ၌)(?=\s+)", RegexOptions.CultureInvariant), "တဲ့အခါမှာ", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား အချိန်ပြပုဒ် ('~တဲ့အခါမှာ')", "အပြောစကားဝါကျ ('~ပဲ။') နှင့် ကြိယာနောက်ဆက် မှန်ကန်စေရန် 'ဟင်းချက်တဲ့အခါမှာ' ဟု သုံးနှုန်းရမည်။"),
            ("တွင်လည်း", new Regex(@"ဆေးပညာ(?<T>တွင်လည်း)(?=\s+[\u1000-\u103F]+တွေက\s+[^\u104B]+?တယ်။)", RegexOptions.CultureInvariant), "မှာလည်း", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား နေရာပြဝိဘတ် ('~မှာလည်း')", "အပြောစကားဝါကျ ('~တွေက ... တယ်။') အတွင်း 'တွင်လည်း' အစား 'မှာလည်း' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။"),
            ("နိုင်သည်", new Regex(@"လူအများကြီးနဲ့\s+[^\u104B]{6,35}?(?<T>နိုင်သည်)$", RegexOptions.CultureInvariant), "နိုင်တယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ဝါကျအဆုံးသတ် ('နိုင်တယ်။')", "အပြောစကား ('~နဲ့ / ~တွေ') နှင့် ညီညွတ်စေရန် 'နိုင်တယ်။' ဟု အဆုံးသတ်ရမည်။"),
            ("ချောင်းများမှာ", new Regex(@"မြစ်တွေ၊\s*(?<T>ချောင်းများမှာ)", RegexOptions.CultureInvariant), "ချောင်းတွေမှာ", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား အများပြပစ္စည်း ('ချောင်းတွေမှာ')", "'မြစ်တွေ' နှင့် စကားအဆင့် ညီညွတ်စေရန် 'ချောင်းတွေမှာ' ဟု သုံးနှုန်းရမည်။"),
            ("ပြည်သူတွေ", new Regex(@"မျိုးချစ်စိတ်ဓာတ်ဖြင့်\s+(?<T>ပြည်သူတွေ)(?=\s+[^\u104B]+?သည်။)", RegexOptions.CultureInvariant), "ပြည်သူများ", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် အများပြပစ္စည်း ('ပြည်သူများ')", "စာပေအရေးအသား ('~ဖြင့် ... သည်။') အတွင်း 'ပြည်သူတွေ' အစား 'ပြည်သူများ' ကို သုံးနှုန်းရမည်။"),
            ("သိမ်းထားပေါ့။", new Regex(@"ပြေစာ\s+(?<T>သိမ်းထားပေါ့။)", RegexOptions.CultureInvariant), "သိမ်းထားပါ။", ProofingIssueType.OfficialStyleGrammarError, "စံ/ယဉ်ကျေးသော အဆုံးသတ် ('သိမ်းထားပါ။')", "အခွန်ပြေစာဆိုင်ရာ လမ်းညွှန်ဝါကျတွင် 'ပေါ့' အစား 'သိမ်းထားပါ။' ဟု သုံးနှုန်းရမည်။"),
            ("ပါမောက္ခက", new Regex(@"ပါမောက္ခက\s+(?<T>ဒါ)(?=ကို\s+)", RegexOptions.CultureInvariant), "ယင်းကိစ္စ", ProofingIssueType.OfficialStyleGrammarError, "စာပေ/ပညာရပ်သုံး အညွှန်းပုဒ် ('ယင်းကိစ္စ')", "ပညာရပ်ဆိုင်ရာ ဝါကျတွင် အရပ်သုံး 'ဒါ' အစား 'ယင်းကိစ္စ' ကို သုံးနှုန်းရမည်။"),
            ("သိပ္ပံပညာရှင်တွေက", new Regex(@"သိပ္ပံပညာရှင်တွေက\s+(?<T>ဒါ)(?=ကို\s+)", RegexOptions.CultureInvariant), "ယင်း", ProofingIssueType.OfficialStyleGrammarError, "စာပေ/ပညာရပ်သုံး ညွှန်းပြနာမ်စား ('ယင်း')", "ပညာရပ်ဆိုင်ရာ ဝါကျတွင် အရပ်သုံး 'ဒါ' အစား 'ယင်း' ကို သုံးနှုန်းရမည်။"),
            ("ပေးလိုက်ပြီ", new Regex(@"ဝန်ကြီးဌာနက\s+ထုတ်ပြန်ချက်အရ\s+[^\u104B]{6,30}?တိုး(?<T>ပေးလိုက်ပြီ)(?=။)", RegexOptions.CultureInvariant), "ပေးခဲ့ပြီဖြစ်သည်", ProofingIssueType.OfficialStyleGrammarError, "ရုံးသုံး/သတင်းစာပေ ကြိယာအဆုံးသတ်", "ဝန်ကြီးဌာန ထုတ်ပြန်ချက် သတင်းဝါကျတွင် 'ပေးခဲ့ပြီဖြစ်သည်' ဟု စာပေဟန်ဖြင့် သုံးနှုန်းရမည်။"),
            ("ကြည့်ရှုပေးပါမယ်", new Regex(@"အုပ်ချုပ်ရေးအဖွဲ့က\s+ဒီကိစ္စကို\s+အမြန်ဆုံး\s+(?<T>ကြည့်ရှုပေးပါမယ်)(?=\s+လို့\s+ပြောခဲ့သည်။)", RegexOptions.CultureInvariant), "ဆောင်ရွက်ပေးမည်ဖြစ်ကြောင်း", ProofingIssueType.OfficialStyleGrammarError, "သတင်းစာပေ အဆင့်မြင့်အသုံး", "အုပ်ချုပ်ရေးအဖွဲ့၏ တရားဝင်ပြောကြားချက် သတင်းဝါကျတွင် 'ဆောင်ရွက်ပေးမည်ဖြစ်ကြောင်း' ဟု သုံးနှုန်းရမည်။"),
            ("ပါဝင်တယ်။", new Regex(@"ငြိမ်းချမ်းရေး၌\s+[^\u104B]{6,30}?များလည်း\s+(?<T>ပါဝင်တယ်။)", RegexOptions.CultureInvariant), "ပါဝင်သည်။", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် ဝါကျအဆုံးသတ် ('ပါဝင်သည်။')", "စာပေဟန် ('~နှင့် ~၌ ... ~များလည်း') နှင့် ညီညွတ်စေရန် 'ပါဝင်သည်။' ဟု အဆုံးသတ်ရမည်။"),
            ("လုပ်တာက", new Regex(@"(?:လေ့ကျင့်ခန်း|အားကစား)(?<T>လုပ်တာက)(?=\s+[^\u104B]{6,30}?(?:စေ|ကောင်း)သည်။)", RegexOptions.CultureInvariant), "လုပ်ခြင်းသည်", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် နာမ်ပြုကတ္တား ('~လုပ်ခြင်းသည်')", "ဝါကျအဆုံး၌ စာပေဟန် 'သည်။' သုံးထားသဖြင့် ရှေ့ကတ္တား၌လည်း 'လုပ်ခြင်းသည်' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။"),
            ("ဆောင်ရွက်တာက", new Regex(@"ဥပဒေနဲ့အညီ\s+(?<T>ဆောင်ရွက်တာက)(?=\s+တရားမျှတမှု)", RegexOptions.CultureInvariant), "ဆောင်ရွက်ခြင်းက", ProofingIssueType.OfficialStyleGrammarError, "စံနာမ်ပြုပစ္စည်း ('ဆောင်ရွက်ခြင်းက')", "ဥပဒေဆိုင်ရာ ဝါကျတွင် 'ဆောင်ရွက်ခြင်းက' ဟု သုံးနှုန်းရမည်။"),
            ("ပြီးတဲ့အခါကတော့", new Regex(@"ပြီးတဲ့အခါ(?<T>ကတော့)", RegexOptions.CultureInvariant), "မှာတော့", ProofingIssueType.OfficialStyleGrammarError, "အချိန်ပြဝိဘတ် ('~မှာတော့')", "အချိန်ပြ 'အခါ' နောက်တွင် ကတ္တား 'ကတော့' အစား အချိန်/နေရာပြ 'မှာတော့' ကို သုံးနှုန်းရမည်။"),
            ("စားပွဲပေါ်မှာ", new Regex(@"(?:စားပွဲထိုး|စားပဲွထိုး)(?<T>ကို)(?=\s+စားပွဲပေါ်မှာ)", RegexOptions.CultureInvariant), "က", ProofingIssueType.OfficialStyleGrammarError, "ကတ္တားဝိဘတ် ('~က')", "ပြုလုပ်သူ ကတ္တားပုဒ်ဖြစ်၍ ကံဝိဘတ် 'ကို' အစား ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည်။"),
            ("သူရဲကောင်းတို့က", new Regex(@"သူရဲကောင်း(?<T>တို့က)(?=\s+နိုင်ငံတော်အတွက်\s+အသက်ပေးခဲ့ကြသည်။)", RegexOptions.CultureInvariant), "တို့သည်", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် ကတ္တားဝိဘတ် ('~တို့သည်')", "စာပေဂုဏ်ပြုဝါကျတွင် ကတ္တားဝိဘတ် 'တို့သည်' ကို သုံးနှုန်းရမည်။"),
            ("သူရဲကောင်းတို့ရဲ့", new Regex(@"သူရဲကောင်း(?<T>တို့ရဲ့)(?=\s+[^\u104B]+?ပါမည်။)", RegexOptions.CultureInvariant), "တို့၏", ProofingIssueType.OfficialStyleGrammarError, "စာပေဟန် ပိုင်ဆိုင်ပြဝိဘတ် ('~တို့၏')", "ဝါကျအဆုံး၌ စာပေဟန် 'ပါမည်။' သုံးထားသဖြင့် 'တို့၏' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။"),
            ("မျိုးစိတ်တွေ", new Regex(@"တောရိုင်းတိရစ္ဆာန်(?<T>တွေကို)(?=\s+မျိုးစိတ်တွေ)", RegexOptions.CultureInvariant), "တွေရဲ့", ProofingIssueType.OfficialStyleGrammarError, "ပိုင်ဆိုင်ပြဝိဘတ် ('~တွေရဲ့')", "နောက်လိုက်နာမ် 'မျိုးစိတ်တွေ' ကို ပိုင်ဆိုင်အထူးပြုသဖြင့် 'တွေရဲ့' ဟု သုံးနှုန်းရမည်။"),
            ("ကျွမ်းကျင်မှုတွေနဲ့", new Regex(@"ကျွမ်းကျင်မှု(?<T>တွေနဲ့)(?=\s+[^\u104B]+?အတွက်\s+အရေးကြီး)", RegexOptions.CultureInvariant), "တွေက", ProofingIssueType.OfficialStyleGrammarError, "ကတ္တားဝိဘတ် ('~တွေက')", "'အရေးကြီးသည်' ကြိယာ၏ ကတ္တားပုဒ်ဖြစ်၍ 'တွေက' ဟု သုံးနှုန်းရမည်။"),
            ("ခွင့်လွှတ်ပေးပါ", new Regex(@"ကျွန်တော့်အမှား(?<T>ကြောင့်)(?=\s+ခွင့်လွှတ်ပေးပါ)", RegexOptions.CultureInvariant), "အတွက်", ProofingIssueType.OfficialStyleGrammarError, "ရည်စူးချက်ပြဝိဘတ် ('~အတွက်')", "'ခွင့်လွှတ်ပေးပါ' နှင့် တွဲဖက်ရာတွင် 'အမှားအတွက်' ဟု သုံးနှုန်းရမည်။"),
            ("မနက်ဖြန် ထမယ်", new Regex(@"မနေ့ညက\s+[^\u104B]{4,20}?(?<T>မနက်ဖြန်\s+ထမယ်)", RegexOptions.CultureInvariant), "ဒီနေ့ ထခဲ့တယ်", ProofingIssueType.OfficialStyleGrammarError, "အတိတ်ကာလ ရှေ့နောက်ညီညွတ်မှု", "ရှေ့တွင် 'မနေ့ညက အိပ်ပျော်ပြီး' ဟု အတိတ်ကာလပြထားသဖြင့် 'ဒီနေ့ ထခဲ့တယ်' ဟု ကာလညီညွတ်ရမည်။"),
            ("တစ်ပတ်တစ်ခါလောက်", new Regex(@"တစ်ပတ်တစ်ခါလောက်\s+(?<T>စားနေတယ်)", RegexOptions.CultureInvariant), "စားတယ်", ProofingIssueType.OfficialStyleGrammarError, "အလေ့အထပြ ကြိယာ ('စားတယ်')", "'တစ်ပတ်တစ်ခါလောက်' သည် ပုံမှန်အလေ့အထပြ ဖြစ်သဖြင့် ဖြစ်ဆဲ 'နေ' မထည့်ဘဲ 'စားတယ်' ဟု သုံးရမည်။"),
            ("ဆွဲဆောင်နိုင်မည်။", new Regex(@"ဝန်ထမ်းတွေကို\s+(?<T>ဆွဲဆောင်နိုင်မည်။)", RegexOptions.CultureInvariant), "ဆွဲဆောင်နိုင်တယ်။", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ပစ္စုပ္ပန်/ယေဘုယျပြ ('နိုင်တယ်။')", "ရှေ့ပိုင်း အပြောစကား ('~နဲ့ / ~တွေက / ~တွေကို') နှင့် ညီညွတ်စေရန် 'ဆွဲဆောင်နိုင်တယ်။' ဟု သုံးနှုန်းရမည်။"),
            ("ဖြစ်ရမည်", new Regex(@"ဝေးကွာသွားတဲ့အခါ\s+စိတ်မကောင်း(?<T>ဖြစ်ရမည်)", RegexOptions.CultureInvariant), "ဖြစ်ရတယ်", ProofingIssueType.OfficialStyleGrammarError, "အပြောစကား ဝါကျအဆုံးသတ် ('ဖြစ်ရတယ်')", "ရှေ့ပိုင်း အပြောစကား ('~တွေနဲ့ / ~တဲ့အခါ') နှင့် ညီညွတ်စေရန် 'ဖြစ်ရတယ်' ဟု သုံးနှုန်းရမည်။"),
            ("လုပ်ငန်းစင်များ", new Regex(@"(?<T>လုပ်ငန်းစင်)(?=များ)", RegexOptions.CultureInvariant), "လုပ်ငန်းစဉ်", ProofingIssueType.SpellingError, "လုပ်ငန်းစဉ် (အစီအစဉ်)", "အစီအစဉ်အနက်ဖြင့် 'လုပ်ငန်းစဉ်' ဟု ညကလေးသတ်ဖြင့် ရေးသားရမည်။")
        };

        private static void ScanV66RegisterAndCaseHarmony(string text, ulong mmMask, List<ProofingIssue> issues)
        {
            const string auth = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် နှင့် စကားအဆင့်ညီညွတ်မှု စည်းမျဉ်း)";
            for (int i = 0; i < V66HarmonyRules.Length; i++)
            {
                var r = V66HarmonyRules[i];
                if (!ContainsMm(text, mmMask, r.Trigger))
                    continue;

                foreach (Match m in r.Pattern.Matches(text))
                {
                    Group g = m.Groups["T"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string rep = r.Replacement;
                    if (r.Trigger == "အရသာရှိ")
                    {
                        rep = g.Value == "ရှိမည်" ? "ရှိတယ်" : "ရှိတယ်။";
                    }

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, rep,
                        r.Type,
                        $"ဝါကျအတွင်း ဝိဘတ်/စကားအဆင့် ညီညွတ်စေရန် '{g.Value}' အစား '{rep}' ဟု သုံးနှုန်းရမည်။",
                        r.Cue, "PART", false,
                        r.Reason, auth);
                }
            }
        }

        private static readonly Regex HybridSovPostVerbalExtrapositionPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Verb>[\u1000-\u103F]{2,22}(?:ပါ)?(?:တယ်|မယ်))[ \t]+(?<Leaked>(?:[\u1000-\u103F]{1,18}(?:မှာ|တွင်|ကို|နဲ့)(?:[ \t]+(?:ခဏ|စောစော|အမြန်|ကောင်းကောင်း|သေချာ))?|[\u1000-\u103F]{2,20}(?:ရတာ|ရင်း|ပေမဲ့|ပေမယ့်|ပေမယ့်)))(?=\s*(?:။|ဆိုတာ(?:\s|$)|ဆိုပြီး(?:\s|$)|လို့(?:\s|$)|ဟု(?:\s|$)))");

        private static readonly Regex HybridPreVerbalSyntaxOrderPattern = new Regex(
            @"(?:(?<CompInv>ပိုပြီး[ \t]+(?<CompStd>[\u1000-\u103F]{2,22}?ထက်))(?=[ \t]+[\u1000-\u103F])|(?<QuantInv>အများကြီး[ \t]+(?<QuantNp>[\u1000-\u103F]{3,26}?(?:တွေ|များ)))(?=[ \t]+ရှိ(?:တယ်|သည်|ပါ|ပြီး|ကြ|နေ|လာ))|(?<![\u1000-\u103F])(?<PostpInv>(?<PostpStem>[\u1000-\u103F]{3,20})အကြောင်း[ \t]+(?<PostpHead>အန္တရာယ်|အကျိုးကျေးဇူး|ပြဿနာ))(?=[ \t]+)|(?<![\u1000-\u103F])(?<StackedPostp>(?<PossBase>[\u1000-\u103F]{2,18}(?:ရဲ့|၏))[ \t]+(?:နဲ့|နှင့်|နှင့်))(?=[ \t]+)|(?<=\s)(?<VerbKoPay>(?<VRoot>ဝယ်|ယူ|ချက်|ဖတ်|ရေး)ကို)(?=[ \t]+ပေး(?:တယ်|သည်|ပါ))|(?:^|[\u104A\u104B]\s*|(?:တွင်|မှာ|၌)\s+)(?<InanimDoubleKo>(?<InanimStem>[\u1000-\u103F]{4,28}?(?:နည်းပညာ|စနစ်|ပညာရေး|စီးပွားရေး|လုပ်ငန်း|စီမံကိန်း)(?:တွေ|များ)?)ကို)(?=[ \t]+[\u1000-\u103F]{2,22}?ကို[ \t]+(?:တိုးမြှင့်|မြှင့်တင်|လျှော့ချ|ပြောင်းလဲ|ဖန်တီး)ပေး))");

        private static readonly string[] ClauseLiteraryMarkers =
        {
            "နှင့်", "နှင့်", "၌", "၏", "သော်လည်း", "သဖြင့်", "သဖြင့်", "ဖြစ်သော", "ဟူသော"
        };

        private static readonly string[] ClauseColloquialMarkers =
        {
            "တွေ", "နဲ့", "မှာ", "တဲ့", "ဖို့", "လို့", "ရင်", "ပေမဲ့", "ပေမယ့်", "ပေမယ့်", "အဲဒီ", "အရမ်း"
        };

        private static void ScanHybridSovClauseAndRegisterConcord(
            string text,
            ulong mmMask,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie,
            List<ProofingIssue> issues)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 6)
                return;

            bool[]? quoteMask = MyanmarCompactLanguageModel.BuildQuotedDialogueMask(text);

            // 9A. Pre-verbal comparative/quantifier/postposition inversions & stacked/colliding case particles
            if ((text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0) &&
                (ContainsMm(text, mmMask, "ပိုပြီး ") || ContainsMm(text, mmMask, "ပိုပြီး\t") ||
                 ContainsMm(text, mmMask, "အများကြီး ") || ContainsMm(text, mmMask, "အများကြီး\t") ||
                 ContainsMm(text, mmMask, "အကြောင်း ") || ContainsMm(text, mmMask, "အကြောင်း\t") ||
                 ContainsMm(text, mmMask, "ကို ပေး") || ContainsMm(text, mmMask, "ကို\tပေး") ||
                 ((ContainsMm(text, mmMask, "ရဲ့ ") || ContainsMm(text, mmMask, "ရဲ့\t") || ContainsMm(text, mmMask, "၏ ") || ContainsMm(text, mmMask, "၏\t")) &&
                  (ContainsMm(text, mmMask, "နဲ့") || ContainsMm(text, mmMask, "နှင့်") || ContainsMm(text, mmMask, "နှင့်"))) ||
                 ((ContainsMm(text, mmMask, "ကို ") || ContainsMm(text, mmMask, "ကို\t")) && ContainsMm(text, mmMask, "ပေး"))))
            {
                foreach (Match mSyn in HybridPreVerbalSyntaxOrderPattern.Matches(text))
                {
                    if (mSyn.Groups["CompInv"].Success)
                    {
                        Group g = mSyn.Groups["CompInv"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string rep = mSyn.Groups["CompStd"].Value + " ပိုပြီး";
                            issues.Add(new ProofingIssue
                            {
                                StartIndex = g.Index,
                                Length = g.Length,
                                OriginalText = g.Value,
                                IssueType = ProofingIssueType.OfficialStyleGrammarError,
                                RequiresReviewBeforeBatchFix = true,
                                Message = $"နှိုင်းယှဉ်ပြဝါကျဖွဲ့ထုံးအရ '~ထက်' ပါပုဒ်စုသည် 'ပိုပြီး' ၏ ရှေ့၌ တည်ရှိရမည် — '{rep}' ဟု စီစဉ်သင့်ပါသည်။",
                                GrammaticalReasoning = "မြန်မာသဒ္ဒါ နှိုင်းယှဉ်ပြဖွဲ့ထုံးတွင် နှိုင်းယှဉ်ခံစံပုဒ် ('~ထက်') ကို ရှေ့မှထား၍ အထူးပြုပုဒ် ('ပိုပြီး/ပို၍') ကို နောက်မှ ကပ်လျက်လိုက်ရသည်။",
                                AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နှိုင်းယှဉ်ပြဝိဘတ် 'ထက်' နှင့် ပုဒ်အစီအစဉ်)",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = rep,
                                        MeaningCue = "နှိုင်းယှဉ်ပြ ပုဒ်အစီအစဉ်မှန် ('~ထက် ပိုပြီး')",
                                        PosTag = "SYNTAX",
                                        IsOfficialThatpon = true,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(rep),
                                        DiagnosticDetail = "နှိုင်းယှဉ်ပြပုဒ်အစီအစဉ် ပြင်ဆင်ချက်",
                                        Score = 96.0
                                    }
                                }
                            });
                        }
                    }
                    else if (mSyn.Groups["QuantInv"].Success)
                    {
                        Group g = mSyn.Groups["QuantInv"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string rep = mSyn.Groups["QuantNp"].Value + " အများကြီး";
                            issues.Add(new ProofingIssue
                            {
                                StartIndex = g.Index,
                                Length = g.Length,
                                OriginalText = g.Value,
                                IssueType = ProofingIssueType.OfficialStyleGrammarError,
                                RequiresReviewBeforeBatchFix = true,
                                Message = $"ပမာဏပြပုဒ် 'အများကြီး' ကို အများပြနာမ်ပုဒ်၏ နောက်၌ ထားသင့်ပါသည် — '{rep}' ဟု စီစဉ်သင့်ပါသည်။",
                                GrammaticalReasoning = "မြန်မာဝါကျဖွဲ့ထုံးတွင် အများပြနာမ်ပုဒ် ('~တွေ/~များ') ၏ နောက်မှ ပမာဏပြ 'အများကြီး' လိုက်ပြီးမှ တည်ရှိခြင်းပြကြိယာ 'ရှိသည်/ရှိတယ်' လိုက်ရသည်။",
                                AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်အထူးပြု ပမာဏပြပုဒ် အစီအစဉ်)",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = rep,
                                        MeaningCue = "နာမ်ပုဒ် + ပမာဏပြ အစီအစဉ်မှန်",
                                        PosTag = "SYNTAX",
                                        IsOfficialThatpon = false,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(rep),
                                        DiagnosticDetail = "ပမာဏပြပုဒ် အစီအစဉ် ပြင်ဆင်ချက်",
                                        Score = 96.0
                                    }
                                }
                            });
                        }
                    }
                    else if (mSyn.Groups["PostpInv"].Success)
                    {
                        Group g = mSyn.Groups["PostpInv"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string rep = mSyn.Groups["PostpStem"].Value + mSyn.Groups["PostpHead"].Value + "အကြောင်း";
                            issues.Add(new ProofingIssue
                            {
                                StartIndex = g.Index,
                                Length = g.Length,
                                OriginalText = g.Value,
                                IssueType = ProofingIssueType.OfficialStyleGrammarError,
                                RequiresReviewBeforeBatchFix = true,
                                Message = $"နောက်ဆက်ပုဒ် 'အကြောင်း' သည် အဓိကနာမ်ပုဒ်၏ နောက်ဆုံး၌ တည်ရှိရမည် — '{rep}' ဟု စီစဉ်သင့်ပါသည်။",
                                GrammaticalReasoning = "ပေါင်းစပ်နာမ်ပုဒ်ကို ရည်ညွှန်းရာတွင် အဓိကနာမ်ပုဒ်ပြီးမှသာ 'အကြောင်း' ကို နောက်ဆက်တွဲရသည်။",
                                AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ပေါင်းစပ်ပုံနှင့် ဝိဘတ်အစီအစဉ်)",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = rep,
                                        MeaningCue = "နာမ်ပေါင်းစပ်ပုဒ် အစီအစဉ်မှန်",
                                        PosTag = "SYNTAX",
                                        IsOfficialThatpon = true,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(rep),
                                        DiagnosticDetail = "'အကြောင်း' နောက်ဆက်အစီအစဉ် ပြင်ဆင်ချက်",
                                        Score = 96.0
                                    }
                                }
                            });
                        }
                    }
                    else if (mSyn.Groups["StackedPostp"].Success)
                    {
                        Group g = mSyn.Groups["StackedPostp"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string rep = mSyn.Groups["PossBase"].Value;
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, rep,
                                ProofingIssueType.OfficialStyleGrammarError,
                                $"ပိုင်ဆိုင်မှုပြဝိဘတ်နောက်၌ 'နဲ့/နှင့်' ထပ်ဆင့်ပိုနေပါသည် — '{rep}' ဟုသာ သုံးနှုန်းရမည်။",
                                "ပိုင်ဆိုင်မှုပြ နာမ်ဝိဘတ်", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                                "ပိုင်ဆိုင်မှုပြဝိဘတ် ('ရဲ့/၏') နှင့် ယှဉ်တွဲပြသမ္ဗန္ဓ ('နဲ့/နှင့်') တို့ကို ကြားခံနာမ်ပုဒ်မပါဘဲ ဆက်တိုက်တွဲစပ်ခွင့် မရှိပါ။",
                                "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် စည်းမျဉ်း)");
                        }
                    }
                    else if (mSyn.Groups["VerbKoPay"].Success)
                    {
                        Group g = mSyn.Groups["VerbKoPay"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string rep = mSyn.Groups["VRoot"].Value + "ပြီး";
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, rep,
                                ProofingIssueType.OfficialStyleGrammarError,
                                $"ကြိယာနောက်၌ ကံဝိဘတ် 'ကို' မလိုက်ရပါ — '{rep}' ဟု သုံးနှုန်းရမည်။",
                                "ရှေ့နောက်ကြိယာ ဆက်စပ်ပစ္စည်း ('~ပြီး')", "PART", false,
                                "ပင်မကြိယာအမြစ်နောက်တွင် နာမ်ဝိဘတ် 'ကို' မလိုက်နိုင်ဘဲ ကြိယာဆက်ပစ္စည်း 'ပြီး' ဖြင့် ဆက်စပ်ရသည်။",
                                "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာပစ္စည်းနှင့် နာမ်ဝိဘတ် ခွဲခြားပုံ)");
                        }
                    }
                    else if (mSyn.Groups["InanimDoubleKo"].Success)
                    {
                        Group g = mSyn.Groups["InanimDoubleKo"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string rep = mSyn.Groups["InanimStem"].Value + "က";
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, rep,
                                ProofingIssueType.OfficialStyleGrammarError,
                                $"ပြုလုပ်စေတတ်သော ရှေ့နာမ်ပုဒ်တွင် ကံဝိဘတ် 'ကို' နှစ်ထပ်ဖြစ်နေပါသည် — ကတ္တားဝိဘတ်ဖြင့် '{rep}' ဟု သုံးနှုန်းရမည်။",
                                "ကတ္တားဝိဘတ် ('~က')", "PART", true,
                                "နောက်၌ ကံပုဒ် ('~ကို') နှင့် အကျိုးပြုကြိယာ ပါရှိနေသဖြင့် အကြောင်းရင်းခံ ရှေ့နာမ်ပုဒ်တွင် ကတ္တားဝိဘတ် 'က' ကို သုံးရသည်။",
                                "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ကံဝိဘတ်)");
                        }
                    }
                }
            }

            // 9B. Finite-Verb-Final SOV Post-Verbal Extraposition Detector (Standard & Formal prose outside quotes)
            if (styleMode != ProofingStyleMode.ColloquialPermissive &&
                (ContainsMm(text, mmMask, "တယ်") || ContainsMm(text, mmMask, "မယ်")) &&
                HasPostVerbalLeakedCandidateAfterSfp(text))
            {
                foreach (Match mExtSov in HybridSovPostVerbalExtrapositionPattern.Matches(text))
                {
                    Group gVerb = mExtSov.Groups["Verb"];
                    Group gLeaked = mExtSov.Groups["Leaked"];
                    int spanStart = gVerb.Index;
                    int spanLen = (gLeaked.Index + gLeaked.Length) - spanStart;

                    if (quoteMask != null && spanStart < quoteMask.Length && quoteMask[spanStart])
                        continue;

                    if (OverlapsExistingIssue(issues, spanStart, spanLen))
                        continue;

                    string verbRaw = gVerb.Value.Trim();
                    string sfpLenCheck = verbRaw.EndsWith("ပါတယ်", StringComparison.Ordinal) || verbRaw.EndsWith("ပါမယ်", StringComparison.Ordinal)
                        ? verbRaw.Substring(0, verbRaw.Length - 5)
                        : verbRaw.Substring(0, verbRaw.Length - 3);
                    string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(sfpLenCheck);
                    if (normStem.Length < 2)
                        continue;

                    if (trie != null &&
                        !HasTrailingLexiconVerbOrAdj(trie, normStem, allowAdj: true) &&
                        !normStem.EndsWith("သွား", StringComparison.Ordinal) &&
                        !normStem.EndsWith("လာ", StringComparison.Ordinal) &&
                        !normStem.EndsWith("လို", StringComparison.Ordinal) &&
                        !normStem.EndsWith("ယူ", StringComparison.Ordinal) &&
                        !normStem.EndsWith("သာ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string leakedRaw = gLeaked.Value.Trim();
                    string rawSpan = text.Substring(spanStart, spanLen);
                    string restored = $"{leakedRaw} {verbRaw}";

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = spanStart,
                        Length = spanLen,
                        OriginalText = rawSpan,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        RequiresReviewBeforeBatchFix = true,
                        Message = $"မြန်မာစံဝါကျဖွဲ့ထုံး (SOV) အရ အဆုံးသတ်ကြိယာ '{verbRaw}' ကို ဝါကျအဆုံး၌ ထားရှိသင့်ပါသည် — '{restored}' ဟု ပြန်လည်စီစဉ်ရန် အကြံပြုပါသည်။",
                        GrammaticalReasoning = $"မြန်မာအရေးအသား စံဝါကျအစီအစဉ်တွင် ဝိဘတ်တွဲပုဒ်နှင့် လက်အောက်ခံဝါကျငယ် ('{leakedRaw}') သည် အဆုံးသတ်ကြိယာ ('{verbRaw}') ၏ ရှေ့တွင် တည်ရှိရသည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (SOV ဝါကျအဆုံးသတ်ကြိယာ စည်းမျဉ်း)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = restored,
                                MeaningCue = "မြန်မာစံဝါကျအစီအစဉ် (SOV)",
                                PosTag = "SYNTAX",
                                IsOfficialThatpon = false,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(restored),
                                DiagnosticDetail = "အဆုံးသတ်ကြိယာကို ဝါကျနောက်ဆုံးသို့ ပို့ခြင်း",
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            // 9C. Intra-Clause Majority-Register Concord in GeneralStandard Mode (outside quoted dialogue)
            //     Flags abrupt bare colloquial 'တယ်။ / ဖြစ်တယ်။' ONLY when the clause has an explicit literary plural/nominative subject ('များသည်'/'တို့သည်'),
            //     >= 2 strong literary markers, and ZERO colloquial markers.
            if (styleMode == ProofingStyleMode.GeneralStandard &&
                ContainsMm(text, mmMask, "တယ်") &&
                ContainsMm(text, mmMask, "သည်"))
            {
                int cStart = 0;
                for (int idx = 0; idx < text.Length; idx++)
                {
                    char ch = text[idx];
                    if (ch != '\u104A' && ch != '\u104B' && ch != '\n' && ch != '\r')
                        continue;

                    if (ch == '\u104B' && idx - cStart >= 12)
                    {
                        if (quoteMask == null || !quoteMask[idx - 1])
                        {
                            int trimEnd = idx;
                            while (trimEnd > cStart && (text[trimEnd - 1] == ' ' || text[trimEnd - 1] == '\t'))
                                trimEnd--;

                            if (trimEnd - cStart >= 12 &&
                                trimEnd >= 3 &&
                                string.CompareOrdinal(text, trimEnd - 3, "တယ်", 0, 3) == 0 &&
                                !(trimEnd >= 5 && string.CompareOrdinal(text, trimEnd - 5, "ပါတယ်", 0, 5) == 0))
                            {
                                string clauseSlice = text.Substring(cStart, trimEnd - cStart);
                                bool hasLiteraryPluralSubj =
                                    clauseSlice.IndexOf("များသည်", StringComparison.Ordinal) >= 0 ||
                                    clauseSlice.IndexOf("တို့သည်", StringComparison.Ordinal) >= 0;

                                if (hasLiteraryPluralSubj)
                                {
                                    bool hasAnyColloq = false;
                                    for (int cm = 0; cm < ClauseColloquialMarkers.Length; cm++)
                                    {
                                        string m = ClauseColloquialMarkers[cm];
                                        int searchAt = 0;
                                        while (searchAt < clauseSlice.Length)
                                        {
                                            int hit = clauseSlice.IndexOf(m, searchAt, StringComparison.Ordinal);
                                            if (hit < 0)
                                                break;
                                            int afterHit = hit + m.Length;
                                            // Do not confuse 'တွေ'/'ရင်' with tone-inflected 'တွေ့'/'တွေး'/'ရင်း'/'ရင့်' (e.g. 'စာရင်း', 'တွေ့ရှိ')
                                            if ((m == "တွေ" || m == "ရင်") &&
                                                afterHit < clauseSlice.Length &&
                                                (clauseSlice[afterHit] == '\u1037' || clauseSlice[afterHit] == '\u1038'))
                                            {
                                                searchAt = afterHit + 1;
                                                continue;
                                            }
                                            hasAnyColloq = true;
                                            break;
                                        }
                                        if (hasAnyColloq)
                                            break;
                                    }

                                    if (!hasAnyColloq)
                                    {
                                        int litCount = 1; // 'များသည်' / 'တို့သည်'
                                        for (int lm = 0; lm < ClauseLiteraryMarkers.Length; lm++)
                                        {
                                            if (clauseSlice.IndexOf(ClauseLiteraryMarkers[lm], StringComparison.Ordinal) >= 0)
                                                litCount++;
                                        }

                                        if (litCount >= 2)
                                        {
                                            bool isPhitTae = trimEnd >= 8 &&
                                                             text[trimEnd - 8] == ' ' &&
                                                             string.CompareOrdinal(text, trimEnd - 7, "ဖြစ်တယ်", 0, 7) == 0;
                                            int flagStart = isPhitTae ? trimEnd - 7 : trimEnd - 3;
                                            int flagLen = isPhitTae ? 7 : 3;
                                            string flagOrig = isPhitTae ? "ဖြစ်တယ်" : "တယ်";
                                            string flagRep = isPhitTae ? "ဖြစ်သည်" : "သည်";

                                            if (!OverlapsExistingIssue(issues, flagStart, flagLen))
                                            {
                                                AddGrammarOrSpellingIssue(
                                                    issues, flagStart, flagLen, flagOrig, flagRep,
                                                    ProofingIssueType.OfficialStyleGrammarError,
                                                    $"ဝါကျအတွင်း စာပေဟန်သဒ္ဒါပစ္စည်းများ ('များသည်/နှင့်/၏') နှင့် ညီညွတ်စေရန် အဆုံးသတ်၌ '{flagOrig}' အစား '{flagRep}' ဟု သုံးနှုန်းရမည်။",
                                                    "စာပေဟန် ကြိယာအဆုံးသတ်ပစ္စည်း ('~သည်')", "PART", true,
                                                    "ဝါကျတစ်ခုတည်းအတွင်း စာပေဟန် ကတ္တားနှင့် ဝိဘတ်များကို အသုံးပြုထားသဖြင့် အဆုံးသတ်ကြိယာဝိဘတ်ကိုလည်း စာပေဟန် 'သည်' ဖြင့် ညီညွတ်စွာ အဆုံးသတ်ရသည်။",
                                                    "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (စာပေဟန်နှင့် စကားပြောဟန် ညီညွတ်မှုစည်းမျဉ်း)");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    cStart = idx + 1;
                }
            }
        }

        private static bool HasPostVerbalLeakedCandidateAfterSfp(string text)
        {
            return HasLeakedAfterMarker(text, "တယ် ") ||
                   HasLeakedAfterMarker(text, "မယ် ") ||
                   HasLeakedAfterMarker(text, "တယ်\t") ||
                   HasLeakedAfterMarker(text, "မယ်\t");
        }

        private static bool HasLeakedAfterMarker(string text, string marker)
        {
            int pos = 0;
            while ((pos = text.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
            {
                int p = pos + marker.Length;
                while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
                    p++;
                if (p < text.Length && text[p] >= '\u1000' && text[p] <= '\u103F')
                {
                    if (!(p + 5 <= text.Length && string.CompareOrdinal(text, p, "ဆိုတာ", 0, 5) == 0) &&
                        !(p + 7 <= text.Length && string.CompareOrdinal(text, p, "ဆိုပြီး", 0, 7) == 0) &&
                        !(p + 4 <= text.Length && string.CompareOrdinal(text, p, "လို့", 0, 4) == 0) &&
                        !(p + 2 <= text.Length && string.CompareOrdinal(text, p, "ဟု", 0, 2) == 0))
                    {
                        return true;
                    }
                }
                pos += marker.Length;
            }
            return false;
        }

        private static bool HasBarePhoWithoutCreaky(string text)
        {
            int idx = 0;
            while ((idx = text.IndexOf("ဖို", idx, StringComparison.Ordinal)) >= 0)
            {
                int after = idx + 3;
                if (after >= text.Length || (text[after] != '\u1037' && text[after] != '\u1038'))
                    return true;
                idx = after;
            }
            return false;
        }

        private static bool HasTrailingLexiconVerbOrAdj(MyanmarTrie trie, string normStem, bool allowAdj)
        {
            if (string.IsNullOrEmpty(normStem))
                return false;
            if (allowAdj &&
                (normStem.EndsWith("ကောင်း", StringComparison.Ordinal) ||
                 normStem.EndsWith("နိုင်", StringComparison.Ordinal) ||
                 normStem.EndsWith("ဖြစ်", StringComparison.Ordinal) ||
                 normStem.EndsWith("ရှိ", StringComparison.Ordinal) ||
                 normStem.EndsWith("တတ်", StringComparison.Ordinal)))
            {
                return true;
            }
            if (trie.HasPosTag(normStem, "V") && (allowAdj || !trie.HasPosTag(normStem, "N")))
                return true;
            if (allowAdj && trie.HasPosTag(normStem, "ADJ"))
                return true;

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(normStem, isPreNormalized: true);
            int maxTail = Math.Min(4, syls.Count);
            int accumChars = 0;
            for (int t = 1; t <= maxTail; t++)
            {
                accumChars += syls[syls.Count - t].Length;
                if (accumChars <= normStem.Length)
                {
                    string tail = normStem.Substring(normStem.Length - accumChars);
                    if (tail.Length >= 2)
                    {
                        if (trie.HasPosTag(tail, "V") && (allowAdj || !trie.HasPosTag(tail, "N")))
                            return true;
                        if (allowAdj && trie.HasPosTag(tail, "ADJ"))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool IsReciprocalOrImmediateChinStem(string normStem)
        {
            return normStem.EndsWith("အချင်း", StringComparison.Ordinal) ||
                   normStem.EndsWith("တစ်ဦး", StringComparison.Ordinal) ||
                   normStem.EndsWith("တစ်ယောက်", StringComparison.Ordinal) ||
                   normStem.EndsWith("ချက်", StringComparison.Ordinal) ||
                   normStem.EndsWith("ရွယ်တူ", StringComparison.Ordinal) ||
                   normStem.EndsWith("မိတ်ဆွေ", StringComparison.Ordinal) ||
                   normStem.EndsWith("ညီအစ်ကို", StringComparison.Ordinal) ||
                   normStem.EndsWith("သူငယ်", StringComparison.Ordinal) ||
                   normStem.EndsWith("သီ", StringComparison.Ordinal) ||
                   normStem.IndexOf("လျှင်", StringComparison.Ordinal) >= 0 ||
                   normStem.IndexOf("ရင်", StringComparison.Ordinal) >= 0;
        }
    }
}
