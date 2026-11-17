using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class OfficialToolkitAndUxTests
    {
        [Fact]
        public void V24_FlagshipToolkit_WinInnwaConverter_NumberToWords_MyanmarCalendar_OfficialTemplates_QualityReport_And5DomainLexicons()
        {
            // 1. Legacy WinInnwa / WinResearcher / Zawgyi -> Pyidaungsu Converter (with English & URL protection)
            Assert.Equal("နိုင်ငံတော်", LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode("EdkifiHawmf"));
            Assert.Equal("မြန်မာနိုင်ငံ", LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode("jrefrmEdkifiH"));
            Assert.Equal("ပြည်ထောင်စုလွှတ်တော်", LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode("jynfaxmifpkvTwfawmf"));
            Assert.Equal("ဦးအောင်ဆန်း", LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode("OD;atmifqef;"));

            string winWithUrl = LegacyMyanmarFontConverter.ConvertWinInnwaToUnicode("EdkifiHawmf https://www.moi.gov.mm info@moi.gov.mm");
            Assert.Equal("နိုင်ငံတော် https://www.moi.gov.mm info@moi.gov.mm", winWithUrl);

            string untouchedEnglish = LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu("Ministry of Information (Room 204)", "Calibri", out bool enConverted);
            Assert.False(enConverted);
            Assert.Equal("Ministry of Information (Room 204)", untouchedEnglish);

            string convertedSeg = LegacyMyanmarFontConverter.ConvertSegmentToPyidaungsu("jynfaxmifpkvTwfawmf", "WinInnwa", out bool mmConverted);
            Assert.True(mmConverted);
            Assert.Equal("ပြည်ထောင်စုလွှတ်တော်", convertedSeg);

            // 2. Context-Smart Numeral Formatter (0-9 <-> ၀-၉) & Official Punctuation / ZWSP Cleaner
            string smartInput = "2026 ခုနှစ် စာမျက်နှာ 45 တွင် COVID-19၊ ISO-9001၊ A4၊ 5G နှင့် https://www.moi.gov.mm:8080/news/2026 admin99@gov.mm တို့ ပါဝင်သည်။";
            string smartOut = MyanmarTypographyFormatter.ConvertNumeralsSmart(smartInput, toMyanmarDigits: true, out int digitsChanged);
            Assert.True(digitsChanged >= 6);
            Assert.Contains("၂၀၂၆ ခုနှစ် စာမျက်နှာ ၄၅", smartOut);
            Assert.Contains("COVID-19", smartOut);
            Assert.Contains("ISO-9001", smartOut);
            Assert.Contains("A4", smartOut);
            Assert.Contains("5G", smartOut);
            Assert.Contains("https://www.moi.gov.mm:8080/news/2026", smartOut);
            Assert.Contains("admin99@gov.mm", smartOut);

            string dirtyPunct = "ပြည်\u200Bထောင်\u200Bစု ၊သမ္မတမြန်မာနိုင်ငံတော် ။နေပြည်တော်။";
            string cleanPunct = MyanmarTypographyFormatter.FormatPunctuationAndCleanInvisible(dirtyPunct, out int punctFixed, out int zwFixed);
            Assert.True(punctFixed > 0);
            Assert.Equal(2, zwFixed);
            Assert.Equal("ပြည်ထောင်စု၊ သမ္မတမြန်မာနိုင်ငံတော်။ နေပြည်တော်။", cleanPunct);

            // 3. Banking Kyat Lakh (သိန်း) & General Cardinal (သန်း) Number-to-Words with Creaky-Tone Sandhi
            var bank15Lakh = MyanmarNumberToWordsConverter.Convert("1,500,000", NumberConversionMode.BankingKyatLakh);
            Assert.True(bank15Lakh.Success);
            Assert.Equal("ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ", bank15Lakh.BurmeseWords);
            Assert.Equal("ကျပ် ၁,၅၀၀,၀၀၀/- (ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ)", bank15Lakh.VoucherInlineText);

            var bank100Lakh = MyanmarNumberToWordsConverter.Convert("10,000,000", NumberConversionMode.BankingKyatLakh);
            Assert.True(bank100Lakh.Success);
            Assert.Equal("ကျပ်သိန်းတစ်ရာတိတိ", bank100Lakh.BurmeseWords);

            var cardMillion = MyanmarNumberToWordsConverter.Convert("1,500,000", NumberConversionMode.GeneralCardinal);
            Assert.True(cardMillion.Success);
            Assert.Equal("တစ်သန်းငါးသိန်း", cardMillion.BurmeseWords);

            var cardSandhi = MyanmarNumberToWordsConverter.Convert("1,250", NumberConversionMode.GeneralCardinal);
            Assert.True(cardSandhi.Success);
            Assert.Equal("တစ်ထောင့်နှစ်ရာ့ငါးဆယ်", cardSandhi.BurmeseWords);

            Assert.True(MyanmarNumberToWordsConverter.TryExtractNumericSelection("Ks 1,500,000/-", out string extractedNum));
            Assert.False(MyanmarNumberToWordsConverter.TryExtractNumericSelection("မြန်မာစာလုံးပေါင်း", out _));

            // 4. 100% Offline Myanmar Traditional Lunisolar Calendar (မြန်မာသက္ကရာဇ် / သာသနာတော်နှစ်)
            var newYear1388 = MyanmarTraditionalCalendar.FromGregorian(new DateTime(2026, 4, 17));
            Assert.Equal(1388, newYear1388.MyanmarYear);
            Assert.Equal(2570, newYear1388.SasanaYear);
            Assert.Contains("၁၃၈၈ ခုနှစ်၊", newYear1388.OfficialMyanmarDateText);

            var nov2026 = MyanmarTraditionalCalendar.FromGregorian(new DateTime(2026, 11, 11));
            Assert.Equal(1388, nov2026.MyanmarYear);
            Assert.Contains("၂၀၂၆ ခုနှစ်၊ နိုဝင်ဘာလ ၁၁ ရက်", nov2026.DualOfficialHeaderText);
            Assert.Contains("၁၃၈၈ ခုနှစ်၊", nov2026.OfficialMyanmarDateText);

            // 5. Official Ministry Templates & 7-Point ရုံးစာ Structure Validator + Quality Scorecard
            string letterTpl = OfficialLetterSuite.BuildTemplateText(OfficialTemplateKind.MinistryOfficialLetter, new DateTime(2026, 11, 11));
            var letterAudit = OfficialLetterSuite.ValidateOfficialLetterStructure(letterTpl);
            Assert.True(letterAudit.LooksLikeOfficialDocument);
            Assert.Equal(letterAudit.TotalRequiredCount, letterAudit.PassedCount);

            // 6. Load Full V11 Master Lexicon & Verify 5 Specialized Domains + Colloquial Slang Rules
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // Clean 5-domain & prose-trap sentences must produce ZERO false positives
            string[] cleanDomainSentences =
            {
                "ပြည်ထောင်စုတရားလွှတ်တော်ချုပ်က ရှေ့တော်သွင်းစာချွန်တော်နှင့် အာဏာပေးစာချွန်တော် လျှောက်ထားချက်ကို စစ်ဆေးပြီး တရားမဒီကရီ ချမှတ်ခဲ့သည်။",
                "ဗဟိုဘဏ်က ငွေကြေးဖောင်းပွမှုနှုန်း ထိန်းချုပ်ရေးနှင့် နိုင်ငံခြားသုံးငွေ လဲလှယ်နှုန်း တည်ငြိမ်ရေးအတွက် အတိုးနှုန်းမူဝါဒ ထုတ်ပြန်သည်။",
                "ကျန်းမာရေးဝန်ကြီးဌာနက ပဋိဇီဝဆေး ယဉ်ပါးမှု ကာကွယ်ရေးနှင့် ခွဲစိတ်ကုသမှု စံနှုန်းများကို ထုတ်ပြန်ကြေညာသည်။",
                "ဆိုက်ဘာလုံခြုံရေးနှင့် ဉာဏ်ရည်တု နည်းပညာဆိုင်ရာ အချက်အလက်လုံခြုံရေး စနစ်ကို အဆင့်မြှင့်တင်သည်။",
                "နိုင်ငံတော်သံဃမဟာနာယကအဖွဲ့က ပဋိစ္စသမုပ္ပာဒ်တရားတော်၊ အဘိဓမ္မာပိဋကတ်နှင့် ဝိပဿနာ ကမ္မဋ္ဌာန်း သင်တန်းများ ဖွင့်လှစ်သည်။",
                "ဒေါ်ရွှေပုနှင့် မစိန်ပုတို့သည် အရပ်မပုပါ၊ ပီပီပြင်ပြင် ပြောဆိုတတ်ပြီး မီးကျီးခဲအနီး မကပ်ကြပါ။"
            };

            foreach (string clean in cleanDomainSentences)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on clean V11 sentence '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Quality Analyzer on clean official prose => 100% A+
            var cleanReport = DocumentQualityAnalyzer.Analyze(cleanDomainSentences[0], null, engine);
            Assert.Equal(100, cleanReport.OverallScore);
            Assert.Equal("A+", cleanReport.GradeCode);
            string certText = DocumentQualityAnalyzer.BuildAuditReportDocumentText("MinistryMemo.docx", cleanReport, new DateTime(2026, 11, 11));
            Assert.Contains("၁၀၀%", certText);

            // Domain orthography/context errors + colloquial SMS slang errors
            var v24Errors = new (string Input, string Wrong, string Fix)[]
            {
                ("တရားရုံးက သမ္မာန်စာ ဆင့်ခေါ်သည်။", "သမ္မာန်စာ", "သမ္မန်စာ"),
                ("တရားရုံးက တရားမဒီဂရီ ချမှတ်သည်။", "ဒီဂရီ", "ဒီကရီ"),
                ("တက္ကသိုလ်မှ မဟာသိပ္ပံ ဘွဲ့ဒီကရီ ပေးအပ်သည်။", "ဒီကရီ", "ဒီဂရီ"),
                ("ပဋိစ္စသမုပ္ပါဒ် တရားတော်ကို နာယူသည်။", "ပဋိစ္စသမုပ္ပါဒ်", "ပဋိစ္စသမုပ္ပာဒ်"),
                ("ဝိပသနာ တရားအားထုတ်သည်။", "ဝိပသနာ", "ဝိပဿနာ"),
                ("ငွေကြေးဖောင်းပွမူ နှုန်းကို လျှော့ချသည်။", "ငွေကြေးဖောင်းပွမူ", "ငွေကြေးဖောင်းပွမှု"),
                ("ဆိုက်ဘာလုံခြုံရေ စနစ် တည်ဆောက်သည်။", "ဆိုက်ဘာလုံခြုံရေ", "ဆိုက်ဘာလုံခြုံရေး"),
                ("ညာဏ်ရည်တု နည်းပညာ ဖွံ့ဖြိုးတိုးတက်လာသည်။", "ညာဏ်ရည်တု", "ဉာဏ်ရည်တု"),
                ("လူနာအား ခွဲစိတ်ကုသမူ ပြုလုပ်ပေးသည်။", "ခွဲစိတ်ကုသမူ", "ခွဲစိတ်ကုသမှု"),
                ("မဟုတ်ပုလေ", "ပု", "ဘူး"),
                ("ဘယ်လိုကျီးလဲ", "ကျီး", "ကြီး"),
                ("စားပီးပီ", "ပီးပီ", "ပြီးပြီ")
            };

            foreach (var err in v24Errors)
            {
                var iss = engine.CheckText(err.Input);
                Assert.True(iss.Any(i =>
                    (i.OriginalText.Contains(err.Wrong) || err.Wrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Any(s => s.Replacement.Contains(err.Fix) || err.Fix.Contains(s.Replacement))),
                    $"Failed V24 check on '{err.Input}': expected '{err.Wrong}' -> '{err.Fix}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Bilingual English -> Burmese 5-Domain Glossary Search
            Assert.Contains(engine.SearchDictionary("Habeas Corpus", 10), e => e.Word == "ရှေ့တော်သွင်းစာချွန်တော်" && e.IsSpecializedDomain);
            Assert.Contains(engine.SearchDictionary("Inflation", 10), e => e.Word == "ငွေကြေးဖောင်းပွမှု" && e.IsSpecializedDomain);
            Assert.Contains(engine.SearchDictionary("Cybersecurity", 10), e => e.Word == "ဆိုက်ဘာလုံခြုံရေး" && e.IsSpecializedDomain);
            Assert.Contains(engine.SearchDictionary("Antibiotic", 10), e => e.Word == "ပဋိဇီဝဆေး" && e.IsSpecializedDomain);
            Assert.Contains(engine.SearchDictionary("Abhidhamma", 10), e => e.Word == "အဘိဓမ္မာ" && e.IsSpecializedDomain);
        }

        [Fact]
        public void V25_CursorQuickFixMultiChoices_ColloquialChatTypos_AndCompositionalMultiSyllableSuggestions()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var suggestionEngine = new SuggestionEngine(trie);

            // 1. Exact 3 lines from user screenshot -> each MUST return >= 2 selectable choices (not description-only!)
            var issues1 = engine.CheckText("မဟုတ်ပုလေ");
            var puIssue = Assert.Single(issues1);
            Assert.Equal("ပု", puIssue.OriginalText);
            Assert.True(puIssue.Suggestions.Count >= 2, $"Expected >= 2 choices for 'ပု', got {puIssue.Suggestions.Count}");
            Assert.Equal("ဘူး", puIssue.Suggestions[0].Replacement);
            Assert.Contains(puIssue.Suggestions, s => s.Replacement == "ပါ");

            var issues2 = engine.CheckText("ဘယ်လိုကျီးလဲ");
            var kyeeIssue = Assert.Single(issues2);
            Assert.Equal("ကျီး", kyeeIssue.OriginalText);
            Assert.True(kyeeIssue.Suggestions.Count >= 2, $"Expected >= 2 choices for 'ကျီး', got {kyeeIssue.Suggestions.Count}");
            Assert.Equal("ကြီး", kyeeIssue.Suggestions[0].Replacement);

            var issues3 = engine.CheckText("စားပီးပီ");
            var peePeeIssue = Assert.Single(issues3);
            Assert.Equal("ပီးပီ", peePeeIssue.OriginalText);
            Assert.True(peePeeIssue.Suggestions.Count >= 3, $"Expected >= 3 choices for 'ပီးပီ', got {peePeeIssue.Suggestions.Count}");
            Assert.Equal("ပြီးပြီ", peePeeIssue.Suggestions[0].Replacement);
            Assert.Contains(peePeeIssue.Suggestions, s => s.Replacement == "ပြီးပါပြီ");
            Assert.Contains(peePeeIssue.Suggestions, s => s.Replacement == "ပြီးဖြစ်သည်");

            // 2. Colloquial Burmese phonetic chat sentences -> every issue MUST have concrete clickable choices
            var chatCases = new (string Input, string Wrong, string TopFix)[]
            {
                ("သွားပီးပီ", "ပီးပီ", "ပြီးပြီ"),
                ("လုပ်ပီးသား", "ပီး", "ပြီး"),
                ("စားပီးသွားပြီ", "ပီး", "ပြီး"),
                ("ဘာဖစ်လို့လဲ", "ဖစ်", "ဖြစ်"),
                ("ဖစ်နေပီ", "ဖစ်", "ဖြစ်"),
                ("ဖစ်နေပီ", "ပီ", "ပြီ"),
                ("မဖစ်ပု", "ဖစ်", "ဖြစ်"),
                ("မဖစ်ပု", "ပု", "ဘူး"),
                ("နိုင်ငံတော်သမ္မဒ", "နိုင်ငံတော်သမ္မဒ", "နိုင်ငံတော်သမ္မတ")
            };

            foreach (var tc in chatCases)
            {
                var iss = engine.CheckText(tc.Input);
                Assert.True(iss.All(i => i.Suggestions.Count >= 1),
                    $"Issue with zero suggestions found in '{tc.Input}'!");
                Assert.True(iss.Any(i => i.OriginalText == tc.Wrong && i.Suggestions[0].Replacement == tc.TopFix),
                    $"Expected '{tc.Wrong}' -> '{tc.TopFix}' in '{tc.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{string.Join("/", x.Suggestions.Select(s => s.Replacement))}"))}]");
            }

            // 3. Direct Compositional Multi-Syllable OOV Suggestion Synthesis (even without sentence context!)
            var compCases = new (string OovWord, string ExpectedChoice)[]
            {
                ("ပီးပီ", "ပြီးပြီ"),
                ("ဖစ်နေပီ", "ဖြစ်နေပြီ"),
                ("သွားပီးပီ", "သွားပြီးပြီ"),
                ("လုပ်ပီးသား", "လုပ်ပြီးသား"),
                ("စာလုံးပေါင်သတ်ပုံ", "စာလုံးပေါင်းသတ်ပုံ")
            };

            foreach (var cc in compCases)
            {
                var cands = suggestionEngine.GetSuggestions(cc.OovWord, 5);
                Assert.True(cands.Count >= 1, $"Expected non-empty compositional suggestions for '{cc.OovWord}'");
                Assert.Contains(cands, c => c.Replacement == cc.ExpectedChoice);
            }

            // 4. Zero False Positives on valid names, adjectives & loanwords
            string[] cleanCounterparts =
            {
                "ဒေါ်ရွှေပုနှင့် မစိန်ပုတို့သည် အရပ်မပုပါ။",
                "ပီပီပြင်ပြင် ပြောဆိုပြီး မီးကျီးခဲအနီး မကပ်ပါနှင့်။",
                "အိန္ဒိယနိုင်ငံသုံး ရူပီးငွေ လဲလှယ်နှုန်း။"
            };

            foreach (string clean in cleanCounterparts)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected V25 false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V27_WikipediaParagraph_ShiftedKinzi_AndUserLoanwordCompoundPairs_AllWorkWithZeroFalsePositives()
        {
            string masterJsonPath = TestPaths.MasterJsonPath;
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Exact Wikipedia paragraph from Screenshot 1
            string wikiParagraph =
                "လူသား (အင်္ဂလိပ်: Humans သို့မဟုတ် Homo sapiens) သည် ကမ္ဘာမြေပေါ်တွင် " +
                "လက်ရှိကျန်ရှိနေသော တစ်ခုတည်းသော ဟိုမိုမျိုးစုဝင် ပရိုင်းမိတ် (Primate) နို့တိုက်သတ္တဝါ မျိုးစိတ် " +
                "ဖြစ်သည်။[၁] လူသားများသည် မတ်မတ်ရပ်၍ နှစ်ခြေဖြင့် လျှောက်နိုင်ခြင်း (Bipedalism)၊ " +
                "အခြားသတ္တဝါများနှင့် မတူဘဲ အလွန်ကြီးမား ရှုပ်ထွေးသော ဦးနှောက် တည်ဆောက်ပုံ ရှိခြင်းနှင့် " +
                "လက်ချောင်းများကို စွမ်းရည်မြင့်မားစွာ အသုံးပြုနိုင်ခြင်း စသည့် ထူးခြားသော ခန္ဓာဗေဒဆိုင်ရာ " +
                "ဝိသေသလက္ခဏာများ ရှိကြသည်။[၂] ဤအဆင့်မြင့် ဇီဝကမ္မဖြစ်စဉ်များကြောင့် လူသားများသည် " +
                "ဆန်းပြားသော တူးရိယာ ကိရိယာများကို တီထွင်နိုင်ခြင်း၊ ကိုယ်ပိုင် ဘာသာစကားနှင့် သကေင်္တများဖြင့် " +
                "ဆက်သွယ်နိုင်ခြင်း၊ ရှုပ်ထွေးသော လူမှုအဖွဲ့အစည်းများကို တည်ဆောက်နိုင်ခြင်းနှင့် စိတ္တဇနာမ် " +
                "ဆင်ခြင်တုံတရား (Abstract reasoning) များကို ဖော်ထုတ်နိုင်စွမ်း ရှိကြသည်။[၃]";

            var wikiIssues = engine.CheckText(wikiParagraph);
            Assert.Equal(2, wikiIssues.Count);

            var turiyaIssue = Assert.Single(wikiIssues, i => i.OriginalText == "တူးရိယာ");
            Assert.Equal("တူရိယာ", turiyaIssue.Suggestions[0].Replacement);
            Assert.DoesNotContain(turiyaIssue.Suggestions, s => s.Replacement == "တုရိယာ");
            Assert.DoesNotContain(turiyaIssue.Suggestions, s => s.Replacement == "တူရိယ");

            var sanketaIssue = Assert.Single(wikiIssues, i => i.OriginalText == "သကေင်္တ");
            Assert.Equal("သင်္ကေတ", sanketaIssue.Suggestions[0].Replacement);

            // 2. Screenshot 2 deliberate misspellings & shifted-Kinzi / retroflex-stack pairs
            var errorPairs = new (string Input, string ExpectedWrong, string ExpectedTop1)[]
            {
                ("ပတ်စီ", "ပတ်စီ", "ပက်စီ"),
                ("ကိတ်မုဒ်", "ကိတ်မုဒ်", "ကိတ်မုန့်"),
                ("ပေါင်မုဒ်", "ပေါင်မုဒ်", "ပေါင်မုန့်"),
                ("ဘေကျင်း", "ဘေကျင်း", "ပေကျင်း"),
                ("ချော့ကလက်", "ချော့ကလက်", "ချောကလက်"),
                ("ဥက္ကဌ္ဌ", "ဥက္ကဌ္ဌ", "ဥက္ကဋ္ဌ"),
                ("ဥက္ကဋ္ဋ", "ဥက္ကဋ္ဋ", "ဥက္ကဋ္ဌ"),
                ("ပြဌ္ဌာန်း", "ပြဌ္ဌာန်း", "ပြဋ္ဌာန်း"),
                ("ပြဋ္ဋာန်း", "ပြဋ္ဋာန်း", "ပြဋ္ဌာန်း"),
                ("ဖတ်ဒရယ်", "ဖတ်ဒရယ်", "ဖက်ဒရယ်"),
                ("မဂင်္လာ", "မဂင်္လာ", "မင်္ဂလာ"),
                ("အဂင်္လိပ်", "အဂင်္လိပ်", "အင်္ဂလိပ်"),
                ("သကင်္န်း", "သကင်္န်း", "သင်္ကန်း"),
                ("သဘေင်္ာ", "သဘေင်္ာ", "သင်္ဘော")
            };

            foreach (var ep in errorPairs)
            {
                var iss = engine.CheckText(ep.Input);
                Assert.True(iss.Any(i =>
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.OriginalText) == UnicodeNormalizer.NormalizeCanonicalOrder(ep.ExpectedWrong) &&
                    i.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(i.Suggestions[0].Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(ep.ExpectedTop1)),
                    $"Expected '{ep.ExpectedWrong}' -> #1 '{ep.ExpectedTop1}' on '{ep.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{string.Join("/", x.Suggestions.Select(s => s.Replacement))}"))}]");
            }

            // Confirm 'ပတ်စီ' offers both 'ပက်စီ' and 'ပက်ပ်စီ' as clickable choices
            var pepsiIssue = engine.CheckText("ပတ်စီ").Single();
            Assert.Contains(pepsiIssue.Suggestions, s => s.Replacement == "ပက်စီ");
            Assert.Contains(pepsiIssue.Suggestions, s => s.Replacement == "ပက်ပ်စီ");

            // 3. Screenshot 2 clean words -> ZERO false positives!
            string[] cleanScreenshot2Words =
            {
                "ပက်စီ",
                "ပက်ပ်စီ",
                "ကိုကာကိုလာ",
                "ချောကလက်",
                "ကိတ်မုန့်",
                "နိုင်ငံတော်",
                "သမ္မတ",
                "ဥက္ကဋ္ဌ",
                "ပြဋ္ဌာန်း",
                "ဆိုရှယ်လစ်",
                "ဒီမိုကရေစီ",
                "ရုရှား",
                "ဖက်ဒရယ်",
                "ပေကျင်း",
                "ဝေါမြို့"
            };

            foreach (string cw in cleanScreenshot2Words)
            {
                var fp = engine.CheckText(cw);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on clean Screenshot 2 word '{cw}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V28_SculptorStoryScreenshot_SayaKaungVsSaraKaung_AndBoundParticleFragments_ZeroFalsePositives()
        {
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            const string sculptorStory =
                "ညီညီ့ရဲ့ ခြင်္သေ့ရုပ်လေးကို စစ်ဆေးတဲ့အခါမှာတော့ ဆရာကြီးရဲ့ မျက်နှာမှာ ကျေနပ်တဲ့အပြုံးတွေ ပေါ်လာတယ်။ " +
                "ခြင်္သေ့ရုပ်လေးက သေးငယ်ပေမယ့် အသက်ဝင်နေပြီး လက်ရာမြောက်လွန်းတဲ့အတွက် အပြစ်ဆိုစရာ မရှိအောင် သေသပ်လှပနေတယ်။\n" +
                "ထိုအခါ ဆရာကြီးက တပည့်နှစ်ယောက်ကို ခေါ် ပြီး \"အလုပ်တစ်ခုကို လုပ်တဲ့အခါ ဘယ်လောက်မြန်မြန် ပြီးသွားတယ်ဆိုတာထက်၊ " +
                "ဘယ်လောက် စေတနာပါပါနဲ့ သေသပ်စွာ လုပ်ဆောင်ခဲ့တယ်ဆိုတာက ပိုအရေးကြီးတယ်။ ပြီးစလွယ်လုပ်တဲ့အလုပ်ဟာ ခဏတာ " +
                "မျက်စိပသာဒဖြစ်ပေမယ့် ရေရှည်မှာ တန်ဖိုးမရှိတတ်ဘူး။ အချိန်ပေးပြီး စိတ်ရှည်ရှည်နဲ့ ကြိုးစားထားတဲ့ အလုပ်ကသာ အမြဲတမ်း " +
                "တန်ဖိုးရှိပြီး ရေရှည်တည်တံ့တယ်\" လို့ ဆုံးမစကား ပြောကြားခဲ့တယ်။\n" +
                "အဲဒီနေ့ကစပြီး အောင်အောင်ဟာ အလုပ်ကို ပြီးစလွယ်လုပ်တတ်တဲ့ အကျင့်ကို ဖျောက်ဖျက်ခဲ့ပြီး၊ ညီညီကဲ့သို့ စိတ်ရှည်သည်းခံကာ " +
                "အကောင်းဆုံးဖြစ်အောင် ကြိုးစားတတ်တဲ့ ပန်းပုဆရာကောင်းတစ်ယောက် ဖြစ်လာပါတော့တယ်။";

            // 1. GeneralStandard ('အထွေထွေ စံ') mode: colloquial narrative story must have ZERO spelling/grammar false positives on 'ဆရာကောင်း'!
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var generalIssues = generalEngine.CheckText(sculptorStory);
            Assert.DoesNotContain(generalIssues, i => i.OriginalText.Contains("ဆရာကောင်း"));

            // 2. FormalOfficial ('စာပေ / ရုံးသုံး') mode: ZERO SpellingError issues across the entire story (only colloquial->literary style suggestions),
            //    'ပြီးသွားတယ်ဆိုတာထက်' consistently flags 'တယ်' -> 'သည်' (no longer shadowed by polluted 'တယ်ဆို'!),
            //    and 'သေးငယ်ပေမယ့်' cleanly suggests 'သော်လည်း' (never corrupting 'မယ့်' -> 'မည့်' into '*သေးငယ်ပေမည့်'!)
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var formalIssues = formalEngine.CheckText(sculptorStory);
            var formalSpellingErrors = formalIssues.Where(i => i.IssueType == ProofingIssueType.SpellingError).ToList();
            Assert.True(formalSpellingErrors.Count == 0,
                $"Expected 0 SpellingErrors on sculptor story in FormalOfficial mode, got: [{string.Join("; ", formalSpellingErrors.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

            Assert.DoesNotContain(formalIssues, i =>
                i.OriginalText == "မယ့်" &&
                i.Suggestions.Any(s => s.Replacement == "မည့်"));
            Assert.Contains(formalIssues, i =>
                i.OriginalText == "ပေမယ့်" &&
                i.IssueType == ProofingIssueType.OfficialStyleGrammarError &&
                i.Suggestions.Any(s => s.Replacement == "သော်လည်း"));

            var talSoIssues = formalEngine.CheckText("ဘယ်လောက်မြန်မြန် ပြီးသွားတယ်ဆိုတာထက်");
            Assert.Contains(talSoIssues, i =>
                i.OriginalText == "တယ်" &&
                i.IssueType == ProofingIssueType.OfficialStyleGrammarError &&
                i.Suggestions.Any(s => s.Replacement == "သည်"));

            // 3. Multi-syllable '[Content Noun] + [PostNominalAdjective]' & post-verbal gerundive '-စရာ' -> ZERO SpellingError false positives!
            string[] cleanSentences =
            {
                "ဆရာကောင်းတစ်ဦး၊ တပည့်ကောင်းနှစ်ဦး၊ မိတ်ဆွေကောင်းများနှင့် ခေါင်းဆောင်ကောင်းများ ပေါ်ထွန်းပါစေ။",
                "ပန်းပုဆရာကောင်းတစ်ယောက်နှင့် ပန်းချီဆရာကြီးတစ်ဦး လာရောက်လေ့လာသည်။",
                "အပြစ်ဆိုစရာ မရှိအောင် သေသပ်လှပနေသည်။",
                "စားစရာကောင်းသည်။ ကြည့်စရာကောင်းသည်။ ပြောစရာမလိုပါ။ ကြောက်စရာကြီး ဖြစ်သည်။ ချစ်စရာလေး ဖြစ်သည်။"
            };

            foreach (string s in cleanSentences)
            {
                var issues = formalEngine.CheckText(s).Where(i => i.IssueType == ProofingIssueType.SpellingError).ToList();
                Assert.True(issues.Count == 0,
                    $"Unexpected SpellingError on '{s}': [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 4. Genuine '*စရာ -> *ဆရာ' and 'ဆရာကတော -> ဆရာကတော်' misspellings -> accurately caught and repaired!
            var sayaTypos = new (string Input, string ExpectedRepaired)[]
            {
                ("ကျောင်းစရာ", "ကျောင်းဆရာ"),
                ("ကျောင်းစရာမ", "ကျောင်းဆရာမ"),
                ("ကျောင်းစရာကြီး", "ကျောင်းဆရာကြီး"),
                ("ပန်းပုစရာ", "ပန်းပုဆရာ"),
                ("ပန်းချီစရာ", "ပန်းချီဆရာ"),
                ("ဆေးစရာ", "ဆေးဆရာ"),
                ("စရာဝန်", "ဆရာဝန်"),
                ("စရာတော်", "ဆရာတော်"),
                ("စရာကြီးက ပြောသည်။", "ဆရာကြီးက ပြောသည်။"),
                ("ဆရာကတော", "ဆရာကတော်")
            };

            foreach (var tp in sayaTypos)
            {
                var iss = formalEngine.CheckText(tp.Input);
                Assert.True(iss.Count > 0 && iss[0].Suggestions.Count > 0,
                    $"Expected spelling issue on '{tp.Input}', got 0 issues.");
                string repaired = tp.Input.Substring(0, iss[0].StartIndex)
                    + iss[0].Suggestions[0].Replacement
                    + tp.Input.Substring(iss[0].StartIndex + iss[0].Length);
                Assert.Equal(tp.ExpectedRepaired, repaired);
            }
        }

        [Fact]
        public void V29_WorldCountries245_EncyclopedicDetails_Currencies_IntlOrgs_Units_AndBilingualSearch()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Clean multi-continent diplomatic, currency, organization, unit & heritage sentences -> ZERO false positives!
            string[] cleanGlobalSentences =
            {
                "အီဂျစ်နိုင်ငံ ကိုင်ရိုမြို့၊ ကင်ညာနိုင်ငံ နိုင်ရိုဘီမြို့၊ ဘာကီးနားဖာဆိုနိုင်ငံ ဝါဂါဒူးဂူးမြို့နှင့် မာဒါဂတ်စကားနိုင်ငံ အန်တာနာနာရီဗိုမြို့။",
                "အာဂျင်တီးနားနိုင်ငံ ဗျူနိုအေးရိစ်မြို့၊ ဘရာဇီးနိုင်ငံ ဘရာစီးလီးယားမြို့၊ ကျူးဘားနိုင်ငံ ဟာဗားနားမြို့နှင့် ဖီဂျီနိုင်ငံ ဆူဗာမြို့။",
                "မွန်ဂိုးလီးယားနိုင်ငံ အူလန်ဘာတာမြို့၊ ကာဇက်စတန်နိုင်ငံ အက်စတာနာမြို့၊ အိုက်စလန်နိုင်ငံ ရေဂျာဗစ်မြို့နှင့် မော်ရိုကိုနိုင်ငံ ရာဘတ်မြို့။",
                "ကုလသမဂ္ဂလုံခြုံရေးကောင်စီ၊ ကမ္ဘာ့ကျန်းမာရေးအဖွဲ့၊ ယူနက်စကို၊ အပြည်ပြည်ဆိုင်ရာတရားရုံးနှင့် ဘရစ်ခ်စ်အဖွဲ့တို့ ဆွေးနွေးကြသည်။",
                "အမေရိကန်ဒေါ်လာ၊ ယူရိုငွေ၊ ဆွစ်ဖရန့်၊ ဂျပန်ယန်း၊ ထိုင်းဘတ်နှင့် မြန်မာကျပ်တို့၏ ငွေလဲလှယ်နှုန်း။",
                "အခေါက်ရွှေ တစ်ကျပ်သား၊ ဆယ့်ခြောက်ပဲရည်၊ တစ်ပိဿာ၊ ယူဇနာ၊ ပန်းဆယ်မျိုးနှင့် ပုဂံရှေးဟောင်းယဉ်ကျေးမှုနယ်မြေ။"
            };

            foreach (string sent in cleanGlobalSentences)
            {
                var fp = engine.CheckText(sent);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on clean global/detail sentence '{sent}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement} ({x.IssueType})"))}]");
            }

            // 2. Rich Encyclopedic Country Metadata Getters on DictionaryEntry
            var switz = engine.SearchDictionary("ဆွစ်ဇာလန်", 5).FirstOrDefault(e => e.Word == "ဆွစ်ဇာလန်" && e.IsProperNoun);
            Assert.NotNull(switz);
            Assert.Contains("ဘန်းမြို့", switz!.CapitalCity);
            Assert.Contains("CHF", switz.CurrencyInfo);
            Assert.Contains("Swiss Confederation", switz.OfficialFullName);
            Assert.Contains("+41", switz.IsoAndCallingCode);
            Assert.Contains("မြို့တော်:", switz.CompactAbidanSummary);
            Assert.Contains("ငွေကြေး:", switz.CompactAbidanSummary);

            // 3. Bilingual English, Capital, ISO-3, Currency ISO-4217 & International Calling Code (+81, +95) Lookup
            Assert.Contains(engine.SearchDictionary("Argentina", 10), e => e.Word == "အာဂျင်တီးနား" && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("Burkina Faso", 10), e => e.Word == "ဘာကီးနားဖာဆို" && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("Madagascar", 10), e => e.Word == "မာဒါဂတ်စကား" && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("Fiji", 10), e => e.Word == "ဖီဂျီ" && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("Ulaanbaatar", 10), e => e.Word.StartsWith("အူလန်ဘာတာ", StringComparison.Ordinal) && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("+81", 10), e => e.Word == "ဂျပန်" && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("+95", 10), e => e.Word == "မြန်မာ" && e.IsProperNoun);
            Assert.Contains(engine.SearchDictionary("CHF", 10), e => e.Word == "ဆွစ်ဖရန့်" || e.Word == "ဆွစ်ဇာလန်");
            Assert.Contains(engine.SearchDictionary("UNESCO", 10), e => e.Word == "ယူနက်စကို");
            Assert.Contains(engine.SearchDictionary("BRICS", 10), e => e.Word.StartsWith("ဘရစ်ခ်စ်", StringComparison.Ordinal));
            Assert.Contains(engine.SearchDictionary("ပိဿာ", 10), e => e.Word == "ပိဿာ" && e.UsageNote.Contains("1.63293") == false && e.UsageNote.Contains("၁.၆၃၂၉၃"));

            // 4. MOFA Country & Capital Orthography Corrections + Authoritative MOFA Reasoning
            var mofaTypos = new (string Input, string Wrong, string Fix)[]
            {
                ("အာဂျင်တီးနာနိုင်ငံသို့ သွားရောက်သည်။", "အာဂျင်တီးနာ", "အာဂျင်တီးနား"),
                ("ဗင်နီဇွဲလာနိုင်ငံနှင့် ဆက်ဆံရေး။", "ဗင်နီဇွဲလာ", "ဗင်နီဇွဲလား"),
                ("အီသီယိုးပီးယာနိုင်ငံတွင် ကျင်းပသည်။", "အီသီယိုးပီးယာ", "အီသီယိုးပီးယား"),
                ("ဥဇဘက်ကစ္စတမ်နိုင်ငံသို့ စေလွှတ်သည်။", "ဥဇဘက်ကစ္စတမ်", "ဥဇဘက်ကစ္စတန်"),
                ("မလေးရှားနိုင်ငံ ကွာလာလန်ပူမြို့သို့ ရောက်ရှိသည်။", "ကွာလာလန်ပူမြို့", "ကွာလာလမ်ပူမြို့")
            };

            foreach (var mt in mofaTypos)
            {
                var iss = engine.CheckText(mt.Input);
                var matched = iss.FirstOrDefault(i =>
                    (i.OriginalText.Contains(mt.Wrong) || mt.Wrong.Contains(i.OriginalText)) &&
                    i.Suggestions.Any(s => s.Replacement.Contains(mt.Fix) || mt.Fix.Contains(s.Replacement)));
                Assert.True(matched != null,
                    $"Expected MOFA typo fix '{mt.Wrong}' -> '{mt.Fix}' on '{mt.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
                Assert.Contains("MOFA", matched!.GrammaticalReasoning);
                Assert.Contains("MOFA", matched.AuthoritativeSource);
            }
        }

        [Fact]
        public void V30_AllInOneFlagship_FastInvertedSearch_DeepGrammarClassifiers_OfficialRewriter_AndSynonyms()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var loadSw = Stopwatch.StartNew();
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            loadSw.Stop();
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Pillar 1: Inverted Search Index Speed & Bilingual Glossary Lookup (< 15ms average post-warmup)
            string[] speedQueries =
            {
                "Procurement", "Accountability", "Transparency", "Climate Change",
                "Curriculum", "Artificial Intelligence", "Switzerland", "+81", "CHF", "ဆောင်ရွက်"
            };
            foreach (string wq in speedQueries)
                _ = engine.SearchDictionary(wq, 15); // warm up lazy index & JIT across Latin, Calling-Code, and Burmese paths

            double bestAvgMs = double.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                var searchSw = Stopwatch.StartNew();
                foreach (string q in speedQueries)
                {
                    var hits = engine.SearchDictionary(q, 15);
                    Assert.True(hits.Count > 0, $"Expected bilingual/inverted index search hit for '{q}', got 0.");
                }
                searchSw.Stop();
                bestAvgMs = Math.Min(bestAvgMs, searchSw.Elapsed.TotalMilliseconds / speedQueries.Length);
            }
            Assert.True(bestAvgMs < 15.0, $"Expected < 15ms avg SearchDictionary latency with inverted index, got {bestAvgMs:0.00} ms.");

            // Bi-directional Synonym (ကြောင်းတူသံကွဲ) Index
            var synsAction = engine.GetSynonyms("ဆောင်ရွက်");
            Assert.NotEmpty(synsAction);
            Assert.Contains("လုပ်ဆောင်", synsAction);
            var synsReverse = engine.GetSynonyms("လုပ်ဆောင်");
            Assert.Contains("ဆောင်ရွက်", synsReverse);

            // 2. Pillar 2: Deep Myanmar Grammar, Classifiers, Case-Markers, Verb-Plural, Modals & Homophone Collocations
            var deepErrors = new (string Input, string ExpectedSuggestionSubstring)[]
            {
                ("နေအိမ် နှစ်စီး ဆောက်လုပ်ထားသည်။", "လုံး"),
                ("နှင်းဆီပန်း သုံးလုံး ဝယ်ယူသည်။", "ပွင့်"),
                ("စာရွက် ငါးထည် ထုတ်ပေးသည်။", "ရွက်"),
                ("လက်စွပ် တစ်ချပ် ဝတ်ဆင်ထားသည်။", "ကွင်း"),
                ("ထမင်းအား စားသည်။", "ကို"),
                ("ကိုယ်စားလှယ်များ သွားရောက်ကျသည်။", "ကြ"),
                ("လိုက်နာဆောင်ရွက်သည့်ပါသည်။", "သင့်"),
                ("ဝန်ကြီးဌာန နှင့် နှင့် ပူးပေါင်းဆောင်ရွက်သည်။", "နှင့်"),
                ("တံတားသစ် တီဆောက်ရေး လုပ်ငန်း။", "တည်ဆောက်"),
                ("နည်းပညာအသစ် တည်ထွင်နိုင်ခဲ့သည်။", "တီထွင်"),
                ("စင်ဆက်မပြတ် ဖွံ့ဖြိုးတိုးတက်ရေး။", "စဉ်ဆက်မပြတ်"),
                ("အစည်းအဝေးက ဆုံးဖျတ်ခဲ့သည်။", "ဆုံးဖြတ်")
            };

            foreach (var de in deepErrors)
            {
                var iss = engine.CheckText(de.Input);
                Assert.True(
                    iss.Any(i => i.Suggestions.Any(s => s.Replacement.Contains(de.ExpectedSuggestionSubstring))),
                    $"Expected suggestion containing '{de.ExpectedSuggestionSubstring}' on '{de.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Zero False Positives on Clean Sentences & Causative/Indirect Object Constructions
            string[] cleanSentences =
            {
                "နေအိမ် နှစ်လုံးနှင့် နှင်းဆီပန်း သုံးပွင့် ဝယ်ယူခဲ့ပါသည်။",
                "ကလေးအား ထမင်းကို စားစေသည်။",
                "ကိုယ်စားလှယ်များ တက်ရောက်ကြပါသည်။",
                "လိုက်နာဆောင်ရွက်သင့်ပါသည်။",
                "စဉ်ဆက်မပြတ် ဖွံ့ဖြိုးတိုးတက်ရေးအတွက် တည်ဆောက်ရေးလုပ်ငန်းများ ဆောင်ရွက်လျက်ရှိပါသည်။"
            };
            foreach (string clean in cleanSentences)
            {
                var fp = engine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on '{clean}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement} ({x.IssueType})"))}]");
            }

            // 3. Pillar 3: 1-Click Colloquial -> Official Ministry Prose Rewriter (with lexical root protection)
            string colloquialSample = "ဒီနေ့ အစည်းအဝေးမှာ ဟိုတယ်လုပ်ငန်းတွေနဲ့ ပတ်သက်ပြီး ဆွေးနွေးနေပါတယ်။ ဒါပေမယ့် ချို့တဲ့သူများအတွက် အခက်အခဲ မရှိပါဘူးလို့ တင်ပြပါတယ်။";
            OfficialRewriteResult rw = MyanmarOfficialRewriter.ConvertToOfficialProse(colloquialSample);
            Assert.True(rw.HasChanges && rw.ReplacementCount >= 6, $"Expected >= 6 colloquial->official rewrites, got {rw.ReplacementCount}: '{rw.RewrittenText}'");
            Assert.Contains("ယနေ့", rw.RewrittenText);
            Assert.Contains("ဟိုတယ်", rw.RewrittenText); // lexical root preserved!
            Assert.Contains("ချို့တဲ့", rw.RewrittenText); // lexical root preserved!
            Assert.Contains("လျက်ရှိပါသည်", rw.RewrittenText);
            Assert.Contains("သို့ရာတွင်", rw.RewrittenText);
            Assert.Contains("မရှိပါဟု", rw.RewrittenText);

            var remainingFormalIssues = engine.CheckText(rw.RewrittenText);
            Assert.True(remainingFormalIssues.Count == 0,
                $"Expected 0 remaining FormalOfficial issues after OfficialRewriter, got [{string.Join("; ", remainingFormalIssues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
        }

        [Fact]
        public void V31_MultiCorpusLearning_BooksEncyclopediaNewsGov_NewWords_PoisonElimination_AndGrammarRules()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Clean Multi-Corpus Prose Acceptance (Books, Abhidhamma, Chronicles, Encyclopedia, Law, News, Geography)
            string[] cleanCorpusSentences =
            {
                "ကထာဝတ္ထုကျမ်းတွင် သကဝါဒီနှင့် ပရဝါဒီတို့ အပြန်အလှန် ဆွေးနွေးကြသည်။",
                "သောတာပတ္တိမဂ္ဂဋ္ဌာန်နှင့် သောတာပတ္တိဖလဋ္ဌာန် ပုဂ္ဂိုလ်တို့သည် မြတ်သောအကျင့်ကို ကျင့်ကြံကြသည်။",
                "ရူပါယတန၊ ဇိဝှါနှင့် ဝေနေယျသတ္တဝါတို့၏ သဘာဝကို သိသလော။",
                "မဒ္ဒရာဇ်မင်းနှင့် အသောကမင်းကြီးတို့၏ သဒ္ဓါစသည်သည် ထင်ရှားလှပေသည်။",
                "လူ့ခန္ဓာကိုယ်ရှိ နက်ဗ်ဆဲလ်နှင့် ပင်မဆဲလ်များသည် အီဗော်လျူရှင်ဖြစ်စဉ်အရ ဂေဟစနစ်နှင့် ဆက်စပ်နေသည်။",
                "မြို့နယ်အခွန်ဦးစီးရုံးသည် ငွေကြေးစနစ်နှင့် ကုန်ထုတ်လုပ်ငန်းများ စနစ်တကျ ရှိစေရန် ကြီးကြပ်သည်။",
                "ထို့အတွက်ကြောင့် နိုင်ငံရေးစုဖွဲ့မှုနှင့် အုပ်ချုပ်ရေးယန္တရား ခိုင်မာစေရေး ဆောင်ရွက်လျက်ရှိပါသည်။",
                "အမေရိကန် ဆီးနိတ်လွှတ်တော်က ဒေါ်လာ ၅ ဘီလျံ တန်ဖိုးရှိ ရန်ပုံငွေကို အတည်ပြုခဲ့သည်။",
                "ဘူဂေးရီးယားနိုင်ငံ၊ အတ္တလန္တိတ်သမုဒ္ဒရာနှင့် အော့တမန်အင်ပိုင်ယာ သမိုင်းကြောင်းကို လေ့လာကြသည်။",
                "မီဇိုရမ်ပြည်နယ်၊ လွန်ပရာဘန်မြို့၊ ပျော်ဘွယ်မြို့နယ်နှင့် ရွှေကုက္ကိုဒေသတို့တွင် လမ်းပန်းဆက်သွယ်ရေး ကောင်းမွန်သည်။"
            };

            foreach (string sent in cleanCorpusSentences)
            {
                var fp = formalEngine.CheckText(sent);
                Assert.True(fp.Count == 0,
                    $"Unexpected false positive on clean multi-corpus sentence '{sent}': [{string.Join("; ", fp.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement} ({x.IssueType})"))}]");
            }

            // Concessive Verb + ပေမဲ့ in GeneralStandard must not false-slice to ဒါပေမဲ့
            var generalConcessive = generalEngine.CheckText("စည်းကမ်းချက်များ ချမှတ်ထားပေမဲ့ လိုက်နာရန် လိုအပ်ပါသည်။");
            Assert.True(generalConcessive.Count == 0,
                $"Unexpected GeneralStandard issue on concessive 'ထားပေမဲ့': [{string.Join("; ", generalConcessive.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");

            // 2. Empirical Orthography Rules, Purged Crowd-Lexicon Poison & Corpus-Derived Grammar Rules
            var expectedDetections = new (string Input, string ExpectedFixSubstring)[]
            {
                ("အိမ်ခန်း ဌားရမ်းခြင်း လုပ်ငန်း။", "ငှားရမ်း"),
                ("ဤစာအုပ်သည် အလွန်လိုချင်ဘွယ် ကောင်းပေသည်။", "ဖွယ်"),
                ("ဗဟိုဘဏ်က ငွေကြေးစံနစ် တည်ငြိမ်ရေး ဆောင်ရွက်သည်။", "ငွေကြေးစနစ်"),
                ("သောတာပတ္တိ မဂ္ဂဌာန် ပုဂ္ဂိုလ်။", "မဂ္ဂဋ္ဌာန်"),
                ("လမ်းဘေးတွင် ကုက္ကိုလ်ပင် စိုက်ပျိုးထားသည်။", "ကုက္ကိုပင်"),
                ("ကျေးလက်ဒေသ ဖွံဖြိုးရေး လုပ်ငန်း။", "ဖွံ့ဖြိုးရေး"),
                ("မတ်ထရစ်စနစ်ဖြင့် တိုင်းတာသည်။", "မက်ထရစ်"),
                ("အာဖဂနိစတန်နိုင်ငံတွင် ငလျင်လှုပ်ခတ်ခဲ့သည်။", "အာဖဂန်နစ္စတန်"),
                ("တစုံတရာ ထူးခြားမှု မရှိပါ။", "တစ်စုံတစ်ရာ"),
                ("ခေတ္တ တအောင့် စောင့်ဆိုင်းပေးပါ။", "တစ်အောင့်"),
                ("အစဉ်တစ်စိုက် ကြိုးပမ်းဆောင်ရွက်ခဲ့သည်။", "အစဉ်တစိုက်"),
                ("တစ်ချို့တစ်လေသာ တက်ရောက်ကြသည်။", "တချို့တလေ"),
                ("တစ်ဒင်္ဂ စိတ်ချမ်းသာမှု ရရှိသည်။", "တဒင်္ဂ"),
                ("ရန်ပုံငွေ ရရှိရေးအတွက်ကို ဆောင်ရွက်သည်။", "အတွက်"),
                ("အစည်းအဝေးကို အွန်လိုင်းနှင့် ကျင်းပသည်။", "ဖြင့်")
            };

            foreach (var ed in expectedDetections)
            {
                var iss = formalEngine.CheckText(ed.Input);
                Assert.True(
                    iss.Any(i => i.Suggestions.Any(s => s.Replacement.Contains(ed.ExpectedFixSubstring))),
                    $"Expected suggestion containing '{ed.ExpectedFixSubstring}' on '{ed.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 3. UX Suggestion Hygiene: No duplicate replacements & no fake compositional splits on explicit orthography rules
            var uxIssues = formalEngine.CheckText("တူးရိယာနှင့် ကိတ်မုဒ် ဝယ်ယူသည်။");
            var turiya = uxIssues.FirstOrDefault(i => i.OriginalText == "တူးရိယာ");
            Assert.NotNull(turiya);
            Assert.Equal(turiya!.Suggestions.Count, turiya.Suggestions.Select(s => s.Replacement).Distinct().Count());
            var cake = uxIssues.FirstOrDefault(i => i.OriginalText == "ကိတ်မုဒ်");
            Assert.NotNull(cake);
            Assert.Equal(
                UnicodeNormalizer.NormalizeCanonicalOrder("ကိတ်မုန့်"),
                UnicodeNormalizer.NormalizeCanonicalOrder(cake!.Suggestions[0].Replacement));
            Assert.DoesNotContain(cake.Suggestions, s => s.Replacement == "ကိတ်မှု");

            // 4. Expanded Colloquial -> Official Ministry Prose Rewriter (ပြီးတော့ -> ထို့ပြင်, ဒီ+Noun -> ဤ+Noun, အတွက်ကို -> အတွက်)
            OfficialRewriteResult rw = MyanmarOfficialRewriter.ConvertToOfficialProse("ပြီးတော့ ဒီလုပ်ငန်းအတွက်ကို ဆွေးနွေးပါတယ်။");
            Assert.True(rw.HasChanges);
            Assert.Contains("ထို့ပြင်", rw.RewrittenText);
            Assert.Contains("ဤလုပ်ငန်း", rw.RewrittenText);
            Assert.DoesNotContain("အတွက်ကို", rw.RewrittenText);
            Assert.Empty(formalEngine.CheckText(rw.RewrittenText));
        }

        [Fact]
        public void V32_MassiveMyanmarToMyanmarKnowledgeDictionary_Coverage_Encyclopedia_Antonyms_AndFastSearch()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Massive Offline Lexicon + Encyclopedic Knowledge Corpus Scale
            Assert.True(trie.WordCount >= 88_000, $"Expected >= 88,000 lexical headwords, got {trie.WordCount}");
            Assert.True(trie.EncyclopediaCount >= 24_000, $"Expected >= 24,000 ENCYC encyclopedic knowledge entries, got {trie.EncyclopediaCount}");
            Assert.True(trie.ReferenceCount >= 25_800, $"Expected >= 25,800 total reference entries (Proverbs + Encyclopedia), got {trie.ReferenceCount}");

            // 2. 100% Authentic Myanmar-to-Myanmar Definition Coverage across Lexical Entries (Zero raw English-only MeaningCues!)
            var allLexical = trie.GetAllEntries().ToList();
            int mmDefCount = 0;
            int rawEnglishOnlyCount = 0;
            foreach (var entry in allLexical)
            {
                if (!string.IsNullOrWhiteSpace(entry.MeaningCue))
                {
                    bool hasMm = entry.MeaningCue.Any(c => c >= '\u1000' && c <= '\u102A');
                    if (hasMm)
                        mmDefCount++;
                    else
                        rawEnglishOnlyCount++;
                }
            }
            Assert.True(mmDefCount >= 88_000, $"Expected >= 88,000 lexical entries with Myanmar-to-Myanmar MeaningCues, got {mmDefCount}");
            Assert.Equal(0, rawEnglishOnlyCount);

            // 3. Encyclopedic Knowledge Search (Burmese & Bilingual English) + Sub-Millisecond Indexing
            var encycKachin = engine.SearchReferenceCorpus("ကချင်ပြည်နယ်", includeProverbs: false, includeEncyclopedia: true, maxResults: 10);
            Assert.Contains(encycKachin, e => e.Word == "ကချင်ပြည်နယ်" && e.IsEncyclopediaEntry && !string.IsNullOrEmpty(e.KnowledgeSummary));

            var encycGrammar = engine.SearchReferenceCorpus("ဃောသအက္ခရာ", includeProverbs: false, includeEncyclopedia: true, maxResults: 10);
            Assert.Contains(encycGrammar, e => e.IsEncyclopediaEntry && e.MeaningCue.Contains("ဃောသအက္ခရာ"));

            var mixedSearch = engine.SearchDictionary("ကကတစ်", 20);
            Assert.Contains(mixedSearch, e => !e.IsReferenceEntry && e.MeaningCue.Contains("ရေချို"));
            Assert.Contains(mixedSearch, e => e.IsEncyclopediaEntry && e.Word == "ကကတစ်");

            // 4. Burmese Antonym Index (ဆန့်ကျင်ဘက်) & Featured Knowledge Discovery
            var goodAnts = engine.GetAntonyms("အကောင်း");
            Assert.Contains("အဆိုး", goodAnts);
            var peaceAnts = engine.GetAntonyms("ငြိမ်းချမ်းရေး");
            Assert.NotEmpty(peaceAnts);

            var featuredBatch1 = engine.GetFeaturedKnowledgeEntries(8);
            var featuredBatch2 = engine.GetFeaturedKnowledgeEntries(8);
            Assert.Equal(8, featuredBatch1.Count);
            Assert.Contains(featuredBatch1, e => e.IsEncyclopediaEntry);
            Assert.NotEqual(featuredBatch1[0].Word, featuredBatch2[0].Word);

            // 5. Sub-15ms Search Latency Gate across Mixed Lexical + 25,000 Encyclopedia Corpus
            string[] perfQueries = { "မြန်မာသဒ္ဒါ", "ကချင်ပြည်နယ်", "ကင်ဆာရောဂါ", "တူရိယာ", "ဆရာကောင်း", "ဘေးကင်း", "Celtic", "ပညာ" };
            foreach (string pq in perfQueries)
                _ = engine.SearchDictionary(pq, 35); // Warm lazy index

            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < perfQueries.Length; i++)
            {
                var hits = engine.SearchDictionary(perfQueries[i], 35);
                Assert.NotEmpty(hits);
            }
            sw.Stop();
            double avgMs = sw.Elapsed.TotalMilliseconds / perfQueries.Length;
            Assert.True(avgMs < 15.0, $"Expected < 15 ms average search latency with 25,000 ENCYC entries, got {avgMs:0.00} ms");
        }

        [Fact]
        public void V33_HighConcurrency_ThreadSafety_LargeDocumentThroughput_AndZeroRaceStressTest()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            trie.PrewarmSearchIndexes();
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            string tempCustomDict = Path.Combine(Path.GetTempPath(), $"mm_v33_stress_{Guid.NewGuid():N}.txt");
            try
            {
                string[] sampleParagraphs =
                {
                    "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် ပြန်ကြားရေးဝန်ကြီးဌာနမှ တရားဝင် ထုတ်ပြန်ကြေညာချက် ထုတ်ပြန်သည်။",
                    "ဆန်းပြားသော တူးရိယာ ကိရိယာများနှင့် ကိတ်မုဒ်များကို ပန်းပုဆရာကောင်းတစ်ဦးက လေ့လာပါတယ်။",
                    "နိုင်ငံတကာ သံတမန်ရေးရာ ဆက်ဆံရေးတွင် မြန်မာနိုင်ငံနှင့် ဂျပန်နိုင်ငံတို့ ပူးပေါင်းဆောင်ရွက်ကြသည်။",
                    "အစည်းအဝေးသို့ ဝန်ကြီးဌာန အသီးသီးမှ တာဝန်ရှိသူတွေ တက်ရောက်ဆွေးနွေးကြပါတယ်။"
                };
                string[] searchQueries = { "တူရိယာ", "Myanmar", "+95", "ရေချို", "ကချင်ပြည်နယ်", "ပညာရေး" };

                var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

                System.Threading.Tasks.Parallel.For(0, 120, i =>
                {
                    try
                    {
                        var issues = engine.CheckText(sampleParagraphs[i % sampleParagraphs.Length]);
                        Assert.NotNull(issues);

                        var dictHits = engine.SearchDictionary(searchQueries[i % searchQueries.Length], 25);
                        Assert.NotEmpty(dictHits);

                        if (i % 3 == 0)
                        {
                            var refHits = engine.SearchReferenceCorpus("ကချင်ပြည်နယ်", includeProverbs: true, includeEncyclopedia: true, maxResults: 10);
                            Assert.NotEmpty(refHits);
                            var syns = engine.GetSynonyms("ငြိမ်းချမ်းရေး", 6);
                            Assert.NotNull(syns);
                            var ants = engine.GetAntonyms("အကောင်း", 6);
                            Assert.Contains("အဆိုး", ants);
                            var feat = engine.GetFeaturedKnowledgeEntries(6);
                            Assert.Equal(6, feat.Count);
                            string homo = engine.GetHomophoneSiblingsSummary("ကိရိယာ");
                            Assert.NotNull(homo);
                        }

                        if (i % 10 == 0)
                        {
                            string customToken = $"စမ်းသပ်ဝေါဟာရဗဟို{(i % 3) + 1}";
                            engine.AddCustomWord(customToken, tempCustomDict);
                            engine.IgnoreWordInSession($"ယာယီစကားလုံး{(i % 4) + 1}");
                            engine.StyleMode = (i % 20 == 0) ? ProofingStyleMode.GeneralStandard : ProofingStyleMode.FormalOfficial;
                            engine.RemoveCustomWord(customToken, tempCustomDict);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                });

                Assert.Empty(exceptions);

                // Clean up custom words & restore FormalOfficial mode, then verify 100% deterministic accuracy
                foreach (string cw in engine.GetCustomWords().ToList())
                {
                    engine.RemoveCustomWord(cw, tempCustomDict);
                }
                engine.StyleMode = ProofingStyleMode.FormalOfficial;

                var postStressIssues = engine.CheckText("ဆန်းပြားသော တူးရိယာနှင့် ကိတ်မုဒ်ကို ဝယ်ယူပါတယ်။");
                Assert.Contains(postStressIssues, x => x.OriginalText == "တူးရိယာ" && x.Suggestions.Any(s => s.Replacement == "တူရိယာ"));
                Assert.Contains(postStressIssues, x => x.OriginalText == "ကိတ်မုဒ်" && x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder("ကိတ်မုန့်")));
                Assert.Contains(postStressIssues, x => x.IssueType == ProofingIssueType.OfficialStyleGrammarError);

                // Large 40-paragraph document throughput benchmark (Stage 2 background scan + QualityReport budget)
                var allDocIssues = new List<ProofingIssue>(128);
                var docParagraphs = Enumerable.Range(0, 40)
                    .Select(idx => sampleParagraphs[idx % sampleParagraphs.Length])
                    .ToArray();
                string joinedDocText = string.Join("\n", docParagraphs);

                var swDoc = new Stopwatch();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                swDoc.Start();
                foreach (string para in docParagraphs)
                {
                    allDocIssues.AddRange(engine.CheckText(para));
                }
                DocumentQualityReport report = DocumentQualityAnalyzer.Analyze(joinedDocText, allDocIssues, engine);
                swDoc.Stop();

                Assert.True(allDocIssues.Count >= 40);
                Assert.True(report.MyanmarSyllableCount > 400);
                Assert.True(swDoc.Elapsed.TotalMilliseconds < 400.0, $"Expected 40-paragraph full NLP + QualityReport under 400 ms, got {swDoc.Elapsed.TotalMilliseconds:0.00} ms");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempCustomDict))
                        File.Delete(tempCustomDict);
                }
                catch { }
            }
        }

        [Fact]
        public void V34_PastedParagraphWithZeroWidthSpaces_HonorificBoundaries_AndWrongMeaningRealWords_AllDetectedWithZeroFalsePositives()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Web/Wikipedia/Facebook/PDF pasted paragraph laced with U+200B, U+200C, U+FEFF, U+00A0
            string webPastedDirty =
                "\uFEFFလူ\u200Bသား\u200Bများ\u200Bသည်\u00A0ဆန်း\u200Bပြား\u200Bသော တူး\u200Bရိ\u200Bယာ ကိ\u200Bရိ\u200Bယာ\u200Bများ\u200Bကို တီ\u200Bထွင်\u200B၍ " +
                "ကုန်\u200Bဈေး\u200Bနှုန်း လျော့\u200Bချ\u200Bပေး\u200Bရန် အချက်\u200Bအလက် ပြည်\u200Bစုံ\u200Bစွာ စာ\u200Cဖက်\u200Bသည်\u200B။";

            var webIssues = engine.CheckText(webPastedDirty);
            foreach (var iss in webIssues)
            {
                Assert.Equal(webPastedDirty.Substring(iss.StartIndex, iss.Length), iss.OriginalText);
                Assert.All(iss.Suggestions, s => Assert.DoesNotContain("\u200B", s.Replacement));
            }

            Assert.Contains(webIssues, x => x.OriginalText == "တူး\u200Bရိ\u200Bယာ" && x.Suggestions.Any(s => s.Replacement == "တူရိယာ"));
            Assert.Contains(webIssues, x => x.OriginalText == "လျော့\u200Bချ" && x.Suggestions.Any(s => s.Replacement == "လျှော့ချ"));
            Assert.Contains(webIssues, x => x.OriginalText == "ပြည်\u200Bစုံ" && x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder("ပြည့်စုံ")));
            Assert.Contains(webIssues, x => x.OriginalText == "စာ\u200Cဖက်" && x.Suggestions.Any(s => s.Replacement == "စာဖတ်"));
            // Clean words in the U+200B-laced paragraph must NOT be falsely underlined
            Assert.DoesNotContain(webIssues, x => x.OriginalText.Contains("ကိ\u200Bရိ\u200Bယာ"));

            string webPastedClean =
                "လူ\u200Bသား\u200Bများ\u200Bသည်\u00A0ဆန်း\u200Bပြား\u200Bသော တူ\u200Bရိ\u200Bယာ ကိ\u200Bရိ\u200Bယာ\u200Bများ\u200Bကို တီ\u200Bထွင်\u200Bနိုင်\u200Bသည်\u200B။";
            Assert.Empty(engine.CheckText(webPastedClean));

            // 2. Space-separated 'ကို', 'ဆရာ', 'ဆရာကြီး' must never falsely mask following misspelled/wrong-meaning words
            var roleCases = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("ဆရာ စာဖက်သည်။", "စာဖက်", "စာဖတ်"),
                ("လူသားများ ကို တူးရိယာ ပေးသည်။", "တူးရိယာ", "တူရိယာ"),
                ("ဆရာကြီး တူးရိယာနှင့် ကိတ်မုဒ်ကို ဝယ်ယူသည်။", "တူးရိယာ", "တူရိယာ")
            };
            foreach (var rc in roleCases)
            {
                var rIssues = engine.CheckText(rc.Input);
                Assert.Contains(rIssues, x =>
                    x.OriginalText.Contains(rc.ExpectedWrong) &&
                    x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement).Contains(UnicodeNormalizer.NormalizeCanonicalOrder(rc.ExpectedFix))));
            }

            // Genuine personal names remain 100% protected
            Assert.Empty(engine.CheckText("ဦးအောင်ကျော်မိုးနှင့် ဒေါ်ခင်မေသန်းတို့ တက်ရောက်သည်။"));
            Assert.Empty(engine.CheckText("ဆရာ ဦးအောင်ကျော်မိုးနှင့် ဆရာမ ဒေါ်ခင်မေသန်းတို့ တက်ရောက်သည်။"));

            // 3. Real-word wrong-meaning homophones & formerly corpus-shadowed typos in pasted paragraphs
            var wrongMeaningCases = new (string Input, string ExpectedWrong, string ExpectedFix)[]
            {
                ("ကုန်ဈေးနှုန်းကို လျော့ချပေးရန် ဆောင်ရွက်သည်။", "လျော့ချ", "လျှော့ချ"),
                ("မိုးရွာသွန်းမှု လျှော့နည်းသွားသည်။", "လျှော့နည်း", "လျော့နည်း"),
                ("ကလေးများ ကျောင်းဝတ်စုံ အသစ် ဝက်ဆင်ကြသည်။", "ဝက်ဆင်", "ဝတ်ဆင်"),
                ("အချက်အလက်များ ပြည်စုံစွာ ဖျည့်စွက်ပေးပါ။", "ပြည်စုံ", "ပြည့်စုံ"),
                ("ပြဿနာများကို သေခြာစွာ စမ်းစစ်၍ ဖျေရှင်းရမည်။", "စမ်းစစ်", "ဆန်းစစ်"),
                ("ပြဿနာများကို သေခြာစွာ စမ်းစစ်၍ ဖျေရှင်းရမည်။", "သေခြာ", "သေချာ"),
                ("ပြဿနာများကို သေခြာစွာ စမ်းစစ်၍ ဖျေရှင်းရမည်။", "ဖျေရှင်း", "ဖြေရှင်း"),
                ("သိပ္ပံဓာတ်ခွဲခန်းတွင် လက်တွေ့ ဆန်းသပ်သည်။", "ဆန်းသပ်", "စမ်းသပ်"),
                ("နိုင်ငံတော်အတွက် အကြိုးကျေးဇူး များစွာ ရရှိစေသည်။", "အကြိုးကျေးဇူး", "အကျိုးကျေးဇူး"),
                ("ကျောင်းသားများ စာသင်ခမ်းအတွင်း စာကြက်နေကြသည်။", "စာသင်ခမ်း", "စာသင်ခန်း"),
                ("ကျောင်းသားများ စာသင်ခမ်းအတွင်း စာကြက်နေကြသည်။", "စာကြက်", "စာကျက်"),
                ("လိုင်စင် သက်တန်း ကုန်ဆုံးမည့် နေ့ရပ်ကို စစ်ဆေးပါ။", "သက်တန်း", "သက်တမ်း"),
                ("လိုင်စင် သက်တန်း ကုန်ဆုံးမည့် နေ့ရပ်ကို စစ်ဆေးပါ။", "နေ့ရပ်", "နေ့ရက်"),
                ("တိုင်းရင်းသားအားလုံး စီးလုံးညီညွတ်စွာ စည်းပွားရေး ဆောင်ရွက်သည်။", "စီးလုံး", "စည်းလုံး"),
                ("တိုင်းရင်းသားအားလုံး စီးလုံးညီညွတ်စွာ စည်းပွားရေး ဆောင်ရွက်သည်။", "စည်းပွားရေး", "စီးပွားရေး"),
                ("ဂရုဏာတရားဖြင့် ခေတ်မှီနည်းပညာနှင့် ကထိကဝတ်များကို စင်္ကြန်လမ်း၌ လေ့လာသည်။", "ဂရုဏာ", "ကရုဏာ"),
                ("ဂရုဏာတရားဖြင့် ခေတ်မှီနည်းပညာနှင့် ကထိကဝတ်များကို စင်္ကြန်လမ်း၌ လေ့လာသည်။", "ခေတ်မှီ", "ခေတ်မီ"),
                ("ဂရုဏာတရားဖြင့် ခေတ်မှီနည်းပညာနှင့် ကထိကဝတ်များကို စင်္ကြန်လမ်း၌ လေ့လာသည်။", "ကထိကဝတ်", "ကတိကဝတ်"),
                ("ဂရုဏာတရားဖြင့် ခေတ်မှီနည်းပညာနှင့် ကထိကဝတ်များကို စင်္ကြန်လမ်း၌ လေ့လာသည်။", "စင်္ကြန်", "စင်္ကြံ")
            };

            foreach (var wc in wrongMeaningCases)
            {
                string expFixNorm = UnicodeNormalizer.NormalizeCanonicalOrder(wc.ExpectedFix);
                var issues = engine.CheckText(wc.Input);
                Assert.True(
                    issues.Any(x =>
                        x.OriginalText.Contains(wc.ExpectedWrong) &&
                        x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement).Contains(expFixNorm))),
                    $"Expected '{wc.ExpectedWrong}' -> '{wc.ExpectedFix}' in: {wc.Input} (got: {string.Join(", ", issues.Select(i => i.OriginalText + "->" + (i.Suggestions.FirstOrDefault()?.Replacement ?? "")))})");
            }

            // 4. Zero false positives on valid lookalike/boundary sentences
            Assert.Empty(engine.CheckText("စုံစမ်းစစ်ဆေးရေးအဖွဲ့က အသေးစိတ် ဆန်းစစ်လေ့လာသည်။"));
            Assert.Empty(engine.CheckText("ကားတစ်စီးလုံး သန့်ရှင်းသပ်ရပ်နေသည်။"));
            Assert.Empty(engine.CheckText("ကုန်ဈေးနှုန်း လျှော့ချပေးသဖြင့် စရိတ် လျော့နည်းသွားသည်။"));
            Assert.Empty(engine.CheckText("အချက်အလက်များ ပြည့်စုံစွာ ဖြည့်စွက်ပြီး စနစ်တကျ ဖြေရှင်းသည်။"));
        }

        [Fact]
        public void V35_DeepMyanmarGrammarPatterns_Postpositions_RegisterHarmony_Pleonasm_PronounCase_AndHonorificConcord_WorkWithZeroFalsePositives()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Deep Myanmar Grammar Patterns in FormalOfficial Mode
            var formalGrammarCases = new (string Input, string ExactSpan, string ExpectedFix)[]
            {
                // WH-Interrogative <-> Polar 'လား/သလား' Clash & Literary WH <-> Colloquial 'လဲ' Clash
                ("အဘယ်ကြောင့် အစည်းအဝေး နောက်ကျပါသလား။", "ပါသလား", "ပါသနည်း"),
                ("မည်သို့ ဆောင်ရွက်ရမလဲ။", "မလဲ", "မည်နည်း"),

                // Oblique Creaky-Tone Pronoun Case Discord & Illegal Creaky Pronouns (both UTN #11 \u1037\u103A and Asat-first \u103A\u1037 orders)
                ("သူ့သည် စာပေကြိုးစားလေ့လာသည်။", "သူ့သည်", "သူသည်"),
                ("ကျွန်တော့်က အစီရင်ခံစာ တင်ပြပါသည်။", "ကျွန်တော့်က", "ကျွန်တော်က"),
                ("ကျွန်တော်\u1037က အစီရင်ခံစာ တင်ပြပါသည်။", "ကျွန်တော်\u1037က", "ကျွန်တော်က"),
                ("သူ့တို့ အားလုံး တက်ရောက်ကြသည်။", "သူ့တို့", "သူတို့"),
                ("သင့်တို့ လိုက်နာရမည်။", "သင့်တို့", "သင်တို့"),
                ("ကျွန်ုပ့် သဘောထားကို တင်ပြပါသည်။", "ကျွန်ုပ့်", "ကျွန်ုပ်"),
                ("ကျွန်မ့ စာအုပ်ကို ယူလာပါသည်။", "ကျွန်မ့", "ကျွန်မ"),

                // Universal & Official Postposition Argument Structure ('အား -> ကို', 'မှ -> က', 'ကနေ -> မှ')
                ("နှစ်ပတ်လည် အခမ်းအနားအား ကျင်းပပါသည်။", "အား", "ကို"),
                ("သုတေသန ဆောင်းပါးအား ဖော်ပြထားသည်။", "အား", "ကို"),
                ("စည်းကမ်းချက်များအား လိုက်နာရမည်။", "အား", "ကို"),
                ("သက်ဆိုင်ရာမှ အမြန်ဆုံး အရေးယူပေးပါရန် တင်ပြအပ်ပါသည်။", "မှ", "က"),
                ("ရန်ကုန်မြို့ကနေ မန္တလေးမြို့သို့ ထွက်ခွာသည်။", "ကနေ", "မှ"),

                // Pleonasm, Hybrid Conjunctions & Stacked Postpositions
                ("အစည်းအဝေး မတိုင်မီခင် ကြိုတင်ပြင်ဆင်ပါ။", "မတိုင်မီခင်", "မတိုင်မီ"),
                ("ပညာရေး ဖွံ့ဖြိုးတိုးတက်စေရန် အလို့ငှာအတွက် ဆောင်ရွက်သည်။", "အလို့ငှာအတွက်", "အတွက်"),
                ("မိုးသည်းထန်စွာ ရွာသွန်းသဖြင့်ကြောင့် လမ်းပိတ်သွားသည်။", "သဖြင့်ကြောင့်", "သောကြောင့်"),
                ("ကြိုးစားအားထုတ်သော်လည်း သို့သော်လည်းပဲ အခက်အခဲရှိသည်။", "သို့သော်လည်းပဲ", "သို့သော်လည်း"),
                ("ကျောင်းသားများတွေကို ဆုချီးမြှင့်သည်။", "များတွေ", "များ"),
                ("ခန်းမအတွင်းတွင်၌ ကျင်းပသည်။", "တွင်၌", "တွင်"),

                // Adverbial 'စသဖြင့်' misused before nominal postpositions
                ("ကျန်းမာရေး၊ ပညာရေး စသဖြင့်တို့ကို ဆောင်ရွက်သည်။", "စသဖြင့်တို့ကို", "စသည်တို့ကို"),
                ("စာအုပ်၊ ခဲတံ စသဖြင့်များ ပါဝင်သည်။", "စသဖြင့်များ", "စသည်များ"),

                // Correlative 'အကယ်၍ ... သောကြောင့်' & Dangling Causal Medial Fragment '...သဖြင့်။'
                ("အကယ်၍ လိုအပ်သောကြောင့် ဆက်သွယ်နိုင်ပါသည်။", "သောကြောင့်", "ပါက"),
                ("ရာသီဥတုသည် အလွန်ဆိုးရွားနေသဖြင့်။", "သဖြင့်", "သောကြောင့် ဖြစ်ပါသည်"),

                // Monastic / Clergy Honorific Subject–Verb Concord
                ("ဆရာတော်ကြီးသည် နေ့ဆွမ်းချိန်၌ ထမင်းစားသည်။", "ထမင်းစား", "ဆွမ်းဘုဉ်းပေး"),
                ("ဆရာတော်ကြီးသည် သက်တော် ၈၅ နှစ်တွင် ကွယ်လွန်သည်။", "ကွယ်လွန်", "ပျံလွန်တော်မူ"),
                ("သံဃာတော်များသည် ကျောင်းတိုက်၌ နေထိုင်ကြသည်။", "နေထိုင်", "သီတင်းသုံး"),

                // Auxiliary Verb Particle Slot Inversion & Bare Negated Verb Before 'အတွက်'
                ("ပြည်သူများ ပါဝင်ဆောင်ရွက်ပါနိုင်သည်။", "ပါနိုင်", "နိုင်ပါ"),
                ("လိုအပ်သလို သိရှိစေပါလိုသည်။", "စေပါလို", "စေလိုပါ"),
                ("နာရီပေါင်းများစွာ စောင့်ဆိုင်းခဲ့နေသည်။", "ခဲ့နေ", "နေခဲ့"),
                ("အချက်အလက် မပြည့်စုံအတွက် ပြန်လည်စိစစ်ပါ။", "အတွက်", "သည့်အတွက်"),

                // Expanded Numeral Classifiers (31 Categories) & Exact Classifier + Redundant Plural
                ("မြစ် နှစ်လုံး ဖြတ်သန်းစီးဆင်းသည်။", "လုံး", "သွယ်"),
                ("သတင်းစာ သုံးလုံး ဖတ်ရှုသည်။", "လုံး", "စောင်"),
                ("ရုပ်ရှင်ဇာတ်ကား နှစ်အုပ် ကြည့်ရှုသည်။", "အုပ်", "ကား"),
                ("စားပွဲ လေးချပ် ခင်းကျင်းထားသည်။", "ချပ်", "လုံး"),
                ("ကော်မတီ သုံးဦး ဖွဲ့စည်းထားသည်။", "ဦး", "ဖွဲ့"),
                ("တရားတော် နှစ်လုံး နာယူသည်။", "လုံး", "ပုဒ်"),
                ("ကတ်ကြေး နှစ်လုံး ဝယ်ယူသည်။", "လုံး", "လက်"),
                ("ခြုံစောင် သုံးလုံး လှူဒါန်းသည်။", "လုံး", "ထည်"),
                ("ကျောင်းသား သုံးဦးများကို ဆုချီးမြှင့်သည်။", "ဦးများ", "ဦး")
            };

            foreach (var tc in formalGrammarCases)
            {
                var issues = formalEngine.CheckText(tc.Input);
                Assert.True(
                    issues.Any(x =>
                        x.OriginalText == tc.ExactSpan &&
                        !string.IsNullOrWhiteSpace(x.GrammaticalReasoning) &&
                        !string.IsNullOrWhiteSpace(x.AuthoritativeSource) &&
                        x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedFix))),
                    $"Expected '{tc.ExactSpan}' -> '{tc.ExpectedFix}' in Formal mode: {tc.Input} (got: {string.Join("; ", issues.Select(i => i.OriginalText + "=>" + (i.Suggestions.FirstOrDefault()?.Replacement ?? "")))})");
            }

            // 2. GeneralStandard Mode: Catches Universal Grammar & Register Disharmony while Allowing Pure Colloquial
            var generalGrammarCases = new (string Input, string ExactSpan, string ExpectedFix)[]
            {
                ("နင် ဘာကြောင့် မလာသလား။", "သလား", "သလဲ"),
                ("အခမ်းအနားအား ကျင်းပပါတယ်။", "အား", "ကို"),
                ("ဤအစည်းအဝေး၌ အသေးစိတ် ဆွေးနွေးကြပါတယ်။", "ပါတယ်", "ပါသည်"),
                ("ကျောင်းသားတွေ စာကြိုးစားကြပေသည်။", "တွေ", "များ"),
                ("အချက်အလက် မသိအတွက် မဖြေနိုင်ပါ။", "အတွက်", "တဲ့အတွက်")
            };

            foreach (var gc in generalGrammarCases)
            {
                var issues = generalEngine.CheckText(gc.Input);
                Assert.True(
                    issues.Any(x =>
                        x.OriginalText == gc.ExactSpan &&
                        x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement) == UnicodeNormalizer.NormalizeCanonicalOrder(gc.ExpectedFix))),
                    $"Expected '{gc.ExactSpan}' -> '{gc.ExpectedFix}' in General mode: {gc.Input} (got: {string.Join("; ", issues.Select(i => i.OriginalText + "=>" + (i.Suggestions.FirstOrDefault()?.Replacement ?? "")))})");
            }

            // 3. Zero False Positives on Tricky Valid Lookalike Constructions
            string[] cleanFormalSentences =
            {
                "ထိုသို့ ဆောင်ရွက်ရန် သင့်သည်ဟု ယူဆပါသည်။",
                "သူ့ကို ကူညီပြီး ကျွန်တော့်အိမ်သို့ ဖိတ်ခေါ်ပါသည်။",
                "သူ့ကားနှင့် သူ့ကလေးများ ကျောင်းသို့ ရောက်လာကြသည်။",
                "နိုင်ငံတော်က ထူးချွန်ဝန်ထမ်းများအား ဂုဏ်ထူးဆောင်ဆုတံဆိပ်ကို ချီးမြှင့်သည်။",
                "ကျန်းမာရေး၊ ပညာရေး စသဖြင့် ကဏ္ဍစုံ ဖွံ့ဖြိုးတိုးတက်လာသည်။",
                "ဥပမာအားဖြင့် စာအုပ်၊ ခဲတံ၊ ပေတံ စသဖြင့်။",
                "ဇာတ်ခုံပေါ်၌ ကချေသည်များ ကနေသည်။",
                "ကော်မတီဝင် သုံးဦးတို့သည် ကော်မတီ သုံးဖွဲ့တွင် ပါဝင်ဆောင်ရွက်သည်။",
                "ရုပ်ရှင်ဇာတ်ကား သုံးကားကို အခမဲ့ ပြသသည်။",
                "တူ နှစ်ဦးနှင့် တူမ သုံးဦးတို့ လာရောက်ဂါရဝပြုကြသည်။",
                "ဆရာတော်ကြီးသည် ကျောင်းတိုက်၌ သီတင်းသုံးတော်မူပြီး နေ့ဆွမ်းဘုဉ်းပေးသည်။",
                "ဆရာတော်ကြီးသည် ဒါယကာများ ထမင်းစားနေသည်ကို မေတ္တာဖြင့် ကြည့်ရှုသည်။"
            };

            foreach (string clean in cleanFormalSentences)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0,
                    $"Unexpected Formal false positive in '{clean}': {string.Join("; ", fp.Select(i => i.OriginalText + "=>" + (i.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            string[] cleanGeneralSentences =
            {
                "ဘာကြောင့် နောက်ကျသလဲ ဆိုတာ သိသလား။",
                "ရန်ကုန်ကနေ မန္တလေးကို သွားမယ့် ရထား ဖြစ်ပါတယ်။",
                "ထိုအခါ ဆရာကြီးက တပည့်နှစ်ယောက်ကို ခေါ်ပြီး ဆုံးမစကား ပြောကြားခဲ့တယ်။"
            };

            foreach (string cleanGen in cleanGeneralSentences)
            {
                var fpGen = generalEngine.CheckText(cleanGen);
                Assert.True(fpGen.Count == 0,
                    $"Unexpected General false positive in '{cleanGen}': {string.Join("; ", fpGen.Select(i => i.OriginalText + "=>" + (i.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }
        }

        [Fact]
        public void V47_ModelessReviewStudio_DiffBadges_DeterministicSuggestions_AndZeroModalLockGate()
        {
            // 1. Static Non-Blocking Modeless & Minimizable Window Gate across WordAddIn + ExcelAddIn
            string wordAddInSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "ThisAddIn.cs"));
            string excelAddInSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "ExcelThisAddIn.cs"));
            string wordReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "FixAllPreviewDialog.cs"));
            string excelReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "FixAllPreviewDialog.cs"));

            // Review & Fix must NEVER call modal ShowDialog(ownerWin) (which locks background Word/Excel windows)
            Assert.DoesNotContain("dlg.ShowDialog(", wordAddInSrc);
            Assert.DoesNotContain("dlg.ShowDialog(", excelAddInSrc);
            Assert.Contains("dlg.Show();", wordAddInSrc);
            Assert.Contains("dlg.Show();", excelAddInSrc);

            // Review & Fix window must allow MinimizeBox = true and ShowInTaskbar = true
            Assert.Contains("MinimizeBox = true;", wordReviewSrc);
            Assert.Contains("MinimizeBox = true;", excelReviewSrc);
            Assert.Contains("ShowInTaskbar = true;", wordReviewSrc);
            Assert.Contains("ShowInTaskbar = true;", excelReviewSrc);

            // 2. Visual Diff Clarity Badges for invisible ZWSP, Myanmar3 mark-order, and lookalike characters
            var zwspIssue = new ProofingIssue
            {
                OriginalText = "မှတ်ပုံတင်ထားခြင်း\u200Bအတွက်",
                IssueType = ProofingIssueType.SpellingError,
                Suggestions = new List<SuggestionCandidate> { new SuggestionCandidate { Replacement = "မှတ်ပုံတင်ထားခြင်းအတွက်", Score = 100.0 } }
            };
            Assert.Contains("[ZWSP]", zwspIssue.FormatVisualOriginalText());

            var myanmar3OrderIssue = new ProofingIssue
            {
                OriginalText = "ပု\u102D\u1037ကုန်သွင်းကုန်",
                IssueType = ProofingIssueType.LookalikeCharacterError,
                Suggestions = new List<SuggestionCandidate> { new SuggestionCandidate { Replacement = "ပို့ကုန်သွင်းကုန်", Score = 100.0 } }
            };
            Assert.Contains("[ုိ➔ို]", myanmar3OrderIssue.FormatVisualOriginalText());

            var tallAaIssue = new ProofingIssue
            {
                OriginalText = "အကောင်အထည်ဖေါ်",
                IssueType = ProofingIssueType.LookalikeCharacterError,
                Suggestions = new List<SuggestionCandidate> { new SuggestionCandidate { Replacement = "အကောင်အထည်ဖော်", Score = 100.0 } }
            };
            Assert.Contains("[ါ➔ာ]", tallAaIssue.FormatVisualOriginalText());

            // 3. Deterministic Suggestion Cleanliness (no bogus fuzzy noise on 100% certain lookalike/orthography rules)
            var model = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var trie = new MyanmarTrie();
            foreach (var e in model.Entries) trie.InsertEntry(e);
            foreach (var r in model.OrthographyRules) trie.InsertOrthographyRule(r);
            if (model.ContextRules != null)
                foreach (var cr in model.ContextRules) trie.InsertContextualRule(cr);
            if (model.WordBigrams != null)
                foreach (var bg in model.WordBigrams) trie.InsertWordBigram(bg.LeftWord, bg.RightWord, bg.Count);

            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            var akaungIssues = formalEngine.CheckText("အကောင်အထည်ဖေါ် ဆောင်ရွက်သည်");
            var akaungHit = Assert.Single(akaungIssues, i => i.OriginalText == "အကောင်အထည်ဖေါ်");
            var akaungSingleSug = Assert.Single(akaungHit.Suggestions);
            Assert.Equal("အကောင်အထည်ဖော်", akaungSingleSug.Replacement);

            var anagatIssues = formalEngine.CheckText("အနာဂါတ် စီမံကိန်း");
            var anagatHit = Assert.Single(anagatIssues, i => i.OriginalText == "အနာဂါတ်");
            var anagatSingleSug = Assert.Single(anagatHit.Suggestions);
            Assert.Equal("အနာဂတ်", anagatSingleSug.Replacement);

            // 4. Window Preferences Store round-trip
            string testHost = "unit_test_v47_" + Guid.NewGuid().ToString("N");
            var prefsIn = new MyanmarSpellChecker.Core.Performance.ReviewWindowPreferences
            {
                HasSavedBounds = true,
                X = 48,
                Y = 92,
                Width = 760,
                Height = 560,
                IsPinnedTopMost = false,
                IsGroupedByWord = true,
                PreferDockedInTaskPane = true
            };
            MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Save(testHost, prefsIn);
            var prefsOut = MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Load(testHost);
            Assert.True(prefsOut.HasSavedBounds);
            Assert.Equal(48, prefsOut.X);
            Assert.Equal(92, prefsOut.Y);
            Assert.Equal(760, prefsOut.Width);
            Assert.Equal(560, prefsOut.Height);
            Assert.False(prefsOut.IsPinnedTopMost);
            Assert.True(prefsOut.IsGroupedByWord);
            Assert.True(prefsOut.PreferDockedInTaskPane);
        }

        [Fact]
        public void V48_ProofingAuditReportBuilder_WrongVsSuspiciousVsSessionFixed_HtmlAndSummaryGate()
        {
            // 1. Static UI Wiring Gate across Word & Excel Add-Ins
            string wordTaskPaneSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.cs"));
            string excelTaskPaneSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.cs"));
            string wordReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "FixAllPreviewDialog.cs"));
            string excelReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "FixAllPreviewDialog.cs"));

            Assert.Contains("RequestExportProofingReport", wordTaskPaneSrc);
            Assert.Contains("RequestExportProofingReport", excelTaskPaneSrc);
            Assert.Contains("OnExportReport", wordReviewSrc);
            Assert.Contains("OnExportReport", excelReviewSrc);

            // 2. Build realistic multi-bucket document input (Confirmed Wrong vs. Suspicious vs. Already Fixed in Session)
            var confirmedLookalike1 = new ProofingIssue
            {
                OriginalText = "အကောင်အထည်ဖေါ်",
                IssueType = ProofingIssueType.LookalikeCharacterError,
                RequiresReviewBeforeBatchFix = false,
                Message = "ဤဗျည်းနောက်တွင် မောက်ချ 'ါ' မသုံးရဘဲ ရေးချ 'ာ' ဖြင့်သာ ရေးသားရမည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "အကောင်အထည်ဖော်", Score = 100.0, MeaningCue = "အကောင်အထည်ပေါ်အောင် ဆောင်ရွက်သည်" }
                }
            };
            var confirmedLookalike2 = new ProofingIssue
            {
                OriginalText = "အကောင်အထည်ဖေါ်",
                IssueType = ProofingIssueType.LookalikeCharacterError,
                RequiresReviewBeforeBatchFix = false,
                Message = "ဤဗျည်းနောက်တွင် မောက်ချ 'ါ' မသုံးရဘဲ ရေးချ 'ာ' ဖြင့်သာ ရေးသားရမည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "အကောင်အထည်ဖော်", Score = 100.0 }
                }
            };
            var confirmedZwsp = new ProofingIssue
            {
                OriginalText = "မှတ်ပုံတင်ထားခြင်း\u200Bအတွက်",
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = false,
                Message = "စာလုံးအတွင်း မမြင်ရသော ZWSP (U+200B) ပါဝင်နေပါသည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "မှတ်ပုံတင်ထားခြင်းအတွက်", Score = 100.0 }
                }
            };
            var confirmedGrammar = new ProofingIssue
            {
                OriginalText = "ရဲ့",
                IssueType = ProofingIssueType.OfficialStyleGrammarError,
                RequiresReviewBeforeBatchFix = false,
                Message = "ရုံးသုံး/စာပေဟန်တွင် 'ရဲ့' အစား '၏' ကို သုံးသင့်ပါသည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "၏", Score = 98.0 }
                }
            };
            var suspiciousNameGuess = new ProofingIssue
            {
                OriginalText = "သမိုင်းဘူတာအနီး",
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = true,
                Message = "လူအမည်/နေရာအမည် သို့မဟုတ် အဘိဓာန်ပြင်ပ စကားလုံး ဖြစ်နိုင်သဖြင့် လူကိုယ်တိုင် စိစစ်ပါ။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "သမိုင်းဘူတာ", Score = 78.0 },
                    new SuggestionCandidate { Replacement = "သမိုင်းအနီး", Score = 72.0 }
                }
            };
            var suspiciousContext = new ProofingIssue
            {
                OriginalText = "ကျရာ",
                IssueType = ProofingIssueType.ContextualMeaningWarning,
                RequiresReviewBeforeBatchFix = true,
                Message = "ရှေ့နောက်စကားအရ အသံတူ အနက်ကွဲ စကားလုံးကို စိစစ်ပါ။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "ကျရာ", Score = 85.0, MeaningCue = "တာဝန်ကျရာ" },
                    new SuggestionCandidate { Replacement = "ကြရာ", Score = 80.0, MeaningCue = "သွားကြရာ" }
                }
            };

            var confirmedSpelling = new ProofingIssue
            {
                OriginalText = "ရောင်းချလျှက်",
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = false,
                Message = "စံသတ်ပုံအရ 'လျှက်' (ဟထိုးပါ) အစား 'လျက်' ဟု ရေးသားရမည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "ရောင်းချလျက်", Score = 100.0 }
                }
            };

            var inputs = new List<ProofingAuditItemInput>
            {
                new ProofingAuditItemInput("Table • Para #39", "E Trade Licencing စနစ်အား အကောင်အထည်ဖေါ် ဆောင်ရွက်လျက်ရှိပြီး...", confirmedLookalike1),
                new ProofingAuditItemInput("Table • Para #42", "လုပ်ငန်းစဉ်များ အကောင်အထည်ဖေါ် ဆောင်ရွက်ရေး...", confirmedLookalike2),
                new ProofingAuditItemInput("Body • Para #67", "လုပ်ငန်းရှင်မှတ်ပုံတင်ထားခြင်း\u200Bအတွက် ကိစ္စရပ်များ...", confirmedZwsp),
                new ProofingAuditItemInput("Body • Para #71", "ဈေးကွက်အတွင်း ရောင်းချလျှက် ရှိသည့် ဆိုင်ကြီးများ...", confirmedSpelling),
                new ProofingAuditItemInput("Body • Para #9", "မြန်မာနိုင်ငံရဲ့ အကြီးဆုံး အွန်လိုင်းဝန်ဆောင်မှု...", confirmedGrammar),
                new ProofingAuditItemInput("Body • Para #3", "သမိုင်းဘူတာအနီး ရုံးချုပ်တည်ရှိပြီး...", suspiciousNameGuess),
                new ProofingAuditItemInput("Body • Para #14", "မိမိတို့ ကျရာ တာဝန်ကို ထမ်းဆောင်ရမည်။", suspiciousContext)
            };

            var sessionFixed = new List<ProofingSessionFixedRecord>
            {
                new ProofingSessionFixedRecord("Body • Para #65", "ပို့\u102F\u102Dကုန်သွင်းကုန်", "ပို့ကုန်သွင်းကုန်", new DateTime(2026, 11, 16, 21, 10, 0)),
                new ProofingSessionFixedRecord("Body • Para #67", "အနာဂါတ်", "အနာဂတ်", new DateTime(2026, 11, 16, 21, 11, 0))
            };

            var rawIssues = inputs.Select(x => x.Issue).ToList();
            string sampleDocText = string.Join("\n", Enumerable.Repeat(
                "ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော် စီးပွားရေးနှင့်ကူးသန်းရောင်းဝယ်ရေးဝန်ကြီးဌာန လုပ်ငန်းစီမံချက် အစီရင်ခံစာ ဖြစ်ပါသည်။",
                12));
            var qReport = DocumentQualityAnalyzer.Analyze(sampleDocText, rawIssues);

            var auditModel = ProofingAuditReportBuilder.Build(
                "ActionPlan_Newform.docx",
                "Microsoft Word",
                ProofingStyleMode.FormalOfficial,
                qReport,
                inputs,
                sessionFixed,
                new DateTime(2026, 11, 16, 21, 15, 0));

            // Verify 4-part bucket counts (1 pure spelling + 3 Unicode/ZWSP/Lookalike + 1 official grammar = 5 confirmed wrong)
            Assert.Equal(5, auditModel.ConfirmedWrongCount);
            Assert.Equal(1, auditModel.ConfirmedSpellingCount);
            Assert.Equal(3, auditModel.ConfirmedEncodingAndLookalikeCount);
            Assert.Equal(1, auditModel.ConfirmedGrammarAndStyleCount);

            Assert.Equal(2, auditModel.SuspiciousReviewCount);
            Assert.Equal(1, auditModel.SuspiciousNameOrOovCount);
            Assert.Equal(1, auditModel.SuspiciousContextMeaningCount);

            Assert.Equal(2, auditModel.SessionFixedCount);
            Assert.True(auditModel.CleanAccuracyPercent > 0 && auditModel.CleanAccuracyPercent < 100.0);

            // Verify Top Repeated Group aggregates duplicate 'အကောင်အထည်ဖေါ်' (×2)
            var repeatedAkaung = Assert.Single(auditModel.TopRepeatedGroups, g => g.RecommendedFix == "အကောင်အထည်ဖော်");
            Assert.Equal(2, repeatedAkaung.Count);
            Assert.True(repeatedAkaung.IsConfirmedWrong);
            Assert.Contains("[ါ➔ာ]", repeatedAkaung.VisualOriginal);

            // Verify ZWSP visual badge in ConfirmedWrongItems
            Assert.Contains(auditModel.ConfirmedWrongItems, r => r.VisualOriginal.Contains("[ZWSP]") && r.RecommendedFix == "မှတ်ပုံတင်ထားခြင်းအတွက်");

            // Verify Suspicious alternatives ("How It Should Be" + alternatives)
            var suspRow = Assert.Single(auditModel.SuspiciousReviewItems, r => r.RawOriginal == "သမိုင်းဘူတာအနီး");
            Assert.Contains("သမိုင်းဘူတာ", suspRow.HowItShouldBeDisplay);
            Assert.Contains("သမိုင်းအနီး", suspRow.HowItShouldBeDisplay);

            // Verify Clipboard summary text
            string clipText = ProofingAuditReportBuilder.BuildClipboardSummaryText(auditModel);
            Assert.Contains("ActionPlan_Newform.docx", clipText);
            Assert.Contains("အကောင်အထည်ဖော်", clipText);
            Assert.Contains("[×2]", clipText);
            Assert.Contains("အနာဂတ်", clipText);

            // Verify Standalone Interactive HTML + Print-to-PDF Report & Disk Save
            string html = ProofingAuditReportBuilder.BuildStandaloneInteractiveHtml(auditModel);
            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("<svg", html);
            Assert.Contains("window.print()", html);
            Assert.Contains("sec-wrong", html);
            Assert.Contains("sec-susp", html);
            Assert.Contains("sec-fixed", html);
            Assert.Contains("[ZWSP]", html);
            Assert.Contains("အကောင်အထည်ဖော်", html);

            string tempDir = Path.Combine(Path.GetTempPath(), "mm_spell_v48_report_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                string savedPath = ProofingAuditReportBuilder.SaveStandaloneInteractiveHtmlToDisk(auditModel, tempDir);
                Assert.True(File.Exists(savedPath));
                Assert.True(new FileInfo(savedPath).Length > 4096);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void V49_ReviewNeededYellowColorIdentityAcrossWordExcelAndPopupsGate()
        {
            // 1. Model-level IsReviewNeeded contract
            var reviewNeededGuess = new ProofingIssue
            {
                OriginalText = "ပါတ်သတ်",
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = true,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "ပတ်သက်", Score = 95.0 },
                    new SuggestionCandidate { Replacement = "ပွတ်သပ်", Score = 88.0 }
                }
            };
            Assert.True(reviewNeededGuess.IsReviewNeeded);
            Assert.False(reviewNeededGuess.IsHighCertaintyPrecheck);

            var contextWarning = new ProofingIssue
            {
                OriginalText = "ကျရာ",
                IssueType = ProofingIssueType.ContextualMeaningWarning,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "ကြရာ", Score = 99.0 }
                }
            };
            Assert.True(contextWarning.IsReviewNeeded);

            var confirmedSpelling = new ProofingIssue
            {
                OriginalText = "အကောင်အထည်ဖေါ်",
                IssueType = ProofingIssueType.LookalikeCharacterError,
                RequiresReviewBeforeBatchFix = false,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "အကောင်အထည်ဖော်", Score = 100.0 }
                }
            };
            Assert.False(confirmedSpelling.IsReviewNeeded);
            Assert.True(confirmedSpelling.IsHighCertaintyPrecheck);

            // 2. Static wiring across Word/Excel underlines, Task Panes, Quick-Fix Popups, and Review & Fix
            string underlineSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "SafeUnderlineManager.cs"));
            string excelWaveSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelWavyUnderlineOverlay.cs"));
            string wordPaneSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.ProofingTab.cs"));
            string excelPaneSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.ProofingTab.cs"));
            string wordReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "FixAllPreviewDialog.cs"));
            string excelReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "FixAllPreviewDialog.cs"));
            string wordPopupSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "MyanmarQuickFixPopup.cs"));
            string excelPopupSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelQuickFixPopup.cs"));

            Assert.Contains("WdColorYellowReviewNeeded", underlineSrc);
            Assert.Contains("issue.IsReviewNeeded", underlineSrc);
            Assert.Contains("penYellow", excelWaveSrc);
            Assert.Contains("item.Issue.IsReviewNeeded", excelWaveSrc);

            // Canary yellow (#FEF9C3 = 254, 249, 195) across cards, popups, and Review & Fix
            Assert.Contains("Color.FromArgb(254, 249, 195)", wordPaneSrc);
            Assert.Contains("Color.FromArgb(254, 249, 195)", excelPaneSrc);
            Assert.Contains("Color.FromArgb(254, 249, 195)", wordReviewSrc);
            Assert.Contains("Color.FromArgb(254, 249, 195)", excelReviewSrc);
            Assert.Contains("Color.FromArgb(254, 249, 195)", wordPopupSrc);
            Assert.Contains("Color.FromArgb(254, 249, 195)", excelPopupSrc);
        }

        [Fact]
        public void V50_AddCustomWordConfirmationAndKnownErrorWarningGate()
        {
            // 1. Known standard MLC spelling error triggers explicit warning & displays standard replacement
            var knownSpellingError = new ProofingIssue
            {
                OriginalText = "တုန့်ပြန်",
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = false,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "တုံ့ပြန်", Score = 100.0 }
                }
            };
            Assert.True(knownSpellingError.CanAddToCustomDictionary);
            string warnMsg = knownSpellingError.BuildAddCustomWordConfirmationMessage(out string warnTitle, out bool isKnownErr);
            Assert.True(isKnownErr);
            Assert.Contains("⚠", warnTitle);
            Assert.Contains("တုန့်ပြန်", warnMsg);
            Assert.Contains("တုံ့ပြန်", warnMsg);

            // 2. Review Needed name/brand uses standard confirmation (isKnownErr = false)
            var brandOrName = new ProofingIssue
            {
                OriginalText = "သမိုင်းဘူတာ",
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = true,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "သမိုင်း", Score = 80.0 }
                }
            };
            Assert.True(brandOrName.CanAddToCustomDictionary);
            string normalMsg = brandOrName.BuildAddCustomWordConfirmationMessage(out string normalTitle, out bool isBrandKnownErr);
            Assert.False(isBrandKnownErr);
            Assert.DoesNotContain("⚠", normalTitle);
            Assert.Contains("သမိုင်းဘူတာ", normalMsg);

            // 3. Corrupted ZWSP / Lookalike / Grammar items cannot be added to Custom Dictionary
            var zwspCorrupted = new ProofingIssue
            {
                OriginalText = "မှတ်ပုံ\u200Bတင်",
                IssueType = ProofingIssueType.SpellingError
            };
            Assert.False(zwspCorrupted.CanAddToCustomDictionary);

            var lookalikeIssue = new ProofingIssue
            {
                OriginalText = "အကောင်အထည်ဖေါ်",
                IssueType = ProofingIssueType.LookalikeCharacterError
            };
            Assert.False(lookalikeIssue.CanAddToCustomDictionary);

            // 4. Static UI Gate: every +Add Word surface requires confirmation with safe default Button2 (No)
            string wordReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "FixAllPreviewDialog.cs"));
            string excelReviewSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "FixAllPreviewDialog.cs"));
            string wordPaneSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.ProofingTab.cs"));
            string wordCtxSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "WordContextMenuHandler.cs"));
            string excelAddInSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "ExcelThisAddIn.cs"));

            foreach (string src in new[] { wordReviewSrc, excelReviewSrc, wordPaneSrc, wordCtxSrc, excelAddInSrc })
            {
                Assert.Contains("BuildAddCustomWordConfirmationMessage", src);
                Assert.Contains("MessageBoxDefaultButton.Button2", src);
            }
        }

        [Fact]
        public void V51B_FriendlyExcelXlsxCsvAndParagraphLineLocationGate()
        {
            // 1. Static verification of Word Paragraph + Line resolution & Landscape Word Report
            string underlineSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "SafeUnderlineManager.cs"));
            string wordToolkitSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "WordDocumentToolkitService.cs"));
            string wordAddInSrc = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "src", "MyanmarSpellChecker.WordAddIn", "ThisAddIn.cs"));

            Assert.Contains("wdActiveEndAdjustedPageNumber", underlineSrc);
            Assert.Contains("wdFirstCharacterLineNumber", underlineSrc);
            Assert.Contains("EnsureFriendlyLocationsResolved", underlineSrc);
            Assert.Contains("FormatFriendlyLocation", underlineSrc);
            Assert.Contains("wdOrientLandscape", wordToolkitSrc);
            Assert.Contains("FriendlyExcelWorkbook", wordAddInSrc);
            Assert.Contains("FriendlyCsvSpreadsheet", wordAddInSrc);

            // 2. Build report with "Page 1 • Paragraph 3, Line 3", Table R4:C1, and multi-line Excel cell
            string para3Text = "အကြောင်းအရာ : Soundit Brand အား စဉ်ဆက်မပျက် အားပေးလျက်ရှိသည့် မိတ်ဆွေများအား ကျေးဇူးတုန့်ပြန်သည့် အနေဖြင့် ကျင်းပသွားမည်။";
            int hitIdx = para3Text.IndexOf("တုန့်ပြန်", StringComparison.Ordinal);
            var confirmedTypo = new ProofingIssue
            {
                OriginalText = "တုန့်ပြန်",
                StartIndex = hitIdx,
                Length = "တုန့်ပြန်".Length,
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = false,
                Message = "သေးသေးတင် အောက်မြစ်ဖြင့် 'တုံ့ပြန်' ဟု ရေးသားရမည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "တုံ့ပြန်", Score = 100.0 }
                }
            };

            string para9Text = "အထက်ပါအကြောင်းအရာကိစ္စနှင့်ပါတ်သတ်၍ City Mall Online သည် အကြီးဆုံး ဝန်ဆောင်မှုဖြစ်ပါသည်။";
            int suspIdx = para9Text.IndexOf("ပါတ်သတ်", StringComparison.Ordinal);
            var reviewItem = new ProofingIssue
            {
                OriginalText = "ပါတ်သတ်",
                StartIndex = suspIdx,
                Length = "ပါတ်သတ်".Length,
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = true,
                Message = "'ပါတ်သတ်' အစား 'ပတ်သက်' ဟု ရေးသားရမည်။",
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "ပတ်သက်", Score = 94.0 },
                    new SuggestionCandidate { Replacement = "ပွတ်သပ်", Score = 80.0 }
                }
            };

            var inputs = new List<ProofingAuditItemInput>
            {
                new ProofingAuditItemInput(
                    "Page 1 • Paragraph 3, Line 3",
                    para3Text,
                    confirmedTypo,
                    "တုံ့ပြန်",
                    "Page 1",
                    "Paragraph 3",
                    "Line 3"),
                new ProofingAuditItemInput(
                    "Page 2 • Table (R4, C1) • Para 9, Line 2",
                    para9Text,
                    reviewItem,
                    "ပတ်သက်")
            };

            var qReport = DocumentQualityAnalyzer.Analyze(para3Text + "\n" + para9Text, inputs.Select(x => x.Issue).ToList());
            var model = ProofingAuditReportBuilder.Build(
                "Campaign_Approval.docx",
                "Microsoft Word",
                ProofingStyleMode.FormalOfficial,
                qReport,
                inputs,
                new List<ProofingSessionFixedRecord>
                {
                    new ProofingSessionFixedRecord("Page 1 • Paragraph 2, Line 1", "အနာဂါတ်", "အနာဂတ်", new DateTime(2026, 11, 16, 22, 0, 0))
                },
                new DateTime(2026, 11, 16, 22, 30, 0));

            Assert.Equal(2, model.AllItemsInDocumentOrder.Count);
            Assert.Equal("Page 1", model.AllItemsInDocumentOrder[0].PageOrSheetLabel);
            Assert.Equal("Paragraph 3", model.AllItemsInDocumentOrder[0].ParagraphOrCellLabel);
            Assert.Equal("Line 3", model.AllItemsInDocumentOrder[0].LineLabel);
            Assert.Contains("〖တုန့်ပြန်〗", model.AllItemsInDocumentOrder[0].MarkedContextSnippet);

            Assert.Equal("Page 2", model.AllItemsInDocumentOrder[1].PageOrSheetLabel);
            Assert.Equal("Table (R4, C1) • Para 9", model.AllItemsInDocumentOrder[1].ParagraphOrCellLabel);
            Assert.Equal("Line 2", model.AllItemsInDocumentOrder[1].LineLabel);
            Assert.Contains("〖ပါတ်သတ်〗", model.AllItemsInDocumentOrder[1].MarkedContextSnippet);

            // 3. Verify OpenXML .xlsx generation, schema order (<autoFilter> before <mergeCells>), and UTF-8 BOM .csv
            string tempDir = Path.Combine(Path.GetTempPath(), "mm_spell_v51_xlsx_csv_" + Guid.NewGuid().ToString("N"));
            try
            {
                string xlsxPath = ProofingAuditSpreadsheetExporter.SaveFriendlyXlsxToDisk(model, tempDir);
                Assert.True(File.Exists(xlsxPath));
                Assert.True(new FileInfo(xlsxPath).Length > 2048);

                using (var fs = File.OpenRead(xlsxPath))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                    Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
                    Assert.NotNull(zip.GetEntry("xl/styles.xml"));
                    var sheet1Entry = zip.GetEntry("xl/worksheets/sheet1.xml");
                    Assert.NotNull(sheet1Entry);
                    using (var sr = new StreamReader(sheet1Entry!.Open(), System.Text.Encoding.UTF8))
                    {
                        string sheet1Xml = sr.ReadToEnd();
                        Assert.Contains("Paragraph 3", sheet1Xml);
                        Assert.Contains("Line 3", sheet1Xml);
                        Assert.Contains("〖တုန့်ပြန်〗", sheet1Xml);
                        Assert.Contains("〖ပါတ်သတ်〗", sheet1Xml);
                        int afIdx = sheet1Xml.IndexOf("<autoFilter", StringComparison.Ordinal);
                        int mcIdx = sheet1Xml.IndexOf("<mergeCells", StringComparison.Ordinal);
                        Assert.True(afIdx > 0 && mcIdx > afIdx, "OpenXML CT_Worksheet requires <autoFilter> before <mergeCells>.");
                    }
                }

                string csvPath = ProofingAuditReportBuilder.SaveFriendlyCsvToDisk(model, tempDir);
                Assert.True(File.Exists(csvPath));
                byte[] csvBytes = File.ReadAllBytes(csvPath);
                Assert.True(csvBytes.Length > 200);
                // Verify UTF-8 BOM (EF BB BF) for Excel Burmese Unicode compatibility
                Assert.Equal(0xEF, csvBytes[0]);
                Assert.Equal(0xBB, csvBytes[1]);
                Assert.Equal(0xBF, csvBytes[2]);
                string csvText = File.ReadAllText(csvPath, System.Text.Encoding.UTF8);
                Assert.Contains("Paragraph 3", csvText);
                Assert.Contains("Line 3", csvText);
                Assert.Contains("〖တုန့်ပြန်〗", csvText);

                string html = ProofingAuditReportBuilder.BuildStandaloneInteractiveHtml(model);
                Assert.Contains("sec-friendly-all", html);
                Assert.Contains("Paragraph 3", html);
                Assert.Contains("Line 3", html);
                Assert.Contains("<mark", html);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void V52B_SlimLaptopChrome_AdjustableDockedCards_Splitter_AndAukmyitDiffGate()
        {
            // 1. Aukmyit-before-lower-vowel (\u1037\u102F -> \u102F\u1037) visual diff badge (Screenshot 4: ပို့ကုန်သွင်းကုန်)
            string wrongAukmyit = "ပ\u102D\u1037\u102Fကုန်သွင်းကုန်";
            string canonicalAukmyit = "ပ\u102D\u102F\u1037ကုန်သွင်းကုန်";
            var markOrderIssue = new ProofingIssue
            {
                OriginalText = wrongAukmyit,
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = false,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = canonicalAukmyit, Score = 99.0 }
                }
            };
            string visMarkOrder = markOrderIssue.FormatVisualOriginalText(canonicalAukmyit);
            Assert.Contains("့ု➔ု့", visMarkOrder);

            // Duplicate combining mark visual diff badge
            string dupMarkOrig = "ပို့\u1037ကုန်";
            var dupMarkIssue = new ProofingIssue
            {
                OriginalText = dupMarkOrig,
                IssueType = ProofingIssueType.SpellingError,
                RequiresReviewBeforeBatchFix = false,
                Suggestions = new List<SuggestionCandidate>
                {
                    new SuggestionCandidate { Replacement = "ပို့ကုန်", Score = 99.0 }
                }
            };
            string visDup = dupMarkIssue.FormatVisualOriginalText(canonicalAukmyit);
            Assert.NotEqual(dupMarkOrig, visDup);
            Assert.Contains("[", visDup);

            // 2. ReviewWindowPreferencesStore round-trip for DetailSplitRatio + SlimTaskPaneHeader
            string testHostTag = "v52test_" + Guid.NewGuid().ToString("N");
            string prefsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyanmarSpellChecker",
                $"review_window_{testHostTag}.prefs");
            try
            {
                var prefs = new MyanmarSpellChecker.Core.Performance.ReviewWindowPreferences
                {
                    IsPinnedTopMost = false,
                    IsGroupedByWord = true,
                    PreferDockedInTaskPane = true,
                    DetailSplitRatio = 0.365,
                    SlimTaskPaneHeader = true,
                    HasSlimHeaderPreference = true,
                    HasSavedBounds = true,
                    X = 40,
                    Y = 50,
                    Width = 680,
                    Height = 490
                };
                MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Save(testHostTag, prefs);

                var loaded = MyanmarSpellChecker.Core.Performance.ReviewWindowPreferencesStore.Load(testHostTag);
                Assert.False(loaded.IsPinnedTopMost);
                Assert.True(loaded.IsGroupedByWord);
                Assert.True(loaded.PreferDockedInTaskPane);
                Assert.InRange(loaded.DetailSplitRatio, 0.36, 0.37);
                Assert.True(loaded.SlimTaskPaneHeader);
                Assert.True(loaded.HasSlimHeaderPreference);
            }
            finally
            {
                try { if (File.Exists(prefsFile)) File.Delete(prefsFile); } catch { }
            }

            // 3. Static architecture gates across Word & Excel Task Panes, Docked Studios, and Resizable Splitters
            string repoRoot = TestPaths.RepoRoot;
            string wordPane = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.cs"));
            string wordPaneTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.ProofingTab.cs"));
            string excelPane = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.cs"));
            string excelPaneTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.ProofingTab.cs"));
            string wordDialog = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "FixAllPreviewDialog.cs"));
            string excelDialog = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "FixAllPreviewDialog.cs"));
            string wordAddIn = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "ThisAddIn.cs"));
            string excelAddIn = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "ExcelThisAddIn.cs"));

            Assert.Contains("_pnlStickyDockedBar", wordPane);
            Assert.Contains("_btnSlimHeader", wordPane);
            Assert.Contains("_pnlStickyDockedBar", excelPane);
            Assert.Contains("_btnSlimHeader", excelPane);

            Assert.Contains("_expandedDockedItems", wordPaneTab);
            Assert.Contains("Adjust", wordPaneTab);
            Assert.Contains("_expandedDockedItems", excelPaneTab);
            Assert.Contains("Adjust", excelPaneTab);

            Assert.Contains("_splitterBar", wordDialog);
            Assert.Contains("Cursors.HSplit", wordDialog);
            Assert.Contains("_userDetailSplitRatio", wordDialog);
            Assert.Contains("_splitterBar", excelDialog);
            Assert.Contains("Cursors.HSplit", excelDialog);
            Assert.Contains("_userDetailSplitRatio", excelDialog);

            Assert.DoesNotContain("EnforceActiveWindowStartupPaneWidth(forceIfNarrow: true)", wordAddIn);
            Assert.DoesNotContain("EnforceActiveWindowStartupPaneWidth(forceIfNarrow: true)", excelAddIn);
        }

        [Fact]
        public void V53B_SmallScreenScrollableDialogsAndPopups_Gate()
        {
            string repoRoot = TestPaths.RepoRoot;
            string wordAbout = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "LicenseActivationForm.cs"));
            string excelAbout = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "LicenseActivationForm.cs"));
            string wordPopup = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "MyanmarQuickFixPopup.cs"));
            string excelPopup = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelQuickFixPopup.cs"));

            foreach (string aboutSrc in new[] { wordAbout, excelAbout })
            {
                Assert.Contains("AutoScroll = true", aboutSrc);
                Assert.Contains("FormBorderStyle.Sizable", aboutSrc);
                Assert.Contains("DockStyle.Bottom", aboutSrc);
                Assert.DoesNotContain("FormBorderStyle.FixedDialog", aboutSrc);
                // Mandatory GPLv3 Section 7(b) attribution preservation
                Assert.Contains("Htet Yar Zar (ထက်ရာဇာ)", aboutSrc);
                Assert.Contains("Kyaukse University", aboutSrc);
                Assert.Contains("U Moe Kyaw Thu", aboutSrc);
                Assert.Contains("Hein Htet Zaw", aboutSrc);
                Assert.Contains("Thu Reain Khant", aboutSrc);
            }

            foreach (string popupSrc in new[] { wordPopup, excelPopup })
            {
                Assert.Contains("AutoScroll = true", popupSrc);
                Assert.Contains("DockStyle.Bottom", popupSrc);
                Assert.Contains("_scrollHost", popupSrc);
            }
        }

        [Fact]
        public void V54_FastWindowedWordBlockScanner_EcoDropdownClarity_AndNoPrematurePassedGate()
        {
            Assert.Equal(25_000, MyanmarSpellChecker.Core.Performance.EcoPerformanceGovernor.DefaultWordTrueCountIssueCap);

            var prevLang = MyanmarSpellChecker.Core.Localization.UiLocalizer.CurrentLanguage;
            try
            {
                MyanmarSpellChecker.Core.Localization.UiLocalizer.CurrentLanguage = MyanmarSpellChecker.Core.Localization.UiLanguage.English;
                Assert.Equal("● Eco: On ▾", MyanmarSpellChecker.Core.Performance.EcoPerformanceGovernor.FormatBadgeLabel(
                    MyanmarSpellChecker.Core.Performance.EcoModePreference.AlwaysOn, true));
                Assert.Equal("● Eco: Auto✓ ▾", MyanmarSpellChecker.Core.Performance.EcoPerformanceGovernor.FormatBadgeLabel(
                    MyanmarSpellChecker.Core.Performance.EcoModePreference.Auto, true));
                Assert.Equal("◐ Eco: Auto ▾", MyanmarSpellChecker.Core.Performance.EcoPerformanceGovernor.FormatBadgeLabel(
                    MyanmarSpellChecker.Core.Performance.EcoModePreference.Auto, false));
                Assert.Equal("○ Eco: Off ▾", MyanmarSpellChecker.Core.Performance.EcoPerformanceGovernor.FormatBadgeLabel(
                    MyanmarSpellChecker.Core.Performance.EcoModePreference.Off, false));
            }
            finally
            {
                MyanmarSpellChecker.Core.Localization.UiLocalizer.CurrentLanguage = prevLang;
            }

            string repoRoot = TestPaths.RepoRoot;
            string scannerSrc = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "StaDocumentScanner.cs"));
            string wordPane = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.cs"));
            string wordPaneTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.ProofingTab.cs"));
            string excelPane = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.cs"));
            string excelPaneTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.ProofingTab.cs"));

            // 1. O(N²) Paragraphs[pIndex] whole-story indexer eliminated in favor of 4KB Windowed Block Scanner + Instant Viewport Preview
            Assert.Contains("BlockWindowChars = 4096", scannerSrc);
            Assert.Contains("CurrentBlockCursorOffset", scannerSrc);
            Assert.Contains("TryEmitInstantVisiblePagePreview", scannerSrc);
            Assert.Contains("ScanViewportPreviewReady", scannerSrc);
            Assert.Contains("DefaultWordTrueCountIssueCap", scannerSrc);
            Assert.DoesNotContain("job.CurrentParagraphs = job.CurrentStoryRange.Paragraphs", scannerSrc);

            // 2. 3-choice Eco Mode dropdown + honest Scanning-in-Progress empty-state card
            Assert.Contains("ShowEcoModeDropdownMenu", wordPane);
            Assert.Contains("RequestSetEcoMode", wordPane);
            Assert.Contains("RenderViewportPreviewDuringScan", wordPane);
            Assert.Contains("Scanning Document in Progress", wordPaneTab);

            Assert.Contains("ShowExcelEcoModeDropdownMenu", excelPane);
            Assert.Contains("RequestSetEcoMode", excelPane);
            Assert.Contains("Scanning Spreadsheet Cells", excelPaneTab);
        }

        [Fact]
        public void V55_HostSmartZwspEngine_WordBoundaryWrap_ExcelStrictVlookup_AndToolkitGate()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var engine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Word Host-Smart ZWSP Rules:
            //    (a) Broken syllable (consonant + ZWSP + medial/vowel -> dotted circle) MUST be flagged
            var brokenIssues = engine.CheckText("က\u200Bြီးမားသော လုပ်ငန်းဖြစ်သည်။");
            Assert.Contains(brokenIssues, x => x.OriginalText.Contains("က\u200Bြီး") && !x.RequiresReviewBeforeBatchFix);

            //    (b) Mid-word ZWSP splitting a dictionary word or detaching a bound particle MUST be flagged as High-Certainty
            var midWordIssues = engine.CheckText("မှတ်ပုံ\u200Bတင် စာရင်းကို စစ်ဆေးသည်။");
            Assert.Contains(midWordIssues, x =>
                x.OriginalText == "မှတ်ပုံ\u200Bတင်" &&
                !x.RequiresReviewBeforeBatchFix &&
                x.Suggestions.Any(s => s.Replacement == "မှတ်ပုံတင်"));

            var orphanParticleIssues = engine.CheckText("စာအုပ်\u200Bကို ဖတ်ရှုသည်။");
            Assert.Contains(orphanParticleIssues, x =>
                x.OriginalText == "စာအုပ်\u200Bကို" &&
                !x.RequiresReviewBeforeBatchFix &&
                x.Suggestions.Any(s => s.Replacement == "စာအုပ်ကို"));

            //    (c) Intentional clean word/phrase-boundary ZWSP in Word tables/justified text MUST be allowed (0 false positives!)
            Assert.Empty(engine.CheckText("ဝန်ကြီးဌာန\u200Bဆောင်ရွက်ချက်များ တိုးတက်သည်။"));
            Assert.Empty(engine.CheckText("မှတ်ပုံတင်ထားခြင်း\u200Bအတွက် ကျေးဇူးတင်ပါသည်။"));

            // 2. Excel Strict VLOOKUP / PivotTable ZWSP Protection:
            //    Flags trailing ZWSP ("ရန်ကုန်\u200B"), boundary ZWSP ("ဝန်ကြီးဌာန\u200Bဆောင်ရွက်ချက်"), and web-pasted ZWSP,
            //    while keeping =VLOOKUP(...) formula cells 100% untouched!
            object?[,] valBlock = new object?[4, 1];
            object?[,] fmlBlock = new object?[4, 1];
            valBlock[0, 0] = "ရန်ကုန်\u200B";
            fmlBlock[0, 0] = "ရန်ကုန်\u200B";
            valBlock[1, 0] = "ဝန်ကြီးဌာန\u200Bဆောင်ရွက်ချက်";
            fmlBlock[1, 0] = "ဝန်ကြီးဌာန\u200Bဆောင်ရွက်ချက်";
            valBlock[2, 0] = "လူ\u200Bသား\u200Bများ\u200Bသည်\u200B";
            fmlBlock[2, 0] = "လူ\u200Bသား\u200Bများ\u200Bသည်\u200B";
            valBlock[3, 0] = "ရန်ကုန်\u200B";
            fmlBlock[3, 0] = "=VLOOKUP(A1,B1:C10,2,FALSE)";

            var excelCellIssues = MyanmarSpellChecker.Core.Spreadsheet.SpreadsheetProofingHelper.ScanBlockValues(
                engine, "Test.xlsx", "Sheet1", 1, 1, 4, 1, valBlock, fmlBlock, blockHasFormula: null);

            Assert.Equal(3, excelCellIssues.Count);
            Assert.All(excelCellIssues, ci => Assert.False(ci.Issue.RequiresReviewBeforeBatchFix));
            Assert.Contains(excelCellIssues, ci => ci.Row == 1 && ci.Issue.Suggestions[0].Replacement == "ရန်ကုန်");
            Assert.Contains(excelCellIssues, ci => ci.Row == 2 && ci.Issue.Suggestions[0].Replacement == "ဝန်ကြီးဌာနဆောင်ရွက်ချက်");
            Assert.Contains(excelCellIssues, ci => ci.Row == 3 && ci.Issue.Suggestions[0].Replacement == "လူသားများသည်");
            Assert.DoesNotContain(excelCellIssues, ci => ci.Row == 4); // Formula cell untouched!

            //    TransformBlockStripAllZwsp also strips ZWSP from English/ID lookup keys ("EMP-102\u200B") while skipping formulas
            valBlock[0, 0] = "EMP-102\u200B";
            fmlBlock[0, 0] = "EMP-102\u200B";
            var stripOps = MyanmarSpellChecker.Core.Spreadsheet.SpreadsheetProofingHelper.TransformBlockStripAllZwsp(
                "Test.xlsx", "Sheet1", 1, 1, 4, 1, valBlock, fmlBlock, blockHasFormula: null);
            Assert.Equal(3, stripOps.Count);
            Assert.Contains(stripOps, op => op.Row == 1 && op.UpdatedText == "EMP-102");
            Assert.DoesNotContain(stripOps, op => op.Row == 4);

            // 3. Toolkit Smart Word-Wrap ZWSP + Strip Round-Trip + Quality Analyzer Diagnostics:
            string wrapped = MyanmarZwspToolkit.InsertSmartWordBoundaryZwsp(
                "ဝန်ကြီးဌာနဆောင်ရွက်ချက်များ တိုးတက်သည်။",
                trie,
                out int insertedWrap,
                out _);
            Assert.True(insertedWrap >= 1);
            Assert.Contains("ဝန်ကြီးဌာန\u200Bဆောင်ရွက်ချက်", wrapped);
            // Bound plural particle 'များ' and verb marker 'သည်' must NEVER be detached by smart word-wrap!
            Assert.DoesNotContain("\u200Bများ", wrapped);
            Assert.DoesNotContain("\u200Bသည်", wrapped);
            Assert.Empty(engine.CheckText(wrapped));

            DocumentQualityReport qReport = DocumentQualityAnalyzer.Analyze(wrapped + " မှတ်ပုံ\u200Bတင်", null, engine);
            Assert.True(qReport.HiddenZeroWidthCount >= 2);
            Assert.Equal(1, qReport.BadZeroWidthCount);
            Assert.True(qReport.CleanBoundaryZeroWidthCount >= 1);

            string strippedClean = MyanmarZwspToolkit.StripAllZeroWidthCharacters(wrapped, out int removedWrap);
            Assert.Equal(insertedWrap, removedWrap);
            Assert.Equal("ဝန်ကြီးဌာနဆောင်ရွက်ချက်များ တိုးတက်သည်။", strippedClean);

            // 4. Word & Excel UI Banner + Toolkit Studio Wiring Gate
            string repoRoot = TestPaths.RepoRoot;
            string wordProofTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.ProofingTab.cs"));
            string wordToolkitTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "UI", "ProofingTaskPaneControl.ToolkitTab.cs"));
            string excelProofTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.ProofingTab.cs"));
            string excelToolkitTab = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.ExcelAddIn", "UI", "ExcelProofingTaskPaneControl.ToolkitTab.cs"));

            Assert.Contains("BuildHiddenZwspBannerCard", wordProofTab);
            Assert.Contains("RequestStripAllZwsp", wordToolkitTab);
            Assert.Contains("RequestInsertSmartWordWrapZwsp", wordToolkitTab);
            Assert.Contains("BuildExcelHiddenZwspBannerCard", excelProofTab);
            Assert.Contains("BuildExcelZwspStudioCard", excelToolkitTab);
        }

        [Fact]
        public void V56_VerbalNegation_DroppedMa_NegativePhu_AndOfficialColloquialRegister_Gate()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Exact user screenshot lines ("နေကောင်းဘူး" & "မအားနေဖူး") in FormalOfficial mode:
            var line1Formal = formalEngine.CheckText("နေကောင်းဘူး");
            var l1f = Assert.Single(line1Formal);
            Assert.Equal("နေကောင်းဘူး", l1f.OriginalText);
            Assert.False(l1f.RequiresReviewBeforeBatchFix);
            Assert.Equal("နေမကောင်းပါ", l1f.Suggestions[0].Replacement);
            Assert.Contains(l1f.Suggestions, s => s.Replacement == "နေမကောင်းဘူး");

            var line2Formal = formalEngine.CheckText("မအားနေဖူး");
            var l2f = Assert.Single(line2Formal);
            Assert.Equal("မအားနေဖူး", l2f.OriginalText);
            Assert.Equal(ProofingIssueType.SpellingError, l2f.IssueType);
            Assert.False(l2f.RequiresReviewBeforeBatchFix);
            Assert.Equal("မအားလပ်ပါ", l2f.Suggestions[0].Replacement);
            Assert.Contains(l2f.Suggestions, s => s.Replacement == "မအားပါ");
            Assert.Contains(l2f.Suggestions, s => s.Replacement == "မအားနေဘူး");

            // 2. Exact user screenshot lines in GeneralStandard mode:
            var line1Gen = generalEngine.CheckText("နေကောင်းဘူး");
            var l1g = Assert.Single(line1Gen);
            Assert.Equal("နေကောင်းဘူး", l1g.OriginalText);
            Assert.Equal("နေမကောင်းဘူး", l1g.Suggestions[0].Replacement);

            var line2Gen = generalEngine.CheckText("မအားနေဖူး");
            var l2g = Assert.Single(line2Gen);
            Assert.Equal("ဖူး", l2g.OriginalText);
            Assert.Equal(ProofingIssueType.SpellingError, l2g.IssueType);
            Assert.Equal("ဘူး", l2g.Suggestions[0].Replacement);

            // 3. Broadened bipartite compound & simple dropped-'မ' + misspelled 'ဖူး/ဘူ' across Myanmar verbs:
            Assert.Equal("အဆင်မပြေပါ", formalEngine.CheckText("အဆင်ပြေဘူး")[0].Suggestions[0].Replacement);
            Assert.Equal("အဆင်မပြေဘူး", generalEngine.CheckText("အဆင်ပြေဘူး")[0].Suggestions[0].Replacement);
            Assert.Equal("နားမလည်ဘူး", generalEngine.CheckText("နားလည်ဖူး")[0].Suggestions[0].Replacement);
            Assert.Equal("မအားဘူး", generalEngine.CheckText("အားဘူး")[0].Suggestions[0].Replacement);
            Assert.Equal("မပြီးသေးဘူး", generalEngine.CheckText("ပြီးသေးဘူး")[0].Suggestions[0].Replacement);
            Assert.Contains(generalEngine.CheckText("နေမကောင်းဖူး"), x => x.OriginalText == "ဖူး" && x.Suggestions[0].Replacement == "ဘူး");
            Assert.Contains(generalEngine.CheckText("မအားဖူး"), x => x.OriginalText == "ဖူး" && x.Suggestions[0].Replacement == "ဘူး");
            Assert.Contains(generalEngine.CheckText("မဖစ်ဘူး"), x => x.OriginalText == "ဖစ်" && x.Suggestions[0].Replacement == "ဖြစ်");

            // 4. FormalOfficial mode flags general colloquial negatives & questions while GeneralStandard allows them cleanly:
            Assert.Contains(formalEngine.CheckText("နေမကောင်းဘူး။"), x => x.Suggestions[0].Replacement == "နေမကောင်းပါ");
            Assert.Contains(formalEngine.CheckText("မအားဘူး။"), x => x.Suggestions[0].Replacement == "မအားပါ");
            Assert.Contains(formalEngine.CheckText("နေကောင်းလား"), x => x.Suggestions[0].Replacement == "နေကောင်းပါသလော");
            Assert.Contains(formalEngine.CheckText("အဆင်ပြေပါသလား။"), x => x.Suggestions[0].Replacement == "ပါသလော");
            Assert.Contains(formalEngine.CheckText("ဘယ်လို ဆောင်ရွက်ရမလဲ။"), x => x.OriginalText == "ဘယ်လို" && x.Suggestions[0].Replacement == "မည်သို့");
            Assert.Contains(formalEngine.CheckText("ဘယ်လို ဆောင်ရွက်ရမလဲ။"), x => x.OriginalText == "ရမလဲ" && x.Suggestions[0].Replacement == "ရမည်နည်း");

            // 5. Over-split hidden compound typos, aspect vowel confusion & unspaced locative suffix typos (benchmark parity):
            Assert.Contains(generalEngine.CheckText("စီမံကိန်း ခုန်ကျစရိတ် တွက်ချက်သည်။"), x => x.OriginalText == "ခုန်ကျ" && x.Suggestions[0].Replacement == "ကုန်ကျ");
            Assert.Contains(generalEngine.CheckText("ကျွန်တော် မနက်ဖြန် ဆာမေးပွဲ ဖြေရမယ်။"), x => x.OriginalText == "ဆာမေးပွဲ" && x.Suggestions[0].Replacement == "စာမေးပွဲ");
            Assert.Contains(generalEngine.CheckText("မင်းဘာလုပ်နီလဲ"), x => x.OriginalText == "နီ" && x.Suggestions[0].Replacement == "နေ");
            Assert.Contains(generalEngine.CheckText("ရန်ကုန်မာ နေပါတယ်။"), x => x.OriginalText == "မာ" && x.Suggestions[0].Replacement == "မှာ");
            Assert.Contains(formalEngine.CheckText("ရန်ကုန်မာ နေပါသည်။"), x => x.OriginalText == "မာ" && x.Suggestions[0].Replacement == "တွင်");

            Assert.Empty(generalEngine.CheckText("နေမကောင်းဘူး။"));
            Assert.Empty(generalEngine.CheckText("မအားဘူး။"));
            Assert.Empty(generalEngine.CheckText("နေကောင်းလား"));
            Assert.Empty(generalEngine.CheckText("ရေဘူးနှင့် ထမင်းဘူး ယူလာပါသည်။"));
            Assert.Empty(formalEngine.CheckText("ရေဘူးနှင့် ထမင်းဘူး ယူလာပါသည်။"));
            Assert.Empty(formalEngine.CheckText("မဖူးသည် ဘုရားဖူး သွားရောက်ပါသည်။"));
            Assert.Empty(formalEngine.CheckText("တစ်ကြိမ်မျှ မကြုံဖူးပါ။"));
        }

        [Fact]
        public void V57_FourPillarUpgrade_GeneralizedTrieHiddenCompounds_ModeAwareAttachedParticles_AwVowelTone_AndWordSquiggleShield_Gate()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // Pillar 1 & 3A: Generalized Trie Hidden Over-Split Compound & Dropped-Tone Scanner (Tier 1 Red Sure ☑)
            var t1Cases = new (string Input, string ExpectedWrong, string ExpectedTop)[]
            {
                ("ပညာရေ တိုးတက်သည်။", "ပညာရေ", "ပညာရေး"),
                ("ကျန်းမာရေ စောင့်ရှောက်မှု ပေးသည်။", "ကျန်းမာရေ", "ကျန်းမာရေး"),
                ("အစည်းအဝေ ကျင်းပသည်။", "အစည်းအဝေ", "အစည်းအဝေး"),
                ("ကော်မတီ အဖွဲ ဖွဲ့စည်းသည်။", "အဖွဲ", "အဖွဲ့"),
                ("ခန်မှန်း တွက်ချက်သည်။", "ခန်မှန်း", "ခန့်မှန်း"),
                ("ကန်သတ် ထားရှိသည်။", "ကန်သတ်", "ကန့်သတ်"),
                ("သတင်း အချက်အလတ် စုဆောင်းသည်။", "အချက်အလတ်", "အချက်အလက်"),
                ("ရှေ့ လုပ်ငန်းစင် ချမှတ်သည်။", "လုပ်ငန်းစင်", "လုပ်ငန်းစဉ်"),
                ("လုပ်ငန်း ဆောင်ရွက်ချတ် တင်ပြသည်။", "ဆောင်ရွက်ချတ်", "ဆောင်ရွက်ချက်")
            };
            foreach (var tc in t1Cases)
            {
                string canonExpected = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedTop);
                var iss = formalEngine.CheckText(tc.Input);
                Assert.True(iss.Any(x =>
                        !x.RequiresReviewBeforeBatchFix &&
                        x.Suggestions.Any(s => UnicodeNormalizer.NormalizeCanonicalOrder(s.Replacement).Contains(canonExpected))),
                    $"Expected '{tc.ExpectedWrong}' -> '{tc.ExpectedTop}' in '{tc.Input}', got [{string.Join("; ", iss.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // Guard literal water & rice-bran contexts (0 false positives!)
            Assert.Empty(formalEngine.CheckText("သောက်ရေနှင့် မြစ်ရေ သန့်ရှင်းသည်။"));

            // Pillar 2A: Mode-Aware Unspaced Attached Particle Typos (Official -> formal #1; General -> spoken #1)
            var marFormal = formalEngine.CheckText("ကုမ္ပဏီမာ အလုပ်လုပ်ပါသည်။");
            var mf = Assert.Single(marFormal, x => x.OriginalText == "မာ");
            Assert.Equal("တွင်", mf.Suggestions[0].Replacement);
            Assert.Contains(mf.Suggestions, s => s.Replacement == "မှာ");

            var marGeneral = generalEngine.CheckText("ကုမ္ပဏီမာ အလုပ်လုပ်ပါတယ်။");
            var mg = Assert.Single(marGeneral, x => x.OriginalText == "မာ");
            Assert.Equal("မှာ", mg.Suggestions[0].Replacement);
            Assert.Contains(mg.Suggestions, s => s.Replacement == "တွင်");

            var taeFormal = formalEngine.CheckText("ပြောတဲ စကားကို နားထောင်ပါသည်။");
            Assert.Equal("သည့်", Assert.Single(taeFormal, x => x.OriginalText == "တဲ").Suggestions[0].Replacement);

            var taeGeneral = generalEngine.CheckText("ပြောတဲ စကားကို နားထောင်ပါတယ်။");
            Assert.Equal("တဲ့", Assert.Single(taeGeneral, x => x.OriginalText == "တဲ").Suggestions[0].Replacement);

            var naeFormal = formalEngine.CheckText("သူငယ်ချင်းနဲ အတူ သွားပါသည်။");
            Assert.Equal("နှင့်", Assert.Single(naeFormal, x => x.OriginalText == "နဲ").Suggestions[0].Replacement);

            var naeGeneral = generalEngine.CheckText("သူငယ်ချင်းနဲ အတူ သွားပါတယ်။");
            Assert.Equal("နဲ့", Assert.Single(naeGeneral, x => x.OriginalText == "နဲ").Suggestions[0].Replacement);

            // Pillar 2B: MergedWordChecker (Yellow Review Needed ☐)
            var mergedKaSar = formalEngine.CheckText("ထမင်းကို သူကစားသည်။");
            var mks = Assert.Single(mergedKaSar, x => x.OriginalText == "ကစား");
            Assert.Equal(ProofingIssueType.ContextualMeaningWarning, mks.IssueType);
            Assert.True(mks.RequiresReviewBeforeBatchFix);
            Assert.Equal("က စား", mks.Suggestions[0].Replacement);

            var mergedKaPya = formalEngine.CheckText("ဆရာကပြသည့် နည်းလမ်းအတိုင်း ဆောင်ရွက်သည်။");
            var mkp = Assert.Single(mergedKaPya, x => x.OriginalText == "ကပြ");
            Assert.True(mkp.RequiresReviewBeforeBatchFix);
            Assert.Equal("က ပြ", mkp.Suggestions[0].Replacement);

            // Legitimate 'ကစား' (play) and 'ကပြ' (dance) produce ZERO false positives!
            Assert.Empty(formalEngine.CheckText("ကလေးများ ဘောလုံး ကစားကြသည်။"));
            Assert.Empty(formalEngine.CheckText("ဇာတ်သဘင် အဖွဲ့က ကပြဖျော်ဖြေသည်။"));

            // Pillar 3B: Deterministic Aw-Vowel (-ော် <-> -ေါ်) Orthography
            Assert.Contains(formalEngine.CheckText("ခော်ယူ မေးမြန်းသည်။"), x => x.Suggestions[0].Replacement == "ခေါ်ယူ");
            Assert.Contains(formalEngine.CheckText("ဖေါ်ပြ ထားပါသည်။"), x => x.Suggestions[0].Replacement == "ဖော်ပြ");
            Assert.Contains(formalEngine.CheckText("ပျေါ်ရွှင်စွာ နေထိုင်သည်။"), x => x.Suggestions[0].Replacement.Contains("ပျော်"));

            // Pillar 4: Word Pure-Myanmar Built-in Fake English Red-Squiggle Shield Wiring Gate
            string repoRoot = TestPaths.RepoRoot;
            string scannerSrc = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "StaDocumentScanner.cs"));
            string underlineSrc = File.ReadAllText(Path.Combine(repoRoot, "src", "MyanmarSpellChecker.WordAddIn", "Interop", "SafeUnderlineManager.cs"));
            Assert.Contains("SuppressWordBuiltInEnglishSquigglesOnPureMyanmarSpans", scannerSrc);
            Assert.Contains("ClassifyMyanmarAndLatinPresence", scannerSrc);
            Assert.Contains("SuppressBuiltInEnglishSquigglesOnRangeOnce", underlineSrc);
        }

        [Fact]
        public void V58_ComprehensiveAccuracyLatencyAndCleanFprBenchmark_Gate()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // Warmup
            _ = formalEngine.CheckText("မြန်မာနိုင်ငံသည် သာယာလှပသော နိုင်ငံတစ်ခု ဖြစ်ပါသည်။");
            _ = generalEngine.CheckText("လာမ ည့် အပတ်တွင် ဃီတပွဲ ကျင်းပမည်။");

            // 1. Clean False-Positive Resistance (Nested Curly Quotes, 'ထောင့်ကျသော', Shan/Kayah 'လွိုင်', Foreign Coda Names, Technical Loanwords)
            string[] cleanControls =
            {
                "“ယခု အမိန့်တော်သည် သင့်ပေ၏။ “လိုက်၍ ပြချေ” ဆိုလျှင် ကျွန်ုပ် သွားပါအံ့” ဟု လျှောက်တင်၏။",
                "အိမ်တွင် ဘုရားစင် မရှိသဖြင့် ထောင့်ကျသော နေရာတွင် နံရံကို မျက်နှာပြု၍ ထားရှိသည်။",
                "နောက်ရက်တွင် လွိုင်နမ်းဖ ကျေးရွာသို့ ရောက်ရှိခဲ့ပါတယ်။",
                "ဆော့ဖ်ဝဲလ်အင်ဂျင်နီယာများသည် ဆာဗာလော့ဂ်များကို ပုံမှန်စစ်ဆေးပြုပြင်ကြသည်။",
                "မက်ခ်လီးနှင့် မေရီကလိဗ်တို့ တက်ရောက်ဆွေးနွေးကြသည်။"
            };
            foreach (string clean in cleanControls)
            {
                var modeEngine = clean.Contains("တယ်။") ? generalEngine : formalEngine;
                var fps = modeEngine.CheckText(clean);
                Assert.True(fps.Count == 0, $"Expected 0 false positives on '{clean}', got: [{string.Join("; ", fps.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            // 2. Structural, Detached Intra-Syllable Coda, Pali/Voiced Homophone, Dropped-2nd-'အ', Space-Split & Contextual Accuracy
            (ProofingStyleMode Mode, string Input, string ExpectedTopSubstring)[] errorSuite =
            {
                (ProofingStyleMode.FormalOfficial, "လာမ ည့် အပတ်တွင် အစည်းအဝေး ကျင်းပမည်။", "လာမည့်"),
                (ProofingStyleMode.FormalOfficial, "အပြုအမူပို င်းဆိုင်ရာ လေ့လာချက်ဖြစ်သည်။", "ပိုင်း"),
                (ProofingStyleMode.FormalOfficial, "ကာယကံရု ပ်သည် အရေးပါသည်။", "ရုပ်"),
                (ProofingStyleMode.GeneralStandard, "သူငယ်ချင်းအသစ်ရှာတာ ပျော်စရာကောင်း့တယ်။", "ကောင်း"),
                (ProofingStyleMode.FormalOfficial, "အရေးကြိးချက်များကို စနစ်တကျ တင်ပြထားပါသည်။", "ကြီး"),
                (ProofingStyleMode.FormalOfficial, "သက်တမး တိုးမြှင့်ပေးရန် လျှောက်ထားသည်။", "တမ်း"),
                (ProofingStyleMode.FormalOfficial, "သမန္တမင်း ပြော့င်းရွှေ့၍ နေထိုင်သည်။", "ပြောင်း"),
                (ProofingStyleMode.FormalOfficial, "A မျိုးသားဒီမိုကရေစီ A ဖွဲ့ချုပ် Aား ဖိတ်ကြားသည်။", "အမျိုးသား"),
                (ProofingStyleMode.FormalOfficial, "ကျောင်းတွင် ဃီတပွဲ ကျင်းပသည်။", "ဂီတ"),
                (ProofingStyleMode.FormalOfficial, "စမ်းသပ်ချက် ရလဓ် ကောင်းမွန်ပါသည်။", "ရလဒ်"),
                (ProofingStyleMode.FormalOfficial, "စီမံကိန်းကို အကောင်ထည်ဖော် ဆောင်ရွက်သည်။", "အကောင်အထည်ဖော်"),
                (ProofingStyleMode.FormalOfficial, "အဖွဲ့အစည်းအကြား ပြေလည်မူရရှိခဲ့သည်။", "ပြေလည်မှု"),
                (ProofingStyleMode.FormalOfficial, "စာ ကြည့် တိုက် နှင့် လူ မှု ရေး လုပ်ငန်းများ ဆောင်ရွက်သည်။", "စာကြည့်တိုက်"),
                (ProofingStyleMode.GeneralStandard, "တစ်နည်းပြော့ရရင် ဒါဟာ ကိုယ်တွေ့ပါဘဲ။", "ပြော"),
                (ProofingStyleMode.GeneralStandard, "ခွေးကို အစာ ကြွေး ပါ။", "ကျွေး"),
                (ProofingStyleMode.GeneralStandard, "ဒီသတင်းကို ကျား ရ တယ်။", "ကြား"),
                (ProofingStyleMode.GeneralStandard, "တောထဲမှာ ကြား တွေ ကို သတိထားရမယ်။", "ကျား"),
                (ProofingStyleMode.GeneralStandard, "ကလေးတွေ ထမင်းဆားပြီးရင် ကျောင်းသွားကြ။", "စား"),
                (ProofingStyleMode.GeneralStandard, "လုံခြုံစိတ်ချရတဲ့ ငွေပေးချေမှု စနစ်တွေ ရှိပါတယ်.", "။")
            };

            // Warmup pass so RegexOptions.Compiled IL generation is excluded from steady-state latency measurement
            foreach (var tc in errorSuite)
            {
                var eng = tc.Mode == ProofingStyleMode.FormalOfficial ? formalEngine : generalEngine;
                _ = eng.CheckText(tc.Input, cacheParagraph: false);
            }

            var latencies = new List<double>(errorSuite.Length);
            var sw = new System.Diagnostics.Stopwatch();
            foreach (var tc in errorSuite)
            {
                var eng = tc.Mode == ProofingStyleMode.FormalOfficial ? formalEngine : generalEngine;
                sw.Restart();
                var issues = eng.CheckText(tc.Input, cacheParagraph: false);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);

                string canonExp = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedTopSubstring);
                Assert.True(
                    issues.Any(iss => iss.Suggestions.Count > 0 &&
                        (UnicodeNormalizer.NormalizeCanonicalOrder(iss.Suggestions[0].Replacement).Contains(canonExp) ||
                         UnicodeNormalizer.NormalizeCanonicalOrder(
                             tc.Input.Substring(0, iss.StartIndex) +
                             iss.Suggestions[0].Replacement +
                             tc.Input.Substring(iss.StartIndex + iss.Length)).Contains(canonExp))),
                    $"Expected Top-1 containing '{tc.ExpectedTopSubstring}' on '{tc.Input}', got: [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }

            latencies.Sort();
            double p50 = latencies[latencies.Count / 2];
            double p95 = latencies[(int)(0.90 * latencies.Count)];
            Assert.True(p50 < 12.0 && p95 < 35.0, $"Expected steady-state p50 < 12 ms & p95 < 35 ms, got p50={p50:F2} ms, p95={p95:F2} ms.");
        }

        [Fact]
        public void V59_ComprehensiveSyllableValidation_Utn11Phonotactics_AndTop1Repair_Gate()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Direct Layer-1 MyanmarSyllableValidator UTN #11 & Phonotactic Unit Checks
            (string BadSyl, SyllableErrorKind ExpectedKind, string ExpectedRepair)[] directCases =
            {
                ("\u1000\u1031\u103B\u1038", SyllableErrorKind.DottedCircleCombiningOrder, "ကျေး"),
                ("\u1015\u1032\u103D", SyllableErrorKind.DottedCircleCombiningOrder, "ပွဲ"),
                ("\u101B\u102D\u103E", SyllableErrorKind.DottedCircleCombiningOrder, "ရှိ"),
                ("\u1019\u103E\u103B", SyllableErrorKind.DottedCircleCombiningOrder, "မျှ"),
                ("\u1016\u1037\u102D\u102F", SyllableErrorKind.DottedCircleCombiningOrder, "ဖို့"),
                ("\u1001\u1037\u1032", SyllableErrorKind.DottedCircleCombiningOrder, "ခဲ့"),
                ("\u1005\u1038\u102C", SyllableErrorKind.DottedCircleCombiningOrder, "စား"),
                ("\u1000\u103C\u102F\u102D\u1038", SyllableErrorKind.DottedCircleCombiningOrder, "ကြိုး"),
                ("\u101B\u103E\u103E\u100A\u103A", SyllableErrorKind.DuplicateCombiningMark, "ရှည်"),
                ("\u101C\u1014\u103A\u1038\u103A", SyllableErrorKind.DuplicateCombiningMark, "လန်း"),
                ("\u1021\u103A", SyllableErrorKind.StrayAsatOnVowelOrAh, "အ"),
                ("\u1011\u102D\u102F\u103A\u1038", SyllableErrorKind.StrayAsatOnVowelOrAh, "ထိုး"),
                ("\u1015\u103E\u1004\u1037\u103A", SyllableErrorKind.InvalidStopOrSibilantHaHto, "ပွင့်"),
                ("\u101E\u103E\u102C\u1038", SyllableErrorKind.InvalidStopOrSibilantHaHto, "သွား"),
                ("\u1029\u103D\u102C\u1038", SyllableErrorKind.InvalidIndependentVowelCluster, "သွား"),
                ("\u1005\u1010\u1039\u102D", SyllableErrorKind.InvalidViramaOrPatSint, "စိတ်"),
                ("\u101E\u102D\u1019\u103A\u1039\u1038", SyllableErrorKind.InvalidViramaOrPatSint, "သိမ်း"),
                ("\u1010\u101A\u1039", SyllableErrorKind.InvalidViramaOrPatSint, "တယ်"),
                ("\u1016\u102F\u1014\u1038", SyllableErrorKind.MissingCodaAsatBeforeVisarga, "ဖုန်း"),
                ("\u1014\u100A\u1038", SyllableErrorKind.MissingCodaAsatBeforeVisarga, "နည်း"),
                ("\u1000\u103C\u102D\u1038", SyllableErrorKind.ShortVowelWithVisarga, "ကြီး")
            };

            foreach (var dc in directCases)
            {
                bool valid = MyanmarSyllableValidator.ValidateSyllable(dc.BadSyl, out SyllableErrorKind kind, out string reason, out string? repair, trie);
                Assert.False(valid, $"Expected '{dc.BadSyl}' to be invalid.");
                Assert.Equal(dc.ExpectedKind, kind);
                Assert.False(string.IsNullOrWhiteSpace(reason));
                Assert.Equal(
                    UnicodeNormalizer.NormalizeCanonicalOrder(dc.ExpectedRepair),
                    UnicodeNormalizer.NormalizeCanonicalOrder(repair ?? string.Empty));
            }

            // 2. Clean UTN #11 & Classical Pali/Thatpon Invariants (including legacy \u1037\u103A vs \u103A\u1037!)
            string[] validSyllables = { "နှင့်", "နှင့်", "သင်္ဘော", "ဒွါ", "ဝှါ", "ဩောင်း", "ရှုး", "ကျုး", "မေတ္တာ", "ကမ္ဘာ" };
            foreach (string vs in validSyllables)
            {
                Assert.True(MyanmarSyllableValidator.ValidateSyllable(vs, out _, out string err, out _, trie), $"Expected '{vs}' valid, got '{err}'");
            }

            string[] cleanSentences =
            {
                "ယောက်ျားပီသစွာဖြင့် ကျွန်ုပ်တို့နှင့်အတူ လက်ယာဘက်သို့ လျှောက်လှမ်းသည်။",
                "ဗုဒ္ဓမြတ်စွာ ဂောတမသည် ဒွါရခြောက်ပါးကို ဆုံးမတော်မူသည်။",
                "မိုင်းရှုးမြို့နယ်တွင် ကျေးရွာဖွံ့ဖြိုးရေး ဆောင်ရွက်သည်။"
            };
            foreach (string cs in cleanSentences)
            {
                var fps = formalEngine.CheckText(cs);
                Assert.True(fps.Count == 0, $"Expected 0 FPs on '{cs}', got: [{string.Join("; ", fps.Select(x => x.OriginalText))}]");
            }

            // 3. End-to-End Shattered Syllable, Hybrid Unicode/Zawgyi Isolation & Missing-Asat Compound Top-1 Repairs
            (ProofingStyleMode Mode, string Input, string ExpectedSplicedSubstring)[] endToEndCases =
            {
                (ProofingStyleMode.GeneralStandard, "ကူညီပေးတဲ့အတွက် ကေျးဇူးတင်ပါတယ်။", "ကျေးဇူး"),
                (ProofingStyleMode.GeneralStandard, "ကြိုးပုြတ်သွားလို့ ပြန်ချည်ထားတယ်။", "ကြိုးပြုတ်"),
                (ProofingStyleMode.GeneralStandard, "စကားပောြဆိုရာမှာ ယဉ်ကျေးသင့်တယ်။", "စကားပြောဆို"),
                (ProofingStyleMode.FormalOfficial, "စည်းလုံးညီည်ွတ်စွာ ဆောင်ရွက်ကြသည်။", "ညီညွတ်စွာ"),
                (ProofingStyleMode.GeneralStandard, "ကျန်းမာရေးကောင်းဖို့အတ်ွက် လေ့ကျင့်ပါ။", "ဖို့အတွက်"),
                (ProofingStyleMode.FormalOfficial, "ကမ္ဘာ်ကြီး ငြိမ်းချမ်းသာယာစေရန် ဆုတောင်းသည်။", "ကမ္ဘာကြီး"),
                (ProofingStyleMode.GeneralStandard, "မိတ်ဆွေတွေနဲ့ ချိ်တ်ဆက် ဆောင်ရွက်တယ်။", "ချိတ်ဆက်"),
                (ProofingStyleMode.GeneralStandard, "အချိ်န်ကုန် သက်သာစေတယ်။", "အချိန်ကုန်"),
                (ProofingStyleMode.GeneralStandard, "ပစ္စည်းတွေကို သေချာသိမ်္းထားပြီးပြီ။", "သိမ်းထားပြီးပြီ"),
                (ProofingStyleMode.GeneralStandard, "အကြောင်းအရာကို သိချင်ပါတယ္။", "သိချင်ပါတယ်။"),
                (ProofingStyleMode.FormalOfficial, "အနုပညာသည် ဘဝကို အဓိပ္ပာယ္ရှိစေပါသည်။", "အဓိပ္ပာယ်ရှိ"),
                (ProofingStyleMode.GeneralStandard, "အလုပ်လုပ်ရာမှာ စတ္ိထားရမယ်။", "စိတ်ထား"),
                (ProofingStyleMode.GeneralStandard, "မုန့်ဟငိ်းခါး စားချင်တယ်။", "မုန့်ဟင်းခါး"),
                (ProofingStyleMode.GeneralStandard, "အခက်အခဲတွေကို အတူရ်ငဆိုင်ကြတယ်။", "အတူရင်ဆိုင်"),
                (ProofingStyleMode.GeneralStandard, "ရာသီဥတု ကော့်င မွန်ပါတယ်။", "ကောင်း"),
                (ProofingStyleMode.GeneralStandard, "ဆောင်းရာသီမှာတော့ ချမ်းအေးတ္တယ်။", "ချမ်းအေးတတ်တယ်။"),
                (ProofingStyleMode.GeneralStandard, "အရက်သောက်ခြင်းက အသည်းိကို ထိခိုက်စေတယ်။", "အသည်းကို"),
                (ProofingStyleMode.GeneralStandard, "သီချင်းနားထောင် ရင်းိ စာဖတ်တယ်။", "ရင်း စာဖတ်"),
                (ProofingStyleMode.FormalOfficial, "သတင်းအချက်အလက များကို စိစစ်သည်။", "အချက်အလက်"),
                (ProofingStyleMode.GeneralStandard, "ကျန်းမာရေးအတွက အထောက်အကူပြုတယ်။", "ကျန်းမာရေးအတွက်"),
                (ProofingStyleMode.FormalOfficial, "စမ်းသပ်ချက် ရလဒကို တင်ပြသည်။", "ရလဒ်ကို"),
                (ProofingStyleMode.GeneralStandard, "အားလုံး အဆင်ပြေပါတယ။", "အဆင်ပြေပါတယ်။"),
                (ProofingStyleMode.GeneralStandard, "စွမ်းေဆာင်ရည် မြင့်မားတယ်။", "စွမ်းဆောင်ရည်"),
                (ProofingStyleMode.GeneralStandard, "အခြေခံသဘောတရားေတွကို လေ့လာတယ်။", "တရားတွေကို"),
                (ProofingStyleMode.GeneralStandard, "စိတ်ရှှည်သည်းခံမှုရှိကြတယ်။", "စိတ်ရှည်သည်းခံမှုရှိကြတယ်။"),
                (ProofingStyleMode.GeneralStandard, "အဖေနဲ့အမ\u200Bေက ချစ်ကြတယ်။", "အဖေနဲ့အမေက")
            };

            foreach (var tc in endToEndCases)
            {
                var engine = tc.Mode == ProofingStyleMode.FormalOfficial ? formalEngine : generalEngine;
                var issues = engine.CheckText(tc.Input);
                Assert.NotEmpty(issues);
                string expNorm = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedSplicedSubstring);
                bool anyTop1Matches = issues.Any(iss =>
                    iss.Suggestions.Count > 0 &&
                    UnicodeNormalizer.NormalizeCanonicalOrder(
                        tc.Input.Substring(0, iss.StartIndex) +
                        iss.Suggestions[0].Replacement +
                        tc.Input.Substring(iss.StartIndex + iss.Length)).Contains(expNorm));
                Assert.True(
                    anyTop1Matches,
                    $"Expected spliced Top-1 containing '{tc.ExpectedSplicedSubstring}' on '{tc.Input}', got: [{string.Join("; ", issues.Select(x => $"{x.OriginalText}=>{x.Suggestions.FirstOrDefault()?.Replacement}"))}]");
            }
        }

        [Fact]
        public void V60B_WordDataValidation_SyllablesParticlePurificationContradictionsAndDomainRescue_Gate()
        {
            // 1. Full 6-Gate Read-Only Audit on Master JSON (0 violations across all Entries, Rules, ContextRules & Bigrams)
            var master = EncryptedDictionaryRepository.ParseFromJsonFile(TestPaths.MasterJsonPath);
            var report = MyanmarWordDataValidator.Validate(master);
            Assert.True(
                report.IsClean,
                $"Expected 0 violations from MyanmarWordDataValidator, got {report.Violations.Count}: " +
                string.Join(" || ", report.Violations.Take(8).Select(v => $"[{v.Table}] '{v.ItemKey}' ({v.Kind}): {v.Detail}")));

            Assert.All(master.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.PosTag), $"Empty PosTag on '{e.Word}'"));

            var byWord = master.Entries
                .GroupBy(e => e.Word, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // 2. Verify Repaired Official, Geographic, Foreign Coda & Encyclopedia Headwords
            Assert.True(byWord.ContainsKey(UnicodeNormalizer.NormalizeCanonicalOrder("ကိုယ်ရင့်တတူ")) &&
                        byWord[UnicodeNormalizer.NormalizeCanonicalOrder("ကိုယ်ရင့်တတူ")].Any(e => e.IsOfficialThatpon));
            Assert.False(byWord.ContainsKey("ကို်ယ်ရင့်တတူ"));
            Assert.False(byWord.ContainsKey("ကို်ယ်ရင့်တတူ"));
            Assert.True(byWord.ContainsKey("ဘန်ဂျူးလ်"));
            Assert.False(byWord.ContainsKey("ဘန်ဂျုးလ်"));
            Assert.True(byWord.ContainsKey("မော်ဂါဒီးရှူး"));
            Assert.False(byWord.ContainsKey("မော်ဂါဒီးသျုး"));
            Assert.True(byWord.ContainsKey("ဆိုးဇန်းဂျိဘူတာ"));
            Assert.False(byWord.ContainsKey("ဆိုးဇန်းဂျိူဘူတာ"));
            Assert.True(byWord.ContainsKey("လိုင်လူမျိုး"));
            Assert.False(byWord.ContainsKey("လိုင်လူမျိူး"));
            Assert.True(byWord.ContainsKey(UnicodeNormalizer.NormalizeCanonicalOrder("နှစ် ၇၀၀ ပြည့် အားကစားကွင်း")));
            Assert.False(byWord.ContainsKey(UnicodeNormalizer.NormalizeCanonicalOrder("နှစ် ၇၀ဝပြည့် အားကစားကွင်း")));
            Assert.True(byWord.ContainsKey("ဗက်တာ"));
            Assert.False(byWord.ContainsKey("ဗှတ္တာ"));
            Assert.True(byWord.ContainsKey("အဗ္ဘဒူ မာလစ်"));
            Assert.False(byWord.ContainsKey("အဘ္ဘဒူ မာလစ်"));
            Assert.False(byWord.ContainsKey("ဒီဇိုင်းနာ"));
            Assert.False(byWord.ContainsKey("မရိုးပင်"));
            Assert.Contains(byWord["ကားဘီး"], e => e.PosTag == "N");
            Assert.Contains(byWord["ကက်တိုင်ယွန်"], e => !e.MeaningCue.Contains("နျယန") && !(e.UsageNote ?? "").Contains("နျယန"));

            // 3. Verify Runtime Encrypted DB: Glued-Particle Leaks Purged, Rescued Domain/Dictionary Words Active!
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            string[] purgedGarbage =
            {
                "ကကျွမ်းကျင်", "ကကျွမ်းကျင်မှု", "ကနည်းနည်း", "ကတည်းကစ",
                "ကိုစိုင်းရဲ့", "ကိုလေးဖြူရဲ့", "ငါ့ရဲ့", "တိုင်းပြည်ရဲ့",
                "ကြည့်လို့", "ကြောက်လို့", "ကောင်းကင်သို့", "ကျွန်ုပ်၏", "တရားသူကြီး၏",
                "ကင်းပါစေ", "ကစ်ရယ်", "ကစ်ရေ", "ကင်းဘူ", "တဆံဃိး", "ဂန္ဒီ"
            };
            foreach (string bad in purgedGarbage)
            {
                Assert.False(trie.ContainsExactWord(bad), $"Expected glued-particle/noise entry '{bad}' to be purged from active Trie.");
            }

            string[] preservedLegitimateWords =
            {
                "တကယ်လို့", "ပတ်သက်လို့", "စေသတည်း", "ဤသို့", "ထိုသို့", "မည်သို့",
                "ဖက်ဒရယ်", "ဂျော့ချ်", "ယောက်ျား", "ကျွန်ုပ်", "ဂန္ဓီ"
            };
            foreach (string good in preservedLegitimateWords)
            {
                Assert.True(trie.ContainsExactWord(good), $"Expected legitimate word '{good}' to remain active in Trie.");
            }

            string[] rescuedHelpfulQuantityWords =
            {
                "ပါတိမောက်", "ပါရာဇိကကျ", "ပါဒရက်ရောဂါ", "ပါကစ္စတန်ရူပီး",
                "ပါရှန်ပင်လယ်ကွေ့", "ပါလစ်", "ပါဝါ", "ပါမစ်", "ပါမန်ဂနိတ်",
                "ပါစက", "ပါစကဓာတ်", "ပါစတာဆေးရုံ", "ပါဏဗေဒဌာန",
                "ဘူးခါး", "ဘူးစင်", "ဘူးသွင်း"
            };
            foreach (string rescued in rescuedHelpfulQuantityWords)
            {
                Assert.True(trie.ContainsExactWord(rescued), $"Expected rescued domain/dictionary word '{rescued}' to exist in active Trie.");
            }

            // Repaired 'ကို်ယ်ရင့်တတူ' is now caught and repaired to 'ကိုယ်ရင့်တတူ'!
            var strayAsatIssues = formalEngine.CheckText("ကို်ယ်ရင့်တတူ ဆောင်ရွက်သည်။");
            Assert.NotEmpty(strayAsatIssues);
            Assert.Contains(strayAsatIssues, x => x.Suggestions.Count > 0 && x.Suggestions[0].Replacement.Contains("ကိုယ်"));
            Assert.Empty(formalEngine.CheckText("ကိုယ်ရင့်တတူ ဆောင်ရွက်သည်။"));
            Assert.Empty(formalEngine.CheckText("ရဟန်းတော်များသည် ပါတိမောက် ပြပါသည်။"));
            Assert.Empty(generalEngine.CheckText("တကယ်လို့ မိုးရွာရင် အိမ်မှာနေမယ်။"));

            // 4. Unit Test SanitizeAndValidateInPlace: Rule-Chain Flattening, ENCYC Contradiction Migration & Noise Pruning
            var syntheticModel = new MasterDictionaryJsonModel
            {
                Entries = new List<DictionaryEntry>
                {
                    new DictionaryEntry { Word = "ဒီဇိုင်နာ", PosTag = "", MeaningCue = "ဖန်တီးသူ။", Frequency = 50, IsOfficialThatpon = true },
                    new DictionaryEntry { Word = "ဒီဇိုင်းနာ", PosTag = "ENCYC", MeaningCue = "ဒီဇိုင်းနာ ဆိုသည်မှာ ဒီဇိုင်းပညာရှင်ဖြစ်သည်။", Frequency = 20, IsOfficialThatpon = false },
                    new DictionaryEntry { Word = "ကိုစိုင်းရဲ့", PosTag = "N/V", MeaningCue = "ခေတ်သုံး မြန်မာစာပေနှင့် ရုံးသုံး စံသတ်ပုံအမှန် နာမ်ဝေါဟာရ။", Frequency = 12, IsOfficialThatpon = false }
                },
                OrthographyRules = new List<OrthographyRule>
                {
                    new OrthographyRule { WrongForm = "ဒီဇိုင်းနား", CorrectForm = "ဒီဇိုင်းနာ", Category = "Spelling", Explanation = "စံသတ်ပုံ" },
                    new OrthographyRule { WrongForm = "ဒီဇိုင်းနာ", CorrectForm = "ဒီဇိုင်နာ", Category = "Spelling", Explanation = "စံသတ်ပုံ" }
                },
                WordBigrams = new List<WordBigramEntry>
                {
                    new WordBigramEntry { LeftWord = "ဒီဇိုင်းနာ", RightWord = "ဒီဇိုင်နာ", Count = 15 }
                }
            };

            var synthReport = MyanmarWordDataValidator.SanitizeAndValidateInPlace(syntheticModel);
            Assert.True(synthReport.IsClean);
            Assert.Equal(1, synthReport.RulesFlattenedCount);
            Assert.Equal("ဒီဇိုင်နာ", syntheticModel.OrthographyRules.First(r => r.WrongForm == "ဒီဇိုင်းနား").CorrectForm);
            Assert.All(syntheticModel.Entries, e => Assert.Equal("ဒီဇိုင်နာ", e.Word));
            Assert.Equal("N", syntheticModel.Entries.First(e => e.IsOfficialThatpon).PosTag);
            Assert.Empty(syntheticModel.WordBigrams);
        }

        [Fact]
        public void V61B_EasyToHardestProgressiveBenchmark_SovTenseParticleAndBrokenCompound_Gate()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            Assert.True(File.Exists(dbPath), "Compiled mlc_thatpon.db must exist.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Adversarial Clean Controls (0 False Positives across tricky medical, culinary, IT, classifier, simile & female-noun patterns)
            string[] cleanGeneralControls =
            {
                "ဗိုက်ဆာလို့ အားမရှိသလိုပဲ။",
                "တစ်နေ့ကျရင် နိုင်ငံခြားတိုင်းပြည်တွေကို လေ့လာရေးသွားချင်တယ်။",
                "ခေါက်ဆွဲပေါ်မှာ နှမ်းဆီနဲ့ ပဲဆီ မွှေးမွှေးလေး ဆမ်းထားတယ်။",
                "ဒီသင်တန်းကို ဆရာမကြီးပဲ ကိုယ်တိုင် ဦးဆောင်သင်ကြားပေးမှာ ဖြစ်ပါတယ်။",
                "တစ်စုံတစ်ယောက်ကို မမေးမြန်းပါဘဲ ဆုံးဖြတ်ချက် မချသင့်ပါဘူး။",
                "ပထမတန်းပဲ တက်ရသေးပေမဲ့ စာဖတ်တာ အရမ်းသွက်လက်တယ်။",
                "မီးဖိုက မီးခိုးတွေ ထွက်လာလို့ ပြတင်းပေါက် ဖွင့်ထားလိုက်တယ်။"
            };
            foreach (string clean in cleanGeneralControls)
            {
                var fp = generalEngine.CheckText(clean);
                Assert.True(fp.Count == 0, $"Expected 0 issues on clean General sentence '{clean}', got: {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            string[] cleanFormalControls =
            {
                "ပါမောက္ခဆရာဝန်ကြီးသည် ပါကင်ဆန်ရောဂါ ကုသရေးစာတမ်းကို ဖတ်ကြားသည်။",
                "ကမ်းခြေထီးတစ်ထည်နှင့် ရေကူးဝတ်စုံကို အိတ်ထဲတွင် ထုပ်ပိုးထားသည်။",
                "ပရိုဂရမ်ရေးသားသူများသည် အယ်လ်ဂိုရီသမ်နှင့် ဘလော့ချိန်းနည်းပညာကို လေ့လာကြသည်။",
                "ခရီးသည်များ သက်သောင့်သက်သာ အနားယူနိုင်သည်။",
                "အစိုးရ အဖွဲ့အသီးသီးနှင့် ညှိနှိုင်း၍ ဒေသဖွံ့ဖြိုးရေးလုပ်ငန်းများကို ဆောင်ရွက်သည်။",
                "လယ်သမားများသည် နွားဘို့နှင့် ထမ်းပိုးကို သေချာစွာ စစ်ဆေးကြသည်။"
            };
            foreach (string clean in cleanFormalControls)
            {
                var fp = formalEngine.CheckText(clean);
                Assert.True(fp.Count == 0, $"Expected 0 issues on clean Formal sentence '{clean}', got: {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            // 2. Easy -> Medium -> Hard -> Hardest Progressive Detection & Top-1 Repair Assertions
            (MyanmarProofingEngine Engine, string Input, string ExpectedOrig, string ExpectedTop1)[] progressiveCases =
            {
                // Easy: ASCII 'A' before Myanmar vowel mark (must repair to 'အား', not strip diacritic!)
                (formalEngine, "ကျောင်းသားများ Aားကစား ကွင်း၌ လေ့ကျင့်ကြသည်။", "Aား", "အား"),

                // Medium: Symmetric oral-stop coda triangle (-ပ် -> -က် / -တ်) & multi-token '-မူ -> -မှု' nominalizations
                (formalEngine, "ယာဉ်မတော်တဆမှုကြောင့် ထိခိုပ်ဒဏ်ရာရရှိခဲ့သည်။", "ထိခိုပ်ဒဏ်ရာ", "ထိခိုက်ဒဏ်ရာ"),
                (generalEngine, "နံရံကို သုပ်ဆေး အသစ် ပြန်သုတ်ထားတယ်။", "သုပ်ဆေး", "သုတ်ဆေး"),
                (formalEngine, "ဒစ်ဂျစ်တယ် ငွေပေးချေမူ စနစ်ကို အသုံးပြုသည်။", "ငွေပေးချေမူ", "ငွေပေးချေမှု"),
                (formalEngine, "ဆရာများ၏ သင်ကြားပို့ချမူကို ကျောင်းသားများ နှစ်သက်သည်။", "သင်ကြားပို့ချမူ", "သင်ကြားပို့ချမှု"),
                (formalEngine, "ခရီးသည်များ သက်တောင့်သက်သာ နားနေနိုင်သည်။", "သက်တောင့်သက်သာ", "သက်သောင့်သက်သာ"),

                // Hard: Broken compounds, bound-suffix space splits, bipartite N-မ-V-ပဲ, purposive ဘို့/ဖို & gerund ချင်း
                (formalEngine, "မြို့နယ် အုပ် ချုပ်ရေးမှူး က ရှင်းလင်းပြောကြားသည်။", "အုပ် ချုပ်ရေးမှူး", "အုပ်ချုပ်ရေးမှူး"),
                (formalEngine, "ဆရာတော်ကြီးက တရားဟောကြား တော်မူ၍ ပရိသတ်များ နာယူကြသည်။", "ဟောကြား တော်မူ", "ဟောကြားတော်မူ"),
                (formalEngine, "ပြည်သူများအပေါ် အမြတ်မထုတ်ပဲ ရောင်းချပေးသည်။", "ပဲ", "ဘဲ"),
                (generalEngine, "သူက မနက်ဖြန် ရောက်လာမှာ ဖြစ်ပါဘဲ။", "ဘဲ", "ပဲ"),
                (formalEngine, "လူနာသည် ဆေးရုံမှ ဆင်းခွင့်ရပြီး။", "ပြီး", "ပြီ"),
                (generalEngine, "သဘာဝပတ်ဝန်းကျင် ကာကွယ်ဖိုက အားလုံးရဲ့ တာဝန်ပါ။", "ဖို", "ဖို့"),
                (generalEngine, "နေ့စဉ် လမ်းလျှောက်ချင်းက ကျန်းမာရေးအတွက် ကောင်းတယ်။", "ချင်း", "ခြင်း"),

                // Hardest / Expert: Real-word collocations, 'တစ်' classifier agreement, morphosyntax, temporal agreement & SOV word order
                (generalEngine, "မနက်တိုင်း အဝတ် လျော် ပါတယ်။", "လျော်", "လျှော်"),
                (generalEngine, "နေပူထဲ အဝတ် လှမ်း ထားတယ်။", "လှမ်း", "လှန်း"),
                (formalEngine, "တရားခံသည် ပြင်းထန်သော ပစ်မှု ကျူးလွန်ခဲ့သည်။", "ပစ်မှု", "ပြစ်မှု"),
                (generalEngine, "ဖိနပ် တစ်ခု ဝယ်လာတယ်။", "ခု", "ရံ"),
                (formalEngine, "ပြဿနာကို ထိရောက်သော ဖြေရှင်းနိုင်ခဲ့သည်။", "သော", "စွာ"),
                (formalEngine, "သူသည် မန္တလေးမြို့ကို နေထိုင်သည်။", "ကို", "တွင်"),
                (formalEngine, "ဝန်ကြီးဌာနသည် စီမံချက်ကို ပြင်ဆင်ကာ ရှိသည်။", "ကာ", "လျက်"),
                (formalEngine, "လာမည့်နှစ်တွင် အဆောက်အအုံသစ်ကို ဖွင့်လှစ်ခဲ့သည်။", "ခဲ့သည်", "မည်ဖြစ်သည်"),
                (formalEngine, "ယမန်နှစ်က ညီလာခံကြီးကို အောင်မြင်စွာ ကျင်းပမည်ဖြစ်သည်။", "မည်ဖြစ်သည်", "ခဲ့သည်"),
                (generalEngine, "ကလေးက အခန်းထဲမှာ စာရေးခဲ့နေပြီ။", "ခဲ့နေပြီ", "နေတယ်"),
                (formalEngine, "ဝန်ကြီးဌာနက တင်သွင်းခဲ့သည် အစီရင်ခံစာသစ်ကို လွှတ်တော်သို့ ဟု ထုတ်ပြန်သည်။",
                    "တင်သွင်းခဲ့သည် အစီရင်ခံစာသစ်ကို လွှတ်တော်သို့",
                    "အစီရင်ခံစာသစ်ကို လွှတ်တော်သို့ တင်သွင်းခဲ့သည်"),
                (generalEngine, "ကျောင်းသားများက လေ့လာချင်ကြတယ် နည်းပညာဘာသာရပ်သစ်ကို။",
                    "လေ့လာချင်ကြတယ် နည်းပညာဘာသာရပ်သစ်ကို",
                    "နည်းပညာဘာသာရပ်သစ်ကို လေ့လာချင်ကြတယ်")
            };

            foreach (var (eng, inp, expectedOrig, expectedTop1) in progressiveCases)
            {
                var issues = eng.CheckText(inp);
                var match = issues.FirstOrDefault(x =>
                    x.OriginalText == expectedOrig ||
                    x.OriginalText.Contains(expectedOrig) ||
                    expectedOrig.Contains(x.OriginalText));
                Assert.True(match != null, $"Expected span '{expectedOrig}' in '{inp}', got: {string.Join(", ", issues.Select(i => i.OriginalText))}");
                Assert.NotEmpty(match!.Suggestions);

                int origPos = inp.IndexOf(expectedOrig, StringComparison.Ordinal);
                string expectedSentence = UnicodeNormalizer.NormalizeCanonicalOrder(
                    inp.Substring(0, origPos) + expectedTop1 + inp.Substring(origPos + expectedOrig.Length));
                string actualSentence = UnicodeNormalizer.NormalizeCanonicalOrder(
                    inp.Substring(0, match.StartIndex) + match.Suggestions[0].Replacement + inp.Substring(match.StartIndex + match.Length));
                Assert.Equal(expectedSentence, actualSentence);
            }
        }

        [Fact]
        public void V62B_NightmareAndGrandmasterMultiErrorTorture_Gate()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            Assert.True(File.Exists(dbPath), "Compiled mlc_thatpon.db must exist.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Evil-Twin Minimal-Pair Clean Controls (must produce 0 false positives)
            (MyanmarProofingEngine Engine, string CleanInput)[] evilTwins =
            {
                (formalEngine, "တပ်ဖွဲ့ဝင်များသည် သေနတ်ပစ်မှု လေ့ကျင့်ရေးကွင်း၌ စနစ်တကျ လေ့ကျင့်ကြသည်။"),
                (formalEngine, "ပျက်စီးသွားသော ပစ္စည်းတန်ဖိုးကို အပြည့်အဝ ပြန်လည်လျော်ပေးရမည်။"),
                (formalEngine, "ကလေးများ လက်လှမ်းမမီသော နေရာတွင် ဆေးဝါးများကို သိမ်းဆည်းပါ။"),
                (generalEngine, "မနက်ဖြန် ရုံးကို စာရွက်စာတမ်းတွေ ယူလာခဲ့မယ် လို့ ပြောသွားတယ်။"),
                (formalEngine, "နူးညံ့ပျော့ပျောင်းသော ပိုးထည်များကို သေတ္တာထဲတွင် စနစ်တကျ ထည့်ထားသည်။"),
                (formalEngine, "ဝင်ငွေနှင့် အထွက်စာရင်းများကို လချုပ်အစီရင်ခံစာတွင် တိကျစွာ ဖော်ပြထားသည်။"),
                (formalEngine, "ရဲတပ်ဖွဲ့ စခန်းမှူးက ယာဉ်စည်းကမ်း လိုက်နာရေးအတွက် အသိပညာပေး ဟောပြောသည်။")
            };
            foreach (var (eng, clean) in evilTwins)
            {
                var fp = eng.CheckText(clean);
                Assert.True(fp.Count == 0, $"Evil-twin clean control flagged '{clean}': {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            // 2. Grandmaster Multi-Error Torture: Apply ALL non-overlapping Top-1 suggestions simultaneously (right-to-left)
            //    and assert 100% exact full-sentence reconstruction!
            (MyanmarProofingEngine Engine, string TortureInput, int ExpectedIssueCount, string ExpectedCleanOutput)[] grandmasterSentences =
            {
                (
                    formalEngine,
                    "ဝန်ကြီးဌာနက အရေးကြိးသော အချက် အလက်များကို ပေးပို့ခဲ့သည် တိုင်းဒေသကြီးရုံးသို့။",
                    3,
                    "ဝန်ကြီးဌာနက အရေးကြီးသော အချက်အလက်များကို တိုင်းဒေသကြီးရုံးသို့ ပေးပို့ခဲ့သည်။"
                ),
                (
                    formalEngine,
                    "ယမန်နှစ်က အာဏာသိမ်းမူဆိုင်ရာ ဆွေနွေးပွဲကို အောင်မြင်စွာ ကျင်းပမည်ဖြစ်သည်။",
                    3,
                    "ယမန်နှစ်က အာဏာသိမ်းမှုဆိုင်ရာ ဆွေးနွေးပွဲကို အောင်မြင်စွာ ကျင်းပခဲ့သည်။"
                ),
                (
                    formalEngine,
                    "လာမ ည့် နှစ်တွင် ပညာရေ စီမံကိန်းသစ်ကို စတင်ဆောင်ရွက်ခဲ့သည်။",
                    3,
                    "လာမည့် နှစ်တွင် ပညာရေး စီမံကိန်းသစ်ကို စတင်ဆောင်ရွက်မည်ဖြစ်သည်။"
                ),
                (
                    formalEngine,
                    "အဆိုရရဲ့ ဝန်စောင့်မှုလုပ်ငန်းများ ပိုမိုကောင်းမွန်လာအောင်းလည်း ဆောင်ရွက်ပေးသည်။",
                    4,
                    "အစိုးရ၏ ဝန်ဆောင်မှုလုပ်ငန်းများ ပိုမိုကောင်းမွန်လာအောင်လည်း ဆောင်ရွက်ပေးသည်။"
                ),
                (
                    generalEngine,
                    "ယာဉ်မတော်တဆမှုကြောင့် ထိခိုပ်ဒဏ်ရာရရှိသူတွေးကို ဆေးရုံသို့ ပို့ဆောင်ပြီးပါပီ.",
                    4,
                    "ယာဉ်မတော်တဆမှုကြောင့် ထိခိုက်ဒဏ်ရာရရှိသူတွေကို ဆေးရုံသို့ ပို့ဆောင်ပြီးပါပြီ။"
                ),
                (
                    generalEngine,
                    "ဒါနပြုခြင်းဖြင့် စိပ်ကြည်နူးမှု ရရှိသလိုဘဲ တိုးတက်စေပါတယ် လူ့အသိုင်းအဝိုင်းကို။",
                    3,
                    "ဒါနပြုခြင်းဖြင့် စိတ်ကြည်နူးမှု ရရှိသလိုပဲ လူ့အသိုင်းအဝိုင်းကို တိုးတက်စေပါတယ်။"
                )
            };

            foreach (var (eng, raw, expectedCount, expectedClean) in grandmasterSentences)
            {
                var issues = eng.CheckText(raw);
                Assert.Equal(expectedCount, issues.Count);

                string patched = raw;
                foreach (var iss in issues.OrderByDescending(i => i.StartIndex))
                {
                    Assert.NotEmpty(iss.Suggestions);
                    patched = patched.Substring(0, iss.StartIndex) +
                              iss.Suggestions[0].Replacement +
                              patched.Substring(iss.StartIndex + iss.Length);
                }

                Assert.Equal(
                    UnicodeNormalizer.NormalizeCanonicalOrder(expectedClean),
                    UnicodeNormalizer.NormalizeCanonicalOrder(patched));
            }
        }

        [Fact]
        public void V63B_CompositionalRerankerBoundaryAndClassicalNissayaShield_Gate()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            Assert.True(File.Exists(dbPath), "Compiled mlc_thatpon.db must exist.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Classical Pitaka/Nissaya Orthography Shield, Euphonic '-ပေတည်း', Foreign Killed-Coda & Productive 'ရကြောင်း' Clean Controls
            (MyanmarProofingEngine Engine, string CleanInput)[] cleanControls =
            {
                (formalEngine, "ရဟန်းတို့ သူတပါးအကျိုးငှါ တစုံတခုသော ကောင်းမှုကို ပြုရာ၏ဟုဆိုကုန်၏။"),
                (formalEngine, "မှန်ကန်သော လုံ့လဝီရိယသည် ကြီးပွားချမ်းသာခြင်း၏ အကြောင်းရင်းပေတည်း။"),
                (formalEngine, "အစည်းအဝေးသို့ မစ္စတာ ရောဘတ်စ် နှင့် ကိုယ်စားလှယ်အဖွဲ့ တက်ရောက်ကြသည်။"),
                (formalEngine, "ကွင်းဆင်းစစ်ဆေးချက်အရ သက်သေအထောက်အထား အပြည့်အစုံ တွေ့ရှိရကြောင်း တင်ပြသည်။"),
                (generalEngine, "အတိအကျ ပြောရရင် တကယ် တကယ် ကြိုးစားမှ အောင်မြင်မှာပါ။")
            };

            foreach (var (eng, clean) in cleanControls)
            {
                var fp = eng.CheckText(clean);
                Assert.True(fp.Count == 0, $"Clean control falsely flagged '{clean}': {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            // 2. Pillar #1 (Compositional Syllable-Preserving Reranker, Unstacked Pat-Sint, Pure-Lookalike Neighbor Shield)
            //    + Pillar #2 (Space-Split Compounds, Trailing Virama-Asat, Glued Phrase Boundaries)
            //    + Pillar #3 (Intra-Sentence Register-Clash Detector)
            (MyanmarProofingEngine Engine, string RawInput, string ExpectedCleanSentence)[] repairSentences =
            {
                (
                    formalEngine,
                    "ယာဉ်အန်ထရာယ် ကင်းရှင်းရေးအတွက် စက်ကန့်ပိုင်း အတွင်း စနတ်တကျ စစ်ဆေးသည်။",
                    "ယာဉ်အန္တရာယ် ကင်းရှင်းရေးအတွက် စက္ကန့်ပိုင်း အတွင်း စနစ်တကျ စစ်ဆေးသည်။"
                ),
                (
                    formalEngine,
                    "တံခါးပေါက်မှ လေအေးများ မ၀င်အောင် သေချာပိတ်ထားသည်။",
                    "တံခါးပေါက်မှ လေအေးများ မဝင်အောင် သေချာပိတ်ထားသည်။"
                ),
                (
                    generalEngine,
                    "ပစ္စည်းတွေကို သေချာ သိမ်ဆည်းတာ ကောင်းသလို အကျံပေးချင်ပါတယ်။",
                    "ပစ္စည်းတွေကို သေချာ သိမ်းဆည်းတာ ကောင်းသလို အကြံပေးချင်ပါတယ်။"
                ),
                (
                    formalEngine,
                    "တရား ဥပဒေ စိုးမိုးရေးအတြက္ စာရွက်စာတမ်းများကို စနတ်တကျ လုံခြုံစွာသိမ်းဆည်း ရမည်။",
                    "တရားဥပဒေ စိုးမိုးရေးအတွက် စာရွက်စာတမ်းများကို စနစ်တကျ လုံခြုံစွာ သိမ်းဆည်း ရမည်။"
                ),
                (
                    formalEngine,
                    "စာမေးပွဲခန်းတွင် ကျောင်းသားများ အေးဆေး နေရမည်။",
                    "စာမေးပွဲခန်းတွင် ကျောင်းသားများ တိတ်တဆိတ် နေရမည်။"
                ),
                (
                    formalEngine,
                    "လွှတ်တော်အစည်းအဝေးတွင် ဒီအကြောင်းအရာကို အကျယ်တဝင့် ဆွေးနွေးသည်။",
                    "လွှတ်တော်အစည်းအဝေးတွင် ယင်းအကြောင်းအရာကို အကျယ်တဝင့် ဆွေးနွေးသည်။"
                )
            };

            foreach (var (eng, raw, expectedClean) in repairSentences)
            {
                var issues = eng.CheckText(raw);
                Assert.NotEmpty(issues);

                string patched = raw;
                foreach (var iss in issues.OrderByDescending(i => i.StartIndex))
                {
                    Assert.NotEmpty(iss.Suggestions);
                    patched = patched.Substring(0, iss.StartIndex) +
                              iss.Suggestions[0].Replacement +
                              patched.Substring(iss.StartIndex + iss.Length);
                }

                Assert.Equal(
                    UnicodeNormalizer.NormalizeCanonicalOrder(expectedClean),
                    UnicodeNormalizer.NormalizeCanonicalOrder(patched));
            }
        }

        [Fact]
        public void V64B_AbyssalMythicMultiLayerTortureAndLexiconMorphoSyntax_Gate()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            Assert.True(File.Exists(dbPath), "Compiled mlc_thatpon.db must exist.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. Adversarial Mythic Evil-Twin Clean Controls (including superlative 'အ-[Adj]-ဆုံး', distributive 'တစ်-[X]-စီ', kiln 'မီးဖိုက', reciprocal 'အချင်းချင်း')
            (MyanmarProofingEngine Engine, string CleanInput)[] mythicCleanControls =
            {
                (formalEngine, "ထင်းမီးဖိုက မီးခိုးများ ထွက်လာသော်လည်း အိုးထဲမှ ဟင်းရည်မှာ မွှေးပျံ့နေသည်။"),
                (formalEngine, "အချင်းချင်း ရိုင်းပင်းကူညီခြင်းက လူမှုအသိုင်းအဝိုင်းကို စည်းလုံးစေသည်။"),
                (formalEngine, "တောင်ခြေရှိ ကျောက်နေရာကို ဘူမိဗေဒပညာရှင်များက အသေးစိတ် စစ်ဆေးကြသည်။"),
                (formalEngine, "ရိုးရာ ခေါက်ဆွဲပြုလုပ်ပုံ အဆင့်ဆင့်ကို သင်တန်းတွင် လက်တွေ့ပြသပေးသည်။"),
                (formalEngine, "မြို့အထွက် လမ်းဆုံတွင် ယာဉ်ထိန်းရဲတပ်ဖွဲ့က လုံခြုံရေး စစ်ဆေးလျက်ရှိသည်။"),
                (generalEngine, "ကျောင်းရှေ့က ကံ့ကော်ပင်ကြီးတွေက နွေဦးရာသီမှာ ပန်းရနံ့ မွှေးပျံ့စေတယ်။"),
                (formalEngine, "အစိုးရ အဖွဲ့အသီးသီးနှင့် ညှိနှိုင်း၍ အကြော့ဆုံး လက်ရာများကို တစ်လှည့်စီ ပြသထားသည်။")
            };

            foreach (var (eng, clean) in mythicCleanControls)
            {
                var fp = eng.CheckText(clean);
                Assert.True(fp.Count == 0, $"Mythic clean control falsely flagged '{clean}': {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            // 2. Abyssal Mythic 5-to-6x Simultaneous Cascading Multi-Layer Reconstruction in 1 Pass!
            (MyanmarProofingEngine Engine, string TortureInput, int ExpectedIssues, string ExpectedCleanOutput)[] abyssalSentences =
            {
                (
                    formalEngine,
                    "အစိုးရ အဖွဲ့က အခြေခံအဆောက်အဦများ တိုးတက်လာဖို့ လုအပ်သော စီမံကန်းများကို အကောင်အထည်ဖော်ဖို ညွှန်ကြားခဲ့သည် တိုင်းဒေသကြီးရုံးများသို့.",
                    6,
                    "အစိုးရအဖွဲ့က အခြေခံအဆောက်အဦများ တိုးတက်လာဖို့ လိုအပ်သော စီမံကိန်းများကို အကောင်အထည်ဖော်ဖို့ တိုင်းဒေသကြီးရုံးများသို့ ညွှန်ကြားခဲ့သည်။"
                ),
                (
                    generalEngine,
                    "ဒီမနက် အစ်ကိုက ဖိနဘ် နှစ်ယောက် ဝယ်ယူပီ မိသားစုအထွက် လက်ဆောင်ပေးခြင်တယ် လို့ ပြောတယ်။",
                    5,
                    "ဒီမနက် အစ်ကိုက ဖိနပ် နှစ်ရံ ဝယ်ယူပြီး မိသားစုအတွက် လက်ဆောင်ပေးချင်တယ် လို့ ပြောတယ်။"
                ),
                (
                    generalEngine,
                    "ပတ် ဝန်းကျင် ထိန်းသိမ်းစောင့်ရှောက်ချင်းက လူသားတွေက ဘေးကင်းလုံခြုံစေပြီး ကျန်းမာရေးကောင်းဖို အဓိကအချက်ပယ်။",
                    5,
                    "ပတ်ဝန်းကျင် ထိန်းသိမ်းစောင့်ရှောက်ခြင်းက လူသားတွေကို ဘေးကင်းလုံခြုံစေပြီး ကျန်းမာရေးကောင်းဖို့ အဓိကအချက်ပဲ။"
                ),
                (
                    formalEngine,
                    "ကျောင်း တိုက်များတွင် သတိပဋ္ဌာ တရားပွားများရန် အလည်အပဒ် လာရောက်သူများအား အကူ အညီ ပေးအပ်ကြောင်း ရေသားထားသည်။",
                    5,
                    "ကျောင်းတိုက်များတွင် သတိပဋ္ဌာန် တရားပွားများရန် အလည်အပတ် လာရောက်သူများအား အကူအညီ ပေးအပ်ကြောင်း ရေးသားထားသည်။"
                ),
                (
                    formalEngine,
                    "ပွန်ထမ်းများ၏ အရည် အချင်း ပိုမို ကျယ်ပြန်လာစေရန်နှင့် အခွန်ဆောင်းဖို့ ခွင့်မူချက် ထုတ်ပေးထားသည်။",
                    5,
                    "ဝန်ထမ်းများ၏ အရည်အချင်း ပိုမို ကျယ်ပြန့်လာစေရန်နှင့် အခွန်ဆောင်ဖို့ ခွင့်ပြုချက် ထုတ်ပေးထားသည်။"
                ),
                (
                    formalEngine,
                    "ယမန်နှစ်က စစ်တက္ ကသိုလ်မှ သုတေသီများသည် မှတ်ပုံ\u200Bတင် စာရင်းနှင့် ကေျာင်းသား အချက်အလတ်များကို ပေးပို့ခဲ့သည် ဗဟိုရုံးချုပ်သို့.",
                    6,
                    "ယမန်နှစ်က စစ်တက္ကသိုလ်မှ သုတေသီများသည် မှတ်ပုံတင် စာရင်းနှင့် ကျောင်းသား အချက်အလက်များကို ဗဟိုရုံးချုပ်သို့ ပေးပို့ခဲ့သည်။"
                )
            };

            foreach (var (eng, raw, expectedCount, expectedClean) in abyssalSentences)
            {
                var issues = eng.CheckText(raw);
                Assert.Equal(expectedCount, issues.Count);

                string patched = raw;
                foreach (var iss in issues.OrderByDescending(i => i.StartIndex))
                {
                    Assert.NotEmpty(iss.Suggestions);
                    patched = patched.Substring(0, iss.StartIndex) +
                              iss.Suggestions[0].Replacement +
                              patched.Substring(iss.StartIndex + iss.Length);
                }

                Assert.Equal(
                    UnicodeNormalizer.NormalizeCanonicalOrder(expectedClean),
                    UnicodeNormalizer.NormalizeCanonicalOrder(patched));
            }
        }

        [Fact]
        public void V65B_GovPortalB2GAuditShields_AndMultiCorpusPrecisionRecall_Gate()
        {
            string dbPath = TestPaths.EncryptedDbPath;
            Assert.True(File.Exists(dbPath), "Compiled mlc_thatpon.db must exist.");

            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);

            // 1. All 14 Live .gov.mm B2G Portal & Gazette Clean Controls + Literary/Pali/Foreign Coda Controls MUST emit ZERO false positives
            (MyanmarProofingEngine Engine, string CleanInput)[] govCleanControls =
            {
                (formalEngine, "အိန္ဒိယနိုင်ငံ၊ ချင်းနိုင်မြို့ (Chennai) ၏ အရှေ့တောင်ဘက် ရေမိုင် ၂၄၀ ခန့်အကွာ၌ မုန်တိုင်းငယ် ဗဟိုပြုနေသည်။"),
                (formalEngine, "ခရိုင်ကော်လိတ္တော်အရာရှိက မြေငှားဂရန် လျှောက်ထားမှုများကို ဥပဒေနှင့်အညီ စိစစ်ဆောင်ရွက်ပေးသည်။"),
                (formalEngine, "နယ်စပ်ကုန်သွယ်ရေးစခန်းတွင် ကားခ (ကုန်ကျစ်) နှင့် (ကုန်ပွ) နှုန်းထားများကို စနစ်တကျ သတ်မှတ်ထားသည်။"),
                (formalEngine, "ဈေးများအတွင်း အသားငါးများ၌ ရေထိုးခြင်း၊ လေထိုးခြင်းနှင့် ဆိုးဆေးသုံးစွဲခြင်း မပြုလုပ်ရန် စစ်ဆေးသည်။"),
                (formalEngine, "ပြည်သူများအားလုံး ကိုယ်စိတ်နှစ်ဖြာ ကျန်းမာချမ်းသာကြပါစေကြောင်း ဆုမွန်ကောင်း မေတ္တာ ပို့သအပ်ပါသည်။"),
                (formalEngine, "စုဒ္ဒသမ တိပိဋကဓရ တိပိဋကကောဝိဒ ရွေးချယ်ရေးစာမေးပွဲကြီးကို နေပြည်တော်၌ ကျင်းပသည်။"),
                (formalEngine, "သဒ္ဓမ္မနန္ဒပရိယတ္တိစာသင်တိုက် ဆရာတော်ကြီးအား လှူဖွယ်ဝတ္ထုပစ္စည်းများ ဆက်ကပ်လှူဒါန်းသည်။"),
                (formalEngine, "အခြေခံ သေနတ်ပစ်ခတ်ပုံကအစ စနစ်တကျ လေ့ကျင့်သင်ကြားပေးလျက်ရှိသည်။"),
                (formalEngine, "ကမ္ဘာအေးဘုရားလမ်းမပေါ်ရှိ မြန်မာပလာဇာရှေ့ ယာဉ်ကြောရှင်းလင်းရေး ဆောင်ရွက်ထားသည်။"),
                (formalEngine, "ဒီဂျစ်တယ်လ်တီထွင်ဆန်းသစ်မှုနှင့် အယ်ဂိုရီသမ် နည်းပညာဆိုင်ရာ ဆွေးနွေးပွဲသို့ တက်ရောက်သည်။"),
                (formalEngine, "(40th ACMF Capital Market Regulators Conference ... ဆက်ဖတ်ရန်"),
                (formalEngine, "“(၂၃) ကြိမ်မြောက် အာရှနှင့် ပစိဖိတ်ဒေသဆိုင်ရာ အာမခံညီလာခံ ... ဆက်ဖတ်ရန်"),
                (formalEngine, "“ဒေးဒေး” ကယားရိုးရာအထည်များ ထုတ်လုပ်ဖြန့်ဖြူးရေးလုပ်ငန်း ပြခန်းကို ကြည့်ရှုအားပေးသည်။"),
                (formalEngine, "ပြည်ထောင်စုနေ့ အခမ်းအနားတွင် တိုင်းကြီး၊ တိုင်းနေ၊ တိုင်းလိ ရိုးရာယဉ်ကျေးမှုအဖွဲ့များက ကပြဖျော်ဖြေကြသည်။"),
                (generalEngine, "ရောင်ဝ် နှင့် မာရထာအင်ပါယာ သမိုင်းအကြောင်း လေ့လာဖတ်ရှုသည်။")
            };

            foreach (var (eng, clean) in govCleanControls)
            {
                var fp = eng.CheckText(clean);
                Assert.True(fp.Count == 0, $"Gov B2G clean control falsely flagged '{clean}': {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            // 2. Authentic .gov.mm Multi-Error Gazette Sentences & V65 Case-Role / Collocation Repairs in 1 Pass
            (MyanmarProofingEngine Engine, string RawInput, int ExpectedIssues, string ExpectedCleanOutput)[] govAndV65Cases =
            {
                (
                    formalEngine,
                    "ချေးငွေသက်သေခံလက်မှတ်လုပ်ငန်း ကြီးကြပ်ရေးကော်မရှင်မှ ရန်ကုန်စတော့အိပ်ချိန်းရှိ အစုရှယ် ယာ ရောင်းဝယ်မှုများကို တပြေးညီ ကြီးကြပ်ဆောင်ရွက်လျှက်ရှိသည်။",
                    4,
                    "ချေးငွေသက်သေခံလက်မှတ်လုပ်ငန်း ကြီးကြပ်ရေးကော်မရှင်မှ ရန်ကုန်စတော့အိတ်ချိန်းရှိ အစုရှယ်ယာ ရောင်းဝယ်မှုများကို တစ်ပြေးညီ ကြီးကြပ်ဆောင်ရွက်လျက်ရှိသည်။"
                ),
                (
                    formalEngine,
                    "မြန်မာနိုင်ငံတော်ဗဟိုဘဏ် ဥက္ကဌ ဦးဆောင်သော အစည်း အဝေးတွင် အတိုးနှုန်း လျော့ချ ရေးမူဝါဒကို ကြေငြာ ခဲ့သည်။",
                    4,
                    "မြန်မာနိုင်ငံတော်ဗဟိုဘဏ် ဥက္ကဋ္ဌ ဦးဆောင်သော အစည်းအဝေးတွင် အတိုးနှုန်း လျှော့ချ ရေးမူဝါဒကို ကြေညာ ခဲ့သည်။"
                ),
                (
                    formalEngine,
                    "ရင်းနှီးမြုပ်နှံမှုနှင့် နိုင်ငံခြားစီးပွားဆက်သွယ်ရေး ဝန်ကြီး ဌာနက ဥပဒေ ပြဌာန်းချက်များနှင့်အညီ ခွင့်မူချက် ထုတ်ပေးသည်။",
                    4,
                    "ရင်းနှီးမြှုပ်နှံမှုနှင့် နိုင်ငံခြားစီးပွားဆက်သွယ်ရေး ဝန်ကြီးဌာနက ဥပဒေ ပြဋ္ဌာန်းချက်များနှင့်အညီ ခွင့်ပြုချက် ထုတ်ပေးသည်။"
                ),
                (
                    formalEngine,
                    "မြို့တော်စည်ပင်သာယာရေးကော်မတီက အဆောက်အဦး အသစ်များနှင့် လူသွား စင်္ကြန် များကို ကွင်းဆင်းကြည့်ရှု့ စစ်ဆေးခဲ့သည်။",
                    3,
                    "မြို့တော်စည်ပင်သာယာရေးကော်မတီက အဆောက်အဦ အသစ်များနှင့် လူသွား စင်္ကြံ များကို ကွင်းဆင်းကြည့်ရှု စစ်ဆေးခဲ့သည်။"
                ),
                (
                    generalEngine,
                    "စိုက်ပျိုးရေးကို မြန်မာ့စီးပွားရေးအတွက် အရေးပါပြီး ခုနှစ်နှစ်အရွယ် ကလေးတွေလဲရှိပါတယ်။",
                    3,
                    "စိုက်ပျိုးရေးဟာ မြန်မာ့စီးပွားရေးအတွက် အရေးပါပြီး ခုနစ်နှစ်အရွယ် ကလေးတွေလည်းရှိပါတယ်။"
                )
            };

            foreach (var (eng, raw, expectedCount, expectedClean) in govAndV65Cases)
            {
                var issues = eng.CheckText(raw);
                Assert.Equal(expectedCount, issues.Count);

                string patched = raw;
                foreach (var iss in issues.OrderByDescending(i => i.StartIndex))
                {
                    Assert.NotEmpty(iss.Suggestions);
                    patched = patched.Substring(0, iss.StartIndex) +
                              iss.Suggestions[0].Replacement +
                              patched.Substring(iss.StartIndex + iss.Length);
                }

                Assert.Equal(
                    UnicodeNormalizer.NormalizeCanonicalOrder(expectedClean),
                    UnicodeNormalizer.NormalizeCanonicalOrder(patched));
            }
        }

        [Fact]
        public void V66B_OptimisedTop1RankingCoalescerAndLatencyChampionship_Gate()
        {
            var repo = new EncryptedDictionaryRepository(TestPaths.EncryptedDbPath);
            var trie = repo.LoadIntoTrie("MM-GOV-THATPON-SQLCIPHER-2026!");
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);

            // 1. Six V66 Evil-Twin Clean Controls (Zero False Positives)
            (MyanmarProofingEngine Engine, string CleanSentence)[] v66CleanControls =
            {
                (formalEngine, "လက်ငင်းငွေမပါဘဲ အရစ်ကျစနစ်ဖြင့် စနစ်တကျ ပေးချေနိုင်ပါသည်။"),
                (generalEngine, "ကိုယ်ပိုင်စီးပွားရေးလုပ်ငန်းတစ်ခုစလုပ်ဖို့ သေချာစွာ ပြင်ဆင်နေပါတယ်။"),
                (formalEngine, "ဧည့်သည်တော်များအတွက် စားဖွယ်မုန့်တို့ကိုထုတ်၏။"),
                (formalEngine, "မဂမ်နှင့် မိသားစုဝင်များသည် နံနက်တိုင်း ပန်းခြံ၌ ဂျောဂင်းထွက်ကြသည်။"),
                (generalEngine, "“ကျမလည်း ဒီသင်တန်းကို တက်ရောက်ချင်ပါတယ်” ဟု ပြောကြားသည်။"),
                (generalEngine, "ဘယ်လို ဖွင့်ပေးတာလည်း ဆိုတာတော့ သေချာမသိသေးပါဘူး။")
            };

            foreach (var (eng, clean) in v66CleanControls)
            {
                var fp = eng.CheckText(clean);
                Assert.True(fp.Count == 0, $"V66 clean control falsely flagged '{clean}': {string.Join(", ", fp.Select(x => x.OriginalText + "->" + (x.Suggestions.FirstOrDefault()?.Replacement ?? "")))}");
            }

            // 2. Whole-Stem Coalescer, Context-Steered Top-1 Reranker & Bidirectional Register Harmony in 1 Pass
            (MyanmarProofingEngine Engine, string RawInput, int ExpectedIssues, string ExpectedCleanOutput)[] v66ChampionshipCases =
            {
                (
                    formalEngine,
                    "ကျောင်းဝင်းအတွင်းရှိ ကေါင်းလောင်းသံကြားလျှင် ကျောင်းသားများ အစည်း အဝေးခန်းမသို့ စနစ် တကျ ဝင်ရောက်ကြသည်။",
                    3,
                    "ကျောင်းဝင်းအတွင်းရှိ ခေါင်းလောင်းသံကြားလျှင် ကျောင်းသားများ အစည်းအဝေးခန်းမသို့ စနစ်တကျ ဝင်ရောက်ကြသည်။"
                ),
                (
                    generalEngine,
                    "သဘာဝဘေးအန္တရာယ် တားသိးနိုင်ခွင့်နှင့် ပတ်ဝန်းကျင် ထိးသိမ်းဖို့က အားလုံးအတွက် အရေးကြီးတယ်။",
                    2,
                    "သဘာဝဘေးအန္တရာယ် တားဆီးနိုင်ခွင့်နှင့် ပတ်ဝန်းကျင် ထိန်းသိမ်းဖို့က အားလုံးအတွက် အရေးကြီးတယ်။"
                ),
                (
                    generalEngine,
                    "တရားစီရိန်ရေးကဏ္ဍမှာ အချက်အလက်တွေကို နှိုင်းယှင်းပြီး သတင်းထိန်ချိန်ထားခြင်း မပြုရပါ။",
                    3,
                    "တရားစီရင်ရေးကဏ္ဍမှာ အချက်အလက်တွေကို နှိုင်းယှဉ်ပြီး သတင်းထိမ်ချန်ထားခြင်း မပြုရပါ။"
                ),
                (
                    generalEngine,
                    "ဒီန့ဲ မနက်ပိုင်း လေယာဉခရီးစဉ်ဖြင့် ရှေးဟောင်းဒေသသို့ သွားရောက်နည်ပတ်ကြတယ်။",
                    3,
                    "ဒီနေ့ မနက်ပိုင်း လေယာဉ်ခရီးစဉ်ဖြင့် ရှေးဟောင်းဒေသသို့ သွားရောက်လည်ပတ်ကြတယ်။"
                ),
                (
                    formalEngine,
                    "ပညာရေ ဝန်ကြီး ဌာန၏ ထုတ်ပြန်ချက်အရ ကော်မရှင် ဥက္ကဌ သည် အရေးပါတဲ့ကဏ္တတစ်ခုဖြစ်သော သုတေသနလုပ်ငန်းစင်များကို ကြေငြာ ခဲ့သည်.",
                    7,
                    "ပညာရေး ဝန်ကြီးဌာန၏ ထုတ်ပြန်ချက်အရ ကော်မရှင် ဥက္ကဋ္ဌ သည် အရေးပါတဲ့ကဏ္ဍတစ်ခုဖြစ်သော သုတေသနလုပ်ငန်းစဉ်များကို ကြေညာ ခဲ့သည်။"
                )
            };

            foreach (var (eng, raw, expectedCount, expectedClean) in v66ChampionshipCases)
            {
                var issues = eng.CheckText(raw);
                Assert.Equal(expectedCount, issues.Count);

                string patched = raw;
                foreach (var iss in issues.OrderByDescending(i => i.StartIndex))
                {
                    Assert.NotEmpty(iss.Suggestions);
                    patched = patched.Substring(0, iss.StartIndex) +
                              iss.Suggestions[0].Replacement +
                              patched.Substring(iss.StartIndex + iss.Length);
                }

                Assert.Equal(
                    UnicodeNormalizer.NormalizeCanonicalOrder(expectedClean),
                    UnicodeNormalizer.NormalizeCanonicalOrder(patched));
            }

            // 3. Optimised Uncached Latency Gate (mean < 35 ms per multi-error sentence)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int r = 0; r < v66ChampionshipCases.Length; r++)
            {
                _ = v66ChampionshipCases[r].Engine.CheckText(v66ChampionshipCases[r].RawInput, cacheParagraph: false);
            }
            sw.Stop();
            double meanUncachedMs = sw.Elapsed.TotalMilliseconds / v66ChampionshipCases.Length;
            Assert.True(meanUncachedMs < 35.0, $"Expected fast < 35 ms mean uncached multi-error sentence latency, got {meanUncachedMs:F2} ms.");
        }
    }
}
