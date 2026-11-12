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
using MyanmarSpellChecker.Core.Tests.Infrastructure;
using MyanmarSpellChecker.Core.Typography;
using Xunit;

namespace MyanmarSpellChecker.Core.Tests
{
    public class ThatponBookAndLexiconTests
    {
        [Fact]
        public void DeepPhonologyAndHonorificDisambiguation_CatchesScreenshotAndPhoneticErrors_WhileProtectingRealPersonNames()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            var deepPhoneticCases = new (string Input, string ExpectedWrong, string ExpectedReplacement)[]
            {
                // Exact user screenshot lines
                ("စံပင်", "စံပင်", "ဆံပင်"),
                ("ဦးဂေါင်း", "ဦးဂေါင်း", "ဦးခေါင်း"),
                ("ဦီးခေါင်း", "ဦီးခေါင်း", "ဦးခေါင်း"),
                ("ဦီးအောင်ကျော် လာပါသည်။", "ဦီး", "ဦး"),
                // Honorific-prefixed common nouns/compounds
                ("ဦးနောက် မှတ်ဉာဏ် ကောင်းသည်။", "ဦးနောက်", "ဦးနှောက်"),
                ("ဦးထုတ် ဆောင်းထားသည်။", "ဦးထုတ်", "ဦးထုပ်"),
                ("စီမံကိန်းဦးစီဌာနသို့ တင်ပြသည်။", "ဦးစီဌာန", "ဦးစီးဌာန"),
                // Legacy WinInnwa visual-order & stacked subscript Tall AA
                ("ယောကျာ်းလေး တစ်ယောက် လာသည်။", "ယောကျာ်း", "ယောက်ျား"),
                ("အဓိပ္ပါယ် ပြည့်စုံစွာ ရှင်းပြသည်။", "အဓိပ္ပါယ်", "အဓိပ္ပာယ်"),
                // Omitted & unstacked Kinzi
                ("အကျီ အသစ် ဝတ်ထားသည်။", "အကျီ", "အင်္ကျီ"),
                ("အင်ကျီ အသစ် ဝတ်ထားသည်။", "အင်ကျီ", "အင်္ကျီ"),
                ("သကြန်ပွဲတော် ကျင်းပမည်။", "သကြန်", "သင်္ကြန်"),
                // Sesquisyllabic weak-syllable reduction & intervocalic voicing
                ("လဖက်ရည် သောက်မယ်။", "လဖက်ရည်", "လက်ဖက်ရည်"),
                ("စဂါး ပြောဆိုရာတွင် ယဉ်ကျေးပါ။", "စဂါး", "စကား"),
                ("တခါး ပိတ်ထားပါ။", "တခါး", "တံခါး"),
                ("တဂါး ပိတ်ထားပါ။", "တဂါး", "တံခါး"),
                ("ပုဇွန် ဟင်းချက်စားမယ်။", "ပုဇွန်", "ပုစွန်"),
                ("ပိုက်စံ စုဆောင်းထားပါ။", "ပိုက်စံ", "ပိုက်ဆံ"),
                // Purified crowd-sourced corpus typos
                ("လေယာဉ်လတ်မှတ် ဝယ်ယူထားသည်။", "လတ်မှတ်", "လက်မှတ်"),
                ("အထူးလျော့ဈေး ချပေးနေပါသည်။", "လျော့ဈေး", "လျှော့ဈေး"),
                ("နားကြပ် တပ်ပြီး နားထောင်သည်။", "နားကြပ်", "နားကျပ်")
            };

            foreach (var tc in deepPhoneticCases)
            {
                var issues = engine.CheckText(tc.Input);
                Assert.True(issues.Any(iss =>
                    iss.OriginalText.Contains(tc.ExpectedWrong) &&
                    iss.Suggestions.Count > 0 &&
                    iss.Suggestions[0].Replacement.Contains(tc.ExpectedReplacement)),
                    $"Failed on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedReplacement}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Valid words & real Burmese personal names after honorifics must produce ZERO false positives!
            string[] validInputs =
            {
                "ဆံပင်",
                "ဦးခေါင်း",
                "ဦးအောင်ကျော်မိုးနှင့် ဒေါ်ခင်မေသန်းတို့ ရောက်ရှိလာသည်။",
                "ဦးသန်းနှင့် ဦးရီတို့သည် ကိုရှင်းသန့်အား တွေ့ဆုံခဲ့သည်။",
                "ဈေးပင် မဖွင့်သေးပါ။",
                "သဘောထား မှန်ကန်စွာဖြင့် ယောက်ျားပီသစွာ ပြောဆိုသည်။",
                "အင်္ကျီသစ် ဝတ်ပြီး လက်ဖက်ရည်ဆိုင်တွင် စကားပြောနေကြသည်။"
            };

            foreach (string valid in validInputs)
            {
                var fp = engine.CheckText(valid);
                Assert.True(fp.Count == 0, $"Unexpected false positive in '{valid}': {string.Join(", ", fp.Select(x => x.OriginalText))}");
            }
        }

        [Fact]
        public void FullThatponBookV4AndContextTraining_ValidatesCollocationsVariantsStemsAndPurity()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);

            // Verify 37,000+ unique words & 17,500+ official Thatpon headwords
            Assert.True(trie.WordCount >= 36500, $"Expected >= 36,500 unique words, got {trie.WordCount}");
            Assert.True(model.Entries.Count(e => e.IsOfficialThatpon) >= 17500);

            // Verify newly unlocked Thatpon collocations, parenthesized variants, classifier stems, and Kinzi/Tall-AA fixes
            string[] mustExistOfficialWords =
            {
                "ဆီကတော့", "ရေကရား", "ပန်းကုံး", "ခေါင်းအုံး", "မိုးအုံ့", "လုံးဝဥဿုံ", "လုံ့လဥဿာဟ",
                "ရှိခိုးကော်ရော်", "ဆုံးမကံမြစ်", "ကြုံရာကျပန်း", "ကျပန်းစကားပြောပွဲ",
                "ကသောကမျော", "သတွတ်ပင်", "ကုတ်ဟည်းပင်", "ဥပါပျံ",
                "ကကုသန်", "ကတညုတ", "ကရကဋ်", "ကမာကျီ", "ကရမက်", "အဿတိုရ်",
                "ရှပ်အင်္ကျီ", "ချွေးခံအင်္ကျီ", "ထိုင်မသိမ်းအင်္ကျီ", "အင်္ဂါနေ့", "ဒင်္ဂါး", "သင်္ဂါယနာ"
            };
            foreach (string w in mustExistOfficialWords)
            {
                Assert.True(trie.ContainsExactWord(w), $"Expected official Thatpon word '{w}' in Trie!");
            }

            // Verify Purity Skeleton blocked non-Thatpon homophone typos like 'နေ့လည်'
            Assert.False(trie.ContainsExactWord("နေ့လည်"), "'နေ့လည်' must never enter valid Trie (official is 'နေ့လယ်')!");

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            var v4ErrorCases = new (string Input, string ExpectedWrong, string ExpectedReplacement)[]
            {
                ("နေ့လည်စာ စားပြီးနောက် နားနေသည်။", "နေ့လည်", "နေ့လယ်"),
                ("အိပ်ယာမှ စောစောထသည်။", "အိပ်ယာ", "အိပ်ရာ"),
                ("သဘာဝပတ်ဝန်းကျင်ကို ထိမ်းသိမ်းရမည်။", "ထိမ်းသိမ်း", "ထိန်းသိမ်း"),
                ("လက်ထပ်ထိန်းမြားမင်္ဂလာ ကျင်းပသည်။", "ထိန်းမြား", "ထိမ်းမြား"),
                ("ဧည့်သည်များကို ရင်းနှီးစွာ နုတ်ဆက်သည်။", "နုတ်ဆက်", "နှုတ်ဆက်"),
                ("ရာထူးမှ နှုတ်ထွက်ခွင့် တင်ပြသည်။", "နှုတ်ထွက်", "နုတ်ထွက်"),
                ("စာအုပ်ကို သေချာစွာ ဖက်သည်။", "ဖက်", "ဖတ်"),
                ("စာရင်းထဲသို့ အချက်အလက်များ ထည်သွင်းသည်။", "ထည်သွင်း", "ထည့်သွင်း"),
                ("ဆုတံဆိပ် ချီးမြင့်ပေးအပ်သည်။", "ချီးမြင့်", "ချီးမြှင့်"),
                ("ပြည်ထောင်စုလွတ်တော် အစည်းအဝေး ကျင်းပသည်။", "လွတ်တော်", "လွှတ်တော်"),
                ("ညွန်ကြားရေးမှူးချုပ်က လမ်းညွှန်သည်။", "ညွန်ကြား", "ညွှန်ကြား"),
                ("ပန်းကုန်း လှလှလေး သီကုံးထားသည်။", "ပန်းကုန်း", "ပန်းကုံး"),
                ("လုံးဝဥသုံ ပျက်စီးသွားသည်။", "လုံးဝဥသုံ", "လုံးဝဥဿုံ"),
                ("အင်္ဂာနေ့တွင် ခောင်းဆောင် ရောက်လာသည်။", "အင်္ဂာ", "အင်္ဂါ"),
                ("ခောင်းဆောင်က အမှာစကား ပြောကြားသည်။", "ခောင်း", "ခေါင်း"),
                ("ဆော့ဝ်ဖဲ အသစ်ကို ကွန်ပြူတာတွင် ထည့်သွင်းသည်။", "ဆော့ဝ်ဖဲ", "ဆော့ဖ်ဝဲ")
            };

