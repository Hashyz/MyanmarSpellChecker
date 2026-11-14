using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Performance;
using MyanmarSpellChecker.Core.Security;
using MyanmarSpellChecker.Core.Spreadsheet;
using MyanmarSpellChecker.Core.Tests.Infrastructure;
using MyanmarSpellChecker.Core.Typography;
using Xunit;

namespace MyanmarSpellChecker.Core.Tests
{
    public class GovernmentAndDomainCorpusTests
    {
        [Fact]
        public void V16_UserScreenshots_PronunciationVsOrthography_PatSint_AndLiteraryForms_ZeroWrongness()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. All Error Lines from User Screenshots 1 & 2 + Systemic Phonetic/Pat-Sint/Voicing Families
            var v16ErrorCases = new (string Input, string ExactSpan, string Top1Fix)[]
            {
                // Screenshot 1 exact error lines
                ("သမ္မဒ", "သမ္မဒ", "သမ္မတ"),
                ("ကုတင်", "ကုတင်", "ခုတင်"),
                // Screenshot 2 exact error lines
                ("ငုတ်မိတဲတိုင်", "ငုတ်မိတဲတိုင်", "ငုပ်မိသဲတိုင်"),
                ("မီးသည်း", "မီးသည်း", "မီးသီး"),
                ("ဒွာဒဒသမ", "ဒွာဒဒသမ", "ဒွါဒသမ"),
                ("ဒဿမ", "ဒဿမ", "ဒသမ"),
                ("လေးခြိုး", "လေးခြိုး", "လေးချိုး"),
                ("ပါရာစီဒမော့", "ပါရာစီဒမော့", "ပါရာစီတမော"),
                ("ပဒီး", "ပဒီး", "ပုတီး"),
                ("ကျေးစီ", "ကျေးစီ", "ကြေးစည်"),
                ("ကျေးစည်", "ကျေးစည်", "ကြေးစည်"),
                ("ကြေးသွမ်း", "ကြေးသွမ်း", "ကြေးသွန်း"),
                // Systemic family expansions across both screenshots
                ("နိုင်ငံတော်သမ္မဒ", "နိုင်ငံတော်သမ္မဒ", "နိုင်ငံတော်သမ္မတ"),
                ("သမဒ", "သမဒ", "သမ္မတ"),
                ("သမတ", "သမတ", "သမ္မတ"),
                ("ဥက္ကဌ", "ဥက္ကဌ", "ဥက္ကဋ္ဌ"),
                ("ဥက္ကဌ္ဌ", "ဥက္ကဌ္ဌ", "ဥက္ကဋ္ဌ"),
                ("ဥက္ကဋ္ဋ", "ဥက္ကဋ္ဋ", "ဥက္ကဋ္ဌ"),
                ("ဥက္ကဋ", "ဥက္ကဋ", "ဥက္ကဋ္ဌ"),
                ("ဉက္ကဋ္ဌ", "ဉက္ကဋ္ဌ", "ဥက္ကဋ္ဌ"),
                ("ဉက္ကဌ", "ဉက္ကဌ", "ဥက္ကဋ္ဌ"),
                ("ဉက္ကဌ္ဌ", "ဉက္ကဌ္ဌ", "ဥက္ကဋ္ဌ"),
                ("မေတာ", "မေတာ", "မေတ္တာ"),
                ("မစ်တာ", "မစ်တာ", "မေတ္တာ"),
                ("မေတ္တ", "မေတ္တ", "မေတ္တာ"),
                ("မေထ္ထာ", "မေထ္ထာ", "မေတ္တာ"),
                ("သစ်စာ", "သစ်စာ", "သစ္စာ"),
                ("သစာ", "သစာ", "သစ္စာ"),
                ("သဆ္ဆာ", "သဆ္ဆာ", "သစ္စာ"),
                ("သမာဒိ", "သမာဒိ", "သမာဓိ"),
                ("သမာဓီ", "သမာဓီ", "သမာဓိ"),
                ("သမ္မာဓိ", "သမ္မာဓိ", "သမာဓိ"),
                ("ဆပင်", "ဆပင်", "ဆံပင်"),
                ("ဇပင်", "ဇပင်", "ဆံပင်"),
                ("ဇံပင်", "ဇံပင်", "ဆံပင်"),
                ("ဆန်ပင်", "ဆန်ပင်", "ဆံပင်"),
                ("ဗိုဟ်", "ဗိုဟ်", "ဗိုလ်"),
                ("ဘိုလ်", "ဘိုလ်", "ဗိုလ်"),
                ("ကျင့်ဝပ်", "ကျင့်ဝပ်", "ကျင့်ဝတ်"),
                ("ကြင့်ဝတ်", "ကြင့်ဝတ်", "ကျင့်ဝတ်"),
                ("ကျင်ဝတ်", "ကျင်ဝတ်", "ကျင့်ဝတ်"),
                ("ဖုရား", "ဖုရား", "ဘုရား"),
                ("ဘုယား", "ဘုယား", "ဘုရား"),
                ("ဖုယား", "ဖုယား", "ဘုရား"),
                ("ကုတင်ပေါ်", "ကုတင်ပေါ်", "ခုတင်ပေါ်"),
                ("ဆေးရုံကုတင်", "ဆေးရုံကုတင်", "ဆေးရုံခုတင်"),
                ("ကုတင်ညောင်စောင်း", "ကုတင်ညောင်စောင်း", "ခုတင်ညောင်စောင်း"),
                ("ဒွာရ", "ဒွာရ", "ဒွါရ"),
                ("ဒွာဒသမ", "ဒွာဒသမ", "ဒွါဒသမ"),
                ("ဒဿမကိန်း", "ဒဿမကိန်း", "ဒသမကိန်း"),
                ("ပါရာစီတမော့", "ပါရာစီတမော့", "ပါရာစီတမော"),
                ("စိတ်ပုတီး", "စိတ်ပုတီး", "စိပ်ပုတီး"),
                ("ကြေးသွမ်းဘုရား", "ကြေးသွမ်းဘုရား", "ကြေးသွန်းဘုရား"),
                ("စာရွက်စာတန်း", "စာရွက်စာတန်း", "စာရွက်စာတမ်း")
            };

