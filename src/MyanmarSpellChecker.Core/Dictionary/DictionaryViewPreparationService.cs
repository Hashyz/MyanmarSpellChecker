// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Linq;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Dictionary
{
    /// <summary>
    /// Precomputed, immutable view-model for a single Dictionary/Encyclopedia card.
    /// Built 100% off the Office STA UI thread so WinForms only creates lightweight controls and paints in one batch.
    /// </summary>
    public sealed class PreparedDictionaryCard
    {
        public DictionaryEntry Entry { get; }
        public string SyllableBreakdown { get; }
        public MyanmarPronunciationInfo PronInfo { get; }
        public string PronCompactDualLine { get; }
        public string PronIpaTooltip { get; }
        public string SpeakableEnglish { get; }
        public IReadOnlyList<string> FormattedSenses { get; }
        public string FullUsageDetails { get; }
        public string CollapsedUsageDetails { get; }
        public int UsageDetailLineCount { get; }
        public bool IsCollapsibleUsage { get; }
        public IReadOnlyList<string> Synonyms { get; }
        public IReadOnlyList<string> Antonyms { get; }
        public IReadOnlyList<string> Collocations { get; }
        public IReadOnlyList<string> RelatedTerms { get; }
        public string HomophoneSummary { get; }
        public bool IsBookmarked { get; }

        public PreparedDictionaryCard(
            DictionaryEntry entry,
            string syllableBreakdown,
            MyanmarPronunciationInfo pronInfo,
            string pronCompactDualLine,
            string pronIpaTooltip,
            string speakableEnglish,
            IReadOnlyList<string> formattedSenses,
            string fullUsageDetails,
            string collapsedUsageDetails,
            int usageDetailLineCount,
            bool isCollapsibleUsage,
            IReadOnlyList<string> synonyms,
            IReadOnlyList<string> antonyms,
            IReadOnlyList<string> collocations,
            IReadOnlyList<string> relatedTerms,
            string homophoneSummary,
            bool isBookmarked)
        {
            Entry = entry;
            SyllableBreakdown = syllableBreakdown;
            PronInfo = pronInfo;
            PronCompactDualLine = pronCompactDualLine;
            PronIpaTooltip = pronIpaTooltip;
            SpeakableEnglish = speakableEnglish;
            FormattedSenses = formattedSenses;
            FullUsageDetails = fullUsageDetails;
            CollapsedUsageDetails = collapsedUsageDetails;
            UsageDetailLineCount = usageDetailLineCount;
            IsCollapsibleUsage = isCollapsibleUsage;
            Synonyms = synonyms;
            Antonyms = antonyms;
            Collocations = collocations;
            RelatedTerms = relatedTerms;
            HomophoneSummary = homophoneSummary;
            IsBookmarked = isBookmarked;
        }
    }

    public sealed class CachedDictionaryQueryView
    {
        public string RawQuery { get; }
        public string EffectiveQuery { get; }
        public bool ConvertedFromZawgyi { get; }
        public double ElapsedMs { get; }
        public List<DictionaryEntry> AllMatches { get; }
        public PreparedDictionaryCard[] InitialCards { get; }

        public CachedDictionaryQueryView(
            string rawQuery,
            string effectiveQuery,
            bool convertedFromZawgyi,
            double elapsedMs,
            List<DictionaryEntry> allMatches,
            PreparedDictionaryCard[] initialCards)
        {
            RawQuery = rawQuery;
            EffectiveQuery = effectiveQuery;
            ConvertedFromZawgyi = convertedFromZawgyi;
            ElapsedMs = elapsedMs;
            AllMatches = allMatches;
            InitialCards = initialCards;
        }
    }

    /// <summary>
    /// Bounded 32-entry LRU cache for recent Dictionary lookups so Back/Next navigation,
    /// repeated starter chip clicks, and backspace re-queries render in 0.0 ms without re-querying.
    /// Automatically invalidates whenever the underlying Trie MutationVersion advances.
    /// </summary>
    public sealed class DictionaryLookupCache
    {
        private readonly int _capacity;
        private readonly object _gate = new object();
        private readonly Dictionary<string, LinkedListNode<(string Key, CachedDictionaryQueryView Value)>> _map;
        private readonly LinkedList<(string Key, CachedDictionaryQueryView Value)> _lru =
            new LinkedList<(string Key, CachedDictionaryQueryView Value)>();
        private long _cachedMutationVersion = -1;

        public DictionaryLookupCache(int capacity = 32)
        {
            _capacity = Math.Max(4, capacity);
            _map = new Dictionary<string, LinkedListNode<(string Key, CachedDictionaryQueryView Value)>>(_capacity, StringComparer.Ordinal);
        }

        private static string BuildCacheKey(bool isEnglishUi, int categoryId, string trimmedQuery) =>
            $"{(isEnglishUi ? 'E' : 'M')}|{categoryId}|{trimmedQuery}";

        public bool TryGet(
            long currentMutationVersion,
            bool isEnglishUi,
            int categoryId,
            string trimmedQuery,
            out CachedDictionaryQueryView? view)
        {
            view = null;
            if (string.IsNullOrWhiteSpace(trimmedQuery))
                return false;

            string key = BuildCacheKey(isEnglishUi, categoryId, trimmedQuery);
            lock (_gate)
            {
                if (_cachedMutationVersion != currentMutationVersion)
                {
                    _map.Clear();
                    _lru.Clear();
                    _cachedMutationVersion = currentMutationVersion;
                    return false;
                }

                if (!_map.TryGetValue(key, out var node))
                    return false;

                if (node != _lru.First)
                {
                    _lru.Remove(node);
                    _lru.AddFirst(node);
                }
                view = node.Value.Value;
                return true;
            }
        }

        public void Store(
            long currentMutationVersion,
            bool isEnglishUi,
            int categoryId,
            string trimmedQuery,
            CachedDictionaryQueryView view)
        {
            if (string.IsNullOrWhiteSpace(trimmedQuery) || view == null)
                return;

            string key = BuildCacheKey(isEnglishUi, categoryId, trimmedQuery);
            lock (_gate)
            {
                if (_cachedMutationVersion != currentMutationVersion)
                {
                    _map.Clear();
                    _lru.Clear();
                    _cachedMutationVersion = currentMutationVersion;
                }

                if (_map.TryGetValue(key, out var existing))
                {
                    _lru.Remove(existing);
                    _map.Remove(key);
                }
                else if (_map.Count >= _capacity && _lru.Last != null)
                {
                    var tail = _lru.Last;
                    _lru.RemoveLast();
                    _map.Remove(tail.Value.Key);
                }

                var added = _lru.AddFirst((key, view));
                _map[key] = added;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _map.Clear();
                _lru.Clear();
            }
        }
    }

    public static class DictionaryViewPreparationService
    {
        private static readonly char[] ListSplitSeparators = { '၊', ',', ';' };
        private static readonly char[] NewlineSplitSeparators = { '\n' };

        public static PreparedDictionaryCard[] PrepareCardsSlice(
            IReadOnlyList<DictionaryEntry>? entries,
            int startIndex,
            int maxCount,
            bool isEnglishUi,
            int maxSynonymsPerCard,
            int maxAntonymsPerCard,
            int maxCollocationsPerCard,
            Func<string, List<string>>? getSynonyms,
            Func<string, List<string>>? getAntonyms,
            Func<string, List<string>>? getCollocations,
            Func<string, string>? getHomophoneSummary,
            DictionaryBookmarkStore? bookmarkStore = null)
        {
            if (entries == null || entries.Count == 0 || maxCount <= 0 || startIndex < 0 || startIndex >= entries.Count)
                return Array.Empty<PreparedDictionaryCard>();

            int endExclusive = Math.Min(entries.Count, startIndex + maxCount);
            int sliceLen = endExclusive - startIndex;
            var prepared = new PreparedDictionaryCard[sliceLen];

            var store = bookmarkStore ?? DictionaryBookmarkStore.Shared;
            HashSet<string> bookmarkSnapshot = store.GetSnapshotSet();

            for (int i = 0; i < sliceLen; i++)
            {
                var entry = entries[startIndex + i];
                bool isBilingual = entry.IsBilingualLexiconEntry;
                bool isRefPhrase = entry.IsReferenceEntry;

                string syllableBreakdown = (!isBilingual && !isRefPhrase)
                    ? OrthographicDiffExplainer.FormatSyllableBreakdown(entry.Word)
                    : string.Empty;

                MyanmarPronunciationInfo pronInfo = isRefPhrase
                    ? default
                    : MyanmarPhoneticTranscriber.GetPronunciation(entry.Word, entry.BurmesePronunciation);
                string pronDualLine = pronInfo.HasValue
                    ? MyanmarPhoneticTranscriber.FormatCompactDualLine(pronInfo, isEnglishUi)
                    : string.Empty;
                string pronIpaTip = (pronInfo.HasValue && !string.IsNullOrWhiteSpace(pronInfo.Ipa))
                    ? MyanmarPhoneticTranscriber.FormatIpaTooltip(pronInfo)
                    : string.Empty;
                string speakableEn = entry.GetSpeakableEnglishText();

                IReadOnlyList<string> formattedSenses = entry.GetFormattedSenses();

                string fullUsage = string.Empty;
                string collapsedUsage = string.Empty;
                int usageLineCount = 0;
                bool isCollapsibleUsage = false;
                if (!isBilingual && entry.HasAbidanMetadata)
                {
                    fullUsage = entry.FormattedUsageDetails;
                    if (!string.IsNullOrWhiteSpace(fullUsage))
                    {
                        string[] lines = fullUsage.Split(NewlineSplitSeparators, StringSplitOptions.RemoveEmptyEntries);
                        usageLineCount = lines.Length;
                        isCollapsibleUsage = usageLineCount >= 5;
                        collapsedUsage = isCollapsibleUsage
                            ? string.Join("\n", lines[0], lines[1], lines[2])
                            : fullUsage;
                    }
                }

                IReadOnlyList<string> synonyms;
                if (isBilingual && !string.IsNullOrWhiteSpace(entry.Synonyms))
                {
                    synonyms = entry.Synonyms
                        .Split(ListSplitSeparators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length >= 1)
                        .Distinct(StringComparer.Ordinal)
                        .Take(maxSynonymsPerCard)
                        .ToArray();
                }
                else if (!isRefPhrase && getSynonyms != null)
                {
                    var rawSyns = getSynonyms(entry.Word);
                    synonyms = rawSyns != null && rawSyns.Count > 0
                        ? (rawSyns.Count <= maxSynonymsPerCard ? rawSyns : rawSyns.GetRange(0, maxSynonymsPerCard))
                        : Array.Empty<string>();
                }
                else
                {
                    synonyms = Array.Empty<string>();
                }

                IReadOnlyList<string> antonyms;
                if (!isRefPhrase && getAntonyms != null)
                {
                    var rawAnts = getAntonyms(entry.Word);
                    antonyms = rawAnts != null && rawAnts.Count > 0
                        ? (rawAnts.Count <= maxAntonymsPerCard ? rawAnts : rawAnts.GetRange(0, maxAntonymsPerCard))
                        : Array.Empty<string>();
                }
                else
                {
                    antonyms = Array.Empty<string>();
                }

                IReadOnlyList<string> collocations;
                if (!isRefPhrase && getCollocations != null)
                {
                    var rawCollocs = getCollocations(entry.Word);
                    collocations = rawCollocs != null && rawCollocs.Count > 0
                        ? (rawCollocs.Count <= maxCollocationsPerCard ? rawCollocs : rawCollocs.GetRange(0, maxCollocationsPerCard))
                        : Array.Empty<string>();
                }
                else
                {
                    collocations = Array.Empty<string>();
                }

                IReadOnlyList<string> relatedTerms;
                if (!string.IsNullOrWhiteSpace(entry.RelatedTerms))
                {
                    relatedTerms = entry.RelatedTerms
                        .Split(ListSplitSeparators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => x.Length >= 2)
                        .Take(6)
                        .ToArray();
                }
                else
                {
                    relatedTerms = Array.Empty<string>();
                }

                string homophoneSummary = (!isRefPhrase && getHomophoneSummary != null)
                    ? (getHomophoneSummary(entry.Word) ?? string.Empty)
                    : string.Empty;

                bool isBookmarked = DictionaryBookmarkStore.ContainsNormalized(bookmarkSnapshot, entry.Word);

                prepared[i] = new PreparedDictionaryCard(
                    entry,
                    syllableBreakdown,
                    pronInfo,
                    pronDualLine,
                    pronIpaTip,
                    speakableEn,
                    formattedSenses,
                    fullUsage,
                    collapsedUsage,
                    usageLineCount,
                    isCollapsibleUsage,
                    synonyms,
                    antonyms,
                    collocations,
                    relatedTerms,
                    homophoneSummary,
                    isBookmarked);
            }

            return prepared;
        }
    }
}

