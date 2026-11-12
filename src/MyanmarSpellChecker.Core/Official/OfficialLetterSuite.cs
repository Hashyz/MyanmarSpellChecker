using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MyanmarSpellChecker.Core.Official
{
    public enum OfficialTemplateKind
    {
        MinistryOfficialLetter = 0,
        GazetteNotification = 1,
        MeetingMinutes = 2
    }

    public sealed class OfficialCheckItem
    {
        public string CriterionName { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public bool IsAdvisory { get; set; }
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class OfficialStructureAuditResult
    {
        public bool LooksLikeOfficialDocument { get; set; }
        public int PassedCount { get; set; }
        public int TotalRequiredCount { get; set; }
        public List<OfficialCheckItem> Items { get; set; } = new List<OfficialCheckItem>();

        public string SummaryStatus =>
            TotalRequiredCount == 0
                ? "စစ်ဆေးပြီး"
                : $"{PassedCount}/{TotalRequiredCount} ချက် ကိုက်ညီသည်";
    }

    /// <summary>
    /// Generates authentic Myanmar Government & Enterprise official templates (ရုံးစာ, အမိန့်ကြော်ငြာစာ, အစည်းအဝေးမှတ်တမ်း)
    /// with live မြန်မာသက္ကရာဇ် + Gregorian dates, and validates ရုံးစာ structural compliance.
    /// </summary>
    public static class OfficialLetterSuite
    {
        private static readonly Regex RefNumberRegex = new Regex(
            @"(?:စာအမှတ်|အမိန့်ကြော်ငြာစာအမှတ်|အမိန့်အမှတ်|ညွှန်ကြားချက်အမှတ်|ထုတ်ပြန်ချက်အမှတ်|ကြော်ငြာချက်အမှတ်|ကြေညာချက်အမှတ်|တင်ဒါအမှတ်|အမှတ်စဉ်)\s*[၊။:-]?",
            RegexOptions.Compiled);

        private static readonly Regex DateHeaderRegex = new Regex(
            @"(?:ရက်စွဲ|ကျင်းပသည့်နေ့ရက်)\s*[၊။:-]?|[\u1041-\u1042][\u1040-\u1049]{3}\s*ခုနှစ်",
            RegexOptions.Compiled);

        private static readonly Regex MyanmarEraYearRegex = new Regex(
            @"၁၃[\u1040-\u1049]{2}\s*ခုနှစ်|(?:လဆန်း|လပြည့်ကျော်|လပြည့်နေ့|လကွယ်နေ့)",
            RegexOptions.Compiled);

        private static readonly Regex AddresseeOrTitleRegex = new Regex(
            @"(?:^|\r|\n)\s*(?:သို့|အမိန့်ကြော်ငြာစာ|အစည်းအဝေးမှတ်တမ်း|သတင်းထုတ်ပြန်ချက်|အသိပေးကြေညာချက်|ကြေညာချက်|ညွှန်ကြားချက်|အိတ်ဖွင့်တင်ဒါ)(?:\s|$|[၊။(])",
            RegexOptions.Compiled);

        private static readonly Regex SubjectLineRegex = new Regex(
            @"အကြောင်းအရာ\s*[။၊:-]",
            RegexOptions.Compiled);

        private static readonly Regex BurmeseNumberedParaRegex = new Regex(
            @"(?:^|\r|\n)\s*(?<Num>[\u1041-\u1049][\u1040-\u1049]*)\s*\u104B",
            RegexOptions.Compiled);

        private static readonly Regex AsciiNumberedParaRegex = new Regex(
            @"(?:^|\r|\n)\s*[1-9][0-9]*[.)]\s*[\u1000-\u1021]",
            RegexOptions.Compiled);

        private static readonly Regex ColloquialLeakRegex = new Regex(
            @"(?:ပါတယ်|ပါမယ်|သွားတယ်|လာတယ်|ဖြစ်တယ်|ရမယ်|မဟုတ်ဘူး|ဒါပေမဲ့|အဲဒါ)(?=\s|$|[\u104A\u104B])",
            RegexOptions.Compiled);

        private static readonly Regex DistributionClosingRegex = new Regex(
            @"(?:မိတ္တူကို|ဖြန့်ဝေခြင်း|အမိန့်အရ|\(ပုံ\)|တာဝန်ခံ|ညွှန်ကြားရေးမှူး|ဝန်ကြီး|ဥက္ကဋ္ဌ|အတွင်းရေးမှူး)",
            RegexOptions.Compiled);

        public static string BuildTemplateText(OfficialTemplateKind kind, DateTime? targetDate = null)
        {
            DateTime dt = targetDate ?? DateTime.Today;
            MyanmarDateInfo mmDate = MyanmarTraditionalCalendar.FromGregorian(dt);
            string gregYr = MyanmarTraditionalCalendar.ToMyanmarDigits(dt.Year);

            switch (kind)
            {
                case OfficialTemplateKind.GazetteNotification:
                    return BuildGazetteNotificationTemplate(mmDate, gregYr);

                case OfficialTemplateKind.MeetingMinutes:
                    return BuildMeetingMinutesTemplate(mmDate, gregYr);

                default:
                    return BuildMinistryOfficialLetterTemplate(mmDate, gregYr);
            }
        }

        private static string BuildMinistryOfficialLetterTemplate(MyanmarDateInfo mmDate, string gregYr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော်");
            sb.AppendLine("[ဝန်ကြီးဌာန / အဖွဲ့အစည်းအမည်]");
            sb.AppendLine("[ဦးစီးဌာန / ကုမ္ပဏီရုံးချုပ်အမည်]");
            sb.AppendLine($"စာအမှတ်၊ ၁၀၀/စီမံ-၁/{gregYr} (     )");
            sb.AppendLine($"ရက်စွဲ၊ {mmDate.OfficialGregorianDateText}");
            sb.AppendLine($"       ({mmDate.OfficialMyanmarDateText})");
            sb.AppendLine();
            sb.AppendLine("သို့");
            sb.AppendLine("    [ရာထူးအမည် / ဌာနအကြီးအကဲအမည်]");
            sb.AppendLine("    [ဦးစီးဌာန / အဖွဲ့အစည်းအမည်]");
            sb.AppendLine("    [မြို့အမည်]");
            sb.AppendLine();
            sb.AppendLine("အကြောင်းအရာ။  [အကြောင်းအရာ ခေါင်းစဉ်ကို ဤနေရာတွင် တိကျစွာ ရေးသားဖော်ပြရန်]");
            sb.AppendLine("ရည်ညွှန်းချက်။  (၁) [သက်ဆိုင်ရာ ဌာန] ၏ ရက်စွဲပါ စာအမှတ် [အမှတ်စဉ်]။");
            sb.AppendLine();
            sb.AppendLine("၁။  အထက်ပါအကြောင်းအရာနှင့် စပ်လျဉ်း၍ ရည်ညွှန်းပါစာဖြင့် အကြောင်းကြားချက်အရ လုပ်ငန်းဆောင်ရွက်ထားရှိမှု အခြေအနေများကို အောက်ပါအတိုင်း တင်ပြအပ်ပါသည်-");
            sb.AppendLine("    (က) [ပထမအချက် အသေးစိတ် ဖော်ပြချက်]၊");
            sb.AppendLine("    (ခ) [ဒုတိယအချက် အသေးစိတ် ဖော်ပြချက်]။");
            sb.AppendLine();
            sb.AppendLine("၂။  သို့ဖြစ်ပါ၍ လိုအပ်သလို ဆက်လက်ဆောင်ရွက်နိုင်ပါရန်နှင့် လမ်းညွှန်မှုပြုနိုင်ပါရန် လေးစားစွာ တင်ပြအပ်ပါသည်။");
            sb.AppendLine();
            sb.AppendLine("                                                    [လက်မှတ်နှင့် အမည်]");
            sb.AppendLine("                                                    [ရာထူးအမည်]");
            sb.AppendLine();
            sb.AppendLine("မိတ္တူကို-");
            sb.AppendLine("    - [သက်ဆိုင်ရာ ဌာနခွဲ / ရုံးခွဲအမည်]");
            sb.AppendLine("    - ရုံးလက်ခံ / မျှောစာတွဲ");
            return sb.ToString();
        }

        private static string BuildGazetteNotificationTemplate(MyanmarDateInfo mmDate, string gregYr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ပြည်ထောင်စုသမ္မတမြန်မာနိုင်ငံတော်အစိုးရ");
            sb.AppendLine("[ဝန်ကြီးဌာန / ပြည်ထောင်စုအဆင့် အဖွဲ့အစည်းအမည်]");
            sb.AppendLine($"အမိန့်ကြော်ငြာစာအမှတ် (     / {gregYr})");
            sb.AppendLine(mmDate.OfficialMyanmarDateText);
            sb.AppendLine($"({mmDate.OfficialGregorianDateText})");
            sb.AppendLine();
            sb.AppendLine("၁။  [ဝန်ကြီးဌာန / အဖွဲ့အစည်းအမည်] သည် သက်ဆိုင်ရာ ဥပဒေပုဒ်မအရ အပ်နှင်းထားသော လုပ်ပိုင်ခွင့်ကို ကျင့်သုံး၍ ဤအမိန့်ကြော်ငြာစာကို ထုတ်ပြန်လိုက်သည်။");
            sb.AppendLine();
            sb.AppendLine("၂။  လုပ်ငန်းတာဝန်များ စနစ်တကျ အကောင်အထည်ဖော် ဆောင်ရွက်နိုင်ရေးအတွက် အောက်ဖော်ပြပါ ပုဂ္ဂိုလ်များပါဝင်သော ကော်မတီ/အဖွဲ့ကို ဖွဲ့စည်းတာဝန်ပေးအပ်လိုက်သည်-");
            sb.AppendLine("    (က) [အမည်နှင့် ရာထူး]                        ဥက္ကဋ္ဌ");
            sb.AppendLine("    (ခ) [အမည်နှင့် ရာထူး]                        အဖွဲ့ဝင်");
            sb.AppendLine("    (ဂ) [အမည်နှင့် ရာထူး]                        အတွင်းရေးမှူး");
            sb.AppendLine();
            sb.AppendLine("၃။  ဤအမိန့်ကြော်ငြာစာသည် ထုတ်ပြန်သည့်နေ့မှစ၍ အကျိုးသက်ရောက်စေရမည်။");
            sb.AppendLine();
            sb.AppendLine("                                                    (ပုံ) [အမည်]");
            sb.AppendLine("                                                    ပြည်ထောင်စုဝန်ကြီး / ဥက္ကဋ္ဌ");
            sb.AppendLine();
            sb.AppendLine("ဖြန့်ဝေခြင်း-");
            sb.AppendLine("    - ပြည်ထောင်စုအစိုးရအဖွဲ့ရုံး");
            sb.AppendLine("    - မြန်မာနိုင်ငံပြန်တမ်းတွင် ထည့်သွင်းပုံနှိပ်ရန်");
            sb.AppendLine("    - ရုံးလက်ခံ");
            return sb.ToString();
        }

        private static string BuildMeetingMinutesTemplate(MyanmarDateInfo mmDate, string gregYr)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[ဝန်ကြီးဌာန / ကုမ္ပဏီ / အဖွဲ့အစည်းအမည်]");
            sb.AppendLine($"အစည်းအဝေးမှတ်တမ်း အမှတ်စဉ် (     / {gregYr})");
            sb.AppendLine();
            sb.AppendLine($"ကျင်းပသည့်နေ့ရက်။  {mmDate.OfficialGregorianDateText} ({mmDate.OfficialMyanmarDateText})၊ {mmDate.WeekdayBurmese}နေ့");
            sb.AppendLine("ကျင်းပသည့်အချိန်။  နံနက် ၀၉:၃၀ နာရီမှ ၁၁:၃၀ နာရီအထိ");
            sb.AppendLine("ကျင်းပသည့်နေရာ။  အစည်းအဝေးခန်းမ (၁)");
            sb.AppendLine("တက်ရောက်သူများ။  သဘာပတိ၊ ကော်မတီဝင်များနှင့် သက်ဆိုင်ရာ ဌာနတာဝန်ခံများ");
            sb.AppendLine();
            sb.AppendLine("၁။  သဘာပတိ၏ အဖွင့်အမှာစကား-");
            sb.AppendLine("    [သဘာပတိ၏ လမ်းညွှန်မှာကြားချက် အကျဉ်းချုပ်ကို ရေးသားဖော်ပြရန်]။");
            sb.AppendLine();
            sb.AppendLine("၂။  ဆွေးနွေးတင်ပြချက်များနှင့် ဆုံးဖြတ်ချက်များ-");
            sb.AppendLine("    (က) ဆုံးဖြတ်ချက် (၁) - [ဆုံးဖြတ်ချက်အကြောင်းအရာနှင့် တာဝန်ခံဆောင်ရွက်မည့်ဌာန]၊");
            sb.AppendLine("    (ခ) ဆုံးဖြတ်ချက် (၂) - [ဆုံးဖြတ်ချက်အကြောင်းအရာနှင့် ပြီးစီးရမည့် သတ်မှတ်ရက်]။");
            sb.AppendLine();
            sb.AppendLine("၃။  အစည်းအဝေးကို နံနက် ၁၁:၃၀ နာရီတွင် အောင်မြင်စွာ ရုပ်သိမ်းခဲ့ပါသည်။");
            sb.AppendLine();
            sb.AppendLine("                                                    မှတ်တမ်းပြုစုသူ - [အမည်နှင့် ရာထူး]");
            sb.AppendLine("                                                    အတည်ပြုသူ     - [အမည်နှင့် ရာထူး]");
            return sb.ToString();
        }

        public static OfficialStructureAuditResult ValidateOfficialLetterStructure(string? text)
        {
            var result = new OfficialStructureAuditResult();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            string body = text!;
            bool hasRef = RefNumberRegex.IsMatch(body);
            bool hasDate = DateHeaderRegex.IsMatch(body);
            bool hasMyanmarEra = MyanmarEraYearRegex.IsMatch(body);
            bool hasAddressee = AddresseeOrTitleRegex.IsMatch(body);
            bool hasSubject = SubjectLineRegex.IsMatch(body)
                || body.Contains("အမိန့်ကြော်ငြာစာ")
                || body.Contains("အစည်းအဝေးမှတ်တမ်း")
                || body.Contains("သတင်းထုတ်ပြန်ချက်")
                || body.Contains("အသိပေးကြေညာချက်")
                || body.Contains("ညွှန်ကြားချက်")
                || body.Contains("အိတ်ဖွင့်တင်ဒါ");
            bool hasClosing = DistributionClosingRegex.IsMatch(body);
            bool hasColloquial = ColloquialLeakRegex.IsMatch(body);

            result.LooksLikeOfficialDocument = hasRef || hasSubject || hasAddressee || body.Contains("အကြောင်းအရာ") || body.Contains("စာအမှတ်");

            // Check Burmese numbered paragraphs sequence (၁။, ၂။, ၃။ ...)
            MatchCollection paraMatches = BurmeseNumberedParaRegex.Matches(body);
            bool numberingOk = true;
            string numberingDetail;
            if (AsciiNumberedParaRegex.IsMatch(body))
            {
                numberingOk = false;
                numberingDetail = "အင်္ဂလိပ်ဂဏန်း '1.' / '2.' အစား မြန်မာရုံးသုံး '၁။' / '၂။' စနစ်ကို သုံးနှုန်းသင့်ပါသည်။";
            }
            else if (paraMatches.Count == 0)
            {
                numberingOk = body.Length < 220;
                numberingDetail = numberingOk
                    ? "စာတိုဖြစ်၍ အပိုဒ်စဉ် မလိုအပ်ပါ။"
                    : "ရုံးစာစာကိုယ်တွင် '၁။'၊ '၂။' အပိုဒ်စဉ်များ တပ်၍ ရေးသားသင့်ပါသည်။";
            }
            else
            {
                var nums = new List<int>(paraMatches.Count);
                foreach (Match pm in paraMatches)
                {
                    nums.Add(ParseMyanmarInt(pm.Groups["Num"].Value));
                }
                for (int i = 0; i < nums.Count; i++)
                {
                    if (nums[i] != i + 1)
                    {
                        numberingOk = false;
                        break;
                    }
                }
                numberingDetail = numberingOk
                    ? $"အပိုဒ်စဉ် ၁။ မှ {MyanmarTraditionalCalendar.ToMyanmarDigits(nums.Count)}။ အထိ အစဉ်လိုက် မှန်ကန်ပါသည်။"
                    : "အပိုဒ်စဉ် နံပါတ်များ ကျော်နေခြင်း သို့မဟုတ် အစဉ်မကျခြင်း တွေ့ရှိရပါသည်။";
            }

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "စာအမှတ် / အမှတ်စဉ် (Reference No.)",
                Passed = hasRef,
                Detail = hasRef ? "စာအမှတ် / အမှတ်စဉ် ပါဝင်ပါသည်။" : "'စာအမှတ်၊ ...' ဖော်ပြချက် မတွေ့ရှိသေးပါ။"
            });

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "ရုံးသုံး ရက်စွဲ (Date Header)",
                Passed = hasDate,
                Detail = hasDate
                    ? (hasMyanmarEra ? "ခရစ်နှစ်နှင့် မြန်မာသက္ကရာဇ် နှစ်မျိုးလုံး ပြည့်စုံစွာ ပါဝင်ပါသည်။" : "ရက်စွဲပါဝင်ပါသည် (လိုအပ်ပါက မြန်မာသက္ကရာဇ် ပူးတွဲဖော်ပြနိုင်သည်)။")
                    : "'ရက်စွဲ၊ ... ခုနှစ်' ဖော်ပြချက် မတွေ့ရှိသေးပါ။"
            });

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "လိပ်မူချက် / စာအမျိုးအစား ('သို့' / ခေါင်းစီး)",
                Passed = hasAddressee,
                Detail = hasAddressee ? "လိပ်မူချက် သို့မဟုတ် ရုံးသုံးခေါင်းစီး ပါဝင်ပါသည်။" : "လိပ်မူချက် ('သို့') သို့မဟုတ် အမိန့်ကြော်ငြာစာခေါင်းစီး မတွေ့ရှိသေးပါ။"
            });

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "အကြောင်းအရာ ခေါင်းစဉ် ('အကြောင်းအရာ။')",
                Passed = hasSubject,
                Detail = hasSubject ? "အကြောင်းအရာ / ခေါင်းစဉ် စနစ်တကျ ပါဝင်ပါသည်။" : "'အကြောင်းအရာ။' ခေါင်းစဉ် မတွေ့ရှိသေးပါ။"
            });

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "အပိုဒ်စဉ် စနစ် ('၁။'၊ '၂။'၊ '၃။')",
                Passed = numberingOk,
                Detail = numberingDetail
            });

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "စာပေနှင့် ရုံးသုံး အရေးအသားဟန်ပန်",
                Passed = !hasColloquial,
                Detail = !hasColloquial
                    ? "ရုံးသုံးစာပေဟန်ပန် (သည်/မည်/၏) နှင့် ညီညွတ်ပါသည်။"
                    : "အပြောစကားအသုံးအနှုန်း (ပါတယ်/မယ်/ဒါပေမဲ့ စသည်) ရောနှောနေပါသည်။"
            });

            result.Items.Add(new OfficialCheckItem
            {
                CriterionName = "လက်မှတ်နှင့် မိတ္တူဖြန့်ဝေမှု ('မိတ္တူကို')",
                Passed = hasClosing,
                IsAdvisory = true,
                Detail = hasClosing ? "တာဝန်ခံလက်မှတ် / မိတ္တူဖြန့်ဝေမှု ပါဝင်ပါသည်။" : "'မိတ္တူကို' သို့မဟုတ် လက်မှတ်ရေးထိုးသူ ရာထူးဖော်ပြချက် ထည့်သွင်းရန် ကျန်ရှိနိုင်ပါသည်။"
            });

            result.TotalRequiredCount = result.Items.Count;
            result.PassedCount = result.Items.Count(x => x.Passed);
            return result;
        }

        private static int ParseMyanmarInt(string mmDigits)
        {
            int val = 0;
            for (int i = 0; i < mmDigits.Length; i++)
            {
                char c = mmDigits[i];
                if (c >= '\u1040' && c <= '\u1049')
                {
                    val = val * 10 + (c - '\u1040');
                }
            }
            return val;
        }
    }
}

