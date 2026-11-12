using System;
using System.Runtime.Serialization;

namespace MyanmarSpellChecker.Core.Models
{
    /// <summary>
    /// Represents an explicit "အမှား -> အမှန်" orthography or official ministry style rule
    /// derived from "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" and "ရုံးသုံးမြန်မာစာ".
    /// </summary>
    [DataContract]
    public sealed class OrthographyRule
    {
        [DataMember]
        public int Id { get; set; }

        /// <summary>
        /// The incorrect, colloquial, or non-standard form (e.g., "တယောက်", "တီဆောက်", "တွေ").
        /// </summary>
        [DataMember]
        public string WrongForm { get; set; } = string.Empty;

        /// <summary>
        /// The official standard form (e.g., "တစ်ယောက်", "တည်ဆောက်", "များ").
        /// </summary>
        [DataMember]
        public string CorrectForm { get; set; } = string.Empty;

        /// <summary>
        /// Rule category: "Spelling" (စာလုံးပေါင်းအမှား), "NumeralClassifier" (တ/တစ် စည်းမျဉ်း),
        /// "OfficialStyle" (ရုံးသုံးအရေးအသား), or "Particle" (ဝိဘတ်/ပစ္စည်းအသုံး).
        /// </summary>
        [DataMember]
        public string Category { get; set; } = "Spelling";

        /// <summary>
        /// Explanation shown in the Word Task Pane and Context Menu.
        /// </summary>
        [DataMember]
        public string Explanation { get; set; } = string.Empty;
    }
}
