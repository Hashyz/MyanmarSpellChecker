// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Spreadsheet;

namespace MyanmarSpellChecker.Core.Official
{
    /// <summary>
    /// Cross-paragraph (Word) and cross-cell streaming (Excel) document consistency &amp; structural reconciliation engine:
    /// 1. Dual-valid orthographic &amp; register variant consistency (e.g. ဒစ်ဂျစ်တယ် vs ဒီဂျစ်တယ်, ဗီဒီယို vs ဗွီဒီယို, ကော်မတီ vs ကော်မီတီ, သို့မဟုတ် vs ဒါမှမဟုတ်).
    /// 2. Cross-paragraph / cross-cell numeral script consistency in GeneralStandard mode (shields English-linked commercial tokens).
    /// 3. Multi-paragraph speech quotation ('“ ... ”') and bracket reconciliation across adjacent Word paragraphs.
    /// All consistency advisories emit <see cref="ProofingIssueType.ContextualMeaningWarning"/> with <c>RequiresReviewBeforeBatchFix = true</c>
    /// so they stay unchecked (☐) by default in Review &amp; Fix while offering 1-click harmonization.
    /// </summary>
    public static class DocumentConsistencyAnalyzer
    {
        public const int MaxStoredHitsPerBucket = 48;

        internal sealed class VariantPairSpec
        {
            public readonly string FormA;
            public readonly string FormB;
            public readonly string CategoryMm;
            public readonly string CategoryEn;
            public readonly bool RequireStrictLeftBoundary;
            public readonly string? ForbiddenRightPrefixOnA;
            public readonly string? ForbiddenRightPrefixOnB;

            public VariantPairSpec(
                string formA,
                string formB,
                string categoryMm,
                string categoryEn,
                bool requireStrictLeftBoundary = false,
                string? forbiddenRightPrefixOnA = null,
                string? forbiddenRightPrefixOnB = null)
            {
                FormA = formA;
                FormB = formB;
                CategoryMm = categoryMm;
                CategoryEn = categoryEn;
                RequireStrictLeftBoundary = requireStrictLeftBoundary;
                ForbiddenRightPrefixOnA = forbiddenRightPrefixOnA;
                ForbiddenRightPrefixOnB = forbiddenRightPrefixOnB;
            }
        }

        internal static readonly VariantPairSpec[] VariantPairs =
        {
            new VariantPairSpec("ဒစ်ဂျစ်တယ်", "ဒီဂျစ်တယ်", "မွေးစားစာလုံးပေါင်း (Digital)", "Loanword spelling (Digital)"),
            new VariantPairSpec("ဗီဒီယို", "ဗွီဒီယို", "မွေးစားစာလုံးပေါင်း (Video)", "Loanword spelling (Video)"),
            new VariantPairSpec("တီဗီ", "တီဗွီ", "မွေးစားစာလုံးပေါင်း (TV)", "Loanword spelling (TV)", requireStrictLeftBoundary: true),
            new VariantPairSpec("ကော်မတီ", "ကော်မီတီ", "အဖွဲ့အစည်းစာလုံးပေါင်း (Committee)", "Loanword spelling (Committee)"),
            new VariantPairSpec("မိုဘိုင်းလ်", "မိုဘိုင်း", "မွေးစားစာလုံးပေါင်း (Mobile)", "Loanword spelling (Mobile)", forbiddenRightPrefixOnB: "လ်"),
            new VariantPairSpec("ဘတ်စ်ကား", "ဘတ်စကား", "မွေးစားစာလုံးပေါင်း (Bus)", "Loanword spelling (Bus)"),
            new VariantPairSpec("ပတ်စ်ပို့", "ပတ်စပို့", "မွေးစားစာလုံးပေါင်း (Passport)", "Loanword spelling (Passport)"),
            new VariantPairSpec("စတိုင်လ်", "စတိုင်", "မွေးစားစာလုံးပေါင်း (Style)", "Loanword spelling (Style)", requireStrictLeftBoundary: true, forbiddenRightPrefixOnB: "လ်"),
            new VariantPairSpec("နံနက်ပိုင်း", "မနက်ပိုင်း", "အချိန်ပြအသုံးအနှုန်း (Morning)", "Time expression (Morning)", requireStrictLeftBoundary: true),
            new VariantPairSpec("နံနက်ခင်း", "မနက်ခင်း", "အချိန်ပြအသုံးအနှုန်း (Morning)", "Time expression (Morning)", requireStrictLeftBoundary: true),
            new VariantPairSpec("နံနက်စာ", "မနက်စာ", "အချိန်ပြအသုံးအနှုန်း (Breakfast)", "Time expression (Breakfast)", requireStrictLeftBoundary: true),
            new VariantPairSpec("သို့မဟုတ်", "ဒါမှမဟုတ်", "သမ္ဗန္ဓပစ္စည်း ('သို့မဟုတ် / ဒါမှမဟုတ်')", "Conjunction consistency ('or')", requireStrictLeftBoundary: true)
        };

        private static readonly Regex PureBurmeseNumeralPhraseRegex = new Regex(
            @"(?<![A-Za-z0-9\u1040-\u1049_./:@#$\-+%])(?<Num>(?:[0-9]{1,4}(?:,[0-9]{3})*|[\u1040-\u1049]{1,4}(?:,[\u1040-\u1049]{3})*))(?<Gap>\)?[ \t]*)(?<Unit>ခုနှစ်|ပြည့်နှစ်|ရက်နေ့|ရာခိုင်နှုန်း|နာရီ|မိနစ်|ယောက်|ကြိမ်|သောင်း|သိန်း|သန်း|ကျပ်|ဦး|ပါး|ခု|စီး|လုံး|ချပ်|စောင်|ထည်|ဖွဲ့|မျိုး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|[A-Za-z0-9\u1040-\u1049])",
            RegexOptions.Compiled);

