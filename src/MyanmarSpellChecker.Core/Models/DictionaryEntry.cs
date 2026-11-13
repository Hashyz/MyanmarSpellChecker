using System;
using System.Runtime.Serialization;

namespace MyanmarSpellChecker.Core.Models
{
    /// <summary>
    /// Represents an official entry from "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" (Myanmar Language Commission).
    /// </summary>
    [DataContract]
    public sealed class DictionaryEntry
    {
        [DataMember]
        public int Id { get; set; }

        /// <summary>
        /// Canonical Myanmar Unicode headword (e.g., "ချည်", "ချဉ်", "နိုင်ငံတော်").
        /// </summary>
        [DataMember]
        public string Word { get; set; } = string.Empty;

        /// <summary>
        /// Disambiguation meaning or context cue from သတ်ပုံကျမ်း (e.g., "ကြိုး~၊ နှောင်~" vs "အရသာ~").
        /// </summary>
        [DataMember]
        public string MeaningCue { get; set; } = string.Empty;

        /// <summary>
        /// Part-of-speech tag (N, V, ADJ, ADV, PART, PRON, CONJ, NUM).
        /// </summary>
        [DataMember]
        public string PosTag { get; set; } = string.Empty;

        /// <summary>
        /// Phonetic skeleton key used to match homophones (e.g., ရ/ယ, သ/ဿ, ဉ/ည, -န်/-မ်, -ည်/-ီ/-ေ).
        /// </summary>
        [DataMember]
        public string PhoneticKey { get; set; } = string.Empty;

        /// <summary>
        /// Optional example or usage note.
        /// </summary>
        [DataMember]
        public string UsageNote { get; set; } = string.Empty;

        /// <summary>
        /// Real-world Myanmar corpus frequency count (higher = more common word in everyday & formal usage).
        /// </summary>
        [DataMember]
        public int Frequency { get; set; }

        /// <summary>
        /// True if this headword is directly attested in the official "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" book.
        /// </summary>
        [DataMember]
        public bool IsOfficialThatpon { get; set; } = true;

        /// <summary>
        /// Cached orthographic syllable count populated when loaded into <see cref="Nlp.MyanmarTrie"/> (avoids repeated regex syllable breaking in hot loops).
        /// </summary>
        [IgnoreDataMember]
        public int SyllableCount { get; set; }

