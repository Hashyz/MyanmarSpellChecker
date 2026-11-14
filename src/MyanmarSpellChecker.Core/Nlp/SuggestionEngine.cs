using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Generates ranked, richly detailed spelling suggestions in under 1ms across 45,000+ headwords using
    /// Generative Multi-Syllable Anchor Indexing, Phonetic/Orthographic Confusion Skeletons, Empirical Burmese
    /// Confusion Matrix weights, Corpus Word Frequencies, and "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" authority ranking.
    /// </summary>
    public sealed class SuggestionEngine
    {
        private static readonly int CacheCap = !Environment.Is64BitProcess ? 1536 : 4096;
        private static readonly int EnrichedChoiceCacheCap = !Environment.Is64BitProcess ? 1024 : 2048;

        private readonly MyanmarTrie _trie;
        private readonly ConcurrentDictionary<string, List<SuggestionCandidate>> _suggestionCache =
            new ConcurrentDictionary<string, List<SuggestionCandidate>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, List<SuggestionCandidate>> _atomicSuggestionCache =
            new ConcurrentDictionary<string, List<SuggestionCandidate>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<(long TrieVer, ProofingIssueType Type, ProofingStyleMode Mode, int MaxChoices, string Raw, string SeedKey), List<SuggestionCandidate>> _enrichedChoiceCache =
            new ConcurrentDictionary<(long TrieVer, ProofingIssueType Type, ProofingStyleMode Mode, int MaxChoices, string Raw, string SeedKey), List<SuggestionCandidate>>();

        private static readonly ulong[] s_compatibleBaseMasks = BuildCompatibleBaseMasks();

        [ThreadStatic]
        private static char[]? t_bufA;

        [ThreadStatic]
        private static char[]? t_bufB;

        [ThreadStatic]
        private static double[]? t_dpBuf;

        [ThreadStatic]
        private static Dictionary<string, DictionaryEntry>? t_anchorScratch;

        [ThreadStatic]
        private static char[]? t_preCompact;

        [ThreadStatic]
        private static double[]? t_preDelCosts;

        [ThreadStatic]
        private static ulong[]? t_preReqMasks;

        internal readonly struct PreEncodedQuery
        {
            public readonly string Source;
            public readonly char[] Compact;
            public readonly int CompactLen;
            public readonly double[] DelCosts;
            public readonly ulong PresentMask;
            public readonly ulong[] ReqMasks;
            public readonly int ReqCount;

            public PreEncodedQuery(
                string source,
                char[] compact,
                int compactLen,
                double[] delCosts,
                ulong presentMask,
                ulong[] reqMasks,
                int reqCount)
            {
                Source = source;
                Compact = compact;
                CompactLen = compactLen;
                DelCosts = delCosts;
                PresentMask = presentMask;
                ReqMasks = reqMasks;
                ReqCount = reqCount;
            }

            public double DistanceTo(string s2, double maxAllowedDistance)
            {
                if (Source == s2) return 0.0;

                if (Source.Length >= 3 && Source.Length == s2.Length && CharMultisetSignatureMatches(Source, s2))
                {
                    List<string> syls = MyanmarSyllableBreaker.BreakIntoSyllables(Source, isPreNormalized: true);
                    if (MatchesAdjacentSyllableSwap(syls, s2))
                        return 0.36;
                }

                char[] b = t_bufB ??= new char[128];
                if (b.Length < s2.Length) b = t_bufB = new char[Math.Max(s2.Length * 2, 128)];
                int m = EncodeCompactRhymeChars(s2, b);

                return ComputeCoreDistanceFromCompact(
                    Compact, CompactLen, DelCosts, PresentMask, ReqMasks, ReqCount, hasPrecomputedMask: true, b, m, maxAllowedDistance);
            }
        }

        internal static PreEncodedQuery CreateThreadLocalPreEncodedQuery(string preNormalizedSource)
        {
            int srcLen = preNormalizedSource.Length;
            char[] qCompact = t_preCompact ??= new char[128];
            if (qCompact.Length < srcLen)
                qCompact = t_preCompact = new char[Math.Max(srcLen * 2, 128)];

            int qLen = EncodeCompactRhymeChars(preNormalizedSource, qCompact);

            double[] qDel = t_preDelCosts ??= new double[128];
            if (qDel.Length < qLen)
                qDel = t_preDelCosts = new double[Math.Max(qLen * 2, 128)];

            ulong[] qReq = t_preReqMasks ??= new ulong[128];
            if (qReq.Length < qLen)
                qReq = t_preReqMasks = new ulong[Math.Max(qLen * 2, 128)];

            ulong presentMask = 0UL;
            int reqCount = 0;
            for (int qi = 0; qi < qLen; qi++)
            {
                char qc = qCompact[qi];
                char qPrev = qi > 0 ? qCompact[qi - 1] : '\0';
                qDel[qi] = GetIndelCost(qc, qPrev);
                int bIdx = GetTrackableBaseIndex(qc);
                if (bIdx >= 0)
                {
                    presentMask |= 1UL << bIdx;
                    if (qPrev != '\u1039')
                    {
                        qReq[reqCount++] = s_compatibleBaseMasks[bIdx];
                    }
                }
            }

            return new PreEncodedQuery(preNormalizedSource, qCompact, qLen, qDel, presentMask, qReq, reqCount);
        }

        internal static void ComputeEntryCompactMasks(
            string word,
            ref char[] scratchBuf,
            out ulong presentMask,
            out ulong packedUnstackedAndLen)
        {
            if (scratchBuf.Length < word.Length)
                scratchBuf = new char[Math.Max(word.Length * 2, 64)];

            int m = EncodeCompactRhymeChars(word, scratchBuf);
            ulong pMask = 0UL;
            ulong uMask = 0UL;
            for (int j = 0; j < m; j++)
            {
                int bIdx = GetTrackableBaseIndex(scratchBuf[j]);
                if (bIdx >= 0)
                {
                    ulong bit = 1UL << bIdx;
                    pMask |= bit;
                    if (j == 0 || scratchBuf[j - 1] != '\u1039')
                    {
                        uMask |= bit;
                    }
                }
            }

            presentMask = pMask;
            packedUnstackedAndLen = uMask | ((ulong)(m & 0xFF) << 45);
        }

        public SuggestionEngine(MyanmarTrie trie)
        {
            _trie = trie ?? throw new ArgumentNullException(nameof(trie));
        }

        public void ClearCache()
        {
            _suggestionCache.Clear();
            _atomicSuggestionCache.Clear();
            _enrichedChoiceCache.Clear();
        }

        private static int GetTrackableBaseIndex(char c)
        {
            if (c >= '\u1000' && c <= '\u102A') return c - '\u1000';
            if (c == '\u103F') return 43;
            if (c == '\u1040') return 44;
            return -1;
        }

        private static char GetTrackableBaseCharFromIndex(int idx)
        {
            if (idx <= 42) return (char)('\u1000' + idx);
            if (idx == 43) return '\u103F';
            return '\u1040';
        }

        private static ulong[] BuildCompatibleBaseMasks()
        {
            const int Count = 45;
            var masks = new ulong[Count];
            for (int i = 0; i < Count; i++)
            {
                char ci = GetTrackableBaseCharFromIndex(i);
                ulong m = 1UL << i;
                for (int j = 0; j < Count; j++)
                {
                    if (i == j) continue;
                    char cj = GetTrackableBaseCharFromIndex(j);
                    if (GetSubstitutionCost(ci, cj) <= 0.45 ||
                        IsNasalVoicedStopPair(ci, cj) ||
                        IsNasalVoicedStopPair(cj, ci))
                    {
                        m |= 1UL << j;
                    }
                }
                masks[i] = m;
            }
            return masks;
        }

        private static List<SuggestionCandidate> CloneCandidates(List<SuggestionCandidate> src, int maxTake)
        {
            int count = Math.Min(src.Count, maxTake);
            var copy = new List<SuggestionCandidate>(count);
            for (int i = 0; i < count; i++)
            {
                var c = src[i];
                copy.Add(new SuggestionCandidate
                {
                    Replacement = c.Replacement,
                    MeaningCue = c.MeaningCue,
                    Score = c.Score,
                    PosTag = c.PosTag,
                    IsOfficialThatpon = c.IsOfficialThatpon,
                    SyllableBreakdown = c.SyllableBreakdown,
                    DiagnosticDetail = c.DiagnosticDetail,
                    HomophoneSiblings = c.HomophoneSiblings,
                    AbidanSummary = c.AbidanSummary
                });
            }
            return copy;
        }

        private static readonly HashSet<string> CommonChainParticles = new HashSet<string>(StringComparer.Ordinal)
        {
            "မ", "အ", "တ", "က", "မှ", "သို့", "နှင့်", "နဲ့", "ကို", "အား", "သည်", "၏", "၌", "တွင်", "မှာ",
            "များ", "တွေ", "တို့", "သည့်", "သော", "မည့်", "မည်", "မယ်", "တယ်", "ပါ", "ကြ", "ခဲ့", "နေ",
            "သွား", "လာ", "ပြီး", "ပြီ", "ဘူး", "လား", "လဲ", "လေ", "နော်", "ပေါ့", "ပဲ", "ဘဲ", "သား", "သေး", "တော့"
        };

        public List<SuggestionCandidate> GetSuggestions(string misspelledWord, int maxResults = 5)
        {
            return GetSuggestionsInternal(misspelledWord, maxResults, allowCompositional: true);
        }

        private static bool HasMyanmarBaseCharacter(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= '\u1000' && c <= '\u102A') ||
                    c == '\u103F' ||
                    (c >= '\u1040' && c <= '\u1049') ||
                    (c >= '\u104C' && c <= '\u104F'))
                {
                    return true;
                }
            }
            return false;
        }

        private List<SuggestionCandidate> GetSuggestionsInternal(string misspelledWord, int maxResults, bool allowCompositional)
        {
            var results = new List<SuggestionCandidate>();
            if (string.IsNullOrWhiteSpace(misspelledWord))
                return results;

            string rawTrimmed = misspelledWord.Trim();
            string normalized = UnicodeNormalizer.NormalizeCanonicalOrder(rawTrimmed);

            if (rawTrimmed == normalized)
            {
                if (allowCompositional && maxResults <= 5 &&
                    _suggestionCache.TryGetValue(normalized, out var cachedList))
                {
                    return CloneCandidates(cachedList, maxResults);
                }
                if (!allowCompositional && maxResults <= 3 &&
                    _atomicSuggestionCache.TryGetValue(normalized, out var atomicCached))
                {
                    return CloneCandidates(atomicCached, maxResults);
                }
            }
            int computeCap = allowCompositional
                ? (maxResults <= 5 ? 5 : maxResults)
                : (maxResults <= 3 ? 3 : maxResults);

            // 0. Curated Colloquial / Chat Phonetic Shortcuts (instant multi-choice options)
            AddColloquialPhoneticShortcuts(results, rawTrimmed, normalized);

            // 1. Check explicit orthography/loanword rule if present
            bool hasExplicitWrongRule = false;
            if (_trie.TryGetWrongFormRule(normalized, out OrthographyRule? rule) && rule != null)
            {
                hasExplicitWrongRule = true;
                string canonRuleTarget = UnicodeNormalizer.NormalizeCanonicalOrder(rule.CorrectForm);
                AddOrUpdateEnrichedCandidate(results, rawTrimmed, canonRuleTarget, 100.0, fallbackCue: rule.Explanation);
            }

            // 2. Check lookalike character fix (၀ -> ဝ, ၄င်း -> ၎င်း, ဥ် -> ဉ်, ဓါတ် -> ဓာတ်, ကင်းစီး င်္)
            string lookalikeFixed = UnicodeNormalizer.FixLookalikeCharacters(normalized);

            // Fast short-circuit (~0.001ms): lone combining marks (e.g. dangling pre-base 'ေ' \u1031, bare 'ြ'/'ျ'/'ွ'/'ါ'/'း' mid-keystroke)
            // have no consonant/vowel base yet, so never scan 25,000 dictionary words for them!
            if (!hasExplicitWrongRule && results.Count == 0 && !HasMyanmarBaseCharacter(lookalikeFixed))
            {
                return results;
            }

            if (lookalikeFixed != normalized && _trie.ContainsExactWord(lookalikeFixed))
            {
                AddOrUpdateEnrichedCandidate(results, rawTrimmed, lookalikeFixed, 99.6);
            }

            List<string> spanSyllables = MyanmarSyllableBreaker.BreakIntoSyllables(lookalikeFixed, isPreNormalized: true);

            // Pre-encode lookalikeFixed once into recursion-safe local buffers so candidate loops never re-encode s1
            var queryCompact = new char[lookalikeFixed.Length];
            int qLen = EncodeCompactRhymeChars(lookalikeFixed, queryCompact);
            var qDelCosts = new double[qLen];
            ulong qPresentMask = 0UL;
            ulong qAllCompatibleMask = 0UL;
            var qReqMasks = new ulong[qLen];
            int qReqCount = 0;
            for (int qi = 0; qi < qLen; qi++)
            {
                char qc = queryCompact[qi];
                char qPrev = qi > 0 ? queryCompact[qi - 1] : '\0';
                qDelCosts[qi] = GetIndelCost(qc, qPrev);
                int bIdx = GetTrackableBaseIndex(qc);
                if (bIdx >= 0)
                {
                    qPresentMask |= 1UL << bIdx;
                    ulong compat = s_compatibleBaseMasks[bIdx];
                    qAllCompatibleMask |= compat;
                    if (qPrev != '\u1039')
                    {
                        qReqMasks[qReqCount++] = compat;
                    }
                }
            }
            ulong invQCompat = (~qAllCompatibleMask) & 0x1FFFFFFFFFFFUL;

            // 3. Strict & Relaxed Phonetic Homophone Index lookup (O(1))
            foreach (var entry in _trie.LookupStrictHomophones(lookalikeFixed))
            {
                if (entry.Word == normalized) continue;
                double dist = ComputeWeightedMyanmarDistancePreEncoded(
                    lookalikeFixed, spanSyllables, queryCompact, qLen, qDelCosts, qPresentMask, qReqMasks, qReqCount, entry.Word, double.MaxValue);
                double freqBonus = Math.Min(8.5, Math.Log10(entry.Frequency + 1) * 2.5);
                double thatponBonus = entry.IsOfficialThatpon ? 1.5 : 0.0;
                double cueBonus = string.IsNullOrWhiteSpace(entry.MeaningCue) ? 0.0 : 0.8;
                double score = Math.Max(80.0, 96.0 - dist * 6.5 + freqBonus + thatponBonus + cueBonus);
                AddOrUpdateEnrichedCandidate(results, rawTrimmed, entry.Word, score);
            }

            foreach (var entry in _trie.LookupByPhoneticKey(lookalikeFixed))
            {
                if (entry.Word == normalized) continue;
                double dist = ComputeWeightedMyanmarDistancePreEncoded(
                    lookalikeFixed, spanSyllables, queryCompact, qLen, qDelCosts, qPresentMask, qReqMasks, qReqCount, entry.Word, double.MaxValue);
                double freqBonus = Math.Min(8.5, Math.Log10(entry.Frequency + 1) * 2.5);
                double thatponBonus = entry.IsOfficialThatpon ? 1.2 : 0.0;
                double cueBonus = string.IsNullOrWhiteSpace(entry.MeaningCue) ? 0.0 : 0.6;
                double score = Math.Max(74.0, 92.5 - dist * 7.0 + freqBonus + thatponBonus + cueBonus);
                AddOrUpdateEnrichedCandidate(results, rawTrimmed, entry.Word, score);
            }

            // 4. Generative Multi-Syllable Anchor Index (catches arbitrary typos, missing medials, vowel swaps, tone drops in 2..5 syllable words)
            if (spanSyllables.Count >= 2 && spanSyllables.Count <= 5)
            {
                var anchorPool = t_anchorScratch ??= new Dictionary<string, DictionaryEntry>(StringComparer.Ordinal);
                anchorPool.Clear();
                _trie.PopulateCompoundAnchorCandidates(spanSyllables, lookalikeFixed, anchorPool);
                foreach (var entry in anchorPool.Values)
                {
                    double dist = ComputeWeightedMyanmarDistancePreEncoded(
                        lookalikeFixed, spanSyllables, queryCompact, qLen, qDelCosts, qPresentMask, qReqMasks, qReqCount, entry.Word, 1.85);
                    if (dist <= 1.85)
                    {
                        double freqBonus = Math.Min(8.5, Math.Log10(entry.Frequency + 1) * 2.5);
                        double thatponBonus = entry.IsOfficialThatpon ? 1.5 : 0.0;
                        double score = Math.Max(68.0, 91.0 - dist * 8.0 + freqBonus + thatponBonus);
                        AddOrUpdateEnrichedCandidate(results, rawTrimmed, entry.Word, score);
                    }
                }
            }

            // 5. Fast Bucketed Weighted Edit Distance using Center-Outward Branch-and-Bound + 45-bit Base Pre-Filtering
            if (results.Count < computeCap || spanSyllables.Count <= 1 || lookalikeFixed.Length <= 8)
            {
                int targetLen = lookalikeFixed.Length;
                char firstChar = lookalikeFixed[0];
                char lastChar = lookalikeFixed[targetLen - 1];
                double maxAllowedDist = hasExplicitWrongRule ? 0.95 : 1.75;
                bool canHaveMedialBonus = targetLen >= 2 &&
                    (firstChar == '\u1001' || firstChar == '\u1002' || firstChar == '\u1004' ||
                     firstChar == '\u1012' || firstChar == '\u1015' || firstChar == '\u101D') &&
                    lookalikeFixed.IndexOf('\u102C') > 0;
                double maxBucketCeiling = 94.0 + 8.5 + 1.5 + 0.5 + (canHaveMedialBonus ? 4.0 : 0.0);
                double floorScore = ComputeSafeFloorScore(results, computeCap);

                int[] bucketOffsets = { 0, 1, -1, 2, -2 };
                for (int bo = 0; bo < bucketOffsets.Length; bo++)
                {
                    int bLen = targetLen + bucketOffsets[bo];
                    if (bLen < 1)
                        continue;

                    double curGlobalMaxDist = floorScore > 0.0
                        ? Math.Min(maxAllowedDist, (maxBucketCeiling - floorScore + 0.001) / 9.0)
                        : maxAllowedDist;
                    if (curGlobalMaxDist < 0.0)
                        break;

                    var bucket = _trie.GetCompiledLengthBucket(bLen);
                    if (bucket == null)
                        continue;

                    int curMaxUnpartnered = curGlobalMaxDist < 0.849 ? 0 : (curGlobalMaxDist < 0.999 ? 1 : 2);
                    int curMaxCompactDelta = (int)((curGlobalMaxDist + 0.002) / 0.20);
                    int minCompactLen = qLen - curMaxCompactDelta;
                    int maxCompactLen = qLen + curMaxCompactDelta;

                    var entriesArr = bucket.Entries;
                    var presentArr = bucket.PresentMasks;
                    var packedArr = bucket.PackedUnstackedAndLen;
                    int count = entriesArr.Length;

                    for (int idx = 0; idx < count; idx++)
                    {
                        ulong packedU = packedArr[idx];
                        int m = (int)((packedU >> 45) & 0xFF);
                        if (m < minCompactLen || m > maxCompactLen)
                            continue;

                        ulong unpB = packedU & invQCompat;
                        int budgetRem = curMaxUnpartnered;
                        if (unpB != 0UL)
                        {
                            if (--budgetRem < 0) continue;
                            unpB &= unpB - 1UL;
                            if (unpB != 0UL)
                            {
                                if (--budgetRem < 0) continue;
                                unpB &= unpB - 1UL;
                                if (unpB != 0UL) continue;
                            }
                        }

                        if (qReqCount > 0)
                        {
                            ulong bPres = presentArr[idx];
                            bool exceeded = false;
                            for (int r = 0; r < qReqCount; r++)
                            {
                                if ((qReqMasks[r] & bPres) == 0UL && --budgetRem < 0)
                                {
                                    exceeded = true;
                                    break;
                                }
                            }
                            if (exceeded)
                                continue;
                        }

                        var entry = entriesArr[idx];
                        string w = entry.Word;
                        if (w == normalized) continue;

                        char w0 = w[0];
                        if (w0 != firstChar && GetSubstitutionCost(firstChar, w0) > 0.45 && w[bLen - 1] != lastChar)
                            continue;

                        double freqBonus = Math.Min(8.5, Math.Log10(entry.Frequency + 1) * 2.6);
                        double thatponBonus = entry.IsOfficialThatpon ? 1.5 : 0.0;
                        double cueBonus = string.IsNullOrWhiteSpace(entry.MeaningCue) ? 0.0 : 0.5;
                        double medialOrthographyBonus = (canHaveMedialBonus && HasMissingMedialBeforeRoundAa(lookalikeFixed, w)) ? 4.0 : 0.0;
                        double entryZeroScore = 94.0 + freqBonus + thatponBonus + cueBonus + medialOrthographyBonus;

                        double entryMaxDist = curGlobalMaxDist;
                        if (floorScore > 0.0)
                        {
                            if (entryZeroScore < floorScore - 0.001)
                                continue;
                            double neededDist = (entryZeroScore - floorScore + 0.001) / 9.0;
                            if (neededDist < entryMaxDist)
                                entryMaxDist = neededDist;
                        }

                        double dist = ComputeWeightedMyanmarDistancePreEncoded(
                            lookalikeFixed, spanSyllables, queryCompact, qLen, qDelCosts, qPresentMask, qReqMasks, qReqCount, w, entryMaxDist);
                        if (dist <= maxAllowedDist)
                        {
                            double score = Math.Max(50.0, entryZeroScore - dist * 9.0);
                            if (floorScore <= 0.0 || score >= floorScore - 0.001)
                            {
                                AddOrUpdateEnrichedCandidate(results, rawTrimmed, w, score);
                                if (results.Count >= computeCap && score > floorScore)
                                {
                                    floorScore = ComputeSafeFloorScore(results, computeCap);
                                    curGlobalMaxDist = floorScore > 0.0
                                        ? Math.Min(maxAllowedDist, (maxBucketCeiling - floorScore + 0.001) / 9.0)
                                        : maxAllowedDist;
                                    curMaxUnpartnered = curGlobalMaxDist < 0.849 ? 0 : (curGlobalMaxDist < 0.999 ? 1 : 2);
                                    curMaxCompactDelta = (int)((curGlobalMaxDist + 0.002) / 0.20);
                                    minCompactLen = qLen - curMaxCompactDelta;
                                    maxCompactLen = qLen + curMaxCompactDelta;
                                }
                            }
                        }
                    }
                }
            }

            // 6. Compositional Multi-Syllable Split & Beam Synthesis
            //    Solves concatenated multi-word phrases & multi-syllable OOV typos (e.g. 'ပီးပီ -> ပြီးပြီ', 'ဖစ်နေပီ -> ဖြစ်နေပြီ',
            //    'သွားပီးပီ -> သွားပြီးပြီ', 'လုပ်ပီးသား -> လုပ်ပြီးသား', 'စာလုံးပေါင်သတ်ပုံ -> စာလုံးပေါင်းသတ်ပုံ') so Suggestions is NEVER empty!
            if (allowCompositional && !hasExplicitWrongRule && results.Count < computeCap && spanSyllables.Count >= 2 && spanSyllables.Count <= 6)
            {
                SynthesizeCompositionalCandidates(results, rawTrimmed, normalized, spanSyllables);
            }

            // 7. Relaxed Fallback Search when an unusual OOV token still has zero matches (skip single-codepoint fragments)
            if (allowCompositional && results.Count == 0 && lookalikeFixed.Length >= 2 && lookalikeFixed.Length <= 12)
            {
                int targetLen = lookalikeFixed.Length;
                char f0 = lookalikeFixed[0];
                char fLast = lookalikeFixed[targetLen - 1];
                int minCompactLen7 = qLen - 13;
                int maxCompactLen7 = qLen + 13;
                int minBucketLen7 = Math.Max(1, targetLen - 3);
                int maxBucketLen7 = targetLen + 3;

                for (int bLen = minBucketLen7; bLen <= maxBucketLen7; bLen++)
                {
                    var bucket = _trie.GetCompiledLengthBucket(bLen);
                    if (bucket == null)
                        continue;

                    var entriesArr = bucket.Entries;
                    var presentArr = bucket.PresentMasks;
                    var packedArr = bucket.PackedUnstackedAndLen;
                    int count = entriesArr.Length;

                    for (int idx = 0; idx < count; idx++)
                    {
                        ulong packedU = packedArr[idx];
                        int m = (int)((packedU >> 45) & 0xFF);
                        if (m < minCompactLen7 || m > maxCompactLen7)
                            continue;

                        ulong unpB = packedU & invQCompat;
                        int uBLower = 0;
                        while (unpB != 0UL && uBLower < 4)
                        {
                            uBLower++;
                            unpB &= unpB - 1UL;
                        }
                        if (uBLower >= 4)
                            continue;

                        int uA = 0;
                        if (qReqCount > 0)
                        {
                            ulong bPres = presentArr[idx];
                            for (int r = 0; r < qReqCount; r++)
                            {
                                if ((qReqMasks[r] & bPres) == 0UL && ++uA >= 4)
                                    break;
                            }
                            if (uA >= 4)
                                continue;
                        }

                        if (uA > 0 || uBLower > 0)
                        {
                            int paired = uA < uBLower ? uA : uBLower;
                            int diff = uA > uBLower ? (uA - uBLower) : (uBLower - uA);
                            if (paired * 1.00 + diff * 0.85 > 2.601)
                                continue;
                        }

                        var entry = entriesArr[idx];
                        if (entry.Frequency < 2 || entry.Word == normalized)
                            continue;

                        string ew = entry.Word;
                        if (targetLen <= 2 && ew[0] != f0 && ew[bLen - 1] != fLast && GetSubstitutionCost(f0, ew[0]) > 0.65)
                            continue;

                        double dist = ComputeWeightedMyanmarDistancePreEncoded(
                            lookalikeFixed, spanSyllables, queryCompact, qLen, qDelCosts, qPresentMask, qReqMasks, qReqCount, ew, 2.60);
                        if (dist <= 2.60)
                        {
                            double freqBonus = Math.Min(7.5, Math.Log10(entry.Frequency + 1) * 2.2);
                            double score = Math.Max(42.0, 84.0 - dist * 9.5 + freqBonus + (entry.IsOfficialThatpon ? 1.5 : 0.0));
                            AddOrUpdateEnrichedCandidate(results, rawTrimmed, ew, score);
                        }
                    }
                }
            }

            var replacementSet = new HashSet<string>(results.Select(c => c.Replacement), StringComparer.Ordinal);
            var topList = results
                .Where(c =>
                {
                    // Suppress zero-frequency non-official Pali bound stems (e.g. 'တူရိယ') when their full standalone word ('တူရိယာ') is already suggested
                    if (!c.IsOfficialThatpon &&
                        _trie.GetWordFrequency(c.Replacement) == 0 &&
                        (replacementSet.Contains(c.Replacement + "\u102C") || replacementSet.Contains(c.Replacement + "\u102B")))
                    {
                        return false;
                    }
                    return true;
                })
                .OrderByDescending(c => c.Score)
                .ThenBy(c => Math.Abs(c.Replacement.Length - normalized.Length))
                .Take(computeCap)
                .ToList();

            // Populate rich diagnostic details, Abidan metadata & homophone siblings on the final top candidates
            foreach (var cand in topList)
            {
                PopulateSingleCandidateMetadata(rawTrimmed, cand);
            }

            if (rawTrimmed == normalized)
            {
                if (allowCompositional && maxResults <= 5)
                {
                    if (_suggestionCache.Count >= CacheCap)
                        _suggestionCache.Clear();
                    _suggestionCache[normalized] = CloneCandidates(topList, computeCap);
                    if (topList.Count > maxResults)
                        return topList.Take(maxResults).ToList();
                }
                else if (!allowCompositional && maxResults <= 3)
                {
                    if (_atomicSuggestionCache.Count >= CacheCap)
                        _atomicSuggestionCache.Clear();
                    _atomicSuggestionCache[normalized] = CloneCandidates(topList, computeCap);
                    if (topList.Count > maxResults)
                        return topList.Take(maxResults).ToList();
                }
            }

            return topList;
        }

        private static string BuildInitialSuggestionsSeedKey(List<SuggestionCandidate> initial)
        {
            int count = initial.Count;
            if (count == 0)
                return string.Empty;
            if (count == 1)
            {
                var c = initial[0];
                if (string.IsNullOrEmpty(c.DiagnosticDetail) && string.IsNullOrEmpty(c.AbidanSummary) &&
                    string.IsNullOrEmpty(c.SyllableBreakdown) && string.IsNullOrEmpty(c.HomophoneSiblings))
                {
                    return string.Concat(
                        c.Replacement, "\u001F",
                        c.MeaningCue, "\u001F",
                        c.PosTag, "\u001F",
                        c.IsOfficialThatpon ? "1|" : "0|",
                        ((int)Math.Round(c.Score * 100.0)).ToString());
                }
            }

            var sb = new System.Text.StringBuilder(count * 48);
            for (int i = 0; i < count; i++)
            {
                var c = initial[i];
                if (i > 0) sb.Append('\u001E');
                sb.Append(c.Replacement).Append('\u001F')
                  .Append(c.MeaningCue).Append('\u001F')
                  .Append(c.PosTag).Append('\u001F')
                  .Append(c.IsOfficialThatpon ? '1' : '0').Append('\u001F')
                  .Append((int)Math.Round(c.Score * 100.0)).Append('\u001F')
                  .Append(c.SyllableBreakdown).Append('\u001F')
                  .Append(c.DiagnosticDetail).Append('\u001F')
                  .Append(c.HomophoneSiblings).Append('\u001F')
                  .Append(c.AbidanSummary);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Ensures every flagged <see cref="ProofingIssue"/> carries concrete, clickable multiple-choice suggestions
        /// (including polite/formal register choices and high-confidence orthographic alternatives) so the user
        /// never sees only an error description without selectable choices.
        /// </summary>
        public void EnrichIssueWithSelectableChoices(ProofingIssue issue, ProofingStyleMode styleMode, int maxChoices = 4)
        {
            if (issue == null ||
                issue.IssueType == ProofingIssueType.PunctuationError ||
                issue.IssueType == ProofingIssueType.ZawgyiEncodingError)
            {
                return;
            }

            if (issue.Suggestions == null)
            {
                issue.Suggestions = new List<SuggestionCandidate>();
            }

            string raw = (issue.OriginalText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
                return;

            string seedKey = BuildInitialSuggestionsSeedKey(issue.Suggestions);
            var choiceCacheKey = (_trie.MutationVersion, issue.IssueType, styleMode, maxChoices, raw, seedKey);
            if (_enrichedChoiceCache.TryGetValue(choiceCacheKey, out var cachedChoices))
            {
                issue.Suggestions = CloneCandidates(cachedChoices, cachedChoices.Count);
                return;
            }

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(raw);
            if (!HasMyanmarBaseCharacter(norm) && issue.Suggestions.Count == 0)
                return;

            // 1. If an issue somehow arrived with 0 suggestions, synthesize candidates immediately
            if (issue.Suggestions.Count == 0)
            {
                issue.Suggestions.AddRange(GetSuggestions(raw, maxChoices));
            }

            // 2. Add natural Burmese casual-standard / polite / formal register choices based on top replacement
            if (issue.Suggestions.Count > 0)
            {
                string top = UnicodeNormalizer.NormalizeCanonicalOrder(issue.Suggestions[0].Replacement.Trim());
                AddRegisterAlternativeChoices(issue.Suggestions, raw, norm, top, styleMode);
            }

            // 3. For true OOV spelling errors (not real-word contextual rules) with fewer than 3 choices, supplement with top runner-up dictionary/compositional candidates
            if (!issue.IsGrammarOrStyle &&
                issue.IssueType == ProofingIssueType.SpellingError &&
                !_trie.ContainsExactWord(norm) &&
                issue.Suggestions.Count < Math.Min(3, maxChoices))
            {
                bool hasExplicitRule = _trie.TryGetWrongFormRule(norm, out _);
                double minRunnerUpScore = hasExplicitRule ? 88.0 : 72.0;
                var extras = GetSuggestions(raw, maxChoices + 1);
                foreach (var extra in extras)
                {
                    if (issue.Suggestions.Count >= maxChoices)
                        break;
                    if (extra.Score < minRunnerUpScore)
                        continue;

                    string extraNorm = UnicodeNormalizer.NormalizeCanonicalOrder(extra.Replacement.Trim());
                    if (extraNorm == norm)
                        continue;
                    bool dup = false;
                    for (int sIdx = 0; sIdx < issue.Suggestions.Count; sIdx++)
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(issue.Suggestions[sIdx].Replacement.Trim()) == extraNorm)
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (dup)
                        continue;

                    issue.Suggestions.Add(extra);
                }
            }

            if (issue.Suggestions.Count > maxChoices)
            {
                issue.Suggestions.RemoveRange(maxChoices, issue.Suggestions.Count - maxChoices);
            }

            for (int cIdx = 0; cIdx < issue.Suggestions.Count; cIdx++)
            {
                PopulateSingleCandidateMetadata(raw, issue.Suggestions[cIdx]);
            }

            if (_enrichedChoiceCache.Count >= EnrichedChoiceCacheCap)
                _enrichedChoiceCache.Clear();
            _enrichedChoiceCache[choiceCacheKey] = CloneCandidates(issue.Suggestions, issue.Suggestions.Count);
        }

        private void AddColloquialPhoneticShortcuts(List<SuggestionCandidate> results, string rawTrimmed, string normalized)
        {
            switch (normalized)
            {
                case "ပီးပီ":
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ပြီးပြီ", 100.0, "အများသုံး စံသတ်ပုံ (စား~၊ လုပ်~)", preferFallbackCue: true);
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ပြီးပါပြီ", 96.5, "ယဉ်ကျေးသော အသုံး", preferFallbackCue: true);
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ပြီးဖြစ်သည်", 92.5, "ရုံးသုံး / စာပေ အသုံး", preferFallbackCue: true);
                    break;
                case "ပီး":
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ပြီး", 99.6, "ကြိယာနောက်ဆက် စံသတ်ပုံ", preferFallbackCue: true);
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ပြီးနောက်", 91.0, "ဆက်စပ်ပုဒ် (ထို့~)", preferFallbackCue: true);
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ဖီး", 84.0, "ခေါင်း~သည် / ငှက်ပျော~", preferFallbackCue: true);
                    break;
                case "ဖစ်":
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ဖြစ်", 99.8, "ဖြစ်ပေါ်/ဖြစ်ပွားသည် (ရရစ်ပါ)", preferFallbackCue: true);
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ဖျစ်", 86.5, "ဖျစ်ညှစ်သည် (ယပင့်)", preferFallbackCue: true);
                    break;
                case "ပော":
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, "ပြော", 99.6, "စကားပြောဆိုသည်", preferFallbackCue: true);
                    break;
            }
        }

        private void AddRegisterAlternativeChoices(
            List<SuggestionCandidate> list,
            string rawWrong,
            string normWrong,
            string topReplacement,
            ProofingStyleMode styleMode)
        {
            void TryAppendChoice(string rep, string cue, double score)
            {
                string repNorm = UnicodeNormalizer.NormalizeCanonicalOrder(rep);
                if (repNorm == normWrong)
                    return;
                if (list.Any(c => UnicodeNormalizer.NormalizeCanonicalOrder(c.Replacement.Trim()) == repNorm))
                    return;

                AddOrUpdateEnrichedCandidate(list, rawWrong, repNorm, score, cue, preferFallbackCue: true);
            }

            if (topReplacement == "ပြီးပြီ")
            {
                TryAppendChoice("ပြီးပါပြီ", "ယဉ်ကျေးသော အသုံး (စားပြီးပါပြီ)", 95.0);
                TryAppendChoice("ပြီးဖြစ်သည်", "ရုံးသုံး / စာပေ အသုံး", 91.0);
            }
            else if (topReplacement == "ပြီ" && (normWrong == "ပီ" || normWrong.EndsWith("ပီ", StringComparison.Ordinal)))
            {
                TryAppendChoice("ပါပြီ", "ယဉ်ကျေးသော အသုံး", 94.0);
                TryAppendChoice("ပြီဖြစ်သည်", "ရုံးသုံး / စာပေ အသုံး", 89.5);
            }
            else if (topReplacement == "ဘူး" && (normWrong == "ပု" || normWrong == "ဖူး" || normWrong == "ဗူး"))
            {
                TryAppendChoice("ပါ", "ယဉ်ကျေး / ရုံးသုံး အငြင်းပစ္စည်း (မဟုတ်ပါ)", 93.0);
            }
            else if (topReplacement == "ပြီး" && normWrong == "ပီး")
            {
                TryAppendChoice("ပြီးနောက်", "အချိန်ဆက်စပ်ပုဒ်", 90.5);
                TryAppendChoice("ပြီး၍", "စာပေ အသုံး", 88.5);
            }
            else if (topReplacement == "ကြီး" && normWrong == "ကျီး")
            {
                TryAppendChoice("ကြီးမား", "အရွယ်အစား/ပမာဏ ကြီးမားသော", 86.0);
            }
            else if (topReplacement == "ပက်စီ")
            {
                TryAppendChoice("ပက်ပ်စီ", "အချိုရည်အမှတ်တံဆိပ် (Pepsi — အသံဖလှယ် စံမူကွဲ)", 95.5);
            }
            else if (topReplacement.EndsWith("ပြီးပြီ", StringComparison.Ordinal) && topReplacement.Length > "ပြီးပြီ".Length)
            {
                string stem = topReplacement.Substring(0, topReplacement.Length - "ပြီးပြီ".Length);
                TryAppendChoice(stem + "ပြီးပါပြီ", "ယဉ်ကျေးသော အသုံး", 94.0);
                TryAppendChoice(stem + "ပြီးဖြစ်သည်", "ရုံးသုံး / စာပေ အသုံး", 90.0);
            }
        }

        private static string ConcatSyllableRange(List<string> syls, int start, int count)
        {
            if (count <= 0) return string.Empty;
            if (count == 1) return syls[start];
            if (count == 2) return string.Concat(syls[start], syls[start + 1]);
            if (count == 3) return string.Concat(syls[start], syls[start + 1], syls[start + 2]);

            int totalLen = 0;
            int end = start + count;
            for (int i = start; i < end; i++)
                totalLen += syls[i].Length;

            var buf = new char[totalLen];
            int pos = 0;
            for (int i = start; i < end; i++)
            {
                string s = syls[i];
                s.CopyTo(0, buf, pos, s.Length);
                pos += s.Length;
            }
            return new string(buf);
        }

        private void SynthesizeCompositionalCandidates(
            List<SuggestionCandidate> results,
            string rawTrimmed,
            string normalized,
            List<string> spanSyllables)
        {
            int count = spanSyllables.Count;

            // 6a. Exact + Repair 2-Segment Split (e.g. 'စာလုံးပေါင်' + 'သတ်ပုံ' -> 'စာလုံးပေါင်းသတ်ပုံ', 'သွား' + 'ပီးပီ' -> 'သွားပြီးပြီ')
            for (int split = 1; split < count; split++)
            {
                string left = ConcatSyllableRange(spanSyllables, 0, split);
                string right = ConcatSyllableRange(spanSyllables, split, count - split);

                bool leftValid = IsKnownSegment(left);
                bool rightValid = IsKnownSegment(right);

                if (leftValid && !rightValid)
                {
                    var rightFixes = GetSegmentRepairOptions(right, isLeading: false, precededByNegMa: left == "မ");
                    for (int r = 0; r < Math.Min(2, rightFixes.Count); r++)
                    {
                        string combined = UnicodeNormalizer.NormalizeCanonicalOrder(left + rightFixes[r].Text);
                        if (combined != normalized)
                        {
                            AddOrUpdateEnrichedCandidate(results, rawTrimmed, combined, 92.5 - r * 3.0, "ပေါင်းစပ်ပုဒ် စံသတ်ပုံအကြံပြုချက်");
                        }
                    }
                }

                if (rightValid && !leftValid)
                {
                    var leftFixes = GetSegmentRepairOptions(left, isLeading: true, precededByNegMa: false);
                    for (int l = 0; l < Math.Min(2, leftFixes.Count); l++)
                    {
                        string combined = UnicodeNormalizer.NormalizeCanonicalOrder(leftFixes[l].Text + right);
                        if (combined != normalized)
                        {
                            AddOrUpdateEnrichedCandidate(results, rawTrimmed, combined, 92.5 - l * 3.0, "ပေါင်းစပ်ပုဒ် စံသတ်ပုံအကြံပြုချက်");
                        }
                    }
                }
            }

            // 6b. Syllable-by-Syllable Beam Synthesis (e.g. 'ဖစ်နေပီ -> ဖြစ်နေပြီ', 'မဖစ်ပု -> မဖြစ်ဘူး', 'စားပီးသွားပီ -> စားပြီးသွားပြီ')
            if (count <= 5)
            {
                var beam = new List<(string Text, double Penalty)>(6) { (string.Empty, 0.0) };
                var nextBeam = new List<(string Text, double Penalty)>(18);
                bool hasNegMa = spanSyllables[0] == "မ";

                for (int i = 0; i < count; i++)
                {
                    string syl = spanSyllables[i];
                    var sylOptions = GetSyllableBeamOptions(syl, i, count, hasNegMa);
                    nextBeam.Clear();

                    for (int b = 0; b < beam.Count; b++)
                    {
                        for (int o = 0; o < sylOptions.Count; o++)
                        {
                            nextBeam.Add((beam[b].Text + sylOptions[o].Text, beam[b].Penalty + sylOptions[o].Penalty));
                        }
                    }

                    nextBeam.Sort((a, b) => a.Penalty.CompareTo(b.Penalty));
                    beam.Clear();
                    for (int nb = 0; nb < nextBeam.Count && beam.Count < 5; nb++)
                    {
                        string candTxt = nextBeam[nb].Text;
                        bool dup = false;
                        for (int k = 0; k < beam.Count; k++)
                        {
                            if (beam[k].Text == candTxt)
                            {
                                dup = true;
                                break;
                            }
                        }
                        if (!dup)
                        {
                            beam.Add(nextBeam[nb]);
                        }
                    }
                }

                int rank = 0;
                foreach (var item in beam)
                {
                    string cand = UnicodeNormalizer.NormalizeCanonicalOrder(item.Text);
                    if (cand == normalized || string.IsNullOrEmpty(cand))
                        continue;

                    double score = Math.Max(74.0, 93.5 - item.Penalty * 14.0 - rank * 1.5);
                    AddOrUpdateEnrichedCandidate(results, rawTrimmed, cand, score, "ဝဏ္ဏအလိုက် စံသတ်ပုံအကြံပြုချက်");
                    rank++;
                    if (rank >= 3)
                        break;
                }
            }
        }

        private bool IsKnownSegment(string normSeg)
        {
            if (string.IsNullOrEmpty(normSeg))
                return false;
            if (normSeg == "ပီး" || normSeg == "ဖစ်" || normSeg == "ပော" || normSeg == "ပီးပီ")
                return false;
            if (_trie.TryGetWrongFormRule(normSeg, out _))
                return false;
            return CommonChainParticles.Contains(normSeg) || _trie.ContainsExactWord(normSeg);
        }

        private List<(string Text, double Penalty)> GetSegmentRepairOptions(string normSeg, bool isLeading, bool precededByNegMa)
        {
            var opts = new List<(string Text, double Penalty)>(3);
            if (normSeg == "ပီးပီ")
            {
                opts.Add(("ပြီးပြီ", 0.04));
                opts.Add(("ပြီးပါပြီ", 0.14));
                return opts;
            }
            if (normSeg == "ပီး")
            {
                opts.Add(("ပြီး", 0.05));
                return opts;
            }
            if (normSeg == "ပီ" && !isLeading)
            {
                opts.Add(("ပြီ", 0.06));
                opts.Add(("ပါပြီ", 0.18));
                return opts;
            }
            if (normSeg == "ဖစ်")
            {
                opts.Add(("ဖြစ်", 0.05));
                return opts;
            }
            if (normSeg == "ပု" && precededByNegMa)
            {
                opts.Add(("ဘူး", 0.06));
                opts.Add(("ပါ", 0.20));
                return opts;
            }

            var subCands = GetSuggestionsInternal(normSeg, maxResults: 2, allowCompositional: false);
            for (int i = 0; i < subCands.Count; i++)
            {
                opts.Add((subCands[i].Replacement, 0.12 + i * 0.10));
            }
            return opts;
        }

        private List<(string Text, double Penalty)> GetSyllableBeamOptions(string syl, int index, int totalCount, bool hasNegMa)
        {
            var opts = new List<(string Text, double Penalty)>(3);
            bool isLast = index == totalCount - 1;

            // High-frequency colloquial Burmese syllable contractions
            if (syl == "ပီး")
            {
                opts.Add(("ပြီး", 0.04));
                return opts;
            }
            if (syl == "ဖစ်")
            {
                opts.Add(("ဖြစ်", 0.04));
                opts.Add(("ဖျစ်", 0.35));
                return opts;
            }
            if (syl == "ပော")
            {
                opts.Add(("ပြော", 0.04));
                return opts;
            }
            if (syl == "ပီ" && index > 0)
            {
                opts.Add(("ပြီ", 0.06));
                if (isLast) opts.Add(("ပါပြီ", 0.20));
                opts.Add(("ပီ", 0.42));
                return opts;
            }
            if (syl == "ပု" && isLast && hasNegMa)
            {
                opts.Add(("ဘူး", 0.06));
                opts.Add(("ပါ", 0.22));
                return opts;
            }
            if (syl == "ကျီး" && index > 0)
            {
                opts.Add(("ကြီး", 0.08));
                opts.Add(("ကျီး", 0.38));
                return opts;
            }

            if (CommonChainParticles.Contains(syl) || (_trie.IsStandaloneWord(syl) && _trie.GetWordFrequency(syl) >= 3))
            {
                opts.Add((syl, 0.0));
                var homos = _trie.LookupStrictHomophones(syl);
                for (int h = 0; h < homos.Count; h++)
                {
                    if (homos[h].Word != syl && homos[h].IsOfficialThatpon && homos[h].Frequency >= 15)
                    {
                        opts.Add((homos[h].Word, 0.34));
                        break;
                    }
                }
                return opts;
            }

            var singleFixes = GetSuggestionsInternal(syl, maxResults: 2, allowCompositional: false);
            for (int i = 0; i < singleFixes.Count; i++)
            {
                opts.Add((singleFixes[i].Replacement, 0.14 + i * 0.12));
            }

            if (opts.Count == 0)
            {
                opts.Add((syl, 0.55));
            }
            return opts;
        }

        private static bool HasMyanmarScript(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch >= '\u1000' && ch <= '\u109F')
                    return true;
            }
            return false;
        }

        private static double ComputeSafeFloorScore(List<SuggestionCandidate> results, int computeCap)
        {
            int count = results.Count;
            if (count < computeCap)
                return 0.0;

            // Only count IsOfficialThatpon candidates toward the pruning floor because non-official 0-freq bound stems can be filtered out later
            SpanLikeTopScores(results, computeCap, out double kthScore);
            return kthScore;
        }

        private static void SpanLikeTopScores(List<SuggestionCandidate> results, int k, out double kthScore)
        {
            kthScore = 0.0;
            if (k <= 0)
                return;

            // Fast zero-allocation selection for small k (typically 3 or 5)
            if (k <= 8)
            {
                double s0 = -1, s1 = -1, s2 = -1, s3 = -1, s4 = -1, s5 = -1, s6 = -1, s7 = -1;
                int filled = 0;
                for (int i = 0; i < results.Count; i++)
                {
                    var c = results[i];
                    if (!c.IsOfficialThatpon)
                        continue;

                    double v = c.Score;
                    if (filled == k && v <= GetSlot(k - 1, s0, s1, s2, s3, s4, s5, s6, s7))
                        continue;

                    InsertSlot(v, k, ref filled, ref s0, ref s1, ref s2, ref s3, ref s4, ref s5, ref s6, ref s7);
                }

                if (filled >= k)
                    kthScore = GetSlot(k - 1, s0, s1, s2, s3, s4, s5, s6, s7);
                return;
            }

            var safeScores = new List<double>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].IsOfficialThatpon)
                    safeScores.Add(results[i].Score);
            }
            if (safeScores.Count >= k)
            {
                safeScores.Sort((a, b) => b.CompareTo(a));
                kthScore = safeScores[k - 1];
            }
        }

        private static double GetSlot(int idx, double s0, double s1, double s2, double s3, double s4, double s5, double s6, double s7)
        {
            switch (idx)
            {
                case 0: return s0;
                case 1: return s1;
                case 2: return s2;
                case 3: return s3;
                case 4: return s4;
                case 5: return s5;
                case 6: return s6;
                default: return s7;
            }
        }

        private static void InsertSlot(
            double v, int cap, ref int filled,
            ref double s0, ref double s1, ref double s2, ref double s3,
            ref double s4, ref double s5, ref double s6, ref double s7)
        {
            double[] tmp = { s0, s1, s2, s3, s4, s5, s6, s7 };
            int lim = filled < cap ? filled : (cap - 1);
            if (filled < cap) filled++;
            int pos = lim;
            while (pos > 0 && v > tmp[pos - 1])
            {
                tmp[pos] = tmp[pos - 1];
                pos--;
            }
            tmp[pos] = v;
            s0 = tmp[0]; s1 = tmp[1]; s2 = tmp[2]; s3 = tmp[3];
            s4 = tmp[4]; s5 = tmp[5]; s6 = tmp[6]; s7 = tmp[7];
        }

        private static string ExtractTopTwoMeaningCues(List<DictionaryEntry> entries)
        {
            string? firstPrimary = null;
            string? secondPrimary = null;
            string? firstSecondary = null;
            string? secondSecondary = null;

            for (int i = 0; i < entries.Count; i++)
            {
                string m = entries[i].MeaningCue;
                if (string.IsNullOrWhiteSpace(m) || !HasMyanmarScript(m))
                    continue;

                bool isSecondary = m.StartsWith("သတ်ပုံကျမ်း တွဲဖက်အညွှန်း", StringComparison.Ordinal);
                if (!isSecondary)
                {
                    if (firstPrimary == null) firstPrimary = m;
                    else if (secondPrimary == null && m != firstPrimary) secondPrimary = m;
                }
                else
                {
                    if (firstSecondary == null) firstSecondary = m;
                    else if (secondSecondary == null && m != firstSecondary) secondSecondary = m;
                }
            }

            string? c1 = firstPrimary ?? firstSecondary;
            if (c1 == null)
                return string.Empty;

            string? c2 = null;
            if (firstPrimary != null)
                c2 = secondPrimary ?? (firstSecondary != firstPrimary ? firstSecondary : secondSecondary);
            else
                c2 = secondSecondary;

            return c2 != null ? (c1 + " / " + c2) : c1;
        }

        private void PopulateSingleCandidateMetadata(string rawTrimmed, SuggestionCandidate cand)
        {
            if (string.IsNullOrWhiteSpace(cand.SyllableBreakdown))
                cand.SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(cand.Replacement);
            if (string.IsNullOrWhiteSpace(cand.DiagnosticDetail))
                cand.DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawTrimmed, cand.Replacement);
            if (string.IsNullOrWhiteSpace(cand.HomophoneSiblings))
                cand.HomophoneSiblings = _trie.GetHomophoneSiblingsSummary(cand.Replacement, 3);

            bool needCue = string.IsNullOrWhiteSpace(cand.MeaningCue);
            bool needPos = string.IsNullOrWhiteSpace(cand.PosTag);
            bool needAbidan = string.IsNullOrWhiteSpace(cand.AbidanSummary);
            if ((needCue || needPos || needAbidan) &&
                _trie.TryGetExactEntries(cand.Replacement, out var candEntries) && candEntries.Count > 0)
            {
                if (needCue)
                    cand.MeaningCue = ExtractTopTwoMeaningCues(candEntries);

                for (int i = 0; i < candEntries.Count; i++)
                {
                    var ce = candEntries[i];
                    if (needPos && !string.IsNullOrWhiteSpace(ce.PosTag))
                    {
                        cand.PosTag = ce.PosTag;
                        needPos = false;
                    }
                    if (needAbidan)
                    {
                        string ab = ce.CompactAbidanSummary;
                        if (!string.IsNullOrWhiteSpace(ab))
                        {
                            cand.AbidanSummary = ab;
                            needAbidan = false;
                        }
                    }
                    if (!needPos && !needAbidan)
                        break;
                }
            }
        }

        private void AddOrUpdateEnrichedCandidate(
            List<SuggestionCandidate> list,
            string rawWrong,
            string replacement,
            double score,
            string fallbackCue = "",
            bool preferFallbackCue = false)
        {
            string cue = string.Empty;
            string pos = string.Empty;
            bool isThatpon = true;

            if (_trie.TryGetExactEntries(replacement, out var entries) && entries.Count > 0)
            {
                isThatpon = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].IsOfficialThatpon)
                    {
                        isThatpon = true;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(fallbackCue))
                {
                    if (!preferFallbackCue)
                        cue = ExtractTopTwoMeaningCues(entries);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(entries[i].PosTag))
                        {
                            pos = entries[i].PosTag;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(cue) &&
                !string.IsNullOrWhiteSpace(fallbackCue) &&
                HasMyanmarScript(fallbackCue))
            {
                cue = fallbackCue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Replacement == replacement)
                {
                    if (score > list[i].Score)
                        list[i].Score = score;
                    if (string.IsNullOrWhiteSpace(list[i].MeaningCue) && !string.IsNullOrWhiteSpace(cue))
                        list[i].MeaningCue = cue;
                    if (string.IsNullOrWhiteSpace(list[i].PosTag) && !string.IsNullOrWhiteSpace(pos))
                        list[i].PosTag = pos;
                    return;
                }
            }

            list.Add(new SuggestionCandidate
            {
                Replacement = replacement,
                MeaningCue = cue,
                PosTag = pos,
                IsOfficialThatpon = isThatpon,
                Score = score
            });
        }

        private static char GetConfusableInitial(char c)
        {
            switch (c)
            {
                case '\u101B': return '\u101A'; // ရ <-> ယ
                case '\u101A': return '\u101B';
                case '\u1000': return '\u1002'; // က <-> ဂ
                case '\u1002': return '\u1000';
                case '\u1005': return '\u1006'; // စ <-> ဆ
                case '\u1006': return '\u1005';
                case '\u1010': return '\u100B'; // တ <-> ဋ
                case '\u100B': return '\u1010';
                case '\u1014': return '\u100F'; // န <-> ဏ
                case '\u100F': return '\u1014';
                case '\u1016': return '\u1018'; // ဖ <-> ဘ
                case '\u1018': return '\u1016';
                case '\u1015': return '\u1017'; // ပ <-> ဗ
                case '\u1017': return '\u1015';
                case '\u1025': return '\u1009'; // ဥ <-> ဉ
                case '\u1009': return '\u1025';
                default: return c;
            }
        }

        private static bool MatchesAdjacentSyllableSwap(List<string> syls, string s2)
        {
            if (syls.Count < 2 || syls.Count > 5)
                return false;

            for (int k = 0; k < syls.Count - 1; k++)
            {
                string sk = syls[k];
                string sk1 = syls[k + 1];
                if (sk == sk1) continue;

                int pos = 0;
                bool match = true;
                for (int idx = 0; idx < syls.Count; idx++)
                {
                    string part = idx == k ? sk1 : (idx == k + 1 ? sk : syls[idx]);
                    if (string.CompareOrdinal(s2, pos, part, 0, part.Length) != 0)
                    {
                        match = false;
                        break;
                    }
                    pos += part.Length;
                }

                if (match && pos == s2.Length)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Computes empirical Burmese orthographic/phonetic edit distance between two canonical strings,
        /// incorporating syllable-rhyme equivalences, labialized rhyme mergers (-ွက်/-ွတ် <-> -ုတ်/-ုပ်),
        /// nasal voicing, and adjacent Burmese syllable transpositions (e.g. မာကလာသီး / မာကာလသီး -> မာလကာသီး).
        /// </summary>
        public static double ComputeWeightedMyanmarDistance(string s1, string s2, double maxAllowedDistance = double.MaxValue)
        {
            if (s1 == s2) return 0.0;

            // Fast O(N) anagram/syllable-transposition check (e.g. မာ-ကာ-လ-သီး <-> မာ-လ-ကာ-သီး, ပု-ဆိတ်-ရွက် <-> ပု-ရွက်-ဆိတ်).
            // Requires identical length and UTF-16 code-unit sum/XOR (zero allocations in 99.99% of calls!) and never runs extra DP.
            // Cost 0.36 is strictly > 0.34 so 2-word standalone phrases (bothFullStandaloneInL2) are never flipped!
            if (s1.Length >= 3 && s1.Length == s2.Length && CharMultisetSignatureMatches(s1, s2))
            {
                List<string> syls = MyanmarSyllableBreaker.BreakIntoSyllables(s1);
                if (MatchesAdjacentSyllableSwap(syls, s2))
                    return 0.36;
            }

            return ComputeCoreDistance(s1, s2, maxAllowedDistance);
        }

        private static double ComputeWeightedMyanmarDistancePreEncoded(
            string s1,
            List<string> s1Syllables,
            char[] a,
            int n,
            double[] aDelCosts,
            ulong qPresentMask,
            ulong[] qReqMasks,
            int qReqCount,
            string s2,
            double maxAllowedDistance)
        {
            if (s1 == s2) return 0.0;

            if (s1.Length >= 3 && s1.Length == s2.Length && CharMultisetSignatureMatches(s1, s2))
            {
                if (MatchesAdjacentSyllableSwap(s1Syllables, s2))
                    return 0.36;
            }

            char[] b = t_bufB ??= new char[128];
            if (b.Length < s2.Length) b = t_bufB = new char[Math.Max(s2.Length * 2, 128)];
            int m = EncodeCompactRhymeChars(s2, b);

            return ComputeCoreDistanceFromCompact(
                a, n, aDelCosts, qPresentMask, qReqMasks, qReqCount, hasPrecomputedMask: true, b, m, maxAllowedDistance);
        }

        private static double ComputeCoreDistance(string s1, string s2, double maxAllowedDistance)
        {
            if (s1 == s2) return 0.0;

            char[] a = t_bufA ??= new char[128];
            char[] b = t_bufB ??= new char[128];
            if (a.Length < s1.Length) a = t_bufA = new char[Math.Max(s1.Length * 2, 128)];
            if (b.Length < s2.Length) b = t_bufB = new char[Math.Max(s2.Length * 2, 128)];

            int n = EncodeCompactRhymeChars(s1, a);
            int m = EncodeCompactRhymeChars(s2, b);

            return ComputeCoreDistanceFromCompact(
                a, n, null, 0UL, null, 0, hasPrecomputedMask: false, b, m, maxAllowedDistance);
        }

        private static double ComputeCoreDistanceFromCompact(
            char[] a,
            int n,
            double[]? aDelCosts,
            ulong qPresentMask,
            ulong[]? qReqMasks,
            int qReqCount,
            bool hasPrecomputedMask,
            char[] b,
            int m,
            double maxAllowedDistance)
        {
            if (n == m)
            {
                bool allSame = true;
                for (int k = 0; k < n; k++)
                {
                    if (a[k] != b[k])
                    {
                        allSame = false;
                        break;
                    }
                }
                if (allSame) return 0.28;
            }

            bool checkThreshold = maxAllowedDistance < 1000.0;
            double thresholdPlusEps = maxAllowedDistance + 0.001;

            // Minimum possible indel cost in GetIndelCost is 0.20 per character delta
            if (checkThreshold && Math.Abs(n - m) * 0.20 > thresholdPlusEps)
            {
                return maxAllowedDistance + 1.0;
            }

            // Exact O(n + m) bipartite unpartnered base-consonant lower bound using 45-bit compatibility masks
            if (checkThreshold && hasPrecomputedMask && qReqMasks != null)
            {
                ulong bPresentMask = 0UL;
                int uB = 0;
                for (int j = 0; j < m; j++)
                {
                    int bIdx = GetTrackableBaseIndex(b[j]);
                    if (bIdx >= 0)
                    {
                        bPresentMask |= 1UL << bIdx;
                        if ((j == 0 || b[j - 1] != '\u1039') && (s_compatibleBaseMasks[bIdx] & qPresentMask) == 0UL)
                        {
                            uB++;
                        }
                    }
                }

                int uA = 0;
                for (int r = 0; r < qReqCount; r++)
                {
                    if ((qReqMasks[r] & bPresentMask) == 0UL)
                    {
                        uA++;
                    }
                }

                if (uA > 0 || uB > 0)
                {
                    int paired = uA < uB ? uA : uB;
                    int diff = uA > uB ? (uA - uB) : (uB - uA);
                    double baseLowerBound = paired * 1.00 + diff * 0.85;
                    if (baseLowerBound > thresholdPlusEps)
                    {
                        return maxAllowedDistance + 1.0;
                    }
                }
            }

            int stride = m + 1;
            int requiredDp = (n + 1) * stride;
            double[] dp = t_dpBuf ??= new double[4096];
            if (dp.Length < requiredDp)
            {
                dp = t_dpBuf = new double[Math.Max(requiredDp * 2, 4096)];
            }

            dp[0] = 0.0;
            for (int j = 1; j <= m; j++)
            {
                dp[j] = dp[j - 1] + GetIndelCost(b[j - 1], j > 1 ? b[j - 2] : '\0');
            }

            double prevRowMinTotal = Math.Abs(n - m) * 0.20;

            for (int i = 1; i <= n; i++)
            {
                int rowOff = i * stride;
                int prevRowOff = (i - 1) * stride;
                int prev2RowOff = (i - 2) * stride;
                int remA = n - i;

                char c1 = a[i - 1];
                double delCost = aDelCosts != null ? aDelCosts[i - 1] : GetIndelCost(c1, i > 1 ? a[i - 2] : '\0');
                double rowZero = dp[prevRowOff] + delCost;
                dp[rowOff] = rowZero;
                double curRowMinTotal = rowZero + Math.Abs(remA - m) * 0.20;

                for (int j = 1; j <= m; j++)
                {
                    char c2 = b[j - 1];
                    double subCost;
                    if (c1 == c2)
                    {
                        subCost = 0.0;
                    }
                    else if ((c1 == '\u102B' && c2 == '\u102C') || (c1 == '\u102C' && c2 == '\u102B'))
                    {
                        // Tall AA (ါ) vs Round AA (ာ) is free ONLY when conditioned by substituting the base consonant (e.g. စဂါး -> စကား),
                        // NOT when inserting a medial on the same consonant (so တခါး -> တံခါး [0.26] cleanly beats တခြား [0.41]!)
                        char baseA = (i > 1 && a[i - 2] == '\u1031' && i > 2) ? a[i - 3] : (i > 1 ? a[i - 2] : '\0');
                        char baseB = (j > 1 && b[j - 2] == '\u1031' && j > 2) ? b[j - 3] : (j > 1 ? b[j - 2] : '\0');
                        bool isConsonantAllographShift = baseA >= '\u1000' && baseA <= '\u1021' &&
                                                         baseB >= '\u1000' && baseB <= '\u1021' &&
                                                         baseA != baseB;
                        subCost = isConsonantAllographShift ? 0.0 : 0.15;
                    }
                    else
                    {
                        subCost = GetSubstitutionCost(c1, c2);
                    }
                    double insCost = GetIndelCost(c2, j > 1 ? b[j - 2] : '\0');

                    double best = Math.Min(
                        dp[prevRowOff + j - 1] + subCost,
                        Math.Min(dp[prevRowOff + j] + delCost, dp[rowOff + j - 1] + insCost));

                    // Adjacent transposition (Damerau-Levenshtein for rapid typing & open-syllable consonant swaps like မာကလာ <-> မာလကာ)
                    if (i > 1 && j > 1 && c1 == b[j - 2] && a[i - 2] == c2)
                    {
                        best = Math.Min(best, dp[prev2RowOff + j - 2] + 0.32);
                    }

                    // Coupled Spoken Burmese Labialized Rhyme Merger:
                    // Medial '-ွ-' + stop/nasal coda (-ွက်/-ွတ်/-ွပ်, -ွန်/-ွမ်) pronounces as /oʊʔ/, /ʊɴ/ in spoken Burmese
                    // and merges with '-ု-' + stop/nasal coda (-ုတ်/-ုပ်, -ုန်/-ုံ), e.g. ပုရုတ်ဆိတ် -> ပုရွက်ဆိတ်, မင်းကုတ်သီး -> မင်းကွတ်သီး, ချုတ်ယွင်း -> ချွတ်ယွင်း.
                    // Cost 0.38 is strictly > 0.36 so standalone 2-word / prefix-compound pairs (ရေစွပ်, အပြင်ထွက်) never false-positive!
                    if (i > 1 && j > 1 &&
                        ((a[i - 2] == '\u102F' && b[j - 2] == '\u103D') || (a[i - 2] == '\u103D' && b[j - 2] == '\u102F')))
                    {
                        bool bothStopCodas = (c1 >= '\uE007' && c1 <= '\uE009') && (c2 >= '\uE007' && c2 <= '\uE009');
                        bool bothLabialNasals = (c1 == '\u1036' || (c1 >= '\uE004' && c1 <= '\uE006')) &&
                                                (c2 == '\u1036' || (c2 >= '\uE004' && c2 <= '\uE006'));
                        if (bothStopCodas || bothLabialNasals)
                        {
                            best = Math.Min(best, dp[prev2RowOff + j - 2] + 0.38);
                        }
                    }

                    // Coupled Burmese Nasal-Induced Voicing + Nasal Reduction (e.g. တံခါး /dəɡá/ -> တဂါး, တံတား -> တဒါး)
                    if (j > 1 && IsNasalRhymeMarker(b[j - 2]) && IsNasalVoicedStopPair(c1, c2))
                    {
                        best = Math.Min(best, dp[prevRowOff + j - 2] + 0.28);
                    }
                    else if (i > 1 && IsNasalRhymeMarker(a[i - 2]) && IsNasalVoicedStopPair(c2, c1))
                    {
                        best = Math.Min(best, dp[prev2RowOff + j - 1] + 0.28);
                    }

                    dp[rowOff + j] = best;
                    int remDiff = remA - (m - j);
                    if (remDiff < 0) remDiff = -remDiff;
                    double estTotal = best + remDiff * 0.20;
                    if (estTotal < curRowMinTotal)
                    {
                        curRowMinTotal = estTotal;
                    }
                }

                if (checkThreshold && curRowMinTotal > thresholdPlusEps && prevRowMinTotal > thresholdPlusEps)
                {
                    return maxAllowedDistance + 1.0;
                }

                prevRowMinTotal = curRowMinTotal;
            }

            return dp[n * stride + m];
        }

        /// <summary>
        /// Zero-allocation single-pass encoder mapping 4-char silent -ို codas (-ိုလ်/-ိုဟ်/-ိုရ်/-ိုဃ်/-ိုဠ်),
        /// 3-char Kinzi (င်္) &amp; -ိင်, and 2-char killed codas into compact Private-Use markers.
        /// </summary>
        private static int EncodeCompactRhymeChars(string s, char[] dest)
        {
            int sLen = s.Length;
            int outLen = 0;
            int i = 0;

            while (i < sLen)
            {
                char c0 = s[i];

                if (c0 == '\u102D' && i + 2 < sLen)
                {
                    char c1 = s[i + 1];
                    if (c1 == '\u102F' && i + 3 < sLen && s[i + 3] == '\u103A')
                    {
                        char mapped4 = '\0';
                        switch (s[i + 2])
                        {
                            case '\u101C': mapped4 = '\uE00C'; break; // -ိုလ်
                            case '\u101F': mapped4 = '\uE00D'; break; // -ိုဟ်
                            case '\u101B': mapped4 = '\uE00E'; break; // -ိုရ်
                            case '\u1003': mapped4 = '\uE00F'; break; // -ိုဃ်
                            case '\u1020': mapped4 = '\uE010'; break; // -ိုဠ်
                        }
                        if (mapped4 != '\0')
                        {
                            dest[outLen++] = mapped4;
                            i += 4;
                            continue;
                        }
                    }
                    else if (c1 == '\u1004' && s[i + 2] == '\u103A' && !(i + 3 < sLen && s[i + 3] == '\u1039'))
                    {
                        // bare -ိင် (/eɪɴ/) when NOT Kinzi င်္
                        dest[outLen++] = '\u102D';
                        dest[outLen++] = '\uE004';
                        i += 3;
                        continue;
                    }
                }
                else if (c0 == '\u1004' && i + 2 < sLen && s[i + 1] == '\u103A' && s[i + 2] == '\u1039')
                {
                    // င်္ (Kinzi 3-char sequence)
                    dest[outLen++] = '\uE00A';
                    i += 3;
                    continue;
                }

                if (i + 1 < sLen && s[i + 1] == '\u103A')
                {
                    char mapped2 = '\0';
                    switch (c0)
                    {
                        case '\u1004': mapped2 = '\uE00B'; break; // င်
                        case '\u100A': mapped2 = '\uE001'; break; // ည်
                        case '\u1009': mapped2 = '\uE002'; break; // ဉ်
                        case '\u101A': mapped2 = '\uE003'; break; // ယ်
                        case '\u1014': mapped2 = '\uE004'; break; // န်
                        case '\u1019': mapped2 = '\uE005'; break; // မ်
                        case '\u100F': mapped2 = '\uE006'; break; // ဏ်
                        case '\u1000': mapped2 = '\uE007'; break; // က်
                        case '\u1010': mapped2 = '\uE008'; break; // တ်
                        case '\u1015': mapped2 = '\uE009'; break; // ပ်
                        case '\u1002': mapped2 = '\uE012'; break; // ဂ်
                        case '\u1012': mapped2 = '\uE013'; break; // ဒ်
                        case '\u1017': mapped2 = '\uE014'; break; // ဗ်
                        case '\u1018': mapped2 = '\uE015'; break; // ဘ်
                        case '\u101E': mapped2 = '\uE016'; break; // သ်
                    }
                    if (mapped2 != '\0')
                    {
                        dest[outLen++] = mapped2;
                        i += 2;
                        continue;
                    }
                }

                dest[outLen++] = c0;
                i++;
            }

            return outLen;
        }

        private static double GetIndelCost(char c, char prev)
        {
            if (c == '\u103E') return 0.20; // ှ ha-hto very frequently omitted/inserted
            if (c == '\u1037' || c == '\u1038') return 0.22; // tone marks ့ / း (#1 everyday omission)
            if (c == '\uE00A' || c == '\uE00B' || c == '\u1036') return 0.26; // Kinzi င်္ / င် / Anusvara ံ (အကျီ -> အင်္ကျီ, တခါး -> တံခါး)
            if (c == '\u103B' || c == '\u103C' || c == '\u103D') return 0.26; // ျ / ြ / ွ medials (#2 everyday omission)
            if (c >= '\uE004' && c <= '\uE006') return 0.30; // nasal codas -န်/-မ်/-ဏ် in weak pre-syllables
            if (c >= '\uE001' && c <= '\uE003') return 0.32; // -ည်/-ဉ်/-ယ်
            if ((c >= '\uE007' && c <= '\uE009') || (c >= '\uE012' && c <= '\uE016')) return 0.32; // glottal/Pali stop codas
            if (c >= '\uE00C' && c <= '\uE010') return 0.34; // silent -ို coda group
            if (c == '\u1039') return 0.20; // virama in geminated stack (ပုဂိုလ် <-> ပုဂ္ဂိုလ်)
            if (prev == '\u1039') return 0.24; // geminated consonant after virama
            if ((c == '\u102B' || c == '\u102C') && prev != '\u1031') return 0.30; // unstressed open vowel ါ / ာ reduction (မလကာ -> မာလကာ), NOT compound vowel -ော
            if (c >= '\u102B' && c <= '\u103A') return 0.36;
            return 0.85;
        }

        private static bool CharMultisetSignatureMatches(string s1, string s2)
        {
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

        private static double GetSubstitutionCost(char a, char b)
        {
            if (a == b) return 0.0;
            if (a > b) { char tmp = a; a = b; b = tmp; }

            // 0. Silent -ို coda equivalences (-ိုလ် <-> -ိုဟ် <-> -ိုရ် <-> -ိုဃ် <-> -ိုဠ်: e.g. ဗိုဟ်->ဗိုလ်, ဂြိုလ်->ဂြိုဟ်, သီဟိုလ်->သီဟိုဠ်)
            if (a >= '\uE00C' && b <= '\uE010') return 0.18;

            // 1. Compact rhyme-token equivalences (-ည်/-ဉ်/-ယ်/-ီ/-ေ, -န်/-မ်/-ဏ်/-ံ/-င်/-င်္, -က်/-တ်/-ပ်/-ဂ်/-ဒ်/-ဗ်/-ဘ်/-သ်)
            if (a == '\uE00A' && b == '\uE00B') return 0.18; // င်္ <-> င် (အင်ကျီ <-> အင်္ကျီ)
            if (a == '\u1036' && (b == '\uE00A' || b == '\uE00B')) return 0.26; // ံ <-> င်္/င် (သံဘော <-> သင်္ဘော)
            if ((a >= '\uE004' && a <= '\uE006') && (b == '\uE00A' || b == '\uE00B')) return 0.26; // န်/မ်/ဏ် <-> င်္/င်

            if (a == '\u102E' && (b == '\uE001' || b == '\uE002')) return 0.24; // ီ <-> ည်/ဉ်
            if (a == '\u1031' && (b == '\uE001' || b == '\uE002' || b == '\uE003')) return 0.28; // ေ <-> ည်/ဉ်/ယ်
            if (a == '\u1031' && b == '\u1032') return 0.38; // ေ <-> ဲ (adjacent front vowels; > 0.36 standalone threshold, <= 0.28+0.22 triangle inequality)
            if (a == '\u1032' && b == '\uE003') return 0.22; // ဲ <-> ယ်
            if (a == '\uE001' && b == '\uE002') return 0.20; // ည် <-> ဉ်
            if ((a == '\uE001' || a == '\uE002') && b == '\uE003') return 0.26; // ည်/ဉ် <-> ယ်

            if (a == '\u1036' && (b >= '\uE004' && b <= '\uE006')) return 0.22; // ံ <-> န်/မ်/ဏ်
            if (a >= '\uE004' && b <= '\uE006') return 0.22; // န် <-> မ် <-> ဏ်

            bool aStop = (a >= '\uE007' && a <= '\uE009') || (a >= '\uE012' && a <= '\uE016');
            bool bStop = (b >= '\uE007' && b <= '\uE009') || (b >= '\uE012' && b <= '\uE016');
            if (aStop && bStop) return 0.24; // က်/တ်/ပ် <-> ဂ်/ဒ်/ဗ်/ဘ်/သ် (e.g. ဆုလာဒ် -> ဆုလာဘ်, ဥပုတ် -> ဥပုသ်)

            // 2. Classic Burmese consonant, medial & corpus-mined confusion matrix weights
            if (a == '\u101A' && b == '\u101B') return 0.22; // ယ <-> ရ
            if (a == '\u101B' && b == '\u101C') return 0.42; // ရ <-> လ (distinct onset consonants in standard Burmese)
            if (a == '\u103B' && b == '\u103C') return 0.20; // ျ <-> ြ
            if (a == '\u103D' && b == '\u103E') return 0.28; // ွ <-> ှ
            if (a == '\u1009' && b == '\u100A') return 0.22; // ဉ <-> ည
            if (a == '\u1009' && b == '\u1025') return 0.10; // ဉ <-> ဥ
            if (a == '\u101E' && b == '\u103F') return 0.22; // သ <-> ဿ
            if (a == '\u101D' && b == '\u1040') return 0.10; // ဝ <-> ၀
            if (a == '\u102B' && b == '\u102C') return 0.15; // ါ <-> ာ
            if (a == '\u102D' && b == '\u102E') return 0.25; // ိ <-> ီ
            if (a == '\u102F' && b == '\u1030') return 0.25; // ု <-> ူ
            if (a == '\u1037' && b == '\u1038') return 0.28; // ့ <-> း
            if (a == '\u1037' && b == '\u103A') return 0.30; // ့ <-> ်
            if (a == '\u1039' && b == '\u103A') return 0.30; // ္ <-> ်
            if (a == '\u1038' && b == '\u103A') return 0.38; // း <-> ်

            // 3. Pali retroflex, dental & cross-vagga keyboard/visual confusions
            if ((IsDentalOrRetroflexStop(a) && IsDentalOrRetroflexStop(b))) return 0.24;
            if (a == '\u100F' && b == '\u1014') return 0.22; // ဏ <-> န
            if (a == '\u1014' && b == '\u1019') return 0.38; // န <-> မ
            if (a == '\u1000' && b == '\u1010') return 0.40; // က <-> တ
            if (a == '\u1010' && b == '\u1018') return 0.40; // တ <-> ဘ
            if (a == '\u101C' && b == '\u1020') return 0.22; // လ <-> ဠ

            // 4. Same-vagga voicing/aspiration confusions (က/ခ/ဂ, စ/ဆ/ဇ/ဈ, ပ/ဖ/ဗ/ဘ)
            if (a >= '\u1000' && b <= '\u1003') return 0.30; // က, ခ, ဂ, ဃ
            if (a >= '\u1005' && b <= '\u1008') return 0.30; // စ, ဆ, ဇ, ဈ
            if (a >= '\u1015' && b <= '\u1018') return 0.30; // ပ, ဖ, ဗ, ဘ

            return 1.0;
        }

        private static bool HasMissingMedialBeforeRoundAa(string rawInput, string candidate)
        {
            // Letters ခ, ဂ, င, ဒ, ပ, ဝ only take Round AA 'ာ' (U+102C) when a medial (ျ, ြ, ွ, ှ) is present (e.g. 'ပြော', 'ပြာ', 'ကြာ')
            if (rawInput.Length >= 2 && candidate.Length >= rawInput.Length + 1 && rawInput[0] == candidate[0])
            {
                char c0 = rawInput[0];
                bool isTallAaConsonant = c0 == '\u1001' || c0 == '\u1002' || c0 == '\u1004' ||
                                         c0 == '\u1012' || c0 == '\u1015' || c0 == '\u101D';
                if (isTallAaConsonant && rawInput.IndexOf('\u102C') > 0 &&
                    candidate[1] >= '\u103B' && candidate[1] <= '\u103E')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsDentalOrRetroflexStop(char c)
        {
            return (c >= '\u100B' && c <= '\u100E') || (c >= '\u1010' && c <= '\u1013');
        }

        private static bool IsNasalRhymeMarker(char c)
        {
            return c == '\u1036' || (c >= '\uE004' && c <= '\uE006') || c == '\uE00A' || c == '\uE00B';
        }

        private static bool IsNasalVoicedStopPair(char voicedInput, char voicelessTarget)
        {
            if (voicedInput == '\u1002' && (voicelessTarget == '\u1000' || voicelessTarget == '\u1001')) return true; // ဂ <-> က/ခ (တဂါး <-> တံခါး)
            if (voicedInput == '\u1007' && (voicelessTarget == '\u1005' || voicelessTarget == '\u1006')) return true; // ဇ <-> စ/ဆ
            if (voicedInput == '\u1012' && (voicelessTarget == '\u1010' || voicelessTarget == '\u1011')) return true; // ဒ <-> တ/ထ (တဒါး <-> တံတား)
            if (voicedInput == '\u1017' && (voicelessTarget == '\u1015' || voicelessTarget == '\u1016')) return true; // ဗ <-> ပ/ဖ
            return false;
        }
    }
}