            foreach (var tc in v4ErrorCases)
            {
                var issues = engine.CheckText(tc.Input);
                Assert.True(issues.Any(iss =>
                    iss.OriginalText.Contains(tc.ExpectedWrong) &&
                    iss.Suggestions.Count > 0 &&
                    iss.Suggestions[0].Replacement.Contains(tc.ExpectedReplacement)),
                    $"Failed V4 check on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedReplacement}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Ensure valid counterparts produce ZERO false positives!
            string[] validV4Sentences =
            {
                "နှုတ်ထွက်စကား တစ်ခွန်းသည် အလွန်အရေးကြီးသည်။",
                "အင်္ဂါနေ့တွင် ရှပ်အင်္ကျီ အဖြူ ဝတ်ဆင်သည်။",
                "အစည်းအဝေး ဖွင့်လှစ်ပြီးနောက် ထမင်းစားပြီး ဆေးသောက်သည်။",
                "လုံ့လဥဿာဟ စိုက်ထုတ်၍ လုံးဝဥဿုံ အောင်မြင်ခဲ့သည်။"
            };
            foreach (string valid in validV4Sentences)
            {
                var fp = engine.CheckText(valid);
                Assert.True(fp.Count == 0, $"Unexpected V4 false positive in '{valid}': {string.Join(", ", fp.Select(x => x.OriginalText))}");
            }
        }

        [Fact]
        public void AspectParticleChains_DroppedAsat_AndBoundSyllables_CatchesScreenshotAndEverydayErrors()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            var errorCases = new (string Input, string ExpectedWrong, string ExpectedTop)[]
            {
                // Exact user screenshot lines
                ("ထမင်းစားပြီးပြီးမလား", "ပြီးပြီးမလား", "ပြီးပြီလား"),
                ("မန်းမန်းစားပြီးပီလား", "မန်းမန်း", "ထမင်း"),
                ("မန်းမန်းစားပြီးပီလား", "ပြီးပီ", "ပြီးပြီ"),
                ("မန်မန် စားပီးပီးလား", "မန်မန်", "ထမင်း"),
                ("မန်မန် စားပီးပီးလား", "ပီးပီး", "ပြီးပြီ"),
                ("ထမင်း စားပီးပီလား", "ပီးပီ", "ပြီးပြီ"),
                ("ထမင်းစားပြီးပီလား", "ပြီးပီ", "ပြီးပြီ"),
                // General aspect/particle/dropped-asat/bound-syllable patterns
                ("ထမင်းစားပြီးပြီးလား", "ပြီးပြီး", "ပြီးပြီ"),
                ("ရောက်ပါပီ", "ပါပီ", "ပါပြီ"),
                ("သွားပီနော်", "ပီ", "ပြီ"),
                ("ဟုတ်ပီလား", "ပီ", "ပြီ"),
                ("သိပီကွာ", "ပီ", "ပြီ"),
                ("စားပြီ သွားမယ်။", "ပြီ", "ပြီး"),
                ("စားပြီတော့ သွားမယ်။", "ပြီ", "ပြီး"),
                ("စားပြီရင် ပြောပါ။", "ပြီ", "ပြီး"),
                ("စားအုံးမယ်။", "စားအုံး", "စားဦး"),
                ("ကြည့်ပါအုံး။", "ပါအုံး", "ပါဦး"),
                ("ငယ်ငယ်တုံးက သွားခဲ့တယ်။", "တုံး", "တုန်း"),
                ("ဘာလုပ်နေတာတုံး။", "တုံး", "တုန်း"),
                ("မသိဖူး။", "ဖူး", "ဘူး"),
                ("မစားရသေးဖူး။", "ဖူး", "ဘူး"),
                ("အိမ်မာ ရှိတယ်။", "မာ", "မှာ"),
                ("သူနဲ သွားမယ်။", "နဲ", "နဲ့"),
                ("သွားတယ။", "တယ", "တယ်"),
                ("လာမယ။", "မယ", "မယ်"),
                ("သွားသည။", "သည", "သည်"),
                ("သွားကျောင်း ပြောတယ်။", "ကျောင်း", "ကြောင်း"),
                ("ထမင်းစားပြီးပြီလာ။", "လာ", "လား"),
                // User screenshot 'ဟုလား' vs 'ဟုတ်လား' & spoken coda/negation/homophone family
                ("ဟုလား", "ဟုလား", "ဟုတ်လား"),
                ("ဟုကဲ့", "ဟုကဲ့", "ဟုတ်ကဲ့"),
                ("ဟုတယ်", "ဟုတယ်", "ဟုတ်တယ်"),
                ("ဟုပါတယ်", "ဟုပါတယ်", "ဟုတ်ပါတယ်"),
                ("ဟုပီ", "ဟုပီ", "ဟုတ်ပြီ"),
                ("မဟုဘူး", "မဟုဘူး", "မဟုတ်ဘူး"),
                ("မဟုလား", "မဟုလား", "မဟုတ်လား"),
                ("ဒါမှမဟု", "ဒါမှမဟု", "ဒါမှမဟုတ်"),
                ("ဟုတ်လာ", "လာ", "လား"),
                ("ဟုတ်ဘူး", "ဟုတ်ဘူး", "မဟုတ်ဘူး"),
                ("သိဘူး", "သိဘူး", "မသိဘူး"),
                ("ဘယ်သွားမလို့လည်း", "လည်း", "လဲ"),
                ("ဘယ်တုံးကလဲ", "တုံး", "တုန်း"),
                ("ပြောပြအုံး", "ပြအုံး", "ပြဦး"),
                ("အစဉ်ပြေလား", "အစဉ်ပြေ", "အဆင်ပြေ"),
                ("ခနစောင့်ပါ", "ခန", "ခဏ"),
                ("နားလဲလား", "နားလဲ", "နားလည်")
            };

            foreach (var tc in errorCases)
            {
                var issues = engine.CheckText(tc.Input);
                Assert.True(issues.Any(iss =>
                    iss.OriginalText.Contains(tc.ExpectedWrong) &&
                    iss.Suggestions.Count > 0 &&
                    iss.Suggestions[0].Replacement.Contains(tc.ExpectedTop)),
                    $"Failed on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedTop}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Valid counterparts must produce ZERO false positives!
            string[] validCounterparts =
            {
                "ထမင်းစားပြီးပြီလား",
                "ဟုတ်လား",
                "ဟုတ်ကဲ့",
                "စားပြီကွာ",
                "ပြီးပြီးရော မလုပ်ပါနဲ့။",
                "မစားရသေးဘူး။",
                "ပီလားကောင် မွေးမြူသည်။",
                "လက သာသည်။",
                "ခေါင်းအုံးနှင့် အုန်းသီး ဝယ်လာသည်။",
                "ရာဟုလာနှင့် ဗဟုသုတ ရှိသည်။",
                "သွားမည်ဟု ပြောကြားသည်။",
                "ဘာပြောပြော သူလည်း နားမထောင်ဘူး။",
                "၁/မခန(နိုင်)၁၂၃၄၅၆"
            };
            foreach (string valid in validCounterparts)
            {
                var fp = engine.CheckText(valid);
                Assert.True(fp.Count == 0, $"Unexpected false positive in '{valid}': {string.Join(", ", fp.Select(x => x.OriginalText))}");
            }
        }

        [Fact]
        public void ProofingEngine_V5_MultiCorpusBigramsCollocationsClassifiersTenseAndZeroWidth_WorkAccurately()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            // 1. Verify Multi-Corpus Scale (50K+ words, 17.5K+ Official Thatpon, audited homophone ContextRules [synthetic collocations.yaml purged!], 35K+ WordBigrams)
            Assert.True(model.Entries.Count >= 50000, $"Expected >= 50,000 lexicon entries, got {model.Entries.Count}");
            Assert.True(model.Entries.Count(e => e.IsOfficialThatpon) >= 17500);
            Assert.True(model.OrthographyRules.Count >= 600, $"Expected >= 600 orthography rules, got {model.OrthographyRules.Count}");
            Assert.True(model.ContextRules.Count >= 4, $"Expected >= 4 audited orthographic contextual rules, got {model.ContextRules.Count}");
            Assert.True(model.WordBigrams.Count >= 35000, $"Expected >= 35,000 word bigrams, got {model.WordBigrams.Count}");

            // 2. Verify MMTHATPON4 AES-256 + HMAC-SHA256 round-trip preserves all 4 tables
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_v5_{Guid.NewGuid():N}.db");
            MyanmarTrie trie;
            try
            {
                var repo = new EncryptedDictionaryRepository(tempDb);
                repo.CompileEncryptedDatabase(
                    "V5-Enterprise-Test-Key-2026!",
                    model.Entries,
                    model.OrthographyRules,
                    model.ContextRules,
                    model.WordBigrams);

                trie = repo.LoadIntoTrie("V5-Enterprise-Test-Key-2026!");
                Assert.Equal(model.ContextRules.Count, trie.ContextualRuleCount);
                Assert.True(trie.BigramCount >= 35000);
            }
            finally
            {
                if (File.Exists(tempDb))
                    File.Delete(tempDb);
            }

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 3. Verify Zawgyi-One -> Pyidaungsu Unicode conversion fixes cascading medials/asat & Kinzi
            string zawgyiSample = "\u1019\u1002\u1064\u101C\u102C\u1015\u102B"; // Zawgyi 'မင်္ဂလာပါ'
            Assert.True(UnicodeNormalizer.IsZawgyiText(zawgyiSample));
            Assert.Equal("မင်္ဂလာပါ", UnicodeNormalizer.ConvertZawgyiToUnicode(zawgyiSample));

            // 4. Verify V5 Error Detection: Zero-width corruption, stray coda vowel, reversed medials,
            //    space-split compounds, classifier agreement, tense clash, doubled SFP, nominalizer -မူ->-မှု,
            //    and data-driven collocations & homophones!
            var v5Cases = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("ငြိမ်း\u200Bချမ်းမှု ရရှိပါစေ။", "ငြိမ်း\u200Bချမ်းမှု", "ငြိမ်းချမ်းမှု"),
                ("မိန်ိးမ တစ်ယောက် လာသည်။", "မိန်ိးမ", "မိန်းမ"),
                ("လှျို့ဝှက် အချက်အလက် ထိန်းသိမ်းပါ။", "လှျို့", "လျှို့"),
                ("စာ ကြည့်တိုက်တွင် စာဖတ်သည်။", "စာ ကြည့်တိုက်", "စာကြည့်တိုက်"),
                ("အစည်း အဝေးသို့ တက်ရောက်သည်။", "အစည်း အဝေး", "အစည်းအဝေး"),
                ("ဖိနပ် နှစ်ယောက် ဝယ်လာသည်။", "ယောက်", "ရံ"),
                ("ကော်မတီဝင် သုံးကောင် တက်ရောက်သည်။", "ကောင်", "ဦး"),
                ("ကွန်ပျူတာ နှစ်ထည် ဝယ်ယူသည်။", "ထည်", "လုံး"),
                ("မနက်ဖြန် အစည်းအဝေး ကျင်းပခဲ့သည်။", "ခဲ့သည်", "မည်"),
                ("မနေ့က စာအုပ် ဝယ်ပါမည်။", "ပါမည်", "ခဲ့ပါသည်"),
                ("အစီရင်ခံစာ တင်ပြပါသည်သည်။", "သည်သည်", "သည်"),
                ("လုပ်ငန်း ထိရောက်မူ ရှိစေရန် ဆောင်ရွက်သည်။", "မူ", "မှု"),
                ("ခွေးကလေးကို အစာ ကြွေးထားသည်။", "ကြွေး", "ကျွေး"),
                ("ဘဏ်တွင် ကျွေးမြီ ပြန်ဆပ်သည်။", "ကျွေးမြီ", "ကြွေးမြီ"),
                ("စက်ရုံအစား ဆက်ရုံ ဟု မရေးရ။", "ဆက်ရုံ", "စက်ရုံ"),
                ("စွမ်းဆောင်ရည တိုးတက်စေရန် ဆောင်ရွက်သည်။", "စွမ်းဆောင်ရည", "စွမ်းဆောင်ရည်"),
                ("အခက်ခဲ ရှိနေလို့ ကူညီပါ။", "အခက်ခဲ", "အခက်အခဲ"),
                ("နှစ်ပေါင်းများစွာ ကြုိးပမ်းခဲ့ကြသည်။", "ကြုိး", "ကြိုး"),
                ("သူ့ကို နားထောင်္ပါ။", "ထောင်္", "ထောင်")
            };

            foreach (var tc in v5Cases)
            {
                string expWrongNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedWrong);
                string expFixNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedFix);
                var issues = engine.CheckText(tc.Input);
                Assert.True(issues.Any(iss =>
                    (iss.OriginalText.Contains(tc.ExpectedWrong) ||
                     UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText).Contains(expWrongNorm)) &&
                    iss.Suggestions.Count > 0 &&
                    iss.Suggestions.Any(s =>
                        s.Replacement.Contains(tc.ExpectedFix) ||
                        UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement).Contains(expFixNorm))),
                    $"Failed V5 check on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedFix}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 5. Verify V5 Clean-Sentence False-Positive Immunity (quotative 'ဟု', 'ကနေ', 'ပါ့မယ်',
            //    induced creaky tone 'မြန်မာ့/ကမ္ဘာ့/ကျွန်တော့်', demonstrative 'ဒီနေရာ', verbal auxiliaries 'ကြီးသွား', modern IT terms)
            string[] v5CleanSentences =
            {
                "ဆရာက ကြိုးစားရမည် ဟု ပြောကြားသည်။",
                "ရန်ကုန်ကနေ မန္တလေးကို သွားမယ့် ရထား ဖြစ်ပါတယ်။",
                "ကျွန်တော် တတ်နိုင်သလောက် ကူညီပါ့မယ်။",
                "မြန်မာ့ ရိုးရာ ယဉ်ကျေးမှုနှင့် ကမ္ဘာ့ စီးပွားရေး အခြေအနေကို လေ့လာသည်။",
                "ဒီနေရာမှာ အပင်တွေ အရမ်း ကြီးသွားတယ်။",
                "မူဝါဒ အသစ်အရ ဆော့ဖ်ဝဲလ်နှင့် ဒေတာဘေ့စ် စနစ်ကို အဆင့်မြှင့်တင်သည်။",
                "ကျွန်ုပ်တို့ ငယ်စဉ်က သဒ္ဓါတရား ထက်သန်စွာ လှူဒါန်းရမည်ဖြစ်သည်။"
            };

            foreach (string clean in v5CleanSentences)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0, $"Unexpected V5 false positive in '{clean}': {string.Join(", ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void Whole462PageThatponBookV6_EnforcesKhweHtarArrowRules_Ideophones_PrefaceTaTasit_AndHomophoneCues()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            // 1. Verify V6 462-Page Complete Book scale (54,900+ unique words, 19,900+ Official Thatpon, 750+ rules)
            Assert.True(trie.WordCount >= 54500, $"Expected >= 54,500 unique words in V6, got {trie.WordCount}");
            Assert.True(model.Entries.Where(e => e.IsOfficialThatpon).Select(e => e.Word).Distinct().Count() >= 19800);
            Assert.True(model.OrthographyRules.Count >= 750, $"Expected >= 750 orthography rules in V6, got {model.OrthographyRules.Count}");

