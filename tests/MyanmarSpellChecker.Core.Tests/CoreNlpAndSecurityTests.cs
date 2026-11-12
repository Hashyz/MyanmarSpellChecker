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
    public class CoreNlpAndSecurityTests
    {
        [Fact]
        public void SyllableBreaker_SegmentsClosedAndCompoundSyllablesCorrectly()
        {
            List<string> s1 = MyanmarSyllableBreaker.BreakIntoSyllables("နိုင်ငံတော်");
            Assert.Equal(new[] { "နိုင်", "ငံ", "တော်" }, s1);

            List<string> s2 = MyanmarSyllableBreaker.BreakIntoSyllables("တည်ဆောက်");
            Assert.Equal(new[] { "တည်", "ဆောက်" }, s2);

            List<string> s3 = MyanmarSyllableBreaker.BreakIntoSyllables("ဝန်ကြီးဌာန");
            Assert.Equal(new[] { "ဝန်", "ကြီး", "ဌာ", "န" }, s3);
        }

        [Fact]
        public void UnicodeNormalizer_NormalizesSwappedCombiningMarks()
        {
            // Swapped ု + ိ instead of ိ + ု
            string nonCanonical = "န\u102F\u102Dင်ငံတော်";
            string normalized = UnicodeNormalizer.NormalizeCanonicalOrder(nonCanonical);
            Assert.Equal("နိုင်ငံတော်", normalized);
        }

        [Fact]
        public void ProofingEngine_FlagsMisusedDigitZeroAsLookalikeError()
        {
            var engine = TestPaths.CreateSeededEngine();

            // '၀န်ကြီးဌာန' using Digit Zero U+1040 instead of Consonant Wa U+101D
            string text = "၀န်ကြီးဌာနနှင့် တာ၀န်";
            List<ProofingIssue> issues = engine.CheckText(text);

            Assert.Contains(issues, i =>
                i.OriginalText == "၀န်ကြီးဌာန" &&
                i.IssueType == ProofingIssueType.LookalikeCharacterError &&
                i.Suggestions.Any(s => s.Replacement == "ဝန်ကြီးဌာန"));

            Assert.Contains(issues, i =>
                i.OriginalText == "တာ၀န်" &&
                i.IssueType == ProofingIssueType.LookalikeCharacterError &&
                i.Suggestions.Any(s => s.Replacement == "တာဝန်"));
        }

        [Fact]
        public void ProofingEngine_FlagsThatponHomophoneCompoundErrorsWithMeaningCues()
        {
            var engine = TestPaths.CreateSeededEngine();

            string text = "တီဆောက်ရေး လက်မတ်ထိုးပြီး ကြော်ညာချက်ထုတ်ပြန်သည်။";
            List<ProofingIssue> issues = engine.CheckText(text);

            var tiSauk = issues.FirstOrDefault(i => i.OriginalText == "တီဆောက်");
            Assert.NotNull(tiSauk);
            Assert.Contains(tiSauk!.Suggestions, s => s.Replacement == "တည်ဆောက်" && s.MeaningCue.Contains("အဆောက်အအုံ"));

            var letMat = issues.FirstOrDefault(i => i.OriginalText == "လက်မတ်");
            Assert.NotNull(letMat);
            Assert.Contains(letMat!.Suggestions, s => s.Replacement == "လက်မှတ်");

            var kyawNyar = issues.FirstOrDefault(i => i.OriginalText == "ကြော်ညာ");
            Assert.NotNull(kyawNyar);
            Assert.Contains(kyawNyar!.Suggestions, s => s.Replacement == "ကြေညာ");
        }

        [Fact]
        public void ProofingEngine_EnforcesTaVsTaitNumeralRulesAndOfficialStyle()
        {
            var engine = TestPaths.CreateSeededEngine();

            string text = "ဝန်ထမ်းတယောက်သည် စီမံကိန်းတခုအား စစ်ဆေးပြီး စာရွက်တွေ တင်ပြပါတယ်။";
            List<ProofingIssue> issues = engine.CheckText(text);

            // 1. 'တယောက်' -> 'တစ်ယောက်'
            Assert.Contains(issues, i =>
                i.OriginalText == "တယောက်" &&
                i.IssueType == ProofingIssueType.NumeralClassifierError &&
                i.Suggestions.Any(s => s.Replacement == "တစ်ယောက်"));

            // 2. 'တခု' -> 'တစ်ခု'
            Assert.Contains(issues, i =>
                i.OriginalText == "တခု" &&
                i.IssueType == ProofingIssueType.NumeralClassifierError &&
                i.Suggestions.Any(s => s.Replacement == "တစ်ခု"));

            // 3. 'အား စစ်ဆေး' -> 'ကို'
            Assert.Contains(issues, i =>
                i.OriginalText == "အား" &&
                i.IssueType == ProofingIssueType.OfficialStyleGrammarError &&
                i.Suggestions.Any(s => s.Replacement == "ကို"));

            // 4. Colloquial 'တွေ' -> 'များ' and 'ပါတယ်' -> 'ပါသည်'
            Assert.Contains(issues, i =>
                i.OriginalText == "တွေ" &&
                i.Suggestions.Any(s => s.Replacement == "များ"));

            Assert.Contains(issues, i =>
                i.OriginalText == "ပါတယ်" &&
                i.Suggestions.Any(s => s.Replacement == "ပါသည်"));
        }

        [Fact]
        public void SqlCipherRepository_EncryptsDatabaseAndRejectsWrongPassword()
        {
            string tempDb = Path.Combine(Path.GetTempPath(), $"test_thatpon_{Guid.NewGuid():N}.db");
            try
            {
                const string validPassword = "Correct-Ministry-Key-2026!";
                var repo = new EncryptedDictionaryRepository(tempDb);
                repo.CompileEncryptedDatabase(
                    validPassword,
                    SeedDictionaryProvider.GetOfficialSeedEntries(),
                    SeedDictionaryProvider.GetOfficialOrthographyRules());

                // Loading with valid password succeeds
                MyanmarTrie trie = repo.LoadIntoTrie(validPassword);
                Assert.True(trie.WordCount > 20);
                Assert.True(trie.ContainsExactWord("ပြည်ထောင်စု"));

                // Loading with wrong password throws CryptographicException (HMAC-SHA256 verification fails)
                Assert.Throws<CryptographicException>(() => repo.LoadIntoTrie("Wrong-Key!"));
            }
            finally
            {
                if (File.Exists(tempDb))
                    File.Delete(tempDb);
            }
        }

        [Fact]
        public void OfflineLicenseService_VerifiesRsaSignatureHardwareLockAndUnwrapsSqlCipherKey()
        {
            var (privKey, pubKey) = LicenseService.GenerateRsaKeyPair();
            const string expectedDbKey = "Secret-SQLCipher-MasterKey-999";
            const string targetMachineId = "MMGOV-1122-3344-5566";

            var payload = new LicensePayload
            {
                MinistryName = "ပြည်ထဲရေးဝန်ကြီးဌာန",
                Mode = LicenseMode.SingleMachine,
                MachineId = targetMachineId,
                ExpiresUtc = DateTime.UtcNow.AddYears(2)
            };

            string signedJson = LicenseService.CreateSignedLicenseJson(payload, expectedDbKey, privKey);

            // 1. Valid on matching Machine ID
            var validResult = LicenseService.ValidateLicenseJson(signedJson, pubKey, currentMachineIdOverride: targetMachineId);
            Assert.True(validResult.IsValid);
            Assert.Equal(expectedDbKey, validResult.SqlCipherDatabaseKey);

            // 2. Rejected on different Machine ID
            var wrongPcResult = LicenseService.ValidateLicenseJson(signedJson, pubKey, currentMachineIdOverride: "MMGOV-FFFF-0000-1111");
            Assert.False(wrongPcResult.IsValid);
        }

        [Fact]
        public void ContextDisambiguator_CatchesValidSyllablesUsedInWrongHomophoneContext()
        {
            var engine = TestPaths.CreateSeededEngine();

            string text = "အချိန်မှီ ရောက်ရှိသော မော်တော်ယဉ်သည် အမိန့်ကြေညာစာနှင့် သတင်းထုတ်ပြန်ကြော်ငြာချက်ကို သယ်ဆောင်လာသည်။";
            List<ProofingIssue> issues = engine.CheckText(text);

            Assert.Contains(issues, i =>
                i.OriginalText == "အချိန်မှီ" &&
                i.Suggestions.Any(s => s.Replacement == "အချိန်မီ"));

            Assert.Contains(issues, i =>
                i.OriginalText == "မော်တော်ယဉ်" &&
                i.Suggestions.Any(s => s.Replacement == "မော်တော်ယာဉ်"));

            Assert.Contains(issues, i =>
                i.OriginalText == "အမိန့်ကြေညာစာ" &&
                i.Suggestions.Any(s => s.Replacement == "အမိန့်ကြော်ငြာစာ"));

            Assert.Contains(issues, i =>
                i.OriginalText == "သတင်းထုတ်ပြန်ကြော်ငြာချက်" &&
                i.Suggestions.Any(s => s.Replacement == "သတင်းထုတ်ပြန်ကြေညာချက်"));
        }

        [Fact]
        public void DictionarySearch_ReturnsExactAndPhoneticHomophoneSiblingsWithMeaningCues()
        {
            var engine = TestPaths.CreateSeededEngine();

            // Searching 'ချည်' returns both 'ချည် [ကြိုး~]' and its phonetic homophone 'ချဉ် [အရသာ~]'
            List<DictionaryEntry> results = engine.SearchDictionary("ချည်");
            Assert.Contains(results, r => r.Word == "ချည်" && r.MeaningCue.Contains("ကြိုး"));
            Assert.Contains(results, r => r.Word == "ချဉ်" && r.MeaningCue.Contains("အရသာ"));
        }

        [Fact]
        public void FullMasterCorpus_33kHeadwords_ZeroFalsePositivesOnValidText_AndCatchesNewRules()
        {
            // Locate repo root data/mlc_thatpon_master.json
            string masterJsonPath = TestPaths.MasterJsonPath;
            Assert.True(File.Exists(masterJsonPath), $"Master JSON not found at {masterJsonPath}");

            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);
            Assert.True(model.Entries.Count > 30000);
            Assert.True(model.OrthographyRules.Count >= 100);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            var engine = new MyanmarProofingEngine(trie);

            // 1. Valid everyday & official Ministry sentences (including personal names & townships) must produce ZERO false positives
            string validSentence = "ပညာရေးဝန်ကြီးဌာနမှ ဦးအောင်ကျော်မိုးနှင့် ဒေါ်ခင်မေသန်းတို့သည် ပျဉ်းမနားမြို့နယ်ရှိ ကျောင်းသားများအတွက် စာအုပ်များကို စနစ်တကျ ပေးအပ်ခဲ့သည်။";
            List<ProofingIssue> falsePositives = engine.CheckText(validSentence);
            Assert.Empty(falsePositives);

            // 2. Catch new Lookalikes (ဥ် -> ဉ်, ဓါတ် -> ဓာတ်) & New Grammar Rules (ခုနစ်/ခုနှစ်, မ...ဘဲ, လဲ/လည်း, ဘက်/ဖက်)
            string errorSentence = "၂၀၂၆ ခုနစ်တွင် ဝန်ထမ်း ခုနှစ်ယောက်သည် အစီအစဥ်ကို မသိပဲ ဘယ်ဖက်သို့ သွားမည်မှာ ဘာလည်း။";
            List<ProofingIssue> detected = engine.CheckText(errorSentence);

            Assert.Contains(detected, i => i.OriginalText == "ခုနစ်" && i.Suggestions.Any(s => s.Replacement == "ခုနှစ်"));
            Assert.Contains(detected, i => i.OriginalText == "ခုနှစ်" && i.Suggestions.Any(s => s.Replacement == "ခုနစ်"));
            Assert.Contains(detected, i => i.OriginalText == "အစီအစဥ်" && i.Suggestions.Any(s => s.Replacement == "အစီအစဉ်"));
            Assert.Contains(detected, i => i.OriginalText == "ပဲ" && i.Suggestions.Any(s => s.Replacement == "ဘဲ"));
            Assert.Contains(detected, i => i.OriginalText == "ဘယ်ဖက်" && i.Suggestions.Any(s => s.Replacement == "ဘယ်ဘက်"));
            Assert.Contains(detected, i => i.OriginalText == "လည်း" && i.Suggestions.Any(s => s.Replacement == "လဲ"));

            // 3. End-to-End Batch "Fix All" Regression Test (no overlapping 'ပါတယ်'+'တယ်' -> 'ပါသည်သည်', and 0 issues after Fix All)
            foreach (var se in SeedDictionaryProvider.GetOfficialSeedEntries()) trie.InsertEntry(se);
            foreach (var sr in SeedDictionaryProvider.GetOfficialOrthographyRules()) trie.InsertOrthographyRule(sr);

            string rawMinistryDraft = "၂၀၂၆ ခုနစ်တွင် ၀န်ကြီးဌာနမှ ဝန်ထမ်းတယောက်သည် အစီအစဥ်အား စစ်ဆေးပြီး ရမ်ပုံငွေ သတ်မတ်ချက်တွေကို မသိပဲ လက်မတ်ထိုးကာ တင်ပြပါတယ်။";
            List<ProofingIssue> draftIssues = engine.CheckText(rawMinistryDraft);

            // Apply replacements in reverse order exactly like SafeUnderlineManager.ReplaceAllIssuesInDocument
            string fixedText = rawMinistryDraft;
            foreach (var issue in draftIssues.Where(i => i.Suggestions.Count > 0).OrderByDescending(i => i.StartIndex))
            {
                fixedText = fixedText.Substring(0, issue.StartIndex)
                            + issue.Suggestions[0].Replacement
                            + fixedText.Substring(issue.StartIndex + issue.Length);
            }

            Assert.Equal(
                "၂၀၂၆ ခုနှစ်တွင် ဝန်ကြီးဌာနမှ ဝန်ထမ်းတစ်ယောက်သည် အစီအစဉ်ကို စစ်ဆေးပြီး ရန်ပုံငွေ သတ်မှတ်ချက်များကို မသိဘဲ လက်မှတ်ထိုးကာ တင်ပြပါသည်။",
                fixedText);

            // Re-checking the fixed sentence must yield ZERO remaining errors!
            Assert.Empty(engine.CheckText(fixedText));

            // 4. Boundary guards: 'သမ္မတဦး...', 'တင့်တယ်', 'ချိနဲ့', 'အမယ်' must produce ZERO false positives
            string edgeCaseValid = "နိုင်ငံတော်သမ္မတဦးဝင်းမြင့်သည် ဂုဏ်သိက္ခာတင့်တယ်စွာ ဆောင်ရွက်ပြီး ချိနဲ့သော သက်ကြီးရွယ်အိုများအား စားသောက်ဖွယ်အမယ် များကို ပေးအပ်သည်။";
            Assert.Empty(engine.CheckText(edgeCaseValid));
        }

        [Fact]
        public void GenerativeEngine_DetectsArbitraryUserMistakes_WithZeroPredefinedWrongFormRules_AndExplainsExactDiffs()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            // Load ONLY valid dictionary headwords — ZERO predefined WrongForm rules!
            var pureLexiconTrie = new MyanmarTrie();
            foreach (var e in model.Entries)
            {
                pureLexiconTrie.InsertEntry(e);
            }
            Assert.Equal(0, pureLexiconTrie.RuleCount);

            var engine = new MyanmarProofingEngine(pureLexiconTrie, ProofingStyleMode.GeneralStandard);

            var arbitraryMistakes = new (string Wrong, string ExpectedCorrect)[]
            {
                ("ပတ်သတ်", "ပတ်သက်"),
                ("အကျောင်းအရာ", "အကြောင်းအရာ"),
                ("အကြောင်အရာ", "အကြောင်းအရာ"),
                ("စီပွားရေး", "စီးပွားရေး"),
                ("ရေရွယ်ချက်", "ရည်ရွယ်ချက်"),
                ("ပြသနာ", "ပြဿနာ"),
                ("ပုဂိုလ်", "ပုဂ္ဂိုလ်"),
                ("သဏ္ဍာန်", "သဏ္ဌာန်"),
                ("မဂင်္လာ", "မင်္ဂလာ"),
                ("ကွန်ပြူတာ", "ကွန်ပျူတာ")
            };

            foreach (var pair in arbitraryMistakes)
            {
                List<ProofingIssue> issues = engine.CheckText(pair.Wrong);
                Assert.True(issues.Count >= 1, $"Expected generative engine to flag '{pair.Wrong}' with zero predefined rules.");
                var topIssue = issues[0];
                Assert.True(topIssue.Suggestions.Count > 0, $"Expected suggestions for '{pair.Wrong}'.");
                Assert.Equal(pair.ExpectedCorrect, topIssue.Suggestions[0].Replacement);
                Assert.False(string.IsNullOrWhiteSpace(topIssue.Suggestions[0].DiagnosticDetail), $"Expected DiagnosticDetail for '{pair.Wrong}' -> '{pair.ExpectedCorrect}'.");
                Assert.False(string.IsNullOrWhiteSpace(topIssue.Suggestions[0].SyllableBreakdown), $"Expected SyllableBreakdown for '{pair.ExpectedCorrect}'.");
            }
        }

        [Fact]
        public void WholeThatponBook_IncludesExtractedStems_RepairedKinzi_AndModernLoanwords()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);

            Assert.True(trie.OfficialThatponCount >= 16000);

            // Book classifier stems unlocked from 'ကညွတ်ပင်', 'ကထိန်ပွဲ', 'စင်ရော်ငှက်', 'ကကူရံငါး'
            Assert.True(trie.ContainsExactWord("ကညွတ်"));
            Assert.True(trie.ContainsExactWord("ကထိန်"));
            Assert.True(trie.ContainsExactWord("စင်ရော်"));
            Assert.True(trie.ContainsExactWord("ကကူရံ"));

            // Repaired Kinzi words & end-of-book 'ဥဿုံ' (while excluding archaic poem 'ညာဏ်' / 'ပုဂ္ဂို')
            Assert.True(trie.ContainsExactWord("ခြင်္သေ့"));
            Assert.True(trie.ContainsExactWord("သင်္ကန်း"));
            Assert.True(trie.ContainsExactWord("ဥဿုံ"));
            Assert.False(trie.ContainsExactWord("ပုဂ္ဂို"));

            // Modern standardized loanwords
            Assert.True(trie.ContainsExactWord("ကွန်ပျူတာ"));
            Assert.True(trie.ContainsExactWord("ဆော့ဖ်ဝဲ"));
            Assert.True(trie.ContainsExactWord("အင်တာနက်"));
        }

        [Fact]
        public void MultiClientStyleModes_GeneralStandardAllowsPoliteColloquial_WhileFormalOfficialConvertsToLiterary()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);

            string politeBusinessSentence = "ကျွန်တော် ဒီကွန်ပျူတာဆော့ဖ်ဝဲတွေကို စမ်းသပ်ကြည့်ထားပါတယ်။";

            // 1. GeneralStandard mode: 0 false positives on polite everyday/commercial Burmese!
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            Assert.Empty(engine.CheckText(politeBusinessSentence));

            // 2. GeneralStandard mode still catches real spelling & numeral mistakes while keeping 'နဲ့' and 'မယ်'
            string generalWithTypos = "ကျွန်တော် မိတ်ဆွေတယောက်နဲ့ ကွန်ပြူတာဝယ်ဖို့ သွားပါအုန်းမယ်။";
            var genIssues = engine.CheckText(generalWithTypos);
            Assert.Contains(genIssues, i => i.OriginalText == "တယောက်" && i.Suggestions[0].Replacement == "တစ်ယောက်");
            Assert.Contains(genIssues, i => i.OriginalText == "ကွန်ပြူတာ" && i.Suggestions[0].Replacement == "ကွန်ပျူတာ");
            Assert.Contains(genIssues, i => i.OriginalText == "ပါအုန်း" && i.Suggestions[0].Replacement == "ပါဦး");
            Assert.DoesNotContain(genIssues, i => i.OriginalText == "နဲ့" || i.OriginalText == "မယ်");

            // 3. FormalOfficial mode: converts colloquialisms 'တွေ' -> 'များ' and 'ပါတယ်' -> 'ပါသည်'
            engine.StyleMode = ProofingStyleMode.FormalOfficial;
            var formalIssues = engine.CheckText(politeBusinessSentence);
            Assert.Contains(formalIssues, i => i.OriginalText == "တွေ" && i.Suggestions.Any(s => s.Replacement == "များ"));
            Assert.Contains(formalIssues, i => i.OriginalText == "ပါတယ်" && i.Suggestions.Any(s => s.Replacement == "ပါသည်"));
        }

        [Fact]
        public void ViterbiWordLattice_CatchesRealWorldEverydayBusinessAndOfficeTypos_WithZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            var realWorldTypos = new (string Sentence, string ExpectedWrongSubspan, string ExpectedTopReplacementSubspan)[]
            {
                ("ကျေးဇူတင်ပါတယ်။", "ကျေးဇူ", "ကျေးဇူး"),
                ("အစိုးရအဖွဲက ထုတ်ပြန်သည်။", "အဖွဲ", "အဖွဲ့"),
                ("စာမေးပွဲဖေဆိုမည်။", "ဖေဆို", "ဖြေဆို"),
                ("အလုပ်လုပ်နေပါတရ်။", "တရ်", "တယ်"),
                ("မနက်ဖန်သွားမယ်။", "မနက်ဖန်", "မနက်ဖြန်"),
                ("အာလုံးပဲ မင်္ဂလာပါ။", "အာလုံး", "အားလုံး"),
                ("ကူညီးပေးပါ။", "ကူညီး", "ကူညီ"),
                ("ပောပြပေးပါ။", "ပောပြ", "ပြောပြ"),
                ("သူ့ကို ပောလိုက်ပါ။", "ပော", "ပြော"),
                ("ပင်ဆင်ချက် ထုတ်ပြန်သည်။", "ပင်ဆင်", "ပြင်ဆင်"),
                ("အောင်မင်မှု ရရှိပါစေ။", "အောင်မင်", "အောင်မြင်"),
                ("ကြိုစားအားထုတ်မှု ရှိသည်။", "ကြိုစား", "ကြိုးစား"),
                ("သတင်အချက်အလက် ထုတ်ပြန်သည်။", "သတင်", "သတင်း"),
                ("အခွင့်အရေ ရရှိမည်။", "အခွင့်အရေ", "အခွင့်အရေး"),
                ("ပညာသင်ကြာရေး အစီအစဉ်။", "သင်ကြာ", "သင်ကြား"),
                ("လေစားစွာဖြင့် တင်ပြအပ်ပါသည်။", "လေစား", "လေးစား"),
                ("အသိပေအကြောင်းကြားစာ ပေးပို့သည်။", "အသိပေ", "အသိပေး"),
                ("ဒါပေမယ့် သူမလာဘူး။", "ဒါပေမယ့်", "ဒါပေမဲ့"),
                ("စျေးဝယ်သွားမယ်။", "စျေး", "ဈေး"),
                ("သြဂုတ်လတွင် ကျင်းပမည်။", "သြဂုတ်", "ဩဂုတ်"),
                ("ဦးအောင်ကျော် လာပါသည်။", "ဦး", "ဦး"),
                ("ဘာလုပ်နေတာလည်း။", "လည်း", "လဲ"),
                ("ကျွန်တော်လဲ သွားမယ်။", "လဲ", "လည်း"),
                ("တစ်ကယ် ပြောတာပါ။", "တစ်ကယ်", "တကယ်"),
                ("တခါတလေ သွားပါတယ်။", "တခါ", "တစ်ခါ"),
                ("မသွားပဲ နေခဲ့တယ်။", "ပဲ", "ဘဲ"),
                ("မစဉ်းစားပဲ မလုပ်ပါနဲ့။", "ပဲ", "ဘဲ"),
                ("သူ မသိဘူ။", "ဘူ", "ဘူး"),
                ("စားပီးတော့ သွားမယ်။", "ပီး", "ပြီး"),
                ("စာဖက်နေပါတယ်။", "စာဖက်", "စာဖတ်"),
                ("ဒီအစီအစဉ်ကို သဘောကြတယ်။", "သဘောကြ", "သဘောကျ"),
                ("သဘာဝပတ်ဝန်းကြင် ထိန်းသိမ်းရေး။", "ပတ်ဝန်းကြင်", "ပတ်ဝန်းကျင်"),
                ("အသိညာဏ် ဖွံ့ဖြိုးတိုးတက်ရေး။", "ညာဏ်", "ဉာဏ်"),
                ("အမြဲတန်း ကြိုးစားနေပါတယ်။", "အမြဲတန်း", "အမြဲတမ်း"),
                ("ရာထူးနေရာ လစ်လက်နေပါသည်။", "လစ်လက်", "လစ်လပ်"),
                ("မုန်တိုင်းကြောင့် ပြတ်စီးဆုံးရှုံးမှု ရှိသည်။", "ပြတ်စီး", "ပျက်စီး"),
                ("လုပ်ငန်းများ ဆောင်ရွက်လျှက်ရှိသည်။", "လျှက်", "လျက်")
            };

            foreach (var tc in realWorldTypos)
            {
                string expWrongNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedWrongSubspan);
                string expFixNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedTopReplacementSubspan);
                var issues = engine.CheckText(tc.Sentence);

                Assert.True(issues.Any(iss =>
                {
                    string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText);
                    string topNorm = iss.Suggestions.Count > 0
                        ? UnicodeNormalizer.NormalizeCanonicalOrder(iss.Suggestions[0].Replacement)
                        : string.Empty;
                    return (iss.OriginalText.Contains(tc.ExpectedWrongSubspan) || origNorm.Contains(expWrongNorm)) &&
                           topNorm.Contains(expFixNorm);
                }), $"Failed on '{tc.Sentence}': expected '{tc.ExpectedWrongSubspan}' -> '{tc.ExpectedTopReplacementSubspan}'.");
            }

            // Tricky valid Burmese sentences that must produce ZERO false positives
            string[] validSentences =
            {
                "ကားသစ်ဝယ်သည်။",
                "အလုပ်သွားမယ်။",
                "ရေသောက်ပါ။",
                "ထမင်းစားပြီးပြီ။",
                "လေးယောက်လာသည်။",
                "မနက်ဖြန်ပဲ သွားမယ်။",
                "မနေ့ကပဲ ရောက်ပါတယ်။",
                "သူမသိတာပဲ ဖြစ်မယ်။",
                "ဒါပေမဲ့ ကျွန်တော် မအားသေးလို့ မလာနိုင်ဘူး။",
                "မြန်မာနိုင်ငံတော်ဗဟိုဘဏ်က နိုင်ငံခြားငွေလဲလှယ်နှုန်းကို နေ့စဉ် ထုတ်ပြန်ပေးလျက်ရှိသည်။",
                "အစားအသောက် သန့်ရှင်းလတ်ဆတ်မှုကို အထူးဂရုပြု စစ်ဆေးဆောင်ရွက်လျက်ရှိသည်။",
                "အားလုံးပဲ ကျေးဇူးတင်ပါတယ်။",
                "ဩဂုတ်လတွင် ဈေးနှုန်းများ ပြန်လည်ကျဆင်းနိုင်သည်။",
                "မိဘများကို လေးစားစွာဖြင့် ကူညီစောင့်ရှောက်ရမည်။",
                "အခွင့်အရေးနှင့် တာဝန်ကို မျှတစွာ သိရှိရန် လိုအပ်ပါသည်။",
                "လူငယ်များ အောင်မြင်မှု ရရှိစေရန် ပညာသင်ကြားရေးသည် အဓိကကျပါသည်။",
                "ဘာလုပ်နေတာလဲ ဆိုတာ ပြောပြပါဦး။",
                "ကျွန်တော်လည်း အဲဒီအစည်းအဝေးကို တက်ရောက်ပါမယ်။"
            };

            foreach (string valid in validSentences)
            {
                var fp = engine.CheckText(valid);
                Assert.True(fp.Count == 0, $"Unexpected false positive in '{valid}': {string.Join(", ", fp.Select(x => x.OriginalText))}");
            }
        }
    }
}
