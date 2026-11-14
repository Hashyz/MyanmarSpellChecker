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
        // 1a. Compound 'တ...တ...' numeral phrases where both 'တ' syllables require 'တစ်' per 462-page သတ်ပုံကျမ်း (တစ်စုံတစ်ရာ, တစ်ခါတစ်ရံ, တစ်ရံတစ်ခါ, တစ်စိတ်တစ်ပိုင်း, etc.)
        private static readonly Regex TaCompoundNumeralPattern = new Regex(
            @"(?<![\u1039])(?:တ(?<First>စုံ)တ(?<Second>ရာ|ခု|ဦး)|တ(?<First>ခါ)တ(?<Second>ရံ)|တ(?<First>ရံ)တ(?<Second>ဆစ်|ဖန်|ခါ)|တ(?<First>စိတ်)တ(?<Second>ပိုင်း)|တ(?<First>ပိုင်း)တ(?<Second>စ)|တ(?<First>ချိန်)တ(?<Second>ခါ)|တ(?<First>နည်း)တ(?<Second>ဖုံ|လမ်း)|တ(?<First>ဦး)တ(?<Second>ယောက်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        // 1b. 'တ' + Numeral Classifier where 'တစ်' is required by မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (အမှာစာ စာ-၇..၈ နှင့် တစ်ဆက်တည်း/တစ်သမတ်တည်း)
        private static readonly Regex TaNumeralClassifierPattern = new Regex(
            @"(?<!ရုတ်|သမ္မ|ပဏ္ဍိ|ပဏ္ဏိ|ဇီဝိ|ပရဟိ|လောပိ|မုဒိ|သုခိ|ရွှေ(?=တချောင်း)|ကျောက်\s*(?=တလုံး)|ရာဇပ(?=တလမ်း)|မျှ|ဘူ|ဇာ|ဒူ|ဝါ|ဂီ|\u101E\u102F|အနန္|စနစ်|သာကေ|ဟင်္သာ|[\u1039])တ(?<Classifier>ခု|ဦး|ယောက်|ဖက်|ကြိမ်|ခါ|ဖန်|ရက်|လ(?!ဘောင်|ပေါင်|မိုင်း|ပိုင်း)|နှစ်|ပတ်|နေ့|ညလုံး|နေကုန်|နာရီ|မိနစ်|စက္ကန့်|အောင့်|စောင်|တွဲ|ဖွဲ့|စု(?!တစည်း)|ချက်|ချိန်|နေရာ|နိုင်ငံလုံး|ပြည်လုံး|သက်လုံး|လျှောက်လုံး|ခဏ|ဆင့်|ပိုင်း|မျိုး|စုံ|ထပ်|လမ်း|မြို့နယ်|ခရိုင်|တိုင်း|ပြိုင်နက်|ပြိုင်တည်း|ဆက်တည်း|သမတ်တည်း|ယောက်တည်း|ဦးချင်း|ဖက်သတ်|ကောင်|စီး|ချောင်း|လုံး|ပြား|ရွက်|အုပ်(?!ချုပ်|ထိန်း|စိုး)|ထည်|ပွဲ|ခန်း(?!မ)|ဆောင်(?!ရွက်)|သံတည်း|သဘောတည်း|ပါတည်း|ပေါင်းတည်း|ထောက်(?!ပံ့|ကူ|ခံ|လှမ်း)|လှည့်|ဝက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])",
            RegexOptions.Compiled);

        // Hypercorrected 'တစ်' before non-numeral adverbs/pronouns where 'တ' must be used (အမှာစာ စာ-၈ အာဂုံပစ္စည်း 'တ'; note: တစ်ဆက်တည်း, တစ်သမတ်တည်း & တစ်သီးတစ်ခြား officially use 'တစ်' in the 462p book!)
        private static readonly Regex HypercorrectedTaitPattern = new Regex(
            @"(?<!တစ်သီး)တစ်(?<Redup>ကယ်|ဖြည်းဖြည်း|ဖြူးဖြူး|ကျက်ကျက်|ရိပ်ရိပ်|စောင်း|လွဲ|စိုက်မတ်မတ်|ညီတညွတ်တည်း|ချို့|ခြား|မင်|ဒင်္ဂ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
            RegexOptions.Compiled);

        // Hypercorrected infix/compound 'တစ်' on official Preface Page 8 'တ' compounds (အမှတ်တစ်ရ -> အမှတ်တရ, တစ်ခမ်းတစ်နား -> တခမ်းတနား, အစဉ်တစ်စိုက် -> အစဉ်တစိုက်, တစ်ချို့တစ်လေ -> တချို့တလေ, etc.)
        private static readonly Regex HypercorrectedCompoundTaitPattern = new Regex(
            @"(?<Target>အမှတ်တစ်ရ|တစ်ခမ်းတစ်နား|တစ်အံ့တစ်ဩ|အရောတစ်ဝင်|မဟုတ်တစ်ရုတ်|မခို့တစ်ရို့|အစဉ်တစ်စိုက်|တစ်ချို့တစ်လေ)(?![\u102B-\u103E])",
            RegexOptions.Compiled);

        // Official Khwe-Htar Page 337 productive ideophone suffix rule: 'ကနဲ(ဝင်း...၊ ထိန်...) → ခနဲ(ဝင်း...၊ ထိန်...)'
        private static readonly Regex MisspelledIdeophoneKanaePattern = new Regex(
            @"(?<Stem>ဝင်း|ထိန်|လက်|ဖျတ်|ဗြုန်း|ရုတ်|မိုက်|အေး|ပူ|စိမ့်|ထိတ်|ဒိတ်|လှစ်|ရိပ်|ဖြတ်|ဆတ်|ငိုက်|ညွတ်|ပြတ်|ဝုန်း|ဒိုင်း|ဂျိမ်း|ဖြန်း|ဖြုတ်|ဖြုန်း|ဖုတ်|ဘောက်|မြိုက်|ခြိုက်|စွေ့|စွတ်|ပြိုက်|လွှတ်|ဝါး|ရွှတ်|ဟုပ်|ဟုန်း|ရှိန်း|ဖိန်း|ဒက်|ဆိုး)(?<Target>ကနဲ)(?![\u102B-\u103E])",
            RegexOptions.Compiled);

        // 2. 'ခုနစ်' (Number 7, no ha-hto) vs 'ခုနှစ်' (Year, with ha-hto; guard against year digits e.g. '၁၉၄၁ ခုနှစ် သန်းခေါင်စာရင်း')
        private static readonly Regex MisusedKhuNhitNumberPattern = new Regex(
            @"(?<![၀-၉0-9]\s*)(?<Target>ခုနှစ်)(?=(?:ယောက်|ဦး|ခု|ရက်|လ|ကြိမ်|ဖွဲ့|စောင်|ထပ်|မိုင်|နာရီ|မိနစ်|ရာ|ထောင်|သောင်း|သိန်း|သန်း(?!ခေါင်)|စီး|လုံး|ချက်|မျိုး|ဆင့်|ပိုင်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A))",
            RegexOptions.Compiled);

        private static readonly Regex MisusedKhuNitYearPattern = new Regex(
            @"(?:၁[၀-၉]{3}|၂၀[၀-၉]{2}|19[0-9]{2}|20[0-9]{2})\s*(?<Target>ခုနစ်)(?![\u102B-\u103E])",
            RegexOptions.Compiled);

        // 3. Generalized Negative Verb 'မ...' + 'ပဲ' -> 'ဘဲ' (works for ANY negative verb in Myanmar while excluding nouns like မနက်/မနေ့ and nominalized '...တာပဲ')
        private static readonly Regex NegativeVerbPePattern = new Regex(
            @"(?<![\u1000-\u103F])မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])(?!မပဲ|ပဲ|ဟာ|နက်|နေ့|ကွေး|လေးရှား|ရမ်း)(?<VerbBody>[\u1000-\u103F]{1,10}?)(?<!တာ|မှာ|ရင်|လို့|ခင်|က|ကို|မှ)(?<Target>ပဲ)(?=\s|$|[\u104A\u104B]|နှင့်|နဲ့)",
            RegexOptions.Compiled);

        // 4a. Clause-level Interrogative 'လည်း' -> 'လဲ' (require non-Myanmar left boundary, exclude reduplicated indefinites 'ဘာပြောပြော/မဆို' & 'ဘယ်ဘက်', and anchor to question/clause end!)
        private static readonly Regex InterrogativeLaePattern = new Regex(
            @"(?<![\u1000-\u103F])(?:ဘာ(?!သာ|ပြောပြော|လုပ်လုပ်|ဖြစ်ဖြစ်|ဆိုဆို|မဆို)|ဘယ်(?!ဘက်|ညာ|လ်|ဂျီ|လာ|လီ|လိုပဲ|သူမဆို|အရာမဆို)|အဘယ်ကြောင့်|အဘယ်|မည်သို့|မည်သူ|မည်သည့်)(?![\u1000-\u103F\s]{0,16}?မဆို)[\u1000-\u103F\s]{0,26}?(?<Target>လည်း)(?=\s*(?:[\u104B?]|$|ဆိုတာ|ဆိုသည်|ဟု\s*မေး|လို့\s*မေး|မသိ))",
            RegexOptions.Compiled);

        // 4b. Affirmative 'လဲ' ("also/too") -> 'လည်း' (e.g. 'ကျွန်တော်လဲ သွားမယ်' -> 'ကျွန်တော်လည်း သွားမယ်')
        private static readonly Regex AffirmativeLaeMisusePattern = new Regex(
            @"(?:ကျွန်တော်|ကျွန်မ|သူ|ငါ|မင်း|သင်|ဒါ|အဲဒါ|ထိုသူ|၎င်း|သား|သမီး|အားလုံး|တို့|များ|တွေ)\s*(?<Target>လဲ)(?=\s+[\u1000-\u102A])",
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

        private static readonly Regex MissingVisargaBuPattern = new Regex(
            @"(?<![\u1000-\u103F])မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])[\u1000-\u103F]{1,12}?(?<Target>ဘူ)(?=\s|$|[\u104A\u104B]|နော်|လား|ပေါ့)");

        private static readonly Regex MisspelledNegativePhuPattern = new Regex(
            @"(?<![\u1000-\u103F])မ(?![\u102B-\u103E]|[\u1000-\u1021]\u103B?[\u1039\u103A])[\u1000-\u103F]{0,12}?(?<=ပါ|သေး|တော့|နိုင်|ချင်|ဟုတ်|ရှိ|သိ|သွား|လာ)(?<Target>ဖူး)(?=\s|$|[\u104A\u104B]|နော်|ပေါ့|လား)");

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

        // Sequential 'ပြီး' misspelled as 'ပြီ' before linkers ('ပြီတော့/ပြီရင်/ပြီမှ') or serial verbs ('စားပြီ သွားမယ်')
        private static readonly Regex MisusedSequentialPyeePattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])(?<Target>\u1015\u103C\u102E)(?=(?:တော့|ရင်|လျှင်|မှ|နောက်|သား|စီး|ဆုံး|မြောက်)(?![\u102B-\u103E]))|(?:စား|သောက်|အိပ်|လုပ်|ဖတ်|ရေး|ဝယ်|ချက်|ဆေး|ချိုး)(?<Target2>\u1015\u103C\u102E)(?=\s+(?:သွား|လာ|ထိုင်|အိပ်|ပြန်|နား|ဆက်)))");

        // Missing visarga on clause-final question particle 'လာ' -> 'လား' after completive/negative/tag markers or standalone 'ဟုတ်လာ/မဟုတ်လာ'
        private static readonly Regex MissingVisargaQuestionLaPattern = new Regex(
            @"(?:(?<=ပြီးပြီ|ပါပြီ|ပြီ|ပါဘူး|ဘူး|ရဲ့|ဟုတ်သ|သိသ|ရှိသ|ကောင်းသ|မဟုတ်)|(?<=^ဟုတ်|[\s\u104A\u104B]ဟုတ်))(?<Target>လာ)(?=\s*(?:[\u104B?]|$))");

        private static readonly Regex CasualPeePattern = new Regex(
            @"(?:(?<=[\u1000-\u103F])(?<!ရူ|ကလို|စ)(?<Target>ပီး)(?=\s|$|[\u104A\u104B]|တော့|ပြီ|ရင်|မှ|ပါ|လား|နောက်|သား)|(?<![\u1000-\u103F])(?<Target2>ပီး)(?=တော့|ရင်|နောက်|မှ|ပြီ))");

        private static readonly Regex CasualPhitPattern = new Regex(
            @"(?<![\u1000-\u103F]|အ|မ)(?<Target>ဖစ်)(?=ပါ|သည်|တယ်|မည်|မယ်|နေ|ခဲ့|သွား|လာ|နိုင်|ကြောင်း|၍)");

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
            @"(?:မနက်ဖြန်|သန်ဖြန်|မကြာမီ|မကြာခင်|လာမည့်(?:နှစ်|လ|အပတ်|ရက်)|လာမယ့်(?:နှစ်|လ|အပတ်|ရက်)|နောက်(?:အပတ်|လ|နှစ်)(?:တွင်|မှာ|၌)?)[^\u104A\u104B\r\n]{1,64}?(?<!ရင်|လျှင်)(?<Target>ခဲ့ပါသည်|ခဲ့ပါတယ်|ခဲ့သည်|ခဲ့တယ်)(?=\s*(?:[\u104B]|$|ဟု|လို့|ဆိုတာ|ဆိုပြီး))");

        private static readonly Regex PastAdverbWithFutureVerbPattern = new Regex(
            @"(?:မနေ့က|ယမန်နေ့က|တမြန်နေ့က|ယနေ့နံနက်က|(?:ပြီးခဲ့|လွန်ခဲ့)(?:သော|သည့်|တဲ့)\s*[\u1000-\u103F]{1,14}(?:က|တွင်|၌|မှာ)?)[^\u104A\u104B\r\n]{1,64}?(?<Target>ပါမည်|ပါမယ်|လိမ့်မည်|လိမ့်မယ်|(?<!ရ|နိုင်|လို|ချင်)မည်|(?<!ရ|နိုင်|လို|ချင်)မယ်)(?=\s*(?:[\u104B]|$|ဟု|လို့|ဆိုတာ|ဆိုပြီး))");

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
            // Footwear / paired items -> ရံ
            (
                new[] { "ဖိနပ်", "ခြေအိတ်", "လက်အိတ်", "နားကပ်" },
                new Regex(@"(?:ဖိနပ်|ခြေအိတ်|လက်အိတ်|နားကပ်)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|လုံး|အုပ်|ချပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
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
            // Hand-held weapons, umbrellas & stringed instruments -> လက်
            (
                new[] { "သေနတ်", "ဓား", "လှံ", "ထီး", "ဂီတာ" },
                new Regex(@"(?:သေနတ်|ဓား|လှံ|ထီး|ဂီတာ)\s*(?<Num>" + MyanmarNumeralCore + @")\s*(?<Wrong>ယောက်|ဦး|ကောင်|စီး|ထည်|အုပ်|ရံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)"),
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

        // Colloquial dropped pre-verbal 'မ-' before 'ဟုတ်ဘူး/ဟုတ်ဖူး' or 'သိဘူး/သိဖူး' ('ဟုတ်ဘူး -> မဟုတ်ဘူး', 'သိဘူး -> မသိဘူး')
        private static readonly Regex DroppedMaColloquialNegationPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?<Clause>[^\u104A\u104B\r\n]{0,56}?)(?<![\u1000-\u103F])(?<Target>(?<Verb>ဟုတ်|သိ)(?:ဘူး|ဖူး))(?=\s*(?:[\u104A\u104B?""”'’]|$|နော်|ပေါ့|ကွာ|ဗျာ|ဗျ|ရှင်|ရှင့်|လေ|ဟု|လို့|ဆိုပြီး|တဲ့))");

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

        // 4h. Broken Space-Split Mandatory Compounds (e.g. 'စာ ကြည့်တိုက်' -> 'စာကြည့်တိုက်', 'အစည်း အဝေး' -> 'အစည်းအဝေး', 'အိမ် စာအကြောင်း' -> 'အိမ်စာ')
        private static readonly Regex CuratedBrokenCompoundPattern = new Regex(
            @"(?:(?<![\u1000-\u103F])(?<Left>စာ|ဝန်|လေ|အိမ်|ဘယ်|ဘာ|ကိုယ်)|(?<Left>အစည်း|အခမ်း|အစီ|အခြေ|အတွေ့|အခွင့်|အချက်|အဆောက်|အထိမ်း|ကျောင်း|အလုပ်|မိန်း|ယောက်ျား|သတင်း|မော်တော်|ကွန်ပျူ|တယ်လီ|စာသင်|တက္က|အစိုး|ဥပ|ကျန်းမာ|စီးပွား|လူနာတင်|မီးသတ်|တပ်မ|ကိုယ်စား|ပလတ်))[ \t]+(?<Right>ကြည့်တိုက်|အဝေး|အနား|အစဉ်|အနေ|အကြုံ|အရေး|အလမ်း|အလက်|အအုံ|အမှတ်|သား|သူ|ထမ်း|သမား|ချိန်|ကလေး|လေး|စာ|ယာဉ်|တာ|ဖုန်း|ကျောင်း|သိုလ်|ရ|အဖွဲ့|ဒေ|ရေး|ကား|လို|လို့|တော်|စားလှယ်|လှယ်|စတစ်)(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|နဲ့|သည်|၏|၌|တွင်|က|မှာ|များ|တွေ|အတွက်|အကြောင်း|ဆိုင်ရာ|ဖြင့်|သဖြင့်|ကြောင့်|အပြင်|ရရှိ|ဖြတ်စ)",
            RegexOptions.Compiled);

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
            "အလုပ်သမား", "အလုပ်ချိန်", "မိန်းကလေး", "ယောက်ျားလေး", "သတင်းစာ", "လေယာဉ်", "မော်တော်ယာဉ်",
            "ကွန်ပျူတာ", "တယ်လီဖုန်း", "စာသင်ကျောင်း", "တက္ကသိုလ်", "အစိုးရ", "အစိုးရအဖွဲ့", "ဥပဒေ",
            "ကျန်းမာရေး", "စီးပွားရေး", "လူနာတင်ယာဉ်", "အိမ်စာ", "မီးသတ်ကား", "ဘယ်လို", "ဘာလို့",
            "တပ်မတော်", "ကိုယ်စားလှယ်", "ပလတ်စတစ်"
        );

        private static readonly Regex SpaceSplitPairCandidateRegex = new Regex(
            @"(?<![\u1000-\u103F])(?<Target>(?<Left>[\u1000-\u103F]{1,8})[ \t]+(?<Right>[\u1000-\u103F]{1,8}?))(?:ကို|အား|မှ|သို့|နှင့်|နဲ့|သည်|၏|၌|တွင်|က|မှာ|များ|တွေ|အတွက်|အကြောင်း|ဆိုင်ရာ|ဖြင့်)?(?![\u1000-\u103F])",
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
            )
        };

        // 6. 'အား' vs 'ကို' particle misuse before direct transitive verbs (Formal / Literary style — guards '-အား' compound nouns like ဓာတ်အား/စွမ်းအား/အင်အား/လုပ်အား & exempts double-ကို/အဖြစ်/စေ & state protocol 'အား လက်ခံတွေ့ဆုံ')
        private static readonly Regex AhInsteadOfKoPattern = new Regex(
            @"(?<=[\u1000-\u103F])(?<!ဓာတ်|စွမ်း|အင်|လုပ်|ခွန်|စိတ်|ကိုယ်|ဉာဏ်|အင်္ဂါ|သက်သာ)(?<Particle>အား)(?!ကစား|လုံး|ဖြင့်|လျော်စွာ|ထုတ်|ပေး|ကိုး|နည်း|သာ|ရ)\s*(?<Verb>စစ်ဆေး|ခန့်အပ်|ရွေးချယ်|ကြိုဆို|ဖမ်းဆီး|တည်ဆောက်|ပြုပြင်|ဖျက်သိမ်း|သုံးသပ်|အတည်ပြု|လက်ခံ(?!တွေ့)|ပယ်ဖျက်|ရေးဆွဲ|ထုတ်ပြန်|ကျင်းပ|ဖော်ပြ|ဖွင့်လှစ်|ပိတ်သိမ်း|ရုပ်သိမ်း|လိုက်နာ|ပြဋ္ဌာန်း|ချိုးဖောက်|အကောင်အထည်ဖော်|စွန့်ပစ်|ဖောက်လုပ်|ဆုံးမ(?!ဩဝါဒ)|ဝယ်ယူ|ရောင်းချ|သုံးစွဲ)",
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
            "နှင့်", "တွင်", "၌", "သို့", "အတွက်", "ကြောင့်", "သဖြင့်", "သော်လည်း", "သော", "သည့်", "မည့်", "၍", "၏"
        };

        private static readonly Regex DuplicatedFunctionWordPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Word>နှင့်|တွင်|၌|သို့|အတွက်|ကြောင့်|သဖြင့်|သော်လည်း|သော|သည့်|မည့်|၍|၏)(?<Dup>[ \t]+\k<Word>)(?=\s|$|[\u104A\u104B])");

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
            @"(?:^|[\u104A\u104B]\s*)(?![^\u104A\u104B\r\n]*?[""”“'’])[^\u104A\u104B\r\n]{0,36}?(?<Target>(?<=[\u1000-\u103F])တွေ(?=\s|ကို|က|မှာ)|(?<=[\u1000-\u103F])(?<!ချိ|ယိမ်း|လှုပ်)နဲ့(?=\s)|(?<![\u1000-\u103F])(?:ဒီနေ့|အဲဒီ))(?![^\u104A\u104B\r\n]{0,44}?(?:ဟု|လို့))[^\u104A\u104B\r\n]{1,44}?(?:ပေသည်|ချေသည်|ပါ၏|သတည်း)(?=\s*[\u104B]|$)");

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

        internal static List<ProofingIssue> FindZeroWidthWordCorruptions(string text)
        {
            var found = new List<ProofingIssue>();
            if (string.IsNullOrEmpty(text) || text.IndexOfAny(ZeroWidthOrDottedChars) < 0)
                return found;

            foreach (Match m in ZeroWidthInsideWordPattern.Matches(text))
            {
                Group g = m.Groups["Target"];
                string stripped = StripZeroWidthRegex.Replace(g.Value, string.Empty);
                string cleaned = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(stripped));
                AddGrammarOrSpellingIssue(
                    found, g.Index, g.Length, g.Value, cleaned,
                    ProofingIssueType.LookalikeCharacterError,
                    $"စာလုံးအတွင်း မမြင်ရသော ခြားနားသင်္ကေတ (Zero-Width Space) ဝင်နေပါသည် — '{cleaned}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
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
                            if (mmRunBeforeHorizSpace <= 8)
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
                    }
                    else if (c == '\u104A')
                    {
                        if (prevChar == '\u104A' || prevChar == '\u104B') hasConsecutiveMyanmarPunct = true;
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

            // 3. Generalized 'မ...ပဲ' -> 'မ...ဘဲ'
            if (hasMa && ContainsMm(text, mmMask, "ပဲ"))
            {
                foreach (Match m in NegativeVerbPePattern.Matches(text))
                {
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
                foreach (Match m in MisspelledNegativePhuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
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
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဖြစ်",
                        ProofingIssueType.SpellingError,
                        "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဖြစ်' ဟု ရေးသားရမည်။",
                        "ဖြစ်ပေါ်သည် (သတ်ပုံကျမ်း)", "V", true);
                }
            }

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
                        string preTarget = text.Substring(m.Index, g.Index - m.Index);
                        if (FutureExemptionSubclauseRegex.IsMatch(preTarget))
                            continue;
                        string v = g.Value;
                        string correct = v == "ခဲ့ပါသည်" ? "ပါမည်"
                                       : v == "ခဲ့ပါတယ်" ? "ပါမယ်"
                                       : v == "ခဲ့သည်" ? "မည်"
                                       : "မယ်";
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, v, correct,
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"ရှေ့တွင် အနာဂတ်အချိန်ပြပုဒ် ပါရှိသဖြင့် အတိတ်ပြဝိဘတ် '{v}' အစား အနာဂတ်ပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                            "ကာလကိုက်ညီသော အနာဂတ်ပြဝိဘတ်", "PART", true,
                            $"ဝါကျအစ၌ အနာဂတ်ကာလပြ ကြိယာဝိသေသန ('မနက်ဖြန်/နောက်လ/လာမည့်...') တည်ရှိပြီး ကြားတွင် ဝါကျခွဲ ('လျှင်/သော်လည်း') မခံသဖြင့် ပင်မကြိယာကို အတိတ်ပြပစ္စည်း '{v}' ဖြင့် မပိတ်ရဘဲ အနာဂတ်ပြ '{correct}' ဖြင့် ညှိနှိုင်းရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိသေသန နှင့် ကြိယာဝိဘတ် လိုက်လျောညီထွေမှု)");
                    }
                }
            }

            if (hasMyiOrMal && (hasKhae || ContainsMm(text, mmMask, "နေ့")))
            {
                foreach (Match m in PastAdverbWithFutureVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string preTarget = text.Substring(m.Index, g.Index - m.Index);
                    if (PastExemptionSubclauseRegex.IsMatch(preTarget))
                        continue;
                    string v = g.Value;
                    bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal);
                    bool hasPa = v.Contains("ပါ");
                    string correct = hasPa
                        ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်")
                        : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ရှေ့တွင် အတိတ်အချိန်ပြပုဒ် ပါရှိသဖြင့် အနာဂတ်ပြဝိဘတ် '{v}' အစား အတိတ်ပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        "ကာလကိုက်ညီသော အတိတ်ပြဝိဘတ်", "PART", true,
                        $"ဝါကျအစ၌ ပြီးလွန်ပြီးသော အတိတ်အချိန်ပြပုဒ် ('မနေ့က/ပြီးခဲ့သော...') ပါရှိပြီး အနာဂတ်အကူးအပြောင်းပုဒ် ('ယခု/ဆက်လက်') မပါသဖြင့် အနာဂတ်ပြ '{v}' အစား အတိတ်ပြ '{correct}' ဖြင့် ကာလညှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိသေသန နှင့် ကြိယာဝိဘတ် လိုက်လျောညီထွေမှု)");
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

            if (hasBu || hasPhu)
            {
                foreach (Match m in DroppedMaColloquialNegationPattern.Matches(text))
                {
                    if (BareNegationPrefixCheckRegex.IsMatch(m.Groups["Clause"].Value))
                        continue;

                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string negFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? ("မ" + verb + "ပါ")
                        : ("မ" + verb + "ဘူး");
                    string posFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (verb + "ပါသည်")
                        : (verb + "တယ်");

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အငြင်းပြရှေ့ဆက် 'မ' ကျန်ခဲ့ပါသည် — စံမြန်မာစာဖြင့် '{negFix}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"အပြောစကား၌ ရှေ့ဆက် 'မ-' ကို ချန်လှပ်၍ '{g.Value}' ဟု ပြောလေ့ရှိသော်လည်း စာအရေးအသားတွင် 'မ + ကြိယာ + ဘူး/ပါ' ဖွဲ့ထုံးအရ '{negFix}' ဟု အပြည့်အစုံ ရေးသားရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း 'မ...ဘူး / မ...ပါ')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = negFix,
                                MeaningCue = "အငြင်းဝါကျ စံအရေးအသား ('မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(negFix),
                                DiagnosticDetail = "အငြင်းပြရှေ့ဆက် 'မ-' ဖြည့်စွက်ချက်",
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 92.0
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
                foreach (Match m in CuratedBrokenCompoundPattern.Matches(text))
                {
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

                if (trie != null && hasShortMyanmarBeforeMyanmar)
                {
                    foreach (Match m in SpaceSplitPairCandidateRegex.Matches(text))
                    {
                        Group leftGrp = m.Groups["Left"];
                        if (text[leftGrp.Index] == 'မ')
                            continue;

                        string leftNorm = UnicodeNormalizer.NormalizeCanonicalOrder(leftGrp.Value);
                        if (SpaceSplitBoundaryParticles.Contains(leftNorm) || leftNorm.StartsWith("မ", StringComparison.Ordinal))
                            continue;

                        string rightNorm = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Right"].Value);
                        if (SpaceSplitBoundaryParticles.Contains(rightNorm))
                            continue;

                        string rawJoined = leftNorm + rightNorm;
                        string joinedNorm = (rightNorm.Length > 0 && rightNorm[0] >= '\u102B' && rightNorm[0] <= '\u103E')
                            ? UnicodeNormalizer.NormalizeCanonicalOrder(rawJoined)
                            : rawJoined;
                        if (VerifiedMandatoryCompounds.Contains(joinedNorm))
                            continue;

                        // Check O(1) dictionary compound existence BEFORE breaking syllables!
                        if (!trie.TryGetExactEntries(joinedNorm, out var jEntries) ||
                            jEntries == null || jEntries.Count == 0 ||
                            !jEntries[0].IsOfficialThatpon ||
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

                        Group tGrp = m.Groups["Target"];
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
                // 5. Colloquial-to-Literary (ရုံးသုံး / စာပေ အရေးအသား) Rules
                foreach (var rule in ColloquialRules)
                {
                    if (!ContainsMm(text, mmMask, rule.Trigger))
                        continue;

                    foreach (Match m in rule.Pattern.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, m.Index, m.Length))
                            continue;

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

                // 6a. 'အား' vs 'ကို' particle misuse (with Maung Khin Min's Double-'ကို' & Causative '-စေ' Exemption!)
                if (hasAh)
                {
                    foreach (Match m in AhInsteadOfKoPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        if (OverlapsExistingIssue(issues, pGroup.Index, pGroup.Length))
                            continue;
                        string verb = m.Groups["Verb"].Value;

                        // Exemption reasoning: If another 'ကို', role marker 'အဖြစ်' (Gazette appointment formula), or causative '-စေ' exists nearby in the same clause, 'အား' is grammatically licensed!
                        int winStart = Math.Max(0, pGroup.Index - 24);
                        int winEnd = Math.Min(text.Length, m.Index + m.Length + 24);
                        string clauseWindow = text.Substring(winStart, winEnd - winStart);
                        if (clauseWindow.Contains("ကို") || clauseWindow.Contains("စေ") || clauseWindow.Contains("အဖြစ်"))
                            continue;

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
            string authoritativeSource = "")
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
    }
}
