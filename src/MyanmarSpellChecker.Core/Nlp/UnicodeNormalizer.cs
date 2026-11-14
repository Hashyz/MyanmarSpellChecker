using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Handles Pyidaungsu / Myanmar Unicode 5.1+ canonical combining mark normalization,
    /// lookalike character error detection (၀ U+1040 vs ဝ U+101D, ၄င်း vs ၎င်း, ဥ်/ဥာ vs ဉ်/ဉာ, ဓါတ်/ဖေါ် tall AA misuse),
    /// and legacy Zawgyi-One detection & conversion.
    /// </summary>
    public static class UnicodeNormalizer
    {
        // Zawgyi-specific codepoints (U+1033, U+1034, U+105A, U+1060..U+1097), visual-order E/Ra before consonant, Zawgyi 'မ်ား'/'ဥပေဒ', bare Zawgyi 'င္' coda, Zawgyi '\u1039' + Consonant + Ya-pin '\u103A', or Zawgyi asat (\u1039 before အ/tone/E/vowel/boundary)
        private static readonly Regex ZawgyiPattern = new Regex(
            @"[\u1033\u1034\u105A\u1060-\u1097]|(?:^|[\s\u1038\u103A\u104A\u104B(\[""'“‘/])\u1031[\u1000-\u1021\u103B-\u103E]|(?:^|[\s\u104A\u104B(\[""'“‘/])\u103B[\u1000-\u1021]|[\u102B-\u1030\u1032\u1036\u1037]\u1031[\u1000-\u1021\u103B-\u103E]|\u1039(?=[\s\u104A\u104B\u1037\u1038\u1031\u103B\u1021\u1023-\u102A\u104C-\u104F)\]}""'”’/]|$)|(?<!\u1004\u103A)\u1004\u1039[\u1000-\u1021]|\u1039[\u1000-\u1021]\u103A(?![\u1037-\u1039])|\u1019\u103A\u102C\u1038|\u1025\u1015\u1031\u1012",
            RegexOptions.Compiled);

        // Unambiguous Pyidaungsu Unicode 5.1+ structural markers inside a Myanmar token (impossible in Zawgyi-One: logical E before AA/tone/end, Unicode kinzi \u1004\u103A\u1039, Unicode Ha-hto \u103E, or non-Ya-pin coda + Unicode asat \u103A)
        private static readonly Regex DefiniteUnicodeWordMarkerPattern = new Regex(
            @"\u1031(?:[\u102B\u102C\u1037\u1038]|$)|\u1004\u103A\u1039|\u103E|[\u1004\u1009\u100A\u100F\u1010\u1014\u101A\u101B\u101D]\u103A",
            RegexOptions.Compiled);

        private static readonly Regex MyanmarWordTokenScanPattern = new Regex(
            @"[\u1000-\u109F]+",
            RegexOptions.Compiled);

        // Double-converted Unicode ("Unicode -> Zawgyi-to-Unicode mojibake", e.g. 'သငျ့စကျတှငျ ဒီမီဒီယာဖိုငျကို ဖှငျ့၍' where \u103A->\u103B, \u103B->\u103C, \u103C->\u103D, \u103D->\u103E).
        // Verified 0 / 89,374 false positives across the entire Myanmar lexicon!
        private static readonly Regex DoubleConvertedUnicodeTokenPattern = new Regex(
            @"(?<=[\u1000-\u1036\u103B-\u103E])[\u1004\u1009\u100A\u1010\u1014]\u103B(?![\u102B-\u1036\u103A\u103C-\u103E]|[\u1000-\u1021][\u103A\u1039])|(?<=[\u1000-\u1036\u103B-\u103E])\u1005\u103B(?![\u102B-\u1036\u103A\u103C-\u103E]|[\u1000-\u1021][\u103A\u1039])|\u103E\u103E",
            RegexOptions.Compiled);

        private static readonly Regex DoubleConvertedCompanionTokenPattern = new Regex(
            @"(?<=[\u1000-\u1036\u103B-\u103E])[\u1000\u1004\u1005\u1009\u100A\u100F\u1010\u1014\u1015\u1019\u101A\u101D]\u103B(?![\u102B-\u1036\u103A\u103C-\u103E]|[\u1000-\u1021][\u103A\u103B\u1039])|[\u1000-\u1002\u1005-\u1007\u1010-\u1012\u1015-\u1018\u101E\u1021]\u103E",
            RegexOptions.Compiled);

        private static readonly Regex DoubleConvertedPatSintInversePattern = new Regex(
            @"([\u1000-\u1021])\u103B\u103A",
            RegexOptions.Compiled);

        private static readonly Regex DoubleConvertedCodaYaPinInversePattern = new Regex(
            @"(?<=[\u1000-\u1036\u103B-\u103E])(?<Coda>[\u1000\u1004\u1005\u1009\u100A\u100F\u1010\u1014\u1015\u1019\u101A\u101D])\u103B(?![\u102B-\u1036\u103A\u103C-\u103E]|[\u1000-\u1021][\u103A\u103B\u1039])",
            RegexOptions.Compiled);

        private static readonly Regex DoubleConvertedNonSonorantHaHtoInversePattern = new Regex(
            @"(?<=[\u1000-\u1002\u1005-\u1007\u1010-\u1012\u1015-\u1018\u101E\u1021])\u103E",
            RegexOptions.Compiled);

        // 1. Digit Zero '၀' (U+1040) misused as Consonant Wa 'ဝ' (U+101D) (exempts time-visarga '၁၀း၀၀ -> ၁၀:၀၀')
        private static readonly Regex MisusedDigitZeroPattern = new Regex(
            @"\u1040(?!\u1038[\u1040-\u1049])(?=[\u1000-\u103F])|(?<=[\u1000-\u103F])(?<![\u1040-\u1049]\u1038)\u1040(?![\u1040-\u1049])",
            RegexOptions.Compiled);

        // 2. Digit Four '၄' (U+1044) or '၎' (U+104E) with stray marks (e.g. '၄်င်း', '၎်င်း'), '၄င်း', '၄င်', '၎င်', or doubled '၎င်းင်း' instead of '၎င်း'
        private static readonly Regex MisusedLaKaungPattern = new Regex(
            @"(?:\u1044[\u102B-\u103E]*|\u104E[\u102B-\u103E]+)(?:\u1004[\u102B-\u103E]*)+|\u104E(?:\u1004\u103A\u1038){2,}|\u104E\u1004\u103A(?![\u1037\u1038\u1039])",
            RegexOptions.Compiled);

        // 2b. Stray combining marks attached directly to Myanmar numerals ၁-၉ causing dotted circles (e.g. '၁်' -> '၁'; exempts time-visarga '၀၉း၀၀ -> ၀၉:၀၀')
        private static readonly Regex DigitWithStrayMarkPattern = new Regex(
            @"(?<Digit>[\u1041-\u1049]+)(?!\u1038[\u1040-\u1049])[\u102B-\u103E]+",
            RegexOptions.Compiled);

        // 3. Independent Vowel U 'ဥ' (U+1025) misused as Consonant Nya 'ဉ' (U+1009) before Asat (\u103A) or AA (\u102C)
        // Examples: စဥ် -> စဉ်, အစီအစဥ် -> အစီအစဉ်, ယာဥ် -> ယာဉ်, ဥာဏ် -> ဉာဏ်
        private static readonly Regex MisusedVowelUPattern = new Regex(
            @"\u1025(?=[\u103A\u102C\u1039\u103E]|\u1037\u103A)",
            RegexOptions.Compiled);

        // 3b. Consonant Nya 'ဉ' (U+1009) misused as Independent Vowel U 'ဥ' (U+1025) or 'ဦ' (U+1026)
        // Examples: ဉက္ကဋ္ဌ -> ဥက္ကဋ္ဌ, ဉပဒေ -> ဥပဒေ, ဉပမာ -> ဥပမာ, ဉယျာဉ် -> ဥယျာဉ်, ဉစ္စာ -> ဥစ္စာ, ဉီး -> ဦး, ကြက်ဉ -> ကြက်ဥ
        private static readonly Regex MisusedNyaIiAsUuPattern = new Regex(
            @"\u1009\u102E",
            RegexOptions.Compiled);

        private static readonly Regex MisusedNyaAsVowelUPattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])\u1009|\u1009(?=[\u1000-\u103F]))(?!(?:[\u102C\u102E\u1031\u103A\u1037\u1038]|\u1039[\u1005-\u1009]|\u103E|\u1010\u103A|\u1010\u1039\u1010|ကလေး|ငယ်|လေး))",
            RegexOptions.Compiled);

        // 4. Tall AA 'ါ' (U+102B) illegally attached to consonants that strictly require Round AA 'ာ' (U+102C),
        // or after any medial (ျ/ြ/ွ/ှ) except 'ဒွါ' (ဒွါရ) and Pali 'ဇိဝှါ'.
        // Only un-medialed ခ (1001), ဂ (1002), င (1004), ဒ (1012), ပ (1015), ဝ (101D) take Tall AA (ါ).
        // Letters like က, စ, ဆ, ည, တ, ထ, ဓ, န, ဖ, ဗ, ဘ, မ, ယ, ရ, လ, သ, ဟ, အ and medial clusters like ပွား, ဖွား, သွား, ခွာ, ငှာ NEVER take Tall AA (e.g. ဓါတ် -> ဓာတ်, ဖေါ် -> ဖော်, ပွါး -> ပွား, ငှါ -> ငှာ).
        private static readonly Regex MisusedTallAaPattern = new Regex(
            @"(?<=(?:[\u1000\u1003\u1005-\u1011\u1014\u1016-\u101C\u101E-\u1021]|(?<!\u1012\u1039)\u1013|(?!\u1012\u103D|\u101D\u103E)[\u1000-\u1021][\u103B-\u103E]+)\u1031?)\u102B",
            RegexOptions.Compiled);

        // 4b. Round AA 'ာ' (U+102C) illegally attached to unstacked, un-medialed ခ, ဂ, င, ဒ, ပ, ဝ or 'ဒွ' (ဒွါရ/ဒွါဒသမ) where Tall AA 'ါ' (U+102B) is mandatory
        // (e.g. ဒော် -> ဒေါ်, ဒွာရ -> ဒွါရ, ပောင်း -> ပေါင်း, ခောင် -> ခေါင်, ဂန့်ဂော -> ဂန့်ဂေါ; exempts classical Pali 'ဂောတမ' and dropped-medial open 'ပော' -> 'ပြော')
        private static readonly Regex MisusedRoundAaPattern = new Regex(
            @"(?<=(?:(?<!\u1039)|\u1004\u103A\u1039)(?:[\u1001\u1002\u1004\u1012\u1015\u101D]|\u1012\u103D)\u1031?)(?!(?<=\u1015\u1031)\u102C(?![\u1000-\u1021]\u103A|\u103A))\u102C(?!တမ)",
            RegexOptions.Compiled);

        // 4c. Accidental Shift+F Virama '\u1039' typed instead of Asat '\u103A' before a consonant+Ya-pin cluster (e.g. 'လိုအပ္ချက်' -> 'လိုအပ်ချက်')
        private static readonly Regex AccidentalViramaBeforeYaPinPattern = new Regex(
            @"(?<!\u1004\u103A)\u1039(?=[\u1000-\u1007\u1009-\u1021]\u103B)",
            RegexOptions.Compiled);

        // 4d. Non-existent Burmese rhyme 'မှုံ / မှုံ့' (\u1019\u103E\u102F\u1036 — 0x in 462p သတ်ပုံကျမ်း) -> 'မှုန် / မှုန့်' (45x in 462p သတ်ပုံကျမ်း)
        private static readonly Regex ImpossibleHmounRhymePattern = new Regex(
            @"\u1019\u103E\u102F\u1036(?<Tone>[\u1037\u1038]?)",
            RegexOptions.Compiled);

        // 5. Misplaced Kinzi 'င်္' (U+1004 U+103A U+1039) typed/converted after its consonant (+ medial/vowel) instead of before it
        // Examples: သကေင်္တ -> သင်္ကေတ, မဂင်္လာ -> မင်္ဂလာ, သကင်္န်း -> သင်္ကန်း, အဂင်္လိပ် -> အင်္ဂလိပ်, သဘေင်္ာ -> သင်္ဘော, ခြသေင့်္ -> ခြင်္သေ့, စကြင်္ံ -> စင်္ကြံ, သချင်္ာ -> သင်္ချာ
        // Verified across all 88,951 lexicon entries: Kinzi NEVER follows long vowels/tones (ာ/ါ/ီ/ူ/ေ/ဲ/့/း) and ONLY stacks over က/ခ/ဂ/ဃ/ဘ/သ (plus လ solely in တနင်္လာ).
        private static readonly Regex MisplacedKinziPattern = new Regex(
            @"(?<Cluster>[\u1000-\u1008\u1010-\u1012\u1015-\u1019\u101C\u101E][\u103B-\u103E]*[\u102B-\u1032\u1037\u1038]*)\u1004\u103A\u1039(?:(?![\u1000-\u1003\u1018\u101E])(?!(?<=တန\u1004\u103A\u1039)\u101C)|(?<=[\u102B\u102C\u102E\u1030\u1031\u1032\u1037\u1038]\u1004\u103A\u1039))",
            RegexOptions.Compiled);

        // 6. Unstacked 'စ' + 'ျ' (U+1005 U+103B) or redundant 'ဈ' + 'ျ' (U+1008 U+103B) instead of 'ဈ' (U+1008), e.g. စျေး/ဈျေး -> ဈေး, အဘိဇ္ဈျာ -> အဘိဇ္ဈာ
        private static readonly Regex MisusedCyaPattern = new Regex(
            @"(?<!\u1039)\u1005\u103B|\u1008\u103B",
            RegexOptions.Compiled);

        // 7. 'သြော်' / 'ဩော်' misused instead of 'ဪ' (U+102A), or 'သြ' (U+101E U+103C without vowel) misused instead of 'ဩ' (U+1029)
        private static readonly Regex MisusedAwExclamPattern = new Regex(
            @"\u101E\u103C\u1031\u102C\u103A|\u1029\u1031\u102C\u103A",
            RegexOptions.Compiled);

        private static readonly Regex MisusedAwLetterPattern = new Regex(
            @"\u101E\u103C(?![\u102B-\u103E])|\u1029\u103C+",
            RegexOptions.Compiled);

        // 8. Decomposed 'ဥ' + 'ီ' (U+1025 U+102E) or redundant 'ဦ' + 'ီ' (U+1026 U+102E) instead of canonical 'ဦ' (U+1026), e.g. ဦး / ဦီး -> ဦး
        private static readonly Regex MisusedDecomposedUuPattern = new Regex(
            @"\u1025\u102E|\u1026[\u102D\u102E]+",
            RegexOptions.Compiled);

        // 9. Misplaced Medial + AA + Asat or Medial + Asat + AA (e.g. 'ယောကျာ်း' / 'ယောကျ်ား' -> 'ယောက်ျား', 'လကျ်ာ' -> 'လက်ျာ'),
        // and Asat + Ra-medial typed before next consonant ('လက်ြပန်' -> 'လက်ပြန်')
        private static readonly Regex MisplacedMedialAaAsatPattern = new Regex(
            @"(?<Base>[\u1000-\u1021])(?<Medial>[\u103B\u103C])(?:[\u102B\u102C]\u103A|\u103A[\u102B\u102C])(?<Tone>[\u1037\u1038]?)",
            RegexOptions.Compiled);

        private static readonly Regex MisplacedAsatBeforeRaPattern = new Regex(
            @"\u103A\u103C(?<NextCons>[\u1000-\u1021])",
            RegexOptions.Compiled);

        // 10. Reversed Medial Order causing dotted circles in Pyidaungsu (e.g. 'လှျို့' ှ+ျ -> 'လျှို့' ျ+ှ, 'ကွျန်' ွ+ျ -> 'ကျွန်')
        private static readonly Regex ReversedMedialClusterPattern = new Regex(
            @"\u103E[\u103B\u103C\u103D]|\u103D[\u103B\u103C]",
            RegexOptions.Compiled);

        // 11. Duplicate combining marks causing dotted circles (e.g. 'ပါတယ််', 'လုံးး', 'နှင့််')
        private static readonly Regex DuplicateCombiningMarkPattern = new Regex(
            @"([\u102B-\u1032\u1036-\u103E])\1+|\u103A\u1037\u103A|\u1037\u103A\u1037",
            RegexOptions.Compiled);

        // 12. Vowel typed after Coda+Asat (e.g. 'မန်ိးမ' / 'မိန်ိးမ' -> 'မိန်းမ'; exempts classical 'ကျွန်ုပ်' \u1014\u103A\u102F\u1015\u103A)
        private static readonly Regex VowelAfterAsatCodaPattern = new Regex(
            @"(?<Onset>[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032\u1036]*)(?<Coda>[\u1000-\u1021]\u103A)(?!\u102F\u1015\u103A)(?<Vowel>[\u102D-\u1030\u1032])(?<Tone>[\u1037\u1038]*)",
            RegexOptions.Compiled);

        // 12b. Impossible 'ိူ / ူိ' (\u102D\u1030 / \u1030\u102D — 0x in 88,684-word lexicon) typed instead of 'ို' (\u102D\u102F, e.g. နိူင်ငံ -> နိုင်ငံ, လူမျိူး -> လူမျိုး, အကျိူး -> အကျိုး, လှိူင်း -> လှိုင်း)
        private static readonly Regex ImpossibleIuVowelComboPattern = new Regex(
            @"\u102D+\u1030+|\u1030+\u102D+",
            RegexOptions.Compiled);

        // 12b-ii. Medial Wa/Ha (\u103D/\u103E) typed after upper/lower/ai vowel or anusvara causing dotted circles (e.g. 'လွဲှအပ် -> လွှဲအပ်', 'ညိှနှိုင်း -> ညှိနှိုင်း', 'သီးနံှ -> သီးနှံ', 'လံှု့ဆော် -> လှုံ့ဆော်', 'ရံွှ့ -> ရွှံ့')
        private static readonly Regex MedialAfterVowelPattern = new Regex(
            @"(?<Vowel>[\u102D-\u1030\u1032\u1036]+)(?<Medial>[\u103D\u103E]+)",
            RegexOptions.Compiled);

        // 12b-iii. Swapped lower/upper vowels rendering dotted circles in Pyidaungsu (e.g. 'ကြုိး' \u102F\u102D -> 'ကြိုး' \u102D\u102F)
        private static readonly Regex SwappedLowerUpperVowelPattern = new Regex(
            @"\u102F\u102D|\u102F\u102E|\u1030\u102E",
            RegexOptions.Compiled);

        // 12c. Dangling Virama '\u1039' not followed by a subscript consonant (e.g. 'ထောင်္' -> 'ထောင်')
        private static readonly Regex DanglingViramaPattern = new Regex(
            @"\u1039(?![\u1000-\u1021])",
            RegexOptions.Compiled);

        // 13. Invisible zero-width characters corrupting Myanmar words
        private static readonly Regex ZeroWidthCorruptionPattern = new Regex(
            @"[\u200B-\u200D\uFEFF]+",
            RegexOptions.Compiled);

        // 14. Unstacked 'သ္သ' (\u101E\u1039\u101E) or doubled 'ဿဿ' (\u103F{2,}) instead of single Great Sa 'ဿ' (\u103F)
        private static readonly Regex MisusedGreatSaPattern = new Regex(
            @"\u101E\u1039\u101E|\u103F{2,}",
            RegexOptions.Compiled);

        // 15. Illegal doubled aspirated Pali pat-sint stacks (e.g. *ဝထ္ထု -> ဝတ္ထု, *ဗုဓ္ဓ -> ဗုဒ္ဓ, *ဥက္ကဌ္ဌ/*ဥက္ကဌ္ဋ -> ဥက္ကဋ္ဌ) or *ည္စ/*ည္ဇ -> ဉ္စ/ဉ္ဇ
        private static readonly Regex IllegalAspiratedOrNyaStackPattern = new Regex(
            @"(?<!\u1004\u103A)(?:\u1001\u1039[\u1000\u1001]|\u1003\u1039[\u1002\u1003]|\u1006\u1039[\u1005\u1006]|\u1008\u1039[\u1007\u1008]|\u100C\u1039[\u100B\u100C]|\u100E\u1039[\u100D\u100E]|\u1011\u1039[\u1010\u1011]|\u1013\u1039[\u1012\u1013]|\u1016\u1039[\u1015\u1016]|\u1018\u1039[\u1017\u1018]|\u100A\u1039[\u1005-\u1008])",
            RegexOptions.Compiled);

        // Note: narrow 1-loop stacks 'ဒ္ဒါ' (သဒ္ဒါ), 'ဒ္ဓါ' (သဒ္ဓါ), 'ဂ္ဂါ/ဂ္ဂေါ' (မဂ္ဂါမဂ္ဂ, မဂ္ဂေါ), and official 'ဥပ္ပါတ/ဥပ္ပါတ်' (ဥပ္ပါတ်ပျံ, ဥပ္ပါတသန္တိစေတီတော်) take Tall AA 'ါ' (\u102B);
        // wide stacks like န္ဒာ, ပ္ပာ (အဓိပ္ပာယ်, သပ္ပာယ်, ပဋိစ္စသမုပ္ပာဒ်), က္ခာ take Round AA 'ာ' (\u102C).
        private static readonly Regex StackedSubscriptTallAaPattern = new Regex(
            @"(?<=(?:(?<!\u1004\u103A)\u1039[\u1001\u1004\u1015\u101D]|(?<!\u1004\u103A|\u1002)\u1039\u1002|(?<!\u1004\u103A|\u1012)\u1039\u1012)\u1031?)(?!(?<=\u1025\u1015\u1039\u1015)\u102B(?=\u1010))\u102B",
            RegexOptions.Compiled);

        private static readonly Regex MedialAfterEVowelPattern = new Regex(
            @"\u1031([\u103B-\u103E]+)",
            RegexOptions.Compiled);

        // 16. Vowel/Anusvara typed before Pali subscript stack causing dotted circles (e.g. 'ဒုကိ္ခတ' -> 'ဒုက္ခိတ', 'ပုဂိ္ဂုလ်' -> 'ပုဂ္ဂိုလ်', 'လတီ္တတွဒ်' -> 'လတ္တီတွဒ်')
        private static readonly Regex VowelBeforeViramaStackPattern = new Regex(
            @"(?<Vowel>[\u102B-\u1032\u1036]+)(?<Stack>(?:\u1039[\u1000-\u1021])+[\u103B-\u103E]*)",
            RegexOptions.Compiled);

        private const uint LookalikeZeroWidth = 1u << 0;
        private const uint LookalikeVirama1039 = 1u << 1;
        private const uint LookalikeGreatSaDup = 1u << 2;
        private const uint LookalikeHmoun = 1u << 3;
        private const uint LookalikeIuCombo = 1u << 4;
        private const uint LookalikeMedialAfterVowel = 1u << 5;
        private const uint LookalikeVowelAfterAsat = 1u << 6;
        private const uint LookalikeDuplicateMark = 1u << 7;
        private const uint LookalikeLaKaung = 1u << 8;
        private const uint LookalikeDigitStrayMark = 1u << 9;
        private const uint LookalikeMisplacedMedialAaAsat = 1u << 10;
        private const uint LookalikeDecomposedUu = 1u << 11;
        private const uint LookalikeMisusedCya = 1u << 12;
        private const uint LookalikeMisusedAw = 1u << 13;
        private const uint LookalikeDigitZero = 1u << 14;
        private const uint LookalikeMisusedVowelU = 1u << 15;
        private const uint LookalikeNya1009 = 1u << 16;
        private const uint LookalikeTallAa = 1u << 17;
        private const uint LookalikeRoundAa = 1u << 18;
        private const uint LookalikeReversedMedial = 1u << 19;
        private const uint LookalikeSwappedLowerUpperVowel = 1u << 20;
        private const uint LookalikeVowelBeforeStack = 1u << 21;

        /// <summary>
        /// Zero-allocation O(N) check returning false when <paramref name="s"/> is already in canonical Myanmar combining order.
        /// </summary>
        private static bool NeedsCanonicalReorder(string s)
        {
            return NeedsCanonicalReorder(s, 0, s.Length);
        }

        private static bool NeedsCanonicalReorder(string s, int start, int len)
        {
            if (len < 2)
                return false;

            int end = start + len;
            bool has1039 = false;
            bool has102B = false;
            char prev = s[start];
            if (prev == '\u1039') has1039 = true;
            else if (prev == '\u102B') has102B = true;

            for (int i = start + 1; i < end; i++)
            {
                char c = s[i];
                if (c == '\u1039') has1039 = true;
                else if (c == '\u102B') has102B = true;

                switch (prev)
                {
                    case '\u1025':
                        if (c == '\u102E') return true;
                        break;
                    case '\u102B':
                    case '\u102C':
                        if (c == '\u1039') return true;
                        break;
                    case '\u1031':
                        if ((c >= '\u103B' && c <= '\u103E') || c == '\u1039') return true;
                        break;
                    case '\u102D':
                    case '\u102E':
                    case '\u1032':
                        if (c == '\u103D' || c == '\u103E' || c == '\u1039') return true;
                        break;
                    case '\u102F':
                        if (c == '\u102D' || c == '\u102E' || c == '\u103D' || c == '\u103E' || c == '\u1039') return true;
                        break;
                    case '\u1030':
                        if (c == '\u102D' || c == '\u103D' || c == '\u103E' || c == '\u1039') return true;
                        break;
                    case '\u1036':
                        if ((c >= '\u102D' && c <= '\u1030') || c == '\u103D' || c == '\u103E' || c == '\u1039') return true;
                        break;
                    case '\u1037':
                        if (c == '\u102B' || c == '\u102C' || c == '\u102F' || c == '\u1036' || c == '\u103A') return true;
                        break;
                    case '\u103D':
                        if (c == '\u103B' || c == '\u103C') return true;
                        break;
                    case '\u103E':
                        if (c >= '\u103B' && c <= '\u103D') return true;
                        break;
                }

                prev = c;
            }

            return has1039 && has102B;
        }

        private static bool IsNarrowTallAaConsonant(char c)
        {
            return c == '\u1001' || c == '\u1002' || c == '\u1004' ||
                   c == '\u1012' || c == '\u1015' || c == '\u101D';
        }

        /// <summary>
        /// Single-pass O(N) bitmask scanner identifying which lookalike / combining-corruption Regexes could possibly match <paramref name="s"/>.
        /// Returns 0 in ~10ns with zero allocations for clean Myanmar words.
        /// </summary>
        private static uint ComputeLookalikeTriggerMask(string s)
        {
            return ComputeLookalikeTriggerMask(s, 0, s.Length);
        }

        private static uint ComputeLookalikeTriggerMask(string s, int start, int len)
        {
            int end = start + len;
            uint mask = 0;
            char prev = '\0';

            for (int i = start; i < end; i++)
            {
                char c = s[i];
                int rel = i - start;

                if ((c >= '\u200B' && c <= '\u200D') || c == '\uFEFF')
                {
                    mask |= LookalikeZeroWidth;
                }
                else if (c == '\u1039')
                {
                    mask |= LookalikeVirama1039;
                    if (rel > 0 && ((prev >= '\u102B' && prev <= '\u1032') || prev == '\u1036') &&
                        i + 1 < end && s[i + 1] >= '\u1000' && s[i + 1] <= '\u1021')
                    {
                        mask |= LookalikeVowelBeforeStack;
                    }
                }
                else if (c == '\u103F')
                {
                    if (prev == '\u103F') mask |= LookalikeGreatSaDup;
                }
                else if (c == '\u1040')
                {
                    mask |= LookalikeDigitZero;
                }
                else if (c == '\u1044' || c == '\u104E')
                {
                    mask |= LookalikeLaKaung;
                }
                else if (c == '\u1009')
                {
                    mask |= LookalikeNya1009;
                }
                else if (c == '\u102B')
                {
                    int baseIdx = prev == '\u1031' ? i - 2 : i - 1;
                    bool cleanNarrowCons = baseIdx >= start &&
                                           IsNarrowTallAaConsonant(s[baseIdx]) &&
                                           (baseIdx == start || s[baseIdx - 1] != '\u1039');
                    if (!cleanNarrowCons)
                    {
                        mask |= LookalikeTallAa;
                    }
                }
                else if (c == '\u102C')
                {
                    int baseIdx = prev == '\u1031' ? i - 2 : i - 1;
                    if (baseIdx >= start)
                    {
                        char b = s[baseIdx];
                        if (IsNarrowTallAaConsonant(b) || (b == '\u103D' && baseIdx >= start + 1 && s[baseIdx - 1] == '\u1012'))
                        {
                            mask |= LookalikeRoundAa;
                        }
                    }
                }

                if (rel > 0)
                {
                    if (c == prev && ((c >= '\u102B' && c <= '\u1032') || (c >= '\u1036' && c <= '\u103E')))
                    {
                        mask |= LookalikeDuplicateMark;
                    }
                    else if (rel >= 2 &&
                             ((c == '\u103A' && prev == '\u1037' && s[i - 2] == '\u103A') ||
                              (c == '\u1037' && prev == '\u103A' && s[i - 2] == '\u1037')))
                    {
                        mask |= LookalikeDuplicateMark;
                    }

                    if (c == '\u1036' && prev == '\u102F')
                    {
                        mask |= LookalikeHmoun;
                    }
                    else if ((c == '\u1030' && prev == '\u102D') || (c == '\u102D' && prev == '\u1030'))
                    {
                        mask |= LookalikeIuCombo;
                    }
                    else if ((c == '\u103D' || c == '\u103E') && ((prev >= '\u102D' && prev <= '\u1030') || prev == '\u1032' || prev == '\u1036'))
                    {
                        mask |= LookalikeMedialAfterVowel;
                    }
                    else if (prev == '\u103A' && ((c >= '\u102D' && c <= '\u1030') || c == '\u1032'))
                    {
                        mask |= LookalikeVowelAfterAsat;
                    }
                    else if (prev >= '\u1041' && prev <= '\u1049' && c >= '\u102B' && c <= '\u103E')
                    {
                        mask |= LookalikeDigitStrayMark;
                    }
                    else if ((((c == '\u103A' && (prev == '\u102B' || prev == '\u102C')) ||
                               ((c == '\u102B' || c == '\u102C') && prev == '\u103A')) &&
                              rel >= 2 && (s[i - 2] == '\u103B' || s[i - 2] == '\u103C')) ||
                             (c >= '\u1000' && c <= '\u1021' && prev == '\u103C' && rel >= 2 && s[i - 2] == '\u103A'))
                    {
                        mask |= LookalikeMisplacedMedialAaAsat;
                    }
                    else if ((prev == '\u1025' && c == '\u102E') || (prev == '\u1026' && (c == '\u102D' || c == '\u102E')))
                    {
                        mask |= LookalikeDecomposedUu;
                    }
                    else if ((prev == '\u1005' || prev == '\u1008') && c == '\u103B')
                    {
                        mask |= LookalikeMisusedCya;
                    }
                    else if (((prev == '\u101E' || prev == '\u1029') && c == '\u103C') || (prev == '\u1029' && c == '\u1031'))
                    {
                        mask |= LookalikeMisusedAw;
                    }
                    else if (prev == '\u1025' && (c == '\u103A' || c == '\u102C' || c == '\u1039' || c == '\u103E' || c == '\u1037'))
                    {
                        mask |= LookalikeMisusedVowelU;
                    }
                    else if ((prev == '\u103E' && (c == '\u103B' || c == '\u103C' || c == '\u103D')) ||
                             (prev == '\u103D' && (c == '\u103B' || c == '\u103C')))
                    {
                        mask |= LookalikeReversedMedial;
                    }
                    else if ((prev == '\u102F' && (c == '\u102D' || c == '\u102E')) || (prev == '\u1030' && c == '\u102E'))
                    {
                        mask |= LookalikeSwappedLowerUpperVowel;
                    }
                }

                prev = c;
            }

            if ((mask & LookalikeDuplicateMark) == 0 && NeedsCanonicalReorder(s, start, len))
            {
                mask |= LookalikeDuplicateMark;
            }

            return mask;
        }

        /// <summary>
        /// Normalizes Myanmar combining marks to canonical Unicode 5.1+ order while preserving length for common swaps.
        /// </summary>
        public static string NormalizeCanonicalOrder(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (!NeedsCanonicalReorder(text))
                return text;

            string result = text;

            // 0. Precomposed Vowel UU (U+1026): normalize U+1025 U+102E ('ဥ' + 'ီ') -> 'ဦ' (U+1026)
            if (result.IndexOf('\u1025') >= 0)
            {
                result = result.Replace("\u1025\u102E", "\u1026");
            }

            // 0a. Subscript stacks (\u1039 + C) must precede vowels/anusvara (e.g. 'ဒုကိ္ခတ' -> 'ဒုက္ခိတ', 'ပုဂိ္ဂုလ်' -> 'ပုဂ္ဂိုလ်')
            if (result.IndexOf('\u1039') >= 0)
            {
                result = VowelBeforeViramaStackPattern.Replace(result, "${Stack}${Vowel}");
            }

            // 0b. Medials must precede vowel E (U+1031) and upper/lower/ai vowels (U+102D..U+1030, U+1032)
            if (result.IndexOf('\u1031') >= 0)
            {
                result = MedialAfterEVowelPattern.Replace(result, "$1\u1031");
            }
            if (result.IndexOf('\u103D') >= 0 || result.IndexOf('\u103E') >= 0)
            {
                result = MedialAfterVowelPattern.Replace(result, "${Medial}${Vowel}");
            }

            // 1. Medial ordering: Ya/Ra (103B/103C) -> Wa (103D) -> Ha (103E)
            result = result.Replace("\u103E\u103D", "\u103D\u103E"); // ှ + ွ -> ွှ
            result = result.Replace("\u103D\u103B", "\u103B\u103D"); // ွ + ျ -> ျွ
            result = result.Replace("\u103D\u103C", "\u103C\u103D"); // ွ + ြ -> ြွ
            result = result.Replace("\u103E\u103B", "\u103B\u103E"); // ှ + ျ -> ျှ
            result = result.Replace("\u103E\u103C", "\u103C\u103E"); // ှ + ြ -> ြှ

            // 2. Upper vowel (102D/102E) before Lower vowel (102F/1030)
            result = result.Replace("\u102F\u102D", "\u102D\u102F"); // ု + ိ -> ို
            result = result.Replace("\u1030\u102D", "\u102D\u1030"); // ူ + ိ -> ိူ
            result = result.Replace("\u102F\u102E", "\u102E\u102F"); // ု + ီ -> ီု

            // 3. Upper/Lower vowel (102D..1030) before Anusvara (1036)
            result = result.Replace("\u1036\u102D", "\u102D\u1036"); // ံ + ိ -> ိံ
            result = result.Replace("\u1036\u102E", "\u102E\u1036"); // ံ + ီ -> ီံ
            result = result.Replace("\u1036\u102F", "\u102F\u1036"); // ံ + ု -> ုံ
            result = result.Replace("\u1036\u1030", "\u1030\u1036"); // ံ + ူ -> ူံ

            // 4. Vowel / Anusvara / Asat before Dot Below (1037)
            result = result.Replace("\u1037\u102C", "\u102C\u1037"); // ့ + ာ -> ာ့
            result = result.Replace("\u1037\u102B", "\u102B\u1037"); // ့ + ါ -> ါ့
            result = result.Replace("\u1037\u1036", "\u1036\u1037"); // ့ + ံ -> ံ့
            result = result.Replace("\u1037\u103A", "\u103A\u1037"); // ့ + ် -> ့်
            result = result.Replace("\u1037\u102F", "\u102F\u1037"); // ့ + ု -> ု့

            if (result.IndexOf('\u1039') >= 0 && result.IndexOf('\u102B') >= 0)
            {
                result = StackedSubscriptTallAaPattern.Replace(result, "\u102C");
            }

            return result;
        }

        /// <summary>
        /// Replaces lookalike characters (၀ -> ဝ, ၄င်း -> ၎င်း, ဥ်/ဥာ -> ဉ်/ဉာ, ဓါတ်/ဖေါ် -> ဓာတ်/ဖော်) in a token.
        /// </summary>
        public static string FixLookalikeCharacters(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            uint mask = ComputeLookalikeTriggerMask(token);
            if (mask == 0)
                return NormalizeCanonicalOrder(token);

            string fixedToken = token;
            if ((mask & LookalikeZeroWidth) != 0)
            {
                fixedToken = ZeroWidthCorruptionPattern.Replace(fixedToken, string.Empty);
                mask = ComputeLookalikeTriggerMask(fixedToken);
            }
            if ((mask & LookalikeVowelBeforeStack) != 0)
            {
                fixedToken = VowelBeforeViramaStackPattern.Replace(fixedToken, "${Stack}${Vowel}");
            }
            if ((mask & LookalikeVirama1039) != 0)
            {
                fixedToken = AccidentalViramaBeforeYaPinPattern.Replace(fixedToken, "\u103A");
                fixedToken = MisplacedKinziPattern.Replace(fixedToken, "\u1004\u103A\u1039${Cluster}");
                fixedToken = DanglingViramaPattern.Replace(fixedToken, string.Empty);
            }
            if ((mask & (LookalikeVirama1039 | LookalikeGreatSaDup)) != 0)
            {
                fixedToken = MisusedGreatSaPattern.Replace(fixedToken, "\u103F");
            }
            if ((mask & LookalikeVirama1039) != 0)
            {
                fixedToken = RepairIllegalAspiratedStacks(fixedToken);
            }
            if ((mask & LookalikeHmoun) != 0)
                fixedToken = ImpossibleHmounRhymePattern.Replace(fixedToken, "\u1019\u103E\u102F\u1014\u103A${Tone}");
            if ((mask & LookalikeIuCombo) != 0)
                fixedToken = ImpossibleIuVowelComboPattern.Replace(fixedToken, "\u102D\u102F");
            if ((mask & LookalikeMedialAfterVowel) != 0)
                fixedToken = MedialAfterVowelPattern.Replace(fixedToken, "${Medial}${Vowel}");
            if ((mask & LookalikeVowelAfterAsat) != 0)
                fixedToken = VowelAfterAsatCodaPattern.Replace(fixedToken, "${Onset}${Vowel}${Coda}${Tone}");

            fixedToken = fixedToken.Replace("\u103A\u1037\u103A", "\u103A\u1037").Replace("\u1037\u103A\u1037", "\u103A\u1037");
            fixedToken = DuplicateCombiningMarkPattern.Replace(fixedToken, "$1");

            if ((mask & LookalikeVirama1039) != 0)
                fixedToken = StackedSubscriptTallAaPattern.Replace(fixedToken, "\u102C");
            if ((mask & LookalikeLaKaung) != 0)
                fixedToken = MisusedLaKaungPattern.Replace(fixedToken, "\u104E\u1004\u103A\u1038");
            if ((mask & LookalikeDigitStrayMark) != 0)
                fixedToken = DigitWithStrayMarkPattern.Replace(fixedToken, "${Digit}");
            if ((mask & LookalikeMisplacedMedialAaAsat) != 0)
            {
                fixedToken = MisplacedMedialAaAsatPattern.Replace(fixedToken, "${Base}\u103A${Medial}\u102C${Tone}");
                fixedToken = MisplacedAsatBeforeRaPattern.Replace(fixedToken, "\u103A${NextCons}\u103C");
            }
            if ((mask & LookalikeDecomposedUu) != 0)
                fixedToken = MisusedDecomposedUuPattern.Replace(fixedToken, "\u1026");
            if ((mask & LookalikeMisusedCya) != 0)
                fixedToken = MisusedCyaPattern.Replace(fixedToken, "\u1008");
            if ((mask & LookalikeMisusedAw) != 0)
            {
                fixedToken = MisusedAwExclamPattern.Replace(fixedToken, "\u102A");
                fixedToken = MisusedAwLetterPattern.Replace(fixedToken, "\u1029");
            }
            if ((mask & LookalikeDigitZero) != 0)
                fixedToken = MisusedDigitZeroPattern.Replace(fixedToken, "\u101D");
            if ((mask & (LookalikeMisusedVowelU | LookalikeZeroWidth | LookalikeVirama1039)) != 0)
                fixedToken = MisusedVowelUPattern.Replace(fixedToken, "\u1009");
            if ((mask & LookalikeNya1009) != 0)
            {
                fixedToken = MisusedNyaIiAsUuPattern.Replace(fixedToken, "\u1026");
                fixedToken = MisusedNyaAsVowelUPattern.Replace(fixedToken, "\u1025");
            }

            // Re-check Tall/Round AA after consonant fixes (e.g. ၀ -> ဝ or ဉ -> ဥ or Kinzi shift)
            if (fixedToken.IndexOf('\u102B') >= 0)
                fixedToken = MisusedTallAaPattern.Replace(fixedToken, "\u102C");
            if (fixedToken.IndexOf('\u102C') >= 0)
                fixedToken = MisusedRoundAaPattern.Replace(fixedToken, "\u102B");

            fixedToken = NormalizeCanonicalOrder(fixedToken);
            fixedToken = DuplicateCombiningMarkPattern.Replace(fixedToken, "$1");
            return NormalizeCanonicalOrder(fixedToken);
        }

        /// <summary>
        /// Zero-allocation slice check: returns false in ~10ns without allocating a Substring whenever the slice [start..start+length) is clean.
        /// </summary>
        public static bool HasLookalikeErrorSlice(string text, int start, int length)
        {
            if (string.IsNullOrEmpty(text) || length <= 0 || start < 0 || start + length > text.Length)
                return false;

            if (ComputeLookalikeTriggerMask(text, start, length) == 0)
                return false;

            return HasLookalikeError(text.Substring(start, length), out _, out _);
        }

        public static bool HasLookalikeErrorSlice(string text, int start, int length, out string correctedToken, out string explanation)
        {
            if (string.IsNullOrEmpty(text) || length <= 0 || start < 0 || start + length > text.Length)
            {
                correctedToken = string.Empty;
                explanation = string.Empty;
                return false;
            }

            if (ComputeLookalikeTriggerMask(text, start, length) == 0)
            {
                correctedToken = string.Empty;
                explanation = string.Empty;
                return false;
            }

            return HasLookalikeError(text.Substring(start, length), out correctedToken, out explanation);
        }

        /// <summary>
        /// Checks whether a token contains misused Digit Zero '၀', '၄င်း', Vowel U 'ဥ' instead of Nya 'ဉ', 'စျ', 'သြ', 'ဦ', misplaced Kinzi 'င်္', or illegal Tall AA 'ါ'.
        /// </summary>
        public static bool HasLookalikeError(string token, out string correctedToken, out string explanation)
        {
            correctedToken = token;
            explanation = string.Empty;

            if (string.IsNullOrEmpty(token))
                return false;

            uint mask = ComputeLookalikeTriggerMask(token);
            if (mask == 0)
                return false;

            if ((mask & LookalikeLaKaung) != 0 && MisusedLaKaungPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ရုံးသုံးစံနှုန်းအရ '၄င်း / ၄်င်း / ၎င်းင်း' အစား ယူနီကုဒ် '၎င်း' (U+104E + င်း) ကို တိကျစွာ ရေးသားရမည်။";
                return true;
            }

            if ((mask & LookalikeDigitStrayMark) != 0 && DigitWithStrayMarkPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သင်္ချာဂဏန်းနောက်တွင် သရ/အသတ်သင်္ကေတ မှားယွင်းကပ်နေပါသည်။";
                return true;
            }

            if ((mask & LookalikeVowelBeforeStack) != 0 && VowelBeforeViramaStackPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သရနှင့် ပါဌ်ဆင့်ဗျည်း အစီအစဉ် လွဲမှားနေပါသည် — ယူနီကုဒ်စံနှုန်းအရ ပါဌ်ဆင့်ဗျည်း ('္' + ဗျည်း) ကို သရရှေ့မှ ဦးစွာ ရိုက်နှိပ်ရမည် (ဥပမာ - ဒုက္ခိတ၊ ပရိယတ္တိ၊ ပုဂ္ဂိုလ်)။";
                return true;
            }

            if ((mask & LookalikeVirama1039) != 0 && MisplacedKinziPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ကင်းစီး ('င်္') အစီအစဉ် လွဲမှားနေပါသည် — ယူနီကုဒ်စံနှုန်းအရ ကင်းစီးကို အောက်ခံဗျည်းရှေ့မှ ရိုက်နှိပ်ရမည် (ဥပမာ - မင်္ဂလာ၊ သင်္ကန်း၊ ခြင်္သေ့)။";
                return true;
            }

            if ((mask & LookalikeMisplacedMedialAaAsat) != 0 &&
                (MisplacedMedialAaAsatPattern.IsMatch(token) || MisplacedAsatBeforeRaPattern.IsMatch(token)))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "အသတ် ('်') နှင့် ယပင့်/ရရစ် အစီအစဉ် လွဲမှားနေပါသည် — ယူနီကုဒ်စံနှုန်းအရ ရှေ့ဗျည်းနောက်တွင် အသတ်ဦးစွာရိုက်ပြီးမှ နောက်ဗျည်းနှင့် ယပင့်/ရရစ် ရိုက်နှိပ်ရမည် (ဥပမာ - ယောက်ျား၊ လက်ျာ၊ လက်ပြန်)။";
                return true;
            }

            if ((mask & LookalikeMisusedCya) != 0 && MisusedCyaPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "စလုံး ယပင့် 'စျ' (သို့မဟုတ် 'ဈ' နောက် ယပင့်ပို 'ဈျ') အစား သတ်ပုံကျမ်းဝင် ဇမြင်းဆွဲဗျည်း 'ဈ' (U+1008) ဖြင့် ရေးသားရမည် (ဥပမာ - ဈေး၊ ဈာန်၊ အဘိဇ္ဈာ)။";
                return true;
            }

            if ((mask & LookalikeMisusedAw) != 0 && (MisusedAwExclamPattern.IsMatch(token) || MisusedAwLetterPattern.IsMatch(token)))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သကြီး ရရစ် 'သြ' အစား ယူနီကုဒ် သီးသန့်အက္ခရာ 'ဩ' (U+1029) သို့မဟုတ် 'ဪ' (U+102A) ဖြင့် ရေးသားရမည် (ဥပမာ - ဩဂုတ်၊ ဩဝါဒ)။";
                return true;
            }

            if ((mask & LookalikeDecomposedUu) != 0 && MisusedDecomposedUuPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "အက္ခရာ 'ဥ' + 'ီ' (သို့မဟုတ် 'ဦ' ပေါ်တွင် 'ီ' ထပ်ရိုက်ထားခြင်း) အစား တစ်လုံးတည်းသော 'ဦ' (U+1026) အက္ခရာဖြင့်သာ ရေးသားရမည် (ဥပမာ - ဦး၊ ဦးခေါင်း)။";
                return true;
            }

            if ((mask & LookalikeDigitZero) != 0 && MisusedDigitZeroPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သုညဂဏန်း '၀' (U+1040) အစား ဝလုံးဗျည်း 'ဝ' (U+101D) ကို ရိုက်နှိပ်ရမည်။";
                return true;
            }

            if ((mask & LookalikeMisusedVowelU) != 0 && MisusedVowelUPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "အက္ခရာ 'ဥ' (U+1025) အစား ဉကလေးဗျည်း 'ဉ' (U+1009) ကို ရိုက်နှိပ်ရမည် (ဥပမာ - စဉ်၊ ယာဉ်၊ ဉာဏ်)။";
                return true;
            }

            if ((mask & LookalikeNya1009) != 0 && (MisusedNyaIiAsUuPattern.IsMatch(token) || MisusedNyaAsVowelUPattern.IsMatch(token)))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ဉကလေးဗျည်း 'ဉ' (U+1009) အစား သရအက္ခရာ 'ဥ' (U+1025) သို့မဟုတ် 'ဦ' (U+1026) ကို ရိုက်နှိပ်ရမည် (ဥပမာ - ဥက္ကဋ္ဌ၊ ဥပဒေ၊ ဥယျာဉ်၊ ဦး)။";
                return true;
            }

            if ((mask & LookalikeTallAa) != 0 && (MisusedTallAaPattern.IsMatch(token) || StackedSubscriptTallAaPattern.IsMatch(token)))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ဤဗျည်းနောက်တွင် မောက်ချ 'ါ' (U+102B) မသုံးရဘဲ ရေးချ 'ာ' (U+102C) ဖြင့်သာ ရေးသားရမည်။";
                return true;
            }

            if ((mask & LookalikeRoundAa) != 0 && MisusedRoundAaPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ခ၊ ဂ၊ င၊ ဒ၊ ပ၊ ဝ ဗျည်းများနောက်တွင် ရေးချ 'ာ' (U+102C) မသုံးရဘဲ မောက်ချ 'ါ' (U+102B) ဖြင့်သာ ရေးသားရမည် (ဥပမာ - ဒေါ်၊ ပေါင်း၊ ခေါင်း၊ ပါ)။";
                return true;
            }

            if ((mask & LookalikeVirama1039) != 0 && AccidentalViramaBeforeYaPinPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "အသတ် ('်' U+103A) အစား ပါဌ်ဆင့်သင်္ကေတ ('္' U+1039) မှားယွင်းရိုက်နှိပ်မိနေပါသည် (ဥပမာ - လိုအပ်ချက်)။";
                return true;
            }

            if ((mask & LookalikeHmoun) != 0 && ImpossibleHmounRhymePattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းတွင် 'မှုံ / မှုံ့' ရေးထုံးမရှိပါ — နသတ်ဖြင့် 'မှုန် / မှုန့်' ဟု ရေးသားရမည်။";
                return true;
            }

            if ((mask & LookalikeIuCombo) != 0 && ImpossibleIuVowelComboPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "မြန်မာယူနီကုဒ်စံနှုန်းတွင် လုံးကြီးတင်နှင့် နှစ်ချောင်းငင်တွဲ ('ိူ') ရေးထုံးမရှိပါ — လုံးကြီးတင် တစ်ချောင်းငင် ('ို') ဖြင့်သာ ရေးသားရမည် (ဥပမာ - နိုင်ငံ၊ လူမျိုး၊ အကျိုး၊ လှိုင်း)။";
                return true;
            }

            if ((mask & LookalikeMedialAfterVowel) != 0 && MedialAfterVowelPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ဗျည်းတွဲ ('ွ / ှ') နှင့် သရ အစီအစဉ် လွဲမှားနေပါသည် — ယူနီကုဒ်စံနှုန်းအရ ဝဆွဲ/ဟထိုးကို သရရှေ့မှ ဦးစွာ ရိုက်နှိပ်ရမည် (ဥပမာ - လွှဲအပ်၊ ညှိနှိုင်း)။";
                return true;
            }

            if ((mask & LookalikeReversedMedial) != 0 && ReversedMedialClusterPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ဗျည်းတွဲ အစီအစဉ် လွဲမှားနေပါသည် — ယူနီကုဒ်စံနှုန်းအရ ယပင့်/ရရစ် (ျ/ြ) ကို ဝဆွဲ/ဟထိုး (ွ/ှ) ရှေ့မှ ရိုက်နှိပ်ရမည် (ဥပမာ - လျှို့၊ ကျွန်)။";
                return true;
            }

            if ((mask & LookalikeDuplicateMark) != 0 &&
                (DuplicateCombiningMarkPattern.IsMatch(token) || DuplicateCombiningMarkPattern.IsMatch(NormalizeCanonicalOrder(token))))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သရ သို့မဟုတ် အသတ်/သင်္ကေတ နှစ်ထပ်ရိုက်နှိပ်မိနေပါသည်။";
                return true;
            }

            if ((mask & LookalikeVowelAfterAsat) != 0 && VowelAfterAsatCodaPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သရနှင့် အသတ် အစီအစဉ် လွဲမှားနေပါသည် — သရကို အသတ်ဗျည်းရှေ့မှ ရိုက်နှိပ်ရမည်။";
                return true;
            }

            if ((mask & LookalikeSwappedLowerUpperVowel) != 0 && SwappedLowerUpperVowelPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သရ အစီအစဉ် လွဲမှားနေပါသည် — လုံးကြီးတင် ('ိ/ီ') ကို တစ်ချောင်းငင် ('ု/ူ') ရှေ့မှ ရိုက်နှိပ်ရမည် (ဥပမာ - ကြိုးပမ်း၊ မြို့)။";
                return true;
            }

            if ((mask & LookalikeVirama1039) != 0 && DanglingViramaPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "အောက်ခံဗျည်း မပါသော ပါဌ်ဆင့်သင်္ကေတ ('္') ပိုနေပါသည်။";
                return true;
            }

            if ((mask & LookalikeZeroWidth) != 0 && ZeroWidthCorruptionPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "စာလုံးအတွင်း မမြင်ရသော ခြားနားသင်္ကေတ (Zero-Width Character) ရောနှောနေပါသည်။";
                return true;
            }

            if ((mask & (LookalikeVirama1039 | LookalikeGreatSaDup)) != 0 && MisusedGreatSaPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "သကြီးနှစ်လုံးဆင့်ကို 'သ္သ' (သို့မဟုတ် 'ဿဿ' နှစ်ထပ်) အစား ယူနီကုဒ် 'ဿ' (U+103F) တစ်လုံးတည်းဖြင့် ရေးသားရမည်။";
                return true;
            }

            if ((mask & LookalikeVirama1039) != 0 && IllegalAspiratedOrNyaStackPattern.IsMatch(token))
            {
                correctedToken = FixLookalikeCharacters(token);
                explanation = "ပါဠိသက် ပါဌ်ဆင့်စည်းကမ်းအရ ဓနိတဗျည်းနှစ်လုံး ထပ်မဆင့်ရဘဲ ရှေ့မှ သိထိလဗျည်းဖြင့်သာ ဆင့်ရမည် (ဥပမာ - ဝတ္ထု၊ ဗုဒ္ဓ၊ ဥက္ကဋ္ဌ၊ ပဉ္စင်း)။";
                return true;
            }

            return false;
        }

        private static string RepairIllegalAspiratedStacks(string s)
        {
            if (s.IndexOf('\u1039') < 0)
                return s;

            return s
                .Replace("\u1001\u1039\u1001", "\u1000\u1039\u1001")
                .Replace("\u1001\u1039\u1000", "\u1000\u1039\u1001")
                .Replace("\u1003\u1039\u1003", "\u1002\u1039\u1003")
                .Replace("\u1003\u1039\u1002", "\u1002\u1039\u1003")
                .Replace("\u1006\u1039\u1006", "\u1005\u1039\u1006")
                .Replace("\u1006\u1039\u1005", "\u1005\u1039\u1006")
                .Replace("\u1008\u1039\u1008", "\u1007\u1039\u1008")
                .Replace("\u1008\u1039\u1007", "\u1007\u1039\u1008")
                .Replace("\u100C\u1039\u100C", "\u100B\u1039\u100C")
                .Replace("\u100C\u1039\u100B", "\u100B\u1039\u100C")
                .Replace("\u100E\u1039\u100E", "\u100D\u1039\u100E")
                .Replace("\u100E\u1039\u100D", "\u100D\u1039\u100E")
                .Replace("\u1011\u1039\u1011", "\u1010\u1039\u1011")
                .Replace("\u1011\u1039\u1010", "\u1010\u1039\u1011")
                .Replace("\u1013\u1039\u1013", "\u1012\u1039\u1013")
                .Replace("\u1013\u1039\u1012", "\u1012\u1039\u1013")
                .Replace("\u1016\u1039\u1016", "\u1015\u1039\u1016")
                .Replace("\u1016\u1039\u1015", "\u1015\u1039\u1016")
                .Replace("\u1018\u1039\u1018", "\u1017\u1039\u1018")
                .Replace("\u1018\u1039\u1017", "\u1017\u1039\u1018")
                .Replace("\u100A\u1039\u1005", "\u1009\u1039\u1005")
                .Replace("\u100A\u1039\u1006", "\u1009\u1039\u1006")
                .Replace("\u100A\u1039\u1007", "\u1009\u1039\u1007")
                .Replace("\u100A\u1039\u1008", "\u1009\u1039\u1008");
        }

        /// <summary>
        /// Detects whether the entire text is Zawgyi-One (no pure-Unicode Myanmar words mixed in).
        /// For mixed Unicode + Zawgyi paragraphs, use FindZawgyiSpans so valid Unicode words are never corrupted.
        /// </summary>
        public static bool IsZawgyiText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !ZawgyiPattern.IsMatch(text))
                return false;

            int zawgyiWords = 0;
            int pureUnicodeWords = 0;
            foreach (Match m in MyanmarWordTokenScanPattern.Matches(text))
            {
                string tok = m.Value;
                bool hasZg = ZawgyiPattern.IsMatch(tok);
                bool hasUni = DefiniteUnicodeWordMarkerPattern.IsMatch(tok);
                if (hasZg && !hasUni)
                    zawgyiWords++;
                else if (hasUni && !hasZg)
                    pureUnicodeWords++;
            }

            return zawgyiWords > 0 && pureUnicodeWords == 0;
        }

        /// <summary>
        /// Zero-allocation O(N) character pre-filter strictly superseting every branch of <see cref="ZawgyiPattern"/>
        /// and <see cref="DoubleConvertedUnicodeTokenPattern"/> so clean Pyidaungsu Unicode skips regex backtracking completely.
        /// </summary>
        private static bool MayContainZawgyiOrMojibake(string text, out bool maybeRawZg, out bool maybeDoubleConv)
        {
            maybeRawZg = false;
            maybeDoubleConv = false;
            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];
                if (c < '\u1000' || c > '\u109F')
                    continue;

                if (!maybeRawZg)
                {
                    if (c == '\u1033' || c == '\u1034' || c == '\u105A' || (c >= '\u1060' && c <= '\u1097'))
                    {
                        maybeRawZg = true;
                    }
                    else if (c == '\u1031')
                    {
                        if (i == 0)
                        {
                            maybeRawZg = true;
                        }
                        else
                        {
                            char prev = text[i - 1];
                            if (!((prev >= '\u1000' && prev <= '\u102A') || (prev >= '\u103B' && prev <= '\u103F')))
                                maybeRawZg = true;
                        }
                    }
                    else if (c == '\u103B')
                    {
                        if (i == 0 || text[i - 1] < '\u1000' || text[i - 1] > '\u103F')
                            maybeRawZg = true;
                    }
                    else if (c == '\u1039')
                    {
                        if (i + 1 >= len || text[i + 1] < '\u1000' || text[i + 1] >= '\u1021' ||
                            (i > 0 && text[i - 1] == '\u1004') ||
                            (i + 2 < len && text[i + 2] == '\u103A'))
                        {
                            maybeRawZg = true;
                        }
                    }
                    else if (c == '\u1019')
                    {
                        if (i + 3 < len && text[i + 1] == '\u103A' && text[i + 2] == '\u102C' && text[i + 3] == '\u1038')
                            maybeRawZg = true;
                    }
                    else if (c == '\u1025')
                    {
                        if (i + 3 < len && text[i + 1] == '\u1015' && text[i + 2] == '\u1031' && text[i + 3] == '\u1012')
                            maybeRawZg = true;
                    }
                }

                if (!maybeDoubleConv)
                {
                    if (c == '\u103B' && i >= 2)
                    {
                        char prev = text[i - 1];
                        if ((prev == '\u1004' || prev == '\u1005' || prev == '\u1009' ||
                             prev == '\u100A' || prev == '\u1010' || prev == '\u1014') &&
                            !(i + 1 < len && text[i + 1] >= '\u102B' && text[i + 1] <= '\u1036'))
                        {
                            maybeDoubleConv = true;
                        }
                    }
                    else if (c == '\u103E' && i + 1 < len && text[i + 1] == '\u103E')
                    {
                        maybeDoubleConv = true;
                    }
                }

                if (maybeRawZg && maybeDoubleConv)
                    return true;
            }

            return maybeRawZg || maybeDoubleConv;
        }

        /// <summary>
        /// Locates exact Zawgyi-One or hybrid Zawgyi-corrupted spans inside text without corrupting surrounding Unicode words.
        /// </summary>
        public static List<(int StartIndex, int Length, string OriginalText, string ConvertedUnicode)> FindZawgyiSpans(string text)
        {
            var spans = new List<(int StartIndex, int Length, string OriginalText, string ConvertedUnicode)>();
            if (string.IsNullOrWhiteSpace(text))
                return spans;

            if (!MayContainZawgyiOrMojibake(text, out bool maybeRawZg, out bool maybeDoubleConv))
                return spans;

            bool hasRawZawgyi = maybeRawZg && ZawgyiPattern.IsMatch(text);
            bool hasDoubleConverted = maybeDoubleConv && DoubleConvertedUnicodeTokenPattern.IsMatch(text);
            if (!hasRawZawgyi && !hasDoubleConverted)
                return spans;

            if (hasRawZawgyi && !hasDoubleConverted && IsZawgyiText(text))
            {
                spans.Add((0, text.Length, text, ConvertZawgyiToUnicode(text)));
                return spans;
            }

            // Mixed Unicode + Zawgyi / Double-Converted Mojibake paragraph: isolate corrupted tokens (coalescing adjacent corrupted words separated by spaces)
            MatchCollection matches = MyanmarWordTokenScanPattern.Matches(text);
            int i = 0;
            while (i < matches.Count)
            {
                Match cur = matches[i];
                string tok = cur.Value;

                bool curIsDoubleConv = hasDoubleConverted &&
                                       !Regex.IsMatch(tok, @"[\u1033\u1034\u1060-\u1097]") &&
                                       (DoubleConvertedUnicodeTokenPattern.IsMatch(tok) ||
                                        (DoubleConvertedCompanionTokenPattern.IsMatch(tok) &&
                                         i + 1 < matches.Count &&
                                         string.IsNullOrWhiteSpace(text.Substring(cur.Index + cur.Length, matches[i + 1].Index - (cur.Index + cur.Length))) &&
                                         DoubleConvertedUnicodeTokenPattern.IsMatch(matches[i + 1].Value)));
                if (curIsDoubleConv)
                {
                    int mStart = cur.Index;
                    int mEnd = cur.Index + cur.Length;
                    int k = i + 1;
                    while (k < matches.Count)
                    {
                        Match nxt = matches[k];
                        string between = text.Substring(mEnd, nxt.Index - mEnd);
                        if (!string.IsNullOrWhiteSpace(between))
                            break;

                        string nxtTok = nxt.Value;
                        if (Regex.IsMatch(nxtTok, @"[\u1033\u1034\u1060-\u1097]"))
                            break;

                        bool nxtMatches = DoubleConvertedUnicodeTokenPattern.IsMatch(nxtTok) ||
                                          DoubleConvertedCompanionTokenPattern.IsMatch(nxtTok) ||
                                          (nxtTok.IndexOf('\u103C') >= 0 && !DefiniteUnicodeWordMarkerPattern.IsMatch(nxtTok)) ||
                                          (nxtTok.IndexOf('\u103D') >= 0 && !DefiniteUnicodeWordMarkerPattern.IsMatch(nxtTok));
                        if (!nxtMatches)
                            break;

                        mEnd = nxt.Index + nxt.Length;
                        k++;
                    }

                    string rawMojibake = text.Substring(mStart, mEnd - mStart);
                    string repairedMojibake = RepairDoubleConvertedUnicodeSequence(rawMojibake);
                    if (repairedMojibake != rawMojibake)
                    {
                        spans.Add((mStart, mEnd - mStart, rawMojibake, repairedMojibake));
                    }
                    i = k;
                    continue;
                }

                if (!hasRawZawgyi || !ZawgyiPattern.IsMatch(tok))
                {
                    i++;
                    continue;
                }

                bool isHybridUnicodeToken = ((tok.IndexOf('\u105A') >= 0 || tok.IndexOf('\u1064') >= 0) ||
                                             (tok.IndexOf("\u1025\u1015\u1031\u1012", StringComparison.Ordinal) >= 0 && tok.IndexOf('\u1039') < 0)) &&
                                            DefiniteUnicodeWordMarkerPattern.IsMatch(tok) &&
                                            !Regex.IsMatch(tok, @"[\u1033\u1034\u1060-\u1063\u1065-\u1097]");
                if (isHybridUnicodeToken)
                {
                    string fixedHybrid = RepairHybridZawgyiGlyphsInUnicodeToken(tok);
                    if (fixedHybrid != tok)
                    {
                        spans.Add((cur.Index, cur.Length, tok, fixedHybrid));
                    }
                    i++;
                    continue;
                }

                // Pure Zawgyi token: coalesce consecutive pure-Zawgyi tokens separated only by spaces
                int spanStart = cur.Index;
                int spanEnd = cur.Index + cur.Length;
                int j = i + 1;
                while (j < matches.Count)
                {
                    Match nxt = matches[j];
                    string between = text.Substring(spanEnd, nxt.Index - spanEnd);
                    if (!string.IsNullOrWhiteSpace(between))
                        break;

                    string nxtTok = nxt.Value;
                    if (!ZawgyiPattern.IsMatch(nxtTok) && DefiniteUnicodeWordMarkerPattern.IsMatch(nxtTok))
                        break;
                    if (!ZawgyiPattern.IsMatch(nxtTok) && nxtTok.IndexOf('\u1039') < 0)
                        break;

                    spanEnd = nxt.Index + nxt.Length;
                    j++;
                }

                string rawSpan = text.Substring(spanStart, spanEnd - spanStart);
                string convSpan = ConvertZawgyiToUnicode(rawSpan);
                if (convSpan != rawSpan)
                {
                    spans.Add((spanStart, spanEnd - spanStart, rawSpan, convSpan));
                }
                i = j;
            }

            return spans;
        }

        private static string RepairHybridZawgyiGlyphsInUnicodeToken(string unicodeToken)
        {
            string s = unicodeToken;
            // e.g. သၚ်္ဂဟော (\u105A\u103A?\u1039) -> သင်္ဂဟော (\u1004\u103A\u1039)
            s = Regex.Replace(s, @"\u105A\u103A?\u1039", "\u1004\u103A\u1039");
            s = s.Replace("\u105A", "\u1004\u103A");
            // e.g. အဂုၤတ္တိုရ် (Base + \u1064) -> အင်္ဂုတ္တိုရ် (\u1004\u103A\u1039 + Base)
            s = Regex.Replace(s, @"(?<Base>[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032]?)\u1064", "\u1004\u103A\u1039${Base}");
            s = s.Replace("\u1064", "\u1004\u103A\u1039");
            // Visual-order 'ဥပေဒ' typed inside an otherwise Pyidaungsu Unicode compound (e.g. 'ပြည်နယ်ဥပေဒချုပ်')
            s = s.Replace("\u1025\u1015\u1031\u1012", "\u1025\u1015\u1012\u1031");
            return FixLookalikeCharacters(NormalizeCanonicalOrder(s));
        }

        private static string RepairDoubleConvertedUnicodeSequence(string mojibakeText)
        {
            string s = mojibakeText;
            // Inverse of Zg2Uni medial/asat shift (\u103A->\u103B, \u103B->\u103C, \u103C->\u103D, \u103D->\u103E)
            s = DoubleConvertedPatSintInversePattern.Replace(s, "$1\u103A\u1039");
            s = DoubleConvertedCodaYaPinInversePattern.Replace(s, "${Coda}\u103A");
            s = s.Replace("\u103C", "\u103B");
            s = s.Replace("\u103D", "\u103C");
            s = s.Replace("\u103E\u103E", "\u103D\u103E");
            s = DoubleConvertedNonSonorantHaHtoInversePattern.Replace(s, "\u103D");
            return FixLookalikeCharacters(NormalizeCanonicalOrder(s));
        }

        /// <summary>
        /// Converts Zawgyi-One character sequences into standard Pyidaungsu Myanmar Unicode 5.1+.
        /// </summary>
        public static string ConvertZawgyiToUnicode(string zawgyiText)
        {
            if (string.IsNullOrEmpty(zawgyiText))
                return string.Empty;

            string s = zawgyiText;

            // 1. Replace shifting medial/asat codepoints in STRICT REVERSE ORDER so they never cascade!
            s = s.Replace("\u103D", "\u103E"); // Zawgyi medial Ha -> U+103E
            s = s.Replace("\u103C", "\u103D"); // Zawgyi medial Wa -> U+103D
            s = s.Replace("\u103B", "\u103C"); // Zawgyi medial Ra -> U+103C
            s = s.Replace("\u103A", "\u103B"); // Zawgyi medial Ya -> U+103B
            s = s.Replace("\u1039", "\u103A"); // Zawgyi Asat -> U+103A

            // 2. Replace Zawgyi-specific vowel, tone, consonant, and ligature codepoints
            s = s.Replace("\u1033", "\u102F"); // lower u
            s = s.Replace("\u1034", "\u1030"); // lower uu
            s = s.Replace("\u105A", "\u102B\u103A"); // tall aa + asat
            s = s.Replace("\u106A", "\u1009"); // ဉ
            s = s.Replace("\u106B", "\u100A"); // ည
            s = s.Replace("\u1086", "\u103F"); // ဿ
            s = s.Replace("\u108F", "\u1014"); // န
            s = s.Replace("\u1090", "\u101B"); // ရ
            s = s.Replace("\u1091", "\u100F\u1039\u100D"); // ဏ္ဍ
            s = s.Replace("\u1092", "\u100B\u1039\u100C"); // ဋ္ဌ
            s = s.Replace("\u1097", "\u100B\u1039\u100B"); // ဋ္ဋ
            s = s.Replace("\u106E", "\u100D\u1039\u100D"); // ဍ္ဍ
            s = s.Replace("\u106F", "\u100E\u1039\u100D"); // ဎ္ဍ

            // 3. Zawgyi medial variants & compound diacritics
            s = s.Replace("\u107D", "\u103B");
            s = Regex.Replace(s, @"[\u107E-\u1084]", "\u103C");
            s = s.Replace("\u1087", "\u103E");
            s = s.Replace("\u1088", "\u103E\u102F");
            s = s.Replace("\u1089", "\u103E\u1030");
            s = s.Replace("\u108A", "\u103D\u103E");
            s = s.Replace("\u108E", "\u102D\u1036");
            s = s.Replace("\u1094", "\u1037");
            s = s.Replace("\u1095", "\u1037");

            // 4. Zawgyi Kinzi variants (typed after consonant in Zawgyi -> mark with sentinel \uFFFF then move before base consonant)
            s = s.Replace("\u1064", "\uFFFF");
            s = s.Replace("\u108B", "\uFFFF\u102D");
            s = s.Replace("\u108C", "\uFFFF\u102E");
            s = s.Replace("\u108D", "\uFFFF\u1036");

            // 5. Zawgyi stacked subscripts (\u1060..\u107C, \u1085, \u1096)
            s = s.Replace("\u1060", "\u1039\u1000");
            s = s.Replace("\u1061", "\u1039\u1001");
            s = s.Replace("\u1062", "\u1039\u1002");
            s = s.Replace("\u1063", "\u1039\u1003");
            s = s.Replace("\u1065", "\u1039\u1005");
            s = s.Replace("\u1066", "\u1039\u1006");
            s = s.Replace("\u1067", "\u1039\u1006");
            s = s.Replace("\u1068", "\u1039\u1007");
            s = s.Replace("\u1069", "\u1039\u1008");
            s = s.Replace("\u106C", "\u1039\u100B");
            s = s.Replace("\u106D", "\u1039\u100C");
            s = s.Replace("\u1070", "\u1039\u100F");
            s = s.Replace("\u1071", "\u1039\u1010");
            s = s.Replace("\u1072", "\u1039\u1010");
            s = s.Replace("\u1073", "\u1039\u1011");
            s = s.Replace("\u1074", "\u1039\u1011");
            s = s.Replace("\u1075", "\u1039\u1012");
            s = s.Replace("\u1076", "\u1039\u1013");
            s = s.Replace("\u1077", "\u1039\u1014");
            s = s.Replace("\u1078", "\u1039\u1015");
            s = s.Replace("\u1079", "\u1039\u1016");
            s = s.Replace("\u107A", "\u1039\u1017");
            s = s.Replace("\u107B", "\u1039\u1018");
            s = s.Replace("\u107C", "\u1039\u1019");
            s = s.Replace("\u1085", "\u1039\u101C");
            s = s.Replace("\u1096", "\u1039\u1010\u103D");

            // 6. Reorder visual-order E (\u1031) and medial Ra (\u103C) to follow Consonant + Subscript + Medials
            s = Regex.Replace(
                s,
                @"(?<E>\u1031+)?(?<Ra>\u103C+)?(?<Cons>[\u1000-\u1021])(?<Sub>\u1039[\u1000-\u1021])?(?<Med>[\u103B\u103D\u103E]*)",
                m =>
                {
                    if (!m.Groups["E"].Success && !m.Groups["Ra"].Success)
                        return m.Value;
                    string ePart = m.Groups["E"].Success ? "\u1031" : string.Empty;
                    string raPart = m.Groups["Ra"].Success ? "\u103C" : string.Empty;
                    return m.Groups["Cons"].Value + m.Groups["Sub"].Value + raPart + m.Groups["Med"].Value + ePart;
                });

            // 7. Move Kinzi sentinel (\uFFFF) before its base consonant cluster -> \u1004\u103A\u1039
            if (s.IndexOf('\uFFFF') >= 0)
            {
                s = Regex.Replace(s, @"(?<Base>[\u1000-\u1021][\u103B-\u103E]*\u1031?)\uFFFF", "\u1004\u103A\u1039${Base}");
                s = s.Replace("\uFFFF", "\u1004\u103A\u1039");
            }

            return FixLookalikeCharacters(NormalizeCanonicalOrder(s));
        }
    }
}