            foreach (var tc in v16ErrorCases)
            {
                var gIss = generalEngine.CheckText(tc.Input);
                Assert.True(gIss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V16 General check on '{tc.Input}': expected '{tc.ExactSpan}' -> #1 '{tc.Top1Fix}', got [{string.Join("; ", gIss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

                var fIss = formalEngine.CheckText(tc.Input);
                Assert.True(fIss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V16 Formal check on '{tc.Input}': expected '{tc.ExactSpan}' -> #1 '{tc.Top1Fix}', got [{string.Join("; ", fIss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. All 18 Valid Words from Screenshots 1 & 2 -> ZERO False Positives in Both Modes!
            string[] v16CleanWords =
            {
                // Screenshot 1 valid words (9)
                "မေတ္တာ",
                "သစ္စာ",
                "သမာဓိ",
                "ဆံပင်",
                "ဗိုလ်",
                "ကျင့်ဝတ်",
                "ဘုရား",
                "ခုတင်",
                "ဥက္ကဋ္ဌ",
                // Screenshot 2 valid words (9) + repaired targets
                "တံခါး",
                "စာရွက်",
                "စာတန်း",
                "စာတမ်း",
                "ငုပ်မိသဲတိုင်",
                "မီးခွက်",
                "မီးသီး",
                "ဒွါဒသမ",
                "ဒသမ",
                "ရတု",
                "ရကန်",
                "လေးချိုး",
                "ဓာတ်ပုံ",
                "ဗိုလ်မှူးကြီး",
                "ပါရာစီတမော",
                "ပုတီး",
                "ကြေးစည်",
                "ကြေးသွန်း"
            };

            foreach (string clean in v16CleanWords)
            {
                var gFp = generalEngine.CheckText(clean);
                Assert.True(gFp.Count == 0,
                    $"Unexpected V16 General false positive on '{clean}': [{string.Join("; ", gFp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

                var fFp = formalEngine.CheckText(clean);
                Assert.True(fFp.Count == 0,
                    $"Unexpected V16 Formal false positive on '{clean}': [{string.Join("; ", fFp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V17_MoiGovMm_OfficialMinistryCorpus_ZeroWrongness()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Official Ministry of Information (https://www.moi.gov.mm/) Sentences -> ZERO False Positives in FormalOfficial Mode!
            string[] moiCleanOfficialSentences =
            {
                "ပြည်သူ့လွှတ်တော်ဥက္ကဋ္ဌ ဦးခင်ရီ ပြည်ထောင်စုနယ်မြေ နေပြည်တော် ဇေယျာသီရိမြို့နယ်နှင့် ပုဗ္ဗသီရိမြို့နယ်တို့မှ ဒေသခံပြည်သူများအား လက်ခံတွေ့ဆုံသည်။",
                "နိုင်ငံတော်သမ္မတရုံးဝန်ကြီးဌာန ပြည်ထောင်စုဝန်ကြီး ဦးတင်အောင်စန်း တောင်ငူခရိုင်အတွင်း စိုက်ပျိုးရေး၊ မွေးမြူရေးနှင့် ဒေသဖွံ့ဖြိုးရေးလုပ်ငန်းများကို ကြည့်ရှုစစ်ဆေးသည်။",
                "ဗုဒ္ဓဝင်အဆိုအရ မြတ်စွာဘုရားရှင်သည် မဟာဝုန်တောကြီး၌ သီတင်းသုံးနေတော်မူစဉ် ကပိလဝတ်ပြည်သားများနှင့် ကောလိယပြည်သားများအကြား ရောဟိနီမြစ်ရေကို အသုံးပြုရေးနှင့်ပတ်သက်၍ အငြင်းပွားမှုဖြစ်ပွားခဲ့သည်။",
                "ရာသီဥတုပြောင်းလဲမှုကြောင့် စိုက်ပျိုးရေနှင့် သောက်သုံးရေ လုံလောက်စွာရရှိရေး၊ ရေအနက်မည်မျှရှိသည်ကို စစ်ဆေးပြီး မိုးဆက်ရွာပါက သတိပြုရန်နှိုးဆော်ထားသည်။",
                "ဈေးကွက်ကို လက်ဦးမှုရယူခြင်းထက် မျှတသောကုန်သွယ်မှုက ပိုအရေးကြီးပြီး အမြတ်အစွန်းထက် လူမှုဘဝနှင့် ကျန်းမာရေးကိုပါ ထည့်သွင်းစဉ်းစားရန် လိုအပ်သည်။",
                "မြန်မာနိုင်ငံသည် တိုင်းရင်းသားလူမျိုးပေါင်းစုံဖြင့် တစုတစည်းတည်း စုပေါင်းနေထိုင်ကြသည့် ပြည်ထောင်စုနိုင်ငံဖြစ်သည်။",
                "ပြည်သူများအနေဖြင့် အသိပညာဗဟုသုတများတိုးပွားရေးသာမက ကြိုတင်ခန့်မှန်းရန်အခက်ခဲဆုံး ဘေးအန္တရာယ်များနှင့် ရေကြီးမှုကြီးအပြီး ပြန်လည်ထူထောင်ရေးကို ဆောင်ရွက်လျက်ရှိသည်။",
                "ကွန်ပျူတာ၊ လက်ပ်တော့၊ တယ်လီဖုန်းဟန်းဆက်၊ ကွမ်တမ်ကွန်ပျူတာများနှင့် သီးသန့်အယ်လ်ဂိုရီသမ်များကို သုတေသနအင်စတီကျုတွင် လေ့လာလျက်ရှိသည်။",
                "နေ့စဉ် နိုင်ငံတော်အတွက် အမေရိကန်ဒေါ်လာ၊ တရုတ်ယွမ်၊ ထိုင်းဘတ်၊ အိန္ဒိယရူပီးတို့ကို ရောင်းဝယ်လျက်ရှိပြီး ဗန်ကောက်မြို့ကဲ့သို့ စည်ကားလာမည်ဖြစ်သည်။",
                "နိုင်ငံခြားသတင်းဌာနများဖြစ်သည့် တန်ယွတ်၊ ပီတီအိုင်၊ ရိုက်တာ၊ ယွန်ဟပ်၊ ဆင်ဟွာသတင်းဌာနတို့မှ ပေးပို့သော သတင်းများကို ဖမ်းယူထုတ်ဝေခဲ့ပြီး ဒို့ကျေးရွာဂျာနယ်ကို အပတ်စဉ်ထုတ်ဝေသည်။",
                "တရုတ်နိုင်ငံ ဟေလုံကျန်းပြည်နယ်၊ ဟဲနန်ပြည်နယ်၊ ယူနန်ပြည်နယ်နှင့် ကွမ်ရှီးကျွမ့် ကိုယ်ပိုင်အုပ်ချုပ်ခွင့်ရဒေသ နန်နင်းမြို့ရှိ ဖိန်လုတူးမြောင်း ပြတိုက်သို့ သွားရောက်လေ့လာခဲ့ကြသည်။",
                "အုမ္မသီးကျေးရွာတွင် ၃၁၅ ကေဗွီအေ ထရန်စဖော်မာနှင့် ဓာတ်အားလိုင်း တည်ဆောက်ခြင်းလုပ်ငန်းကို ဦးစားပေးအစီအစဉ်အလိုက် ဆောင်ရွက်ပေးသွားမည် ဖြစ်ပါကြောင်းဖြေကြားသည်။",
                "အပတ်စဉ် ပလတ်စတစ်နှင့် ကြွပ်ကြွပ်အိတ်မသုံးစွဲရနေ့အဖြစ် သတ်မှတ်ဆောင်ရွက်လျက်ရှိပြီး မီးသတ်ဦးစီးဌာနမှ ထုတ်ပြန်ထားသည့် ညွှန်ကြားချက်များကို တိကျစွာ လိုက်နာဆောင်ရွက်ကြရပါမည်။",
                "ကျောင်းနေအရွယ် ကလေးသူငယ်များသည် အနိုင်ကျင့်မှုနှင့် ပြက်ရယ်ပြုမှုများ မခံရစေရန်နှင့် ဈေးနှုန်းတိုးမြင့်မှုများ မဖြစ်ပေါ်စေရန် ကြပ်မတ်ဆောင်ရွက်လျက်ရှိသည်။",
                "မြန်မာ့စာတိုက်လုပ်ငန်း၏ စာရွေးစာပို့စခန်းနှင့် ဗြဟ္မပုတ္တရမြစ်ကြောင်းတစ်လျှောက် သစ်တောဇီဝဒြပ်ထုပမာဏ ၇၀၉ ဂစ်ဂါတန် ရှိသည်။"
            };

            foreach (string clean in moiCleanOfficialSentences)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V17 false positive on official moi.gov.mm sentence '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Real Typos, Lookalike Glyphs & Space-Split Broken Compounds Discovered on moi.gov.mm -> 100% Caught!
            var moiRealTypoCases = new (string Input, string ExactSpan, string Top1Fix)[]
            {
                ("ပြန်ကြားရေး၀န်ကြီးဌာန", "၀န်ကြီးဌာန", "ဝန်ကြီးဌာန"),
                ("ပြဌာန်းပြီးဥပဒေများ", "ပြဌာန်း", "ပြဋ္ဌာန်း"),
                ("ပြည့်သူ့လွှတ်တော်ရွေးကောက်ပွဲ", "ပြည့်သူ့လွှတ်တော်", "ပြည်သူ့လွှတ်တော်"),
                ("တိုင်းဒေသကြီးနှင့်ပြည်နည်အသီးသီး", "ပြည်နည်", "ပြည်နယ်"),
                ("သတင်းနှင့်စာနယ်ဇင်လုပ်ငန်း", "စာနယ်ဇင်", "စာနယ်ဇင်း"),
                ("ဆည်မြောင်ဝန်ကြီး", "ဆည်မြောင်", "ဆည်မြောင်း"),
                ("စာရေရိက္ခာဖူလုံရေး", "စာရေရိက္ခာ", "စားရေရိက္ခာ"),
                ("ရှာပါးစာအုပ်များ", "ရှာပါးစာအုပ်", "ရှားပါးစာအုပ်"),
                ("ဂြိုလ်တုစလောင်းများ", "ဂြိုလ်တုစလောင်း", "ဂြိုဟ်တုစလောင်း"),
                ("ဖွံဖြိုးတိုးတက်မှုအတွက်", "ဖွံဖြိုးတိုးတက်", "ဖွံ့ဖြိုးတိုးတက်"),
                ("လမ်းညွန်မှုများပေးခြင်း", "လမ်းညွန်မှု", "လမ်းညွှန်မှု"),
                ("မီဒီယာပုံစံအသစီ", "မီဒီယာပုံစံအသစီ", "မီဒီယာပုံစံအသစ်"),
                ("၁၉၄၇ ခုနစ်တွင် ပြန်ကြားရေးဌာန", "ခုနစ်", "ခုနှစ်"),
                ("လေးနှစ်ပြည့် အထိမ်း အမှတ် သုတေသနစာတမ်း", "အထိမ်း အမှတ်", "အထိမ်းအမှတ်"),
                ("သတင်းအချက် အလက်များ", "အချက် အလက်", "အချက်အလက်"),
                ("အလုပ်အကိုင်အခွင့် အလမ်းရရှိစေရန်", "အခွင့် အလမ်း", "အခွင့်အလမ်း"),
                ("လွှတ်တော်ကိုယ်စား လှယ်များအပြင်", "ကိုယ်စား လှယ်", "ကိုယ်စားလှယ်"),
                ("ပြန်လည်အသုံးပြုနိုင်သောပလတ် စတစ်ဖြတ်စ", "ပလတ် စတစ်", "ပလတ်စတစ်")
            };

            foreach (var tc in moiRealTypoCases)
            {
                var fIss = formalEngine.CheckText(tc.Input);
                Assert.True(fIss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V17 MOI typo check on '{tc.Input}': expected '{tc.ExactSpan}' -> #1 '{tc.Top1Fix}', got [{string.Join("; ", fIss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V18_AllGovMm_MultiMinistryCorpus_ZeroWrongness()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Cross-Ministry Official *.gov.mm Prose (49 Portals) -> ZERO False Positives in FormalOfficial Mode!
            string[] multiMinistryCleanSentences =
            {
                // unionsupremecourt.gov.mm & oagmac.gov.mm (Judiciary & Legal)
                "ပြည်ထောင်စုတရားလွှတ်တော်ချုပ်သည် စာချွန်တော်အမိန့်လျှောက်ထားမှုများနှင့် နိုတြီပဗ္ဗလစ်ခန့်အပ်ခြင်းဆိုင်ရာ ကိစ္စရပ်များကို ဥပဒေနှင့်အညီ စီရင်ဆုံးဖြတ်သည်။",
                // accm.gov.mm (Anti-Corruption Commission)
                "အဂတိလိုက်စားမှုတိုက်ဖျက်ရေးကော်မရှင်တွင် ဖြောင့်မတ်တည်ကြည်မှုမြှင့်တင်ရေးနှင့် အဂတိလိုက်စားမှုတားဆီးကာကွယ်ရေးအဖွဲ့များ ဖွဲ့စည်းဆောင်ရွက်လျက်ရှိသည်။",
                // secm.gov.mm & cbm.gov.mm (Capital Markets & Central Bank — parenthesized loanword coda clarifiers)
                "ငွေချေးသက်သေခံလက်မှတ်လုပ်ငန်းကြီးကြပ်ရေးကော်မရှင်သည် ဖတ်(စ်) မြန်မာအင်ဗက်(စ်)မဲ့(န်)နှင့် မြန်မာအက်ဂရိုအိတ်(စ်)ချိန်းပတ်ဘလစ်ကုမ္ပဏီတို့၏ အစုရှယ်ယာရောင်းဝယ်မှုကို ကြီးကြပ်သည်။",
                "မြန်မာနိုင်ငံတော်ဗဟိုဘဏ်က နိုင်ငံခြားသုံးငွေလဲလှယ်နှုန်းနှင့် ငွေကြေးဖောင်းပွမှုနှုန်း တည်ငြိမ်စေရန် ငွေကြေးမူဝါဒကို ထိန်းကျောင်းသည်။",
                // mora.gov.mm (Religious Affairs & Culture)
                "နိုင်ငံတော်သံဃမဟာနာယကအဖွဲ့ အဋ္ဌမအကြိမ် နိုင်ငံတော်ဗဟိုသံဃာ့ဝန်ဆောင်အစည်းအဝေး အဋ္ဌမနေ့တွင် အဘိဓဇမဟာရဋ္ဌဂုရု ဘဒ္ဒန္တကုမာရာဘိဝံသ ဆရာတော်ကြီးက ဩဝါဒကထာ မြွက်ကြားတော်မူသည်။",
                "တိပိဋကဓရ တိပိဋကကောဝိဒ ရွေးချယ်ရေးစာမေးပွဲတွင် ပိဋကတ်သုံးပုံကို အာဂုံပြန်ဆို သရဇ္ဈာယ်တော်မူကြသည်။",
                // moep.gov.mm & energy.gov.mm (Electric Power & Energy — မဂ္ဂါဝပ် / မဂ္ဂါဝပ်နာရီ)
                "နေရောင်ခြည်စွမ်းအင်သုံး ဓာတ်အားပေးစက်ရုံမှ လျှပ်စစ်ဓာတ်အား ၄၀ မဂ္ဂါဝပ်နှင့် နှစ်စဉ် ကီလိုဝပ်နာရီသန်းပေါင်းများစွာ သို့မဟုတ် မဂ္ဂါဝပ်နာရီ ထုတ်လုပ်ဖြန့်ဖြူးပေးလျက်ရှိသည်။",
                // moezala.gov.mm (Meteorology & Hydrology)
                "အနောက်တောင်မုတ်သုံလေ အားကောင်းရာမှ အားအလွန်ကောင်းနေပြီး အယ်လ်နီညိုနှင့် လာနီညာ ရာသီဥတုဖြစ်စဉ်များကြောင့် မိုးထစ်ချုန်းရွာနိုင်သည်။",
                // dmr.gov.mm & fda.gov.mm (Medical Research & FDA)
                "ဆေးသုတေသနဦးစီးဌာနတွင် အဏုဇီဝဗေဒ၊ ပဋိပစ္စည်းနှင့် မျိုးရိုးဗီဇဆိုင်ရာ ဓာတ်ခွဲစမ်းသပ်မှုများကို စံချိန်စံညွှန်းနှင့်အညီ စစ်ဆေးဆောင်ရွက်သည်။",
                // monrec.gov.mm & forestdepartment.gov.mm (Forestry & Environment)
                "သဘာဝပတ်ဝန်းကျင်နှင့် ဂေဟစနစ် ထိန်းသိမ်းရေးအတွက် ဒီရေတောစိုက်ခင်းများနှင့် ရေဝေရေလဲသစ်တောများကို စနစ်တကျ စိုက်ပျိုးထိန်းသိမ်းလျက်ရှိသည်။",
                // mcrd.gov.mm (Cooperatives & Rural Development — '[V] + လျှင် + [V] + ချင်း')
                "ကျေးလက်ဒေသဖွံ့ဖြိုးတိုးတက်ရေးဦးစီးဌာနသည် သဘာဝဘေးသတင်းရလျှင်ရချင်း သောက်သုံးရေသန့် အမြန်ဆုံးဖြန့်ဝေပေးသည်။",
                // construction.gov.mm (Construction — nominalizer '-မှု' boundary + postposition '+ အတွက် / အထိ / အမီ')
                "ပြည်တွင်းပြည်ပ လုပ်ငန်းရှင်များ ရင်းနှီးမြှုပ်နှံရမှုသက်သာစေရန်နှင့် သတ်မှတ်ချိန်အမီ ပြီးစီးစေရန် နိုဝင်ဘာလအထိ ဆောင်ရွက်ပေးမည်။",
                "သင်တန်းကာလ (၃)ရက်အတွက် သင်ထောက်ကူပစ္စည်းများနှင့် လွှတ်တော်(၃)ရပ်အတွက် အစီရင်ခံစာများကို သတ်မှတ်ချိန်အထိ ပေးပို့ပါရန် အကြောင်းကြားသည်။",
                // nptdc.gov.mm & ycdc.gov.mm (Municipal & Optative '+ ပါစေ')
                "နေပြည်တော်စည်ပင်သာယာရေးကော်မတီက မြို့တော်သန့်ရှင်းသာယာလှပရေးနှင့် ပြည်သူ့အကျိုးပြုလုပ်ငန်းများ တာဝန်ကျေပါစေ ဟု တိုက်တွန်းနှိုးဆော်သည်။",
                // mofa.gov.mm & mifer.gov.mm (Honorifics across space & foreign/ethnic titles)
                "ပြည်ထောင်စုဝန်ကြီး ဗိုလ်ချုပ်ကြီးတင်အောင်စန်း၊ ဒေါက်တာ ထွန်းမင်း၊ ဦးစောထွန်းအောင်မြင့်နှင့် မစ္စတာ မာသီးရက်စ် လေရှာ့စ်တို့ တက်ရောက်ဆွေးနွေးကြသည်။",
                // portal.moea.gov.mm (Ethnic Affairs — မြိုတိုင်းရင်းသား without masking မြိုနယ် -> မြို့နယ်)
                "ရခိုင်ပြည်နယ်ရှိ မြိုတိုင်းရင်းသား၊ သက်တိုင်းရင်းသားနှင့် ဒိုင်းနက်တိုင်းရင်းသားတို့၏ ရိုးရာယဉ်ကျေးမှုအမွေအနှစ်များကို ထိန်းသိမ်းစောင့်ရှောက်သည်။"
            };

            foreach (string clean in multiMinistryCleanSentences)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V18 false positive on official *.gov.mm sentence '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Real Typos, Pure Zawgyi Spans & Duplicate Combining Marks Discovered Across 49 *.gov.mm Portals -> 100% Caught!
            var govMmRealErrors = new (string Input, string ExactSpan, string Top1Fix)[]
            {
                // Pure U+1000..U+103F Zawgyi visual-order spans discovered on dsw.gov.mm & hybrid 'ဥပေဒ' inside Unicode on kachinstate.gov.mm
                ("ျပန္လည္ထူေထာင္ေရးဦးစီးဌာန", "ျပန္လည္ထူေထာင္ေရးဦးစီးဌာန", "ပြန်လည်ထူထောင်ရေးဦးစီးဌာန"),
                ("ကလေးသူငယ် နည္းဥပေဒ များ", "နည္းဥပေဒ", "နည်းဥပဒေ"),
                ("ကချင်ပြည်နယ် ပြည်နယ်ဥပေဒချုပ် တက်ရောက်သည်", "ပြည်နယ်ဥပေဒချုပ်", "ပြည်နယ်ဥပဒေချုပ်"),
                // Interleaved duplicate asat/dot-below ('နှင့်် -> နှင့်')
                ("ဝန်ကြီးဌာနနှင့်် ဦးစီးဌာနများ", "နှင့််", "နှင့်"),
                // Verified cross-ministry orthography errors vs 462-page သတ်ပုံကျမ်း
                ("စာရွက်စာတမ်းတင်သည့် ဘီဒို", "ဘီဒို", "ဗီရို"),
                ("ကျောက်စာနှင့် မော်ကွန်းကမ္ဗည်း ရေးထိုးသည်", "မော်ကွန်းကမ္ဗည်း", "မော်ကွန်းကမ္ပည်း"),
                ("ပရိတ်တရားတော် သရဇ္ဈယ် ခြင်း", "သရဇ္ဈယ်", "သရဇ္ဈာယ်"),
                ("မြေပြင်အနေအထား ပုံသဏ္ဍန် နှင့် ပုံသဏ္ဍာန်", "ပုံသဏ္ဍန်", "ပုံသဏ္ဌာန်"),
                ("ညွှန်ကြားရေးမှုးချုပ် နှင့် ဦးစီးမှုး", "ညွှန်ကြားရေးမှုးချုပ်", "ညွှန်ကြားရေးမှူးချုပ်"),
                ("၂၀၂၅-၂၀၂၆ ဘဏာရေးနှစ် အစီရင်ခံစာ", "ဘဏာရေးနှစ်", "ဘဏ္ဍာရေးနှစ်"),
                ("၂၀၂၅-၂၀၂၆ ဘဏရေးနှစ် ရသုံးမှန်းခြေ", "ဘဏရေးနှစ်", "ဘဏ္ဍာရေးနှစ်"),
                ("အရေးကြီးသည့် ကိစ္မ ရပ်များ", "ကိစ္မ", "ကိစ္စ"),
                ("အယ်လ်နီညိုလ် ရာသီဥတုဖြစ်စဉ်", "အယ်လ်နီညိုလ်", "အယ်လ်နီညို"),
                ("ပါမောက္ခ ဒေါတ်တာ အောင်ကျော်", "ဒေါတ်တာ", "ဒေါက်တာ"),
                ("လုပ်ငန်းလိုင်စင် ခွင့်ပြု့ ပေးခဲ့သည်", "ခွင့်ပြု့", "ခွင့်ပြု"),
                ("ကျေးလက်နေပြည်သူများ လူမှု့စီးပွား ဘဝမြင့်မားရေး", "လူမှု့စီးပွား", "လူမှုစီးပွား"),
                ("နှစ်ပတ်လည် အစိရင်ခံစာ ထုတ်ပြန်ခြင်း", "အစိရင်ခံစာ", "အစီရင်ခံစာ"),
                ("စံချိန်စံညွှန်း သက်မှတ် ချက်များ", "သက်မှတ်", "သတ်မှတ်"),
                ("ရှားပါး အပင်မျိုးစိပ် များထိန်းသိမ်းရေး", "အပင်မျိုးစိပ်", "အပင်မျိုးစိတ်"),
                ("ကချင်ပြည့်နယ် နှင့် ဖလမ်းမြိုနယ်", "ကချင်ပြည့်နယ်", "ကချင်ပြည်နယ်"),
                ("ဖလမ်းမြိုနယ် စည်ပင်သာယာရေးအဖွဲ့", "ဖလမ်းမြိုနယ်", "ဖလမ်းမြို့နယ်"),
                ("အဆိုပါ မြိုနယ် စည်ပင်သာယာရေးအဖွဲ့", "မြိုနယ်", "မြို့နယ်"),
                ("စစ်ကိုင်းတိုင်ဒေသကြီး အစိုးရအဖွဲ့", "တိုင်ဒေသကြီး", "တိုင်းဒေသကြီး"),
                ("အဂတိလိုက်စာမှု တားဆီးကာကွယ်ရေး", "အဂတိလိုက်စာမှု", "အဂတိလိုက်စားမှု"),
                // Ensure military rank typo is never masked by HonorificPrefixes, and Pali ordinal typo repairs cleanly
                ("ဗိုလ်မူးကြီး", "ဗိုလ်မူးကြီး", "ဗိုလ်မှူးကြီး"),
                ("ဒွာဒဒဿမ", "ဒွာဒဒဿမ", "ဒွါဒသမ")
            };

            foreach (var tc in govMmRealErrors)
            {
                var fIss = formalEngine.CheckText(tc.Input);
                Assert.True(fIss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V18 *.gov.mm error check on '{tc.Input}': expected '{tc.ExactSpan}' -> #1 '{tc.Top1Fix}', got [{string.Join("; ", fIss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V19_PublishedGovernmentNewsCorpus_ZeroWrongness()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Authentic Published Government News Sentences Across Ministries (moi, mrtv, moali, mora, mol, moezala, mifer, secm) -> 100% CLEAN!
            string[] cleanPublishedGovNews =
            {
                "နေပြည်တော် ဥပ္ပါတသန္တိစေတီတော်မြတ်ကြီး၌ ဆီမီးထွန်းညှိပူဇော်ပွဲ ကျင်းပသည်။",
                "သဘာဝဘေးအန္တရာယ် သတိပေးစနစ်များကို အဆင့်မြှင့်တင်ခဲ့သည်။",
                "နိုင်ငံတော်အခြေပြမိန့်ခွန်းနှင့် ကုန်သွယ်လုပ်ငန်းခွန် ဆိုင်ရာ ရှင်းလင်းချက်များ ထုတ်ပြန်သည်။",
                "နိုင်ငံတော်သံဃမဟာနာယကအဖွဲ့နှင့် သံဃဝန်ဆောင်များ၏ များရာသံဃဆန္ဒ ရယူ၍ အတည်ပြုကြေညာသည်။",
                "အဆိုပါဂျီအမ်အက်စ်ဝန်ကြီးများအစည်းအဝေးသို့ ပြည်ထောင်စုဝန်ကြီး သူရဦးမြင့်မောင် တက်ရောက်ခဲ့ပါသည်။",
                "ဦးတင်မိုးအောင် (ခ) နေရှိုင်းသည် သစ်ပင်စိုက်ပျိုးခြင်းနှင့် ဘဝခရီးမှာ တိုးတက်ရာတိုးတက်ကြောင်း ဆောင်ရွက်သည်။",
                "နှစ်နိုင်ငံကြား ဥပဒေကြမ်းပြုစုရေးနှင့် ကတ္တရာထပ်ပိုးလွှာခင်းခြင်း လုပ်ငန်းများ ဆောင်ရွက်လျက်ရှိသည်။",
                "မိုးလေဝသနှင့် ဇလဗေဒညွှန်ကြားမှုဦးစီးဌာနက ဘင်္ဂလားပင်လယ်အော်အခြေအနေကို ထုတ်ပြန်သည်။",
                "ဦးဆောင်ဦးရွက်ပြုမှုအစီအစဉ်ဖြင့် ပဏ္ဏိတလမ်းနှင့် ဂီတခန်းမတို့တွင် ပရဟိတဖွဲ့များ တွေ့ဆုံသည်။"
            };

            foreach (string clean in cleanPublishedGovNews)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V19 false positive on published gov news '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Real Published News Orthography Typos & Pure U+1000..U+103F Zawgyi Paragraph (mol.gov.mm) -> 100% Caught!
            var govNewsTypos = new (string Input, string ExactSpan, string Top1Fix)[]
            {
                ("ရန်ကုန်စတော့အိပ်ချိန်းတွင် အစုရှယ်ယာများ အရောင်းအဝယ်ပြုလုပ်သည်", "စတော့အိပ်ချိန်း", "စတော့အိတ်ချိန်း"),
                ("၁၉၈၀ ခုနှစ်၊ နိုင်ဝင်ဘာလ ၁ ရက်နေ့တွင် ကျင်းပသည်", "နိုင်ဝင်ဘာလ", "နိုဝင်ဘာလ"),
                ("စီမံကိန်းစီစစ်ရေးနှင့် တိုးတက်မှုအစီရင်ခံစာ", "စီစစ်", "စိစစ်"),
                ("အပူပိုင်းဇုံတွင် ကျောက်ပန်တောင်းမြို့နယ်၌ မိုးရွာသည်", "ဇုံ", "ဇုန်"),
                ("ညောင်ဦးခရိုင် ကျောက်ပန်တောင်းမြို့နယ်၌ ကျင်းပသည်", "ကျောက်ပန်တောင်းမြို့နယ်", "ကျောက်ပန်းတောင်းမြို့နယ်"),
                ("ညောင်ဦးနှင့် ကျောက်ပန်တောင်း ဒေသ၌ ကျင်းပသည်", "ကျောက်ပန်တောင်း", "ကျောက်ပန်းတောင်း"),
                ("ဗီဒီယိုကွန့်ဖရင့် စနစ်ဖြင့် တက်ရောက်ကြသည်", "ကွန့်ဖရင့်", "ကွန်ဖရင့်"),
                ("ဂြိုလ်တုစလောင်းများ တပ်ဆင်ပေးခဲ့သည်", "ဂြိုလ်တုစလောင်း", "ဂြိုဟ်တုစလောင်း"),
                ("အေထာက္အထားမဲ့ ျမန္မာအလုပ္သမားမ်ား ျပန္လည္လက္ခံေရး", "အေထာက္အထားမဲ့ ျမန္မာအလုပ္သမားမ်ား ျပန္လည္လက္ခံေရး", "အထောက်အထားမဲ့ မြန်မာအလုပ်သမားများ ပြန်လည်လက်ခံရေး")
            };

            foreach (var tc in govNewsTypos)
            {
                var iss = formalEngine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V19 published gov news typo check on '{tc.Input}': expected '{tc.ExactSpan}' -> '{tc.Top1Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V20_MyanmarToMyanmarAbidan_DefinitionsPosClassifiersAndSearch_WorkEndToEnd()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            var repo = new EncryptedDictionaryRepository(dbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Verify Official Myanmar-to-Myanmar Dictionary ('မြန်မာအဘိဓာန်') Definitions, Burmese Pronunciations, Classifiers, Origins & Synonyms Round-Trip Through Encrypted mlc_thatpon.db!
            var monkMatches = engine.SearchDictionary("ရဟန်း", 10);
            Assert.NotEmpty(monkMatches);
            var monkTop = monkMatches.First(e => e.Word == "ရဟန်း" && e.HasAbidanMetadata);
            Assert.Contains("ဗုဒ္ဓသာသနာ", monkTop.MeaningCue);
            Assert.Equal("ပါး", monkTop.NumeralClassifier);
            Assert.Contains("အရဟန္တ", monkTop.Etymology);
            Assert.Contains("ပဉ္စင်း", monkTop.Synonyms);
            Assert.NotEmpty(monkTop.BurmesePronunciation);
            Assert.Contains("• မျိုးပြပစ္စည်း: ပါး", monkTop.FormattedUsageDetails);

            // 2. Verify 'ထ-' headwords recovered from Burmese Wiktionary ('ထောက်ပံ့' -> V with Myanmar-to-Myanmar definition & synonyms)
            var supportMatches = engine.SearchDictionary("ထောက်ပံ့", 10);
            var supportTop = supportMatches.First(e => e.Word == "ထောက်ပံ့");
            Assert.Contains("ကူညီသည်", supportTop.MeaningCue);
            Assert.Equal("V", supportTop.PosTag);
            Assert.Contains("ပံ့ပိုး", supportTop.Synonyms);

            // 3. Verify Typo-Tolerant Dictionary Search (typing common misspelling 'ဥက္ကဌ' surfaces official 'ဥက္ကဋ္ဌ' + Myanmar-to-Myanmar definition first!)
            var chairmanSearch = engine.SearchDictionary("ဥက္ကဌ", 10);
            Assert.NotEmpty(chairmanSearch);
            Assert.Equal("ဥက္ကဋ္ဌ", chairmanSearch[0].Word);
            Assert.Contains("အကြီးအကဲ", chairmanSearch[0].MeaningCue);
            Assert.Contains("အုတ်ကထာ့", chairmanSearch[0].BurmesePronunciation);

            // 4. Verify Reverse Myanmar Synonym / Meaning Search & New Abidan Reduplicated Adverbs ('ကစဉ့်ကရဲ', 'ကိုးလိုးကန့်လန့်')
            var synSearch = engine.SearchDictionary("ဦးပဉ္စင်း", 25);
            Assert.Contains(synSearch, e => e.Word == "ရဟန်း" || e.Word == "ဦးပဉ္စင်း");

            var cleanAdvIssues = engine.CheckText("ပစ္စည်းများ ကစဉ့်ကရဲ နှင့် ကိုးလိုးကန့်လန့် ဖြစ်နေသည်။");
            Assert.Empty(cleanAdvIssues);

            Assert.Equal("အာမေဍိတ် (Interjection)", DictionaryEntry.FormatPosTag("INTJ"));

            // 5. Verify Expanded Noun-Classifier Agreement Grammar Checking ('မျိုးပြပစ္စည်း သဒ္ဒါစစ်ဆေးချက်')
            var clsIssues1 = engine.CheckText("သံဃာတော် သုံးယောက် နှင့် စေတီ နှစ်လုံး ဖူးမြော်သည်။");
            Assert.Contains(clsIssues1, i => i.OriginalText == "ယောက်" && i.Suggestions[0].Replacement == "ပါး");
            Assert.Contains(clsIssues1, i => i.OriginalText == "လုံး" && i.Suggestions[0].Replacement == "ဆူ");

            var clsIssues2 = engine.CheckText("မော်တော်ယာဉ် သုံးခု နှင့် သင်္ဘော နှစ်ခု ဆိုက်ကပ်သည်။");
            Assert.Contains(clsIssues2, i => i.OriginalText == "ခု" && i.Suggestions[0].Replacement == "စီး");
            Assert.Contains(clsIssues2, i => i.OriginalText == "ခု" && i.Suggestions[0].Replacement == "စင်း");
        }

        [Fact]
        public void V21_MassiveLexiconExpansion_And_DeepLinguisticReasoningEngine_EndToEnd()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            var repo = new EncryptedDictionaryRepository(dbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Verify Massive V9 Lexicon Scale (80,000+ Unique Lexical Headwords + 60,000+ Empirical Bigrams in Encrypted mlc_thatpon.db)
            Assert.True(trie.WordCount >= 80000, $"Expected >= 80,000 unique lexical headwords in V9, got {trie.WordCount}");
            Assert.True(trie.BigramCount >= 60000, $"Expected >= 60,000 empirical word bigrams in V9, got {trie.BigramCount}");

            // 2. Verify Newly Distilled U Hoke Sein Headwords + Pali Equivalents, Abidan Morphological Derivations & Corpus Compounds
            var catalystSearch = formalEngine.SearchDictionary("ကက်တလစ်", 10);
            Assert.Contains(catalystSearch, e => e.Word == "ကက်တလစ်" && e.PaliEquivalent.Contains("အာဝေဇက"));

            var derivSearch1 = formalEngine.SearchDictionary("ထောက်ပံ့ခြင်း", 10);
            Assert.Contains(derivSearch1, e => e.Word == "ထောက်ပံ့ခြင်း" && e.PosTag == "N");

            var cleanV9Sentence = formalEngine.CheckText("အတိုင်ပင်ခံပုဂ္ဂိုလ်က အကယ်ဒမီဆု ချီးမြှင့်ခြင်းနှင့် ထောက်ပံ့ခြင်း လုပ်ငန်းများ ဆောင်ရွက်သည်။");
            Assert.True(cleanV9Sentence.Count == 0,
                $"Unexpected V21 false positive: [{string.Join("; ", cleanV9Sentence.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

            // 3. Verify Universal Deep Burmese Linguistic Reasoning Engine across All 9 Linguistic Categories
            var reasoningCases = new (string Input, string ExpectedOriginal, string ExpectedFix, string ExpectedReasoningKeyword)[]
            {
                // (a) Pali 5-Vagga Consonant Stacking Law (တ-ဝဂ် တတိယ + စတုတ္ထ)
                ("ဗုဓ္ဓဘာသာ ထွန်းကားသည်။", "ဗုဓ္ဓ", "ဗုဒ္ဓ", "ဝဂ္ဂန္တဗျည်းစဉ်"),
                // (b) Pali 5-Vagga Consonant Stacking Law (ဋ-ဝဂ် ပထမ + ဒုတိယ)
                ("အဖွဲ့ဥက္ကဌ တက်ရောက်သည်။", "ဥက္ကဌ", "ဥက္ကဋ္ဌ", "ဋ-ဝဂ်"),
                // (c) Pali Non-Geminate Ordinal Law
                ("ဒဿမတန်း စာမေးပွဲ ဖြေဆိုသည်။", "ဒဿမ", "ဒသမ", "ပါဠိ"),
                // (d) Pali Silent-Coda Etymology Law
                ("ဂြိုလ်တု လွှတ်တင်သည်။", "ဂြိုလ်တု", "ဂြိုဟ်တု", "ဟသတ်"),
                // (e) Burmese Intervocalic Voicing Sandhi + Live Bigram Collocation ('နိုင်ငံတော် + သမ္မတ')
                ("နိုင်ငံတော်သမ္မဒ မိန့်ခွန်းပြောကြားသည်။", "သမ္မဒ", "သမ္မတ", "Voicing Sandhi"),
                // (f) Tibeto-Burman Simplex-Duplex Causative Aspiration ('ရွေ့' vs 'ရွှေ့')
                ("ရွေ့ပြောင်းလုပ်သား ကူညီရေးအဖွဲ့။", "ရွေ့ပြောင်း", "ရွှေ့ပြောင်း", "သကမ္မက"),
                // (g) Syllable Metathesis / Transposition ('မာကလာသီး' -> 'မာလကာသီး')
                ("မာကလာသီး စားသည်။", "မာကလာသီး", "မာလကာသီး", "Metathesis"),
                // (h) Real-Word Homophone Semantic Contrast ('မှီ' vs 'မီ')
                ("အချိန်မှီ ဆောင်ရွက်ပါသည်။", "အချိန်မှီ", "အချိန်မီ", "အနက်"),
                // (i) Classifier Semantic Agreement & Unicode Lookalike & Zawgyi Encoding
                ("သံဃာတော် သုံးယောက် ကြွလာသည်။", "ယောက်", "ပါး", "မျိုးပြပစ္စည်း"),
                ("၀န်ကြီးဌာန ထုတ်ပြန်ချက်။", "၀န်ကြီးဌာန", "ဝန်ကြီးဌာန", "U+1040"),
                ("နည္းဥပေဒ ထုတ္ျပန္သည္။", "နည္းဥပေဒ ထုတ္ျပန္သည္", "နည်းဥပဒေ ထုတ်ပြန်သည်", "Logical Order")
            };

            foreach (var rc in reasoningCases)
            {
                var issues = formalEngine.CheckText(rc.Input);
                Assert.NotEmpty(issues);
                var matched = issues.FirstOrDefault(i =>
                    i.OriginalText.Contains(rc.ExpectedOriginal) &&
                    i.Suggestions.Count > 0 &&
                    i.Suggestions[0].Replacement.Contains(rc.ExpectedFix));

                Assert.True(matched != null,
                    $"Expected issue '{rc.ExpectedOriginal}' -> '{rc.ExpectedFix}' on '{rc.Input}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
                Assert.False(string.IsNullOrWhiteSpace(matched!.GrammaticalReasoning),
                    $"Missing GrammaticalReasoning on '{rc.Input}' ({matched.OriginalText})");
                Assert.False(string.IsNullOrWhiteSpace(matched.AuthoritativeSource),
                    $"Missing AuthoritativeSource on '{rc.Input}' ({matched.OriginalText})");
                Assert.Contains(rc.ExpectedReasoningKeyword, matched.GrammaticalReasoning);
            }

            // Also verify live empirical bigram collocation proof appears on 'နိုင်ငံတော်သမ္မဒ' -> 'နိုင်ငံတော် + သမ္မတ'
            var presIssues = formalEngine.CheckText("နိုင်ငံတော်သမ္မဒ မိန့်ခွန်းပြောကြားသည်။");
            var presIssue = presIssues.First(i => i.OriginalText.Contains("သမ္မဒ"));
            Assert.Contains("နိုင်ငံတော် + သမ္မတ", presIssue.GrammaticalReasoning);
        }

        [Fact]
        public void V22_CustomDictionaryLifecycle_AddListSearchAndRemove_IsReversibleAndSafe()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            var repo = new EncryptedDictionaryRepository(dbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            int baselineOfficialCount = trie.OfficialThatponCount;

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            string tempFile = Path.Combine(Path.GetTempPath(), $"mm_custom_dict_test_{Guid.NewGuid():N}.txt");

            try
            {
                File.WriteAllText(tempFile, "# Custom Enterprise Dictionary\r\n", System.Text.Encoding.UTF8);

                // Before adding custom word, typo 'သမ္မဒ' is flagged
                Assert.NotEmpty(engine.CheckText("သမ္မဒ မိန့်ခွန်းပြောသည်။"));

                // Add one legitimate custom brand term and one accidental typo
                Assert.True(engine.AddCustomWord("မြန်မာဂိတ်ဝေး", tempFile));
                Assert.True(engine.AddCustomWord("သမ္မဒ", tempFile));

                // OfficialThatponCount must NEVER be inflated by custom user words
                Assert.Equal(baselineOfficialCount, trie.OfficialThatponCount);

                var customList = engine.GetCustomWords();
                Assert.Equal(2, customList.Count);
                Assert.Contains("မြန်မာဂိတ်ဝေး", customList);
                Assert.Contains("သမ္မဒ", customList);

                // SearchDictionary finds custom entry with CUSTOM POS label
                var dictHits = engine.SearchDictionary("မြန်မာဂိတ်ဝေး", 10);
                Assert.Contains(dictHits, e => e.Word == "မြန်မာဂိတ်ဝေး" && e.PosTag == "CUSTOM" && e.PosDisplayLabel.Contains("မှတ်သားဝေါဟာရ"));

                // While 'သမ္မဒ' is in custom dictionary, it is not flagged
                Assert.Empty(engine.CheckText("သမ္မဒ မိန့်ခွန်းပြောသည်။"));

                // Removing accidental 'သမ္မဒ' via Custom Dictionary Manager immediately restores spellcheck detection!
                Assert.True(engine.RemoveCustomWord("သမ္မဒ", tempFile));
                Assert.DoesNotContain("သမ္မဒ", engine.GetCustomWords());
                Assert.Contains("မြန်မာဂိတ်ဝေး", engine.GetCustomWords());

                var rechecked = engine.CheckText("သမ္မဒ မိန့်ခွန်းပြောသည်။");
                Assert.Contains(rechecked, i => i.OriginalText.Contains("သမ္မဒ") && i.Suggestions.Any(s => s.Replacement.Contains("သမ္မတ")));

                // Verify persisted file preserved header comment and 'မြန်မာဂိတ်ဝေး' while purging 'သမ္မဒ'
                string persistedText = File.ReadAllText(tempFile, System.Text.Encoding.UTF8);
                Assert.Contains("# Custom Enterprise Dictionary", persistedText);
                Assert.Contains("မြန်မာဂိတ်ဝေး", persistedText);
                Assert.DoesNotContain("သမ္မဒ", persistedText);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }

        [Fact]
        public void V23_WorldCountriesCitiesAndMyanmar330Townships_ZeroFalsePositives_AndCatchesGeographicTyposWithReasoning()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            var repo = new EncryptedDictionaryRepository(dbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Clean Diplomatic, World Capital, Myanmar 330-Township & Prose-Trap Sentences (MUST produce ZERO false positives)
            string[] cleanGeoAndTrapSentences =
            {
                "မြန်မာ၊ ထိုင်း၊ လာအို၊ ကမ္ဘောဒီးယား၊ ဗီယက်နမ်၊ မလေးရှား၊ စင်ကာပူ၊ အင်ဒိုနီးရှား၊ ဖိလစ်ပိုင်၊ ဘရူနိုင်းနှင့် တီမောလက်စ်တေနိုင်ငံတို့သည် အာဆီယံအဖွဲ့ဝင်များ ဖြစ်ကြသည်။",
                "ဆွစ်ဇာလန်နိုင်ငံ ဂျနီဗာမြို့၊ ဘယ်လာရုစ်နိုင်ငံ မင့်စ်မြို့၊ တူကီယဲနိုင်ငံ အန်ကာရာမြို့နှင့် ဩစတြေးလျနိုင်ငံ ဆစ်ဒနီမြို့တို့သို့ သွားရောက်ခဲ့သည်။",
                "ဗန်ကောက်မြို့နှင့် ဘန်ကောက်မြို့၊ ဖနွမ်းပင်မြို့၊ ဟနွိုင်းမြို့နှင့် ဗီယင်ကျန်းမြို့တို့တွင် အစည်းအဝေး ကျင်းပသည်။",
                "အိန္ဒိယနိုင်ငံ ဗုဒ္ဓဂယာ၊ ဗာရာဏသီ၊ သာဝတ္ထိ၊ ရာဇဂြိုဟ်၊ ကုသိနာရုံနှင့် နီပေါနိုင်ငံ လုမ္ဗိနီတို့သို့ ဘုရားဖူး သွားရောက်ကြသည်။",
                "ရန်ကုန်တိုင်းဒေသကြီး သင်္ဃန်းကျွန်းမြို့နယ်၊ ဗိုလ်တထောင်မြို့နယ်၊ ထန်းတပင်မြို့နယ်နှင့် သန်လျင်မြို့နယ်တို့တွင် လုပ်ငန်းဆောင်ရွက်သည်။",
                "ကချင်ပြည်နယ် မိုးညှင်းမြို့နယ်၊ ရှမ်းပြည်နယ် ပုံပါကျင်မြို့၊ ဟိုပုံးမြို့နယ်နှင့် မန္တလေးမြို့ ချမ်းအေးသာစံမြို့နယ်၊ ချမ်းအေးသာဇံမြို့နယ်တို့ ပါဝင်သည်။",
                "နာဂကိုယ်ပိုင်အုပ်ချုပ်ခွင့်ရဒေသ၊ ပအိုဝ်းကိုယ်ပိုင်အုပ်ချုပ်ခွင့်ရဒေသနှင့် ဝကိုယ်ပိုင်အုပ်ချုပ်ခွင့်ရတိုင်းတို့တွင် ဖွံ့ဖြိုးရေး ဆောင်ရွက်သည်။",
                "ခါကာဘိုရာဇီတောင်၊ အင်းတော်ကြီးကန်၊ သရေခေတ္တရာ ပျူမြို့ဟောင်းနှင့် ဘင်းမ်စတက်အဖွဲ့။",
                // Prose-collision traps against 2-syllable foreign/domestic toponyms
                "ခဲတံတိုကျိုးသွားသည်။",
                "ဝါသနာအားသန်လျှင် ကြိုးစားအားထုတ်ပါ။",
                "ဟိုပုန်းဒီပုန်း မလုပ်ဘဲ ရိုးသားစွာ ပြောဆိုပါ။",
                "ခြံထဲတွင် ဝါးဆစ်နီတစ်ချောင်း တွေ့ရှိသည်။"
            };

            foreach (string sent in cleanGeoAndTrapSentences)
            {
                var fp = engine.CheckText(sent);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on clean geographic/trap sentence '{sent}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement} ({x.IssueType})"))}]");
            }

            // 2. World Country, World Capital & Myanmar Township Misspellings + MOFA / GAD Authoritative Reasoning
            var geoErrors = new (string Input, string Wrong, string Fix, string ExpectedAuthority)[]
            {
                ("ဩစတေးလျနိုင်ငံသို့ ပညာတော်သင် စေလွှတ်သည်။", "ဩစတေးလျ", "ဩစတြေးလျ", "MOFA"),
                ("ဂျာမဏီနိုင်ငံနှင့် ပူးပေါင်းဆောင်ရွက်သည်။", "ဂျာမဏီ", "ဂျာမနီ", "MOFA"),
                ("ကမ်ဘောဒီးယားနိုင်ငံ ဖနောင်ပင်မြို့သို့ ရောက်ရှိသည်။", "ကမ်ဘောဒီးယား", "ကမ္ဘောဒီးယား", "MOFA"),
                ("တောင်ကိုးရီးယားသို့ သွားရောက်သည်။", "တောင်ကိုးရီးယား", "တောင်ကိုရီးယား", "MOFA"),
                ("ဘင်္ဂလားဒေ့ချ်နိုင်ငံ ဓာကာမြို့။", "ဘင်္ဂလားဒေ့ချ်", "ဘင်္ဂလားဒေ့ရှ်", "MOFA"),
                ("ဗီယက်နမ်နိုင်ငံ ဟနွိုင်မြို့တွင် ကျင်းပသည်။", "ဟနွိုင်", "ဟနွိုင်း", "MOFA"),
                ("ဩစတြေးလျနိုင်ငံ ဆစ်နီမြို့သို့ သွားသည်။", "ဆစ်နီမြို့", "ဆစ်ဒနီမြို့", "MOFA"),
                ("ဂျပန်နိုင်ငံ တိုကျိုးမြို့သို့ ရောက်ရှိသည်။", "တိုကျိုး", "တိုကျို", "MOFA"),
                ("အိန္ဒိယနိုင်ငံ ဗုဓ္ဓဂယာသို့ ဘုရားဖူးသွားသည်။", "ဗုဓ္ဓဂယာ", "ဗုဒ္ဓဂယာ", "MOFA"),
                ("တနသာရီတိုင်းဒေသကြီးတွင် မိုးရွာသွန်းသည်။", "တနသာရီ", "တနင်္သာရီ", "GAD"),
                ("ကချင်ပြည်နယ် မိုးညင်းမြို့နယ်တွင် တည်ရှိသည်။", "မိုးညင်း", "မိုးညှင်း", "GAD"),
                ("ကရင်ပြည်နယ် ဖျာပွန်မြို့နယ်။", "ဖျာပွန်", "ဖာပွန်", "GAD"),
                ("နေပြည်တော် ပျဉ်မနားမြို့တွင် ကျင်းပသည်။", "ပျဉ်မနား", "ပျဉ်းမနား", "GAD"),
                ("ရန်ကုန်တိုင်း သန်လျှင်မြို့နယ်သို့ သွားသည်။", "သန်လျှင်မြို့နယ်", "သန်လျင်မြို့နယ်", "GAD"),
                ("ရှမ်းပြည်နယ် ဟိုပုန်းမြို့နယ်။", "ဟိုပုန်းမြို့နယ်", "ဟိုပုံးမြို့နယ်", "GAD")
            };

            foreach (var ge in geoErrors)
            {
                var iss = engine.CheckText(ge.Input);
                var matched = iss.FirstOrDefault(i =>
                    (i.OriginalText.Contains(ge.Wrong) || ge.Wrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Any(s => s.Replacement.Contains(ge.Fix) || ge.Fix.Contains(s.Replacement)));
                Assert.True(matched != null,
                    $"Failed geographic check on '{ge.Input}': expected '{ge.Wrong}' -> '{ge.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
                Assert.Contains(ge.ExpectedAuthority, matched!.GrammaticalReasoning);
                Assert.Contains(ge.ExpectedAuthority, matched.AuthoritativeSource);
            }

            // 3. Bilingual English -> Official Burmese Proper-Noun Search in Dictionary Workspace
            var switzHits = engine.SearchDictionary("Switzerland", 10);
            Assert.Contains(switzHits, e => e.Word == "ဆွစ်ဇာလန်" && e.IsProperNoun);

            var tokyoHits = engine.SearchDictionary("tokyo", 10);
            Assert.Contains(tokyoHits, e => e.Word == "တိုကျို" && e.IsProperNoun);

            var mohnyinHits = engine.SearchDictionary("Mohnyin", 10);
            Assert.Contains(mohnyinHits, e => e.Word == "မိုးညှင်း" && e.IsProperNoun);

            var thingangyunHits = engine.SearchDictionary("Thingangyun", 10);
            Assert.Contains(thingangyunHits, e => e.Word.StartsWith("သင်္ဃန်းကျွန်း", StringComparison.Ordinal) && e.IsProperNoun);

            var aseanHits = engine.SearchDictionary("ASEAN", 10);
            Assert.Contains(aseanHits, e => e.Word == "အာဆီယံ" && e.IsProperNoun);
        }

        [Fact]
        public void V26_GovNewsAndPublishedDocs_MassiveCorpus_Lexicon_Bigrams_Rules_AndZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            Assert.True(model.WordBigrams.Count >= 80000, $"Expected >= 80,000 empirical word bigrams, got {model.WordBigrams.Count}");
            Assert.True(model.OrthographyRules.Count >= 1300, $"Expected >= 1,300 orthography rules, got {model.OrthographyRules.Count}");

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Real-world *.gov.mm Unicode, PDF-extraction & Typography errors caught cleanly
            var unicodeAndTypoCases = new (string Input, string Wrong, string TopFix)[]
            {
                ("မြန်မာနိူင်ငံတော်အစိုးရ။", "မြန်မာနိူင်ငံတော်", "မြန်မာနိုင်ငံတော်"),
                ("နိူင်ငံ ဖွံ့ဖြိုးရေး။", "နိူင်ငံ", "နိုင်ငံ"),
                ("တာဝန်လွဲှအပ်ခြင်း။", "လွဲှအပ်", "လွှဲအပ်"),
                ("သချင်္ာဘာသာရပ်။", "သချင်္ာ", "သင်္ချာ"),
                ("ရုံးချုပ်သိုို့ ပေးပို့ပါသည်။", "သိုို့", "သို့"),
                ("နံနက် ၀၉း၀၀ နာရီတွင် စတင်မည်။", "၀၉း၀၀", "၀၉:၀၀"),
                ("ဥပဒေနှင ့်အညီ ဆောင်ရွက်သည ်။", "နှင ့်", "နှင့်"),
                ("ကော်မတီအဖွဲ ့ဝင်များ။", "ဖွဲ ့", "ဖွဲ့"),
                ("၂၊၊ အစည်းအဝေးသို့ တက်ရောက်ပါသည်။", "၊၊", "။"),
                ("ဆောင်ရွက်ပါသည်၊၊", "၊၊", "။")
            };

            foreach (var tc in unicodeAndTypoCases)
            {
                string canonTopFix = UnicodeNormalizer.NormalizeCanonicalOrder(tc.TopFix);
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                        i.OriginalText.Contains(tc.Wrong) &&
                        i.Suggestions.Count > 0 &&
                        UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement).Contains(canonTopFix)),
                    $"Expected '{tc.Wrong}' -> '{canonTopFix}' in '{tc.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{string.Join("/", x.Suggestions.Select(s => s.Replacement))}"))}]");
            }

            // 2. Empirical *.gov.mm OrthographyRules (Pali pat-sint, GAD townships, loanwords, dropped vowels)
            var govRuleCases = new (string Input, string Wrong, string TopFix)[]
            {
                ("ကရဝိတ်ဖောင်တော်။", "ကရဝိတ်ဖောင်", "ကရဝိက်ဖောင်"),
                ("ကုန်ပစ္စည်း ဂိုထောင်များ။", "ဂိုထောင်", "ဂိုဒေါင်"),
                ("နေပြည်တော် ဇဗ္ဗူသီရိမြို့နယ်။", "ဇဗ္ဗူသီရိမြို့နယ်", "ဇမ္ဗူသီရိမြို့နယ်"),
                ("မဟာသတိပဠာန်သုတ်တော်။", "သတိပဠာန်", "သတိပဋ္ဌာန်"),
                ("ကော်မတီ ဥက္ကဠ နှင့် ဒုတိယဥက္ကဠ။", "ဥက္ကဠ", "ဥက္ကဋ္ဌ"),
                ("ပုညဝဎုနအသင်း။", "ပုညဝဎုန", "ပုညဝဍ္ဎန"),
                ("မင်းမြောက်တန်ဆာ သံလျက်ဓား။", "သံလျက်", "သန်လျက်"),
                ("ရန်ကုန်မြို့ ကြည်မြင်တိုင်နှင့် ပန်းပဲတန်းမြို့နယ်။", "ကြည်မြင်တိုင်", "ကြည့်မြင်တိုင်"),
                ("ရန်ကုန်မြို့ ကြည်မြင်တိုင်နှင့် ပန်းပဲတန်းမြို့နယ်။", "ပန်းပဲတန်းမြို့နယ်", "ပန်းဘဲတန်းမြို့နယ်"),
                ("နိုင်ငံပိုင် အဆောက်အဦးများ။", "အဆောက်အဦး", "အဆောက်အဦ"),
                ("နိုင်ငံခြား ရင်းနှီးမြှပ်နှံမှု လုပ်ငန်း။", "ရင်းနှီးမြှပ်နှံမှု", "ရင်းနှီးမြှုပ်နှံမှု"),
                ("အမြန်ဆုံး ဆာင်ရွက်ပေးပါရန်။", "ဆာင်ရွက်", "ဆောင်ရွက်"),
                ("မန္တလေးတိုင်း မလိူင်မြို့နယ်။", "မလိူင်", "မလှိုင်")
            };

            foreach (var rc in govRuleCases)
            {
                string expectedFull = UnicodeNormalizer.NormalizeCanonicalOrder(rc.Input.Replace(rc.Wrong, rc.TopFix));
                var iss = engine.CheckText(rc.Input);
                Assert.True(iss.Any(i =>
                {
                    if (i.Suggestions.Count == 0) return false;
                    string patched = rc.Input.Remove(i.StartIndex, i.Length).Insert(i.StartIndex, i.Suggestions[0].Replacement);
                    string canonPatched = UnicodeNormalizer.NormalizeCanonicalOrder(patched);
                    string canonTopFix = UnicodeNormalizer.NormalizeCanonicalOrder(rc.TopFix);
                    return canonPatched == expectedFull || canonPatched.Contains(canonTopFix);
                }),
                $"Expected gov rule '{rc.Wrong}' -> '{rc.TopFix}' in '{rc.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{string.Join("/", x.Suggestions.Select(s => s.Replacement))}"))}]");
            }

            // 3. Zero False Positives across authentic *.gov.mm statutory, municipal, court, tax & cultural sentences
            string[] authenticGovCleanSentences =
            {
                "ယင်းပြဋ္ဌာန်းချက်ကို တစ်သီးတစ်ခြား စဉ်းစားဆုံးဖြတ်ရမည်။",
                "မန္တလေးမြို့ သံလျက်မှော်တောင်ရပ်နှင့် ရွှေတချောင်းလမ်းတွင် ကွင်းဆင်းစစ်ဆေးသည်။",
                "လောပိတလမ်း၊ ကျောက်တလုံးလမ်းနှင့် ဘားနဒ်လမ်းတို့တွင် ရေထုတ်ပြွန် တည်ဆောက်ရေးလုပ်ငန်း ဆောင်ရွက်သည်။",
                "ပညာ့ရိပ်မွန် အိမ်ရာအမှတ် ၃၃/အေ တွင် ဝန်ထမ်းအိမ်ရာ စိစစ်ရေးကော်မတီ အစည်းအဝေး ကျင်းပသည်။",
                "စာအမှတ် ရပ/၄ နှင့် တတ-၅၃/၁၀ အရ ပတခ(ဝင)-၁ ပုံစံဖြင့် ဝင်ငွေခွန် ထမ်းဆောင်ရမည်။",
                "မိမိကလွှဲအပ်ထားသော ကိုယ်စားလှယ်က ရောင်းချနေမှုကို အတည်ပြုပေးခြင်း ဖြစ်ပါသည်။",
                "စက်ဆုပ်ဖွယ်အရာများကို ရှောင်ကြဉ်၍ ချွေတာသုံးစွဲရေးအကြောင်း ဆွေးနွေးကြသည်။",
                "နဝမအကြိမ်မြောက်အဖြစ် ဝါခေါင်လချည်ဖြူဖွဲ့မင်္ဂလာ အထိမ်းအမှတ်အခမ်းအနား ကျင်းပရာ တိုင်းရင်းသားများ တက်ရောက်ကြသည်။",
                "သောက်ရေကို စနစ်တကျ ကြိုချက်ခြင်းဖြင့် ရောဂါကြိုတင်ကာကွယ်ရေး ဆောင်ရွက်နိုင်သည်။",
                "မော်တော်ယာဉ် စီးခြင်းနှင့် စက်ဘီး စီးခြင်းတို့တွင် ယာဉ်စည်းကမ်း လိုက်နာရမည်။",
                "အဂတိလိုက်စားမှုတိုက်ဖျက်ရေးကော်မရှင်နှင့် ပြည်ထောင်စုစာရင်းစစ်ချုပ်ရုံးတို့ ပူးပေါင်းဆောင်ရွက်မှု မြှင့်တင်သည်။"
            };

            foreach (string clean in authenticGovCleanSentences)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on authentic *.gov.mm sentence '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 4. OfficialLetterSuite structural recognition of *.gov.mm Press Releases, Directives & Open Tenders
            string pressReleaseDoc =
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော်\n" +
                "နိုင်ငံခြားရေးဝန်ကြီးဌာန\n" +
                "သတင်းထုတ်ပြန်ချက်\n" +
                "ထုတ်ပြန်ချက်အမှတ်၊ ၁၂/၂၀၂၆\n" +
                "၁၃၈၈ ခုနှစ်၊ တန်ဆောင်မုန်းလဆန်း ၅ ရက်\n" +
                "(၂၀၂၆ ခုနှစ်၊ နိုဝင်ဘာလ ၁၁ ရက်)\n" +
                "၁။ အာဆီယံ ဆွေးနွေးဖက်နိုင်ငံများ အစည်းအဝေးသို့ မြန်မာကိုယ်စားလှယ်အဖွဲ့ တက်ရောက်ခဲ့ပါသည်။\n" +
                "၂။ အစည်းအဝေးတွင် ဒေသတွင်း ပူးပေါင်းဆောင်ရွက်ရေး ကိစ္စရပ်များကို ရင်းနှီးပွင့်လင်းစွာ ဆွေးနွေးခဲ့ကြပါသည်။\n" +
                "နိုင်ငံခြားရေးဝန်ကြီးဌာန";

            var audit = Official.OfficialLetterSuite.ValidateOfficialLetterStructure(pressReleaseDoc);
            Assert.True(audit.LooksLikeOfficialDocument);
            Assert.Equal(audit.TotalRequiredCount, audit.PassedCount);
        }

        [Fact]
        public void V36_MultiRoundGovBooksAndNews_IterativeCorpusLearning_Mojibake_BoundaryShield_AndZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            Assert.True(model.Entries.Count >= 118500, $"Expected >= 118,500 master lexicon entries, got {model.Entries.Count}");
            Assert.True(model.OrthographyRules.Count >= 1455, $"Expected >= 1,455 orthography rules, got {model.OrthographyRules.Count}");

            var trie = new MyanmarTrie();
            foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries()) trie.InsertEntry(se);
            foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules()) trie.InsertOrthographyRule(sr);
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Double-converted Zawgyi->Unicode mojibake sequence coalescing & exact repair (discovered on live news media players)
            string mojibakeSentence = "သငျ့စကျတှငျ ဒီမီဒီယာဖိုငျကို ဖှငျ့၍ မရပါ။";
            var mojibakeIssues = generalEngine.CheckText(mojibakeSentence);
            var mojibakeSpan = Assert.Single(mojibakeIssues, i => i.IssueType == ProofingIssueType.ZawgyiEncodingError);
            Assert.Equal("သငျ့စကျတှငျ ဒီမီဒီယာဖိုငျကို ဖှငျ့၍", mojibakeSpan.OriginalText);
            Assert.Equal(
                UnicodeNormalizer.NormalizeCanonicalOrder("သင့်စက်တွင် ဒီမီဒီယာဖိုင်ကို ဖွင့်၍"),
                UnicodeNormalizer.NormalizeCanonicalOrder(mojibakeSpan.Suggestions[0].Replacement));

            // 2. Empirical GOV, BOOK & NEWS orthography and numeral rules learned in Rounds 1 & 2
            var empiricalRuleCases = new (string Input, string Wrong, string TopFix)[]
            {
                ("သူငယ်ချင်းအရင်းအခြာကို ပြောပြသည်။", "အရင်းအခြာ", "အရင်းအချာ"),
                ("ဟိန္ဒူဇာတ်တော် ရာမယဏကို တင်ဆက်သည်။", "ရာမယဏ", "ရာမာယဏ"),
                ("ဤနှစ်၌ မိုဃ်းကောင်းသည် ဖြစ်လတ္တံ့။", "မိုဃ်း", "မိုး"),
                ("မြိန်ရည်ယှက်ရည် စားသုံးကြသည်။", "မြိန်ရည်ယှက်ရည်", "မြိန်ရေရှက်ရေ"),
                ("လေးယောက်ကုန်သော သူဌေးသ္မီးတို့သည် အစီရဝတီမြစ်၌ ရေချိုးကြ၏။", "သူဌေးသ္မီး", "သူဌေးသမီး"),
                ("လေးယောက်ကုန်သော သူဌေးသမီးတို့သည် အစီရဝတီမြစ်၌ ရေချိုးကြ၏။", "အစီရဝတီ", "အစိရဝတီ"),
                ("တရံတခါ သွားရောက်လေ့ရှိသည်။", "တရံတခါ", "တစ်ရံတစ်ခါ"),
                ("ကောင်းတင်ပြင်ဝယ် လင်းလက်တောက်ပသည်။", "ကောင်းတင်ပြင်", "ကောင်းကင်ပြင်"),
                ("အက်ပ်ကို အလွယ်တကူ ဒေါင်းလုတ်နိုင်ပါသည်။", "ဒေါင်းလုတ်", "ဒေါင်းလုဒ်")
            };

            foreach (var rc in empiricalRuleCases)
            {
                string canonTopFix = UnicodeNormalizer.NormalizeCanonicalOrder(rc.TopFix);
                var iss = formalEngine.CheckText(rc.Input);
                Assert.True(iss.Any(i =>
                        i.Suggestions.Count > 0 &&
                        UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement).Contains(canonTopFix)),
                    $"Expected '{rc.Wrong}' -> '{rc.TopFix}' in '{rc.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 3. Zero False Positives on authentic unseen GOV & Classical Literary BOOK sentences (FormalOfficial mode)
            string[] cleanFormalGovAndBookSentences =
            {
                "မြေစာရင်းပုံစံ (၁၀၅) မြေပုံရေးကူးခွင့် လျှောက်ထားနိုင်ပါသည်။",
                "စပါးခင်းအတွင်း ရွှေခရု ကျရောက်မှု ကာကွယ်နှိမ်နင်းရေးနှင့် သွင်းအားစုဈေးနှုန်း တည်ငြိမ်ရေး ဆောင်ရွက်သည်။",
                "မွေးမြူရေးခြံများတွင် နို့ပေါင်းစက်နှင့် စက်မှုဒီဇိုင်း မူပိုင်ခွင့် မှတ်ပုံတင်ခြင်း ဆောင်ရွက်သည်။",
                "သိက္ခာသမာဓိရှိမှု၊ စွမ်းဆောင်ရည်ရှိမှုအပြင် ကျားမ တန်းတူရှိမှုကို အခြေခံ၍ ရွေးချယ်ခန့်အပ်ရမည်။",
                "ထိုသီတင်းသည်သည် ဒေသနာတော်အဆုံး၌ သောတာပတ္တိဖိုလ်၌ တည်လေ၏။",
                "မနသာ စေ ပဒုဋ္ဌေန၊ ဘာသတိ ဝါ ကရောတိ ဝါ။",
                "ပလိတံ ၊ ညိုသောဆံပင်တို့၏အကြား၌ ရောက်သော ဆံဖြူကို။",
                "ပတိရူပေ ၊ သင့်တင့်သော ဂုဏ်သဘော၌။",
                "ကြိုးတပ်၍ပြီးပြီးသော် မဟောသဓသုခမိန်သည် အသစ်ပြင်၍ ခင်းဦးလော့ဟု ဆို၏။",
                "သင်ကား သင့်တင့်ရာအရပ်၌ နေလော့ဟု ဆို၍ လက်သုတ်ပုဝါကို သုတ်သင့်ကြပေသည်။",
                "ကလေးများ၏ ဘာသာစကားဖွံ့ဖြိုးမှုတွင် ပုံပြင်ပြောခြင်းနှင့် ကလေးကဗျာ ရွတ်ဆိုခြင်းတို့ ပါဝင်သည်။"
            };

            foreach (string clean in cleanFormalGovAndBookSentences)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected FormalOfficial false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 4. Zero False Positives on authentic General-mode Children's Literature & International News sentences
            string[] cleanGeneralBookAndNewsSentences =
            {
                "စိတ်ရှည်သည်းခံကာ ကြိုးစားတတ်တဲ့ ပန်းပုဆရာကောင်းတစ်ယောက် ဖြစ်လာပါတော့တယ်။",
                "အဲဒီနားမှာ လိပ်ကလေးတစ်ကောင်ကို တွေ့တော့ ချိုးကူသံလို သာသာယာယာ မေးပြန်တယ်။",
                "သည်တော့ မနက်ခင်းချိုးကလေးကူသလို ကလေးတို့လည်း တေးမဆိုချင်ကြဘူးလား။",
                "ဘီဘီစီမြန်မာပိုင်းသတင်းတွင် ပြေးစရာမြေမရှိသူများနှင့် ပိုးဝင်ရောဂါအကြောင်း ဖော်ပြထားပါတယ်။",
                "ရန်ဘီးရ် ကပူးရ်နဲ့ ဟန်တရုတ်မျိုးနွယ်စုအကြောင်း သတင်းဆောင်းပါး ဖတ်ရှုရပါတယ်။",
                "တရုတ်အထူးကိုယ်စားလှယ် တိန့်ရှီကျွင်းနဲ့ ကချင်လွတ်လပ်ရေးတပ် (KIA) ကရော ဘယ်လိုဖြေရှင်းနေလဲ။",
                "မြန်မာအမျိုးသမီး အယောက် ၂၀ ပါဝင်ကြောင်း သတင်းထုတ်ပြန်ပါတယ်။"
            };

            foreach (string clean in cleanGeneralBookAndNewsSentences)
            {
                var fp = generalEngine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected GeneralStandard false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 5. Primary human sculptor definition on 'ပန်းပုဆရာ' ranks ahead of ENCYC constellation homograph
            var sculptorResults = formalEngine.SearchDictionary("ပန်းပုဆရာ", 5);
            Assert.NotEmpty(sculptorResults);
            Assert.Equal("ပန်းပုဆရာ", sculptorResults[0].Word);
            Assert.NotEqual("ENCYC", sculptorResults[0].PosTag);
            Assert.Contains("ထုလုပ်", sculptorResults[0].MeaningCue);
        }

        [Fact]
        public void V37_SculptorStoryScreenshot_PanPuSayarKaung_AndHotSwapTrie_ZeroFalsePositives()
        {
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var trie = new MyanmarTrie();
            foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries()) trie.InsertEntry(se);
            foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules()) trie.InsertOrthographyRule(sr);
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            string[] storyParagraphs =
            {
                "ညီညီ့ရဲ့ ခြင်္သေ့ရုပ်လေးကို စစ်ဆေးတဲ့အခါမှာတော့ ဆရာကြီးရဲ့ မျက်နှာမှာ ကျေနပ်တဲ့အပြုံးတွေ ပေါ်လာတယ်။ ခြင်္သေ့ရုပ်လေးက သေးငယ်ပေမယ့် အသက်ဝင်နေပြီး လက်ရာမြောက်လွန်းတဲ့အတွက် အပြစ်ဆိုစရာ မရှိအောင် သေသပ်လှပနေတယ်။",
                "ထိုအခါ ဆရာကြီးက တပည့်နှစ်ယောက်ကို ခေါ် ပြီး \"အလုပ်တစ်ခုကို လုပ်တဲ့အခါ ဘယ်လောက်မြန်မြန် ပြီးသွားတယ်ဆိုတာထက်၊ ဘယ်လောက် စေတနာပါပါနဲ့ သေသပ်စွာ လုပ်ဆောင်ခဲ့တယ်ဆိုတာက ပိုအရေးကြီးတယ်။ ပြီးစလွယ်လုပ်တဲ့အလုပ်ဟာ ခဏတာ မျက်စိပသာဒဖြစ်ပေမယ့် ရေရှည်မှာ တန်ဖိုးမရှိတတ်ဘူး။ အချိန်ပေးပြီး စိတ်ရှည်ရှည်နဲ့ ကြိုးစားထားတဲ့ အလုပ်ကသာ အမြဲတမ်း တန်ဖိုးရှိပြီး ရေရှည်တည်တံ့တယ်\" လို့ ဆုံးမစကား ပြောကြားခဲ့တယ်။",
                "အဲဒီနောက်ကစပြီး အောင်အောင်ဟာ အလုပ်ကို ပြီးစလွယ်လုပ်တတ်တဲ့ အကျင့်ကို ဖျောက်ဖျက်ခဲ့ပြီး၊ ညီညီကဲ့သို့ စိတ်ရှည်သည်းခံကာ အကောင်းဆုံးဖြစ်အောင် ကြိုးစားတတ်တဲ့ ပန်းပုဆရာကောင်းတစ်ယောက် ဖြစ်လာပါတော့တယ်။"
            };

            // 1. GeneralStandard mode: 0 issues across all 3 paragraphs of the colloquial story
            foreach (string para in storyParagraphs)
            {
                var genIssues = generalEngine.CheckText(para);
                Assert.True(genIssues.Count == 0,
                    $"Expected 0 GeneralStandard issues on '{para}', got: {string.Join("; ", genIssues.Select(i => i.OriginalText))}");
            }

            // 2. FormalOfficial mode: 0 SpellingErrors (never flags 'ဆရာကောင်း -> စရာကောင်း'!), only Colloquial->Official style suggestions
            foreach (string para in storyParagraphs)
            {
                var offIssues = formalEngine.CheckText(para);
                Assert.DoesNotContain(offIssues, i => i.IssueType == ProofingIssueType.SpellingError);
                Assert.DoesNotContain(offIssues, i => i.OriginalText.Contains("ဆရာကောင်း"));
            }

            // 3. HotSwapTrie preserves custom words and invalidates paragraph caches cleanly
            var miniTrie = new MyanmarTrie();
            miniTrie.InsertEntry(new DictionaryEntry { Word = "မြန်မာ", MeaningCue = "နိုင်ငံ", PosTag = "N", Frequency = 500, IsOfficialThatpon = true });
            var swapEngine = new MyanmarProofingEngine(miniTrie, ProofingStyleMode.GeneralStandard);
            swapEngine.AddCustomWord("အထူးစမ်းသပ်ဝေါဟာရသစ်");
            Assert.Empty(swapEngine.CheckText("အထူးစမ်းသပ်ဝေါဟာရသစ်"));

            swapEngine.HotSwapTrie(trie);
            Assert.Empty(swapEngine.CheckText("အထူးစမ်းသပ်ဝေါဟာရသစ်"));
            Assert.Empty(swapEngine.CheckText(storyParagraphs[2]));
        }

        [Fact]
        public void V38_WrongMeaningAndCollocationEngineV2_TwoTierConfidence_AndMMTHATPON5RoundTrip()
        {
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            Assert.True(model.ContextRules.Count >= 700, $"Expected >=700 V21 ContextRules in master JSON, got {model.ContextRules.Count}");

            // 1. Verify MMTHATPON5 encrypted binary round-trip preserves all V21 ContextRules (AntiTriggerWords, IsWarningOnly, SkipParticles)
            int expectedCtxCount = model.ContextRules.Count;
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"v38_mmthatpon5_{Guid.NewGuid():N}.db");
            MyanmarTrie trie;
            try
            {
                const string testPw = "V38-MMTHATPON5-ROUNDTRIP-KEY!";
                foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries()) model.Entries.Add(se);
                foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules()) model.OrthographyRules.Add(sr);

                var repo = new EncryptedDictionaryRepository(tempDbPath);
                repo.CompileEncryptedDatabase(testPw, model);
                trie = repo.LoadIntoTrie(testPw);
                Assert.Equal(expectedCtxCount, trie.ContextualRuleCount);
            }
            finally
            {
                if (File.Exists(tempDbPath))
                    File.Delete(tempDbPath);
            }

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 2. Tier-1 High-Certainty Wrong-Meaning Homophones & SOV Particle-Skipping Clashes -> Red SpellingError (IsAutoFixSafe == true)
            var tier1Cases = new (string Input, string WrongSpan, string ExpectedTop1)[]
            {
                ("ကြိုးကို တင်းကျပ်စွာ ချဉ်သည်။", "ချဉ်", "ချည်"),
                ("စာအုပ်ကို သေချာစွာ ဖက်သည်။", "ဖက်", "ဖတ်"),
                ("ဆရာတော်ကို ရိုသေစွာ ကတော့သည်။", "ကတော့", "ကန်တော့"),
                ("သင်္ချာ ကဏန်း တွက်ချက်သည်။", "ကဏန်း", "ဂဏန်း"),
                ("ပင်လယ် ဂဏန်း ဖမ်းဆီးသည်။", "ဂဏန်း", "ကဏန်း"),
                ("မျက်နှာထား ကပ်သည်။", "ကပ်", "ကတ်"),
                ("အမိန့် ချမှက်ခဲ့သည်။", "ချမှက်", "ချမှတ်"),
                ("ရှေးဟောင်းအမွေအနှစ်များကို ပြုပြင် ထိမ်းသိမ်းရမည်။", "ထိမ်းသိမ်း", "ထိန်းသိမ်း"),
                ("အမြတ်ခွန်ဥပဒေကို ရုတ်သိမ်းလိုက်သည်။", "ရုတ်သိမ်း", "ရုပ်သိမ်း"),
                ("ဆေးရုံ အခမ်းအတွင်း ကုသပေးနေသည်။", "အခမ်း", "အခန်း")
            };

