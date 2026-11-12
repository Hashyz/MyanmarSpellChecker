using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
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

            // 2. Verify repo-root legal & citation files exist and preserve author, academic & location attribution
            string noticeText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "NOTICE"));
            Assert.Contains("Htet Yar Zar", noticeText);
            Assert.Contains("ထက်ရာဇာ", noticeText);
            Assert.Contains("M.Sc. Part II Candidate in Geology", noticeText);
            Assert.Contains("Kyaukse University", noticeText);
            Assert.Contains("Kyaukse, Mandalay Region, Myanmar", noticeText);

            string citationText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "CITATION.cff"));
            Assert.Contains("Htet Yar Zar", citationText);
            Assert.Contains("Kyaukse University", citationText);

            // 3. Verify Encrypted DB & Proofing Engine include Kyaukse University + Geology & Earth Sciences Lexicon
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie(MyanmarSpellChecker.Core.Security.AntiTamperGuard.ReconstructProtectedFallbackKey());
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

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
    }
}
