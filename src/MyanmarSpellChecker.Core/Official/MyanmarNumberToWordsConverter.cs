using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyanmarSpellChecker.Core.Official
{
    public enum NumberConversionMode
    {
        /// <summary>
        /// Myanmar Banking, Treasury, Legal Contract & Voucher Lakh (သိန်း) accounting mode:
        /// e.g. 1,500,000 -> "ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ"
        /// </summary>
        BankingKyatLakh = 0,

        /// <summary>
        /// General Burmese Cardinal Number mode (သန်း = million) with official သတ်ပုံကျမ်း creaky-tone sandhi:
        /// e.g. 1,500,000 -> "တစ်သန်းငါးသိန်း", 1,250 -> "တစ်ထောင့်နှစ်ရာ့ငါးဆယ်"
        /// </summary>
        GeneralCardinal = 1
    }

    public sealed class NumberConversionResult
    {
        public bool Success { get; set; }
        public string NormalizedMyanmarDigits { get; set; } = string.Empty;
        public string BurmeseWords { get; set; } = string.Empty;
        public string VoucherInlineText { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Converts ASCII or Myanmar numeric/currency strings into authoritative Burmese words
    /// obeying "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" creaky-tone sandhi (ဆယ့် / ရာ့ / ထောင့်)
    /// and Myanmar Banking/Treasury Lakh (သိန်း) conventions.
    /// </summary>
    public static class MyanmarNumberToWordsConverter
    {
        private static readonly string[] DigitWords =
        {
            "သုည", "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး"
        };

        private static readonly Regex CleanNumericInputRegex = new Regex(
            @"^(?:Ks\.?|MMK|ကျပ်|ငွေကျပ်|\$)?\s*(?<Sign>-)?\s*(?<Int>[0-9\u1040-\u1049,၊\s]+)(?:\.(?<Frac>[0-9\u1040-\u1049]{1,2}))?\s*(?:/-|=|ကျပ်|တိတိ)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool TryExtractNumericSelection(string? rawSelection, out string cleanedNumericToken)
        {
            cleanedNumericToken = string.Empty;
            if (string.IsNullOrWhiteSpace(rawSelection))
                return false;

            string trimmed = rawSelection!.Trim();
            if (trimmed.Length > 48)
                return false;

            Match m = CleanNumericInputRegex.Match(trimmed);
            if (!m.Success)
                return false;

            string digits = NormalizeToAsciiDigits(m.Groups["Int"].Value);
            if (string.IsNullOrEmpty(digits) || digits.Length > 15)
                return false;

            cleanedNumericToken = trimmed;
            return true;
        }

        public static NumberConversionResult Convert(string? input, NumberConversionMode mode = NumberConversionMode.BankingKyatLakh)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new NumberConversionResult
                {
                    Success = false,
                    ErrorMessage = "ကျေးဇူးပြု၍ ဂဏန်းတန်ဖိုး ရိုက်ထည့်ပါ (ဥပမာ - 1500000 သို့မဟုတ် ၁,၅၀၀,၀၀၀)။"
                };
            }

            Match m = CleanNumericInputRegex.Match(input!.Trim());
            if (!m.Success)
            {
                return new NumberConversionResult
                {
                    Success = false,
                    ErrorMessage = "ဂဏန်းပုံစံ မမှန်ကန်ပါ။ ကိန်းဂဏန်း သီးသန့် ရိုက်ထည့်ပါ (ဥပမာ - 1,500,000)။"
                };
            }

            bool isNegative = m.Groups["Sign"].Success;
            string intAscii = NormalizeToAsciiDigits(m.Groups["Int"].Value);
            string fracAscii = m.Groups["Frac"].Success ? NormalizeToAsciiDigits(m.Groups["Frac"].Value) : string.Empty;

            if (string.IsNullOrEmpty(intAscii) || !long.TryParse(intAscii, NumberStyles.None, CultureInfo.InvariantCulture, out long wholeValue))
            {
                return new NumberConversionResult
                {
                    Success = false,
                    ErrorMessage = "ဂဏန်းပမာဏ ကြီးမားလွန်းနေသည် သို့မဟုတ် ဖတ်ရှု၍မရပါ။"
                };
            }

            int pyaValue = 0;
            if (!string.IsNullOrEmpty(fracAscii))
            {
                if (fracAscii.Length == 1)
                    fracAscii += "0";
                int.TryParse(fracAscii, NumberStyles.None, CultureInfo.InvariantCulture, out pyaValue);
            }

            string formattedAscii = wholeValue.ToString("N0", CultureInfo.InvariantCulture);
            string formattedMmDigits = ToMyanmarDigits(formattedAscii);
            if (pyaValue > 0)
            {
                formattedMmDigits += "." + ToMyanmarDigits(pyaValue.ToString("D2", CultureInfo.InvariantCulture));
            }

            if (mode == NumberConversionMode.BankingKyatLakh)
            {
                string kyatWords = ConvertToBankingKyatLakh(wholeValue, pyaValue, isNegative);
                string voucherPrefix = (isNegative ? "-" : "") + formattedMmDigits + (pyaValue == 0 ? "/-" : "");
                return new NumberConversionResult
                {
                    Success = true,
                    NormalizedMyanmarDigits = (isNegative ? "-" : "") + formattedMmDigits,
                    BurmeseWords = kyatWords,
                    VoucherInlineText = $"ကျပ် {voucherPrefix} ({kyatWords})"
                };
            }
            else
            {
                string cardinalWords = (isNegative ? "အနုတ် " : string.Empty) + ConvertCardinal(wholeValue);
                if (pyaValue > 0)
                {
                    cardinalWords += " ဒသမ ";
                    foreach (char c in fracAscii)
                    {
                        cardinalWords += DigitWords[c - '0'];
                    }
                }

                return new NumberConversionResult
                {
                    Success = true,
                    NormalizedMyanmarDigits = (isNegative ? "-" : "") + formattedMmDigits,
                    BurmeseWords = cardinalWords,
                    VoucherInlineText = $"{(isNegative ? "-" : "")}{formattedMmDigits} ({cardinalWords})"
                };
            }
        }

        /// <summary>
        /// Converts a non-negative integer into Burmese cardinal words obeying official creaky-tone sandhi.
        /// Uses သန်း (10^6) for >= 1,000,000.
        /// </summary>
        public static string ConvertCardinal(long n)
        {
            if (n < 0)
                return "အနုတ် " + ConvertCardinal(-n);
            if (n == 0)
                return DigitWords[0];

            var sb = new StringBuilder();

            if (n >= 1_000_000)
            {
                long millions = n / 1_000_000;
                n %= 1_000_000;

                if (millions >= 100 && n == 0 && millions % 10 == 0)
                {
                    sb.Append("သန်း");
                    sb.Append(ConvertUnderOneMillion(millions));
                }
                else
                {
                    sb.Append(ConvertUnderOneMillion(millions));
                    sb.Append("သန်း");
                }
            }

            if (n > 0)
            {
                sb.Append(ConvertUnderOneMillion(n));
            }

            return sb.ToString();
        }

        private static string ConvertToBankingKyatLakh(long wholeKyat, int pyaValue, bool isNegative)
        {
            if (wholeKyat == 0 && pyaValue == 0)
                return "ကျပ်သုညတိတိ";

            var sb = new StringBuilder();
            if (isNegative)
                sb.Append("အနုတ် ");

            if (wholeKyat > 0)
            {
                sb.Append("ကျပ်");
                long lakhs = wholeKyat / 100_000;
                long subLakh = wholeKyat % 100_000;

                if (lakhs > 0)
                {
                    if (lakhs >= 100 && subLakh == 0)
                    {
                        // Standard Myanmar Banking & Treasury phrasing for >= 100 lakhs without sub-lakh remainder:
                        // e.g. 10,000,000 (100 lakhs) -> "ကျပ်သိန်းတစ်ရာတိတိ"
                        //      12,500,000 (125 lakhs) -> "ကျပ်သိန်းတစ်ရာ့နှစ်ဆယ့်ငါးတိတိ"
                        sb.Append("သိန်း");
                        sb.Append(ConvertCardinal(lakhs));
                    }
                    else
                    {
                        // e.g. 1,500,000 (15 lakhs) -> "ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ"
                        //      12,550,000 -> "ကျပ်တစ်ရာ့နှစ်ဆယ့်ငါးသိန်းငါးသောင်းတိတိ"
                        sb.Append(ConvertCardinal(lakhs));
                        sb.Append("သိန်း");
                    }
                }

                if (subLakh > 0)
                {
                    sb.Append(ConvertUnderOneLakh(subLakh));
                }
            }

            if (pyaValue > 0)
            {
                if (wholeKyat > 0)
                    sb.Append(" ");
                sb.Append("ပြား");
                sb.Append(ConvertUnderOneHundred(pyaValue));
            }
            else
            {
                sb.Append("တိတိ");
            }

            return sb.ToString();
        }

        private static string ConvertUnderOneMillion(long n)
        {
            if (n <= 0) return string.Empty;
            var sb = new StringBuilder();

            if (n >= 100_000)
            {
                long lakhs = n / 100_000;
                n %= 100_000;
                sb.Append(DigitWords[lakhs]);
                sb.Append("သိန်း");
            }

            if (n > 0)
            {
                sb.Append(ConvertUnderOneLakh(n));
            }

            return sb.ToString();
        }

        private static string ConvertUnderOneLakh(long n)
        {
            if (n <= 0) return string.Empty;
            var sb = new StringBuilder();

            if (n >= 10_000)
            {
                long tenThousands = n / 10_000;
                n %= 10_000;
                sb.Append(DigitWords[tenThousands]);
                sb.Append("သောင်း");
            }

            if (n >= 1_000)
            {
                long thousands = n / 1_000;
                n %= 1_000;
                sb.Append(DigitWords[thousands]);
                // Creaky-tone sandhi ('ထောင့်') when followed by a smaller non-zero remainder!
                sb.Append(n > 0 ? "ထောင့်" : "ထောင်");
            }

            if (n >= 100)
            {
                long hundreds = n / 100;
                n %= 100;
                sb.Append(DigitWords[hundreds]);
                // Creaky-tone sandhi ('ရာ့') when followed by a smaller non-zero remainder!
                sb.Append(n > 0 ? "ရာ့" : "ရာ");
            }

            if (n > 0)
            {
                sb.Append(ConvertUnderOneHundred((int)n));
            }

            return sb.ToString();
        }

        private static string ConvertUnderOneHundred(int n)
        {
            if (n <= 0) return string.Empty;
            if (n < 10) return DigitWords[n];

            int tens = n / 10;
            int ones = n % 10;
            if (ones == 0)
                return DigitWords[tens] + "ဆယ်";

            // Creaky-tone sandhi ('ဆယ့်') when followed by ones digit 1..9!
            return DigitWords[tens] + "ဆယ့်" + DigitWords[ones];
        }

        private static string NormalizeToAsciiDigits(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c >= '0' && c <= '9')
                {
                    sb.Append(c);
                }
                else if (c >= '\u1040' && c <= '\u1049')
                {
                    sb.Append((char)('0' + (c - '\u1040')));
                }
            }
            return sb.ToString();
        }

        private static string ToMyanmarDigits(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] >= '0' && chars[i] <= '9')
                    chars[i] = (char)('\u1040' + (chars[i] - '0'));
            }
            return new string(chars);
        }
    }
}