        internal readonly struct ParagraphHit
        {
            public readonly int ParagraphIndex;
            public readonly int StartIndex;
            public readonly int Length;
            public readonly string RawText;
            public readonly string UnitLabel;

            public ParagraphHit(int paragraphIndex, int startIndex, int length, string rawText, string unitLabel = "")
            {
                ParagraphIndex = paragraphIndex;
                StartIndex = startIndex;
                Length = length;
                RawText = rawText;
                UnitLabel = unitLabel;
            }
        }

        /// <summary>
        /// Reconciles multi-paragraph speeches/brackets and injects cross-paragraph consistency warnings into Word scan results.
        /// Returns concise human-readable consistency summary lines for <see cref="DocumentQualityReport"/>.
        /// </summary>
        public static List<string> ReconcileAndAnalyzeWordParagraphs(
            IReadOnlyList<string> paragraphTexts,
            IList<List<ProofingIssue>> issuesPerParagraph,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie = null,
            int maxConsistencyIssuesToAdd = 64)
        {
            var summaries = new List<string>();
            if (paragraphTexts == null || issuesPerParagraph == null || paragraphTexts.Count == 0 || paragraphTexts.Count != issuesPerParagraph.Count)
                return summaries;

            ReconcileMultiParagraphQuotesAndBrackets(paragraphTexts, issuesPerParagraph);

            int count = paragraphTexts.Count;
            int pairCount = VariantPairs.Length;
            int[] countA = new int[pairCount];
            int[] countB = new int[pairCount];
            var hitsA = new List<ParagraphHit>?[pairCount];
            var hitsB = new List<ParagraphHit>?[pairCount];

            int mmNumCount = 0;
            int asciiNumCount = 0;
            List<ParagraphHit>? mmNumHits = null;
            List<ParagraphHit>? asciiNumHits = null;

            for (int p = 0; p < count; p++)
            {
                string text = paragraphTexts[p];
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                ulong mmMask = OfficialStyleGrammarEngine.ComputeMyanmarCharMask(text);
                if (mmMask == 0UL)
                    continue;

                List<ProofingIssue> existing = issuesPerParagraph[p];
                List<(int Start, int End)>? quoteSpans = null;
                bool quoteSpansComputed = false;

                for (int v = 0; v < pairCount; v++)
                {
                    VariantPairSpec spec = VariantPairs[v];
                    if (ContainsWithMask(text, mmMask, spec.FormA))
                    {
                        if (!quoteSpansComputed) { quoteSpans = ComputeSimpleQuoteRanges(text); quoteSpansComputed = true; }
                        ScanVariantInText(
                            text, spec.FormA, spec.RequireStrictLeftBoundary, spec.ForbiddenRightPrefixOnA,
                            existing, quoteSpans,
                            (idx, len) =>
                            {
                                countA[v]++;
                                var list = hitsA[v] ??= new List<ParagraphHit>(4);
                                if (list.Count < MaxStoredHitsPerBucket)
                                    list.Add(new ParagraphHit(p, idx, len, spec.FormA));
                            });
                    }

                    if (ContainsWithMask(text, mmMask, spec.FormB))
                    {
                        if (!quoteSpansComputed) { quoteSpans = ComputeSimpleQuoteRanges(text); quoteSpansComputed = true; }
                        ScanVariantInText(
                            text, spec.FormB, spec.RequireStrictLeftBoundary, spec.ForbiddenRightPrefixOnB,
                            existing, quoteSpans,
                            (idx, len) =>
                            {
                                countB[v]++;
                                var list = hitsB[v] ??= new List<ParagraphHit>(4);
                                if (list.Count < MaxStoredHitsPerBucket)
                                    list.Add(new ParagraphHit(p, idx, len, spec.FormB));
                            });
                    }
                }

                if (styleMode == ProofingStyleMode.GeneralStandard &&
                    HasAnyDigit(text) &&
                    (trie == null || !OfficialStyleGrammarEngine.IsCleanCanonicalStandaloneNrc(text, trie)))
                {
                    if (!quoteSpansComputed) { quoteSpans = ComputeSimpleQuoteRanges(text); quoteSpansComputed = true; }
                    ScanNumeralConsistencyInText(
                        text, existing, quoteSpans,
                        (idx, len, rawNum, unit, isMyanmarDigits) =>
                        {
                            if (isMyanmarDigits)
                            {
                                mmNumCount++;
                                mmNumHits ??= new List<ParagraphHit>(8);
                                if (mmNumHits.Count < MaxStoredHitsPerBucket)
                                    mmNumHits.Add(new ParagraphHit(p, idx, len, rawNum, unit));
                            }
                            else
                            {
                                asciiNumCount++;
                                asciiNumHits ??= new List<ParagraphHit>(8);
                                if (asciiNumHits.Count < MaxStoredHitsPerBucket)
                                    asciiNumHits.Add(new ParagraphHit(p, idx, len, rawNum, unit));
                            }
                        });
                }
            }

            int addedTotal = 0;
            var touchedParagraphs = new HashSet<int>();

            for (int v = 0; v < pairCount && addedTotal < maxConsistencyIssuesToAdd; v++)
            {
                if (countA[v] <= 0 || countB[v] <= 0)
                    continue;

                VariantPairSpec spec = VariantPairs[v];
                bool preferA = countA[v] >= countB[v];
                string dominant = preferA ? spec.FormA : spec.FormB;
                string minority = preferA ? spec.FormB : spec.FormA;
                int domCount = preferA ? countA[v] : countB[v];
                int minCount = preferA ? countB[v] : countA[v];
                List<ParagraphHit>? targetHits = preferA ? hitsB[v] : hitsA[v];

                summaries.Add($"'{dominant}' ({domCount} ကြိမ်) ⇄ '{minority}' ({minCount} ကြိမ်) — {spec.CategoryMm}");

                if (targetHits == null)
                    continue;

                for (int h = 0; h < targetHits.Count && addedTotal < maxConsistencyIssuesToAdd; h++)
                {
                    ParagraphHit hit = targetHits[h];
                    List<ProofingIssue> paraIssues = issuesPerParagraph[hit.ParagraphIndex];
                    if (OverlapsAny(paraIssues, hit.StartIndex, hit.Length))
                        continue;

                    paraIssues.Add(BuildVariantConsistencyIssue(spec, hit.StartIndex, hit.Length, minority, dominant, domCount, minCount));
                    touchedParagraphs.Add(hit.ParagraphIndex);
                    addedTotal++;
                }
            }

            // Cross-paragraph numeral script consistency in GeneralStandard mode (requires >= 3 pure-Burmese numbers & dominant >= 2)
            if (styleMode == ProofingStyleMode.GeneralStandard &&
                mmNumCount > 0 && asciiNumCount > 0 &&
                (mmNumCount + asciiNumCount) >= 3 &&
                Math.Max(mmNumCount, asciiNumCount) >= 2 &&
                addedTotal < maxConsistencyIssuesToAdd)
            {
                bool preferMmDigits = mmNumCount >= asciiNumCount;
                int domCount = preferMmDigits ? mmNumCount : asciiNumCount;
                int minCount = preferMmDigits ? asciiNumCount : mmNumCount;
                List<ParagraphHit>? targetHits = preferMmDigits ? asciiNumHits : mmNumHits;

                string domLabel = preferMmDigits ? "မြန်မာဂဏန်း (၀-၉)" : "အင်္ဂလိပ်ဂဏန်း (0-9)";
                string minLabel = preferMmDigits ? "အင်္ဂလိပ်ဂဏန်း (0-9)" : "မြန်မာဂဏန်း (၀-၉)";
                summaries.Add($"{domLabel} ({domCount} ကြိမ်) ⇄ {minLabel} ({minCount} ကြိမ်) — ကိန်းဂဏန်းရေးထုံး ညီညွတ်မှု");

                if (targetHits != null)
                {
                    for (int h = 0; h < targetHits.Count && addedTotal < maxConsistencyIssuesToAdd; h++)
                    {
                        ParagraphHit hit = targetHits[h];
                        List<ProofingIssue> paraIssues = issuesPerParagraph[hit.ParagraphIndex];
                        if (OverlapsAny(paraIssues, hit.StartIndex, hit.Length))
                            continue;

                        string converted = preferMmDigits
                            ? OfficialStyleGrammarEngine.ConvertAsciiDigitsToMyanmar(hit.RawText)
                            : ConvertMyanmarDigitsToAscii(hit.RawText);

                        paraIssues.Add(BuildNumeralConsistencyIssue(
                            hit.StartIndex, hit.Length, hit.RawText, converted, hit.UnitLabel, preferMmDigits, domCount, minCount));
                        touchedParagraphs.Add(hit.ParagraphIndex);
                        addedTotal++;
                    }
                }
            }

            foreach (int pIdx in touchedParagraphs)
            {
                issuesPerParagraph[pIdx].Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
            }

            return summaries;
        }

