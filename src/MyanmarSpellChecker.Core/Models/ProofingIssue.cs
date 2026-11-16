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
        /// True when this issue arises from an unrecognized OOV syllable/name or non-trivial fuzzy edit-distance guess
        /// and should default to UNCHECKED (☐) in the Fix-All Preview Checklist so names and already-checked words stay safe.
        /// </summary>
        public bool RequiresReviewBeforeBatchFix { get; set; }

        /// <summary>
        /// True when this issue is a 100% deterministic orthography, lookalike, Zawgyi, numeral, punctuation, or formal-style rule
        /// safe to pre-check (☑) in the Fix-All Preview Checklist; False (☐) for unknown words/names, fuzzy guesses, close ties, or context warnings.
        /// </summary>
        public bool IsHighCertaintyPrecheck
        {
            get
            {
                if (IsContextualWarning ||
                    RequiresReviewBeforeBatchFix ||
                    Suggestions == null ||
                    Suggestions.Count == 0 ||
                    string.IsNullOrEmpty(Suggestions[0].Replacement))
                {
                    return false;
                }

                if (IssueType == ProofingIssueType.LookalikeCharacterError ||
                    IssueType == ProofingIssueType.ZawgyiEncodingError ||
                    IssueType == ProofingIssueType.NumeralClassifierError ||
                    IssueType == ProofingIssueType.OfficialStyleGrammarError ||
                    IssueType == ProofingIssueType.PunctuationError)
                {
                    return true;
                }

                double topScore = Suggestions[0].Score;
                if (topScore < 97.5)
                    return false;

                if (Suggestions.Count >= 2)
                {
                    string secondCue = Suggestions[1].MeaningCue ?? string.Empty;
                    bool isRegisterVariant =
                        secondCue.IndexOf("စကားပြောဟန်", StringComparison.Ordinal) >= 0 ||
                        secondCue.IndexOf("စာပေဟန်", StringComparison.Ordinal) >= 0 ||
                        secondCue.IndexOf("ရုံးသုံး", StringComparison.Ordinal) >= 0;

                    if (!isRegisterVariant && (topScore - Suggestions[1].Score) < 2.5)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        /// True whenever this issue requires human review (Context Meaning check, unrecognized name/OOV guess,
        /// no suggestion, or close multi-option tie) — displayed in Yellow across document/sheet underlines,
        /// Task Pane cards, Quick-Fix popups, Review &amp; Fix, and Audit Reports instead of Spelling Red.
        /// </summary>
        public bool IsReviewNeeded => !IsHighCertaintyPrecheck;

        /// <summary>
        /// Human-readable certainty label displayed in the Fix-All Preview Checklist Dialog.
        /// </summary>
        public string CertaintyBadgeText
        {
            get
            {
                if (IsContextualWarning)
                    return Localization.UiLocalizer.Pick("⚠ Context Check (Review)", "⚠ အနက်သံသယ (စိစစ်ရန်)");
                if (IsHighCertaintyPrecheck)
                    return Localization.UiLocalizer.Pick("✓ High Certainty (Standard Rule)", "✓ သေချာ (စံသတ်ပုံ/သဒ္ဒါ)");
                return Localization.UiLocalizer.Pick("⚠ Review Needed (Name/Guess)", "⚠ စိစစ်ရန် (အမည်/ခန့်မှန်းချက်)");
            }
        }

        /// <summary>
        /// True when this issue has high-certainty suggestions safe for headless batch 'Fix All'.
        /// </summary>
        public bool IsAutoFixSafe =>
            IssueType != ProofingIssueType.ContextualMeaningWarning &&
            !RequiresReviewBeforeBatchFix &&
            Suggestions != null &&
            Suggestions.Count > 0;

        /// <summary>
        /// Computes a concise, BMP-safe visual diff badge (e.g. "[ZWSP]", "[ါ->ာ]", "[၀->ဝ]", "[ုိ->ို]", "[ေ/ြ ရှေ့ထား]")
        /// whenever an issue involves invisible zero-width characters, Unicode combining-mark ordering, or lookalike codepoints
        /// that otherwise look identical or near-identical to the human eye in UI tables and cards.
        /// </summary>
        public string FormatVisualDiffBadge(string? chosenReplacement = null)
        {
            string orig = OriginalText ?? string.Empty;
            string rep = chosenReplacement ?? (Suggestions != null && Suggestions.Count > 0 ? Suggestions[0].Replacement : string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(orig))
                return string.Empty;

            bool hasZwsp = orig.IndexOf('\u200B') >= 0;
            bool hasZwnj = orig.IndexOf('\u200C') >= 0;
            bool hasZwj = orig.IndexOf('\u200D') >= 0;
            bool hasBom = orig.IndexOf('\uFEFF') >= 0;

            // Visual-order pre-base 'ေ' / 'ြ' typed before base consonant
            for (int i = 0; i < orig.Length; i++)
            {
                char ch = orig[i];
                if ((ch == '\u1031' || ch == '\u103C') &&
                    (i == 0 || orig[i - 1] == '\u200C' || !((orig[i - 1] >= '\u1000' && orig[i - 1] <= '\u102A') || orig[i - 1] == '\u103F' || orig[i - 1] == '\u104E' || (orig[i - 1] >= '\u103B' && orig[i - 1] <= '\u103E'))))
                {
                    return Localization.UiLocalizer.Pick("[Visual ေ/ြ]", "[ေ/ြ အစီအစဉ်]");
                }
            }

            if (hasZwsp && rep.IndexOf('\u200B') < 0)
                return "[ZWSP]";
            if (hasBom && rep.IndexOf('\uFEFF') < 0)
                return "[BOM]";
            if (hasZwj && rep.IndexOf('\u200D') < 0)
                return "[ZWJ]";
            if (hasZwnj && rep.IndexOf('\u200C') < 0)
                return "[ZWNJ]";

            if (!string.IsNullOrEmpty(rep) && !string.Equals(orig, rep, StringComparison.Ordinal))
            {
                if (orig.IndexOf("\u102F\u102D", StringComparison.Ordinal) >= 0 && rep.IndexOf("\u102D\u102F", StringComparison.Ordinal) >= 0)
                    return "[ုိ➔ို]";
                if (orig.IndexOf("\u1037\u103A", StringComparison.Ordinal) >= 0 && rep.IndexOf("\u103A\u1037", StringComparison.Ordinal) >= 0)
                    return "[့်➔င့်]";
                if (orig.IndexOf("\u103A\u1039", StringComparison.Ordinal) >= 0 && rep.IndexOf("\u103A\u1039", StringComparison.Ordinal) < 0 && IssueType == ProofingIssueType.LookalikeCharacterError)
                    return "[ကင်းစီး]";
                if (orig.IndexOf('\u1040') >= 0 && rep.IndexOf('\u101D') >= 0 && rep.IndexOf('\u1040') < 0)
                    return "[၀➔ဝ]";
                if (orig.IndexOf('\u101D') >= 0 && rep.IndexOf('\u1040') >= 0 && rep.IndexOf('\u101D') < 0)
                    return "[ဝ➔၀]";
                if (orig.IndexOf('\u102B') >= 0 && rep.IndexOf('\u102C') >= 0 && orig.Length == rep.Length)
                    return "[ါ➔ာ]";
                if (orig.IndexOf('\u102C') >= 0 && rep.IndexOf('\u102B') >= 0 && orig.Length == rep.Length)
                    return "[ာ➔ါ]";
                if (orig.IndexOf("\u1005\u103B", StringComparison.Ordinal) >= 0 && rep.IndexOf('\u1008') >= 0)
                    return "[စျ➔ဈ]";
                if (orig.IndexOf("\u1025\u103A", StringComparison.Ordinal) >= 0 && rep.IndexOf("\u1009\u103A", StringComparison.Ordinal) >= 0)
                    return "[ဥ်➔ဉ်]";

                string normOrig = Nlp.UnicodeNormalizer.NormalizeCanonicalOrder(orig);
                string normRep = Nlp.UnicodeNormalizer.NormalizeCanonicalOrder(rep);
                if (string.Equals(normOrig, normRep, StringComparison.Ordinal))
                {
                    return Localization.UiLocalizer.Pick("[Unicode Order]", "[ယူနီကုဒ်အစီအစဉ်]");
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Formats <see cref="OriginalText"/> for checklist grids and cards so invisible zero-width spaces and
        /// subtle combining-mark / lookalike differences are immediately visible to the user.
        /// </summary>
        public string FormatVisualOriginalText(string? chosenReplacement = null)
        {
            string orig = OriginalText ?? string.Empty;
            if (string.IsNullOrEmpty(orig))
                return string.Empty;

            string display = orig
                .Replace("\u200B", "[ZWSP]")
                .Replace("\uFEFF", "[BOM]")
                .Replace("\u200D", "[ZWJ]");

            string badge = FormatVisualDiffBadge(chosenReplacement);
            if (!string.IsNullOrEmpty(badge) &&
                badge != "[ZWSP]" &&
                badge != "[BOM]" &&
                badge != "[ZWJ]" &&
                display.IndexOf(badge, StringComparison.Ordinal) < 0)
            {
                return $"{display}  {badge}";
            }

            return display;
        }
    }
}

