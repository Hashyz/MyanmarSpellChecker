// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

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
    /// <summary>
    /// Unified Myanmar Spell & Grammar Proofing Engine grounded in the complete "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း",
    /// real-world Myanmar corpus statistics, and dual style modes (General Standard vs Formal/Official).
    /// Uses Viterbi Word-Lattice Segmentation + Multi-Scale Noisy-Channel Cohesion to detect arbitrary multi-syllable
    /// tone omissions, medial omissions, prefix-trapped compounds, homophones, and lookalikes with zero predefined WrongForms.
    /// </summary>
    public sealed class MyanmarProofingEngine
    {
        private static readonly int FuzzyWindowCacheCap = !Environment.Is64BitProcess ? 4096 : 12288;

        private const int ViterbiBeamWidth = 3;

        private struct ViterbiBeamSlot
        {
            public double Cost;
            public int PrevPos;
            public byte PrevBeam;
            public MyanmarUposTag PrevTag;
            public MyanmarUposTag CurTag;
            public ulong WordHash;
            public LatticeEdge Edge;
        }

        private sealed class ProofingScratchWorkspace
        {
            public bool InUse;
            public readonly List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> MatchBuf =
                new List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)>(16);
            public readonly List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> AllTokenSpans =
                new List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)>(128);
            public int[] TokenSpanOffsets = new int[128];
            public int[] LongestMatchLengths = new int[128];
            public bool[] InsideKnownWord = new bool[128];
            public bool[] PersonNameMask = new bool[128];
            public readonly List<(int Start, int End)> OfficialSpans = new List<(int Start, int End)>(16);
            public readonly List<(int Start, int End)> ExactMultiWordSpans = new List<(int Start, int End)>(16);

            public ViterbiBeamSlot[] BeamTable = new ViterbiBeamSlot[64 * ViterbiBeamWidth];
            public byte[] BeamCounts = new byte[64];
            public ushort[] ExactMultiWordBits = new ushort[64];
            public readonly List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> ClauseExactSpans =
                new List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)>(64);
            public int[] ClauseSpanOffsets = new int[64];
            public readonly List<LatticeEdge> CandidateEdges = new List<LatticeEdge>(12);
            public readonly List<ProofingIssue> ChosenIssues = new List<ProofingIssue>(8);
        }

        [ThreadStatic]
        private static ProofingScratchWorkspace? t_workspace;

        private MyanmarTrie _trie;
        private SuggestionEngine _suggestionEngine;
        private readonly OfficialStyleGrammarEngine _grammarEngine;
        private readonly ContextDisambiguator _contextDisambiguator;
        private readonly ReaderWriterLockSlim _lexiconRwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly HashSet<string> _sessionIgnoredWords = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _customUserWords = new HashSet<string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, List<ProofingIssue>> _paragraphCache = new ConcurrentDictionary<string, List<ProofingIssue>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<(string SylKey, ushort Flags), (DictionaryEntry? Best, double Dist)> _fuzzyWindowCache =
            new ConcurrentDictionary<(string SylKey, ushort Flags), (DictionaryEntry? Best, double Dist)>();
        private int _styleModeValue;
        private long _engineStateVersion;

        [ThreadStatic]
        private static HashSet<string>? t_fuzzySeenWords;

        private static HashSet<string> CreateNormalizedSet(params string[] items)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string item in items)
            {
                set.Add(UnicodeNormalizer.NormalizeCanonicalOrder(item));
            }
            return set;
        }

        private static readonly HashSet<string> GrammaticalParticles = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မည်", "ပါ", "ပါ့", "ကြ", "ခဲ့",
            "လျက်", "၍", "ကြောင့်", "ဖြင့်", "အတွက်", "အလို့ငှာ", "ငှာ", "အရ", "အပေါ်", "အောက်",
            "နှင့်အညီ", "ဖြစ်သည်", "ပါသည်", "ဖြစ်ကြောင်း", "ရှိသည်", "ပြီး", "လျှင်", "သော်လည်း",
            "သဖြင့်", "သောကြောင့်", "အဖြစ်", "ဆိုင်ရာ", "အနေဖြင့်", "ပေါ်", "ထဲ", "တိုင်အောင်",
            "သာ", "ပင်", "စွာ", "လည်း", "ဘဲ", "ပြီ", "ဟု", "ကာ", "သော်", "တည်း", "ရန်",
            "လေ", "ပေ", "ချေ", "လော", "သလော", "စီ", "မျှ", "ကုန်", "စဉ်", "လတ္တံ့", "အံ့",
            "ထက်", "ကြောင်း", "ရာ", "လော့"
        );

        // Polite everyday Burmese particles & interjections accepted in GeneralStandard mode without false spelling errors
        private static readonly HashSet<string> ColloquialParticles = CreateNormalizedSet(
            "တယ်", "မယ်", "မယ့်", "တွေ", "နဲ့", "ဘူး", "တဲ့", "တာ", "ပေါ့", "လား", "လဲ", "သလဲ", "မလဲ",
            "နော်", "ဗျ", "ဗျာ", "ခင်ဗျာ", "ခင်ဗျား", "ရှင်", "ရှင့်", "ရဲ့", "လို့", "ပဲ", "တုန်း", "သေး",
            "တော့", "ဦး", "ပါတယ်", "ပါမယ်", "ပါ့မယ်", "ပါမလား", "ပါ့မလား", "ပါမလဲ", "ပါ့မလဲ",
            "လိမ့်မယ်", "ကနေ", "ကွာ", "ကွ", "ဟေ့", "ဟ", "ဟယ်", "ဟေး", "အေ့", "ရှယ်", "ဝေါင်း", "လေ", "ဖို့", "မို့"
        );

        // Pure boundary case/sentence/post-verbal particles that must never be absorbed as the start of a repaired compound word
        // (Excludes 'စီ' and 'ပင်' which commonly start lexical roots like 'စီပွားရေး->စီးပွားရေး', 'ပင်ဆင်->ပြင်ဆင်')
        private static readonly HashSet<string> LeadingBoundaryParticles = CreateNormalizedSet(
            "ကို", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "ကနေ",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မယ့်", "မည်", "၍", "ကြောင့်", "ဖြင့်", "သဖြင့်",
            "လျက်", "လျှင်", "ခဲ့", "ဟု", "ကာ", "လည်း", "သာ", "စွာ", "ဘဲ", "ပဲ", "မျှ",
            "တွေ", "နဲ့", "ဘူး", "ပေါ့", "လား", "လော", "သလော", "နော်", "ဗျ", "ဗျာ", "ရှင်", "ရှင့်", "ရဲ့", "လို့", "တယ်", "မယ်", "ပြီ", "တာ", "တဲ့", "မို့", "လော့"
        );

        // Lexical roots that are homographs of particles when opening a compound (e.g. 'လောလောဆည်->လောလောဆယ်', 'ကာကွည်->ကာကွယ်', 'သာမာန်->သာမန်', 'တာချီလိပ်->တာချီလိတ်'),
        // permitted into compound repair ONLY when the target candidate preserves firstSyl unchanged and is Official Thatpon
        private static readonly HashSet<string> LexicalHomographLeadingRoots = CreateNormalizedSet(
            "လော", "ကာ", "သာ", "တာ", "ပဲ", "ဘူး", "ရှင်", "ဗျာ", "သော", "မယ်", "လား", "ပေါ့"
        );

        // Pure boundary case/sentence particles that must never be absorbed as the end of a repaired compound word
        private static readonly HashSet<string> TrailingBoundaryParticles = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က", "ကနေ",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မယ့်", "မည်", "ခဲ့", "၍", "ကြောင့်", "ဖြင့်", "သဖြင့်", "ဟု", "ကာ", "စွာ", "ပါ့", "လျက်", "လျှင်",
            "တွေ", "နဲ့", "ဘူး", "ပေါ့", "လား", "လော", "သလော", "လဲ", "သလဲ", "မလဲ", "နော်", "ဗျ", "ဗျာ", "ခင်ဗျာ", "ခင်ဗျား",
            "ရှင့်", "ဟေ့", "ကွာ", "ကွ", "ရဲ့", "လို့", "တယ်", "မယ်", "ပြီ", "လည်း", "ဘဲ", "ပဲ", "သေး", "ချေ", "တည်း", "ငှာ", "မျှ", "တဲ့", "မို့", "လော့"
        );

        // Post-verbal mood/tense particles that confirm a preceding 'ကြ' is the plural verb marker (e.g. 'ပြောနေကြသည်', not 'နေကျ')
        private static readonly HashSet<string> PluralVerbClosers = CreateNormalizedSet(
            "သည်", "တယ်", "မည်", "မယ်", "ပြီ", "ပြီး", "ပါ", "ဘူး", "သေး", "တော့", "နော်", "လား", "စို့", "အောင်",
            "သည့်", "သော", "မည့်", "မယ့်", "၍", "သဖြင့်", "ကြောင်း", "လို့", "တာ", "တဲ့", "ရင်", "မှ", "ဟု", "ကုန်", "၏", "လေ", "ပေ"
        );

        // Productive post-verbal auxiliaries & copulas that attach freely to standalone words (prevents 'ပိုပေး->ပို့ပေး', 'ရက်ရှိ->ရရှိ', 'သားဖြစ်->သာဖြစ်', 'ပတ်ရန်->ပတ်ရံ', 'ဖြစ်ခဲ->ဖြစ်ခဲ့', 'ပြုပြန်->ပြုပြင်')
        private static readonly HashSet<string> ProductiveVerbalAuxiliaries = CreateNormalizedSet(
            "သွား", "လာ", "ကြ", "တတ်", "ပါ", "နေ", "နိုင်", "ချင်", "ခဲ့", "ခဲ", "ပြန်", "ပြီ", "ပြီး", "သေး", "တော့", "စေ", "ဖူး", "ရ",
            "ပေး", "ထား", "မိ", "လေ", "ပေ", "ချေ", "ရန်", "ဖို့", "စဉ်", "ရှိ", "ဖြစ်", "တာ", "တဲ့", "သမား", "ဖက်"
        );

        // Burmese honorifics & monastic/historical/military/ethnic/diplomatic titles preceding personal names
        private static readonly HashSet<string> HonorificPrefixes = CreateNormalizedSet(
            "ဦး", "ဒေါ်", "ကို", "မောင်", "ဒေါက်တာ", "ပါမောက္ခ", "ဆရာ", "ဆရာမ", "ဆရာကြီး", "ဆရာမကြီး",
            "ဗိုလ်", "ဗိုလ်ကြီး", "ဗိုလ်မှူး", "ဒုတိယဗိုလ်မှူးကြီး", "ဗိုလ်မှူးကြီး", "ဗိုလ်မှူးချုပ်",
            "ဗိုလ်ချုပ်", "ဒုတိယဗိုလ်ချုပ်ကြီး", "ဗိုလ်ချုပ်ကြီး", "ဒုတိယဗိုလ်ချုပ်မှူးကြီး", "ဗိုလ်ချုပ်မှူးကြီး",
            "အသျှင်", "သျှင်", "အရှင်", "ဘဒ္ဒန္တ", "သခင်", "သူရ", "သီဟသူရ", "စည်သူ", "သီရိပျံချီ", "ဝဏ္ဏကျော်ထင်", "ဇေယျာကျော်ထင်",
            "စိုင်း", "နန်း", "ခွန်", "စဝ်", "ဆလိုင်း", "ဒူဝါ",
            "မစ္စတာ", "မစ္စက်", "မစ္စစ်", "မစ္စ", "မဒမ်", "ဒါတို", "ဒါတွတ်", "ဆမ်ဒက်ချ်",
            "ကာဒီနယ်", "ရှရီမတီ", "ရှရီ", "ဘုရင်မင်းမြတ်", "အရှင်မင်းမြတ်", "ဆူလ်တန်", "အိမ်ရှေ့မင်းသား"
        );

        // Canonical Burmese personal-name syllables used to protect unprefixed 3-syllable author/byline personal names (e.g. 'ကျော်လွင်ဦး(မိုးဇလ)', 'သင်းသင်းနွယ်')
        private static readonly HashSet<string> CommonBurmesePersonalNameSyllables = CreateNormalizedSet(
            "ဦး", "အောင်", "ကျော်", "ဇော်", "မော်", "ထွန်း", "ဝင်း", "မင်း", "လင်း", "သင်း", "နှင်း",
            "ခင်", "တင်", "လွင်", "သွင်", "မြင့်", "တင့်", "ဆင့်", "သန့်", "ခန့်", "ညွန့်",
            "စိုး", "မိုး", "ဖြိုး", "မျိုး", "သန်း", "စန်း", "နန်း", "ချမ်း", "အေး", "ဌေး", "ထွေး",
            "ရွှေ", "ငွေ", "နေ", "မေ", "ကေ", "ဝေ", "စံ", "ဟန်", "ဉာဏ်", "သက်", "ထက်", "မြတ်", "လတ်",
            "ဇင်", "ယဉ်", "သီ", "နီ", "ကြည်", "ရည်", "စည်", "ဖြူ", "သူ", "ဇာ", "သာ", "မာ", "ရာ", "ဝါ",
            "နွယ်", "ကြွယ်", "ထွဋ်"
        );

        private static readonly HashSet<string> HonorificFirstSyllables = BuildFirstSyllableSet(HonorificPrefixes);

        private static HashSet<string> BuildFirstSyllableSet(HashSet<string> phrases)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string p in phrases)
            {
                var syls = MyanmarSyllableBreaker.BreakIntoSyllables(p, isPreNormalized: true);
                if (syls.Count > 0)
                    set.Add(syls[0]);
            }
            return set;
        }

        // Titles/conjunctions/particles that can immediately precede an unspaced honorific (e.g. သမ္မတဦးဝင်းမြင့်, သူရဦးမြင့်မောင်, ဆရာတော်ဘဒ္ဒန္တ..., ဥက္ကဋ္ဌဒေါ်...)
        private static readonly HashSet<string> TitleSylBeforeHonorific = CreateNormalizedSet(
            "တ", "ကြီး", "ချုပ်", "မှူး", "ရှင်", "နှင့်", "နဲ့", "ဆရာ", "သျှင်", "အသျှင်",
            "တော်", "ဋ္ဌ", "ဝန်", "က္ခ", "ဝင်", "ခံ", "သူ", "ရ", "ထင်", "ချီ", "မှ", "က", "၏", "အား"
        );

        private static readonly HashSet<string> TwoSyllablePostpositionSeconds = CreateNormalizedSet(
            "တွက်", "ထိ", "မီ", "နက်", "တွင်း", "ပေါ်", "ကြား", "လိုက်", "တိုင်း"
        );

        // Verbal & derivational suffixes indicating the preceding 2-syllable span is functioning as a verb/adjective stem
        private static readonly HashSet<string> VerbalContextFollowers = CreateNormalizedSet(
            "ချက်", "မှု", "ခြင်း", "စွာ", "ရေး", "ရာ", "ပေး", "ထား", "နေ", "နိုင်", "ရ", "ခဲ့",
            "ပြီ", "ပြီး", "မည်", "မယ်", "သည်", "တယ်", "ပါ", "သော", "သည့်", "မည့်",
            "လျက်", "ကြ", "ဖွယ်", "ထိုက်", "စေ", "ဖို့", "ရန်", "ဘူး"
        );

        // Common post-nominal 1-syllable modifiers (prevents Noun + Modifier like 'ကားသစ်' from merging into 'ကျားသစ်')
        private static readonly HashSet<string> PostNominalAdjectives = CreateNormalizedSet(
            "သစ်", "ဟောင်း", "ကောင်း", "ဆိုး", "ကြီး", "ငယ်", "လေး", "ဖြူ", "နီ", "နက်", "ပြာ", "ဝါ", "စိမ်း", "များ", "နည်း"
        );

        // Numerals, demonstratives, and pure/possessive pronouns that regularly precede arbitrary nouns/classifiers without forming compounds
        // (Excludes 'သင်' [teach/learn] and 'မင်း' [king/prince] which are productive compound roots like 'သင်ကြား', 'မင်းသား')
        private static readonly HashSet<string> NumeralsAndDemonstratives = CreateNormalizedSet(
            "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး", "ဆယ်", "ရာ", "ထောင်", "သောင်း", "သိန်း", "သန်း",
            "ဒီ", "ဟို", "အဲဒီ", "ထို", "ထို့", "ဤ", "ယင်း", "၎င်း", "သူ", "ငါ", "သူ့", "ငါ့", "ကိုယ့်", "မိမိ", "ဘာ", "ဘယ်"
        );

        // Pure numerals that bind tightly to a following numeral classifier (e.g. 'လေးယောက်' + 'လာသည်' -> 'ယောက်' must not merge with 'လာ')
        private static readonly HashSet<string> PureNumerals = CreateNormalizedSet(
            "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး", "ဆယ်", "ရာ", "ထောင်", "သောင်း", "သိန်း", "သန်း", "နှ"
        );

        private static readonly HashSet<string> NumeralClassifiers = CreateNormalizedSet(
            "ယောက်", "ဦး", "ပါး", "ခု", "ကောင်", "စီး", "လုံး", "ချပ်", "စောင်", "ထည်", "ခါ", "ကြိမ်", "လုတ်",
            "ရက်", "ရပ်", "ပတ်", "လ", "နှစ်", "နာရီ", "မိနစ်", "မိုင်", "ပွဲ", "ဖွဲ့", "မျိုး", "ဆင့်", "ပိုင်း", "အုပ်", "ပင်", "ခန်း", "စုံ", "တွဲ", "ဆောင်",
            "ကျပ်", "တန်", "တင်း", "ပိဿာ", "ဧက", "ပေ"
        );

        private static readonly string NormalizedKhant = UnicodeNormalizer.NormalizeCanonicalOrder("ခန့်");

        // Clause-closing & subordinate/nominalized negative markers indicating 'မ' is the negative verb prefix (e.g. 'မလာနိုင်ဘူး', 'မရေးရတာ', 'မခံရစေရေး')
        private static readonly HashSet<string> NegativeClauseClosers = CreateNormalizedSet(
            "ဘူး", "ဘဲ", "နဲ့", "နှင့်", "သေး", "ပါ", "တာ", "တဲ့", "သည့်", "လို့", "လျှင်", "ရင်", "သော်",
            "မီ", "ခင်", "စေ", "ရေး", "ခြင်း", "မှု", "ကြောင်း", "ပေ", "ချေ", "နိုင်", "ရာ", "ဖူး"
        );

        // Foreign loanword & Pali final killed consonants (e.g. ကားလ်, ဆိုးလ်, ဆဲလ်, ဘီးရ်, ပူးရ်, ကာတာရ်, ဂိတ်စ်, ဖေ့စ်, ဘေ့စ်, လိုက်ဘ်နစ်ဇ်, မတ်ခ်, သံဃာဒိသိသ်, ပရိဝါသ်)
        // Excludes bare low-tone '-ိုလ်' (ဂြိုလ်, သီဟိုလ်, မြင်းမိုလ်) and '-ာဒ် / -ာဘ်' (လာဒ်, ဆုလာဒ်, ရလာဒ်) so native silent-coda & Pali-coda typos are caught!
        private static readonly System.Text.RegularExpressions.Regex ForeignOrPaliKilledCodaRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?:(?<!\u102D\u102F)\u101C\u103A|(?<!\u101E\u1031\u102C)\u101D\u103A|(?:[\u102B\u102C\u102E\u1030\u1031\u1032][\u1037\u1038]*)\u101B\u103A|(?<![\u102B\u102C])[\u1012\u1018]\u103A|[\u101E\u1017\u1016\u1002\u1001\u1007\u100B\u100C\u100D]\u103A|\u1001\u103B\u103A|\u101B\u103E\u103A|(?:[\u102B\u102C\u1031\u102E\u1030\u1032][\u1037\u1038]*|[\u1000\u1010\u1014\u1019\u1018\u1015]\u103A[\u1037\u1038]*)\u1005\u103A)[\u1037\u1038]*$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static bool IsForeignOrPaliKilledCoda(string normSyl)
        {
            int asatIdx = normSyl.LastIndexOf('\u103A');
            if (asatIdx <= 0)
                return false;
            char codaCons = normSyl[asatIdx - 1];
            switch (codaCons)
            {
                case '\u103B':
                case '\u103E':
                case '\u101C':
                case '\u101D':
                case '\u101B':
                case '\u1012':
                case '\u1018':
                case '\u101E':
                case '\u1017':
                case '\u1016':
                case '\u1002':
                case '\u1001':
                case '\u1007':
                case '\u100B':
                case '\u100C':
                case '\u100D':
                case '\u1005':
                    return ForeignOrPaliKilledCodaRegex.IsMatch(normSyl);
                default:
                    return false;
            }
        }

        // Classical Pali Gatha / Nissaya open-syllable signature (contains Pali pat-sint \u1039, Great-Sa \u103F, retroflex ဋ..ဏ/ဠ, or Pali case/optative/absolutive endings, with ZERO Burmese killed codas/tones)
        private static readonly System.Text.RegularExpressions.Regex ClassicalPaliMarkerRegex =
            new System.Text.RegularExpressions.Regex(@"[\u1039\u103F\u100B-\u100F\u1020]|\u1012\u103D|\u1010\u103D\u102C|\u1010\u103C\u102C|\u1019\u103E\u102D|\u101E\u102F\u1031|\u101A\u103B", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Verbal/clausal particles confirming a preceding 1-syllable token is functioning as an independent predicate (not a bound section header)
        private static readonly HashSet<string> BoundVerbFrameFollowers = CreateNormalizedSet(
            "နေ", "တယ်", "သည်", "မယ်", "မည်", "ပြီ", "ပြီး", "သွား", "လာ", "ပါ", "ပါတယ်", "ပါသည်",
            "ဘူး", "လား", "သလား", "မလား", "တာ", "တဲ့", "ကဲ့", "ကဲ", "သေး", "တော့", "ဖူး",
            "ချင်", "နိုင်", "ပေး", "ထား", "ခဲ့", "ကြ", "ရ", "မိ", "လိုက်", "ဦး", "အုံး", "စို့", "ရန်", "ဖို့"
        );

        // 100% Ghost Pali Pat-Sint Alphabetical Index Tabs in Part 1 of 462-Page သတ်ပုံကျမ်း (never appear as unstacked words in Burmese)
        private static readonly HashSet<string> PureGhostPatSintTabs = CreateNormalizedSet(
            "သောပ်", "ဖဂ်", "သဉ်", "မဉ်", "သိက်", "သိဒ်", "ပိသ်", "ကုမ်", "ဆဒ်", "သဒ်", "ဝသ်"
        );

        // Forensically verified 462-Page သတ်ပုံကျမ်း bound-only section headers & ghost pat-sint tabs mapped to their dominant standalone homophones
        private static readonly Dictionary<string, (string Target, string Explanation)> BoundSyllableVerbFrameMap =
            new Dictionary<string, (string Target, string Explanation)>(StringComparer.Ordinal)
            {
                [UnicodeNormalizer.NormalizeCanonicalOrder("ဖြား")] = ("ဖျား", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဖြား' ကို 'ဖြားယောင်း-' ၌သာ သုံးပြီး၊ နာမကျန်းဖြစ်ခြင်းကို 'ဖျား' (ယပင့်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ဟုပ်")] = ("ဟုတ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဟုပ်' ကို 'ဟုပ်ခနဲ / ဟုပ်ဟုပ်' ၌သာ သုံးပြီး၊ မှန်ကန်ကြောင်းပြကြိယာကို 'ဟုတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ပြောက်")] = ("ပျောက်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပြောက်' ကို 'ပြောက်ကျား' ၌သာ သုံးပြီး၊ ကွယ်ပျောက်ခြင်းကို 'ပျောက်' (ယပင့်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ဖပ်")] = ("ဖတ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဖပ်' ကို 'ဖပ်ဖပ်စို-' ၌သာ သုံးပြီး၊ စာဖတ်ခြင်းကို 'ဖတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ဖဂ်")] = ("ဖတ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာဖတ်ခြင်းကို 'ဖတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("သောပ်")] = ("သောက်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ သောက်သုံးခြင်းကို 'သောက်' (ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ထွတ်")] = ("ထွက်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ထွတ်' ကို 'အထွတ်အထိပ် / ထွတ်မြတ်-' ၌သာ သုံးပြီး၊ ထွက်ခွာခြင်းကို 'ထွက်' (ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ပျ")] = ("ပြ", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပျ' ကို 'ရိုင်းပျ / ပျပျ' ၌သာ သုံးပြီး၊ ညွှန်ပြ/ပြောပြခြင်းကို 'ပြ' (ရရစ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ကျာ")] = ("ကြာ", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ကျာ' ကို 'ကျာပွတ် / ကျာကျာ' ၌သာ သုံးပြီး၊ အချိန်ကြာမြင့်ခြင်းကို 'ကြာ' (ရရစ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ပျောင်း")] = ("ပြောင်း", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပျောင်း' ကို 'ပျော့ပျောင်း / ပျောင်းနွဲ့-' ၌သာ သုံးပြီး၊ ပြောင်းလဲ/ပြောင်းရွှေ့ခြင်းကို 'ပြောင်း' (ရရစ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ရှုတ်")] = ("ရှုပ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ရှုတ်' ကို 'ရှုတ်ချ-' ၌သာ သုံးပြီး၊ ရှုပ်ထွေးခြင်းကို 'ရှုပ်' (ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ချုတ်")] = ("ချုပ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ချုပ်နှောင်/ချုပ်လုပ်ခြင်းကို 'ချုပ်' (ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ခြုတ်")] = ("ချုပ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ချုပ်နှောင်/ချုပ်လုပ်ခြင်းကို 'ချုပ်' (ချ နှင့် ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("လိန်း")] = ("လိမ်း", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'လိန်း' ကို 'လိန်းထေး / လိန်းဖာ-' ၌သာ သုံးပြီး၊ လိမ်းကျံခြင်းကို 'လိမ်း' (မသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ရှုန်း")] = ("ရှုံး", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကျရှုံးခြင်းကို 'ရှုံး' (သေးသေးတင်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ခြိုး")] = ("ချိုး", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ခြိုး' ကို 'ခြိုးခြံ-' ၌သာ သုံးပြီး၊ ရေချိုး/ချိုးဖဲ့ခြင်းကို 'ချိုး' (ယပင့်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ခြောင်း")] = ("ချောင်း", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ခြောင်း' ကို 'ဒေါသခြောင်းခြောင်း' ၌သာ သုံးပြီး၊ ချောင်းဆိုး/ချောင်းကြည့်ခြင်းကို 'ချောင်း' (ယပင့်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("သိက်")] = ("သိပ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'သိပ်' (ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("သိဒ်")] = ("သိပ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'သိပ်' (ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("သဉ်")] = ("သည်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'သည်' (ညကြီးသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("မဉ်")] = ("မည်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'မည်' (ညကြီးသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ပိသ်")] = ("ပိတ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပိတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ကုမ်")] = ("ကုန်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ကုန်' (နသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ဆဒ်")] = ("ဆက်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဆက်' (ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("သဒ်")] = ("သတ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'သတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"),
                [UnicodeNormalizer.NormalizeCanonicalOrder("ဝသ်")] = ("ဝတ်", "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဝတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။")
            };

        [ThreadStatic]
        private static StringBuilder? t_spanBuilder;

        private readonly struct LatticeEdge
        {
            public readonly int Length;
            public readonly double Cost;
            public readonly ulong WordHash;
            public readonly MyanmarUposTag PosTag;
            public readonly string? IssueOriginalText;
            public readonly int UnrecognizedSingleSylTokenIndex;
            public readonly Func<ProofingIssue>? IssueFactory;

            public bool HasIssue => UnrecognizedSingleSylTokenIndex >= 0 || IssueFactory != null;

            public static LatticeEdge Clean(int length, double cost, ulong wordHash = 0UL, MyanmarUposTag posTag = MyanmarUposTag.Unk) =>
                new LatticeEdge(length, cost, wordHash, posTag, null, -1, null);

            public static LatticeEdge UnrecognizedSingleSyllable(int tokenIndex, string rawSyl, double cost) =>
                new LatticeEdge(1, cost, 0UL, MyanmarUposTag.Unk, rawSyl, tokenIndex, null);

            public static LatticeEdge DeferredIssue(
                int length,
                double cost,
                string originalText,
                Func<ProofingIssue> issueFactory,
                ulong wordHash = 0UL,
                MyanmarUposTag posTag = MyanmarUposTag.Unk) =>
                new LatticeEdge(length, cost, wordHash, posTag, originalText, -1, issueFactory);

            private LatticeEdge(
                int length,
                double cost,
                ulong wordHash,
                MyanmarUposTag posTag,
                string? issueOriginalText,
                int unrecognizedSingleSylTokenIndex,
                Func<ProofingIssue>? issueFactory)
            {
                Length = length;
                Cost = cost;
                WordHash = wordHash;
                PosTag = posTag;
                IssueOriginalText = issueOriginalText;
                UnrecognizedSingleSylTokenIndex = unrecognizedSingleSylTokenIndex;
                IssueFactory = issueFactory;
            }
        }

        public MyanmarProofingEngine(MyanmarTrie trie, ProofingStyleMode styleMode = ProofingStyleMode.FormalOfficial)
        {
            _trie = trie ?? throw new ArgumentNullException(nameof(trie));
            StyleMode = styleMode;
            _suggestionEngine = new SuggestionEngine(_trie);
            _grammarEngine = new OfficialStyleGrammarEngine();
            _contextDisambiguator = new ContextDisambiguator();
        }

        public MyanmarTrie Trie
        {
            get
            {
                _lexiconRwLock.EnterReadLock();
                try { return _trie; }
                finally { _lexiconRwLock.ExitReadLock(); }
            }
        }

        /// <summary>
        /// Thread-safely swaps in a newly compiled/reloaded dictionary Trie at runtime without restarting Microsoft Word,
        /// preserving all custom user words and invalidating paragraph &amp; suggestion caches.
        /// </summary>
        public void HotSwapTrie(MyanmarTrie newTrie)
        {
            if (newTrie == null)
                throw new ArgumentNullException(nameof(newTrie));

            List<string> customSnapshot;
            lock (_customUserWords)
            {
                customSnapshot = _customUserWords.ToList();
            }

            for (int i = 0; i < customSnapshot.Count; i++)
            {
                newTrie.InsertEntry(new DictionaryEntry
                {
                    Word = customSnapshot[i],
                    MeaningCue = "အသုံးပြုသူ / ရုံးသုံး မှတ်သားထားသော စံဝေါဟာရ",
                    PosTag = "CUSTOM",
                    Frequency = 50,
                    IsOfficialThatpon = false
                });
            }

            _lexiconRwLock.EnterWriteLock();
            try
            {
                _trie = newTrie;
                _suggestionEngine = new SuggestionEngine(newTrie);
                MyanmarLinguisticReasoningEngine.ClearCache();
                Interlocked.Increment(ref _engineStateVersion);
                _paragraphCache.Clear();
                _fuzzyWindowCache.Clear();
            }
            finally
            {
                _lexiconRwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Controls whether proofing enforces General Standard Myanmar (allowing polite colloquialisms)
        /// or Formal Literary / Official Myanmar (converting colloquialisms to literary equivalents).
        /// </summary>
        public ProofingStyleMode StyleMode
        {
            get => (ProofingStyleMode)Volatile.Read(ref _styleModeValue);
            set => Volatile.Write(ref _styleModeValue, (int)value);
        }

        public bool IsDictionarySearchReady => _trie.IsSearchIndexReady;
        public long MutationVersion => _trie.MutationVersion;

        public void PrewarmDictionaryIndexes()
        {
            _lexiconRwLock.EnterReadLock();
            try { _trie.PrewarmSearchIndexes(); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<DictionaryEntry> SearchDictionary(string query, int maxResults = 30)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.SearchDictionary(query, maxResults); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<DictionaryEntry> SearchReferenceCorpus(
            string query,
            bool includeProverbs = true,
            bool includeEncyclopedia = true,
            int maxResults = 35)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.SearchReferenceCorpus(query, includeProverbs, includeEncyclopedia, maxResults); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<string> GetSynonyms(string word, int maxSynonyms = 8)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetSynonyms(word, maxSynonyms); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<string> GetAntonyms(string word, int maxAntonyms = 8)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetAntonyms(word, maxAntonyms); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<string> GetTopCollocations(string word, int maxCount = 6)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetTopCollocations(word, maxCount); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public string ReconstructBrokenPdfText(string text, out int repairedLineCount)
        {
            _lexiconRwLock.EnterReadLock();
            try { return BrokenPdfCMapReconstructor.Reconstruct(text, _trie, out repairedLineCount); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public string ReconstructBrokenPdfConfirmedSpan(string spanText, out bool changed)
        {
            _lexiconRwLock.EnterReadLock();
            try { return BrokenPdfCMapReconstructor.ReconstructConfirmedSpan(spanText, _trie, out changed); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public string RepairClipboardOrPastedText(string rawText, out int changedCount)
        {
            _lexiconRwLock.EnterReadLock();
            try { return BrokenPdfCMapReconstructor.RepairClipboardOrPastedText(rawText, _trie, out changedCount); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<DictionaryEntry> LookupBookmarkedEntries(IEnumerable<string> words)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.LookupBookmarkedEntries(words); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<DictionaryEntry> GetFeaturedKnowledgeEntries(int count = 8)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetFeaturedKnowledgeEntries(count); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public List<DictionaryEntry> GetFeaturedProverbEntries(int count = 10)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetFeaturedProverbEntries(count); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public string GetHomophoneSiblingsSummary(string word, int maxSiblings = 3)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetHomophoneSiblingsSummary(word, maxSiblings); }
            finally { _lexiconRwLock.ExitReadLock(); }
        }

        public void IgnoreWordInSession(string word)
        {
            if (!string.IsNullOrWhiteSpace(word))
            {
                bool added;
                lock (_sessionIgnoredWords)
                {
                    added = _sessionIgnoredWords.Add(UnicodeNormalizer.NormalizeCanonicalOrder(word.Trim()));
                }
                if (added)
                {
                    Interlocked.Increment(ref _engineStateVersion);
                    _paragraphCache.Clear();
                }
            }
        }

        public bool IsIgnoredInSession(string word)
        {
            lock (_sessionIgnoredWords)
            {
                return _sessionIgnoredWords.Contains(UnicodeNormalizer.NormalizeCanonicalOrder(word));
            }
        }

        /// <summary>
        /// Adds a validated Pyidaungsu Unicode term to the active proofing lexicon and optionally persists it
        /// to the offline user/ministry Custom Dictionary file across Word sessions.
        /// </summary>
        public bool AddCustomWord(string word, string? persistFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(word) || UnicodeNormalizer.IsZawgyiText(word))
                return false;

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
            if (string.IsNullOrWhiteSpace(norm) || norm.Length > 60)
                return false;

            lock (_customUserWords)
            {
                _customUserWords.Add(norm);
            }

            IgnoreWordInSession(norm);
            _lexiconRwLock.EnterWriteLock();
            try
            {
                _trie.InsertEntry(new DictionaryEntry
                {
                    Word = norm,
                    MeaningCue = "အသုံးပြုသူ / ရုံးသုံး မှတ်သားထားသော စံဝေါဟာရ",
                    PosTag = "CUSTOM",
                    Frequency = 50,
                    IsOfficialThatpon = false
                });
                _suggestionEngine.ClearCache();
                MyanmarLinguisticReasoningEngine.ClearCache();
                Interlocked.Increment(ref _engineStateVersion);
                _paragraphCache.Clear();
                _fuzzyWindowCache.Clear();
            }
            finally
            {
                _lexiconRwLock.ExitWriteLock();
            }

            if (!string.IsNullOrWhiteSpace(persistFilePath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(persistFilePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    var existing = new HashSet<string>(StringComparer.Ordinal);
                    if (File.Exists(persistFilePath))
                    {
                        foreach (string ln in File.ReadAllLines(persistFilePath, Encoding.UTF8))
                        {
                            string c = UnicodeNormalizer.NormalizeCanonicalOrder(ln.Trim());
                            if (!string.IsNullOrEmpty(c))
                                existing.Add(c);
                        }
                    }
                    if (existing.Add(norm))
                    {
                        File.AppendAllText(persistFilePath, norm + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch
                {
                    // Ignore file I/O errors in locked environments
                }
            }

            return true;
        }

        /// <summary>
        /// Returns all currently loaded custom dictionary words in sorted order.
        /// </summary>
        public IReadOnlyList<string> GetCustomWords()
        {
            lock (_customUserWords)
            {
                var list = _customUserWords.ToList();
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        /// <summary>
        /// Removes a custom word from memory, session ignore list, active trie, and persistent file.
        /// </summary>
        public bool RemoveCustomWord(string word, string? persistFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
            if (string.IsNullOrWhiteSpace(norm))
                return false;

            bool removed;
            lock (_customUserWords)
            {
                removed = _customUserWords.Remove(norm);
            }

            lock (_sessionIgnoredWords)
            {
                _sessionIgnoredWords.Remove(norm);
            }

            _lexiconRwLock.EnterWriteLock();
            try
            {
                if (_trie.RemoveCustomEntry(norm))
                    removed = true;
                _suggestionEngine.ClearCache();
                MyanmarLinguisticReasoningEngine.ClearCache();
                Interlocked.Increment(ref _engineStateVersion);
                _paragraphCache.Clear();
                _fuzzyWindowCache.Clear();
            }
            finally
            {
                _lexiconRwLock.ExitWriteLock();
            }

            if (!string.IsNullOrWhiteSpace(persistFilePath) && File.Exists(persistFilePath))
            {
                try
                {
                    var keptLines = new List<string>();
                    foreach (string rawLine in File.ReadAllLines(persistFilePath, Encoding.UTF8))
                    {
                        string trimmed = rawLine.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                        {
                            keptLines.Add(rawLine);
                            continue;
                        }

                        string lineNorm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(trimmed));
                        if (!string.Equals(lineNorm, norm, StringComparison.Ordinal))
                        {
                            keptLines.Add(lineNorm);
                        }
                        else
                        {
                            removed = true;
                        }
                    }

                    File.WriteAllLines(persistFilePath, keptLines, Encoding.UTF8);
                }
                catch
                {
                    // Ignore file I/O errors in locked environments
                }
            }

            return removed;
        }

        /// <summary>
        /// Loads persistent user or IT/GPO ministry custom dictionary words into the offline engine.
        /// </summary>
        public int LoadCustomDictionaryFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return 0;

            int loaded = 0;
            try
            {
                foreach (string rawLine in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    string trimmed = rawLine.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    if (AddCustomWord(trimmed, persistFilePath: null))
                        loaded++;
                }
            }
            catch
            {
                // Ignore
            }
            return loaded;
        }

        private static readonly List<ProofingIssue> EmptyCachedIssues = new List<ProofingIssue>(0);

        public void TrimMemoryCaches() => TrimMemoryCaches(aggressive: false);

        public void TrimMemoryCaches(bool aggressive)
        {
            _paragraphCache.Clear();
            _fuzzyWindowCache.Clear();
            _suggestionEngine.ClearCache();
            MyanmarLinguisticReasoningEngine.ClearCache();

            if (aggressive)
            {
                _lexiconRwLock.EnterWriteLock();
                try
                {
                    _trie.TrimReferenceAndSearchCaches();
                }
                finally
                {
                    _lexiconRwLock.ExitWriteLock();
                }
            }
        }

        private static bool HasAnyMyanmarCodePoint(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u1000' && c <= '\u109F') ||
                    (c >= '\uAA60' && c <= '\uAA7F') ||
                    (c >= '\uA9E0' && c <= '\uA9FF'))
                {
                    return true;
                }
            }
            return false;
        }

        public List<ProofingIssue> CheckText(string text) => CheckText(text, cacheParagraph: true);

        public List<ProofingIssue> CheckText(string text, bool cacheParagraph)
        {
            if (string.IsNullOrWhiteSpace(text) || !HasAnyMyanmarCodePoint(text))
                return new List<ProofingIssue>(0);

            _lexiconRwLock.EnterReadLock();
            try
            {
                return CheckTextCore(text, cacheParagraph);
            }
            finally
            {
                _lexiconRwLock.ExitReadLock();
            }
        }

        private List<ProofingIssue> CheckTextCore(string text, bool cacheParagraph)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<ProofingIssue>(0);

            bool canCacheParagraph = cacheParagraph && text.Length <= 2500;
            string? cacheKey = canCacheParagraph
                ? $"{(int)StyleMode}:{Volatile.Read(ref _engineStateVersion)}:{_trie.MutationVersion}:{text}"
                : null;

            if (cacheKey != null && _paragraphCache.TryGetValue(cacheKey, out var cachedIssues))
            {
                return CloneIssues(cachedIssues);
            }

            List<ProofingIssue> computed = CheckTextUncached(text);

            if (cacheKey != null)
            {
                int maxCache = EcoPerformanceGovernor.ComputeBudgets(isLargeFile: false).MaxParagraphCacheEntries;
                if (_paragraphCache.Count >= maxCache)
                {
                    int targetRemove = Math.Max(64, maxCache / 4);
                    int removed = 0;
                    foreach (var existingKey in _paragraphCache.Keys)
                    {
                        if (_paragraphCache.TryRemove(existingKey, out _) && ++removed >= targetRemove)
                            break;
                    }
                }
                _paragraphCache[cacheKey] = computed.Count == 0 ? EmptyCachedIssues : CloneIssues(computed);
            }

            return computed;
        }

        private static List<ProofingIssue> CloneIssues(List<ProofingIssue> source)
        {
            if (source.Count == 0)
                return new List<ProofingIssue>(0);

            var copy = new List<ProofingIssue>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                var src = source[i];
                var clonedSuggestions = new List<SuggestionCandidate>(src.Suggestions.Count);
                for (int j = 0; j < src.Suggestions.Count; j++)
                {
                    var s = src.Suggestions[j];
                    clonedSuggestions.Add(new SuggestionCandidate
                    {
                        Replacement = s.Replacement,
                        MeaningCue = s.MeaningCue,
                        Score = s.Score,
                        PosTag = s.PosTag,
                        IsOfficialThatpon = s.IsOfficialThatpon,
                        SyllableBreakdown = s.SyllableBreakdown,
                        DiagnosticDetail = s.DiagnosticDetail,
                        HomophoneSiblings = s.HomophoneSiblings,
                        AbidanSummary = s.AbidanSummary
                    });
                }

                copy.Add(new ProofingIssue
                {
                    StartIndex = src.StartIndex,
                    Length = src.Length,
                    OriginalText = src.OriginalText,
                    IssueType = src.IssueType,
                    Message = src.Message,
                    GrammaticalReasoning = src.GrammaticalReasoning,
                    AuthoritativeSource = src.AuthoritativeSource,
                    RequiresReviewBeforeBatchFix = src.RequiresReviewBeforeBatchFix,
                    Suggestions = clonedSuggestions
                });
            }

            return copy;
        }

        private static bool IsInvisibleFormatChar(char c)
        {
            return c == '\u200B' || c == '\u200C' || c == '\u200D' ||
                   c == '\uFEFF' || c == '\u2060' || c == '\u00AD';
        }

        private static bool IsNonBreakingSpaceChar(char c)
        {
            return c == '\u00A0' || c == '\u202F' || c == '\u2007';
        }

        private List<ProofingIssue> CheckTextUncached(string text)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text))
                return issues;

            // 0. Broken Pyidaungsu / OpenType PDF Font CMap Mojibake Guard + Trie/Bigram Reconstructor
            //    (e.g. legacy-exported PDFs with 'ကကီေး', 'ပပည်ရ ာင်စု', 'မ္ှာ', 'သိုို့', 'ဥပရေ')
            //    Emits a single paragraph-level diagnostic with a 1-click reconstructed Unicode suggestion (unchecked under Sure Only in Review & Fix).
            if (UnicodeNormalizer.IsBrokenPdfFontCMapText(text))
            {
                int pos = 0;
                while (pos < text.Length)
                {
                    while (pos < text.Length && (text[pos] <= '\u001F' || char.IsWhiteSpace(text[pos])))
                        pos++;
                    if (pos >= text.Length)
                        break;

                    int segStart = pos;
                    while (pos < text.Length && text[pos] > '\u001F')
                        pos++;

                    int segEnd = pos;
                    while (segEnd > segStart && char.IsWhiteSpace(text[segEnd - 1]))
                        segEnd--;

                    if (segEnd <= segStart)
                        continue;

                    string spanText = text.Substring(segStart, segEnd - segStart);
                    if (!HasAnyMyanmarCodePoint(spanText))
                        continue;

                    string rebuilt = BrokenPdfCMapReconstructor.ReconstructConfirmedSpan(spanText, _trie, out bool changed);
                    if (!changed && !UnicodeNormalizer.IsBrokenPdfFontCMapText(spanText))
                        continue;

                    var pdfSuggestions = new List<SuggestionCandidate>(1);
                    if (changed && !string.IsNullOrWhiteSpace(rebuilt))
                    {
                        pdfSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = rebuilt,
                            MeaningCue = "ပျက်စီးနေသော PDF စာသားကို ပြည်ထောင်စုယူနီကုဒ် (Pyidaungsu Unicode) သို့ ပြန်လည်တည်ဆောက်မည်",
                            Score = 98.0
                        });
                    }

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = segStart,
                        Length = spanText.Length,
                        OriginalText = spanText,
                        IssueType = ProofingIssueType.ZawgyiEncodingError,
                        RequiresReviewBeforeBatchFix = true,
                        Message = "PDF ဖိုင်မှ ကူးယူရာတွင် စာလုံးပုံစံဇယား (PDF Font ToUnicode CMap) ပျက်စီးနေပါသည် — ပြန်လည်တည်ဆောက်ထားသော ယူနီကုဒ်စာသားကို စစ်ဆေးအတည်ပြု၍ အစားထိုးနိုင်ပါသည်။",
                        GrammaticalReasoning = "မူရင်း PDF ဖိုင်ထုတ်လုပ်စဉ်က စာလုံးပုံစံဇယား (Font ToUnicode CMap) ပျက်စီးနေသောကြောင့် ဝစ္စပေါက် ('း' -> 'ေး')၊ မဗျည်း ('မ' -> 'မ္')၊ ပျောက်ဆုံးအက္ခရာများ ('ပြည်ထောင်စု' -> 'ပပည်ရ ာင်စု'၊ 'ကြီး' -> 'ကကီေး'၊ 'ဥပဒေ' -> 'ဥပရေ') အက္ခရာကျိုးပဲ့နေပါသည်။ အဘိဓာန် (၉၆,၀၀၀+) နှင့် စကားလုံးတွဲ (၁၁၀,၀၀၀) တို့ဖြင့် ပြန်လည်တည်ဆောက်ထားပြီး လူပုဂ္ဂိုလ်အမည်များ မလွဲမှားစေရန် စိစစ်အတည်ပြုချက်ဖြင့် ပြင်ဆင်နိုင်ပါသည်။",
                        AuthoritativeSource = "ပြည်ထောင်စုဖောင့် နှင့် ယူနီကုဒ် စံချိန်စံနှုန်း (PDF ToUnicode CMap Reconstructor)",
                        Suggestions = pdfSuggestions
                    });
                }

                if (issues.Count > 0)
                    return issues;
            }

            // 1. Chunk-Safe Zawgyi-One & Hybrid Residual Glyph Guard (never corrupts surrounding Unicode words!)
            string workingText = text;
            var zawgyiSpans = UnicodeNormalizer.FindZawgyiSpans(text);
            if (zawgyiSpans.Count > 0)
            {
                if (zawgyiSpans.Count == 1 && zawgyiSpans[0].StartIndex == 0 && zawgyiSpans[0].Length == text.Length)
                {
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = 0,
                        Length = text.Length,
                        OriginalText = text,
                        IssueType = ProofingIssueType.ZawgyiEncodingError,
                        Message = "ဇော်ဂျီ (Zawgyi-One) စာလုံးစနစ် တွေ့ရှိရပါသည်။ ရုံးသုံး ပြည်ထောင်စုယူနီကုဒ် (Pyidaungsu Unicode) သို့ ပြောင်းလဲရန် လိုအပ်ပါသည်။",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = zawgyiSpans[0].ConvertedUnicode,
                                MeaningCue = "Pyidaungsu Unicode သို့ ပြောင်းလဲမည်",
                                Score = 100.0
                            }
                        }
                    });
                    MyanmarLinguisticReasoningEngine.EnrichIssuesWithDeepReasoning(text, Array.Empty<SyllableToken>(), issues, _trie, StyleMode);
                    return issues;
                }

                char[] masked = text.ToCharArray();
                for (int z = 0; z < zawgyiSpans.Count; z++)
                {
                    var zs = zawgyiSpans[z];
                    if (!IsIgnoredInSession(zs.OriginalText))
                    {
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = zs.StartIndex,
                            Length = zs.Length,
                            OriginalText = zs.OriginalText,
                            IssueType = ProofingIssueType.ZawgyiEncodingError,
                            Message = "ဇော်ဂျီ (Zawgyi-One) အက္ခရာ ရောနှောနေပါသည် — ရုံးသုံး ပြည်ထောင်စုယူနီကုဒ် (Pyidaungsu Unicode) သို့ ပြောင်းလဲရန် လိုအပ်ပါသည်။",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = zs.ConvertedUnicode,
                                    MeaningCue = "Pyidaungsu Unicode သို့ ပြောင်းလဲမည်",
                                    Score = 100.0
                                }
                            }
                        });
                    }
                    for (int k = 0; k < zs.Length; k++)
                    {
                        masked[zs.StartIndex + k] = ' ';
                    }
                }
                workingText = new string(masked);
            }

            // 1b. Visual-Order Pre-Base Clerk Sequences ('‌ေဒါ်' -> 'ဒေါ်', '‌ြေမာက်' -> 'မြောက်', 'ြမန်မာ' -> 'မြန်မာ', 'ဦး‌ေ၀' -> 'ဦးဝေ')
            //     + Index-Mapped Invisible Format Projection for Pasted Web/Mobile/PDF Paragraphs
            var visualOrderSpans = UnicodeNormalizer.FindVisualOrderPreBaseSyllables(workingText);
            string nlpText = workingText;
            int[]? projectedRawStart = null;
            int[]? projectedRawEnd = null;
            int invisibleFormatCount = 0;
            bool hasNbsp = false;
            for (int cIdx = 0; cIdx < workingText.Length; cIdx++)
            {
                char ch = workingText[cIdx];
                if (IsInvisibleFormatChar(ch))
                    invisibleFormatCount++;
                else if (IsNonBreakingSpaceChar(ch))
                    hasNbsp = true;
            }

            if (visualOrderSpans.Count > 0 || invisibleFormatCount > 0)
            {
                int approxCap = workingText.Length + 8;
                var projChars = new List<char>(approxCap);
                var rawStarts = new List<int>(approxCap);
                var rawEnds = new List<int>(approxCap);

                int voIdx = 0;
                int r = 0;
                while (r < workingText.Length)
                {
                    if (voIdx < visualOrderSpans.Count && r == visualOrderSpans[voIdx].StartIndex)
                    {
                        var vo = visualOrderSpans[voIdx];
                        string logical = vo.LogicalUnicode;
                        int logLen = logical.Length;
                        for (int k = 0; k < logLen; k++)
                        {
                            projChars.Add(logical[k]);
                            rawStarts.Add(k == 0 ? vo.StartIndex : vo.StartIndex + Math.Min(k, vo.Length - 1));
                            rawEnds.Add(k == logLen - 1 ? vo.StartIndex + vo.Length : vo.StartIndex + Math.Min(k + 1, vo.Length));
                        }
                        r = vo.StartIndex + vo.Length;
                        voIdx++;
                        continue;
                    }

                    char ch = workingText[r];
                    if (!IsInvisibleFormatChar(ch))
                    {
                        projChars.Add(IsNonBreakingSpaceChar(ch) ? ' ' : ch);
                        rawStarts.Add(r);
                        rawEnds.Add(r + 1);
                    }
                    r++;
                }

                nlpText = new string(projChars.ToArray());
                projectedRawStart = rawStarts.ToArray();
                projectedRawEnd = rawEnds.ToArray();
            }
            else if (hasNbsp)
            {
                var normSpaces = workingText.ToCharArray();
                for (int r = 0; r < normSpaces.Length; r++)
                {
                    if (IsNonBreakingSpaceChar(normSpaces[r]))
                        normSpaces[r] = ' ';
                }
                nlpText = new string(normSpaces);
            }

            var nlpIssues = new List<ProofingIssue>();

            ProofingScratchWorkspace ws = t_workspace ??= new ProofingScratchWorkspace();
            bool rentedWorkspace = !ws.InUse;
            if (!rentedWorkspace)
                ws = new ProofingScratchWorkspace();
            ws.InUse = true;

            try
            {
                // Pre-tokenize once & build Supreme Official-Thatpon Compound Shield + Exact Multi-Word Lexicon Shield + Personal-Name Shield!
                List<SyllableToken> tokens = MyanmarSyllableBreaker.Tokenize(nlpText);
                PrecomputeTokenTrieSpans(nlpText, tokens, ws);
                var officialHeadwordSpans = ws.OfficialSpans;
                var exactLexiconMultiWordSpans = ws.ExactMultiWordSpans;
                bool[] personNameProtected = tokens.Count > 0
                    ? ComputePersonNameProtectionMask(nlpText, tokens, ws)
                    : Array.Empty<bool>();

                bool isClassicalNissayaContext = IsClassicalPitakaNissayaParagraph(nlpText);

                // 2. Style & Grammar Check (တ/တစ်, ခုနစ်/ခုနှစ်, မ...ဘဲ, လဲ/လည်း, classifiers, tense, broken compounds, casual typos, plus အပြောစကား->အရေးစကား when FormalOfficial)
                List<ProofingIssue> grammarIssues = _grammarEngine.AnalyzeGrammarAndStyle(nlpText, StyleMode, _trie);
                if (grammarIssues.Count > 1)
                {
                    grammarIssues.Sort((a, b) =>
                    {
                        int cmp = a.StartIndex.CompareTo(b.StartIndex);
                        return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
                    });
                }
                for (int gIdx = 0; gIdx < grammarIssues.Count; gIdx++)
                {
                    var gi = grammarIssues[gIdx];
                    if (IsIgnoredInSession(gi.OriginalText) ||
                        (isClassicalNissayaContext && IsClassicalPitakaOrthographyVariant(gi.OriginalText)))
                    {
                        continue;
                    }

                    if (ViolatesOfficialHeadwordSpan(gi, officialHeadwordSpans))
                    {
                        // Allow syntactic noun + 'တစ်' + wrong-classifier agreement (e.g. 'ဖိနပ် တစ်ခု -> ရံ', 'ကား တစ်ခု -> စီး')
                        // when the only overlapping official headword span is the 2-syllable numeral phrase 'တစ် + [Classifier]' itself
                        // (never an extended lexical compound like 'တစ်ခုလပ်' or 'တစ်ယောက်တည်း').
                        bool allowTaitClassifierAgreement =
                            gi.IssueType == ProofingIssueType.NumeralClassifierError &&
                            gi.StartIndex >= 3 &&
                            string.CompareOrdinal(nlpText, gi.StartIndex - 3, "တစ်", 0, 3) == 0 &&
                            !string.IsNullOrEmpty(gi.AuthoritativeSource) &&
                            gi.AuthoritativeSource.IndexOf("မျိုးပြပစ္စည်း", StringComparison.Ordinal) >= 0;

                        if (allowTaitClassifierAgreement)
                        {
                            int taitStart = gi.StartIndex - 3;
                            int clfEnd = gi.StartIndex + gi.Length;
                            for (int oIdx = 0; oIdx < officialHeadwordSpans.Count; oIdx++)
                            {
                                var sp = officialHeadwordSpans[oIdx];
                                if (gi.StartIndex < sp.End && clfEnd > sp.Start &&
                                    (sp.Start < taitStart || sp.End > clfEnd))
                                {
                                    allowTaitClassifierAgreement = false;
                                    break;
                                }
                            }
                        }

                        if (!allowTaitClassifierAgreement)
                            continue;
                    }

                    // In FormalOfficial (Government) mode, never let colloquial-to-literary particle rules ('တယ်->သည်', 'မယ်->မည်', 'နဲ့->နှင့်', 'တွေ->များ')
                    // slice inside an exact multi-syllable dictionary compound (e.g. 'ဒစ်ဂျစ်တယ်', 'ဟိုတယ်', 'ပေါ်တယ်', 'ဇင်းမယ်', 'ခနော်ခနဲ့') or a person's name ('ဒေါ်ခင်မေမယ်')!
                    bool isFormalColloquialSuffixRule =
                        StyleMode == ProofingStyleMode.FormalOfficial &&
                        gi.IssueType == ProofingIssueType.OfficialStyleGrammarError &&
                        (gi.OriginalText == "တယ်" || gi.OriginalText == "မယ်" || gi.OriginalText == "နဲ့" || gi.OriginalText == "တွေ" ||
                         gi.OriginalText == "သလား" || gi.OriginalText == "မလား" || gi.OriginalText == "သလဲ" || gi.OriginalText == "မလဲ");

                    if (isFormalColloquialSuffixRule &&
                        (ViolatesOfficialHeadwordProperSubspan(gi, exactLexiconMultiWordSpans) ||
                         OverlapsProtectedPersonName(gi, tokens, personNameProtected)))
                    {
                        continue;
                    }

                    // Never let colloquial verb-suffix rule '[Verb] + အုန်း -> ဦး' corrupt personal names ending in '-အုန်း' (e.g. 'ဒေါ်တင်တင်အုန်း', 'ဦးခင်မောင်အုန်း')
                    if (gi.OriginalText.EndsWith("အုန်း", StringComparison.Ordinal) &&
                        OverlapsProtectedPersonName(gi, tokens, personNameProtected))
                    {
                        continue;
                    }

                    AddIssueIfNonOverlapping(nlpIssues, gi);
                }

                // 2b. Curated High-Precision Context-Aware Homophone Phrases (မီ/မှီ, ယာဉ်/ယဉ်, ကြေညာ/ကြော်ငြာ, ဘက်/ဖက်, စာဖတ်/စာဖက်, သဘောကျ, -မူ->-မှု, စိုး/ဆိုး, ဖူး/ဘူး)
                var ctxIssues = _contextDisambiguator.AnalyzeContextHomophones(nlpText);
                for (int cIdx = 0; cIdx < ctxIssues.Count; cIdx++)
                {
                    var ctxIssue = ctxIssues[cIdx];
                    if (!IsIgnoredInSession(ctxIssue.OriginalText) &&
                        !ViolatesOfficialHeadwordProperSubspan(ctxIssue, officialHeadwordSpans) &&
                        !OverlapsProtectedPersonName(ctxIssue, tokens, personNameProtected))
                    {
                        AddIssueIfNonOverlapping(nlpIssues, ctxIssue);
                    }
                }

                // 3. Syllable Tokenization, Personal-Name Protection & Viterbi Word-Lattice Segmentation
                if (tokens.Count > 0)
                {
                    int idx = 0;
                    while (idx < tokens.Count)
                    {
                        SyllableToken token = tokens[idx];
                        if (token.Kind != TokenKind.MyanmarSyllable || IsOverlappingAny(nlpIssues, token.StartIndex, token.Length))
                        {
                            idx++;
                            continue;
                        }

                        // Handle personal-name protected syllables individually (check lookalikes & structural validity, skip fuzzy repair)
                        if (personNameProtected[idx])
                        {
                            CheckProtectedSingleSyllable(token, nlpIssues);
                            idx++;
                            continue;
                        }

                        // Collect maximal contiguous unprotected, unclaimed Myanmar syllable clause run [runStart .. runEnd)
                        int runStart = idx;
                        int runEnd = idx + 1;
                        while (runEnd < tokens.Count &&
                               tokens[runEnd].Kind == TokenKind.MyanmarSyllable &&
                               !personNameProtected[runEnd] &&
                               !IsOverlappingAny(nlpIssues, tokens[runEnd].StartIndex, tokens[runEnd].Length))
                        {
                            runEnd++;
                        }

                        SegmentClauseWithViterbi(nlpText, tokens, runStart, runEnd, nlpIssues, ws);
                        idx = runEnd;
                    }

                    // 4. Post-Viterbi Data-Driven ContextualRules & Bigram Real-Word Homophone Disambiguation
                    //    (Runs strictly on clean words after Viterbi compound repair so broken syllables inside typos are never hijacked)
                    var postCtxIssues = _contextDisambiguator.AnalyzePostViterbiContextAndBigrams(nlpText, _trie, nlpIssues, tokens, ws.LongestMatchLengths, personNameProtected);
                    for (int pIdx = 0; pIdx < postCtxIssues.Count; pIdx++)
                    {
                        var postCtxIssue = postCtxIssues[pIdx];
                        bool isMergedParticleVerb =
                            !string.IsNullOrEmpty(postCtxIssue.AuthoritativeSource) &&
                            postCtxIssue.AuthoritativeSource.IndexOf("ပုဒ်အဖြတ်အတောက်", StringComparison.Ordinal) >= 0;
                        if (!IsIgnoredInSession(postCtxIssue.OriginalText) &&
                            (isMergedParticleVerb ||
                             (!OverlapsProtectedPersonName(postCtxIssue, tokens, personNameProtected) &&
                              !ViolatesOfficialHeadwordProperSubspan(postCtxIssue, officialHeadwordSpans))))
                        {
                            AddIssueIfNonOverlapping(nlpIssues, postCtxIssue);
                        }
                    }
                    if (isClassicalNissayaContext && nlpIssues.Count > 0)
                    {
                        for (int nIdx = nlpIssues.Count - 1; nIdx >= 0; nIdx--)
                        {
                            if (IsClassicalPitakaOrthographyVariant(nlpIssues[nIdx].OriginalText))
                            {
                                nlpIssues.RemoveAt(nIdx);
                            }
                        }
                    }
                }

                // 4.0b. Quoted-Dialogue Spoken Contraction Tolerance (GeneralStandard) & ColloquialPermissive Mode Filtering
                if (nlpIssues.Count > 0 && StyleMode != ProofingStyleMode.FormalOfficial)
                {
                    if (StyleMode == ProofingStyleMode.ColloquialPermissive)
                    {
                        for (int pIdx = nlpIssues.Count - 1; pIdx >= 0; pIdx--)
                        {
                            if (MyanmarCompactLanguageModel.IsPermissiveColloquialVariant(nlpIssues[pIdx], isQuotedDialogueOnly: false))
                            {
                                nlpIssues.RemoveAt(pIdx);
                            }
                        }
                    }
                    else
                    {
                        bool[]? quoteMask = MyanmarCompactLanguageModel.BuildQuotedDialogueMask(nlpText);
                        if (quoteMask != null)
                        {
                            for (int qIdx = nlpIssues.Count - 1; qIdx >= 0; qIdx--)
                            {
                                var qIss = nlpIssues[qIdx];
                                if (qIss.StartIndex >= 0 && qIss.StartIndex < quoteMask.Length &&
                                    quoteMask[qIss.StartIndex] &&
                                    MyanmarCompactLanguageModel.IsPermissiveColloquialVariant(qIss, isQuotedDialogueOnly: true))
                                {
                                    nlpIssues.RemoveAt(qIdx);
                                }
                            }
                        }
                    }
                }

                // 4a. Whole-Compound Sub-Span Coalescing & Context-Steered Top-1 Suggestion Reranking
                if (nlpIssues.Count > 0 && tokens.Count > 0)
                {
                    CoalesceSubspansAndRerankTop1WithContext(nlpText, tokens, personNameProtected, nlpIssues);
                }

                // 4b. Guarantee multiple selectable replacement choices (casual-standard, polite, formal, and compositional repairs)
                for (int k = 0; k < nlpIssues.Count; k++)
                {
                    _suggestionEngine.EnrichIssueWithSelectableChoices(nlpIssues[k], StyleMode, maxChoices: 4);
                }

                // 5. Universal Deep Burmese Linguistic, Pali Etymological, Phonological & Syntactic Reasoning Enrichment
                MyanmarLinguisticReasoningEngine.EnrichIssuesWithDeepReasoning(nlpText, tokens, nlpIssues, _trie, StyleMode);
            }
            finally
            {
                if (rentedWorkspace)
                    ws.InUse = false;
            }

            // 5b. Remap projected NLP issues back to exact raw character offsets in `text`
            if (projectedRawStart != null && projectedRawEnd != null)
            {
                for (int k = 0; k < nlpIssues.Count; k++)
                {
                    ProofingIssue pi = nlpIssues[k];
                    if (pi.Length <= 0 || pi.StartIndex < 0 || pi.StartIndex + pi.Length > projectedRawStart.Length)
                        continue;

                    int rawStart = projectedRawStart[pi.StartIndex];
                    int rawEnd = projectedRawEnd[pi.StartIndex + pi.Length - 1];
                    if (rawEnd <= rawStart || rawStart < 0 || rawEnd > text.Length)
                        continue;

                    pi.StartIndex = rawStart;
                    pi.Length = rawEnd - rawStart;
                    pi.OriginalText = text.Substring(rawStart, rawEnd - rawStart);
                    AddIssueIfNonOverlapping(issues, pi);
                }

                // Emit standalone visual-order pre-base clerk repairs ('‌ေဒါ်' -> 'ဒေါ်', '‌ြေမာက်' -> 'မြောက်', 'ြမန်' -> 'မြန်', '‌ေ၀' -> 'ဝေ')
                // whenever not already subsumed by a wider multi-syllable spelling repair
                for (int v = 0; v < visualOrderSpans.Count; v++)
                {
                    var vo = visualOrderSpans[v];
                    if (IsIgnoredInSession(vo.OriginalText) || IsOverlappingAny(issues, vo.StartIndex, vo.Length))
                        continue;

                    var voIssue = new ProofingIssue
                    {
                        StartIndex = vo.StartIndex,
                        Length = vo.Length,
                        OriginalText = vo.OriginalText,
                        IssueType = ProofingIssueType.LookalikeCharacterError,
                        Message = $"သဝေထိုး ('ေ') သို့မဟုတ် ရရစ် ('ြ') ကို အခြေခံဗျည်းရှေ့မှ မှားယွင်းရိုက်နှိပ်ထားပါသည် — ယူနီကုဒ်စံနှုန်းအရ '{vo.LogicalUnicode}' ဟု အခြေခံဗျည်းကို ဦးစွာရိုက်နှိပ်ရမည်။",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = vo.LogicalUnicode,
                                MeaningCue = "ပြည်ထောင်စုယူနီကုဒ် အက္ခရာအစီအစဉ်အမှန်",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(vo.LogicalUnicode),
                                DiagnosticDetail = "အခြေခံဗျည်းရှေ့ ရောက်နေသော သဝေထိုး/ရရစ် အစီအစဉ်ပြင်ဆင်ချက်",
                                Score = 100.0
                            }
                        }
                    };
                    _suggestionEngine.EnrichIssueWithSelectableChoices(voIssue, StyleMode, maxChoices: 4);
                    var singleVoList = new List<ProofingIssue>(1) { voIssue };
                    MyanmarLinguisticReasoningEngine.EnrichIssuesWithDeepReasoning(text, Array.Empty<SyllableToken>(), singleVoList, _trie, StyleMode);
                    AddIssueIfNonOverlapping(issues, voIssue);
                }

                // Host-smart Word ZWSP check:
                // - Always flag broken-syllable dotted-circle ZWSP even in heavily ZWSP-laced paragraphs (> 2 ZWSPs).
                // - When <= 2 ZWSPs appear in a paragraph, flag mid-word & orphan-particle ZWSPs (protecting Ctrl+F)
                //   while allowing clean phrase/word-boundary ZWSPs (e.g. မှတ်ပုံတင်ထားခြင်း[ZWSP]အတွက်).
                if (invisibleFormatCount > 0)
                {
                    bool brokenSylOnly = invisibleFormatCount > 2;
                    foreach (var zwIssue in OfficialStyleGrammarEngine.FindZeroWidthWordCorruptions(workingText, _trie, brokenSylOnly))
                    {
                        if (!IsIgnoredInSession(zwIssue.OriginalText) && !IsOverlappingAny(issues, zwIssue.StartIndex, zwIssue.Length))
                        {
                            _suggestionEngine.EnrichIssueWithSelectableChoices(zwIssue, StyleMode, maxChoices: 4);
                            var singleList = new List<ProofingIssue>(1) { zwIssue };
                            MyanmarLinguisticReasoningEngine.EnrichIssuesWithDeepReasoning(workingText, Array.Empty<SyllableToken>(), singleList, _trie, StyleMode);
                            AddIssueIfNonOverlapping(issues, zwIssue);
                        }
                    }
                }
            }
            else
            {
                for (int k = 0; k < nlpIssues.Count; k++)
                {
                    ProofingIssue pi = nlpIssues[k];
                    if (hasNbsp && pi.StartIndex >= 0 && pi.StartIndex + pi.Length <= text.Length)
                    {
                        pi.OriginalText = text.Substring(pi.StartIndex, pi.Length);
                    }
                    AddIssueIfNonOverlapping(issues, pi);
                }
            }

            if (issues.Count > 1)
            {
                issues.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
            }
            return issues;
        }

        private static bool IsClassicalPitakaNissayaParagraph(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 12)
                return false;

            return text.IndexOf("ရဟန်းတို့", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ဘုရားလောင်းသည်", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ဘုရားလောင်းဆိုတိုင်း", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ဝိသုဒ္ဓိမဂ်", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ပါဠိ၌", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ဟုဆိုကုန်၏", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("ဟု ဆိုကုန်၏", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("သလောဟု မေး၏", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("။ပ။", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("-ကြည့်။", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("-ကြည့်။", StringComparison.Ordinal) >= 0;
        }

        private static bool IsClassicalPitakaOrthographyVariant(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Trim());
            return norm == "တပါး" ||
                   norm == "သူတပါး" ||
                   norm == "တစုံတခု" ||
                   norm == "တခု" ||
                   norm == "တစုံ" ||
                   norm == "တယောက်" ||
                   norm == "ငှါ" ||
                   norm == "အလို့ငှါ" ||
                   norm == "အကျိုးငှါ" ||
                   norm == "ဘာရဒွာဇ" ||
                   norm == "ရှေးရှူ";
        }

        private void PrecomputeTokenTrieSpans(
            string fullText,
            List<SyllableToken> tokens,
            ProofingScratchWorkspace ws)
        {
            int count = tokens.Count;
            ws.AllTokenSpans.Clear();
            ws.OfficialSpans.Clear();
            ws.ExactMultiWordSpans.Clear();

            if (ws.TokenSpanOffsets.Length < count + 1)
                ws.TokenSpanOffsets = new int[Math.Max((count + 1) * 2, 128)];
            if (ws.LongestMatchLengths.Length < count)
                ws.LongestMatchLengths = new int[Math.Max(count * 2, 128)];
            if (ws.InsideKnownWord.Length < count)
                ws.InsideKnownWord = new bool[Math.Max(count * 2, 128)];
            else if (count > 0)
                Array.Clear(ws.InsideKnownWord, 0, count);

            if (count == 0)
            {
                ws.TokenSpanOffsets[0] = 0;
                return;
            }

            var buf = ws.MatchBuf;
            for (int i = 0; i < count; i++)
            {
                ws.TokenSpanOffsets[i] = ws.AllTokenSpans.Count;
                if (tokens[i].Kind != TokenKind.MyanmarSyllable)
                {
                    ws.LongestMatchLengths[i] = 0;
                    continue;
                }

                _trie.MatchSpansAt(tokens, i, count - i, buf);
                TrySynthesizeProductiveRedupChinSpans(fullText, tokens, i, count, buf);
                ws.LongestMatchLengths[i] = buf.Count > 0 ? buf[buf.Count - 1].Length : 0;

                for (int s = 0; s < buf.Count; s++)
                {
                    var sp = buf[s];
                    if (sp.Length > 12)
                        break;

                    ws.AllTokenSpans.Add(sp);
                    var (len, entries, wrongRule) = sp;
                    if (len >= 2 && wrongRule == null && entries != null && entries.Count > 0)
                    {
                        for (int k = 1; k < len; k++)
                        {
                            ws.InsideKnownWord[i + k] = true;
                        }

                        // Confirm contiguous syllables (no whitespace inside compound) and zero lookalike error
                        bool contiguous = true;
                        for (int k = 1; k < len; k++)
                        {
                            if (tokens[i + k].StartIndex != tokens[i + k - 1].StartIndex + tokens[i + k - 1].Length)
                            {
                                contiguous = false;
                                break;
                            }
                        }
                        if (!contiguous)
                            continue;

                        int charStart = tokens[i].StartIndex;
                        SyllableToken lastTok = tokens[i + len - 1];
                        int charLen = (lastTok.StartIndex + lastTok.Length) - charStart;
                        if (!UnicodeNormalizer.HasLookalikeErrorSlice(fullText, charStart, charLen) &&
                            !HasAnyInvalidRawSyllableInRange(tokens, i, len, _trie))
                        {
                            var range = (charStart, charStart + charLen);
                            ws.ExactMultiWordSpans.Add(range);
                            bool isProtectedOfficial = false;
                            for (int eIdx = 0; eIdx < entries.Count; eIdx++)
                            {
                                var e = entries[eIdx];
                                if (e.IsOfficialThatpon || e.IsProperNoun || string.Equals(e.PosTag, "CUSTOM", StringComparison.OrdinalIgnoreCase))
                                {
                                    isProtectedOfficial = true;
                                    break;
                                }
                            }
                            if (isProtectedOfficial)
                            {
                                ws.OfficialSpans.Add(range);
                            }
                        }
                    }
                }
            }
            ws.TokenSpanOffsets[count] = ws.AllTokenSpans.Count;
        }

        private static readonly List<DictionaryEntry> ProductiveRedupChinEntries = new List<DictionaryEntry>(1)
        {
            new DictionaryEntry
            {
                Word = "ချင်း",
                PosTag = "ADV",
                Frequency = 80,
                IsOfficialThatpon = true,
                MeaningCue = "နှစ်ထပ်ကြိယာ/နာမ် အချိန်ပြ·အချင်းချင်းပြ ပစ္စည်း ('~ချင်း')"
            }
        };

        private void TrySynthesizeProductiveRedupChinSpans(
            string fullText,
            List<SyllableToken> tokens,
            int startIdx,
            int count,
            List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> buf)
        {
            // 1. Productive 3-syllable '[W][W]ချင်း' (e.g. 'သိသိချင်း', 'တွေ့တွေ့ချင်း', 'မြင်မြင်ချင်း', 'ရရချင်း', 'လူလူချင်း')
            if (startIdx + 2 < count &&
                tokens[startIdx].NormalizedText == tokens[startIdx + 1].NormalizedText &&
                tokens[startIdx + 2].NormalizedText == "ချင်း")
            {
                SyllableToken t0 = tokens[startIdx];
                SyllableToken t1 = tokens[startIdx + 1];
                SyllableToken t2 = tokens[startIdx + 2];
                if (t1.Kind == TokenKind.MyanmarSyllable &&
                    t2.Kind == TokenKind.MyanmarSyllable &&
                    t1.StartIndex == t0.StartIndex + t0.Length &&
                    t2.StartIndex == t1.StartIndex + t1.Length &&
                    !HasCleanSpanOfLength(buf, 3) &&
                    IsProductiveRedupChinBase(t0.NormalizedText) &&
                    !UnicodeNormalizer.HasLookalikeErrorSlice(fullText, t0.StartIndex, (t2.StartIndex + t2.Length) - t0.StartIndex))
                {
                    InsertSortedSpan(buf, 3, ProductiveRedupChinEntries);
                }
            }

            // 2. Productive 5-syllable '[W1 W2][W1 W2]ချင်း' (e.g. 'ရောက်ရှိရောက်ရှိချင်း', 'သိရှိသိရှိချင်း', 'မိတ်ဆွေမိတ်ဆွေချင်း')
            if (startIdx + 4 < count &&
                tokens[startIdx + 4].NormalizedText == "ချင်း" &&
                tokens[startIdx].NormalizedText == tokens[startIdx + 2].NormalizedText &&
                tokens[startIdx + 1].NormalizedText == tokens[startIdx + 3].NormalizedText)
            {
                bool contiguous = true;
                for (int k = 1; k <= 4; k++)
                {
                    SyllableToken prev = tokens[startIdx + k - 1];
                    SyllableToken cur = tokens[startIdx + k];
                    if (cur.Kind != TokenKind.MyanmarSyllable || cur.StartIndex != prev.StartIndex + prev.Length)
                    {
                        contiguous = false;
                        break;
                    }
                }

                if (contiguous && !HasCleanSpanOfLength(buf, 5))
                {
                    SyllableToken t0 = tokens[startIdx];
                    SyllableToken t4 = tokens[startIdx + 4];
                    if (IsProductiveRedupChinBase(t0.NormalizedText + tokens[startIdx + 1].NormalizedText) &&
                        !UnicodeNormalizer.HasLookalikeErrorSlice(fullText, t0.StartIndex, (t4.StartIndex + t4.Length) - t0.StartIndex))
                    {
                        InsertSortedSpan(buf, 5, ProductiveRedupChinEntries);
                    }
                }
            }
        }

        private static bool HasCleanSpanOfLength(
            List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> buf,
            int targetLength)
        {
            for (int i = 0; i < buf.Count; i++)
            {
                if (buf[i].Length == targetLength && buf[i].WrongRule == null && buf[i].Entries != null && buf[i].Entries!.Count > 0)
                    return true;
            }
            return false;
        }

        private static void InsertSortedSpan(
            List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)> buf,
            int length,
            List<DictionaryEntry> entries)
        {
            for (int i = 0; i < buf.Count; i++)
            {
                if (buf[i].Length > length)
                {
                    buf.Insert(i, (length, entries, null));
                    return;
                }
            }
            buf.Add((length, entries, null));
        }

        private bool IsProductiveRedupChinBase(string normBase)
        {
            if (string.IsNullOrEmpty(normBase) ||
                normBase == "ချင်း" || normBase == "ခြင်း" || normBase == "အ" || normBase == "မ" ||
                GrammaticalParticles.Contains(normBase) || ColloquialParticles.Contains(normBase))
            {
                return false;
            }

            if (!_trie.TryGetExactEntries(normBase, out var entries) || entries == null || entries.Count == 0)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.IsOfficialThatpon && e.Frequency < 8)
                    continue;

                string pos = (e.PosTag ?? string.Empty).Trim().ToUpperInvariant();
                if (pos == "PART" || pos == "PRON" || pos == "NUM" || pos == "CONJ" || pos == "INTJ")
                    continue;

                return true;
            }
            return false;
        }

        private static bool OverlapsProtectedPersonName(ProofingIssue issue, List<SyllableToken> tokens, bool[] personNameProtected)
        {
            if (personNameProtected.Length == 0)
                return false;

            int iStart = issue.StartIndex;
            int iEnd = issue.StartIndex + issue.Length;
            for (int k = 0; k < tokens.Count; k++)
            {
                if (!personNameProtected[k])
                    continue;
                int tStart = tokens[k].StartIndex;
                int tEnd = tStart + tokens[k].Length;
                if (iStart < tEnd && iEnd > tStart)
                    return true;
            }
            return false;
        }

        private static bool ViolatesOfficialHeadwordSpan(ProofingIssue issue, List<(int Start, int End)> officialSpans)
        {
            // Contextual 'ခုနစ်' vs 'ခုနှစ်' rules and monastic subject-verb concord rules intentionally disambiguate whole words/verb stems in context
            if (issue.OriginalText == "ခုနစ်" || issue.OriginalText == "ခုနှစ်" ||
                (!string.IsNullOrEmpty(issue.AuthoritativeSource) &&
                 (issue.AuthoritativeSource.IndexOf("သာသနာရေး", StringComparison.Ordinal) >= 0 ||
                  issue.AuthoritativeSource.IndexOf("အဆက်တွဲဝိဘတ်", StringComparison.Ordinal) >= 0)))
                return false;

            return ViolatesOfficialHeadwordProperSubspan(issue, officialSpans);
        }

        private static bool ViolatesOfficialHeadwordProperSubspan(ProofingIssue issue, List<(int Start, int End)> officialSpans)
        {
            int iStart = issue.StartIndex;
            int iEnd = issue.StartIndex + issue.Length;
            int spanCount = officialSpans.Count;
            for (int k = 0; k < spanCount; k++)
            {
                var (oStart, oEnd) = officialSpans[k];
                // Block any regex that slices a STRICT PROPER SUB-SPAN out of a longer Official Thatpon compound
                // (e.g. 'တရက်' inside 'ရုတ်တရက်', 'တမြို့နယ်' inside 'ဟင်္သာတမြို့နယ်', 'ပီး' inside 'ရူပီး', 'အကျွေး' inside 'အကျွေးအမွေး'),
                // while allowing intentional whole-word contextual disambiguation (where oStart == iStart && iEnd == oEnd, e.g. 'အစာ ကြွေးထား')!
                if (oStart <= iStart && iEnd <= oEnd && (oStart < iStart || iEnd < oEnd))
                    return true;

                // Block regex/contextual rules that slice across the boundary of TWO adjacent Official Thatpon compounds
                // (e.g. 'ခြေလမ်း -> ခြေလှမ်း' straddling 'သည်းခြေ' + 'လမ်းကြောင်း')!
                if (oStart < iStart && iStart < oEnd && oEnd < iEnd)
                {
                    for (int m = 0; m < spanCount; m++)
                    {
                        var (o2Start, o2End) = officialSpans[m];
                        if (o2Start <= oEnd && o2Start < iEnd && iEnd <= o2End)
                            return true;
                    }
                }
            }
            return false;
        }

        private static readonly HashSet<string> ForeignNameTerminatingCaseParticles = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က",
            "မှာ", "တို့", "သည့်", "သော", "မည့်", "၍", "ကြောင့်", "ဖြင့်", "သဖြင့်",
            "ဟု", "လည်း", "နဲ့", "ရဲ့", "လို့"
        );

        private bool StartsEstablishedVocabularyAfterForeignName(List<SyllableToken> tokens, int nextTokIdx)
        {
            int tokCount = tokens.Count;
            if (nextTokIdx >= tokCount || tokens[nextTokIdx].Kind != TokenKind.MyanmarSyllable)
                return false;

            string firstNorm = tokens[nextTokIdx].NormalizedText;

            int chunkEnd = nextTokIdx + 1;
            while (chunkEnd < tokCount &&
                   tokens[chunkEnd].Kind == TokenKind.MyanmarSyllable &&
                   tokens[chunkEnd].StartIndex == tokens[chunkEnd - 1].StartIndex + tokens[chunkEnd - 1].Length)
            {
                chunkEnd++;
            }

            // Isolated 1-syllable token after space: only standalone particles terminate the foreign name
            if (chunkEnd == nextTokIdx + 1)
            {
                return GrammaticalParticles.Contains(firstNorm) || ColloquialParticles.Contains(firstNorm);
            }

            int stemEnd = chunkEnd;
            while (stemEnd > nextTokIdx + 1)
            {
                string tailNorm = tokens[stemEnd - 1].NormalizedText;
                if (GrammaticalParticles.Contains(tailNorm) ||
                    ColloquialParticles.Contains(tailNorm) ||
                    VerbalContextFollowers.Contains(tailNorm))
                {
                    stemEnd--;
                }
                else
                {
                    break;
                }
            }

            int chunkLen = chunkEnd - nextTokIdx;
            if (chunkLen >= 2 && chunkLen <= 6 && HasEstablishedNonProperEntry(ConcatNormalizedTokens(tokens, nextTokIdx, chunkLen)))
                return true;

            int stemLen = stemEnd - nextTokIdx;
            if (stemLen >= 2 && stemLen <= 6 && HasEstablishedNonProperEntry(ConcatNormalizedTokens(tokens, nextTokIdx, stemLen)))
                return true;

            // Long unspaced Burmese clause after a foreign name (e.g. 'ဦးဆောင်သည့်ကိုယ်စားလှယ်အဖွဲ့သည်'):
            // check if a 2..4-syllable Official/established word opens the chunk and is immediately followed by a particle/verbal follower or another established multi-syllable word.
            if (StartsWithEstablishedCompoundAndFollower(tokens, nextTokIdx, chunkEnd))
                return true;

            // Also handle a single case particle glued onto the front of an established multi-syllable Burmese verb/noun (e.g. 'ကပြောကြားသည်', 'ကဦးဆောင်သည့်ကိုယ်စားလှယ်အဖွဲ့')
            if (ForeignNameTerminatingCaseParticles.Contains(firstNorm))
            {
                int afterPartLen = stemEnd - (nextTokIdx + 1);
                if (afterPartLen >= 2 && afterPartLen <= 6 &&
                    HasEstablishedNonProperEntry(ConcatNormalizedTokens(tokens, nextTokIdx + 1, afterPartLen)))
                {
                    return true;
                }

                if (StartsWithEstablishedCompoundAndFollower(tokens, nextTokIdx + 1, chunkEnd))
                    return true;
            }

            return false;
        }

        private bool HasEstablishedNonProperEntry(string normSpan)
        {
            if (_trie.TryGetExactEntries(normSpan, out var entries) && entries != null)
            {
                for (int eIdx = 0; eIdx < entries.Count; eIdx++)
                {
                    var e = entries[eIdx];
                    if (!e.IsProperNoun && (e.IsOfficialThatpon || e.Frequency >= 15))
                        return true;
                }
            }
            return false;
        }

        private bool StartsWithEstablishedCompoundAndFollower(List<SyllableToken> tokens, int startIdx, int chunkEnd)
        {
            int rem = chunkEnd - startIdx;
            if (rem < 3)
                return false;

            int maxPref = Math.Min(4, rem - 1);
            for (int pLen = maxPref; pLen >= 2; pLen--)
            {
                if (!HasEstablishedNonProperEntry(ConcatNormalizedTokens(tokens, startIdx, pLen)))
                    continue;

                int nextPos = startIdx + pLen;
                string nextSyl = tokens[nextPos].NormalizedText;
                if (GrammaticalParticles.Contains(nextSyl) ||
                    ColloquialParticles.Contains(nextSyl) ||
                    VerbalContextFollowers.Contains(nextSyl))
                {
                    return true;
                }

                int maxNextWord = Math.Min(4, chunkEnd - nextPos);
                for (int nLen = maxNextWord; nLen >= 2; nLen--)
                {
                    if (HasEstablishedNonProperEntry(ConcatNormalizedTokens(tokens, nextPos, nLen)))
                        return true;
                }
            }
            return false;
        }

        private bool[] ComputePersonNameProtectionMask(string fullText, List<SyllableToken> tokens, ProofingScratchWorkspace ws)
        {
            int tokCount = tokens.Count;
            if (ws.PersonNameMask.Length < tokCount)
                ws.PersonNameMask = new bool[Math.Max(tokCount * 2, 128)];
            else
                Array.Clear(ws.PersonNameMask, 0, tokCount);

            bool[] mask = ws.PersonNameMask;
            bool[] insideKnownWord = ws.InsideKnownWord;

            int grace = 0;
            bool awaitingNameStartAfterHonorific = false;
            bool inForeignName = false;
            int foreignNameSpacesLeft = 0;
            int i = 0;

            while (i < tokCount)
            {
                SyllableToken tok = tokens[i];
                if (tok.Kind != TokenKind.MyanmarSyllable)
                {
                    // Burmese personal-name alias marker '(ခ)' ("alias / aka", e.g. 'ဦးတင်မိုးအောင် (ခ) နေရှိုင်း၏', 'သားဖြစ်သူ ကင်မ်အဲရစ် (ခ) ကိုထိန်လင်း')
                    if (tok.Text == "(" && i + 2 < tokCount &&
                        tokens[i + 1].NormalizedText == "ခ" &&
                        tokens[i + 2].Text == ")")
                    {
                        int preEnd = i;
                        while (preEnd > 0 && tokens[preEnd - 1].Kind == TokenKind.Whitespace)
                            preEnd--;

                        int preStart = preEnd;
                        while (preStart > 0 && preEnd - preStart < 6)
                        {
                            int prevIdx = preStart - 1;
                            if (tokens[prevIdx].Kind != TokenKind.MyanmarSyllable)
                                break;
                            if (preStart < preEnd && tokens[prevIdx].StartIndex + tokens[prevIdx].Length != tokens[preStart].StartIndex)
                                break;
                            string ps = tokens[prevIdx].NormalizedText;
                            if (GrammaticalParticles.Contains(ps) || ColloquialParticles.Contains(ps) ||
                                UnicodeNormalizer.HasLookalikeError(tokens[prevIdx].Text, out _, out _))
                            {
                                break;
                            }
                            preStart = prevIdx;
                        }

                        if (preStart < preEnd)
                        {
                            bool preHasWrongRule = false;
                            for (int a = preStart; a < preEnd && !preHasWrongRule; a++)
                            {
                                for (int b = a + 1; b <= preEnd; b++)
                                {
                                    if (_trie.TryGetWrongFormRule(GetTokenSpan(fullText, tokens, a, b - a).NormalizedText, out _))
                                    {
                                        preHasWrongRule = true;
                                        break;
                                    }
                                }
                            }
                            if (!preHasWrongRule)
                            {
                                for (int k = preStart; k < preEnd; k++)
                                    mask[k] = true;
                            }
                        }

                        grace = 5;
                        awaitingNameStartAfterHonorific = true;
                        inForeignName = false;
                        foreignNameSpacesLeft = 0;
                        i += 3;
                        continue;
                    }

                    // Preserve foreign-name continuation across initials/hyphens/apostrophes (e.g. 'မစ္စတာ အဗ္ဗဒူလာဇစ်ယူစွတ်အေ.ဂျေ.အယ်လ် - အလီတို့ကို')
                    if (inForeignName && grace > 0 &&
                        (tok.Text == "." || tok.Text == "-" || tok.Text == "–" || tok.Text == "—" || tok.Text == "'" || tok.Text == "\u2019"))
                    {
                        i++;
                        continue;
                    }

                    if (tok.Kind == TokenKind.Whitespace && grace > 0)
                    {
                        if (awaitingNameStartAfterHonorific)
                        {
                            i++;
                            continue;
                        }
                        if (foreignNameSpacesLeft > 0)
                        {
                            int peekIdx = i + 1;
                            while (peekIdx < tokCount && tokens[peekIdx].Kind == TokenKind.Whitespace)
                                peekIdx++;

                            if (!StartsEstablishedVocabularyAfterForeignName(tokens, peekIdx))
                            {
                                foreignNameSpacesLeft--;
                                i++;
                                continue;
                            }
                        }
                    }
                    grace = 0;
                    awaitingNameStartAfterHonorific = false;
                    inForeignName = false;
                    foreignNameSpacesLeft = 0;
                    i++;
                    continue;
                }

                bool shouldTerminateOnParticle;
                if (awaitingNameStartAfterHonorific)
                {
                    // Never terminate before the very first syllable of a personal name (e.g. 'မစ္စတာ သော် ဆရလမ်', 'ဦးသာအောင်')
                    shouldTerminateOnParticle = false;
                }
                else if (inForeignName)
                {
                    bool hasContiguousNextSyl =
                        i + 1 < tokCount &&
                        tokens[i + 1].Kind == TokenKind.MyanmarSyllable &&
                        tokens[i + 1].StartIndex == tok.StartIndex + tok.Length;
                    bool hasContiguousPrevSyl =
                        i > 0 &&
                        tokens[i - 1].Kind == TokenKind.MyanmarSyllable &&
                        tok.StartIndex == tokens[i - 1].StartIndex + tokens[i - 1].Length;

                    if (hasContiguousNextSyl)
                    {
                        shouldTerminateOnParticle = false;
                    }
                    else if (hasContiguousPrevSyl)
                    {
                        // Trailing syllable attached to the end of a multi-syllable foreign name (e.g. 'ကလီဘန့်အား' vs 'ဆာရာ'):
                        // terminate only on genuine nominal case particles ('အား', 'ကို', 'က', 'မှ', 'နှင့်', '၏', 'သည်', etc.), never on non-case homographs like 'ရာ' / 'ကာ' / 'သာ'!
                        shouldTerminateOnParticle = ForeignNameTerminatingCaseParticles.Contains(tok.NormalizedText);
                    }
                    else
                    {
                        shouldTerminateOnParticle = GrammaticalParticles.Contains(tok.NormalizedText) || ColloquialParticles.Contains(tok.NormalizedText);
                    }
                }
                else
                {
                    shouldTerminateOnParticle = GrammaticalParticles.Contains(tok.NormalizedText) || ColloquialParticles.Contains(tok.NormalizedText);
                }

                if (shouldTerminateOnParticle)
                {
                    grace = 0;
                    awaitingNameStartAfterHonorific = false;
                    inForeignName = false;
                    foreignNameSpacesLeft = 0;
                }

                // Never start an honorific in the middle of an existing compound word (e.g. 'ကနဦး', 'ပထမဦးဆုံး', 'ဦးဆောင်ဦးရွက်'), unless preceded by a state/academic title (e.g. 'သူရဦး...', 'ဒေါက်တာဘဒ္ဒန္တ...')!
                bool precededByTitle = (i > 0 && TitleSylBeforeHonorific.Contains(tokens[i - 1].NormalizedText)) ||
                                       (i >= 2 && tokens[i - 1].NormalizedText == "တာ" && tokens[i - 2].NormalizedText == "ဒေါက်");
                bool canStartHonorific = HonorificFirstSyllables.Contains(tok.NormalizedText) &&
                                         (!insideKnownWord[i] || precededByTitle) &&
                                         (i == 0 ||
                                          tokens[i - 1].Kind != TokenKind.MyanmarSyllable ||
                                          precededByTitle);

                if (canStartHonorific)
                {
                    int matchedHonorificLen = 0;
                    string matchedHonorificNorm = string.Empty;
                    for (int len = Math.Min(7, tokens.Count - i); len >= 1; len--)
                    {
                        bool allSyl = true;
                        for (int k = 0; k < len; k++)
                        {
                            if (tokens[i + k].Kind != TokenKind.MyanmarSyllable) { allSyl = false; break; }
                        }
                        if (!allSyl) continue;

                        var span = GetTokenSpan(fullText, tokens, i, len);
                        if (HonorificPrefixes.Contains(span.NormalizedText))
                        {
                            // Prevent object particle 'ကို' right after a noun/title (without space, e.g. 'တာဝန်ကို', 'သမ္မတကို', 'စေတီတော်ကို')
                            // OR space-followed 'ကို ' ('လူသားများ ကို တူးရိယာ') from acting as honorific 'ကို' (honorific 'ကို' attaches directly: 'ကိုအောင်ကျော်')!
                            if (span.NormalizedText == "ကို")
                            {
                                if (i + len >= tokens.Count || tokens[i + len].Kind != TokenKind.MyanmarSyllable)
                                    continue;
                                if (i > 0 && tokens[i - 1].Kind == TokenKind.MyanmarSyllable &&
                                    tokens[i - 1].NormalizedText != "နှင့်" && tokens[i - 1].NormalizedText != "နဲ့")
                                {
                                    continue;
                                }
                            }

                            // Never treat 'ဆရာ' inside a compound verb/nominalizer ('ဂုဏ်ယူဆရာပင်ဖြစ်ပြီး') as a personal-name honorific!
                            if (span.NormalizedText == "ဆရာ")
                            {
                                if (i > 0 && tokens[i - 1].Kind == TokenKind.MyanmarSyllable &&
                                    !TitleSylBeforeHonorific.Contains(tokens[i - 1].NormalizedText))
                                {
                                    continue;
                                }
                                if (i + len < tokens.Count && tokens[i + len].Kind == TokenKind.MyanmarSyllable)
                                {
                                    string afterSaya = tokens[i + len].NormalizedText;
                                    if (GrammaticalParticles.Contains(afterSaya) || ColloquialParticles.Contains(afterSaya) ||
                                        afterSaya == "ပင်" || afterSaya == "ဖြစ်" || afterSaya == "ကောင်း" || afterSaya == "လှ")
                                    {
                                        continue;
                                    }
                                }
                            }

                            // Common role nouns ('ဆရာ', 'ဆရာမ', 'ဆရာကြီး', 'ဆရာမကြီး') followed by whitespace are ordinary sentence nouns
                            // ('ဆရာ စာဖက်သည်', 'ဆရာကြီး တူးရိယာ...') unless followed by an explicit personal-name honorific ('ဆရာ ဦးအောင်ကျော်')!
                            if ((span.NormalizedText == "ဆရာ" || span.NormalizedText == "ဆရာမ" ||
                                 span.NormalizedText == "ဆရာကြီး" || span.NormalizedText == "ဆရာမကြီး") &&
                                (i + len >= tokens.Count || tokens[i + len].Kind != TokenKind.MyanmarSyllable))
                            {
                                int nextTok = i + len;
                                while (nextTok < tokens.Count && tokens[nextTok].Kind == TokenKind.Whitespace)
                                    nextTok++;

                                bool nextIsChainedHonorific = false;
                                if (nextTok < tokens.Count && tokens[nextTok].Kind == TokenKind.MyanmarSyllable)
                                {
                                    for (int hLen = Math.Min(5, tokens.Count - nextTok); hLen >= 1; hLen--)
                                    {
                                        bool hAllSyl = true;
                                        for (int hk = 0; hk < hLen; hk++)
                                        {
                                            if (tokens[nextTok + hk].Kind != TokenKind.MyanmarSyllable) { hAllSyl = false; break; }
                                        }
                                        if (!hAllSyl) continue;
                                        string nh = GetTokenSpan(fullText, tokens, nextTok, hLen).NormalizedText;
                                        if (HonorificPrefixes.Contains(nh) && nh != "ဆရာ" && nh != "ဆရာမ" && nh != "ဆရာကြီး" && nh != "ဆရာမကြီး")
                                        {
                                            nextIsChainedHonorific = true;
                                            break;
                                        }
                                    }
                                }

                                if (!nextIsChainedHonorific)
                                    continue;
                            }

                            matchedHonorificLen = len;
                            matchedHonorificNorm = span.NormalizedText;
                            break;
                        }
                    }

                    if (matchedHonorificLen > 0 &&
                        !LooksLikeVocabularyCompoundWithHonorificPrefix(fullText, tokens, i, matchedHonorificLen))
                    {
                        // Mark the honorific itself as protected only if it has no lookalike error (so 'ဦး' still gets flagged -> 'ဦး')
                        for (int k = 0; k < matchedHonorificLen; k++)
                        {
                            if (!UnicodeNormalizer.HasLookalikeError(tokens[i + k].Text, out _, out _))
                                mask[i + k] = true;
                        }
                        bool isRoyalForeignTitle =
                            matchedHonorificNorm == "ဘုရင်မင်းမြတ်" ||
                            matchedHonorificNorm == "အရှင်မင်းမြတ်" ||
                            matchedHonorificNorm == "ဆူလ်တန်" ||
                            matchedHonorificNorm == "အိမ်ရှေ့မင်းသား";
                        bool isForeignTitle =
                            isRoyalForeignTitle ||
                            matchedHonorificNorm == "မစ္စတာ" ||
                            matchedHonorificNorm == "မစ္စက်" ||
                            matchedHonorificNorm == "မစ္စစ်" ||
                            matchedHonorificNorm == "မစ္စ" ||
                            matchedHonorificNorm == "မဒမ်" ||
                            matchedHonorificNorm == "ဒါတို" ||
                            matchedHonorificNorm == "ဒါတွတ်" ||
                            matchedHonorificNorm == "ဆမ်ဒက်ချ်" ||
                            matchedHonorificNorm == "ကာဒီနယ်" ||
                            matchedHonorificNorm == "ရှရီမတီ" ||
                            matchedHonorificNorm == "ရှရီ";
                        grace = isRoyalForeignTitle ? 36 : (isForeignTitle ? 18 : 5);
                        awaitingNameStartAfterHonorific = true;
                        inForeignName = isForeignTitle;
                        foreignNameSpacesLeft = isRoyalForeignTitle ? 16 : (isForeignTitle ? 5 : 0);
                        i += matchedHonorificLen;
                        continue;
                    }
                }

                if (grace > 0)
                {
                    // Never mask an explicit multi-syllable WrongFormRule:
                    // - If it repairs to a chained honorific/rank title (e.g. 'ပါမောက္ခ ဒေါတ်တာ အောင်ကျော်' -> 'ဒေါက်တာ'), leave the typo unmasked and preserve grace for the following name.
                    // - Otherwise (it is a known vocabulary typo such as 'တူးရိယာ', 'အစည်းအဝေ', 'ကိတ်မုဒ်'), terminate name-protection immediately so Viterbi flags it!
                    int typoSpanLen = 0;
                    bool isTypoHonorificTitle = false;
                    for (int tLen = Math.Min(6, tokens.Count - i); tLen >= 2; tLen--)
                    {
                        bool allSyl = true;
                        for (int k = 0; k < tLen; k++)
                        {
                            if (tokens[i + k].Kind != TokenKind.MyanmarSyllable) { allSyl = false; break; }
                        }
                        if (!allSyl) continue;

                        var tSpan = GetTokenSpan(fullText, tokens, i, tLen);
                        if (_trie.TryGetWrongFormRule(tSpan.NormalizedText, out var wRule) && wRule != null)
                        {
                            typoSpanLen = tLen;
                            isTypoHonorificTitle = HonorificPrefixes.Contains(wRule.CorrectForm);
                            break;
                        }
                    }

                    if (typoSpanLen > 0)
                    {
                        if (isTypoHonorificTitle)
                        {
                            grace = 5;
                            awaitingNameStartAfterHonorific = true;
                            i += typoSpanLen;
                            continue;
                        }

                        grace = 0;
                        awaitingNameStartAfterHonorific = false;
                        inForeignName = false;
                        foreignNameSpacesLeft = 0;
                    }
                    else
                    {
                        mask[i] = true;
                        awaitingNameStartAfterHonorific = false;
                        grace--;
                        if (grace == 0)
                            inForeignName = false;
                    }
                }

                i++;
            }

            // Protect proper village toponyms (1..4 contiguous syllables) immediately preceding 'ကျေးရွာ' or 'စံပြကျေးရွာ'
            // (CheckProtectedSingleSyllable still verifies lookalikes & syllable structure, while skipping false fuzzy mutations)
            for (int v = 1; v + 1 < tokens.Count; v++)
            {
                if (tokens[v].Kind != TokenKind.MyanmarSyllable || tokens[v + 1].Kind != TokenKind.MyanmarSyllable)
                    continue;
                if (tokens[v].NormalizedText != "ကျေး" || tokens[v + 1].NormalizedText != "ရွာ")
                    continue;
                if (tokens[v].StartIndex + tokens[v].Length != tokens[v + 1].StartIndex)
                    continue;

                int nameEnd = v;
                if (v >= 2 &&
                    tokens[v - 2].Kind == TokenKind.MyanmarSyllable &&
                    tokens[v - 1].Kind == TokenKind.MyanmarSyllable &&
                    tokens[v - 2].NormalizedText == "စံ" &&
                    tokens[v - 1].NormalizedText == "ပြ" &&
                    tokens[v - 2].StartIndex + tokens[v - 2].Length == tokens[v - 1].StartIndex &&
                    tokens[v - 1].StartIndex + tokens[v - 1].Length == tokens[v].StartIndex)
                {
                    nameEnd = v - 2;
                }

                int nameStart = nameEnd;
                while (nameStart > 0 && nameEnd - nameStart < 4)
                {
                    int prev = nameStart - 1;
                    if (tokens[prev].Kind != TokenKind.MyanmarSyllable ||
                        tokens[prev].StartIndex + tokens[prev].Length != tokens[nameStart].StartIndex)
                    {
                        break;
                    }

                    string ps = tokens[prev].NormalizedText;
                    if (GrammaticalParticles.Contains(ps) ||
                        ColloquialParticles.Contains(ps) ||
                        NumeralsAndDemonstratives.Contains(ps) ||
                        ps == "ရှိ" || ps == "နယ်" || ps == "ခရိုင်" || ps == "တွင်း")
                    {
                        break;
                    }

                    if (ps == "ပါ" && prev >= 2 &&
                        tokens[prev - 2].Kind == TokenKind.MyanmarSyllable &&
                        tokens[prev - 1].Kind == TokenKind.MyanmarSyllable &&
                        tokens[prev - 2].NormalizedText == "အ" &&
                        tokens[prev - 1].NormalizedText == "ဆို")
                    {
                        break;
                    }

                    nameStart = prev;
                }

                if (nameStart < nameEnd)
                {
                    bool hasExplicitWrongRule = false;
                    for (int a = nameStart; a < nameEnd && !hasExplicitWrongRule; a++)
                    {
                        for (int b = a + 1; b <= nameEnd; b++)
                        {
                            var sub = GetTokenSpan(fullText, tokens, a, b - a);
                            if (_trie.TryGetWrongFormRule(sub.NormalizedText, out _))
                            {
                                hasExplicitWrongRule = true;
                                break;
                            }
                        }
                    }

                    if (!hasExplicitWrongRule)
                    {
                        for (int k = nameStart; k < nameEnd; k++)
                            mask[k] = true;
                    }
                }
            }

            // Protect Thai/Lao/Cambodian Buddhist Wat monastery names ('ဝပ်' + [2..8 contiguous transliterated syllables] + 'ကျောင်း', e.g. 'ဝပ်ရာ့တ်ပရခေါထမ်ကျောင်း')
            for (int w = 0; w + 3 < tokCount; w++)
            {
                if (tokens[w].Kind != TokenKind.MyanmarSyllable || tokens[w].NormalizedText != "ဝပ်")
                    continue;
                if (w > 0 && tokens[w - 1].Kind == TokenKind.MyanmarSyllable &&
                    tokens[w - 1].StartIndex + tokens[w - 1].Length == tokens[w].StartIndex)
                {
                    continue;
                }

                int cur = w + 1;
                int kyaungIdx = -1;
                while (cur < tokCount && cur - (w + 1) <= 8 &&
                       tokens[cur].Kind == TokenKind.MyanmarSyllable &&
                       tokens[cur - 1].StartIndex + tokens[cur - 1].Length == tokens[cur].StartIndex)
                {
                    if (tokens[cur].NormalizedText == "ကျောင်း" && cur - (w + 1) >= 2)
                    {
                        kyaungIdx = cur;
                        break;
                    }
                    cur++;
                }

                if (kyaungIdx > w + 2)
                {
                    bool watClean = true;
                    for (int a = w; a < kyaungIdx && watClean; a++)
                    {
                        if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                        {
                            watClean = false;
                            break;
                        }
                        for (int b = a + 1; b <= kyaungIdx; b++)
                        {
                            if (_trie.TryGetWrongFormRule(GetTokenSpan(fullText, tokens, a, b - a).NormalizedText, out _))
                            {
                                watClean = false;
                                break;
                            }
                        }
                    }

                    if (watClean)
                    {
                        for (int k = w; k < kyaungIdx; k++)
                            mask[k] = true;
                    }
                }
            }

            // Protect Hindu/Sanskrit temple names ('ဆရီ' + [2..9 contiguous transliterated syllables] + [optional space] + 'ဘုရားကျောင်း', e.g. 'ဆရီဆပ်သရာနာရာရမ်း ဘုရားကျောင်း')
            for (int w = 0; w + 5 < tokCount; w++)
            {
                if (tokens[w].Kind != TokenKind.MyanmarSyllable || tokens[w].NormalizedText != "ဆ" ||
                    tokens[w + 1].Kind != TokenKind.MyanmarSyllable || tokens[w + 1].NormalizedText != "ရီ" ||
                    tokens[w].StartIndex + tokens[w].Length != tokens[w + 1].StartIndex)
                {
                    continue;
                }
                if (w > 0 && tokens[w - 1].Kind == TokenKind.MyanmarSyllable &&
                    tokens[w - 1].StartIndex + tokens[w - 1].Length == tokens[w].StartIndex)
                {
                    continue;
                }

                int nameEnd = w + 2;
                while (nameEnd < tokCount && nameEnd - (w + 2) <= 9 &&
                       tokens[nameEnd].Kind == TokenKind.MyanmarSyllable &&
                       tokens[nameEnd - 1].StartIndex + tokens[nameEnd - 1].Length == tokens[nameEnd].StartIndex)
                {
                    if (nameEnd + 2 < tokCount &&
                        tokens[nameEnd].NormalizedText == "ဘု" &&
                        tokens[nameEnd + 1].NormalizedText == "ရား" &&
                        tokens[nameEnd + 2].NormalizedText == "ကျောင်း")
                    {
                        break;
                    }
                    nameEnd++;
                }

                if (nameEnd - (w + 2) >= 2)
                {
                    int templeIdx = nameEnd;
                    while (templeIdx < tokCount && tokens[templeIdx].Kind == TokenKind.Whitespace)
                        templeIdx++;

                    if (templeIdx + 2 < tokCount &&
                        tokens[templeIdx].Kind == TokenKind.MyanmarSyllable && tokens[templeIdx].NormalizedText == "ဘု" &&
                        tokens[templeIdx + 1].Kind == TokenKind.MyanmarSyllable && tokens[templeIdx + 1].NormalizedText == "ရား" &&
                        tokens[templeIdx + 2].Kind == TokenKind.MyanmarSyllable && tokens[templeIdx + 2].NormalizedText == "ကျောင်း")
                    {
                        bool sriClean = true;
                        for (int a = w; a < nameEnd; a++)
                        {
                            if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                            {
                                sriClean = false;
                                break;
                            }
                        }
                        if (sriClean)
                        {
                            for (int k = w; k < nameEnd; k++)
                                mask[k] = true;
                        }
                    }
                }
            }

            // Protect parenthesized village / branch-school toponyms directly adjacent to 'ကျောင်း' / 'ကျောင်းခွဲ' / 'အခြေခံပညာ...'
            // (e.g. 'အခြေခံပညာအထက်တန်းကျောင်းခွဲ (ခလပ်) ၌', 'နောင်ချိုမြို့နယ် (နမ့်ဆောင်ဟူး) အခြေခံပညာအထက်တန်းကျောင်းခွဲ')
            for (int p = 0; p + 2 < tokCount; p++)
            {
                if (tokens[p].Text != "(" && tokens[p].Text != "（")
                    continue;

                int innerStart = p + 1;
                while (innerStart < tokCount && tokens[innerStart].Kind == TokenKind.Whitespace)
                    innerStart++;

                int innerEnd = innerStart;
                bool allCleanSyl = true;
                while (innerEnd < tokCount && tokens[innerEnd].Text != ")" && tokens[innerEnd].Text != "）")
                {
                    if (tokens[innerEnd].Kind == TokenKind.Whitespace)
                    {
                        innerEnd++;
                        continue;
                    }
                    if (tokens[innerEnd].Kind != TokenKind.MyanmarSyllable ||
                        UnicodeNormalizer.HasLookalikeError(tokens[innerEnd].Text, out _, out _))
                    {
                        allCleanSyl = false;
                        break;
                    }
                    innerEnd++;
                }

                if (!allCleanSyl || innerEnd >= tokCount || (tokens[innerEnd].Text != ")" && tokens[innerEnd].Text != "）"))
                    continue;

                int sylCountInParen = 0;
                for (int k = innerStart; k < innerEnd; k++)
                {
                    if (tokens[k].Kind == TokenKind.MyanmarSyllable)
                        sylCountInParen++;
                }
                if (sylCountInParen < 1 || sylCountInParen > 5)
                    continue;

                // Check left for '...ကျောင်း' or '...ကျောင်းခွဲ' and right for 'အခြေခံပညာ...' or '...ကျောင်း'
                bool adjacentToSchool = false;
                int leftIdx = p - 1;
                while (leftIdx >= 0 && tokens[leftIdx].Kind == TokenKind.Whitespace)
                    leftIdx--;
                if (leftIdx >= 0 && tokens[leftIdx].Kind == TokenKind.MyanmarSyllable)
                {
                    string ls = tokens[leftIdx].NormalizedText;
                    if (ls == "ကျောင်း" ||
                        (ls == "ခွဲ" && leftIdx >= 1 && tokens[leftIdx - 1].Kind == TokenKind.MyanmarSyllable && tokens[leftIdx - 1].NormalizedText == "ကျောင်း"))
                    {
                        adjacentToSchool = true;
                    }
                }

                if (!adjacentToSchool)
                {
                    int rightIdx = innerEnd + 1;
                    while (rightIdx < tokCount && tokens[rightIdx].Kind == TokenKind.Whitespace)
                        rightIdx++;
                    if (rightIdx + 3 < tokCount &&
                        tokens[rightIdx].Kind == TokenKind.MyanmarSyllable && tokens[rightIdx].NormalizedText == "အ" &&
                        tokens[rightIdx + 1].Kind == TokenKind.MyanmarSyllable && tokens[rightIdx + 1].NormalizedText == "ခြေ" &&
                        tokens[rightIdx + 2].Kind == TokenKind.MyanmarSyllable && tokens[rightIdx + 2].NormalizedText == "ခံ")
                    {
                        adjacentToSchool = true;
                    }
                }

                if (adjacentToSchool)
                {
                    for (int k = innerStart; k < innerEnd; k++)
                    {
                        if (tokens[k].Kind == TokenKind.MyanmarSyllable)
                            mask[k] = true;
                    }
                }
            }

            // Protect unprefixed foreign personal names immediately following ministerial/diplomatic office titles
            // (e.g. 'ဆေးဝါးနှင့်ကျန်းမာရေးဝန်ကြီး ဂါဂျန်ဒရာဆင်းနှင့်', 'နိုင်ငံခြားရေးဝန်ကြီး မာကိုရူဘီယိုအား')
            for (int t = 1; t + 2 < tokCount; t++)
            {
                if (tokens[t].Kind != TokenKind.Whitespace)
                    continue;

                bool precededByDiplomaticOffice = false;
                if (tokens[t - 1].Kind == TokenKind.MyanmarSyllable &&
                    !UnicodeNormalizer.HasLookalikeError(tokens[t - 1].Text, out _, out _))
                {
                    string prev1 = tokens[t - 1].NormalizedText;
                    string prev2 = t >= 2 && tokens[t - 2].Kind == TokenKind.MyanmarSyllable ? tokens[t - 2].NormalizedText : string.Empty;
                    string prev3 = t >= 3 && tokens[t - 3].Kind == TokenKind.MyanmarSyllable ? tokens[t - 3].NormalizedText : string.Empty;

                    if ((prev2 == "ဝန်" && prev1 == "ကြီး") ||
                        (prev3 == "ဝန်" && prev2 == "ကြီး" && prev1 == "ချုပ်") ||
                        (prev3 == "အ" && prev2 == "မတ်" && prev1 == "ကြီး") ||
                        (prev3 == "စစ်" && prev2 == "ဝန်" && prev1 == "ချုပ်") ||
                        ((prev2 == "သမ္မ" || prev2 == "မ္မ") && prev1 == "တ"))
                    {
                        precededByDiplomaticOffice = true;
                    }
                }
                if (!precededByDiplomaticOffice)
                    continue;

                int nStart = t + 1;
                while (nStart < tokCount && tokens[nStart].Kind == TokenKind.Whitespace)
                    nStart++;

                if (nStart >= tokCount || tokens[nStart].Kind != TokenKind.MyanmarSyllable ||
                    HonorificFirstSyllables.Contains(tokens[nStart].NormalizedText))
                {
                    continue;
                }

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

                bool hasCaseAnchor = nNameEnd < nChunkEnd;
                if (!hasCaseAnchor)
                {
                    int afterChunk = nChunkEnd;
                    while (afterChunk < tokCount && tokens[afterChunk].Kind == TokenKind.Whitespace)
                        afterChunk++;
                    if (afterChunk < tokCount && tokens[afterChunk].Kind == TokenKind.MyanmarSyllable &&
                        ForeignNameTerminatingCaseParticles.Contains(tokens[afterChunk].NormalizedText))
                    {
                        hasCaseAnchor = true;
                    }
                }

                int nameLen = nNameEnd - nStart;
                if (!hasCaseAnchor || nameLen < 3 || nameLen > 8)
                    continue;

                bool hasForeignNonStandaloneSyl = false;
                bool cleanForeignSpan = true;
                for (int a = nStart; a < nChunkEnd && cleanForeignSpan; a++)
                {
                    if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                    {
                        cleanForeignSpan = false;
                        break;
                    }
                    if (a < nNameEnd && !_trie.IsStandaloneWord(tokens[a].NormalizedText))
                        hasForeignNonStandaloneSyl = true;

                    for (int b = a + 1; b <= nChunkEnd; b++)
                    {
                        string subNorm = GetTokenSpan(fullText, tokens, a, b - a).NormalizedText;
                        if (_trie.TryGetWrongFormRule(subNorm, out _))
                        {
                            cleanForeignSpan = false;
                            break;
                        }
                        if (b <= nNameEnd && b - a >= 2 && HasEstablishedNonProperEntry(subNorm))
                        {
                            cleanForeignSpan = false;
                            break;
                        }
                    }
                }

                if (cleanForeignSpan && hasForeignNonStandaloneSyl)
                {
                    for (int k = nStart; k < nNameEnd; k++)
                        mask[k] = true;
                }
            }

            // V65 B2G Government Portal, Quoted Brand Name, Empire & Foreign Toponym Protection:
            //   (a) Short quoted MSME/brand/enterprise names '“...”' (1..4 contiguous Myanmar syllables with zero internal spaces, lookalikes, or WrongFormRules, e.g. '“ဒေးဒေး” ကယားရိုးရာအထည်များ')
            //   (b) '[1..4 contiguous syllables] + အင်ပါယာ' (e.g. 'မာရထာအင်ပါယာ', 'အော်တိုမန်အင်ပါယာ')
            //   (c) Verified B2G legal/ethnic/foreign toponym spans ('ကော်လိတ္တော်' Collector, 'တိုင်းလိ' Tai Lue, 'ချင်းနိုင်မြို့' Chennai, 'ဘိုပါးမြို့' Bhopal, 'ထျန်းကျင်းမြို့' Tianjin, 'ဘာဂက်မြို့', 'ဝူရှန်တောင်', 'ရေပျံတောင်')
            for (int q = 0; q < tokCount; q++)
            {
                if (tokens[q].Kind == TokenKind.MyanmarSyllable)
                {
                    string s0 = tokens[q].NormalizedText;
                    // 'ကော်လိတ္တော်' (Land Revenue & Stamp Act Collector)
                    if (s0 == "ကော်" && q + 2 < tokCount &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "လိတ္" &&
                        tokens[q + 2].Kind == TokenKind.MyanmarSyllable && tokens[q + 2].NormalizedText == "တော်")
                    {
                        mask[q] = mask[q + 1] = mask[q + 2] = true;
                    }
                    else if (s0 == "ကော်" && q + 1 < tokCount &&
                             tokens[q + 1].Kind == TokenKind.MyanmarSyllable &&
                             tokens[q + 1].NormalizedText.StartsWith("လိ", StringComparison.Ordinal) &&
                             q + 2 < tokCount && tokens[q + 2].Kind == TokenKind.MyanmarSyllable &&
                             (tokens[q + 2].NormalizedText == "တ္တော်" || tokens[q + 2].NormalizedText == "တော်"))
                    {
                        mask[q] = mask[q + 1] = mask[q + 2] = true;
                    }

                    // 'တိုင်းလိ' (Tai Lue / Tai Li ethnic group on moea.gov.mm) and 'အယ်ဂိုရီသမ်' (Algorithm on secm.gov.mm)
                    if (s0 == "တိုင်း" && q + 1 < tokCount &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable &&
                        tokens[q + 1].NormalizedText == "လိ" &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex)
                    {
                        mask[q] = mask[q + 1] = true;
                    }
                    else if (s0 == "အယ်" && q + 3 < tokCount &&
                             tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "ဂို" &&
                             tokens[q + 2].Kind == TokenKind.MyanmarSyllable && tokens[q + 2].NormalizedText == "ရီ" &&
                             tokens[q + 3].Kind == TokenKind.MyanmarSyllable && tokens[q + 3].NormalizedText == "သမ်")
                    {
                        mask[q] = mask[q + 1] = mask[q + 2] = mask[q + 3] = true;
                    }
                    else if (s0 == "မ" && q + 1 < tokCount &&
                             tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "ဂမ်" &&
                             tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex)
                    {
                        mask[q] = mask[q + 1] = true;
                    }
                    else if (s0 == "ဂျီး" && q > 0 && tokens[q - 1].Kind == TokenKind.MyanmarSyllable &&
                             tokens[q - 1].StartIndex + tokens[q - 1].Length == tokens[q].StartIndex)
                    {
                        mask[q - 1] = mask[q] = true;
                        if (q >= 2 && tokens[q - 2].Kind == TokenKind.MyanmarSyllable &&
                            tokens[q - 2].StartIndex + tokens[q - 2].Length == tokens[q - 1].StartIndex)
                        {
                            mask[q - 2] = true;
                        }
                    }
                    else if (s0 == "ဂျော" && q + 1 < tokCount &&
                             tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "ဂင်း" &&
                             tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex)
                    {
                        mask[q] = mask[q + 1] = true;
                    }
                    else if (StyleMode != ProofingStyleMode.FormalOfficial &&
                             s0 == "ကျ" && q + 2 < tokCount &&
                             tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "မ" &&
                             tokens[q + 2].Kind == TokenKind.MyanmarSyllable &&
                             (tokens[q + 2].NormalizedText == "လည်း" || tokens[q + 2].NormalizedText == "က" ||
                              tokens[q + 2].NormalizedText == "ကို" || tokens[q + 2].NormalizedText == "ရဲ့" ||
                              tokens[q + 2].NormalizedText == "တို့" || tokens[q + 2].NormalizedText == "ဟာ") &&
                             (q == 0 || tokens[q - 1].Kind != TokenKind.MyanmarSyllable))
                    {
                        mask[q] = mask[q + 1] = true;
                    }

                    // Foreign/regional toponyms before 'မြို့' or 'တောင်'
                    if (q + 2 < tokCount &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable &&
                        tokens[q + 2].Kind == TokenKind.MyanmarSyllable &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex &&
                        tokens[q + 1].StartIndex + tokens[q + 1].Length == tokens[q + 2].StartIndex)
                    {
                        string s1 = tokens[q + 1].NormalizedText;
                        string s2 = tokens[q + 2].NormalizedText;
                        if ((s2 == "မြို့" &&
                             ((s0 == "ချင်း" && s1 == "နိုင်") ||
                              (s0 == "ဘို" && s1 == "ပါး") ||
                              (s0 == "ထျန်း" && s1 == "ကျင်း") ||
                              (s0 == "ဘာ" && s1 == "ဂက်"))) ||
                            (s2 == "တောင်" &&
                             ((s0 == "ဝူ" && s1 == "ရှန်") ||
                              (s0 == "ရေ" && s1 == "ပျံ"))))
                        {
                            mask[q] = mask[q + 1] = true;
                        }
                    }

                    // '[1..4 contiguous syllables] + အင်ပါယာ' (e.g. 'မာရထာအင်ပါယာ')
                    if (s0 == "အင်" && q + 2 < tokCount &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "ပါ" &&
                        tokens[q + 2].Kind == TokenKind.MyanmarSyllable && tokens[q + 2].NormalizedText == "ယာ" &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex &&
                        tokens[q + 1].StartIndex + tokens[q + 1].Length == tokens[q + 2].StartIndex)
                    {
                        int empStart = q;
                        while (empStart > 0 && q - empStart < 4 &&
                               tokens[empStart - 1].Kind == TokenKind.MyanmarSyllable &&
                               tokens[empStart - 1].StartIndex + tokens[empStart - 1].Length == tokens[empStart].StartIndex &&
                               !GrammaticalParticles.Contains(tokens[empStart - 1].NormalizedText) &&
                               !ColloquialParticles.Contains(tokens[empStart - 1].NormalizedText))
                        {
                            empStart--;
                        }
                        if (empStart < q)
                        {
                            bool hasRule = false;
                            for (int a = empStart; a < q && !hasRule; a++)
                            {
                                for (int b = a + 1; b <= q; b++)
                                {
                                    if (_trie.TryGetWrongFormRule(GetTokenSpan(fullText, tokens, a, b - a).NormalizedText, out _))
                                    {
                                        hasRule = true;
                                        break;
                                    }
                                }
                            }
                            if (!hasRule)
                            {
                                for (int k = empStart; k < q; k++)
                                    mask[k] = true;
                            }
                        }
                    }

                    // Short quoted brand/enterprise names '“...”' (1..4 contiguous syllables containing at least one OOV/non-standalone syllable or reduplicated brand syllable)
                    if (tokens[q].StartIndex > 0 && fullText[tokens[q].StartIndex - 1] == '\u201C')
                    {
                        int qe = q + 1;
                        while (qe < tokCount && qe - q <= 4 &&
                               tokens[qe].Kind == TokenKind.MyanmarSyllable &&
                               tokens[qe - 1].StartIndex + tokens[qe - 1].Length == tokens[qe].StartIndex)
                        {
                            qe++;
                        }
                        int spanSyls = qe - q;
                        int endChar = tokens[qe - 1].StartIndex + tokens[qe - 1].Length;
                        if (spanSyls >= 1 && spanSyls <= 4 && endChar < fullText.Length && fullText[endChar] == '\u201D')
                        {
                            bool brandClean = true;
                            bool hasBrandOov = false;
                            for (int a = q; a < qe && brandClean; a++)
                            {
                                if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                                {
                                    brandClean = false;
                                    break;
                                }
                                if (!_trie.IsStandaloneWord(tokens[a].NormalizedText))
                                    hasBrandOov = true;
                                for (int b = a + 1; b <= qe; b++)
                                {
                                    if (_trie.TryGetWrongFormRule(GetTokenSpan(fullText, tokens, a, b - a).NormalizedText, out _))
                                    {
                                        brandClean = false;
                                        break;
                                    }
                                }
                            }
                            string wholeQuoted = GetTokenSpan(fullText, tokens, q, spanSyls).NormalizedText;
                            if (brandClean && hasBrandOov && !HasEstablishedNonProperEntry(wholeQuoted))
                            {
                                for (int k = q; k < qe; k++)
                                    mask[k] = true;
                            }
                        }
                    }
                }
            }

            // Whole-Chunk Exact-Headword Atomicity Guard:
            // Never split a standalone / whole-chunk exact multi-syllable Trie headword (e.g. 'လှာကဒေါင်ကျေးရွာ', 'ဒေါက်တာဘဒ္ဒန္တဉာဏိဿရ')
            // into partially [masked] + [unmasked] halves.
            var allTokenSpans = ws.AllTokenSpans;
            var tokenSpanOffsets = ws.TokenSpanOffsets;
            for (int st = 0; st < tokCount; st++)
            {
                if (tokens[st].Kind != TokenKind.MyanmarSyllable)
                    continue;
                if (st > 0 && tokens[st - 1].Kind == TokenKind.MyanmarSyllable)
                    continue;

                int sliceStart = tokenSpanOffsets[st];
                int sliceEnd = tokenSpanOffsets[st + 1];
                for (int s = sliceStart; s < sliceEnd; s++)
                {
                    var (len, entries, wrongRule) = allTokenSpans[s];
                    if (len < 2 || wrongRule != null || entries == null || entries.Count == 0)
                        continue;

                    int afterIdx = st + len;
                    if (afterIdx < tokCount &&
                        tokens[afterIdx].Kind == TokenKind.MyanmarSyllable &&
                        !GrammaticalParticles.Contains(tokens[afterIdx].NormalizedText) &&
                        !ColloquialParticles.Contains(tokens[afterIdx].NormalizedText))
                    {
                        continue;
                    }

                    bool contiguous = true;
                    bool anyTrue = false;
                    bool anyFalse = false;
                    for (int k = 0; k < len; k++)
                    {
                        if (k > 0 && tokens[st + k].StartIndex != tokens[st + k - 1].StartIndex + tokens[st + k - 1].Length)
                        {
                            contiguous = false;
                            break;
                        }
                        if (mask[st + k]) anyTrue = true;
                        else anyFalse = true;
                    }
                    if (!contiguous || !anyTrue || !anyFalse)
                        continue;

                    int wholeStart = tokens[st].StartIndex;
                    SyllableToken endTok = tokens[st + len - 1];
                    int wholeLen = (endTok.StartIndex + endTok.Length) - wholeStart;
                    if (!UnicodeNormalizer.HasLookalikeErrorSlice(fullText, wholeStart, wholeLen))
                    {
                        for (int k = 0; k < len; k++)
                            mask[st + k] = false;
                    }
                }
            }

            return mask;
        }

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
                double dist = localPre.DistanceTo(w, tightenMax);
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
                    if ((_trie.ContainsExactWord(pL) && (_trie.GetWordFrequency(pL) >= 3 || GrammaticalParticles.Contains(pL))) &&
                        (_trie.ContainsExactWord(pR) && (_trie.GetWordFrequency(pR) >= 3 || GrammaticalParticles.Contains(pR))))
                    {
                        splitsIntoCleanWords = true;
                        break;
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
                        var fallbackM2 = _suggestionEngine.GetSuggestions(coreAfterSingleFix, maxResults: 4);
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
                    RemoveOtherIssuesInRange(nlpIssues, absCoreStart, absCoreEnd, ref i);
                    string newOrig = text.Substring(absCoreStart, absCoreEnd - absCoreStart);
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
                if (iss.IssueType != ProofingIssueType.SpellingError || iss.Suggestions == null || iss.Suggestions.Count == 0)
                    continue;

                string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText.Trim());

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

                if (iss.Suggestions.Count < 2 || iss.Suggestions[0].Score >= 105.0)
                    continue;

                bool topFormsCompound = CandidateFormsContextCompound(iss.Suggestions[0].Replacement, left1, left2, right1, right2);
                if (topFormsCompound)
                    continue;

                bool origHasClosedCoda = origNorm.IndexOf('\u103A') >= 0;
                bool topKeepsClosedCoda = iss.Suggestions[0].Replacement.IndexOf('\u103A') >= 0 ||
                                          iss.Suggestions[0].Replacement.IndexOf('\u1036') >= 0;

                bool promotedByCompound = false;
                for (int c = 1; c < iss.Suggestions.Count; c++)
                {
                    if (iss.Suggestions[0].Score - iss.Suggestions[c].Score > 6.5)
                        continue;
                    string candRep = iss.Suggestions[c].Replacement;
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
