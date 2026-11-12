// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MyanmarSpellChecker.Core.Models;

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
        private MyanmarTrie _trie;
        private SuggestionEngine _suggestionEngine;
        private readonly OfficialStyleGrammarEngine _grammarEngine;
        private readonly ContextDisambiguator _contextDisambiguator;
        private readonly ReaderWriterLockSlim _lexiconRwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly HashSet<string> _sessionIgnoredWords = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _customUserWords = new HashSet<string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, List<ProofingIssue>> _paragraphCache = new ConcurrentDictionary<string, List<ProofingIssue>>(StringComparer.Ordinal);
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

        // Pure boundary case/sentence particles that must never be absorbed as the end of a repaired compound word
        private static readonly HashSet<string> TrailingBoundaryParticles = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က", "ကနေ",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မယ့်", "မည်", "ခဲ့", "၍", "ကြောင့်", "ဖြင့်", "သဖြင့်", "ဟု", "ကာ", "စွာ", "ပါ့", "လျှင်",
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

        // Burmese honorifics & monastic/historical/military/ethnic titles preceding personal names
        private static readonly HashSet<string> HonorificPrefixes = CreateNormalizedSet(
            "ဦး", "ဒေါ်", "ကို", "မောင်", "ဒေါက်တာ", "ပါမောက္ခ", "ဆရာ", "ဆရာမ", "ဆရာကြီး", "ဆရာမကြီး",
            "ဗိုလ်", "ဗိုလ်ကြီး", "ဗိုလ်မှူး", "ဒုတိယဗိုလ်မှူးကြီး", "ဗိုလ်မှူးကြီး", "ဗိုလ်မှူးချုပ်",
            "ဗိုလ်ချုပ်", "ဒုတိယဗိုလ်ချုပ်ကြီး", "ဗိုလ်ချုပ်ကြီး", "ဒုတိယဗိုလ်ချုပ်မှူးကြီး", "ဗိုလ်ချုပ်မှူးကြီး",
            "အသျှင်", "သျှင်", "အရှင်", "ဘဒ္ဒန္တ", "သခင်", "သူရ", "သီဟသူရ", "စည်သူ", "သီရိပျံချီ", "ဝဏ္ဏကျော်ထင်", "ဇေယျာကျော်ထင်",
            "စိုင်း", "နန်း", "ခွန်", "မစ္စတာ", "မစ္စက်", "မစ္စ"
        );

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
            "ရက်", "ရပ်", "လ", "နှစ်", "နာရီ", "မိနစ်", "မိုင်", "ပွဲ", "ဖွဲ့", "မျိုး", "ဆင့်", "ပိုင်း", "အုပ်", "ပင်", "ခန်း", "စုံ", "တွဲ", "ဆောင်"
        );

        // Clause-closing & subordinate/nominalized negative markers indicating 'မ' is the negative verb prefix (e.g. 'မလာနိုင်ဘူး', 'မရေးရတာ', 'မခံရစေရေး')
        private static readonly HashSet<string> NegativeClauseClosers = CreateNormalizedSet(
            "ဘူး", "ဘဲ", "နဲ့", "နှင့်", "သေး", "ပါ", "တာ", "တဲ့", "သည့်", "လို့", "လျှင်", "ရင်", "သော်",
            "မီ", "ခင်", "စေ", "ရေး", "ခြင်း", "မှု", "ကြောင်း", "ပေ", "ချေ", "နိုင်", "ရာ", "ဖူး"
        );

        // Foreign loanword & Pali final killed consonants (e.g. ကားလ်, ဆိုးလ်, ဆဲလ်, ဘီးရ်, ပူးရ်, ကာတာရ်, ဂိတ်စ်, ဖေ့စ်, ဘေ့စ်, လိုက်ဘ်နစ်ဇ်, မတ်ခ်, သံဃာဒိသိသ်, ပရိဝါသ်)
        // Excludes bare low-tone '-ိုလ်' (ဂြိုလ်, သီဟိုလ်, မြင်းမိုလ်) and '-ာဒ် / -ာဘ်' (လာဒ်, ဆုလာဒ်, ရလာဒ်) so native silent-coda & Pali-coda typos are caught!
        private static readonly System.Text.RegularExpressions.Regex ForeignOrPaliKilledCodaRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?:(?<!\u102D\u102F)\u101C\u103A|(?:[\u102B\u102C\u102E\u1030\u1031\u1032][\u1037\u1038]*)\u101B\u103A|(?<![\u102B\u102C])[\u1012\u1018]\u103A|[\u101E\u1017\u1016\u1002\u1001\u1007\u100B\u100C\u100D]\u103A|\u1001\u103B\u103A|\u101B\u103E\u103A|(?:[\u102B\u102C\u1031\u102E\u1030\u1032][\u1037\u1038]*|[\u1000\u1010\u1014\u1019\u1018\u1015]\u103A[\u1037\u1038]*)\u1005\u103A)[\u1037\u1038]*$",
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
            public readonly string? IssueOriginalText;
            public readonly int UnrecognizedSingleSylTokenIndex;
            public readonly Func<ProofingIssue>? IssueFactory;

            public bool HasIssue => UnrecognizedSingleSylTokenIndex >= 0 || IssueFactory != null;

            public static LatticeEdge Clean(int length, double cost) =>
                new LatticeEdge(length, cost, null, -1, null);

            public static LatticeEdge UnrecognizedSingleSyllable(int tokenIndex, string rawSyl, double cost) =>
                new LatticeEdge(1, cost, rawSyl, tokenIndex, null);

            public static LatticeEdge DeferredIssue(int length, double cost, string originalText, Func<ProofingIssue> issueFactory) =>
                new LatticeEdge(length, cost, originalText, -1, issueFactory);

            private LatticeEdge(int length, double cost, string? issueOriginalText, int unrecognizedSingleSylTokenIndex, Func<ProofingIssue>? issueFactory)
            {
                Length = length;
                Cost = cost;
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
                Interlocked.Increment(ref _engineStateVersion);
                _paragraphCache.Clear();
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

        public List<DictionaryEntry> GetFeaturedKnowledgeEntries(int count = 8)
        {
            _lexiconRwLock.EnterReadLock();
            try { return _trie.GetFeaturedKnowledgeEntries(count); }
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
                Interlocked.Increment(ref _engineStateVersion);
                _paragraphCache.Clear();
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
                Interlocked.Increment(ref _engineStateVersion);
                _paragraphCache.Clear();
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

        public List<ProofingIssue> CheckText(string text)
        {
            _lexiconRwLock.EnterReadLock();
            try
            {
                return CheckTextCore(text);
            }
            finally
            {
                _lexiconRwLock.ExitReadLock();
            }
        }

        private List<ProofingIssue> CheckTextCore(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<ProofingIssue>(0);

            bool canCacheParagraph = text.Length <= 2500;
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
                if (_paragraphCache.Count > 2048)
                {
                    _paragraphCache.Clear();
                }
                _paragraphCache[cacheKey] = CloneIssues(computed);
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

            // 1b. Index-Mapped Invisible Format Projection for Pasted Web/Mobile/PDF Paragraphs
            //     Strips U+200B/U+200C/U+200D/U+FEFF/U+2060/U+00AD and normalizes U+00A0/U+202F -> ' ' before NLP
            //     so hidden zero-width syllable splitters never blind Viterbi, Grammar, or ContextDisambiguator,
            //     then maps every detected issue back to exact raw Word.Range character offsets!
            string nlpText = workingText;
            int[]? projectedToRawIndex = null;
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

            if (invisibleFormatCount > 0)
            {
                int projectedLen = workingText.Length - invisibleFormatCount;
                var projChars = new char[projectedLen];
                projectedToRawIndex = new int[projectedLen];
                int p = 0;
                for (int r = 0; r < workingText.Length; r++)
                {
                    char ch = workingText[r];
                    if (IsInvisibleFormatChar(ch))
                        continue;
                    projChars[p] = IsNonBreakingSpaceChar(ch) ? ' ' : ch;
                    projectedToRawIndex[p] = r;
                    p++;
                }
                nlpText = new string(projChars, 0, p);
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

            // Pre-tokenize once & build Supreme Official-Thatpon Compound Shield + Exact Multi-Word Lexicon Shield + Personal-Name Shield!
            List<SyllableToken> tokens = MyanmarSyllableBreaker.Tokenize(nlpText);
            ComputeCompoundProtectionSpans(
                nlpText,
                tokens,
                out List<(int Start, int End)> officialHeadwordSpans,
                out List<(int Start, int End)> exactLexiconMultiWordSpans,
                out bool[] insideKnownWord);
            bool[] personNameProtected = tokens.Count > 0
                ? ComputePersonNameProtectionMask(nlpText, tokens, insideKnownWord)
                : Array.Empty<bool>();

            // 2. Style & Grammar Check (တ/တစ်, ခုနစ်/ခုနှစ်, မ...ဘဲ, လဲ/လည်း, classifiers, tense, broken compounds, casual typos, plus အပြောစကား->အရေးစကား when FormalOfficial)
            List<ProofingIssue> grammarIssues = _grammarEngine.AnalyzeGrammarAndStyle(nlpText, StyleMode, _trie);
            foreach (var gi in grammarIssues.OrderBy(g => g.StartIndex).ThenByDescending(g => g.Length))
            {
                if (IsIgnoredInSession(gi.OriginalText) || ViolatesOfficialHeadwordSpan(gi, officialHeadwordSpans))
                    continue;

                // In FormalOfficial (Government) mode, never let colloquial-to-literary particle rules ('တယ်->သည်', 'မယ်->မည်', 'နဲ့->နှင့်', 'တွေ->များ')
                // slice inside an exact multi-syllable dictionary compound (e.g. 'ဒစ်ဂျစ်တယ်', 'ဟိုတယ်', 'ပေါ်တယ်', 'ဇင်းမယ်', 'ခနော်ခနဲ့') or a person's name ('ဒေါ်ခင်မေမယ်')!
                bool isFormalColloquialSuffixRule =
                    StyleMode == ProofingStyleMode.FormalOfficial &&
                    gi.IssueType == ProofingIssueType.OfficialStyleGrammarError &&
                    (gi.OriginalText == "တယ်" || gi.OriginalText == "မယ်" || gi.OriginalText == "နဲ့" || gi.OriginalText == "တွေ");

                if (isFormalColloquialSuffixRule &&
                    (ViolatesOfficialHeadwordProperSubspan(gi, exactLexiconMultiWordSpans) ||
                     OverlapsProtectedPersonName(gi, tokens, personNameProtected)))
                {
                    continue;
                }

                AddIssueIfNonOverlapping(nlpIssues, gi);
            }

            // 2b. Curated High-Precision Context-Aware Homophone Phrases (မီ/မှီ, ယာဉ်/ယဉ်, ကြေညာ/ကြော်ငြာ, ဘက်/ဖက်, စာဖတ်/စာဖက်, သဘောကျ, -မူ->-မှု, စိုး/ဆိုး, ဖူး/ဘူး)
            foreach (var ctxIssue in _contextDisambiguator.AnalyzeContextHomophones(nlpText))
            {
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

                    SegmentClauseWithViterbi(nlpText, tokens, runStart, runEnd, nlpIssues);
                    idx = runEnd;
                }

                // 4. Post-Viterbi Data-Driven ContextualRules & Bigram Real-Word Homophone Disambiguation
                //    (Runs strictly on clean words after Viterbi compound repair so broken syllables inside typos are never hijacked)
                foreach (var postCtxIssue in _contextDisambiguator.AnalyzePostViterbiContextAndBigrams(nlpText, _trie, nlpIssues, tokens))
                {
                    if (!IsIgnoredInSession(postCtxIssue.OriginalText) &&
                        !OverlapsProtectedPersonName(postCtxIssue, tokens, personNameProtected) &&
                        !ViolatesOfficialHeadwordProperSubspan(postCtxIssue, officialHeadwordSpans))
                    {
                        AddIssueIfNonOverlapping(nlpIssues, postCtxIssue);
                    }
                }
            }

            // 4b. Guarantee multiple selectable replacement choices (casual-standard, polite, formal, and compositional repairs)
            for (int k = 0; k < nlpIssues.Count; k++)
            {
                _suggestionEngine.EnrichIssueWithSelectableChoices(nlpIssues[k], StyleMode, maxChoices: 4);
            }

            // 5. Universal Deep Burmese Linguistic, Pali Etymological, Phonological & Syntactic Reasoning Enrichment
            MyanmarLinguisticReasoningEngine.EnrichIssuesWithDeepReasoning(nlpText, tokens, nlpIssues, _trie, StyleMode);

            // 5b. Remap projected NLP issues back to exact raw character offsets in `text`
            if (projectedToRawIndex != null)
            {
                for (int k = 0; k < nlpIssues.Count; k++)
                {
                    ProofingIssue pi = nlpIssues[k];
                    if (pi.Length <= 0 || pi.StartIndex < 0 || pi.StartIndex + pi.Length > projectedToRawIndex.Length)
                        continue;

                    int rawStart = projectedToRawIndex[pi.StartIndex];
                    int rawEnd = projectedToRawIndex[pi.StartIndex + pi.Length - 1] + 1;
                    pi.StartIndex = rawStart;
                    pi.Length = rawEnd - rawStart;
                    pi.OriginalText = text.Substring(rawStart, rawEnd - rawStart);
                    AddIssueIfNonOverlapping(issues, pi);
                }

                // Preserve isolated mid-word zero-width corruption warning (<= 2 zero-width chars in sentence)
                // when the word itself had no spelling/meaning error, while avoiding false underlines on clean words
                // across web/Facebook/PDF pasted paragraphs that inject U+200B between every syllable (>= 3 occurrences).
                if (invisibleFormatCount <= 2)
                {
                    foreach (var zwIssue in OfficialStyleGrammarEngine.FindZeroWidthWordCorruptions(workingText))
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

            return issues.OrderBy(i => i.StartIndex).ToList();
        }

        private void ComputeCompoundProtectionSpans(
            string fullText,
            List<SyllableToken> tokens,
            out List<(int Start, int End)> officialSpans,
            out List<(int Start, int End)> exactMultiWordSpans,
            out bool[] insideKnownWord)
        {
            officialSpans = new List<(int Start, int End)>();
            exactMultiWordSpans = new List<(int Start, int End)>();
            insideKnownWord = tokens.Count > 0 ? new bool[tokens.Count] : Array.Empty<bool>();
            if (tokens.Count == 0)
                return;

            var buf = new List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)>(8);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.MyanmarSyllable)
                    continue;

                _trie.MatchSpansAt(tokens, i, Math.Min(12, tokens.Count - i), buf);
                for (int s = 0; s < buf.Count; s++)
                {
                    var (len, entries, wrongRule) = buf[s];
                    if (len >= 2 && wrongRule == null && entries != null && entries.Count > 0)
                    {
                        for (int k = 1; k < len; k++)
                        {
                            insideKnownWord[i + k] = true;
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
                        if (!contiguous) continue;

                        int charStart = tokens[i].StartIndex;
                        SyllableToken lastTok = tokens[i + len - 1];
                        int charLen = (lastTok.StartIndex + lastTok.Length) - charStart;
                        string rawSpan = fullText.Substring(charStart, charLen);
                        if (!UnicodeNormalizer.HasLookalikeError(rawSpan, out _, out _))
                        {
                            var range = (charStart, charStart + charLen);
                            exactMultiWordSpans.Add(range);
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
                                officialSpans.Add(range);
                            }
                        }
                    }
                }
            }
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
                (!string.IsNullOrEmpty(issue.AuthoritativeSource) && issue.AuthoritativeSource.IndexOf("သာသနာရေး", StringComparison.Ordinal) >= 0))
                return false;

            return ViolatesOfficialHeadwordProperSubspan(issue, officialSpans);
        }

        private static bool ViolatesOfficialHeadwordProperSubspan(ProofingIssue issue, List<(int Start, int End)> officialSpans)
        {
            int iStart = issue.StartIndex;
            int iEnd = issue.StartIndex + issue.Length;
            for (int k = 0; k < officialSpans.Count; k++)
            {
                var (oStart, oEnd) = officialSpans[k];
                // Block any regex that slices a STRICT PROPER SUB-SPAN out of a longer Official Thatpon compound
                // (e.g. 'တရက်' inside 'ရုတ်တရက်', 'တမြို့နယ်' inside 'ဟင်္သာတမြို့နယ်', 'ပီး' inside 'ရူပီး', 'အကျွေး' inside 'အကျွေးအမွေး'),
                // while allowing intentional whole-word contextual disambiguation (where oStart == iStart && iEnd == oEnd, e.g. 'အစာ ကြွေးထား')!
                if (oStart <= iStart && iEnd <= oEnd && (oStart < iStart || iEnd < oEnd))
                    return true;
            }
            return false;
        }

        private bool[] ComputePersonNameProtectionMask(string fullText, List<SyllableToken> tokens, bool[] insideKnownWord)
        {
            var mask = new bool[tokens.Count];

            int grace = 0;
            bool awaitingNameStartAfterHonorific = false;
            int foreignNameSpacesLeft = 0;
            int i = 0;

            while (i < tokens.Count)
            {
                SyllableToken tok = tokens[i];
                if (tok.Kind != TokenKind.MyanmarSyllable)
                {
                    // Burmese personal-name alias marker '(ခ)' ("alias / aka", e.g. 'ဦးတင်မိုးအောင် (ခ) နေရှိုင်း၏')
                    if (tok.Text == "(" && i + 2 < tokens.Count &&
                        tokens[i + 1].NormalizedText == "ခ" &&
                        tokens[i + 2].Text == ")")
                    {
                        grace = 4;
                        awaitingNameStartAfterHonorific = true;
                        foreignNameSpacesLeft = 0;
                        i += 3;
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
                            foreignNameSpacesLeft--;
                            i++;
                            continue;
                        }
                    }
                    grace = 0;
                    awaitingNameStartAfterHonorific = false;
                    foreignNameSpacesLeft = 0;
                    i++;
                    continue;
                }

                if (GrammaticalParticles.Contains(tok.NormalizedText) || ColloquialParticles.Contains(tok.NormalizedText))
                {
                    grace = 0;
                    awaitingNameStartAfterHonorific = false;
                    foreignNameSpacesLeft = 0;
                }

                // Never start an honorific in the middle of an existing compound word (e.g. 'ကနဦး', 'ပထမဦးဆုံး', 'ဦးဆောင်ဦးရွက်'), unless preceded by a state title (e.g. 'သူရဦး...')!
                bool precededByTitle = i > 0 && TitleSylBeforeHonorific.Contains(tokens[i - 1].NormalizedText);
                bool canStartHonorific = (!insideKnownWord[i] || precededByTitle) &&
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
                        bool isForeignTitle = matchedHonorificNorm == "မစ္စတာ" || matchedHonorificNorm == "မစ္စက်" || matchedHonorificNorm == "မစ္စ";
                        grace = isForeignTitle ? 6 : 5;
                        awaitingNameStartAfterHonorific = true;
                        foreignNameSpacesLeft = isForeignTitle ? 2 : 0;
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
                        foreignNameSpacesLeft = 0;
                    }
                    else
                    {
                        mask[i] = true;
                        awaitingNameStartAfterHonorific = false;
                        grace--;
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
                if (_trie.ContainsExactWord(span.NormalizedText) ||
                    _trie.ContainsExactWord(fixedSpan) ||
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

            if (!MyanmarSyllableBreaker.IsStructurallyValidSyllable(normSyl, out string structErr))
            {
                AddIssueIfNonOverlapping(issues, new ProofingIssue
                {
                    StartIndex = token.StartIndex,
                    Length = token.Length,
                    OriginalText = rawSyl,
                    IssueType = ProofingIssueType.SpellingError,
                    Message = structErr,
                    Suggestions = _suggestionEngine.GetSuggestions(normSyl)
                });
            }
        }

        private void SegmentClauseWithViterbi(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            List<ProofingIssue> issues)
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
                        !UnicodeNormalizer.HasLookalikeError(singleTok.Text, out _, out _))
                    {
                        return;
                    }
                }
            }

            // Official administrative, tax-form, court-docket & municipal plot initialisms made of bare Myanmar consonants
            // (e.g. 'ရပ/၄', 'တတ-၅၃/၁၀', 'ပတခ(ဝင)-၁', '၁၂/ကခဂ') — prevents Dropped-Asat Tier 1b from turning 'ရပ' -> 'ရပ်' or 'တတ' -> 'တတ်'
            if (m >= 1 && m <= 5 && IsGovernmentCodeInitialismRun(fullText, tokens, runStart, runEnd))
            {
                return;
            }

            var bestCost = new double[m + 1];
            var backPrev = new int[m + 1];
            var backEdge = new LatticeEdge[m + 1];

            for (int i = 1; i <= m; i++)
            {
                bestCost[i] = double.PositiveInfinity;
                backPrev[i] = -1;
            }
            bestCost[0] = 0.0;

            var exactSpansBuffer = new List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)>(12);
            var allExactSpans = new List<(int Length, List<DictionaryEntry>? Entries, OrthographyRule? WrongRule)>(m * 2);
            var spanOffsets = new int[m + 1];
            var candidateEdges = new List<LatticeEdge>(8);

            // Precompute exact Trie spans & multi-syllable word mask [i .. i+len) in ONE single Trie pass per clause!
            var exactMultiWordMask = new bool[m, 13];
            for (int i = 0; i < m; i++)
            {
                spanOffsets[i] = allExactSpans.Count;
                _trie.MatchSpansAt(tokens, runStart + i, Math.Min(12, m - i), exactSpansBuffer);
                for (int s = 0; s < exactSpansBuffer.Count; s++)
                {
                    var sp = exactSpansBuffer[s];
                    allExactSpans.Add(sp);
                    if (sp.WrongRule == null && sp.Entries != null && sp.Entries.Count > 0 && sp.Length <= 12)
                    {
                        exactMultiWordMask[i, sp.Length] = true;
                    }
                }
            }
            spanOffsets[m] = allExactSpans.Count;

            bool isPaliVerseRun = IsClassicalPaliVerseWord(tokens, runStart, runEnd);

            for (int i = 0; i < m; i++)
            {
                if (double.IsPositiveInfinity(bestCost[i]))
                    continue;

                BuildEdgesAt(
                    fullText,
                    tokens,
                    runStart,
                    runEnd,
                    i,
                    exactMultiWordMask,
                    isPaliVerseRun,
                    allExactSpans,
                    spanOffsets[i],
                    spanOffsets[i + 1],
                    candidateEdges);

                for (int eIdx = 0; eIdx < candidateEdges.Count; eIdx++)
                {
                    LatticeEdge edge = candidateEdges[eIdx];
                    int nextPos = i + edge.Length;
                    if (nextPos > m)
                        continue;

                    double total = bestCost[i] + edge.Cost;
                    if (total < bestCost[nextPos])
                    {
                        bestCost[nextPos] = total;
                        backPrev[nextPos] = i;
                        backEdge[nextPos] = edge;
                    }
                }
            }

            // Backtrack optimal Viterbi path from m -> 0 and materialize ONLY winning non-ignored issues!
            var chosenIssues = new List<ProofingIssue>();
            int cur = m;
            while (cur > 0 && backPrev[cur] >= 0)
            {
                LatticeEdge edge = backEdge[cur];
                if (edge.HasIssue && !IsIgnoredInSession(edge.IssueOriginalText!))
                {
                    ProofingIssue materialized = edge.UnrecognizedSingleSylTokenIndex >= 0
                        ? BuildUnrecognizedSingleSyllableIssue(tokens[edge.UnrecognizedSingleSylTokenIndex])
                        : edge.IssueFactory!();
                    chosenIssues.Add(materialized);
                }
                cur = backPrev[cur];
            }

            for (int k = chosenIssues.Count - 1; k >= 0; k--)
            {
                AddIssueIfNonOverlapping(issues, chosenIssues[k]);
            }
        }

        private void BuildEdgesAt(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            int localOffset,
            bool[,] exactMultiWordMask,
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
                var span = GetTokenSpan(fullText, tokens, tokIdx, len);

                if (wrongRule != null)
                {
                    if (wrongRule.Category == "OfficialStyle" && StyleMode == ProofingStyleMode.GeneralStandard)
                    {
                        edgesOut.Add(LatticeEdge.Clean(len, 0.86 + 0.10 * len));
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
                    if (span.NormalizedText == "ရနေ့" && tokIdx > runStart)
                        continue;

                    // Immediacy construction '[V] + လျှင် + [V] + ချင်း' (e.g. 'သတင်းရလျှင်ရချင်း', 'ရောက်လျှင်ရောက်ချင်း') uses 'ချင်း', never 'ခြင်း'
                    if (len == 2 && tokens[tokIdx + 1].NormalizedText == "ချင်း" && tokIdx >= runStart + 2 &&
                        (tokens[tokIdx - 1].NormalizedText == "လျှင်" || tokens[tokIdx - 1].NormalizedText == "ရင်") &&
                        tokens[tokIdx - 2].NormalizedText == tokens[tokIdx].NormalizedText)
                    {
                        continue;
                    }

                    // Do not trigger 'အခက်ခဲ -> အခက်အခဲ' when followed by superlative suffix 'ဆုံး' ('အခက်ခဲဆုံး', 'အသေချာဆုံး')
                    if (len == 3 && tokens[tokIdx].NormalizedText == "အ" &&
                        tokIdx + 3 < runEnd && tokens[tokIdx + 3].NormalizedText == "ဆုံး")
                    {
                        continue;
                    }

                    if (IsProtectedSyntacticBoundarySlice(fullText, tokens, runStart, runEnd, localOffset, len, exactMultiWordMask))
                    {
                        continue;
                    }

                    var capturedSpan = span;
                    var capturedRule = wrongRule;
                    edgesOut.Add(LatticeEdge.DeferredIssue(
                        len,
                        0.55 + 0.08 * len,
                        span.RawText,
                        () =>
                        {
                            string canonCorrect = UnicodeNormalizer.NormalizeCanonicalOrder(capturedRule.CorrectForm);
                            _trie.TryGetExactEntries(canonCorrect, out var targetEntries);
                            string? ruleMeaningCue = targetEntries != null && targetEntries.Count > 0 ? targetEntries[0].MeaningCue : null;
                            var suggestions = _suggestionEngine.GetSuggestions(capturedSpan.NormalizedText);
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
                        }));
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

                    if (UnicodeNormalizer.HasLookalikeError(span.RawText, out string fixedLookalike, out string lookReason))
                    {
                        var capturedSpan = span;
                        var capturedFirstEntry = entries[0];
                        string capFixed = fixedLookalike;
                        string capReason = lookReason;
                        edgesOut.Add(LatticeEdge.DeferredIssue(
                            len,
                            0.86 + 0.10 * len,
                            span.RawText,
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
                            }));
                    }
                    else
                    {
                        exactCleanMask |= 1u << len;
                        edgesOut.Add(LatticeEdge.Clean(len, 0.88 + 0.12 * len - freqBonus));
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
            edgesOut.Add(BuildSingleSyllableEdge(fullText, tokens, runStart, runEnd, localOffset, exactMultiWordMask, isPaliVerseRun));

            // 3. Multi-Syllable Generative Repair Edges (L = 2 .. 5)
            int maxRepairLen = Math.Min(5, remaining);
            for (int windowLen = 2; windowLen <= maxRepairLen; windowLen++)
            {
                if ((exactCleanMask & (1u << windowLen)) != 0)
                    continue;

                LatticeEdge? repairEdge = TryBuildCompoundRepairEdge(
                    fullText, tokens, runStart, runEnd, localOffset, windowLen, exactMultiWordMask, isPaliVerseRun);
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
                bool isPureVerb = _trie.HasPosTag(baseOpen, "V") &&
                                  !_trie.HasPosTag(baseOpen, "N") &&
                                  !_trie.HasPosTag(baseOpen, "PRON");
                if (!isPureVerb)
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

        private static bool IsCoveredByAnyExactMultiWord(bool[,] exactMultiWordMask, int localOffset, int runLen)
        {
            int minStart = Math.Max(0, localOffset - 11);
            for (int st = minStart; st <= localOffset; st++)
            {
                int minLen = Math.Max(2, localOffset - st + 1);
                int maxLen = Math.Min(12, runLen - st);
                for (int len = minLen; len <= maxLen; len++)
                {
                    if (exactMultiWordMask[st, len])
                        return true;
                }
            }
            return false;
        }

        private static bool IsEndOfExactMultiWordMinLen(bool[,] exactMultiWordMask, int endLocalOffset, int minWordLen)
        {
            int minStart = Math.Max(0, endLocalOffset - 11);
            int maxStart = endLocalOffset - (minWordLen - 1);
            for (int st = minStart; st <= maxStart; st++)
            {
                int len = endLocalOffset - st + 1;
                if (len <= 12 && exactMultiWordMask[st, len])
                    return true;
            }
            return false;
        }

        private static bool IsStartOfAnyExactMultiWord(bool[,] exactMultiWordMask, int startLocalOffset, int runLen)
        {
            if (startLocalOffset < 0 || startLocalOffset >= runLen)
                return false;
            int maxLen = Math.Min(12, runLen - startLocalOffset);
            for (int len = 2; len <= maxLen; len++)
            {
                if (exactMultiWordMask[startLocalOffset, len])
                    return true;
            }
            return false;
        }

        private bool IsProtectedSyntacticBoundarySlice(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd,
            int localOffset,
            int windowLen,
            bool[,] exactMultiWordMask)
        {
            int tokIdx = runStart + localOffset;
            if (windowLen < 2 || tokIdx + windowLen > runEnd)
                return false;

            string firstSyl = tokens[tokIdx].NormalizedText;
            string lastSyl = tokens[tokIdx + windowLen - 1].NormalizedText;
            var span = GetTokenSpan(fullText, tokens, tokIdx, windowLen);

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
                ((localOffset >= 2 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 3)) ||
                 (tokIdx > runStart && IsStartOfAnyExactMultiWord(exactMultiWordMask, localOffset + 1, runEnd - runStart))))
            {
                return true;
            }

            if (firstSyl == "ဖွယ်" && tokIdx > runStart &&
                IsStartOfAnyExactMultiWord(exactMultiWordMask, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 2b. Never let subject particle 'က' after an exact multi-syllable noun/pronoun steal the opening syllable of a following exact multi-syllable word
            //     (e.g. 'မိမိကလွှဲအပ်' must never turn 'ကလွှဲ' into 'ကလွဲ', while genuine typos like 'မိမိကလွဲအပ်' still repair 'လွဲအပ် -> လွှဲအပ်')
            if (windowLen == 2 && firstSyl == "က" && localOffset >= 2 &&
                IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset - 1, 2) &&
                IsStartOfAnyExactMultiWord(exactMultiWordMask, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 2c. Never let calendar suffix '-လ' after a multi-syllable month name fuse with a following exact multi-syllable word
            //     (e.g. 'ဝါခေါင်လချည်ဖြူဖွဲ့' must never turn 'လချည်' into 'လှချည်')
            if (windowLen == 2 && firstSyl == "လ" && localOffset >= 2 &&
                (IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 3) ||
                 IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset - 1, 2)) &&
                IsStartOfAnyExactMultiWord(exactMultiWordMask, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 2d. Contextual gerund 'စီးခြင်း' ("riding/boarding/flowing") vs 'စီးချင်း' (only valid before 'ထိုး'/'တိုက်' in 'စီးချင်းထိုး')
            if (span.NormalizedText == "စီးခြင်း")
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
            if (span.NormalizedText == "ချက်ခြင်း")
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
            if (span.NormalizedText == "နိုင်ငံကြား")
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
            if (span.NormalizedText == "ကြမ်းပြု" && tokIdx > runStart)
            {
                string prevSyl = tokens[tokIdx - 1].NormalizedText;
                if (prevSyl == "ဒေ" || prevSyl == "မူ")
                    return true;
            }

            // 5. Contextual '[Place + တောင်] + ဆိုသော/ဆိုသည့်' (e.g. 'ချင်းတောင်ဆိုသော' != 'တောင်းဆို')
            if (span.NormalizedText == "တောင်ဆို" && tokIdx > runStart && tokIdx + 2 < runEnd)
            {
                string nextSyl = tokens[tokIdx + 2].NormalizedText;
                if (nextSyl == "သော" || nextSyl == "\u101E\u100A\u103A\u1037" || nextSyl == "တာ" || nextSyl == "ရာ")
                    return true;
            }

            // 6. Quantitative '-ကျော်' + [Exact Multi-Word Verb/Noun] (e.g. '၁၅၀ ကီလိုဂရမ်ကျော်စားသုံးပြီး')
            if (windowLen == 2 && firstSyl == "ကျော်" && tokIdx > runStart &&
                IsStartOfAnyExactMultiWord(exactMultiWordMask, localOffset + 1, runEnd - runStart))
            {
                return true;
            }

            // 7. Administrative compound 'ဦးစီးရုံး' (e.g. 'မြို့နယ်အခွန်ဦးစီးရုံး' must never slice 'စီးရုံး -> စည်းရုံး')
            if (span.NormalizedText == "စီးရုံး" && tokIdx > runStart &&
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
            if (span.NormalizedText == "စဉ်ပင်" && tokIdx > runStart)
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
                 (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2))))
            {
                return true;
            }

            // 13. Clause/phrase-ending simile particle 'လို' after sound/noun compounds (e.g. 'ချိုးကူသံလို သာသာယာယာပဲ' != 'သံလိုက်')
            if (windowLen == 2 && lastSyl == "လို" && tokIdx + 2 == runEnd &&
                (firstSyl == "သံ" || firstSyl == "ဒီ" || firstSyl == "ဟို" || firstSyl == "ဘယ်" ||
                 (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2))))
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
            if (span.NormalizedText == "ကရော")
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

            return false;
        }

        private static bool IsSpacedBoundCompoundNeighbor(string normSyl, string? prevSyl, string? nextSyl)
        {
            if (prevSyl == normSyl || nextSyl == normSyl)
                return true; // reduplication (e.g. ပျပျ, ကျာကျာ, ရှုတ်ရှုတ်, ဟုပ်ဟုပ်, ခြောင်းခြောင်း)

            if (normSyl == "ဖြား" && nextSyl == "ယောင်း") return true;
            if (normSyl == "ပြောက်" && (nextSyl == "ကျား" || nextSyl == "စွန်း")) return true;
            if (normSyl == "ထွတ်" && (prevSyl == "အ" || prevSyl == "ပြည်" || nextSyl == "မြတ်")) return true;
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
            @"^[ \t]*(?:[-–/][ \t]*[\u1040-\u10490-9]|[\(（][\u1000-\u1021]{1,5}[\)）][ \t]*[-–/])",
            RegexOptions.Compiled);

        private static readonly Regex GovCodeLeftContextRegex = new Regex(
            @"[\u1040-\u10490-9][ \t]*[-–/][ \t]*$",
            RegexOptions.Compiled);

        private static bool IsGovernmentCodeInitialismRun(
            string fullText,
            List<SyllableToken> tokens,
            int runStart,
            int runEnd)
        {
            for (int i = runStart; i < runEnd; i++)
            {
                string norm = tokens[i].NormalizedText;
                if (norm.Length != 1 || norm[0] < '\u1000' || norm[0] > '\u1021')
                    return false;
                if (UnicodeNormalizer.HasLookalikeError(tokens[i].Text, out _, out _))
                    return false;
            }

            int startChar = tokens[runStart].StartIndex;
            int endChar = tokens[runEnd - 1].StartIndex + tokens[runEnd - 1].Length;

            if (endChar < fullText.Length)
            {
                int rightLen = Math.Min(14, fullText.Length - endChar);
                string rightSlice = fullText.Substring(endChar, rightLen);
                if (GovCodeRightContextRegex.IsMatch(rightSlice))
                    return true;

                // Parenthesized sub-code immediately before '-1' / '/2', e.g. '(ဝင)-၁' in 'ပတခ(ဝင)-၁'
                if (startChar > 0 &&
                    (fullText[startChar - 1] == '(' || fullText[startChar - 1] == '（') &&
                    (fullText[endChar] == ')' || fullText[endChar] == '）') &&
                    endChar + 1 < fullText.Length)
                {
                    int afterParenLen = Math.Min(10, fullText.Length - (endChar + 1));
                    string afterParen = fullText.Substring(endChar + 1, afterParenLen);
                    if (GovCodeRightContextRegex.IsMatch(afterParen))
                        return true;
                }
            }

            if (startChar >= 2)
            {
                int leftStart = Math.Max(0, startChar - 10);
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
            bool[,] exactMultiWordMask,
            bool isPaliVerseRun)
        {
            int tokIdx = runStart + localOffset;
            int runLen = runEnd - runStart;
            SyllableToken token = tokens[tokIdx];
            string rawSyl = token.Text;
            string normSyl = token.NormalizedText;

            if (UnicodeNormalizer.HasLookalikeError(rawSyl, out string fixedSyl, out string lookalikeReason))
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

            if (!MyanmarSyllableBreaker.IsStructurallyValidSyllable(normSyl, out string structError))
            {
                var capToken = token;
                string capRaw = rawSyl;
                string capNorm = normSyl;
                string capErr = structError;
                return LatticeEdge.DeferredIssue(
                    1,
                    2.40,
                    rawSyl,
                    () => new ProofingIssue
                    {
                        StartIndex = capToken.StartIndex,
                        Length = capToken.Length,
                        OriginalText = capRaw,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = capErr,
                        Suggestions = _suggestionEngine.GetSuggestions(capNorm)
                    });
            }

            if (GrammaticalParticles.Contains(normSyl) ||
                (StyleMode == ProofingStyleMode.GeneralStandard && ColloquialParticles.Contains(normSyl)) ||
                NumeralsAndDemonstratives.Contains(normSyl))
            {
                return LatticeEdge.Clean(1, 1.02);
            }

            // Contextual Bound-Morpheme & Ghost Pat-Sint Index Tab Repair (e.g. 'ဖြားနေတာလား -> ဖျား', 'ဟုပ်လား -> ဟုတ်', 'ပြောက်သွားတယ် -> ပျောက်', 'ရေသောပ်မယ် -> သောက်')
            if (tokens.Count > 1 &&
                BoundSyllableVerbFrameMap.TryGetValue(normSyl, out var boundFix) &&
                !IsCoveredByAnyExactMultiWord(exactMultiWordMask, localOffset, runLen))
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
                int freq = _trie.GetWordFrequency(normSyl);
                if (freq > 0)
                {
                    double bonus = Math.Min(0.18, Math.Log10(freq + 1) * 0.05);
                    return LatticeEdge.Clean(1, 1.50 - bonus);
                }

                // Zero-frequency archaic standalone headword: valid if alone, but prefers coalescing into a compound
                return LatticeEdge.Clean(1, 1.65);
            }

            // Induced creaky tone on 1-syllable Nouns/Pronouns (e.g. လူ့, သူ့, ငါ့, ကိုယ့်, ရွာ့)
            if (IsValidInducedCreakyToneForm(normSyl, normSyl))
            {
                return LatticeEdge.Clean(1, 1.44);
            }

            // Valid Pali pat-sint / Kinzi / Great-Sa syllable (e.g. က္ကံ, ဗ္ဗ, ဂ္ဂီ, န္ဒ, င်္ခ, ဋ္ဌာ, ဿု), classical Pali Gatha word (e.g. ဓမ္မေန, မက္ကဋော, ကိမ္ပုရိသော, နိဝတ္တဿု), or foreign transliteration coda (ကားလ်, ဂိတ်စ်, ဖေ့စ်, လိုက်ဘ်, သိသ်)
            if ((_trie.IsValidSingleSyllable(normSyl) && (normSyl.IndexOf('\u1039') >= 0 || normSyl.IndexOf('\u103F') >= 0)) ||
                IsForeignOrPaliKilledCoda(normSyl) ||
                isPaliVerseRun ||
                IsAdjacentToParenthesizedCodaClarifier(fullText, token))
            {
                return LatticeEdge.Clean(1, 1.48);
            }

            // Unrecognized single syllable -> defer expensive GetSuggestions unless this L=1 edge actually wins Viterbi!
            return LatticeEdge.UnrecognizedSingleSyllable(tokIdx, rawSyl, 2.65);
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
            bool[,] exactMultiWordMask,
            bool isPaliVerseRun = false)
        {
            int startIndex = runStart + localOffset;
            var span = GetTokenSpan(fullText, tokens, startIndex, windowLen);

            // Tier 1: Lookalike / Misplaced Kinzi / Digit Zero / U-Nya / Tall AA / စျ / သြ / ဦ repair
            if (UnicodeNormalizer.HasLookalikeError(span.RawText, out string fixedCompound, out string lookalikeExplanation))
            {
                string normFixed = UnicodeNormalizer.NormalizeCanonicalOrder(fixedCompound);
                if (_trie.ContainsExactWord(normFixed))
                {
                    var capSpan = span;
                    string capNormFixed = normFixed;
                    string capReason = lookalikeExplanation;
                    return LatticeEdge.DeferredIssue(
                        windowLen,
                        0.85 + 0.10 * windowLen,
                        span.RawText,
                        () =>
                        {
                            var suggestions = _suggestionEngine.GetSuggestions(capNormFixed);
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
                    var capSpan = span;
                    var capRule = ruleAfterLookalike;
                    string capReason = lookalikeExplanation;
                    return LatticeEdge.DeferredIssue(
                        windowLen,
                        0.55 + 0.08 * windowLen,
                        span.RawText,
                        () =>
                        {
                            string targetWord = UnicodeNormalizer.NormalizeCanonicalOrder(capRule.CorrectForm);
                            var suggestions = _suggestionEngine.GetSuggestions(targetWord);
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

                // If the first syllable is a lookalike error on a valid standalone word (e.g. 'ပွါး' -> 'ပွား', 'ခွါ' -> 'ခွာ') followed by a particle/auxiliary ('စေ', 'ကာ'),
                // do not fall through to Tier 2/3 fuzzy repair (which would corrupt 'ပွါးစေ' into 'ပါစေ'); let 1-syllable lookalike repair fix 'ပွါး -> ပွား'!
                if (windowLen == 2 && UnicodeNormalizer.HasLookalikeError(tokens[startIndex].Text, out string fixedFirstSyl, out _))
                {
                    string normFixedFirst = UnicodeNormalizer.NormalizeCanonicalOrder(fixedFirstSyl);
                    string secondSylNorm = tokens[startIndex + 1].NormalizedText;
                    if (_trie.IsStandaloneWord(normFixedFirst) &&
                        (GrammaticalParticles.Contains(secondSylNorm) ||
                         VerbalContextFollowers.Contains(secondSylNorm) ||
                         ProductiveVerbalAuxiliaries.Contains(secondSylNorm)))
                    {
                        return null;
                    }
                }
            }

            if (isPaliVerseRun)
                return null;

            string firstSyl = tokens[startIndex].NormalizedText;
            string lastSyl = tokens[startIndex + windowLen - 1].NormalizedText;

            // Pali Pat-Sint Cluster Boundary Guard: never slice a repair window mid-word starting at or ending immediately before a non-Kinzi pat-sint stack (\u1039 or \u103F)
            if ((startIndex > runStart && HasNonKinziPatSint(firstSyl)) ||
                (startIndex + windowLen < runEnd && HasNonKinziPatSint(tokens[startIndex + windowLen].NormalizedText)))
            {
                return null;
            }

            // Tier 1b: Dropped-Asat ('်', U+103A) Generative Syllable Merge (e.g. 'တယ' -> 'တယ်', 'မယ' -> 'မယ်', 'သည' -> 'သည်', 'စွမ်းဆောင်ရည' -> 'စွမ်းဆောင်ရည်')
            // Excludes bare particle consonants 'က' and 'မ' so 'လက'/'ညက'/'စမ...' never false-merge!
            if (windowLen >= 2 && windowLen <= 5 && IsDroppedAsatBareConsonant(lastSyl, out char finalCons, out string trailingTone))
            {
                string prefixNorm = windowLen == 2
                    ? firstSyl
                    : ConcatNormalizedTokens(tokens, startIndex, windowLen - 1);
                string asatMerged = UnicodeNormalizer.NormalizeCanonicalOrder(prefixNorm + finalCons + "\u103A" + trailingTone);
                if (_trie.TryGetExactEntries(asatMerged, out var mergedEntries) && mergedEntries != null && mergedEntries.Count > 0)
                {
                    int maxMergedFreq = 0;
                    for (int mIdx = 0; mIdx < mergedEntries.Count; mIdx++)
                    {
                        if (mergedEntries[mIdx].Frequency > maxMergedFreq)
                            maxMergedFreq = mergedEntries[mIdx].Frequency;
                    }
                    int minReqFreq = windowLen == 2 ? 80 : 15;
                    if (maxMergedFreq >= minReqFreq)
                    {
                        string chosenTarget = asatMerged;
                        if (StyleMode == ProofingStyleMode.FormalOfficial)
                        {
                            if (chosenTarget == "တယ်") chosenTarget = "သည်";
                            else if (chosenTarget == "မယ်") chosenTarget = "မည်";
                        }

                        var capSpan = span;
                        string capTarget = chosenTarget;
                        var capFirstMerged = mergedEntries[0];
                        return LatticeEdge.DeferredIssue(
                            windowLen,
                            0.85 + 0.10 * windowLen,
                            span.RawText,
                            () =>
                            {
                                var suggestions = _suggestionEngine.GetSuggestions(capTarget);
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
                        symEntries != null && symEntries.Count > 0 &&
                        symEntries.Max(e => e.Frequency) >= (symEntries.Any(e => e.IsOfficialThatpon) ? 10 : 25))
                    {
                        var capSpan = span;
                        string capSym = symmetricCand;
                        var capFirstSym = symEntries[0];
                        return LatticeEdge.DeferredIssue(
                            3,
                            1.08,
                            span.RawText,
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

            if (LeadingBoundaryParticles.Contains(firstSyl) ||
                TrailingBoundaryParticles.Contains(lastSyl))
            {
                return null;
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
            if (span.NormalizedText == "ရနေ့" && startIndex > runStart)
            {
                return null;
            }

            if (IsProtectedSyntacticBoundarySlice(fullText, tokens, runStart, runEnd, localOffset, windowLen, exactMultiWordMask))
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
            if (lastSyl == "ထက်")
            {
                if (windowLen == 2 &&
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

            // Constituent Boundary Guard 2d: Do not absorb 'သ' from literary/interrogative verb particles ('သကဲ့သို့', 'သလော', 'သနည်း', 'သလား', 'သလဲ', 'သတည်း')
            // into the preceding verb (e.g. 'နင်းမိသကဲ့သို့' != 'မိဿ', 'သိသလော' != 'သိဿ', 'သွားပြန်သကဲ့သို့' != 'ပြသ')
            if (lastSyl == "သ" && startIndex + windowLen < tokens.Count &&
                tokens[startIndex + windowLen].Kind == TokenKind.MyanmarSyllable)
            {
                string afterTha = tokens[startIndex + windowLen].NormalizedText;
                if (afterTha == "ကဲ့" || afterTha == "လော" || afterTha == "နည်း" ||
                    afterTha == "လား" || afterTha == "လဲ" || afterTha == "တည်း")
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

            // Never start a compound repair window mid-Kinzi ('င်္' \u1004\u103A\u1039 belongs phonologically to the preceding open syllable, e.g. 'မဟာဥမင်္ဂဇာတ်', 'ဒီပင်္ကရာ')
            if (startIndex > runStart && firstSyl.StartsWith("\u1004\u103A\u1039", StringComparison.Ordinal))
            {
                return null;
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
                (windowLen >= 2 && IsForeignOrPaliKilledCoda(firstSyl) && IsStartOfAnyExactMultiWord(exactMultiWordMask, localOffset + 1, runEnd - runStart)))
            {
                return null;
            }

            // Constituent Boundary Guard 4b: Productive literary & classical constructions ('[Verb] + တော် + မူ', 'အ + [Verb] + ခံ', '[Word] + တရား', '[Root] + ခနဲ')
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
                    IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset + 1, 3) &&
                    (PostNominalAdjectives.Contains(lastSyl) ||
                     (_trie.IsStandaloneWord(lastSyl) && _trie.GetWordFrequency(lastSyl) >= 10)))
                {
                    return null;
                }

                // Cross-compound L=3 head-slicing shield: '[Classifier / EndOfExactMultiWord] + [Exact 2-Syl Official/HighFreq Word]'
                // (e.g. 'မြန်မာပိုင်းသတင်း' != 'ပိုးသီတင်း', 'ပိုးဝင်ရောဂါ' != 'ဝမ်းရောဂါ', '၆ လနီးပါး' != 'လုနီးပါး', 'ဒေသနာတော်အဆုံး' != 'အတော်ဆုံး')
                if (exactMultiWordMask[localOffset + 1, 2] &&
                    firstSyl != "အ" && firstSyl != "မ" && firstSyl != "တ" &&
                    (NumeralClassifiers.Contains(firstSyl) ||
                     (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2))))
                {
                    var right2Span = GetTokenSpan(fullText, tokens, startIndex + 1, 2);
                    if (_trie.TryGetExactEntries(right2Span.NormalizedText, out var r2Entries) &&
                        r2Entries != null &&
                        r2Entries.Any(re => (re.IsOfficialThatpon && re.Frequency >= 8) || re.Frequency >= 35))
                    {
                        return null;
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
                     (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2)) ||
                     (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2)))
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

                // Productive '[Verb/CompoundEnd] + [Auxiliary/Copula/PureVerb] + [Nominalizer]'
                // (e.g. 'စွမ်းဆောင်ရည်ရှိမှု' != 'ရရှိမှု', 'တန်းတူရှိမှု' != 'တည်ရှိမှု', 'ပုံပြင်ပြောခြင်း' != 'ပြန့်ပြောခြင်း', 'ရောင်းချနေမှု' != 'ကျနမှု')
                if (((midSyl == "နေ" || midSyl == "ပေး" || midSyl == "ထား" || midSyl == "ခဲ့" || midSyl == "ရ" ||
                      midSyl == "သွား" || midSyl == "လာ" || midSyl == "နိုင်" || midSyl == "တတ်" || midSyl == "စေ" ||
                      midSyl == "ပြီး" || midSyl == "ရှိ" || midSyl == "ဖြစ်") &&
                     ((localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2)) ||
                      (_trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 2))) ||
                    (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2) && IsPureVerbSyllable(midSyl)))
                {
                    if (lastSyl == "မှု" || lastSyl == "ခြင်း" || lastSyl == "ချက်" || lastSyl == "ရေး" ||
                        lastSyl == "ရာ" || lastSyl == "သူ" || lastSyl == "ဖွယ်" || lastSyl == "နည်း")
                    {
                        return null;
                    }
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
                     (localOffset >= 1 && IsEndOfExactMultiWordMinLen(exactMultiWordMask, localOffset, 2)) ||
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
            }

            // Transliterated foreign/Pali street names '[ForeignKilledCoda] + လမ်း' (e.g. 'ဘားနဒ်လမ်း' != 'နတ်လမ်း')
            if (windowLen == 2 && lastSyl == "လမ်း" && IsForeignOrPaliKilledCoda(firstSyl))
            {
                return null;
            }

            // Multi-syllable exact noun/compound + 2-syllable postposition (e.g. 'နိုင်ငံ့အရေးအတွက်' != 'အရေအတွက်', 'တိုင်းဒေသကြီးအတွင်း')
            if (windowLen >= 4 &&
                tokens[startIndex + windowLen - 2].NormalizedText == "အ" &&
                TwoSyllablePostpositionSeconds.Contains(lastSyl) &&
                exactMultiWordMask[localOffset, windowLen - 2])
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
                exactMultiWordMask[localOffset, windowLen - 1] &&
                PostNominalAdjectives.Contains(lastSyl))
            {
                var stemSpan = GetTokenSpan(fullText, tokens, startIndex, windowLen - 1);
                if (_trie.TryGetExactEntries(stemSpan.NormalizedText, out var stemEntries) &&
                    stemEntries != null &&
                    stemEntries.Any(e => !string.Equals(e.PosTag, "PART", StringComparison.OrdinalIgnoreCase) &&
                                         (e.IsOfficialThatpon || e.Frequency >= 2)))
                {
                    isExactContentStemPlusPostAdj = true;
                }
            }

            bool isExactStemPlusValidSuffix =
                windowLen >= 3 &&
                exactMultiWordMask[localOffset, windowLen - 1] &&
                (!isEuphonicPeOrLe || euphonicAtLiteraryClose) &&
                (GrammaticalParticles.Contains(lastSyl) ||
                 ColloquialParticles.Contains(lastSyl) ||
                 VerbalContextFollowers.Contains(lastSyl) ||
                 ProductiveVerbalAuxiliaries.Contains(lastSyl) ||
                 isExactContentStemPlusPostAdj ||
                 lastSyl == "ပင်" || lastSyl == "ဆေး" || lastSyl == "မဲ့" || lastSyl == "ထက်" ||
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
                if (!exactMultiWordMask[localOffset, longerLen])
                    continue;

                var longerSpan = GetTokenSpan(fullText, tokens, startIndex, longerLen);
                if (!_trie.TryGetExactEntries(longerSpan.NormalizedText, out var longerEntries) ||
                    longerEntries == null ||
                    !longerEntries.Any(le => le.IsOfficialThatpon || le.Frequency >= 15))
                {
                    continue;
                }

                int afterExactOffset = localOffset + longerLen;
                if (afterExactOffset >= runLenForPrefixShield)
                    return null;

                string afterExactSyl = tokens[runStart + afterExactOffset].NormalizedText;
                if (IsStartOfAnyExactMultiWord(exactMultiWordMask, afterExactOffset, runLenForPrefixShield) ||
                    GrammaticalParticles.Contains(afterExactSyl) ||
                    ColloquialParticles.Contains(afterExactSyl) ||
                    NumeralsAndDemonstratives.Contains(afterExactSyl) ||
                    (_trie.IsStandaloneWord(afterExactSyl) && _trie.GetWordFrequency(afterExactSyl) >= 2))
                {
                    return null;
                }
            }

            // Check whether this span has an exact multi-syllable prefix compound (e.g. 'မနက်'+'ဖန်', 'သတိ'+'မဲ့', 'အပြင်း'+'ထွက်')
            bool hasExactPrefixCompound = false;
            for (int split = 2; split < windowLen; split++)
            {
                if (exactMultiWordMask[localOffset, split])
                {
                    hasExactPrefixCompound = true;
                    break;
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

            bool hasPreQuery = false;
            SuggestionEngine.PreEncodedQuery preQuery = default;

            // Tier 2: Strict Tone-Preserving Orthographic Homophone Skeleton
            DictionaryEntry? bestStrict = null;
            double bestStrictDist = double.MaxValue;
            double strictMaxDist = windowLen >= 3 ? (hasExactPrefixCompound ? 0.65 : 1.15) : 0.78;
            List<DictionaryEntry> strictMatches = _trie.LookupStrictHomophones(span.NormalizedText);
            if (strictMatches.Count > 0)
            {
                int minStrictFreq = (!isPartialSliceOfRun && !isProductiveANominalization && !hasExactPrefixCompound) ? 1 : 3;
                for (int i = 0; i < strictMatches.Count; i++)
                {
                    var e = strictMatches[i];
                    if (e.Word == span.NormalizedText || !IsCompatibleCompoundLength(e, windowLen))
                        continue;
                    if (windowLen == 2 && !e.IsOfficialThatpon && e.IsProperNoun && !followedByGeoClassifier)
                        continue;

                    if (bothFullStandaloneInL2 || hasExactPrefixCompound || isProductiveANominalization)
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
                    if (d <= strictMaxDist && IsBetterCandidate(e, d, bestStrict, bestStrictDist))
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
                span.NormalizedText, tokens, startIndex, windowLen, firstSyl, lastSyl, hasExactPrefixCompound, bothFullStandaloneInL2, isPartialSliceOfRun, ref hasPreQuery, ref preQuery, out bestFuzzyDist);
            if (isProductiveANominalization && bestFuzzy != null &&
                bestFuzzy.Word.IndexOf('\u1039') < 0 &&
                !AreTrueBurmeseHomophones(span.NormalizedText, bestFuzzy.Word))
            {
                bool allowSameOnsetNasalOrToneRepair =
                    windowLen == 2 &&
                    firstSyl == "အ" &&
                    bestFuzzy.IsOfficialThatpon &&
                    bestFuzzy.Frequency >= 60 &&
                    bestFuzzyDist <= 0.28 &&
                    SharesSameSecondSyllableOnsetCluster(lastSyl, bestFuzzy.Word);
                if (!allowSameOnsetNasalOrToneRepair)
                {
                    bestFuzzy = null;
                }
            }

            // Never let Tier 3 Fuzzy corrupt an already-exact 2-syllable Official/HighFreq right constituent after a standalone word (e.g. 'အိမ်အနီး' != 'အိမ်အမီ')
            if (bestFuzzy != null && windowLen == 3 && exactMultiWordMask[localOffset + 1, 2] &&
                firstSyl != "အ" && firstSyl != "မ" && firstSyl != "တ" &&
                _trie.IsStandaloneWord(firstSyl) && _trie.GetWordFrequency(firstSyl) >= 12)
            {
                var right2Span = GetTokenSpan(fullText, tokens, startIndex + 1, 2);
                if (!bestFuzzy.Word.EndsWith(right2Span.NormalizedText, StringComparison.Ordinal) &&
                    _trie.TryGetExactEntries(right2Span.NormalizedText, out var r2List) &&
                    r2List != null &&
                    r2List.Any(re => (re.IsOfficialThatpon && re.Frequency >= 8) || re.Frequency >= 35))
                {
                    bestFuzzy = null;
                }
            }

            // Compare Tier 2 vs Tier 3 so high-frequency Thatpon words (e.g. 'အင်္ကျီ' freq 140) beat obscure zero-freq collisions ('အကျည်' freq 0)
            if (bestStrict != null && (bestFuzzy == null || !IsBetterCandidate(bestFuzzy, bestFuzzyDist, bestStrict, bestStrictDist)))
            {
                double freqBonus = Math.Min(0.24, Math.Log10(bestStrict.Frequency + 1) * 0.065);
                double thatponBonus = bestStrict.IsOfficialThatpon ? 0.06 : 0.0;
                var capSpan = span;
                var capBestStrict = bestStrict;
                double capStrictDist = bestStrictDist;

                return LatticeEdge.DeferredIssue(
                    windowLen,
                    0.96 + 0.12 * windowLen + bestStrictDist * 0.85 - freqBonus - thatponBonus,
                    span.RawText,
                    () =>
                    {
                        var suggestions = _suggestionEngine.GetSuggestions(capSpan.NormalizedText);
                        EnsureTopSuggestionMetadata(suggestions, capSpan.RawText, capBestStrict.Word, capBestStrict.MeaningCue, capBestStrict.PosTag, capBestStrict.IsOfficialThatpon);
                        string diffDetail = OrthographicDiffExplainer.ExplainDifference(capSpan.RawText, capBestStrict.Word);
                        bool strictNeedsHumanReview =
                            IsProperNameDictionaryEntry(capBestStrict) ||
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
                    });
            }

            if (bestFuzzy == null)
                return null;

            double fBonus = Math.Min(0.24, Math.Log10(bestFuzzy.Frequency + 1) * 0.065);
            double tBonus = bestFuzzy.IsOfficialThatpon ? 0.06 : 0.0;
            var capFuzzySpan = span;
            var capBestFuzzy = bestFuzzy;
            double capFuzzyDist = bestFuzzyDist;

            return LatticeEdge.DeferredIssue(
                windowLen,
                1.00 + 0.12 * windowLen + bestFuzzyDist * 1.10 - fBonus - tBonus,
                span.RawText,
                () =>
                {
                    var fuzzySuggestions = _suggestionEngine.GetSuggestions(capFuzzySpan.NormalizedText);
                    EnsureTopSuggestionMetadata(fuzzySuggestions, capFuzzySpan.RawText, capBestFuzzy.Word, capBestFuzzy.MeaningCue, capBestFuzzy.PosTag, capBestFuzzy.IsOfficialThatpon);
                    string fuzzyDiff = OrthographicDiffExplainer.ExplainDifference(capFuzzySpan.RawText, capBestFuzzy.Word);
                    bool fuzzyNeedsHumanReview =
                        IsProperNameDictionaryEntry(capBestFuzzy) ||
                        capFuzzyDist > 0.36;

                    return new ProofingIssue
                    {
                        StartIndex = capFuzzySpan.StartIndex,
                        Length = capFuzzySpan.Length,
                        OriginalText = capFuzzySpan.RawText,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = BuildDetailedSpellingMessage(capFuzzySpan.RawText, capBestFuzzy.Word, capBestFuzzy.MeaningCue, fuzzyDiff),
                        RequiresReviewBeforeBatchFix = fuzzyNeedsHumanReview,
                        Suggestions = fuzzySuggestions
                    };
                });
        }

        private DictionaryEntry? TryFindBestFuzzyCandidate(
            string spanNormalized,
            List<SyllableToken> tokens,
            int startIndex,
            int windowLen,
            string firstSyl,
            string lastSyl,
            bool hasExactPrefixCompound,
            bool bothFullStandaloneInL2,
            bool isPartialSliceOfRun,
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

                if (f1 >= 40 && PostNominalAdjectives.Contains(lastSyl))
                    return null;

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
                fuzzyMaxDist = hasExactPrefixCompound ? 0.36 : 0.68;
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

            List<DictionaryEntry> phoneticHits = _trie.LookupByPhoneticKey(spanNormalized, out string spanPhoneticKey);

            bool zeroEmpiricalBigram = windowLen == 2 &&
                                       _trie.BigramCount > 0 &&
                                       _trie.GetBigramCount(firstSyl, lastSyl) == 0 &&
                                       !ProductiveVerbalAuxiliaries.Contains(lastSyl);
            bool spanHasPaliPatSintOrRetroflex = HasNonKinziPatSintOrRetroflex(spanNormalized);
            bool spanHasNonKinziPatSint = spanHasPaliPatSintOrRetroflex && HasNonKinziPatSint(spanNormalized);
            bool followedByGeoClassifier = windowLen == 2 && IsFollowedByGeographicClassifier(tokens, startIndex + windowLen);
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

                bool isCredibleLexeme = e.IsOfficialThatpon || e.Frequency >= 4 ||
                                        e.IsProperNoun ||
                                        string.Equals(e.PosTag, "LOAN", StringComparison.OrdinalIgnoreCase);
                if (!isCredibleLexeme)
                    return;

                double allowedMaxDist = fuzzyMaxDist;
                if (windowLen >= 3 && hasExactPrefixCompound && e.Frequency < 10)
                    return;

                if (windowLen == 2 && bothKnownInL2)
                {
                    if (bothFullStandaloneInL2)
                    {
                        if (pureVerbPairInL2 && e.Frequency < 140)
                            return;
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

                        if (!signalBoundOrZeroFreq && !signalVerbalContext && !signalFreqDominance && !signalPhoneticOfficial && !signalZeroBigramOfficial)
                            return;

                        if (signalZeroBigramOfficial && allowedMaxDist < 0.42)
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
                double dist = localPre.DistanceTo(w, allowedMaxDist);
                if (dist > allowedMaxDist)
                    return;

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
                    if (exBucket != null)
                    {
                        for (int i = 0; i < exBucket.Count; i++)
                            EvaluateCandidate(exBucket[i], enforceAnchorLengthDelta: true);
                    }
                    if (phBucket != null)
                    {
                        for (int i = 0; i < phBucket.Count; i++)
                            EvaluateCandidate(phBucket[i], enforceAnchorLengthDelta: true);
                    }
                }
            }

            hasPreQuery = localHasPre;
            preQuery = localPre;
            bestFuzzyDist = localBestDist;
            return bestFuzzy;
        }

        private static bool IsBetterCandidate(DictionaryEntry cand, double candDist, DictionaryEntry? currentBest, double currentBestDist)
        {
            if (currentBest == null)
                return true;

            // Combine weighted edit distance with corpus frequency log-prior so high-usage headwords beat obscure collisions at nearly equal distance
            double candRankScore = candDist - Math.Min(0.18, Math.Log10(cand.Frequency + 1) * 0.045) - (cand.IsOfficialThatpon ? 0.02 : 0.0);
            double bestRankScore = currentBestDist - Math.Min(0.18, Math.Log10(currentBest.Frequency + 1) * 0.045) - (currentBest.IsOfficialThatpon ? 0.02 : 0.0);
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

        private static bool IsDroppedAsatBareConsonant(string syl, out char cons, out string trailingTone)
        {
            cons = '\0';
            trailingTone = string.Empty;
            if (string.IsNullOrEmpty(syl) || syl.Length > 2)
                return false;

            char c0 = syl[0];
            // Valid Burmese coda consonants excluding 'က' and 'မ' (which are standalone case/negative particles)
            bool isCodaCandidate = c0 == '\u1004' || // င
                                   c0 == '\u1005' || // စ
                                   c0 == '\u1009' || // ဉ
                                   c0 == '\u100A' || // ည
                                   c0 == '\u100F' || // ဏ
                                   c0 == '\u1010' || // တ
                                   c0 == '\u1014' || // န
                                   c0 == '\u1015' || // ပ
                                   c0 == '\u101A' || // ယ
                                   c0 == '\u101D';   // ဝ
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
            string norm = ConcatNormalizedTokens(tokens, startIdx, count);
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
    }
}
