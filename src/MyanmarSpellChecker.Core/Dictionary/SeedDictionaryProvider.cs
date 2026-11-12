using System.Collections.Generic;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Dictionary
{
    /// <summary>
    /// Provides curated official entries and orthography rules from "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း"
    /// and "ရုံးသုံးမြန်မာစာ" to initialize the SQLCipher database and support offline compilation.
    /// </summary>
    public static class SeedDictionaryProvider
    {
        public static List<DictionaryEntry> GetOfficialSeedEntries()
        {
            return new List<DictionaryEntry>
            {
                // Government & Ministry Core Vocabulary (ရုံးသုံးဝေါဟာရများ)
                new DictionaryEntry { Word = "ပြည်ထောင်စု", MeaningCue = "နိုင်ငံတော်~", PosTag = "N" },
                new DictionaryEntry { Word = "သမ္မတ", MeaningCue = "နိုင်ငံတော်~", PosTag = "N" },
                new DictionaryEntry { Word = "နိုင်ငံတော်", MeaningCue = "အချုပ်အခြာအာဏာပိုင်~", PosTag = "N" },
                new DictionaryEntry { Word = "အစိုးရ", MeaningCue = "အုပ်ချုပ်ရေးအဖွဲ့အစည်း", PosTag = "N" },
                new DictionaryEntry { Word = "ဝန်ကြီးဌာန", MeaningCue = "ပြည်ထောင်စု~", PosTag = "N" },
                new DictionaryEntry { Word = "ဦးစီးဌာန", MeaningCue = "ဌာနဆိုင်ရာ~", PosTag = "N" },
                new DictionaryEntry { Word = "ညွှန်ကြားရေးမှူးချုပ်", MeaningCue = "ဦးစီးဌာနအကြီးအကဲ", PosTag = "N" },
                new DictionaryEntry { Word = "အမြဲတမ်းအတွင်းဝန်", MeaningCue = "ဝန်ကြီးဌာန ရုံးအဖွဲ့အကြီးအကဲ", PosTag = "N" },
                new DictionaryEntry { Word = "ပြည်ထောင်စုဝန်ကြီး", MeaningCue = "ဝန်ကြီးဌာနအကြီးအကဲ", PosTag = "N" },
                new DictionaryEntry { Word = "အမိန့်ကြော်ငြာစာ", MeaningCue = "ရုံးသုံးထုတ်ပြန်စာ", PosTag = "N" },
                new DictionaryEntry { Word = "ညွှန်ကြားချက်", MeaningCue = "လိုက်နာရန်~", PosTag = "N" },
                new DictionaryEntry { Word = "လုပ်ထုံးလုပ်နည်း", MeaningCue = "နည်းဥပဒေနှင့်~", PosTag = "N" },
                new DictionaryEntry { Word = "စည်းမျဉ်းစည်းကမ်း", MeaningCue = "လိုက်နာရမည့်~", PosTag = "N" },
                new DictionaryEntry { Word = "ဥပဒေ", MeaningCue = "ပြဋ္ဌာန်းချက်~", PosTag = "N" },
                new DictionaryEntry { Word = "ပြဋ္ဌာန်းချက်", MeaningCue = "ဥပဒေ~", PosTag = "N" },
                new DictionaryEntry { Word = "ဥက္ကဋ္ဌ", MeaningCue = "အဖွဲ့အစည်းအကြီးအကဲ", PosTag = "N" },
                new DictionaryEntry { Word = "အတွင်းရေးမှူး", MeaningCue = "ကော်မတီ~", PosTag = "N" },
                new DictionaryEntry { Word = "ကော်မတီ", MeaningCue = "လုပ်ငန်း~", PosTag = "N" },
                new DictionaryEntry { Word = "အစည်းအဝေး", MeaningCue = "ညှိနှိုင်း~", PosTag = "N" },
                new DictionaryEntry { Word = "မှတ်တမ်း", MeaningCue = "အစည်းအဝေး~", PosTag = "N" },
                new DictionaryEntry { Word = "အစီရင်ခံစာ", MeaningCue = "လုပ်ငန်းဆောင်ရွက်မှု~", PosTag = "N" },
                new DictionaryEntry { Word = "ဘဏ္ဍာရေး", MeaningCue = "ရသုံးမှန်းခြေငွေစာရင်း~", PosTag = "N" },
                new DictionaryEntry { Word = "စီမံကိန်း", MeaningCue = "နိုင်ငံတော်~", PosTag = "N" },
                new DictionaryEntry { Word = "တာဝန်", MeaningCue = "ထမ်းဆောင်ရမည့်~", PosTag = "N" },
                new DictionaryEntry { Word = "ဝန်ထမ်း", MeaningCue = "နိုင်ငံ့~", PosTag = "N" },
                new DictionaryEntry { Word = "လက်မှတ်", MeaningCue = "အသိအမှတ်ပြု~ / ရေးထိုးသည့်~", PosTag = "N" },
                new DictionaryEntry { Word = "နေပြည်တော်", MeaningCue = "မြို့တော်~", PosTag = "N" },
                new DictionaryEntry { Word = "မြန်မာစာ", MeaningCue = "ရုံးသုံးဘာသာစကား", PosTag = "N" },
                new DictionaryEntry { Word = "စာလုံးပေါင်း", MeaningCue = "~သတ်ပုံကျမ်း", PosTag = "N" },
                new DictionaryEntry { Word = "သတ်ပုံကျမ်း", MeaningCue = "မြန်မာစာ စာလုံးပေါင်း~", PosTag = "N" },

                // Famous "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" Homophone & Disambiguation Pairs
                new DictionaryEntry { Word = "ချည်", MeaningCue = "ကြိုး~၊ နှောင်~၊ အထည်~", PosTag = "N/V" },
                new DictionaryEntry { Word = "ချဉ်", MeaningCue = "အရသာ~၊ ချဉ်ပေါင်~", PosTag = "ADJ/V" },
                new DictionaryEntry { Word = "ချည်နှောင်", MeaningCue = "ကြိုးဖြင့်~", PosTag = "V" },
                new DictionaryEntry { Word = "မီ", MeaningCue = "အချိန်~၊ စံချိန်~၊ အမီလိုက်~ (ဟထိုးမပါ)", PosTag = "V" },
                new DictionaryEntry { Word = "မှီ", MeaningCue = "မှီခို~၊ မှီတွယ်~၊ မှီငြမ်းပြု~ (ဟထိုးပါ)", PosTag = "V" },
                new DictionaryEntry { Word = "အချိန်မီ", MeaningCue = "သတ်မှတ်ချိန်အတွင်း ရောက်ရှိ~", PosTag = "ADV" },
                new DictionaryEntry { Word = "စံချိန်မီ", MeaningCue = "အရည်အသွေး စံနှုန်း~", PosTag = "ADJ/V" },
                new DictionaryEntry { Word = "မှီခို", MeaningCue = "အားကိုး~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ယာဉ်", MeaningCue = "မော်တော်~၊ ရထား~၊ လေ~ (သွားလာရေးယာဉ်)", PosTag = "N" },
                new DictionaryEntry { Word = "ယဉ်", MeaningCue = "~ကျေးမှု၊ သိမ်မွေ့~သည်", PosTag = "ADJ/V" },
                new DictionaryEntry { Word = "မော်တော်ယာဉ်", MeaningCue = "စက်တပ်သွားလာရေး~", PosTag = "N" },
                new DictionaryEntry { Word = "ယဉ်ကျေးမှု", MeaningCue = "ရိုးရာဓလေ့နှင့် အနုပညာ~", PosTag = "N" },
                new DictionaryEntry { Word = "စဉ်", MeaningCue = "အစီအ~၊ စဉ်ဆက်မပြတ် (ဉကလေးသတ်)", PosTag = "N/PART" },
                new DictionaryEntry { Word = "စည်", MeaningCue = "~ပင်သာယာ၊ ဗုံ~ (ညကြီးသတ်)", PosTag = "N/V" },
                new DictionaryEntry { Word = "အစီအစဉ်", MeaningCue = "လုပ်ငန်းဆောင်ရွက်မှု~", PosTag = "N" },
                new DictionaryEntry { Word = "စည်ပင်သာယာ", MeaningCue = "မြို့ရွာဖွံ့ဖြိုး~ရေး", PosTag = "N/V" },
                new DictionaryEntry { Word = "သဏ္ဌာန်", MeaningCue = "ပုံပန်း~ (ဌဝမ်းဘဲဆင့်)", PosTag = "N" },
                new DictionaryEntry { Word = "ပြဿနာ", MeaningCue = "ဖြေရှင်းရန်ကိစ္စ (ဿကြီး)", PosTag = "N" },
                new DictionaryEntry { Word = "ပုဂ္ဂိုလ်", MeaningCue = "လူ~ (ဂငယ်နှစ်လုံးဆင့်)", PosTag = "N" },
                new DictionaryEntry { Word = "ကဏ္ဍ", MeaningCue = "အခန်း~ (ဏကြီး ဍရင်ကောက်ဆင့်)", PosTag = "N" },
                new DictionaryEntry { Word = "အကြောင်းအရာ", MeaningCue = "ရုံးစာခေါင်းစဉ်~", PosTag = "N" },
                new DictionaryEntry { Word = "ရည်ညွှန်းချက်", MeaningCue = "ရုံးစာ~ (ဟထိုးပါ)", PosTag = "N" },
                new DictionaryEntry { Word = "စာအမှတ်", MeaningCue = "ရုံးစာအမှတ်စဉ်~", PosTag = "N" },
                new DictionaryEntry { Word = "ကြဉ်း", MeaningCue = "ဆောင်~၊ သယ်~", PosTag = "V" },
                new DictionaryEntry { Word = "ကျဉ်း", MeaningCue = "~မြောင်း၊ အကျယ်အ~", PosTag = "ADJ/V" },
                new DictionaryEntry { Word = "ကျဉ်းမြောင်း", MeaningCue = "နေရာ~သည်", PosTag = "ADJ/V" },
                new DictionaryEntry { Word = "ဆောင်ကြဉ်း", MeaningCue = "အကျိုး~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "တည်ဆောက်", MeaningCue = "အဆောက်အအုံ~သည်၊ နိုင်ငံ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ဖတ်ရှု", MeaningCue = "စာ~သည်၊ လေ့လာ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ဖက်", MeaningCue = "ပွေ့~သည်၊ လက်တွဲ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ဖတ်", MeaningCue = "စာ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ကြေညာ", MeaningCue = "အများသိစေရန် ထုတ်ပြန်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ကြေညာချက်", MeaningCue = "သတင်းထုတ်ပြန်~", PosTag = "N" },
                new DictionaryEntry { Word = "ကြော်ငြာ", MeaningCue = "ကုန်ပစ္စည်း~၊ စီးပွားရေး~", PosTag = "N/V" },
                new DictionaryEntry { Word = "ရည်ရွယ်ချက်", MeaningCue = "ဦးတည်ချက်နှင့်~", PosTag = "N" },
                new DictionaryEntry { Word = "ရည်ရွယ်", MeaningCue = "ဦးတည်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ကျန်းမာရေး", MeaningCue = "ကိုယ်စိတ်~", PosTag = "N" },
                new DictionaryEntry { Word = "ပညာရေး", MeaningCue = "သင်ကြားမှု~", PosTag = "N" },
                new DictionaryEntry { Word = "စီးပွားရေး", MeaningCue = "ကုန်သွယ်မှုနှင့်~", PosTag = "N" },
                new DictionaryEntry { Word = "လူမှုရေး", MeaningCue = "အများပြည်သူ~", PosTag = "N" },
                new DictionaryEntry { Word = "ဆောင်ရွက်", MeaningCue = "လုပ်ငန်း~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "စစ်ဆေး", MeaningCue = "ကြည့်ရှု~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ခန့်အပ်", MeaningCue = "ရာထူးတာဝန်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ရွေးချယ်", MeaningCue = "စိစစ်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ပေးအပ်", MeaningCue = "တာဝန်/ဆု~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "အကြောင်းကြား", MeaningCue = "သိရှိစေရန်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "တင်ပြ", MeaningCue = "အစီရင်ခံ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "အတည်ပြု", MeaningCue = "ဆုံးဖြတ်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ညှိနှိုင်း", MeaningCue = "ဆွေးနွေး~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ဆွေးနွေး", MeaningCue = "တိုင်ပင်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "လိုက်နာ", MeaningCue = "စည်းကမ်း~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ထုတ်ပြန်", MeaningCue = "အမိန့်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ပြုစု", MeaningCue = "စာရင်း/ကျမ်း~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "စိစစ်", MeaningCue = "သေချာစွာ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ကြီးကြပ်", MeaningCue = "ကွပ်ကဲ~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ထိန်းသိမ်း", MeaningCue = "စောင့်ရှောက်~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ကာကွယ်", MeaningCue = "တားဆီး~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "ဖွံ့ဖြိုးတိုးတက်", MeaningCue = "စဉ်ဆက်မပြတ်~သည်", PosTag = "V" },

                // Official Numeral Classifiers & Adverbs in သတ်ပုံကျမ်း
                new DictionaryEntry { Word = "တစ်ခု", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်ဦး", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်ယောက်", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်ဖက်", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်ကြိမ်", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်ရက်", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်လ", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်နှစ်", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တစ်နိုင်ငံလုံး", MeaningCue = "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း", PosTag = "NUM" },
                new DictionaryEntry { Word = "တဖြည်းဖြည်း", MeaningCue = "ကြိယာဝိသေသန (အသတ်မပါ)", PosTag = "ADV" },
                new DictionaryEntry { Word = "တစိုက်မတ်မတ်", MeaningCue = "ကြိယာဝိသေသန (အသတ်မပါ)", PosTag = "ADV" },
                new DictionaryEntry { Word = "တင့်တယ်", MeaningCue = "ကျက်သရေရှိ၍ လှပ~သည်", PosTag = "V/ADJ" },
                new DictionaryEntry { Word = "ချိနဲ့", MeaningCue = "အင်အား~သည်", PosTag = "V/ADJ" },
                new DictionaryEntry { Word = "ယိမ်းနဲ့", MeaningCue = "လှုပ်ရှား~သည်", PosTag = "V" },
                new DictionaryEntry { Word = "အမယ်", MeaningCue = "အမျိုး~ / မိခင်", PosTag = "N" },
                new DictionaryEntry { Word = "တချို့", MeaningCue = "အချို့ (သတ်ပုံကျမ်း)", PosTag = "PRON" },
                new DictionaryEntry { Word = "တခြား", MeaningCue = "အခြား (သတ်ပုံကျမ်း)", PosTag = "ADJ" },
                new DictionaryEntry { Word = "၎င်း", MeaningCue = "ထိုအရာ / ယင်း (ရုံးသုံးသင်္ကေတ U+104E)", PosTag = "PRON" }
            };
        }

        public static List<OrthographyRule> GetOfficialOrthographyRules()
        {
            return new List<OrthographyRule>
            {
                new OrthographyRule
                {
                    WrongForm = "တီဆောက်",
                    CorrectForm = "တည်ဆောက်",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဆောက်အအုံ/နိုင်ငံ ဆောက်လုပ်ခြင်းကို 'တည်ဆောက်' (ညသတ်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "လက်မတ်",
                    CorrectForm = "လက်မှတ်",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'လက်မှတ်' (ဟထိုးပါ) ဟု ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ဖက်ရှု",
                    CorrectForm = "ဖတ်ရှု",
                    Category = "Spelling",
                    Explanation = "စာကြည့်ရှုခြင်းကို 'ဖတ်ရှု' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "စာဖက်",
                    CorrectForm = "စာဖတ်",
                    Category = "Spelling",
                    Explanation = "စာကြည့်ရှုခြင်းကို 'စာဖတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ကြော်ညာ",
                    CorrectForm = "ကြေညာ",
                    Category = "Spelling",
                    Explanation = "'ကြော်ညာ' ဟူသော စာလုံးပေါင်းမရှိပါ။ အများသိစေရန် ထုတ်ပြန်ခြင်းအတွက် 'ကြေညာ' သို့မဟုတ် စီးပွားရေးအတွက် 'ကြော်ငြာ' ကို သုံးရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ကြေငြာ",
                    CorrectForm = "ကြေညာ",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အများသိစေရန် ထုတ်ပြန်ခြင်းကို 'ကြေညာ' (ညကြီး) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ချဉ်နှောင်",
                    CorrectForm = "ချည်နှောင်",
                    Category = "Spelling",
                    Explanation = "ကြိုးဖြင့် နှောင်ဖွဲ့ခြင်းကို 'ချည်နှောင်' (ညကြီးသတ်) ဖြင့် ရေးသားရမည်။ ('ချဉ်' သည် အရသာချဉ်ခြင်းဖြစ်သည်)။"
                },
                new OrthographyRule
                {
                    WrongForm = "ရီရွယ်ချက်",
                    CorrectForm = "ရည်ရွယ်ချက်",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ရည်ရွယ်ချက်' (ညသတ်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ကျန်းမာယေး",
                    CorrectForm = "ကျန်းမာရေး",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ကျန်းမာရေး' (ရကောက်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ပြဌာန်း",
                    CorrectForm = "ပြဋ္ဌာန်း",
                    Category = "Spelling",
                    Explanation = "ဥပဒေ သတ်မှတ်ထုတ်ပြန်ခြင်းကို 'ပြဋ္ဌာန်း' (ဋသံလျင်းချိတ် ဌဝမ်းဘဲဆင့်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ဥက္ကဌ",
                    CorrectForm = "ဥက္ကဋ္ဌ",
                    Category = "Spelling",
                    Explanation = "အဖွဲ့အစည်းအကြီးအကဲကို 'ဥက္ကဋ္ဌ' (ဋသံလျင်းချိတ် ဌဝမ်းဘဲဆင့်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "စည်းမျည်း",
                    CorrectForm = "စည်းမျဉ်း",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'စည်းမျဉ်း' (ဉကလေးသတ်) ဖြင့် ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "ညှိနိုင်း",
                    CorrectForm = "ညှိနှိုင်း",
                    Category = "Spelling",
                    Explanation = "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ညှိနှိုင်း' (ဟထိုးပါ) ဟု ရေးသားရမည်။"
                },
                new OrthographyRule
                {
                    WrongForm = "စီစစ်",
                    CorrectForm = "စိစစ်",
                    Category = "Spelling",
                    Explanation = "သေချာစွာ စစ်ဆေးခြင်းကို 'စိစစ်' (လုံးကြီးတင်) ဖြင့် ရေးသားရမည်။"
                }
            };
        }
    }
}

