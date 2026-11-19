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
    public sealed partial class MyanmarProofingEngine
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
                        (string.IsNullOrEmpty(gi.AuthoritativeSource) || gi.AuthoritativeSource.IndexOf("အဆက်တွဲဝိဘတ်", StringComparison.Ordinal) < 0) &&
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
                        if (token.Kind != TokenKind.MyanmarSyllable || IsBlockingLexicalOverlap(nlpIssues, token.StartIndex, token.Length))
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
                               !IsBlockingLexicalOverlap(nlpIssues, tokens[runEnd].StartIndex, tokens[runEnd].Length))
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

            // 3. Productive 4-syllable 'မ[W]တ[W]' half-reduplicated idiom (e.g. 'မလုံတလုံ', 'မပွင့်တပွင့်', 'မသိတသိ', 'မမြင်တမြင်', 'မပေါ်တပေါ်', 'မဝံ့တဝံ့')
            if (startIdx + 3 < count &&
                tokens[startIdx].NormalizedText == "မ" &&
                tokens[startIdx + 2].NormalizedText == "တ" &&
                tokens[startIdx + 1].NormalizedText == tokens[startIdx + 3].NormalizedText)
            {
                SyllableToken t0 = tokens[startIdx];
                SyllableToken t1 = tokens[startIdx + 1];
                SyllableToken t2 = tokens[startIdx + 2];
                SyllableToken t3 = tokens[startIdx + 3];
                if (t1.Kind == TokenKind.MyanmarSyllable &&
                    t2.Kind == TokenKind.MyanmarSyllable &&
                    t3.Kind == TokenKind.MyanmarSyllable &&
                    t1.StartIndex == t0.StartIndex + t0.Length &&
                    t2.StartIndex == t1.StartIndex + t1.Length &&
                    t3.StartIndex == t2.StartIndex + t2.Length &&
                    !HasCleanSpanOfLength(buf, 4) &&
                    IsProductiveRedupChinBase(t1.NormalizedText) &&
                    !UnicodeNormalizer.HasLookalikeErrorSlice(fullText, t0.StartIndex, (t3.StartIndex + t3.Length) - t0.StartIndex))
                {
                    InsertSortedSpan(buf, 4, ProductiveRedupChinEntries);
                }
            }

            // 4. Canonical MLC compound 'စည်းကမ်းစည်းကျပ်' (guards 'စည်းကျပ်' inside discipline compound while keeping 'အခွန်စည်းကျပ် -> စည်းကြပ်')
            if (startIdx + 3 < count &&
                tokens[startIdx].NormalizedText == "စည်း" &&
                tokens[startIdx + 1].NormalizedText == "ကမ်း" &&
                tokens[startIdx + 2].NormalizedText == "စည်း" &&
                tokens[startIdx + 3].NormalizedText == "ကျပ်")
            {
                SyllableToken t0 = tokens[startIdx];
                SyllableToken t1 = tokens[startIdx + 1];
                SyllableToken t2 = tokens[startIdx + 2];
                SyllableToken t3 = tokens[startIdx + 3];
                if (t1.Kind == TokenKind.MyanmarSyllable && t2.Kind == TokenKind.MyanmarSyllable && t3.Kind == TokenKind.MyanmarSyllable &&
                    t1.StartIndex == t0.StartIndex + t0.Length &&
                    t2.StartIndex == t1.StartIndex + t1.Length &&
                    t3.StartIndex == t2.StartIndex + t2.Length &&
                    !HasCleanSpanOfLength(buf, 4))
                {
                    InsertSortedSpan(buf, 4, ProductiveRedupChinEntries);
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

            // Protect proper village/ward toponyms (1..4 contiguous syllables) immediately preceding 'ကျေးရွာ', 'စံပြကျေးရွာ',
            // or administrative-cue-gated 'ရပ်ကွက်'
            // (CheckProtectedSingleSyllable still verifies lookalikes & syllable structure, while skipping false fuzzy mutations)
            bool hasAdminRegionCue =
                fullText.IndexOf("ပြည်နယ်", StringComparison.Ordinal) >= 0 ||
                fullText.IndexOf("တိုင်းဒေသကြီး", StringComparison.Ordinal) >= 0 ||
                fullText.IndexOf("ခရိုင်", StringComparison.Ordinal) >= 0 ||
                fullText.IndexOf("မြို့နယ်", StringComparison.Ordinal) >= 0 ||
                fullText.IndexOf("မြို့", StringComparison.Ordinal) >= 0;
            for (int v = 1; v + 1 < tokens.Count; v++)
            {
                if (tokens[v].Kind != TokenKind.MyanmarSyllable || tokens[v + 1].Kind != TokenKind.MyanmarSyllable)
                    continue;
                bool isVillage = tokens[v].NormalizedText == "ကျေး" && tokens[v + 1].NormalizedText == "ရွာ";
                bool isAdminWard = !isVillage && hasAdminRegionCue &&
                                   tokens[v].NormalizedText == "ရပ်" && tokens[v + 1].NormalizedText == "ကွက်";
                if (!isVillage && !isAdminWard)
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
                while (nameStart > 0 && nameEnd - nameStart < 5)
                {
                    int prev = nameStart - 1;
                    if (tokens[prev].Kind != TokenKind.MyanmarSyllable ||
                        tokens[prev].StartIndex + tokens[prev].Length != tokens[nameStart].StartIndex)
                    {
                        break;
                    }

                    bool hasContiguousBeforePrev =
                        prev > 0 &&
                        tokens[prev - 1].Kind == TokenKind.MyanmarSyllable &&
                        tokens[prev - 1].StartIndex + tokens[prev - 1].Length == tokens[prev].StartIndex;

                    string ps = tokens[prev].NormalizedText;
                    if (GrammaticalParticles.Contains(ps) ||
                        (!hasContiguousBeforePrev && (ColloquialParticles.Contains(ps) || NumeralsAndDemonstratives.Contains(ps))) ||
                        ps == "ရှိ" || ps == "နယ်" || ps == "ခရိုင်" || ps == "မြို့" || ps == "ကြီး" || ps == "တွင်း")
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
                    else if (s0 == "တ" && q + 4 < tokCount &&
                             tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "လုံး" &&
                             tokens[q + 2].Kind == TokenKind.MyanmarSyllable && tokens[q + 2].NormalizedText == "ကြီး" &&
                             tokens[q + 3].Kind == TokenKind.MyanmarSyllable && tokens[q + 3].NormalizedText == "မြို့")
                    {
                        mask[q] = mask[q + 1] = mask[q + 2] = true;
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

                    // Foreign historical era '[1..2 syls] + ဒို + ခေတ်' (e.g. 'အဲဒိုခေတ်')
                    if (s0 == "ဒို" && q > 0 && q + 1 < tokCount &&
                        tokens[q - 1].Kind == TokenKind.MyanmarSyllable &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "ခေတ်" &&
                        tokens[q - 1].StartIndex + tokens[q - 1].Length == tokens[q].StartIndex &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex &&
                        !_trie.ContainsExactWord(tokens[q - 1].NormalizedText + "ဒို") &&
                        !UnicodeNormalizer.HasLookalikeError(tokens[q - 1].Text, out _, out _))
                    {
                        mask[q - 1] = mask[q] = true;
                    }

                    // Named transliterated cyclones '[1..4 contiguous OOV syllables] + မုန်တိုင်း' (e.g. 'ဘီဂျလီမုန်တိုင်း', 'မိုခါမုန်တိုင်း')
                    if (s0 == "မုန်" && q + 1 < tokCount &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "တိုင်း" &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex)
                    {
                        int cycStart = q;
                        while (cycStart > 0 && q - cycStart < 4 &&
                               tokens[cycStart - 1].Kind == TokenKind.MyanmarSyllable &&
                               tokens[cycStart - 1].StartIndex + tokens[cycStart - 1].Length == tokens[cycStart].StartIndex &&
                               !GrammaticalParticles.Contains(tokens[cycStart - 1].NormalizedText) &&
                               !ColloquialParticles.Contains(tokens[cycStart - 1].NormalizedText))
                        {
                            cycStart--;
                        }
                        if (cycStart < q)
                        {
                            string cycPrefixNorm = GetTokenSpan(fullText, tokens, cycStart, q - cycStart).NormalizedText;
                            if (!_trie.ContainsExactWord(cycPrefixNorm))
                            {
                                bool cycClean = true;
                                bool hasCycOovSyl = false;
                                for (int a = cycStart; a < q && cycClean; a++)
                                {
                                    if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                                    {
                                        cycClean = false;
                                        break;
                                    }
                                    if (!_trie.IsStandaloneWord(tokens[a].NormalizedText))
                                        hasCycOovSyl = true;
                                    for (int b = a + 1; b <= q; b++)
                                    {
                                        if (_trie.TryGetWrongFormRule(GetTokenSpan(fullText, tokens, a, b - a).NormalizedText, out _))
                                        {
                                            cycClean = false;
                                            break;
                                        }
                                    }
                                }
                                if (cycClean && hasCycOovSyl)
                                {
                                    for (int k = cycStart; k < q; k++)
                                        mask[k] = true;
                                }
                            }
                        }
                    }

                    // Foreign temple/historical names '[2..5 contiguous OOV syllables] + ဂန်းဂျိ' (e.g. 'နိရှိဟွန်းဂန်းဂျိ')
                    if (s0 == "ဂန်း" && q + 1 < tokCount &&
                        tokens[q + 1].Kind == TokenKind.MyanmarSyllable && tokens[q + 1].NormalizedText == "ဂျိ" &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex)
                    {
                        int jpStart = q;
                        while (jpStart > 0 && q - jpStart < 5 &&
                               tokens[jpStart - 1].Kind == TokenKind.MyanmarSyllable &&
                               tokens[jpStart - 1].StartIndex + tokens[jpStart - 1].Length == tokens[jpStart].StartIndex &&
                               !GrammaticalParticles.Contains(tokens[jpStart - 1].NormalizedText) &&
                               !ColloquialParticles.Contains(tokens[jpStart - 1].NormalizedText))
                        {
                            jpStart--;
                        }
                        if (q - jpStart >= 2)
                        {
                            bool jpClean = true;
                            for (int a = jpStart; a <= q + 1 && jpClean; a++)
                            {
                                if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _))
                                    jpClean = false;
                            }
                            if (jpClean)
                            {
                                for (int k = jpStart; k <= q + 1; k++)
                                    mask[k] = true;
                            }
                        }
                    }

                    // Hyphen-linked foreign historical/proper names '[2..4 syls]-[2..5 syls]' (e.g. 'အဇုချိ-မိုမိုယမ')
                    if (q + 2 < tokCount && tokens[q + 1].Kind != TokenKind.MyanmarSyllable && tokens[q + 1].Text == "-" &&
                        tokens[q].StartIndex + tokens[q].Length == tokens[q + 1].StartIndex &&
                        tokens[q + 2].Kind == TokenKind.MyanmarSyllable &&
                        tokens[q + 1].StartIndex + tokens[q + 1].Length == tokens[q + 2].StartIndex)
                    {
                        int lSt = q;
                        while (lSt > 0 && q - lSt + 1 < 4 &&
                               tokens[lSt - 1].Kind == TokenKind.MyanmarSyllable &&
                               tokens[lSt - 1].StartIndex + tokens[lSt - 1].Length == tokens[lSt].StartIndex)
                        {
                            lSt--;
                        }
                        int rEn = q + 2;
                        while (rEn + 1 < tokCount && rEn - (q + 2) + 1 < 5 &&
                               tokens[rEn + 1].Kind == TokenKind.MyanmarSyllable &&
                               tokens[rEn].StartIndex + tokens[rEn].Length == tokens[rEn + 1].StartIndex)
                        {
                            rEn++;
                        }
                        // Strip trailing case/plural particle on right half if present ('တို့', 'ကို', 'တွင်', 'မှ')
                        while (rEn > q + 2 && (GrammaticalParticles.Contains(tokens[rEn].NormalizedText) || tokens[rEn].NormalizedText == "တို့"))
                        {
                            rEn--;
                        }
                        int lCnt = q - lSt + 1;
                        int rCnt = rEn - (q + 2) + 1;
                        if (lCnt >= 2 && rCnt >= 2)
                        {
                            string lNorm = GetTokenSpan(fullText, tokens, lSt, lCnt).NormalizedText;
                            string rNorm = GetTokenSpan(fullText, tokens, q + 2, rCnt).NormalizedText;
                            if (!_trie.ContainsExactWord(lNorm) && !_trie.ContainsExactWord(rNorm))
                            {
                                bool hyClean = true;
                                bool lHasOov = false, rHasOov = false;
                                for (int a = lSt; a <= q && hyClean; a++)
                                {
                                    if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _) ||
                                        _trie.TryGetWrongFormRule(tokens[a].NormalizedText, out _))
                                        hyClean = false;
                                    if (!_trie.IsStandaloneWord(tokens[a].NormalizedText))
                                        lHasOov = true;
                                }
                                for (int a = q + 2; a <= rEn && hyClean; a++)
                                {
                                    if (UnicodeNormalizer.HasLookalikeError(tokens[a].Text, out _, out _) ||
                                        _trie.TryGetWrongFormRule(tokens[a].NormalizedText, out _))
                                        hyClean = false;
                                    if (!_trie.IsStandaloneWord(tokens[a].NormalizedText))
                                        rHasOov = true;
                                }
                                if (hyClean && (lHasOov || rHasOov))
                                {
                                    for (int a = lSt; a <= q; a++) mask[a] = true;
                                    for (int a = q + 2; a <= rEn; a++) mask[a] = true;
                                }
                            }
                        }
                    }
                }
            }

            // V76 Structural Cue-Gated Personal Name & Creative Credit Shields:
            //   (a) Gazette / Police / Court Age-Parenthetical Names: '[ရွာ/ရပ်ကွက်/မြို့/လမ်း]နေ <Name 1..5 syls> (<Age>)နှစ်' (e.g. 'ကျောက်ချပ်ရွာနေ ကျော်လွင်(၆၄)နှစ်၏')
            //   (b) Age-Prefixed Foreign/Personal Names: 'အသက် ... အရွယ် <Name 2..7 syls>' (e.g. 'အသက် အရွယ် ဂျက်စဝမ့်ဒီလီယန်နှင့်')
            //   (c) Broadcast / Music / Film Credit Lists: 'တေးဆို / တေးရေး / သရုပ်ဆောင် / ဒါရိုက်တာ ...' (e.g. 'တေးဆို … အက်စတာဒေါ့ချင်းဆုန်', 'စန္ဒရားမောင်လှအေး၊ နေထွဏ်းဦး', 'မေသန်းနု၊ မင်းသူ၊ ကျော်ဇောဟိန်း၊ ... ဒါရိုက်တာ')
            ApplyV76StructuralNameShields(fullText, tokens, mask);

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

    }
}
