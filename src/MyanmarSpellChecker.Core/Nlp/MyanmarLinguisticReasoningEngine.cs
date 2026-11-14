using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    /// <summary>
    /// Universal 100%-Offline Burmese Linguistic, Pali Etymological, Phonological &amp; Syntactic Reasoning Engine.
    /// Enriches every <see cref="ProofingIssue"/> with multi-layer step-by-step linguistic reasoning,
    /// live မြန်မာအဘိဓာန် semantic contrast, empirical corpus bigram collocation evidence, and authoritative citations.
    /// </summary>
    public static class MyanmarLinguisticReasoningEngine
    {
        private static readonly int ReasoningCacheCap = !Environment.Is64BitProcess ? 1024 : 2048;

        private static readonly ConcurrentDictionary<(long TrieVer, string RawWrong, string Rep, bool IsGrammarOrStyle), (string Breakdown, string Diag, string Pos, string Cue, string Abidan, bool HasExact, bool IsThatpon, string Homo)> s_candidateMetaCache =
            new ConcurrentDictionary<(long TrieVer, string RawWrong, string Rep, bool IsGrammarOrStyle), (string Breakdown, string Diag, string Pos, string Cue, string Abidan, bool HasExact, bool IsThatpon, string Homo)>();

        private static readonly ConcurrentDictionary<(long TrieVer, ProofingIssueType Type, ProofingStyleMode Mode, string OrigRaw, string Target, string Message, string BaseReasoning, string BaseSource, string BigramEv), (string Reasoning, string Source)> s_reasoningCache =
            new ConcurrentDictionary<(long TrieVer, ProofingIssueType Type, ProofingStyleMode Mode, string OrigRaw, string Target, string Message, string BaseReasoning, string BaseSource, string BigramEv), (string Reasoning, string Source)>();

        private static readonly ConcurrentDictionary<(long TrieVer, string Target), (int Count, string Collocation)> s_internalConstituentBigramCache =
            new ConcurrentDictionary<(long TrieVer, string Target), (int Count, string Collocation)>();

        public static void ClearCache()
        {
            s_candidateMetaCache.Clear();
            s_reasoningCache.Clear();
            s_internalConstituentBigramCache.Clear();
        }
        private static readonly Regex PatSintClusterRegex = new Regex(
            @"(?<!\u1004\u103A)([\u1000-\u102A\u103F])\u1039([\u1000-\u102A\u103F])",
            RegexOptions.Compiled);

        private static readonly Regex ZawgyiCodepointRegex = new Regex(
            @"[\u1033\u1034\u105A\u1060-\u1097]",
            RegexOptions.Compiled);

        // Tibeto-Burman Simplex (Intransitive/Stative) <-> Duplex (Transitive/Causative) Verb Root Pairs
        private static readonly (string Simplex, string Duplex, string SimplexSense, string DuplexSense)[] CausativeSimplexDuplexPairs =
        {
            ("မြင့်", "မြှင့်", "အလိုအလျောက် မြင့်မားခြင်း (အကမ္မက)", "မြင့်တက်လာအောင် ပြုလုပ်ခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("နိမ့်", "နှိမ့်", "အလိုအလျောက် နိမ့်ကျခြင်း (အကမ္မက)", "နိမ့်ကျသွားအောင် ပြုလုပ်ခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("ရွေ့", "ရွှေ့", "မိမိဘာသာ ရွေ့လျားခြင်း (အကမ္မက)", "တစ်နေရာမှ တစ်နေရာသို့ ပြောင်းရွှေ့စေခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("လွတ်", "လွှတ်", "အချုပ်အနှောင်မှ လွတ်မြောက်ခြင်း (အကမ္မက)", "စေလွှတ်ခြင်း / လွတ်မြောက်စေခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("ကျ", "ချ", "အလိုအလျောက် ကျဆင်းခြင်း (အကမ္မက)", "အောက်သို့ ချထားစေခြင်း (သကမ္မက — စတုတ္ထအက္ခရာ/ဒုတိယအက္ခရာ)"),
            ("ပျက်", "ဖျက်", "အလိုအလျောက် ပျက်စီးခြင်း (အကမ္မက)", "ပျက်စီးသွားအောင် ဖျက်ဆီးခြင်း (သကမ္မက)"),
            ("ပြည့်", "ဖြည့်", "အလိုအလျောက် ပြည့်ဝခြင်း (အကမ္မက)", "ပြည့်လာအောင် ဖြည့်ဆည်းခြင်း (သကမ္မက)"),
            ("ပြတ်", "ဖြတ်", "အလိုအလျောက် ပြတ်တောက်ခြင်း (အကမ္မက)", "ပြတ်တောက်အောင် ဖြတ်တောက်ခြင်း (သကမ္မက)"),
            ("ကွဲ", "ခွဲ", "အလိုအလျောက် ကွဲပြားခြင်း (အကမ္မက)", "ကွဲပြားအောင် ခွဲခြားခြင်း (သကမ္မက)"),
            ("ညွတ်", "ညွှတ်", "ဦးညွတ်/ကိုင်းညွတ်ခြင်း (အကမ္မက)", "ညွတ်သွားအောင် ညွှတ်စေခြင်း / ညွှန်ပြခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("နိုး", "နှိုး", "အိပ်ရာမှ အလိုအလျောက် နိုးထခြင်း (အကမ္မက)", "သူတစ်ပါး နိုးထလာအောင် နှိုးဆော်ခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("နစ်", "နှစ်", "ရေ/နွံ၌ နစ်မြုပ်ခြင်း (အကမ္မက)", "ရေ၌ နှစ်မြှုပ်စေခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("လန့်", "လှန့်", "ထိတ်လန့်ကြောက်ရွံ့ခြင်း (အကမ္မက)", "ထိတ်လန့်အောင် ခြောက်လှန့်ခြင်း (သကမ္မက — ဟထိုး 'ှ')"),
            ("ကြွေ", "ခြွေ", "သစ်ရွက်/အသီး အလိုအလျောက် ကြွေကျခြင်း (အကမ္မက)", "ကြွေကျအောင် ဆွတ်ခြွေခြင်း (သကမ္မက)")
        };

        // Pali / Sanskrit Etymological Silent-Coda Roots
        private static readonly (string Suffix, string CodaName, string PaliRootNote)[] SilentCodaEtymologies =
        {
            ("ဂြိုဟ်", "ဟသတ် (ဟ်)", "ပါဠိ/သက္ကတ 'ဂဟ / ဂြဟ' (gaha/graha) မှ ဆင်းသက်သဖြင့် မူရင်း 'ဟ' အက္ခရာကို အသံတိတ် 'ဟသတ် (ဟ်)' အဖြစ် ထိန်းသိမ်းရေးထုံးပြုသည်"),
            ("သင်္ဂြိုဟ်", "ကင်းစီး (င်္) + ဟသတ် (ဟ်)", "ပါဠိ 'သင်္ဂဟ' (saṅgaha = ချီးမြှောက်ထောက်ပံ့ခြင်း/အကျဉ်းချုပ်ခြင်း) မှ ဆင်းသက်သဖြင့် ကင်းစီးနှင့် 'ဟသတ် (ဟ်)' ရေးရသည်"),
            ("ကုသိုလ်", "လသတ် (လ်)", "ပါဠိ 'ကုသလ' (kusala = ကောင်းမြတ်သောအမှု) မှ ဆင်းသက်သဖြင့် မူရင်း 'လ' အက္ခရာကို 'လသတ် (လ်)' ဖြင့် ရေးရသည်"),
            ("ပုဂ္ဂိုလ်", "လသတ် (လ်)", "ပါဠိ 'ပုဂ္ဂလ' (puggala) မှ ဆင်းသက်သဖြင့် မူရင်း 'လ' အက္ခရာကို 'လသတ် (လ်)' ဖြင့် ရေးရသည်"),
            ("ဗိုလ်", "လသတ် (လ်)", "ပါဠိ 'ဗလ' (bala = အင်အား/စစ်သည်အကြီးအမှူး) မှ ဆင်းသက်သဖြင့် 'လသတ် (လ်)' ဖြင့် ရေးရသည်"),
            ("ဖိုလ်", "လသတ် (လ်)", "ပါဠိ 'ဖလ' (phala = အကျိုးတရား) မှ ဆင်းသက်သဖြင့် 'လသတ် (လ်)' ဖြင့် ရေးရသည်"),
            ("သီဟိုဠ်", "ဠကြီးသတ် (ဠ်)", "ပါဠိ 'သီဟဠ' (Sīhaḷa) မှ ဆင်းသက်သဖြင့် မူရင်း ဋ-ဝဂ် 'ဠ' အက္ခရာကို 'ဠကြီးသတ် (ဠ်)' ဖြင့် ရေးရသည်"),
            ("မြင်းမိုရ်", "ရကောက်သတ် (ရ်)", "ပါဠိ/သက္ကတ 'မေရု' (Meru) မှ ဆင်းသက်သဖြင့် မူရင်း 'ရ' အက္ခရာကို 'ရကောက်သတ် (ရ်)' ဖြင့် ရေးရသည်"),
            ("ဗြဟ္မစိုရ်", "ရကောက်သတ် (ရ်)", "ပါဠိ 'ဗြဟ္မစရိယ' (brahmacariya) မှ ဆင်းသက်သဖြင့် 'ရကောက်သတ် (ရ်)' ဖြင့် ရေးရသည်"),
            ("ပဟိုရ်", "ရကောက်သတ် (ရ်)", "ပါဠိ 'ပဟာရ' (pahāra = နာရီစည်တီးချက်) မှ ဆင်းသက်သဖြင့် 'ရကောက်သတ် (ရ်)' ဖြင့် ရေးရသည်"),
            ("နဂိုရ်", "ရကောက်သတ် (ရ်)", "ပါဠိ 'နဂရ' (nagara = မြို့တော် — သင်္ကဿနဂိုရ်) မှ ဆင်းသက်သည် (မူလသဘာဝ 'နဂို' တွင် အသတ်မပါ)"),
            ("လာဘ်", "ဘကုန်းသတ် (ဘ်)", "ပါဠိ 'လာဘ' (lābha = ရရှိအပ်သော အကျိုးစီးပွား) မှ ဆင်းသက်သဖြင့် မူရင်း 'ဘ' ကို 'ဘကုန်းသတ် (ဘ်)' ဖြင့် ရေးရသည်"),
            ("ပြာသာဒ်", "ဒထွေးသတ် (ဒ်)", "ပါဠိ 'ပါသာဒ' (pāsāda) မှ ဆင်းသက်သဖြင့် ရှေ့နှစ်လုံး ရေးချပါ၍ 'ဒထွေးသတ် (ဒ်)' ဖြင့် ရေးရသည်"),
            ("ပရိသတ်", "တဝမ်းပူသတ် (တ်)", "ပါဠိ 'ပရိသာ / ပရိသဒါ' (parisā) မှ ဆင်းသက်သဖြင့် အလယ်ဝဏ္ဏ 'ရိ' တွင် အသတ်မပါဘဲ 'ပရိသတ်' ဟု ရေးရသည်"),
            ("ဝိညာဉ်", "ဉကလေးသတ် (ဉ်)", "ပါဠိ 'ဝိညာဏ' (viññāṇa) မှ မြန်မာသတ်ပုံကျမ်းစံအရ စ-ဝဂ်အသတ် 'ဉကလေးသတ် (ဉ်)' ဖြင့် ရေးရသည်"),
            ("ပဋိညာဉ်", "ဉကလေးသတ် (ဉ်)", "ပါဠိ 'ပဋိညာ' (paṭiññā = ဝန်ခံကတိပြုချက်) မှ ဆင်းသက်သဖြင့် 'ဉကလေးသတ် (ဉ်)' ဖြင့် ရေးရသည်")
        };

        /// <summary>
        /// Enriches all issues detected in a document paragraph with deep Burmese linguistic reasoning,
        /// live မြန်မာအဘိဓာန် metadata, bigram collocation proof, and authoritative citations.
        /// </summary>
        public static void EnrichIssuesWithDeepReasoning(
            string fullText,
            IReadOnlyList<SyllableToken> tokens,
            List<ProofingIssue> issues,
            MyanmarTrie trie,
            ProofingStyleMode styleMode)
        {
            if (issues == null || issues.Count == 0 || trie == null)
                return;

            long trieVer = trie.MutationVersion;

            for (int idx = 0; idx < issues.Count; idx++)
            {
                ProofingIssue issue = issues[idx];
                PopulateSuggestionMetadata(issue, trie, trieVer);

                SuggestionCandidate? top = issue.Suggestions.Count > 0 ? issue.Suggestions[0] : null;
                string origRaw = (issue.OriginalText ?? string.Empty).Trim();
                string origNorm = UnicodeNormalizer.NormalizeCanonicalOrder(origRaw);
                string target = top != null ? UnicodeNormalizer.NormalizeCanonicalOrder(top.Replacement.Trim()) : string.Empty;

                // Build live bigram collocation proof from neighboring clause tokens or compound constituents
                string bigramEvidence = BuildSentenceBigramEvidence(fullText, tokens, issue, target, trie);

                string baseReasoning = issue.GrammaticalReasoning ?? string.Empty;
                string baseSource = issue.AuthoritativeSource ?? string.Empty;
                var cacheKey = (trieVer, issue.IssueType, styleMode, origRaw, target, issue.Message ?? string.Empty, baseReasoning, baseSource, bigramEvidence);
                if (s_reasoningCache.TryGetValue(cacheKey, out var cachedReason))
                {
                    issue.GrammaticalReasoning = cachedReason.Reasoning;
                    issue.AuthoritativeSource = cachedReason.Source;
                    continue;
                }

                DictionaryEntry? targetEntry = GetPrimaryEntry(trie, target);
                DictionaryEntry? origEntry = (!string.IsNullOrEmpty(origNorm) && origNorm != target)
                    ? GetPrimaryEntry(trie, origNorm)
                    : null;

                if (!string.IsNullOrWhiteSpace(issue.GrammaticalReasoning))
                {
                    // If ContextDisambiguator set a short generic Khwe-Htar note, enrich it with deep phonological/morphological & semantic contrast!
                    if (issue.GrammaticalReasoning!.StartsWith("သတ်ပုံကျမ်း အနက်ခွဲထားစနစ်အရ", StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(target))
                    {
                        var deepPrefix = new List<string>(3);
                        string paliLaw = TryAnalyzePaliVaggantaAndEtymology(origNorm, target, targetEntry);
                        if (!string.IsNullOrEmpty(paliLaw)) deepPrefix.Add(paliLaw);
                        string phonoLaw = TryAnalyzeBurmesePhonologyAndMorphology(origRaw, origNorm, target);
                        if (!string.IsNullOrEmpty(phonoLaw) && !phonoLaw.StartsWith("အက္ခရာ/ဝဏ္ဏ စိစစ်ချက်", StringComparison.Ordinal))
                            deepPrefix.Add(phonoLaw);
                        if (origEntry != null && !string.IsNullOrWhiteSpace(origEntry.MeaningCue) &&
                            targetEntry != null && !string.IsNullOrWhiteSpace(targetEntry.MeaningCue))
                        {
                            deepPrefix.Add($"အနက်အဓိပ္ပာယ် နှိုင်းယှဉ်ချက် — '{origNorm}' သည် [{TrimSense(origEntry.MeaningCue, 60)}] အနက်ရှိပြီး၊ '{target}' မှာ [{TrimSense(targetEntry.MeaningCue, 68)}] အနက်ဖြစ်သည်။");
                        }
                        if (deepPrefix.Count > 0)
                        {
                            issue.GrammaticalReasoning = string.Join(" ", deepPrefix) + " " + issue.GrammaticalReasoning.Trim();
                        }
                    }

                    if (!string.IsNullOrEmpty(bigramEvidence) &&
                        issue.GrammaticalReasoning.IndexOf("ဝါကျအထောက်အထား", StringComparison.Ordinal) < 0)
                    {
                        issue.GrammaticalReasoning = issue.GrammaticalReasoning.TrimEnd() + " " + bigramEvidence;
                    }
                }
                else
                {
                    issue.GrammaticalReasoning = SynthesizeLinguisticReasoning(
                        issue, origRaw, origNorm, target, origEntry, targetEntry, bigramEvidence, trie, styleMode);
                }

                if (string.IsNullOrWhiteSpace(issue.AuthoritativeSource))
                {
                    issue.AuthoritativeSource = DetermineAuthoritativeSource(issue, origNorm, target, targetEntry, styleMode);
                }

                if (s_reasoningCache.Count >= ReasoningCacheCap)
                    s_reasoningCache.Clear();
                s_reasoningCache[cacheKey] = (issue.GrammaticalReasoning ?? string.Empty, issue.AuthoritativeSource ?? string.Empty);
            }
        }

        private static void PopulateSuggestionMetadata(ProofingIssue issue, MyanmarTrie trie, long trieVer)
        {
            if (issue.Suggestions == null || issue.Suggestions.Count == 0)
                return;

            string rawWrong = (issue.OriginalText ?? string.Empty).Trim();
            bool isGrammarOrStyle = issue.IsGrammarOrStyle;
            for (int i = 0; i < issue.Suggestions.Count; i++)
            {
                SuggestionCandidate cand = issue.Suggestions[i];
                if (string.IsNullOrWhiteSpace(cand.Replacement))
                    continue;

                string rep = cand.Replacement.Trim();
                var metaKey = (trieVer, rawWrong, rep, isGrammarOrStyle);
                if (!s_candidateMetaCache.TryGetValue(metaKey, out var meta))
                {
                    bool hasNoSpace = rep.IndexOf(' ') < 0;
                    string breakdown = hasNoSpace ? OrthographicDiffExplainer.FormatSyllableBreakdown(rep) : string.Empty;
                    string diag = (!string.IsNullOrEmpty(rawWrong) && hasNoSpace)
                        ? OrthographicDiffExplainer.ExplainDifference(rawWrong, rep)
                        : string.Empty;
                    string pos = string.Empty;
                    string cue = string.Empty;
                    string abidan = string.Empty;
                    bool hasExact = false;
                    bool isThatpon = false;

                    if (trie.TryGetExactEntries(rep, out var entries) && entries.Count > 0)
                    {
                        hasExact = true;
                        for (int eIdx = 0; eIdx < entries.Count; eIdx++)
                        {
                            var e = entries[eIdx];
                            if (e.IsOfficialThatpon)
                                isThatpon = true;
                            if (pos.Length == 0 && !string.IsNullOrWhiteSpace(e.PosTag))
                                pos = e.PosTag;
                            if (cue.Length == 0 && !string.IsNullOrWhiteSpace(e.MeaningCue))
                                cue = e.MeaningCue;
                            if (!isGrammarOrStyle && abidan.Length == 0)
                            {
                                string ab = e.CompactAbidanSummary;
                                if (!string.IsNullOrWhiteSpace(ab))
                                    abidan = ab;
                            }
                        }
                    }

                    string homo = hasNoSpace ? trie.GetHomophoneSiblingsSummary(rep, 3) : string.Empty;
                    meta = (breakdown, diag, pos, cue, abidan, hasExact, isThatpon, homo);
                    if (s_candidateMetaCache.Count >= ReasoningCacheCap)
                        s_candidateMetaCache.Clear();
                    s_candidateMetaCache[metaKey] = meta;
                }

                if (string.IsNullOrWhiteSpace(cand.SyllableBreakdown) && meta.Breakdown.Length > 0)
                    cand.SyllableBreakdown = meta.Breakdown;
                if (string.IsNullOrWhiteSpace(cand.DiagnosticDetail) && meta.Diag.Length > 0)
                    cand.DiagnosticDetail = meta.Diag;
                if (meta.HasExact)
                {
                    if (string.IsNullOrWhiteSpace(cand.PosTag))
                        cand.PosTag = meta.Pos;
                    if (string.IsNullOrWhiteSpace(cand.MeaningCue))
                        cand.MeaningCue = meta.Cue;
                    if (string.IsNullOrWhiteSpace(cand.AbidanSummary) && !isGrammarOrStyle)
                        cand.AbidanSummary = meta.Abidan;
                    cand.IsOfficialThatpon = meta.IsThatpon;
                }
                if (string.IsNullOrWhiteSpace(cand.HomophoneSiblings) && meta.Homo.Length > 0)
                    cand.HomophoneSiblings = meta.Homo;
            }
        }

        private static string SynthesizeLinguisticReasoning(
            ProofingIssue issue,
            string origRaw,
            string origNorm,
            string target,
            DictionaryEntry? origEntry,
            DictionaryEntry? targetEntry,
            string bigramEvidence,
            MyanmarTrie trie,
            ProofingStyleMode styleMode)
        {
            var parts = new List<string>(4);

            // 1. Zawgyi Encoding Forensics
            if (issue.IssueType == ProofingIssueType.ZawgyiEncodingError)
            {
                bool hasZawgyiRange = ZawgyiCodepointRegex.IsMatch(origRaw);
                string codepointNote = hasZawgyiRange
                    ? "ဇော်ဂျီသီးသန့် ကုဒ်ပွိုင့်များ (U+1060..U+1097 / U+1033..U+1034) ပါဝင်နေပါသည်။"
                    : "သဝေထိုး (U+1031) ကို မူရင်းဗျည်းရှေ့၌ မျက်မြင်အစီအစဉ် (Visual Order) ဖြင့် ရိုက်နှိပ်ထားပါသည်။";
                parts.Add($"ယူနီကုဒ် သိုလှောင်ပုံနိယာမ — {codepointNote} စံမြန်မာယူနီကုဒ် (Pyidaungsu / ISO 10646) တွင် အက္ခရာများကို အသံထွက်သဒ္ဒါအစီအစဉ် (Logical Order: ဗျည်း + ဗျည်းတွဲ + သရ + အသတ် + ဝစ္စပေါက်) အတိုင်းသာ သိမ်းဆည်းရသဖြင့် စံယူနီကုဒ်စာသားအဖြစ် ပြောင်းလဲပေးထားပါသည်။");
                return string.Join(" ", parts);
            }

            // 2. Lookalike Unicode Codepoint Forensics (checks true Unicode codepoint confusions first, then Pali Pat-Sint lookalike normalizations)
            if (issue.IssueType == ProofingIssueType.LookalikeCharacterError)
            {
                bool isTrueCodepointLookalike =
                    origRaw.IndexOf('\u1040') >= 0 ||
                    origRaw.StartsWith("၄င်း", StringComparison.Ordinal) ||
                    origRaw.Contains("ဥ်") ||
                    (origRaw != target && origRaw.Replace('\u102B', '\u102C') == target.Replace('\u102B', '\u102C'));

                if (isTrueCodepointLookalike)
                {
                    parts.Add(AnalyzeLookalikeCodepointLaw(origRaw, target));
                }
                else
                {
                    string paliInLookalike = TryAnalyzePaliVaggantaAndEtymology(origNorm, target, targetEntry);
                    parts.Add(!string.IsNullOrEmpty(paliInLookalike)
                        ? paliInLookalike
                        : AnalyzeLookalikeCodepointLaw(origRaw, target));
                }
                AppendTargetLexicalSense(parts, target, targetEntry);
                if (!string.IsNullOrEmpty(bigramEvidence)) parts.Add(bigramEvidence);
                return string.Join(" ", parts);
            }

            // 3. Punctuation Forensics
            if (issue.IssueType == ProofingIssueType.PunctuationError)
            {
                parts.Add($"ပုဒ်ဖြတ်ပုဒ်ရပ် စည်းကမ်း — မြန်မာစာအရေးအသား၌ ပုဒ်ကလေး (၊) သည် ဝါကျအတွင်း ပုဒ်စုများကို ခေတ္တရပ်နားရန်ဖြစ်ပြီး ပုဒ်မကြီး (။) သည် ဝါကျအဆုံးသတ်ရန် ဖြစ်သည်။ ပုဒ်ဖြတ်ပုဒ်ရပ်၏ ရှေ့၌ Space မခြားရဘဲ အပိုထပ်နေသော သင်္ကေတများကို တစ်ခုတည်းသာ ထားရှိရမည်။");
                return string.Join(" ", parts);
            }

            // 4. Numeral 'တ' vs 'တစ်' & 'ခုနစ်' Principle
            if (issue.IssueType == ProofingIssueType.NumeralClassifierError)
            {
                if (origNorm.Contains("ခုနှစ်") && target.Contains("ခုနစ်"))
                {
                    parts.Add("သင်္ချာရေးထုံး နိယာမ — ဂဏန်း (၂) 'နှစ်' နှင့် နှစ်ကာလ 'နှစ်' တို့တွင်သာ ဟထိုး (ှ) ပါရှိပြီး၊ ဂဏန်း (၇) ကို ရေးသားရာ၌ ဟထိုးမပါဘဲ 'ခုနစ်' (နသတ်သီးသန့်) ဖြင့်သာ ရေးထုံးပြုသည်။");
                }
                else
                {
                    parts.Add($"သင်္ချာ 'တစ်' နှင့် ရှေ့ဆက် 'တ' ခွဲခြားရေးထုံး — မျိုးပြပစ္စည်း (ခု၊ ဦး၊ ယောက်၊ ပါး၊ မျိုး၊ ကြိမ်၊ စီး စသည်) ရှေ့၌ အရေအတွက် (၁) ကို ရည်ညွှန်းသည့်အခါ စလုံးသတ်ပါသော ကိန်းဂဏန်း 'တစ်' ဖြင့် '{target}' ဟု ရေးရသည် (သင်္ချာအနက်မဟုတ်သော 'တချို့၊ တခြား၊ တစ္ဆေ၊ တပည့်၊ တမင်' တို့၌သာ 'တ' သီးသန့် ရေးသည်)။");
                }
                if (!string.IsNullOrEmpty(bigramEvidence)) parts.Add(bigramEvidence);
                return string.Join(" ", parts);
            }

            // 5. Official Style Grammar & Classifier Agreement Reasoning
            if (issue.IssueType == ProofingIssueType.OfficialStyleGrammarError)
            {
                string clfReason = TryAnalyzeClassifierAgreement(issue.Message, origNorm, target);
                if (!string.IsNullOrEmpty(clfReason))
                {
                    parts.Add(clfReason);
                }
                else
                {
                    parts.Add($"အရေးစကားနှင့် အပြောစကား ဟန်ပန်စည်းကမ်း — '{origRaw}' သည် နေ့စဉ်အပြောစကားသုံး ပစ္စည်း/အသုံးအနှုန်း ဖြစ်သည်။ ရုံးသုံးနှင့် စာပေအရေးအသား စံနှုန်း (Formal Literary Register) တွင် ဝါကျအဆုံးသတ်၊ အများကိန်းနှင့် သမ္ဗန္ဓပစ္စည်းများကို စာပေသုံး '{target}' ဖြင့် ညီညွတ်စွာ သုံးနှုန်းရသည်။");
                }
                if (!string.IsNullOrEmpty(bigramEvidence)) parts.Add(bigramEvidence);
                return string.Join(" ", parts);
            }

            // 5b. Tier-2 Contextual Meaning & Collocation Check ('အနက်သံသယ')
            if (issue.IssueType == ProofingIssueType.ContextualMeaningWarning)
            {
                string origCue = origEntry != null && !string.IsNullOrWhiteSpace(origEntry.MeaningCue) ? origEntry.MeaningCue : "မူရင်းအနက်";
                string targCue = targetEntry != null && !string.IsNullOrWhiteSpace(targetEntry.MeaningCue) ? targetEntry.MeaningCue : "စကားစပ်အနက်";
                parts.Add($"အနက်အဓိပ္ပာယ် နှိုင်းယှဉ်စိစစ်ချက် — '{origRaw}' [{origCue}] နှင့် '{target}' [{targCue}] နှစ်ခုစလုံးသည် တရားဝင် ဝေါဟာရများဖြစ်ကြသော်လည်း ဤဝါကျ၏ ရှေ့နောက်တွဲဖက်အသုံးအရ '{target}' က ပိုမိုကိုက်ညီနိုင်ပါသည်။ ရည်ရွယ်ရင်းအနက်နှင့် ကိုက်ညီပါက လျစ်လျူရှုနိုင်ပါသည်။");
                if (!string.IsNullOrEmpty(bigramEvidence)) parts.Add(bigramEvidence);
                return string.Join(" ", parts);
            }

            // 6. Deep Spelling, Proper-Noun Gazetteer, Pali Etymology, Phonological & Semantic Contrast Analysis
            if (!string.IsNullOrEmpty(target))
            {
                // 6.0 Official Geographic, Diplomatic & Administrative Proper-Noun Law (MOFA / GAD)
                if (targetEntry != null && targetEntry.IsProperNoun)
                {
                    string enLabel = !string.IsNullOrEmpty(targetEntry.EnglishName) ? $" ({targetEntry.EnglishName})" : string.Empty;
                    if (IsDomesticMyanmarProperNoun(target, targetEntry))
                    {
                        parts.Add($"ပထဝီဝင်နှင့် အုပ်ချုပ်ရေးနယ်မြေ စံအမည်နာမ နိယာမ — အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာန (GAD) နှင့် မြန်မာစာအဖွဲ့၏ တရားဝင်နယ်မြေဒေသစာရင်းအရ '{target}'{enLabel} ဟု တိကျစွာ စံသတ်မှတ်ထားပါသည်။");
                    }
                    else
                    {
                        parts.Add($"သံတမန်ရေးရာနှင့် နိုင်ငံတကာ ပထဝီဝင်အမည်နာမ နိယာမ — နိုင်ငံခြားရေးဝန်ကြီးဌာန (MOFA) နှင့် ပြန်ကြားရေးဝန်ကြီးဌာန ရုံးသုံးသံတမန်စံနှုန်းအရ '{target}'{enLabel} ဟု စံသတ်မှတ်ရေးသားရသည်။");
                    }
                }
                else if (targetEntry != null && targetEntry.IsSpecializedDomain)
                {
                    string enDom = !string.IsNullOrEmpty(targetEntry.EnglishName) ? $" ({targetEntry.EnglishName})" : string.Empty;
                    string auth = !string.IsNullOrEmpty(targetEntry.AuthorityStandard)
                        ? targetEntry.AuthorityStandard
                        : GetSpecializedDomainAuthority(targetEntry.SpecializedDomain);
                    parts.Add($"{targetEntry.SpecializedDomain} ဝေါဟာရစံနှုန်း နိယာမ — {auth} နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း စံသတ်မှတ်ချက်အရ '{target}'{enDom} ဟု တိကျစွာ ရေးထုံးပြုရသည်။");
                }

                // 6a. Pali Vagganta Stacking & Pali Geminate Ordinance
                string paliStackLaw = TryAnalyzePaliVaggantaAndEtymology(origNorm, target, targetEntry);
                if (!string.IsNullOrEmpty(paliStackLaw))
                    parts.Add(paliStackLaw);

                // 6b. Burmese Phonological & Morphological Laws (Voicing Sandhi, Simplex-Duplex Causative, Metathesis, Medials, Rhymes)
                string phonoLaw = TryAnalyzeBurmesePhonologyAndMorphology(origRaw, origNorm, target);
                if (!string.IsNullOrEmpty(phonoLaw))
                    parts.Add(phonoLaw);

                // 6c. Semantic Contrast (Real-Word Homophone vs Target Definition)
                if (origEntry != null && !string.IsNullOrWhiteSpace(origEntry.MeaningCue) &&
                    targetEntry != null && !string.IsNullOrWhiteSpace(targetEntry.MeaningCue))
                {
                    string origSense = TrimSense(origEntry.MeaningCue, 68);
                    string targSense = TrimSense(targetEntry.MeaningCue, 78);
                    parts.Add($"အနက်အဓိပ္ပာယ် နှိုင်းယှဉ်ချက် — မူရင်းရေးထားသော '{origNorm}' သည် [{origSense}] အနက်ရှိသော်လည်း၊ ဤဝါကျအကြောင်းအရာအရ လိုအပ်သော '{target}' မှာ [{targSense}] အနက်ဖြစ်၍ သတ်ပုံခွဲထားရမည်။");
                }
                else
                {
                    AppendTargetLexicalSense(parts, target, targetEntry);
                }

                // 6d. Live Sentence Bigram Collocation Evidence
                if (!string.IsNullOrEmpty(bigramEvidence))
                {
                    parts.Add(bigramEvidence);
                }

                // Fallback guarantee if target is an exotic loanword or multi-character edit not hit above
                if (parts.Count == 0)
                {
                    string diff = OrthographicDiffExplainer.ExplainDifference(origRaw, target);
                    parts.Add($"စာလုံးပေါင်းသတ်ပုံ သုံးသပ်ချက် — {diff} မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း စံနှုန်းအရ '{target}' ဟု အတည်ပြုရေးသားရမည်။");
                }
            }
            else
            {
                // Unrecognized syllable/cluster with no close suggestion yet
                parts.Add($"ဝဏ္ဏဖွဲ့စည်းပုံ သုံးသပ်ချက် — '{origRaw}' သည် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် ဝဏ္ဏဖွဲ့စည်းပုံ (၂,၆၆၉ လုံး) နှင့် စံအဘိဓာန် (၈၄,၅၀၀+ ပုဒ်) တွင် မပါဝင်သော အက္ခရာတွဲစပ်မှု ဖြစ်နေပါသည်။ ဗျည်းတွဲ၊ သရနှင့် အသတ် အစီအစဉ်ကို ပြန်လည်စစ်ဆေးပါ။");
            }

            return string.Join(" ", parts);
        }

        private static string AnalyzeLookalikeCodepointLaw(string origRaw, string target)
        {
            if (origRaw.IndexOf('\u1040') >= 0)
            {
                return "ယူနီကုဒ် အက္ခရာခွဲခြားမှု နိယာမ — စာသားတွင် မြန်မာသုညဂဏန်း '၀' (U+1040 Myanmar Digit Zero) ကို ဝလုံးဗျည်း 'ဝ' (U+101D Myanmar Letter Wa) အစား မှားယွင်းရိုက်နှိပ်ထားပါသည်။ စာလုံးပေါင်းဝဏ္ဏများအတွင်း ဗျည်းအက္ခရာ 'ဝ' (U+101D) ကိုသာ သုံးစွဲရမည်။";
            }
            if (origRaw.StartsWith("၄င်း", StringComparison.Ordinal))
            {
                return "ယူနီကုဒ် အက္ခရာခွဲခြားမှု နိယာမ — ရုံးသုံးနာမ်စား '၎င်း' ကို ရေးသားရာ၌ လေးဂဏန်း '၄' (U+1044) မသုံးရဘဲ သီးသန့်သင်္ကေတ '၎' (U+104E Myanmar Symbol Aforementioned) + 'င်း' ဖြင့်သာ ရေးရသည်။";
            }
            if (origRaw.Contains("ဥ်"))
            {
                return "ယူနီကုဒ် အက္ခရာခွဲခြားမှု နိယာမ — စ-ဝဂ်အသတ် 'ဉ်' ကို ရိုက်နှိပ်ရာ၌ သီးခြားသရအက္ခရာ 'ဥ' (U+1025) + အသတ် မသုံးရဘဲ ဉကလေးဗျည်း 'ဉ' (U+1009) + အသတ်ဖြင့် 'ဉ်' ဟု ရေးသားရမည်။";
            }
            if (origRaw != target && origRaw.Replace('\u102B', '\u102C') == target.Replace('\u102B', '\u102C'))
            {
                return "မောက်ချ (ါ) နှင့် ရေးချ (ာ) ဗျည်း ၆ လုံး နိယာမ — မြန်မာအက္ခရာစနစ်၌ ဗျည်းတွဲမပါသော 'ခ၊ ဂ၊ င၊ ဒ၊ ပ၊ ဝ' ဗျည်း (၆) လုံးနောက်တွင် ရေးချ (ာ) ထည့်ပါက အခြားဗျည်းများ (ဆ၊ တ၊ အ) နှင့် ရုပ်ပုံရောထွေးနိုင်သဖြင့် မောက်ချ (ါ) သုံးရပြီး၊ ကျန်ဗျည်းများနှင့် ဗျည်းတွဲပါသောအခါ ရေးချ (ာ) သုံးရသည် (ပါဠိသက် ခြွင်းချက် 'သဒ္ဒါ၊ သဒ္ဓါ၊ ဥပ္ပါတ' မှအပ)။";
            }
            return $"ယူနီကုဒ် အက္ခရာစီစဉ်မှု နိယာမ — '{origRaw}' တွင် ပုံသဏ္ဌာန်ဆင်တူ အက္ခရာ/သရ အစီအစဉ် လွဲမှားနေသဖြင့် စံယူနီကုဒ် '{target}' ဖြင့် ပြင်ဆင်ရမည်။";
        }

        private static string TryAnalyzePaliVaggantaAndEtymology(string origNorm, string target, DictionaryEntry? targetEntry)
        {
            // 1. Pali ordinal 'ဒသမ / ဒွါဒသမ' vs '*ဒဿမ'
            if (origNorm.Contains("ဿမ") && target.Contains("သမ") && !target.Contains("ဿမ"))
            {
                return $"ပါဠိသက် သင်္ချာပုဒ် နိယာမ — '{target}' သည် ပါဠိ 'ဒသမ' (dasa + ma = ဆယ်ခုမြောက် / ဆယ်လီစိတ်) မှ တိုက်ရိုက်ဆင်းသက်သဖြင့် သကြီးနှစ်လုံးဆင့် 'ဿ' မသုံးရဘဲ 'သ' တစ်လုံးတည်းဖြင့် '{target}' ဟု ရေးရသည် ('ဝိပဿနာ / ပဿနာ' တို့၌သာ ပါဠိ 'passa' အရ 'ဿ' သုံးသည်)။";
            }

            // 2. Unnecessary Pat-Sint on standalone 'ဌာန' (e.g., ဝန်ကြီးဋ္ဌာန -> ဝန်ကြီးဌာန)
            if (origNorm.Contains("ဋ္ဌာန") && target.Contains("ဌာန") && !target.Contains("ဋ္ဌာန"))
            {
                return "ပါဠိသက် ပါဌ်ဆင့်နိယာမ — ပါဠိ 'ဌာန' (ṭhāna = တည်ရာအရပ်/ရုံးဌာန) သည် ပင်ကိုသီးခြားပုဒ်ဖြစ်သောကြောင့် ရှေ့၌ ပါဠိရှေ့ဆက် (ပ/အဓိ/သံ/ဥ) မပါသည့် မြန်မာပေါင်းစပ်ပုဒ်များ ('ဝန်ကြီးဌာန၊ ဦးစီးဌာန၊ ကျမ်းပြုဌာန') တွင် ပါဌ်ဆင့်မပါဘဲ 'ဌာန' (ဌဝမ်းဘဲ သီးသန့်) ဖြင့် ရေးရသည် ('ပြဋ္ဌာန်း၊ အဓိဋ္ဌာန်၊ သန္နိဋ္ဌာန်' တို့၌သာ ပါဌ်ဆင့်ပါသည်)။";
            }

            // 3. Pali 5-Vagga Consonant Stacking Law (ဝဂ္ဂန္တဗျည်းစဉ်ထုံး)
            if (target.IndexOf('\u1039') >= 0)
            {
                MatchCollection targetClusters = PatSintClusterRegex.Matches(target);
                if (targetClusters.Count > 0)
                {
                    for (int i = 0; i < targetClusters.Count; i++)
                    {
                        char topC = targetClusters[i].Groups[1].Value[0];
                        char botC = targetClusters[i].Groups[2].Value[0];
                        string correctPair = $"{topC}\u1039{botC}";
                        if (!origNorm.Contains(correctPair))
                        {
                            string vaggaName = GetPaliVaggaName(botC);
                            string stackingRule = DescribePaliStackPair(topC, botC, vaggaName);
                            string paliRoot = targetEntry != null && !string.IsNullOrEmpty(targetEntry.PaliEquivalent)
                                ? $" (ပါဠိအနက်: {targetEntry.PaliEquivalent})"
                                : (targetEntry != null && !string.IsNullOrEmpty(targetEntry.Etymology) ? $" (ရင်းမြစ်: {targetEntry.Etymology})" : string.Empty);
                            return $"ပါဠိသက် ဝဂ္ဂန္တဗျည်းစဉ် နိယာမ — {stackingRule} ထို့ကြောင့် '{origNorm}' အစား '{target}' ({GetConsonantShortName(topC)} + {GetConsonantShortName(botC)}ဆင့်){paliRoot} ဟု ရေးသားရမည်။";
                        }
                    }
                }
            }

            // 4. Silent-Coda Etymology Law (-ိုဟ် / -ိုလ် / -ိုဠ် / -ိုရ် / -ာဘ် / -ာဉ်)
            for (int i = 0; i < SilentCodaEtymologies.Length; i++)
            {
                var item = SilentCodaEtymologies[i];
                if (target.Contains(item.Suffix) && !origNorm.Contains(item.Suffix))
                {
                    return $"ပါဠိ/သက္ကတ ရင်းမြစ်ထိန်း အသတ်နိယာမ — မြန်မာအသံထွက်၌ အသံတူနေသော်လည်း {item.PaliRootNote}။ ထို့ကြောင့် '{target}' ({item.CodaName}) ဖြင့် အတိအကျ ရေးသားရမည်။";
                }
            }

            // 5. Dynamic Etymology from မြန်မာအဘိဓာန် / U Hoke Sein if Pali/Sanskrit/Mon root is recorded
            if (targetEntry != null)
            {
                if (!string.IsNullOrEmpty(targetEntry.Etymology) &&
                    (targetEntry.Etymology.Contains("ပါဠိ") || targetEntry.Etymology.Contains("သက္ကတ") || targetEntry.Etymology.Contains("မွန်")))
                {
                    return $"ဝေါဟာရရင်းမြစ် သုံးသပ်ချက် — မြန်မာအဘိဓာန် ရင်းမြစ်မှတ်တမ်းအရ '{target}' သည် {targetEntry.Etymology} မှ ဆင်းသက်လာသော စံသတ်ပုံဖြစ်သည်။";
                }
            }

            return string.Empty;
        }

        private static string TryAnalyzeBurmesePhonologyAndMorphology(string origRaw, string origNorm, string target)
        {
            // 1. Incompatible Medial Cluster (e.g., ချြို -> ချို)
            if ((origRaw.Contains("\u103B\u103C") || origRaw.Contains("\u103C\u103B")) &&
                !target.Contains("\u103B\u103C") && !target.Contains("\u103C\u103B"))
            {
                return "ဗျည်းတွဲ စည်းကမ်းချက် — မြန်မာအက္ခရာဖွဲ့စည်းပုံ၌ ဗျည်းတစ်လုံးတည်းတွင် ယပင့် (ျ) နှင့် ရရစ် (ြ) နှစ်ခုလုံး ပြိုင်တူတွဲစပ်ခွင့် မရှိပါ (အသံတူ ဗျည်းတွဲတစ်မျိုးတည်းကိုသာ သတ်ပုံကျမ်းအရ ရွေးချယ်ရသည်)။";
            }

            List<string> origSyls = MyanmarSyllableBreaker.BreakIntoSyllables(origNorm);
            List<string> targSyls = MyanmarSyllableBreaker.BreakIntoSyllables(target);

            // 2. Syllable / Consonant Metathesis (e.g., မာကလာသီး -> မာလကာသီး or မာကာလသီး -> မာလကာသီး)
            if (origSyls.Count >= 2 && origSyls.Count == targSyls.Count)
            {
                for (int k = 0; k < origSyls.Count - 1; k++)
                {
                    if (origSyls[k] == targSyls[k + 1] && origSyls[k + 1] == targSyls[k] && origSyls[k] != origSyls[k + 1])
                    {
                        return $"ဝဏ္ဏနေရာစဉ် သုံးသပ်ချက် (Syllable Metathesis) — '{origNorm}' တွင် '{origSyls[k]}' နှင့် '{origSyls[k + 1]}' ဝဏ္ဏနှစ်လုံး ရှေ့နောက်နေရာချင်း လွဲမှားနေပါသည်။ သတ်ပုံကျမ်းဝင် ဝဏ္ဏအစီအစဉ်မှန်မှာ '{OrthographicDiffExplainer.FormatSyllableBreakdown(target)}' ဖြစ်သည်။";
                    }
                    char oc1 = GetBaseConsonant(origSyls[k]);
                    char oc2 = GetBaseConsonant(origSyls[k + 1]);
                    char tc1 = GetBaseConsonant(targSyls[k]);
                    char tc2 = GetBaseConsonant(targSyls[k + 1]);
                    if (oc1 != '\0' && oc2 != '\0' && oc1 != oc2 && oc1 == tc2 && oc2 == tc1)
                    {
                        return $"ဗျည်း/ဝဏ္ဏ နေရာစဉ် သုံးသပ်ချက် (Metathesis) — '{origNorm}' တွင် ကပ်လျက်ဝဏ္ဏနှစ်လုံးဖြစ်သော '{origSyls[k]} + {origSyls[k + 1]}' ၏ အခြေခံဗျည်း ('{oc1}' နှင့် '{oc2}') ရှေ့နောက်နေရာချင်း လွဲမှားနေပါသည်။ သတ်ပုံကျမ်းဝင် အစီအစဉ်မှန်မှာ '{OrthographicDiffExplainer.FormatSyllableBreakdown(target)}' ဖြစ်သည်။";
                    }
                }
            }

            // 3. Tibeto-Burman Simplex–Duplex Causative Aspiration Alternation (အကမ္မက / သကမ္မက ကြိယာအနက်ကွဲ)
            if (origSyls.Count == targSyls.Count)
            {
                for (int k = 0; k < origSyls.Count; k++)
                {
                    if (origSyls[k] == targSyls[k]) continue;
                    for (int p = 0; p < CausativeSimplexDuplexPairs.Length; p++)
                    {
                        var pair = CausativeSimplexDuplexPairs[p];
                        if (origSyls[k] == pair.Simplex && targSyls[k] == pair.Duplex)
                        {
                            return $"မြန်မာသဒ္ဒါ အကမ္မက/သကမ္မက ကြိယာနိယာမ — '{pair.Simplex}' သည် {pair.SimplexSense} ဖြစ်ပြီး၊ '{pair.Duplex}' သည် {pair.DuplexSense} ဖြစ်သည်။ '{target}' ပေါင်းစပ်ပုဒ်တွင် ပြုလုပ်ဆောင်ရွက်ခြင်း (Causative/Transitive) သဘောဆောင်သဖြင့် '{pair.Duplex}' ဖြင့် ရေးသားရမည်။";
                        }
                        if (origSyls[k] == pair.Duplex && targSyls[k] == pair.Simplex)
                        {
                            return $"မြန်မာသဒ္ဒါ အကမ္မက/သကမ္မက ကြိယာနိယာမ — '{pair.Duplex}' သည် {pair.DuplexSense} ဖြစ်ပြီး၊ '{pair.Simplex}' သည် {pair.SimplexSense} ဖြစ်သည်။ '{target}' တွင် အလိုအလျောက်ဖြစ်တည်မှု (Intransitive/Stative) သဘောဖြစ်သဖြင့် '{pair.Simplex}' ဖြင့် ရေးသားရမည်။";
                        }
                    }
                }
            }

            // 4. Intervocalic Voicing Sandhi & Initial Aspiration/Voicing Laws (e.g., သမ္မဒ->သမ္မတ, ပါရာစီဒမော့->ပါရာစီတမော, ကုတင်->ခုတင်, ဖူးသီး->ဘူးသီး, ရှုဒေါင့်->ရှုထောင့်)
            if (origSyls.Count == targSyls.Count && origSyls.Count >= 1)
            {
                for (int k = 0; k < origSyls.Count; k++)
                {
                    if (origSyls[k] == targSyls[k]) continue;
                    char oc = GetBaseConsonant(origSyls[k]);
                    char tc = GetBaseConsonant(targSyls[k]);
                    if (oc != '\0' && tc != '\0' && oc != tc)
                    {
                        if (k >= 1 && IsVoicedStop(oc) && IsVoicelessOrAspiratedCounterpart(oc, tc))
                        {
                            return $"မြန်မာအသံဗေဒ အသံပြောင်းနိယာမ (Voicing Sandhi) — မြန်မာစကားပြောသံစဉ်၌ ရှေ့ဝဏ္ဏနောက်တွင် လိုက်သော အသံမဲ့ဗျည်း '{GetConsonantShortName(tc)}' ကို အသံပါဗျည်း '/{oc}/' အသံဖြင့် ပျော့ပြောင်းစွာ ရွတ်ဆိုလေ့ရှိသော်လည်း၊ စာအရေးစံနှုန်းတွင် အသံထွက်အတိုင်း '{origSyls[k]}' မရေးရဘဲ မူရင်းဗျည်း '{targSyls[k]}' ဖြင့် '{target}' ဟု ရေးသားရမည်။";
                        }
                        if ((oc == 'က' && tc == 'ခ') || (oc == 'ဖ' && tc == 'ဘ') || (oc == 'ဘ' && tc == 'ဖ') ||
                            (oc == 'စ' && tc == 'ဆ') || (oc == 'ဆ' && tc == 'စ') || (oc == 'ပ' && tc == 'ဗ'))
                        {
                            return $"ဗျည်းအသံထွက်နှင့် စံသတ်ပုံ ခွဲခြားချက် — အရပ်သုံးအသံထွက်ကြောင့် '{GetConsonantShortName(oc)}' နှင့် '{GetConsonantShortName(tc)}' ရောထွေးတတ်သော်လည်း မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းတွင် '{GetConsonantShortName(tc)}' အက္ခရာဖြင့် '{target}' ဟု တစ်မျိုးတည်း စံသတ်မှတ်ထားသည်။";
                        }
                        if ((oc == 'ယ' && tc == 'ရ') || (oc == 'ရ' && tc == 'ယ'))
                        {
                            return $"ရကောက် (ရ) နှင့် ယပက်လက် (ယ) ခွဲထားနိယာမ — ခေတ်မြန်မာအသံထွက်တွင် 'ရ' နှင့် 'ယ' အသံတူ (/j/) ထွက်သော်လည်း သတ်ပုံကျမ်း ခွဲထားစည်းကမ်းနှင့် ဝေါဟာရရင်းမြစ်အရ '{GetConsonantShortName(tc)}' ဖြင့် '{target}' ဟု ရေးသားရမည်။";
                        }
                    }
                }
            }

            // 5. Medial Ya-Pint (ျ) vs Ra-Yit (ြ) Contrast
            if ((origNorm.Contains("ျ") && target.Contains("ြ") && !target.Contains("ျ")) ||
                (origNorm.Contains("ြ") && target.Contains("ျ") && !target.Contains("ြ")))
            {
                string rightMedial = target.Contains("ြ") ? "ရရစ် (ြ)" : "ယပင့် (ျ)";
                return $"ယပင့် (ျ) နှင့် ရရစ် (ြ) ခွဲထားနိယာမ — 'ကျ/ကြ၊ ချ/ခြ၊ ပျ/ပြ၊ ဖျ/ဖြ' တို့သည် အသံထွက်တူညီသော်လည်း အနက်အဓိပ္ပာယ်နှင့် မူရင်းဓာတ်ပစ္စည်း မတူညီသဖြင့် သတ်ပုံကျမ်းအရ {rightMedial} ဖြင့် '{target}' ဟု ခွဲခြားရေးသားရမည်။";
            }

            // 6. Syllable-Specific Aspiration (ှ), Nasal Coda & Stop Coda Merger Laws
            string diffSummary = OrthographicDiffExplainer.ExplainDifference(origRaw, target);
            if (origSyls.Count == targSyls.Count)
            {
                for (int k = 0; k < origSyls.Count; k++)
                {
                    string os = origSyls[k];
                    string ts = targSyls[k];
                    if (os == ts) continue;

                    if (os.Replace("\u103E", "") == ts.Replace("\u103E", ""))
                    {
                        string modeDesc = ts.Contains("\u103E") ? "ဟထိုး (ှ) ပါရှိရမည်" : "ဟထိုး (ှ) မပါရှိရပါ";
                        return $"ဟထိုး (ှ) အသံနှင့်အနက် ခွဲထားနိယာမ — '{os}' နှင့် '{ts}' တို့သည် ဟထိုး (ှ) ပါ/မပါ ပေါ်မူတည်၍ ဝေါဟာရအနက် သီးခြားစီ ကွဲပြားသဖြင့် '{target}' တွင် '{ts}' ({modeDesc}) ဖြင့် ရေးသားရမည်။";
                    }
                    if (HasNasalCoda(os) && HasNasalCoda(ts))
                    {
                        return $"နှာသံအသတ် (-န် / -မ် / -ဏ် / -ံ) ခွဲထားနိယာမ — ခေတ်မြန်မာအသံထွက်၌ '{os}' နှင့် '{ts}' နှာသံအသတ်ချင်း အသံတူညီသော်လည်း ဝေါဟာရအနက်ကွဲပြားစေရန် သတ်ပုံကျမ်းက '{target}' ဟု သတ်မှတ်ထားသည် ({diffSummary})။";
                    }
                    if (HasStopCoda(os) && HasStopCoda(ts))
                    {
                        return $"အသံပြတ်အသတ် (-က် / -တ် / -ပ်) ခွဲထားနိယာမ — ရပ်သံအသတ်များသည် အသံထွက် (/ʔ/) တူညီသော်လည်း မူရင်းဝေါဟာရဓာတ်အရ '{os}' အစား '{ts}' ဖြင့် '{target}' ဟု ခွဲခြားရေးသားရမည် ({diffSummary})။";
                    }
                }
            }

            return $"အက္ခရာ/ဝဏ္ဏ စိစစ်ချက် — {diffSummary}";
        }

        private static string TryAnalyzeClassifierAgreement(string message, string origNorm, string target)
        {
            if (target.EndsWith("ဆူ", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — မြန်မာသဒ္ဒါနှင့် မြန်မာအဘိဓာန်အရ ဘုရား၊ စေတီ၊ ပုထိုး၊ ရုပ်ပွားတော်နှင့် ပိဋကတ်ကျမ်းဂန် စသော အထွတ်အမြတ် ပူဇော်အပ်သည့်အရာများကို ရေတွက်ရာ၌ 'ဆူ' မျိုးပြပစ္စည်းကိုသာ သုံးစွဲရသည်။";
            if (target.EndsWith("ပါး", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — ရဟန်းသံဃာတော်၊ သာမဏေ၊ သီလရှင်နှင့် မင်းစိုးရာဇာ စသော ရိုသေလေးမြတ်အပ်သည့် ပုဂ္ဂိုလ်များကို ရေတွက်ရာ၌ 'ယောက်/ဦး' မသုံးရဘဲ 'ပါး' မျိုးပြပစ္စည်းကိုသာ သုံးစွဲရသည်။";
            if (target.EndsWith("ဦး", StringComparison.Ordinal) && origNorm.EndsWith("ယောက်", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — ရုံးသုံးနှင့် လေးစားသမှုပြုအပ်သော ပုဂ္ဂိုလ်/တာဝန်ရှိသူများကို ရေတွက်ရာ၌ 'ယောက်' အစား ပိုမိုယဉ်ကျေးခန့်ညားသော 'ဦး' မျိုးပြပစ္စည်းကို သုံးစွဲရသည်။";
            if (target.EndsWith("စီး", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — ကုန်းကြောင်းနှင့် လေကြောင်းသွား စီးနင်းအပ်သော ယာဉ်များ (ကား၊ ဆိုင်ကယ်၊ လေယာဉ် စသည်) ကို ရေတွက်ရာ၌ 'စီး' မျိုးပြပစ္စည်းဖြင့်သာ တွဲဖက်ရသည် ('စင်း' ကို ရေယာဉ်နှင့် မီးရထားတို့၌ သုံးသည်)။";
            if (target.EndsWith("စင်း", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — အလျားရှည်လျားသော ရေကြောင်းသွားယာဉ်များ (သင်္ဘော၊ လှေ၊ ရေယာဉ်) နှင့် မီးရထားတို့ကို ရေတွက်ရာ၌ 'စင်း' မျိုးပြပစ္စည်းဖြင့်သာ တွဲဖက်ရသည်။";
            if (target.EndsWith("ပင်", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — သစ်ပင်၊ ဝါးပင်၊ ပန်းပင်နှင့် ပျိုးပင်များကို ရေတွက်ရာ၌ 'ပင်' မျိုးပြပစ္စည်းဖြင့် တွဲဖက်ရသည်။";
            if (target.EndsWith("လက်", StringComparison.Ordinal))
                return "မျိုးပြပစ္စည်း သဒ္ဒါနိယာမ — လက်ဖြင့် ကိုင်ဆောင်အသုံးပြုရသော လက်နက်၊ တူရိယာနှင့် ကိရိယာတန်ဆာပလာများ (သေနတ်၊ ဓား၊ ထီး၊ အပ် စသည်) ကို ရေတွက်ရာ၌ 'လက်' မျိုးပြပစ္စည်းဖြင့် တွဲဖက်ရသည်။";

            return string.Empty;
        }

        private static void AppendTargetLexicalSense(List<string> parts, string target, DictionaryEntry? targetEntry)
        {
            if (targetEntry == null || string.IsNullOrWhiteSpace(targetEntry.MeaningCue))
                return;

            string sense = TrimSense(targetEntry.MeaningCue, 85);
            string posLabel = DictionaryEntry.FormatPosTag(targetEntry.PosTag);
            if (sense == "ခေတ်သုံး စံသတ်ပုံအမှန်" || sense == "သတ်ပုံကျမ်း ခွဲထားအမှန်")
            {
                parts.Add($"စံဝေါဟာရ အတည်ပြုချက် — '{target}' ({posLabel}) သည် ရုံးသုံးနှင့် ခေတ်သုံး စံသတ်ပုံဝေါဟာရ ဖြစ်သည်။");
                return;
            }

            parts.Add($"အဘိဓာန်အနက် — '{target}' ({posLabel}) ၏ အနက်မှာ [{sense}] ဖြစ်သည်။");
        }

        private static string BuildSentenceBigramEvidence(
            string fullText,
            IReadOnlyList<SyllableToken> tokens,
            ProofingIssue issue,
            string target,
            MyanmarTrie trie)
        {
            if (string.IsNullOrEmpty(target) || target.IndexOf(' ') >= 0 || tokens == null || tokens.Count == 0)
                return string.Empty;

            int issueStart = issue.StartIndex;
            int issueEnd = issue.StartIndex + issue.Length;

            // Locate token index immediately preceding issueStart and immediately following issueEnd
            int leftEndTok = -1;
            int rightStartTok = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                SyllableToken t = tokens[i];
                if (t.StartIndex + t.Length <= issueStart && t.Kind == TokenKind.MyanmarSyllable)
                {
                    leftEndTok = i;
                }
                if (rightStartTok < 0 && t.StartIndex >= issueEnd && t.Kind == TokenKind.MyanmarSyllable)
                {
                    rightStartTok = i;
                }
            }

            string bestCollocation = string.Empty;
            int bestCount = 0;

            // Check up to 3 syllables on the left (contiguous in fullText)
            if (leftEndTok >= 0 && tokens[leftEndTok].StartIndex + tokens[leftEndTok].Length == issueStart)
            {
                for (int len = Math.Min(3, leftEndTok + 1); len >= 1; len--)
                {
                    int startTok = leftEndTok - len + 1;
                    if (!AreContiguousMyanmarSyllables(tokens, startTok, leftEndTok))
                        continue;
                    int sIdx = tokens[startTok].StartIndex;
                    int eIdx = tokens[leftEndTok].StartIndex + tokens[leftEndTok].Length;
                    string leftCandidate = fullText.Substring(sIdx, eIdx - sIdx);
                    int cnt = trie.GetBigramCount(leftCandidate, target);
                    if (cnt > bestCount)
                    {
                        bestCount = cnt;
                        bestCollocation = $"{leftCandidate} + {target}";
                    }
                }
            }

            // Check up to 3 syllables on the right (contiguous in fullText)
            if (rightStartTok >= 0 && tokens[rightStartTok].StartIndex == issueEnd)
            {
                int maxLen = Math.Min(3, tokens.Count - rightStartTok);
                for (int len = maxLen; len >= 1; len--)
                {
                    int endTok = rightStartTok + len - 1;
                    if (!AreContiguousMyanmarSyllables(tokens, rightStartTok, endTok))
                        continue;
                    int sIdx = tokens[rightStartTok].StartIndex;
                    int eIdx = tokens[endTok].StartIndex + tokens[endTok].Length;
                    string rightCandidate = fullText.Substring(sIdx, eIdx - sIdx);
                    int cnt = trie.GetBigramCount(target, rightCandidate);
                    if (cnt > bestCount)
                    {
                        bestCount = cnt;
                        bestCollocation = $"{target} + {rightCandidate}";
                    }
                }
            }

            // Also check internal constituent bigram when target is a fused multi-syllable compound (e.g. 'နိုင်ငံတော်သမ္မတ' -> 'နိုင်ငံတော် + သမ္မတ')
            if (bestCount < 5 && target.Length >= 3)
            {
                var intKey = (trie.MutationVersion, target);
                if (!s_internalConstituentBigramCache.TryGetValue(intKey, out var cachedInt))
                {
                    int bestIntCount = 0;
                    string bestIntColloc = string.Empty;
                    List<string> targSyls = MyanmarSyllableBreaker.BreakIntoSyllables(target);
                    if (targSyls.Count >= 3)
                    {
                        int charOffset = 0;
                        for (int split = 1; split < targSyls.Count; split++)
                        {
                            charOffset += targSyls[split - 1].Length;
                            string leftPart = target.Substring(0, charOffset);
                            string rightPart = target.Substring(charOffset);
                            int cnt = trie.GetBigramCount(leftPart, rightPart);
                            if (cnt > bestIntCount)
                            {
                                bestIntCount = cnt;
                                bestIntColloc = $"{leftPart} + {rightPart}";
                            }
                        }
                    }
                    cachedInt = (bestIntCount, bestIntColloc);
                    if (s_internalConstituentBigramCache.Count >= ReasoningCacheCap)
                        s_internalConstituentBigramCache.Clear();
                    s_internalConstituentBigramCache[intKey] = cachedInt;
                }

                if (cachedInt.Count > bestCount)
                {
                    bestCount = cachedInt.Count;
                    bestCollocation = cachedInt.Collocation;
                }
            }

            if (bestCount >= 5 && !string.IsNullOrEmpty(bestCollocation))
            {
                return $"ဝါကျအထောက်အထား — ဝါကျရှေ့နောက်အရ '{bestCollocation}' တွဲဖက်အသုံးအနှုန်းသည် ရုံးသုံးနှင့် စာပေကျမ်းဂန် သာဓကများ၌ အခိုင်အမာ တွေ့ရှိရပါသည်။";
            }

            return string.Empty;
        }

        private static bool AreContiguousMyanmarSyllables(IReadOnlyList<SyllableToken> tokens, int fromIdx, int toIdx)
        {
            for (int i = fromIdx; i <= toIdx; i++)
            {
                if (tokens[i].Kind != TokenKind.MyanmarSyllable)
                    return false;
                if (i > fromIdx && tokens[i - 1].StartIndex + tokens[i - 1].Length != tokens[i].StartIndex)
                    return false;
            }
            return true;
        }

        private static string DetermineAuthoritativeSource(
            ProofingIssue issue,
            string origNorm,
            string target,
            DictionaryEntry? targetEntry,
            ProofingStyleMode styleMode)
        {
            if (issue.IssueType == ProofingIssueType.ZawgyiEncodingError ||
                issue.IssueType == ProofingIssueType.LookalikeCharacterError)
            {
                return "မြန်မာယူနီကုဒ် စံသတ်မှတ်ချက် (ISO/IEC 10646 & Unicode UTN #11 — Pyidaungsu Standard)";
            }

            if (issue.IssueType == ProofingIssueType.PunctuationError)
            {
                return "မြန်မာစာအဖွဲ့ ပုဒ်ဖြတ်ပုဒ်ရပ် သုံးစွဲနည်း လမ်းညွှန်ကျမ်း";
            }

            if (issue.IssueType == ProofingIssueType.NumeralClassifierError)
            {
                return "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (သင်္ချာ 'တစ်' နှင့် 'တ' ခွဲခြားရေးထုံး — မြန်မာစာအဖွဲ့)";
            }

            if (issue.IssueType == ProofingIssueType.OfficialStyleGrammarError)
            {
                if (!string.IsNullOrEmpty(TryAnalyzeClassifierAgreement(issue.Message, origNorm, target)))
                    return "မြန်မာအဘိဓာန် (မျိုးပြပစ္စည်းစနစ်) နှင့် မြန်မာသဒ္ဒါ (မြန်မာစာအဖွဲ့)";
                return "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မြန်မာသဒ္ဒါ (မြန်မာစာအဖွဲ့)";
            }

            if (issue.IssueType == ProofingIssueType.ContextualMeaningWarning)
            {
                return "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ခွဲထား) နှင့် တွဲဖက်အနက် စိစစ်ရေးစနစ်";
            }

            if (targetEntry != null)
            {
                if (targetEntry.IsProperNoun)
                {
                    return IsDomesticMyanmarProperNoun(target, targetEntry)
                        ? "အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာန (GAD) နယ်မြေဒေသစာရင်း နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း"
                        : "နိုင်ငံခြားရေးဝန်ကြီးဌာန (MOFA) နိုင်ငံတကာနှင့် သံတမန်ရေးရာ ရုံးသုံးစံသတ်ပုံညွှန်း";
                }

                if (targetEntry.IsSpecializedDomain)
                {
                    string auth = !string.IsNullOrEmpty(targetEntry.AuthorityStandard)
                        ? targetEntry.AuthorityStandard
                        : GetSpecializedDomainAuthority(targetEntry.SpecializedDomain);
                    return $"{auth} နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း";
                }

                if (targetEntry.PosTag == "LOAN")
                    return "မြန်မာစာအဖွဲ့ ခေတ်သုံး မွေးစားဝေါဟာရ စံသတ်ပုံညွှန်း နှင့် မြန်မာအဘိဓာန်";

                if (!string.IsNullOrEmpty(targetEntry.PaliEquivalent) || PatSintClusterRegex.IsMatch(target))
                    return "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း၊ ပါဠိသက် ဝဂ္ဂန္တဗျည်းစဉ်ထုံး နှင့် ဦးဟုတ်စိန် မြန်မာ-ပါဠိ အဘိဓာန်";

                if (targetEntry.IsOfficialThatpon && targetEntry.HasAbidanMetadata)
                    return "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (စာ-၄၆၂ အပြည့်အစုံ) နှင့် မြန်မာအဘိဓာန် (မြန်မာစာအဖွဲ့)";

                if (targetEntry.IsOfficialThatpon)
                    return "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (စာ-၄၆၂ အပြည့်အစုံ — မြန်မာစာအဖွဲ့)";
            }

            return "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာအဘိဓာန် (မြန်မာစာအဖွဲ့)";
        }

        private static bool IsDomesticMyanmarProperNoun(string target, DictionaryEntry targetEntry)
        {
            string uNote = targetEntry.UsageNote ?? string.Empty;
            string mCue = targetEntry.MeaningCue ?? string.Empty;
            string domain = targetEntry.SpecializedDomain ?? string.Empty;
            return uNote.IndexOf("GAD", StringComparison.Ordinal) >= 0 ||
                   uNote.IndexOf("Pcode:", StringComparison.Ordinal) >= 0 ||
                   uNote.IndexOf("မြန်မာနိုင်ငံ", StringComparison.Ordinal) >= 0 ||
                   uNote.IndexOf("မြန်မာ့", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("GAD", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("Pcode:", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("မြန်မာနိုင်ငံ", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("မြန်မာ့", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("တိုင်းဒေသကြီး", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("ပြည်နယ်", StringComparison.Ordinal) >= 0 ||
                   mCue.IndexOf("မြို့နယ်", StringComparison.Ordinal) >= 0 ||
                   domain.IndexOf("မြန်မာ", StringComparison.Ordinal) >= 0 ||
                   target.EndsWith("မြို့နယ်", StringComparison.Ordinal) ||
                   target.EndsWith("တိုင်းဒေသကြီး", StringComparison.Ordinal) ||
                   target.EndsWith("ပြည်နယ်", StringComparison.Ordinal);
        }

        private static string GetSpecializedDomainAuthority(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return "ပညာရပ်ဆိုင်ရာ ရုံးသုံးစံသတ်ပုံညွှန်း";
            string d = domain!;
            if (d.IndexOf("ဥပဒေ", StringComparison.Ordinal) >= 0 || d.IndexOf("တရား", StringComparison.Ordinal) >= 0)
                return "ပြည်ထောင်စုတရားလွှတ်တော်ချုပ် နှင့် ဥပဒေရေးရာဝန်ကြီးဌာန စံဝေါဟာရညွှန်း";
            if (d.IndexOf("ဆေး", StringComparison.Ordinal) >= 0 || d.IndexOf("ကျန်းမာ", StringComparison.Ordinal) >= 0)
                return "ကျန်းမာရေးဝန်ကြီးဌာန (MOH / FDA) ဆေးပညာစံဝေါဟာရညွှန်း";
            if (d.IndexOf("ဘဏ်", StringComparison.Ordinal) >= 0 || d.IndexOf("အခွန်", StringComparison.Ordinal) >= 0 || d.IndexOf("ဘဏ္ဍာ", StringComparison.Ordinal) >= 0)
                return "မြန်မာနိုင်ငံတော်ဗဟိုဘဏ် (CBM) နှင့် ပြည်တွင်းအခွန်များဦးစီးဌာန (IRD) စံဝေါဟာရညွှန်း";
            if (d.IndexOf("နည်းပညာ", StringComparison.Ordinal) >= 0 || d.IndexOf("သတင်းအချက်အလက်", StringComparison.Ordinal) >= 0)
                return "မြန်မာနိုင်ငံကွန်ပျူတာအသင်းချုပ် (MCF) နှင့် ပို့ဆောင်ရေးနှင့်ဆက်သွယ်ရေးဝန်ကြီးဌာန (MOTC) စံဝေါဟာရညွှန်း";
            if (d.IndexOf("ပါဠိ", StringComparison.Ordinal) >= 0 || d.IndexOf("သာသနာ", StringComparison.Ordinal) >= 0)
                return "သာသနာရေးနှင့် ယဉ်ကျေးမှုဝန်ကြီးဌာန / ဆဋ္ဌသင်္ဂါယနာမူ ပိဋကတ်တော် စံသတ်ပုံညွှန်း";
            if (d.IndexOf("ပထဝီဝင်", StringComparison.Ordinal) >= 0 || d.IndexOf("ဒေသန္တရ", StringComparison.Ordinal) >= 0)
                return "အထွေထွေအုပ်ချုပ်ရေးဦးစီးဌာန (GAD) နှင့် နိုင်ငံခြားရေးဝန်ကြီးဌာန (MOFA) ပထဝီဝင်စံညွှန်း";
            return "ပညာရပ်ဆိုင်ရာ ရုံးသုံးစံသတ်ပုံညွှန်း";
        }

        private static DictionaryEntry? GetPrimaryEntry(MyanmarTrie trie, string word)
        {
            if (string.IsNullOrEmpty(word))
                return null;
            if (trie.TryGetExactEntries(word, out var entries) && entries != null && entries.Count > 0)
            {
                var primary = entries
                    .OrderByDescending(e => e.HasAbidanMetadata)
                    .ThenByDescending(e => e.IsOfficialThatpon)
                    .First();

                if (!primary.IsProperNoun)
                {
                    var propn = entries.FirstOrDefault(e => e.IsProperNoun);
                    if (propn != null &&
                        (primary.SpecializedDomain.IndexOf("ပထဝီဝင်", StringComparison.Ordinal) >= 0 ||
                         primary.MeaningCue.IndexOf("မြို့နယ်", StringComparison.Ordinal) >= 0 ||
                         primary.MeaningCue.IndexOf("တိုင်းဒေသကြီး", StringComparison.Ordinal) >= 0 ||
                         primary.MeaningCue.IndexOf("ပြည်နယ်", StringComparison.Ordinal) >= 0 ||
                         primary.MeaningCue.IndexOf("မြို့အမည်", StringComparison.Ordinal) >= 0))
                    {
                        return propn;
                    }
                }

                return primary;
            }
            return null;
        }

        private static string GetPaliVaggaName(char c)
        {
            if (c >= '\u1000' && c <= '\u1004') return "က-ဝဂ် (က၊ ခ၊ ဂ၊ ဃ၊ င)";
            if (c >= '\u1005' && c <= '\u1009') return "စ-ဝဂ် (စ၊ ဆ၊ ဇ၊ ဈ၊ ဉ)";
            if (c >= '\u100B' && c <= '\u100F') return "ဋ-ဝဂ် (ဋ၊ ဌ၊ ဍ၊ ဎ၊ ဏ)";
            if (c >= '\u1010' && c <= '\u1014') return "တ-ဝဂ် (တ၊ ထ၊ ဒ၊ ဓ၊ န)";
            if (c >= '\u1015' && c <= '\u1019') return "ပ-ဝဂ် (ပ၊ ဖ၊ ဗ၊ ဘ၊ မ)";
            return "အဝဂ်ဗျည်း";
        }

        private static string DescribePaliStackPair(char top, char bot, string vaggaName)
        {
            if (top == bot)
            {
                return $"ပါဠိသက် {vaggaName} စည်းကမ်းအရ သံတူဗျည်းနှစ်လုံးဆင့်ရာ၌ အသံမပြင်းသော ပထမ/တတိယ/ပဉ္စမ အက္ခရာအချင်းချင်း ('{GetConsonantShortName(top)}' နှစ်လုံးဆင့်) တွဲစပ်ရသည်။";
            }
            if ((top == 'က' && bot == 'ခ') || (top == 'စ' && bot == 'ဆ') || (top == 'ဋ' && bot == 'ဌ') ||
                (top == 'တ' && bot == 'ထ') || (top == 'ပ' && bot == 'ဖ'))
            {
                return $"ပါဠိ {vaggaName} ပါဌ်ဆင့်စည်းကမ်းအရ ဒုတိယအက္ခရာ ('{GetConsonantShortName(bot)}') အပေါ်၌ ပထမအက္ခရာ ('{GetConsonantShortName(top)}') ကိုသာ တင်၍ ဆင့်ရသည် (ဒုတိယအက္ခရာနှစ်လုံး ထပ်မဆင့်ရပါ)။";
            }
            if ((top == 'ဂ' && bot == 'ဃ') || (top == 'ဇ' && bot == 'ဈ') || (top == 'ဍ' && bot == 'ဎ') ||
                (top == 'ဒ' && bot == 'ဓ') || (top == 'ဗ' && bot == 'ဘ'))
            {
                return $"ပါဠိ {vaggaName} ပါဌ်ဆင့်စည်းကမ်းအရ စတုတ္ထအက္ခရာ ('{GetConsonantShortName(bot)}') အပေါ်၌ တတိယအက္ခရာ ('{GetConsonantShortName(top)}') ကိုသာ တင်၍ ဆင့်ရသည် (စတုတ္ထအက္ခရာနှစ်လုံး ထပ်မဆင့်ရပါ)။";
            }
            if (top == 'ဉ' || top == 'ဏ' || top == 'န' || top == 'မ')
            {
                return $"ပါဠိ {vaggaName} ဝဂ္ဂန္တနှာသံစည်းကမ်းအရ ဝဂ်၏ ပဉ္စမနှာသံအက္ခရာ ('{GetConsonantShortName(top)}') သည် ၎င်းဝဂ်အတွင်းရှိ '{GetConsonantShortName(bot)}' အပေါ်၌သာ အစဉ်လိုက် ဆင့်ရသည်။";
            }
            return $"ပါဠိသက် ပါဌ်ဆင့်စည်းကမ်းအရ '{GetConsonantShortName(top)}' နှင့် '{GetConsonantShortName(bot)}' တို့ကို အစဉ်အတိုင်း ဆင့်ရသည်။";
        }

        private static char GetBaseConsonant(string syllable)
        {
            if (string.IsNullOrEmpty(syllable))
                return '\0';
            int start = syllable.StartsWith("\u1004\u103A\u1039", StringComparison.Ordinal) ? 3 : 0;
            if (start < syllable.Length && syllable[start] >= '\u1000' && syllable[start] <= '\u1021')
                return syllable[start];
            return '\0';
        }

        private static bool IsVoicedStop(char c) =>
            c == 'ဂ' || c == 'ဇ' || c == 'ဒ' || c == 'ဗ' || c == 'ဘ';

        private static bool IsVoicelessOrAspiratedCounterpart(char voiced, char voiceless)
        {
            switch (voiced)
            {
                case 'ဂ': return voiceless == 'က' || voiceless == 'ခ';
                case 'ဇ': return voiceless == 'စ' || voiceless == 'ဆ';
                case 'ဒ': return voiceless == 'တ' || voiceless == 'ထ';
                case 'ဗ':
                case 'ဘ': return voiceless == 'ပ' || voiceless == 'ဖ';
                default: return false;
            }
        }

        private static bool HasNasalCoda(string s) =>
            s.Contains("န်") || s.Contains("မ်") || s.Contains("ဏ်") || s.Contains("ံ") || s.Contains("င်") || s.Contains("ဉ်");

        private static bool HasStopCoda(string s) =>
            s.Contains("က်") || s.Contains("တ်") || s.Contains("ပ်") || s.Contains("စ်");

        private static string GetConsonantShortName(char c)
        {
            switch (c)
            {
                case 'က': return "ကကြီး";
                case 'ခ': return "ခကွေး";
                case 'ဂ': return "ဂငယ်";
                case 'ဃ': return "ဃကြီး";
                case 'င': return "င";
                case 'စ': return "စလုံး";
                case 'ဆ': return "ဆလိမ်";
                case 'ဇ': return "ဇကွဲ";
                case 'ဈ': return "ဈမျဉ်းဆွဲ";
                case 'ဉ': return "ဉကလေး";
                case 'ည': return "ညကြီး";
                case 'ဋ': return "ဋသံလျင်းချိတ်";
                case 'ဌ': return "ဌဝမ်းဘဲ";
                case 'ဍ': return "ဍရင်ကောက်";
                case 'ဎ': return "ဎရေမှုတ်";
                case 'ဏ': return "ဏကြီး";
                case 'တ': return "တဝမ်းပူ";
                case 'ထ': return "ထဆင်ထူး";
                case 'ဒ': return "ဒထွေး";
                case 'ဓ': return "ဓအောက်ခြိုက်";
                case 'န': return "နငယ်";
                case 'ပ': return "ပစောက်";
                case 'ဖ': return "ဖဦးထုပ်";
                case 'ဗ': return "ဗထက်ခြိုက်";
                case 'ဘ': return "ဘကုန်း";
                case 'မ': return "မ";
                case 'ယ': return "ယပက်လက်";
                case 'ရ': return "ရကောက်";
                case 'လ': return "လ";
                case 'ဝ': return "ဝလုံး";
                case 'သ': return "သ";
                case 'ဟ': return "ဟ";
                case 'ဠ': return "ဠကြီး";
                case 'အ': return "အ";
                default: return c.ToString();
            }
        }

        private static string TrimSense(string raw, int maxLen)
        {
            string clean = raw.Trim();
            if (clean.Length <= maxLen)
                return clean;
            return clean.Substring(0, maxLen - 3).TrimEnd() + "...";
        }
    }
}
