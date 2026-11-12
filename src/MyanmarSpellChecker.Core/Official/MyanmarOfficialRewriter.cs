using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Official
{
    public sealed class OfficialRewriteResult
    {
        public string OriginalText { get; set; } = string.Empty;
        public string RewrittenText { get; set; } = string.Empty;
        public int ReplacementCount { get; set; }
        public bool HasChanges => ReplacementCount > 0;
        public List<string> AppliedChanges { get; set; } = new List<string>();
    }

    /// <summary>
    /// Deterministic 100%-offline Colloquial Burmese (အပြောစကားဟန်) to Formal/Official Literary Prose (ရုံးသုံး/စာပေဟန်) Rewriter.
    /// Preserves numbers, English terms, punctuation, and lexical roots containing homographic syllables (e.g. ဟိုတယ်, ချို့တဲ့, ခနော်ခနဲ့, မှာကြား).
    /// </summary>
    public static class MyanmarOfficialRewriter
    {
        private sealed class RewriteRule
        {
            public Regex Pattern { get; }
            public string Replacement { get; }
            public string Label { get; }

            public RewriteRule(string pattern, string replacement, string label)
            {
                Pattern = new Regex(UnicodeNormalizer.NormalizeCanonicalOrder(pattern), RegexOptions.Compiled);
                Replacement = UnicodeNormalizer.NormalizeCanonicalOrder(replacement);
                Label = label;
            }
        }

        private static readonly RewriteRule[] OrderedRules =
        {
            // 1. Discourse connectors & temporal demonstratives
            new RewriteRule(@"(?<![\u1000-\u103F])(?:ဒါပေမဲ့|ဒါပေမယ့်)(?![\u102B-\u103E])", "သို့ရာတွင်", "ဒါပေမဲ့ ➔ သို့ရာတွင်"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:ဒါကြောင့်မို့လို့|ဒါကြောင့်မို့|ဒါကြောင့်|အဲဒါကြောင့်|အဲ့ဒါကြောင့်)(?![\u102B-\u103E])", "ထို့ကြောင့်", "ဒါကြောင့် ➔ ထို့ကြောင့်"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:ဒါ့အပြင်|ဒါအပြင်)(?![\u102B-\u103E])", "ထို့အပြင်", "ဒါ့အပြင် ➔ ထို့အပြင်"),
            new RewriteRule(@"(?:^|(?<=[\u104A\u104B]\s*))ပြီးတော့(?=\s|,|[\u1000-\u102A])", "ထို့ပြင်", "ပြီးတော့ ➔ ထို့ပြင်"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:အဲဒီနောက်|အဲ့ဒီနောက်)(?![\u102B-\u103E])", "ထို့နောက်", "အဲဒီနောက် ➔ ထို့နောက်"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:အဲဒီလို|အဲ့ဒီလို|ဒီလိုမျိုး)(?![\u102B-\u103E])", "ထိုသို့", "အဲဒီလို ➔ ထိုသို့"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:အဲဒီ|အဲ့ဒီ|ဒီ)(?=\s*(?:လုပ်ငန်း|စီမံကိန်း|အစည်းအဝေး|ကိစ္စ|အဖွဲ့|ဌာန|ဥပဒေ|အစီအစဉ်|ပြဿနာ|စာတမ်း))", "ဤ", "ဒီ/အဲဒီ ➔ ဤ"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:အဲဒီ|အဲ့ဒီ)(?=\s*[\u1000-\u102A])", "အဆိုပါ", "အဲဒီ ➔ အဆိုပါ"),
            new RewriteRule(@"(?<![\u1000-\u103F])ဒီနေ့(?![\u102B-\u103E])", "ယနေ့", "ဒီနေ့ ➔ ယနေ့"),
            new RewriteRule(@"(?<![\u1000-\u103F])မနေ့က(?![\u102B-\u103E])", "ယမန်နေ့က", "မနေ့က ➔ ယမန်နေ့က"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:အခုလက်ရှိ|အခုချိန်)(?![\u102B-\u103E])", "ယခုလက်ရှိ", "အခုလက်ရှိ ➔ ယခုလက်ရှိ"),
            new RewriteRule(@"(?<![\u1000-\u103F])အခု(?=\s|တွင်|၌|မှာ|အချိန်|ကာလ)", "ယခု", "အခု ➔ ယခု"),
            new RewriteRule(@"(?<![\u1000-\u103F])(?:အရင်က|ဟိုတစ်နေ့က)(?![\u102B-\u103E])", "ယခင်က", "အရင်က ➔ ယခင်က"),

            // 2. Subordinate clauses, relative participles & quotatives
            new RewriteRule(@"(?<=[\u1000-\u103F])(?<!ဒါ|သို့)ပေ(?:မဲ့|မယ့်)(?=\s|$|[\u104A\u104B])", "သော်လည်း", "ပေမဲ့ ➔ သော်လည်း"),
            new RewriteRule(@"(?<=[\u1000-\u103F])တဲ့အခါ(?:မှာ)?", "သည့်အခါ", "တဲ့အခါ ➔ သည့်အခါ"),
            new RewriteRule(@"(?<=[\u1000-\u103F])တဲ့အတွက်", "သည့်အတွက်", "တဲ့အတွက် ➔ သည့်အတွက်"),
            new RewriteRule(@"(?<=[\u1000-\u103F])တဲ့အကြောင်း", "သည့်အကြောင်း", "တဲ့အကြောင်း ➔ သည့်အကြောင်း"),
            new RewriteRule(@"(?<=[\u1000-\u103F])တဲ့အတိုင်း", "သည့်အတိုင်း", "တဲ့အတိုင်း ➔ သည့်အတိုင်း"),
            new RewriteRule(@"(?<=[\u1000-\u103F])(?<!ချို့|နွမ်း|ယိုင်|ပြို)တဲ့(?=\s+[\u1000-\u102A])", "သည့်", "တဲ့ ➔ သည့်"),
            new RewriteRule(@"(?<=[\u1000-\u103F])(?<!ပေ)မယ့်(?=\s+[\u1000-\u102A]|အခါ|အချိန်|နေ့|နှစ်|လ)", "မည့်", "မယ့် ➔ မည့်"),
            new RewriteRule(@"လို့(?=\s*(?:ပြော|ဆို|သိရ|ယူဆ|ဖော်ပြ|တင်ပြ|ကြေညာ|မိန့်ကြား|ဖြေကြား|ညွှန်ကြား))", "ဟု", "လို့ ➔ ဟု"),

            // 3. Negative verb closers ('မ...ပါဘူး / မ...ဘူး -> မ...ပါ')
            new RewriteRule(@"မဟုတ်(?:ပါ)?ဘူး", "မဟုတ်ပါ", "မဟုတ်ဘူး ➔ မဟုတ်ပါ"),
            new RewriteRule(@"(?<![\u1000-\u103F])မ(?!နက်|နေ့|ဟာ|ကွေး|လေးရှား|ရမ်း)(?<Stem>[\u1000-\u103F]{1,16}?)ပါဘူး(?=\s*(?:[""”'’]\s*)?(?:[\u104A\u104B?]|$|ဟု|လို့))", "မ${Stem}ပါ", "မ~ပါဘူး ➔ မ~ပါ"),
            new RewriteRule(@"(?<![\u1000-\u103F])မ(?!နက်|နေ့|ဟာ|ကွေး|လေးရှား|ရမ်း)(?<Stem>[\u1000-\u103F]{1,16}?)(?<!ပါ)ဘူး(?=\s*(?:[""”'’]\s*)?(?:[\u104A\u104B?]|$|ဟု|လို့))", "မ${Stem}ပါ", "မ~ဘူး ➔ မ~ပါ"),

            // 4. Sentence-final verb particles & progressive aspect
            new RewriteRule(@"ပြီ(?:ကွာ|ကွ|နော်|ဗျာ|ဗျ|ရှင်|ရှင့်)(?=\s|$|[\u104A\u104B])", "ပါပြီ", "ပြီနော်/ဗျာ ➔ ပါပြီ"),
            new RewriteRule(@"(?<=[\u1000-\u103F])နေပါတယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", "လျက်ရှိပါသည်", "နေပါတယ် ➔ လျက်ရှိပါသည်"),
            new RewriteRule(@"ပါတယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", "ပါသည်", "ပါတယ် ➔ ပါသည်"),
            new RewriteRule(@"(?<=[\u1000-\u103F])(?<!တင့်|ဟို|မို|ပေါ်|ဒစ်ဂျစ်|ဒီဂျစ်|ခရစ်စ|ဟော်|ကယ်)တယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", "သည်", "တယ် ➔ သည်"),
            new RewriteRule(@"ပါမယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", "ပါမည်", "ပါမယ် ➔ ပါမည်"),
            new RewriteRule(@"လိမ့်မယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", "လိမ့်မည်", "လိမ့်မယ် ➔ လိမ့်မည်"),
            new RewriteRule(@"(?<=[\u1000-\u103F])(?<!အ|နှမ|မောင်|ပျို|လုံ|ရှမ်း|ဇင်း)မယ်(?=\s*(?:[""”'’]\s*)?(?:[\u104B?]|$|ဟု|လို့|ဆိုတာ|ဆိုသည်))", "မည်", "မယ် ➔ မည်"),

            // 5. Nominal plural, associative, possessive & locative postpositions
            new RewriteRule(@"(?<=[\u1000-\u103F])အတွက်ကို(?=\s|$|[\u104A\u104B])", "အတွက်", "အတွက်ကို ➔ အတွက်"),
            new RewriteRule(@"(?<=[\u1000-\u103F])တွေ(?=\s|$|[\u104A\u104B]|ကို|အား|မှ|သို့|နှင့်|နဲ့|သည်|မှာ|တွင်|က|ရဲ့|၏)", "များ", "တွေ ➔ များ"),
            new RewriteRule(@"(?<=[\u1000-\u103F]|\s)(?<!ချိ|ယိမ်း|လှုပ်|ပြို|ခ|ခနော်ခ|ခနိုးခ)နဲ့(?=\s|$|[\u104A\u104B]|အတူ|ပတ်သက်|စပ်လျဉ်း|အညီ|အမျှ)", "နှင့်", "နဲ့ ➔ နှင့်"),
            new RewriteRule(@"(?<=[\u1000-\u103F])ရဲ့(?=\s+[\u1000-\u102A])", "၏", "ရဲ့ ➔ ၏"),
            new RewriteRule(@"(?<=(?:နိုင်ငံ|မြို့နယ်|ခရိုင်|တိုင်းဒေသကြီး|ပြည်နယ်|မြို့|ရွာ|ကျေးရွာ|ဌာန|ရုံး|ကျောင်း|တက္ကသိုလ်|ဆေးရုံ|အစည်းအဝေး|အခမ်းအနား|နှစ်|လ|ပိုင်း|ချိန်|နေရာ|ဒေသ|ကာလ|အပေါ်|အောက်|အတွင်း|အပြင်|ရှေ့|နောက်|ကြား))မှာ(?=\s|[\u104A])", "တွင်", "မှာ ➔ တွင်")
        };

        public static OfficialRewriteResult ConvertToOfficialProse(string? input)
        {
            var res = new OfficialRewriteResult
            {
                OriginalText = input ?? string.Empty,
                RewrittenText = input ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(input))
                return res;

            string working = UnicodeNormalizer.NormalizeCanonicalOrder(input!);
            int totalReplacements = 0;
            var seenLabels = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < OrderedRules.Length; i++)
            {
                RewriteRule rule = OrderedRules[i];
                int ruleHits = 0;
                working = rule.Pattern.Replace(working, m =>
                {
                    ruleHits++;
                    return m.Result(rule.Replacement);
                });

                if (ruleHits > 0)
                {
                    totalReplacements += ruleHits;
                    if (seenLabels.Add(rule.Label))
                    {
                        res.AppliedChanges.Add(rule.Label);
                    }
                }
            }

            res.RewrittenText = working;
            res.ReplacementCount = totalReplacements;
            return res;
        }
    }
}