            // 2. Verify newly unlocked Part 2 'ခွဲထား' (Pages 336..462) Official Book compounds & headwords exist in Trie
            string[] khweHtarOfficialWords =
            {
                "ကန်စွန်းရွက်", "ကန်စွန်းဥ", "ကန့်လန့်ကာ", "ကတိစာချုပ်", "ကန့်သတ်",
                "ခုံးကျော်တံတား", "ချင်းသုပ်", "ချိုင်းထောက်", "စံကားဝါ", "စံပယ်ပင်",
                "ဆက်ရက်ငှက်", "ထဘီ", "ထမနဲ", "နှုန်းနှိုင်း", "ပွဲ့လျက်",
                "ဘာသိဘာသာ", "ဖြည်းဖြည်းဆေးဆေး", "ဆေးဘူး", "ဘူးသီး", "အမြွှာပူး",
                "သူကောင်းသား", "သာမန်ကာလျှံကာ", "အကြေအညာ", "အချုပ်အခြာ", "အဏုကြည့်ကိရိယာ",
                "အာရုံခံတန်ဆောင်း", "အသည်းနှလုံး", "အရုဏ်တက်", "အာလာပသလ္လာပ", "ဥသဘနွား"
            };
            foreach (string ow in khweHtarOfficialWords)
            {
                string normOw = UnicodeNormalizer.NormalizeCanonicalOrder(ow);
                Assert.True(trie.ContainsExactWord(normOw), $"Expected Khwe-Htar official word '{ow}' in Trie!");
            }

            // 3. Verify condemned 'မှား →' forms from Khwe-Htar NEVER entered valid Trie entries
            string[] khweHtarCondemnedWords =
            {
                "ထမီ", "ထမိန်", "ခလေး", "ဖြေးဖြေး", "စပယ်ပင်", "လည်ဂုတ်", "ဂုံးကျော်တံတား",
                "အသဲနှလုံး", "နှမ်းမနဲ", "ပွဲညက်", "ဥဿဖနွား", "အလ္လာပသလ္လာပ", "အချုပ်အချာ", "အခန်းအနား"
            };
            foreach (string cw in khweHtarCondemnedWords)
            {
                string normCw = UnicodeNormalizer.NormalizeCanonicalOrder(cw);
                Assert.False(trie.ContainsExactWord(normCw), $"Book-condemned form '{cw}' must NEVER be a valid Trie headword!");
            }