        /// <summary>
        /// Standalone plain-text document consistency check used by <see cref="DocumentQualityAnalyzer.Analyze"/> when callers pass raw multi-line text.
        /// </summary>
        public static List<ProofingIssue> AnalyzePlainDocumentConsistency(
            string? documentText,
            IReadOnlyList<ProofingIssue>? existingIssues,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie,
            out List<string> consistencySummaries,
            int maxIssuesToEmit = 32)
        {
            consistencySummaries = new List<string>();
            var emitted = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(documentText))
                return emitted;

            string text = documentText!;
            ulong mmMask = OfficialStyleGrammarEngine.ComputeMyanmarCharMask(text);
            if (mmMask == 0UL)
                return emitted;

            var existingList = existingIssues as List<ProofingIssue> ?? (existingIssues != null ? new List<ProofingIssue>(existingIssues) : new List<ProofingIssue>(0));
            List<(int Start, int End)>? quoteSpans = ComputeSimpleQuoteRanges(text);

            int pairCount = VariantPairs.Length;
            for (int v = 0; v < pairCount && emitted.Count < maxIssuesToEmit; v++)
            {
                VariantPairSpec spec = VariantPairs[v];
                if (!ContainsWithMask(text, mmMask, spec.FormA) || !ContainsWithMask(text, mmMask, spec.FormB))
                    continue;

                var listA = new List<(int Idx, int Len)>(4);
                var listB = new List<(int Idx, int Len)>(4);
                ScanVariantInText(text, spec.FormA, spec.RequireStrictLeftBoundary, spec.ForbiddenRightPrefixOnA, existingList, quoteSpans, (idx, len) => listA.Add((idx, len)));
                ScanVariantInText(text, spec.FormB, spec.RequireStrictLeftBoundary, spec.ForbiddenRightPrefixOnB, existingList, quoteSpans, (idx, len) => listB.Add((idx, len)));

                if (listA.Count <= 0 || listB.Count <= 0)
                    continue;

                bool preferA = listA.Count >= listB.Count;
                string dominant = preferA ? spec.FormA : spec.FormB;
                string minority = preferA ? spec.FormB : spec.FormA;
                int domCount = preferA ? listA.Count : listB.Count;
                int minCount = preferA ? listB.Count : listA.Count;
                var targets = preferA ? listB : listA;

                consistencySummaries.Add($"'{dominant}' ({domCount} ကြိမ်) ⇄ '{minority}' ({minCount} ကြိမ်) — {spec.CategoryMm}");
                for (int i = 0; i < targets.Count && emitted.Count < maxIssuesToEmit; i++)
                {
                    emitted.Add(BuildVariantConsistencyIssue(spec, targets[i].Idx, targets[i].Len, minority, dominant, domCount, minCount));
                }
            }

