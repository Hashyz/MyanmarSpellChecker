using System;

namespace MyanmarSpellChecker.Core.Models
{
    public sealed class SuggestionCandidate
    {
        /// <summary>
        /// The replacement word in canonical Myanmar Unicode.
        /// </summary>
        public string Replacement { get; set; } = string.Empty;

        /// <summary>
        /// Disambiguation meaning cue from "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" (e.g., "ကြိုး~၊ နှောင်~").
        /// </summary>
        public string MeaningCue { get; set; } = string.Empty;

        /// <summary>
        /// Confidence / distance score (higher confidence = ranked first).
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Part-of-speech tag of the suggested headword (e.g. N, V, ADJ, LOAN).
        /// </summary>
        public string PosTag { get; set; } = string.Empty;

        /// <summary>
        /// True when the replacement comes directly from "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း".
        /// </summary>
        public bool IsOfficialThatpon { get; set; } = true;

        /// <summary>
        /// Syllable-by-syllable visual breakdown (e.g. "အ • ကြောင်း • အ • ရာ").
        /// </summary>
        public string SyllableBreakdown { get; set; } = string.Empty;

        /// <summary>
        /// Auto-generated character/syllable diff explanation showing exactly what was wrong and why.
        /// </summary>
        public string DiagnosticDetail { get; set; } = string.Empty;

        /// <summary>
        /// Confusable homophone siblings with meaning cues so the writer chooses the right spelling for their meaning.
        /// </summary>
        public string HomophoneSiblings { get; set; } = string.Empty;

        /// <summary>
        /// Compact မြန်မာအဘိဓာန် / Pali cognate metadata (e.g. "အသံထွက်: ... | မျိုးပြပစ္စည်း: ... | ရင်းမြစ်: ...").
        /// </summary>
        public string AbidanSummary { get; set; } = string.Empty;

        /// <summary>
        /// Formatted label for Word Quick-Fix Popup, Right-Click Context Menu, and Task Pane choice buttons.
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MeaningCue))
                    return Replacement;

                bool hasMyanmarCueChar = false;
                for (int i = 0; i < MeaningCue.Length; i++)
                {
                    char ch = MeaningCue[i];
                    if (ch >= '\u1000' && ch <= '\u109F')
                    {
                        hasMyanmarCueChar = true;
                        break;
                    }
                }
                if (!hasMyanmarCueChar)
                    return Replacement;

                string cue = MeaningCue.Trim();
                // Strip redundant leading 'Word (' wrapper so 'ဘူး [ဘူး (မဟုတ်~)]' becomes clean 'ဘူး [မဟုတ်~]'
                string prefixParen = Replacement + " (";
                if (cue.StartsWith(prefixParen, StringComparison.Ordinal) && cue.EndsWith(")", StringComparison.Ordinal))
                {
                    cue = cue.Substring(prefixParen.Length, cue.Length - prefixParen.Length - 1).Trim();
                }

                // Strip trailing Khwe-Htar '(နှိုင်းယှဉ်: ...)' comparison tail on compact Quick-Fix buttons when a Burmese gloss precedes it
                int cmpIdx = cue.IndexOf("(နှိုင်းယှဉ်:", StringComparison.Ordinal);
                if (cmpIdx >= 4)
                {
                    cue = cue.Substring(0, cmpIdx).TrimEnd(' ', '•', '—', '-');
                }

                if (cue.Length > 38)
                {
                    cue = Nlp.MyanmarSyllableBreaker.TruncateAtSyllableBoundary(cue, 38, "…");
                }

                return $"{Replacement}  [{cue}]";
            }
        }
    }
}

