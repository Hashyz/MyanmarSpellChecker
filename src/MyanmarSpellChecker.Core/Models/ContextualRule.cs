using System;
using System.Runtime.Serialization;

namespace MyanmarSpellChecker.Core.Models
{
    /// <summary>
    /// Represents a data-driven context-aware disambiguation or collocation rule
    /// (e.g., collocations, contextual medial swaps, aukmyit/visarga disambiguation, and homophone triggers).
    /// </summary>
    [DataContract]
    public sealed class ContextualRule
    {
        [DataMember]
        public string WrongForm { get; set; } = string.Empty;

        [DataMember]
        public string CorrectForm { get; set; } = string.Empty;

        /// <summary>
        /// Pipe-delimited ('|') context trigger words or suffixes.
        /// </summary>
        [DataMember]
        public string TriggerWords { get; set; } = string.Empty;

        /// <summary>
        /// Optional pipe-delimited ('|') anti-trigger / veto words anywhere in the same clause that suppress this rule
        /// when the literal meaning of <see cref="WrongForm"/> is genuinely intended.
        /// </summary>
        [DataMember(EmitDefaultValue = false)]
        public string AntiTriggerWords { get; set; } = string.Empty;

        /// <summary>
        /// Context scan direction: "Left", "Right", "Both", or "LeftSuffix".
        /// </summary>
        [DataMember]
        public string Direction { get; set; } = "Both";

        /// <summary>
        /// Number of adjacent Myanmar word/syllable tokens to inspect (typically 1..5).
        /// </summary>
        [DataMember]
        public int Window { get; set; } = 3;

        /// <summary>
        /// When true, emits Tier-2 <see cref="ProofingIssueType.ContextualMeaningWarning"/> ('အနက်သံသယ' Amber-Gold underline,
        /// excluded from blind Fix All) instead of Tier-1 Red <see cref="ProofingIssueType.SpellingError"/>.
        /// </summary>
        [DataMember(EmitDefaultValue = false)]
        public bool IsWarningOnly { get; set; }

        /// <summary>
        /// When true, case/postpositional particles (ကို, က, မှာ, တွင်, ဖြင့်, သို့, နှင့်, ၏, etc.) do not consume window distance,
        /// allowing SOV noun-particle-verb collocations like 'ကြိုးကို ချဉ်သည် -> ချည်သည်' to match cleanly.
        /// </summary>
        [DataMember(EmitDefaultValue = false)]
        public bool SkipParticles { get; set; }

        [DataMember]
        public string Explanation { get; set; } = string.Empty;

        [IgnoreDataMember]
        internal string[] ParsedTriggers { get; set; } = Array.Empty<string>();

        [IgnoreDataMember]
        internal string[] ParsedAntiTriggers { get; set; } = Array.Empty<string>();
    }
}

