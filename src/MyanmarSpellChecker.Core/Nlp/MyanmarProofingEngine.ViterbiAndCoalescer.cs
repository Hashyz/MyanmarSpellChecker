// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Performance;

namespace MyanmarSpellChecker.Core.Nlp
{
    public sealed partial class MyanmarProofingEngine
    {
        private static readonly HashSet<string> CommonPersonalNameSylAfterU = CreateNormalizedSet(
            "တင်", "သန်း", "သစ်", "စန်း", "မင်း", "ဝင်း", "အောင်", "ကျော်", "မြင့်", "ထွန်း",
            "လှ", "ဘ", "မြ", "လွင်", "ရွှေ", "ခင်", "စိုး", "ဇော်", "သိန်း", "မောင်", "ကို",
            "ညွန့်", "ကြည်", "တင့်", "ထွေး", "သောင်း", "အေး", "စိန်", "ရဲ", "ပေါင်",
            "စော", "စိုင်း", "ခွန်", "မန်း", "နိုင်း"
        );

        /// <summary>
        /// Distinguishes common Myanmar vocabulary compounds starting with 'ဦး/ဒေါ်/ကို/မောင်/ဆရာ/ဗိုလ်'
        /// (such as 'ဦးခေါင်း', 'ဦးဂေါင်း', 'ဦးနှောက်', 'ဦးနောက်', 'ဦးထုပ်', 'ဦးထုတ်', 'ဦးစီးဌာန', 'ဦးဆောင်', 'မောင်နှမ', 'ဆရာဝန်')
        /// from actual Burmese personal names ('ဦးအောင်ကျော်မိုး', 'ဦးသန်း', 'ဦးတင်အောင်စန်း', 'ဒေါ်ခင်မေသန်း').
        /// </summary>
        private bool LooksLikeVocabularyCompoundWithHonorificPrefix(
            string fullText,
            List<SyllableToken> tokens,
            int startIndex,
            int honorificSylLen)
        {
            string honorificNorm = GetTokenSpan(fullText, tokens, startIndex, honorificSylLen).NormalizedText;
            if (honorificNorm == "ဦး" &&
                startIndex + honorificSylLen < tokens.Count &&
                tokens[startIndex + honorificSylLen].Kind == TokenKind.MyanmarSyllable &&
                CommonPersonalNameSylAfterU.Contains(tokens[startIndex + honorificSylLen].NormalizedText))
            {
                return false;
            }

            int maxSpan = Math.Min(honorificSylLen + 4, tokens.Count - startIndex);

            for (int len = honorificSylLen + 1; len <= maxSpan; len++)
            {
                if (tokens[startIndex + len - 1].Kind != TokenKind.MyanmarSyllable)
                    break;

                var span = GetTokenSpan(fullText, tokens, startIndex, len);
                string fixedSpan = UnicodeNormalizer.FixLookalikeCharacters(span.NormalizedText);
                if ((_trie.TryGetExactEntries(span.NormalizedText, out var exactNormEntries) &&
                     exactNormEntries != null &&
                     exactNormEntries.Any(e => !e.IsProperNoun)) ||
                    (_trie.TryGetExactEntries(fixedSpan, out var exactFixedEntries) &&
                     exactFixedEntries != null &&
                     exactFixedEntries.Any(e => !e.IsProperNoun)) ||
                    _trie.TryGetWrongFormRule(span.NormalizedText, out _))
                {
                    return true;
                }

                // Check official Thatpon headwords sharing this prefix (e.g. ဦးဂေါင်း -> ဦးခေါင်း, ဦးနောက် -> ဦးနှောက်, ဦးထုတ် -> ဦးထုပ်, ဦးစီဌာန -> ဦးစီးဌာန)
                // Require Frequency >= 8 on 2-syllable words so zero-freq archaic headwords like 'ဦးရည်(0)' never collide with personal names like 'ဦးရီ'!
                var spanSyls = new List<string>(len);
                for (int k = 0; k < len; k++)
                    spanSyls.Add(tokens[startIndex + k].NormalizedText);

                IEnumerable<DictionaryEntry> candidates = _trie.LookupStrictHomophones(fixedSpan)
                    .Concat(_trie.LookupByPhoneticKey(fixedSpan))
                    .Concat(_trie.LookupCompoundAnchorCandidates(spanSyls));

                foreach (var e in candidates)
                {
                    if (!e.IsOfficialThatpon ||
                        e.IsProperNoun ||
                        !e.Word.StartsWith(honorificNorm, StringComparison.Ordinal) ||
                        !IsCompatibleCompoundLength(e, len))
                    {
                        continue;
                    }

                    if (len == honorificSylLen + 1 && e.Frequency < 8)
                        continue;

                    if (SuggestionEngine.ComputeWeightedMyanmarDistance(fixedSpan, e.Word, 0.45) <= 0.45)
                        return true;
                }
            }

            return false;
        }

        private void CheckProtectedSingleSyllable(SyllableToken token, List<ProofingIssue> issues)
        {
            string rawSyl = token.Text;
            string normSyl = token.NormalizedText;
            if (IsIgnoredInSession(rawSyl))
                return;

            if (UnicodeNormalizer.HasLookalikeError(rawSyl, out string fixedSyl, out string lookReason))
            {
                AddIssueIfNonOverlapping(issues, new ProofingIssue
                {
                    StartIndex = token.StartIndex,
                    Length = token.Length,
                    OriginalText = rawSyl,
                    IssueType = ProofingIssueType.LookalikeCharacterError,
                    Message = lookReason,
                    Suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate
                        {
                            Replacement = fixedSyl,
                            MeaningCue = "စာလုံးပေါင်းအမှန်",
                            IsOfficialThatpon = true,
                            SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(fixedSyl),
                            DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawSyl, fixedSyl),
                            Score = 100.0
                        }
                    }
                });
                return;
            }