            foreach (var (input, wrongSpan, expectedTop1) in tier1Cases)
            {
                var issues = formalEngine.CheckText(input);
                var hit = issues.FirstOrDefault(i => i.OriginalText == wrongSpan);
                Assert.True(hit != null, $"Expected Tier-1 SpellingError on '{wrongSpan}' in '{input}', got: [{string.Join(", ", issues.Select(x => x.OriginalText))}]");
                Assert.Equal(ProofingIssueType.SpellingError, hit!.IssueType);
                Assert.False(hit.IsContextualWarning);
                Assert.True(hit.IsAutoFixSafe);
                Assert.NotEmpty(hit.Suggestions);
                Assert.Equal(expectedTop1, hit.Suggestions[0].Replacement);
            }

            // 3. Clause-Level AntiTriggerWords Veto Protection -> 0 False Positives when legitimate meaning cue co-occurs
            string[] vetoCleanSentences =
            {
                "ကြိုးအနီးတွင် ချဉ်ပေါင်ဟင်း အရသာ ချဉ်သည်။",
                "စာအုပ်ကို ကိုင်လျက် မိခင်၏ ရင်ခွင်၌ ဖက်သည်။",
                "ပေါင်မုန့်ကို လက်ဖြင့် ချိုးဖဲ့သည်။",
                "မိမိတို့၏ ဆုံးဖြတ်ချက်အတိုင်း ခိုင်မာစွာ ရပ်တည်သည်။"
            };

            foreach (string vetoSentence in vetoCleanSentences)
            {
                var vetoIssues = formalEngine.CheckText(vetoSentence);
                Assert.True(vetoIssues.Count == 0,
                    $"Expected 0 issues on veto-protected sentence '{vetoSentence}', got: [{string.Join("; ", vetoIssues.Select(i => $"{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement} ({i.IssueType})"))}]");
            }

            // 4. Tier-2 ContextualMeaningWarning ('အနက်သံသယ') -> Amber Warning, Excluded from Blind Fix All (IsAutoFixSafe == false)
            var tier2WarningCases = new (string Input, string WrongSpan, string ExpectedTop1)[]
            {
                ("ဥပဒေနှင့် စည်းကမ်းများကို ချိုးဖဲ့သည်။", "ချိုးဖဲ့", "ချိုးဖောက်"),
                ("ဈေးနှုန်းကို လျော့ပေးပါသည်။", "လျော့", "လျှော့"),
                ("တရားရုံးတွင် သတ်သေ ထွက်ဆိုသည်။", "သတ်သေ", "သက်သေ"),
                ("မိုးရွာသွန်းမှု ခိုင်မာစွာ ဖြစ်ပေါ်သည်။", "ခိုင်မာစွာ", "သည်းထန်စွာ"),
                ("အသတ် အန္တရာယ် ကယ်ဆယ်ရေး လုပ်ငန်း။", "အသတ်", "အသက်")
            };

            var collectedWarnings = new List<ProofingIssue>();
            foreach (var (input, wrongSpan, expectedTop1) in tier2WarningCases)
            {
                var issues = formalEngine.CheckText(input);
                var warn = issues.FirstOrDefault(i => i.OriginalText == wrongSpan);
                Assert.True(warn != null, $"Expected Tier-2 ContextualMeaningWarning on '{wrongSpan}' in '{input}', got: [{string.Join(", ", issues.Select(x => x.OriginalText))}]");
                Assert.Equal(ProofingIssueType.ContextualMeaningWarning, warn!.IssueType);
                Assert.True(warn.IsContextualWarning);
                Assert.False(warn.IsAutoFixSafe);
                Assert.False(warn.IsGrammarOrStyle);
                Assert.NotEmpty(warn.Suggestions);
                Assert.Equal(expectedTop1, warn.Suggestions[0].Replacement);
                Assert.False(string.IsNullOrWhiteSpace(warn.GrammaticalReasoning));
                collectedWarnings.Add(warn);
            }

            // 5. DocumentQualityAnalyzer tracks ContextWarningCount separately without unfairly tanking SpellingIssueCount
            var qualityReport = DocumentQualityAnalyzer.Analyze(
                "ဥပဒေနှင့် စည်းကမ်းများကို ချိုးဖဲ့သည်။ တရားရုံးတွင် သတ်သေ ထွက်ဆိုသည်။",
                collectedWarnings.Take(2).ToList(),
                formalEngine);
            Assert.Equal(0, qualityReport.SpellingIssueCount);
            Assert.Equal(2, qualityReport.ContextWarningCount);
        }

