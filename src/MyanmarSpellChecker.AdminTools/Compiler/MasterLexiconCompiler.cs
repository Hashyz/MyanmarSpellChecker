// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MyanmarSpellChecker.AdminTools.Compiler
{
    public static class MasterLexiconCompiler
    {
        public sealed class EntryItem
        {
            public string Word = "";
            public string MeaningCue = "";
            public string PosTag = "";
            public string UsageNote = "";
            public int Frequency = 0;
            public bool IsOfficialThatpon = false;
        }

        public sealed class RuleItem
        {
            public string WrongForm = "";
            public string CorrectForm = "";
            public string Category = "Spelling";
            public string Explanation = "";
        }

        public sealed class ContextRuleItem
        {
            public string WrongForm = "";
            public string CorrectForm = "";
            public string TriggerWords = "";
            public string AntiTriggerWords = "";
            public string Direction = "Both";
            public int Window = 3;
            public bool IsWarningOnly = false;
            public bool SkipParticles = false;
            public string Explanation = "";
        }

        public sealed class BigramItem
        {
            public string LeftWord = "";
            public string RightWord = "";
            public int Count = 0;
        }

        private static readonly HashSet<string> KnownWrongBlacklist = new HashSet<string>(StringComparer.Ordinal)
        {
            "တယောက်", "တခု", "တဦး", "တဖက်", "တခါ", "တချက်", "တစ်ကယ်", "တစ်ချို့", "တစ်ခြား",
            "အချိန်မှီ", "မော်တော်ယဉ်", "ကြော်ညာ", "ကြေငြာ", "လက်မတ်", "လတ်မှတ်", "သတ်မတ်", "ပြသနာ", "သဏ္ဍာန်", "ဥက္ကဌ", "ပြဌာန်း",
            "ပါအုန်း", "ပတ်သတ်", "ရမ်ပုံငွေ", "အကျောင်းအရာ", "ရေရွယ်ချက်", "စီပွားရေး", "ပုဂိုလ်", "ကွန်ပြူတာ", "အင်တာနတ်",
            "ဒါပေမယ့်", "သို့ပေမယ့်", "တရ်", "မရ်", "ပီး", "နောက်ပီး", "ဖစ်", "ညာဏ်", "သဘောကြ", "ပတ်ဝန်းကြင်", "လျှက်",
            "ယောကျာ်း", "ပုဇွန်", "စံပင်", "ဦးဂေါင်း", "ဦးနောက်", "ဦးထုတ်", "ဦးစီဌာန", "ဦးသောင်",
            "အကျီ", "လဖက်", "လဖက်ရည်", "တခါး", "တဂါး", "စဂါး", "ပိုက်စံ",
            "လျော့ဈေး", "ဆက်နွယ်", "နားကြပ်", "တိတ်ခွေ", "စပယ်ရာ", "သကရာဇ်",
            "အစွန်းအစ", "အရုဏ်တတ်", "မျောက်မြား", "သံရောင်လိုက်", "ရှင်သံ", "ရာရေ", "ရေရွက်",
            "ကဏ္ဋကောစ", "ကပ္ပဌီတိလေးပါး", "ဖောဠ္ဋဗ္ဗာရုံ", "ကြပ်ထုတ်", "ကြွက်ကြွက်ဆူ", "နုနုထွတ်ထွက်", "ဆတ်ဆန်",
            "နေ့လည်", "အိပ်ယာ", "ထိမ်းသိမ်း", "ထိန်းမြား", "အထိန်းအမှတ်", "နုတ်ဆက်", "ထည်သွင်း",
            "ချီးမြင့်", "မြင့်တင်", "လွတ်တော်", "ညွန်ကြား", "ပန်းကုန်း", "မျက်စိကမ်း", "ခါးကုံး",
            "လုံးဝဥသုံ", "လုံးဝဥဿုန်", "နလုံး", "နလုံးသား", "လျောက်ထား", "ယွဉ်ပြိုင်", "ကျနော်",
            "ဆက်ရုံ", "ခစားကွင်း", "အိမ်ပျံ", "စာစား", "သမတ", "ဖူးမျှော်", "စျေး",
            "အခက်ခဲ", "အသိမှတ်", "အသိမှတ်ပြု", "အကောင်ထည်", "အကောင်ထည်ဖော်", "အစည်းဝေး", "အတွေ့ကြုံ", "အခွင့်ရေး", "အချက်လက်", "အခြေနေ", "အခမ်းနား",
            "ပတ်သက်မု", "လူမူ", "လူမူရေး", "မိုးညင်း", "မိုးညင်းမြို့", "မိုးညင်းမြို့နယ်", "ပီလား", "သတိမ", "နဲ", "ခန",
            "ရလာဒ်", "အပုဒ်", "စာပုဒ်", "ဒါဏ်", "ပြစ်ဒါဏ်", "ထောင်ဒါဏ်", "ဒါဏ်ငွေ", "ဒါဏ်ရာ", "ပိုဒ်မ",
            "ဗိုဟ်", "ဗိုဟ်ကြီး", "ဗိုဟ်မှူး", "ဗိုဟ်ချုပ်", "ဂြိုလ်", "ဂြိုလ်တု", "ဂြိုလ်ကျ", "ဂြိုလ်စားသက်ရောက်", "အရံဂြိုလ်",
            "သင်္ဂြိုလ်", "ကဲ့ရဲ့သင်္ဂြိုလ်", "သင်းဂြိုဟ်", "မီးသဂြိုဟ်", "ဗျဉ်စတားဂြိုဟ်", "ပဉ္စတားဂြိုဟ်",
            "မြင့်မိုရ်", "မြင်းမိုလ်", "သီဟိုလ်", "ဗြဟ္မစိုလ်", "ကုသိုဟ်", "ပုဂ္ဂိုဟ်", "ပုဂိ္ဂုလ်", "ဝိနည်းဓိုရ်ပုဂိ္ဂုလ်",
            "ဇဒိပ္ဖိုလ်", "ဇဒိပ္ဖိုလ်ပွင့်", "ဆုလာဒ်", "လာဒ်", "လဒ်", "ပြသာဒ်", "ရှားစောင်းပြသာဒ်",
            "သဗ်ညုတဉာဏ်", "သဗ်ကြတ်", "သဗ်ရနံပေါင်း", "ရက္ခိုက်သ်", "ချသ်တောက်မွေးစုတ်", "စရာတော်",
            "ပရိတ်သတ်", "ပရိတ်ဘောဂ", "ပရိတ်ကရာ", "ပင်ကိုယ်", "စိုပြေ", "ပြဿဒ်", "ပြဿာဒ်", "တန်ဆောင်းပြဿဒ်",
            "စကြာဝဠာ", "ဝိရိယ", "ပိတိ", "ယေဘူယျ", "ဖြန့်ချီ", "ကြည့်ရှူ", "ရှေးရှူ", "အသက်ရှု",
            "စာရိတ်", "မတ်တပ်", "မတ်တပ်ရပ်", "ငလျှင်", "ပြူတင်းပေါက်", "ပတင်းပေါက်", "တတောင်ဆစ်", "တလျှောက်",
            "တဝန်း", "တစ်ဝှမ်း", "ရှာလကာရည်", "ဝိဇ္ဇာမိုရ်", "အဿထိုရ်", "အဿထိုရ်ရ", "မျက်စေ့",
            "ရွေ့ပြောင်း", "စွဲဆောင်", "စွဲဆောင်မှု", "ထိန်ချိန်", "စက်ကန့်", "စကန့်",
            "မိးကုန်ယမ်းကုန်", "အသုရိန်", "ယုဇနာ", "သီရီ", "ရှုနာရှိုက်ကုန်း", "ကွာစိ", "နိသိဒိုင်",
            "ခါရမ်း", "အာကြီး", "အာလွန်", "အာစောက်", "အာမာ", "စာလုတ်", "စိတ္တဇာ", "ဒန္တဇာ", "မုဒ္ဓဇာ", "သံသေဒဇာ",
            "ရောဂန္တရာကပ်", "ကတ္တရာသင်္ချေ", "ဗဟိုရ်", "သာမာန်", "အနာဂါတ်", "ဟေမာန်", "ဘေးဥပါဒ်",
            "ကောက်နှုတ်", "ထုတ်နှုတ်", "စွဲလန်း", "ရှစ်ခိုး", "မွေးမြှူ", "ဖွံ့ပျိုး", "ဖြိုးထောင်",
            "သတင်းကျွတ်", "သတင်းသုံး", "သီတင်းစာ", "အန္တရာဉ်", "ပရိယာဉ်", "လိုက်ချင်", "ကိုကိုယ်",
            "ဒွီလိင်", "နပုလိင်", "ဒွိလိမ်", "ဒွိလိန်", "ဆုဆောင်း", "စုတောင်း", "နှုတ်စက်", "ဆက်ဆပ်", "ဆိုက်ပျိုး", "ဆိုက်ပျိုးရေး",
            "ကုတင်", "ကုတင်ပေါ်", "ဆေးရုံကုတင်", "ကုတင်ညောင်စောင်း", "သံကုတင်", "ခေါက်ကုတင်", "သမ္မဒ", "သမဒ",
            "ဥက္ကဋ္ဋ", "ဥက္ကဋ", "ဥက္ကဌ္ဌ", "ဉက္ကဋ္ဌ", "ဉက္ကဌ", "ဉက္ကဌ္ဌ", "မစ်တာ", "မေတ္တ", "သစ်စာ", "သစ္စ",
            "ဇပင်", "ကျင်ဝတ်", "ဖုယား", "ကြက်ဉဆန်ချောင်", "ကြောင်ဉကျည်", "ဉက္ကာပျံ", "ဉက္ကောဋေယျ", "ဉက်", "ဖွတ်ဉကျည်",
            "စာရွက်စာတန်း", "စာချုပ်စာတန်း", "သုတေသနစာတန်း", "ငုတ်မိတဲတိုင်", "ငုတ်မိသဲတိုင်", "ငုပ်မိတဲတိုင်",
            "မီးသည်း", "ဒွာဒဒသမ", "ဒွါဒဒသမ", "ဒွာဒသမ", "ဒဿမ", "ဒဿမကိန်း", "ဒဿမစနစ်", "ဒဿမစနစ်သုံး", "ဒဿမပုံစံ",
            "လေးခြိုး", "လေးခြိုးကြီး", "ဒွေးခြိုး", "တြိခြိုး", "ပါရာစီဒမော့", "ပါရာစီတမော့", "ပါရာစီဒမော", "ပါရာဇီတမော", "ပါရာဇီဒမော့",
            "ပဒီး", "ပုဒီး", "စိတ်ပုတီး", "ပုတီးစိတ်", "ကျေးစီ", "ကျေးစည်", "ကြေးစီ", "ကြေးသွမ်း", "ကြေးသွမ်းဘုရား", "သွမ်းဘုရား",
            "ကဒ်", "ဆင်းကဒ်", "ဆင်းမ်ကဒ်", "ဖုန်းကဒ်", "ဝန်ထမ်းကဒ်", "အကြွေးဝယ်ကဒ်", "ကဒ်ပြား", "ဘဏ်ကဒ်", "မှတ်ပုံတင်ကဒ်", "မန်မိုရီကဒ်",
            "အစီစဉ်", "ထပ်မ", "ရန်လိုအပ်", "မွေးနေ့မှ", "ရရှိရန်", "ကွင်းအတွင်း",
            "စတော့အိပ်ချိန်း", "နိုင်ဝင်ဘာ", "နိုင်ဝင်ဘာလ", "အုပ်ချူပ်", "အုပ်ချူပ်ရေး", "ဦးစီးချူပ်",
            "ရင်းနှီးမြှပ်နှံ", "ရင်းနှီမြှုပ်နှံ", "ရင်းနှီးမြုပ်နှံ", "ရံပုံငွေ", "အစည်အဝေး", "အစည်းအဝေ",
            "ဖတ်ရှု့", "စီမံခန့်ခွဲ့", "ဖွဲစည်း", "ကိုယ်စာလှယ်", "ဆင်နွဲ", "လွဲပြောင်း",
            "စွမ်အင်", "စွမ်အား", "လူ့စွမ်အား", "တွဲဘက်", "ဇုံ", "ကျောက်ပန်တောင်း", "ပျဉ်မနား",
            "ဖြန့်ဖြူရေး", "စာရေရိက္ခာ", "အသက်ရှုလမ်းကြောင်း", "မီးထွန်ညှိ", "ကွန့်ဖရင့်", "သီးတင်းကျွတ်",
            "ဓာတ်ခန်းခွဲ", "ကူသန်းရောင်းဝယ်ရေး", "တန်းဖိုးနည်း", "ဈပန", "ထေရုပတ္တိ", "လွပ်လပ်ရေး",
            "ဖရောင်းတိုင်", "ဂျာမဏီ", "လှည်လည်", "ရှာပါး", "ပက်သက်", "ပတ်သတ်ပြီး", "စပ်လဉ်း", "စပ်လျဉ်",
            "ဖုန်းနံပတ်", "ဝိသုဒ္ဒါရုံ", "ပတ်ဝန်းကျင်း", "ဇာသဘင်", "မြူနီဆီပယ်", "မြူနီစပယ်",
            "အကျိူး", "ပက်ဖြန်း", "နှိုင်းယဉ်", "တစ်ဟုန်တိုး", "သမာရိုးကျ", "စာနယ်ဇင်", "ခလုပ်နှိပ်",
            "ကွင်ဆင်း", "နှစ်ပတ်လည့်နေ့", "အကျင်းထောင်", "သင့်လျှော်", "အထောက်ကူ", "နည်လမ်း",
            "ညွှန်ကြားရေးမှူ", "လျှာ့ချ", "ဝိုင်ဝန်း", "လီမီတက်", "အာလုံး", "အခွင့်အလန်း",
            "အသုံးအဆာင်", "အသုံးဆောင်", "မတ်ထရစ်", "မြွတ်ကြား", "ပြည်နည်", "ကွက်ကဲရေး", "ဟိုပုန်းမြို့", "ဟိုပုန်းမြို့နယ်",
            "မိုးညင်း", "မိုးညင်းမြို့", "မိုးညင်းမြို့နယ်", "မိုးညင်းခရိုင်", "ဖျာပွန်", "ဖျာပွန်မြို့", "ဖျာပွန်မြို့နယ်", "ဖောင်ပျင်း", "ဖောင်ပျင်းမြို့", "ဖောင်ပျင်းမြို့နယ်",
            "ပျယ်ပြယ်", "ထက်မြတ်စေရေး", "ထုတ်ပိုးသူ", "ကုန်သွယ်မူ", "အရှိန်အဟုန်မပြက်", "အရည်းအချင်း",
            "လေဟာပြင်", "ရပ်တံ့", "ဂါရပြု", "တင်ပြင်ချက်", "ဩစတေးလျ", "မျက်နှာကျက်", "ထွန်ကား",
            "ဗိမ္မာန်", "ရှုဒေါင့်", "ဝက်ဒ်ဆိုဒ်", "ပျက်ဆီး", "အခမ်အနား", "မုံညှင်း", "သဘောဝ",
            "တောင်ပြုံး", "တောင်ကိုးရီးယား", "တွံ့တေး", "တွံ့တေးမြို့နယ်", "နိုင်ငံခြာသား", "စူပါမတ်ကက်",
            "နှီးနွယ်", "တွက်ချေကိုက်", "သံတမံ", "ဖြန်ဖြေ", "မတိုင်မှီ", "စီစစ်ရေး", "ကြည်ရှုစစ်ဆေး",
            "သိဂုင်္တ္တရတောင်", "သံင်္ဃာစဉ်", "တုရိယာ", "တူးရိယာ",
            "ချော့ကလက်", "ချောကလတ်", "ချော့ကလတ်", "ပတ်စီ", "ကိုကာကိုလား", "ကိုးကာကိုလာ",
            "ကိတ်မုဒ်", "ကိတ်မုန်", "ကိက်မုန့်", "ကိက်မုဒ်", "ပေါင်မုဒ်", "ပေါင်မုန်", "ဘီစကွတ်မုဒ်", "မုဒ်ဟင်းခါး",
            "ဘေကျင်း", "ဘေဂျင်း", "ဘေကျင်းမြို့", "ဘေဂျင်းမြို့", "ဖတ်ဒရယ်",
            "ပြဋ္ဋာန်း", "ပြဌ္ဌာန်း", "ပြဋာန်း", "ပြဌာန်း",
            "ဆရာကတော", "စရာအမှတ်", "ကျောင်းစရာ", "ကျောင်းစရာမ", "ကျောင်းစရာကြီး", "ပန်းပုစရာ", "ပန်းချီစရာ", "ဆေးစရာ", "စရာဝန်",
            "မြတ်သေအကျင့်", "မြတ်သေအကျင့်", "အသောသူ", "သေအပ်ငွေ", "စုံသေအမွေပြတ်", "အသော်က",
            "မဂ္ဂဌာန်", "ဖလဌာန်", "စံနစ်", "ငွေကြေးစံနစ်", "ကြေးငွေစံနစ်", "ကြိုးနီစံနစ်", "စည်းစံနစ်",
            "ကုက္ကိုလ်", "ကုက္ကိုလ်ပင်", "ရွှေကုက္ကိုလ်", "ဖွံဖြိုး", "ဖွံဖြိုးရေး",
            "အသွင်သဏ္ဍန်", "အသွင်သဏ္ဍာန်", "အနုမြူ", "ဌား", "ဌားရမ်း", "ဌားယူ", "အိမ်ဌား", "အဌား", "ကရိယာ",
            "တရက်", "ရေယဉ်", "ဘိုလ်", "ဖုရား", "ဘုယား", "သစာ", "မေတာ", "ဆပင်", "ဆန်ပင်", "ဒွာရ",
            "လျော့ချ", "လျှော့နည်း", "ကြည့်ရှု့", "ဂရုဏာ", "စင်္ကြန်", "ခေတ်မှီ", "ကထိကဝတ်",
            "ကမ္ဗာလုံး", "ကုမ္ဗဏ်", "ကိုယ်တိုင်း", "ကာတွန်", "ကူးဆက်", "စီးဆင်", "စွက်ပက်",
            "ကော်ပိုရှေးရှင်း", "ခြေလမ်း", "ကျီးကျူး", "ချီကျူး", "ချက်ဖြုတ်", "ခြားနာ", "ဂနန်း",
            "ပြည်စုံ", "သေခြာ", "ဖျေရှင်း", "ဖျည့်ဆည်း", "ဖျည့်စွက်", "ဆန်းသပ်", "အကြိုးကျေးဇူး", "စည်းပွားရေး",
            "ငါလိုး", "ငိုးမ", "လီး", "စောက်ပတ်", "စောက်ဖုတ်", "မအေလိုး",
            // V26 Whole-Lexicon 360 Consistency Audit: colloquial inflections, Mon/OCR leaks, non-official Rakhine သျှင် forms, & weak typos
            "ပီးပီး", "ပါတယ်", "ပါမယ်", "ပါ့မယ်", "ပေါ်တယ်", "ဒါနဲ့", "ကိုဦးစားပေး",
            "ယောကျ်ား", "ယောကျ်ားလျာ", "လကျ်ာ",
            "က်ုကျဝ်ဍဝ်", "က်ုစင်", "က်ုစယ်", "စှ်ပန်", "ထပှ်", "ဓဂဵု", "ပနံာ်ရိကျး",
            "တစ်ဠှပံကြီး", "တစ်ဠှမံတစ်မ", "တစ်ဠှမံတစ်သိုက်",
            "စကားပျည်", "စကားပြစ်", "တပ်သိ", "ကုတ်ခြက်", "စောင်ရဉ်း", "အဟန်း",
            "ခိုင်လုံသောကြွေးသျှင်", "ခံဝန်ချုပ်မြီးသျှင်", "စတော့ပိုင်သျှင်", "စာမတတ်လိပ်စာသျှင်",
            "ဆန္ဒမဲသျှင်စု", "ဆိုင်းငံ့မြီးသျှင်", "ဉာဏ်ကြီးသျှင်", "ဉာဏ်သျှင်", "တရားဝင်ပိုင်သျှင်",
            "တွဲဖက်ပိုင်သျှင်", "နွားသျှင်", "ပင်ရင်းပိုင်သျှင်", "ပုဏ္ဏားအိမ့်သျှင်", "မဲဆန္ဒသျှင်စု",
            "မြင်းသျှင်", "သမက်သျှင်", "သမီးသျှင်", "သောတသျှင်", "အတတ်သျှင်", "ဓနသျှင်",
            "ကသပေါင်", "ခလာသီး", "ချိန်ခွင်ပေါင်း", "ချုံပိတ်ပေါင်", "ငါးကတို", "ထီနမိဒ္ဓ",
            "နာရနီ", "ပတ်ချာလှည့်", "မျဉ်းပြိုင်တံ", "ရဲရဲညီး", "ရှောက်ရွာမိုး", "လင်းကဲတဖြာ",
            "သက်သေခန်း", "သီတဂု", "အံသကိုဋ်", "ဣဿရမူလိ", "ပိန်ညော်", "ပြိန်မြက်", "ဖင့်နွှဲ့",
            // V32 Whole-Corpus Data Validation & Sanitization: vulgar / bound-fragment derivative leaks
            "စောက်ရေး", "တို့အဖွဲ့", "တို့အရေး", "ဆိုင်ရာဥပဒေ", "ရေးလုပ်ငန်း"
        };

        private static readonly HashSet<string> ToxicExternalRuleWrongForms = new HashSet<string>(StringComparer.Ordinal)
        {
            "သတ်သေ", "ကလေ", "မိတာ", "ဂါး", "ူးဘ", "ဖက်နေ", "ကြောင်းပို့",
            "နည်", "လမ်", "ကျောင်", "ငေ", "စိတ", "ရုပ", "ရလဒ",
            "ဘီရို", "ဝိညာဏ်", "ဝိနယ",
            "မော်နီတာ", "ဒီဂျစ်တယ်", "ဖယ်ဒရယ်", "ကောင်ဆယ်", "အိတ်ချ်အိုင်ဗွီ",
            "အယ်လာဂျီ", "ဆော့ဝဲလ်", "ဆူဆီ", "ဓာန", "သစာ", "စပီး", "နက်ဝတ်", "သွားခဲ",
            "တစ်ဆက်တည်း", "တစ်သမတ်တည်း", "အလျှောက်", "လက်ဆက်", "သွင်",
            "ဆင်းမ်ကတ်", "ဆင်းကတ်", "ကိုယ်ရေ", "ပညာ့", "မေတ္တာ့", "သစ္စာ့"
        };

        private static readonly Regex PatSintCheckRegex = new Regex(@"(?<!\u1004\u103A)([\u1000-\u102A\u103F])\u1039([\u1000-\u102A\u103F])", RegexOptions.Compiled);
        private static readonly HashSet<string> ValidPatSintPairs = new HashSet<string>(StringComparer.Ordinal)
        {
            "\u1000\u1000", "\u1002\u1002", "\u1005\u1005", "\u1007\u1007",
            "\u100B\u100B", "\u100D\u100D", "\u100F\u100F", "\u1010\u1010",
            "\u1012\u1012", "\u1014\u1014", "\u1015\u1015", "\u1017\u1017",
            "\u1019\u1019", "\u101A\u101A", "\u101C\u101C", "\u1020\u1020",
            "\u1000\u1001", "\u1002\u1003", "\u1005\u1006", "\u1007\u1008",
            "\u100B\u100C", "\u100D\u100E", "\u1010\u1011", "\u1012\u1013",
            "\u1015\u1016", "\u1017\u1018",
            "\u1009\u1005", "\u1009\u1006", "\u1009\u1007", "\u1009\u1008",
            "\u100F\u100B", "\u100F\u100C", "\u100F\u100D", "\u100F\u100E",
            "\u1014\u1010", "\u1014\u1011", "\u1014\u1012", "\u1014\u1013",
            "\u1019\u1015", "\u1019\u1016", "\u1019\u1017", "\u1019\u1018",
            "\u101F\u1019", "\u101E\u1019", "\u101E\u1010", "\u101E\u1011",
            "\u1012\u1017", "\u1012\u1018", "\u1000\u1010", "\u1015\u1010",
            "\u101C\u1015"
        };

        private static readonly HashSet<string> CollocationSkipHeads = new HashSet<string>(StringComparer.Ordinal)
        {
            "ကာ", "ကတည်းက", "သော်လည်း", "လျက်", "ဘဲ", "၍", "သဖြင့်", "ကြောင့်", "တိုင်အောင်"
        };

        private static readonly Regex ZeroWidthRegex = new Regex(@"[\u200B-\u200F\u2060-\u206F\uFEFF\u00AD]", RegexOptions.Compiled);
        private static readonly Regex MultiMedialClusterRegex = new Regex(@"[\u103B-\u103E]{2,}", RegexOptions.Compiled);
        private static readonly Regex PureMyanmarWordRegex = new Regex(@"^[\u1000-\u103F\u104C-\u104F]+$", RegexOptions.Compiled);
        private static readonly Regex ZawgyiCheckRegex = new Regex(@"[\u1033\u1034\u105A\u1060-\u1097]|(?:^)\u1031[\u1000-\u1021]", RegexOptions.Compiled);
        private static readonly Regex MisplacedKinziRegex = new Regex(@"(?<Cluster>[\u1000-\u1008\u1010-\u1012\u1015-\u1019\u101C\u101E][\u103B-\u103E]*[\u102B-\u1032\u1037\u1038]*)\u1004\u103A\u1039(?:(?![\u1000-\u1003\u1018\u101E])(?!(?<=တန\u1004\u103A\u1039)\u101C)|(?<=[\u102B\u102C\u102E\u1030\u1031\u1032\u1037\u1038]\u1004\u103A\u1039))", RegexOptions.Compiled);
        private static readonly Regex MedialAfterERegex = new Regex(@"\u1031([\u103B-\u103E]+)", RegexOptions.Compiled);
        private static readonly Regex SyllableCountRegex = new Regex(@"(?:\u1004\u103A\u1039)?[\u1000-\u102A\u103F\u104C-\u104F](?:\u1039[\u1000-\u1021])*[\u102B-\u103E]*(?:[\u1000-\u1021]\u103B?(?:\u103A(?!\u1039)[\u1037\u1038]*|\u1037\u103A\u1038?))*", RegexOptions.Compiled);
        private static readonly Regex ZawgyiReorderRegex = new Regex(@"(?<E>\u1031+)?(?<Ra>\u103C+)?(?<Cons>[\u1000-\u1021])(?<Sub>\u1039[\u1000-\u1021])?(?<Med>[\u103B\u103D\u103E]*)", RegexOptions.Compiled);
        private static readonly Regex BigramLineRegex = new Regex(@"^\('([^']+)',\s*'([^']+)'\)\t(\d+)$", RegexOptions.Compiled);
        private static readonly Regex VulgarOrnagaiEnglishRegex = new Regex(@"\b(arse|arsehole|ass|asshole|bitch|fuck|fucking|shit|bullshit|bastard|whore|slut|cunt|dick|cock|penis|vagina|anus|piss|masturbat\w*|porn\w*|mistress|snob)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VowelBeforeStackRegex = new Regex(@"(?<Vowel>[\u102B-\u1032\u1036]+)(?<Stack>(?:\u1039[\u1000-\u1021])+[\u103B-\u103E]*)", RegexOptions.Compiled);
        private static readonly Regex MedialAfterVowelCompilerRegex = new Regex(@"(?<Vowel>[\u102D-\u1030\u1032\u1036]+)(?<Medial>[\u103D\u103E]+)", RegexOptions.Compiled);
        private static readonly Regex VisualOrderRaInWordRegex = new Regex(@"(?<=[\u102B-\u1038\u103A\u103D\u103E])(?<ERa>\u103C+\u1031*|\u1031+\u103C+)(?<Cons>[\u1000-\u1021])(?<Sub>\u1039[\u1000-\u1021])?(?<Med>[\u103B\u103D\u103E]*)", RegexOptions.Compiled);
        private static readonly Regex VisualOrderBoundaryERaRegex = new Regex(@"(?<=^|[\s\u104A\u104B(\[""'“‘/—\-•:,;])(?<ERa>[\u1031\u103C]+)(?<Cons>[\u1000-\u1021])(?<Sub>\u1039[\u1000-\u1021])?(?<Med>[\u103B\u103D\u103E]*)", RegexOptions.Compiled);
        private static readonly Regex ImpossibleHeadwordSequenceRegex = new Regex(@"[\u1033-\u1035]|\u103E\u103A|\u103A(?!(?<=\u1014\u103A)\u102F\u1015\u103A)[\u102B-\u1032]|[\u1000-\u1003\u1005-\u1008\u100B-\u100E\u1010-\u1013\u1015-\u1018\u101F\u1021][\u103B-\u103D]*\u103E", RegexOptions.Compiled);
        private static readonly Regex ShiftedShweMojibakeRegex = new Regex(@"(?<Pfx>\u101B\u103D|\u101C\u103D|\u1019\u103D|\u101A\u103D)(?<C2>(?:\u1004\u103A\u1039)?[\u1000-\u1021][\u103B-\u103D]*)(?<Hs>\u103E+)(?<Es>\u1031+)(?<After>(?:[\u102B-\u1030\u1032\u1036-\u103A]|[\u1000-\u1021]\u103A)*)", RegexOptions.Compiled);
        private static readonly Regex ShiftedMweMojibakeRegex = new Regex(@"(?<Pfx>\u1019\u103C|\u1000\u103C|\u1001\u103C|\u1001\u103B|\u1000\u103B|\u1015\u103C)(?!(?:\u1004\u103D\u1031(?![\u1000-\u1021]\u103A)|\u101E\u103D\u1031\u1038|\u1019\u103C\u103D\u1031\u1000\u103C\u102E\u1038))(?<C2>(?:\u1004\u103A\u1039)?[\u1000-\u1021][\u103B\u103C]*)(?<Ws>\u103D+)(?<Es>\u1031+)(?<After>(?:[\u102B-\u1030\u1032\u1036-\u103A]|[\u1000-\u1021]\u103A)*)", RegexOptions.Compiled);
        private static readonly Regex KeepShiftedEVowelAfterRegex = new Regex(@"^(?:[\u102B\u102C](?:\u1000\u103A|\u1004\u103A|\u103A)|[\u1037\u1038]+$)", RegexOptions.Compiled);
        private static readonly Regex ProseMixedDigitWaRegex = new Regex(@"(?<=[\u1040-\u1049])\u101D+(?=[\u1040-\u1049]|[\s၊။)]|ပြည့်|ခု|ခန့်|ကျော်|ဖြင့်|အထိ)", RegexOptions.Compiled);
        private static readonly Regex ProseSpaceBeforeCombiningRegex = new Regex(@"[ \u00A0]+(?=[\u1037-\u103E\u1060-\u107C])", RegexOptions.Compiled);
        private static readonly KeyValuePair<string, string>[] ExactShweMweMojibakeFixes = new KeyValuePair<string, string>[]
        {
            new KeyValuePair<string, string>("ညောင်ရွမြှေို့", "ညောင်ရွှေမြို့"),
            new KeyValuePair<string, string>("ရွပှေလွှော", "ရွှေပေလွှာ"),
            new KeyValuePair<string, string>("ရွမျှေဉ်း", "ရွှေမျဉ်း"),
            new KeyValuePair<string, string>("မြကျွေင်", "မြွေကျင်"),
            new KeyValuePair<string, string>("မြပြွေဒါး", "မြွေပြဒါး"),
            new KeyValuePair<string, string>("ရွကှငျေ်", "ရွှေကျင်"),
            new KeyValuePair<string, string>("ရွင်္ဒှေဂါး", "ရွှေဒင်္ဂါး"),
            new KeyValuePair<string, string>("ရွစှနွေ်", "ရွှေစွန်"),
            new KeyValuePair<string, string>("ရွမှဉျေ်း", "ရွှေမျဉ်း"),
            new KeyValuePair<string, string>("ရွလှောင်", "ရွှေလှောင်"),
            new KeyValuePair<string, string>("ရွမှောက်", "ရွှေမှောက်"),
            new KeyValuePair<string, string>("ရွမှျှေား", "ရွှေမျှား"),
            new KeyValuePair<string, string>("ရွမျှေား", "ရွှေမျှား"),
            new KeyValuePair<string, string>("ရွမှျေား", "ရွှေများ"),
            new KeyValuePair<string, string>("ရွလွှော", "ရွှေလွှာ"),
            new KeyValuePair<string, string>("ရွဘွှေား", "ရွှေဘွား"),
            new KeyValuePair<string, string>("ရွသွှေး", "ရွှေသွေး"),
            new KeyValuePair<string, string>("ရွငွှေ", "ရွှေငွေ"),
            new KeyValuePair<string, string>("ရွမြှေ", "ရွှေမြေ"),
            new KeyValuePair<string, string>("ရွပှြေ", "ရွှေပြေ"),
            new KeyValuePair<string, string>("ရွရွှေ", "ရွှေရွှေ"),
            new KeyValuePair<string, string>("ရွလှှေ", "ရွှေလှေ"),
            new KeyValuePair<string, string>("ရွနှှေ", "ရွှေနှ"),
            new KeyValuePair<string, string>("ရွမှှော", "ရွှေမှာ"),
            new KeyValuePair<string, string>("ရွသှောလျောင်", "ရွှေသာလျောင်"),
            new KeyValuePair<string, string>("ရွပှေါသည်", "ရွှေပေါ်သည်"),
            new KeyValuePair<string, string>("ရွသှော", "ရွှေသော"),
            new KeyValuePair<string, string>("ကြကျွေ", "ကြွေကျေ"),
            new KeyValuePair<string, string>("ကြကွျေ", "ကြွေကျေ"),
            new KeyValuePair<string, string>("ကြကြွေ", "ကြွေကြွေ"),
            new KeyValuePair<string, string>("ကြမြွေ", "ကြွေမြေ"),
            new KeyValuePair<string, string>("ကြတွော", "ကြွေတော"),
            new KeyValuePair<string, string>("ကြသွော", "ကြွေသော"),
            new KeyValuePair<string, string>("ကြခွေး", "ကြွေခွေး"),
            new KeyValuePair<string, string>("ချချွေ", "ချွေချွေ"),
            new KeyValuePair<string, string>("ချခွျေ", "ချွေချွေ"),
            new KeyValuePair<string, string>("ခြခြွေ", "ခြွေခြွေ"),
            new KeyValuePair<string, string>("မြပွေး", "မြွေပွေး"),
            new KeyValuePair<string, string>("မြမွေ့", "မြွေမွေ့"),
            new KeyValuePair<string, string>("မြခွေ", "မြွေခွေ"),
            new KeyValuePair<string, string>("မြကွြေီး", "မြွေကြီး"),
            new KeyValuePair<string, string>("မြရွေမေန်း", "မြွေရေမန်း"),
            new KeyValuePair<string, string>("ကြစွေသေော", "ကြွေစေသော"),
            new KeyValuePair<string, string>("မြရွေလေဲ", "မြွေရေလဲ"),
            new KeyValuePair<string, string>("နိုးကြားတက်ကြစွေလေိုသည့်", "နိုးကြားတက်ကြွစေလိုသည\u103A\u1037"),
            new KeyValuePair<string, string>("နိုးကြားတက်ကြစွေလေိုသည့်", "နိုးကြားတက်ကြွစေလိုသည\u103A\u1037"),
            new KeyValuePair<string, string>("ရွငှကွေေို", "ရွှေငွေကို"),
            new KeyValuePair<string, string>("ရွှေစတေီ", "ရွှေစေတီ"),
            new KeyValuePair<string, string>("ရွစှေတေီ", "ရွှေစေတီ"),
            new KeyValuePair<string, string>("ဒေါ်သန်းမေတေို့မှ", "ဒေါ်သန်းမေတို့မှ"),
            new KeyValuePair<string, string>("ပြောင်းရွှေနထေိုင်ခဲ့သည်", "ပြောင်းရွှေ့နေထိုင်ခဲ့သည်"),
            new KeyValuePair<string, string>("ပြောင်းရွနှေထေိုင်ခဲ့သည်", "ပြောင်းရွှေ့နေထိုင်ခဲ့သည်"),
            new KeyValuePair<string, string>("စီရင်မီုအေုက်တွင်", "စီရင်မှုအောက်တွင်"),
            new KeyValuePair<string, string>("ကြယ်ကြမွှေု", "ကြယ်ကြွေမှု"),
            new KeyValuePair<string, string>("ဖက်ခြမွေိုး", "ဖက်ခြွေမိုး"),
            new KeyValuePair<string, string>("ကျယွေသှေုတ်", "ကျွေယေသုတ်")
        };

        private static readonly KeyValuePair<string, string>[] BookClassifierSuffixes = new KeyValuePair<string, string>[]
        {
            new KeyValuePair<string, string>("ပြည်နယ်", "ပြည်နယ်အမည် (~ပြည်နယ်)"),
            new KeyValuePair<string, string>("မြို့နယ်", "မြို့နယ်အမည် (~မြို့နယ်)"),
            new KeyValuePair<string, string>("သင်္ကန်း", "သင်္ကန်းအမျိုးအစား (~သင်္ကန်း)"),
            new KeyValuePair<string, string>("အင်္ကျီ", "အင်္ကျီအမျိုးအစား (~အင်္ကျီ)"),
            new KeyValuePair<string, string>("သီချင်း", "သီချင်း/ဂီတအမည် (~သီချင်း)"),
            new KeyValuePair<string, string>("နက္ခတ်", "နက္ခတ်အမည် (~နက္ခတ်)"),
            new KeyValuePair<string, string>("ရောဂါ", "ရောဂါအမည် (~ရောဂါ)"),
            new KeyValuePair<string, string>("ဘုရား", "ဘုရား/စေတီအမည် (~ဘုရား)"),
            new KeyValuePair<string, string>("တရား", "တရားဓမ္မအမည် (~တရား)"),
            new KeyValuePair<string, string>("စပါး", "စပါးမျိုးအမည် (~စပါး)"),
            new KeyValuePair<string, string>("ရာသီ", "ရာသီအမည် (~ရာသီ)"),
            new KeyValuePair<string, string>("ပုဆိုး", "ပုဆိုးအမျိုးအစား (~ပုဆိုး)"),
            new KeyValuePair<string, string>("နံ့သာ", "နံ့သာအမည် (~နံ့သာ)"),
            new KeyValuePair<string, string>("ဒင်္ဂါး", "ဒင်္ဂါးအမည် (~ဒင်္ဂါး)"),
            new KeyValuePair<string, string>("စေတီ", "စေတီအမည် (~စေတီ)"),
            new KeyValuePair<string, string>("ကျောက်", "ကျောက်အမျိုးအစား (~ကျောက်)"),
            new KeyValuePair<string, string>("တောင်", "တောင်အမည် (~တောင်)"),
            new KeyValuePair<string, string>("ဖောင်", "ဖောင်/လှေအမည် (~ဖောင်)"),
            new KeyValuePair<string, string>("ကျွန်း", "ကျွန်းအမည် (~ကျွန်း)"),
            new KeyValuePair<string, string>("မြင်း", "မြင်းအမျိုးအစား (~မြင်း)"),
            new KeyValuePair<string, string>("မြစ်", "မြစ်အမည် (~မြစ်)"),
            new KeyValuePair<string, string>("မြက်", "မြက်ပင်အမည် (~မြက်)"),
            new KeyValuePair<string, string>("ပင်", "အပင်အမည် (~ပင်)"),
            new KeyValuePair<string, string>("ငါး", "ငါးအမည် (~ငါး)"),
            new KeyValuePair<string, string>("ငှက်", "ငှက်အမည် (~ငှက်)"),
            new KeyValuePair<string, string>("မြွေ", "မြွေအမည် (~မြွေ)"),
            new KeyValuePair<string, string>("ပန်း", "ပန်းအမည် (~ပန်း)"),
            new KeyValuePair<string, string>("ပွဲ", "ပွဲတော်/အခမ်းအနား (~ပွဲ)"),
            new KeyValuePair<string, string>("လ", "မြန်မာလအမည် (~လ)"),
            new KeyValuePair<string, string>("မုန့်", "မုန့်အမည် (~မုန့်)"),
            new KeyValuePair<string, string>("ကျမ်း", "စာပေကျမ်းအမည် (~ကျမ်း)"),
            new KeyValuePair<string, string>("နွား", "နွားအမျိုးအစား (~နွား)"),
            new KeyValuePair<string, string>("ကောင်", "သတ္တဝါအမည် (~ကောင်)"),
            new KeyValuePair<string, string>("ဝါး", "ဝါးပင်အမျိုးအစား (~ဝါး)"),
            new KeyValuePair<string, string>("မူလ", "သတ်ပုံကျမ်း အနက်တွဲပုဒ် (~မူလ)"),
            new KeyValuePair<string, string>("အလယ်", "သတ်ပုံကျမ်း အနက်တွဲပုဒ် (~အလယ်)"),
            new KeyValuePair<string, string>("ကာလ", "အချိန်ကာလပြပုဒ် (~ကာလ)"),
            new KeyValuePair<string, string>("ဝေဝုစ်", "သတ်ပုံကျမ်း အနက်တွဲပုဒ် (~ဝေဝုစ်)"),
            new KeyValuePair<string, string>("ပစ္စည်း", "ပစ္စည်းအမျိုးအစား (~ပစ္စည်း)"),
            new KeyValuePair<string, string>("ချိန်", "အလေးချိန်ယူနစ် (~ချိန်)"),
            new KeyValuePair<string, string>("ကနား", "အဆောက်အအုံ (~ကနား)"),
            new KeyValuePair<string, string>("လှေ", "လှေ/ရေယာဉ်အမည် (~လှေ)"),
            new KeyValuePair<string, string>("ဥတု", "ရာသီဥတုအမည် (~ဥတု)"),
            new KeyValuePair<string, string>("သာမဏေ", "သာသနာဝင်အမည် (~သာမဏေ)"),
            new KeyValuePair<string, string>("ရဟန်း", "သာသနာဝင်အမည် (~ရဟန်း)"),
            new KeyValuePair<string, string>("နတ်", "နတ်ဒေဝတာအမည် (~နတ်)"),
            new KeyValuePair<string, string>("ငရဲ", "ငရဲဘုံအမည် (~ငရဲ)"),
            new KeyValuePair<string, string>("ဒေသ", "နယ်မြေဒေသအမည် (~ဒေသ)"),
            new KeyValuePair<string, string>("အချိန်", "အချိန်ကာလ (~အချိန်)"),
            new KeyValuePair<string, string>("နေ့", "နေ့ရက်အမည် (~နေ့)"),
            new KeyValuePair<string, string>("တိုင်း", "တိုင်းဒေသကြီးအမည် (~တိုင်း)"),
            new KeyValuePair<string, string>("သတ္တိ", "အရည်အချင်း (~သတ္တိ)")
        };

        private static bool StartsWithBareNegativeMa(string w)
        {
            if (string.IsNullOrEmpty(w) || w[0] != '\u1019') return false;
            Match m = SyllableCountRegex.Match(w);
            return m.Success && m.Index == 0 && m.Value == "\u1019";
        }

        private static readonly HashSet<string> CompositionalNumeralPrefixes = new HashSet<string>(StringComparer.Ordinal)
        {
            "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး", "ဆယ်"
        };

        private static bool StartsWithNumeralSyllable(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            Match m = SyllableCountRegex.Match(w);
            return m.Success && m.Index == 0 && CompositionalNumeralPrefixes.Contains(m.Value);
        }

        private static bool StartsWithBoundClausalParticle(string w)
        {
            if (string.IsNullOrEmpty(w)) return false;
            MatchCollection matches = SyllableCountRegex.Matches(w);
            int sc = matches.Count;
            if (sc < 2) return false;
            if (sc >= 3 && w.StartsWith("စရာ", StringComparison.Ordinal))
                return true;

            string s0 = matches[0].Value;
            switch (s0)
            {
                case "ခဲ့":
                case "ရဲ့":
                case "လို့":
                case "သည့်":
                case "မည့်":
                case "မယ့်":
                case "နှင့်":
                case "ကြ":
                    return true;
                case "တဲ့":
                    return !w.StartsWith("တဲ့တိုး", StringComparison.Ordinal);
                case "နဲ့":
                    return w != "နဲ့နဲ့";
                case "ဖို့":
                    return w != "ဖို့မံ" && w != "ဖို့မြေ";
                case "တွေ":
                    return !w.StartsWith("တွေဝေ", StringComparison.Ordinal) &&
                           !w.StartsWith("တွေတွေ", StringComparison.Ordinal) &&
                           !w.StartsWith("တွေမှိုင်", StringComparison.Ordinal);
                case "တယ်":
                    return !w.StartsWith("တယ်လီ", StringComparison.Ordinal);
                case "မယ်":
                    return true;
                case "တော့":
                    return !w.StartsWith("တော့တီ", StringComparison.Ordinal) &&
                           !w.StartsWith("တော့အီ", StringComparison.Ordinal) &&
                           w != "တော့ဘူး";
                case "ချင်":
                    return !w.StartsWith("ချင်ရွေး", StringComparison.Ordinal) &&
                           !w.StartsWith("ချင်ခြင်း", StringComparison.Ordinal) &&
                           !w.StartsWith("ချင်ချင်", StringComparison.Ordinal) &&
                           !w.StartsWith("ချင်ရဲ", StringComparison.Ordinal) &&
                           w != "ချင်တိုင်း" &&
                           w != "ချင်ဘူး";
                case "ဘူး":
                    return !w.StartsWith("ဘူးသီး", StringComparison.Ordinal) &&
                           !w.StartsWith("ဘူးပင်", StringComparison.Ordinal) &&
                           !w.StartsWith("ဘူးညွန့်", StringComparison.Ordinal) &&
                           !w.StartsWith("ဘူးရွက်", StringComparison.Ordinal);
                case "မှာ":
                    return !w.StartsWith("မှာကြား", StringComparison.Ordinal) &&
                           !w.StartsWith("မှာတမ်း", StringComparison.Ordinal) &&
                           !w.StartsWith("မှာယူ", StringComparison.Ordinal) &&
                           !w.StartsWith("မှာထား", StringComparison.Ordinal) &&
                           !w.StartsWith("မှာစာ", StringComparison.Ordinal);
                case "ပါ":
                    return !w.StartsWith("ပါဝင်", StringComparison.Ordinal) &&
                           !w.StartsWith("ပါတီ", StringComparison.Ordinal) &&
                           !w.StartsWith("ပါရှိ", StringComparison.Ordinal) &&
                           !w.StartsWith("ပါမောက္ခ", StringComparison.Ordinal) &&
                           !w.StartsWith("ပါရဂူ", StringComparison.Ordinal) &&
                           !w.StartsWith("ပါရမီ", StringComparison.Ordinal) &&
                           !w.StartsWith("ပါဠိ", StringComparison.Ordinal);
                case "လျက်":
                    return !w.StartsWith("လျက်ဆား", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        public static string ConvertZawgyi(string zawgyiText)
        {
            if (string.IsNullOrEmpty(zawgyiText)) return "";
            string s = zawgyiText;

            s = s.Replace("\u103D", "\u103E");
            s = s.Replace("\u103C", "\u103D");
            s = s.Replace("\u103B", "\u103C");
            s = s.Replace("\u103A", "\u103B");
            s = s.Replace("\u1039", "\u103A");

            s = s.Replace("\u1033", "\u102F");
            s = s.Replace("\u1034", "\u1030");
            s = s.Replace("\u105A", "\u102B\u103A");
            s = s.Replace("\u106A", "\u1009");
            s = s.Replace("\u106B", "\u100A");
            s = s.Replace("\u1086", "\u103F");
            s = s.Replace("\u108F", "\u1014");
            s = s.Replace("\u1090", "\u101B");
            s = s.Replace("\u1091", "\u100F\u1039\u100D");
            s = s.Replace("\u1092", "\u100B\u1039\u100C");
            s = s.Replace("\u1097", "\u100B\u1039\u100B");
            s = s.Replace("\u106E", "\u100D\u1039\u100D");
            s = s.Replace("\u106F", "\u100E\u1039\u100D");

            s = s.Replace("\u107D", "\u103B");
            s = Regex.Replace(s, @"[\u107E-\u1084]", "\u103C");
            s = s.Replace("\u1087", "\u103E");
            s = s.Replace("\u1088", "\u103E\u102F");
            s = s.Replace("\u1089", "\u103E\u1030");
            s = s.Replace("\u108A", "\u103D\u103E");
            s = s.Replace("\u108E", "\u102D\u1036");
            s = s.Replace("\u1094", "\u1037");
            s = s.Replace("\u1095", "\u1037");

            s = s.Replace("\u1064", "\uFFFF");
            s = s.Replace("\u108B", "\uFFFF\u102D");
            s = s.Replace("\u108C", "\uFFFF\u102E");
            s = s.Replace("\u108D", "\uFFFF\u1036");

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

            s = ZawgyiReorderRegex.Replace(s, delegate(Match m) {
                if (!m.Groups["E"].Success && !m.Groups["Ra"].Success)
                    return m.Value;
                string ePart = m.Groups["E"].Success ? "\u1031" : "";
                string raPart = m.Groups["Ra"].Success ? "\u103C" : "";
                return m.Groups["Cons"].Value + m.Groups["Sub"].Value + raPart + m.Groups["Med"].Value + ePart;
            });

            if (s.IndexOf('\uFFFF') >= 0)
            {
                s = Regex.Replace(s, @"(?<Base>[\u1000-\u1021][\u103B-\u103E]*\u1031?)\uFFFF", "\u1004\u103A\u1039${Base}");
                s = s.Replace("\uFFFF", "\u1004\u103A\u1039");
            }

            s = s.Replace("\u100B\u103F\u100C", "\u100B\u1039\u100C")
                 .Replace("ပြဵ", "ပြ")
                 .Replace("မဵနိုင်", "မ~နိုင်");

            return NormalizeMyanmar(s);
        }

        private static string RepairShiftedEAndMedialMojibake(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\u1031') < 0)
                return s;

            for (int i = 0; i < ExactShweMweMojibakeFixes.Length; i++)
            {
                string bad = ExactShweMweMojibakeFixes[i].Key;
                if (s.IndexOf(bad, StringComparison.Ordinal) >= 0)
                    s = s.Replace(bad, ExactShweMweMojibakeFixes[i].Value);
            }

            bool maybeShwe = s.IndexOf("\u101B\u103D", StringComparison.Ordinal) >= 0 ||
                             s.IndexOf("\u101C\u103D", StringComparison.Ordinal) >= 0 ||
                             s.IndexOf("\u1019\u103D", StringComparison.Ordinal) >= 0 ||
                             s.IndexOf("\u101A\u103D", StringComparison.Ordinal) >= 0;
            bool maybeMwe = s.IndexOf("\u1019\u103C", StringComparison.Ordinal) >= 0 ||
                            s.IndexOf("\u1000\u103C", StringComparison.Ordinal) >= 0 ||
                            s.IndexOf("\u1001\u103C", StringComparison.Ordinal) >= 0 ||
                            s.IndexOf("\u1001\u103B", StringComparison.Ordinal) >= 0 ||
                            s.IndexOf("\u1000\u103B", StringComparison.Ordinal) >= 0 ||
                            s.IndexOf("\u1015\u103C", StringComparison.Ordinal) >= 0;
            if (!maybeShwe && !maybeMwe)
                return s;

            // Pre-normalize E+medial and sort multi-medial clusters WITHOUT collapsing duplicate \u1031\u1031 / \u103E\u103E / \u103D\u103D!
            s = MedialAfterERegex.Replace(s, "$1\u1031");
            s = MultiMedialClusterRegex.Replace(s, delegate(Match m)
            {
                string meds = m.Value;
                var sb = new StringBuilder(meds.Length);
                char[] order = { '\u103B', '\u103C', '\u103D', '\u103E' };
                for (int oi = 0; oi < order.Length; oi++)
                {
                    char mc = order[oi];
                    for (int ci = 0; ci < meds.Length; ci++)
                        if (meds[ci] == mc) sb.Append(mc);
                }
                return sb.ToString();
            });

            for (int i = 0; i < ExactShweMweMojibakeFixes.Length; i++)
            {
                string bad = ExactShweMweMojibakeFixes[i].Key;
                if (s.IndexOf(bad, StringComparison.Ordinal) >= 0)
                    s = s.Replace(bad, ExactShweMweMojibakeFixes[i].Value);
            }

            if (maybeShwe)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    string next = ShiftedShweMojibakeRegex.Replace(s, delegate(Match m)
                    {
                        string pfx = m.Groups["Pfx"].Value;
                        string c2 = m.Groups["C2"].Value;
                        string hs = m.Groups["Hs"].Value;
                        string es = m.Groups["Es"].Value;
                        string after = m.Groups["After"].Value;
                        bool keepH = hs.Length >= 2;
                        bool keepE = es.Length >= 2 || KeepShiftedEVowelAfterRegex.IsMatch(after);
                        return pfx + "\u103E\u1031" + c2 + (keepH ? "\u103E" : "") + (keepE ? "\u1031" : "") + after;
                    });
                    if (next == s) break;
                    s = next;
                }
            }

            if (maybeMwe)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    string next = ShiftedMweMojibakeRegex.Replace(s, delegate(Match m)
                    {
                        string pfx = m.Groups["Pfx"].Value;
                        string c2 = m.Groups["C2"].Value;
                        string ws = m.Groups["Ws"].Value;
                        string es = m.Groups["Es"].Value;
                        string after = m.Groups["After"].Value;
                        bool keepW = ws.Length >= 2;
                        bool keepE = es.Length >= 2 || KeepShiftedEVowelAfterRegex.IsMatch(after);
                        return pfx + "\u103D\u1031" + c2 + (keepW ? "\u103D" : "") + (keepE ? "\u1031" : "") + after;
                    });
                    if (next == s) break;
                    s = next;
                }
            }

            return s;
        }

        public static string RepairWinInnwaGlitches(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = ZeroWidthRegex.Replace(raw, "").Trim();
            s = s.Replace("`", "");
            s = s.Replace("နပုလ’ိင်", "နပုလ္လိင်");
            s = s.Replace("နပုလ'ိင်", "နပုလ္လိင်");
            s = s.Replace("သဂြØုဟ်", "သင်္ဂြိုဟ်");
            s = s.Replace("ဗျ ဂ္ဃ", "ဗျဂ္ဃ");
            s = s.Replace("ဂိဇ္စျကုဋ်ုတောင်", "ဂိဇ္ဈကုဋ်တောင်");
            s = s.Replace("သရဇ္စျာယ်", "သရဇ္ဈာယ်");
            s = s.Replace("လက်ကတော့ထိုး...", "လက်ကတော့ထိုး-");
            s = s.Replace("ဣတ္တိလိင်", "ဣတ္ထိလိင်");
            s = s.Replace("ဣဒ္မိပါဒ်", "ဣဒ္ဓိပါဒ်");
            s = s.Replace("ဥု့ေဩာင်း", "ဥဩ");
            s = s.Replace("သသင်္ကရိုက်", "သင်္သကရိုက်");
            s = s.Replace("အကျ ရှေ့တွဲ", "အင်္ကျီရှေ့တွဲ");
            s = s.Replace("ကင်းထောက်လေယဉ်", "ကင်းထောက်လေယာဉ်");
            s = s.Replace("ဥပဇဈာယ်", "ဥပဇ္ဈာယ်");
            s = s.Replace("ဂှေးစေ့", "ဂွေးစေ့");
            s = s.Replace("လွဲချွှတ်", "လွဲချွတ်");
            s = s.Replace("တမင်ရှိှုု့", "တမင်ရှို့").Replace("တမင်ရှိှု့", "တမင်ရှို့");
            s = Regex.Replace(s, @"အင်္ကျ(?![\u102B-\u103A])", "အင်္ကျီ");
            s = RepairShiftedEAndMedialMojibake(s);
            return s;
        }

        private static string RepairVisualOrderERa(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            if (s.IndexOf("ခိုင်မြဲြုခင်း", StringComparison.Ordinal) >= 0)
                s = s.Replace("ခိုင်မြဲြုခင်း", "ခိုင်မြဲခြင်း");
            if (s.IndexOf("နှင်ြ ", StringComparison.Ordinal) >= 0)
                s = s.Replace("နှင်ြ ", "နှင့် ");
            if (s.IndexOf('\u103C') >= 0)
            {
                s = VisualOrderRaInWordRegex.Replace(s, delegate(Match m)
                {
                    string era = m.Groups["ERa"].Value;
                    bool hasE = era.IndexOf('\u1031') >= 0;
                    return m.Groups["Cons"].Value + m.Groups["Sub"].Value + "\u103C" + m.Groups["Med"].Value + (hasE ? "\u1031" : "");
                });
            }
            if (s.IndexOf('\u103C') >= 0 || s.IndexOf('\u1031') >= 0)
            {
                s = VisualOrderBoundaryERaRegex.Replace(s, delegate(Match m)
                {
                    string era = m.Groups["ERa"].Value;
                    bool hasE = era.IndexOf('\u1031') >= 0;
                    bool hasRa = era.IndexOf('\u103C') >= 0;
                    return m.Groups["Cons"].Value + m.Groups["Sub"].Value + (hasRa ? "\u103C" : "") + m.Groups["Med"].Value + (hasE ? "\u1031" : "");
                });
            }
            return s;
        }

        public static string NormalizeMyanmar(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = RepairWinInnwaGlitches(raw);

            s = s.Replace("\u100B\u103F\u100C", "\u100B\u1039\u100C");
            s = RepairVisualOrderERa(s);
            s = s.Replace("\u1025\u102E", "\u1026");
            if (s.IndexOf('\u1026') >= 0)
            {
                s = Regex.Replace(s, @"\u1026[\u102D\u102E]+", "\u1026");
            }
            if (s.IndexOf('\u1039') >= 0)
            {
                s = VowelBeforeStackRegex.Replace(s, "${Stack}${Vowel}");
            }
            s = s.Replace("\u101E\u1039\u101E", "\u103F");
            s = Regex.Replace(s, @"\u103F{2,}", "\u103F");
            s = Regex.Replace(s, @"(?<!\u1039)\u1005\u103B|\u1008\u103B", "\u1008");
            s = s.Replace("\u101E\u103C\u1031\u102C\u103A", "\u102A");
            s = s.Replace("\u1029\u1031\u102C\u103A", "\u102A");
            s = Regex.Replace(s, @"\u101E\u103C(?![\u102B-\u103E])", "\u1029");
            s = Regex.Replace(s, @"(?<Base>[\u1000-\u1021])(?<Medial>[\u103B\u103C])(?:[\u102B\u102C]\u103A|\u103A[\u102B\u102C])", "${Base}\u103A${Medial}\u102C");
            if (s.IndexOf("\u103A\u103C", StringComparison.Ordinal) >= 0)
            {
                s = Regex.Replace(s, @"\u103A\u103C([\u1000-\u1021])", "\u103A$1\u103C");
            }

            if (s.IndexOf("\u1004\u103A\u1039", StringComparison.Ordinal) >= 0)
            {
                s = MisplacedKinziRegex.Replace(s, "\u1004\u103A\u1039${Cluster}");
            }

            if (s.IndexOf('\u1031') >= 0)
            {
                s = MedialAfterERegex.Replace(s, "$1\u1031");
            }
            if (s.IndexOf('\u103D') >= 0 || s.IndexOf('\u103E') >= 0)
            {
                s = MedialAfterVowelCompilerRegex.Replace(s, "${Medial}${Vowel}");
            }

            s = Regex.Replace(s, @"\u1040(?=[\u1000-\u103F])|(?<=[\u1000-\u103F])\u1040", "\u101D");
            s = s.Replace("\u1025\u103A", "\u1009\u103A");
            s = s.Replace("\u1025\u102C", "\u1009\u102C");
            s = s.Replace("\u1025\u1039", "\u1009\u1039");
            s = s.Replace("\u1025\u103E", "\u1009\u103E");
            s = s.Replace("\u1025\u1037\u103A", "\u1009\u103A\u1037");
            s = s.Replace("\u104D\u103A", "\u104D");

            // Preserve official Tall AA 'ါ' on 'ဒ္ဒါ' (သဒ္ဒါ), 'ဒ္ဓါ' (သဒ္ဓါ), 'ဂ္ဂါ' (မဂ္ဂါမဂ္ဂ/မဂ္ဂါဝပ်), 'ဒွါ' (ဒွါရ/ဒွါဒသမ), and Pali 'ဇိဝှါ' per 462-page သတ်ပုံကျမ်း!
            s = Regex.Replace(s, @"(?<=(?:[\u1000\u1003\u1005-\u1011\u1014\u1016-\u101C\u101E-\u1021]|(?<!\u1012\u1039)\u1013|(?!\u1012\u103D|\u101D\u103E)[\u1000-\u1021][\u103B-\u103E]+)\u1031?)\u102B", "\u102C");
            s = Regex.Replace(s, @"(?<=(?:(?<!\u1004\u103A)\u1039[\u1001\u1004\u1015\u101D]|(?<!\u1004\u103A|\u1002)\u1039\u1002|(?<!\u1004\u103A|\u1012)\u1039\u1012)\u1031?)\u102B", "\u102C");
            s = Regex.Replace(s, @"(?<=(?:(?<!\u1039)|\u1004\u103A\u1039)(?:[\u1001\u1002\u1004\u1012\u1015\u101D]|\u1012\u103D)\u1031?)\u102C(?!တမ)", "\u102B");
            s = s.Replace("သဒ္ဒာ", "သဒ္ဒါ").Replace("သဒ္ဓာ", "သဒ္ဓါ").Replace("ဇိဝှာ", "ဇိဝှါ");

            s = s.Replace("\u103E\u103D", "\u103D\u103E");
            s = s.Replace("\u103D\u103B", "\u103B\u103D");
            s = s.Replace("\u103D\u103C", "\u103C\u103D");
            s = s.Replace("\u103E\u103B", "\u103B\u103E");
            s = s.Replace("\u103E\u103C", "\u103C\u103E");
            s = s.Replace("\u102F\u102D", "\u102D\u102F");
            s = s.Replace("\u1030\u102D", "\u102D\u1030");
            s = s.Replace("\u102F\u102E", "\u102E\u102F");
            s = s.Replace("\u102D\u102E", "\u102E").Replace("\u102E\u102D", "\u102E");
            s = s.Replace("\u102F\u1030", "\u1030").Replace("\u1030\u102F", "\u1030");
            s = s.Replace("\u1036\u102D", "\u102D\u1036");
            s = s.Replace("\u1036\u102E", "\u102E\u1036");
            s = s.Replace("\u1036\u102F", "\u102F\u1036");
            s = s.Replace("\u1036\u1030", "\u1030\u1036");
            s = s.Replace("\u1019\u103E\u102F\u1036", "\u1019\u103E\u102F\u1014\u103A");
            s = s.Replace("\u1037\u102C", "\u102C\u1037");
            s = s.Replace("\u1037\u102B", "\u102B\u1037");
            s = s.Replace("\u1037\u1036", "\u1036\u1037");
            s = s.Replace("\u1037\u103A", "\u103A\u1037");
            s = s.Replace("\u1037\u102F", "\u102F\u1037");
            s = s.Replace("\u102C\u1038\u102C", "\u102C\u1038").Replace("\u102B\u1038\u102B", "\u102B\u1038");
            s = s.Replace("\u103A\u1037\u103A", "\u103A\u1037").Replace("\u1037\u103A\u1037", "\u103A\u1037");
            s = s.Replace("\u1037\u1038", "\u1038").Replace("\u1038\u1037", "\u1038");
            s = Regex.Replace(s, @"([\u102B-\u1032\u1036-\u103E])\1+", "$1");

            return s;
        }

        private static string SanitizeOrnagaiPosTag(string rawPos)
        {
            if (string.IsNullOrWhiteSpace(rawPos))
                return string.Empty;

            string p = Regex.Replace(rawPos, @"[\u200B-\u200F\uFEFF\uF000-\uF8FF]+", "");
            p = Regex.Replace(p, @"\s+", " ").Trim(' ', '.', ',', ';', '=');
            if (p.Length == 0)
                return string.Empty;

            if (p.StartsWith("also ", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("null ", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("(forming", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("(French", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("[C]", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "gp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "sa", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // Repair upstream Ornagai 'm'/'in' -> 'idm' export corruptions & typos
            p = p.Replace("idmnidmef", "indef")
                 .Replace("idmnfidml", "idm, infml")
                 .Replace("idm (idmnf", "idm, infml")
                 .Replace("idm (fidml", "idm, infml")
                 .Replace("idm (Bridm", "idm, Brit")
                 .Replace("(IDM)(infm", "idm, infml")
                 .Replace("IDM(infml)", "idm, infml")
                 .Replace("IDM(infml", "idm, infml")
                 .Replace("IDM (infml", "idm, infml")
                 .Replace("IDM(Brit)", "idm, Brit")
                 .Replace("IDM (sayin", "idm")
                 .Replace("[PHRV](inf", "phrv, infml")
                 .Replace("phrv (idmn", "phrv")
                 .Replace("phrv (Brid", "phrv, Brit")
                 .Replace("n (Bridmt", "n, Brit")
                 .Replace("Bridmt", "Brit")
                 .Replace("idmnterj", "interj")
                 .Replace("coidmb for", "comb form")
                 .Replace("coidmb", "comb")
                 .Replace("Aidmj", "adj")
                 .Replace("syidmb", "symb")
                 .Replace("cheidm", "chem")
                 .Replace("usu sidmng", "usu sing")
                 .Replace("sidmng", "sing")
                 .Replace("sidmgn", "sing")
                 .Replace("fidml", "fml")
                 .Replace("model v", "modal v")
                 .Replace("interf adv", "interrog adv");

            if (string.Equals(p, "perp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "prap", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "preps", StringComparison.OrdinalIgnoreCase))
                p = "prep";
            else if (string.Equals(p, "adh", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(p, "a", StringComparison.OrdinalIgnoreCase))
                p = "adj";

            // Flatten nested brackets so '[pos]' rendering never produces '[[...]]'
            p = p.Replace("n[pl]", "n, pl")
                 .Replace("n(pl)", "n, pl")
                 .Replace("n (pl)", "n, pl")
                 .Replace("n,pl", "n, pl")
                 .Replace("n[sing]", "n, sg")
                 .Replace("n[sg]", "n, sg")
                 .Replace("[sg]", "sg")
                 .Replace("v[pt]", "v, pt")
                 .Replace("v[pp]", "v, pp")
                 .Replace("v[ing]", "v, -ing")
                 .Replace("v[ping]", "v, -ing")
                 .Replace("v[sg]", "v, sg")
                 .Replace("v[ag]", "v, sg")
                 .Replace("v[int]", "vi")
                 .Replace("adj[com]", "adj, comp")
                 .Replace("adj[er]", "adj, comp")
                 .Replace("adj[sup]", "adj, superl")
                 .Replace("adj[sp]", "adj, superl")
                 .Replace("adj[est]", "adj, superl")
                 .Replace("adv[com]", "adv, comp")
                 .Replace("adv[sup]", "adv, superl");

            // Strip any VARCHAR(10)-truncated open parenthesis tail (e.g. 'n(esp sing' -> 'n', 'int(archai' -> 'interj')
            int openParen = p.IndexOf('(');
            while (openParen >= 0)
            {
                int closeParen = p.IndexOf(')', openParen);
                if (closeParen < 0)
                {
                    p = p.Substring(0, openParen).Trim(' ', ',', ';', '-');
                    break;
                }
                openParen = p.IndexOf('(', closeParen + 1);
            }

            if (string.Equals(p, "int", StringComparison.OrdinalIgnoreCase))
                p = "interj";
            else if (string.Equals(p, "IDM", StringComparison.Ordinal))
                p = "idm";
            else if (string.Equals(p, "PHRV", StringComparison.Ordinal))
                p = "phrv";

            p = p.Replace("[", "").Replace("]", "").Trim(' ', '.', ',', ';', '-', '=');
            p = Regex.Replace(p, @"\s{2,}", " ");
            return p;
        }

        private static int CountSyllables(string word)
        {
            if (string.IsNullOrEmpty(word)) return 0;
            return SyllableCountRegex.Matches(word).Count;
        }

        private static bool IsCleanHeadword(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length > 75) return false;
            if (KnownWrongBlacklist.Contains(word)) return false;
            if (ZawgyiCheckRegex.IsMatch(word)) return false;
            if (!PureMyanmarWordRegex.IsMatch(word)) return false;
            if (ImpossibleHeadwordSequenceRegex.IsMatch(word)) return false;
            char c0 = word[0];
            if (c0 >= '\u102B' && c0 <= '\u103E') return false;

            // Reject crowd-sourced typos with dropped 'ှ' on nominalizer '-မှု' (e.g. 'ပတ်သက်မု', 'ဆောင်ရွက်မှူ') and fused particle+pronoun 'ကကျွန်...'
            if (word.StartsWith("ကကျွန်", StringComparison.Ordinal) || word == "ကကျွတ်")
            {
                return false;
            }
            if (word.Length >= 4 && (word.EndsWith("မု", StringComparison.Ordinal) || word.EndsWith("မှူ", StringComparison.Ordinal)) &&
                word != "ဂမု" && word != "ကမူ")
            {
                return false;
            }
            if (word.EndsWith("စံနစ်", StringComparison.Ordinal))
            {
                return false;
            }

            // Enforce authentic Pali/Myanmar pat-sint stacking rules (rejects *အဇ္စျတ္တ, *မုည္စလိန္ဒာ, *ဝထ္ထု, *အဓ္ဓမ, *ပဉ္ဖင်း)
            if (word.IndexOf('\u1039') >= 0)
            {
                foreach (Match m in PatSintCheckRegex.Matches(word))
                {
                    string pair = m.Groups[1].Value + m.Groups[2].Value;
                    if (!ValidPatSintPairs.Contains(pair))
                        return false;
                }
            }
            if (MyanmarSpellChecker.Core.Nlp.UnicodeNormalizer.HasLookalikeError(word, out _, out _))
                return false;
            return true;
        }

        private static bool HasTrailingInflectionParticle(string w)
        {
            return w.EndsWith("သည်", StringComparison.Ordinal) ||
                   w.EndsWith("တယ်", StringComparison.Ordinal) ||
                   w.EndsWith("မည်", StringComparison.Ordinal) ||
                   w.EndsWith("မယ်", StringComparison.Ordinal) ||
                   w.EndsWith("ကို", StringComparison.Ordinal) ||
                   w.EndsWith("တွင်", StringComparison.Ordinal) ||
                   w.EndsWith("များ", StringComparison.Ordinal) ||
                   w.EndsWith("တွေ", StringComparison.Ordinal) ||
                   w.EndsWith("နှင့်", StringComparison.Ordinal) ||
                   w.EndsWith("နဲ့", StringComparison.Ordinal) ||
                   w.EndsWith("သော", StringComparison.Ordinal) ||
                   w.EndsWith("သည့်", StringComparison.Ordinal) ||
                   w.EndsWith("ဖြင့်", StringComparison.Ordinal) ||
                   w.EndsWith("ကြောင့်", StringComparison.Ordinal) ||
                   w.EndsWith("ပြီ", StringComparison.Ordinal);
        }

        private static void Upsert(
            Dictionary<string, EntryItem> map,
            List<EntryItem> homographEntries,
            HashSet<string> officialThatponSet,
            string rawWord,
            string rawCue,
            string pos,
            bool isOfficialBook,
            bool allowMultipleCues)
        {
            string w = NormalizeMyanmar(rawWord);
            if (!IsCleanHeadword(w)) return;
            string cue = string.IsNullOrEmpty(rawCue) ? "" : rawCue.Trim().Replace("...", "~");

            if (isOfficialBook)
            {
                officialThatponSet.Add(w);
            }

            EntryItem existing;
            if (allowMultipleCues && !string.IsNullOrEmpty(cue) && map.TryGetValue(w, out existing) &&
                !string.IsNullOrEmpty(existing.MeaningCue) && existing.MeaningCue != cue)
            {
                if (isOfficialBook) existing.IsOfficialThatpon = true;
                bool alreadyInHomographs = false;
                for (int i = 0; i < homographEntries.Count; i++)
                {
                    if (homographEntries[i].Word == w && homographEntries[i].MeaningCue == cue)
                    {
                        alreadyInHomographs = true;
                        if (isOfficialBook) homographEntries[i].IsOfficialThatpon = true;
                        break;
                    }
                }
                if (!alreadyInHomographs)
                {
                    homographEntries.Add(new EntryItem
                    {
                        Word = w,
                        MeaningCue = cue,
                        PosTag = string.IsNullOrEmpty(pos) ? existing.PosTag : pos,
                        IsOfficialThatpon = isOfficialBook || existing.IsOfficialThatpon
                    });
                }
                return;
            }

            EntryItem item;
            if (!map.TryGetValue(w, out item))
            {
                item = new EntryItem
                {
                    Word = w,
                    MeaningCue = cue,
                    PosTag = pos,
                    IsOfficialThatpon = isOfficialBook
                };
                map[w] = item;
            }
            else
            {
                if (string.IsNullOrEmpty(item.MeaningCue) && !string.IsNullOrEmpty(cue))
                    item.MeaningCue = cue;
                if (string.IsNullOrEmpty(item.PosTag) && !string.IsNullOrEmpty(pos))
                    item.PosTag = pos;
                if (isOfficialBook)
                    item.IsOfficialThatpon = true;
            }
        }

        private static void AddCoreHomophoneCues(
            Dictionary<string, EntryItem> map,
            List<EntryItem> homographEntries,
            HashSet<string> officialThatponSet)
        {
            string[][] pairs = new string[][]
            {
                new string[] { "ကျောင်း", "စာသင်~၊ ဘုန်းကြီး~", "N" },
                new string[] { "ကြောင်း", "အကြောင်းအရာ၊ မျဉ်း~", "N" },
                new string[] { "ကျမ်း", "စာပေကျမ်းဂန်၊ သတ်ပုံ~", "N" },
                new string[] { "ကြမ်း", "~တမ်းသည်၊ ကြမ်းပြင်၊ မူ~", "ADJ" },
                new string[] { "ကြီး", "~မားသည်၊ လူ~", "ADJ" },
                new string[] { "ကျီး", "~ကန်းငှက်၊ စပါး~", "N" },
                new string[] { "ကျောက်", "~စိမ်း၊ ~မျက်ရတနာ", "N" },
                new string[] { "ကြောက်", "~ရွံ့ထိတ်လန့်သည်", "V" },
                new string[] { "ခန်း", "အခန်းကဏ္ဍ၊ အိပ်~", "N" },
                new string[] { "ခမ်း", "အခမ်းအနား၊ ရေ~ခြောက်သည်", "V" },
                new string[] { "ကံ", "~ကြမ္မာ၊ ~ကောင်းသည်", "N" },
                new string[] { "ကန်", "ရေ~၊ ခြေဖြင့်~သည်", "N" },
                new string[] { "လမ်း", "သွားလာရာ~ကြောင်း", "N" },
                new string[] { "လန်း", "စိတ်ရွှင်~သည်၊ ~ဆန်းသည်", "V" },
                new string[] { "လှမ်း", "ခြေ~သည်၊ ~ယူသည်", "V" },
                new string[] { "လွမ်း", "သတိရ~ဆွတ်သည်", "V" },
                new string[] { "ချည်", "ကြိုး~နှောင်သည်၊ အပ်~", "V" },
                new string[] { "ချဉ်", "အရသာ~သည်၊ ~ပေါင်ရွက်", "ADJ" },
                new string[] { "မီ", "အချိန်~၊ စံချိန်~၊ ခေတ်~", "V" },
                new string[] { "မှီ", "~ခိုသည်၊ ~တွယ်သည်၊ ~ငြမ်းပြု", "V" },
                new string[] { "ယာဉ်", "မော်တော်~၊ ရထား~၊ လေ~", "N" },
                new string[] { "ယဉ်", "~ကျေးသိမ်မွေ့သည်", "ADJ" }
            };

            for (int i = 0; i < pairs.Length; i++)
            {
                Upsert(map, homographEntries, officialThatponSet, pairs[i][0], pairs[i][1], pairs[i][2], true, false);
            }
        }

        private static string MapOrnagaiPos(string rawPos)
        {
            if (string.IsNullOrEmpty(rawPos)) return "";
            string p = rawPos.Trim().ToLowerInvariant();
            if (p == "n" || p.StartsWith("n.") || p == "noun") return "N";
            if (p == "v" || p.StartsWith("v.") || p == "vt" || p == "vi" || p == "verb") return "V";
            if (p == "adj" || p.StartsWith("adj.")) return "ADJ";
            if (p == "adv" || p.StartsWith("adv.")) return "ADV";
            if (p == "pron" || p.StartsWith("pron.")) return "PRON";
            if (p == "conj" || p.StartsWith("conj.")) return "CONJ";
            if (p == "prep" || p.StartsWith("prep.")) return "POST";
            return "";
        }

        private static void ParseSimpleCorrectionYaml(
            string yamlPath,
            string sourceLabel,
            Dictionary<string, EntryItem> map,
            HashSet<string> officialThatponSet,
            List<RuleItem> outRules)
        {
            if (!File.Exists(yamlPath)) return;

            Regex incRx = new Regex(@"^\s*-\s*incorrect:\s*""([^""]+)""", RegexOptions.Compiled);
            Regex corSingleRx = new Regex(@"^\s*correct:\s*""([^""]+)""", RegexOptions.Compiled);
            Regex corListItemRx = new Regex(@"^\s*-\s*""([^""]+)""", RegexOptions.Compiled);
            Regex contextSkipRx = new Regex(@"^\s*(followed_by|context):", RegexOptions.Compiled);

            string currentIncorrect = "";
            var currentCandidates = new List<string>();
            bool inCorrectList = false;
            bool isContextual = false;

            string[] lines = File.ReadAllLines(yamlPath, Encoding.UTF8);
            for (int i = 0; i <= lines.Length; i++)
            {
                string line = (i < lines.Length) ? lines[i] : "  - incorrect: \"\"";
                Match mInc = incRx.Match(line);
                if (mInc.Success)
                {
                    if (!string.IsNullOrEmpty(currentIncorrect) && !isContextual && currentCandidates.Count > 0)
                    {
                        CommitYamlRule(currentIncorrect, currentCandidates, sourceLabel, map, officialThatponSet, outRules);
                    }
                    currentIncorrect = mInc.Groups[1].Value.Trim();
                    currentCandidates.Clear();
                    inCorrectList = false;
                    isContextual = false;
                    continue;
                }

                if (string.IsNullOrEmpty(currentIncorrect)) continue;

                if (contextSkipRx.IsMatch(line))
                {
                    isContextual = true;
                    inCorrectList = false;
                    continue;
                }

                Match mCorSingle = corSingleRx.Match(line);
                if (mCorSingle.Success)
                {
                    currentCandidates.Add(mCorSingle.Groups[1].Value.Trim());
                    inCorrectList = false;
                    continue;
                }

                if (line.Trim() == "correct:")
                {
                    inCorrectList = true;
                    continue;
                }

                if (inCorrectList)
                {
                    Match mListItem = corListItemRx.Match(line);
                    if (mListItem.Success)
                    {
                        currentCandidates.Add(mListItem.Groups[1].Value.Trim());
                    }
                    else if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        inCorrectList = false;
                    }
                }
            }
        }

        private static void CommitYamlRule(
            string rawWrong,
            List<string> rawCandidates,
            string sourceLabel,
            Dictionary<string, EntryItem> map,
            HashSet<string> officialThatponSet,
            List<RuleItem> outRules)
        {
            if (rawWrong.IndexOf(' ') >= 0) return;
            string wf = NormalizeMyanmar(rawWrong);
            if (string.IsNullOrEmpty(wf) || !PureMyanmarWordRegex.IsMatch(wf) || officialThatponSet.Contains(wf))
                return;

            int wfSyls = CountSyllables(wf);
            if (map.ContainsKey(wf))
                return;

            string chosenCorrect = "";
            for (int i = 0; i < rawCandidates.Count; i++)
            {
                string cand = NormalizeMyanmar(rawCandidates[i]);
                if (cand != wf && map.ContainsKey(cand) && CountSyllables(cand) == wfSyls)
                {
                    chosenCorrect = cand;
                    break;
                }
            }
            if (string.IsNullOrEmpty(chosenCorrect))
            {
                for (int i = 0; i < rawCandidates.Count; i++)
                {
                    string cand = NormalizeMyanmar(rawCandidates[i]);
                    if (cand != wf && map.ContainsKey(cand) && Math.Abs(CountSyllables(cand) - wfSyls) <= 1 && CountSyllables(cand) >= wfSyls)
                    {
                        chosenCorrect = cand;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(chosenCorrect) && rawCandidates.Count > 0 && wfSyls >= 2)
            {
                string cand0 = NormalizeMyanmar(rawCandidates[0]);
                if (cand0 != wf && PureMyanmarWordRegex.IsMatch(cand0) &&
                    Math.Abs(CountSyllables(cand0) - wfSyls) <= 1 &&
                    !HasTrailingInflectionParticle(cand0))
                {
                    chosenCorrect = cand0;
                }
            }

            if (!string.IsNullOrEmpty(chosenCorrect))
            {
                outRules.Add(new RuleItem
                {
                    WrongForm = wf,
                    CorrectForm = chosenCorrect,
                    Category = "Spelling",
                    Explanation = string.Format("{0} စံသတ်ပုံအရ '{1}' အစား '{2}' ဟု ရေးသားရမည်။", sourceLabel, wf, chosenCorrect)
                });
            }
        }

        private static void ParseMinedLoanWordsYaml(
            string yamlPath,
            HashSet<string> officialThatponSet,
            Dictionary<string, EntryItem> map,
            List<EntryItem> homographEntries,
            List<RuleItem> outRules)
        {
            if (!File.Exists(yamlPath)) return;
            Regex varRx = new Regex(@"^\s*-\s*variant:\s*(\S+)", RegexOptions.Compiled);
            Regex stdRx = new Regex(@"^\s*standard:\s*(\S+)", RegexOptions.Compiled);
            string curVar = "";
            foreach (string line in File.ReadAllLines(yamlPath, Encoding.UTF8))
            {
                Match mv = varRx.Match(line);
                if (mv.Success)
                {
                    curVar = NormalizeMyanmar(mv.Groups[1].Value.Trim('"', '\''));
                    continue;
                }
                Match ms = stdRx.Match(line);
                if (ms.Success && !string.IsNullOrEmpty(curVar))
                {
                    string std = NormalizeMyanmar(ms.Groups[1].Value.Trim('"', '\''));
                    if (!string.IsNullOrEmpty(std) && curVar != std &&
                        !officialThatponSet.Contains(curVar) &&
                        PureMyanmarWordRegex.IsMatch(curVar) &&
                        PureMyanmarWordRegex.IsMatch(std) &&
                        CountSyllables(curVar) >= 2 &&
                        !HasTrailingInflectionParticle(std))
                    {
                        Upsert(map, homographEntries, officialThatponSet, std, "ခေတ်သုံး စံဝေါဟာရ", "N/V", officialThatponSet.Contains(std), false);
                        outRules.Add(new RuleItem
                        {
                            WrongForm = curVar,
                            CorrectForm = std,
                            Category = "Spelling",
                            Explanation = string.Format("စံသတ်ပုံအရ '{0}' အစား '{1}' ဟု ရေးသားရမည်။", curVar, std)
                        });
                    }
                    curVar = "";
                }
            }
        }

        private static void ParseMedialUnconditionalYaml(
            string yamlPath,
            Dictionary<string, EntryItem> map,
            HashSet<string> officialThatponSet,
            List<RuleItem> outRules)
        {
            if (!File.Exists(yamlPath)) return;
            string[] lines = File.ReadAllLines(yamlPath, Encoding.UTF8);
            bool inUncond = false;
            Regex kvSingleRx = new Regex(@"^\s*""([^""]+)"":\s*""([^""]+)""", RegexOptions.Compiled);
            Regex keyListStartRx = new Regex(@"^\s*""([^""]+)"":\s*$", RegexOptions.Compiled);
            Regex listItemRx = new Regex(@"^\s*-\s*""([^""]+)""", RegexOptions.Compiled);

            string pendingKey = "";
            var pendingList = new List<string>();

            for (int i = 0; i <= lines.Length; i++)
            {
                string line = (i < lines.Length) ? lines[i] : "contextual:";
                string trimmed = line.Trim();
                if (trimmed == "unconditional:" || trimmed == "colloquial_contractions:")
                {
                    if (!string.IsNullOrEmpty(pendingKey) && pendingList.Count > 0)
                    {
                        CommitYamlRule(pendingKey, pendingList, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, outRules);
                    }
                    pendingKey = "";
                    pendingList.Clear();
                    inUncond = true;
                    continue;
                }
                if (trimmed == "contextual:")
                {
                    if (!string.IsNullOrEmpty(pendingKey) && pendingList.Count > 0)
                    {
                        CommitYamlRule(pendingKey, pendingList, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, outRules);
                    }
                    pendingKey = "";
                    pendingList.Clear();
                    inUncond = false;
                    continue;
                }
                if (!inUncond || string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                    continue;

                Match mSingle = kvSingleRx.Match(line);
                if (mSingle.Success)
                {
                    if (!string.IsNullOrEmpty(pendingKey) && pendingList.Count > 0)
                    {
                        CommitYamlRule(pendingKey, pendingList, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, outRules);
                        pendingKey = "";
                        pendingList.Clear();
                    }
                    var oneList = new List<string>();
                    oneList.Add(mSingle.Groups[2].Value);
                    CommitYamlRule(mSingle.Groups[1].Value, oneList, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, outRules);
                    continue;
                }

                Match mStart = keyListStartRx.Match(line);
                if (mStart.Success)
                {
                    if (!string.IsNullOrEmpty(pendingKey) && pendingList.Count > 0)
                    {
                        CommitYamlRule(pendingKey, pendingList, "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, outRules);
                    }
                    pendingKey = mStart.Groups[1].Value;
                    pendingList.Clear();
                    continue;
                }

                Match mItem = listItemRx.Match(line);
                if (mItem.Success && !string.IsNullOrEmpty(pendingKey))
                {
                    pendingList.Add(mItem.Groups[1].Value);
                }
            }
        }

        private static void ParseCompoundConfusionYaml(
            string yamlPath,
            Dictionary<string, EntryItem> map,
            HashSet<string> officialThatponSet,
            List<RuleItem> outRules)
        {
            if (!File.Exists(yamlPath)) return;
            Regex patKeyRx = new Regex(@"^\s{2}""([^""]+)"":", RegexOptions.Compiled);
            Regex wrongRx = new Regex(@"^\s{4}wrong:\s*""([^""]+)""", RegexOptions.Compiled);
            Regex corrRx = new Regex(@"^\s{4}correct:\s*""([^""]+)""", RegexOptions.Compiled);

            string curPat = "";
            string curWrong = "";
            bool inSection = false;

            foreach (string rawLine in File.ReadAllLines(yamlPath, Encoding.UTF8))
            {
                string line = Regex.Unescape(rawLine);
                string trimmed = line.Trim();
                if (trimmed == "ha_htoe_compounds:" || trimmed == "aspirated_compounds:" || trimmed == "consonant_confusion_compounds:")
                {
                    inSection = true;
                    curPat = "";
                    curWrong = "";
                    continue;
                }
                if (trimmed == "suffix_confusion_replacements:")
                {
                    inSection = false;
                    continue;
                }
                if (!inSection) continue;

                Match mp = patKeyRx.Match(line);
                if (mp.Success)
                {
                    curPat = NormalizeMyanmar(mp.Groups[1].Value);
                    curWrong = "";
                    continue;
                }
                Match mw = wrongRx.Match(line);
                if (mw.Success)
                {
                    curWrong = NormalizeMyanmar(mw.Groups[1].Value);
                    continue;
                }
                Match mc = corrRx.Match(line);
                if (mc.Success && !string.IsNullOrEmpty(curPat) && !string.IsNullOrEmpty(curWrong))
                {
                    string curCorr = NormalizeMyanmar(mc.Groups[1].Value);
                    if (curPat.IndexOf(curWrong, StringComparison.Ordinal) >= 0)
                    {
                        string fullCorrect = NormalizeMyanmar(curPat.Replace(curWrong, curCorr));
                        if (fullCorrect != curPat &&
                            !officialThatponSet.Contains(curPat) &&
                            PureMyanmarWordRegex.IsMatch(curPat) &&
                            CountSyllables(curPat) >= 2 &&
                            !HasTrailingInflectionParticle(curPat))
                        {
                            outRules.Add(new RuleItem
                            {
                                WrongForm = curPat,
                                CorrectForm = fullCorrect,
                                Category = "Spelling",
                                Explanation = string.Format("မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ '{0}' အစား '{1}' ဟု ရေးသားရမည်။", curPat, fullCorrect)
                            });
                        }
                    }
                    curPat = "";
                    curWrong = "";
                }
            }
        }

        private static void ParseCollocationsAndHomophoneContextRules(
            string collocationsYamlPath,
            string homophoneYamlPath,
            List<ContextRuleItem> outCtxRules)
        {
            // NOTE: collocations.yaml is intentionally excluded because forensic auditing revealed 150+ synthetic
            // literal-metaphor rules (e.g., flagging valid 'သီချင်း ရေး', 'ရာထူး တက်', 'ဆု ပေး', 'အစည်းအဝေး ဖွင့်', 'မိဘ မြင်' as errors!).
            // Only genuine orthographic homophone rules from homophone_confusion.yaml are admitted.
            if (File.Exists(homophoneYamlPath))
            {
                string[] lines = File.ReadAllLines(homophoneYamlPath, Encoding.UTF8);
                string section = "";
                string curWrong = "";
                string curCorr = "";
                var curTrigs = new List<string>();
                Regex secRx = new Regex(@"^(aukmyit_context|extra_aukmyit_context|left_context_homophones|left_suffix_triggers):", RegexOptions.Compiled);
                Regex keyRx = new Regex(@"^\s{2}""([^""]+)"":\s*$", RegexOptions.Compiled);
                Regex corrRx = new Regex(@"^\s{4}correct:\s*""([^""]+)""", RegexOptions.Compiled);
                Regex itemRx = new Regex(@"^\s{6}-\s*""([^""]+)""", RegexOptions.Compiled);

                for (int i = 0; i <= lines.Length; i++)
                {
                    string line = (i < lines.Length) ? lines[i] : "aukmyit_context:";
                    Match ms = secRx.Match(line);
                    Match mk = keyRx.Match(line);
                    if (ms.Success || mk.Success)
                    {
                        if (!string.IsNullOrEmpty(curWrong) && !string.IsNullOrEmpty(curCorr) && curTrigs.Count > 0)
                        {
                            string dir = (section == "aukmyit_context" || section == "extra_aukmyit_context") ? "Right"
                                       : (section == "left_suffix_triggers" ? "LeftSuffix" : "Left");
                            int win = (dir == "Right" || dir == "LeftSuffix") ? 1 : 4;
                            outCtxRules.Add(new ContextRuleItem
                            {
                                WrongForm = curWrong,
                                CorrectForm = curCorr,
                                TriggerWords = string.Join("|", curTrigs.ToArray()),
                                Direction = dir,
                                Window = win,
                                Explanation = string.Format("မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရှေ့နောက်စကားစပ်တွင် '{0}' အစား '{1}' ဟု ရေးသားရမည်။", curWrong, curCorr)
                            });
                        }
                        curWrong = mk.Success ? NormalizeMyanmar(mk.Groups[1].Value) : "";
                        curCorr = "";
                        curTrigs.Clear();
                        if (ms.Success) section = ms.Groups[1].Value;
                        continue;
                    }
                    Match mc = corrRx.Match(line);
                    if (mc.Success) { curCorr = NormalizeMyanmar(mc.Groups[1].Value); continue; }
                    Match mi = itemRx.Match(line);
                    if (mi.Success)
                    {
                        string t = NormalizeMyanmar(mi.Groups[1].Value);
                        if (!string.IsNullOrEmpty(t)) curTrigs.Add(t);
                    }
                }
            }
        }

        public static void BuildJson(
            string sourcesDir,
            string thatponPath,
            string wordlistsPath,
            string freqPath,
            string myWordUnigramPath,
            string myWordPhrasePath,
            string curatedLexiconPath,
            Dictionary<string, string> posJsonPaths,
            string nrcJsonPath,
            EntryItem[] ministryEntries,
            RuleItem[] ministryRules,
            string[] part1PdfLines,
            EntryItem[] khweHtarEntries,
            RuleItem[] khweHtarRules,
            string outputJsonPath)
        {
            // A0. Fortify KnownWrongBlacklist with all Official 462-Page Book 'မှား → မှန်' condemned forms so external corpora never admit them!
            for (int kr = 0; kr < khweHtarRules.Length; kr++)
            {
                string kw = NormalizeMyanmar(khweHtarRules[kr].WrongForm);
                string kc = NormalizeMyanmar(khweHtarRules[kr].CorrectForm);
                if (!string.IsNullOrEmpty(kw) && kw != kc)
                {
                    KnownWrongBlacklist.Add(kw);
                }
            }

            var map = new Dictionary<string, EntryItem>(StringComparer.Ordinal);
            var homographEntries = new List<EntryItem>();
            var officialThatponSet = new HashSet<string>(StringComparer.Ordinal);

            // A. Seed Core Homophone Cues & Curated Ministry Entries
            AddCoreHomophoneCues(map, homographEntries, officialThatponSet);
            for (int i = 0; i < ministryEntries.Length; i++)
            {
                var m = ministryEntries[i];
                Upsert(map, homographEntries, officialThatponSet, m.Word, m.MeaningCue, m.PosTag, true, false);
            }

            // B. Parse 100% of Official 462-Page 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' Part 1 (Pages 51..335) + Kanaung Backup
            int bookLinesParsed = 0;
            int bookCollocationsExtracted = 0;
            int bookParenVariantsExtracted = 0;
            int bookStemsExtracted = 0;
            var unlockedClassifierStems = new HashSet<string>(StringComparer.Ordinal);

            var combinedPart1Lines = new List<string>(part1PdfLines.Length + 14000);
            combinedPart1Lines.AddRange(part1PdfLines);
            if (File.Exists(thatponPath))
            {
                foreach (string kl in File.ReadAllLines(thatponPath, Encoding.UTF8))
                {
                    if (RepairWinInnwaGlitches(kl).StartsWith("အဆင့်သတ်ပုံ", StringComparison.Ordinal))
                        break;
                    combinedPart1Lines.Add(kl);
                }
            }

            foreach (string rawLine in combinedPart1Lines)
            {
                string line = RepairWinInnwaGlitches(rawLine);
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("အဆင့်သတ်ပုံ", StringComparison.Ordinal))
                    continue;

                bookLinesParsed++;
                string pos = "";
                string cue = "";
                string head = line;

                int parenStart = head.IndexOf('(');
                int parenEnd = head.LastIndexOf(')');
                if (parenStart > 0 && parenEnd > parenStart)
                {
                    cue = NormalizeMyanmar(head.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim());
                    head = head.Substring(0, parenStart).Trim();
                    if (cue == "ပေါ") cue = "ရှေးသုံး/ကဗျာသုံး (ပေါရာဏ)";
                }

                if (head.EndsWith("-", StringComparison.Ordinal))
                {
                    pos = "V";
                    head = head.TrimEnd('-').Trim();
                }

                if (head.IndexOf('/') > 0)
                {
                    string[] altParts = head.Split('/');
                    for (int a = 0; a < altParts.Length; a++)
                    {
                        Upsert(map, homographEntries, officialThatponSet, altParts[a].Trim(), cue, pos, true, true);
                    }
                    continue;
                }

                if (head.IndexOf(' ') > 0)
                {
                    string joined = head.Replace(" ", "");
                    Upsert(map, homographEntries, officialThatponSet, joined, cue, pos, true, true);
                    string[] spaceParts = head.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int sp = 0; sp < spaceParts.Length; sp++)
                    {
                        string partNorm = NormalizeMyanmar(spaceParts[sp]);
                        if (CountSyllables(partNorm) >= 2)
                        {
                            Upsert(map, homographEntries, officialThatponSet, partNorm, string.IsNullOrEmpty(cue) ? ("တွဲဖက်အသုံး (" + joined + ")") : cue, pos, true, false);
                        }
                    }
                    continue;
                }

                string normHead = NormalizeMyanmar(head);
                string effectivePos = string.IsNullOrEmpty(pos) ? "N" : pos;
                Upsert(map, homographEntries, officialThatponSet, normHead, cue, effectivePos, true, true);

                if (!string.IsNullOrEmpty(cue) && cue != "ရှေးသုံး/ကဗျာသုံး (ပေါရာဏ)" && !CollocationSkipHeads.Contains(normHead))
                {
                    string[] cueItems = cue.Split(new char[] { '၊', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int ci = 0; ci < cueItems.Length; ci++)
                    {
                        string rawItem = cueItems[ci].Trim();
                        bool itemIsVerb = rawItem.EndsWith("-", StringComparison.Ordinal);
                        string cleanItem = rawItem.TrimEnd('-').Trim();
                        if (string.IsNullOrEmpty(cleanItem) || cleanItem == "..." || cleanItem == "~")
                            continue;

                        bool hasEllipsis = cleanItem.IndexOf("...", StringComparison.Ordinal) >= 0 || cleanItem.IndexOf('~') >= 0;
                        if (hasEllipsis)
                        {
                            bool startsOrEnds = cleanItem.StartsWith("...", StringComparison.Ordinal) ||
                                                cleanItem.EndsWith("...", StringComparison.Ordinal) ||
                                                cleanItem.StartsWith("~", StringComparison.Ordinal) ||
                                                cleanItem.EndsWith("~", StringComparison.Ordinal);
                            if (startsOrEnds &&
                                cleanItem.IndexOf("၍", StringComparison.Ordinal) < 0 &&
                                cleanItem.IndexOf("သည့်", StringComparison.Ordinal) < 0 &&
                                cleanItem.IndexOf("သော", StringComparison.Ordinal) < 0 &&
                                cleanItem.IndexOf("ကို", StringComparison.Ordinal) < 0 &&
                                cleanItem.IndexOf("ဖြင့်", StringComparison.Ordinal) < 0)
                            {
                                string expanded = NormalizeMyanmar(cleanItem.Replace("...", normHead).Replace("~", normHead).Replace(" ", ""));
                                int expSyls = CountSyllables(expanded);
                                if (expanded != normHead && IsCleanHeadword(expanded) && expSyls >= 2 && expSyls <= 6 && !HasTrailingInflectionParticle(expanded))
                                {
                                    string collocCue = normHead + " (" + cleanItem.Replace("...", "~") + ")";
                                    string collocPos = (itemIsVerb || pos == "V") ? "V" : "N";
                                    if (!map.ContainsKey(expanded))
                                        bookCollocationsExtracted++;
                                    Upsert(map, homographEntries, officialThatponSet, expanded, collocCue, collocPos, true, false);
                                }
                            }
                        }
                        else
                        {
                            if (cleanItem.IndexOf(' ') < 0 && !cleanItem.EndsWith("သုံး", StringComparison.Ordinal))
                            {
                                string normVar = NormalizeMyanmar(cleanItem);
                                int varSyls = CountSyllables(normVar);
                                bool isShortNegatedGloss = (varSyls == 2 && normVar.StartsWith("မ", StringComparison.Ordinal));
                                if (IsCleanHeadword(normVar) && varSyls >= 2 && varSyls <= 6 && !isShortNegatedGloss && !HasTrailingInflectionParticle(normVar))
                                {
                                    string varCue = "သတ်ပုံကျမ်း တွဲဖက်အညွှန်း (" + normHead + ")";
                                    string varPos = (itemIsVerb || pos == "V") ? "V" : "N";
                                    if (!map.ContainsKey(normVar))
                                        bookParenVariantsExtracted++;
                                    Upsert(map, homographEntries, officialThatponSet, normVar, varCue, varPos, true, false);
                                }
                            }
                        }
                    }
                }

                if (pos != "V" && IsCleanHeadword(normHead))
                {
                    for (int sf = 0; sf < BookClassifierSuffixes.Length; sf++)
                    {
                        string suffix = BookClassifierSuffixes[sf].Key;
                        if (normHead.Length > suffix.Length && normHead.EndsWith(suffix, StringComparison.Ordinal))
                        {
                            string stem = normHead.Substring(0, normHead.Length - suffix.Length);
                            if (IsCleanHeadword(stem) && CountSyllables(stem) >= 2 &&
                                CountSyllables(stem) + CountSyllables(suffix) == CountSyllables(normHead))
                            {
                                string stemCue = string.IsNullOrEmpty(cue)
                                    ? BookClassifierSuffixes[sf].Value
                                    : (cue + " • " + BookClassifierSuffixes[sf].Value);
                                if (!map.ContainsKey(stem))
                                {
                                    bookStemsExtracted++;
                                    unlockedClassifierStems.Add(stem);
                                }
                                Upsert(map, homographEntries, officialThatponSet, stem, stemCue, "N", true, false);
                            }
                            break;
                        }
                    }
                }
            }

            foreach (string rawLine in combinedPart1Lines)
            {
                string h = RepairWinInnwaGlitches(rawLine);
                if (h.StartsWith("အဆင့်သတ်ပုံ", StringComparison.Ordinal)) continue;
                int ps = h.IndexOf('(');
                if (ps > 0) h = h.Substring(0, ps).Trim();
                h = NormalizeMyanmar(h.TrimEnd('-').Trim());
                unlockedClassifierStems.Remove(h);
            }

            // B2. Parse 100% of Part 2 'ခွဲထား' (Pages 336..462, 13,535 entries, 3,696+ brand-new Official Book compounds & homophone contrasts!)
            int khweHtarNewAdded = 0;
            int khweHtarCueEnriched = 0;
            for (int ki = 0; ki < khweHtarEntries.Length; ki++)
            {
                var kh = khweHtarEntries[ki];
                string kw = NormalizeMyanmar(kh.Word);
                if (!IsCleanHeadword(kw)) continue;
                string kCue = string.IsNullOrEmpty(kh.MeaningCue) ? "" : kh.MeaningCue.Trim();
                string kPos = string.IsNullOrEmpty(kh.PosTag) ? "N" : kh.PosTag;

                EntryItem existingKh;
                if (map.TryGetValue(kw, out existingKh))
                {
                    existingKh.IsOfficialThatpon = true;
                    officialThatponSet.Add(kw);
                    if (string.IsNullOrEmpty(existingKh.MeaningCue) && !string.IsNullOrEmpty(kCue))
                    {
                        existingKh.MeaningCue = kCue;
                        khweHtarCueEnriched++;
                    }
                    else if (!string.IsNullOrEmpty(kCue) &&
                             kCue.IndexOf("(နှိုင်းယှဉ်:", StringComparison.Ordinal) >= 0 &&
                             existingKh.MeaningCue.IndexOf("နှိုင်းယှဉ်:", StringComparison.Ordinal) < 0)
                    {
                        int cmpIdx = kCue.IndexOf("(နှိုင်းယှဉ်:", StringComparison.Ordinal);
                        string cmpTail = kCue.Substring(cmpIdx).Trim();
                        existingKh.MeaningCue = existingKh.MeaningCue + " " + cmpTail;
                        string cmpNote = cmpTail.Trim('(', ')', ' ');
                        if (existingKh.UsageNote == null || existingKh.UsageNote.IndexOf("နှိုင်းယှဉ်:", StringComparison.Ordinal) < 0)
                        {
                            existingKh.UsageNote = string.IsNullOrEmpty(existingKh.UsageNote)
                                ? cmpNote
                                : existingKh.UsageNote + " | " + cmpNote;
                        }
                        khweHtarCueEnriched++;
                    }
                }
                else
                {
                    Upsert(map, homographEntries, officialThatponSet, kw, kCue, kPos, true, false);
                    if (map.ContainsKey(kw))
                    {
                        khweHtarNewAdded++;
                        unlockedClassifierStems.Remove(kw);
                    }
                }
            }

            // C. Parse kanaung/wordlists.list (Official MLC headwords)
            foreach (string rawLine in File.ReadAllLines(wordlistsPath, Encoding.UTF8))
            {
                string line = ZeroWidthRegex.Replace(rawLine, "").Trim();
                if (string.IsNullOrEmpty(line)) continue;
                string pos = "";
                if (line.EndsWith("-", StringComparison.Ordinal))
                {
                    pos = "V";
                    line = line.TrimEnd('-').Trim();
                }
                Upsert(map, homographEntries, officialThatponSet, line, "", pos, true, false);
            }

            // D. Parse Myanmar NRC Townships (470 official State/Region + Township NRC records across States/Regions 1..14)
            string nrcContent = File.ReadAllText(nrcJsonPath, Encoding.UTF8);
            var nrcParsedItems = new List<(int StateNum, string CodeMm, string CodeEn, string TownshipMm)>(512);
            Regex nrcItemRegex = new Regex(
                "\"name_en\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"name_mm\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"nrc_code\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.Compiled);
            Regex nrcParenLeadRx = new Regex(@"^\(([^)]+)\)\s*(.+)$", RegexOptions.Compiled);
            Regex nrcParenTrailRx = new Regex(@"^([^\(]+)\(([^)]+)\)$", RegexOptions.Compiled);
            Regex nrcTrailingQualifierRx = new Regex(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);

            foreach (Match m in nrcItemRegex.Matches(nrcContent))
            {
                string rawEn = m.Groups[1].Value.Trim();
                string rawMm = m.Groups[2].Value.Trim();
                string rawSt = m.Groups[3].Value.Replace("*", "").Trim();
                int stNum;
                if (!int.TryParse(rawSt, out stNum) || stNum < 1 || stNum > 14)
                    continue;

                string code = "";
                string township = "";
                Match mp1 = nrcParenLeadRx.Match(rawMm);
                if (mp1.Success)
                {
                    code = NormalizeMyanmar(mp1.Groups[1].Value.Trim().Replace('\u1040', '\u101D'));
                    township = NormalizeMyanmar(mp1.Groups[2].Value.Trim().Replace('\u1040', '\u101D'));
                }
                else
                {
                    Match mp2 = nrcParenTrailRx.Match(rawMm);
                    if (mp2.Success)
                    {
                        code = NormalizeMyanmar(mp2.Groups[1].Value.Trim().Replace('\u1040', '\u101D'));
                        township = NormalizeMyanmar(mp2.Groups[2].Value.Trim().Replace('\u1040', '\u101D'));
                    }
                }

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(township))
                    continue;
                if (township == "မိုးညင်း") township = "မိုးညှင်း";

                nrcParsedItems.Add((stNum, code, rawEn, township));

                string baseTownship = nrcTrailingQualifierRx.Replace(township, "").Trim();
                if (!string.IsNullOrEmpty(baseTownship) && baseTownship.IndexOf(',') < 0 && baseTownship.IndexOf(' ') < 0)
                {
                    Upsert(map, homographEntries, officialThatponSet, baseTownship, "မြန်မာနိုင်ငံ မြို့နယ်/ဒေသအမည်", "N", true, false);
                    Upsert(map, homographEntries, officialThatponSet, baseTownship + "မြို့", "မြန်မာနိုင်ငံ မြို့အမည်", "N", true, false);
                    Upsert(map, homographEntries, officialThatponSet, baseTownship + "မြို့နယ်", "မြန်မာနိုင်ငံ မြို့နယ်အမည်", "N", true, false);
                }
            }

            // E. Parse Standardized Loanwords (loan_words.yaml)
            string loanYamlPath = Path.Combine(sourcesDir, "rules", "loan_words.yaml");
            var loanRules = new List<RuleItem>();
            if (File.Exists(loanYamlPath))
            {
                string currentStandard = "";
                string currentSource = "";
                Regex stdRx = new Regex(@"^\s*-\s*standard:\s*""([^""]+)""", RegexOptions.Compiled);
                Regex srcRx = new Regex(@"^\s*source:\s*""([^""]+)""", RegexOptions.Compiled);
                Regex varRx = new Regex(@"^\s*variants:\s*\[(.*)\]", RegexOptions.Compiled);
                Regex quotedItemRx = new Regex(@"""([^""]+)""", RegexOptions.Compiled);

                foreach (string rawLine in File.ReadAllLines(loanYamlPath, Encoding.UTF8))
                {
                    Match mStd = stdRx.Match(rawLine);
                    if (mStd.Success)
                    {
                        currentStandard = NormalizeMyanmar(mStd.Groups[1].Value);
                        currentSource = "";
                        continue;
                    }
                    Match mSrc = srcRx.Match(rawLine);
                    if (mSrc.Success && !string.IsNullOrEmpty(currentStandard))
                    {
                        currentSource = mSrc.Groups[1].Value.Trim();
                        string cue = string.IsNullOrEmpty(currentSource)
                            ? "ခေတ်သုံး မွေးစားဝေါဟာရ"
                            : ("မွေးစားဝေါဟာရ (" + currentSource + ")");
                        bool alreadyOfficial = officialThatponSet.Contains(currentStandard);
                        Upsert(map, homographEntries, officialThatponSet, currentStandard, cue, alreadyOfficial ? "N" : "LOAN", alreadyOfficial, false);
                        continue;
                    }
                    Match mVar = varRx.Match(rawLine);
                    if (mVar.Success && !string.IsNullOrEmpty(currentStandard) && IsCleanHeadword(currentStandard))
                    {
                        foreach (Match mv in quotedItemRx.Matches(mVar.Groups[1].Value))
                        {
                            string variant = NormalizeMyanmar(mv.Groups[1].Value);
                            if (!string.IsNullOrEmpty(variant) && variant != currentStandard &&
                                !officialThatponSet.Contains(variant) && PureMyanmarWordRegex.IsMatch(variant) &&
                                CountSyllables(variant) >= 2)
                            {
                                string srcNote = string.IsNullOrEmpty(currentSource) ? "" : (" (" + currentSource + ")");
                                loanRules.Add(new RuleItem
                                {
                                    WrongForm = variant,
                                    CorrectForm = currentStandard,
                                    Category = "Spelling",
                                    Explanation = string.Format("ခေတ်သုံး မွေးစားဝေါဟာရ စံသတ်ပုံအရ{0} '{1}' အစား '{2}' ဟု ရေးသားရမည်။", srcNote, variant, currentStandard)
                                });
                            }
                        }
                    }
                }
            }

            // Build Extended Purity Skeleton Set from 100% of Official Thatpon Words!
            var officialPurityKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string ow in officialThatponSet)
            {
                officialPurityKeys.Add(ComputeExtendedPuritySkeleton(ow));
            }

            // F. Load Empirical Myanmar Unigram + Phrase Corpus Frequencies (mm_word_freq + myWord 121K unigrams + myWord 417K phrases)
            var empiricalFreqMap = new Dictionary<string, int>(StringComparer.Ordinal);
            if (File.Exists(freqPath))
            {
                foreach (string rawLine in File.ReadAllLines(freqPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    int spaceIdx = rawLine.LastIndexOf(' ');
                    if (spaceIdx <= 0) continue;
                    string w = NormalizeMyanmar(rawLine.Substring(0, spaceIdx));
                    int cnt;
                    if (int.TryParse(rawLine.Substring(spaceIdx + 1).Trim(), out cnt) && cnt > 0)
                    {
                        int prev;
                        if (!empiricalFreqMap.TryGetValue(w, out prev) || cnt > prev)
                            empiricalFreqMap[w] = cnt;
                    }
                }
            }
            if (File.Exists(myWordUnigramPath))
            {
                foreach (string rawLine in File.ReadAllLines(myWordUnigramPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    int tabIdx = rawLine.IndexOf('\t');
                    if (tabIdx <= 0) continue;
                    string w = NormalizeMyanmar(rawLine.Substring(0, tabIdx));
                    int rawCnt;
                    if (int.TryParse(rawLine.Substring(tabIdx + 1).Trim(), out rawCnt) && rawCnt > 0)
                    {
                        int scaledCnt = (int)Math.Ceiling(rawCnt / 15.0);
                        int prev;
                        if (!empiricalFreqMap.TryGetValue(w, out prev) || scaledCnt > prev)
                            empiricalFreqMap[w] = scaledCnt;
                    }
                }
            }
            if (File.Exists(myWordPhrasePath))
            {
                foreach (string rawLine in File.ReadAllLines(myWordPhrasePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    int tabIdx = rawLine.IndexOf('\t');
                    if (tabIdx <= 0) continue;
                    string rawKey = rawLine.Substring(0, tabIdx);
                    if (rawKey.IndexOf('_') < 0) continue;
                    string w = NormalizeMyanmar(rawKey.Replace("_", ""));
                    int rawCnt;
                    if (int.TryParse(rawLine.Substring(tabIdx + 1).Trim(), out rawCnt) && rawCnt >= 15)
                    {
                        int scaledCnt = (int)Math.Ceiling(rawCnt / 15.0);
                        int prev;
                        if (!empiricalFreqMap.TryGetValue(w, out prev) || scaledCnt > prev)
                            empiricalFreqMap[w] = scaledCnt;
                    }
                }
            }
            string govNewsUnigramPath = Path.Combine(sourcesDir, "corpora", "gov_published_news_unigrams.txt");
            if (File.Exists(govNewsUnigramPath))
            {
                foreach (string rawLine in File.ReadAllLines(govNewsUnigramPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    int tabIdx = rawLine.IndexOf('\t');
                    if (tabIdx <= 0) continue;
                    string w = NormalizeMyanmar(rawLine.Substring(0, tabIdx));
                    int govCnt;
                    if (int.TryParse(rawLine.Substring(tabIdx + 1).Trim(), out govCnt) && govCnt >= 2 && !KnownWrongBlacklist.Contains(w))
                    {
                        int prev;
                        int boosted = empiricalFreqMap.TryGetValue(w, out prev)
                            ? Math.Max(prev, Math.Min(prev + govCnt, 25000))
                            : Math.Max(6, govCnt * 2);
                        empiricalFreqMap[w] = boosted;
                    }
                }
            }

            // G. Parse myG2P Pronunciation Lexicon (24,802 entries — words, compounds, sandhi romanizations & IPA pronunciations)
            string myG2pPath = Path.Combine(sourcesDir, "dictionaries", "myg2p_v2.txt");
            var myG2pPronMap = new Dictionary<string, string>(25000, StringComparer.Ordinal);
            int myG2pAdded = 0;
            if (File.Exists(myG2pPath))
            {
                foreach (string rawLine in File.ReadAllLines(myG2pPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 4) continue;
                    string rawWord = cols[1].Trim();
                    if (rawWord.IndexOf('.') >= 0 || rawWord.IndexOf(' ') >= 0) continue;
                    string w = NormalizeMyanmar(rawWord);
                    if (!IsCleanHeadword(w)) continue;
                    string roman = cols[3].Trim();
                    string ipa = (cols.Length >= 5 && !string.IsNullOrWhiteSpace(cols[4])) ? cols[4].Trim() : "";
                    string g2pStructured = !string.IsNullOrEmpty(roman) && !string.IsNullOrEmpty(ipa)
                        ? (roman + " [" + ipa + "]")
                        : (!string.IsNullOrEmpty(ipa) ? ipa : roman);
                    if (!string.IsNullOrEmpty(g2pStructured) && !myG2pPronMap.ContainsKey(w))
                    {
                        myG2pPronMap[w] = g2pStructured;
                    }

                    string pronCue = string.IsNullOrEmpty(ipa)
                        ? (string.IsNullOrEmpty(roman) ? "" : ("အသံထွက်: " + roman))
                        : ("အသံထွက်: /" + ipa + "/");

                    EntryItem existing;
                    if (map.TryGetValue(w, out existing))
                    {
                        if (string.IsNullOrEmpty(existing.MeaningCue) && !string.IsNullOrEmpty(pronCue))
                            existing.MeaningCue = pronCue;
                    }
                    else if (CountSyllables(w) >= 2 && CountSyllables(w) <= 6 &&
                             !HasTrailingInflectionParticle(w) &&
                             !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                    {
                        Upsert(map, homographEntries, officialThatponSet, w, pronCue, "N/V", false, false);
                        myG2pAdded++;
                    }
                }
            }

            // H. Parse mydict_common_words.txt (28.8K) + mydict_mmnlp_words.txt (32K) + compound_morphology.yaml + compounds.yaml + named_entities.yaml
            int mydictAdded = 0;
            string[] mydictFiles = new string[]
            {
                Path.Combine(sourcesDir, "dictionaries", "mydict_common_words.txt"),
                Path.Combine(sourcesDir, "dictionaries", "mydict_mmnlp_words.txt")
            };
            for (int mf = 0; mf < mydictFiles.Length; mf++)
            {
                if (!File.Exists(mydictFiles[mf])) continue;
                foreach (string rawLine in File.ReadAllLines(mydictFiles[mf], Encoding.UTF8))
                {
                    string w = NormalizeMyanmar(rawLine.Trim());
                    int syls = CountSyllables(w);
                    if (IsCleanHeadword(w) && syls >= 2 && syls <= 6 &&
                        !HasTrailingInflectionParticle(w) &&
                        !map.ContainsKey(w) &&
                        !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                    {
                        Upsert(map, homographEntries, officialThatponSet, w, "", "N/V", false, false);
                        mydictAdded++;
                    }
                }
            }

            string compMorphPath = Path.Combine(sourcesDir, "rules", "compound_morphology.yaml");
            if (File.Exists(compMorphPath))
            {
                Regex cmRx = new Regex(@"^\s*-\s*compound:\s*(\S+)", RegexOptions.Compiled);
                foreach (string rawLine in File.ReadAllLines(compMorphPath, Encoding.UTF8))
                {
                    Match mc = cmRx.Match(rawLine);
                    if (mc.Success)
                    {
                        string cw = NormalizeMyanmar(mc.Groups[1].Value.Trim('"', '\''));
                        if (IsCleanHeadword(cw) && CountSyllables(cw) >= 2 && CountSyllables(cw) <= 6 &&
                            !HasTrailingInflectionParticle(cw) &&
                            (map.ContainsKey(cw) || !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(cw))))
                        {
                            Upsert(map, homographEntries, officialThatponSet, cw, "မြန်မာပေါင်းစပ်ဝေါဟာရ", "N/V", false, false);
                        }
                    }
                }
            }

            string compoundsYamlPath = Path.Combine(sourcesDir, "rules", "compounds.yaml");
            if (File.Exists(compoundsYamlPath))
            {
                Regex compRx = new Regex(@"^\s*compound:\s*""([^""]+)""", RegexOptions.Compiled);
                foreach (string rawLine in File.ReadAllLines(compoundsYamlPath, Encoding.UTF8))
                {
                    Match mc = compRx.Match(rawLine);
                    if (mc.Success)
                    {
                        string cw = NormalizeMyanmar(mc.Groups[1].Value);
                        if (IsCleanHeadword(cw) && CountSyllables(cw) >= 2 &&
                            (map.ContainsKey(cw) || !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(cw))))
                        {
                            Upsert(map, homographEntries, officialThatponSet, cw, "မြန်မာပေါင်းစပ်ဝေါဟာရ", "N/V", false, false);
                        }
                    }
                }
            }

            string namedEntitiesYamlPath = Path.Combine(sourcesDir, "rules", "named_entities.yaml");
            if (File.Exists(namedEntitiesYamlPath))
            {
                Regex neItemRx = new Regex(@"^\s*-\s*""([^""]+)""", RegexOptions.Compiled);
                foreach (string rawLine in File.ReadAllLines(namedEntitiesYamlPath, Encoding.UTF8))
                {
                    Match mNe = neItemRx.Match(rawLine);
                    if (mNe.Success)
                    {
                        string ne = NormalizeMyanmar(mNe.Groups[1].Value);
                        if (IsCleanHeadword(ne) && CountSyllables(ne) >= 2 &&
                            (map.ContainsKey(ne) || !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(ne))))
                        {
                            Upsert(map, homographEntries, officialThatponSet, ne, "အမည်နာမ/အဖွဲ့အစည်းအမည်", "N", false, false);
                        }
                    }
                }
            }

            // I. Parse POS-Tagged JSON files from myanmartools/myanmar-words
            Regex jsonStrRegex = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
            foreach (var kvp in posJsonPaths)
            {
                string posTag = kvp.Key;
                string fileContent = File.ReadAllText(kvp.Value, Encoding.UTF8);
                foreach (Match m in jsonStrRegex.Matches(fileContent))
                {
                    string val = NormalizeMyanmar(Regex.Unescape(m.Groups[1].Value).Trim());
                    if (val == "value" || !IsCleanHeadword(val)) continue;
                    if (map.ContainsKey(val) || !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(val)))
                    {
                        Upsert(map, homographEntries, officialThatponSet, val, "", posTag, false, false);
                    }
                }
            }

            // J. Parse Ornagai Myanmar-English Dictionary (52,604 Zawgyi rows -> Convert to Unicode -> Enrich Definitions, Multi-Sense Glosses & EN_LEX!)
            var mmToEnGlossList = new Dictionary<string, List<string>>(42000, StringComparer.Ordinal);
            var ornagaiEnBuckets = new Dictionary<string, List<(string Pos, string MmGloss)>>(28000, StringComparer.OrdinalIgnoreCase);

            void AddMmEnglishSense(string mmWord, string enSense)
            {
                if (string.IsNullOrWhiteSpace(mmWord) || string.IsNullOrWhiteSpace(enSense))
                    return;
                string cleanEn = enSense.Trim();
                if (cleanEn.Length < 2 || cleanEn.Length > 60 || VulgarOrnagaiEnglishRegex.IsMatch(cleanEn))
                    return;
                if (!mmToEnGlossList.TryGetValue(mmWord, out var list))
                {
                    list = new List<string>(3);
                    mmToEnGlossList[mmWord] = list;
                }
                if (list.Count >= 4)
                    return;
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i], cleanEn, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                list.Add(cleanEn);
            }

            string ornagaiPath = Path.Combine(sourcesDir, "dictionaries", "ornagai_mmtab.tsv");
            int ornagaiEnriched = 0;
            int ornagaiAdded = 0;
            if (File.Exists(ornagaiPath))
            {
                foreach (string rawLine in File.ReadAllLines(ornagaiPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 2) continue;

                    string engWord = cols[1].Trim();
                    if (string.IsNullOrEmpty(engWord) || engWord.StartsWith("_", StringComparison.Ordinal) || engWord.StartsWith("-", StringComparison.Ordinal) ||
                        VulgarOrnagaiEnglishRegex.IsMatch(engWord))
                        continue;
                    string rawPos = SanitizeOrnagaiPosTag(cols.Length >= 3 ? cols[2].Trim() : "");
                    string mappedPos = MapOrnagaiPos(rawPos);
                    string engCue = string.IsNullOrEmpty(rawPos) ? engWord : (engWord + " (" + rawPos + ")");

                    string uniGloss = NormalizeMyanmar(ConvertZawgyi(cols[0]).Trim());
                    if (!string.IsNullOrEmpty(uniGloss))
                    {
                        if (!ornagaiEnBuckets.TryGetValue(engWord, out var obList))
                        {
                            obList = new List<(string Pos, string MmGloss)>(2);
                            ornagaiEnBuckets[engWord] = obList;
                        }
                        if (obList.Count < 4)
                            obList.Add((rawPos, uniGloss));
                    }

                    string[] parts = uniGloss.Split(new char[] { '၊', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int pi = 0; pi < parts.Length; pi++)
                    {
                        string w = NormalizeMyanmar(parts[pi].Trim());
                        if (w.IndexOf(' ') >= 0 || !IsCleanHeadword(w)) continue;

                        AddMmEnglishSense(w, engCue);

                        EntryItem existing;
                        if (map.TryGetValue(w, out existing))
                        {
                            if (string.IsNullOrEmpty(existing.MeaningCue) ||
                                existing.MeaningCue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                                existing.MeaningCue == "မြန်မာပေါင်းစပ်ဝေါဟာရ")
                            {
                                existing.MeaningCue = engCue;
                                ornagaiEnriched++;
                            }
                            else if (existing.MeaningCue.StartsWith("အသံထွက်:", StringComparison.Ordinal))
                            {
                                existing.MeaningCue = engCue + " • " + existing.MeaningCue;
                                ornagaiEnriched++;
                            }
                            if ((string.IsNullOrEmpty(existing.PosTag) || existing.PosTag == "N/V") && !string.IsNullOrEmpty(mappedPos))
                            {
                                existing.PosTag = mappedPos;
                            }
                        }
                        else
                        {
                            int syls = CountSyllables(w);
                            int empCnt;
                            bool hasBacking = empiricalFreqMap.TryGetValue(w, out empCnt) && empCnt >= 1;
                            if (hasBacking && syls >= 2 && syls <= 5 &&
                                !StartsWithBoundClausalParticle(w) &&
                                !HasTrailingInflectionParticle(w) &&
                                !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                            {
                                Upsert(map, homographEntries, officialThatponSet, w, engCue, string.IsNullOrEmpty(mappedPos) ? "N/V" : mappedPos, false, false);
                                ornagaiAdded++;
                            }
                        }
                    }
                }
            }

            // K. Enrich POS Tags & High-Confidence Modern Myanmar Lexemes from 57.5K curated_lexicon.csv
            int curatedPosEnriched = 0;
            int curatedModernAdded = 0;
            if (File.Exists(curatedLexiconPath))
            {
                bool firstRow = true;
                foreach (string rawLine in File.ReadAllLines(curatedLexiconPath, Encoding.UTF8))
                {
                    if (firstRow) { firstRow = false; continue; }
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split(',');
                    if (cols.Length < 3) continue;

                    string w = NormalizeMyanmar(cols[0].Trim());
                    string pTag = cols[1].Trim();
                    double conf;
                    if (!double.TryParse(cols[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out conf))
                        conf = 0.0;

                    if (!IsCleanHeadword(w)) continue;

                    EntryItem existingEntry;
                    if (map.TryGetValue(w, out existingEntry))
                    {
                        if ((string.IsNullOrEmpty(existingEntry.PosTag) || existingEntry.PosTag == "N/V") && !string.IsNullOrEmpty(pTag))
                        {
                            existingEntry.PosTag = pTag;
                            curatedPosEnriched++;
                        }
                    }
                    else
                    {
                        int syls = CountSyllables(w);
                        int empFreq;
                        bool hasCorpusBacking = empiricalFreqMap.TryGetValue(w, out empFreq) && empFreq >= 2;
                        if (hasCorpusBacking && conf >= 0.88 && syls >= 2 && syls <= 5 &&
                            !StartsWithBoundClausalParticle(w) &&
                            !HasTrailingInflectionParticle(w) &&
                            !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                        {
                            Upsert(map, homographEntries, officialThatponSet, w, "ခေတ်သုံး မြန်မာဝေါဟာရ", pTag, false, false);
                            curatedModernAdded++;
                        }
                    }
                }
            }

            // K2. Enrich Verified POS Tags from Audited 'ye-kyaw-thu/myPOS v3.0' (43,196 sentences, 536,961 tokens — purified against Thatpon & converter bugs!)
            string myPosPath = Path.Combine(sourcesDir, "corpora", "mypos_v3_upos.txt");
            int myPosEnriched = 0;
            if (File.Exists(myPosPath))
            {
                var myPosWordTags = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
                foreach (string rawLine in File.ReadAllLines(myPosPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] tokens = rawLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int ti = 0; ti < tokens.Length; ti++)
                    {
                        string tok = tokens[ti];
                        int slashIdx = tok.LastIndexOf('/');
                        if (slashIdx <= 0 || slashIdx >= tok.Length - 1) continue;
                        string rawW = tok.Substring(0, slashIdx);
                        string rawT = tok.Substring(slashIdx + 1).TrimEnd('|');
                        string mappedTag = rawT == "VERB" ? "V"
                                         : rawT == "NOUN" ? "N"
                                         : rawT == "ADJ" ? "ADJ"
                                         : rawT == "ADV" ? "ADV"
                                         : rawT == "PRON" ? "PRON"
                                         : rawT == "ADP" ? "POST"
                                         : rawT == "CONJ" ? "CONJ"
                                         : rawT == "PRT" ? "PART"
                                         : (rawT == "NUM" || rawT == "NOUNum") ? "NUM"
                                         : "";
                        if (string.IsNullOrEmpty(mappedTag)) continue;
                        string w = NormalizeMyanmar(rawW);
                        if (!IsCleanHeadword(w)) continue;

                        Dictionary<string, int> tc;
                        if (!myPosWordTags.TryGetValue(w, out tc))
                        {
                            tc = new Dictionary<string, int>(StringComparer.Ordinal);
                            myPosWordTags[w] = tc;
                        }
                        int prevC;
                        tc[mappedTag] = tc.TryGetValue(mappedTag, out prevC) ? (prevC + 1) : 1;
                    }
                }

                foreach (var kv in myPosWordTags)
                {
                    string w = kv.Key;
                    EntryItem ex;
                    if (!map.TryGetValue(w, out ex)) continue;
                    int totalCnt = 0;
                    string bestTag = "";
                    int bestCnt = 0;
                    foreach (var tkv in kv.Value)
                    {
                        totalCnt += tkv.Value;
                        if (tkv.Value > bestCnt)
                        {
                            bestCnt = tkv.Value;
                            bestTag = tkv.Key;
                        }
                    }
                    if (totalCnt >= 5 && ((double)bestCnt / totalCnt) >= 0.85 &&
                        (string.IsNullOrEmpty(ex.PosTag) || ex.PosTag == "N/V" || (ex.PosTag == "N" && bestTag != "N" && bestCnt >= 15)))
                    {
                        ex.PosTag = bestTag;
                        myPosEnriched++;
                    }
                }
            }

            // L. Seed Literary/Everyday Particles, Classical Pali Terms, Induced-Creaky Pronouns & Modern IT Loanwords
            string[][] coreSeedWords = LoadFourColumnTsv(Path.Combine(sourcesDir, "official", "mlc_core_seed_lexicon.tsv"));
            for (int ep = 0; ep < coreSeedWords.Length; ep++)
            {
                string pTag = coreSeedWords[ep][2];
                bool isOff = (pTag != "PART" && pTag != "LOAN") || coreSeedWords[ep][0] == "ဟု";
                Upsert(map, homographEntries, officialThatponSet, coreSeedWords[ep][0], coreSeedWords[ep][1], pTag, isOff, false);
                EntryItem epItem;
                string normSeed = NormalizeMyanmar(coreSeedWords[ep][0]);
                if (map.TryGetValue(normSeed, out epItem))
                {
                    epItem.Frequency = Math.Max(epItem.Frequency, int.Parse(coreSeedWords[ep][3]));
                    if (!string.IsNullOrEmpty(coreSeedWords[ep][1]))
                        epItem.MeaningCue = coreSeedWords[ep][1];
                }
            }
            // Preserve classical Pali spelling 'ဂောတမ' (\u1002\u1031\u102C) without Tall-AA conversion alongside 'ဂေါတမ'
            map["ဂောတမ"] = new EntryItem { Word = "ဂောတမ", MeaningCue = "ပါဠိမူ ဗုဒ္ဓမြတ်စွာဘုရား အနွယ်တော်အမည်", PosTag = "N", Frequency = 55, IsOfficialThatpon = true };

            // M. Attach Calibrated Corpus Frequencies & High-Frequency Clean Everyday Compounds
            int freqMatched = 0;
            int corpusCompoundsAdded = 0;
            foreach (var kv in empiricalFreqMap)
            {
                string w = kv.Key;
                int count = kv.Value;
                EntryItem found;
                if (map.TryGetValue(w, out found))
                {
                    if (count > found.Frequency) found.Frequency = count;
                    freqMatched++;
                }
                else if (count >= 8 && IsCleanHeadword(w) && CountSyllables(w) >= 2 && CountSyllables(w) <= 4 &&
                         !StartsWithBareNegativeMa(w) &&
                         !StartsWithBoundClausalParticle(w) &&
                         !HasTrailingInflectionParticle(w) &&
                         !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                {
                    Upsert(map, homographEntries, officialThatponSet, w, "ခေတ်သုံး မြန်မာဝေါဟာရ", "N/V", false, false);
                    if (map.TryGetValue(w, out found))
                    {
                        found.Frequency = count;
                        corpusCompoundsAdded++;
                    }
                }
            }

            // Purge any unverified crowd-only bound-particle fragments admitted earlier with empty/crowd cues
            var boundFragmentGarbage = new List<string>();
            foreach (var kv in map)
            {
                EntryItem ei = kv.Value;
                if (!ei.IsOfficialThatpon && ei.PosTag != "PART" && ei.PosTag != "LOAN" && ei.PosTag != "PROPN" &&
                    (string.IsNullOrEmpty(ei.MeaningCue) ||
                     ei.MeaningCue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                     ei.MeaningCue == "မြန်မာပေါင်းစပ်ဝေါဟာရ") &&
                    StartsWithBoundClausalParticle(kv.Key))
                {
                    boundFragmentGarbage.Add(kv.Key);
                }
            }
            for (int bi = 0; bi < boundFragmentGarbage.Count; bi++)
            {
                map.Remove(boundFragmentGarbage[bi]);
            }

            // M1b. Promote Existing Attested Multi-Syllable Stems (Syls >= 2, Freq >= 5) Matched Exact-Syllable-Aligned in 462-Page သတ်ပုံကျမ်း Compounds
            var attestedBookSubStems = new HashSet<string>(StringComparer.Ordinal);
            foreach (string bw in officialThatponSet)
            {
                var bwMatches = SyllableCountRegex.Matches(bw);
                int bn = bwMatches.Count;
                if (bn < 3) continue;
                for (int k = 2; k < bn; k++)
                {
                    var sbPref = new StringBuilder();
                    for (int x = 0; x < k; x++) sbPref.Append(bwMatches[x].Value);
                    attestedBookSubStems.Add(sbPref.ToString());
                }
                for (int k = 1; k <= bn - 2; k++)
                {
                    var sbSuf = new StringBuilder();
                    for (int x = k; x < bn; x++) sbSuf.Append(bwMatches[x].Value);
                    attestedBookSubStems.Add(sbSuf.ToString());
                }
            }
            int promotedOfficialStems = 0;
            foreach (var kv in map)
            {
                EntryItem ei = kv.Value;
                if (!ei.IsOfficialThatpon && ei.PosTag != "LOAN" && ei.PosTag != "PART" &&
                    ei.Frequency >= 5 && CountSyllables(kv.Key) >= 2 &&
                    !StartsWithBareNegativeMa(kv.Key) && !StartsWithNumeralSyllable(kv.Key) &&
                    !StartsWithBoundClausalParticle(kv.Key) &&
                    !HasTrailingInflectionParticle(kv.Key) &&
                    attestedBookSubStems.Contains(kv.Key))
                {
                    ei.IsOfficialThatpon = true;
                    officialThatponSet.Add(kv.Key);
                    promotedOfficialStems++;
                }
            }
            // Fix misleading crowd Ornagai gloss on 'အပင်' ("clone (v)")
            EntryItem apinEntry;
            if (map.TryGetValue("အပင်", out apinEntry))
            {
                apinEntry.MeaningCue = "သစ်ပင် / ပန်းပင် စသော အပင်";
                apinEntry.PosTag = "N";
                apinEntry.IsOfficialThatpon = true;
                officialThatponSet.Add("အပင်");
            }

            // M1c. Promote High-Frequency (Freq >= 45, 2..4 Syls) Standard Formal/General Compounds Composed 100% of Official 462p Syllables
            var officialBookSyllables = new HashSet<string>(StringComparer.Ordinal);
            foreach (string bw in officialThatponSet)
            {
                foreach (Match sm in SyllableCountRegex.Matches(bw))
                {
                    officialBookSyllables.Add(sm.Value);
                }
            }
            foreach (var kv in map)
            {
                string w = kv.Key;
                EntryItem ei = kv.Value;
                int sc = CountSyllables(w);
                if (!ei.IsOfficialThatpon && ei.PosTag != "LOAN" && ei.PosTag != "PART" &&
                    ei.Frequency >= 45 && sc >= 2 && sc <= 4 &&
                    !StartsWithBareNegativeMa(w) && !StartsWithNumeralSyllable(w) &&
                    !StartsWithBoundClausalParticle(w) &&
                    !HasTrailingInflectionParticle(w) &&
                    !w.StartsWith("ဦး") && !w.StartsWith("ဒေါ်") && !w.StartsWith("ကို") &&
                    !w.StartsWith("မောင်") && !w.StartsWith("မစ္စ") && !w.StartsWith("သခင်") &&
                    w != "အခု" && w != "အခုလို" && w != "ဒီနေ့" && w != "ဒါကြောင့်" && w != "အရင်က" &&
                    !w.Contains("အဲ့") && !w.Contains("အဲဒ") && !w.StartsWith("အရမ်း") &&
                    !w.EndsWith("ကြီး") && !w.EndsWith("ပါစေ") && !w.EndsWith("လျက်") &&
                    !w.EndsWith("မှာ") && !w.EndsWith("နေ") && !w.EndsWith("အား") &&
                    !w.EndsWith("မှ") && !w.EndsWith("လဲ") && !w.EndsWith("ပဲ") && !w.EndsWith("ဘဲ"))
                {
                    bool allSylsOfficial = true;
                    foreach (Match sm in SyllableCountRegex.Matches(w))
                    {
                        if (!officialBookSyllables.Contains(sm.Value))
                        {
                            allSylsOfficial = false;
                            break;
                        }
                    }
                    if (allSylsOfficial)
                    {
                        ei.IsOfficialThatpon = true;
                        officialThatponSet.Add(w);
                        promotedOfficialStems++;
                    }
                }
            }

            // M1d. Botanical '[STEM]ပင်' -> '[STEM]သီး / ရွက် / ပွင့် / ညွန့် / ဖူး' Official Derivational Promotion & Everyday Core Calibration
            var officialPlantStems = new List<string>();
            foreach (string bw in officialThatponSet)
            {
                if (bw.Length > 3 && bw.EndsWith("ပင်", StringComparison.Ordinal))
                {
                    officialPlantStems.Add(bw.Substring(0, bw.Length - 3));
                }
            }
            string[] botanicalHeads = new string[] { "သီး", "ရွက်", "ပွင့်", "ညွန့်", "ဖူး" };
            for (int ps = 0; ps < officialPlantStems.Count; ps++)
            {
                string pStem = officialPlantStems[ps];
                if (!IsCleanHeadword(pStem)) continue;
                bool anyBotanicalAttested = false;
                for (int bh = 0; bh < botanicalHeads.Length; bh++)
                {
                    string derived = pStem + botanicalHeads[bh];
                    EntryItem exBot;
                    int empBotCnt;
                    bool inMap = map.TryGetValue(derived, out exBot);
                    bool inEmp = empiricalFreqMap.TryGetValue(derived, out empBotCnt) && empBotCnt >= 1;
                    if (inMap || inEmp)
                    {
                        anyBotanicalAttested = true;
                        if (!inMap)
                        {
                            Upsert(map, homographEntries, officialThatponSet, derived, pStem + "ပင်မှ အ" + botanicalHeads[bh], "N", true, false);
                            exBot = map[derived];
                        }
                        else
                        {
                            exBot.IsOfficialThatpon = true;
                            officialThatponSet.Add(derived);
                            if (string.IsNullOrEmpty(exBot.PosTag) || exBot.PosTag == "N/V") exBot.PosTag = "N";
                        }
                        exBot.Frequency = Math.Max(exBot.Frequency, Math.Max(empBotCnt, 28));
                        unlockedClassifierStems.Remove(derived);
                    }
                }
                if (anyBotanicalAttested && CountSyllables(pStem) >= 2)
                {
                    EntryItem stEx;
                    if (map.TryGetValue(pStem, out stEx))
                    {
                        stEx.IsOfficialThatpon = true;
                        officialThatponSet.Add(pStem);
                        stEx.Frequency = Math.Max(stEx.Frequency, 24);
                        unlockedClassifierStems.Remove(pStem);
                    }
                }
            }

            string[][] v14OfficialLexemes = LoadFourColumnTsv(Path.Combine(sourcesDir, "official", "mlc_curated_official_lexemes.tsv"));
            for (int vx = 0; vx < v14OfficialLexemes.Length; vx++)
            {
                string vw = NormalizeMyanmar(v14OfficialLexemes[vx][0]);
                string vCue = v14OfficialLexemes[vx][1];
                string vPos = v14OfficialLexemes[vx][2];
                int vFreq = int.Parse(v14OfficialLexemes[vx][3]);
                Upsert(map, homographEntries, officialThatponSet, vw, vCue, vPos, true, false);
                EntryItem vItem;
                if (map.TryGetValue(vw, out vItem))
                {
                    vItem.MeaningCue = vCue;
                    vItem.PosTag = vPos;
                    vItem.IsOfficialThatpon = true;
                    vItem.Frequency = Math.Max(vItem.Frequency, vFreq);
                    unlockedClassifierStems.Remove(vw);
                }
            }

            // M1.5. Integrate Unified Authoritative Myanmar-to-Myanmar Dictionary ('မြန်မာအဘိဓာန်' — 24,953 Entries: MLC Abidan + Burmese Wiktionary)
            int abidanDefinitionsEnriched = 0;
            int abidanPosEnriched = 0;
            int abidanUsageNotesAdded = 0;
            int abidanNewWordsAdded = 0;
            string abidanTsvPath = Path.Combine(sourcesDir, "dictionaries", "mlc_mm_to_mm_dictionary.tsv");
            Regex hasMyanmarConsonantRx = new Regex(@"[\u1000-\u102A]", RegexOptions.Compiled);
            Regex startsWithLatinGlossRx = new Regex(@"^[A-Za-z]", RegexOptions.Compiled);
            if (File.Exists(abidanTsvPath))
            {
                foreach (string rawLine in File.ReadAllLines(abidanTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 4) continue;

                    string w = NormalizeMyanmar(cols[0].Trim());
                    string aPos = cols[1].Trim();
                    string aDef = NormalizeMyanmar(cols[2].Trim());
                    string aNote = cols[3].Trim();
                    if (!IsCleanHeadword(w) || string.IsNullOrEmpty(aDef))
                        continue;

                    EntryItem existingAb;
                    if (map.TryGetValue(w, out existingAb))
                    {
                        bool isForeignPropn =
                            existingAb.PosTag == "PROPN" &&
                            (string.IsNullOrEmpty(existingAb.UsageNote) ||
                             (existingAb.UsageNote.IndexOf("မြန်မာနိုင်ငံ", StringComparison.Ordinal) < 0 &&
                              existingAb.UsageNote.IndexOf("မြို့နယ်", StringComparison.Ordinal) < 0));

                        // 1. Attach or merge rich မြန်မာအဘိဓာန် UsageNote (အသံထွက်, မျိုးပြပစ္စည်း, ရင်းမြစ်, ကြောင်းတူသံကွဲ, ဆန့်ကျင်ဘက်, ဗဟုသုတ, သာဓက)
                        if (!string.IsNullOrEmpty(aNote) && !isForeignPropn)
                        {
                            if (string.IsNullOrEmpty(existingAb.UsageNote))
                            {
                                existingAb.UsageNote = aNote;
                                abidanUsageNotesAdded++;
                            }
                            else
                            {
                                string[] noteSegs = aNote.Split(new string[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
                                bool mergedAny = false;
                                for (int ns = 0; ns < noteSegs.Length; ns++)
                                {
                                    string seg = noteSegs[ns].Trim();
                                    int colonIdx = seg.IndexOf(':');
                                    if (colonIdx > 0)
                                    {
                                        string prefixKey = seg.Substring(0, colonIdx + 1);
                                        if (existingAb.UsageNote.IndexOf(prefixKey, StringComparison.Ordinal) < 0)
                                        {
                                            existingAb.UsageNote += " | " + seg;
                                            mergedAny = true;
                                        }
                                    }
                                }
                                if (mergedAny)
                                    abidanUsageNotesAdded++;
                            }
                        }

                        // 2. Upgrade default/untyped POS tag to authoritative မြန်မာအဘိဓာန် POS tag
                        if (!string.IsNullOrEmpty(aPos) &&
                            existingAb.PosTag != "LOAN" &&
                            existingAb.PosTag != "PART" &&
                            existingAb.PosTag != "NUM" &&
                            existingAb.PosTag != "PROPN" &&
                            existingAb.PosTag != aPos &&
                            (string.IsNullOrEmpty(existingAb.PosTag) || existingAb.PosTag == "N" || existingAb.PosTag == "N/V"))
                        {
                            existingAb.PosTag = aPos;
                            abidanPosEnriched++;
                        }

                        // 3. Upgrade MeaningCue to authentic Myanmar-to-Myanmar definition while preserving 462-page သတ်ပုံကျမ်း cues!
                        string curCue = existingAb.MeaningCue ?? "";
                        bool isEnglishCue = !string.IsNullOrEmpty(curCue) &&
                                            (startsWithLatinGlossRx.IsMatch(curCue) || !hasMyanmarConsonantRx.IsMatch(curCue));
                        bool isCollocationOrKhweHtarCue =
                            curCue.StartsWith("တွဲဖက်:", StringComparison.Ordinal) ||
                            curCue.StartsWith("သတ်ပုံကျမ်း တွဲဖက်အညွှန်း", StringComparison.Ordinal) ||
                            curCue.StartsWith("သတ်ပုံကျမ်း ခွဲထားအမှန်", StringComparison.Ordinal);
                        bool isPlaceholderOrIpaOrEnglish =
                            string.IsNullOrEmpty(curCue) ||
                            curCue.StartsWith("အသံထွက်:", StringComparison.Ordinal) ||
                            curCue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                            curCue == "မြန်မာပေါင်းစပ်ဝေါဟာရ" ||
                            curCue == "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်" ||
                            curCue == "ခေတ်သုံး စံသတ်ပုံအမှန်" ||
                            isEnglishCue ||
                            isCollocationOrKhweHtarCue;

                        string savedKhweTail = "";
                        int khwePosInAb = curCue.IndexOf("(နှိုင်းယှဉ်:", StringComparison.Ordinal);
                        if (khwePosInAb >= 0)
                        {
                            savedKhweTail = curCue.Substring(khwePosInAb).Trim();
                        }

                        if (isPlaceholderOrIpaOrEnglish && !isForeignPropn)
                        {
                            if (isEnglishCue && (existingAb.UsageNote == null || existingAb.UsageNote.IndexOf("အင်္ဂလိပ်", StringComparison.Ordinal) < 0))
                            {
                                existingAb.UsageNote = string.IsNullOrEmpty(existingAb.UsageNote)
                                    ? ("အင်္ဂလိပ်အနက်: " + curCue)
                                    : (existingAb.UsageNote + " | အင်္ဂလိပ်အနက်: " + curCue);
                            }
                            else if (isCollocationOrKhweHtarCue && (existingAb.UsageNote == null || existingAb.UsageNote.IndexOf("သတ်ပုံအညွှန်း:", StringComparison.Ordinal) < 0))
                            {
                                existingAb.UsageNote = string.IsNullOrEmpty(existingAb.UsageNote)
                                    ? ("သတ်ပုံအညွှန်း: " + curCue)
                                    : (existingAb.UsageNote + " | သတ်ပုံအညွှန်း: " + curCue);
                            }
                            existingAb.MeaningCue = (!string.IsNullOrEmpty(savedKhweTail) && aDef.IndexOf("နှိုင်းယှဉ်:", StringComparison.Ordinal) < 0)
                                ? (aDef + " " + savedKhweTail)
                                : aDef;
                            abidanDefinitionsEnriched++;
                        }
                        else if (!isForeignPropn && curCue.Length <= 42 && curCue.IndexOf(aDef, StringComparison.Ordinal) < 0 && curCue.IndexOf(" — ", StringComparison.Ordinal) < 0)
                        {
                            string shortAbDef = aDef.Length > 95 ? (aDef.Substring(0, 92).TrimEnd() + "...") : aDef;
                            string baseCur = khwePosInAb > 0 ? curCue.Substring(0, khwePosInAb).Trim() : curCue;
                            existingAb.MeaningCue = string.IsNullOrEmpty(savedKhweTail)
                                ? (baseCur + " — " + shortAbDef)
                                : (baseCur + " — " + shortAbDef + " " + savedKhweTail);
                            abidanDefinitionsEnriched++;
                        }
                    }
                    else
                    {
                        int syls = CountSyllables(w);
                        if (syls >= 2 && syls <= 7 &&
                            !HasTrailingInflectionParticle(w) &&
                            !aDef.StartsWith("[ရှေး]", StringComparison.Ordinal) &&
                            !officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                        {
                            var newAbItem = new EntryItem
                            {
                                Word = w,
                                MeaningCue = aDef,
                                PosTag = string.IsNullOrEmpty(aPos) ? "N" : aPos,
                                UsageNote = aNote,
                                Frequency = 22,
                                IsOfficialThatpon = false
                            };
                            map[w] = newAbItem;
                            abidanNewWordsAdded++;
                            if (!string.IsNullOrEmpty(aNote))
                                abidanUsageNotesAdded++;
                        }
                    }
                }
            }

            // M1.6. Ingest U Hoke Sein's Pali Cognate Enrichments & 7-Gate Verified Massive Lexicon (UHS + Attested Compounds + Abidan Derivations)
            int uhsPaliEnriched = 0;
            string uhsEnrichTsvPath = Path.Combine(sourcesDir, "dictionaries", "mlc_uhs_pali_enrichment.tsv");
            if (File.Exists(uhsEnrichTsvPath))
            {
                foreach (string rawLine in File.ReadAllLines(uhsEnrichTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 4) continue;
                    string w = NormalizeMyanmar(cols[0].Trim());
                    string uPos = cols[1].Trim();
                    string uEng = cols[2].Trim();
                    string uPali = NormalizeMyanmar(cols[3].Trim());
                    EntryItem exUhs;
                    if (map.TryGetValue(w, out exUhs))
                    {
                        if (!string.IsNullOrEmpty(uPali))
                        {
                            string curNote = exUhs.UsageNote ?? "";
                            if (curNote.IndexOf("ပါဠိအနက်:", StringComparison.Ordinal) < 0)
                            {
                                exUhs.UsageNote = string.IsNullOrEmpty(curNote)
                                    ? ("ပါဠိအနက်: " + uPali)
                                    : (curNote + " | ပါဠိအနက်: " + uPali);
                                uhsPaliEnriched++;
                            }
                        }
                        if (!string.IsNullOrEmpty(uEng) && !VulgarOrnagaiEnglishRegex.IsMatch(uEng))
                        {
                            AddMmEnglishSense(w, uEng);
                            if (string.IsNullOrEmpty(exUhs.MeaningCue) ||
                                exUhs.MeaningCue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                                exUhs.MeaningCue == "မြန်မာပေါင်းစပ်ဝေါဟာရ")
                            {
                                exUhs.MeaningCue = uEng;
                            }
                        }
                        if (!string.IsNullOrEmpty(uPos) && (string.IsNullOrEmpty(exUhs.PosTag) || exUhs.PosTag == "N/V"))
                        {
                            exUhs.PosTag = uPos;
                        }
                    }
                }
            }

            int massiveLexiconAdded = 0;
            string massiveLexTsvPath = Path.Combine(sourcesDir, "dictionaries", "mlc_massive_verified_lexicon.tsv");
            if (File.Exists(massiveLexTsvPath))
            {
                foreach (string rawLine in File.ReadAllLines(massiveLexTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 5) continue;
                    string w = NormalizeMyanmar(cols[0].Trim());
                    string mPos = cols[1].Trim();
                    string mCue = NormalizeMyanmar(cols[2].Trim());
                    string mNote = cols.Length >= 4 ? NormalizeMyanmar(cols[3].Trim()) : "";
                    int mFreq;
                    if (!int.TryParse(cols[4].Trim(), out mFreq)) mFreq = 14;

                    if (string.IsNullOrEmpty(w) || map.ContainsKey(w) || !IsCleanHeadword(w) ||
                        KnownWrongBlacklist.Contains(w) || VulgarOrnagaiEnglishRegex.IsMatch(mCue))
                        continue;
                    if (StartsWithBoundClausalParticle(w) ||
                        HasTrailingInflectionParticle(w) ||
                        officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                        continue;

                    map[w] = new EntryItem
                    {
                        Word = w,
                        MeaningCue = string.IsNullOrEmpty(mCue) ? "မြန်မာ-ပါဠိ အဘိဓာန်ဝင် ဝေါဟာရ" : mCue,
                        PosTag = string.IsNullOrEmpty(mPos) ? "N" : mPos,
                        UsageNote = mNote,
                        Frequency = Math.Max(10, mFreq),
                        IsOfficialThatpon = false
                    };
                    massiveLexiconAdded++;
                }
            }

            // M1.7. Ingest Authoritative MOFA / GAD / MIMU / CLDR Geographic & Proper-Noun Gazetteer (Countries, World Cities, 330 Myanmar Townships, Landmarks)
            int geoPropnAdded = 0;
            int geoPropnEnriched = 0;
            string geoPropnTsvPath = Path.Combine(sourcesDir, "official", "mlc_geographic_and_proper_nouns.tsv");
            if (File.Exists(geoPropnTsvPath))
            {
                foreach (string rawLine in File.ReadAllLines(geoPropnTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 5) continue;
                    string w = NormalizeMyanmar(cols[0].Trim());
                    string gCue = NormalizeMyanmar(cols[2].Trim());
                    string gNote = cols.Length >= 4 ? cols[3].Trim() : "";
                    int gFreq;
                    if (!int.TryParse(cols[4].Trim(), out gFreq)) gFreq = 220;

                    if (string.IsNullOrEmpty(w) || !PureMyanmarWordRegex.IsMatch(w) || KnownWrongBlacklist.Contains(w))
                        continue;

                    EntryItem existingGeo;
                    if (map.TryGetValue(w, out existingGeo))
                    {
                        if (existingGeo.IsOfficialThatpon)
                        {
                            existingGeo.Frequency = Math.Max(existingGeo.Frequency, gFreq);
                            bool hasPropnHomograph = false;
                            for (int hi = 0; hi < homographEntries.Count; hi++)
                            {
                                if (homographEntries[hi].Word == w && homographEntries[hi].PosTag == "PROPN")
                                {
                                    hasPropnHomograph = true;
                                    break;
                                }
                            }
                            if (!hasPropnHomograph)
                            {
                                homographEntries.Add(new EntryItem
                                {
                                    Word = w,
                                    MeaningCue = gCue,
                                    PosTag = "PROPN",
                                    UsageNote = gNote,
                                    Frequency = Math.Max(existingGeo.Frequency, gFreq),
                                    IsOfficialThatpon = false
                                });
                                geoPropnEnriched++;
                            }
                        }
                        else
                        {
                            existingGeo.PosTag = "PROPN";
                            if (!string.IsNullOrEmpty(gCue))
                                existingGeo.MeaningCue = gCue;
                            if (!string.IsNullOrEmpty(gNote))
                                existingGeo.UsageNote = gNote;
                            existingGeo.Frequency = Math.Max(existingGeo.Frequency, gFreq);
                            geoPropnEnriched++;
                        }
                    }
                    else
                    {
                        map[w] = new EntryItem
                        {
                            Word = w,
                            MeaningCue = gCue,
                            PosTag = "PROPN",
                            UsageNote = gNote,
                            Frequency = Math.Max(120, gFreq),
                            IsOfficialThatpon = false
                        };
                        geoPropnAdded++;
                    }
                }
            }
            Console.WriteLine("  - MOFA/GAD Geographic & Proper-Noun Gazetteer: +" + geoPropnAdded + " new PROPN headwords, " + geoPropnEnriched + " enriched.");

            // M1.8. Ingest V11 5-Domain Specialized Lexicons (Legal/Court, Medical/Pharma, Banking/Tax/Finance, IT/Cybersecurity, Buddhist Pali/Tipitaka)
            int specDomainAdded = 0;
            int specDomainEnriched = 0;
            string domainsDir = Path.Combine(sourcesDir, "domains");
            string[] domainTsvFiles = Directory.Exists(domainsDir)
                ? Directory.GetFiles(domainsDir, "*.tsv").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
                : new string[0];
            foreach (string specDomainTsvPath in domainTsvFiles)
            {
                foreach (string rawLine in File.ReadAllLines(specDomainTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 5) continue;
                    string w = NormalizeMyanmar(cols[0].Trim());
                    string dPos = string.IsNullOrWhiteSpace(cols[1]) ? "N" : cols[1].Trim();
                    string dCue = NormalizeMyanmar(cols[2].Trim());
                    string dNote = cols[3].Trim();
                    int dFreq;
                    if (!int.TryParse(cols[4].Trim(), out dFreq)) dFreq = 180;

                    if (string.IsNullOrEmpty(w) || !PureMyanmarWordRegex.IsMatch(w) || KnownWrongBlacklist.Contains(w))
                        continue;

                    EntryItem existingDom;
                    if (map.TryGetValue(w, out existingDom))
                    {
                        existingDom.Frequency = Math.Max(existingDom.Frequency, dFreq);
                        if (!string.IsNullOrEmpty(dNote))
                        {
                            if (string.IsNullOrEmpty(existingDom.UsageNote))
                            {
                                existingDom.UsageNote = dNote;
                            }
                            else if (existingDom.UsageNote.IndexOf("နယ်ပယ်:", StringComparison.Ordinal) < 0)
                            {
                                existingDom.UsageNote = dNote + " | " + existingDom.UsageNote;
                            }
                            else if (existingDom.UsageNote.IndexOf("ကြောင်းတူသံကွဲ:", StringComparison.Ordinal) < 0 &&
                                     dNote.IndexOf("ကြောင်းတူသံကွဲ:", StringComparison.Ordinal) >= 0)
                            {
                                foreach (string seg in dNote.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (seg.TrimStart().StartsWith("ကြောင်းတူသံကွဲ:", StringComparison.Ordinal))
                                    {
                                        existingDom.UsageNote = existingDom.UsageNote + " | " + seg.Trim();
                                        break;
                                    }
                                }
                            }
                        }
                        if (!existingDom.IsOfficialThatpon && !string.IsNullOrEmpty(dCue) &&
                            (string.IsNullOrEmpty(existingDom.MeaningCue) ||
                             existingDom.MeaningCue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                             existingDom.MeaningCue == "မြန်မာပေါင်းစပ်ဝေါဟာရ" ||
                             existingDom.MeaningCue == "ခေတ်သုံး စံသတ်ပုံအမှန်"))
                        {
                            existingDom.MeaningCue = dCue;
                        }
                        specDomainEnriched++;
                    }
                    else
                    {
                        if (dPos != "PROPN" && officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                            continue;

                        map[w] = new EntryItem
                        {
                            Word = w,
                            MeaningCue = dCue,
                            PosTag = dPos,
                            UsageNote = dNote,
                            Frequency = Math.Max(140, dFreq),
                            IsOfficialThatpon = false
                        };
                        specDomainAdded++;
                    }
                }
            }
            Console.WriteLine("  - V11 5-Domain Specialized Lexicons: +" + specDomainAdded + " new domain headwords, " + specDomainEnriched + " enriched.");

            // M1.9. Ingest V13 Live *.gov.mm + V15 Published Books, Encyclopedia, Laws & News Lexicons
            int govDocsAdded = 0;
            int govDocsEnriched = 0;
            string[] curatedCorpusTsvPaths = new string[]
            {
                Path.Combine(sourcesDir, "official", "mlc_gov_published_docs_lexicon.tsv"),
                Path.Combine(sourcesDir, "official", "mlc_books_news_encyclopedia_lexicon.tsv")
            };
            foreach (string govDocsTsvPath in curatedCorpusTsvPaths)
            {
                if (!File.Exists(govDocsTsvPath)) continue;
                bool isV15CuratedTsv = govDocsTsvPath.EndsWith("mlc_books_news_encyclopedia_lexicon.tsv", StringComparison.OrdinalIgnoreCase);
                foreach (string rawLine in File.ReadAllLines(govDocsTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 5) continue;
                    string w = NormalizeMyanmar(cols[0].Trim());
                    string gPos = string.IsNullOrWhiteSpace(cols[1]) ? "N" : cols[1].Trim();
                    string gCue = NormalizeMyanmar(cols[2].Trim());
                    string gNote = cols[3].Trim();
                    int gFreq;
                    if (!int.TryParse(cols[4].Trim(), out gFreq)) gFreq = 200;
                    bool isAuthOfficial = cols.Length >= 6 && string.Equals(cols[5].Trim(), "OFFICIAL", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(w) || !PureMyanmarWordRegex.IsMatch(w) || KnownWrongBlacklist.Contains(w))
                        continue;

                    if (isAuthOfficial)
                    {
                        officialThatponSet.Add(w);
                    }

                    EntryItem existingGov;
                    if (map.TryGetValue(w, out existingGov))
                    {
                        existingGov.Frequency = Math.Max(existingGov.Frequency, gFreq);
                        if (isAuthOfficial)
                            existingGov.IsOfficialThatpon = true;

                        if (gPos == "PROPN")
                        {
                            if (existingGov.IsOfficialThatpon && existingGov.PosTag != "PROPN")
                            {
                                bool hasPropnHomograph = false;
                                for (int hi = 0; hi < homographEntries.Count; hi++)
                                {
                                    if (homographEntries[hi].Word == w && homographEntries[hi].PosTag == "PROPN")
                                    {
                                        hasPropnHomograph = true;
                                        break;
                                    }
                                }
                                if (!hasPropnHomograph)
                                {
                                    homographEntries.Add(new EntryItem
                                    {
                                        Word = w,
                                        MeaningCue = gCue,
                                        PosTag = "PROPN",
                                        UsageNote = gNote,
                                        Frequency = Math.Max(existingGov.Frequency, gFreq),
                                        IsOfficialThatpon = false
                                    });
                                }
                            }
                            else
                            {
                                existingGov.PosTag = "PROPN";
                            }
                        }

                        if (!string.IsNullOrEmpty(gNote))
                        {
                            if (string.IsNullOrEmpty(existingGov.UsageNote))
                            {
                                existingGov.UsageNote = gNote;
                            }
                            else if (existingGov.UsageNote.IndexOf("နယ်ပယ်:", StringComparison.Ordinal) < 0)
                            {
                                existingGov.UsageNote = gNote + " | " + existingGov.UsageNote;
                            }
                        }
                        if (!string.IsNullOrEmpty(gCue) &&
                            (string.IsNullOrEmpty(existingGov.MeaningCue) ||
                             existingGov.MeaningCue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                             existingGov.MeaningCue == "မြန်မာပေါင်းစပ်ဝေါဟာရ" ||
                             existingGov.MeaningCue == "ခေတ်သုံး စံသတ်ပုံအမှန်"))
                        {
                            existingGov.MeaningCue = gCue;
                        }
                        govDocsEnriched++;
                    }
                    else
                    {
                        if (!isAuthOfficial && !isV15CuratedTsv && gPos != "PROPN" && officialPurityKeys.Contains(ComputeExtendedPuritySkeleton(w)))
                            continue;

                        map[w] = new EntryItem
                        {
                            Word = w,
                            MeaningCue = gCue,
                            PosTag = gPos,
                            UsageNote = gNote,
                            Frequency = Math.Max(140, gFreq),
                            IsOfficialThatpon = isAuthOfficial
                        };
                        govDocsAdded++;
                    }
                }
            }
            Console.WriteLine("  - V13-V15 Books, Encyclopedia, News & *.gov.mm Docs Lexicon: +" + govDocsAdded + " new headwords, " + govDocsEnriched + " enriched.");

            // M1.10. Ingest V29 Official MOIP / GAD Myanmar National Registration Card (NRC) Township Codes (470 State/Township pairs -> 351 distinct codes)
            // Ingested AFTER officialPurityKeys so bare consonant initialisms never block unrelated multi-syllable vocabulary, while setting PosTag="PROPN" & IsOfficialThatpon=true!
            string ToMyanmarDigitsLocal(int num)
            {
                string ascii = num.ToString(CultureInfo.InvariantCulture);
                var sbDig = new StringBuilder(ascii.Length);
                for (int di = 0; di < ascii.Length; di++)
                {
                    char dc = ascii[di];
                    sbDig.Append((dc >= '0' && dc <= '9') ? (char)('\u1040' + (dc - '0')) : dc);
                }
                return sbDig.ToString();
            }

            var nrcByCode = new Dictionary<string, List<(int StateNum, string CodeEn, string TownshipMm)>>(StringComparer.Ordinal);
            for (int ni = 0; ni < nrcParsedItems.Count; ni++)
            {
                var item = nrcParsedItems[ni];
                if (!IsCleanHeadword(item.CodeMm))
                    continue;
                List<(int StateNum, string CodeEn, string TownshipMm)> bucket;
                if (!nrcByCode.TryGetValue(item.CodeMm, out bucket))
                {
                    bucket = new List<(int StateNum, string CodeEn, string TownshipMm)>(2);
                    nrcByCode[item.CodeMm] = bucket;
                }
                bucket.Add((item.StateNum, item.CodeEn, item.TownshipMm));
            }

            int nrcCodesRegistered = 0;
            foreach (var kvpNrc in nrcByCode.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string codeMm = kvpNrc.Key;
                var rows = kvpNrc.Value.OrderBy(x => x.StateNum).ThenBy(x => x.TownshipMm, StringComparer.Ordinal).ToList();
                var mmPairLabels = new List<string>(rows.Count);
                var enCodeLabels = new List<string>(rows.Count);
                string primaryEn = rows[0].CodeEn;

                for (int ri = 0; ri < rows.Count; ri++)
                {
                    string stMm = ToMyanmarDigitsLocal(rows[ri].StateNum);
                    string twnLabel = rows[ri].TownshipMm.EndsWith("မြို့နယ်", StringComparison.Ordinal) || rows[ri].TownshipMm.IndexOf('(') >= 0
                        ? rows[ri].TownshipMm
                        : (rows[ri].TownshipMm + "မြို့နယ်");
                    string pairDesc = stMm + "/" + codeMm + " (" + twnLabel + ")";
                    if (!mmPairLabels.Contains(pairDesc))
                        mmPairLabels.Add(pairDesc);

                    string enPair = rows[ri].StateNum.ToString(CultureInfo.InvariantCulture) + "/" + rows[ri].CodeEn;
                    if (!string.IsNullOrEmpty(rows[ri].CodeEn) && !enCodeLabels.Contains(enPair))
                        enCodeLabels.Add(enPair);
                }

                string nrcCue = "နိုင်ငံသားစိစစ်ရေးကတ်ပြား မြို့နယ်သင်္ကေတ — " + string.Join("၊ ", mmPairLabels);
                string enSummary = !string.IsNullOrEmpty(primaryEn)
                    ? (primaryEn + " (" + string.Join(", ", enCodeLabels.Take(4)) + ")")
                    : string.Join(", ", enCodeLabels.Take(4));
                string nrcNote = "အင်္ဂလိပ်သင်္ကေတ: " + enSummary +
                                 " | ကတ်အမျိုးအစား: (နိုင်)/(ဧည့်)/(ပြု)/(သာသနာ)/(ယာယီ)/(စ)" +
                                 " | အာဏာပိုင်စံနှုန်း: လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP / GAD)";

                officialThatponSet.Add(codeMm);

                EntryItem existingNrc;
                if (map.TryGetValue(codeMm, out existingNrc))
                {
                    existingNrc.IsOfficialThatpon = true;
                    existingNrc.Frequency = Math.Max(existingNrc.Frequency, 220);
                    if (!string.IsNullOrEmpty(existingNrc.MeaningCue) &&
                        existingNrc.MeaningCue.IndexOf("နိုင်ငံသားစိစစ်ရေး", StringComparison.Ordinal) < 0)
                    {
                        homographEntries.Add(new EntryItem
                        {
                            Word = codeMm,
                            MeaningCue = nrcCue,
                            PosTag = "PROPN",
                            UsageNote = nrcNote,
                            Frequency = 220,
                            IsOfficialThatpon = true
                        });
                    }
                    else
                    {
                        existingNrc.PosTag = "PROPN";
                        existingNrc.MeaningCue = nrcCue;
                        existingNrc.UsageNote = nrcNote;
                    }
                }
                else
                {
                    map[codeMm] = new EntryItem
                    {
                        Word = codeMm,
                        MeaningCue = nrcCue,
                        PosTag = "PROPN",
                        UsageNote = nrcNote,
                        Frequency = 220,
                        IsOfficialThatpon = true
                    };
                }

                if (!string.IsNullOrEmpty(primaryEn) && !ornagaiEnBuckets.ContainsKey(primaryEn))
                {
                    ornagaiEnBuckets[primaryEn] = new List<(string Pos, string MmGloss)>(1)
                    {
                        ("NRC", codeMm + " — " + string.Join("၊ ", mmPairLabels.Take(3)))
                    };
                }
                nrcCodesRegistered++;
            }
            Console.WriteLine("  - V29 Official MOIP/GAD NRC Township Codes: +" + nrcCodesRegistered + " distinct PROPN codes (" + nrcParsedItems.Count + " State/Township pairs).");

            for (int i = 0; i < homographEntries.Count; i++)
            {
                EntryItem main;
                if (map.TryGetValue(homographEntries[i].Word, out main))
                {
                    if (main.Frequency > homographEntries[i].Frequency)
                        homographEntries[i].Frequency = main.Frequency;
                    if (string.IsNullOrEmpty(homographEntries[i].UsageNote) && !string.IsNullOrEmpty(main.UsageNote))
                        homographEntries[i].UsageNote = main.UsageNote;
                }
            }

            EntryItem boEntry;
            if (map.TryGetValue("ဗိုလ်", out boEntry))
            {
                boEntry.Frequency = Math.Max(boEntry.Frequency, 520);
            }

            // M2. Prune zero-frequency bound-only classifier stems that collide phonetically with high-freq words
            var highFreqSkeletons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in map)
            {
                if (kv.Value.Frequency >= 400)
                    highFreqSkeletons.Add(ComputeExtendedPuritySkeleton(kv.Key));
            }
            foreach (string boundStem in unlockedClassifierStems)
            {
                EntryItem stItem;
                if (map.TryGetValue(boundStem, out stItem) && stItem.Frequency == 0 &&
                    highFreqSkeletons.Contains(ComputeExtendedPuritySkeleton(boundStem)))
                {
                    map.Remove(boundStem);
                    officialThatponSet.Remove(boundStem);
                    homographEntries.RemoveAll(h => h.Word == boundStem);
                }
            }

            // M3. PurgeKnownWrongBlacklist & Crowd/Ornagai Non-Official Orthographic Shadow Typos
            foreach (string badWord in KnownWrongBlacklist)
            {
                if (!officialThatponSet.Contains(badWord))
                {
                    map.Remove(badWord);
                    homographEntries.RemoveAll(h => h.Word == badWord);
                }
            }

            string[] productiveDerivSuffixes = new string[]
            {
                "ခြင်း", "မှု", "ချက်", "သူ", "သား", "ရေး", "ဖွယ်", "နည်း", "ပုံ", "ရာ", "စရာ"
            };
            var officialShadowBuckets = new Dictionary<string, List<EntryItem>>(StringComparer.Ordinal);
            foreach (var kv in map)
            {
                if (!kv.Value.IsOfficialThatpon)
                    continue;
                int sc = CountSyllables(kv.Key);
                if (sc < 2)
                    continue;
                string bKey = sc.ToString(CultureInfo.InvariantCulture) + "|" + ComputeShadowAuditBucket(kv.Key);
                List<EntryItem> bList;
                if (!officialShadowBuckets.TryGetValue(bKey, out bList))
                {
                    bList = new List<EntryItem>(4);
                    officialShadowBuckets[bKey] = bList;
                }
                bList.Add(kv.Value);
            }

            var shadowedCrowdTypos = new List<string>();
            Regex hasMmConsonantCueRx = new Regex(@"[\u1000-\u1021]", RegexOptions.Compiled);
            foreach (var kv in map)
            {
                EntryItem cand = kv.Value;
                if (cand.IsOfficialThatpon ||
                    cand.PosTag == "PROPN" || cand.PosTag == "LOAN" || cand.PosTag == "PART" ||
                    cand.PosTag == "ENCYC" || cand.PosTag == "PROV")
                {
                    continue;
                }

                string cue = (cand.MeaningCue ?? string.Empty).Trim();
                string note = cand.UsageNote ?? string.Empty;
                if (note.IndexOf("နယ်ပယ်:", StringComparison.Ordinal) >= 0)
                    continue;

                bool isCrowdOrUnverifiedCue =
                    string.IsNullOrEmpty(cue) ||
                    cue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                    cue == "မြန်မာပေါင်းစပ်ဝေါဟာရ" ||
                    cue == "ခေတ်သုံး စံဝေါဟာရ" ||
                    cue.StartsWith("အသံထွက်:", StringComparison.Ordinal) ||
                    !hasMmConsonantCueRx.IsMatch(cue);
                if (!isCrowdOrUnverifiedCue)
                    continue;

                bool isProductiveDerivative = false;
                for (int sx = 0; sx < productiveDerivSuffixes.Length; sx++)
                {
                    string suf = productiveDerivSuffixes[sx];
                    if (cand.Word.Length > suf.Length + 1 && cand.Word.EndsWith(suf, StringComparison.Ordinal))
                    {
                        string stem = cand.Word.Substring(0, cand.Word.Length - suf.Length);
                        if (officialThatponSet.Contains(stem))
                        {
                            isProductiveDerivative = true;
                            break;
                        }
                    }
                }
                if (isProductiveDerivative)
                    continue;

                int sc = CountSyllables(cand.Word);
                if (sc < 2)
                    continue;

                string bKey = sc.ToString(CultureInfo.InvariantCulture) + "|" + ComputeShadowAuditBucket(cand.Word);
                List<EntryItem> offList;
                if (!officialShadowBuckets.TryGetValue(bKey, out offList))
                    continue;

                for (int oi = 0; oi < offList.Count; oi++)
                {
                    EntryItem off = offList[oi];
                    if (off.Word == cand.Word)
                        continue;
                    double dist = MyanmarSpellChecker.Core.Nlp.SuggestionEngine.ComputeWeightedMyanmarDistance(cand.Word, off.Word, 0.32);
                    if (dist <= 0.32 && off.Frequency >= Math.Max(25, cand.Frequency * 3))
                    {
                        shadowedCrowdTypos.Add(cand.Word);
                        break;
                    }
                }
            }

            for (int si = 0; si < shadowedCrowdTypos.Count; si++)
            {
                string bad = shadowedCrowdTypos[si];
                map.Remove(bad);
                homographEntries.RemoveAll(h => h.Word == bad);
            }
            if (shadowedCrowdTypos.Count > 0)
            {
                Console.WriteLine("  - M3 Orthographic Shadow Audit: pruned " + shadowedCrowdTypos.Count + " crowd/corpus-shadowed typos.");
            }

            // N. Parse All Unconditional Orthography, Loanword, Medial, Compound & Typo Rules
            var extraYamlRules = new List<RuleItem>();
            ParseSimpleCorrectionYaml(Path.Combine(sourcesDir, "rules", "loan_word_corrections.yaml"), "မွေးစားဝေါဟာရ", map, officialThatponSet, extraYamlRules);
            ParseSimpleCorrectionYaml(Path.Combine(sourcesDir, "rules", "orthographic_corrections.yaml"), "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, extraYamlRules);
            ParseSimpleCorrectionYaml(Path.Combine(sourcesDir, "rules", "visarga_corrections.yaml"), "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, extraYamlRules);
            ParseSimpleCorrectionYaml(Path.Combine(sourcesDir, "rules", "typo_corrections.yaml"), "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း", map, officialThatponSet, extraYamlRules);
            ParseMinedLoanWordsYaml(Path.Combine(sourcesDir, "rules", "loan_words_mined.yaml"), officialThatponSet, map, homographEntries, extraYamlRules);
            ParseMedialUnconditionalYaml(Path.Combine(sourcesDir, "rules", "medial_confusion.yaml"), map, officialThatponSet, extraYamlRules);
            ParseCompoundConfusionYaml(Path.Combine(sourcesDir, "rules", "compound_confusion.yaml"), map, officialThatponSet, extraYamlRules);

            // O. Parse Data-Driven ContextualRules (V21 empirically audited TSV + homophone_confusion.yaml fallback)
            var rawCtxRules = new List<ContextRuleItem>();
            string v21CtxTsvPath = Path.Combine(sourcesDir, "rules", "mlc_v21_contextual_meaning_rules.tsv");
            if (File.Exists(v21CtxTsvPath))
            {
                string[] v21Lines = File.ReadAllLines(v21CtxTsvPath, Encoding.UTF8);
                for (int li = 1; li < v21Lines.Length; li++)
                {
                    if (string.IsNullOrWhiteSpace(v21Lines[li])) continue;
                    string[] cols = v21Lines[li].Split('\t');
                    if (cols.Length < 9) continue;
                    int winVal;
                    if (!int.TryParse(cols[5].Trim(), out winVal) || winVal <= 0)
                        winVal = 2;
                    rawCtxRules.Add(new ContextRuleItem
                    {
                        WrongForm = NormalizeMyanmar(cols[0].Trim()),
                        CorrectForm = NormalizeMyanmar(cols[1].Trim()),
                        TriggerWords = NormalizeMyanmar(cols[2].Trim()),
                        AntiTriggerWords = NormalizeMyanmar(cols[3].Trim()),
                        Direction = string.IsNullOrWhiteSpace(cols[4]) ? "Both" : cols[4].Trim(),
                        Window = winVal,
                        IsWarningOnly = cols[6].Trim() == "1" || string.Equals(cols[6].Trim(), "true", StringComparison.OrdinalIgnoreCase),
                        SkipParticles = cols[7].Trim() == "1" || string.Equals(cols[7].Trim(), "true", StringComparison.OrdinalIgnoreCase),
                        Explanation = cols[8].Trim()
                    });
                }
            }
            else
            {
                ParseCollocationsAndHomophoneContextRules(
                    Path.Combine(sourcesDir, "rules", "collocations.yaml"),
                    Path.Combine(sourcesDir, "rules", "homophone_confusion.yaml"),
                    rawCtxRules);

                rawCtxRules.Add(new ContextRuleItem
                {
                    WrongForm = "ဆစ်နီ",
                    CorrectForm = "ဆစ်ဒနီ",
                    TriggerWords = "ဩစတြေးလျ,မြို့,နိုင်ငံ,လေဆိပ်,ခရီး,မဲလ်ဘုန်း",
                    Direction = "Both",
                    Window = 4,
                    Explanation = "နိုင်ငံခြားရေးဝန်ကြီးဌာန (MOFA) စံနှုန်းအရ ဩစတြေးလျနိုင်ငံ၏ မြို့ကြီး Sydney ကို ဒထွေးသတ်မဟုတ်ဘဲ 'ဆစ်ဒနီ' ဟု 'ဒ' ထည့်သွင်းရေးသားရမည်။"
                });
                rawCtxRules.Add(new ContextRuleItem
                {
                    WrongForm = "ဟိုပုန်း",
                    CorrectForm = "ဟိုပုံး",
                    TriggerWords = "မြို့,မြို့နယ်,ရှမ်းပြည်နယ်,တောင်ကြီး,ပအိုဝ်း,ဒေသ",
                    Direction = "Both",
                    Window = 4,
                    Explanation = "အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာန (GAD) စံနှုန်းအရ ရှမ်းပြည်နယ်တောင်ပိုင်း မြို့နယ်အမည်မှန်ကို သေးသေးတင်ဖြင့် 'ဟိုပုံး' ဟု ရေးသားရမည်။"
                });
                rawCtxRules.Add(new ContextRuleItem
                {
                    WrongForm = "သန်လျှင်",
                    CorrectForm = "သန်လျင်",
                    TriggerWords = "မြို့,မြို့နယ်,ခရိုင်,တံတား,ရန်ကုန်,သီလဝါ,ဆိပ်ကမ်း",
                    Direction = "Both",
                    Window = 4,
                    Explanation = "အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာန (GAD) နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဟထိုးမပါဘဲ 'သန်လျင်' ဟု ရေးသားရမည်။"
                });
            }

            var cleanCtxRules = new List<ContextRuleItem>();
            var seenCtxKey = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rawCtxRules.Count; i++)
            {
                var cr = rawCtxRules[i];
                if (string.IsNullOrEmpty(cr.WrongForm) || string.IsNullOrEmpty(cr.CorrectForm) ||
                    cr.WrongForm == cr.CorrectForm || string.IsNullOrEmpty(cr.TriggerWords))
                    continue;
                string dedupKey = cr.WrongForm + "->" + cr.CorrectForm + "|" + cr.Direction + "|" + cr.TriggerWords;
                if (seenCtxKey.Add(dedupKey))
                {
                    cleanCtxRules.Add(cr);
                }
            }

            // P. Combine & Deduplicate Lexicon + Unconditional Orthography Rules
            var allEntries = map.Values.Concat(homographEntries)
                .OrderBy(e => e.Word, StringComparer.Ordinal)
                .ThenByDescending(e => e.IsOfficialThatpon)
                .ThenByDescending(e => !string.Equals(e.PosTag, "ENCYC", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(e => e.Frequency)
                .ThenBy(e => e.MeaningCue, StringComparer.Ordinal)
                .ToList();

            RuleItem[] authoritativeExtraRules = LoadRuleTsv(Path.Combine(sourcesDir, "rules", "mlc_authoritative_orthography_rules.tsv"));

            var cleanRules = new List<RuleItem>();
            var seenWrong = new HashSet<string>(StringComparer.Ordinal);
            var combinedInputRules = authoritativeExtraRules.Concat(ministryRules).Concat(khweHtarRules).Concat(loanRules).Concat(extraYamlRules);

            var ministryWrongSet = new HashSet<string>(
                authoritativeExtraRules.Concat(ministryRules).Concat(khweHtarRules).Select(mr => NormalizeMyanmar(mr.WrongForm)),
                StringComparer.Ordinal);
            foreach (var r in combinedInputRules)
            {
                string wf = NormalizeMyanmar(r.WrongForm);
                string cf = NormalizeMyanmar(r.CorrectForm);
                if (string.IsNullOrEmpty(wf) || string.IsNullOrEmpty(cf) || wf == cf || string.IsNullOrEmpty(r.Explanation))
                    continue;
                if ((ToxicExternalRuleWrongForms.Contains(wf) && cf != "ဝိညာဉ်") || cf == "ဝိညာဏ")
                    continue;
                if (KnownWrongBlacklist.Contains(cf))
                    continue;
                if (officialThatponSet.Contains(wf) && r.Category != "OfficialStyle")
                    continue;
                if (!ministryWrongSet.Contains(wf) && map.ContainsKey(wf) && r.Category != "OfficialStyle")
                    continue;

                if (seenWrong.Add(wf))
                {
                    cleanRules.Add(new RuleItem
                    {
                        WrongForm = wf,
                        CorrectForm = cf,
                        Category = r.Category,
                        Explanation = r.Explanation
                    });
                    if (r.Category != "OfficialStyle")
                    {
                        allEntries.RemoveAll(e => e.Word == wf);
                        map.Remove(wf);
                    }
                    EntryItem existingRuleCf;
                    if (!map.TryGetValue(cf, out existingRuleCf))
                    {
                        var addedCf = new EntryItem
                        {
                            Word = cf,
                            MeaningCue = officialThatponSet.Contains(cf) ? "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်" : "ခေတ်သုံး စံသတ်ပုံအမှန်",
                            PosTag = "N",
                            IsOfficialThatpon = officialThatponSet.Contains(cf)
                        };
                        allEntries.Add(addedCf);
                        map[cf] = addedCf;
                    }
                    else if (existingRuleCf.PosTag == "PROVERB" || existingRuleCf.PosTag == "SAYING" ||
                             existingRuleCf.PosTag == "RHETORIC" || existingRuleCf.PosTag == "ENCYC")
                    {
                        existingRuleCf.PosTag = "N";
                    }
                }
            }

            // Ensure 100% of single-word Myanmar ContextualRule CorrectForms exist in active lexicon
            var protectedCorrectForms = new HashSet<string>(cleanRules.Select(r => r.CorrectForm), StringComparer.Ordinal);
            for (int ci = 0; ci < cleanCtxRules.Count; ci++)
            {
                string ccf = cleanCtxRules[ci].CorrectForm;
                if (!string.IsNullOrEmpty(ccf) &&
                    PureMyanmarWordRegex.IsMatch(ccf) &&
                    !KnownWrongBlacklist.Contains(ccf) &&
                    !seenWrong.Contains(ccf))
                {
                    protectedCorrectForms.Add(ccf);
                    EntryItem existingCtxCf;
                    if (!map.TryGetValue(ccf, out existingCtxCf))
                    {
                        var addedCtxCf = new EntryItem
                        {
                            Word = ccf,
                            MeaningCue = officialThatponSet.Contains(ccf) ? "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်" : "ခေတ်သုံး စံသတ်ပုံအမှန်",
                            PosTag = "N",
                            IsOfficialThatpon = officialThatponSet.Contains(ccf)
                        };
                        allEntries.Add(addedCtxCf);
                        map[ccf] = addedCtxCf;
                    }
                    else if (existingCtxCf.PosTag == "PROVERB" || existingCtxCf.PosTag == "SAYING" ||
                             existingCtxCf.PosTag == "RHETORIC" || existingCtxCf.PosTag == "ENCYC")
                    {
                        existingCtxCf.PosTag = "N";
                    }
                }
            }

            // P2. Purge Non-Official Crowd Entries Containing Syllable-Aligned Condemned Multi-Syllable WrongForms!
            // (Removes polluted crowd compounds like 'စေတနာပြဌာန်း', 'ပြဌာန်းထား', 'သန္နိဌာန်ချ', 'အဆင်းသဏ္ဍာန်', 'အရာဝတ္တု', 'ကော့မှုးမြို့နယ်')
            var condemnedMultiSylSet = new HashSet<string>(StringComparer.Ordinal);
            for (int ri = 0; ri < cleanRules.Count; ri++)
            {
                if (cleanRules[ri].Category == "OfficialStyle") continue;
                var rSyls = SyllableCountRegex.Matches(cleanRules[ri].WrongForm);
                if (rSyls.Count >= 2)
                {
                    var sbKey = new StringBuilder();
                    for (int k = 0; k < rSyls.Count; k++)
                    {
                        if (k > 0) sbKey.Append('|');
                        sbKey.Append(rSyls[k].Value);
                    }
                    condemnedMultiSylSet.Add(sbKey.ToString());
                }
            }

            var pollutedWordsToRemove = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in map)
            {
                bool isProtectedPropn = kv.Value.PosTag == "PROPN" &&
                                        kv.Key.IndexOf("ကော့မှုး", StringComparison.Ordinal) < 0 &&
                                        kv.Key.IndexOf("ဆည်းမြောင်း", StringComparison.Ordinal) < 0;
                if (kv.Value.IsOfficialThatpon || isProtectedPropn || protectedCorrectForms.Contains(kv.Key)) continue;
                var wSyls = SyllableCountRegex.Matches(kv.Key);
                int n = wSyls.Count;
                if (n < 2) continue;
                bool isPolluted = false;
                for (int a = 0; a < n && !isPolluted; a++)
                {
                    var sbSub = new StringBuilder();
                    sbSub.Append(wSyls[a].Value);
                    for (int b = a + 1; b < n; b++)
                    {
                        sbSub.Append('|').Append(wSyls[b].Value);
                        if (condemnedMultiSylSet.Contains(sbSub.ToString()))
                        {
                            isPolluted = true;
                            break;
                        }
                    }
                }
                if (isPolluted)
                    pollutedWordsToRemove.Add(kv.Key);
            }
            foreach (string pw in pollutedWordsToRemove)
            {
                map.Remove(pw);
            }
            if (pollutedWordsToRemove.Count > 0)
            {
                allEntries.RemoveAll(e => !e.IsOfficialThatpon && pollutedWordsToRemove.Contains(e.Word));
            }

            // P2b. V26 Promote up to 650 verified pure-Myanmar Encyclopedia single-word headwords (100% Official သတ်ပုံကျမ်း syllables, 0 condemned substrings)
            var officialAttestedSyllables = new HashSet<string>(StringComparer.Ordinal);
            foreach (string ow in officialThatponSet)
            {
                foreach (Match om in SyllableCountRegex.Matches(ow))
                {
                    if (om.Length > 0)
                        officialAttestedSyllables.Add(om.Value);
                }
            }

            string encycPreScanPath = Path.Combine(sourcesDir, "dictionaries", "mlc_mm_encyclopedia_knowledge.tsv");
            int promotedEncycHeadwords = 0;
            if (File.Exists(encycPreScanPath))
            {
                foreach (string rawLine in File.ReadAllLines(encycPreScanPath, Encoding.UTF8))
                {
                    if (promotedEncycHeadwords >= 650) break;
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    int tab1 = rawLine.IndexOf('\t');
                    if (tab1 <= 0) continue;
                    string candEncycWord = NormalizeMyanmar(rawLine.Substring(0, tab1).Trim());
                    if (candEncycWord.Length < 4 || candEncycWord.Length > 28) continue;
                    if (map.ContainsKey(candEncycWord) || seenWrong.Contains(candEncycWord) || !IsCleanHeadword(candEncycWord))
                        continue;
                    if (HasTrailingInflectionParticle(candEncycWord) || StartsWithBoundClausalParticle(candEncycWord))
                        continue;

                    var eSyls = SyllableCountRegex.Matches(candEncycWord);
                    int esc = eSyls.Count;
                    if (esc < 2 || esc > 6) continue;
                    int totalMatchedLen = 0;
                    bool allSylsOfficial = true;
                    for (int si = 0; si < esc; si++)
                    {
                        totalMatchedLen += eSyls[si].Length;
                        if (!officialAttestedSyllables.Contains(eSyls[si].Value))
                        {
                            allSylsOfficial = false;
                            break;
                        }
                    }
                    if (!allSylsOfficial || totalMatchedLen != candEncycWord.Length)
                        continue;

                    bool hasCondemnedSub = false;
                    for (int a = 0; a < esc && !hasCondemnedSub; a++)
                    {
                        var sbSub = new StringBuilder();
                        sbSub.Append(eSyls[a].Value);
                        for (int b = a + 1; b < esc; b++)
                        {
                            sbSub.Append('|').Append(eSyls[b].Value);
                            if (condemnedMultiSylSet.Contains(sbSub.ToString()))
                            {
                                hasCondemnedSub = true;
                                break;
                            }
                        }
                    }
                    if (hasCondemnedSub) continue;

                    string[] eCols = rawLine.Split('\t');
                    string shortCue = eCols.Length >= 3 ? NormalizeMyanmar(eCols[2].Trim()) : "မြန်မာစွယ်စုံကျမ်းဝင် ဝေါဟာရ";
                    if (shortCue.Length > 110)
                        shortCue = shortCue.Substring(0, 107).TrimEnd() + "…";

                    var promItem = new EntryItem
                    {
                        Word = candEncycWord,
                        PosTag = "PROPN",
                        MeaningCue = shortCue,
                        Frequency = 35,
                        IsOfficialThatpon = false
                    };
                    map[candEncycWord] = promItem;
                    allEntries.Add(promItem);
                    promotedEncycHeadwords++;
                }
            }
            if (promotedEncycHeadwords > 0)
            {
                Console.WriteLine("  - V26 Pure-Myanmar Encyclopedia Headwords Promoted: +" + promotedEncycHeadwords + " (100% official syllable-verified).");
            }

            // P3. V16 Massive Myanmar-to-Myanmar Knowledge Synthesis & Duplicate English-Only Homograph Cleanup
            var enToMmGlossMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string enGlossTsvPath = Path.Combine(sourcesDir, "dictionaries", "mlc_wiktionary_en_to_mm_glosses.tsv");
            if (File.Exists(enGlossTsvPath))
            {
                foreach (string egLine in File.ReadAllLines(enGlossTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(egLine)) continue;
                    int tabIdx = egLine.IndexOf('\t');
                    if (tabIdx <= 0 || tabIdx + 1 >= egLine.Length) continue;
                    string ek = egLine.Substring(0, tabIdx).Trim();
                    if (string.Equals(ek, "roof liles", StringComparison.OrdinalIgnoreCase)) continue;
                    string rawMv = egLine.Substring(tabIdx + 1).Trim();
                    var wikZgSpans = MyanmarSpellChecker.Core.Nlp.UnicodeNormalizer.FindZawgyiSpans(rawMv);
                    if (wikZgSpans.Count > 0)
                    {
                        for (int zi = wikZgSpans.Count - 1; zi >= 0; zi--)
                        {
                            var sp = wikZgSpans[zi];
                            rawMv = rawMv.Substring(0, sp.StartIndex) + sp.ConvertedUnicode + rawMv.Substring(sp.StartIndex + sp.Length);
                        }
                    }
                    string mv = NormalizeMyanmar(rawMv);
                    if (ek.Length >= 2 && mv.Length >= 2 && !enToMmGlossMap.ContainsKey(ek))
                    {
                        enToMmGlossMap[ek] = mv;
                        if (!VulgarOrnagaiEnglishRegex.IsMatch(ek))
                        {
                            string[] wParts = mv.Split(new char[] { '၊', '။', ',', ';', '•' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int wpi = 0; wpi < wParts.Length; wpi++)
                            {
                                string candMm = NormalizeMyanmar(wParts[wpi].Trim());
                                if (candMm.Length >= 1 && candMm.IndexOf(' ') < 0 && map.ContainsKey(candMm))
                                    AddMmEnglishSense(candMm, ek);
                            }
                        }
                    }
                }
            }

            bool IsSelfReferentialGloss(string word, string gloss)
            {
                if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(gloss))
                    return false;
                string normWord = NormalizeMyanmar(word.Trim());
                string wholeClean = NormalizeMyanmar(gloss.Trim(' ', '။', '၊', '.', ';', ':', '•', '~', '"', '\'', '“', '”', '(', ')'));
                if (wholeClean.Length > 0 && string.Equals(wholeClean, normWord, StringComparison.Ordinal))
                    return true;
                string[] parts = gloss.Split(new char[] { '•', '၊', ';', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return true;
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    string rawPart = Regex.Replace(parts[pi].Trim(), @"^(?:\[[^\]]*\]\s*|[၁၂၃၄၅၆၇၈၉၀0-9]+\s*[၊။\.\)]\s*)+", "");
                    string cleaned = NormalizeMyanmar(rawPart.Trim(' ', '။', '၊', '.', ';', ':', '•', '~', '"', '\'', '“', '”', '(', ')'));
                    if (cleaned.Length > 0 && !string.Equals(cleaned, normWord, StringComparison.Ordinal))
                        return false;
                }
                return true;
            }

            bool NeedsAuthenticMmDefinition(string cue, string headword = null)
            {
                if (string.IsNullOrWhiteSpace(cue))
                    return true;
                if (cue == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                    cue == "မြန်မာပေါင်းစပ်ဝေါဟာရ" ||
                    cue == "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်" ||
                    cue == "ခေတ်သုံး စံသတ်ပုံအမှန်" ||
                    cue.StartsWith("အသံထွက်:", StringComparison.Ordinal) ||
                    cue.StartsWith("တွဲဖက်:", StringComparison.Ordinal) ||
                    cue.StartsWith("သတ်ပုံကျမ်း တွဲဖက်အညွှန်း", StringComparison.Ordinal) ||
                    cue.StartsWith("သတ်ပုံကျမ်း ခွဲထားအမှန်", StringComparison.Ordinal))
                {
                    return true;
                }
                if (startsWithLatinGlossRx.IsMatch(cue) || !hasMyanmarConsonantRx.IsMatch(cue))
                    return true;
                if (!string.IsNullOrEmpty(headword) && IsSelfReferentialGloss(headword, cue))
                    return true;
                return false;
            }

            string BalanceShortenedParentheses(string s)
            {
                if (string.IsNullOrEmpty(s) || s.IndexOf('(') < 0)
                    return s;
                int openDepth = 0;
                int lastUnmatchedOpen = -1;
                for (int idx = 0; idx < s.Length; idx++)
                {
                    if (s[idx] == '(')
                    {
                        if (openDepth == 0) lastUnmatchedOpen = idx;
                        openDepth++;
                    }
                    else if (s[idx] == ')' && openDepth > 0)
                    {
                        openDepth--;
                    }
                }
                if (openDepth > 0)
                {
                    if (lastUnmatchedOpen >= 6)
                        return s.Substring(0, lastUnmatchedOpen).TrimEnd(' ', '—', '-', '၊', ',');
                    return s + new string(')', openDepth);
                }
                return s;
            }

            string ShortenStemGloss(string g, string stemWord = null)
            {
                if (string.IsNullOrEmpty(g)) return "";
                g = g.Trim();
                int khweIdx = g.IndexOf("(နှိုင်းယှဉ်:", StringComparison.Ordinal);
                if (khweIdx > 0) g = g.Substring(0, khweIdx).Trim();

                int dashIdx = g.IndexOf(" — ", StringComparison.Ordinal);
                if (dashIdx > 0)
                {
                    string leftDash = g.Substring(0, dashIdx).Trim();
                    string rightDash = g.Substring(dashIdx + 3).Trim();
                    if (leftDash.IndexOf('~') >= 0 && rightDash.Length >= 2 && rightDash.IndexOf('~') < 0)
                        g = rightDash;
                    else if (dashIdx > 8)
                        g = leftDash;
                }

                g = Regex.Replace(g, @"^[၁၂၃၄၅၆၇၈၉၀0-9]+\s*[၊။\.\)]\s*", "");
                int bulletIdx = g.IndexOf(" • ", StringComparison.Ordinal);
                if (bulletIdx > 4) g = g.Substring(0, bulletIdx);
                int sentIdx = g.IndexOf('။');
                if (sentIdx > 3) g = g.Substring(0, sentIdx);

                if (g.IndexOf('~') >= 0)
                {
                    g = Regex.Replace(g, @"\(\s*~[^)]*\)", "").Trim();
                    g = !string.IsNullOrEmpty(stemWord) ? g.Replace("~", stemWord) : g.Replace("~", "");
                    g = Regex.Replace(g, @"-(?=\s*[၊။,;)]|$)", "");
                }

                g = g.Trim(' ', '။', '.', '•', '၊', '-');
                if (g.Length > 48)
                    g = g.Substring(0, 45).TrimEnd() + "…";
                return BalanceShortenedParentheses(g);
            }

            void PromoteEnglishCueToUsageNote(EntryItem target, string rawEnCue)
            {
                if (target == null || string.IsNullOrWhiteSpace(rawEnCue))
                    return;
                string enPart = rawEnCue.Trim();
                string pronPart = null;
                int pronIdx = enPart.IndexOf("• အသံထွက်:", StringComparison.Ordinal);
                if (pronIdx < 0) pronIdx = enPart.IndexOf("| အသံထွက်:", StringComparison.Ordinal);
                if (pronIdx >= 0)
                {
                    pronPart = enPart.Substring(pronIdx + 1).Trim();
                    enPart = enPart.Substring(0, pronIdx).Trim();
                }
                if (!string.IsNullOrEmpty(pronPart) && (string.IsNullOrEmpty(target.UsageNote) || target.UsageNote.IndexOf("အသံထွက်:", StringComparison.Ordinal) < 0))
                {
                    target.UsageNote = string.IsNullOrEmpty(target.UsageNote)
                        ? pronPart
                        : (target.UsageNote + " | " + pronPart);
                }
                if (!string.IsNullOrEmpty(enPart) && (string.IsNullOrEmpty(target.UsageNote) || target.UsageNote.IndexOf("အင်္ဂလိပ်", StringComparison.Ordinal) < 0))
                {
                    target.UsageNote = string.IsNullOrEmpty(target.UsageNote)
                        ? ("အင်္ဂလိပ်အနက်: " + enPart)
                        : (target.UsageNote + " | အင်္ဂလိပ်အနက်: " + enPart);
                }
            }

            // Step P3-a: Remove redundant English-only / placeholder duplicate homograph entries when primary headword already has a Myanmar definition
            int dedupedEnglishHomographs = 0;
            allEntries.RemoveAll(e =>
            {
                EntryItem primary;
                if (map.TryGetValue(e.Word, out primary) && !ReferenceEquals(e, primary))
                {
                    if (NeedsAuthenticMmDefinition(e.MeaningCue, e.Word) && !NeedsAuthenticMmDefinition(primary.MeaningCue, primary.Word))
                    {
                        bool primaryForeignPropn = primary.PosTag == "PROPN" &&
                            (string.IsNullOrEmpty(primary.UsageNote) ||
                             (primary.UsageNote.IndexOf("မြန်မာနိုင်ငံ", StringComparison.Ordinal) < 0 &&
                              primary.UsageNote.IndexOf("မြို့နယ်", StringComparison.Ordinal) < 0));
                        if (!primaryForeignPropn &&
                            !string.IsNullOrWhiteSpace(e.MeaningCue) &&
                            startsWithLatinGlossRx.IsMatch(e.MeaningCue))
                        {
                            PromoteEnglishCueToUsageNote(primary, e.MeaningCue);
                        }
                        dedupedEnglishHomographs++;
                        return true;
                    }
                }
                return false;
            });

            // Step P3-b: Build lookup of all verified Myanmar-to-Myanmar stem definitions
            var realMmStemDefs = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < allEntries.Count; i++)
            {
                var e = allEntries[i];
                if (e.Word.Length <= 5 && (e.PosTag == "PROPN" || e.PosTag == "ENCYC"))
                    continue;
                if (!NeedsAuthenticMmDefinition(e.MeaningCue, e.Word) && !realMmStemDefs.ContainsKey(e.Word))
                {
                    string sg = ShortenStemGloss(e.MeaningCue, e.Word);
                    if (sg.Length >= 2 &&
                        !string.Equals(sg, e.Word, StringComparison.Ordinal) &&
                        sg.IndexOf("မြို့နယ်/ဒေသအမည်", StringComparison.Ordinal) < 0)
                    {
                        realMmStemDefs[e.Word] = sg;
                    }
                }
            }

            // Common 1-syllable Burmese modifiers/roots fallback for transparent compound definitions
            (string Mod, string Def, bool OverrideHomograph)[] commonCompoundRoots = new (string, string, bool)[]
            {
                ("ကောင်း", "မွန်မြတ်သင့်လျော်သော", false), ("ဆိုး", "မကောင်းသော", false), ("ကြီး", "ပမာဏ/အဆင့် မြင့်မားသော", false),
                ("ငယ်", "သေးငယ်သော", false), ("သစ်", "အသစ်ဖြစ်သော", false), ("ဟောင်း", "ရှေးကျသော", false),
                ("ကင်း", "လွတ်ကင်းသော", false), ("ပြည့်", "ပြည့်စုံသော", false), ("မဲ့", "ကင်းမဲ့သော", false),
                ("ဆရာ", "ပညာသင်ကြားပေးသူ / ကျွမ်းကျင်ပညာရှင်", false), ("ဘေး", "အန္တရာယ်၊ ဘေးဥပဒ်", false),
                ("လမ်း", "သွားလာရာ လမ်းကြောင်း", false), ("ရေ", "သောက်သုံးရေ / အရည်", false), ("မီး", "အပူနှင့် အလင်းစွမ်းအင်", false),
                ("မြေ", "ကမ္ဘာမြေပြင်", false), ("လေ", "လေထု", false), ("စိတ်", "သိတတ်သော သဘောတရား", false),
                ("လယ်", "စပါးစိုက်ပျိုးရာ လယ်ယာမြေ", true), ("ရုံး", "အုပ်ချုပ်စီမံရာ ရုံးဌာန", true), ("ရေး", "စာရေးသားခြင်း / အရေးကိစ္စ", true)
            };
            for (int cr = 0; cr < commonCompoundRoots.Length; cr++)
            {
                if (commonCompoundRoots[cr].OverrideHomograph || !realMmStemDefs.ContainsKey(commonCompoundRoots[cr].Mod))
                    realMmStemDefs[commonCompoundRoots[cr].Mod] = commonCompoundRoots[cr].Def;
            }

            string StripSelfSenseFromGloss(string headword, string rawMmHit)
            {
                if (string.IsNullOrWhiteSpace(rawMmHit))
                    return null;
                if (string.IsNullOrEmpty(headword))
                    return rawMmHit;
                if (IsSelfReferentialGloss(headword, rawMmHit))
                    return null;
                string[] bullets = rawMmHit.Split(new[] { " • " }, StringSplitOptions.RemoveEmptyEntries);
                if (bullets.Length <= 1)
                    return rawMmHit;
                var kept = new List<string>(bullets.Length);
                for (int bi = 0; bi < bullets.Length; bi++)
                {
                    if (!IsSelfReferentialGloss(headword, bullets[bi]))
                        kept.Add(bullets[bi].Trim());
                }
                return kept.Count > 0 ? string.Join(" • ", kept) : null;
            }

            string LookupEnglishSenseInMm(string rawEn, string headword = null)
            {
                if (string.IsNullOrWhiteSpace(rawEn)) return null;
                string[] parts = rawEn.Split(new char[] { ';', ',', '/' }, StringSplitOptions.RemoveEmptyEntries);
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    string cand = Regex.Replace(parts[pi], @"\([^)]*\)|\[[^\]]*\]", " ").Trim(' ', '.', '-', ':', '\'', '"').ToLowerInvariant();
                    string[] stripLeads = { "to be ", "to ", "be ", "a kind of ", "kind of ", "a ", "an ", "the ", "of " };
                    for (int sl = 0; sl < stripLeads.Length; sl++)
                    {
                        if (cand.StartsWith(stripLeads[sl], StringComparison.Ordinal))
                        {
                            cand = cand.Substring(stripLeads[sl].Length).Trim();
                            break;
                        }
                    }
                    if (cand.Length < 2) continue;
                    string mmHit;
                    if (enToMmGlossMap.TryGetValue(cand, out mmHit))
                    {
                        string filtered = StripSelfSenseFromGloss(headword, mmHit);
                        if (!string.IsNullOrEmpty(filtered)) return filtered;
                    }
                    if (cand.EndsWith("ing", StringComparison.Ordinal) && cand.Length > 5)
                    {
                        string baseStem = cand.Substring(0, cand.Length - 3);
                        if (enToMmGlossMap.TryGetValue(baseStem, out mmHit) || enToMmGlossMap.TryGetValue(baseStem + "e", out mmHit))
                        {
                            string filtered = StripSelfSenseFromGloss(headword, mmHit);
                            if (!string.IsNullOrEmpty(filtered)) return filtered;
                        }
                    }
                    if (cand.EndsWith("ed", StringComparison.Ordinal) && cand.Length > 4)
                    {
                        string baseStem = cand.Substring(0, cand.Length - 2);
                        if (enToMmGlossMap.TryGetValue(baseStem, out mmHit) || enToMmGlossMap.TryGetValue(cand.Substring(0, cand.Length - 1), out mmHit))
                        {
                            string filtered = StripSelfSenseFromGloss(headword, mmHit);
                            if (!string.IsNullOrEmpty(filtered)) return filtered;
                        }
                    }
                    if (cand.EndsWith("s", StringComparison.Ordinal) && cand.Length > 3 && !cand.EndsWith("ss", StringComparison.Ordinal))
                    {
                        string baseStem = cand.Substring(0, cand.Length - 1);
                        if (enToMmGlossMap.TryGetValue(baseStem, out mmHit))
                        {
                            string filtered = StripSelfSenseFromGloss(headword, mmHit);
                            if (!string.IsNullOrEmpty(filtered)) return filtered;
                        }
                    }
                    // Multi-word English gloss: try head word if 2 words (e.g. 'musical instrument' -> 'instrument')
                    string[] enWords = cand.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (enWords.Length == 2)
                    {
                        string w1Mm, w2Mm;
                        if (enToMmGlossMap.TryGetValue(enWords[0], out w1Mm) && enToMmGlossMap.TryGetValue(enWords[1], out w2Mm))
                        {
                            string s1 = ShortenStemGloss(w1Mm);
                            string s2 = ShortenStemGloss(w2Mm);
                            if (!string.IsNullOrEmpty(s1) && !string.IsNullOrEmpty(s2))
                            {
                                string comb = s2 + " (" + s1 + ")။";
                                if (!IsSelfReferentialGloss(headword, comb))
                                    return comb;
                            }
                        }
                        if (enToMmGlossMap.TryGetValue(enWords[1], out w2Mm) && enWords[1].Length >= 4 && !string.Equals(enWords[1], "work", StringComparison.Ordinal))
                        {
                            string filtered = StripSelfSenseFromGloss(headword, w2Mm);
                            if (!string.IsNullOrEmpty(filtered)) return filtered;
                        }
                    }
                }
                return null;
            }

            (string Suffix, string Template)[] productiveSuffixes = new (string, string)[]
            {
                ("ဝန်ကြီးဌာန", "{0}ဆိုင်ရာ ပြည်ထောင်စု ဝန်ကြီးဌာန ({1})။"),
                ("ဦးစီးဌာန", "{0}ဆိုင်ရာ အစိုးရ ဦးစီးဌာန ({1})။"),
                ("ကော်မရှင်", "{0}ဆိုင်ရာ ကြီးကြပ်ဆောင်ရွက်သည့် ကော်မရှင်အဖွဲ့ ({1})။"),
                ("စီမံကိန်း", "{0}ဆိုင်ရာ ရည်မှန်းချက် စီမံကိန်း ({1})။"),
                ("လုပ်ငန်း", "{0}နှင့် သက်ဆိုင်သော စီးပွားရေး သို့မဟုတ် ဆောင်ရွက်မှု လုပ်ငန်း ({1})။"),
                ("ကော်မတီ", "{0}ဆိုင်ရာ ပူးပေါင်းဆောင်ရွက်ရေး ကော်မတီအဖွဲ့ ({1})။"),
                ("တပ်ဖွဲ့", "{0}ဆိုင်ရာ လုံခြုံရေး သို့မဟုတ် ကာကွယ်ရေး တပ်ဖွဲ့ ({1})။"),
                ("ကျောင်း", "{0}သင်ကြားရာ သို့မဟုတ် ဆိုင်ရာ ကျောင်း ({1})။"),
                ("ဆေးရုံ", "{0}ကုသသည့် ဆေးရုံ ({1})။"),
                ("စက်ရုံ", "{0}ထုတ်လုပ်သည့် စက်ရုံ ({1})။"),
                ("စခန်း", "{0}ဆိုင်ရာ အခြေစိုက်စခန်း သို့မဟုတ် နေရာ ({1})။"),
                ("ဥပဒေ", "{0}ဆိုင်ရာ ပြဋ္ဌာန်းသတ်မှတ်ထားသော ဥပဒေ ({1})။"),
                ("ရောဂါ", "{0}ကြောင့်ဖြစ်သော သို့မဟုတ် ယင်းဆိုင်ရာ ရောဂါဝေဒနာ ({1})။"),
                ("အဖွဲ့", "{0}ဆိုင်ရာ စုဖွဲ့ထားသော အဖွဲ့အစည်း ({1})။"),
                ("စနစ်", "{0}ခြင်းဆိုင်ရာ စည်းမျဉ်းစနစ် ({1})။"),
                ("ပညာ", "{0}ခြင်းဆိုင်ရာ အတတ်ပညာရပ် ({1})။"),
                ("သမား", "{0}ခြင်း၌ ကျွမ်းကျင်သူ သို့မဟုတ် လုပ်ကိုင်သူ ({1})။"),
                ("ဌာန", "{0}ဆိုင်ရာ ရုံးဌာန သို့မဟုတ် ဌာနစိတ် ({1})။"),
                ("ခြင်း", "{0}သည့် အပြုအမူ သို့မဟုတ် ဖြစ်စဉ် ({1})။"),
                ("မှု", "{0}သည့် ကိစ္စရပ် သို့မဟုတ် အခြေအနေ ({1})။"),
                ("ရေး", "{0}ခြင်းဆိုင်ရာ လုပ်ငန်းကိစ္စ ({1})။"),
                ("ချက်", "{0}ထားသော အချက်အလက် သို့မဟုတ် ဖော်ပြချက် ({1})။"),
                ("နည်း", "{0}သည့် နည်းလမ်းစနစ် ({1})။"),
                ("ရှင်", "{0}ကို ပိုင်ဆိုင်သူ သို့မဟုတ် ဆောင်ရွက်သူ ({1})။"),
                ("သူ", "{0}သည့် ပုဂ္ဂိုလ် ({1})။"),
                ("ဖွယ်", "{0}ထိုက်သော / {0}ဖွယ်ရာ အကြောင်းအရာ ({1})။"),
                ("စရာ", "{0}ရန် အကြောင်းအရာ သို့မဟုတ် ပစ္စည်း ({1})။"),
                ("စွာ", "{0}သော အခြင်းအရာဖြင့် ({1})။"),
                ("ခန်း", "{0}ရာ အခန်း သို့မဟုတ် နေရာ ({1})။"),
                ("ရုံး", "{0}ဆိုင်ရာ ရုံးဌာန ({1})။"),
                ("ရုံ", "{0}ရာ အဆောက်အအုံ ({1})။"),
                ("ဆိုင်", "{0}ရောင်းဝယ်ရာ ဆိုင် ({1})။"),
                ("ကွင်း", "{0}ရာ ကွင်းပြင်နေရာ ({1})။"),
                ("ပင်", "{0}အပင်မျိုး ({1})။"),
                ("သီး", "{0}ပင်မှ သီးသော အသီး ({1})။")
            };

            bool IsExactSyllableBoundary(MatchCollection sylMatches, int charPos)
            {
                for (int si = 1; si < sylMatches.Count; si++)
                {
                    if (sylMatches[si].Index == charPos)
                        return true;
                }
                return false;
            }

            int mmTranslatedFromEn = 0;
            int mmMorphSynthesized = 0;
            int mmCompoundSynthesized = 0;
            int mmPaliOrOrthSynthesized = 0;

            for (int i = 0; i < allEntries.Count; i++)
            {
                var e = allEntries[i];
                if (!NeedsAuthenticMmDefinition(e.MeaningCue, e.Word))
                    continue;

                bool isForeignPropn =
                    e.PosTag == "PROPN" &&
                    (string.IsNullOrEmpty(e.UsageNote) ||
                     (e.UsageNote.IndexOf("မြန်မာနိုင်ငံ", StringComparison.Ordinal) < 0 &&
                      e.UsageNote.IndexOf("မြို့နယ်", StringComparison.Ordinal) < 0));

                string oldCue = (e.MeaningCue ?? "").Trim();
                bool oldWasEnglish = !string.IsNullOrEmpty(oldCue) && (startsWithLatinGlossRx.IsMatch(oldCue) || !hasMyanmarConsonantRx.IsMatch(oldCue));
                bool oldWasColloc = oldCue.StartsWith("တွဲဖက်:", StringComparison.Ordinal) ||
                                    oldCue.StartsWith("သတ်ပုံကျမ်း တွဲဖက်အညွှန်း", StringComparison.Ordinal) ||
                                    oldCue.StartsWith("သတ်ပုံကျမ်း ခွဲထားအမှန်", StringComparison.Ordinal);

                if (oldWasEnglish && !isForeignPropn)
                {
                    PromoteEnglishCueToUsageNote(e, oldCue);
                }
                else if (oldWasColloc && !isForeignPropn && (e.UsageNote == null || e.UsageNote.IndexOf("သတ်ပုံအညွှန်း:", StringComparison.Ordinal) < 0))
                {
                    e.UsageNote = string.IsNullOrEmpty(e.UsageNote)
                        ? ("သတ်ပုံအညွှန်း: " + oldCue)
                        : (e.UsageNote + " | သတ်ပုံအညွှန်း: " + oldCue);
                }

                // 1. Try translating English sense via 52,270-entry Wiktionary+UHS English->Myanmar sense table
                string enSource = null;
                if (oldWasEnglish)
                {
                    int pCut = oldCue.IndexOf("• အသံထွက်:", StringComparison.Ordinal);
                    enSource = pCut >= 0 ? oldCue.Substring(0, pCut).Trim() : oldCue;
                }
                if (enSource == null && !string.IsNullOrEmpty(e.UsageNote))
                {
                    int enIdx = e.UsageNote.IndexOf("အင်္ဂလိပ်အနက်:", StringComparison.Ordinal);
                    if (enIdx >= 0)
                    {
                        int endPipe = e.UsageNote.IndexOf('|', enIdx);
                        enSource = (endPipe > enIdx
                            ? e.UsageNote.Substring(enIdx + "အင်္ဂလိပ်အနက်:".Length, endPipe - (enIdx + "အင်္ဂလိပ်အနက်:".Length))
                            : e.UsageNote.Substring(enIdx + "အင်္ဂလိပ်အနက်:".Length)).Trim();
                    }
                }
                string translatedMm = LookupEnglishSenseInMm(enSource ?? "", e.Word);
                if (!string.IsNullOrEmpty(translatedMm) && hasMyanmarConsonantRx.IsMatch(translatedMm))
                {
                    e.MeaningCue = translatedMm;
                    string shortTrans = ShortenStemGloss(translatedMm, e.Word);
                    if (shortTrans.Length >= 2 && !string.Equals(shortTrans, e.Word, StringComparison.Ordinal))
                        realMmStemDefs[e.Word] = shortTrans;
                    mmTranslatedFromEn++;
                    continue;
                }

                var wSylMatches = SyllableCountRegex.Matches(e.Word);

                // 2. Productive Burmese Derivational Morphology (suffixes & 'အ-' / 'မ-' prefixes)
                bool morphDone = false;
                for (int sx = 0; sx < productiveSuffixes.Length; sx++)
                {
                    string suf = productiveSuffixes[sx].Suffix;
                    if (e.Word.Length > suf.Length + 1 && e.Word.EndsWith(suf, StringComparison.Ordinal))
                    {
                        int stemCharLen = e.Word.Length - suf.Length;
                        if (!IsExactSyllableBoundary(wSylMatches, stemCharLen))
                            continue;
                        string stem = e.Word.Substring(0, stemCharLen);
                        string stemGloss;
                        if (realMmStemDefs.TryGetValue(stem, out stemGloss))
                        {
                            e.MeaningCue = string.Format(productiveSuffixes[sx].Template, stem, stemGloss);
                            mmMorphSynthesized++;
                            morphDone = true;
                            break;
                        }
                    }
                }
                if (morphDone) continue;

                if (e.Word.Length >= 3 && e.Word[0] == 'အ' && IsExactSyllableBoundary(wSylMatches, 1))
                {
                    string stem = e.Word.Substring(1);
                    string stemGloss;
                    if (realMmStemDefs.TryGetValue(stem, out stemGloss))
                    {
                        e.MeaningCue = stem + "ခြင်း သို့မဟုတ် ယင်းနှင့် သက်ဆိုင်သော သဘော (" + stemGloss + ")။";
                        mmMorphSynthesized++;
                        continue;
                    }
                }
                if (e.Word.Length >= 4 && e.Word[0] == 'မ' && IsExactSyllableBoundary(wSylMatches, 1))
                {
                    string stem = e.Word.Substring(1);
                    string stemGloss;
                    if (realMmStemDefs.TryGetValue(stem, out stemGloss))
                    {
                        e.MeaningCue = stem + "ခြင်း မဟုတ်သော / ကင်းသော သဘော (" + stemGloss + ")။";
                        mmMorphSynthesized++;
                        continue;
                    }
                }

                // 3. Two-Constituent Burmese Compound Synthesis across syllable boundaries (scored by constituent headhood & frequency)
                if (wSylMatches.Count >= 2 && wSylMatches.Count <= 6)
                {
                    double bestSplitScore = double.NegativeInfinity;
                    string bestCompoundDef = null;
                    for (int split = 1; split < wSylMatches.Count; split++)
                    {
                        int splitCharPos = wSylMatches[split].Index;
                        string leftPart = e.Word.Substring(0, splitCharPos);
                        string rightPart = e.Word.Substring(splitCharPos);
                        if (leftPart.Length < 2 || rightPart.Length < 2)
                            continue;
                        string leftGloss, rightGloss;
                        if (realMmStemDefs.TryGetValue(leftPart, out leftGloss) &&
                            realMmStemDefs.TryGetValue(rightPart, out rightGloss))
                        {
                            int leftSyls = split;
                            int rightSyls = wSylMatches.Count - split;
                            EntryItem lEnt, rEnt;
                            int leftFreq = map.TryGetValue(leftPart, out lEnt) ? Math.Max(1, lEnt.Frequency) : 1;
                            int rightFreq = map.TryGetValue(rightPart, out rEnt) ? Math.Max(1, rEnt.Frequency) : 1;
                            bool leftOff = lEnt != null && lEnt.IsOfficialThatpon;
                            bool rightOff = rEnt != null && rEnt.IsOfficialThatpon;

                            double splitScore = Math.Log10(leftFreq + 1) * 2.2
                                              + Math.Log10(rightFreq + 1) * 2.8
                                              + (rightSyls >= 2 ? 4.5 : 0.0)
                                              + (leftSyls >= 2 ? 2.0 : 0.0)
                                              - Math.Abs(leftSyls - rightSyls) * 0.6
                                              + (leftOff ? 1.0 : 0.0)
                                              + (rightOff ? 1.5 : 0.0);
                            if (splitScore > bestSplitScore)
                            {
                                bestSplitScore = splitScore;
                                bestCompoundDef = leftPart + " နှင့် " + rightPart + " ပေါင်းစပ်ဝေါဟာရ — " +
                                                  leftPart + " (" + leftGloss + ") + " + rightPart + " (" + rightGloss + ")။";
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(bestCompoundDef))
                    {
                        e.MeaningCue = bestCompoundDef;
                        mmCompoundSynthesized++;
                        continue;
                    }
                }

                // 4. Pali Etymology / Official Thatpon Collocation / Standard Burmese Grammatical Definition
                if (!string.IsNullOrEmpty(e.UsageNote) && e.UsageNote.IndexOf("ပါဠိအနက်:", StringComparison.Ordinal) >= 0)
                {
                    int pIdx = e.UsageNote.IndexOf("ပါဠိအနက်:", StringComparison.Ordinal) + "ပါဠိအနက်:".Length;
                    int pEnd = e.UsageNote.IndexOf('|', pIdx);
                    string pVal = (pEnd > pIdx ? e.UsageNote.Substring(pIdx, pEnd - pIdx) : e.UsageNote.Substring(pIdx)).Trim();
                    e.MeaningCue = "ပါဠိရင်းမြစ် မြန်မာဝေါဟာရ (ပါဠိမူရင်း — " + pVal + ")။";
                }
                else if (oldWasColloc)
                {
                    string cleanCol = oldCue
                        .Replace("သတ်ပုံကျမ်း တွဲဖက်အညွှန်း", "")
                        .Replace("တွဲဖက်:", "")
                        .Trim();
                    if (cleanCol.StartsWith("(", StringComparison.Ordinal) && cleanCol.EndsWith(")", StringComparison.Ordinal))
                    {
                        int depthCheck = 0;
                        bool enclosesAll = true;
                        for (int ci = 0; ci < cleanCol.Length - 1; ci++)
                        {
                            if (cleanCol[ci] == '(') depthCheck++;
                            else if (cleanCol[ci] == ')') depthCheck--;
                            if (depthCheck == 0) { enclosesAll = false; break; }
                        }
                        if (enclosesAll)
                            cleanCol = cleanCol.Substring(1, cleanCol.Length - 2).Trim();
                    }
                    e.MeaningCue = string.IsNullOrEmpty(cleanCol)
                        ? "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် ခွဲထားစံအမှန် ဝေါဟာရ။"
                        : ("မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် စံဝေါဟာရ (တွဲဖက်အသုံး — " + cleanCol + ")။");
                }
                else if (e.PosTag == "LOAN")
                {
                    e.MeaningCue = "နိုင်ငံခြားဘာသာမှ မွေးစားသုံးနှုန်းထားသော ခေတ်သုံး မြန်မာဝေါဟာရ။";
                }
                else if (e.IsOfficialThatpon)
                {
                    e.MeaningCue = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် စံသတ်ပုံအမှန် မြန်မာဝေါဟာရ။";
                }
                else if (e.PosTag == "V")
                {
                    e.MeaningCue = "ပြုခြင်း၊ ဖြစ်ခြင်း၊ ရှိခြင်းကို ဖော်ပြသော မြန်မာကြိယာဝေါဟာရ။";
                }
                else if (e.PosTag == "ADJ" || e.PosTag == "ADV")
                {
                    e.MeaningCue = "အရည်အသွေး သို့မဟုတ် အခြင်းအရာကို အထူးပြုဖော်ပြသော မြန်မာဝေါဟာရ။";
                }
                else
                {
                    e.MeaningCue = "ခေတ်သုံး မြန်မာစာပေနှင့် ရုံးသုံး စံသတ်ပုံအမှန် နာမ်ဝေါဟာရ။";
                }
                mmPaliOrOrthSynthesized++;
            }

            // Re-attach preserved Official 462-Page Book Khwe-Htar '(နှိုင်းယှဉ်: ...)' comparison suffixes onto MeaningCue
            for (int i = 0; i < allEntries.Count; i++)
            {
                var e = allEntries[i];
                if (!string.IsNullOrEmpty(e.UsageNote) &&
                    !string.IsNullOrEmpty(e.MeaningCue) &&
                    e.MeaningCue.IndexOf("နှိုင်းယှဉ်:", StringComparison.Ordinal) < 0)
                {
                    int cmpPos = e.UsageNote.IndexOf("နှိုင်းယှဉ်:", StringComparison.Ordinal);
                    if (cmpPos >= 0)
                    {
                        int pipeEnd = e.UsageNote.IndexOf('|', cmpPos);
                        string rawCmp = (pipeEnd > cmpPos
                            ? e.UsageNote.Substring(cmpPos, pipeEnd - cmpPos)
                            : e.UsageNote.Substring(cmpPos)).Trim(' ', ')', '(');
                        if (!string.IsNullOrEmpty(rawCmp))
                        {
                            e.MeaningCue = e.MeaningCue.TrimEnd() + " (" + rawCmp + ")";
                        }
                    }
                }
            }
            Console.WriteLine("  - V16 Myanmar-to-Myanmar Knowledge Synthesis: +" + mmTranslatedFromEn + " EN->MM translated, +" +
                              mmMorphSynthesized + " morphological, +" + mmCompoundSynthesized + " compound, +" +
                              mmPaliOrOrthSynthesized + " Pali/Thatpon formatted (" + dedupedEnglishHomographs + " duplicate EN homographs merged).");

            // Q. Distill Top 38,000 Clean Empirical Myanmar Word Bigrams from 1.2M+ myWord Bigram Rows!
            var bigramMap = new Dictionary<string, BigramItem>(StringComparer.Ordinal);
            string[] bigramFiles = new string[]
            {
                Path.Combine(sourcesDir, "corpora", "myword_bigram_aa.txt"),
                Path.Combine(sourcesDir, "corpora", "myword_bigram_ab.txt"),
                Path.Combine(sourcesDir, "corpora", "myword_bigram_ac.txt")
            };
            for (int bf = 0; bf < bigramFiles.Length; bf++)
            {
                if (!File.Exists(bigramFiles[bf])) continue;
                using (var sr = new StreamReader(bigramFiles[bf], Encoding.UTF8))
                {
                    string bLine;
                    while ((bLine = sr.ReadLine()) != null)
                    {
                        Match mb = BigramLineRegex.Match(bLine);
                        if (!mb.Success) continue;
                        int rawCnt;
                        if (!int.TryParse(mb.Groups[3].Value, out rawCnt) || rawCnt < 12)
                            continue;

                        string w1 = NormalizeMyanmar(mb.Groups[1].Value);
                        string w2 = NormalizeMyanmar(mb.Groups[2].Value);
                        if (string.IsNullOrEmpty(w1) || string.IsNullOrEmpty(w2))
                            continue;
                        if (!PureMyanmarWordRegex.IsMatch(w1) || !PureMyanmarWordRegex.IsMatch(w2))
                            continue;
                        if (KnownWrongBlacklist.Contains(w1) || KnownWrongBlacklist.Contains(w2) ||
                            seenWrong.Contains(w1) || seenWrong.Contains(w2))
                            continue;
                        if (!map.ContainsKey(w1) || !map.ContainsKey(w2))
                            continue;
                        string joinedStdBg = w1 + w2;
                        if (KnownWrongBlacklist.Contains(joinedStdBg) || seenWrong.Contains(joinedStdBg))
                            continue;

                        string bgKey = w1 + "\t" + w2;
                        BigramItem existingBg;
                        if (bigramMap.TryGetValue(bgKey, out existingBg))
                        {
                            if (rawCnt > existingBg.Count)
                                existingBg.Count = rawCnt;
                        }
                        else
                        {
                            bigramMap[bgKey] = new BigramItem { LeftWord = w1, RightWord = w2, Count = rawCnt };
                        }
                    }
                }
            }

            string govNewsBigramPath = Path.Combine(sourcesDir, "corpora", "gov_published_news_bigrams.txt");
            if (File.Exists(govNewsBigramPath))
            {
                using (var srGov = new StreamReader(govNewsBigramPath, Encoding.UTF8))
                {
                    string gLine;
                    while ((gLine = srGov.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(gLine)) continue;
                        string[] gCols = gLine.Split('\t');
                        if (gCols.Length != 3) continue;
                        int govCnt;
                        if (!int.TryParse(gCols[2].Trim(), out govCnt) || govCnt < 2)
                            continue;

                        string w1 = NormalizeMyanmar(gCols[0].Trim());
                        string w2 = NormalizeMyanmar(gCols[1].Trim());
                        if (string.IsNullOrEmpty(w1) || string.IsNullOrEmpty(w2))
                            continue;
                        if (!PureMyanmarWordRegex.IsMatch(w1) || !PureMyanmarWordRegex.IsMatch(w2))
                            continue;
                        if (KnownWrongBlacklist.Contains(w1) || KnownWrongBlacklist.Contains(w2) ||
                            seenWrong.Contains(w1) || seenWrong.Contains(w2))
                            continue;
                        if (!map.ContainsKey(w1) || !map.ContainsKey(w2))
                            continue;
                        string joinedGovBg = w1 + w2;
                        if (KnownWrongBlacklist.Contains(joinedGovBg) || seenWrong.Contains(joinedGovBg))
                            continue;

                        int weightedGovCnt = Math.Max(18, govCnt * 8);
                        string bgKey = w1 + "\t" + w2;
                        BigramItem existingBg;
                        if (bigramMap.TryGetValue(bgKey, out existingBg))
                        {
                            existingBg.Count = Math.Max(existingBg.Count, existingBg.Count + govCnt * 4);
                        }
                        else
                        {
                            bigramMap[bgKey] = new BigramItem { LeftWord = w1, RightWord = w2, Count = weightedGovCnt };
                        }
                    }
                }
            }

            var topBigrams = bigramMap.Values
                .OrderByDescending(b => b.Count)
                .Take(110000)
                .ToList();

            // R. Ingest Sanitized Classical Myanmar Proverbs (စကားပုံ), Sayings (ဆိုရိုးစကား), & Rhetorical Styles (စကားကြီးဆယ်မျိုး)
            int referencePhrasesAdded = 0;
            string proverbsCleanPath = Path.Combine(sourcesDir, "dictionaries", "mlc_proverbs_and_sayings_clean.json");
            if (File.Exists(proverbsCleanPath))
            {
                string provJson = File.ReadAllText(proverbsCleanPath, Encoding.UTF8);
                Regex provItemRx = new Regex(
                    "\"Word\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"\\s*,\\s*\"PosTag\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"\\s*,\\s*\"MeaningCue\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"",
                    RegexOptions.Compiled);
                foreach (Match pm in provItemRx.Matches(provJson))
                {
                    string pw = pm.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\").Trim();
                    string pp = pm.Groups[2].Value.Trim();
                    string pc = pm.Groups[3].Value.Replace("\\\"", "\"").Replace("\\\\", "\\").Trim();
                    if (string.IsNullOrEmpty(pw) || string.IsNullOrEmpty(pp)) continue;
                    allEntries.Add(new EntryItem
                    {
                        Word = NormalizeMyanmar(pw),
                        PosTag = pp,
                        MeaningCue = NormalizeMyanmar(pc),
                        Frequency = 25,
                        IsOfficialThatpon = false
                    });
                    referencePhrasesAdded++;
                }
            }

            string idiomsCleanTsvPath = Path.Combine(sourcesDir, "dictionaries", "mlc_mm_idioms_clean.tsv");
            if (File.Exists(idiomsCleanTsvPath))
            {
                foreach (string rawLine in File.ReadAllLines(idiomsCleanTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] iCols = rawLine.Split('\t');
                    if (iCols.Length < 3) continue;
                    string iw = NormalizeMyanmar(iCols[0].Trim());
                    string ip = iCols[1].Trim();
                    string ic = NormalizeMyanmar(iCols[2].Trim());
                    if (string.IsNullOrEmpty(iw) || string.IsNullOrEmpty(ip)) continue;
                    allEntries.Add(new EntryItem
                    {
                        Word = iw,
                        PosTag = ip,
                        MeaningCue = ic,
                        Frequency = 25,
                        IsOfficialThatpon = false
                    });
                    referencePhrasesAdded++;
                }
            }

            // R2. Ingest V16 Offline Myanmar Encyclopedic & Classical Treatise Knowledge Corpus (25,000 ENCYC Articles)
            int encyclopediaArticlesAdded = 0;
            string encycTsvPath = Path.Combine(sourcesDir, "dictionaries", "mlc_mm_encyclopedia_knowledge.tsv");
            if (File.Exists(encycTsvPath))
            {
                foreach (string rawLine in File.ReadAllLines(encycTsvPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] eCols = rawLine.Split('\t');
                    if (eCols.Length < 3) continue;
                    string ew = NormalizeMyanmar(eCols[0].Trim());
                    string ep = eCols[1].Trim();
                    string ec = NormalizeMyanmar(eCols[2].Trim());
                    string enote = eCols.Length >= 4 ? eCols[3].Trim() : "";
                    if (string.IsNullOrEmpty(ew) || string.IsNullOrEmpty(ec)) continue;
                    allEntries.Add(new EntryItem
                    {
                        Word = ew,
                        PosTag = string.IsNullOrEmpty(ep) ? "ENCYC" : ep,
                        MeaningCue = ec,
                        UsageNote = enote,
                        Frequency = 35,
                        IsOfficialThatpon = false
                    });
                    encyclopediaArticlesAdded++;
                }
            }
            Console.WriteLine("  - V16 Offline Myanmar Encyclopedic Knowledge Corpus: +" + encyclopediaArticlesAdded + " ENCYC articles.");

            // V23 Hybrid Smart Merge:
            // (1) Enrich canonical Myanmar headwords in-place with myG2P sandhi pronunciations & deduplicated English glosses (0 duplicate headwords!)
            // (2) Compile Orthography-Sanitized Lazy Bilingual English-to-Myanmar Reference Lexicon (PosTag = "EN_LEX")
            int g2pNotesEnriched = 0;
            int bilingualNotesEnriched = 0;
            for (int i = 0; i < allEntries.Count; i++)
            {
                var ce = allEntries[i];
                if (ce.PosTag == "ENCYC" || ce.PosTag == "PROVERB" || ce.PosTag == "SAYING" || ce.PosTag == "RHETORIC" || ce.PosTag == "EN_LEX")
                    continue;

                string curUsage = ce.UsageNote ?? string.Empty;
                string g2pVal;
                if (curUsage.IndexOf("အသံထွက်:", StringComparison.Ordinal) < 0 &&
                    myG2pPronMap.TryGetValue(ce.Word, out g2pVal) &&
                    !string.IsNullOrEmpty(g2pVal))
                {
                    curUsage = string.IsNullOrEmpty(curUsage)
                        ? ("အသံထွက်: " + g2pVal)
                        : (curUsage + " | အသံထွက်: " + g2pVal);
                    g2pNotesEnriched++;
                }

                bool foreignPropn = ce.PosTag == "PROPN" &&
                    curUsage.IndexOf("မြန်မာနိုင်ငံ", StringComparison.Ordinal) < 0 &&
                    curUsage.IndexOf("မြို့နယ်", StringComparison.Ordinal) < 0;
                if (!foreignPropn &&
                    curUsage.IndexOf("အင်္ဂလိပ်", StringComparison.Ordinal) < 0 &&
                    mmToEnGlossList.TryGetValue(ce.Word, out var enGlosses) &&
                    enGlosses.Count > 0)
                {
                    string joinedEn = string.Join("; ", enGlosses);
                    curUsage = string.IsNullOrEmpty(curUsage)
                        ? ("အင်္ဂလိပ်အနက်: " + joinedEn)
                        : (curUsage + " | အင်္ဂလိပ်အနက်: " + joinedEn);
                    bilingualNotesEnriched++;
                }

                ce.UsageNote = curUsage;
            }

            // Build Official Thatpon orthography sanitizer lookup for EN_LEX Myanmar definitions
            // (Skip short/cross-morpheme hazard substrings that can collide inside valid words like မိဖုရား, ပြင်သစ်စာ, ဂီတနည်းပြ, ဒိုင်ယာရီ, ရေစီးကမ်းပြို, မေမြင့်မိုရ်, ဗိုလ်တထောင်)
            var crossMorphemeHazardWrongForms = new HashSet<string>(StringComparer.Ordinal)
            {
                NormalizeMyanmar("ဖုရား"), NormalizeMyanmar("သစ်စာ"), NormalizeMyanmar("တနည်း"),
                NormalizeMyanmar("ယာရီ"), NormalizeMyanmar("စီးကမ်း"), NormalizeMyanmar("မြင့်မိုရ်"),
                NormalizeMyanmar("ပိတိ"), NormalizeMyanmar("မေတ္တ"), NormalizeMyanmar("သစ္စ"),
                NormalizeMyanmar("ပတီး"), NormalizeMyanmar("တထောင်"), NormalizeMyanmar("တလုံး"),
                NormalizeMyanmar("တပါး"), NormalizeMyanmar("တချို့")
            };
            var defWrongFixes = new List<(string Wrong, string Right)>(cleanRules.Count + 4);
            defWrongFixes.Add((NormalizeMyanmar("ပုတ်ကိုယ်"), NormalizeMyanmar("ပုဂ္ဂိုလ်")));
            defWrongFixes.Add((NormalizeMyanmar("ရုပ်ထွေး"), NormalizeMyanmar("ရှုပ်ထွေး")));
            for (int ri = 0; ri < cleanRules.Count; ri++)
            {
                string wf = cleanRules[ri].WrongForm;
                string cf = cleanRules[ri].CorrectForm;
                if (!string.IsNullOrEmpty(wf) && !string.IsNullOrEmpty(cf) && wf.Length >= 4 &&
                    !crossMorphemeHazardWrongForms.Contains(wf) &&
                    !string.Equals(wf, cf, StringComparison.Ordinal))
                {
                    defWrongFixes.Add((wf, cf));
                }
            }

            string SanitizeBilingualMmText(string rawMm)
            {
                if (string.IsNullOrWhiteSpace(rawMm))
                    return string.Empty;
                string s = rawMm.Replace("`", "").Replace("´", "").Replace("\r", " ").Replace("\n", " ");
                s = NormalizeMyanmar(s);
                for (int fi = 0; fi < defWrongFixes.Count; fi++)
                {
                    if (s.IndexOf(defWrongFixes[fi].Wrong, StringComparison.Ordinal) >= 0)
                        s = s.Replace(defWrongFixes[fi].Wrong, defWrongFixes[fi].Right);
                }
                s = Regex.Replace(s, @"\s{2,}", " ").Trim();
                return s;
            }

            var allEnKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in enToMmGlossMap.Keys) allEnKeys.Add(k);
            foreach (string k in ornagaiEnBuckets.Keys) allEnKeys.Add(k);

            Regex validEnHeadwordRx = new Regex(@"^[A-Za-z][A-Za-z0-9'\- ]{1,46}$", RegexOptions.Compiled);
            Regex placeholderNoiseRx = new Regex(@"\b(?:sth|sb|ones|busive)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var stopLinkParticles = new HashSet<string>(StringComparer.Ordinal)
            {
                "သည်", "သော", "ခြင်း", "မှု", "ရာ", "၌", "တွင်", "ကို", "နှင့်", "၏", "မှ", "သို့", "ဖြင့်", "အား", "ဟု", "၍", "လည်း", "ပါ", "သည့်", "ဖြစ်", "ရှိ", "ရန်", "သူ", "အရာ"
            };

            int enLexAdded = 0;
            foreach (string rawEnKey in allEnKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string enKey = Regex.Replace(rawEnKey.Trim(), @"\s{2,}", " ");
                if (!validEnHeadwordRx.IsMatch(enKey) || placeholderNoiseRx.IsMatch(enKey) || VulgarOrnagaiEnglishRegex.IsMatch(enKey))
                    continue;

                // Keep up to 4-word English idioms/phrasal verbs ("break the ice", "a change of heart"), skip long sentence fragments
                int spaceCnt = 0;
                for (int ci = 0; ci < enKey.Length; ci++)
                    if (enKey[ci] == ' ') spaceCnt++;
                if (spaceCnt > 3)
                    continue;

                var senses = new List<string>(4);
                void AddSenseUnique(string candidateSense)
                {
                    if (string.IsNullOrWhiteSpace(candidateSense) || senses.Count >= 3)
                        return;
                    string cs = candidateSense.Trim().TrimEnd('•', ';', ',');
                    if (cs.Length < 1 || !hasMyanmarConsonantRx.IsMatch(cs))
                        return;
                    for (int si = 0; si < senses.Count; si++)
                    {
                        if (string.Equals(senses[si], cs, StringComparison.Ordinal) ||
                            senses[si].IndexOf(cs, StringComparison.Ordinal) >= 0)
                            return;
                    }
                    senses.Add(cs);
                }

                if (ornagaiEnBuckets.TryGetValue(rawEnKey, out var ornList))
                {
                    for (int oi = 0; oi < ornList.Count && senses.Count < 3; oi++)
                    {
                        string cleanOrn = SanitizeBilingualMmText(ornList[oi].MmGloss);
                        if (!string.IsNullOrEmpty(cleanOrn))
                        {
                            string rawPos = ornList[oi].Pos ?? string.Empty;
                            int openParenInPos = rawPos.IndexOf('(');
                            if (openParenInPos >= 0 && rawPos.IndexOf(')', openParenInPos) < 0)
                                rawPos = rawPos.Substring(0, openParenInPos).Trim(' ', ',', ';');
                            string posPrefix = !string.IsNullOrEmpty(rawPos) ? ("[" + rawPos + "] ") : "";
                            AddSenseUnique(posPrefix + cleanOrn);
                        }
                    }
                }

                string wikGloss;
                if (enToMmGlossMap.TryGetValue(rawEnKey, out wikGloss) && !string.IsNullOrEmpty(wikGloss))
                {
                    string[] wikParts = wikGloss.Split(new[] { " • " }, StringSplitOptions.RemoveEmptyEntries);
                    for (int wi = 0; wi < wikParts.Length && senses.Count < 3; wi++)
                    {
                        string cleanWik = SanitizeBilingualMmText(wikParts[wi]);
                        if (cleanWik.Length > 105)
                            cleanWik = BalanceShortenedParentheses(cleanWik.Substring(0, 102).TrimEnd() + "…");
                        AddSenseUnique(cleanWik);
                    }
                }

                if (senses.Count == 0)
                    continue;

                string finalCue = string.Join(" • ", senses);
                if (finalCue.Length > 165)
                    finalCue = BalanceShortenedParentheses(finalCue.Substring(0, 162).TrimEnd() + "…");

                // Extract up to 3 verified canonical Myanmar headwords for 1-click jump pills
                var crossLinks = new List<string>(3);
                string[] linkTokens = finalCue.Split(new char[] { '၊', '။', ',', ';', '•', ' ', '(', ')', '[', ']', '—', '/' }, StringSplitOptions.RemoveEmptyEntries);
                for (int li = 0; li < linkTokens.Length && crossLinks.Count < 3; li++)
                {
                    string tok = linkTokens[li].Trim();
                    if (tok.Length >= 2 &&
                        !stopLinkParticles.Contains(tok) &&
                        map.ContainsKey(tok) &&
                        !crossLinks.Contains(tok))
                    {
                        crossLinks.Add(tok);
                    }
                }

                string linkNote = crossLinks.Count > 0
                    ? ("ကြောင်းတူသံကွဲ: " + string.Join("၊ ", crossLinks))
                    : string.Empty;

                allEntries.Add(new EntryItem
                {
                    Word = enKey.ToLowerInvariant(),
                    PosTag = "EN_LEX",
                    MeaningCue = finalCue,
                    UsageNote = linkNote,
                    Frequency = ornagaiEnBuckets.ContainsKey(rawEnKey) ? 30 : 18,
                    IsOfficialThatpon = false
                });
                enLexAdded++;
            }
            Console.WriteLine(string.Format("  - V23 Bilingual & Phonetic Upgrade: +{0} G2P pronunciations | +{1} MM->EN headword glosses | +{2} EN_LEX English->Myanmar entries!",
                g2pNotesEnriched, bilingualNotesEnriched, enLexAdded));

            // Final enterprise hygiene + V32 Whole-Corpus 360° Data Validation & Sanitization Pass across all MeaningCue, UsageNote, and Rule Explanations
            Regex parenSensitiveEnRx = new Regex(@"\([^)]*\b(arse|arsehole|asshole|bitch|fuck|shit|bastard|whore|slut|cunt|mistress|snob)\b[^)]*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Regex wikiSectionHeaderRx = new Regex(@"={2,}\s*([^=]+?)\s*={2,}", RegexOptions.Compiled);
            Regex degreeHalfMojibakeRx = new Regex(@"([၀-၉0-9]+)\s*½", RegexOptions.Compiled);
            Regex ethnicCitationRx = new Regex(@"[\u105A-\u109F\uAA60-\uAA7F]|(?:မွန်|ရှမ်း|ကရင်|ပအိုဝ်း|တအာင်း|ကယား|ပလောင်)(?:\s*[-–—၊:]|ဘာသာ|စကား|ဝေါဟာရ|အခေါ်)", RegexOptions.Compiled);
            Regex symbolPuaRx = new Regex(@"[\uE000-\uF8FF]+", RegexOptions.Compiled);
            Regex wikiInterwikiRx = new Regex(@"\[\[:?[a-z]{2,3}:|(?<=:\s*):[a-z]{2,3}:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Regex wikiNoteCaptionRx = new Regex(@"(ဗဟုသုတ:\s*)[^|\[]*?\]\]\s*", RegexOptions.Compiled);

            KeyValuePair<string, string>[] mojibakeAndEtymologyFixes = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>("လိပ်၊ ခကဘန", "အင်္ဂလိပ်၊ cube"),
                new KeyValuePair<string, string>("လိပ်၊ ခသကစသည", "အင်္ဂလိပ်၊ coupon"),
                new KeyValuePair<string, string>("လိပ်၊ စမေူငေါနညအ", "အင်္ဂလိပ်၊ parliament"),
                new KeyValuePair<string, string>("လိပ်၊ စမေူငောနညအ", "အင်္ဂလိပ်၊ parliament"),
                new KeyValuePair<string, string>("လိပ်၊ တငမန", "အင်္ဂလိပ်၊ wire"),
                new KeyValuePair<string, string>("လိပ်၊ နိငအသမ", "အင်္ဂလိပ်၊ editor"),
                new KeyValuePair<string, string>("လိပ်၊ ဗစမငူ", "အင်္ဂလိပ်၊ April"),
                new KeyValuePair<string, string>("လိပ်၊ မောသည", "အင်္ဂလိပ်၊ argon"),
                new KeyValuePair<string, string>("လိပ်၊ မေါသည", "အင်္ဂလိပ်၊ argon"),
                new KeyValuePair<string, string>("လိပ်၊ argon", "အင်္ဂလိပ်၊ argon"),
                new KeyValuePair<string, string>("လိပ်၊ parliament", "အင်္ဂလိပ်၊ parliament"),
                new KeyValuePair<string, string>("လိပ်၊ cube", "အင်္ဂလိပ်၊ cube"),
                new KeyValuePair<string, string>("လိပ်၊ coupon", "အင်္ဂလိပ်၊ coupon"),
                new KeyValuePair<string, string>("လိပ်၊ wire", "အင်္ဂလိပ်၊ wire"),
                new KeyValuePair<string, string>("လိပ်၊ editor", "အင်္ဂလိပ်၊ editor"),
                new KeyValuePair<string, string>("လိပ်၊ April", "အင်္ဂလိပ်၊ April"),
                new KeyValuePair<string, string>("လိပ်၊ voucher", "အင်္ဂလိပ်၊ voucher"),
                new KeyValuePair<string, string>("လိပ်၊ guard", "အင်္ဂလိပ်၊ guard"),
                new KeyValuePair<string, string>("လိပ်၊ second", "အင်္ဂလိပ်၊ second"),
                new KeyValuePair<string, string>("လိပ်၊ studio", "အင်္ဂလိပ်၊ studio"),
                new KeyValuePair<string, string>("လိပ်၊ stone", "အင်္ဂလိပ်၊ stone"),
                new KeyValuePair<string, string>("လိပ်၊ A.D. (Anno Domini)", "အင်္ဂလိပ်၊ A.D. (Anno Domini)"),
                new KeyValuePair<string, string>("Pathol, s----------", "ရောဂါဗေဒအသုံး"),
                new KeyValuePair<string, string>("လိပ်၊ လသကခ့နမ", "အင်္ဂလိပ်၊ voucher"),
                new KeyValuePair<string, string>("လိပ်၊ ါကမေိ", "အင်္ဂလိပ်၊ guard"),
                new KeyValuePair<string, string>("လိပ်၊ျနခသညိ", "အင်္ဂလိပ်၊ second"),
                new KeyValuePair<string, string>("လိပ်၊ျအကိငသ", "အင်္ဂလိပ်၊ studio"),
                new KeyValuePair<string, string>("လိပ်၊ျအသညန", "အင်္ဂလိပ်၊ stone"),
                new KeyValuePair<string, string>("\u100B\u103F\u100C", "\u100B\u1039\u100C"),
                new KeyValuePair<string, string>("ပြဵ", "ပြ"),
                new KeyValuePair<string, string>("မဵနိုင်", "မ~နိုင်"),
                new KeyValuePair<string, string>("ဥပေဒပြန်ဋ္ဌာန်း", "ဥပဒေပြဋ္ဌာန်း"),
                new KeyValuePair<string, string>("ဥပဒေပြန်ဋ္ဌာန်း", "ဥပဒေပြဋ္ဌာန်း"),
                new KeyValuePair<string, string>("ဥပေဒ", "ဥပဒေ"),
                new KeyValuePair<string, string>("ရတ္ဂ်ဘဘောလုံး", "ရက်ဂ်ဘီဘောလုံး"),
                new KeyValuePair<string, string>("ရတ်္ဂဘီ", "ရက်ဂ်ဘီ"),
                new KeyValuePair<string, string>("ရတ္ဂ်ဘီ", "ရက်ဂ်ဘီ"),
                new KeyValuePair<string, string>("ဂျတ္ဇ်", "ဂျက်ဇ်"),
                new KeyValuePair<string, string>("ဖရွိုက္ဒ်", "ဖရွိုက်ဒ်"),
                new KeyValuePair<string, string>("အမိုးအုဋ္ဌ်ကြွပ်", "အမိုးအုတ်ကြွပ်"),
                new KeyValuePair<string, string>("ရေရွတ်ဆရေးသော", "ရေရွတ်ဆဲရေးသော"),
                new KeyValuePair<string, string>("ဆွဆောင်သည်", "ဆွဲဆောင်သည်"),
                new KeyValuePair<string, string>("အက်စစ်ဓာတ် ကသောမိုး", "အက်စစ်ဓာတ်ကဲသောမိုး"),
                new KeyValuePair<string, string>("ကြောက်ရွှံ့", "ကြောက်ရွံ့"),
                new KeyValuePair<string, string>("ကြောက်ရွှံ", "ကြောက်ရွံ့"),
                new KeyValuePair<string, string>("သွေးထိုးလှုံဆော်", "သွေးထိုးလှုံ့ဆော်"),
                new KeyValuePair<string, string>("လမ္မော်ရောင်", "လိမ္မော်ရောင်"),
                new KeyValuePair<string, string>("သသည္", "သည်"),
                new KeyValuePair<string, string>("မြန်မာသက္က်ရာဇ်", "မြန်မာသက္ကရာဇ်"),
                new KeyValuePair<string, string>("စတုရဂင်္", "စတုရင်္ဂ"),
                new KeyValuePair<string, string>("မူဠ္", "မူဠှ"),
                new KeyValuePair<string, string>("အာမေဍ္ဍိတ်", "အာမေဍိတ်"),
                new KeyValuePair<string, string>("အီသီယီုးပီးယား", "အီသီယိုးပီးယား"),
                new KeyValuePair<string, string>("အသုံးြုပမူ", "အသုံးပြုမှု"),
                new KeyValuePair<string, string>("အသုံးြုပ", "အသုံးပြု"),
                new KeyValuePair<string, string>("တရားမျှတမူ", "တရားမျှတမှု"),
                new KeyValuePair<string, string>("မှန်ကန်မူ", "မှန်ကန်မှု"),
                new KeyValuePair<string, string>("စွပ်ြုတ်", "စွပ်ပြုတ်"),
                new KeyValuePair<string, string>("ယှဉ်ြိုင်", "ယှဉ်ပြိုင်"),
                new KeyValuePair<string, string>("တောင်းနိုင်ြောင်း", "တောင်းနိုင်ကြောင်း"),
                new KeyValuePair<string, string>("မြင်းအိြုမင်းနာ", "မြင်းအိုမြင်းနာ"),
                new KeyValuePair<string, string>("ယခြုမစ်ကြီးနား", "ယခုမြစ်ကြီးနား"),
                new KeyValuePair<string, string>("ရွှေလြီမစ်ဝှမ်း", "ရွှေလီမြစ်ဝှမ်း"),
                new KeyValuePair<string, string>("မျောက်အတ္တလန္တိတ်စင်ရောျကြီး", "မြောက်အတ္တလန္တိတ်စင်ရော်ကြီး"),
                new KeyValuePair<string, string>("မျောက်အတ္တလန္တိတ်", "မြောက်အတ္တလန္တိတ်"),
                new KeyValuePair<string, string>("\u1031\u102C\u103B", "\u1031\u102C\u103A"),
                new KeyValuePair<string, string>("လူနည်းုအုပ်ချုပ်မှု", "လူနည်းစုအုပ်ချုပ်မှု"),
                new KeyValuePair<string, string>("အာလူးံမကာ", "အာလူးဖုံးကာ"),
                new KeyValuePair<string, string>("လူမှုရေးောင်ရွက်သူ", "လူမှုရေးဆောင်ရွက်သူ"),
                new KeyValuePair<string, string>("တာပြတ် ျေးရွာ", "တာပြတ်ကျေးရွာ"),
                new KeyValuePair<string, string>("တာပြတ်ျေးရွာ", "တာပြတ်ကျေးရွာ"),
                new KeyValuePair<string, string>("ဧရာဝတီတိုင်း သေကြီး", "ဧရာဝတီတိုင်းဒေသကြီး"),
                new KeyValuePair<string, string>("ကျိုတ်လတ်မြို့နယ်", "ကျိုက်လတ်မြို့နယ်"),
                new KeyValuePair<string, string>("မြို့နယ် ျောင်းစု ျေးရွာ", "မြို့နယ် ကျောင်းစုကျေးရွာ"),
                new KeyValuePair<string, string>("မြို့နယ်ျောင်းစုျေးရွာ", "မြို့နယ် ကျောင်းစုကျေးရွာ"),
                new KeyValuePair<string, string>("လဇ\u103A", "လစ်ဇ်"),
                new KeyValuePair<string, string>("ဥက္ကဋ္ဌ်", "ဥက္ကဋ္ဌ"),
                new KeyValuePair<string, string>("ဥက်ကဋ်ဌျ", "ဥက္ကဋ္ဌ"),
                new KeyValuePair<string, string>("ဆဲလ်စီယပ်။ ½။", "ဆဲလ်စီယပ် (°C)။"),
                new KeyValuePair<string, string>("link=link=link=", ""),
                new KeyValuePair<string, string>("link=", "")
            };

            KeyValuePair<string, string>[] proseOrthFixes = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>("၄င်း", "၎င်း"),
                new KeyValuePair<string, string>("ဆေးမှု့န့်", "ဆေးမှုန့်"),
                new KeyValuePair<string, string>("တနသာၤရီ", "တနင်္သာရီ"),
                new KeyValuePair<string, string>("ခြသေ့ၤ", "ခြင်္သေ့"),
                new KeyValuePair<string, string>("အကျီၤ", "အင်္ကျီ"),
                new KeyValuePair<string, string>("အကုၤရ", "အင်္ကုရ"),
                new KeyValuePair<string, string>("အကျိဳး", "အကျိုး"),
                new KeyValuePair<string, string>("ခေတၱ", "ခေတ္တ"),
                new KeyValuePair<string, string>("ၾွမှူဆွယ်", "မြှူဆွယ်"),
                new KeyValuePair<string, string>("ြှမှူဆွယ်", "မြှူဆွယ်"),
                new KeyValuePair<string, string>("ၾိကိုတင်", "ကြိုတင်"),
                new KeyValuePair<string, string>("ြိကိုတင်", "ကြိုတင်"),
                new KeyValuePair<string, string>("ေ႔ရှ၌", "ရှေ့၌"),
                new KeyValuePair<string, string>("ေ့ရှ၌", "ရှေ့၌"),
                new KeyValuePair<string, string>("ႈဦးချို", "ဦးချို"),
                new KeyValuePair<string, string>("ှုဦးချို", "ဦးချို"),
                new KeyValuePair<string, string>("ပတ်သတ်", "ပတ်သက်"),
                new KeyValuePair<string, string>("ပက်သက်", "ပတ်သက်"),
                new KeyValuePair<string, string>("အချိန်မှီ", "အချိန်မီ"),
                new KeyValuePair<string, string>("ခေတ်မှီ", "ခေတ်မီ"),
                new KeyValuePair<string, string>("ကွန်ပြူတာ", "ကွန်ပျူတာ"),
                new KeyValuePair<string, string>("ကွန်ယက်", "ကွန်ရက်"),
                new KeyValuePair<string, string>("စံနစ်", "စနစ်"),
                new KeyValuePair<string, string>("ယေဘူယျ", "ယေဘုယျ"),
                new KeyValuePair<string, string>("ယျေဘုယျ", "ယေဘုယျ"),
                new KeyValuePair<string, string>("ပရိတ်သတ်", "ပရိသတ်"),
                new KeyValuePair<string, string>("သာမာန်", "သာမန်"),
                new KeyValuePair<string, string>("အနာဂါတ်", "အနာဂတ်"),
                new KeyValuePair<string, string>("သင်္ဂြိုလ်", "သင်္ဂြိုဟ်"),
                new KeyValuePair<string, string>("ဂြိုလ်", "ဂြိုဟ်"),
                new KeyValuePair<string, string>("ဒဿမ", "ဒသမ"),
                new KeyValuePair<string, string>("အစီစဉ်", "အစီအစဉ်"),
                new KeyValuePair<string, string>("အခြေနေ", "အခြေအနေ"),
                new KeyValuePair<string, string>("ဂရုဏာ", "ကရုဏာ"),
                new KeyValuePair<string, string>("ဆဌမ", "ဆဋ္ဌမ"),
                new KeyValuePair<string, string>("အဌမ", "အဋ္ဌမ"),
                new KeyValuePair<string, string>("ပြဌာန်း", "ပြဋ္ဌာန်း"),
                new KeyValuePair<string, string>("ဥက္ကဌ", "ဥက္ကဋ္ဌ"),
                new KeyValuePair<string, string>("ဒိဌိ", "ဒိဋ္ဌိ"),
                new KeyValuePair<string, string>("သဏ္ဍာန်", "သဏ္ဌာန်"),
                new KeyValuePair<string, string>("သဏ္ဍန်", "သဏ္ဌာန်"),
                new KeyValuePair<string, string>("တောင်ကိုးရီးယား", "တောင်ကိုရီးယား"),
                new KeyValuePair<string, string>("ကိုရီယား", "ကိုရီးယား"),
                new KeyValuePair<string, string>("သတ္ထု", "သတ္တု"),
                new KeyValuePair<string, string>("အရာဝတ္တု", "အရာဝတ္ထု"),
                new KeyValuePair<string, string>("အဆောက်အဦး", "အဆောက်အဦ"),
                new KeyValuePair<string, string>("တစိတ်တပိုင်း", "တစ်စိတ်တစ်ပိုင်း"),
                new KeyValuePair<string, string>("တနည်း", "တစ်နည်း"),
                new KeyValuePair<string, string>("တစ်ဘက်", "တစ်ဖက်"),
                new KeyValuePair<string, string>("ဝိညာဏ်", "ဝိညာဉ်"),
                new KeyValuePair<string, string>("ဩစတေးလျ", "ဩစတြေးလျ"),
                new KeyValuePair<string, string>("ဩစတီးယား", "ဩစတြီးယား"),
                new KeyValuePair<string, string>("သမာရိုးကျ", "သမားရိုးကျ"),
                new KeyValuePair<string, string>("နယ်နမိတ်", "နယ်နိမိတ်"),
                new KeyValuePair<string, string>("ဂျာမဏီ", "ဂျာမနီ"),
                new KeyValuePair<string, string>("ဘေဂျင်းမြို့", "ပေကျင်းမြို့"),
                new KeyValuePair<string, string>("ဘေကျင်းမြို့", "ပေကျင်းမြို့"),
                new KeyValuePair<string, string>("မက္ကစီကို", "မက္ကဆီကို"),
                new KeyValuePair<string, string>("ဒီပဲရင်း", "ဒီပဲယင်း"),
                new KeyValuePair<string, string>("ဒီဇိုင်းနာ", "ဒီဇိုင်နာ"),
                new KeyValuePair<string, string>("နျူးကလီးယား", "နျူကလီးယား"),
                new KeyValuePair<string, string>("နိုင်ဝင်ဘာ", "နိုဝင်ဘာ"),
                new KeyValuePair<string, string>("နိဿယည်း", "နိဿရည်း"),
                new KeyValuePair<string, string>("ခရစ်ယန်", "ခရစ်ယာန်"),
                new KeyValuePair<string, string>("ပိုင်သျှင်", "ပိုင်ရှင်"),
                new KeyValuePair<string, string>("ပညာသျှင်", "ပညာရှင်"),
                new KeyValuePair<string, string>("အချုပ်အချာ", "အချုပ်အခြာ"),
                new KeyValuePair<string, string>("အသိအမတ်", "အသိအမှတ်"),
                new KeyValuePair<string, string>("မြူနီစပယ်", "မြူနီစီပယ်"),
                new KeyValuePair<string, string>("မြူနီဆီပယ်", "မြူနီစီပယ်"),
                new KeyValuePair<string, string>("မျိုးစိပ်", "မျိုးစိတ်"),
                new KeyValuePair<string, string>("ရင်းနှီးမြုပ်နှံ", "ရင်းနှီးမြှုပ်နှံ"),
                new KeyValuePair<string, string>("မြင့်မိုရ်", "မြင်းမိုရ်"),
                new KeyValuePair<string, string>("သဘာဝအလျှောက်", "သဘာဝအလျောက်"),
                new KeyValuePair<string, string>("အလိုအလျှောက်", "အလိုအလျောက်"),
                new KeyValuePair<string, string>("လေဟာပြင်", "လဟာပြင်"),
                new KeyValuePair<string, string>("ပတ်ဝန်ကျင်", "ပတ်ဝန်းကျင်"),
                new KeyValuePair<string, string>("ကုတင်", "ခုတင်")
            };

            Regex safeTaNiProseRx = new Regex(@"(?<!ဂီ)တနည်း", RegexOptions.Compiled);
            Regex safeMyintMoProseRx = new Regex(@"(?<!မေ)မြင\u103A\u1037မိုရ်", RegexOptions.Compiled);
            Regex classifierEtcNoiseRx = new Regex(@"\.*\s*\betc\b\.?|\.{2,}\s*စသည်", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            string ApplySafeProseOrthReplacementsOnSegment(string segment)
            {
                string updated = segment;
                for (int fi = 0; fi < proseOrthFixes.Length; fi++)
                {
                    string bad = proseOrthFixes[fi].Key;
                    if (updated.IndexOf(bad, StringComparison.Ordinal) < 0)
                        continue;
                    if (bad == "တနည်း")
                        updated = safeTaNiProseRx.Replace(updated, "တစ်နည်း");
                    else if (bad == "မြင့်မိုရ်" || bad == "မြင\u103A\u1037မိုရ်")
                        updated = safeMyintMoProseRx.Replace(updated, "မြင်းမိုရ်");
                    else
                        updated = updated.Replace(bad, proseOrthFixes[fi].Value);
                }
                if (updated.IndexOf("မြင\u103A\u1037မိုရ်", StringComparison.Ordinal) >= 0)
                    updated = safeMyintMoProseRx.Replace(updated, "မြင်းမိုရ်");
                return updated;
            }

            string ApplySafeProseOrthFixes(string text, bool isUsageNote)
            {
                if (string.IsNullOrEmpty(text))
                    return text;

                if (isUsageNote)
                {
                    string[] pipes = text.Split(new[] { " | " }, StringSplitOptions.None);
                    bool changed = false;
                    for (int pi = 0; pi < pipes.Length; pi++)
                    {
                        string seg = pipes[pi].TrimStart();
                        if (seg.StartsWith("အသံထွက်:", StringComparison.Ordinal) ||
                            seg.StartsWith("နှိုင်းယှဉ်:", StringComparison.Ordinal) ||
                            seg.StartsWith("ပါဠိအနက်:", StringComparison.Ordinal) ||
                            seg.StartsWith("ရင်းမြစ်:", StringComparison.Ordinal) ||
                            seg.StartsWith("သာဓက:", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        string updated = ApplySafeProseOrthReplacementsOnSegment(pipes[pi]);
                        if (!string.Equals(updated, pipes[pi], StringComparison.Ordinal))
                        {
                            pipes[pi] = updated;
                            changed = true;
                        }
                    }
                    return changed ? string.Join(" | ", pipes) : text;
                }
                else
                {
                    int cmpIdx = text.IndexOf("(နှိုင်းယှဉ်:", StringComparison.Ordinal);
                    string mainPart = cmpIdx > 0 ? text.Substring(0, cmpIdx) : text;
                    string tailPart = cmpIdx > 0 ? text.Substring(cmpIdx) : string.Empty;
                    mainPart = ApplySafeProseOrthReplacementsOnSegment(mainPart);
                    return cmpIdx > 0 ? (mainPart + tailPart) : mainPart;
                }
            }

            string DeduplicateUsagePronunciation(string usageNote)
            {
                if (string.IsNullOrEmpty(usageNote))
                    return usageNote;
                if (usageNote.IndexOf("အသံထွက်:", StringComparison.Ordinal) < 0 &&
                    usageNote.IndexOf("မျိုးပြပစ္စည်း:", StringComparison.Ordinal) < 0 &&
                    usageNote.IndexOf("အင်္ဂလိပ်အနက်:", StringComparison.Ordinal) < 0)
                {
                    return usageNote;
                }

                string[] pipes = usageNote.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
                bool seenPron = false;
                var kept = new List<string>(pipes.Length);
                for (int pi = 0; pi < pipes.Length; pi++)
                {
                    string seg = pipes[pi].Trim();
                    int innerBulletPron = seg.IndexOf("• အသံထွက်:", StringComparison.Ordinal);
                    if (innerBulletPron >= 0)
                    {
                        string extraPron = seg.Substring(innerBulletPron + 1).Trim();
                        seg = seg.Substring(0, innerBulletPron).Trim();
                        if (!seenPron && extraPron.Length > "အသံထွက်:".Length)
                        {
                            kept.Add(extraPron);
                            seenPron = true;
                        }
                    }
                    if (seg.StartsWith("အသံထွက်:", StringComparison.Ordinal))
                    {
                        if (seenPron) continue;
                        seenPron = true;
                    }
                    else if (seg.StartsWith("မျိုးပြပစ္စည်း:", StringComparison.Ordinal))
                    {
                        string rawClf = seg.Substring("မျိုးပြပစ္စည်း:".Length).Trim();
                        rawClf = classifierEtcNoiseRx.Replace(rawClf, "");
                        rawClf = Regex.Replace(rawClf, @"\.{2,}", "၊");
                        string[] cParts = rawClf.Split(new char[] { '၊', ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries);
                        var uniqClf = new List<string>(cParts.Length);
                        for (int ci = 0; ci < cParts.Length; ci++)
                        {
                            string cp = cParts[ci].Trim(' ', '.', '။');
                            if (cp.Length > 0 && hasMyanmarConsonantRx.IsMatch(cp) && !uniqClf.Contains(cp))
                                uniqClf.Add(cp);
                        }
                        if (uniqClf.Count == 0)
                            continue;
                        seg = "မျိုးပြပစ္စည်း: " + string.Join("၊ ", uniqClf);
                    }
                    else if (seg.StartsWith("အင်္ဂလိပ်အနက်:", StringComparison.Ordinal))
                    {
                        string val = seg.Substring("အင်္ဂလိပ်အနက်:".Length).Trim();
                        if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                            val = val.Substring(1, val.Length - 2).Trim();
                        seg = "အင်္ဂလိပ်အနက်: " + val;
                    }
                    if (seg.Length > 0)
                        kept.Add(seg);
                }
                return string.Join(" | ", kept);
            }

            string SanitizeProseField(string rawText, bool isUsageNote = false)
            {
                if (string.IsNullOrEmpty(rawText))
                    return string.Empty;

                string s = ZeroWidthRegex.Replace(rawText, "");

                // Clean CSV doubled quotes, Symbol-font PUA, MediaWiki caption/link remnants, degree mojibake, and WinInnwa/OCR glitches
                if (s.IndexOf('\uF064') >= 0)
                    s = s.Replace('\uF064', 'δ');
                if (symbolPuaRx.IsMatch(s))
                    s = symbolPuaRx.Replace(s, " ");
                if (s.IndexOf("\"\"", StringComparison.Ordinal) >= 0)
                {
                    s = s.Replace("\"\"\"", "\"").Replace("\"\"", "\"");
                }
                if (s.IndexOf("==", StringComparison.Ordinal) >= 0)
                {
                    s = wikiSectionHeaderRx.Replace(s, "$1 — ");
                    s = Regex.Replace(s, @"={2,}", " ");
                }
                if (s.IndexOf("---", StringComparison.Ordinal) >= 0)
                {
                    s = Regex.Replace(s, @"-{3,}", " ");
                }
                if (s.IndexOf("[[", StringComparison.Ordinal) >= 0 || s.IndexOf(":en:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    s = wikiInterwikiRx.Replace(s, "");
                }
                if (s.IndexOf("]]", StringComparison.Ordinal) >= 0)
                {
                    if (isUsageNote && s.IndexOf("ဗဟုသုတ:", StringComparison.Ordinal) >= 0)
                        s = wikiNoteCaptionRx.Replace(s, "$1");
                    int closeBr = s.IndexOf("]]", StringComparison.Ordinal);
                    int openBr = s.IndexOf("[[", StringComparison.Ordinal);
                    if (closeBr >= 0 && (openBr < 0 || closeBr < openBr) && closeBr < s.Length - 8)
                        s = s.Substring(closeBr + 2).TrimStart();
                    s = s.Replace("]]", "");
                }
                if (s.IndexOf("[[", StringComparison.Ordinal) >= 0)
                {
                    s = s.Replace("[[", "");
                }
                for (int mi = 0; mi < mojibakeAndEtymologyFixes.Length; mi++)
                {
                    string bad = mojibakeAndEtymologyFixes[mi].Key;
                    if (s.IndexOf(bad, StringComparison.Ordinal) >= 0)
                        s = s.Replace(bad, mojibakeAndEtymologyFixes[mi].Value);
                }
                if (s.IndexOf('½') >= 0)
                {
                    s = degreeHalfMojibakeRx.Replace(s, "$1°");
                }

                bool hasEthnicCitation = ethnicCitationRx.IsMatch(s);

                if (!hasEthnicCitation && (s.IndexOf(' ') >= 0 || s.IndexOf('\u00A0') >= 0))
                {
                    s = ProseSpaceBeforeCombiningRegex.Replace(s, "");
                }

                // Repair WinInnwa shifted-E / visual-order Ra mojibake FIRST so FindZawgyiSpans never misclassifies shifted Unicode medials
                s = RepairShiftedEAndMedialMojibake(s);
                s = RepairVisualOrderERa(s);

                if (!hasEthnicCitation)
                {
                    if (s.IndexOf('\u1033') >= 0) s = s.Replace('\u1033', '\u102F');
                    if (s.IndexOf('\u1034') >= 0) s = s.Replace('\u1034', '\u1030');
                    if (s.IndexOf('\u1071') >= 0) s = s.Replace("\u1071", "\u1039\u1010");
                    if (s.IndexOf('\u1094') >= 0) s = s.Replace('\u1094', '\u1037');
                    if (s.IndexOf('\u1095') >= 0) s = s.Replace('\u1095', '\u1037');

                    var zgSpans = MyanmarSpellChecker.Core.Nlp.UnicodeNormalizer.FindZawgyiSpans(s);
                    if (zgSpans.Count > 0)
                    {
                        for (int zi = zgSpans.Count - 1; zi >= 0; zi--)
                        {
                            var sp = zgSpans[zi];
                            s = s.Substring(0, sp.StartIndex) + sp.ConvertedUnicode + s.Substring(sp.StartIndex + sp.Length);
                        }
                        s = RepairShiftedEAndMedialMojibake(s);
                        s = RepairVisualOrderERa(s);
                    }
                }

                if (s.IndexOf('\u1025') >= 0)
                {
                    s = s.Replace("\u1025\u102E", "\u1026")
                         .Replace("\u1025\u103A", "\u1009\u103A")
                         .Replace("\u1025\u102C", "\u1009\u102C")
                         .Replace("\u1025\u1039", "\u1009\u1039")
                         .Replace("\u1025\u103E", "\u1009\u103E")
                         .Replace("\u1025\u1037\u103A", "\u1009\u103A\u1037");
                }
                if (s.IndexOf('\u1005') >= 0 || s.IndexOf('\u1008') >= 0)
                {
                    s = Regex.Replace(s, @"(?<!\u1039)\u1005\u103B|\u1008\u103B", "\u1008");
                }
                if (s.IndexOf('\u1039') >= 0)
                {
                    s = VowelBeforeStackRegex.Replace(s, "${Stack}${Vowel}");
                }
                if (s.IndexOf('\u1031') >= 0)
                {
                    s = MedialAfterERegex.Replace(s, "$1\u1031");
                }
                if (s.IndexOf('\u103D') >= 0 || s.IndexOf('\u103E') >= 0)
                {
                    s = MedialAfterVowelCompilerRegex.Replace(s, "${Medial}${Vowel}");
                }
                if (s.IndexOf('\u101D') >= 0)
                {
                    s = ProseMixedDigitWaRegex.Replace(s, m => new string('\u1040', m.Value.Length));
                }

                // Re-normalize Tall/Round AA on non-ethnic prose after shifted-E & visual-Ra repairs
                if (!hasEthnicCitation)
                {
                    if (s.IndexOf('\u102B') >= 0)
                    {
                        s = Regex.Replace(s, @"(?<=(?:[\u1000\u1003\u1005-\u1011\u1014\u1016-\u101C\u101E-\u1021]|(?<!\u1012\u1039)\u1013|(?!\u1012\u103D|\u101D\u103E)[\u1000-\u1021][\u103B-\u103E]+)\u1031?)\u102B", "\u102C");
                    }
                    if (s.IndexOf('\u102C') >= 0)
                    {
                        s = Regex.Replace(s, @"(?<=(?:(?<!\u1039)|\u1004\u103A\u1039)(?:[\u1001\u1002\u1004\u1012\u1015\u101D]|\u1012\u103D)\u1031?)\u102C(?!တမ)", "\u102B");
                    }
                }

                // Universal Unicode 5.1+ UTN#11 combining mark order (valid for Burmese AND Mon/Shan/Karen citations!)
                s = s.Replace("\u103E\u103D", "\u103D\u103E")
                     .Replace("\u103D\u103B", "\u103B\u103D")
                     .Replace("\u103D\u103C", "\u103C\u103D")
                     .Replace("\u103E\u103B", "\u103B\u103E")
                     .Replace("\u103E\u103C", "\u103C\u103E")
                     .Replace("\u102F\u102D", "\u102D\u102F")
                     .Replace("\u1030\u102D", "\u102D\u1030")
                     .Replace("\u102F\u102E", "\u102E\u102F")
                     .Replace("\u102D\u102E", "\u102E")
                     .Replace("\u102E\u102D", "\u102E")
                     .Replace("\u102F\u1030", "\u1030")
                     .Replace("\u1030\u102F", "\u1030")
                     .Replace("\u1036\u102D", "\u102D\u1036")
                     .Replace("\u1036\u102E", "\u102E\u1036")
                     .Replace("\u1036\u102F", "\u102F\u1036")
                     .Replace("\u1036\u1030", "\u1030\u1036")
                     .Replace("\u1037\u103A", "\u103A\u1037")
                     .Replace("\u1038\u103A", "\u103A\u1038")
                     .Replace("\u1037\u102C", "\u102C\u1037")
                     .Replace("\u1037\u102B", "\u102B\u1037")
                     .Replace("\u1037\u1036", "\u1036\u1037")
                     .Replace("\u1037\u102F", "\u102F\u1037")
                     .Replace("\u102C\u1038\u102C", "\u102C\u1038")
                     .Replace("\u102B\u1038\u102B", "\u102B\u1038")
                     .Replace("\u103A\u1037\u103A", "\u103A\u1037")
                     .Replace("\u1037\u103A\u1037", "\u103A\u1037");
                if (!hasEthnicCitation && s.IndexOf('\u1038') >= 0)
                {
                    s = Regex.Replace(s, @"\u1038[\u102B-\u103E]+", "\u1038");
                }
                s = Regex.Replace(s, @"([\u102B-\u1032\u1036-\u103E])\1+", "$1");

                if (s.IndexOf("လစ်ဇ်", StringComparison.Ordinal) >= 0 || s.IndexOf("လဇ်", StringComparison.Ordinal) >= 0)
                    s = Regex.Replace(s, @"ဖရန[\u1037\u103A\u103B]+လ(?:စ်)?ဇ်", "ဖရန\u103A\u1037လစ်ဇ်");
                if (s.IndexOf("ဥက္ကဋ္ဌ", StringComparison.Ordinal) >= 0 && s.IndexOf("မင", StringComparison.Ordinal) >= 0)
                    s = Regex.Replace(s, @"မင[\u1037\u1038\u103B]+မင[\u1037\u1038\u103B]+ဥက္ကဋ္ဌ", "မင်းမင\u103A\u1037ဥက္ကဋ္ဌ");
                if (s.IndexOf('\u103B') >= 0 && s.IndexOf("\u1031\u102C\u103B", StringComparison.Ordinal) >= 0)
                    s = s.Replace("\u1031\u102C\u103B", "\u1031\u102C\u103A");

                s = ApplySafeProseOrthFixes(s, isUsageNote);

                // Clean double dandas & duplicate punctuation
                if (s.IndexOf("။)။", StringComparison.Ordinal) >= 0)
                    s = s.Replace("။)။", ")။");
                if (s.IndexOf('။') >= 0)
                    s = Regex.Replace(s, @"။(?:\s*။)+", "။");
                if (s.IndexOf("၊၊", StringComparison.Ordinal) >= 0)
                    s = Regex.Replace(s, @"၊{2,}", "၊");
                s = Regex.Replace(s, @"\s{2,}", " ");

                return s.Trim();
            }

            string EnsureBalancedParentheses(string text)
            {
                if (string.IsNullOrEmpty(text) || (text.IndexOf('(') < 0 && text.IndexOf(')') < 0))
                    return text;

                string s = text.Trim();
                // Strip orphan ')' when depth would drop below 0 (e.g. ') ===...===' or 'ထိုင်ရန်) တင်ပျဉ်ခွေ')
                if (s.IndexOf(')') >= 0)
                {
                    var sbPar = new StringBuilder(s.Length);
                    int depth = 0;
                    for (int ci = 0; ci < s.Length; ci++)
                    {
                        char ch = s[ci];
                        if (ch == '(')
                        {
                            depth++;
                            sbPar.Append(ch);
                        }
                        else if (ch == ')')
                        {
                            if (depth > 0)
                            {
                                depth--;
                                sbPar.Append(ch);
                            }
                            else if (ci > 0 && ci + 1 < s.Length && s[ci - 1] >= '0' && s[ci - 1] <= '9')
                            {
                                // Convert list marker '1)' to '1.' instead of leaving an unbalanced ')'
                                sbPar.Append('.');
                            }
                        }
                        else
                        {
                            sbPar.Append(ch);
                        }
                    }
                    s = Regex.Replace(sbPar.ToString(), @"\s{2,}", " ").Trim();
                }

                while (s.EndsWith("(", StringComparison.Ordinal))
                    s = s.Substring(0, s.Length - 1).TrimEnd(' ', '—', '-', '၊', ',', ';');

                int openCount = 0;
                int closeCount = 0;
                for (int ci = 0; ci < s.Length; ci++)
                {
                    if (s[ci] == '(') openCount++;
                    else if (s[ci] == ')') closeCount++;
                }

                int deficit = openCount - closeCount;
                if (deficit <= 0)
                    return s;

                string closeStr = new string(')', deficit);
                if (s.EndsWith("။", StringComparison.Ordinal))
                    return s.Substring(0, s.Length - 1).TrimEnd() + closeStr + "။";
                return s + closeStr;
            }

            string NormalizeRuleExplanation(string rawExpl, string wrongForm)
            {
                if (string.IsNullOrEmpty(rawExpl))
                    return string.Empty;
                string s = ZeroWidthRegex.Replace(rawExpl, "").Trim();
                const string wfToken = "\uE000";
                bool masked = !string.IsNullOrEmpty(wrongForm) && s.IndexOf(wrongForm, StringComparison.Ordinal) >= 0;
                if (masked)
                    s = s.Replace(wrongForm, wfToken);
                s = s.Replace("\u1037\u103A", "\u103A\u1037")
                     .Replace("\u1037\u102C", "\u102C\u1037")
                     .Replace("\u1037\u102B", "\u102B\u1037")
                     .Replace("\u1037\u1036", "\u1036\u1037")
                     .Replace("\u1037\u102F", "\u102F\u1037")
                     .Replace("\u1036\u102F", "\u102F\u1036")
                     .Replace("\u102F\u102D", "\u102D\u102F");
                if (masked)
                    s = s.Replace(wfToken, wrongForm);
                return s;
            }

            string NormalizeTriggerList(string rawTriggers)
            {
                if (string.IsNullOrEmpty(rawTriggers))
                    return rawTriggers;
                string[] parts = rawTriggers.Split('|');
                for (int pi = 0; pi < parts.Length; pi++)
                {
                    if (!string.IsNullOrWhiteSpace(parts[pi]))
                        parts[pi] = NormalizeMyanmar(parts[pi].Trim());
                }
                return string.Join("|", parts);
            }

            for (int i = 0; i < allEntries.Count; i++)
            {
                string mc = allEntries[i].MeaningCue;
                if (allEntries[i].PosTag == "ENCYC")
                {
                    if (allEntries[i].Word == "အီရန်နိုင်ငံရှိ နိုင်ငံရေးပါတီများ")
                        mc = "အီရန်နိုင်ငံရှိ ထင်ရှားသော နိုင်ငံရေးပါတီများနှ\u1004\u103A\u1037 အတိုက်အခံအဖွဲ့အစည်းများ စာရင်း။";
                    else if (allEntries[i].Word == "အီရန်နိုင်ငံရှိ ရွေးကောက်ပွဲများ")
                        mc = "အီရန်နိုင်ငံတွင် နိုင်ငံတော်သမ္မတ၊ လွှတ်တော် (Majlis) နှ\u1004\u103A\u1037 ကျွမ်းကျင်သူများကောင်စီ ရွေးကောက်တင်မြှောက်သည\u103A\u1037 စနစ်။";
                }
                if (!string.IsNullOrEmpty(mc) && VulgarOrnagaiEnglishRegex.IsMatch(mc))
                {
                    mc = parenSensitiveEnRx.Replace(mc, "");
                    mc = VulgarOrnagaiEnglishRegex.Replace(mc, "");
                    mc = Regex.Replace(mc, @"\(\s*\)", "");
                    mc = Regex.Replace(mc, @"\s{2,}", " ").Trim();
                }
                mc = SanitizeProseField(mc, isUsageNote: false);
                if (allEntries[i].PosTag != "EN_LEX" && IsSelfReferentialGloss(allEntries[i].Word, mc))
                {
                    if (allEntries[i].Word == "ကိုယ်ပိုင်အုပ်ချုပ်ခွင့်ရဒေသ" || allEntries[i].Word == "ကိုယ်ပိုင်အုပ်ချုပ်ခွင\u103A\u1037ရဒေသ")
                    {
                        mc = "ဖွဲ့စည်းပုံအခြေခံဥပဒေအရ သတ်မှတ်ထားသော ကိုယ်ပိုင်အုပ်ချုပ်ခွင\u103A\u1037ရ နယ်မြေဒေသ။";
                    }
                    else if (allEntries[i].PosTag == "ENCYC" && allEntries[i].Word.IndexOf("ရွာ", StringComparison.Ordinal) >= 0)
                    {
                        mc = "မြန်မာနိုင်ငံရှိ ကျေးရွာ/ဒေသအမည်။";
                    }
                    else if (allEntries[i].IsOfficialThatpon)
                    {
                        mc = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် စံသတ်ပုံအမှန် မြန်မာဝေါဟာရ။";
                    }
                    else
                    {
                        mc = "ခေတ်သုံး မြန်မာစာပေနှ\u1004\u103A\u1037 ရုံးသုံး စံသတ်ပုံအမှန် ဝေါဟာရ။";
                    }
                    mc = SanitizeProseField(mc, isUsageNote: false);
                }
                allEntries[i].MeaningCue = EnsureBalancedParentheses(mc);
                if (!string.IsNullOrEmpty(allEntries[i].UsageNote))
                {
                    string un = DeduplicateUsagePronunciation(allEntries[i].UsageNote);
                    un = SanitizeProseField(un, isUsageNote: true);
                    allEntries[i].UsageNote = DeduplicateUsagePronunciation(un);
                }
            }
            for (int i = 0; i < cleanRules.Count; i++)
            {
                if (!string.IsNullOrEmpty(cleanRules[i].Explanation))
                    cleanRules[i].Explanation = NormalizeRuleExplanation(cleanRules[i].Explanation, cleanRules[i].WrongForm);
            }
            for (int i = 0; i < cleanCtxRules.Count; i++)
            {
                if (!string.IsNullOrEmpty(cleanCtxRules[i].Explanation))
                    cleanCtxRules[i].Explanation = NormalizeRuleExplanation(cleanCtxRules[i].Explanation, cleanCtxRules[i].WrongForm);
                if (!string.IsNullOrEmpty(cleanCtxRules[i].TriggerWords))
                    cleanCtxRules[i].TriggerWords = NormalizeTriggerList(cleanCtxRules[i].TriggerWords);
                if (!string.IsNullOrEmpty(cleanCtxRules[i].AntiTriggerWords))
                    cleanCtxRules[i].AntiTriggerWords = NormalizeTriggerList(cleanCtxRules[i].AntiTriggerWords);
            }

            allEntries = allEntries.OrderBy(e => e.Word, StringComparer.Ordinal).ToList();
            int officialCount = allEntries.Where(e => e.IsOfficialThatpon).Select(e => e.Word).Distinct(StringComparer.Ordinal).Count();
            int uniqueWords = allEntries.Select(e => e.Word).Distinct(StringComparer.Ordinal).Count();

            // Write compact UTF-8 JSON directly via buffered StreamWriter (0 MB LOH allocation & GitHub < 100 MB compliant)
            using (var sw = new StreamWriter(outputJsonPath, append: false, new UTF8Encoding(false), bufferSize: 65536))
            {
                sw.WriteLine("{");
                sw.WriteLine("  \"Standard\": \"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (462-Page Complete Book) + မြန်မာအဘိဓာန် + ဦးဟုတ်စိန် + MOFA/GAD Gazetteer + V11 5-Domain Suite + V16 Encyclopedia + V23 Bilingual EN-MM & myG2P Suite\",");
                sw.WriteLine("  \"Version\": \"2026.23\",");
                sw.WriteLine("  \"TotalEntries\": " + allEntries.Count + ",");
                sw.WriteLine("  \"UniqueHeadwords\": " + uniqueWords + ",");
                sw.WriteLine("  \"OfficialThatponUniqueWords\": " + officialCount + ",");
                sw.WriteLine("  \"TotalOrthographyRules\": " + cleanRules.Count + ",");
                sw.WriteLine("  \"TotalContextRules\": " + cleanCtxRules.Count + ",");
                sw.WriteLine("  \"TotalWordBigrams\": " + topBigrams.Count + ",");
                sw.WriteLine("  \"Entries\": [");
                for (int i = 0; i < allEntries.Count; i++)
                {
                    var e = allEntries[i];
                    string comma = (i == allEntries.Count - 1) ? "" : ",";
                    sw.Write("{\"Word\":\"");
                    sw.Write(EscapeJson(e.Word));
                    sw.Write("\",\"MeaningCue\":\"");
                    sw.Write(EscapeJson(e.MeaningCue));
                    sw.Write("\",\"PosTag\":\"");
                    sw.Write(EscapeJson(e.PosTag));
                    if (!string.IsNullOrEmpty(e.UsageNote))
                    {
                        sw.Write("\",\"UsageNote\":\"");
                        sw.Write(EscapeJson(e.UsageNote));
                    }
                    sw.Write('\"');
                    if (e.Frequency != 0)
                    {
                        sw.Write(",\"Frequency\":");
                        sw.Write(e.Frequency);
                    }
                    if (e.IsOfficialThatpon)
                    {
                        sw.Write(",\"IsOfficialThatpon\":true");
                    }
                    sw.Write('}');
                    sw.WriteLine(comma);
                }
                sw.WriteLine("  ],");
                sw.WriteLine("  \"OrthographyRules\": [");
                for (int i = 0; i < cleanRules.Count; i++)
                {
                    var r = cleanRules[i];
                    string comma = (i == cleanRules.Count - 1) ? "" : ",";
                    sw.Write("{\"WrongForm\":\"");
                    sw.Write(EscapeJson(r.WrongForm));
                    sw.Write("\",\"CorrectForm\":\"");
                    sw.Write(EscapeJson(r.CorrectForm));
                    sw.Write("\",\"Category\":\"");
                    sw.Write(EscapeJson(r.Category));
                    sw.Write("\",\"Explanation\":\"");
                    sw.Write(EscapeJson(r.Explanation));
                    sw.Write("\"}");
                    sw.WriteLine(comma);
                }
                sw.WriteLine("  ],");
                sw.WriteLine("  \"ContextRules\": [");
                for (int i = 0; i < cleanCtxRules.Count; i++)
                {
                    var cr = cleanCtxRules[i];
                    string comma = (i == cleanCtxRules.Count - 1) ? "" : ",";
                    sw.Write("{\"WrongForm\":\"");
                    sw.Write(EscapeJson(cr.WrongForm));
                    sw.Write("\",\"CorrectForm\":\"");
                    sw.Write(EscapeJson(cr.CorrectForm));
                    sw.Write("\",\"TriggerWords\":\"");
                    sw.Write(EscapeJson(cr.TriggerWords));
                    if (!string.IsNullOrEmpty(cr.AntiTriggerWords))
                    {
                        sw.Write("\",\"AntiTriggerWords\":\"");
                        sw.Write(EscapeJson(cr.AntiTriggerWords));
                    }
                    sw.Write("\",\"Direction\":\"");
                    sw.Write(EscapeJson(cr.Direction));
                    sw.Write("\",\"Window\":");
                    sw.Write(cr.Window);
                    if (cr.IsWarningOnly)
                    {
                        sw.Write(",\"IsWarningOnly\":true");
                    }
                    if (cr.SkipParticles)
                    {
                        sw.Write(",\"SkipParticles\":true");
                    }
                    sw.Write(",\"Explanation\":\"");
                    sw.Write(EscapeJson(cr.Explanation));
                    sw.Write("\"}");
                    sw.WriteLine(comma);
                }
                sw.WriteLine("  ],");
                sw.WriteLine("  \"WordBigrams\": [");
                for (int i = 0; i < topBigrams.Count; i++)
                {
                    var bg = topBigrams[i];
                    string comma = (i == topBigrams.Count - 1) ? "" : ",";
                    sw.Write("{\"LeftWord\":\"");
                    sw.Write(EscapeJson(bg.LeftWord));
                    sw.Write("\",\"RightWord\":\"");
                    sw.Write(EscapeJson(bg.RightWord));
                    sw.Write("\",\"Count\":");
                    sw.Write(bg.Count);
                    sw.Write('}');
                    sw.WriteLine(comma);
                }
                sw.WriteLine("  ]");
                sw.WriteLine("}");
            }
            Console.WriteLine(string.Format("[+] Book Part 1 lines: {0} | Collocations: {1} | Paren variants: {2} | Classifier stems: {3}",
                bookLinesParsed, bookCollocationsExtracted, bookParenVariantsExtracted, bookStemsExtracted));
            Console.WriteLine(string.Format("[+] Book Part 2 (ခွဲထား) NEW Official Words: {0} | Homophone Cues Enriched: {1} | Arrow Rules: {2}",
                khweHtarNewAdded, khweHtarCueEnriched, khweHtarRules.Length));
            Console.WriteLine(string.Format("[+] myG2P added: {0} | mydict added: {1} | Ornagai enriched: {2} | myPOS v3 POS enriched: {3} | Curated modern: {4}",
                myG2pAdded, mydictAdded, ornagaiEnriched, myPosEnriched, curatedModernAdded));
            Console.WriteLine(string.Format("[+] မြန်မာအဘိဓာန် (Myanmar-to-Myanmar): {0} definitions enriched | {1} POS upgraded | {2} UsageNotes added | {3} NEW headwords!",
                abidanDefinitionsEnriched, abidanPosEnriched, abidanUsageNotesAdded, abidanNewWordsAdded));
            Console.WriteLine(string.Format("[+] U Hoke Sein + 7-Gate Massive Lexicon: {0} Pali cognates enriched | {1} NEW verified headwords!",
                uhsPaliEnriched, massiveLexiconAdded));
            Console.WriteLine(string.Format("[+] Master Corpus V9 Built: {0} entries ({1} unique words, {2} Official Thatpon, {3} Proverbs/Sayings), {4} rules, {5} context rules, {6} bigrams!",
                allEntries.Count, uniqueWords, officialCount, referencePhrasesAdded, cleanRules.Count, cleanCtxRules.Count, topBigrams.Count));
        }

        private static string ComputeExtendedPuritySkeleton(string s)
        {
            // True Burmese Homophone Purity Skeleton:
            // Preserves aspiration ('ှ') and consonant stacking (\u1039) so genuine distinct words
            // (လေယာဉ် vs လှေယာဉ်, အမြင် vs အမျှင်, သတိ vs သတ္တိ, အက္ခရာ vs အခရာ) are NEVER falsely blocked!
            if (string.IsNullOrEmpty(s)) return "";
            string x = s.Replace("\u1004\u103A\u1039", "\u1004\u103A");
            x = Regex.Replace(x, @"[\u100B-\u100E]", "\u100B");
            x = Regex.Replace(x, @"\u102D\u102F[\u101C\u101F\u101B\u1003\u1020]\u103A(?![\u1037\u1038])", "O");
            x = x.Replace("\u102D\u102F", "O").Replace("\u1031\u102B", "A").Replace("\u1031\u102C", "A");
            x = x.Replace("\u102D\u1004\u103A", "\u102D\u1036");
            x = x.Replace("\u100A\u103A", "\u102E").Replace("\u1009\u103A", "\u102E").Replace("\u101A\u103A", "\u102E");
            x = x.Replace("\u1014\u103A", "\u1036").Replace("\u1019\u103A", "\u1036").Replace("\u100F\u103A", "\u1036");
            x = x.Replace("\u1000\u103A", "Q").Replace("\u1010\u103A", "Q").Replace("\u1015\u103A", "Q")
                 .Replace("\u1002\u103A", "Q").Replace("\u1012\u103A", "Q").Replace("\u1017\u103A", "Q")
                 .Replace("\u1018\u103A", "Q").Replace("\u101E\u103A", "Q");
            x = x.Replace("\u101B", "\u101A").Replace("\u103C", "\u103B").Replace("\u102B", "\u102C");
            return x;
        }

        private static string ComputeShadowAuditBucket(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\u1037' || c == '\u1038' || c == '\u103E' || c == '\u103B' || c == '\u103C' || c == '\u1039')
                    continue;
                if ((c >= '\u102B' && c <= '\u1032') || c == '\u103A')
                    continue;
                char m = c;
                if (c == '\u1001' || c == '\u1002' || c == '\u1003') m = '\u1000';
                else if (c == '\u1006' || c == '\u1007' || c == '\u1008') m = '\u1005';
                else if (c >= '\u100B' && c <= '\u1013') m = '\u1010';
                else if (c == '\u100F' || c == '\u1019' || c == '\u1036' || c == '\u1004') m = '\u1014';
                else if (c == '\u1009' || c == '\u100A' || c == '\u101B') m = '\u101A';
                else if (c == '\u1016' || c == '\u1017' || c == '\u1018') m = '\u1015';
                else if (c == '\u1020') m = '\u101C';
                sb.Append(m);
            }
            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class MinistryRulesJsonDoc
        {
            [System.Runtime.Serialization.DataMember(Name = "MinistryEntries")]
            public List<MinistryEntryDto> MinistryEntries { get; set; }

            [System.Runtime.Serialization.DataMember(Name = "OrthographyRules")]
            public List<MinistryRuleDto> OrthographyRules { get; set; }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class MinistryEntryDto
        {
            [System.Runtime.Serialization.DataMember(Name = "Word")]
            public string Word { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "MeaningCue")]
            public string MeaningCue { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "PosTag")]
            public string PosTag { get; set; }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class MinistryRuleDto
        {
            [System.Runtime.Serialization.DataMember(Name = "WrongForm")]
            public string WrongForm { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "CorrectForm")]
            public string CorrectForm { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "Category")]
            public string Category { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "Explanation")]
            public string Explanation { get; set; }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class ThatponBook462JsonDoc
        {
            [System.Runtime.Serialization.DataMember(Name = "part1_lines")]
            public List<string> Part1Lines { get; set; }

            [System.Runtime.Serialization.DataMember(Name = "preface_ta_tasit")]
            public PrefaceTaTasitDto PrefaceTaTasit { get; set; }

            [System.Runtime.Serialization.DataMember(Name = "khwe_htar_entries")]
            public List<KhweHtarEntryDto> KhweHtarEntries { get; set; }

            [System.Runtime.Serialization.DataMember(Name = "arrow_word_pairs")]
            public List<ArrowWordPairDto> ArrowWordPairs { get; set; }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class PrefaceTaTasitDto
        {
            [System.Runtime.Serialization.DataMember(Name = "tasit_examples")]
            public List<string> TasitExamples { get; set; }

            [System.Runtime.Serialization.DataMember(Name = "ta_examples")]
            public List<string> TaExamples { get; set; }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class KhweHtarEntryDto
        {
            [System.Runtime.Serialization.DataMember(Name = "word")]
            public string Word { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "cue")]
            public string Cue { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "pos")]
            public string Pos { get; set; }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class ArrowWordPairDto
        {
            [System.Runtime.Serialization.DataMember(Name = "wrong")]
            public string Wrong { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "correct")]
            public string Correct { get; set; }
            [System.Runtime.Serialization.DataMember(Name = "explanation")]
            public string Explanation { get; set; }
        }

        private static T DeserializeJsonFile<T>(string path)
        {
            var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
            using (var fs = File.OpenRead(path))
            {
                return (T)ser.ReadObject(fs);
            }
        }

        private static string[][] LoadFourColumnTsv(string tsvPath)
        {
            if (!File.Exists(tsvPath)) return new string[0][];
            var rows = new List<string[]>();
            foreach (string line in File.ReadAllLines(tsvPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] cols = line.Split('\t');
                if (cols.Length >= 4)
                {
                    rows.Add(new string[] { cols[0], cols[1], cols[2], cols[3] });
                }
            }
            return rows.ToArray();
        }

        private static RuleItem[] LoadRuleTsv(string tsvPath)
        {
            if (!File.Exists(tsvPath)) return new RuleItem[0];
            var list = new List<RuleItem>();
            foreach (string line in File.ReadAllLines(tsvPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] cols = line.Split('\t');
                if (cols.Length >= 4)
                {
                    list.Add(new RuleItem
                    {
                        WrongForm = cols[0],
                        CorrectForm = cols[1],
                        Category = cols[2],
                        Explanation = cols[3]
                    });
                }
            }
            return list.ToArray();
        }

        public static void BuildFromDataDirectory(string dataDir, string outputJsonPath = null)
        {
            string fullDataDir = Path.GetFullPath(dataDir);
            string sourcesDir = Path.Combine(fullDataDir, "sources");
            string officialDir = Path.Combine(sourcesDir, "official");
            string dictDir = Path.Combine(sourcesDir, "dictionaries");
            string corporaDir = Path.Combine(sourcesDir, "corpora");
            string posDir = Path.Combine(corporaDir, "pos");
            string rulesDir = Path.Combine(sourcesDir, "rules");

            string outPath = string.IsNullOrEmpty(outputJsonPath)
                ? Path.Combine(fullDataDir, "mlc_thatpon_master.json")
                : Path.GetFullPath(outputJsonPath);

            string ministryJsonPath = Path.Combine(fullDataDir, "ministry_orthography_rules.json");
            var mEntries = new List<EntryItem>();
            var mRules = new List<RuleItem>();
            if (File.Exists(ministryJsonPath))
            {
                var mDoc = DeserializeJsonFile<MinistryRulesJsonDoc>(ministryJsonPath);
                if (mDoc != null && mDoc.MinistryEntries != null)
                {
                    foreach (var e in mDoc.MinistryEntries)
                    {
                        mEntries.Add(new EntryItem
                        {
                            Word = e.Word ?? "",
                            MeaningCue = e.MeaningCue ?? "",
                            PosTag = e.PosTag ?? "",
                            IsOfficialThatpon = true
                        });
                    }
                }
                if (mDoc != null && mDoc.OrthographyRules != null)
                {
                    foreach (var r in mDoc.OrthographyRules)
                    {
                        mRules.Add(new RuleItem
                        {
                            WrongForm = r.WrongForm ?? "",
                            CorrectForm = r.CorrectForm ?? "",
                            Category = r.Category ?? "Spelling",
                            Explanation = r.Explanation ?? ""
                        });
                    }
                }
            }

            var part1PdfLines = new List<string>();
            var khEntries = new List<EntryItem>();
            var khRules = new List<RuleItem>();

            string book462Path = Path.Combine(officialDir, "thatpon_462p_structured.json");
            if (File.Exists(book462Path))
            {
                Console.WriteLine("    Loading 100% Decoded 462-Page Official Book JSON (" + book462Path + ")...");
                var book462 = DeserializeJsonFile<ThatponBook462JsonDoc>(book462Path);
                if (book462 != null)
                {
                    if (book462.Part1Lines != null)
                    {
                        part1PdfLines.AddRange(book462.Part1Lines);
                    }
                    if (book462.PrefaceTaTasit != null)
                    {
                        if (book462.PrefaceTaTasit.TasitExamples != null)
                        {
                            foreach (string tw in book462.PrefaceTaTasit.TasitExamples)
                            {
                                khEntries.Add(new EntryItem
                                {
                                    Word = tw ?? "",
                                    MeaningCue = "သတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈): သင်္ချာဂဏန်း (၁) အရင်းခံ 'တစ်' အရေး",
                                    PosTag = "NUM",
                                    IsOfficialThatpon = true
                                });
                            }
                        }
                        if (book462.PrefaceTaTasit.TaExamples != null)
                        {
                            foreach (string tw in book462.PrefaceTaTasit.TaExamples)
                            {
                                khEntries.Add(new EntryItem
                                {
                                    Word = tw ?? "",
                                    MeaningCue = "သတ်ပုံကျမ်း အမှာစာ (စာ-၈): အာဂုံပစ္စည်း 'တ' (စမသတ်) အရေး",
                                    PosTag = "ADV",
                                    IsOfficialThatpon = true
                                });
                            }
                        }
                    }
                    if (book462.KhweHtarEntries != null)
                    {
                        foreach (var ke in book462.KhweHtarEntries)
                        {
                            khEntries.Add(new EntryItem
                            {
                                Word = ke.Word ?? "",
                                MeaningCue = ke.Cue ?? "",
                                PosTag = ke.Pos ?? "",
                                IsOfficialThatpon = true
                            });
                        }
                    }

                    var loanAmbiguousSingles = new HashSet<string>(StringComparer.Ordinal) { "ဂါး", "ဂျိုး", "ဖာသာ" };
                    if (book462.ArrowWordPairs != null)
                    {
                        foreach (var ar in book462.ArrowWordPairs)
                        {
                            string wf = ar.Wrong ?? "";
                            if (loanAmbiguousSingles.Contains(wf)) continue;
                            khRules.Add(new RuleItem
                            {
                                WrongForm = wf,
                                CorrectForm = ar.Correct ?? "",
                                Category = "Spelling",
                                Explanation = ar.Explanation ?? ""
                            });
                        }
                    }

                    string safeBookPairsPath = Path.Combine(rulesDir, "mlc_thatpon_safe_book_pairs.tsv");
                    foreach (var sp in LoadRuleTsv(safeBookPairsPath))
                    {
                        khRules.Add(sp);
                    }

                    string extraStdHwPath = Path.Combine(officialDir, "mlc_extra_standard_headwords.tsv");
                    if (File.Exists(extraStdHwPath))
                    {
                        foreach (string line in File.ReadAllLines(extraStdHwPath, Encoding.UTF8))
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                                continue;
                            string[] cols = line.Split('\t');
                            if (cols.Length >= 3)
                            {
                                khEntries.Add(new EntryItem
                                {
                                    Word = cols[0],
                                    PosTag = cols[1],
                                    MeaningCue = cols[2],
                                    IsOfficialThatpon = true
                                });
                            }
                        }
                    }
                }
            }

            var posDict = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "N", Path.Combine(posDir, "myanmar_words_nouns.json") },
                { "V", Path.Combine(posDir, "myanmar_words_verbs.json") },
                { "ADV", Path.Combine(posDir, "myanmar_words_adverbs.json") },
                { "ADJ", Path.Combine(posDir, "myanmar_words_adjectives.json") },
                { "PRON", Path.Combine(posDir, "myanmar_words_pronouns.json") },
                { "POST", Path.Combine(posDir, "myanmar_words_postpositions.json") },
                { "CONJ", Path.Combine(posDir, "myanmar_words_conjunctions.json") },
                { "PART", Path.Combine(posDir, "myanmar_words_particles.json") }
            };

            BuildJson(
                sourcesDir,
                Path.Combine(officialDir, "kanaung_thatpon_raw.txt"),
                Path.Combine(officialDir, "kanaung_wordlists.list"),
                Path.Combine(corporaDir, "mm_word_freq.txt"),
                Path.Combine(corporaDir, "myword_unigram.txt"),
                Path.Combine(corporaDir, "myword_unigram_phrase.txt"),
                Path.Combine(dictDir, "curated_lexicon.csv"),
                posDict,
                Path.Combine(officialDir, "myanmar_nrc_townships.json"),
                mEntries.ToArray(),
                mRules.ToArray(),
                part1PdfLines.ToArray(),
                khEntries.ToArray(),
                khRules.ToArray(),
                outPath);
        }
    }
}