            if (!MyanmarSyllableValidator.ValidateSyllable(rawSyl, out string structErr, out string? detProtectedFix, _trie))
            {
                var suggestions = _suggestionEngine.GetSuggestions(rawSyl);
                if (!string.IsNullOrEmpty(detProtectedFix))
                {
                    EnsureTopSuggestionMetadata(suggestions, rawSyl, detProtectedFix!, "စံဝဏ္ဏ စာလုံးပေါင်းအမှန်");
                }
                AddIssueIfNonOverlapping(issues, new ProofingIssue
                {
                    StartIndex = token.StartIndex,
                    Length = token.Length,
                    OriginalText = rawSyl,
                    IssueType = ProofingIssueType.SpellingError,
                    Message = structErr,
                    Suggestions = suggestions
                });
            }
        }

        private void SegmentClauseWithViterbi(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            List<ProofingIssue> issues,
            ProofingScratchWorkspace ws)
        {
            int m = runEnd - runStart;
            if (m <= 0)
                return;

            // Official corporate/legal loanword coda clarifiers in parentheses, e.g. 'ဖတ်(စ်)', 'အင်ဗက်(စ်)မဲ့(န်)', 'ဟိုး(လ်)ဒင်း' on secm.gov.mm
            if (m == 1)
            {
                SyllableToken singleTok = tokens[runStart];
                string normSingle = singleTok.NormalizedText;
                if (normSingle.Length == 2 && normSingle[1] == '\u103A' &&
                    singleTok.StartIndex > 0 && singleTok.StartIndex + singleTok.Length < fullText.Length)
                {
                    char beforeCh = fullText[singleTok.StartIndex - 1];
                    char afterCh = fullText[singleTok.StartIndex + singleTok.Length];
                    if (((beforeCh == '(' && afterCh == ')') || (beforeCh == '（' && afterCh == '）')) &&
                        !UnicodeNormalizer.HasLookalikeErrorSlice(singleTok.Text, 0, singleTok.Text.Length))
                    {
                        return;
                    }
                }
            }

            // Official administrative, tax-form, court-docket, municipal plot & MOIP NRC initialisms made of bare Myanmar letters
            // (e.g. 'ရပ/၄', 'တတ-၅၃/၁၀', 'ပတခ(ဝင)-၁', '၁၂/ဥကတ(နိုင်)၁၂၃၄၅၆', 'ဇဗသ/နိုင်', 'ဇ.ဗ.သ/နိုင်')
            if (m >= 1 && m <= 5 &&
                (IsGovernmentCodeInitialismRun(fullText, tokens, runStart, runEnd) ||
                 IsValidNrcCardTypeMarkerRun(fullText, tokens, runStart, runEnd)))
            {
                return;
            }

            if (ws.BeamCounts.Length < m + 1)
            {
                int grown = Math.Max((m + 1) * 2, 64);
                ws.BeamTable = new ViterbiBeamSlot[grown * ViterbiBeamWidth];
                ws.BeamCounts = new byte[grown];
                ws.ClauseSpanOffsets = new int[grown];
                ws.ExactMultiWordBits = new ushort[grown];
            }
            else
            {
                Array.Clear(ws.BeamCounts, 1, m);
                Array.Clear(ws.ExactMultiWordBits, 0, m);
            }

            ViterbiBeamSlot[] beamTable = ws.BeamTable;
            byte[] beamCounts = ws.BeamCounts;
            ushort[] exactMultiWordBits = ws.ExactMultiWordBits;
            int[] spanOffsets = ws.ClauseSpanOffsets;
            var allExactSpans = ws.ClauseExactSpans;
            allExactSpans.Clear();
            var candidateEdges = ws.CandidateEdges;

            // Seed BOS / preceding space-separated word state for K=3 Bigram + UPOS-Trigram Beam Viterbi
            ulong seedWordHash = 0UL;
            MyanmarUposTag seedTag = MyanmarUposTag.Boundary;
            for (int p = runStart - 1; p >= Math.Max(0, runStart - 3); p--)
            {
                var pTok = tokens[p];
                if (pTok.Kind == TokenKind.Whitespace)
                    continue;
                if (pTok.Kind == TokenKind.MyanmarSyllable)
                {
                    int wStart = p;
                    while (wStart > 0 &&
                           tokens[wStart - 1].Kind == TokenKind.MyanmarSyllable &&
                           tokens[wStart - 1].StartIndex + tokens[wStart - 1].Length == tokens[wStart].StartIndex &&
                           p - wStart < 4)
                    {
                        wStart--;
                    }
                    int wLen = p - wStart + 1;
                    seedWordHash = MyanmarCompactLanguageModel.ComputeTokenSliceHash(tokens, wStart, wLen);
                    seedTag = wLen == 1
                        ? MyanmarCompactLanguageModel.ResolveWordUposTag(tokens[p].NormalizedText, _trie)
                        : MyanmarCompactLanguageModel.ResolveWordUposTag(ConcatNormalizedTokens(tokens, wStart, wLen), _trie);
                }
                break;
            }

            beamCounts[0] = 1;
            beamTable[0] = new ViterbiBeamSlot
            {
                Cost = 0.0,
                PrevPos = -1,
                PrevBeam = 0,
                PrevTag = MyanmarUposTag.Boundary,
                CurTag = seedTag,
                WordHash = seedWordHash,
                Edge = default
            };

            // Populate clause spans & 1D bitmask directly from precomputed token spans (0 extra Trie node walks!)
            var preAllSpans = ws.AllTokenSpans;
            var preOffsets = ws.TokenSpanOffsets;
            for (int i = 0; i < m; i++)
            {
                spanOffsets[i] = allExactSpans.Count;
                int tokIdx = runStart + i;
                int maxRem = m - i;
                int preStart = preOffsets[tokIdx];
                int preEnd = preOffsets[tokIdx + 1];
                ushort mask = 0;
                for (int s = preStart; s < preEnd; s++)
                {
                    var sp = preAllSpans[s];
                    if (sp.Length > maxRem)
                        break;
                    allExactSpans.Add(sp);
                    if (sp.WrongRule == null && sp.Entries != null && sp.Entries.Count > 0 && sp.Length <= 12)
                    {
                        mask |= (ushort)(1 << sp.Length);
                    }
                }
                exactMultiWordBits[i] = mask;
            }
            spanOffsets[m] = allExactSpans.Count;

            bool isPaliVerseRun = IsClassicalPaliVerseWord(tokens, runStart, runEnd);

            for (int i = 0; i < m; i++)
            {
                int activeBeams = beamCounts[i];
                if (activeBeams == 0)
                    continue;

                BuildEdgesAt(
                    fullText,
                    tokens,
                    runStart,
                    runEnd,
                    i,
                    exactMultiWordBits,
                    isPaliVerseRun,
                    allExactSpans,
                    spanOffsets[i],
                    spanOffsets[i + 1],
                    candidateEdges);

                int srcBase = i * ViterbiBeamWidth;
                for (int b = 0; b < activeBeams; b++)
                {
                    ViterbiBeamSlot src = beamTable[srcBase + b];
                    for (int eIdx = 0; eIdx < candidateEdges.Count; eIdx++)
                    {
                        LatticeEdge edge = candidateEdges[eIdx];
                        int nextPos = i + edge.Length;
                        if (nextPos > m)
                            continue;

                        double rawBgBonus = (src.WordHash != 0UL && edge.WordHash != 0UL)
                            ? _trie.GetFastBigramLogBonus(src.WordHash, edge.WordHash)
                            : 0.0;
                        double bgBonus = (!edge.HasIssue && edge.Length == 1) ? rawBgBonus * 0.25 : rawBgBonus;
                        double rawPosDelta = edge.HasIssue
                            ? MyanmarCompactLanguageModel.GetViterbiPosStepDelta(src.PrevTag, src.CurTag, edge.PosTag)
                            : 0.0;
                        double posPenalty = rawPosDelta > 0.0 ? rawPosDelta * 0.50 : 0.0;
                        double total = src.Cost + edge.Cost - bgBonus + posPenalty;

                        InsertBeamSlot(
                            beamTable,
                            beamCounts,
                            nextPos,
                            new ViterbiBeamSlot
                            {
                                Cost = total,
                                PrevPos = i,
                                PrevBeam = (byte)b,
                                PrevTag = src.CurTag,
                                CurTag = edge.PosTag != MyanmarUposTag.Unk ? edge.PosTag : src.CurTag,
                                WordHash = edge.WordHash,
                                Edge = edge
                            });
                    }
                }
            }

            // Backtrack optimal K=3 Beam Viterbi path from m -> 0 and materialize ONLY winning non-ignored issues!
            var chosenIssues = ws.ChosenIssues;
            chosenIssues.Clear();
            if (beamCounts[m] > 0)
            {
                int curPos = m;
                int curBeam = 0;
                while (curPos > 0)
                {
                    ViterbiBeamSlot winSlot = beamTable[curPos * ViterbiBeamWidth + curBeam];
                    if (winSlot.PrevPos < 0)
                        break;
                    LatticeEdge edge = winSlot.Edge;
                    if (edge.HasIssue && !IsIgnoredInSession(edge.IssueOriginalText!))
                    {
                        ProofingIssue materialized = edge.UnrecognizedSingleSylTokenIndex >= 0
                            ? BuildUnrecognizedSingleSyllableIssue(tokens[edge.UnrecognizedSingleSylTokenIndex])
                            : edge.IssueFactory!();
                        chosenIssues.Add(materialized);
                    }
                    curPos = winSlot.PrevPos;
                    curBeam = winSlot.PrevBeam;
                }
            }

            for (int k = chosenIssues.Count - 1; k >= 0; k--)
            {
                AddIssueIfNonOverlapping(issues, chosenIssues[k]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InsertBeamSlot(ViterbiBeamSlot[] beamTable, byte[] beamCounts, int pos, ViterbiBeamSlot candidate)
        {
            int baseIdx = pos * ViterbiBeamWidth;
            int count = beamCounts[pos];

            // Hypothesis recombination: if an identical (WordHash, CurTag) Markov state is already in the beam, keep the lower-cost path
            for (int k = 0; k < count; k++)
            {
                ref ViterbiBeamSlot existing = ref beamTable[baseIdx + k];
                if (existing.WordHash == candidate.WordHash && existing.CurTag == candidate.CurTag)
                {
                    if (candidate.Cost < existing.Cost)
                    {
                        existing = candidate;
                        while (k > 0 && beamTable[baseIdx + k].Cost < beamTable[baseIdx + k - 1].Cost)
                        {
                            ViterbiBeamSlot tmp = beamTable[baseIdx + k - 1];
                            beamTable[baseIdx + k - 1] = beamTable[baseIdx + k];
                            beamTable[baseIdx + k] = tmp;
                            k--;
                        }
                    }
                    return;
                }
            }

            if (count < ViterbiBeamWidth)
            {
                int ins = count;
                while (ins > 0 && candidate.Cost < beamTable[baseIdx + ins - 1].Cost)
                {
                    beamTable[baseIdx + ins] = beamTable[baseIdx + ins - 1];
                    ins--;
                }
                beamTable[baseIdx + ins] = candidate;
                beamCounts[pos] = (byte)(count + 1);
                return;
            }

            if (candidate.Cost >= beamTable[baseIdx + ViterbiBeamWidth - 1].Cost)
                return;

            int replaceIdx = ViterbiBeamWidth - 1;
            while (replaceIdx > 0 && candidate.Cost < beamTable[baseIdx + replaceIdx - 1].Cost)
            {
                beamTable[baseIdx + replaceIdx] = beamTable[baseIdx + replaceIdx - 1];
                replaceIdx--;
            }
            beamTable[baseIdx + replaceIdx] = candidate;
        }

        private void BuildEdgesAt(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            int localOffset,
            ushort[] exactMultiWordBits,
            bool isPaliVerseRun,
            List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> allExactSpans,
            int spanSliceStart,
            int spanSliceEnd,
            List<LatticeEdge> edgesOut)
        {
            edgesOut.Clear();
            int tokIdx = runStart + localOffset;
            int remaining = runEnd - tokIdx;

            // 1. Exact Trie Words & Explicit Orthography Rules (L = 1 .. 12) — reused from precomputed slice
            uint exactCleanMask = 0;

            for (int s = spanSliceStart; s < spanSliceEnd; s++)
            {
                var (len, entries, wrongRule) = allExactSpans[s];

                if (wrongRule != null)
                {
                    if (wrongRule.Category == "OfficialStyle" && StyleMode != ProofingStyleMode.FormalOfficial)
                    {
                        ulong offHash = MyanmarCompactLanguageModel.ComputeTokenSliceHash(tokens, tokIdx, len);
                        MyanmarUposTag offTag = MyanmarCompactLanguageModel.ResolveUposTagFromEntries(
                            entries, tokens[tokIdx].NormalizedText, tokens[tokIdx + len - 1].NormalizedText);
                        edgesOut.Add(LatticeEdge.Clean(len, 0.86 + 0.10 * len, offHash, offTag));
                        exactCleanMask |= 1u << len;
                        continue;
                    }

                    // Do not trigger 1-syllable Burmese wrong-form rules on transliterated syllables directly attached to parenthesized loanword coda clarifiers like 'ဗက်(စ်)မဲ့(န်)'
                    if (len == 1 && IsAdjacentToParenthesizedCodaClarifier(fullText, tokens[tokIdx]))
                    {
                        edgesOut.Add(LatticeEdge.Clean(1, 1.45));
                        exactCleanMask |= 1u << 1;
                        continue;
                    }

                    // Do not trigger 'ရနေ့ -> ယနေ့' when 'ရ' is an obligative verb suffix attached to a preceding verb (e.g. 'မသုံးစွဲရနေ့')
                    if (len == 2 && tokens[tokIdx].NormalizedText == "ရ" && tokens[tokIdx + 1].NormalizedText == "နေ့" && tokIdx > runStart)
                        continue;

                    // Immediacy / reciprocal constructions '[V] + [V] + ချင်း' ('သိသိချင်း', 'သတင်းရရချင်း') and '[V] + လျှင် + [V] + ချင်း' ('သတင်းရလျှင်ရချင်း') use 'ချင်း', never 'ခြင်း' or 'သီချင်း'
                    if (len == 2 && tokens[tokIdx + 1].NormalizedText == "ချင်း" &&
                        ((tokIdx >= runStart + 1 && tokens[tokIdx - 1].NormalizedText == tokens[tokIdx].NormalizedText) ||
                         (tokIdx >= runStart + 2 &&
                          (tokens[tokIdx - 1].NormalizedText == "လျှင်" || tokens[tokIdx - 1].NormalizedText == "ရင်") &&
                          tokens[tokIdx - 2].NormalizedText == tokens[tokIdx].NormalizedText)))
                    {
                        continue;
                    }

                    // Do not trigger 'အခက်ခဲ -> အခက်အခဲ' when followed by superlative suffix 'ဆုံး' ('အခက်ခဲဆုံး', 'အသေချာဆုံး')
                    if (len == 3 && tokens[tokIdx].NormalizedText == "အ" &&
                        tokIdx + 3 < runEnd && tokens[tokIdx + 3].NormalizedText == "ဆုံး")
                    {
                        continue;
                    }

                    if (IsProtectedSyntacticBoundarySlice(tokens, runStart, runEnd, localOffset, len, exactMultiWordBits))
                    {
                        continue;
                    }

                    var span = GetTokenSpan(fullText, tokens, tokIdx, len);
                    // CMS ellipsis / UTF-8 byte-cut truncation guard: if the chunk ends immediately before '…', '...', or '\uFFFD'
                    // AND CorrectForm is a strict codepoint extension of the truncated span (e.g. 'စပ်လျဉ်...' chopped from 'စပ်လျဉ်း၍...'),
                    // emit a Clean edge rather than flagging a mid-syllable truncation. (Non-prefix rules like 'လောလောဆည်…' still fire!)
                    if (tokIdx + len == runEnd &&
                        IsFollowedImmediatelyByTruncationMarker(fullText, tokens[tokIdx + len - 1], out bool isRuleByteCut))
                    {
                        string canonTarget = UnicodeNormalizer.NormalizeCanonicalOrder(wrongRule.CorrectForm);
                        if (isRuleByteCut ||
                            (canonTarget.Length > span.NormalizedText.Length &&
                             canonTarget.StartsWith(span.NormalizedText, StringComparison.Ordinal)))
                        {
                            exactCleanMask |= 1u << len;
                            edgesOut.Add(LatticeEdge.Clean(len, 0.88));
                            continue;
                        }
                    }

                    var capturedSpan = span;
                    var capturedRule = wrongRule;
                    string ruleTargetNorm = UnicodeNormalizer.NormalizeCanonicalOrder(wrongRule.CorrectForm);
                    ulong ruleTargetHash = MyanmarCompactLanguageModel.ComputeWordHash(ruleTargetNorm);
                    MyanmarUposTag ruleTargetTag = MyanmarCompactLanguageModel.ResolveWordUposTag(ruleTargetNorm, _trie);
                    edgesOut.Add(LatticeEdge.DeferredIssue(
                        len,
                        0.55 + 0.08 * len,
                        span.RawText,
                        () =>
                        {
                            string canonCorrect = UnicodeNormalizer.NormalizeCanonicalOrder(capturedRule.CorrectForm);
                            _trie.TryGetExactEntries(canonCorrect, out var targetEntries);
                            string? ruleMeaningCue = targetEntries != null && targetEntries.Count > 0 ? targetEntries[0].MeaningCue : null;
                            bool allowHomophoneAlternates =
                                capturedSpan.NormalizedText == "ကြော်ညာ" ||
                                (capturedSpan.NormalizedText.Length >= 4 &&
                                 capturedSpan.NormalizedText.EndsWith("ချ", StringComparison.Ordinal) &&
                                 _trie.ContainsExactWord(capturedSpan.NormalizedText.Substring(0, capturedSpan.NormalizedText.Length - 2) + "ကျ"));
                            var suggestions = allowHomophoneAlternates
                                ? _suggestionEngine.GetSuggestions(capturedSpan.NormalizedText)
                                : new List<SuggestionCandidate>(2);
                            EnsureTopSuggestionMetadata(suggestions, capturedSpan.RawText, canonCorrect, ruleMeaningCue);
                            string diffDetail = OrthographicDiffExplainer.ExplainDifference(capturedSpan.RawText, canonCorrect);

                            return new ProofingIssue
                            {
                                StartIndex = capturedSpan.StartIndex,
                                Length = capturedSpan.Length,
                                OriginalText = capturedSpan.RawText,
                                IssueType = capturedRule.Category == "NumeralClassifier"
                                    ? ProofingIssueType.NumeralClassifierError
                                    : (capturedRule.Category == "OfficialStyle"
                                        ? ProofingIssueType.OfficialStyleGrammarError
                                        : ProofingIssueType.SpellingError),
                                Message = string.IsNullOrWhiteSpace(capturedRule.Explanation)
                                    ? BuildDetailedSpellingMessage(capturedSpan.RawText, capturedRule.CorrectForm, ruleMeaningCue, diffDetail)
                                    : capturedRule.Explanation,
                                Suggestions = suggestions
                            };
                        },
                        ruleTargetHash,
                        ruleTargetTag));
                    continue;
                }

                if (len >= 2 && entries != null && entries.Count > 0)
                {
                    int maxFreq = 0;
                    for (int eIdx = 0; eIdx < entries.Count; eIdx++)
                    {
                        if (entries[eIdx].Frequency > maxFreq)
                            maxFreq = entries[eIdx].Frequency;
                    }
                    double freqBonus = Math.Min(0.24, Math.Log10(maxFreq + 1) * 0.065);
                    ulong exactHash = MyanmarCompactLanguageModel.ComputeTokenSliceHash(tokens, tokIdx, len);
                    MyanmarUposTag exactTag = MyanmarCompactLanguageModel.ResolveUposTagFromEntries(
                        entries, tokens[tokIdx].NormalizedText, tokens[tokIdx + len - 1].NormalizedText);

                    int charStart = tokens[tokIdx].StartIndex;
                    SyllableToken endTok = tokens[tokIdx + len - 1];
                    int charLen = (endTok.StartIndex + endTok.Length) - charStart;
                    if (UnicodeNormalizer.HasLookalikeErrorSlice(fullText, charStart, charLen, out string fixedLookalike, out string lookReason))
                    {
                        var capturedSpan = GetTokenSpan(fullText, tokens, tokIdx, len);
                        var capturedFirstEntry = entries[0];
                        string capFixed = fixedLookalike;
                        string capReason = lookReason;
                        edgesOut.Add(LatticeEdge.DeferredIssue(
                            len,
                            0.86 + 0.10 * len,
                            capturedSpan.RawText,
                            () => new ProofingIssue
                            {
                                StartIndex = capturedSpan.StartIndex,
                                Length = capturedSpan.Length,
                                OriginalText = capturedSpan.RawText,
                                IssueType = ProofingIssueType.LookalikeCharacterError,
                                Message = capReason,
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = capFixed,
                                        MeaningCue = capturedFirstEntry.MeaningCue ?? "စာလုံးပေါင်းအမှန်",
                                        PosTag = capturedFirstEntry.PosTag,
                                        IsOfficialThatpon = capturedFirstEntry.IsOfficialThatpon,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(capFixed),
                                        DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(capturedSpan.RawText, capFixed),
                                        Score = 100.0
                                    }
                                }
                            },
                            exactHash,
                            exactTag));
                    }
                    else if (!HasAnyInvalidRawSyllableInRange(tokens, tokIdx, len, _trie))
                    {
                        exactCleanMask |= 1u << len;
                        edgesOut.Add(LatticeEdge.Clean(len, 0.88 + 0.12 * len - freqBonus, exactHash, exactTag));
                    }
                }
            }

            // 1b. Multi-Syllable Induced Creaky Tone ('့', U+1037) on Nouns/Pronouns (e.g., မြန်မာ့, ကမ္ဘာ့, ကျွန်တော့်, နိုင်ငံ့, အမေ့, အဖေ့)
            for (int creakyLen = 2; creakyLen <= Math.Min(4, remaining); creakyLen++)
            {
                if ((exactCleanMask & (1u << creakyLen)) != 0)
                    continue;

                string lastCreakySyl = tokens[tokIdx + creakyLen - 1].NormalizedText;
                if (lastCreakySyl.Length == 0 || lastCreakySyl[lastCreakySyl.Length - 1] != '\u1037')
                    continue;

                string cNorm = ConcatNormalizedTokens(tokens, tokIdx, creakyLen);
                if (IsValidInducedCreakyToneForm(cNorm, lastCreakySyl))
                {
                    exactCleanMask |= 1u << creakyLen;
                    edgesOut.Add(LatticeEdge.Clean(creakyLen, 0.88 + 0.12 * creakyLen - 0.12));
                }
            }

            // 2. Single-Syllable Baseline Edge (L = 1)
            edgesOut.Add(BuildSingleSyllableEdge(fullText, tokens, runStart, runEnd, localOffset, exactMultiWordBits, isPaliVerseRun));

            // 3. Multi-Syllable Generative Repair Edges (L = 2 .. 5)
            int maxRepairLen = Math.Min(5, remaining);
            for (int windowLen = 2; windowLen <= maxRepairLen; windowLen++)
            {
                if ((exactCleanMask & (1u << windowLen)) != 0)
                    continue;

                LatticeEdge? repairEdge = TryBuildCompoundRepairEdge(
                    fullText, tokens, runStart, runEnd, localOffset, windowLen, exactMultiWordBits, isPaliVerseRun);
                if (repairEdge.HasValue)
                {
                    edgesOut.Add(repairEdge.Value);
                }
            }
        }

        private bool IsValidInducedCreakyToneForm(string normWord, string lastSylNorm)
        {
            if (string.IsNullOrEmpty(normWord) || normWord.Length < 2 || normWord[normWord.Length - 1] != '\u1037')
                return false;

            if (_trie.TryGetWrongFormRule(normWord, out _))
                return false;

            if (!MyanmarSyllableBreaker.IsStructurallyValidSyllable(lastSylNorm, out _))
                return false;

            string baseOpen = normWord.Substring(0, normWord.Length - 1);
            if (_trie.TryGetExactEntries(baseOpen, out var baseEntries) &&
                baseEntries != null && baseEntries.Count > 0 &&
                (baseEntries.Any(e => e.IsOfficialThatpon) || baseEntries.Max(e => e.Frequency) >= 4))
            {
                bool isPureVerbOrAdj = (_trie.HasPosTag(baseOpen, "V") || _trie.HasPosTag(baseOpen, "ADJ")) &&
                                       !_trie.HasPosTag(baseOpen, "N") &&
                                       !_trie.HasPosTag(baseOpen, "PRON");
                if (!isPureVerbOrAdj)
                    return true;
            }

            return false;
        }

        private static bool IsClassicalPaliVerseWord(List<SyllableToken> tokens, int runStart, int runEnd)
        {
            int runLen = runEnd - runStart;
            if (runLen < 1 || runLen > 12)
                return false;

            bool hasPaliSignature = false;
            for (int i = runStart; i < runEnd; i++)
            {
                if (UnicodeNormalizer.HasLookalikeError(tokens[i].Text, out _, out _))
                    return false;

                string sy = tokens[i].NormalizedText;
                // Pure Pali Gatha words never use Burmese aukmyit (\u1037), visarga (\u1038), or non-Kinzi killed consonants (\u103A)
                if (sy.IndexOf('\u1037') >= 0 || sy.IndexOf('\u1038') >= 0)
                    return false;

                string withoutKinzi = sy;
                if (sy.IndexOf('\u103A') >= 0)
                {
                    if (sy.IndexOf("\u1004\u103A\u1039", StringComparison.Ordinal) < 0)
                        return false;
                    withoutKinzi = sy.Replace("\u1004\u103A\u1039", string.Empty);
                    if (withoutKinzi.IndexOf('\u103A') >= 0)
                        return false;
                }

                if (!hasPaliSignature && ClassicalPaliMarkerRegex.IsMatch(withoutKinzi))
                    hasPaliSignature = true;
            }

            if (hasPaliSignature)
            {
                if (runLen >= 4)
                    return true;

                string runNorm = ConcatNormalizedTokens(tokens, runStart, runLen);
                if (runNorm.EndsWith("\u1031\u102C", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1031\u102B", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1036", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u103F", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u103F\u102F", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1031\u1014", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u102C\u101A", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u102B\u101A", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u101A\u102C", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1031\u101E\u102F", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1031\u101F\u102D", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1010\u102D", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u101A\u103B", StringComparison.Ordinal) ||
                    runNorm.EndsWith("\u1031", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // Also recognize open/niggahita Pali words (e.g. အယံ, ဣဒံ, ဧဝံ, နမော, ဘဂဝတော) inside a pure Pali Gatha clause
            // where the entire clause between punctuation (၊ / ။ / \n) has >= 4 syllables, ZERO Burmese tones/codas, and contains Pali pat-sint/retroflex markers!
            bool clauseHasPaliMarker = false;
            int cStart = runStart;
            while (cStart > 0)
            {
                var prevTok = tokens[cStart - 1];
                if (prevTok.Kind != TokenKind.MyanmarSyllable &&
                    (prevTok.Text.IndexOf('\u104A') >= 0 || prevTok.Text.IndexOf('\u104B') >= 0 || prevTok.Text.IndexOf('\n') >= 0))
                {
                    break;
                }
                cStart--;
            }

            int cEnd = runEnd;
            while (cEnd < tokens.Count)
            {
                var nextTok = tokens[cEnd];
                if (nextTok.Kind != TokenKind.MyanmarSyllable &&
                    (nextTok.Text.IndexOf('\u104A') >= 0 || nextTok.Text.IndexOf('\u104B') >= 0 || nextTok.Text.IndexOf('\n') >= 0))
                {
                    break;
                }
                cEnd++;
            }

            int clauseSyllableCount = 0;
            string lastClauseSyl = string.Empty;
            for (int i = cStart; i < cEnd; i++)
            {
                if (tokens[i].Kind != TokenKind.MyanmarSyllable)
                    continue;
                clauseSyllableCount++;
                string sy = tokens[i].NormalizedText;
                lastClauseSyl = sy;
                if (sy.IndexOf('\u1037') >= 0 || sy.IndexOf('\u1038') >= 0)
                    return false;
                string withoutKinzi = sy.Replace("\u1004\u103A\u1039", string.Empty);
                if (withoutKinzi.IndexOf('\u103A') >= 0)
                    return false;
                if (ClassicalPaliMarkerRegex.IsMatch(withoutKinzi))
                    clauseHasPaliMarker = true;
            }

            if (clauseHasPaliMarker && clauseSyllableCount >= 4)
                return true;

            // Sentence-initial Pali Nissaya lemma before '၊' (e.g. 'ရောဟိတံ ၊', 'ပလိတံ ၊', 'နိဝေသယေ ၊', 'အမ္ဗာ ၊', 'ဦနတိံသံ ဝါ ၊', 'စေ ကရောတိ ဝါ ၊', 'အနုတီရစာရီ ၊', 'ဧကိကာ၊'):
            // pure open/niggahita Pali lemma with ZERO Burmese tones/codas followed immediately by '၊' and Burmese gloss
            if (cStart == 0 && clauseSyllableCount >= 2 && cEnd < tokens.Count)
            {
                bool followedByCommaDelimiter = false;
                for (int d = cEnd; d < Math.Min(tokens.Count, cEnd + 2); d++)
                {
                    if (tokens[d].Kind == TokenKind.MyanmarSyllable)
                        break;
                    if (tokens[d].Text.IndexOf('\u104A') >= 0 || tokens[d].Text.IndexOf('\u2014') >= 0 || tokens[d].Text.IndexOf('=') >= 0)
                    {
                        followedByCommaDelimiter = true;
                        break;
                    }
                }

                if (followedByCommaDelimiter &&
                    (clauseHasPaliMarker ||
                     lastClauseSyl.EndsWith("\u1036", StringComparison.Ordinal) ||
                     (clauseSyllableCount >= 3 && (lastClauseSyl.EndsWith("\u1031", StringComparison.Ordinal) || lastClauseSyl.EndsWith("\u1031\u102C", StringComparison.Ordinal))) ||
                     lastClauseSyl == "\u1010\u102D" ||
                     lastClauseSyl == "\u101D\u102B" ||
                     lastClauseSyl == "\u101A\u1031" ||
                     lastClauseSyl == "\u101B\u102E" ||
                     lastClauseSyl == "\u1000\u102C"))
                {
                    return true;
                }
            }

            // Full two-pada Pali Gatha verse across '၊' (e.g. 'မနသာ စေ ပဒုဋ္ဌေန၊ ဘာသတိ ဝါ ကရောတိ ဝါ။', 'တတော နံ ဒုက္ခ မနွေတိ၊ စက္ကံဝ ဝဟတော ပဒံ။'):
            // if the entire sentence up to '။' / '\n' has >= 6 syllables, ZERO Burmese tones/non-Kinzi codas, and contains a Pali pat-sint/retroflex marker, both padas are Pali!
            if (clauseSyllableCount >= 2)
            {
                int vStart = cStart;
                while (vStart > 0)
                {
                    var prevTok = tokens[vStart - 1];
                    if (prevTok.Kind != TokenKind.MyanmarSyllable &&
                        (prevTok.Text.IndexOf('\u104B') >= 0 || prevTok.Text.IndexOf('\n') >= 0))
                    {
                        break;
                    }
                    vStart--;
                }

                int vEnd = cEnd;
                while (vEnd < tokens.Count)
                {
                    var nextTok = tokens[vEnd];
                    if (nextTok.Kind != TokenKind.MyanmarSyllable &&
                        (nextTok.Text.IndexOf('\u104B') >= 0 || nextTok.Text.IndexOf('\n') >= 0))
                    {
                        break;
                    }
                    vEnd++;
                }

                if (vStart < cStart || vEnd > cEnd)
                {
                    bool verseHasPaliMarker = clauseHasPaliMarker;
                    int verseSyllableCount = 0;
                    bool pureOpenOrNiggahitaVerse = true;
                    for (int i = vStart; i < vEnd; i++)
                    {
                        if (tokens[i].Kind != TokenKind.MyanmarSyllable)
                            continue;
                        verseSyllableCount++;
                        string sy = tokens[i].NormalizedText;
                        if (sy.IndexOf('\u1037') >= 0 || sy.IndexOf('\u1038') >= 0)
                        {
                            pureOpenOrNiggahitaVerse = false;
                            break;
                        }
                        string withoutKinzi = sy.Replace("\u1004\u103A\u1039", string.Empty);
                        if (withoutKinzi.IndexOf('\u103A') >= 0)
                        {
                            pureOpenOrNiggahitaVerse = false;
                            break;
                        }
                        if (ClassicalPaliMarkerRegex.IsMatch(withoutKinzi))
                            verseHasPaliMarker = true;
                    }

                    if (pureOpenOrNiggahitaVerse && verseHasPaliMarker && verseSyllableCount >= 6)
                        return true;
                }
            }

            return false;
        }

        private static bool HasNonKinziPatSint(string normText)
        {
            if (string.IsNullOrEmpty(normText))
                return false;
            if (normText.IndexOf('\u103F') >= 0)
                return true;
            string withoutKinzi = normText.Replace("\u1004\u103A\u1039", string.Empty);
            return withoutKinzi.IndexOf('\u1039') >= 0;
        }

        private static bool HasNonKinziPatSintOrRetroflex(string normText)
        {
            if (HasNonKinziPatSint(normText))
                return true;
            for (int i = 0; i < normText.Length; i++)
            {
                char c = normText[i];
                if ((c >= '\u100B' && c <= '\u100F') || c == '\u1020')
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasExactSpan(ushort[] exactMultiWordBits, int offset, int len)
        {
            return (exactMultiWordBits[offset] & (1 << len)) != 0;
        }

        private static bool IsCoveredByAnyExactMultiWord(ushort[] exactMultiWordBits, int localOffset, int runLen)
        {
            int minStart = Math.Max(0, localOffset - 11);
            for (int st = minStart; st <= localOffset; st++)
            {
                int minLen = Math.Max(2, localOffset - st + 1);
                ushort mask = (ushort)(0x1FFE & ~((1 << minLen) - 1));
                if ((exactMultiWordBits[st] & mask) != 0)
                    return true;
            }
            return false;
        }

        private static bool IsEndOfExactMultiWordMinLen(ushort[] exactMultiWordBits, int endLocalOffset, int minWordLen)
        {
            int minStart = Math.Max(0, endLocalOffset - 11);
            int maxStart = endLocalOffset - (minWordLen - 1);
            for (int st = minStart; st <= maxStart; st++)
            {
                int len = endLocalOffset - st + 1;
                if (len <= 12 && (exactMultiWordBits[st] & (1 << len)) != 0)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsStartOfAnyExactMultiWord(ushort[] exactMultiWordBits, int startLocalOffset, int runLen)
        {
            return (uint)startLocalOffset < (uint)runLen && (exactMultiWordBits[startLocalOffset] & 0x1FFC) != 0;
        }

        private bool IsProtectedSyntacticBoundarySlice(
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            int localOffset,
            int windowLen,
            ushort[] exactMultiWordBits)
        {
            int tokIdx = runStart + localOffset;
            if (windowLen < 2 || tokIdx + windowLen > runEnd)
                return false;

            string firstSyl = tokens[tokIdx].NormalizedText;
            string lastSyl = tokens[tokIdx + windowLen - 1].NormalizedText;

            // 1. Never detach 3rd syllable 'ပါ' of demonstrative 'အဆိုပါ' (e.g. 'အဆိုပါကျောင်း' != 'ပါကြောင်း', 'အဆိုပါဝင်း' != 'ပါဝင်')
            if (firstSyl == "ပါ" && tokIdx >= runStart + 2 &&
                tokens[tokIdx - 2].NormalizedText == "အ" &&
                tokens[tokIdx - 1].NormalizedText == "ဆို")
            {
                return true;
            }

            // 2. Never detach nominalizer '-ရေး' or '-ဖွယ်' to fuse with a following exact multi-syllable word
            //    (e.g. 'သယ်ယူပို့ဆောင်ရေးယာဉ်' != 'ရေယာဉ်', 'ချွေတာသုံးစွဲရေးအကြောင်း' != 'ရှေးအကြောင်း', 'စက်ဆုပ်ဖွယ်အရာ' != 'ဘယ်အရာ')
            if (firstSyl == "ရေး" &&
                ((localOffset >= 2 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 3)) ||
                 (tokIdx > runStart && IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart))))
            {
                return true;
            }

            if (firstSyl == "ဖွယ်" && tokIdx > runStart &&
                IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 2b. Never let subject particle 'က' after an exact multi-syllable noun/pronoun steal the opening syllable of a following exact multi-syllable word
            //     (e.g. 'မိမိကလွှဲအပ်' must never turn 'ကလွှဲ' into 'ကလွဲ', while genuine typos like 'မိမိကလွဲအပ်' still repair 'လွဲအပ် -> လွှဲအပ်')
            if (windowLen == 2 && firstSyl == "က" && localOffset >= 2 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset - 1, 2) &&
                IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 2c. Never let calendar suffix '-လ' after a multi-syllable month name fuse with a following exact multi-syllable word
            //     (e.g. 'ဝါခေါင်လချည်ဖြူဖွဲ့' must never turn 'လချည်' into 'လှချည်')
            if (windowLen == 2 && firstSyl == "လ" && localOffset >= 2 &&
                (IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 3) ||
                 IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset - 1, 2)) &&
                IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 2c-ii. Never slice off the final syllable of a preceding exact Official multi-syllable word
            //        to fuse with an already-valid standalone word or following exact multi-syllable word
            //        (e.g. 'သောကရောက်သည်' must never slice 'ကရောက် -> ကျရောက်', 'တန်းစဉ်ကူး ပြောင်းနှုန်း' must never slice 'စဉ်ကူး -> စဉ့်ကူး')
            if (windowLen == 2 && localOffset >= 1 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2) &&
                (IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart) ||
                 (_trie.IsStandaloneWord(lastSyl) && _trie.GetWordFrequency(lastSyl) >= 8)))
            {
                for (int prevLen = 2; prevLen <= Math.Min(6, localOffset + 1); prevLen++)
                {
                    int prevStartOffset = (localOffset + 1) - prevLen;
                    if (!HasExactSpan(exactMultiWordBits, prevStartOffset, prevLen))
                        continue;

                    string prevWordNorm = ConcatNormalizedTokens(tokens, runStart + prevStartOffset, prevLen);
                    if (_trie.TryGetExactEntries(prevWordNorm, out var prevEntries) && prevEntries != null)
                    {
                        for (int pe = 0; pe < prevEntries.Count; pe++)
                        {
                            if (prevEntries[pe].IsOfficialThatpon && prevEntries[pe].Frequency >= 2)
                                return true;
                        }
                    }
                }
            }

            // 2c-iii. Spatial entrance suffix '-ဝ' + 'ရောက်' ('အခန်းဝရောက်', 'အိမ်ဝရောက်', 'တံခါးဝရောက်', 'လမ်းဝရောက်') & attached foreign era '-ဒိုခေတ်' ('အဲဒိုခေတ်')
            if (windowLen == 2 && tokIdx > runStart &&
                tokens[tokIdx - 1].StartIndex + tokens[tokIdx - 1].Length == tokens[tokIdx].StartIndex)
            {
                if (firstSyl == "ဝ" && lastSyl == "ရောက်")
                {
                    string prevSyl = tokens[tokIdx - 1].NormalizedText;
                    if (prevSyl == "ခန်း" || prevSyl == "အိမ်" || prevSyl == "တံခါး" || prevSyl == "လမ်း" ||
                        prevSyl == "မြစ်" || prevSyl == "ဂူ" || prevSyl == "စခန်း" || prevSyl == "ကျောင်း" ||
                        prevSyl == "ခြံ" || prevSyl == "ရွာ" || prevSyl == "မြို့")
                    {
                        return true;
                    }
                }
                else if (firstSyl == "ဒို" && lastSyl == "ခေတ်")
                {
                    return true;
                }
            }

            // 2d. Contextual gerund 'စီးခြင်း' ("riding/boarding/flowing") vs 'စီးချင်း' (only valid before 'ထိုး'/'တိုက်' in 'စီးချင်းထိုး')
            if (windowLen == 2 && firstSyl == "စီး" && lastSyl == "ခြင်း")
            {
                bool followedByDuelVerb = false;
                for (int n = tokIdx + 2; n < Math.Min(tokens.Count, tokIdx + 4); n++)
                {
                    if (tokens[n].Kind == TokenKind.MyanmarSyllable)
                    {
                        string ns = tokens[n].NormalizedText;
                        followedByDuelVerb = ns == "ထိုး" || ns == "တိုက်";
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(tokens[n].Text))
                        break;
                }
                if (!followedByDuelVerb)
                    return true;
            }

            if (windowLen == 3 && lastSyl == "ခြင်း" && tokens[tokIdx + 1].NormalizedText == "ချက်" &&
                (firstSyl == "ကြို" || firstSyl == "ဟင်း" || firstSyl == "မင်း"))
            {
                return true;
            }

            // 2e. Contextual gerund 'ချက်ခြင်း' ("cooking/boiling/distilling", e.g. 'ကြိုချက်ခြင်းဖြင့်', 'ထမင်းချက်ခြင်း') vs adverb 'ချက်ချင်း' ("immediately")
            if (windowLen == 2 && firstSyl == "ချက်" && lastSyl == "ခြင်း")
            {
                for (int p = tokIdx - 1; p >= Math.Max(0, tokIdx - 2); p--)
                {
                    if (tokens[p].Kind == TokenKind.MyanmarSyllable)
                    {
                        string ps = tokens[p].NormalizedText;
                        if (ps == "ကြို" || ps == "မင်း" || ps == "ဟင်း" || ps == "ပြုတ်" || ps == "ပေါင်း" || ps == "ကြော်" || ps == "ရက်" || ps == "နွေး")
                            return true;
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(tokens[p].Text))
                        break;
                }
                for (int n = tokIdx + 2; n < Math.Min(tokens.Count, tokIdx + 4); n++)
                {
                    if (tokens[n].Kind == TokenKind.MyanmarSyllable)
                    {
                        string ns = tokens[n].NormalizedText;
                        if (ns == "\u1016\u103C\u1004\u103A\u1037" || ns == "\u1014\u103E\u1004\u103A\u1037" || ns == "\u1000\u103C\u1031\u102C\u1004\u103A\u1037" || ns == "မ")
                            return true;
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(tokens[n].Text))
                        break;
                }
            }

            // 3. Contextual 'နိုင်ငံကြား' ("between countries") vs 'နိုင်ငံခြား' ("foreign")
            if (windowLen == 3 && firstSyl == "နိုင်" && tokens[tokIdx + 1].NormalizedText == "ငံ" && lastSyl == "ကြား")
            {
                for (int p = tokIdx - 1; p >= Math.Max(0, tokIdx - 2); p--)
                {
                    if (tokens[p].Kind == TokenKind.MyanmarSyllable)
                    {
                        string ps = tokens[p].NormalizedText;
                        if (ps == "နှစ်" || ps == "သုံး" || ps == "လေး" || ps == "ဝင်" || ps == "ဖက်" || ps == "ချင်း" || ps == "တွင်း")
                            return true;
                        break;
                    }
                    if (tokens[p].Text.IndexOf('-') >= 0 || tokens[p].Text.IndexOf('–') >= 0)
                        return true;
                    if (!string.IsNullOrWhiteSpace(tokens[p].Text))
                        break;
                }
            }

            // 4. Contextual 'ဥပဒေကြမ်းပြုစု' / 'မူကြမ်းပြုစု' ('ကြမ်းပြု' != 'ကျမ်းပြု')
            if (windowLen == 2 && firstSyl == "ကြမ်း" && lastSyl == "ပြု" && tokIdx > runStart)
            {
                string prevSyl = tokens[tokIdx - 1].NormalizedText;
                if (prevSyl == "ဒေ" || prevSyl == "မူ")
                    return true;
            }

            // 5. Contextual '[Place + တောင်] + ဆိုသော/ဆိုသည့်' (e.g. 'ချင်းတောင်ဆိုသော' != 'တောင်းဆို')
            if (windowLen == 2 && firstSyl == "တောင်" && lastSyl == "ဆို" && tokIdx > runStart && tokIdx + 2 < runEnd)
            {
                string nextSyl = tokens[tokIdx + 2].NormalizedText;
                if (nextSyl == "သော" || nextSyl == "\u101E\u100A\u103A\u1037" || nextSyl == "တာ" || nextSyl == "ရာ")
                    return true;
            }

            // 6. Quantitative '-ကျော်' + [Exact Multi-Word Verb/Noun] (e.g. '၁၅၀ ကီလိုဂရမ်ကျော်စားသုံးပြီး')
            if (windowLen == 2 && firstSyl == "ကျော်" && tokIdx > runStart &&
                IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 7. Administrative compound 'ဦးစီးရုံး' (e.g. 'မြို့နယ်အခွန်ဦးစီးရုံး' must never slice 'စီးရုံး -> စည်းရုံး')
            if (windowLen == 2 && firstSyl == "စီး" && lastSyl == "ရုံး" && tokIdx > runStart &&
                tokens[tokIdx - 1].NormalizedText == "ဦး")
            {
                return true;
            }

            // 8. Directional/serial verb 'လိုက် + [PureVerb]' (e.g. 'ကျောင်းသားတွေလိုက်ဖျက်', 'လိုက်ရှာ', 'လိုက်ဖမ်း' != 'လိုက်ဖက်')
            if (windowLen == 2 && firstSyl == "လိုက်" && lastSyl != "ဖက်" && IsPureVerbSyllable(lastSyl))
            {
                return true;
            }

            // 9. Temporal '-စဉ်' + emphatic 'ပင်' (e.g. 'အသက်ထင်ရှားရှိစဉ်ပင်' != 'စည်ပင်')
            if (windowLen == 2 && firstSyl == "စဉ်" && lastSyl == "ပင်" && tokIdx > runStart)
            {
                return true;
            }

            // 10. Agentive '[Noun] + သတ် + သမား' (e.g. 'ဝက်သတ်သမား', 'နွားသတ်သမား' != 'ဝက်သက်')
            if (windowLen == 2 && lastSyl == "သတ်" && tokIdx + 3 < runEnd &&
                tokens[tokIdx + 2].NormalizedText == "သ" &&
                tokens[tokIdx + 3].NormalizedText == "မား")
            {
                return true;
            }

            // 11. Productive nominalizer '[PureVerb / ရ] + ရေး' (e.g. 'အလုပ်ဆက်ရရေး' != 'ယရည်း')
            if (windowLen == 2 && lastSyl == "ရေး" &&
                (firstSyl == "ရ" || IsPureVerbSyllable(firstSyl)) &&
                firstSyl != "စီ" && firstSyl != "ကြီး")
            {
                return true;
            }

            // 12. Contrastive literary topic particle 'ကား' after pronouns or exact >=2-syllable nouns (e.g. 'သင်ကား' != 'သင်ကြား', 'ဩဘာသကား' != 'သကြား')
            if (windowLen == 2 && lastSyl == "ကား" &&
                (firstSyl == "သင်" || firstSyl == "ငါ" || firstSyl == "သူ" || firstSyl == "ဤ" || firstSyl == "ထို" ||
                 (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2))))
            {
                return true;
            }

            // 13. Clause/phrase-ending simile particle 'လို' after sound/noun compounds (e.g. 'ချိုးကူသံလို သာသာယာယာပဲ' != 'သံလိုက်')
            if (windowLen == 2 && lastSyl == "လို" && tokIdx + 2 == runEnd &&
                (firstSyl == "သံ" || firstSyl == "ဒီ" || firstSyl == "ဟို" || firstSyl == "ဘယ်" ||
                 (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2))))
            {
                return true;
            }

            // 14. Deontic modal auxiliary '-သင့်' ("ought/should") after negated or standalone verb/word before verbal closers (e.g. 'မသုတ်သင့်ချေ', 'သုတ်သင့်ကြပေသည်' != 'သုတ်သင်')
            if (windowLen == 2 && lastSyl == "\u101E\u1004\u103A\u1037" &&
                ((tokIdx > runStart && tokens[tokIdx - 1].NormalizedText == "မ") ||
                 (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2 && tokIdx + 2 < runEnd &&
                  (GrammaticalParticles.Contains(tokens[tokIdx + 2].NormalizedText) ||
                   ColloquialParticles.Contains(tokens[tokIdx + 2].NormalizedText) ||
                   ProductiveVerbalAuxiliaries.Contains(tokens[tokIdx + 2].NormalizedText)))))
            {
                return true;
            }

            // 14b. Pre-numeral classifier 'အ + [Classifier] + [Number]' for multiples of 10 (e.g. 'အယောက် ၂၀', 'အခု ၅၀', 'အလုံး ၁၀၀' != 'အရောက်')
            if (windowLen == 2 && firstSyl == "အ" && NumeralClassifiers.Contains(lastSyl))
            {
                for (int n = tokIdx + 2; n < Math.Min(tokens.Count, tokIdx + 4); n++)
                {
                    if (tokens[n].Kind == TokenKind.Whitespace)
                        continue;
                    if (tokens[n].Kind == TokenKind.MyanmarNumber ||
                        (tokens[n].Text.Length > 0 && char.IsDigit(tokens[n].Text[0])) ||
                        (tokens[n].Kind == TokenKind.MyanmarSyllable && PureNumerals.Contains(tokens[n].NormalizedText)))
                    {
                        return true;
                    }
                    break;
                }
            }

            // 15. Iterative/polite '-ဦး' before imperative/optative particle (e.g. 'ခင်းဦးလော့' != 'ခင်ဦး')
            if (windowLen == 2 && lastSyl == "ဦး" && IsPureVerbSyllable(firstSyl) && tokIdx + 2 < runEnd)
            {
                string afterOo = tokens[tokIdx + 2].NormalizedText;
                if (afterOo == "လော့" || afterOo == "ပါ" || afterOo == "စို့" ||
                    afterOo == "အံ့" || afterOo == "မည်" || afterOo == "မယ်")
                {
                    return true;
                }
            }

            // 16. Composite topic/interrogative particle 'ကရော' ("and what about ...?", e.g. '(KIA) ကရော ဘယ်လိုဖြေရှင်းနေလဲ' != ethnic 'ကယော')
            if (windowLen == 2 && firstSyl == "က" && lastSyl == "ရော")
            {
                bool followedByEthnicNoun = false;
                for (int n = tokIdx + 2; n < Math.Min(tokens.Count, tokIdx + 4); n++)
                {
                    if (tokens[n].Kind == TokenKind.MyanmarSyllable)
                    {
                        string ns = tokens[n].NormalizedText;
                        followedByEthnicNoun = ns == "လူ" || ns == "တိုင်း" || ns == "ဒေ" || ns == "စ";
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(tokens[n].Text))
                        break;
                }
                if (!followedByEthnicNoun)
                    return true;
            }

            // 17. Diminutive noun suffix '-ကလေး' + [PureVerb] (e.g. 'မနက်ခင်းချိုးကလေးကူသလို' != 'ကလေးက')
            if (windowLen == 3 && firstSyl == "က" && tokens[tokIdx + 1].NormalizedText == "လေး" &&
                tokIdx > runStart && IsPureVerbSyllable(lastSyl))
            {
                return true;
            }

            // 18. '[Number/Numeral] + [Classifier] + ဝင်' ("included in the N items/members", e.g. 'အာယတန ၁၂ ပါးဝင်', 'ရတနာသုံးပါးဝင်' != 'ပါဝင်')
            if (windowLen == 2 && lastSyl == "ဝင်" && NumeralClassifiers.Contains(firstSyl))
            {
                for (int p = tokIdx - 1; p >= Math.Max(0, tokIdx - 3); p--)
                {
                    if (tokens[p].Kind == TokenKind.Whitespace)
                        continue;
                    if (tokens[p].Kind == TokenKind.MyanmarNumber ||
                        (tokens[p].Text.Length > 0 && char.IsDigit(tokens[p].Text[0])) ||
                        (tokens[p].Kind == TokenKind.MyanmarSyllable && PureNumerals.Contains(tokens[p].NormalizedText)))
                    {
                        return true;
                    }
                    break;
                }
            }

            // 19. Locative/object nominalizer '[Exact >=2-syl Verb/Compound] + ရာ' (e.g. 'စာသင်ကြားရာ' != 'ကြားရ', 'နေထိုင်ရာ')
            if (windowLen == 2 && lastSyl == "ရာ" && localOffset >= 1 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2) &&
                !_trie.TryGetWrongFormRule(firstSyl + lastSyl, out _))
            {
                return true;
            }

            // 20. Nominalized '[...ချက်] + စာ' (e.g. 'ညွှန်ကြားချက်စာ', 'အကြောင်းကြားချက်စာ' != 'ချက်စား')
            if (windowLen == 2 && firstSyl == "ချက်" && lastSyl == "စာ" && localOffset >= 1 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2))
            {
                return true;
            }

            // 21. Cross-compound L=2 tail-slicing shield: never tear the final syllable off a preceding >=3-syllable exact compound
            //     to fuse with a clean standalone noun, classifier, or post-nominal adjective
            //     (e.g. 'ဆေးကုသဆောင်သစ်' != 'သယ်ဆောင်', 'အရေးပေါ်ဆန်' != 'ပေါ်ဆန်း')
            if (windowLen == 2 && localOffset >= 2 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 3) &&
                (PostNominalAdjectives.Contains(lastSyl) ||
                 NumeralClassifiers.Contains(lastSyl) ||
                 (lastSyl.Length >= 2 && _trie.IsStandaloneWord(lastSyl) && _trie.GetWordFrequency(lastSyl) >= 8)) &&
                !_trie.TryGetWrongFormRule(firstSyl + lastSyl, out _))
            {
                int afterL2Offset = localOffset + 2;
                int runLen = runEnd - runStart;
                if (afterL2Offset >= runLen)
                    return true;

                string afterL2Syl = tokens[runStart + afterL2Offset].NormalizedText;
                if (PostNominalAdjectives.Contains(afterL2Syl) ||
                    GrammaticalParticles.Contains(afterL2Syl) ||
                    ColloquialParticles.Contains(afterL2Syl) ||
                    IsStartOfAnyExactMultiWord(exactMultiWordBits, afterL2Offset, runLen) ||
                    (_trie.IsStandaloneWord(afterL2Syl) && _trie.GetWordFrequency(afterL2Syl) >= 2))
                {
                    return true;
                }
            }

            // 22. Standalone 3-syllable Burmese Personal Name shield (e.g. 'ကျော်လွင်ဦး(မိုးဇလ)', 'သင်းသင်းနွယ်', 'သင်းသင်းနွယ်နှင့်အတူ')
            if (runEnd - runStart >= 3 &&
                localOffset <= 2 &&
                localOffset + windowLen <= 3 &&
                CommonBurmesePersonalNameSyllables.Contains(tokens[runStart].NormalizedText) &&
                CommonBurmesePersonalNameSyllables.Contains(tokens[runStart + 1].NormalizedText) &&
                CommonBurmesePersonalNameSyllables.Contains(tokens[runStart + 2].NormalizedText) &&
                (runEnd - runStart == 3 || TrailingBoundaryParticles.Contains(tokens[runStart + 3].NormalizedText)) &&
                !_trie.TryGetWrongFormRule(ConcatNormalizedTokens(tokens, runStart, 3), out _))
            {
                return true;
            }

            // 23. Productive adverbial suffix '[Exact >=2-syl Noun] + တကျ' ("in an orderly / systematic manner", e.g. 'အစီအစဉ်တကျရေးဆွဲ', 'စည်းကမ်းတကျ', 'နေရာတကျ' != 'တိကျ')
            if (windowLen == 2 && firstSyl == "တ" && lastSyl == "ကျ" && localOffset >= 2 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset - 1, 2))
            {
                return true;
            }

            // 24. Distributive '[Digit / ')' / Numeral / အ] + [NumeralClassifier] + စီ' ("N units/weeks/items each", e.g. '(၁၈)ပတ်စီ', 'တစ်ပတ်စီ', 'အပတ်စီ', 'နှစ်ဦးစီ' != 'ပက်စီ')
            if (windowLen == 2 && lastSyl == "စီ" && NumeralClassifiers.Contains(firstSyl))
            {
                if (tokIdx > runStart && firstSyl == "ပတ်" && tokens[tokIdx - 1].NormalizedText == "အ")
                    return true;

                for (int p = tokIdx - 1; p >= Math.Max(0, tokIdx - 4); p--)
                {
                    if (tokens[p].Kind == TokenKind.Whitespace || tokens[p].Text == ")" || tokens[p].Text == "）")
                        continue;
                    if (tokens[p].Kind == TokenKind.MyanmarNumber ||
                        (tokens[p].Text.Length > 0 && char.IsDigit(tokens[p].Text[0])) ||
                        (tokens[p].Kind == TokenKind.MyanmarSyllable && PureNumerals.Contains(tokens[p].NormalizedText)))
                    {
                        return true;
                    }
                    break;
                }
            }

            // 25. Street-name suffix '[Gem/Color Compound Ending in စိမ်း] + လမ်း' (e.g. 'ကျောက်စိမ်းလမ်း', 'မြစိမ်းလမ်း' != 'စိမ်းလန်း')
            if (windowLen == 2 && firstSyl == "စိမ်း" && lastSyl == "လမ်း" && tokIdx > runStart)
            {
                string prevSyl = tokens[tokIdx - 1].NormalizedText;
                if (prevSyl == "ကျောက်" || prevSyl == "မြ" ||
                    (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2)))
                {
                    return true;
                }
            }

            // 26. Exact >=3-syllable Official/ProperNoun Super-Span Prefix Shield:
            //     Never let a shorter L=2 WrongFormRule (e.g. 'ရေပုန်း -> ရေပုံး') chop off the prefix of a longer exact
            //     Official/ProperNoun headword starting at the same offset (e.g. 'ရေပုန်းရပ်', 'ရေပုန်းရပ်ကွက်', 'ရေပုန်းနယ်မြေ')!
            if (windowLen == 2)
            {
                int runLen = runEnd - runStart;
                int maxSuper = Math.Min(12, runLen - localOffset);
                for (int superLen = 3; superLen <= maxSuper; superLen++)
                {
                    if (!HasExactSpan(exactMultiWordBits, localOffset, superLen))
                        continue;
                    string superNorm = ConcatNormalizedTokens(tokens, tokIdx, superLen);
                    if (_trie.TryGetExactEntries(superNorm, out var sEntries) && sEntries != null)
                    {
                        for (int sIdx = 0; sIdx < sEntries.Count; sIdx++)
                        {
                            if (sEntries[sIdx].IsProperNoun || sEntries[sIdx].IsOfficialThatpon)
                                return true;
                        }
                    }
                }
            }

            // 27. Literary copula '[Word] + ပေ + တည်း' (e.g. 'အကျိုးပေတည်း' != 'အကျိုးပေး')
            if (lastSyl == "ပေ" && tokIdx + windowLen < runEnd &&
                tokens[tokIdx + windowLen].NormalizedText == "တည်း")
            {
                return true;
            }

            // 28. Causative transitive 'ရေ၌နှစ် + လော့/စေ/သွင်း' ("submerge in water", e.g. 'ရေ၌နှစ်လော့' != intransitive 'ရေ၌နစ်')
            if (windowLen == 3 && firstSyl == "ရေ" && tokens[tokIdx + 1].NormalizedText == "၌" && lastSyl == "နှစ်" &&
                tokIdx + 3 < runEnd &&
                (tokens[tokIdx + 3].NormalizedText == "လော့" || tokens[tokIdx + 3].NormalizedText == "စေ" || tokens[tokIdx + 3].NormalizedText == "သွင်း"))
            {
                return true;
            }

            // 29. Accepted modern IT & commercial loanwords ('အယ်ဂိုရီသမ်' Algorithm, 'ဒီဂျစ်တယ်လ်' Digital, 'မြန်မာပလာဇာ' Plaza, 'စတားဝါး / စူပါစတား' Star)
            if ((windowLen >= 3 && firstSyl == "အယ်" && tokens[tokIdx + 1].NormalizedText == "ဂို") ||
                (firstSyl == "ဂျစ်" && tokIdx > runStart && (tokens[tokIdx - 1].NormalizedText == "ဒီ" || tokens[tokIdx - 1].NormalizedText == "ဒစ်")) ||
                (windowLen == 3 && firstSyl == "မာ" && tokens[tokIdx + 1].NormalizedText == "ပ" && lastSyl == "လာ" &&
                 tokIdx > runStart && tokens[tokIdx - 1].NormalizedText == "မြန်" &&
                 tokIdx + 3 < runEnd && tokens[tokIdx + 3].NormalizedText == "ဇာ") ||
                (windowLen == 2 && firstSyl == "စ" && lastSyl == "တား" &&
                 ((tokIdx + 2 < runEnd && tokens[tokIdx + 2].NormalizedText == "ဝါး") ||
                  (tokIdx >= runStart + 2 && tokens[tokIdx - 2].NormalizedText == "စူ" && tokens[tokIdx - 1].NormalizedText == "ပါ"))))
            {
                return true;
            }

            // 30. Inclusive scope construction '[Noun/NominalizedVerb] + ကအစ' ("starting from / including even ~", e.g. 'သေနတ်ပစ်ခတ်ပုံကအစ' != 'ခအစ')
            if (windowLen == 3 && firstSyl == "က" && tokens[tokIdx + 1].NormalizedText == "အ" && lastSyl == "စ" && tokIdx > runStart)
            {
                return true;
            }

            // 31. Formal B2G ceremonial humble auxiliary '[Verb] + အပ်ပါသည်/၏' (e.g. 'မေတ္တာ ပို့သအပ်ပါသည်' != 'အသက်ပါ')
            if (windowLen == 3 && tokens[tokIdx + 1].NormalizedText == "အပ်" && lastSyl == "ပါ" &&
                _trie.IsStandaloneWord(firstSyl))
            {
                return true;
            }

            // 32. Ministry of Commerce freight classification 'ကုန်ကျစ်' ("compact/dense cargo", paired with 'ကုန်ပွ', != 'ကုန်ကျ')
            if (windowLen == 2 && firstSyl == "ကုန်" && lastSyl == "ကျစ်")
            {
                return true;
            }

            // 33. Abhidhamma / Pali jhana compounds ending in '-ဈာန်' (e.g. 'ဝိဝေကဇဈာန်' != 'ကစ')
            if (tokens[runEnd - 1].NormalizedText == "ဈာန်" && runEnd - runStart >= 3)
            {
                return true;
            }

            // 34. Outskirts + paddy-field seam '[ရွာ/မြို့/အိမ်] + ပြင် + လယ်[တော/ကွင်း/ယာ]' (e.g. 'ရွာပြင်လယ်တောထဲ' != 'ပင်လယ်')
            if (windowLen == 2 && firstSyl == "ပြင်" && lastSyl == "လယ်" &&
                ((tokIdx > runStart && (tokens[tokIdx - 1].NormalizedText == "ရွာ" || tokens[tokIdx - 1].NormalizedText == "မြို့" || tokens[tokIdx - 1].NormalizedText == "အိမ်")) ||
                 (tokIdx + 2 < runEnd && (tokens[tokIdx + 2].NormalizedText == "တော" || tokens[tokIdx + 2].NormalizedText == "ကွင်း" || tokens[tokIdx + 2].NormalizedText == "ယာ"))))
            {
                return true;
            }

            // 35. Passive narrative clause-linker 'အ + [Verb] + ခံရာ' (e.g. 'ပြန်သွားအရွေးခံရာ' != 'ခံရ')
            if (windowLen == 2 && firstSyl == "ခံ" && lastSyl == "ရာ" && tokIdx >= runStart + 2 &&
                (tokens[tokIdx - 2].NormalizedText == "အ" ||
                 (tokIdx >= runStart + 3 && tokens[tokIdx - 3].NormalizedText == "အ")))
            {
                return true;
            }

            // 36. Veterinary/municipal literal 'ရေ + ထိုး + ခြင်း/မှု/ပိုက်' ("injecting/spraying water", e.g. 'ရေထိုးခြင်း၊ လေထိုးခြင်း' != 'ရေးထိုး')
            if (windowLen == 2 && firstSyl == "ရေ" && lastSyl == "ထိုး" &&
                tokIdx + 2 < runEnd &&
                (tokens[tokIdx + 2].NormalizedText == "ခြင်း" ||
                 tokens[tokIdx + 2].NormalizedText == "မှု" ||
                 tokens[tokIdx + 2].NormalizedText == "ပိုက်"))
            {
                return true;
            }

            // 37. Productive inceptive verb 'စ + [PureVerb] + [ဖို့/ရန်/ကြ/သည်/တယ်/ပြီး/နေ]' (e.g. 'စီးပွားရေးတစ်ခုစလုပ်ဖို့' != 'စလုတ်')
            if (windowLen == 2 && firstSyl == "စ" && IsPureVerbSyllable(lastSyl) &&
                tokIdx + 2 < runEnd &&
                (tokens[tokIdx + 2].NormalizedText == "ဖို့" ||
                 tokens[tokIdx + 2].NormalizedText == "ရန်" ||
                 tokens[tokIdx + 2].NormalizedText == "ကြ" ||
                 tokens[tokIdx + 2].NormalizedText == "နေ"))
            {
                return true;
            }

            // 38. Cross-compound L=2 tail-slicing before 1-char reduced pre-syllable (e.g. 'ဆေးကုသ + စရစ်' must never slice 'သစ -> သစ်')
            if (windowLen == 2 && localOffset >= 2 && lastSyl.Length == 1 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 3) &&
                tokIdx + 2 < runEnd &&
                !_trie.TryGetWrongFormRule(firstSyl + lastSyl, out _))
            {
                return true;
            }

            // 39. Never fuse an OOV/rare 1-syllable head with existential 'ရှိ' or definitional 'ဆို(တာ)' ('ဇေးရှိတယ်' -> 'ဇေး', 'ဂံဆိုတာ' -> 'ဂံ')
            if (windowLen == 2 &&
                !_trie.TryGetWrongFormRule(firstSyl + lastSyl, out _) &&
                (!_trie.IsStandaloneWord(firstSyl) || _trie.GetWordFrequency(firstSyl) == 0) &&
                (lastSyl == "ရှိ" ||
                 (lastSyl == "ဆို" && tokIdx + 2 < runEnd && tokens[tokIdx + 2].NormalizedText == "တာ")))
            {
                return true;
            }

            // 40. Protect productive 'စာ + ရိုက်' ("type text", e.g. 'စာရိုက်လေ့ကျင့်ခြင်း', 'စာရိုက်စနစ်') from corrupting into 'စရိုက်' ("character/habit")
            if (windowLen == 2 && firstSyl == "စာ" && lastSyl == "ရိုက်")
            {
                return true;
            }

            // 41. Protect spaced reduplicative '[V1]သာ [V2]သာ' idioms ('လုပ်သာ ကိုင်သာ', 'နေသာ ထိုင်သာ', 'သွားသာ လာသာ', 'ပြောသာ ဆိုသာ', 'စားသာ သောက်သာ')
            //     so 'လုပ်သာ' is never mutated to 'လုပ်သား' ("worker") when paired with 'ကိုင်သာ'.
            if (windowLen == 2 && lastSyl == "သာ" &&
                (firstSyl == "လုပ်" || firstSyl == "နေ" || firstSyl == "သွား" || firstSyl == "ပြော" || firstSyl == "စား"))
            {
                int nextTok = tokIdx + 2;
                while (nextTok < tokens.Count && tokens[nextTok].Kind == TokenKind.Whitespace)
                    nextTok++;
                if (nextTok + 1 < tokens.Count &&
                    tokens[nextTok].Kind == TokenKind.MyanmarSyllable &&
                    tokens[nextTok + 1].Kind == TokenKind.MyanmarSyllable &&
                    tokens[nextTok + 1].NormalizedText == "သာ")
                {
                    string v2 = tokens[nextTok].NormalizedText;
                    if ((firstSyl == "လုပ်" && v2 == "ကိုင်") ||
                        (firstSyl == "နေ" && v2 == "ထိုင်") ||
                        (firstSyl == "သွား" && v2 == "လာ") ||
                        (firstSyl == "ပြော" && v2 == "ဆို") ||
                        (firstSyl == "စား" && v2 == "သောက်"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSpacedBoundCompoundNeighbor(string normSyl, string? prevSyl, string? nextSyl)
        {
            if (prevSyl == normSyl || nextSyl == normSyl)
                return true; // reduplication (e.g. ပျပျ, ကျာကျာ, ရှုတ်ရှုတ်, ဟုပ်ဟုပ်, ခြောင်းခြောင်း)

            if (normSyl == "ဖြား" && nextSyl == "ယောင်း") return true;
            if (normSyl == "ပြောက်" && (nextSyl == "ကျား" || nextSyl == "စွန်း")) return true;
            if (normSyl == "ထွတ်" && (prevSyl == "အ" || prevSyl == "ပြည်" || nextSyl == "မြတ်" || nextSyl == "ထား" || nextSyl == "ခေါင်" || nextSyl == "တင်")) return true;
            if (normSyl == "ပျ" && (prevSyl == "ရိုင်း" || nextSyl == "ပိုး")) return true;
            if (normSyl == "ကျာ" && nextSyl == "ပွတ်") return true;
            if (normSyl == "ပျောင်း" && (prevSyl == "ပျော့" || nextSyl == "နွဲ့" || nextSyl == "ညွတ်" || nextSyl == "ခွေ" || nextSyl == "နုံး" || nextSyl == "ညံ့")) return true;
            if (normSyl == "ရှုတ်" && nextSyl == "ချ") return true;
            if (normSyl == "လိန်း" && (nextSyl == "ထေး" || nextSyl == "ထိုး" || nextSyl == "ဖာ")) return true;
            if (normSyl == "ခြိုး" && nextSyl == "ခြံ") return true;
            if (normSyl == "ဖပ်" && nextSyl == "စို") return true;
            return false;
        }

        private static bool IsAdjacentToParenthesizedCodaClarifier(string fullText, SyllableToken token)
        {
            int end = token.StartIndex + token.Length;
            if (end + 4 <= fullText.Length &&
                (fullText[end] == '(' || fullText[end] == '（') &&
                fullText[end + 1] >= '\u1000' && fullText[end + 1] <= '\u1021' &&
                fullText[end + 2] == '\u103A' &&
                (fullText[end + 3] == ')' || fullText[end + 3] == '）'))
            {
                return true;
            }

            int start = token.StartIndex;
            if (start >= 4 &&
                (fullText[start - 1] == ')' || fullText[start - 1] == '）') &&
                fullText[start - 2] == '\u103A' &&
                fullText[start - 3] >= '\u1000' && fullText[start - 3] <= '\u1021' &&
                (fullText[start - 4] == '(' || fullText[start - 4] == '（'))
            {
                return true;
            }

            return false;
        }

        private static readonly Regex GovCodeRightContextRegex = new Regex(
            @"^[ \t]*(?:[-–/\\][ \t]*[\u1040-\u10490-9]|[\(（][\u1000-\u1021\u1025]{1,5}[\)）][ \t]*[-–/\\]|(?:[ \t.]+[\u1000-\u1021\u1025]){0,3}[ \t]*(?:[-–/\\][ \t]*(?:နိုင်|နိင်|နိုင|ဧည့်|ဧည့်|ဧည်|ပြု|ပြူ|သာသနာ့?|သာ|သ|ယာယီ|ယာရီ|စ|န|[NEPTYS])(?![\u1000-\u103F])|[\(（][ \t]*(?:နိုင်|နိင်|နိုင|ဧည့်|ဧည့်|ဧည်|ပြု|ပြူ|သာသနာ့?|သာ|သ|ယာယီ|ယာရီ|စ|န|[NEPTYS])[ \t]*[\)）]))",
            RegexOptions.Compiled);

        private static readonly Regex GovParenBlockCodeRightRegex = new Regex(
            @"^[ \t]*[\(（][ \t]*(?:[\u1040-\u10490-9]{1,5}|[A-Za-z]{1,4}|[\u1000-\u1021]{1,3})[ \t]*[\)）]",
            RegexOptions.Compiled);

        private static readonly Regex GovCodeLeftContextRegex = new Regex(
            @"[\u1040-\u10490-9][ \t]*[-–/\\][ \t]*(?:[\u1000-\u1021\u1025][ \t]*\.[ \t]*){0,3}$",
            RegexOptions.Compiled);

        private static readonly Regex NrcCardTypeLeftContextRegex = new Regex(
            @"[\u1000-\u1021\u1025](?:[ \t.]*[\u1000-\u1021\u1025]){1,3}[ \t]*[\(（/\\–-][ \t]*$",
            RegexOptions.Compiled);

        private static readonly HashSet<string> ValidNrcCardTypeForms = new HashSet<string>(StringComparer.Ordinal)
        {
            "နိုင်", "ဧည့်", "ပြု", "သာသနာ", "သာ", "သ", "ယာယီ", "စ", "န"
        };

        private bool IsValidNrcCardTypeMarkerRun(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd)
        {
            int runLen = runEnd - runStart;
            if (runLen < 1 || runLen > 3)
                return false;

            int startChar = tokens[runStart].StartIndex;
            if (startChar < 3)
                return false;

            // Fast zero-allocation pre-check: must be immediately preceded (ignoring spaces/tabs) by '(', '（', '/', '\', '–', or '-'
            int p = startChar - 1;
            while (p >= 0 && (fullText[p] == ' ' || fullText[p] == '\t'))
                p--;
            if (p < 2)
                return false;
            char sep = fullText[p];
            if (sep != '(' && sep != '（' && sep != '/' && sep != '\\' && sep != '–' && sep != '-')
                return false;

            for (int i = runStart; i < runEnd; i++)
            {
                if (UnicodeNormalizer.HasLookalikeErrorSlice(tokens[i].Text, 0, tokens[i].Text.Length))
                    return false;
            }

            string spanNorm = runLen == 1
                ? tokens[runStart].NormalizedText
                : ConcatNormalizedTokens(tokens, runStart, runLen);

            if (!ValidNrcCardTypeForms.Contains(spanNorm) || _trie.TryGetWrongFormRule(spanNorm, out _))
                return false;

            int endChar = tokens[runEnd - 1].StartIndex + tokens[runEnd - 1].Length;
            int leftStart = Math.Max(0, startChar - 16);
            string leftSlice = fullText.Substring(leftStart, startChar - leftStart);
            if (!NrcCardTypeLeftContextRegex.IsMatch(leftSlice))
                return false;

            if (endChar < fullText.Length)
            {
                char nextCh = fullText[endChar];
                if (nextCh >= '\u1000' && nextCh <= '\u103F')
                    return false;
            }

            return true;
        }

        private bool IsGovernmentCodeInitialismRun(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd)
        {
            for (int i = runStart; i < runEnd; i++)
            {
                string norm = tokens[i].NormalizedText;
                if (norm.Length != 1)
                    return false;
                char ch0 = norm[0];
                if (!((ch0 >= '\u1000' && ch0 <= '\u1021') || ch0 == '\u1025'))
                    return false;
                if (UnicodeNormalizer.HasLookalikeErrorSlice(tokens[i].Text, 0, tokens[i].Text.Length))
                    return false;
            }

            int runLen = runEnd - runStart;
            string spanNorm = runLen == 1
                ? tokens[runStart].NormalizedText
                : ConcatNormalizedTokens(tokens, runStart, runLen);
            if (_trie.TryGetWrongFormRule(spanNorm, out _))
                return false;

            int startChar = tokens[runStart].StartIndex;
            int endChar = tokens[runEnd - 1].StartIndex + tokens[runEnd - 1].Length;

            if (endChar < fullText.Length)
            {
                int rightLen = Math.Min(22, fullText.Length - endChar);
                string rightSlice = fullText.Substring(endChar, rightLen);
                if (GovCodeRightContextRegex.IsMatch(rightSlice))
                    return true;

                // 2..5 bare Myanmar consonant acronym/plot/ward initialism immediately before '(၂)', '(၂၅)', '(၄၁)', '(တ)'
                // (e.g. 'နမ(၂)', 'လလ(၂၅)', 'အရစ(၄၁)', 'ပသယ(တ)', 'ဆရသ(၇၃)', 'နခ(၆၀)')
                if (runLen >= 2 && GovParenBlockCodeRightRegex.IsMatch(rightSlice))
                    return true;

                // Parenthesized bare-consonant Burmese agency/military/department acronyms, e.g. '(လမသ)', '(ကပစ)', '(မဟန)', '(လဝက)', 'ပတခ(ဝင)-၁'
                if (startChar > 0 &&
                    (fullText[startChar - 1] == '(' || fullText[startChar - 1] == '（' || fullText[startChar - 1] == '[') &&
                    (fullText[endChar] == ')' || fullText[endChar] == '）' || fullText[endChar] == ']'))
                {
                    if (runLen >= 2)
                        return true;

                    if (endChar + 1 < fullText.Length)
                    {
                        int afterParenLen = Math.Min(14, fullText.Length - (endChar + 1));
                        string afterParen = fullText.Substring(endChar + 1, afterParenLen);
                        if (GovCodeRightContextRegex.IsMatch(afterParen))
                            return true;
                    }
                }
            }

            if (startChar >= 2)
            {
                int leftStart = Math.Max(0, startChar - 16);
                string leftSlice = fullText.Substring(leftStart, startChar - leftStart);
                if (GovCodeLeftContextRegex.IsMatch(leftSlice))
                    return true;
            }

            return false;
        }

        private LatticeEdge BuildSingleSyllableEdge(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            int localOffset,
            ushort[] exactMultiWordBits,
            bool isPaliVerseRun)
        {
            int tokIdx = runStart + localOffset;
            int runLen = runEnd - runStart;
            SyllableToken token = tokens[tokIdx];
            string rawSyl = token.Text;
            string normSyl = token.NormalizedText;

            if (UnicodeNormalizer.HasLookalikeErrorSlice(rawSyl, 0, rawSyl.Length, out string fixedSyl, out string lookalikeReason))
            {
                string normFixedSyl = UnicodeNormalizer.NormalizeCanonicalOrder(fixedSyl);
                bool isFixedStandalone = _trie.IsStandaloneWord(normFixedSyl) || _trie.ContainsExactWord(normFixedSyl);
                var capToken = token;
                string capRaw = rawSyl;
                string capFixed = fixedSyl;
                string capReason = lookalikeReason;
                return LatticeEdge.DeferredIssue(
                    1,
                    isFixedStandalone ? 1.05 : 1.95,
                    rawSyl,
                    () => new ProofingIssue
                    {
                        StartIndex = capToken.StartIndex,
                        Length = capToken.Length,
                        OriginalText = capRaw,
                        IssueType = ProofingIssueType.LookalikeCharacterError,
                        Message = capReason,
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = capFixed,
                                MeaningCue = "စာလုံးပေါင်းအမှန်",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(capFixed),
                                DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(capRaw, capFixed),
                                Score = 100.0
                            }
                        }
                    });
            }

            if (!MyanmarSyllableValidator.ValidateSyllable(rawSyl, out string structError, out string? detSylFix, _trie))
            {
                var capToken = token;
                string capRaw = rawSyl;
                string capErr = structError;
                string? capDetFix = detSylFix;
                bool isDetStandalone = !string.IsNullOrEmpty(detSylFix) &&
                                       (_trie.IsStandaloneWord(detSylFix!) || _trie.ContainsExactWord(detSylFix!));
                return LatticeEdge.DeferredIssue(
                    1,
                    isDetStandalone ? 1.15 : 2.40,
                    rawSyl,
                    () =>
                    {
                        var suggestions = _suggestionEngine.GetSuggestions(capRaw);
                        if (!string.IsNullOrEmpty(capDetFix))
                        {
                            EnsureTopSuggestionMetadata(suggestions, capRaw, capDetFix!, "စံဝဏ္ဏ စာလုံးပေါင်းအမှန်");
                        }
                        return new ProofingIssue
                        {
                            StartIndex = capToken.StartIndex,
                            Length = capToken.Length,
                            OriginalText = capRaw,
                            IssueType = ProofingIssueType.SpellingError,
                            Message = capErr,
                            Suggestions = suggestions
                        };
                    });
            }

            if (GrammaticalParticles.Contains(normSyl) ||
                (StyleMode != ProofingStyleMode.FormalOfficial && ColloquialParticles.Contains(normSyl)) ||
                NumeralsAndDemonstratives.Contains(normSyl))
            {
                ulong prtHash = MyanmarCompactLanguageModel.ComputeWordHash(normSyl);
                MyanmarUposTag prtTag = MyanmarCompactLanguageModel.ResolveWordUposTag(normSyl, _trie);
                return LatticeEdge.Clean(1, 1.02, prtHash, prtTag);
            }

            // Contextual Bound-Morpheme & Ghost Pat-Sint Index Tab Repair (e.g. 'ဖြားနေတာလား -> ဖျား', 'ဟုပ်လား -> ဟုတ်', 'ပြောက်သွားတယ် -> ပျောက်', 'ရေသောပ်မယ် -> သောက်')
            if (tokens.Count > 1 &&
                BoundSyllableVerbFrameMap.TryGetValue(normSyl, out var boundFix) &&
                !IsCoveredByAnyExactMultiWord(exactMultiWordBits, localOffset, runLen))
            {
                string? prevSyl = null;
                for (int p = tokIdx - 1; p >= Math.Max(0, tokIdx - 2); p--)
                {
                    if (tokens[p].Kind == TokenKind.MyanmarSyllable) { prevSyl = tokens[p].NormalizedText; break; }
                    if (!string.IsNullOrWhiteSpace(tokens[p].Text)) break;
                }
                string? nextSyl = null;
                for (int n = tokIdx + 1; n <= Math.Min(tokens.Count - 1, tokIdx + 2); n++)
                {
                    if (tokens[n].Kind == TokenKind.MyanmarSyllable) { nextSyl = tokens[n].NormalizedText; break; }
                    if (!string.IsNullOrWhiteSpace(tokens[n].Text)) break;
                }

                if (!IsSpacedBoundCompoundNeighbor(normSyl, prevSyl, nextSyl))
                {
                    bool inVerbOrClauseFrame = PureGhostPatSintTabs.Contains(normSyl) ||
                                               (nextSyl != null && BoundVerbFrameFollowers.Contains(nextSyl)) ||
                                               (prevSyl == "မ" && nextSyl != null);
                    if (inVerbOrClauseFrame)
                    {
                        var capToken = token;
                        string capRaw = rawSyl;
                        string capNorm = normSyl;
                        var capBoundFix = boundFix;
                        return LatticeEdge.DeferredIssue(
                            1,
                            1.38,
                            rawSyl,
                            () =>
                            {
                                _trie.TryGetExactEntries(capBoundFix.Target, out var domEntries);
                                string? domCue = domEntries != null && domEntries.Count > 0 ? domEntries[0].MeaningCue : "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်";
                                string? domPos = domEntries != null && domEntries.Count > 0 ? domEntries[0].PosTag : "V";
                                var boundSuggestions = _suggestionEngine.GetSuggestions(capNorm);
                                EnsureTopSuggestionMetadata(boundSuggestions, capRaw, capBoundFix.Target, domCue, domPos, true);

                                return new ProofingIssue
                                {
                                    StartIndex = capToken.StartIndex,
                                    Length = capToken.Length,
                                    OriginalText = capRaw,
                                    IssueType = ProofingIssueType.SpellingError,
                                    Message = capBoundFix.Explanation,
                                    GrammaticalReasoning = $"သတ်ပုံကျမ်းအရ '{capRaw}' သည် တစ်လုံးတည်း သီးခြားကြိယာအဖြစ် မသုံးဘဲ သတ်မှတ်ပေါင်းစပ်ပုဒ်၌သာ တွဲသုံးရသဖြင့် ဤဝါကျစကားစပ်တွင် အသံတူ '{capBoundFix.Target}' ဖြစ်ရမည်။",
                                    AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ",
                                    Suggestions = boundSuggestions
                                };
                            });
                    }
                }
            }

            if (_trie.IsStandaloneWord(normSyl))
            {
                ulong sylHash = MyanmarCompactLanguageModel.ComputeWordHash(normSyl);
                MyanmarUposTag sylTag = MyanmarCompactLanguageModel.ResolveWordUposTag(normSyl, _trie);
                int freq = _trie.GetWordFrequency(normSyl);
                if (freq > 0)
                {
                    double bonus = Math.Min(0.18, Math.Log10(freq + 1) * 0.05);
                    return LatticeEdge.Clean(1, 1.50 - bonus, sylHash, sylTag);
                }

                // Zero-frequency archaic standalone headword: valid if alone, but prefers coalescing into a compound
                return LatticeEdge.Clean(1, 1.65, sylHash, sylTag);
            }

            // Valid dictionary syllable inside a personal name introduced by female honorific 'ဒေါ်' (e.g. 'ဒေါ်တင်တင်အုန်း' where 'အုန်း' has cue 'ခြိမ့်~-')
            if (tokIdx > runStart &&
                runEnd - runStart >= 3 &&
                tokens[runStart].NormalizedText == "ဒေါ်" &&
                _trie.ContainsExactWord(normSyl))
            {
                return LatticeEdge.Clean(1, 1.45);
            }

            // Induced creaky tone on 1-syllable Nouns/Pronouns (e.g. လူ့, သူ့, ငါ့, ကိုယ့်, ရွာ့) — never right before finite verbal closer 'တယ်/သည်/ပါ' ('အရမ်းလှ့တယ်' -> 'လှ')
            bool beforeVerbalCloser = tokIdx + 1 < tokens.Count &&
                                      tokens[tokIdx + 1].Kind == TokenKind.MyanmarSyllable &&
                                      (tokens[tokIdx + 1].NormalizedText == "တယ်" ||
                                       tokens[tokIdx + 1].NormalizedText == "သည်" ||
                                       tokens[tokIdx + 1].NormalizedText == "ပါ" ||
                                       tokens[tokIdx + 1].NormalizedText == "ဘူး");
            if (!beforeVerbalCloser && IsValidInducedCreakyToneForm(normSyl, normSyl))
            {
                return LatticeEdge.Clean(1, 1.44);
            }

            // Valid Pali pat-sint / Kinzi / Great-Sa syllable (e.g. က္ကံ, ဗ္ဗ, ဂ္ဂီ, န္ဒ, င်္ခ, ဋ္ဌာ, ဿု), classical Pali Gatha word / citation stem (e.g. ဓမ္မေန, ပဉ္စိ။), foreign transliteration coda/run (ကားလ်, မတ်ခ်လီး, ဒိုလားရက်စ်, နော်မန် သာဂတ်၊ မေရီကလိဗ်, ဘွန်ထိုက်ချို), hyphenated backchannel interjection (အမ်-ဟမ်), or Shan/Kayah 'လွိုင်' mountain/village prefix (လွိုင်နမ်းဖ)
            if ((_trie.IsValidSingleSyllable(normSyl) && (normSyl.IndexOf('\u1039') >= 0 || normSyl.IndexOf('\u103F') >= 0)) ||
                IsForeignOrPaliKilledCoda(normSyl) ||
                (!_trie.TryGetWrongFormRule(normSyl, out _) && _trie.IsValidSingleSyllable(normSyl) && RunHasForeignKilledCoda(tokens, tokIdx, runStart, runEnd)) ||
                (normSyl == "ဘွန်" && runEnd - runStart >= 3) ||
                IsInCommaCoordinatedForeignNameList(fullText, tokens, tokIdx) ||
                IsHyphenatedRhymingInterjection(fullText, token) ||
                IsPaliPatSintCitationStem(fullText, token, normSyl) ||
                (normSyl == "လွိုင်" && tokIdx + 1 < runEnd) ||
                isPaliVerseRun ||
                IsAdjacentToParenthesizedCodaClarifier(fullText, token))
            {
                return LatticeEdge.Clean(1, 1.48);
            }

            // Mid-word / mid-codepoint CMS ellipsis or byte-cut truncation (e.g. 'တာဝန်ရှိသ\uFFFD...', 'သ…')
            if (tokIdx + 1 == runEnd &&
                IsFollowedImmediatelyByTruncationMarker(fullText, token, out bool isUtf8ByteCut1) &&
                !_trie.TryGetWrongFormRule(normSyl, out _) &&
                (isUtf8ByteCut1 || _trie.HasStrictHeadwordPrefix(tokens, tokIdx, 1)))
            {
                return LatticeEdge.Clean(1, 1.35);
            }

            // Unrecognized single syllable -> defer expensive GetSuggestions unless this L=1 edge actually wins Viterbi!
            return LatticeEdge.UnrecognizedSingleSyllable(tokIdx, rawSyl, 2.65);
        }

        private static bool RunHasForeignKilledCoda(List<SyllableToken> tokens, int tokIdx, int runStart, int runEnd)
        {
            int lo = Math.Max(runStart, tokIdx - 4);
            int hi = Math.Min(runEnd, tokIdx + 5);
            for (int r = lo; r < hi; r++)
            {
                string s = tokens[r].NormalizedText;
                if (IsForeignOrPaliKilledCoda(s))
                    return true;
                if (r > runStart && s == "\u1005\u103A" && tokens[r - 1].NormalizedText.IndexOf('\u103A') > 0)
                    return true;
            }
            return false;
        }

        private static bool IsInCommaCoordinatedForeignNameList(string fullText, List<SyllableToken> tokens, int tokIdx)
        {
            // e.g. 'နော်မန် သာဂတ်၊ မေရီကလိဗ်နှင့် မတ်ခ်လီးတို့' where a short surname token immediately precedes '၊' followed by a foreign-killed-coda name
            SyllableToken tok = tokens[tokIdx];
            int afterPos = tok.StartIndex + tok.Length;
            if (afterPos >= fullText.Length || fullText[afterPos] != '\u104A')
                return false;
            int lookEnd = Math.Min(tokens.Count, tokIdx + 10);
            for (int k = tokIdx + 1; k < lookEnd; k++)
            {
                if (tokens[k].Kind == TokenKind.MyanmarSyllable && IsForeignOrPaliKilledCoda(tokens[k].NormalizedText))
                    return true;
            }
            return false;
        }

        private static bool IsHyphenatedRhymingInterjection(string fullText, SyllableToken token)
        {
            // e.g. 'အမ်-ဟမ်' or 'အင်း-ဟင်း' where a short syllable immediately follows '-' after a rhyming syllable
            int idx = token.StartIndex;
            if (idx < 3 || fullText[idx - 1] != '-' || token.Length < 2 || token.Length > 5)
                return false;
            string rightRhyme = token.NormalizedText.Substring(1);
            int leftStart = Math.Max(0, idx - 1 - token.Length - 1);
            string leftSlice = UnicodeNormalizer.NormalizeCanonicalOrder(fullText.Substring(leftStart, (idx - 1) - leftStart));
            return rightRhyme.Length >= 1 && leftSlice.EndsWith(rightRhyme, StringComparison.Ordinal);
        }

        private static bool IsPaliPatSintCitationStem(string fullText, SyllableToken token, string normSyl)
        {
            // e.g. 'မောဂ်-၅၊ ၄၄၊ ပဉ္စိ။' — Pali pat-sint stem ending in short Pali vowel (-ိ/-ု/-ေ) right before '။' in a numbered citation
            if (!HasNonKinziPatSint(normSyl) || normSyl.IndexOf('\u1037') >= 0 || normSyl.IndexOf('\u1038') >= 0)
                return false;
            char tail = normSyl[normSyl.Length - 1];
            if (tail != '\u102D' && tail != '\u102F' && tail != '\u1031')
                return false;
            int after = token.StartIndex + token.Length;
            if (after >= fullText.Length || fullText[after] != '\u104B')
                return false;
            int lookBack = Math.Max(0, token.StartIndex - 16);
            for (int k = lookBack; k < token.StartIndex; k++)
            {
                if (fullText[k] >= '\u1040' && fullText[k] <= '\u1049')
                    return true;
            }
            return false;
        }

        private static bool IsFollowedImmediatelyByTruncationMarker(string fullText, SyllableToken endTok, out bool isUtf8ByteCut)
        {
            isUtf8ByteCut = false;
            int end = endTok.StartIndex + endTok.Length;
            if (end >= fullText.Length)
                return false;

            char ch = fullText[end];
            if (ch == '\uFFFD')
            {
                isUtf8ByteCut = true;
                return true;
            }

            // Allow up to 2 horizontal spaces before CMS card ellipsis ('စီမ ....', 'ရုံးအမှတ ....', 'ဒုတိယညွှန်ကြားရေးမှူးချု …')
            int pos = end;
            int spaces = 0;
            while (pos < fullText.Length && (fullText[pos] == ' ' || fullText[pos] == '\t') && spaces < 2)
            {
                pos++;
                spaces++;
            }
            if (pos >= fullText.Length)
                return false;

            ch = fullText[pos];
            if (ch == '\u2026')
                return true;

            if (ch == '.' && pos + 1 < fullText.Length && fullText[pos + 1] == '.' &&
                (spaces == 0 || (pos + 2 < fullText.Length && fullText[pos + 2] == '.')))
            {
                return true;
            }

            return false;
        }

        private ProofingIssue BuildUnrecognizedSingleSyllableIssue(SyllableToken token)
        {
            string rawSyl = token.Text;
            string normSyl = token.NormalizedText;
            var singleSuggestions = _suggestionEngine.GetSuggestions(normSyl);
            string singleMessage = singleSuggestions.Count > 0 && !string.IsNullOrWhiteSpace(singleSuggestions[0].DiagnosticDetail)
                ? $"'{rawSyl}' သည် သတ်ပုံကျမ်းဝင် စာလုံးပေါင်းမဟုတ်ပါ — အနီးစပ်ဆုံး '{singleSuggestions[0].Replacement}' ({singleSuggestions[0].DiagnosticDetail})။"
                : $"'{rawSyl}' သည် မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းတွင် မပါဝင်သော စာလုံးပေါင်းဖြစ်နေပါသည်။";

            bool hasExplicitSingleRule = _trie.TryGetWrongFormRule(normSyl, out _);
            return new ProofingIssue
            {
                StartIndex = token.StartIndex,
                Length = token.Length,
                OriginalText = rawSyl,
                IssueType = ProofingIssueType.SpellingError,
                Message = singleMessage,
                RequiresReviewBeforeBatchFix = !hasExplicitSingleRule,
                Suggestions = singleSuggestions
            };
        }

        private LatticeEdge? TryBuildCompoundRepairEdge(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            int localOffset,
            int windowLen,
            ushort[] exactMultiWordBits,
            bool isPaliVerseRun = false)
        {
            int startIndex = runStart + localOffset;
            int charStart = tokens[startIndex].StartIndex;
            SyllableToken lastWinTok = tokens[startIndex + windowLen - 1];
            int charLen = (lastWinTok.StartIndex + lastWinTok.Length) - charStart;

            // Tier 1: Lookalike / Misplaced Kinzi / Digit Zero / U-Nya / Tall AA / စျ / သြ / ဦ repair (zero-alloc fast reject when no lookalike char)
            if (UnicodeNormalizer.HasLookalikeErrorSlice(fullText, charStart, charLen, out string fixedCompound, out string lookalikeExplanation))
            {
                string normFixed = UnicodeNormalizer.NormalizeCanonicalOrder(fixedCompound);
                if (_trie.ContainsExactWord(normFixed))
                {
                    var capSpan = GetTokenSpan(fullText, tokens, startIndex, windowLen);
                    string capNormFixed = normFixed;
                    string capReason = lookalikeExplanation;
                    return LatticeEdge.DeferredIssue(
                        windowLen,
                        0.85 + 0.10 * windowLen,
                        capSpan.RawText,
                        () =>
                        {
                            var suggestions = new List<SuggestionCandidate>(2);
                            EnsureTopSuggestionMetadata(suggestions, capSpan.RawText, capNormFixed, "စာလုံးပေါင်းအမှန်");
                            return new ProofingIssue
                            {
                                StartIndex = capSpan.StartIndex,
                                Length = capSpan.Length,
                                OriginalText = capSpan.RawText,
                                IssueType = ProofingIssueType.LookalikeCharacterError,
                                Message = capReason,
                                Suggestions = suggestions
                            };
                        });
                }

                if (_trie.TryGetWrongFormRule(normFixed, out var ruleAfterLookalike) && ruleAfterLookalike != null)
                {
                    var capSpan = GetTokenSpan(fullText, tokens, startIndex, windowLen);
                    var capRule = ruleAfterLookalike;
                    string capReason = lookalikeExplanation;
                    return LatticeEdge.DeferredIssue(
                        windowLen,
                        0.55 + 0.08 * windowLen,
                        capSpan.RawText,
                        () =>
                        {
                            string targetWord = UnicodeNormalizer.NormalizeCanonicalOrder(capRule.CorrectForm);
                            var suggestions = new List<SuggestionCandidate>(2);
                            EnsureTopSuggestionMetadata(suggestions, capSpan.RawText, targetWord, "စာလုံးပေါင်းအမှန်");
                            return new ProofingIssue
                            {
                                StartIndex = capSpan.StartIndex,
                                Length = capSpan.Length,
                                OriginalText = capSpan.RawText,
                                IssueType = ProofingIssueType.LookalikeCharacterError,
                                Message = capRule.Explanation ?? capReason,
                                Suggestions = suggestions
                            };
                        });
                }

                // Pure-Lookalike Neighbor Corruption Shield:
                // Whenever a multi-syllable span contains a lookalike error (e.g. 'ဆီပောက် -> ဆီ+ပေါက်', 'မ၀င်အောင် -> မ+ဝင်+အောင်', 'ပွါးစေ -> ပွား+စေ')
                // whose lookalike-fixed concatenation is NOT a single dictionary entry, if every individual syllable after lookalike normalization
                // is already a valid standalone word or grammatical/verbal/colloquial particle, do NOT fall through to Tier 2/3 fuzzy compound repair
                // (which would corrupt clean neighbors into 'ဆည်ပေါက်', 'ဝအောင်', or 'ပါစေ') — let 1-syllable lookalike repair fix the exact syllable!
                bool allSylsValidAfterLookalike = true;
                for (int k = 0; k < windowLen; k++)
                {
                    string rawK = tokens[startIndex + k].Text;
                    string fixedK = UnicodeNormalizer.HasLookalikeErrorSlice(rawK, 0, rawK.Length, out string lkOut, out _)
                        ? UnicodeNormalizer.NormalizeCanonicalOrder(lkOut)
                        : tokens[startIndex + k].NormalizedText;
                    if (_trie.TryGetWrongFormRule(fixedK, out _) ||
                        !(_trie.IsStandaloneWord(fixedK) ||
                          GrammaticalParticles.Contains(fixedK) ||
                          ColloquialParticles.Contains(fixedK) ||
                          VerbalContextFollowers.Contains(fixedK) ||
                          ProductiveVerbalAuxiliaries.Contains(fixedK) ||
                          NumeralsAndDemonstratives.Contains(fixedK)))
                    {
                        allSylsValidAfterLookalike = false;
                        break;
                    }
                }
                if (allSylsValidAfterLookalike)
                {
                    return null;
                }
            }

            if (isPaliVerseRun)
                return null;

            // Mid-syllable / mid-word CMS ellipsis or UTF-8 byte-cut truncation at the end of a clause chunk
            // (e.g. 'China-ASEAN Expo အားသွားရေ…' cut from 'သွားရောက်…', 'စပ်လျဉ်...' cut from 'စပ်လျဉ်း...', 'တာဝန်ရှိသ\uFFFD...' cut from 'တာဝန်ရှိသူ...')
            // Keeps catching real WrongFormRules ('လောလောဆည်…') and non-prefix typos ('ပျော်ရွင်…')!
            if (startIndex + windowLen == runEnd &&
                IsFollowedImmediatelyByTruncationMarker(fullText, lastWinTok, out bool isUtf8ByteCut))
            {
                string truncNorm = ConcatNormalizedTokens(tokens, startIndex, windowLen);
                if (!_trie.TryGetWrongFormRule(truncNorm, out _) &&
                    (isUtf8ByteCut || _trie.HasStrictHeadwordPrefix(tokens, startIndex, windowLen)))
                {
                    return LatticeEdge.Clean(windowLen, 0.88);
                }
            }

            string firstSyl = tokens[startIndex].NormalizedText;
            string lastSyl = lastWinTok.NormalizedText;

            // Pali Pat-Sint Cluster Boundary Guard: never slice a repair window mid-word starting at or ending immediately before a non-Kinzi pat-sint stack (\u1039 or \u103F)
            if ((startIndex > runStart && HasNonKinziPatSint(firstSyl)) ||
                (startIndex + windowLen < runEnd && HasNonKinziPatSint(tokens[startIndex + windowLen].NormalizedText)))
            {
                return null;
            }

            // Tier 1b: Dropped-Asat ('်', U+103A) Generative Syllable Merge (e.g. 'တယ' -> 'တယ်', 'မယ' -> 'မယ်', 'သည' -> 'သည်', 'စွမ်းဆောင်ရည' -> 'စွမ်းဆောင်ရည်', 'ရလဒ' -> 'ရလဒ်', 'အတွက' -> 'အတွက်', 'အချက်အလက' -> 'အချက်အလက်')
            // Excludes bare particle consonants 'က' and 'မ' for L=2 so 'လက'/'ညက'/'စမ...' never false-merge,
            // and for L>=3 'က' strictly requires non-word prefix + non-word penultimate stem so '[Noun] + က' is 100% protected!
            if (windowLen >= 2 && windowLen <= 5 && IsDroppedAsatBareConsonant(lastSyl, windowLen >= 3, out char finalCons, out string trailingTone))
            {
                bool blockedByLongerOrRightCompound = false;
                for (int longerLen = windowLen + 1; longerLen <= 12; longerLen++)
                {
                    if (HasExactSpan(exactMultiWordBits, localOffset, longerLen))
                    {
                        blockedByLongerOrRightCompound = true;
                        break;
                    }
                }

                string prefixNorm = windowLen == 2
                    ? firstSyl
                    : ConcatNormalizedTokens(tokens, startIndex, windowLen - 1);

                if (finalCons == '\u1000')
                {
                    // Guard any legitimate '[Word] + က' case-particle attachment!
                    string penultCheck = windowLen == 3
                        ? tokens[startIndex + 1].NormalizedText
                        : ConcatNormalizedTokens(tokens, startIndex + windowLen - 3, 2);
                    if (_trie.IsStandaloneWord(prefixNorm) ||
                        _trie.ContainsExactWord(prefixNorm) ||
                        (windowLen == 3 ? _trie.IsStandaloneWord(penultCheck) : _trie.ContainsExactWord(penultCheck)) ||
                        (startIndex + windowLen < runEnd && _trie.HasStrictHeadwordPrefix(tokens, startIndex + windowLen - 1, 2)))
                    {
                        blockedByLongerOrRightCompound = true;
                    }
                }

                if (!blockedByLongerOrRightCompound &&
                    _trie.IsStandaloneWord(prefixNorm) &&
                    HasEstablishedExactRightCompound(tokens, startIndex + windowLen - 1, exactMultiWordBits, localOffset + windowLen - 1))
                {
                    blockedByLongerOrRightCompound = true;
                }

                if (!blockedByLongerOrRightCompound)
                {
                    string asatMerged = UnicodeNormalizer.NormalizeCanonicalOrder(prefixNorm + finalCons + "\u103A" + trailingTone);
                    if (_trie.TryGetExactEntries(asatMerged, out var mergedEntries) && mergedEntries != null && mergedEntries.Count > 0)
                    {
                        int maxMergedFreq = 0;
                        bool anyOfficialMerged = false;
                        for (int mIdx = 0; mIdx < mergedEntries.Count; mIdx++)
                        {
                            if (mergedEntries[mIdx].Frequency > maxMergedFreq)
                                maxMergedFreq = mergedEntries[mIdx].Frequency;
                            if (mergedEntries[mIdx].IsOfficialThatpon)
                                anyOfficialMerged = true;
                        }
                        int minReqFreq = windowLen == 2 ? 80 : (finalCons == '\u1000' ? 25 : 15);
                        if (maxMergedFreq >= minReqFreq && (finalCons != '\u1000' || anyOfficialMerged))
                        {
                            string chosenTarget = asatMerged;
                            if (StyleMode == ProofingStyleMode.FormalOfficial)
                            {
                                if (chosenTarget == "တယ်") chosenTarget = "သည်";
                                else if (chosenTarget == "မယ်") chosenTarget = "မည်";
                            }

                            var capSpan = GetTokenSpan(fullText, tokens, startIndex, windowLen);
                            string capTarget = chosenTarget;
                            var capFirstMerged = mergedEntries[0];
                            return LatticeEdge.DeferredIssue(
                                windowLen,
                                0.85 + 0.10 * windowLen,
                                capSpan.RawText,
                                () =>
                                {
                                    var suggestions = new List<SuggestionCandidate>(2);
                                    EnsureTopSuggestionMetadata(suggestions, capSpan.RawText, capTarget, capFirstMerged.MeaningCue, capFirstMerged.PosTag, capFirstMerged.IsOfficialThatpon);
                                    return new ProofingIssue
                                    {
                                        StartIndex = capSpan.StartIndex,
                                        Length = capSpan.Length,
                                        OriginalText = capSpan.RawText,
                                        IssueType = ProofingIssueType.SpellingError,
                                        Message = $"အသတ် ('်') ကျန်ခဲ့ပါသည် — '{capSpan.RawText}' အစား '{capTarget}' ဟု ရေးသားရမည်။",
                                        Suggestions = suggestions
                                    };
                                });
                        }
                    }
                }
            }

            // Tier 1c: Dropped-Second-'အ' Symmetric Compound Repair (e.g. 'အခက်ခဲ' -> 'အခက်အခဲ', 'အသိမှတ်' -> 'အသိအမှတ်', 'အကောင်ထည်' -> 'အကောင်အထည်')
            // Exempts superlative adjectives 'အ + [2-syl] + ဆုံး' (e.g. 'အခက်ခဲဆုံး', 'အသေချာဆုံး', 'အပြည့်စုံဆုံး')
            if (windowLen == 3 && firstSyl == "အ" &&
                !(startIndex + 3 < runEnd && tokens[startIndex + 3].NormalizedText == "ဆုံး"))
            {
                string sylMid = tokens[startIndex + 1].NormalizedText;
                if (!sylMid.StartsWith("အ", StringComparison.Ordinal) &&
                    !lastSyl.StartsWith("အ", StringComparison.Ordinal) &&
                    !TrailingBoundaryParticles.Contains(lastSyl) &&
                    !VerbalContextFollowers.Contains(lastSyl))
                {
                    string symmetricCand = UnicodeNormalizer.NormalizeCanonicalOrder("အ" + sylMid + "အ" + lastSyl);
                    if (_trie.TryGetExactEntries(symmetricCand, out var symEntries) &&
                        symEntries != null && symEntries.Count > 0)
                    {
                        int maxSymFreq = 0;
                        bool anySymOfficial = false;
                        for (int sIdx = 0; sIdx < symEntries.Count; sIdx++)
                        {
                            if (symEntries[sIdx].Frequency > maxSymFreq) maxSymFreq = symEntries[sIdx].Frequency;
                            if (symEntries[sIdx].IsOfficialThatpon) anySymOfficial = true;
                        }
                        if (maxSymFreq >= (anySymOfficial ? 10 : 25))
                        {
                            var capSpan = GetTokenSpan(fullText, tokens, startIndex, windowLen);
                            string capSym = symmetricCand;
                            var capFirstSym = symEntries[0];
                            return LatticeEdge.DeferredIssue(
                                3,
                                1.08,
                                capSpan.RawText,
                                () =>
                                {
                                    var suggestions = _suggestionEngine.GetSuggestions(capSym);
                                    EnsureTopSuggestionMetadata(suggestions, capSpan.RawText, capSym, capFirstSym.MeaningCue, capFirstSym.PosTag, true);
                                    return new ProofingIssue
                                    {
                                        StartIndex = capSpan.StartIndex,
                                        Length = capSpan.Length,
                                        OriginalText = capSpan.RawText,
                                        IssueType = ProofingIssueType.SpellingError,
                                        Message = $"ပေါင်းစပ်ပုဒ်အတွင်း 'အ' ကျန်ခဲ့ပါသည် — '{capSpan.RawText}' အစား '{capSym}' ဟု ရေးသားရမည်။",
                                        Suggestions = suggestions
                                    };
                                });
                        }
                    }
                }
            }

            // Tier 1d: OOV Syllable + Valid Partner Forming an Official Thatpon Compound via Dropped Tone ('ခန်မှန်း -> ခန့်မှန်း') or Coda Slip ('ဆောင်ရွက်ချတ် -> ဆောင်ရွက်ချက်')
            if (windowLen >= 2 && windowLen <= 4 &&
                !IsProtectedSyntacticBoundarySlice(tokens, runStart, runEnd, localOffset, windowLen, exactMultiWordBits))
            {
                string? oovCompoundTarget = null;
                DictionaryEntry? oovCompoundEntry = null;

                if (windowLen == 2 &&
                    !_trie.IsStandaloneWord(firstSyl) &&
                    firstSyl.Length >= 2 &&
                    firstSyl.IndexOf('\u1037') < 0 &&
                    firstSyl.IndexOf('\u1038') < 0 &&
                    _trie.IsStandaloneWord(lastSyl))
                {
                    string candCreaky = UnicodeNormalizer.NormalizeCanonicalOrder(firstSyl + "\u1037" + lastSyl);
                    if (_trie.TryGetExactEntries(candCreaky, out var ceList) && ceList != null)
                    {
                        for (int ci = 0; ci < ceList.Count; ci++)
                        {
                            if (ceList[ci].IsOfficialThatpon && ceList[ci].Frequency >= 20)
                            {
                                oovCompoundTarget = candCreaky;
                                oovCompoundEntry = ceList[ci];
                                break;
                            }
                        }
                    }
                }

                if (oovCompoundTarget == null &&
                    !_trie.IsStandaloneWord(lastSyl) &&
                    lastSyl.Length >= 2 &&
                    lastSyl[lastSyl.Length - 1] == '\u103A')
                {
                    char cEnd = lastSyl[lastSyl.Length - 2];
                    string? swappedLast = cEnd switch
                    {
                        '\u1010' => lastSyl.Substring(0, lastSyl.Length - 2) + "\u1000\u103A",
                        '\u1000' => lastSyl.Substring(0, lastSyl.Length - 2) + "\u1010\u103A",
                        '\u1012' or '\u1013' => lastSyl.Substring(0, lastSyl.Length - 2) + "\u1010\u103A",
                        '\u1017' or '\u1018' => lastSyl.Substring(0, lastSyl.Length - 2) + "\u1015\u103A",
                        '\u1002' or '\u1003' => lastSyl.Substring(0, lastSyl.Length - 2) + "\u1000\u103A",
                        _ => null
                    };
                    if (swappedLast != null)
                    {
                        int minCodaFreq = (cEnd == '\u1010' || cEnd == '\u1000') ? 20 : 12;
                        string prefixPart = ConcatNormalizedTokens(tokens, startIndex, windowLen - 1);
                        string candCoda = UnicodeNormalizer.NormalizeCanonicalOrder(prefixPart + swappedLast);
                        if (_trie.TryGetExactEntries(candCoda, out var ccList) && ccList != null)
                        {
                            for (int ci = 0; ci < ccList.Count; ci++)
                            {
                                if (ccList[ci].IsOfficialThatpon && ccList[ci].Frequency >= minCodaFreq)
                                {
                                    oovCompoundTarget = candCoda;
                                    oovCompoundEntry = ccList[ci];
                                    break;
                                }
                            }
                        }
                    }
                }

                // Truncated Pali pat-sint tail missing final '-န်' (e.g. 'သတိပဋ္ဌာ -> သတိပဋ္ဌာန်')
                if (oovCompoundTarget == null &&
                    windowLen >= 3 &&
                    !_trie.IsStandaloneWord(lastSyl) &&
                    HasNonKinziPatSint(lastSyl) &&
                    lastSyl.IndexOf('\u103A') < 0)
                {
                    string candPaliNasal = UnicodeNormalizer.NormalizeCanonicalOrder(
                        ConcatNormalizedTokens(tokens, startIndex, windowLen) + "\u1014\u103A");
                    if (_trie.TryGetExactEntries(candPaliNasal, out var pnList) && pnList != null)
                    {
                        for (int pi = 0; pi < pnList.Count; pi++)
                        {
                            if (pnList[pi].IsOfficialThatpon && pnList[pi].Frequency >= 15)
                            {
                                oovCompoundTarget = candPaliNasal;
                                oovCompoundEntry = pnList[pi];
                                break;
                            }
                        }
                    }
                }

                if (oovCompoundTarget != null && oovCompoundEntry != null)
                {
                    var capSpan1d = GetTokenSpan(fullText, tokens, startIndex, windowLen);
                    string capTarget1d = oovCompoundTarget;
                    var capEntry1d = oovCompoundEntry;
                    return LatticeEdge.DeferredIssue(
                        windowLen,
                        0.86 + 0.10 * windowLen,
                        capSpan1d.RawText,
                        () =>
                        {
                            var suggestions = _suggestionEngine.GetSuggestions(capTarget1d);
                            EnsureTopSuggestionMetadata(suggestions, capSpan1d.RawText, capTarget1d, capEntry1d.MeaningCue, capEntry1d.PosTag, true);
                            return new ProofingIssue
                            {
                                StartIndex = capSpan1d.StartIndex,
                                Length = capSpan1d.Length,
                                OriginalText = capSpan1d.RawText,
                                IssueType = ProofingIssueType.SpellingError,
                                Message = $"ပေါင်းစပ်ပုဒ် စာလုံးပေါင်းအမှန် — '{capSpan1d.RawText}' အစား '{capTarget1d}' ဟု ရေးသားရမည်။",
                                Suggestions = suggestions
                            };
                        });
                }

                // Tier 1e: Unstacked Pali/loanword Pat-Sint Compound Folding (e.g. 'အန်ထရာယ် -> အန္တရာယ်', 'စက်ကန့် -> စက္ကန့်')
                // Never fold a standalone noun + trailing case particle ('စက်က' = machine + subj 'က' != Pali 'စက္က')!
                if (!(windowLen == 2 && TrailingBoundaryParticles.Contains(lastSyl) && _trie.IsStandaloneWord(firstSyl)))
                {
                    string spanNorm1e = ConcatNormalizedTokens(tokens, startIndex, windowLen);
                    if (SuggestionEngine.TryFoldUnstackedPatSint(spanNorm1e, _trie, out string foldedTarget1e, out var foldedEntry1e) &&
                        foldedEntry1e != null)
                    {
                        var capSpan1e = GetTokenSpan(fullText, tokens, startIndex, windowLen);
                        string capFolded = foldedTarget1e;
                        var capFoldEntry = foldedEntry1e;
                        return LatticeEdge.DeferredIssue(
                            windowLen,
                            0.62 + 0.06 * windowLen,
                            capSpan1e.RawText,
                            () =>
                            {
                                var suggestions = _suggestionEngine.GetSuggestions(capSpan1e.NormalizedText);
                                EnsureTopSuggestionMetadata(suggestions, capSpan1e.RawText, capFolded, capFoldEntry.MeaningCue, capFoldEntry.PosTag, capFoldEntry.IsOfficialThatpon);
                                return new ProofingIssue
                                {
                                    StartIndex = capSpan1e.StartIndex,
                                    Length = capSpan1e.Length,
                                    OriginalText = capSpan1e.RawText,
                                    IssueType = ProofingIssueType.SpellingError,
                                    Message = $"ပါဋ္ဌ်ဆင့် စာလုံးပေါင်းအမှန် — '{capSpan1e.RawText}' အစား '{capFolded}' ဟု ဆင့်ရေးရမည်။",
                                    Suggestions = suggestions
                                };
                            });
                    }
                }

                if (windowLen >= 3 && HasExactSpan(exactMultiWordBits, localOffset + windowLen - 1, 1))
                {
                    string prefNorm1e = ConcatNormalizedTokens(tokens, startIndex, windowLen - 1);
                    if (SuggestionEngine.TryFoldUnstackedPatSint(prefNorm1e, _trie, out _, out _))
                    {
                        // Let the shorter exact pat-sint fold (e.g. 'စက်ကန့် -> စက္ကန့်') + clean trailing word ('ပိုင်း') win!
                        return null;
                    }
                }
            }

            // Do not merge colloquial contraction 'ဖစ်' + '-မှု' into 'ပြစ်မှု' (crime), passive 'အ-[V]-ခံရ' into 'အထင်ကရ',
            // productive distributive 'တစ်-[Root]-စီ' ('တစ်လှည့်စီ') into archaic 'တလှည့်စီ', productive superlative 'အ-[V/ADJ]-ဆုံး' ('အကြော့ဆုံး') into 'အချောဆုံး',
            // Pali pat-sint citation stems ('မောဂ်-၅၊ ၄၄၊ ပဉ္စိ။'), or historical Japanese/Sino loanword 'ဘွန်ထိုက်ချို'
            if ((windowLen == 2 && firstSyl == "ဖစ်" && lastSyl == "မှု") ||
                (windowLen == 4 && firstSyl == "အ" && tokens[startIndex + 2].NormalizedText == "ခံ" && lastSyl == "ရ") ||
                (windowLen == 3 && firstSyl == "တစ်" && lastSyl == "စီ" && _trie.IsStandaloneWord(tokens[startIndex + 1].NormalizedText)) ||
                (windowLen == 3 && firstSyl == "အ" && lastSyl == "ဆုံး" &&
                 !_trie.TryGetWrongFormRule(tokens[startIndex + 1].NormalizedText, out _) &&
                 (_trie.HasPosTag(tokens[startIndex + 1].NormalizedText, "ADJ") || _trie.HasPosTag(tokens[startIndex + 1].NormalizedText, "V"))) ||
                (firstSyl == "ဘွန်" && runEnd - runStart >= 3) ||
                IsPaliPatSintCitationStem(fullText, tokens[startIndex + windowLen - 1], lastSyl))
            {
                return null;
            }

            bool mustPreserveFirstSyl = false;
            if (LeadingBoundaryParticles.Contains(firstSyl))
            {
                bool allowLexicalHomographStart =
                    LexicalHomographLeadingRoots.Contains(firstSyl) &&
                    !ProductiveVerbalAuxiliaries.Contains(lastSyl) &&
                    lastSyl != "ပေါ့" && lastSyl != "မျိုး" && lastSyl != "သူ" && lastSyl != "စကား" && lastSyl != "ကြောင့်" &&
                    (firstSyl == "လော"
                        ? !(startIndex > runStart && tokens[startIndex - 1].NormalizedText == "သ")
                        : (startIndex == runStart ||
                           (firstSyl == "ကာ" && tokens[startIndex + 1].NormalizedText.StartsWith("ကွ", StringComparison.Ordinal))));
                if (!allowLexicalHomographStart)
                    return null;
                mustPreserveFirstSyl = true;
            }

            bool mustPreserveLastSyl = false;
            if (TrailingBoundaryParticles.Contains(lastSyl))
            {
                bool allowTemporalOrRedupLawEnd =
                    lastSyl == "လော" &&
                    windowLen >= 3 &&
                    (tokens[startIndex + windowLen - 2].NormalizedText == "တ" ||
                     tokens[startIndex + windowLen - 2].NormalizedText == "လော");
                if (!allowTemporalOrRedupLawEnd)
                    return null;
                mustPreserveLastSyl = true;
            }
            else if (lastSyl == "တာ")
            {
                // Preserve colloquial nominalizer '-တာ' / loanword '-တာ' (e.g. 'ကွန်ပြူတာ -> ကွန်ပျူတာ' stays valid,
                // while 'ဖွင်တာ' / 'သိမ်ဆည်းတာ' never mutates '-တာ' into '-ထား'!)
                mustPreserveLastSyl = true;
            }
            else if (lastSyl == "ချင်" && startIndex + windowLen < tokens.Count &&
                     tokens[startIndex + windowLen].Kind == TokenKind.MyanmarSyllable)
            {
                string afterChin = tokens[startIndex + windowLen].NormalizedText;
                if (afterChin == "ပါ" || afterChin == "တယ်" || afterChin == "သည်" || afterChin == "ဘူး" ||
                    afterChin == "လို့" || afterChin == "သေး" || afterChin == "တော့" || afterChin == "ကြ")
                {
                    // Desiderative '-ချင်' before verbal closer ('အကျံပေးချင်ပါတယ်') must never mutate into nominalizer '-ခြင်း'!
                    mustPreserveLastSyl = true;
                }
            }

            // Never backward-merge bare prefix 'အ' with the preceding word when 'အ' opens a following nominalization (e.g. 'ရေကြီးမှုကြီးအပြီး' must never turn 'ကြီးအ' into 'ကျီးအ')
            if (lastSyl == "အ" && startIndex + windowLen < runEnd)
            {
                return null;
            }

            // Never absorb 'သာ' from 'သာမက' ("not only") into the preceding syllable (e.g. 'တိုးပွားရေးသာမက' must never turn 'ရေးသာ' into 'ရေးသား')
            if (lastSyl == "သာ" && startIndex + windowLen < runEnd &&
                tokens[startIndex + windowLen].NormalizedText == "မ")
            {
                return null;
            }

            // Never convert obligative suffix '-ရ' + 'နေ့' (e.g. 'ကြွပ်ကြွပ်အိတ်မသုံးစွဲရနေ့') into 'ယနေ့'
            if (windowLen == 2 && firstSyl == "ရ" && lastSyl == "နေ့" && startIndex > runStart)
            {
                return null;
            }

            // Never mutate temporal verb 'ကျ' before conditional/temporal particles ('တစ်နေ့ကျရင်' != 'တစ်နေ့ကရင်', 'ညနေကျမှ', 'နောက်ကျတော့')
            if (lastSyl == "ကျ" && startIndex + windowLen < tokens.Count &&
                tokens[startIndex + windowLen].Kind == TokenKind.MyanmarSyllable)
            {
                string afterKyaTemporal = tokens[startIndex + windowLen].NormalizedText;
                if (afterKyaTemporal == "ရင်" || afterKyaTemporal == "လျှင်" ||
                    afterKyaTemporal == "တော့" || afterKyaTemporal == "မှ")
                {
                    return null;
                }
            }

            // Culinary seed/plant oil compounds ('နှမ်းဆီ' = sesame oil != 'နှင်းဆီ' rose, 'ပဲဆီ', 'ပြောင်းဆီ', 'အုန်းဆီ'),
            // inverted symmetric literary idiom 'အယွင်းအတိမ်း' ("without deviation" != 'အတိုင်း'),
            // Mon-Burmese pagoda title 'ရွှေမုဒေါဘုရား / ရွှေမုဋောဘုရား' (!= 'ရွှေမုဓော'),
            // and accepted modern IT transliterations without optional cluster killer ('အယ်ဂိုရီသမ်', 'ဘလော့ချိန်း')
            if ((windowLen == 2 && lastSyl == "ဆီ" &&
                 (firstSyl == "နှမ်း" || firstSyl == "ပဲ" || firstSyl == "ပြောင်း" || firstSyl == "အုန်း")) ||
                (windowLen >= 2 && firstSyl == "အ" && tokens[startIndex + 1].NormalizedText == "တိမ်း" &&
                 startIndex >= runStart + 2 && tokens[startIndex - 2].NormalizedText == "အ" && tokens[startIndex - 1].NormalizedText == "ယွင်း") ||
                (windowLen >= 3 && firstSyl == "ရွှေ" && tokens[startIndex + 1].NormalizedText == "မု" &&
                 (tokens[startIndex + 2].NormalizedText == "ဒေါ" || tokens[startIndex + 2].NormalizedText == "ဋော")) ||
                (windowLen >= 3 && firstSyl == "အယ်" && tokens[startIndex + 1].NormalizedText == "ဂို") ||
                (windowLen >= 3 && firstSyl == "ဘ" && tokens[startIndex + 1].NormalizedText == "လော့" && lastSyl == "ချိန်း"))
            {
                return null;
            }

            if (IsProtectedSyntacticBoundarySlice(tokens, runStart, runEnd, localOffset, windowLen, exactMultiWordBits))
            {
                return null;
            }

            // Immediacy construction '[V] + လျှင် + [V] + ချင်း' (e.g. 'သတင်းရလျှင်ရချင်း') uses 'ချင်း', never 'ခြင်း'
            if (windowLen == 2 && lastSyl == "ချင်း" && startIndex >= runStart + 2 &&
                (tokens[startIndex - 1].NormalizedText == "လျှင်" || tokens[startIndex - 1].NormalizedText == "ရင်") &&
                tokens[startIndex - 2].NormalizedText == firstSyl)
            {
                return null;
            }

            // Nominalizer '-မှု' closing a preceding verb phrase (e.g. 'ရင်းနှီးမြှုပ်နှံရမှုသက်သာစေရန်') must never mutate into negative prefix 'မ-'
            if (firstSyl == "မှု" && startIndex > runStart)
            {
                return null;
            }

            // Composite topic-marker guard: never absorb '~ကတော့ / ~ကလည်း / ~ကသာ / ~ကပဲ / ~ကပင်' into a repaired noun (e.g. 'မင်းကတော့ ဆရာပဲ' must never become 'မင်းကတော်')
            if (windowLen >= 3 &&
                tokens[startIndex + windowLen - 2].NormalizedText == "က" &&
                (lastSyl == "တော့" || lastSyl == "လည်း" || lastSyl == "သာ" || lastSyl == "ပဲ" || lastSyl == "ပင်" || lastSyl == "တောင်"))
            {
                return null;
            }

            // Comparative particle 'ထက်' after classifier, ablative 'က/မှ', or valid standalone word (e.g. '(၆)လထက်', 'မူလကထက်', 'ယခင်ကထက်')
            // Exempts '[PureVerb] + ထက် + [နေ/လာ]' verbal aspect slips (e.g. 'ပေါ်ထက်နေတယ် -> ပေါ်ထွက်နေတယ်')!
            if (lastSyl == "ထက်")
            {
                bool isVerbalAspectAfterHtet =
                    windowLen == 2 &&
                    IsPureVerbSyllable(firstSyl) &&
                    startIndex + 2 < tokens.Count &&
                    tokens[startIndex + 2].Kind == TokenKind.MyanmarSyllable &&
                    (tokens[startIndex + 2].NormalizedText == "နေ" || tokens[startIndex + 2].NormalizedText == "လာ");

                if (!isVerbalAspectAfterHtet &&
                    windowLen == 2 &&
                    (NumeralClassifiers.Contains(firstSyl) ||
                     firstSyl == "က" ||
                     firstSyl == "မှ" ||
                     (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 5)))
                {
                    return null;
                }

                if (windowLen >= 3 &&
                    (tokens[startIndex + windowLen - 2].NormalizedText == "က" ||
                     tokens[startIndex + windowLen - 2].NormalizedText == "မှ"))
                {
                    return null;
                }
            }

            // Do not merge plural verb marker 'ကြ' when followed by a clause-final verb particle (e.g. 'စကားပြောနေကြသည်')
            if (lastSyl == "ကြ" && startIndex + windowLen < tokens.Count &&
                tokens[startIndex + windowLen].Kind == TokenKind.MyanmarSyllable &&
                PluralVerbClosers.Contains(tokens[startIndex + windowLen].NormalizedText))
            {
                return null;
            }

            // Do not slice plural verb marker 'ကြ' + post-verbal modal/particle chain after a preceding verb (e.g. 'လိုက်နာဆောင်ရွက်ကြရပါမည်' != 'ကြံရာပါ')
            if (firstSyl == "ကြ" && startIndex > runStart)
            {
                string afterKya = tokens[startIndex + 1].NormalizedText;
                if (PluralVerbClosers.Contains(afterKya) || ProductiveVerbalAuxiliaries.Contains(afterKya))
                {
                    return null;
                }
            }

            // Constituent Boundary Guard 1: Do not detach a Numeral Classifier from its preceding Numeral (e.g. 'လေးယောက်' + 'လာ' must not merge 'ယောက်လာ -> ရောက်လာ')
            if (startIndex > 0 &&
                tokens[startIndex - 1].Kind == TokenKind.MyanmarSyllable &&
                PureNumerals.Contains(tokens[startIndex - 1].NormalizedText) &&
                NumeralClassifiers.Contains(firstSyl))
            {
                return null;
            }

            // Constituent Boundary Guard 2: Do not fuse negative prefix 'မ' with its verb or preceding word
            // (covers colloquial 'မ...ဘူး', symmetric 4-syllable 'မနီးမဝေး / မပျက်မစီး', '[Word + မ] + Verb' like 'ငတ်မရှက် / ဂေါ်မစင်',
            // and formal literary / proverbial bare negation 'မလှုပ် / မစားရ / မရှား / ပြင်မထွက်')
            if (firstSyl == "မ" || lastSyl == "မ")
            {
                bool isBotanicalCompoundHead =
                    firstSyl == "မ" && lastSyl != "မ" && windowLen >= 3 &&
                    (lastSyl == "သီး" || lastSyl == "ပင်" || lastSyl == "ရွက်" || lastSyl == "\u1015\u103D\u1004\u103A\u1037" || lastSyl == "\u100A\u103D\u1014\u103A\u1037");

                if (!isBotanicalCompoundHead)
                {
                    int negPos = firstSyl == "မ" ? startIndex : (startIndex + windowLen - 1);

                    // 2a. Symmetric 4-syllable 'မ-A-မ-B' negative adverbial idiom (e.g. မနီးမဝေး, မပျက်မစီး, မသာမယာ, မတန်မရာ, မဖောက်မပြန်)
                    if ((negPos + 2 < tokens.Count && tokens[negPos + 1].Kind == TokenKind.MyanmarSyllable && tokens[negPos + 2].Kind == TokenKind.MyanmarSyllable && tokens[negPos + 2].NormalizedText == "မ") ||
                        (negPos >= 2 && tokens[negPos - 1].Kind == TokenKind.MyanmarSyllable && tokens[negPos - 2].Kind == TokenKind.MyanmarSyllable && tokens[negPos - 2].NormalizedText == "မ"))
                    {
                        return null;
                    }

                    // 2b. Trailing 'မ' belongs to the following negative verb (e.g. 'သော် + မမြဲ', 'ငတ် + မရှက်', 'ဂေါ် + မစင်', 'မျက်နှာထက် + မများ')
                    if (lastSyl == "မ" && startIndex + windowLen < tokens.Count && tokens[startIndex + windowLen].Kind == TokenKind.MyanmarSyllable)
                    {
                        string afterMa = tokens[startIndex + windowLen].NormalizedText;
                        if ((!LeadingBoundaryParticles.Contains(afterMa) || afterMa == "များ" || afterMa == "နည်း") &&
                            !BoundSyllableVerbFrameMap.ContainsKey(afterMa) &&
                            _trie.IsStandaloneWord(afterMa) &&
                            _trie.GetWordFrequency(afterMa) >= 1)
                        {
                            return null;
                        }
                    }

                    // 2c. Formal literary & proverbial bare negation 'မ + [Valid Verb (+ ရ/နိုင်/ရာ)]' without colloquial 'ဘူး'
                    if (firstSyl == "မ" && (windowLen == 2 || (windowLen == 3 && (lastSyl == "ရ" || lastSyl == "နိုင်" || lastSyl == "ရာ"))))
                    {
                        string verbStem = tokens[startIndex + 1].NormalizedText;
                        if ((verbStem.Length >= 2 || verbStem == "ထ") &&
                            verbStem != "နက်" &&
                            !BoundSyllableVerbFrameMap.ContainsKey(verbStem) &&
                            _trie.IsStandaloneWord(verbStem) &&
                            _trie.GetWordFrequency(verbStem) >= 2)
                        {
                            return null;
                        }
                    }

                    int lookLimit = Math.Min(tokens.Count, negPos + 6);
                    for (int k = negPos + 1; k < lookLimit; k++)
                    {
                        if (tokens[k].Kind != TokenKind.MyanmarSyllable)
                            break;
                        if (tokens[k].NormalizedText == "မ")
                            break; // Subsequent 'မ' opens a separate negative verb phrase (e.g. 'မလကာ မစားဘူး')
                        if (NegativeClauseClosers.Contains(tokens[k].NormalizedText))
                            return null;
                    }
                }
            }

            // Constituent Boundary Guard 2d: Do not absorb 'သ' or 'ပေ' from literary/interrogative verb particles ('သကဲ့သို့', 'သလော', 'သနည်း', 'သလား', 'သလဲ', 'သတည်း', 'ပေတည်း')
            // into the preceding word (e.g. 'နင်းမိသကဲ့သို့' != 'မိဿ', 'သိသလော' != 'သိဿ', 'အကျိုးပေတည်း' != 'အကျိုးပေး')
            if ((lastSyl == "သ" || lastSyl == "ပေ") && startIndex + windowLen < tokens.Count &&
                tokens[startIndex + windowLen].Kind == TokenKind.MyanmarSyllable)
            {
                string afterTha = tokens[startIndex + windowLen].NormalizedText;
                if ((lastSyl == "သ" && (afterTha == "ကဲ့" || afterTha == "လော" || afterTha == "နည်း" ||
                                        afterTha == "လား" || afterTha == "လဲ" || afterTha == "တည်း")) ||
                    (lastSyl == "ပေ" && afterTha == "တည်း"))
                {
                    return null;
                }
            }

            // Constituent Boundary Guard 2e: Productive 'အ + [Valid Root]' nominalizations (e.g. 'အရွက်' [leaf], 'အနှေး', 'အနွေး')
            // and '[Noun] + မ + [Valid Verb]' clauses (e.g. 'ဂေါ်မစင်' = ဂေါ် + မစင်) must never fuzzy-mutate into non-homophones ('အရက်', 'ဂေါ်မစွံ')
            bool isProductiveANominalization = (windowLen == 2 &&
                                                firstSyl == "အ" &&
                                                lastSyl.Length >= 2 &&
                                                !BoundSyllableVerbFrameMap.ContainsKey(lastSyl) &&
                                                _trie.IsStandaloneWord(lastSyl) &&
                                                _trie.GetWordFrequency(lastSyl) >= 2) ||
                                               (windowLen == 3 &&
                                                tokens[startIndex + 1].NormalizedText == "မ" &&
                                                lastSyl.Length >= 2 &&
                                                !BoundSyllableVerbFrameMap.ContainsKey(lastSyl) &&
                                                _trie.IsStandaloneWord(firstSyl) &&
                                                _trie.IsStandaloneWord(lastSyl) &&
                                                _trie.GetWordFrequency(lastSyl) >= 2);

            // Constituent Boundary Guard 3: Do not merge Demonstrative/Pronoun/Numeral/Comparative/Conjunction + Valid Noun/Verb/Classifier
            // (e.g. 'ဒီနေရာ' must never become 'တည်နေရာ', 'ပိုအရေးကြီး' must never become 'ဖို့အရေးကြီး', 'တွေ့ဆုံပြီးမြို့နယ်' must never become 'ပြည်မြို့နယ်', 'ဖြစ်ပါကြောင်းဖြေကြား' must never become 'ကြောင်းပြောကြား')
            if ((NumeralsAndDemonstratives.Contains(firstSyl) ||
                 (firstSyl == "ပို" && windowLen >= 3) ||
                 ((firstSyl == "ပြီး" || firstSyl == "ကြောင်း") && startIndex > runStart)) &&
                windowLen >= 2)
            {
                string remNorm = ConcatNormalizedTokens(tokens, startIndex + 1, windowLen - 1);
                if (_trie.ContainsExactWord(remNorm) || NumeralClassifiers.Contains(remNorm))
                    return null;
            }

            // Constituent Boundary Guard 4: Do not merge Standalone Word + Productive Verbal Auxiliary / Suffix into an unrelated noun (e.g. 'ပိုပေး -> ပို့ပေး', 'ရက်ရှိ -> ရရှိ', 'သားဖြစ် -> သာဖြစ်', 'ကြီးပင် -> ကျည်းပင်')
            if (windowLen == 2 &&
                ProductiveVerbalAuxiliaries.Contains(lastSyl) &&
                _trie.IsStandaloneWord(firstSyl) &&
                _trie.GetWordFrequency(firstSyl) >= 5)
            {
                return null;
            }

            // Guard '[Standalone Noun] + ကျ + (သော|သည့်|တဲ့|ရာ)' (e.g. 'ထောင့်ကျသော နေရာတွင်' must never strip creaky tone into 'ထောင်ကျ'!)
            // and Shan/Kayah 'လွိုင် + [Syllable]' place names ('လွိုင်နမ်းဖ' != 'လှိုင်')
            if (firstSyl == "လွိုင်" ||
                (windowLen == 2 &&
                 lastSyl == "ကျ" &&
                 firstSyl.IndexOf('\u1037') >= 0 &&
                 _trie.IsStandaloneWord(firstSyl) &&
                 startIndex + 2 < tokens.Count &&
                 tokens[startIndex + 2].Kind == TokenKind.MyanmarSyllable &&
                 (tokens[startIndex + 2].NormalizedText == "သော" ||
                  tokens[startIndex + 2].NormalizedText == "သည့်" ||
                  tokens[startIndex + 2].NormalizedText == "တဲ့" ||
                  tokens[startIndex + 2].NormalizedText == "ရာ")))
            {
                return null;
            }

            // Personal name shield after female honorific 'ဒေါ်' (e.g. 'ဒေါ်တင်တင်အုန်း' != 'တင်ဦး', excluding 'ဒေါ်လာ')
            if (windowLen == 2 &&
                startIndex > runStart &&
                runEnd - runStart >= 3 &&
                tokens[runStart].NormalizedText == "ဒေါ်" &&
                _trie.ContainsExactWord(firstSyl) &&
                _trie.ContainsExactWord(lastSyl))
            {
                return null;
            }

            // Never start a compound repair window mid-Kinzi ('င်္' \u1004\u103A\u1039 belongs phonologically to the preceding open syllable, e.g. 'မဟာဥမင်္ဂဇာတ်', 'ဒီပင်္ကရာ')
            if (startIndex > runStart && firstSyl.StartsWith("\u1004\u103A\u1039", StringComparison.Ordinal))
            {
                return null;
            }

            // Cross-Compound Exact Multi-Word Seam Shield & Medical Transliteration Guard ('ပါကင်ဆန်ရောဂါ' != 'ကင်ဆာရောဂါ'):
            // Never slice across the internal boundary of two contiguous exact >=2-syllable dictionary compounds,
            // and never slice 'ကင်ဆန်...' out of 'ပါကင်ဆန်...' (Parkinson's disease) into 'ကင်ဆာ...' (Cancer)!
            if (localOffset >= 1 && firstSyl == "ကင်" &&
                tokens[startIndex - 1].NormalizedText == "ပါ" &&
                windowLen >= 2 && tokens[startIndex + 1].NormalizedText == "ဆန်")
            {
                return null;
            }
            if (localOffset >= 1 && windowLen >= 3)
            {
                for (int splitK = 1; splitK < windowLen; splitK++)
                {
                    int rightLen = windowLen - splitK;
                    if (rightLen >= 2 &&
                        IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset + splitK - 1, splitK + 1) &&
                        HasExactSpan(exactMultiWordBits, localOffset + splitK, rightLen))
                    {
                        return null;
                    }
                }
            }

            // Colloquial ablative postposition '[Noun/Classifier] + ကနေ' (e.g. 'တခုကနေ', 'မြို့ကနေ' != 'ခုနက')
            if (windowLen >= 3 &&
                tokens[startIndex + windowLen - 2].NormalizedText == "က" &&
                lastSyl == "နေ")
            {
                return null;
            }

            // Foreign killed-coda loanword boundary shield (e.g. 'AI ချစ်ပ်သူဌေး' != 'ချစ်သူ', 'ရန်ဘီးရ်' != 'ဘီးလင်း', 'ကပူးရ်' != 'ပေးရ')
            if ((windowLen == 2 && (IsForeignOrPaliKilledCoda(firstSyl) || IsForeignOrPaliKilledCoda(lastSyl))) ||
                (windowLen >= 2 && IsForeignOrPaliKilledCoda(firstSyl) && IsStartOfAnyExactMultiWord(exactMultiWordBits, localOffset + 1, runEnd - runStart)))
            {
                return null;
            }

            // Constituent Boundary Guard 4b: Productive literary & classical constructions ('[Verb] + တော် + မူ', 'အ + [Verb] + ခံ', '[Root] + တရား', '[Root] + ခနဲ')
            if (windowLen == 3)
            {
                string midSyl = tokens[startIndex + 1].NormalizedText;
                // Productive gerundive '[PureVerb / Auxiliary] + စရာ' (e.g. 'ပြေးစရာမြေ', 'စားစရာ', 'ကြောက်စရာ' != 'ပြောစရာ'; explicit Step 1 rules like 'ကျောင်းစရာ->ကျောင်းဆရာ' already fired!)
                if (midSyl == "စ" && lastSyl == "ရာ" &&
                    (IsPureVerbSyllable(firstSyl) || ProductiveVerbalAuxiliaries.Contains(firstSyl)))
                {
                    return null;
                }

                // Productive potential-negative '[Verb / StandaloneWord] + မရ' (e.g. 'အိပ်မရ', 'စားမရ', 'သွားမရ' != 'အိပ်မက်ရ')
                if (midSyl == "မ" && lastSyl == "ရ" &&
                    (IsPureVerbSyllable(firstSyl) || (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2)))
                {
                    return null;
                }

                // Cross-compound L=3 tail-slicing shield: never slice the last 2 syllables of a preceding >=3-syllable exact compound + 1-syllable standalone noun or post-nominal adjective
                // (e.g. 'သွင်းအားစုဈေး' != 'အစုဈေး', 'ပန်းပုဆရာကောင်း' != 'စရာကောင်း')
                if (localOffset >= 1 &&
                    IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset + 1, 3) &&
                    (PostNominalAdjectives.Contains(lastSyl) ||
                     (_trie.IsStandaloneWord(lastSyl) && _trie.GetWordFrequency(lastSyl) >= 10)))
                {
                    return null;
                }

                // Cross-compound L=3 head-slicing shield: '[Classifier / EndOfExactMultiWord] + [Exact 2-Syl Official/HighFreq Word]'
                // (e.g. 'မြန်မာပိုင်းသတင်း' != 'ပိုးသီတင်း', 'ပိုးဝင်ရောဂါ' != 'ဝမ်းရောဂါ', '၆ လနီးပါး' != 'လုနီးပါး', 'ဒေသနာတော်အဆုံး' != 'အတော်ဆုံး')
                if (HasExactSpan(exactMultiWordBits, localOffset + 1, 2) &&
                    firstSyl != "အ" && firstSyl != "မ" && firstSyl != "တ" &&
                    (NumeralClassifiers.Contains(firstSyl) ||
                     (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2))))
                {
                    string right2Norm = ConcatNormalizedTokens(tokens, startIndex + 1, 2);
                    if (_trie.TryGetExactEntries(right2Norm, out var r2Entries) && r2Entries != null)
                    {
                        for (int rIdx = 0; rIdx < r2Entries.Count; rIdx++)
                        {
                            var re = r2Entries[rIdx];
                            if ((re.IsOfficialThatpon && re.Frequency >= 8) || re.Frequency >= 35)
                                return null;
                        }
                    }
                }
                if (midSyl == "တော်" && lastSyl == "မူ" &&
                    !BoundSyllableVerbFrameMap.ContainsKey(firstSyl) &&
                    _trie.IsStandaloneWord(firstSyl) &&
                    _trie.GetWordFrequency(firstSyl) >= 2)
                {
                    return null;
                }

                if (firstSyl == "အ" && lastSyl == "ခံ" &&
                    !BoundSyllableVerbFrameMap.ContainsKey(midSyl) &&
                    _trie.IsStandaloneWord(midSyl) &&
                    _trie.GetWordFrequency(midSyl) >= 2)
                {
                    return null;
                }

                if (midSyl == "တ" && lastSyl == "ရား" &&
                    !BoundSyllableVerbFrameMap.ContainsKey(firstSyl) &&
                    _trie.IsStandaloneWord(firstSyl) &&
                    _trie.GetWordFrequency(firstSyl) >= 2)
                {
                    return null;
                }

                if (midSyl == "ခ" && lastSyl == "နဲ" &&
                    !BoundSyllableVerbFrameMap.ContainsKey(firstSyl) &&
                    _trie.IsStandaloneWord(firstSyl) &&
                    _trie.GetWordFrequency(firstSyl) >= 2)
                {
                    return null;
                }

                // 2-syllable postpositions '+ အတွက် / အထိ / အမီ / အနက် / အတွင်း / အပေါ် / အကြား / အလိုက် / အတိုင်း' after a valid classifier, noun, or multi-word compound
                if (midSyl == "အ" && TwoSyllablePostpositionSeconds.Contains(lastSyl) &&
                    (NumeralClassifiers.Contains(firstSyl) ||
                     firstSyl == "ချိန်" ||
                     IsForeignOrPaliKilledCoda(firstSyl) ||
                     (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2)) ||
                     (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2) ||
                     (UnicodeNormalizer.HasLookalikeErrorSlice(firstSyl, 0, firstSyl.Length, out string fixedFirstSyl, out _) && _trie.IsStandaloneWord(fixedFirstSyl))))
                {
                    return null;
                }

                // Polite government directive '-ပါရန်' & optative '-ပါစေ' after a valid verb/adjective (e.g. 'ပေးပို့ပါရန်', 'တာဝန်ကျေပါစေ', 'ပိုပြီးလှပါစေ')
                if (midSyl == "ပါ" && (lastSyl == "ရန်" || lastSyl == "စေ") &&
                    _trie.IsStandaloneWord(firstSyl) &&
                    _trie.GetWordFrequency(firstSyl) >= 2)
                {
                    return null;
                }

                // Concessive verb suffix '+ ပေမဲ့ / ပေမယ့်' after a valid verb or verbal auxiliary (e.g. 'ထားပေမဲ့' != 'ဒါပေမဲ့', 'ရှိပေမဲ့' != 'ဒါပေမဲ့')
                if (midSyl == "ပေ" && (lastSyl == "မဲ့" || lastSyl == "\u1019\u101A\u103A\u1037") &&
                    firstSyl != "ဒါ" && firstSyl != "သို့" && firstSyl != "ဒါ့" &&
                    (startIndex > runStart ||
                     ProductiveVerbalAuxiliaries.Contains(firstSyl) ||
                     (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2)))
                {
                    return null;
                }

                // Productive '[Verb/CompoundEnd] + [Auxiliary/Copula/PureVerb] + [Nominalizer/Modal]'
                // (e.g. 'စွမ်းဆောင်ရည်ရှိမှု' != 'ရရှိမှု', 'မြန်မာနိုင်ငံသားဖြစ်ရမည်' != 'သားဖြစ်လာ', 'ပုံပြင်ပြောခြင်း' != 'ပြန့်ပြောခြင်း')
                bool isEndOfMultiWordAtFirst = localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2);
                if (((midSyl == "နေ" || midSyl == "ပေး" || midSyl == "ထား" || midSyl == "ခဲ့" || midSyl == "ရ" ||
                      midSyl == "သွား" || midSyl == "လာ" || midSyl == "နိုင်" || midSyl == "တတ်" || midSyl == "စေ" ||
                      midSyl == "ပြီး" || midSyl == "ရှိ" || midSyl == "ဖြစ်" || midSyl == "လွန်း") &&
                     (isEndOfMultiWordAtFirst ||
                      (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2))) ||
                    (isEndOfMultiWordAtFirst && IsPureVerbSyllable(midSyl)))
                {
                    if (lastSyl == "မှု" || lastSyl == "ခြင်း" || lastSyl == "ချက်" || lastSyl == "ရေး" ||
                        lastSyl == "ရာ" || lastSyl == "သူ" || lastSyl == "ဖွယ်" || lastSyl == "နည်း" ||
                        (isEndOfMultiWordAtFirst && (lastSyl == "ရ" || lastSyl == "နိုင်" || lastSyl == "ပါ" || lastSyl == "မည်")))
                    {
                        return null;
                    }
                }

                // Negated inadvertent verb 'မ + [OfficialVerb] + မိ' (e.g. 'ခြေမကိုင်မိ လက်မကိုင်မိ' != 'မတိုင်မီ')
                if (firstSyl == "မ" && lastSyl == "မိ" && IsOfficialVerbOrAdjMonosyllable(midSyl))
                {
                    return null;
                }

                // Approximative particle 'ခန့်' after a numeral/classifier/quantifier + exact 2-syllable verb (e.g. 'ကျပ်ခန့်ရရှိ' != 'ခွင့်ရရှိ', '၆ သိန်း ကျော်ခန့်ရရှိရာ')
                if (firstSyl == NormalizedKhant && HasExactSpan(exactMultiWordBits, localOffset + 1, 2) &&
                    startIndex > runStart &&
                    (NumeralClassifiers.Contains(tokens[startIndex - 1].NormalizedText) ||
                     PureNumerals.Contains(tokens[startIndex - 1].NormalizedText) ||
                     tokens[startIndex - 1].NormalizedText == "ကျော်"))
                {
                    return null;
                }

                // Ward/quarter names '[Syllable] + ရပ်ကွက်' (e.g. 'ရန်အောင်မြေရပ်ကွက်' != 'မြေလပ်ကွက်', 'ဆ/ကရပ်ကွက်' != 'ကရကွက်')
                if (midSyl == "ရပ်" && lastSyl == "ကွက်")
                {
                    return null;
                }

                // Copular/role postposition '[Ordinal/Classifier/Noun] + အဖြစ်' (e.g. 'နဝမအကြိမ်မြောက်အဖြစ်' != 'နောက်အဖြစ်')
                if (midSyl == "အ" && lastSyl == "ဖြစ်" &&
                    (firstSyl == "မြောက်" ||
                     NumeralClassifiers.Contains(firstSyl) ||
                     (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset, 2)) ||
                     (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2)))
                {
                    return null;
                }

                // Interrogative 'ဘယ်သူ + [Verb]' (e.g. 'ဘယ်သူမှန်လဲ' != 'ဘယ်သူမှ')
                if (firstSyl == "ဘယ်" && midSyl == "သူ" && lastSyl != "မှ" &&
                    _trie.IsStandaloneWord(lastSyl) && _trie.GetWordFrequency(lastSyl) >= 2)
                {
                    return null;
                }

                // Municipal & medical 'ရေထိုးခြင်း / လေထိုးခြင်း / ဆေးထိုးခြင်း' ("injecting water/air/medicine", e.g. ycdc.gov.mm != 'ရေးထိုးခြင်း')
                if ((firstSyl == "ရေ" || firstSyl == "လေ" || firstSyl == "ဆေး") &&
                    midSyl == "ထိုး" &&
                    (lastSyl == "ခြင်း" || lastSyl == "မှု"))
                {
                    return null;
                }
            }

            // Transliterated foreign/Pali street names '[ForeignKilledCoda] + လမ်း' (e.g. 'ဘားနဒ်လမ်း' != 'နတ်လမ်း')
            if (windowLen == 2 && lastSyl == "လမ်း" && IsForeignOrPaliKilledCoda(firstSyl))
            {
                return null;
            }

            // Productive L=2 verbal nominalization '[OfficialVerb] + ခြင်း/မှု' (e.g. 'ပြေးခြင်း' != 'ပေးခြင်း', 'စဉ်ခြင်း' != 'စေခြင်း')
            // and productive reduplicated Official adjective/verb 'AA' (e.g. 'ပျစ်ပျစ်' != 'ပြစ်ပြစ်', 'ကျပ်ကျပ်' != 'ကျက်ကျက်')
            if (windowLen == 2 && firstSyl.Length >= 2 && !_trie.TryGetWrongFormRule(firstSyl, out _))
            {
                if ((lastSyl == "ခြင်း" || lastSyl == "မှု") && IsOfficialVerbOrAdjMonosyllable(firstSyl))
                    return null;

                if (firstSyl == lastSyl && IsOfficialVerbOrAdjMonosyllable(firstSyl))
                    return null;
            }

            // Ward/quarter names after an exact >=3-syllable proper noun (e.g. 'ရွှေဂုံတိုင်ရပ်ကွက်' must never slice 'ဂုံတိုင်ရပ်ကွက် -> ဂုံဘိုရပ်ကွက်')
            if (windowLen == 4 &&
                tokens[startIndex + 2].NormalizedText == "ရပ်" &&
                lastSyl == "ကွက်" &&
                localOffset >= 1 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordBits, localOffset + 1, 3))
            {
                return null;
            }

            // Multi-syllable exact noun/compound + 2-syllable postposition (e.g. 'နိုင်ငံ့အရေးအတွက်' != 'အရေအတွက်', 'တိုင်းဒေသကြီးအတွင်း')
            if (windowLen >= 4 &&
                tokens[startIndex + windowLen - 2].NormalizedText == "အ" &&
                TwoSyllablePostpositionSeconds.Contains(lastSyl) &&
                HasExactSpan(exactMultiWordBits, localOffset, windowLen - 2))
            {
                return null;
            }

            // Auxiliary + 'သောအခါ' temporal clause (e.g. 'ကြုံလာရသောအခါ' must never turn 'ရသောအခါ' into 'လာသောအခါ')
            if (windowLen == 4 &&
                tokens[startIndex + 1].NormalizedText == "သော" &&
                tokens[startIndex + 2].NormalizedText == "အ" &&
                lastSyl == "ခါ" &&
                ProductiveVerbalAuxiliaries.Contains(firstSyl))
            {
                return null;
            }

            // Guard [ExactWord (L>=3) + Valid Particle / Productive Suffix] (stops 'ပတ်သက်မှု -> ပတ်သက်မု', 'ရှားစောင်းကြီးပင် -> ကျည်းပင်', 'အသက်ရှည်ဆေး -> ရေဆေး', 'အကြောင်းကား -> အကြောင်းကြား', 'အမြတ်အစွန်းထက် -> အစွမ်းထက်')
            // while keeping L>=3 generative compound repairs like 'စီပွားရေး -> စီးပွားရေး' and 'အသိပေအကြောင်းကြားစာ -> အသိပေး' active!
            bool isEuphonicPeOrLe = lastSyl == "ပေ" || lastSyl == "လေ";
            bool euphonicAtLiteraryClose = false;
            if (isEuphonicPeOrLe)
            {
                int afterIdx = startIndex + windowLen;
                if (afterIdx >= tokens.Count)
                {
                    euphonicAtLiteraryClose = true;
                }
                else if (tokens[afterIdx].Kind == TokenKind.MyanmarSyllable)
                {
                    string nextSyl = tokens[afterIdx].NormalizedText;
                    euphonicAtLiteraryClose = nextSyl == "၏" || nextSyl == "သည်" || nextSyl == "တည်း" ||
                                              nextSyl == "\u101C\u102D\u1019\u103A\u1037" || nextSyl == "အံ့" || nextSyl == "မည်" ||
                                              nextSyl == "ရာ" || nextSyl == "သော်";
                }
                else if (tokens[afterIdx].Text.IndexOf('\u104B') >= 0 || tokens[afterIdx].Text.IndexOf('\u104A') >= 0)
                {
                    euphonicAtLiteraryClose = true;
                }
            }

            // Post-verbal gerundive '-စရာ' guard: when 'စရာ' is suffixed onto a preceding verb/clause in the same run
            // (e.g. 'ကြောက်စရာကြီး', 'စားစရာကောင်းသည်', 'ရယ်စရာလေး', 'ပြောစရာမလိုပါ'), never fuzzy-mutate 'စရာ...' into 'ဆရာ...'!
            if (windowLen >= 3 && startIndex > runStart &&
                firstSyl == "စ" && tokens[startIndex + 1].NormalizedText == "ရာ" &&
                (PostNominalAdjectives.Contains(lastSyl) ||
                 lastSyl == "ရှိ" || lastSyl == "လို" || lastSyl == "မ"))
            {
                return null;
            }

            bool isExactContentStemPlusPostAdj = false;
            if (windowLen >= 3 &&
                HasExactSpan(exactMultiWordBits, localOffset, windowLen - 1) &&
                PostNominalAdjectives.Contains(lastSyl))
            {
                string stemNorm = ConcatNormalizedTokens(tokens, startIndex, windowLen - 1);
                if (_trie.TryGetExactEntries(stemNorm, out var stemEntries) && stemEntries != null)
                {
                    for (int stIdx = 0; stIdx < stemEntries.Count; stIdx++)
                    {
                        var se = stemEntries[stIdx];
                        if (!string.Equals(se.PosTag, "PART", StringComparison.OrdinalIgnoreCase) &&
                            (se.IsOfficialThatpon || se.Frequency >= 2))
                        {
                            isExactContentStemPlusPostAdj = true;
                            break;
                        }
                    }
                }
            }

            bool isExactStemPlusValidSuffix =
                windowLen >= 3 &&
                HasExactSpan(exactMultiWordBits, localOffset, windowLen - 1) &&
                (!isEuphonicPeOrLe || euphonicAtLiteraryClose) &&
                (GrammaticalParticles.Contains(lastSyl) ||
                 ColloquialParticles.Contains(lastSyl) ||
                 VerbalContextFollowers.Contains(lastSyl) ||
                 ProductiveVerbalAuxiliaries.Contains(lastSyl) ||
                 isExactContentStemPlusPostAdj ||
                 (isEuphonicPeOrLe && euphonicAtLiteraryClose) ||
                 lastSyl == "ပုံ" || lastSyl == "ပင်" || lastSyl == "ဆေး" || lastSyl == "မဲ့" || lastSyl == "ထက်" ||
                 lastSyl == "ကား" || lastSyl == "သွင်" || lastSyl == "စွ" || lastSyl == "သောဝ်");

            if (isExactStemPlusValidSuffix)
            {
                return null;
            }

            // Exact-Word Prefix-Truncation Shield:
            // Never chop off the opening syllables (e.g. L=2 'အဓိ -> အထိ') of a longer exact Official/established multi-syllable word
            // starting at the same position (e.g. L=3 'အဓိက' in 'အဓိကစစ်တလင်းပြင်တစ်ခု', 'ပဓာန', 'သဘာဝ', 'အစိုးရ', 'ဥပမာ')
            // when the remainder after that exact word either ends the run or continues with a valid word, multi-word, or particle.
            int runLenForPrefixShield = runEnd - runStart;
            int maxLongerExactLen = Math.Min(12, runLenForPrefixShield - localOffset);
            for (int longerLen = windowLen + 1; longerLen <= maxLongerExactLen; longerLen++)
            {
                if (!HasExactSpan(exactMultiWordBits, localOffset, longerLen))
                    continue;

                string longerNorm = ConcatNormalizedTokens(tokens, startIndex, longerLen);
                if (!_trie.TryGetExactEntries(longerNorm, out var longerEntries) || longerEntries == null)
                    continue;

                bool establishedLonger = false;
                for (int lIdx = 0; lIdx < longerEntries.Count; lIdx++)
                {
                    if (longerEntries[lIdx].IsOfficialThatpon || longerEntries[lIdx].Frequency >= 15)
                    {
                        establishedLonger = true;
                        break;
                    }
                }
                if (!establishedLonger)
                    continue;

                int afterExactOffset = localOffset + longerLen;
                if (afterExactOffset >= runLenForPrefixShield)
                    return null;

                string afterExactSyl = tokens[runStart + afterExactOffset].NormalizedText;
                if (IsStartOfAnyExactMultiWord(exactMultiWordBits, afterExactOffset, runLenForPrefixShield) ||
                    GrammaticalParticles.Contains(afterExactSyl) ||
                    ColloquialParticles.Contains(afterExactSyl) ||
                    NumeralsAndDemonstratives.Contains(afterExactSyl) ||
                    (_trie.IsStandaloneWord(afterExactSyl) && _trie.GetWordFrequency(afterExactSyl) >= 2))
                {
                    return null;
                }
            }

            // Check whether this span has an exact multi-syllable prefix or suffix constituent, or a full clean productive compound split
            // (e.g. 'ရဲ'+'လုပ်ငန်း', 'ပဲ'+'လုပ်ငန်း', 'ရဲ'+'တပ်ဖွဲ့', 'မနက်'+'ဖန်', 'သတိ'+'မဲ့', 'အပြင်း'+'ထွက်')
            bool hasExactPrefixCompound = false;
            byte cleanSplitMask = 0;
            bool hasCleanLeadingStandalone = false;
            if (windowLen >= 3 && windowLen <= 5)
            {
                for (int split = 2; split < windowLen; split++)
                {
                    if (HasExactSpan(exactMultiWordBits, localOffset, split))
                    {
                        hasExactPrefixCompound = true;
                        break;
                    }
                }

                hasCleanLeadingStandalone =
                    IsCleanProductiveConstituent(tokens, startIndex, localOffset, exactMultiWordBits, 0, 1) &&
                    _trie.GetWordFrequency(firstSyl) >= 5;

                for (int split = 1; split < windowLen; split++)
                {
                    if ((split >= 2 || windowLen - split >= 2) &&
                        IsCleanProductiveConstituent(tokens, startIndex, localOffset, exactMultiWordBits, 0, split) &&
                        IsCleanProductiveConstituent(tokens, startIndex, localOffset, exactMultiWordBits, split, windowLen - split))
                    {
                        cleanSplitMask |= (byte)(1 << split);
                    }
                }
            }

            // Full 2-Word Guard: when BOTH syllables of L=2 are genuine multi-char standalone Burmese words (not weak 1-char ə- pre-syllables like တခါး/စဂါး/လဖက်),
            // Tier 2 StrictHomophones must preserve true homophony and Tier 3 Fuzzy requires an Official Thatpon target within tight distance <= 0.34!
            bool bothFullStandaloneInL2 = windowLen == 2 &&
                                          firstSyl.Length >= 2 &&
                                          lastSyl.Length >= 2 &&
                                          _trie.IsStandaloneWord(firstSyl) &&
                                          _trie.IsStandaloneWord(lastSyl) &&
                                          _trie.GetWordFrequency(firstSyl) >= 3 &&
                                          _trie.GetWordFrequency(lastSyl) >= 3;

            bool isPartialSliceOfRun = windowLen < (runEnd - runStart);
            bool followedByGeoClassifier = windowLen == 2 && IsFollowedByGeographicClassifier(tokens, startIndex + windowLen);

            // Cross-compound L=2 head-slicing shield: when firstSyl is a clean standalone word (e.g. 'ဆီ') and lastSyl opens an established
            // Official/HighFreq >=2-syllable compound (e.g. 'သိုလှောင်' in 'ဆီသိုလှောင်ကန်'), never mutate lastSyl ('သို -> သို့') away from its right compound!
            if (windowLen == 2 &&
                firstSyl.Length >= 2 &&
                firstSyl != "အ" && firstSyl != "မ" && firstSyl != "တ" &&
                _trie.IsStandaloneWord(firstSyl) &&
                _trie.GetWordFrequency(firstSyl) >= 5 &&
                HasEstablishedExactRightCompound(tokens, startIndex + 1, exactMultiWordBits, localOffset + 1))
            {
                mustPreserveLastSyl = true;
            }

            var span = GetTokenSpan(fullText, tokens, startIndex, windowLen);

            bool hasPreQuery = false;
            SuggestionEngine.PreEncodedQuery preQuery = default;

            // Tier 2: Strict Tone-Preserving Orthographic Homophone Skeleton
            DictionaryEntry? bestStrict = null;
            double bestStrictDist = double.MaxValue;
            double strictMaxDist = windowLen >= 3 ? ((hasExactPrefixCompound || cleanSplitMask != 0) ? 0.65 : 1.15) : 0.78;
            List<DictionaryEntry> strictMatches = _trie.LookupStrictHomophones(span.NormalizedText);
            if (strictMatches.Count > 0)
            {
                int minStrictFreq = (!isPartialSliceOfRun && !isProductiveANominalization && !hasExactPrefixCompound && cleanSplitMask == 0) ? 1 : 3;
                for (int i = 0; i < strictMatches.Count; i++)
                {
                    var e = strictMatches[i];
                    if (e.Word == span.NormalizedText || !IsCompatibleCompoundLength(e, windowLen))
                        continue;
                    if (mustPreserveFirstSyl && (!e.IsOfficialThatpon || !e.Word.StartsWith(firstSyl, StringComparison.Ordinal)))
                        continue;
                    if (mustPreserveLastSyl && (!e.IsOfficialThatpon || !e.Word.EndsWith(lastSyl, StringComparison.Ordinal)))
                        continue;
                    if (windowLen == 2 && !e.IsOfficialThatpon && e.IsProperNoun && !followedByGeoClassifier)
                        continue;
                    if (!e.IsOfficialThatpon && IsBiographyPersonEntry(e))
                        continue;

                    if (bothFullStandaloneInL2 || hasExactPrefixCompound || cleanSplitMask != 0 || isProductiveANominalization)
                    {
                        if (!AreTrueBurmeseHomophones(span.NormalizedText, e.Word))
                            continue;
                        if (!e.IsOfficialThatpon || e.Frequency < minStrictFreq || (windowLen == 2 && _trie.GetBigramCount(firstSyl, lastSyl) > 0))
                            continue;
                        if (windowLen == 2 && IsPureVerbSyllable(firstSyl) && IsPureVerbSyllable(lastSyl) && e.Frequency < 140)
                            continue;
                    }

                    if (!hasPreQuery)
                    {
                        preQuery = SuggestionEngine.CreateThreadLocalPreEncodedQuery(span.NormalizedText);
                        hasPreQuery = true;
                    }
                    double d = preQuery.DistanceTo(e.Word, strictMaxDist);
                    if (d <= strictMaxDist &&
                        (cleanSplitMask == 0 || IsAllowedOverCleanProductiveSplit(span.NormalizedText, tokens, startIndex, windowLen, cleanSplitMask, e, d)) &&
                        IsBetterCandidate(e, d, bestStrict, bestStrictDist))
                    {
                        bestStrict = e;
                        bestStrictDist = d;
                    }
                }

                // Guard against merging Noun + PostNominalAdjective even if a rare homophone exists
                if (bestStrict != null && windowLen == 2 && _trie.GetWordFrequency(firstSyl) >= 40 &&
                    PostNominalAdjectives.Contains(lastSyl) && bestStrict.Frequency < 50)
                {
                    bestStrict = null;
                }
            }

            // Tier 3: Generative Noisy-Channel Fuzzy Repair (Phonetic Skeleton + Positional Syllable Anchor Index)
            double bestFuzzyDist = double.MaxValue;
            DictionaryEntry? bestFuzzy = TryFindBestFuzzyCandidate(
                span.NormalizedText, tokens, startIndex, windowLen, firstSyl, lastSyl, hasExactPrefixCompound, cleanSplitMask, hasCleanLeadingStandalone, bothFullStandaloneInL2, isPartialSliceOfRun, mustPreserveFirstSyl, mustPreserveLastSyl, isProductiveANominalization, ref hasPreQuery, ref preQuery, out bestFuzzyDist);

            // Never let Tier 3 Fuzzy corrupt an already-exact 2-syllable Official/HighFreq right constituent after a standalone word (e.g. 'အိမ်အနီး' != 'အိမ်အမီ')
            if (bestFuzzy != null && windowLen == 3 && HasExactSpan(exactMultiWordBits, localOffset + 1, 2) &&
                firstSyl != "အ" && firstSyl != "မ" && firstSyl != "တ" &&
                _trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 12)
            {
                string right2Norm = ConcatNormalizedTokens(tokens, startIndex + 1, 2);
                if (!bestFuzzy.Word.EndsWith(right2Norm, StringComparison.Ordinal) &&
                    _trie.TryGetExactEntries(right2Norm, out var r2List) &&
                    r2List != null)
                {
                    for (int rIdx = 0; rIdx < r2List.Count; rIdx++)
                    {
                        var re = r2List[rIdx];
                        if ((re.IsOfficialThatpon && re.Frequency >= 8) || re.Frequency >= 35)
                        {
                            bestFuzzy = null;
                            break;
                        }
                    }
                }
            }

            // Double-'အ' Prefix Slicing Guard: never suggest an 'အ...'-prefixed replacement on a sub-window immediately preceded by 'အ'
            // (prevents slicing '[အ] + [ကြည်အညိုပျက် -> အကြည်ညိုပျက်]' into corrupted 'အအကြည်ညိုပျက်'!)
            if (startIndex > runStart &&
                firstSyl != "အ" &&
                tokens[startIndex - 1].NormalizedText == "အ")
            {
                if (bestStrict != null && bestStrict.Word.StartsWith("အ", StringComparison.Ordinal))
                    bestStrict = null;
                if (bestFuzzy != null && bestFuzzy.Word.StartsWith("အ", StringComparison.Ordinal))
                    bestFuzzy = null;
            }

            // Compare Tier 2 vs Tier 3 so high-frequency Thatpon words (e.g. 'အင်္ကျီ' freq 140) beat obscure zero-freq collisions ('အကျည်' freq 0)
            if (bestStrict != null && (bestFuzzy == null || !IsBetterCandidate(bestFuzzy, bestFuzzyDist, bestStrict, bestStrictDist)))
            {
                double freqBonus = Math.Min(0.24, Math.Log10(bestStrict.Frequency + 1) * 0.065);
                double thatponBonus = bestStrict.IsOfficialThatpon ? 0.06 : 0.0;
                var capSpan = span;
                var capBestStrict = bestStrict;
                double capStrictDist = bestStrictDist;
                ulong strictHash = MyanmarCompactLanguageModel.ComputeWordHash(bestStrict.Word);
                MyanmarUposTag strictTag = MyanmarCompactLanguageModel.ResolveWordUposTag(bestStrict.Word, _trie);

                return LatticeEdge.DeferredIssue(
                    windowLen,
                    0.96 + 0.12 * windowLen + bestStrictDist * 0.85 - freqBonus - thatponBonus,
                    span.RawText,
                    () =>
                    {
                        var suggestions = _suggestionEngine.GetSuggestions(capSpan.NormalizedText);
                        EnsureTopSuggestionMetadata(suggestions, capSpan.RawText, capBestStrict.Word, capBestStrict.MeaningCue, capBestStrict.PosTag, capBestStrict.IsOfficialThatpon);
                        bool safeStrictGeoSuffix = IsSafeTrailingGeoClassifierAukmyitRepair(capSpan.NormalizedText, capBestStrict.Word);
                        if (safeStrictGeoSuffix && suggestions.Count > 0 && suggestions[0].Replacement == capBestStrict.Word && suggestions[0].Score < 101.0)
                        {
                            suggestions[0].Score = 101.0;
                        }
                        string diffDetail = OrthographicDiffExplainer.ExplainDifference(capSpan.RawText, capBestStrict.Word);
                        bool strictNeedsHumanReview =
                            (IsProperNameDictionaryEntry(capBestStrict) && !safeStrictGeoSuffix) ||
                            capStrictDist > 0.45;

                        return new ProofingIssue
                        {
                            StartIndex = capSpan.StartIndex,
                            Length = capSpan.Length,
                            OriginalText = capSpan.RawText,
                            IssueType = ProofingIssueType.SpellingError,
                            Message = BuildDetailedSpellingMessage(capSpan.RawText, capBestStrict.Word, capBestStrict.MeaningCue, diffDetail),
                            RequiresReviewBeforeBatchFix = strictNeedsHumanReview,
                            Suggestions = suggestions
                        };
                    },
                    strictHash,
                    strictTag);
            }

            if (bestFuzzy == null)
                return null;

            double fBonus = Math.Min(0.24, Math.Log10(bestFuzzy.Frequency + 1) * 0.065);
            double tBonus = bestFuzzy.IsOfficialThatpon ? 0.06 : 0.0;
            var capFuzzySpan = span;
            var capBestFuzzy = bestFuzzy;
            double capFuzzyDist = bestFuzzyDist;
            ulong fuzzyHash = MyanmarCompactLanguageModel.ComputeWordHash(bestFuzzy.Word);
            MyanmarUposTag fuzzyTag = MyanmarCompactLanguageModel.ResolveWordUposTag(bestFuzzy.Word, _trie);

            return LatticeEdge.DeferredIssue(
                windowLen,
                1.00 + 0.12 * windowLen + bestFuzzyDist * 1.10 - fBonus - tBonus,
                span.RawText,
                () =>
                {
                    var fuzzySuggestions = _suggestionEngine.GetSuggestions(capFuzzySpan.NormalizedText);
                    bool keepCompositionalHomophoneTop =
                        firstSyl.Length >= 2 &&
                        capBestFuzzy.Word.IndexOf('\u1039') < 0 &&
                        fuzzySuggestions.Count > 0 &&
                        fuzzySuggestions[0].Score >= 88.0 &&
                        !_trie.ContainsExactWord(fuzzySuggestions[0].Replacement) &&
                        AreTrueBurmeseHomophones(capFuzzySpan.NormalizedText, fuzzySuggestions[0].Replacement) &&
                        !AreTrueBurmeseHomophones(capFuzzySpan.NormalizedText, capBestFuzzy.Word);
                    string chosenTopWord = keepCompositionalHomophoneTop ? fuzzySuggestions[0].Replacement : capBestFuzzy.Word;
                    if (!keepCompositionalHomophoneTop)
                    {
                        EnsureTopSuggestionMetadata(fuzzySuggestions, capFuzzySpan.RawText, capBestFuzzy.Word, capBestFuzzy.MeaningCue, capBestFuzzy.PosTag, capBestFuzzy.IsOfficialThatpon);
                    }
                    bool safeFuzzyGeoSuffix = IsSafeTrailingGeoClassifierAukmyitRepair(capFuzzySpan.NormalizedText, chosenTopWord);
                    if (safeFuzzyGeoSuffix && fuzzySuggestions.Count > 0 && fuzzySuggestions[0].Replacement == chosenTopWord && fuzzySuggestions[0].Score < 101.0)
                    {
                        fuzzySuggestions[0].Score = 101.0;
                    }
                    string fuzzyDiff = OrthographicDiffExplainer.ExplainDifference(capFuzzySpan.RawText, chosenTopWord);
                    string? chosenCue = keepCompositionalHomophoneTop ? fuzzySuggestions[0].MeaningCue : capBestFuzzy.MeaningCue;
                    bool fuzzyNeedsHumanReview =
                        (IsProperNameDictionaryEntry(capBestFuzzy) && !safeFuzzyGeoSuffix) ||
                        capFuzzyDist > 0.36;

                    return new ProofingIssue
                    {
                        StartIndex = capFuzzySpan.StartIndex,
                        Length = capFuzzySpan.Length,
                        OriginalText = capFuzzySpan.RawText,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = BuildDetailedSpellingMessage(capFuzzySpan.RawText, chosenTopWord, chosenCue, fuzzyDiff),
                        RequiresReviewBeforeBatchFix = fuzzyNeedsHumanReview,
                        Suggestions = fuzzySuggestions
                    };
                },
                fuzzyHash,
                fuzzyTag);
        }

    }
}
