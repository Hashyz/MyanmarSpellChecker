using System;
using System.Runtime.Serialization;

namespace MyanmarSpellChecker.Core.Models
{
    /// <summary>
    /// Represents an empirical Myanmar word bigram co-occurrence count P(RightWord | LeftWord)
    /// mined from large-scale Myanmar corpora for Viterbi lattice scoring and real-word confusion detection.
    /// </summary>
    [DataContract]
    public sealed class WordBigramEntry
    {
        [DataMember]
        public string LeftWord { get; set; } = string.Empty;

        [DataMember]
        public string RightWord { get; set; } = string.Empty;

        [DataMember]
        public int Count { get; set; }
    }
}

