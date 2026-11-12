using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// High-speed in-memory Prefix Trie, Generative Multi-Syllable Anchor Index, Phonetic/Orthographic Confusion Index,
    /// and Bucketed Fuzzy Index for 45,000+ "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" headwords andmodern vocabulary.
    /// </summary>
    public sealed class MyanmarTrie
    {
        private sealed class TrieNode
        {
            public readonly string EdgeSyllable;
            private object? _children; // null | TrieNode | TrieNode?[] | Dictionary<string, TrieNode>
            public List<DictionaryEntry>? Entries { get; set; }
            public OrthographyRule? WrongFormRule { get; set; }

            public TrieNode(string edgeSyllable)
            {
                EdgeSyllable = edgeSyllable;
            }

            public bool TryGetChild(string syl, out TrieNode? next)
            {
                object? ch = _children;
                if (ch == null)
                {
                    next = null;
                    return false;
                }
                if (ch is TrieNode single)
                {
                    if (string.Equals(single.EdgeSyllable, syl, StringComparison.Ordinal))
                    {
                        next = single;
                        return true;
                    }
                    next = null;
                    return false;
                }
                if (ch is TrieNode?[] arr)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        TrieNode? item = arr[i];
                        if (item == null) break;
                        if (string.Equals(item.EdgeSyllable, syl, StringComparison.Ordinal))
                        {
                            next = item;
                            return true;
                        }
                    }
                    next = null;
                    return false;
                }
                return ((Dictionary<string, TrieNode>)ch).TryGetValue(syl, out next);
            }

            public TrieNode GetOrAddChild(string syl)
            {
                object? ch = _children;
                if (ch == null)
                {
                    var created = new TrieNode(syl);
                    _children = created;
                    return created;
                }
                if (ch is TrieNode single)
                {
                    if (string.Equals(single.EdgeSyllable, syl, StringComparison.Ordinal))
                        return single;
                    var next = new TrieNode(syl);
                    _children = new TrieNode?[] { single, next, null, null };
                    return next;
                }
                if (ch is TrieNode?[] arr)
                {
                    int count = 0;
                    for (; count < arr.Length; count++)
                    {
                        TrieNode? item = arr[count];
                        if (item == null) break;
                        if (string.Equals(item.EdgeSyllable, syl, StringComparison.Ordinal))
                            return item;
                    }
                    var next = new TrieNode(syl);
                    if (count < arr.Length)
                    {
                        arr[count] = next;
                        return next;
                    }
                    if (arr.Length < 6)
                    {
                        var grown = new TrieNode?[6];
                        Array.Copy(arr, grown, arr.Length);
                        grown[count] = next;
                        _children = grown;
                        return next;
                    }
                    var dict = new Dictionary<string, TrieNode>(12, StringComparer.Ordinal);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        TrieNode item = arr[i]!;
                        dict[item.EdgeSyllable] = item;
                    }
                    dict[syl] = next;
                    _children = dict;
                    return next;
                }
                var existingDict = (Dictionary<string, TrieNode>)ch;
                if (!existingDict.TryGetValue(syl, out TrieNode? found))
                {
                    found = new TrieNode(syl);
                    existingDict[syl] = found;
                }
                return found;
            }

            public void PushMatchingChildren(Stack<TrieNode> stack, string? prefixFilter, bool requireLongerThanPrefix)
            {
                object? ch = _children;
                if (ch == null)
                    return;
                if (ch is TrieNode single)
                {
                    if (MatchesPrefix(single.EdgeSyllable, prefixFilter, requireLongerThanPrefix))
                        stack.Push(single);
                    return;
                }
                if (ch is TrieNode?[] arr)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        TrieNode? item = arr[i];
                        if (item == null) break;
                        if (MatchesPrefix(item.EdgeSyllable, prefixFilter, requireLongerThanPrefix))
                            stack.Push(item);
                    }
                    return;
                }
                var dict = (Dictionary<string, TrieNode>)ch;
                if (prefixFilter == null)
                {
                    foreach (var child in dict.Values)
                        stack.Push(child);
                }
                else
                {
                    foreach (var kvp in dict)
                    {
                        if (MatchesPrefix(kvp.Key, prefixFilter, requireLongerThanPrefix))
                            stack.Push(kvp.Value);
                    }
                }
            }

            private static bool MatchesPrefix(string key, string? prefix, bool requireLonger)
            {
                if (prefix == null) return true;
                if (requireLonger && key.Length <= prefix.Length) return false;
                return key.StartsWith(prefix, StringComparison.Ordinal);
            }
        }

        private sealed class OrdinalStringPairComparer : IEqualityComparer<(string Left, string Right)>
        {
            public static readonly OrdinalStringPairComparer Instance = new OrdinalStringPairComparer();

            public bool Equals((string Left, string Right) x, (string Left, string Right) y) =>
                string.Equals(x.Left, y.Left, StringComparison.Ordinal) &&
                string.Equals(x.Right, y.Right, StringComparison.Ordinal);

            public int GetHashCode((string Left, string Right) obj)
            {
                unchecked
                {
                    int h1 = StringComparer.Ordinal.GetHashCode(obj.Left);
                    int h2 = StringComparer.Ordinal.GetHashCode(obj.Right);
                    return ((h1 << 5) + h1) ^ h2;
                }
            }
        }

        [ThreadStatic]
        private static StringBuilder? t_skeletonSb;

        private static readonly Regex GeminatedStackRegex = new Regex(
            @"([\u1000-\u1021])\u1039([\u1000-\u1021])",
            RegexOptions.Compiled);

        private static readonly Regex NasalStackRegex = new Regex(
            @"[\u100F\u1014\u1019]\u1039([\u1000-\u1021])",
            RegexOptions.Compiled);

        private static readonly Regex AsciiTokenExtractRegex = new Regex(
            @"(?:\+[0-9]{1,4}(?:-[0-9]{1,4})?|[A-Za-z][A-Za-z0-9\-]{1,}|[0-9]+[A-Za-z][A-Za-z0-9\-]*)",
            RegexOptions.Compiled);

        private static readonly List<DictionaryEntry> EmptyEntryList = new List<DictionaryEntry>(0);
        private static readonly ConcurrentDictionary<string, string> s_strictSkeletonCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, string> s_phoneticSkeletonCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private readonly TrieNode _root = new TrieNode(string.Empty);
        private readonly Dictionary<string, List<DictionaryEntry>> _exactWordIndex;
        private readonly Dictionary<string, object> _phoneticIndex;
        private readonly Dictionary<string, object> _strictHomophoneIndex;
        private readonly Dictionary<string, List<DictionaryEntry>>?[,] _exactAnchorBuckets = new Dictionary<string, List<DictionaryEntry>>?[6, 5];
        private readonly Dictionary<string, List<DictionaryEntry>>?[,] _phoneticAnchorBuckets = new Dictionary<string, List<DictionaryEntry>>?[6, 5];
        internal sealed class CompiledLengthBucket
        {
            public readonly DictionaryEntry[] Entries;
            public readonly ulong[] PresentMasks;
            public readonly ulong[] PackedUnstackedAndLen;

            public CompiledLengthBucket(DictionaryEntry[] entries, ulong[] presentMasks, ulong[] packedUnstackedAndLen)
            {
                Entries = entries;
                PresentMasks = presentMasks;
                PackedUnstackedAndLen = packedUnstackedAndLen;
            }
        }

        private readonly Dictionary<int, List<DictionaryEntry>> _lengthBucketIndex = new Dictionary<int, List<DictionaryEntry>>();
        private readonly object _lengthBucketLock = new object();
        private readonly ConcurrentDictionary<int, CompiledLengthBucket> _compiledLengthBuckets = new ConcurrentDictionary<int, CompiledLengthBucket>();
        private readonly Dictionary<string, OrthographyRule> _wrongFormIndex;
        private readonly Dictionary<string, List<ContextualRule>> _contextualRuleIndex = new Dictionary<string, List<ContextualRule>>(StringComparer.Ordinal);
        private readonly Dictionary<(string Left, string Right), int> _wordBigramIndex;
        private readonly ConcurrentDictionary<string, string> _syllablePhoneticCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _homophoneSummaryCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _validSyllables = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _standaloneSyllables = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<DictionaryEntry> _referenceCorpus = new List<DictionaryEntry>();
        private Func<List<DictionaryEntry>>? _lazyReferenceLoader;
        private int _deferredReferenceCount;
        private long _mutationVersion;

        public MyanmarTrie() : this(bulkCapacity: false)
        {
        }

        internal MyanmarTrie(bool bulkCapacity)
        {
            _exactWordIndex = new Dictionary<string, List<DictionaryEntry>>(bulkCapacity ? 90500 : 0, StringComparer.Ordinal);
            _phoneticIndex = new Dictionary<string, object>(bulkCapacity ? 80000 : 0, StringComparer.Ordinal);
            _strictHomophoneIndex = new Dictionary<string, object>(bulkCapacity ? 88500 : 0, StringComparer.Ordinal);
            _wrongFormIndex = new Dictionary<string, OrthographyRule>(bulkCapacity ? 1600 : 0, StringComparer.Ordinal);
            _wordBigramIndex = new Dictionary<(string Left, string Right), int>(bulkCapacity ? 86000 : 0, OrdinalStringPairComparer.Instance);
        }

        public long MutationVersion => Volatile.Read(ref _mutationVersion);

        // Lazy-built sub-millisecond Inverted ASCII/Code Index, Reference Syllable Index & Bi-directional Synonym/Antonym Indexes
        private readonly object _searchIndexLock = new object();
        private volatile bool _searchAndSynonymIndexBuilt;
        private readonly Dictionary<string, List<DictionaryEntry>> _latinTokenIndex = new Dictionary<string, List<DictionaryEntry>>(StringComparer.OrdinalIgnoreCase);
        private string[] _sortedLatinTokens = Array.Empty<string>();
        private readonly Dictionary<string, List<string>> _synonymIndex = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _antonymIndex = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DictionaryEntry>> _lexicalHeadwordSyllableIndex = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DictionaryEntry>> _lexicalCueSyllableIndex = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DictionaryEntry>> _referenceExactIndex = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DictionaryEntry>> _referenceSyllableIndex = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DictionaryEntry>> _referenceCueSyllableIndex = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
        private int _featuredRotationCursor;

        private static readonly HashSet<string> CueStopSyllables = new HashSet<string>(StringComparer.Ordinal)
        {
            "သည်", "သော", "ခြင်း", "မှု", "ရာ", "၌", "တွင်", "ကို", "နှင့်", "နှင့်", "၏", "မှ", "သို့", "ဖြင့်", "ဖြင့်", "အား", "ဟု", "၍", "လည်း", "ပါ", "သည့်", "သည့်"
        };

        private static readonly (string A, string B)[] CoreBurmeseAntonymPairs =
        {
            ("ကောင်း", "ဆိုး"), ("အကောင်း", "အဆိုး"), ("ကြီး", "ငယ်"), ("အကြီး", "အငယ်"),
            ("မြင့်", "နိမ့်"), ("အမြင့်", "အနိမ့်"), ("ရှည်", "တို"), ("အရှည်", "အတို"),
            ("ဖြူ", "မည်း"), ("အဖြူ", "အမည်း"), ("မှန်", "မှား"), ("အမှန်", "အမှား"),
            ("အလင်း", "အမှောင်"), ("လင်း", "မှောင်"), ("အပူ", "အအေး"), ("ပူ", "အေး"),
            ("အသစ်", "အဟောင်း"), ("သစ်", "ဟောင်း"), ("အောင်မြင်", "ရှုံးနိမ့်"), ("အနိုင်", "အရှုံး"),
            ("ငြိမ်းချမ်းရေး", "စစ်ပွဲ"), ("တိုးတက်", "ဆုတ်ယုတ်"), ("ဖွံ့ဖြိုး", "ဆုတ်ယုတ်"),
            ("တရားဝင်", "တရားမဝင်"), ("အကျိုး", "အပြစ်"), ("ချမ်းသာ", "ဆင်းရဲ"),
            ("အနီး", "အဝေး"), ("နီး", "ဝေး"), ("အထူ", "အပါး"), ("ထူ", "ပါး"),
            ("အပျော့", "အမာ"), ("ပျော့", "မာ"), ("အချို", "အခါး"), ("ချို", "ခါး"),
            ("အတက်", "အဆင်း"), ("တက်", "ဆင်း"), ("အဝင်", "အထွက်"), ("ဝင်", "ထွက်"),
            ("အစ", "အဆုံး"), ("ရှေ့", "နောက်"), ("အပေါ်", "အောက်"), ("အတွင်း", "အပြင်"),
            ("များ", "နည်း"), ("အများ", "အနည်း"), ("လွယ်ကူ", "ခက်ခဲ"), ("အလွယ်", "အခက်"),
            ("မြန်", "နှေး"), ("အမြန်", "အနှေး"), ("ကျယ်", "ကျဉ်း"), ("အကျယ်", "အကျဉ်း"),
            ("လေး", "ပေါ့"), ("အလေး", "အပေါ့"), ("စို", "ခြောက်"), ("အစို", "အခြောက်"),
            ("သန့်ရှင်း", "ညစ်ပတ်"), ("ချစ်", "မုန်း"), ("မေတ္တာ", "ဒေါသ"), ("ကုသိုလ်", "အကုသိုလ်"),
            ("ဓမ္မ", "အဓမ္မ"), ("သစ္စာရှိ", "သစ္စာဖောက်"), ("စည်းလုံး", "ပြိုကွဲ"), ("အမြတ်", "အရှုံး"),
            ("ရောင်း", "ဝယ်"), ("အရောင်း", "အဝယ်"), ("ပေး", "ယူ"), ("မွေးဖွား", "ကွယ်လွန်")
        };

        private static readonly HashSet<string> PoliteVerbFollowersAfterPa = new HashSet<string>(StringComparer.Ordinal)
        {
            "တယ်", "သည်", "မယ်", "မည်", "ဦး", "နော်", "လား", "စေ", "ဗျာ", "ရှင်", "ရှင့်", "ပြီ", "ဘူး"
        };

        public int WordCount => _exactWordIndex.Count;
        public int RuleCount => _wrongFormIndex.Count;
        public int ContextualRuleCount { get; private set; }
        public int BigramCount { get; private set; }
        public int OfficialThatponCount { get; private set; }
        public int SpecializedDomainCount { get; private set; }
        public int ReferenceCount => _referenceCorpus.Count + Volatile.Read(ref _deferredReferenceCount);
        public int EncyclopediaCount { get; private set; }

        internal void AttachLazyReferenceCorpus(int deferredRefCount, int deferredEncycCount, Func<List<DictionaryEntry>> loader)
        {
            _deferredReferenceCount = deferredRefCount;
            EncyclopediaCount += deferredEncycCount;
            _lazyReferenceLoader = loader;
        }

        private void EnsureReferenceCorpusLoaded()
        {
            if (_lazyReferenceLoader == null)
                return;

            lock (_searchIndexLock)
            {
                EnsureReferenceCorpusLoadedUnderLock();
            }
        }

        private void EnsureReferenceCorpusLoadedUnderLock()
        {
            var loader = _lazyReferenceLoader;
            if (loader == null)
                return;

            _lazyReferenceLoader = null;
            List<DictionaryEntry> loaded = loader();
            if (loaded != null && loaded.Count > 0)
            {
                _referenceCorpus.Capacity = Math.Max(_referenceCorpus.Capacity, _referenceCorpus.Count + loaded.Count);
                _referenceCorpus.AddRange(loaded);
            }
            Interlocked.Exchange(ref _deferredReferenceCount, 0);
        }

        public void InsertEntry(DictionaryEntry entry, bool isPreNormalized = false) =>
            InsertEntryCore(entry, isPreNormalized, reusableSylBuf: null, stringPool: null);

        internal void InsertEntryBulkPreNormalized(
            DictionaryEntry entry,
            List<string> reusableSylBuf,
            Dictionary<string, string> stringPool) =>
            InsertEntryCore(entry, isPreNormalized: true, reusableSylBuf, stringPool);

        private static string PoolString(string s, Dictionary<string, string>? pool)
        {
            if (pool == null || s.Length == 0)
                return s;
            if (pool.TryGetValue(s, out string? existing))
                return existing;
            pool[s] = s;
            return s;
        }

        private void InsertEntryCore(
            DictionaryEntry entry,
            bool isPreNormalized,
            List<string>? reusableSylBuf,
            Dictionary<string, string>? stringPool)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Word))
                return;

            string normalizedWord = isPreNormalized
                ? entry.Word
                : UnicodeNormalizer.NormalizeCanonicalOrder(entry.Word.Trim());
            if (stringPool != null)
                normalizedWord = PoolString(normalizedWord, stringPool);
            entry.Word = normalizedWord;

            bool isCustomEntry = string.Equals(entry.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase);
            if (!isCustomEntry)
            {
                _searchAndSynonymIndexBuilt = false;
            }

            // Multi-clause Proverbs (စကားပုံ), Sayings (ဆိုရိုးစကား), Rhetorical Styles (စကားကြီးဆယ်မျိုး), and Encyclopedic Knowledge (ENCYC)
            // are stored in the dedicated offline Reference Corpus for instant Task Pane search without polluting single-word lattice anchors!
            if (entry.IsReferenceEntry)
            {
                if (entry.IsEncyclopediaEntry)
                    EncyclopediaCount++;
                _referenceCorpus.Add(entry);
                return;
            }

            if (!isPreNormalized || string.IsNullOrEmpty(entry.PhoneticKey))
            {
                entry.PhoneticKey = BuildSkeleton(normalizedWord, ignoreTones: true, isPreNormalized: true);
            }
            if (stringPool != null)
            {
                entry.PhoneticKey = PoolString(entry.PhoneticKey, stringPool);
            }

            List<string> syllables;
            if (reusableSylBuf != null && isPreNormalized)
            {
                MyanmarSyllableBreaker.BreakIntoSyllablesFast(normalizedWord, reusableSylBuf, stringPool);
                syllables = reusableSylBuf;
            }
            else
            {
                syllables = MyanmarSyllableBreaker.BreakIntoSyllables(normalizedWord, isPreNormalized);
            }

            if (syllables.Count == 0)
                return;
            entry.SyllableCount = syllables.Count;

            bool isBrandNewHeadword = false;

            // 1. Exact Word Index
            if (!_exactWordIndex.TryGetValue(normalizedWord, out var exactList))
            {
                isBrandNewHeadword = true;
                exactList = new List<DictionaryEntry>(1);
                _exactWordIndex[normalizedWord] = exactList;

                if (entry.IsOfficialThatpon && !isCustomEntry)
                    OfficialThatponCount++;
                if (entry.IsSpecializedDomain && !isCustomEntry)
                    SpecializedDomainCount++;

                int wLen = normalizedWord.Length;
                if (stringPool != null)
                {
                    if (!_lengthBucketIndex.TryGetValue(wLen, out var lenBucket))
                    {
                        lenBucket = new List<DictionaryEntry>();
                        _lengthBucketIndex[wLen] = lenBucket;
                    }
                    lenBucket.Add(entry);
                }
                else
                {
                    lock (_lengthBucketLock)
                    {
                        if (!_lengthBucketIndex.TryGetValue(wLen, out var lenBucket))
                        {
                            lenBucket = new List<DictionaryEntry>();
                            _lengthBucketIndex[wLen] = lenBucket;
                        }
                        lenBucket.Add(entry);
                        _compiledLengthBuckets.TryRemove(wLen, out _);
                    }
                }
            }
            else if (entry.IsOfficialThatpon && !isCustomEntry && !exactList.Any(x => x.IsOfficialThatpon))
            {
                OfficialThatponCount++;
            }

            bool duplicateExact = false;
            for (int i = 0; i < exactList.Count; i++)
            {
                if (exactList[i].MeaningCue == entry.MeaningCue)
                {
                    duplicateExact = true;
                    if (string.IsNullOrEmpty(exactList[i].PosTag) && !string.IsNullOrEmpty(entry.PosTag))
                        exactList[i].PosTag = entry.PosTag;
                    if (entry.Frequency > exactList[i].Frequency)
                        exactList[i].Frequency = entry.Frequency;
                    if (entry.IsOfficialThatpon)
                        exactList[i].IsOfficialThatpon = true;
                    break;
                }
            }
            if (!duplicateExact)
            {
                // Propagate max frequency across homograph entries of the same word
                if (exactList.Count > 0)
                {
                    int maxFreq = Math.Max(entry.Frequency, exactList[0].Frequency);
                    entry.Frequency = maxFreq;
                    for (int i = 0; i < exactList.Count; i++)
                        exactList[i].Frequency = maxFreq;
                }
                exactList.Add(entry);
            }

            // 2. Relaxed Phonetic/Orthographic Confusion Index (for Suggestions, Dictionary Homophone Search & Generative Repair)
            AddToSingleOrListIndex(_phoneticIndex, entry.PhoneticKey, entry, 256);

            // 3. Strict Tone-Preserving Homophone Index (for 2+ Syllable Generative Compound Error Detection)
            string strictKey = BuildSkeleton(normalizedWord, ignoreTones: false, isPreNormalized: true);
            if (stringPool != null)
                strictKey = PoolString(strictKey, stringPool);
            AddToSingleOrListIndex(_strictHomophoneIndex, strictKey, entry, 128);

            // 4. Generative Multi-Syllable Anchor Index (exact syllable + phonetic syllable anchors so even dual-syllable typos like တဂါး -> တံခါး resolve in O(1))
            if (!isCustomEntry && isBrandNewHeadword && syllables.Count >= 2 && syllables.Count <= 5)
            {
                int sCount = syllables.Count;
                bool includePhoneticAnchor = entry.IsOfficialThatpon || entry.Frequency >= 5;
                for (int pos = 0; pos < sCount; pos++)
                {
                    var exactMap = _exactAnchorBuckets[sCount, pos];
                    if (exactMap == null)
                    {
                        exactMap = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
                        _exactAnchorBuckets[sCount, pos] = exactMap;
                    }
                    if (!exactMap.TryGetValue(syllables[pos], out var anchorList))
                    {
                        anchorList = new List<DictionaryEntry>(4);
                        exactMap[syllables[pos]] = anchorList;
                    }
                    anchorList.Add(entry);

                    if (includePhoneticAnchor)
                    {
                        string pSyl = GetCachedSyllablePhoneticKey(syllables[pos]);
                        if (!string.IsNullOrEmpty(pSyl))
                        {
                            var pMap = _phoneticAnchorBuckets[sCount, pos];
                            if (pMap == null)
                            {
                                pMap = new Dictionary<string, List<DictionaryEntry>>(StringComparer.Ordinal);
                                _phoneticAnchorBuckets[sCount, pos] = pMap;
                            }
                            if (!pMap.TryGetValue(pSyl, out var pAnchorList))
                            {
                                pAnchorList = new List<DictionaryEntry>(4);
                                pMap[pSyl] = pAnchorList;
                            }
                            pAnchorList.Add(entry);
                        }
                    }
                }
            }

            // 5. Syllable Prefix Trie & Standalone vs Bound Syllable Sets
            if (!isCustomEntry && syllables.Count == 1)
            {
                _standaloneSyllables.Add(syllables[0]);
            }

            TrieNode current = _root;
            for (int i = 0; i < syllables.Count; i++)
            {
                string syl = syllables[i];
                if (entry.IsOfficialThatpon && !isCustomEntry)
                {
                    _validSyllables.Add(syl);
                }
                current = current.GetOrAddChild(syl);
            }

            if (current.Entries == null)
            {
                current.Entries = exactList;
            }
            else if (!ReferenceEquals(current.Entries, exactList))
            {
                if (current.Entries.Count > 0 &&
                    _exactWordIndex.TryGetValue(current.Entries[0].Word, out var ownerList) &&
                    ReferenceEquals(current.Entries, ownerList))
                {
                    current.Entries = new List<DictionaryEntry>(current.Entries);
                }
                if (!ContainsWordAndCue(current.Entries, entry.Word, entry.MeaningCue))
                {
                    current.Entries.Add(entry);
                }
            }

            if (isCustomEntry)
            {
                _homophoneSummaryCache.Clear();
            }
            if (isCustomEntry && _searchAndSynonymIndexBuilt)
            {
                lock (_searchIndexLock)
                {
                    if (_searchAndSynonymIndexBuilt)
                    {
                        var customSyls = new List<string>(8);
                        ExtractMyanmarSyllableUnits(normalizedWord, customSyls);
                        for (int i = 0; i < customSyls.Count; i++)
                        {
                            string cs = customSyls[i];
                            if (!_lexicalHeadwordSyllableIndex.TryGetValue(cs, out var bucket))
                            {
                                bucket = new List<DictionaryEntry>(2);
                                _lexicalHeadwordSyllableIndex[cs] = bucket;
                            }
                            if (!ContainsWordAndCue(bucket, entry.Word, entry.MeaningCue))
                            {
                                bucket.Add(entry);
                            }
                        }
                    }
                }
            }

            if (stringPool == null)
            {
                Interlocked.Increment(ref _mutationVersion);
            }
        }

        private static void AddToSingleOrListIndex(
            Dictionary<string, object> index,
            string key,
            DictionaryEntry entry,
            int maxCap)
        {
            if (!index.TryGetValue(key, out object? existing))
            {
                index[key] = entry;
                return;
            }
            if (existing is DictionaryEntry single)
            {
                if (single.Word == entry.Word && single.MeaningCue == entry.MeaningCue)
                    return;
                index[key] = new List<DictionaryEntry>(2) { single, entry };
                return;
            }
            var list = (List<DictionaryEntry>)existing;
            if (list.Count < maxCap && !ContainsWordAndCue(list, entry.Word, entry.MeaningCue))
            {
                list.Add(entry);
            }
        }

        private static void RemoveCustomFromSingleOrListIndex(Dictionary<string, object> index, string key, string normWord)
        {
            if (!index.TryGetValue(key, out object? existing))
                return;
            if (existing is DictionaryEntry single)
            {
                if (single.Word == normWord && string.Equals(single.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase))
                    index.Remove(key);
                return;
            }
            var list = (List<DictionaryEntry>)existing;
            list.RemoveAll(e => e.Word == normWord && string.Equals(e.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0)
                index.Remove(key);
        }

        private static List<DictionaryEntry> LookupSingleOrListIndex(Dictionary<string, object> index, string key)
        {
            if (!index.TryGetValue(key, out object? val))
                return EmptyEntryList;
            if (val is List<DictionaryEntry> list)
                return list;
            return new List<DictionaryEntry>(1) { (DictionaryEntry)val };
        }

        private string GetCachedSyllablePhoneticKey(string normSyllable)
        {
            if (string.IsNullOrEmpty(normSyllable))
                return string.Empty;
            if (!_syllablePhoneticCache.TryGetValue(normSyllable, out string? cached))
            {
                cached = BuildSkeleton(normSyllable, ignoreTones: true, isPreNormalized: true);
                _syllablePhoneticCache[normSyllable] = cached;
            }
            return cached;
        }

        /// <summary>
        /// Surgically removes a user-added CUSTOM dictionary entry without disturbing any built-in lexicon entries.
        /// </summary>
        public bool RemoveCustomEntry(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
            if (string.IsNullOrEmpty(norm))
                return false;

            bool removedAny = false;

            if (_exactWordIndex.TryGetValue(norm, out var exactList))
            {
                int removed = exactList.RemoveAll(e => string.Equals(e.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    removedAny = true;
                    if (exactList.Count == 0)
                    {
                        _exactWordIndex.Remove(norm);
                    }
                }
            }

            lock (_lengthBucketLock)
            {
                if (_lengthBucketIndex.TryGetValue(norm.Length, out var lenBucket))
                {
                    lenBucket.RemoveAll(e => e.Word == norm && string.Equals(e.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase));
                    _compiledLengthBuckets.TryRemove(norm.Length, out _);
                }
            }

            string pKey = ComputePhoneticKey(norm);
            RemoveCustomFromSingleOrListIndex(_phoneticIndex, pKey, norm);

            string sKey = ComputeStrictHomophoneKey(norm);
            RemoveCustomFromSingleOrListIndex(_strictHomophoneIndex, sKey, norm);

            List<string> syllables = MyanmarSyllableBreaker.BreakIntoSyllables(norm);
            TrieNode? current = _root;
            for (int i = 0; i < syllables.Count && current != null; i++)
            {
                if (!current.TryGetChild(syllables[i], out current))
                    current = null;
            }
            if (current?.Entries != null)
            {
                current.Entries.RemoveAll(e => e.Word == norm && string.Equals(e.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase));
            }

            if (removedAny)
            {
                _homophoneSummaryCache.Clear();
            }
            if (removedAny && _searchAndSynonymIndexBuilt)
            {
                lock (_searchIndexLock)
                {
                    if (_searchAndSynonymIndexBuilt)
                    {
                        var customSyls = new List<string>(8);
                        ExtractMyanmarSyllableUnits(norm, customSyls);
                        for (int i = 0; i < customSyls.Count; i++)
                        {
                            if (_lexicalHeadwordSyllableIndex.TryGetValue(customSyls[i], out var bucket))
                            {
                                bucket.RemoveAll(e => e.Word == norm && string.Equals(e.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase));
                            }
                        }
                    }
                }
            }

            if (removedAny)
            {
                Interlocked.Increment(ref _mutationVersion);
            }

            return removedAny;
        }

        private static bool ContainsWordAndCue(List<DictionaryEntry> list, string word, string cue)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Word == word && list[i].MeaningCue == cue)
                    return true;
            }
            return false;
        }

        public void InsertOrthographyRule(OrthographyRule rule, bool isPreNormalized = false) =>
            InsertOrthographyRuleCore(rule, isPreNormalized, reusableSylBuf: null, stringPool: null);

        internal void InsertOrthographyRuleBulkPreNormalized(
            OrthographyRule rule,
            List<string> reusableSylBuf,
            Dictionary<string, string> stringPool) =>
            InsertOrthographyRuleCore(rule, isPreNormalized: true, reusableSylBuf, stringPool);

        private void InsertOrthographyRuleCore(
            OrthographyRule rule,
            bool isPreNormalized,
            List<string>? reusableSylBuf,
            Dictionary<string, string>? stringPool)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.WrongForm))
                return;

            string wrongNormalized = isPreNormalized
                ? rule.WrongForm
                : UnicodeNormalizer.NormalizeCanonicalOrder(rule.WrongForm.Trim());
            string correctNormalized = isPreNormalized
                ? rule.CorrectForm
                : UnicodeNormalizer.NormalizeCanonicalOrder(rule.CorrectForm.Trim());
            if (stringPool != null)
            {
                wrongNormalized = PoolString(wrongNormalized, stringPool);
                correctNormalized = PoolString(correctNormalized, stringPool);
                if (!string.IsNullOrEmpty(rule.Category))
                    rule.Category = PoolString(rule.Category, stringPool);
            }
            rule.WrongForm = wrongNormalized;
            rule.CorrectForm = correctNormalized;

            _wrongFormIndex[wrongNormalized] = rule;

            List<string> syllables;
            if (reusableSylBuf != null && isPreNormalized)
            {
                MyanmarSyllableBreaker.BreakIntoSyllablesFast(wrongNormalized, reusableSylBuf, stringPool);
                syllables = reusableSylBuf;
            }
            else
            {
                syllables = MyanmarSyllableBreaker.BreakIntoSyllables(wrongNormalized, isPreNormalized);
            }
            if (syllables.Count == 0)
                return;

            TrieNode current = _root;
            for (int i = 0; i < syllables.Count; i++)
            {
                current = current.GetOrAddChild(syllables[i]);
            }

            current.WrongFormRule = rule;
            if (stringPool == null)
            {
                Interlocked.Increment(ref _mutationVersion);
            }
        }

        public void InsertContextualRule(ContextualRule rule, bool isPreNormalized = false)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.WrongForm) || string.IsNullOrWhiteSpace(rule.CorrectForm))
                return;

            string normWrong = isPreNormalized
                ? rule.WrongForm
                : UnicodeNormalizer.NormalizeCanonicalOrder(rule.WrongForm.Trim());
            string normCorrect = isPreNormalized
                ? rule.CorrectForm
                : UnicodeNormalizer.NormalizeCanonicalOrder(rule.CorrectForm.Trim());
            if (normWrong == normCorrect)
                return;

            var normalizedTriggers = new List<string>();
            foreach (string rawTrig in (rule.TriggerWords ?? string.Empty).Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = isPreNormalized ? rawTrig.Trim() : UnicodeNormalizer.NormalizeCanonicalOrder(rawTrig.Trim());
                if (!string.IsNullOrEmpty(t))
                    normalizedTriggers.Add(t);
            }
            if (normalizedTriggers.Count == 0)
                return;

            var normalizedAntiTriggers = new List<string>();
            if (!string.IsNullOrWhiteSpace(rule.AntiTriggerWords))
            {
                foreach (string rawAnti in rule.AntiTriggerWords.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string a = isPreNormalized ? rawAnti.Trim() : UnicodeNormalizer.NormalizeCanonicalOrder(rawAnti.Trim());
                    if (!string.IsNullOrEmpty(a))
                        normalizedAntiTriggers.Add(a);
                }
            }

            rule.WrongForm = normWrong;
            rule.CorrectForm = normCorrect;
            rule.TriggerWords = string.Join("|", normalizedTriggers);
            rule.AntiTriggerWords = normalizedAntiTriggers.Count > 0 ? string.Join("|", normalizedAntiTriggers) : string.Empty;
            rule.ParsedTriggers = normalizedTriggers.ToArray();
            rule.ParsedAntiTriggers = normalizedAntiTriggers.Count > 0 ? normalizedAntiTriggers.ToArray() : Array.Empty<string>();
            if (rule.Window <= 0)
                rule.Window = 3;

            if (!_contextualRuleIndex.TryGetValue(normWrong, out var list))
            {
                list = new List<ContextualRule>(2);
                _contextualRuleIndex[normWrong] = list;
            }
            list.Add(rule);
            ContextualRuleCount++;
            Interlocked.Increment(ref _mutationVersion);
        }

        public bool TryGetContextualRules(string word, out List<ContextualRule> rules)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            return _contextualRuleIndex.TryGetValue(norm, out rules!);
        }

        public IEnumerable<KeyValuePair<string, List<ContextualRule>>> EnumerateContextualRules() => _contextualRuleIndex;

        public void InsertWordBigram(string leftWord, string rightWord, int count, bool isPreNormalized = false)
        {
            if (count <= 0 || string.IsNullOrWhiteSpace(leftWord) || string.IsNullOrWhiteSpace(rightWord))
                return;

            string normLeft = isPreNormalized ? leftWord : UnicodeNormalizer.NormalizeCanonicalOrder(leftWord.Trim());
            string normRight = isPreNormalized ? rightWord : UnicodeNormalizer.NormalizeCanonicalOrder(rightWord.Trim());
            var key = (normLeft, normRight);
            if (!_wordBigramIndex.TryGetValue(key, out int prev) || count > prev)
            {
                if (prev == 0)
                    BigramCount++;
                _wordBigramIndex[key] = count;
                Interlocked.Increment(ref _mutationVersion);
            }
        }

        internal void InsertWordBigramBulkPreNormalized(
            string leftWord,
            string rightWord,
            int count,
            Dictionary<string, string> stringPool)
        {
            if (count <= 0 || leftWord.Length == 0 || rightWord.Length == 0)
                return;

            string normLeft = PoolString(leftWord, stringPool);
            string normRight = PoolString(rightWord, stringPool);
            var key = (normLeft, normRight);
            if (!_wordBigramIndex.TryGetValue(key, out int prev) || count > prev)
            {
                if (prev == 0)
                    BigramCount++;
                _wordBigramIndex[key] = count;
            }
        }

        public int GetBigramCount(string leftWord, string rightWord)
        {
            if (string.IsNullOrEmpty(leftWord) || string.IsNullOrEmpty(rightWord))
                return 0;
            string normLeft = UnicodeNormalizer.NormalizeCanonicalOrder(leftWord);
            string normRight = UnicodeNormalizer.NormalizeCanonicalOrder(rightWord);
            return _wordBigramIndex.TryGetValue((normLeft, normRight), out int c) ? c : 0;
        }

        public bool HasPosTag(string word, string tagFragment)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(tagFragment))
                return false;
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            if (!_exactWordIndex.TryGetValue(norm, out var list))
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                string pt = list[i].PosTag;
                if (!string.IsNullOrEmpty(pt) && pt.IndexOf(tagFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public bool ContainsExactWord(string word)
        {
            if (string.IsNullOrEmpty(word))
                return false;
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            return _exactWordIndex.ContainsKey(norm);
        }

        public bool IsValidSingleSyllable(string syllable)
        {
            if (string.IsNullOrEmpty(syllable))
                return false;
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(syllable);
            return _validSyllables.Contains(norm) || _exactWordIndex.ContainsKey(norm);
        }

        /// <summary>
        /// Returns true only if the syllable is registered as an independent 1-syllable word (not merely a bound sub-syllable of a compound).
        /// </summary>
        public bool IsStandaloneWord(string syllable)
        {
            if (string.IsNullOrEmpty(syllable))
                return false;
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(syllable);
            if (norm.Length == 1 && ((norm[0] >= '\u1000' && norm[0] <= '\u102A') || (norm[0] >= '\u104C' && norm[0] <= '\u104F')))
                return true;
            return _standaloneSyllables.Contains(norm);
        }

        public int GetWordFrequency(string word)
        {
            if (string.IsNullOrEmpty(word))
                return 0;
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            if (_exactWordIndex.TryGetValue(norm, out var list) && list.Count > 0)
                return list[0].Frequency;
            return 0;
        }

        public bool TryGetExactEntries(string word, out List<DictionaryEntry> entries)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            return _exactWordIndex.TryGetValue(norm, out entries!);
        }

        public bool TryGetWrongFormRule(string word, out OrthographyRule rule)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            return _wrongFormIndex.TryGetValue(norm, out rule!);
        }

        public List<DictionaryEntry> LookupByPhoneticKey(string word) =>
            LookupSingleOrListIndex(_phoneticIndex, ComputePhoneticKey(word));

        public List<DictionaryEntry> LookupByPhoneticKey(string word, out string phoneticKey)
        {
            phoneticKey = ComputePhoneticKey(word);
            return LookupSingleOrListIndex(_phoneticIndex, phoneticKey);
        }

        public List<DictionaryEntry> LookupStrictHomophones(string word) =>
            LookupSingleOrListIndex(_strictHomophoneIndex, ComputeStrictHomophoneKey(word));

        /// <summary>
        /// Retrieves multi-syllable Thatpon headwords sharing at least one exact anchor syllable at the same position.
        /// Enables catching arbitrary user typos (tone omission, medial omission, vowel swap, keyboard slip) with zero predefined WrongForm rules.
        /// </summary>
        public List<DictionaryEntry> LookupCompoundAnchorCandidates(IReadOnlyList<string> spanSyllables)
        {
            var results = new List<DictionaryEntry>();
            int count = spanSyllables.Count;
            if (count < 2 || count > 5)
                return results;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int pos = 0; pos < count; pos++)
            {
                GetCompoundAnchorBuckets(count, pos, spanSyllables[pos], out var bucket, out var pBucket);
                if (bucket != null)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var e = bucket[i];
                        if (seen.Add(e.Word))
                        {
                            results.Add(e);
                        }
                    }
                }

                if (pBucket != null)
                {
                    for (int i = 0; i < pBucket.Count; i++)
                    {
                        var e = pBucket[i];
                        if (seen.Add(e.Word))
                        {
                            results.Add(e);
                        }
                    }
                }
            }

            return results;
        }

        internal void GetCompoundAnchorBuckets(
            int syllableCount,
            int pos,
            string exactSyllable,
            out List<DictionaryEntry>? exactBucket,
            out List<DictionaryEntry>? phoneticBucket)
        {
            exactBucket = null;
            phoneticBucket = null;
            if (syllableCount < 2 || syllableCount > 5 || pos < 0 || pos >= syllableCount)
                return;

            var exactMap = _exactAnchorBuckets[syllableCount, pos];
            if (exactMap != null)
            {
                exactMap.TryGetValue(exactSyllable, out exactBucket);
            }

            var pMap = _phoneticAnchorBuckets[syllableCount, pos];
            if (pMap != null)
            {
                string pSyl = GetCachedSyllablePhoneticKey(exactSyllable);
                if (!string.IsNullOrEmpty(pSyl))
                {
                    pMap.TryGetValue(pSyl, out phoneticBucket);
                }
            }
        }

        /// <summary>
        /// Zero-intermediate-allocation fast path that populates <paramref name="targetPool"/> directly with
        /// length-compatible multi-syllable anchor candidates (skipping string hashing on length-incompatible entries).
        /// </summary>
        public void PopulateCompoundAnchorCandidates(
            IReadOnlyList<string> spanSyllables,
            string spanNormalized,
            Dictionary<string, DictionaryEntry> targetPool)
        {
            int count = spanSyllables.Count;
            if (count < 2 || count > 5)
                return;

            int spanLen = spanNormalized.Length;

            for (int pos = 0; pos < count; pos++)
            {
                GetCompoundAnchorBuckets(count, pos, spanSyllables[pos], out var bucket, out var pBucket);
                if (bucket != null)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var e = bucket[i];
                        string w = e.Word;
                        int delta = Math.Abs(w.Length - spanLen);
                        if (delta > 4 || w == spanNormalized)
                            continue;
                        if (delta > 2 && w.IndexOf('\u1039') < 0)
                            continue;
                        if (!targetPool.ContainsKey(w))
                            targetPool[w] = e;
                    }
                }

                if (pBucket != null)
                {
                    for (int i = 0; i < pBucket.Count; i++)
                    {
                        var e = pBucket[i];
                        string w = e.Word;
                        int delta = Math.Abs(w.Length - spanLen);
                        if (delta > 4 || w == spanNormalized)
                            continue;
                        if (delta > 2 && w.IndexOf('\u1039') < 0)
                            continue;
                        if (!targetPool.ContainsKey(w))
                            targetPool[w] = e;
                    }
                }
            }
        }

        /// <summary>
        /// Returns curated &amp; symmetric Burmese synonyms (ကြောင်းတူသံကွဲ) for Thesaurus cards and right-click replacement.
        /// </summary>
        public List<string> GetSynonyms(string word, int maxSynonyms = 8)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(word) || maxSynonyms <= 0)
                return list;

            EnsureSearchAndSynonymIndexBuilt();

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
            if (_synonymIndex.TryGetValue(norm, out var syns))
            {
                for (int i = 0; i < syns.Count && list.Count < maxSynonyms; i++)
                {
                    if (!string.Equals(syns[i], norm, StringComparison.Ordinal))
                    {
                        list.Add(syns[i]);
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Returns curated &amp; symmetric Burmese antonyms (ဆန့်ကျင်ဘက် ဝေါဟာရ) for Thesaurus and Dictionary cards.
        /// </summary>
        public List<string> GetAntonyms(string word, int maxAntonyms = 8)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(word) || maxAntonyms <= 0)
                return list;

            EnsureSearchAndSynonymIndexBuilt();

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
            if (_antonymIndex.TryGetValue(norm, out var ants))
            {
                for (int i = 0; i < ants.Count && list.Count < maxAntonyms; i++)
                {
                    if (!string.Equals(ants[i], norm, StringComparison.Ordinal))
                    {
                        list.Add(ants[i]);
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// Returns a rotating set of rich Myanmar-to-Myanmar Encyclopedic &amp; Abidan knowledge entries for discovery exploration.
        /// </summary>
        public List<DictionaryEntry> GetFeaturedKnowledgeEntries(int count = 8)
        {
            var results = new List<DictionaryEntry>();
            if (count <= 0)
                return results;

            int seedCursor = Interlocked.Add(ref _featuredRotationCursor, 37) & int.MaxValue;
            int encycTarget = Math.Max(1, (count * 2) / 3);
            EnsureReferenceCorpusLoaded();
            if (_referenceCorpus.Count > 0)
            {
                int totalRef = _referenceCorpus.Count;
                int stride = Math.Max(17, totalRef / Math.Max(1, encycTarget + 3));
                int cursor = seedCursor % totalRef;
                int scanned = 0;
                while (results.Count < encycTarget && scanned < totalRef)
                {
                    var cand = _referenceCorpus[cursor];
                    if (cand.IsEncyclopediaEntry && !string.IsNullOrEmpty(cand.MeaningCue) && !string.IsNullOrEmpty(cand.KnowledgeSummary))
                    {
                        results.Add(cand);
                        cursor = (cursor + stride) % totalRef;
                    }
                    else
                    {
                        cursor = (cursor + 1) % totalRef;
                    }
                    scanned++;
                }
            }

            if (results.Count < count && _exactWordIndex.Count > 0)
            {
                int skip = (seedCursor * 13) % Math.Max(1, _exactWordIndex.Count);
                int idx = 0;
                foreach (var kvp in _exactWordIndex)
                {
                    if (idx++ < skip) continue;
                    var e = kvp.Value[0];
                    if (e.HasAbidanMetadata && !string.IsNullOrEmpty(e.MeaningCue) &&
                        (!string.IsNullOrEmpty(e.Synonyms) || !string.IsNullOrEmpty(e.Antonyms) || !string.IsNullOrEmpty(e.KnowledgeSummary)))
                    {
                        results.Add(e);
                        if (results.Count >= count)
                            break;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Pre-builds all inverted search, Latin code, synonym, antonym, and syllable-unit indexes on a background thread
        /// so the user's very first Dictionary or Thesaurus lookup executes in &lt; 1 ms with zero UI-thread stall.
        /// </summary>
        public void PrewarmSearchIndexes() => EnsureSearchAndSynonymIndexBuilt();

        private void EnsureSearchAndSynonymIndexBuilt()
        {
            if (_searchAndSynonymIndexBuilt)
                return;

            lock (_searchIndexLock)
            {
                if (_searchAndSynonymIndexBuilt)
                    return;

                EnsureSearchAndSynonymIndexBuiltCore();
                _searchAndSynonymIndexBuilt = true;
            }
        }

        private void EnsureSearchAndSynonymIndexBuiltCore()
        {
            EnsureReferenceCorpusLoadedUnderLock();

            _latinTokenIndex.Clear();
            _synonymIndex.Clear();
            _antonymIndex.Clear();
            _lexicalHeadwordSyllableIndex.Clear();
            _lexicalCueSyllableIndex.Clear();
            _referenceExactIndex.Clear();
            _referenceSyllableIndex.Clear();
            _referenceCueSyllableIndex.Clear();

            var scratchUnits = new List<string>(16);
            var localSylSeen = new HashSet<string>(StringComparer.Ordinal);

            void AddSyllableBucket(Dictionary<string, List<DictionaryEntry>> index, string syl, DictionaryEntry entry, int maxBucketCap)
            {
                if (!index.TryGetValue(syl, out var bucket))
                {
                    bucket = new List<DictionaryEntry>(4);
                    index[syl] = bucket;
                }
                if (bucket.Count < maxBucketCap)
                {
                    bucket.Add(entry);
                }
            }

            void AddLatinToken(string token, DictionaryEntry entry)
            {
                if (string.IsNullOrEmpty(token) || token.Length < 2)
                    return;
                if (!_latinTokenIndex.TryGetValue(token, out var bucket))
                {
                    bucket = new List<DictionaryEntry>(2);
                    _latinTokenIndex[token] = bucket;
                }
                if (!ContainsWordAndCue(bucket, entry.Word, entry.MeaningCue))
                {
                    bucket.Add(entry);
                }
            }

            void AddSynonymPair(string a, string b)
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
                    return;
                if (!_synonymIndex.TryGetValue(a, out var sList))
                {
                    sList = new List<string>(4);
                    _synonymIndex[a] = sList;
                }
                if (!sList.Contains(b))
                {
                    sList.Add(b);
                }
            }

            void AddAntonymPair(string a, string b)
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b)
                    return;
                if (!_antonymIndex.TryGetValue(a, out var aList))
                {
                    aList = new List<string>(4);
                    _antonymIndex[a] = aList;
                }
                if (!aList.Contains(b))
                {
                    aList.Add(b);
                }
            }

            for (int ci = 0; ci < CoreBurmeseAntonymPairs.Length; ci++)
            {
                var pair = CoreBurmeseAntonymPairs[ci];
                AddAntonymPair(pair.A, pair.B);
                AddAntonymPair(pair.B, pair.A);
            }

            char[] synSeparators = { '၊', ',', ';', '/' };

            foreach (var kvp in _exactWordIndex)
            {
                var entries = kvp.Value;

                // Index headword syllable units once per unique word key (covering all homograph entries)
                localSylSeen.Clear();
                ExtractMyanmarSyllableUnits(kvp.Key, scratchUnits);
                for (int su = 0; su < scratchUnits.Count; su++)
                {
                    string hSyl = scratchUnits[su];
                    if (localSylSeen.Add(hSyl))
                    {
                        for (int ei = 0; ei < entries.Count; ei++)
                            AddSyllableBucket(_lexicalHeadwordSyllableIndex, hSyl, entries[ei], int.MaxValue);
                    }
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];

                    // 1. Index ASCII / ISO / CallingCode tokens across MeaningCue & UsageNote (which includes EnglishName)
                    if (!string.IsNullOrEmpty(e.UsageNote))
                    {
                        foreach (Match m in AsciiTokenExtractRegex.Matches(e.UsageNote))
                        {
                            string tok = m.Value.TrimEnd('-');
                            AddLatinToken(tok, e);
                            int dashIdx = tok.IndexOf('-');
                            if (dashIdx > 0)
                            {
                                AddLatinToken(tok.Substring(0, dashIdx), e);
                                if (dashIdx + 1 < tok.Length)
                                    AddLatinToken(tok.Substring(dashIdx + 1), e);
                            }
                        }

                        // 2. Index bi-directional Burmese synonyms (ကြောင်းတူသံကွဲ:)
                        if (e.UsageNote.IndexOf("ကြောင်းတူသံကွဲ:", StringComparison.Ordinal) >= 0)
                        {
                            string rawSyns = e.Synonyms;
                            if (!string.IsNullOrEmpty(rawSyns))
                            {
                                string[] parts = rawSyns.Split(synSeparators, StringSplitOptions.RemoveEmptyEntries);
                                for (int s = 0; s < parts.Length; s++)
                                {
                                    string cleanSyn = UnicodeNormalizer.NormalizeCanonicalOrder(parts[s].Trim());
                                    int parenIdx = cleanSyn.IndexOf('(');
                                    if (parenIdx > 0)
                                        cleanSyn = cleanSyn.Substring(0, parenIdx).Trim();
                                    if (cleanSyn.Length >= 1 && cleanSyn.Length <= 40)
                                    {
                                        AddSynonymPair(e.Word, cleanSyn);
                                        AddSynonymPair(cleanSyn, e.Word);
                                    }
                                }
                            }
                        }

                        // 3. Index bi-directional Burmese antonyms (ဆန့်ကျင်ဘက်:)
                        if (e.UsageNote.IndexOf("ဆန့်ကျင်ဘက်:", StringComparison.Ordinal) >= 0)
                        {
                            string rawAnts = e.Antonyms;
                            if (!string.IsNullOrEmpty(rawAnts))
                            {
                                string[] parts = rawAnts.Split(synSeparators, StringSplitOptions.RemoveEmptyEntries);
                                for (int s = 0; s < parts.Length; s++)
                                {
                                    string cleanAnt = UnicodeNormalizer.NormalizeCanonicalOrder(parts[s].Trim());
                                    int parenIdx = cleanAnt.IndexOf('(');
                                    if (parenIdx > 0)
                                        cleanAnt = cleanAnt.Substring(0, parenIdx).Trim();
                                    if (cleanAnt.Length >= 1 && cleanAnt.Length <= 40)
                                    {
                                        AddAntonymPair(e.Word, cleanAnt);
                                        AddAntonymPair(cleanAnt, e.Word);
                                    }
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(e.MeaningCue))
                    {
                        foreach (Match m in AsciiTokenExtractRegex.Matches(e.MeaningCue))
                        {
                            string tok = m.Value.TrimEnd('-');
                            AddLatinToken(tok, e);
                        }

                        localSylSeen.Clear();
                        ExtractMyanmarSyllableUnits(e.MeaningCue, scratchUnits);
                        for (int su = 0; su < scratchUnits.Count; su++)
                        {
                            string cSyl = scratchUnits[su];
                            if (!CueStopSyllables.Contains(cSyl) && localSylSeen.Add(cSyl))
                            {
                                AddSyllableBucket(_lexicalCueSyllableIndex, cSyl, e, 2500);
                            }
                        }
                    }
                }
            }

            // 4. Index Offline Classical & Encyclopedic Reference Corpus (_referenceCorpus) for < 1.5 ms lookup
            for (int ri = 0; ri < _referenceCorpus.Count; ri++)
            {
                var refEntry = _referenceCorpus[ri];
                if (!_referenceExactIndex.TryGetValue(refEntry.Word, out var exRefList))
                {
                    exRefList = new List<DictionaryEntry>(1);
                    _referenceExactIndex[refEntry.Word] = exRefList;
                }
                exRefList.Add(refEntry);

                localSylSeen.Clear();
                ExtractMyanmarSyllableUnits(refEntry.Word, scratchUnits);
                for (int si = 0; si < scratchUnits.Count; si++)
                {
                    string syl = scratchUnits[si];
                    if (syl.Length > 0 && localSylSeen.Add(syl))
                    {
                        AddSyllableBucket(_referenceSyllableIndex, syl, refEntry, int.MaxValue);
                    }
                }

                if (!string.IsNullOrEmpty(refEntry.UsageNote))
                {
                    foreach (Match m in AsciiTokenExtractRegex.Matches(refEntry.UsageNote))
                    {
                        AddLatinToken(m.Value.TrimEnd('-'), refEntry);
                    }
                }
                if (!string.IsNullOrEmpty(refEntry.MeaningCue))
                {
                    foreach (Match m in AsciiTokenExtractRegex.Matches(refEntry.MeaningCue))
                    {
                        AddLatinToken(m.Value.TrimEnd('-'), refEntry);
                    }

                    localSylSeen.Clear();
                    ExtractMyanmarSyllableUnits(refEntry.MeaningCue, scratchUnits);
                    for (int si = 0; si < scratchUnits.Count; si++)
                    {
                        string cSyl = scratchUnits[si];
                        if (!CueStopSyllables.Contains(cSyl) && localSylSeen.Add(cSyl))
                        {
                            AddSyllableBucket(_referenceCueSyllableIndex, cSyl, refEntry, 1500);
                        }
                    }
                }
            }

            _sortedLatinTokens = _latinTokenIndex.Keys.ToArray();
            Array.Sort(_sortedLatinTokens, StringComparer.OrdinalIgnoreCase);
            _searchAndSynonymIndexBuilt = true;
        }

        /// <summary>
        /// Returns a formatted summary of confusable homophone siblings (with meaning cues) for UI detail cards.
        /// </summary>
        public string GetHomophoneSiblingsSummary(string word, int maxSiblings = 3)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(word);
            if (maxSiblings == 3 && _homophoneSummaryCache.TryGetValue(norm, out string? cachedSummary))
                return cachedSummary;

            var siblings = LookupStrictHomophones(norm)
                .Concat(LookupByPhoneticKey(norm))
                .Where(e => e.Word != norm && Math.Abs(e.Word.Length - norm.Length) <= 3)
                .GroupBy(e => e.Word)
                .Select(g => g.OrderByDescending(x => !string.IsNullOrEmpty(x.MeaningCue)).First())
                .OrderByDescending(e => !string.IsNullOrEmpty(e.MeaningCue))
                .ThenByDescending(e => e.Frequency)
                .Take(maxSiblings)
                .Select(e => string.IsNullOrWhiteSpace(e.MeaningCue) ? e.Word : $"{e.Word} [{e.MeaningCue}]")
                .ToList();

            string summary = siblings.Count > 0 ? string.Join(" ၊ ", siblings) : string.Empty;
            if (maxSiblings == 3)
            {
                if (_homophoneSummaryCache.Count > 4096)
                    _homophoneSummaryCache.Clear();
                _homophoneSummaryCache[norm] = summary;
            }
            return summary;
        }

        internal CompiledLengthBucket? GetCompiledLengthBucket(int length)
        {
            if (length <= 0)
                return null;
            if (_compiledLengthBuckets.TryGetValue(length, out var cached))
                return cached;

            DictionaryEntry[] snapshot;
            lock (_lengthBucketLock)
            {
                if (!_lengthBucketIndex.TryGetValue(length, out var bucket) || bucket.Count == 0)
                    return null;
                snapshot = bucket.ToArray();
            }

            int count = snapshot.Length;
            var presentMasks = new ulong[count];
            var packedUnstacked = new ulong[count];
            var scratchBuf = new char[Math.Max(length + 4, 64)];

            for (int i = 0; i < count; i++)
            {
                SuggestionEngine.ComputeEntryCompactMasks(
                    snapshot[i].Word,
                    ref scratchBuf,
                    out presentMasks[i],
                    out packedUnstacked[i]);
            }

            var compiled = new CompiledLengthBucket(snapshot, presentMasks, packedUnstacked);
            _compiledLengthBuckets[length] = compiled;
            return compiled;
        }

        public IEnumerable<DictionaryEntry> GetEntriesByLengthWindow(int targetLength, int maxLengthDelta = 2)
        {
            int minLen = Math.Max(1, targetLength - maxLengthDelta);
            int maxLen = targetLength + maxLengthDelta;
            for (int len = minLen; len <= maxLen; len++)
            {
                var compiled = GetCompiledLengthBucket(len);
                if (compiled != null)
                {
                    var arr = compiled.Entries;
                    for (int i = 0; i < arr.Length; i++)
                        yield return arr[i];
                }
            }
        }

        public IEnumerable<DictionaryEntry> GetAllEntries() =>
            _exactWordIndex.Values.SelectMany(x => x);

        public List<DictionaryEntry> SearchDictionary(string query, int maxResults = 35)
        {
            var results = new List<DictionaryEntry>();
            if (string.IsNullOrWhiteSpace(query))
                return results;

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(query.Trim()));
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void AddUnique(DictionaryEntry e)
            {
                string key = $"{e.Word}|{e.PosTag}|{e.MeaningCue}";
                if (seen.Add(key))
                {
                    results.Add(e);
                }
            }

            // 0. Typo-tolerant WrongForm redirect: if query is a known misspelling (e.g. 'ဥက္ကဌ', 'ကုတင်', 'သဏ္ဍာန်'), surface the official CorrectForm headword first!
            if (_wrongFormIndex.TryGetValue(norm, out var wfRule) &&
                !string.IsNullOrEmpty(wfRule.CorrectForm) &&
                _exactWordIndex.TryGetValue(wfRule.CorrectForm, out var corrExactList))
            {
                foreach (var e in corrExactList
                    .OrderByDescending(x => x.IsOfficialThatpon)
                    .ThenByDescending(x => !string.IsNullOrEmpty(x.UsageNote))
                    .ThenByDescending(x => !string.IsNullOrEmpty(x.MeaningCue)))
                {
                    AddUnique(e);
                }
            }

            // 1. Exact matches first (prioritize Thatpon + မြန်မာအဘိဓာန် entries with rich definitions & UsageNotes)
            if (_exactWordIndex.TryGetValue(norm, out var exactList))
            {
                foreach (var e in exactList
                    .OrderByDescending(x => x.IsOfficialThatpon)
                    .ThenByDescending(x => !string.IsNullOrEmpty(x.UsageNote))
                    .ThenByDescending(x => !string.IsNullOrEmpty(x.MeaningCue)))
                {
                    AddUnique(e);
                }
            }

            // 1a. Sub-Millisecond Inverted Index Bilingual English / Latin / ISO Code / Calling Code (+95, +81) Lookup
            bool hasLatinChars = false;
            for (int ci = 0; ci < norm.Length; ci++)
            {
                char ch = norm[ci];
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    hasLatinChars = true;
                    break;
                }
            }
            bool isCallingCodeQuery = norm.Length >= 2 && norm[0] == '+' && norm[1] >= '0' && norm[1] <= '9';
            if ((hasLatinChars || isCallingCodeQuery) && norm.Length >= 2)
            {
                EnsureSearchAndSynonymIndexBuilt();

                var candidatePool = new List<DictionaryEntry>();
                var candidateSeen = new HashSet<string>(StringComparer.Ordinal);
                void AddCandidate(DictionaryEntry ce)
                {
                    if (candidateSeen.Add($"{ce.Word}|{ce.PosTag}|{ce.MeaningCue}"))
                        candidatePool.Add(ce);
                }

                var qTokens = new List<string>();
                foreach (Match qm in AsciiTokenExtractRegex.Matches(norm))
                {
                    string qt = qm.Value.TrimEnd('-');
                    if (qt.Length >= 2)
                        qTokens.Add(qt);
                }

                if (qTokens.Count > 0)
                {
                    // Pick the longest query token as primary anchor to minimize candidate set
                    string anchorTok = qTokens[0];
                    for (int ti = 1; ti < qTokens.Count; ti++)
                    {
                        if (qTokens[ti].Length > anchorTok.Length)
                            anchorTok = qTokens[ti];
                    }

                    if (_latinTokenIndex.TryGetValue(anchorTok, out var directBucket))
                    {
                        for (int i = 0; i < directBucket.Count; i++)
                            AddCandidate(directBucket[i]);
                    }

                    // O(log N) binary-search prefix expansion over sorted _sortedLatinTokens
                    int bsIdx = Array.BinarySearch(_sortedLatinTokens, anchorTok, StringComparer.OrdinalIgnoreCase);
                    int startIdx = bsIdx >= 0 ? bsIdx + 1 : ~bsIdx;
                    for (int k = startIdx; k < _sortedLatinTokens.Length && candidatePool.Count < maxResults * 4; k++)
                    {
                        string keyTok = _sortedLatinTokens[k];
                        if (!keyTok.StartsWith(anchorTok, StringComparison.OrdinalIgnoreCase))
                            break;
                        if (_latinTokenIndex.TryGetValue(keyTok, out var prefBucket))
                        {
                            for (int i = 0; i < prefBucket.Count; i++)
                                AddCandidate(prefBucket[i]);
                        }
                    }

                    // Mid-token substring fallback only when exact + prefix yielded zero candidates
                    if (candidatePool.Count == 0)
                    {
                        for (int k = 0; k < _sortedLatinTokens.Length; k++)
                        {
                            string keyTok = _sortedLatinTokens[k];
                            if (keyTok.IndexOf(anchorTok, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                _latinTokenIndex.TryGetValue(keyTok, out var subBucket))
                            {
                                for (int i = 0; i < subBucket.Count; i++)
                                    AddCandidate(subBucket[i]);
                            }
                        }
                    }
                }

                var exactEnMatches = new List<DictionaryEntry>();
                var partialEnMatches = new List<DictionaryEntry>();
                string parenToken = "(" + norm + ")";
                string parenSlashToken = "(" + norm + " ";
                string iso2Token = "ISO " + norm + " ";
                string iso3Token = "/ " + norm + " ";
                string dialExactToken = "ဖုန်းကုဒ် " + norm + ")";
                string dialPrefixToken = "ဖုန်းကုဒ် " + norm + "-";

                for (int i = 0; i < candidatePool.Count; i++)
                {
                    var e = candidatePool[i];
                    if (isCallingCodeQuery)
                    {
                        if (!string.IsNullOrEmpty(e.UsageNote))
                        {
                            if (e.UsageNote.IndexOf(dialExactToken, StringComparison.OrdinalIgnoreCase) >= 0)
                                exactEnMatches.Add(e);
                            else if (e.UsageNote.IndexOf(dialPrefixToken, StringComparison.OrdinalIgnoreCase) >= 0)
                                partialEnMatches.Add(e);
                        }
                        continue;
                    }

                    string enField = e.EnglishName;
                    if (!string.IsNullOrEmpty(enField))
                    {
                        if (string.Equals(enField, norm, StringComparison.OrdinalIgnoreCase) ||
                            enField.StartsWith(norm + " ", StringComparison.OrdinalIgnoreCase) ||
                            enField.IndexOf(parenToken, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            exactEnMatches.Add(e);
                            continue;
                        }
                        if (enField.StartsWith(norm, StringComparison.OrdinalIgnoreCase) ||
                            enField.IndexOf(norm, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            partialEnMatches.Add(e);
                            continue;
                        }
                    }
                    if ((!string.IsNullOrEmpty(e.MeaningCue) && e.MeaningCue.IndexOf(norm, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(e.UsageNote) && e.UsageNote.IndexOf(norm, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        bool isExactCodeOrToken =
                            (!string.IsNullOrEmpty(e.MeaningCue) &&
                             (e.MeaningCue.IndexOf(parenToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              e.MeaningCue.IndexOf(parenSlashToken, StringComparison.OrdinalIgnoreCase) >= 0)) ||
                            (!string.IsNullOrEmpty(e.UsageNote) &&
                             (e.UsageNote.IndexOf(parenToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              e.UsageNote.IndexOf(parenSlashToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              e.UsageNote.IndexOf(iso2Token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                              e.UsageNote.IndexOf(iso3Token, StringComparison.OrdinalIgnoreCase) >= 0));
                        if (isExactCodeOrToken)
                            exactEnMatches.Add(e);
                        else
                            partialEnMatches.Add(e);
                    }
                }

                foreach (var e in exactEnMatches
                    .OrderByDescending(x => !string.IsNullOrEmpty(x.EnglishName) && string.Equals(x.EnglishName, norm, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.IsProperNoun || x.IsSpecializedDomain)
                    .ThenBy(x => x.Word.Length)
                    .ThenByDescending(x => x.Frequency))
                {
                    AddUnique(e);
                    if (results.Count >= maxResults) break;
                }

                if (results.Count < maxResults)
                {
                    foreach (var e in partialEnMatches
                        .OrderByDescending(x => x.IsProperNoun || x.IsSpecializedDomain)
                        .ThenByDescending(x => x.Frequency)
                        .ThenBy(x => x.Word.Length))
                    {
                        AddUnique(e);
                        if (results.Count >= maxResults) break;
                    }
                }

                return results.Take(maxResults).ToList();
            }

            var qUnits = new List<string>(8);

            // 1b. Sub-millisecond exact or prefix matches in Offline Reference Corpus (စကားပုံ / ဆိုရိုးစကား / စွယ်စုံဗဟုသုတ ENCYC)
            if (ReferenceCount > 0 && norm.Length >= 2)
            {
                EnsureSearchAndSynonymIndexBuilt();

                if (_referenceExactIndex.TryGetValue(norm, out var exactRefs))
                {
                    for (int i = 0; i < exactRefs.Count; i++)
                        AddUnique(exactRefs[i]);
                }

                if (norm.Length >= 4)
                {
                    ExtractMyanmarSyllableUnits(norm, qUnits);
                    if (qUnits.Count > 0 && _referenceSyllableIndex.TryGetValue(qUnits[0], out var firstSylRefs))
                    {
                        int directPrefAdded = 0;
                        for (int i = 0; i < firstSylRefs.Count && directPrefAdded < 5; i++)
                        {
                            var refEntry = firstSylRefs[i];
                            if (refEntry.Word.StartsWith(norm, StringComparison.Ordinal))
                            {
                                AddUnique(refEntry);
                                directPrefAdded++;
                            }
                        }
                    }
                }
            }

            // Reserve up to 8 slots at the bottom of results for matching Proverbs / Sayings & Encyclopedic Knowledge articles
            int lexicalCap = ReferenceCount > 0 ? Math.Max(8, maxResults - 8) : maxResults;

            // 2. Strict & Relaxed Phonetic homophone matches (e.g. 'ချည်' -> 'ချဉ် [အရသာ~]', 'ကဏန်း' -> 'ဂဏန်း')
            foreach (var e in LookupStrictHomophones(norm)
                .OrderByDescending(x => !string.IsNullOrEmpty(x.MeaningCue))
                .ThenByDescending(x => x.Frequency))
            {
                AddUnique(e);
                if (results.Count >= lexicalCap) break;
            }
            if (results.Count < lexicalCap)
            {
                foreach (var e in LookupByPhoneticKey(norm)
                    .OrderByDescending(x => !string.IsNullOrEmpty(x.MeaningCue))
                    .ThenByDescending(x => x.Frequency))
                {
                    AddUnique(e);
                    if (results.Count >= lexicalCap) break;
                }
            }

            // 3. Prefix matches via O(1) Syllable Trie walk (including partial trailing syllable expansion without scanning _exactWordIndex)
            if (results.Count < lexicalCap)
            {
                var prefixMatches = new List<DictionaryEntry>();
                var querySyls = MyanmarSyllableBreaker.BreakIntoSyllables(norm);
                if (querySyls.Count > 0)
                {
                    TrieNode? cur = _root;
                    TrieNode? parentBeforeLast = _root;
                    int matchedDepth = 0;
                    for (int s = 0; s < querySyls.Count && cur != null; s++)
                    {
                        if (s == querySyls.Count - 1)
                            parentBeforeLast = cur;
                        if (cur.TryGetChild(querySyls[s], out TrieNode? nextNode))
                        {
                            cur = nextNode;
                            matchedDepth++;
                        }
                        else
                        {
                            cur = null;
                        }
                    }

                    var stack = new Stack<TrieNode>();
                    if (cur != null)
                        stack.Push(cur);

                    if (matchedDepth >= querySyls.Count - 1 && parentBeforeLast != null)
                    {
                        string lastSylPrefix = querySyls[querySyls.Count - 1];
                        parentBeforeLast.PushMatchingChildren(stack, lastSylPrefix, requireLongerThanPrefix: true);
                    }

                    if (stack.Count == 0 && norm.Length >= 3 && norm.Length <= 6 && norm[norm.Length - 1] == '\u103A' &&
                        _root.TryGetChild(norm.Substring(0, norm.Length - 2), out TrieNode? kinziBaseNode) &&
                        kinziBaseNode != null)
                    {
                        string kinziLead = norm.Substring(norm.Length - 2);
                        kinziBaseNode.PushMatchingChildren(stack, kinziLead, requireLongerThanPrefix: false);
                    }

                    while (stack.Count > 0 && prefixMatches.Count < 150)
                    {
                        TrieNode node = stack.Pop();
                        if (node.Entries != null)
                        {
                            for (int ei = 0; ei < node.Entries.Count; ei++)
                            {
                                if (node.Entries[ei].Word.StartsWith(norm, StringComparison.Ordinal))
                                    prefixMatches.Add(node.Entries[ei]);
                            }
                        }
                        node.PushMatchingChildren(stack, prefixFilter: null, requireLongerThanPrefix: false);
                    }
                }

                foreach (var e in prefixMatches
                    .OrderByDescending(x => !string.IsNullOrEmpty(x.MeaningCue))
                    .ThenBy(x => x.Word.Length)
                    .ThenByDescending(x => x.Frequency))
                {
                    AddUnique(e);
                    if (results.Count >= lexicalCap) break;
                }
            }

            // 3b. O(1) Bi-directional Burmese Synonym cross-reference (e.g. 'ဦးပဉ္စင်း' -> 'ရဟန်း')
            if (results.Count < lexicalCap)
            {
                EnsureSearchAndSynonymIndexBuilt();
                if (_synonymIndex.TryGetValue(norm, out var synWords))
                {
                    for (int si = 0; si < synWords.Count && results.Count < lexicalCap; si++)
                    {
                        if (_exactWordIndex.TryGetValue(synWords[si], out var synEntries))
                        {
                            for (int ei = 0; ei < synEntries.Count && results.Count < lexicalCap; ei++)
                                AddUnique(synEntries[ei]);
                        }
                    }
                }
            }

            // 4. Sub-millisecond Syllable-Inverted Substring & Myanmar-to-Myanmar Definition (MeaningCue/UsageNote) Lookup
            if (results.Count < lexicalCap && norm.Length >= 2)
            {
                EnsureSearchAndSynonymIndexBuilt();
                if (qUnits.Count == 0)
                    ExtractMyanmarSyllableUnits(norm, qUnits);

                if (qUnits.Count > 0)
                {
                    // 4a. Headword substring matches via rarest syllable unit in _lexicalHeadwordSyllableIndex
                    var bestHwBucket = PickSmallestSyllableBucket(_lexicalHeadwordSyllableIndex, qUnits, skipStopSyllables: false, out int hwPickedIdx);
                    int hwMatched = 0;
                    if (bestHwBucket != null)
                    {
                        for (int i = 0; i < bestHwBucket.Count && results.Count < lexicalCap; i++)
                        {
                            var cand = bestHwBucket[i];
                            if (cand.Word.IndexOf(norm, StringComparison.Ordinal) >= 0)
                            {
                                AddUnique(cand);
                                hwMatched++;
                            }
                        }
                    }
                    if (hwMatched == 0 && hwPickedIdx == qUnits.Count - 1 && qUnits.Count >= 2 && results.Count < lexicalCap)
                    {
                        var fallbackHwBucket = PickSmallestSyllableBucket(_lexicalHeadwordSyllableIndex, qUnits, skipStopSyllables: false, out _, qUnits.Count - 1);
                        if (fallbackHwBucket != null)
                        {
                            for (int i = 0; i < fallbackHwBucket.Count && results.Count < lexicalCap; i++)
                            {
                                var cand = fallbackHwBucket[i];
                                if (cand.Word.IndexOf(norm, StringComparison.Ordinal) >= 0)
                                    AddUnique(cand);
                            }
                        }
                    }

                    // 4b. Myanmar-to-Myanmar MeaningCue / UsageNote matches via rarest non-stop syllable unit in _lexicalCueSyllableIndex
                    if (results.Count < lexicalCap)
                    {
                        var bestCueBucket = PickSmallestSyllableBucket(_lexicalCueSyllableIndex, qUnits, skipStopSyllables: true, out int cuePickedIdx);
                        int cueMatched = 0;
                        if (bestCueBucket != null)
                        {
                            for (int i = 0; i < bestCueBucket.Count && results.Count < lexicalCap; i++)
                            {
                                var cand = bestCueBucket[i];
                                if ((!string.IsNullOrEmpty(cand.MeaningCue) && cand.MeaningCue.IndexOf(norm, StringComparison.Ordinal) >= 0) ||
                                    (!string.IsNullOrEmpty(cand.UsageNote) && cand.UsageNote.IndexOf(norm, StringComparison.Ordinal) >= 0))
                                {
                                    AddUnique(cand);
                                    cueMatched++;
                                }
                            }
                        }
                        if (cueMatched == 0 && cuePickedIdx == qUnits.Count - 1 && qUnits.Count >= 2 && results.Count < lexicalCap)
                        {
                            var fallbackCueBucket = PickSmallestSyllableBucket(_lexicalCueSyllableIndex, qUnits, skipStopSyllables: true, out _, qUnits.Count - 1);
                            if (fallbackCueBucket != null)
                            {
                                for (int i = 0; i < fallbackCueBucket.Count && results.Count < lexicalCap; i++)
                                {
                                    var cand = fallbackCueBucket[i];
                                    if ((!string.IsNullOrEmpty(cand.MeaningCue) && cand.MeaningCue.IndexOf(norm, StringComparison.Ordinal) >= 0) ||
                                        (!string.IsNullOrEmpty(cand.UsageNote) && cand.UsageNote.IndexOf(norm, StringComparison.Ordinal) >= 0))
                                    {
                                        AddUnique(cand);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Non-syllabic query (e.g. Myanmar digits '၁.၆၃') bounded scan
                    int scanned = 0;
                    foreach (var kvp in _exactWordIndex)
                    {
                        if (++scanned > 2000) break;
                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            var e = kvp.Value[i];
                            if ((!string.IsNullOrEmpty(e.MeaningCue) && e.MeaningCue.IndexOf(norm, StringComparison.Ordinal) >= 0) ||
                                (!string.IsNullOrEmpty(e.UsageNote) && e.UsageNote.IndexOf(norm, StringComparison.Ordinal) >= 0))
                            {
                                AddUnique(e);
                            }
                        }
                        if (results.Count >= lexicalCap) break;
                    }
                }
            }

            // 5. Sub-millisecond Syllable-Indexed Proverbs, Sayings & Encyclopedic Knowledge matches
            if (ReferenceCount > 0 && results.Count < maxResults)
            {
                int remainingSlots = Math.Max(4, maxResults - results.Count);
                int halfSlots = Math.Max(2, remainingSlots / 2);

                var proverbHits = SearchReferenceCorpus(norm, includeProverbs: true, includeEncyclopedia: false, maxResults: halfSlots);
                for (int i = 0; i < proverbHits.Count && results.Count < maxResults; i++)
                    AddUnique(proverbHits[i]);

                var encycHits = SearchReferenceCorpus(norm, includeProverbs: false, includeEncyclopedia: true, maxResults: maxResults - results.Count);
                for (int i = 0; i < encycHits.Count && results.Count < maxResults; i++)
                    AddUnique(encycHits[i]);
            }

            return results.Take(maxResults).ToList();
        }

        /// <summary>
        /// Sub-millisecond inverted-index search across offline Proverbs/Sayings and Encyclopedic Knowledge (ENCYC) entries.
        /// </summary>
        public List<DictionaryEntry> SearchReferenceCorpus(
            string query,
            bool includeProverbs = true,
            bool includeEncyclopedia = true,
            int maxResults = 35)
        {
            var results = new List<DictionaryEntry>();
            if (string.IsNullOrWhiteSpace(query) || ReferenceCount == 0 || maxResults <= 0)
                return results;

            EnsureSearchAndSynonymIndexBuilt();

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(query.Trim()));
            var seen = new HashSet<string>(StringComparer.Ordinal);

            bool MatchesFilter(DictionaryEntry e) =>
                (includeProverbs && e.IsProverbOrSayingEntry) ||
                (includeEncyclopedia && e.IsEncyclopediaEntry);

            void AddRefUnique(DictionaryEntry e)
            {
                if (!MatchesFilter(e))
                    return;
                if (seen.Add($"{e.Word}|{e.PosTag}"))
                    results.Add(e);
            }

            // 1. Exact title match
            if (_referenceExactIndex.TryGetValue(norm, out var exBucket))
            {
                for (int i = 0; i < exBucket.Count; i++)
                    AddRefUnique(exBucket[i]);
            }

            // 2. Latin/English query over _latinTokenIndex filtered to reference entries
            bool isLatin = false;
            for (int ci = 0; ci < norm.Length; ci++)
            {
                char ch = norm[ci];
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    isLatin = true;
                    break;
                }
            }
            if (isLatin)
            {
                foreach (Match qm in AsciiTokenExtractRegex.Matches(norm))
                {
                    string qt = qm.Value.TrimEnd('-');
                    if (qt.Length >= 2 && _latinTokenIndex.TryGetValue(qt, out var latBucket))
                    {
                        for (int i = 0; i < latBucket.Count && results.Count < maxResults; i++)
                        {
                            if (latBucket[i].IsReferenceEntry)
                                AddRefUnique(latBucket[i]);
                        }
                    }
                }
                return results.Take(maxResults).ToList();
            }

            // 3. Syllable-anchored title inverted index lookup (pick rarest/smallest bucket among query syllable units)
            var qUnits = new List<string>(8);
            ExtractMyanmarSyllableUnits(norm, qUnits);

            var bestTitleBucket = PickSmallestSyllableBucket(_referenceSyllableIndex, qUnits, skipStopSyllables: false, out _);
            if (bestTitleBucket != null)
            {
                var prefixList = new List<DictionaryEntry>();
                var substrList = new List<DictionaryEntry>();
                for (int i = 0; i < bestTitleBucket.Count; i++)
                {
                    var re = bestTitleBucket[i];
                    if (!MatchesFilter(re))
                        continue;
                    if (re.Word.StartsWith(norm, StringComparison.Ordinal))
                        prefixList.Add(re);
                    else if (re.Word.IndexOf(norm, StringComparison.Ordinal) >= 0 || (qUnits.Count == 1 && norm.Length <= 4))
                        substrList.Add(re);
                }

                foreach (var p in prefixList.OrderBy(x => x.Word.Length))
                {
                    AddRefUnique(p);
                    if (results.Count >= maxResults) break;
                }
                if (results.Count < maxResults)
                {
                    foreach (var sub in substrList.OrderBy(x => x.Word.Length))
                    {
                        AddRefUnique(sub);
                        if (results.Count >= maxResults) break;
                    }
                }
            }

            // 4. Syllable-anchored MeaningCue inverted index lookup across all 26,900+ Proverbs & Encyclopedia articles
            if (results.Count < maxResults && qUnits.Count > 0)
            {
                var bestRefCueBucket = PickSmallestSyllableBucket(_referenceCueSyllableIndex, qUnits, skipStopSyllables: true, out _);
                if (bestRefCueBucket != null)
                {
                    for (int i = 0; i < bestRefCueBucket.Count && results.Count < maxResults; i++)
                    {
                        var refEntry = bestRefCueBucket[i];
                        if (!MatchesFilter(refEntry))
                            continue;
                        if (!string.IsNullOrEmpty(refEntry.MeaningCue) &&
                            refEntry.MeaningCue.IndexOf(norm, StringComparison.Ordinal) >= 0)
                        {
                            AddRefUnique(refEntry);
                        }
                    }
                }
            }
            else if (results.Count < maxResults && qUnits.Count == 0)
            {
                int scanLimit = Math.Min(_referenceCorpus.Count, 800);
                for (int i = 0; i < scanLimit && results.Count < maxResults; i++)
                {
                    var refEntry = _referenceCorpus[i];
                    if (!MatchesFilter(refEntry))
                        continue;
                    if (refEntry.Word.IndexOf(norm, StringComparison.Ordinal) >= 0 ||
                        (!string.IsNullOrEmpty(refEntry.MeaningCue) && refEntry.MeaningCue.IndexOf(norm, StringComparison.Ordinal) >= 0))
                    {
                        AddRefUnique(refEntry);
                    }
                }
            }

            return results.Take(maxResults).ToList();
        }

        private static List<DictionaryEntry>? PickSmallestSyllableBucket(
            Dictionary<string, List<DictionaryEntry>> index,
            List<string> units,
            bool skipStopSyllables,
            out int pickedUnitIndex,
            int maxUnitCount = -1)
        {
            pickedUnitIndex = -1;
            int limit = maxUnitCount >= 0 && maxUnitCount < units.Count ? maxUnitCount : units.Count;
            if (limit <= 0)
                return null;

            List<DictionaryEntry>? smallest = null;
            for (int i = 0; i < limit; i++)
            {
                string u = units[i];
                if (skipStopSyllables && CueStopSyllables.Contains(u))
                    continue;

                if (index.TryGetValue(u, out var bucket))
                {
                    if (smallest == null || bucket.Count < smallest.Count)
                    {
                        smallest = bucket;
                        pickedUnitIndex = i;
                    }
                }
                else if (i < limit - 1)
                {
                    // An interior/leading syllable unit is completely absent from the index -> no entry can contain the full query
                    pickedUnitIndex = -1;
                    return null;
                }
            }

            return smallest;
        }

        private static bool IsMyanmarBaseChar(char c) =>
            (c >= '\u1000' && c <= '\u102A') || c == '\u103F' || (c >= '\u104C' && c <= '\u104F');

        private static void ExtractMyanmarSyllableUnits(string text, List<string> dest)
        {
            dest.Clear();
            if (string.IsNullOrEmpty(text))
                return;

            int len = text.Length;
            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if (!IsMyanmarBaseChar(c))
                {
                    i++;
                    continue;
                }

                int start = i;
                if (c == '\u1004' && i + 3 < len && text[i + 1] == '\u103A' && text[i + 2] == '\u1039' && IsMyanmarBaseChar(text[i + 3]))
                {
                    i += 4;
                }
                else
                {
                    i++;
                }

                while (i < len)
                {
                    char m = text[i];
                    if (m == '\u1039' && i + 1 < len && IsMyanmarBaseChar(text[i + 1]))
                    {
                        i += 2;
                    }
                    else if (m >= '\u102B' && m <= '\u103E')
                    {
                        i++;
                    }
                    else if ((m >= '\u1000' && m <= '\u1021') || m == '\u1025')
                    {
                        if (m == '\u1004' && i + 2 < len && text[i + 1] == '\u103A' && text[i + 2] == '\u1039')
                            break;

                        if (i + 1 < len && text[i + 1] == '\u103A' && !(i + 2 < len && text[i + 2] == '\u1039'))
                        {
                            i += 2;
                            while (i < len && (text[i] == '\u1037' || text[i] == '\u1038' || text[i] == '\u103A'))
                                i++;
                        }
                        else if (i + 2 < len && text[i + 1] == '\u103B' && text[i + 2] == '\u103A')
                        {
                            i += 3;
                            while (i < len && (text[i] == '\u1037' || text[i] == '\u1038' || text[i] == '\u103A'))
                                i++;
                        }
                        else if (i + 2 < len && text[i + 1] == '\u1037' && text[i + 2] == '\u103A')
                        {
                            i += 3;
                            while (i < len && (text[i] == '\u1037' || text[i] == '\u1038' || text[i] == '\u103A'))
                                i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                dest.Add(text.Substring(start, i - start));
            }
        }

        public void MatchLongestAt(
            IReadOnlyList<SyllableToken> tokens,
            int startIndex,
            out int matchedSyllableCount,
            out List<DictionaryEntry>? matchedEntries,
            out OrthographyRule? matchedWrongRule)
        {
            matchedSyllableCount = 0;
            matchedEntries = null;
            matchedWrongRule = null;

            TrieNode current = _root;
            int cursor = startIndex;

            while (cursor < tokens.Count && tokens[cursor].Kind == TokenKind.MyanmarSyllable)
            {
                string syl = tokens[cursor].NormalizedText;
                if (!current.TryGetChild(syl, out TrieNode? next) || next == null)
                    break;

                current = next;
                int span = (cursor - startIndex) + 1;

                if (current.WrongFormRule != null || (current.Entries != null && current.Entries.Count > 0))
                {
                    if (!IsFalsePolitePaCompoundMatch(tokens, cursor, span, current.Entries, current.WrongFormRule))
                    {
                        matchedSyllableCount = span;
                        matchedEntries = current.Entries;
                        matchedWrongRule = current.WrongFormRule;
                    }
                }

                cursor++;
            }
        }

        /// <summary>
        /// Enumerates all exact headword and explicit rule spans starting at startIndex (up to maxLen syllables) for Viterbi lattice construction.
        /// </summary>
        public void MatchSpansAt(
            IReadOnlyList<SyllableToken> tokens,
            int startIndex,
            int maxLen,
            List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> results)
        {
            results.Clear();
            TrieNode current = _root;
            int limit = Math.Min(tokens.Count, startIndex + maxLen);

            for (int cursor = startIndex; cursor < limit; cursor++)
            {
                if (tokens[cursor].Kind != TokenKind.MyanmarSyllable)
                    break;

                string syl = tokens[cursor].NormalizedText;
                if (!current.TryGetChild(syl, out TrieNode? next) || next == null)
                    break;

                current = next;
                int span = (cursor - startIndex) + 1;

                if (current.WrongFormRule != null || (current.Entries != null && current.Entries.Count > 0))
                {
                    if (!IsFalsePolitePaCompoundMatch(tokens, cursor, span, current.Entries, current.WrongFormRule))
                    {
                        results.Add((span, current.Entries, current.WrongFormRule));
                    }
                }
            }
        }

        private static bool IsFalsePolitePaCompoundMatch(
            IReadOnlyList<SyllableToken> tokens,
            int endTokenIndex,
            int span,
            List<DictionaryEntry>? entries,
            OrthographyRule? wrongRule)
        {
            // Prevent nouns ending in 'ပါ' (such as 'တင်ပါ' = buttocks) from absorbing polite verb particle 'ပါ' before 'တယ်/သည်/မယ်/မည်/ဦး'
            if (span >= 2 && wrongRule == null && entries != null &&
                tokens[endTokenIndex].NormalizedText == "ပါ" &&
                endTokenIndex + 1 < tokens.Count &&
                tokens[endTokenIndex + 1].Kind == TokenKind.MyanmarSyllable &&
                PoliteVerbFollowersAfterPa.Contains(tokens[endTokenIndex + 1].NormalizedText))
            {
                bool isVerbParticle = entries.Any(e => string.Equals(e.PosTag, "PART", StringComparison.OrdinalIgnoreCase));
                if (!isVerbParticle)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Strict tone-preserving Burmese homophone skeleton used for automatic compound homophone error detection.
        /// Unifies Pali dental/retroflex stacks, geminated stacks, -တ်/-ပ်/-က် stop codas, -န်/-မ်/-ဏ်/-ံ nasals, and -ည်/-ဉ်/-ီ/-ေ rhymes.
        /// </summary>
        public static string ComputeStrictHomophoneKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            if (text.Length <= 40 && s_strictSkeletonCache.TryGetValue(text, out string? cached))
                return cached;
            string computed = BuildSkeleton(text, ignoreTones: false);
            if (text.Length <= 40)
            {
                if (s_strictSkeletonCache.Count > 16384)
                    s_strictSkeletonCache.Clear();
                s_strictSkeletonCache[text] = computed;
            }
            return computed;
        }

        /// <summary>
        /// Relaxed phonetic/orthographic skeleton used for Suggestion ranking, Dictionary Homophone Search, and 3+ syllable generative repair.
        /// </summary>
        public static string ComputePhoneticKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            if (text.Length <= 40 && s_phoneticSkeletonCache.TryGetValue(text, out string? cached))
                return cached;
            string computed = BuildSkeleton(text, ignoreTones: true);
            if (text.Length <= 40)
            {
                if (s_phoneticSkeletonCache.Count > 16384)
                    s_phoneticSkeletonCache.Clear();
                s_phoneticSkeletonCache[text] = computed;
            }
            return computed;
        }

        private static bool IsSilentOiCodaConsonant(char c) =>
            c == '\u101C' || c == '\u101F' || c == '\u101B' || c == '\u1003' || c == '\u1020';

        private static string BuildSkeleton(string text, bool ignoreTones, bool isPreNormalized = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string s = isPreNormalized
                ? text
                : UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(text));

            // 0. Normalize Pali/Sanskrit stacked consonants so flattened user spellings (ပုဂိုလ်, မေတာ, သဏ္ဍာန်, ဥကဌ, အကျီ, သကြန်) match!
            // In relaxed phonetic mode, strip Kinzi (င်္) so omitted-Kinzi spellings (အကျီ -> အင်္ကျီ, သကြန် -> သင်္ကြန်) share the same PhoneticKey;
            // in strict homophone mode, preserve Kinzi as င် so အင်ကျီ <-> အင်္ကျီ match without merging သဘော with သင်္ဘော.
            if (s.IndexOf('\u1039') >= 0)
            {
                s = ignoreTones
                    ? s.Replace("\u1004\u103A\u1039", string.Empty)
                    : s.Replace("\u1004\u103A\u1039", "\u1004\u103A");
                s = NasalStackRegex.Replace(s, "\u1036$1"); // ဏ္ဌ/န္တ/မ္ပ -> ံ + C
                s = GeminatedStackRegex.Replace(s, "$2");  // က္က->က, ဂ္ဂ->ဂ, စ္စ->စ, တ္တ->တ, ဋ္ဌ->ဌ, ပ္ပ->ပ, မ္မ->မ, လ္လ->လ
            }

            StringBuilder? sb = t_skeletonSb;
            if (sb == null)
            {
                sb = new StringBuilder(32);
                t_skeletonSb = sb;
            }
            else
            {
                sb.Clear();
            }

            int len = s.Length;
            for (int i = 0; i < len; i++)
            {
                char c = s[i];

                // 1. Handle '\u102D' (-ိုလ်/-ိုဟ်/-ိုရ်/-ိုဃ်/-ိုဠ် -> O, -ို -> O, -ိင် -> -ိံ/-ီံ, standalone -ိ)
                if (c == '\u102D')
                {
                    if (i + 1 < len && s[i + 1] == '\u102F')
                    {
                        if (i + 3 < len &&
                            s[i + 3] == '\u103A' &&
                            IsSilentOiCodaConsonant(s[i + 2]) &&
                            (i + 4 >= len || (s[i + 4] != '\u1037' && s[i + 4] != '\u1038')))
                        {
                            sb.Append('O');
                            i += 3;
                        }
                        else
                        {
                            sb.Append('O');
                            i += 1;
                        }
                        continue;
                    }
                    if (i + 2 < len && s[i + 1] == '\u1004' && s[i + 2] == '\u103A')
                    {
                        sb.Append(ignoreTones ? '\u102E' : '\u102D');
                        sb.Append('\u1036');
                        i += 2;
                        continue;
                    }
                    sb.Append(ignoreTones ? '\u102E' : '\u102D');
                    continue;
                }

                // 2. Handle '\u1031' (-ော/-ေါ -> A, open -ေ -> ီ)
                if (c == '\u1031')
                {
                    if (i + 1 < len && (s[i + 1] == '\u102B' || s[i + 1] == '\u102C'))
                    {
                        sb.Append('A');
                        i += 1;
                    }
                    else
                    {
                        sb.Append('\u102E');
                    }
                    continue;
                }

                // 3. Handle 2-char C + '\u103A' (asat) palatal/nasal/stop codas in a single lookahead
                if (i + 1 < len && s[i + 1] == '\u103A')
                {
                    switch (c)
                    {
                        case '\u100A': // ည် -> ီ
                        case '\u1009': // ဉ် -> ီ
                            sb.Append('\u102E');
                            i += 1;
                            continue;
                        case '\u101A': // ယ် -> ီ (relaxed mode)
                            if (ignoreTones)
                            {
                                sb.Append('\u102E');
                                i += 1;
                                continue;
                            }
                            break;
                        case '\u1014': // န် -> ံ
                        case '\u1019': // မ် -> ံ
                        case '\u100F': // ဏ် -> ံ
                            sb.Append('\u1036');
                            i += 1;
                            continue;
                        case '\u1000': // က် -> Q
                        case '\u1010': // တ် -> Q
                        case '\u1015': // ပ် -> Q
                        case '\u1002': // ဂ် -> Q
                        case '\u1012': // ဒ် -> Q
                        case '\u1017': // ဗ် -> Q
                        case '\u1018': // ဘ် -> Q
                        case '\u101E': // သ် -> Q
                            sb.Append('Q');
                            i += 1;
                            continue;
                        case '\u1005': // စ် -> Q (relaxed mode only; keeps သစ် distinct from သတ်/သက် in strict mode)
                            if (ignoreTones)
                            {
                                sb.Append('Q');
                                i += 1;
                                continue;
                            }
                            break;
                    }
                }

                switch (c)
                {
                    case '\u1032': // ဲ -> ီ in relaxed mode
                        sb.Append(ignoreTones ? '\u102E' : c);
                        break;
                    case '\u1001': // ခ
                    case '\u1002': // ဂ
                    case '\u1003': // ဃ
                        sb.Append(ignoreTones ? '\u1000' : c);
                        break;
                    case '\u1006': // ဆ
                    case '\u1007': // ဇ
                    case '\u1008': // ဈ
                        sb.Append(ignoreTones ? '\u1005' : c);
                        break;
                    case '\u1016': // ဖ
                    case '\u1017': // ဗ
                    case '\u1018': // ဘ
                        sb.Append(ignoreTones ? '\u1015' : c);
                        break;
                    case '\u101B': // ရ -> ယ
                        sb.Append('\u101A');
                        break;
                    case '\u103C': // ြ -> ျ
                        sb.Append('\u103B');
                        break;
                    case '\u1009': // ဉ -> ည
                        sb.Append('\u100A');
                        break;
                    case '\u103F': // ဿ -> သ
                        sb.Append('\u101E');
                        break;
                    case '\u100B': // ဋ -> တ
                    case '\u100C': // ဌ -> တ
                    case '\u100D': // ဍ -> တ
                    case '\u100E': // ဎ -> တ
                    case '\u1011': // ထ -> တ (in relaxed; in strict unify Pali retroflexes ဋ/ဌ/ဍ/ဎ/ဓ with တ/ထ/ဒ)
                    case '\u1012': // ဒ
                    case '\u1013': // ဓ
                        if (c == '\u1011' && !ignoreTones) sb.Append('\u1011');
                        else sb.Append('\u1010');
                        break;
                    case '\u100F': // ဏ -> န
                        sb.Append('\u1014');
                        break;
                    case '\u1020': // ဠ -> လ
                        sb.Append('\u101C');
                        break;
                    case '\u102B': // ါ -> ာ
                        sb.Append('\u102C');
                        break;
                    case '\u1030': // ူ -> ု
                        if (ignoreTones) sb.Append('\u102F');
                        else sb.Append(c);
                        break;
                    case '\u103E': // ှ aspiration omitted frequently by typists
                        break;
                    case '\u1037':
                    case '\u1038':
                        if (!ignoreTones) sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
