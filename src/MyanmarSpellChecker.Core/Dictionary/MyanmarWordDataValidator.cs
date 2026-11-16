using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Dictionary
{
    public enum WordViolationKind
    {
        None = 0,
        EmptyOrMalformedWord,
        ZeroWidthOrControlCharacter,
        ZawgyiOrMojibakeEncoding,
        LookalikeOrNonCanonicalOrder,
        InvalidSyllablePhonotactics,
        OrthographyRuleContradiction,
        RuleChainOrSelfCycle,
        MissingTargetHeadword,
        GluedParticleOrPhraseLeak,
        MissingOrMisclassifiedPosTag,
        CorruptedGlossOrEtymology,
        InvalidBigramToken
    }

    public sealed class WordDataViolation
    {
        public WordViolationKind Kind { get; set; }
        public string Table { get; set; } = string.Empty;
        public string ItemKey { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string? RepairedValue { get; set; }
    }

    public sealed class WordDataValidationReport
    {
        public int TotalEntriesChecked { get; set; }
        public int UniqueHeadwords { get; set; }
        public int OfficialThatponHeadwords { get; set; }
        public int ActiveSpellcheckHeadwords { get; set; }
        public int ReferenceEntries { get; set; }
        public int OrthographyRulesChecked { get; set; }
        public int ContextRulesChecked { get; set; }
        public int WordBigramsChecked { get; set; }

        public int AutoRepairedCount { get; set; }
        public int PurgedNoiseCount { get; set; }
        public int PosBackfilledCount { get; set; }
        public int GlossSanitizedCount { get; set; }
        public int RulesFlattenedCount { get; set; }
        public int OrphanBigramsPrunedCount { get; set; }

        public List<WordDataViolation> Violations { get; } = new List<WordDataViolation>();
        public Dictionary<WordViolationKind, int> ViolationCounts { get; } = new Dictionary<WordViolationKind, int>();

        public bool IsClean => Violations.Count == 0;

        public void RecordViolation(
            WordViolationKind kind,
            string table,
            string itemKey,
            string detail,
            int maxStoredViolations = 500,
            string? repairedValue = null)
        {
            ViolationCounts[kind] = ViolationCounts.TryGetValue(kind, out int prev) ? prev + 1 : 1;
            if (Violations.Count < maxStoredViolations)
            {
                Violations.Add(new WordDataViolation
                {
                    Kind = kind,
                    Table = table,
                    ItemKey = itemKey,
                    Detail = detail,
                    RepairedValue = repairedValue
                });
            }
        }

        public string FormatSummaryReport()
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine("========================================================================");
            sb.AppendLine(" Myanmar Word & Lexicon Data Validation Report (6-Gate Quality Shield)");
            sb.AppendLine("========================================================================");
            sb.AppendLine($"  Total Entries Audited        : {TotalEntriesChecked:N0} ({UniqueHeadwords:N0} unique headwords)");
            sb.AppendLine($"  Official Thatpon Headwords   : {OfficialThatponHeadwords:N0}");
            sb.AppendLine($"  Active Spellcheck Headwords  : {ActiveSpellcheckHeadwords:N0}");
            sb.AppendLine($"  Reference / Bilingual Rows   : {ReferenceEntries:N0}");
            sb.AppendLine($"  Orthography Rules Audited    : {OrthographyRulesChecked:N0}");
            sb.AppendLine($"  Contextual Rules Audited     : {ContextRulesChecked:N0}");
            sb.AppendLine($"  Empirical Bigrams Audited    : {WordBigramsChecked:N0}");
            if (AutoRepairedCount > 0 || PurgedNoiseCount > 0 || PosBackfilledCount > 0 ||
                GlossSanitizedCount > 0 || RulesFlattenedCount > 0 || OrphanBigramsPrunedCount > 0)
            {
                sb.AppendLine("------------------------------------------------------------------------");
                sb.AppendLine($"  Auto-Repaired / Rescued      : {AutoRepairedCount:N0}");
                sb.AppendLine($"  POS Tags Backfilled/Fixed    : {PosBackfilledCount:N0}");
                sb.AppendLine($"  Gloss / Etymology Sanitized  : {GlossSanitizedCount:N0}");
                sb.AppendLine($"  Rule Chains Flattened        : {RulesFlattenedCount:N0}");
                sb.AppendLine($"  Glued-Particle Noise Purged  : {PurgedNoiseCount:N0}");
                sb.AppendLine($"  Orphan Bigrams Pruned        : {OrphanBigramsPrunedCount:N0}");
            }
            sb.AppendLine("------------------------------------------------------------------------");
            int totalViolations = ViolationCounts.Values.Sum();
            sb.AppendLine($"  Validation Status            : {(IsClean ? "PASS (0 violations)" : $"FAIL ({totalViolations:N0} violation(s))")}");
            if (!IsClean)
            {
                foreach (var kv in ViolationCounts.OrderByDescending(k => k.Value))
                {
                    sb.AppendLine($"    - {kv.Key,-30}: {kv.Value:N0}");
                }
                sb.AppendLine("  Sample Violations:");
                for (int i = 0; i < Math.Min(15, Violations.Count); i++)
                {
                    var v = Violations[i];
                    sb.AppendLine($"    [{v.Table}] '{v.ItemKey}' ({v.Kind}): {v.Detail}");
                }
            }
            sb.AppendLine("========================================================================");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Layer-2 Authoritative Myanmar Word, Lexicon &amp; Corpus Data Validator and Sanitizer.
    /// Enforces 6 strict enterprise quality gates across all dictionary headwords, reference articles,
    /// orthography rules, contextual rules, and word bigrams — rescuing &amp; repairing recoverable entries
    /// while purging unverified corpus n-gram / attached-particle leaks and cross-table contradictions.
    /// </summary>
    public static class MyanmarWordDataValidator
    {
        private static readonly Regex ZeroWidthOrPuaRegex = new Regex(
            @"[\u200B-\u200F\u2060-\u206F\uFEFF\uE000-\uF8FF]",
            RegexOptions.Compiled);

        private static readonly Regex NonCanonicalCombiningSubseqRegex = new Regex(
            @"(?:[\u102B-\u1030\u1032\u1036][\u103B-\u103E]|\u103A\u103A|\u1039\u1039|\u1031\u1031|\u102D\u102E|\u102E\u102D|\u102F\u1030|\u1030\u102F|\u1037\u103A|\u1038\u103A|\u1038[\u102B-\u103E]|\u1036[\u102D-\u1030]|[\u102B-\u1038\u103A]\u103C|\u1008\u103B|\u100B\u103F\u100C)",
            RegexOptions.Compiled);

        private static readonly Regex SpaceBeforeCombiningMarkRegex = new Regex(
            @"(?<=[\u1000-\u102A])\s+(?=[\u102B-\u103E])",
            RegexOptions.Compiled);

        private static readonly Regex DigitZeroAdjacentWaRegex = new Regex(
            @"([\u1040-\u1049]+)\u101D(?=[\u1000-\u1021])",
            RegexOptions.Compiled);

        //Deterministic headword / encyclopedia title OCR & lookalike repairs (rescue instead of deleting!)
        private static readonly Dictionary<string, string> DeterministicHeadwordRepairs =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ကို်ယ်ရင့်တတူ"] = "ကိုယ်ရင့်တတူ",
                ["ကို်ယ်ရင့်တတူ"] = "ကိုယ်ရင့်တတူ",
                ["ဆိုးဇန်းဂျိူဘူတာ"] = "ဆိုးဇန်းဂျိဘူတာ",
                ["လိုင်လူမျိူး"] = "လိုင်လူမျိုး",
                ["နှစ် ၇၀ဝပြည့် အားကစားကွင်း"] = "နှစ် ၇၀၀ ပြည့် အားကစားကွင်း",
                ["နှစ် ၇၀ဝပြည့် အားကစားကွင်း"] = "နှစ် ၇၀၀ ပြည့် အားကစားကွင်း",
                ["သဿေရိဉဗရပါရမီ"] = "သဿေရိဥဗရပါရမီ",
                ["အဘ္ဘဒူ မာလစ်"] = "အဗ္ဘဒူ မာလစ်",
                ["တတ္စုယ ဖူဂျီဝရ"] = "တတ်စုယ ဖူဂျီဝရ",
                ["ဗှတ္တာ"] = "ဗက်တာ",
                ["ဘန်ဂျုးလ်"] = "ဘန်ဂျူးလ်",
                ["ဘန်ဂျုးလ်မြို့"] = "ဘန်ဂျူးလ်မြို့",
                ["မော်ဂါဒီးသျုး"] = "မော်ဂါဒီးရှူး",
                ["မော်ဂါဒီးသျုးမြို့"] = "မော်ဂါဒီးရှူးမြို့",
                ["ဂန္ဒီ"] = "ဂန္ဓီ"
            };

        // Non-Burmese Wiktionary language leak or unverified crowd slang headwords
        private static readonly HashSet<string> ExplicitNoiseHeadwords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "တဆံဃိး",       // Mon Wiktionary numeral ('eighteen') leaked into Burmese headwords
                "ကကျွတ်",
                "ကကျွမ်းကျင်",
                "ကကျွမ်းကျင်မှု",
                "ကနည်းနည်း",
                "ကတည်းကစ",
                "ကစ်ရယ်",
                "ကစ်ရေ",
                "ကစ်ကစ်ရယ်",
                "ကစ်ကစ်ရေ",
                "ကစ်လေးချစ်",
                "ကင်းဘူ",
                "ကြွေးစား"
            };

        // Classical Judson / Quranic / Arabic proper-noun transliteration clusters found in reference titles
        private static readonly HashSet<string> AllowedReferenceTransliterationSyllables =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "န္သု",
                "အအ်ရ်",
                "ဗ္ဒူ"
            };

        // Closed-class demonstratives / interrogatives / locatives that legitimately lexicalize with '-သို့'
        private static readonly HashSet<string> ClosedClassThoStems =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ဤ", "ထို", "ယင်း", "မည်", "အဘယ်", "မ", "ရှေ့", "နောက်", "အထက်", "အောက်"
            };

        // Trailing grammatical/colloquial particles that indicate an unspaced corpus phrase when attached to synthetic entries
        private static readonly string[] GluedClausalOrColloquialSuffixes =
        {
            "ရဲ့", "ပါစေ", "သော်လည်း", "ပေမယ့်", "ပေမဲ့",
            "ပါတယ်", "ပါသည်", "ခဲ့တယ်", "ခဲ့သည်", "ကြတယ်", "ကြသည်",
            "ပါနဲ့", "ပါနှင့်", "လိမ့်မည်", "လိမ့်မယ်",
            "တော့မယ်", "တော့မည်", "ဦးမယ်", "ဦးမည်",
            "ပါလား", "ပါပဲ", "ပါဘဲ", "သေးတယ်", "သေးသည်"
        };

        public static bool IsReferenceTierPosTag(string? posTag)
        {
            if (string.IsNullOrEmpty(posTag))
                return false;
            return string.Equals(posTag, "EN_LEX", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "ENCYC", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "PROVERB", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "SAYING", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "RHETORIC", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsClosedClassPosTag(string? posTag)
        {
            if (string.IsNullOrEmpty(posTag))
                return false;
            return string.Equals(posTag, "PART", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "POST", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "CONJ", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(posTag, "PRON", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSyntheticAutoGloss(string? meaningCue)
        {
            if (string.IsNullOrWhiteSpace(meaningCue))
                return true;
            string mc = meaningCue!;
            if (mc.IndexOf("ပေါင်းစပ်ဝေါဟာရ —", StringComparison.Ordinal) >= 0 ||
                mc.IndexOf("ပေါင်းစပ်ထားသော မြန်မာဝေါဟာရ", StringComparison.Ordinal) >= 0)
            {
                return true;
            }
            if (mc.StartsWith("ခေတ်သုံး မြန်မာစာပေနှ", StringComparison.Ordinal) ||
                mc == "ပြုခြင်း၊ ဖြစ်ခြင်း၊ ရှိခြင်းကို ဖော်ပြသော မြန်မာကြိယာဝေါဟာရ။" ||
                mc == "အရည်အသွေး သို့မဟုတ် အခြင်းအရာကို အထူးပြုဖော်ပြသော မြန်မာဝေါဟာရ။" ||
                mc == "ခေတ်သုံး မြန်မာဝေါဟာရ" ||
                mc == "မြန်မာပေါင်းစပ်ဝေါဟာရ" ||
                mc == "ခေတ်သုံး စံသတ်ပုံအမှန်")
            {
                return true;
            }
            if (mc.StartsWith("ပါဠိရင်းမြစ် မြန်မာဝေါဟာရ (", StringComparison.Ordinal) ||
                mc.StartsWith("မြန်မာဝေါဟာရ (ပါဠိအနက် —", StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Validates all Myanmar syllables in a headword or reference title against UTN #11 and Burmese/Pali phonotactics,
        /// accounting for canonical classical ligatures ('ယောက်ျား', 'ကျွန်ုပ်') and foreign '-ch/-j' loanword codas ('ဒတ်ခ်ျ', 'ဂျော့ဂ်ျ').
        /// </summary>
        public static bool ValidateWordSyllables(
            string word,
            bool allowReferenceTransliteration,
            out string invalidSyllable,
            out string errorReason,
            MyanmarTrie? trie = null)
        {
            invalidSyllable = string.Empty;
            errorReason = string.Empty;

            if (string.IsNullOrWhiteSpace(word))
            {
                errorReason = "Headword is empty or whitespace.";
                return false;
            }

            // Classical Judson spelling of 'ကျွန်ုပ်' attested in Official Thatpon
            if (word == "ကျွနု်ပ်")
                return true;

            List<SyllableToken> tokens = MyanmarSyllableBreaker.Tokenize(word);
            int count = tokens.Count;
            for (int k = 0; k < count; k++)
            {
                if (tokens[k].Kind != TokenKind.MyanmarSyllable)
                    continue;

                string raw = tokens[k].Text;
                if (k > 0)
                {
                    string prevNorm = tokens[k - 1].NormalizedText;
                    if ((raw == "\u103B\u102C" || raw == "\u103B\u102C\u1038" || raw == "\u103B\u102C\u1037") &&
                        prevNorm.EndsWith("\u1000\u103A", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if ((raw == "\u102F\u1015\u103A" ||
                         (raw == "\u102F" && k + 1 < count && tokens[k + 1].NormalizedText.StartsWith("\u1015\u103A", StringComparison.Ordinal))) &&
                        prevNorm.EndsWith("\u1014\u103A", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (raw == "\u103B" &&
                        (prevNorm.EndsWith("\u1001\u103A", StringComparison.Ordinal) ||
                         prevNorm.EndsWith("\u1002\u103A", StringComparison.Ordinal) ||
                         prevNorm.EndsWith("\u1017\u103A", StringComparison.Ordinal) ||
                         prevNorm.EndsWith("\u1007\u103A", StringComparison.Ordinal)))
                    {
                        continue;
                    }
                }

                if (allowReferenceTransliteration && AllowedReferenceTransliterationSyllables.Contains(raw))
                    continue;

                if (!MyanmarSyllableValidator.ValidateSyllable(raw, out errorReason, out _, trie))
                {
                    invalidSyllable = raw;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Detects unverified corpus n-gram or attached-particle phrase leaks that should not sit as active dictionary headwords.
        /// </summary>
        public static bool IsGluedParticleOrNoiseEntry(
            DictionaryEntry entry,
            HashSet<string>? officialWords,
            out string reason)
        {
            reason = string.Empty;
            if (entry == null || string.IsNullOrEmpty(entry.Word))
            {
                reason = "Empty word.";
                return true;
            }

            if (entry.IsOfficialThatpon ||
                IsReferenceTierPosTag(entry.PosTag) ||
                IsClosedClassPosTag(entry.PosTag))
            {
                return false;
            }

            string w = entry.Word;
            if (ExplicitNoiseHeadwords.Contains(w))
            {
                reason = $"Explicitly quarantined corpus/language noise headword '{w}'.";
                return true;
            }

            string mc = entry.MeaningCue ?? string.Empty;
            string un = entry.UsageNote ?? string.Empty;
            bool isSynthetic = IsSyntheticAutoGloss(mc);
            bool isLoanOrPropn = string.Equals(entry.PosTag, "LOAN", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(entry.PosTag, "PROPN", StringComparison.OrdinalIgnoreCase);

            // 1. Unconditional clausal/colloquial endings on non-official open-class entries
            for (int i = 0; i < GluedClausalOrColloquialSuffixes.Length; i++)
            {
                string suf = GluedClausalOrColloquialSuffixes[i];
                if (w.Length > suf.Length + 1 && w.EndsWith(suf, StringComparison.Ordinal))
                {
                    reason = $"Trailing clausal/colloquial particle '{suf}' glued onto stem.";
                    return true;
                }
            }

            if (!isSynthetic)
                return false;

            // 2. Synthetic entries with attached literary genitive '၏', colloquial causal 'လို့', directional 'သို့', or vocative 'ရယ်'
            if (w.Length > 2 && w.EndsWith("၏", StringComparison.Ordinal))
            {
                reason = "Trailing genitive particle '၏' glued onto synthetic headword.";
                return true;
            }

            if (w.Length > 4 && w.EndsWith("လို့", StringComparison.Ordinal))
            {
                reason = "Trailing colloquial particle 'လို့' glued onto synthetic headword.";
                return true;
            }

            if (w.Length > 4 && w.EndsWith("သို့", StringComparison.Ordinal))
            {
                string stem = w.Substring(0, w.Length - "သို့".Length);
                if (!ClosedClassThoStems.Contains(stem))
                {
                    reason = "Trailing directional particle 'သို့' glued onto open-class synthetic headword.";
                    return true;
                }
            }

            if (!isLoanOrPropn &&
                un.IndexOf("အင်္ဂလိပ်", StringComparison.Ordinal) < 0 &&
                w.Length > 4 &&
                w.EndsWith("ရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("ပြုံးရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("ရယ်ရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("ဖက်ဒရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("ရီရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("နရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("ထရယ်", StringComparison.Ordinal) &&
                !w.EndsWith("ဗီရယ်", StringComparison.Ordinal))
            {
                reason = "Trailing vocative/colloquial particle 'ရယ်' glued onto synthetic headword.";
                return true;
            }

            // 3. Leading subject particle 'က' glued to a >=2-syllable Official word on low-frequency synthetic entry ('ကကျွမ်းကျင်')
            if (!isLoanOrPropn &&
                entry.Frequency <= 5 &&
                un.IndexOf("နယ်ပယ်:", StringComparison.Ordinal) < 0 &&
                w.Length >= 6 &&
                w[0] == 'က')
            {
                var syls = MyanmarSyllableBreaker.Tokenize(w);
                if (syls.Count >= 3 && syls[0].Text == "က")
                {
                    string rest = w.Substring(1);
                    if (officialWords != null && officialWords.Contains(rest) &&
                        !w.StartsWith("ကချော်", StringComparison.Ordinal) &&
                        !w.StartsWith("ကတုန်", StringComparison.Ordinal) &&
                        !w.StartsWith("ကပြောင်း", StringComparison.Ordinal) &&
                        !w.StartsWith("ကရော်", StringComparison.Ordinal))
                    {
                        reason = $"Leading subject particle 'က' glued onto multi-syllable stem '{rest}'.";
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Infers a clean grammatical PosTag when an entry has an empty PosTag or a crowd-mislabeled tag.
        /// </summary>
        public static string InferOrRepairPosTag(string word, string? currentPos, string? meaningCue)
        {
            string pos = (currentPos ?? string.Empty).Trim();
            string mc = meaningCue ?? string.Empty;

            if (word == "ကားဘီး" && pos == "ADJ")
                return "N";

            if (!string.IsNullOrEmpty(pos))
                return pos;

            if (word.EndsWith("ခနဲ", StringComparison.Ordinal) ||
                word.EndsWith("ကနဲ", StringComparison.Ordinal))
            {
                return "ADV";
            }

            if (mc.EndsWith("သည်။", StringComparison.Ordinal) &&
                mc.IndexOf("ဝေါဟာရ", StringComparison.Ordinal) < 0 &&
                !word.EndsWith("ခြင်း", StringComparison.Ordinal) &&
                !word.EndsWith("မှု", StringComparison.Ordinal))
            {
                return "V";
            }

            return "N";
        }

        /// <summary>
        /// Sanitizes U Hoke Sein Pali equivalent notes and WinInnwa OCR artifacts in MeaningCue and UsageNote.
        /// </summary>
        public static bool SanitizeGlossAndEtymologyFields(DictionaryEntry entry)
        {
            if (entry == null)
                return false;

            bool changed = false;
            string mc = entry.MeaningCue ?? string.Empty;
            string un = entry.UsageNote ?? string.Empty;

            // 1. Repair specific corrupted 'ကက်တိုင်ယွန်' (WinInnwa 'နျယန' inside UHS Pali column)
            if (entry.Word == "ကက်တိုင်ယွန်")
            {
                if (mc.IndexOf("နျယန", StringComparison.Ordinal) >= 0 || mc.IndexOf("ပါဠိမူရင်း", StringComparison.Ordinal) >= 0)
                {
                    mc = "လျှပ်စစ်အဖိုဓာတ်ဆောင် အိုင်းယွန်း (Cation)။";
                    changed = true;
                }
                if (un.IndexOf("ပါဠိအနက်: နျယန", StringComparison.Ordinal) >= 0)
                {
                    un = un.Replace("ပါဠိအနက်: နျယန | ", "").Replace(" | ပါဠိအနက်: နျယန", "").Replace("ပါဠိအနက်: နျယန", "").Trim();
                    changed = true;
                }
            }

            // 2. Fix WinInnwa '၎' -> 'ဌ/ဋ' OCR corruptions & stray ']ာ' / space-before-combining-mark inside Pali notes
            if (!string.IsNullOrEmpty(un) && un.IndexOf("ပါဠိအနက်:", StringComparison.Ordinal) >= 0)
            {
                string fixedUn = un
                    .Replace("၎ာန", "ဌာန")
                    .Replace("၎ာပယတိ", "ဌာပယတိ")
                    .Replace("၎ပေတိ", "ဌပေတိ")
                    .Replace("က၎ိန", "ကဋိန")
                    .Replace("]ာ", "");
                fixedUn = SpaceBeforeCombiningMarkRegex.Replace(fixedUn, "");
                fixedUn = UnicodeNormalizer.NormalizeCanonicalOrder(fixedUn);
                if (!string.Equals(fixedUn, un, StringComparison.Ordinal))
                {
                    un = fixedUn;
                    changed = true;
                }
            }

            // 3. Relabel misleading 'ပါဠိရင်းမြစ် မြန်မာဝေါဟာရ (ပါဠိမူရင်း — ...)' on native Burmese / English loanword stems
            const string oldPaliPrefix = "ပါဠိရင်းမြစ် မြန်မာဝေါဟာရ (ပါဠိမူရင်း — ";
            if (mc.StartsWith(oldPaliPrefix, StringComparison.Ordinal))
            {
                string updatedMc = "မြန်မာဝေါဟာရ (ပါဠိအနက် — " + mc.Substring(oldPaliPrefix.Length);
                updatedMc = updatedMc
                    .Replace("၎ာန", "ဌာန")
                    .Replace("၎ာပယတိ", "ဌာပယတိ")
                    .Replace("၎ပေတိ", "ဌပေတိ")
                    .Replace("က၎ိန", "ကဋိန")
                    .Replace("]ာ", "");
                updatedMc = SpaceBeforeCombiningMarkRegex.Replace(updatedMc, "");
                updatedMc = UnicodeNormalizer.NormalizeCanonicalOrder(updatedMc);
                mc = updatedMc;
                changed = true;
            }

            // 4. Repair 'ဗှတ္တာ' / 'ဗှတ္တမှာ' -> 'ဗက်တာ' / 'ဗက်တာမှာ' inside physics/math definitions
            if (mc.IndexOf("ဗှတ္တ", StringComparison.Ordinal) >= 0)
            {
                mc = UnicodeNormalizer.NormalizeCanonicalOrder(
                    mc.Replace("ဗှတ္တာ", "ဗက်တာ").Replace("ဗှတ္တမှာ", "ဗက်တာမှာ").Replace("ဗှတ္တ", "ဗက်တာ"));
                changed = true;
            }
            if (un.IndexOf("ဗှတ္တ", StringComparison.Ordinal) >= 0)
            {
                un = UnicodeNormalizer.NormalizeCanonicalOrder(
                    un.Replace("ဗှတ္တာ", "ဗက်တာ").Replace("ဗှတ္တမှာ", "ဗက်တာမှာ").Replace("ဗှတ္တ", "ဗက်တာ"));
                changed = true;
            }

            if (changed)
            {
                entry.MeaningCue = mc;
                entry.UsageNote = un;
            }
            return changed;
        }

        /// <summary>
        /// Performs a deterministic Repair-First + Purge-Noise pass on <paramref name="model"/> in place,
        /// then runs the full 6-Gate read-only audit to guarantee 0 remaining violations.
        /// </summary>
        public static WordDataValidationReport SanitizeAndValidateInPlace(MasterDictionaryJsonModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            int autoRepaired = 0;
            int purgedNoise = 0;
            int posBackfilled = 0;
            int glossSanitized = 0;
            int rulesFlattened = 0;
            int orphanBigramsPruned = 0;

            // Step 1: Canonicalize & flatten OrthographyRules (resolve A -> B -> C chains and drop A -> A)
            var cleanRules = new List<OrthographyRule>(model.OrthographyRules.Count);
            var ruleMap = new Dictionary<string, OrthographyRule>(StringComparer.Ordinal);
            foreach (var r in model.OrthographyRules)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.WrongForm) || string.IsNullOrWhiteSpace(r.CorrectForm))
                    continue;
                string wf = UnicodeNormalizer.NormalizeCanonicalOrder(ZeroWidthOrPuaRegex.Replace(r.WrongForm.Trim(), ""));
                string cf = UnicodeNormalizer.NormalizeCanonicalOrder(ZeroWidthOrPuaRegex.Replace(r.CorrectForm.Trim(), ""));
                if (wf.Length == 0 || cf.Length == 0 || string.Equals(wf, cf, StringComparison.Ordinal))
                    continue;
                if (!ruleMap.ContainsKey(wf))
                {
                    var nr = new OrthographyRule
                    {
                        Id = r.Id,
                        WrongForm = wf,
                        CorrectForm = cf,
                        Category = string.IsNullOrWhiteSpace(r.Category) ? "Spelling" : r.Category.Trim(),
                        Explanation = r.Explanation ?? string.Empty
                    };
                    ruleMap[wf] = nr;
                    cleanRules.Add(nr);
                }
            }

            for (int i = 0; i < cleanRules.Count; i++)
            {
                var r = cleanRules[i];
                if (string.Equals(r.Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase))
                    continue;
                int hops = 0;
                string target = r.CorrectForm;
                while (hops < 4 && ruleMap.TryGetValue(target, out var nextRule) &&
                       !string.Equals(nextRule.Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(nextRule.CorrectForm, r.WrongForm, StringComparison.Ordinal))
                {
                    target = nextRule.CorrectForm;
                    hops++;
                }
                if (hops > 0 && !string.Equals(target, r.CorrectForm, StringComparison.Ordinal))
                {
                    r.CorrectForm = target;
                    rulesFlattened++;
                }
            }
            model.OrthographyRules = cleanRules;

            var condemnedWrongToRight = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < cleanRules.Count; i++)
            {
                if (!string.Equals(cleanRules[i].Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase))
                    condemnedWrongToRight[cleanRules[i].WrongForm] = cleanRules[i].CorrectForm;
            }

            // Build initial Official Thatpon word set
            var officialWords = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < model.Entries.Count; i++)
            {
                var e = model.Entries[i];
                if (e != null && e.IsOfficialThatpon && !string.IsNullOrWhiteSpace(e.Word))
                {
                    string nw = UnicodeNormalizer.NormalizeCanonicalOrder(e.Word.Trim());
                    if (DeterministicHeadwordRepairs.TryGetValue(nw, out string? repOff))
                        nw = repOff;
                    officialWords.Add(nw);
                }
            }

            // Step 2: Repair, Rescue, Re-tag & Purge Entries
            var sanitizedEntries = new List<DictionaryEntry>(model.Entries.Count);
            var seenEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            var activeWordPrimaryMap = new Dictionary<string, DictionaryEntry>(StringComparer.Ordinal);

            for (int i = 0; i < model.Entries.Count; i++)
            {
                var e = model.Entries[i];
                if (e == null || string.IsNullOrWhiteSpace(e.Word))
                {
                    purgedNoise++;
                    continue;
                }

                string pos = (e.PosTag ?? string.Empty).Trim();
                bool isEnLex = string.Equals(pos, "EN_LEX", StringComparison.OrdinalIgnoreCase);
                bool isEncyc = string.Equals(pos, "ENCYC", StringComparison.OrdinalIgnoreCase);
                bool isRef = IsReferenceTierPosTag(pos);
                if (isRef && e.IsOfficialThatpon)
                    e.IsOfficialThatpon = false;

                string w = ZeroWidthOrPuaRegex.Replace(e.Word.Trim(), "");
                if (!isEnLex)
                {
                    w = UnicodeNormalizer.NormalizeCanonicalOrder(w);
                    if (DeterministicHeadwordRepairs.TryGetValue(w, out string? mappedRepair))
                    {
                        if (w == "ဂန္ဒီ")
                            pos = "PROPN";
                        w = UnicodeNormalizer.NormalizeCanonicalOrder(mappedRepair);
                        autoRepaired++;
                    }
                    else if (DigitZeroAdjacentWaRegex.IsMatch(w))
                    {
                        w = UnicodeNormalizer.NormalizeCanonicalOrder(DigitZeroAdjacentWaRegex.Replace(w, "$1\u1040 "));
                        autoRepaired++;
                    }
                }

                e.Word = w;

                // Rescue literary particle 'စေသတည်း' and postpositional connector 'ပတ်သက်လို့' as closed-class entries
                if (w == "စေသတည်း" && !e.IsOfficialThatpon && IsSyntheticAutoGloss(e.MeaningCue))
                {
                    pos = "PART";
                    e.MeaningCue = UnicodeNormalizer.NormalizeCanonicalOrder("ဆုတောင်း သို့မဟုတ် အဓိဋ္ဌာန် ဝါကျအဆုံးသတ် စာပေသုံးပစ္စည်း။");
                    posBackfilled++;
                }
                else if (w == "ပတ်သက်လို့" && !e.IsOfficialThatpon && IsSyntheticAutoGloss(e.MeaningCue))
                {
                    pos = "POST";
                    e.MeaningCue = UnicodeNormalizer.NormalizeCanonicalOrder("နှင့်စပ်လျဉ်း၍၊ နှင့်သက်ဆိုင်၍ (အပြောသုံး ဆက်စပ်ပစ္စည်း)။");
                    posBackfilled++;
                }

                string newPos = isEnLex ? "EN_LEX" : InferOrRepairPosTag(w, pos, e.MeaningCue);
                if (!string.Equals(newPos, pos, StringComparison.Ordinal))
                {
                    pos = newPos;
                    posBackfilled++;
                }
                e.PosTag = pos;

                if (SanitizeGlossAndEtymologyFields(e))
                    glossSanitized++;

                // Cross-table contradiction resolution:
                // - ENCYC articles titled with a WrongForm ('ဒီဇိုင်းနာ', 'မရိုးပင်') migrate to CorrectForm!
                // - Non-official active entries matching WrongForm are removed so they never shield typos!
                if (!e.IsOfficialThatpon && condemnedWrongToRight.TryGetValue(w, out string? rightForm))
                {
                    if (isEncyc)
                    {
                        string oldWrong = w;
                        w = rightForm;
                        e.Word = w;
                        if (!string.IsNullOrEmpty(e.MeaningCue) && e.MeaningCue.IndexOf(oldWrong, StringComparison.Ordinal) >= 0)
                        {
                            e.MeaningCue = UnicodeNormalizer.NormalizeCanonicalOrder(e.MeaningCue.Replace(oldWrong, rightForm));
                        }
                        autoRepaired++;
                    }
                    else
                    {
                        purgedNoise++;
                        continue;
                    }
                }

                // Anti-Glued-Particle & Noise Gate on non-official entries
                if (!isEnLex && IsGluedParticleOrNoiseEntry(e, officialWords, out _))
                {
                    purgedNoise++;
                    continue;
                }

                // Syllable & Lookalike Gate on Myanmar headwords & reference titles
                if (!isEnLex)
                {
                    if (UnicodeNormalizer.HasLookalikeError(w, out string fixedLookalike, out _) &&
                        !string.IsNullOrEmpty(fixedLookalike) &&
                        !string.Equals(fixedLookalike, w, StringComparison.Ordinal))
                    {
                        if (!condemnedWrongToRight.ContainsKey(fixedLookalike))
                        {
                            w = UnicodeNormalizer.NormalizeCanonicalOrder(fixedLookalike);
                            e.Word = w;
                            autoRepaired++;
                        }
                        else if (!e.IsOfficialThatpon)
                        {
                            purgedNoise++;
                            continue;
                        }
                    }

                    if (!ValidateWordSyllables(w, allowReferenceTransliteration: isRef, out string badSyl, out _))
                    {
                        // Attempt single-syllable deterministic repair before dropping non-official corrupted entry
                        if (MyanmarSyllableValidator.ValidateSyllable(badSyl, out _, out string? sylRep) &&
                            !string.IsNullOrEmpty(sylRep))
                        {
                            string candidateWord = UnicodeNormalizer.NormalizeCanonicalOrder(w.Replace(badSyl, sylRep));
                            if (ValidateWordSyllables(candidateWord, allowReferenceTransliteration: isRef, out _, out _) &&
                                !UnicodeNormalizer.HasLookalikeError(candidateWord, out _, out _))
                            {
                                w = candidateWord;
                                e.Word = w;
                                autoRepaired++;
                            }
                            else if (!e.IsOfficialThatpon)
                            {
                                purgedNoise++;
                                continue;
                            }
                        }
                        else if (!e.IsOfficialThatpon)
                        {
                            purgedNoise++;
                            continue;
                        }
                    }
                }

                // Deduplicate or merge frequencies when a repaired entry lands on an existing headword
                if (!isRef && w.IndexOf(' ') < 0 && activeWordPrimaryMap.TryGetValue(w, out var existingPrimary))
                {
                    if (e.Frequency > existingPrimary.Frequency)
                        existingPrimary.Frequency = e.Frequency;
                    if (e.IsOfficialThatpon)
                        existingPrimary.IsOfficialThatpon = true;

                    // Skip if incoming repaired duplicate only had a synthetic gloss while primary already has a real gloss
                    if (IsSyntheticAutoGloss(e.MeaningCue) && !IsSyntheticAutoGloss(existingPrimary.MeaningCue))
                        continue;
                }

                string dedupKey = $"{w}|{e.PosTag}|{e.MeaningCue}";
                if (!seenEntryKeys.Add(dedupKey))
                    continue;

                if (!isRef && w.IndexOf(' ') < 0 && !activeWordPrimaryMap.ContainsKey(w))
                    activeWordPrimaryMap[w] = e;

                sanitizedEntries.Add(e);
            }

            // Step 3: Guarantee 100% of single-word OrthographyRule & ContextRule CorrectForms exist in active lexicon
            for (int i = 0; i < model.OrthographyRules.Count; i++)
            {
                string cf = model.OrthographyRules[i].CorrectForm;
                if (!string.IsNullOrEmpty(cf) && cf.IndexOf(' ') < 0 && !activeWordPrimaryMap.ContainsKey(cf))
                {
                    var added = new DictionaryEntry
                    {
                        Word = cf,
                        PosTag = "N",
                        MeaningCue = officialWords.Contains(cf)
                            ? "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် စံသတ်ပုံအမှန် မြန်မာဝေါဟာရ။"
                            : "ခေတ်သုံး မြန်မာစာပေနှင့် ရုံးသုံး စံသတ်ပုံအမှန် နာမ်ဝေါဟာရ။",
                        Frequency = 25,
                        IsOfficialThatpon = officialWords.Contains(cf)
                    };
                    sanitizedEntries.Add(added);
                    activeWordPrimaryMap[cf] = added;
                }
            }

            if (model.ContextRules != null)
            {
                for (int i = 0; i < model.ContextRules.Count; i++)
                {
                    var cr = model.ContextRules[i];
                    cr.WrongForm = UnicodeNormalizer.NormalizeCanonicalOrder(ZeroWidthOrPuaRegex.Replace(cr.WrongForm.Trim(), ""));
                    cr.CorrectForm = UnicodeNormalizer.NormalizeCanonicalOrder(ZeroWidthOrPuaRegex.Replace(cr.CorrectForm.Trim(), ""));
                    if (!string.IsNullOrEmpty(cr.CorrectForm) &&
                        cr.CorrectForm.IndexOf(' ') < 0 &&
                        !condemnedWrongToRight.ContainsKey(cr.CorrectForm) &&
                        !activeWordPrimaryMap.ContainsKey(cr.CorrectForm))
                    {
                        var added = new DictionaryEntry
                        {
                            Word = cr.CorrectForm,
                            PosTag = "N",
                            MeaningCue = officialWords.Contains(cr.CorrectForm)
                                ? "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် စံသတ်ပုံအမှန် မြန်မာဝေါဟာရ။"
                                : "ခေတ်သုံး မြန်မာစာပေနှင့် ရုံးသုံး စံသတ်ပုံအမှန် နာမ်ဝေါဟာရ။",
                            Frequency = 25,
                            IsOfficialThatpon = officialWords.Contains(cr.CorrectForm)
                        };
                        sanitizedEntries.Add(added);
                        activeWordPrimaryMap[cr.CorrectForm] = added;
                    }
                }
            }

            model.Entries = sanitizedEntries
                .OrderBy(e => e.Word, StringComparer.Ordinal)
                .ToList();

            // Step 4: Prune any WordBigram referencing a condemned WrongForm or purged noise token
            if (model.WordBigrams != null && model.WordBigrams.Count > 0)
            {
                var cleanBigrams = new List<WordBigramEntry>(model.WordBigrams.Count);
                var seenBg = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < model.WordBigrams.Count; i++)
                {
                    var bg = model.WordBigrams[i];
                    if (bg == null || bg.Count <= 0 || string.IsNullOrWhiteSpace(bg.LeftWord) || string.IsNullOrWhiteSpace(bg.RightWord))
                    {
                        orphanBigramsPruned++;
                        continue;
                    }
                    string lw = UnicodeNormalizer.NormalizeCanonicalOrder(bg.LeftWord.Trim());
                    string rw = UnicodeNormalizer.NormalizeCanonicalOrder(bg.RightWord.Trim());
                    if (condemnedWrongToRight.ContainsKey(lw) ||
                        condemnedWrongToRight.ContainsKey(rw) ||
                        condemnedWrongToRight.ContainsKey(lw + rw) ||
                        !activeWordPrimaryMap.ContainsKey(lw) ||
                        !activeWordPrimaryMap.ContainsKey(rw))
                    {
                        orphanBigramsPruned++;
                        continue;
                    }
                    if (seenBg.Add(lw + "\t" + rw))
                    {
                        bg.LeftWord = lw;
                        bg.RightWord = rw;
                        cleanBigrams.Add(bg);
                    }
                }
                model.WordBigrams = cleanBigrams;
            }

            // Step 5: Run full read-only validation audit and attach sanitization telemetry
            WordDataValidationReport report = Validate(model);
            report.AutoRepairedCount = autoRepaired;
            report.PurgedNoiseCount = purgedNoise;
            report.PosBackfilledCount = posBackfilled;
            report.GlossSanitizedCount = glossSanitized;
            report.RulesFlattenedCount = rulesFlattened;
            report.OrphanBigramsPrunedCount = orphanBigramsPruned;
            return report;
        }

        /// <summary>
        /// Runs a strictly read-only 6-Gate audit across all Entries, OrthographyRules, ContextRules, and WordBigrams.
        /// </summary>
        public static WordDataValidationReport Validate(
            MasterDictionaryJsonModel model,
            MyanmarTrie? trie = null,
            int maxStoredViolations = 500)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var report = new WordDataValidationReport
            {
                TotalEntriesChecked = model.Entries?.Count ?? 0,
                OrthographyRulesChecked = model.OrthographyRules?.Count ?? 0,
                ContextRulesChecked = model.ContextRules?.Count ?? 0,
                WordBigramsChecked = model.WordBigrams?.Count ?? 0
            };

            var uniqueAll = new HashSet<string>(StringComparer.Ordinal);
            var officialWords = new HashSet<string>(StringComparer.Ordinal);
            var activeWords = new HashSet<string>(StringComparer.Ordinal);

            if (model.Entries != null)
            {
                for (int i = 0; i < model.Entries.Count; i++)
                {
                    var e = model.Entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.Word)) continue;
                    uniqueAll.Add(e.Word);
                    if (e.IsOfficialThatpon)
                        officialWords.Add(e.Word);
                    if (!IsReferenceTierPosTag(e.PosTag) && e.Word.IndexOf(' ') < 0)
                        activeWords.Add(e.Word);
                }
            }

            report.UniqueHeadwords = uniqueAll.Count;
            report.OfficialThatponHeadwords = officialWords.Count;
            report.ActiveSpellcheckHeadwords = activeWords.Count;

            var condemnedSet = new HashSet<string>(StringComparer.Ordinal);
            if (model.OrthographyRules != null)
            {
                for (int i = 0; i < model.OrthographyRules.Count; i++)
                {
                    var r = model.OrthographyRules[i];
                    if (r != null && !string.IsNullOrEmpty(r.WrongForm) &&
                        !string.Equals(r.Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase))
                    {
                        condemnedSet.Add(r.WrongForm);
                    }
                }
            }

            // Gate Audit 1: Entries
            if (model.Entries != null)
            {
                for (int i = 0; i < model.Entries.Count; i++)
                {
                    var e = model.Entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.Word))
                    {
                        report.RecordViolation(WordViolationKind.EmptyOrMalformedWord, "Entries", $"Row#{i}", "Empty headword.", maxStoredViolations);
                        continue;
                    }

                    string w = e.Word;
                    string pos = e.PosTag ?? string.Empty;
                    bool isRef = IsReferenceTierPosTag(pos);
                    bool isEnLex = string.Equals(pos, "EN_LEX", StringComparison.OrdinalIgnoreCase);
                    if (isRef)
                        report.ReferenceEntries++;

                    if (string.IsNullOrWhiteSpace(pos))
                    {
                        report.RecordViolation(WordViolationKind.MissingOrMisclassifiedPosTag, "Entries", w, "Empty PosTag.", maxStoredViolations);
                    }

                    if (ZeroWidthOrPuaRegex.IsMatch(w) ||
                        ZeroWidthOrPuaRegex.IsMatch(e.MeaningCue ?? string.Empty) ||
                        ZeroWidthOrPuaRegex.IsMatch(e.UsageNote ?? string.Empty))
                    {
                        report.RecordViolation(WordViolationKind.ZeroWidthOrControlCharacter, "Entries", w, "Zero-width or PUA character detected.", maxStoredViolations);
                    }

                    if (!e.IsOfficialThatpon && condemnedSet.Contains(w))
                    {
                        report.RecordViolation(WordViolationKind.OrthographyRuleContradiction, "Entries", w, $"Entry '{w}' ({pos}) contradicts OrthographyRules.WrongForm.", maxStoredViolations);
                    }

                    if (!isEnLex)
                    {
                        if (UnicodeNormalizer.FindZawgyiSpans(w).Count > 0)
                        {
                            report.RecordViolation(WordViolationKind.ZawgyiOrMojibakeEncoding, "Entries", w, "Zawgyi codepoint sequence in headword.", maxStoredViolations);
                        }

                        if (NonCanonicalCombiningSubseqRegex.IsMatch(w) ||
                            UnicodeNormalizer.HasLookalikeError(w, out _, out string lookDesc))
                        {
                            report.RecordViolation(WordViolationKind.LookalikeOrNonCanonicalOrder, "Entries", w, "Lookalike or non-canonical combining sequence in headword.", maxStoredViolations);
                        }

                        if (!ValidateWordSyllables(w, allowReferenceTransliteration: isRef, out string badSyl, out string sylErr, trie))
                        {
                            report.RecordViolation(WordViolationKind.InvalidSyllablePhonotactics, "Entries", w, $"Invalid syllable '{badSyl}': {sylErr}", maxStoredViolations);
                        }

                        if (IsGluedParticleOrNoiseEntry(e, officialWords, out string leakReason))
                        {
                            report.RecordViolation(WordViolationKind.GluedParticleOrPhraseLeak, "Entries", w, leakReason, maxStoredViolations);
                        }
                    }

                    string mc = e.MeaningCue ?? string.Empty;
                    string un = e.UsageNote ?? string.Empty;
                    if (mc.IndexOf("နျယန", StringComparison.Ordinal) >= 0 ||
                        un.IndexOf("နျယန", StringComparison.Ordinal) >= 0 ||
                        mc.IndexOf("ဗှတ္တ", StringComparison.Ordinal) >= 0 ||
                        un.IndexOf("၎ာန", StringComparison.Ordinal) >= 0 ||
                        un.IndexOf("၎ာပယတိ", StringComparison.Ordinal) >= 0 ||
                        mc.StartsWith("ပါဠိရင်းမြစ် မြန်မာဝေါဟာရ (ပါဠိမူရင်း —", StringComparison.Ordinal))
                    {
                        report.RecordViolation(WordViolationKind.CorruptedGlossOrEtymology, "Entries", w, "Corrupted WinInnwa Pali artifact or mislabeled Pali etymology.", maxStoredViolations);
                    }
                }
            }

            // Gate Audit 2: OrthographyRules
            if (model.OrthographyRules != null)
            {
                for (int i = 0; i < model.OrthographyRules.Count; i++)
                {
                    var r = model.OrthographyRules[i];
                    if (r == null || string.IsNullOrWhiteSpace(r.WrongForm) || string.IsNullOrWhiteSpace(r.CorrectForm))
                    {
                        report.RecordViolation(WordViolationKind.EmptyOrMalformedWord, "OrthographyRules", $"Rule#{i}", "Empty WrongForm or CorrectForm.", maxStoredViolations);
                        continue;
                    }

                    if (string.Equals(r.WrongForm, r.CorrectForm, StringComparison.Ordinal))
                    {
                        report.RecordViolation(WordViolationKind.RuleChainOrSelfCycle, "OrthographyRules", r.WrongForm, "Self-referential rule WrongForm == CorrectForm.", maxStoredViolations);
                    }

                    if (!string.Equals(r.Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase) &&
                        condemnedSet.Contains(r.CorrectForm))
                    {
                        report.RecordViolation(WordViolationKind.RuleChainOrSelfCycle, "OrthographyRules", $"{r.WrongForm}->{r.CorrectForm}", "Unflattened rule chain: CorrectForm is itself a WrongForm.", maxStoredViolations);
                    }

                    if (r.CorrectForm.IndexOf(' ') < 0 && !activeWords.Contains(r.CorrectForm))
                    {
                        report.RecordViolation(WordViolationKind.MissingTargetHeadword, "OrthographyRules", $"{r.WrongForm}->{r.CorrectForm}", "Single-word CorrectForm missing from active Entries.", maxStoredViolations);
                    }

                    if (UnicodeNormalizer.HasLookalikeError(r.CorrectForm, out _, out _) ||
                        NonCanonicalCombiningSubseqRegex.IsMatch(r.CorrectForm) ||
                        !ValidateWordSyllables(r.CorrectForm, allowReferenceTransliteration: false, out _, out _, trie))
                    {
                        report.RecordViolation(WordViolationKind.InvalidSyllablePhonotactics, "OrthographyRules", $"{r.WrongForm}->{r.CorrectForm}", "CorrectForm fails syllable or lookalike validation.", maxStoredViolations);
                    }
                }
            }

            // Gate Audit 3: ContextRules
            if (model.ContextRules != null)
            {
                for (int i = 0; i < model.ContextRules.Count; i++)
                {
                    var cr = model.ContextRules[i];
                    if (cr == null || string.IsNullOrWhiteSpace(cr.WrongForm) || string.IsNullOrWhiteSpace(cr.CorrectForm))
                    {
                        report.RecordViolation(WordViolationKind.EmptyOrMalformedWord, "ContextRules", $"CtxRule#{i}", "Empty WrongForm or CorrectForm.", maxStoredViolations);
                        continue;
                    }

                    if (cr.CorrectForm.IndexOf(' ') < 0 && !activeWords.Contains(cr.CorrectForm))
                    {
                        report.RecordViolation(WordViolationKind.MissingTargetHeadword, "ContextRules", $"{cr.WrongForm}->{cr.CorrectForm}", "ContextRule CorrectForm missing from active Entries.", maxStoredViolations);
                    }

                    if (UnicodeNormalizer.HasLookalikeError(cr.CorrectForm, out _, out _) ||
                        NonCanonicalCombiningSubseqRegex.IsMatch(cr.CorrectForm) ||
                        !ValidateWordSyllables(cr.CorrectForm, allowReferenceTransliteration: false, out _, out _, trie))
                    {
                        report.RecordViolation(WordViolationKind.InvalidSyllablePhonotactics, "ContextRules", $"{cr.WrongForm}->{cr.CorrectForm}", "ContextRule CorrectForm fails syllable or lookalike validation.", maxStoredViolations);
                    }
                }
            }

            // Gate Audit 4: WordBigrams
            if (model.WordBigrams != null)
            {
                for (int i = 0; i < model.WordBigrams.Count; i++)
                {
                    var bg = model.WordBigrams[i];
                    if (bg == null || bg.Count <= 0 || string.IsNullOrWhiteSpace(bg.LeftWord) || string.IsNullOrWhiteSpace(bg.RightWord))
                    {
                        report.RecordViolation(WordViolationKind.InvalidBigramToken, "WordBigrams", $"Bigram#{i}", "Empty or non-positive bigram.", maxStoredViolations);
                        continue;
                    }

                    if (condemnedSet.Contains(bg.LeftWord) || condemnedSet.Contains(bg.RightWord) || condemnedSet.Contains(bg.LeftWord + bg.RightWord))
                    {
                        report.RecordViolation(WordViolationKind.InvalidBigramToken, "WordBigrams", $"{bg.LeftWord} {bg.RightWord}", "Bigram contains condemned WrongForm.", maxStoredViolations);
                    }

                    if (!activeWords.Contains(bg.LeftWord) || !activeWords.Contains(bg.RightWord))
                    {
                        report.RecordViolation(WordViolationKind.InvalidBigramToken, "WordBigrams", $"{bg.LeftWord} {bg.RightWord}", "Bigram references token absent from active Entries.", maxStoredViolations);
                    }
                }
            }

            return report;
        }
    }
}
