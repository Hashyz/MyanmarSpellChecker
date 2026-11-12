using System;
using System.Collections.Generic;

namespace MyanmarSpellChecker.Core.Models
{
    /// <summary>
    /// Controls whether the proofing engine checks standard orthography for all general clients
    /// or also enforces strict formal literary style (ရုံးသုံး/စာပေ အရေးစကား).
    /// </summary>
    public enum ProofingStyleMode
    {
        /// <summary>
        /// Default for businesses, media, students, writers & general clients:
        /// Enforces 100% "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" spelling, lookalikes, numerals (တ/တစ်, ခုနစ်/ခုနှစ်),
        /// မ...ဘဲ, လဲ/လည်း, and context homophones, while allowing polite colloquial Burmese (ပါတယ်, တွေ, နဲ့, ပါမယ်).
        /// </summary>
        GeneralStandard = 0,

        /// <summary>
        /// For Government Ministries, legal contracts, academic theses & formal publishing:
        /// Enforces all standard orthography PLUS formal literary style (ပါတယ် -> ပါသည်, တွေ -> များ, နဲ့ -> နှင့်, အား -> ကို).
        /// </summary>
        FormalOfficial = 1
    }

    public enum ProofingIssueType
    {
        /// <summary>
        /// Unrecognized spelling or explicit အမှား->အမှန် entry in မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Red wavy underline).
        /// </summary>
        SpellingError,

        /// <summary>
        /// Lookalike character confusion such as Myanmar Digit Zero '၀' (U+1040) vs Consonant Wa 'ဝ' (U+101D) (Red wavy underline).
        /// </summary>
        LookalikeCharacterError,

        /// <summary>
        /// Legacy Zawgyi-One text detected inside a Pyidaungsu Unicode document (Red wavy underline).
        /// </summary>
        ZawgyiEncodingError,

        /// <summary>
        /// 'တ' vs 'တစ်' numeral classifier error per မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (Blue wavy underline).
        /// </summary>
        NumeralClassifierError,

        /// <summary>
        /// Colloquialism or particle misuse in official government style (ရုံးသုံးမြန်မာစာ) (Blue wavy underline).
        /// </summary>
        OfficialStyleGrammarError,

        /// <summary>
        /// Myanmar punctuation (ပုဒ်ဖြတ်ပုဒ်ရပ် ၊ ။) error (Blue wavy underline).
        /// </summary>
        PunctuationError,

        /// <summary>
        /// Ambiguous or statistical collocation / wrong-meaning warning requiring human confirmation
        /// (Amber-Gold wavy underline, excluded from blind Fix All).
        /// </summary>
        ContextualMeaningWarning
    }

    public sealed class ProofingIssue
    {
        /// <summary>
        /// Zero-based character offset within the inspected text/paragraph.
        /// </summary>
        public int StartIndex { get; set; }

        /// <summary>
        /// Character length of the flagged span.
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// The original substring flagged in the document.
        /// </summary>
        public string OriginalText { get; set; } = string.Empty;

        public ProofingIssueType IssueType { get; set; }

        /// <summary>
        /// Human-readable explanation in Myanmar (shown in Task Pane).
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Explicit step-by-step grammatical / syntactic reasoning explaining WHY this form violates
        /// Burmese grammar in this clause context and WHEN the contrasting form is valid.
        /// </summary>
        public string GrammaticalReasoning { get; set; } = string.Empty;

        /// <summary>
        /// Verified authoritative reference for this rule (e.g., မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ, မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း, ရုံးသုံးမြန်မာစာ).
        /// </summary>
        public string AuthoritativeSource { get; set; } = string.Empty;

        /// <summary>
        /// Ranked list of suggested replacements with သတ်ပုံကျမ်း meaning cues.
        /// </summary>
        public List<SuggestionCandidate> Suggestions { get; set; } = new List<SuggestionCandidate>();

        /// <summary>
        /// True if this issue should be underlined in Blue (Grammar / Style); False for Red (Spelling / Encoding) or Amber (Contextual Check).
        /// </summary>
        public bool IsGrammarOrStyle =>
            IssueType == ProofingIssueType.NumeralClassifierError ||
            IssueType == ProofingIssueType.OfficialStyleGrammarError ||
            IssueType == ProofingIssueType.PunctuationError;

        /// <summary>
        /// True if this issue is an Amber-Gold 'အနက်သံသယ (Context Check)' semantic/collocation warning
        /// that requires explicit human review and must never be overwritten by blind 'Fix All'.
        /// </summary>
        public bool IsContextualWarning =>
            IssueType == ProofingIssueType.ContextualMeaningWarning;

        /// <summary>
        /// True when this issue has high-certainty suggestions safe for batch 'Fix All'.
        /// </summary>
        public bool IsAutoFixSafe =>
            IssueType != ProofingIssueType.ContextualMeaningWarning &&
            Suggestions != null &&
            Suggestions.Count > 0;
    }
}