            if (styleMode == ProofingStyleMode.GeneralStandard && HasAnyDigit(text) && emitted.Count < maxIssuesToEmit)
            {
                var mmHits = new List<(int Idx, int Len, string Raw, string Unit)>(8);
                var ascHits = new List<(int Idx, int Len, string Raw, string Unit)>(8);
                ScanNumeralConsistencyInText(
                    text, existingList, quoteSpans,
                    (idx, len, raw, unit, isMm) =>
                    {
                        if (isMm) mmHits.Add((idx, len, raw, unit));
                        else ascHits.Add((idx, len, raw, unit));
                    });

                if (mmHits.Count > 0 && ascHits.Count > 0 &&
                    (mmHits.Count + ascHits.Count) >= 3 &&
                    Math.Max(mmHits.Count, ascHits.Count) >= 2)
                {
                    bool preferMm = mmHits.Count >= ascHits.Count;
                    int domCount = preferMm ? mmHits.Count : ascHits.Count;
                    int minCount = preferMm ? ascHits.Count : mmHits.Count;
                    var targets = preferMm ? ascHits : mmHits;

                    string domLabel = preferMm ? "မြန်မာဂဏန်း (၀-၉)" : "အင်္ဂလိပ်ဂဏန်း (0-9)";
                    string minLabel = preferMm ? "အင်္ဂလိပ်ဂဏန်း (0-9)" : "မြန်မာဂဏန်း (၀-၉)";
                    consistencySummaries.Add($"{domLabel} ({domCount} ကြိမ်) ⇄ {minLabel} ({minCount} ကြိမ်) — ကိန်းဂဏန်းရေးထုံး ညီညွတ်မှု");

                    for (int i = 0; i < targets.Count && emitted.Count < maxIssuesToEmit; i++)
                    {
                        var t = targets[i];
                        string conv = preferMm
                            ? OfficialStyleGrammarEngine.ConvertAsciiDigitsToMyanmar(t.Raw)
                            : ConvertMyanmarDigitsToAscii(t.Raw);
                        emitted.Add(BuildNumeralConsistencyIssue(t.Idx, t.Len, t.Raw, conv, t.Unit, preferMm, domCount, minCount));
                    }
                }
            }

            return emitted;
        }

        private static void ReconcileMultiParagraphQuotesAndBrackets(
            IReadOnlyList<string> paragraphTexts,
            IList<List<ProofingIssue>> issuesPerParagraph)
        {
            int count = paragraphTexts.Count;
            if (count < 2)
                return;

            // 1. Reconcile multi-paragraph curly quotes (“ in paragraph p .. q-1 closed by ” in paragraph q within 6 paragraphs)
            for (int p = 0; p < count - 1; p++)
            {
                int openIssueIdx = FindUnclosedSymbolIssueIndex(issuesPerParagraph[p], '\u201C');
                if (openIssueIdx < 0)
                    continue;

                int maxLookahead = Math.Min(count - 1, p + 6);
                int matchedClosePara = -1;
                int matchedCloseIssueIdx = -1;

                for (int q = p + 1; q <= maxLookahead; q++)
                {
                    if (string.IsNullOrWhiteSpace(paragraphTexts[q]))
                        continue;

                    int closeIdx = FindUnmatchedCloseSymbolIssueIndex(issuesPerParagraph[q], '\u201D');
                    if (closeIdx >= 0)
                    {
                        matchedClosePara = q;
                        matchedCloseIssueIdx = closeIdx;
                        break;
                    }
                }

                if (matchedClosePara > p && matchedCloseIssueIdx >= 0)
                {
                    issuesPerParagraph[matchedClosePara].RemoveAt(matchedCloseIssueIdx);
                    for (int k = p; k < matchedClosePara; k++)
                    {
                        int kOpen = FindUnclosedSymbolIssueIndex(issuesPerParagraph[k], '\u201C');
                        if (kOpen >= 0)
                            issuesPerParagraph[k].RemoveAt(kOpen);
                    }
                }
            }

            // 2. Reconcile multi-paragraph parentheses/brackets ('(' -> ')', '[' -> ']') across up to 3 paragraphs
            ReconcileBracketPairAcrossParagraphs(paragraphTexts, issuesPerParagraph, '(', ')');
            ReconcileBracketPairAcrossParagraphs(paragraphTexts, issuesPerParagraph, '[', ']');
        }

        private static void ReconcileBracketPairAcrossParagraphs(
            IReadOnlyList<string> paragraphTexts,
            IList<List<ProofingIssue>> issuesPerParagraph,
            char openCh,
            char closeCh)
        {
            int count = paragraphTexts.Count;
            for (int p = 0; p < count - 1; p++)
            {
                int openIssueIdx = FindUnclosedSymbolIssueIndex(issuesPerParagraph[p], openCh);
                if (openIssueIdx < 0)
                    continue;

                int maxLook = Math.Min(count - 1, p + 3);
                for (int q = p + 1; q <= maxLook; q++)
                {
                    if (string.IsNullOrWhiteSpace(paragraphTexts[q]))
                        continue;

                    if (FindUnclosedSymbolIssueIndex(issuesPerParagraph[q], openCh) >= 0)
                        break;

                    int closeIssueIdx = FindUnmatchedCloseSymbolIssueIndex(issuesPerParagraph[q], closeCh);
                    if (closeIssueIdx >= 0)
                    {
                        issuesPerParagraph[q].RemoveAt(closeIssueIdx);
                        issuesPerParagraph[p].RemoveAt(openIssueIdx);
                        break;
                    }
                }
            }
        }

