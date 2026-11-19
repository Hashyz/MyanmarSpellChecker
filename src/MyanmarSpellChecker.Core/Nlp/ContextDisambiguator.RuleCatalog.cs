using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Context-aware collocation and homophone disambiguator based on "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း".
    /// Detects valid Myanmar syllables/words used in the wrong semantic context (e.g., အချိန်မှီ vs အချိန်မီ,
    /// မော်တော်ယဉ် vs မော်တော်ယာဉ်, ထုတ်ပြန်ကြော်ငြာ vs ထုတ်ပြန်ကြေညာ, ဘယ်ဖက် vs ဘယ်ဘက်).
    /// </summary>
    public sealed partial class ContextDisambiguator
    {
        private sealed class CollocationRule
        {
            public Regex Pattern { get; }
            public string WrongSpanGroup { get; }
            public string Replacement { get; }
            public string MeaningCue { get; }
            public string Explanation { get; }
            public string[]? RequiredAnyLiterals { get; }
            public ulong[]? RequiredLiteralMasks { get; }
            public ulong CommonRequiredMask { get; }
            public ulong AnyAnchorCharMask { get; }
            public bool AllRequiredHaveMyanmarChar { get; }

            public CollocationRule(string pattern, string wrongSpanGroup, string replacement, string meaningCue, string explanation)
            {
                RequiredAnyLiterals = ExtractGuaranteedGroupLiterals(pattern, wrongSpanGroup);
                if (RequiredAnyLiterals != null && RequiredAnyLiterals.Length > 0)
                {
                    var masks = new ulong[RequiredAnyLiterals.Length];
                    ulong common = ulong.MaxValue;
                    ulong any = 0UL;
                    bool allHaveMm = true;
                    for (int i = 0; i < RequiredAnyLiterals.Length; i++)
                    {
                        ulong m = OfficialStyleGrammarEngine.ComputeMyanmarCharMask(RequiredAnyLiterals[i]);
                        masks[i] = m;
                        common &= m;
                        any |= m;
                        if (m == 0UL)
                            allHaveMm = false;
                    }
                    RequiredLiteralMasks = masks;
                    CommonRequiredMask = common;
                    AnyAnchorCharMask = any;
                    AllRequiredHaveMyanmarChar = allHaveMm;
                }
                Pattern = new Regex(pattern, RequiredAnyLiterals == null ? RegexOptions.Compiled : RegexOptions.None);
                WrongSpanGroup = wrongSpanGroup;
                Replacement = replacement;
                MeaningCue = meaningCue;
                Explanation = explanation;
            }
        }

        /// <summary>
        /// Conservatively extracts required literal Myanmar substrings from all <c>(?&lt;groupName&gt;...)</c> definitions in <paramref name="pattern"/>.
        /// Returns <c>null</c> if any disjunctive branch lacks a provably mandatory literal (falling back to unconditional regex execution).
        /// </summary>
        private static string[]? ExtractGuaranteedGroupLiterals(string pattern, string groupName)
        {
            string marker = "(?<" + groupName + ">";
            int searchFrom = 0;
            var allLiterals = new List<string>(4);
            bool foundGroup = false;

            while (searchFrom < pattern.Length)
            {
                int idx = pattern.IndexOf(marker, searchFrom, StringComparison.Ordinal);
                if (idx < 0)
                    break;

                foundGroup = true;
                int contentStart = idx + marker.Length;
                int depth = 1;
                bool inClass = false;
                int p = contentStart;
                while (p < pattern.Length && depth > 0)
                {
                    char c = pattern[p];
                    if (c == '\\')
                    {
                        p += 2;
                        continue;
                    }
                    if (c == '[') inClass = true;
                    else if (c == ']') inClass = false;
                    else if (!inClass)
                    {
                        if (c == '(') depth++;
                        else if (c == ')') depth--;
                    }
                    p++;
                }

                if (depth != 0)
                    return null;

                // If the group itself is optional (? or *), it is not mandatory on that path
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == '*'))
                    return null;

                string groupBody = pattern.Substring(contentStart, p - 1 - contentStart);
                if (!TryExtractBranchLiterals(groupBody, allLiterals))
                    return null;

                searchFrom = p;
            }

            if (!foundGroup || allLiterals.Count == 0)
                return null;

            return allLiterals.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static bool TryExtractBranchLiterals(string expr, List<string> outLiterals)
        {
            var branches = SplitTopLevelAlternation(expr);
            for (int b = 0; b < branches.Count; b++)
            {
                string br = branches[b];
                string bestRun = FindLongestMandatoryMyanmarRunAtTopLevel(br);
                if (!string.IsNullOrEmpty(bestRun))
                {
                    outLiterals.Add(bestRun);
                    continue;
                }

                // Handle leading non-optional '(?:A|B|C)' group at depth 0 (e.g. '(?:ဖူး|ဗူး)(?:သီး|ညွန့်)')
                if (br.StartsWith("(?:", StringComparison.Ordinal))
                {
                    int depth = 1;
                    bool inClass = false;
                    int p = 3;
                    while (p < br.Length && depth > 0)
                    {
                        char c = br[p];
                        if (c == '\\') { p += 2; continue; }
                        if (c == '[') inClass = true;
                        else if (c == ']') inClass = false;
                        else if (!inClass)
                        {
                            if (c == '(') depth++;
                            else if (c == ')') depth--;
                        }
                        p++;
                    }
                    bool groupOptional = p < br.Length && (br[p] == '?' || br[p] == '*');
                    if (depth == 0 && !groupOptional)
                    {
                        string subBody = br.Substring(3, p - 4);
                        var subLits = new List<string>(4);
                        if (TryExtractPureMyanmarLiteralsOnly(subBody, subLits) && subLits.Count > 0)
                        {
                            outLiterals.AddRange(subLits);
                            continue;
                        }
                    }
                }

                return false;
            }
            return true;
        }

        private static bool TryExtractPureMyanmarLiteralsOnly(string expr, List<string> outLiterals)
        {
            var parts = SplitTopLevelAlternation(expr);
            if (parts.Count == 0 || parts.Count > 12)
                return false;

            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part))
                    return false;
                for (int k = 0; k < part.Length; k++)
                {
                    char c = part[k];
                    if (c < '\u1000' || c > '\u104F')
                        return false;
                }
                outLiterals.Add(part);
            }
            return true;
        }

        private static List<string> SplitTopLevelAlternation(string expr)
        {
            var list = new List<string>(2);
            int depth = 0;
            bool inClass = false;
            int lastStart = 0;
            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '\\')
                {
                    i++;
                    continue;
                }
                if (c == '[') inClass = true;
                else if (c == ']') inClass = false;
                else if (!inClass)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == '|' && depth == 0)
                    {
                        list.Add(expr.Substring(lastStart, i - lastStart));
                        lastStart = i + 1;
                    }
                }
            }
            list.Add(expr.Substring(lastStart));
            return list;
        }

        private static string FindLongestMandatoryMyanmarRunAtTopLevel(string branch)
        {
            string best = string.Empty;
            var current = new System.Text.StringBuilder();
            int depth = 0;
            bool inClass = false;

            for (int i = 0; i < branch.Length; i++)
            {
                char c = branch[i];
                if (c == '\\')
                {
                    if (current.Length > best.Length) best = current.ToString();
                    current.Clear();
                    i++;
                    continue;
                }
                if (c == '[')
                {
                    if (current.Length > best.Length) best = current.ToString();
                    current.Clear();
                    inClass = true;
                    continue;
                }
                if (c == ']')
                {
                    inClass = false;
                    continue;
                }
                if (inClass)
                    continue;

                if (c == '(')
                {
                    if (current.Length > best.Length) best = current.ToString();
                    current.Clear();
                    depth++;
                    continue;
                }
                if (c == ')')
                {
                    depth--;
                    continue;
                }

                if (depth == 0 && c >= '\u1000' && c <= '\u104F')
                {
                    bool optionalNext = (i + 1 < branch.Length) && (branch[i + 1] == '?' || branch[i + 1] == '*' || branch[i + 1] == '{');
                    if (!optionalNext)
                    {
                        current.Append(c);
                        continue;
                    }
                }

                if (current.Length > best.Length)
                    best = current.ToString();
                current.Clear();
            }

            if (current.Length > best.Length)
                best = current.ToString();
            return best;
        }

        private static readonly List<CollocationRule> Rules = new List<CollocationRule>
        {
            // 1. မီ (reach/on time/up to standard) vs မှီ (lean on/depend on)
            new CollocationRule(
                @"(?<Target>(?:အချိန်|စံချိန်|အမီ|သတ်မှတ်ချိန်|ရာသီ|ခေတ်|သက်တမ်း)မှီ)",
                "Target",
                "",
                "မီ (အချိန်~၊ စံချိန်~၊ ခေတ်~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အချိန်/စံချိန် ရောက်ရှိခြင်းကို 'မီ' (ဟထိုးမပါ) ဖြင့် ရေးသားရမည်။ ('မှီ' သည် မှီခိုခြင်း၊ မှီတွယ်ခြင်းအတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>မီ(?:ခို|တွယ်|ငြမ်း|အုံး))",
                "Target",
                "",
                "မှီ (မှီခို~၊ မှီတွယ်~၊ မှီငြမ်းပြု~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အားကိုးမှီခိုခြင်းနှင့် မှီငြမ်းပြုခြင်းကို 'မှီ' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),

            // 2. ယာဉ် (vehicle/craft) vs ယဉ် (polite/civilized/tame; note: 'ရေယဉ်ကြော/ရေယဉ်ဘောင်' = gentle stream in သတ်ပုံကျမ်း!)
            new CollocationRule(
                @"(?<Target>(?:မော်တော်|စက်|ရထား|လေ|အာကာသ)ယဉ်|ရေယဉ်(?!ကြော|ဘောင်))",
                "Target",
                "",
                "ယာဉ် (မော်တော်~၊ ရထား~၊ လေ~)",
                "သွားလာသယ်ယူပို့ဆောင်ရေးယာဉ် အနက်အတွက် 'ယာဉ်' (ရေးချပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ယဉ်(?:မောင်း|ထိန်း|စည်းကမ်း|လမ်းကြော|အန္တရာယ်|ရပ်နား|လိုင်း))",
                "Target",
                "",
                "ယာဉ် (ယာဉ်မောင်း၊ ယာဉ်စည်းကမ်း)",
                "သွားလာရေးယာဉ်နှင့် ပတ်သက်သော ပုဒ်များတွင် 'ယာဉ်' (ရေးချပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ယာဉ်(?:ကျေး|ကျေးမှု|ပါး))",
                "Target",
                "",
                "ယဉ် (ယဉ်ကျေးမှု၊ သိမ်မွေ့ယဉ်ကျေး~)",
                "သိမ်မွေ့ယဉ်ကျေးခြင်း အနက်အတွက် 'ယဉ်' (ရေးချမပါ) ဖြင့် ရေးသားရမည်။"
            ),

            // 3. ကြေညာ (public announcement) vs ကြော်ငြာ (commercial ad / Gazette အမိန့်ကြော်ငြာစာ)
            new CollocationRule(
                @"(?<Target>အမိန့်(?:ကြေညာ|ကြော်ညာ|ကြေငြာ)စာ)",
                "Target",
                "အမိန့်ကြော်ငြာစာ",
                "ရုံးသုံးထုတ်ပြန်စာ (Gazette Notification)",
                "ပြည်ထောင်စုအစိုးရနှင့် ဝန်ကြီးဌာန ရုံးသုံးစံနှုန်းအရ 'အမိန့်ကြော်ငြာစာ' (ကြော် + ငြာ) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:သတင်းထုတ်ပြန်|ထုတ်ပြန်|တရားဝင်|အသိပေး)ကြော်ငြာ(?<Suffix>ချက်|သည်)?)(?!စာ)",
                "Target",
                "",
                "ကြေညာ (အများသိစေရန် ထုတ်ပြန်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အများသိစေရန် ထုတ်ပြန်ခြင်းကို 'ကြေညာ' ဟု ရေးသားရမည်။ ('ကြော်ငြာ' သည် စီးပွားရေးကုန်ပစ္စည်း ကြော်ငြာခြင်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=(?:သတင်းထုတ်ပြန်|ထုတ်ပြန်|တရားဝင်|အသိပေး|အောင်စာရင်း|ရွေးကောက်ပွဲရလဒ်)[ \t]+)(?<Target>ကြော်ငြာ)(?!စာ|ဘုတ်|ဆိုင်းဘုတ်|လုပ်ငန်း|ကုမ္ပဏီ)",
                "Target",
                "ကြေညာ",
                "ကြေညာ (အများသိစေရန် ထုတ်ပြန်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အများသိစေရန် ထုတ်ပြန်ခြင်းကို 'ကြေညာ' ဟု ရေးသားရမည် ('အမိန့်ကြော်ငြာစာ' နှင့် စီးပွားရေးကုန်ပစ္စည်း 'ကြော်ငြာ' ၌သာ 'ကြော်ငြာ' သုံးသည်)။"
            ),

            // 4. ချည် (tie/thread) vs ချဉ် (sour) in compounds
            new CollocationRule(
                @"(?<Target>ကြိုးချဉ်)",
                "Target",
                "ကြိုးချည်",
                "ချည် (ကြိုး~၊ နှောင်~)",
                "ကြိုးဖြင့် ချည်နှောင်ခြင်းကို 'ကြိုးချည်' (ညကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ချည်ပေါင်)",
                "Target",
                "ချဉ်ပေါင်",
                "ချဉ် (အရသာ~၊ ချဉ်ပေါင်ရွက်)",
                "ချဉ်ပေါင်ပင်/ချဉ်ပေါင်ရွက်ကို 'ချဉ်' (ဉကလေးသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ဟင်းရည်|ချဉ်ရည်|သံပရာသီး|မန်ကျည်းသီး|သရက်သီး|အရသာ)[^\u104A\u104B\r\n]{0,14}?(?<Target>ချည်)(?=နေ|လှ|လွန်း|သွား|သည်|တယ်|ပါ|သော|သည့်)",
                "Target",
                "ချဉ်",
                "ချဉ် (အရသာ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အရသာချဉ်ခြင်းကို 'ချဉ်' (ဉကလေးသတ်) ဖြင့် ရေးသားရမည် ('ချည်' သည် ကြိုးချည်ခြင်း/ချည်မျှင် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:လျော်ကြေး|နစ်နာကြေး|ဒဏ်ကြေး|အကြွေး|ပျက်စီးဆုံးရှုံးမှု|ဆုံးရှုံးမှုတန်ဖိုး)[^\u104A\u104B\r\n]{0,18}?(?<Target>လျှော်)(?=ပေး|ရ|မည်|သည်|ရန်|ခိုင်း|စေ)",
                "Target",
                "လျော်",
                "လျော် (အစားပြန်ပေး~သည်၊ လျော်ကြေး)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နစ်နာကြေး/တန်ဖိုး အစားပြန်ပေးလျော်ခြင်းကို ဟထိုးမပါဘဲ 'လျော်' ဟု ရေးသားရမည် ('လျှော်' သည် အဝတ်/ခေါင်း လျှော်ဖွပ်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:အဝတ်အစား|အဝတ်အထည်|အဝတ်|အင်္ကျီ|ပုဆိုး|ထဘီ|စောင်|ခြင်ထောင်|ဆံပင်)[^\u104A\u104B\r\n]{0,18}?(?<Target>လျော်)(?=\s*(?:ဖွပ်|ပေး|ပြီး|နေ|ထား|ရန်|သည်|ပါ|တယ်|ကြ|မယ်|ခဲ့))",
                "Target",
                "လျှော်",
                "လျှော် (ရေ/ဆပ်ပြာဖြင့် အဝတ်~ဖွပ်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဝတ်အထည်/ဆံပင် ရေဖြင့် လျှော်ဖွပ်ခြင်းကို ဟထိုးဖြင့် 'လျှော်' ဟု ရေးသားရမည် ('လျော်' သည် လျော်ကြေးပေးလျော်ခြင်း/သင့်လျော်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:နေပူထဲ|နေရောင်အောက်)(?:မှာ|တွင်|၌)?[^\u104A\u104B\r\n]{0,18}?(?<!ခြေ|လက်)(?<Target>လှမ်း)(?=\s*(?:ရ|ထား|နေ|ပြီး|ပါ|သည်|တယ်|မည်|မယ်))",
                "Target",
                "လှန်း",
                "လှန်း (နေပူထဲ၌ အခြောက်ခံ နေ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နေရောင်/နေပူထဲ၌ အခြောက်ခံခြင်းကို နသတ်ဖြင့် 'လှန်း' ဟု ရေးသားရမည် ('လှမ်း' သည် ခြေလှမ်းခြင်း/လက်လှမ်းခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>တင်ပြွေဆေးနွေး)",
                "Target",
                "တင်ပြဆွေးနွေး",
                "တင်ပြဆွေးနွေး (အစည်းအဝေး၌ တင်ပြ၍ ဆွေးနွေးသည်)",
                "ဝဆွဲ ('ွ') နေရာလွဲမှားနေပါသည် — 'တင်ပြွေဆေးနွေး' အစား 'တင်ပြဆွေးနွေး' ဟု ရေးသားရမည်။"
            ),

            // 5. စဉ် (order/sequence) vs စည် (prosper/drum) vs စည်း (rule/boundary)
            new CollocationRule(
                @"(?<Target>စည်ကမ်း)",
                "Target",
                "စည်းကမ်း",
                "စည်းကမ်း (လိုက်နာရမည့်~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'စည်းကမ်း' (ဝစ္စပေါက်ပါ) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အစီအစည်)",
                "Target",
                "အစီအစဉ်",
                "အစီအစဉ် (လုပ်ငန်း~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'အစီအစဉ်' (ဉကလေးသတ်) ဟု ရေးသားရမည်။"
            ),

            // 6. ဘက် (direction/side) vs ဖက် (embrace/leaf/တစ်ဖက်)
            new CollocationRule(
                @"(?<Target>(?:အရှေ့|အနောက်|တောင်|မြောက်|ဘယ်|ညာ|လက်ဝဲ|လက်ယာ|အတွင်း|အပြင်)ဖက်)",
                "Target",
                "",
                "ဘက် (အရပ်မျက်နှာ/အခြမ်းပြပုဒ်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အရပ်မျက်နှာနှင့် အခြမ်းပြပုဒ်များတွင် 'ဘက်' (ဘကုန်း) ဖြင့် ရေးသားရမည်။"
            ),

            // 7. ကျမ်း (treatise/book) vs ကြမ်း (rough/floor/draft; note: 'ဆေးကြမ်း' = potent medicine in သတ်ပုံကျမ်း!)
            new CollocationRule(
                @"(?<Target>(?:သတ်ပုံ|အဘိဓာန်|သဒ္ဒါ|ဗေဒင်|ပိဋကတ်|ပြုစုသော|ဓာနွန္တရီဆေး)ကြမ်း)",
                "Target",
                "",
                "ကျမ်း (စာပေကျမ်းဂန် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာပေကျမ်းဂန် အနက်အတွက် 'ကျမ်း' (ကကြီး ယပင့်) ဖြင့် ရေးသားရမည်။ ('ကြမ်း' သည် ကြမ်းတမ်းခြင်း/မူကြမ်း/ဆေးကြမ်း အတွက်သာ သုံးသည်)။"
            ),

            // 8. ခမ်း (grand/ceremony/dry up) vs ခန်း (room/section/chapter)
            new CollocationRule(
                @"(?<Target>အခန်းအနား)",
                "Target",
                "အခမ်းအနား",
                "အခမ်းအနား (ပွဲလမ်းသဘင် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပွဲလမ်းသဘင် ကျင်းပခြင်းကို 'အခမ်းအနား' (မသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အခမ်းကဏ္ဍ)",
                "Target",
                "အခန်းကဏ္ဍ",
                "အခန်းကဏ္ဍ (ကဏ္ဍ/အပိုင်းအခြား ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အပိုင်းအခြား/ကဏ္ဍ အနက်အတွက် 'အခန်းကဏ္ဍ' (နသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 9. ကြီး (big/great) vs ကျီး (crow)
            new CollocationRule(
                @"(?<Target>ကြီးကန်း)",
                "Target",
                "ကျီးကန်း",
                "ကျီးကန်း (ကျီးငှက် ~)",
                "ငှက်အမည်ဖြစ်သဖြင့် 'ကျီးကန်း' (ယပင့်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကျီး(?:မား|ကျယ်|ကြပ်|ပြင်း))",
                "Target",
                "",
                "ကြီး (ကြီးမား~၊ ကြီးကျယ်~၊ ကြီးကြပ်~)",
                "အရွယ်အစား/အဆင့်အတန်း ကြီးမားခြင်းနှင့် ကြီးကြပ်ခြင်းအတွက် 'ကြီး' (ရရစ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 10. ကျောက် (stone/gem) vs ကြောက် (fear)
            new CollocationRule(
                @"(?<Target>ကြောက်(?:စိမ်း|မျက်|ဖြူ|မီးသွေး|သင်ပုန်း|တိုင်))",
                "Target",
                "",
                "ကျောက် (ကျောက်စိမ်း၊ ကျောက်မျက် ~)",
                "တွင်းထွက်ကျောက်နှင့် ကျောက်သားပစ္စည်းများအတွက် 'ကျောက်' (ယပင့်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကျောက်(?:ရွံ့|လန့်|မက်ဖွယ်))",
                "Target",
                "",
                "ကြောက် (ကြောက်ရွံ့~၊ ကြောက်လန့်~)",
                "ထိတ်လန့်ကြောက်ရွံ့ခြင်း အနက်အတွက် 'ကြောက်' (ရရစ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 11. ကံ (karma/luck) vs ကန် (lake/tank/kick)
            new CollocationRule(
                @"(?<Target>ကန်(?:ကောင်း|ဆိုး|ကြမ္မာ|ဇာတာ))",
                "Target",
                "",
                "ကံ (ကံကြမ္မာ၊ ကံကောင်း ~)",
                "ကံကြမ္မာ အနက်အတွက် 'ကံ' (သေးသေးတင်) ဖြင့် ရေးသားရမည်။ ('ကန်' သည် ရေကန်/ခြေဖြင့်ကန်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ရေကံ)",
                "Target",
                "ရေကန်",
                "ရေကန် (ရေသိုလှောင်ရာ ~)",
                "ရေသိုလှောင်ရာကန် အနက်အတွက် 'ရေကန်' (နသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 12. လန်း (fresh) vs လမ်း (road)
            new CollocationRule(
                @"(?<Target>လမ်းဆန်း)",
                "Target",
                "လန်းဆန်း",
                "လန်းဆန်း (စိတ်ရွှင်လန်း~)",
                "စိတ်လက်ကြည်လင်လန်းဆန်းခြင်းကို 'လန်းဆန်း' (နသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လန်း(?:လျှောက်|ကြောင်း|ညွှန်|မကြီး))",
                "Target",
                "",
                "လမ်း (သွားလာရာ လမ်းကြောင်း ~)",
                "သွားလာရာ လမ်းကြောင်း အနက်အတွက် 'လမ်း' (မသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 13. Semantic Collocations (from Myanmar collocation standards)
            new CollocationRule(
                @"မိုး[\u1000-\u103F\s]{0,8}?(?<Target>ခိုင်မာစွာ)\s*ရွာ",
                "Target",
                "သည်းထန်စွာ",
                "မိုးသည်းထန်စွာ ရွာသွန်း~ (တွဲဖက်အသုံးမှန်)",
                "မိုးရွာသွန်းခြင်းနှင့် တွဲဖက်ရာတွင် 'ခိုင်မာစွာ' အစား 'သည်းထန်စွာ' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အရသာပေါ်)\s*(?:အခြေအနေ|အစီအမံ|ဆောင်ရွက်|ကယ်ဆယ်|လူနာ)",
                "Target",
                "အရေးပေါ်",
                "အရေးပေါ် (ချက်ချင်းဆောင်ရွက်ရန် ~)",
                "ချက်ချင်းဆောင်ရွက်ရမည့် ကိစ္စရပ်များတွင် 'အရေးပေါ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဂရုဆိုက်)",
                "Target",
                "ဂရုစိုက်",
                "ဂရုစိုက် (အလေးထား ဂရုစိုက်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အာရုံစူးစိုက် အလေးထားခြင်းကို စလုံးဖြင့် 'ဂရုစိုက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သွန်သင်ဆုံမ)",
                "Target",
                "သွန်သင်ဆုံးမ",
                "သွန်သင်ဆုံးမ (လမ်းညွှန်ဆုံးမ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လမ်းညွှန်ပဲ့ပြင်ခြင်းကို ဝစ္စပေါက်ပါလျက် 'သွန်သင်ဆုံးမ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စနစ်တကွ)",
                "Target",
                "စနစ်တကျ",
                "စနစ်တကျ (နည်းစနစ်မှန်ကန်စွာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နည်းလမ်းမှန်ကန်စွာ ဆောင်ရွက်ခြင်းကို 'စနစ်တကျ' ဟု ရေးသားရမည် ('တကွ' ကို 'အတူတကွ' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အလွယ်တကာ)(?=ဝယ်|ရ|သိ|လုပ်|တွေ့|သွား|ပြော|ဖတ်|သုံး|\s)",
                "Target",
                "အလွယ်တကူ",
                "အလွယ်တကူ (လွယ်လင့်တကူ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ မခက်မခဲ လွယ်ကူစွာ ပြုလုပ်နိုင်ခြင်းကို 'အလွယ်တကူ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သက်တောင့်သက်သာ|သက်တောင့်သက်သာ)",
                "Target",
                "သက်သောင့်သက်သာ",
                "သက်သောင့်သက်သာ (ကိုယ်စိတ်ချမ်းသာစွာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပင်ပန်းဆင်းရဲမှုကင်းစွာ နေထိုင်ခြင်းကို သကြီးဖြင့် 'သက်သောင့်သက်သာ' ဟု ရေးသားရမည် ('သက်တောင့်သက်သာ' မှာ အသံထွက်လိုက် မှားယွင်းရေးထုံး ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<!သေနတ်|လေး|မြား|အမြောက်)(?<Target>ပစ်မှု)(?=\s*(?:များ|တွေ|ကို|ကျူးလွန်|ဖြစ်ပွား|နှိမ်နင်း|စစ်ဆေး|ဆိုင်ရာ|ဖြင့်|ကြောင့်|အတွက်))",
                "Target",
                "ပြစ်မှု",
                "ပြစ်မှု (ဥပဒေချိုးဖောက်သော ရာဇဝတ်ပြစ်မှု ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဥပဒေချိုးဖောက်သည့် ရာဇဝတ်မှုကို ရရစ်ဖြင့် 'ပြစ်မှု' ဟု ရေးသားရမည် ('ပစ်' သည် ပစ်ခတ်/စွန့်ပစ်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>သုပ်ဆေး)",
                "Target",
                "သုတ်ဆေး",
                "သုတ်ဆေး (နံရံ/သစ်သား အရောင်ခြယ်~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အရောင်ခြယ်သုတ်လိမ်းသည့် ဆေးကို တဝမ်းပူသတ်ဖြင့် 'သုတ်ဆေး' ဟု ရေးသားရမည် ('သုပ်' သည် အစားအစာ နယ်ဖတ်သုပ်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ဖွံ့ဖြိုးတိုးထက်)",
                "Target",
                "ဖွံ့ဖြိုးတိုးတက်",
                "ဖွံ့ဖြိုးတိုးတက် (ကြီးပွားတိုးတက်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြီးပွားဖွံ့ဖြိုးခြင်းကို တဝမ်းပူဖြင့် 'ဖွံ့ဖြိုးတိုးတက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<=ဟု\s+|လို့\s+|ခေါ်|ခော်)(?<Target>ပြော့)(?=သည်|တယ်|ကြ|ပါ|တော့|ခဲ့)|(?<![\u1000-\u103F])(?<Target>ပြော့)(?=(?:သည်။|တယ်။|ပါသည်။|ပါတယ်။|တော့\s|ဆို|ပြ(?:သည်|တယ်|ပါ))))",
                "Target",
                "ပြော",
                "ပြော (စကား~ဆိုသည်)",
                "စကားပြောဆိုခြင်း အနက်အတွက် အောက်မြစ်မပါဘဲ 'ပြော' ဟု ရေးသားရမည် ('ပြော့' သည် ပြက်ရယ်ပြော့ခြင်း အနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အဆိုရ)(?=ရဲ့|၏|က|သည်|အဖွဲ့|ဝန်ထမ်း|ဌာန)",
                "Target",
                "အစိုးရ",
                "အစိုးရ (နိုင်ငံတော်အုပ်ချုပ်သည့် ~အဖွဲ့)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နိုင်ငံအုပ်ချုပ်ရေးအဖွဲ့ကို စလုံးနှင့် ဝစ္စပေါက်ဖြင့် 'အစိုးရ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>တောင်ဆို)(?=ချင်|ပါ|သည်|တယ်|ခဲ့|ကြ|မှု|ချက်)",
                "Target",
                "တောင်းဆို",
                "တောင်းဆို (လိုလားချက်ကို ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လိုလားချက် တင်ပြတောင်းခံခြင်းကို ဝစ္စပေါက်ဖြင့် 'တောင်းဆို' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပျောက်စုန်)",
                "Target",
                "ပျောက်ဆုံး",
                "ပျောက်ဆုံး (ကွယ်ပျောက်ဆုံးရှုံး~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပျောက်ဆုံး' (ဆလိမ်၊ သေးသေးတင် ဝစ္စပေါက်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ရှာဖွင့်)(?=နိုင်|ရ|တွေ့|သည်|တယ်|ပါ)",
                "Target",
                "ရှာဖွေ",
                "ရှာဖွေ (စူးစမ်း~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စူးစမ်းလိုက်လံရှာခြင်းကို 'ရှာဖွေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:စိတ်လှုပ်ရှား|ပျော်|ကြောက်|အံ့ဩ|ဝမ်းသာ|စိတ်ဝင်စား))(?<Target>ဆရာကောင်း)",
                "Target",
                "စရာကောင်း",
                "စရာကောင်း (~စရာကောင်းသည်)",
                "ကြိယာနောက်ဆက် နာမဝိသေသနပြုပစ္စည်းကို စလုံးဖြင့် 'စရာကောင်း' ('~စရာကောင်းသည်') ဟု ရေးသားရမည် ('ဆရာကောင်း' သည် သင်ကြားသူဆရာ အနက်၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<=(?:အရေး|နာမည်|ကမ္ဘာ))(?<Target>ကြည်)(?=တဲ့|သော|သည့်|သည့်|ကို|တယ်|သည်)",
                "Target",
                "ကြီး",
                "ကြီး (အရေး~သည်၊ နာမည်~သည်၊ ကမ္ဘာ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြီးမား/ထင်ရှားခြင်း အနက်အတွက် 'ကြီး' ဟု ရေးသားရမည် ('ကြည်' သည် ရေကြည်/စိတ်ကြည် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:(?<=အရည်အသွေး)(?<Target>မင်း)|(?<=စရိတ်)(?<Target>မင်)(?=တက်))",
                "Target",
                "မြင့်",
                "မြင့် (အရည်အသွေး~မားသည်၊ စရိတ်~တက်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဆင့်/စရိတ် မြင့်မားခြင်းကို ရရစ် ငသတ် အောက်မြစ်ဖြင့် 'မြင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=အခန်း)(?<Target>ကန့်)(?=မှ|တွင်|၌|က|\s*ပါဝင်)",
                "Target",
                "ကဏ္ဍ",
                "အခန်းကဏ္ဍ (ပါဝင်သည့် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပါဝင်ဆောင်ရွက်သည့် ကဏ္ဍအနက်အတွက် ပါဋ္ဌ်ဆင့်ဖြင့် 'အခန်းကဏ္ဍ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အလှအပါ)(?=တွေ|များ|ကို|ပဲ)",
                "Target",
                "အလှအပ",
                "အလှအပ (တင့်တယ်လှပမှု ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'အလှအပ' ဟု ရေးချမပါဘဲ ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လွယ်ကူး)(?=မြန်ဆန်)",
                "Target",
                "လွယ်ကူ",
                "လွယ်ကူ (မခက်မခဲ ~မြန်ဆန်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်မပါဘဲ 'လွယ်ကူ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=စီး)(?<Target>စင်း)(?=လာ|သွား|နေ|သည်|တယ်)",
                "Target",
                "ဆင်း",
                "စီးဆင်း (ရေ/မျက်ရည် ~လာသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အရည်စီးကျလာခြင်းကို ဆလိမ်ဖြင့် 'စီးဆင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:သီတင်းကျွတ်|တန်ဆောင်တိုင်|သင်္ကြန်|ဘုရား|ရိုးရာ))(?<Target>ပွဲတော)(?![\u102B-\u103E\u103A])",
                "Target",
                "ပွဲတော်",
                "ပွဲတော် (အထိမ်းအမှတ် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အထိမ်းအမှတ်ပွဲအနက်တွင် ရှေ့ထိုးဖြင့် 'ပွဲတော်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မြန်မာပြဲ)",
                "Target",
                "မြန်မာပြည်",
                "မြန်မာပြည် (မြန်မာနိုင်ငံ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ညသတ်ဖြင့် 'မြန်မာပြည်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မြေသိလွှာ)",
                "Target",
                "မြေဆီလွှာ",
                "မြေဆီလွှာ (စိုက်ပျိုးမြေ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'မြေဆီလွှာ' (ဆလိမ်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ချွဲခွာ)(?=နေ|သွား|ရ|ခဲ့|သည်|တယ်)",
                "Target",
                "ခွဲခွာ",
                "ခွဲခွာ (တစ်ဦးနှင့်တစ်ဦး ~နေထိုင်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ယပင့်မပါဘဲ 'ခွဲခွာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဝန်စောင့်မှု)",
                "Target",
                "ဝန်ဆောင်မှု",
                "ဝန်ဆောင်မှု (ဆောင်ရွက်ပေးသည့် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဝန်ဆောင်မှု' (ဆလိမ်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အထောက်အကူလုပ်)",
                "Target",
                "အထောက်အကူပြု",
                "အထောက်အကူပြု (ကူညီပံ့ပိုး ~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အထောက်အကူပြုသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စိတ်ဖိ(?:ကိုင်|စီ)မှု)",
                "Target",
                "စိတ်ဖိစီးမှု",
                "စိတ်ဖိစီးမှု (စိတ်ပင်ပန်းဖိစီးခြင်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'စိတ်ဖိစီးမှု' (စလုံး လုံးကြီးတင်ဆန်ခတ် ဝစ္စပေါက်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မုန်တီ)",
                "Target",
                "မုန့်တီ",
                "မုန့်တီ (မြန်မာ့ရိုးရာ အစားအစာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ်ပါလျက် 'မုန့်တီ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပဲပြတ်)",
                "Target",
                "ပဲပြုတ်",
                "ပဲပြုတ် (နံနက်စာ စားဖွယ် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တစ်ချောင်းငင်ဖြင့် 'ပဲပြုတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ပဲပြုတ်|ပဲပြတ်|လက်ဖက်ရည်)(?:နဲ့|နှင့်|နှင့်)\s*)(?<Target>နံပါး)",
                "Target",
                "နံပြား",
                "နံပြား (ဂျုံဖြင့်ဖုတ်သော အစားအစာ ~)",
                "စားသောက်ဖွယ်ရာ အနက်ဖြစ်သဖြင့် ရရစ်ဖြင့် 'နံပြား' ဟု ရေးသားရမည် ('နံပါး' သည် ခန္ဓာကိုယ် နံဘေး/နံပါး ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ကြက်သွန်ကော်)",
                "Target",
                "ကြက်သွန်ကြော်",
                "ကြက်သွန်ကြော် (ဆီဖြင့်ကြော်ထားသော ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆီဖြင့်ကြော်ချက်ခြင်းကို ရရစ်ဖြင့် 'ကြက်သွန်ကြော်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကြက်စား)(?=၊|\s*ဝက်သား)",
                "Target",
                "ကြက်သား",
                "ကြက်သား (ဟင်းလျာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသားဟင်းလျာအနက်တွင် 'ကြက်သား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ငါဖယ်)",
                "Target",
                "ငါးဖယ်",
                "ငါးဖယ် (ရေချိုငါး ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်ဖြင့် 'ငါးဖယ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လူးအင်အား)",
                "Target",
                "လူအင်အား",
                "လူအင်အား (လုပ်သား/ကယ်ဆယ်ရေး ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်မပါဘဲ 'လူအင်အား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စိပ်ကြည်နူး)",
                "Target",
                "စိတ်ကြည်နူး",
                "စိတ်ကြည်နူး (စိတ်နှလုံး ကြည်နူးချမ်းမြေ့သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တဝမ်းပူသတ်ဖြင့် 'စိတ်ကြည်နူး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ငိုချင်စစ်)",
                "Target",
                "ငိုချင်စိတ်",
                "ငိုချင်စိတ် (စိတ်ခံစားချက် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စိတ်သဘော အနက်ဖြစ်သဖြင့် တဝမ်းပူသတ်ဖြင့် 'ငိုချင်စိတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လူချစ်လူခင်မှား)",
                "Target",
                "လူချစ်လူခင်များ",
                "လူချစ်လူခင်များ (ချစ်ခင်သူ ပေါများသည်)",
                "ပေါများခြင်း အနက်ဖြစ်သဖြင့် ယပင့်ဖြင့် 'လူချစ်လူခင်များ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကိုယ့်ကိုကို)(?=\s)",
                "Target",
                "ကိုယ့်ကိုယ်ကို",
                "ကိုယ့်ကိုယ်ကို (မိမိကိုယ်ကို ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နာမ်စား 'ကိုယ်' တွင် ယပက်လက်အသတ်ဖြင့် 'ကိုယ့်ကိုယ်ကို' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:မိခင်|ဖခင်|သတို့သား|သတို့သမီး|ဘုရား|ရှင်))(?<Target>လောင်)(?=တို့|များ|တွေ|သည်|က|\s)",
                "Target",
                "လောင်း",
                "လောင်း (ဖြစ်လတ္တံ့သော မိခင်~၊ ဘုရား~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အလောင်းအလျာပြ ပစ္စည်းကို ဝစ္စပေါက်ဖြင့် 'လောင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>တိရိစ္ဆာန်)",
                "Target",
                "တိရစ္ဆာန်",
                "တိရစ္ဆာန် (တောရိုင်း~၊ အိမ်မွေး~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ရ' တွင် လုံးကြီးတင်မပါဘဲ 'တိရစ္ဆာန်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အခါသမ္မယ)",
                "Target",
                "အခါသမယ",
                "အခါသမယ (မင်္ဂလာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပါဠိ 'သမယ' (အချိန်အခါ) ကို ပါဋ္ဌ်ဆင့်မပါဘဲ 'အခါသမယ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ခရမ်းချင်)",
                "Target",
                "ခရမ်းချဉ်",
                "ခရမ်းချဉ် (~သီး)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ချဉ်သောအရသာရှိသည့် အသီးအနက်တွင် ညကလေးသတ်ဖြင့် 'ခရမ်းချဉ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ထမင်း[ \t]*ကျော်)",
                "Target",
                "ထမင်းကြော်",
                "ထမင်းကြော် (ဆီဖြင့်ကြော်ထားသော ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆီဖြင့်ကြော်ချက်ထားသော အစားအစာကို ရရစ်ဖြင့် 'ထမင်းကြော်' ဟု တစ်ဆက်တည်း ရေးသားရမည် ('ကျော်' သည် ကျော်ဖြတ်သည်/ကျော်ကြားသည် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>သတိပဋ္ဌာ)(?!န်|န)",
                "Target",
                "သတိပဋ္ဌာန်",
                "သတိပဋ္ဌာန် (~တရားပွားများသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် ပါဠိသက်ရေးထုံးအရ နသတ်ဖြင့် 'သတိပဋ္ဌာန်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ကျယ်ပြန်)(?=စေ|လာ|သွား|လျက်|နေ|သည်|တယ်|ပါ|သော|သည့်|သည့်)",
                "Target",
                "ကျယ်ပြန့်",
                "ကျယ်ပြန့် (အတွေးအမြင်/အသိပညာ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ်ဖြင့် 'ကျယ်ပြန့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>မျောလင့်|မျောလင့်)",
                "Target",
                "မျှော်လင့်",
                "မျှော်လင့် (~ချက်၊ ~မိသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဟထိုး ရှေ့ထိုးဖြင့် 'မျှော်လင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ခေါက်စွဲ)",
                "Target",
                "ခေါက်ဆွဲ",
                "ခေါက်ဆွဲ (ရှမ်း~၊ အုန်းနို့~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆလိမ်ဖြင့် 'ခေါက်ဆွဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:ဂုဏ်ယူ|ဝမ်းသာ|ချီးကျူး|အားကျ|လေးစား|အံ့ဩ|ဝမ်းနည်း|ကြည်နူး|စိတ်ဝင်စား|ကြောက်မက်|သနား|ပျော်ရွှင်)ဆရာ)(?=ပင်|ပါ|ဖြစ်|ကောင်း|ပဲ|လှ|[\s\u104A\u104B]|$)",
                "Target",
                "",
                "ကြိယာနာမ်ပြုပစ္စည်း ('~စရာ')",
                "ကြိယာနောက်ဆက် နာမဝိသေသန/နာမ်ပြုပစ္စည်းကို စလုံးဖြင့် '~စရာ' (ဥပမာ 'ဂုဏ်ယူစရာ') ဟု ရေးသားရမည် ('ဆရာ' သည် သင်ကြားသူနာမ် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=ဟင်းသီးဟင်း)(?<Target>ပြုတ်)",
                "Target",
                "ရွက်",
                "ဟင်းသီးဟင်းရွက် (စားဖွယ် ~)",
                "မြန်မာစာ ပေါင်းစပ်ပုဒ်စံနှုန်းအရ 'ဟင်းသီးဟင်းရွက်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဦးနှစ်)(?<Target>ဖတ်)",
                "Target",
                "ဖက်",
                "နှစ်ဦးနှစ်ဖက် (နှစ်ဖက်စလုံး သဘောတူသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကကြီးသတ်ဖြင့် 'နှစ်ဦးနှစ်ဖက်' ဟု ရေးသားရမည် ('ဖတ်' သည် စာဖတ်သည် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>နေ့ဆင်)(?=\s+(?:လေ့ကျင့်|ပုံမှန်|စားသုံး|သွားလာ))",
                "Target",
                "နေ့စဉ်",
                "နေ့စဉ် (နေ့တိုင်း ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နေ့တိုင်းမပြတ် အနက်တွင် 'နေ့စဉ်' (စလုံး ညကလေးသတ်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ရန်ကုန်)(?<Target>မြူ)(?=မှာ|တွင်|၌|က|သို့)",
                "Target",
                "မြို့",
                "ရန်ကုန်မြို့ (~တွင်)",
                "မြို့ပြအနက်ဖြစ်သဖြင့် 'ရန်ကုန်မြို့' ဟု ရေးသားရမည် ('မြူ' သည် မြူခိုး ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ဘောနတ်စ်)",
                "Target",
                "ဘောနပ်စ်",
                "ဘောနပ်စ် (အပိုဆုကြေး Bonus)",
                "မွေးစားစာလုံး စံသတ်ပုံအရ ပသတ်ဖြင့် 'ဘောနပ်စ်' (Bonus) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ဘတ်စ)(?=\s*ကား)",
                "Target",
                "ဘတ်စ်",
                "ဘတ်စ်ကား (ခရီးသည်တင် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စသတ် ('စ်') ဖြင့် 'ဘတ်စ်ကား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<=(?:စာတိုက်|လျှို့ဝှက်|ဘား|ကျူအာ|သင်္ကေတ|ဧရိယာ|ဘဏ်|အရောင်)[ \t]*)(?<Target>ကုတ်)(?!အင်္ကျီ|ခြစ်|ဖဲ့|ကော်|ဟည်း)|(?<![\u1000-\u103F])(?<Target>ကုတ်)(?=[ \t]*နံပါတ်))",
                "Target",
                "ကုဒ်",
                "ကုဒ် (သင်္ကေတအမှတ် Code)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ကေတအမှတ်အသား (Code) အနက်တွင် ဒထွေးသတ်ဖြင့် 'ကုဒ်' ဟု ရေးသားရမည် ('ကုတ်' သည် ကုတ်အင်္ကျီ/ကုတ်ခြစ်သည် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>(?:(?:တစ်ယောက်|တယောက်|တစ်ဦး|တဦး|တစ်ခါ|တခါ|တစ်မျိုး|တမျိုး|တစ်သံ|တစ်သဘော)ထဲ(?!မှာ|တွင်|၌|သို့|ကနေ|မှ)|(?:တစ်ဖက်|တဖက်)ထဲ(?=ကိုပဲ|ကိုသာ|ပဲ|သာ)|(?:ငယ်ငယ်|ဟိုးအရင်|အရင်|ရှေး|မနက်|မနေ့|အစ|စစချင်း)က?ထဲက(?![\u1000-\u103F])))",
                "Target",
                "",
                "တည်း (အနက်ကန့်သတ်ပစ္စည်း '~တည်း' / '~ကတည်းက')",
                "မြန်မာသဒ္ဒါနှင့် သတ်ပုံကျမ်းအရ တစ်ခု/တစ်ဦးသာ ဟူသော ကန့်သတ်အနက်နှင့် အချိန်အစပြ '~ကတည်းက' အနက်တို့တွင် 'တည်း' ဟု ရေးသားရမည် ('ထဲ' သည် အတွင်းနေရာပြပုဒ် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ရောဝတီ)(?=[ \t]*(?:တိုင်းဒေသကြီး|တိုင်း|မြစ်ဝကျွန်းပေါ်|မြစ်))",
                "Target",
                "ဧရာဝတီ",
                "ဧရာဝတီ (~တိုင်းဒေသကြီး / ~မြစ်)",
                "တိုင်းဒေသကြီးနှင့် မြစ်အမည်မှန်အရ အစ၌ 'ဧ' အက္ခရာပါဝင်လျက် 'ဧရာဝတီ' ဟု ရေးသားရမည် ('ရေဝတီ' သည် နက္ခတ်အမည်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=[\u1000-\u103F]ခြင်း)(?<Target>ခြင့်|ခြင့်)(?![\u1000-\u103F])",
                "Target",
                "ဖြင့်",
                "ဖြင့် (ကရိုဏ်းဝိဘတ် '~ခြင်းဖြင့်')",
                "နာမ်ပြပစ္စည်း 'ခြင်း' နောက်တွင် ကရိုဏ်းဝိဘတ်ကို ဖဦးထုပ် ရရစ်ဖြင့် 'ခြင်းဖြင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ဘန်)(?=မှာ\s*ငွေ|အကောင့်|လုပ်ငန်း)",
                "Target",
                "ဘဏ်",
                "ဘဏ် (ငွေကြေး ~လုပ်ငန်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ငွေကြေးအဖွဲ့အစည်းအနက်တွင် ဏကြီးသတ်ဖြင့် 'ဘဏ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<=(?:အလွန်|အရမ်း|သိပ်)\s*)(?<Target>ကျောက်)(?=နေ(?:တယ်|သည်|ပါ|ပေ))|(?<=မြွေကို\s*)(?<Target>ကျောက်)(?=\s*(?:သည်|တယ်|ပါ)))",
                "Target",
                "ကြောက်",
                "ကြောက် (ထိတ်လန့်~ရွံ့သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ထိတ်လန့်စိုးရွံ့သည့် ကြိယာအနက်တွင် ရရစ်ဖြင့် 'ကြောက်' ဟု ရေးသားရမည် ('ကျောက်' သည် ကျောက်တုံးနာမ် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=မင်္ဂလာပါ\s+)(?<Target>ခင်ဗျား)(?=\s*။)",
                "Target",
                "ခင်ဗျာ",
                "ခင်ဗျာ (ယဉ်ကျေးသော အာလုပ်စကား ~)",
                "နှုတ်ခွန်းဆက် အာလုပ်စကားတွင် ဝစ္စပေါက်မပါဘဲ 'မင်္ဂလာပါ ခင်ဗျာ' ဟု သုံးနှုန်းရမည် ('ခင်ဗျား' သည် နာမ်စားဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=အကူအညီ\s*)(?<Target>တောင်)(?=ပါ|သည်|တယ်|ခံ)",
                "Target",
                "တောင်း",
                "တောင်း (အကူအညီ~ခံသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လိုလားချက်တောင်းခံသည့် ကြိယာကို ဝစ္စပေါက်ဖြင့် 'တောင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ကြောင်း\s*)(?<Target>ရေ)(?=ပါ(?:\s*။|$))",
                "Target",
                "ရေး",
                "ရေး (စာ~သားသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာရေးသားခြင်း အနက်တွင် ဝစ္စပေါက်ဖြင့် 'ရေး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=စကား\s*)(?<Target>ဖျော်)(?=တာ|သည်|တယ်|ရင်း|ကြ)",
                "Target",
                "ပြော",
                "ပြော (စကား~ဆိုသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စကားဆိုခြင်းကို 'စကားပြော' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဆိုတာ\s+)(?<Target>သီ)(?=ချင်)",
                "Target",
                "သိ",
                "သိ (~ချင်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သိရှိလိုခြင်းအနက်တွင် လုံးကြီးတင်ဖြင့် 'သိချင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ခေါက်ဆွဲ)(?<Target>ပြု)(?=\s+(?:သောက်|စား))",
                "Target",
                "ပြုတ်",
                "ခေါက်ဆွဲပြုတ် (ရေနွေးဖြင့်ပြုတ်ထားသော ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တဝမ်းပူသတ်ဖြင့် 'ခေါက်ဆွဲပြုတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လက်ဖက်သုတ်)",
                "Target",
                "လက်ဖက်သုပ်",
                "လက်ဖက်သုပ် (နယ်ဖတ်သုပ်ထားသော အစားအစာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အစားအစာနယ်ဖတ်သုပ်ခြင်းကို ပသတ်ဖြင့် 'လက်ဖက်သုပ်' ဟု ရေးသားရမည် ('သုတ်' သည် ဖုန်သုတ်/ဆေးသုတ် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အချက်အရက်)",
                "Target",
                "အချက်အလက်",
                "အချက်အလက် (သတင်း~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လလိမ်ဖြင့် 'အချက်အလက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကိစ္စရုပ်)(?=တွေ|များ)",
                "Target",
                "ကိစ္စရပ်",
                "ကိစ္စရပ် (လုပ်ငန်း~များ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တစ်ချောင်းငင်မပါဘဲ 'ကိစ္စရပ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အသက်ပယ်)(?=ခဲ့ကြ)",
                "Target",
                "အသက်ပေး",
                "အသက်ပေး (နိုင်ငံတော်အတွက် ~ခဲ့ကြသည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အသက်ပေးခဲ့ကြသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သတ္တိရမိ)",
                "Target",
                "သတိရမိ",
                "သတိရမိ (အောက်မေ့~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မေ့အမှတ်ရခြင်းကို ပါဋ္ဌ်ဆင့်မပါဘဲ 'သတိရမိ' ဟု ရေးသားရမည် ('သတ္တိ' သည် ရဲစွမ်းသတ္တိ ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အချိန်ကုန်း)(?=\s*သက်သာ)",
                "Target",
                "အချိန်ကုန်",
                "အချိန်ကုန် (~သက်သာသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်မပါဘဲ 'အချိန်ကုန်သက်သာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ငံ့ပြာရည်)",
                "Target",
                "ငံပြာရည်",
                "ငံပြာရည် (ဟင်းခတ် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ်မပါဘဲ 'ငံပြာရည်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပျော့စရာ)(?=ကောင်း)",
                "Target",
                "ပျော်စရာ",
                "ပျော်စရာ (~ကောင်းသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ်မပါဘဲ ရှေ့ထိုးဖြင့် 'ပျော်စရာကောင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=မြို့)(?<Target>ထွင်း)(?=သွားလာ)",
                "Target",
                "တွင်း",
                "မြို့တွင်း (~သွားလာသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တဝမ်းပူဖြင့် 'မြို့တွင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပူပင်)(?=ခြောက်သွေ့)",
                "Target",
                "ပူပြင်း",
                "ပူပြင်း (ရာသီဥတု ~ခြောက်သွေ့သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရာသီဥတုပူအိုက်ခြင်းကို 'ပူပြင်းခြောက်သွေ့' ဟု ရေးသားရမည် ('ပူပင်' သည် စိတ်ပူပင်သောက ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အခွန်ဆောင်း)(?=\s*(?:ဖို့|ရန်|ကြ|ရ|သူ|ခြင်း|မှု|သည်|တယ်))",
                "Target",
                "အခွန်ဆောင်",
                "အခွန်ဆောင် (နိုင်ငံတော်သို့ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်မပါဘဲ 'အခွန်ဆောင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဘောပင်း)",
                "Target",
                "ဘောပင်",
                "ဘောပင် (စာရေးကိရိယာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်မပါဘဲ 'ဘောပင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဆော့ဝဲလ်)",
                "Target",
                "ဆော့ဖ်ဝဲ",
                "ဆော့ဖ်ဝဲ (ကွန်ပျူတာ Software)",
                "မြန်မာစာ မွေးစားစာလုံး စံသတ်ပုံအရ ဖသတ် ('ဖ်') ပါဝင်သော 'ဆော့ဖ်ဝဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အဒါ)(?=ကြောင့်|ကြောင့်)",
                "Target",
                "အဲဒါ",
                "အဲဒါ (~ကြောင့်)",
                "နောက်ပြန်ညွှန်း နာမ်စားကို နောက်ပစ်ဖြင့် 'အဲဒါကြောင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=တစ်)(?<Target>ဂု)(?=ပါ|တည်း|စီ|\s*။)",
                "Target",
                "ခု",
                "တစ်ခု (မျိုးပြပစ္စည်း ~)",
                "မျိုးပြပစ္စည်းကို ခခွေး တစ်ချောင်းငင်ဖြင့် 'တစ်ခု' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မဟဂျွတ်)",
                "Target",
                "မဟဂ္ဂုတ်",
                "မဟဂ္ဂုတ် (ပါဠိ — ဈာန် ~ကုသိုလ်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် ပါဠိရေးထုံးအရ ဂငယ်ပါဋ္ဌ်ဆင့်ဖြင့် 'မဟဂ္ဂုတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ခွင့်မူချက်|ခွင့်မူချက်)",
                "Target",
                "ခွင့်ပြုချက်",
                "ခွင့်ပြုချက် (တရားဝင် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ခွင့်ပြုချက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပွန်ထမ်း)",
                "Target",
                "ဝန်ထမ်း",
                "ဝန်ထမ်း (နိုင်ငံ့~၊ ကုမ္ပဏီ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝလုံးဖြင့် 'ဝန်ထမ်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>သီချန်)(?=တွေ|များ|နှင့်|နှင့်|နဲ့|ကို|သံ|ဆို|နားထောင်)",
                "Target",
                "သီချင်း",
                "သီချင်း (တေး~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ငသတ် ဝစ္စပေါက်ဖြင့် 'သီချင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သယံဇာ)(?!တ)",
                "Target",
                "သယံဇာတ",
                "သယံဇာတ (သဘာဝ ~ပစ္စည်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'သယံဇာတ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=သူ(?:နှင့်|နှင့်|နဲ့)\s+)(?<Target>လမ်းခွ)(?=ခဲ့|သွား|ကြ)",
                "Target",
                "လမ်းခွဲ",
                "လမ်းခွဲ (တစ်ဦးနှင့်တစ်ဦး ~ခဲ့သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခွဲခွာသွားခြင်း ကြိယာအနက်တွင် နောက်ပစ်ဖြင့် 'လမ်းခွဲ' ဟု ရေးသားရမည် ('လမ်းခွ' သည် လမ်းဆုံလမ်းခွ နာမ်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>လုအပ်)(?=တယ်|သည်|ပါ|သော|သည့်|သည့်|တဲ့)",
                "Target",
                "လိုအပ်",
                "လိုအပ် (~ချက်၊ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လုံးကြီးတင် တစ်ချောင်းငင်ဖြင့် 'လိုအပ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဖမ်းစီ)(?=\s*အရေးယူ|၍|ပြီး|လျက်|ထိန်းသိမ်း|ခြင်း|မှု|သည်|တယ်|ရန်|ဖို့)",
                "Target",
                "ဖမ်းဆီး",
                "ဖမ်းဆီး (~အရေးယူသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆလိမ် လုံးကြီးတင်ဆန်ခတ် ဝစ္စပေါက်ဖြင့် 'ဖမ်းဆီး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ရေသား)(?=ထား|ခဲ့|သည်|တယ်|ပါ|ဖော်ပြ)",
                "Target",
                "ရေးသား",
                "ရေးသား (စာပေ ~ဖော်ပြသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်ဖြင့် 'ရေးသား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=စာဖတ်)(?<Target>ခြင်းနှင့်)(?=\s+ဗဟုသုတ)",
                "Target",
                "ခြင်းဖြင့်",
                "ခြင်းဖြင့် (စာဖတ်~ ဗဟုသုတရရှိသည်)",
                "အကြောင်းရင်း/အသုံးခံပြ နာမ်ဝိဘတ်တွင် 'နှင့်' အစား 'ဖြင့်' ကို သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<=စီးပွားရေးကို\s+)(?<Target>မြင့်တက်ပေး)",
                "Target",
                "မြှင့်တင်ပေး",
                "မြှင့်တင်ပေး (တိုးတက်အောင် ~သည်)",
                "ကံပုဒ် ('~ကို') နောက်လိုက် ကံလိုကြိယာဖြစ်သဖြင့် ဟထိုးဖြင့် 'မြှင့်တင်ပေး' ဟု ရေးသားရမည် ('မြင့်တက်' သည် ကံမလိုကြိယာ ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အထောက်အပံ့ပြု)(?=တယ်|သည်|ပါ)",
                "Target",
                "အထောက်အကူပြု",
                "အထောက်အကူပြု (ကူညီပံ့ပိုး ~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အထောက်အကူပြုသည်' (သို့မဟုတ် 'အထောက်အပံ့ပေးသည်') ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ရှာဖွေခြင်းဟာ|ဖြေရှင်းဖို့က|ဖြေရှင်းဖိုက)\s+)(?<Target>ခက်ထန်)(?=နိုင်)",
                "Target",
                "ခက်ခဲ",
                "ခက်ခဲ (မလွယ်ကူဘဲ ~သည်)",
                "မလွယ်ကူခြင်း အနက်ဖြစ်သဖြင့် 'ခက်ခဲ' ဟု သုံးနှုန်းရမည် ('ခက်ထန်' သည် စိတ်သဘောထား ကြမ်းတမ်းခက်ထန်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=အရသာရှိပြီး\s+)(?<Target>စုံလုံ)(?=တယ်|သည်|ပါ)",
                "Target",
                "စုံလင်",
                "စုံလင် (အမယ်စုံ ပြည့်စုံ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'စုံလင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ပေးဖို့)(?<Target>လုံ)(?=ပါတယ်|ပါသည်)",
                "Target",
                "လို",
                "လိုအပ်သည် (~ဖို့လိုပါသည်)",
                "လိုအပ်ခြင်း အနက်ဖြစ်သဖြင့် သေးသေးတင်မပါဘဲ 'ပေးဖို့လိုပါတယ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ထွေး)(?=တစ်ကောင်)",
                "Target",
                "ခွေး",
                "ခွေး (အိမ်မွေးတိရစ္ဆာန် ~တစ်ကောင်)",
                "အိမ်မွေးတိရစ္ဆာန်အနက်တွင် ခခွေး ဝဆွဲဖြင့် 'ခွေးတစ်ကောင်' ဟု ရေးသားရမည်။"
            ),
            // V65 Verified High-Precision Collocation, Homophone & Orthographic Frame Rules
            new CollocationRule(
                @"(?<Target>နိဿဂ္ဂိပါစိတ်)(?!တို့)",
                "Target",
                "နိဿဂ္ဂိယပါစိတ်",
                "နိဿဂ္ဂိယပါစိတ် (ဝိနည်း ~အာပတ်)",
                "ပါဠိဝိနည်း စံသတ်ပုံအရ 'နိဿဂ္ဂိယပါစိတ်' ဟု ယပက်လက်ထည့်သွင်း ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ဟောအပ်|ဆိုအပ်|ဖြစ်|ရှိ))(?<Target>ကုမ်)(?=၏)",
                "Target",
                "ကုန်",
                "ကုန်၏ (အများပြကြိယာနောက်ဆက် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နသတ်ဖြင့် 'ကုန်၏' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဂုဏ်ယူဆရာ)(?=ပင်ဖြစ်|ဖြစ်|ကောင်း)",
                "Target",
                "ဂုဏ်ယူစရာ",
                "ဂုဏ်ယူစရာ (~ပင်ဖြစ်သည်)",
                "ကြိယာနောက်ဆက် နာမ်ပြုပစ္စည်းတွင် စလုံးဖြင့် 'ဂုဏ်ယူစရာ' ဟု ရေးသားရမည် ('ဆရာ' သည် သင်ကြားပေးသူ နာမ်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:ချီးကျူး|လေးစား|အံ့ဩ|ဝမ်းသာ)(?<Target>ဆရာ)(?=ပင်ဖြစ်|ဖြစ်|ကောင်း)",
                "Target",
                "စရာ",
                "စရာ (ချီးကျူး~ / လေးစား~)",
                "ကြိယာနောက်ဆက် နာမ်ပြုပစ္စည်းတွင် စလုံးဖြင့် 'စရာ' ဟု ရေးသားရမည်။"
            ),
            // V66 Verified Zero-FP Collocations, Idioms, Real-Word Confusions & IT Loanwords
            new CollocationRule(
                @"(?<Target>စွဲဆောင်မူ)",
                "Target",
                "စွဲဆောင်မှု",
                "စွဲဆောင်မှု (~ရှိသည်)",
                "ကြိယာနာမ်ပြုပစ္စည်းတွင် ဟထိုးဖြင့် 'စွဲဆောင်မှု' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပေါ်ထက်)(?=နေ|လာ)",
                "Target",
                "ပေါ်ထွက်",
                "ပေါ်ထွက် (~လာသည် / ~နေသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝဆွဲဖြင့် 'ပေါ်ထွက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"တော်မူသည်(?<Target>ကာ)(?=၊)",
                "Target",
                "ကား",
                "သည်ကား (စာပေသုံး အလေးအနက်ပြ ~)",
                "စာပေအရေးအသား 'တော်မူသည်ကား၊' တွင် ဝစ္စပေါက်ဖြင့် 'ကား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ဆေးကုသ|သယ်ယူပို့ဆောင်|ပညာသင်)(?<Target>စရစ်)",
                "Target",
                "စရိတ်",
                "စရိတ် (ကုန်ကျ~ / ဆေးကုသ~)",
                "ကုန်ကျစရိတ် အနက်တွင် တသတ်ဖြင့် 'စရိတ်' ဟု ရေးသားရမည် ('စရစ်' သည် ငှက်အမည်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"အင်ဂျင်နီယာများက\s+(?<Target>အရသာသော)(?=\s+စမ်းသပ်ချက်)",
                "Target",
                "အသေးစိတ်သော",
                "အသေးစိတ်သော (~စမ်းသပ်ချက်)",
                "နည်းပညာစမ်းသပ်ချက် စကားစပ်အရ 'အသေးစိတ်သော' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"စိ[ 	]*စစ်ပြီးမှ\s+(?<Target>ယုံစား)(?=သင့်)",
                "Target",
                "ယုံကြည်",
                "ယုံကြည် (စိစစ်ပြီးမှ ~သင့်သည်)",
                "သတင်းအချက်အလက် စိစစ်လက်ခံရာတွင် 'ယုံကြည်သင့်သည်' ဟု သုံးနှုန်းရမည် ('ယုံစား' သည် အလွယ်တကူ နားယောင်ယုံစားခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"ပျော်စရာ(?<Target>လှပ)(?=ခဲ့|တယ်|သည်)",
                "Target",
                "ကောင်း",
                "ပျော်စရာကောင်း (~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'ပျော်စရာကောင်းသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စိတ်မကောင်းပက်လက်)",
                "Target",
                "ဝမ်းနည်းပက်လက်",
                "ဝမ်းနည်းပက်လက် (~ဖြစ်သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'ဝမ်းနည်းပက်လက်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"အချိန်ကုန်(?<Target>သက်ဆိုင်)(?=တယ်|သည်|ပါ)",
                "Target",
                "သက်သာ",
                "အချိန်ကုန်သက်သာ (~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အချိန်ကုန်သက်သာသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"အရက်ကို\s+(?<Target>ရပ်သင့်)",
                "Target",
                "ဖြတ်သင့်",
                "ဖြတ်သင့် (ဆေးလိပ်/အရက် ~သည်)",
                "ဆေးလိပ်နှင့် အရက် စွန့်လွှတ်ရာတွင် 'ဖြတ်သင့်သည်' ဟု တွဲဖက်သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"စိတ်ဝင်စားဖို့(?<Target>မှန်)(?=တယ်|သည်)",
                "Target",
                "ကောင်း",
                "စိတ်ဝင်စားဖို့ကောင်း (~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'စိတ်ဝင်စားဖို့ကောင်းသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သာယာပတ်ဝန်းကျင်)",
                "Target",
                "သဘာဝပတ်ဝန်းကျင်",
                "သဘာဝပတ်ဝန်းကျင် (Environment ~)",
                "မြန်မာစာ စံအသုံးအနှုန်းအရ 'သဘာဝပတ်ဝန်းကျင်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"စွမ်းအင်(?<Target>အရင်းအနှီး)(?=သစ်)",
                "Target",
                "အရင်းအမြစ်",
                "စွမ်းအင်အရင်းအမြစ် (Energy Resource ~)",
                "စွမ်းအင်ဆိုင်ရာ စံအသုံးအနှုန်းအရ 'စွမ်းအင်အရင်းအမြစ်' ဟု သုံးနှုန်းရမည် ('အရင်းအနှီး' သည် ငွေကြေးရင်းနှီးမြှုပ်နှံမှု Capital ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"စိတ်အနှောင့်အယှက်(?<Target>ပြုလုပ်)",
                "Target",
                "ဖြစ်စေ",
                "စိတ်အနှောင့်အယှက်ဖြစ်စေ (~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'စိတ်အနှောင့်အယှက်ဖြစ်စေသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"နံနက်စာမှာ\s+(?<Target>ဆန်ကြော်)",
                "Target",
                "ထမင်းကြော်",
                "ထမင်းကြော် (နံနက်စာ ~)",
                "မြန်မာစာ အစားအစာအခေါ်အဝေါ်အရ 'ထမင်းကြော်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"ယာဉ်ကြော(?<Target>ပိတ်ပင်)(?=မှု)",
                "Target",
                "ပိတ်ဆို့",
                "ယာဉ်ကြောပိတ်ဆို့မှု (Traffic Congestion ~)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'ယာဉ်ကြောပိတ်ဆို့မှု' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"အကျိုးခံစားခွင့်(?:တွေ|များ)ကလည်း\s+(?<Target>အရေးလုပ်)",
                "Target",
                "အရေးပါ",
                "အရေးပါ (~သည်)",
                "အရေးကြီး/အဓိကကျခြင်း အနက်တွင် 'အရေးပါသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"စားလို့(?<Target>များ)(?=တယ်|သည်)",
                "Target",
                "ကောင်း",
                "စားလို့ကောင်း (အရသာရှိ ~သည်)",
                "ဟင်းလျာအရသာ ကောင်းမွန်ခြင်းအနက်တွင် 'စားလို့ကောင်းသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"သန့်ရှင်းရေး(?<Target>ယူ)(?=ပါတယ်|ပါသည်|တယ်|သည်)",
                "Target",
                "လုပ်",
                "သန့်ရှင်းရေးလုပ် (~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'သန့်ရှင်းရေးလုပ်သည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>တိုးပွား)(?=ပြောင်းလဲနေ)",
                "Target",
                "တိုးတက်",
                "တိုးတက်ပြောင်းလဲ (နည်းပညာ ~နေသည်)",
                "နည်းပညာ ဖွံ့ဖြိုးပြောင်းလဲခြင်းတွင် 'တိုးတက်ပြောင်းလဲနေသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"စိတ်ကို\s+(?<Target>လန်းသစ်)(?=စေ)",
                "Target",
                "လန်းဆန်း",
                "လန်းဆန်း (စိတ်ကို ~စေသည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'စိတ်ကို လန်းဆန်းစေသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"အရည်အသွေး(?<Target>များတဲ့)",
                "Target",
                "ကောင်းတဲ့",
                "အရည်အသွေးကောင်း (~သော)",
                "အရည်အသွေးနှင့် တွဲဖက်ရာတွင် 'အရည်အသွေးကောင်းတဲ့/မြင့်မားတဲ့' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"ရောဂါ(?:တွေ|များ)ကို\s+(?<Target>ဖျက်ဆီး)(?=ပေးနိုင်)",
                "Target",
                "ကုသ",
                "ရောဂါကုသ (~ပေးနိုင်သည်)",
                "ဆေးပညာဖြင့် ရောဂါပျောက်ကင်းစေရာတွင် 'ရောဂါကုသပေးနိုင်သည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"မီးလောင်(?:မှု|မူ)ဖြစ်ရင်\s+(?<Target>အမြန်ကောင်း)(?=\s+ထွက်ပြေး)",
                "Target",
                "အမြန်ဆုံး",
                "အမြန်ဆုံး (~ထွက်ပြေးရမည်)",
                "အရေးပေါ်အခြေအနေတွင် 'အမြန်ဆုံး' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"ငွေကြေးဖောင်းပွမှုက\s+စီးပွားရေးကို\s+(?<Target>ထိန်းသိမ်းစေတယ်)",
                "Target",
                "ထိခိုက်စေတယ်",
                "ထိခိုက်စေသည် (စီးပွားရေးကို ~)",
                "ငွေကြေးဖောင်းပွမှု၏ ဆိုးကျိုးအနက်ဖြစ်သဖြင့် 'ထိခိုက်စေတယ်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"အာဟာရ(?<Target>ပြည့်ကျပ်အောင်)",
                "Target",
                "ပြည့်ဝအောင်",
                "အာဟာရပြည့်ဝ (~အောင်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အာဟာရပြည့်ဝအောင်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"ဖြစ်ပါစေလို့\s+(?<Target>ဆုပေး)(?=ကြ)",
                "Target",
                "ဆုတောင်း",
                "ဆုတောင်း (နှစ်သစ် ~ကြသည်)",
                "ကောင်းမွန်စေကြောင်း မေတ္တာပို့ရာတွင် 'ဆုတောင်းကြသည်' ဟု သုံးနှုန်းရမည် ('ဆုပေး' သည် ဆုချီးမြှင့်ခြင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"ညစ်ညမ်းမှုက\s+[^။]{0,18}?(?<Target>တိုးတက်)(?=လာ)",
                "Target",
                "တိုးပွား",
                "တိုးပွား (ညစ်ညမ်းမှု ~များပြားလာသည်)",
                "ညစ်ညမ်းမှု ပမာဏများပြားလာခြင်းတွင် 'တိုးပွားလာသည်' (သို့မဟုတ် 'မြင့်တက်လာသည်') ဟု သုံးနှုန်းရမည် ('တိုးတက်' သည် ကောင်းမွန်ဖွံ့ဖြိုးခြင်း၌ သုံးသည်)။"
            ),
            new CollocationRule(
                @"ယုံကြည်သူတို့အတွက်\s+(?<Target>အဓိကထားပါတယ်)",
                "Target",
                "အဓိကကျပါတယ်",
                "အဓိကကျ (~သည်)",
                "ကတ္တားပုဒ် ('~ခြင်းဟာ') ၏ အရေးပါမှုကို ပြရာတွင် 'အဓိကကျပါတယ်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"အဖွဲ့တစ်ဖွဲ့ရဲ့\s+(?<Target>စွမ်းအင်)(?=ဟာ\s+တစ်ဦးချင်း)",
                "Target",
                "စွမ်းအား",
                "စွမ်းအား (အဖွဲ့၏ စုပေါင်း~)",
                "လူပုဂ္ဂိုလ်/အဖွဲ့အစည်း၏ စွမ်းဆောင်နိုင်စွမ်းတွင် 'စွမ်းအား' ဟု သုံးနှုန်းရမည် ('စွမ်းအင်' သည် လျှပ်စစ်/နေရောင်ခြည် Energy ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"အမှန်တရားကို\s+(?<Target>ဖော်ပြဖို့က)",
                "Target",
                "ဖော်ထုတ်ဖို့က",
                "အမှန်တရားကို ဖော်ထုတ် (~သည်)",
                "အမှုအခင်း၏ အမှန်တရား ပေါ်ပေါက်စေရာတွင် 'အမှန်တရားကို ဖော်ထုတ်သည်' ဟု တွဲဖက်သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကွန်ဖစ်ကူရေးရှင်း)",
                "Target",
                "ကွန်ဖီဂူရေးရှင်း",
                "ကွန်ဖီဂူရေးရှင်း (Configuration)",
                "နည်းပညာမွေးစားစာလုံး စံနှုန်းအရ 'ကွန်ဖီဂူရေးရှင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ဒေတာ[ 	]+(?<Target>ဘေ့)(?=[ 	]+အပ်|စနစ်|များ|ကို)",
                "Target",
                "ဘေ့စ်",
                "ဒေတာဘေ့စ် (Database)",
                "နည်းပညာမွေးစားစာလုံး Database တွင် စသတ်ဖြင့် 'ဘေ့စ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဗိုင်ယာလက်ရှင်း)",
                "Target",
                "ဗိုင်အိုလေးရှင်း",
                "ဗိုင်အိုလေးရှင်း (Violation)",
                "မွေးစားစာလုံး Violation အသံထွက်အရ 'ဗိုင်အိုလေးရှင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အဲဂျိုင်)(?=နည်း|စနစ်)",
                "Target",
                "အဲဂျိုင်းလ်",
                "အဲဂျိုင်းလ် (Agile)",
                "နည်းပညာမွေးစားစာလုံး Agile ကို 'အဲဂျိုင်းလ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပိုဂျက်)(?=တစ်ခု|တွေ|များ|ကို)",
                "Target",
                "ပရောဂျက်",
                "ပရောဂျက် (Project)",
                "မွေးစားစာလုံး Project ကို ရရစ်ပါလျက် 'ပရောဂျက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ဆာ)(?=ပေပို့ချ)",
                "Target",
                "စာ",
                "စာပေ (~ပို့ချသင်ကြားသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စလုံးဖြင့် 'စာပေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>အာမကံ)(?=ဝန်ဆောင်)",
                "Target",
                "အာမခံ",
                "အာမခံ (~ဝန်ဆောင်မှု)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခခွေးဖြင့် 'အာမခံ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အစိတ်အဆိုင်း)",
                "Target",
                "အစိတ်အပိုင်း",
                "အစိတ်အပိုင်း (ပါဝင်သော ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပစောက်ဖြင့် 'အစိတ်အပိုင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ထိန်းသိမ်း(?<Target>ပေါင့်|ပေါင့်)(?=ရှောက်)",
                "Target",
                "စောင့်",
                "ထိန်းသိမ်းစောင့်ရှောက် (~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စလုံးဖြင့် 'ထိန်းသိမ်းစောင့်ရှောက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:သင်ခန်းစာ|သင်ခဏ်းစာ)(?:တွေ|များ)\s+(?<Target>ဖြတ်)(?=စေ(?:တယ်|သည်))",
                "Target",
                "ဖြစ်",
                "ဖြစ်စေသည် (သင်ခန်းစာ ~)",
                "ဖြစ်ပေါ်စေခြင်း အနက်ဖြစ်သဖြင့် စသတ်ဖြင့် 'ဖြစ်စေသည်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"အသပြာ(?<Target>ခို)(?=\s+ကိုယ်)",
                "Target",
                "ကို",
                "ကို (ကံဝိဘတ် ~)",
                "ကံဝိဘတ်တွင် ကကြီးဖြင့် 'အသပြာကို' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"အစ္စရေး[ \t]*စစ်(?<Target>ဟား)(?=\s*တွေ)",
                "Target",
                "သား",
                "စစ်သား (တပ်မတော် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သကြီးဖြင့် 'စစ်သား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ယဉ်ကျေးမှု|ယဉ်ကျေးမု)(?<Target>အထစ်)(?=တွေ|များ)",
                "Target",
                "အသစ်",
                "အသစ် (~အဆန်းများ)",
                "အသစ်အဆန်း အနက်တွင် သကြီးဖြင့် 'အသစ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ရုတာ)(?=\s+(?:တစ်|နှစ်|သုံး|လေး|ငါး|[၁-၉])လုံး)",
                "Target",
                "ရူတာ",
                "ရူတာ (ကွန်ရက် Router)",
                "မွေးစားစာလုံး စံသတ်ပုံအရ နှစ်ချောင်းငင်ဖြင့် 'ရူတာ' (Router) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"မြန်မာ့[ \t]*(?<Target>ရွှဲ)(?=ထည်ပစ္စည်း)",
                "Target",
                "ရွှေ",
                "ရွှေထည်ပစ္စည်း (မြန်မာ့~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သဝေထိုးဖြင့် 'ရွှေထည်ပစ္စည်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အမွမ်းအတင်)",
                "Target",
                "အမွှမ်းအတင်",
                "အမွှမ်းအတင် (ချီးကျူး~ခံရသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဟထိုးဖြင့် 'အမွှမ်းအတင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ပျောင်း)(?=လဲ(?:စေ|နေ|လာ|သွား|မှု))",
                "Target",
                "ပြောင်း",
                "ပြောင်းလဲ (~စေသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရရစ်ဖြင့် 'ပြောင်းလဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"လူမျိုး(?<Target>ထွေ)(?=ရဲ့|၏|က|ကို)",
                "Target",
                "တွေ",
                "တွေ (အများပြပစ္စည်း ~)",
                "အများပြပစ္စည်းကို တဝမ်းပူဖြင့် 'လူမျိုးတွေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:မနေ့က|ကလေးတွေက|ကျောင်းသားတွေက)\s+(?<Target>ကြောင်း)(?=သွား(?:ခဲ့|ဖို့|ကြ|မယ်|သည်))",
                "Target",
                "ကျောင်း",
                "ကျောင်း (စာသင်~သွားသည်)",
                "စာသင်ကျောင်းသို့ သွားရောက်ခြင်းအနက်တွင် ယပင့်ဖြင့် 'ကျောင်းသွား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:^|[\u104A\u104B]\s*)(?<Target>ကျေးခြင်း)(?=သည်\s+ကျန်းမာရေး)",
                "Target",
                "ပြေးခြင်း",
                "ပြေးခြင်း (အားကစား ~)",
                "ကျန်းမာရေးလေ့ကျင့်ခန်း အနက်တွင် 'ပြေးခြင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ကောင်းမှု(?:တွေ|များ)\s+(?<Target>ပြူ)(?=ကြ(?:ပါ)?(?:တယ်|သည်))",
                "Target",
                "ပြု",
                "ပြု (ကုသိုလ်ကောင်းမှု ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တစ်ချောင်းငင်ဖြင့် 'ပြုကြသည်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ကန်တော့ကြတဲ့\s*(?<Target>ဓလဲ့)",
                "Target",
                "ဓလေ့",
                "ဓလေ့ (ရိုးရာ~ထုံးစံ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သဝေထိုး အောက်မြစ်ဖြင့် 'ဓလေ့' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:တက်ခဲ့|ပြောခဲ့|လုပ်ခဲ့|ကြိုက်)တာ(?<Target>ခြင်း)(?=(?:လည်း)?တူ)",
                "Target",
                "ချင်း",
                "ချင်း (~တူညီသည်)",
                "အချင်းချင်း တူညီမှုပြပစ္စည်းတွင် ယပင့်ဖြင့် 'တာချင်းတူ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:^|[\s၊။])(?<Target>ပဲ့)(?=၊\s*နှမ်း)",
                "Target",
                "ပဲ",
                "ပဲ (~၊ နှမ်း အစားအစာ)",
                "စားဖွယ် ပဲသီးနှံအနက်တွင် အောက်မြစ်မပါဘဲ 'ပဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"အင်တာဗျူး(?<Target>အတွင်)(?=\s)",
                "Target",
                "အတွင်း",
                "အတွင်း (ကာလ~)",
                "ကာလအပိုင်းအခြားပြ နာမ်ဝိဘတ်တွင် ဝစ္စပေါက်ဖြင့် 'အတွင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ဆိုင်(?:တွေ|များ)မှာ\s+(?<Target>မုန်)(?=မျိုးစုံ)",
                "Target",
                "မုန့်",
                "မုန့် (စားစရာ ~မျိုးစုံ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ်ဖြင့် 'မုန့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"အခွင့်အလမ်း(?:တွေ|များ)\s+(?<Target>ပို့)(?=ရစေ)",
                "Target",
                "ပို",
                "ပို (~မိုရရှိစေသည်)",
                "သာလွန်ပိုမိုခြင်း အနက်တွင် အောက်မြစ်မပါဘဲ 'ပိုရစေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ကျွန်တော်|ကျွန်မ|သူ)\s+(?<Target>ရုံ)(?=သို့\s+ရောက်)",
                "Target",
                "ရုံး",
                "ရုံး (အလုပ်~သို့ ရောက်သည်)",
                "အလုပ်ဌာန အနက်တွင် ဝစ္စပေါက်ဖြင့် 'ရုံးသို့' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"လာသည်[ \t]+(?<Target>အတိုင်)(?=\s)",
                "Target",
                "အတိုင်း",
                "အတိုင်း (ကျမ်းစာလာသည့် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်ဖြင့် 'အတိုင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>အစ္စရေ)(?=\s+အနေ)",
                "Target",
                "အစ္စရေး",
                "အစ္စရေး (~နိုင်ငံ)",
                "နိုင်ငံအမည် စံသတ်ပုံအရ ဝစ္စပေါက်ဖြင့် 'အစ္စရေး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ဒါပေမဲ့|ဒါပေမယ့်|ဒါပေမယ့်)\s+(?<Target>လိုင်)(?=ကတော့)",
                "Target",
                "လိုင်း",
                "လိုင်း (အင်တာနက်/ဖုန်း ~)",
                "ဆက်သွယ်ရေးလိုင်း အနက်တွင် ဝစ္စပေါက်ဖြင့် 'လိုင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:^|[\u104B]\s*)(?<Target>ဤကာ)(?=\s+သီလ)",
                "Target",
                "ဤကား",
                "ဤကား (စာပေသုံး ညွှန်းပြပုဒ် ~)",
                "စာပေအရေးအသားတွင် ဝစ္စပေါက်ဖြင့် 'ဤကား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>မြက်ဖျာတွင်)(?=\s+ကပ်သော)",
                "Target",
                "မြက်ဖျားတွင်",
                "မြက်ဖျားတွင် (မြက်ပင်အဖျား၌ ~)",
                "မြက်ပင်၏ အဖျားပိုင်းအနက်တွင် ဝစ္စပေါက်ဖြင့် 'မြက်ဖျားတွင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ပျာ)(?=အနံ့ရှိ)",
                "Target",
                "ပျား",
                "ပျား (~ရည်အနံ့)",
                "ပျားရည်အနံ့ အနက်တွင် ဝစ္စပေါက်ဖြင့် 'ပျားအနံ့' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ဒီစီကြောင်း)(?=ထဲက)",
                "Target",
                "ဒီစီးကြောင်း",
                "စီးကြောင်း (ရေ~)",
                "စီးဆင်းရာလမ်းကြောင်း အနက်တွင် ဝစ္စပေါက်ဖြင့် 'ဒီစီးကြောင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>သရေစ)(?=တွေ|များ)",
                "Target",
                "သရေစာ",
                "သရေစာ (မုန့်ပဲ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရေးချဖြင့် 'သရေစာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ဆွမ်း(?<Target>ခခံ)(?=ကြွ)",
                "Target",
                "ခံ",
                "ဆွမ်းခံ (~ကြွသည်)",
                "ဗျည်းနှစ်ထပ် ရိုက်မိနေပါသည် — 'ဆွမ်းခံကြွ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ပိုမို(?<Target>ခိုင်မရ)(?=စေ)",
                "Target",
                "ခိုင်မာ",
                "ခိုင်မာ (ပိုမို~စေသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ခိုင်မာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ခက်ခေ)(?=နိုင်|တယ်|သည်|ပါ)",
                "Target",
                "ခက်ခဲ",
                "ခက်ခဲ (မလွယ်ကူဘဲ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နောက်ပစ်ဖြင့် 'ခက်ခဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ထစ်သား)(?=ထြင်း|ထွင်း)",
                "Target",
                "သစ်သား",
                "သစ်သား (~ထွင်းထုပညာ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သကြီးဖြင့် 'သစ်သား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ဒီ(?<Target>ဆိုဒ်)(?=ကို\s+ဒိုမိန်း)",
                "Target",
                "ဆိုက်",
                "ဆိုက် (ဝက်ဘ်~ Website)",
                "ဝက်ဘ်ဆိုက် အနက်တွင် ကကြီးသတ်ဖြင့် 'ဆိုက်' (Site) ဟု ရေးသားရမည် ('ဆိုဒ်' သည် အရွယ်အစား Size ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:ဝယ်ယူ|ပြောဆို|လုပ်ကိုင်|ဆောင်ရွက်)(?<Target>ငိုင်)(?=တယ်|သည်|ပါ|ဘူး)",
                "Target",
                "နိုင်",
                "နိုင် (ကြိယာထောက်ပစ္စည်း ~သည်)",
                "စွမ်းဆောင်နိုင်ခြင်းပြ ပစ္စည်းကို နငယ်ဖြင့် 'နိုင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:မိန်းကလေး|ယောက်ျားလေး|ကလေး|လူငယ်)(?<Target>တိုင်က)(?=\s)",
                "Target",
                "တိုင်းက",
                "တိုင်း (လူ~ စေ့နှံ့ခြင်းပြပစ္စည်း)",
                "အားလုံးစေ့နှံ့ခြင်းပြ ပစ္စည်းတွင် ဝစ္စပေါက်ဖြင့် 'တိုင်းက' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"သွားရ(?<Target>ထက်)(?=တယ်|သည်|ပါ)",
                "Target",
                "ခက်",
                "ခက် (သွားလာရ ~ခဲသည်)",
                "မလွယ်ကူခြင်း အနက်တွင် ခခွေးဖြင့် 'သွားရခက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"စာဖတ်ခြင်းက\s+(?<Target>ဘာဝ)(?=ကို\s+အလင်းပေး)",
                "Target",
                "ဘဝ",
                "ဘဝ (လူ့~)",
                "လူ့ဘဝ အနက်တွင် ရေးချမပါဘဲ 'ဘဝ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>လျော်ကြ)(?=စေ(?:တယ်|သည်|ပါ))",
                "Target",
                "လျော့ကျ",
                "လျော့ကျ (လျော့နည်းကျဆင်း~စေသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'လျော့ကျ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"လူတိုင်းရ(?<Target>သိပ်)(?=တဲ့|သော|သည့်|သည့်)",
                "Target",
                "သင့်",
                "သင့် (ရရှိ~သော)",
                "သင့်လျော်ထိုက်တန်ခြင်းပြ ပစ္စည်းကို 'သင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ပျံသန်းနေတဲ့\s+(?<Target>မြင်တွေ့က)(?=\s+လှပ)",
                "Target",
                "မြင်ကွင်းက",
                "မြင်ကွင်း (ရှုခင်း ~)",
                "ရှုမြင်ရသော မြင်ကွင်းနာမ်ဖြစ်သဖြင့် 'မြင်ကွင်းက' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"သူတို့\s+(?<Target>ပျံ)(?=\s+ကာကွယ်)",
                "Target",
                "ပြန်",
                "ပြန် (တုံ့ပြန် ~လည်ကာကွယ်သည်)",
                "ပြန်လည်တုံ့ပြန်ခြင်း အနက်တွင် ရရစ် နသတ်ဖြင့် 'ပြန်ကာကွယ်' ဟု ရေးသားရမည် ('ပျံ' သည် ငှက်ပျံသန်းသည် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"ဒီလိုဖြစ်[ \t]+(?<Target>ကျောင်း)(?=[ \t]+ကို)",
                "Target",
                "ကြောင်း",
                "ကြောင်း (အကြောင်းအရာပြပစ္စည်း ~)",
                "အကြောင်းအရာပြ နာမ်ပြုပစ္စည်းတွင် ရရစ်ဖြင့် 'ကြောင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"အိမ်မှာ(?<Target>တိုင်)(?=ပြီး\s+ပစ္စည်း)",
                "Target",
                "ထိုင်",
                "ထိုင် (အိမ်မှာ~ပြီး)",
                "ထိုင်နေခြင်း အနက်တွင် ထဆင်ထူးဖြင့် 'ထိုင်ပြီး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ခြင်းတွေ(?<Target>ရှည်နေတတ်)",
                "Target",
                "ရှိနေတတ်",
                "ရှိနေတတ် (ဖြစ်ပေါ်~သည်)",
                "တည်ရှိဖြစ်ပေါ်နေတတ်ခြင်း အနက်တွင် 'ရှိနေတတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ကျန်းမာရေးစောင့်ရှောက်မှု(?<Target>ပို့)(?=တယ်|သည်)",
                "Target",
                "ပေး",
                "ပေး (စောင့်ရှောက်မှု ~သည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'ကျန်းမာရေးစောင့်ရှောက်မှုပေးသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"နဲ့\s+(?<Target>စာတော့)(?=\s*\()",
                "Target",
                "စားတော့",
                "စားတော့ (ကိန်းဂဏန်းဖြင့် ~)",
                "သင်္ချာစားခြင်း အနက်တွင် ဝစ္စပေါက်ဖြင့် 'စားတော့' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ငှက်အုံးများ)",
                "Target",
                "ငှက်အုပ်များ",
                "ငှက်အုပ် (ငှက်အစုအဝေး ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပသတ်ဖြင့် 'ငှက်အုပ်များ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"သိတဲ့သူတွေ\s+(?:မန့်ပေး|မန့်ပေး)(?<Target>ပို့)(?=\s+မျှော်လင့်|\s+မျှော်လင့်)",
                "Target",
                "ဖို့",
                "ဖို့ (ရည်ရွယ်ချက်ပြပစ္စည်း ~)",
                "ရည်ရွယ်ချက်ပြပစ္စည်းတွင် ဖဦးထုပ်ဖြင့် 'ဖို့' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"မှန်သမျှဟာ\s+(?<Target>လူတွေသိ)(?=\s+ကို)",
                "Target",
                "လူတွေဆီ",
                "ဆီ (လူတွေ~သို့)",
                "ဦးတည်ရာပြပုဒ်တွင် ဆလိမ်ဖြင့် 'လူတွေဆီ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"ကောင်းတဲ့(?<Target>အရံတစ်ခု)(?=ပါ)",
                "Target",
                "အရာတစ်ခု",
                "အရာတစ်ခု (ကောင်းသော ~)",
                "အရာဝတ္ထု/အချက်အလက် အနက်တွင် 'အရာတစ်ခု' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:^|[\s၊။])(?<Target>လု)(?=ငယ်တွေ\s+စီးပွားရေး)",
                "Target",
                "လူ",
                "လူငယ် (လူရွယ်~များ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နှစ်ချောင်းငင်ဖြင့် 'လူငယ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"အရက်ကို\s+(?<Target>ဆောင်ထင့်)",
                "Target",
                "ရှောင်သင့်",
                "ရှောင်သင့် (~ကြဉ်သင့်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ရှောင်သင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဝန်ထမ်းတွေရဲ့\s+)(?<Target>ဝင်ကူ)(?=ဖြစ်)",
                "Target",
                "ဝင်ငွေ",
                "ဝင်ငွေ (လစာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လုပ်ခရငွေအနက်တွင် 'ဝင်ငွေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=အရမ်း)(?<Target>အရေး(?:ကျယ်|ပေး))(?=ပါတယ်|ပါသည်)",
                "Target",
                "အရေးကြီး",
                "အရေးကြီး (အလွန် ~ပါသည်)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အရေးကြီးပါသည်' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<=သမိုင်းမှာ\s+)(?<Target>အရေးသာ)(?=တဲ့)",
                "Target",
                "အရေးပါ",
                "အရေးပါ (သမိုင်းတွင် ~သော)",
                "မြန်မာစာ တွဲဖက်အသုံးစံနှုန်းအရ 'အရေးပါတဲ့' ဟု သုံးနှုန်းရမည်။"
            ),
            new CollocationRule(
                @"(?<=ရှည်လျားတဲ့)(?<Target>ကန်ခြေ)(?=မှာ)",
                "Target",
                "ကမ်းခြေ",
                "ကမ်းခြေ (ပင်လယ် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ မသတ် ဝစ္စပေါက်ဖြင့် 'ကမ်းခြေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=အရမ်း)(?<Target>အရေကြီး)",
                "Target",
                "အရေးကြီး",
                "အရေးကြီး (~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်ဖြင့် 'အရေးကြီး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ကောလဟာလတွေ)(?<Target>ချောင်း)",
                "Target",
                "ကြောင့်",
                "ကြောင့် (အကြောင်းပြ နာမ်ဝိဘတ် ~)",
                "အကြောင်းရင်းပြ နာမ်ဝိဘတ်ကို 'ကြောင့်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သတင်းမူ)(?=ဖတ်)",
                "Target",
                "သတင်းစာ",
                "သတင်းစာ (~ဖတ်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'သတင်းစာဖတ်သည်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=အလုပ်)(?<Target>ထွက်စံ)(?=တင်)",
                "Target",
                "ထွက်စာ",
                "အလုပ်ထွက်စာ (~တင်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သေးသေးတင်အစား ရေးချဖြင့် 'အလုပ်ထွက်စာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=အစိုးရက\s+)(?<Target>ပေးမူ)(?=ပါတယ်|ပါသည်)",
                "Target",
                "ပေး",
                "ပေး (~ပါသည်)",
                "ပိုနေသော 'မူ' ကို ဖယ်ရှား၍ 'ပေးပါတယ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အသိပန်းရာ)(?=ကို)",
                "Target",
                "အသိပညာ",
                "အသိပညာ (ဗဟုသုတ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ညကြီးဖြင့် 'အသိပညာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဆေးသုတ်ပြီး\s+)(?<Target>ပြန်ပင်)(?=တာ)",
                "Target",
                "ပြန်ပြင်",
                "ပြန်ပြင် (ပြုပြင်မွမ်းမံသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရရစ်ဖြင့် 'ပြန်ပြင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ရာသီဥတုက\s+ဒီနေ့\s+)(?<Target>နေတာ)(?=တယ်|သည်)",
                "Target",
                "နေသာ",
                "နေသာ (ရာသီဥတု ကြည်လင်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သကြီးဖြင့် 'နေသာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ရေထူး)(?=တာ၊)",
                "Target",
                "ရေကူး",
                "ရေကူး (~အားကစား)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကကြီးဖြင့် 'ရေကူး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=တောင်သူ\s*)(?<Target>လယ်ဆမား)",
                "Target",
                "လယ်သမား",
                "တောင်သူလယ်သမား (~များ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သကြီးဖြင့် 'တောင်သူလယ်သမား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဖယောင်းထိုင်)",
                "Target",
                "ဖယောင်းတိုင်",
                "ဖယောင်းတိုင် (မီးထွန်း ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တဝမ်းပူဖြင့် 'ဖယောင်းတိုင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဟင်းဆတ်)(?=ခဲ့|တာ|ချက်)",
                "Target",
                "ဟင်းချက်",
                "ဟင်းချက် (ထမင်း~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဟင်းချက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဘုရားထခင်)",
                "Target",
                "ဘုရားသခင်",
                "ဘုရားသခင် (ကိုးကွယ်ရာ ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သကြီးဖြင့် 'ဘုရားသခင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ထက်ခဲ)(?=နိုင်|တယ်|သည်|သော)",
                "Target",
                "ခက်ခဲ",
                "ခက်ခဲ (မလွယ်ကူဘဲ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခခွေးဖြင့် 'ခက်ခဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ဆိတ်ထဲ)(?=က|မှာ|တွင်)",
                "Target",
                "စိတ်ထဲ",
                "စိတ်ထဲ (~၌ အတွေးပေါ်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စလုံးဖြင့် 'စိတ်ထဲ' ဟု ရေးသားရမည် ('ဆိတ်' သည် တိရစ္ဆာန်ဆိတ် ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ဆပ်ရုံ)(?=တွေ|များ|မှာ|တွင်)",
                "Target",
                "စက်ရုံ",
                "စက်ရုံ (ကုန်ထုတ် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စလုံး ကသတ်ဖြင့် 'စက်ရုံ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ကိုးကာ)(?=\s+ကဏ္ဍ)",
                "Target",
                "ကိုးကား",
                "ကိုးကား (ကျမ်း~ချက်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝစ္စပေါက်ဖြင့် 'ကိုးကား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဌာနီ၌\s+)(?<Target>ထင်စား)(?=၍)",
                "Target",
                "တင်စား",
                "တင်စား (ဥပစာ ~၍ ဆိုခြင်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တဝမ်းပူဖြင့် 'တင်စား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ရဲ့|၏|\s))(?<Target>အေလှ)(?=ကို|က|ဟာ|သည်)",
                "Target",
                "အလှ",
                "အလှ (သဘာဝ ~အပ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပိုနေသော သဝေထိုးကို ဖယ်ရှား၍ 'အလှ' ဟု ရေးသားရမည်။"
            ),

            // 14. ဖတ် (read) vs ဖက် (hug/leaf/partner)
            new CollocationRule(
                @"(?<Target>စာဖက်|ဖက်ရှု|ဖက်ကြား)",
                "Target",
                "",
                "ဖတ် (စာဖတ်~၊ ဖတ်ရှု~၊ ဖတ်ကြား~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာပေကြည့်ရှုရွတ်ဆိုခြင်းကို 'ဖတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 15. သတ် (designate/orthography) vs သက် (life/age/witness)
            new CollocationRule(
                @"(?<Target>သက်မှတ်)",
                "Target",
                "သတ်မှတ်",
                "သတ်မှတ် (စည်းကမ်း/အချိန် ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပိုင်းခြားပြဋ္ဌာန်းခြင်းကို 'သတ်မှတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:စာလုံးပေါင်း)?သက်ပုံ)",
                "Target",
                "",
                "သတ်ပုံ (စာလုံးပေါင်းသတ်ပုံကျမ်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသတ်အမှန်ရေးထုံးကို 'သတ်ပုံ' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အသတ်(?:အရွယ်|ရှင်|မွေးဝမ်းကျောင်း))",
                "Target",
                "",
                "အသက် (အသက်အရွယ်၊ အသက်ရှင်~)",
                "ဇီဝအသက်နှင့် အသက်အရွယ်အနက်အတွက် 'အသက်' (ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သတ်သေ(?:ပြ|ခံ|အထောက်အထား))",
                "Target",
                "",
                "သက်သေ (သက်သေပြ~၊ သက်သေခံ~)",
                "အထောက်အထားပြသခြင်း အနက်အတွက် 'သက်သေ' (ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 16. လပ် (vacant/free) vs လက် (hand)
            new CollocationRule(
                @"(?<Target>လစ်လက်)",
                "Target",
                "လစ်လပ်",
                "လစ်လပ် (ရာထူး/နေရာ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နေရာ/ရာထူး အားလပ်နေခြင်းကို 'လစ်လပ်' (ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အားလက်(?:ရက်|ချိန်))",
                "Target",
                "",
                "အားလပ် (အားလပ်ရက်၊ အားလပ်ချိန်)",
                "အနားယူချိန် အနက်အတွက် 'အားလပ်' (ပစောက်သတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 17. ပြတ် (sever/decisive) vs ပျက် (ruined/fail)
            new CollocationRule(
                @"(?<Target>ပျက်သား)(?=စွာ|သော|သည့်|သည်|တယ်)",
                "Target",
                "ပြတ်သား",
                "ပြတ်သား (တိကျပြတ်သားစွာ ~)",
                "တိကျသေချာခြင်း အနက်အတွက် 'ပြတ်သား' (ရရစ်၊ တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပြတ်စီး)",
                "Target",
                "ပျက်စီး",
                "ပျက်စီး (ပျက်စီးဆုံးရှုံး~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပျက်ယွင်းဆုံးရှုံးခြင်းကို 'ပျက်စီး' (ယပင့်၊ ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပြတ်ကွက်)",
                "Target",
                "ပျက်ကွက်",
                "ပျက်ကွက် (တာဝန်/အစည်းအဝေး ~သည်)",
                "တာဝန်မကျေပွန်ခြင်း/မတက်ရောက်ခြင်းကို 'ပျက်ကွက်' (ယပင့်၊ ကကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 18. ကျ (fall/fit/like) vs ကြ (plural verb marker) — guard against 'သဘောကြောင်' ('ကြောင်' = \u1000\u103C\u1031\u102C\u1004\u103A)
            new CollocationRule(
                @"(?<Target>(?:သဘော|စိတ်ဓာတ်|စနစ်တ|အံဝင်ခွင်)ကြ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ကျ (သဘောကျ~၊ စိတ်ဓာတ်ကျ~၊ စနစ်တကျ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နှစ်သက်ခြင်း/ကျဆင်းခြင်း/အံဝင်ခြင်း အနက်အတွက် 'ကျ' (ယပင့်) ဖြင့် ရေးသားရမည်။ ('ကြ' သည် အများပြကြိယာနောက်ဆက် ဖြစ်သည်)။"
            ),

            // 19. ကျင် (surroundings/ache) vs ကြင် (affectionate; exempt 'ဘေးထိုးရင်ကျင်နာ' in သတ်ပုံကျမ်း!)
            new CollocationRule(
                @"(?<Target>ပတ်ဝန်းကြင်)",
                "Target",
                "ပတ်ဝန်းကျင်",
                "ပတ်ဝန်းကျင် (သဘာဝပတ်ဝန်းကျင် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ထက်ဝန်းကျင် အနက်အတွက် 'ပတ်ဝန်းကျင်' (ယပင့်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!ရင်|ကိုယ်လက်)(?<Target>ကျင်နာ)",
                "Target",
                "ကြင်နာ",
                "ကြင်နာ (ကြင်နာသနား~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ချစ်ခင်သနားခြင်းကို 'ကြင်နာ' (ရရစ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 20. အမြဲတမ်း (always/permanent) vs အမြဲတန်း (misspelled)
            new CollocationRule(
                @"(?<Target>အမြဲတန်း)",
                "Target",
                "အမြဲတမ်း",
                "အမြဲတမ်း (ထာဝစဉ် ~၊ အမြဲတမ်းအတွင်းဝန်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'အမြဲတမ်း' (မသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 21. ဉာဏ် (wisdom/intellect with Nya-galay ဉ) vs ညာဏ် (misspelled with Nya-gyi ည; exempt 'ဝိညာဏ် -> ဝိညာဉ်')
            new CollocationRule(
                @"(?<!ဝိ)(?<Target>ညာဏ်)",
                "Target",
                "ဉာဏ်",
                "ဉာဏ် (အသိဉာဏ်၊ ဉာဏ်ပညာ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသိပညာကို 'ဉာဏ်' (ဉကလေး U+1009) ဖြင့် ရေးသားရမည်။"
            ),

            // 22. Contextual 'ဖက်' -> 'ဖတ်' after reading objects (စာအုပ်၊ သတင်းစာ၊ ဥပဒေ၊ စာချုပ် ...)
            new CollocationRule(
                @"(?:စာအုပ်|သတင်းစာ|ဂျာနယ်|မဂ္ဂဇင်း|မှတ်တမ်း|အမိန့်စာ|စာချုပ်|ဥပဒေ|စာမျက်နှာ|သင်ခန်းစာ|စာတမ်း)(?:ကို|များ|တွေ)?[\u1000-\u103F\s]{0,22}?(?<Target>ဖက်)(?=သည်|တယ်|ပြီး|၍|ပါ|ကြ|မည်|မယ်|နေ|ရှု)",
                "Target",
                "ဖတ်",
                "ဖတ် (စာအုပ်~၊ သတင်းစာ~၊ ဥပဒေ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာပေကြည့်ရှုဖတ်ရှုခြင်းကို 'ဖတ်' (တဝမ်းပူသတ်) ဖြင့် ရေးသားရမည်။"
            ),

            // 23. Contextual 'ထည်' -> 'ထည့်' and 'ပြော့' -> 'ပြော' (Aukmyit dot-below disambiguation)
            new CollocationRule(
                @"(?<Target>ထည်သွင်း)",
                "Target",
                "ထည့်သွင်း",
                "ထည့်သွင်း (စာရင်း/အချက်အလက် ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သွတ်သွင်းထည့်ထားခြင်းကို 'ထည့်သွင်း' (အောက်မြစ်ပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ရေ|ဆား|ဆီ|သကြား|ငွေ|စာရင်း|အချက်အလက်|အိတ်ထဲ|ပုံးထဲ)\s*(?<Target>ထည်)(?=ပေး|ထား|လိုက်|ပါ|သည်|တယ်|မည်|မယ်)",
                "Target",
                "ထည့်",
                "ထည့် (ထည့်သွင်း~၊ ထည့်ထား~)",
                "အတွင်းသို့ ထည့်သွင်းခြင်း အနက်အတွက် 'ထည့်' (အောက်မြစ်ပါ) ဖြင့် ရေးသားရမည်။ ('ထည်' သည် အဝတ်အထည် အတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ပြော့)(?=ကြား|ဆို|ပြ|ခွင့်|စကား)",
                "Target",
                "ပြော",
                "ပြော (ပြောကြား~၊ ပြောဆို~၊ ပြောပြ~)",
                "စကားပြောဆိုခြင်း အနက်အတွက် 'ပြော' (အောက်မြစ်မပါ) ဖြင့် ရေးသားရမည်။ ('ပြော့' သည် ပျော့ပြောင်း/ပြော့သည် အတွက်သာ သုံးသည်)။"
            ),

            // 24. Official Thatpon Book Homophone Pairs (နေ့လယ်၊ အိပ်ရာ၊ ထိန်းသိမ်း/ထိမ်းမြား၊ နှုတ်ဆက်/နုတ်ထွက်)
            new CollocationRule(
                @"(?<Target>နေ့လည်)",
                "Target",
                "နေ့လယ်",
                "နေ့လယ် (မွန်းတည့်~၊ နေ့လယ်စာ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နေ့၏အလယ်ပိုင်းကို 'နေ့လယ်' (ယပက်လက်သတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အိပ်ယာ)",
                "Target",
                "အိပ်ရာ",
                "အိပ်ရာ (အိပ်စက်ရာနေရာ၊ အိပ်ရာခင်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အိပ်စက်ရာနေရာကို 'အိပ်ရာ' (ရကောက်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?:ကျောင်း|အတန်းပိုင်|ကျောင်းအုပ်|တက္ကသိုလ်|မူလတန်း|အလယ်တန်း|အထက်တန်း)\s*(?<Target>စရာ(?:မကြီး|မ|ကြီး))|(?<![\u1000-\u103F])(?<Target>စရာ(?:မကြီး|မ|ကြီး))(?=\s*(?:ဒေါ်|ဦး|က\s|မှ\s|ကို\s)))",
                "Target",
                "",
                "ဆရာမ / ဆရာကြီး (သင်ကြားပြသသူ ပညာရှင်)",
                "သင်ကြားပြသသူ အနက်အတွက် စလုံး ('စရာ') အစား ဆလိမ်ဖြင့် 'ဆရာမ / ဆရာကြီး' ဟု ရေးသားရမည် ('စရာ' သည် 'စားစရာ/ပြောစရာ' ကြိယာနောက်ဆက်၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<=သွား|လာ|စား|သောက်|လုပ်|ပြော|ကြည့်|ဖတ်|ရေး|ပေး|ယူ|ဝယ်|သိ|နေ|ထိုင်|အိပ်|ပြန်|တင်|ဆောင်ရွက်|ကူညီ|သုံး)(?<Target>ဘို့)(?![\u102B-\u103E])",
                "Target",
                "ဖို့",
                "ဖို့ (ရည်ရွယ်ချက်ပြပစ္စည်း '~ဖို့')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြိယာနောက်ဆက် ရည်ရွယ်ချက်ပြပစ္စည်းကို ဖဦးထုပ်ဖြင့် 'ဖို့' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=သွား|လာ|စား|သောက်|လုပ်|ပြော|ကြည့်|ဖတ်|ရေး|ပေး|ယူ|ဝယ်|သိ|နေ|ထိုင်|အိပ်|ပြန်|ဆောင်ရွက်|ကူညီ)(?<Target>ဖို)(?=\s*(?:လို|အတွက်|ခက်|လွယ်|ကောင်း|သင့်|စဉ်းစား|ကြိုးစား|ပြင်ဆင်|ဆုံးဖြတ်|မဟုတ်|ပါ))",
                "Target",
                "ဖို့",
                "ဖို့ (ရည်ရွယ်ချက်ပြပစ္စည်း '~ဖို့')",
                "ကြိယာနောက်ဆက် ရည်ရွယ်ချက်ပြပစ္စည်းတွင် အောက်မြစ် ('့') ကျန်ခဲ့ပါသည် — 'ဖို' အစား 'ဖို့' ဟု ရေးသားရမည် ('ဖို' သည် မီးဖို/အဖိုအမ အနက်၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?:(?:တစ်ဦးတစ်ယောက်|တစ်ယောက်|တစ်ဦး|တစ်ကောင်|တစ်ခါ|တစ်ကြိမ်)(?<Target>ထဲ)(?![\u102B-\u103E])|(?:တစ်ခု|တစ်မျိုး)(?<Target>ထဲ)(?=\s*(?:သာ|ပဲ|ပါပဲ)))",
                "Target",
                "တည်း",
                "တည်း (တစ်ခုတည်း / တစ်ဦးတစ်ယောက်တည်း)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အရေအတွက် တစ်ခုသာဖြစ်ကြောင်း အနက်လေးနက်ပြပစ္စည်းကို 'တည်း' ဖြင့် ရေးသားရမည် ('ထဲ' သည် အတွင်းနေရာပြပုဒ် 'အိမ်ထဲ' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<!သင်္ကဿ|ပုဂံ|သရေခေတ္တရာ|အမရပူရ|ယတြာ)(?<Target>နဂိုရ်)(?=\s*(?:အတိုင်း|ကတည်းက|ကပင်|မူလ|စိတ်|ရုပ်|အခြေအနေ|ပုံစံ|သဘာဝ|အသံ|အရောင်))",
                "Target",
                "နဂို",
                "နဂို (မူလပင်ကို ~အတိုင်း)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ မူလအစ/ပင်ကို အနက်အတွက် ရကောက်သတ်မပါဘဲ 'နဂို' ဟု ရေးသားရမည် ('နဂိုရ်' သည် ပါဠိသက် 'နဂရ = မြို့' သင်္ကဿနဂိုရ် နှင့် ယတြာနဂိုရ် တို့၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?:နိုင်ငံ|ကမ္ဘာ|မြန်မာနိုင်ငံ|မြို့နယ်|တိုင်းပြည်|ဒေသ)\s*(?<Target>တစ်ဝမ်း)",
                "Target",
                "တစ်ဝန်း",
                "တစ်ဝန်း (နိုင်ငံ~၊ ကမ္ဘာ~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ နယ်ပယ်အဝန်းအဝိုင်း တစ်ခုလုံးကို နသတ်ဖြင့် 'တစ်ဝန်း' ဟု ရေးသားရမည် ('တစ်ဝမ်း' သည် 'တစ်မိတစ်ဝမ်း' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?:ညီ(?<Target>အကို)(?![\u1000-\u103F])|(?:ကျွန်တော့်|ကျွန်မ|သူ့|ငါ့|ကိုယ့်)\s*(?<Target>အကို)(?![\u1000-\u103F])|(?<![\u1000-\u103F])(?<Target>အကို)(?=ကြီး|လတ်|လေး)(?![\u1000-\u103F]))",
                "Target",
                "အစ်ကို",
                "အစ်ကို (ညီ~၊ ~ကြီး)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသက်ကြီးသော အမျိုးသားမောင်နှမကို စသတ်ဖြင့် 'အစ်ကို' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လုံလ)(?=\s*(?:ဝီရိယ|ဝိရိယ|စိုက်ထုတ်|ထုတ်|ပြု|ကြိုးကုတ်))",
                "Target",
                "လုံ့လ",
                "လုံ့လ (~ဝီရိယ၊ ~စိုက်ထုတ်-)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အားထုတ်ကြိုးပမ်းမှုကို အောက်မြစ်ပါလျက် 'လုံ့လ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ထိမ်းသိမ်း)",
                "Target",
                "ထိန်းသိမ်း",
                "ထိန်းသိမ်း (စောင့်ရှောက်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စောင့်ရှောက်ကွပ်ကဲခြင်းကို 'ထိန်းသိမ်း' (နသတ်) ဖြင့် ရေးသားရမည်။ ('ထိမ်း' ကို 'ထိမ်းမြား'၊ 'အထိမ်းအမှတ်' တို့၌ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ထိန်းမြား)",
                "Target",
                "ထိမ်းမြား",
                "ထိမ်းမြား (လက်ထပ်~သည်၊ ထိမ်းမြားမင်္ဂလာ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လက်ထပ်မင်္ဂလာပြုခြင်းကို 'ထိမ်းမြား' (မသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အထိန်းအမှတ်)",
                "Target",
                "အထိမ်းအမှတ်",
                "အထိမ်းအမှတ် (အောက်မေ့ဖွယ် ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အမှတ်တရသင်္ကေတကို 'အထိမ်းအမှတ်' (မသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>နုတ်ဆက်)",
                "Target",
                "နှုတ်ဆက်",
                "နှုတ်ဆက် (နှုတ်ခွန်းဆက်သ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နှုတ်ခွန်းဆက်သခြင်းကို 'နှုတ်ဆက်' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ရာထူးမှ|အလုပ်မှ|အဖွဲ့မှ|တာဝန်မှ|အသင်းမှ)\s*(?<Target>နှုတ်ထွက်)",
                "Target",
                "နုတ်ထွက်",
                "နုတ်ထွက် (ရာထူး/တာဝန်မှ ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရာထူး/အဖွဲ့မှ ထွက်ခွာခြင်းကို 'နုတ်ထွက်' (ဟထိုးမပါ) ဖြင့် ရေးသားရမည်။ ('နှုတ်ထွက်' သည် ပါးစပ်မှထွက်သော နှုတ်ထွက်စကား ဖြစ်သည်)။"
            ),

            // 25. Causative Ha-htoe (ှ) Verb Pairs from Thatpon Book (ချီးမြှင့်၊ မြှင့်တင်၊ လွှတ်တော်၊ ညွှန်ကြား၊ ရွှေ့ပြောင်း)
            new CollocationRule(
                @"(?<Target>ချီးမြင့်)",
                "Target",
                "ချီးမြှင့်",
                "ချီးမြှင့် (ဆု/ဂုဏ်ထူး ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ချီးမြှောက်ပေးအပ်ခြင်းကို 'ချီးမြှင့်' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မြင့်တင်)",
                "Target",
                "မြှင့်တင်",
                "မြှင့်တင် (အဆင့်အတန်း/စွမ်းရည် ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ မြင့်မားအောင် ပြုလုပ်ခြင်း (ကြိယာ) ကို 'မြှင့်တင်' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လွတ်တော်)",
                "Target",
                "လွှတ်တော်",
                "လွှတ်တော် (ပြည်ထောင်စု~၊ ဥပဒေပြု~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဥပဒေပြုအဖွဲ့အစည်းကို 'လွှတ်တော်' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ညွန်ကြား)",
                "Target",
                "ညွှန်ကြား",
                "ညွှန်ကြား (ညွှန်ကြားချက်၊ ညွှန်ကြားရေးမှူး)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လမ်းညွှန်မှာကြားခြင်းကို 'ညွှန်ကြား' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ရွေ့ပြောင်း)(?=လုပ်သား|အလုပ်သမား|နေထိုင်|ပေး|ချထား)",
                "Target",
                "ရွှေ့ပြောင်း",
                "ရွှေ့ပြောင်း (ရွှေ့ပြောင်းလုပ်သား၊ နေရာ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တစ်နေရာမှတစ်နေရာသို့ ပြောင်းရွှေ့ခြင်းကို 'ရွှေ့ပြောင်း' (ဟထိုးပါ) ဖြင့် ရေးသားရမည်။"
            ),

            // 26. Book Collocations (~ / ...) from 'မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း' (မိုးအုံ့၊ ပန်းကုံး၊ မျက်စိကန်း၊ ခါးကုန်း၊ လုံးဝဥဿုံ)
            new CollocationRule(
                @"မိုး\s*(?<Target>အုံ)(?=သည်|တယ်|နေ|လာ|ပြီး)",
                "Target",
                "အုံ့",
                "အုံ့ (မိုး~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ မိုးသားတက်၍ မှိုင်းအုံ့ခြင်းကို 'မိုးအုံ့' (အောက်မြစ်ပါ) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပန်းကုန်း)",
                "Target",
                "ပန်းကုံး",
                "ကုံး (ပန်း~သည်၊ ပန်းကုံး)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပန်းသီကုံးခြင်းကို 'ပန်းကုံး' (သေးသေးတင်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မျက်စိကမ်း)",
                "Target",
                "မျက်စိကန်း",
                "ကန်း (မျက်စိ~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အမြင်ကွယ်ခြင်းကို 'မျက်စိကန်း' (နသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ခါးကုံး)",
                "Target",
                "ခါးကုန်း",
                "ကုန်း (ခါး~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခါးကိုင်းညွတ်ခြင်းကို 'ခါးကုန်း' (နသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လုံးဝဥသုံ|လုံးဝဥဿုန်)",
                "Target",
                "လုံးဝဥဿုံ",
                "ဥဿုံ (လုံးဝ~၊ အကြွင်းမဲ့)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အကြွင်းမဲ့အားလုံးကို 'လုံးဝဥဿုံ' (သကြီးနှစ်လုံးဆင့်၊ သေးသေးတင်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:ဈေးနှုန်း|ကုန်ဈေးနှုန်း)(?:များ|တွေ)?\s*(?<Target>ပျော့တက်)",
                "Target",
                "မြင့်တက်",
                "မြင့်တက် (ဈေးနှုန်း ~သည်)",
                "ဈေးနှုန်းတက်ခြင်းနှင့် တွဲဖက်ရာတွင် 'ပျော့တက်' အစား 'မြင့်တက်' ဟု သုံးနှုန်းရမည်။"
            ),

            // 27. Everyday Spoken/Typed Homophone & Particle Disambiguations (မန်းမန်း/မန်မန် စား၊ တုံး/တုန်း၊ မာ/မှာ၊ ကျောင်း/ကြောင်း)
            new CollocationRule(
                @"(?<Target>မန်းမန်း|မန်မန်)(?=\s*စား)",
                "Target",
                "ထမင်း",
                "ထမင်း (ဆန်ချက်ပြီးသား အစာ ~စားသည်)",
                "ကလေးစကား/အရပ်သုံး အရေးအသားအစား စံမြန်မာစာဖြင့် 'ထမင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!ဘယ့်နှယ်|သစ်|ဓား|အညွန့်|လုံး|မီး)(?:(?<=ငယ်ငယ်|ကြီးကြီး|အဲဒီ|ဟို|ဒီ|မနေ့|ဘယ်|အရင်|ငယ်|သွား|လာ|စား|နေ|ရှိ|ဖြစ်)(?<Target>တုံး)(?=က|\s|$|[\u104A\u104B])|(?:ဘာ|ဘယ်)[\u1000-\u103F\s]{0,18}?(?<Target>တုံး)(?=\s|$|[\u104A\u104B?]))",
                "Target",
                "တုန်း",
                "တုန်း (သွား~လာ~၊ ငယ်ငယ်~က၊ ဘယ်~က၊ ဘာလုပ်နေတာ~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အချိန်ကာလနှင့် အမေးနောက်ဆက်အနက်အတွက် 'တုန်း' (နသတ်) ဖြင့် ရေးသားရမည်။ ('တုံး' သည် သစ်တုံး/ဓားတုံး အနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:(?<=အိမ်|ကျောင်း|ရုံး|ဆိုင်|လမ်း|မြို့|ရွာ|ဒီ|ဟို|အဲဒီ|ဘယ်|အထဲ|အပြင်|အပေါ်|အောက်|ရှေ့|နောက်)(?<Target>မာ)(?=\s+(?:ရှိ|နေ|ထိုင်|လုပ်|စား|သောက်|သွား|လာ|အိပ်|တွေ့|ထား|ရောက်|စောင့်))|(?<=ဒီ|ဟို|အဲဒီ|ဘယ်)(?<Target>မာ)(?=\s|$|[\u104A\u104B?]|လဲ))",
                "Target",
                "မှာ",
                "မှာ (နေရာပြဝိဘတ် — အိမ်~၊ ဒီ~၊ ဘယ်~)",
                "နေရာပြဝိဘတ်တွင် ဟထိုး ('ှ') ကျန်ခဲ့ပါသည် — 'မာ' အစား 'မှာ' ဟု ရေးသားရမည် ('မာ' သည် မာကျောခြင်း အနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=သွား|လာ|သိရှိ|ရောက်ရှိ|ဖြစ်ပွား|ပြီးစီး|မှန်ကန်|သဘောတူ|လက်ခံ|ဆောင်ရွက်ပြီး)(?<Target>ကျောင်း)(?=\s*(?:ပြော|တင်ပြ|ကြေညာ|ဖော်ပြ|သိရ|အသိပေး|အကြောင်းကြား|ဝန်ခံ|ရေးသား|ကို))",
                "Target",
                "ကြောင်း",
                "ကြောင်း (အကြောင်းအရာပြပစ္စည်း — သွား~ ပြောသည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြိယာနောက်ဆက် အကြောင်းအရာပြပစ္စည်းကို 'ကြောင်း' (ရရစ်) ဖြင့် ရေးသားရမည်။ ('ကျောင်း' သည် စာသင်ကျောင်း/ဘုန်းကြီးကျောင်း ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:အထက်တန်း|အလယ်တန်း|မူလတန်း|စာသင်|ဘုန်းကြီး|တက္ကသိုလ်|သင်တန်း)\s*(?<Target>ကြောင်း)(?![\u102B-\u103E]|အရာ|ပြချက်|ရင်း)",
                "Target",
                "ကျောင်း",
                "ကျောင်း (စာသင်~၊ အထက်တန်း~၊ ဘုန်းကြီး~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပညာသင်ကြားရာနေရာကို 'ကျောင်း' (ယပင့်) ဖြင့် ရေးသားရမည်။"
            ),

            // 28. Nominalizer '-မူ' -> '-မှု' after verb/adjective stems (guards မူဝါဒ, မူကြမ်း, မူလ, မူပိုင်, ပြုမူ, အမူအရာ, ကမူ)
            new CollocationRule(
                @"(?:ထိရောက်|အောင်မြင်|တိုးတက်|ပြောင်းလဲ|ဆောင်ရွက်|ပူးပေါင်း|လုံခြုံ|ကျန်းမာ|လူနေ|စိုးရိမ်|ခက်ခဲ|လိုအပ်|မှားယွင်း|ချို့ယွင်း|အားနည်း|အားသာ|ကွာခြား|တူညီ|သဘောတူ|ကျေနပ်|နားလည်|ယုံကြည်|လေးစား|တာဝန်ယူ|ပွင့်လင်းမြင်သာ|ဖွံ့ဖြိုး|ဆုံးရှုံး|နစ်နာ|ထိခိုက်|လှုပ်ရှား|ကြိုးပမ်း|အားထုတ်|ပါဝင်|ကူညီ|ပံ့ပိုး|စောင့်ရှောက်|ကာကွယ်|ထိန်းသိမ်း|စီမံခန့်ခွဲ|ကြီးကြပ်|စစ်ဆေး|သုံးသပ်|ဆုံးဖြတ်|ပြင်ဆင်|ပြုပြင်|ဖန်တီး|တီထွင်|ပြုလုပ်|ဆက်သွယ်|ရင်းနှီးမြှုပ်နှံ)(?<Target>မူ)(?![\u102B-\u103E]|ဝါဒ|ကြမ်း|လ|ပိုင်|ရင်း|တည်|ကား|သေ)",
                "Target",
                "မှု",
                "မှု (နာမ်ပြပစ္စည်း — ဆောင်ရွက်~၊ ထိရောက်~)",
                "ကြိယာ/နာမဝိသေသန နောက်ဆက် နာမ်ပြပစ္စည်းကို 'မှု' (ဟထိုး၊ တစ်ချောင်းငင်) ဖြင့် ရေးသားရမည် ('မူ' သည် မူဝါဒ/မူလ/ပြုမူ ၌သာ သုံးသည်)။"
            ),

            // 29. Past modifier 'ပြီခဲ့' -> 'ပြီးခဲ့', 'အစာ ကြွေး' -> 'ကျွေး', 'သတင်း ကျား' -> 'ကြား', 'ကာမောင်း' -> 'ကားမောင်း'
            new CollocationRule(
                @"(?<Target>ပြီ)(?=ခဲ့(?:သော|သည့်|တဲ့|သော်|လျှင်|ရင်|သည့်အတိုင်း))",
                "Target",
                "ပြီး",
                "ပြီးခဲ့သော / ပြီးခဲ့သည့် (အတိတ်ကာလပြပုဒ်)",
                "အတိတ်ကာလပြပုဒ်တွင် ဝစ္စပေါက်ပါသော 'ပြီး' ('ပြီးခဲ့သော/ပြီးခဲ့သည့်') ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:အစာ|ထမင်း|မုန့်|နို့|မြက်|ဟင်း)\s*(?<Target>ကြွေး)(?=သည်|တယ်|မည်|မယ်|ပါ|နေ|ခဲ့|ပြီး|ဖို့|ရန်|မွေး|ထား|လိုက်|ပေး|ကြ|ချင်)",
                "Target",
                "ကျွေး",
                "ကျွေး (အစာ~သည်၊ ကျွေးမွေး~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စားစရာပေးခြင်းကို 'ကျွေး' (ယပင့်) ဖြင့် ရေးသားရမည် ('ကြွေး' သည် အကြွေး/ကြွေးမြီ ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ကျွေးမြီ|(?<!အလုပ်)အကျွေး(?!အမွေး))",
                "Target",
                "",
                "ကြွေး (ကြွေးမြီ၊ အကြွေးတင်~)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပေးဆပ်ရန်ရှိသောငွေကို 'ကြွေး' (ရရစ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:သတင်း|အသံ|စကားသံ|သီချင်းသံ)(?:ကို|များ|တွေ)?\s*(?<Target>ကျား)(?=ရ|သည်|တယ်|ဖူး|မိ|လိုက်|ပါ|သိ)",
                "Target",
                "ကြား",
                "ကြား (နားဖြင့် ~သည်၊ သတင်း~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နားဖြင့်ကြားသိခြင်းကို 'ကြား' (ရရစ်) ဖြင့် ရေးသားရမည် ('ကျား' သည် သားရဲတိရစ္ဆာန် ကျား ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F])(?<Target>ကာမောင်း|ကာ\s+မောင်း)(?=သည်|တယ်|မည်|မယ်|နေ|တတ်|သွား|လာ|ပြီး)",
                "Target",
                "ကားမောင်း",
                "ကားမောင်း (မော်တော်ကား မောင်းနှင်သည်)",
                "မော်တော်ကား မောင်းနှင်ခြင်းကို 'ကားမောင်း' (ဝစ္စပေါက်ပါ) ဟု ရေးသားရမည်။"
            ),

            // 30. 'အစဉ်ပြေ' -> 'အဆင်ပြေ', 'နားလဲ' -> 'နားလည်', and temporal 'ခန' -> 'ခဏ' (guarded against NRC codes like ၁/မခန)
            new CollocationRule(
                @"(?<Target>အစဉ်ပြေ)",
                "Target",
                "အဆင်ပြေ",
                "အဆင်ပြေ (ချောမွေ့~သည်၊ အဆင်ပြေပါစေ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အခြေအနေ ချောမွေ့သင့်လျော်ခြင်းကို 'အဆင်ပြေ' (ဆလိမ်၊ ငသတ်) ဖြင့် ရေးသားရမည် ('အစဉ်' သည် အစဉ်အမြဲ/အစဉ်အလာ အနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>နားလဲ)(?=သည်|တယ်|ပါ|လား|သလား|နိုင်|ရ|ကြ|သေး|ပြီ|ပီ|မည်|မယ်)",
                "Target",
                "နားလည်",
                "နားလည် (သဘောပေါက်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သဘောပေါက်သိရှိခြင်းကို 'နားလည်' (ညကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>နားမလဲ)",
                "Target",
                "နားမလည်",
                "နားမလည် (သဘောမပေါက်ခြင်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သဘောမပေါက်ခြင်းကို 'နားမလည်' (ညကြီးသတ်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<![\u1000-\u103F/])(?<Target>ခန)(?![\u102B-\u103F]|[\u1000-\u1021][\u1039\u103A])(?=\s*(?:လေး|ခန|တဖြုတ်|စောင့်|နေ|နား|ထိုင်|လောက်|ကြာ|ပဲ))",
                "Target",
                "ခဏ",
                "ခဏ (အချိန်အနည်းငယ် — ခဏလေး၊ ခဏစောင့်ပါ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အချိန်အနည်းငယ်ကို 'ခဏ' (ဏကြီး) ဖြင့် ရေးသားရမည်။"
            ),

            // 31. Government Legal & Administrative Orthography (ဒါဏ်->ဒဏ်, ပိုဒ်မ->ပုဒ်မ, အပုဒ်->အပိုဒ်, မိတ်တူ->မိတ္တူ, ~နဲ->~နည်း)
            new CollocationRule(
                @"(?<Target>[\u1000-\u103F]*ဒါဏ်[\u1000-\u103F]*)",
                "Target",
                "",
                "ဒဏ် (ပြစ်ဒဏ်၊ ထောင်ဒဏ်၊ ဒဏ်ငွေ၊ ဒဏ်ရာ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဒါဏ်' ဟူသော စာလုံးပေါင်းမရှိပါ — ရေးချမပါဘဲ 'ဒဏ်' ဖြင့်သာ ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပိုဒ်မ(?:ခွဲ)?)",
                "Target",
                "",
                "ပုဒ်မ / ပုဒ်မခွဲ (ဥပဒေ ~)",
                "ဥပဒေပါ အချက်အမှတ်စဉ်ကို 'ပုဒ်မ / ပုဒ်မခွဲ' (တစ်ချောင်းငင်) ဖြင့် ရေးသားရမည် ('အပိုဒ်' သည် စာပိုဒ်အတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အပုဒ်(?:ခွဲ)?)(?=\s*(?:\(|[၀-၉0-9]|ခွဲ|ပါ|အရ))",
                "Target",
                "",
                "အပိုဒ် / အပိုဒ်ခွဲ (အမိန့်ကြော်ငြာစာ/ရုံးစာ ~)",
                "ရုံးစာနှင့် အမိန့်ကြော်ငြာစာပါ စာပိုဒ်အမှတ်စဉ်ကို 'အပိုဒ် / အပိုဒ်ခွဲ' (လုံးကြီးတင် တစ်ချောင်းငင်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စာပုဒ်)",
                "Target",
                "စာပိုဒ်",
                "စာပိုဒ် (စာတစ်ပိုဒ်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'စာပိုဒ်' (လုံးကြီးတင် တစ်ချောင်းငင်) ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မိတ်တူ)(?!နံတူ)(?=\s*(?:ပေးပို့|ဖြန့်ဝေ|ကူး|ဆွဲ|စာရွက်|မှန်|အမှန်|ကို|ဖြင့်|အဖြစ်|:|၊|-|$))",
                "Target",
                "မိတ္တူ",
                "မိတ္တူ (ရုံးစာမိတ္တူ၊ မိတ္တူဖြန့်ဝေခြင်း)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာရွက်စာတမ်းပွားကို 'မိတ္တူ' (တဝမ်းပူနှစ်လုံးဆင့်) ဖြင့် ရေးသားရမည် ('မိတ်တူ' ကို 'မိတ်တူနံတူ' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>လုပ်ထုံးလုပ်နဲ|နဲဥပဒေ|နဲပညာ|နဲလမ်း|နဲစနစ်|နဲဗျူဟာ|နဲနဲ|နဲပါး|အနဲငယ်|အနဲဆုံး|အနဲနှင့်အများ)",
                "Target",
                "",
                "နည်း (လုပ်ထုံးလုပ်နည်း၊ နည်းဥပဒေ၊ နည်းပညာ၊ အနည်းငယ်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ နည်းလမ်းနှင့် ပမာဏနည်းပါးခြင်းအနက်တို့ကို 'နည်း' (ညကြီးသတ် ဝစ္စပေါက်) ဖြင့် ရေးသားရမည်။"
            ),
            // Poetry rhyme 'ကာရန်' vs fence/enclose 'ကာရံ-' (သတ်ပုံကျမ်း ခွဲထား)
            new CollocationRule(
                @"(?<Target>ကာရံ(?=နဘေ|စပ်|ညီ|ယူ|သင့်|ချိတ်))|(?<=ကဗျာ|လင်္ကာ|သီချင်း)\s*(?<Target>ကာရံ)",
                "Target",
                "ကာရန်",
                "ကာရန် (ကဗျာ~၊ ~နဘေ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကဗျာလင်္ကာ အသံတူစပ်ဆိုပုံကို 'ကာရန်' (နသတ်) ဖြင့် ရေးသားရမည် ('ကာရံ-' သည် ခြံစည်းရိုးကာရံခြင်း ဖြစ်သည်)။"
            ),
            // Food/air freshness 'လတ်ဆတ်' vs marriage 'ထိမ်းမြားလက်ဆက် / လက်ဆက်ပေး-'
            new CollocationRule(
                @"(?<=(?:သန့်ရှင်း|အစားအသောက်|အသီးအနှံ|ဟင်းသီးဟင်းရွက်|အသား|ငါး|ပင်လယ်စာ|နွားနို့|လေထု)\s*)(?<Target>လက်ဆက်)|(?<Target>လက်ဆက်)(?=(?:သော|သည့်|တဲ့)\s*(?:အစားအသောက်|အသီးအနှံ|ဟင်းသီးဟင်းရွက်|အသား|ငါး|လေ))",
                "Target",
                "လတ်ဆတ်",
                "လတ်ဆတ် (သန့်ရှင်း~၊ အသီးအနှံ~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသစ်ဖြစ်၍ မသိုးမနွမ်းခြင်းကို 'လတ်ဆတ်' (တဝမ်းပူသတ်နှစ်ချက်) ဖြင့် ရေးသားရမည် ('လက်ဆက်-' သည် ထိမ်းမြားလက်ဆက်ပေးခြင်း ဖြစ်သည်)။"
            ),
            // Classical Myanmar Proverbs (မြန်မာစကားပုံ) & Sayings (မြန်မာဆိုရိုးစကား) high-precision misquote corrections
            new CollocationRule(
                @"(?<Target>တစ်လုပ်စားဖူး)(?=\s*သူ့ကျေးဇူး)",
                "Target",
                "တစ်လုတ်စားဖူး",
                "တစ်လုတ်စားဖူး သူ့ကျေးဇူး (မြန်မာစကားပုံ)",
                "မြန်မာစကားပုံနှင့် သတ်ပုံကျမ်းအရ ထမင်း/အစာ တစ်လုတ်စာ စားဖူးခြင်းကို တဝမ်းပူသတ်ဖြင့် 'တစ်လုတ်စားဖူး သူ့ကျေးဇူး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကိုင်းကျွန်းမီ|ကျွန်းကိုင်းမီ)",
                "Target",
                "",
                "ကိုင်းကျွန်းမှီ ကျွန်းကိုင်းမှီ (မြန်မာစကားပုံ)",
                "မြန်မာစကားပုံအရ အပြန်အလှန် အားကိုးမှီခိုခြင်းဖြစ်သဖြင့် ဟထိုးပါဝင်လျက် 'ကိုင်းကျွန်းမှီ ကျွန်းကိုင်းမှီ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အလျောက်ကောင်း)(?=\s*အထောင်းလွတ်)",
                "Target",
                "အလျှောက်ကောင်း",
                "အလျှောက်ကောင်း အထောင်းလွတ် (မြန်မာစကားပုံ)",
                "မြန်မာစကားပုံအရ တရားရုံး/မင်းရှေ့ လျှောက်ထားချေပခြင်း ('အတင်အလျှောက်') ဖြစ်သဖြင့် ဟထိုးပါဝင်လျက် 'အလျှောက်ကောင်း အထောင်းလွတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ပညာရှိ\s*)(?<Target>သတိဖြစ်ခဲ့)",
                "Target",
                "သတိဖြစ်ခဲ",
                "ပညာရှိ သတိဖြစ်ခဲ (မြန်မာဆိုရိုးစကား)",
                "မြန်မာဆိုရိုးစကားအရ သတိလက်လွတ်ဖြစ်ရန် ခဲယဉ်းခြင်း ('ဖြစ်ခဲ') ကို အောက်မြစ်မပါဘဲ 'ပညာရှိ သတိဖြစ်ခဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မိချောင်းမင်း)(?=\s*ရေခင်းပြ)",
                "Target",
                "မိကျောင်းမင်း",
                "မိကျောင်းမင်း ရေခင်းပြ (မြန်မာစကားပုံ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် မြန်မာစကားပုံအရ ကကြီးယပင့်ဖြင့် 'မိကျောင်းမင်း ရေခင်းပြ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဆင်သွား(?:လျှင်|ရင်)\s*)(?<Target>လန်းဖြစ်)",
                "Target",
                "လမ်းဖြစ်",
                "ဆင်သွားလျှင် လမ်းဖြစ် (မြန်မာစကားပုံ)",
                "မြန်မာစကားပုံအရ သွားလာရာလမ်းကြောင်း ဖြစ်ပေါ်လာခြင်းကို မသတ်ဖြင့် 'ဆင်သွားလျှင် လမ်းဖြစ်' ဟု ရေးသားရမည်။"
            ),
            // Systemic Sibilant Aspiration Disambiguation: 'စို' (wet) vs 'ဆို' (say/sing/mean/concessive idiom)
            new CollocationRule(
                @"(?<=(?:မည်သို့ပင်|မည်သို့|ဘယ်လိုပဲ|ဘာပဲ|မည်သို့ပင်ဖြစ်စေ)\s*)(?<Target>စို)(?=စေ(?:ကာမူ)?|သော်လည်း|ဦးတော့)|(?<=(?:ဘာပဲ|ဘယ်လိုပဲ)\s*)(?<Target>စိုစို)|(?<Target>စို)(?=လို(?:ရင်း|သည်မှာ|ချက်|တာက))|(?<=ကြို)(?<Target>စို)(?=ပါ|သည်|တယ်|ကြ)|(?<=သီချင်း\s*)(?<Target>စို)(?=သည်|တယ်|ကြ|မည်|မယ်|ပါ|နေ)",
                "Target",
                "",
                "ဆို (မည်သို့ပင်~စေကာမူ၊ ~လိုရင်း၊ ကြို~၊ သီချင်း~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပြောဆိုခြင်းနှင့် 'မည်သို့ပင်ဆိုစေကာမူ' စကားစပ်တို့တွင် ဆလိမ်ဖြင့် 'ဆို' ဟု ရေးသားရမည် ('စို' သည် ရေစိုစွတ်ခြင်းအတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<=(?:မိုးရေ|ရေ|ချွေး|မျက်ရည်)\s*)(?<Target>ဆို)(?=စွတ်|ရွှဲ)|(?<Target>ဆို)(?=(?:စွတ်|ရွှဲ)(?:နေ|သွား|သည်|တယ်))",
                "Target",
                "စို",
                "စို (ရေ~စွတ်၊ မိုးရေ~ရွှဲ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရေစိုစွတ်ခြင်းကို စလုံးဖြင့် 'စို' ဟု ရေးသားရမည်။"
            ),
            // Systemic Sibilant Aspiration Disambiguation: 'စိုက်' (plant/cultivate/erect/focus) vs 'ဆိုက်' (arrive/dock)
            new CollocationRule(
                @"(?<=(?:အပင်|အပန်|သစ်ပင်|ပန်းပင်|ပျိုးပင်|သီးနှံ|စပါး|သစ်တော|ဥယျာဉ်|တိုင်|ကျောက်တိုင်|မှတ်တိုင်|အလံ|မျက်မှောင်|အာရုံ|စူးစူး)(?:တော်)?(?:များ|တွေ|ကို)?\s*)(?<Target>ဆိုက်)(?=(?:ပျိုး|ထူ|ထုတ်|ကြည့်|ကြ|သည်|တယ်|မည်|မယ်|ပါ|ရန်|ဖို့|စို့|နေ|ထား))|(?<Target>ဆိုက်)(?=ပျိုး|ထူ)",
                "Target",
                "စိုက်",
                "စိုက် (အပင်~၊ ~ပျိုးရေး၊ အလံ~ထူ၊ အာရုံ~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အပင်စိုက်ပျိုးခြင်း၊ စိုက်ထူခြင်းနှင့် အာရုံစိုက်ခြင်းတို့ကို စလုံးဖြင့် 'စိုက်' ဟု ရေးသားရမည် ('ဆိုက်-' သည် သင်္ဘော/ရထား ဆိုက်ရောက်ခြင်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=(?:သင်္ဘော|လှေ|ရထား|လေယာဉ်|ဆိပ်ကမ်း|ဘူတာ|လေဆိပ်)(?:သို့|မှာ|တွင်)?\s*)(?<Target>စိုက်)(?=ရောက်|ကပ်)|(?<Target>စိုက်)(?=(?:ရောက်|ကပ်)(?:လာ|သွား|ပြီ|သည်|တယ်|မည်|မယ်|ပါ))",
                "Target",
                "ဆိုက်",
                "ဆိုက် (သင်္ဘော~ကပ်၊ ဘူတာသို့~ရောက်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခရီးရောက်ရှိ/ကမ်းကပ်ခြင်းကို ဆလိမ်ဖြင့် 'ဆိုက်' (ဆိုက်ရောက်/ဆိုက်ကပ်) ဟု ရေးသားရမည်။"
            ),
            // Pali '-လိင်' (gender/sex: liṅga) vs 'လိမ်' (lie/twist)
            new CollocationRule(
                @"(?<Target>လိမ်)(?=ပိုင်းဆိုင်ရာ|ခွဲခြား|အင်္ဂါ|တူချစ်သူ|တူဆက်ဆံ|စိတ်)",
                "Target",
                "လိင်",
                "လိင် (ဒွိ~၊ ~ပိုင်းဆိုင်ရာ၊ ~ခွဲခြားမှု)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကျား/မ လိင်သဘာဝ (ပါဠိသက် 'လိင်္ဂ') ကို ငသတ်ဖြင့် 'လိင်' ဟု ရေးသားရမည် ('လိမ်-' သည် လိမ်ညာ/လိမ်ဖည်ခြင်းဖြစ်သည်)။"
            ),

            // 29. စိုး (rule/govern/worry: စိုးရိမ်၊ ကြီးစိုး၊ အစိုးရ) vs ဆိုး (bad/naughty/severe/dye/cough: ဆိုးချင်ဆိုး-၊ ဆိုးပေ၊ ဆိုးသွမ်း၊ ဆိုးရွား)
            new CollocationRule(
                @"(?<!ကြီး|အုပ်|စိုးရိမ်)(?<Target>စိုး(?:ချင်|လည်း|တော့|သာ)စိုး)(?!(?:ရိမ်|ရွံ့|ထိတ်|မိုး|စံ|အုပ်)|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဆိုး (မကောင်းသော / ~သွမ်းသော ~ချင်~မည်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် မြန်မာသဒ္ဒါအရ 'မကောင်းခြင်း/ဆိုးသွမ်းခြင်း' ကြိယာနှစ်ထပ်သဏ္ဌာန်တွင် ဆလိမ်ဖြင့် 'ဆိုးချင်ဆိုး-' ဟု ရေးသားရမည် ('စိုး-' သည် စိုးရိမ်-/စိုးမိုး-/ကြီးစိုး- တို့၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<!ကြီး|အုပ်|မင်း|အ|တ|ဘဏ္ဍာ|ရုက္ခ|ဘုမ္မ|အာကာသ|ကန|ဦး|ကို|မောင်|ဒေါ်)(?<Target>(?:လူ)?စိုးပေ)(?!မယ့်|မဲ့|တံ|စာ|ပင်|သီး|ထုပ်|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဆိုးပေ (ဆိုးသွမ်းပေတေ~၊ လူဆိုးလူပေ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆိုးသွမ်းပေတေခြင်း အနက်အတွက် ဆလိမ်ဖြင့် 'ဆိုးပေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စိုး(?:သွမ်း|ရွား|ဝါး|ယုတ်|ကျိုး|ပြစ်)|လူစိုးလူ(?:ပေ|သွမ်း|မိုက်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဆိုး (~သွမ်း၊ ~ရွား၊ ~ကျိုး၊ လူ~လူပေ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ မကောင်းခြင်း/ဆိုးရွားခြင်း အနက်ရှိ ပေါင်းစပ်ပုဒ်များတွင် ဆလိမ်ဖြင့် 'ဆိုး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:ချောင်း|စိတ်|ကံ|အကျင့်|ဉာဉ်|အနံ့|သေခြင်း|မကောင်း)စိုး)(?!(?:ရိမ်|ရွံ့|ထိတ်|မိုး|စံ|အုပ်)|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဆိုး (ချောင်း~၊ စိတ်~၊ ကံ~၊ အကျင့်~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဤတွဲဖက်ပုဒ်တွင် ဆလိမ်ဖြင့် 'ဆိုး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဆိုး(?:ရိမ်|ထိတ်|ရွံ့)|ကြီးဆိုး|အုပ်ဆိုး|တရားဥပဒေဆိုးမိုး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "စိုး (စိုးရိမ်~၊ ကြီးစိုး~၊ စိုးမိုး~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပူပန်စိုးရိမ်ခြင်းနှင့် အုပ်စိုးလွှမ်းမိုးခြင်း အနက်တို့တွင် စလုံးဖြင့် 'စိုး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!မ|အ)(?<Target>အဆိုးရ)(?=အဖွဲ့|ဌာန|ရုံး|ဝန်ကြီး|မင်း|ဝန်ထမ်း|သစ်|\s|$|[\u104A\u104B])",
                "Target",
                "အစိုးရ",
                "အစိုးရ (နိုင်ငံတော်~အဖွဲ့)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အုပ်ချုပ်သူအဖွဲ့အစည်းကို စလုံးဖြင့် 'အစိုးရ' ဟု ရေးသားရမည်။"
            ),

            // 30. ဘူး (gourd ဘူးသီး/ဘူးညွန့် & container ထမင်းဘူး/နို့ဆီဘူး) vs ဖူး/ဗူး (bud/worship)
            new CollocationRule(
                @"(?<!အ|ပွင့်)(?<Target>(?:ဖူး|ဗူး)(?:သီး|ညွန့်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဘူး (ဘူးသီး၊ ဘူးညွန့်၊ ဘူးပင်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား) အရ ဘူးပင်/ဘူးသီး/ဘူးညွန့် တို့ကို ဘကုန်းဖြင့် 'ဘူး' ဟု ရေးသားရမည် ('ဖူး' သည် ဖူးငုံခြင်းနှင့် ဘုရားဖူးခြင်းအတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>(?:ထမင်း|နို့ဆီ|နို့စိမ်း|ကော်)(?:ဖူး|ဗူး))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဘူး (ထည့်စရာ ~၊ ထမင်းဘူး၊ နို့ဆီဘူး)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား) အရ ပစ္စည်းထည့်စရာ ဘူးအမျိုးမျိုးကို ဘကုန်းဖြင့် 'ဘူး' ဟု ရေးသားရမည်။"
            ),

            // 31. ဆွတ်ပျံ့ (ကြည်နူးဆွတ်ပျံ့ဖွယ်) vs ဆုတ် (စာရွက်ဆုတ်-၊ နောက်ဆုတ်-)
            new CollocationRule(
                @"(?<Target>ဆုတ်ပျံ့)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဆွတ်ပျံ့",
                "ဆွတ်ပျံ့ (ကြည်နူး~ဖွယ်၊ လွမ်း~ဖွယ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား) အရ ကြည်နူးလွမ်းမောဖွယ် အနက်တွင် ဝဆွဲ တသတ်ဖြင့် 'ဆွတ်ပျံ့' ဟု ရေးသားရမည် ('ဆုတ်' သည် စာရွက်ဆုတ်ခြင်းနှင့် နောက်ဆုတ်ခြင်းအတွက်သာ သုံးသည်)။"
            ),

            // 32. Pre-1986 & Published-Book Orthography Standardization: '~ဘွယ် -> ~ဖွယ်', experiential '[Verb]+ဘူး(ခဲ့|သည်|၏|သော|သည့်) -> ~ဖူး', countable 'တရာ -> တစ်ရာ'
            new CollocationRule(
                @"(?<=(?:လှူ|အံ့|အံ့ဩ|ချစ်|ချစ်မိ|သနား|ငြီး|ငြီးငွေ့|ကြောက်|ကြောက်မက်|စက်ဆုပ်|မှတ်သား|သိမှတ်|စိတ်ဝင်စား|ကြည်နူး|နာပျော်|နှစ်သက်|ဝမ်းမြောက်|ဝမ်းနည်း|ကြည်ညို|ချင်|ရှုချင်|သွားလာချင်|အောက်မေ့|လွမ်း|လွမ်းမော|စား|သောက်|ခဲ|နှစ်လို|ချီးမွမ်း|ရယ်))(?<Target>ဘွယ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဖွယ်",
                "ဖွယ် (လှူ~၊ အံ့~၊ ချစ်~၊ သနား~၊ မှတ်သား~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ နာမ်နှင့် နာမဝိသေသနပြုပစ္စည်း '~ဖွယ်' (လှူဖွယ်၊ အံ့ဖွယ်၊ ချစ်ဖွယ်၊ သနားဖွယ်) ကို ဖဦးထုပ်ဖြင့် 'ဖွယ်' ဟု ရေးသားရမည် ('ဘွယ်' ကို 'ပျော်ဘွယ်မြို့' အမည်၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<!မ(?![\u102B-\u103F])\s*)(?:မြင်|ကြား|ကြားနာ|တွေ့|ရောက်|ဖြစ်|သေ|ပြုခဲ့|လျှောက်|ဖတ်|စား|သောက်|နေ|လုပ်|သွား|လာ)(?<Target>ဘူး)(?=ခဲ့|သည်|၏|သော|သည့်)",
                "Target",
                "ဖူး",
                "ဖူး (ရှေးက ဖြစ်~၊ မြင်~ခဲ့၏၊ ကြား~သော)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် မြန်မာသဒ္ဒါအရ အတိတ်က ကြုံတွေ့ဖူးကြောင်းပြ ကြိယာထောက်ပစ္စည်းကို ဖဦးထုပ်ဖြင့် 'ဖူး' ဟု ရေးသားရမည် ('ဘူး' သည် 'မ...ဘူး' အငြင်းဝါကျပိတ်ပစ္စည်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=(?:နှစ်ပေါင်း|အနှစ်|သက်တော်|ဂါထာ|အပုဒ်|ယူဇနာ|အခန်း|စာမျက်နှာ|လူ|ဦးရေ|ကျပ်|ပေါင်း|အသက်|တန်ဖိုး|အရေအတွက်)\s*)(?<Target>တရာ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "တစ်ရာ",
                "တစ်ရာ (အရေအတွက် ၁၀၀ — နှစ်ပေါင်း~၊ သက်တော်~၊ ဂါထာ~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အရေအတွက် (၁၀၀) ကို ရည်ညွှန်းရာတွင် စသတ်ဖြင့် 'တစ်ရာ' ဟု ရေးသားရမည်။"
            ),

            // 33. Legal Court Decree 'ဒီကရီ' (ကကြီး — အနိုင်ဒီကရီ၊ တရားမဒီကရီ၊ ဒီကရီချမှတ်-) vs Academic/Temperature Degree 'ဒီဂရီ' (ဂငယ် — ဘွဲ့ဒီဂရီ၊ ဒီဂရီစင်တီဂရိတ်)
            new CollocationRule(
                @"(?<Target>(?:အနိုင်|တရားမ|အပြီးသတ်|ပဏာမ|ကြားဖြတ်|တစ်ဖက်သတ်|သဘောတူ)ဒီဂရီ)|(?<Target>ဒီဂရီ)(?=\s*(?:ချမှတ်|အတည်ပြု|ဇာရီ|တရားရှုံး|တရားနိုင်))",
                "Target",
                "",
                "ဒီကရီ (တရားရုံး အနိုင်~၊ တရားမ~ချမှတ်-)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် ပြည်ထောင်စုတရားလွှတ်တော်ချုပ် စံနှုန်းအရ တရားရုံး 'ဒီကရီ' (Court Decree) ကို ကကြီးဖြင့် 'ဒီကရီ' ဟု ရေးသားရမည် ('ဒီဂရီ' သည် ဘွဲ့ဒီဂရီနှင့် အပူချိန်/ထောင့်ဒီဂရီအတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>(?:ဘွဲ့|မဟာဝိဇ္ဇာ|မဟာသိပ္ပံ|ပါရဂူ|ဂုဏ်ထူးဆောင်)ဒီကရီ)|(?<Target>ဒီကရီ)(?=\s*(?:စင်တီဂရိတ်|ဆဲလ်စီးယပ်စ်|ဖာရင်ဟိုက်))",
                "Target",
                "",
                "ဒီဂရီ (ဘွဲ့~၊ အပူချိန်/ထောင့် ~)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပညာရေးဘွဲ့နှင့် အပူချိန်/ထောင့်တိုင်းတာမှု Degree ကို ဂငယ်ဖြင့် 'ဒီဂရီ' ဟု ရေးသားရမည်။"
            ),

            // 34. Colloquial Chat / Phonetic Slang Normalization ('မ...ပု -> မ...ဘူး', 'ဘယ်လိုကျီးလဲ -> ဘယ်လိုကြီးလဲ', 'ပီးပီ -> ပြီးပြီ', 'ဖစ် -> ဖြစ်')
            new CollocationRule(
                @"(?<=မ(?![\u102B-\u103F])(?:[\u1000-\u1021][\u102B-\u103F]*){0,4}?(?:ဟုတ်|သိ|ရှိ|ရ|လုပ်|သွား|လာ|စား|သောက်|ပြော|ဆို|ကြည့်|မြင်|ကြား|တွေ့|ဖြစ်|ဖစ်|ကောင်း|လို|ချင်|တတ်|နိုင်|ဝံ့|ရဲ|မှန်|မှား|ထင်|ထား|ပေး|ယူ|ဝယ်|ရောင်း|ဖတ်|ရေး|အိပ်|ထိုင်|နေ|မေ့|လွယ်|ခက်|တူ|ဆိုင်|ပါ|ဝင်|ထွက်|ရောက်|ပြန်|သေး|တော့|ခဲ့|ဖူး|တန်|သင့်|ပြေ|ပျော်|ကြိုက်|အား|မီ|လောက်|ကုန်|ပျောက်|ပျက်))(?<Target>ပု)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)(?=(?:လေ|နော်|ကွာ|ကွယ်|ဗျာ|ဗျ|ရှင့်|ရှင်|ဟ|ဟယ်|ပေါ့|လား|ဘဲ|ပဲ|ဦး)?(?=$|\s|[\u104A\u104B!?.,]))",
                "Target",
                "ဘူး",
                "ဘူး (မဟုတ်~၊ မသိ~ — အငြင်းဝါကျပိတ်ပစ္စည်း)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် မြန်မာသဒ္ဒါအရ 'မ...' အငြင်းဝါကျပိတ်ပစ္စည်းကို အသံထွက်ဗန်းစကား 'ပု' ဖြင့် မရေးဘဲ ဘကုန်းဖြင့် 'ဘူး' (ဥပမာ - မဟုတ်ဘူးလေ) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ဘယ်လို|အဲဒီလို|အဲလို|အဲ့လို|ဒီလို|ဟိုလို|ဘာ|ဘယ်သူ|ဘယ်ဟာ|အကြီး|အကျယ်)\s*)(?<Target>ကျီး)(?!(?:ကန်း|ငှက်|ပေါင်း|အာ|မည်း|နက်)|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ကြီး",
                "ကြီး (ဘယ်လို~လဲ၊ ဒီလို~ — အထူးပြုပစ္စည်း)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အထူးပြု/အားဖြည့်ပစ္စည်းကို ကကြီးရရစ်ဖြင့် 'ကြီး' (ဥပမာ - ဘယ်လိုကြီးလဲ) ဟု ရေးသားရမည် ('ကျီး' သည် ကျီးကန်းငှက်နှင့် စပါးကျီးအတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ပီးပီ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ပြီးပြီ",
                "ပြီးပြီ (စား~၊ လုပ်~ — ပြီးစီးကြောင်းပြပစ္စည်း)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြိယာပြီးစီးကြောင်းပြပစ္စည်းကို ရရစ်ပါဝင်လျက် 'ပြီးပြီ' (ဥပမာ - စားပြီးပြီ) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!ရူ)(?<=[\u1000-\u1021][\u102B-\u103F]*)(?<Target>ပီး)(?=(?:ပြီ|ပီ|သွား|လာ|မှ|သား|ရင်|လျှင်|သော်|နောက်|တာ|ကြောင်း|ဖြစ်|ပါ|ပြန်|ချိန်|ခါ))",
                "Target",
                "ပြီး",
                "ပြီး (ဆောင်ရွက်~၊ စား~သွားပြီ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြိယာနောက်ဆက် 'ပြီး' ကို ပစောက်ရရစ်ဖြင့် 'ပြီး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ပြီး|ပီး|သွား|လာ|ရောက်|သိ|တွေ့|ရ|ဖြစ်|ဖစ်|နေ|ပြေ|စား|သောက်|လုပ်|ပြော|ရေး|ဖတ်|အိပ်|နိုး)(?:သွား|လာ|ခဲ့|နေ)?)(?<Target>ပီ)(?!(?:ပီ|ပြင်|တိ|ယ|ကေ|နံ|ဖော်)|[\u102B-\u103E]|[\u1000-\u1021]\u103A)(?=(?:နော်|လား|လေ|ပေါ့|ဗျာ|ဗျ|ရှင့်)?(?=$|\s|[\u104A\u104B!?.,]))",
                "Target",
                "ပြီ",
                "ပြီ (ပြီး~၊ ရောက်~ — ဝါကျပိတ်ပစ္စည်း)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဖြစ်စဉ်ရောက်ရှိ/ပြီးစီးကြောင်းပြ ဝါကျပိတ်ပစ္စည်းကို ပစောက်ရရစ်ဖြင့် 'ပြီ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!အော့)(?:(?<=(?:မ|ဘာ|လို|သလို))(?<Target>ဖစ်)|(?<Target>ဖစ်)(?=(?:နေ|သွား|လာ|တယ်|သည်|ပီ|ပြီ|လို့|တာ|မှာ|မယ်|မည်|ရင်|လျှင်|နိုင်|ပါ|ဘူး|ပု)))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဖြစ်",
                "ဖြစ် (ဖြစ်ပေါ်/ဖြစ်ပွား~ — ရရစ်ပါ စံသတ်ပုံ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဖြစ်' ကြိယာကို ဖဦးထုပ်ရရစ်ဖြင့် 'ဖြစ်' ဟု ရေးသားရမည်။"
            ),

            // 35. High-Frequency Official & Literary Compound Homophones (တည်ဆောက် vs တီထွင်, စဉ်ဆက်မပြတ်, ဆုံးဖြတ်, လက်ခံ, ထိရောက်)
            new CollocationRule(
                @"(?<Target>တီဆောက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "တည်ဆောက်",
                "တည်ဆောက် (အဆောက်အအုံ/နိုင်ငံတော် ~သည်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ တည်ထောင်ဆောက်လုပ်ခြင်းကို ညကြီးသတ်ဖြင့် 'တည်ဆောက်' ဟု ရေးသားရမည် ('တီ-' ကို 'တီထွင်-' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>တည်ထွင်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "တီထွင်",
                "တီထွင် (အသစ်~ဖန်တီးသည်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသစ်ကြံဆဖန်တီးခြင်းကို လုံးကြီးတင်ဆန်ခတ်ဖြင့် 'တီထွင်' ဟု ရေးသားရမည် ('တည်-' ကို 'တည်ဆောက်/တည်ထောင်-' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>စင်ဆက်မပြတ်|စဉ်ဆက်မပျက်|စင်ဆက်မပျက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စဉ်ဆက်မပြတ်",
                "စဉ်ဆက်မပြတ် (အစဉ်အဆက် မပြတ်တောက်ဘဲ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အစဉ်အဆက် မရပ်မနား ဆက်တိုက်ဖြစ်ခြင်းကို 'စဉ်ဆက်မပြတ်' (ဉကလေးသတ် နှင့် ရရစ်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဆုံးဖျတ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဆုံးဖြတ်",
                "ဆုံးဖြတ် (အဆုံးအဖြတ်ပေး~သည်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဆုံးအဖြတ်ပေးခြင်းကို ရရစ်ဖြင့် 'ဆုံးဖြတ်' ဟု ရေးသားရမည် ('ဖျတ်' သည် 'ဖျတ်ခနဲ/ဖျတ်လတ်-' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>လတ်ခံ)(?=တွေ့ဆုံ|သည်|တယ်|ပါ|ရရှိ|အတည်ပြု|ကျင်းပ|ဆောင်ရွက်|ကြိုဆို|စကားပြော)",
                "Target",
                "လက်ခံ",
                "လက်ခံ (သဘောတူ~သည်၊ ~တွေ့ဆုံသည်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ သဘောတူသိမ်းပိုက်/တွေ့ဆုံခြင်းကို ကကြီးသတ်ဖြင့် 'လက်ခံ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ထိ့ရောက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ထိရောက်",
                "ထိရောက် (အကျိုးသက်ရောက်မှု ရှိ~သော)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အကျိုးသက်ရောက်ခြင်းကို အောက်မြစ်မပါဘဲ 'ထိရောက်' ဟု ရေးသားရမည်။"
            ),

            // 36. V18 Real-Word Wrong-Meaning Homophone Pairs (လျော့/လျှော့, ပြည်/ပြည့်, ဝက်/ဝတ်, အကြိုး/အကျိုး, ကျိုး/ကြိုး, မြက်/မျက်, ဖျေ/ဖြေ, သတ်/သက်, ရက်/ရပ်, စမ်း/ဆန်း, တွေ့/တွေး, ခြ/ချ, ခမ်း/ခန်း, သက်တန်း/သက်တမ်း)
            new CollocationRule(
                @"(?<Target>လျော့(?:ချ|ပေါ့)|ဈေးလျော့ပေး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "လျှော့ချ / လျှော့ပေါ့ (လျော့သွားအောင် ပြုလုပ်သည် — ဟထိုးပါ စံသတ်ပုံ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ကာရန်နှင့် အနက်ခွဲထားစနစ်အရ သူတစ်ပါး/အရာဝတ္ထုကို လျော့နည်းသွားအောင် ပြုလုပ်ခြင်း (ကာရိုက်ကြိယာ) ကို ဟထိုးဖြင့် 'လျှော့ချ / လျှော့ပေါ့' ဟု ရေးသားရမည် ('လျော့' သည် အလိုအလျောက် လျော့နည်း/လျော့ကျခြင်း၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>လျှော့(?:နည်း|ပါး|ကျ|ရဲ))(?!စေ|အောင်|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "လျော့နည်း / လျော့ပါး / လျော့ကျ (အလိုအလျောက် နည်းပါးသွားသည် — ဟထိုးမပါ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အလိုအလျောက် နည်းပါးကျဆင်းသွားခြင်းကို ဟထိုးမပါဘဲ 'လျော့နည်း / လျော့ပါး / လျော့ကျ' ဟု ရေးသားရမည် ('လျှော့' သည် လျော့အောင်ပြုလုပ်သော 'လျှော့ချ/လျှော့ပေါ့' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ပြည်(?:စုံ|မီ|နှက်|လျှံ))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ပြည့်စုံ / ပြည့်မီ / ပြည့်နှက် (အပြည့်အဝရှိသည် — အောက်မြစ်ပါ စံသတ်ပုံ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အပြည့်အစုံရှိခြင်းကို အောက်မြစ်ဖြင့် 'ပြည့်စုံ / ပြည့်မီ / ပြည့်နှက် / ပြည့်လျှံ' ဟု ရေးသားရမည် ('ပြည်' သည် တိုင်းပြည်/ပြည်နယ်အနက်၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?:(?:အရည်အချင်း|ဗဟုသုတ|ပညာ|အတွေ့အကြုံ|ကျန်းမာရေး|အင်အား|စွမ်းရည်)[\u1000-\u103F\s]{0,12}?(?<Target>ပြည်ဝ)|(?<Target>ပြည်ဝ)(?=စွာ|သော|သည့်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ပြည့်ဝ",
                "ပြည့်ဝ (အရည်အချင်း/ဗဟုသုတ အပြည့်အဝရှိသည်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အပြည့်အဝရှိခြင်းကို အောက်မြစ်ဖြင့် 'ပြည့်ဝ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဝက်(?:ဆင်|စုံ))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဝတ်ဆင် / ဝတ်စုံ (အဝတ်အစား ဆင်ယင်သည် — တဝမ်းပူသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဝတ်အစား ဆင်ယင်ခြင်းကို တဝမ်းပူသတ်ဖြင့် 'ဝတ်ဆင် / ဝတ်စုံ' ဟု ရေးသားရမည် ('ဝက်' သည် သတ္တဝါ/ဝက်အူအနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<!အ|လူ့|မင်း|စာရိတ္တ)(?<Target>ဝတ်သား)(?!ကျေ|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဝက်သား",
                "ဝက်သား (ဝက်၏ အသားဟင်းလျာ — ကကြီးသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝက်သတ္တဝါ၏ အသားကို ကကြီးသတ်ဖြင့် 'ဝက်သား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အကြိုး(?:ကျေးဇူး|ရလဒ်|သက်ရောက်|ရှိ|ပြု|ဆောင်|အမြတ်|ခံစား|မဲ့|နည်း|များ)|အကြောင်းအကြိုး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "အကျိုးကျေးဇူး / အကြောင်းအကျိုး (ကောင်းကျိုးရလဒ် — ယပင့် 'ကျိုး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အကြောင်းကြောင့် ဖြစ်ပေါ်သော ရလဒ်နှင့် ကျေးဇူးတရားကို ယပင့်ဖြင့် 'အကျိုး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကျိုး(?:စား|ပမ်း|ကုတ်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ကြိုးစား / ကြိုးပမ်း / ကြိုးကုတ် (လုံ့လဝီရိယစိုက်ထုတ်သည် — ရရစ် 'ကြိုး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ လုံ့လစိုက်ထုတ် အားထုတ်ခြင်းကို ရရစ်ဖြင့် 'ကြိုးစား / ကြိုးပမ်း / ကြိုးကုတ်' ဟု ရေးသားရမည် ('ကျိုး' သည် ကျိုးပဲ့/အကျိုး/ကျိုးနွံ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ကြိုးနွံ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ကျိုးနွံ",
                "ကျိုးနွံ (ရိုသေနာခံတတ်သော — ယပင့် 'ကျိုး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရိုသေနာခံခြင်းကို ယပင့်ဖြင့် 'ကျိုးနွံ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>မြက်(?:နှာ|စိ|မှန်|ရည်|မှောက်|မြင်|ခုံး|တောင်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "မျက်နှာ / မျက်စိ / မျက်မှောက် (ယပင့် 'မျက်' — စံသတ်ပုံ)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ မျက်စိ/မျက်နှာနှင့်ဆိုင်သော ဝေါဟာရများကို ယပင့်ဖြင့် 'မျက်-' ဟု ရေးသားရမည် ('မြက်' သည် မြက်ပင်အတွက်သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ဖျည့်(?:ဆည်း|စွက်|တင်း))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဖြည့်ဆည်း / ဖြည့်စွက် / ဖြည့်တင်း (ပြည့်စုံအောင် ဖြည့်သည် — ရရစ် 'ဖြည့်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပြည့်စုံအောင် ထည့်သွင်းခြင်းကို ရရစ်ဖြင့် 'ဖြည့်ဆည်း / ဖြည့်စွက် / ဖြည့်တင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဖျေ(?:ရှင်း|ဆို|ကြား)|အဖျေ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ဖြေရှင်း / ဖြေဆို / အဖြေ (မေးခွန်း/ပြဿနာ ဖြေသည် — ရရစ် 'ဖြေ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ မေးခွန်းဖြေဆိုခြင်းနှင့် ပြဿနာဖြေရှင်းခြင်းကို ရရစ်ဖြင့် 'ဖြေရှင်း / ဖြေဆို / အဖြေ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သတ်(?:သာ|ရောက်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "သက်သာ / သက်ရောက် (ချောင်လည်~သည်၊ အကျိုး~သည် — ကကြီးသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝေဒနာ/စရိတ် ချောင်လည်ခြင်းနှင့် အကျိုးရောက်ရှိခြင်းကို ကကြီးသတ်ဖြင့် 'သက်သာ / သက်ရောက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!သက်)(?<Target>သက်ဖြတ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "သတ်ဖြတ်",
                "သတ်ဖြတ် (အသက်သေစေသည် — တဝမ်းပူသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသက်သေစေခြင်းကို တဝမ်းပူသတ်ဖြင့် 'သတ်ဖြတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ရက်(?:တန့်|ကွက်|တည်))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ရပ်တန့် / ရပ်ကွက် / ရပ်တည် (ပစောက်သတ် 'ရပ်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရပ်တန့်ခြင်း၊ ရပ်တည်ခြင်းနှင့် မြို့ရွာရပ်ကွက်ကို ပစောက်သတ်ဖြင့် 'ရပ်-' ဟု ရေးသားရမည် ('ရက်' သည် နေ့ရက်/ရက်ကန်း၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>နေ့ရပ်)(?!ဌာနေ|လိပ်စာ|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "နေ့ရက်",
                "နေ့ရက် (အချိန်ကာလ နေ့နှင့်ရက် — ကကြီးသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ နေ့နှင့်ရက် အချိန်ကာလကို ကကြီးသတ်ဖြင့် 'နေ့ရက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ရပ်စွဲ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ရက်စွဲ",
                "ရက်စွဲ (စာချုပ်/ရုံးစာပါ နေ့စွဲ — ကကြီးသတ် 'ရက်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရုံးစာနှင့် စာချုပ်ပါ နေ့စွဲကို ကကြီးသတ်ဖြင့် 'ရက်စွဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ရက်စဲ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ရပ်စဲ",
                "ရပ်စဲ (အပစ်အခတ်/လုပ်ငန်း ~သည် — ပစောက်သတ် 'ရပ်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆက်မလုပ်ဘဲ ရပ်တန့်စဲသွားစေခြင်းကို ပစောက်သတ်ဖြင့် 'ရပ်စဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကြယ်(?:ဝန်း|ပြန့်|ပြော))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ကျယ်ဝန်း / ကျယ်ပြန့် / ကျယ်ပြော (အကျယ်အဝန်းကြီးမားသော — ယပင့် 'ကျယ်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အကျယ်အဝန်းကြီးမားခြင်းကို ယပင့်ဖြင့် 'ကျယ်ဝန်း / ကျယ်ပြန့် / ကျယ်ပြော' ဟု ရေးသားရမည် ('ကြယ်' သည် ကောင်းကင်ကြယ်တာရာဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ပျံ(?:လည်|ကြားရေး))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ပြန်လည် / ပြန်ကြားရေး (ရရစ် နသတ် 'ပြန်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ တစ်ဖန်ပြန်၍ ဆောင်ရွက်ခြင်းနှင့် သတင်းထုတ်ပြန်ခြင်းကို ရရစ် နသတ်ဖြင့် 'ပြန်လည် / ပြန်ကြားရေး' ဟု ရေးသားရမည် ('ပျံ' သည် ငှက်/လေယာဉ် ပျံသန်းခြင်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?:ကောင်းကင်|ဝေဟင်|လေထဲ|ငှက်|လေယာဉ်)[\u1000-\u103F\s]{0,14}?(?<Target>ပြန်(?:သန်း|ဝဲ))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "ပျံသန်း / ပျံဝဲ (ကောင်းကင်၌ ~သည် — ယပင့် သေးသေးတင် 'ပျံ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝေဟင်ကောင်းကင်၌ ပျံသန်းခြင်းကို ယပင့် သေးသေးတင်ဖြင့် 'ပျံသန်း / ပျံဝဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:စာသင်|အိပ်|ရုံး|အစည်းအဝေး|ဧည့်|ထမင်းစား|စာဖတ်|စာကြည့်)ခမ်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "စာသင်ခန်း / အိပ်ခန်း / ရုံးခန်း (အဆောက်အအုံ အခန်း — နသတ် 'ခန်း')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဆောက်အအုံအတွင်းရှိ အခန်းကန့်များကို နသတ်ဖြင့် 'စာသင်ခန်း / အိပ်ခန်း / ရုံးခန်း' ဟု ရေးသားရမည် ('ခမ်း' သည် ခမ်းနား/ရေခမ်းခြောက်ခြင်း၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<!အထက်)(?<Target>သက်တန်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "သက်တမ်း",
                "သက်တမ်း (အသက်ရှင်သန်ချိန် / ရာထူးကာလ — မသတ် 'တမ်း')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသက်ရှင်သန်ချိန်နှင့် တာဝန်ထမ်းဆောင်ချိန်ကာလကို မသတ်ဖြင့် 'သက်တမ်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!စုံ)(?<Target>စမ်းစစ်)(?!ဆေး|[\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဆန်းစစ်",
                "ဆန်းစစ် (စေ့စပ်သေချာစွာ စိစစ်လေ့လာသည် — ဆလိမ် နသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အချက်အလက်များကို စေ့စပ်စွာ စိစစ်လေ့လာခြင်းကို ဆလိမ် နသတ်ဖြင့် 'ဆန်းစစ်' ဟု ရေးသားရမည် ('စမ်း' သည် စမ်းသပ်ခြင်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ဆန်းသပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စမ်းသပ်",
                "စမ်းသပ် (လက်တွေ့ စမ်းကြည့်သည် — စလုံး မသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ လက်တွေ့စမ်းကြည့်ခြင်းကို စလုံး မသတ်ဖြင့် 'စမ်းသပ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ထူးစမ်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|သပ်|ရေ|ချောင်း|ကြည့်)",
                "Target",
                "ထူးဆန်း",
                "ထူးဆန်း (သာမန်ထက် ထူးခြားဆန်းပြားသော — ဆလိမ် နသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ထူးခြားဆန်းပြားခြင်းကို ဆလိမ် နသတ်ဖြင့် 'ထူးဆန်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စမ်းသစ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ဆန်းသစ်",
                "ဆန်းသစ် (တီထွင်~သော — ဆလိမ် နသတ်)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အသစ်တီထွင်ဆန်းပြားခြင်းကို ဆလိမ် နသတ်ဖြင့် 'ဆန်းသစ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>တွေ့(?:ခေါ်|တော))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "တွေးခေါ် / တွေးတော (စဉ်းစားဆင်ခြင်သည် — ဝဆွဲ ဝစ္စပေါက် 'တွေး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ စဉ်းစားဆင်ခြင်ခြင်းကို ဝစ္စပေါက်ဖြင့် 'တွေးခေါ် / တွေးတော' ဟု ရေးသားရမည် ('တွေ့' သည် တွေ့ဆုံ/တွေ့မြင်ခြင်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>တွေး(?:ဆုံ|ရှိ|ကြုံ))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "",
                "တွေ့ဆုံ / တွေ့ရှိ / တွေ့ကြုံ (အောက်မြစ် 'တွေ့')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဆုံစည်းမြင်တွေ့ခြင်းကို အောက်မြစ်ဖြင့် 'တွေ့ဆုံ / တွေ့ရှိ / တွေ့ကြုံ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?:မူဝါဒ|အမိန့်|ဆုံးဖြတ်ချက်|လမ်းညွှန်ချက်|ပြစ်ဒဏ်|စည်းကမ်း|စီမံချက်|ရည်မှန်းချက်|လုပ်ငန်းစဉ်|ဒဏ်ငွေ)[\u1000-\u103F\s]{0,12}?(?<Target>ခြမှတ်)|(?<Target>ခြမှတ်)(?=သည်|တယ်|ထား|ပေး|ခဲ့|မည်|ရန်|ကြောင်း))(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ချမှတ်",
                "ချမှတ် (မူဝါဒ/ဆုံးဖြတ်ချက် ~သည် — ယပင့် 'ချ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ မူဝါဒ၊ အမိန့် သို့မဟုတ် ဆုံးဖြတ်ချက် သတ်မှတ်ပေးခြင်းကို ယပင့်ဖြင့် 'ချမှတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>သေခြာ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "သေချာ",
                "သေချာ (တိကျခိုင်မာသော — ယပင့် 'ချာ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ တိကျခိုင်မာခြင်းကို ယပင့်ဖြင့် 'သေချာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အခြေခြ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "အခြေချ",
                "အခြေချ (အတည်တကျ နေထိုင်တည်ထောင်သည် — ယပင့် 'ချ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အတည်တကျ နေထိုင်တည်ထောင်ခြင်းကို 'အခြေချ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စိတ်ခြလက်ခြ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စိတ်ချလက်ချ",
                "စိတ်ချလက်ချ (စိုးရိမ်ပူပန်မှုကင်းစွာ — ယပင့် 'ချ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ စိုးရိမ်ပူပန်မှုကင်းစွာ ယုံကြည်စိတ်ချခြင်းကို 'စိတ်ချလက်ချ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စာကြက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စာကျက်",
                "စာကျက် (သင်ခန်းစာ အလွတ်မှတ်သားလေ့ကျင့်သည် — ယပင့် 'ကျက်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ စာအံမှတ်သားခြင်းကို ယပင့်ဖြင့် 'စာကျက်' ဟု ရေးသားရမည် ('ကြက်' သည် ကြက်ငှက်သတ္တဝါဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>နေရောင်ချည်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "နေရောင်ခြည်",
                "နေရောင်ခြည် (အလင်းတန်း — ရရစ် 'ခြည်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အလင်းရောင်တန်းကို ရရစ်ဖြင့် 'နေရောင်ခြည်' ဟု ရေးသားရမည် ('ချည်' သည် ကြိုးချည်/ချည်မျှင်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>အပ်ခြည်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "အပ်ချည်",
                "အပ်ချည် (အထည်ချုပ်ရန် ချည်မျှင် — ယပင့် 'ချည်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အထည်ချုပ်သော ချည်မျှင်ကို ယပင့်ဖြင့် 'အပ်ချည်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စီးလုံး)(?=ညီညွတ်)",
                "Target",
                "စည်းလုံး",
                "စည်းလုံးညီညွတ် (တစ်စုတစ်စည်းတည်း ပေါင်းစည်းသည် — ညကြီးသတ် 'စည်း')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ညီညွတ်ပေါင်းစည်းခြင်းကို ညကြီးသတ်ဖြင့် 'စည်းလုံးညီညွတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>စည်းပွားရေး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စီးပွားရေး",
                "စီးပွားရေး (ဥစ္စာဓန တိုးပွားရေးလုပ်ငန်း — လုံးကြီးတင်ဆန်ခတ် 'စီး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကုန်သွယ်မှုနှင့် ဥစ္စာဓနလုပ်ငန်းကို 'စီးပွားရေး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဂရုဏာ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ကရုဏာ",
                "ကရုဏာ (သနားကြင်နာခြင်း — ပါဠိမူရင်း ကကြီး 'ကရုဏာ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် ပါဠိရင်းမြစ်အရ သနားကြင်နာခြင်းကို ကကြီးဖြင့် 'ကရုဏာ' ဟု ရေးသားရမည် ('ဂရု' သည် 'ဂရုစိုက်/ဂရုပြု' ၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ကြည့်ရှု့)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ကြည့်ရှု",
                "ကြည့်ရှု (အောက်မြစ်မပါ စံသတ်ပုံ 'ရှု')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြည့်ရှုလေ့လာခြင်းကို 'ရှု' ၌ အောက်မြစ်မပါဘဲ 'ကြည့်ရှု' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!သည်း)(?<Target>ခြေလမ်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|ကြောင်း)",
                "Target",
                "ခြေလှမ်း",
                "ခြေလှမ်း (ခြေဖြင့် လှမ်းသော အလှမ်း — ဟထိုး 'လှမ်း')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခြေဖြင့်လှမ်းခြင်းကို ဟထိုးဖြင့် 'ခြေလှမ်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?:ရောဂါ|ကပ်ရောဂါ|ဗိုင်းရပ်စ်|ပိုး|ဝေဒနာ|ဖျားနာ|တုပ်ကွေး|ကိုဗစ်|မီး)[\u1000-\u103F\s]{0,16}?(?<!ပေါင်း)(?<Target>ကူးဆက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|သွယ်|ဆံ)|(?<!ပေါင်း)(?<Target>ကူးဆက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A|သွယ်|ဆံ)[\u1000-\u103F\s]{0,12}?(?:ရောဂါ|ကပ်ရောဂါ|ဗိုင်းရပ်စ်|ပိုး|ဝေဒနာ|ပျံ့နှံ့))",
                "Target",
                "ကူးစက်",
                "ကူးစက် (ရောဂါ/မီး ပျံ့နှံ့~သည် — စလုံး 'စက်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရောဂါ သို့မဟုတ် မီး ပျံ့နှံ့ခြင်းကို စလုံးဖြင့် 'ကူးစက်' ဟု ရေးသားရမည် ('ကူးဆက်' သည် တစ်နေရာနှင့်တစ်နေရာ ဆက်သွယ်ပေါင်းကူးသည့် အနက်၌သာ သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?:(?:ဆရာဝန်|သူနာပြု|သီချင်း|အသံ|ဖုန်း)[\u1000-\u103F\s]{0,14}?(?<Target>နားကျပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)|(?<Target>နားကျပ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)[\u1000-\u103F\s]{0,16}?(?:ကိရိယာ|တပ်|နားထောင်|ကိုင်|ချိတ်))",
                "Target",
                "နားကြပ်",
                "နားကြပ် (နားထောင်ကိရိယာ / ဆရာဝန်သုံး နားကြပ်ကိရိယာ — ရရစ် 'ကြပ်')",
                "မြန်မာအဘိဓာန်နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (စာ-၃၅၂) အရ အသံနားထောင်ကိရိယာ/ဆရာဝန်သုံးကိရိယာကို ရရစ်ဖြင့် 'နားကြပ် / နားကြပ်ကိရိယာ' ဟု ရေးသားရမည် ('နားကျပ်-' သည် နားပေါက်သန့်စင်သည့် ကြိယာအနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>စွက်ပက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စွက်ဖက်",
                "စွက်ဖက် (သူတစ်ပါးကိစ္စ ဝင်ရောက်~သည် — ဖဦးထုပ် 'ဖက်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဝင်ရောက်နှောင့်ယှက်ခြင်းကို ဖဦးထုပ်ဖြင့် 'စွက်ဖက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:ကျီး|ချီ)ကျူး)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ချီးကျူး",
                "ချီးကျူး (ဂုဏ်ကျေးဇူးကို ထုတ်ဖော်ပြောဆိုသည် — 'ချီးကျူး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဂုဏ်ပြုထောမနာပြုခြင်းကို 'ချီးကျူး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ချက်ဖြုတ်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ချက်ပြုတ်",
                "ချက်ပြုတ် (ထမင်းဟင်း ~သည် — ရရစ် 'ပြုတ်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အစားအစာ ချက်လုပ်ခြင်းကို 'ချက်ပြုတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကော်ပိုရှေးရှင်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ကော်ပိုရေးရှင်း",
                "ကော်ပိုရေးရှင်း (Corporation — ရကောက် 'ရေး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ Corporation ကို ရကောက်ဖြင့် 'ကော်ပိုရေးရှင်း' ဟု ရေးသားရမည်။"
            ),
            // 37. V56 Hidden Over-Split Compound Typos, Progressive Aspect Vowel Confusions & Unspaced Locative '-မာ -> -မှာ'
            new CollocationRule(
                @"(?<Target>ခုန်ကျ)(?=စရိတ်|ငွေ|တန်ဖိုး|စာရင်း)",
                "Target",
                "ကုန်ကျ",
                "ကုန်ကျစရိတ် / ကုန်ကျငွေ (သုံးစွဲကုန်ကျသော စရိတ် — ကကြီး 'ကုန်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ငွေကြေးစရိတ် သုံးစွဲကုန်ဆုံးခြင်းကို ကကြီးဖြင့် 'ကုန်ကျစရိတ် / ကုန်ကျငွေ' ဟု ရေးသားရမည် ('ခုန်' သည် ခုန်ပေါက်ခြင်းဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<Target>ဆာမေးပွဲ)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "စာမေးပွဲ",
                "စာမေးပွဲ (ပညာအရည်အချင်း စစ်ဆေးသောပွဲ — စလုံး 'စာ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပညာစစ်ဆေးမေးမြန်းသောပွဲကို စလုံးဖြင့် 'စာမေးပွဲ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ဘာလုပ်|လုပ်|စား|သောက်|သွား|လာ|ထိုင်|အိပ်|ပြော|ကြည့်|ဖတ်|ရေး|စောင့်|ရှာ|နေထိုင်))(?<Target>နီ)(?=(?:လဲ|သလဲ|သနည်း|တာလဲ|တယ်|သည်|ပါတယ်|ပါသည်|လား|သလား)(?=\s|$|[\u104A\u104B?]))",
                "Target",
                "နေ",
                "နေ (လုပ်ဆောင်ဆဲ ဖြစ်ပေါ်ဆဲပြ ကြိယာထောက်ပစ္စည်း — 'ဘာလုပ်နေလဲ')",
                "မြန်မာသဒ္ဒါနှင့် စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကြိယာဖြစ်ဆဲပြပစ္စည်းကို သဝေထိုးဖြင့် 'နေ' (ဥပမာ - ဘာလုပ်နေလဲ) ဟု ရေးသားရမည် ('နီ' သည် အနီရောင်အနက်ဖြစ်သည်)။"
            ),
            // 38. V58 Benchmark-Driven Contextual Homophones, Verbal Aukmyit Confusions & Compound Collocations
            new CollocationRule(
                @"(?:(?<=(?:တစ်နည်း|စ|မင်း|သူ|ကျွန်တော်|ကျွန်မ|ဆရာက|ဆရာမက|လို့|လို့လည်း|ဟု)\s*)(?<Target>ပြော့)(?=(?:ဆို|ပြ|ကြား|ခဲ့|ပါ|သည်|တယ်|လိုက်|လို့|တော့|နေ|ရ))|(?<Target>ပြော့)(?=(?:ဆို|ပြ|ကြား|ခဲ့ဖူး|ပါတယ်|ပါသည်|လိုက်|လို့ရ)))",
                "Target",
                "ပြော",
                "ပြော (နှုတ်ဖြင့် ပြောဆိုသည် — အောက်မြစ်မပါ 'ပြော')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ နှုတ်ဖြင့် ပြောဆိုခြင်းကို အောက်မြစ်မပါဘဲ 'ပြော' ဟု ရေးသားရမည် ('ပြော့' သည် မာကျောမှုမရှိ ပျော့ပျောင်းခြင်းအနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=အစာ\s*|အစားအစာ\s*)(?<Target>ကြွေး)(?=\s*(?:ပါ|သည်|တယ်|မည်|မယ်|ထား|ခဲ့|မွေး)|$)",
                "Target",
                "ကျွေး",
                "ကျွေး (အစာ ~သည် — ယပင့် ဝဆွဲ 'ကျွေး')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား) အရ အစားအစာ ပေးကမ်းကျွေးမွေးခြင်းကို ယပင့်ဖြင့် 'ကျွေး' ဟု ရေးသားရမည် ('ကြွေး' သည် ကြွေးမြီအနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=သတင်း(?:ကို|များ|တွေ)?\s*)(?<Target>ကျား)(?=\s*(?:ရ|သိ|ဖူး|ခဲ့|သည်|တယ်))",
                "Target",
                "ကြား",
                "ကြား (နားဖြင့် သတင်း~ရသည် — ရရစ် 'ကြား')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား) အရ နားဖြင့် ကြားသိခြင်းကို ရရစ်ဖြင့် 'ကြား' ဟု ရေးသားရမည် ('ကျား' သည် သားရဲတိရစ္ဆာန်/အဖို အနက်ဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=တော(?:ထဲ|နက်)(?:မှာ|တွင်|၌)?\s*)(?<Target>ကြား)(?=\s*(?:တွေ|များ|ကြီး|သစ်|ရဲ))",
                "Target",
                "ကျား",
                "ကျား (တောနေ သားရဲတိရစ္ဆာန် — ယပင့် 'ကျား')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ တောနေသားရဲတိရစ္ဆာန်ကို ယပင့်ဖြင့် 'ကျား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<=(?:ထမင်း|ညနေစာ|နံနက်စာ|မနက်စာ|နေ့လယ်စာ|မုန့်)\s*)(?<Target>ဆား)(?=(?:ပြီး|ဖို့|ရန်|မယ်|မည်|တယ်|သည်|ကြ|ပါ))|(?<=လျှောက်မ|ရှောက်မ)(?<Target>ဆား)(?=နဲ့|နှင့်))",
                "Target",
                "စား",
                "စား (အစားအစာ ~သုံးသည် — စလုံး 'စား')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အစားအစာ စားသုံးခြင်းကို စလုံးဖြင့် 'စား' ဟု ရေးသားရမည် ('ဆား' သည် အငန်ဓာတ် ဟင်းခတ်ဆားဖြစ်သည်)။"
            ),
            new CollocationRule(
                @"(?<=ဟင်းချက်[\u1000-\u103F\s]{0,12}?)(?<Target>စား)(?=(?:နည်းနည်း|အနည်းငယ်|ခတ်|ထည့်))",
                "Target",
                "ဆား",
                "ဆား (ဟင်းခတ် အငန်ဓာတ် — ဆလိမ် 'ဆား')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဟင်းခတ်ဆားကို ဆလိမ်ဖြင့် 'ဆား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<=(?:ကျွန်တော့်|ကျွန်မရဲ့|ငါ့|သင်္ချာ|ကျောင်း)\s*)(?<Target>စရာ)(?=\s*(?:က|သည်|ကြီး|မ))|(?<=^|[\u104A\u104B]\s*)(?<Target>စရာ)(?=\s*ရေ\s*ကို\s*လေးစား))",
                "Target",
                "ဆရာ",
                "ဆရာ (သင်ကြားပို့ချသူ — ဆလိမ် 'ဆရာ')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပညာသင်ကြားပေးသူကို ဆလိမ်ဖြင့် 'ဆရာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:စား|သောက်|ကြည့်|မှတ်သား|စိတ်လှုပ်ရှား))(?<Target>ဆရာ)(?=\s*(?:ကောင်း|ရှိ|မရှိ))",
                "Target",
                "စရာ",
                "စရာ (ကြိယာနောက်ဆက် နာမ်ပြုပစ္စည်း 'စားစရာ / မှတ်သားစရာ' — စလုံး 'စရာ')",
                "မြန်မာသဒ္ဒါအရ ကြိယာကို နာမ်ပြုသည့်ပစ္စည်းကို စလုံးဖြင့် '~စရာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:အရမ်း|သိပ်|အလွန်|စား|စရာ|ပျော်စရာ))(?<Target>ကောင်)(?=တယ်|သည်|ပါတယ်|ပါသည်)",
                "Target",
                "ကောင်း",
                "ကောင်း (ကောင်းမွန်သည် — ဝစ္စပေါက် 'ကောင်း')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကောင်းမွန်ခြင်းအနက်တွင် ဝစ္စပေါက်ဖြင့် 'ကောင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<=ကား[\u1000-\u103F\s]{0,14}?(?:အရမ်း|သိပ်|အလွန်))(?<Target>မန်)(?=တယ်|သည်)|(?<=(?:ထွက်|ရောက်|သိ|တွေ့|အောင်|စား|သွား|လာ|လုပ်))(?<Target>ပီ)(?=\s|$|[\u104A\u104B])|(?<=ကျေးဇူးပြု၍\s*)(?<Target>ရှင်)(?=ပေး|ပြ)|(?<=^|[\u104A\u104B]\s*)(?<Target>ကာ)(?=\s*လမ်းပေါ်)|(?<=(?:ထမင်းစား|အိပ်ရာထ|မျက်နှာသစ်))(?<Target>ပြီ)(?=\s+[\u1000-\u1021][\u1000-\u103F]*(?:ပြီး|၍)))",
                "Target",
                "",
                "စကားစပ်အရ စာလုံးပေါင်းအမှန်",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် မြန်မာသဒ္ဒါအရ စကားစပ်နှင့် ကိုက်ညီသော စာလုံးပေါင်းအမှန်ဖြင့် ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<Target>နိုင်ငံသာ)(?=တွေ|များ|တိုင်း|တစ်ဦး)|(?<Target>ကလေ)(?=တွေ|များ)|(?<=ငါးဖမ်း[\u1000-\u103F\s]{0,14}?)(?<Target>ငါ)(?=တွေကို|များကို))",
                "Target",
                "",
                "ဝစ္စပေါက် ('း') ပါဝင်ရမည့် နာမ်ပုဒ် (နိုင်ငံသား / ကလေး / ငါး)",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ဤနာမ်ပုဒ်တွင် ဝစ္စပေါက် ('း') ထည့်သွင်းရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?:(?<Target>ခစားကွင်း)|(?<Target>ကဆား)(?=ခဲ့|သည်|တယ်|ကြ|ကွင်း)|(?<Target>စပါ)(?=စိုက်ပျိုး)|(?<Target>တရာ)(?=ဓမ္မ)|(?<Target>မုန်တိုင်)(?=တိုက်)|(?<Target>အရာသာ)(?=ရှိ|ကောင်း)|(?<Target>စောင့်လျှောက်)|(?<=လူကြိုက်)(?<Target>မြား)|(?<Target>ပျော်ရွှေ့)(?=စွာ)|(?<Target>မရှိမပြတ်)|(?<Target>အသီးအရွတ်)|(?<Target>အပန်းချွေ)|(?<Target>သတိသား)(?=ရမယ်|ရမည်|ပါ)|(?<Target>အကျိုးတက်ရောက်)|(?<Target>စတုတ္တ)(?=မြောက်|အကြိမ်)|(?<Target>အပျိုဘော်ဝင်))",
                "Target",
                "",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း စံသတ်ပုံအမှန်",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့်အညီ ပေါင်းစပ်ပုဒ် စာလုံးပေါင်းအမှန်ဖြင့် ရေးသားရမည်။"
            ),
            // Transitive Simplex-for-Causative (ဟထိုး / အပြုကြိယာ) Verb-Frame Rules (strictly guarded by [Object]ကို + (?!စေ) so intransitives & 'ပျက်စီးစေ' are never touched!)
            new CollocationRule(
                @"(?<=(?:သစ်တော|အဆောက်အအုံ|တံတား|လမ်း|ပစ္စည်း|သဘာဝပတ်ဝန်းကျင်|အမွေအနှစ်|အထောက်အထား|စာရွက်စာတမ်း)(?:များ|တွေ)?ကို\s+)(?<Target>ပျက်စီး)(?!စေ)(?=ခဲ့|ကြ|သည်|တယ်|မည်|မယ်|ရန်|ဖို့|ခြင်း|၍|ပြီး|ကာ|လျက်)",
                "Target",
                "ဖျက်ဆီး",
                "ကံပုဒ် ('~ကို') ပါသော အပြုကြိယာ ('ဖျက်ဆီး')",
                "ရှေ့တွင် ကံပုဒ် ('~ကို') ပါရှိပြီး ကိုယ်တိုင်ပြုလုပ်ဖျက်ဆီးခြင်းကို ပြသဖြင့် အဖြစ်ကြိယာ 'ပျက်စီး' အစား အပြုကြိယာ 'ဖျက်ဆီး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:အထောက်အထား|သက်သေ|စာရွက်စာတမ်း|အချက်အလက်|ခြေရာလက်ရာ)(?:များ|တွေ)?ကို\s+)(?<Target>ပျောက်ဖျက်|ပျောက်ပျက်)(?!စေ)(?=ခဲ့|ကြ|သည်|တယ်|မည်|မယ်|ရန်|ဖို့|ခြင်း|၍|ပြီး|ကာ|လျက်)",
                "Target",
                "ဖျောက်ဖျက်",
                "ကံပုဒ် ('~ကို') ပါသော အပြုကြိယာ ('ဖျောက်ဖျက်')",
                "သက်သေအထောက်အထား စသည်ကို တမင်ပျောက်ကွယ်အောင် ပြုလုပ်ခြင်းဖြစ်သဖြင့် အပြုကြိယာ 'ဖျောက်ဖျက်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:ဖုန်းလိုင်း|အင်တာနက်လိုင်း|လျှပ်စစ်မီး|ရေပေးဝေမှု|အဆက်အသွယ်|ထောက်ပံ့မှု|လမ်းကြောင်း)(?:များ|တွေ)?ကို\s+)(?<Target>ပြတ်တောက်)(?!စေ)(?=ခဲ့|ကြ|သည်|တယ်|မည်|မယ်|ရန်|ဖို့|ခြင်း|၍|ပြီး|ကာ|လျက်)",
                "Target",
                "ဖြတ်တောက်",
                "ကံပုဒ် ('~ကို') ပါသော အပြုကြိယာ ('ဖြတ်တောက်')",
                "ရှေ့တွင် ကံပုဒ် ('~ကို') ပါရှိပြီး တာဝန်ရှိသူက ပြတ်တောက်စေရန် ပြုလုပ်ခြင်းဖြစ်သဖြင့် အပြုကြိယာ 'ဖြတ်တောက်' (ဟထိုးပါ) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အနှစ်နာခံ)",
                "Target",
                "အနစ်နာခံ",
                "ကိုယ်ကျိုးစွန့် အနစ်နာခံခြင်း ('အနစ်နာခံ' — ဟထိုးမပါ)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ကိုယ်ကျိုးစွန့်၍ နစ်နာခံခြင်းကို ဟထိုးမပါဘဲ 'အနစ်နာခံ' ဟု ရေးသားရမည် ('အနှစ်သာရ' ၌သာ ဟထိုးပါသည်)။"
            ),
            new CollocationRule(
                @"(?<=(?:သယံဇာတ|သတ္တု|ရေနံ|ကျောက်မျက်|ရတနာ|ရှေးဟောင်း|မြေအောက်)[\u1000-\u103F\s]{0,24}?)(?<Target>တုဖော်)",
                "Target",
                "တူးဖော်",
                "မြေအောက်သယံဇာတ တူးဖော်ခြင်း ('တူးဖော်')",
                "မြေကြီး/သယံဇာတ တူးဆွဖော်ထုတ်ခြင်းကို နှစ်ချောင်းငင် ဝစ္စပေါက်ဖြင့် 'တူးဖော်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ထုပ်ယူ)(?=ရာ|သည်|တယ်|ကြ|ခဲ့|ရန်|ဖို့|ခြင်း)",
                "Target",
                "ထုတ်ယူ",
                "အပြင်သို့ ဆွဲထုတ်ယူငင်ခြင်း ('ထုတ်ယူ')",
                "အတွင်းမှ အပြင်သို့ ဖော်ထုတ်ယူငင်ခြင်းကို တဝမ်းပူသတ်ဖြင့် 'ထုတ်ယူ' ဟု ရေးသားရမည် ('ထုပ်' = အထုပ်အပိုး)။"
            ),
            new CollocationRule(
                @"(?<=\s)(?<Target>တွေ)(?=ရတယ်|ရသည်|ရပါတယ်|ရပါသည်)",
                "Target",
                "တွေ့",
                "မြင်တွေ့ရခြင်း ('တွေ့ရ~')",
                "ကြိယာထောက်ပစ္စည်း 'ရတယ်/ရသည်' ရှေ့ရှိ မြင်တွေ့ခြင်းကြိယာတွင် အောက်မြစ်ပါဝင်လျက် 'တွေ့' ('တွေ့ရတယ်') ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဆိုပြီး\s+)(?<Target>မတ်)(?=ထားတယ်|ထားသည်|ထားပါ)",
                "Target",
                "မှတ်",
                "စိတ်၌ မှတ်သားထားခြင်း ('မှတ်ထား~')",
                "စကားရပ်နောက် 'ဆိုပြီး' နှင့်တွဲသော စိတ်၌ မှတ်သားခြင်းကြိယာကို ဟထိုးပါဝင်လျက် 'မှတ်ထား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=ဆောမြို့နယ်၊\s*)(?<Target>သိမီး)(?=ကျေးရွာ)",
                "Target",
                "ဆီမီး",
                "ဆောမြို့နယ်ရှိ ဆီမီးကျေးရွာအုပ်စု (GAD စံအမည်)",
                "ဂန့်ဂေါခရိုင် ဆောမြို့နယ်ရှိ ရုံးသုံးကျေးရွာအုပ်စုအမည်မှန်မှာ 'ဆီမီးကျေးရွာအုပ်စု' ဖြစ်သည်။"
            ),
            new CollocationRule(
                @"(?<Target>စာတိုက်သင်္ကေတကုတ်)",
                "Target",
                "စာတိုက်သင်္ကေတကုဒ်",
                "စာတိုက်သင်္ကေတကုဒ် (Postal Code — ဒထွေးသတ် 'ကုဒ်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ကေတအမှတ်အသား (Postal Code) အနက်တွင် ဒထွေးသတ်ဖြင့် 'စာတိုက်သင်္ကေတကုဒ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<!စာတိုက်)(?<Target>သင်္ကေတကုတ်)",
                "Target",
                "သင်္ကေတကုဒ်",
                "သင်္ကေတကုဒ် (Code — ဒထွေးသတ် 'ကုဒ်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ကေတအမှတ်အသား (Code) အနက်တွင် ဒထွေးသတ်ဖြင့် 'သင်္ကေတကုဒ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ပို့စ်ကုတ်)",
                "Target",
                "ပို့စ်ကုဒ်",
                "ပို့စ်ကုဒ် (Postcode — ဒထွေးသတ် 'ကုဒ်')",
                "စာတိုက်သင်္ကေတ (Postcode) အနက်တွင် ဒထွေးသတ်ဖြင့် 'ပို့စ်ကုဒ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=(?:အခွန်|ဝင်ငွေခွန်|ကုန်သွယ်လုပ်ငန်းခွန်|အကောက်ခွန်|မြေခွန်|အခွန်အကောက်)[ \t]*)(?<Target>စည်းကျပ်)",
                "Target",
                "စည်းကြပ်",
                "စည်းကြပ် (အခွန်~ကောက်ခံသည် — ရရစ် 'ကြပ်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အခွန်အကောက် စည်းကြပ်ခြင်းကို ရရစ်ဖြင့် 'စည်းကြပ်' ဟု ရေးသားရမည် ('စည်းကမ်းစည်းကျပ်' ၌သာ ယပင့်သုံးသည်)။"
            ),
            new CollocationRule(
                @"(?<Target>တရားဆွဲဆို)",
                "Target",
                "တရားစွဲဆို",
                "တရားစွဲဆို (ဥပဒေအရ အမှု~သည် — စလုံးဝဆွဲ 'စွဲ')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တရားရုံး၌ အမှုစွဲဆိုခြင်းကို စလုံးဝဆွဲဖြင့် 'တရားစွဲဆို' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဆွဲချက်တင်)",
                "Target",
                "စွဲချက်တင်",
                "စွဲချက်တင် (တရားရုံး၌ အမှု~သည် — စလုံးဝဆွဲ 'စွဲ')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တရားရုံး၌ စွဲချက်တင်ခြင်းကို စလုံးဝဆွဲဖြင့် 'စွဲချက်တင်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လျောက်ထား)",
                "Target",
                "လျှောက်ထား",
                "လျှောက်ထား (အသနားခံစာ/လျှောက်လွှာ တင်ပြ~သည် — ဟထိုး 'လျှောက်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အထက်ဌာန/တရားရုံးသို့ တင်ပြလျှောက်ထားခြင်းကို ယပင့်ဟထိုးဖြင့် 'လျှောက်ထား' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>လျောက်လွှာ)",
                "Target",
                "လျှောက်လွှာ",
                "လျှောက်လွှာ (တင်ပြတောင်းခံသည့် စာ~ — ဟထိုး 'လျှောက်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တင်ပြလျှောက်ထားသည့်စာကို ယပင့်ဟထိုးဖြင့် 'လျှောက်လွှာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အနှောက်အယှက်)",
                "Target",
                "အနှောင့်အယှက်",
                "အနှောင့်အယှက် (နှောင့်ယှက်ဟန့်တားမှု — ငသတ်အောက်မြစ် 'အနှောင့်အယှက်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'အနှောင့်အယှက်' (ငသတ် အောက်မြစ်) ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>နှောက်နှေး)",
                "Target",
                "နှောင့်နှေး",
                "နှောင့်နှေး (ကြန့်ကြာ~သည် — ငသတ်အောက်မြစ် 'နှောင့်နှေး')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အချိန်ကြန့်ကြာနှောင့်နှေးခြင်းကို ငသတ် အောက်မြစ်ဖြင့် 'နှောင့်နှေး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဖြည့်စည်း)",
                "Target",
                "ဖြည့်ဆည်း",
                "ဖြည့်ဆည်း (လိုအပ်ချက်များ ~ပေးသည် — ဆလိမ် 'ဆည်း')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ လိုအပ်ချက် ဖြည့်ဆည်းပေးခြင်းကို ဆလိမ်ဖြင့် 'ဖြည့်ဆည်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>အကျိုးအမြက်)",
                "Target",
                "အကျိုးအမြတ်",
                "အကျိုးအမြတ် (စီးပွားရေး ~ရလဒ် — တဝမ်းပူသတ် 'အမြတ်')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အကျိုးစီးပွားနှင့် အမြတ်အစွန်းကို တဝမ်းပူသတ်ဖြင့် 'အကျိုးအမြတ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>(?:ဈေး|စျေး)ဝယ်ချင်)(?=အတွက်|ကြောင့်|ဖြင့်)",
                "Target",
                "ဈေးဝယ်ခြင်း",
                "ဈေးဝယ်ခြင်း (ကြိယာနာမ် '~ခြင်းအတွက်')",
                "နာမ်ဝိဘတ် 'အတွက်/ကြောင့်/ဖြင့်' ရှေ့တွင် ကြိယာနာမ်ပြုပစ္စည်းဖြင့် 'ဈေးဝယ်ခြင်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<=အွန်လိုင်း[ \t]*)(?<Target>(?:ဈေး|စျေး)ဝင်)(?=တာ|ခြင်း|မှု|ရတာ|လို့)",
                "Target",
                "ဈေးဝယ်",
                "ဈေးဝယ် (အွန်လိုင်း~ခြင်း)",
                "အွန်လိုင်းမှ ပစ္စည်းဝယ်ယူခြင်းအနက်တွင် 'ဈေးဝယ်' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဘဏ္ဏာရေး)",
                "Target",
                "ဘဏ္ဍာရေး",
                "ဘဏ္ဍာရေး (ဏကြီး ဍရင်မှုတ်ဆင့် — 'ဘဏ္ဍာရေး')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ငွေကြေးဘဏ္ဍာအနက်တွင် ဏကြီး ဍရင်မှုတ်ဆင့်ဖြင့် 'ဘဏ္ဍာရေး' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ဘဏ္ဏာ)(?!ရေး)",
                "Target",
                "ဘဏ္ဍာ",
                "ဘဏ္ဍာ (ဏကြီး ဍရင်မှုတ်ဆင့် — 'ဘဏ္ဍာ')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ ပစ္စည်းဥစ္စာဘဏ္ဍာကို ဏကြီး ဍရင်မှုတ်ဆင့်ဖြင့် 'ဘဏ္ဍာ' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>တစ်သမှတ်တည်း)",
                "Target",
                "တစ်သမတ်တည်း",
                "တစ်သမတ်တည်း (တစ်ပြေးညီ မပြောင်းမလဲ — ဟထိုးမပါ 'တစ်သမတ်တည်း')",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ တစ်ပြေးညီ မပြောင်းမလဲအနက်တွင် ဟထိုးမပါဘဲ 'တစ်သမတ်တည်း' ဟု ရေးသားရမည်။"
            )
        };

    }
}
