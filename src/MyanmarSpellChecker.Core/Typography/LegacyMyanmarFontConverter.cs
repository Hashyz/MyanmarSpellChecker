using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Typography
{
    /// <summary>
    /// Font-aware converter for legacy ASCII Myanmar fonts (WinInnwa, WinResearcher, Win-Myanmar,
    /// Wwin_Burmese, CE Font, Academy, Metrix) and legacy Zawgyi-One into canonical Pyidaungsu Unicode 5.1+,
    /// while leaving genuine English/Latin fonts (Calibri, Times New Roman, Arial, URLs, emails) untouched.
    /// </summary>
    public static class LegacyMyanmarFontConverter
    {
        private static readonly string[] LegacyWinAsciiFontPrefixes =
        {
            "WinInnwa",
            "Win Innwa",
            "WinResearcher",
            "Win Researcher",
            "Win-Myanmar",
            "Win Myanmar",
            "Win_Burmese",
            "Wwin_Burmese",
            "WinKalaw",
            "Win Kalaw",
            "Win Haka",
            "WinHaka",
            "WinMandalay",
            "Win Mandalay",
            "WinTaunggyi",
            "Win Taunggyi",
            "WinPonnya",
            "Win Ponnya",
            "CE Font",
            "CEClassic",
            "CE-Myanmar",
            "Academy",
            "Metrix",
            "Gandamar",
            "King"
        };

        private static readonly Regex UrlOrEmailGuardRegex = new Regex(
            @"(?:https?://\S+|www\.\S+|\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Visual WinInnwa cluster:
        // Optional pre-base E ('a' / '\u00EA') + optional pre-base Ra/Ra-Wa ('j','M','B','N','<','>','^','`')
        // + Base Consonant (1-char or 2-char stacked cluster)
        // + optional post-base Kinzi ('F') and post-base medials ('s','\u00DF','Q','R','W','G','T','S','\u00A7','I')
        private static readonly Dictionary<char, string> SingleBaseConsonants = new Dictionary<char, string>
        {
            ['u'] = "\u1000", // က
            ['c'] = "\u1001", // ခ
            ['*'] = "\u1002", // ဂ
            ['C'] = "\u1003", // ဃ
            ['i'] = "\u1004", // င
            ['p'] = "\u1005", // စ
            ['q'] = "\u1006", // ဆ
            ['Z'] = "\u1007", // ဇ
            ['\u00DA'] = "\u1009", // ဉ (Ú)
            ['\u00CD'] = "\u1009", // ဉ (Í before subscript)
            ['n'] = "\u100A", // ည
            ['#'] = "\u100B", // ဋ
            ['X'] = "\u100C", // ဌ
            ['!'] = "\u100D", // ဍ
            ['\u00A1'] = "\u100E", // ဎ (¡)
            ['~'] = "\u100E", // ဎ
            ['P'] = "\u100F", // ဏ
            ['w'] = "\u1010", // တ
            ['x'] = "\u1011", // ထ
            ['\''] = "\u1012", // ဒ
            ['"'] = "\u1013", // ဓ
            ['e'] = "\u1014", // န
            ['E'] = "\u1014", // န (tall variant)
            ['y'] = "\u1015", // ပ
            ['z'] = "\u1016", // ဖ
            ['A'] = "\u1017", // ဗ
            ['b'] = "\u1018", // ဘ
            ['r'] = "\u1019", // မ
            [','] = "\u101A", // ယ
            ['&'] = "\u101B", // ရ
            ['\u00BD'] = "\u101B", // ရ (½ variant)
            ['v'] = "\u101C", // လ
            ['\u0001'] = "\u101D", // Contextual Wa placeholder for '0'
            ['o'] = "\u101E", // သ
            ['['] = "\u101F", // ဟ
            ['V'] = "\u1020", // ဠ
            ['t'] = "\u1021", // အ
            ['\u00A3'] = "\u1023", // ဣ (£)
            ['\u00FE'] = "\u1024", // ဤ (þ)
            ['O'] = "\u1025", // ဥ
            ['{'] = "\u1027", // ဧ
            ['\u00F3'] = "\u103F", // ဿ (ó)
            ['\u00A5'] = "\u100B\u1039\u100B", // ဋ္ဋ (¥)
            ['|'] = "\u100B\u1039\u100C",      // ဋ္ဌ (|)
            ['\u00D7'] = "\u100D\u1039\u100D", // ဍ္ဍ (×)
            ['\u00B9'] = "\u100D\u1039\u100E", // ဍ္ဎ (¹)
            ['@'] = "\u100F\u1039\u100D",      // ဏ္ဍ (@)
            ['\u0002'] = "\u1008",             // Placeholder for 'ps' -> ဈ
            ['\u0003'] = "\u1029",             // Placeholder for 'Mo' -> ဩ
            ['\u0004'] = "\u1026"              // Placeholder for 'OD' -> ဦ
        };

        private static readonly Dictionary<char, string> SubscriptChars = new Dictionary<char, string>
        {
            ['\u00FA'] = "\u1039\u1000", // ္က (ú)
            ['\u00A9'] = "\u1039\u1001", // ္ခ (©)
            ['\u00BE'] = "\u1039\u1002", // ္ဂ (¾)
            ['\u00A2'] = "\u1039\u1003", // ္ဃ (¢)
            ['\u00F6'] = "\u1039\u1005", // ္စ (ö)
            ['\u00E4'] = "\u1039\u1006", // ္ဆ (ä)
            ['\u00C6'] = "\u1039\u1007", // ္ဇ (Æ)
            ['\u00D1'] = "\u1039\u1008", // ္ဈ (Ñ)
            ['\u00B3'] = "\u1039\u100B", // ္ဋ (³)
            ['\u00E5'] = "\u1039\u1010", // ္တ (å)
            ['\u00C5'] = "\u1039\u1010", // ္တ (Å)
            ['\u00AC'] = "\u1039\u1011", // ္ထ (¬)
            ['\u00A6'] = "\u1039\u1011", // ္ထ (¦)
            ['\u00B4'] = "\u1039\u1012", // ္ဒ (´)
            ['\u00A8'] = "\u1039\u1013", // ္ဓ (¨)
            ['\u00E9'] = "\u1039\u1014", // ္န (é)
            ['\u00DC'] = "\u1039\u1015", // ္ပ (Ü)
            ['\u00E6'] = "\u1039\u1016", // ္ဖ (æ)
            ['\u00C1'] = "\u1039\u1017", // ဗ္ဗ (Á)
            ['\u00C7'] = "\u1039\u1018", // ္ဘ (Ç)
            ['\u00AE'] = "\u1039\u1019", // ္မ (®)
            ['\u2019'] = "\u1039\u101C"  // ္လ (’ cp1252 0x92)
        };

        public static bool IsLegacyAsciiMyanmarFont(string? fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return false;

            string trimmed = fontName!.Trim();
            if (trimmed.IndexOf("Uni", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("Pyidaungsu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            for (int i = 0; i < LegacyWinAsciiFontPrefixes.Length; i++)
            {
                if (trimmed.StartsWith(LegacyWinAsciiFontPrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool IsZawgyiFont(string? fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return false;

            return fontName!.IndexOf("Zawgyi", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Converts a text segment into canonical Pyidaungsu Unicode 5.1+ based on its font name or Zawgyi codepoints.
        /// Preserves English runs, URLs, and email addresses.
        /// </summary>
        public static string ConvertSegmentToPyidaungsu(string text, string? fontName, out bool wasConverted)
        {
            wasConverted = false;
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            if (IsLegacyAsciiMyanmarFont(fontName))
            {
                string converted = ConvertWinInnwaToUnicode(text);
                wasConverted = !string.Equals(converted, text, StringComparison.Ordinal);
                return converted;
            }

            if (IsZawgyiFont(fontName) || UnicodeNormalizer.IsZawgyiText(text))
            {
                string converted = UnicodeNormalizer.NormalizeCanonicalOrder(
                    UnicodeNormalizer.FixLookalikeCharacters(
                        UnicodeNormalizer.ConvertZawgyiToUnicode(text)));
                wasConverted = !string.Equals(converted, text, StringComparison.Ordinal);
                return converted;
            }

            return text;
        }

        /// <summary>
        /// Transcodes WinInnwa / WinResearcher / Win-Myanmar / CE ASCII & PUA text into canonical Pyidaungsu Unicode 5.1+.
        /// Protects embedded URLs and email addresses if any appear inside the string.
        /// </summary>
        public static string ConvertWinInnwaToUnicode(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Protect any literal URL or email address inside the text
            MatchCollection urls = UrlOrEmailGuardRegex.Matches(input);
            if (urls.Count > 0)
            {
                var sb = new StringBuilder(input.Length * 2);
                int cursor = 0;
                foreach (Match m in urls)
                {
                    if (m.Index > cursor)
                    {
                        sb.Append(ConvertWinInnwaCore(input.Substring(cursor, m.Index - cursor)));
                    }
                    sb.Append(m.Value);
                    cursor = m.Index + m.Length;
                }
                if (cursor < input.Length)
                {
                    sb.Append(ConvertWinInnwaCore(input.Substring(cursor)));
                }
                return sb.ToString();
            }

            return ConvertWinInnwaCore(input);
        }

        private static string ConvertWinInnwaCore(string raw)
        {
            // 1. Normalize Symbol-font PUA range (U+F020..U+F0FF -> U+0020..U+00FF) & Word Smart Quotes
            var normChars = new char[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch >= '\uF020' && ch <= '\uF0FF')
                {
                    ch = (char)(ch - '\uF000');
                }
                if (ch == '\u201C' || ch == '\u201D')
                {
                    ch = '"'; // WinInnwa ဓ
                }
                else if (ch == '\u2018')
                {
                    ch = '\''; // WinInnwa ဒ
                }
                else if (ch == '\u2019')
                {
                    // After 'v' (လ), ’ (cp1252 0x92) is stacked subscript ္လ (လ္လ); otherwise Word Smart Quote for ' (ဒ)!
                    bool prevIsLa = i > 0 && (normChars[i - 1] == 'v');
                    if (!prevIsLa)
                        ch = '\'';
                }
                normChars[i] = ch;
            }

            string s = new string(normChars);

            // 2. Multi-character WinInnwa lexical/ligature idioms before cluster parsing
            s = s.Replace("4if;", "\u104E\u1004\u103A\u1038"); // ၎င်း
            s = s.Replace("aMomf", "\u102A");                  // ဪ
            s = s.Replace("]]", "\u201C");                     // “
            s = s.Replace("}}", "\u201D");                     // ”
            s = s.Replace("\u00D3", "\u1009\u102C");           // Ó -> ဉာ
            s = s.Replace("Of", "\u1009\u103A");               // Of -> ဉ်
            s = s.Replace("Om", "\u1009\u102C");               // Om -> ဉာ
            s = s.Replace("OD", "\u0004");                     // OD -> ဦ placeholder
            s = s.Replace("ps", "\u0002");                     // ps -> ဈ placeholder
            s = s.Replace("Mo", "\u0003");                     // Mo -> ဩ placeholder

            // 3. Disambiguate '0' between Myanmar Digit Zero '၀' (\u1040) and Consonant Wa 'ဝ' (\u0001 -> \u101D)
            char[] buf = s.ToCharArray();
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] != '0')
                    continue;

                bool prevDigit = i > 0 && IsNumericNeighbor(buf[i - 1]);
                bool nextDigit = i + 1 < buf.Length && IsNumericNeighbor(buf[i + 1]);
                bool prevLetter = i > 0 && IsWinSyllableNeighbor(buf[i - 1]);
                bool nextLetter = i + 1 < buf.Length && IsWinSyllableNeighbor(buf[i + 1]);

                if ((prevDigit || nextDigit) && !nextLetter)
                {
                    buf[i] = '0'; // stays digit 0 -> ၀
                }
                else if (prevLetter || nextLetter)
                {
                    buf[i] = '\u0001'; // consonant Wa -> ဝ
                }
            }

            // 4. Left-to-right visual-to-logical Burmese syllable transducer
            var outSb = new StringBuilder(buf.Length * 2);
            int pos = 0;
            while (pos < buf.Length)
            {
                // Check for optional pre-base E ('a' or 'ê') and pre-base Ra / Ra-Wa ('j','M','B','N','<','>','^','`')
                int scan = pos;
                bool hasPreE = false;
                string preRaMedial = string.Empty;

                while (scan < buf.Length)
                {
                    char c = buf[scan];
                    if (!hasPreE && (c == 'a' || c == '\u00EA'))
                    {
                        hasPreE = true;
                        scan++;
                    }
                    else if (preRaMedial.Length == 0 && (c == 'j' || c == 'M' || c == 'B' || c == 'N'))
                    {
                        preRaMedial = "\u103C";
                        scan++;
                    }
                    else if (preRaMedial.Length == 0 && (c == '<' || c == '>' || c == '^' || c == '`'))
                    {
                        preRaMedial = "\u103C\u103D";
                        scan++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (scan < buf.Length && SingleBaseConsonants.TryGetValue(buf[scan], out string? baseUni))
                {
                    scan++;
                    string subscriptUni = string.Empty;
                    if (scan < buf.Length && SubscriptChars.TryGetValue(buf[scan], out string? sub))
                    {
                        subscriptUni = sub;
                        scan++;
                    }

                    // Collect post-base Kinzi ('F') and post-base medials ('s','ß','Q','R','W','G','T','S','§','I')
                    bool hasKinzi = false;
                    var postMedials = new StringBuilder();
                    string trailingVowelsFromLigature = string.Empty;

                    while (scan < buf.Length)
                    {
                        char pm = buf[scan];
                        if (pm == 'F')
                        {
                            hasKinzi = true;
                            scan++;
                        }
                        else if (pm == 's' || pm == '\u00DF')
                        {
                            postMedials.Append('\u103B');
                            scan++;
                        }
                        else if (pm == 'Q')
                        {
                            postMedials.Append("\u103B\u103E");
                            scan++;
                        }
                        else if (pm == 'R')
                        {
                            postMedials.Append("\u103B\u103D");
                            scan++;
                        }
                        else if (pm == 'W')
                        {
                            postMedials.Append("\u103B\u103D\u103E");
                            scan++;
                        }
                        else if (pm == 'G')
                        {
                            if (!preRaMedial.Contains("\u103D"))
                                postMedials.Append('\u103D');
                            scan++;
                        }
                        else if (pm == 'T')
                        {
                            if (preRaMedial.Contains("\u103D"))
                                postMedials.Append('\u103E');
                            else
                                postMedials.Append("\u103D\u103E");
                            scan++;
                        }
                        else if (pm == 'S' || pm == '\u00A7')
                        {
                            postMedials.Append('\u103E');
                            scan++;
                        }
                        else if (pm == 'I')
                        {
                            postMedials.Append('\u103E');
                            trailingVowelsFromLigature += "\u102F";
                            scan++;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Emit in canonical Unicode 5.1+ order: Kinzi -> Base -> Subscript -> Ya -> Ra -> Wa -> Ha -> E -> LigatureVowel
                    if (hasKinzi)
                        outSb.Append("\u1004\u103A\u1039");

                    // Special case: a + Mo ('ဩ') -> ဪ / ဩော
                    if (baseUni == "\u1029" && hasPreE && scan < buf.Length && buf[scan] == 'm' && scan + 1 < buf.Length && buf[scan + 1] == 'f')
                    {
                        outSb.Append('\u102A'); // ဪ
                        pos = scan + 2;
                        continue;
                    }

                    outSb.Append(baseUni);
                    outSb.Append(subscriptUni);
                    outSb.Append(OrderMedials(preRaMedial, postMedials.ToString()));
                    if (hasPreE)
                        outSb.Append('\u1031');
                    if (trailingVowelsFromLigature.Length > 0)
                        outSb.Append(trailingVowelsFromLigature);

                    pos = scan;
                    continue;
                }

                // Standalone post-base vowels, digits, punctuation, or whitespace
                char chCurrent = buf[pos];
                outSb.Append(MapStandaloneChar(chCurrent));
                pos++;
            }

            return UnicodeNormalizer.FixLookalikeCharacters(outSb.ToString());
        }

        private static string OrderMedials(string preRa, string postMed)
        {
            string combined = preRa + postMed;
            if (combined.Length <= 1)
                return combined;

            bool hasYa = combined.IndexOf('\u103B') >= 0;
            bool hasRa = combined.IndexOf('\u103C') >= 0;
            bool hasWa = combined.IndexOf('\u103D') >= 0;
            bool hasHa = combined.IndexOf('\u103E') >= 0;

            var sb = new StringBuilder(4);
            if (hasYa) sb.Append('\u103B');
            if (hasRa) sb.Append('\u103C');
            if (hasWa) sb.Append('\u103D');
            if (hasHa) sb.Append('\u103E');
            return sb.ToString();
        }

        private static string MapStandaloneChar(char c)
        {
            switch (c)
            {
                case 'd': return "\u102D"; // ိ
                case 'D': return "\u102E"; // ီ
                case 'k':
                case 'K': return "\u102F"; // ု
                case 'l':
                case 'L': return "\u1030"; // ူ
                case 'a':
                case '\u00EA': return "\u1031"; // ေ
                case 'J': return "\u1032"; // ဲ
                case 'H': return "\u1036"; // ံ
                case '\u00F0': return "\u102D\u1036"; // ð -> ိံ
                case 'h':
                case 'Y':
                case 'U': return "\u1037"; // ့
                case ';': return "\u1038"; // း
                case 'g': return "\u102B"; // ါ
                case 'm': return "\u102C"; // ာ
                case ':': return "\u102B\u103A"; // ါ်
                case 'f': return "\u103A"; // ်
                case 'F': return "\u1004\u103A\u1039"; // င်္
                case 'I': return "\u103E\u102F"; // ှု
                case '?': return "\u104A"; // ၊
                case '/': return "\u104B"; // ။
                case '\u00FC': return "\u104C"; // ü -> ၌
                case '\u00ED': return "\u104D"; // í -> ၍
                case '\u00A4': return "\u104E"; // ¤ -> ၎
                case '\\': return "\u104F"; // ၏
                case '\u00E7': return ","; // ç -> ,
                case ']': return "\u2018"; // ‘
                case '}': return "\u2019"; // ’
                case '0': return "\u1040"; // ၀
                case '1': return "\u1041"; // ၁
                case '2': return "\u1042"; // ၂
                case '3': return "\u1043"; // ၃
                case '4': return "\u1044"; // ၄
                case '5': return "\u1045"; // ၅
                case '6': return "\u1046"; // ၆
                case '7': return "\u1047"; // ၇
                case '8': return "\u1048"; // ၈
                case '9': return "\u1049"; // ၉
                default:
                    if (SubscriptChars.TryGetValue(c, out string? sub))
                        return sub;
                    return c.ToString();
            }
        }

        private static bool IsNumericNeighbor(char c)
        {
            return (c >= '1' && c <= '9') || c == '0' || c == '.';
        }

        private static bool IsWinSyllableNeighbor(char c)
        {
            if (c == 'a' || c == '\u00EA' || c == 'j' || c == 'M' || c == 'B' || c == 'N' ||
                c == '<' || c == '>' || c == '^' || c == '`' ||
                c == 'g' || c == 'm' || c == 'd' || c == 'D' || c == 'k' || c == 'K' ||
                c == 'l' || c == 'L' || c == 'J' || c == 'H' || c == 'f' || c == 'F' ||
                c == ';' || c == 'h' || c == 'Y' || c == 'U' || c == ':' || c == 'I' ||
                c == 's' || c == 'G' || c == 'S' || c == 'T' || c == 'Q' || c == 'R' || c == 'W')
            {
                return true;
            }
            return SingleBaseConsonants.ContainsKey(c) || SubscriptChars.ContainsKey(c);
        }
    }
}
