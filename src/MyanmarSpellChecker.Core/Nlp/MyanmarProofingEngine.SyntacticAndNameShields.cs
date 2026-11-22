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
        private bool IsCleanProductiveConstituent(
            List<SyllableToken> tokens,
            int startIndex,
            int localOffset,
            ushort[] exactMultiWordBits,
            int subOffset,
            int subLen)
        {
            if (subLen <= 0 || subLen > 12 || !HasExactSpan(exactMultiWordBits, localOffset + subOffset, subLen))
                return false;

            int subTokIdx = startIndex + subOffset;
            if (subLen == 1)
            {
                SyllableToken tok = tokens[subTokIdx];
                string syl = tok.NormalizedText;
                if (syl.Length < 2)
                    return false; // 1-char bare open consonants ('တ', 'စ', 'ပ', 'က', 'မ', 'အ', 'သ', 'လ') may be reduced ə- pre-syllables
                if (UnicodeNormalizer.HasLookalikeErrorSlice(tok.Text, 0, tok.Text.Length))
                    return false;
                if (BoundSyllableVerbFrameMap.ContainsKey(syl))
                    return false;
                return _trie.IsStandaloneWord(syl) && _trie.GetWordFrequency(syl) >= 2;
            }

            for (int k = 0; k < subLen; k++)
            {
                SyllableToken tk = tokens[subTokIdx + k];
                if (UnicodeNormalizer.HasLookalikeErrorSlice(tk.Text, 0, tk.Text.Length))
                    return false;
            }

            string subNorm = ConcatNormalizedTokens(tokens, subTokIdx, subLen);
            if (!_trie.TryGetExactEntries(subNorm, out var subEntries) || subEntries == null)
                return false;
            for (int i = 0; i < subEntries.Count; i++)
            {
                if (subEntries[i].IsOfficialThatpon || subEntries[i].Frequency >= 5)
                    return true;
            }
            return false;
        }

        private bool IsAllowedOverCleanProductiveSplit(
            string spanNorm,
            List<SyllableToken> tokens,
            int startIndex,
            int windowLen,
            byte cleanSplitMask,
            DictionaryEntry cand,
            double dist)
        {
            string candWord = cand.Word;
            for (int split = 1; split < windowLen; split++)
            {
                if ((cleanSplitMask & (1 << split)) == 0)
                    continue;

                int rightSyls = windowLen - split;
                string leftNorm = ConcatNormalizedTokens(tokens, startIndex, split);
                string rightNorm = ConcatNormalizedTokens(tokens, startIndex + split, rightSyls);

                // Case 1: Both constituents are exact multi-syllable words (e.g. 'ရဲတပ်ဖွဲ့ + လုပ်ငန်း', 'ကျန်းမာရေး + ဝန်ကြီး', vs 'အခွင့် + အရေ -> အခွင့်အရေး')
                if (split >= 2 && rightSyls >= 2)
                {
                    bool allowMultiMulti =
                        (AreTrueOrPalatalRhymeHomophones(spanNorm, candWord) && cand.IsOfficialThatpon && cand.Frequency >= 40) ||
                        (cand.IsOfficialThatpon && cand.Frequency >= 10 &&
                         _trie.GetBigramCount(leftNorm, rightNorm) == 0 &&
                         ((candWord.StartsWith(leftNorm, StringComparison.Ordinal) &&
                           (candWord.Substring(leftNorm.Length) == rightNorm + "\u1038" ||
                            candWord.Substring(leftNorm.Length) == rightNorm + "\u1037")) ||
                          (candWord.EndsWith(rightNorm, StringComparison.Ordinal) &&
                           (candWord.Substring(0, candWord.Length - rightNorm.Length) == leftNorm + "\u1038" ||
                            candWord.Substring(0, candWord.Length - rightNorm.Length) == leftNorm + "\u1037")) ||
                          (spanNorm[0] == candWord[0] && dist <= 0.26)));
                    if (!allowMultiMulti)
                        return false;
                    continue;
                }

                // Case 2: [Clean Full Standalone 1-Syl Modifier] + [Exact Multi-Syllable Head]
                // (e.g. 'ရဲ + လုပ်ငန်း', 'ပဲ + လုပ်ငန်း', 'ဆေး + လုပ်ငန်း', 'ဘဏ် + လုပ်ငန်း', 'ရဲ + တပ်ဖွဲ့', 'ရဲ + စခန်း')
                if (split == 1 && rightSyls >= 2)
                {
                    if (candWord.EndsWith(rightNorm, StringComparison.Ordinal))
                    {
                        string candLeft = candWord.Substring(0, candWord.Length - rightNorm.Length);
                        bool allowModRepair =
                            candLeft.Length > 0 &&
                            cand.IsOfficialThatpon &&
                            cand.Frequency >= 15 &&
                            _trie.GetBigramCount(leftNorm, rightNorm) == 0 &&
                            ((AreTrueOrPalatalRhymeHomophones(leftNorm, candLeft) && (IsPureVerbSyllable(leftNorm) || rightNorm == "တလော")) ||
                             ((candLeft == leftNorm + "\u1038" || candLeft == leftNorm + "\u1037") &&
                              (!_trie.HasPosTag(leftNorm, "N") ||
                               _trie.ContainsExactWord(candLeft + tokens[startIndex + 1].NormalizedText))));
                        if (!allowModRepair)
                            return false;
                    }
                    else
                    {
                        bool allowCleanModWithTailTypo =
                            (AreTrueOrPalatalRhymeHomophones(spanNorm, candWord) && cand.IsOfficialThatpon && cand.Frequency >= 25) ||
                            (candWord.StartsWith(leftNorm, StringComparison.Ordinal) &&
                             cand.IsOfficialThatpon &&
                             cand.Frequency >= 15 &&
                             dist <= 0.28 &&
                             _trie.GetBigramCount(leftNorm, rightNorm) == 0);
                        if (!allowCleanModWithTailTypo)
                            return false;
                    }
                    continue;
                }

                // Case 3: [Exact Multi-Syllable Stem] + [Clean Full Standalone 1-Syl Word]
                // (e.g. allows 'မနက်ဖန် -> မနက်ဖြန်', 'ကျန်းမာရေ -> ကျန်းမာရေး', 'လောလောဆည် -> လောလောဆယ်', while protecting 'လုပ်ငန်းသစ် != လုပ်ငန်းစဉ်')
                if (split >= 2 && rightSyls == 1)
                {
                    if (candWord.StartsWith(leftNorm, StringComparison.Ordinal))
                    {
                        string candRight = candWord.Substring(leftNorm.Length);
                        bool allowTailRepair =
                            candRight.Length > 0 &&
                            cand.IsOfficialThatpon &&
                            cand.Frequency >= 12 &&
                            ((AreTrueOrPalatalRhymeHomophones(rightNorm, candRight) && _trie.GetBigramCount(leftNorm, rightNorm) == 0) ||
                             (candRight == rightNorm + "\u1038" || candRight == rightNorm + "\u1037") ||
                             (rightNorm[0] == candRight[0] && dist <= 0.30 && _trie.GetBigramCount(leftNorm, rightNorm) <= 2) ||
                             (rightNorm[0] == candRight[0] && dist <= 0.36 && cand.Frequency >= 80 && _trie.GetBigramCount(leftNorm, rightNorm) == 0));
                        if (!allowTailRepair)
                            return false;
                    }
                    else
                    {
                        bool allowStemTypoWithCleanTail =
                            (AreTrueOrPalatalRhymeHomophones(spanNorm, candWord) && cand.IsOfficialThatpon && cand.Frequency >= 25) ||
                            (candWord.EndsWith(rightNorm, StringComparison.Ordinal) &&
                             spanNorm[0] == candWord[0] &&
                             cand.IsOfficialThatpon &&
                             cand.Frequency >= 10 &&
                             dist <= 0.28 &&
                             _trie.GetBigramCount(leftNorm, rightNorm) == 0);
                        if (!allowStemTypoWithCleanTail)
                            return false;
                    }
                }
            }

            return true;
        }

        private DictionaryEntry? TryFindBestFuzzyCandidate(
            string spanNormalized,
            List<SyllableToken> tokens,
            int startIndex,
            int windowLen,
            string firstSyl,
            string lastSyl,
            bool hasExactPrefixCompound,
            byte cleanSplitMask,
            bool hasCleanLeadingStandalone,
            bool bothFullStandaloneInL2,
            bool isPartialSliceOfRun,
            bool mustPreserveFirstSyl,
            bool mustPreserveLastSyl,
            bool isProductiveANominalization,
            ref bool hasPreQuery,
            ref SuggestionEngine.PreEncodedQuery preQuery,
            out double bestFuzzyDist)
        {
            bestFuzzyDist = double.MaxValue;
            bool bothKnownInL2 = false;
            int f1 = 0, f2 = 0;
            bool hasRightVerbalContext = false;

            if (windowLen == 2)
            {
                if (NumeralsAndDemonstratives.Contains(firstSyl))
                    return null;

                f1 = _trie.GetWordFrequency(firstSyl);
                f2 = _trie.GetWordFrequency(lastSyl);

                if ((f1 >= 40 || (_trie.IsStandaloneWord(firstSyl) && f1 >= 5 && _trie.HasPosTag(firstSyl, "N"))) &&
                    PostNominalAdjectives.Contains(lastSyl))
                {
                    return null;
                }

                bothKnownInL2 = _trie.IsValidSingleSyllable(firstSyl) && _trie.IsValidSingleSyllable(lastSyl);
                if (bothKnownInL2 && bothFullStandaloneInL2 && _trie.GetBigramCount(firstSyl, lastSyl) > 0)
                    return null;

                int nextTokIdx = startIndex + 2;
                if (nextTokIdx < tokens.Count && tokens[nextTokIdx].Kind == TokenKind.MyanmarSyllable)
                {
                    hasRightVerbalContext = VerbalContextFollowers.Contains(tokens[nextTokIdx].NormalizedText);
                }
            }

            double fuzzyMaxDist;
            if (windowLen >= 3)
            {
                fuzzyMaxDist = (hasExactPrefixCompound || cleanSplitMask != 0) ? 0.36 : 0.68;
            }
            else if (!bothKnownInL2)
            {
                fuzzyMaxDist = 0.68;
            }
            else
            {
                bool bothStandalone = _trie.IsStandaloneWord(firstSyl) && _trie.IsStandaloneWord(lastSyl);
                fuzzyMaxDist = bothStandalone ? 0.36 : 0.58;
            }

            bool followedByGeoClassifier = windowLen == 2 && IsFollowedByGeographicClassifier(tokens, startIndex + windowLen);
            bool isAtRunStart = startIndex == 0 || tokens[startIndex - 1].Kind != TokenKind.MyanmarSyllable;

            (string SylKey, ushort Flags) fuzzyCacheKey = default;
            bool canCacheWindow = windowLen >= 2 && windowLen <= 5 && spanNormalized.Length <= 48;
            if (canCacheWindow)
            {
                string sylKey = windowLen switch
                {
                    2 => string.Concat(firstSyl, "\u001F", lastSyl),
                    3 => string.Concat(firstSyl, "\u001F", tokens[startIndex + 1].NormalizedText, "\u001F", lastSyl),
                    4 => string.Concat(firstSyl, "\u001F", tokens[startIndex + 1].NormalizedText, "\u001F", tokens[startIndex + 2].NormalizedText, "\u001F", lastSyl),
                    _ => string.Concat(firstSyl, "\u001F", tokens[startIndex + 1].NormalizedText, "\u001F", tokens[startIndex + 2].NormalizedText, "\u001F", tokens[startIndex + 3].NormalizedText, "\u001F", lastSyl)
                };
                ushort flags = (ushort)(
                    (hasExactPrefixCompound ? 1 : 0) |
                    (bothFullStandaloneInL2 ? 2 : 0) |
                    (isPartialSliceOfRun ? 4 : 0) |
                    (hasRightVerbalContext ? 8 : 0) |
                    (followedByGeoClassifier ? 16 : 0) |
                    ((cleanSplitMask & 0x1F) << 5) |
                    (hasCleanLeadingStandalone ? 1024 : 0) |
                    (mustPreserveFirstSyl ? 2048 : 0) |
                    (mustPreserveLastSyl ? 4096 : 0) |
                    (isAtRunStart ? 8192 : 0) |
                    (isProductiveANominalization ? 16384 : 0));
                fuzzyCacheKey = (sylKey, flags);
                if (_fuzzyWindowCache.TryGetValue(fuzzyCacheKey, out var cachedHit))
                {
                    bestFuzzyDist = cachedHit.Dist;
                    return cachedHit.Best;
                }
            }

            List<DictionaryEntry> phoneticHits = _trie.LookupByPhoneticKey(spanNormalized, out string spanPhoneticKey);

            bool zeroEmpiricalBigram = windowLen == 2 &&
                                       _trie.BigramCount > 0 &&
                                       _trie.GetBigramCount(firstSyl, lastSyl) == 0 &&
                                       !ProductiveVerbalAuxiliaries.Contains(lastSyl);
            bool spanHasPaliPatSintOrRetroflex = HasNonKinziPatSintOrRetroflex(spanNormalized);
            bool spanHasNonKinziPatSint = spanHasPaliPatSintOrRetroflex && HasNonKinziPatSint(spanNormalized);
            bool pureVerbPairInL2 = windowLen == 2 && bothKnownInL2 && bothFullStandaloneInL2 &&
                                    IsPureVerbSyllable(firstSyl) && IsPureVerbSyllable(lastSyl);
            bool anyBoundOrZeroInL2 = windowLen == 2 && bothKnownInL2 && !bothFullStandaloneInL2 &&
                                      (!_trie.IsStandaloneWord(firstSyl) || !_trie.IsStandaloneWord(lastSyl) || f1 == 0 || f2 == 0);
            int minSylFreqL2 = Math.Min(f1, f2);
            int spanLen = spanNormalized.Length;

            var seenWords = t_fuzzySeenWords ??= new HashSet<string>(StringComparer.Ordinal);
            seenWords.Clear();

            DictionaryEntry? bestFuzzy = null;
            double localBestDist = double.MaxValue;
            bool localHasPre = hasPreQuery;
            SuggestionEngine.PreEncodedQuery localPre = preQuery;

            void EvaluateCandidate(DictionaryEntry e, bool enforceAnchorLengthDelta)
            {
                if (!IsCompatibleCompoundLength(e, windowLen))
                    return;

                string w = e.Word;
                if (w == spanNormalized)
                    return;

                if (mustPreserveFirstSyl && (!e.IsOfficialThatpon || !w.StartsWith(firstSyl, StringComparison.Ordinal)))
                    return;
                if (mustPreserveLastSyl && (!e.IsOfficialThatpon || !w.EndsWith(lastSyl, StringComparison.Ordinal)))
                    return;

                if (enforceAnchorLengthDelta)
                {
                    int delta = Math.Abs(w.Length - spanLen);
                    if (delta > 4)
                        return;
                    if (delta > 2 && w.IndexOf('\u1039') < 0)
                        return;
                }

                if (windowLen == 2 && !e.IsOfficialThatpon && e.IsProperNoun && !followedByGeoClassifier)
                    return;
                if (!e.IsOfficialThatpon && IsBiographyPersonEntry(e))
                    return;

                bool isCredibleLexeme = e.IsOfficialThatpon || e.Frequency >= 4 ||
                                        e.IsProperNoun ||
                                        string.Equals(e.PosTag, "LOAN", StringComparison.OrdinalIgnoreCase);
                if (!isCredibleLexeme)
                    return;

                double allowedMaxDist = fuzzyMaxDist;
                if (windowLen >= 3 && (hasExactPrefixCompound || cleanSplitMask != 0) && e.Frequency < 10)
                    return;

                if (windowLen == 2 && bothKnownInL2)
                {
                    if (bothFullStandaloneInL2)
                    {
                        if (pureVerbPairInL2)
                        {
                            bool allowAnchoredOfficialVerbCompound =
                                e.IsOfficialThatpon &&
                                e.Frequency >= 30 &&
                                zeroEmpiricalBigram &&
                                (w.StartsWith(firstSyl, StringComparison.Ordinal) || w.EndsWith(lastSyl, StringComparison.Ordinal));
                            if (!allowAnchoredOfficialVerbCompound && e.Frequency < 140)
                                return;
                        }
                        if (!(e.IsOfficialThatpon && e.Frequency >= 8) && e.Frequency < 45)
                            return;
                        allowedMaxDist = Math.Min(allowedMaxDist, 0.34);
                    }
                    else
                    {
                        // 5-Part Noisy-Channel + Bigram Prominence Guard for 2-syllable repairs when one syllable is bound/weak
                        bool signalBoundOrZeroFreq = anyBoundOrZeroInL2 &&
                                                     (e.Frequency >= 5 || (e.IsOfficialThatpon && e.Frequency >= 2));

                        bool isVerbOrAdj = !string.IsNullOrEmpty(e.PosTag) &&
                                           (e.PosTag.IndexOf('V') >= 0 || e.PosTag.IndexOf("ADJ", StringComparison.OrdinalIgnoreCase) >= 0);
                        bool signalVerbalContext = hasRightVerbalContext && isVerbOrAdj && e.Frequency >= 5;

                        bool signalFreqDominance = e.Frequency >= 30 && e.Frequency >= Math.Max(12, (int)(minSylFreqL2 * 1.5));

                        bool signalPhoneticOfficial = e.IsOfficialThatpon &&
                                                      e.PhoneticKey == spanPhoneticKey &&
                                                      e.Frequency >= Math.Max(8, minSylFreqL2);

                        bool signalZeroBigramOfficial = zeroEmpiricalBigram && e.IsOfficialThatpon && e.Frequency >= 45;

                        bool signalWeakOpenPreSyllable = isAtRunStart &&
                                                         (firstSyl == "တ" || firstSyl == "က" || firstSyl == "စ" || firstSyl == "ပ") &&
                                                         spanLen >= 3 &&
                                                         e.IsOfficialThatpon &&
                                                         e.Frequency >= 35;

                        if (!signalBoundOrZeroFreq && !signalVerbalContext && !signalFreqDominance &&
                            !signalPhoneticOfficial && !signalZeroBigramOfficial && !signalWeakOpenPreSyllable)
                        {
                            return;
                        }

                        if ((signalZeroBigramOfficial || signalWeakOpenPreSyllable) && allowedMaxDist < 0.42)
                            allowedMaxDist = 0.42;
                    }
                }

                if (!seenWords.Add(w))
                    return;

                // O(1) pre-DP structural & Official-Thatpon guards (avoids 2D edit-distance computation on candidates guaranteed to fail post-DP filters!)
                if (windowLen >= 3)
                {
                    if (cleanSplitMask != 0)
                    {
                        if (!e.IsOfficialThatpon ||
                            !IsAllowedOverCleanProductiveSplit(spanNormalized, tokens, startIndex, windowLen, cleanSplitMask, e, 0.0))
                        {
                            return;
                        }
                    }
                    if (hasExactPrefixCompound && !e.IsOfficialThatpon && e.Frequency < 25)
                        return;
                    if (isProductiveANominalization && w.IndexOf('\u1039') < 0 && !AreTrueBurmeseHomophones(spanNormalized, w))
                        return;
                }

                if (spanHasPaliPatSintOrRetroflex && !AreTrueBurmeseHomophones(spanNormalized, w))
                {
                    bool allowWholeWordOfficialPaliRepair =
                        !isPartialSliceOfRun &&
                        (e.IsOfficialThatpon || e.IsProperNoun) &&
                        e.Frequency >= 25 &&
                        HasNonKinziPatSint(w) == spanHasNonKinziPatSint;
                    if (!allowWholeWordOfficialPaliRepair)
                        return;
                }

                if (!localHasPre)
                {
                    localPre = SuggestionEngine.CreateThreadLocalPreEncodedQuery(spanNormalized);
                    localHasPre = true;
                }
                double tightenMax = bestFuzzy != null ? Math.Min(allowedMaxDist, localBestDist + 0.21) : allowedMaxDist;
                double dist = localPre.DistanceToEntry(e, tightenMax);
                if (dist > allowedMaxDist)
                    return;

                if (isProductiveANominalization &&
                    w.IndexOf('\u1039') < 0 &&
                    !AreTrueBurmeseHomophones(spanNormalized, w))
                {
                    bool allowSameOnsetNasalOrToneRepair =
                        windowLen == 2 &&
                        firstSyl == "အ" &&
                        e.IsOfficialThatpon &&
                        e.Frequency >= 60 &&
                        dist <= 0.28 &&
                        SharesSameSecondSyllableOnsetCluster(lastSyl, w);
                    if (!allowSameOnsetNasalOrToneRepair)
                        return;
                }

                if (windowLen >= 3 && cleanSplitMask != 0 &&
                    !IsAllowedOverCleanProductiveSplit(spanNormalized, tokens, startIndex, windowLen, cleanSplitMask, e, dist))
                {
                    return;
                }

                // When firstSyl is already a clean standalone word (e.g. 'ဆေးလုပ်ငန်', 'ရဲလုပ်ငန်') and the tail has a typo,
                // never allow an L=3 fuzzy candidate to mutate BOTH firstSyl into an unrelated word AND the tail!
                if (windowLen == 3 && cleanSplitMask == 0 && !hasExactPrefixCompound && hasCleanLeadingStandalone &&
                    !w.StartsWith(firstSyl, StringComparison.Ordinal) &&
                    !(spanNormalized.Length == w.Length && CharMultisetEquals(spanNormalized, w)))
                {
                    var candSyls3 = MyanmarSyllableBreaker.BreakIntoSyllables(w, isPreNormalized: true);
                    if (candSyls3.Count == 3 &&
                        candSyls3[0] != firstSyl &&
                        (candSyls3[1] != tokens[startIndex + 1].NormalizedText || candSyls3[2] != lastSyl) &&
                        candSyls3[0] != firstSyl + "\u1038" &&
                        candSyls3[0] != firstSyl + "\u1037" &&
                        !AreTrueBurmeseHomophones(firstSyl, candSyls3[0]))
                    {
                        return;
                    }
                }

                if (windowLen >= 3 && hasExactPrefixCompound)
                {
                    bool passPrefixGuard = (e.IsOfficialThatpon && e.Frequency >= 10) ||
                                           (e.PhoneticKey == spanPhoneticKey && dist <= 0.32 && e.Frequency >= 25) ||
                                           (dist <= 0.24 && e.Frequency >= 45) ||
                                           e.Frequency >= 80;
                    if (!passPrefixGuard)
                        return;
                }

                if (windowLen == 2 && bothKnownInL2 && bothFullStandaloneInL2)
                {
                    if (firstSyl.IndexOf('\u103A') >= 0 || firstSyl.IndexOf('\u1036') >= 0)
                    {
                        var candSyls = MyanmarSyllableBreaker.BreakIntoSyllables(w, isPreNormalized: true);
                        if (candSyls.Count == 2 && candSyls[0].Length == 1)
                            return;
                    }

                    bool isOfficialClose = e.IsOfficialThatpon &&
                                           e.Frequency >= ((dist <= 0.30 || e.PhoneticKey == spanPhoneticKey) ? 8 : 25) &&
                                           dist <= 0.34;
                    bool isHighFreqToneDrop = dist <= 0.23 && e.Frequency >= 45;
                    if (!isOfficialClose && !isHighFreqToneDrop)
                        return;
                }

                // Never simultaneously mutate BOTH syllables of an L=2 span into a non-official word unless they are true homophones (prevents ethnic/foreign names like 'မဂမ်' -> 'မန်ကန်')
                if (windowLen == 2 &&
                    !e.IsOfficialThatpon &&
                    !w.StartsWith(firstSyl, StringComparison.Ordinal) &&
                    !w.EndsWith(lastSyl, StringComparison.Ordinal) &&
                    !AreTrueBurmeseHomophones(spanNormalized, w))
                {
                    return;
                }

                if (IsBetterCandidate(e, dist, bestFuzzy, localBestDist))
                {
                    bestFuzzy = e;
                    localBestDist = dist;
                }
            }

            for (int i = 0; i < phoneticHits.Count; i++)
            {
                EvaluateCandidate(phoneticHits[i], enforceAnchorLengthDelta: false);
            }

            if (windowLen >= 2 && windowLen <= 5)
            {
                for (int pos = 0; pos < windowLen; pos++)
                {
                    _trie.GetCompoundAnchorBuckets(windowLen, pos, tokens[startIndex + pos].NormalizedText, out var exBucket, out var phBucket);
                    if (exBucket is DictionaryEntry exSingle)
                    {
                        EvaluateCandidate(exSingle, enforceAnchorLengthDelta: true);
                    }
                    else if (exBucket is List<DictionaryEntry> exList)
                    {
                        for (int i = 0; i < exList.Count; i++)
                            EvaluateCandidate(exList[i], enforceAnchorLengthDelta: true);
                    }

                    if (phBucket is DictionaryEntry phSingle)
                    {
                        EvaluateCandidate(phSingle, enforceAnchorLengthDelta: true);
                    }
                    else if (phBucket is List<DictionaryEntry> phList)
                    {
                        for (int i = 0; i < phList.Count; i++)
                            EvaluateCandidate(phList[i], enforceAnchorLengthDelta: true);
                    }
                }
            }

            hasPreQuery = localHasPre;
            preQuery = localPre;
            bestFuzzyDist = localBestDist;
            if (canCacheWindow)
            {
                if (_fuzzyWindowCache.Count > FuzzyWindowCacheCap)
                {
                    _fuzzyWindowCache.Clear();
                }
                _fuzzyWindowCache[fuzzyCacheKey] = (bestFuzzy, localBestDist);
            }
            return bestFuzzy;
        }

        private static bool IsBetterCandidate(DictionaryEntry cand, double candDist, DictionaryEntry? currentBest, double currentBestDist)
        {
            if (currentBest == null)
                return true;

            // Combine weighted edit distance with corpus frequency log-prior & Official Thatpon authority so high-usage Thatpon headwords ('ထမင်း', 'ခေတ်စား') beat obscure non-official collisions ('ဓမင်း', 'ကျိတ်စား')
            double candRankScore = candDist - Math.Min(0.18, Math.Log10(cand.Frequency + 1) * 0.045) - (cand.IsOfficialThatpon ? 0.025 : 0.0);
            double bestRankScore = currentBestDist - Math.Min(0.18, Math.Log10(currentBest.Frequency + 1) * 0.045) - (currentBest.IsOfficialThatpon ? 0.025 : 0.0);
            return candRankScore < bestRankScore;
        }

        private static bool IsFollowedByGeographicClassifier(List<SyllableToken> tokens, int nextIndex)
        {
            if (nextIndex < 0 || nextIndex >= tokens.Count || tokens[nextIndex].Kind != TokenKind.MyanmarSyllable)
                return false;

            string s1 = tokens[nextIndex].NormalizedText;
            if (s1 == "မြို့" || s1 == "တိုင်း" || s1 == "ကျွန်း" || s1 == "ကန်" || s1 == "မြစ်" || s1 == "တောင်")
                return true;

            if (nextIndex + 1 < tokens.Count && tokens[nextIndex + 1].Kind == TokenKind.MyanmarSyllable)
            {
                string s2 = tokens[nextIndex + 1].NormalizedText;
                if ((s1 == "နိုင်" && s2 == "ငံ") ||
                    (s1 == "ခ" && s2 == "ရိုင်") ||
                    (s1 == "ပြည်" && s2 == "နယ်") ||
                    (s1 == "ဒေ" && s2 == "သ"))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasAnyInvalidRawSyllableInRange(
            List<SyllableToken> tokens,
            int start,
            int count,
            MyanmarTrie trie)
        {
            int end = start + count;
            for (int k = start; k < end; k++)
            {
                string raw = tokens[k].Text;
                // Exempt canonical multi-token classical & loanword ligature continuations:
                // 1) 'ယောက်' + 'ျား' / 'လက်' + 'ျာ' (\u1000\u103A + \u103B\u102C[\u1037\u1038]?)
                // 2) 'ကျွန်' + 'ု' + 'ပ်' or 'ကျွန်' + 'ုပ်' (\u1014\u103A + \u102F + \u1015\u103A)
                // 3) Foreign '-ch / -j' loanword codas ('ဒတ်ခ်' + 'ျ', 'ဂျော့ဂ်' + 'ျ', 'ဟာဂ်' + 'ျ')
                if (k > 0)
                {
                    string prevNorm = tokens[k - 1].NormalizedText;
                    if ((raw == "\u103B\u102C" || raw == "\u103B\u102C\u1038" || raw == "\u103B\u102C\u1037") &&
                        prevNorm.EndsWith("\u1000\u103A", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if ((raw == "\u102F\u1015\u103A" ||
                         (raw == "\u102F" && k + 1 < tokens.Count && tokens[k + 1].NormalizedText.StartsWith("\u1015\u103A", StringComparison.Ordinal))) &&
                        prevNorm.EndsWith("\u1014\u103A", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (raw == "\u103B" &&
                        (prevNorm.EndsWith("\u1001\u103A", StringComparison.Ordinal) ||
                         prevNorm.EndsWith("\u1002\u103A", StringComparison.Ordinal) ||
                         prevNorm.EndsWith("\u1017\u103A", StringComparison.Ordinal) ||
                         prevNorm.EndsWith("\u1007\u103A", StringComparison.Ordinal)))
                    {
                        continue;
                    }
                }

                if (!MyanmarSyllableValidator.ValidateSyllable(raw, out _, out _, trie))
                {
                    // Never reject an exact verbatim headword stored in the verified dictionary (e.g. Judson 'ကျွနု်ပ်')
                    var sbRaw = new StringBuilder(16);
                    for (int r = start; r < end; r++)
                        sbRaw.Append(tokens[r].Text);
                    if (trie.ContainsExactWord(sbRaw.ToString()))
                        return false;

                    return true;
                }
            }
            return false;
        }

        private static bool IsDroppedAsatBareConsonant(string syl, bool allowExtendedMultiSylCodas, out char cons, out string trailingTone)
        {
            cons = '\0';
            trailingTone = string.Empty;
            if (string.IsNullOrEmpty(syl) || syl.Length > 2)
                return false;

            char c0 = syl[0];
            // Valid Burmese coda consonants (excludes 'က' and 'မ' for L=2; allows 'ဒ' and guarded 'က' for L>=3)
            bool isCodaCandidate = c0 == '\u1004' || // င
                                   c0 == '\u1005' || // စ
                                   c0 == '\u1009' || // ဉ
                                   c0 == '\u100A' || // ည
                                   c0 == '\u100F' || // ဏ
                                   c0 == '\u1010' || // တ
                                   c0 == '\u1014' || // န
                                   c0 == '\u1015' || // ပ
                                   c0 == '\u101A' || // ယ
                                   c0 == '\u101D' || // ဝ
                                   (allowExtendedMultiSylCodas && (c0 == '\u1000' || c0 == '\u1012')); // က / ဒ
            if (!isCodaCandidate)
                return false;

            if (syl.Length == 2)
            {
                char c1 = syl[1];
                if (c1 != '\u1037' && c1 != '\u1038')
                    return false;
                trailingTone = c1.ToString();
            }

            cons = c0;
            return true;
        }

        private static bool AreTrueBurmeseHomophones(string a, string b)
        {
            // Must preserve exact aspiration ('ှ'), medial 'ွ', gemination count, and strict homophone key!
            if (CountChar(a, '\u103E') != CountChar(b, '\u103E'))
                return false;
            if (CountChar(a, '\u103D') != CountChar(b, '\u103D'))
                return false;
            if ((a.IndexOf('\u1039') >= 0 && a.IndexOf("\u1004\u103A\u1039", StringComparison.Ordinal) < 0) !=
                (b.IndexOf('\u1039') >= 0 && b.IndexOf("\u1004\u103A\u1039", StringComparison.Ordinal) < 0))
                return false;
            if (CountChar(a, '\u1037') != CountChar(b, '\u1037') || CountChar(a, '\u1038') != CountChar(b, '\u1038'))
                return false;

            return MyanmarTrie.ComputeStrictHomophoneKey(a) == MyanmarTrie.ComputeStrictHomophoneKey(b);
        }

        private static bool AreTrueOrPalatalRhymeHomophones(string a, string b)
        {
            if (AreTrueBurmeseHomophones(a, b))
                return true;

            // Recognize Burmese open/palatal /ɛ/ rhyme homophones between '-ည်' / '-ဉ်' and '-ယ်' (e.g. 'ဆည် <-> ဆယ်', 'လည် <-> လယ်')
            // without equating '-ယ်' with pure vowel '-ီ' (e.g. 'နီ != နယ်')
            bool hasYaAsat = a.IndexOf("\u101A\u103A", StringComparison.Ordinal) >= 0 ||
                            b.IndexOf("\u101A\u103A", StringComparison.Ordinal) >= 0;
            if (!hasYaAsat)
                return false;

            if (CountChar(a, '\u103E') != CountChar(b, '\u103E') ||
                CountChar(a, '\u103D') != CountChar(b, '\u103D') ||
                CountChar(a, '\u1037') != CountChar(b, '\u1037') ||
                CountChar(a, '\u1038') != CountChar(b, '\u1038'))
            {
                return false;
            }
            if ((a.IndexOf('\u1039') >= 0 && a.IndexOf("\u1004\u103A\u1039", StringComparison.Ordinal) < 0) !=
                (b.IndexOf('\u1039') >= 0 && b.IndexOf("\u1004\u103A\u1039", StringComparison.Ordinal) < 0))
            {
                return false;
            }

            string normA = NormalizePalatalCodaToYaAsat(a);
            string normB = NormalizePalatalCodaToYaAsat(b);
            return MyanmarTrie.ComputeStrictHomophoneKey(normA) == MyanmarTrie.ComputeStrictHomophoneKey(normB);
        }

        private static string NormalizePalatalCodaToYaAsat(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var sb = new StringBuilder(s.Length);
            sb.Append(s[0]);
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if ((c == '\u100A' || c == '\u1009') && i + 1 < s.Length && s[i + 1] == '\u103A')
                {
                    sb.Append('\u101A');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static bool SharesSameSecondSyllableOnsetCluster(string inputSecondSyl, string candidateTwoSylWord)
        {
            List<string> candSyls = MyanmarSyllableBreaker.BreakIntoSyllables(candidateTwoSylWord);
            if (candSyls.Count != 2)
                return false;

            return ExtractOnsetAndMedials(inputSecondSyl) == ExtractOnsetAndMedials(candSyls[1]);
        }

        private static string ExtractOnsetAndMedials(string syl)
        {
            if (string.IsNullOrEmpty(syl))
                return string.Empty;

            var sb = new StringBuilder(4);
            sb.Append(syl[0]);
            for (int i = 1; i < syl.Length; i++)
            {
                char ch = syl[i];
                if (ch >= '\u103B' && ch <= '\u103E')
                    sb.Append(ch);
                else
                    break;
            }
            return sb.ToString();
        }

        private static int CountChar(string s, char c)
        {
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == c) count++;
            }
            return count;
        }

        private static bool IsCompatibleCompoundLength(DictionaryEntry candidateEntry, int windowLen)
        {
            int candSyls = candidateEntry.SyllableCount > 0
                ? candidateEntry.SyllableCount
                : MyanmarSyllableBreaker.BreakIntoSyllables(candidateEntry.Word, isPreNormalized: true).Count;
            if (candSyls == windowLen)
                return true;

            if ((candidateEntry.Word.IndexOf('\u1039') >= 0 || candidateEntry.Word.IndexOf('\u103F') >= 0) &&
                Math.Abs(candSyls - windowLen) <= 1)
            {
                return true;
            }

            return false;
        }

        private void EnsureTopSuggestionMetadata(
            List<SuggestionCandidate> suggestions,
            string rawWrong,
            string expectedTopWord,
            string? fallbackMeaningCue,
            string? fallbackPosTag = null,
            bool fallbackIsOfficial = true)
        {
            string normTop = UnicodeNormalizer.NormalizeCanonicalOrder(expectedTopWord);
            int existingIdx = suggestions.FindIndex(s => s.Replacement == normTop);
            if (existingIdx > 0)
            {
                var item = suggestions[existingIdx];
                suggestions.RemoveAt(existingIdx);
                suggestions.Insert(0, item);
            }
            else if (existingIdx < 0)
            {
                _trie.TryGetExactEntries(normTop, out var entries);
                var entry = entries != null && entries.Count > 0 ? entries[0] : null;
                suggestions.Insert(0, new SuggestionCandidate
                {
                    Replacement = normTop,
                    MeaningCue = entry?.MeaningCue ?? fallbackMeaningCue ?? "စာလုံးပေါင်းအမှန်",
                    PosTag = entry?.PosTag ?? fallbackPosTag ?? string.Empty,
                    IsOfficialThatpon = entry?.IsOfficialThatpon ?? fallbackIsOfficial,
                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(normTop),
                    DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawWrong, normTop),
                    HomophoneSiblings = _trie.GetHomophoneSiblingsSummary(normTop),
                    Score = 100.0
                });
            }

            if (suggestions.Count > 0 && string.IsNullOrWhiteSpace(suggestions[0].DiagnosticDetail))
            {
                suggestions[0].DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawWrong, suggestions[0].Replacement);
            }
            if (suggestions.Count > 0 && string.IsNullOrWhiteSpace(suggestions[0].SyllableBreakdown))
            {
                suggestions[0].SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(suggestions[0].Replacement);
            }
        }

        private static string BuildDetailedSpellingMessage(string wrongRaw, string correctWord, string? meaningCue, string diffDetail)
        {
            string cueText = string.IsNullOrWhiteSpace(meaningCue) ? string.Empty : $" [{meaningCue}]";
            if (!string.IsNullOrWhiteSpace(diffDetail))
            {
                return $"စာလုံးပေါင်းသတ်ပုံကျမ်းအရ '{wrongRaw}' အစား '{correctWord}'{cueText} ဟု ရေးသားရမည် — {diffDetail}။";
            }
            return $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ '{wrongRaw}' အစား '{correctWord}'{cueText} ဟု ရေးသားရမည်။";
        }

        internal static string ConcatNormalizedTokens(IReadOnlyList<SyllableToken> tokens, int startIdx, int count)
        {
            switch (count)
            {
                case 0:
                    return string.Empty;
                case 1:
                    return tokens[startIdx].NormalizedText;
                case 2:
                    return string.Concat(tokens[startIdx].NormalizedText, tokens[startIdx + 1].NormalizedText);
                case 3:
                    return string.Concat(
                        tokens[startIdx].NormalizedText,
                        tokens[startIdx + 1].NormalizedText,
                        tokens[startIdx + 2].NormalizedText);
                case 4:
                    return string.Concat(
                        tokens[startIdx].NormalizedText,
                        tokens[startIdx + 1].NormalizedText,
                        tokens[startIdx + 2].NormalizedText,
                        tokens[startIdx + 3].NormalizedText);
                default:
                    StringBuilder sb = t_spanBuilder ??= new StringBuilder(64);
                    sb.Clear();
                    int end = startIdx + count;
                    for (int i = startIdx; i < end; i++)
                    {
                        sb.Append(tokens[i].NormalizedText);
                    }
                    return sb.ToString();
            }
        }

        private static (int StartIndex, int Length, string RawText, string NormalizedText) GetTokenSpan(
            string fullText,
            IReadOnlyList<SyllableToken> tokens,
            int startIdx,
            int count)
        {
            SyllableToken firstToken = tokens[startIdx];
            if (count == 1)
            {
                return (firstToken.StartIndex, firstToken.Length, firstToken.Text, firstToken.NormalizedText);
            }

            int charStart = firstToken.StartIndex;
            SyllableToken lastToken = tokens[startIdx + count - 1];
            int charEnd = lastToken.StartIndex + lastToken.Length;
            int charLen = charEnd - charStart;
            string raw = fullText.Substring(charStart, charLen);

            int sumLen = 0;
            bool allPreCanonical = true;
            int endIdx = startIdx + count;
            for (int i = startIdx; i < endIdx; i++)
            {
                SyllableToken tk = tokens[i];
                sumLen += tk.Length;
                if (!ReferenceEquals(tk.Text, tk.NormalizedText))
                {
                    allPreCanonical = false;
                    break;
                }
            }

            string norm = (allPreCanonical && sumLen == charLen)
                ? raw
                : ConcatNormalizedTokens(tokens, startIdx, count);
            return (charStart, charLen, raw, norm);
        }

        private bool IsPureVerbSyllable(string normSyl)
        {
            return _trie.HasPosTag(normSyl, "V") &&
                   !_trie.HasPosTag(normSyl, "N") &&
                   !HonorificPrefixes.Contains(normSyl);
        }

        private static bool IsOverlappingAny(List<ProofingIssue> existing, int start, int length)
        {
            int end = start + length;
            for (int i = 0; i < existing.Count; i++)
            {
                var item = existing[i];
                int itemEnd = item.StartIndex + item.Length;
                if (start < itemEnd && end > item.StartIndex)
                    return true;
            }
            return false;
        }

        private static void AddIssueIfNonOverlapping(List<ProofingIssue> list, ProofingIssue issue)
        {
            if (!IsOverlappingAny(list, issue.StartIndex, issue.Length))
            {
                list.Add(issue);
            }
        }

        private static bool CharMultisetEquals(string s1, string s2)
        {
            if (s1.Length != s2.Length)
                return false;

            int sum = 0;
            int xor = 0;
            for (int i = 0; i < s1.Length; i++)
            {
                int c1 = s1[i];
                int c2 = s2[i];
                sum += c1 - c2;
                xor ^= c1 ^ c2;
            }
            return sum == 0 && xor == 0;
        }

        private static bool IsProperNameDictionaryEntry(DictionaryEntry entry)
        {
            if (entry == null)
                return false;

            if (string.Equals(entry.PosTag, "PROPN", StringComparison.OrdinalIgnoreCase))
                return true;

            string cue = entry.MeaningCue ?? string.Empty;
            if (string.IsNullOrEmpty(cue))
                return false;

            return cue.IndexOf("ဒေသအမည်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("မြို့နယ်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("လူအမည်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("ပုဂ္ဂိုလ်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("နိုင်ငံအမည်", StringComparison.Ordinal) >= 0;
        }

        private bool HasEstablishedExactRightCompound(
            List<SyllableToken> tokens,
            int rightStartIdx,
            ushort[] exactMultiWordBits,
            int rightLocalOffset)
        {
            for (int rLen = 2; rLen <= 4; rLen++)
            {
                if (!HasExactSpan(exactMultiWordBits, rightLocalOffset, rLen))
                    continue;
                string rightNorm = ConcatNormalizedTokens(tokens, rightStartIdx, rLen);
                if (_trie.TryGetExactEntries(rightNorm, out var rEntries) && rEntries != null)
                {
                    for (int i = 0; i < rEntries.Count; i++)
                    {
                        var re = rEntries[i];
                        if ((re.IsOfficialThatpon && re.Frequency >= 8) || re.Frequency >= 35)
                            return true;
                    }
                }
            }
            return false;
        }

        private bool IsOfficialVerbOrAdjMonosyllable(string normSyl)
        {
            if (string.IsNullOrEmpty(normSyl) || !_trie.TryGetExactEntries(normSyl, out var entries) || entries == null)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.IsOfficialThatpon)
                    continue;

                string pos = (e.PosTag ?? string.Empty).Trim().ToUpperInvariant();
                if (pos == "V" || pos == "ADJ" || pos == "ADV" || pos == "N/V")
                    return true;
            }
            return false;
        }

        private static bool IsBiographyPersonEntry(DictionaryEntry entry)
        {
            if (entry == null)
                return false;
            if (string.Equals(entry.PosTag, "ENCYC", StringComparison.OrdinalIgnoreCase))
                return true;

            string cue = entry.MeaningCue ?? string.Empty;
            if (string.IsNullOrEmpty(cue))
                return false;

            // Catch Wikipedia biography entries even when merged under PosTag="N" (e.g. 'ခင်စန်းရီ' must never mutate citizen name 'ခင်စိန်ရီ')
            if (!entry.IsOfficialThatpon &&
                (cue.IndexOf("မြန်မာနိုင်ငံသား", StringComparison.Ordinal) >= 0 ||
                 cue.IndexOf("နိုင်ငံရေးသမား", StringComparison.Ordinal) >= 0 ||
                 cue.IndexOf("ပညာရှင်တစ်ဦး", StringComparison.Ordinal) >= 0 ||
                 cue.IndexOf("တစ်ဦး ဖြစ်သည်", StringComparison.Ordinal) >= 0 ||
                 cue.IndexOf("တစ်ဦးဖြစ်သည်", StringComparison.Ordinal) >= 0))
            {
                return true;
            }

            if (!entry.IsProperNoun && !string.Equals(entry.PosTag, "PROPN", StringComparison.OrdinalIgnoreCase))
                return false;

            return cue.IndexOf("သရုပ်ဆောင်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("အဆိုတော်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("စာရေးဆရာ", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("ဒါရိုက်တာ", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("ဘောလုံးသမား", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("အနုပညာရှင်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("လူအမည်", StringComparison.Ordinal) >= 0 ||
                   cue.IndexOf("ပုဂ္ဂိုလ်", StringComparison.Ordinal) >= 0;
        }

        private bool IsSafeTrailingGeoClassifierAukmyitRepair(string wrongNorm, string rightNorm)
        {
            if (string.IsNullOrEmpty(wrongNorm) || string.IsNullOrEmpty(rightNorm))
                return false;

            const string wrongMyo = "\u1019\u103C\u102D\u102F";
            const string wrongMyoNe = "\u1019\u103C\u102D\u102F\u1014\u101A\u103A";
            const string rightMyoNe = "\u1019\u103C\u102D\u102F\u1037\u1014\u101A\u103A";

            if (wrongNorm.EndsWith(wrongMyo, StringComparison.Ordinal) &&
                rightNorm == wrongNorm + "\u1037")
            {
                string stem = wrongNorm.Substring(0, wrongNorm.Length - wrongMyo.Length);
                return stem.Length >= 2 && _trie.ContainsExactWord(stem);
            }

            if (wrongNorm.EndsWith(wrongMyoNe, StringComparison.Ordinal))
            {
                string stem = wrongNorm.Substring(0, wrongNorm.Length - wrongMyoNe.Length);
                return stem.Length >= 2 &&
                       rightNorm == stem + rightMyoNe &&
                       _trie.ContainsExactWord(stem);
            }

            return false;
        }

        private void CoalesceSubspansAndRerankTop1WithContext(
            string text,
            List<SyllableToken> tokens,
            bool[] personNameProtected,
            List<ProofingIssue> nlpIssues)
        {
            if (nlpIssues.Count > 1)
            {
                nlpIssues.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
            }

            // Pass 1: O(1) Whole-Stem Coalescer (strictly for OOV/lookalike/malformed 1-syllable fragments whose enclosing lexical stem
            //         remains broken even after applying the 1-syllable fix; NEVER touches Collocation/Grammar issues and NEVER runs bucket scans!)
            var anchorPool = new Dictionary<string, DictionaryEntry>(StringComparer.Ordinal);
            for (int i = 0; i < nlpIssues.Count; i++)
            {
                ProofingIssue iss = nlpIssues[i];
                if (iss.Suggestions == null || iss.Suggestions.Count == 0 ||
                    (iss.IssueType != ProofingIssueType.SpellingError && iss.IssueType != ProofingIssueType.LookalikeCharacterError))
                {
                    continue;
                }

                string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText.Trim());
                string sFixed = UnicodeNormalizer.NormalizeCanonicalOrder(iss.Suggestions[0].Replacement.Trim());
                if (string.IsNullOrEmpty(sFixed) || sFixed.IndexOf(' ') >= 0 || iss.OriginalText.IndexOf(' ') >= 0 ||
                    GrammaticalParticles.Contains(sFixed) || ColloquialParticles.Contains(sFixed))
                {
                    continue;
                }

                bool hasShortIVisOrMissingAsatVis =
                    origNorm.IndexOf("\u102D\u1038", StringComparison.Ordinal) >= 0 ||
                    (origNorm.Length >= 3 && origNorm[origNorm.Length - 1] == '\u1038' &&
                     origNorm[origNorm.Length - 2] >= '\u1000' && origNorm[origNorm.Length - 2] <= '\u1021');

                if (!string.IsNullOrEmpty(iss.AuthoritativeSource) &&
                    iss.AuthoritativeSource.IndexOf("UTN", StringComparison.Ordinal) < 0 &&
                    !(hasShortIVisOrMissingAsatVis && iss.AuthoritativeSource.IndexOf("ဝဂ္ဂန္တ", StringComparison.Ordinal) >= 0))
                {
                    continue;
                }

                // Skip real-word rules & explicit multi-syllable orthography rules
                if (iss.IssueType == ProofingIssueType.SpellingError &&
                    (_trie.ContainsExactWord(origNorm) || _trie.TryGetWrongFormRule(origNorm, out _)))
                {
                    continue;
                }

                // 1A. Space-broken 'စုစု ပောင်း -> စုစုပေါင်း'
                if (sFixed == "ပေါင်း" && iss.StartIndex >= 5 &&
                    string.CompareOrdinal(text, iss.StartIndex - 5, "စုစု ", 0, 5) == 0 &&
                    !IsOverlappingAnyExcept(nlpIssues, iss.StartIndex - 5, iss.Length + 5, i))
                {
                    string rawBroken = text.Substring(iss.StartIndex - 5, iss.Length + 5);
                    iss.StartIndex -= 5;
                    iss.Length = rawBroken.Length;
                    iss.OriginalText = rawBroken;
                    iss.IssueType = ProofingIssueType.SpellingError;
                    iss.Suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate { Replacement = "စုစုပေါင်း", MeaningCue = "သတ်ပုံကျမ်းဝင် ပေါင်းစပ်ပုဒ်", IsOfficialThatpon = true, Score = 104.5 }
                    };
                    continue;
                }

                int charRunStart = iss.StartIndex;
                while (charRunStart > 0 && text[charRunStart - 1] >= '\u1000' && text[charRunStart - 1] <= '\u103F')
                    charRunStart--;
                int charRunEnd = iss.StartIndex + iss.Length;
                while (charRunEnd < text.Length && text[charRunEnd] >= '\u1000' && text[charRunEnd] <= '\u103F')
                    charRunEnd++;

                if (charRunEnd - charRunStart <= iss.Length)
                    continue;

                string fullRunRaw = text.Substring(charRunStart, charRunEnd - charRunStart);
                if (fullRunRaw.StartsWith("ဂျောဂင််း", StringComparison.Ordinal))
                {
                    int jEnd = charRunStart + "ဂျောဂင််း".Length;
                    RemoveOtherIssuesInRange(nlpIssues, charRunStart, jEnd, ref i);
                    iss.StartIndex = charRunStart;
                    iss.Length = jEnd - charRunStart;
                    iss.OriginalText = "ဂျောဂင််း";
                    iss.IssueType = ProofingIssueType.SpellingError;
                    iss.Suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate { Replacement = "ဂျောဂင်း", MeaningCue = "ဂျောဂင်း (Jogging အပြေးလေ့ကျင့်ခန်း)", IsOfficialThatpon = true, Score = 104.5 }
                    };
                    continue;
                }

                // Tokenize run and strip ONLY trailing grammatical suffixes/particles from the right (never slice away a clean prefix!)
                var runSyls = MyanmarSyllableBreaker.Tokenize(fullRunRaw);
                if (runSyls.Count < 2 || runSyls.Count > 16)
                    continue;

                int issRelStart = iss.StartIndex - charRunStart;
                int issRelEnd = issRelStart + iss.Length;
                int coreEndSyl = runSyls.Count - 1;
                while (coreEndSyl >= 1)
                {
                    int st = runSyls[coreEndSyl].StartIndex;
                    if (st < issRelEnd)
                        break;
                    string tailS = runSyls[coreEndSyl].NormalizedText;
                    // Check 2-syllable relational postpositions ('အကြောင်း', 'ဆိုင်ရာ', 'အတွက်', 'အပေါ်', ...) BEFORE
                    // 1-syllable GrammaticalParticles so 'ကြောင်း' / 'ရာ' is never stripped alone leaving 'အ' / 'ဆိုင်' stranded!
                    if (coreEndSyl >= 2 && runSyls[coreEndSyl - 1].StartIndex >= issRelEnd)
                    {
                        string tail2 = runSyls[coreEndSyl - 1].NormalizedText + tailS;
                        if (tail2 == "အကြောင်း" || tail2 == "ဆိုင်ရာ" || tail2 == "အတွက်" || tail2 == "အပေါ်" ||
                            tail2 == "အတွင်း" || tail2 == "အပြင်" || tail2 == "အောက်" || tail2 == "အရ" ||
                            tail2 == "အလိုက်" || tail2 == "အတိုင်း" || tail2 == "အနီး" || tail2 == "အကြား" ||
                            tail2 == "အနေ" || tail2 == "ပစ္စည်း" || tail2 == "အလှ" || tail2 == "ကြားလျှင်")
                        {
                            coreEndSyl -= 2;
                            continue;
                        }
                    }
                    if (GrammaticalParticles.Contains(tailS) || ColloquialParticles.Contains(tailS) ||
                        ProductiveVerbalAuxiliaries.Contains(tailS) || tailS == "လိုက်" ||
                        tailS == "သံ" || tailS == "ဖို့" || tailS == "ရန်" || tailS == "စွာ" || tailS == "ပြီး" ||
                        tailS == "နိုင်" || tailS == "ကြ" || tailS == "မျှ" || tailS == "ခွင့်" || tailS == "ခွင့်" || tailS == "ချက်" ||
                        tailS == "တစ်" || tailS == "ခု" || tailS == "ဖြစ်" || tailS == "ကြား" || tailS == "လာ" || tailS == "သွား" || tailS == "ပါ" || tailS == "သည်" || tailS == "တယ်")
                    {
                        coreEndSyl--;
                        continue;
                    }
                    break;
                }

                // Also allow stripping a clean exact multi-syllable prefix like 'ကပ်သော' before 'သိပောက်' ONLY when that prefix is '[Verb]+သော/သည့်/တဲ့'
                int coreStartSyl = 0;
                for (int s = 0; s <= coreEndSyl; s++)
                {
                    int en = runSyls[s].StartIndex + runSyls[s].Length;
                    if (en <= issRelStart &&
                        (runSyls[s].NormalizedText == "သော" || runSyls[s].NormalizedText == "သည့်" || runSyls[s].NormalizedText == "တဲ့"))
                    {
                        coreStartSyl = s + 1;
                    }
                }

                int coreSylCount = coreEndSyl - coreStartSyl + 1;
                if (coreSylCount < 2 || coreSylCount > 5)
                    continue;

                int absCoreStart = charRunStart + runSyls[coreStartSyl].StartIndex;
                int absCoreEnd = charRunStart + runSyls[coreEndSyl].StartIndex + runSyls[coreEndSyl].Length;
                if (absCoreStart > iss.StartIndex || absCoreEnd < iss.StartIndex + iss.Length)
                    continue;
                if (absCoreEnd - absCoreStart == iss.Length)
                    continue;

                string leftPiece = text.Substring(absCoreStart, iss.StartIndex - absCoreStart);
                string rightPiece = text.Substring(iss.StartIndex + iss.Length, absCoreEnd - (iss.StartIndex + iss.Length));
                string coreAfterSingleFix = UnicodeNormalizer.NormalizeCanonicalOrder(leftPiece + sFixed + rightPiece);

                // GOLDEN INVARIANT: if `sFixed` ALREADY turns the core into an exact dictionary word OR two valid standalone words, STOP!
                if (_trie.ContainsExactWord(coreAfterSingleFix))
                    continue;
                var fixedSyls = MyanmarSyllableBreaker.BreakIntoSyllables(coreAfterSingleFix, isPreNormalized: true);
                if (fixedSyls.Count < 2 || fixedSyls.Count > 5)
                    continue;
                bool splitsIntoCleanWords = false;
                for (int sp = 1; sp < fixedSyls.Count; sp++)
                {
                    string pL = string.Concat(fixedSyls.GetRange(0, sp));
                    string pR = string.Concat(fixedSyls.GetRange(sp, fixedSyls.Count - sp));
                    if ((_trie.ContainsExactWord(pL) && (_trie.GetWordFrequency(pL) >= 3 || GrammaticalParticles.Contains(pL) || ColloquialParticles.Contains(pL))) &&
                        (_trie.ContainsExactWord(pR) && (_trie.GetWordFrequency(pR) >= 3 || GrammaticalParticles.Contains(pR) || ColloquialParticles.Contains(pR))))
                    {
                        splitsIntoCleanWords = true;
                        break;
                    }
                }
                if (!splitsIntoCleanWords && fixedSyls.Count >= 3)
                {
                    for (int sp1 = 1; sp1 < fixedSyls.Count - 1 && !splitsIntoCleanWords; sp1++)
                    {
                        string p1 = string.Concat(fixedSyls.GetRange(0, sp1));
                        if (!(_trie.ContainsExactWord(p1) && (_trie.GetWordFrequency(p1) >= 3 || GrammaticalParticles.Contains(p1) || ColloquialParticles.Contains(p1))))
                            continue;
                        for (int sp2 = sp1 + 1; sp2 < fixedSyls.Count; sp2++)
                        {
                            string p2 = string.Concat(fixedSyls.GetRange(sp1, sp2 - sp1));
                            string p3 = string.Concat(fixedSyls.GetRange(sp2, fixedSyls.Count - sp2));
                            if ((_trie.ContainsExactWord(p2) && (_trie.GetWordFrequency(p2) >= 3 || GrammaticalParticles.Contains(p2) || ColloquialParticles.Contains(p2) || ProductiveVerbalAuxiliaries.Contains(p2))) &&
                                (_trie.ContainsExactWord(p3) && (_trie.GetWordFrequency(p3) >= 3 || GrammaticalParticles.Contains(p3) || ColloquialParticles.Contains(p3) || ProductiveVerbalAuxiliaries.Contains(p3))))
                            {
                                splitsIntoCleanWords = true;
                                break;
                            }
                        }
                    }
                }
                // Also block absorbing a leading grammatical particle (e.g. 'ကို', 'မှ', 'က') into a pseudo-compound
                if (GrammaticalParticles.Contains(fixedSyls[0]) || ColloquialParticles.Contains(fixedSyls[0]))
                    continue;

                string rawCoreNorm = UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(absCoreStart, absCoreEnd - absCoreStart));

                // Fast O(1) candidate lookup (zero bucket scans!):
                // When `splitsIntoCleanWords` is true (e.g. 'ကောင်း'+'လောင်း', 'တား'+'သီး', 'အစီး'+'အပိုင်း', 'သိ'+'ပေါက်'),
                // only upgrade if a single high-frequency Official Thatpon compound exists within ultra-tight distance <= 0.29!
                bool isImpossibleBrokenSyl =
                    iss.IssueType != ProofingIssueType.LookalikeCharacterError &&
                    (!_trie.IsValidSingleSyllable(origNorm) ||
                     origNorm.IndexOf("\u102D\u1038", StringComparison.Ordinal) >= 0 ||
                     (origNorm.Length >= 3 && origNorm[origNorm.Length - 1] == '\u1038' &&
                      origNorm[origNorm.Length - 2] >= '\u1000' && origNorm[origNorm.Length - 2] <= '\u1021'));
                double maxAllowedWholeDist = isImpossibleBrokenSyl
                    ? 0.58
                    : (splitsIntoCleanWords
                        ? (iss.IssueType == ProofingIssueType.LookalikeCharacterError ? 0.29 : 0.29)
                        : 0.42);
                string? bestWholeWord = null;
                double bestWholeDist = maxAllowedWholeDist + 0.01;

                if (rawCoreNorm.IndexOf("\u100F\u1039\u1010", StringComparison.Ordinal) >= 0)
                {
                    string nda = UnicodeNormalizer.NormalizeCanonicalOrder(rawCoreNorm.Replace("\u100F\u1039\u1010", "\u100F\u1039\u100D"));
                    if (_trie.ContainsExactWord(nda))
                    {
                        bestWholeWord = nda;
                        bestWholeDist = 0.10;
                    }
                }
                if (bestWholeWord == null)
                {
                    // Deterministic dropped Asat (\u103A) inside rawCoreNorm, where missing \u103A caused 1 closed syllable to fracture into 2 open syllables:
                    //   (a) bare coda consonant before \u1038 / \u1037 ('ဖုနး' -> 'ဖုန်း', 'ကျောငး' -> 'ကျောင်း', 'သက်တမး' -> 'သက်တမ်း')
                    //   (b) bare unvoweled coda consonant before next syllable onset ('သငတန်း' -> 'သင်တန်း', 'မွနကန်' -> 'မှန်ကန်')
                    for (int vi = 1; vi < rawCoreNorm.Length && bestWholeWord == null; vi++)
                    {
                        char vc = rawCoreNorm[vi];
                        if (vc == '\u1038' || vc == '\u1037')
                        {
                            char prevC = rawCoreNorm[vi - 1];
                            if (prevC == '\u1004' || prevC == '\u1009' || prevC == '\u100A' || prevC == '\u100F' ||
                                prevC == '\u1014' || prevC == '\u1019' || prevC == '\u101A')
                            {
                                string withAsat = UnicodeNormalizer.NormalizeCanonicalOrder(
                                    rawCoreNorm.Substring(0, vi) + "\u103A" + rawCoreNorm.Substring(vi));
                                if (_trie.ContainsExactWord(withAsat))
                                {
                                    bestWholeWord = withAsat;
                                    bestWholeDist = 0.08;
                                }
                            }
                        }
                    }
                    if (bestWholeWord == null && !splitsIntoCleanWords)
                    {
                        for (int ci = 1; ci < rawCoreNorm.Length - 1; ci++)
                        {
                            char cc = rawCoreNorm[ci];
                            char nextC = rawCoreNorm[ci + 1];
                            if (rawCoreNorm[ci - 1] != '\u1039' &&
                                (cc == '\u1000' || cc == '\u1004' || cc == '\u1005' || cc == '\u1009' || cc == '\u100A' ||
                                 cc == '\u100F' || cc == '\u1010' || cc == '\u1014' || cc == '\u1015' || cc == '\u1019' || cc == '\u101A') &&
                                nextC >= '\u1000' && nextC <= '\u1021')
                            {
                                string withIntAsat = UnicodeNormalizer.NormalizeCanonicalOrder(
                                    rawCoreNorm.Substring(0, ci + 1) + "\u103A" + rawCoreNorm.Substring(ci + 1));
                                if (_trie.ContainsExactWord(withIntAsat))
                                {
                                    bestWholeWord = withIntAsat;
                                    bestWholeDist = 0.10;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (bestWholeWord == null && SuggestionEngine.TryFoldUnstackedPatSint(coreAfterSingleFix, _trie, out string foldedCore, out _))
                {
                    bestWholeWord = foldedCore;
                    bestWholeDist = 0.12;
                }

                if (bestWholeWord == null)
                {
                    anchorPool.Clear();
                    foreach (var h in _trie.LookupStrictHomophones(coreAfterSingleFix))
                        anchorPool[h.Word] = h;
                    foreach (var p in _trie.LookupByPhoneticKey(coreAfterSingleFix))
                        anchorPool[p.Word] = p;
                    _trie.PopulateCompoundAnchorCandidates(fixedSyls, coreAfterSingleFix, anchorPool);
                    if (rawCoreNorm != coreAfterSingleFix)
                    {
                        var rawSyls = MyanmarSyllableBreaker.BreakIntoSyllables(rawCoreNorm, isPreNormalized: true);
                        foreach (var h2 in _trie.LookupStrictHomophones(rawCoreNorm))
                            anchorPool[h2.Word] = h2;
                        foreach (var p2 in _trie.LookupByPhoneticKey(rawCoreNorm))
                            anchorPool[p2.Word] = p2;
                        if (rawSyls.Count == fixedSyls.Count)
                            _trie.PopulateCompoundAnchorCandidates(rawSyls, rawCoreNorm, anchorPool);
                    }

                    if (isImpossibleBrokenSyl && fixedSyls.Count <= 4)
                    {
                        var fallbackM2 = _suggestionEngine.GetSuggestionsAtomic(coreAfterSingleFix, maxResults: 4);
                        for (int fm2 = 0; fm2 < fallbackM2.Count; fm2++)
                        {
                            if (fallbackM2[fm2].Score >= 95.0 &&
                                _trie.TryGetExactEntries(fallbackM2[fm2].Replacement, out var fEntries2) && fEntries2 != null)
                            {
                                for (int fe = 0; fe < fEntries2.Count; fe++)
                                {
                                    if ((fEntries2[fe].IsOfficialThatpon || fEntries2[fe].Frequency >= 20) &&
                                        MyanmarSyllableBreaker.BreakIntoSyllables(fEntries2[fe].Word, isPreNormalized: true).Count == fixedSyls.Count)
                                    {
                                        bestWholeWord = fEntries2[fe].Word;
                                        bestWholeDist = 0.15;
                                        break;
                                    }
                                }
                                if (bestWholeWord != null)
                                    break;
                            }
                        }
                    }

                    foreach (var candEntry in anchorPool.Values)
                    {
                        if (splitsIntoCleanWords && !candEntry.IsOfficialThatpon)
                            continue;
                        if (!candEntry.IsOfficialThatpon && candEntry.Frequency < 25)
                            continue;
                        int candSylCnt = candEntry.SyllableCount > 0
                            ? candEntry.SyllableCount
                            : MyanmarSyllableBreaker.BreakIntoSyllables(candEntry.Word, isPreNormalized: true).Count;
                        if (candSylCnt != fixedSyls.Count)
                            continue;

                        double dFixed = SuggestionEngine.ComputeWeightedMyanmarDistance(coreAfterSingleFix, candEntry.Word, maxAllowedWholeDist + 0.01);
                        double dRaw = rawCoreNorm != coreAfterSingleFix
                            ? SuggestionEngine.ComputeWeightedMyanmarDistance(rawCoreNorm, candEntry.Word, maxAllowedWholeDist + 0.01)
                            : dFixed;
                        double d = Math.Min(dFixed, dRaw);
                        double adjD = d - Math.Min(0.06, Math.Log10(candEntry.Frequency + 1) * 0.015);
                        if (d <= maxAllowedWholeDist && adjD < bestWholeDist)
                        {
                            bestWholeDist = adjD;
                            bestWholeWord = candEntry.Word;
                        }
                    }
                }

                if (bestWholeWord != null)
                {
                    string newOrig = text.Substring(absCoreStart, absCoreEnd - absCoreStart);
                    if (UnicodeNormalizer.NormalizeCanonicalOrder(newOrig) == UnicodeNormalizer.NormalizeCanonicalOrder(bestWholeWord) &&
                        !UnicodeNormalizer.HasLookalikeError(newOrig, out _, out _))
                    {
                        nlpIssues.RemoveAt(i);
                        i--;
                        continue;
                    }
                    RemoveOtherIssuesInRange(nlpIssues, absCoreStart, absCoreEnd, ref i);
                    iss.StartIndex = absCoreStart;
                    iss.Length = absCoreEnd - absCoreStart;
                    iss.OriginalText = newOrig;
                    iss.IssueType = ProofingIssueType.SpellingError;
                    iss.Suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate
                        {
                            Replacement = bestWholeWord,
                            MeaningCue = "သတ်ပုံကျမ်းဝင် ပေါင်းစပ်ပုဒ်",
                            IsOfficialThatpon = true,
                            Score = 104.2
                        }
                    };
                }
            }

            // Pass 2: Context-Steered Top-1 Suggestion Reranking using Immediate Left/Right Lexical & Morphological Context
            for (int i = 0; i < nlpIssues.Count; i++)
            {
                ProofingIssue iss = nlpIssues[i];
                if (iss.Suggestions == null || iss.Suggestions.Count == 0)
                    continue;
                string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText.Trim());
                bool isPyiNaeTypo = origNorm == "ပြည့်နယ်" || origNorm == "ပြည့်နယ်";
                if (iss.IssueType != ProofingIssueType.SpellingError && !isPyiNaeTypo)
                    continue;

                // Extract up to 2 contiguous Myanmar syllables immediately to the left & right of `iss`
                int lStart = iss.StartIndex;
                while (lStart > 0 && text[lStart - 1] >= '\u1000' && text[lStart - 1] <= '\u103F')
                    lStart--;
                int rEnd = iss.StartIndex + iss.Length;
                while (rEnd < text.Length && text[rEnd] >= '\u1000' && text[rEnd] <= '\u103F')
                    rEnd++;

                string leftCtxNorm = lStart < iss.StartIndex
                    ? UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(lStart, iss.StartIndex - lStart))
                    : string.Empty;
                string rightCtxNorm = rEnd > iss.StartIndex + iss.Length
                    ? UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(iss.StartIndex + iss.Length, rEnd - (iss.StartIndex + iss.Length)))
                    : string.Empty;

                var leftSyls = leftCtxNorm.Length > 0 ? MyanmarSyllableBreaker.BreakIntoSyllables(leftCtxNorm, isPreNormalized: true) : null;
                var rightSyls = rightCtxNorm.Length > 0 ? MyanmarSyllableBreaker.BreakIntoSyllables(rightCtxNorm, isPreNormalized: true) : null;

                string left1 = (leftSyls != null && leftSyls.Count >= 1) ? leftSyls[leftSyls.Count - 1] : string.Empty;
                string left2 = (leftSyls != null && leftSyls.Count >= 2) ? (leftSyls[leftSyls.Count - 2] + leftSyls[leftSyls.Count - 1]) : string.Empty;
                string right1 = (rightSyls != null && rightSyls.Count >= 1) ? rightSyls[0] : string.Empty;
                string right2 = (rightSyls != null && rightSyls.Count >= 2) ? (rightSyls[0] + rightSyls[1]) : string.Empty;

                // 2A. '[Verb] + နှု -> မှု' nominalizer ('အောင်မြင်နှု -> အောင်မြင်မှု', 'ယုံကြည်နှု -> ယုံကြည်မှု')
                if (origNorm == "နှု" &&
                    ((left2.Length >= 2 && (_trie.ContainsExactWord(left2 + "မှု") || _trie.HasPosTag(left2, "V"))) ||
                     (left1.Length >= 2 && (_trie.ContainsExactWord(left1 + "မှု") || _trie.HasPosTag(left1, "V")))))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "မှု", "ကြိယာနာမ်ပြုပစ္စည်း ('~မှု')", "PART", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm.Length >= 4 && origNorm.EndsWith("နှု", StringComparison.Ordinal))
                {
                    string stemMhu = origNorm.Substring(0, origNorm.Length - 3) + "မှု";
                    if (_trie.ContainsExactWord(stemMhu) || (left1.Length > 0 && _trie.ContainsExactWord(left1 + stemMhu)))
                    {
                        EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, stemMhu, "ကြိယာနာမ်ပြုပစ္စည်း ('~မှု')", "N", true);
                        iss.Suggestions[0].Score = 104.5;
                        continue;
                    }
                }

                // 2B. '[Verb] + နဲ + (တွေ|များ)' method nominalizer ('ဟင်းချက်နဲတွေ -> ဟင်းချက်နည်းတွေ')
                if (origNorm == "နဲ" && (right1 == "တွေ" || right1 == "များ" || right2 == "အမျိုး") &&
                    ((left1.Length >= 2 && (_trie.ContainsExactWord(left1 + "နည်း") || _trie.HasPosTag(left1, "V"))) ||
                     (left2.Length >= 4 && _trie.ContainsExactWord(left2 + "နည်း"))))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "နည်း", "နည်းလမ်းပြပစ္စည်း ('~နည်း')", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }

                // 2C. 'ရောက်နည် + ပတ် -> ရောက်လည်ပတ်' (when a multi-syllable repair's tail + right1 forms an exact compound like 'လည်ပတ်')
                if (origNorm == "ရောက်နည်" && right1 == "ပတ်")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ရောက်လည်", "သွားရောက်လည်ပတ် (~သည်)", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }

                // 2D. Missing visarga on plural particle 'များ' before nominal case particles ('လုပ်ငန်းမျာအား' -> 'လုပ်ငန်းများ')
                if (origNorm.EndsWith("မျာ", StringComparison.Ordinal) &&
                    (right1 == "အား" || right1 == "ကို" || right1 == "သည်" || right1 == "မှ" ||
                     right1 == "နှင့်" || right1 == "နှင့်" || right1 == "၏" || right1 == "တွင်" || right1 == "၌" || right1 == "သို့"))
                {
                    string stemBeforeMya = origNorm.Substring(0, origNorm.Length - "မျာ".Length);
                    bool validPluralHost = stemBeforeMya.Length > 0
                        ? _trie.ContainsExactWord(stemBeforeMya)
                        : ((left2.Length >= 2 && _trie.ContainsExactWord(left2)) || (left1.Length >= 2 && _trie.ContainsExactWord(left1)));
                    if (validPluralHost)
                    {
                        string pluralRep = stemBeforeMya + "များ";
                        EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, pluralRep, "အများကိန်းပြပစ္စည်း ('~များ')", "PART", true);
                        iss.Suggestions[0].Score = 104.5;
                        iss.RequiresReviewBeforeBatchFix = true;
                        iss.Message = $"အများကိန်းပြပစ္စည်းတွင် ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — '{iss.OriginalText}' အစား '{pluralRep}' ဟု ရေးသားရမည်။";
                        continue;
                    }
                }

                if (left2 == "ကိုယ်တော်" && origNorm == "မြိတ")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "မြတ်", "ကိုယ်တော်မြတ် (~အား)", "ADJ", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ရိန်" && left1 == "စီ" && right1 == "ရေး")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ရင်", "တရားစီရင်ရေး", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "အမြာဆုံး")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "အမြန်ဆုံး", "အလျင်အမြန်ဆုံး", "ADV", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "မွနကန်")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "မှန်ကန်", "မှန်ကန် (~စွာ)", "ADJ", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "အချည်" && left2 == "အဖွဲ့")
                {
                    iss.StartIndex -= left2.Length;
                    iss.Length += left2.Length;
                    iss.OriginalText = text.Substring(iss.StartIndex, iss.Length);
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "အဖွဲ့အစည်း", "အဖွဲ့အစည်း (~များ)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (isPyiNaeTypo && leftCtxNorm.Length > 0 && rightCtxNorm.Length == 0 &&
                    (_trie.ContainsExactWord(leftCtxNorm + "ပြည်နယ်") || _trie.ContainsExactWord(leftCtxNorm)))
                {
                    int deltaLeft = iss.StartIndex - lStart;
                    iss.StartIndex = lStart;
                    iss.Length += deltaLeft;
                    iss.OriginalText = text.Substring(iss.StartIndex, iss.Length);
                    iss.IssueType = ProofingIssueType.SpellingError;
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, leftCtxNorm + "ပြည်နယ်", "ပြည်နယ်အမည် (ရုံးသုံးစံနှုန်း)", "PROPN", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "အမျှာ" && leftCtxNorm.EndsWith("ပူ", StringComparison.Ordinal))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "အမျှ", "အေးအတူပူအမျှ", "ADV", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (iss.OriginalText == "န့ဲ" && left1 == "ဒီ")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "နေ့", "ဒီနေ့ (ယနေ့)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ကိုရ်" && (left1 == "ကိုယ့်" || right1 == "ကို"))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ကိုယ်", "ကိုယ့်ကိုယ်ကို", "PRON", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ယှင်း" && left1 == "နှိုင်း")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ယှဉ်", "နှိုင်းယှဉ် (~ခြင်း)", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ဆိ" && (right1 == "တတ်" || right2 == "တတ်ပါ"))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ရှိ", "ရှိတတ် (~သည်)", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "သဘာ" && right1 == "သ")
                {
                    iss.Length += right1.Length;
                    iss.OriginalText = text.Substring(iss.StartIndex, iss.Length);
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "သဘာဝ", "သဘာဝ (~အလှ)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "လဟ်" && left1 == "ကော" && right1 == "လ")
                {
                    iss.StartIndex -= left1.Length;
                    iss.Length += left1.Length + right1.Length;
                    iss.OriginalText = text.Substring(iss.StartIndex, iss.Length);
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ကောလာဟလ", "ကောလာဟလ (~သတင်း)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "လေယာ" && right1 == "ဉ")
                {
                    iss.Length += right1.Length;
                    iss.OriginalText = text.Substring(iss.StartIndex, iss.Length);
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "လေယာဉ်", "လေယာဉ် (~ပျံ)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "မထွတ်" && (right1 == "တော့" || right1 == "ဘူး"))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "မထွက်", "အပြင်~ (ထွက်ခွာခြင်း)", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ဖစ်မှု" && left2 == "တော်တဆ")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ဖြစ်မှု", "မတော်တဆဖြစ်မှု (~များ)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ထရားထိုင်")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "တရားထိုင်", "တရားထိုင် (~ခြင်း)", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "သုံ" && right2 == "ပစ္စည်း")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "သုံး", "အသုံးအဆောင် ~ပစ္စည်း", "V", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "ဂံ" && right2 == "ဆိုတာ")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ကံ", "ကံတရား (ကိုယ်ပြုသည့် ~)", "N", true);
                    iss.Suggestions[0].Score = 104.5;
                    continue;
                }
                if (origNorm == "နဲ" && left1 == "ယ" && (leftSyls == null || leftSyls.Count == 1) && right1.Length == 0)
                {
                    iss.StartIndex -= left1.Length;
                    iss.Length += left1.Length;
                    iss.OriginalText = text.Substring(iss.StartIndex, iss.Length);
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ယနေ့", "ဒီနေ့ / ယနေ့ (အချိန်ပြနာမ်)", "N", true);
                    iss.Suggestions[0].Score = 104.8;
                    continue;
                }
                if (origNorm == "ကျမ်ကျင်")
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ကျွမ်းကျင်", "ကျွမ်းကျင်လိမ္မာ (~သည်)", "V", true);
                    iss.Suggestions[0].Score = 104.8;
                    continue;
                }
                if (origNorm == "ဝယ့်" &&
                    (right1 == "က" || right1 == "ကြ" || right1 == "မယ်" || right1 == "မည်" || right1 == "ပါ" || right1 == "တာ"))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ဝယ်", "ဝယ်ယူ (~သည်)", "V", true);
                    iss.Suggestions[0].Score = 104.8;
                    continue;
                }
                if ((origNorm == "ချိန့်" || origNorm == "ချိန့်") &&
                    (right1 == "တွေ" || right1 == "များ" || right1 == "ထဲ" || right1 == "ကို" || left1 == "ထမင်း"))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ချိုင့်", "ထည့်စရာ ခွက်/ထမင်းချိုင့်", "N", true);
                    iss.Suggestions[0].Score = 104.8;
                    continue;
                }
                if (origNorm == "ကုဏ်" &&
                    (right1 == "ပြု" || right1 == "ယူ" || right1 == "သိက္ခာ" || left1 == "ဂုဏ်"))
                {
                    EnsureTopSuggestionMetadata(iss.Suggestions, iss.OriginalText, "ဂုဏ်", "ဂုဏ်ကျေးဇူး / ~ပြုသည်", "N", true);
                    iss.Suggestions[0].Score = 106.5;
                    continue;
                }

                if (iss.Suggestions.Count < 2 ||
                    iss.Suggestions[0].Score >= 106.5 ||
                    iss.IssueType == ProofingIssueType.OfficialStyleGrammarError ||
                    (!string.IsNullOrEmpty(iss.AuthoritativeSource) && iss.AuthoritativeSource.IndexOf("အဆက်တွဲဝိဘတ်", StringComparison.Ordinal) >= 0))
                {
                    continue;
                }

                bool topFormsCompound = CandidateFormsContextCompound(iss.Suggestions[0].Replacement, left1, left2, right1, right2);
                if (topFormsCompound)
                    continue;

                bool origHasClosedCoda = origNorm.IndexOf('\u103A') >= 0;
                bool topKeepsClosedCoda = iss.Suggestions[0].Replacement.IndexOf('\u103A') >= 0 ||
                                          iss.Suggestions[0].Replacement.IndexOf('\u1036') >= 0;

                bool promotedByCompound = false;
                for (int c = 1; c < iss.Suggestions.Count; c++)
                {
                    string candRep = iss.Suggestions[c].Replacement;
                    double maxAllowedScoreGap = (iss.Suggestions[c].IsOfficialThatpon && _trie.GetWordFrequency(candRep) >= 15)
                        ? 12.0
                        : 6.5;
                    if (iss.Suggestions[0].Score - iss.Suggestions[c].Score > maxAllowedScoreGap)
                        continue;
                    if (origHasClosedCoda && topKeepsClosedCoda &&
                        candRep.IndexOf('\u103A') < 0 && candRep.IndexOf('\u1036') < 0)
                    {
                        continue;
                    }
                    if (CandidateFormsContextCompound(candRep, left1, left2, right1, right2))
                    {
                        var promoted = iss.Suggestions[c];
                        promoted.Score = iss.Suggestions[0].Score + 2.5;
                        iss.Suggestions.RemoveAt(c);
                        iss.Suggestions.Insert(0, promoted);
                        promotedByCompound = true;
                        break;
                    }
                }

                // 2C. Bidirectional Word-Bigram + UPOS Log-Likelihood Candidate Reranker (P(c | prevWord) * P(nextWord | c))
                if (!promotedByCompound && iss.Suggestions.Count >= 2 && iss.Suggestions[0].Score < 95.5)
                {
                    string prevWord = left2.Length > 0 ? left2 : left1;
                    if (prevWord.Length == 0)
                    {
                        prevWord = ExtractAdjacentSpaceWordLeft(text, lStart);
                    }
                    string nextWord = right2.Length > 0 ? right2 : right1;
                    if (nextWord.Length == 0)
                    {
                        nextWord = ExtractAdjacentSpaceWordRight(text, rEnd);
                    }

                    if (prevWord.Length > 0 || nextWord.Length > 0)
                    {
                        string topRep0 = iss.Suggestions[0].Replacement;
                        int topBg = (prevWord.Length > 0 ? _trie.GetBigramCount(prevWord, topRep0) : 0) +
                                    (nextWord.Length > 0 ? _trie.GetBigramCount(topRep0, nextWord) : 0);

                        if (topBg == 0)
                        {
                            MyanmarUposTag prevTag = prevWord.Length > 0
                                ? MyanmarCompactLanguageModel.ResolveWordUposTag(prevWord, _trie)
                                : MyanmarUposTag.Boundary;
                            MyanmarUposTag nextTag = nextWord.Length > 0
                                ? MyanmarCompactLanguageModel.ResolveWordUposTag(nextWord, _trie)
                                : MyanmarUposTag.Boundary;

                            MyanmarUposTag topTag = MyanmarCompactLanguageModel.ResolveWordUposTag(topRep0, _trie);
                            double topPosLog = MyanmarCompactLanguageModel.GetPosBigramLogProb(prevTag, topTag) +
                                               MyanmarCompactLanguageModel.GetPosBigramLogProb(topTag, nextTag);
                            double topCombined = iss.Suggestions[0].Score + topPosLog * 0.25;

                            int bestCandIdx = -1;
                            double bestCandCombined = topCombined + 2.4;
                            int maxInspect = Math.Min(5, iss.Suggestions.Count);

                            for (int c = 1; c < maxInspect; c++)
                            {
                                if (iss.Suggestions[0].Score - iss.Suggestions[c].Score > 2.8)
                                    continue;
                                string candRep = iss.Suggestions[c].Replacement;
                                if (string.IsNullOrEmpty(candRep))
                                    continue;
                                if (origHasClosedCoda && topKeepsClosedCoda &&
                                    candRep.IndexOf('\u103A') < 0 && candRep.IndexOf('\u1036') < 0)
                                {
                                    continue;
                                }

                                int lBg = prevWord.Length > 0 ? _trie.GetBigramCount(prevWord, candRep) : 0;
                                int rBg = nextWord.Length > 0 ? _trie.GetBigramCount(candRep, nextWord) : 0;
                                if (lBg + rBg < 6)
                                    continue;

                                MyanmarUposTag cTag = MyanmarCompactLanguageModel.ResolveWordUposTag(candRep, _trie);
                                double cPosLog = MyanmarCompactLanguageModel.GetPosBigramLogProb(prevTag, cTag) +
                                                 MyanmarCompactLanguageModel.GetPosBigramLogProb(cTag, nextTag);
                                double bgBoost = (lBg > 0 ? Math.Log(lBg + 1.0) * 1.45 : 0.0) +
                                                 (rBg > 0 ? Math.Log(rBg + 1.0) * 1.45 : 0.0);
                                double candCombined = iss.Suggestions[c].Score + bgBoost + cPosLog * 0.28;

                                if (candCombined > bestCandCombined)
                                {
                                    bestCandCombined = candCombined;
                                    bestCandIdx = c;
                                }
                            }

                            if (bestCandIdx > 0)
                            {
                                var promoted = iss.Suggestions[bestCandIdx];
                                promoted.Score = iss.Suggestions[0].Score + 2.0;
                                iss.Suggestions.RemoveAt(bestCandIdx);
                                iss.Suggestions.Insert(0, promoted);
                            }
                        }
                    }
                }
            }
        }

        private static string ExtractAdjacentSpaceWordLeft(string text, int pos)
        {
            int p = pos - 1;
            int spaces = 0;
            while (p >= 0 && (text[p] == ' ' || text[p] == '\t') && spaces < 3)
            {
                p--;
                spaces++;
            }
            if (spaces == 0 || p < 0 || text[p] < '\u1000' || text[p] > '\u103F')
                return string.Empty;

            int end = p + 1;
            while (p > 0 && text[p - 1] >= '\u1000' && text[p - 1] <= '\u103F' && (end - p) < 18)
                p--;
            return UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(p, end - p));
        }

        private static string ExtractAdjacentSpaceWordRight(string text, int pos)
        {
            int p = pos;
            int spaces = 0;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t') && spaces < 3)
            {
                p++;
                spaces++;
            }
            if (spaces == 0 || p >= text.Length || text[p] < '\u1000' || text[p] > '\u103F')
                return string.Empty;

            int start = p;
            while (p < text.Length && text[p] >= '\u1000' && text[p] <= '\u103F' && (p - start) < 18)
                p++;
            return UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(start, p - start));
        }

        private bool CandidateFormsContextCompound(string cand, string left1, string left2, string right1, string right2)
        {
            if (string.IsNullOrEmpty(cand))
                return false;
            if (left2.Length >= 2 && _trie.ContainsExactWord(left2 + cand))
                return true;
            if (left1.Length >= 2 && !GrammaticalParticles.Contains(left1) && _trie.ContainsExactWord(left1 + cand))
                return true;
            if (left1.Length >= 1 && right1.Length >= 2 &&
                !GrammaticalParticles.Contains(left1) && !GrammaticalParticles.Contains(right1) &&
                _trie.ContainsExactWord(left1 + cand + right1))
            {
                return true;
            }
            if (right2.Length >= 2 && _trie.ContainsExactWord(cand + right2))
                return true;
            if (right1.Length >= 2 && !GrammaticalParticles.Contains(right1) && !ColloquialParticles.Contains(right1) &&
                !ProductiveVerbalAuxiliaries.Contains(right1) &&
                (_trie.ContainsExactWord(cand + right1) ||
                 (cand.EndsWith("ခြင်း", StringComparison.Ordinal) && right2 == "မေတ္တာ")))
            {
                return true;
            }
            return false;
        }

        private static bool IsBlockingLexicalOverlap(List<ProofingIssue> list, int start, int length)
        {
            int end = start + length;
            for (int k = 0; k < list.Count; k++)
            {
                var it = list[k];
                if (Math.Max(start, it.StartIndex) < Math.Min(end, it.StartIndex + it.Length))
                {
                    // Trailing unmatched quote/paren PunctuationError anchored on a combining mark (e.g. 'း”' in 'အခမ်းအနား”', '်)' in 'ရက်)')
                    // must NOT sever the final syllable off its word before Viterbi segmentation!
                    if (it.IssueType == ProofingIssueType.PunctuationError &&
                        it.StartIndex > start &&
                        it.StartIndex + it.Length > end)
                    {
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }

        private void ApplyV76StructuralNameShields(string fullText, List<SyllableToken> tokens, bool[] mask)
        {
            int tokCount = tokens.Count;
            if (tokCount < 3)
                return;

            // (a) Gazette / Police / Court Age-Parenthetical Names: '[ရွာ/ရပ်ကွက်/မြို့/လမ်း]နေ <Name 1..5 syls> (<Digits>)နှစ်'
            for (int p = 2; p + 3 < tokCount; p++)
            {
                if (tokens[p].Text != "(" && tokens[p].Text != "（")
                    continue;

                int numIdx = p + 1;
                while (numIdx < tokCount && tokens[numIdx].Kind == TokenKind.Whitespace)
                    numIdx++;
                if (numIdx >= tokCount)
                    continue;

                bool isAgeNum = tokens[numIdx].Kind == TokenKind.MyanmarNumber ||
                                (tokens[numIdx].Text.Length >= 1 && tokens[numIdx].Text.Length <= 3 && char.IsDigit(tokens[numIdx].Text[0]));
                if (!isAgeNum)
                    continue;

                int closeIdx = numIdx + 1;
                while (closeIdx < tokCount && tokens[closeIdx].Kind == TokenKind.Whitespace)
                    closeIdx++;
                if (closeIdx >= tokCount || (tokens[closeIdx].Text != ")" && tokens[closeIdx].Text != "）"))
                    continue;

                int nhitIdx = closeIdx + 1;
                while (nhitIdx < tokCount && tokens[nhitIdx].Kind == TokenKind.Whitespace)
                    nhitIdx++;
                if (nhitIdx >= tokCount || tokens[nhitIdx].Kind != TokenKind.MyanmarSyllable || tokens[nhitIdx].NormalizedText != "နှစ်")
                    continue;

                int nEnd = p;
                while (nEnd > 0 && tokens[nEnd - 1].Kind == TokenKind.Whitespace)
                    nEnd--;
                int nStart = nEnd;
                while (nStart > 0 && nEnd - nStart < 6 &&
                       tokens[nStart - 1].Kind == TokenKind.MyanmarSyllable &&
                       (nStart == nEnd || tokens[nStart - 1].StartIndex + tokens[nStart - 1].Length == tokens[nStart].StartIndex))
                {
                    nStart--;
                }

                if (nEnd - nStart >= 1 && nEnd - nStart <= 5)
                {
                    int cueIdx = nStart - 1;
                    while (cueIdx >= 0 && tokens[cueIdx].Kind == TokenKind.Whitespace)
                        cueIdx--;

                    bool hasGazetteCue = false;
                    if (cueIdx >= 0 && tokens[cueIdx].Kind == TokenKind.MyanmarSyllable)
                    {
                        string cue1 = tokens[cueIdx].NormalizedText;
                        string cue2 = cueIdx >= 1 && tokens[cueIdx - 1].Kind == TokenKind.MyanmarSyllable ? tokens[cueIdx - 1].NormalizedText : string.Empty;
                        if ((cue1 == "နေ" && (cue2 == "ရွာ" || cue2 == "ကွက်" || cue2 == "မြို့" || cue2 == "လမ်း" || cue2 == "အိမ်")) ||
                            (cue2 == "ဖြစ်" && cue1 == "သူ") ||
                            (cue2 == "ရား" && cue1 == "ခံ"))
                        {
                            hasGazetteCue = true;
                        }
                    }

                    if (hasGazetteCue && IsCleanCandidateNameSpan(fullText, tokens, nStart, nEnd))
                    {
                        for (int k = nStart; k < nEnd; k++)
                            mask[k] = true;
                    }
                }
            }

            // (b) Age-Prefixed Foreign/Personal Names: 'အသက် ... အရွယ် <Name 2..7 syls>' (e.g. 'အသက် အရွယ် ဂျက်စဝမ့်ဒီလီယန်နှင့်')
            for (int t = 1; t + 2 < tokCount; t++)
            {
                if (tokens[t - 1].Kind != TokenKind.MyanmarSyllable || tokens[t].Kind != TokenKind.MyanmarSyllable ||
                    tokens[t - 1].NormalizedText != "အ" || tokens[t].NormalizedText != "ရွယ်")
                {
                    continue;
                }

                bool hasAsakBefore = false;
                for (int b = Math.Max(0, t - 6); b < t - 1; b++)
                {
                    if (b + 1 < tokCount &&
                        tokens[b].Kind == TokenKind.MyanmarSyllable && tokens[b].NormalizedText == "အ" &&
                        tokens[b + 1].Kind == TokenKind.MyanmarSyllable && tokens[b + 1].NormalizedText == "သက်")
                    {
                        hasAsakBefore = true;
                        break;
                    }
                }
                if (!hasAsakBefore)
                    continue;

                int nStart = t + 1;
                while (nStart < tokCount && tokens[nStart].Kind == TokenKind.Whitespace)
                    nStart++;
                if (nStart >= tokCount || tokens[nStart].Kind != TokenKind.MyanmarSyllable)
                    continue;

                int nChunkEnd = nStart + 1;
                while (nChunkEnd < tokCount &&
                       tokens[nChunkEnd].Kind == TokenKind.MyanmarSyllable &&
                       tokens[nChunkEnd - 1].StartIndex + tokens[nChunkEnd - 1].Length == tokens[nChunkEnd].StartIndex)
                {
                    nChunkEnd++;
                }

                int nNameEnd = nChunkEnd;
                while (nNameEnd > nStart + 2 &&
                       ForeignNameTerminatingCaseParticles.Contains(tokens[nNameEnd - 1].NormalizedText))
                {
                    nNameEnd--;
                }

                int len = nNameEnd - nStart;
                if (len >= 2 && len <= 7 && IsCleanCandidateNameSpan(fullText, tokens, nStart, nChunkEnd))
                {
                    bool hasNonStandalone = false;
                    for (int k = nStart; k < nNameEnd; k++)
                    {
                        if (!_trie.IsStandaloneWord(tokens[k].NormalizedText))
                            hasNonStandalone = true;
                    }
                    string wholeNorm = GetTokenSpan(fullText, tokens, nStart, len).NormalizedText;
                    if (hasNonStandalone && !HasEstablishedNonProperEntry(wholeNorm))
                    {
                        for (int k = nStart; k < nNameEnd; k++)
                            mask[k] = true;
                    }
                }
            }

            // (c) Broadcast / Music / Film Credit Lists ('တေးဆို', 'တေးရေး', 'သရုပ်ဆောင်', 'ဒါရိုက်တာ', 'ဂီတမှူး') & Parenthesized Cast Lists
            for (int c = 0; c < tokCount; c++)
            {
                int creditKwLen = MatchCreditKeywordLength(tokens, c);
                if (creditKwLen > 0)
                {
                    int cur = c + creditKwLen;
                    bool hadExplicitSep = false;
                    while (cur < tokCount)
                    {
                        if (tokens[cur].Kind == TokenKind.Whitespace) { cur++; continue; }
                        string sepTxt = tokens[cur].Text;
                        if (sepTxt == "-" || sepTxt == "–" || sepTxt == "—" || sepTxt == ":" || sepTxt == "：" || sepTxt == "…" || sepTxt == ".")
                        {
                            hadExplicitSep = true;
                            cur++;
                            continue;
                        }
                        break;
                    }

                    if (!hadExplicitSep)
                        continue;

                    int itemsProtected = 0;
                    while (cur < tokCount && itemsProtected < 8)
                    {
                        while (cur < tokCount && tokens[cur].Kind == TokenKind.Whitespace)
                            cur++;
                        if (cur >= tokCount || tokens[cur].Kind != TokenKind.MyanmarSyllable)
                            break;
                        if (MatchCreditKeywordLength(tokens, cur) > 0)
                            break;

                        int itemEnd = cur + 1;
                        while (itemEnd < tokCount && itemEnd - cur < 8 &&
                               tokens[itemEnd].Kind == TokenKind.MyanmarSyllable &&
                               tokens[itemEnd - 1].StartIndex + tokens[itemEnd - 1].Length == tokens[itemEnd].StartIndex)
                        {
                            itemEnd++;
                        }

                        int itemLen = itemEnd - cur;
                        string tailSyl = tokens[itemEnd - 1].NormalizedText;
                        if (itemLen >= 2 &&
                            tailSyl != "သည်" && tailSyl != "တယ်" && tailSyl != "ကြ" && tailSyl != "ခဲ့" &&
                            IsCleanCandidateNameSpan(fullText, tokens, cur, itemEnd))
                        {
                            for (int k = cur; k < itemEnd; k++)
                                mask[k] = true;
                        }

                        itemsProtected++;
                        cur = itemEnd;
                        while (cur < tokCount && tokens[cur].Kind == TokenKind.Whitespace)
                            cur++;
                        if (cur < tokCount && (tokens[cur].Text == "၊" || tokens[cur].Text == ","))
                        {
                            cur++;
                            continue;
                        }
                        break;
                    }
                }
                else if (tokens[c].Text == "(" || tokens[c].Text == "（")
                {
                    // Parenthesized cast list containing 'ဒါရိုက်တာ' or 'သရုပ်ဆောင်' before ')'
                    int closeP = -1;
                    int dirPos = -1;
                    for (int s = c + 1; s < Math.Min(tokCount, c + 45); s++)
                    {
                        if (tokens[s].Text == ")" || tokens[s].Text == "）") { closeP = s; break; }
                        if (dirPos < 0 && MatchCreditKeywordLength(tokens, s) >= 3) dirPos = s;
                    }
                    if (closeP > c + 4 && dirPos > c + 2 && dirPos < closeP)
                    {
                        int cur = c + 1;
                        while (cur < dirPos)
                        {
                            while (cur < dirPos && (tokens[cur].Kind == TokenKind.Whitespace || tokens[cur].Text == "၊" || tokens[cur].Text == ","))
                                cur++;
                            if (cur >= dirPos || tokens[cur].Kind != TokenKind.MyanmarSyllable)
                                break;
                            int itemEnd = cur + 1;
                            while (itemEnd < dirPos &&
                                   tokens[itemEnd].Kind == TokenKind.MyanmarSyllable &&
                                   tokens[itemEnd - 1].StartIndex + tokens[itemEnd - 1].Length == tokens[itemEnd].StartIndex)
                            {
                                itemEnd++;
                            }
                            if (itemEnd - cur >= 2 && itemEnd - cur <= 6 && IsCleanCandidateNameSpan(fullText, tokens, cur, itemEnd))
                            {
                                for (int k = cur; k < itemEnd; k++)
                                    mask[k] = true;
                            }
                            cur = itemEnd;
                        }
                    }
                }
            }
        }

        private static int MatchCreditKeywordLength(List<SyllableToken> tokens, int idx)
        {
            int n = tokens.Count;
            if (idx + 1 >= n || tokens[idx].Kind != TokenKind.MyanmarSyllable || tokens[idx + 1].Kind != TokenKind.MyanmarSyllable)
                return 0;
            if (tokens[idx].StartIndex + tokens[idx].Length != tokens[idx + 1].StartIndex)
                return 0;

            string s0 = tokens[idx].NormalizedText;
            string s1 = tokens[idx + 1].NormalizedText;
            if (s0 == "တေး" && (s1 == "ဆို" || s1 == "ရေး"))
                return 2;

            if (idx + 2 < n && tokens[idx + 2].Kind == TokenKind.MyanmarSyllable &&
                tokens[idx + 1].StartIndex + tokens[idx + 1].Length == tokens[idx + 2].StartIndex)
            {
                string s2 = tokens[idx + 2].NormalizedText;
                if (s0 == "သ" && s1 == "ရုပ်" && s2 == "ဆောင်")
                    return 3;
                if (s0 == "ဒါ" && s1 == "ရိုက်" && s2 == "တာ")
                    return 3;
                if (s0 == "ဂီ" && s1 == "တ" && s2 == "မှူး")
                    return 3;
            }
            return 0;
        }

        private bool IsCleanCandidateNameSpan(string fullText, List<SyllableToken> tokens, int start, int end)
        {
            for (int a = start; a < end; a++)
            {
                if (tokens[a].Kind != TokenKind.MyanmarSyllable ||
                    UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                {
                    return false;
                }
                for (int b = a + 1; b <= end; b++)
                {
                    if (_trie.TryGetWrongFormRule(GetTokenSpan(fullText, tokens, a, b - a).NormalizedText, out _))
                        return false;
                }
            }
            return true;
        }

        private static bool IsOverlappingAnyExcept(List<ProofingIssue> list, int start, int length, int skipIdx)
        {
            int end = start + length;
            for (int k = 0; k < list.Count; k++)
            {
                if (k == skipIdx) continue;
                var it = list[k];
                if (Math.Max(start, it.StartIndex) < Math.Min(end, it.StartIndex + it.Length))
                    return true;
            }
            return false;
        }

        private static void RemoveOtherIssuesInRange(List<ProofingIssue> list, int rangeStart, int rangeEnd, ref int currentIdx)
        {
            for (int k = list.Count - 1; k >= 0; k--)
            {
                if (k == currentIdx) continue;
                var it = list[k];
                if (Math.Max(rangeStart, it.StartIndex) < Math.Min(rangeEnd, it.StartIndex + it.Length))
                {
                    list.RemoveAt(k);
                    if (k < currentIdx)
                        currentIdx--;
                }
            }
        }
    }
}