        private static int FindUnclosedSymbolIssueIndex(List<ProofingIssue>? issues, char openSymbol)
        {
            if (issues == null || issues.Count == 0)
                return -1;

            string token = $"'{openSymbol}'";
            for (int i = 0; i < issues.Count; i++)
            {
                ProofingIssue iss = issues[i];
                if (iss.IssueType == ProofingIssueType.PunctuationError &&
                    iss.RequiresReviewBeforeBatchFix &&
                    iss.Message != null &&
                    iss.Message.IndexOf(token, StringComparison.Ordinal) >= 0 &&
                    (iss.Message.IndexOf("ပြန်မပိတ်ရသေးပါ", StringComparison.Ordinal) >= 0 ||
                     iss.Message.IndexOf("မတွေ့ရှိရပါ", StringComparison.Ordinal) >= 0))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindUnmatchedCloseSymbolIssueIndex(List<ProofingIssue>? issues, char closeSymbol)
        {
            if (issues == null || issues.Count == 0)
                return -1;

            string token = $"'{closeSymbol}' တစ်ခုတည်း";
            for (int i = 0; i < issues.Count; i++)
            {
                ProofingIssue iss = issues[i];
                if (iss.IssueType == ProofingIssueType.PunctuationError &&
                    iss.RequiresReviewBeforeBatchFix &&
                    iss.Message != null &&
                    iss.Message.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    return i;
                }
            }
            return -1;
        }

        internal static void ScanVariantInText(
            string text,
            string form,
            bool requireStrictLeftBoundary,
            string? forbiddenRightPrefix,
            IReadOnlyList<ProofingIssue>? existingIssues,
            List<(int Start, int End)>? quoteSpans,
            Action<int, int> onMatch)
        {
            int len = text.Length;
            int fLen = form.Length;
            int pos = 0;
            while (pos < len)
            {
                int hit = text.IndexOf(form, pos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                pos = hit + fLen;

                // Left boundary check
                if (hit > 0)
                {
                    char prev = text[hit - 1];
                    if (requireStrictLeftBoundary)
                    {
                        if (prev >= '\u1000' && prev <= '\u103F')
                            continue;
                    }
                    else
                    {
                        if (prev == '\u1039')
                            continue;
                    }
                }

                // Right boundary check: never split a combining vowel/medial/tone or killed consonant coda
                int after = hit + fLen;
                if (after < len)
                {
                    char next = text[after];
                    if (next >= '\u102B' && next <= '\u103E')
                        continue;
                    if (next >= '\u1000' && next <= '\u1021' && after + 1 < len &&
                        (text[after + 1] == '\u103A' || text[after + 1] == '\u1039'))
                    {
                        // Allow only if 'next + \u103A' is NOT a coda extending the last open syllable (unless next is standard postposition 'သည်', 'နှင့်', 'တွင်', 'မှ', 'ဖြင့်')
                        if (!StartsWithCommonPostposition(text, after))
                        {
                            // Check if last char of form was a bare consonant (in which case next+\u103A IS a coda of that syllable!)
                            char formLast = form[fLen - 1];
                            if (formLast >= '\u1000' && formLast <= '\u1021')
                                continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(forbiddenRightPrefix) &&
                        after + forbiddenRightPrefix!.Length <= len &&
                        string.CompareOrdinal(text, after, forbiddenRightPrefix, 0, forbiddenRightPrefix.Length) == 0)
                    {
                        continue;
                    }

                    // Special lexical guard: 'စတိုင်ပင်' ("stipend") is a distinct word from 'စတိုင်' ("style")
                    if (form == "စတိုင်" && after + 3 <= len && string.CompareOrdinal(text, after, "ပင်", 0, 3) == 0)
                    {
                        continue;
                    }
                }

                if (IsInsideQuotes(quoteSpans, hit, fLen) || OverlapsAny(existingIssues, hit, fLen))
                    continue;

                onMatch(hit, fLen);
            }
        }

        private static bool StartsWithCommonPostposition(string text, int idx)
        {
            return MatchesAt(text, idx, "သည်") ||
                   MatchesAt(text, idx, "သည့်") ||
                   MatchesAt(text, idx, "နှင့်") ||
                   MatchesAt(text, idx, "တွင်") ||
                   MatchesAt(text, idx, "ဖြင့်") ||
                   MatchesAt(text, idx, "များ") ||
                   MatchesAt(text, idx, "တို့");
        }

        private static bool MatchesAt(string text, int idx, string lit)
        {
            return idx + lit.Length <= text.Length && string.CompareOrdinal(text, idx, lit, 0, lit.Length) == 0;
        }

        internal static void ScanNumeralConsistencyInText(
            string text,
            IReadOnlyList<ProofingIssue>? existingIssues,
            List<(int Start, int End)>? quoteSpans,
            Action<int, int, string, string, bool> onNumeralMatch)
        {
            foreach (Match m in PureBurmeseNumeralPhraseRegex.Matches(text))
            {
                Group numGroup = m.Groups["Num"];
                int idx = numGroup.Index;
                int len = numGroup.Length;
                if (IsInsideQuotes(quoteSpans, idx, len) || OverlapsAny(existingIssues, idx, len))
                    continue;

                if (OfficialStyleGrammarEngine.IsPrecededByEnglishOrTechnicalToken(text, idx))
                    continue;

                string raw = numGroup.Value;
                char first = raw[0];
                bool isMm = first >= '\u1040' && first <= '\u1049';
                string unit = m.Groups["Unit"].Value;
                onNumeralMatch(idx, len, raw, unit, isMm);
            }
        }

        internal static ProofingIssue BuildVariantConsistencyIssue(
            VariantPairSpec spec,
            int startIndex,
            int length,
            string minorityForm,
            string dominantForm,
            int dominantCount,
            int minorityCount)
        {
            string msg = UiLocalizer.IsEnglish
                ? $"Document consistency ({spec.CategoryEn}): '{dominantForm}' is used {dominantCount}x while '{minorityForm}' is used {minorityCount}x — harmonize to '{dominantForm}' for consistency."
                : $"စာတမ်းတစ်ခုလုံး အသုံးအနှုန်း ညီညွတ်မှု ({spec.CategoryMm}) — ဤစာတမ်း၌ '{dominantForm}' ကို ({dominantCount}) ကြိမ် သုံးထားပြီး '{minorityForm}' ကို ({minorityCount}) ကြိမ် ရောနှောသုံးထားသဖြင့် '{dominantForm}' ဖြင့် တစ်ပြေးညီ ညှိနှိုင်းသင့်ပါသည်။";

            return new ProofingIssue
            {
                StartIndex = startIndex,
                Length = length,
                OriginalText = minorityForm,
                IssueType = ProofingIssueType.ContextualMeaningWarning,
                RequiresReviewBeforeBatchFix = true,
                Message = msg,
                GrammaticalReasoning = $"'{dominantForm}' နှင့် '{minorityForm}' နှစ်မျိုးစလုံးမှာ အသုံးရှိသော်လည်း စာတမ်း/စာရင်းဇယား တစ်ခုတည်းအတွင်း တစ်မျိုးတည်းကိုသာ တစ်ပြေးညီ (Consistent) သုံးနှုန်းသင့်ပါသည်။",
                AuthoritativeSource = "စာတမ်းတည်းဖြတ်ရေးနှင့် ရုံးသုံးမြန်မာစာ ညီညွတ်မှုစံနှုန်း",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate
                    {
                        Replacement = dominantForm,
                        MeaningCue = $"စာတမ်းအတွင်း အများစုသုံးပုံစံ ('{dominantForm}' — {dominantCount} ကြိမ်)",
                        PosTag = "CONSISTENCY",
                        IsOfficialThatpon = true,
                        Score = 98.0
                    }
                }
            };
        }

        internal static ProofingIssue BuildNumeralConsistencyIssue(
            int startIndex,
            int length,
            string rawNum,
            string convertedNum,
            string unitLabel,
            bool preferMmDigits,
            int dominantCount,
            int minorityCount)
        {
            string targetScriptName = preferMmDigits ? "မြန်မာဂဏန်း" : "အင်္ဂလိပ်ဂဏန်း";
            string msg = UiLocalizer.IsEnglish
                ? $"Numeral consistency: Pure-Burmese numbers in this document predominantly use {(preferMmDigits ? "Myanmar digits" : "ASCII digits")} ({dominantCount}x vs {minorityCount}x) — consider '{convertedNum}' before '{unitLabel}'."
                : $"ကိန်းဂဏန်းရေးထုံး ညီညွတ်မှု — ဤစာတမ်းရှိ မြန်မာမျိုးပြပုဒ်များရှေ့တွင် {targetScriptName}ကို အဓိက ({dominantCount} ကြိမ်) သုံးထားသဖြင့် '{rawNum} {unitLabel}' ကိုလည်း '{convertedNum}' ဖြင့် တစ်ပြေးညီ ရေးသားသင့်ပါသည်။";

            return new ProofingIssue
            {
                StartIndex = startIndex,
                Length = length,
                OriginalText = rawNum,
                IssueType = ProofingIssueType.ContextualMeaningWarning,
                RequiresReviewBeforeBatchFix = true,
                Message = msg,
                GrammaticalReasoning = $"စာတမ်းတစ်ခုတည်းအတွင်း မြန်မာမျိုးပြပစ္စည်း ('{unitLabel}') နှင့်တွဲသော ကိန်းဂဏန်းများကို ဂဏန်းစနစ် တစ်မျိုးတည်းဖြင့် တစ်ပြေးညီ ရေးသားသင့်ပါသည် (အင်္ဂလိပ်အမှတ်တံဆိပ်/ယူနစ်တွဲများကို အလိုအလျောက် ချန်လှပ်ထားသည်)။",
                AuthoritativeSource = "စာတမ်းတည်းဖြတ်ရေး ကိန်းဂဏန်းညီညွတ်မှု စံနှုန်း",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate
                    {
                        Replacement = convertedNum,
                        MeaningCue = $"တစ်ပြေးညီ {targetScriptName} ('{convertedNum} {unitLabel}')",
                        PosTag = "NUM",
                        IsOfficialThatpon = preferMmDigits,
                        Score = 97.0
                    }
                }
            };
        }

        public static string ConvertMyanmarDigitsToAscii(string mmDigits)
        {
            char[] buf = mmDigits.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] >= '\u1040' && buf[i] <= '\u1049')
                    buf[i] = (char)('0' + (buf[i] - '\u1040'));
            }
            return new string(buf);
        }

        private static bool ContainsWithMask(string text, ulong mmMask, string literal)
        {
            for (int i = 0; i < literal.Length; i++)
            {
                int d = literal[i] - '\u1000';
                if ((uint)d < 64u && (mmMask & (1UL << d)) == 0UL)
                    return false;
            }
            return text.IndexOf(literal, StringComparison.Ordinal) >= 0;
        }

        private static bool HasAnyDigit(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '0' && c <= '9') || (c >= '\u1040' && c <= '\u1049'))
                    return true;
            }
            return false;
        }

        private static bool OverlapsAny(IReadOnlyList<ProofingIssue>? issues, int start, int length)
        {
            if (issues == null || issues.Count == 0)
                return false;
            int end = start + length;
            for (int i = 0; i < issues.Count; i++)
            {
                ProofingIssue ex = issues[i];
                if (start < ex.StartIndex + ex.Length && end > ex.StartIndex)
                    return true;
            }
            return false;
        }

        private static List<(int Start, int End)>? ComputeSimpleQuoteRanges(string text)
        {
            List<(int Start, int End)>? ranges = null;
            int len = text.Length;
            int i = 0;
            while (i < len)
            {
                char ch = text[i];
                char closeCh = ch == '\u201C' ? '\u201D' : (ch == '"' ? '"' : '\0');
                if (closeCh != '\0')
                {
                    int limit = Math.Min(len, i + 600);
                    int closeIdx = -1;
                    for (int j = i + 1; j < limit; j++)
                    {
                        if (text[j] == '\r' || text[j] == '\n') break;
                        if (text[j] == closeCh) { closeIdx = j; break; }
                    }
                    if (closeIdx > i + 1)
                    {
                        ranges ??= new List<(int Start, int End)>(2);
                        ranges.Add((i + 1, closeIdx));
                        i = closeIdx + 1;
                        continue;
                    }
                }
                i++;
            }
            return ranges;
        }

        private static bool IsInsideQuotes(List<(int Start, int End)>? ranges, int start, int length)
        {
            if (ranges == null)
                return false;
            int end = start + length;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (start >= r.Start && end <= r.End)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Bounded-RAM (&lt; 8 KB) thread-safe streaming accumulator for cross-cell consistency checks across Excel sheets and workbooks.
    /// Designed so 500,000-cell spreadsheets free every 2D <c>Value2</c> chunk immediately while still harmonizing mixed variants.
    /// </summary>
    public sealed class DocumentConsistencyAccumulator
    {
        private readonly struct CellHitRecord
        {
            public readonly string SheetName;
            public readonly int Row;
            public readonly int Column;
            public readonly string CellSnapshotText;
            public readonly int StartIndex;
            public readonly int Length;
            public readonly string RawMatchedText;
            public readonly string UnitLabel;

            public CellHitRecord(
                string sheetName,
                int row,
                int column,
                string cellSnapshotText,
                int startIndex,
                int length,
                string rawMatchedText,
                string unitLabel = "")
            {
                SheetName = sheetName;
                Row = row;
                Column = column;
                CellSnapshotText = cellSnapshotText;
                StartIndex = startIndex;
                Length = length;
                RawMatchedText = rawMatchedText;
                UnitLabel = unitLabel;
            }
        }

        private readonly object _sync = new object();
        private readonly ProofingStyleMode _styleMode;
        private readonly MyanmarTrie? _trie;
        private readonly int[] _countA;
        private readonly int[] _countB;
        private readonly List<CellHitRecord>?[] _hitsA;
        private readonly List<CellHitRecord>?[] _hitsB;
        private int _mmNumCount;
        private int _asciiNumCount;
        private List<CellHitRecord>? _mmNumHits;
        private List<CellHitRecord>? _asciiNumHits;

        public DocumentConsistencyAccumulator(ProofingStyleMode styleMode, MyanmarTrie? trie = null)
        {
            _styleMode = styleMode;
            _trie = trie;
            int n = DocumentConsistencyAnalyzer.VariantPairs.Length;
            _countA = new int[n];
            _countB = new int[n];
            _hitsA = new List<CellHitRecord>?[n];
            _hitsB = new List<CellHitRecord>?[n];
        }

        public void ObserveCell(
            string sheetName,
            int row,
            int column,
            string cellText,
            IReadOnlyList<ProofingIssue>? existingCellIssues)
        {
            if (string.IsNullOrWhiteSpace(cellText) || cellText.Length > 600)
                return;

            ulong mmMask = OfficialStyleGrammarEngine.ComputeMyanmarCharMask(cellText);
            if (mmMask == 0UL)
                return;

            var pairs = DocumentConsistencyAnalyzer.VariantPairs;
            for (int v = 0; v < pairs.Length; v++)
            {
                var spec = pairs[v];
                if (cellText.IndexOf(spec.FormA, StringComparison.Ordinal) >= 0)
                {
                    DocumentConsistencyAnalyzer.ScanVariantInText(
                        cellText, spec.FormA, spec.RequireStrictLeftBoundary, spec.ForbiddenRightPrefixOnA,
                        existingCellIssues, null,
                        (idx, len) =>
                        {
                            lock (_sync)
                            {
                                _countA[v]++;
                                var list = _hitsA[v] ??= new List<CellHitRecord>(4);
                                if (list.Count < DocumentConsistencyAnalyzer.MaxStoredHitsPerBucket)
                                    list.Add(new CellHitRecord(sheetName, row, column, cellText, idx, len, spec.FormA));
                            }
                        });
                }

                if (cellText.IndexOf(spec.FormB, StringComparison.Ordinal) >= 0)
                {
                    DocumentConsistencyAnalyzer.ScanVariantInText(
                        cellText, spec.FormB, spec.RequireStrictLeftBoundary, spec.ForbiddenRightPrefixOnB,
                        existingCellIssues, null,
                        (idx, len) =>
                        {
                            lock (_sync)
                            {
                                _countB[v]++;
                                var list = _hitsB[v] ??= new List<CellHitRecord>(4);
                                if (list.Count < DocumentConsistencyAnalyzer.MaxStoredHitsPerBucket)
                                    list.Add(new CellHitRecord(sheetName, row, column, cellText, idx, len, spec.FormB));
                            }
                        });
                }
            }

            if (_styleMode == ProofingStyleMode.GeneralStandard &&
                (_trie == null || !OfficialStyleGrammarEngine.IsCleanCanonicalStandaloneNrc(cellText, _trie)))
            {
                bool hasDigit = false;
                for (int i = 0; i < cellText.Length; i++)
                {
                    char c = cellText[i];
                    if ((c >= '0' && c <= '9') || (c >= '\u1040' && c <= '\u1049'))
                    {
                        hasDigit = true;
                        break;
                    }
                }

                if (hasDigit)
                {
                    DocumentConsistencyAnalyzer.ScanNumeralConsistencyInText(
                        cellText, existingCellIssues, null,
                        (idx, len, rawNum, unit, isMm) =>
                        {
                            lock (_sync)
                            {
                                if (isMm)
                                {
                                    _mmNumCount++;
                                    _mmNumHits ??= new List<CellHitRecord>(8);
                                    if (_mmNumHits.Count < DocumentConsistencyAnalyzer.MaxStoredHitsPerBucket)
                                        _mmNumHits.Add(new CellHitRecord(sheetName, row, column, cellText, idx, len, rawNum, unit));
                                }
                                else
                                {
                                    _asciiNumCount++;
                                    _asciiNumHits ??= new List<CellHitRecord>(8);
                                    if (_asciiNumHits.Count < DocumentConsistencyAnalyzer.MaxStoredHitsPerBucket)
                                        _asciiNumHits.Add(new CellHitRecord(sheetName, row, column, cellText, idx, len, rawNum, unit));
                                }
                            }
                        });
                }
            }
        }

        public List<SpreadsheetCellIssue> BuildSpreadsheetConsistencyIssues(string workbookName, int maxIssuesToCollect = 48)
        {
            var output = new List<SpreadsheetCellIssue>();
            if (maxIssuesToCollect <= 0)
                return output;

            lock (_sync)
            {
                var pairs = DocumentConsistencyAnalyzer.VariantPairs;
                for (int v = 0; v < pairs.Length && output.Count < maxIssuesToCollect; v++)
                {
                    if (_countA[v] <= 0 || _countB[v] <= 0)
                        continue;

                    var spec = pairs[v];
                    bool preferA = _countA[v] >= _countB[v];
                    string dominant = preferA ? spec.FormA : spec.FormB;
                    string minority = preferA ? spec.FormB : spec.FormA;
                    int domCount = preferA ? _countA[v] : _countB[v];
                    int minCount = preferA ? _countB[v] : _countA[v];
                    List<CellHitRecord>? targets = preferA ? _hitsB[v] : _hitsA[v];
                    if (targets == null)
                        continue;

                    for (int i = 0; i < targets.Count && output.Count < maxIssuesToCollect; i++)
                    {
                        CellHitRecord hit = targets[i];
                        output.Add(new SpreadsheetCellIssue
                        {
                            WorkbookName = workbookName ?? string.Empty,
                            SheetName = hit.SheetName ?? string.Empty,
                            Row = hit.Row,
                            Column = hit.Column,
                            CellSnapshotText = hit.CellSnapshotText,
                            Issue = DocumentConsistencyAnalyzer.BuildVariantConsistencyIssue(
                                spec, hit.StartIndex, hit.Length, minority, dominant, domCount, minCount),
                            IsResolved = false
                        });
                    }
                }

                if (_styleMode == ProofingStyleMode.GeneralStandard &&
                    _mmNumCount > 0 && _asciiNumCount > 0 &&
                    (_mmNumCount + _asciiNumCount) >= 3 &&
                    Math.Max(_mmNumCount, _asciiNumCount) >= 2 &&
                    output.Count < maxIssuesToCollect)
                {
                    bool preferMm = _mmNumCount >= _asciiNumCount;
                    int domCount = preferMm ? _mmNumCount : _asciiNumCount;
                    int minCount = preferMm ? _asciiNumCount : _mmNumCount;
                    List<CellHitRecord>? targets = preferMm ? _asciiNumHits : _mmNumHits;
                    if (targets != null)
                    {
                        for (int i = 0; i < targets.Count && output.Count < maxIssuesToCollect; i++)
                        {
                            CellHitRecord hit = targets[i];
                            string conv = preferMm
                                ? OfficialStyleGrammarEngine.ConvertAsciiDigitsToMyanmar(hit.RawMatchedText)
                                : DocumentConsistencyAnalyzer.ConvertMyanmarDigitsToAscii(hit.RawMatchedText);

                            output.Add(new SpreadsheetCellIssue
                            {
                                WorkbookName = workbookName ?? string.Empty,
                                SheetName = hit.SheetName ?? string.Empty,
                                Row = hit.Row,
                                Column = hit.Column,
                                CellSnapshotText = hit.CellSnapshotText,
                                Issue = DocumentConsistencyAnalyzer.BuildNumeralConsistencyIssue(
                                    hit.StartIndex, hit.Length, hit.RawMatchedText, conv, hit.UnitLabel, preferMm, domCount, minCount),
                                IsResolved = false
                            });
                        }
                    }
                }
            }

            return output;
        }
    }
}