        [Fact]
        public void V39_SalweenRiverScreenshot_AdhikaSitTalinPyin_ExactWordPrefixTruncationShield_ZeroFalsePositives()
        {
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var trie = new MyanmarTrie();
            foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries()) trie.InsertEntry(se);
            foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules()) trie.InsertOrthographyRule(sr);
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            string[] salweenParagraphs =
            {
                "ဒေသများအနက် တစ်ခုအဖြစ် သတ်မှတ်ခံထားရသည်။ ထို့အပြင် သံလွင်မြစ်သည် စိုက်ပျိုးရေးနှင့် ရေလုပ်ငန်းများကို များစွာအထောက်အကူပြုပေးနိုင်ရုံသာမက လွန်ခဲ့သောနှစ်ပေါင်း ၅,၀၀၀ ခန့်ကတည်းက တိဘက်ကုန်းပြင်မြင့်မှ တောင်ဘက်သို့ ရွှေ့ပြောင်းအခြေချလာခဲ့ကြသော တိုင်းရင်းသားလူမျိုးစုပေါင်းများစွာ မှီတင်းနေထိုင်ရာ သမိုင်းဝင်ဒေသလည်းဖြစ်သည်။",
                "သမိုင်းကြောင်းတစ်လျှောက်တွင် သံလွင်မြစ်သည် မြန်မာ၊ ထိုင်း နှင့် တရုတ်အင်ပါယာတို့အကြား အရေးပါသော နယ်နိမိတ်မျဉ်းတစ်ခုအဖြစ် တည်ရှိခဲ့ပြီး ၁၉၄၈ ခုနှစ် မြန်မာနိုင်ငံလွတ်လပ်ရေးရပြီးနောက်ပိုင်းတွင်လည်း ရှမ်းပြည်နယ်နှင့် ကရင်ပြည်နယ်အတွင်းရှိ ပြည်တွင်းစစ်ပွဲများအတွက် အဓိကစစ်တလင်းပြင်တစ်ခု ဖြစ်ခဲ့သည်။ ယနေ့ခေတ်တွင် သံလွင်မြစ်သည် ရေကာတာတည်ဆောက်မှု အနည်းဆုံးနှင့် သဘာဝအတိုင်း စီးဆင်းနေဆဲဖြစ်သော အာရှတိုက်၏ အဓိကမြစ်ကြီးများထဲတွင် ပါဝင်နေသေးသည်။ မြစ်ဖျားခံရာနေရာမှ အမြင့်ပေ ၅,၀၀၀ မီတာကျော်မှ စီးဆင်းလာသဖြင့် ရေအားလျှပ်စစ် ထုတ်လုပ်နိုင်စွမ်း အလွန်မြင့်မားသောကြောင့် ၁၉၇၀ ပြည့်လွန်နှစ်များမှစ၍ မြန်မာနှင့် ထိုင်းအစိုးရတို့က ရေကာတာကြီးများ တည်ဆောက်ရန် ကြိုးပမ်းခဲ့ကြသည်။ သို့သော်လည်း တရုတ်နိုင်ငံဘက်တွင် ရေကာတာစီမံကိန်းများကို ဖျက်သိမ်းကာ အမျိုးသားဥယျာဉ်အဖြစ် ပြောင်းလဲသတ်မှတ်ခဲ့သည့်အတွက် မြန်မာနှင့် ထိုင်းနိုင်ငံတို့ရှိ သံလွင်မြစ်ပေါ်မှ အနာဂတ်ရေကာတာစီမံကိန်းများမှာမူ မရေမရာ ဖြစ်လျက်ရှိသည်။"
            };

            foreach (string para in salweenParagraphs)
            {
                var fIssues = formalEngine.CheckText(para);
                Assert.True(fIssues.Count == 0,
                    $"Expected 0 FormalOfficial issues on Salween River paragraph, got: [{string.Join("; ", fIssues.Select(i => $"{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}]");

                var gIssues = generalEngine.CheckText(para);
                Assert.True(gIssues.Count == 0,
                    $"Expected 0 GeneralStandard issues on Salween River paragraph, got: [{string.Join("; ", gIssues.Select(i => $"{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Systemic check across similar 'အဓိက + [C-...]' / 'ပဓာန + [...]' / 'သဘာဝ + [...]' exact-word prefix-truncation boundaries
            string[] systemicCompounds =
            {
                "အဓိကစစ်တလင်းပြင်တစ်ခု ဖြစ်ခဲ့သည်။",
                "အဓိကစစ်ကြောင်းကြီး ချီတက်သည်။",
                "အဓိကစားသုံးကုန်ပစ္စည်းများ ဖြစ်သည်။",
                "ပဓာနကျသော အချက်တစ်ခု ဖြစ်သည်။",
                "သဘာဝစစ်စစ် ထွက်ကုန်ဖြစ်သည်။"
            };
            foreach (string sc in systemicCompounds)
            {
                Assert.Empty(formalEngine.CheckText(sc));
            }

            // Ensure genuine isolated 'အဓိ' typo (when NOT part of 'အဓိက' / 'အဓိပ္ပာယ်' / 'အဓိဋ္ဌာန်') STILL flags 'အဓိ -> အထိ'!
            var genuineTypoIssues = formalEngine.CheckText("ရန်ကုန်မြို့အဓိ သွားရောက်ခဲ့သည်။");
            Assert.Contains(genuineTypoIssues, i => i.OriginalText == "အဓိ" && i.Suggestions.FirstOrDefault()?.Replacement == "အထိ");
        }

        [Fact]
        public void V40_ExcelSpreadsheetProofing_FormulaProtection_1BasedSafeArray_MultiIssueCellDelta_SkipAmberFixAll_ToolkitAndUndo()
        {
            // 1. Verify Excel A1 column coordinate conversion up to XFD (16384)
            Assert.Equal("A", SpreadsheetProofingHelper.ColumnIndexToLetters(1));
            Assert.Equal("Z", SpreadsheetProofingHelper.ColumnIndexToLetters(26));
            Assert.Equal("AA", SpreadsheetProofingHelper.ColumnIndexToLetters(27));
            Assert.Equal("AZ", SpreadsheetProofingHelper.ColumnIndexToLetters(52));
            Assert.Equal("ZZ", SpreadsheetProofingHelper.ColumnIndexToLetters(702));
            Assert.Equal("AAA", SpreadsheetProofingHelper.ColumnIndexToLetters(703));
            Assert.Equal("XFD", SpreadsheetProofingHelper.ColumnIndexToLetters(16384));

            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var trie = new MyanmarTrie();
            foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries()) trie.InsertEntry(se);
            foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules()) trie.InsertOrthographyRule(sr);
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 2. Build realistic 1-based Excel COM SAFEARRAYs (object[1..4, 1..3], anchored at B2 -> baseRow=2, baseCol=2)
            var val2 = (object?[,])Array.CreateInstance(typeof(object), new[] { 4, 3 }, new[] { 1, 1 });
            var fml = (object?[,])Array.CreateInstance(typeof(object), new[] { 4, 3 }, new[] { 1, 1 });

            // Row offset 0 -> Excel Row 2 (B2, C2, D2): Header + Numeric double + SUM formula
            val2[1, 1] = "ဘဏ္ဍာရေးနှစ် အစီရင်ခံစာ"; fml[1, 1] = "ဘဏ္ဍာရေးနှစ် အစီရင်ခံစာ";
            val2[1, 2] = 1500000.50;                  fml[1, 2] = 1500000.50;
            val2[1, 3] = 4500000.00;                  fml[1, 3] = "=SUM(C2:C10)";

            // Row offset 1 -> Excel Row 3 (B3, C3, D3): Formula cell whose evaluated string contains a typo ("စီပွားရေး")!
            // MUST be 100% shielded and NEVER flagged or overwritten!
            val2[2, 1] = "စီပွားရေး ချမှက်ချက်";      fml[2, 1] = "=IF(A3=1,\"စီပွားရေး ချမှက်ချက်\",\"မှန်သည်\")";
            val2[2, 2] = "INV-2026-0091";             fml[2, 2] = "INV-2026-0091";
            val2[2, 3] = true;                        fml[2, 3] = true;

            // Row offset 2 -> Excel Row 4 (B4, C4, D4):
            // B4: 2 spelling issues in the SAME cell ("စီပွားရေး" -> "စီးပွားရေး", "ချမှက်" -> "ချမှတ်")
            // C4: Valid Salween compound ("အဓိကစစ်တလင်းပြင်တစ်ခု ဖြစ်ခဲ့သည်။") -> 0 issues
            // D4: Amber ContextualMeaningWarning ("ဥပဒေကို ချိုးဖဲ့သည်။" -> "ချိုးဖောက်")
            val2[3, 1] = "မြန်မာနိုင်ငံ၏ စီပွားရေး ချမှက်ချက်"; fml[3, 1] = "မြန်မာနိုင်ငံ၏ စီပွားရေး ချမှက်ချက်";
            val2[3, 2] = "အဓိကစစ်တလင်းပြင်တစ်ခု ဖြစ်ခဲ့သည်။";   fml[3, 2] = "အဓိကစစ်တလင်းပြင်တစ်ခု ဖြစ်ခဲ့သည်။";
            val2[3, 3] = "ဥပဒေကို ချိုးဖဲ့သည်။";                fml[3, 3] = "ဥပဒေကို ချိုးဖဲ့သည်။";

            // Row offset 3 -> Excel Row 5 (B5, C5, D5):
            // B5: Mixed Spelling ("စီပွားရေး") + Amber Context Warning ("ချိုးဖဲ့") in the SAME cell!
            // C5: Legacy Zawgyi text ("ျမန္မာစာ")
            // D5: ASCII digits inside Burmese text with technical code ("၂၀၂၆ ခုနှစ် အစီရင်ခံစာ အမှတ် 15 (ISO-9001)")
            val2[4, 1] = "စီပွားရေး ဥပဒေကို ချိုးဖဲ့သည်။";       fml[4, 1] = "စီပွားရေး ဥပဒေကို ချိုးဖဲ့သည်။";
            val2[4, 2] = "ျမန္မာစာ";                            fml[4, 2] = "ျမန္မာစာ";
            val2[4, 3] = "၂၀၂၆ ခုနှစ် အစီရင်ခံစာ အမှတ် 15 (ISO-9001)"; fml[4, 3] = "၂၀၂၆ ခုနှစ် အစီရင်ခံစာ အမှတ် 15 (ISO-9001)";

            var issues = SpreadsheetProofingHelper.ScanBlockValues(
                engine,
                workbookName: "MinistryBudget.xlsx",
                sheetName: "စာရင်း(၁)",
                baseRowOneBased: 2,
                baseColOneBased: 2,
                rowCount: 4,
                colCount: 3,
                rawValue2Block: val2,
                rawFormulaBlock: fml,
                blockHasFormula: null); // mixed sheet

            // Formula cell B3 (Row 3, Col 2) and valid cells B2/C2/D2/C4 MUST have ZERO issues!
            Assert.DoesNotContain(issues, i => i.Row == 3 && i.Column == 2);
            Assert.DoesNotContain(issues, i => i.Row == 2);
            Assert.DoesNotContain(issues, i => i.Row == 4 && i.Column == 3);

            // Check B4 (Row 4, Col 2 -> "စာရင်း(၁)!B4") has both spelling issues and test single-issue delta shifting!
            var b4Issues = issues.Where(i => i.Row == 4 && i.Column == 2).OrderBy(i => i.Issue.StartIndex).ToList();
            Assert.Equal(2, b4Issues.Count);
            Assert.Equal("စာရင်း(၁)!B4", b4Issues[0].DisplayLocation);
            Assert.Equal("စီပွားရေး", b4Issues[0].Issue.OriginalText);
            Assert.Equal("ချမှက်", b4Issues[1].Issue.OriginalText.Substring(0, Math.Min(6, b4Issues[1].Issue.OriginalText.Length)));

            string liveB4 = (string)val2[3, 1]!;
            bool okFirst = SpreadsheetProofingHelper.ApplySingleIssueReplacement(
                liveB4,
                b4Issues[0],
                "စီးပွားရေး",
                issues,
                out string updatedB4Step1);
            Assert.True(okFirst);
            Assert.Equal("မြန်မာနိုင်ငံ၏ စီးပွားရေး ချမှက်ချက်", updatedB4Step1);
            Assert.True(b4Issues[0].IsResolved);
            Assert.False(b4Issues[1].IsResolved);

            // Second fix in the same cell uses the shifted StartIndex (+1) without corrupting offsets!
            string sug2 = b4Issues[1].Issue.Suggestions[0].Replacement;
            bool okSecond = SpreadsheetProofingHelper.ApplySingleIssueReplacement(
                updatedB4Step1,
                b4Issues[1],
                sug2,
                issues,
                out string updatedB4Step2);
            Assert.True(okSecond);
            Assert.Equal("မြန်မာနိုင်ငံ၏ စီးပွားရေး ချမှတ်ချက်", updatedB4Step2);

            // 3. Test Batch Fix All: fixes B5's "စီပွားရေး -> စီးပွားရေး" & C5's Zawgyi, while SKIPPING Amber "ချိုးဖဲ့" in D4 & B5!
            var batchOps = SpreadsheetProofingHelper.BuildBatchReplacementsByCell(issues);
            Assert.DoesNotContain(batchOps, op => op.Row == 3 && op.Column == 2); // Formula B3 untouched
            Assert.DoesNotContain(batchOps, op => op.Row == 4 && op.Column == 4); // Pure Amber D4 untouched

            var b5Op = Assert.Single(batchOps.Where(op => op.Row == 5 && op.Column == 2));
            Assert.Equal("စီးပွားရေး ဥပဒေကို ချိုးဖဲ့သည်။", b5Op.UpdatedText);

            // Remaining Amber warning on B5 ("ချိုးဖဲ့") is STILL unresolved AND its StartIndex shifted by +1!
            var b5RemainingAmber = Assert.Single(issues.Where(i => i.Row == 5 && i.Column == 2 && !i.IsResolved));
            Assert.True(b5RemainingAmber.Issue.IsContextualWarning);
            Assert.True(SpreadsheetProofingHelper.ApplySingleIssueReplacement(
                b5Op.UpdatedText,
                b5RemainingAmber,
                b5RemainingAmber.Issue.Suggestions[0].Replacement,
                issues,
                out string b5Final));
            Assert.Equal("စီးပွားရေး ဥပဒေကို ချိုးဖောက်သည်။", b5Final);

            // 4. Test Scalar 1-Cell Range (Excel COM returns scalar string instead of object[,] when CountLarge == 1)
            var scalarHits = SpreadsheetProofingHelper.ScanBlockValues(
                engine, "Book1.xlsx", "Sheet1", 10, 3, 1, 1,
                rawValue2Block: "စီပွားရေး",
                rawFormulaBlock: "စီပွားရေး",
                blockHasFormula: false);
            var singleHit = Assert.Single(scalarHits);
            Assert.Equal("Sheet1!C10", singleHit.DisplayLocation);
            Assert.Equal("စီးပွားရေး", singleHit.Issue.Suggestions[0].Replacement);

            // 5. Test Spreadsheet Toolkit Transforms (Zawgyi -> Pyidaungsu & Smart Numerals) + Undo Manager
            var fontOps = SpreadsheetProofingHelper.TransformBlockLegacyFontsToPyidaungsu(
                "MinistryBudget.xlsx", "စာရင်း(၁)", 2, 2, 4, 3, val2, fml, null);
            var c5FontOp = Assert.Single(fontOps.Where(o => o.Row == 5 && o.Column == 3));
            Assert.Equal("မြန်မာစာ", c5FontOp.UpdatedText);
            Assert.Equal("Pyidaungsu", c5FontOp.TargetFontName);
            Assert.DoesNotContain(fontOps, o => o.Row == 3 && o.Column == 2); // Formula B3 untouched

            var numOps = SpreadsheetProofingHelper.TransformBlockNumerals(
                "MinistryBudget.xlsx", "စာရင်း(၁)", 2, 2, 4, 3, val2, fml, null, toMyanmarDigits: true);
            var d5NumOp = Assert.Single(numOps); // Only D5 modified; numeric C2, formula D2, and English SKU C3 untouched!
            Assert.Equal(5, d5NumOp.Row);
            Assert.Equal(4, d5NumOp.Column);
            Assert.Equal("၂၀၂၆ ခုနှစ် အစီရင်ခံစာ အမှတ် ၁၅ (ISO-9001)", d5NumOp.UpdatedText);

            var undoMgr = new SpreadsheetUndoManager(maxBatches: 10);
            undoMgr.PushOperations("Numeral Convert", numOps);
            Assert.True(undoMgr.CanUndo);
            Assert.Equal(1, undoMgr.UndoCount);
            var popped = undoMgr.PopUndoBatch();
            Assert.NotNull(popped);
            Assert.Equal("၂၀၂၆ ခုနှစ် အစီရင်ခံစာ အမှတ် 15 (ISO-9001)", popped!.Entries[0].PreviousText);
            Assert.False(undoMgr.CanUndo);
        }

        [Fact]
        public void V41_AuthorAttribution_KyaukseUniversity_GeologyLexicon_AndAssemblyMetadata_Verification()
        {
            // 1. Verify Assembly Metadata & Copyright on Core DLL carries Htet Yar Zar & Kyaukse University
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(MyanmarProofingEngine).Assembly.Location);
            Assert.Contains("Htet Yar Zar", fvi.LegalCopyright ?? string.Empty);
            Assert.Contains("Kyaukse University", fvi.LegalCopyright ?? string.Empty);
            Assert.Contains("Htet Yar Zar", fvi.CompanyName ?? string.Empty);

            // 2. Verify repo-root legal, citation & README files preserve author, father dedication & collaborator credits
            string noticeText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "NOTICE"));
            Assert.Contains("Htet Yar Zar", noticeText);
            Assert.Contains("ထက်ရာဇာ", noticeText);
            Assert.Contains("M.Sc. Part II Candidate in Geology", noticeText);
            Assert.Contains("Kyaukse University", noticeText);
            Assert.Contains("Kyaukse, Mandalay Region, Myanmar", noticeText);
            Assert.Contains("U Moe Kyaw Thu", noticeText);
            Assert.Contains("ဦးမိုးကျော်သူ", noticeText);
            Assert.Contains("Hein Htet Zaw", noticeText);
            Assert.Contains("ဟိန်းထက်ဇော်", noticeText);
            Assert.Contains("Thu Reain Khant", noticeText);
            Assert.Contains("သူရိန်ခန့်", noticeText);

            string citationText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "CITATION.cff"));
            Assert.Contains("Htet Yar Zar", citationText);
            Assert.Contains("Kyaukse University", citationText);
            Assert.Contains("U Moe Kyaw Thu", citationText);
            Assert.Contains("Hein Htet Zaw", citationText);
            Assert.Contains("Thu Reain Khant", citationText);

            string readmeText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "README.md"));
            Assert.Contains("U Moe Kyaw Thu (ဦးမိုးကျော်သူ)", readmeText);
            Assert.Contains("Hein Htet Zaw (ဟိန်းထက်ဇော်)", readmeText);
            Assert.Contains("Thu Reain Khant (သူရိန်ခန့်)", readmeText);

            // 3. Verify Encrypted DB & Proofing Engine include Kyaukse University + Geology Lexicon + Zero False Positives on Creator/Family/Collaborator Names
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            string namesPassage =
                "ဤမြန်မာစာ စစ်ဆေးရေးစနစ်ကို ဖခင် ဦးမိုးကျော်သူ၏ မူလစိတ်ကူးလမ်းညွှန်မှုဖြင့် ထက်ရာဇာက ဖန်တီးပြုစုခဲ့ပြီး " +
                "ဟိန်းထက်ဇော်နှင့် သူရိန်ခန့်တို့က ပူးပေါင်းပါဝင်ဆောင်ရွက်ခဲ့ကြသည်။";
            var nameIssues = engine.CheckText(namesPassage);
            Assert.True(nameIssues.Count == 0,
                $"Expected 0 false positives on Father & Collaborator Burmese names, got: [{string.Join("; ", nameIssues.Select(i => $"{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}]");

            string geologyPassage =
                "မန္တလေးတိုင်းဒေသကြီး ကျောက်ဆည်မြို့ရှိ ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာနတွင် ကျောက်ပညာ၊ တွင်းထွက်ဗေဒ၊ အလွှာဗေဒ၊ " +
                "နေထားဘူမိဗေဒနှင့် ကျောက်ဖြစ်ရုပ်ကြွင်းဗေဒ ဘာသာရပ်များကို လေ့လာသင်ကြားလျက်ရှိသည်။ " +
                "မီးသင့်ကျောက်၊ အနည်ကျကျောက်နှင့် အသွင်ပြောင်းကျောက်တို့အပြင် စစ်ကိုင်းပြတ်ရွေ့ကြောင်းနှင့် " +
                "မိုးကုတ်အသွင်ပြောင်းရပ်ဝန်းတစ်လျှောက် ကွင်းဆင်းဘူမိဗေဒ သုတေသနပြုလုပ်ကြသည်။";

            var issues = engine.CheckText(geologyPassage);
            Assert.True(issues.Count == 0,
                $"Expected 0 false positives on Kyaukse University Geology passage, got: [{string.Join("; ", issues.Select(i => $"{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}]");

            var kuDict = engine.SearchDictionary("ကျောက်ဆည်တက္ကသိုလ်", 10);
            Assert.Contains(kuDict, d => d.Word == "ကျောက်ဆည်တက္ကသိုလ်" && (d.UsageNote ?? "").Contains("Htet Yar Zar"));

            var kuEncyc = engine.SearchReferenceCorpus("ကျောက်ဆည်တက္ကသိုလ်", includeProverbs: false, includeEncyclopedia: true, maxResults: 10);
            Assert.Contains(kuEncyc, e => e.Word.Contains("ကျောက်ဆည်တက္ကသိုလ်") && (e.UsageNote ?? "").Contains("Htet Yar Zar"));

            // 4. Verify Printable Audit Certificate carries Htet Yar Zar & Kyaukse University credit
            var auditReport = DocumentQualityAnalyzer.Analyze(geologyPassage, issues, engine);
            string certText = DocumentQualityAnalyzer.BuildAuditReportDocumentText("Geology_Thesis_Draft.docx", auditReport);
            Assert.Contains("ထက်ရာဇာ", certText);
            Assert.Contains("Htet Yar Zar", certText);
            Assert.Contains("Kyaukse University", certText);
        }

        [Fact]
        public void V42_FixAllPreviewChecklist_SafePrecheckAndApprovedBatch()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Deterministic 100% rules MUST be pre-checked (☑ IsHighCertaintyPrecheck == true)
            var piiPii = Assert.Single(engine.CheckText("ပီးပီ"));
            Assert.True(piiPii.IsHighCertaintyPrecheck);
            Assert.Equal("ပြီးပြီ", piiPii.Suggestions[0].Replacement);

            var lookalike = Assert.Single(engine.CheckText("၀န်ကြီးဌာန"));
            Assert.True(lookalike.IsHighCertaintyPrecheck);

            var styleRule = Assert.Single(engine.CheckText("ဆောင်ရွက်ပါတယ်။"));
            Assert.True(styleRule.IsHighCertaintyPrecheck);

            // 2. Unknown OOV syllable / rare name guess & Amber Context Warning MUST default to UNCHECKED (☐ IsHighCertaintyPrecheck == false)
            var oovGuess = engine.CheckText("ဇွဲဖလန်း").FirstOrDefault();
            Assert.NotNull(oovGuess);
            Assert.False(oovGuess!.IsHighCertaintyPrecheck);

            var amberCtx = engine.CheckText("ဥပဒေကို ချိုးဖဲ့သည်။").FirstOrDefault(i => i.IsContextualWarning);
            Assert.NotNull(amberCtx);
            Assert.False(amberCtx!.IsHighCertaintyPrecheck);

            // 3. BuildApprovedReplacementsByCell applies ONLY user-checked (☑) items while leaving unchecked (☐) items untouched!
            object[,] val2 = new object[2, 1];
            object[,] fml = new object[2, 1];
            val2[0, 0] = "ပီးပီ နှင့် ဇွဲဖလန်း"; fml[0, 0] = val2[0, 0];
            val2[1, 0] = "၀န်ကြီးဌာန"; fml[1, 0] = val2[1, 0];

            var cellIssues = SpreadsheetProofingHelper.ScanBlockValues(
                engine, "Book1.xlsx", "Sheet1", 1, 1, 2, 1, val2, fml, blockHasFormula: false);

            // Simulate default Preview Checklist state (☑ IsHighCertaintyPrecheck items only; ☐ OOV name 'ဖလန်း' stays unchecked!)
            var approved = cellIssues
                .Where(c => c.Issue.IsHighCertaintyPrecheck && c.Issue.Suggestions.Count > 0)
                .ToDictionary(c => c, c => c.Issue.Suggestions[0].Replacement);

            var ops = SpreadsheetProofingHelper.BuildApprovedReplacementsByCell(cellIssues, approved);
            Assert.Equal(2, ops.Count);

            var a1Op = Assert.Single(ops.Where(o => o.Row == 1 && o.Column == 1));
            Assert.Equal("ပြီးပြီ နှင့် ဇွဲဖလန်း", a1Op.UpdatedText); // 'ပီးပီ -> ပြီးပြီ' fixed, while OOV name 'ဇွဲဖလန်း' was NOT ruined!

            var a2Op = Assert.Single(ops.Where(o => o.Row == 2 && o.Column == 1));
            Assert.Equal("ဝန်ကြီးဌာန", a2Op.UpdatedText);
        }

        [Fact]
        public void V43_SingleCharLatencyAndFullDatabaseIntegrity()
        {
            var dbFile = new System.IO.FileInfo(TestPaths.EncryptedDbPath);
            Assert.True(dbFile.Exists && dbFile.Length >= 10_000_000,
                $"Expected full tracked mlc_thatpon.db (>= 10 MB), got {(dbFile.Exists ? dbFile.Length : 0)} bytes.");

            var repo = new EncryptedDictionaryRepository(dbFile.FullName);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            Assert.True(trie.WordCount >= 85_000, $"Expected >= 85,000 headwords, got {trie.WordCount} (must never fall back to 106 seed words!).");

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // Simulate background startup 1-sentence JIT warmup (pre-JITs both valid & typo suggestion paths without eager PrewarmSearchIndexes!)
            _ = engine.CheckText("မြန်မာစာ ပီးပီ။");

            // Benchmark single-character & incomplete-cluster keystrokes ('ေ', 'ြ', 'ျ', 'ါ', 'က', 'မ', 'ပ', 'ပီ', 'ပီး')
            string[] liveKeystrokes = { "\u1031", "\u103C", "\u103B", "\u102B", "က", "မ", "ပ", "ပီ", "ပီး" };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (string stroke in liveKeystrokes)
            {
                _ = engine.CheckText(stroke);
            }
            sw.Stop();

            Assert.True(sw.Elapsed.TotalMilliseconds < 80.0,
                $"Single-character keystroke batch took {sw.Elapsed.TotalMilliseconds:F2} ms (expected < 80 ms total across 9 keystrokes).");

            // Lazy dictionary search still builds on demand and finds Kyaukse University Geology terms accurately
            var geoHits = trie.SearchDictionary("ဘူမိဗေဒ", maxResults: 10);
            Assert.NotEmpty(geoHits);
        }

        [Fact]
        public void V44_LowMemoryStreamingAndLazyReferenceEquivalence()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());

            // 1. Verify exact corpus counts BEFORE lazy reference load
            int initialWordCount = trie.WordCount;
            int initialOfficialCount = trie.OfficialThatponCount;
            int initialRefCount = trie.ReferenceCount;
            int initialEncycCount = trie.EncyclopediaCount;
            int initialBigramCount = trie.BigramCount;

            Assert.True(initialWordCount >= 89_500, $"Expected >= 89,500 headwords, got {initialWordCount}.");
            Assert.True(initialOfficialCount >= 24_200, $"Expected >= 24,200 official Thatpon words, got {initialOfficialCount}.");
            Assert.True(initialRefCount >= 26_900, $"Expected >= 26,900 reference entries, got {initialRefCount}.");
            Assert.True(initialEncycCount >= 25_000, $"Expected >= 25,000 encyclopedia entries, got {initialEncycCount}.");
            Assert.True(initialBigramCount >= 85_000, $"Expected >= 85,000 word bigrams, got {initialBigramCount}.");

            // 2. Verify lazy Encyclopedia & Proverb hydration preserves exact counts and returns full articles
            var featured = trie.GetFeaturedKnowledgeEntries(8);
            Assert.NotEmpty(featured);
            Assert.Contains(featured, e => e.IsEncyclopediaEntry && !string.IsNullOrEmpty(e.KnowledgeSummary));

            var encycHits = trie.SearchReferenceCorpus("မြန်မာ", includeProverbs: false, includeEncyclopedia: true, maxResults: 10);
            Assert.NotEmpty(encycHits);

            var proverbHits = trie.SearchReferenceCorpus("ရေ", includeProverbs: true, includeEncyclopedia: false, maxResults: 10);
            Assert.NotEmpty(proverbHits);

            Assert.Equal(initialRefCount, trie.ReferenceCount);
            Assert.Equal(initialEncycCount, trie.EncyclopediaCount);
            Assert.Equal(initialWordCount, trie.WordCount);

            // 3. Verify every headword in the Trie resolves cleanly via exact, phonetic, and strict homophone lookups
            int verifiedEntries = 0;
            foreach (var entry in trie.GetAllEntries())
            {
                Assert.True(trie.ContainsExactWord(entry.Word));
                Assert.False(string.IsNullOrEmpty(entry.PhoneticKey));
                Assert.True(entry.SyllableCount >= 1);
                verifiedEntries++;
            }
            Assert.True(verifiedEntries >= 90_000);
        }

        [Fact]
        public void V45_CpuThroughputAndZeroLinguisticRegressionVerification()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var sugg = new SuggestionEngine(trie);

            // 1. Verify zero-Regex SyllableBreaker equivalence (including Kinzi, Pali stacks, LaKaung, zero-lookalike, numbers, ASCII)
            string[] trickyTokenizerInputs =
            {
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် င်္က သင်္ဘော ၎င်း ၄င်း တာ၀န် ၁၀၀ ၂၀၂၆ ISO-9001!",
                "ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာနတွင် သုတေသန စာတမ်းများ ရေးသားပြုစုလျက် ရှိပါသည်။",
                "၁၀ာ ၀ါ ဥက္ကဋ္ဌ ပုဂ္ဂိုလ် ဣန္ဒြေ ဿ ၌ ၍ ၎င်းတို့၏ အချက်အလက်များ။"
            };
            foreach (string sample in trickyTokenizerInputs)
            {
                var toks = MyanmarSyllableBreaker.Tokenize(sample);
                int expectedSyls = toks.Count(t => t.Kind == TokenKind.MyanmarSyllable);
                Assert.Equal(expectedSyls, MyanmarSyllableBreaker.CountMyanmarSyllables(sample));
                Assert.Equal(sample, string.Concat(toks.Select(t => t.Text)));
            }

            // 2. Verify zero linguistic regression on uncached GetSuggestions across colloquial, Pali, medial-drop, nasal-voicing & compositional typos
            (string Wrong, string ExpectedTop)[] suggestionPairs =
            {
                ("ပီးပီ", "ပြီးပြီ"),
                ("ဥက္ကဌ", "ဥက္ကဋ္ဌ"),
                ("ပော", "ပြော"),
                ("တဂါး", "တံခါး"),
                ("စာလုံးပေါင်သတ်ပုံ", "စာလုံးပေါင်းသတ်ပုံ")
            };
            foreach (var (wrong, expected) in suggestionPairs)
            {
                sugg.ClearCache();
                var cands = sugg.GetSuggestions(wrong, 5);
                Assert.NotEmpty(cands);
                Assert.Contains(cands, c => c.Replacement == expected);
                Assert.False(string.IsNullOrWhiteSpace(cands[0].SyllableBreakdown));
            }
            Assert.Equal("ပြော", sugg.GetSuggestions("ပော", 5)[0].Replacement);
            Assert.Equal("ပြီးပြီ", sugg.GetSuggestions("ပီးပီ", 5)[0].Replacement);
            Assert.Equal("ဥက္ကဋ္ဌ", sugg.GetSuggestions("ဥက္ကဌ", 5)[0].Replacement);
            Assert.Equal("တံခါး", sugg.GetSuggestions("တဂါး", 5)[0].Replacement);

            // 3. Verify uncached CheckText accuracy on lookalikes, spelling, grammar, Tier-1 homophones & Tier-2 Amber warnings
            var checkHits = engine.CheckText("တာ၀န်ရှိသူများက ကြိုးကို တင်းကျပ်စွာ ချဉ်ပြီး ဥပဒေကို ချိုးဖဲ့ပါတယ်။");
            Assert.Contains(checkHits, i => i.OriginalText.Contains("တာ၀န်") && i.Suggestions.Any(s => s.Replacement.Contains("တာဝန်")));
            Assert.Contains(checkHits, i => i.OriginalText == "ချဉ်" && i.IsAutoFixSafe && i.Suggestions[0].Replacement == "ချည်");
            Assert.Contains(checkHits, i => i.OriginalText == "ချိုးဖဲ့" && i.IsContextualWarning && !i.IsAutoFixSafe && i.Suggestions[0].Replacement == "ချိုးဖောက်");
            Assert.Contains(checkHits, i => i.IssueType == ProofingIssueType.OfficialStyleGrammarError);

            // 4. Verify uncached CheckText, GetSuggestions & DocumentQualityAnalyzer throughput
            string basePara = "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် ဝန်ကြီးဌာန အစည်းအဝေးတွင် စီမံကိန်းလုပ်ငန်းများ ဆောင်ရွက်ပြီးပြီ ဖြစ်ပါတယ်။ ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာနတွင် သုတေသန စာတမ်းများ ရေးသားပြုစုလျက် ရှိပါသည်။";
            _ = engine.CheckText(basePara + " warmup");

            double bestBatchMs = double.MaxValue;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int offset = attempt * 100;
                var swAttempt = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 50; i++)
                {
                    var res = engine.CheckText(basePara + " " + (offset + i));
                    Assert.NotEmpty(res);
                }
                swAttempt.Stop();
                if (swAttempt.Elapsed.TotalMilliseconds < bestBatchMs)
                    bestBatchMs = swAttempt.Elapsed.TotalMilliseconds;
                if (bestBatchMs < 1500.0)
                    break;
            }
            Assert.True(bestBatchMs < 1500.0,
                $"50 unique uncached paragraphs took {bestBatchMs:F2} ms (expected < 1500 ms under full multi-core Parallel.ForEach test contention).");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int r = 0; r < 5; r++)
            {
                sugg.ClearCache();
                foreach (var (wrong, _) in suggestionPairs)
                {
                    var res = sugg.GetSuggestions(wrong, 5);
                    Assert.NotEmpty(res);
                }
            }
            sw.Stop();
            Assert.True(sw.Elapsed.TotalMilliseconds < 650.0,
                $"25 uncached GetSuggestions calls took {sw.Elapsed.TotalMilliseconds:F2} ms (expected < 650 ms under parallel test contention).");

            string doc40 = string.Join("\n", Enumerable.Repeat(basePara, 40));
            var sampleIssues = engine.CheckText(basePara);
            _ = DocumentQualityAnalyzer.Analyze(doc40, sampleIssues, engine);
            sw.Restart();
            for (int i = 0; i < 20; i++)
            {
                var rep = DocumentQualityAnalyzer.Analyze(doc40, sampleIssues, engine);
                Assert.Equal(40, rep.ParagraphCount);
                Assert.True(rep.MyanmarSyllableCount > 800);
            }
            sw.Stop();
            Assert.True(sw.Elapsed.TotalMilliseconds < 250.0,
                $"20x 40-paragraph DocumentQualityAnalyzer.Analyze took {sw.Elapsed.TotalMilliseconds:F2} ms (expected < 250 ms under parallel test contention).");
        }

        [Fact]
        public void V46_CleanMachineInstallerAndSingleDbDiscoveryVerification()
        {
            string repoRoot = TestPaths.RepoRoot;
            string sourceDb = Path.Combine(repoRoot, "data", "mlc_thatpon.db");
            string easySetupPs1 = Path.Combine(repoRoot, "installer", "Easy-Setup.ps1");
            string registerDevPs1 = Path.Combine(repoRoot, "installer", "Register-DevAddIn.ps1");

            Assert.True(File.Exists(sourceDb), "Tracked data/mlc_thatpon.db must exist for clean git clones.");
            long dbLength = new FileInfo(sourceDb).Length;
            Assert.True(dbLength >= 10_000_000, $"Expected full mlc_thatpon.db >= 10 MB, actual: {dbLength} bytes.");

            // 1. Verify UTF-8 BOM (EF BB BF) on PowerShell scripts containing Myanmar Unicode text (required for Windows PowerShell 5.1)
            foreach (string ps1 in new[] { easySetupPs1, registerDevPs1 })
            {
                byte[] header = File.ReadAllBytes(ps1);
                Assert.True(header.Length >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF,
                    $"Script '{Path.GetFileName(ps1)}' must be encoded as UTF-8 with BOM for Windows PowerShell 5.1 compatibility.");
            }

            // Locate compiled Core/WordAddIn/ExcelAddIn DLLs to populate simulated clean-PC prebuilt & standalone directories
            string prebuiltDir = Path.Combine(repoRoot, "installer", "prebuilt");
            string coreDll = File.Exists(Path.Combine(prebuiltDir, "MyanmarSpellChecker.Core.dll"))
                ? Path.Combine(prebuiltDir, "MyanmarSpellChecker.Core.dll")
                : typeof(MyanmarProofingEngine).Assembly.Location;
            string wordDll = File.Exists(Path.Combine(prebuiltDir, "MyanmarSpellChecker.WordAddIn.dll"))
                ? Path.Combine(prebuiltDir, "MyanmarSpellChecker.WordAddIn.dll")
                : coreDll;
            string excelDll = File.Exists(Path.Combine(prebuiltDir, "MyanmarSpellChecker.ExcelAddIn.dll"))
                ? Path.Combine(prebuiltDir, "MyanmarSpellChecker.ExcelAddIn.dll")
                : coreDll;

            // 2. Simulate a clean second PC (fresh git clone / Download ZIP in a folder with spaces AND [1] wildcard brackets,
            //    with ONLY ONE database file 'data\mlc_thatpon.db' and NO 'artifacts\' or 'src\*\bin\' directories!)
            string simRoot = Path.Combine(
                Path.GetTempPath(),
                "MyanmarSpellChecker Clean PC [1]_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            string simInstaller = Path.Combine(simRoot, "installer");
            string simPrebuilt = Path.Combine(simInstaller, "prebuilt");
            string simData = Path.Combine(simRoot, "data");
            string simStandalone = Path.Combine(simRoot, "standalone_extracted [1]");

            try
            {
                Directory.CreateDirectory(simPrebuilt);
                Directory.CreateDirectory(simData);
                Directory.CreateDirectory(simStandalone);

                File.Copy(easySetupPs1, Path.Combine(simInstaller, "Easy-Setup.ps1"), overwrite: true);
                File.Copy(coreDll, Path.Combine(simPrebuilt, "MyanmarSpellChecker.Core.dll"), overwrite: true);
                File.Copy(wordDll, Path.Combine(simPrebuilt, "MyanmarSpellChecker.WordAddIn.dll"), overwrite: true);
                File.Copy(excelDll, Path.Combine(simPrebuilt, "MyanmarSpellChecker.ExcelAddIn.dll"), overwrite: true);
                File.Copy(sourceDb, Path.Combine(simData, "mlc_thatpon.db"), overwrite: true);

                string psExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                if (!File.Exists(psExe))
                    psExe = "powershell.exe";

                // 2a. Verify Repo / Clean-Clone Mode (single data\mlc_thatpon.db + installer\prebuilt\*)
                var psiRepo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = psExe,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(simInstaller, "Easy-Setup.ps1")}\" -ValidateOnly -Silent",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = System.Diagnostics.Process.Start(psiRepo)!)
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(20_000);
                    Assert.True(proc.ExitCode == 0, $"Clean-clone Easy-Setup -ValidateOnly failed (exit {proc.ExitCode}): {stdout} {stderr}");
                    Assert.Contains("VALIDATE_OK|", stdout);
                    Assert.Contains($"DbBytes={dbLength}", stdout);
                }

                // 2b. Verify Standalone Extracted ZIP Mode (Easy-Setup.ps1 + DLLs + mlc_thatpon.db in flat directory)
                File.Copy(easySetupPs1, Path.Combine(simStandalone, "Easy-Setup.ps1"), overwrite: true);
                File.Copy(coreDll, Path.Combine(simStandalone, "MyanmarSpellChecker.Core.dll"), overwrite: true);
                File.Copy(wordDll, Path.Combine(simStandalone, "MyanmarSpellChecker.WordAddIn.dll"), overwrite: true);
                File.Copy(excelDll, Path.Combine(simStandalone, "MyanmarSpellChecker.ExcelAddIn.dll"), overwrite: true);
                File.Copy(sourceDb, Path.Combine(simStandalone, "mlc_thatpon.db"), overwrite: true);

                var psiZip = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = psExe,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(simStandalone, "Easy-Setup.ps1")}\" -ValidateOnly -Silent",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = System.Diagnostics.Process.Start(psiZip)!)
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(20_000);
                    Assert.True(proc.ExitCode == 0, $"Standalone Easy-Setup -ValidateOnly failed (exit {proc.ExitCode}): {stdout} {stderr}");
                    Assert.Contains("VALIDATE_OK|", stdout);
                    Assert.Contains($"DbBytes={dbLength}", stdout);
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(simRoot))
                        Directory.Delete(simRoot, recursive: true);
                }
                catch { }
            }
        }

        [Fact]
        public void V47_LargeDocumentAndMassiveSpreadsheetStressVerification()
        {
            string dbPath = Path.Combine(TestPaths.RepoRoot, "data", "mlc_thatpon.db");
            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            trie.PrewarmSearchIndexes();
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // Warmup JIT once
            _ = engine.CheckText("ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် ဝန်ကြီးဌာန အစည်းအဝေး မှတ်တမ်း။");

            // 1. Large 150-Page Word Book / Ministry Report Simulation (3,200 paragraphs, 600 unique variations)
            const int paragraphCount = 3200;
            const int uniqueParagraphVariants = 600;
            var bookParagraphs = new string[paragraphCount];
            for (int i = 0; i < paragraphCount; i++)
            {
                int variant = i % uniqueParagraphVariants;
                bookParagraphs[i] = (variant % 3) switch
                {
                    0 => $"အပိုဒ် ({variant}) — ပြည်ထောင်စုဝန်ကြီးဌာနနှင့် ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာနတို့ ပူးပေါင်း၍ သုတေသနစီမံကိန်းများ စနစ်တကျ အကောင်အထည်ဖော် ဆောင်ရွက်လျက်ရှိပါသည်။",
                    1 => $"အပိုဒ် ({variant}) — တာ၀န်ရှိသူများက လုပ်ငန်းများ ဆောင်ရွတ်ပီးပီ ဖြစ်ကြောင်း ဥက္ကဌထံ တင်ပြပါတယ်။",
                    _ => $"အပိုဒ် ({variant}) — ကြိုးကို တင်းကျပ်စွာ ချဉ်ပြီး စည်းကမ်းချက်များကို မချိုးဖဲ့ရန် အသိပေးနှိုးဆော်အပ်ပါသည်။"
                };
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var swBook = System.Diagnostics.Stopwatch.StartNew();
            var perParaIssues = new IReadOnlyList<ProofingIssue>[paragraphCount];
            System.Threading.Tasks.Parallel.For(
                0,
                paragraphCount,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 6) },
                idx =>
                {
                    perParaIssues[idx] = engine.CheckText(bookParagraphs[idx]);
                });

            var flatBookIssues = new List<ProofingIssue>(4096);
            for (int i = 0; i < paragraphCount; i++)
            {
                if (perParaIssues[i].Count > 0)
                    flatBookIssues.AddRange(perParaIssues[i]);
            }

            string fullBookText = string.Join("\n", bookParagraphs);
            DocumentQualityReport quality = DocumentQualityAnalyzer.Analyze(fullBookText, flatBookIssues, engine);
            swBook.Stop();

            Assert.True(flatBookIssues.Count >= 2000, $"Expected >= 2000 issues across 3,200 paragraphs, got {flatBookIssues.Count}.");
            Assert.Equal(paragraphCount, quality.ParagraphCount);
            Assert.True(swBook.Elapsed.TotalMilliseconds < 4000.0,
                $"3,200-paragraph (~150-page) parallel book scan + quality audit took {swBook.Elapsed.TotalMilliseconds:F1} ms (expected < 4000 ms).");

            // Incremental edit on a 3,200-paragraph book (verifies 8,192-entry paragraph cache prevents cache thrashing!)
            bookParagraphs[paragraphCount - 1] = "နောက်ဆုံးပြင်ဆင်ချက် — လုပ်ငန်းအားလုံး ဆောင်ရွတ်ပီးပီ ဖြစ်ပါသည်။";
            var swIncremental = System.Diagnostics.Stopwatch.StartNew();
            int incrementalTotal = 0;
            for (int i = 0; i < paragraphCount; i++)
            {
                incrementalTotal += engine.CheckText(bookParagraphs[i]).Count;
            }
            swIncremental.Stop();
            Assert.True(incrementalTotal >= 2000);
            Assert.True(swIncremental.Elapsed.TotalMilliseconds < 120.0,
                $"Incremental re-scan of 3,200 paragraphs (3,199 warm cache hits + 1 dirty paragraph) took {swIncremental.Elapsed.TotalMilliseconds:F2} ms (expected < 120 ms).");

            // 2. Massive 50,000-Cell Excel Sheet Stress Test (10,000 rows x 5 cols: IDs, repeated labels, 1,500 unique remarks, numbers, formulas)
            const int rows = 10_000;
            const int cols = 5;
            var val2 = new object?[rows, cols];
            var fml = new object?[rows, cols];

            string[] townships =
            {
                "ကျောက်ဆည်မြို့နယ်", "မန္တလေးမြို့", "နေပြည်တော်", "ရန်ကုန်တိုင်းဒေသကြီး",
                "ပြင်ဦးလွင်မြို့နယ်", "မိတ္ထီလာမြို့နယ်", "ပုသိမ်မြို့နယ်", "တောင်ကြီးမြို့နယ်"
            };

            for (int r = 0; r < rows; r++)
            {
                val2[r, 0] = (double)(r + 1);
                val2[r, 1] = townships[r % townships.Length];

                int remarkVariant = r % 1500;
                val2[r, 2] = (remarkVariant % 2 == 0)
                    ? $"မှတ်ချက် #{remarkVariant} — စစ်ဆေးရေးလုပ်ငန်းများ အောင်မြင်စွာ ဆောင်ရွက်ပြီးစီးပါသည်။"
                    : $"မှတ်ချက် #{remarkVariant} — တာ၀န်ခံမှ စစ်ဆေးပီးပီ ဆောင်ရွတ်ဆဲ။";

                val2[r, 3] = (double)((r + 1) * 12500);

                // Column 5 (index 4): Formula cell whose cached string result intentionally contains a typo ("ပီးပီ")
                // to prove 100% formula protection even under parallel massive-block scanning!
                val2[r, 4] = "ပီးပီ ဆောင်ရွတ်";
                fml[r, 4] = $"=IF(D{r + 1}>0,\"ပီးပီ ဆောင်ရွတ်\",\"\")";
            }

            var swExcel = System.Diagnostics.Stopwatch.StartNew();
            var sharedMemo = new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            var sheetIssues = SpreadsheetProofingHelper.ScanBlockValues(
                engine,
                "MinistryBudget2026.xlsx",
                "မြို့နယ်စာရင်း",
                1,
                1,
                rows,
                cols,
                val2,
                fml,
                null,
                sharedMemo);

            // Build approved replacements across all high-certainty issues
            var approved = new Dictionary<SpreadsheetCellIssue, string>(sheetIssues.Count);
            for (int i = 0; i < sheetIssues.Count; i++)
            {
                var item = sheetIssues[i];
                if (item.Issue.IsHighCertaintyPrecheck && item.Issue.Suggestions.Count > 0)
                {
                    approved[item] = item.Issue.Suggestions[0].Replacement;
                }
            }

            var batchWriteOps = SpreadsheetProofingHelper.BuildApprovedReplacementsByCell(
                sheetIssues,
                approved,
                anchor => val2[anchor.Row - 1, anchor.Column - 1] as string);
            swExcel.Stop();

            // Formula column (Column == 5) must NEVER be flagged or modified!
            Assert.DoesNotContain(sheetIssues, x => x.Column == 5);
            Assert.DoesNotContain(batchWriteOps, op => op.Column == 5);
            Assert.True(sheetIssues.Count >= 10_000, $"Expected >= 10,000 issues across 5,000 erroneous rows, got {sheetIssues.Count}.");
            Assert.Equal(5000, batchWriteOps.Count);
            Assert.All(batchWriteOps, op =>
            {
                Assert.Contains("တာဝန်ခံမှ", op.UpdatedText);
                Assert.Contains("ပြီးပြီ", op.UpdatedText);
                Assert.Contains("ဆောင်ရွက်ဆဲ", op.UpdatedText);
            });
            Assert.True(swExcel.Elapsed.TotalMilliseconds < 4000.0,
                $"50,000-cell spreadsheet scan + 5,000-cell multi-issue batch fix planning took {swExcel.Elapsed.TotalMilliseconds:F1} ms (expected < 4000 ms).");

            // 3. Verify instant cancellation responsiveness on huge spreadsheet blocks
            using (var cts = new System.Threading.CancellationTokenSource())
            {
                cts.Cancel();
                var swCancel = System.Diagnostics.Stopwatch.StartNew();
                var cancelledHits = SpreadsheetProofingHelper.ScanBlockValues(
                    engine,
                    "Cancelled.xlsx",
                    "Sheet1",
                    1,
                    1,
                    rows,
                    cols,
                    val2,
                    fml,
                    null,
                    null,
                    cts.Token);
                swCancel.Stop();
                Assert.Empty(cancelledHits);
                Assert.True(swCancel.Elapsed.TotalMilliseconds < 25.0);
            }
        }

        [Fact]
        public void V48_PotatoPcEcoModeAndStreamingResourceGuardVerification()
        {
            EcoModePreference savedPref = EcoPerformanceGovernor.CurrentPreference;
            string tempDir = Path.Combine(Path.GetTempPath(), "MyanmarSpellChecker_V48_" + Guid.NewGuid().ToString("N"));
            try
            {
                // 1. Simulated Potato PC (Dual-Core Celeron, 4 GB RAM, 32-bit Office)
                var potatoHw = new HardwareResourceProfile
                {
                    LogicalCores = 2,
                    TotalPhysicalRamMb = 4096,
                    AvailablePhysicalRamMb = 900,
                    Is32BitOfficeProcess = true,
                    IsLowSpecHardware = true
                };

                var potatoAuto = EcoPerformanceGovernor.ComputeBudgets(
                    isLargeFile: false,
                    overridePreference: EcoModePreference.Auto,
                    overrideHardware: potatoHw);

                Assert.True(potatoAuto.IsEcoActive);
                Assert.True(potatoAuto.ActivatedByHardware);
                Assert.Equal(1, potatoAuto.MaxBackgroundWorkers);
                Assert.True(potatoAuto.UseLowPriorityThreads);
                Assert.Equal(20, potatoAuto.MicroYieldEveryItems);
                Assert.Equal(80, potatoAuto.WordVisualUnderlineCap);
                Assert.Equal(25, potatoAuto.ExcelVisualUnderlineCap);
                Assert.Equal(2048, potatoAuto.MaxParagraphCacheEntries);
                Assert.Equal(8192, potatoAuto.MaxSpreadsheetCellCacheEntries);
                Assert.Equal(2500, potatoAuto.MaxIssuesPerFullScan);

                // 2. Simulated High-Spec Workstation (16 Cores, 32 GB RAM, 64-bit Office)
                var desktopHw = new HardwareResourceProfile
                {
                    LogicalCores = 16,
                    TotalPhysicalRamMb = 32_768,
                    AvailablePhysicalRamMb = 22_000,
                    Is32BitOfficeProcess = false,
                    IsLowSpecHardware = false
                };

                var desktopSmall = EcoPerformanceGovernor.ComputeBudgets(
                    isLargeFile: false,
                    overridePreference: EcoModePreference.Auto,
                    overrideHardware: desktopHw);

                Assert.False(desktopSmall.IsEcoActive);
                Assert.Equal(4, desktopSmall.MaxBackgroundWorkers);
                Assert.Equal(450, desktopSmall.WordVisualUnderlineCap);
                Assert.Equal(120, desktopSmall.ExcelVisualUnderlineCap);
                Assert.Equal(8192, desktopSmall.MaxParagraphCacheEntries);
                Assert.Equal(32_768, desktopSmall.MaxSpreadsheetCellCacheEntries);

                // High-spec machine opening a huge 600-paragraph Word doc or 50,000-cell Excel sheet auto-activates Eco Mode!
                Assert.True(EcoPerformanceGovernor.IsLargeWordDocument(600, 120_000));
                Assert.True(EcoPerformanceGovernor.IsLargeExcelScope(50_000));

                var desktopLarge = EcoPerformanceGovernor.ComputeBudgets(
                    isLargeFile: true,
                    overridePreference: EcoModePreference.Auto,
                    overrideHardware: desktopHw);

                Assert.True(desktopLarge.IsEcoActive);
                Assert.True(desktopLarge.ActivatedByFileSize);
                Assert.Equal(1, desktopLarge.MaxBackgroundWorkers);
                Assert.Equal(80, desktopLarge.WordVisualUnderlineCap);
                Assert.Equal(25, desktopLarge.ExcelVisualUnderlineCap);

                // Manual overrides: AlwaysOn vs Off
                var forcedOn = EcoPerformanceGovernor.ComputeBudgets(false, EcoModePreference.AlwaysOn, desktopHw);
                Assert.True(forcedOn.IsEcoActive);
                Assert.Equal(1, forcedOn.MaxBackgroundWorkers);

                var forcedOff = EcoPerformanceGovernor.ComputeBudgets(true, EcoModePreference.Off, desktopHw);
                Assert.False(forcedOff.IsEcoActive);
                Assert.Equal(4, forcedOff.MaxBackgroundWorkers);

                // 3. Preference cycle + shared Word/Excel persistence round-trip
                Assert.Equal(EcoModePreference.AlwaysOn, EcoPerformanceGovernor.CyclePreference(EcoModePreference.Auto));
                Assert.Equal(EcoModePreference.Off, EcoPerformanceGovernor.CyclePreference(EcoModePreference.AlwaysOn));
                Assert.Equal(EcoModePreference.Auto, EcoPerformanceGovernor.CyclePreference(EcoModePreference.Off));

                string cfgFile = Path.Combine(tempDir, "eco_mode.txt");
                EcoPerformanceGovernor.SavePreference(cfgFile, EcoModePreference.AlwaysOn);
                Assert.Equal(EcoModePreference.AlwaysOn, EcoPerformanceGovernor.LoadSavedPreference(cfgFile));
                EcoPerformanceGovernor.SavePreference(cfgFile, EcoModePreference.Off);
                Assert.Equal(EcoModePreference.Off, EcoPerformanceGovernor.LoadSavedPreference(cfgFile));
                EcoPerformanceGovernor.SavePreference(cfgFile, EcoModePreference.Auto);
                Assert.Equal(EcoModePreference.Auto, EcoPerformanceGovernor.LoadSavedPreference(cfgFile));

                UiLanguage prevLang = UiLocalizer.CurrentLanguage;
                try
                {
                    UiLocalizer.CurrentLanguage = UiLanguage.English;
                    Assert.Contains("Auto✓", EcoPerformanceGovernor.FormatBadgeLabel(EcoModePreference.Auto, true));
                    Assert.Contains("On", EcoPerformanceGovernor.FormatBadgeLabel(EcoModePreference.AlwaysOn, true));
                    Assert.Contains("Off", EcoPerformanceGovernor.FormatBadgeLabel(EcoModePreference.Off, false));
                    Assert.False(string.IsNullOrWhiteSpace(EcoPerformanceGovernor.FormatTooltip(EcoModePreference.Auto, true)));

                    UiLocalizer.CurrentLanguage = UiLanguage.Myanmar;
                    Assert.Contains("ဖွင့်", EcoPerformanceGovernor.FormatBadgeLabel(EcoModePreference.AlwaysOn, true));
                    Assert.Contains("ပိတ်", EcoPerformanceGovernor.FormatBadgeLabel(EcoModePreference.Off, false));
                }
                finally
                {
                    UiLocalizer.CurrentLanguage = prevLang;
                }

                // 4. Zero-allocation < 25 ms fast skip on 5,000 non-Myanmar (English/numeric/code) paragraphs
                string dbPath = Path.Combine(TestPaths.RepoRoot, "data", "mlc_thatpon.db");
                var repo = new EncryptedDictionaryRepository(dbPath);
                MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
                var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

                var swAscii = System.Diagnostics.Stopwatch.StartNew();
                int nonMmIssues = 0;
                for (int i = 0; i < 5000; i++)
                {
                    nonMmIssues += engine.CheckText("FY-2026 Budget Summary Row #10482 | Total USD 1,450,200.00 | https://gov.mm/report").Count;
                }
                swAscii.Stop();
                Assert.Equal(0, nonMmIssues);
                Assert.True(swAscii.Elapsed.TotalMilliseconds < 25.0,
                    $"5,000 English/numeric fast-skip calls took {swAscii.Elapsed.TotalMilliseconds:F2} ms (expected < 25 ms).");

                // 5. Potato PC 2,500-issue safe cap + single-worker gentle priority + TrimMemoryCaches
                const int rows = 4000;
                var val2 = new object?[rows, 2];
                for (int r = 0; r < rows; r++)
                {
                    val2[r, 0] = $"အတန်း #{r + 1} — တာ၀န်ခံမှ စစ်ဆေးပီးပီ ဆောင်ရွတ်ဆဲ။";
                    val2[r, 1] = 100.0 + r;
                }

                List<SpreadsheetCellIssue>? cappedIssues = null;
                EcoPerformanceGovernor.ExecuteWithGentlePriority(useLowPriority: true, () =>
                {
                    cappedIssues = SpreadsheetProofingHelper.ScanBlockValues(
                        engine,
                        "PotatoPcWorkbook.xlsx",
                        "Sheet1",
                        1,
                        1,
                        rows,
                        2,
                        val2,
                        null,
                        false,
                        new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal),
                        default,
                        maxIssuesToCollect: potatoAuto.MaxIssuesPerFullScan,
                        explicitBudgets: potatoAuto);
                });

                Assert.NotNull(cappedIssues);
                Assert.Equal(2500, cappedIssues!.Count);

                // TrimMemoryCaches frees paragraph/segment caches cleanly without breaking subsequent checks
                engine.TrimMemoryCaches();
                var postTrimCheck = engine.CheckText("စစ်ဆေးပီးပီ။");
                Assert.Contains(postTrimCheck, x => x.OriginalText == "ပီးပီ");
            }
            finally
            {
                EcoPerformanceGovernor.CurrentPreference = savedPref;
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }

        [Fact]
        public void V49_DefaultEnglishUiAndLiveLanguageSwitchVerification()
        {
            UiLanguage originalLang = UiLocalizer.CurrentLanguage;
            string tempDir = Path.Combine(Path.GetTempPath(), "MmUiLangTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                // 1. Fresh install / missing config file MUST default to English (UiLanguage.English = 0)
                string missingCfg = Path.Combine(tempDir, "missing_ui_language.txt");
                Assert.Equal(UiLanguage.English, default(UiLanguage));
                Assert.Equal(UiLanguage.English, UiLocalizer.LoadSavedLanguage(missingCfg));

                string emptyCfg = Path.Combine(tempDir, "empty_ui_language.txt");
                File.WriteAllText(emptyCfg, "   ");
                Assert.Equal(UiLanguage.English, UiLocalizer.LoadSavedLanguage(emptyCfg));

                UiLocalizer.CurrentLanguage = UiLanguage.English;
                Assert.True(UiLocalizer.IsEnglish);
                Assert.Equal("Scan Document", UiLocalizer.Pick("Scan Document", "စာတမ်းစစ်မည်"));
                Assert.Equal("A/က EN", UiLocalizer.FormatLanguageBadge());
                Assert.Contains("မြန်မာ", UiLocalizer.FormatLanguageTooltip());

                // 2. Domain UI badges & labels switch cleanly between English and Myanmar
                var sampleIssue = new ProofingIssue
                {
                    OriginalText = "ပီးပီ",
                    IssueType = ProofingIssueType.SpellingError,
                    Suggestions = new List<SuggestionCandidate> { new SuggestionCandidate { Replacement = "ပြီးပြီ", Score = 99.0 } }
                };
                Assert.Equal("✓ High Certainty (Standard Rule)", sampleIssue.CertaintyBadgeText);

                var qualityReport = new DocumentQualityReport
                {
                    OverallScore = 96,
                    GradeCode = "A+",
                    GradeLabel = "A+ စံချိန်မီ အထူးကောင်းမွန်"
                };
                Assert.Equal("A+ Excellent (Publication Ready)", qualityReport.LocalizedGradeLabel);
                Assert.Equal("Noun", DictionaryEntry.FormatLocalizedPosTag("N"));
                Assert.Equal("အာမေဍိတ် (Interjection)", DictionaryEntry.FormatPosTag("INTJ"));

                // 3. Live 1-click toggle to Myanmar + shared Word/Excel persistence round-trip
                UiLanguage toggled = UiLocalizer.ToggleLanguage();
                Assert.Equal(UiLanguage.Myanmar, toggled);
                Assert.False(UiLocalizer.IsEnglish);
                Assert.Equal("စာတမ်းစစ်မည်", UiLocalizer.Pick("Scan Document", "စာတမ်းစစ်မည်"));
                Assert.Equal("A/က မြန်မာ", UiLocalizer.FormatLanguageBadge());
                Assert.Contains("English", UiLocalizer.FormatLanguageTooltip());
                Assert.Equal("✓ သေချာ (စံသတ်ပုံ/သဒ္ဒါ)", sampleIssue.CertaintyBadgeText);
                Assert.Equal("A+ စံချိန်မီ အထူးကောင်းမွန်", qualityReport.LocalizedGradeLabel);
                Assert.Equal("နာမ် (Noun)", DictionaryEntry.FormatLocalizedPosTag("N"));

                string langFile = Path.Combine(tempDir, "ui_language.txt");
                UiLocalizer.SaveLanguage(langFile, UiLanguage.Myanmar);
                Assert.Equal(UiLanguage.Myanmar, UiLocalizer.LoadSavedLanguage(langFile));

                UiLanguage backToEn = UiLocalizer.ToggleLanguage();
                Assert.Equal(UiLanguage.English, backToEn);
                UiLocalizer.SaveLanguage(langFile, backToEn);
                Assert.Equal(UiLanguage.English, UiLocalizer.LoadSavedLanguage(langFile));
            }
            finally
            {
                UiLocalizer.CurrentLanguage = originalLang;
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }

        [Fact]
        public void V50_ExpandedGovernmentLawsGazetteerMinistriesAndContextRulesVerification()
        {
            string dbPath = Path.Combine(TestPaths.RepoRoot, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath), $"Missing encrypted DB at {dbPath}");

            // 1. Database size & expanded 4-pillar government corpus count assertions
            long dbSizeBytes = new FileInfo(dbPath).Length;
            Assert.True(dbSizeBytes >= 10 * 1024 * 1024 && dbSizeBytes <= 23 * 1024 * 1024,
                $"Expected mlc_thatpon.db within 10–23 MB Potato-PC/GitHub budget, got {dbSizeBytes / (1024.0 * 1024.0):F2} MB.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            Assert.True(trie.WordCount >= 94500, $"Expected >= 94,500 unique headwords after 4-pillar expansion, got {trie.WordCount}.");
            Assert.True(trie.RuleCount >= 1500, $"Expected >= 1,500 explicit orthography rules, got {trie.RuleCount}.");
            Assert.True(trie.ContextualRuleCount >= 712, $"Expected >= 712 contextual meaning rules, got {trie.ContextualRuleCount}.");
            Assert.True(trie.BigramCount >= 105000, $"Expected >= 105,000 statutory & corpus word bigrams, got {trie.BigramCount}.");

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 2. Zero false positives across Statutory Laws, MIMU v9.7 Gazetteer, Ministries, UCSB Ranks & Universities
            var cleanGovSentences = new[]
            {
                "မြန်မာ့သတ္တုတွင်းဥပဒေ၊ မြန်မာ့ကျောက်မျက်ရတနာဥပဒေနှင့် ပတ်ဝန်းကျင်ထိန်းသိမ်းရေးဥပဒေတို့ကို ပြဋ္ဌာန်းထားသည်။",
                "နိုင်ငံ့ဝန်ထမ်းဥပဒေနှင့် နိုင်ငံ့ဝန်ထမ်းနည်းဥပဒေများအရ အမိန့်ကြော်ငြာစာကို မြန်မာနိုင်ငံပြန်တမ်းတွင် ထုတ်ပြန်သည်။",
                "မန္တလေးတိုင်းဒေသကြီး ကျောက်ဆည်ခရိုင် ကျောက်ဆည်မြို့ရှိ ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာနတွင် သုတေသနပြုလုပ်သည်။",
                "ရန်ကုန်မြောက်ပိုင်းခရိုင်နှင့် ဝေါလေမြိုင်မြို့တို့တွင် အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာနက စာရင်းကောက်ယူသည်။",
                "သယံဇာတနှင့်သဘာဝပတ်ဝန်းကျင်ထိန်းသိမ်းရေးဝန်ကြီးဌာန ဘူမိဗေဒလေ့လာရေးနှင့်ဓာတ်သတ္တုရှာဖွေရေးဦးစီးဌာန ညွှန်ကြားရေးမှူးချုပ်က စစ်ဆေးသည်။",
                "ပြည်ထောင်စုရာထူးဝန်အဖွဲ့နှင့် ပြည်ထောင်စုစာရင်းစစ်ချုပ်ရုံးတို့သို့ ရသုံးမှန်းခြေငွေစာရင်း တင်ပြသည်။",
                "အိတ်ဖွင့်တင်ဒါခေါ်ယူရာတွင် တင်ဒါစိစစ်ရေးကော်မတီက ကြမ်းခင်းဈေးကို သတ်မှတ်သည်။",
                "ဈေးနှုန်းတိုးမြင့်မှုများ မဖြစ်ပေါ်စေရန် ကြပ်မတ်ဆောင်ရွက်သည်။"
            };

            foreach (string sentence in cleanGovSentences)
            {
                var cleanIssues = formalEngine.CheckText(sentence);
                Assert.True(cleanIssues.Count == 0,
                    $"Expected 0 issues on clean government sentence: '{sentence}', got: {string.Join("; ", cleanIssues.Select(i => $"{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}");
            }

            // 3. Bilingual English-to-Myanmar institutional, university & rank search + metadata round-trip
            string expectedDgse = UnicodeNormalizer.NormalizeCanonicalOrder("ဘူမိဗေဒလေ့လာရေးနှင့်ဓာတ်သတ္တုရှာဖွေရေးဦးစီးဌာန");
            var dgseHits = trie.SearchDictionary("DGSE", 20);
            Assert.Contains(dgseHits, e => e.Word == expectedDgse && !string.IsNullOrEmpty(e.EnglishName));

            string expectedKyaukseUni = UnicodeNormalizer.NormalizeCanonicalOrder("ကျောက်ဆည်တက္ကသိုလ်");
            var kyaukseUniHits = trie.SearchDictionary("Kyaukse University", 20);
            Assert.Contains(kyaukseUniHits, e => e.Word == expectedKyaukseUni && !string.IsNullOrEmpty(e.SpecializedDomain));

            string expectedDgRank = UnicodeNormalizer.NormalizeCanonicalOrder("ညွှန်ကြားရေးမှူးချုပ်");
            var dgRankHits = trie.SearchDictionary("Director General", 25);
            Assert.Contains(dgRankHits, e => e.Word == expectedDgRank);

            string expectedGad = UnicodeNormalizer.NormalizeCanonicalOrder("အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာန");
            var gadHits = trie.SearchDictionary("GAD", 25);
            Assert.Contains(gadHits, e => e.Word == expectedGad);

            // 4. High-precision detection & Top-1 repair of official government orthography & contextual errors
            var govErrorCases = new (string Input, string ExpectedSpan, string ExpectedFix)[]
            {
                ("အမိန့်ကြေညာစာ ထုတ်ပြန်သည်။", "ကြေညာ", "ကြော်ငြာ"),
                ("ရာထူးအဆင့်ကို တိုးမြင့်ပေးသည်။", "တိုးမြင့်", "တိုးမြှင့်"),
                ("ညွန်ကြားရေးမှူးက လက်မှတ်ရေးထိုးသည်။", "ညွန်ကြား", "ညွှန်ကြား"),
                ("ညွှန်ကြားရေးမှူးချူပ်က လက်မှတ်ရေးထိုးသည်။", "ချူပ်", "ချုပ်"),
                ("ရသုံးမှန်းချေငွေစာရင်း တင်ပြသည်။", "မှန်းချေ", "မှန်းခြေ"),
                ("တင်ဒါစီစစ်ရေး ဆောင်ရွက်သည်။", "စီစစ်", "စိစစ်"),
                ("အမိန့်ကို ပယ်ဖြတ်လိုက်သည်။", "ပယ်ဖြတ်", "ပယ်ဖျက်")
            };

                foreach (var (input, expectedSpan, expectedFix) in govErrorCases)
            {
                string normSpan = UnicodeNormalizer.NormalizeCanonicalOrder(expectedSpan);
                string normFix = UnicodeNormalizer.NormalizeCanonicalOrder(expectedFix);
                var issues = formalEngine.CheckText(input);
                Assert.True(issues.Count > 0, $"Expected government error detection for '{input}', but got 0 issues.");
                Assert.Contains(issues, i =>
                {
                    string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText);
                    string topNorm = i.Suggestions.Count > 0
                        ? UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement)
                        : string.Empty;
                    return origNorm.Contains(normSpan) && topNorm.Contains(normFix);
                });
            }
        }

        [Fact]
        public void V51_ZeroCellColorMutationOverlayAndExcelQuickFixVerification()
        {
            string repoRoot = TestPaths.RepoRoot;
            string excelSrcDir = Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn");
            Assert.True(Directory.Exists(excelSrcDir), $"Expected ExcelAddIn directory at {excelSrcDir}");

            // 1. Guarantee zero partial-character Font.Color / Font.Underline mutations anywhere in ExcelAddIn
            var csFiles = Directory.GetFiles(excelSrcDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                            !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .ToList();
            Assert.NotEmpty(csFiles);

            foreach (string file in csFiles)
            {
                string source = File.ReadAllText(file);
                Assert.DoesNotContain("chars.Font.Color =", source, StringComparison.Ordinal);
                Assert.DoesNotContain("chars.Font.Underline =", source, StringComparison.Ordinal);
            }

            // 2. Verify Win32 click-through layered wavy overlay + Word-style on-cursor Quick-Fix Popup + Disconnect cleanup wiring
            Assert.True(File.Exists(Path.Combine(excelSrcDir, "UI", "ExcelWavyUnderlineOverlay.cs")));
            Assert.True(File.Exists(Path.Combine(excelSrcDir, "UI", "ExcelQuickFixPopup.cs")));
            Assert.True(File.Exists(Path.Combine(excelSrcDir, "Interop", "ExcelQuickFixController.cs")));
            Assert.True(File.Exists(Path.Combine(excelSrcDir, "Interop", "ExcelCellGeometryHelper.cs")));

            string excelAddInSource = File.ReadAllText(Path.Combine(excelSrcDir, "ExcelThisAddIn.cs"));
            Assert.Contains("SheetSelectionChange", excelAddInSource, StringComparison.Ordinal);
            Assert.Contains("SheetBeforeRightClick", excelAddInSource, StringComparison.Ordinal);
            Assert.Contains("ExcelQuickFixController", excelAddInSource, StringComparison.Ordinal);
            Assert.Contains("ExcelWavyUnderlineOverlay", excelAddInSource, StringComparison.Ordinal);

            string wordUnderlineSource = File.ReadAllText(
                Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "SafeUnderlineManager.cs"));
            Assert.Contains("StripAllProofingUnderlines(distinctDocs[i], clearTrackingList: true)", wordUnderlineSource, StringComparison.Ordinal);

            // 2b. Verify responsive 2-tier Task Pane brand header + post-show DPI startup pane width in both Word & Excel
            string wordTaskPaneSource = File.ReadAllText(
                Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.cs"));
            string excelTaskPaneSource = File.ReadAllText(
                Path.Combine(excelSrcDir, "UI", "ExcelProofingTaskPaneControl.cs"));
            string wordAddInSource = File.ReadAllText(
                Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "ThisAddIn.cs"));
            Assert.Contains("brandPillsGrid", wordTaskPaneSource, StringComparison.Ordinal);
            Assert.Contains("brandPillsGrid", excelTaskPaneSource, StringComparison.Ordinal);
            Assert.Contains("EnsureComfortableStartupPaneWidth", wordAddInSource, StringComparison.Ordinal);
            Assert.Contains("EnsureComfortableStartupPaneWidth", excelAddInSource, StringComparison.Ordinal);

            // 3. Verify screenshot cell ("စားပီးပီ") detection + multi-issue single-cell Quick-Fix stepping ("တာ၀န်ခံ စားပီးပီ")
            string dbPath = Path.Combine(repoRoot, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath));
            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            var a1Issues = engine.CheckText("စားပီးပီ");
            Assert.NotEmpty(a1Issues);
            Assert.Contains(a1Issues, i =>
                UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder("ပီးပီ") &&
                i.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder("ပြီးပြီ")));

            string multiIssueCell = "တာ၀န်ခံ စားပီးပီ";
            var rawMulti = engine.CheckText(multiIssueCell);
            Assert.True(rawMulti.Count >= 2, $"Expected >= 2 issues in '{multiIssueCell}', got {rawMulti.Count}");

            var cellItems = rawMulti.Select(iss => new SpreadsheetCellIssue
            {
                WorkbookName = "Book1",
                SheetName = "Sheet1",
                Row = 1,
                Column = 1,
                CellSnapshotText = multiIssueCell,
                Issue = iss
            }).ToList();

            string firstRep = cellItems[0].Issue.Suggestions[0].Replacement;
            bool okStep1 = SpreadsheetProofingHelper.ApplySingleIssueReplacement(
                multiIssueCell,
                cellItems[0],
                firstRep,
                cellItems,
                out string afterStep1);
            Assert.True(okStep1);
            Assert.True(cellItems[0].IsResolved);
            Assert.False(cellItems[1].IsResolved);

            string secondRep = cellItems[1].Issue.Suggestions[0].Replacement;
            bool okStep2 = SpreadsheetProofingHelper.ApplySingleIssueReplacement(
                afterStep1,
                cellItems[1],
                secondRep,
                cellItems,
                out string afterStep2);
            Assert.True(okStep2);
            Assert.Equal(
                UnicodeNormalizer.NormalizeCanonicalOrder("တာဝန်ခံ စားပြီးပြီ"),
                UnicodeNormalizer.NormalizeCanonicalOrder(afterStep2));

            // 4. Ensure UTF-8 BOM (0xEF 0xBB 0xBF) is preserved on all Windows PowerShell 5.1 installer scripts
            foreach (string ps1 in Directory.GetFiles(Path.Combine(repoRoot, "installer"), "*.ps1"))
            {
                byte[] bytes = File.ReadAllBytes(ps1);
                Assert.True(
                    bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    $"Installer script {Path.GetFileName(ps1)} is missing UTF-8 BOM required for Windows PowerShell 5.1 Myanmar strings!");
            }
        }

        [Fact]
        public void V52_HighDpiTaskPaneLayout_ShouldAvoidHardcodedHeightsFlowGapsMnemonicsAndSurrogateTofu()
        {
            string repoRoot = TestPaths.RepoRoot;
            UiLanguage origLang = UiLocalizer.CurrentLanguage;
            try
            {
                UiLocalizer.CurrentLanguage = UiLanguage.English;
                Assert.DoesNotContain(UiLocalizer.FormatLanguageBadge(), char.IsSurrogate);

                UiLocalizer.CurrentLanguage = UiLanguage.Myanmar;
                Assert.DoesNotContain(UiLocalizer.FormatLanguageBadge(), char.IsSurrogate);
            }
            finally
            {
                UiLocalizer.CurrentLanguage = origLang;
            }

            // 1. Zero supplementary-plane surrogate characters in Word/Excel Add-in C# sources (prevents GDI Pyidaungsu [▯] Tofu boxes)
            string wordDir = Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn");
            string excelDir = Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn");
            foreach (string dir in new[] { wordDir, excelDir })
            {
                foreach (string csPath in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (csPath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                        csPath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    {
                        continue;
                    }

                    string text = File.ReadAllText(csPath);
                    Assert.False(
                        text.Any(char.IsSurrogate),
                        $"Supplementary-plane surrogate emoji found in {Path.GetFileName(csPath)} — GDI Pyidaungsu renders surrogates as [▯] Tofu!");
                }
            }

            // 2. Deterministic TableLayoutPanel grids + dynamic DPI font-metric heights (zero hardcoded 96-DPI heights & zero FlowLayoutPanel vertical gap)
            string wordPane = File.ReadAllText(Path.Combine(wordDir, "UI", "ProofingTaskPaneControl.cs"));
            string excelPane = File.ReadAllText(Path.Combine(excelDir, "UI", "ExcelProofingTaskPaneControl.cs"));
            foreach ((string name, string src) in new[] { ("ProofingTaskPaneControl.cs", wordPane), ("ExcelProofingTaskPaneControl.cs", excelPane) })
            {
                Assert.DoesNotContain("brandPillsFlow", src, StringComparison.Ordinal);
                Assert.DoesNotContain("dictCatRow = new FlowLayoutPanel", src, StringComparison.Ordinal);
                Assert.DoesNotContain("dictChipFlow = new FlowLayoutPanel", src, StringComparison.Ordinal);
                Assert.Contains("brandPillsGrid", src, StringComparison.Ordinal);
                Assert.Contains("dictCatGrid", src, StringComparison.Ordinal);
                Assert.Contains("ApplyDpiLayoutMetrics", src, StringComparison.Ordinal);
                Assert.Contains("RefreshDpiMetricsAndLayout", src, StringComparison.Ordinal);
                Assert.Contains("UiFontCache.DisableMnemonicsRecursive(this)", src, StringComparison.Ordinal);

                foreach (int badPx in new[] { 19, 20, 22, 23, 26, 27, 28, 30 })
                {
                    Assert.DoesNotContain($"Height = {badPx}", src, StringComparison.Ordinal);
                }
            }

            // 3. Per-window High-DPI startup Task Pane width enforcement across Office 365 Backstage -> document open
            string wordAddIn = File.ReadAllText(Path.Combine(wordDir, "ThisAddIn.cs"));
            string excelAddIn = File.ReadAllText(Path.Combine(excelDir, "ExcelThisAddIn.cs"));
            foreach (string addInSrc in new[] { wordAddIn, excelAddIn })
            {
                Assert.Contains("ScheduleStartupPaneWidthEnforcement", addInSrc, StringComparison.Ordinal);
                Assert.Contains("UiFontCache.GetWindowDpi", addInSrc, StringComparison.Ordinal);
                Assert.Contains("UiFontCache.GetWindowWidthPx", addInSrc, StringComparison.Ordinal);
                Assert.Contains("_settledPaneWidthHwnds", addInSrc, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void V53_BilingualLexiconPhoneticsCollocationsRomanizedZawgyiAndBookmarksVerification()
        {
            string repoRoot = TestPaths.RepoRoot;
            string dbPath = Path.Combine(repoRoot, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath));

            // 1. Verify compact encrypted DB size (<= 22 MB) & zero active spellcheck headword inflation
            double dbSizeMb = new FileInfo(dbPath).Length / (1024.0 * 1024.0);
            Assert.True(dbSizeMb <= 22.0, $"Expected mlc_thatpon.db <= 22 MB, got {dbSizeMb:F2} MB");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            Assert.True(trie.WordCount >= 95000, $"Expected >= 95,000 active Myanmar headwords, got {trie.WordCount}");
            Assert.True(trie.BilingualLexiconCount >= 55000, $"Expected >= 55,000 lazy EN_LEX bilingual entries, got {trie.BilingualLexiconCount}");

            // 2. Verify English -> Myanmar bilingual lookups + Official Thatpon orthography sanitization
            var abandonHits = engine.SearchDictionary("abandon", 20);
            Assert.Contains(abandonHits, e => e.IsBilingualLexiconEntry && string.Equals(e.Word, "abandon", StringComparison.OrdinalIgnoreCase));

            var negotiateHits = engine.SearchDictionary("negotiate", 20);
            Assert.NotEmpty(negotiateHits);

            var procrastinateHits = engine.SearchDictionary("procrastinate", 20);
            Assert.Contains(procrastinateHits, e => string.Equals(e.Word, "procrastinate", StringComparison.OrdinalIgnoreCase));

            var knottyHits = engine.SearchDictionary("a knotty problem", 10);
            var knottyEntry = Assert.Single(knottyHits, e => string.Equals(e.Word, "a knotty problem", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                UnicodeNormalizer.NormalizeCanonicalOrder("ရှုပ်ထွေး"),
                UnicodeNormalizer.NormalizeCanonicalOrder(knottyEntry.MeaningCue),
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                UnicodeNormalizer.NormalizeCanonicalOrder("ရုပ်ထွေး"),
                UnicodeNormalizer.NormalizeCanonicalOrder(knottyEntry.MeaningCue),
                StringComparison.Ordinal);

            var eatHits = engine.SearchDictionary("eat", 25);
            Assert.Contains(eatHits, e => e.IsBilingualLexiconEntry && string.Equals(e.Word, "eat", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(eatHits, e => !e.IsReferenceEntry && UnicodeNormalizer.NormalizeCanonicalOrder(e.Word) == UnicodeNormalizer.NormalizeCanonicalOrder("စား"));

            // 3. Verify Dual Spoken Burmese + Easy Romanization + IPA & Offline TTS sanitizer
            var thatponPron = MyanmarPhoneticTranscriber.GetPronunciation("သတ်ပုံ");
            Assert.True(thatponPron.HasValue);
            Assert.Equal("သပ်-ပုံ", thatponPron.SpokenBurmese);
            Assert.Equal("that-pon", thatponPron.EasyRomanization);
            Assert.Equal("θaʔ.bòʊɴ", thatponPron.Ipa);
            string dualLine = MyanmarPhoneticTranscriber.FormatCompactDualLine(thatponPron, englishUi: true);
            Assert.Contains("သပ်-ပုံ", dualLine, StringComparison.Ordinal);
            Assert.Contains("that-pon", dualLine, StringComparison.Ordinal);

            var g2pPron = MyanmarPhoneticTranscriber.GetPronunciation("ကစား", "ga- za: [gə zá]");
            Assert.True(g2pPron.HasValue);
            Assert.False(string.IsNullOrWhiteSpace(g2pPron.SpokenBurmese));
            Assert.False(string.IsNullOrWhiteSpace(g2pPron.EasyRomanization));
            Assert.Equal("gə.zá", g2pPron.Ipa);

            Assert.Equal("eat", OfflineSpeechService.CleanSpeakableEnglishTerm("eat; consume (food)"));

            // 4. Verify Smart Search: Romanized Burmese ("mingalaba", "thatpon", "kyaukse") + Auto Zawgyi -> Unicode
            var mingalabaHits = engine.SearchDictionary("mingalaba", 20);
            Assert.Contains(mingalabaHits, e =>
                UnicodeNormalizer.NormalizeCanonicalOrder(e.Word) == UnicodeNormalizer.NormalizeCanonicalOrder("မင်္ဂလာပါ"));

            var thatponRomanHits = engine.SearchDictionary("thatpon", 20);
            Assert.Contains(thatponRomanHits, e =>
                UnicodeNormalizer.NormalizeCanonicalOrder(e.Word) == UnicodeNormalizer.NormalizeCanonicalOrder("သတ်ပုံ"));

            var kyaukseRomanHits = engine.SearchDictionary("kyaukse", 25);
            Assert.Contains(kyaukseRomanHits, e =>
                UnicodeNormalizer.NormalizeCanonicalOrder(e.Word).StartsWith(UnicodeNormalizer.NormalizeCanonicalOrder("ကျောက်ဆည်"), StringComparison.Ordinal));

            string zawgyiPanyar = "\u1015\u100A\u102C\u1031\u101B\u1038"; // Zawgyi for ပညာရေး
            string normPanyar = MyanmarTrie.NormalizeSearchQuery(zawgyiPanyar, out bool wasZawgyi);
            Assert.True(wasZawgyi);
            Assert.Equal(UnicodeNormalizer.NormalizeCanonicalOrder("ပညာရေး"), normPanyar);
            var zawgyiHits = engine.SearchDictionary(zawgyiPanyar, 15);
            Assert.Contains(zawgyiHits, e =>
                UnicodeNormalizer.NormalizeCanonicalOrder(e.Word) == UnicodeNormalizer.NormalizeCanonicalOrder("ပညာရေး"));

            // 5. Verify Real-World Corpus Collocations Explorer
            var collocations = engine.GetTopCollocations("စီမံ", 6);
            Assert.NotEmpty(collocations);
            Assert.All(collocations, c =>
                Assert.Contains(UnicodeNormalizer.NormalizeCanonicalOrder("စီမံ"), UnicodeNormalizer.NormalizeCanonicalOrder(c), StringComparison.Ordinal));

            // 6. Verify Live-Synced Word/Excel DictionaryBookmarkStore + LookupBookmarkedEntries
            string tempBookmarkFile = Path.Combine(Path.GetTempPath(), $"msc_bookmarks_v53_{Guid.NewGuid():N}.txt");
            try
            {
                var wordStore = new DictionaryBookmarkStore(tempBookmarkFile);
                var excelStore = new DictionaryBookmarkStore(tempBookmarkFile);

                Assert.True(wordStore.ToggleBookmark("သတ်ပုံ"));
                Assert.True(wordStore.ToggleBookmark("abandon"));
                Assert.Equal(2, excelStore.Count);
                Assert.True(excelStore.IsBookmarked("သတ်ပုံ"));
                Assert.True(excelStore.IsBookmarked("ABANDON"));

                var resolvedBookmarks = engine.LookupBookmarkedEntries(excelStore.GetBookmarkedWords());
                Assert.Equal(2, resolvedBookmarks.Count);
                Assert.Contains(resolvedBookmarks, e => UnicodeNormalizer.NormalizeCanonicalOrder(e.Word) == UnicodeNormalizer.NormalizeCanonicalOrder("သတ်ပုံ"));
                Assert.Contains(resolvedBookmarks, e => string.Equals(e.Word, "abandon", StringComparison.OrdinalIgnoreCase));

                Assert.False(excelStore.ToggleBookmark("abandon"));
                Assert.Equal(1, wordStore.Count);
            }
            finally
            {
                if (File.Exists(tempBookmarkFile))
                    File.Delete(tempBookmarkFile);
            }
        }

        [Fact]
        public void V54_FortySixThousandCellExcelAntiFreezeTrueCountAndBatchFixVerification()
        {
            string root = TestPaths.RepoRoot;
            string dbPath = Path.Combine(root, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath));

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie);

            // 1. Verify EcoPerformanceGovernor large-Excel true-count budget (50,000 issues) & worker scaling
            Assert.True(EcoPerformanceGovernor.IsLargeExcelScope(46_000));
            var largeBudgets = EcoPerformanceGovernor.ComputeBudgets(isLargeFile: true);
            Assert.Equal(50_000, largeBudgets.MaxExcelIssuesPerFullScan);
            Assert.True(largeBudgets.ExcelBackgroundWorkers >= 1);

            // 2. Verify deterministic Viterbi fuzzy-window memoization produces 100% identical NLP results
            string sampleUniqueA = "အမှတ်စဉ် ၉၀၀၁ — ကျောက်ဆည်မြို့နယ် စာရင်းစစ်ဆေးချက် ဆောင်ရွက်ပီးပီ ဖြစ်ပါသည်။";
            var coldIssues = engine.CheckText(sampleUniqueA);
            var warmIssues = engine.CheckText(sampleUniqueA, cacheParagraph: false);
            Assert.NotEmpty(coldIssues);
            Assert.Equal(coldIssues.Count, warmIssues.Count);
            for (int i = 0; i < coldIssues.Count; i++)
            {
                Assert.Equal(coldIssues[i].OriginalText, warmIssues[i].OriginalText);
                Assert.Equal(coldIssues[i].StartIndex, warmIssues[i].StartIndex);
                Assert.Equal(coldIssues[i].Suggestions.FirstOrDefault()?.Replacement, warmIssues[i].Suggestions.FirstOrDefault()?.Replacement);
            }

            // 3. Simulate exact 46,000-cell Excel sheet (11,500 rows x 4 cols = 46,000 cells):
            //    - Col 1: 11,500 UNIQUE Burmese row strings (defeats exact-string memo; exercises _fuzzyWindowCache!)
            //    - Col 2: Repeated status phrases with typos ("စားပီးပီ" / "လုပ်ငန်း ဆောင်ရွက်ပီးပီ")
            //    - Col 3: Numeric values (doubles)
            //    - Col 4: Formula column protected solely via sheetFormulaRects (rawFormulaBlock == null!)
            const int totalRows = 11_500;
            const int totalCols = 4;
            const int sliceRows = 250;

            string[] townships =
            {
                "ကျောက်ဆည်မြို့နယ်", "မန္တလေးမြို့တော်", "နေပြည်တော်ကောင်စီ", "ပြင်ဦးလွင်မြို့နယ်",
                "မိတ္ထီလာခရိုင်", "မြင်းခြံမြို့နယ်", "ညောင်ဦးခရိုင်", "ရမည်းသင်းမြို့နယ်"
            };

            var liveGrid = new string?[totalRows + 1, totalCols + 1];
            var formulaRects = new List<(int R1, int C1, int R2, int C2)> { (1, 4, totalRows, 4) };
            var sharedMemo = new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            var collected = new List<SpreadsheetCellIssue>(24_000);

            var swScan = System.Diagnostics.Stopwatch.StartNew();
            for (int baseRow = 1; baseRow <= totalRows; baseRow += sliceRows)
            {
                int rowsInSlice = Math.Min(sliceRows, totalRows - baseRow + 1);
                var valBlock = new object?[rowsInSlice, totalCols];

                for (int r = 0; r < rowsInSlice; r++)
                {
                    int absRow = baseRow + r;
                    string col1 = $"အမှတ်စဉ် {absRow} — {townships[absRow & 7]} စစ်ဆေးချက် ပီးပီ";
                    string col2 = (absRow % 2 == 0) ? "စားပီးပီ" : "လုပ်ငန်း ဆောင်ရွက်ပီးပီ";

                    liveGrid[absRow, 1] = col1;
                    liveGrid[absRow, 2] = col2;

                    valBlock[r, 0] = col1;
                    valBlock[r, 1] = col2;
                    valBlock[r, 2] = absRow * 1250.50;
                    // Col 4 has an evaluated string containing a typo, but is inside formulaRects (rawFormulaBlock is null!)
                    valBlock[r, 3] = "စားပီးပီ";
                }

                int remaining = largeBudgets.MaxExcelIssuesPerFullScan - collected.Count;
                var chunkHits = SpreadsheetProofingHelper.ScanBlockValues(
                    engine,
                    "Huge46k.xlsx",
                    "Sheet1",
                    baseRow,
                    1,
                    rowsInSlice,
                    totalCols,
                    valBlock,
                    rawFormulaBlock: null,
                    blockHasFormula: null,
                    sharedCellMemo: sharedMemo,
                    maxIssuesToCollect: remaining,
                    explicitBudgets: largeBudgets,
                    sheetFormulaRects: formulaRects);

                if (chunkHits.Count > 0)
                {
                    collected.AddRange(chunkHits);
                }
            }
            swScan.Stop();

            // True count must capture ALL 23,000 issues across 11,500 rows without stopping early at 2,500!
            Assert.True(
                collected.Count >= 20_000,
                $"Expected true count >= 20,000 issues on 46,000-cell sheet (not capped at 2,500), got {collected.Count}");

            // Zero formula cells from Column 4 may ever be flagged, even with rawFormulaBlock == null!
            Assert.DoesNotContain(collected, x => x.Column == 4);

            // Full 46,000-cell scan (with 11,500 unique Burmese sentences) must complete in < 6 seconds (down from 3-5 minutes!)
            Assert.True(
                swScan.ElapsedMilliseconds < 6_000,
                $"Expected 46,000-cell scan to finish in < 6,000 ms, took {swScan.ElapsedMilliseconds} ms");

            // 4. Verify 1-Click Repeated-Word Batch Fix across all 23,000 occurrences executes cleanly in < 1,000 ms
            var sampleIssue = collected.First(x =>
                !x.Issue.IsContextualWarning &&
                x.Issue.Suggestions.Count > 0 &&
                UnicodeNormalizer.NormalizeCanonicalOrder(x.Issue.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder("ပီးပီ"));
            string topReplacement = sampleIssue.Issue.Suggestions[0].Replacement;

            var swBatch = System.Diagnostics.Stopwatch.StartNew();
            var batchOps = SpreadsheetProofingHelper.BuildSameWordBatchReplacementsByCell(
                collected,
                sampleIssue.Issue.OriginalText,
                topReplacement,
                anchor => anchor.Column == 4 ? null : liveGrid[anchor.Row, anchor.Column]);
            swBatch.Stop();

            Assert.Equal(totalRows * 2, batchOps.Count);
            Assert.DoesNotContain(batchOps, op => op.Column == 4);
            Assert.All(batchOps, op => Assert.Contains(
                UnicodeNormalizer.NormalizeCanonicalOrder(topReplacement),
                UnicodeNormalizer.NormalizeCanonicalOrder(op.UpdatedText),
                StringComparison.Ordinal));
            Assert.True(
                swBatch.ElapsedMilliseconds < 1_000,
                $"Expected 23,000-cell repeated-word batch replacement plan in < 1,000 ms, took {swBatch.ElapsedMilliseconds} ms");

            // 5. Static source guard: ensure 17.18-billion-cell ws.Cells.Find("*") never regresses
            string scannerCs = File.ReadAllText(Path.Combine(root, "src", "MyanmarSpellChecker.ExcelAddIn", "Interop", "ExcelWorkbookScannerAndToolkit.cs"));
            Assert.DoesNotContain("ws.Cells.Find(", scannerCs, StringComparison.Ordinal);
            Assert.Contains("used.Find(", scannerCs, StringComparison.Ordinal);
        }

        [Fact]
        public void V55_WholeLexicon360ConsistencyAndOrthographyGate()
        {
            string root = TestPaths.RepoRoot;
            string dbPath = Path.Combine(root, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath));

            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            Assert.True(trie.WordCount >= 95_000, $"Expected >= 95,000 active headwords, got {trie.WordCount}");

            // 1. Rule-vs-Headword & Rule-vs-Bigram Zero-Contradiction Gate
            var activeHeadwords = new HashSet<string>(
                model.Entries.Where(e => !e.IsReferenceEntry).Select(e => e.Word),
                StringComparer.Ordinal);
            var condemnedWrongSet = new HashSet<string>(
                model.OrthographyRules
                    .Where(r => !string.Equals(r.Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.WrongForm),
                StringComparer.Ordinal);

            foreach (string wf in condemnedWrongSet)
            {
                Assert.DoesNotContain(wf, activeHeadwords);
            }

            foreach (var bg in model.WordBigrams)
            {
                Assert.DoesNotContain(bg.LeftWord, condemnedWrongSet);
                Assert.DoesNotContain(bg.RightWord, condemnedWrongSet);
            }

            // 2. 100% of single-word Myanmar CorrectForms across OrthographyRules & ContextRules must exist in active Trie
            var pureMyanmarRx = new System.Text.RegularExpressions.Regex(@"^[\u1000-\u103F\u104C-\u104F]+$");
            foreach (var r in model.OrthographyRules)
            {
                if (pureMyanmarRx.IsMatch(r.CorrectForm))
                {
                    Assert.True(
                        trie.ContainsExactWord(UnicodeNormalizer.NormalizeCanonicalOrder(r.CorrectForm)),
                        $"OrthographyRule CorrectForm missing from active Trie: {r.WrongForm} -> {r.CorrectForm}");
                }
            }
            foreach (var cr in model.ContextRules)
            {
                if (pureMyanmarRx.IsMatch(cr.CorrectForm))
                {
                    Assert.True(
                        trie.ContainsExactWord(UnicodeNormalizer.NormalizeCanonicalOrder(cr.CorrectForm)),
                        $"ContextualRule CorrectForm missing from active Trie: {cr.WrongForm} -> {cr.CorrectForm}");
                }
            }

            // 3. Active Headword Structural & Mojibake Purity Gate
            var vowelBeforeStackRx = new System.Text.RegularExpressions.Regex(@"[\u102B-\u1032\u1036]\u1039[\u1000-\u1021]");
            var misplacedYaAaAsatRx = new System.Text.RegularExpressions.Regex(@"[\u1000-\u1021][\u103B\u103C](?:[\u102B\u102C]\u103A|\u103A[\u102B\u102C])");
            var shiftedShweRx = new System.Text.RegularExpressions.Regex(@"\u101B\u103D(?:\u1004\u103A\u1039)?[\u1000-\u1021][\u103B-\u103D]*\u103E\u1031");
            var shiftedMweRx = new System.Text.RegularExpressions.Regex(@"(?:\u1019\u103C|\u1000\u103C|\u1001\u103C|\u1001\u103B)(?!(?:\u1004\u103D\u1031(?![\u1000-\u1021]\u103A)|\u101E\u103D\u1031\u1038|\u1019\u103C\u103D\u1031\u1000\u103C\u102E\u1038))(?:\u1004\u103A\u1039)?[\u1000-\u1021][\u103B\u103C]*\u103D\u1031");
            var illegalMedialTallAaRx = new System.Text.RegularExpressions.Regex(@"(?!\u1012\u103D|\u101D\u103E)[\u1000-\u1021][\u103B-\u103E]+\u1031?\u102B");

            foreach (string hw in activeHeadwords)
            {
                Assert.Matches(pureMyanmarRx, hw);
                Assert.DoesNotMatch(vowelBeforeStackRx, hw);
                Assert.DoesNotMatch(misplacedYaAaAsatRx, hw);
                Assert.DoesNotContain("\u103A\u103C", hw, StringComparison.Ordinal);
                Assert.DoesNotContain("\u1019\u103E\u102F\u1036", hw, StringComparison.Ordinal);
                Assert.DoesNotMatch(shiftedShweRx, hw);
                Assert.DoesNotMatch(shiftedMweRx, hw);
                Assert.DoesNotMatch(illegalMedialTallAaRx, hw);
            }

            // 4. Purged leaks must not exist in active headwords
            string[] purgedLeaks =
            {
                "ပီးပီး", "က်ုစင်", "ထပှ်", "ဓဂဵု", "အလုပ်သျှင်", "ပညာသျှင်",
                "စားလုံးပေါင်း", "ဆဌမ", "အဌမ", "ဓမ္မဓိဌာန်", "ကော့မှုး", "ကော့မှုးမြို့နယ်",
                "နှုတ်ခမ်းမွှေး", "ပုံပန်းသဏ္ဍာန်", "လုံးရပ်သဏ္ဍာန်"
            };
            foreach (string leak in purgedLeaks)
            {
                Assert.DoesNotContain(leak, activeHeadwords);
            }

            // 5. Live Engine Proofing & Zero-False-Positive Verification
            var expectedCorrections = new (string WrongInput, string ExpectedCanonical)[]
            {
                ("စားလုံးပေါင်း", "စာလုံးပေါင်း"),
                ("ဆဌမ", "ဆဋ္ဌမ"),
                ("အဌမ", "အဋ္ဌမ"),
                ("ဓမ္မဓိဌာန်", "ဓမ္မဓိဋ္ဌာန်"),
                ("ကော့မှုးမြို့နယ်", "ကော့မှူးမြို့နယ်"),
                ("နှုတ်ခမ်းမွှေး", "နှုတ်ခမ်းမွေး"),
                ("အလုပ်သျှင်", "အလုပ်ရှင်"),
                ("ယောကျ်ား", "ယောက်ျား"),
                ("ဒုကိ္ခတ", "ဒုက္ခိတ"),
                ("ပဥှာ", "ပဉှာ")
            };
            foreach (var (wrongInput, expectedCanonical) in expectedCorrections)
            {
                var issues = engine.CheckText(wrongInput);
                Assert.NotEmpty(issues);
                Assert.Contains(
                    issues.SelectMany(x => x.Suggestions),
                    s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(expectedCanonical));
            }

            string[] cleanCanonicalWords =
            {
                "ရွှေဘိုမြို့နယ်", "မြွေပါ", "မြွေပွေး", "ကြွေရောင်", "ချွေတာ",
                "ဂွာတီမာလာ", "ဒုက္ခိတ", "ပဉှာ", "သြောင်း", "ဥုံသြောင်း",
                "စတုမဓု", "အခက်ခဲဆုံး", "ကော့မှူးမြို့နယ်", "ဆည်မြောင်းဦးစီးဌာန"
            };
            foreach (string cleanWord in cleanCanonicalWords)
            {
                var issues = engine.CheckText(cleanWord);
                Assert.Empty(issues);
            }

            // 6. Full 360° Prose Sanitization Gate across all MeaningCue & UsageNote fields
            var zeroWidthRx = new System.Text.RegularExpressions.Regex(@"[\u200B-\u200F\uFEFF\u00AD]");
            foreach (var e in model.Entries)
            {
                foreach (string prose in new[] { e.MeaningCue ?? string.Empty, e.UsageNote ?? string.Empty })
                {
                    if (prose.Length == 0) continue;
                    Assert.DoesNotMatch(zeroWidthRx, prose);
                    Assert.DoesNotContain("၄င်း", prose, StringComparison.Ordinal);
                    Assert.DoesNotContain("\u1025\u103A", prose, StringComparison.Ordinal);
                    Assert.DoesNotMatch(vowelBeforeStackRx, prose);
                    Assert.DoesNotMatch(shiftedShweRx, prose);
                    Assert.DoesNotMatch(shiftedMweRx, prose);
                }
            }
        }

        [Fact]
        public void V56_WholeGovernmentPortalValidationAndLearningGate()
        {
            string root = TestPaths.RepoRoot;
            string dbPath = Path.Combine(root, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath));

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Zero False Positives on authentic .gov.mm prose across 83 government & statutory portals
            string[] cleanOfficialGovSentences =
            {
                "ကာကွယ်ရေးဦးစီးချုပ်ရုံး(ကြည်း၊ ရေ၊ လေ)မှ တာဝန်ရှိသူများ တက်ရောက်ကြသည်။",
                "ကျား/မ စာရင်းနှင့် အရောင်း/အဝယ် စာချုပ်များ စိစစ်သည်။",
                "သက်ဆိုင်ရာ အက်ဥပဒေအရ အရေးယူဆောင်ရွက်မည်။",
                "ကုန်းတလဘောင်ကျေးရွာအုပ်စု၊ ရာဇပတလမ်းနှင့် ကျောက် တလုံးကြီးမြို့နယ်ခွဲတို့တွင် ဆောင်ရွက်သည်။",
                "အရောင်းအိမ်ရာများ၊ အငှားအိမ်ရာများ၊ တန်ဖိုးနည်းအိမ်ရာနှင့် တန်ဖိုးသင့်အိမ်ရာများ တည်ဆောက်သည်။",
                "ပင်လုံလမ်း၊ လေးထောင့်ကန်လမ်း၊ ရွှေဂုံတိုင်ရပ်ကွက်နှင့် အောက်ပုဇွန်တောင်လမ်းရှိ ခြံအမှတ်များ စစ်ဆေးသည်။",
                "မြေခွန် မြေခ၊ သတင်းပို့ချက်၊ မြန်မာငွေကျပ်နှင့် ချေးငွေစာချုပ်များ စိစစ်သည်။",
                "ယွန်းလုပ်ငန်း၊ ချည်စက်ရုံ၊ ပြည်ဝင်ခွင့်၊ လူကူးတံတား၊ ရဲလုပ်ငန်းနှင့် ကုန်တင်ကုန်ချ လုပ်ငန်းများ ဆောင်ရွက်သည်။",
                "ဆည်ရေအမှတ်၊ စိုးရိမ်ရေမှတ်နှင့် ရေလွှဲဆည်တို့ကို စောင့်ကြည့်စစ်ဆေးသည်။",
                "ပေါ်ဆန်းစပါး၊ ရွှေဘိုပေါ်ဆန်း၊ တီလားပီးယားနှင့် ငါးဖမ်းရေယာဉ်များ စစ်ဆေးသည်။",
                "အမိုနီယမ်ဆာလဖိတ်၊ ဂျစ်ပ်ဆမ်၊ ရှီးလိုက်၊ ဒိုလိုမိုက်နှင့် အဖြိုက်နက် သတ္တုများ တူးဖော်သည်။",
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံသားဖြစ်ရမည်။",
                "စပါးတစ်တင်းလျှင် (၅၃၀၀၀)ကျပ်ခန့်ရရှိသောကြောင့် တောင်သူများ အဆင်ပြေသည်။",
                "ရေဝက်၊ ပုံရိပ်ဖော်စက်၊ ဓာတ်ဆီပန့်၊ ပင်နီယံနှင့် အိတ်ဖွင့်တင်ဒါ ခေါ်ယူသည်။",
                "ဆေးရုံသစ်လမ်း၊အောင်ဆန်း ပြည်သာယာရပ်ကွက်နှင့် ရာဇဓိရာဇ်လမ်း၊ မြရတနာလမ်း၊ ဘိုတဲရပ်ကွက်တို့ကို စစ်ဆေးသည်။",
                "ဒေါ်တင်တင်အုန်းက သာသနာ့မြေဂရန်၊ နို့ချက်စက်ရုံ၊ ဇာမဏီနှင့် ကုန်စည်အသွင်းအထုတ် လုပ်ငန်းများကို တင်ပြသည်။"
            };

            foreach (string sentence in cleanOfficialGovSentences)
            {
                var issues = engine.CheckText(sentence);
                Assert.True(
                    issues.Count == 0,
                    $"Expected 0 issues on valid .gov.mm sentence '{sentence}', but got: {string.Join("; ", issues.Select(i => $"{i.IssueType}:{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}");
            }

            // 2. Real .gov.mm clerical typos must be caught and repaired to 462-page Thatpon standard
            var govClericalTypos = new (string WrongInput, string ExpectedCanonical)[]
            {
                ("ညွှန်ကြားရေမှူး", "ညွှန်ကြားရေးမှူး"),
                ("ဖွံဖြိုးတိုးတက်ရေး", "ဖွံ့ဖြိုးတိုးတက်ရေး"),
                ("ရင်းနှီးမြှပ်နှံမှု", "ရင်းနှီးမြှုပ်နှံမှု"),
                ("စိမ်းလန်းစိုပြေ", "စိမ်းလန်းစိုပြည်"),
                ("အစားသောက်", "အစားအသောက်"),
                ("အကောင်ထည်ဖော်", "အကောင်အထည်ဖော်"),
                ("ထိမ်းသိမ်း", "ထိန်းသိမ်း"),
                ("မဟာဗျုဟာ", "မဟာဗျူဟာ"),
                ("ရေလွဲဆည်", "ရေလွှဲဆည်"),
                ("ငါးဖမ်ရေယာဉ်", "ငါးဖမ်းရေယာဉ်"),
                ("ထိန်ချန်", "ထိမ်ချန်"),
                ("အနှံအပြား", "အနှံ့အပြား"),
                ("ရေတခါး", "ရေတံခါး"),
                ("စီမံခန့်ခွဲမူ", "စီမံခန့်ခွဲမှု")
            };

            foreach (var (wrongInput, expectedCanonical) in govClericalTypos)
            {
                var issues = engine.CheckText(wrongInput);
                Assert.NotEmpty(issues);
                string normExpected = UnicodeNormalizer.NormalizeCanonicalOrder(expectedCanonical);
                Assert.Contains(
                    issues.SelectMany(issue => issue.Suggestions.Select(s =>
                        wrongInput.Substring(0, issue.StartIndex) + s.Replacement + wrongInput.Substring(issue.StartIndex + issue.Length))),
                    repaired => UnicodeNormalizer.NormalizeCanonicalOrder(repaired) == normExpected);
            }

            // 3. Lookalike postposition safety: 'စျေးအတွင်း' must repair 'စျေး -> ဈေး' and NEVER mutate into 'စည်းအတွင်း'
            var marketIssues = engine.CheckText("စျေးအတွင်း ရောင်းချသည်။");
            Assert.NotEmpty(marketIssues);
            Assert.DoesNotContain(
                marketIssues.SelectMany(x => x.Suggestions),
                s => s.Replacement.IndexOf("စည်းအတွင်း", StringComparison.Ordinal) >= 0);
            Assert.Contains(
                marketIssues.SelectMany(x => x.Suggestions),
                s => s.Replacement.IndexOf("ဈေး", StringComparison.Ordinal) >= 0);

            // 4. Context-aware duplicate comma ('၊၊') suggestion ranking
            var parenCommaIssues = engine.CheckText("အမှတ်(၃၀၆၂၊၃၀၆၃၊၊၃၁၁၆)");
            var parenDup = Assert.Single(parenCommaIssues.Where(x => x.IssueType == ProofingIssueType.PunctuationError));
            Assert.Equal("၊", parenDup.Suggestions[0].Replacement);

            var headerCommaIssues = engine.CheckText("၂၊၊ ပြည်ထောင်စုဝန်ကြီးဌာန");
            var headerDup = Assert.Single(headerCommaIssues.Where(x => x.IssueType == ProofingIssueType.PunctuationError));
            Assert.Equal("။", headerDup.Suggestions[0].Replacement);
        }

        [Fact]
        public void V57_GovernmentNewsValidationAndPmiLearningGate()
        {
            string root = TestPaths.RepoRoot;
            string dbPath = Path.Combine(root, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath));

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Zero False Positives across all 7 Official Myanmar Government News Outlet Groups
            //    (MOI, IPRD 330-townships, MRTV, MDN, Myawady, Moezala Meteorology, Union/Regional Newsrooms)
            string[] cleanGovNewsSentences =
            {
                "ထုတ်ကုန်မြှင့်တင်မျှတဆောင်ရွက်ရေး အစည်းအဝေးကို သပြေဘူတခန်းမ၌ ကျင်းပသည်။",
                "သုံးလောကထွတ်ထား မြတ်စွာဘုရားရှင်အား ရည်မှန်း၍ အန္တိမအဂ္ဂိဈာပန သာဓုကီဠန ပူဇာသဘင်နှင့် ဝိဇ္ဇရိယဘုန်းတော်ကြီးကျောင်း ရေစက်ချပွဲ ကျင်းပသည်။",
                "စက်မှုကုန်ထုတ်ပစ္စည်းများ၊ ဆီမီးကွန်ဒတ်တာ၊ လျှပ်စစ်ထရန်စဖော်မာနှင့် ရေနံချက်စက်ရုံ လုပ်ငန်းများကို စစ်ဆေးသည်။",
                "ဗဟိုချက်လေဖိအား ၁၀၀၀ ဟက်တိုပါစကယ် ရှိသော ဆိုင်ကလုန်းမုန်တိုင်းနှင့် မြို့ပြအပူကျွန်းဖြစ်စဉ်ကြောင့် မိုးသက်လေပြင်း တိုက်ခတ်နိုင်သည်။",
                "မြစ်ကမ်းထိန်းလုပ်ငန်း၊ တမံပေါင်လျော့ကျမှုနှင့် တာအိပဲ့ကျမှု အခြေအနေများကို မြေပြင်ကွင်းဆင်း စစ်ဆေးသည်။",
                "တစ်ဧကလျှင် ငွေကျပ် ၆ သိန်း ကျော်ခန့်ရရှိရာ ဒေသခံတောင်သူများ သီးနှံဖျက်ပိုး ကာကွယ်ရေးကို ဆောင်ရွက်ကြသည်။",
                "ရှမ်းယူနိုက်တက်အသင်းသည် ပိုမိုကောင်းမွန်သည့် ကစားပုံဖြင့် ချေပဂိုးနှင့် အနိုင်ဂိုးများ သွင်းယူခဲ့သည်။",
                "နှစ်နိုင်ငံအစိုးရချင်း နားလည်မှုစာချွန်လွှာ လက်မှတ်ရေးထိုးပြီး ရင်းနှီးပွင့်လင်းစွာ အမြင်ချင်းဖလှယ် ဆွေးနွေးကြသည်။",
                "ပြည်သူ့ဆေးရုံကြီးရှိ ကလေးကုသဆောင်၊ အထူးကြပ်မတ်ကုသဆောင်နှင့် အရေးပေါ်ကုသဆောင်တို့တွင် ကျန်းမာရေးစောင့်ရှောက်မှု ပေးလျက်ရှိသည်။",
                "သက်ဆိုင်ရာလဝကမြို့နယ်ရုံးက လူဦးရေစာရင်း ကောက်ယူပြီး ကျောင်းစာအပြင် စာဖတ်ရှိန်မြှင့်တင်ရေး လူငယ်စကားဝိုင်း ကျင်းပသည်။",
                "ရေလယ်တိုင်အမှတ်၊ မိုင်တိုင်အမှတ်နှင့် ကုန်တင်ယာဉ်အစီးရေ စာရင်းများကို စစ်ဆေးပြီး ဂြိုဟ်တုကို အာကာသတွင်းသို့ လွှတ်တင်သည်။",
                "ရုရှားဖက်ဒရေးရှင်းနိုင်ငံ၊ ဟနွိုင်းမြို့၊ ဆီယမ်ရိမြို့နှင့် ပို့ဘလဲမြို့ သတင်းများကို ထုတ်လွှင့်ပြသပြီး ရင်တွင်းခံစားချက်များကို မေးမြန်းသည်။",
                "သီတဂူဆရာတော် ဒေါက်တာဘဒ္ဒန္တဉာဏိဿရ ၏ ဓမ္မစကြာတရားတော်ကို နာယူပြီး မစ္စတာ ယိုရှိတာခဲ ရှိုဂို အား လက်ခံတွေ့ဆုံသည်။",
                "ဘုန်းကြီး ကျောင်းလမ်းနှင့် မင်းရပ်တန်းလမ်းအနီး ဈေးကွက်ဝင်သီးနှံများ စိုက်ပျိုးကာ ဖလက်ရေထိန်းတံခါး ပြုပြင်သည်။"
            };

            foreach (string sentence in cleanGovNewsSentences)
            {
                var issues = engine.CheckText(sentence);
                Assert.True(
                    issues.Count == 0,
                    $"Expected 0 issues on valid Government News sentence '{sentence}', but got: {string.Join("; ", issues.Select(i => $"{i.IssueType}:{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}");
            }

            // 2. Real Journalistic Clerical Typos Caught & Repaired to 462-Page Thatpon / Official Standard
            var newsroomClericalTypos = new (string WrongInput, string ExpectedCanonical)[]
            {
                ("လျော့ချ", "လျှော့ချ"),
                ("နေ့လည်", "နေ့လယ်"),
                ("ဆက်နွယ်", "ဆက်နွှယ်"),
                ("ရှေ့ရှု", "ရှေးရှု"),
                ("အသက်မွေးဝမ်းကြောင်း", "အသက်မွေးဝမ်းကျောင်း"),
                ("ကျောင်းဝန်း", "ကျောင်းဝင်း"),
                ("မုတ်သုန်လေ", "မုတ်သုံလေ"),
                ("လျှပ်တစ်ပျက်", "လျှပ်တစ်ပြက်"),
                ("ရုရှားဖက်ဒရေးရှင်", "ရုရှားဖက်ဒရေးရှင်း"),
                ("ပျှမ်းမျှ", "ပျမ်းမျှ"),
                ("ရေငုတ်", "ရေငုပ်"),
                ("မချမ်းဘော့မြို့နယ်", "မချမ်းဘောမြို့နယ်"),
                ("ဟနွိင်းမြို့", "ဟနွိုင်းမြို့"),
                ("တစ်သျူး", "တစ်သျှူး"),
                ("အဖွဲဝင်", "အဖွဲ့ဝင်")
            };

            foreach (var (wrongInput, expectedCanonical) in newsroomClericalTypos)
            {
                var issues = engine.CheckText(wrongInput);
                Assert.NotEmpty(issues);
                string normExpected = UnicodeNormalizer.NormalizeCanonicalOrder(expectedCanonical);
                Assert.Contains(
                    issues.SelectMany(issue => issue.Suggestions.Select(s =>
                        wrongInput.Substring(0, issue.StartIndex) + s.Replacement + wrongInput.Substring(issue.StartIndex + issue.Length))),
                    repaired => UnicodeNormalizer.NormalizeCanonicalOrder(repaired) == normExpected);
            }

            // 3. Homophone & Minimal-Pair Safety on Government News ('စေတီတော်မြက်ကြီး -> စေတီတော်မြတ်ကြီး' & 'စေတီတော် မြက်ကြီး -> မြတ်' + symmetric shields 'စာတို', 'ရေစုပ်ပန့်', 'ထုတ်ကုန်ပစ္စည်း', 'လေဘေးသင့်ပြည်သူ')
            var unspacedPagodaIssues = engine.CheckText("ရှေးဟောင်းသမိုင်းဝင် စေတီတော်မြက်ကြီးအား ဖူးမြော်ကြည်ညိုသည်။");
            Assert.NotEmpty(unspacedPagodaIssues);
            Assert.Contains(
                unspacedPagodaIssues.SelectMany(x => x.Suggestions),
                s => s.Replacement.IndexOf("မြတ်", StringComparison.Ordinal) >= 0);

            var spacedPagodaIssues = engine.CheckText("ရှေးဟောင်းသမိုင်းဝင် စေတီတော် မြက်ကြီးအား ဖူးမြော်ကြည်ညိုသည်။");
            var pagodaIssue = Assert.Single(spacedPagodaIssues.Where(x =>
                x.IssueType == ProofingIssueType.SpellingError ||
                x.IssueType == ProofingIssueType.ContextualMeaningWarning));
            Assert.Equal("မြက်", pagodaIssue.OriginalText);
            Assert.Contains(
                pagodaIssue.Suggestions,
                s => s.Replacement.IndexOf("မြတ်", StringComparison.Ordinal) >= 0);

            var symmetricCleanIssues = engine.CheckText("ဖုန်းစာတို ပေးပို့၍ လေဘေးသင့်ပြည်သူများအတွက် ရေစုပ်ပန့်၊ ထုတ်ကုန်ပစ္စည်းနှင့် အရေးပေါ်ဆန်ရိက္ခာများ ထောက်ပံ့သည်။");
            Assert.Empty(symmetricCleanIssues);
        }

        [Fact]
        public void V58_MyanmarNationalRegistrationCardCodeValidationGate()
        {
            string dbPath = Path.Combine(TestPaths.RepoRoot, "data", "mlc_thatpon.db");
            Assert.True(File.Exists(dbPath), "Compiled mlc_thatpon.db must exist for V58 NRC validation.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. All Real-World Myanmar NRC Formats (Full, Slash 'ဇဗသ/နိုင်', Split-Column, Dotted, Spaced, ASCII-Digit & All Card Types) = 0 False Positives
            string[] validNrcInputs =
            {
                "ဇဗသ/နိုင်",
                "ခမသ/နိုင်",
                "ဥကတ/နိုင်",
                "ကဘလ/နိုင်",
                "ပတယ/နိုင်",
                "၉/ဇဗသ(နိုင်)၁၂၃၄၅၆",
                "၉/ဥတသ(နိုင်)၁၂၃၄၅၆",
                "၉/ဒကသ(နိုင်)၁၂၃၄၅၆",
                "၉/ပဗသ(နိုင်)၁၂၃၄၅၆",
                "၉/ဇယသ(နိုင်)၁၂၃၄၅၆",
                "၉/ပမန(နိုင်)၁၂၃၄၅၆",
                "၉/လဝန(နိုင်)၁၂၃၄၅၆",
                "၉/တကန(နိုင်)၁၂၃၄၅၆",
                "၉/ကဆန(နိုင်)၁၂၃၄၅၆",
                "၉/ပဥလ(နိုင်)၁၂၃၄၅၆",
                "၉/ညဥန(နိုင်)၁၂၃၄၅၆",
                "၉/တတဥ(နိုင်)၁၂၃၄၅၆",
                "၁၂/ဥကတ(နိုင်)၀၁၂၃၄၅",
                "12/ဥကတ(နိုင်)012345",
                "၁၂/ဥကမ(နိုင်)၁၂၃၄၅၆",
                "၁၂/ဒဂဆ(နိုင်)၁၂၃၄၅၆",
                "၁၂/တတထ(နိုင်)၁၂၃၄၅၆",
                "၄/ထတလ(နိုင်)၁၂၃၄၅၆",
                "၅/ဘတလ(နိုင်)၁၂၃၄၅၆",
                "၇/နတလ(နိုင်)၁၂၃၄၅၆",
                "၇/ပတလ(နိုင်)၁၂၃၄၅၆",
                "၁၁/ကတလ(နိုင်)၁၂၃၄၅၆",
                "၁၃/ကတလ(နိုင်)၁၂၃၄၅၆",
                "၁၃/တလန(နိုင်)၁၂၃၄၅၆",
                "၉/ဇဗသ/နိုင်/၁၂၃၄၅၆",
                "ဇဗသ(နိုင်)၁၂၃၄၅၆",
                "၉/ဇဗသ(ဧည့်)၁၂၃၄၅၆",
                "၉/ဇဗသ(ပြု)၁၂၃၄၅၆",
                "၉/ဇဗသ(သာသနာ)၁၂၃၄၅၆",
                "၉/ဇဗသ(သာ)၁၂၃၄၅၆",
                "၉/ဇဗသ(ယာယီ)၁၂၃၄၅၆",
                "၉/ဇဗသ(စ)၁၂၃၄၅၆",
                "ဇဗသ/စ",
                "ဇဗသ/န",
                "ဇ.ဗ.သ/နိုင်",
                "၉/ဇ.ဗ.သ(နိုင်)၁၂၃၄၅၆",
                "ဇ ဗ သ/နိုင်",
                "၉/ဇ ဗ သ(နိုင်)၁၂၃၄၅၆",
                "ဇဗသ",
                "ဥကတ",
                "ခမသ",
                "နိုင်ငံသားစိစစ်ရေးကတ်ပြားအမှတ် ၉/ဇဗသ(နိုင်)၁၂၃၄၅၆ နှင့် ၁၂/ဥကတ(နိုင်)၀၁၂၃၄၅ ကိုင်ဆောင်သူများ စာရင်းသွင်းပြီးဖြစ်သည်။"
            };

            foreach (string validInput in validNrcInputs)
            {
                var issues = engine.CheckText(validInput);
                Assert.True(
                    issues.Count == 0,
                    $"Expected 0 issues on valid NRC input '{validInput}', got: {string.Join("; ", issues.Select(i => $"{i.IssueType}:{i.OriginalText}->{i.Suggestions.FirstOrDefault()?.Replacement}"))}");
            }

            // 2. Seed-Only Fallback Mode Must Also Pass 'ဇဗသ/နိုင်', '၁၂/ဥကတ(နိုင်)၀၁၂၃၄၅', and '၄/ထတလ(နိုင်)၁၂၃၄၅၆' With 0 False Positives
            var seedTrie = new MyanmarTrie();
            foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries())
                seedTrie.InsertEntry(se);
            foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules())
                seedTrie.InsertOrthographyRule(sr);
            var seedEngine = new MyanmarProofingEngine(seedTrie, ProofingStyleMode.FormalOfficial);

            foreach (string seedInput in new[] { "ဇဗသ/နိုင်", "၉/ဇဗသ(နိုင်)၁၂၃၄၅၆", "၁၂/ဥကတ(နိုင်)၀၁၂၃၄၅", "၄/ထတလ(နိုင်)၁၂၃၄၅၆", "ခမသ/နိုင်", "ဇဗသ/စ" })
            {
                Assert.Empty(seedEngine.CheckText(seedInput));
            }

            // 3. Real NRC Clerical Typos Caught & Repaired with MOIP Precision
            var nrcClericalTypos = new (string WrongInput, string ExpectedTokenRepair)[]
            {
                ("၉/လ၀န(နိုင်)၁၂၃၄၅၆", "လဝန"),
                ("၁၂/ဦကတ(နိုင်)၁၂၃၄၅၆", "ဥကတ"),
                ("၉/ဦတသ(နိုင်)၁၂၃၄၅၆", "ဥတသ"),
                ("၉/ပဦလ(နိုင်)၁၂၃၄၅၆", "ပဥလ"),
                ("၁၂/ဉကတ(နိုင်)၁၂၃၄၅၆", "ဥကတ"),
                ("ဇဘသ/နိုင်", "ဇဗသ"),
                ("ပဘသ/နိုင်", "ပဗသ"),
                ("ဇဗသ/နိင်", "နိုင်"),
                ("၉/ဇဗသ(နိင်)၁၂၃၄၅၆", "နိုင်"),
                ("၉/ဇဗသ(နိုင)၁၂၃၄၅၆", "နိုင်"),
                ("၉/ဇဗသ(ဧည်)၁၂၃၄၅၆", "ဧည့်"),
                ("၉/ဇဗသ(ပြူ)၁၂၃၄၅၆", "ပြု"),
                ("ဇဗသ/ပြူ", "ပြု"),
                ("၉/ဇဗသ(ယာရီ)၁၂၃၄၅၆", "ယာယီ"),
                ("၁ဝ/မလမ(နိုင်)၁၂၃၄၅၆", "၁၀"),
                ("၉/ဇဗသ(နိုင်)ဝ၁၂၃၄၅", "၀၁၂၃၄၅"),
                ("၁၅/ဇဗသ(နိုင်)၁၂၃၄၅၆", "၁၂")
            };

            foreach (var (wrongInput, expectedRepair) in nrcClericalTypos)
            {
                var issues = engine.CheckText(wrongInput);
                Assert.NotEmpty(issues);
                string normExpected = UnicodeNormalizer.NormalizeCanonicalOrder(expectedRepair);
                Assert.Contains(
                    issues.SelectMany(x => x.Suggestions),
                    s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == normExpected);
            }

            // 4. Bilingual Myanmar & English NRC Dictionary Search ('ဇဗသ', 'ZaBaTha', '၉/ဇဗသ(နိုင်)၁၂၃၄၅၆', '12/ဥကတ(နိုင်)012345')
            var searchMm = trie.SearchDictionary("ဇဗသ", 10);
            Assert.NotEmpty(searchMm);
            Assert.Equal("ဇဗသ", searchMm[0].Word);
            Assert.Contains("ဇမ္ဗူသီရိ", searchMm[0].MeaningCue);

            var searchEn = trie.SearchDictionary("ZaBaTha", 10);
            Assert.NotEmpty(searchEn);
            Assert.Contains(searchEn, e => e.MeaningCue.IndexOf("ဇမ္ဗူသီရိ", StringComparison.Ordinal) >= 0);

            var searchFullMm = trie.SearchDictionary("၉/ဇဗသ(နိုင်)၁၂၃၄၅၆", 10);
            Assert.NotEmpty(searchFullMm);
            Assert.Equal("ဇဗသ", searchFullMm[0].Word);

            var searchFullOkkala = trie.SearchDictionary("12/ဥကတ(နိုင်)012345", 10);
            Assert.NotEmpty(searchFullOkkala);
            Assert.Equal("ဥကတ", searchFullOkkala[0].Word);
            Assert.Contains("တောင်ဥက္ကလာပ", searchFullOkkala[0].MeaningCue);
        }

        [Fact]
        public void V59_PotatoPcPerformanceAndMemoryGate()
        {
            string dbPath = Path.Combine(TestPaths.RepoRoot, "data", "mlc_thatpon.db");
            string key = AntiTamperGuard.ReconstructProtectedFallbackKey();

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(key);
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Zero-regex stack-normalizer phonetic & strict homophone parity
            Assert.Equal(MyanmarTrie.ComputePhoneticKey("သင်္ကြန်"), MyanmarTrie.ComputePhoneticKey("သကြန်"));
            Assert.Equal(MyanmarTrie.ComputePhoneticKey("အင်္ကျီ"), MyanmarTrie.ComputePhoneticKey("အကျီ"));
            foreach (string stackedWord in new[] { "မန္တလေး", "ကမ္ဘာ", "ပုဂ္ဂိုလ်", "မေတ္တာ", "ဥက္ကဋ္ဌ", "သဏ္ဌာန်" })
            {
                Assert.False(string.IsNullOrEmpty(MyanmarTrie.ComputePhoneticKey(stackedWord)));
                Assert.False(string.IsNullOrEmpty(MyanmarTrie.ComputeStrictHomophoneKey(stackedWord)));
            }

            // 2. Clean Pyidaungsu vs Zawgyi fast pre-filter parity
            Assert.Empty(UnicodeNormalizer.FindZawgyiSpans("ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် ပညာရေးဝန်ကြီးဌာန ၂၀၂၆ ခုနှစ်။"));
            Assert.NotEmpty(UnicodeNormalizer.FindZawgyiSpans("ျမန္မာစာ ႏိုင္ငံေတာ္"));

            // 3. Synonym & Antonym lookup must NOT hydrate the 84,000+ deferred reference corpus
            int expectedRefCount = trie.ReferenceCount;
            Assert.True(expectedRefCount > 80_000, $"Expected >80,000 deferred reference entries, got {expectedRefCount}");
            Assert.False(trie.IsReferenceCorpusLoaded);
            Assert.False(engine.IsDictionarySearchReady);

            var syns = engine.GetSynonyms("ရဟန်း", 8);
            var ants = engine.GetAntonyms("အကောင်း", 8);
            Assert.NotEmpty(syns);
            Assert.NotEmpty(ants);
            Assert.False(trie.IsReferenceCorpusLoaded);
            Assert.False(engine.IsDictionarySearchReady);

            // 4. Potato PC Hardware Governance + 46,000-cell Excel scan with 4,600 unique canonical NRCs
            var potatoHw = new MyanmarSpellChecker.Core.Performance.HardwareResourceProfile
            {
                LogicalCores = 2,
                TotalPhysicalRamMb = 4096,
                AvailablePhysicalRamMb = 896,
                Is32BitOfficeProcess = true,
                IsLowSpecHardware = true
            };
            Assert.True(potatoHw.IsUltraLowMemoryHost);
            var potatoBudgets = MyanmarSpellChecker.Core.Performance.EcoPerformanceGovernor.ComputeBudgets(
                true,
                MyanmarSpellChecker.Core.Performance.EcoModePreference.AlwaysOn,
                potatoHw);

            int rows = 4600, cols = 10;
            var grid = new object[rows, cols];
            static string ToMmSixDigits(int n)
            {
                char[] c = n.ToString("D6").ToCharArray();
                for (int i = 0; i < c.Length; i++) c[i] = (char)('၀' + (c[i] - '0'));
                return new string(c);
            }

            for (int r = 0; r < rows; r++)
            {
                grid[r, 0] = (double)(r + 1);
                grid[r, 1] = (r % 2 == 0)
                    ? "၉/ဇဗသ(နိုင်)" + ToMmSixDigits(r + 1)
                    : "၁/မကန(နိုင်)" + ToMmSixDigits(r + 1);
                grid[r, 2] = "ပညာရေးဝန်ကြီးဌာန";
                grid[r, 3] = "ကျောက်ဆည်တက္ကသိုလ်";
                grid[r, 4] = "စိစစ်ပြီး";
                grid[r, 5] = "2026-11-14";
                grid[r, 6] = (double)(1000 + r);
                grid[r, 7] = "မှတ်တမ်းအမှတ် " + (r % 200);
                grid[r, 8] = (r == 10) ? "၁၅/မရက(နိုင်)၁၂၃၄၅၆"
                           : (r == 20) ? "၉/ဇဗသ(ပြူ)၁၂၃၄၅၆"
                           : (r == 30) ? "၁၂/မရက(နိုင်)၁၂ဝ၄၅၆"
                           : "အတည်ပြုပြီး";
                grid[r, 9] = (r == 40) ? "နေ့လည် စီစစ်ပီး" : "ဆောင်ရွက်ပြီး";
            }

            var memo = new Dictionary<string, IReadOnlyList<ProofingIssue>>(StringComparer.Ordinal);
            var swExcel = System.Diagnostics.Stopwatch.StartNew();
            var excelIssues = SpreadsheetProofingHelper.ScanBlockValues(
                engine,
                "Potato46k.xlsx",
                "NRC_Sheet",
                1,
                1,
                rows,
                cols,
                grid,
                rawFormulaBlock: null,
                blockHasFormula: false,
                sharedCellMemo: memo,
                cancellationToken: default,
                maxIssuesToCollect: 10000,
                explicitBudgets: potatoBudgets);
            swExcel.Stop();

            // Zero valid canonical NRCs in Column 2 may be flagged OR pollute the cell memo
            Assert.DoesNotContain(excelIssues, x => x.Column == 2);
            Assert.False(memo.ContainsKey("၉/ဇဗသ(နိုင်)၀၀၀၀၀၁"));
            // Malformed NRCs and prose typos must still be caught with zero accuracy loss
            Assert.Contains(excelIssues, x => x.Row == 11 && x.Column == 9);
            Assert.Contains(excelIssues, x => x.Row == 21 && x.Column == 9);
            Assert.Contains(excelIssues, x => x.Row == 31 && x.Column == 9);
            Assert.Contains(excelIssues, x => x.Row == 41 && x.Column == 10);
            Assert.True(swExcel.ElapsedMilliseconds < 3500, $"Expected Potato PC 46k scan < 3500 ms, took {swExcel.ElapsedMilliseconds} ms");

            // 5. Fast Dictionary Search + Potato PC Aggressive Memory Eviction & Transparent Reload
            var enHits = engine.SearchDictionary("abandon", 15);
            var romHits = engine.SearchDictionary("mingalaba", 15);
            var nrcHits = engine.SearchDictionary("ဇဗသ/နိုင်", 10);
            Assert.NotEmpty(enHits);
            Assert.NotEmpty(romHits);
            Assert.NotEmpty(nrcHits);
            Assert.True(engine.IsDictionarySearchReady);
            Assert.True(trie.IsReferenceCorpusLoaded);

            // Aggressive trim evicts lazy search & reference caches while preserving ReferenceCount and full proofing
            engine.TrimMemoryCaches(aggressive: true);
            Assert.False(engine.IsDictionarySearchReady);
            Assert.False(trie.IsReferenceCorpusLoaded);
            Assert.Equal(expectedRefCount, trie.ReferenceCount);

            var postTrimIssues = engine.CheckText("ယနေ့ နေ့လည် ပီးပီ။");
            Assert.True(postTrimIssues.Count >= 2);
            Assert.False(trie.IsReferenceCorpusLoaded);

            // Transparent reload on subsequent dictionary / encyclopedia lookup
            var reloadedHits = engine.SearchDictionary("abandon", 15);
            var featuredHits = engine.GetFeaturedKnowledgeEntries(8);
            Assert.NotEmpty(reloadedHits);
            Assert.NotEmpty(featuredHits);
            Assert.True(engine.IsDictionarySearchReady);
            Assert.True(trie.IsReferenceCorpusLoaded);
        }

        [Fact]
        public void V60_ProductiveCompoundAndSyntheticGlossIntegrityGate()
        {
            string dbPath = Path.Combine(TestPaths.RepoRoot, "data", "mlc_thatpon.db");
            string key = AntiTamperGuard.ReconstructProtectedFallbackKey();

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(key);
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var sugg = new SuggestionEngine(trie);

            // 1. Productive Burmese Compounds ([Clean Standalone Word] + [Multi-Syllable Head], etc.) MUST produce ZERO false positives
            string[] validProductiveCompounds =
            {
                "ရဲလုပ်ငန်း",
                "မှုခင်းရဲလုပ်ငန်း",
                "လုံခြုံရေးရဲလုပ်ငန်း",
                "ပဲလုပ်ငန်း",
                "သံလုပ်ငန်း",
                "နို့လုပ်ငန်း",
                "ဆားလုပ်ငန်း",
                "နွားလုပ်ငန်း",
                "ဝါးလုပ်ငန်း",
                "ပျားလုပ်ငန်း",
                "ဆေးလုပ်ငန်း",
                "ဈေးလုပ်ငန်း",
                "ဘဏ်လုပ်ငန်း",
                "မိုင်းလုပ်ငန်း",
                "ကျောက်လုပ်ငန်း",
                "သစ်လုပ်ငန်း",
                "စက်လုပ်ငန်း",
                "ရုံးလုပ်ငန်း",
                "လယ်လုပ်ငန်း",
                "လယ်ယာလုပ်ငန်း",
                "ရဲတပ်ဖွဲ့",
                "ရဲတပ်ဖွဲ့ဝင်",
                "ရဲစခန်း",
                "ရဲအရာရှိ",
                "လုပ်ငန်းသစ်",
                "လုပ်ငန်းရှင်",
                "လုပ်ငန်းခွင်",
                "လုပ်ငန်းစဉ်"
            };

            foreach (string validCompound in validProductiveCompounds)
            {
                var hitsOfficial = engine.CheckText(validCompound);
                Assert.True(hitsOfficial.Count == 0,
                    $"Expected 0 issues for valid productive compound '{validCompound}' in FormalOfficial mode, got: [{string.Join("; ", hitsOfficial.Select(h => $"{h.OriginalText}->{h.Suggestions.FirstOrDefault()?.Replacement}"))}]");

                var hitsGeneral = generalEngine.CheckText(validCompound);
                Assert.True(hitsGeneral.Count == 0,
                    $"Expected 0 issues for valid productive compound '{validCompound}' in GeneralStandard mode, got: [{string.Join("; ", hitsGeneral.Select(h => $"{h.OriginalText}->{h.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            string mpfSentence = "မြန်မာနိုင်ငံရဲတပ်ဖွဲ့သည် ပြည်သူ့လုံခြုံရေးနှင့် ရဲလုပ်ငန်းများကို ဥပဒေနှင့်အညီ ဆောင်ရွက်လျက်ရှိသည်။";
            var mpfIssues = engine.CheckText(mpfSentence);
            Assert.True(mpfIssues.Count == 0,
                $"Expected 0 issues on Myanmar Police Force sentence, got: [{string.Join("; ", mpfIssues.Select(h => $"{h.OriginalText}->{h.Suggestions.FirstOrDefault()?.Replacement}"))}]");

            // 2. Genuine Compound & Standalone Typos MUST remain 100% caught and never corrupt clean modifiers
            static string ApplyTopFix(string input, ProofingIssue issue)
            {
                string rep = issue.Suggestions[0].Replacement;
                return input.Substring(0, issue.StartIndex) + rep + input.Substring(issue.StartIndex + issue.Length);
            }

            (string Typo, string ExpectedRepairedWord)[] genuineTypos =
            {
                ("ရေလုပ်ငန်", "ရေလုပ်ငန်း"),
                ("လယ်လုပ်ငန်", "လယ်လုပ်ငန်း"),
                ("ရဲလုပ်ငန်", "ရဲလုပ်ငန်း"),
                ("ဆေးလုပ်ငန်", "ဆေးလုပ်ငန်း"),
                ("မနက်ဖန်", "မနက်ဖြန်"),
                ("ကျန်းမာရေ", "ကျန်းမာရေး"),
                ("စီပွားရေး", "စီးပွားရေး")
            };

            foreach (var (typo, expectedRepaired) in genuineTypos)
            {
                var typoHits = engine.CheckText(typo);
                Assert.True(typoHits.Count >= 1, $"Expected typo '{typo}' to be flagged, but got 0 issues.");
                Assert.NotEmpty(typoHits[0].Suggestions);
                string repaired = ApplyTopFix(typo, typoHits[0]);
                Assert.Equal(expectedRepaired, repaired);
            }

            var noticeHits = engine.CheckText("အသိပေအကြောင်းကြားစာ");
            Assert.Contains(noticeHits, h => h.OriginalText == "အသိပေ" && h.Suggestions.Any(s => s.Replacement == "အသိပေး"));

            // 3. Same-Onset Suggestion Ranking Sanity ('ရ-' queries rank 'ရ-' candidates ahead of cross-consonant 'လ-' candidates)
            var sYe = sugg.GetSuggestions("ရေလုပ်ငန်", 8);
            Assert.NotEmpty(sYe);
            Assert.Equal("ရေလုပ်ငန်း", sYe[0].Replacement);
            int idxYe = sYe.ToList().FindIndex(c => c.Replacement == "ရေလုပ်ငန်း");
            int idxLeInYe = sYe.ToList().FindIndex(c => c.Replacement == "လယ်လုပ်ငန်း");
            if (idxLeInYe >= 0)
                Assert.True(idxYe < idxLeInYe, $"Expected 'ရေလုပ်ငန်း' (#{idxYe}) ahead of 'လယ်လုပ်ငန်း' (#{idxLeInYe}).");

            var sYae = sugg.GetSuggestions("ရဲလုပ်ငန်", 8);
            Assert.NotEmpty(sYae);
            Assert.Equal("ရဲလုပ်ငန်း", sYae[0].Replacement);
            int idxYae = sYae.ToList().FindIndex(c => c.Replacement == "ရဲလုပ်ငန်း");
            int idxLeInYae = sYae.ToList().FindIndex(c => c.Replacement == "လယ်လုပ်ငန်း");
            if (idxLeInYae >= 0)
                Assert.True(idxYae < idxLeInYae, $"Expected 'ရဲလုပ်ငန်း' (#{idxYae}) ahead of 'လယ်လုပ်ငန်း' (#{idxLeInYae}).");

            // 4. Corpus-Wide Dictionary Gloss Integrity across ALL entries in mlc_thatpon.db
            Assert.True(trie.TryGetExactEntries("ရဲလုပ်ငန်း", out var entriesYaeLok) && entriesYaeLok != null && entriesYaeLok.Any(e => e.IsOfficialThatpon));
            Assert.True(trie.TryGetExactEntries("လယ်လုပ်ငန်း", out var entriesLeLok) && entriesLeLok != null && entriesLeLok.All(e => !e.MeaningCue.Contains("နှင့် ငန်း")));
            Assert.True(trie.TryGetExactEntries("ရုံးလုပ်ငန်း", out var entriesYoneLok) && entriesYoneLok != null && entriesYoneLok.All(e => e.MeaningCue.Trim(' ', '။', '.', ';', '၊') != "ရုံးလုပ်ငန်း"));

            var badNganSplits = new List<string>();
            var tautologicalGlosses = new List<string>();
            var unbalancedParens = new List<string>();

            foreach (var entry in trie.GetAllEntries())
            {
                string cue = entry.MeaningCue ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cue))
                    continue;

                if (cue.IndexOf("နှင့် ငန်း ပေါင်းစပ်ဝေါဟာရ", StringComparison.Ordinal) >= 0)
                    badNganSplits.Add($"{entry.Word}: {cue}");

                string strippedCue = cue.Trim(' ', '\t', '။', '၊', '.', ',', ';', ':', '-', '—', '–', '"', '\'');
                if (strippedCue == entry.Word)
                    tautologicalGlosses.Add($"{entry.Word}: {cue}");

                int openP = 0, closeP = 0;
                for (int i = 0; i < cue.Length; i++)
                {
                    if (cue[i] == '(') openP++;
                    else if (cue[i] == ')') closeP++;
                }
                if (openP > closeP)
                    unbalancedParens.Add($"{entry.Word}: {cue}");
            }

            Assert.True(badNganSplits.Count == 0,
                $"Found {badNganSplits.Count} corrupted 'နှင့် ငန်း' splits: {string.Join(" | ", badNganSplits.Take(5))}");
            Assert.True(tautologicalGlosses.Count == 0,
                $"Found {tautologicalGlosses.Count} tautological self-definitions: {string.Join(" | ", tautologicalGlosses.Take(5))}");
            Assert.True(unbalancedParens.Count == 0,
                $"Found {unbalancedParens.Count} unclosed '(' in MeaningCue: {string.Join(" | ", unbalancedParens.Take(5))}");

            // Verify UI DisplayLabel truncation also closes open '(' when cutting at 32 characters
            var sampleCand = new SuggestionCandidate
            {
                Replacement = "လယ်လုပ်ငန်း",
                MeaningCue = "လယ် နှင့် လုပ်ငန်း ပေါင်းစပ်ဝေါဟာရ — လယ် (စပါးစိုက်ပျိုးရာမြေကွက်)"
            };
            string disp = sampleCand.DisplayLabel;
            int dispOpen = disp.Count(c => c == '(');
            int dispClose = disp.Count(c => c == ')');
            Assert.Equal(dispOpen, dispClose);
        }

        [Fact]
        public void V61_WholeCorpusDataValidationAndSanitizationGate()
        {
            // 1. Live UnicodeNormalizer Canonical Reorder & Redundant Medial Detection (V32 Upgrades)
            var liveNormPairs = new (string Raw, string Expected)[]
            {
                ("သီးန\u1036\u103E", "သီးနှံ"),
                ("လ\u1036\u103E\u102F\u1037ဆော်", "လှုံ့ဆော်"),
                ("ရ\u1036\u103D\u103E\u1037", "ရွှံ့"),
                ("ရ\u1036\u103E\u102F\u1038", "ရှုံး"),
                ("အဘိ\u1007\u1039\u1008\u103B\u102C", "အဘိ\u1007\u1039\u1008\u102C"),
                ("\u1008\u103Bေး", "ဈေး")
            };
            foreach (var (raw, expected) in liveNormPairs)
            {
                Assert.True(UnicodeNormalizer.HasLookalikeError(raw, out string fixedForm, out _),
                    $"Expected HasLookalikeError=true for '{raw}'.");
                Assert.Equal(expected, fixedForm);
                Assert.Equal(expected, UnicodeNormalizer.FixLookalikeCharacters(raw));
            }

            // 2. Load Both Master JSON & Encrypted Runtime DB and Verify Curated Domain/Official Sanitization
            var master = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());

            string[] requiredCanonicalWords =
            {
                "ကြေးငွေစနစ်", "ငွေကြေးစနစ်", "တရားဝင်ပိုင်ရှင်", "တရားဝင်ပေးခြင်း",
                "ဒွါရ", "ဘာရဒွါဇ", "အယ်လ်ဂိုရီသမ်", "ကုက္ကို", "ရွှေကုက္ကို",
                "လွမ်ပရာဘမ်", "ဂွာတီမာလာ", "နီကာရာဂွာ", "မာနာဂွာ", "အန်တီဂွာနှင့်ဘာဘူဒါ",
                "အာရောဂျံ", "အာယုဝဍ္ဎနဆေး", "အဘိဇ္ဈာ", "ဪ",
                "ရဲလုပ်ငန်း", "လယ်လုပ်ငန်း", "ရုံးလုပ်ငန်း"
            };
            foreach (string w in requiredCanonicalWords)
            {
                Assert.True(trie.ContainsExactWord(w), $"Expected sanitized canonical headword '{w}' in active lexicon.");
            }

            string[] forbiddenCondemnedOrJunkWords =
            {
                "ကြေးငွေစံနစ်", "ငွေကြေးစံနစ်", "တရားဝင်ပိုင်သျှင်", "ဒွာရ",
                "ချော့ကလက်", "ကုက္ကိုလ်", "ရွှေကုက္ကိုလ်", "အာရောဂ္ဃျံ", "အဘိဇ္ဈျာ",
                "စောက်ရေး", "တို့အဖွဲ့", "တို့အရေး", "ဆိုင်ရာဥပဒေ", "ရေးလုပ်ငန်း"
            };
            foreach (string bad in forbiddenCondemnedOrJunkWords)
            {
                Assert.False(trie.ContainsExactWord(bad), $"Condemned/junk form '{bad}' must NOT exist in active lexicon.");
            }

            Assert.True(trie.TryGetExactEntries("လယ်လုပ်ငန်း", out var leEntries) && leEntries != null);
            Assert.All(leEntries, e =>
            {
                Assert.DoesNotContain("အလယ် အလတ်", e.MeaningCue ?? "");
                Assert.DoesNotContain("နှင့် ငန်း", e.MeaningCue ?? "");
            });

            // 3. Whole-Corpus 360° Zero-Defect Audit across ALL Entries, Rules, ContextRules, and Bigrams
            var badSubseqRx = new System.Text.RegularExpressions.Regex(
                @"(?:[\u102B-\u1030\u1032\u1036][\u103B-\u103E]|\u103A\u103A|\u1039\u1039|\u1031\u1031|\u102D\u102E|\u102E\u102D|\u102F\u1030|\u1030\u102F|\u1037\u103A|\u1038\u103A|\u1038[\u102B-\u103E]|\u1036[\u102D-\u1030]|[\u102B-\u1038\u103A]\u103C|\u1008\u103B|\u100B\u103F\u100C)",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            var corruptedArtifactRx = new System.Text.RegularExpressions.Regex(
                @"idmn|fidml|coidmb|Aidmj|syidmb|sidmn|sidmg|Bridm|cheidm|\[\[|\]\]|[\uE000-\uF8FF]",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            var degreeHalfRx = new System.Text.RegularExpressions.Regex(@"[၀-၉0-9]\s*½", System.Text.RegularExpressions.RegexOptions.Compiled);
            var ethnicScriptOrLabelRx = new System.Text.RegularExpressions.Regex(
                @"[\u105A-\u109F\uAA60-\uAA7F]|(?:မွန်|ရှမ်း|ကရင်|ပအိုဝ်း|တအာင်း|ကယား|ပလောင်)(?:\s*[-–—၊:]|ဘာသာ|စကား|ဝေါဟာရ|အခေါ်)",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            var usagePronHeaderRx = new System.Text.RegularExpressions.Regex(@"(?:^|\|\s*)အသံထွက်:", System.Text.RegularExpressions.RegexOptions.Compiled);

            var activeWords = new HashSet<string>(StringComparer.Ordinal);
            var defects = new List<string>();

            foreach (var e in master.Entries)
            {
                string pos = e.PosTag ?? "";
                bool isDeferred = pos == "EN_LEX" || pos == "ENCYC" || pos == "PROVERB" || pos == "SAYING" || pos == "RHETORIC";
                if (!isDeferred && e.Word.IndexOf(' ') < 0)
                {
                    activeWords.Add(e.Word);
                    if (UnicodeNormalizer.HasLookalikeError(e.Word, out _, out string desc))
                        defects.Add($"HW Lookalike '{e.Word}': {desc}");
                    if (badSubseqRx.IsMatch(e.Word))
                        defects.Add($"HW BadSubseq '{e.Word}'");
                    if (UnicodeNormalizer.FindZawgyiSpans(e.Word).Count > 0)
                        defects.Add($"HW Zawgyi '{e.Word}'");
                }

                string mc = e.MeaningCue ?? "";
                string un = e.UsageNote ?? "";
                bool hasEthnic = ethnicScriptOrLabelRx.IsMatch(mc) || ethnicScriptOrLabelRx.IsMatch(un);

                string mcTrim = mc.Trim(' ', '။', '၊', '.', '"', '\'');
                if (mcTrim.Length > 0 && string.Equals(mcTrim, e.Word, StringComparison.Ordinal))
                    defects.Add($"Tautology MeaningCue '{e.Word}': {mc}");

                if (badSubseqRx.IsMatch(mc))
                    defects.Add($"Cue BadSubseq '{e.Word}': {mc}");
                if (!hasEthnic && UnicodeNormalizer.FindZawgyiSpans(mc).Count > 0)
                    defects.Add($"Cue Zawgyi '{e.Word}': {mc}");

                int depth = 0;
                bool negParen = false;
                for (int ci = 0; ci < mc.Length; ci++)
                {
                    if (mc[ci] == '(') depth++;
                    else if (mc[ci] == ')') { depth--; if (depth < 0) negParen = true; }
                }
                if (depth != 0 || negParen)
                    defects.Add($"Unbalanced Paren '{e.Word}': {mc}");

                if (badSubseqRx.IsMatch(un))
                    defects.Add($"Note BadSubseq '{e.Word}': {un}");
                if (!hasEthnic && UnicodeNormalizer.FindZawgyiSpans(un).Count > 0)
                    defects.Add($"Note Zawgyi '{e.Word}': {un}");
                if (usagePronHeaderRx.Matches(un).Count > 1)
                    defects.Add($"Dup Pron '{e.Word}': {un}");

                if (corruptedArtifactRx.IsMatch(mc) || corruptedArtifactRx.IsMatch(un))
                    defects.Add($"Artifact/PUA/Wiki '{e.Word}': {mc}");
                if (mc.Contains("\"\"") || un.Contains("\"\"") || mc.Contains("==") || un.Contains("==") ||
                    mc.Contains("။)။") || un.Contains("။)။") || mc.Contains("။။") || un.Contains("။။") ||
                    degreeHalfRx.IsMatch(mc) || degreeHalfRx.IsMatch(un))
                {
                    defects.Add($"Formatting Defect '{e.Word}': {mc}");
                }

                bool isSynthCue = mc.Contains("ပေါင်းစပ်ဝေါဟာရ —") ||
                                  ((mc.Contains("ခြင်းဆိုင်ရာ ") || mc.Contains("သည့် အပြုအမူ") || mc.Contains("သည့် ကိစ္စရပ်") || mc.Contains("ဆိုင်ရာ ရုံးဌာန")) && mc.EndsWith(")။", StringComparison.Ordinal));
                if (isSynthCue && mc.Contains("~"))
                    defects.Add($"Unexpanded Tilde '{e.Word}': {mc}");
            }

            var condemnedSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in master.OrthographyRules)
            {
                condemnedSet.Add(r.WrongForm);
                if (activeWords.Contains(r.WrongForm))
                    defects.Add($"Rule WrongForm in Active Lexicon: '{r.WrongForm}'");
                if (r.CorrectForm.IndexOf(' ') < 0 && !activeWords.Contains(r.CorrectForm))
                    defects.Add($"Rule CorrectForm Missing from Active Lexicon: '{r.CorrectForm}'");
                if (UnicodeNormalizer.HasLookalikeError(r.CorrectForm, out _, out string rDesc) || badSubseqRx.IsMatch(r.CorrectForm))
                    defects.Add($"Rule CorrectForm Defect '{r.CorrectForm}': {rDesc}");
                string maskedExpl = (r.Explanation ?? "").Replace("('င်္')", "က").Replace("'င်္'", "က");
                if (!string.IsNullOrEmpty(r.WrongForm)) maskedExpl = maskedExpl.Replace(r.WrongForm, "က");
                if (badSubseqRx.IsMatch(maskedExpl) || UnicodeNormalizer.FindZawgyiSpans(maskedExpl).Count > 0)
                    defects.Add($"Rule Explanation Defect '{r.WrongForm}': {r.Explanation}");
            }

            foreach (var cr in master.ContextRules)
            {
                if (UnicodeNormalizer.HasLookalikeError(cr.CorrectForm, out _, out string cDesc) || badSubseqRx.IsMatch(cr.CorrectForm))
                    defects.Add($"CtxRule CorrectForm Defect '{cr.CorrectForm}': {cDesc}");
                string maskedExpl = (cr.Explanation ?? "").Replace("('င်္')", "က").Replace("'င်္'", "က");
                if (!string.IsNullOrEmpty(cr.WrongForm)) maskedExpl = maskedExpl.Replace(cr.WrongForm, "က");
                if (badSubseqRx.IsMatch(maskedExpl) || UnicodeNormalizer.FindZawgyiSpans(maskedExpl).Count > 0)
                    defects.Add($"CtxRule Explanation Defect '{cr.WrongForm}': {cr.Explanation}");
                if (badSubseqRx.IsMatch(cr.TriggerWords ?? "") || badSubseqRx.IsMatch(cr.AntiTriggerWords ?? ""))
                    defects.Add($"CtxRule Trigger Defect '{cr.WrongForm}': {cr.TriggerWords}");
            }

            foreach (var bg in master.WordBigrams)
            {
                if (condemnedSet.Contains(bg.LeftWord) || condemnedSet.Contains(bg.RightWord) ||
                    badSubseqRx.IsMatch(bg.LeftWord) || badSubseqRx.IsMatch(bg.RightWord))
                {
                    defects.Add($"Bigram Defect: ({bg.LeftWord}, {bg.RightWord})");
                }
            }

            Assert.True(defects.Count == 0,
                $"Expected 0 whole-corpus validation defects across {master.Entries.Count} entries, {master.OrthographyRules.Count} rules, {master.ContextRules.Count} context rules, and {master.WordBigrams.Count} bigrams, but found {defects.Count}: {string.Join(" || ", defects.Take(8))}");
        }
    }
}
