using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MyanmarSpellChecker.Core.Nlp
{
    public readonly struct MyanmarPronunciationInfo
    {
        public string SpokenBurmese { get; }
        public string EasyRomanization { get; }
        public string Ipa { get; }

        public MyanmarPronunciationInfo(string spokenBurmese, string easyRomanization, string ipa)
        {
            SpokenBurmese = spokenBurmese ?? string.Empty;
            EasyRomanization = easyRomanization ?? string.Empty;
            Ipa = ipa ?? string.Empty;
        }

        public bool HasValue =>
            !string.IsNullOrEmpty(SpokenBurmese) ||
            !string.IsNullOrEmpty(EasyRomanization);
    }

    /// <summary>
    /// Offline Burmese Grapheme-to-Phoneme (G2P) transcriber, spoken Burmese respeller (with intervocalic voicing sandhi),
    /// myG2P formatter, and Romanized-Burmese-to-PhoneticKey search transducer.
    /// Zero external dependencies and zero supplementary-plane surrogates.
    /// </summary>
    public static class MyanmarPhoneticTranscriber
    {
        private static readonly Dictionary<string, MyanmarPronunciationInfo> CuratedIrregularPronunciations =
            new Dictionary<string, MyanmarPronunciationInfo>(StringComparer.Ordinal)
            {
                ["သတ်ပုံ"] = new MyanmarPronunciationInfo("သပ်-ပုံ", "that-pon", "θaʔ.bòʊɴ"),
                ["စာလုံးပေါင်းသတ်ပုံကျမ်း"] = new MyanmarPronunciationInfo("စာ-လုံး-ပေါင်း-သပ်-ပုံ-ကျမ်း", "sa-lon-baung-that-pon-kyan", "sà.lóʊɴ.báʊɴ.θaʔ.bòʊɴ.tɕáɴ"),
                ["လက်ဖက်"] = new MyanmarPronunciationInfo("လ-ဖက်", "la-hpet", "lə.pʰɛʔ"),
                ["လက်ဖက်ရည်"] = new MyanmarPronunciationInfo("လ-ဖက်-ရည်", "la-hpet-ye", "lə.pʰɛʔ.jè"),
                ["ပုဂ္ဂိုလ်"] = new MyanmarPronunciationInfo("ပုဂ်-ဂို", "pouk-go", "poʊʔ.gò"),
                ["မင်္ဂလာပါ"] = new MyanmarPronunciationInfo("မင်-ဂ-လာ-ပါ", "min-ga-la-ba", "mɪ̀ɴ.gə.là.bà"),
                ["မင်္ဂလာ"] = new MyanmarPronunciationInfo("မင်-ဂ-လာ", "min-ga-la", "mɪ̀ɴ.gə.là"),
                ["ကျောက်ဆည်"] = new MyanmarPronunciationInfo("ကျောက်-ဆယ်", "kyauk-hse", "tɕaʊʔ.sʰɛ̀"),
                ["ပညာ"] = new MyanmarPronunciationInfo("ပျင်-ညာ", "pyin-nya", "pjɪ̀ɴ.ɲà"),
                ["ပညာရေး"] = new MyanmarPronunciationInfo("ပျင်-ညာ-ရေး", "pyin-nya-ye", "pjɪ̀ɴ.ɲà.jé"),
                ["သင်္ကြန်"] = new MyanmarPronunciationInfo("သ-ဂျန်", "tha-gyan", "θə.dʑàɴ"),
                ["အင်္ကျီ"] = new MyanmarPronunciationInfo("အင်-ဂျီ", "in-gyi", "ʔɪ̀ɴ.dʑì"),
                ["ခြင်္သေ့"] = new MyanmarPronunciationInfo("ခြင်-သေ့", "chin-the", "tɕʰɪ̀ɴ.θḛ"),
                ["ရန်ကုန်"] = new MyanmarPronunciationInfo("ရန်-ဂုန်", "yan-gon", "jàɴ.gòʊɴ"),
                ["မန္တလေး"] = new MyanmarPronunciationInfo("မန်-ဒ-လေး", "man-da-le", "màɴ.də.lé"),
                ["နေပြည်တော်"] = new MyanmarPronunciationInfo("နေ-ပြည်-ဒေါ်", "ne-pyi-daw", "nè.pjì.dɔ̀"),
                ["တက္ကသိုလ်"] = new MyanmarPronunciationInfo("တက်-က-သို", "tet-ka-tho", "tɛʔ.kə.θò"),
                ["ဥက္ကဋ္ဌ"] = new MyanmarPronunciationInfo("အုတ်-က-ထ", "ouk-ka-hta", "ʔoʊʔ.kə.tʰa̰"),
                ["ပုဂံ"] = new MyanmarPronunciationInfo("ပ-ဂံ", "ba-gan", "bə.gàɴ"),
                ["မြန်မာ"] = new MyanmarPronunciationInfo("မြန်-မာ", "myan-ma", "mjàɴ.mà"),
                ["စကား"] = new MyanmarPronunciationInfo("ဇ-ဂါး", "za-ga", "zə.gá"),
                ["ထမင်း"] = new MyanmarPronunciationInfo("ထ-မင်း", "hta-min", "tʰə.mɪ́ɴ"),
                ["အစိုးရ"] = new MyanmarPronunciationInfo("အ-စိုး-ရ", "a-soe-ya", "ʔə.só.ja̰"),
                ["ဗိုလ်ချုပ်"] = new MyanmarPronunciationInfo("ဗို-ချုပ်", "bo-chouk", "bò.tɕʰoʊʔ")
            };

        private static readonly Dictionary<string, string[]> DirectRomanizedAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["mingalaba"] = new[] { "မင်္ဂလာပါ", "မင်္ဂလာ" },
                ["mingalarpar"] = new[] { "မင်္ဂလာပါ", "မင်္ဂလာ" },
                ["mingalarbar"] = new[] { "မင်္ဂလာပါ", "မင်္ဂလာ" },
                ["mingalar"] = new[] { "မင်္ဂလာ", "မင်္ဂလာပါ" },
                ["thatpon"] = new[] { "သတ်ပုံ", "သတ်ပုံကျမ်း" },
                ["thatpoun"] = new[] { "သတ်ပုံ" },
                ["kyaukse"] = new[] { "ကျောက်ဆည်" },
                ["yangon"] = new[] { "ရန်ကုန်" },
                ["rangoon"] = new[] { "ရန်ကုန်" },
                ["mandalay"] = new[] { "မန္တလေး" },
                ["naypyidaw"] = new[] { "နေပြည်တော်" },
                ["naypyitaw"] = new[] { "နေပြည်တော်" },
                ["pyinnya"] = new[] { "ပညာ", "ပညာရေး" },
                ["pyinnyar"] = new[] { "ပညာ", "ပညာရေး" },
                ["pyinnyaryay"] = new[] { "ပညာရေး" },
                ["lahpet"] = new[] { "လက်ဖက်", "လက်ဖက်ရည်" },
                ["laphet"] = new[] { "လက်ဖက်", "လက်ဖက်ရည်" },
                ["thingyan"] = new[] { "သင်္ကြန်" },
                ["bagan"] = new[] { "ပုဂံ" },
                ["pagan"] = new[] { "ပုဂံ" },
                ["myanmar"] = new[] { "မြန်မာ" },
                ["bamar"] = new[] { "ဗမာ" },
                ["asuuya"] = new[] { "အစိုးရ" },
                ["asoeya"] = new[] { "အစိုးရ" },
                ["shwedagon"] = new[] { "ရွှေတိဂုံ" },
                ["chinthay"] = new[] { "ခြင်္သေ့" },
                ["poukgo"] = new[] { "ပုဂ္ဂိုလ်" },
                ["abidan"] = new[] { "အဘိဓာန်" },
                ["thadingyut"] = new[] { "သီတင်းကျွတ်" },
                ["tazaungdaing"] = new[] { "တန်ဆောင်တိုင်" },
                ["taunggyi"] = new[] { "တောင်ကြီး" },
                ["mawlamyine"] = new[] { "မော်လမြိုင်" },
                ["myitkyina"] = new[] { "မြစ်ကြီးနား" },
                ["pathein"] = new[] { "ပုသိမ်" },
                ["sagaing"] = new[] { "စစ်ကိုင်း" },
                ["magway"] = new[] { "မကွေး" },
                ["bago"] = new[] { "ပဲခူး" },
                ["monywa"] = new[] { "မုံရွာ" },
                ["meiktila"] = new[] { "မိတ္ထီလာ" },
                ["pyinoolwin"] = new[] { "ပြင်ဦးလွင်" },
                ["sittwe"] = new[] { "စစ်တွေ" },
                ["dawei"] = new[] { "ထားဝယ်" },
                ["hakha"] = new[] { "ဟားခါး" },
                ["loikaw"] = new[] { "လွိုင်ကော်" },
                ["hpaan"] = new[] { "ဘားအံ" }
            };

        private static readonly (string Roman, string MyanmarOnset)[] RomanOnsetsLongestFirst =
        {
            ("hpy", "ဖြ"), ("phy", "ဖြ"), ("khy", "ချ"), ("hky", "ချ"),
            ("shw", "ရွှ"), ("nyw", "ညွ"), ("myw", "မြွ"), ("pyw", "ပြွ"),
            ("kyw", "ကြွ"), ("gyw", "ကြွ"), ("chw", "ချွ"), ("khw", "ခွ"),
            ("thw", "သွ"), ("hnw", "နှ"), ("hmw", "မှ"), ("hlw", "လှ"),
            ("ky", "ကျ"), ("gy", "ကြ"), ("ch", "ချ"), ("sh", "ရှ"),
            ("hs", "ဆ"), ("ht", "ထ"), ("hp", "ဖ"), ("ph", "ဖ"),
            ("kh", "ခ"), ("th", "သ"), ("dh", "သ"), ("ny", "ည"),
            ("ng", "င"), ("my", "မြ"), ("py", "ပြ"), ("by", "ဗျ"),
            ("hm", "မှ"), ("hn", "နှ"), ("hl", "လှ"), ("hw", "ဝှ"),
            ("kw", "ကွ"), ("gw", "ဂွ"), ("sw", "စွ"), ("zw", "ဇွ"),
            ("tw", "တွ"), ("dw", "ဒွ"), ("pw", "ပွ"), ("bw", "ဘွ"),
            ("mw", "မွ"), ("lw", "လွ"), ("yw", "ရွ"),
            ("k", "က"), ("g", "ဂ"), ("s", "စ"), ("z", "ဇ"),
            ("t", "တ"), ("d", "ဒ"), ("n", "န"), ("p", "ပ"),
            ("b", "ဗ"), ("m", "မ"), ("y", "ယ"), ("r", "ရ"),
            ("l", "လ"), ("w", "ဝ"), ("h", "ဟ")
        };

        private static readonly (string Roman, string MyanmarRhyme)[] RomanRhymesLongestFirst =
        {
            ("aung", "ောင်"), ("aing", "ိုင်"), ("oung", "ောင်"),
            ("eik", "ိတ်"), ("ein", "ိန်"), ("ouk", "ုတ်"), ("oun", "ုန်"),
            ("auk", "ောက်"), ("aun", "ောင်"), ("ain", "ိုင်"), ("out", "ောက်"),
            ("aw", "ော်"), ("ar", "ာ"), ("ay", "ေး"), ("ei", "ေ"),
            ("oe", "ိုး"), ("oo", "ူ"), ("ee", "ီ"), ("ai", "ဲ"),
            ("at", "တ်"), ("et", "က်"), ("it", "စ်"), ("ut", "ွတ်"), ("ot", "ုတ်"),
            ("ap", "ပ်"), ("ep", "က်"), ("ip", "စ်"), ("up", "ုပ်"),
            ("ak", "က်"), ("ik", "ိတ်"), ("uk", "ုတ်"), ("ok", "ုတ်"),
            ("an", "န်"), ("in", "င်"), ("un", "ွန်"), ("on", "ုံ"), ("en", "င်"),
            ("am", "မ်"), ("im", "ိမ်"), ("um", "ုံ"),
            ("a", "ာ"), ("i", "ီ"), ("u", "ူ"), ("e", "ေ"), ("o", "ို")
        };

        private static readonly Regex KinziExpandRegex = new Regex(
            @"\u1004\u103A\u1039([\u1000-\u1021])",
            RegexOptions.Compiled);

        private static readonly Regex PatSintExpandRegex = new Regex(
            @"([\u1000-\u1021])\u1039([\u1000-\u1021])",
            RegexOptions.Compiled);

        public static MyanmarPronunciationInfo GetPronunciation(string myanmarWord, string storedPronunciationField = "")
        {
            if (string.IsNullOrWhiteSpace(myanmarWord))
                return default;

            string norm = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(myanmarWord.Trim()));
            if (!ContainsMyanmarLetters(norm))
                return default;

            if (CuratedIrregularPronunciations.TryGetValue(norm, out var curated))
                return curated;

            // Case 1: Stored field from myg2p_v2.txt -> "ga- za: [gə zá]"
            if (!string.IsNullOrWhiteSpace(storedPronunciationField))
            {
                string trimmedField = storedPronunciationField.Trim();
                int bracketStart = trimmedField.IndexOf('[');
                int bracketEnd = bracketStart >= 0 ? trimmedField.IndexOf(']', bracketStart + 1) : -1;
                if (bracketStart > 0 && bracketEnd > bracketStart)
                {
                    string rawRoman = trimmedField.Substring(0, bracketStart).Trim();
                    string rawIpa = trimmedField.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
                    string cleanRoman = NormalizeMyG2pRomanization(rawRoman);
                    string cleanIpa = NormalizeMyG2pIpa(rawIpa);
                    string spokenMm = BuildSpokenBurmese(norm, rawRoman);
                    return new MyanmarPronunciationInfo(spokenMm, cleanRoman, cleanIpa);
                }

                // Case 2: Stored Burmese pronunciation from မြန်မာအဘိဓာန် (e.g., "လဖက်", "ဂဇား")
                if (ContainsMyanmarLetters(trimmedField))
                {
                    var fromRespelling = TranscribeAlgorithmically(trimmedField, applyVoicingSandhi: false);
                    string spokenDisplay = !string.IsNullOrEmpty(fromRespelling.SpokenBurmese)
                        ? fromRespelling.SpokenBurmese
                        : trimmedField;
                    return new MyanmarPronunciationInfo(spokenDisplay, fromRespelling.EasyRomanization, fromRespelling.Ipa);
                }
            }

            // Case 3: Algorithmic Burmese syllable G2P + intervocalic voicing sandhi
            return TranscribeAlgorithmically(norm, applyVoicingSandhi: true);
        }

        public static string FormatCompactDualLine(MyanmarPronunciationInfo info, bool englishUi)
        {
            if (!info.HasValue)
                return string.Empty;

            string prefix = englishUi ? "Pronunciation: " : "အသံထွက်: ";
            if (!string.IsNullOrEmpty(info.SpokenBurmese) && !string.IsNullOrEmpty(info.EasyRomanization))
                return prefix + info.SpokenBurmese + "  •  " + info.EasyRomanization;

            return prefix + (!string.IsNullOrEmpty(info.SpokenBurmese) ? info.SpokenBurmese : info.EasyRomanization);
        }

        public static string FormatIpaTooltip(MyanmarPronunciationInfo info)
        {
            if (string.IsNullOrEmpty(info.Ipa))
                return string.Empty;
            return "IPA: [" + info.Ipa + "]";
        }

        /// <summary>
        /// Returns direct Myanmar headword aliases (if any) for popular irregular Romanized Burmese queries.
        /// </summary>
        public static IReadOnlyList<string> GetDirectRomanizedCandidates(string latinQuery)
        {
            if (string.IsNullOrWhiteSpace(latinQuery))
                return Array.Empty<string>();

            string key = NormalizeRomanLookupInput(latinQuery);
            if (key.Length >= 3 && DirectRomanizedAliases.TryGetValue(key, out var aliases))
                return aliases;

            return Array.Empty<string>();
        }

        /// <summary>
        /// Parses a Romanized Burmese query (e.g. "mingalaba", "thatpon", "kazar", "pyinnya") into up to 8 candidate
        /// <see cref="MyanmarTrie.ComputePhoneticKey"/> skeletons for sub-millisecond lookup in <see cref="MyanmarTrie"/>.
        /// </summary>
        public static List<string> TryBuildPhoneticKeysFromRomanized(string latinQuery)
        {
            var keys = new List<string>(6);
            if (string.IsNullOrWhiteSpace(latinQuery))
                return keys;

            string clean = NormalizeRomanLookupInput(latinQuery);
            if (clean.Length < 2 || clean.Length > 28)
                return keys;

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            void AddKeyFromMyanmar(string mmCandidate)
            {
                if (string.IsNullOrEmpty(mmCandidate))
                    return;
                string pk = MyanmarTrie.ComputePhoneticKey(mmCandidate);
                if (!string.IsNullOrEmpty(pk) && seenKeys.Add(pk))
                    keys.Add(pk);
            }

            if (DirectRomanizedAliases.TryGetValue(clean, out var directWords))
            {
                for (int i = 0; i < directWords.Length; i++)
                    AddKeyFromMyanmar(directWords[i]);
            }

            // Segment hyphen/space-delimited or continuous romanized syllables (1..5 syllables)
            var syllableForms = new List<string>(6);
            if (latinQuery.IndexOf('-') >= 0 || latinQuery.IndexOf(' ') >= 0)
            {
                string[] rawParts = latinQuery.ToLowerInvariant().Split(new[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (rawParts.Length >= 1 && rawParts.Length <= 6)
                {
                    var sb = new StringBuilder();
                    bool allOk = true;
                    for (int i = 0; i < rawParts.Length; i++)
                    {
                        if (!TryParseSingleRomanSyllable(rawParts[i], out string mmSyl))
                        {
                            allOk = false;
                            break;
                        }
                        sb.Append(mmSyl);
                    }
                    if (allOk)
                        syllableForms.Add(sb.ToString());
                }
            }

            ParseContinuousRomanSyllables(clean, 0, 0, new List<string>(5), syllableForms, maxCandidates: 6);

            for (int i = 0; i < syllableForms.Count; i++)
            {
                string form = syllableForms[i];
                AddKeyFromMyanmar(form);

                // Also emit Kinzi-collapsed skeleton when 'င်' precedes a stop/affricate (e.g. မင်ဂလာပါ -> မင်္ဂလာပါ, အင်ဂျီ -> အင်္ကျီ)
                if (form.IndexOf("\u1004\u103A", StringComparison.Ordinal) >= 0)
                {
                    string kinziForm = Regex.Replace(form, @"\u1004\u103A([\u1000-\u1021])", "\u1004\u103A\u1039$1");
                    if (!string.Equals(kinziForm, form, StringComparison.Ordinal))
                        AddKeyFromMyanmar(kinziForm);
                }
            }

            return keys;
        }

        private static string NormalizeRomanLookupInput(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c >= 'A' && c <= 'Z')
                    sb.Append((char)(c + 32));
                else if (c >= 'a' && c <= 'z')
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static void ParseContinuousRomanSyllables(
            string text,
            int index,
            int depth,
            List<string> currentSyllables,
            List<string> outputForms,
            int maxCandidates)
        {
            if (outputForms.Count >= maxCandidates)
                return;

            if (index == text.Length)
            {
                if (currentSyllables.Count >= 1)
                    outputForms.Add(string.Concat(currentSyllables));
                return;
            }

            if (depth >= 5)
                return;

            int maxLen = Math.Min(7, text.Length - index);
            for (int len = maxLen; len >= 1; len--)
            {
                string slice = text.Substring(index, len);
                if (TryParseSingleRomanSyllable(slice, out string mmSyl))
                {
                    currentSyllables.Add(mmSyl);
                    ParseContinuousRomanSyllables(text, index + len, depth + 1, currentSyllables, outputForms, maxCandidates);
                    currentSyllables.RemoveAt(currentSyllables.Count - 1);
                    if (outputForms.Count >= maxCandidates)
                        return;
                }
            }
        }

        private static bool TryParseSingleRomanSyllable(string slice, out string myanmarSyllable)
        {
            myanmarSyllable = string.Empty;
            if (string.IsNullOrEmpty(slice))
                return false;

            // Vowel-initial syllable (အ + rhyme)
            for (int r = 0; r < RomanRhymesLongestFirst.Length; r++)
            {
                if (string.Equals(slice, RomanRhymesLongestFirst[r].Roman, StringComparison.Ordinal))
                {
                    myanmarSyllable = "အ" + RomanRhymesLongestFirst[r].MyanmarRhyme;
                    return true;
                }
            }

            // Onset + rhyme
            for (int o = 0; o < RomanOnsetsLongestFirst.Length; o++)
            {
                string onsetRom = RomanOnsetsLongestFirst[o].Roman;
                if (slice.Length > onsetRom.Length && slice.StartsWith(onsetRom, StringComparison.Ordinal))
                {
                    string rem = slice.Substring(onsetRom.Length);
                    for (int r = 0; r < RomanRhymesLongestFirst.Length; r++)
                    {
                        if (string.Equals(rem, RomanRhymesLongestFirst[r].Roman, StringComparison.Ordinal))
                        {
                            myanmarSyllable = RomanOnsetsLongestFirst[o].MyanmarOnset + RomanRhymesLongestFirst[r].MyanmarRhyme;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string NormalizeMyG2pRomanization(string rawRoman)
        {
            if (string.IsNullOrWhiteSpace(rawRoman))
                return string.Empty;

            string[] tokens = rawRoman.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>(tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim().TrimEnd('.', ':', '-');
                if (tok.Length == 0 || tok == "...")
                    continue;

                // Convert myG2P glottal apostrophe into intuitive Latin final stop spelling
                if (tok.EndsWith("'", StringComparison.Ordinal))
                {
                    string stem = tok.Substring(0, tok.Length - 1);
                    if (stem.EndsWith("au", StringComparison.Ordinal) ||
                        stem.EndsWith("ou", StringComparison.Ordinal) ||
                        stem.EndsWith("ai", StringComparison.Ordinal) ||
                        stem.EndsWith("ei", StringComparison.Ordinal))
                    {
                        tok = stem + "k";
                    }
                    else
                    {
                        tok = stem + "t";
                    }
                }

                // Convert 'j' onset in myG2P (IPA /j/) to reader-friendly 'y' (unless 'nj')
                if (tok.StartsWith("j", StringComparison.Ordinal))
                    tok = "y" + tok.Substring(1);
                tok = tok.Replace("nj", "ny");

                cleaned.Add(tok);
            }

            return string.Join("-", cleaned);
        }

        private static string NormalizeMyG2pIpa(string rawIpa)
        {
            if (string.IsNullOrWhiteSpace(rawIpa))
                return string.Empty;

            string[] tokens = rawIpa.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(".", tokens);
        }

        private static string BuildSpokenBurmese(string normWord, string myG2pRomanHint)
        {
            string expanded = ExpandStackedClusters(normWord);
            var tokens = MyanmarSyllableBreaker.Tokenize(expanded);
            var syllables = new List<string>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.MyanmarSyllable)
                    syllables.Add(tokens[i].NormalizedText);
            }

            if (syllables.Count == 0)
                return string.Empty;

            string[] romanTokens = string.IsNullOrWhiteSpace(myG2pRomanHint)
                ? Array.Empty<string>()
                : myG2pRomanHint.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var spokenParts = new List<string>(syllables.Count);
            bool prevOpenOrNasal = false;

            for (int i = 0; i < syllables.Count; i++)
            {
                string syl = syllables[i];
                bool voiceFromHint = false;
                bool schwaFromHint = false;

                if (romanTokens.Length == syllables.Count)
                {
                    string rt = romanTokens[i];
                    voiceFromHint = rt.StartsWith("g", StringComparison.Ordinal) ||
                                    rt.StartsWith("z", StringComparison.Ordinal) ||
                                    rt.StartsWith("d", StringComparison.Ordinal) ||
                                    rt.StartsWith("b", StringComparison.Ordinal);
                    schwaFromHint = rt.EndsWith("-", StringComparison.Ordinal);
                }

                bool shouldVoice = romanTokens.Length == syllables.Count
                    ? voiceFromHint
                    : (i > 0 && prevOpenOrNasal);

                spokenParts.Add(RespellSingleBurmeseSyllable(syl, shouldVoice, schwaFromHint));
                prevOpenOrNasal = IsVoiceTriggeringSyllable(syl);
            }

            return string.Join("-", spokenParts);
        }

        private static MyanmarPronunciationInfo TranscribeAlgorithmically(string normWord, bool applyVoicingSandhi)
        {
            string expanded = ExpandStackedClusters(normWord);
            var tokens = MyanmarSyllableBreaker.Tokenize(expanded);
            var syllables = new List<string>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.MyanmarSyllable)
                    syllables.Add(tokens[i].NormalizedText);
            }

            if (syllables.Count == 0)
                return default;

            var spokenList = new List<string>(syllables.Count);
            var romanList = new List<string>(syllables.Count);
            var ipaList = new List<string>(syllables.Count);
            bool prevOpenOrNasal = false;

            for (int i = 0; i < syllables.Count; i++)
            {
                string syl = syllables[i];
                bool voice = applyVoicingSandhi && i > 0 && prevOpenOrNasal &&
                             !(i > 0 && string.Equals(syllables[i - 1], syl, StringComparison.Ordinal));

                spokenList.Add(RespellSingleBurmeseSyllable(syl, voice, schwaReduced: false));
                TranscribeSingleSyllableToRomanAndIpa(syl, voice, out string rom, out string ipa);
                romanList.Add(rom);
                ipaList.Add(ipa);

                prevOpenOrNasal = IsVoiceTriggeringSyllable(syl);
            }

            return new MyanmarPronunciationInfo(
                string.Join("-", spokenList),
                string.Join("-", romanList),
                string.Join(".", ipaList));
        }

        private static string ExpandStackedClusters(string word)
        {
            string s = word;
            if (s.IndexOf('\u100A') > 0)
            {
                // Burmese 'ပညာ' -> 'ပျင်ညာ' medial-palatal gemination
                s = s.Replace("ပညာ", "ပျင်ညာ");
            }
            if (s.IndexOf('\u1039') >= 0)
            {
                s = KinziExpandRegex.Replace(s, "\u1004\u103A$1");
                s = PatSintExpandRegex.Replace(s, "$1\u103A$2");
            }
            return s;
        }

        private static bool IsVoiceTriggeringSyllable(string syl)
        {
            if (string.IsNullOrEmpty(syl))
                return false;

            // Checked glottal-stop codas (-က်, -တ်, -ပ်, -စ်) block intervocalic voicing on the following syllable!
            if (syl.EndsWith("\u1000\u103A", StringComparison.Ordinal) ||
                syl.EndsWith("\u1010\u103A", StringComparison.Ordinal) ||
                syl.EndsWith("\u1015\u103A", StringComparison.Ordinal) ||
                syl.EndsWith("\u1005\u103A", StringComparison.Ordinal) ||
                syl.EndsWith("\u1002\u103A", StringComparison.Ordinal) ||
                syl.EndsWith("\u1012\u103A", StringComparison.Ordinal) ||
                syl.EndsWith("\u1017\u103A", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static string RespellSingleBurmeseSyllable(string syl, bool voiceOnset, bool schwaReduced)
        {
            if (string.IsNullOrEmpty(syl))
                return syl;

            string res = syl;

            // Silent final -လ်/-ဟ်/-ရ် after -ို (e.g. သိုလ် -> သို, ဗိုလ် -> ဗို)
            if (res.Length >= 4 && res.EndsWith("\u103A", StringComparison.Ordinal))
            {
                char coda = res[res.Length - 2];
                if ((coda == 'လ' || coda == 'ဟ' || coda == 'ရ') &&
                    res.IndexOf("\u102D\u102F", StringComparison.Ordinal) >= 0)
                {
                    res = res.Substring(0, res.Length - 2);
                }
            }

            if (voiceOnset && res.Length > 0 && res.IndexOf('\u103E') < 0)
            {
                char c0 = res[0];
                char voiced = c0;
                switch (c0)
                {
                    case 'က':
                    case 'ခ': voiced = 'ဂ'; break;
                    case 'စ':
                    case 'ဆ': voiced = 'ဇ'; break;
                    case 'တ':
                    case 'ထ':
                    case 'ဋ':
                    case 'ဌ': voiced = 'ဒ'; break;
                    case 'ပ':
                    case 'ဖ': voiced = 'ဗ'; break;
                }

                if (voiced != c0)
                {
                    // ကွ/ကျ/ကြ -> ဂွ/ဂျ/ဂျ
                    res = voiced + res.Substring(1);
                }
            }

            if (schwaReduced && res.Length == 1)
                return res;

            return res;
        }

        private static void TranscribeSingleSyllableToRomanAndIpa(string syl, bool voiceOnset, out string roman, out string ipa)
        {
            roman = string.Empty;
            ipa = string.Empty;
            if (string.IsNullOrEmpty(syl))
                return;

            // Handle standalone independent vowels first
            switch (syl)
            {
                case "ဣ": roman = "i"; ipa = "ʔḭ"; return;
                case "ဤ": roman = "i"; ipa = "ʔì"; return;
                case "ဥ": roman = "u"; ipa = "ʔṵ"; return;
                case "ဦ": roman = "u"; ipa = "ʔù"; return;
                case "ဦး": roman = "u"; ipa = "ʔú"; return;
                case "ဧ": roman = "e"; ipa = "ʔè"; return;
                case "ဩ": roman = "aw"; ipa = "ʔɔ́"; return;
                case "ဪ": roman = "aw"; ipa = "ʔɔ̀"; return;
                case "၏": roman = "i"; ipa = "ʔḭ"; return;
                case "၍": roman = "ywe"; ipa = "jwḛ"; return;
                case "၌": roman = "hnaik"; ipa = "n̥aɪʔ"; return;
                case "၎င်း": roman = "la-gaung"; ipa = "lə.gáʊɴ"; return;
            }

            char baseC = syl[0];
            int idx = 1;
            bool hasYa = false, hasRa = false, hasWa = false, hasHa = false;
            while (idx < syl.Length)
            {
                char m = syl[idx];
                if (m == '\u103B') { hasYa = true; idx++; }
                else if (m == '\u103C') { hasRa = true; idx++; }
                else if (m == '\u103D') { hasWa = true; idx++; }
                else if (m == '\u103E') { hasHa = true; idx++; }
                else break;
            }

            string rhymePart = syl.Substring(idx);
            MapOnset(baseC, hasYa, hasRa, hasWa, hasHa, voiceOnset && !hasHa, out string romOnset, out string ipaOnset);
            MapRhyme(rhymePart, out string romRhyme, out string ipaRhyme);

            roman = romOnset + romRhyme;
            ipa = ipaOnset + ipaRhyme;
        }

        private static void MapOnset(
            char baseC,
            bool hasYa,
            bool hasRa,
            bool hasWa,
            bool hasHa,
            bool voice,
            out string rom,
            out string ipa)
        {
            bool palatal = hasYa || hasRa;
            rom = "";
            ipa = "";

            if (palatal)
            {
                switch (baseC)
                {
                    case 'က': rom = voice ? "gy" : "ky"; ipa = voice ? "dʑ" : "tɕ"; break;
                    case 'ခ': rom = voice ? "gy" : "ch"; ipa = voice ? "dʑ" : "tɕʰ"; break;
                    case 'ဂ':
                    case 'ဃ': rom = "gy"; ipa = "dʑ"; break;
                    case 'င': rom = hasHa ? "hny" : "ny"; ipa = hasHa ? "ɲ̊" : "ɲ"; break;
                    case 'ပ': rom = voice ? "by" : "py"; ipa = voice ? "bj" : "pj"; break;
                    case 'ဖ': rom = voice ? "by" : "hpy"; ipa = voice ? "bj" : "pʰj"; break;
                    case 'ဗ':
                    case 'ဘ': rom = "by"; ipa = "bj"; break;
                    case 'မ': rom = hasHa ? "hmy" : "my"; ipa = hasHa ? "m̥j" : "mj"; break;
                    case 'လ': rom = hasHa ? "sh" : "ly"; ipa = hasHa ? "ʃ" : "j"; break;
                    case 'သ': rom = "thy"; ipa = "θj"; break;
                    default: rom = "y"; ipa = hasHa ? "ʃ" : "j"; break;
                }
            }
            else
            {
                switch (baseC)
                {
                    case 'က': rom = voice ? "g" : "k"; ipa = voice ? "g" : "k"; break;
                    case 'ခ': rom = voice ? "g" : "kh"; ipa = voice ? "g" : "kʰ"; break;
                    case 'ဂ':
                    case 'ဃ': rom = "g"; ipa = "g"; break;
                    case 'င': rom = hasHa ? "hng" : "ng"; ipa = hasHa ? "ŋ̊" : "ŋ"; break;
                    case 'စ': rom = voice ? "z" : "s"; ipa = voice ? "z" : "s"; break;
                    case 'ဆ': rom = voice ? "z" : "hs"; ipa = voice ? "z" : "sʰ"; break;
                    case 'ဇ':
                    case 'ဈ': rom = "z"; ipa = "z"; break;
                    case 'ဉ':
                    case 'ည': rom = hasHa ? "hny" : "ny"; ipa = hasHa ? "ɲ̊" : "ɲ"; break;
                    case 'ဋ':
                    case 'တ': rom = voice ? "d" : "t"; ipa = voice ? "d" : "t"; break;
                    case 'ဌ':
                    case 'ထ': rom = voice ? "d" : "ht"; ipa = voice ? "d" : "tʰ"; break;
                    case 'ဍ':
                    case 'ဎ':
                    case 'ဒ':
                    case 'ဓ': rom = "d"; ipa = "d"; break;
                    case 'ဏ':
                    case 'န': rom = hasHa ? "hn" : "n"; ipa = hasHa ? "n̥" : "n"; break;
                    case 'ပ': rom = voice ? "b" : "p"; ipa = voice ? "b" : "p"; break;
                    case 'ဖ': rom = voice ? "b" : "hp"; ipa = voice ? "b" : "pʰ"; break;
                    case 'ဗ':
                    case 'ဘ': rom = "b"; ipa = "b"; break;
                    case 'မ': rom = hasHa ? "hm" : "m"; ipa = hasHa ? "m̥" : "m"; break;
                    case 'ယ': rom = hasHa ? "sh" : "y"; ipa = hasHa ? "ʃ" : "j"; break;
                    case 'ရ': rom = hasHa ? "sh" : "y"; ipa = hasHa ? "ʃ" : "j"; break;
                    case 'လ':
                    case 'ဠ': rom = hasHa ? "hl" : "l"; ipa = hasHa ? "l̥" : "l"; break;
                    case 'ဝ': rom = hasHa ? "hw" : "w"; ipa = hasHa ? "w̥" : "w"; break;
                    case 'သ':
                    case 'ဿ': rom = voice ? "dh" : "th"; ipa = voice ? "ð" : "θ"; break;
                    case 'ဟ': rom = "h"; ipa = "h"; break;
                    case 'အ': rom = ""; ipa = "ʔ"; break;
                    default: rom = ""; ipa = ""; break;
                }
            }

            if (hasWa)
            {
                rom += "w";
                ipa += "w";
            }
        }

        private static void MapRhyme(string r, out string rom, out string ipa)
        {
            if (string.IsNullOrEmpty(r))
            {
                rom = "a";
                ipa = "a̰";
                return;
            }

            // Strip tone marks to classify core vowel/coda, while preserving tone for IPA
            bool creaky = r.IndexOf('\u1037') >= 0;
            bool high = r.IndexOf('\u1038') >= 0;
            string core = r.Replace("\u1037", string.Empty).Replace("\u1038", string.Empty);

            switch (core)
            {
                case "\u102B":
                case "\u102C":
                    rom = "a"; ipa = high ? "á" : (creaky ? "a̰" : "à"); return;
                case "\u102D":
                    rom = "i"; ipa = "ḭ"; return;
                case "\u102E":
                    rom = "i"; ipa = high ? "í" : (creaky ? "ḭ" : "ì"); return;
                case "\u102F":
                    rom = "u"; ipa = "ṵ"; return;
                case "\u1030":
                    rom = "u"; ipa = high ? "ú" : (creaky ? "ṵ" : "ù"); return;
                case "\u1031":
                    rom = "e"; ipa = high ? "é" : (creaky ? "ḛ" : "è"); return;
                case "\u1032":
                    rom = "e"; ipa = high ? "ɛ́" : (creaky ? "ɛ̰" : "ɛ̀"); return;
                case "\u1031\u102B":
                case "\u1031\u102C":
                    rom = "aw"; ipa = "ɔ́"; return;
                case "\u1031\u102B\u103A":
                case "\u1031\u102C\u103A":
                    rom = "aw"; ipa = creaky ? "ɔ̰" : "ɔ̀"; return;
                case "\u102D\u102F":
                case "\u102D\u102F\u101C\u103A":
                case "\u102D\u102F\u101F\u103A":
                case "\u102D\u102F\u101B\u103A":
                    rom = "o"; ipa = high ? "ó" : (creaky ? "o̰" : "ò"); return;
                case "\u1000\u103A":
                    rom = "et"; ipa = "ɛʔ"; return;
                case "\u1005\u103A":
                    rom = "it"; ipa = "ɪʔ"; return;
                case "\u1010\u103A":
                case "\u1015\u103A":
                case "\u102B\u1010\u103A":
                case "\u102C\u1010\u103A":
                case "\u102B\u1015\u103A":
                case "\u102C\u1015\u103A":
                case "\u1012\u103A":
                case "\u1017\u103A":
                    rom = "at"; ipa = "aʔ"; return;
                case "\u102D\u1010\u103A":
                case "\u102D\u1015\u103A":
                    rom = "eik"; ipa = "eɪʔ"; return;
                case "\u102F\u1010\u103A":
                case "\u102F\u1015\u103A":
                case "\u102F\u1002\u103A":
                    rom = "ouk"; ipa = "oʊʔ"; return;
                case "\u1031\u102B\u1000\u103A":
                case "\u1031\u102C\u1000\u103A":
                    rom = "auk"; ipa = "aʊʔ"; return;
                case "\u102D\u102F\u1000\u103A":
                    rom = "aik"; ipa = "aɪʔ"; return;
                case "\u1004\u103A":
                    rom = "in"; ipa = high ? "ɪ́ɴ" : (creaky ? "ɪ̰ɴ" : "ɪ̀ɴ"); return;
                case "\u1009\u103A":
                    rom = "in"; ipa = high ? "ɪ́ɴ" : (creaky ? "ɪ̰ɴ" : "ɪ̀ɴ"); return;
                case "\u100A\u103A":
                    rom = "i"; ipa = high ? "í" : (creaky ? "ḭ" : "ì"); return;
                case "\u101A\u103A":
                    rom = "e"; ipa = "ɛ̀"; return;
                case "\u1014\u103A":
                case "\u1019\u103A":
                case "\u1036":
                case "\u102B\u1014\u103A":
                case "\u102C\u1014\u103A":
                case "\u102B\u1019\u103A":
                case "\u102C\u1019\u103A":
                case "\u100F\u103A":
                    rom = "an"; ipa = high ? "áɴ" : (creaky ? "a̰ɴ" : "àɴ"); return;
                case "\u102D\u1014\u103A":
                case "\u102D\u1019\u103A":
                case "\u102D\u1036":
                    rom = "ein"; ipa = high ? "éɪɴ" : (creaky ? "ḛɪɴ" : "èɪɴ"); return;
                case "\u102F\u1014\u103A":
                case "\u102F\u1019\u103A":
                case "\u102F\u1036":
                    rom = "on"; ipa = high ? "óʊɴ" : (creaky ? "o̰ʊɴ" : "òʊɴ"); return;
                case "\u1031\u102B\u1004\u103A":
                case "\u1031\u102C\u1004\u103A":
                    rom = "aung"; ipa = high ? "áʊɴ" : (creaky ? "a̰ʊɴ" : "àʊɴ"); return;
                case "\u102D\u102F\u1004\u103A":
                    rom = "aing"; ipa = high ? "áɪɴ" : (creaky ? "a̰ɪɴ" : "àɪɴ"); return;
                default:
                    rom = "a"; ipa = "ə"; return;
            }
        }

        private static bool ContainsMyanmarLetters(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '\u1000' && c <= '\u103F')
                    return true;
            }
            return false;
        }
    }
}
