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

            // 2. Glued Multi-Letter English Prose Between Myanmar Words ('နေရာတွင်Upton Parkဘူတာ' -> ' နေရာတွင် Upton Park ဘူတာ ')
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

            // 3. Missing Copula After Plural Noun ('~တွေတယ်။' -> '~တွေပါ။')
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

            // 4. Corpus Word-Order, Syllable-Spaced Omission & Intruder Adverb Rules
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
