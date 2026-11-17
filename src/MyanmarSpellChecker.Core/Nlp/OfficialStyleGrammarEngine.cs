using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Enforces "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" numeral rules ('တ' vs 'တစ်', 'ခုနစ်' vs 'ခုနှစ်'),
    /// clause-level particle grammar ('မ...ဘဲ', 'လဲ' vs 'လည်း'), everyday casual/keyboard typos ('ဒါပေမယ့်->ဒါပေမဲ့',
    /// 'တရ်->တယ်', 'ပီး->ပြီး', 'ဖစ်->ဖြစ်', '~အုန်း->~ဦး', 'မ...ဘူ->ဘူး'), and Formal/Official literary style.
    /// </summary>
    public sealed class OfficialStyleGrammarEngine
    {
        // 1a. Compound 'တ...တ...' numeral phrases where both 'တ' syllables require 'တစ်' per 462-page သတ်ပုံကျမ်း (တစ်စုံတစ်ရာ, တစ်ခါတစ်ရံ, တစ်ရံတစ်ခါ, တစ်စိတ်တစ်ပိုင်း, တစ်အုပ်တစ်မ, etc.)
        private static readonly Regex TaCompoundNumeralPattern = new Regex(
            @"(?<![\u1039])(?:တ(?<First>စုံ)တ(?<Second>ရာ|ခု|ဦး)|တ(?<First>ခါ)တ(?<Second>ရံ)|တ(?<First>ရံ)တ(?<Second>ဆစ်|ဖန်|ခါ)|တ(?<First>စိတ်)တ(?<Second>ပိုင်း)|တ(?<First>ပိုင်း)တ(?<Second>စ)|တ(?<First>ချိန်)တ(?<Second>ခါ)|တ(?<First>နည်း)တ(?<Second>ဖုံ|လမ်း)|တ(?<First>ဦး)တ(?<Second>ယောက်)|တ(?<First>အုပ်)တ(?<Second>မ))(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        // 1b. 'တ' + Numeral Classifier where 'တစ်' is required by မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (အမှာစာ စာ-၇..၈ နှင့် တစ်ဆက်တည်း/တစ်သမတ်တည်း)
        private static readonly Regex TaNumeralClassifierPattern = new Regex(
            @"(?<!ရုတ်|သမ္မ|ပဏ္ဍိ|ပဏ္ဏိ|ဇီဝိ|ပရဟိ|လောပိ|မုဒိ|သုခိ|တမ်း|ရွှေ(?=တချောင်း)|ကျောက်\s*(?=တလုံး)|ရာဇပ(?=တလမ်း)|မျှ|ဘူ|ဇာ|ဒူ|ဝါ|ဂီ|\u101E\u102F|အနန္|စနစ်|သာကေ|ဟင်္သာ|[\u1039])တ(?<Classifier>ခု|ဦး|ယောက်|ဖက်|ကြိမ်|ခါ|ဖန်|ရက်|လ(?!ဘောင်|ပေါင်|မိုင်း|ပိုင်း)|နှစ်|ပတ်|နေ့|ညလုံး|နေကုန်|နာရီ|မိနစ်|စက္ကန့်|အောင့်|စောင်|တွဲ|ဖွဲ့|စု(?!တစည်း)|ချက်(?!ချက်)|ချိန်|နေရာ|နိုင်ငံလုံး|ပြည်လုံး|သက်လုံး|လျှောက်လုံး|ခဏ|ဆင့်|ပိုင်း|မျိုး|စုံ|ထပ်|လမ်း|မြို့နယ်|ခရိုင်|တိုင်း|ပြိုင်နက်|ပြိုင်တည်း|ဆက်တည်း|သမတ်တည်း|ယောက်တည်း|ဦးချင်း|ဖက်သတ်|ကောင်|စီး|ချောင်း|လုံး|ပြား|ရွက်|အုပ်(?!ချုပ်|ထိန်း|စိုး)|ထည်|ပွဲ|ခန်း(?!မ)|ဆောင်(?!ရွက်)|သံတည်း|သဘောတည်း|ပါတည်း|ပေါင်းတည်း|ထောက်(?!ပံ့|ကူ|ခံ|လှမ်း)|လှည့်|ဝက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        // Hypercorrected 'တစ်' before non-numeral adverbs/pronouns where 'တ' must be used (အမှာစာ စာ-၈ အာဂုံပစ္စည်း 'တ'; note: တစ်ဆက်တည်း, တစ်သမတ်တည်း & တစ်သီးတစ်ခြား officially use 'တစ်' in the 462p book!)
        private static readonly Regex HypercorrectedTaitPattern = new Regex(
            @"(?<!တစ်သီး)တစ်(?<Redup>ကယ်|ဖြည်းဖြည်း|ဖြူးဖြူး|ကျက်ကျက်|ရိပ်ရိပ်|စောင်း|လွဲ|စိုက်မတ်မတ်|ညီတညွတ်တည်း|ချို့|ခြား|မင်|ဒင်္ဂ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
            RegexOptions.Compiled);

        // Hypercorrected infix/compound 'တစ်' on official Preface Page 8 'တ' compounds (အမှတ်တစ်ရ -> အမှတ်တရ, အတူတစ်ကွ -> အတူတကွ, တစ်ခမ်းတစ်နား -> တခမ်းတနား, အစဉ်တစ်စိုက် -> အစဉ်တစိုက်, တစ်ချို့တစ်လေ -> တချို့တလေ, etc.)
        private static readonly Regex HypercorrectedCompoundTaitPattern = new Regex(
            @"(?<Target>အမှတ်တစ်ရ|အတူတစ်ကွ|အလိုတစ်တူ|တစ်ခမ်းတစ်နား|တစ်အံ့တစ်ဩ|အရောတစ်ဝင်|မဟုတ်တစ်ရုတ်|မခို့တစ်ရို့|အစဉ်တစ်စိုက်|တစ်ချို့တစ်လေ)(?![\u102B-\u103E])",
            RegexOptions.Compiled);

        // Misplaced aukmyit on attributive interrogative/indefinite pronoun 'မည့်သည်' -> 'မည်သည့်' (e.g. 'မည့်သည် ပစ္စည်းနှင့်မဆို')
        private static readonly Regex MisplacedAukmyitMyiThiPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>မည့်သည်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        // Official Khwe-Htar Page 337 productive ideophone suffix rule: 'ကနဲ(ဝင်း...၊ ထိန်...) → ခနဲ(ဝင်း...၊ ထိန်...)'
        private static readonly Regex MisspelledIdeophoneKanaePattern = new Regex(
            @"(?<Stem>ဝင်း|ထိန်|လက်|ဖျတ်|ဗြုန်း|ရုတ်|မိုက်|အေး|ပူ|စိမ့်|ထိတ်|ဒိတ်|လှစ်|ရိပ်|ဖြတ်|ဆတ်|ငိုက်|ညွတ်|ပြတ်|ဝုန်း|ဒိုင်း|ဂျိမ်း|ဖြန်း|ဖြုတ်|ဖြုန်း|ဖုတ်|ဘောက်|မြိုက်|ခြိုက်|စွေ့|စွတ်|ပြိုက်|လွှတ်|ဝါး|ရွှတ်|ဟုပ်|ဟုန်း|ရှိန်း|ဖိန်း|ဒက်|ဆိုး)(?<Target>ကနဲ)(?![\u102B-\u103E])",
            RegexOptions.Compiled);

        // 2. 'ခုနစ်' (Number 7, no ha-hto) vs 'ခုနှစ်' (Year, with ha-hto; guard against year digits e.g. '၁၉၄၁ ခုနှစ် သန်းခေါင်စာရင်း')
        private static readonly Regex MisusedKhuNhitNumberPattern = new Regex(
            @"(?<![၀-၉0-9]\s*)(?<Target>ခုနှစ်)(?=(?:ယောက်|ဦး|ခု|ရက်|လ|ကြိမ်|ဖွဲ့|စောင်|ထပ်|မိုင်|နာရီ|မိနစ်|ရာ|ထောင်|သောင်း|သိန်း|သန်း(?!ခေါင်)|စီး|လုံး|ချက်|မျိုး|ဆင့်|ပိုင်း|နှစ်(?=အရွယ်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A))",
            RegexOptions.Compiled);

        private static readonly Regex MisusedKhuNitYearPattern = new Regex(
            @"(?:၁[၀-၉]{3}|၂၀[၀-၉]{2}|19[0-9]{2}|20[0-9]{2})\s*(?<Target>ခုနစ်)(?![\u102B-\u103E])",
            RegexOptions.Compiled);

        // 3. Generalized Negative Verb 'မ...' + 'ပဲ' -> 'ဘဲ' (works for ANY negative verb in Myanmar while excluding nouns like မနက်/မနေ့, ordinal Pali numerals ပထမ/ပဉ္စမ/သတ္တမ, and nominalized/simile '...တာပဲ' / '...သလောက်ပဲ' / '...သလိုပဲ')
        private static readonly Regex NegativeVerbPePattern = new Regex(
            @"(?:(?<![\u1000-\u103F])|(?<=[\u1000-\u103F]{2,12})(?<!ပထ|ပဉ္စ|ဆဋ္ဌ|သတ္တ|အဋ္ဌ|နဝ|ဒသ))(?<Ma>မ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])(?!မပဲ|ပဲ|ဟာ|နက်|နေ့|ကွေး|လေးရှား|ရမ်း)(?<VerbBody>[\u1000-\u103F]{1,10}?)(?<!တာ|မှာ|ရင်|လို့|ခင်|က|ကို|မှ|လောက်|ရုံ|စရာ|သလို|လို)(?<Target>ပဲ)(?=\s|$|[\u104A\u104B]|နှင့်|နဲ့)",
            RegexOptions.Compiled);

        // 4a. Clause-level Interrogative 'လည်း' -> 'လဲ' (require non-Myanmar left boundary, exclude reduplicated indefinites 'ဘာပြောပြော/မဆို' & 'ဘယ်ဘက်', forbid intervening 'လဲ/ဆိုတော့/ပေမဲ့', and anchor to question/clause end!)
        private static readonly Regex InterrogativeLaePattern = new Regex(
            @"(?<![\u1000-\u103F])(?:ဘာ(?!သာ|ပြောပြော|လုပ်လုပ်|ဖြစ်ဖြစ်|ဆိုဆို|မဆို)|ဘယ်(?!ဘက်|ညာ|လ်|ဂျီ|လာ|လီ|လိုပဲ|သူမဆို|အရာမဆို)|အဘယ်ကြောင့်|အဘယ်|မည်သို့|မည်သူ|မည်သည့်)(?![\u1000-\u103F\s]{0,18}?(?:မဆို|လဲ|ဆိုတော့|ပေမဲ့|ပေမယ့်|ပေမယ့်|သော်လည်း))[\u1000-\u103F\s]{0,26}?(?<Target>လည်း)(?=\s*(?:[\u104B?]|$|ဆိုတာ|ဆိုသည်|ဟု\s*မေး|လို့\s*မေး|မသိ))",
            RegexOptions.Compiled);

        // 4b. Affirmative 'လဲ' ("also/too") -> 'လည်း' (e.g. 'ကျွန်တော်လဲ သွားမယ်' -> 'ကျွန်တော်လည်း သွားမယ်', 'အနာဂတ်မှာလဲ' -> 'အနာဂတ်မှာလည်း')
        private static readonly Regex AffirmativeLaeMisusePattern = new Regex(
            @"(?:(?:ကျွန်တော်|ကျွန်မ|သူ|ငါ|မင်း|သင်|ဒါ|အဲဒါ|ထိုသူ|၎င်း|သား|သမီး|အားလုံး|တို့|များ|တွေ)|(?:အနာဂတ်|ပစ္စုပ္ပန်|အတိတ်|ဒီနေရာ|အိမ်|ကျောင်း|ရုံး|မြန်မာနိုင်ငံ)(?:မှာ|တွင်))\s*(?<Target>လဲ)(?=\s+[\u1000-\u102A])",
            RegexOptions.Compiled);

        // 4c. Universal Everyday Spelling & Casual Typo Corrections (apply in BOTH General & Formal modes)
        private static readonly Regex MisspelledDarPayMaePattern = new Regex(
            @"(?<Prefix>ဒါပေ|သို့ပေ)(?<Target>မယ့်)");

        private static readonly Regex MisspelledVerbOhnPattern = new Regex(
            @"(?<Prefix>ပါ|သွား|လာ|စား|သောက်|နေ|ထိုင်|ပြော|ပြ|ကြည့်|လုပ်|နား|အိပ်|စောင့်|ဖတ်|ရေး|မေး|ဖြေ|ဝယ်|ယူ|ပေး|လိုက်|ကြ|ညီ|ခေါ်|ထည့်|ကိုင်|ဖွင့်|ပိတ်|ပြန်|(?<!ခု|င)တင်|စမ်း|တွေး|ရှာ)(?<Target>အုန်း|အုံး)(?![\u102B-\u103E]|သီး|ပင်|ရည်|နို့|ဆီ|မှုတ်|ကျွက်|တား)");

        // Dropped 'တ်' coda on 'ဟုတ်-' before verbal/polite/interrogative suffixes (e.g. 'ဟုလား -> ဟုတ်လား', 'ဟုကဲ့ -> ဟုတ်ကဲ့', 'ဟုကဲ့ပါ -> ဟုတ်ကဲ့ပါ', 'ဟုတယ် -> ဟုတ်တယ်', 'ဟုပီ -> ဟုတ်ပြီ')
        // Guards Pali stems (ဗဟု, လဟု, ရာဟု) and literary clause-ending quotes (ပြီဟု, သည်ဟု, မည်ဟု, လော့ဟု, သနည်းဟု), while allowing trailing polite particles (ပါ, ရှင့်, ဗျာ, နော်, ခင်ဗျား, etc.)
        private static readonly Regex MisspelledHuInsteadOfHotePattern = new Regex(
            @"(?<!ဗ|လ|ရာ|ဂရု|သိန်းန|ပြီ|သည်|မည်|လော့|အံ့|သနည်း|ပါ|ဘူး|၏|မ)(?<Target>ဟု(?<Suffix>ပါသလား|ရဲ့လား|လို့လား|သလား|သလော|လား|ကဲ့|ကဲ|ပါတယ်|ပါသည်|တယ်|ပါပီ|ပါပြီ|ပီ|ပြီ|တာပေါ့|ပေါ့|ပါ့|နော်|ဗျာ|ဗျ|ရှင့်|ရှင်|ကွာ))(?=(?:ပါခင်ဗျား|ပါခင်ဗျာ|ပါရှင့်|ပါရှင်|ပါဗျာ|ပါဗျ|ပါနော်|ပါလား|ပါလေ|ပါ|ခင်ဗျား|ခင်ဗျာ|ရှင့်|ရှင်|ဗျာ|ဗျ|နော်|လေ|ပေါ့|ကွာ|ကွ|ဟဲ့|တဲ့|မလား)*(?:\s|$|[\u104A\u104B?]))");

        // Dropped 'တ်' coda on negated 'မဟုတ်-' / 'ဒါမှမဟုတ်' / 'သို့မဟုတ်' (e.g. 'မဟုဘူး -> မဟုတ်ဘူး', 'မဟုဘူးလေ -> မဟုတ်ဘူးလေ', 'မဟုလား -> မဟုတ်လား', 'မဟုဘဲ -> မဟုတ်ဘဲ', 'ဒါမှမဟု -> ဒါမှမဟုတ်')
        private static readonly Regex MisspelledNegatedMaHuPattern = new Regex(
            @"(?<Target>(?:(?<Conj>ဒါမှ|သို့တည်း|သို့)မဟု(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])(?<Suffix1>ပါဘူး|ပါဖူး|ဘူး|ဖူး|ဘဲ|ပဲ|လား|လာ|ပါက|ပါ|တာ|တဲ့|သေးဘူး|သေး|တော့ဘူး|တော့|နိုင်|ကြောင်း|လျှင်|ရင်|သော|သည့်|ချေ)?|(?<!တစ်မု)မဟု(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])(?<Suffix2>ပါဘူး|ပါဖူး|ဘူး|ဖူး|ဘဲ|ပဲ|လား|လာ|ပါက|ပါ|တာ|တဲ့|သေးဘူး|သေး|တော့ဘူး|တော့|နိုင်|ကြောင်း|လျှင်|ရင်|သော|သည့်|ချေ)))(?=(?:ပါခင်ဗျား|ပါခင်ဗျာ|ပါရှင့်|ပါရှင်|ပါဗျာ|ပါဗျ|ပါနော်|ပါလား|ပါလေ|ပါ|ခင်ဗျား|ခင်ဗျာ|ရှင့်|ရှင်|ဗျာ|ဗျ|နော်|လေ|ပေါ့|ကွာ|ကွ|ဟဲ့|တဲ့|မလား)*(?:\s|$|[\u104A\u104B?]))");

        private static readonly Regex CasualTalMalPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>တရ်|မရ်)(?=\s|$|[\u104A\u104B]|နော်|ဗျာ|ရှင်)");

        private const string CompoundNegationHeads =
            @"နေ|အဆင်|စိတ်|သဘော|နား|အချိန်|အသုံး|အရသာ|အဓိပ္ပာယ်|သတိ|အလုပ်|မျက်နှာ|တန်ဖိုး|အကျိုး";

        private const string NegativePhuAllowedPrecedingSyllables =
            @"ပါ|သေး|တော့|နေ|ကြ|နိုင်|ချင်|တတ်|သင့်|လောက်|ရဲ|ဝံ့|ရ|ခဲ့|ဖူး|လိုက်|မိ|သွား|လာ|" +
            @"ဟုတ်|ရှိ|သိ|အား|လပ်|ကောင်း|ဆိုး|လုပ်|ပြော|ဆို|မေး|ဖြေ|ပေး|ယူ|ဝယ်|ရောင်း|စား|သောက်|အိပ်|ထိုင်|ထ|" +
            @"ကြည့်|ထောင်|ဖတ်|ရေး|မှတ်မိ|မေ့|လည်|တူ|ကျ|ပြေ|မှန်|မှား|လွယ်|ခက်|လို|အပ်|ဆိုင်|ကြိုက်|ချစ်|မုန်း|ကြောက်|" +
            @"ပူ|အေး|နာ|ဖျား|မော|ပန်း|ပျင်း|ဝ|လှ|တော်|မြန်|နှေး|ကြာ|ဝေး|နီး|များ|နည်း|ကြီး|ငယ်|မြင့်|နိမ့်|" +
            @"သေချာ|ရေရာ|ပြည့်စုံ|လုံလောက်|ဖြစ်|ဝင်|ထွက်|တက်|ဆင်း|ပြန်|ကူညီ|စောင့်|မှီ|မီ|ထင်|ပြီး";

        private const string ClauseFinalNegationFollowLookahead =
            @"(?=\s*(?:[\u104A\u104B?""”'’]|$|နော်|ပေါ့|လား|ဗျာ|ဗျ|ရှင်|ရှင့်|ကွာ|ကွ|လေ|ဟဲ့|ဟု|လို့|ဆိုပြီး|တဲ့))";

        private static readonly Regex MissingVisargaBuPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])|(?<=(?:" + CompoundNegationHeads + @")))မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A]|နက်|နေ့|ဟာ|ကွေး|လေးရှား|ရမ်း)[\u1000-\u103F]{1,18}?(?<Target>ဘူ)" + ClauseFinalNegationFollowLookahead);

        private static readonly Regex RedundantMaAhNayPhuPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>မအားနေဖူး)" + ClauseFinalNegationFollowLookahead);

        private static readonly Regex MisspelledNegativePhuPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])|(?<=(?:" + CompoundNegationHeads + @")))မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A]|နက်|နေ့|ဟာ|ကွေး|လေးရှား|ရမ်း)[\u1000-\u103F]{1,18}?(?<=" + NegativePhuAllowedPrecedingSyllables + @")(?<Target>ဖူး)" + ClauseFinalNegationFollowLookahead);

        // 'ပြီးပြီးမလား' / 'ပြီးပြီမလား' / 'ပီးပီးမလား' / 'ပီးပီမလား' / 'ပြီးပီမလား' -> 'ပြီးပြီလား' (or 'ပြီးပြီ မဟုတ်လား')
        private static readonly Regex CompletiveMaLarPattern = new Regex(
            @"(?<Target>(?:ပြီး|ပီး)(?:ပြီး|ပြီ|ပီး|ပီ)မလား)(?=\s|$|[\u104A\u104B?])");

        // 'ပြီးပြီး' (excluding 'ပြီးပြီးရော', 'ပြီးပြီးချင်း', 'ပြီးပြီးသော်/လျှင်', '...ပြီး ပြီးဆုံး/ပြီးမြောက်/ပြီးစီး/ပြီးပြတ်'), 'ပီးပီး', 'ပီးပီ', 'ပြီးပီ', 'ပီးပြီ' -> 'ပြီးပြီ'
        private static readonly Regex DoubledOrMedialDroppedPeePyeePattern = new Regex(
            @"(?<Target>(?:ပြီးပြီး(?!ရော|ဆုံး|ချင်း|မြောက်|ပြတ်|စီး|သတ်|ပျောက်|သော်|လျှင်)|ပီးပီး|ပီးပီ|ပြီးပီ|ပီးပြီ))(?![\u102B-\u103E])");

        // 'ပါပီ' -> 'ပါပြီ'
        private static readonly Regex CasualPaPeePattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>ပါပီ)(?![\u102B-\u103E]|ယွန်)");

        // Clause-final '[Verb] + ပီ' -> 'ပြီ' (guards legitimate 'ပီ' loanwords & place/noun compounds like ကော်ပီ, ရူပီ, မတူပီ, ကပီ, ပီပီ, ပီလားကောင်, စကားပီ, ခရုသပီ)
        private static readonly Regex CasualClauseFinalPiPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<!ကော်|ဂေါ်|ရူ|မတူ|က|ပီ|တီ|ဂျီ|စီ|ဒီ|အီး|ဗီ|အေ|ဘီ|ကေ|အိုင်|ထရို|ဟစ်|စကား|အသံ|လေသံ|ခရုသ|ပိန်း|လိုင်လင်း|ဟမ်|ပရင်စီ|ဟိုမိုတို|အင်သာ)(?<Target>\u1015\u102E)(?=(?:လား(?!ကောင်)|နော်|ကွာ|ကွ|ဗျာ|ဗျ|ရှင်|ရှင့်|ပေါ့|လေ|ဟ)(?=\s|$|[\u104A\u104B?])|\s*(?:[\u104A\u104B]|$)|\s+(?:ဖြစ်|ဟု|လို့|ဆိုပြီး))");

        // Sequential 'ပြီး' misspelled as 'ပြီ' before linkers ('ပြီတော့/ပြီရင်/ပြီမှ') or serial verbs ('စားပြီ သွားမယ်', 'အလုပ်လုပ်ပြီ အိမ်ပြန်မယ်')
        private static readonly Regex MisusedSequentialPyeePattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])(?<Target>\u1015\u103C\u102E)(?=(?:တော့|ရင်|လျှင်|မှ|နောက်|သား|စီး|ဆုံး|မြောက်)(?![\u102B-\u103E]))|(?:အလုပ်လုပ်|စာဖတ်|ထမင်းစား|ရေချိုး|ဈေးဝယ်|လမ်းလျှောက်|အိပ်ရာထ|စား|သောက်|အိပ်|လုပ်|ဖတ်|ရေး|ဝယ်|ချက်|ဆေး|ချိုး)(?<Target2>\u1015\u103C\u102E)(?=\s+(?:အိမ်ပြန်|ကျောင်းသွား|ရုံးသွား|သွား|လာ|ထိုင်|အိပ်|ပြန်|နား|ဆက်)))");

        // Sentence-final inchoative 'ပြီ' misspelled as consecutive converb 'ပြီး' right before '။' ('ဆင်းခွင့်ရပြီး။ -> ပြီ။', 'စာမေးပွဲ အောင်ပြီး။ -> ပြီ။')
        private static readonly Regex MisusedSentenceFinalConverbPyeePattern = new Regex(
            @"(?<=(?:ဆင်းခွင့်ရ|ခွင့်ရ|အောင်မြင်|အောင်|ပြီးစီး|ရောက်ရှိ|ရောက်လာ|ပျောက်ကင်း|သက်သာ|ပြည့်စုံ|လုံလောက်|သိရှိ|နားလည်|သဘောပေါက်|အဆင်ပြေ))(?<Target>ပြီး)(?=\s*။)",
            RegexOptions.Compiled);

        // Missing visarga on clause-final question particle 'လာ' -> 'လား' after completive/negative/tag markers or standalone 'ဟုတ်လာ/မဟုတ်လာ'
        private static readonly Regex MissingVisargaQuestionLaPattern = new Regex(
            @"(?:(?<=ပြီးပြီ|ပါပြီ|ပြီ|ပါဘူး|ဘူး|ရဲ့|ဟုတ်သ|သိသ|ရှိသ|ကောင်းသ|မဟုတ်)|(?<=^ဟုတ်|[\s\u104A\u104B]ဟုတ်))(?<Target>လာ)(?=\s*(?:[\u104B?]|$))");

        private static readonly Regex CasualPeePattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])(?<!ရူ|ကလို|စ)(?<Target>ပီး)(?=\s|$|[\u104A\u104B]|တော့|ပြီ|ရင်|မှ|ပါ|လား|နောက်|သား)|(?<![\u1000-\u103F])(?<Target2>ပီး)(?=တော့|ရင်|နောက်|မှ|ပြီ))");

        private static readonly Regex CasualPhitPattern = new Regex(
            @"(?:(?<![\u1000-\u103F]|အ|မ)(?<Target>ဖစ်)(?=ပါ|သည်|တယ်|မည်|မယ်|နေ|ခဲ့|သွား|လာ|နိုင်|ကြောင်း|၍)|(?<=မ)(?<Target2>ဖစ်)(?=ဘူး|ဖူး|ဘူ|ပါ|သေး|တော့|နိုင်|ဘဲ))");

        private static readonly Regex UnspacedAttachedMarPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{2,24}?)(?<Target>မာ)(?=\s+(?:နေ|ရှိ|လုပ်|တက်|အိပ်|ထိုင်|စောင့်|တွေ့|ဆုံ|ကျင်းပ|ဖွင့်|ထား|ရောက်|စား|သောက်|သွား|လာ|ပြော|ဖတ်|ရေး|ဝယ်|ရောင်း|အလုပ်လုပ်|နေထိုင်))");

        private static readonly Regex UnspacedAttachedNaePattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{2,24}?)(?<Target>နဲ)(?=\s+(?:အတူ|ပတ်သက်|စပ်လျဉ်း|သွား|လာ|နေ|စား|သောက်|ပြော|ဆို|လုပ်|ဆောင်ရွက်|တွေ့|ဆုံ|ရေး|ဖတ်|တွဲ|တူ))");

        private static readonly Regex UnspacedAttachedTaePattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{1,24}?)(?<Target>တဲ)(?=\s+(?!(?:အိမ်|နန်း|စု|ခို|ကုပ်|ပုတ်))[\u1000-\u102A][\u1000-\u103F]{1,24}|\s*(?:အခါ|အချိန်|နေရာ|အလုပ်|စကား|ကိစ္စ|လူ|သူ|ပုံစံ|နည်းလမ်း|အကြောင်း|အချက်|စာအုပ်|စာတမ်း))");

        private static readonly Regex UnspacedAttachedYaePattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{3,24}?)(?<Target>ရဲ)(?=\s+(?!(?:တပ်ဖွဲ့|စခန်း|အရာရှိ|မှူး|ဘော်|မေ|ဝံ့|ရင့်|စွမ်း|တိုက်|ကင်း|သား|ချုပ်))[\u1000-\u102A][\u1000-\u103F]{1,20})");

        // Non-existent 'လျှက်' (with illegal ha-hto) -> always 'လျက်' in မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း
        private static readonly Regex MisspelledLyetPattern = new Regex(
            @"(?<Target>\u101C\u103B\u103E\u1000\u103A)");

        // 4d. Zero-width characters (\u200B-\u200D, \uFEFF) or dotted-circle (\u25CC) corrupting a Myanmar word
        private static readonly Regex ZeroWidthInsideWordPattern = new Regex(
            @"(?<Target>[\u1000-\u103F]+(?:[\u200B-\u200D\uFEFF\u25CC]+[\u1000-\u103F]+)+)");

        private static readonly Regex StripZeroWidthRegex = new Regex(
            @"[\u200B-\u200D\uFEFF\u25CC]+");

        // Stray vowel typed after killed coda consonant inside a word (e.g. 'မန်ိးမ' / 'မိန်ိးမ' -> 'မိန်းမ'; exempts 'ကျွန်ုပ်')
        private static readonly Regex StrayVowelAfterCodaWordPattern = new Regex(
            @"(?<Target>[\u1000-\u103F]*[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032\u1036]*[\u1000-\u1021]\u103A(?!\u102F\u1015\u103A)[\u102D-\u1032][\u1037\u1038]*[\u1000-\u103F]*)");

        // Accidental Virama '\u1039' typed after killed coda before a grammatical particle (e.g. 'နားထောင်္ပါ' -> 'ထောင်')
        private static readonly Regex AccidentalViramaAfterCodaPattern = new Regex(
            @"(?<Target>[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032]+\u1004\u103A\u1039)(?=(?:ပါ|သည်|တယ်|မည်|မယ်|ပြီး|ပြီ|ကို|မှ|သို့|၍))");

        // 4e. Doubled sentence-ending particles (e.g. 'တင်ပြပါသည်သည်။' -> 'တင်ပြပါသည်။', 'သွားတယ်တယ်။' -> 'သွားတယ်။';
        // exempts reduplicated 'ဟိုဟိုသည်သည်'/'ထိုထိုသည်သည်', 'စသည်' ("etc.") + subject marker 'သည်' ('သဒ္ဓါစသည်သည်'), agentive noun '-သည်' + subject marker 'သည်' like 'အိုးသည်သည်', and lexical verb 'ပါ-' + polite 'ပါ')
        private static readonly Regex DoubledSentenceFinalParticlePattern = new Regex(
            @"(?<Target>(?<!လိုလို)မည်မည်|(?<!(?:ဟိုဟို|ထိုထို|စ|ဈေး|အိုး|ကုန်|ဧည့်|လူနာ|စစ်|တရား|သတို့သား|သတို့သမီး|အမှု|အငြိမ့်|အနှိပ်|ကချေ|ဇာတ်|လက်မှု|မုန့်|ပန်း|ပန်းထိမ်|ပန်းပဲ|လယ်|ယာ|သားသတ်|တံငါ|သတင်းစာ|စာအုပ်|ဖိနပ်|ထီး|နွားနို့|ရေ|သီတင်း|ဒုက္ခ|ခရီး|အိမ်|လက်|ဖောက်|အနာ))သည်သည်|တယ်တယ်|မယ်မယ်|(?<!(?:^|[\s\u104A\u104B]|အရေး|လိုက်|စိတ်|စရာ|တာ|လည်း|က|ကို|မှာ|ထဲ|နှင့်|နဲ့|သာ|ပဲ))(?:ပါပါသည်|ပါပါတယ်|ပါပါမည်|ပါပါမယ်))(?=\s|$|[\u104A\u104B])");

        // Conflicting Past + Future markers on non-motion/non-transfer verbs (exempts valid Burmese venitive/spatial '-ခဲ့-' on motion/placement verbs like 'လာခဲ့မယ်', 'သွားခဲ့မယ်', 'ယူခဲ့မယ်', 'ထားခဲ့မယ်')
        private static readonly Regex ConflictingPastFutureMarkerPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<!လာ|ပြန်|သွား|ယူ|ဝယ်|ခေါ်|ပို့|ထား|ပေး|ချန်|စောင့်|နေ|ဝင်|ထွက်|တက်|ဆင်း|ကူး|လိုက်)(?<Target>ခဲ့(?:ပါ)?(?:မည်|မယ်))(?=\s|$|[\u104A\u104B]|ဟု|လို့)");

        // 4f. Temporal Adverb <-> Clause/Sentence-Final Tense Agreement (excludes narrative 'နောက်ရက်/နောက်နေ့' which means 'the following day [in the past]' in stories!)
        private static readonly Regex FutureAdverbWithPastVerbPattern = new Regex(
            @"(?:မနက်ဖြန်|သန်ဖြန်|မကြာမီ|မကြာခင်|လာမ\s*(?:ည့်|ည့်|ယ့်|ယ့်)\s*(?:နှစ်|လ|အပတ်|ရက်)|နောက်(?:အပတ်|လ|နှစ်)(?:တွင်|မှာ|၌)?)[^\u104A\u104B\r\n]{1,64}?(?<!ရင်|လျှင်)(?<Target>ခဲ့ကြပါသည်|ခဲ့ကြသည်|ခဲ့ကြတယ်|ခဲ့ပါသည်|ခဲ့ပါတယ်|ခဲ့သည်|ခဲ့တယ်)(?=\s*(?:[\u104B]|$|ဟု|လို့|ဆိုတာ|ဆိုပြီး))");

        private static readonly Regex PastAdverbWithFutureVerbPattern = new Regex(
            @"(?:မနေ့က|ယမန်နေ့က|ယမန်နှစ်က|တမြန်နေ့က|ယနေ့နံနက်က|(?:ပြီးခဲ့|လွန်ခဲ့)(?:သော|သည့်|တဲ့)\s*[\u1000-\u103F]{1,14}(?:က|တွင်|၌|မှာ)?)[^\u104A\u104B\r\n]{1,64}?(?<Target>မည်ဖြစ်ကြောင်း|မည်ဖြစ်သည်|ပါမည်|ပါမယ်|လိမ့်မည်|လိမ့်မယ်|(?<!ရ|နိုင်|လို|ချင်)မည်|(?<!ရ|နိုင်|လို|ချင်)မယ်)(?=\s*(?:[\u104B]|$|ဟု|လို့|ဆိုတာ|ဆိုပြီး|ထုတ်ပြန်))");

        private static readonly Regex HistoricalEraWithSpeculativeFuturePattern = new Regex(
            @"(?:ပုဂံခေတ်|အင်းဝခေတ်|ကုန်းဘောင်ခေတ်|ပျူခေတ်|ရှေးခေတ်|[\u1000-\u103F\s]{2,16}?မင်းကြီး\s*လက်ထက်)(?:ဟာ|သည်|မှာ|တွင်|၌|က)?[^\u104A\u104B\r\n]{1,64}?(?<Target>ဖြစ်ပါလိမ့်မယ်|ဖြစ်ပါလိမ့်မည်|ဖြစ်လိမ့်မယ်|ဖြစ်လိမ့်မည်|ပါလိမ့်မယ်|ပါလိမ့်မည်|လိမ့်မယ်|လိမ့်မည်)(?=\s*[\u104B])",
            RegexOptions.Compiled);

        private static readonly Regex ImpossibleAspectStackPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>ခဲ့နေပြီ)(?=[\s\u104A\u104B]|$)",
            RegexOptions.Compiled);

        private static readonly Regex GenericGnomicHabitualPastPattern = new Regex(
            @"ဆိုတာ\s+[^\u104A\u104B\r\n]{1,36}?(?<Target>တတ်ခဲ့တယ်)(?=\s*(?:[\u104B]|$))",
            RegexOptions.Compiled);

        private static readonly Regex PresentOngoingWithPastVerbPattern = new Regex(
            @"(?:လက်ရှိတွင်|ယခုလက်ရှိတွင်|လောလောဆယ်တွင်)[^\u104A\u104B\r\n]{1,64}?(?:စုံစမ်းစစ်ဆေး|ဆွေးနွေးညှိနှိုင်း|ညှိနှိုင်းဆောင်ရွက်|စစ်ဆေးဆောင်ရွက်)(?<Target>ခဲ့ပါသည်|ခဲ့ပါတယ်|ခဲ့သည်|ခဲ့တယ်)(?=\s*(?:[\u104B]|$|ဟု|လို့|ဆိုတာ|ဆိုပြီး))");

        private static readonly Regex FutureExemptionSubclauseRegex = new Regex(
            @"(?:လျှင်|(?<!အစီ)ရင်(?![\u1000-\u103F]*ခံ)|သော်လည်း|ပေမဲ့|ပေမယ့်|ဟု(?!တ်)|လို့|ကြောင်း|မနေ့က|ယမန်နေ့က|ပြီးခဲ့|လွန်ခဲ့)");

        private static readonly Regex PastExemptionSubclauseRegex = new Regex(
            @"(?:လျှင်|(?<!အစီ)ရင်(?![\u1000-\u103F]*ခံ)|သော်လည်း|ပေမဲ့|ပေမယ့်|ဟု(?!တ်)|လို့|ကြောင်း|ယခု|အခု|မနက်ဖြန်|သန်ဖြန်|ဆက်လက်|ထပ်မံ)");

        // 4g. Numeral Classifier Agreement ('Noun + Numeral + WrongClassifier' — audited against မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း & မြန်မာသဒ္ဒါ)
        private const string MyanmarNumeralCore = @"(?:တစ်|နှစ်|သုံး|လေး|ငါး|ခြောက်|ခုနစ်|ရှစ်|ကိုး|တစ်ဆယ်|ဆယ်|[၁-၉][၀-၉]*)";

        private static readonly (string[] Triggers, Regex Pattern, string CorrectClassifier, string CategoryLabel)[] ClassifierAgreementRules =
        {
            // Footwear / paired items -> ရံ (typo-resilient across head-noun stop-coda slips like 'ဖိနဘ် နှစ်ယောက်')
            (
                new[] { "ဖိနပ်", "ဖိနဘ်", "ဖိနတ်", "ဖိနက်", "ခြေအိတ်", "လက်အိတ်", "နားကပ်" },
                new Regex(@"(?:ဖိန[ပဘဗတက]်|ခြေအိတ်|လက်အိတ်|နားကပ်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ခု|လုံး|အုပ်|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ရံ", "အစုံလိုက်ဝတ်ဆင်ရသော ပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ရံ')"
            ),
            // Land vehicles -> စီး (guards 'ဇာတ်ကား / ပန်းချီကား / စကား' so only actual vehicles match!)
            (
                new[] { "မော်တော်ယာဉ်", "ကား", "ဆိုင်ကယ်", "စက်ဘီး", "ရထား", "တက္ကစီ" },
                new Regex(@"(?:မော်တော်ယာဉ်|မော်တော်ကား|ဘတ်စ်ကား|အိမ်စီးကား|ကုန်တင်ကား|မီးသတ်ကား|လူနာတင်ကား|(?<![\u1000-\u103F])ကား|ဆိုင်ကယ်|စက်ဘီး|ရထား|တက္ကစီ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|ခု|အုပ်|လုံး|ထည်|ရံ|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "စီး", "ယာဉ်ပြ မျိုးပြပစ္စည်း ('~စီး')"
            ),
            // Watercraft & aircraft -> စင်း
            (
                new[] { "သင်္ဘော", "ယာဉ်", "လှေ" },
                new Regex(@"(?:သင်္ဘော|ရေယာဉ်|လေယာဉ်|လေယာဉ်ပျံ|ရဟတ်ယာဉ်|လှေ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|ခု|လုံး|ထည်|အုပ်|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "စင်း", "ရေယာဉ်/လေယာဉ်ပြ မျိုးပြပစ္စည်း ('~စင်း')"
            ),
            // Books & bound publications -> အုပ်
            (
                new[] { "စာအုပ်", "မဂ္ဂဇင်း" },
                new Regex(@"(?:စာအုပ်|ဝတ္ထုစာအုပ်|အဘိဓာန်စာအုပ်|မဂ္ဂဇင်း|မှတ်စုစာအုပ်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|လုံး|ထည်|ရံ|စင်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "အုပ်", "စာအုပ်ပြ မျိုးပြပစ္စည်း ('~အုပ်')"
            ),
            // Letters, reports, contracts & applications -> စောင်
            (
                new[] { "အစီရင်ခံစာ", "လျှောက်လွှာ", "စာချုပ်", "မေတ္တာရပ်ခံစာ", "တိုင်ကြားစာ" },
                new Regex(@"(?:အစီရင်ခံစာ|လျှောက်လွှာ|စာချုပ်|မေတ္တာရပ်ခံစာ|တိုင်ကြားစာ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|ရံ|လုံး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "စောင်", "စာရွက်စာတမ်း/စာချုပ်ပြ မျိုးပြပစ္စည်း ('~စောင်')"
            ),
            // Long slender writing/cutting tools -> ချောင်း (official Thatpon 'ချောင်း', never 'ခြောင်း')
            (
                new[] { "ဘောပင်", "ခဲတံ", "ကလောင်တံ", "စုတ်တံ" },
                new Regex(@"(?:ဘောပင်|ခဲတံ|ကလောင်တံ|စုတ်တံ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ|ခြောင်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ချောင်း", "အချောင်းလိုက်ရှိသော ပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ချောင်း')"
            ),
            // Poems, songs, articles -> ပုဒ်
            (
                new[] { "ကဗျာ", "သီချင်း", "ဆောင်းပါး" },
                new Regex(@"(?:ကဗျာ|သီချင်း|ဆောင်းပါး)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|လုံး|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ပုဒ်", "စာပေ/ဂီတအဖွဲ့အနွဲ့ပြ မျိုးပြပစ္စည်း ('~ပုဒ်')"
            ),
            // Monasteries & halls -> ဆောင် (audited: never 'အောင်')
            (
                new[] { "ဘုန်းကြီးကျောင်း", "ကျောင်းဆောင်", "ဇရပ်" },
                new Regex(@"(?:ဘုန်းကြီးကျောင်း|ကျောင်းဆောင်|ဇရပ်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ|အောင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ဆောင်", "အဆောင်အအုံပြ မျိုးပြပစ္စည်း ('~ဆောင်')"
            ),
            // Electronic devices / round appliances -> လုံး
            (
                new[] { "ကွန်ပျူတာ", "လက်ပ်တော့", "ဆာဗာ", "ရူတာ", "ဖုန်း", "တီဗီ", "ဘောလုံး" },
                new Regex(@"(?:ကွန်ပျူတာ|လက်ပ်တော့|ဆာဗာ|ရူတာ|တယ်လီဖုန်း|ဖုန်း|တီဗီ|ဘောလုံး)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|ရံ|အုပ်|စောင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "လုံး", "စက်ပစ္စည်း/အလုံးပြ မျိုးပြပစ္စည်း ('~လုံး')"
            ),
            // Animals -> ကောင် (excludes ambiguous 'ကျား' which means 'Male [human gender]' in census/police reports like 'တရားခံ ကျား ၅၉ ဦး'!)
            (
                new[] { "ခွေး", "ကြောင်", "နွား", "ကျား", "ခြင်္သေ့", "ငှက်", "ကြက်", "ဝက်", "ဆိတ်", "သိုး", "ယုန်" },
                new Regex(@"(?:ခွေး|ကြောင်|နွား|ကျားသစ်|တောကျား|ခြင်္သေ့|ငှက်|ကြက်|ဝက်|ဆိတ်|သိုး|ယုန်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ပါး|စီး|လုံး|ထည်|အုပ်|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ကောင်", "သတ္တဝါ/တိရစ္ဆာန်ပြ မျိုးပြပစ္စည်း ('~ကောင်')"
            ),
            // Riding animals (ဆင် / မြင်း) -> ကောင် (or 'စီး' when counted as mounts/royal steeds e.g. 'ဆင်ကိုးစီး', 'မြင်းတစ်စီး')
            (
                new[] { "မြင်း", "ဆင်" },
                new Regex(@"(?:မြင်း|ဆင်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ပါး|လုံး|ထည်|အုပ်|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ကောင်", "သတ္တဝါ/စီးတော်ယာဉ်ပြ မျိုးပြပစ္စည်း ('~ကောင်' သို့မဟုတ် '~စီး')"
            ),
            // Monks / clergy -> ပါး
            (
                new[] { "ရဟန်း", "သံဃာ", "ဆရာတော်", "ဘုန်း", "ရှင်သာမဏေ", "ကိုရင်", "သီလရှင်", "ပဉ္စင်း" },
                new Regex(@"(?:ရဟန်း|သံဃာ|သံဃာတော်|ဆရာတော်|ဘုန်းကြီး|ဘုန်းတော်ကြီး|ရှင်သာမဏေ|ကိုရင်|သီလရှင်|ပဉ္စင်း|ဦးပဉ္စင်း)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ကောင်|လုံး|ခု|စီး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ပါး", "ရဟန်းသံဃာ/သာသနာ့ဝန်ထမ်းပြ မျိုးပြပစ္စည်း ('~ပါး')"
            ),
            // People / officials / students -> ဦး (left boundary prevents animal diminutives like 'လိပ်ကလေးတစ်ကောင်', 'ကြောင်ကလေးတစ်ကောင်' from matching 'ကလေး')
            (
                new[] { "ကျောင်းသား", "ကျောင်းသူ", "ဆရာ", "ဝန်ထမ်း", "အရာရှိ", "ကော်မတီဝင်", "ကိုယ်စားလှယ်", "ဧည့်သည်", "လူနာ", "အလုပ်သမား", "ခရီးသည်", "လူကြီး", "ကလေး" },
                new Regex(@"(?<![\u1000-\u103F])(?:ကျောင်းသား|ကျောင်းသူ|ဆရာ|ဆရာမ|ဆရာဝန်|ဝန်ထမ်း|အရာရှိ|ကော်မတီဝင်|ကိုယ်စားလှယ်|ဧည့်သည်|လူနာ|အလုပ်သမား|ခရီးသည်|လူကြီး|ကလေး)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ကောင်|လုံး|စီး|ထည်|ရံ|အုပ်|ချပ်|စောင်|ခု)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ဦး", "လူပုဂ္ဂိုလ်ပြ မျိုးပြပစ္စည်း ('~ဦး' သို့မဟုတ် '~ယောက်')"
            ),
            // Clothing -> ထည်
            (
                new[] { "အင်္ကျီ", "ဘောင်းဘီ", "လုံချည်", "ပုဆိုး", "ထဘီ", "ဂါဝန်" },
                new Regex(@"(?:အင်္ကျီ|ဘောင်းဘီ|လုံချည်|ပုဆိုး|ထဘီ|ဂါဝန်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|လုံး|အုပ်|စောင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ထည်", "အဝတ်အထည်ပြ မျိုးပြပစ္စည်း ('~ထည်')"
            ),
            // Sacred pagodas / Buddha images / Tripitaka -> ဆူ
            (
                new[] { "စေတီ", "ဘုရား", "ရုပ်ပွားတော်", "ဆင်းတုတော်", "ဓာတ်တော်", "ပိဋကတ်" },
                new Regex(@"(?:စေတီ|စေတီတော်|ဘုရား|ရုပ်ပွားတော်|ဆင်းတုတော်|ဓာတ်တော်|ပိဋကတ်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|ခု|လုံး|စီး|ထည်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ဆူ", "ဘုရား/စေတီ/ပိဋကတ်ပြ မျိုးပြပစ္စည်း ('~ဆူ')"
            ),
            // Trees & plants -> ပင်
            (
                new[] { "ပင်" },
                new Regex(@"(?:သစ်ပင်|ပန်းပင်|ထန်းပင်|အုန်းပင်|ညောင်ပင်|ဝါးပင်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|ခု|လုံး|စီး|ထည်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ပင်", "သစ်ပင်ပန်းမန်ပြ မျိုးပြပစ္စည်း ('~ပင်')"
            ),
            // Hand-held weapons, umbrellas & stringed instruments -> လက် (exempts 'ထီး + ထည်' e.g. 'ကမ်းခြေထီးတစ်ထည်')
            (
                new[] { "သေနတ်", "ဓား", "လှံ", "ထီး", "ဂီတာ" },
                new Regex(@"(?:(?:သေနတ်|ဓား|လှံ|ဂီတာ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ)|ထီး\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|အုပ်|ရံ))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "လက်", "လက်နက်/တူရိယာ/ထီးပြ မျိုးပြပစ္စည်း ('~လက်')"
            ),
            // Inanimate abstract nouns -> ခု (when misclassified with human/animal classifier ယောက်/ဦး/ကောင်)
            (
                new[] { "မေးခွန်း", "ပြဿနာ", "အစီအစဉ်", "စီမံကိန်း", "လုပ်ငန်း", "ကိစ္စ", "နည်းလမ်း", "ရလဒ်", "အကြောင်းအရာ" },
                new Regex(@"(?:မေးခွန်း|ပြဿနာ|အစီအစဉ်|စီမံကိန်း|လုပ်ငန်း|ကိစ္စ|နည်းလမ်း|ရလဒ်|အကြောင်းအရာ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ခု", "အရာဝတ္ထု/သဘောတရားပြ မျိုးပြပစ္စည်း ('~ခု')"
            ),
            // Houses & buildings -> လုံး
            (
                new[] { "နေအိမ်", "တိုက်တာ", "အဆောက်အအုံ" },
                new Regex(@"(?:နေအိမ်|တိုက်တာ|အဆောက်အအုံ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ|ချပ်|ပင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "လုံး", "နေအိမ်/အဆောက်အအုံပြ မျိုးပြပစ္စည်း ('~လုံး')"
            ),
            // Flowers -> ပွင့်
            (
                new[] { "ပန်း" },
                new Regex(@"(?:နှင်းဆီပန်း|စံပယ်ပန်း|ကြာပန်း|သစ်ခွပန်း|ပန်းပွင့်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|လုံး|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ပွင့်", "ပန်းပွင့်ပြ မျိုးပြပစ္စည်း ('~ပွင့်')"
            ),
            // Flat plates, glass panes, mats, cards -> ချပ်
            (
                new[] { "ပန်းကန်ပြား", "မှန်ချပ်", "ဖျာ", "ကတ်ပြား", "ဆင်းမ်ကတ်" },
                new Regex(@"(?:ပန်းကန်ပြား|မှန်ချပ်|ဖျာ|ကတ်ပြား|ဆင်းမ်ကတ်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ပင်|စင်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ချပ်", "အပြား/အချပ်လိုက်ပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ချပ်')"
            ),
            // Loose paper sheets & leaves -> ရွက်
            (
                new[] { "ရွက်" },
                new Regex(@"(?:စာရွက်|သစ်ရွက်|ဖက်ရွက်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|လုံး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ရွက်", "အရွက်လိုက်ရှိသော ပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ရွက်')"
            ),
            // Rings & bracelets -> ကွင်း
            (
                new[] { "လက်စွပ်", "လက်ကောက်" },
                new Regex(@"(?:လက်စွပ်|လက်ကောက်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|လုံး|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ကွင်း", "အကွင်းလိုက်ပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ကွင်း')"
            ),
            // Rivers, canals, highways & garlands -> သွယ်
            (
                new[] { "မြစ်", "တူးမြောင်း", "လမ်းမကြီး", "ပန်းကုံး" },
                new Regex(@"(?:မြစ်|တူးမြောင်း|လမ်းမကြီး|ပန်းကုံး)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|လုံး|ရံ|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "သွယ်", "မြစ်ချောင်း/လမ်းကြောင်း/ပန်းကုံးပြ မျိုးပြပစ္စည်း ('~သွယ်')"
            ),
            // Newspapers, journals & periodicals -> စောင်
            (
                new[] { "သတင်းစာ", "ဂျာနယ်", "စာစောင်", "သဝဏ်လွှာ", "ကြေညာချက်" },
                new Regex(@"(?:သတင်းစာ|ဂျာနယ်|စာစောင်|သဝဏ်လွှာ|ကြေညာချက်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|ရံ|လုံး|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "စောင်", "သတင်းစာ/စာစောင်ပြ မျိုးပြပစ္စည်း ('~စောင်')"
            ),
            // Feature films & movies -> ကား
            (
                new[] { "ဇာတ်ကား" },
                new Regex(@"(?:ရုပ်ရှင်ဇာတ်ကား|ဗီဒီယိုဇာတ်ကား|ဇာတ်ကား)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|လုံး|ရံ|စောင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ကား", "ရုပ်ရှင်/ဇာတ်ကားပြ မျိုးပြပစ္စည်း ('~ကား')"
            ),
            // Timepieces & household furniture -> လုံး
            (
                new[] { "နာရီ", "စားပွဲ", "ကုလားထိုင်", "ဗီရို", "ခြင်ထောင်" },
                new Regex(@"(?:လက်ပတ်နာရီ|တိုင်ကပ်နာရီ|စားပွဲ|ကုလားထိုင်|ဗီရို|ခြင်ထောင်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ|စောင်|ပင်|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "လုံး", "နာရီ/ပရိဘောဂ အလုံးပြ မျိုးပြပစ္စည်း ('~လုံး')"
            ),
            // Committees, commissions & formal organizations (guards 'ကော်မတီဝင်' which takes 'ဦး') -> ဖွဲ့
            (
                new[] { "ကော်မတီ", "ကော်မရှင်", "အဖွဲ့အစည်း" },
                new Regex(@"(?:ကော်မတီ(?!ဝင်)|ကော်မရှင်(?!အဖွဲ့ဝင်)|အဖွဲ့အစည်း)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|လုံး|အုပ်|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ဖွဲ့", "ကော်မတီ/အဖွဲ့အစည်းပြ မျိုးပြပစ္စည်း ('~ဖွဲ့')"
            ),
            // Sermons, suttas & legal articles -> ပုဒ်
            (
                new[] { "တရားတော်", "သုတ္တန်", "ဥပဒေပုဒ်မ" },
                new Regex(@"(?:တရားတော်|သုတ္တန်|ဥပဒေပုဒ်မ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|လုံး|ရံ|စောင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ပုဒ်", "တရားတော်/ပုဒ်မပြ မျိုးပြပစ္စည်း ('~ပုဒ်')"
            ),
            // Served meals & dishes -> ပွဲ
            (
                new[] { "ထမင်းပွဲ", "ဟင်းပွဲ" },
                new Regex(@"(?:ထမင်းပွဲ|ဟင်းပွဲ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ပွဲ", "စားသောက်ဖွယ်ရာ ပွဲပြ မျိုးပြပစ္စည်း ('~ပွဲ')"
            ),
            // Hand tools (uses unambiguous 'သံတူ/တူရွင်း' rather than bare 'တူ' which also means 'nephew'!) -> လက်
            (
                new[] { "ကတ်ကြေး", "လွှ", "ဆောက်", "သံတူ", "တူရွင်း" },
                new Regex(@"(?<![\u1000-\u103F])(?:ကတ်ကြေး|လွှ|ဆောက်|သံတူ|တူရွင်း)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|လုံး|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "လက်", "လက်သုံးကိရိယာပြ မျိုးပြပစ္စည်း ('~လက်')"
            ),
            // Blankets & shawls -> ထည်
            (
                new[] { "ခြုံစောင်", "ပဝါ" },
                new Regex(@"(?:ခြုံစောင်|ပဝါ|လက်ကိုင်ပဝါ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|လုံး|အုပ်|ရံ|စင်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
                "ထည်", "အဝတ်အထည်/စောင်ပဝါပြ မျိုးပြပစ္စည်း ('~ထည်')"
            )
        };

        // Exact numeral + classifier + redundant indefinite plural 'များ/တွေ' before case postposition ('*ကျောင်းသား သုံးဦးများကို' -> 'သုံးဦးကို / သုံးဦးတို့ကို')
        private static readonly Regex ClassifierRedundantPluralPattern = new Regex(
            @"(?:နှစ်|သုံး|လေး|ငါး|ခြောက်|ခုနစ်|ရှစ်|ကိုး|တစ်ဆယ်|[၂-၉][၀-၉]*)\s*(?<Target>(?<Clf>ဦး|ယောက်|ပါး|ကောင်|စီး|စင်း|အုပ်|စောင်|လုံး|ထည်|ခု|ချောင်း|ရံ|ဆောင်|ပုဒ်|သွယ်|ဖွဲ့)(?<Redundant>များ|တွေ))(?=(?:ကို|က|ဟာ|မှာ|တွင်|၌|နှင့်|နဲ့|အတွက်|အား)(?=\s|$|[\u104A\u104B]))");

        private static readonly Regex ReciprocalHumanClassifierPattern = new Regex(
            @"(?:မိတ်ဆွေ|သူငယ်ချင်း|လူသား|ညီအစ်ကို|မောင်နှမ|ဝန်ထမ်း|ကျောင်းသား)[^\u104A\u104B\r\n]{0,24}?(?<Target>တစ်ကောင်ကိုတစ်ကောင်|တစ်ကောင်နှင့်တစ်ကောင်)");

        // 4i. Negation Framing, Experiential 'ဖူး/ဘူး', Verbal-Nominalizer 'သောကြောင့်', Particle Chains & Postposition Frames
        // Experiential 'ဖူး' miswritten as 'ဘူး' before affirmative/interrogative SFP ('ရောက်ဘူးတယ်' -> 'ရောက်ဖူးတယ်')
        private static readonly Regex ExperientialBuInsteadOfPhuPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<!မ)(?<Verb>ရောက်|စား|သောက်|မြင်|ကြား|တွေ့|သွား|လာ|ဖတ်|ကြည့်|လုပ်|နေ|ကြုံ|လေ့လာ)(?<Target>ဘူး)(?=(?:တယ်|သည်|ပါတယ်|ပါသည်|သလား|လား|ပြီ)(?=\s|$|[\u104A\u104B?]))");

        // Negated colloquial main verb closed with affirmative 'တယ်/မယ်' ('*မသွားတယ်။' -> 'မသွားဘူး။')
        private static readonly Regex NegatedVerbWithAffirmativeSfpPattern = new Regex(
            @"(?<![\u1000-\u103F])မ(?!နက်|နေ့|ကြာ|ကွေး|လေး)(?<Verb>သွား|လာ|စား|သောက်|သိ|ရှိ|ဟုတ်|လုပ်|ပြော|ကြည့်|ဖတ်|ရေး|အိပ်|ထိုင်|ရောက်|တွေ့|ဝယ်|ယူ|ပေး|ဖြစ်|ကစား)(?<Target>တယ်|ပါတယ်|မယ်|ပါမယ်)(?=\s*[\u104B]|$)");

        // Unpaired 'ဘူး' at sentence end without negation prefix 'မ-' ('အရသာရှိဘူး။' -> 'အရသာရှိတယ်' or 'အရသာမရှိဘူး')
        private static readonly Regex BareNegationPrefixCheckRegex = new Regex(
            @"(?<![\u1000-\u103F]|ကျွန်|အစ်|ညီ|နှ)မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A]|နက်|နေ့|ကြာ|ကွေး|လေး)");

        private static readonly Regex UnpairedNegativeBuWithoutMaPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?<Clause>[^\u104A\u104B\r\n]{2,56}?)(?<Target>(?<Verb>အရသာရှိ|ကောင်းမွန်|သေချာ|ဖြစ်နိုင်|ရောက်လာ|ပြည့်စုံ)ဘူး)(?=\s*[\u104B]|$)");

        // Bipartite compound verbs with dropped infixed 'မ-' before 'ဘူး/ဖူး/ဘူ' (e.g. 'နေကောင်းဘူး -> နေမကောင်းဘူး / နေမကောင်းပါ', 'အဆင်ပြေဘူး -> အဆင်မပြေဘူး / အဆင်မပြေပါ')
        private static readonly Regex DroppedInfixMaCompoundNegationPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?<Clause>[^\u104A\u104B\r\n]{0,56}?)(?<![\u1039]|မ)(?<Target>(?:(?<Head>နေ)(?<Root>ကောင်း)|(?<Head>အဆင်)(?<Root>ပြေ)|(?<Head>စိတ်)(?<Root>ကောင်း|ဆိုး|ပူ|ချ|ရှည်|ဝင်စား)|(?<Head>သဘော)(?<Root>တူ|ကျ|ပေါက်)|(?<Head>နား)(?<Root>လည်|ထောင်)|(?<Head>အချိန်)(?<Root>ရ|ရှိ)|(?<Head>အဓိပ္ပာယ်)(?<Root>ရှိ)|(?<Head>အသုံး)(?<Root>ဝင်|တည့်)|(?<Head>သတိ)(?<Root>ရ|ထားမိ)|(?<Head>အလုပ်)(?<Root>အား|ဖြစ်))(?<Aux>နေ|သေး|တော့|နိုင်|ချင်|တတ်|ကြ|ပါ|သေးပါ|တော့ပါ|နိုင်ပါ)?(?<End>ဘူး|ဖူး|ဘူ))" + ClauseFinalNegationFollowLookahead);

        // Colloquial dropped pre-verbal 'မ-' before 'ဘူး/ဖူး/ဘူ' ('ဟုတ်ဘူး -> မဟုတ်ဘူး', 'သိဘူး -> မသိဘူး', 'အားနေဖူး -> မအားနေဘူး / မအားလပ်ပါ', 'ရှိဘူး -> မရှိဘူး')
        private static readonly Regex DroppedMaColloquialNegationPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?<Clause>[^\u104A\u104B\r\n]{0,56}?)(?<![\u1000-\u103F])(?<Target>(?<Verb>ဟုတ်|သိ|ရှိ|အား|အားလပ်|ရ|ကောင်း|ဆိုး|လာ|သွား|လုပ်|ပြော|မေး|ဖြေ|ပေး|ယူ|ဝယ်|စား|သောက်|အိပ်|ကြည့်|ဖတ်|ရေး|မှတ်မိ|မေ့|တူ|မှန်|မှား|လွယ်|ခက်|လို|လိုအပ်|ဆိုင်|ကြိုက်|ချစ်|ကြောက်|ပျင်း|မော|နာ|ဝေး|ကြာ|ထင်|ပြီး|ရောက်|တွေ့)(?<Aux>နေ|သေး|တော့|နိုင်|ချင်|တတ်|ရ|ခဲ့|ကြ|ပါ|သေးပါ|တော့ပါ|နိုင်ပါ)?(?<End>ဘူး|ဖူး|ဘူ))" + ClauseFinalNegationFollowLookahead);

        // Incomplete colloquial negation missing 'ဘူး' ('ညနေပိုင်းမှာ ကျွန်တော်တို့ ဘောလုံးမကစား။' -> 'မကစားဘူး')
        private static readonly Regex IncompleteColloquialNegationPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?=[^\u104A\u104B\r\n]*(?:မှာ|တွေ|နဲ့|ဒီမနက်|ညနေပိုင်းမှာ))[^\u104A\u104B\r\n]{2,56}?(?<Target>မ(?:ကစား|သွား|စား|လာ|သောက်|အိပ်|ဖတ်|ရေး))(?=\s*[\u104B])");

        // Bare verb directly before nominal causal postposition 'ကြောင့်' ('*မိုးရွာကြောင့်' -> 'မိုးရွာသောကြောင့်')
        private static readonly Regex BareVerbBeforeKyauntPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Verb>မိုးရွာ|နေမကောင်း|ဖျားနာ|နောက်ကျ|ပင်ပန်း|အလုပ်များ|မအား|မသိ|မရှိ|မလာ|မသွား|မရောက်|မတွေ့|မဖတ်|မလုပ်|မပြည့်စုံ|မလုံလောက်|ချို့ယွင်း|ပျက်စီး|ခက်ခဲ)\s*(?<Target>ကြောင့်)");

        // Bare negated verb directly before purposive nominal postposition 'အတွက်' ('*မသိအတွက်' -> 'မသိသည့်အတွက်')
        private static readonly Regex BareNegatedVerbBeforeAtwetPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Verb>မသိ|မရှိ|မလာ|မသွား|မရောက်|မလုပ်|မဖတ်|မရေး|မပြော|မပေး|မအောင်မြင်|မပြည့်စုံ|မလုံလောက်|မလိုက်နာ)\s*(?<Target>အတွက်)(?=\s|$|[\u104A\u104B])");

        // Adjacent subject+object case collision ('*သူကကို'), mixed colloquial+formal SFP ('*တယ်သည်', guarded against ဟိုတယ်/တင့်တယ်), duplicate/contradictory aspect ('*ခဲ့ခဲ့', '*သေးပြီ'), and nominal '*တစ်ခုပါခဲ့။'
        private static readonly Regex AdjacentSubjObjCollisionPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Pron>သူ|ငါ|မင်း|သင်|ကျွန်တော်|ကျွန်မ|ကျွန်ုပ်)(?<Target>ကကို)(?=\s)");

        private static readonly Regex MixedRegisterSfpCollisionPattern = new Regex(
            @"(?<!ဟို|မို|တို|တင့်|စတို)(?<Target>တယ်သည်|သည်တယ်|မယ်မည်|မည်မယ်|ပါတယ်သည်|ပါသည်တယ်)(?=\s*[\u104B]|$)");

        private static readonly Regex ContradictoryOrDoubledAspectPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>ခဲ့ခဲ့|ပြီပြီ|သေးပြီ)(?=\s*(?:သည်|တယ်|[\u104B]|$))");

        private static readonly Regex NominalCopulaPastClashPattern = new Regex(
            @"(?<Target>(?<NumClf>တစ်ခု|တစ်ယောက်|တစ်ဦး|တစ်နေရာ)ပါခဲ့)(?=\s*[\u104B]|$)");

        // Syntactic Case-Postposition Frame Confusions (နေရာပြ 'မှာ/တွင်' vs ရှေးရှုရာပြ 'ကို' vs ထွက်ခွာရာပြ 'မှ' vs ရည်စူးချက်ပြ 'အတွက်' vs ကတ္တားပြ 'က')
        private static readonly Regex InteriorGoalWithStaticVerbPattern = new Regex(
            @"(?:အိမ်ထဲ|အခန်းထဲ|ကျောင်းထဲ|ရုံးထဲ|ဆိုင်ထဲ)(?<Target>ကို)\s*(?=အနားယူနေ|အိပ်နေ|ထိုင်နေ|ရှိနေ|နေထိုင်နေ)");

        private static readonly Regex StaticLocativeWithMotionVerbPattern = new Regex(
            @"(?<![\u1000-\u103F])(?:ကျောင်း|ဈေး|ရုံး|ဆေးရုံ)(?<Target>မှာ)\s+(?=သွားတယ်|သွားမယ်|သွားသည်|သွားမည်)(?=[^\u104A\u104B]{0,12}(?:[\u104B]|$))");

        private static readonly Regex SourceRepositoryKoInsteadOfMhaPattern = new Regex(
            @"(?:ဒေတာဘေ့စ်|စာရင်း|ဘဏ်အကောင့်|ဖိုင်တွဲ|ဂိုဒေါင်|မော်ကွန်းတိုက်)(?<Target>ကို)\s+[\u1000-\u103F]{2,20}?\s+(?=ပြန်လည်ရယူ|ထုတ်ယူ)");

        private static readonly Regex RegionKoBeforeNominalizedInspectionPattern = new Regex(
            @"(?:ပြည်နယ်|တိုင်းဒေသကြီး|မြို့နယ်)(?<Target>ကို)\s+(?=စစ်ဆေးမှု\s+ပြုလုပ်)");

        private static readonly Regex BenefactiveMhaInsteadOfAtwetPattern = new Regex(
            @"(?:စာမေးပွဲ|ပြိုင်ပွဲ|ကော်မတီ)(?<Target>မှာ)\s+(?=(?:လေ့ကျင့်ခန်း|အစီရင်ခံစာ)\s+(?:ပေးခဲ့|တင်သွင်းခဲ့))");

        private static readonly Regex TripleKoSubjectConfusionPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>(?:ကျွန်တော်|ကျွန်မ|ကျွန်ုပ်|အစ်ကို|အစ်မ)ကို)(?=\s+[\u1000-\u103F]+ကို\s+[\u1000-\u103F]+(?:ကို|အား|နဲ့|နှင့်)\s+(?:ပြော|ပေး|ပြ|ပို့))");

        // Intra-Sentence Register Clash (Colloquial 'လို့' + Bureaucratic 'မည်ဖြစ်သည်', or Formal Report + Heavy Slang)
        private static readonly Regex ColloquialLoWithBureaucraticMyiPhitPattern = new Regex(
            @"(?:မရှိလို့|မအားလို့|နာလို့|မို့လို့)[^\u104A\u104B\r\n]{1,28}?(?<Target>(?<Verb>[\u1000-\u103F]{1,8}?)မည်ဖြစ်သည်)(?=\s*(?:[\u104B]|$|ဆိုပြီး|ဆိုတာ|လို့))");

        private static readonly Regex FormalContextSlangPattern = new Regex(
            @"(?=.*(?:တွင်|၌|စာတမ်း|ညီလာခံ|ဝန်ကြီးဌာန|သတင်းရင်းမြစ်)).*?(?<Target>တော်တော်ဂွမ်း|တော်တော်မိုက်နေသည်|အူသွားတယ်)");

        // 4h. Broken Space-Split Mandatory Compounds (e.g. 'စာ ကြည့်တိုက်' -> 'စာကြည့်တိုက်', 'အစည်း အဝေး' -> 'အစည်းအဝေး', 'နေ့အထိမ်း အမှတ်ဂုဏ်ပြု' -> 'အထိမ်းအမှတ်')
        private static readonly Regex CuratedBrokenCompoundPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])(?<Left>စာ|ဝန်|လေ|အိမ်|ဘယ်|ဘာ|ကိုယ်|လက်|ဒီအ|ထိုနေ|ဆရာ|ကား|ရေ|မြို့|ရုံး|မီး|စိ|လှေ|ရှေ့)|(?<![\u1000-\u103F]အ)(?<Left>လုပ်)|(?<Left>သိပ္ပံ|အဖွဲ့|ဘာသာ|ရဟန်း|ရောဂါ|မြေဆီ|အုပ်|မြန်မာ|စုစု|အစည်း|အခမ်း|အစီ|အခြေ|အတွေ့|အခွင့်|အချက်|အဆောက်|အထိမ်း|အလုပ်|အစား|အဝတ်|အရောင်း|အပေး|အဆက်|အကောင်|အသိ|လုပ်ရိုး|ကျောင်း|ကျောင်းအုပ်|ဆေးရုံ|မိန်း|ယောက်ျား|သတင်း|မော်တော်|ကွန်ပျူ|တယ်လီ|စာသင်|တက္က|အစိုး|ဥပ|ကျန်းမာ|စီးပွား|စိုက်ပျိုး|မွေးမြူ|ပညာ|လူမှု|ပို့ဆောင်|ရေကြောင်း|ဝန်ကြီး|ဦးစီး|အိမ်ခြံ|နိုင်ငံ|ဆောင်ရွက်|လူနာတင်|မီးသတ်|တပ်မ|ကိုယ်စား|ပလတ်|တရား|လုပ်ငန်း|အိမ်မှု|ရုပ်ရှင်|စာစီ))[ \t]+(?<Right>ကြည့်တိုက်|ပညာ|အစည်း|ထောင်|ငန်း|သံဃာ|ပုံးပျံ|ဝေဒနာ|လွှာ|ချုပ်ရေးမှူး|ချုပ်ရေး|နိုင်ငံ|တော်သား|ပေါင်း|အဝေး|အနား|အစဉ်|အနေ|အကြုံ|အရေး|အလမ်း|အလက်|အအုံ|အမှတ်|အကိုင်|အသောက်|အစား|အဝယ်|အယူ|အသွယ်|အထည်|လုပ်စဉ်|သား|သူ|ထမ်း|သမား|ချိန်|အုပ်|ကြီး|ဝန်|လမ်း|ချိုး|နယ်|ခန်း|ကလေး|လေး|စာ|ယာဉ်|တာ|ဖုန်း|ကျောင်း|သိုလ်|ရ|ဒေ|ရေး|ပို့ဆောင်ရေး|ဌာန|မြေ|ခြား|မှတ်|ချက်|တိုင်း|ရာ|ကား|လို|လို့|တော်|စားလှယ်|လှယ်|စတစ်|ဥပဒေ|ခွင်|စစ်|ကိစ္စ|ရုံ|ဆိပ်|စာရိုက်|မှောက်)(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|နှင့်|နဲ့|သည်|၏|ရဲ့|၌|တွင်|က|မှာ|ဟာ|များ|တွေ|အနှံ့|အတွက်|အကြောင်း|ဆိုင်ရာ|ဖြင့်|သဖြင့်|ကြောင့်|အပြင်|ရရှိ|ဖြတ်စ|ကဏ္ဍ|ရေးရာ|အုပ်ချုပ်|လုပ်ငန်း|တစ်ခု|လွတ်|အတိုင်း|ပြီး|အနီး)",
            RegexOptions.Compiled);

        private static readonly Regex ThreePartBrokenCompoundPattern = new Regex(
            @"(?<![\u1000-\u103F])(?:(?<P1>စာ)[ \t]+(?<P2>ကြည့်)[ \t]+(?<P3>တိုက်)|(?<P1>လူ)[ \t]+(?<P2>သွား)[ \t]+(?<P3>လမ်း)|(?<P1>မျက်နှာ)[ \t]+(?<P2>ချင်း)[ \t]+(?<P3>ဆိုင်)|(?<P1>ကျောင်း)[ \t]+(?<P2>အုပ်)[ \t]+(?<P3>ကြီး)|(?<P1>လူ)[ \t]+(?<P2>မှု)[ \t]+(?<P3>ရေး)|(?<P1>ဘာ)[ \t]+(?<P2>သာ)[ \t]+(?<P3>ရေး)|(?<P1>ကျန်း)[ \t]+(?<P2>မာ)[ \t]+(?<P3>ရေး)|(?<P1>စီး)[ \t]+(?<P2>ပွား)[ \t]+(?<P3>ရေး)|(?<P1>ပ)[ \t]+(?<P2>ညာ)[ \t]+(?<P3>ရေး)|(?<P1>နိုင်)[ \t]+(?<P2>ငံ)[ \t]+(?<P3>ရေး)|(?<P1>တိုင်း)[ \t]+(?<P2>ရင်း)[ \t]+(?<P3>သား))(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|နဲ့|သည်|၏|၌|တွင်|က|မှာ|များ|တွေ|လုပ်ငန်း|ဆိုင်ရာ)",
            RegexOptions.Compiled);

        private static readonly Regex BoundSuffixBrokenSpacePattern = new Regex(
            @"(?:(?<Left>[\u1000-\u103F]{2,14})[ \t]+(?<Right>တော်မူ(?=၍|သည်|၏|ပါ|သော|သည့်|သည့်))|(?<Left>[\u1000-\u103F]+ပေ)[ \t]+(?<Right>မဲ့|မယ့်|မယ့်)|(?<Left>[\u1000-\u103F]+ခဲ့)[ \t]+(?<Right>ရ(?=တယ်|သည်|ပါ))|(?<Left>[\u1000-\u103F]{1,12})[ \t]+(?<Right>မှာလား)|(?<Left>မခွဲ)[ \t]+(?<Right>ခြားဘဲ)|(?<Left>မြောက်|တောင်|အရှေ့|အနောက်)[ \t]+(?<Right>ပိုင်း(?=ရှိ|တွင်|၌|မှာ))|(?<Left>အရှေ့|အနောက်|တောင်|မြောက်)[ \t]+(?<Right>ဘက်(?=မှာ|တွင်|၌|သို့|က))|(?<![\u1000-\u103F])(?<Left>ပျော်ရွှင်|အောင်မြင်|တိုးတက်|ပြောင်းလဲ|ဆောင်ရွက်|ပူးပေါင်း|လှုပ်ရှား|အသတ်ခံရ|ထိခိုက်|ဆုံးရှုံး|ဝေဖန်|စမ်းသပ်|နှိပ်စက်|ထောက်ပံ့|ကူညီ|စောင့်ရှောက်)[ \t]+(?<Right>မှု)(?=တွေ|များ|ကို|က|သည်|ဟာ|နှင့်|နှင့်|နဲ့|ကြောင့်|ကြောင့်|[\s\u104A\u104B]|$)|(?<Left>[\u1000-\u103F]{3,14}မှု)[ \t]+(?<Right>များ|တွေ|ကို)(?![\u102B-\u103E\u103A])|(?<![\u1000-\u103F])(?<Left>သူ့|ငါ့|ကိုယ့်|ကိုယ့်|ကျွန်တော့်|ကျွန်တော့်)[ \t]+(?<Right>ကို|အား|ရဲ့|၏|အကြောင်းကို)(?=[\s\u104A\u104B]|$)|(?<Left>အနား)[ \t]+(?<Right>ယူ(?=အပန်းဖြေ|ချင်|နိုင်|ပါ|သည်|တယ်))|(?<Left>အဆင်)[ \t]+(?<Right>ပြေ(?=သလို|ပါ|တယ်|သည်|အောင်))|(?<Left>မတင်)[ \t]+(?<Right>မီ)(?![\u102B-\u103E\u103A])|(?<Left>မယှဉ်သာ)[ \t]+(?<Right>လောက်အောင်)|(?<Left>သဘောကျလဲ)[ \t]+(?<Right>ဆိုတာ)|(?<Left>ဝေးရာသွား)[ \t]+(?<Right>နေသလို)|(?<Left>ပင်တို့)[ \t]+(?<Right>သည်လည်းကောင်း)|(?<Left>ပီတိသု)[ \t]+(?<Right>ခဈာန်)|(?<Left>သီလ)[ \t]+(?<Right>စောင့်ထိန်း|ရှိသော)|(?<Left>ပြန်လည်)[ \t]+(?<Right>ပြုပြင်တာ)|(?<Left>ပြင်ဆင်|ဖလှယ်|ဆောင်ရွက်|လုပ်ဆောင်|စားသောက်|သွားလာ)[ \t]+(?<Right>ကြ(?=တယ်|သည်|မယ်|မည်|ပါ))|(?<Left>လက်ခံ|နားလည်|ဖြေရှင်း|ပြောဆို)[ \t]+(?<Right>တတ်ဖို့)|(?<Left>(?:မျှော်ငေး|စောင့်ဆိုင်း|ထိုင်|ရပ်|နေထိုင်)နေ)[ \t]+(?<Right>သော)|(?<Left>ပြီးသွား)[ \t]+(?<Right>ရင်)|(?<Left>ဆိုက်)[ \t]+(?<Right>လာသော)|(?<Left>လုပ်ဆောင်)[ \t]+(?<Right>သည့်|သည့်)|(?<Left>သိပ်)[ \t]+(?<Right>ကောင်း(?=တယ်|သည်|ပါ))|(?<Left>နေ)[ \t]+(?<Right>ထိုင်(?=ဖို့|ရန်|ရာ|ကြ|သည်|တယ်|ပါ|မှု|ခြင်း))|(?<Left>ကျေးဇူး)[ \t]+(?<Right>တင်(?=ပါ|သည်|တယ်|ရှိ))|(?<Left>ကျင်း)[ \t]+(?<Right>ပ(?=ကြ|သည်|တယ်|ပါ|မည်|မယ်|ခဲ့))|(?<Left>ရောင်း)[ \t]+(?<Right>ချ(?=ကြ|သည်|တယ်|ပါ|မည်|မယ်|ခဲ့))|(?<Left>အရေး)[ \t]+(?<Right>ယူ(?=သည့်|သည့်|သော|ရန်|ဖို့|ခဲ့|သည်|တယ်))|(?<Left>ရင်)[ \t]+(?<Right>ဆိုင်(?=နေ|ရ|ဖို့|ရန်))|(?<Left>ရုံး)[ \t]+(?<Right>ထုတ်(?=စစ်ဆေး))|(?<Left>အချိန်)[ \t]+(?<Right>ကုန်(?=သက်သာ))|(?<![\u1000-\u103F])(?<Left>ဒီ)[ \t]+(?<Right>နှစ်(?=အတွက်|မှာ|တွင်))|(?<Left>သတင်း)[ \t]+(?<Right>အချက်အလက်)|(?<Left>နေရောင်ခြည်|လေ)[ \t]+(?<Right>စွမ်းအင်)|(?<Left>နေ့စဉ်)[ \t]+(?<Right>ဘဝ)|(?<Left>သိမ်းဆည်း)[ \t]+(?<Right>နည်း)|(?<Left>ကူညီ)[ \t]+(?<Right>ပေးခြင်း)|(?<Left>ဂီတ)[ \t]+(?<Right>သံစဉ်)|(?<Left>အသား)[ \t]+(?<Right>ဟင်းလျာ)|(?<Left>ပျော်ရွှင်)[ \t]+(?<Right>စရာ)|(?<Left>အကျိုး)[ \t]+(?<Right>ခံစားခွင့်|ခံစားခွင့်)|(?<Left>စနစ်)[ \t]+(?<Right>တကျ)|(?<=အစု)(?<Left>ရှယ်)[ \t]+(?<Right>ယာ)|(?<=ကာကွယ်ရေး)(?<Left>တပ်ဖွဲ့)[ \t]+(?<Right>များ)|(?<Left>နံဘေး)[ \t]+(?<Right>တွင်)|(?<Left>မြို့)[ \t]+(?<Right>တွင်းသွား))",
            RegexOptions.Compiled);

        private static readonly Regex BoundStemBrokenCompoundPattern = new Regex(
            @"(?:(?<Left>အထိမ်း)[ \t]+(?<Right>အမှတ်)|(?<Left>အစည်း)[ \t]+(?<Right>အဝေး)|(?<Left>အခမ်း)[ \t]+(?<Right>အနား)|(?<Left>အစီ)[ \t]+(?<Right>အစဉ်)|(?<Left>အဆောက်)[ \t]+(?<Right>အအုံ)|(?<Left>အချက်)[ \t]+(?<Right>အလက်)|(?<Left>အကူ)[ \t]+(?<Right>အညီ)|(?<Left>အရည်)[ \t]+(?<Right>အချင်း)|(?<Left>အခက်)[ \t]+(?<Right>အခဲ)|(?<Left>အထောက်)[ \t]+(?<Right>အကူ)|(?<Left>အလွယ်)[ \t]+(?<Right>တကူ)|(?<Left>အစာ)[ \t]+(?<Right>အိမ်)|(?<Left>အသုံး)[ \t]+(?<Right>ဝင်)|(?<Left>အရေး)[ \t]+(?<Right>ကြီး)|(?<Left>ပတ်)[ \t]+(?<Right>ဝန်းကျင်)|(?<Left>စိတ်)[ \t]+(?<Right>နှလုံး)|(?<Left>မူးယစ်)[ \t]+(?<Right>ဆေးဝါး)|(?<Left>မင်္ဂလာ)[ \t]+(?<Right>ဆောင်)|(?<Left>ဆင်းရဲ)[ \t]+(?<Right>ဒုက္ခ)|(?<Left>ကျောင်း)[ \t]+(?<Right>တိုက်)|(?<Left>ကမ်း)[ \t]+(?<Right>ခြေ)|(?<Left>သဲသောင်)[ \t]+(?<Right>ပြင်)|(?<Left>စိတ်ဖိ)[ \t]+(?<Right>စီး)|(?<Left>ပေါင်)[ \t]+(?<Right>မုန့်|မုန့်)|(?<Left>နံနက်|မနက်|ညနေ|နေ့လယ်)[ \t]+(?<Right>စာ)|(?<Left>နည်း)[ \t]+(?<Right>လမ်း)|(?<Left>သမိုင်း)[ \t]+(?<Right>ကြောင်း)|(?<Left>ချက်)[ \t]+(?<Right>ပြုတ်)|(?<Left>နား)[ \t]+(?<Right>ထောင်)|(?<Left>ထိန်း)[ \t]+(?<Right>ချုပ်)|(?<Left>လက်ဖက်)[ \t]+(?<Right>သုပ်)|(?<Left>ပင်လယ်)[ \t]+(?<Right>စာ)|(?<Left>စိတ်ရှည်)[ \t]+(?<Right>သည်းခံ)|(?<Left>စာရွက်)[ \t]+(?<Right>စာတမ်း)|(?<Left>စည်း)[ \t]+(?<Right>ကမ်း)|(?<Left>လုပ်ငန်း)[ \t]+(?<Right>စဉ်)|(?<Left>တရား)[ \t]+(?<Right>စခန်း)|(?<Left>ထမင်း)[ \t]+(?<Right>ကြော်)|(?<Left>ပျော်ပျော်)[ \t]+(?<Right>ပါးပါး)|(?<Left>ခပ်လှမ်း)[ \t]+(?<Right>လှမ်း)|(?<Left>သန့်|သန့်)[ \t]+(?<Right>ရှင်း)|(?<Left>ကိုယ်စား)[ \t]+(?<Right>လှယ်)|(?<Left>တက္က)[ \t]+(?<Right>သိုလ်)|(?<Left>ကွန်ပျူ)[ \t]+(?<Right>တာ)|(?<Left>ပလတ်)[ \t]+(?<Right>စတစ်)|(?<Left>မြင့်)[ \t]+(?<Right>မိုရ်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        private static readonly (Regex Pattern, string Target, string Cue)[] CuratedAddressAndHistoricalBrokenCompounds =
        {
            (new Regex(@"အောက်ပုဇွန်[ \t]+တောင်(?=လမ်း)", RegexOptions.Compiled), "အောက်ပုဇွန်တောင်", "ရန်ကုန်မြို့ ပုဇွန်တောင်မြို့နယ်ရှိ အထင်ကရလမ်းမအမည် (~လမ်း)"),
            (new Regex(@"ပြည်လုံးချမ်း[ \t]+သာ(?![\u1000-\u103F])", RegexOptions.Compiled), "ပြည်လုံးချမ်းသာ", "ရပ်ကွက်/အကွက်/စေတီအမည် (ပြည်လုံးချမ်းသာ)"),
            (new Regex(@"ကျောက်[ \t]+စိမ်းလမ်း(?![\u102B-\u103E])", RegexOptions.Compiled), "ကျောက်စိမ်းလမ်း", "လမ်းအမည် (ကျောက်စိမ်း + လမ်း)"),
            (new Regex(@"(?<![\u1000-\u103F])သဲ[ \t]+တောမြို့(?![\u102B-\u103E])", RegexOptions.Compiled), "သဲတောမြို့", "မန္တလေးတိုင်းဒေသကြီး ဝမ်းတွင်းမြို့နယ်ရှိ ရုံးသုံးမြို့အမည်"),
            (new Regex(@"ရာဇာ?[ \t]+ဓိရာဇ်", RegexOptions.Compiled), "ရာဇဓိရာဇ်", "ဟံသာဝတီဘုရင် ရာဇဓိရာဇ် (သတ်ပုံကျမ်းစံနှုန်း)"),
            (new Regex(@"ဒဂုန်[ \t]+ရွှေမြှား", RegexOptions.Compiled), "ဒဂုန်ရွှေမျှား", "စာရေးဆရာကြီး ဒဂုန်ရွှေမျှား (~လမ်း)"),
            (new Regex(@"ရန်အောင်[ \t]+မြင်(?=ရပ်)", RegexOptions.Compiled), "ရန်အောင်မြင်", "ရပ်ကွက်အမည် (ရန်အောင်မြင်ရပ်ကွက်)"),
            (new Regex(@"မင်းရဲကျော်[ \t]+စွာ(?=လမ်း|ရပ်)", RegexOptions.Compiled), "မင်းရဲကျော်စွာ", "သမိုင်းဝင်မင်းသား/လမ်းအမည် (မင်းရဲကျော်စွာ)"),
            (new Regex(@"ကွမ်[ \t]+အပ်သားမင်းသား", RegexOptions.Compiled), "ကွမ်အပ်သားမင်းသား", "ဒဂုံမြို့သစ်(ဆိပ်ကမ်း)မြို့နယ်ရှိ လမ်းမကြီးအမည်"),
            (new Regex(@"(?<![\u1000-\u103F])က[ \t]+မ္ဘောဇ", RegexOptions.Compiled), "ကမ္ဘောဇ", "ကမ္ဘောဇဒေသ/လမ်းအမည် (ကမ္ဘောဇ)"),
            (new Regex(@"အနော်[ \t]+ရထာ", RegexOptions.Compiled), "အနော်ရထာ", "ပုဂံခေတ် အနော်ရထာမင်းကြီး / အနော်ရထာလမ်း"),
            (new Regex(@"(?<=ညီ[ \t]*အစ်[ \t]*ကို[ \t]*)မောင်[ \t]+မ(?=\s|တွေ|များ|$)", RegexOptions.Compiled), "မောင်နှမ", "ဆွေမျိုးသားချင်း ပေါင်းစပ်ပုဒ် (ညီအစ်ကိုမောင်နှမ)")
        };

        private static HashSet<string> CreateNormalizedSet(params string[] items)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Length; i++)
            {
                set.Add(UnicodeNormalizer.NormalizeCanonicalOrder(items[i]));
            }
            return set;
        }

        private static readonly HashSet<string> VerifiedMandatoryCompounds = CreateNormalizedSet(
            "စာကြည့်တိုက်", "အစည်းအဝေး", "အခမ်းအနား", "အစီအစဉ်", "အခြေအနေ", "အတွေ့အကြုံ",
            "အခွင့်အရေး", "အခွင့်အလမ်း", "အချက်အလက်", "အဆောက်အအုံ", "အထိမ်းအမှတ်", "ကျောင်းသား", "ကျောင်းသူ", "ဝန်ထမ်း",
            "အလုပ်သမား", "အလုပ်ချိန်", "အလုပ်အကိုင်", "အစားအသောက်", "အဝတ်အစား", "အရောင်းအဝယ်", "အပေးအယူ",
            "အဆက်အသွယ်", "အကောင်အထည်", "အသိအမှတ်", "လုပ်ရိုးလုပ်စဉ်",
            "မိန်းကလေး", "ယောက်ျားလေး", "သတင်းစာ", "လေယာဉ်", "မော်တော်ယာဉ်",
            "ကွန်ပျူတာ", "တယ်လီဖုန်း", "စာသင်ကျောင်း", "တက္ကသိုလ်", "အစိုးရ", "ဥပဒေ",
            "ကျန်းမာရေး", "စီးပွားရေး", "စိုက်ပျိုးရေး", "မွေးမြူရေး", "ပညာရေး", "လူမှုရေး", "ပို့ဆောင်ရေး", "ရေကြောင်းပို့ဆောင်ရေး",
            "ဝန်ကြီးဌာန", "ဦးစီးဌာန", "ဆေးရုံအုပ်", "ကျောင်းအုပ်", "ကျောင်းအုပ်ကြီး", "ဆရာဝန်", "ကားလမ်း", "ရေချိုး", "မြို့နယ်", "ရုံးခန်း",
            "အိမ်ခြံမြေ", "နိုင်ငံခြား", "လက်မှတ်", "ဆောင်ရွက်ချက်", "ဒီအတိုင်း", "ထိုနေရာ",
            "လူသွားလမ်း", "မျက်နှာချင်းဆိုင်",
            "လူနာတင်ယာဉ်", "အိမ်စာ", "မီးသတ်ကား", "ဘယ်လို", "ဘာလို့",
            "တပ်မတော်", "တပ်မတော်သား", "ကိုယ်စားလှယ်", "ပလတ်စတစ်", "မြင့်မိုရ်",
            "အဖွဲ့အစည်း", "ဘာသာရေး", "အိမ်ထောင်", "လုပ်ငန်း", "ရဟန်းသံဃာ", "မီးပုံးပျံ",
            "ရောဂါဝေဒနာ", "မြေဆီလွှာ", "အုပ်ချုပ်ရေး", "အုပ်ချုပ်ရေးမှူး", "မြန်မာနိုင်ငံ", "စုစုပေါင်း", "သိပ္ပံပညာ",
            "တရားဥပဒေ", "လုပ်ငန်းခွင်", "စိစစ်", "အိမ်မှုကိစ္စ", "ရုပ်ရှင်ရုံ", "လှေဆိပ်", "စာစီစာရိုက်", "ရှေ့မှောက်",
            "အကူအညီ", "အရည်အချင်း", "အခက်အခဲ", "အထောက်အကူ", "အလွယ်တကူ", "အစာအိမ်", "အသုံးဝင်", "အရေးကြီး",
            "ပတ်ဝန်းကျင်", "စိတ်နှလုံး", "မူးယစ်ဆေးဝါး", "မင်္ဂလာဆောင်", "ဆင်းရဲဒုက္ခ", "ကျောင်းတိုက်", "ကမ်းခြေ",
            "သဲသောင်ပြင်", "စိတ်ဖိစီး", "ပေါင်မုန့်", "နံနက်စာ", "မနက်စာ", "ညနေစာ", "နေ့လယ်စာ", "နည်းလမ်း",
            "သမိုင်းကြောင်း", "ချက်ပြုတ်", "နားထောင်", "ထိန်းချုပ်", "လက်ဖက်သုပ်", "ပင်လယ်စာ", "စိတ်ရှည်သည်းခံ",
            "စာရွက်စာတမ်း", "စည်းကမ်း", "လုပ်ငန်းစဉ်", "တရားစခန်း", "ထမင်းကြော်", "ပျော်ပျော်ပါးပါး", "ခပ်လှမ်းလှမ်း", "သန့်ရှင်း"
        );

        private static readonly Regex SpaceSplitPairCandidateRegex = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>(?<Left>[\u1000-\u103F]{1,10})[ \t]+(?<Right>[\u1000-\u103F]{1,8}?))(?<Suffix>လမ်းမကြီး|လမ်းသွယ်|လမ်းမ|လမ်း|မြို့နယ်|မြို့|ရပ်ကွက်|ရပ်|ကျေးရွာ|ရွာ|ရေကျော်|ဇုန်|ကို|အား|မှ|သို့|နှင့်|နဲ့|သည်|၏|၌|တွင်|က|မှာ|များ|တွေ|အတွက်|အကြောင်း|ဆိုင်ရာ|ဖြင့်)?(?![\u1000-\u103F])",
            RegexOptions.Compiled);

        private static readonly HashSet<string> SpaceSplitBoundaryParticles = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က", "ကနေ",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မည်", "ပါ", "ကြ", "ခဲ့", "လျက်", "၍",
            "ကြောင့်", "ဖြင့်", "သဖြင့်", "ကာ", "သာ", "ပင်", "စွာ", "လည်း", "ဘဲ", "ပြီ", "ပြီး", "လျှင်", "ဟု",
            "တယ်", "မယ်", "မယ့်", "တွေ", "နဲ့", "ဘူး", "တဲ့", "တာ", "ပေါ့", "လား", "လဲ", "နော်",
            "ရဲ့", "လို့", "ပဲ", "တုန်း", "သေး", "တော့", "ဦး", "မ", "အ", "ဒီ", "ဟို", "ထို", "ဤ", "ယင်း"
        );

        // 5. Colloquial expressions converted in Formal / Official mode (စာပေ / ရုံးသုံး အရေးစကား)
        private static readonly (string Trigger, Regex Pattern, string Replacement, string MeaningCue, string Explanation)[] ColloquialRules =
        {
            (
                "တွေ",
                new Regex(@"(?<=[\u1000-\u103F])တွေ(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|သည်|မှာ)"),
                "များ",
                "အများပြပစ္စည်း (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'တွေ' အစား 'များ' သို့မဟုတ် 'တို့' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "နဲ့",
                new Regex(@"(?<=[\u1000-\u103F]|\s)(?<!ချိ|ယိမ်း|လှုပ်|ပြို|ခ|ခနော်ခ|ခနိုးခ)နဲ့(?=\s|$|[\u104A\u104B]|အတူ|ပတ်သက်|စပ်လျဉ်း|အညီ|အမျှ)"),
                "နှင့်",
                "သမ္ဗန္ဓ / ဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'နဲ့' အစား 'နှင့်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါတယ်",
                new Regex(@"ပါတယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))"),
                "ပါသည်",
                "ပစ္စုပ္ပန်/အတိတ် ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'ပါတယ်' အစား 'ပါသည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "တယ်",
                new Regex(@"(?<=[\u1000-\u103F])(?<!တင့်|ဟို|မို|ပေါ်|ဒစ်ဂျစ်|ဒီဂျစ်|ခရစ်စ|ဟော်|ကယ်)တယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))"),
                "သည်",
                "ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'တယ်' အစား 'သည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါမယ်",
                new Regex(@"ပါမယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))"),
                "ပါမည်",
                "အနာဂတ် ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'ပါမယ်' အစား 'ပါမည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "မယ်",
                new Regex(@"(?<=[\u1000-\u103F])(?<!အ|နှမ|မောင်|ပျို|လုံ|ရှမ်း|ဇင်း)မယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))"),
                "မည်",
                "အနာဂတ် ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'မယ်' အစား 'မည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "မဟုတ်ဘူး",
                new Regex(@"မဟုတ်ဘူး"),
                "မဟုတ်ပါ",
                "အငြင်းကြိယာ (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် 'မဟုတ်ဘူး' အစား 'မဟုတ်ပါ' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "လို့",
                new Regex(@"လို့(?=\s*(?:ပြော|ဆို|သိရ|ယူဆ|ဖော်ပြ|တင်ပြ))"),
                "ဟု",
                "စာပေ/ရုံးသုံး အရေးအသား",
                "အရေးစာပေနှင့် ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'လို့' အစား 'ဟု' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ရဲ့",
                new Regex(@"(?<=[\u1000-\u103F])ရဲ့(?=\s+[\u1000-\u1021])"),
                "၏",
                "ပိုင်ဆိုင်ပြဝိဘတ် (အရေးစကား)",
                "အရေးစာပေနှင့် ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'ရဲ့' အစား '၏' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပြီ",
                new Regex(@"ပြီ(?:ကွာ|ကွ|နော်|ဗျာ|ဗျ|ရှင်)(?=\s|$|[\u104A\u104B])"),
                "ပါပြီ",
                "ယဉ်ကျေးသော ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အရပ်သုံးအာမေဍိတ်ပစ္စည်း ('ကွာ/နော်/ဗျာ') အစား 'ပါပြီ' သို့မဟုတ် 'ပြီးဖြစ်ပါသည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ဒီနေ့",
                new Regex(@"(?<![\u1000-\u103F])ဒီနေ့(?![\u102B-\u103E])"),
                "ယနေ့",
                "အချိန်ပြနာမ် (ရုံးသုံး)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ဒီနေ့' အစား 'ယနေ့' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "အခု",
                new Regex(@"(?<![\u1000-\u103F])အခု(?=\s|တွင်|၌|အချိန်|ကာလ)"),
                "ယခု",
                "အချိန်ပြနာမ် (ရုံးသုံး)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'အခု' အစား 'ယခု' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "အရင်က",
                new Regex(@"(?<![\u1000-\u103F])အရင်က(?![\u102B-\u103E])"),
                "ယခင်က",
                "အတိတ်ကာလပြပုဒ် (ရုံးသုံး)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် 'ယခင်က' ဟု သုံးနှုန်းရမည်။"
            ),
            (
                "ဟိုတစ်နေ့က",
                new Regex(@"(?<![\u1000-\u103F])ဟိုတစ်နေ့က(?![\u102B-\u103E])"),
                "ယခင်က",
                "အတိတ်ကာလပြပုဒ် (ရုံးသုံး)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် 'ယခင်က' ဟု သုံးနှုန်းရမည်။"
            ),
            (
                "ဒါကြောင့်",
                new Regex(@"(?<![\u1000-\u103F])ဒါကြောင့်(?![\u102B-\u103E])"),
                "ထို့ကြောင့်",
                "သမ္ဗန္ဓပစ္စည်း (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ဒါကြောင့်' အစား 'ထို့ကြောင့်' သို့မဟုတ် 'သို့ဖြစ်ပါ၍' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပေမ",
                new Regex(@"(?<=[\u1000-\u103F])(?<!ဒါ|သို့)ပေ(?:မဲ့|မယ့်)(?=\s|$|[\u104A\u104B])"),
                "သော်လည်း",
                "ဆန့်ကျင်ဘက်ပြ သမ္ဗန္ဓပစ္စည်း (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ပေမဲ့ / ပေမယ့်' အစား 'သော်လည်း' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "မယ့်",
                new Regex(@"(?<=[\u1000-\u103F])(?<!ပေ)မယ့်(?=\s+[\u1000-\u1021])"),
                "မည့်",
                "အနာဂတ် နာမဝိသေသနပစ္စည်း (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'မယ့်' အစား 'မည့်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ကနေ",
                new Regex(@"(?<=မြို့|ရွာ|အိမ်|ရုံး|ကျောင်း|ဆိုင်|ဌာန|နိုင်ငံ|နေရာ|ဒေသ|လမ်း|ဒီ|ဟို|အဲဒီ)ကနေ(?=\s+)"),
                "မှ",
                "ထွက်ခွာရာပြဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား ထွက်ခွာရာပြ 'ကနေ' အစား 'မှ' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါသလား",
                new Regex(@"ပါသလား(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "ပါသလော",
                "အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ပါသလား' အစား 'ပါသလော' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "သလား",
                new Regex(@"(?<=[\u1000-\u103F])(?<!ပါ)သလား(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "သလော",
                "အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'သလား' အစား 'သလော' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ရမလား",
                new Regex(@"ရမလား(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "ရမည်လော",
                "အနာဂတ် အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ရမလား' အစား 'ရမည်လော' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါမလား",
                new Regex(@"ပါမလား(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "ပါမည်လော",
                "အနာဂတ် အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ပါမလား' အစား 'ပါမည်လော' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "မလား",
                new Regex(@"(?<=[\u1000-\u103F])(?<!က|မ|ရ|ပါ)မလား(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "မည်လော",
                "အနာဂတ် အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'မလား' အစား 'မည်လော' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါသလဲ",
                new Regex(@"ပါသလဲ(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "ပါသနည်း",
                "အကြောင်းအရာ အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ပါသလဲ' အစား 'ပါသနည်း' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "သလဲ",
                new Regex(@"(?<=[\u1000-\u103F])(?<!ပါ)သလဲ(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "သနည်း",
                "အကြောင်းအရာ အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'သလဲ' အစား 'သနည်း' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ရမလဲ",
                new Regex(@"ရမလဲ(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "ရမည်နည်း",
                "အနာဂတ် အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ရမလဲ' အစား 'ရမည်နည်း' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါမလဲ",
                new Regex(@"ပါမလဲ(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "ပါမည်နည်း",
                "အနာဂတ် အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ပါမလဲ' အစား 'ပါမည်နည်း' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "မလဲ",
                new Regex(@"(?<=[\u1000-\u103F])(?<!ရ|ပါ)မလဲ(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))"),
                "မည်နည်း",
                "အနာဂတ် အမေးဝိဘတ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'မလဲ' အစား 'မည်နည်း' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ဘယ်လို",
                new Regex(@"(?<![\u1000-\u103F])ဘယ်လို(?=\s|[\u1000-\u102A])"),
                "မည်သို့",
                "အမေးနာမ်စား (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ဘယ်လို' အစား 'မည်သို့' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ဘာကြောင့်",
                new Regex(@"(?<![\u1000-\u103F])ဘာကြောင့်(?=\s|[\u1000-\u102A])"),
                "အဘယ်ကြောင့်",
                "အကြောင်းရင်းမေးပုဒ် (ရုံးသုံး/စာပေ)",
                "ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား 'ဘာကြောင့်' အစား 'အဘယ်ကြောင့်' ကို သုံးနှုန်းရမည်။"
            )
        };

        private static readonly Regex ColloquialNegativeBuInOfficialPattern = new Regex(
            @"(?<Target>(?:(?<![\u1000-\u103F])|(?:" + CompoundNegationHeads + @"))မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A]|နက်|နေ့|ဟာ|ကွေး|လေးရှား|ရမ်း)(?<Stem>[\u1000-\u103F]{1,18}?)(?<End>ပါဘူး|ဘူး))(?=\s*(?:[""”'’]\s*)?(?:[\u104A\u104B]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))",
            RegexOptions.Compiled);

        private static readonly Regex ColloquialBareVerbLarInOfficialPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>(?<Verb>နေကောင်း|အဆင်ပြေ|ဟုတ်|သိ|ရှိ|အား|အားလပ်|ရ|သေချာ|မှန်|စားပြီးပြီ|ပြီးပြီ)လား)(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$))",
            RegexOptions.Compiled);

        // 6. 'အား' vs 'ကို' particle misuse before direct transitive verbs (Formal / Literary style — guards '-အား' compound nouns like ဓာတ်အား/စွမ်းအား/အင်အား/လုပ်အား & exempts double-ကို/အဖြစ်/စေ & state protocol 'အား လက်ခံတွေ့ဆုံ / ကြိုဆို')
        private static readonly Regex AhInsteadOfKoPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<!ဓာတ်|စွမ်း|အင်|လုပ်|ခွန်|စိတ်|ကိုယ်|ဉာဏ်|အင်္ဂါ|သက်သာ)(?<Particle>အား)(?!ကစား|လုံး|ဖြင့်|လျော်စွာ|ထုတ်|ပေး|ကိုး|နည်း|သာ|ရ)\s*(?<Verb>စစ်ဆေး|ခန့်အပ်|ရွေးချယ်|ဖမ်းဆီး|တည်ဆောက်|ပြုပြင်|ဖျက်သိမ်း|သုံးသပ်|အတည်ပြု|လက်ခံ(?!တွေ့)|ပယ်ဖျက်|ရေးဆွဲ|ထုတ်ပြန်|ကျင်းပ|ဖော်ပြ|ဖွင့်လှစ်|ပိတ်သိမ်း|ရုပ်သိမ်း|လိုက်နာ|ပြဋ္ဌာန်း|ချိုးဖောက်|အကောင်အထည်ဖော်|စွန့်ပစ်|ဖောက်လုပ်|ဆုံးမ(?!ဩဝါဒ)|ဝယ်ယူ|ရောင်းချ|သုံးစွဲ)",
            RegexOptions.Compiled);

        // Matches a genuine direct-object postposition 'ကို' inside the same clause while rejecting lexical 'ကို-' prefixes like 'ကိုဗစ်', 'ကိုရီးယား', 'ကိုလိုနီ', 'ကိုင်', and standalone 'ကိုယ်-' (unless reflexive 'ကိုယ်ကို')
        private static readonly Regex LicensingKoParticleInClauseRegex = new Regex(
            @"ကို(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|ဗစ်|ယ်(?!ကို)|ရီး|လို|ရင်|ကာ|မာ|ထီး)",
            RegexOptions.Compiled);

        // Sequential / subordinate clause-chaining markers that close one verb's argument structure before starting the next predicate clause
        private static readonly Regex ClauseChainingConjunctionRegex = new Regex(
            @"(?:ပြီး(?:နောက်|မှ|လျှင်)?|၍|သဖြင့်|သောကြောင့်|သော်လည်း|(?:^|(?<=[\u1000-\u103F]))ကာ)(?=\s+)",
            RegexOptions.Compiled);

        // Multi-token & dotted-circle '၎င်း' government/legacy-keyboard Unicode typos ('၄်င်း', '၎်င်း', '၄င်း', '၎င်းင်း', '၄င်းင်း', '၄င်', '၎င်')
        private static readonly Regex MisspelledLaKaungMultiTokenPattern = new Regex(
            @"(?<Target>(?:\u1044[\u102B-\u103E]*|\u104E[\u102B-\u103E]+)(?:\u1004\u103A(?!\u1039)\u1038?)+|\u104E(?:\u1004\u103A\u1038){2,}|\u104E\u1004\u103A(?![\u1037\u1038\u1039]))");

        // 6b. 'နှင့်' vs 'ဖြင့်' instrumental postposition in Formal/Official style (မောင်ခင်မင် နှင့် မြန်မာသဒ္ဒါ — အသုံးခံပြဝိဘတ်)
        private static readonly Regex InstrumentalNhitInsteadOfPhintPattern = new Regex(
            @"(?:ဘောပင်|ခဲတံ|ကလောင်|ကလောင်တံ|စုတ်တံ|မင်|ဓား|တုတ်|လက်နက်|စက်|ကွန်ပျူတာ|မော်တော်ယာဉ်|လေယာဉ်|ရထား|အထောက်အထား|အွန်လိုင်း|ဗီဒီယိုကွန်ဖရင့်|အီးမေးလ်|တယ်လီဖုန်း)\s*(?<Particle>နှင့်)\s*(?<Verb>ရေးသား|ရေး|ရိုက်နှိပ်|ခုတ်|ဖြတ်|ထိုး|သွားရောက်|ပို့ဆောင်|တင်ပြ|ကျင်းပ|ဆက်သွယ်|ပေးပို့)");

        // 6d. Redundant stacked postpositions 'အတွက်ကို -> အတွက်' (ဝိဘတ်နှစ်ထပ်: ရည်စူးချက်ပြ 'အတွက်' နောက်၌ ကံဝိဘတ် 'ကို' ထပ်မလိုက်ရ)
        private static readonly Regex RedundantAtwetKoPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>အတွက်ကို)(?=\s|$|[\u104A\u104B])");

        // 6c. 'မှ' (ထွက်ခွာရာပြဝိဘတ်) misused as subject marker instead of 'က' (ကတ္တားဝိဘတ်) — enforced across BOTH GeneralStandard & FormalOfficial!
        // Covers:
        //   (1) '[Place / City / Country / Organization / Host] + မှ + ကြိုဆို...' (e.g. 'နေပြည်တော်မှကြိုဆိုပါသည်' -> 'နေပြည်တော်ကကြိုဆိုပါသည်', spaced or unspaced;
        //       exempts clauses that already have an earlier 'က' subject like 'ဝန်ကြီးက လေဆိပ်မှ ကြိုဆိုသည်', conditional '[Verb]မှ', and negative 'ဘယ်သူမှ မ...')
        //   (2) '[Pronoun / Human Role / Ministry / Department / Committee / Organization] + မှ + [Agentive Speech / Submission / Announcement Verb]'
        //       (exempts ablative-source relative clauses like 'မီးသတ်ဦးစီးဌာနမှ ထုတ်ပြန်ထားသည့် ညွှန်ကြားချက်များ')
        private static readonly Regex HostWelcomeMhaSubjectPattern = new Regex(
            @"(?:^|[\u104A\u104B\r\n])\s*(?![^\u104A\u104B\r\n]*?[\u1000-\u103F]က\s+)(?<Host>[\u1000-\u103F\s]{2,42}?)(?<!ဘယ်သူ|မည်သူ|ဘယ်|မည်သည့်|လာ|ရောက်|တွေ့|မြင်|သိ|ဖိတ်|ခေါ်|ပြီး|စီး|တန်)(?<Particle>မှ)(?!ာ)\s*(?:လှိုက်လှဲစွာ\s*|နွေးထွေးစွာ\s*|ဝမ်းမြောက်စွာ\s*|ဂုဏ်ယူစွာ\s*|အထူး\s*)?(?<Verb>ကြိုဆို)",
            RegexOptions.Compiled);

        private static readonly Regex AgentiveMhaSubjectPattern = new Regex(
            @"(?:^|[\u104A\u104B\r\n])\s*(?![^\u104A\u104B\r\n]*?[\u1000-\u103F]က\s+)(?:[\u1000-\u103F\s]{0,28}?)?(?:ကျွန်တော်|ကျွန်မ|ကျွန်ုပ်|ကျွန်တော်တို့|ကျွန်ုပ်တို့|သက်ဆိုင်ရာ|တာဝန်ရှိသူ(?:များ)?|ဝန်ကြီးဌာန|ဦးစီးဌာန|ကော်မတီ|အဖွဲ့ချုပ်|အဖွဲ့အစည်း|ကုမ္ပဏီ|တရားခံ|တရားလို|ဥက္ကဋ္ဌ|အတွင်းရေးမှူး|ကျောင်းအုပ်(?:ကြီး)?|ဆရာ(?:ကြီး|မ)?)(?<Particle>မှ)(?!ာ)\s*(?:အမှာစကား\s*|အသိပေးချက်\s*|ကြေညာချက်\s*|အစီရင်ခံစာ\s*|ဆက်လက်\s*|ပြန်လည်\s*|အမြန်ဆုံး\s*)?(?<Verb>တင်ပြ|ပြောကြား|ရှင်းလင်း|လျှောက်ထား|အသိပေး|အကြံပြု|ထုတ်ပြန်(?!ထားသည့်|ထားသော|သည့်|သော)|ကြေညာ|မိန့်ကြား|မှာကြား|ဖြေကြား|ဝန်ခံ|ထွက်ဆို|အရေးယူပေး|ဖြေရှင်းပေး|စိစစ်ပေး|ကွင်းဆင်းစစ်ဆေး)",
            RegexOptions.Compiled);

        // 7. Duplicated or invalid Myanmar punctuation (၊၊ or ။။ or ၊။)
        private static readonly Regex DuplicatePunctuationPattern = new Regex(
            @"(\u104A{2,}|\u104B{2,}|\u104A\u104B)");

        private static readonly Regex SentenceOrItemBeforeDoublePaukKaleRegex = new Regex(
            @"(?:(?:\([\u1000-\u1021\u1040-\u10490-9A-Za-z]+\)|[\u1040-\u10490-9]+)|(?:သည်|၏|မည်|ပြီ|ပါ|တယ်|မယ်|ဘူး|လား|လဲ|သတည်း|သနည်း))\s*$");

        // 0c. Government time notation using Myanmar visarga ('း', U+1038) instead of colon (':'), e.g. '၀၉း၀၀' -> '၀၉:၀၀', '၁၀း၀၀' -> '၁၀:၀၀'
        private static readonly Regex TimeVisargaInsteadOfColonPattern = new Regex(
            @"(?<![\u1000-\u10490-9/.-])(?<Target>(?:[\u1040\u1041]?[\u1040-\u1049]|\u1042[\u1040-\u1044])\u1038[\u1040-\u1045][\u1040-\u1049])(?![\u1000-\u10490-9])");

        // 0d. Accidental ASCII space/tab typed inside a single Myanmar syllable right before combining marks (e.g. 'နှင ့်' -> 'နှင့်', 'သည ်' -> 'သည်', 'အဖွဲ ့' -> 'အဖွဲ့', 'ဓာတ ်' -> 'ဓာတ်')
        private static readonly Regex AccidentalSpaceBeforeCombiningMarkPattern = new Regex(
            @"(?<Target>(?:\u1004\u103A\u1039)?[\u1000-\u102A](?:\u1039[\u1000-\u1021])?[\u102B-\u1039\u103B-\u103E]*[\u1000-\u1021][ \t]+(?=[ \t\u102B-\u103E]*\u103A)[\u102B-\u103E]+|(?:\u1004\u103A\u1039)?[\u1000-\u102A\u104E](?:\u1039[\u1000-\u1021])?[\u102B-\u1039\u103B-\u103E]*[ \t]+[\u102B-\u1039\u103B-\u103E][\u102B-\u103E]*(?:[\u1000-\u1021]\u103A[\u1037\u1038]?)?)");

        private static readonly Regex StripHorizontalWhitespaceRegex = new Regex(
            @"[ \t]+");

        // Universal direct-object physical/ingestive verb 'အား -> ကို' misuse (applies in BOTH GeneralStandard & FormalOfficial)
        private static readonly Regex PhysicalDirectObjectAhPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Obj>ထမင်း|ဟင်း|မုန့်|ရေ|လက်ဖက်ရည်|ကော်ဖီ|စာအုပ်|သတင်းစာ|စာတမ်း|တံခါး|ပြတင်းပေါက်|အင်္ကျီ|ဖိနပ်)(?<Particle>အား)\s*(?<Verb>စားသုံး|စားသောက်|စား|သောက်သုံး|သောက်|ဖတ်ရှု|ဖတ်|ဖွင့်လှစ်|ဖွင့်|ပိတ်|ဝတ်ဆင်|ဝတ်)(?=(?:ပါ|ကြ|ခဲ့|နေ|မည်|မယ်|သည်|တယ်|ပြီး|ပြီ|၍|\s*[\u104A\u104B]))");

        // Universal inanimate event/document/law direct-object 'အား -> ကို' misuse ('*အခမ်းအနားအား ကျင်းပသည် -> အခမ်းအနားကို ကျင်းပသည်', '*ဆောင်းပါးအား ဖော်ပြသည် -> ဆောင်းပါးကို ဖော်ပြသည်')
        private static readonly Regex EventDocumentLawDirectObjectAhPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Obj>(?:အခမ်းအနား|အစည်းအဝေး|ညီလာခံ|ဆောင်းပါး|အစီရင်ခံစာ|စီမံကိန်း|လုပ်ငန်းစဉ်|ဥပဒေ|နည်းဥပဒေ|စည်းကမ်း(?:ချက်)?|စည်းမျဉ်း|အမိန့်ကြော်ငြာစာ|အမှိုက်(?:သရိုက်)?|သင်တန်း|ပြပွဲ|ပြိုင်ပွဲ|လမ်းတံတား)(?:များ|တို့)?)(?<Particle>အား)\s*(?<Verb>ကျင်းပ|တက်ရောက်|ဖွင့်လှစ်|ပိတ်သိမ်း|ရုပ်သိမ်း|ဖော်ပြ|ရေးသား|ရေးဆွဲ|တင်ပြ|လိုက်နာ|ပြဋ္ဌာန်း|ချိုးဖောက်|ထုတ်ပြန်|အကောင်အထည်ဖော်|စွန့်ပစ်|ရှင်းလင်း|ဖောက်လုပ်)(?=(?:ပါ|ကြ|ခဲ့|နေ|မည်|မယ်|သည်|တယ်|ပြီး|ပြီ|၍|ရန်|ခြင်း|သွား|လျက်|\s*[\u104A\u104B]))");

        // Plural verb marker 'ကြ' misspelled as 'ကျ' after multi-syllable agentive verbs (e.g. 'သွားရောက်ကျသည် -> သွားရောက်ကြသည်')
        private static readonly Regex PluralVerbKyaMisusePattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Verb>သွားရောက်|လာရောက်|ဆောင်ရွက်|လုပ်ကိုင်|ပြောဆို|ဆွေးနွေး|တိုင်ပင်|စားသောက်|နေထိုင်|ဖတ်ရှု|လေ့လာ|ကြိုးပမ်း|ပူးပေါင်း|တက်ရောက်|ပါဝင်|ဝိုင်းဝန်း|ကြည့်ရှု)(?<Target>ကျ)(?=(?:ပါသည်|ပါတယ်|ပါမည်|ပါမယ်|သည်|တယ်|မည်|မယ်|ပြီ|သည့်|သော|၍|ကြောင်း)(?=\s|$|[\u104A\u104B]))");

        // Modal auxiliary 'သင့်' ("ought/should") misspelled as relative participle 'သည့်' immediately before polite clause-final particles ('~သည့်ပါသည် -> ~သင့်ပါသည်')
        private static readonly Regex MisusedSinthBeforePoliteSfpPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>သည့်)(?=(?:ပါသည်|ပါတယ်|ပါမည်|ပါမယ်|ပါသလား)(?=\s|$|[\u104A\u104B?]))");

        // Accidental space-separated duplicated postpositions & conjunctions (e.g. 'နှင့် နှင့် -> နှင့်', 'တွင် တွင် -> တွင်')
        private static readonly string[] DuplicatedFunctionWordCandidates =
        {
            "နှင့်", "တွင်", "၌", "သို့", "အတွက်", "ကြောင့်", "သဖြင့်", "သော်လည်း", "သော", "သည့်", "မည့်", "၍", "၏",
            "သည်", "မည်", "လည်း", "သာ", "ပင်", "ကာ"
        };

        private static readonly Regex DuplicatedFunctionWordPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Word>နှင့်|တွင်|၌|သို့|အတွက်|ကြောင့်|သဖြင့်|သော်လည်း|သော|သည့်|မည့်|၍|၏|သည်|မည်|လည်း|သာ|ပင်|ကာ)(?<Dup>[ \t]+\k<Word>)(?=\s|$|[\u104A\u104B])",
            RegexOptions.Compiled);

        // 4m. V42 POS-Syntactic Homophone Suffix Tables (Zero-Regex O(N) IndexOf Scanners)
        private static readonly string[] VerbalAspectBeforeKyaungSuffixes =
        {
            "ပြီးဖြစ်", "ပြီဖြစ်", "မည်ဖြစ်", "လျက်ရှိ", "မဟုတ်", "မရှိ", "နိုင်", "သင့်", "ခဲ့", "ဖူး"
        };

        private static readonly string[] ReportingPredicatesAfterKyaung =
        {
            "သိရှိရ", "သိရ", "ပြောကြား", "ပြောဆို", "ပြောသည်", "ပြောတယ်", "တင်ပြ", "ဖော်ပြ",
            "ကြေညာ", "ထုတ်ပြန်", "တွေ့ရှိရ", "တွေ့ရ", "အသိပေး", "လျှောက်ထား", "အစီရင်ခံ", "ဆိုသည်", "ဆိုတယ်"
        };

        private static readonly string[] NominalPostpositionsAfterChin =
        {
            "ဖြစ်ပါသည်", "ဖြစ်သည်", "အားဖြင့်", "ကြောင့်", "အတွက်", "မပြု", "မရှိ",
            "သည်", "ကို", "ဖြင့်", "နှင့်", "တွင်", "၌", "မှ"
        };

        private static readonly string[] PluralVerbClosersAfterKya =
        {
            "ပါသည်", "ပါတယ်", "ပါမည်", "ပါမယ်", "ကြောင်း", "သည့်", "မည့်",
            "သည်", "တယ်", "မည်", "မယ်", "ပြီ", "သော", "၍"
        };

        private static readonly string[] VerbalAuxStripSuffixes =
        {
            "ပေးထား", "လာခဲ့", "သွားခဲ့", "ပြီးစီး", "ခဲ့", "နိုင်", "ပေး", "ထား", "လာ", "သွား", "ရ", "မိ", "လိုက်"
        };

        private static readonly string[] DirectionalFallVerbEndings =
        {
            "ပြုတ်", "လဲ", "ပြို", "ကြွေ", "စီး", "ထိုး", "လိမ့်", "လျှော", "နိမ့်", "လျော့",
            "ခုန်", "ယို", "ကျိုး", "ပဲ့", "ပျက်", "မြုပ်", "နစ်", "သက်", "ဆင်း", "နေ", "ကျ",
            "စိုက်", "လျား", "အသေးစိတ်", "ပျမ်းမျှ", "လက်တွေ့", "သဘော", "စနစ်တ", "အချိုး",
            "နေရာ", "အံဝင်ခွင်", "ယုတ္တိ", "အဓိပ္ပာယ်", "စည်းကမ်း", "အကွက်"
        };

        // 4n. Smart space-separated multi-syllable duplicate words (e.g. 'ဝန်ကြီးဌာန ဝန်ကြီးဌာန -> ဝန်ကြီးဌာန') & English function words ('the the -> the')
        private static readonly Regex DuplicatedMultiSyllableMyanmarWordPattern = new Regex(
            @"(?<![\u1000-\u104F])(?<Word>[\u1000-\u103F]{4,36})(?<Dup>[ \t]+\k<Word>)(?![\u1000-\u104F])",
            RegexOptions.Compiled);

        private static readonly Regex DuplicatedEnglishFunctionWordPattern = new Regex(
            @"(?<![A-Za-z0-9'-])(?<Word>the|and|in|of|to|for|is|are|on|at|with|by|from|as|an)(?<Dup>[ \t]+\k<Word>)(?![A-Za-z0-9'-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 7c. Accidental horizontal space immediately before Myanmar punctuation '၊' / '။' (with Pali Nissaya gloss exemption)
        private static readonly Regex AccidentalSpaceBeforeMyanmarPunctPattern = new Regex(
            @"(?<=[\u1000-\u103F\u1040-\u1049\)\]\u201D])(?<Target>[ \t]+(?<Punct>[\u104A\u104B]))",
            RegexOptions.Compiled);

        // 7d. Pure-Burmese ASCII numerals before unambiguous Myanmar classifiers/date units in Formal/Official mode (shields English-linked numbers)
        private static readonly Regex PureBurmeseAsciiNumeralPattern = new Regex(
            @"(?<![A-Za-z0-9_./:@#$\-+%])(?<Num>[0-9]{1,4}(?:,[0-9]{3})*)(?<Gap>\)?[ \t]*)(?<Unit>ခုနှစ်|ပြည့်နှစ်|ရက်နေ့|ရာခိုင်နှုန်း|နာရီ|မိနစ်|ယောက်|ကြိမ်|သောင်း|သိန်း|သန်း|ကျပ်|ဦး|ပါး|ခု|စီး|လုံး|ချပ်|စောင်|ထည်|ဖွဲ့|မျိုး|နှစ်(?!သက်|သိမ့်|လို|မြို့|ထောင်း)|ရက်(?!ကန်း|စက်|ရော|သတ္တ)|လ(?![\u1000-\u103F]))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|[A-Za-z0-9])",
            RegexOptions.Compiled);

        // 4k. V19 Deep Myanmar Grammar Patterns (မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးထုံး)
        // 4k-1. WH-Interrogative <-> Polar Question Particle Clash ('*အဘယ်ကြောင့် နောက်ကျပါသလား -> ပါသနည်း/ပါသလဲ', '*ဘာကြောင့် မလာသလား -> သလဲ')
        private static readonly Regex WhInterrogativeWithPolarLarPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Wh>အဘယ်ကြောင့်|အဘယ်သို့|အဘယ်|မည်သို့|မည်သည့်အတွက်|မည်သည့်အချိန်|မည်မျှ|ဘာကြောင့်|ဘာဖြစ်လို့|ဘယ်အချိန်|ဘယ်နေရာမှာ|ဘယ်လိုလုပ်ပြီး)(?![^\u104A\u104B\r\n]{0,36}?(?:လဲ|နည်း|ဆိုတာ|ဆိုသည်|ဟု|လို့|ကြောင်း|မဆို))[^\u104A\u104B\r\n]{0,36}?(?<Target>ပါသလား|သလား|ပါမလား|မလား|တာလား)(?=\s*(?:[\u104B?]|$))");

        // Classical literary WH-pronouns ('အဘယ်ကြောင့်/မည်သို့') closed with colloquial 'သလဲ/မလဲ' ('*မည်သို့ ဆောင်ရွက်ရမလဲ -> မည်နည်း')
        private static readonly Regex LiteraryWhWithColloquialLaePattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Wh>အဘယ်ကြောင့်|အဘယ်သို့|မည်သို့|မည်သည့်အတွက်|မည်မျှ)(?![^\u104A\u104B\r\n]{0,36}?(?:နည်း|ဆိုတာ|ဆိုသည်|ဟု|လို့|ကြောင်း|မဆို))[^\u104A\u104B\r\n]{0,36}?(?<Target>ပါသလဲ|သလဲ|ပါမလဲ|မလဲ|တာလဲ)(?=\s*(?:[\u104B?]|$))");

        // 4k-2. Oblique Creaky-Tone Pronoun Case Discord ('*သူ့သည် -> သူသည်', '*ကျွန်တော့်က -> ကျွန်တော်က', '*သူ့တို့ -> သူတို့') & Illegal Creaky Pronouns ('*ကျွန်ုပ့် -> ကျွန်ုပ်', '*ကျွန်မ့ -> ကျွန်မ')
        // Supports both Asat+DotBelow (\u103A\u1037) and UTN #11 DotBelow+Asat (\u1037\u103A) orders on raw Word text offsets.
        private static readonly Regex CreakyPronounBeforeSubjectOrPluralPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>(?:(?<RawPron>သူ\u1037|ငါ\u1037|ကျွန်တော်\u1037|ကျွန်တော\u1037\u103A|ကိုယ်\u1037|ကိုယ\u1037\u103A)(?<Post>သည်(?=\s|$|[\u104A\u104B])|က(?=\s+)|တို့(?![\u102B-\u103E])))|(?:(?<RawPron2>သင်\u1037|သင\u1037\u103A)(?<Post2>တို့(?![\u102B-\u103E]))))");

        private static readonly Regex IllegalPronounCreakyTonePattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>ကျွန်ုပ်\u1037|ကျွန်ုပ\u1037\u103A|ကျွန်မ\u1037|အစ်မ\u1037|ညီမ\u1037)");

        // 4k-3. Pleonastic & Hybrid Grammatical Constructions ('*မတိုင်မီခင် -> မတိုင်မီ', '*အလို့ငှာအတွက် -> အတွက်/အလို့ငှာ', '*သဖြင့်ကြောင့် -> သောကြောင့်', '*များတွေ -> များ', '*တွင်၌ -> တွင်')
        private static readonly string[] PleonasticTriggers =
        {
            "မတိုင်မီခင်", "မတိုင်ခင်မီ", "အလို့ငှာအတွက်", "သို့သော်လည်းပဲ", "သို့သော်ငြားလည်းပဲ",
            "သဖြင့်ကြောင့်", "သောကြောင့်မို့", "များတွေ", "တွင်၌", "၌တွင်",
            "သို့ကို", "ဖြင့်နှင့်", "နှင့်ဖြင့်", "မှကနေ"
        };

        private static readonly Regex PleonasticHybridGrammarPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])(?<Target>မတိုင်မီခင်|မတိုင်ခင်မီ|အလို့ငှာအတွက်|သို့သော်လည်းပဲ|သို့သော်ငြားလည်းပဲ)(?=\s|$|[\u104A\u104B]|၌|တွင်|မှာ)|(?<=[\u1000-\u103F])(?<Target2>သဖြင့်ကြောင့်|သောကြောင့်မို့လို့|သောကြောင့်မို့|များတွေ|တွင်၌|၌တွင်|သို့ကို|ဖြင့်နှင့်|နှင့်ဖြင့်|မှကနေ)(?=\s|$|[\u104A\u104B]|ကို|က|မှာ|တွင်|၌|နှင့်|နဲ့|သည်|ဟာ|အတွက်))");

        // 4k-4. Adverbial 'စသဖြင့်' misused before nominal plural/case postpositions ('*စသဖြင့်တို့ကို -> စသည်တို့ကို', '*စသဖြင့်များ -> စသည်များ')
        private static readonly Regex MisusedSaThaPhintNominalPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>စသဖြင့်(?<Post>တို့(?:ကို|အား|မှ|သို့|နှင့်|ဖြင့်|သည်|၏|၌|တွင်|က|အတွက်)?|များ(?:ကို|အား|မှ|သို့|နှင့်|ဖြင့်|သည်|၏|၌|တွင်|က|အတွက်)?|ကို|အား|သည်|၏|၌|တွင်))(?=\s|$|[\u104A\u104B])");

        // 4k-5. Hypothetical 'အကယ်၍' paired with causal 'သောကြောင့်/သဖြင့်' instead of conditional 'ပါက/လျှင်', & Dangling Causal Medial Fragment ('...သဖြင့်။')
        private static readonly Regex HypotheticalAkaeYweWithCausalPattern = new Regex(
            @"(?<![\u1000-\u103F])အကယ်၍(?![^\u104A\u104B\r\n]{0,44}?(?:လျှင်|ပါက|သော်|ရင်|မှသာ))[^\u104A\u104B\r\n]{2,44}?(?<Target>သောကြောင့်|သဖြင့်|မို့လို့)(?=\s)");

        private static readonly Regex DanglingCausalMedialSentenceFragmentPattern = new Regex(
            @"(?:^|[\u104B\r\n]\s*)(?<Clause>[^\u104A\u104B\r\n]{6,72}?(?:သည်|က|မှာ|တွင်|၌|ကြောင့်|များ|တို့)[^\u104A\u104B\r\n]{2,48}?)(?<!စ)(?<Target>သဖြင့်)\s*(?=\u104B)");

        // 4k-6. Clause-Internal Diglossia / Register Clash (Pure literary '၌/သဖြင့်/အလို့ငှာ/၎င်းသည်' + colloquial 'ပါတယ်/ပါဘူး', or colloquial 'တွေ/နဲ့/ဒီနေ့' + classical 'ပေသည်/ချေသည်/ပါ၏/သတည်း')
        private static readonly Regex PureLiteraryMarkerWithColloquialSfpPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?![^\u104A\u104B\r\n]*?[""”“'’])[^\u104A\u104B\r\n]{0,48}?(?:(?<=[\u1000-\u103F])၌(?=\s)|(?<!စ)သဖြင့်(?=\s)|အလို့ငှာ(?=\s)|(?<![\u1000-\u103F])၎င်း(?:တို့)?သည်(?=\s))(?![^\u104A\u104B\r\n]{0,48}?(?:ဟု|လို့|ဆိုတာ|ဆိုပြီး))[^\u104A\u104B\r\n]{1,48}?(?<Target>ပါတယ်|ပါမယ်|မဟုတ်ဘူး|ပါဘူး)(?=\s*[\u104B]|$)");

        private static readonly Regex ColloquialMarkerWithClassicalSfpPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?![^\u104A\u104B\r\n]*?[""”“'’])[^\u104A\u104B\r\n]{0,36}?(?<Target>(?<=[\u1000-\u103F])တွေ(?=\s|ကို|က|မှာ)|(?<=[\u1000-\u103F])(?<!ချိ|ယိမ်း|လှုပ်)နဲ့(?=\s)|(?<![\u1000-\u103F])(?:ဒီနေ့|အဲဒီ))(?![^\u104A\u104B\r\n]{0,44}?(?:ဟု|လို့))[^\u104A\u104B\r\n]{1,44}?(?:ပေသည်|ချေသည်|သတည်း)(?=\s*[\u104B]|$)");

        // 4k-7. Monastic / Clergy Honorific Subject–Verb Concord ('*ဆရာတော်ကြီးသည် ... ထမင်းစားသည် -> ဆွမ်းဘုဉ်းပေးသည်', '*သေဆုံး/ကွယ်လွန် -> ပျံလွန်တော်မူ')
        private static readonly Regex MonasticHonorificVerbConcordPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?:[\u1000-\u103F\s]{0,18}?)?(?<Subj>ဆရာတော်(?:ကြီး|ဘုရား)?|သံဃာတော်(?:အရှင်သူမြတ်)?|ရဟန်းတော်|ဘုန်းတော်ကြီး)(?:များ|တို့)?(?:သည်|က)\s+(?![^\u104A\u104B\r\n]{0,28}?(?:ဒကာ|ဒါယကာ|ဒါယိကာမ|လူ|ကလေး|ကျောင်းသား|ဧည့်သည်|ပြည်သူ|ရွာသား))[^\u104A\u104B\r\n]{0,28}?(?<Target>ထမင်းစား|သေဆုံး|ကွယ်လွန်|အိပ်စက်|အိပ်ပျော်|နေထိုင်)(?=(?:တော်မူ|ကြ|ခဲ့|နေ|ပါ|သည်|တယ်|ပြီ|သွား|\s*[\u104B]))");

        // 4k-8. Auxiliary Verb Particle Slot Inversion ('*ဆောင်ရွက်ပါနိုင်သည် -> ဆောင်ရွက်နိုင်ပါသည်', '*သိရှိစေပါလိုသည် -> သိရှိစေလိုပါသည်', '*စောင့်ဆိုင်းခဲ့နေသည် -> စောင့်ဆိုင်းနေခဲ့သည်')
        private static readonly Regex InvertedPoliteModalParticlePattern = new Regex(
            @"(?:(?<Verb>ဆောင်ရွက်|တက်ရောက်|အသုံးပြု|လေ့လာ|ပြောကြား|တင်ပြ|ဖတ်ရှု|ဝယ်ယူ|လိုက်နာ|ဖြေရှင်း|ကူညီ|ပူးပေါင်း|စစ်ဆေး|လျှောက်ထား|ဆက်သွယ်|ပါဝင်)(?<Target>ပါ(?<Modal>နိုင်|ချင်|လို|သင့်))(?=(?:သည်|တယ်|မည်|မယ်|သလား|သလဲ|သနည်း|\s*[\u104B]))|(?<=[\u1000-\u103F])(?<Target2>စေပါလို)(?=(?:သည်|ပါသည်|၏|\s*[\u104B])))");

        private static readonly Regex InvertedPastProgressiveParticlePattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<!အ|မ)(?<Target>ခဲ့နေ)(?=(?:ပါသည်|ပါတယ်|သည်|တယ်)(?=\s|$|[\u104A\u104B]))");

        // 4l. V29 Official Myanmar National Registration Card (NRC / နိုင်ငံသားစိစစ်ရေးကတ်ပြား) Span Shield & Slot Validation
        private static readonly Regex ValidNrcCodeSpanPattern = new Regex(
            @"(?:[၀-၉0-9]{1,2}[ \t]*[-–/\\][ \t]*[\u1000-\u1021\u1025](?:[ \t.]*[\u1000-\u1021\u1025]){1,3}|[\u1000-\u1021\u1025](?:[ \t.]*[\u1000-\u1021\u1025]){1,3}[ \t]*(?:[\(（][ \t]*(?:နိုင်|နိင်|နိုင|ဧည့်|ဧည့်|ဧည်|ပြု|ပြူ|သာသနာ့?|သာ|သ|ယာယီ|ယာရီ|စ|န|[NEPTYS])[ \t]*[\)）]|[-–/\\][ \t]*(?:နိုင်|နိင်|နိုင|ဧည့်|ဧည့်|ဧည်|ပြု|ပြူ|သာသနာ့?|သာ|သ|ယာယီ|ယာရီ|စ|န|[NEPTYS])(?![\u1000-\u103F])))",
            RegexOptions.Compiled);

        private static readonly Regex MisspelledNrcCardTypePattern = new Regex(
            @"(?:(?:[၀-၉0-9]{1,2}[ \t]*[-–/\\][ \t]*)?[\u1000-\u1021\u1025](?:[ \t.]*[\u1000-\u1021\u1025]){1,3}[ \t]*[\(（][ \t]*(?<Target1>ပြူ|သာသနာ့)[ \t]*[\)）]|(?:[၀-၉0-9]{1,2}[ \t]*[-–/\\][ \t]*)?[\u1000-\u1021\u1025](?:[ \t.]*[\u1000-\u1021\u1025]){1,3}[ \t]*[-–/\\][ \t]*(?<Target2>ပြူ|သာသနာ့)(?=[ \t]*(?:[-–/\\]|[၀-၉0-9]|$|[\s,၊။\)\]])))");

        private static readonly Regex MisusedWaInNrcStatePattern = new Regex(
            @"(?<![\u1000-\u10490-9])(?<Target>[\u1041-\u1049]*\u101D[\u1040-\u1049\u101D]*)(?=[ \t]*[-–/\\][ \t]*[\u1000-\u1021\u1025]{2,4}[ \t]*[\(（/\\–-])");

        private static readonly Regex MisusedWaInNrcSerialPattern = new Regex(
            @"(?:[\(（][ \t]*(?:နိုင်|ဧည့်|ဧည့်|ပြု|သာသနာ|သာ|သ|ယာယီ|စ|န|[NEPTYS])[ \t]*[\)）][ \t]*[-–/\\]?[ \t]*|[-–/\\][ \t]*(?:နိုင်|ဧည့်|ဧည့်|ပြု|သာသနာ|သာ|သ|ယာယီ|စ|န|[NEPTYS])[ \t]*[-–/\\][ \t]*)(?<Target>(?=[\u1040-\u1049\u101D]*\u101D)(?=[\u1040-\u1049\u101D]*[\u1040-\u1049])[\u1040-\u1049\u101D]{5,7})(?![\u1000-\u10490-9])");

        private static readonly Regex FullNrcStateRangeCheckPattern = new Regex(
            @"(?<![\u1000-\u10490-9])(?<State>[၀-၉0-9]{1,3})[ \t]*[-–/\\][ \t]*[\u1000-\u1021\u1025]{2,4}[ \t]*(?:[\(（][ \t]*(?:နိုင်|ဧည့်|ဧည့်|ပြု|သာသနာ|သာ|သ|ယာယီ|စ|န|[NEPTYS])[ \t]*[\)）]|[-–/\\][ \t]*(?:နိုင်|ဧည့်|ဧည့်|ပြု|သာသနာ|သာ|သ|ယာယီ|စ|န|[NEPTYS])[ \t]*[-–/\\])[ \t]*[၀-၉0-9]{5,7}");

        private static readonly char[] ZeroWidthOrDottedChars = { '\u200B', '\u200C', '\u200D', '\uFEFF', '\u25CC' };
        private static readonly char[] MyanmarDigitsChars = { '\u1040', '\u1041', '\u1042', '\u1043', '\u1044', '\u1045', '\u1046', '\u1047', '\u1048', '\u1049' };

        internal static List<ProofingIssue> FindZeroWidthWordCorruptions(
            string text,
            MyanmarTrie? trie = null,
            bool brokenSyllableOnly = false)
        {
            var found = new List<ProofingIssue>();
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(ZeroWidthOrDottedChars) < 0)
                return found;

            foreach (Match m in ZeroWidthInsideWordPattern.Matches(text))
            {
                Group g = m.Groups["Target"];
                bool hasDottedCircle = g.Value.IndexOf('\u25CC') >= 0;
                if (!hasDottedCircle)
                {
                    bool isMidWordOrBroken = Typography.MyanmarZwspToolkit.IsMidWordOrBrokenWordZwsp(
                        g.Value,
                        trie,
                        out bool isBrokenSyllable);

                    if (brokenSyllableOnly)
                    {
                        if (!isBrokenSyllable)
                            continue;
                    }
                    else if (!isMidWordOrBroken)
                    {
                        // Clean word/phrase boundary ZWSP (e.g. ဝန်ကြီးဌာန[ZWSP]ဆောင်ရွက်ချက် or မှတ်ပုံတင်ထားခြင်း[ZWSP]အတွက်)
                        // used intentionally for line wrapping in Microsoft Word — do not falsely underline!
                        continue;
                    }
                }

                string stripped = StripZeroWidthRegex.Replace(g.Value, string.Empty);
                string cleaned = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(stripped));
                AddGrammarOrSpellingIssue(
                    found, g.Index, g.Length, g.Value, cleaned,
                    ProofingIssueType.LookalikeCharacterError,
                    $"စာလုံးအတွင်း မမြင်ရသော ခြားနားသင်္ကေတ (Zero-Width Space) ဝင်နေပါသည် — ရှာဖွေမှု (Ctrl+F) မပျက်စေရန် '{cleaned}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                    "ယူနီကုဒ် စာလုံးပေါင်းအမှန်", "WORD", true);
            }
            return found;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static ulong ComputeMyanmarCharMask(string s)
        {
            ulong mask = 0UL;
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '\u1000';
                if ((uint)d < 64u)
                    mask |= 1UL << d;
            }
            return mask;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool ContainsMm(string text, ulong mmMask, string literal)
        {
            for (int i = 0; i < literal.Length; i++)
            {
                int d = literal[i] - '\u1000';
                if ((uint)d < 64u && (mmMask & (1UL << d)) == 0UL)
                    return false;
            }
            return text.IndexOf(literal, StringComparison.Ordinal) >= 0;
        }

        public List<ProofingIssue> AnalyzeGrammarAndStyle(
            string text,
            ProofingStyleMode styleMode = ProofingStyleMode.FormalOfficial,
            MyanmarTrie? trie = null)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text))
                return issues;

            // Single-pass O(N) character/bigram + 64-bit Myanmar character register mask scan
            ulong mmMask = 0UL;
            bool hasZeroWidthOrDotted = false;
            bool hasLaKaungTrigger = false;
            bool hasVisarga = false;
            bool hasMyanmarDigit = false;
            bool hasNonZeroMyanmarDigit = false;
            bool hasHorizSpace = false;
            bool hasMyanmarSpaceMyanmar = false;
            bool hasShortMyanmarBeforeMyanmar = false;
            bool hasSpaceBeforeCombiningMark = false;
            bool hasStrayVowelAfterAsat = false;
            bool hasAsatVirama = false;
            bool hasTa = false;
            bool hasMa = false;
            bool hasCreakyDot = false;
            bool hasPaukMaGyi = false;
            bool hasConsecutiveMyanmarPunct = false;
            bool hasSpaceBeforeMyanmarPunct = false;
            bool hasBracketOrSmartQuote = false;
            bool hasAsciiDigit = false;
            bool hasAsciiLetter = false;
            bool hasNonWsBefore = false;
            bool hasNrcDelimiter = false;
            bool hasNrcSlashOrDash = false;
            bool prevWasHorizSpace = false;
            int currentMmRunLen = 0;
            int mmRunBeforeHorizSpace = 0;
            char prevChar = '\0';

            for (int idx = 0; idx < text.Length; idx++)
            {
                char c = text[idx];
                if (c == ' ' || c == '\t')
                {
                    if (!prevWasHorizSpace)
                    {
                        mmRunBeforeHorizSpace = currentMmRunLen;
                    }
                    currentMmRunLen = 0;
                    hasHorizSpace = true;
                    prevWasHorizSpace = true;
                    prevChar = c;
                    continue;
                }

                bool wasHorizSpaceBefore = prevWasHorizSpace;
                int mmDelta = c - '\u1000';
                if ((uint)mmDelta < 64u)
                {
                    mmMask |= 1UL << mmDelta;
                    if (prevWasHorizSpace)
                    {
                        if (c >= '\u102B' && c <= '\u103E')
                            hasSpaceBeforeCombiningMark = true;
                        if (mmRunBeforeHorizSpace > 0)
                        {
                            hasMyanmarSpaceMyanmar = true;
                            if (mmRunBeforeHorizSpace <= 12)
                                hasShortMyanmarBeforeMyanmar = true;
                        }
                    }
                    currentMmRunLen++;
                }
                else
                {
                    currentMmRunLen = 0;
                }
                prevWasHorizSpace = false;

                if (c == '/' || c == '\\' || c == '-' || c == '–')
                {
                    hasNrcDelimiter = true;
                    hasNrcSlashOrDash = true;
                }
                else if (c == '(' || c == '（')
                {
                    hasNrcDelimiter = true;
                    hasBracketOrSmartQuote = true;
                }
                else if (c == ')' || c == '）' || c == '[' || c == ']' || c == '【' || c == '】' ||
                         c == '\u201C' || c == '\u201D' || c == '"')
                {
                    hasBracketOrSmartQuote = true;
                }
                else if (c >= '0' && c <= '9')
                {
                    hasAsciiDigit = true;
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    hasAsciiLetter = true;
                }

                if (c >= '\u1000' && c <= '\u104F')
                {
                    if (c == 'တ') hasTa = true;
                    else if (c == 'မ') hasMa = true;
                    else if (c == '\u1037') hasCreakyDot = true;
                    else if (c == '\u1038') hasVisarga = true;
                    else if (c == '\u104B')
                    {
                        hasPaukMaGyi = true;
                        if (prevChar == '\u104A' || prevChar == '\u104B') hasConsecutiveMyanmarPunct = true;
                        if (wasHorizSpaceBefore && hasNonWsBefore) hasSpaceBeforeMyanmarPunct = true;
                    }
                    else if (c == '\u104A')
                    {
                        if (prevChar == '\u104A' || prevChar == '\u104B') hasConsecutiveMyanmarPunct = true;
                        if (wasHorizSpaceBefore && hasNonWsBefore) hasSpaceBeforeMyanmarPunct = true;
                    }
                    else if (c == '\u104E' || c == '\u1044') hasLaKaungTrigger = true;
                    else if (c == '\u1039' && prevChar == '\u103A') hasAsatVirama = true;
                    else if (prevChar == '\u103A' && c >= '\u102D' && c <= '\u1032') hasStrayVowelAfterAsat = true;

                    if (c >= '\u1040' && c <= '\u1049')
                    {
                        hasMyanmarDigit = true;
                        if (c >= '\u1041') hasNonZeroMyanmarDigit = true;
                    }
                }
                else if ((c >= '\u200B' && c <= '\u200D') || c == '\uFEFF' || c == '\u25CC')
                {
                    hasZeroWidthOrDotted = true;
                }
                if (!char.IsWhiteSpace(c))
                    hasNonWsBefore = true;
                prevChar = c;
            }

            // 0. Zero-Width / Dotted-Circle Word Corruption (must run first so invisible U+200B..U+200D inside words are caught cleanly)
            if (hasZeroWidthOrDotted)
            {
                issues.AddRange(FindZeroWidthWordCorruptions(text));
            }

            // 0b. Government Unicode '၎င်း' check ('၄င်း', '၎င်းင်း', '၄င်းင်း', or bare '၎' without 'င်း')
            if (hasLaKaungTrigger)
            {
                foreach (Match m in MisspelledLaKaungMultiTokenPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "၎င်း",
                        ProofingIssueType.LookalikeCharacterError,
                        $"ရုံးသုံး ယူနီကုဒ်စံနှုန်းအရ '{g.Value}' အစား '၎င်း' (U+104E + င်း) ကို တိကျစွာ ရေးသားရမည်။",
                        "ရုံးသုံးနာမ်စား ('၎င်း')", "PRON", true,
                        "ပြည်ထောင်စုယူနီကုဒ် (Pyidaungsu Unicode) တွင် '၎င်း' ကို သင်္ကေတ '၎' (U+104E) နောက်၌ 'င်း' တစ်ကြိမ်သာတွဲ၍ ရေးသားရသည် ('၄' ဂဏန်းသုံးခြင်း သို့မဟုတ် '၎င်းင်း' ဟု နှစ်ထပ်ရိုက်ခြင်းသည် မှားယွင်းသည်)။",
                        "မြန်မာယူနီကုဒ်စံနှုန်း နှင့် ရုံးသုံးမြန်မာစာရေးထုံး");
                }
            }

            // 0c. Time notation using Myanmar visarga ('း', U+1038) instead of colon (':')
            if (hasVisarga && hasMyanmarDigit)
            {
                foreach (Match m in TimeVisargaInsteadOfColonPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string fixedTime = g.Value.Replace('\u1038', ':');
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, fixedTime,
                        ProofingIssueType.LookalikeCharacterError,
                        $"အချိန်နာရီ ဖော်ပြရာတွင် ဝစ္စပေါက် ('း') အစား အစက်နှစ်စက် (':') ကို သုံးရမည် — '{fixedTime}' ဟု ရေးသားရမည်။",
                        "အချိန်နာရီ စံရေးထုံး", "NUM", true,
                        "မြန်မာဂဏန်းနောက်၌ ဝစ္စပေါက် ('း' U+1038) ရိုက်နှိပ်ပါက ယူနီကုဒ်အစက်ဝိုင်း (Dotted Circle) ပေါ်တတ်သဖြင့် အချိန်နာရီတွင် ':' (Colon) ကိုသာ သုံးရမည်။",
                        "မြန်မာယူနီကုဒ်စံနှုန်း နှင့် ရုံးသုံးမြန်မာစာရေးထုံး");
                }
            }

            // 0d. Accidental space before combining vowel/medial/asat/tone mark inside a single syllable
            if (hasSpaceBeforeCombiningMark)
            {
                foreach (Match m in AccidentalSpaceBeforeCombiningMarkPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string joined = StripHorizontalWhitespaceRegex.Replace(g.Value, string.Empty);
                    string cleaned = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(joined));
                    var syls = MyanmarSyllableBreaker.BreakIntoSyllables(cleaned);
                    if (syls.Count == 1 && MyanmarSyllableBreaker.IsStructurallyValidSyllable(syls[0], out _))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, cleaned,
                            ProofingIssueType.LookalikeCharacterError,
                            $"သရ/အသတ်/သင်္ကေတရှေ့တွင် Space မှားခြားမိနေပါသည် — '{cleaned}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            "ယူနီကုဒ် စာလုံးပေါင်းအမှန်", "WORD", true,
                            "မြန်မာယူနီကုဒ်တွင် ဗျည်းနှင့် သရ၊ အသတ် ('်')၊ အောက်မြစ် ('့')၊ ဝစ္စပေါက် ('း') တို့ကြား၌ Space ခြားမိပါက စာလုံးပျက်စီး၍ အစက်ဝိုင်းပေါ်တတ်ပါသည်။",
                            "ပြည်ထောင်စုယူနီကုဒ်စံနှုန်း (ISO/IEC 10646)");
                    }
                }
            }

            // 0e. V29 Official Myanmar NRC Slot Validation (Card-type slot typos '(ပြူ)->(ပြု)', consonant 'ဝ' in NRC digits, & invalid State > 14 / 0)
            List<(int Start, int End)>? validNrcSpans = null;
            if (hasNrcDelimiter)
            {
                foreach (Match mNrc in ValidNrcCodeSpanPattern.Matches(text))
                {
                    validNrcSpans ??= new List<(int Start, int End)>(2);
                    validNrcSpans.Add((mNrc.Index, mNrc.Index + mNrc.Length));
                }

                if (ContainsMm(text, mmMask, "ပြူ") || ContainsMm(text, mmMask, "သာသနာ့"))
                {
                    foreach (Match mCard in MisspelledNrcCardTypePattern.Matches(text))
                    {
                        Group g = mCard.Groups["Target1"].Success ? mCard.Groups["Target1"] : mCard.Groups["Target2"];
                        string wrongCard = g.Value;
                        string rightCard = wrongCard == "ပြူ" ? "ပြု" : "သာသနာ";
                        string cardExplain = wrongCard == "ပြူ"
                            ? "နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမျိုးအစားသင်္ကေတ (MOIP) စံနှုန်းအရ နိုင်ငံသားပြုခွင့်ရသူအတွက် တစ်ချောင်းငင်ဖြင့် 'ပြု' ဟု ရေးသားရမည် ('ပြူ' မဟုတ်ပါ)။"
                            : "သာသနာဝင်စိစစ်ရေးကတ်ပြား အမျိုးအစားသင်္ကေတတွင် အောက်မြစ်မပါဘဲ 'သာသနာ' ဟု ရေးသားရမည်။";
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, wrongCard, rightCard,
                            ProofingIssueType.SpellingError,
                            cardExplain,
                            "နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမျိုးအစားသင်္ကေတ (MOIP)", "N", true,
                            cardExplain,
                            "လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP) နိုင်ငံသားစိစစ်ရေးကတ်ပြား စံနှုန်း");
                    }
                }

                if ((mmMask & (1UL << ('\u101D' - '\u1000'))) != 0UL)
                {
                    foreach (Match mWaSt in MisusedWaInNrcStatePattern.Matches(text))
                    {
                        Group g = mWaSt.Groups["Target"];
                        string fixedSt = g.Value.Replace('\u101D', '\u1040');
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, fixedSt,
                            ProofingIssueType.LookalikeCharacterError,
                            $"နိုင်ငံသားစိစစ်ရေးကတ်ပြား တိုင်းဒေသကြီး/ပြည်နယ်ဂဏန်းတွင် ဗျည်း 'ဝ' (U+101D) အစား သုညဂဏန်း '၀' (U+1040) ဖြင့် '{fixedSt}' ဟု ရေးသားရမည်။",
                            "နိုင်ငံသားစိစစ်ရေးကတ်ပြား ပြည်နယ်/တိုင်းဂဏန်း", "NUM", true,
                            "ဂဏန်းနေရာတွင် ဗျည်းအက္ခရာ 'ဝ' (U+101D) မှားယွင်းရိုက်နှိပ်မိနေပါသည် — သင်္ချာသုညဂဏန်း '၀' (U+1040) ကို သုံးရမည်။",
                            "ပြည်ထောင်စုယူနီကုဒ်စံနှုန်း နှင့် MOIP နိုင်ငံသားစိစစ်ရေးကတ်ပြား စံနှုန်း");
                    }

                    foreach (Match mWaSer in MisusedWaInNrcSerialPattern.Matches(text))
                    {
                        Group g = mWaSer.Groups["Target"];
                        string fixedSer = g.Value.Replace('\u101D', '\u1040');
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, fixedSer,
                            ProofingIssueType.LookalikeCharacterError,
                            $"နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမှတ်စဉ်ဂဏန်းအတွင်း ဗျည်း 'ဝ' (U+101D) အစား သုညဂဏန်း '၀' (U+1040) ဖြင့် '{fixedSer}' ဟု ရေးသားရမည်။",
                            "နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမှတ်စဉ်ဂဏန်း", "NUM", true,
                            "မှတ်ပုံတင်ဂဏန်း ၆ လုံးအတွင်း ဗျည်းအက္ခရာ 'ဝ' (U+101D) ရောနှောနေပါသည် — သင်္ချာသုညဂဏန်း '၀' (U+1040) ကို သုံးရမည်။",
                            "ပြည်ထောင်စုယူနီကုဒ်စံနှုန်း နှင့် MOIP နိုင်ငံသားစိစစ်ရေးကတ်ပြား စံနှုန်း");
                    }
                }

                if (hasNrcSlashOrDash)
                {
                    foreach (Match mStRange in FullNrcStateRangeCheckPattern.Matches(text))
                    {
                        Group gSt = mStRange.Groups["State"];
                        int parsedSt = 0;
                        bool okSt = true;
                        for (int si = 0; si < gSt.Value.Length; si++)
                        {
                            char sc = gSt.Value[si];
                            int dVal = (sc >= '\u1040' && sc <= '\u1049') ? (sc - '\u1040')
                                     : ((sc >= '0' && sc <= '9') ? (sc - '0') : -1);
                            if (dVal < 0) { okSt = false; break; }
                            parsedSt = parsedSt * 10 + dVal;
                        }
                        if (okSt && (parsedSt < 1 || parsedSt > 14))
                        {
                            bool useMmDig = gSt.Value[0] >= '\u1040' && gSt.Value[0] <= '\u1049';
                            string sampleValid = parsedSt == 0 ? (useMmDig ? "၁" : "1") : (useMmDig ? "၁၂" : "12");
                            AddGrammarOrSpellingIssue(
                                issues, gSt.Index, gSt.Length, gSt.Value, sampleValid,
                                ProofingIssueType.SpellingError,
                                $"မြန်မာနိုင်ငံ နိုင်ငံသားစိစစ်ရေးကတ်ပြား တိုင်းဒေသကြီး/ပြည်နယ်သင်္ကေတဂဏန်းသည် (၁) မှ (၁၄) အတွင်းသာ ရှိပါသည် — '{gSt.Value}/' သည် တရားဝင်ပြည်နယ်/တိုင်းဂဏန်း မဟုတ်ပါ။",
                                "ပြည်နယ်/တိုင်းဒေသကြီး သင်္ကေတ (၁ မှ ၁၄)", "NUM", true,
                                "လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP) သတ်မှတ်ချက်အရ ပြည်နယ်နှင့် တိုင်းဒေသကြီး သင်္ကေတဂဏန်းများမှာ ၁ (ကချင်) မှ ၁၄ (ဧရာဝတီ) အထိသာ ရှိပါသည်။",
                                "လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP) စံနှုန်း");
                        }
                    }
                }
            }

            // 1. 'တ' vs 'တစ်' Numeral Classifier Rules
            if (hasTa)
            {
                foreach (Match m in TaCompoundNumeralPattern.Matches(text))
                {
                    string first = m.Groups["First"].Value;
                    string second = m.Groups["Second"].Value;
                    string correct = "တစ်" + first + "တစ်" + second;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.NumeralClassifierError,
                        $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈) အရ သင်္ချာဂဏန်း (၁) အရင်းခံ ပေါင်းစပ်ပုဒ်ကို စသတ်ဖြင့် '{correct}' ဟု ရေးသားရမည်။",
                        "သတ်ပုံကျမ်း ဂဏန်း (၁) ပေါင်းစပ်ပုဒ်", "ADV", true,
                        $"'{correct}' အတွင်းရှိ 'တစ်' နှစ်လုံးစလုံးသည် သင်္ချာဂဏန်း (၁) အရင်းခံပုဒ်များဖြစ်သဖြင့် စသတ်ဖြင့် ရေးသားရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈)");
                }

                foreach (Match m in TaNumeralClassifierPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length)) continue;
                    if (validNrcSpans != null)
                    {
                        bool insideNrc = false;
                        int mEnd = m.Index + m.Length;
                        for (int ni = 0; ni < validNrcSpans.Count; ni++)
                        {
                            if (m.Index < validNrcSpans[ni].End && mEnd > validNrcSpans[ni].Start)
                            {
                                insideNrc = true;
                                break;
                            }
                        }
                        if (insideNrc) continue;
                    }
                    if (IsShieldedLexicalTaStem(text, m.Index, m.Length, trie)) continue;
                    string classifier = m.Groups["Classifier"].Value;
                    string correct = "တစ်" + classifier;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.NumeralClassifierError,
                        $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ချာဂဏန်း (၁) အနက်ဆောင်သော မျိုးပြပစ္စည်းရှေ့တွင် '{correct}' ဟု စလုံးသတ်ဖြင့် ရေးသားရမည်။",
                        "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း (သတ်ပုံကျမ်း)", "NUM", true,
                        $"နောက်ကပ်လျက် '{classifier}' သည် မျိုးပြပစ္စည်း/နာမ်ဖြစ်၍ ရှေ့ရှိပုဒ်မှာ သင်္ချာဂဏန်း (၁) အရင်းခံပုဒ် ဖြစ်သည် — 'ရေးတော့အမှန် ဖတ်တော့အသံ' မူအရ စသတ်ဖြင့် '{correct}' ဟု ရေးရမည် (ကြိယာ/မြည်သံစွဲရှေ့ရှိ အာဂုံပစ္စည်း 'တ' ၌သာ စမသတ်ရ)။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈) နှင့် မြန်မာသဒ္ဒါ");
                }

                if (ContainsMm(text, mmMask, "တစ်"))
                {
                    foreach (Match m in HypercorrectedCompoundTaitPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        string correct = g.Value.Replace("တစ်", "တ");
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, correct,
                            ProofingIssueType.NumeralClassifierError,
                            $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၈) အရ အာဂုံပစ္စည်း 'တ' ကို စမသတ်ဘဲ '{correct}' ဟု ရေးသားရမည်။",
                            "သတ်ပုံကျမ်း အမှာစာ (စာ-၈) အာဂုံပစ္စည်း 'တ'", "ADV", true,
                            $"ပေါင်းစပ်ကြိယာဝိသေသနပုဒ် '{correct}' အတွင်းရှိ 'တ' သည် သင်္ချာကိန်းဂဏန်းမဟုတ်သော အာဂုံပစ္စည်းဖြစ်၍ စမသတ်ရပါ။",
                            "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၈)");
                    }

                    foreach (Match m in HypercorrectedTaitPattern.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, m.Index, m.Length)) continue;
                        string redup = m.Groups["Redup"].Value;
                        string correct = "တ" + redup;
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, correct,
                            ProofingIssueType.NumeralClassifierError,
                            $"သတ်ပုံကျမ်းအရ ဂဏန်း (၁) အနက်မဟုတ်သဖြင့် 'တစ်' အစား '{correct}' ဟု အသတ်မပါဘဲ ရေးသားရမည်။",
                            "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်", "ADV", true,
                            $"'{redup}' နှင့်တွဲသော ရှေ့ဆက်ပစ္စည်းသည် ရေတွက်နိုင်သည့် သင်္ချာဂဏန်း (၁) မဟုတ်ဘဲ ကြိယာဝိသေသန/နာမဝိသေသနပြု အာဂုံပစ္စည်း 'တ' ဖြစ်သဖြင့် စသတ်မထည့်ရပါ။",
                            "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၈)");
                    }
                }
            }

            if (ContainsMm(text, mmMask, "ကနဲ"))
            {
                foreach (Match m in MisspelledIdeophoneKanaePattern.Matches(text))
                {
                    string stem = m.Groups["Stem"].Value;
                    string correct = stem + "ခနဲ";
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား (စာ-၃၃၇) “ကနဲ(ဝင်း...၊ ထိန်...) → ခနဲ(ဝင်း...၊ ထိန်...)” အရ '{m.Value}' အစား '{correct}' (ခကွေး) ဖြင့် ရေးသားရမည်။",
                        "သတ်ပုံကျမ်း ခွဲထား (စာ-၃၃၇) ရုတ်ခြည်းပြပစ္စည်း '~ခနဲ'", "ADV", true,
                        "ရုတ်ခြည်းဖြစ်ပေါ်ပုံပြ ကြိယာဝိသေသနနောက်ဆက်ပစ္စည်းကို အသံထွက်၌ '/ဂနဲ/' ဟု ဖတ်သော်လည်း ရေးထုံး၌ 'ခနဲ' (ခကွေး) ဖြင့်သာ ရေးရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား (စာ-၃၃၇)");
                }
            }

            // 2. 'ခုနစ်' vs 'ခုနှစ်'
            if (ContainsMm(text, mmMask, "ခုနှစ်"))
            {
                foreach (Match m in MisusedKhuNhitNumberPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ခုနစ်",
                        ProofingIssueType.NumeralClassifierError,
                        "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ချာဂဏန်း (၇) ကို 'ခုနစ်' (ဟထိုးမပါ) ဟု ရေးသားရမည်။ ('ခုနှစ်' သည် သက္ကရာဇ်/နှစ်ကာလအတွက်သာ ဖြစ်သည်)။",
                        "သင်္ချာဂဏန်း (၇) (ဟထိုးမပါ)", "NUM", true,
                        "နောက်တွင် မျိုးပြပစ္စည်း လိုက်နေသဖြင့် သင်္ချာကိန်း (၇) အနက်ဖြစ်သည် — ဂဏန်း (၇) ကို 'ခုနစ်' (ဟထိုးမပါ) နှင့် နှစ်သက္ကရာဇ်ပြ နာမ်ကို 'ခုနှစ်' (ဟထိုးပါ) ဟု ခွဲခြားရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ");
                }
            }

            if (ContainsMm(text, mmMask, "ခုနစ်"))
            {
                foreach (Match m in MisusedKhuNitYearPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ခုနှစ်",
                        ProofingIssueType.NumeralClassifierError,
                        "သက္ကရာဇ်/နှစ်ကာလကို ဖော်ပြရာတွင် 'ခုနှစ်' (ဟထိုးပါ) ဟု ရေးသားရမည်။",
                        "သက္ကရာဇ်/နှစ်ကာလ (ဟထိုးပါ)", "N", true,
                        "ရှေ့တွင် သက္ကရာဇ်ဂဏန်း ပါရှိနေသဖြင့် အချိန်ကာလပြ 'နှစ်' အနက်ဖြစ်ရာ ဟထိုးပါသော 'ခုနှစ်' ကို သုံးရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ");
                }
            }

            // 3. Generalized 'မ...ပဲ' -> 'မ...ဘဲ' (including bipartite N-မ-V-ပဲ such as 'အမြတ်မထုတ်ပဲ' -> 'အမြတ်မထုတ်ဘဲ')
            if (hasMa && ContainsMm(text, mmMask, "ပဲ"))
            {
                foreach (Match m in NegativeVerbPePattern.Matches(text))
                {
                    Group maGroup = m.Groups["Ma"];
                    if (maGroup.Index > 0)
                    {
                        char prevCh = text[maGroup.Index - 1];
                        if (prevCh >= '\u1000' && prevCh <= '\u103F')
                        {
                            string verbBody = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["VerbBody"].Value);
                            if (verbBody.StartsWith("ကြီး", StringComparison.Ordinal) ||
                                verbBody.StartsWith("ငယ်", StringComparison.Ordinal) ||
                                verbBody.StartsWith("လေး", StringComparison.Ordinal) ||
                                verbBody.StartsWith("ကလေး", StringComparison.Ordinal) ||
                                verbBody.StartsWith("တို့", StringComparison.Ordinal) ||
                                verbBody.StartsWith("များ", StringComparison.Ordinal) ||
                                verbBody.StartsWith("တွေ", StringComparison.Ordinal) ||
                                verbBody.StartsWith("သာ", StringComparison.Ordinal) ||
                                verbBody.StartsWith("လည်း", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (trie != null)
                            {
                                string verbCore = verbBody;
                                string[] auxSuffixes = { "ပါ", "သေး", "တော့", "နိုင်", "ချင်", "တတ်", "ရ", "ကြ" };
                                for (int aIdx = 0; aIdx < auxSuffixes.Length; aIdx++)
                                {
                                    if (verbCore.Length > auxSuffixes[aIdx].Length &&
                                        verbCore.EndsWith(auxSuffixes[aIdx], StringComparison.Ordinal))
                                    {
                                        verbCore = verbCore.Substring(0, verbCore.Length - auxSuffixes[aIdx].Length);
                                        break;
                                    }
                                }
                                if (!trie.HasPosTag(verbCore, "V") && !trie.HasPosTag(verbCore, "ADJ"))
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဘဲ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "အငြင်းကြိယာ ('မ...') နောက်တွင် 'ပဲ' အစား 'ဘဲ' (ဘကုန်း) ဖြင့် ရေးသားရမည်။",
                        "အငြင်းကြိယာနောက်ဆက် ('မ...ဘဲ')", "PART", true,
                        "ရှေ့၌ အငြင်းပြပစ္စည်း 'မ' နှင့် ကြိယာတည်ရှိနေသဖြင့် 'မပြုလုပ်ဘဲ' ဟူသော ကြိယာဝိသေသနပြုပစ္စည်း 'ဘဲ' (ဘကုန်း) ဖြစ်ရမည် ('ပဲ' သည် နာမ်/ဝါကျနောက်လိုက် အနက်လေးနက်စေသောပစ္စည်း 'ဒါပဲ/သူပဲ' ၌သာ သုံးသည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိသေသနပြုပစ္စည်း 'ဘဲ')");
                }
            }

            // 4a. Clause Interrogative 'လည်း' -> 'လဲ'
            if (ContainsMm(text, mmMask, "လည်း") &&
                (((mmMask & (1UL << ('ဘ' - '\u1000'))) != 0UL) || ContainsMm(text, mmMask, "မည်")))
            {
                foreach (Match m in InterrogativeLaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လဲ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "အမေးဝါကျနောက်ဆက်ပစ္စည်းတွင် 'လည်း' အစား 'လဲ' ကို သုံးနှုန်းရမည်။",
                        "အမေးပစ္စည်း (သတ်ပုံကျမ်း)", "PART", true,
                        "ဝါကျအတွင်း အမေးနာမ်စား ('ဘာ/ဘယ်/မည်') ပါရှိနေသဖြင့် ဝါကျအဆုံး အမေးပစ္စည်းအဖြစ် 'လဲ' ကို သုံးရမည် ('လည်း' သည် အတူပြ သမ္ဗန္ဓပစ္စည်း ဖြစ်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အမေးပစ္စည်း 'လဲ' နှင့် သမ္ဗန္ဓ 'လည်း')");
                }
            }

            // 4b. Affirmative 'လဲ' -> 'လည်း'
            bool hasLae = ContainsMm(text, mmMask, "လဲ");
            if (hasLae)
            {
                foreach (Match m in AffirmativeLaeMisusePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လည်း",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "'လည်းကောင်း / အတူတူပင်' အနက်ပြ သမ္ဗန္ဓပစ္စည်းတွင် 'လဲ' အစား 'လည်း' (ညကြီးသတ် ဝစ္စပေါက်) ဖြင့် ရေးသားရမည်။",
                        "အတူပြပစ္စည်း ('...လည်း...')", "PART", true,
                        "အမေးဝါကျမဟုတ်ဘဲ နာမ်/နာမ်စားနောက်၌ 'အတူတူပင်/ပါဝင်ခြင်း' အနက်ဖြင့် ဆက်စပ်ပေးသော ပုဒ်ဖြစ်၍ သမ္ဗန္ဓပစ္စည်း 'လည်း' ကို သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သမ္ဗန္ဓပစ္စည်း 'လည်း')");
                }
            }

            // 4c. Universal Orthography & Casual Keyboard Mistakes (apply in BOTH modes)
            if (hasMa && hasCreakyDot && ContainsMm(text, mmMask, "မည့်သည်"))
            {
                foreach (Match m in MisplacedAukmyitMyiThiPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "မည်သည့်",
                        ProofingIssueType.SpellingError,
                        "အောက်မြစ် ('့') နေရာမှားယွင်းနေပါသည် — နာမ်အထူးပြု အမေး/အညွှန်းနာမ်စားကို 'မည်သည့်' ('သည့်' ၌ အောက်မြစ်ပါ) ဟု ရေးသားရမည်။",
                        "နာမ်အထူးပြု အမေး/အညွှန်းနာမ်စား ('မည်သည့်')", "PRON", true,
                        "နာမ်ရှေ့၌ လိုက်သော အထူးပြုနာမ်စား 'မည်သည့်' (ဥပမာ - မည်သည့်အချိန်၊ မည်သည့်ပစ္စည်းနှင့်မဆို) တွင် အောက်မြစ် ('့') ကို ဒုတိယဝဏ္ဏ 'သည့်' အောက်၌သာ ထားရမည် ('မည့်သည်' ဟု မရေးရပါ)။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ");
                }
            }

            if (ContainsMm(text, mmMask, "ပေမယ့်"))
            {
                foreach (Match m in MisspelledDarPayMaePattern.Matches(text))
                {
                    string correct = m.Groups["Prefix"].Value + "မဲ့";
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ '{m.Value}' အစား '{correct}' (နောက်ပစ်၊ အောက်မြစ်) ဟု ရေးသားရမည်။",
                        "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်", "CONJ", true);
                }
            }

            if (ContainsMm(text, mmMask, "အုန်း") || ContainsMm(text, mmMask, "အုံး"))
            {
                foreach (Match m in MisspelledVerbOhnPattern.Matches(text))
                {
                    if (IsShieldedOfficialWordSpan(text, m.Index, m.Length, trie)) continue;
                    string wrongPart = m.Groups["Target"].Value;
                    string correct = m.Groups["Prefix"].Value + "ဦး";
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"ကြိယာနောက်ဆက်ပစ္စည်းအဖြစ် '{wrongPart}' အစား 'ဦး' ('{correct}') ဟု ရေးသားရမည်။",
                        "ကြိယာနောက်ဆက်ပစ္စည်း (သတ်ပုံကျမ်း)", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "ဟု"))
            {
                foreach (Match m in MisspelledHuInsteadOfHotePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string sfx = m.Groups["Suffix"].Value;
                    string normalizedSfx = sfx == "ကဲ" ? "ကဲ့"
                        : (sfx == "ပီ" ? "ပြီ"
                        : (sfx == "ပါပီ" ? "ပါပြီ" : sfx));
                    string generalFix = "ဟုတ်" + normalizedSfx;
                    string primaryFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (normalizedSfx == "လား" ? "ဟုတ်ပါသလား"
                            : (normalizedSfx == "တယ်" || normalizedSfx == "ပါတယ်" ? "ဟုတ်ပါသည်" : generalFix))
                        : generalFix;

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, primaryFix,
                        ProofingIssueType.SpellingError,
                        $"တဝမ်းပူသတ် ('တ်') ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{primaryFix}' ဟု ရေးသားရမည် ('ဟု' သည် စာပေအဆိုပြပစ္စည်းသာ ဖြစ်၍ ကြိယာနောက်ဆက် '{normalizedSfx}' နှင့် မတွဲနိုင်ပါ)။",
                        "ဟုတ်မှန်ကြောင်းပြ/အမေး ကြိယာပုဒ် ('ဟုတ်~')", "V", true,
                        $"မြန်မာသဒ္ဒါအရ 'ဟု' သည် အဆိုပြပုဒ်ဆက်ပစ္စည်း (quotative particle) သာဖြစ်၍ အမေး/ဝါကျအဆုံးသတ်ပစ္စည်း '{normalizedSfx}' ရှေ့၌ မလိုက်နိုင်ပါ — 'မှန်ကန်သည်' အနက်ဟော 'ဟုတ်' ကြိယာတွင် တဝမ်းပူသတ် ('တ်') ထည့်၍ '{primaryFix}' ဟု ရေးရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ");
                }

                if (hasMa && ContainsMm(text, mmMask, "မဟု"))
                {
                    foreach (Match m in MisspelledNegatedMaHuPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        string conj = m.Groups["Conj"].Success ? m.Groups["Conj"].Value : string.Empty;
                        string rawSfx = m.Groups["Suffix1"].Success ? m.Groups["Suffix1"].Value
                            : (m.Groups["Suffix2"].Success ? m.Groups["Suffix2"].Value : string.Empty);

                        string normSfx = rawSfx;
                        if (normSfx == "ဖူး") normSfx = "ဘူး";
                        else if (normSfx == "ပါဖူး") normSfx = "ပါဘူး";
                        else if (normSfx == "ပဲ") normSfx = "ဘဲ";
                        else if (normSfx == "လာ") normSfx = "လား";

                        if (styleMode == ProofingStyleMode.FormalOfficial && (normSfx == "ဘူး" || normSfx == "ပါဘူး"))
                        {
                            normSfx = "ပါ";
                        }

                        string correct = conj + "မဟုတ်" + normSfx;
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, correct,
                            ProofingIssueType.SpellingError,
                            $"တဝမ်းပူသတ် ('တ်') ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{correct}' ဟု ရေးသားရမည်။",
                            "အငြင်းကြိယာ / သမ္ဗန္ဓ ('မဟုတ်~')", "V", true,
                            $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းတွင် 'မဟု' ဟူသော ပုဒ်မရှိပါ — အငြင်းကြိယာ 'မဟုတ်' ၌ တဝမ်းပူသတ် ('တ်') ဖြင့် '{correct}' ဟု အတိအကျ ရေးရမည်။",
                            "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                    }
                }
            }

            bool hasPiOpen = ContainsMm(text, mmMask, "ပီ");
            bool hasPyeeOpen = ContainsMm(text, mmMask, "ပြီ");
            bool hasMaLar = (hasPiOpen || hasPyeeOpen) && ContainsMm(text, mmMask, "မလား");

            if (hasMaLar)
            {
                foreach (Match m in CompletiveMaLarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = $"'{g.Value}' သည် သဒ္ဒါနှင့် စာလုံးပေါင်း မှားယွင်းနေပါသည် — ပြီးစီးမှုအမေးအတွက် 'ပြီးပြီလား' (သို့မဟုတ် အတည်ပြုအမေး 'ပြီးပြီ မဟုတ်လား') ဟု ရေးသားရမည်။",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = "ပြီးပြီလား",
                                MeaningCue = "ပြီးစီးမှု အမေးပုဒ်တွဲ (ဥပမာ - ထမင်းစားပြီးပြီလား)",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("ပြီးပြီလား"),
                                DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(g.Value, "ပြီးပြီလား"),
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = "ပြီးပြီ မဟုတ်လား",
                                MeaningCue = "အတည်ပြု အမေးပုဒ်တွဲ",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("ပြီးပြီမဟုတ်လား"),
                                DiagnosticDetail = "အပြောစကားအတိုကောက် 'မလား' အစား 'မဟုတ်လား'",
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            if (hasPiOpen || (hasPyeeOpen && ContainsMm(text, mmMask, "ပြီးပြီး")))
            {
                foreach (Match m in DoubledOrMedialDroppedPeePyeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (hasMaLar && CompletiveMaLarPattern.IsMatch(text.Substring(g.Index)))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီးပြီ",
                        ProofingIssueType.SpellingError,
                        $"ပြီးစီးကြောင်းပြ ကြိယာနောက်ဆက်တွင် '{g.Value}' အစား 'ပြီးပြီ' (ရှေ့လုံး ဝစ္စပေါက်ပါ၊ နောက်လုံး ဝစ္စပေါက်မပါ၊ ရရစ်ဖြင့်) ဟု ရေးသားရမည်။",
                        "ပြီးစီးကြောင်းပြ ပုဒ်တွဲ ('~ပြီးပြီ')", "PART", true);
                }
            }

            if (hasPiOpen)
            {
                if (ContainsMm(text, mmMask, "ပါပီ"))
                {
                    foreach (Match m in CasualPaPeePattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "ပါပြီ",
                            ProofingIssueType.SpellingError,
                            "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — 'ပါပီ' အစား 'ပါပြီ' ဟု ရေးသားရမည်။",
                            "ယဉ်ကျေးမှုနှင့် ပြီးစီးခြင်းပြပစ္စည်း", "PART", true);
                    }
                }

                foreach (Match m in CasualClauseFinalPiPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (IsShieldedLexicalPiWord(text, g.Index, g.Length, trie)) continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီ",
                        ProofingIssueType.SpellingError,
                        "ကြိယာနောက်ဆက်တွင် ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — 'ပီ' အစား 'ပြီ' ဟု ရေးသားရမည် ('ပီ' သည် ပီပြင်/ပီသ အနက်၌သာ သုံးသည်)။",
                        "ပြီးစီးခြင်းပြ ကြိယာဝိဘတ် ('~ပြီ')", "PART", true);
                }
            }

            if (hasPyeeOpen)
            {
                foreach (Match m in MisusedSequentialPyeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီး",
                        ProofingIssueType.SpellingError,
                        "ရှေ့ကြိယာ ပြီးဆုံး၍ နောက်ဆက်တွဲဖော်ပြရာတွင် ဝစ္စပေါက်ပါသော 'ပြီး' ('~ပြီးတော့ / ~ပြီးလျှင်') ကို သုံးရမည် ('ပြီ' သည် ဝါကျအဆုံးသတ် ဝိဘတ်ဖြစ်သည်)။",
                        "ရှေ့နောက်စဉ်ပြပစ္စည်း ('~ပြီး')", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "ပြီး") && text.IndexOf('။') >= 0)
            {
                foreach (Match m in MisusedSentenceFinalConverbPyeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီ",
                        ProofingIssueType.SpellingError,
                        "ဝါကျအဆုံး ပြီးစီးခြင်းပြ ကြိယာဝိဘတ်တွင် ဝစ္စပေါက်မပါဘဲ 'ပြီ' ဟု ရေးသားရမည် ('ပြီး' သည် ရှေ့နောက်စဉ်ပြပစ္စည်း ဖြစ်သည်)။",
                        "ပြီးစီးခြင်းပြ ကြိယာဝိဘတ် ('~ပြီ')", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "လာ"))
            {
                foreach (Match m in MissingVisargaQuestionLaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လား",
                        ProofingIssueType.SpellingError,
                        "အမေးဝါကျနောက်ဆက်ပစ္စည်းတွင် ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — 'လာ' အစား 'လား' ဟု ရေးသားရမည်။",
                        "အမေးပစ္စည်း ('~လား')", "PART", true);
                }
            }

            bool hasPhu = ContainsMm(text, mmMask, "ဖူး");
            if (hasMa && hasPhu)
            {
                if (styleMode == ProofingStyleMode.FormalOfficial && ContainsMm(text, mmMask, "အားနေ"))
                {
                    foreach (Match m in RedundantMaAhNayPhuPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = g.Value,
                            IssueType = ProofingIssueType.SpellingError,
                            Message = "'မအားနေဖူး' တွင် အငြင်းပိတ်ပစ္စည်း 'ဖူး' စာလုံးပေါင်းမှားနေပြီး ရုံးသုံး/စာပေစနစ်၌ 'မအားလပ်ပါ' သို့မဟုတ် 'မအားပါ' (အပြောစကား၌ 'မအားနေဘူး / မအားဘူး') ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = "အငြင်းပြ 'မ-' နောက်တွင် အတွေ့အကြုံပြ 'ဖူး' မသုံးရဘဲ ဘကုန်းဖြင့် 'ဘူး' (သို့မဟုတ် ရုံးသုံး/စာပေဟန် 'မအားလပ်ပါ / မအားပါ') သုံးနှုန်းရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = "မအားလပ်ပါ",
                                    MeaningCue = "ရုံးသုံး/စာပေ အငြင်းအရေးအသား",
                                    PosTag = "V",
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("မအားလပ်ပါ"),
                                    DiagnosticDetail = "ရုံးသုံး/စာပေ စံအရေးအသား ('မအားလပ်ပါ')",
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = "မအားပါ",
                                    MeaningCue = "စာပေသုံး အငြင်းကြိယာ ('မ...ပါ')",
                                    PosTag = "V",
                                    IsOfficialThatpon = true,
                                    Score = 97.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = "မအားနေဘူး",
                                    MeaningCue = "အပြောစကား စာလုံးပေါင်းအမှန် ('ဖူး' ➔ 'ဘူး')",
                                    PosTag = "V",
                                    IsOfficialThatpon = false,
                                    Score = 93.0
                                }
                            }
                        });
                    }
                }

                foreach (Match m in MisspelledNegativePhuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "ပါ" : "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.SpellingError,
                        "အငြင်းဝါကျ အဆုံးသတ်ပစ္စည်းတွင် 'ဖူး' အစား 'ဘူး' (ဘကုန်း) ဖြင့် ရေးသားရမည် ('ဖူး' သည် အတွေ့အကြုံပြ 'ရောက်ဖူးသည်' ၌သာ သုံးသည်)။",
                        "အငြင်းကြိယာနောက်ဆက် ('မ...ဘူး')", "PART", false);
                }
            }

            if (ContainsMm(text, mmMask, "ရ်"))
            {
                foreach (Match m in CasualTalMalPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    bool isTal = g.Value == "တရ်";
                    string correct = styleMode == ProofingStyleMode.FormalOfficial
                        ? (isTal ? "သည်" : "မည်")
                        : (isTal ? "တယ်" : "မယ်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"အင်တာနက်အတိုကောက် '{g.Value}' သည် စံစာလုံးပေါင်းမဟုတ်ပါ — '{correct}' ဟု ရေးသားရမည်။",
                        "ကြိယာဝိဘတ် စာလုံးပေါင်းအမှန်", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
            }

            if (hasMa && ContainsMm(text, mmMask, "ဘူ"))
            {
                foreach (Match m in MissingVisargaBuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "ပါ" : "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.SpellingError,
                        "အငြင်းကြိယာနောက်ဆက်တွင် ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — 'ဘူး' ဟု ရေးသားရမည်။",
                        "အငြင်းကြိယာနောက်ဆက်", "PART", false);
                }
            }

            if (hasPiOpen && ContainsMm(text, mmMask, "ပီး"))
            {
                foreach (Match m in CasualPeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီး",
                        ProofingIssueType.SpellingError,
                        "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပြီး' ဟု ရေးသားရမည်။",
                        "ပြီးစီးခြင်းပြပစ္စည်း (သတ်ပုံကျမ်း)", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "ဖစ်"))
            {
                foreach (Match m in CasualPhitPattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဖြစ်",
                        ProofingIssueType.SpellingError,
                        "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဖြစ်' ဟု ရေးသားရမည်။",
                        "ဖြစ်ပေါ်သည် (သတ်ပုံကျမ်း)", "V", true);
                }
            }

            ScanUnspacedAttachedParticleTypos(text, mmMask, styleMode, trie, issues);
            ScanStructuralSyllableAndBoundaryRepairs(text, mmMask, styleMode, trie, issues);

            if (ContainsMm(text, mmMask, "\u101C\u103B\u103E\u1000\u103A"))
            {
                foreach (Match m in MisspelledLyetPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လျက်",
                        ProofingIssueType.SpellingError,
                        "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'လျှက်' (ဟထိုးပါ) ဟူသော စာလုံးပေါင်းမရှိပါ — 'လျက်' (ဟထိုးမပါ) ဖြင့်သာ ရေးသားရမည်။",
                        "ကြိယာနောက်ဆက် / ကြိယာ ('သွား~ လာ~')", "PART", true);
                }
            }

            // 4d. Stray vowel typed after killed coda consonant inside a word (e.g. 'မိန်ိးမ' -> 'မိန်းမ')
            if (hasStrayVowelAfterAsat)
            {
                foreach (Match m in StrayVowelAfterCodaWordPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string fixedWord = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(g.Value));
                    if (fixedWord != g.Value)
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, fixedWord,
                            ProofingIssueType.LookalikeCharacterError,
                            $"အသတ်နောက်တွင် သရအစီအစဉ် မှားယွင်းနေပါသည် — '{g.Value}' အစား '{fixedWord}' ဟု ရေးသားရမည်။",
                            "ယူနီကုဒ် အစီအစဉ်အမှန်", "WORD", true);
                    }
                }
            }

            if (hasAsatVirama)
            {
                foreach (Match m in AccidentalViramaAfterCodaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string fixedCoda = g.Value.Substring(0, g.Value.Length - 1);
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, fixedCoda,
                        ProofingIssueType.LookalikeCharacterError,
                        $"အသတ်နောက်တွင် ပါဌ်ဆင့်သင်္ကေတ ('္') ပိုရိုက်မိနေပါသည် — '{g.Value}' အစား '{fixedCoda}' ဟု ရေးသားရမည်။",
                        "ယူနီကုဒ် အသတ်အမှန်", "WORD", true);
                }
            }

            // 4e. Doubled sentence-ending particles ('တင်ပြပါသည်သည်။' -> 'တင်ပြပါသည်။') & Past+Future Marker Clash ('သွားခဲ့မယ်')
            if (ContainsMm(text, mmMask, "သည်သည်") ||
                ContainsMm(text, mmMask, "တယ်တယ်") ||
                ContainsMm(text, mmMask, "မည်မည်") ||
                ContainsMm(text, mmMask, "မယ်မယ်") ||
                ContainsMm(text, mmMask, "ပါပါ"))
            {
                foreach (Match m in DoubledSentenceFinalParticlePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = v.StartsWith("ပါပါ", StringComparison.Ordinal)
                        ? v.Substring(2)
                        : v.Substring(0, v.Length / 2);
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျအဆုံးသတ် ကြိယာဝိဘတ် နှစ်ထပ်ဖြစ်နေပါသည် — '{v}' အစား '{correct}' ဟု ရေးသားရမည်။",
                        "ကြိယာဝိဘတ် အမှန်", "PART", true);
                }
            }

            bool hasKhae = ContainsMm(text, mmMask, "ခဲ့");
            bool hasMyiOrMal = ContainsMm(text, mmMask, "မည်") ||
                               ContainsMm(text, mmMask, "မယ်");
            if (hasKhae)
            {
                if (hasMyiOrMal)
                {
                    foreach (Match m in ConflictingPastFutureMarkerPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        string v = g.Value;
                        bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal);
                        bool hasPa = v.Contains("ပါ");
                        string futForm = hasPa ? (isFormal ? "ပါမည်" : "ပါမယ်") : (isFormal ? "မည်" : "မယ်");
                        string pastForm = hasPa ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်") : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = v,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"အတိတ်ပြပစ္စည်း ('ခဲ့') နှင့် အနာဂတ်ပြဝိဘတ် ('{futForm}') ဆန့်ကျင်ဘက် တွဲဖက်နေပါသည် — '{futForm}' သို့မဟုတ် '{pastForm}' ကိုသာ ရွေးချယ်သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ကြိယာတစ်ခုတည်းနောက်တွင် ပြီးစီးခဲ့ပြီးသော အတိတ်ကာလပြ ကြိယာထောက်ပစ္စည်း ('ခဲ့') နှင့် မဖြစ်သေးသော အနာဂတ်ကာလပြ ကြိယာဝိဘတ် ('{futForm}') တို့သည် ကာလသဘော ဆန့်ကျင်နေသဖြင့် တစ်ပြိုင်နက် မတွဲဖက်နိုင်ပါ။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'ခဲ့' နှင့် ကာလပြကြိယာဝိဘတ်)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = futForm,
                                    MeaningCue = "အနာဂတ်ပြ ကြိယာဝိဘတ်",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = pastForm,
                                    MeaningCue = "အတိတ်ပြ ကြိယာဝိဘတ်",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 96.0
                                }
                            }
                        });
                    }
                }

                // 4f. Temporal Adverb <-> Sentence-Final Tense Agreement
                if (ContainsMm(text, mmMask, "ဖြန်") ||
                    ContainsMm(text, mmMask, "မကြာ") ||
                    ContainsMm(text, mmMask, "လာမ") ||
                    ContainsMm(text, mmMask, "နောက်"))
                {
                    foreach (Match m in FutureAdverbWithPastVerbPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        string preTarget = text.Substring(m.Index, g.Index - m.Index);
                        if (FutureExemptionSubclauseRegex.IsMatch(preTarget))
                            continue;
                        string v = g.Value;
                        bool passivePatientWithoutExplicitAgent =
                            preTarget.IndexOf("ကို ", StringComparison.Ordinal) >= 0 &&
                            preTarget.IndexOf("က ", StringComparison.Ordinal) < 0;

                        string primary;
                        string secondary;
                        if (v == "ခဲ့ကြပါသည်") { primary = "ကြပါမည်"; secondary = "ကြမည်ဖြစ်သည်"; }
                        else if (v == "ခဲ့ကြသည်") { primary = "ကြမည်ဖြစ်သည်"; secondary = "ကြမည်"; }
                        else if (v == "ခဲ့ကြတယ်") { primary = "ကြမယ်"; secondary = "ကြပါမယ်"; }
                        else if (v == "ခဲ့ပါသည်") { primary = "ပါမည်"; secondary = "မည်ဖြစ်သည်"; }
                        else if (v == "ခဲ့ပါတယ်") { primary = "ပါမယ်"; secondary = "မယ်"; }
                        else if (v == "ခဲ့သည်")
                        {
                            primary = passivePatientWithoutExplicitAgent ? "မည်ဖြစ်သည်" : "မည်";
                            secondary = passivePatientWithoutExplicitAgent ? "မည်" : "မည်ဖြစ်သည်";
                        }
                        else
                        {
                            primary = "မယ်";
                            secondary = "ပါမယ်";
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = v,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ရှေ့တွင် အနာဂတ်အချိန်ပြပုဒ် ပါရှိသဖြင့် အတိတ်ပြဝိဘတ် '{v}' အစား အနာဂတ်ပြဝိဘတ် '{primary}' ကို သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ဝါကျအစ၌ အနာဂတ်ကာလပြ ကြိယာဝိသေသန ('မနက်ဖြန်/နောက်လ/လာမည့်...') တည်ရှိပြီး ကြားတွင် ဝါကျခွဲ ('လျှင်/သော်လည်း') မခံသဖြင့် ပင်မကြိယာကို အတိတ်ပြပစ္စည်း '{v}' ဖြင့် မပိတ်ရဘဲ အနာဂတ်ပြ '{primary}' ဖြင့် ညှိနှိုင်းရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိသေသန နှင့် ကြိယာဝိဘတ် လိုက်လျောညီထွေမှု)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = primary,
                                    MeaningCue = "ကာလကိုက်ညီသော အနာဂတ်ပြဝိဘတ်",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = secondary,
                                    MeaningCue = "အနာဂတ်ပြ ကြိယာဝိဘတ်ပုံစံကွဲ",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 95.0
                                }
                            }
                        });
                    }
                }

                if (ContainsMm(text, mmMask, "ခဲ့နေပြီ"))
                {
                    foreach (Match m in ImpossibleAspectStackPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        string primary = styleMode == ProofingStyleMode.FormalOfficial ? "နေသည်" : "နေတယ်";
                        string secondary = styleMode == ProofingStyleMode.FormalOfficial ? "နေပါသည်" : "နေပါတယ်";
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = g.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ကြိယာထောက်ပစ္စည်း အစီအစဉ်နှင့် ကာလဝိဘတ် မှားယွင်းနေပါသည် — '{g.Value}' အစား '{primary}' ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = "အတိတ်ပြ 'ခဲ့'၊ ဖြစ်ဆဲပြ 'နေ' နှင့် အခြေအနေသစ်ရောက်ပြ 'ပြီ' တို့ကို '*ခဲ့နေပြီ' ဟု တစ်ပြိုင်နက် စီစဉ်တွဲစပ်၍ မရပါ။ ဖြစ်ဆဲအနက်အတွက် 'နေတယ်/နေသည်' ကို သုံးရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်းနှင့် ကာလပြဝိဘတ်)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate { Replacement = primary, MeaningCue = "ဖြစ်ပေါ်ဆဲပြ ကြိယာဝိဘတ်", PosTag = "PART", IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial, Score = 100.0 },
                                new SuggestionCandidate { Replacement = secondary, MeaningCue = "ယဉ်ကျေးမှုပါ ဖြစ်ပေါ်ဆဲပြဝိဘတ်", PosTag = "PART", IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial, Score = 95.0 }
                            }
                        });
                    }
                }

                if (ContainsMm(text, mmMask, "ဆိုတာ") && ContainsMm(text, mmMask, "တတ်ခဲ့တယ်"))
                {
                    foreach (Match m in GenericGnomicHabitualPastPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "တတ်တယ်",
                            ProofingIssueType.OfficialStyleGrammarError,
                            "ယေဘုယျဓမ္မတာပြဝါကျ ('~ဆိုတာ ...') တွင် အတိတ်ပြ 'ခဲ့' မလိုအပ်ပါ — 'တတ်ခဲ့တယ်' အစား 'တတ်တယ်' ဟု ရေးသားရမည်။",
                            "ဓမ္မတာ/အလေ့အထပြ ကြိယာနောက်ဆက် ('~တတ်တယ်')", "PART", false,
                            "အမြဲမှန်ကန်သော သဘာဝဓမ္မတာ သို့မဟုတ် အလေ့အထကို ဖော်ပြသည့် ဝါကျ၌ အတိတ်ကာလပြပစ္စည်း 'ခဲ့' မတွဲရဘဲ 'တတ်တယ်/တတ်သည်' ဟုသာ သုံးနှုန်းရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဓမ္မတာပြ ပစ္စုပ္ပန်ကာလ)");
                    }
                }
            }

            if (hasMyiOrMal && (hasKhae || ContainsMm(text, mmMask, "နေ့") || ContainsMm(text, mmMask, "ယမန်")))
            {
                foreach (Match m in PastAdverbWithFutureVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string preTarget = text.Substring(m.Index, g.Index - m.Index);
                    if (PastExemptionSubclauseRegex.IsMatch(preTarget))
                        continue;
                    string v = g.Value;
                    string correct;
                    if (v == "မည်ဖြစ်ကြောင်း")
                    {
                        correct = "ခဲ့ကြောင်း";
                    }
                    else if (v == "မည်ဖြစ်သည်")
                    {
                        correct = "ခဲ့သည်";
                    }
                    else
                    {
                        bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal);
                        bool hasPa = v.Contains("ပါ");
                        correct = hasPa
                            ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်")
                            : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                    }
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ရှေ့တွင် အတိတ်အချိန်ပြပုဒ် ပါရှိသဖြင့် အနာဂတ်ပြဝိဘတ် '{v}' အစား အတိတ်ပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        "ကာလကိုက်ညီသော အတိတ်ပြဝိဘတ်", "PART", true,
                        $"ဝါကျအစ၌ ပြီးလွန်ပြီးသော အတိတ်အချိန်ပြပုဒ် ('မနေ့က/ယမန်နှစ်က/ပြီးခဲ့သော...') ပါရှိပြီး အနာဂတ်အကူးအပြောင်းပုဒ် ('ယခု/ဆက်လက်') မပါသဖြင့် အနာဂတ်ပြ '{v}' အစား အတိတ်ပြ '{correct}' ဖြင့် ကာလညှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိသေသန နှင့် ကြိယာဝိဘတ် လိုက်လျောညီထွေမှု)");
                }
            }

            if (hasMyiOrMal && (ContainsMm(text, mmMask, "ခေတ်") || ContainsMm(text, mmMask, "လက်ထက်")))
            {
                foreach (Match m in HistoricalEraWithSpeculativeFuturePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string v = g.Value;
                    bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal) || styleMode == ProofingStyleMode.FormalOfficial;
                    bool isCopulaPhit = v.StartsWith("ဖြစ်", StringComparison.Ordinal);
                    bool hasPa = v.Contains("ပါ");
                    string correct;
                    if (isCopulaPhit)
                    {
                        correct = hasPa
                            ? (isFormal ? "ဖြစ်ပါသည်" : "ဖြစ်ပါတယ်")
                            : (isFormal ? "ဖြစ်သည်" : "ဖြစ်တယ်");
                    }
                    else
                    {
                        correct = hasPa
                            ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်")
                            : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                    }
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သမိုင်းဝင်ခေတ်ကာလကို ဖော်ပြရာတွင် ခန့်မှန်းအနာဂတ်ပြ '{v}' အစား '{correct}' ကို သုံးနှုန်းရမည်။",
                        "သမိုင်းကာလနှင့် ကိုက်ညီသော ကြိယာဝိဘတ်", "PART", isFormal,
                        "ပြီးလွန်ခဲ့ပြီးသော သမိုင်းခေတ်/မင်းလက်ထက် အကြောင်းအရာဖြစ်သဖြင့် မသေချာသော အနာဂတ်ပြ 'လိမ့်မည်/လိမ့်မယ်' မသုံးရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိဘတ် စည်းမျဉ်း)");
                }
            }

            if (hasKhae &&
                (ContainsMm(text, mmMask, "လက်ရှိတွင်") ||
                 ContainsMm(text, mmMask, "လောလောဆယ်တွင်")))
            {
                foreach (Match m in PresentOngoingWithPastVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string preTarget = text.Substring(m.Index, g.Index - m.Index);
                    if (FutureExemptionSubclauseRegex.IsMatch(preTarget))
                        continue;
                    string v = g.Value;
                    bool isFormal = v.EndsWith("သည်", StringComparison.Ordinal);
                    bool hasPa = v.Contains("ပါ");
                    string correct = hasPa
                        ? (isFormal ? "နေပါသည်" : "နေပါတယ်")
                        : (isFormal ? "နေသည်" : "နေတယ်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ရှေ့တွင် ပစ္စုပ္ပန်ဆဲဆဲပြပုဒ် ('လက်ရှိတွင်') ပါရှိသဖြင့် အတိတ်ပြ '{v}' အစား ဆောင်ရွက်ဆဲပြ '{correct}' ကို သုံးနှုန်းရမည်။",
                        "ဖြစ်ပေါ်ဆဲပြ ကြိယာထောက်ပစ္စည်း ('~နေသည်')", "PART", true,
                        $"'လက်ရှိတွင်' သည် ယခုလက်ရှိ ဆောင်ရွက်ဆဲအခြေအနေကို ညွှန်းသဖြင့် ပြီးဆုံးခဲ့ပြီးသော အတိတ်ပြ '{v}' အစား ဖြစ်ဆဲပြ ကြိယာထောက်ပစ္စည်း '{correct}' (သို့မဟုတ် 'လျက်ရှိသည်') နှင့် တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'နေ' နှင့် အချိန်ကာလ ဆက်စပ်မှု)");
                }
            }

            // 4g. Numeral Classifier Agreement ('ဖိနပ် နှစ်ယောက် -> နှစ်ရံ', 'ကော်မတီဝင် သုံးကောင် -> သုံးဦး', etc.)
            if (HasAnyMyanmarNumeralCore(text, mmMask, hasNonZeroMyanmarDigit))
            {
                for (int rIdx = 0; rIdx < ClassifierAgreementRules.Length; rIdx++)
                {
                    var cRule = ClassifierAgreementRules[rIdx];
                    if (!HasAnySubstring(text, mmMask, cRule.Triggers))
                        continue;

                    foreach (Match m in cRule.Pattern.Matches(text))
                    {
                        Group wrongGrp = m.Groups["Wrong"];
                        AddGrammarOrSpellingIssue(
                            issues, wrongGrp.Index, wrongGrp.Length, wrongGrp.Value, cRule.CorrectClassifier,
                            ProofingIssueType.NumeralClassifierError,
                            $"ရှေ့နာမ်ပုဒ်နှင့် လိုက်ဖက်ညီသော မျိုးပြပစ္စည်း မဟုတ်ပါ — '{wrongGrp.Value}' အစား '{cRule.CorrectClassifier}' ({cRule.CategoryLabel}) ကို သုံးနှုန်းရမည်။",
                            cRule.CategoryLabel, "CLF", true,
                            $"မြန်မာသဒ္ဒါ (နာမ်ပစ္စည်း — မျိုးပြပစ္စည်း) အရ သင်္ချာဂဏန်းနောက်ရှိ မျိုးပြပစ္စည်းသည် ရှေ့နာမ်ပုဒ်၏ သက်ရှိ/သက်မဲ့၊ ပုံသဏ္ဌာန်၊ အမျိုးအစားနှင့် ကိုက်ညီရမည်ဖြစ်ရာ '{cRule.CorrectClassifier}' ကို သုံးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ပစ္စည်း ၉ မျိုး — မျိုးပြပစ္စည်း)");
                    }
                }

                if (ContainsMm(text, mmMask, "များ") || ContainsMm(text, mmMask, "တွေ"))
                {
                    foreach (Match m in ClassifierRedundantPluralPattern.Matches(text))
                    {
                        Group tGrp = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, tGrp.Index, tGrp.Length))
                            continue;
                        string clf = m.Groups["Clf"].Value;
                        string red = m.Groups["Redundant"].Value;
                        string collectiveAlt = clf + "တို့";
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = tGrp.Index,
                            Length = tGrp.Length,
                            OriginalText = tGrp.Value,
                            IssueType = ProofingIssueType.NumeralClassifierError,
                            Message = $"တိကျသော သင်္ချာမျိုးပြပစ္စည်း ('{clf}') နောက်တွင် အများပြပစ္စည်း '{red}' ထပ်မထည့်ရပါ — '{clf}' (သို့မဟုတ် အစုအပေါင်းပြ '{collectiveAlt}') ဟု သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"မြန်မာသဒ္ဒါအရ ကိန်းဂဏန်းနှင့် မျိုးပြပစ္စည်း ('{clf}') ဖြင့် အရေအတွက် အတိအကျ ဖော်ပြပြီးဖြစ်သော နာမ်ပုဒ်နောက်၌ မရေရာအများပြပစ္စည်း '{red}' ထပ်မလိုက်ရပါ (အစုအဖွဲ့ကို အတိအကျ ညွှန်းလိုပါက 'တို့' ကိုသာ သုံးနိုင်သည်)။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကိန်းနှင့် အများပြပစ္စည်း စည်းမျဉ်း)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = clf,
                                    MeaningCue = $"တိကျသော မျိုးပြပစ္စည်း သီးသန့် ('~{clf}')",
                                    PosTag = "CLF",
                                    IsOfficialThatpon = true,
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = collectiveAlt,
                                    MeaningCue = $"အစုအပေါင်းပြ ('~{collectiveAlt}')",
                                    PosTag = "CLF",
                                    IsOfficialThatpon = true,
                                    Score = 95.0
                                }
                            }
                        });
                    }
                }
            }

            if (hasTa && ContainsMm(text, mmMask, "တစ်ကောင်"))
            {
                foreach (Match m in ReciprocalHumanClassifierPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = g.Value.Replace("ကောင်", "ယောက်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.NumeralClassifierError,
                        $"လူပုဂ္ဂိုလ်ချင်း အပြန်အလှန်ပြုရာတွင် တိရစ္ဆာန်ပြ မျိုးပြပစ္စည်း 'ကောင်' အစား လူပုဂ္ဂိုလ်ပြ '{correct}' ဟု သုံးနှုန်းရမည်။",
                        "လူပုဂ္ဂိုလ် အပြန်အလှန်ပြပုဒ်", "PRON", true,
                        "ရှေ့တွင် လူပုဂ္ဂိုလ်ပြနာမ် ('မိတ်ဆွေ/သူငယ်ချင်း/လူသား...') ပါရှိသဖြင့် တိရစ္ဆာန်ပြ မျိုးပြပစ္စည်း 'ကောင်' မသုံးရဘဲ လူပုဂ္ဂိုလ်ပြ 'ယောက်/ဦး' ဖြင့် ဖွဲ့စည်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (မျိုးပြပစ္စည်း နှင့် ယဉ်ကျေးမှုအသုံးအနှုန်း)");
                }
            }

            // 4i. Audited Burmese Grammar Rules: Negation Framing, Experiential 'ဖူး', Nominalizer 'သောကြောင့်', Particle Chains & Postposition Frames
            bool hasBu = hasVisarga && ContainsMm(text, mmMask, "ဘူး");
            if (hasBu)
            {
                foreach (Match m in ExperientialBuInsteadOfPhuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဖူး",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အတွေ့အကြုံပြ ကြိယာထောက်ပစ္စည်းတွင် 'ဘူး' အစား 'ဖူး' ('{verb}ဖူး...') ဟု ရေးသားရမည်။",
                        "အတွေ့အကြုံပြ ကြိယာထောက်ပစ္စည်း ('~ဖူးသည်')", "PART", true,
                        $"ရှေ့၌ အငြင်းပြပစ္စည်း 'မ' မပါဘဲ နောက်၌ ဟုတ်မှန်/အမေး ကြိယာဝိဘတ် ဆက်လိုက်နေသဖြင့် အတိတ်အတွေ့အကြုံပြ ကြိယာထောက်ပစ္စည်း 'ဖူး' (ဖဦးထုပ်) ဖြစ်ရမည် ('ဘူး' သည် 'မ...ဘူး' အငြင်းပိတ်ပစ္စည်းသာ ဖြစ်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား (စာ-၄၀၈)");
                }
            }

            if (hasMa && (ContainsMm(text, mmMask, "တယ်") || ContainsMm(text, mmMask, "မယ်")))
            {
                foreach (Match m in NegatedVerbWithAffirmativeSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string correct = g.Value.StartsWith("ပါ", StringComparison.Ordinal) ? "ပါဘူး" : "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အငြင်းကြိယာ ('မ{verb}...') ကို ဟုတ်မှန်ပြဝိဘတ် '{g.Value}' ဖြင့် မပိတ်ရပါ — အငြင်းပိတ်ပစ္စည်း '{correct}' ကို သုံးရမည်။",
                        "အငြင်းဝါကျ အဆုံးသတ်ပစ္စည်း ('မ...ဘူး')", "PART", false,
                        $"မြန်မာသဒ္ဒါအရ 'မ' ဖြင့် ငြင်းပယ်ထားသော အပြောစကား ပင်မကြိယာကို ဟုတ်မှန်ကြောင်းပြဝိဘတ် ('တယ်/မယ်') ဖြင့် အဆုံးသတ်၍ မရပါ — 'ဘူး/ပါဘူး' (သို့မဟုတ် အရေးစကား 'ပါ') ဖြင့်သာ ပိတ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း 'မ...ဘူး / မ...ပါ')");
                }
            }

            if (hasBu)
            {
                foreach (Match m in UnpairedNegativeBuWithoutMaPattern.Matches(text))
                {
                    if (BareNegationPrefixCheckRegex.IsMatch(m.Groups["Clause"].Value))
                        continue;
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string posFix = verb + "တယ်";
                    string negFix = verb == "အရသာရှိ" ? "အရသာမရှိဘူး" : ("မ" + verb + "ဘူး");
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ရှေ့တွင် အငြင်းပြပစ္စည်း 'မ' မပါဘဲ အငြင်းပိတ်ပစ္စည်း 'ဘူး' သုံးထားပါသည် — အပြုသဘောဆိုလျှင် '{posFix}'၊ အငြင်းသဘောဆိုလျှင် '{negFix}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = "'ဘူး' သည် အငြင်းပြပစ္စည်း 'မ' နှင့် တွဲဖက်ရသော အငြင်းဝါကျနောက်ဆက် ('မ + ကြိယာ + ဘူး') ဖြစ်သဖြင့် 'မ' မပါဘဲ သီးခြားရပ်တည်၍ မရပါ။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း ဖွဲ့ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = false,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = negFix,
                                MeaningCue = "အငြင်းဝါကျ အပြည့်အစုံ ('မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = false,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            bool hasBareBuShort = ContainsMm(text, mmMask, "ဘူ");
            if (hasBu || hasPhu || hasBareBuShort)
            {
                foreach (Match m in DroppedInfixMaCompoundNegationPattern.Matches(text))
                {
                    string clausePrefix = TrimClauseAfterConjunction(m.Groups["Clause"].Value);
                    if (BareNegationPrefixCheckRegex.IsMatch(clausePrefix))
                        continue;

                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string head = m.Groups["Head"].Value;
                    string root = m.Groups["Root"].Value;
                    string aux = m.Groups["Aux"].Value;
                    string endToken = m.Groups["End"].Value;
                    bool hasSpellingCorruption = endToken == "ဖူး" || endToken == "ဘူ";

                    string formalAux = (aux == "နေ" || aux == "ပါ")
                        ? string.Empty
                        : (aux.EndsWith("ပါ", StringComparison.Ordinal) ? aux.Substring(0, aux.Length - 2) : aux);
                    string colloqClose = aux.EndsWith("ပါ", StringComparison.Ordinal) ? (aux + "ဘူး") : (aux + "ဘူး");
                    string formalNeg = head + "မ" + root + formalAux + "ပါ";
                    string colloqNeg = head + "မ" + root + colloqClose;
                    string primaryNeg = styleMode == ProofingStyleMode.FormalOfficial ? formalNeg : colloqNeg;
                    string secondaryNeg = styleMode == ProofingStyleMode.FormalOfficial ? colloqNeg : formalNeg;
                    string posFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (head + root + "ပါသည်")
                        : (head + root + "တယ်");

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = hasSpellingCorruption ? ProofingIssueType.SpellingError : ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ပေါင်းစပ်ကြိယာ '{head}{root}' ကို ငြင်းပယ်ရာတွင် အလယ်၌ အငြင်းပြ 'မ' ထည့်သွင်းရမည် — '{primaryNeg}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါ ပေါင်းစပ်ကြိယာဖွဲ့ထုံးအရ '{head} + {root}' အငြင်းပုံစံတွင် 'မ-' ကို ကြားညှပ်၍ '{primaryNeg}' ('{head} + မ + {root} + ဘူး/ပါ') ဟု ရေးသားရမည် ('{g.Value}' ဟု 'မ' ချန်လှပ်မရေးရပါ)။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ပေါင်းစပ်ကြိယာ အငြင်းဖွဲ့ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryNeg,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "ရုံးသုံး/စာပေ အငြင်းအရေးအသား"
                                    : "အငြင်းဝါကျ စံအရေးအသား ('~မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primaryNeg),
                                DiagnosticDetail = "အငြင်းပြ 'မ-' ဖြည့်စွက်ချက်",
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = secondaryNeg,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "အပြောစကား အငြင်းအမှန်"
                                    : "ရုံးသုံး/စာပေ အငြင်းအရေးအသား",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode != ProofingStyleMode.FormalOfficial,
                                Score = 95.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 90.0
                            }
                        }
                    });
                }

                foreach (Match m in DroppedMaColloquialNegationPattern.Matches(text))
                {
                    string clausePrefix = TrimClauseAfterConjunction(m.Groups["Clause"].Value);
                    if (BareNegationPrefixCheckRegex.IsMatch(clausePrefix))
                        continue;

                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    if (trie != null && trie.ContainsExactWord(g.Value))
                        continue;

                    string verb = m.Groups["Verb"].Value;
                    string aux = m.Groups["Aux"].Value;
                    string endToken = m.Groups["End"].Value;
                    bool hasSpellingCorruption = endToken == "ဖူး" || endToken == "ဘူ";

                    string formalAux = (aux == "နေ" || aux == "ပါ")
                        ? string.Empty
                        : (aux.EndsWith("ပါ", StringComparison.Ordinal) ? aux.Substring(0, aux.Length - 2) : aux);
                    string formalStem = (verb == "အား" && aux == "နေ") ? "အားလပ်" : verb;
                    string formalNeg = "မ" + formalStem + formalAux + "ပါ";
                    string colloqNeg = "မ" + verb + aux + "ဘူး";

                    string negFix = styleMode == ProofingStyleMode.FormalOfficial ? formalNeg : colloqNeg;
                    string altNegFix = styleMode == ProofingStyleMode.FormalOfficial ? colloqNeg : formalNeg;
                    string posFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (verb + "ပါသည်")
                        : (verb + "တယ်");

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = hasSpellingCorruption ? ProofingIssueType.SpellingError : ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အငြင်းပြရှေ့ဆက် 'မ' ကျန်ခဲ့ပါသည် — စံမြန်မာစာဖြင့် '{negFix}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"အပြောစကား၌ ရှေ့ဆက် 'မ-' ကို ချန်လှပ်၍ '{g.Value}' ဟု ပြောလေ့ရှိသော်လည်း စာအရေးအသားတွင် 'မ + ကြိယာ + ဘူး/ပါ' ဖွဲ့ထုံးအရ '{negFix}' ဟု အပြည့်အစုံ ရေးသားရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း 'မ...ဘူး / မ...ပါ')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = negFix,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "ရုံးသုံး/စာပေ အငြင်းအရေးအသား ('မ...ပါ')"
                                    : "အငြင်းဝါကျ စံအရေးအသား ('မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(negFix),
                                DiagnosticDetail = "အငြင်းပြရှေ့ဆက် 'မ-' ဖြည့်စွက်ချက်",
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = altNegFix,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "အပြောစကား အငြင်းအမှန် ('မ...ဘူး')"
                                    : "ရုံးသုံး/စာပေ အငြင်းအရေးအသား ('မ...ပါ')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode != ProofingStyleMode.FormalOfficial,
                                Score = 95.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 90.0
                            }
                        }
                    });
                }
            }

            if (hasMa && hasPaukMaGyi)
            {
                foreach (Match m in IncompleteColloquialNegationPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = g.Value + "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အပြောစကား အငြင်းဝါကျအဆုံးတွင် အငြင်းပိတ်ပစ္စည်း 'ဘူး' ကျန်ခဲ့ပါသည် — '{correct}' ဟု ရေးသားရမည်။",
                        "အပြောစကား အငြင်းဝါကျအဆုံးသတ် ('မ...ဘူး')", "V", false,
                        "ဝါကျအတွင်း အပြောစကားအသုံးအနှုန်း ('မှာ/တွေ/နဲ့') ပါရှိသော အငြင်းဝါကျဖြစ်သဖြင့် ကြိယာနောက်၌ 'ဘူး' (သို့မဟုတ် 'ပါဘူး') ထည့်သွင်းမှ ဝါကျပြည့်စုံမည် (စာပေစကားပြေ/ကဗျာ၌သာ 'မ+ကြိယာ။' သုံးလေ့ရှိသည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် Okell / Jenny Burmese Reference Grammar");
                }
            }

            if (ContainsMm(text, mmMask, "ကြောင့်"))
            {
                foreach (Match m in BareVerbBeforeKyauntPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string primaryFix = "သောကြောင့်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ကြိယာပုဒ် '{verb}' နောက်တွင် နာမ်ဝိဘတ် 'ကြောင့်' တိုက်ရိုက်မလိုက်ရပါ — '{verb}သောကြောင့်' သို့မဟုတ် '{verb}သဖြင့်' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"'{verb}' သည် ကြိယာပုဒ်ဖြစ်ပြီး 'ကြောင့်' သည် နာမ်/နာမ်စားနောက်၌သာ လိုက်ရသော အကြောင်းပြ နာမ်ဝိဘတ်ဖြစ်သဖြင့် နာမဝိသေသနပြုပစ္စည်း 'သော/သည့်' ခံ၍ 'သောကြောင့်' ဟု ဆက်စပ်ရမည် (သို့မဟုတ် ကြိယာဆက်သမ္ဗန္ဓ 'သဖြင့် / လို့' သုံးရမည်)။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် 'ကြောင့်' နှင့် ဝါကျဆက်သမ္ဗန္ဓ)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryFix,
                                MeaningCue = $"အကြောင်းပြသမ္ဗန္ဓ ({verb}~)",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = styleMode == ProofingStyleMode.FormalOfficial ? "သဖြင့်" : "လို့",
                                MeaningCue = "ကြိယာနောက်ဆက် အကြောင်းပြသမ္ဗန္ဓ",
                                PosTag = "CONJ",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            if (ContainsMm(text, mmMask, "အတွက်"))
            {
                foreach (Match m in BareNegatedVerbBeforeAtwetPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string verb = m.Groups["Verb"].Value;
                    string primaryFix = styleMode == ProofingStyleMode.FormalOfficial ? "သည့်အတွက်" : "တဲ့အတွက်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အငြင်းကြိယာ '{verb}' နောက်တွင် နာမ်ဝိဘတ် 'အတွက်' တိုက်ရိုက်မလိုက်ရပါ — '{verb}{primaryFix}' (သို့မဟုတ် '{verb}သောကြောင့်') ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"'{verb}' သည် ကြိယာပုဒ်ဖြစ်ပြီး 'အတွက်' သည် နာမ်/နာမ်စားနောက်၌သာ လိုက်နိုင်သော နာမ်ဝိဘတ်ဖြစ်သဖြင့် နာမဝိသေသနပြုပစ္စည်း 'သည့်/တဲ့' (သို့မဟုတ် နာမ်ပြုပစ္စည်း 'ခြင်း') ခံ၍ ဆက်စပ်ရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် 'အတွက်' နှင့် ကြိယာနာမ်ပြုဖွဲ့ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryFix,
                                MeaningCue = $"အကြောင်းရင်းပြ ဝါကျဆက် ({verb}~)",
                                PosTag = "PART",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = "သောကြောင့်",
                                MeaningCue = $"အကြောင်းပြသမ္ဗန္ဓ ({verb}သောကြောင့်)",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            if (ContainsMm(text, mmMask, "ကကို"))
            {
                foreach (Match m in AdjacentSubjObjCollisionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string pron = m.Groups["Pron"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ကို",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကတ္တားဝိဘတ် 'က' နှင့် ကံဝိဘတ် 'ကို' တစ်ဆက်တည်း ပူးတွဲသုံးစွဲ၍ မရပါ — '{pron}' ၏ ကာရကအနက်အလိုက် 'ကို' သို့မဟုတ် 'က' တစ်ခုတည်းသာ သုံးရမည်။",
                        "နာမ်ဝိဘတ် အမှန်", "PART", true,
                        "နာမ်စားတစ်ခုတည်းနောက်တွင် ပြုလုပ်သူပြ ကတ္တားဝိဘတ် ('က') နှင့် ခံရသူပြ ကံဝိဘတ် ('ကို') တို့သည် သဒ္ဒါတာဝန် ဆန့်ကျင်နေသဖြင့် ပူးတွဲမလိုက်နိုင်ပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် ၁၇ မျိုး)");
                }
            }

            if (ContainsMm(text, mmMask, "တယ်သည်") ||
                ContainsMm(text, mmMask, "သည်တယ်") ||
                ContainsMm(text, mmMask, "မယ်မည်") ||
                ContainsMm(text, mmMask, "မည်မယ်"))
            {
                foreach (Match m in MixedRegisterSfpCollisionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = styleMode == ProofingStyleMode.FormalOfficial
                        ? (v.Contains("မည်") ? "မည်" : (v.Contains("ပါ") ? "ပါသည်" : "သည်"))
                        : (v.Contains("မယ်") ? "မယ်" : (v.Contains("ပါ") ? "ပါတယ်" : "တယ်"));
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အပြောစကားနှင့် အရေးစကား ဝါကျအဆုံးသတ်ဝိဘတ် နှစ်ထပ်ရောထွေးနေပါသည် — '{correct}' တစ်ခုတည်းသာ သုံးရမည်။",
                        "ကြိယာဝိဘတ် အမှန်", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                        "ဝါကျအဆုံးတွင် အပြောစကားဝိဘတ် ('တယ်/မယ်') နှင့် အရေးစကားဝိဘတ် ('သည်/မည်') တို့ကို ထပ်ဆင့်တွဲစပ်၍ မရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိဘတ်)");
                }
            }

            if ((hasKhae && ContainsMm(text, mmMask, "ခဲ့ခဲ့")) ||
                (hasPyeeOpen && (ContainsMm(text, mmMask, "ပြီပြီ") || ContainsMm(text, mmMask, "သေးပြီ"))))
            {
                foreach (Match m in ContradictoryOrDoubledAspectPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = v == "ခဲ့ခဲ့" ? "ခဲ့" : "ပြီ";
                    string reason = v == "သေးပြီ"
                        ? "ဆက်လက်ဖြစ်ပေါ်ဆဲပြ 'သေး' နှင့် ပြီးစီးပြောင်းလဲပြ 'ပြီ' တို့သည် အနက်ဆန့်ကျင်နေသဖြင့် ပူးတွဲမသုံးရပါ။"
                        : $"ကြိယာနောက်ဆက် '{correct}' နှစ်ထပ်ဖြစ်နေသဖြင့် တစ်လုံးတည်းသာ ရေးရမည်။";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာနောက်ဆက် မှားယွင်းနေပါသည် — '{v}' အစား '{correct}' ဟု ရေးသားရမည်။",
                        "ကြိယာထောက်/နောက်ဆက် အမှန်", "PART", true,
                        reason,
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာပစ္စည်း)");
                }
            }

            if (hasKhae && ContainsMm(text, mmMask, "ပါခဲ့"))
            {
                foreach (Match m in NominalCopulaPastClashPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string numClf = m.Groups["NumClf"].Value;
                    string primaryFix = numClf + "ပါပဲ";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"နာမ်ပုဒ်နောက်တွင် ကြိယာမပါဘဲ အတိတ်ပြပစ္စည်း 'ခဲ့' တိုက်ရိုက်မကပ်ရပါ — '{primaryFix}' (သို့မဟုတ် '{numClf}ဖြစ်ခဲ့သည်') ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = "'ခဲ့' သည် ကြိယာနောက်၌သာ လိုက်နိုင်သော ကြိယာထောက်ပစ္စည်းဖြစ်သဖြင့် နာမ်/မျိုးပြပစ္စည်းနောက်ရှိ လေးနက်ပြပစ္စည်း 'ပါ' နောက်တွင် 'ဖြစ်-' ကြိယာမပါဘဲ '*ပါခဲ့' ဟု တွဲစပ်၍ မရပါ။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'ခဲ့' ၏ တည်နေရာစည်းမျဉ်း)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryFix,
                                MeaningCue = "နာမ်ဝါကျ လေးနက်ပြအဆုံးသတ် ('~ပါပဲ')",
                                PosTag = "PART",
                                IsOfficialThatpon = false,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = numClf + "ဖြစ်ခဲ့သည်",
                                MeaningCue = "အတိတ်ပြ ကြိယာဝါကျ ('~ဖြစ်ခဲ့သည်')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            bool hasKo = ContainsMm(text, mmMask, "ကို");
            bool hasMhar = ContainsMm(text, mmMask, "မှာ");

            if (hasKo && ContainsMm(text, mmMask, "ထဲကို"))
            {
                foreach (Match m in InteriorGoalWithStaticVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "တွင်" : "မှာ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ငြိမ်သက်တည်ရှိပြ ကြိယာရှေ့တွင် ရှေးရှုရာပြဝိဘတ် 'ကို' အစား နေရာပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        $"နေရာပြဝိဘတ် ('~{correct}')", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                        "နောက်ရှိကြိယာ ('အနားယူနေ/ထိုင်နေ/အိပ်နေ/ရှိနေ') သည် ရွေ့လျားသွားလာခြင်းမဟုတ်ဘဲ တစ်နေရာတည်း၌ တည်ရှိဖြစ်ပျက်နေမှုကို ပြသဖြင့် ရှေးရှုရာပြဝိဘတ် 'ကို' အစား နေရာပြဝိဘတ် 'မှာ/တွင်' သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နေရာပြဝိဘတ် နှင့် ရှေးရှုရာပြဝိဘတ်)");
                }
            }

            if (hasMhar && ContainsMm(text, mmMask, "သွား"))
            {
                foreach (Match m in StaticLocativeWithMotionVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "သို့" : "ကို";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဦးတည်ရွေ့လျားပြ ကြိယာ ('သွား-') ရှေ့တွင် တည်နေရာပြ 'မှာ' အစား ရှေးရှုရာပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        $"ရှေးရှုရာပြဝိဘတ် ('~{correct}')", "PART", true,
                        "'သွားသည်/သွားတယ်' သည် ဦးတည်ရာနေရာသို့ ရွေ့လျားမှုကိုပြသော ကြိယာဖြစ်သဖြင့် ငြိမ်သက်တည်နေရာပြ 'မှာ' အစား ရှေးရှုရာပြဝိဘတ် 'ကို/သို့' နှင့် တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ရှေးရှုရာပြဝိဘတ်)");
                }
            }

            if (hasKo && (ContainsMm(text, mmMask, "ထုတ်ယူ") || ContainsMm(text, mmMask, "ပြန်လည်ရယူ")))
            {
                foreach (Match m in SourceRepositoryKoInsteadOfMhaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "မှ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ထုတ်ယူ/ပြန်လည်ရယူရာ မူလရင်းမြစ်နေရာနောက်တွင် ကံဝိဘတ် 'ကို' အစား ထွက်ခွာရာပြဝိဘတ် 'မှ' ကို သုံးနှုန်းရမည်။",
                        "ထွက်ခွာရာပြဝိဘတ် ('~မှ')", "PART", true,
                        "နောက်တွင် ထုတ်ယူခံရသည့် တိုက်ရိုက်ကံပုဒ် သီးခြားပါရှိနေပြီဖြစ်သဖြင့် မူလသိုလှောင်ရာ/ရင်းမြစ်နေရာပုဒ်နောက်၌ ကံဝိဘတ် 'ကို' မသုံးရဘဲ ထွက်ခွာရာပြဝိဘတ် 'မှ' သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ထွက်ခွာရာပြဝိဘတ် 'မှ')");
                }
            }

            if (hasKo && ContainsMm(text, mmMask, "စစ်ဆေးမှု"))
            {
                foreach (Match m in RegionKoBeforeNominalizedInspectionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "တွင်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "'စစ်ဆေးမှု ပြုလုပ်-' ရှေ့ရှိ ဒေသအမည်နောက်တွင် 'ကို' အစား နေရာပြဝိဘတ် 'တွင်' ကို သုံးနှုန်းရမည်။",
                        "နေရာပြဝိဘတ် ('~တွင်')", "PART", true,
                        "'ပြုလုပ်-' ကြိယာ၏ တိုက်ရိုက်ကံပုဒ်အဖြစ် 'စစ်ဆေးမှု' ပါရှိနေပြီဖြစ်ရာ ရှေ့ရှိ ပြည်နယ်/မြို့နယ်ပုဒ်သည် လုပ်ငန်းဆောင်ရွက်ရာ တည်နေရာဖြစ်သဖြင့် နေရာပြဝိဘတ် 'တွင်' သုံးရမည် ('စစ်ဆေးခဲ့သည်' ဟု တိုက်ရိုက်ကြိယာသုံးမှသာ 'ကို' သုံးနိုင်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နေရာပြဝိဘတ် နှင့် ကံဝိဘတ် ခွဲခြားပုံ)");
                }
            }

            if (hasMhar && hasKhae)
            {
                foreach (Match m in BenefactiveMhaInsteadOfAtwetPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "အတွက်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ရည်ရွယ်ပေးအပ်/တင်သွင်းခြင်းကို ပြရာတွင် တည်နေရာပြ 'မှာ' အစား ရည်စူးချက်ပြဝိဘတ် 'အတွက်' (သို့မဟုတ် 'သို့') ကို သုံးနှုန်းရမည်။",
                        "ရည်စူးချက်ပြဝိဘတ် ('~အတွက်')", "PART", true,
                        "လေ့ကျင့်ခန်းပေးခြင်း သို့မဟုတ် အစီရင်ခံစာတင်သွင်းခြင်း၏ ရည်စူးရာ/ဦးတည်ရာဖြစ်သဖြင့် အပြောစကား နေရာပြ 'မှာ' အစား ရည်စူးချက်ပြဝိဘတ် 'အတွက်' (သို့မဟုတ် 'သို့') နှင့် တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ရည်စူးချက်ပြဝိဘတ် 'အတွက်')");
                }
            }

            if (hasKo && hasHorizSpace &&
                (ContainsMm(text, mmMask, "ကျွန်") ||
                 ContainsMm(text, mmMask, "အစ်")))
            {
                foreach (Match m in TripleKoSubjectConfusionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string subjFix = g.Value.Substring(0, g.Value.Length - 3) + "က";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, subjFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျ၏ ပြုလုပ်သူ (ကတ္တား) နောက်တွင် ကံဝိဘတ် 'ကို' မှားယွင်းသုံးထားပါသည် — '{subjFix}' ဟု ရေးသားရမည်။",
                        "ကတ္တားဝိဘတ် ('~က')", "PART", true,
                        "နောက်ပိုင်းတွင် ကံပုဒ်နှင့် လက်ခံသူပုဒ်များ ပါရှိနေပြီး ရှေ့ဆုံးနာမ်စားသည် ပြောဆို/ပေးပို့သူ ကတ္တားဖြစ်သဖြင့် ကတ္တားဝိဘတ် 'က' ကို သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် 'က')");
                }
            }

            // Universal 'မှ' (ထွက်ခွာရာပြဝိဘတ်) misused as 'က' (ကတ္တားဝိဘတ်) — e.g. 'နေပြည်တော်မှကြိုဆိုပါသည် -> နေပြည်တော်ကကြိုဆိုပါသည်', 'ဝန်ကြီးဌာနမှ အသိပေးသည် -> ဝန်ကြီးဌာနက'
            if (hasMa && ((mmMask & (1UL << ('\u103E' - '\u1000'))) != 0UL) && HasStandaloneMhaParticle(text))
            {
                if (ContainsMm(text, mmMask, "ကြိုဆို"))
                {
                    foreach (Match m in HostWelcomeMhaSubjectPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        string host = m.Groups["Host"].Value.Trim();
                        AddGrammarOrSpellingIssue(
                            issues, pGroup.Index, pGroup.Length, pGroup.Value, "က",
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"'{host}' သည် ကြိုဆိုသူ (ကတ္တား) ဖြစ်သဖြင့် ထွက်ခွာရာပြဝိဘတ် 'မှ' အစား ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည် (ဥပမာ - '{host}က ကြိုဆိုပါသည်')။",
                            "ကတ္တားဝိဘတ် (~က ကြိုဆိုပါသည်)", "PART", true,
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါအရ 'မှ' သည် နေရာ/အချိန်မှ စတင်ထွက်ခွာရာကိုပြသော ထွက်ခွာရာပြဝိဘတ်သာဖြစ်ပြီး၊ ကြိုဆိုမှုကို ပြုလုပ်သော မြို့/ဒေသ/အဖွဲ့အစည်း (ကတ္တား) နောက်တွင် ကတ္တားဝိဘတ် 'က' ကိုသာ သုံးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် 'က' နှင့် ထွက်ခွာရာပြဝိဘတ် 'မှ')");
                    }
                }

                foreach (Match m in AgentiveMhaSubjectPattern.Matches(text))
                {
                    Group pGroup = m.Groups["Particle"];
                    string verb = m.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, pGroup.Index, pGroup.Length, pGroup.Value, "က",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ပြုလုပ်သူ (ကတ္တား) နောက်တွင် ထွက်ခွာရာပြဝိဘတ် 'မှ' အစား ကတ္တားဝိဘတ် 'က' (သို့မဟုတ် 'သည်') ကို သုံးနှုန်းရမည်။",
                        $"ကတ္တားဝိဘတ် (~က {verb})", "PART", true,
                        $"မြန်မာသဒ္ဒါအရ 'မှ' သည် နေရာ/အချိန်မှ စတင်ထွက်ခွာရာကိုပြသော ထွက်ခွာရာပြဝိဘတ်သာဖြစ်ပြီး '{verb}-' မှုကို ပြုလုပ်သူ (ကတ္တား) နောက်၌ ကတ္တားဝိဘတ် 'က' သို့မဟုတ် 'သည်' ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ထွက်ခွာရာပြဝိဘတ်) နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            if (ContainsMm(text, mmMask, "လို့") && ContainsMm(text, mmMask, "မည်ဖြစ်သည်"))
            {
                foreach (Match m in ColloquialLoWithBureaucraticMyiPhitPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string correct = verb + "မယ်";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အပြောစကား 'လို့' ဝါကျခွဲနှင့် ရုံးသုံး 'မည်ဖြစ်သည်' စကားအဆင့် ရောနှောနေပါသည် — '{correct}' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                        "စကားအဆင့် ညီညွတ်သော အပြောစကားဝိဘတ်", "V", false,
                        "ဝါကျရှေ့ပိုင်း၌ အပြောစကား အကြောင်းပြပစ္စည်း ('...လို့') သုံးထားပါလျက် ဝါကျအဆုံး၌ ရုံးသုံးစာပေဟန် ('...မည်ဖြစ်သည်') သုံးထားသဖြင့် အပြော/အရေး စကားအဆင့် (Register) မညီညွတ်ပါ။",
                        "မောင်ခင်မင်(ဓနုဖြူ) — အပြောစကားနှင့် အရေးစကား ညီညွတ်မှုစည်းမျဉ်း");
                }
            }

            if (ContainsMm(text, mmMask, "တော်တော်") || ContainsMm(text, mmMask, "အူသွားတယ်"))
            {
                foreach (Match m in FormalContextSlangPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = v == "တော်တော်ဂွမ်း" ? "အလွန်အားနည်းစွာ"
                                   : v == "အူသွားတယ်" ? "ဝမ်းမြောက်ခဲ့ကြသည်"
                                   : "အလွန်ဆိုးရွားနေသည်";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စာပေ/သတင်း အရေးအသားအတွင်း ဗန်းစကား '{v}' ပါဝင်နေပါသည် — '{correct}' ဟု စံအရေးစကားဖြင့် ရေးသားရမည်။",
                        "စာပေ/ရုံးသုံး အရေးအသား", "ADV", true,
                        $"ဝါကျသည် စာတမ်း/သတင်း/ရုံးသုံး အရေးစကားပုံစံ ဖြစ်သဖြင့် အရပ်သုံးဗန်းစကား ('{v}') အစား အဓိပ္ပာယ်တိကျသော စာပေအသုံး ('{correct}') ကို သုံးရမည်။",
                        "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            // 4j. Universal Direct-Object 'အား -> ကို', Plural Verb 'ကျ -> ကြ', Modal 'သည့် -> သင့်' & Duplicated Function Words
            bool hasAh = ContainsMm(text, mmMask, "အား");
            if (hasAh)
            {
                foreach (Match m in PhysicalDirectObjectAhPattern.Matches(text))
                {
                    Group pGroup = m.Groups["Particle"];
                    string obj = m.Groups["Obj"].Value;
                    string verb = m.Groups["Verb"].Value;
                    int winStart = Math.Max(0, pGroup.Index - 20);
                    int winEnd = Math.Min(text.Length, m.Index + m.Length + 20);
                    string clauseWin = text.Substring(winStart, winEnd - winStart);
                    if (clauseWin.Contains("ကို") || clauseWin.Contains("စေ"))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, pGroup.Index, pGroup.Length, pGroup.Value, "ကို",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သက်မဲ့ကံပုဒ် '{obj}' နောက်တွင် တိုက်ရိုက်ကြိယာ '{verb}-' လိုက်သဖြင့် လက်ခံပြဝိဘတ် 'အား' အစား ကံဝိဘတ် 'ကို' သုံးနှုန်းရမည်။",
                        $"ကံဝိဘတ် ({obj}ကို {verb}-)", "PART", true,
                        $"မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါအရ 'အား' သည် ပေးကမ်းချီးမြှင့်ခြင်းကို လက်ခံရယူသူပြ လက်ခံပြဝိဘတ်ဖြစ်ပြီး '{obj}' ကို တိုက်ရိုက်ပြုလုပ်သော '{verb}-' ကြိယာရှေ့၌ ကံဝိဘတ် 'ကို' ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကံဝိဘတ် 'ကို' နှင့် လက်ခံပြဝိဘတ် 'အား')");
                }

                foreach (Match m in EventDocumentLawDirectObjectAhPattern.Matches(text))
                {
                    Group pGroup = m.Groups["Particle"];
                    if (OverlapsExistingIssue(issues, pGroup.Index, pGroup.Length))
                        continue;
                    string obj = m.Groups["Obj"].Value;
                    string verb = m.Groups["Verb"].Value;
                    int winStart = Math.Max(0, pGroup.Index - 24);
                    int winEnd = Math.Min(text.Length, m.Index + m.Length + 24);
                    string clauseWin = text.Substring(winStart, winEnd - winStart);
                    if (clauseWin.Contains("ကို") || clauseWin.Contains("စေ") || clauseWin.Contains("အဖြစ်"))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, pGroup.Index, pGroup.Length, pGroup.Value, "ကို",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သက်မဲ့ကံပုဒ် '{obj}' နောက်တွင် တိုက်ရိုက်ကံလိုကြိယာ '{verb}-' လိုက်သဖြင့် လက်ခံပြဝိဘတ် 'အား' အစား ကံဝိဘတ် 'ကို' သုံးနှုန်းရမည်။",
                        $"ကံဝိဘတ် ({obj}ကို {verb}-)", "PART", true,
                        $"မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါနှင့် ရုံးသုံးမြန်မာစာအရ 'အား' သည် ပေးကမ်း/လှူဒါန်းခြင်းကို လက်ခံရယူသူ၌သာ သုံးရသော လက်ခံပြဝိဘတ်ဖြစ်ပြီး၊ သက်မဲ့ '{obj}' ကို တိုက်ရိုက်ဆောင်ရွက်သော '{verb}-' ကြိယာရှေ့၌ ကံဝိဘတ် 'ကို' ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးသားနည်း (မောင်ခင်မင်-ဓနုဖြူ)");
                }
            }

            if (ContainsMm(text, mmMask, "ကျ"))
            {
                foreach (Match m in PluralVerbKyaMisusePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ကြ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အများပြ ကြိယာထောက်ပစ္စည်းတွင် 'ကျ' (ယပင့်) အစား 'ကြ' (ရရစ် — '{verb}ကြ...') ဟု ရေးသားရမည်။",
                        $"အများပြ ကြိယာထောက်ပစ္စည်း ({verb}ကြ~)", "PART", true,
                        $"'{verb}-' ကြိယာကို အများဝုစ်ဖြင့် ပြုလုပ်ကြောင်းပြရာ၌ အများပြ ကြိယာထောက်ပစ္စည်း 'ကြ' (ကကြီးရရစ်) ကို သုံးရမည် ('ကျ' သည် အောက်သို့ကျဆင်းခြင်း/သဘောကျခြင်း အနက်ဖြစ်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }

            if (hasCreakyDot && ContainsMm(text, mmMask, "သည့်ပါ"))
            {
                foreach (Match m in MisusedSinthBeforePoliteSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "သင့်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "သင့်လျော်ကြောင်းပြ ကြိယာထောက်ပစ္စည်းတွင် 'သည့်' အစား 'သင့်' (ငသတ် အောက်မြစ် — '~သင့်ပါသည်') ဟု ရေးသားရမည်။",
                        "သင့်လျော်ကြောင်းပြ ကြိယာထောက်ပစ္စည်း ('~သင့်ပါသည်')", "PART", true,
                        "နောက်တွင် ယဉ်ကျေးမှုပြပစ္စည်းနှင့် ဝါကျအဆုံးသတ်ဝိဘတ် ('ပါသည်/ပါတယ်') တိုက်ရိုက်လိုက်နေသဖြင့် နာမဝိသေသနပြုပစ္စည်း 'သည့်' မဖြစ်နိုင်ဘဲ သင့်လျော်ကြောင်းပြ ကြိယာထောက်ပစ္စည်း 'သင့်' ဖြစ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'သင့်')");
                }
            }

            if (hasMyanmarSpaceMyanmar && MayContainDuplicatedFunctionWord(text, mmMask))
            {
                foreach (Match m in DuplicatedFunctionWordPattern.Matches(text))
                {
                    string singleWord = m.Groups["Word"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, singleWord,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သဒ္ဒါပစ္စည်း '{singleWord}' နှစ်ထပ်ရိုက်မိနေပါသည် — '{singleWord}' တစ်ကြိမ်သာ ရေးသားရမည်။",
                        $"နှစ်ထပ်စာလုံး ဖယ်ရှားရန် ('{singleWord}')", "PART", true,
                        $"ဝိဘတ်/သမ္ဗန္ဓပစ္စည်း '{singleWord}' ကို Space ခြား၍ နှစ်ကြိမ်ထပ်ရေးမိထားသဖြင့် တစ်လုံးတည်းသာ ထားရှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ");
                }
            }

            // 4m. V42 Zero-Regex IndexOf Trie-POS Syntactic Homophone Disambiguation ('ကျောင်း->ကြောင်း', 'ချင်း->ခြင်း', generalized 'ကျ->ကြ')
            if (ContainsMm(text, mmMask, "ကျောင်း"))
            {
                ScanMisusedKyaungNominalizer(text, trie, issues);
            }

            if (trie != null && ContainsMm(text, mmMask, "ချင်း"))
            {
                ScanMisusedChinNominalizer(text, trie, issues);
            }

            if (trie != null && ContainsMm(text, mmMask, "ကျ"))
            {
                ScanMisusedKyaPluralVerb(text, trie, issues);
            }

            // 4n. V42 Smart Accidental Repeated-Word Detection (Myanmar multi-syllable words & English function words)
            if (hasMyanmarSpaceMyanmar && MayContainAdjacentDuplicateMyanmarWord(text))
            {
                foreach (Match m in DuplicatedMultiSyllableMyanmarWordPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;

                    string singleWord = m.Groups["Word"].Value;
                    if (!IsAccidentalDuplicateMyanmarWord(singleWord, trie))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, singleWord,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စကားလုံး '{singleWord}' ကို မတော်တဆ နှစ်ကြိမ်ထပ်ရိုက်မိနေပါသည် — '{singleWord}' တစ်ကြိမ်သာ ရေးသားရမည်။",
                        $"နှစ်ထပ်စာလုံး ဖယ်ရှားရန် ('{singleWord}')", "DUP", true,
                        $"စာရိုက်ရာတွင် သို့မဟုတ် ကူးယူရာတွင် '{singleWord}' ကို Space ခြား၍ နှစ်ကြိမ်ဆက်တိုက် ထပ်ရေးမိထားခြင်း ဖြစ်ပါသည်။",
                        "စာတမ်းတည်းဖြတ်ရေးနှင့် ရုံးသုံးမြန်မာစာ စံနှုန်း");
                }
            }

            if (hasHorizSpace && hasAsciiLetter && MayContainAdjacentDuplicateAsciiWord(text))
            {
                foreach (Match m in DuplicatedEnglishFunctionWordPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;

                    string singleWord = m.Groups["Word"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, singleWord,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အင်္ဂလိပ် သဒ္ဒါစာလုံး '{singleWord}' နှစ်ထပ်ရိုက်မိနေပါသည် — '{singleWord}' တစ်ကြိမ်သာ ထားရှိရမည်။",
                        $"Remove duplicate '{singleWord}'", "PART", true,
                        $"Duplicate function word '{m.Value}' detected across whitespace.",
                        "Bilingual Document Editing Standard");
                }
            }

            // 4k. V19 Deep Myanmar Grammar Pattern Checks (O(1) pre-filtered)
            // 4k-1. WH-Interrogative <-> Polar Question Particle Clash ('လား/သလား' vs 'လဲ/သလဲ/သနည်း')
            if (hasVisarga && ContainsMm(text, mmMask, "လား"))
            {
                foreach (Match m in WhInterrogativeWithPolarLarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string wh = m.Groups["Wh"].Value;
                    string raw = g.Value;
                    bool isLiteraryWh = wh.StartsWith("အဘယ်", StringComparison.Ordinal) || wh.StartsWith("မည်", StringComparison.Ordinal);
                    bool preferFormal = isLiteraryWh || styleMode == ProofingStyleMode.FormalOfficial;
                    string formalFix = raw == "ပါသလား" ? "ပါသနည်း"
                                     : raw == "သလား" ? "သနည်း"
                                     : raw == "ပါမလား" ? "ပါမည်နည်း"
                                     : raw == "မလား" ? "မည်နည်း"
                                     : "သနည်း";
                    string colloqFix = raw.Substring(0, raw.Length - 3) + "လဲ";
                    string primary = preferFormal ? formalFix : colloqFix;
                    string secondary = preferFormal ? colloqFix : formalFix;

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = raw,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အကြောင်းအရာမေးပုဒ် '{wh}' ပါသော မေးခွန်းဝါကျကို 'ဟုတ်/မဟုတ်' အမေးပစ္စည်း '{raw}' ဖြင့် မပိတ်ရပါ — '{primary}' ဟု သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါအရ '{wh}' သည် အချက်အလက်/အကြောင်းရင်းကို ဖြေဆိုရသော အမေးပုဒ်ဖြစ်သဖြင့် ဟုတ်/မဟုတ် မေးခွန်းပိတ်ပစ္စည်း 'လား/သလား' နှင့် သဒ္ဒါသဘော ဆန့်ကျင်နေပါသည် — အကြောင်းအရာအမေးပိတ်ပစ္စည်း '{primary}' ('လဲ/သနည်း') ကိုသာ သုံးရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အမေးပစ္စည်း 'လား'၊ 'လဲ'၊ 'သနည်း' ခွဲခြားပုံ)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primary,
                                MeaningCue = preferFormal ? "စာပေ/ရုံးသုံး အမေးပိတ်ပစ္စည်း ('~နည်း')" : "အကြောင်းအရာမေးပစ္စည်း ('~လဲ')",
                                PosTag = "PART",
                                IsOfficialThatpon = preferFormal,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = secondary,
                                MeaningCue = preferFormal ? "အပြောစကား အမေးပိတ်ပစ္စည်း ('~လဲ')" : "စာပေသုံး အမေးပိတ်ပစ္စည်း ('~နည်း')",
                                PosTag = "PART",
                                IsOfficialThatpon = !preferFormal,
                                Score = 94.0
                            }
                        }
                    });
                }
            }

            if (hasLae)
            {
                foreach (Match m in LiteraryWhWithColloquialLaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string wh = m.Groups["Wh"].Value;
                    string raw = g.Value;
                    string formalFix = raw == "ပါသလဲ" ? "ပါသနည်း"
                                     : raw == "သလဲ" ? "သနည်း"
                                     : raw == "ပါမလဲ" ? "ပါမည်နည်း"
                                     : raw == "မလဲ" ? "မည်နည်း"
                                     : "သနည်း";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, formalFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စာပေသုံး အမေးပုဒ် '{wh}' နှင့် အပြောစကား '{raw}' စကားအဆင့် ရောထွေးနေပါသည် — '{formalFix}' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                        "စာပေသုံး အမေးပိတ်ပစ္စည်း ('~နည်း')", "PART", true,
                        $"ရှေ့တွင် စာပေအရေးစကား အမေးနာမ်စား '{wh}' သုံးထားသဖြင့် ဝါကျအဆုံး၌လည်း စာပေသုံး အမေးပစ္စည်း '{formalFix}' ဖြင့် စကားအဆင့် ညှိနှိုင်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            // 4k-2. Oblique Creaky-Tone Pronoun Case Discord & Phonologically Illegal Creaky Pronouns
            if (hasCreakyDot)
            {
                foreach (Match m in CreakyPronounBeforeSubjectOrPluralPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string rawPron = m.Groups["RawPron"].Success ? m.Groups["RawPron"].Value : m.Groups["RawPron2"].Value;
                    string basePron = rawPron.Replace("\u1037", string.Empty);
                    string post = m.Groups["Post"].Success ? m.Groups["Post"].Value : m.Groups["Post2"].Value;
                    string correct = basePron + post;
                    string roleDesc = post == "တို့" ? "အများပြပစ္စည်း 'တို့'" : $"ကတ္တားဝိဘတ် '{post}'";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"{roleDesc} ရှေ့ရှိ နာမ်စားတွင် အောက်မြစ်သံပြောင်း မထည့်ရပါ — '{g.Value}' အစား '{correct}' ဟု ရေးသားရမည်။",
                        $"မူလသံ နာမ်စား + {roleDesc} ('{correct}')", "PRON", true,
                        $"မြန်မာသဒ္ဒါ နာမ်စားစည်းမျဉ်းအရ ပိုင်ဆိုင်ပြနှင့် ကံ/လက်ခံဝိဘတ် ('ကို/အား/အတွက်') ရှေ့၌သာ အောက်မြစ်သံပြောင်း ('{rawPron}') ဖြစ်ပေါ်ပြီး၊ {roleDesc} ရှေ့တွင် မူလသံ '{basePron}' အတိုင်း '{correct}' ဟုသာ ရေးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်စား အောက်မြစ်သံပြောင်း စည်းမျဉ်း)");
                }

                foreach (Match m in IllegalPronounCreakyTonePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string basePron = g.Value.Replace("\u1037", string.Empty);
                    string possFix = styleMode == ProofingStyleMode.FormalOfficial ? (basePron + "၏") : (basePron + "ရဲ့");
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"နာမ်စား '{basePron}' တွင် အောက်မြစ် ('့') ထပ်မထည့်ရပါ — '{basePron}' (သို့မဟုတ် ပိုင်ဆိုင်ပြ '{possFix}') ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = basePron == "ကျွန်ုပ်"
                            ? "'ကျွန်ုပ်' သည် ပသတ် ('ပ်') အသတ်ပါသော ရပ်သံဝဏ္ဏဖြစ်သဖြင့် အောက်မြစ် ('့') လုံးဝ မတင်နိုင်ပါ (ပိုင်ဆိုင်မှုပြလိုပါက 'ကျွန်ုပ်၏' ဟု ရေးရသည်)။"
                            : $"'{basePron}' ၏ နောက်ဆုံးဝဏ္ဏ 'မ' သည် ပင်ကိုသရတို အောက်မြစ်သံ ပါပြီးဖြစ်သဖြင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ် ('့') ထပ်မထည့်ရပါ။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = basePron,
                                MeaningCue = $"စံနာမ်စား စာလုံးပေါင်း ('{basePron}')",
                                PosTag = "PRON",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = possFix,
                                MeaningCue = $"ပိုင်ဆိုင်ပြပုဒ် ('{possFix}')",
                                PosTag = "PRON",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            // 4k-3. Pleonastic & Hybrid Grammatical Constructions ('မတိုင်မီခင်', 'အလို့ငှာအတွက်', 'သဖြင့်ကြောင့်', 'များတွေ', 'တွင်၌', etc.)
            if (HasAnySubstring(text, mmMask, PleonasticTriggers))
            {
                foreach (Match m in PleonasticHybridGrammarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string p1;
                    string p2;
                    switch (raw)
                    {
                        case "မတိုင်မီခင်":
                        case "မတိုင်ခင်မီ":
                            p1 = styleMode == ProofingStyleMode.FormalOfficial ? "မတိုင်မီ" : "မတိုင်ခင်";
                            p2 = styleMode == ProofingStyleMode.FormalOfficial ? "မတိုင်ခင်" : "မတိုင်မီ";
                            break;
                        case "အလို့ငှာအတွက်":
                            p1 = "အတွက်";
                            p2 = "အလို့ငှာ";
                            break;
                        case "သဖြင့်ကြောင့်":
                            p1 = "သောကြောင့်";
                            p2 = "သဖြင့်";
                            break;
                        case "သောကြောင့်မို့လို့":
                        case "သောကြောင့်မို့":
                            p1 = "သောကြောင့်";
                            p2 = "ဒါကြောင့်မို့";
                            break;
                        case "သို့သော်လည်းပဲ":
                        case "သို့သော်ငြားလည်းပဲ":
                            p1 = "သို့သော်လည်း";
                            p2 = "ဒါပေမဲ့";
                            break;
                        case "များတွေ":
                            p1 = styleMode == ProofingStyleMode.FormalOfficial ? "များ" : "တွေ";
                            p2 = styleMode == ProofingStyleMode.FormalOfficial ? "တွေ" : "များ";
                            break;
                        case "တွင်၌":
                        case "၌တွင်":
                            p1 = "တွင်";
                            p2 = "၌";
                            break;
                        case "သို့ကို":
                            p1 = "သို့";
                            p2 = "ကို";
                            break;
                        case "ဖြင့်နှင့်":
                        case "နှင့်ဖြင့်":
                            p1 = "ဖြင့်";
                            p2 = "နှင့်";
                            break;
                        default: // မှကနေ
                            p1 = styleMode == ProofingStyleMode.FormalOfficial ? "မှ" : "ကနေ";
                            p2 = styleMode == ProofingStyleMode.FormalOfficial ? "ကနေ" : "မှ";
                            break;
                    }

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = raw,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အနက်တူ သဒ္ဒါပစ္စည်းနှစ်ခု ထပ်ဆင့်ရောထွေးနေပါသည် ('{raw}') — '{p1}' သို့မဟုတ် '{p2}' တစ်ခုတည်းကိုသာ သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = $"'{raw}' သည် တာဝန်တူ ဝိဘတ်/သမ္ဗန္ဓပစ္စည်းနှစ်ခု ('{p1}' နှင့် '{p2}') ကို နှစ်ထပ်တွဲစပ်ထားသော သဒ္ဒါအနက်ထပ် (Pleonasm) အမှားဖြစ်သဖြင့် စကားအဆင့်နှင့် လျော်ညီသော တစ်ခုတည်းကိုသာ ရွေးချယ်ရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) — သဒ္ဒါအနက်ထပ် အမှားများ",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = p1,
                                MeaningCue = $"ကျစ်လျစ်မှန်ကန်သော သဒ္ဒါပုဒ် ('{p1}')",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = p2,
                                MeaningCue = $"အခြားရွေးချယ်ရန် ပုဒ် ('{p2}')",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                Score = 94.0
                            }
                        }
                    });
                }
            }

            // 4k-4. Adverbial 'စသဖြင့်' misused before nominal plural/case postpositions -> 'စသည်...'
            bool hasSaThaPhint = hasCreakyDot && ContainsMm(text, mmMask, "သဖြင့်");
            if (hasSaThaPhint && ContainsMm(text, mmMask, "စသဖြင့်"))
            {
                foreach (Match m in MisusedSaThaPhintNominalPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string post = m.Groups["Post"].Value;
                    string correct = "စသည်" + post;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"'စသဖြင့်' နောက်တွင် နာမ်ပစ္စည်း/နာမ်ဝိဘတ် '{post}' တိုက်ရိုက်မလိုက်ရပါ — '{correct}' ဟု ရေးသားရမည်။",
                        $"နာမ်ပုဒ်တွဲ အမှန် ('{correct}')", "PART", true,
                        "'စသဖြင့်' တွင် အသုံးခံပြဝိဘတ် 'ဖြင့်' ပါဝင်ပြီးဖြစ်၍ ကြိယာဝိသေသနအဖြစ် ကြိယာရှေ့၌သာ သုံးနိုင်သည် — နောက်မှ 'တို့/များ/ကို/၌' စသော နာမ်ပစ္စည်း/နာမ်ဝိဘတ် ဆက်လိုပါက နာမ်သဘောရှိသော 'စသည်' ဖြင့်သာ တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးသားနည်း");
                }
            }

            // 4k-5. Correlative 'အကယ်၍ ... သောကြောင့်/သဖြင့်' -> 'ပါက/လျှင်', & Dangling Causal Medial Fragment ('...သဖြင့်။')
            if (ContainsMm(text, mmMask, "အကယ်၍"))
            {
                foreach (Match m in HypotheticalAkaeYweWithCausalPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string p1 = styleMode == ProofingStyleMode.FormalOfficial ? "ပါက" : "လျှင်";
                    string p2 = styleMode == ProofingStyleMode.FormalOfficial ? "လျှင်" : "ပါက";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ရှေ့တွင် ခြွင်းချက်ပြ 'အကယ်၍' ပါရှိသဖြင့် အကြောင်းပြ '{g.Value}' အစား ခြွင်းချက်ပြသမ္ဗန္ဓ '{p1}' သို့မဟုတ် '{p2}' နှင့် တွဲဖက်ရမည်။",
                        GrammaticalReasoning = "မြန်မာသဒ္ဒါ အရှေ့အနောက် တွဲဖက်သမ္ဗန္ဓစည်းမျဉ်းအရ 'အကယ်၍' သည် ဖြစ်နိုင်ခြေ/ခြွင်းချက်ကိုပြသော ပုဒ်ဖြစ်ရာ နောက်ခံကြိယာကို 'လျှင် / ပါက / သော်' ဖြင့်သာ ပိတ်ရမည် ('သောကြောင့် / သဖြင့်' နှင့် မတွဲဖက်ရပါ)။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သမ္ဗန္ဓပစ္စည်း တွဲဖက်ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = p1,
                                MeaningCue = $"ခြွင်းချက်ပြသမ္ဗန္ဓ ('အကယ်၍ ... {p1}')",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = p2,
                                MeaningCue = $"ခြွင်းချက်ပြသမ္ဗန္ဓ ('အကယ်၍ ... {p2}')",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            if (hasPaukMaGyi && hasSaThaPhint)
            {
                foreach (Match m in DanglingCausalMedialSentenceFragmentPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "သောကြောင့် ဖြစ်ပါသည်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ဝါကျဆက်သမ္ဗန္ဓ 'သဖြင့်' ဖြင့် ဝါကျကို ပင်မကြိယာမပါဘဲ အဆုံးသတ်ထားပါသည် — 'သောကြောင့် ဖြစ်ပါသည်' ဟု ပြည့်စုံစွာ အဆုံးသတ်ရမည်။",
                        "ပြည့်စုံသော အကြောင်းပြဝါကျ အဆုံးသတ်", "CONJ", true,
                        "မြန်မာသဒ္ဒါ ဝါကျဖွဲ့ထုံးအရ 'သဖြင့်' သည် နောက်မှ ပင်မဝါကျ ဆက်လိုက်ရမည့် ဝါကျခွဲဆက်သမ္ဗန္ဓဖြစ်သောကြောင့် ၎င်းနောက်၌ ပုဒ်မကြီး ('။') တိုက်ရိုက်ချ၍ ဝါကျမပိတ်ရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဝါကျဖွဲ့ထုံး နှင့် သမ္ဗန္ဓပစ္စည်း)");
                }
            }

            // 4k-6. Clause-Internal Register Disharmony (Universal: catches stark literary+colloquial clash even in GeneralStandard)
            if ((hasBu || ContainsMm(text, mmMask, "ပါတယ်") || ContainsMm(text, mmMask, "ပါမယ်")) &&
                (ContainsMm(text, mmMask, "၌") ||
                 hasSaThaPhint ||
                 ContainsMm(text, mmMask, "အလို့ငှာ") ||
                 (hasLaKaungTrigger && ContainsMm(text, mmMask, "၎င်း"))))
            {
                foreach (Match m in PureLiteraryMarkerWithColloquialSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string litFix = raw == "ပါတယ်" ? "ပါသည်"
                                  : raw == "ပါမယ်" ? "ပါမည်"
                                  : raw == "မဟုတ်ဘူး" ? "မဟုတ်ပါ"
                                  : "ပါ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, litFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျအတွင်း စာပေသုံးပစ္စည်း ('၌ / သဖြင့် / အလို့ငှာ / ၎င်းသည်') နှင့် အပြောစကားအဆုံးသတ် '{raw}' ရောထွေးနေပါသည် — '{litFix}' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                        $"စာပေဟန် ညီညွတ်သော ကြိယာဝိဘတ် ('{litFix}')", "PART", true,
                        $"တစ်ဝါကျတည်းအတွင်း စာပေသီးသန့်ပစ္စည်းနှင့် အပြောစကားဝိဘတ် '{raw}' ရောနှောသုံးစွဲခြင်းသည် စကားအဆင့် (Register) မညီညွတ်သည့် သဒ္ဒါအမှားဖြစ်သဖြင့် '{litFix}' ဖြင့် ညှိနှိုင်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) — အရေး/အပြော ညီညွတ်မှုစည်းမျဉ်း");
                }
            }

            if (ContainsMm(text, mmMask, "ပေသည်") ||
                ContainsMm(text, mmMask, "ချေသည်") ||
                ContainsMm(text, mmMask, "ပါ၏") ||
                ContainsMm(text, mmMask, "သတည်း"))
            {
                foreach (Match m in ColloquialMarkerWithClassicalSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string litFix = raw == "တွေ" ? "များ"
                                  : raw == "နဲ့" ? "နှင့်"
                                  : raw == "ဒီနေ့" ? "ယနေ့"
                                  : "ထို";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, litFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စာပေအရေးအသားဝါကျ ('~ပေသည်/ချေသည်/ပါ၏') အတွင်း အပြောစကား '{raw}' ရောထွေးနေပါသည် — '{litFix}' ဟု သုံးနှုန်းရမည်။",
                        $"စာပေသုံး စံပုဒ် ('{litFix}')", "PART", true,
                        $"ဝါကျအဆုံး၌ စာပေဟန်မြင့် ကြိယာဝိဘတ် သုံးထားသဖြင့် ဝါကျအတွင်းရှိ အပြောစကား '{raw}' ကို စာပေသုံး '{litFix}' သို့ ပြောင်းလဲညှိနှိုင်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            // 4k-7. Monastic / Clergy Honorific Subject–Verb Concord
            if (ContainsMm(text, mmMask, "ဆရာတော်") ||
                ContainsMm(text, mmMask, "သံဃာတော်") ||
                ContainsMm(text, mmMask, "ရဟန်းတော်") ||
                ContainsMm(text, mmMask, "ဘုန်းတော်ကြီး"))
            {
                foreach (Match m in MonasticHonorificVerbConcordPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string subj = m.Groups["Subj"].Value;
                    string layVerb = g.Value;
                    string h1;
                    string h2;
                    switch (layVerb)
                    {
                        case "ထမင်းစား":
                            h1 = "ဆွမ်းဘုဉ်းပေး";
                            h2 = "ဆွမ်းသုံးဆောင်";
                            break;
                        case "သေဆုံး":
                        case "ကွယ်လွန်":
                            h1 = "ပျံလွန်တော်မူ";
                            h2 = "ဘဝနတ်ထံပျံလွန်တော်မူ";
                            break;
                        case "အိပ်စက်":
                        case "အိပ်ပျော်":
                            h1 = "ကျိန်းစက်တော်မူ";
                            h2 = "ကျိန်းစက်";
                            break;
                        default: // နေထိုင်
                            h1 = "သီတင်းသုံး";
                            h2 = "သီတင်းသုံးနေထိုင်";
                            break;
                    }

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = layVerb,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ရဟန်းသံဃာ ကတ္တားပုဒ် ('{subj}') နှင့် လူဝတ်ကြောင်သုံးကြိယာ '{layVerb}-' မကိုက်ညီပါ — ရဟန်းသုံး ဂါရဝပြုကြိယာ '{h1}' ကို သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါနှင့် ရဟန်းသံဃာသုံး ဝေါဟာရစည်းမျဉ်းအရ ကတ္တားသည် '{subj}' ဖြစ်သောအခါ သာမန်လူပုဂ္ဂိုလ်သုံး '{layVerb}-' အစား သာသနာ့ဝေါဟာရ '{h1}' သို့မဟုတ် '{h2}' ဖြင့် ကတ္တား-ကြိယာ အဆင့်အတန်း ညှိနှိုင်းရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံး/သာသနာရေး ဂါရဝပြုဝေါဟာရစံနှုန်း",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = h1,
                                MeaningCue = $"ရဟန်းသံဃာသုံး ဂါရဝပြုကြိယာ ('{h1}~')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = h2,
                                MeaningCue = $"ရဟန်းသုံး အခြားရွေးချယ်ရန်ပုဒ် ('{h2}~')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            // 4k-8. Auxiliary Verb Particle Slot Inversion ('*ဆောင်ရွက်ပါနိုင်သည် -> ဆောင်ရွက်နိုင်ပါသည်', '*သိရှိစေပါလိုသည် -> သိရှိစေလိုပါသည်', '*ခဲ့နေသည် -> နေခဲ့သည်')
            if (ContainsMm(text, mmMask, "ပါနိုင်") ||
                ContainsMm(text, mmMask, "ပါချင်") ||
                ContainsMm(text, mmMask, "ပါလို") ||
                ContainsMm(text, mmMask, "ပါသင့်"))
            {
                foreach (Match m in InvertedPoliteModalParticlePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string correct = m.Groups["Target"].Success
                        ? (m.Groups["Modal"].Value + "ပါ")
                        : "စေလိုပါ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာထောက်ပစ္စည်း အစီအစဉ် လွဲမှားနေပါသည် — '{raw}' အစား '{correct}' ဟု ရှေ့နောက်အစီအစဉ်မှန်ဖြင့် ရေးသားရမည်။",
                        $"ကြိယာထောက်ပစ္စည်း အစီအစဉ်မှန် ('~{correct}')", "PART", true,
                        $"မြန်မာသဒ္ဒါ ကြိယာပစ္စည်းစဉ်ထုံးအရ ယဉ်ကျေးမှုပြပစ္စည်း 'ပါ' သည် စွမ်းဆောင်နိုင်မှု/အလိုဆန္ဒ/သင့်လျော်မှုပြ ကြိယာထောက်ပစ္စည်း ('နိုင်/ချင်/လို/သင့်') ၏ နောက်၌သာ လိုက်ရသဖြင့် '{correct}' ဟု စီစဉ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း အစီအစဉ်စည်းမျဉ်း)");
                }
            }

            if (hasKhae && ContainsMm(text, mmMask, "ခဲ့နေ"))
            {
                foreach (Match m in InvertedPastProgressiveParticlePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "နေခဲ့",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ကြိယာထောက်ပစ္စည်း အစီအစဉ် လွဲမှားနေပါသည် — 'ခဲ့နေ' အစား 'နေခဲ့' ('~နေခဲ့သည်') ဟု ရေးသားရမည်။",
                        "ဖြစ်ပေါ်ဆဲအတိတ်ပြ အစီအစဉ်မှန် ('~နေခဲ့')", "PART", true,
                        "မြန်မာသဒ္ဒါ ကြိယာထောက်ပစ္စည်းစဉ်ထုံးအရ ဖြစ်ဆဲပြ 'နေ' သည် အတိတ်ပြ 'ခဲ့' ရှေ့တွင် တည်ရှိရသဖြင့် 'နေခဲ့' ဟုသာ တွဲစပ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း အစီအစဉ်)");
                }
            }

            // 4h. Broken Space-Split Mandatory Compounds ('စာ ကြည့်တိုက်' -> 'စာကြည့်တိုက်', 'အစည်း အဝေး' -> 'အစည်းအဝေး')
            if (hasMyanmarSpaceMyanmar)
            {
                foreach (Match m3 in ThreePartBrokenCompoundPattern.Matches(text))
                {
                    string joined3 = UnicodeNormalizer.NormalizeCanonicalOrder(
                        m3.Groups["P1"].Value + m3.Groups["P2"].Value + m3.Groups["P3"].Value);
                    if (VerifiedMandatoryCompounds.Contains(joined3))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, m3.Index, m3.Length, m3.Value, joined3,
                            ProofingIssueType.SpellingError,
                            $"တစ်ပေါင်းတည်း ရေးသားရမည့် ပေါင်းစပ်ပုဒ် ဖြစ်ပါသည် — '{m3.Value}' အစား '{joined3}' ဟု ခြားမရေးဘဲ ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် ပေါင်းစပ်ပုဒ် (သတ်ပုံကျမ်း)", "N", true);
                    }
                }

                foreach (Match m in CuratedBrokenCompoundPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                    if (VerifiedMandatoryCompounds.Contains(joined))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, joined,
                            ProofingIssueType.SpellingError,
                            $"တစ်ပေါင်းတည်း ရေးသားရမည့် ပေါင်းစပ်ပုဒ် ဖြစ်ပါသည် — '{m.Value}' အစား '{joined}' ဟု ခြားမရေးဘဲ ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် ပေါင်းစပ်ပုဒ် (သတ်ပုံကျမ်း)", "N", true);
                    }
                }

                foreach (Match m in BoundStemBrokenCompoundPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                    if (VerifiedMandatoryCompounds.Contains(joined))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, joined,
                            ProofingIssueType.SpellingError,
                            $"တစ်ပေါင်းတည်း ရေးသားရမည့် ပေါင်းစပ်ပုဒ် ဖြစ်ပါသည် — '{m.Value}' အစား '{joined}' ဟု ခြားမရေးဘဲ ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် ပေါင်းစပ်ပုဒ် (သတ်ပုံကျမ်း)", "N", true);
                    }
                }

                foreach (Match m in BoundSuffixBrokenSpacePattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, joined,
                        ProofingIssueType.SpellingError,
                        $"နောက်ဆက်ပစ္စည်းနှင့် ပင်မပုဒ်ကြား နေရာလွတ် (Space) မခြားရပါ — '{m.Value}' အစား '{joined}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                        "တစ်ဆက်တည်းရေးရမည့် နောက်ဆက်တွဲပုဒ်", "PART", true);
                }

                if (styleMode == ProofingStyleMode.FormalOfficial && ContainsMm(text, mmMask, "အစိုးရ"))
                {
                    foreach (Match mGov in FormalCabinetBrokenCompoundPattern.Matches(text))
                    {
                        if (!OverlapsExistingIssue(issues, mGov.Index, mGov.Length))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, mGov.Index, mGov.Length, mGov.Value, "အစိုးရအဖွဲ့",
                                ProofingIssueType.SpellingError,
                                $"ရုံးသုံးပေါင်းစပ်ပုဒ် '{mGov.Value}' ကို 'အစိုးရအဖွဲ့' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                                "ရုံးသုံးပေါင်းစပ်ပုဒ် ('အစိုးရအဖွဲ့')", "N", true);
                        }
                    }
                }

                for (int cIdx = 0; cIdx < CuratedAddressAndHistoricalBrokenCompounds.Length; cIdx++)
                {
                    var (pat, targetJoined, cue) = CuratedAddressAndHistoricalBrokenCompounds[cIdx];
                    foreach (Match m in pat.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, m.Index, m.Length))
                            continue;
                        string normTarget = UnicodeNormalizer.NormalizeCanonicalOrder(targetJoined);
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, normTarget,
                            ProofingIssueType.SpellingError,
                            $"စာလုံးအတွင်း နေရာလွတ် (Space) ခြားထားခြင်း သို့မဟုတ် စာလုံးပေါင်း လွဲမှားနေပါသည် — '{m.Value}' အစား '{normTarget}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            cue, "PROPN", true);
                    }
                }

                if (trie != null && hasShortMyanmarBeforeMyanmar)
                {
                    foreach (Match m in SpaceSplitPairCandidateRegex.Matches(text))
                    {
                        Group tGrp = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, tGrp.Index, tGrp.Length))
                            continue;

                        Group leftGrp = m.Groups["Left"];
                        if (text[leftGrp.Index] == 'မ')
                            continue;

                        string leftNorm = UnicodeNormalizer.NormalizeCanonicalOrder(leftGrp.Value);
                        if (SpaceSplitBoundaryParticles.Contains(leftNorm) || leftNorm.StartsWith("မ", StringComparison.Ordinal))
                            continue;

                        string rightNorm = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Right"].Value);
                        if (SpaceSplitBoundaryParticles.Contains(rightNorm))
                            continue;

                        string suffixVal = m.Groups["Suffix"].Success
                            ? UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Suffix"].Value)
                            : string.Empty;
                        if (suffixVal.Length > 0 &&
                            trie.ContainsExactWord(leftNorm) &&
                            trie.ContainsExactWord(rightNorm + suffixVal))
                        {
                            continue;
                        }

                        string rawJoined = leftNorm + rightNorm;
                        string joinedNorm = (rightNorm.Length > 0 && rightNorm[0] >= '\u102B' && rightNorm[0] <= '\u103E')
                            ? UnicodeNormalizer.NormalizeCanonicalOrder(rawJoined)
                            : rawJoined;
                        if (VerifiedMandatoryCompounds.Contains(joinedNorm))
                            continue;

                        // Check O(1) dictionary compound existence BEFORE breaking syllables!
                        if (!trie.TryGetExactEntries(joinedNorm, out var jEntries) ||
                            jEntries == null || jEntries.Count == 0 ||
                            (!jEntries[0].IsOfficialThatpon && !jEntries[0].IsProperNoun) ||
                            jEntries[0].Frequency < 20)
                        {
                            continue;
                        }

                        if (trie.ContainsExactWord(leftNorm) && trie.ContainsExactWord(rightNorm))
                            continue;

                        int leftSyls = MyanmarSyllableBreaker.BreakIntoSyllables(leftNorm, isPreNormalized: true).Count;
                        int rightSyls = MyanmarSyllableBreaker.BreakIntoSyllables(rightNorm, isPreNormalized: true).Count;
                        if (leftSyls == 0 || rightSyls == 0 || leftSyls > 2 || rightSyls > 2)
                            continue;

                        string cue = !string.IsNullOrWhiteSpace(jEntries[0].MeaningCue) ? jEntries[0].MeaningCue : "သတ်ပုံကျမ်းဝင် ပေါင်းစပ်ပုဒ်";
                        AddGrammarOrSpellingIssue(
                            issues, tGrp.Index, tGrp.Length, tGrp.Value, joinedNorm,
                            ProofingIssueType.SpellingError,
                            $"စာလုံးအတွင်း နေရာလွတ် (Space) မှားခြားထားပါသည် — '{tGrp.Value}' အစား '{joinedNorm}' [{cue}] ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            cue, jEntries[0].PosTag, true);
                    }
                }
            }

            if (styleMode == ProofingStyleMode.FormalOfficial)
            {
                // Exempt colloquial register markers inside balanced verbatim quotation marks (“...”, "...", ‘...’) while keeping spelling/numeral rules active!
                List<(int Start, int End)>? balancedQuoteRanges = ComputeBalancedQuoteRanges(text);

                // 5. Colloquial-to-Literary (ရုံးသုံး / စာပေ အရေးအသား) Rules
                foreach (var rule in ColloquialRules)
                {
                    if (!ContainsMm(text, mmMask, rule.Trigger))
                        continue;

                    foreach (Match m in rule.Pattern.Matches(text))
                    {
                        if (IsInsideAnyRange(balancedQuoteRanges, m.Index, m.Length) ||
                            OverlapsExistingIssue(issues, m.Index, m.Length))
                        {
                            continue;
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = m.Index,
                            Length = m.Length,
                            OriginalText = m.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = rule.Explanation,
                            GrammaticalReasoning = $"ရုံးသုံးနှင့် စာပေအရေးအသားစနစ် (Formal/Official Mode) တွင် အပြောစကားပစ္စည်း '{m.Value}' အစား စာပေ/ရုံးသုံး စံပုဒ် '{rule.Replacement}' ဖြင့် စကားအဆင့် တစ်ပြေးညီ ထိန်းသိမ်းရမည်။",
                            AuthoritativeSource = "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = rule.Replacement,
                                    MeaningCue = rule.MeaningCue,
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(rule.Replacement),
                                    DiagnosticDetail = "အပြောစကားအသုံး ('" + m.Value + "') မှ စာပေ/ရုံးသုံး အရေးစကား ('" + rule.Replacement + "') သို့ ပြောင်းလဲရန်",
                                    Score = 98.0
                                }
                            }
                        });
                    }
                }

                // 5b. General Colloquial Negative ('မ...ဘူး / မ...ပါဘူး / နေမကောင်းဘူး -> ~ပါ') & Bare Polar Questions ('နေကောင်းလား -> နေကောင်းပါသလော') in Formal/Official Mode
                if (hasMa && hasBu)
                {
                    foreach (Match m in ColloquialNegativeBuInOfficialPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (IsInsideAnyRange(balancedQuoteRanges, g.Index, g.Length) ||
                            OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            continue;
                        }

                        string raw = g.Value;
                        string endTok = m.Groups["End"].Value;
                        string prefixBeforeEnd = raw.Substring(0, raw.Length - endTok.Length);
                        string primaryOfficial;
                        string? altOfficial = null;

                        if (raw == "မအားနေဘူး" || raw == "မအားနေပါဘူး")
                        {
                            primaryOfficial = "မအားလပ်ပါ";
                            altOfficial = "မအားပါ";
                        }
                        else if (raw == "မအားဘူး" || raw == "မအားပါဘူး")
                        {
                            primaryOfficial = "မအားပါ";
                            altOfficial = "မအားလပ်ပါ";
                        }
                        else
                        {
                            primaryOfficial = prefixBeforeEnd + "ပါ";
                        }

                        var cands = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryOfficial,
                                MeaningCue = "ရုံးသုံး/စာပေ အငြင်းအရေးအသား ('မ...ပါ')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primaryOfficial),
                                DiagnosticDetail = $"အပြောစကား '{raw}' မှ ရုံးသုံး/စာပေ '{primaryOfficial}' သို့ ပြောင်းလဲရန်",
                                Score = 98.5
                            }
                        };
                        if (!string.IsNullOrEmpty(altOfficial))
                        {
                            cands.Add(new SuggestionCandidate
                            {
                                Replacement = altOfficial!,
                                MeaningCue = "ရုံးသုံး/စာပေ အခြားရွေးချယ်ရန်ပုဒ်",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 96.0
                            });
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = raw,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား အငြင်းပုံစံ '{raw}' အစား '{primaryOfficial}' ကို သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ရုံးသုံး/စာပေ အရေးစကားတွင် အငြင်းကြိယာကို အပြောစကားပိတ်ပစ္စည်း 'ဘူး/ပါဘူး' အစား 'ပါ' ('{primaryOfficial}') ဖြင့် ယဉ်ကျေးသပ်ရပ်စွာ အဆုံးသတ်ရမည်။",
                            AuthoritativeSource = "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ",
                            Suggestions = cands
                        });
                    }
                }

                if (hasVisarga && ContainsMm(text, mmMask, "လား"))
                {
                    foreach (Match m in ColloquialBareVerbLarInOfficialPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (IsInsideAnyRange(balancedQuoteRanges, g.Index, g.Length) ||
                            OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            continue;
                        }

                        string verb = m.Groups["Verb"].Value;
                        string primaryQ = verb.EndsWith("ပြီ", StringComparison.Ordinal)
                            ? (verb + "လော")
                            : (verb + "ပါသလော");

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = g.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား အမေးပုံစံ '{g.Value}' အစား '{primaryQ}' ကို သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ရုံးသုံး/စာပေ အရေးစကားတွင် ဟုတ်/မဟုတ် အမေးဝိဘတ် 'လား' အစား စာပေသုံး 'ပါသလော / သလော' ('{primaryQ}') ကို သုံးနှုန်းရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အမေးဝိဘတ် 'သလော / လော')",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = primaryQ,
                                    MeaningCue = "ရုံးသုံး/စာပေ အမေးအရေးအသား ('~သလော')",
                                    PosTag = "V",
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primaryQ),
                                    Score = 98.0
                                }
                            }
                        });
                    }
                }

                // 6a. 'အား' vs 'ကို' particle misuse (with Maung Khin Min's Double-'ကို', 'အဖြစ်', & Causative '-စေ' Clause-Level Exemption!)
                if (hasAh)
                {
                    foreach (Match m in AhInsteadOfKoPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        if (OverlapsExistingIssue(issues, pGroup.Index, pGroup.Length))
                            continue;
                        string verb = m.Groups["Verb"].Value;

                        // Exemption reasoning: If another direct-object postposition 'ကို', role marker 'အဖြစ်' (Gazette appointment formula),
                        // or causative '-စေ' exists within the SAME predicate clause (bounded by ၊ / ။ / newline AND sequential clause-chaining markers ပြီး / ၍ / သဖြင့် / ကာ), 'အား' is grammatically licensed!
                        int winStart = pGroup.Index;
                        int minBound = Math.Max(0, pGroup.Index - 120);
                        while (winStart > minBound)
                        {
                            char ch = text[winStart - 1];
                            if (ch == '\u104A' || ch == '\u104B' || ch == '\r' || ch == '\n')
                                break;
                            winStart--;
                        }

                        int verbEnd = m.Index + m.Length;
                        int winEnd = verbEnd;
                        int maxBound = Math.Min(text.Length, verbEnd + 80);
                        while (winEnd < maxBound)
                        {
                            char ch = text[winEnd];
                            if (ch == '\u104A' || ch == '\u104B' || ch == '\r' || ch == '\n')
                                break;
                            winEnd++;
                        }

                        string leftClause = text.Substring(winStart, pGroup.Index - winStart);
                        MatchCollection leftSplits = ClauseChainingConjunctionRegex.Matches(leftClause);
                        if (leftSplits.Count > 0)
                        {
                            Match lastSplit = leftSplits[leftSplits.Count - 1];
                            leftClause = leftClause.Substring(lastSplit.Index + lastSplit.Length);
                        }

                        string rightClause = text.Substring(verbEnd, winEnd - verbEnd);
                        Match rightSplit = ClauseChainingConjunctionRegex.Match(rightClause);
                        if (rightSplit.Success)
                        {
                            rightClause = rightClause.Substring(0, rightSplit.Index);
                        }

                        string clauseWindow = leftClause + m.Value + rightClause;
                        if (clauseWindow.IndexOf("စေ", StringComparison.Ordinal) >= 0 ||
                            clauseWindow.IndexOf("အဖြစ်", StringComparison.Ordinal) >= 0 ||
                            LicensingKoParticleInClauseRegex.IsMatch(clauseWindow))
                        {
                            continue;
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = pGroup.Index,
                            Length = pGroup.Length,
                            OriginalText = pGroup.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"'{verb}' သည် တိုက်ရိုက်ကံပုဒ်လို ကြိယာဖြစ်သဖြင့် လက်ခံပြဝိဘတ် 'အား' အစား ကံဝိဘတ် 'ကို' သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"မြန်မာသဒ္ဒါအရ 'အား' သည် ပေးကမ်း/ချီးမြှင့်ခြင်းကို လက်ခံရယူသူပြ လက်ခံပြဝိဘတ်ဖြစ်ပြီး '{verb}' မှာ တိုက်ရိုက်ပြုလုပ်ခံရသော ကံပုဒ်လိုကြိယာဖြစ်၍ ကံဝိဘတ် 'ကို' သုံးရမည် (ချွင်းချက်: ဝါကျအတွင်း အခြား 'ကို' သို့မဟုတ် စေခိုင်းပြ 'စေ' ပါရှိမှသာ 'အား' သုံးနိုင်သည်)။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကံဝိဘတ် 'ကို' နှင့် လက်ခံပြဝိဘတ် 'အား') နှင့် မောင်ခင်မင်(ဓနုဖြူ)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = "ကို",
                                    MeaningCue = $"ကံဝိဘတ် (~{verb})",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    DiagnosticDetail = $"ကံပုဒ်လိုကြိယာ '{verb}' ရှေ့တွင် ကံဝိဘတ် 'ကို' သုံးရမည်",
                                    Score = 99.0
                                }
                            }
                        });
                    }
                }

                // 6b. 'နှင့်' vs 'ဖြင့်' instrumental postposition in Formal/Official style
                if (hasCreakyDot && ContainsMm(text, mmMask, "နှင့်"))
                {
                    foreach (Match m in InstrumentalNhitInsteadOfPhintPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        string verb = m.Groups["Verb"].Value;
                        AddGrammarOrSpellingIssue(
                            issues, pGroup.Index, pGroup.Length, pGroup.Value, "ဖြင့်",
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"ကိရိယာ/နည်းလမ်းကို အသုံးပြု၍ '{verb}-' ရာတွင် ယှဉ်တွဲပြဝိဘတ် 'နှင့်' အစား အသုံးခံပြဝိဘတ် 'ဖြင့်' ကို သုံးနှုန်းရမည်။",
                            $"အသုံးခံပြဝိဘတ် (~ဖြင့် {verb})", "PART", true,
                            "မြန်မာသဒ္ဒါနှင့် ရုံးသုံးမြန်မာစာအရ 'နှင့်' သည် အဖော်ပြု/ပေါင်းစပ်ခြင်းပြ ယှဉ်တွဲပြဝိဘတ်ဖြစ်ပြီး၊ ကိရိယာတန်ဆာပလာ သို့မဟုတ် အထောက်အထားကို အသုံးပြု၍ ဆောင်ရွက်ကြောင်းပြရာ၌ အသုံးခံပြဝိဘတ် 'ဖြင့်' ကို သုံးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) — 'နှင့်' နှင့် 'ဖြင့်'");
                    }
                }

                // 6d. Redundant stacked postpositions 'အတွက်ကို -> အတွက်' (ဝိဘတ်နှစ်ထပ်)
                if (hasKo && ContainsMm(text, mmMask, "အတွက်ကို"))
                {
                    foreach (Match m in RedundantAtwetKoPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "အတွက်",
                            ProofingIssueType.OfficialStyleGrammarError,
                            "စာပေနှင့် ရုံးသုံးမြန်မာစာအရ ရည်စူးချက်ပြပုဒ် 'အတွက်' နောက်၌ ကံဝိဘတ် 'ကို' နှစ်ထပ်မထည့်ဘဲ 'အတွက်' ဟုသာ ရေးသားရမည်။",
                            "ရုံးသုံးသဒ္ဒါ စံနှုန်း ('အတွက်')", "PART", true,
                            "ရည်ရွယ်ချက်/ရည်စူးချက်ပြ ဝိဘတ် 'အတွက်' သည် ဝါကျအတွင်း ဆက်စပ်မှု ပြည့်စုံပြီးဖြစ်၍ နောက်မှ ကံဝိဘတ် 'ကို' ထပ်ဆင့်တွဲရန် မလိုအပ်ပါ (ဝိဘတ်နှစ်ထပ်အမှား)။",
                            "ရုံးသုံးမြန်မာစာ နှင့် မှားတတ်သော မြန်မာစာအသုံးအနှုန်းများ");
                    }
                }

                // (Note: 6c Agentive/Host 'မှ -> က' now runs universally in Step 4 across both GeneralStandard and FormalOfficial modes)
            }

            // 7. Duplicated Punctuation (ပုဒ်ဖြတ်ပုဒ်ရပ် — including '၊၊' typed twice instead of '။' on list numbers / sentence endings)
            if (hasConsecutiveMyanmarPunct &&
                (text.IndexOf("\u104A\u104A", StringComparison.Ordinal) >= 0 ||
                 text.IndexOf("\u104B\u104B", StringComparison.Ordinal) >= 0 ||
                 text.IndexOf("\u104A\u104B", StringComparison.Ordinal) >= 0))
            {
                foreach (Match m in DuplicatePunctuationPattern.Matches(text))
            {
                var puncSuggestions = new List<SuggestionCandidate>(2);
                if (m.Value.Contains("\u104B"))
                {
                    puncSuggestions.Add(new SuggestionCandidate
                    {
                        Replacement = "\u104B",
                        MeaningCue = "ပုဒ်မကြီး ('။' — ဝါကျအဆုံး)",
                        Score = 100.0
                    });
                }
                else
                {
                    int lookStart = Math.Max(0, m.Index - 24);
                    string beforePunc = text.Substring(lookStart, m.Index - lookStart);
                    int afterIdx = m.Index + m.Length;
                    string afterPunc = afterIdx < text.Length
                        ? text.Substring(afterIdx, Math.Min(16, text.Length - afterIdx))
                        : string.Empty;
                    bool insideUnclosedParens = beforePunc.LastIndexOf('(') > beforePunc.LastIndexOf(')');
                    bool followedByDigit = Regex.IsMatch(afterPunc, @"^\s*[\u1040-\u10490-9]");
                    bool precededByCommaListDigit = Regex.IsMatch(beforePunc, @"[\u104A,]\s*[\u1040-\u10490-9]+\s*$");
                    bool preferPaukMaGyi = !insideUnclosedParens && !followedByDigit && !precededByCommaListDigit &&
                        SentenceOrItemBeforeDoublePaukKaleRegex.IsMatch(beforePunc);
                    if (preferPaukMaGyi)
                    {
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104B",
                            MeaningCue = "ပုဒ်မကြီး ('။' — ဝါကျအဆုံး/အမှတ်စဉ်)",
                            Score = 100.0
                        });
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104A",
                            MeaningCue = "ပုဒ်ကလေး ('၊' — ပုဒ်ဖြတ်)",
                            Score = 94.0
                        });
                    }
                    else
                    {
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104A",
                            MeaningCue = "ပုဒ်ကလေး ('၊' — ပုဒ်ဖြတ်)",
                            Score = 100.0
                        });
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104B",
                            MeaningCue = "ပုဒ်မကြီး ('။' — ဝါကျအဆုံး)",
                            Score = 94.0
                        });
                    }
                }

                issues.Add(new ProofingIssue
                {
                    StartIndex = m.Index,
                    Length = m.Length,
                    OriginalText = m.Value,
                    IssueType = ProofingIssueType.PunctuationError,
                    Message = m.Value == "\u104A\u104A"
                        ? "ပုဒ်ကလေး ('၊') နှစ်ထပ်ဖြစ်နေပါသည် — ပုဒ်မကြီး ('။') သို့မဟုတ် ပုဒ်ကလေး ('၊') တစ်ခုကို ရွေးချယ်ပါ။"
                        : "ပုဒ်ဖြတ်ပုဒ်ရပ် သင်္ကေတ နှစ်ထပ်ဖြစ်နေပါသည်။",
                    GrammaticalReasoning = "မြန်မာစာ ပုဒ်ဖြတ်ပုဒ်ရပ်စနစ်တွင် ပုဒ်ကလေး ('၊' U+104A) နှစ်ချက်ရိုက်၍ ပုဒ်မကြီး ('။' U+104B) အစားထိုးခြင်း မပြုရပါ။",
                    AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ပုဒ်ဖြတ်ပုဒ်ရပ်)",
                    Suggestions = puncSuggestions
                });
                }
            }

            // 7b. V42 Unclosed & Mismatched Brackets / Smart Quotations (LanguageTool-grade structural check, Myanmar paragraphs only)
            if (mmMask != 0 && hasBracketOrSmartQuote)
            {
                AnalyzeUnpairedBracketsAndQuotes(text, issues);
            }

            // 7c. V42 Accidental Space Before Myanmar Punctuation ('၊' / '။' — exempts classical Pali Nissaya '<Pali> ၊ <Gloss>')
            if (hasSpaceBeforeMyanmarPunct)
            {
                foreach (Match m in AccidentalSpaceBeforeMyanmarPunctPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string punct = m.Groups["Punct"].Value;
                    if (punct == "\u104A" && IsClassicalPaliNissayaHeadBeforeComma(text, g.Index, trie))
                        continue;

                    string punctName = punct == "\u104B" ? "ပုဒ်မကြီး ('။')" : "ပုဒ်ကလေး ('၊')";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, punct,
                        ProofingIssueType.PunctuationError,
                        $"{punctName} ရှေ့တွင် Space မခြားရပါ — စာလုံးနှင့်ကပ်၍ '{punct}' ဟု ရေးသားရမည်။",
                        $"{punctName} ရှေ့ Space ဖယ်ရှားရန်", "PUNCT", true,
                        $"မြန်မာစာ ပုဒ်ဖြတ်ပုဒ်ရပ်စံနှုန်းအရ {punctName} ကို ရှေ့စာလုံးနှင့် တစ်ဆက်တည်းကပ်၍ ရိုက်နှိပ်ရပြီး ပုဒ်ဖြတ်ပုဒ်ရပ်၏ နောက်၌သာ Space ခြားရသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးထုံး");
                }
            }

            // 7d. V42 Smart Bilingual Numeral Consistency in Formal/Official Mode (flags pure-Burmese ASCII numerals, shields English-linked numbers)
            if (styleMode == ProofingStyleMode.FormalOfficial && mmMask != 0 && hasAsciiDigit && (trie == null || !IsCleanCanonicalStandaloneNrc(text, trie)))
            {
                var quoteSpansForNum = ComputeBalancedQuoteRanges(text);
                foreach (Match m in PureBurmeseAsciiNumeralPattern.Matches(text))
                {
                    Group numGroup = m.Groups["Num"];
                    if (OverlapsExistingIssue(issues, numGroup.Index, numGroup.Length) ||
                        IsInsideAnyRange(quoteSpansForNum, numGroup.Index, numGroup.Length))
                    {
                        continue;
                    }

                    if (IsPrecededByEnglishOrTechnicalToken(text, numGroup.Index))
                        continue;

                    string unit = m.Groups["Unit"].Value;
                    string mmDigits = ConvertAsciiDigitsToMyanmar(numGroup.Value);
                    AddGrammarOrSpellingIssue(
                        issues, numGroup.Index, numGroup.Length, numGroup.Value, mmDigits,
                        ProofingIssueType.NumeralClassifierError,
                        $"ရုံးသုံး/စာပေ မြန်မာစာအရေးအသားတွင် မြန်မာမျိုးပြပစ္စည်း/အချိန်ပြပုဒ် ('{unit}') ရှေ့ရှိ ကိန်းဂဏန်းကို မြန်မာဂဏန်း '{mmDigits}' ဖြင့် တစ်ပြေးညီ ရေးသားသင့်ပါသည်။",
                        $"မြန်မာဂဏန်း ('{mmDigits} {unit}')", "NUM", true,
                        $"မြန်မာဝါကျသက်သက်အတွင်း အင်္ဂလိပ်ဂဏန်း ('{numGroup.Value}') နှင့် မြန်မာစာ ('{unit}') ရောနှောနေသဖြင့် ရုံးသုံးမြန်မာစာစံနှုန်းအရ မြန်မာဂဏန်း '{mmDigits}' သုံးသင့်ပါသည် (အင်္ဂလိပ်အမှတ်တံဆိပ်/ယူနစ်တွဲများကို အလိုအလျောက် ချန်လှပ်ပေးထားသည်)။",
                        "ရုံးသုံးမြန်မာစာရေးသားနည်း စံနှုန်း",
                        requiresReviewBeforeBatchFix: true);
                }
            }

            return issues;
        }

        private static bool HasAnySubstring(string text, ulong mmMask, string[] triggers)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                if (ContainsMm(text, mmMask, triggers[i]))
                    return true;
            }
            return false;
        }

        private static bool MayContainDuplicatedFunctionWord(string text, ulong mmMask)
        {
            int len = text.Length;
            for (int i = 0; i < DuplicatedFunctionWordCandidates.Length; i++)
            {
                string w = DuplicatedFunctionWordCandidates[i];
                if (!ContainsMm(text, mmMask, w))
                    continue;
                int searchPos = 0;
                while (searchPos < len)
                {
                    int hit = text.IndexOf(w, searchPos, StringComparison.Ordinal);
                    if (hit < 0)
                        break;
                    int after = hit + w.Length;
                    if (after < len && (text[after] == ' ' || text[after] == '\t'))
                    {
                        int k = after + 1;
                        while (k < len && (text[k] == ' ' || text[k] == '\t'))
                            k++;
                        if (k + w.Length <= len && text[k] == w[0] && string.CompareOrdinal(text, k, w, 0, w.Length) == 0)
                            return true;
                    }
                    searchPos = hit + 1;
                }
            }
            return false;
        }

        /// <summary>
        /// Ultra-fast O(1) validator returning <c>true</c> ONLY when <paramref name="text"/> is a 100% canonical,
        /// spotless standalone Myanmar NRC cell (State 1..14 + '/' + verified MOIP Township in <paramref name="trie"/>
        /// + canonical card type + 6 clean digits with zero lookalike/zero-width corruptions).
        /// Any questionable or malformed NRC returns <c>false</c> so full <c>CheckText</c> diagnostics run unchanged.
        /// </summary>
        internal static bool IsCleanCanonicalStandaloneNrc(string text, MyanmarTrie trie)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 10 || text.Length > 28)
                return false;

            for (int k = 0; k < text.Length; k++)
            {
                char ch = text[k];
                if (ch == '\u00A0' || ch == '\u202F' || ch == '\u2007' ||
                    (ch >= '\u200B' && ch <= '\u200D') || ch == '\uFEFF' || ch == '\u2060' || ch == '\u00AD' || ch == '\u25CC')
                {
                    return false;
                }
            }

            int p = 0;
            int end = text.Length;
            while (p < end && (text[p] == ' ' || text[p] == '\t'))
                p++;
            while (end > p && (text[end - 1] == ' ' || text[end - 1] == '\t'))
                end--;
            if (end - p < 10)
                return false;

            // 1. State number 1..14 (no leading zero, pure Myanmar or pure ASCII digits)
            char d0 = text[p];
            bool mmState = d0 >= '\u1041' && d0 <= '\u1049';
            bool ascState = d0 >= '1' && d0 <= '9';
            if (!mmState && !ascState)
                return false;

            int stateVal = mmState ? (d0 - '\u1040') : (d0 - '0');
            p++;
            if (p < end)
            {
                char d1 = text[p];
                if (mmState && d1 >= '\u1040' && d1 <= '\u1049')
                {
                    stateVal = stateVal * 10 + (d1 - '\u1040');
                    p++;
                }
                else if (ascState && d1 >= '0' && d1 <= '9')
                {
                    stateVal = stateVal * 10 + (d1 - '0');
                    p++;
                }
            }
            if (stateVal < 1 || stateVal > 14)
                return false;

            while (p < end && text[p] == ' ')
                p++;
            if (p >= end || text[p] != '/')
                return false;
            p++;
            while (p < end && text[p] == ' ')
                p++;

            // 2. Township code: 2..4 bare Myanmar letters in trie (excluding \u1009 lookalike)
            int twStart = p;
            while (p < end)
            {
                char tc = text[p];
                if ((tc >= '\u1000' && tc <= '\u1021' && tc != '\u1009') || tc == '\u1025')
                    p++;
                else
                    break;
            }
            int twLen = p - twStart;
            if (twLen < 2 || twLen > 4)
                return false;

            string township = text.Substring(twStart, twLen);
            if (!trie.ContainsExactWord(township) || trie.TryGetWrongFormRule(township, out _))
                return false;

            while (p < end && text[p] == ' ')
                p++;
            if (p >= end || text[p] != '(')
                return false;
            p++;
            while (p < end && text[p] == ' ')
                p++;

            // 3. Canonical card type inside (...)
            int closeParen = text.IndexOf(')', p);
            if (closeParen < 0 || closeParen >= end)
                return false;
            int ctEnd = closeParen;
            while (ctEnd > p && text[ctEnd - 1] == ' ')
                ctEnd--;
            int ctLen = ctEnd - p;
            bool validCardType =
                MatchesSliceOrdinal(text, p, ctLen, "နိုင်") ||
                MatchesSliceOrdinal(text, p, ctLen, "ဧည့်") ||
                MatchesSliceOrdinal(text, p, ctLen, "ပြု") ||
                MatchesSliceOrdinal(text, p, ctLen, "သာသနာ") ||
                MatchesSliceOrdinal(text, p, ctLen, "ယာယီ") ||
                (ctLen == 1 && (text[p] == 'စ' || text[p] == 'န' || text[p] == 'သ')) ||
                MatchesSliceOrdinal(text, p, ctLen, "သာ");

            if (!validCardType)
                return false;

            p = closeParen + 1;
            while (p < end && text[p] == ' ')
                p++;

            // 4. Exactly 6 clean digits (pure Myanmar or pure ASCII, zero consonant Wa \u101D)
            if (end - p != 6)
                return false;

            char s0 = text[p];
            bool mmSer = s0 >= '\u1040' && s0 <= '\u1049';
            bool ascSer = s0 >= '0' && s0 <= '9';
            if (!mmSer && !ascSer)
                return false;

            for (int si = 1; si < 6; si++)
            {
                char sc = text[p + si];
                if (mmSer)
                {
                    if (sc < '\u1040' || sc > '\u1049')
                        return false;
                }
                else
                {
                    if (sc < '0' || sc > '9')
                        return false;
                }
            }

            return true;
        }

        private static bool MatchesSliceOrdinal(string text, int start, int length, string literal)
        {
            return length == literal.Length && string.CompareOrdinal(text, start, literal, 0, literal.Length) == 0;
        }

        private static bool HasStandaloneMhaParticle(string text)
        {
            int pos = 0;
            int len = text.Length;
            while (pos < len)
            {
                int hit = text.IndexOf("မှ", pos, StringComparison.Ordinal);
                if (hit < 0)
                    return false;

                int after = hit + 2;
                if (after >= len)
                    return true;

                char next = text[after];
                // Skip open vowel/combining syllables like မှာ, မှီ, မှု, မှူး, မှေ, မှဲ, မှံ
                if (next >= '\u102B' && next <= '\u103A')
                {
                    pos = after;
                    continue;
                }

                // Skip closed coda syllables like မှတ်, မှန်, မှက်, မှပ်, မှယ် (where next is C and next+1 is Asat \u103A)
                if (next >= '\u1000' && next <= '\u1021' && after + 1 < len && text[after + 1] == '\u103A')
                {
                    pos = after;
                    continue;
                }

                return true;
            }
            return false;
        }

        private static bool HasAnyMyanmarNumeralCore(string text, ulong mmMask, bool hasNonZeroMyanmarDigit)
        {
            if (hasNonZeroMyanmarDigit)
                return true;

            return ContainsMm(text, mmMask, "တစ်") ||
                   ContainsMm(text, mmMask, "နှစ်") ||
                   ContainsMm(text, mmMask, "သုံး") ||
                   ContainsMm(text, mmMask, "လေး") ||
                   ContainsMm(text, mmMask, "ငါး") ||
                   ContainsMm(text, mmMask, "ခြောက်") ||
                   ContainsMm(text, mmMask, "ခုနစ်") ||
                   ContainsMm(text, mmMask, "ရှစ်") ||
                   ContainsMm(text, mmMask, "ကိုး") ||
                   ContainsMm(text, mmMask, "ဆယ်");
        }

        private static bool OverlapsExistingIssue(List<ProofingIssue> issues, int index, int length)
        {
            int end = index + length;
            for (int i = 0; i < issues.Count; i++)
            {
                ProofingIssue ex = issues[i];
                if (index < ex.StartIndex + ex.Length && end > ex.StartIndex)
                    return true;
            }
            return false;
        }

        private static bool IsMyanmarRunLetterOrMark(char c)
        {
            return c >= '\u1000' && c <= '\u103F';
        }

        private static bool IsBoundaryParticleBeforeProperStem(string syl)
        {
            return syl == "က" || syl == "ကို" || syl == "မှ" || syl == "သို့" ||
                   syl == "၌" || syl == "တွင်" || syl == "မှာ" || syl == "၏" ||
                   syl == "နှင့်" || syl == "နှင့်" || syl == "ဖြင့်" || syl == "ဖြင့်";
        }

        private static string ConcatSyllableRange(List<string> syls, int startIdx, int endIdxInclusive)
        {
            if (startIdx == endIdxInclusive)
                return UnicodeNormalizer.NormalizeCanonicalOrder(syls[startIdx]);

            var sb = new System.Text.StringBuilder(24);
            for (int i = startIdx; i <= endIdxInclusive; i++)
                sb.Append(syls[i]);
            return UnicodeNormalizer.NormalizeCanonicalOrder(sb.ToString());
        }

        private static bool IsShieldedLexicalTaStem(string text, int taIndex, int matchLength, MyanmarTrie? trie)
        {
            if (trie == null)
                return false;

            int runStart = taIndex;
            while (runStart > 0 && IsMyanmarRunLetterOrMark(text[runStart - 1]))
                runStart--;

            int runEnd = taIndex + matchLength;
            while (runEnd < text.Length && IsMyanmarRunLetterOrMark(text[runEnd]))
                runEnd++;

            string runRaw = text.Substring(runStart, runEnd - runStart);
            List<string> syls = MyanmarSyllableBreaker.BreakIntoSyllables(runRaw, isPreNormalized: true);
            if (syls.Count < 2)
                return false;

            int relTa = taIndex - runStart;
            int relEnd = relTa + matchLength;
            int cursor = 0;
            int taSylIdx = -1;
            int endSylIdx = -1;

            for (int i = 0; i < syls.Count; i++)
            {
                if (cursor == relTa)
                    taSylIdx = i;
                cursor += syls[i].Length;
                if (cursor == relEnd)
                    endSylIdx = i;
            }

            if (taSylIdx < 0 || endSylIdx < taSylIdx)
                return false;

            // 1. Rightward multi-syllable lexical stem starting at 'တ' (e.g. 'တလဟတ်', 'တလလ', 'တလဖက်', 'တလပါ', 'တလန', 'တအုပ်တအာ')
            for (int r = endSylIdx + 1; r <= Math.Min(syls.Count - 1, taSylIdx + 5); r++)
            {
                string rightStem = ConcatSyllableRange(syls, taSylIdx, r);
                if (trie.ContainsExactWord(rightStem) && !trie.TryGetWrongFormRule(rightStem, out _))
                    return true;
            }

            // 2. Multi-syllable stem spanning across 'တ + Classifier' (e.g. 'မဟီတလ', 'ယတလ', 'သွတ်တပတ်')
            //    or preceding proper/Pali stem ending in 'တ' before Classifier (e.g. 'နီးဂတ' + 'ခရိုင်', 'ယမဂတ' + 'ခရိုင်', 'အခိတ' + 'ခရိုင်')
            for (int l = 0; l < taSylIdx; l++)
            {
                if (l > 0 && !IsBoundaryParticleBeforeProperStem(UnicodeNormalizer.NormalizeCanonicalOrder(syls[l - 1])))
                    continue;

                string leftBeforeTa = ConcatSyllableRange(syls, l, taSylIdx - 1);
                bool leftIsStandalone = trie.ContainsExactWord(leftBeforeTa);

                for (int r = endSylIdx; r < syls.Count; r++)
                {
                    string spanStem = ConcatSyllableRange(syls, l, r);
                    if (trie.ContainsExactWord(spanStem) && !trie.TryGetWrongFormRule(spanStem, out _))
                    {
                        return true;
                    }
                }

                if (taSylIdx - l >= 1)
                {
                    string stemEndingTa = ConcatSyllableRange(syls, l, taSylIdx);
                    if (trie.TryGetExactEntries(stemEndingTa, out var tEntries) &&
                        tEntries != null && tEntries.Count > 0 &&
                        !trie.TryGetWrongFormRule(stemEndingTa, out _) &&
                        (!leftIsStandalone || (taSylIdx - l >= 2 && (tEntries[0].PosTag == "PROPN" || tEntries[0].IsOfficialThatpon))))
                    {
                        return true;
                    }
                    if (!leftIsStandalone)
                    {
                        var taHomos = trie.LookupStrictHomophones(stemEndingTa);
                        for (int hi = 0; hi < taHomos.Count; hi++)
                        {
                            if (taHomos[hi].IsOfficialThatpon && taHomos[hi].Word.EndsWith("တ", StringComparison.Ordinal))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsShieldedLexicalPiWord(string text, int piIndex, int piLength, MyanmarTrie? trie)
        {
            if (trie == null)
                return false;

            int runStart = piIndex;
            while (runStart > 0 && IsMyanmarRunLetterOrMark(text[runStart - 1]))
                runStart--;

            int runEnd = piIndex + piLength;
            while (runEnd < text.Length && IsMyanmarRunLetterOrMark(text[runEnd]))
                runEnd++;

            if (runEnd - runStart <= piLength)
                return false;

            string wordEndingAtPi = UnicodeNormalizer.NormalizeCanonicalOrder(
                text.Substring(runStart, (piIndex + piLength) - runStart));
            if (wordEndingAtPi.Length > piLength &&
                trie.ContainsExactWord(wordEndingAtPi) &&
                !trie.TryGetWrongFormRule(wordEndingAtPi, out _))
            {
                return true;
            }

            if (runEnd > piIndex + piLength)
            {
                string fullWord = UnicodeNormalizer.NormalizeCanonicalOrder(
                    text.Substring(runStart, runEnd - runStart));
                if (trie.ContainsExactWord(fullWord) && !trie.TryGetWrongFormRule(fullWord, out _))
                    return true;
            }

            return false;
        }

        private static bool IsShieldedOfficialWordSpan(string text, int matchIndex, int matchLength, MyanmarTrie? trie)
        {
            if (trie == null)
                return false;

            int runStart = matchIndex;
            while (runStart > 0 && IsMyanmarRunLetterOrMark(text[runStart - 1]))
                runStart--;

            int runEnd = matchIndex + matchLength;
            while (runEnd < text.Length && IsMyanmarRunLetterOrMark(text[runEnd]))
                runEnd++;

            if (runEnd - runStart <= matchLength)
                return false;

            string fullWord = UnicodeNormalizer.NormalizeCanonicalOrder(
                text.Substring(runStart, runEnd - runStart));
            return trie.TryGetExactEntries(fullWord, out var entries) &&
                   entries != null && entries.Count > 0 &&
                   entries[0].IsOfficialThatpon &&
                   !trie.TryGetWrongFormRule(fullWord, out _);
        }

        private static void AddGrammarOrSpellingIssue(
            List<ProofingIssue> issues,
            int index,
            int length,
            string rawWrong,
            string correct,
            ProofingIssueType issueType,
            string message,
            string meaningCue,
            string posTag,
            bool isOfficialThatpon,
            string reasoning = "",
            string authoritativeSource = "",
            bool requiresReviewBeforeBatchFix = false)
        {
            issues.Add(new ProofingIssue
            {
                StartIndex = index,
                Length = length,
                OriginalText = rawWrong,
                IssueType = issueType,
                Message = message,
                GrammaticalReasoning = reasoning,
                AuthoritativeSource = authoritativeSource,
                RequiresReviewBeforeBatchFix = requiresReviewBeforeBatchFix,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate
                    {
                        Replacement = correct,
                        MeaningCue = meaningCue,
                        PosTag = posTag,
                        IsOfficialThatpon = isOfficialThatpon,
                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(correct),
                        DiagnosticDetail = !string.IsNullOrEmpty(reasoning)
                            ? reasoning
                            : OrthographicDiffExplainer.ExplainDifference(rawWrong, correct),
                        Score = 100.0
                    }
                }
            });
        }

        private static void ScanMisusedKyaungNominalizer(string text, MyanmarTrie? trie, List<ProofingIssue> issues)
        {
            const string target = "ကျောင်း";
            int len = text.Length;
            int searchPos = 0;
            while (searchPos < len)
            {
                int hit = text.IndexOf(target, searchPos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                searchPos = hit + 7;

                int after = hit + 7;
                bool isBoundaryAfter = after >= len || char.IsWhiteSpace(text[after]) || text[after] == '\u104A' || text[after] == '\u104B';
                bool hasReportingPred = HasReportingPredicateAfterKyaung(text, after);
                bool startsWithHuOrPyaaw = MatchesSliceAt(text, after, "ဟု") || MatchesSliceAt(text, after, "ပြော");
                if (!isBoundaryAfter && !hasReportingPred && !startsWithHuOrPyaaw)
                    continue;

                int stemStart = hit;
                while (stemStart > 0 && IsMyanmarRunLetterOrMark(text[stemStart - 1]))
                    stemStart--;
                int stemLen = hit - stemStart;
                if (stemLen < 4 || stemLen > 40)
                    continue;

                if (OverlapsExistingIssue(issues, hit, 7) || IsShieldedOfficialWordSpan(text, hit, 7, trie))
                    continue;

                string stem = text.Substring(stemStart, stemLen);

                bool matchedAspect = false;
                for (int i = 0; i < VerbalAspectBeforeKyaungSuffixes.Length; i++)
                {
                    string sfx = VerbalAspectBeforeKyaungSuffixes[i];
                    int minLen = (sfx == "မရှိ" || sfx == "မဟုတ်") ? sfx.Length : sfx.Length + 2;
                    if (stemLen >= minLen && stem.EndsWith(sfx, StringComparison.Ordinal))
                    {
                        matchedAspect = true;
                        break;
                    }
                }

                if (matchedAspect)
                {
                    if (!HasAnyKnownLexiconSuffixWithEnding(trie, stem, target))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, hit, 7, target, "ကြောင်း",
                            ProofingIssueType.OfficialStyleGrammarError,
                            "ကြိယာနောက်ဆက် အကြောင်းအရာပြပစ္စည်းတွင် နာမ် 'ကျောင်း' (ယပင့်) အစား 'ကြောင်း' (ရရစ်) ကို သုံးနှုန်းရမည်။",
                            "အကြောင်းအရာပြ နာမ်ပြုပစ္စည်း ('~ကြောင်း')", "PART", true,
                            $"ရှေ့တွင် ကြိယာထောက်ပစ္စည်း ('{stem}-') ရှိနေသဖြင့် စာသင်ကျောင်း/ဘုန်းကြီးကျောင်း အနက်မဟုတ်ဘဲ ဖြစ်စဉ်အကြောင်းအရာကို ညွှန်းဆိုသော နာမ်ပြုပစ္စည်း 'ကြောင်း' (ကကြီးရရစ်) ဖြစ်ရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                    }
                    continue;
                }

                if (trie != null && hasReportingPred && stemLen <= 28 &&
                    TryExtractTrailingPureVerbStem(trie, stem, target, checkDirectionalFallGuard: false, out string matchedVerb))
                {
                    AddGrammarOrSpellingIssue(
                        issues, hit, 7, target, "ကြောင်း",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာ '{matchedVerb}-' နောက်တွင် အဆို/သတင်းပြကြိယာ လိုက်သဖြင့် 'ကျောင်း' အစား အကြောင်းအရာပြပစ္စည်း 'ကြောင်း' ဟု ရေးသားရမည်။",
                        $"နာမ်ပြုပစ္စည်း ({matchedVerb}ကြောင်း)", "PART", true,
                        $"'{matchedVerb}-' သည် ကြိယာပုဒ်ဖြစ်ပြီး နောက်တွင် သတင်းပို့/ပြောကြားခြင်းပြ ကြိယာလိုက်နေသဖြင့် 'ကြောင်း' (ရရစ်) ကိုသာ သုံးနှုန်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }
        }

        private static bool HasReportingPredicateAfterKyaung(string text, int afterIdx)
        {
            int len = text.Length;
            int p = afterIdx;
            while (p < len && char.IsWhiteSpace(text[p]))
                p++;
            if (MatchesSliceAt(text, p, "ဟု"))
            {
                p += 2;
                while (p < len && char.IsWhiteSpace(text[p]))
                    p++;
            }
            if (p >= len)
                return false;

            for (int i = 0; i < ReportingPredicatesAfterKyaung.Length; i++)
            {
                if (MatchesSliceAt(text, p, ReportingPredicatesAfterKyaung[i]))
                    return true;
            }
            return false;
        }

        private static void ScanMisusedChinNominalizer(string text, MyanmarTrie trie, List<ProofingIssue> issues)
        {
            const string target = "ချင်း";
            int len = text.Length;
            int searchPos = 0;
            while (searchPos < len)
            {
                int hit = text.IndexOf(target, searchPos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                searchPos = hit + 5;

                int after = hit + 5;
                if (!HasNominalPostpositionAfterChin(text, after))
                    continue;

                int stemStart = hit;
                while (stemStart > 0 && IsMyanmarRunLetterOrMark(text[stemStart - 1]))
                    stemStart--;
                int stemLen = hit - stemStart;
                if (stemLen < 4 || stemLen > 28)
                    continue;

                if (OverlapsExistingIssue(issues, hit, 5) || IsShieldedOfficialWordSpan(text, hit, 5, trie))
                    continue;

                string stem = text.Substring(stemStart, stemLen);
                if (IsReduplicatedTemporalVerbStem(stem))
                    continue;

                if (TryExtractTrailingPureVerbStem(trie, stem, target, checkDirectionalFallGuard: false, out string matchedVerb))
                {
                    AddGrammarOrSpellingIssue(
                        issues, hit, 5, target, "ခြင်း",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာ '{matchedVerb}-' ကို နာမ်ပုဒ်အဖြစ် ပြောင်းလဲရာတွင် 'ချင်း' (ယပင့်) အစား နာမ်ပြုပစ္စည်း 'ခြင်း' (ရရစ် — '{matchedVerb}ခြင်း') ကို သုံးနှုန်းရမည်။",
                        $"ကြိယာနာမ်ပြုပစ္စည်း ({matchedVerb}ခြင်း)", "PART", true,
                        $"နှစ်ခါထပ် ကြိယာ ('ရောက်ရောက်ချင်း/သိသိချင်း') မဟုတ်ဘဲ ကြိယာ '{matchedVerb}-' နောက်၌ ဝိဘတ်/ဖြစ်ခြင်းပြပုဒ် လိုက်နေသဖြင့် ပြုမူဆောင်ရွက်မှုပြ နာမ်ပြုပစ္စည်း 'ခြင်း' (ခကွေးရရစ်) ဖြစ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }
        }

        private static bool HasNominalPostpositionAfterChin(string text, int afterIdx)
        {
            int len = text.Length;
            int p = afterIdx;
            while (p < len && (text[p] == ' ' || text[p] == '\t'))
                p++;
            if (p >= len)
                return false;

            for (int i = 0; i < NominalPostpositionsAfterChin.Length; i++)
            {
                string post = NominalPostpositionsAfterChin[i];
                if (MatchesSliceAt(text, p, post))
                {
                    int end = p + post.Length;
                    if (end >= len)
                        return true;
                    char next = text[end];
                    if (char.IsWhiteSpace(next) || next == '\u104A' || next == '\u104B' ||
                        next == 'ပ' || next == 'သ' || next == 'တ' || next == 'မ' || next == 'ဘ')
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ScanMisusedKyaPluralVerb(string text, MyanmarTrie trie, List<ProofingIssue> issues)
        {
            const string target = "ကျ";
            int len = text.Length;
            int searchPos = 0;
            while (searchPos < len)
            {
                int hit = text.IndexOf(target, searchPos, StringComparison.Ordinal);
                if (hit < 0)
                    break;
                searchPos = hit + 2;

                int after = hit + 2;
                if (after >= len)
                    continue;

                // O(1) single-char pre-filter rejects 99.9% of 'ကျ-' words ('ကျင်းပ', 'ကျောင်း', 'ကျေးရွာ', 'ကျော်', 'ကျပ်') in 1 ns
                char cAfter = text[after];
                if (cAfter != 'ပ' && cAfter != 'သ' && cAfter != 'တ' && cAfter != 'မ' && cAfter != '၍' && cAfter != 'က')
                    continue;

                if (!HasPluralVerbCloserAt(text, after))
                    continue;

                int stemStart = hit;
                while (stemStart > 0 && IsMyanmarRunLetterOrMark(text[stemStart - 1]))
                    stemStart--;
                int stemLen = hit - stemStart;
                if (stemLen < 4 || stemLen > 28)
                    continue;

                if (OverlapsExistingIssue(issues, hit, 2) || IsShieldedOfficialWordSpan(text, hit, 2, trie))
                    continue;

                string stem = text.Substring(stemStart, stemLen);
                if (TryExtractTrailingPureVerbStem(trie, stem, target, checkDirectionalFallGuard: true, out string matchedVerb))
                {
                    AddGrammarOrSpellingIssue(
                        issues, hit, 2, target, "ကြ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာ '{matchedVerb}-' နောက်ရှိ အများပြ ကြိယာထောက်ပစ္စည်းတွင် 'ကျ' (ယပင့်) အစား 'ကြ' (ရရစ် — '{matchedVerb}ကြ...') ဟု ရေးသားရမည်။",
                        $"အများပြ ကြိယာထောက်ပစ္စည်း ({matchedVerb}ကြ~)", "PART", true,
                        $"'{matchedVerb}-' သည် အပြုကြိယာပုဒ်ဖြစ်ပြီး နောက်တွင် ဝါကျအဆုံးသတ်ပစ္စည်း လိုက်နေသဖြင့် အများပြပစ္စည်း 'ကြ' (ကကြီးရရစ်) ကိုသာ သုံးနှုန်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }
        }

        private static bool HasPluralVerbCloserAt(string text, int afterIdx)
        {
            int len = text.Length;
            for (int i = 0; i < PluralVerbClosersAfterKya.Length; i++)
            {
                string closer = PluralVerbClosersAfterKya[i];
                if (MatchesSliceAt(text, afterIdx, closer))
                {
                    int end = afterIdx + closer.Length;
                    if (end >= len)
                        return true;
                    char next = text[end];
                    if (char.IsWhiteSpace(next) || next == '\u104A' || next == '\u104B')
                        return true;
                }
            }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool MatchesSliceAt(string text, int start, string literal)
        {
            return start + literal.Length <= text.Length &&
                   text[start] == literal[0] &&
                   string.CompareOrdinal(text, start, literal, 0, literal.Length) == 0;
        }

        private static bool HasAnyKnownLexiconSuffixWithEnding(MyanmarTrie? trie, string rawStem, string ending)
        {
            if (trie == null || string.IsNullOrEmpty(rawStem))
                return false;

            string normFull = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem + ending);
            if (trie.ContainsExactWord(normFull))
                return true;

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(normFull, isPreNormalized: true);
            for (int start = 1; start <= syls.Count - 2; start++)
            {
                string sub = string.Concat(syls.GetRange(start, syls.Count - start));
                if (trie.ContainsExactWord(sub))
                    return true;
            }
            return false;
        }

        private static bool IsPureMultiSyllableVerbInTrie(MyanmarTrie trie, string normCandidate)
        {
            if (string.IsNullOrEmpty(normCandidate) || !trie.TryGetExactEntries(normCandidate, out var entries) || entries == null || entries.Count == 0)
                return false;

            bool hasVerb = false;
            for (int i = 0; i < entries.Count; i++)
            {
                string pos = (entries[i].PosTag ?? string.Empty).Trim().ToUpperInvariant();
                if (pos == "ENCYC" || pos == "PROVERB")
                    continue;
                if (pos == "N" || pos == "N/V" || pos == "PROPER" || pos == "LOAN" || pos == "PART" || pos == "PRON" || pos == "ABB" || pos == "NUM")
                    return false;
                if (pos == "V")
                    hasVerb = true;
            }
            return hasVerb;
        }

        private static bool TryExtractTrailingPureVerbStem(
            MyanmarTrie trie,
            string rawStem,
            string wrongEnding,
            bool checkDirectionalFallGuard,
            out string matchedVerb)
        {
            matchedVerb = string.Empty;
            if (string.IsNullOrEmpty(rawStem))
                return false;

            if (HasAnyKnownLexiconSuffixWithEnding(trie, rawStem, wrongEnding))
                return false;

            string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem);
            string strippedStem = normStem;
            for (int i = 0; i < VerbalAuxStripSuffixes.Length; i++)
            {
                string aux = VerbalAuxStripSuffixes[i];
                if (strippedStem.Length > aux.Length + 3 && strippedStem.EndsWith(aux, StringComparison.Ordinal))
                {
                    strippedStem = strippedStem.Substring(0, strippedStem.Length - aux.Length);
                    break;
                }
            }

            if (checkDirectionalFallGuard)
            {
                for (int i = 0; i < DirectionalFallVerbEndings.Length; i++)
                {
                    if (normStem.EndsWith(DirectionalFallVerbEndings[i], StringComparison.Ordinal) ||
                        strippedStem.EndsWith(DirectionalFallVerbEndings[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(strippedStem, isPreNormalized: true);
            if (syls.Count < 2)
                return false;

            // 1. Direct match on full stripped stem (>= 2 syllables)
            if (IsPureMultiSyllableVerbInTrie(trie, strippedStem))
            {
                matchedVerb = strippedStem;
                return true;
            }

            // 2. Unspaced [Known Lexicon Object/Prefix] + [2-or-3-syllable Pure Verb] (e.g. 'အစည်းအဝေးကျင်းပကျောင်း')
            for (int vLen = Math.Min(3, syls.Count - 1); vLen >= 2; vLen--)
            {
                int split = syls.Count - vLen;
                string prefix = string.Concat(syls.GetRange(0, split));
                string verbTail = string.Concat(syls.GetRange(split, vLen));
                if (trie.ContainsExactWord(prefix) && IsPureMultiSyllableVerbInTrie(trie, verbTail))
                {
                    matchedVerb = verbTail;
                    return true;
                }
            }

            return false;
        }

        private static bool IsReduplicatedTemporalVerbStem(string rawStem)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem);
            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(norm, isPreNormalized: true);
            int n = syls.Count;
            if (n >= 2 && syls[n - 1] == syls[n - 2])
                return true;
            if (n >= 4 && syls[n - 4] == syls[n - 2] && syls[n - 3] == syls[n - 1])
                return true;
            return false;
        }

        private static bool MayContainAdjacentDuplicateMyanmarWord(string text)
        {
            int len = text.Length;
            int prevStart = -1;
            int prevLen = 0;
            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if (c >= '\u1000' && c <= '\u103F')
                {
                    int start = i;
                    i++;
                    while (i < len && text[i] >= '\u1000' && text[i] <= '\u103F')
                        i++;
                    int wLen = i - start;
                    if (wLen >= 4 && wLen == prevLen && text[start] == text[prevStart] &&
                        string.CompareOrdinal(text, prevStart, text, start, wLen) == 0)
                    {
                        return true;
                    }
                    prevStart = start;
                    prevLen = wLen;
                }
                else if (c == ' ' || c == '\t')
                {
                    i++;
                }
                else
                {
                    prevStart = -1;
                    prevLen = 0;
                    i++;
                }
            }
            return false;
        }

        private static bool IsAccidentalDuplicateMyanmarWord(string rawWord, MyanmarTrie? trie)
        {
            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(rawWord);
            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(norm, isPreNormalized: true);
            if (syls.Count < 2)
                return false;

            // Exempt reduplicated adverbs/ideophones ('ကောင်းကောင်း', 'ဖြည်းဖြည်း') & distributive 'တစ်...' ('တစ်ခု တစ်ခု', 'တစ်ဦး တစ်ဦး')
            if (syls.Count == 2 && syls[0] == syls[1])
                return false;
            if (norm == "တကယ်" || norm == "အမှန်" || norm == "မကြာ" ||
                norm.StartsWith("တစ်", StringComparison.Ordinal) ||
                norm.StartsWith("အသီး", StringComparison.Ordinal) ||
                norm.StartsWith("အမျိုး", StringComparison.Ordinal))
            {
                return false;
            }

            // Exempt 4-syllable lexical reduplications typed with a space (e.g. 'ခဏ ခဏ' -> 'ခဏခဏ')
            if (trie != null && trie.ContainsExactWord(norm + norm))
                return false;

            return true;
        }

        private static bool MayContainAdjacentDuplicateAsciiWord(string text)
        {
            int len = text.Length;
            int prevStart = -1;
            int prevLen = 0;
            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    int start = i;
                    i++;
                    while (i < len && ((text[i] >= 'A' && text[i] <= 'Z') || (text[i] >= 'a' && text[i] <= 'z')))
                        i++;
                    int wLen = i - start;
                    if (wLen >= 2 && wLen <= 4 && wLen == prevLen &&
                        string.Compare(text, prevStart, text, start, wLen, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        return true;
                    }
                    prevStart = start;
                    prevLen = wLen;
                }
                else if (c == ' ' || c == '\t')
                {
                    i++;
                }
                else
                {
                    prevStart = -1;
                    prevLen = 0;
                    i++;
                }
            }
            return false;
        }

        private static bool IsClassicalPaliNissayaHeadBeforeComma(string text, int spaceStartIndex, MyanmarTrie? trie)
        {
            if (spaceStartIndex <= 0)
                return false;

            int tokEnd = spaceStartIndex;
            int tokStart = tokEnd;
            while (tokStart > 0 && text[tokStart - 1] >= '\u1000' && text[tokStart - 1] <= '\u103F')
                tokStart--;

            int tokLen = tokEnd - tokStart;
            if (tokLen < 2)
                return false;

            // Must stand at start of paragraph/line or right after sentence boundary (Pali Nissaya '<PaliWord> ၊ <Gloss>')
            int before = tokStart - 1;
            while (before >= 0 && (text[before] == ' ' || text[before] == '\t'))
                before--;
            if (before >= 0 && text[before] != '\u104B' && text[before] != '\r' && text[before] != '\n')
                return false;

            // Pali Nissaya lemmas contain zero Burmese non-kinzi killed codas (\u103A) and zero Burmese creaky/visarga tones (\u1037/\u1038)
            for (int k = tokStart; k < tokEnd; k++)
            {
                char ch = text[k];
                if (ch == '\u1037' || ch == '\u1038')
                    return false;
                if (ch == '\u103A' && (k + 1 >= tokEnd || text[k + 1] != '\u1039'))
                    return false;
            }

            string normTok = UnicodeNormalizer.NormalizeCanonicalOrder(text.Substring(tokStart, tokLen));
            if (trie != null && !trie.ContainsExactWord(normTok))
                return true;

            // Also shield classical Pali Nissaya lemmas that exist in the lexicon (e.g. 'ပတိရူပေ ၊ သင့်တင့်သော ဂုဏ်သဘော၌။')
            char last = normTok[normTok.Length - 1];
            if (last == '\u1036' || last == '\u1031')
                return true;
            for (int k = 0; k < normTok.Length; k++)
            {
                char c = normTok[k];
                if (c == '\u1039' || (c >= '\u100B' && c <= '\u100F') || c == '\u1020' || c == '\u103F')
                    return true;
            }
            return trie == null && (last == '\u102D' || last == '\u102F');
        }

        internal static bool IsPrecededByEnglishOrTechnicalToken(string text, int numIndex)
        {
            // Guard paragraph-leading numbered list items like '1. ...' or '1) ...'
            int k = numIndex - 1;
            int openParensSkipped = 0;
            while (k >= 0 && (text[k] == ' ' || text[k] == '\t' || ((text[k] == '(' || text[k] == '（') && openParensSkipped < 1)))
            {
                if (text[k] == '(' || text[k] == '（')
                    openParensSkipped++;
                k--;
            }

            if (k < 0)
                return false;

            char prev = text[k];
            if ((prev >= 'A' && prev <= 'Z') || (prev >= 'a' && prev <= 'z') ||
                (prev >= '0' && prev <= '9') ||
                prev == '-' || prev == '–' || prev == '/' || prev == '.' || prev == ':' || prev == '@' || prev == '#' || prev == '$')
            {
                return true;
            }

            // Walk through immediately preceding non-space token to verify it contains no ASCII Latin/digit characters (e.g. 'Post (2) ခု', 'MP4 3 ခု')
            while (k >= 0 && !char.IsWhiteSpace(text[k]) && text[k] != '\u104A' && text[k] != '\u104B')
            {
                char c = text[k];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return true;
                k--;
            }

            return false;
        }

        internal static string ConvertAsciiDigitsToMyanmar(string asciiNum)
        {
            char[] buf = asciiNum.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] >= '0' && buf[i] <= '9')
                    buf[i] = (char)('\u1040' + (buf[i] - '0'));
            }
            return new string(buf);
        }

        [ThreadStatic]
        private static (char Ch, int Index)[]? t_bracketStack;

        private static void AnalyzeUnpairedBracketsAndQuotes(string text, List<ProofingIssue> issues)
        {
            int len = text.Length;
            var bracketStack = t_bracketStack ??= new (char Ch, int Index)[32];
            int stackCount = 0;

            int i = 0;
            while (i < len)
            {
                char c = text[i];

                // Skip URLs ('http://...', 'https://...', 'www....') so Wikipedia/web URLs with parens never false-trigger
                if ((c == 'h' || c == 'H' || c == 'w' || c == 'W') && IsUrlStartAt(text, i, out int urlEnd))
                {
                    i = urlEnd;
                    continue;
                }

                if (c == '(' || c == '[' || c == '（' || c == '【')
                {
                    if (stackCount < bracketStack.Length)
                    {
                        bracketStack[stackCount++] = (c, i);
                    }
                }
                else if (c == ')' || c == ']' || c == '）' || c == '】')
                {
                    if (stackCount == 0)
                    {
                        if ((c == ')' || c == '）') && IsStandaloneListMarkerOrEmoticonCloseParen(text, i))
                        {
                            i++;
                            continue;
                        }

                        if (!OverlapsExistingIssue(issues, i, 1))
                        {
                            EmitUnmatchedCloseBracketIssue(text, i, c, issues);
                        }
                    }
                    else
                    {
                        var (openCh, openIdx) = bracketStack[--stackCount];
                        if (!AreMatchingBrackets(openCh, c) && !IsHalfOpenMathInterval(text, openIdx, i, openCh, c))
                        {
                            char expectedClose = GetMatchingCloseBracket(openCh);
                            if (!OverlapsExistingIssue(issues, i, 1))
                            {
                                AddGrammarOrSpellingIssue(
                                    issues, i, 1, c.ToString(), expectedClose.ToString(),
                                    ProofingIssueType.PunctuationError,
                                    $"လက်သည်းကွင်း အဖွင့် '{openCh}' နှင့် အပိတ် '{c}' မကိုက်ညီပါ — '{expectedClose}' ဖြင့် ပိတ်ရမည်။",
                                    $"ကိုက်ညီသော အပိတ်သင်္ကေတ ('{expectedClose}')", "PUNCT", true,
                                    $"ရှေ့တွင် '{openCh}' ဖြင့် ဖွင့်ထားသဖြင့် တူညီသော အပိတ်သင်္ကေတ '{expectedClose}' ကိုသာ သုံးနှုန်းရမည်။",
                                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                    requiresReviewBeforeBatchFix: true);
                            }
                        }
                    }
                }
                i++;
            }

            for (int s = 0; s < stackCount; s++)
            {
                var (openCh, openIdx) = bracketStack[s];
                if (!OverlapsExistingIssue(issues, openIdx, 1))
                {
                    EmitUnclosedOpenBracketIssue(text, openIdx, openCh, issues);
                }
            }

            AnalyzeCurlyAndMismatchedQuotes(text, issues);
        }

        private static void EmitUnmatchedCloseBracketIssue(string text, int closeIdx, char closeCh, List<ProofingIssue> issues)
        {
            char expectedOpen = GetMatchingOpenBracket(closeCh);
            if (closeIdx > 0 && text[closeIdx - 1] == closeCh && !OverlapsExistingIssue(issues, closeIdx - 1, 2))
            {
                string dup = new string(closeCh, 2);
                AddGrammarOrSpellingIssue(
                    issues, closeIdx - 1, 2, dup, closeCh.ToString(),
                    ProofingIssueType.PunctuationError,
                    $"အပိတ်လက်သည်းကွင်း '{dup}' နှစ်ထပ်ရိုက်မိနေပါသည် — '{closeCh}' တစ်ခုသာ ထားရှိရမည်။",
                    $"နှစ်ထပ် '{closeCh}' ဖယ်ရှားရန်", "PUNCT", true,
                    $"အဖွင့် '{expectedOpen}' တစ်ခုတည်းသာရှိသဖြင့် အပိတ် '{closeCh}' တစ်ခုပိုနေပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
                return;
            }

            if (closeIdx > 0 && !char.IsWhiteSpace(text[closeIdx - 1]) && !OverlapsExistingIssue(issues, closeIdx - 1, 2))
            {
                string span = text.Substring(closeIdx - 1, 2);
                string withoutClose = text[closeIdx - 1].ToString();
                AddGrammarOrSpellingIssue(
                    issues, closeIdx - 1, 2, span, withoutClose,
                    ProofingIssueType.PunctuationError,
                    $"ရှေ့တွင် အဖွင့် '{expectedOpen}' မပါဘဲ အပိတ် '{closeCh}' တစ်ခုတည်း ဖြစ်နေပါသည် — စိစစ်ပြင်ဆင်ပါ။",
                    $"ပိုနေသော '{closeCh}' ဖယ်ရှားရန်", "PUNCT", true,
                    $"ဤစာပိုဒ်အတွင်း အဖွင့်လက်သည်းကွင်း '{expectedOpen}' မတွေ့ရှိရဘဲ အပိတ် '{closeCh}' ပါဝင်နေပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
        }

        private static bool EndsWithEllipsisTruncation(string text, int limitExclusive)
        {
            int idx = Math.Min(limitExclusive, text.Length);
            while (idx > 0 && (char.IsWhiteSpace(text[idx - 1]) || text[idx - 1] == '\u104B' || text[idx - 1] == '\uFFFD'))
                idx--;

            if (idx <= 0)
                return false;

            if (text[idx - 1] == '\u2026')
                return true;

            if (idx >= 2 && text[idx - 1] == '.' && text[idx - 2] == '.')
                return true;

            return false;
        }

        private static bool HasEllipsisTruncationAfterOpen(string text, int openIdx, int limitExclusive)
        {
            if (EndsWithEllipsisTruncation(text, limitExclusive))
                return true;

            int searchLen = limitExclusive - (openIdx + 1);
            if (searchLen >= 1 && text.IndexOf('\u2026', openIdx + 1, searchLen) >= 0)
                return true;

            if (searchLen >= 2 && text.IndexOf("..", openIdx + 1, searchLen, StringComparison.Ordinal) >= 0)
                return true;

            return false;
        }

        private static void EmitUnclosedOpenBracketIssue(string text, int openIdx, char openCh, List<ProofingIssue> issues)
        {
            if (HasEllipsisTruncationAfterOpen(text, openIdx, text.Length))
                return;

            char expectedClose = GetMatchingCloseBracket(openCh);
            int len = text.Length;

            // Find clean natural end before trailing ။ or newline so user gets a 1-click close option while staying unchecked by default
            int endLimit = len;
            while (endLimit > openIdx + 1 && (char.IsWhiteSpace(text[endLimit - 1]) || text[endLimit - 1] == '\u104B'))
                endLimit--;

            int spanLen = endLimit - openIdx;
            if (spanLen >= 2 && spanLen <= 48 && !OverlapsExistingIssue(issues, openIdx, spanLen))
            {
                string rawSpan = text.Substring(openIdx, spanLen);
                string closedSpan = rawSpan + expectedClose;
                AddGrammarOrSpellingIssue(
                    issues, openIdx, spanLen, rawSpan, closedSpan,
                    ProofingIssueType.PunctuationError,
                    $"လက်သည်းကွင်း အဖွင့် '{openCh}' ကို ပြန်မပိတ်ရသေးပါ — အပိတ် '{expectedClose}' ထည့်သွင်းရန် စိစစ်ပါ။",
                    $"အပိတ် '{expectedClose}' ထည့်သွင်းရန်", "PUNCT", true,
                    $"ဤစာပိုဒ်အတွင်း လက်သည်းကွင်း '{openCh}' ဖွင့်ထားသော်လည်း သက်ဆိုင်ရာ အပိတ် '{expectedClose}' ကျန်ခဲ့ပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
            else if (openIdx + 1 < len && !char.IsWhiteSpace(text[openIdx + 1]) && !OverlapsExistingIssue(issues, openIdx, 2))
            {
                string rawPair = text.Substring(openIdx, 2);
                string stripped = text[openIdx + 1].ToString();
                AddGrammarOrSpellingIssue(
                    issues, openIdx, 2, rawPair, stripped,
                    ProofingIssueType.PunctuationError,
                    $"လက်သည်းကွင်း အဖွင့် '{openCh}' အတွက် အပိတ် '{expectedClose}' မတွေ့ရှိရပါ — စိစစ်ပြင်ဆင်ပါ။",
                    $"ပိုနေသော '{openCh}' ဖယ်ရှားရန် (သို့မဟုတ် '{expectedClose}' ပိတ်ရန်)", "PUNCT", true,
                    $"ဤစာပိုဒ်အတွင်း လက်သည်းကွင်း '{openCh}' ဖွင့်ထားသော်လည်း အပိတ် '{expectedClose}' မပါရှိသေးပါ။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
        }

        private static void AnalyzeCurlyAndMismatchedQuotes(string text, List<ProofingIssue> issues)
        {
            int len = text.Length;
            var openCurlyStack = new List<int>(4);
            int openDoubleSingleIdx = -1;
            int openStraightBeforeMmIdx = -1;

            int i = 0;
            while (i < len)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                {
                    openCurlyStack.Clear();
                    openDoubleSingleIdx = -1;
                    openStraightBeforeMmIdx = -1;
                    i++;
                    continue;
                }

                // Recognize double single-quotes ‘‘...’’ typed instead of “...”
                if (c == '\u2018' && i + 1 < len && text[i + 1] == '\u2018')
                {
                    openDoubleSingleIdx = i;
                    i += 2;
                    continue;
                }
                if (c == '\u2019' && i + 1 < len && text[i + 1] == '\u2019')
                {
                    openDoubleSingleIdx = -1;
                    if (openCurlyStack.Count > 0)
                        openCurlyStack.RemoveAt(openCurlyStack.Count - 1);
                    i += 2;
                    continue;
                }

                if (c == '\u201C') // “ (supports nested quotes “... “...” ...” cleanly)
                {
                    openCurlyStack.Add(i);
                    openStraightBeforeMmIdx = -1;
                }
                else if (c == '\u201D') // ”
                {
                    if (openCurlyStack.Count > 0)
                    {
                        openCurlyStack.RemoveAt(openCurlyStack.Count - 1);
                    }
                    else if (openDoubleSingleIdx >= 0)
                    {
                        openDoubleSingleIdx = -1;
                    }
                    else if (openStraightBeforeMmIdx >= 0)
                    {
                        // Opened with straight ASCII " and closed with curly ” -> offer converting opening " to “
                        int sIdx = openStraightBeforeMmIdx;
                        openStraightBeforeMmIdx = -1;
                        if (!OverlapsExistingIssue(issues, sIdx, 1))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, sIdx, 1, "\"", "\u201C",
                                ProofingIssueType.PunctuationError,
                                "မျက်တောင်အဖွင့် '\"' နှင့် အပိတ် '”' ပုံစံမတူဖြစ်နေပါသည် — အဖွင့် '“' ဖြင့် တစ်ပြေးညီ ရေးသားသင့်ပါသည်။",
                                "အဖွင့်မျက်တောင် ('“')", "PUNCT", true,
                                "အပိတ်တွင် ကွေးသောမျက်တောင် ('”' U+201D) သုံးထားသဖြင့် အဖွင့်၌လည်း '“' (U+201C) ကို တစ်ပြေးညီ သုံးနှုန်းရမည်။",
                                "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                requiresReviewBeforeBatchFix: true);
                        }
                    }
                    else if (!IsInchOrArcsecondSymbol(text, i))
                    {
                        if (i > 0 && !char.IsWhiteSpace(text[i - 1]) && !OverlapsExistingIssue(issues, i - 1, 2))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, i - 1, 2, text.Substring(i - 1, 2), text[i - 1].ToString(),
                                ProofingIssueType.PunctuationError,
                                "ရှေ့တွင် အဖွင့်မျက်တောင် '“' မပါဘဲ အပိတ်မျက်တောင် '”' တစ်ခုတည်း ဖြစ်နေပါသည် — စိစစ်ပြင်ဆင်ပါ။",
                                "ပိုနေသော '”' ဖယ်ရှားရန်", "PUNCT", true,
                                "ဤစာပိုဒ်အတွင်း အဖွင့်မျက်တောင် '“' မပါရှိဘဲ အပိတ်မျက်တောင် '”' သီးသန့်ဖြစ်နေပါသည်။",
                                "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                requiresReviewBeforeBatchFix: true);
                        }
                    }
                }
                else if (c == '"')
                {
                    int topCurlyIdx = openCurlyStack.Count > 0 ? openCurlyStack[openCurlyStack.Count - 1] : -1;
                    if (topCurlyIdx >= 0 && i > topCurlyIdx + 1 && !char.IsWhiteSpace(text[i - 1]) && !IsInchOrArcsecondSymbol(text, i))
                    {
                        // Opened with curly “ and closed with straight ASCII " -> offer converting " to ”
                        openCurlyStack.RemoveAt(openCurlyStack.Count - 1);
                        if (!OverlapsExistingIssue(issues, i, 1))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, i, 1, "\"", "\u201D",
                                ProofingIssueType.PunctuationError,
                                "အဖွင့်မျက်တောင် '“' နှင့် ကိုက်ညီစေရန် အပိတ်၌ '\"' အစား '”' (အပိတ်မျက်တောင်) သုံးနှုန်းရမည်။",
                                "အပိတ်မျက်တောင် ('”')", "PUNCT", true,
                                "ရှေ့တွင် ကွေးသောအဖွင့်မျက်တောင် ('“' U+201C) သုံးထားသဖြင့် အပိတ်၌လည်း '”' (U+201D) ဖြင့် ပိတ်ရမည်။",
                                "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                                requiresReviewBeforeBatchFix: true);
                        }
                    }
                    else if (openStraightBeforeMmIdx >= 0)
                    {
                        // Balanced straight quotes "..." -> close cleanly
                        openStraightBeforeMmIdx = -1;
                    }
                    else if (openCurlyStack.Count == 0 &&
                             !IsInchOrArcsecondSymbol(text, i) &&
                             (i == 0 || char.IsWhiteSpace(text[i - 1]) || text[i - 1] == '(' || text[i - 1] == '[' ||
                              text[i - 1] == '\u104A' || text[i - 1] == '\u104B' || text[i - 1] == ':') &&
                             i + 1 < len &&
                             (IsMyanmarRunLetterOrMark(text[i + 1]) || char.IsLetterOrDigit(text[i + 1])))
                    {
                        openStraightBeforeMmIdx = i;
                    }
                }

                i++;
            }

            for (int q = 0; q < openCurlyStack.Count; q++)
            {
                EmitUnclosedOpenQuoteIssue(text, openCurlyStack[q], len, issues);
            }
        }

        private static void EmitUnclosedOpenQuoteIssue(string text, int openIdx, int limitExclusive, List<ProofingIssue> issues)
        {
            if (HasEllipsisTruncationAfterOpen(text, openIdx, limitExclusive))
                return;

            int endPos = limitExclusive;
            while (endPos > openIdx + 1 && char.IsWhiteSpace(text[endPos - 1]))
                endPos--;

            // Place closing ” right before trailing ။ if sentence ends with ။, or at endPos
            int insertBeforePaukMa = endPos;
            if (insertBeforePaukMa > openIdx + 1 && text[insertBeforePaukMa - 1] == '\u104B')
                insertBeforePaukMa--;

            int spanLen = insertBeforePaukMa - openIdx;
            if (spanLen >= 2 && spanLen <= 64 && !OverlapsExistingIssue(issues, openIdx, spanLen))
            {
                string rawSpan = text.Substring(openIdx, spanLen);
                string closedSpan = rawSpan + "\u201D";
                AddGrammarOrSpellingIssue(
                    issues, openIdx, spanLen, rawSpan, closedSpan,
                    ProofingIssueType.PunctuationError,
                    "အဖွင့်မျက်တောင် '“' ကို ပြန်မပိတ်ရသေးပါ — အပိတ်မျက်တောင် '”' ထည့်သွင်းရန် စိစစ်ပါ။",
                    "အပိတ်မျက်တောင် ('”') ထည့်သွင်းရန်", "PUNCT", true,
                    "ဤစာပိုဒ်အတွင်း မျက်တောင်အဖွင့် ('“' U+201C) ရှိသော်လည်း အပိတ်မျက်တောင် ('”' U+201D) ကျန်ခဲ့ပါသည်။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
            else if (openIdx + 1 < text.Length && !char.IsWhiteSpace(text[openIdx + 1]) && !OverlapsExistingIssue(issues, openIdx, 2))
            {
                AddGrammarOrSpellingIssue(
                    issues, openIdx, 2, text.Substring(openIdx, 2), text[openIdx + 1].ToString(),
                    ProofingIssueType.PunctuationError,
                    "အဖွင့်မျက်တောင် '“' အတွက် အပိတ်မျက်တောင် '”' မတွေ့ရှိရပါ — စိစစ်ပြင်ဆင်ပါ။",
                    "ပိုနေသော '“' ဖယ်ရှားရန် (သို့မဟုတ် '”' ပိတ်ရန်)", "PUNCT", true,
                    "ဤစာပိုဒ်အတွင်း မျက်တောင်အဖွင့် ('“') ရှိသော်လည်း အပိတ်မျက်တောင် ('”') မပါရှိသေးပါ။",
                    "စာတမ်းဖွဲ့စည်းပုံနှင့် ပုဒ်ဖြတ်ပုဒ်ရပ် စံနှုန်း",
                    requiresReviewBeforeBatchFix: true);
            }
        }

        private static bool IsInchOrArcsecondSymbol(string text, int quoteIdx)
        {
            if (quoteIdx <= 0)
                return false;
            char prev = text[quoteIdx - 1];
            return (prev >= '0' && prev <= '9') || (prev >= '\u1040' && prev <= '\u1049');
        }

        private static bool IsUrlStartAt(string text, int idx, out int urlEnd)
        {
            urlEnd = idx;
            int rem = text.Length - idx;
            if (rem < 8)
                return false;

            bool isUrl = string.Compare(text, idx, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(text, idx, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(text, idx, "www.", 0, 4, StringComparison.OrdinalIgnoreCase) == 0;
            if (!isUrl)
                return false;

            int k = idx + 4;
            while (k < text.Length && !char.IsWhiteSpace(text[k]) && text[k] != '\u104A' && text[k] != '\u104B')
                k++;
            urlEnd = k;
            return true;
        }

        private static bool AreMatchingBrackets(char open, char close)
        {
            return (open == '(' && (close == ')' || close == '）')) ||
                   (open == '（' && (close == '）' || close == ')')) ||
                   (open == '[' && close == ']') ||
                   (open == '【' && close == '】');
        }

        private static char GetMatchingCloseBracket(char open) => open switch
        {
            '(' => ')',
            '（' => '）',
            '[' => ']',
            '【' => '】',
            _ => ')'
        };

        private static char GetMatchingOpenBracket(char close) => close switch
        {
            ')' => '(',
            '）' => '（',
            ']' => '[',
            '】' => '【',
            _ => '('
        };

        private static bool IsHalfOpenMathInterval(string text, int openIdx, int closeIdx, char openCh, char closeCh)
        {
            if (!((openCh == '[' && closeCh == ')') || (openCh == '(' && closeCh == ']')))
                return false;
            int len = closeIdx - openIdx - 1;
            if (len < 3 || len > 24 || text.IndexOf(',', openIdx + 1, len) < 0)
                return false;
            for (int k = openIdx + 1; k < closeIdx; k++)
            {
                char ch = text[k];
                if (!((ch >= '0' && ch <= '9') || (ch >= '\u1040' && ch <= '\u1049') ||
                      ch == ',' || ch == '.' || ch == '-' || ch == '+' || ch == ' ' || ch == '∞'))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsStandaloneListMarkerOrEmoticonCloseParen(string text, int closeIdx)
        {
            int k = closeIdx - 1;
            if (k < 0)
                return false;

            char prev = text[k];
            // Emoticons ':)', ';)', '=)'
            if (prev == ':' || prev == ';' || prev == '=')
                return true;

            // Digits 1) .. 999) or ၁) .. ၉၉၉)
            if ((prev >= '0' && prev <= '9') || (prev >= '\u1040' && prev <= '\u1049'))
            {
                int start = k;
                while (start > 0 && (k - start) < 3 &&
                       ((text[start - 1] >= '0' && text[start - 1] <= '9') || (text[start - 1] >= '\u1040' && text[start - 1] <= '\u1049')))
                {
                    start--;
                }
                return IsListMarkerLeftBoundary(text, start);
            }

            // Single ASCII letter a), b) or short Roman numeral i), ii), iii), iv), v), vi)..x)
            if ((prev >= 'A' && prev <= 'Z') || (prev >= 'a' && prev <= 'z'))
            {
                int start = k;
                while (start > 0 && (k - start) < 3 && IsRomanNumeralChar(text[start - 1]))
                {
                    start--;
                }
                if (start == k)
                    return IsListMarkerLeftBoundary(text, start);

                string roman = text.Substring(start, k - start + 1).ToLowerInvariant();
                if (roman == "ii" || roman == "iii" || roman == "iv" || roman == "vi" || roman == "vii" || roman == "viii" || roman == "ix")
                    return IsListMarkerLeftBoundary(text, start);
                return false;
            }

            // Single Myanmar consonant list marker က), ခ), ဂ), ဃ), င)
            if (prev >= '\u1000' && prev <= '\u1021')
            {
                return IsListMarkerLeftBoundary(text, k);
            }

            return false;
        }

        private static bool IsRomanNumeralChar(char c)
        {
            char lo = char.ToLowerInvariant(c);
            return lo == 'i' || lo == 'v' || lo == 'x';
        }

        private static bool IsListMarkerLeftBoundary(string text, int markerStartIdx)
        {
            if (markerStartIdx <= 0)
                return true;
            char left = text[markerStartIdx - 1];
            return char.IsWhiteSpace(left) || left == '\u104A' || left == '\u104B' || left == ':' || left == ';' || left == '—';
        }

        private static List<(int Start, int End)>? ComputeBalancedQuoteRanges(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            bool hasAnyQuoteChar = false;
            int len = text.Length;
            for (int k = 0; k < len; k++)
            {
                char c = text[k];
                if (c == '\u201C' || c == '"' || c == '\u2018')
                {
                    hasAnyQuoteChar = true;
                    break;
                }
            }
            if (!hasAnyQuoteChar)
                return null;

            List<(int Start, int End)>? ranges = null;
            int i = 0;
            while (i < len)
            {
                char ch = text[i];
                char expectedClose = '\0';
                int maxSpan = 0;
                if (ch == '\u201C') { expectedClose = '\u201D'; maxSpan = 800; }
                else if (ch == '"') { expectedClose = '"'; maxSpan = 600; }
                else if (ch == '\u2018') { expectedClose = '\u2019'; maxSpan = 400; }

                if (expectedClose != '\0')
                {
                    int limit = Math.Min(len, i + maxSpan);
                    int closeIdx = -1;
                    for (int j = i + 1; j < limit; j++)
                    {
                        char cj = text[j];
                        if (cj == '\r' || cj == '\n')
                            break;
                        if (cj == expectedClose)
                        {
                            closeIdx = j;
                            break;
                        }
                    }

                    if (closeIdx > i + 1)
                    {
                        ranges ??= new List<(int Start, int End)>(4);
                        ranges.Add((i + 1, closeIdx));
                        i = closeIdx + 1;
                        continue;
                    }
                }
                i++;
            }

            return ranges;
        }

        private static bool IsInsideAnyRange(List<(int Start, int End)>? ranges, int index, int length)
        {
            if (ranges == null)
                return false;
            int end = index + length;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (index >= r.Start && end <= r.End)
                    return true;
            }
            return false;
        }

        private static readonly string[] ClauseChainLinkers =
        {
            "သောကြောင့်", "သဖြင့်", "သော်လည်း", "ပေမဲ့", "ပေမယ့်", "ပြီးနောက်", "ပြီးမှ", "ပြီးလျှင်", "ပြီးတော့", "ပြီး", "လို့", "၍", "ရင်", "လျှင်"
        };

        private static string TrimClauseAfterConjunction(string clause)
        {
            if (string.IsNullOrEmpty(clause))
                return string.Empty;

            int cut = -1;
            for (int i = 0; i < ClauseChainLinkers.Length; i++)
            {
                string lk = ClauseChainLinkers[i];
                int hit = clause.LastIndexOf(lk, StringComparison.Ordinal);
                if (hit >= 0)
                {
                    int after = hit + lk.Length;
                    if (after >= clause.Length || char.IsWhiteSpace(clause[after]))
                    {
                        if (after > cut)
                            cut = after;
                    }
                }
            }
            return cut > 0 ? clause.Substring(cut) : clause;
        }

        private static bool IsKnownStemOrSuffixedWord(string stem, MyanmarTrie? trie)
        {
            if (string.IsNullOrEmpty(stem) || stem.Length < 2)
                return false;
            if (trie == null)
                return true;
            if (trie.ContainsExactWord(stem))
                return true;

            string[] pluralSuffixes = { "များ", "တွေ", "တို့" };
            for (int i = 0; i < pluralSuffixes.Length; i++)
            {
                string sfx = pluralSuffixes[i];
                if (stem.Length > sfx.Length + 1 && stem.EndsWith(sfx, StringComparison.Ordinal))
                {
                    string root = stem.Substring(0, stem.Length - sfx.Length);
                    if (trie.ContainsExactWord(root))
                        return true;
                }
            }
            return false;
        }

        private static bool IsKnownVerbOrAuxStem(string stem, MyanmarTrie? trie)
        {
            if (string.IsNullOrEmpty(stem))
                return false;
            string[] auxEndings = { "နေ", "ခဲ့", "ပြီး", "နိုင်", "ချင်", "တတ်", "သင့်", "ရ", "ပါ", "သွား", "လာ", "ထား", "ပေး", "များ" };
            for (int i = 0; i < auxEndings.Length; i++)
            {
                string aux = auxEndings[i];
                if (stem.Length > aux.Length && stem.EndsWith(aux, StringComparison.Ordinal))
                {
                    string baseVerb = stem.Substring(0, stem.Length - aux.Length);
                    if (trie == null || trie.ContainsExactWord(baseVerb) || baseVerb.StartsWith("မ", StringComparison.Ordinal))
                        return true;
                }
            }
            if (trie != null)
            {
                return trie.HasPosTag(stem, "V") || trie.HasPosTag(stem, "ADJ");
            }
            return true;
        }

        private void ScanUnspacedAttachedParticleTypos(
            string text,
            ulong mmMask,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie,
            List<ProofingIssue> issues)
        {
            bool isFormal = styleMode == ProofingStyleMode.FormalOfficial;

            // 1. '[Valid Noun/Place/Pronoun] + မာ' -> 'တွင် / မှာ' (FormalOfficial) or 'မှာ / တွင်' (GeneralStandard)
            if (ContainsMm(text, mmMask, "မာ"))
            {
                foreach (Match m in UnspacedAttachedMarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "မာ";
                    if (trie != null && trie.ContainsExactWord(fullJoined))
                        continue;
                    if (!IsKnownStemOrSuffixedWord(stem, trie))
                        continue;

                    string p1 = isFormal ? "တွင်" : "မှာ";
                    string p2 = isFormal ? "မှာ" : "တွင်";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"တည်နေရာပြဝိဘတ်တွင် ဟထိုး ('ှ') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ တည်နေရာပြဝိဘတ် ('~တွင်')" : "တည်နေရာပြဝိဘတ် စာလုံးပေါင်းအမှန် ('~မှာ')",
                        isFormal ? "အပြောစကား တည်နေရာပြဝိဘတ် ('~မှာ')" : "ရုံးသုံး/စာပေ တည်နေရာပြဝိဘတ် ('~တွင်')");
                }
            }

            // 2. '[Valid Noun/Pronoun] + နဲ' -> 'နှင့် / နဲ့' (FormalOfficial) or 'နဲ့ / နှင့်' (GeneralStandard)
            if (ContainsMm(text, mmMask, "နဲ"))
            {
                foreach (Match m in UnspacedAttachedNaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "နဲ";
                    if (stem.EndsWith("ခ", StringComparison.Ordinal) ||
                        stem.EndsWith("မ", StringComparison.Ordinal) ||
                        (trie != null && trie.ContainsExactWord(fullJoined)))
                    {
                        continue;
                    }
                    if (!IsKnownStemOrSuffixedWord(stem, trie))
                        continue;

                    string p1 = isFormal ? "နှင့်" : "နဲ့";
                    string p2 = isFormal ? "နဲ့" : "နှင့်";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"အတူပြ/သမ္ဗန္ဓဝိဘတ်တွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ သမ္ဗန္ဓဝိဘတ် ('~နှင့်')" : "အပြောစကား သမ္ဗန္ဓဝိဘတ်အမှန် ('~နဲ့')",
                        isFormal ? "အပြောစကား သမ္ဗန္ဓဝိဘတ်အမှန် ('~နဲ့')" : "ရုံးသုံး/စာပေ သမ္ဗန္ဓဝိဘတ် ('~နှင့်')");
                }
            }

            // 3. '[Valid Verb/Aux] + တဲ' before Noun -> 'သည့် / တဲ့' (FormalOfficial) or 'တဲ့ / သည့်' (GeneralStandard)
            if (ContainsMm(text, mmMask, "တဲ"))
            {
                foreach (Match m in UnspacedAttachedTaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "တဲ";
                    if (trie != null && trie.ContainsExactWord(fullJoined))
                        continue;
                    if (!IsKnownVerbOrAuxStem(stem, trie))
                        continue;

                    string p1 = isFormal ? "သည့်" : "တဲ့";
                    string p2 = isFormal ? "တဲ့" : "သည့်";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"နာမဝိသေသနပြုပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ နာမဝိသေသနပစ္စည်း ('~သည့်')" : "အပြောစကား နာမဝိသေသနပစ္စည်းအမှန် ('~တဲ့')",
                        isFormal ? "အပြောစကား နာမဝိသေသနပစ္စည်းအမှန် ('~တဲ့')" : "ရုံးသုံး/စာပေ နာမဝိသေသနပစ္စည်း ('~သည့်')");
                }
            }

            // 4. '[Multi-Syllable Noun / Plural] + ရဲ' before Noun -> '၏ / ရဲ့' (FormalOfficial) or 'ရဲ့ / ၏' (GeneralStandard)
            if (ContainsMm(text, mmMask, "ရဲ"))
            {
                foreach (Match m in UnspacedAttachedYaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string stem = m.Groups["Stem"].Value;
                    string fullJoined = stem + "ရဲ";
                    if (stem.EndsWith("နိုင်ငံ", StringComparison.Ordinal) ||
                        stem.EndsWith("ပြည်နယ်", StringComparison.Ordinal) ||
                        stem.EndsWith("မြို့နယ်", StringComparison.Ordinal) ||
                        (trie != null && trie.ContainsExactWord(fullJoined)))
                    {
                        continue;
                    }
                    bool hasPlural = stem.EndsWith("များ", StringComparison.Ordinal) ||
                                     stem.EndsWith("တွေ", StringComparison.Ordinal) ||
                                     stem.EndsWith("တို့", StringComparison.Ordinal);
                    if (!hasPlural && (trie == null || !trie.HasPosTag(stem, "N") || MyanmarSyllableBreaker.BreakIntoSyllables(stem, isPreNormalized: true).Count < 2))
                        continue;

                    string p1 = isFormal ? "၏" : "ရဲ့";
                    string p2 = isFormal ? "ရဲ့" : "၏";
                    AddModeAwareAttachedParticleIssue(
                        issues, g.Index, g.Length, g.Value, p1, p2, isFormal,
                        $"ပိုင်ဆိုင်ပြဝိဘတ်တွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — '{p1}' ဟု ရေးသားရမည်။",
                        isFormal ? "ရုံးသုံး/စာပေ ပိုင်ဆိုင်ပြဝိဘတ် ('~၏')" : "အပြောစကား ပိုင်ဆိုင်ပြဝိဘတ်အမှန် ('~ရဲ့')",
                        isFormal ? "အပြောစကား ပိုင်ဆိုင်ပြဝိဘတ်အမှန် ('~ရဲ့')" : "ရုံးသုံး/စာပေ ပိုင်ဆိုင်ပြဝိဘတ် ('~၏')");
                }
            }
        }

        private static void AddModeAwareAttachedParticleIssue(
            List<ProofingIssue> issues,
            int index,
            int length,
            string raw,
            string primary,
            string secondary,
            bool isFormal,
            string message,
            string primaryCue,
            string secondaryCue)
        {
            issues.Add(new ProofingIssue
            {
                StartIndex = index,
                Length = length,
                OriginalText = raw,
                IssueType = ProofingIssueType.SpellingError,
                Message = message,
                GrammaticalReasoning = $"ရှေ့နာမ်/ကြိယာပုဒ်နောက်တွင် တစ်ဆက်တည်း တွဲရိုက်ထားသော ဝိဘတ် '{raw}' စာလုံးပေါင်း ချို့ယွင်းနေသဖြင့် '{primary}' (သို့မဟုတ် '{secondary}') ဖြင့် ရေးသားရမည်။",
                AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် စာလုံးပေါင်းစံနှုန်း)",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate
                    {
                        Replacement = primary,
                        MeaningCue = primaryCue,
                        PosTag = "PART",
                        IsOfficialThatpon = isFormal,
                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primary),
                        Score = 100.0
                    },
                    new SuggestionCandidate
                    {
                        Replacement = secondary,
                        MeaningCue = secondaryCue,
                        PosTag = "PART",
                        IsOfficialThatpon = !isFormal,
                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(secondary),
                        Score = 95.0
                    }
                }
            });
        }

        private static readonly Regex OrphanSpaceDetachedCodaPattern = new Regex(
            @"(?<Prefix>[\u1000-\u103F]{0,14}?)(?<OpenSyl>[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032\u1036]*)[ \t]+(?<Coda>[\u1000-\u1021]\u103A[\u1037\u1038]?|[\u1004\u1014\u1019\u100A\u101A]\u1037\u103A)(?![\u102B-\u1039\u103B-\u103E])",
            RegexOptions.Compiled);

        private static readonly Regex BrokenViramaStackAcrossSpacePattern = new Regex(
            @"(?<Left>[\u1000-\u103F]*?[\u1000-\u1021]\u1039)[ \t]+(?<Right>[\u1000-\u1021][\u1000-\u103F]{0,14})",
            RegexOptions.Compiled);

        private static readonly Regex ImpossibleSyllableVowelMedialPattern = new Regex(
            @"(?:(?<ShortIVisarga>[\u1000-\u1021][\u103B-\u103E]*\u102D\u1038)|(?<DoubleMedial>[\u1000-\u1021](?:\u103B\u103C|\u103C\u103B)[\u102B-\u103E]*[\u1000-\u1021]?\u103A?[\u1037\u1038]?)|(?<MissingAsatVisarga>[\u1000-\u1021][\u103B-\u103E]*[\u1004\u1014\u1019\u101A]\u1038)|(?<=နိုင်ငံ|ကမ္ဘာ့|ဒေသ|ပြည်နယ်|မြို့နယ်)(?<TaWun>တဝန်း)(?=ရှိ|၌|တွင်|မှာ|လုံး))",
            RegexOptions.Compiled);

        private static readonly Regex RedundantDoubleToneSyllablePattern = new Regex(
            @"(?<Syl>[\u1000-\u102A][\u103B-\u103E]*[\u102B-\u1036]*(?:[\u1000-\u1021]\u103A)?(?:\u1038\u1037|\u1037\u1038))",
            RegexOptions.Compiled);

        private static readonly Regex CorruptedZawgyiAwNasalCodaPattern = new Regex(
            @"(?<Syl>[\u1000-\u1021][\u103B-\u103E]*\u1031[\u102B\u102C]\u1037[\u1004\u1014\u1019]\u103A\u1038)",
            RegexOptions.Compiled);

        private static readonly Regex CorruptedAsciiAPrefixPattern = new Regex(
            @"(?<![A-Za-z0-9])(?:(?<Target>A[\u102B-\u103E]+)|(?<Target2>A[ \t]+(?<Stem>[\u1000-\u1021][\u1000-\u103F]{1,16})))(?![A-Za-z])",
            RegexOptions.Compiled);

        private static readonly Regex AffirmativeEmphaticBaePattern = new Regex(
            @"(?:(?:^|[\u104A\u104B]\s*)(?![^\u104A\u104B\r\n]*?(?<![\u1000-\u103F])မ[\u1000-\u1021])[^\u104A\u104B\r\n]{1,64}?(?:ပါ|တာ|သလို|ရုံ)(?<Target>ဘဲ)(?=[\s\u104A\u104B]|$)|(?<!မ(?![\u102B-\u103E\u103A])[\u1000-\u103F]{1,14})ပါ[ \t]*(?<Target2>ဘဲ)(?=[\s\u104A\u104B]|$)|(?:သလို|ဖို့|မို့|တာ)[ \t]*(?<Target3>ဘဲ)(?=[\s\u104A\u104B]|$))",
            RegexOptions.Compiled);

        private static readonly Regex SpacedVerbalParticleTypoPattern = new Regex(
            @"(?:(?<=(?:သွား|လာ|လုပ်|စား|သောက်|ပြော|ကြည့်|ဖတ်|ရေး|နေ|ဝယ်|ပေး|ယူ|တွေ့|ရောက်)[ \t]+)(?<Khae>ခဲ)(?=[ \t]*(?:တယ်|သည်|ပါ))|(?<=(?:သွား|လာ|လုပ်|စား|သောက်|ပြော|ပေး|ယူ)[ \t]+)(?<Mae>မဲ)(?=[ \t]*(?:လို့|ဟု))|(?<=(?:ဆောက်|လုပ်|သွား|လာ|စား|ပြော|ဖတ်|ရေး|ရှောင်|သင်ယူ|ကြိုးစား))(?<Pho>ဖို)(?=\s+(?:ခက်ခဲ|လွယ်ကူ|လိုအပ်|ကောင်း|သင့်|အတွက်))|(?<=(?:ရှောင်|လုပ်|စား|သွား|ပြော|သတိထား|လိုက်နာ|ထည့်ပေး))(?<Thing>သင်)(?=တယ်|သည်|ပါတယ်|ပါသည်)|(?<KyoKa>ကြိုက)(?=တယ်|သည်|ပါ)|(?<=(?:စို|ကျ|ပျက်|မော|သေ|ရှုံး|နောက်ကျ))(?<IrrealisMar>မာ)(?=ပဲ|လား|လဲ|စိုး)|(?<=[\u1000-\u103F])(?<PaTaYa>ပါတယ)(?=[\s\u104A\u104B]|$))",
            RegexOptions.Compiled);

        private static readonly Regex FormalCabinetBrokenCompoundPattern = new Regex(
            @"အစိုးရ[ \t]+အဖွဲ့(?!ဝင်|အသီးသီး|အစည်း)",
            RegexOptions.Compiled);

        private static readonly Regex VerbStemMissingCreakyPhoPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{2,36}?)(?<!မီး|အုတ်|ထုံး|ပန်းပဲ|မီးသွေး|လူပျို)(?<Pho2>ဖို)(?=က[\s\u1000-\u1021]|နဲ့|နှင့်|နှင့်|အတွက်|ပဲ|လည်း|\s*(?:လိုအပ်|လိုအဘ်|လိုပါ|လိုတယ်|လိုသည်|ခက်ခဲ|ခက်ထန်|လွယ်ကူ|ကောင်း|သင့်|သင့်|အတွက်|တာဝန်|အထောက်အကူ|အရေးကြီး|အဓိက|ကြိုးစား|ဆောင်ရွက်|လေ့ကျင့်|လေ့ကျင့်|ညွှန်ကြား))",
            RegexOptions.Compiled);

        private static readonly Regex LexiconVerbGerundChinPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Stem>[\u1000-\u103F]{2,32}?)(?<GerundChin2>ချင်း)(?=က[\s\u1000-\u1021]|သည်[\s\u104A\u104B]|ဟာ[\s\u1000-\u1021]|ကြောင့်|ကြောင့်|ဖြင့်|ဖြင့်|ဖြင်\s|ဖျင်\s)",
            RegexOptions.Compiled);

        private static readonly Regex ExtendedParticleAndMorphoSyntaxPattern = new Regex(
            @"(?:(?<!နွား|ကျွဲ|ကုလားအုတ်)(?<Bho>ဘို့)(?=\s*(?:အတွက်|လို|က\s|ပါ|ပဲ|မဟုတ်|ဖြစ်))|(?<=(?:မှု|ခြင်း|ချက်|များ|တွေ))(?<Nhae>နှဲ့)(?=[\s\u1000-\u102A])|(?<=(?:ကြ|ပါ))(?<FinalTae>တဲ)(?=\s*။)|(?<=(?:ဆေးလိပ်သောက်|အရက်သောက်|လမ်းလျှောက်|စာဖတ်|အားကစားလုပ်|အိပ်ရေးပျက်))(?<GerundChin>ချင်း)(?=က|သည်|ဟာ|ကြောင့်|ဖြင့်)|(?<=(?:ထိရောက်|သေချာ|ပွင့်လင်းမြင်သာ|အောင်မြင်|ကောင်းမွန်))(?<AttrThaw>သော)(?=\s+(?:ဖြေရှင်း|ဆောင်ရွက်|လုပ်ဆောင်|ပြောဆို|ကာကွယ်|ကူညီ|အသုံးပြု)[\u1000-\u103F]*(?:နိုင်|ရ|ခဲ့|ပါ|သည်|တယ်|မည်|မယ်))|(?<=[\u1000-\u103F]{2,20})(?<!တစ်ဦး|တစ်ယောက်|လူ|သူ|မိသားစု)(?<LocKo>ကို)(?=\s+နေထိုင်(?:ပါ)?(?<LocEnd>သည်|တယ်))|(?<=ပြင်ဆင်|ဆောင်ရွက်|ကြိုးပမ်း|ထိန်းသိမ်း)(?<ContKar>ကာ)(?=\s+ရှိ(?:ပါ)?သည်))",
            RegexOptions.Compiled);

        // SOV Clause-Final Verb Restorer: detects English-calqued SVO clauses where a finite non-creaky main verb precedes postpositional NP arguments right before '။' / '.' / 'ဟု' / 'ဆိုတာ'
        private static readonly Regex SovVerbInversionPattern = new Regex(
            @"(?<=(?:က|မှ|ကို|အား|နှင့်|နှင့်|နဲ့|ဖြင့်|ဖြင့်|မှာ|တွင်|၌|ဖို့|ဖို|စွာ|လည်း|ဘဲ|ပဲ|အစ်ကို|အစ်မ|ညီမ|သူ|ကျွန်တော်|ကျွန်မ|တဲ့|သော|သည့်|သည့်)\s+)(?<Verb>[\u1000-\u103F]{2,26}?(?:ခဲ့ကြပါသည်|ခဲ့ပါသည်|ခဲ့ကြသည်|ခဲ့သည်|ချင်ကြတယ်|စေပါတယ်|အနားယူတယ်|နားယူတယ်))\s+(?<Args>(?!(?:ဟု|ဆိုတာ|လို့|သော်လည်း|ပေမဲ့|ပေမယ့်|ပြီး|၍)(?:[\s\u104A\u104B]|$))[^\u104A\u104B""“”.\r\n]{2,60}?(?:ကို|အား|သို့|ဖြင့်|ဖြင့်|တွင်|မှာ)(?:\s+(?!(?:ဟု|ဆိုတာ|လို့|သိရ|ဖော်ပြ|တွေ့ရ|ပါရှိ)(?:[\s\u104A\u104B]|$))[\u1000-\u103F]{2,20})?)(?=\s*(?:။|\.(?!\.|[0-9A-Za-z])|ဟု(?:\s|$)|ဆိုတာ(?:\s|$)))",
            RegexOptions.Compiled);

        private static readonly Regex NightmareParticleAndCaseCollisionPattern = new Regex(
            @"(?:(?<=(?:ပေး|လုပ်|သွား|လာ|စား|သောက်|ပြော|ဆို|ကြည့်|ဖတ်|ရေး|ကူညီ|စောင့်ရှောက်|စောင့်လျှောက်|ဆောင်ရွက်|ကြိုးစား))(?<KyaPlural>ကြာ)(?=ပါ(?:တယ်|သည်|မယ်|မည်))|(?<=(?:ကြ|နေ|ခဲ့|နိုင်|တတ်))(?<PatPolite>ပတ်)(?=တယ်|သည်)|(?<=လို့)(?<YaPot>ရာ)(?=တယ်|သည်|ပါတယ်|ပါသည်)|(?<=(?:ကမ္ဘာကြီး|နိုင်ငံတော်|ပြည်သူများ|ပြည်သူတွေ|ကျောင်းသားများ|သားသမီးများ|ကျန်းမာရေး|ပညာရေး|အနာဂတ်|မိသားစု|အဲဒီ|ဒီ|ဤ|ထို))(?<Ahtwet>အထွက်)(?=[\s\u104A\u104B])|(?<=(?:တစ်ခု|တစ်ယောက်|တစ်ဦး|တစ်ကောင်|နိုင်ငံ|အစိုးရ|မိသားစု|ကုမ္ပဏီ|ကျောင်းသား))(?<UnspacedYae>ရဲ)(?=\s+(?!(?:တပ်ဖွဲ့|စခန်း|အရာရှိ|မှူး|ဘော်|မေ|ဝံ့|ရင့်|စွမ်း|တိုက်|ကင်း|သား|ချုပ်))[\u1000-\u102A])|(?<=(?:လဲ|မလဲ|သလဲ|တယ်|မယ်|ဘူး|ပြီ))(?<QuotLo>လို)(?=\s+(?:[\u1000-\u103F]{1,18}(?:က|သည်|ဟာ)\s+)?(?:ယုံကြည်|ထင်|ပြော|မေး|ဆို|သိ|မှတ်|ခေါ်|ကြား))|(?:(?<=(?:ဖို့|စရာ|အစားအသောက်))(?<KaungVis1>ကောင်)(?=တယ်|သည်|ပါတယ်|ပါသည်|သော|တဲ့|တွေ)|(?<![\u1000-\u103F])မ(?<KaungVis2>ကောင်)(?=ဘူး|ပါဘူး|သေးဘူး|တဲ့|သော))|(?<=(?:လာ|သွား|ဖြစ်|ရ|သိ|နားလည်|တိုးတက်|ကောင်းမွန်))(?<AungSpur>အောင်း)(?=(?:လည်း|ပဲ)?\s*(?:[\u1000-\u103F]{1,18}(?:က|သည်|ဟာ)\s+)?(?:ကူညီ|လုပ်|ဆောင်ရွက်|ကြိုးစား|ပြုလုပ်|ပေး))|(?<=(?:သူ|လူ|ကလေး|ဝန်ထမ်း|ကျောင်းသား|ပြည်သူ|မိဘ|ဆရာ))(?<TwePlural>တွေး)(?=ကို[\s\u104A\u104B])|(?:^|[\u104A\u104B]\s*|မှာ\s+|တွင်\s+)(?<SubjKo>(?:ကျွန်တော်|ကျွန်မ|ကျွန်ုပ်|သူ|ငါ|မင်း)ကို)(?=\s+[^\u104A\u104B]{2,28}?ကို\s+[^\u104A\u104B]{0,20}?(?:ကြိုက်|နှစ်သက်|ပေး|ပြော|ပြ|ပို့|ဝယ်|စား|ဖတ်))|(?<=(?:သည့်|သည့်|သော|တဲ့)\s+[\u1000-\u103F]{2,18})(?<CopKo>ကို\s+(?<CopVerb>ဖြစ်(?:ပါ)?(?:သည်|တယ်)))(?=\s*။)|(?<=\s)(?<IrrInv>မှာ(?<IrrVerb>[\u1000-\u103F]{2,14}?)(?<IrrNeg>မဟုတ်(?:ပါ)?(?:ဘူး|ချေ|ပေ)))(?=[\s\u104A\u104B]|$))",
            RegexOptions.Compiled);

        private static readonly Regex MyanmarSentenceFinalAsciiDotPattern = new Regex(
            @"(?<=(?:ပါတယ်|ပါသည်|ပါမည်|ပါမယ်|ခဲ့သည်|ခဲ့တယ်|ဖြစ်သည်|ရှိသည်|ရမည်|ပါပြီ|ပါပီ|ပါတယ|တယ်|သည်|မယ်|မည်|ပြီ|ပီ|ဘူး|ဖူး|များသို့|ချုပ်သို့|ရုံးသို့))(?<Dot>\.)(?!\.|[0-9A-Za-z])(?=\s|$)",
            RegexOptions.Compiled);

        private static readonly Regex V64MorphoAndCaseCollisionPattern = new Regex(
            @"(?:(?<=(?:လွတ်လပ်|ခံစား|အရေးယူ|ဝင်ရောက်|ပြောဆို|လုပ်ပိုင်))(?<Khwin>ခွင်)(?=ပေး|ပြု|ရ|ရှိ|တောင်း|မရှိ|တွေ|များ)|(?<=(?:သင်ခန်းစာယူ|ဝယ်ယူ|စားသောက်|လုပ်ကိုင်|ကျောင်း))(?<YuPi>ပီ)(?=ရင်|\s+[\u1000-\u1021])|(?<=(?:ပူဇော်|ကန်တော့|ဆုတောင်း))(?<PuzawPyi>ပြီ)(?=\s+[\u1000-\u1021])|(?<=(?:သောက်|စား|ကြည့်|ကြည့်|နားထောင်|ဖတ်))(?<LoKaung>လို)(?=ကောင်း(?:တယ်|သည်|ပါ|ပြီး))|(?<=(?:စတင်တာ|ခွင့်မူချက်|ခွင့်ပြုချက်|အချက်|တစ်ခု))(?<TaPae>ပယ်)(?=\s*။)|(?<=(?:အိမ်စာတွေ|အလုပ်တွေ)\s+[^\u104A\u104B]{0,18}?)(?<LokeYae>လုပ်ရယ်)(?=\s*။)|(?<=(?:ရင်|စိတ်|အိမ်|ကျောင်း|အခန်း))(?<HtaeMa>ထဲမာ)(?=\s)|(?<=(?:ခရစ္စမတ်|မွေး|ဥပုသ်|လပြည့်|လပြည့်|နှစ်သစ်ကူး))(?<XmasNay>နေမှာ)|(?<=(?:အရမ်း|သိပ်|အလွန်))(?<LhaCreaky>လှ့)(?=တယ်|သည်|ပါ)|(?<KyawntawCreaky>ကျွန်တော်)(?=မှာ\s+[^\u104A\u104B]{2,35}?(?:ရှိ|စကား)|ရဲ့\s)|(?<KyawntawNoAsat>ကျွန်တော)(?=\s*အတွက်)|(?<=(?:လူမျိုးတွေ|သူတို့|ကျွန်တော်တို့|ဝန်ထမ်းတွေ|ကလေးတွေ|မိဘတွေ))(?<TweYa>ရ)(?=\s+အ[\u1000-\u103F]+ဆုံး)|(?<=(?:သွား|လာ|ပြု|လုပ်|ဝယ်|သုံး|တက်))(?<MaeThu>မယ်)(?=သူ(?:တွေ|များ))|(?<=(?:မနက်|နံနက်|နေ့လယ်|ညနေ|ည))(?<NyanayPaing>ပိုင်)(?=မှာ|တွင်|၌)|(?<=(?:ထုတ်လုပ်|စီးပွား|ပညာ|ကျန်းမာ|လူမှု))(?<YeLupan>ရေ)(?=လုပ်ငန်း|စနစ်|ကဏ္ဍ)|(?<PataeLar>ပါတယ်လား)(?=\s*[။?]|$)|(?<PasayLar>ပါစေလား)(?=\s*[။?]|$)|(?<=ခြင်း)(?<ChinnTae>တည်)(?=\s)|(?<=(?:တွေ|များ))(?<TweGa>ဂ)(?=\s)|(?<=လုပ်ငန်း)(?<LupanHtwin>ထွင်)(?=\s)|(?<=မရှိ)(?<MashiHaw>ဟော)(?=\s+[\u1000-\u103F]+လော)|(?<=ဟု\s+[\u1000-\u103F]{2,18}?)(?<HuHo>ဟို့)(?=\s+ပြော)|(?<=(?:မှာ|ပြော|သွား|စား|လုပ်|ပေး|ဝယ်|မေး|သိ))(?<DesidChin>ခြင်)(?=တယ်|သည်|ပါတယ်|ပါသည်|ဘူး)|(?<=(?:စက်|ဆပ်ပြာ|စနစ်|နည်းပညာ|သတင်း|ခြင်း|ချင်း|မှု)က\s+)(?<CausPat>[\u1000-\u103F]{2,18}?(?:တွေ|များ)က)(?=\s+မ?[\u1000-\u103F]{2,18}?စေ(?:ပါ)?(?:တယ်|သည်|ပြီး)))",
            RegexOptions.Compiled);

        private static readonly Regex GluedPhraseBoundarySplitPattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])(?<Left>တွေ)(?<Right>အများကြီး)|(?<Left>လို့)(?<Right>တောင်းဆို|ပြောတာက)|(?<=[\u1000-\u103F]{2})(?<Left>တွေနဲ့|တွေရဲ့)(?<Right>[\u1000-\u103F]{6,}တွေ)|(?<=[\u1000-\u103F]{3})(?<Left>နဲ့)(?<Right>ပရောဂျက်တွေ)|(?<Left>လုံခြုံစွာ)(?<Right>သိမ်းဆည်း)|(?<=[\u1000-\u103F]{3})(?<Left>က)(?<Right>သန့်ရှင်းသပ်ရပ်|အရေးပါပါ|စိတ်အပန်းပြေ))",
            RegexOptions.Compiled);

        private static readonly Regex DanglingConjunctionBeforeToPattern = new Regex(
            @"(?<Target>(?:နှင့်|နှင့်)[ \t]+တို့)(?=တွင်|၌|ကို|အား|သည်|မှ)",
            RegexOptions.Compiled);

        private static readonly Regex SpuriousTweAndPhinMorphoPattern = new Regex(
            @"(?:(?<=(?:ဆေးရုံ|စက်ရုပ်|ရည်မှန်းချက်|အစားအသောက်|ကျောင်းသား|ပစ္စည်း|အမှား|ဘုရားပွဲ|အခက်အခဲ))(?<SpurTwe>တွေ့)(?=မှာ|ဟာ|က|ကနေ|\s+အများကြီး)|(?<=(?:ခြင်း|ချင်း))(?<MisPhin>ဖြင်|ဖျင်)(?=\s)|(?<=ခြင်း)(?<MisIiPoss>ဤ)(?=\s+အခြေခံ))",
            RegexOptions.Compiled);

        private static readonly Regex V65CaseAndMorphoPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])ဘာ(?<BaMa>မ)(?=မဟုတ်)|(?:သ[ \t]*လို|ပါ[ \t]*ဝင်[ \t]*ဖို့)[ \t]+(?<SpacedBae>ဘဲ)(?=[\s၊။]|$)|(?:ရဟန်း|ပုဂ္ဂိုလ်|သူ)တို့(?<ToThint>သည့်|သည့်)(?=မြတ်စွာဘုရားအား)|(?:စိုက်ပျိုးရေး|ခရီးသွားလုပ်ငန်း|ပညာရေး|ကျန်းမာရေး)(?<BenefKoHa>ကို)(?=\s+[^\u104A\u104B]{2,22}?အတွက်\s+အရေး(?:ပါ|ကြီး|ကြီ))|လုပ်တာ(?<TaKoKa>ကို)(?=\s+ကျန်းမာရေးအတွက်\s+ကောင်း)|[\u1000-\u103F]{2}ခြင်း(?<GerundKoKa>ကို)(?=\s+(?:[\u1000-\u103F]+ကို\s+)?[\u1000-\u103F]{2,14}?စေ(?:တယ်|သည်|ပါ))|(?:နည်းပညာ|သတင်း|စနစ်)(?:တွေ|များ)(?<DoubleKoKa>ကို)(?=\s+[\u1000-\u103F]{2,18}?ကို\s+[\u1000-\u103F]{2,20}?(?:တိုးမြှင့်ပေး|ဖြစ်စေ))|(?:အာမခံ|ပစ္စည်းမျိုးစုံ)(?<ChooseKaKo>က)(?=\s+ရွေးချယ်နိုင်)|(?:ဆိုးကျိုး|အန္တရာယ်)(?:တွေ|များ)(?<NoticeKaKo>က)(?=\s+သတိပြုသင့်)|ရေဒီယို(?<RadioKaKo>က)(?=\s+နားထောင်)|နွမ်းပါးသူ(?:တွေ|များ)(?<HelpKaKo>က)(?=\s+ကူညီပေး)|ရှိသလို\s+တာဝန်(?:တွေ|များ)(?<PaeLae>ပဲ)(?=ရှိ)|[\u1000-\u103F]{2}(?:တွေ|များ)(?<AffLae1>လဲ)(?=ရှိပါ)|အတွင်းမှာ(?<AffLae2>လဲ)(?=\s)|(?:စေတီတော်|ဘုရား|ကျောင်း)တွေ(?<CopTweKoKa>ကို)(?=\s+[\u1000-\u103F]+ရဲ့\s+ယဉ်ကျေးမှု)|အနုပညာတို့(?<CopToKoHa>ကို)(?=\s+[\u1000-\u103F]+ရဲ့\s+ဥပမာ)|(?:သတ္တုဓာတ်|အာဟာရဓာတ်)တွေ(?<VitMhaKa>မှာ)(?=\s+ခန္ဓာကိုယ်အတွက်\s+မရှိမဖြစ်လိုအပ်)|ဖွံ့ဖြိုးတိုးတက်ရေး(?<DevKoMha>ကို)(?=\s+အရေးပါပါ))",
            RegexOptions.Compiled);

        private static readonly Regex IntraSentenceRegisterClashV63Pattern = new Regex(
            @"(?:(?:စာမေးပွဲခန်း|အစည်းအဝေးခန်း|တရားရုံးတော်|စာကြည့်တိုက်)(?:တွင်|၌)\s+(?:[^\u104A\u104B\r\n]{0,20}?\s+)?(?<AeSay>အေးဆေး)(?=\s+နေရမည်)|(?:^|[\u104A\u104B]\s*)[^\u104A\u104B\r\n]{0,28}?မှာ\s+[^\u104A\u104B\r\n]{2,36}?(?<PayEi>ပေး၏)(?=\s*(?:။|လို့))|(?:ပါမောက္ခချုပ်ကြီး|ပါမောက္ခကြီး|ဝန်ကြီးချုပ်|သမ္မတကြီး|ပြီးပါသည်၊)[^\u104A\u104B\r\n]{2,48}?(?<CasualNegBya>(?<NegStem>နားမလည်|မသိ)ဘူး[ \t]*ဗျာ?)(?=\s*။|$)|(?:ခဲ့ပါသည်|ပြီးပါသည်|ထားပါသည်)၊\s*(?:ဒါပေမဲ့|ဒါကြောင့်|ထို့ကြောင့်|ပြီးတော့|တပည့်တော်တို့လည်း)?\s*[^\u104A\u104B\r\n]{2,36}?(?<Clause2Colloq>ဈေးကြီးတယ်|နာယူမှတ်သားခဲ့ကြတယ်)(?=\s*။|$)|လွှတ်တော်အစည်းအဝေးတွင်\s+(?<ParlDi>ဒီ)(?=အကြောင်း)|တရားရုံးက\s+(?<CourtDiLu>ဒီလူ)(?=ကို)|သုတေသနအဖွဲ့က\s+(?<ResHoTaNay>ဟိုတစ်နေ့က)|နိုင်ငံတော်သမ္မတက\s+[^\u104A\u104B\r\n]{2,36}?(?<PresPyaw>ပြောခဲ့ပါတယ်)(?=\s*။)|အစိုးရ၏\s+[^\u104A\u104B\r\n]{2,28}?\s+(?<GovKhuMha>ခုမှ)(?=\s+))",
            RegexOptions.Compiled);

        private static void ScanStructuralSyllableAndBoundaryRepairs(
            string text,
            ulong mmMask,
            ProofingStyleMode styleMode,
            MyanmarTrie? trie,
            List<ProofingIssue> issues)
        {
            // 1. Space-detached intra-syllable killed coda ('လာမ ည့် -> လာမည့်', 'ပီကု တ် -> ပီကုတ်', 'ကို ယ် -> ကိုယ်', 'ပို င်း -> ပိုင်း') & broken virama stack ('စစ်တက္ ကသိုလ် -> စစ်တက္ကသိုလ်')
            if (text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0)
            {
                if (text.IndexOf('\u1039') >= 0)
                {
                    foreach (Match mv in BrokenViramaStackAcrossSpacePattern.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, mv.Index, mv.Length))
                            continue;

                        string leftVal = mv.Groups["Left"].Value;
                        string rightVal = mv.Groups["Right"].Value;

                        if (trie == null)
                        {
                            string joinedFallback = UnicodeNormalizer.NormalizeCanonicalOrder(leftVal + rightVal);
                            AddGrammarOrSpellingIssue(
                                issues, mv.Index, mv.Length, mv.Value, joinedFallback,
                                ProofingIssueType.SpellingError,
                                $"ပါဋ္ဌ်ဆင့်သင်္ကေတ ('္') နောက်၌ နေရာလွတ် (Space) မှားခြားမိနေပါသည် — '{mv.Value}' အစား '{joinedFallback}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                                "ပါဋ္ဌ်ဆင့်စာလုံး တစ်ဆက်တည်းရေးရန်", "N", true);
                            continue;
                        }

                        // 1a-i. Check if Left + [1..4 syllables of Right] forms a verified lexicon pat-sint word (e.g. 'စစ်တက္ ကသိုလ် -> စစ်တက္ကသိုလ်', 'ဝတ္ ထု -> ဝတ္ထု')
                        var rightSyls = MyanmarSyllableBreaker.BreakIntoSyllables(rightVal);
                        int matchedRightChars = 0;
                        string? validJoinedStack = null;
                        int accumR = 0;
                        for (int rCnt = 1; rCnt <= Math.Min(4, rightSyls.Count); rCnt++)
                        {
                            accumR += rightSyls[rCnt - 1].Length;
                            if (accumR <= rightVal.Length)
                            {
                                string tryStack = UnicodeNormalizer.NormalizeCanonicalOrder(leftVal + rightVal.Substring(0, accumR));
                                if (trie.ContainsExactWord(tryStack))
                                {
                                    validJoinedStack = tryStack;
                                    matchedRightChars = accumR;
                                }
                            }
                        }

                        if (validJoinedStack != null && matchedRightChars > 0)
                        {
                            int stackSpanLen = (mv.Groups["Right"].Index - mv.Index) + matchedRightChars;
                            if (!OverlapsExistingIssue(issues, mv.Index, stackSpanLen))
                            {
                                string rawSlice = text.Substring(mv.Index, stackSpanLen);
                                AddGrammarOrSpellingIssue(
                                    issues, mv.Index, stackSpanLen, rawSlice, validJoinedStack,
                                    ProofingIssueType.SpellingError,
                                    $"ပါဋ္ဌ်ဆင့်သင်္ကေတ ('္') နောက်၌ နေရာလွတ် (Space) မှားခြားမိနေပါသည် — '{rawSlice}' အစား '{validJoinedStack}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                                    "ပါဋ္ဌ်ဆင့်စာလုံး တစ်ဆက်တည်းရေးရန်", "N", true);
                            }
                            continue;
                        }

                        // 1a-ii. Otherwise trailing '\u1039' before space is Zawgyi / Shift+F mistyped Asat '\u103A' (e.g. 'အပြိုင်အဆိုင္ -> အပြိုင်အဆိုင်', 'ကျန်းမာရေးအတြက္ -> အတွက်')
                        if (leftVal.Length >= 2 && leftVal[leftVal.Length - 1] == '\u1039')
                        {
                            string leftAsat = leftVal.Substring(0, leftVal.Length - 1) + "\u103A";
                            var leftSyls = MyanmarSyllableBreaker.BreakIntoSyllables(leftAsat);
                            int rawTailChars = 0;
                            for (int tCnt = 1; tCnt <= Math.Min(4, leftSyls.Count); tCnt++)
                            {
                                rawTailChars += leftSyls[leftSyls.Count - tCnt].Length;
                                if (rawTailChars <= leftVal.Length)
                                {
                                    int subStart = mv.Index + (leftVal.Length - rawTailChars);
                                    string rawWord = text.Substring(subStart, rawTailChars);
                                    string candAsat = UnicodeNormalizer.NormalizeCanonicalOrder(rawWord.Substring(0, rawWord.Length - 1) + "\u103A");
                                    string candZg = candAsat.IndexOf('\u103C') >= 0
                                        ? UnicodeNormalizer.NormalizeCanonicalOrder(candAsat.Replace('\u103C', '\u103D'))
                                        : candAsat;

                                    string? repairedWord = trie.ContainsExactWord(candAsat)
                                        ? candAsat
                                        : (candZg != candAsat && trie.ContainsExactWord(candZg) ? candZg : null);

                                    if (repairedWord != null && !OverlapsExistingIssue(issues, subStart, rawTailChars))
                                    {
                                        AddGrammarOrSpellingIssue(
                                            issues, subStart, rawTailChars, rawWord, repairedWord,
                                            ProofingIssueType.LookalikeCharacterError,
                                            $"စာလုံးအဆုံး၌ ပါဋ္ဌ်ဆင့်သင်္ကေတ ('္') မှားရိုက်မိနေပါသည် — အသတ် ('်') ဖြင့် '{repairedWord}' ဟု ရေးသားရမည်။",
                                            "ယူနီကုဒ် အသတ်စာလုံးပေါင်းအမှန်", "WORD", true);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (Match m in OrphanSpaceDetachedCodaPattern.Matches(text))
                {
                    string openSyl = m.Groups["OpenSyl"].Value;
                    string coda = m.Groups["Coda"].Value;
                    string joinedSyl = UnicodeNormalizer.NormalizeCanonicalOrder(openSyl + coda);
                    if (trie != null && !trie.IsValidSingleSyllable(joinedSyl) && !trie.ContainsExactWord(joinedSyl))
                        continue;

                    string prefix = m.Groups["Prefix"].Value;
                    string fullJoined = UnicodeNormalizer.NormalizeCanonicalOrder(prefix + openSyl + coda);
                    bool useFullPrefix = prefix.Length > 0 && trie != null && trie.ContainsExactWord(fullJoined);

                    int startIdx = useFullPrefix ? m.Index : m.Groups["OpenSyl"].Index;
                    int spanLen = (m.Index + m.Length) - startIdx;
                    if (OverlapsExistingIssue(issues, startIdx, spanLen))
                        continue;

                    string rawSpan = text.Substring(startIdx, spanLen);
                    string targetRep = useFullPrefix ? fullJoined : joinedSyl;
                    AddGrammarOrSpellingIssue(
                        issues, startIdx, spanLen, rawSpan, targetRep,
                        ProofingIssueType.SpellingError,
                        $"ဝဏ္ဏအတွင်း အသတ်ရှေ့၌ နေရာလွတ် (Space) မှားခြားမိနေပါသည် — '{rawSpan}' အစား '{targetRep}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                        "အသတ်နှင့် ရှေ့ဗျည်း တစ်ဆက်တည်းရေးရန်", "N", true);
                }
            }

            // 1a. Corrupted Zawgyi/WinInnwa ASCII 'A' for Burmese 'အ' ('Aား -> အား', 'A မျိုးသား -> အမျိုးသား')
            //     Must run BEFORE Layer-1 MyanmarSyllableValidator so 'Aား' is repaired to 'အား' rather than stripped as an orphan diacritic!
            if (text.IndexOf('A') >= 0)
            {
                foreach (Match m in CorruptedAsciiAPrefixPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    if (m.Groups["Target"].Success)
                    {
                        string raw = m.Groups["Target"].Value;
                        string repaired = UnicodeNormalizer.NormalizeCanonicalOrder("\u1021" + raw.Substring(1));
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, raw, repaired,
                            ProofingIssueType.SpellingError,
                            $"အင်္ဂလိပ်အက္ခရာ 'A' ရောနှောနေပါသည် — '{raw}' အစား မြန်မာအက္ခရာ 'အ' ဖြင့် '{repaired}' ဟု ရေးသားရမည်။",
                            "မြန်မာအက္ခရာ 'အ' အမှန်", "PART", true);
                    }
                    else if (m.Groups["Target2"].Success && trie != null)
                    {
                        Group stemGrp = m.Groups["Stem"];
                        string rawStem = stemGrp.Value;
                        if (!rawStem.StartsWith("\u1021", StringComparison.Ordinal))
                        {
                            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(rawStem);
                            string? bestPrefix = null;
                            int bestRawLen = 0;
                            int accumRaw = 0;
                            for (int sCnt = 1; sCnt <= Math.Min(4, syls.Count); sCnt++)
                            {
                                accumRaw += syls[sCnt - 1].Length;
                                if (accumRaw <= rawStem.Length)
                                {
                                    string subNorm = UnicodeNormalizer.NormalizeCanonicalOrder(rawStem.Substring(0, accumRaw));
                                    string tryCand = "\u1021" + subNorm;
                                    if (trie.TryGetExactEntries(tryCand, out var aEntries) &&
                                        aEntries != null && aEntries.Count > 0 && aEntries[0].IsOfficialThatpon)
                                    {
                                        bestPrefix = tryCand;
                                        bestRawLen = (stemGrp.Index - m.Index) + accumRaw;
                                    }
                                }
                            }
                            if (bestPrefix != null && bestRawLen > 0 && !OverlapsExistingIssue(issues, m.Index, bestRawLen))
                            {
                                string rawSlice = text.Substring(m.Index, bestRawLen);
                                AddGrammarOrSpellingIssue(
                                    issues, m.Index, bestRawLen, rawSlice, bestPrefix,
                                    ProofingIssueType.SpellingError,
                                    $"အင်္ဂလိပ်အက္ခရာ 'A' ရောနှောနေပါသည် — '{rawSlice}' အစား '{bestPrefix}' ဟု ရေးသားရမည်။",
                                    "သတ်ပုံကျမ်းဝင် 'အ~' ပေါင်းစပ်ပုဒ်", "N", true);
                            }
                        }
                    }
                }
            }

            // 1b. Layer-1 UTN #11 & Burmese Phonotactic Cross-Token Shattered Syllable Span Repairs
            MyanmarSyllableValidator.ScanMalformedSyllableSpans(text, trie, issues);

            // 2. Impossible double tone mark on the same syllable ('ကောင်း့ -> ကောင်း')
            if ((text.IndexOf("\u1038\u1037", StringComparison.Ordinal) >= 0) ||
                (text.IndexOf("\u1037\u1038", StringComparison.Ordinal) >= 0))
            {
                foreach (Match m in RedundantDoubleToneSyllablePattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string raw = m.Value;
                    string keepVisarga = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u1037", string.Empty));
                    string keepCreaky = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u1038", string.Empty));
                    string best = (trie != null && !trie.ContainsExactWord(keepVisarga) && trie.ContainsExactWord(keepCreaky))
                        ? keepCreaky
                        : keepVisarga;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, raw, best,
                        ProofingIssueType.SpellingError,
                        $"ဝဏ္ဏတစ်ခုတည်း၌ ဝစ္စပေါက် ('း') နှင့် အောက်မြစ် ('့') နှစ်ခုတွဲ မသုံးရပါ — '{raw}' အစား '{best}' ဟု ရေးသားရမည်။",
                        "ပိုနေသော အသံပြောင်းသင်္ကေတ ဖယ်ရှားရန်", "V", true);
                }
            }

            // 2b. Impossible syllable vowel/medial/missing-asat combinations ('အရေးကြိး -> ကြီး', 'ကျြန်တော် -> ကျွန်', 'သက်တမး -> တမ်း', 'နိုင်ငံတဝန်း -> တစ်ဝန်း')
            foreach (Match mImp in ImpossibleSyllableVowelMedialPattern.Matches(text))
            {
                if (OverlapsExistingIssue(issues, mImp.Index, mImp.Length))
                    continue;
                if (mImp.Groups["ShortIVisarga"].Success)
                {
                    string raw = mImp.Value;
                    string rep = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u102D\u1038", "\u102E\u1038"));
                    if (trie == null || trie.IsValidSingleSyllable(rep) || trie.ContainsExactWord(rep))
                    {
                        AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, raw, rep, ProofingIssueType.SpellingError,
                            $"ဝစ္စပေါက် ('း') နှင့်တွဲရာတွင် လုံးကြီးတင်ဆန်ခတ် ('ီး') ဖြင့် '{rep}' ဟု ရေးသားရမည်။", "စံဝဏ္ဏဖွဲ့စည်းပုံအမှန်", "ADJ", true);
                    }
                }
                else if (mImp.Groups["DoubleMedial"].Success)
                {
                    string raw = mImp.Value;
                    string srcMed = raw.IndexOf("\u103B\u103C", StringComparison.Ordinal) >= 0 ? "\u103B\u103C" : "\u103C\u103B";
                    string repYw = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace(srcMed, "\u103B\u103D"));
                    string repRw = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace(srcMed, "\u103C\u103D"));
                    string? chosen = (trie == null || trie.IsValidSingleSyllable(repYw) || trie.ContainsExactWord(repYw))
                        ? repYw
                        : ((trie.IsValidSingleSyllable(repRw) || trie.ContainsExactWord(repRw)) ? repRw : null);
                    if (chosen != null)
                    {
                        AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, raw, chosen, ProofingIssueType.SpellingError,
                            $"ဗျည်းတွဲ ယပင့်နှင့် ရရစ် ('ျြ') တွဲမသုံးရပါ — '{raw}' အစား '{chosen}' ဟု ရေးသားရမည်။", "ဗျည်းတွဲ စာလုံးပေါင်းအမှန်", "N", true);
                    }
                }
                else if (mImp.Groups["MissingAsatVisarga"].Success)
                {
                    string raw = mImp.Value;
                    string rep = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Substring(0, raw.Length - 1) + "\u103A\u1038");
                    if (trie == null || trie.IsValidSingleSyllable(rep) || trie.ContainsExactWord(rep))
                    {
                        AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, raw, rep, ProofingIssueType.SpellingError,
                            $"အသတ် ('်') ကျန်ခဲ့ပါသည် — '{raw}' အစား '{rep}' ဟု ရေးသားရမည်။", "အသတ်နှင့် ဝစ္စပေါက် အမှန်", "N", true);
                    }
                }
                else if (mImp.Groups["TaWun"].Success)
                {
                    AddGrammarOrSpellingIssue(issues, mImp.Index, mImp.Length, mImp.Value, "တစ်ဝန်း", ProofingIssueType.NumeralClassifierError,
                        "သတ်ပုံကျမ်းစံနှုန်းအရ 'တစ်ဝန်း' ဟု စသတ်ဖြင့် ရေးသားရမည်။", "တစ်ဝန်း (နိုင်ငံ~ရှိ)", "N", true);
                }
            }

            // 3. Corrupted Zawgyi '-ော့င်း' conversion artifact ('ပြော့င်း -> ပြောင်း')
            if (text.IndexOf("\u1037", StringComparison.Ordinal) >= 0 && text.IndexOf("\u1038", StringComparison.Ordinal) >= 0)
            {
                foreach (Match m in CorruptedZawgyiAwNasalCodaPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string raw = m.Value;
                    string repaired = UnicodeNormalizer.NormalizeCanonicalOrder(raw.Replace("\u1037", string.Empty));
                    if (trie != null && (trie.ContainsExactWord(raw) || (!trie.IsValidSingleSyllable(repaired) && !trie.ContainsExactWord(repaired))))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, raw, repaired,
                        ProofingIssueType.SpellingError,
                        $"စာလုံးဖွဲ့စည်းပုံ မှားယွင်းနေပါသည် — '{raw}' အစား အောက်မြစ်မပါဘဲ '{repaired}' ဟု ရေးသားရမည်။",
                        "စံယူနီကုဒ် ဝဏ္ဏအမှန်", "V", true);
                }
            }

            // 5. Affirmative emphatic 'ဘဲ' -> 'ပဲ' (when not negated by 'မ-')
            if (ContainsMm(text, mmMask, "ဘဲ"))
            {
                foreach (Match m in AffirmativeEmphaticBaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"]
                            : m.Groups["Target2"].Success ? m.Groups["Target2"]
                            : m.Groups["Target3"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပဲ",
                        ProofingIssueType.SpellingError,
                        "အဟုတ်အမှန် အလေးအနက်ပြပစ္စည်းကို ပစောက်ဖြင့် 'ပဲ' ဟု ရေးသားရမည် ('ဘဲ' ကို 'မ...ဘဲ' အငြင်းပစ္စည်း၌သာ သုံးသည်)။",
                        "အလေးအနက်ပြပစ္စည်း ('~ပါပဲ / ~သလိုပဲ')", "PART", true);
                }
            }

            // 6. Spaced/unspaced verbal particle & modal typos ('သွား ခဲ တယ် -> ခဲ့', 'ပေး မဲ လို့ -> မယ်', '~ဖို -> ~ဖို့', '~သင်တယ် -> ~သင့်', 'ကြိုကတယ် -> ကြိုက်')
            foreach (Match m in SpacedVerbalParticleTypoPattern.Matches(text))
            {
                if (m.Groups["Khae"].Success && !OverlapsExistingIssue(issues, m.Groups["Khae"].Index, m.Groups["Khae"].Length))
                {
                    Group g = m.Groups["Khae"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ခဲ့", ProofingIssueType.SpellingError,
                        "အတိတ်ပြ ကြိယာထောက်ပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ခဲ့' ဟု ရေးသားရမည်။", "အတိတ်ပြပစ္စည်း ('~ခဲ့')", "PART", true);
                }
                else if (m.Groups["Mae"].Success && !OverlapsExistingIssue(issues, m.Groups["Mae"].Index, m.Groups["Mae"].Length))
                {
                    Group g = m.Groups["Mae"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မယ်", ProofingIssueType.SpellingError,
                        "အနာဂတ်ပြ ကြိယာပိတ်ပစ္စည်းကို ယပက်လက် အသတ်ဖြင့် 'မယ်' ဟု ရေးသားရမည်။", "အနာဂတ်ပြပစ္စည်း ('~မယ်')", "PART", false);
                }
                else if (m.Groups["Pho"].Success && !OverlapsExistingIssue(issues, m.Groups["Pho"].Index, m.Groups["Pho"].Length))
                {
                    Group g = m.Groups["Pho"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဖို့", ProofingIssueType.SpellingError,
                        "ရည်ရွယ်ချက်ပြပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ဖို့' ဟု ရေးသားရမည်။", "ရည်ရွယ်ချက်ပြပစ္စည်း ('~ဖို့')", "PART", true);
                }
                else if (m.Groups["Thing"].Success && !OverlapsExistingIssue(issues, m.Groups["Thing"].Index, m.Groups["Thing"].Length))
                {
                    Group g = m.Groups["Thing"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သင့်", ProofingIssueType.SpellingError,
                        "ကြိယာထောက်ပစ္စည်း '~သင့်' တွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'သင့်' ဟု ရေးသားရမည်။", "သင့်လျော်ကြောင်းပြပစ္စည်း ('~သင့်')", "PART", true);
                }
                else if (m.Groups["KyoKa"].Success && !OverlapsExistingIssue(issues, m.Groups["KyoKa"].Index, m.Groups["KyoKa"].Length))
                {
                    Group g = m.Groups["KyoKa"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကြိုက်", ProofingIssueType.SpellingError,
                        "အသတ် ('်') ကျန်ခဲ့ပါသည် — 'ကြိုက' အစား 'ကြိုက်' ဟု ရေးသားရမည်။", "နှစ်သက်သည် ('ကြိုက်')", "V", true);
                }
                else if (m.Groups["IrrealisMar"].Success && !OverlapsExistingIssue(issues, m.Groups["IrrealisMar"].Index, m.Groups["IrrealisMar"].Length))
                {
                    Group g = m.Groups["IrrealisMar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မှာ", ProofingIssueType.SpellingError,
                        "ဟထိုး ('ှ') ကျန်ခဲ့ပါသည် — 'မာ' အစား 'မှာ' ဟု ရေးသားရမည်။", "ဖြစ်လတ္တံ့ပြပစ္စည်း ('~မှာပဲ / ~မှာလား')", "PART", false);
                }
                else if (m.Groups["PaTaYa"].Success && !OverlapsExistingIssue(issues, m.Groups["PaTaYa"].Index, m.Groups["PaTaYa"].Length))
                {
                    Group g = m.Groups["PaTaYa"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "ပါသည်" : "ပါတယ်";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"အသတ် ('်') ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည်။", "ကြိယာအဆုံးသတ်ပစ္စည်း", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
            }

            // 6b. Lexicon-backed VerbStem + 'ဖို' -> 'ဖို့' before purposive/modal predicates ('ကာကွယ်ဖိုက -> ကာကွယ်ဖို့က', 'ကျန်းမာရေးကောင်းဖို -> ဖို့')
            if (trie != null && ContainsMm(text, mmMask, "ဖို") && HasBarePhoWithoutCreaky(text))
            {
                foreach (Match mPho in VerbStemMissingCreakyPhoPattern.Matches(text))
                {
                    Group gPho = mPho.Groups["Pho2"];
                    if (OverlapsExistingIssue(issues, gPho.Index, gPho.Length))
                        continue;
                    string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(mPho.Groups["Stem"].Value);
                    if (HasTrailingLexiconVerbOrAdj(trie, normStem, allowAdj: true))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, gPho.Index, gPho.Length, gPho.Value, "ဖို့",
                            ProofingIssueType.SpellingError,
                            "ရည်ရွယ်ချက်ပြပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ဖို' အစား 'ဖို့' ဟု ရေးသားရမည်။",
                            "ရည်ရွယ်ချက်ပြပစ္စည်း ('~ဖို့')", "PART", true);
                    }
                }
            }

            // 6b-ii. Lexicon-backed VerbStem + 'ချင်း' -> 'ခြင်း' before nominal postpositions ('ရှောင်ရှားချင်းက -> ခြင်း', 'စောင့်ရှောက်ချင်းက -> ခြင်း')
            if (trie != null && ContainsMm(text, mmMask, "ချင်း"))
            {
                foreach (Match mGer in LexiconVerbGerundChinPattern.Matches(text))
                {
                    Group gGer = mGer.Groups["GerundChin2"];
                    if (OverlapsExistingIssue(issues, gGer.Index, gGer.Length))
                        continue;
                    string normStem = UnicodeNormalizer.NormalizeCanonicalOrder(mGer.Groups["Stem"].Value);
                    if (!IsReciprocalOrImmediateChinStem(normStem) && HasTrailingLexiconVerbOrAdj(trie, normStem, allowAdj: false))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, gGer.Index, gGer.Length, gGer.Value, "ခြင်း",
                            ProofingIssueType.SpellingError,
                            "ကြိယာမှ နာမ်ပြောင်းရာတွင် ရရစ် ('ြ') ဖြင့် 'ခြင်း' ဟု ရေးသားရမည် ('ချင်း' သည် အချင်းချင်း/တစ်ဦးချင်း၌သာ သုံးသည်)။",
                            "ကြိယာနာမ်ပြုပစ္စည်း ('~ခြင်း')", "PART", true);
                    }
                }
            }

            // 6c. Extended particle, nominalizer, adverbializer & case-frame confusions ('ဘို့ -> ဖို့', '~မှုနှဲ့ -> နဲ့', '~ချင်း -> ~ခြင်း', '~သော -> ~စွာ', '~ကို နေထိုင် -> တွင်/မှာ', '~ကာ ရှိသည် -> လျက်')
            foreach (Match mExt in ExtendedParticleAndMorphoSyntaxPattern.Matches(text))
            {
                if (mExt.Groups["Bho"].Success && !OverlapsExistingIssue(issues, mExt.Groups["Bho"].Index, mExt.Groups["Bho"].Length))
                {
                    Group g = mExt.Groups["Bho"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဖို့", ProofingIssueType.SpellingError,
                        "ရည်ရွယ်ချက်ပြပစ္စည်းကို ဖဦးထုပ်ဖြင့် 'ဖို့' ဟု ရေးသားရမည် ('ဘို့' သည် 'နွားဘို့' နာမ်၌သာ သုံးသည်)။",
                        "ရည်ရွယ်ချက်ပြပစ္စည်း ('~ဖို့')", "PART", true);
                }
                else if (mExt.Groups["Nhae"].Success && !OverlapsExistingIssue(issues, mExt.Groups["Nhae"].Index, mExt.Groups["Nhae"].Length))
                {
                    Group g = mExt.Groups["Nhae"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "နှင့်" : "နဲ့";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"အတူပြဝိဘတ်တွင် '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည် ('နှဲ့' သည် လှုပ်နှဲ့သည့် ကြိယာဖြစ်သည်)။",
                        "အတူပြ နာမ်ဝိဘတ်", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
                else if (mExt.Groups["FinalTae"].Success && !OverlapsExistingIssue(issues, mExt.Groups["FinalTae"].Index, mExt.Groups["FinalTae"].Length))
                {
                    Group g = mExt.Groups["FinalTae"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "သည်" : "တယ်";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"ဝါကျအဆုံး ကြိယာဝိဘတ်တွင် '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည်။",
                        "ကြိယာအဆုံးသတ်ပစ္စည်း", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
                else if (mExt.Groups["GerundChin"].Success && !OverlapsExistingIssue(issues, mExt.Groups["GerundChin"].Index, mExt.Groups["GerundChin"].Length))
                {
                    Group g = mExt.Groups["GerundChin"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ခြင်း", ProofingIssueType.SpellingError,
                        "ကြိယာမှ နာမ်ပြောင်းရာတွင် ရရစ် ('ြ') ဖြင့် 'ခြင်း' ဟု ရေးသားရမည်။",
                        "ကြိယာနာမ်ပြုပစ္စည်း ('~ခြင်း')", "PART", true);
                }
                else if (mExt.Groups["AttrThaw"].Success && !OverlapsExistingIssue(issues, mExt.Groups["AttrThaw"].Index, mExt.Groups["AttrThaw"].Length))
                {
                    Group g = mExt.Groups["AttrThaw"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "စွာ", ProofingIssueType.OfficialStyleGrammarError,
                        "နောက်မှ ကြိယာကို အထူးပြုရာတွင် နာမဝိသေသနပြ 'သော' အစား ကြိယာဝိသေသနပြ 'စွာ' ကို သုံးနှုန်းရမည်။",
                        "ကြိယာဝိသေသနပြုပစ္စည်း ('~စွာ')", "PART", true,
                        "နောက်၌ နာမ်ပုဒ်မလိုက်ဘဲ ကြိယာတိုက်ရိုက်လိုက်နေသဖြင့် နာမ်အထူးပြု 'သော' အစား ကြိယာဝိသေသနပြုပစ္စည်း 'စွာ' ဖြင့် တွဲစပ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိသေသနပြုပစ္စည်း 'စွာ')");
                }
                else if (mExt.Groups["LocKo"].Success && !OverlapsExistingIssue(issues, mExt.Groups["LocKo"].Index, mExt.Groups["LocKo"].Length))
                {
                    Group g = mExt.Groups["LocKo"];
                    bool formalSentence = styleMode == ProofingStyleMode.FormalOfficial || mExt.Groups["LocEnd"].Value == "သည်";
                    string primary = formalSentence ? "တွင်" : "မှာ";
                    string secondary = formalSentence ? "မှာ" : "တွင်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"'နေထိုင်' ကြိယာရှေ့ နေရာပြဝိဘတ်တွင် 'ကို' အစား '{primary}' ကို သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = "'နေထိုင်သည်/နေထိုင်တယ်' သည် တည်နေရာပြ ကြိယာဖြစ်သဖြင့် ကံပုဒ်ပြ/ရှေးရှုရာပြ 'ကို' မသုံးရဘဲ နေရာပြဝိဘတ် 'တွင်/မှာ' ကို သုံးရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နေရာပြ နာမ်ဝိဘတ် 'တွင်/၌/မှာ')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate { Replacement = primary, MeaningCue = "နေရာပြ နာမ်ဝိဘတ်", PosTag = "PART", IsOfficialThatpon = formalSentence, Score = 100.0 },
                            new SuggestionCandidate { Replacement = secondary, MeaningCue = "နေရာပြ ဝိဘတ်ပုံစံကွဲ", PosTag = "PART", IsOfficialThatpon = !formalSentence, Score = 95.0 }
                        }
                    });
                }
                else if (mExt.Groups["ContKar"].Success && !OverlapsExistingIssue(issues, mExt.Groups["ContKar"].Index, mExt.Groups["ContKar"].Length))
                {
                    Group g = mExt.Groups["ContKar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လျက်", ProofingIssueType.OfficialStyleGrammarError,
                        "'~ရှိသည်' ရှေ့ ဆောင်ရွက်ဆဲပြရာတွင် 'ကာ' အစား 'လျက်' ('~လျက် ရှိသည်') ကို သုံးနှုန်းရမည်။",
                        "ဖြစ်ပေါ်ဆဲပြပစ္စည်း ('~လျက် ရှိသည်')", "PART", true,
                        "အကူကြိယာ 'ရှိသည်' နှင့်တွဲ၍ ဖြစ်ပေါ်ဆဲအခြေအနေကို ဖော်ပြရာတွင် 'လျက် ရှိသည်' ဟူသော စံတွဲထုံးကို သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိသေသနပြုပစ္စည်း 'လျက်')");
                }
            }

            // 6c-2. Nightmare particle, case-collision & inverted irrealis repairs
            foreach (Match mNm in NightmareParticleAndCaseCollisionPattern.Matches(text))
            {
                if (mNm.Groups["KyaPlural"].Success && !OverlapsExistingIssue(issues, mNm.Groups["KyaPlural"].Index, mNm.Groups["KyaPlural"].Length))
                {
                    Group g = mNm.Groups["KyaPlural"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကြ", ProofingIssueType.SpellingError,
                        "အများပြ ကြိယာထောက်ပစ္စည်းတွင် ရေးချ ('ာ') မပါရပါ — 'ကြာ' အစား 'ကြ' ဟု ရေးသားရမည်။",
                        "အများပြ ကြိယာထောက်ပစ္စည်း ('~ကြ')", "PART", true);
                }
                else if (mNm.Groups["PatPolite"].Success && !OverlapsExistingIssue(issues, mNm.Groups["PatPolite"].Index, mNm.Groups["PatPolite"].Length))
                {
                    Group g = mNm.Groups["PatPolite"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပါ", ProofingIssueType.SpellingError,
                        "ယဉ်ကျေးမှုပြ ကြိယာထောက်ပစ္စည်းကို 'ပါ' ('~ပါတယ် / ~ပါသည်') ဟု ရေးသားရမည်။",
                        "ယဉ်ကျေးမှုပြ ကြိယာထောက်ပစ္စည်း ('~ပါ')", "PART", true);
                }
                else if (mNm.Groups["YaPot"].Success && !OverlapsExistingIssue(issues, mNm.Groups["YaPot"].Index, mNm.Groups["YaPot"].Length))
                {
                    Group g = mNm.Groups["YaPot"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ရ", ProofingIssueType.SpellingError,
                        "'~လို့ရတယ်' ပုံစံတွင် ရေးချမပါဘဲ 'ရ' ဟု ရေးသားရမည်။",
                        "ဖြစ်နိုင်စွမ်းပြ ကြိယာထောက်ပစ္စည်း ('~ရ')", "PART", true);
                }
                else if (mNm.Groups["Ahtwet"].Success && !OverlapsExistingIssue(issues, mNm.Groups["Ahtwet"].Index, mNm.Groups["Ahtwet"].Length))
                {
                    Group g = mNm.Groups["Ahtwet"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "အတွက်", ProofingIssueType.SpellingError,
                        "ရည်စူးချက်ပြ နာမ်ဝိဘတ်ကို တဝမ်းပူ ဝဆွဲဖြင့် 'အတွက်' ဟု ရေးသားရမည် ('အထွက်' သည် ထွက်ရှိမှု အနက်ဖြစ်သည်)။",
                        "ရည်စူးချက်ပြ နာမ်ဝိဘတ် ('~အတွက်')", "PART", true);
                }
                else if (mNm.Groups["UnspacedYae"].Success && !OverlapsExistingIssue(issues, mNm.Groups["UnspacedYae"].Index, mNm.Groups["UnspacedYae"].Length))
                {
                    Group g = mNm.Groups["UnspacedYae"];
                    string rep = styleMode == ProofingStyleMode.FormalOfficial ? "၏" : "ရဲ့";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, rep, ProofingIssueType.SpellingError,
                        $"ပိုင်ဆိုင်မှုပြဝိဘတ်တွင် အောက်မြစ်ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{rep}' ဟု ရေးသားရမည်။",
                        "ပိုင်ဆိုင်မှုပြ နာမ်ဝိဘတ်", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
                else if (mNm.Groups["QuotLo"].Success && !OverlapsExistingIssue(issues, mNm.Groups["QuotLo"].Index, mNm.Groups["QuotLo"].Length))
                {
                    Group g = mNm.Groups["QuotLo"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လို့", ProofingIssueType.SpellingError,
                        "ပြောဆို/ထင်မြင်ချက်ပြ နောက်ဆက်ပစ္စည်းတွင် အောက်မြစ်ဖြင့် 'လို့' ဟု ရေးသားရမည် ('လို' သည် နှိုင်းယှဉ်ပြပစ္စည်း ဖြစ်သည်)။",
                        "ဆိုလိုချက်ပြပစ္စည်း ('~လို့')", "PART", false);
                }
                else if ((mNm.Groups["KaungVis1"].Success || mNm.Groups["KaungVis2"].Success))
                {
                    Group g = mNm.Groups["KaungVis1"].Success ? mNm.Groups["KaungVis1"] : mNm.Groups["KaungVis2"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကောင်း", ProofingIssueType.SpellingError,
                            "ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — 'ကောင်' အစား 'ကောင်း' ဟု ရေးသားရမည်။",
                            "ကောင်းမွန်ခြင်းပြ နာမဝိသေသန/ကြိယာ ('ကောင်း')", "ADJ", true);
                    }
                }
                else if (mNm.Groups["AungSpur"].Success && !OverlapsExistingIssue(issues, mNm.Groups["AungSpur"].Index, mNm.Groups["AungSpur"].Length))
                {
                    Group g = mNm.Groups["AungSpur"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "အောင်", ProofingIssueType.SpellingError,
                        "ရည်ရွယ်ချက်/အကျိုးဆက်ပြပစ္စည်းတွင် ဝစ္စပေါက်မပါဘဲ 'အောင်' ဟု ရေးသားရမည်။",
                        "အကျိုးဆက်ပြ ကြိယာပစ္စည်း ('~အောင်')", "PART", true);
                }
                else if (mNm.Groups["TwePlural"].Success && !OverlapsExistingIssue(issues, mNm.Groups["TwePlural"].Index, mNm.Groups["TwePlural"].Length))
                {
                    Group g = mNm.Groups["TwePlural"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တွေ", ProofingIssueType.SpellingError,
                        "အများပြပစ္စည်းတွင် ဝစ္စပေါက်မပါရပါ — 'တွေး' (စဉ်းစားတွေးတောသည်) အစား 'တွေ' ဟု ရေးသားရမည်။",
                        "အများပြ နာမ်ပစ္စည်း ('~တွေ')", "PART", false);
                }
                else if (mNm.Groups["SubjKo"].Success && !OverlapsExistingIssue(issues, mNm.Groups["SubjKo"].Index, mNm.Groups["SubjKo"].Length))
                {
                    Group g = mNm.Groups["SubjKo"];
                    string subjRoot = g.Value.Substring(0, g.Value.Length - 3);
                    string correctSubj = subjRoot + "က";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, correctSubj, ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျအစ ကတ္တားပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား ကတ္တားဝိဘတ် 'က' ဖြင့် '{correctSubj}' ဟု သုံးနှုန်းရမည်။",
                        "ကတ္တားဝိဘတ် ('~က')", "PART", true,
                        "နောက်၌ ကံပုဒ် ('~ကို') နှင့် ကံလိုကြိယာ ပါရှိနေသဖြင့် ပြုလုပ်သူ/ခံစားသူ ကတ္တားနာမ်စားနောက်တွင် 'က' ဝိဘတ်ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ကံဝိဘတ် ခွဲခြားပုံ)");
                }
                else if (mNm.Groups["CopKo"].Success && !OverlapsExistingIssue(issues, mNm.Groups["CopKo"].Index, mNm.Groups["CopKo"].Length))
                {
                    Group g = mNm.Groups["CopKo"];
                    string copVerb = mNm.Groups["CopVerb"].Value;
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, copVerb, ProofingIssueType.OfficialStyleGrammarError,
                        $"'ဖြစ်သည်/ဖြစ်တယ်' ကြိယာရှေ့ အမည်နာမ်ပုဒ်နောက်တွင် ကံဝိဘတ် 'ကို' မထည့်ရပါ — '{copVerb}' ဟုသာ ရေးသားရမည်။",
                        "ဖြစ်ခြင်းပြကြိယာ ('ဖြစ်သည်')", "V", true,
                        "'ဖြစ်သည်' သည် ကံမလိုသော ဖြစ်ခြင်းပြကြိယာ (Copula) ဖြစ်သဖြင့် ရှေ့ရှိ နာမ်ပုဒ်နောက်၌ ကံဝိဘတ် 'ကို' မလိုက်ရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဖြစ်ခြင်းပြကြိယာ နှင့် ဝိဘတ်စည်းမျဉ်း)");
                }
                else if (mNm.Groups["IrrInv"].Success && !OverlapsExistingIssue(issues, mNm.Groups["IrrInv"].Index, mNm.Groups["IrrInv"].Length))
                {
                    Group g = mNm.Groups["IrrInv"];
                    string fixedIrr = mNm.Groups["IrrVerb"].Value + "မှာ" + mNm.Groups["IrrNeg"].Value;
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, fixedIrr, ProofingIssueType.OfficialStyleGrammarError,
                        $"အနာဂတ်အငြင်းပုဒ် အစီအစဉ် လွဲမှားနေပါသည် — '{g.Value}' အစား '{fixedIrr}' ဟု ရေးသားရမည်။",
                        "အနာဂတ်အငြင်းပုဒ် အစီအစဉ်မှန် ('~မှာမဟုတ်ဘူး')", "SYNTAX", false,
                        "ကြိယာနာမ်ပြုပစ္စည်း 'မှာ' သည် ပင်မကြိယာ၏ နောက်၌ ကပ်လျက် တည်ရှိရပြီး ထို့နောက်မှ 'မဟုတ်ဘူး/မဟုတ်ပါ' လိုက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဝါကျဖွဲ့ထုံးနှင့် ပစ္စည်းအစီအစဉ်)");
                }
            }

            // 6d. SOV Clause-Final Verb Restorer (repairs English-calqued SVO clauses where finite main verb precedes postpositional NP arguments)
            if (text.IndexOf(' ') >= 0 &&
                (ContainsMm(text, mmMask, "ခဲ့") || ContainsMm(text, mmMask, "တယ်")))
            {
                foreach (Match mSov in SovVerbInversionPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, mSov.Index, mSov.Length))
                        continue;
                    string verbPhrase = mSov.Groups["Verb"].Value.Trim();
                    string argsPhrase = mSov.Groups["Args"].Value.Trim();
                    string restoredSov = $"{argsPhrase} {verbPhrase}";
                    AddGrammarOrSpellingIssue(
                        issues, mSov.Index, mSov.Length, mSov.Value, restoredSov,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"မြန်မာဝါကျဖွဲ့ထုံး (SOV) အရ ပင်မကြိယာ '{verbPhrase}' ကို ဝါကျအဆုံး၌ ထားရှိရမည် — '{restoredSov}' ဟု ပြန်လည်စီစဉ်သင့်ပါသည်။",
                        "မြန်မာစံဝါကျအစီအစဉ် (ကတ္တား + ကံ/နေရာပြပုဒ် + ကြိယာ)", "SYNTAX", true,
                        $"မြန်မာဘာသာစကား၏ စံဝါကျတည်ဆောက်ပုံ (Subject-Object-Verb) တွင် ကံပုဒ်နှင့် ဝိဘတ်တွဲပုဒ်များ ('{argsPhrase}') သည် အဆုံးသတ်ပင်မကြိယာ ('{verbPhrase}') ၏ ရှေ့တွင် တည်ရှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဝါကျဖွဲ့ထုံးနှင့် ပုဒ်အစီအစဉ်)");
                }
            }

            // 6e. Glued Phrase-Boundary Splits ('တွေအများကြီး -> တွေ အများကြီး', 'လို့တောင်းဆို -> လို့ တောင်းဆို', 'တွေနဲ့စားသောက်ဆိုင်တွေ -> တွေနဲ့ စားသောက်ဆိုင်တွေ', 'လုံခြုံစွာသိမ်းဆည်း -> လုံခြုံစွာ သိမ်းဆည်း')
            if (ContainsMm(text, mmMask, "တွေ") ||
                ContainsMm(text, mmMask, "လို့") ||
                ContainsMm(text, mmMask, "ပရောဂျက်") ||
                ContainsMm(text, mmMask, "လုံခြုံစွာ") ||
                ContainsMm(text, mmMask, "သန့်ရှင်း") ||
                ContainsMm(text, mmMask, "အရေးပါပါ") ||
                ContainsMm(text, mmMask, "စိတ်အပန်းပြေ"))
            {
                foreach (Match mGlue in GluedPhraseBoundarySplitPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, mGlue.Index, mGlue.Length))
                        continue;
                    string spacedFix = mGlue.Groups["Left"].Value + " " + mGlue.Groups["Right"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, mGlue.Index, mGlue.Length, mGlue.Value, spacedFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ပုဒ်အဖြတ်အတောက် ရှင်းလင်းစေရန် ဝိဘတ်/နောက်ဆက်ပစ္စည်းနောက်၌ Space ခြားသင့်ပါသည် — '{spacedFix}' ဟု ရေးသားရမည်။",
                        "ပုဒ်အဖြတ်အတောက် မှန်ကန်သော အရေးအသား", "SYNTAX", true,
                        "သီးခြားပုဒ်စုနှစ်ခု ဆက်စပ်ရာတွင် ဝိဘတ် သို့မဟုတ် ကြိယာဝိသေသနပြုပစ္စည်းနောက်၌ နေရာလွတ် (Space) ခြားရေးခြင်းဖြင့် ဝါကျဖတ်ရှုမှု ပိုမိုရှင်းလင်းမှန်ကန်စေသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာ ပုဒ်အဖြတ်အတောက်စံနှုန်း");
                }
            }

            // 6f. Dangling Conjunction Before Plural Pronoun ('နှင့် တို့တွင် -> တို့တွင်')
            if (ContainsMm(text, mmMask, "တို့"))
            {
                foreach (Match mDang in DanglingConjunctionBeforeToPattern.Matches(text))
                {
                    Group g = mDang.Groups["Target"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "တို့",
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"ဒုတိယနာမ်ပုဒ် မပါရှိဘဲ သမ္ဗန္ဓ 'နှင့်' ပိုနေပါသည် — '{g.Value}' အစား 'တို့' ဟု ရေးသားရမည်။",
                            "အများပြပစ္စည်း ('~တို့')", "PART", true,
                            "ယှဉ်တွဲပြသမ္ဗန္ဓ 'နှင့်' နောက်တွင် ဒုတိယနာမ်ပုဒ် မပါဘဲ အများပြပစ္စည်း 'တို့' တိုက်ရိုက်မလိုက်နိုင်ပါ။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သမ္ဗန္ဓပစ္စည်း နှင့် နာမ်ပစ္စည်း)");
                    }
                }
            }

            // 6g. Spurious Creaky 'တွေ့ -> တွေ', Dropped-Creaky 'ခြင်းဖြင် -> ခြင်းဖြင့်', & Demonstrative/Possessive 'ခြင်းဤ -> ခြင်း၏'
            if (ContainsMm(text, mmMask, "တွေ့") ||
                ContainsMm(text, mmMask, "ဖြင်") ||
                ContainsMm(text, mmMask, "ဖျင်") ||
                ContainsMm(text, mmMask, "ခြင်းဤ"))
            {
                foreach (Match mMorph in SpuriousTweAndPhinMorphoPattern.Matches(text))
                {
                    if (mMorph.Groups["SpurTwe"].Success && !OverlapsExistingIssue(issues, mMorph.Groups["SpurTwe"].Index, mMorph.Groups["SpurTwe"].Length))
                    {
                        Group g = mMorph.Groups["SpurTwe"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တွေ", ProofingIssueType.SpellingError,
                            "အများပြ နာမ်ပစ္စည်းတွင် အောက်မြစ်မပါရပါ — 'တွေ့' (ကြုံတွေ့သည်) အစား 'တွေ' ဟု ရေးသားရမည်။",
                            "အများပြပစ္စည်း ('~တွေ')", "PART", false);
                    }
                    else if (mMorph.Groups["MisPhin"].Success && !OverlapsExistingIssue(issues, mMorph.Groups["MisPhin"].Index, mMorph.Groups["MisPhin"].Length))
                    {
                        Group g = mMorph.Groups["MisPhin"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဖြင့်", ProofingIssueType.SpellingError,
                            $"အသုံးခံပြ နာမ်ဝိဘတ်တွင် '{g.Value}' အစား 'ဖြင့်' (ဖဦးထုပ် ရရစ် ငသတ် အောက်မြစ်) ဟု ရေးသားရမည်။",
                            "အသုံးခံပြဝိဘတ် ('~ဖြင့်')", "PART", true);
                    }
                    else if (mMorph.Groups["MisIiPoss"].Success && !OverlapsExistingIssue(issues, mMorph.Groups["MisIiPoss"].Index, mMorph.Groups["MisIiPoss"].Length))
                    {
                        Group g = mMorph.Groups["MisIiPoss"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "၏", ProofingIssueType.SpellingError,
                            "ပိုင်ဆိုင်မှုပြ နာမ်ဝိဘတ်တွင် ညွှန်ပြနာမ်စား 'ဤ' အစား '၏' ကို သုံးနှုန်းရမည်။",
                            "ပိုင်ဆိုင်မှုပြဝိဘတ် ('~၏')", "PART", true);
                    }
                }
            }

            // 6h. Intra-Sentence Register-Clash Detector (catches stark colloquial<->formal mixing within the same clause/sentence)
            if (ContainsMm(text, mmMask, "အေးဆေး") ||
                ContainsMm(text, mmMask, "ပေး၏") ||
                ContainsMm(text, mmMask, "ဘူး") ||
                ContainsMm(text, mmMask, "ပါသည်၊") ||
                ContainsMm(text, mmMask, "လွှတ်တော်") ||
                ContainsMm(text, mmMask, "တရားရုံးက") ||
                ContainsMm(text, mmMask, "ဟိုတစ်နေ့က") ||
                ContainsMm(text, mmMask, "သမ္မတက") ||
                ContainsMm(text, mmMask, "ခုမှ"))
            {
                foreach (Match mReg in IntraSentenceRegisterClashV63Pattern.Matches(text))
                {
                    if (mReg.Groups["AeSay"].Success && !OverlapsExistingIssue(issues, mReg.Groups["AeSay"].Index, mReg.Groups["AeSay"].Length))
                    {
                        Group g = mReg.Groups["AeSay"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တိတ်တဆိတ်", ProofingIssueType.OfficialStyleGrammarError,
                            "စည်းကမ်း/စာပေဝါကျ ('~ခန်းတွင် ... နေရမည်') အတွင်း အပြောစကား 'အေးဆေး' အစား 'တိတ်တဆိတ်' (သို့မဟုတ် 'ငြိမ်သက်စွာ') ကို သုံးနှုန်းရမည်။",
                            "စာပေ/ရုံးသုံး ကြိယာဝိသေသန ('တိတ်တဆိတ်')", "ADV", true,
                            "စာပေဟန် 'တွင် ... နေရမည်' ဝါကျဘောင်အတွင်း အရပ်သုံးစကား 'အေးဆေး' ရောနှောနေသဖြင့် စကားအဆင့်ညီညွတ်သော 'တိတ်တဆိတ်' ဖြင့် အစားထိုးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                    }
                    else if (mReg.Groups["PayEi"].Success && !OverlapsExistingIssue(issues, mReg.Groups["PayEi"].Index, mReg.Groups["PayEi"].Length))
                    {
                        Group g = mReg.Groups["PayEi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပေးလိုက်တယ်", ProofingIssueType.OfficialStyleGrammarError,
                            "အပြောစကားဝါကျ ('~မှာ ...') အဆုံး၌ စာပေဟန် 'ပေး၏' ရောထွေးနေပါသည် — 'ပေးလိုက်တယ်' (သို့မဟုတ် 'ပေးတယ်') ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                            "အပြောစကား ကြိယာအဆုံးသတ် ('ပေးလိုက်တယ်')", "V", false,
                            "ဝါကျရှေ့ပိုင်း၌ အပြောစကားဝိဘတ် 'မှာ' သုံးထားသဖြင့် အဆုံးသတ်၌ စာပေဝိဘတ် '၏' မသုံးရဘဲ အပြောစကားနှင့် ညီညွတ်အောင် ရေးသားရမည်။",
                            "မောင်ခင်မင်(ဓနုဖြူ) — အပြောစကားနှင့် အရေးစကား ညီညွတ်မှုစည်းမျဉ်း");
                    }
                    else if (mReg.Groups["CasualNegBya"].Success && !OverlapsExistingIssue(issues, mReg.Groups["CasualNegBya"].Index, mReg.Groups["CasualNegBya"].Length))
                    {
                        Group g = mReg.Groups["CasualNegBya"];
                        string politeNeg = mReg.Groups["NegStem"].Value + "ပါ";
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, politeNeg, ProofingIssueType.OfficialStyleGrammarError,
                            $"လေးစားဂါရဝပြု/စာပေ ဝါကျအတွင်း အရပ်သုံး '{g.Value}' ရောနှောနေပါသည် — '{politeNeg}' ဟု ယဉ်ကျေးညီညွတ်စွာ သုံးနှုန်းရမည်။",
                            $"ယဉ်ကျေး/စာပေ အငြင်းပုဒ် ('{politeNeg}')", "V", true,
                            "ရှေ့ဝါကျပိုင်း၏ ဂါရဝပြု/စာပေ အဆင့်အတန်းနှင့် ကိုက်ညီစေရန် 'ဘူးဗျ/ဘူးဗျာ' အစား 'ပါ' ဖြင့် ပိတ်ရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာ လမ်းညွှန်");
                    }
                    else if (mReg.Groups["Clause2Colloq"].Success && !OverlapsExistingIssue(issues, mReg.Groups["Clause2Colloq"].Index, mReg.Groups["Clause2Colloq"].Length))
                    {
                        Group g = mReg.Groups["Clause2Colloq"];
                        string formalFix = g.Value.Substring(0, g.Value.Length - 3) + "ပါသည်";
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, formalFix, ProofingIssueType.OfficialStyleGrammarError,
                            $"ပထမဝါကျခွဲ ('~ပါသည်၊') နှင့် စကားအဆင့် ညီညွတ်စေရန် '{g.Value}' အစား '{formalFix}' ဟု သုံးနှုန်းရမည်။",
                            $"စကားအဆင့်ညီညွတ်သော ဝါကျအဆုံးသတ် ('{formalFix}')", "V", true,
                            "တစ်ဝါကျတည်းအတွင်း ရှေ့ဝါကျခွဲကို 'ပါသည်' ဖြင့် ရေးသားထားပြီး နောက်ဝါကျခွဲကို 'တယ်' ဖြင့် အဆုံးသတ်ခြင်းသည် စကားအဆင့် မညီညွတ်ပါ။",
                            "မောင်ခင်မင်(ဓနုဖြူ) — အပြောစကားနှင့် အရေးစကား ညီညွတ်မှုစည်းမျဉ်း");
                    }
                    else if (mReg.Groups["ParlDi"].Success && !OverlapsExistingIssue(issues, mReg.Groups["ParlDi"].Index, mReg.Groups["ParlDi"].Length))
                    {
                        Group g = mReg.Groups["ParlDi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယင်း", ProofingIssueType.OfficialStyleGrammarError,
                            "လွှတ်တော်/ရုံးသုံး အရေးစကားအတွင်း အပြောစကား 'ဒီ' အစား 'ယင်း' (သို့မဟုတ် 'ဤ') ကို သုံးနှုန်းရမည်။",
                            "ရုံးသုံး ညွှန်းပြနာမ်စား ('ယင်း')", "PRON", true);
                    }
                    else if (mReg.Groups["CourtDiLu"].Success && !OverlapsExistingIssue(issues, mReg.Groups["CourtDiLu"].Index, mReg.Groups["CourtDiLu"].Length))
                    {
                        Group g = mReg.Groups["CourtDiLu"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယင်းတရားခံ", ProofingIssueType.OfficialStyleGrammarError,
                            "တရားရုံး စာပေအရေးအသားအတွင်း အပြောစကား 'ဒီလူ' အစား 'ယင်းတရားခံ' (သို့မဟုတ် 'အဆိုပါပုဂ္ဂိုလ်') ကို သုံးနှုန်းရမည်။",
                            "တရားရုံး/ရုံးသုံး အသုံးအနှုန်း ('ယင်းတရားခံ')", "N", true);
                    }
                    else if (mReg.Groups["ResHoTaNay"].Success && !OverlapsExistingIssue(issues, mReg.Groups["ResHoTaNay"].Index, mReg.Groups["ResHoTaNay"].Length))
                    {
                        Group g = mReg.Groups["ResHoTaNay"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယခင်က", ProofingIssueType.OfficialStyleGrammarError,
                            "သုတေသန/စာပေ အရေးစကားအတွင်း အပြောစကား 'ဟိုတစ်နေ့က' အစား 'ယခင်က' (သို့မဟုတ် 'ပြီးခဲ့သည့်ရက်က') ကို သုံးနှုန်းရမည်။",
                            "စာပေသုံး အချိန်ပြပုဒ် ('ယခင်က')", "ADV", true);
                    }
                    else if (mReg.Groups["PresPyaw"].Success && !OverlapsExistingIssue(issues, mReg.Groups["PresPyaw"].Index, mReg.Groups["PresPyaw"].Length))
                    {
                        Group g = mReg.Groups["PresPyaw"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မိန့်ကြားခဲ့သည်", ProofingIssueType.OfficialStyleGrammarError,
                            "နိုင်ငံတော်အကြီးအကဲဆိုင်ရာ စာပေသတင်းဝါကျတွင် အပြောစကား 'ပြောခဲ့ပါတယ်' အစား 'မိန့်ကြားခဲ့သည်' (သို့မဟုတ် 'ပြောကြားခဲ့သည်') ကို သုံးနှုန်းရမည်။",
                            "ရုံးသုံး/သတင်းစာပေ ကြိယာ ('မိန့်ကြားခဲ့သည်')", "V", true);
                    }
                    else if (mReg.Groups["GovKhuMha"].Success && !OverlapsExistingIssue(issues, mReg.Groups["GovKhuMha"].Index, mReg.Groups["GovKhuMha"].Length))
                    {
                        Group g = mReg.Groups["GovKhuMha"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ယခုမှ", ProofingIssueType.OfficialStyleGrammarError,
                            "စာပေအရေးအသား ('အစိုးရ၏ ...') အတွင်း အပြောစကား 'ခုမှ' အစား 'ယခုမှ' ကို သုံးနှုန်းရမည်။",
                            "စာပေသုံး အချိန်ပြပုဒ် ('ယခုမှ')", "ADV", true);
                    }
                }
            }

            // 6i. V64 Bound Morphological Tone/Coda, Desiderative '-ချင်', Interrogative Hybrid & Causative Double-'က' Repairs
            bool mayHaveV64Morpho =
                ContainsMm(text, mmMask, "လပ်ခွင်") ||
                ContainsMm(text, mmMask, "စားခွင်") ||
                ContainsMm(text, mmMask, "ယူခွင်") ||
                ContainsMm(text, mmMask, "ရောက်ခွင်") ||
                ContainsMm(text, mmMask, "ဆိုခွင်") ||
                ContainsMm(text, mmMask, "ပိုင်ခွင်") ||
                ContainsMm(text, mmMask, "ယူပီ") ||
                ContainsMm(text, mmMask, "သောက်ပီ") ||
                ContainsMm(text, mmMask, "ကိုင်ပီ") ||
                ContainsMm(text, mmMask, "ကျောင်းပီ") ||
                ContainsMm(text, mmMask, "ပူဇော်ပြီ") ||
                ContainsMm(text, mmMask, "ကန်တော့ပြီ") ||
                ContainsMm(text, mmMask, "ဆုတောင်းပြီ") ||
                ContainsMm(text, mmMask, "လိုကောင်း") ||
                ContainsMm(text, mmMask, "ပယ်") ||
                ContainsMm(text, mmMask, "လုပ်ရယ်") ||
                ContainsMm(text, mmMask, "ထဲမာ") ||
                ContainsMm(text, mmMask, "နေမှာ") ||
                ContainsMm(text, mmMask, "လှ့") ||
                ContainsMm(text, mmMask, "ကျွန်တော်မှာ") ||
                ContainsMm(text, mmMask, "ကျွန်တော်ရဲ့") ||
                ContainsMm(text, mmMask, "ကျွန်တော ") ||
                ContainsMm(text, mmMask, "ကျွန်တောအ") ||
                ContainsMm(text, mmMask, "တွေရ ") ||
                ContainsMm(text, mmMask, "တို့ရ ") ||
                ContainsMm(text, mmMask, "မယ်သူ") ||
                ContainsMm(text, mmMask, "ပိုင်မှာ") ||
                ContainsMm(text, mmMask, "ပိုင်တွင်") ||
                ContainsMm(text, mmMask, "ပိုင်၌") ||
                ContainsMm(text, mmMask, "ရေလုပ်") ||
                ContainsMm(text, mmMask, "ရေစနစ်") ||
                ContainsMm(text, mmMask, "ရေကဏ္ဍ") ||
                ContainsMm(text, mmMask, "ပါတယ်လား") ||
                ContainsMm(text, mmMask, "ပါစေလား") ||
                ContainsMm(text, mmMask, "ခြင်းတည်") ||
                ContainsMm(text, mmMask, "တွေဂ") ||
                ContainsMm(text, mmMask, "များဂ") ||
                ContainsMm(text, mmMask, "လုပ်ငန်းထွင်") ||
                ContainsMm(text, mmMask, "မရှိဟော") ||
                ContainsMm(text, mmMask, "ဟို့") ||
                ContainsMm(text, mmMask, "ခြင်တယ်") ||
                ContainsMm(text, mmMask, "ခြင်သည်") ||
                ContainsMm(text, mmMask, "ခြင်ပါ") ||
                ContainsMm(text, mmMask, "ခြင်ဘူး") ||
                ((ContainsMm(text, mmMask, "တွေက ") || ContainsMm(text, mmMask, "များက ")) &&
                 (ContainsMm(text, mmMask, "စေတယ်") || ContainsMm(text, mmMask, "စေသည်") || ContainsMm(text, mmMask, "စေပါ") || ContainsMm(text, mmMask, "စေပြီး")));

            if (mayHaveV64Morpho)
            {
                foreach (Match m64 in V64MorphoAndCaseCollisionPattern.Matches(text))
                {
                    if (m64.Groups["Khwin"].Success && !OverlapsExistingIssue(issues, m64.Groups["Khwin"].Index, m64.Groups["Khwin"].Length))
                    {
                        Group g = m64.Groups["Khwin"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ခွင့်", ProofingIssueType.SpellingError,
                            "အခွင့်အရေး/ခွင့်ပြုချက်ပြ နောက်ဆက်ပုဒ်တွင် အောက်မြစ်ဖြင့် 'ခွင့်' ဟု ရေးသားရမည်။", "ခွင့်ပြုချက်ပြပုဒ် ('~ခွင့်')", "N", true);
                    }
                    else if (m64.Groups["YuPi"].Success && !OverlapsExistingIssue(issues, m64.Groups["YuPi"].Index, m64.Groups["YuPi"].Length))
                    {
                        Group g = m64.Groups["YuPi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပြီး", ProofingIssueType.SpellingError,
                            "ပြီးဆုံး/ဆက်စပ်ပြ ကြိယာထောက်ပစ္စည်းကို ရရစ် ဝစ္စပေါက်ဖြင့် 'ပြီး' ဟု ရေးသားရမည်။", "ကြိယာနောက်ဆက် ('~ပြီး')", "PART", true);
                    }
                    else if (m64.Groups["PuzawPyi"].Success && !OverlapsExistingIssue(issues, m64.Groups["PuzawPyi"].Index, m64.Groups["PuzawPyi"].Length))
                    {
                        Group g = m64.Groups["PuzawPyi"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပြီး", ProofingIssueType.SpellingError,
                            "ရှေ့ကြိယာနှင့် နောက်ကြိယာ ဆက်စပ်ရာတွင် ဝစ္စပေါက်ဖြင့် 'ပြီး' ဟု ရေးသားရမည်။", "ကြိယာဆက်ပစ္စည်း ('~ပြီး')", "PART", true);
                    }
                    else if (m64.Groups["LoKaung"].Success && !OverlapsExistingIssue(issues, m64.Groups["LoKaung"].Index, m64.Groups["LoKaung"].Length))
                    {
                        Group g = m64.Groups["LoKaung"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လို့", ProofingIssueType.SpellingError,
                            "'~လို့ကောင်းသည်' ပုံစံတွင် အောက်မြစ်ဖြင့် 'လို့' ဟု ရေးသားရမည်။", "ကြိယာဆက်ပစ္စည်း ('~လို့ကောင်း')", "PART", false);
                    }
                    else if (m64.Groups["TaPae"].Success && !OverlapsExistingIssue(issues, m64.Groups["TaPae"].Index, m64.Groups["TaPae"].Length))
                    {
                        Group g = m64.Groups["TaPae"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပဲ", ProofingIssueType.SpellingError,
                            "အလေးအနက်ပြပစ္စည်းကို နောက်ပစ်ဖြင့် 'ပဲ' ဟု ရေးသားရမည် ('ပယ်' သည် ပယ်ဖျက်သည် ကြိယာဖြစ်သည်)။", "အလေးအနက်ပြပစ္စည်း ('~ပဲ')", "PART", true);
                    }
                    else if (m64.Groups["LokeYae"].Success && !OverlapsExistingIssue(issues, m64.Groups["LokeYae"].Index, m64.Groups["LokeYae"].Length))
                    {
                        Group g = m64.Groups["LokeYae"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လုပ်ရတယ်", ProofingIssueType.SpellingError,
                            $"ကြိယာအဆုံးသတ် ချို့ယွင်းနေပါသည် — '{g.Value}' အစား 'လုပ်ရတယ်' ဟု ရေးသားရမည်။", "ကြိယာအဆုံးသတ် ('လုပ်ရတယ်')", "V", false);
                    }
                    else if (m64.Groups["HtaeMa"].Success && !OverlapsExistingIssue(issues, m64.Groups["HtaeMa"].Index, m64.Groups["HtaeMa"].Length))
                    {
                        Group g = m64.Groups["HtaeMa"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ထဲမှာ", ProofingIssueType.SpellingError,
                            "နေရာပြ နာမ်ဝိဘတ်တွင် ဟထိုးဖြင့် 'ထဲမှာ' ဟု ရေးသားရမည်။", "နေရာပြဝိဘတ် ('~ထဲမှာ')", "PART", false);
                    }
                    else if (m64.Groups["XmasNay"].Success && !OverlapsExistingIssue(issues, m64.Groups["XmasNay"].Index, m64.Groups["XmasNay"].Length))
                    {
                        Group g = m64.Groups["XmasNay"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "နေ့မှာ", ProofingIssueType.SpellingError,
                            "ရက်/နေ့ရက် အနက်တွင် အောက်မြစ်ဖြင့် 'နေ့မှာ' ဟု ရေးသားရမည်။", "အချိန်ပြနာမ် ('~နေ့မှာ')", "N", false);
                    }
                    else if (m64.Groups["LhaCreaky"].Success && !OverlapsExistingIssue(issues, m64.Groups["LhaCreaky"].Index, m64.Groups["LhaCreaky"].Length))
                    {
                        Group g = m64.Groups["LhaCreaky"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လှ", ProofingIssueType.SpellingError,
                            "နာမဝိသေသန 'လှ' တွင် အောက်မြစ်မပါရပါ — 'လှ' ဟု ရေးသားရမည်။", "နာမဝိသေသန ('လှ')", "ADJ", true);
                    }
                else if ((m64.Groups["KyawntawCreaky"].Success || m64.Groups["KyawntawNoAsat"].Success))
                {
                    Group g = m64.Groups["KyawntawCreaky"].Success ? m64.Groups["KyawntawCreaky"] : m64.Groups["KyawntawNoAsat"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကျွန်တော့်", ProofingIssueType.SpellingError,
                            $"ဝိဘတ်ရှေ့ နာမ်စားတွင် အောက်မြစ်ဖြင့် 'ကျွန်တော့်' ဟု ရေးသားရမည်။", "နာမ်စား ('ကျွန်တော့်')", "PRON", true);
                    }
                }
                else if (m64.Groups["TweYa"].Success && !OverlapsExistingIssue(issues, m64.Groups["TweYa"].Index, m64.Groups["TweYa"].Length))
                {
                    Group g = m64.Groups["TweYa"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ရဲ့", ProofingIssueType.SpellingError,
                        "ပိုင်ဆိုင်မှုပြဝိဘတ်တွင် 'ရဲ့' ဟု ရေးသားရမည်။", "ပိုင်ဆိုင်မှုပြဝိဘတ် ('~ရဲ့')", "PART", false);
                }
                else if (m64.Groups["MaeThu"].Success && !OverlapsExistingIssue(issues, m64.Groups["MaeThu"].Index, m64.Groups["MaeThu"].Length))
                {
                    Group g = m64.Groups["MaeThu"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မယ့်", ProofingIssueType.SpellingError,
                        "နောက်လိုက်နာမ် ('သူ') ကို အထူးပြုရာတွင် အောက်မြစ်ဖြင့် 'မယ့်' ဟု ရေးသားရမည်။", "နာမဝိသေသနပြုပစ္စည်း ('~မယ့်')", "PART", false);
                }
                else if (m64.Groups["NyanayPaing"].Success && !OverlapsExistingIssue(issues, m64.Groups["NyanayPaing"].Index, m64.Groups["NyanayPaing"].Length))
                {
                    Group g = m64.Groups["NyanayPaing"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပိုင်း", ProofingIssueType.SpellingError,
                        "အချိန်အပိုင်းအခြားပြ ပစ္စည်းတွင် ဝစ္စပေါက်ဖြင့် 'ပိုင်း' ဟု ရေးသားရမည်။", "အချိန်ပိုင်းပြပုဒ် ('~ပိုင်း')", "N", true);
                }
                else if (m64.Groups["YeLupan"].Success && !OverlapsExistingIssue(issues, m64.Groups["YeLupan"].Index, m64.Groups["YeLupan"].Length))
                {
                    Group g = m64.Groups["YeLupan"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ရေး", ProofingIssueType.SpellingError,
                        "ကဏ္ဍ/လုပ်ငန်းအမည် ပေါင်းစပ်ပုဒ်တွင် ဝစ္စပေါက်ဖြင့် 'ရေး' ဟု ရေးသားရမည်။", "ပေါင်းစပ်နာမ်ပစ္စည်း ('~ရေး')", "PART", true,
                        "ကြိယာ/နာမ်ပေါင်းစပ်ပုဒ်နောက်ဆက် '~ရေး' ကို ဝစ္စပေါက်ဖြင့် ရေးသားရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (အဆက်တွဲဝိဘတ် နှင့် ပေါင်းစပ်ပုဒ်)");
                }
                else if (m64.Groups["PataeLar"].Success && !OverlapsExistingIssue(issues, m64.Groups["PataeLar"].Index, m64.Groups["PataeLar"].Length))
                {
                    Group g = m64.Groups["PataeLar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပါသလား", ProofingIssueType.OfficialStyleGrammarError,
                        "အမေးဝါကျအဆုံး၌ ဟုတ်မှန်ပြ 'တယ်' နှင့် 'လား' ရောထွေးနေပါသည် — 'ပါသလား' ဟု သုံးနှုန်းရမည်။", "အမေးဝါကျအဆုံးသတ် ('~ပါသလား')", "PART", true);
                }
                else if (m64.Groups["PasayLar"].Success && !OverlapsExistingIssue(issues, m64.Groups["PasayLar"].Index, m64.Groups["PasayLar"].Length))
                {
                    Group g = m64.Groups["PasayLar"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပါစေ", ProofingIssueType.OfficialStyleGrammarError,
                        "ဆုတောင်းဝါကျ ('~ပါစေ') နောက်တွင် အမေးပစ္စည်း 'လား' မထည့်ရပါ — 'ပါစေ' ဟုသာ ရေးသားရမည်။", "ဆုတောင်းပစ္စည်း ('~ပါစေ')", "PART", true);
                }
                else if (m64.Groups["ChinnTae"].Success && !OverlapsExistingIssue(issues, m64.Groups["ChinnTae"].Index, m64.Groups["ChinnTae"].Length))
                {
                    Group g = m64.Groups["ChinnTae"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သည်", ProofingIssueType.SpellingError,
                        "ကတ္တားဝိဘတ်ကို သကြီးဖြင့် 'သည်' ဟု ရေးသားရမည်။", "ကတ္တားဝိဘတ် ('~သည်')", "PART", true);
                }
                else if (m64.Groups["TweGa"].Success && !OverlapsExistingIssue(issues, m64.Groups["TweGa"].Index, m64.Groups["TweGa"].Length))
                {
                    Group g = m64.Groups["TweGa"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "က", ProofingIssueType.SpellingError,
                        "ကတ္တားဝိဘတ်ကို ကကြီးဖြင့် 'က' ဟု ရေးသားရမည်။", "ကတ္တားဝိဘတ် ('~က')", "PART", true);
                }
                else if (m64.Groups["LupanHtwin"].Success && !OverlapsExistingIssue(issues, m64.Groups["LupanHtwin"].Index, m64.Groups["LupanHtwin"].Length))
                {
                    Group g = m64.Groups["LupanHtwin"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "တွင်", ProofingIssueType.SpellingError,
                        "နေရာပြ နာမ်ဝိဘတ်ကို တဝမ်းပူဖြင့် 'တွင်' ဟု ရေးသားရမည်။", "နေရာပြဝိဘတ် ('~တွင်')", "PART", true);
                }
                else if (m64.Groups["MashiHaw"].Success && !OverlapsExistingIssue(issues, m64.Groups["MashiHaw"].Index, m64.Groups["MashiHaw"].Length))
                {
                    Group g = m64.Groups["MashiHaw"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သော", ProofingIssueType.SpellingError,
                        "နာမဝိသေသနပြုပစ္စည်းကို သကြီးဖြင့် 'သော' ဟု ရေးသားရမည်။", "နာမဝိသေသနပြုပစ္စည်း ('~သော')", "PART", true);
                }
                else if (m64.Groups["HuHo"].Success && !OverlapsExistingIssue(issues, m64.Groups["HuHo"].Index, m64.Groups["HuHo"].Length))
                {
                    Group g = m64.Groups["HuHo"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သို့", ProofingIssueType.SpellingError,
                        "ရှေးရှုရာပြ နာမ်ဝိဘတ်ကို သကြီးဖြင့် 'သို့' ဟု ရေးသားရမည်။", "ရှေးရှုရာပြဝိဘတ် ('~သို့')", "PART", true);
                }
                else if (m64.Groups["DesidChin"].Success && !OverlapsExistingIssue(issues, m64.Groups["DesidChin"].Index, m64.Groups["DesidChin"].Length))
                {
                    Group g = m64.Groups["DesidChin"];
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ချင်", ProofingIssueType.SpellingError,
                        "ဆန္ဒပြ ကြိယာထောက်ပစ္စည်းကို ယပင့်ဖြင့် 'ချင်' ဟု ရေးသားရမည် ('ခြင်' သည် ပိုးခြင် ဖြစ်သည်)။", "ဆန္ဒပြ ကြိယာထောက်ပစ္စည်း ('~ချင်')", "PART", true);
                }
                else if (m64.Groups["CausPat"].Success && !OverlapsExistingIssue(issues, m64.Groups["CausPat"].Index, m64.Groups["CausPat"].Length))
                {
                    Group g = m64.Groups["CausPat"];
                    string fixedKo = g.Value.Substring(0, g.Value.Length - 1) + "ကို";
                    AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, fixedKo, ProofingIssueType.OfficialStyleGrammarError,
                        $"စေခိုင်း/ဖြစ်စေကြောင်းပြ ('~စေ') ဝါကျတွင် ရှေ့၌ ကတ္တား ('~က') ပါရှိပြီးဖြစ်သဖြင့် ခံရသူပုဒ်၌ ကံဝိဘတ် '{fixedKo}' ကို သုံးနှုန်းရမည်။",
                        $"ကံဝိဘတ် ('{fixedKo}')", "PART", true,
                        "ပြုလုပ်စေတတ်သော ကတ္တားပုဒ် ('~က') ပါရှိပြီးဖြစ်၍ အကျိုးသက်ရောက်ခံရသော ဒုတိယနာမ်ပုဒ်တွင် ကတ္တား 'က' ထပ်မသုံးရဘဲ ကံဝိဘတ် 'ကို' သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ကံဝိဘတ် စည်းမျဉ်း)");
                }
                }
            }

            // 6j. V65 Case-Role Collision, Causative Gerund Subject & Polarity/Additive Particle Repairs
            bool mayHaveV65Morpho =
                ContainsMm(text, mmMask, "ဘာမမဟုတ်") ||
                ContainsMm(text, mmMask, " ဘဲ") ||
                ContainsMm(text, mmMask, "မြတ်စွာဘုရားအား") ||
                ContainsMm(text, mmMask, "အရေးပါ") ||
                ContainsMm(text, mmMask, "အရေးကြီ") ||
                ContainsMm(text, mmMask, "လုပ်တာကို ") ||
                ContainsMm(text, mmMask, "ခြင်းကို ") ||
                ContainsMm(text, mmMask, "တိုးမြှင့်ပေး") ||
                ContainsMm(text, mmMask, "ဖြစ်စေ") ||
                ContainsMm(text, mmMask, "ရွေးချယ်နိုင်") ||
                ContainsMm(text, mmMask, "သတိပြုသင့်") ||
                ContainsMm(text, mmMask, "ရေဒီယိုက ") ||
                ContainsMm(text, mmMask, "ကူညီပေး") ||
                ContainsMm(text, mmMask, "ရှိသလို ") ||
                ContainsMm(text, mmMask, "လဲရှိပါ") ||
                ContainsMm(text, mmMask, "အတွင်းမှာလဲ") ||
                ContainsMm(text, mmMask, "ယဉ်ကျေးမှု") ||
                ContainsMm(text, mmMask, "ဥပမာ") ||
                ContainsMm(text, mmMask, "မရှိမဖြစ်လိုအပ်");

            if (mayHaveV65Morpho)
            {
                const string v65Auth = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် နှင့် ပစ္စည်းစည်းမျဉ်း)";
                foreach (Match m65 in V65CaseAndMorphoPattern.Matches(text))
                {
                    if (m65.Groups["BaMa"].Success && !OverlapsExistingIssue(issues, m65.Groups["BaMa"].Index, m65.Groups["BaMa"].Length))
                    {
                        Group g = m65.Groups["BaMa"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မှ", ProofingIssueType.SpellingError,
                            "'ဘာမှမဟုတ်' ပုံစံတွင် ဟထိုးဖြင့် 'မှ' ဟု ရေးသားရမည်။", "အလေးအနက်ပြပစ္စည်း ('ဘာမှ~')", "PART", false,
                            "အငြင်းကြိယာရှေ့ အလေးအနက်ပြပစ္စည်းတွင် 'ဘာမှ' ဟု ဟထိုးဖြင့် ရေးသားရမည်။", v65Auth);
                    }
                    else if (m65.Groups["SpacedBae"].Success && !OverlapsExistingIssue(issues, m65.Groups["SpacedBae"].Index, m65.Groups["SpacedBae"].Length))
                    {
                        Group g = m65.Groups["SpacedBae"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ပဲ", ProofingIssueType.OfficialStyleGrammarError,
                            "ဟုတ်မှန်/အလေးအနက်ပြ ပစ္စည်းတွင် 'ဘဲ' အစား 'ပဲ' ဟု ရေးသားရမည်။", "အလေးအနက်ပြပစ္စည်း ('~ပဲ')", "PART", false,
                            "အငြင်းကြိယာ ('မ~') မဟုတ်သဖြင့် အလေးအနက်ပြပစ္စည်း 'ပဲ' ကို သုံးနှုန်းရမည်။", v65Auth);
                    }
                    else if (m65.Groups["ToThint"].Success && !OverlapsExistingIssue(issues, m65.Groups["ToThint"].Index, m65.Groups["ToThint"].Length))
                    {
                        Group g = m65.Groups["ToThint"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "သည် ", ProofingIssueType.OfficialStyleGrammarError,
                            "ကတ္တားနာမ်ပုဒ်နောက်တွင် အောက်မြစ်မပါဘဲ ကတ္တားဝိဘတ် 'သည် ' ဟု ပုဒ်ဖြတ်ရေးသားရမည်။", "ကတ္တားဝိဘတ် ('~သည်')", "PART", true,
                            "နောက်လိုက်ပုဒ်သည် ကံပုဒ် ('မြတ်စွာဘုရားအား') ဖြစ်၍ နာမဝိသေသနပြု 'သည့်' အစား ကတ္တားဝိဘတ် 'သည်' သုံးရမည်။", v65Auth);
                    }
                    else if ((m65.Groups["BenefKoHa"].Success || m65.Groups["CopToKoHa"].Success))
                    {
                        Group g = m65.Groups["BenefKoHa"].Success ? m65.Groups["BenefKoHa"] : m65.Groups["CopToKoHa"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ဟာ", ProofingIssueType.OfficialStyleGrammarError,
                                "ကတ္တားပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား ကတ္တားဝိဘတ် 'ဟာ' (သို့မဟုတ် 'သည်') ကို သုံးနှုန်းရမည်။", "ကတ္တားဝိဘတ် ('~ဟာ')", "PART", false,
                                "ဖြစ်ခြင်း/အရေးပါခြင်းပြ ကြိယာ၏ ကတ္တားပုဒ်ဖြစ်၍ ကံဝိဘတ် 'ကို' မသုံးရဘဲ ကတ္တားဝိဘတ် 'ဟာ' သုံးရမည်။", v65Auth);
                        }
                    }
                    else if ((m65.Groups["TaKoKa"].Success || m65.Groups["GerundKoKa"].Success || m65.Groups["DoubleKoKa"].Success || m65.Groups["CopTweKoKa"].Success || m65.Groups["VitMhaKa"].Success))
                    {
                        Group g = m65.Groups["TaKoKa"].Success ? m65.Groups["TaKoKa"]
                                : m65.Groups["GerundKoKa"].Success ? m65.Groups["GerundKoKa"]
                                : m65.Groups["DoubleKoKa"].Success ? m65.Groups["DoubleKoKa"]
                                : m65.Groups["CopTweKoKa"].Success ? m65.Groups["CopTweKoKa"]
                                : m65.Groups["VitMhaKa"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "က", ProofingIssueType.OfficialStyleGrammarError,
                                $"ကတ္တားပုဒ်တွင် '{g.Value}' အစား ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည်။", "ကတ္တားဝိဘတ် ('~က')", "PART", false,
                                "ဝါကျ၏ ပြုလုပ်သူ/အကြောင်းရင်းခံ ကတ္တားပုဒ်ဖြစ်၍ ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည်။", v65Auth);
                        }
                    }
                    else if ((m65.Groups["ChooseKaKo"].Success || m65.Groups["NoticeKaKo"].Success || m65.Groups["RadioKaKo"].Success || m65.Groups["HelpKaKo"].Success))
                    {
                        Group g = m65.Groups["ChooseKaKo"].Success ? m65.Groups["ChooseKaKo"]
                                : m65.Groups["NoticeKaKo"].Success ? m65.Groups["NoticeKaKo"]
                                : m65.Groups["RadioKaKo"].Success ? m65.Groups["RadioKaKo"]
                                : m65.Groups["HelpKaKo"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "ကို", ProofingIssueType.OfficialStyleGrammarError,
                                "ကံပုဒ်တွင် ကတ္တားဝိဘတ် 'က' အစား ကံဝိဘတ် 'ကို' ကို သုံးနှုန်းရမည်။", "ကံဝိဘတ် ('~ကို')", "PART", true,
                                "ကံလိုကြိယာ၏ ခံရသူ ကံပုဒ်ဖြစ်သဖြင့် ကတ္တားဝိဘတ် 'က' အစား ကံဝိဘတ် 'ကို' သုံးရမည်။", v65Auth);
                        }
                    }
                    else if ((m65.Groups["PaeLae"].Success || m65.Groups["AffLae1"].Success || m65.Groups["AffLae2"].Success))
                    {
                        Group g = m65.Groups["PaeLae"].Success ? m65.Groups["PaeLae"]
                                : m65.Groups["AffLae1"].Success ? m65.Groups["AffLae1"]
                                : m65.Groups["AffLae2"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "လည်း", ProofingIssueType.OfficialStyleGrammarError,
                                $"အပေါင်းအစုပြ ပစ္စည်းတွင် '{g.Value}' အစား 'လည်း' ဟု ရေးသားရမည်။", "အပေါင်းအစုပြပစ္စည်း ('~လည်း')", "PART", true,
                                "ရှေ့အချက်အပြင် ထပ်လောင်းပါဝင်ကြောင်းပြသဖြင့် အပေါင်းအစုပြပစ္စည်း 'လည်း' ကို သုံးနှုန်းရမည်။", v65Auth);
                        }
                    }
                    else if (m65.Groups["DevKoMha"].Success && !OverlapsExistingIssue(issues, m65.Groups["DevKoMha"].Index, m65.Groups["DevKoMha"].Length))
                    {
                        Group g = m65.Groups["DevKoMha"];
                        AddGrammarOrSpellingIssue(issues, g.Index, g.Length, g.Value, "မှာ", ProofingIssueType.OfficialStyleGrammarError,
                            "ကဏ္ဍ/နေရာပြပုဒ်တွင် ကံဝိဘတ် 'ကို' အစား နေရာပြဝိဘတ် 'မှာ' (သို့မဟုတ် 'တွင်') ကို သုံးနှုန်းရမည်။", "နေရာပြဝိဘတ် ('~မှာ')", "PART", false,
                            "'အရေးပါသည်' ကြိယာနှင့် တွဲဖက်ရာတွင် နေရာ/ကဏ္ဍပြဝိဘတ် 'မှာ/တွင်' သုံးနှုန်းရမည်။", v65Auth);
                    }
                }
            }

            // 7. Trailing ASCII period '.' right after Myanmar sentence-final verb closer ('ရှိပါတယ်. -> ရှိပါတယ်။')
            if (text.IndexOf('.') >= 0)
            {
                foreach (Match m in MyanmarSentenceFinalAsciiDotPattern.Matches(text))
                {
                    Group g = m.Groups["Dot"];
                    if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, ".", "။",
                            ProofingIssueType.PunctuationError,
                            "မြန်မာဝါကျအဆုံးတွင် အင်္ဂလိပ် Full Stop ('.') အစား မြန်မာပုဒ်မ ('။') ကို သုံးနှုန်းရမည်။",
                            "မြန်မာပုဒ်မ ('။')", "PUNCT", true);
                    }
                }
            }
        }

        private static bool HasBarePhoWithoutCreaky(string text)
        {
            int idx = 0;
            while ((idx = text.IndexOf("ဖို", idx, StringComparison.Ordinal)) >= 0)
            {
                int after = idx + 3;
                if (after >= text.Length || (text[after] != '\u1037' && text[after] != '\u1038'))
                    return true;
                idx = after;
            }
            return false;
        }

        private static bool HasTrailingLexiconVerbOrAdj(MyanmarTrie trie, string normStem, bool allowAdj)
        {
            if (string.IsNullOrEmpty(normStem))
                return false;
            if (allowAdj &&
                (normStem.EndsWith("ကောင်း", StringComparison.Ordinal) ||
                 normStem.EndsWith("နိုင်", StringComparison.Ordinal) ||
                 normStem.EndsWith("ဖြစ်", StringComparison.Ordinal) ||
                 normStem.EndsWith("ရှိ", StringComparison.Ordinal) ||
                 normStem.EndsWith("တတ်", StringComparison.Ordinal)))
            {
                return true;
            }
            if (trie.HasPosTag(normStem, "V") && (allowAdj || !trie.HasPosTag(normStem, "N")))
                return true;
            if (allowAdj && trie.HasPosTag(normStem, "ADJ"))
                return true;

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(normStem, isPreNormalized: true);
            int maxTail = Math.Min(4, syls.Count);
            int accumChars = 0;
            for (int t = 1; t <= maxTail; t++)
            {
                accumChars += syls[syls.Count - t].Length;
                if (accumChars <= normStem.Length)
                {
                    string tail = normStem.Substring(normStem.Length - accumChars);
                    if (tail.Length >= 2)
                    {
                        if (trie.HasPosTag(tail, "V") && (allowAdj || !trie.HasPosTag(tail, "N")))
                            return true;
                        if (allowAdj && trie.HasPosTag(tail, "ADJ"))
                            return true;
                    }
                }
            }
            return false;
        }

        private static bool IsReciprocalOrImmediateChinStem(string normStem)
        {
            return normStem.EndsWith("အချင်း", StringComparison.Ordinal) ||
                   normStem.EndsWith("တစ်ဦး", StringComparison.Ordinal) ||
                   normStem.EndsWith("တစ်ယောက်", StringComparison.Ordinal) ||
                   normStem.EndsWith("ချက်", StringComparison.Ordinal) ||
                   normStem.EndsWith("ရွယ်တူ", StringComparison.Ordinal) ||
                   normStem.EndsWith("မိတ်ဆွေ", StringComparison.Ordinal) ||
                   normStem.EndsWith("ညီအစ်ကို", StringComparison.Ordinal) ||
                   normStem.EndsWith("သူငယ်", StringComparison.Ordinal) ||
                   normStem.EndsWith("သီ", StringComparison.Ordinal) ||
                   normStem.IndexOf("လျှင်", StringComparison.Ordinal) >= 0 ||
                   normStem.IndexOf("ရင်", StringComparison.Ordinal) >= 0;
        }
    }
}
