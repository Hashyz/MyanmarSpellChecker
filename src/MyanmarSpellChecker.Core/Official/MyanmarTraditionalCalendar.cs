using System;
using System.Text;

namespace MyanmarSpellChecker.Core.Official
{
    /// <summary>
    /// 100% Offline Myanmar Traditional Lunisolar Calendar (မြန်မာသက္ကရာဇ် / သာသနာတော်နှစ်) calculator
    /// implementing the canonical Burmese astronomical equations (Surya Siddhanta / Thandeikta / 3rd Era post-1312 ME)
    /// for Government ရုံးစာ correspondence, Gazette notifications, and legal date headers.
    /// </summary>
    public sealed class MyanmarDateInfo
    {
        public DateTime GregorianDate { get; set; }
        public int SasanaYear { get; set; }
        public int MyanmarYear { get; set; }
        public int YearType { get; set; } // 0 = သာမန်နှစ်, 1 = ဝါငယ်ထပ်နှစ်, 2 = ဝါကြီးထပ်နှစ်
        public int MonthCode { get; set; }
        public string MonthNameBurmese { get; set; } = string.Empty;
        public int MoonPhaseCode { get; set; } // 0 = လဆန်း, 1 = လပြည့်, 2 = လပြည့်ကျော်, 3 = လကွယ်
        public string MoonPhaseBurmese { get; set; } = string.Empty;
        public int FortnightDay { get; set; } // 1..15
        public int DayOfMonth { get; set; } // 1..30
        public string WeekdayBurmese { get; set; } = string.Empty;
        public string SabbathLabel { get; set; } = string.Empty;
        public string AstrologicalNote { get; set; } = string.Empty;

        public string YearTypeLabel
        {
            get
            {
                if (YearType == 2) return "ဝါကြီးထပ်နှစ်";
                if (YearType == 1) return "ဝါငယ်ထပ်နှစ်";
                return "သာမန်နှစ်";
            }
        }

        /// <summary>
        /// Official Myanmar Era date line, e.g. "၁၃၈၈ ခုနှစ်၊ တန်ဆောင်မုန်းလဆန်း ၂ ရက်" or "၁၃၈၈ ခုနှစ်၊ ကဆုန်လပြည့်နေ့".
        /// </summary>
        public string OfficialMyanmarDateText
        {
            get
            {
                string yr = MyanmarTraditionalCalendar.ToMyanmarDigits(MyanmarYear);
                if (MoonPhaseCode == 1 || MoonPhaseCode == 3)
                {
                    return $"{yr} ခုနှစ်၊ {MonthNameBurmese}{MoonPhaseBurmese}နေ့";
                }

                string fd = MyanmarTraditionalCalendar.ToMyanmarDigits(FortnightDay);
                return $"{yr} ခုနှစ်၊ {MonthNameBurmese}{MoonPhaseBurmese} {fd} ရက်";
            }
        }

        /// <summary>
        /// Official Gregorian date line in Burmese orthography, e.g. "၂၀၂၆ ခုနှစ်၊ နိုဝင်ဘာလ ၁၁ ရက်".
        /// </summary>
        public string OfficialGregorianDateText =>
            MyanmarTraditionalCalendar.FormatGregorianDateBurmese(GregorianDate);

        /// <summary>
        /// Standard two-line Ministry ရုံးစာ header date block (Gregorian + parenthesized Myanmar Era).
        /// </summary>
        public string DualOfficialHeaderText =>
            $"{OfficialGregorianDateText}{Environment.NewLine}({OfficialMyanmarDateText})";

