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
    public sealed class ContextDisambiguator
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
                @"(?<Target>(?:သတင်းထုတ်ပြန်|ထုတ်ပြန်|တရားဝင်)ကြော်ငြာ(?<Suffix>ချက်|သည်)?)",
                "Target",
                "",
                "ကြေညာ (အများသိစေရန် ထုတ်ပြန်~သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အများသိစေရန် ထုတ်ပြန်ခြင်းကို 'ကြေညာ' ဟု ရေးသားရမည်။ ('ကြော်ငြာ' သည် စီးပွားရေးကုန်ပစ္စည်း ကြော်ငြာခြင်းဖြစ်သည်)။"
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
                @"(?:အဝတ်အစား|အဝတ်အထည်|အင်္ကျီ|ပုဆိုး|ထဘီ|စောင်|ခြင်ထောင်|ဆံပင်)[^\u104A\u104B\r\n]{0,18}?(?<Target>လျော်)(?=ဖွပ်|ပေး|ပြီး|နေ|ထား|ရန်|သည်)",
                "Target",
                "လျှော်",
                "လျှော် (ရေ/ဆပ်ပြာဖြင့် အဝတ်~ဖွပ်သည်)",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ အဝတ်အထည်/ဆံပင် ရေဖြင့် လျှော်ဖွပ်ခြင်းကို ဟထိုးဖြင့် 'လျှော်' ဟု ရေးသားရမည် ('လျော်' သည် လျော်ကြေးပေးလျော်ခြင်း/သင့်လျော်ခြင်း ဖြစ်သည်)။"
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
                @"(?<Target>ခြေလမ်း)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ခြေလှမ်း",
                "ခြေလှမ်း (ခြေဖြင့် လှမ်းသော အလှမ်း — ဟထိုး 'လှမ်း')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ခြေဖြင့်လှမ်းခြင်းကို ဟထိုးဖြင့် 'ခြေလှမ်း' ဟု ရေးသားရမည်။"
            ),
            new CollocationRule(
                @"(?<Target>ကူးဆက်)(?![\u102B-\u103E]|[\u1000-\u1021]\u103A)",
                "Target",
                "ကူးစက်",
                "ကူးစက် (ရောဂါ/မီး ပျံ့နှံ့~သည် — စလုံး 'စက်')",
                "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ ရောဂါ သို့မဟုတ် မီး ပျံ့နှံ့ခြင်းကို စလုံးဖြင့် 'ကူးစက်' ဟု ရေးသားရမည်။"
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
            )
        };

        private static HashSet<string> CreateNormalizedSet(params string[] items)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Length; i++)
            {
                set.Add(UnicodeNormalizer.NormalizeCanonicalOrder(items[i]));
            }
            return set;
        }

        // Particles, pronouns, nominalizers, and numerals excluded from statistical bigram replacement so grammar words are never altered
        private static readonly HashSet<string> ProtectedFunctionWords = CreateNormalizedSet(
            "ကို", "အား", "မှ", "သို့", "နှင့်", "သည်", "၏", "၌", "တွင်", "ဝယ်", "က", "ကနေ",
            "မှာ", "များ", "တို့", "သည့်", "သော", "မည့်", "မည်", "ပါ", "ပါ့", "ကြ", "ကျ", "ခဲ့",
            "လျက်", "၍", "ကြောင့်", "ဖြင့်", "သဖြင့်", "ကာ", "သာ", "ပင်", "စွာ", "လည်း", "ဘဲ", "ပြီ", "ပြီး", "လျှင်", "ဟု",
            "ခြင်း", "ချင်း", "မှု", "ရင်း", "စဉ်", "တိုင်း", "ဖို့", "ရန်", "ချင်", "တတ်", "နိုင်", "စေ", "လိုက်",
            "တယ်", "မယ်", "မယ့်", "တွေ", "နဲ့", "ဘူး", "တဲ့", "တာ", "ပေါ့", "လား", "လဲ",
            "နော်", "ဗျ", "ဗျာ", "ရှင်", "ရှင့်", "ရဲ့", "လို့", "ပဲ", "တုန်း", "သေး", "တော့", "ဦး",
            "ဒီ", "ဟို", "အဲဒီ", "ထို", "ဤ", "ယင်း", "၎င်း", "သူ", "ငါ", "မင်း", "သင်", "ဘာ", "ဘယ်",
            "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး", "ဆယ်", "ရာ", "ထောင်", "သောင်း", "သိန်း", "သန်း",
            "မ", "အ", "တ", "ရ", "နေ", "ထား", "ပေး", "သွား", "လာ"
        );

        // Generic syntactic particles that attach to any verb/noun and therefore must not act as semantic collocation triggers in bigram disambiguation
        private static readonly HashSet<string> GenericSyntacticParticles = CreateNormalizedSet(
            "သည်", "တယ်", "မည်", "မယ်", "ပါသည်", "ပါတယ်", "ပါမည်", "ပါမယ်",
            "ကို", "အား", "မှ", "သို့", "နှင့်", "နဲ့", "၏", "ရဲ့", "တွင်", "မှာ", "၌", "က", "ကနေ",
            "များ", "တွေ", "တို့", "သော", "သည့်", "မည့်", "မယ့်", "တဲ့", "တာ",
            "ပါ", "ခဲ့", "ပြီ", "ပြီး", "၍", "လျက်", "ကာ", "ကြောင့်", "ဖြင့်", "သဖြင့်",
            "ဟု", "လို့", "လည်း", "ပင်", "သာ", "ပဲ", "ဘဲ", "ဘူး", "လား", "လဲ", "နော်", "ပေါ့", "တော့", "သေး"
        );

        private sealed class ClauseWordToken
        {
            public int StartIndex { get; set; }
            public int Length { get; set; }
            public int EndIndex => StartIndex + Length;
            public int ClauseId { get; set; }
            public int SyllableCount { get; set; }
            public string RawText { get; set; } = string.Empty;
            public string NormText { get; set; } = string.Empty;
        }

        public List<ProofingIssue> AnalyzeContextHomophones(string text, MyanmarTrie? trie = null)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text))
                return issues;

            ulong textMmMask = OfficialStyleGrammarEngine.ComputeMyanmarCharMask(text);

            // 1. High-precision compiled regex collocation & homophone rules
            for (int rIdx = 0; rIdx < Rules.Count; rIdx++)
            {
                var rule = Rules[rIdx];
                if (string.IsNullOrEmpty(rule.Explanation))
                    continue;

                if (rule.RequiredAnyLiterals != null)
                {
                    if ((textMmMask & rule.CommonRequiredMask) != rule.CommonRequiredMask)
                        continue;
                    if (rule.AllRequiredHaveMyanmarChar && (textMmMask & rule.AnyAnchorCharMask) == 0UL)
                        continue;

                    bool anyAnchorHit = false;
                    string[] req = rule.RequiredAnyLiterals;
                    ulong[] reqMasks = rule.RequiredLiteralMasks!;
                    for (int k = 0; k < req.Length; k++)
                    {
                        ulong litMask = reqMasks[k];
                        if ((textMmMask & litMask) == litMask &&
                            text.IndexOf(req[k], StringComparison.Ordinal) >= 0)
                        {
                            anyAnchorHit = true;
                            break;
                        }
                    }
                    if (!anyAnchorHit)
                        continue;
                }

                foreach (Match match in rule.Pattern.Matches(text))
                {
                    Group g = match.Groups[rule.WrongSpanGroup];
                    if (!g.Success) continue;

                    string raw = g.Value;
                    string replacement = rule.Replacement;

                    if (string.IsNullOrEmpty(replacement))
                    {
                        replacement = ComputeDynamicReplacement(raw);
                    }

                    if (raw == replacement)
                        continue;

                    string matchedSpan = match.Value.Trim();
                    string reasoning = !string.Equals(matchedSpan, raw, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(matchedSpan)
                        ? $"ရှေ့/နောက် ဆက်စပ်ပုဒ် '{matchedSpan}' အရ ဤနေရာတွင် '{raw}' အစား အနက်မှန် '{replacement}' ({rule.MeaningCue}) ကို သုံးနှုန်းရမည်။"
                        : $"သတ်ပုံကျမ်း အနက်ခွဲထားစနစ်အရ '{raw}' အစား '{replacement}' ({rule.MeaningCue}) ဟု ရေးသားရမည်။";

                    var suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate
                        {
                            Replacement = replacement,
                            MeaningCue = rule.MeaningCue,
                            IsOfficialThatpon = true,
                            SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(replacement),
                            DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(raw, replacement),
                            Score = 100.0
                        }
                    };

                    if (raw == "စိုးချင်စိုး")
                    {
                        suggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "စိုချင်စို",
                            MeaningCue = "စို (မိုးရေ~စွတ်သည်)",
                            IsOfficialThatpon = true,
                            SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("စိုချင်စို"),
                            DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(raw, "စိုချင်စို"),
                            Score = 88.0
                        });
                    }

                    AddNonOverlapping(issues, new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = raw,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = rule.Explanation,
                        GrammaticalReasoning = reasoning,
                        AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား နှင့် တွဲဖက်အသုံး)",
                        Suggestions = suggestions
                    });
                }
            }

            return issues;
        }

        public List<ProofingIssue> AnalyzePostViterbiContextAndBigrams(
            string text,
            MyanmarTrie? trie,
            List<ProofingIssue> existingIssues,
            List<SyllableToken>? precomputedTokens = null,
            int[]? precomputedLongestMatchLengths = null)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text) || trie == null || (trie.ContextualRuleCount == 0 && trie.BigramCount == 0))
                return issues;

            // 2. Word-level tokenization for Data-Driven ContextualRules & Statistical Bigram Homophone Disambiguation
            List<ClauseWordToken> wordTokens = TokenizeIntoClauseWords(text, trie, precomputedTokens, precomputedLongestMatchLengths);
            if (wordTokens.Count == 0)
                return issues;

            // 2a. Data-Driven ContextualRules from mlc_thatpon.db (collocations, aukmyit/visarga, medial context rules)
            if (trie.ContextualRuleCount > 0)
            {
                for (int i = 0; i < wordTokens.Count; i++)
                {
                    ClauseWordToken tok = wordTokens[i];
                    if (OverlapsExisting(existingIssues, tok.StartIndex, tok.Length) ||
                        OverlapsExisting(issues, tok.StartIndex, tok.Length))
                    {
                        continue;
                    }

                    // Do not apply 1-syllable word-level context rules to an interior syllable glued inside an unspaced lexical compound,
                    // EXCEPT when the right neighbor is a post-verbal suffix/particle (e.g. 'အလွန်ချည်နေပါသည်', 'ဈေးလျော့ပေးသည်')
                    if (tok.SyllableCount == 1 &&
                        i > 0 && wordTokens[i - 1].EndIndex == tok.StartIndex &&
                        i + 1 < wordTokens.Count && tok.EndIndex == wordTokens[i + 1].StartIndex &&
                        !ProtectedFunctionWords.Contains(wordTokens[i + 1].NormText) &&
                        !GenericSyntacticParticles.Contains(wordTokens[i - 1].NormText))
                    {
                        continue;
                    }

                    if (!trie.TryGetContextualRules(tok.NormText, out var ctxRules) || ctxRules == null)
                        continue;

                    for (int r = 0; r < ctxRules.Count; r++)
                    {
                        ContextualRule cr = ctxRules[r];
                        if (MatchesContextTrigger(text, wordTokens, i, cr, out string matchedEvidence))
                        {
                            trie.TryGetExactEntries(cr.CorrectForm, out var targetEntries);
                            string cue = targetEntries != null && targetEntries.Count > 0 && !string.IsNullOrWhiteSpace(targetEntries[0].MeaningCue)
                                ? targetEntries[0].MeaningCue
                                : "ရှေ့နောက်စကားစပ်အရ စာလုံးပေါင်းအမှန်";
                            string msg = !string.IsNullOrWhiteSpace(cr.Explanation)
                                ? cr.Explanation
                                : (cr.IsWarningOnly
                                    ? $"အနက်သံသယ စိစစ်ရန် — ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{cr.CorrectForm}' ဖြစ်နိုင်ပါသည်။"
                                    : $"ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{cr.CorrectForm}' ဟု ရေးသားရမည်။");

                            AddNonOverlapping(issues, new ProofingIssue
                            {
                                StartIndex = tok.StartIndex,
                                Length = tok.Length,
                                OriginalText = tok.RawText,
                                IssueType = cr.IsWarningOnly
                                    ? ProofingIssueType.ContextualMeaningWarning
                                    : ProofingIssueType.SpellingError,
                                Message = msg,
                                GrammaticalReasoning = cr.IsWarningOnly
                                    ? $"အနက်အဓိပ္ပာယ်နှင့် တွဲဖက်အသုံး သတိပြုချက် — ဝါကျအတွင်း '{matchedEvidence}' နှင့် တွဲဖက်ရာ၌ '{cr.CorrectForm}' [{cue}] ကို အသုံးများပါသည်။ ရည်ရွယ်ရင်းအနက်နှင့် တိုက်ဆိုင်စိစစ်ပါ။"
                                    : $"ဝါကျအတွင်း အနီးအနားရှိ သက်သေပုဒ် '{matchedEvidence}' နှင့် တွဲဖက်အနက် (Collocation) အရ '{tok.RawText}' အစား '{cr.CorrectForm}' [{cue}] ဖြစ်ရမည်။",
                                AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား နှင့် တွဲဖက်စကားစပ်စံနှုန်း",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = cr.CorrectForm,
                                        MeaningCue = cue,
                                        IsOfficialThatpon = true,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(cr.CorrectForm),
                                        DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(tok.RawText, cr.CorrectForm),
                                        Score = cr.IsWarningOnly ? 94.0 : 100.0
                                    }
                                }
                            });
                            break;
                        }
                    }
                }
            }

            // 2b. Two-Tier Content-Bigram Real-Word Homophone Disambiguation (Immediate + Particle-Skipped SOV Neighbors)
            if (trie.BigramCount > 0)
            {
                for (int i = 0; i < wordTokens.Count; i++)
                {
                    ClauseWordToken tok = wordTokens[i];
                    if (tok.SyllableCount > 2 ||
                        ProtectedFunctionWords.Contains(tok.NormText) ||
                        OverlapsExisting(existingIssues, tok.StartIndex, tok.Length) ||
                        OverlapsExisting(issues, tok.StartIndex, tok.Length) ||
                        !trie.ContainsExactWord(tok.NormText))
                    {
                        continue;
                    }

                    int wFreq = trie.GetWordFrequency(tok.NormText);
                    if (wFreq >= 550)
                        continue;

                    bool hasLeftPunct = i > 0 && wordTokens[i - 1].ClauseId == tok.ClauseId &&
                        HasInterveningNonSpacePunctuation(text, wordTokens[i - 1], tok);
                    bool hasRightPunct = i + 1 < wordTokens.Count && wordTokens[i + 1].ClauseId == tok.ClauseId &&
                        HasInterveningNonSpacePunctuation(text, tok, wordTokens[i + 1]);

                    string prevW = (i > 0 && wordTokens[i - 1].ClauseId == tok.ClauseId && !hasLeftPunct)
                        ? wordTokens[i - 1].NormText
                        : string.Empty;
                    string nextW = (i + 1 < wordTokens.Count && wordTokens[i + 1].ClauseId == tok.ClauseId && !hasRightPunct)
                        ? wordTokens[i + 1].NormText
                        : string.Empty;

                    // Never statistically replace a syllable/word that forms an exact dictionary compound with its immediate neighbor (e.g. 'အက်' + 'ဥပဒေ' = 'အက်ဥပဒေ')
                    if ((!string.IsNullOrEmpty(prevW) && trie.ContainsExactWord(prevW + tok.NormText)) ||
                        (!string.IsNullOrEmpty(nextW) && trie.ContainsExactWord(tok.NormText + nextW)))
                    {
                        continue;
                    }

                    // Particle-skipping SOV neighbor lookup (e.g. 'ကြိုး + ကို + ချဉ်' -> contentPrev = 'ကြိုး')
                    string contentPrevW = prevW;
                    if (!string.IsNullOrEmpty(prevW) && GenericSyntacticParticles.Contains(prevW) &&
                        i >= 2 && wordTokens[i - 2].ClauseId == tok.ClauseId &&
                        !HasInterveningNonSpacePunctuation(text, wordTokens[i - 2], wordTokens[i - 1]) &&
                        !GenericSyntacticParticles.Contains(wordTokens[i - 2].NormText))
                    {
                        contentPrevW = wordTokens[i - 2].NormText;
                    }

                    string contentNextW = nextW;
                    if (!string.IsNullOrEmpty(nextW) && GenericSyntacticParticles.Contains(nextW) &&
                        i + 2 < wordTokens.Count && wordTokens[i + 2].ClauseId == tok.ClauseId &&
                        !HasInterveningNonSpacePunctuation(text, wordTokens[i + 1], wordTokens[i + 2]) &&
                        !GenericSyntacticParticles.Contains(wordTokens[i + 2].NormText))
                    {
                        contentNextW = wordTokens[i + 2].NormText;
                    }

                    bool canUseLeft = !string.IsNullOrEmpty(contentPrevW) && !GenericSyntacticParticles.Contains(contentPrevW);
                    bool canUseRight = !string.IsNullOrEmpty(contentNextW) && !GenericSyntacticParticles.Contains(contentNextW);
                    if (!canUseLeft && !canUseRight)
                        continue;

                    // Only consider words that have ZERO empirical bigram support on both immediate AND particle-skipped content neighbors
                    if ((!string.IsNullOrEmpty(prevW) && trie.GetBigramCount(prevW, tok.NormText) > 0) ||
                        (!string.IsNullOrEmpty(nextW) && trie.GetBigramCount(tok.NormText, nextW) > 0) ||
                        (canUseLeft && trie.GetBigramCount(contentPrevW, tok.NormText) > 0) ||
                        (canUseRight && trie.GetBigramCount(tok.NormText, contentNextW) > 0))
                    {
                        continue;
                    }

                    bool usedParticleSkip = (contentPrevW != prevW && canUseLeft) || (contentNextW != nextW && canUseRight);
                    bool isHighFreqRealWord = wFreq >= 180;
                    bool isMediumFreqRealWord = wFreq >= 12;
                    int minCandFreq = isHighFreqRealWord
                        ? Math.Max(600, wFreq * 5)
                        : (isMediumFreqRealWord ? Math.Max(220, wFreq * 8) : Math.Max(120, wFreq * 15));

                    // Strict homophones only (never cross consonant voicing/aspiration like ဘ->ပ or က->ခ in statistical bigram mode!)
                    var candidates = trie.LookupStrictHomophones(tok.NormText);
                    if (candidates.Count == 0)
                        continue;

                    DictionaryEntry? bestCand = null;
                    int bestBgSupport = 0;
                    bool tokHasHahto = tok.NormText.IndexOf('\u103E') >= 0;
                    var seenWords = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var cand in candidates)
                    {
                        if (!cand.IsOfficialThatpon || cand.Frequency < minCandFreq || cand.Word == tok.NormText ||
                            ProtectedFunctionWords.Contains(cand.Word))
                            continue;

                        // Preserve aspiration 'ှ' so non-homophones like 'လေ' vs 'လှေ' or 'မာ' vs 'မှာ' are never swapped statistically
                        if (tokHasHahto != (cand.Word.IndexOf('\u103E') >= 0) || !seenWords.Add(cand.Word))
                            continue;

                        int candSylCount = cand.SyllableCount > 0
                            ? cand.SyllableCount
                            : MyanmarSyllableBreaker.BreakIntoSyllables(cand.Word, isPreNormalized: true).Count;
                        if (candSylCount != tok.SyllableCount)
                            continue;

                        int leftBg = canUseLeft ? trie.GetBigramCount(contentPrevW, cand.Word) : 0;
                        int rightBg = canUseRight ? trie.GetBigramCount(cand.Word, contentNextW) : 0;
                        int totalBg = leftBg + rightBg;

                        bool hasStrongBigramProof = isHighFreqRealWord
                            ? ((leftBg >= 85 && rightBg >= 85) || totalBg >= 680)
                            : (isMediumFreqRealWord
                                ? ((leftBg >= 40 && rightBg >= 40) || totalBg >= 400)
                                : (totalBg >= 120));
                        if (!hasStrongBigramProof || totalBg <= bestBgSupport)
                            continue;

                        double dist = SuggestionEngine.ComputeWeightedMyanmarDistance(tok.NormText, cand.Word, 0.28);
                        if (dist > 0.28)
                            continue;

                        bestBgSupport = totalBg;
                        bestCand = cand;
                    }

                    if (bestCand != null)
                    {
                        // Tier-2 Amber ContextualMeaningWarning when the original word is itself an established real word (wFreq >= 45)
                        // or when bigram evidence came across an intervening particle; Tier-1 Red SpellingError when wFreq < 45 & adjacent!
                        bool emitAsContextWarning = wFreq >= 45 || usedParticleSkip;
                        string cue = !string.IsNullOrWhiteSpace(bestCand.MeaningCue) ? bestCand.MeaningCue : "စကားစပ်အရ စာလုံးပေါင်းအမှန်";
                        string neighborNote = canUseLeft && trie.GetBigramCount(contentPrevW, bestCand.Word) > 0
                            ? $"ရှေ့ပုဒ် '{contentPrevW}' နှင့် '{bestCand.Word}' တွဲဖက်အသုံး"
                            : $"နောက်ပုဒ် '{contentNextW}' နှင့် '{bestCand.Word}' တွဲဖက်အသုံး";
                        AddNonOverlapping(issues, new ProofingIssue
                        {
                            StartIndex = tok.StartIndex,
                            Length = tok.Length,
                            OriginalText = tok.RawText,
                            IssueType = emitAsContextWarning
                                ? ProofingIssueType.ContextualMeaningWarning
                                : ProofingIssueType.SpellingError,
                            Message = emitAsContextWarning
                                ? $"အနက်သံသယ စိစစ်ရန် — ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{bestCand.Word}' [{cue}] ဖြစ်နိုင်ပါသည်။"
                                : $"ရှေ့နောက်စကားစပ်အရ '{tok.RawText}' အစား '{bestCand.Word}' [{cue}] ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = $"မြန်မာစာ ဝါကျသက်သေအချက်အလက်အရ '{tok.RawText}' သည် ဤနေရာ၌ တွဲဖက်မှုမရှိဘဲ {neighborNote} ({bestBgSupport:N0}+ ကြိမ်) ဖြင့်သာ အနက်ကိုက်ညီပါသည်။",
                            AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာဝါကျတွဲဖက်သက်သေပြုကျမ်း (Bigram Corpus)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = bestCand.Word,
                                    MeaningCue = cue,
                                    PosTag = bestCand.PosTag,
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(bestCand.Word),
                                    DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(tok.RawText, bestCand.Word),
                                    Score = emitAsContextWarning ? 93.0 : 99.0
                                }
                            }
                        });
                    }
                }
            }

            return issues;
        }

        private static bool HasInterveningNonSpacePunctuation(string text, ClauseWordToken left, ClauseWordToken right)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            int start = Math.Max(0, left.StartIndex + left.Length);
            int end = Math.Min(text.Length, right.StartIndex);
            for (int k = start; k < end; k++)
            {
                char ch = text[k];
                if (!char.IsWhiteSpace(ch) && ch != '\u200B' && ch != '\u200C' && ch != '\u200D' && ch != '\uFEFF')
                {
                    return true;
                }
            }
            return false;
        }

        private static List<ClauseWordToken> TokenizeIntoClauseWords(
            string text,
            MyanmarTrie trie,
            List<SyllableToken>? precomputedTokens = null,
            int[]? precomputedLongestMatchLengths = null)
        {
            var result = new List<ClauseWordToken>();
            List<SyllableToken> sylTokens = precomputedTokens ?? MyanmarSyllableBreaker.Tokenize(text);
            int clauseId = 0;
            int idx = 0;

            while (idx < sylTokens.Count)
            {
                SyllableToken st = sylTokens[idx];
                if (st.Kind != TokenKind.MyanmarSyllable)
                {
                    if (st.Text.IndexOf('\u104B') >= 0 || st.Text.IndexOf('\n') >= 0 ||
                        st.Text.IndexOf('?') >= 0 || st.Text.IndexOf('!') >= 0)
                    {
                        clauseId++;
                    }
                    idx++;
                    continue;
                }

                // 1. Check longest exact dictionary span so valid compounds (e.g. လက်ဖက်ရည်, ကျန်းမာရေး, အမိန့်ကြော်ငြာစာ) stay intact
                int matchedLen;
                if (precomputedLongestMatchLengths != null && idx < precomputedLongestMatchLengths.Length)
                {
                    matchedLen = precomputedLongestMatchLengths[idx];
                }
                else
                {
                    trie.MatchLongestAt(sylTokens, idx, out matchedLen, out _, out _);
                }
                if (matchedLen >= 2 && matchedLen <= 8)
                {
                    SyllableToken endTok = sylTokens[idx + matchedLen - 1];
                    int charLen = (endTok.StartIndex + endTok.Length) - st.StartIndex;
                    string raw = text.Substring(st.StartIndex, charLen);
                    bool allPreNormAndContiguous = true;
                    int sumLen = 0;
                    for (int k = 0; k < matchedLen; k++)
                    {
                        SyllableToken tk = sylTokens[idx + k];
                        sumLen += tk.Length;
                        if (!ReferenceEquals(tk.Text, tk.NormalizedText) && tk.Text != tk.NormalizedText)
                        {
                            allPreNormAndContiguous = false;
                            break;
                        }
                    }
                    string norm = (allPreNormAndContiguous && sumLen == charLen)
                        ? raw
                        : MyanmarProofingEngine.ConcatNormalizedTokens(sylTokens, idx, matchedLen);
                    result.Add(new ClauseWordToken
                    {
                        StartIndex = st.StartIndex,
                        Length = charLen,
                        ClauseId = clauseId,
                        SyllableCount = matchedLen,
                        RawText = raw,
                        NormText = norm
                    });
                    idx += matchedLen;
                    continue;
                }

                // 2. Check if a multi-syllable ContextualRule WrongForm (2..3 contiguous syllables) starts here
                int ctxMatchedLen = 0;
                string? matchedCtxNorm = null;
                if (trie.ContextualRuleCount > 0 && trie.IsContextualMultiSyllableStart(st.NormalizedText))
                {
                    int maxCheck = Math.Min(3, sylTokens.Count - idx);
                    for (int len = maxCheck; len >= 2; len--)
                    {
                        bool contiguous = true;
                        for (int k = 0; k < len; k++)
                        {
                            if (sylTokens[idx + k].Kind != TokenKind.MyanmarSyllable) { contiguous = false; break; }
                            if (k > 0 && sylTokens[idx + k].StartIndex != sylTokens[idx + k - 1].StartIndex + sylTokens[idx + k - 1].Length)
                            {
                                contiguous = false;
                                break;
                            }
                        }
                        if (!contiguous) continue;

                        string candNorm = MyanmarProofingEngine.ConcatNormalizedTokens(sylTokens, idx, len);
                        if (trie.TryGetContextualRulesPreNormalized(candNorm, out _))
                        {
                            ctxMatchedLen = len;
                            matchedCtxNorm = candNorm;
                            break;
                        }
                    }
                }

                if (ctxMatchedLen >= 2)
                {
                    SyllableToken endTok = sylTokens[idx + ctxMatchedLen - 1];
                    int charLen = (endTok.StartIndex + endTok.Length) - st.StartIndex;
                    result.Add(new ClauseWordToken
                    {
                        StartIndex = st.StartIndex,
                        Length = charLen,
                        ClauseId = clauseId,
                        SyllableCount = ctxMatchedLen,
                        RawText = text.Substring(st.StartIndex, charLen),
                        NormText = matchedCtxNorm!
                    });
                    idx += ctxMatchedLen;
                    continue;
                }

                // 3. Single syllable fallback
                result.Add(new ClauseWordToken
                {
                    StartIndex = st.StartIndex,
                    Length = st.Length,
                    ClauseId = clauseId,
                    SyllableCount = 1,
                    RawText = st.Text,
                    NormText = st.NormalizedText
                });
                idx++;
            }

            return result;
        }

        private static bool MatchesContextTrigger(string text, List<ClauseWordToken> tokens, int targetIdx, ContextualRule rule, out string matchedEvidence)
        {
            matchedEvidence = string.Empty;
            ClauseWordToken target = tokens[targetIdx];
            string[] triggers = rule.ParsedTriggers != null && rule.ParsedTriggers.Length > 0
                ? rule.ParsedTriggers
                : (rule.TriggerWords ?? string.Empty).Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (triggers.Length == 0)
                return false;

            string[] antiTriggers = rule.ParsedAntiTriggers != null && rule.ParsedAntiTriggers.Length > 0
                ? rule.ParsedAntiTriggers
                : (!string.IsNullOrWhiteSpace(rule.AntiTriggerWords)
                    ? rule.AntiTriggerWords.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>());

            if (antiTriggers.Length > 0)
            {
                for (int k = targetIdx - 1; k >= 0 && tokens[k].ClauseId == target.ClauseId; k--)
                {
                    if (TokenMatchesAnyTrigger(tokens[k].NormText, antiTriggers))
                        return false;
                }
                for (int k = targetIdx + 1; k < tokens.Count && tokens[k].ClauseId == target.ClauseId; k++)
                {
                    if (TokenMatchesAnyTrigger(tokens[k].NormText, antiTriggers))
                        return false;
                }
            }

            string dir = rule.Direction ?? "Both";
            int win = Math.Max(1, rule.Window);
            bool skipParticles = rule.SkipParticles;

            if (string.Equals(dir, "LeftSuffix", StringComparison.OrdinalIgnoreCase))
            {
                if (targetIdx == 0)
                    return false;
                ClauseWordToken prev = tokens[targetIdx - 1];
                if (prev.ClauseId != target.ClauseId || prev.EndIndex != target.StartIndex)
                    return false;
                for (int t = 0; t < triggers.Length; t++)
                {
                    if (prev.NormText.EndsWith(triggers[t], StringComparison.Ordinal))
                    {
                        matchedEvidence = prev.RawText;
                        return true;
                    }
                }
                return false;
            }

            bool checkLeft = string.Equals(dir, "Left", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(dir, "Both", StringComparison.OrdinalIgnoreCase);
            bool checkRight = string.Equals(dir, "Right", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(dir, "Both", StringComparison.OrdinalIgnoreCase);

            if (checkLeft)
            {
                int steps = 0;
                for (int j = targetIdx - 1; j >= 0 && steps < win; j--)
                {
                    if (tokens[j].ClauseId != target.ClauseId)
                        break;
                    if (win == 1 && !skipParticles && HasInterveningNonSpacePunctuation(text, tokens[j], target))
                        break;
                    string candNorm = tokens[j].NormText;
                    if (skipParticles && GenericSyntacticParticles.Contains(candNorm))
                        continue;
                    steps++;
                    if (TokenMatchesAnyTrigger(candNorm, triggers))
                    {
                        matchedEvidence = tokens[j].RawText;
                        return true;
                    }
                }
            }

            if (checkRight)
            {
                int steps = 0;
                for (int j = targetIdx + 1; j < tokens.Count && steps < win; j++)
                {
                    if (tokens[j].ClauseId != target.ClauseId)
                        break;
                    if (win == 1 && !skipParticles && HasInterveningNonSpacePunctuation(text, target, tokens[j]))
                        break;
                    string candNorm = tokens[j].NormText;
                    if (skipParticles && GenericSyntacticParticles.Contains(candNorm))
                        continue;
                    steps++;
                    if (TokenMatchesAnyTrigger(candNorm, triggers))
                    {
                        matchedEvidence = tokens[j].RawText;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TokenMatchesAnyTrigger(string tokenNorm, string[] triggers)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                string trig = triggers[i];
                if (tokenNorm == trig)
                    return true;

                // Allow genuine multi-syllable triggers (>= 2 syllables) to match compound heads/tails
                // (e.g., ကုန်ဈေးနှုန်း ends with ဈေးနှုန်း), never 1-syllable triggers like 'ဆန်း' inside 'အောင်ဆန်း'
                if (trig.Length >= 4 &&
                    MyanmarSyllableBreaker.CountMyanmarSyllables(trig) >= 2 &&
                    (tokenNorm.StartsWith(trig, StringComparison.Ordinal) || tokenNorm.EndsWith(trig, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool OverlapsExisting(List<ProofingIssue> issues, int start, int length)
        {
            int end = start + length;
            for (int i = 0; i < issues.Count; i++)
            {
                int iEnd = issues[i].StartIndex + issues[i].Length;
                if (start < iEnd && end > issues[i].StartIndex)
                    return true;
            }
            return false;
        }

        private static void AddNonOverlapping(List<ProofingIssue> issues, ProofingIssue candidate)
        {
            if (!OverlapsExisting(issues, candidate.StartIndex, candidate.Length))
            {
                issues.Add(candidate);
            }
        }

        private static string ComputeDynamicReplacement(string raw)
        {
            if (raw == "ဈေးလျော့ပေး")
                return "ဈေးလျှော့ပေး";
            if (raw.StartsWith("လျော့", StringComparison.Ordinal))
                return "လျှော့" + raw.Substring("လျော့".Length);
            if (raw.StartsWith("လျှော့", StringComparison.Ordinal))
                return "လျော့" + raw.Substring("လျှော့".Length);
            if (raw.StartsWith("ပြည်", StringComparison.Ordinal) && raw.Length > "ပြည်".Length)
                return "ပြည့်" + raw.Substring("ပြည်".Length);
            if (raw == "ဝက်ဆင်" || raw == "ဝက်စုံ")
                return "ဝတ်" + raw.Substring("ဝက်".Length);
            if (raw == "အကြောင်းအကြိုး")
                return "အကြောင်းအကျိုး";
            if (raw.StartsWith("အကြိုး", StringComparison.Ordinal))
                return "အကျိုး" + raw.Substring("အကြိုး".Length);
            if (raw == "ကျိုးစား" || raw == "ကျိုးပမ်း" || raw == "ကျိုးကုတ်")
                return "ကြိုး" + raw.Substring("ကျိုး".Length);
            if (raw.StartsWith("မြက်", StringComparison.Ordinal) && raw.Length > "မြက်".Length)
                return "မျက်" + raw.Substring("မြက်".Length);
            if (raw.StartsWith("ဖျည့်", StringComparison.Ordinal))
                return "ဖြည့်" + raw.Substring("ဖျည့်".Length);
            if (raw == "အဖျေ")
                return "အဖြေ";
            if (raw.StartsWith("ဖျေ", StringComparison.Ordinal))
                return "ဖြေ" + raw.Substring("ဖျေ".Length);
            if (raw == "သတ်သာ" || raw == "သတ်ရောက်")
                return "သက်" + raw.Substring("သတ်".Length);
            if (raw == "ရက်တန့်" || raw == "ရက်ကွက်" || raw == "ရက်တည်")
                return "ရပ်" + raw.Substring("ရက်".Length);
            if (raw == "ကြယ်ဝန်း" || raw == "ကြယ်ပြန့်" || raw == "ကြယ်ပြော")
                return "ကျယ်" + raw.Substring("ကြယ်".Length);
            if (raw == "ပျံလည်" || raw == "ပျံကြားရေး")
                return "ပြန်" + raw.Substring("ပျံ".Length);
            if (raw == "ပြန်သန်း" || raw == "ပြန်ဝဲ")
                return "ပျံ" + raw.Substring("ပြန်".Length);
            if (raw.EndsWith("ခမ်း", StringComparison.Ordinal) && raw.Length > "ခမ်း".Length)
                return raw.Substring(0, raw.Length - "ခမ်း".Length) + "ခန်း";
            if (raw == "တွေ့ခေါ်" || raw == "တွေ့တော")
                return "တွေး" + raw.Substring("တွေ့".Length);
            if (raw == "တွေးဆုံ" || raw == "တွေးရှိ" || raw == "တွေးကြုံ")
                return "တွေ့" + raw.Substring("တွေး".Length);

            if (raw == "ကျွေးမြီ")
                return "ကြွေးမြီ";
            if (raw == "အကျွေး")
                return "အကြွေး";
            if (raw.EndsWith("မှီ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "မီ";
            if (raw == "ကိုင်းကျွန်းမီ")
                return "ကိုင်းကျွန်းမှီ";
            if (raw == "ကျွန်းကိုင်းမီ")
                return "ကျွန်းကိုင်းမှီ";
            if (raw == "စိုစို")
                return "ဆိုဆို";
            if (raw == "စို")
                return "ဆို";
            if (raw.StartsWith("မီ", StringComparison.Ordinal))
                return "မှီ" + raw.Substring(2);
            if (raw.EndsWith("ယဉ်", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "ယာဉ်";
            if (raw.StartsWith("ယဉ်", StringComparison.Ordinal))
                return "ယာဉ်" + raw.Substring(3);
            if (raw.StartsWith("ယာဉ်", StringComparison.Ordinal))
                return "ယဉ်" + raw.Substring(4);
            if (raw.Contains("ကြော်ငြာ"))
                return raw.Replace("ကြော်ငြာ", "ကြေညာ");
            if (raw == "စာဖက်")
                return "စာဖတ်";
            if (raw.StartsWith("ဖက်", StringComparison.Ordinal))
                return "ဖတ်" + raw.Substring(3);
            if (raw.EndsWith("ဖက်", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "ဘက်";
            if (raw.EndsWith("ကြမ်း", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 5) + "ကျမ်း";
            if (raw.StartsWith("ကျီး", StringComparison.Ordinal))
                return "ကြီး" + raw.Substring(4);
            if (raw.StartsWith("ကြောက်", StringComparison.Ordinal))
                return "ကျောက်" + raw.Substring(6);
            if (raw.StartsWith("ကျောက်", StringComparison.Ordinal))
                return "ကြောက်" + raw.Substring(6);
            if (raw.StartsWith("ကန်", StringComparison.Ordinal))
                return "ကံ" + raw.Substring(3);
            if (raw.StartsWith("လန်း", StringComparison.Ordinal))
                return "လမ်း" + raw.Substring(4);
            if (raw.EndsWith("သက်ပုံ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 6) + "သတ်ပုံ";
            if (raw.StartsWith("အသတ်", StringComparison.Ordinal))
                return "အသက်" + raw.Substring(4);
            if (raw.StartsWith("သတ်သေ", StringComparison.Ordinal))
                return "သက်သေ" + raw.Substring(5);
            if (raw.StartsWith("အားလက်", StringComparison.Ordinal))
                return "အားလပ်" + raw.Substring(6);
            if (raw.Contains("ဒါဏ်"))
                return raw.Replace("ဒါဏ်", "ဒဏ်");
            if (raw.StartsWith("ပိုဒ်မ", StringComparison.Ordinal))
                return "ပုဒ်မ" + raw.Substring(6);
            if (raw.StartsWith("အပုဒ်", StringComparison.Ordinal))
                return "အပိုဒ်" + raw.Substring(5);
            if (raw.StartsWith("စရာ", StringComparison.Ordinal))
                return "ဆရာ" + raw.Substring(3);
            if (raw.Contains("နဲ"))
                return raw.Replace("နဲ", "နည်း");
            if (raw.EndsWith("ကြ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 2) + "ကျ";
            if (raw.StartsWith("ဖူး", StringComparison.Ordinal) || raw.StartsWith("ဗူး", StringComparison.Ordinal))
                return "ဘူး" + raw.Substring(3);
            if (raw.EndsWith("ဖူး", StringComparison.Ordinal) || raw.EndsWith("ဗူး", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "ဘူး";
            if (raw.Contains("စိုး"))
                return raw.Replace("စိုး", "ဆိုး");
            if (raw.Contains("ဆိုး"))
                return raw.Replace("ဆိုး", "စိုး");
            if (raw.Contains("ဒီဂရီ"))
                return raw.Replace("ဒီဂရီ", "ဒီကရီ");
            if (raw.Contains("ဒီကရီ"))
                return raw.Replace("ဒီကရီ", "ဒီဂရီ");

            return raw;
        }
    }
}
