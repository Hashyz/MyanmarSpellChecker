// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    public sealed partial class ContextDisambiguator
    {
        private static HashSet<string> CreateNormalizedSet(params string[] items)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Length; i++)
            {
                set.Add(UnicodeNormalizer.NormalizeCanonicalOrder(items[i]));
            }
            return set;
        }

        // Particles, pronouns, nominalizers, and numerals excluded from statistical bigram replacement so grammar words are never altered
        private static readonly HashSet<string> ProtectedFunctionWords = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က", "ကနေ",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မည်", "ပါ", "ပါ့", "ကြ", "ကျ", "ခဲ့",
            "လျက်", "၍", "ကြောင့်", "ဖြင့်", "သဖြင့်", "ကာ", "သာ", "ပင်", "စွာ", "လည်း", "ဘဲ", "ပြီ", "ပြီး", "လျှင်", "ဟု",
            "ခြင်း", "ချင်း", "မှု", "ရင်း", "စဉ်", "တိုင်း", "ဖို့", "ရန်", "ချင်", "တတ်", "နိုင်", "စေ", "လိုက်",
            "တယ်", "မယ်", "မယ့်", "တွေ", "နဲ့", "ဘူး", "တဲ့", "တာ", "ပေါ့", "လား", "လဲ",
            "နော်", "ဗျ", "ဗျာ", "ရှင်", "ရှင့်", "ရဲ့", "လို့", "ပဲ", "တုန်း", "သေး", "တော့", "ဦး",
            "ဒီ", "ဟို", "အဲဒီ", "ထို", "ဤ", "ယင်း", "၎င်း", "သူ", "ငါ", "မင်း", "သင်", "ဘာ", "ဘယ်",
            "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး", "ဆယ်", "ရာ", "ထောင်", "သောင်း", "သိန်း", "သန်း",
            "မ", "အ", "တ", "ရ", "နေ", "ထား", "ပေး", "သွား", "လာ"
        );

        // Generic syntactic particles that attach to any verb/noun and therefore must not act as semantic collocation triggers in bigram disambiguation
        private static readonly HashSet<string> GenericSyntacticParticles = CreateNormalizedSet(
            "သည်", "တယ်", "မည်", "မယ်", "ပါသည်", "ပါတယ်", "ပါမည်", "ပါမယ်",
            "ကို", "အား", "မှ", "သို့", "နှင့်", "နဲ့", "၏", "ရဲ့", "တွင်", "မှာ", "၌", "က", "ကနေ",
            "များ", "တွေ", "တို့", "သော", "သည့်", "မည့်", "မယ့်", "တဲ့", "တာ",
            "ပါ", "ခဲ့", "ပြီ", "ပြီး", "၍", "လျက်", "ကာ", "ကြောင့်", "ဖြင့်", "သဖြင့်",
            "ဟု", "လို့", "လည်း", "ပင်", "သာ", "ပဲ", "ဘဲ", "ဘူး", "လား", "လဲ", "နော်", "ပေါ့", "တော့", "သေး"
        );

        private sealed class ClauseWordToken
        {
            public int StartIndex { get; set; }
            public int Length { get; set; }
            public int EndIndex => StartIndex + Length;
            public int ClauseId { get; set; }
            public int SyllableCount { get; set; }
            public bool IsProtectedName { get; set; }
            public string RawText { get; set; } = string.Empty;
            public string NormText { get; set; } = string.Empty;
        }

        public List<ProofingIssue> AnalyzeContextHomophones(string text, MyanmarTrie? trie = null)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text))
                return issues;

            ulong textMmMask = OfficialStyleGrammarEngine.ComputeMyanmarCharMask(text);

            // 1. High-precision compiled regex collocation & homophone rules
            for (int rIdx = 0; rIdx < Rules.Count; rIdx++)
            {
                var rule = Rules[rIdx];
                if (string.IsNullOrEmpty(rule.Explanation))
                    continue;

                if (rule.RequiredAnyLiterals != null)
                {
                    if ((textMmMask & rule.CommonRequiredMask) != rule.CommonRequiredMask)
                        continue;
                    if (rule.AllRequiredHaveMyanmarChar && (textMmMask & rule.AnyAnchorCharMask) == 0UL)
                        continue;

                    bool anyAnchorHit = false;
                    string[] req = rule.RequiredAnyLiterals;
                    ulong[] reqMasks = rule.RequiredLiteralMasks!;
                    for (int k = 0; k < req.Length; k++)
                    {
                        ulong litMask = reqMasks[k];
                        if ((textMmMask & litMask) == litMask &&
                            text.IndexOf(req[k], StringComparison.Ordinal) >= 0)
                        {
                            anyAnchorHit = true;
                            break;
                        }
                    }
                    if (!anyAnchorHit)
                        continue;
                }

                foreach (Match match in rule.Pattern.Matches(text))
                {
                    Group g = match.Groups[rule.WrongSpanGroup];
                    if (!g.Success) continue;

                    string raw = g.Value;
                    string replacement = rule.Replacement;

                    if (string.IsNullOrEmpty(replacement))
                    {
                        replacement = ComputeDynamicReplacement(raw);
                    }

                    if (raw == replacement)
                        continue;

                    string matchedSpan = match.Value.Trim();
                    string reasoning = !string.Equals(matchedSpan, raw, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(matchedSpan)
                        ? $"ရှေ့/နောက် ဆက်စပ်ပုဒ် '{matchedSpan}' အရ ဤနေရာတွင် '{raw}' အစား အနက်မှန် '{replacement}' ({rule.MeaningCue}) ကို သုံးနှုန်းရမည်။"
                        : $"သတ်ပုံကျမ်း အနက်ခွဲထားစနစ်အရ '{raw}' အစား '{replacement}' ({rule.MeaningCue}) ဟု ရေးသားရမည်။";

                    var suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate
                        {
                            Replacement = replacement,
                            MeaningCue = rule.MeaningCue,
                            IsOfficialThatpon = true,
                            SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(replacement),
                            DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(raw, replacement),
                            Score = 100.0
                        }
                    };

                    if (raw == "စိုးချင်စိုး")
                    {
                        suggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "စိုချင်စို",
                            MeaningCue = "စို (မိုးရေ~စွတ်သည်)",
                            IsOfficialThatpon = true,
                            SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("စိုချင်စို"),
                            DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(raw, "စိုချင်စို"),
                            Score = 88.0
                        });
                    }

                    AddNonOverlapping(issues, new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = raw,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = rule.Explanation,
                        GrammaticalReasoning = reasoning,
                        AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား နှင့် တွဲဖက်အသုံး)",
                        Suggestions = suggestions
                    });
                }
            }

            return issues;
        }

        public List<ProofingIssue> AnalyzePostViterbiContextAndBigrams(
            string text,
            MyanmarTrie? trie,
            List<ProofingIssue> existingIssues,
            List<SyllableToken>? precomputedTokens = null,
            int[]? precomputedLongestMatchLengths = null,
            bool[]? personNameMask = null)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text) || trie == null || (trie.ContextualRuleCount == 0 && trie.BigramCount == 0))
                return issues;

            // 2. Word-level tokenization for Data-Driven ContextualRules & Statistical Bigram Homophone Disambiguation
            List<ClauseWordToken> wordTokens = TokenizeIntoClauseWords(text, trie, precomputedTokens, precomputedLongestMatchLengths, personNameMask);
            if (wordTokens.Count == 0)
                return issues;

            // 2a. Data-Driven ContextualRules from mlc_thatpon.db (collocations, aukmyit/visarga, medial context rules)
            if (trie.ContextualRuleCount > 0)
            {
                for (int i = 0; i < wordTokens.Count; i++)
                {
                    ClauseWordToken tok = wordTokens[i];
                    if (tok.IsProtectedName ||
                        OverlapsExisting(existingIssues, tok.StartIndex, tok.Length) ||
                        OverlapsExisting(issues, tok.StartIndex, tok.Length))
                    {
                        continue;
                    }

                    // Do not apply 1-syllable word-level context rules to an interior syllable glued inside an unspaced lexical compound,
                    // EXCEPT when the right neighbor is a post-verbal suffix/particle (e.g. 'အလွန်ချည်နေပါသည်', 'ဈေးလျော့ပေးသည်')
                    if (tok.SyllableCount == 1 &&
                        i > 0 && wordTokens[i - 1].EndIndex == tok.StartIndex &&
                        i + 1 < wordTokens.Count && tok.EndIndex == wordTokens[i + 1].StartIndex &&
                        !ProtectedFunctionWords.Contains(wordTokens[i + 1].NormText) &&
                        !GenericSyntacticParticles.Contains(wordTokens[i - 1].NormText))
                    {
                        continue;
                    }

                    if (!trie.TryGetContextualRules(tok.NormText, out var ctxRules) || ctxRules == null)
                        continue;

                    for (int r = 0; r < ctxRules.Count; r++)
                    {
                        ContextualRule cr = ctxRules[r];
                        if (tok.NormText == "ရက်" && cr.CorrectForm == "ယက်" && IsCalendarDayContext(text, tok.StartIndex, tok.EndIndex))
                            continue;
                        if (tok.NormText == "ထုတ်" && cr.CorrectForm == "ထုပ်" &&
                            i > 0 && wordTokens[i - 1].NormText == "ကို" &&
                            i + 1 < wordTokens.Count &&
                            (wordTokens[i + 1].NormText == "၏" || wordTokens[i + 1].NormText == "သည်" || wordTokens[i + 1].NormText == "တယ်"))
                        {
                            continue;
                        }

                        if (MatchesContextTrigger(text, wordTokens, i, cr, out string matchedEvidence))
                        {
                            trie.TryGetExactEntries(cr.CorrectForm, out var targetEntries);
                            string cue = targetEntries != null && targetEntries.Count > 0 && !string.IsNullOrWhiteSpace(targetEntries[0].MeaningCue)
                                ? targetEntries[0].MeaningCue
                                : "ရှေ့နောက်စကားစပ်အရ စာလုံးပေါင်းအမှန်";
                            string msg = !string.IsNullOrWhiteSpace(cr.Explanation)
                                ? cr.Explanation
                                : (cr.IsWarningOnly
                                    ? $"အနက်သံသယ စိစစ်ရန် — ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{cr.CorrectForm}' ဖြစ်နိုင်ပါသည်။"
                                    : $"ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{cr.CorrectForm}' ဟု ရေးသားရမည်။");

                            AddNonOverlapping(issues, new ProofingIssue
                            {
                                StartIndex = tok.StartIndex,
                                Length = tok.Length,
                                OriginalText = tok.RawText,
                                IssueType = cr.IsWarningOnly
                                    ? ProofingIssueType.ContextualMeaningWarning
                                    : ProofingIssueType.SpellingError,
                                Message = msg,
                                GrammaticalReasoning = cr.IsWarningOnly
                                    ? $"အနက်အဓိပ္ပာယ်နှင့် တွဲဖက်အသုံး သတိပြုချက် — ဝါကျအတွင်း '{matchedEvidence}' နှင့် တွဲဖက်ရာ၌ '{cr.CorrectForm}' [{cue}] ကို အသုံးများပါသည်။ ရည်ရွယ်ရင်းအနက်နှင့် တိုက်ဆိုင်စိစစ်ပါ။"
                                    : $"ဝါကျအတွင်း အနီးအနားရှိ သက်သေပုဒ် '{matchedEvidence}' နှင့် တွဲဖက်အနက် (Collocation) အရ '{tok.RawText}' အစား '{cr.CorrectForm}' [{cue}] ဖြစ်ရမည်။",
                                AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား နှင့် တွဲဖက်စကားစပ်စံနှုန်း",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = cr.CorrectForm,
                                        MeaningCue = cue,
                                        IsOfficialThatpon = true,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(cr.CorrectForm),
                                        DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(tok.RawText, cr.CorrectForm),
                                        Score = cr.IsWarningOnly ? 94.0 : 100.0
                                    }
                                }
                            });
                            break;
                        }
                    }
                }
            }

            // 2b. Two-Tier Content-Bigram Real-Word Homophone Disambiguation (Immediate + Particle-Skipped SOV Neighbors)
            if (trie.BigramCount > 0)
            {
                for (int i = 0; i < wordTokens.Count; i++)
                {
                    ClauseWordToken tok = wordTokens[i];
                    if (tok.IsProtectedName ||
                        tok.SyllableCount > 2 ||
                        ProtectedFunctionWords.Contains(tok.NormText) ||
                        OverlapsExisting(existingIssues, tok.StartIndex, tok.Length) ||
                        OverlapsExisting(issues, tok.StartIndex, tok.Length) ||
                        !trie.ContainsExactWord(tok.NormText))
                    {
                        continue;
                    }

                    int wFreq = trie.GetWordFrequency(tok.NormText);
                    if (wFreq >= 550)
                        continue;

                    bool hasLeftPunct = i > 0 && wordTokens[i - 1].ClauseId == tok.ClauseId &&
                        HasInterveningNonSpacePunctuation(text, wordTokens[i - 1], tok);
                    bool hasRightPunct = i + 1 < wordTokens.Count && wordTokens[i + 1].ClauseId == tok.ClauseId &&
                        HasInterveningNonSpacePunctuation(text, tok, wordTokens[i + 1]);

                    string prevW = (i > 0 && wordTokens[i - 1].ClauseId == tok.ClauseId && !hasLeftPunct)
                        ? wordTokens[i - 1].NormText
                        : string.Empty;
                    string nextW = (i + 1 < wordTokens.Count && wordTokens[i + 1].ClauseId == tok.ClauseId && !hasRightPunct)
                        ? wordTokens[i + 1].NormText
                        : string.Empty;

                    // Never statistically replace a syllable/word that forms an exact dictionary compound with its immediate neighbor (e.g. 'အက်' + 'ဥပဒေ' = 'အက်ဥပဒေ')
                    if ((!string.IsNullOrEmpty(prevW) && trie.ContainsExactWord(prevW + tok.NormText)) ||
                        (!string.IsNullOrEmpty(nextW) && trie.ContainsExactWord(tok.NormText + nextW)))
                    {
                        continue;
                    }

                    // Particle-skipping SOV neighbor lookup (e.g. 'ကြိုး + ကို + ချဉ်' -> contentPrev = 'ကြိုး')
                    string contentPrevW = prevW;
                    if (!string.IsNullOrEmpty(prevW) && GenericSyntacticParticles.Contains(prevW) &&
                        i >= 2 && wordTokens[i - 2].ClauseId == tok.ClauseId &&
                        !HasInterveningNonSpacePunctuation(text, wordTokens[i - 2], wordTokens[i - 1]) &&
                        !GenericSyntacticParticles.Contains(wordTokens[i - 2].NormText))
                    {
                        contentPrevW = wordTokens[i - 2].NormText;
                    }

                    string contentNextW = nextW;
                    if (!string.IsNullOrEmpty(nextW) && GenericSyntacticParticles.Contains(nextW) &&
                        i + 2 < wordTokens.Count && wordTokens[i + 2].ClauseId == tok.ClauseId &&
                        !HasInterveningNonSpacePunctuation(text, wordTokens[i + 1], wordTokens[i + 2]) &&
                        !GenericSyntacticParticles.Contains(wordTokens[i + 2].NormText))
                    {
                        contentNextW = wordTokens[i + 2].NormText;
                    }

                    bool canUseLeft = !string.IsNullOrEmpty(contentPrevW) && !GenericSyntacticParticles.Contains(contentPrevW);
                    bool canUseRight = !string.IsNullOrEmpty(contentNextW) && !GenericSyntacticParticles.Contains(contentNextW);
                    if (!canUseLeft && !canUseRight)
                        continue;

                    // Only consider words that have ZERO empirical bigram support on both immediate AND particle-skipped content neighbors
                    if ((!string.IsNullOrEmpty(prevW) && trie.GetBigramCount(prevW, tok.NormText) > 0) ||
                        (!string.IsNullOrEmpty(nextW) && trie.GetBigramCount(tok.NormText, nextW) > 0) ||
                        (canUseLeft && trie.GetBigramCount(contentPrevW, tok.NormText) > 0) ||
                        (canUseRight && trie.GetBigramCount(tok.NormText, contentNextW) > 0))
                    {
                        continue;
                    }

                    bool usedParticleSkip = (contentPrevW != prevW && canUseLeft) || (contentNextW != nextW && canUseRight);
                    bool isHighFreqRealWord = wFreq >= 180;
                    bool isMediumFreqRealWord = wFreq >= 12;
                    int minCandFreq = isHighFreqRealWord
                        ? Math.Max(600, wFreq * 5)
                        : (isMediumFreqRealWord ? Math.Max(220, wFreq * 8) : Math.Max(120, wFreq * 15));

                    // Strict homophones only (never cross consonant voicing/aspiration like ဘ->ပ or က->ခ in statistical bigram mode!)
                    var candidates = trie.LookupStrictHomophones(tok.NormText);
                    if (candidates.Count == 0)
                        continue;

                    DictionaryEntry? bestCand = null;
                    int bestBgSupport = 0;
                    bool tokHasHahto = tok.NormText.IndexOf('\u103E') >= 0;
                    var seenWords = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var cand in candidates)
                    {
                        if (!cand.IsOfficialThatpon || cand.Frequency < minCandFreq || cand.Word == tok.NormText ||
                            ProtectedFunctionWords.Contains(cand.Word))
                            continue;

                        // Preserve aspiration 'ှ' so non-homophones like 'လေ' vs 'လှေ' or 'မာ' vs 'မှာ' are never swapped statistically
                        if (tokHasHahto != (cand.Word.IndexOf('\u103E') >= 0) || !seenWords.Add(cand.Word))
                            continue;

                        int candSylCount = cand.SyllableCount > 0
                            ? cand.SyllableCount
                            : MyanmarSyllableBreaker.BreakIntoSyllables(cand.Word, isPreNormalized: true).Count;
                        if (candSylCount != tok.SyllableCount)
                            continue;

                        int leftBg = canUseLeft ? trie.GetBigramCount(contentPrevW, cand.Word) : 0;
                        int rightBg = canUseRight ? trie.GetBigramCount(cand.Word, contentNextW) : 0;
                        int totalBg = leftBg + rightBg;

                        // Never mutate a middle syllable glued contiguously inside an unspaced multi-syllable compound/proper noun
                        // (e.g. 'မကျီးစဥ်' where 'မ + ကြီး' has high bigrams in general prose, while 'ကြီး' has 0 support with the glued right stem!)
                        char prevCh = tok.StartIndex > 0 ? text[tok.StartIndex - 1] : '\0';
                        bool gluedLeftInRun = prevCh >= '\u1000' && prevCh <= '\u109F' && prevCh != '\u104A' && prevCh != '\u104B';
                        int tokEndChar = tok.StartIndex + tok.Length;
                        char nextCh = tokEndChar < text.Length ? text[tokEndChar] : '\0';
                        bool gluedRightInRun = nextCh >= '\u1000' && nextCh <= '\u109F' && nextCh != '\u104A' && nextCh != '\u104B' &&
                                               (string.IsNullOrEmpty(nextW) || !GenericSyntacticParticles.Contains(nextW));
                        if (gluedLeftInRun && gluedRightInRun && (leftBg == 0 || rightBg == 0))
                            continue;

                        bool hasStrongBigramProof = isHighFreqRealWord
                            ? ((leftBg >= 85 && rightBg >= 85) || totalBg >= 680)
                            : (isMediumFreqRealWord
                                ? ((leftBg >= 40 && rightBg >= 40) || totalBg >= 400)
                                : (totalBg >= 120));
                        if (!hasStrongBigramProof || totalBg <= bestBgSupport)
                            continue;

                        double dist = SuggestionEngine.ComputeWeightedMyanmarDistance(tok.NormText, cand.Word, 0.28);
                        if (dist > 0.28)
                            continue;

                        bestBgSupport = totalBg;
                        bestCand = cand;
                    }

                    if (bestCand != null)
                    {
                        // Tier-2 Amber ContextualMeaningWarning when the original word is itself an established real word (wFreq >= 45)
                        // or when bigram evidence came across an intervening particle; Tier-1 Red SpellingError when wFreq < 45 & adjacent!
                        bool emitAsContextWarning = wFreq >= 45 || usedParticleSkip;
                        string cue = !string.IsNullOrWhiteSpace(bestCand.MeaningCue) ? bestCand.MeaningCue : "စကားစပ်အရ စာလုံးပေါင်းအမှန်";
                        string neighborNote = canUseLeft && trie.GetBigramCount(contentPrevW, bestCand.Word) > 0
                            ? $"ရှေ့ပုဒ် '{contentPrevW}' နှင့် '{bestCand.Word}' တွဲဖက်အသုံး"
                            : $"နောက်ပုဒ် '{contentNextW}' နှင့် '{bestCand.Word}' တွဲဖက်အသုံး";
                        AddNonOverlapping(issues, new ProofingIssue
                        {
                            StartIndex = tok.StartIndex,
                            Length = tok.Length,
                            OriginalText = tok.RawText,
                            IssueType = emitAsContextWarning
                                ? ProofingIssueType.ContextualMeaningWarning
                                : ProofingIssueType.SpellingError,
                            Message = emitAsContextWarning
                                ? $"အနက်သံသယ စိစစ်ရန် — ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{bestCand.Word}' [{cue}] ဖြစ်နိုင်ပါသည်။"
                                : $"ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{bestCand.Word}' [{cue}] ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = $"မြန်မာစာ ဝါကျသက်သေအချက်အလက်အရ '{tok.RawText}' သည် ဤနေရာ၌ တွဲဖက်မှုမရှိဘဲ {neighborNote} ({bestBgSupport:N0}+ ကြိမ်) ဖြင့်သာ အနက်ကိုက်ညီပါသည်။",
                            AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာဝါကျတွဲဖက်သက်သေပြုကျမ်း (Bigram Corpus)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = bestCand.Word,
                                    MeaningCue = cue,
                                    PosTag = bestCand.PosTag,
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(bestCand.Word),
                                    DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(tok.RawText, bestCand.Word),
                                    Score = emitAsContextWarning ? 93.0 : 99.0
                                }
                            }
                        });
                    }
                }
            }

            // 2c. Generalized Trie Hidden Over-Split Compound & Dropped-Tone Scanner (Two-Tier Smart Certainty)
            ScanHiddenCompoundAndToneNearMisses(text, trie, wordTokens, existingIssues, issues);

            // 2d. Accidental Particle + Verb Merge Disambiguator (MergedWordChecker — Yellow Review Needed)
            ScanMergedParticleVerbAmbiguities(text, existingIssues, issues);

            return issues;
        }

    }
}
