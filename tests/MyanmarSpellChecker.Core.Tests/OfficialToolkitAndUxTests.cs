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
    }
}