            // 4. Verify Khwe-Htar homophone comparison cues '(နှိုင်းယှဉ်: ...)' enriched Official Thatpon entries
            Assert.True(model.Entries.Count(e => e.MeaningCue.Contains("နှိုင်းယှဉ်:")) >= 5000);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 5. Verify Khwe-Htar 'မှား → မှန်' rules, productive '-ကနဲ → -ခနဲ' ideophones & Preface 'တ/တစ်' rules
            var v6Cases = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("ကဇွန်းရွက် ကြော်စားသည်။", "ကဇွန်းရွက်", "ကန်စွန်းရွက်"),
                ("ပြတင်းပေါက်တွင် ကုလားကာ တပ်ဆင်ထားသည်။", "ကုလားကာ", "ကန့်လန့်ကာ"),
                ("ဂုံးကျော်တံတား အသစ် ဖွင့်လှစ်သည်။", "ဂုံးကျော်တံတား", "ခုံးကျော်တံတား"),
                ("လည်ဂုတ် နာနေပါသည်။", "လည်ဂုတ်", "လည်ကုပ်"),
                ("ဂျင်းသုပ် စားချင်သည်။", "ဂျင်းသုပ်", "ချင်းသုပ်"),
                ("စပယ်ပင် စိုက်ပျိုးထားသည်။", "စပယ်ပင်", "စံပယ်ပင်"),
                ("ပိုးထမိန် ဝတ်ဆင်ထားသည်။", "ထမိန်", "ထဘီ"),
                ("တပို့တွဲလတွင် နှမ်းမနဲ ထိုးကြသည်။", "နှမ်းမနဲ", "ထမနဲ"),
                ("ဥပဒေသစ် ပြဌာန်းလိုက်သည်။", "ပြဌာန်း", "ပြဋ္ဌာန်း"),
                ("သူက ဖာသိဖာသာ နေတတ်သည်။", "ဖာသိဖာသာ", "ဘာသိဘာသာ"),
                ("ဖြေးဖြေး မောင်းပါ။", "ဖြေးဖြေး", "ဖြည်းဖြည်း"),
                ("ရေဗူး ယူလာခဲ့ပါ။", "ရေဗူး", "ရေဘူး"),
                ("အမွှာပူး ကလေး မွေးဖွားသည်။", "အမွှာပူး", "အမြွှာပူး"),
                ("နိုင်ငံတော်၏ အချုပ်အချာ အာဏာကို ကာကွယ်သည်။", "အချုပ်အချာ", "အချုပ်အခြာ"),
                ("အခန်းအနား ကျင်းပမည်။", "အခန်းအနား", "အခမ်းအနား"),
                ("အသဲနှလုံး ရောဂါ ကုသသည်။", "အသဲနှလုံး", "အသည်းနှလုံး"),
                ("အာရုဏ်တက် အချိန်တွင် ထသည်။", "အာရုဏ်တက်", "အရုဏ်တက်"),
                ("ရင်းနှီးစွာ အလ္လာပသလ္လာပ ပြောဆိုကြသည်။", "အလ္လာပသလ္လာပ", "အာလာပသလ္လာပ"),
                ("ရုတ်ကနဲ မိုးရွာချလိုက်သည်။", "ရုတ်ကနဲ", "ရုတ်ခနဲ"),
                ("မိုးကြိုး လက်ကနဲ ပစ်သွားသည်။", "လက်ကနဲ", "လက်ခနဲ"),
                ("အမှတ်တစ်ရ လက်ဆောင် ပေးအပ်သည်။", "အမှတ်တစ်ရ", "အမှတ်တရ"),
                ("တစ်ခမ်းတစ်နား ကြိုဆိုကြသည်။", "တစ်ခမ်းတစ်နား", "တခမ်းတနား"),
                ("တပါတည်း ယူဆောင်လာပါ။", "တပါတည်း", "တစ်ပါတည်း")
            };

            foreach (var tc in v6Cases)
            {
                string expWrongNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedWrong);
                string expFixNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedFix);
                var issues = engine.CheckText(tc.Input);
                Assert.True(issues.Any(iss =>
                    (iss.OriginalText.Contains(tc.ExpectedWrong) ||
                     UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText).Contains(expWrongNorm)) &&
                    iss.Suggestions.Count > 0 &&
                    iss.Suggestions.Any(s =>
                        s.Replacement.Contains(tc.ExpectedFix) ||
                        UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement).Contains(expFixNorm))),
                    $"Failed V6 check on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedFix}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 6. Verify clean Preface 'တ' compounds & Khwe-Htar valid sentences produce ZERO false positives!
            string[] v6CleanSentences =
            {
                "အမှတ်တရ လက်ဆောင်ကို တခမ်းတနား ပေးအပ်ရာ တအံ့တဩ ဖြစ်သွားသည်။",
                "အရောတဝင် ပြောဆိုပြီး အာလာပသလ္လာပ စကားဆိုကြသည်။",
                "ခုံးကျော်တံတား ပေါ်မှ တဖြည်းဖြည်း မောင်းနှင်လာသည်။"
            };
            foreach (string clean in v6CleanSentences)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0, $"Unexpected V6 false positive in '{clean}': {string.Join(", ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void BurmeseGrammarSourcesAndReasoningEngine_EnforcesAuditedGrammarExemptionsAndReasoning()
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

            // 1. Audited Burmese Grammar Error Cases (GeneralStandard) — every case must also carry explicit GrammaticalReasoning & AuthoritativeSource!
            var generalGrammarCases = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("ရန်ကုန်သို့ ရောက်ဘူးတယ်။", "ဘူး", "ဖူး"),
                ("သူ ကျောင်းကို မသွားတယ်။", "တယ်", "ဘူး"),
                ("ပင်လယ်စာတွေက အရသာရှိဘူး။", "အရသာရှိဘူး", "အရသာရှိတယ်"),
                ("ညနေပိုင်းမှာ ကျွန်တော်တို့ ဘောလုံးမကစား။", "မကစား", "မကစားဘူး"),
                ("မိုးရွာကြောင့် မသွားတော့ဘူး။", "ကြောင့်", "သောကြောင့်"),
                ("သူကကို ပြောလိုက်ပါ။", "ကကို", "ကို"),
                ("ကျွန်တော် သွားတယ်သည်။", "တယ်သည်", "တယ်"),
                ("လက်ဖက်ရည်ဆိုင်ဟာ နေရာကောင်းတစ်ခုပါခဲ့။", "တစ်ခုပါခဲ့", "တစ်ခုပါပဲ"),
                ("လက်ရှိတွင် ကော်မတီက စုံစမ်းစစ်ဆေးခဲ့သည်။", "ခဲ့သည်", "နေသည်"),
                ("အလုပ်ပြီးရင် အစ်ကိုက အိမ်ထဲကို အနားယူနေတယ်။", "ကို", "မှာ"),
                ("မနက်ဖြန် ကျောင်းမှာ သွားမယ်။", "မှာ", "ကို"),
                ("လုံခြုံရေးအဖွဲ့က ဒေတာဘေ့စ်ကို မှတ်တမ်းများ ပြန်လည်ရယူခဲ့သည်။", "ကို", "မှ"),
                ("ကော်မတီက ကချင်ပြည်နယ်ကို စစ်ဆေးမှု ပြုလုပ်ခဲ့သည်။", "ကို", "တွင်"),
                ("ဘောပင် သုံးခြောင်း ဝယ်လာသည်။", "ခြောင်း", "ချောင်း"),
                ("ဘုန်းကြီးကျောင်း တစ်အောင် ဆောက်လုပ်လှူဒါန်းသည်။", "အောင်", "ဆောင်"),
                ("မိတ်ဆွေဆိုတာ တစ်ကောင်ကိုတစ်ကောင် အားကိုးရသည်။", "တစ်ကောင်ကိုတစ်ကောင်", "တစ်ယောက်ကိုတစ်ယောက်"),
                ("မနက်ဖြန် အလုပ်မရှိလို့ နောက်ကျမှ ထမည်ဖြစ်သည်။", "ထမည်ဖြစ်သည်", "ထမယ်"),
                ("နည်းပညာစာတမ်းတွင် လုပ်ငန်းစဉ်ကို တော်တော်ဂွမ်း ဖော်ပြထားသည်။", "တော်တော်ဂွမ်း", "အလွန်အားနည်းစွာ")
            };

            foreach (var tc in generalGrammarCases)
            {
                var issues = generalEngine.CheckText(tc.Input);
                var matched = issues.FirstOrDefault(iss =>
                    iss.OriginalText.Contains(tc.ExpectedWrong) &&
                    iss.Suggestions.Any(s => s.Replacement.Contains(tc.ExpectedFix)));

                Assert.True(matched != null,
                    $"Failed V7 grammar check on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedFix}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
                Assert.False(string.IsNullOrWhiteSpace(matched!.GrammaticalReasoning),
                    $"Missing GrammaticalReasoning on '{tc.Input}'!");
                Assert.False(string.IsNullOrWhiteSpace(matched.AuthoritativeSource),
                    $"Missing AuthoritativeSource on '{tc.Input}'!");
            }

            // 2. Audited Formal/Official Administrative Grammar Rules ('နှင့်' vs 'ဖြင့်', 'ကျွန်တော်မှ' vs 'ကျွန်တော်က', 'အား' vs 'ကို')
            var formalGrammarCases = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("စာချုပ်ကို ဘောပင်နှင့် ရေးသားသည်။", "နှင့်", "ဖြင့်"),
                ("ကျွန်တော်မှ တင်ပြအပ်ပါသည်။", "မှ", "က"),
                ("လုပ်ငန်းစဉ်အား စစ်ဆေးသည်။", "အား", "ကို")
            };
            foreach (var fc in formalGrammarCases)
            {
                var issues = formalEngine.CheckText(fc.Input);
                var matched = issues.FirstOrDefault(iss =>
                    iss.OriginalText == fc.ExpectedWrong &&
                    iss.Suggestions.Any(s => s.Replacement == fc.ExpectedFix));
                Assert.True(matched != null,
                    $"Failed V7 formal grammar check on '{fc.Input}': expected '{fc.ExpectedWrong}' -> '{fc.ExpectedFix}', got [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
                Assert.False(string.IsNullOrWhiteSpace(matched!.GrammaticalReasoning));
            }

            // 3. Verify Contextual Homophone Disambiguation ALSO populates Evidence-Backed Reasoning!
            var ctxIssues = generalEngine.CheckText("ခွေးကလေးကို အစာ ကြွေးထားသည်။");
            var ctxMatch = ctxIssues.FirstOrDefault(i => i.OriginalText == "ကြွေး");
            Assert.NotNull(ctxMatch);
            Assert.Contains("အစာ", ctxMatch!.GrammaticalReasoning);

            // 4. Verify Reasoned Linguistic Exemptions (Zero False Positives on Traps Found in Unvetted Grammar Sources!)
            string[] generalTrapsMustStayClean =
            {
                "ဟိုတယ်သည် သန့်ရှင်းသပ်ရပ်သည်။",                  // 'တယ်သည်' inside noun 'ဟိုတယ်' + 'သည်'
                "မြို့ဟောင်းသည် အလွန် လှပတင့်တယ်သည်။",          // 'တယ်သည်' inside verb 'တင့်တယ်-' + 'သည်'
                "အပြင်သို့ မသွားနဲ့။",                              // Sentence-final prohibitive 'နဲ့'
                "သူ သွားတယ်။",                                     // Zero-marked subject in Burmese
                "စာရင်းတွင် သူလည်း လိုက်ပါခဲ့သည်။"                // Lexical verb 'ပါ-' + 'ခဲ့သည်'
            };
            foreach (string clean in generalTrapsMustStayClean)
            {
                var fp = generalEngine.CheckText(clean);
                Assert.True(fp.Count == 0, $"False positive on General linguistic trap '{clean}': [{string.Join("; ", fp.Select(x => x.OriginalText))}]");
            }

            string[] formalTrapsMustStayClean =
            {
                "ဝန်ကြီးဌာနမှ ဝန်ထမ်းများ တက်ရောက်သည်။",          // Valid ablative 'မှ' modifying 'ဝန်ထမ်းများ'
                "ရန်ကုန်မြို့မှ ထွက်ခွာသည်။",                      // Valid ablative 'မှ' with departure verb
                "ဆရာက ကျောင်းသားအား စာအုပ်ကို လက်ခံစေသည်။",     // Double-'ကို' & causative '-စေ' licensing 'အား'!
                "မြတ်စွာဘုရားသည် တရားဟောတော်မူ၏။",              // Valid literary sentence-final '၏'
                "ဤစည်းကမ်းကို မမေ့နှင့်။"                          // Valid formal prohibitive sentence-final 'နှင့်'
            };
            foreach (string clean in formalTrapsMustStayClean)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0, $"False positive on Formal linguistic trap '{clean}': [{string.Join("; ", fp.Select(x => x.OriginalText))}]");
            }
        }

        [Fact]
        public void ZeroWrongnessEnterpriseResponsibility_ProtectsOfficialHeadwordsAndEverydayCleanSentences()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. ALL Official 462-Page 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' Headwords MUST Produce ZERO Errors Standalone!
            var officialHeadwords = model.Entries
                .Where(e => e.IsOfficialThatpon)
                .Select(e => e.Word)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var flaggedOfficial = new List<string>();
            foreach (string ow in officialHeadwords)
            {
                var iss = generalEngine.CheckText(ow);
                if (iss.Count > 0)
                {
                    flaggedOfficial.Add($"{ow} => {iss[0].OriginalText}->{iss[0].Suggestions.FirstOrDefault()?.Replacement} ({iss[0].IssueType})");
                }
            }
            Assert.True(flaggedOfficial.Count == 0,
                $"Official Thatpon headwords falsely flagged ({flaggedOfficial.Count}/{officialHeadwords.Count}): {string.Join(" | ", flaggedOfficial.Take(20))}");

            // 2. High-Responsibility Clean Real-World Sentences (Must Produce ZERO False Positives!)
            string[] enterpriseCleanSentences =
            {
                "ပတ်သက်မှု မရှိပါ။",
                "လေယာဉ်ခရီးစဉ်က အရမ်း စိတ်ဝင်စားဖို့ကောင်းတယ်။",
                "လေဆိပ်ကို ဘယ်အချိန် သွားသင့်သလဲ။",
                "မျက်လုံးတွေက ကြယ်လေးတွေလို လှတယ်။",
                "မြန်မာသဒ္ဒါနှင့် သဒ္ဓါတရားသည် အရေးပါပါတယ်။",
                "ကောင်းသောညပါရှင့်။",
                "ကူညီပေးဖို့ လာခဲ့မယ်။",
                "ခဏနေမှ ပြန်လာခဲ့မယ်။",
                "ဆက်မသွားနိုင်တော့ဘူး၊ ပင်ပန်းလို့။",
                "အဲဒီအကြောင်းအရာကို မြန်မာလို မရေးရတာ များတယ်။",
                "ထို့ကြောင့် ဤကိစ္စတွင် လုပ်ငန်းစဉ်သည် အလွန်ရိုးရှင်းပါသည်။",
                "ဝင်ငွေကောင်းမည့် ရုပ်ရှင်တွင် ဇာတ်ရုပ်မှာ သရုပ်ဆောင်ထားသည်။",
                "နိုင်ငံရေး အမြတ်မထုတ်ဘဲ သူ၏အမြင်ကို တင်ပြသည်။",
                "ဒီတာယာက လေစိမ့်နေတယ်။",
                "ဆေးသောက်လိုက်တော့ အအေးမိတာ ပျောက်သွားတယ်။",
                "မိမိကိုယ်ကို သတ်သေခဲ့သူအား စစ်ဆေးသည်။",
                "တရားခံ ကျား ၅၉ ဦး၊ မ ၁၀ ဦး ဖမ်းဆီးရမိသည်။",
                "၁၉၄၁ ခုနှစ် သန်းခေါင်စာရင်းအရ လူဦးရေ တိုးလာသည်။",
                "ရော်ဘာခြစ် အလုပ်သမားများလည်း စားဝတ်နေရေး အဆင်ပြေကြောင်း သိရသည်။",
                "ရုတ်တရက် ကံကောင်းမှုကို ကြုံတွေ့ဖွယ်ရှိသည်။",
                "ဟင်္သာတမြို့နယ်နှင့် မိုးညှင်းမြို့နယ်တို့တွင် တစ်သမတ်တည်း ဆောင်ရွက်သည်။",
                "သီချင်း ရေးသည်၊ ရာထူး တက်သည်၊ အစည်းအဝေး ဖွင့်သည်။",
                "အကျွေးအမွေးနှင့် အလုပ်အကျွေး ပြုစုသည်။",
                "ပယောဂဆေးကြမ်းနှင့် ရေယဉ်ကြော။"
            };

            foreach (string sent in enterpriseCleanSentences)
            {
                var fp = generalEngine.CheckText(sent);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on clean enterprise sentence '{sent}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 3. Verify Pali Stacking Errors & Genuine Orthographic Errors Are Still Accurately Caught!
            var mustCatch = new (string Input, string Wrong, string Fix)[]
            {
                ("ရုပ်ဝထ္ထု ပစ္စည်း။", "ဝထ္ထု", "ဝတ္ထု"),
                ("ဗုဓ္ဓဘာသာ။", "ဗုဓ္ဓ", "ဗုဒ္ဓ"),
                ("သတ်သေခံ ပစ္စည်း။", "သတ်သေခံ", "သက်သေခံ"),
                ("တာဝန်ကို လစ်လျူရှုထားသည်။", "လစ်လျူရှု", "လျစ်လျူရှု"),
                ("တသမတ်တည်း ဆောင်ရွက်သည်။", "တသမတ်တည်း", "တစ်သမတ်တည်း"),
                ("ဝိညာဏ် ချုပ်ငြိမ်းသည်။", "ဝိညာဏ်", "ဝိညာဉ်")
            };

            foreach (var tc in mustCatch)
            {
                var iss = generalEngine.CheckText(tc.Input);
                Assert.True(iss.Any(i => i.OriginalText.Contains(tc.Wrong) && i.Suggestions.Any(s => s.Replacement.Contains(tc.Fix))),
                    $"Failed to catch '{tc.Wrong}' -> '{tc.Fix}' in '{tc.Input}': got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void GovernmentOfficialMode_ZeroFalsePositivesOnMinistryAndGazetteCorpus_AndCatchesAdministrativeErrors()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalGovEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Authentic Union Government Notifications, Ministries, Constitutional Clauses & Official Letters -> ZERO False Positives!
            string[] cleanGovOfficialCorpus =
            {
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော်၊ ပြည်ထောင်စုအစိုးရအဖွဲ့၊ အမိန့်ကြော်ငြာစာအမှတ် (၁၂/၂၀၂၆)။",
                "ပို့ဆောင်ရေးနှင့် ဆက်သွယ်ရေးဝန်ကြီးဌာနနှင့် ဒစ်ဂျစ်တယ် စီးပွားရေး ဖွံ့ဖြိုးတိုးတက်မှု ကော်မတီ။",
                "လျှပ်စစ်စွမ်းအားဝန်ကြီးဌာနနှင့် စွမ်းအင်ဝန်ကြီးဌာနတို့က လျှပ်စစ်ဓာတ်အား ပြုပြင်ထိန်းသိမ်းရေး ဆောင်ရွက်သည်။",
                "လူ့စွမ်းအားအရင်းအမြစ် ဖွံ့ဖြိုးတိုးတက်ရေးနှင့် စက်စွမ်းအား စစ်ဆေးခြင်း လုပ်ငန်းများကို ဆောင်ရွက်သည်။",
                "တပ်ဖွဲ့ဝင်အင်အား စစ်ဆေးခြင်းနှင့် လျှပ်စစ်ဓာတ်အား တည်ဆောက်ပြုပြင်ခြင်းတို့ကို အတည်ပြုသည်။",
                "အဆိုပါ ဟိုတယ်၊ တည်းခိုခန်းနှင့် ခရီးသွားလုပ်ငန်းများကို စည်းကမ်းနှင့်အညီ စိစစ်ခွင့်ပြုသည်။",
                "အစိုးရ ဝက်ဘ်ပေါ်တယ်၊ အီးဂါးဗားမန့်စနစ်နှင့် ဒစ်ဂျစ်တယ် ဝန်ဆောင်မှုများကို တိုးချဲ့ဆောင်ရွက်လျက်ရှိသည်။",
                "ဦးအောင်ကျော်မိုးအား ညွှန်ကြားရေးမှူးချုပ်အဖြစ် ခန့်အပ်တာဝန်ပေးလိုက်သည်။",
                "ဒေါ်ခင်မေမယ်၊ ဒုတိယညွှန်ကြားရေးမှူးသည် အစည်းအဝေးသို့ တက်ရောက်ခဲ့သည်။",
                "ခနော်ခနဲ့ ဖြစ်နေသော တံတားဟောင်းများကို ဦးစားပေး ပြုပြင်တည်ဆောက်ရန် ညွှန်ကြားသည်။",
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် ဖွဲ့စည်းပုံအခြေခံဥပဒေ ပုဒ်မ ၄၁၉ နှင့် ပုဒ်မခွဲ (က)၊ အပိုဒ် (၃)၊ အပိုဒ်ခွဲ (ခ) အရ ဤအမိန့်ကို ထုတ်ပြန်လိုက်သည်။",
                "မိတ်တူနံတူ ဖြစ်ကြသည်။",
                "လင်းဇင်း၊ ဇင်းမယ် စသည့်မြို့များ တည်ထောင်ကြသည်။"
            };

            foreach (string line in cleanGovOfficialCorpus)
            {
                var fp = formalGovEngine.CheckText(line);
                Assert.True(fp.Count == 0,
                    $"Government FormalOfficial false positive in '{line}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Real Government / Ministry Administrative Spelling & Gazette Errors -> Must Catch Top-1!
            var govAdminErrors = new (string Input, string Wrong, string Fix)[]
            {
                ("၄င်းကိစ္စကို ဆောင်ရွက်ရမည်။", "၄င်း", "၎င်း"),
                ("၎င်းင်းအဖွဲ့သို့ ပေးပို့သည်။", "၎င်းင်း", "၎င်း"),
                ("ဝန်ကြီးဋ္ဌာနသို့ တင်ပြသည်။", "ဋ္ဌာန", "ဌာန"),
                ("ဦးစီးဋ္ဌာနက ထုတ်ပြန်သည်။", "ဋ္ဌာန", "ဌာန"),
                ("အမိန့်ကြေညာစာ ထုတ်ပြန်သည်။", "အမိန့်ကြေညာစာ", "အမိန့်ကြော်ငြာစာ"),
                ("အမိန့်ကြော်ညာစာ ထုတ်ပြန်သည်။", "အမိန့်ကြော်ညာစာ", "အမိန့်ကြော်ငြာစာ"),
                ("အမိန့်ကြေငြာစာ ထုတ်ပြန်သည်။", "အမိန့်ကြေငြာစာ", "အမိန့်ကြော်ငြာစာ"),
                ("ဥပဒေ ပိုဒ်မ ၁၂ အရ အရေးယူမည်။", "ပိုဒ်မ", "ပုဒ်မ"),
                ("အမိန့်ကြော်ငြာစာ အပုဒ် (၃) အရ။", "အပုဒ်", "အပိုဒ်"),
                ("စာပုဒ် တစ်ပိုဒ် ရေးသားပါ။", "စာပုဒ်", "စာပိုဒ်"),
                ("ထောင်ဒါဏ်နှင့် ဒါဏ်ငွေ ချမှတ်သည်။", "ထောင်ဒါဏ်", "ထောင်ဒဏ်"),
                ("ရလာဒ် ကောင်းများ ရရှိသည်။", "ရလာဒ်", "ရလဒ်"),
                ("မိတ်တူ ပေးပို့အပ်ပါသည်။", "မိတ်တူ", "မိတ္တူ"),
                ("လုပ်ထုံးလုပ်နဲ နှင့်အညီ ဆောင်ရွက်သည်။", "လုပ်ထုံးလုပ်နဲ", "လုပ်ထုံးလုပ်နည်း"),
                ("ရန်ပုံငွေ လျှာထားချက် တင်ပြသည်။", "လျှာထား", "လျာထား")
            };

            foreach (var tc in govAdminErrors)
            {
                var iss = formalGovEngine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                    (i.OriginalText.Contains(tc.Wrong) || tc.Wrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Count > 0 &&
                    (i.Suggestions[0].Replacement.Contains(tc.Fix) || tc.Fix.Contains(i.Suggestions[0].Replacement))),
                    $"Failed Government Official check on '{tc.Input}': expected '{tc.Wrong}' -> '{tc.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V9_SilentCodas_BoundMorphemes_DottedCircleLaKaung_AndPrefixCompounds_WorkWithZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            // 1. Verify Zero Vulgar / Misleading Crowd English Glosses in Enterprise/Gov Lexicon
            var vulgarRx = new System.Text.RegularExpressions.Regex(
                @"\b(arse|arsehole|asshole|bitch|fuck|shit|bastard|whore|slut|cunt|mistress|snob)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pollutedGlosses = model.Entries
                .Where(e => !string.IsNullOrEmpty(e.MeaningCue) && vulgarRx.IsMatch(e.MeaningCue))
                .Select(e => $"{e.Word}:{e.MeaningCue}")
                .ToList();
            Assert.True(pollutedGlosses.Count == 0,
                $"Found inappropriate English glosses in lexicon: {string.Join(", ", pollutedGlosses)}");

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 2. Screenshot 1: Dotted-circle '၄်င်း' (\u1044\u103A\u1004\u103A\u1038) & '၎်င်း' must underline the WHOLE token -> '၎င်း' (never orphan 'င်း -> မင်း')
            foreach (string dottedLaKaung in new[] { "\u1044\u103A\u1004\u103A\u1038", "\u104E\u103A\u1004\u103A\u1038" })
            {
                var lkIssues = engine.CheckText(dottedLaKaung);
                Assert.Single(lkIssues);
                Assert.Equal(dottedLaKaung, lkIssues[0].OriginalText);
                Assert.Equal("၎င်း", lkIssues[0].Suggestions[0].Replacement);
            }

            // 3. Screenshot 2 + Systemic Family Coverage (Polite Trailing Particles, Prefix Compounds, Bound Morphemes, Silent & Pali Codas)
            var v9ErrorCases = new (string Input, string Wrong, string Fix)[]
            {
                // Screenshot 2 exact error lines
                ("ဟုကဲ့ပါ", "ဟုကဲ့", "ဟုတ်ကဲ့"),
                ("ဟုကဲ့", "ဟုကဲ့", "ဟုတ်ကဲ့"),
                ("စရာတော်", "စရာတော်", "ဆရာတော်"),
                ("ဖြားနေတာလား", "ဖြား", "ဖျား"),
                ("ဗိုဟ်ကြီး", "ဗိုလ်ကြီး", "ဗိုလ်ကြီး"), // checked via replacement below
                ("ဂြိုလ်", "ဂြိုလ်", "ဂြိုဟ်"),
                // Systemic family expansions
                ("ဟုကဲ့ပါရှင့်", "ဟုကဲ့", "ဟုတ်ကဲ့"),
                ("ဟုကဲ့ခင်ဗျား", "ဟုကဲ့", "ဟုတ်ကဲ့"),
                ("ဟုတယ်နော်", "ဟုတယ်", "ဟုတ်တယ်"),
                ("ဟုလားဗျာ", "ဟုလား", "ဟုတ်လား"),
                ("မဟုဘူးလေ", "မဟုဘူး", "မဟုတ်ဘူး"),
                ("မဟုပါဘူးရှင့်", "မဟုပါဘူး", "မဟုတ်ပါဘူး"),
                ("ကျောင်းစရာမ က စာသင်သည်။", "စရာမ", "ဆရာမ"),
                ("ကျောင်းအုပ်စရာကြီး ဦးဘ။", "စရာကြီး", "ဆရာကြီး"),
                ("ဗိုဟ်မှူးနှင့် ဗိုဟ်ချုပ်။", "ဗိုဟ်မှူး", "ဗိုလ်မှူး"),
                ("ဗိုဟ်သင်တန်း တက်ရောက်သည်။", "ဗိုဟ်", "ဗိုလ်"),
                ("ဂြိုလ်တု လွှတ်တင်သည်။", "ဂြိုလ်တု", "ဂြိုဟ်တု"),
                ("ဂြိုလ်ပတ်လမ်းအတွင်း လှည့်ပတ်သည်။", "ဂြိုလ်", "ဂြိုဟ်"),
                ("မီးသဂြိုဟ်ခြင်းနှင့် သင်္ဂြိုလ်ခြင်း။", "သင်္ဂြိုလ်", "သင်္ဂြိုဟ်"),
                ("ကုသိုဟ်ကောင်းမှု ပြုသည်။", "ကုသိုဟ်", "ကုသိုလ်"),
                ("ပုဂ္ဂိုဟ်ရေး ကိစ္စ။", "ပုဂ္ဂိုဟ်", "ပုဂ္ဂိုလ်"),
                ("သီဟိုလ်ကျွန်းနှင့် မြင်းမိုလ်တောင်။", "သီဟိုလ်", "သီဟိုဠ်"),
                ("သီဟိုလ်ကျွန်းနှင့် မြင်းမိုလ်တောင်။", "မြင်းမိုလ်", "မြင်းမိုရ်"),
                ("ဆုလာဒ် ချီးမြှင့်သည်။", "ဆုလာဒ်", "ဆုလာဘ်"),
                ("ပြသာဒ် ဆောင်ကြီး။", "ပြသာဒ်", "ပြာသာဒ်"),
                ("ဟုပ်လား", "ဟုပ်", "ဟုတ်"),
                ("ပိုက်ဆံ ပြောက်သွားတယ်", "ပြောက်", "ပျောက်"),
                ("စာ ဖပ်နေတယ်", "ဖပ်", "ဖတ်"),
                ("ရေ သောပ်မယ်", "သောပ်", "သောက်"),
                ("အပြင် ထွတ်သွားတယ်", "ထွတ်", "ထွက်"),
                ("သေချာ ရှင်းပျပါ", "ပျ", "ပြ"),
                ("အိမ် ပျောင်းသွားတယ်", "ပျောင်း", "ပြောင်း"),
                ("စိတ် ရှုတ်နေတယ်", "ရှုတ်", "ရှုပ်"),
                ("ရေ ခြိုးနေတယ်", "ခြိုး", "ချိုး")
            };

            foreach (var tc in v9ErrorCases)
            {
                string expectedWrong = tc.Input == "ဗိုဟ်ကြီး" ? "ဗိုဟ်ကြီး" : tc.Wrong;
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                    (i.OriginalText.Contains(expectedWrong) || expectedWrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Count > 0 &&
                    i.Suggestions[0].Replacement.Contains(tc.Fix)),
                    $"Failed V9 check on '{tc.Input}': expected '{expectedWrong}' -> '{tc.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 4. Clean Counterparts, Bound-Morpheme Compounds, Quotative 'ဟု' & Genuine Rare Verbs -> ZERO False Positives!
            string[] v9CleanCases =
            {
                "ဟုတ်ကဲ့",
                "ဟုတ်ကဲ့ပါ",
                "ဆရာ",
                "ဆရာတော်",
                "ဖျားနေတာလား",
                "ဗိုလ်ကြီး",
                "ဂြိုဟ်",
                "၎င်းကိစ္စကို ဆောင်ရွက်သည်။",
                "ဖြားယောင်းသွေးဆောင်သည်။",
                "ပြောက်ကျားတပ်ဖွဲ့။",
                "ပျော့ပျောင်းသိမ်မွေ့သည်။",
                "အပြောအဆို ရိုင်းပျသည်။",
                "အကြမ်းဖက်မှုကို ရှုတ်ချသည်။",
                "ခြိုးခြံချွေတာသည်။",
                "ပြောစရာမရှိပါ။",
                "လူမိုက်ဟုကဲ့ရဲ့သည်။",
                "ခြေထောက် ကျဉ်နေတာလား။",
                "မိုး ခြိမ်းနေတယ်။",
                "နေ ကြတ်သွားပြီ။",
                "နှုတ်ခမ်း အသား ပပ်နေတယ်။",
                "တောင်ကိုရီးယားမြို့တော် ဆိုးလ်တွင် ဆဲလ်သုတေသန ပြုလုပ်သည်။"
            };

            foreach (string clean in v9CleanCases)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V9 false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V10_DeepBurmeseLexicology_PaliLoanwords_ThatponStems_AndMorphophonemics_WorkAccurately()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            // 1. Verify Ma-consonant vocabulary expansion (55,200+ unique headwords) & 20,900+ Official Thatpon authority!
            Assert.True(model.Entries.Select(e => e.Word).Distinct().Count() >= 55200,
                $"Expected >= 55,200 unique headwords after Ma-consonant fix, got {model.Entries.Select(e => e.Word).Distinct().Count()}");
            Assert.True(model.Entries.Where(e => e.IsOfficialThatpon).Select(e => e.Word).Distinct().Count() >= 20500,
                $"Expected >= 20,500 Official Thatpon headwords after stem promotion, got {model.Entries.Where(e => e.IsOfficialThatpon).Select(e => e.Word).Distinct().Count()}");

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 2. Deep Burmese Orthography, Pali Loanword Laws, Round-AA Lookalikes, Non-Existent 'မှုံ' Rhyme & Contextual Pairs
            var v10Errors = new (string Input, string Wrong, string Fix)[]
            {
                ("ပရိတ်သတ် အများအပြား တက်ရောက်သည်။", "ပရိတ်သတ်", "ပရိသတ်"),
                ("ပင်ကိုယ် သဘာဝအရ ရိုးသားသည်။", "ပင်ကိုယ်", "ပင်ကို"),
                ("သစ်တောများ စိမ်းလန်းစိုပြေနေသည်။", "စိုပြေ", "စိုပြည်"),
                ("ပြဿဒ် ဆောင်ကြီး တည်ဆောက်သည်။", "ပြဿဒ်", "ပြာသာဒ်"),
                ("စကြာဝဠာ အတွင်း၌ တည်ရှိသည်။", "စကြာဝဠာ", "စကြဝဠာ"),
                ("သာမာန် ပြည်သူများနှင့် အနာဂါတ် ကာလ။", "သာမာန်", "သာမန်"),
                ("သာမာန် ပြည်သူများနှင့် အနာဂါတ် ကာလ။", "အနာဂါတ်", "အနာဂတ်"),
                ("လုံလ ဝိရိယ စိုက်ထုတ်သည်။", "လုံလ", "လုံ့လ"),
                ("လုံ့လ ဝိရိယ စိုက်ထုတ်သည်။", "ဝိရိယ", "ဝီရိယ"),
                ("ပိတိ ဖြစ်မိသည်။", "ပိတိ", "ပီတိ"),
                ("ယေဘူယျ အားဖြင့် မှန်ကန်သည်။", "ယေဘူယျ", "ယေဘုယျ"),
                ("စာအုပ်များ ထုတ်ဝေဖြန့်ချီသည်။", "ဖြန့်ချီ", "ဖြန့်ချိ"),
                ("လုပ်ငန်းခွင်သို့ သွားရောက်ကြည့်ရှူသည်။", "ကြည့်ရှူ", "ကြည့်ရှု"),
                ("ရှေးရှူ ဆောင်ရွက်သည်။", "ရှေးရှူ", "ရှေးရှု"),
                ("လေကောင်းလေသန့် အသက်ရှုသည်။", "အသက်ရှု", "အသက်ရှူ"),
                ("ခရီးစာရိတ် ထုတ်ပေးသည်။", "စာရိတ်", "စရိတ်"),
                ("မတ်တပ် ရပ်နေသည်။", "မတ်တပ်", "မတ်တတ်"),
                ("ငလျှင် လှုပ်ခတ်သည်။", "ငလျှင်", "ငလျင်"),
                ("ပြူတင်းပေါက် ဖွင့်ထားသည်။", "ပြူတင်းပေါက်", "ပြတင်းပေါက်"),
                ("စီးကမ်း လိုက်နာရမည်။", "စီးကမ်း", "စည်းကမ်း"),
                ("တန်ဘိုး ကြီးမားသည်။", "တန်ဘိုး", "တန်ဖိုး"),
                ("ဗဟိုရ်ဘဏ်က ထုတ်ပြန်သည်။", "ဗဟိုရ်", "ဗဟို"),
                ("နဂိုရ်အတိုင်း ပြန်ထားပါ။", "နဂိုရ်", "နဂို"),
                ("နိုင်ငံတစ်ဝမ်းနှင့် ကမ္ဘာတစ်ဝှမ်း။", "တစ်ဝမ်း", "တစ်ဝန်း"),
                ("နိုင်ငံတစ်ဝန်းနှင့် ကမ္ဘာတစ်ဝှမ်း။", "တစ်ဝှမ်း", "တစ်ဝန်း"),
                ("ကောက်နှုတ်ချက် ထုတ်ပြန်သည်။", "ကောက်နှုတ်", "ကောက်နုတ်"),
                ("သတင်းကျွတ်လတွင် သတင်းသုံးတော်မူသည်။", "သတင်းကျွတ်", "သီတင်းကျွတ်"),
                ("သီတင်းစာ ဖတ်ရှုသည်။", "သီတင်းစာ", "သတင်းစာ"),
                ("ဘေးအန္တရာဉ်နှင့် နည်းပရိယာဉ်။", "အန္တရာဉ်", "အန္တရာယ်"),
                ("ဘေးအန္တရာယ်နှင့် နည်းပရိယာဉ်။", "ပရိယာဉ်", "ပရိယာယ်"),
                ("သတင်းအချက်အလက် ထိန်ချိန်ထားသည်။", "ထိန်ချိန်", "ထိမ်ချန်"),
                ("ရွေ့ပြောင်းလုပ်သားများ။", "ရွေ့ပြောင်း", "ရွှေ့ပြောင်း"),
                ("စွဲဆောင်မှု ရှိသည်။", "စွဲဆောင်မှု", "ဆွဲဆောင်မှု"),
                ("ဖုန်မှုံနှင့် သနပ်ခါးမှုံ့။", "မှုံ", "မှုန်"),
                ("မျက်စေ့ လည်သွားသည်။", "မျက်စေ့", "မျက်စိ"),
                ("ဒော်အောင်ဆန်းစုကြည်။", "ဒော်", "ဒေါ်"),
                ("စုစုပောင်း နှင့် သန်းခောင်စာရင်း။", "ပောင်း", "ပေါင်း"),
                ("လိုအပ္ချက်များ တင်ပြသည်။", "လိုအပ္ချက်", "လိုအပ်ချက်"),
                ("သွားဘို့ ပြင်ဆင်သည်။", "ဘို့", "ဖို့"),
                ("ကူညီပေးဖို လိုအပ်သည်။", "ဖို", "ဖို့"),
                ("တစ်ဦးတစ်ယောက်ထဲ သွားသည်။", "ထဲ", "တည်း"),
                ("သူ့အကို နှင့် ညီအကို။", "အကို", "အစ်ကို")
            };

            foreach (var tc in v10Errors)
            {
                string normWrong = UnicodeNormalizer.NormalizeCanonicalOrder(tc.Wrong);
                string normFix = UnicodeNormalizer.NormalizeCanonicalOrder(tc.Fix);
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                {
                    string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText);
                    if (!origNorm.Contains(normWrong) && !normWrong.Contains(origNorm))
                        return false;
                    if (i.Suggestions.Count == 0)
                        return false;
                    string repNorm = UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement);
                    return repNorm.Contains(normFix) || normFix.Contains(repNorm);
                }),
                $"Failed V10 check on '{tc.Input}': expected '{tc.Wrong}' -> '{tc.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 3. Clean Counterparts, Pali Tipitaka Verses, Topic Markers & Serial-Verb Constructions -> ZERO False Positives!
            string[] v10Clean =
            {
                "မင်းကတော့ ဆရာပဲ။",
                "ငါးကို ဖက်ရွက်နဲ့ ထုပ်ပြီး ကင်စားတဲ့ နည်းလမ်းက နာမည်ကြီးတယ်။",
                "ပလောပီနံနှင့် ပီလောပီနံတို့ ပေါပါတယ်။",
                "ဖေ့စ်ဘွတ်သုံးတဲ့သူတွေနဲ့ လိုက်ဘ်နစ်ဇ် စသူတို့ စိတ်ကူးလာကြ၏။",
                "ဓမ္မေန ကိရ ဇာတဿ၊ ပိတာ ပုတ္တဿ မက္ကဋော။",
                "ဇဟိ ရာဇ နိဝတ္တဿု၊ မာ ရဋ္ဌံ ဝိနသာ ဣဒံ။",
                "ပဟိုရ်စည် တီးခတ်သံနှင့် ဗဟိုအစိုးရ။",
                "သင်္ကဿနဂိုရ်ပြည်မှ နဂိုမူလ နေရာသို့ ပြန်ကြွတော်မူသည်။",
                "တစ်မိတစ်ဝမ်းတည်း မွေးဖွားလာသော ညီအစ်ကို။",
                "သေတ္တာတစ်ခုထဲ ထည့်ထားပါ။",
                "မီးဖိုပေါ်တွင် ထမင်းအိုး တည်ထားသည်။",
                "ရဟန်းဂောတမသည် တရားဟောတော်မူ၏။",
                "မိဖုရားကြီးနှင့် စစ်သည်များသည် မျိုးသုဉ်းမည့် အန္တရာယ်မှ ကာကွယ်ကြသည်။"
            };

            foreach (string clean in v10Clean)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V10 false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V11_ClassicalProverbs_LiteraryNegation_AndMmProverbsForensicCases_WorkWithZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Authentic orthography, grammar & lookalike errors caught on mmproverbs.com
            var v11Errors = new (string Input, string Wrong, string Fix)[]
            {
                ("ခတ်တင်မောင်းနင်း ပြောနည်း", "ခတ်တင်မောင်းနင်း", "ခက်တင်မောင်းနင်း"),
                ("ဝိဂြိုဟ်မငံ ဂဠန်ပျံ", "ဂဠန်", "ဂဠုန်"),
                ("မြေဩြဇာ ကောင်းများ ကျွေးစေသည်။", "မြေဩြဇာ", "မြေဩဇာ"),
                ("ကာရံနဘေ ညီစွာပြောဆိုခြင်း", "ကာရံ", "ကာရန်"),
                ("တင်းတိမ်ရောင်ရဲခြင်းသည် မင်္ဂလာမည်၏။", "တင်းတိမ်ရောင်ရဲ", "တင်းတိမ်ရောင့်ရဲ"),
                ("ခိုင်ခန့်သော ငြမ်းကို အားပြုသည်။", "ခိုင်ခန့်", "ခိုင်ခံ့"),
                ("အစာကို ခြိုးခြဲစားရုံမျှမက", "ခြိုးခြဲ", "ခြိုးခြံ"),
                ("ကျွန်ပ်တို့ လုပ်ကျွေးပါမည်။", "ကျွန်ပ်", "ကျွန်ုပ်"),
                ("လမ်းညွန်သူ၏ အကူအညီဖြင့် သွားသည်။", "လမ်းညွန်", "လမ်းညွှန်"),
                ("အများထက် ထူခြားသာလွန်မှ လက်ခံသည်။", "ထူခြား", "ထူးခြား"),
                ("သဘာဝအလျှောက် ဖြစ်ပေါ်လာသည်။", "သဘာဝအလျှောက်", "သဘာဝအလျောက်"),
                ("သန့်ရှင်းလက်ဆက်သော အသီးအနှံများ", "လက်ဆက်", "လတ်ဆတ်")
            };

            foreach (var tc in v11Errors)
            {
                string normFix = UnicodeNormalizer.NormalizeCanonicalOrder(tc.Fix);
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                    (i.OriginalText.Contains(tc.Wrong) || tc.Wrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement).Contains(normFix)),
                    $"Failed V11 check on '{tc.Input}': expected '{tc.Wrong}' -> '{normFix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Authentic classical Myanmar proverbs, literary bare negations, 'သကဲ့သို့', 'အရွက်', 'တစ်လုတ်', 'လက်ဆက်ပေး-', 'ဟိုဟိုသည်သည်' -> ZERO False Positives!
            string[] v11CleanProverbs =
            {
                "မစားရ အညှော်ခံ",
                "မြင်းကမလှုပ်၊ ခုံက လှုပ်",
                "လူ့အလို နတ်မလိုက်နိုင်",
                "လူမသေ ငွေမရှား",
                "ဘုန်းကြီးလို့ သက်မရှည်၊ သက်ရှည်လို့ ဘုန်းမကြီး",
                "ပညာရှိအမျက် ပြင်မထွက်",
                "ပညာရှိ သတိဖြစ်ခဲ",
                "ဖြစ်လွယ်လျှင် ပျက်လွယ်၊ ဖြစ်ခဲလျှင် ပျက်ခဲ",
                "တစ်လုတ်စားဖူး သူ့ကျေးဇူး",
                "အသားတွင် ဝက်၊ အသီးတွင် သရက်၊ အရွက်တွင် လက်ဖက်",
                "အလျင်လို အနှေးဖြစ်",
                "ပတ္တမြားတော်ဝင် ဂေါ်မစင်",
                "အလျှောက်ကောင်း အထောင်းလွတ်",
                "ကိုယ့်ဝန် ကိုယ်ထမ်း၊ ကိုယ့်လမ်း ကိုယ်သွား",
                "ကြိုက်မရှက်၊ ငိုက်မရှက်၊ ငတ်မရှက်",
                "မနီးမဝေးအရပ်တွင် ရသေ့ကြီး နေထိုင်သည်။",
                "မစင်ပုံကို နင်းမိသကဲ့သို့ ဖြစ်သွားပြန်သကဲ့သို့ မှတ်ယူရမည်။",
                "သမီးငယ်နှင့် လက်ဆက်ပေးမည်ဟု ကြံစည်သည်။",
                "အိုးသည်သည် ဟိုဟိုသည်သည် ငေးမောချင်သည်။",
                "လှေသမားသည် တက်ကိုင်၊ ပဲ့ကိုင် ကျွမ်းကျင်ရသည်။"
            };

            foreach (string clean in v11CleanProverbs)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V11 false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V12_MixedZawgyiUnicodeSafety_SanitizedProverbsSearch_MisquotedProverbFixes_AndCustomDictionary_WorkEndToEnd()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Verify Sanitized Offline Proverbs & Sayings Reference Corpus (1,880+ entries) is loaded without polluting single-word lattice
            Assert.True(trie.ReferenceCount >= 1880, $"Expected >= 1880 sanitized proverbs/sayings, got {trie.ReferenceCount}");

            var proverbMatches = engine.SearchDictionary("တစ်လုတ်စားဖူး", 15);
            Assert.Contains(proverbMatches, m => m.IsReferenceEntry && m.Word.Contains("တစ်လုတ်စားဖူး"));

            var rhetoricMatches = engine.SearchDictionary("ရေကူးညာတင်", 15);
            Assert.Contains(rhetoricMatches, m => m.IsReferenceEntry && m.PosTag == "RHETORIC");

            // 2. Verify Chunk-Safe Mixed Unicode + Zawgyi Protection:
            //    Must NEVER corrupt surrounding Pyidaungsu Unicode words ('အဆွေအမျိုးတို့ကို -> အဆှအေမြိုးတို့ကို') when a single hybrid/Zawgyi token is present!
            string mixedHybrid = "၁၆။ အဆွေအမျိုးတို့ကို ထောက်ပံ့ကူညီခြင်း (ဉာတကာနဉ္စ သၚ်္ဂဟော)";
            var hybridIssues = engine.CheckText(mixedHybrid);
            Assert.Single(hybridIssues);
            Assert.Equal("သၚ်္ဂဟော", hybridIssues[0].OriginalText);
            Assert.Equal("သင်္ဂဟော", hybridIssues[0].Suggestions[0].Replacement);

            string mixedPastedZawgyi = "ဤအစီရင်ခံစာတွင် က ခ်င္လ်က္ လက္က်ိဳး ဟု ကူးယူမိသည်။";
            var mixedZgIssues = engine.CheckText(mixedPastedZawgyi);
            Assert.Single(mixedZgIssues);
            Assert.Equal("ခ်င္လ်က္ လက္က်ိဳး", mixedZgIssues[0].OriginalText);
            Assert.Equal("ချင်လျက် လက်ကျိုး", mixedZgIssues[0].Suggestions[0].Replacement);
            string repairedMixed = mixedPastedZawgyi.Remove(mixedZgIssues[0].StartIndex, mixedZgIssues[0].Length)
                .Insert(mixedZgIssues[0].StartIndex, mixedZgIssues[0].Suggestions[0].Replacement);
            Assert.Equal("ဤအစီရင်ခံစာတွင် က ချင်လျက် လက်ကျိုး ဟု ကူးယူမိသည်။", repairedMixed);

            // Pure Zawgyi paragraph still converts cleanly as a whole
            string pureZawgyi = "ကုသိုလ္တစ္ပဲ ငရဲတစ္ပိႆာ";
            var pureZgIssues = engine.CheckText(pureZawgyi);
            Assert.Single(pureZgIssues);
            Assert.Equal("ကုသိုလ်တစ်ပဲ ငရဲတစ်ပိဿာ", pureZgIssues[0].Suggestions[0].Replacement);

            // 3. Verify High-Precision Misquoted Proverb Corrections
            var misquotedProverbs = new (string Input, string Wrong, string Fix)[]
            {
                ("တစ်လုပ်စားဖူး သူ့ကျေးဇူး ကို မမေ့အပ်ပါ။", "တစ်လုပ်စားဖူး", "တစ်လုတ်စားဖူး"),
                ("ကိုင်းကျွန်းမီ ကျွန်းကိုင်းမီ နေထိုင်ကြသည်။", "ကိုင်းကျွန်းမီ", "ကိုင်းကျွန်းမှီ"),
                ("အလျောက်ကောင်း အထောင်းလွတ် ဆိုသကဲ့သို့ ဖြစ်၏။", "အလျောက်ကောင်း", "အလျှောက်ကောင်း"),
                ("ပညာရှိ သတိဖြစ်ခဲ့ ဆိုရိုးရှိသည်။", "သတိဖြစ်ခဲ့", "သတိဖြစ်ခဲ"),
                ("မိချောင်းမင်း ရေခင်းပြ မလုပ်ပါနှင့်။", "မိချောင်းမင်း", "မိကျောင်းမင်း"),
                ("ဆင်သွားလျှင် လန်းဖြစ် စမြဲပင်။", "လန်းဖြစ်", "လမ်းဖြစ်")
            };
            foreach (var mp in misquotedProverbs)
            {
                var iss = engine.CheckText(mp.Input);
                Assert.True(iss.Any(i => i.OriginalText == mp.Wrong && i.Suggestions.Count > 0 && i.Suggestions[0].Replacement == mp.Fix),
                    $"Failed misquoted proverb check on '{mp.Input}': expected '{mp.Wrong}' -> '{mp.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 4. Verify Persistent Offline User / Ministry Custom Dictionary Round-Trip
            string tempCustomDict = Path.Combine(Path.GetTempPath(), $"mm_custom_dict_{Guid.NewGuid():N}.txt");
            try
            {
                string novelMinistryAcronym = "နေပြည်တော်ဇီဝသိပ္ပံဗဟို";
                Assert.True(engine.AddCustomWord("ဇီဝသိပ္ပံဗဟို", tempCustomDict));
                Assert.True(File.Exists(tempCustomDict));

                var engineReloaded = new MyanmarProofingEngine(new MyanmarTrie(), ProofingStyleMode.GeneralStandard);
                foreach (var e in SeedDictionaryProvider.GetOfficialSeedEntries()) engineReloaded.Trie.InsertEntry(e);
                int countLoaded = engineReloaded.LoadCustomDictionaryFile(tempCustomDict);
                Assert.Equal(1, countLoaded);
                Assert.Empty(engineReloaded.CheckText("ဇီဝသိပ္ပံဗဟို"));
                Assert.Empty(engine.CheckText(novelMinistryAcronym));
            }
            finally
            {
                if (File.Exists(tempCustomDict)) File.Delete(tempCustomDict);
            }
        }

        [Fact]
        public void V13_SubjectPostpositionMhaVsKa_SibilantSaVsHsa_PaliLingaHomophones_AndCoreANouns_WorkWithZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Exact user screenshot error lines + systemic family expansions
            var v13Errors = new (string Input, string Wrong, string Fix)[]
            {
                // Screenshot exact error lines
                ("မည်သို့ပင်စိုစေကာမူ", "စို", "ဆို"),
                ("အပန်ဆိုက်ကြစို့", "အပန်", "အပင်"),
                ("အပန်ဆိုက်ကြစို့", "ဆိုက်", "စိုက်"),
                ("အပင်ဆိုက်ကြစို့", "ဆိုက်", "စိုက်"),
                ("နေပြည်တော်မှကြိုဆိုပါသည်", "မှ", "က"),
                ("ဒွိလိမ်", "ဒွိလိမ်", "ဒွိလိင်"),
                // Systemic family expansions
                ("ရန်ကုန်မြို့မှ လှိုက်လှဲစွာ ကြိုဆိုပါသည်။", "မှ", "က"),
                ("ဝန်ကြီးဌာနမှ အသိပေးကြေညာအပ်ပါသည်။", "မှ", "က"),
                ("ဥက္ကဋ္ဌမှ အမှာစကား ပြောကြားသည်။", "မှ", "က"),
                ("ကျွန်တော်မှ တင်ပြအပ်ပါသည်။", "မှ", "က"),
                ("ဘာပဲစိုစို ကြိုးစားမည်။", "စိုစို", "ဆိုဆို"),
                ("စိုလိုရင်း အဓိပ္ပာယ်မှာ အောက်ပါအတိုင်းဖြစ်သည်။", "စို", "ဆို"),
                ("မိုးရေ ဆိုစွတ်နေသည်။", "ဆို", "စို"),
                ("သစ်ပင်များ ဆိုက်ပျိုးကြသည်။", "ဆိုက်", "စိုက်"),
                ("အလံတော် ဆိုက်ထူသည်။", "ဆိုက်", "စိုက်"),
                ("သင်္ဘော စိုက်ကပ်လာသည်။", "စိုက်", "ဆိုက်"),
                ("ပုလ္လိမ်နှင့် ဣတ္ထိလိမ် ခွဲခြားမှု။", "ပုလ္လိမ်", "ပုလ္လိင်"),
                ("ပုလ္လိမ်နှင့် ဣတ္ထိလိမ် ခွဲခြားမှု။", "ဣတ္ထိလိမ်", "ဣတ္ထိလိင်"),
                ("လိမ်ပိုင်းဆိုင်ရာ အသိပညာပေးခြင်း။", "လိမ်", "လိင်"),
                ("ငွေကြေး ဆုဆောင်းပြီး ဆင်းရဲသားအတွက် စုတောင်းပေးသည်။", "ဆုဆောင်း", "စုဆောင်း"),
                ("ငွေကြေး ဆုဆောင်းပြီး ဆင်းရဲသားအတွက် စုတောင်းပေးသည်။", "စုတောင်း", "ဆုတောင်း")
            };

            foreach (var tc in v13Errors)
            {
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                    (i.OriginalText.Contains(tc.Wrong) || tc.Wrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Count > 0 &&
                    i.Suggestions[0].Replacement.Contains(tc.Fix)),
                    $"Failed V13 check on '{tc.Input}': expected '{tc.Wrong}' -> '{tc.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Exact user screenshot clean lines + valid ablative/conditional 'မှ' & valid 'စိုက်/ဆိုက်/စို/ဆို/လိင်/လိမ်' -> ZERO False Positives!
            string[] v13Clean =
            {
                "မည်သို့ပင်ဆိုစေ",
                "မည်သို့ပင်ဆိုစေကာမူ",
                "အပင်စိုက်ကြစို့",
                "နေပြည်တော်ကကြိုဆိုပါသည်",
                "ဒွိလိင်",
                "ရန်ကုန်မြို့မှ မန္တလေးမြို့သို့ ထွက်ခွာလာသည်။",
                "ဝန်ကြီးက လေဆိပ်မှ ကြိုဆိုသည်။",
                "ဧည့်သည်များ ရောက်လာမှ ကြိုဆိုပါမည်။",
                "ဘယ်သူမှ မကြိုဆိုကြပါ။",
                "သင်္ဘော ဆိုက်ကပ်လာသည်။",
                "မိုးရေ စိုစွတ်နေသည်။",
                "အရွက်တွင် လက်ဖက်၊ အသီးတွင် သရက်။",
                "လိမ်ညာပြောဆိုခြင်းကို ရှောင်ကြဉ်ပါ။"
            };

            foreach (string clean in v13Clean)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V13 false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V14_BotanicalFruitCompounds_SyllableTransposition_LabializedAnt_AndSoeVsHsoe_WorkWithZeroFalsePositives()
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

            // 1. Exact 8 error lines from user screenshot (verify EXACT span & Top-1 suggestion!)
            var exactScreenshotErrors = new (string Input, string ExactSpan, string Top1Fix)[]
            {
                ("ဖူးသီး", "ဖူးသီး", "ဘူးသီး"),
                ("မလကာသီး", "မလကာသီး", "မာလကာသီး"),
                ("မကလာသီး", "မကလာသီး", "မာလကာသီး"),
                ("မာကလာသီး", "မာကလာသီး", "မာလကာသီး"),
                ("ခြိုလိုက်တာ", "ခြို", "ချို"),
                ("စိုးချင်စိုးမယ်", "စိုးချင်စိုး", "ဆိုးချင်ဆိုး"),
                ("ပုရုတ်ဆိတ်", "ပုရုတ်ဆိတ်", "ပုရွက်ဆိတ်"),
                ("စိုးပေ", "စိုးပေ", "ဆိုးပေ")
            };

            foreach (var tc in exactScreenshotErrors)
            {
                var gIss = generalEngine.CheckText(tc.Input);
                Assert.True(gIss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V14 General exact check on '{tc.Input}': expected '{tc.ExactSpan}' -> #1 '{tc.Top1Fix}', got [{string.Join("; ", gIss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

                var fIss = formalEngine.CheckText(tc.Input);
                Assert.True(fIss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExactSpan) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.Top1Fix)),
                    $"Failed V14 Formal exact check on '{tc.Input}': expected '{tc.ExactSpan}' -> #1 '{tc.Top1Fix}', got [{string.Join("; ", fIss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // In FormalOfficial mode, 'စိုးချင်စိုးမယ်' must flag BOTH 'စိုးချင်စိုး' -> 'ဆိုးချင်ဆိုး' AND 'မယ်' -> 'မည်'!
            var formalSoeIssues = formalEngine.CheckText("စိုးချင်စိုးမယ်");
            Assert.Contains(formalSoeIssues, i => i.OriginalText == "စိုးချင်စိုး" && i.Suggestions[0].Replacement == "ဆိုးချင်ဆိုး");
            Assert.Contains(formalSoeIssues, i => i.OriginalText == "မယ်" && i.Suggestions[0].Replacement == "မည်");

            // 2. Systemic generative expansions (unseen transpositions, botanical fruits, labialized -ွက်/-ွတ် <-> -ုတ်, စိုး/ဆိုး & ဖူး/ဘူး)
            var systemicExpansions = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("မာကာလသီး", "မာကာလသီး", "မာလကာသီး"),
                ("ပုဆိတ်ရွက်", "ပုဆိတ်ရွက်", "ပုရွက်ဆိတ်"),
                ("ရွက်ပုဆိတ်", "ရွက်ပုဆိတ်", "ပုရွက်ဆိတ်"),
                ("မလကာသီး မစားဘူး။", "မလကာသီး", "မာလကာသီး"),
                ("ဗူးသီး", "ဗူးသီး", "ဘူးသီး"),
                ("ဖူးညွန့် ဟင်းချို", "ဖူးညွန့်", "ဘူးညွန့်"),
                ("ထမင်းဖူး ယူလာသည်။", "ထမင်းဖူး", "ထမင်းဘူး"),
                ("ပိနဲသီး စားချင်တယ်။", "ပိနဲသီး", "ပိန္နဲသီး"),
                ("ဒူရင်းသီး မွှေးတယ်။", "ဒူရင်းသီး", "ဒူးရင်းသီး"),
                ("သခွာသီး သုပ်စားတယ်။", "သခွာသီး", "သခွားသီး"),
                ("မင်းကုတ်သီး ဝယ်လာတယ်။", "မင်းကုတ်သီး", "မင်းကွတ်သီး"),
                ("မရမ်းသီး သုပ်စားတယ်။", "မရမ်းသီး", "မရန်းသီး"),
                ("စက်ပစ္စည်း ချုတ်ယွင်းနေသည်။", "ချုတ်ယွင်း", "ချွတ်ယွင်း"),
                ("ကြည်နူးဆုတ်ပျံ့ဖွယ် ကောင်းသည်။", "ဆုတ်ပျံ့", "ဆွတ်ပျံ့"),
                ("အခြေအနေ အလွန်စိုးရွားနေသည်။", "စိုးရွား", "ဆိုးရွား"),
                ("စိုးကျိုး သက်ရောက်မှု ရှိသည်။", "စိုးကျိုး", "ဆိုးကျိုး"),
                ("ကျန်းမာရေးအတွက် ဆိုးရိမ်ရသည်။", "ဆိုးရိမ်", "စိုးရိမ်"),
                ("တရားဥပဒေဆိုးမိုးရေး ဆောင်ရွက်သည်။", "တရားဥပဒေဆိုးမိုး", "တရားဥပဒေစိုးမိုး"),
                ("နိုင်ငံတော် အဆိုးရအဖွဲ့။", "အဆိုးရ", "အစိုးရ")
            };

            foreach (var sc in systemicExpansions)
            {
                var iss = generalEngine.CheckText(sc.Input);
                Assert.True(iss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText).Contains(UnicodeNormalizer.NormalizeCanonicalOrder(sc.ExpectedWrong)) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement).Contains(UnicodeNormalizer.NormalizeCanonicalOrder(sc.ExpectedFix))),
                    $"Failed V14 systemic check on '{sc.Input}': expected '{sc.ExpectedWrong}' -> '{sc.ExpectedFix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 3. Exact screenshot clean lines + linguistic traps (MUST produce ZERO False Positives!)
            string[] mustStayCleanBothModes =
            {
                "ဘူးသီး",
                "မာလကာသီး",
                "ဆိုးပေ",
                "ကြီးစိုးပေသည်။",
                "အုပ်စိုးပေ၏။",
                "စိတ်စိုးရိမ်သည်။",
                "ငွေစုဖူးသည်။",
                "လက်ဆေးဖူးသည်။",
                "အပြင်ထွက်သည်။",
                "အဝတ်ကို ရေစွပ်ထားသည်။",
                "ဦးစိုးပေ တက်ရောက်သည်။"
            };

            foreach (string clean in mustStayCleanBothModes)
            {
                var gFp = generalEngine.CheckText(clean);
                Assert.True(gFp.Count == 0,
                    $"Unexpected V14 General false positive on '{clean}': [{string.Join("; ", gFp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

                var fFp = formalEngine.CheckText(clean);
                Assert.True(fFp.Count == 0,
                    $"Unexpected V14 Formal false positive on '{clean}': [{string.Join("; ", fFp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V15_MyanmarPublishedBooksAudit_TrueErrorsCaught_AndLiteraryVocabularyHasZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Category I: Where Myanmar Published Books / Pre-1986 Typesetters Were Genuinely Wrong per 462-Page သတ်ပုံကျမ်း
            var publishedBookErrors = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                // Medial -ွ / -ှ + Tall -ါ -> Round -ာ
                ("ချမ်းသာကြီးပွါးကြောင်း တရားတော်။", "ကြီးပွါး", "ကြီးပွား"),
                ("စီးပွါးဥစ္စာ တိုးတက်စေသည်။", "စီးပွါး", "စီးပွား"),
                ("ဝိပဿနာကို ပွါးစေလျက် အရဟတ္တဖိုလ်သို့ ရောက်တော်မူလေသည်။", "ပွါး", "ပွား"),
                ("ထိုအရပ်မှ ဖဲခွါသွားလေ၏။", "ခွါ", "ခွာ"),
                ("ကောင်းကျိုး ပြည့်စုံစေခြင်းငှါ ဆုတောင်းသည်။", "ငှါ", "ငှာ"),
                // Pre-1986 '~ဘွယ်' -> '~ဖွယ်'
                ("လှူဘွယ်ဝတ္ထု အစုစုတို့ကို ဆက်ကပ်လှူဒါန်းသည်။", "ဘွယ်", "ဖွယ်"),
                ("အံ့ဘွယ်သရဲ ထူးကဲလှပေ၏။", "ဘွယ်", "ဖွယ်"),
                ("ချစ်ဘွယ်သော သားငယ်ကို ပွေ့ဖက်သည်။", "ဘွယ်", "ဖွယ်"),
                ("သနားဘွယ် ကောင်းလှပေသည်။", "ဘွယ်", "ဖွယ်"),
                ("မှတ်သားဘွယ် စကားပုံများ။", "ဘွယ်", "ဖွယ်"),
                // Affirmative experiential '[Verb] + ဘူး + (ခဲ့|သည်|၏|သော|သည့်)' -> 'ဖူး'
                ("ရှေးအခါက မြင်ဘူးခဲ့၏။", "ဘူး", "ဖူး"),
                ("မိမိ ကြားဘူးသော တရားကို ပြန်ဟောသည်။", "ဘူး", "ဖူး"),
                ("ထိုသို့ ဖြစ်ဘူးသည် ဟူ၍ ဆို၏။", "ဘူး", "ဖူး"),
                // Pre-1986 numeral 'တ' without 'စ်' (including 'တရာ -> တစ်ရာ' and 'တထောင် -> တစ်ထောင်')
                ("နှစ်ပေါင်းတရာ ကြာမြင့်ခဲ့ပြီ။", "တရာ", "တစ်ရာ"),
                ("သက်တော်တရာ ပြည့်မြောက်သည်။", "တရာ", "တစ်ရာ"),
                ("ဂါထာတရာ ပါဝင်သော ကျမ်းစာ။", "တရာ", "တစ်ရာ"),
                ("လူပေါင်းတထောင် တက်ရောက်သည်။", "တထောင်", "တစ်ထောင်"),
                ("ရဟန်းတပါး ကြွလာတော်မူ၏။", "တပါး", "တစ်ပါး"),
                ("တနည်းအားဖြင့် ဆိုသော် မှန်ကန်၏။", "တနည်း", "တစ်နည်း"),
                // Verified book orthography errors per 462-Page သတ်ပုံကျမ်း
                ("ဆရာတော်ထံ နိဿယည်း ခံယူသည်။", "နိဿယည်း", "နိဿရည်း"),
                ("စက္ခုဝိညာဏ် ဖြစ်ပေါ်လာသည်။", "ဝိညာဏ်", "ဝိညာဉ်"),
                ("ဆဋ္ဌသံဂါယနာ တင်တော်မူသည်။", "သံဂါယနာ", "သင်္ဂါယနာ"),
                ("ပုထုဇန် ပုဂ္ဂိုလ်တို့၏ သဘော။", "ပုထုဇန်", "ပုထုဇဉ်"),
                ("ဒီပဲရင်းမြို့နယ်တွင် မွေးဖွားသည်။", "ဒီပဲရင်း", "ဒီပဲယင်း"),
                ("နဲနဲပါးပါး သိရှိထားသည်။", "နဲနဲ", "နည်းနည်း"),
                ("အောက်ပါအတိုင်း ဘော်ပြထားပါသည်။", "ဘော်ပြ", "ဖော်ပြ"),
                ("အခမ်းအနား ဘွင့်လှစ်သည်။", "ဘွင့်လှစ်", "ဖွင့်လှစ်"),
                ("အဓိပ္ပါယ် ပြည့်စုံလှပေသည်။", "အဓိပ္ပါယ်", "အဓိပ္ပာယ်")
            };

            foreach (var tc in publishedBookErrors)
            {
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText).Contains(UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedWrong)) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement).Contains(UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedFix))),
                    $"Failed V15 book error check on '{tc.Input}': expected '{tc.ExpectedWrong}' -> '{tc.ExpectedFix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Category II: Where Myanmar Published Books Are Right — Must Produce ZERO False Positives!
            string[] publishedBookCleanSentences =
            {
                "မြတ်စွာဘုရားသည် တရားတော်ကို ဟောကြားပြတော်မူ၏။",
                "အရှုခံ ရုပ်တရားနှင့် နာမ်တရားတို့ကို ပိုင်းခြားသိတော်မူ၏။",
                "ထိုအကြောင်းကား ထင်ရှားလှပေ၏။",
                "အကျဉ်းနှင့် အကျယ် ခွဲခြားဖော်ပြထားသည်။",
                "ဗြဟ္မာ့ဘုံနှင့် သာသနာ့ဝန်ထမ်း သံဃာ့အဖွဲ့အစည်း။",
                "ပန်းသွင် လသွင် တင့်တယ်စွာ ရှိပေ၏။",
                "လိမ့်မည်ကိုသောဝ် ဟု ပျို့ကဗျာတွင် စပ်ဆိုခဲ့သည်။",
                "မဂ္ဂါမဂ္ဂဉာဏဒဿနဝိသုဒ္ဓိ ဖြစ်ပေါ်လာ၏။",
                "နမော တဿ ဘဂဝတော အရဟတော သမ္မာသမ္ဗုဒ္ဓဿ။",
                "ဧကာယနော အယံ ဘိက္ခဝေ မဂ္ဂေါ သတ္တာနံ ဝိသုဒ္ဓိယာ။",
                "ရူပါဝစရ ကုသိုလ်စိတ်နှင့် အရူပါဝစရ ဈာန်ကို ရရှိသော ယောဂါဝစရ ပုဂ္ဂိုလ်။",
                "ကာယာနုပဿနာ၊ ဝေဒနာနုပဿနာ၊ စိတ္တာနုပဿနာ၊ ဓမ္မာနုပဿနာ သတိပဋ္ဌာန်လေးပါး။",
                "ဗုဒ္ဓါနုဿတိနှင့် မရဏဿတိ ကမ္မဋ္ဌာန်းကို ပွားများအားထုတ်သည်။",
                "သမ္မပ္ပဓာန် လေးပါးနှင့် သမ္မသနဉာဏ် ရင့်သန်စေသည်။",
                "သောတာပတ္တိမဂ်၊ သကဒါဂါမိမဂ်၊ အနာဂါမိမဂ်၊ အရဟတ္တမဂ်နှင့် အရိယဖိုလ် လေးပါး။",
                "ဝိဝါဒါဓိကရုဏ်း စသော အဓိကရုဏ်း လေးပါးကို ဆုံးဖြတ်တော်မူသည်။",
                "မာဂဓီဘာသာသည် သဘာဝနိရုတ္တိ ဖြစ်ပေသည်။",
                "ပါဋလိပုတ်ပြည် ကုက္ကုဋာရာမကျောင်းတိုက်၌ အရှင်သာရိပုတြာ သီတင်းသုံးတော်မူ၏။",
                "စစ်ကိုင်းမြို့နယ် သံဃနာယက အဖွဲ့နှင့် ဉေယျဓမ်တရား။",
                "ပျော်ဘွယ်မြို့နယ်နှင့် ဒဂုန်မြို့နယ်တို့တွင် တည်ရှိသည်။"
            };

            foreach (string clean in publishedBookCleanSentences)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V15 false positive on published book sentence '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }
    }
}