        /// <summary>
        /// Full ceremonial / Gazette date string including Sasana Year and weekday.
        /// </summary>
        public string CeremonialSasanaDateText
        {
            get
            {
                string sy = MyanmarTraditionalCalendar.ToMyanmarDigits(SasanaYear);
                var sb = new StringBuilder();
                sb.Append($"သာသနာတော်နှစ် {sy} ပြည့်နှစ်၊ ကောဇာသက္ကရာဇ် {OfficialMyanmarDateText}၊ {WeekdayBurmese}နေ့");
                if (!string.IsNullOrEmpty(SabbathLabel))
                    sb.Append($" ({SabbathLabel})");
                return sb.ToString();
            }
        }
    }

    public static class MyanmarTraditionalCalendar
    {
        private const double SolarYear = 1577917828.0 / 4320000.0;     // 365.2587564814815 days
        private const double LunarMonth = 1577917828.0 / 53433336.0;   // 29.53058794607172 days
        private const double MyanmarEpoch = 1954168.050623;            // Beginning of 0 ME (MMT)

        private static readonly string[] GregorianMonthsBurmese =
        {
            "",
            "ဇန်နဝါရီလ",
            "ဖေဖော်ဝါရီလ",
            "မတ်လ",
            "ဧပြီလ",
            "မေလ",
            "ဇွန်လ",
            "ဇူလိုင်လ",
            "ဩဂုတ်လ",
            "စက်တင်ဘာလ",
            "အောက်တိုဘာလ",
            "နိုဝင်ဘာလ",
            "ဒီဇင်ဘာလ"
        };

        private static readonly string[] WeekdaysBurmeseByDayOfWeek =
        {
            "တနင်္ဂနွေ", // Sunday = 0
            "တနင်္လာ",   // Monday = 1
            "အင်္ဂါ",     // Tuesday = 2
            "ဗုဒ္ဓဟူး",   // Wednesday = 3
            "ကြာသပတေး", // Thursday = 4
            "သောကြာ",   // Friday = 5
            "စနေ"       // Saturday = 6
        };

        public static MyanmarDateInfo FromGregorian(DateTime date)
        {
            long jdn = GregorianToJulianDayNumber(date.Year, date.Month, date.Day);
            int my = (int)Math.Floor((jdn - 0.5 - MyanmarEpoch) / SolarYear);
            CheckYearResult yo = CheckMyanmarYear(my);

            int dd = (int)(jdn - yo.FirstDayOfTaguJdn) + 1;
            int b = yo.YearType / 2;
            int c = yo.YearType == 0 ? 1 : 0; // 1 for common year, 0 for watat year
            int myl = 354 + (1 - c) * 30 + b;
            int mmt = (dd - 1) / myl; // 1 = late (နှောင်း), 0 = regular
            dd -= mmt * myl;

            int a = (dd + 423) / 512;
            int mm = (int)Math.Floor((dd - b * a + c * a * 30 + 29.26) / 29.544);
            int e = (mm + 12) / 16;
            int f = (mm + 11) / 16;
            int md = dd - (int)Math.Floor(29.544 * mm - 29.26) - b * e + c * f * 30;
            mm += f * 3 - e * 4 + 12 * mmt;

            int mml = GetMonthLength(mm, yo.YearType);
            int mp = ((md + 1) / 16) + (md / 16) + (md / mml);
            int mf = md - 15 * (md / 16);

            // Sasana Year advances on the day after Full Moon of Kason (or standard my + 1182 convention)
            int sasanaYear = my + 1182;

            // Myanmar weekday index used by traditional astrological formulas: 0=Sat, 1=Sun, ..., 6=Fri
            int mmWd = (int)((jdn + 2) % 7);
            if (mmWd < 0) mmWd += 7;

            int sabbathCode = GetSabbathCode(md, mml);
            string sabbath = sabbathCode == 1 ? "ဥပုသ်နေ့" : (sabbathCode == 2 ? "အဖိတ်နေ့" : string.Empty);

            var astroParts = new System.Collections.Generic.List<string>(2);
            if (IsYatyaza(mm, mmWd))
                astroParts.Add("ရက်ရာဇာ");
            int pyathada = GetPyathada(mm, mmWd);
            if (pyathada == 1)
                astroParts.Add("ပြဿဒါး");
            else if (pyathada == 2)
                astroParts.Add("မွန်းလွဲပြဿဒါး");

            return new MyanmarDateInfo
            {
                GregorianDate = date.Date,
                SasanaYear = sasanaYear,
                MyanmarYear = my,
                YearType = yo.YearType,
                MonthCode = mm,
                MonthNameBurmese = GetMonthNameBurmese(mm, yo.YearType),
                MoonPhaseCode = mp,
                MoonPhaseBurmese = GetMoonPhaseBurmese(mp),
                FortnightDay = mf,
                DayOfMonth = md,
                WeekdayBurmese = WeekdaysBurmeseByDayOfWeek[(int)date.DayOfWeek],
                SabbathLabel = sabbath,
                AstrologicalNote = astroParts.Count > 0 ? string.Join(" / ", astroParts) : string.Empty
            };
        }