        /// <summary>
        /// True when this entry is a classical proverb, traditional saying, or rhetorical style reference.
        /// </summary>
        public bool IsProverbOrSayingEntry
        {
            get
            {
                if (string.IsNullOrEmpty(PosTag)) return false;
                return string.Equals(PosTag, "PROVERB", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(PosTag, "SAYING", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(PosTag, "RHETORIC", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// True when this entry is an offline encyclopedic / treatise knowledge article (စွယ်စုံဗဟုသုတ).
        /// </summary>
        public bool IsEncyclopediaEntry => string.Equals(PosTag, "ENCYC", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when this entry is a lazy-loaded English-to-Myanmar bilingual lexicon entry (EN_LEX).
        /// </summary>
        public bool IsBilingualLexiconEntry => string.Equals(PosTag, "EN_LEX", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when this entry is stored in the reference/knowledge corpus rather than single-word spellcheck lattice anchors.
        /// </summary>
        public bool IsReferenceEntry => IsProverbOrSayingEntry || IsEncyclopediaEntry || IsBilingualLexiconEntry;

        /// <summary>
        /// True when this entry is an official geographic toponym, sovereign country, city, township, or proper noun.
        /// </summary>
        public bool IsProperNoun => string.Equals(PosTag, "PROPN", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when this entry belongs to one of the specialized professional/scholarly lexicons.
        /// </summary>
        public bool IsSpecializedDomain => !IsEncyclopediaEntry && !IsBilingualLexiconEntry && !string.IsNullOrEmpty(SpecializedDomain);

        /// <summary>
        /// True when structured မြန်မာအဘိဓာန်, encyclopedic, or geographic metadata is available.
        /// </summary>
        public bool HasAbidanMetadata => !string.IsNullOrWhiteSpace(UsageNote);

        public string BurmesePronunciation => ExtractUsageField("အသံထွက်:");
        public string NumeralClassifier => ExtractUsageField("မျိုးပြပစ္စည်း:");
        public string Etymology => ExtractUsageField("ရင်းမြစ်:");
        public string PaliEquivalent => ExtractUsageField("ပါဠိအနက်:");
        public string Synonyms => ExtractUsageField("ကြောင်းတူသံကွဲ:");
        public string Antonyms => ExtractUsageField("ဆန့်ကျင်ဘက်:");
        public string RelatedTerms => ExtractUsageField("ဆက်စပ်ဝေါဟာရ:");
        public string KnowledgeSummary => ExtractUsageField("ဗဟုသုတ:");
        public string ExampleSentence => ExtractUsageField("သာဓက:");
        public string EnglishName
        {
            get
            {
                string en = ExtractUsageField("အင်္ဂလိပ်အမည်:");
                if (!string.IsNullOrEmpty(en)) return en;
                en = ExtractUsageField("အင်္ဂလိပ်ဝေါဟာရ:");
                if (!string.IsNullOrEmpty(en)) return en;
                return ExtractUsageField("အင်္ဂလိပ်အနက်:");
            }
        }
        public string EntityCategory => ExtractUsageField("အမျိုးအစား:");
        public string ContinentRegion => ExtractUsageField("တိုက်ကြီး / ဒေသ:");
        public string OfficialFullName => ExtractUsageField("ရုံးသုံးအမည်:");
        public string CapitalCity => ExtractUsageField("မြို့တော်:");
        public string CurrencyInfo => ExtractUsageField("သုံးစွဲငွေကြေး:");
        public string OfficialLanguageInfo => ExtractUsageField("ရုံးသုံးဘာသာစကား:");
        public string IsoAndCallingCode => ExtractUsageField("နိုင်ငံကုဒ် / ဖုန်းကုဒ်:");
        public string SpecializedDomain => ExtractUsageField("နယ်ပယ်:");
        public string AuthorityStandard => ExtractUsageField("အာဏာပိုင်စံနှုန်း:");

        /// <summary>
        /// Compact single-line summary of key မြန်မာအဘိဓာန်, Pali, Specialized Domain, or MOFA/GAD geographic metadata for issue cards.
        /// </summary>
        public string CompactAbidanSummary
        {
            get
            {
                if (string.IsNullOrWhiteSpace(UsageNote))
                    return string.Empty;

                var chips = new System.Collections.Generic.List<string>(4);
                if (IsProperNoun)
                {
                    string cap = CapitalCity;
                    if (!string.IsNullOrEmpty(cap))
                    {
                        string reg = ContinentRegion;
                        chips.Add(!string.IsNullOrEmpty(reg) ? reg : EntityCategory);
                        chips.Add("မြို့တော်: " + cap);
                        string curr = CurrencyInfo;
                        if (!string.IsNullOrEmpty(curr))
                            chips.Add("ငွေကြေး: " + curr);
                        string enCountry = EnglishName;
                        if (!string.IsNullOrEmpty(enCountry))
                            chips.Add("EN: " + enCountry);
                        return string.Join("  |  ", chips);
                    }

                    string cat = EntityCategory;
                    if (!string.IsNullOrEmpty(cat))
                        chips.Add(cat);

                    string loc = ExtractUsageField("တည်နေရာ:");
                    if (string.IsNullOrEmpty(loc))
                        loc = ExtractUsageField("နိုင်ငံ:");
                    if (string.IsNullOrEmpty(loc))
                        loc = ExtractUsageField("ရုံးချုပ်:");
                    if (!string.IsNullOrEmpty(loc))
                        chips.Add(loc);

                    string en = EnglishName;
                    if (!string.IsNullOrEmpty(en))
                        chips.Add("EN: " + en);

                    if (chips.Count > 0)
                        return string.Join("  |  ", chips);
                }

                if (IsEncyclopediaEntry || IsSpecializedDomain)
                {
                    if (!string.IsNullOrEmpty(SpecializedDomain))
                        chips.Add("နယ်ပယ်: " + SpecializedDomain);
                    string enDom = EnglishName;
                    if (!string.IsNullOrEmpty(enDom))
                        chips.Add("EN: " + enDom);
                    string auth = AuthorityStandard;
                    if (!string.IsNullOrEmpty(auth))
                        chips.Add(auth);
                    if (chips.Count > 0)
                        return string.Join("  |  ", chips);
                }

                string pron = BurmesePronunciation;
                if (!string.IsNullOrEmpty(pron))
                    chips.Add("အသံထွက်: " + pron);

                string clf = NumeralClassifier;
                if (!string.IsNullOrEmpty(clf))
                    chips.Add("မျိုးပြပစ္စည်း: " + clf);

                string etym = Etymology;
                if (!string.IsNullOrEmpty(etym))
                {
                    chips.Add("ရင်းမြစ်: " + etym);
                }
                else
                {
                    string pali = PaliEquivalent;
                    if (!string.IsNullOrEmpty(pali))
                        chips.Add("ပါဠိအနက်: " + pali);
                }

                if (chips.Count == 0)
                {
                    string raw = UsageNote.Trim();
                    return raw.Length > 75 ? raw.Substring(0, 72).TrimEnd() + "..." : raw;
                }

                return string.Join("  |  ", chips);
            }
        }

        /// <summary>
        /// Formats pipe-delimited မြန်မာအဘိဓာန် metadata in <see cref="UsageNote"/> as clean multi-line bullets for Word UI cards.
        /// </summary>
        public string FormattedUsageDetails
        {
            get
            {
                if (string.IsNullOrWhiteSpace(UsageNote))
                    return string.Empty;

                string[] parts = UsageNote.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 1)
                    return UsageNote.Trim();

                return "• " + string.Join(Environment.NewLine + "• ", parts);
            }
        }

        private string ExtractUsageField(string prefix)
        {
            if (string.IsNullOrWhiteSpace(UsageNote))
                return string.Empty;

            string[] parts = UsageNote.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.StartsWith(prefix, StringComparison.Ordinal))
                    return p.Substring(prefix.Length).Trim();
            }
            return string.Empty;
        }

        /// <summary>
        /// Human-readable Burmese + English part-of-speech description for UI detail cards.
        /// </summary>
        public string PosDisplayLabel => FormatPosTag(PosTag);

        public string LocalizedPosDisplayLabel => FormatLocalizedPosTag(PosTag);

        public static string FormatLocalizedPosTag(string? pos)
        {
            if (!Localization.UiLocalizer.IsEnglish)
                return FormatPosTag(pos);

            if (string.IsNullOrWhiteSpace(pos))
                return "Official Lexicon";

            switch (pos!.Trim().ToUpperInvariant())
            {
                case "N": return "Noun";
                case "V": return "Verb";
                case "ADJ": return "Adjective";
                case "ADV": return "Adverb";
                case "N/V":
                case "V/N": return "Noun / Verb";
                case "V/ADJ":
                case "ADJ/V": return "Verb / Adjective";
                case "N/ADJ":
                case "ADJ/N": return "Noun / Adjective";
                case "V/ADV":
                case "ADV/V": return "Verb / Adverb";
                case "ADJ/ADV":
                case "ADV/ADJ": return "Adjective / Adverb";
                case "PRON": return "Pronoun";
                case "PART": return "Particle";
                case "POST": return "Postposition";
                case "CONJ": return "Conjunction";
                case "INTJ": return "Interjection";
                case "NUM": return "Numeral / Classifier";
                case "LOAN": return "Loanword";
                case "PROPN": return "Proper Noun";
                case "PROVERB": return "Myanmar Proverb";
                case "SAYING": return "Myanmar Saying";
                case "RHETORIC": return "Traditional Rhetoric";
                case "ENCYC": return "Encyclopedia";
                case "EN_LEX": return "English ⇄ Myanmar";
                case "CUSTOM": return "Custom Word";
                default: return pos!;
            }
        }

        public static string FormatPosTag(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return "သတ်ပုံကျမ်း ဝေါဟာရ";

            switch (pos!.Trim().ToUpperInvariant())
            {
                case "N": return "နာမ် (Noun)";
                case "V": return "ကြိယာ (Verb)";
                case "ADJ": return "နာမဝိသေသန (Adjective)";
                case "ADV": return "ကြိယာဝိသေသန (Adverb)";
                case "N/V":
                case "V/N": return "နာမ် / ကြိယာ";
                case "V/ADJ":
                case "ADJ/V": return "ကြိယာ / နာမဝိသေသန";
                case "N/ADJ":
                case "ADJ/N": return "နာမ် / နာမဝိသေသန";
                case "V/ADV":
                case "ADV/V": return "ကြိယာ / ကြိယာဝိသေသန";
                case "ADJ/ADV":
                case "ADV/ADJ": return "နာမဝိသေသန / ကြိယာဝိသေသန";
                case "PRON": return "နာမ်စား (Pronoun)";
                case "PART": return "ပစ္စည်း / ဝိဘတ် (Particle)";
                case "POST": return "ဝိဘတ် (Postposition)";
                case "CONJ": return "သမ္ဗန္ဓ (Conjunction)";
                case "INTJ": return "အာမေဍိတ် (Interjection)";
                case "NUM": return "သင်္ချာ / မျိုးပြပစ္စည်း (Numeral)";
                case "LOAN": return "မွေးစားဝေါဟာရ (Loanword)";
                case "PROPN": return "ပထဝီဝင် / သီးခြားအမည် (Proper Noun)";
                case "PROVERB": return "မြန်မာစကားပုံ (Proverb)";
                case "SAYING": return "မြန်မာဆိုရိုးစကား (Saying)";
                case "RHETORIC": return "စကားကြီးဆယ်မျိုး (Rhetoric)";
                case "ENCYC": return "စွယ်စုံဗဟုသုတ (Encyclopedia)";
                case "EN_LEX": return "အင်္ဂလိပ် ⇄ မြန်မာ (EN-MM)";
                case "CUSTOM": return "မှတ်သားဝေါဟာရ (Custom)";
                default: return pos!;
            }
        }

        /// <summary>
        /// Splits multi-sense definitions cleanly into numbered senses ("1. ...", "2. ...") when 2+ distinct senses exist.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> GetFormattedSenses()
        {
            if (string.IsNullOrWhiteSpace(MeaningCue))
                return Array.Empty<string>();

            string raw = MeaningCue.Trim();
            string[] rawParts;
            if (raw.IndexOf(" • ", StringComparison.Ordinal) >= 0)
            {
                rawParts = raw.Split(new[] { " • " }, StringSplitOptions.RemoveEmptyEntries);
            }
            else if (raw.IndexOf("؛", StringComparison.Ordinal) >= 0 || raw.IndexOf("; ", StringComparison.Ordinal) >= 0)
            {
                rawParts = raw.Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                return new[] { raw };
            }

            var cleaned = new System.Collections.Generic.List<string>(rawParts.Length);
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rawParts.Length; i++)
            {
                string part = rawParts[i].Trim();
                if (part.Length == 0)
                    continue;

                // Strip leading existing numbering like "1. " or "၁။ " before re-numbering consistently
                int dotIdx = part.IndexOf(". ", StringComparison.Ordinal);
                if (dotIdx > 0 && dotIdx <= 2 && char.IsDigit(part[0]))
                {
                    part = part.Substring(dotIdx + 2).Trim();
                }

                if (part.Length > 0 && seen.Add(part))
                    cleaned.Add(part);
            }

            if (cleaned.Count <= 1)
                return cleaned.Count == 1 ? new[] { cleaned[0] } : new[] { raw };

            var numbered = new string[cleaned.Count];
            for (int i = 0; i < cleaned.Count; i++)
            {
                numbered[i] = (i + 1).ToString() + ". " + cleaned[i];
            }
            return numbered;
        }

        /// <summary>
        /// Returns a clean English headword or primary English translation suitable for offline SAPI TTS playback, or empty if none.
        /// </summary>
        public string GetSpeakableEnglishText()
        {
            return Dictionary.OfflineSpeechService.CleanSpeakableEnglishTerm(
                IsBilingualLexiconEntry ? Word : EnglishName);
        }
    }
}
