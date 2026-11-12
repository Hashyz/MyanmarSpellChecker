using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Typography
{
    public sealed class TypographyFormatSummary
    {
        public int NumeralsConverted { get; set; }
        public int PunctuationSpacesFixed { get; set; }
        public int ZeroWidthRemoved { get; set; }

        public int TotalChanges => NumeralsConverted + PunctuationSpacesFixed + ZeroWidthRemoved;
    }

    /// <summary>
    /// Context-smart Myanmar numeral (0-9 <-> ၀-၉) and official punctuation (၊ / ။ / ZWSP) formatter.
    /// Automatically protects URLs, emails, IPv4/version numbers, and English alphanumeric identifiers
    /// such as COVID-19, ISO-9001, A4, SHA-256, 5G, and 2FA.
    /// </summary>
    public static class MyanmarTypographyFormatter
    {
        private static readonly Regex ProtectedTechnicalTokenRegex = new Regex(
            @"(?:https?://\S+|www\.\S+|\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b|\b\d{1,3}(?:\.\d{1,3}){3}\b|\bv\d+(?:\.\d+)+\b|\b(?:ISO|IEC|IEEE|RFC|ASTM|DIN|JIS)\s*[-:]?\s*\d+(?:[-:]\d+)*\b|\b[A-Za-z]+[-_]?\d+[A-Za-z0-9.-]*\b|\b\d+[A-Za-z][A-Za-z0-9-]*\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MidWordZeroWidthRegex = new Regex(
            @"(?<=[\u1000-\u103F])[\u200B-\u200D\uFEFF]+(?=[\u1000-\u103F])",
            RegexOptions.Compiled);

        private static readonly Regex SpaceBeforeMyanmarPunctRegex = new Regex(
            @"[ \u00A0]+(?=[\u104A\u104B])",
            RegexOptions.Compiled);

        private static readonly Regex MissingSpaceAfterPoteHtiRegex = new Regex(
            @"(?<![\u1040-\u1049]|\d)\u104A(?=[\u1000-\u104F])|\u104A(?=[\u1000-\u103F\u104C-\u104F])",
            RegexOptions.Compiled);

        private static readonly Regex MissingSpaceAfterPoteMaRegex = new Regex(
            @"\u104B(?=[\u1000-\u104FA-Za-z])",
            RegexOptions.Compiled);

        private static readonly Regex DuplicateMidSentenceSpaceRegex = new Regex(
            @"(?<=\S) {2,}(?=\S)",
            RegexOptions.Compiled);

        /// <summary>
        /// Converts numerals between ASCII (0-9) and Myanmar (၀-၉) while shielding URLs, emails, and English technical codes.
        /// </summary>
        public static string ConvertNumeralsSmart(string input, bool toMyanmarDigits, out int convertedDigitsCount)
        {
            convertedDigitsCount = 0;
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            if (!toMyanmarDigits)
            {
                char[] chars = input.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] >= '\u1040' && chars[i] <= '\u1049')
                    {
                        chars[i] = (char)('0' + (chars[i] - '\u1040'));
                        convertedDigitsCount++;
                    }
                }
                return new string(chars);
            }

            // Build protected intervals for URLs, emails, IP addresses, and English alphanumeric codes (COVID-19, ISO 9001, A4, 5G)
            MatchCollection matches = ProtectedTechnicalTokenRegex.Matches(input);
            bool[] isProtected = new bool[input.Length];
            foreach (Match m in matches)
            {
                for (int k = m.Index; k < m.Index + m.Length && k < isProtected.Length; k++)
                {
                    isProtected[k] = true;
                }
            }

            char[] outChars = input.ToCharArray();
            for (int i = 0; i < outChars.Length; i++)
            {
                if (!isProtected[i] && outChars[i] >= '0' && outChars[i] <= '9')
                {
                    outChars[i] = (char)('\u1040' + (outChars[i] - '0'));
                    convertedDigitsCount++;
                }
            }

            return new string(outChars);
        }

        /// <summary>
        /// Enforces official Burmese punctuation spacing around ၊ and ။, removes mid-word broken zero-width spaces,
        /// and normalizescanonical combining mark order.
        /// </summary>
        public static string FormatPunctuationAndCleanInvisible(string input, out int punctuationFixes, out int zeroWidthRemoved)
        {
            punctuationFixes = 0;
            zeroWidthRemoved = 0;
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            int zwCount = MidWordZeroWidthRegex.Matches(input).Count;
            string cleaned = zwCount > 0 ? MidWordZeroWidthRegex.Replace(input, string.Empty) : input;
            zeroWidthRemoved = zwCount;

            int beforeCount = SpaceBeforeMyanmarPunctRegex.Matches(cleaned).Count;
            if (beforeCount > 0)
            {
                cleaned = SpaceBeforeMyanmarPunctRegex.Replace(cleaned, string.Empty);
                punctuationFixes += beforeCount;
            }

            int afterHtiCount = MissingSpaceAfterPoteHtiRegex.Matches(cleaned).Count;
            if (afterHtiCount > 0)
            {
                cleaned = MissingSpaceAfterPoteHtiRegex.Replace(cleaned, "\u104A ");
                punctuationFixes += afterHtiCount;
            }

            int afterMaCount = MissingSpaceAfterPoteMaRegex.Matches(cleaned).Count;
            if (afterMaCount > 0)
            {
                cleaned = MissingSpaceAfterPoteMaRegex.Replace(cleaned, "\u104B ");
                punctuationFixes += afterMaCount;
            }

            int dupSpaces = DuplicateMidSentenceSpaceRegex.Matches(cleaned).Count;
            if (dupSpaces > 0)
            {
                cleaned = DuplicateMidSentenceSpaceRegex.Replace(cleaned, " ");
                punctuationFixes += dupSpaces;
            }

            return UnicodeNormalizer.NormalizeCanonicalOrder(cleaned);
        }

        /// <summary>
        /// Runs complete official Myanmar typography normalization (0-9 -> ၀-၉ + ၊/။ spacing + ZWSP cleanup) in one pass.
        /// </summary>
        public static string FormatAllOfficialMyanmarTypography(string input, bool convertAsciiDigitsToMyanmar, out TypographyFormatSummary summary)
        {
            summary = new TypographyFormatSummary();
            if (string.IsNullOrEmpty(input))
                return input ?? string.Empty;

            string step1 = FormatPunctuationAndCleanInvisible(input, out int pFixes, out int zwFixes);
            summary.PunctuationSpacesFixed = pFixes;
            summary.ZeroWidthRemoved = zwFixes;

            if (convertAsciiDigitsToMyanmar)
            {
                step1 = ConvertNumeralsSmart(step1, toMyanmarDigits: true, out int numConverted);
                summary.NumeralsConverted = numConverted;
            }

            return step1;
        }
    }
}

