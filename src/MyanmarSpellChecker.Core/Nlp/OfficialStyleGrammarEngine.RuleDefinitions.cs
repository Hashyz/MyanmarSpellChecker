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
    public sealed partial class OfficialStyleGrammarEngine
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
            @"(?<![\u1000-\u103F])(?:ဘာ(?!သာ|ပြောပြော|လုပ်လုပ်|ဖြစ်ဖြစ်|ဆိုဆို|မဆို)|ဘယ်(?!ဘက်|ညာ|လ်|ဂျီ|လာ|လီ|လိုပဲ|သူမဆို|အရာမဆို)|အဘယ်ကြောင့်|အဘယ်|မည်သို့|မည်သူ|မည်သည့်)(?![\u1000-\u103F\s]{0,18}?(?:မဆို|လဲ|ဆိုတော့|ပေမဲ့|ပေမယ့်|ပေမယ့်|သော်လည်း))[\u1000-\u103F\s]{0,26}?(?<Target>လည်း)(?=\s*(?:[\u104B?]|$|ဆိုတာ(?!တော့)|ဆိုသည်|ဟု\s*မေး|လို့\s*မေး|မသိ))");

        // 4b. Affirmative 'လဲ' ("also/too") -> 'လည်း' (e.g. 'ကျွန်တော်လဲ သွားမယ်' -> 'ကျွန်တော်လည်း သွားမယ်', 'အနာဂတ်မှာလဲ' -> 'အနာဂတ်မှာလည်း')
        private static readonly Regex AffirmativeLaeMisusePattern = new Regex(
            @"(?:(?:ကျွန်တော်|ကျွန်မ|သူ|ငါ|မင်း|သင်|ဒါ|အဲဒါ|ထိုသူ|၎င်း|သား|သမီး|အားလုံး|တို့|များ|တွေ)|(?:အနာဂတ်|ပစ္စုပ္ပန်|အတိတ်|ဒီနေရာ|အိမ်|ကျောင်း|ရုံး|မြန်မာနိုင်ငံ)(?:မှာ|တွင်))\s*(?<Target>လဲ)(?=\s+[\u1000-\u102A])");

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
            @"(?<![\u1000-\u103F])(?<Target>[\u1000-\u103F]*[\u1000-\u1021][\u103B-\u103E]*[\u102B-\u1032\u1036]*[\u1000-\u1021]\u103A(?!\u102F\u1015\u103A)[\u102D-\u1032][\u1037\u1038]*[\u1000-\u103F]*)");

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
            @"(?:ပုဂံခေတ်|အင်းဝခေတ်|ကုန်းဘောင်ခေတ်|ပျူခေတ်|ရှေးခေတ်|[\u1000-\u103F\s]{2,16}?မင်းကြီး\s*လက်ထက်)(?:ဟာ|သည်|မှာ|တွင်|၌|က)?[^\u104A\u104B\r\n]{1,64}?(?<Target>ဖြစ်ပါလိမ့်မယ်|ဖြစ်ပါလိမ့်မည်|ဖြစ်လိမ့်မယ်|ဖြစ်လိမ့်မည်|ပါလိမ့်မယ်|ပါလိမ့်မည်|လိမ့်မယ်|လိမ့်မည်)(?=\s*[\u104B])");

        private static readonly Regex ImpossibleAspectStackPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>ခဲ့နေပြီ)(?=[\s\u104A\u104B]|$)");

        private static readonly Regex GenericGnomicHabitualPastPattern = new Regex(
            @"ဆိုတာ\s+[^\u104A\u104B\r\n]{1,36}?(?<Target>တတ်ခဲ့တယ်)(?=\s*(?:[\u104B]|$))");

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
                new Regex(@"(?:ရဟန်း(?:တော်)?|သံဃာ(?:တော်)?|ဆရာတော်(?:ကြီး|ဘုရား)?|ဘုန်းကြီး|ဘုန်းတော်ကြီး|ရှင်သာမဏေ|ကိုရင်|သီလရှင်|ပဉ္စင်း|ဦးပဉ္စင်း)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ကောင်|လုံး|ခု|စီး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
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

        private static readonly Regex RegionKaBeforeTashiPattern = new Regex(
            @"(?:ပြည်နယ်|တိုင်းဒေသကြီး|မြို့နယ်|ခရိုင်)(?<Target>က)(?=\s+တည်ရှိ(?:ပါ)?(?<End>သည်|တယ်|ပြီး|၍|သော|သည့်|သည့်|တဲ့))");

        private static readonly Regex PartitiveKaBeforeAsateApaingPattern = new Regex(
            @"(?<=(?:ဘဝ|စီးပွားရေး|နိုင်ငံရေး|ပညာရေး|ကျန်းမာရေး|ယဉ်ကျေးမှု|သမိုင်း|လူ့အဖွဲ့အစည်း|သဘာဝ|စနစ်|လုပ်ငန်းစဉ်))(?<Target>က)(?=\s+အစိတ်အပိုင်းတစ်ခု(?:ပဲ|ပါပဲ|သာ)?(?:\s*(?<End>ဖြစ်(?:ပါ)?(?:သည်|တယ်))|\s*[။\r\n]|$))");

        private static readonly Regex MissingCreakyAttributiveLamyiPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>လာမည်(?<Unit>နှစ်|လ|အပတ်|ရက်|ဘဏ္ဍာနှစ်))");

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
            @"(?:(?<![\u1000-\u103F])(?<Left>စာ|ဝန်|လေ|အိမ်|ဘယ်|ဘာ|ကိုယ်|လက်|ဒီအ|ထိုနေ|ဆရာ|ကား|ရေ|မြို့|ရုံး|မီး|စိ|လှေ|ရှေ့)|(?<![\u1000-\u103F]အ)(?<Left>လုပ်)|(?<Left>သိပ္ပံ|အဖွဲ့|ဘာသာ|ရဟန်း|ရောဂါ|မြေဆီ|အုပ်|မြန်မာ|စုစု|အစည်း|အခမ်း|အစီ|အခြေ|အတွေ့|အခွင့်|အချက်|အဆောက်|အထိမ်း|အလုပ်|အစား|အဝတ်|အရောင်း|အပေး|အဆက်|အကောင်|အသိ|လုပ်ရိုး|ကျောင်း|ကျောင်းအုပ်|ဆေးရုံ|မိန်း|ယောက်ျား|သတင်း|မော်တော်|ကွန်ပျူ|တယ်လီ|စာသင်|တက္က|အစိုး|ဥပ|ကျန်းမာ|စီးပွား|စိုက်ပျိုး|မွေးမြူ|ပညာ|လူမှု|ပို့ဆောင်|ရေကြောင်း|ဝန်ကြီး|ဦးစီး|အိမ်ခြံ|နိုင်ငံ|ဆောင်ရွက်|လူနာတင်|မီးသတ်|တပ်မ|ကိုယ်စား|ပလတ်|တရား|လုပ်ငန်း|အိမ်မှု|ရုပ်ရှင်|စာစီ))[ \t]+(?<Right>ကြည့်တိုက်|ပညာ|အစည်း|ထောင်|ငန်း|သံဃာ|ပုံးပျံ|ဝေဒနာ|လွှာ|ချုပ်ရေးမှူး|ချုပ်ရေး|နိုင်ငံ|တော်သား|ပေါင်း|အဝေး|အနား|အစဉ်|အနေ|အကြုံ|အရေး|အလမ်း|အလက်|အအုံ|အမှတ်|အကိုင်|အသောက်|အစား|အဝယ်|အယူ|အသွယ်|အထည်|လုပ်စဉ်|သား|သူ|ထမ်း|သမား|ချိန်|အုပ်|ကြီး|ဝန်|လမ်း|ချိုး|နယ်|ခန်း|ကလေး|လေး|စာ|ယာဉ်|တာ|ဖုန်း|ကျောင်း|သိုလ်|ရ|ဒေ|ရေး|ပို့ဆောင်ရေး|ဌာန|မြေ|ခြား|မှတ်|ချက်|တိုင်း|ရာ|ကား|လို|လို့|တော်|စားလှယ်|လှယ်|စတစ်|ဥပဒေ|ခွင်|စစ်|ကိစ္စ|ရုံ|ဆိပ်|စာရိုက်|မှောက်)(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|နှင့်|နဲ့|သည်|၏|ရဲ့|၌|တွင်|က|မှာ|ဟာ|များ|တွေ|တို့|အနှံ့|အတွက်|အကြောင်း|ဆိုင်ရာ|ဖြင့်|သဖြင့်|ကြောင့်|အပြင်|ရရှိ|ဖြတ်စ|ကဏ္ဍ|ရေးရာ|အုပ်ချုပ်|လုပ်ငန်း|တစ်ခု|လွတ်|အတိုင်း|ပြီး|အနီး)",
            RegexOptions.Compiled);

        private static readonly Regex ThreePartBrokenCompoundPattern = new Regex(
            @"(?<![\u1000-\u103F])(?:(?<P1>စာ)[ \t]+(?<P2>ကြည့်)[ \t]+(?<P3>တိုက်)|(?<P1>လူ)[ \t]+(?<P2>သွား)[ \t]+(?<P3>လမ်း)|(?<P1>မျက်နှာ)[ \t]+(?<P2>ချင်း)[ \t]+(?<P3>ဆိုင်)|(?<P1>ကျောင်း)[ \t]+(?<P2>အုပ်)[ \t]+(?<P3>ကြီး)|(?<P1>လူ)[ \t]+(?<P2>မှု)[ \t]+(?<P3>ရေး)|(?<P1>ဘာ)[ \t]+(?<P2>သာ)[ \t]+(?<P3>ရေး)|(?<P1>ကျန်း)[ \t]+(?<P2>မာ)[ \t]+(?<P3>ရေး)|(?<P1>စီး)[ \t]+(?<P2>ပွား)[ \t]+(?<P3>ရေး)|(?<P1>ပ)[ \t]+(?<P2>ညာ)[ \t]+(?<P3>ရေး)|(?<P1>နိုင်)[ \t]+(?<P2>ငံ)[ \t]+(?<P3>ရေး)|(?<P1>တိုင်း)[ \t]+(?<P2>ရင်း)[ \t]+(?<P3>သား))(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|နဲ့|သည်|၏|၌|တွင်|က|မှာ|များ|တွေ|လုပ်ငန်း|ဆိုင်ရာ)",
            RegexOptions.Compiled);

        private static readonly Regex WildcardLeftBoundSuffixPattern = new Regex(
            @"(?:(?<Left>[\u1000-\u103F]{2,14})[ \t]+(?<Right>တော်မူ(?=၍|သည်(?!ကား|ကာ)|၏|ပါ|သော|သည့်|သည့်))|(?<![\u1000-\u103F])(?<Left>[\u1000-\u103F]{1,12}ပေ)[ \t]+(?<Right>မဲ့|မယ့်|မယ့်)|(?<![\u1000-\u103F])(?<Left>[\u1000-\u103F]{1,12}ခဲ့)[ \t]+(?<Right>ရ(?=တယ်|သည်|ပါ))|(?<![\u1000-\u103F])(?<Left>[\u1000-\u103F]{1,12})[ \t]+(?<Right>မှာလား)|(?<![\u1000-\u103F])(?<Left>[\u1000-\u103F]{3,14}မှု)[ \t]+(?<Right>များ|တွေ|ကို)(?![\u102B-\u103E\u103A]))",
            RegexOptions.Compiled);

        private static readonly Regex BoundSuffixBrokenSpacePattern = new Regex(
            @"(?:(?<Left>မခွဲ)[ \t]+(?<Right>ခြားဘဲ)|(?<Left>မြောက်|တောင်|အရှေ့|အနောက်)[ \t]+(?<Right>ပိုင်း(?=ရှိ|တွင်|၌|မှာ))|(?<Left>အရှေ့|အနောက်|တောင်|မြောက်)[ \t]+(?<Right>ဘက်(?=မှာ|တွင်|၌|သို့|က))|(?<![\u1000-\u103F])(?<Left>ပျော်ရွှင်|အောင်မြင်|တိုးတက်|ပြောင်းလဲ|ဆောင်ရွက်|ပူးပေါင်း|လှုပ်ရှား|အသတ်ခံရ|ထိခိုက်|ဆုံးရှုံး|ဝေဖန်|စမ်းသပ်|နှိပ်စက်|ထောက်ပံ့|ကူညီ|စောင့်ရှောက်)[ \t]+(?<Right>မှု)(?=တွေ|များ|ကို|က|သည်|ဟာ|နှင့်|နှင့်|နဲ့|ကြောင့်|ကြောင့်|[\s\u104A\u104B]|$)|(?<![\u1000-\u103F])(?<Left>သူ့|ငါ့|ကိုယ့်|ကိုယ့်|ကျွန်တော့်|ကျွန်တော့်)[ \t]+(?<Right>ကို|အား|ရဲ့|၏|အကြောင်းကို)(?=[\s\u104A\u104B]|$)|(?<Left>အနား)[ \t]+(?<Right>ယူ(?=အပန်းဖြေ|ချင်|နိုင်|ပါ|သည်|တယ်))|(?<Left>အဆင်)[ \t]+(?<Right>ပြေ(?=သလို|ပါ|တယ်|သည်|အောင်))|(?<Left>မတင်)[ \t]+(?<Right>မီ)(?![\u102B-\u103E\u103A])|(?<Left>မယှဉ်သာ)[ \t]+(?<Right>လောက်အောင်)|(?<Left>သဘောကျလဲ)[ \t]+(?<Right>ဆိုတာ)|(?<Left>ဝေးရာသွား)[ \t]+(?<Right>နေသလို)|(?<Left>ပင်တို့)[ \t]+(?<Right>သည်လည်းကောင်း)|(?<Left>ပီတိသု)[ \t]+(?<Right>ခဈာန်)|(?<Left>သီလ)[ \t]+(?<Right>စောင့်ထိန်း|ရှိသော)|(?<Left>ပြန်လည်)[ \t]+(?<Right>ပြုပြင်တာ)|(?<Left>ပြင်ဆင်|ဖလှယ်|ဆောင်ရွက်|လုပ်ဆောင်|စားသောက်|သွားလာ)[ \t]+(?<Right>ကြ(?=တယ်|သည်|မယ်|မည်|ပါ))|(?<Left>လက်ခံ|နားလည်|ဖြေရှင်း|ပြောဆို)[ \t]+(?<Right>တတ်ဖို့)|(?<Left>(?:မျှော်ငေး|စောင့်ဆိုင်း|ထိုင်|ရပ်|နေထိုင်)နေ)[ \t]+(?<Right>သော)|(?<Left>ပြီးသွား)[ \t]+(?<Right>ရင်)|(?<Left>ဆိုက်)[ \t]+(?<Right>လာသော)|(?<Left>လုပ်ဆောင်)[ \t]+(?<Right>သည့်|သည့်)|(?<Left>သိပ်)[ \t]+(?<Right>ကောင်း(?=တယ်|သည်|ပါ))|(?<Left>နေ)[ \t]+(?<Right>ထိုင်(?=ဖို့|ရန်|ရာ|ကြ|သည်|တယ်|ပါ|မှု|ခြင်း))|(?<Left>ကျေးဇူး)[ \t]+(?<Right>တင်(?=ပါ|သည်|တယ်|ရှိ))|(?<Left>ကျင်း)[ \t]+(?<Right>ပ(?=ကြ|သည်|တယ်|ပါ|မည်|မယ်|ခဲ့))|(?<Left>ရောင်း)[ \t]+(?<Right>ချ(?=ကြ|သည်|တယ်|ပါ|မည်|မယ်|ခဲ့))|(?<Left>အရေး)[ \t]+(?<Right>ယူ(?=သည့်|သည့်|သော|ရန်|ဖို့|ခဲ့|သည်|တယ်))|(?<Left>ရင်)[ \t]+(?<Right>ဆိုင်(?=နေ|ရ|ဖို့|ရန်))|(?<Left>ရုံး)[ \t]+(?<Right>ထုတ်(?=စစ်ဆေး))|(?<Left>အချိန်)[ \t]+(?<Right>ကုန်(?=သက်သာ))|(?<![\u1000-\u103F])(?<Left>ဒီ)[ \t]+(?<Right>နှစ်(?=အတွက်|မှာ|တွင်))|(?<Left>သတင်း)[ \t]+(?<Right>အချက်အလက်)|(?<Left>နေရောင်ခြည်|လေ)[ \t]+(?<Right>စွမ်းအင်)|(?<Left>နေ့စဉ်)[ \t]+(?<Right>ဘဝ)|(?<Left>သိမ်းဆည်း)[ \t]+(?<Right>နည်း)|(?<Left>ကူညီ)[ \t]+(?<Right>ပေးခြင်း)|(?<Left>ဂီတ)[ \t]+(?<Right>သံစဉ်)|(?<Left>အသား)[ \t]+(?<Right>ဟင်းလျာ)|(?<Left>ပျော်ရွှင်)[ \t]+(?<Right>စရာ)|(?<Left>အကျိုး)[ \t]+(?<Right>ခံစားခွင့်|ခံစားခွင့်)|(?<Left>စနစ်)[ \t]+(?<Right>တကျ)|(?<=အစု)(?<Left>ရှယ်)[ \t]+(?<Right>ယာ)|(?<=ကာကွယ်ရေး)(?<Left>တပ်ဖွဲ့)[ \t]+(?<Right>များ)|(?<Left>နံဘေး)[ \t]+(?<Right>တွင်)|(?<Left>မြို့)[ \t]+(?<Right>တွင်းသွား)|(?<!အ)(?<Left>ရှေ့)[ \t]+(?<Right>တွင်|မှောက်သို့)|(?<Left>အရွက်)[ \t]+(?<Right>ရိုးတံကြီး)|(?<Left>လက်မှတ်ဝယ်)[ \t]+(?<Right>ရန်))",
            RegexOptions.Compiled);

        private static readonly Regex BoundStemBrokenCompoundPattern = new Regex(
            @"(?:(?<Left>အထိမ်း)[ \t]+(?<Right>အမှတ်)|(?<Left>အစည်း)[ \t]+(?<Right>အဝေး)|(?<Left>အခမ်း)[ \t]+(?<Right>အနား)|(?<Left>အစီ)[ \t]+(?<Right>အစဉ်)|(?<Left>အဆောက်)[ \t]+(?<Right>အအုံ)|(?<Left>အချက်)[ \t]+(?<Right>အလက်)|(?<Left>အကူ)[ \t]+(?<Right>အညီ)|(?<Left>အရည်)[ \t]+(?<Right>အချင်း)|(?<Left>အခက်)[ \t]+(?<Right>အခဲ)|(?<Left>အထောက်)[ \t]+(?<Right>အကူ)|(?<Left>အလွယ်)[ \t]+(?<Right>တကူ)|(?<Left>အစာ)[ \t]+(?<Right>အိမ်)|(?<Left>အသုံး)[ \t]+(?<Right>ဝင်)|(?<Left>အရေး)[ \t]+(?<Right>ကြီး)|(?<Left>ပတ်)[ \t]+(?<Right>ဝန်းကျင်)|(?<Left>စိတ်)[ \t]+(?<Right>နှလုံး)|(?<Left>မူးယစ်)[ \t]+(?<Right>ဆေးဝါး)|(?<Left>မင်္ဂလာ)[ \t]+(?<Right>ဆောင်)|(?<Left>ဆင်းရဲ)[ \t]+(?<Right>ဒုက္ခ)|(?<Left>ကျောင်း)[ \t]+(?<Right>တိုက်)|(?<Left>ကမ်း)[ \t]+(?<Right>ခြေ)|(?<Left>သဲသောင်)[ \t]+(?<Right>ပြင်)|(?<Left>စိတ်ဖိ)[ \t]+(?<Right>စီး)|(?<Left>ပေါင်)[ \t]+(?<Right>မုန့်|မုန့်)|(?<Left>နံနက်|မနက်|ညနေ|နေ့လယ်)[ \t]+(?<Right>စာ)|(?<Left>နည်း)[ \t]+(?<Right>လမ်း)|(?<Left>သမိုင်း)[ \t]+(?<Right>ကြောင်း)|(?<Left>ချက်)[ \t]+(?<Right>ပြုတ်)|(?<Left>နား)[ \t]+(?<Right>ထောင်)|(?<Left>ထိန်း)[ \t]+(?<Right>ချုပ်)|(?<Left>လက်ဖက်)[ \t]+(?<Right>သုပ်)|(?<Left>ပင်လယ်)[ \t]+(?<Right>စာ)|(?<Left>စိတ်ရှည်)[ \t]+(?<Right>သည်းခံ)|(?<Left>စာရွက်)[ \t]+(?<Right>စာတမ်း)|(?<Left>စည်း)[ \t]+(?<Right>ကမ်း)|(?<Left>လုပ်ငန်း)[ \t]+(?<Right>စဉ်)|(?<Left>တရား)[ \t]+(?<Right>စခန်း)|(?<Left>ထမင်း)[ \t]+(?<Right>ကြော်)|(?<Left>ပျော်ပျော်)[ \t]+(?<Right>ပါးပါး)|(?<Left>ခပ်လှမ်း)[ \t]+(?<Right>လှမ်း)|(?<Left>သန့်|သန့်)[ \t]+(?<Right>ရှင်း)|(?<Left>ကိုယ်စား)[ \t]+(?<Right>လှယ်)|(?<Left>တက္က)[ \t]+(?<Right>သိုလ်)|(?<Left>ကွန်ပျူ)[ \t]+(?<Right>တာ)|(?<Left>ပလတ်)[ \t]+(?<Right>စတစ်)|(?<Left>မြင့်)[ \t]+(?<Right>မိုရ်)|(?<Left>ကျန်းမာရေး)[ \t]+(?<Right>အာမခံ)|(?<Left>လူမှု)[ \t]+(?<Right>ဖူလုံရေး)|(?<Left>စာရင်း)[ \t]+(?<Right>စစ်ချုပ်)|(?<Left>အစီရင်)[ \t]+(?<Right>ခံစာ)|(?<Left>ကုန်ထုတ်)[ \t]+(?<Right>လုပ်မှု)|(?<Left>ယဉ်ကျေးမှု)[ \t]+(?<Right>အထောက်အထား)|(?<Left>ငွေပေးချေမှု)[ \t]+(?<Right>စနစ်)(?!တွေ)|(?<Left>မြန်မာ့)[ \t]+(?<Right>ရိုးရာ(?=[\u1000-\u1021]))|(?<Left>သီချင်း)[ \t]+(?<Right>အများကြီး)|(?<Left>ကျောင်းတော်)[ \t]+(?<Right>လိုက်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        private static readonly (string Trigger, Regex Pattern, string Target, string Cue)[] CuratedAddressAndHistoricalBrokenCompounds =
        {
            ("ပုဇွန်", new Regex(@"အောက်ပုဇွန်[ \t]+တောင်(?=လမ်း)", RegexOptions.Compiled), "အောက်ပုဇွန်တောင်", "ရန်ကုန်မြို့ ပုဇွန်တောင်မြို့နယ်ရှိ အထင်ကရလမ်းမအမည် (~လမ်း)"),
            ("ချမ်း", new Regex(@"ပြည်လုံးချမ်း[ \t]+သာ(?![\u1000-\u103F])", RegexOptions.Compiled), "ပြည်လုံးချမ်းသာ", "ရပ်ကွက်/အကွက်/စေတီအမည် (ပြည်လုံးချမ်းသာ)"),
            ("ကျောက်", new Regex(@"ကျောက်[ \t]+စိမ်းလမ်း(?![\u102B-\u103E])", RegexOptions.Compiled), "ကျောက်စိမ်းလမ်း", "လမ်းအမည် (ကျောက်စိမ်း + လမ်း)"),
            ("တောမြို့", new Regex(@"(?<![\u1000-\u103F])သဲ[ \t]+တောမြို့(?![\u102B-\u103E])", RegexOptions.Compiled), "သဲတောမြို့", "မန္တလေးတိုင်းဒေသကြီး ဝမ်းတွင်းမြို့နယ်ရှိ ရုံးသုံးမြို့အမည်"),
            ("ဓိရာဇ်", new Regex(@"ရာဇာ?[ \t]+ဓိရာဇ်", RegexOptions.Compiled), "ရာဇဓိရာဇ်", "ဟံသာဝတီဘုရင် ရာဇဓိရာဇ် (သတ်ပုံကျမ်းစံနှုန်း)"),
            ("ဒဂုန်", new Regex(@"ဒဂုန်[ \t]+ရွှေမြှား", RegexOptions.Compiled), "ဒဂုန်ရွှေမျှား", "စာရေးဆရာကြီး ဒဂုန်ရွှေမျှား (~လမ်း)"),
            ("ရန်အောင်", new Regex(@"ရန်အောင်[ \t]+မြင်(?=ရပ်)", RegexOptions.Compiled), "ရန်အောင်မြင်", "ရပ်ကွက်အမည် (ရန်အောင်မြင်ရပ်ကွက်)"),
            ("မင်းရဲ", new Regex(@"မင်းရဲကျော်[ \t]+စွာ(?=လမ်း|ရပ်)", RegexOptions.Compiled), "မင်းရဲကျော်စွာ", "သမိုင်းဝင်မင်းသား/လမ်းအမည် (မင်းရဲကျော်စွာ)"),
            ("အပ်သား", new Regex(@"ကွမ်[ \t]+အပ်သားမင်းသား", RegexOptions.Compiled), "ကွမ်အပ်သားမင်းသား", "ဒဂုံမြို့သစ်(ဆိပ်ကမ်း)မြို့နယ်ရှိ လမ်းမကြီးအမည်"),
            ("မ္ဘောဇ", new Regex(@"(?<![\u1000-\u103F])က[ \t]+မ္ဘောဇ", RegexOptions.Compiled), "ကမ္ဘောဇ", "ကမ္ဘောဇဒေသ/လမ်းအမည် (ကမ္ဘောဇ)"),
            ("အနော်", new Regex(@"အနော်[ \t]+ရထာ", RegexOptions.Compiled), "အနော်ရထာ", "ပုဂံခေတ် အနော်ရထာမင်းကြီး / အနော်ရထာလမ်း"),
            ("မောင်", new Regex(@"(?<=ညီ[ \t]*အစ်[ \t]*ကို[ \t]*)မောင်[ \t]+မ(?=\s|တွေ|များ|$)", RegexOptions.Compiled), "မောင်နှမ", "ဆွေမျိုးသားချင်း ပေါင်းစပ်ပုဒ် (ညီအစ်ကိုမောင်နှမ)")
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
            "စာရွက်စာတမ်း", "စည်းကမ်း", "လုပ်ငန်းစဉ်", "တရားစခန်း", "ထမင်းကြော်", "ပျော်ပျော်ပါးပါး", "ခပ်လှမ်းလှမ်း", "သန့်ရှင်း",
            "ကျန်းမာရေးအာမခံ", "လူမှုဖူလုံရေး", "စာရင်းစစ်ချုပ်", "အစီရင်ခံစာ", "ကုန်ထုတ်လုပ်မှု",
            "ယဉ်ကျေးမှုအထောက်အထား", "ငွေပေးချေမှုစနစ်", "မြန်မာ့ရိုးရာ", "သီချင်းအများကြီး", "ကျောင်းတော်လိုက်"
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
                new Regex(@"ပါတယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?.]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", RegexOptions.Compiled),
                "ပါသည်",
                "ပစ္စုပ္ပန်/အတိတ် ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'ပါတယ်' အစား 'ပါသည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "တယ်",
                new Regex(@"(?<=[\u1000-\u103F])(?<!တင့်|ဟို|မို|ပေါ်|ဒစ်ဂျစ်|ဒီဂျစ်|ခရစ်စ|ဟော်|ကယ်)တယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?.]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", RegexOptions.Compiled),
                "သည်",
                "ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'တယ်' အစား 'သည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "ပါမယ်",
                new Regex(@"ပါမယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?.]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", RegexOptions.Compiled),
                "ပါမည်",
                "အနာဂတ် ကြိယာဝိဘတ် (ရုံးသုံး)",
                "ရုံးသုံးစာအရေးအသားတွင် အပြောစကား 'ပါမယ်' အစား 'ပါမည်' ကို သုံးနှုန်းရမည်။"
            ),
            (
                "မယ်",
                new Regex(@"(?<=[\u1000-\u103F])(?<!အ|နှမ|မောင်|ပျို|လုံ|ရှမ်း|ဇင်း)မယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?.]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", RegexOptions.Compiled),
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
            @"(?:^|[\u104A\u104B\r\n])\s*(?![^\u104A\u104B\r\n]*?[\u1000-\u103F]က\s+)(?<Host>[\u1000-\u103F\s]{2,42}?)(?<!ဘယ်သူ|မည်သူ|ဘယ်|မည်သည့်|လာ|ရောက်|တွေ့|မြင်|သိ|ဖိတ်|ခေါ်|ပြီး|စီး|တန်)(?<Particle>မှ)(?!ာ)\s*(?:လှိုက်လှဲစွာ\s*|နွေးထွေးစွာ\s*|ဝမ်းမြောက်စွာ\s*|ဂုဏ်ယူစွာ\s*|အထူး\s*)?(?<Verb>ကြိုဆို)");

        private static readonly Regex AgentiveMhaSubjectPattern = new Regex(
            @"(?:^|[\u104A\u104B\r\n])\s*(?![^\u104A\u104B\r\n]*?[\u1000-\u103F]က\s+)(?:[\u1000-\u103F\s]{0,28}?)?(?:ကျွန်တော်|ကျွန်မ|ကျွန်ုပ်|ကျွန်တော်တို့|ကျွန်ုပ်တို့|သက်ဆိုင်ရာ|တာဝန်ရှိသူ(?:များ)?|ဝန်ကြီးဌာန|ဦးစီးဌာန|ကော်မတီ|အဖွဲ့ချုပ်|အဖွဲ့အစည်း|ကုမ္ပဏီ|တရားခံ|တရားလို|ဥက္ကဋ္ဌ|အတွင်းရေးမှူး|ကျောင်းအုပ်(?:ကြီး)?|ဆရာ(?:ကြီး|မ)?)(?<Particle>မှ)(?!ာ)\s*(?:အမှာစကား\s*|အသိပေးချက်\s*|ကြေညာချက်\s*|အစီရင်ခံစာ\s*|ဆက်လက်\s*|ပြန်လည်\s*|အမြန်ဆုံး\s*)?(?<Verb>တင်ပြ|ပြောကြား|ရှင်းလင်း|လျှောက်ထား|အသိပေး|အကြံပြု|ထုတ်ပြန်(?!ထားသည့်|ထားသော|သည့်|သော)|ကြေညာ|မိန့်ကြား|မှာကြား|ဖြေကြား|ဝန်ခံ|ထွက်ဆို|အရေးယူပေး|ဖြေရှင်းပေး|စိစစ်ပေး|ကွင်းဆင်းစစ်ဆေး)");

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

        // 4n. Smart space-separated multi-syllable duplicate words (e.g. 'ဝန်ကြီးဌာန ဝန်ကြီးဌာန -> ဝန်ကြီးဌာန', 'တိုင် တိုင်ကို -> တိုင်ကို') & English function words ('the the -> the')
        private static readonly Regex DuplicatedMultiSyllableMyanmarWordPattern = new Regex(
            @"(?<![\u1000-\u104F])(?<Word>[\u1000-\u103F]{4,36})(?<Dup>[ \t]+\k<Word>)(?![\u1000-\u104F])",
            RegexOptions.Compiled);

        private static readonly Regex DuplicatedMyanmarWordWithCasePattern = new Regex(
            @"(?<![\u1000-\u104F])(?<Word>[\u1000-\u103F]{4,30})[ \t]+\k<Word>(?<Case>ကို|တွင်|၌|မှာ|သို့|မှ|အား|၏|နှင့်|နှင့်|နဲ့)(?![\u1000-\u104F])",
            RegexOptions.Compiled);

        private static readonly Regex DuplicatedEnglishFunctionWordPattern = new Regex(
            @"(?<![A-Za-z0-9'-])(?<Word>the|and|in|of|to|for|is|are|on|at|with|by|from|as|an)(?<Dup>[ \t]+\k<Word>)(?![A-Za-z0-9'-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 7c. Accidental horizontal space immediately before Myanmar punctuation '၊' / '။' (with Pali Nissaya gloss exemption)
        private static readonly Regex AccidentalSpaceBeforeMyanmarPunctPattern = new Regex(
            @"(?<=[\u1000-\u103F\u1040-\u1049\)\]\u201D])(?<Target>[ \t]+(?<Punct>[\u104A\u104B]))",
            RegexOptions.Compiled);

        // 7c-ii. Spaced stray ASCII period '.' between two Myanmar words ('မြန်မာနိုင်ငံ . တရားဥပဒေ')
        private static readonly Regex SpacedMidClauseAsciiDotPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<Target>[ \t]+\.[ \t]+)(?=[\u1000-\u103F])",
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
            @"(?:^|[\u104A\u104B]\s*)(?:[\u1000-\u103F\s]{0,18}?)?(?<Subj>ဆရာတော်(?:ကြီး|ဘုရား)?|သံဃာတော်(?:အရှင်သူမြတ်)?|ရဟန်းတော်|ဘုန်းတော်ကြီး)(?:များ|တို့)?(?:\s*" + MyanmarNumeralCore + @"\s*(?:ပါး|ဦး|ယောက်))?(?:သည်|က)\s+(?![^\u104A\u104B\r\n]{0,28}?(?:ဒကာ|ဒါယကာ|ဒါယိကာမ|လူ|ကလေး|ကျောင်းသား|ဧည့်သည်|ပြည်သူ|ရွာသား))[^\u104A\u104B\r\n]{0,28}?(?<Target>ထမင်းစား|သေဆုံး|ကွယ်လွန်|အိပ်စက်|အိပ်ပျော်|နေထိုင်)(?=(?:တော်မူ|ကြ|ခဲ့|နေ|ပါ|သည်|တယ်|ပြီ|ပြီး|၍|ကာ|လျက်|သွား|\s*[\u104B]))");

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
            @"(?:[\(（][ \t]*(?:နိုင်|ဧည့်|ဧည့်|ပြု|ပြူ|သာသနာ့?|သာ|သ|ယာယီ|စ|န|[NEPTYS])[ \t]*[\)）][ \t]*[-–/\\]?[ \t]*|[-–/\\][ \t]*(?:နိုင်|ဧည့်|ဧည့်|ပြု|ပြူ|သာသနာ့?|သာ|သ|ယာယီ|စ|န|[NEPTYS])[ \t]*[-–/\\][ \t]*)(?<Target>(?=[\u1040-\u1049\u101D]*\u101D)(?=[\u1040-\u1049\u101D]*[\u1040-\u1049])[\u1040-\u1049\u101D]{5,7})(?![\u1000-\u10490-9])");

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

                int targetIndex = g.Index;
                string targetRaw = g.Value;
                if (trie != null && !hasDottedCircle && targetRaw.Length > 12)
                {
                    NarrowZeroWidthSpanToEnclosingWord(targetRaw, trie, brokenSyllableOnly, out int relOffset, out string narrowedRaw);
                    targetIndex += relOffset;
                    targetRaw = narrowedRaw;
                }

                string stripped = StripZeroWidthRegex.Replace(targetRaw, string.Empty);
                string cleaned = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(stripped));
                AddGrammarOrSpellingIssue(
                    found, targetIndex, targetRaw.Length, targetRaw, cleaned,
                    ProofingIssueType.LookalikeCharacterError,
                    $"စာလုံးအတွင်း မမြင်ရသော ခြားနားသင်္ကေတ (Zero-Width Space) ဝင်နေပါသည် — ရှာဖွေမှု (Ctrl+F) မပျက်စေရန် '{cleaned}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                    "ယူနီကုဒ် စာလုံးပေါင်းအမှန်", "WORD", true);
            }
            return found;
        }

        private static bool IsZeroWidthFormatChar(char c)
        {
            return c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF';
        }

        private static void NarrowZeroWidthSpanToEnclosingWord(
            string rawRun,
            MyanmarTrie trie,
            bool brokenSyllableOnly,
            out int relOffset,
            out string narrowedRaw)
        {
            relOffset = 0;
            narrowedRaw = rawRun;

            var sbStripped = new System.Text.StringBuilder(rawRun.Length);
            var strippedToRawStart = new List<int>(rawRun.Length);
            var strippedToRawEnd = new List<int>(rawRun.Length);

            for (int r = 0; r < rawRun.Length; r++)
            {
                char ch = rawRun[r];
                if (IsZeroWidthFormatChar(ch))
                    continue;
                sbStripped.Append(ch);
                strippedToRawStart.Add(r);
                strippedToRawEnd.Add(r + 1);
            }

            string stripped = sbStripped.ToString();
            if (stripped.Length == 0)
                return;

            var syls = MyanmarSyllableBreaker.Tokenize(stripped);
            var mmSyls = new List<SyllableToken>(syls.Count);
            for (int i = 0; i < syls.Count; i++)
            {
                if (syls[i].Kind == TokenKind.MyanmarSyllable)
                    mmSyls.Add(syls[i]);
                else
                    return;
            }

            int mCount = mmSyls.Count;
            if (mCount < 4)
                return;

            var sylRawStart = new int[mCount];
            var sylRawEnd = new int[mCount];
            for (int s = 0; s < mCount; s++)
            {
                int st = mmSyls[s].StartIndex;
                int en = st + mmSyls[s].Length - 1;
                sylRawStart[s] = s == 0 ? 0 : strippedToRawStart[st];
                sylRawEnd[s] = s == mCount - 1 ? rawRun.Length : strippedToRawEnd[en];
            }

            int firstDirtySyl = mCount;
            int lastDirtySyl = -1;
            for (int s = 0; s < mCount; s++)
            {
                bool hasInternalZw = (sylRawEnd[s] - sylRawStart[s]) > mmSyls[s].Length;
                if (hasInternalZw)
                {
                    if (s < firstDirtySyl) firstDirtySyl = s;
                    if (s > lastDirtySyl) lastDirtySyl = s;
                }
                if (s + 1 < mCount && sylRawStart[s + 1] > sylRawEnd[s])
                {
                    if (s < firstDirtySyl) firstDirtySyl = s;
                    if (s + 1 > lastDirtySyl) lastDirtySyl = s + 1;
                }
            }

            if (lastDirtySyl < 0 || firstDirtySyl > lastDirtySyl)
                return;

            // Segment stripped syllables greedily into exact lexicon words
            var segStarts = new List<int>(mCount);
            var segEnds = new List<int>(mCount);
            int pos = 0;
            while (pos < mCount)
            {
                int bestLen = 1;
                int maxTry = Math.Min(8, mCount - pos);
                for (int len = maxTry; len >= 2; len--)
                {
                    var sbW = new System.Text.StringBuilder(len * 4);
                    for (int k = 0; k < len; k++)
                        sbW.Append(mmSyls[pos + k].NormalizedText);
                    if (trie.ContainsExactWord(sbW.ToString()))
                    {
                        bestLen = len;
                        break;
                    }
                }
                segStarts.Add(pos);
                segEnds.Add(pos + bestLen - 1);
                pos += bestLen;
            }

            int segA = 0;
            int segB = segStarts.Count - 1;
            for (int i = 0; i < segStarts.Count; i++)
            {
                if (segStarts[i] <= firstDirtySyl && firstDirtySyl <= segEnds[i])
                    segA = i;
                if (segStarts[i] <= lastDirtySyl && lastDirtySyl <= segEnds[i])
                    segB = i;
            }

            int trimStartSyl = segStarts[segA];
            int trimEndSyl = segEnds[segB];
            if (trimStartSyl == 0 && trimEndSyl == mCount - 1)
                return;

            int candRawStart = sylRawStart[trimStartSyl];
            int candRawEnd = sylRawEnd[trimEndSyl];

            // Ensure no zero-width characters exist outside [candRawStart .. candRawEnd)
            for (int r = 0; r < candRawStart; r++)
            {
                if (IsZeroWidthFormatChar(rawRun[r]))
                    return;
            }
            for (int r = candRawEnd; r < rawRun.Length; r++)
            {
                if (IsZeroWidthFormatChar(rawRun[r]))
                    return;
            }

            string candSub = rawRun.Substring(candRawStart, candRawEnd - candRawStart);
            bool stillMidOrBroken = Typography.MyanmarZwspToolkit.IsMidWordOrBrokenWordZwsp(candSub, trie, out bool stillBrokenSyl);
            if (brokenSyllableOnly ? !stillBrokenSyl : !stillMidOrBroken)
                return;

            relOffset = candRawStart;
            narrowedRaw = candSub;
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

    }
}