        public static string FormatGregorianDateBurmese(DateTime date)
        {
            string yr = ToMyanmarDigits(date.Year);
            string mo = date.Month >= 1 && date.Month <= 12 ? GregorianMonthsBurmese[date.Month] : string.Empty;
            string dy = ToMyanmarDigits(date.Day);
            return $"{yr} ခုနှစ်၊ {mo} {dy} ရက်";
        }

        public static string ToMyanmarDigits(long value)
        {
            string ascii = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            char[] buf = ascii.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] >= '0' && buf[i] <= '9')
                    buf[i] = (char)('\u1040' + (buf[i] - '0'));
            }
            return new string(buf);
        }

        public static long GregorianToJulianDayNumber(int year, int month, int day)
        {
            int a = (14 - month) / 12;
            int y = year + 4800 - a;
            int m = month + 12 * a - 3;
            return day + (153 * m + 2) / 5 + 365L * y + (y / 4) - (y / 100) + (y / 400) - 32045;
        }

        private static string GetMonthNameBurmese(int mm, int yearType)
        {
            switch (mm)
            {
                case 0: return "ပထမဝါဆို";
                case 1: return "တန်ခူး";
                case 2: return "ကဆုန်";
                case 3: return "နယုန်";
                case 4: return yearType > 0 ? "ဒုတိယဝါဆို" : "ဝါဆို";
                case 5: return "ဝါခေါင်";
                case 6: return "တော်သလင်း";
                case 7: return "သီတင်းကျွတ်";
                case 8: return "တန်ဆောင်မုန်း";
                case 9: return "နတ်တော်";
                case 10: return "ပြာသို";
                case 11: return "တပို့တွဲ";
                case 12: return "တပေါင်း";
                case 13: return "နှောင်းတန်ခူး";
                case 14: return "နှောင်းကဆုန်";
                default: return "တန်ခူး";
            }
        }

        private static string GetMoonPhaseBurmese(int mp)
        {
            switch (mp)
            {
                case 0: return "လဆန်း";
                case 1: return "လပြည့်";
                case 2: return "လပြည့်ကျော်";
                case 3: return "လကွယ်";
                default: return "လဆန်း";
            }
        }

        private static int GetMonthLength(int mm, int yearType)
        {
            int mml = 30 - (mm % 2);
            if (mm == 3)
                mml += yearType / 2; // In Big Watat (ဝါကြီးထပ်), Nayon has 30 days instead of 29
            return mml;
        }

        private static int GetSabbathCode(int md, int mml)
        {
            if (md == 8 || md == 15 || md == 23 || md == mml) return 1;
            if (md == 7 || md == 14 || md == 22 || md == mml - 1) return 2;
            return 0;
        }

        private static bool IsYatyaza(int mm, int wd)
        {
            int m1 = mm % 4;
            if (m1 < 0) m1 += 4;
            int wd1 = (m1 / 2) + 4;
            int wd2 = (1 - (m1 / 2) + (m1 % 2)) * (1 + 2 * (m1 % 2));
            return wd == wd1 || wd == wd2;
        }

        private static int GetPyathada(int mm, int wd)
        {
            int m1 = mm % 4;
            if (m1 < 0) m1 += 4;
            int[] wda = { 1, 3, 3, 0, 2, 1, 2 };
            if (m1 == 0 && wd == 4) return 2;
            if (wd >= 0 && wd < wda.Length && m1 == wda[wd]) return 1;
            return 0;
        }

        private struct WatatCheck
        {
            public long FullMoonWaso2Jdn;
            public int IsWatat;
        }

        private struct CheckYearResult
        {
            public int YearType;
            public long FirstDayOfTaguJdn;
            public long FullMoonWaso2Jdn;
        }

        private static CheckYearResult CheckMyanmarYear(int my)
        {
            WatatCheck y2 = CalculateWatat(my);
            int myt = y2.IsWatat;
            int yd = 0;
            WatatCheck y1;
            do
            {
                yd++;
                y1 = CalculateWatat(my - yd);
            } while (y1.IsWatat == 0 && yd < 3);

            long fm;
            if (myt != 0)
            {
                long nd = (y2.FullMoonWaso2Jdn - y1.FullMoonWaso2Jdn) % 354;
                myt = (int)(nd / 31) + 1; // 1 = little watat (30), 2 = big watat (31)
                fm = y2.FullMoonWaso2Jdn;
            }
            else
            {
                fm = y1.FullMoonWaso2Jdn + 354L * yd;
            }

            long tg1 = y1.FullMoonWaso2Jdn + 354L * yd - 102L;
            return new CheckYearResult
            {
                YearType = myt,
                FirstDayOfTaguJdn = tg1,
                FullMoonWaso2Jdn = fm
            };
        }

        private static WatatCheck CalculateWatat(int my)
        {
            GetEraConstants(my, out double ei, out double wo, out int nm, out int ew);
            double ta = (SolarYear / 12.0 - LunarMonth) * (12 - nm);
            double ed = (SolarYear * (my + 3739)) % LunarMonth;
            if (ed < 0) ed += LunarMonth;
            if (ed < ta) ed += LunarMonth;

            long fm = (long)Math.Round(SolarYear * my + MyanmarEpoch - ed + 4.5 * LunarMonth + wo, MidpointRounding.AwayFromZero);
            int watat = 0;
            if (ei >= 2.0)
            {
                double tw = LunarMonth - (SolarYear / 12.0 - LunarMonth) * nm;
                if (ed >= tw) watat = 1;
            }
            else
            {
                int rem = (my * 7 + 2) % 19;
                if (rem < 0) rem += 19;
                watat = rem / 12;
            }

            watat ^= ew;
            return new WatatCheck { FullMoonWaso2Jdn = fm, IsWatat = watat };
        }

        private static void GetEraConstants(int my, out double ei, out double wo, out int nm, out int ew)
        {
            ew = 0;
            if (my >= 1312)
            {
                ei = 3.0;
                wo = -0.5;
                nm = 8;
                if (my == 1377) wo += 1.0;
                if (my == 1344 || my == 1345) ew = 1;
            }
            else if (my >= 1217)
            {
                ei = 2.0;
                wo = -1.0;
                nm = 4;
                if (my == 1234) wo += 1.0;
                else if (my == 1261) wo -= 1.0;
                if (my == 1263 || my == 1264) ew = 1;
            }
            else if (my >= 1100)
            {
                ei = 1.3;
                wo = -0.85;
                nm = -1;
                if (my == 1120 || my == 1150 || my == 1207) wo += 1.0;
                else if (my == 1126 || my == 1172) wo -= 1.0;
                if (my == 1201 || my == 1202) ew = 1;
            }
            else
            {
                ei = 1.2;
                wo = -1.1;
                nm = -1;
            }
        }
    }
}

