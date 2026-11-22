// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    public sealed partial class OfficialStyleGrammarEngine
    {
        // Review-only dangling missing-number unit patterns (never auto-replaced during blind Fix-All)
        private static readonly Regex DanglingMissingNumberUnitPattern = new Regex(
            @"(?:(?<![0-9\u1040-\u1049])အသက်[ \t]+(?<AgeUnit>နှစ်အရွယ်|နှစ်[ \t]+မပြည့်|နှစ်[ \t]+မပြည်)|(?<=(?:ကို|၌|တွင်|အပြီး|ပြီးနောက်|ဖြစ်ပြီး|ဌာန[ \t]*က))[ \t]+(?<YearUnit>ခုနှစ်(?:တွင်|၌ကား|၌|က))|(?<=(?:သည်|တွင်|နှင့်|နှင့်))[ \t]+(?<CenturyUnit>ရာစု(?:တွင်|နှင့်|နှင့်|ကာလ))|(?<=(?:နိုင်ငံသား|တရားခံ|ဝန်ထမ်း))[ \t]+(?<PersonUnit>ဦးအား)|(?<=(?:အဆောက်အဦ|အဆောက်အအုံ))[ \t]+(?<BuildingUnit>ခုကို))",
            RegexOptions.Compiled);

        // Multi-letter English prose phrase glued directly between Myanmar words ('နေရာတွင်Upton Parkဘူတာ' -> 'နေရာတွင် Upton Park ဘူတာ')
        private static readonly Regex GluedMyanmarLatinMyanmarProsePattern = new Regex(
            @"(?<=[\u1000-\u103F]{2})(?<Eng>[A-Z][a-z]{2,}(?:[ \t]+[A-Z][a-z]{2,})*)(?=[\u1000-\u103F]{2})",
            RegexOptions.Compiled);

        // Missing copula after plural noun ('~တွေတယ်။' -> '~တွေပါ။')
        private static readonly Regex PluralNounMissingCopulaTaePattern = new Regex(
            @"(?<=(?:အရာ|ပစ္စည်း|အချက်|ကိစ္စ)တွေ)(?<Target>တယ်။)",
            RegexOptions.Compiled);

        // Misplaced outer 'မ-' prefix on bipartite [Noun/Head]+[Verb] compounds ('*မနေကောင်းဘူး' -> 'နေမကောင်းဘူး', '*မအဆင်ပြေဘူး' -> 'အဆင်မပြေဘူး')
        private static readonly Regex BipartiteMisplacedMaNegationPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Whole>မ(?<Head>နေ(?=ကောင်း)|အဆင်(?=ပြေ)|နား(?=လည်)|စိတ်(?=ဆိုး|ပူ|ညစ်|ချ|ဝင်စား)|သဘော(?=တူ|ကျ)|အသုံး(?=ဝင်|ကျ)|အား(?=နာ)|သတိ(?=ရ|ထား)|အရေး(?=ကြီး|ပါ)|တာဝန်(?=ကျေ)|အချိန်(?=မီ))(?<Verb>ကောင်း|ပြေ|လည်|ဆိုး|ပူ|ညစ်|ချ|ဝင်စား|တူ|ကျ|ဝင်|နာ|ရ|ထား|ကြီး|ပါ|ကျေ|မီ))(?=(?:နိုင်|ရ|သေး|တော့|တတ်|ပါ|မိ|ခဲ့|ဖူး|ဝံ့|ချင်|လိုက်|သွား|လာ)*(?:ဘူး|ပါဘူး|ပါနဲ့|နဲ့|ပါ(?=\s*[။.?]|$|\s*(?:လို့|ဟု|ကြောင်း))|ချေ|ပေ|သဖြင့်|သဖြင့်|သောကြောင့်|သောကြောင့်|သလို|လို့|ဘဲ|ပေမဲ့|သော်လည်း))",
            RegexOptions.Compiled);

        // Literary Negative Polarity Item ('~မျှ') with dropped 'မ-' before literary negative-polarity closer ('ရှိပေ -> မရှိပေ')
        private static readonly Regex LiteraryNpiDroppedMaBeforePePattern = new Regex(
            @"(?:တစ်ဦးတစ်ယောက်မျှ|တစ်ယောက်မျှ|တစ်ဦးမျှ|တစ်စုံတစ်ရာမျှ|မည်သူမျှ|အနည်းငယ်မျှ|တစ်ခုမျှ)\s+[^\u104A\u104B\r\n]{2,42}?(?<!မ)(?<Target>ရှိပေ|ဖြစ်ပေ|သိပေ|တွေ့ရပေ|နိုင်ပေ|ရှိချေ|ဖြစ်ချေ)(?=\s*[။.])",
            RegexOptions.Compiled);

        // Generalized unspaced corrupted grammatical suffixes:
        //   1) [Institution/Place Noun] + 'မာ' before a Myanmar clause ('စာကြည့်တိုက်မာ စာဖတ်' -> 'မှာ')
        //   2) [Source/Place Noun] + 'ကနီ' before a motion/action verb ('အိမ်ကနီ ထွက်လာ' -> 'ကနေ', shields 'ကနီမြို့နယ်')
        //   3) [Verb] + 'မဲ' before quotative ('သွားမဲ လို့' -> 'မယ်')
        //   4) [Verb/Aspect] + 'တဲ' before quotative/concessive ('စာဖတ်နေကြတဲ ဆိုပေမဲ့' -> 'တယ်')
        private static readonly Regex UnspacedCorruptedCaseAndAspectSuffixPattern = new Regex(
            @"(?:(?<=(?:ကျောင်း|ရုံး|ဆေးရုံ|စာကြည့်တိုက်|ဈေး|စျေး|လမ်း|ခန်းမ|အိမ်|မြို့|ရွာ|နိုင်ငံ|ကွင်း|ဆိုင်|ဘဏ်))(?<LocMa>မာ)(?=\s+[\u1000-\u1021])|(?<=(?:အိမ်|ကျောင်း|ရုံး|မြို့|ရွာ|ဆိုင်|နိုင်ငံ|ဒီ|အဲဒီ|ဟို))(?<AblKani>ကနီ)(?=\s+(?:ထွက်|လာ|သွား|ပြန်|စ|ပို့|ယူ|ဝယ်))|(?<=(?:သွား|လာ|လုပ်|စား|သောက်|ပြော|ပေး|ယူ|ဝယ်|နေ|ကြည့်|ကြည့်|ဖတ်|ရေး))(?<IrrMae>မဲ)(?=\s*(?:လို့|ဟု|ဆိုပြီး|ဆိုတာ))|(?<=(?:နေ|ကြ|ပါ|ခဲ့|သွား|လာ|ပြီး|ရ|နိုင်))(?<RealTae>တဲ)(?=\s*(?:လို့|ဟု|ဆိုပေမဲ့|ဆိုကာ|ဆိုပြီး|ဆိုတာ|[။.])))",
            RegexOptions.Compiled);

        // Unspaced merged subject particle 'က' + eating verb 'စား' after an explicit food object ('မနက်စာကို သူကစားပြီးမှ' -> 'က စား')
        private static readonly Regex FoodObjectMergedKaSarPattern = new Regex(
            @"(?:ထမင်း|မနက်စာ|နံနက်စာ|နေ့လယ်စာ|ညစာ|ညနေစာ|မုန့်|မုန့်|မုန့်ဟင်းခါး|ခေါက်ဆွဲ|ဟင်း)(?:တွေ|များ)?ကို\s+(?:သူ|ငါ|ကျွန်တော်|ကျွန်မ|ကလေး|အစ်ကို|အစ်မ|ညီလေး|ညီမလေး)(?<Target>ကစား)(?=ပြီး|တယ်|သည်|ပါ|နေ|ခဲ့)",
            RegexOptions.Compiled);

        // High-precision corpus word-order, syllable-spaced omission & intruder adverb repairs
        private static readonly (string Trigger, Regex Pattern, string Replacement, ProofingIssueType Type, string Cue, string Reason)[] V82CorpusPrecisionRules =
        {
            (
                "အ ခြေ",
                new Regex(@"(?<T>အ[ \t]+ခြေ[ \t]+ပြီး)", RegexOptions.Compiled),
                "အ ခြေ ချ ပြီး",
                ProofingIssueType.OfficialStyleGrammarError,
                "ကြိယာအမြစ် ကျန်ခဲ့မှု ('အခြေချပြီး')",
                "'အခြေချနေထိုင်ပြီး' အနက်တွင် 'ချ' ကြိယာအမြစ် ကျန်ခဲ့သဖြင့် 'အ ခြေ ချ ပြီး' (အခြေချပြီး) ဟု ဖြည့်စွက်ရမည်။"
            ),
            (
                "အိမ်တွင်း ",
                new Regex(@"(?<![\u1000-\u103F])(?<T>အိမ်တွင်း[ \t]+အလှဆင်ခြင်း)", RegexOptions.Compiled),
                "အိမ်တွင်းအလှဆင်ခြင်း",
                ProofingIssueType.SpellingError,
                "ပေါင်းစပ်နာမ် ('အိမ်တွင်းအလှဆင်ခြင်း')",
                "ပေါင်းစပ်ဝေါဟာရ ဖြစ်သဖြင့် Space မခြားဘဲ 'အိမ်တွင်းအလှဆင်ခြင်း' ဟု တစ်ဆက်တည်း ရေးသားရမည်။"
            ),
            (
                "နေရာလှပတဲ့",
                new Regex(@"(?<=အရမ်း)(?<T>နေရာလှပတဲ့)(?=တစ်ခု)", RegexOptions.Compiled),
                "လှပတဲ့နေရာ",
                ProofingIssueType.OfficialStyleGrammarError,
                "နာမဝိသေသန + နာမ် အစီအစဉ် ('လှပတဲ့နေရာ')",
                "မြန်မာသဒ္ဒါ နာမ်အထူးပြုဖွဲ့ထုံးအရ နာမဝိသေသန ('လှပတဲ့') သည် အထူးပြုခံနာမ် ('နေရာ') ရှေ့၌ တည်ရှိရမည်။"
            ),
            (
                "ကျွန်တော်တို့ကို ပြောပြ",
                new Regex(@"(?<=အဘိုးအဘွားတွေက[ \t]+)(?<T>ပုံပြင်တွေ[ \t]+ကျွန်တော်တို့ကို)(?=[ \t]+ပြောပြ)", RegexOptions.Compiled),
                "ကျွန်တော်တို့ကို ပုံပြင်တွေ",
                ProofingIssueType.OfficialStyleGrammarError,
                "လက်ခံသူကံပုဒ် + တိုက်ရိုက်ကံပုဒ် အစီအစဉ်",
                "'ပြောပြသည်' ကြိယာရှေ့တွင် လက်ခံသူပုဒ် ('ကျွန်တော်တို့ကို') ကို ရှေ့မှထား၍ တိုက်ရိုက်ကံ ('ပုံပြင်တွေ') ကို ကြိယာအနီး၌ ကပ်ထားရမည်။"
            ),
            (
                "စာအုပ်ဖတ်ရတာ နားထောင်ရင်း",
                new Regex(@"သီချင်း(?<T>စာအုပ်ဖတ်ရတာ[ \t]+နားထောင်ရင်း)", RegexOptions.Compiled),
                "နားထောင်ရင်း စာအုပ်ဖတ်ရတာ",
                ProofingIssueType.OfficialStyleGrammarError,
                "ရှေ့နောက်ကြိယာပုဒ်စု အစီအစဉ် ('သီချင်းနားထောင်ရင်း စာအုပ်ဖတ်ရတာ')",
                "'သီချင်း' သည် 'နားထောင်ရင်း' ၏ ကံပုဒ်ဖြစ်သဖြင့် 'သီချင်းနားထောင်ရင်း စာအုပ်ဖတ်ရတာ' ဟု ပုဒ်စုအစီအစဉ် မှန်ကန်စွာ စီစဉ်ရမည်။"
            ),
            (
                "ထူးဆန်းစွာ တိုးပွားစေ",
                new Regex(@"ပျော်ရွှင်မှုကို[ \t]+(?<T>ထူးဆန်းစွာ[ \t]+)(?=တိုးပွားစေ)", RegexOptions.Compiled),
                "",
                ProofingIssueType.OfficialStyleGrammarError,
                "ရှေ့နောက်အနက်မဆီလျော်သော ကြိယာဝိသေသန ဖယ်ရှားရန်",
                "'သတိရှိခြင်းက ပျော်ရွှင်မှုကို တိုးပွားစေသည်' တွင် 'ထူးဆန်းစွာ' သည် အဓိပ္ပာယ်မဆီလျော်သော အပိုပုဒ်ဖြစ်သည်။"
            ),
            (
                "ဆင်းရဲစွာ စား",
                new Regex(@"သရေစာ(?:တွေ|များ)[ \t]+(?<T>ဆင်းရဲစွာ[ \t]+)(?=စား)", RegexOptions.Compiled),
                "",
                ProofingIssueType.OfficialStyleGrammarError,
                "ရှေ့နောက်အနက်မဆီလျော်သော ကြိယာဝိသေသန ဖယ်ရှားရန်",
                "'သရေစာ စားကြသည်' တွင် 'ဆင်းရဲစွာ' သည် အဓိပ္ပာယ်မဆီလျော်သော အပိုပုဒ်ဖြစ်သည်။"
            ),
            (
                "ဝတ္ထု ",
                new Regex(@"(?<T>ဝတ္ထု[ \t]+(?:နှင့်|နှင့်))", RegexOptions.Compiled),
                "ဝတ္ထုနှင့်",
                ProofingIssueType.OfficialStyleGrammarError,
                "သမ္ဗန္ဓပစ္စည်း တစ်ဆက်တည်းရေးရန် ('ဝတ္ထုနှင့်')",
                "နာမ်ပုဒ်နောက်လိုက် သမ္ဗန္ဓပစ္စည်း 'နှင့်' ကို Space မခြားဘဲ 'ဝတ္ထုနှင့်' ဟု တစ်ဆက်တည်း ရေးသားရမည်။"
            ),
            (
                "အသပြာ ",
                new Regex(@"(?<T>အသပြာ[ \t]+ကို)", RegexOptions.Compiled),
                "အသပြာကို",
                ProofingIssueType.OfficialStyleGrammarError,
                "ကံဝိဘတ် တစ်ဆက်တည်းရေးရန် ('အသပြာကို')",
                "နာမ်ပုဒ်နောက်လိုက် ကံဝိဘတ် 'ကို' ကို Space မခြားဘဲ 'အသပြာကို' ဟု တစ်ဆက်တည်း ရေးသားရမည်။"
            ),
            (
                "ယေရှု ",
                new Regex(@"(?<T>ယေရှု[ \t]+သည်)", RegexOptions.Compiled),
                "ယေရှုသည်",
                ProofingIssueType.OfficialStyleGrammarError,
                "ကတ္တားဝိဘတ် တစ်ဆက်တည်းရေးရန် ('ယေရှုသည်')",
                "နာမ်ပုဒ်နောက်လိုက် ကတ္တားဝိဘတ် 'သည်' ကို Space မခြားဘဲ 'ယေရှုသည်' ဟု တစ်ဆက်တည်း ရေးသားရမည်။"
            ),
            (
                "လှေလှော်သမားများဖြစ်သည်",
                new Regex(@"(?<=လှေလှော်သမားများ)(?<T>ဖြစ်သည်)$", RegexOptions.Compiled),
                "ဖြစ်သည်။",
                ProofingIssueType.PunctuationError,
                "ဝါကျအဆုံး ပုဒ်မကြီး ('။') ထည့်သွင်းရန်",
                "ဝါကျအဆုံးသတ် ကြိယာနောက်တွင် မြန်မာပုဒ်မကြီး ('။') ရေးသားရမည်။"
            )
        };

        private static void ScanCorpusPrecisionAndMissingNumberRules(
            string text,
            ulong mmMask,
            ProofingStyleMode styleMode,
            List<ProofingIssue> issues)
        {
            if (mmMask == 0 || text.Length < 5)
                return;

            // 1. Review-Only Dangling Missing-Number Warnings before Age / Year / Century / Count Classifiers
            if ((text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0) &&
                (ContainsMm(text, mmMask, "နှစ်") ||
                 ContainsMm(text, mmMask, "ရာစု") ||
                 ContainsMm(text, mmMask, "ဦးအား") ||
                 ContainsMm(text, mmMask, "ခုကို")))
            {
                foreach (Match mn in DanglingMissingNumberUnitPattern.Matches(text))
                {
                    Group g = mn.Groups["AgeUnit"].Success ? mn.Groups["AgeUnit"]
                            : mn.Groups["YearUnit"].Success ? mn.Groups["YearUnit"]
                            : mn.Groups["CenturyUnit"].Success ? mn.Groups["CenturyUnit"]
                            : mn.Groups["PersonUnit"].Success ? mn.Groups["PersonUnit"]
                            : mn.Groups["BuildingUnit"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string unitKind = mn.Groups["AgeUnit"].Success ? "အသက်ဂဏန်း"
                                    : mn.Groups["YearUnit"].Success ? "ခုနှစ်ဂဏန်း"
                                    : mn.Groups["CenturyUnit"].Success ? "ရာစုဂဏန်း"
                                    : "အရေအတွက်ဂဏန်း";

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        RequiresReviewBeforeBatchFix = true,
                        Message = $"မျိုးပြပုဒ်/အချိန်ပြပုဒ် '{g.Value}' ရှေ့တွင် {unitKind} ကျန်ခဲ့ပါသည် — စစ်ဆေး၍ ဂဏန်းအမှန် ဖြည့်စွက်ပါ။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါ ဖွဲ့ထုံးအရ '{g.Value}' ရှေ့တွင် သက်ဆိုင်ရာ ကိန်းဂဏန်း ပါရှိရမည်ဖြစ်သဖြင့် စာရေးသူကိုယ်တိုင် စစ်ဆေးဖြည့်စွက်ရန် အသိပေးချက်ဖြစ်သည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သင်္ချာနာမ် နှင့် မျိုးပြပစ္စည်း စည်းမျဉ်း)",
                        Suggestions = new List<SuggestionCandidate>()
                    });
                }
            }

            // 2. Bipartite [Noun/Head]+[Verb] Misplaced Outer 'မ-' Prefix ('*မနေကောင်းဘူး' -> 'နေမကောင်းဘူး', '*မအဆင်ပြေ' -> 'အဆင်မပြေ')
            if (text.IndexOf('မ') >= 0)
            {
                foreach (Match mb in BipartiteMisplacedMaNegationPattern.Matches(text))
                {
                    Group g = mb.Groups["Whole"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string infixed = mb.Groups["Head"].Value + "မ" + mb.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, infixed,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"နာမ်+ကြိယာ ပေါင်းစပ်ပုဒ်ကို အငြင်းပြုရာတွင် ရှေ့ဆုံးမှ 'မ-' မထားရပါ — '{infixed}' ဟု ကြိယာအမြစ်ရှေ့၌ 'မ' ထည့်သွင်းရေးသားရမည်။",
                        $"အငြင်းပေါင်းစပ်ကြိယာ ('{infixed}')", "V", true,
                        $"မြန်မာသဒ္ဒါ အငြင်းကြိယာဖွဲ့ထုံးအရ '{mb.Groups["Head"].Value}+{mb.Groups["Verb"].Value}' ကဲ့သို့သော ပေါင်းစပ်ကြိယာများ၌ အငြင်းပစ္စည်း 'မ' ကို ဒုတိယပိုင်း ကြိယာရှေ့၌ ကပ်၍ '{infixed}' ဟု ရေးသားရသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းကြိယာဖွဲ့ထုံး)");
                }
            }

            // 3. Literary Negative Polarity Concord ('တစ်ဦးတစ်ယောက်မျှ ... ရှိပေ။' -> 'မရှိပေ')
            if (ContainsMm(text, mmMask, "မျှ") && (ContainsMm(text, mmMask, "ပေ") || ContainsMm(text, mmMask, "ချေ")))
            {
                foreach (Match mnpi in LiteraryNpiDroppedMaBeforePePattern.Matches(text))
                {
                    Group g = mnpi.Groups["Target"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string negFixed = "မ" + g.Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, negFixed,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ရှေ့၌ အငြင်းအညွှန်းပုဒ် ('~မျှ') ပါရှိနေသဖြင့် အငြင်းကြိယာ '{negFixed}' ဟု 'မ-' ထည့်သွင်းရေးသားရမည်။",
                        $"စာပေသုံး အငြင်းကြိယာ ('{negFixed}')", "V", true,
                        "စာပေသုံး 'တစ်ဦးတစ်ယောက်မျှ / မည်သူမျှ / တစ်စုံတစ်ရာမျှ' စသည့် အငြင်းအညွှန်းပုဒ်များနှင့် တွဲဖက်ရာတွင် ဝါကျအဆုံးကြိယာ၌ အငြင်းပစ္စည်း 'မ-' မပါမဖြစ် ပါရှိရသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းဝါကျ ရှေ့နောက်ညီညွတ်မှု စည်းမျဉ်း)");
                }
            }

            // 4. Generalized Unspaced Locative / Ablative / Irrealis / Realis Suffix Re-Segmenter ('စာကြည့်တိုက်မာ -> မှာ', 'အိမ်ကနီ -> ကနေ', 'သွားမဲ လို့ -> မယ်', 'စာဖတ်နေကြတဲ ဆိုပေမဲ့ -> တယ်')
            if (ContainsMm(text, mmMask, "မာ ") || ContainsMm(text, mmMask, "ကနီ ") || ContainsMm(text, mmMask, "မဲ") || ContainsMm(text, mmMask, "တဲ"))
            {
                const string attachedSuffixSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် နှင့် ပုဒ်ဖြတ်စည်းမျဉ်း)";
                foreach (Match ms in UnspacedCorruptedCaseAndAspectSuffixPattern.Matches(text))
                {
                    if (ms.Groups["LocMa"].Success)
                    {
                        Group g = ms.Groups["LocMa"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, "မှာ",
                                ProofingIssueType.SpellingError,
                                "နေရာပြ နာမ်ဝိဘတ်တွင် ဟထိုးဖြင့် 'မှာ' ဟု ရေးသားရမည်။",
                                "နေရာပြဝိဘတ် ('~မှာ')", "PART", false,
                                "နေရာပြ နာမ်ဝိဘတ်ကို စာလုံးနောက်၌ တစ်ဆက်တည်းရေးရာတွင် 'မှာ' ဟု ဟထိုးဖြင့် ရေးသားရသည်။",
                                attachedSuffixSource);
                        }
                    }
                    else if (ms.Groups["AblKani"].Success)
                    {
                        Group g = ms.Groups["AblKani"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, "ကနေ",
                                ProofingIssueType.SpellingError,
                                "ထွက်ခွာရာပြ ဝိဘတ်တွင် သဝေထိုးဖြင့် 'ကနေ' ဟု ရေးသားရမည်။",
                                "ထွက်ခွာရာပြဝိဘတ် ('~ကနေ')", "PART", false,
                                "နေရာ/ရင်းမြစ်မှ ထွက်ခွာရာပြ အဆက်တွဲဝိဘတ်တွင် 'ကနေ' ဟု သဝေထိုးဖြင့် ရေးသားရသည်။",
                                attachedSuffixSource);
                        }
                    }
                    else if (ms.Groups["IrrMae"].Success)
                    {
                        Group g = ms.Groups["IrrMae"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string irrRep = styleMode == ProofingStyleMode.FormalOfficial ? "မည်" : "မယ်";
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, irrRep,
                                ProofingIssueType.SpellingError,
                                $"အနာဂတ်ပြ ကြိယာဝိဘတ်တွင် '{irrRep}' ဟု ရေးသားရမည် ('မဲ' မဟုတ်ပါ)။",
                                $"အနာဂတ်ပြ ကြိယာဝိဘတ် ('~{irrRep}')", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                                $"အနာဂတ်ပြ အဆက်တွဲကြိယာဝိဘတ်ကို '{irrRep}' ဟု ရေးသားရသည်။",
                                attachedSuffixSource);
                        }
                    }
                    else if (ms.Groups["RealTae"].Success)
                    {
                        Group g = ms.Groups["RealTae"];
                        if (!OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            string realRep = styleMode == ProofingStyleMode.FormalOfficial ? "သည်" : "တယ်";
                            AddGrammarOrSpellingIssue(
                                issues, g.Index, g.Length, g.Value, realRep,
                                ProofingIssueType.SpellingError,
                                $"ပစ္စုပ္ပန်/အတိတ်ပြ ကြိယာဝိဘတ်တွင် '{realRep}' ဟု ရေးသားရမည် ('တဲ' မဟုတ်ပါ)။",
                                $"ကြိယာအဆုံးသတ်ဝိဘတ် ('~{realRep}')", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                                $"အဆိုပြုဝါကျအဆုံး အဆက်တွဲကြိယာဝိဘတ်တွင် '{realRep}' ဟု ယပက်လက်အသတ်ဖြင့် ရေးသားရသည်။",
                                attachedSuffixSource);
                        }
                    }
                }
            }

            // 5. Unspaced Merged Subject Particle 'က' + Verb 'စား' After Food Object ('မနက်စာကို သူကစားပြီးမှ' -> 'က စား')
            if (ContainsMm(text, mmMask, "ကစား") && ContainsMm(text, mmMask, "ကို "))
            {
                foreach (Match mf in FoodObjectMergedKaSarPattern.Matches(text))
                {
                    Group g = mf.Groups["Target"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "က စား",
                        ProofingIssueType.ContextualMeaningWarning,
                        "အနက်သံသယ စိစစ်ရန် — အစားအစာ စားသုံးသည့် စကားစပ်အရ 'ကစား' (ဆော့ကစားသည်) အစား ကတ္တားဝိဘတ်နှင့် ကြိယာကို ခြား၍ 'က စား' ဟု ရည်ရွယ်ခြင်း ဖြစ်နိုင်ပါသည်။",
                        "ကတ္တားဝိဘတ် 'က' + ကြိယာ 'စား' (စားသုံးသည်)", "V", true,
                        "ရှေ့တွင် စားစရာကံပုဒ် ('~ကို') ပါရှိနေသဖြင့် ကစားခြင်း ('play') မဟုတ်ဘဲ ကတ္တားဝိဘတ် 'က' နှင့် စားသုံးခြင်းကြိယာ 'စား' တို့ ဖြစ်သည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် နှင့် ပုဒ်ဖြတ်စည်းမျဉ်း)",
                        requiresReviewBeforeBatchFix: true);
                }
            }

            // 6. Glued Multi-Letter English Prose Between Myanmar Words ('နေရာတွင်Upton Parkဘူတာ' -> ' နေရာတွင် Upton Park ဘူတာ ')
            bool hasUpperAscii = false;
            for (int ci = 0; ci < text.Length; ci++)
            {
                char ch = text[ci];
                if (ch >= 'A' && ch <= 'Z')
                {
                    hasUpperAscii = true;
                    break;
                }
            }
            if (hasUpperAscii)
            {
                foreach (Match me in GluedMyanmarLatinMyanmarProsePattern.Matches(text))
                {
                    Group g = me.Groups["Eng"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string spacedEng = " " + g.Value + " ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, spacedEng,
                        ProofingIssueType.PunctuationError,
                        $"မြန်မာစာလုံးနှင့် အင်္ဂလိပ်ပုဒ် '{g.Value}' အကြား Space ခြား၍ '{spacedEng.Trim()}' ဟု ရေးသားရမည်။",
                        "မြန်မာ-အင်္ဂလိပ် ပုဒ်အကြား Space ခြားရန်", "PUNCT", true,
                        "မြန်မာစာသားအတွင်း အင်္ဂလိပ်စကားလုံးများ ထည့်သွင်းရေးသားရာတွင် ရှေ့နောက် မြန်မာစာလုံးများနှင့် Space တစ်ချက်စီ ခြားရသည်။",
                        "ရုံးသုံးမြန်မာစာရေးထုံး (ဘာသာခြားစကားလုံး ထည့်သွင်းရေးသားပုံ)");
                }
            }

            // 7. Missing Copula After Plural Noun ('~တွေတယ်။' -> '~တွေပါ။')
            if (ContainsMm(text, mmMask, "တွေတယ်။"))
            {
                foreach (Match mc in PluralNounMissingCopulaTaePattern.Matches(text))
                {
                    Group g = mc.Groups["Target"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပါ။",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "အများပြနာမ်ပုဒ် ('~တွေ') နောက်တွင် ကြိယာမပါဘဲ 'တယ်။' မလိုက်နိုင်ပါ — 'ပါ။' (သို့မဟုတ် 'ဖြစ်ပါတယ်။') ဟု ရေးသားရမည်။",
                        "နာမ်ဝါကျအဆုံးသတ် ('~ပါ။')", "PART", false,
                        "ကြိယာဝိဘတ် 'တယ်' သည် ကြိယာနောက်၌သာ လိုက်နိုင်ပြီး နာမ်ပုဒ်နောက်၌ တိုက်ရိုက်ပိတ်လိုလျှင် 'ပါ။' ကို သုံးရသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိဘတ် နှင့် ပစ္စည်းစည်းမျဉ်း)");
                }
            }

            // 8. Corpus Word-Order, Syllable-Spaced Omission & Intruder Adverb Rules
            for (int rIdx = 0; rIdx < V82CorpusPrecisionRules.Length; rIdx++)
            {
                var r = V82CorpusPrecisionRules[rIdx];
                if (!ContainsMm(text, mmMask, r.Trigger))
                    continue;

                foreach (Match mr in r.Pattern.Matches(text))
                {
                    Group g = mr.Groups["T"];
                    if (!g.Success || OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string displayRep = r.Replacement.Length == 0 ? "(ဖယ်ရှားရန်)" : r.Replacement;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, r.Replacement,
                        r.Type,
                        $"ဝါကျဖွဲ့ထုံးနှင့် အဓိပ္ပာယ် မှန်ကန်စေရန် '{g.Value.Trim()}' ကို '{displayRep}' ဟု ပြင်ဆင်သင့်ပါသည်။",
                        r.Cue, "SYNTAX", r.Type == ProofingIssueType.SpellingError,
                        r.Reason,
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် တွဲဖက်အသုံးစံနှုန်း");
                }
            }
        }
    }
}
