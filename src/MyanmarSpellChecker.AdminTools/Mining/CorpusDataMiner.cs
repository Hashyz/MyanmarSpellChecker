// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.AdminTools.Mining
{
    /// <summary>
    /// Reusable 4-Pillar Empirical Burmese Corpus &amp; Lexicon Data-Mining Engine (`AdminTools.exe mine-corpus`):
    /// 1. Pillar 1 — Multi-Domain Vocabulary, Idioms &amp; Assimilation Orthography Pair Miner
    /// 2. Pillar 2 — Bilingual Meaning, Classifier, Synonym, PMI Collocation &amp; Clean Example Sentence Miner
    /// 3. Pillar 3 — Zero-FP Discriminative PMI Homophone &amp; Tone/Medial Context Rule Miner
    /// 4. Pillar 4 — Zero-FP O(1) Grammar Pattern Miner (Noun->Classifier Concord, Mandatory Compounds, Verb Valence)
    /// </summary>
    public static class CorpusDataMiner
    {
        private static readonly Regex PureMyanmarWordRx = new Regex(@"^[\u1000-\u103F\u104C-\u104F]{2,42}$", RegexOptions.Compiled);
        private static readonly Regex MyanmarTokenScanRx = new Regex(@"[\u1000-\u103F\u104C-\u104F]+", RegexOptions.Compiled);
        private static readonly Regex BigramTupleRx = new Regex(@"^\('([^']+)',\s*'([^']+)'\)\t(\d+)$", RegexOptions.Compiled);
        private static readonly Regex ZawgyiArtifactRx = new Regex(@"[\u1033\u1034\u105A\u1060-\u1097]|(?:^|\s)\u1031[\u1000-\u1021]", RegexOptions.Compiled);
        private static readonly Regex ClassifierTagRx = new Regex(@"မျိုးပြပစ္စည်း:\s*([^|\r\n]+)", RegexOptions.Compiled);
        private static readonly Regex ExampleTagRx = new Regex(@"သာဓက:\s*([^|\r\n]+)", RegexOptions.Compiled);
        private static readonly Regex VulgarEnglishRx = new Regex(@"\b(arse|arsehole|ass|asshole|bitch|fuck|fucking|shit|bullshit|bastard|whore|slut|cunt|dick|cock|penis|vagina|anus|piss|masturbat\w*|porn\w*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> StopParticlesAndFunctionWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "သည်", "တယ်", "မည်", "မယ်", "ပါ", "ပါသည်", "ပါတယ်", "ပါမည်", "ပါမယ်", "ပြီ", "ပြီး", "ဘူး",
            "ကို", "အား", "မှ", "က", "ကနေ", "သို့", "နှင့်", "နဲ့", "၏", "ရဲ့", "တွင်", "၌", "မှာ", "ဝယ်",
            "ဖြင့်", "သဖြင့်", "ကြောင့်", "သောကြောင့်", "သော်လည်း", "ပေမဲ့", "ပေမယ့်", "၍", "လျက်", "ကာ",
            "များ", "တွေ", "တို့", "သော", "သည့်", "မည့်", "တဲ့", "တာ", "လည်း", "လဲ", "ပင်", "သာ", "ပဲ", "ဘဲ",
            "ဟု", "လို့", "ခဲ့", "ကြ", "နိုင်", "ချင်", "တတ်", "သင့်", "ရ", "နေ", "ထား", "ပေး", "စေ", "လိုက်",
            "ဤ", "ထို", "ယင်း", "အဆိုပါ", "ဒီ", "ဟို", "အဲဒီ", "သူ", "ငါ", "မင်း", "သင်", "ကျွန်တော်", "ကျွန်မ", "၎င်း",
            "တစ်", "နှစ်", "သုံး", "လေး", "ငါး", "ခြောက်", "ခုနစ်", "ရှစ်", "ကိုး", "ဆယ်", "ရာ", "ထောင်", "သောင်း", "သိန်း", "သန်း",
            "ခြင်း", "မှု", "ရေး", "ချက်", "ရာ", "စဉ်", "တိုင်း", "ဖို့", "ရန်", "အတွက်", "အပေါ်", "အောက်", "အတွင်း", "အပြင်"
        };

        private static readonly string[] TrailingInflectionSuffixes =
        {
            "ပါသည်", "ပါတယ်", "ခဲ့သည်", "ခဲ့တယ်", "ဖြစ်သည်", "ရှိသည်", "ကြသည်", "ကြတယ်",
            "များကို", "တွေကို", "များက", "တွေက", "များသည်", "တွေဟာ", "သဖြင့်", "သောကြောင့်",
            "သည်", "တယ်", "မည်", "မယ်", "သည့်", "သော", "မည့်", "များ", "တွေ", "တို့",
            "ကို", "အား", "တွင်", "၌", "ဖြင့်", "ကြောင့်"
        };

        private static readonly HashSet<string> BoundCompoundStems = new HashSet<string>(StringComparer.Ordinal)
        {
            "ဗေဒ", "သိပ္ပံ", "ဘူမိ", "ဇီဝ", "ဓာတု", "ရူပ", "နက္ခတ္တ", "ဂေဟ", "မနုဿ", "စိတ်ပညာ",
            "တက္က", "သိုလ်", "ဗိသုကာ", "ဒီမို", "ကရေစီ", "တယ်လီ", "ကွန်ပျူ", "အီလက်", "အင်တာ",
            "သယံ", "ဇာတ", "ပတ်ဝန်း", "ဝန်းကျင်", "သင်္ကေတ", "ဥပဒေ", "ပရိသတ်", "အခမ်း", "အနား",
            "အစည်း", "အဝေး", "အထိမ်း", "အမှတ်", "အဆောက်", "အအုံ", "အချက်", "အလက်", "အတွေ့", "အကြုံ"
        };

        private static readonly Dictionary<string, (string Label, string[] WrongSet)> SortalClassifierProfiles =
            new Dictionary<string, (string Label, string[] WrongSet)>(StringComparer.Ordinal)
            {
                ["ပါး"] = ("ရဟန်းသံဃာ/မြင့်မြတ်သူပြ မျိုးပြပစ္စည်း ('~ပါး')", new[] { "ယောက်", "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်" }),
                ["ဆူ"] = ("ဘုရား/စေတီ/မြင့်မြတ်အရာပြ မျိုးပြပစ္စည်း ('~ဆူ')", new[] { "ယောက်", "ဦး", "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "ရံ", "ပင်", "ကွက်" }),
                ["စီး"] = ("ယာဉ်/စီးတော်သတ္တဝါပြ မျိုးပြပစ္စည်း ('~စီး')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်" }),
                ["စင်း"] = ("ရေယာဉ်/လေယာဉ်/မြစ်ချောင်းပြ မျိုးပြပစ္စည်း ('~စင်း')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်" }),
                ["ထည်"] = ("အဝတ်အထည်ပြ မျိုးပြပစ္စည်း ('~ထည်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "အုပ်", "စောင်", "ပုဒ်", "ပင်", "လက်" }),
                ["ချပ်"] = ("အပြားလိုက်ရှိသောအရာပြ မျိုးပြပစ္စည်း ('~ချပ်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "အုပ်", "ပုဒ်", "ရံ", "ပင်" }),
                ["ရံ"] = ("အစုံလိုက်ဝတ်ဆင်ရသောပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ရံ')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "အုပ်", "စောင်", "ပုဒ်", "ပင်" }),
                ["အုပ်"] = ("စာအုပ်/ကျမ်းဂန်ပြ မျိုးပြပစ္စည်း ('~အုပ်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "ထည်", "ရံ", "ပင်", "လက်" }),
                ["စောင်"] = ("စာရွက်စာတမ်း/သတင်းစာပြ မျိုးပြပစ္စည်း ('~စောင်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "ထည်", "ရံ", "ပင်", "လက်" }),
                ["ပုဒ်"] = ("စာပေ/ကဗျာ/သီချင်းပြ မျိုးပြပစ္စည်း ('~ပုဒ်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "ရံ", "ပင်" }),
                ["လက်"] = ("လက်နက်/ကိရိယာပြ မျိုးပြပစ္စည်း ('~လက်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်" }),
                ["ချောင်း"] = ("အချောင်းလိုက်ရှိသောပစ္စည်းပြ မျိုးပြပစ္စည်း ('~ချောင်း')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "ထည်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်" }),
                ["ကောင်"] = ("သတ္တဝါ/တိရစ္ဆာန်ပြ မျိုးပြပစ္စည်း ('~ကောင်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "စင်း", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်", "လက်" }),
                ["ပင်"] = ("သစ်ပင်/အပင်ပြ မျိုးပြပစ္စည်း ('~ပင်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "လက်" }),
                ["ဆောင်"] = ("အဆောက်အအုံ/ကျောင်းဆောင်ပြ မျိုးပြပစ္စည်း ('~ဆောင်')", new[] { "ယောက်", "ဦး", "ပါး", "ဆူ", "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "အုပ်", "ရံ", "ပင်" }),
                ["ဦး"] = ("ပုဂ္ဂိုလ်/လူပုဂ္ဂိုလ်ပြ မျိုးပြပစ္စည်း ('~ဦး')", new[] { "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်", "ဆူ", "လက်" }),
                ["ယောက်"] = ("လူပုဂ္ဂိုလ်ပြ မျိုးပြပစ္စည်း ('~ယောက်')", new[] { "ကောင်", "စီး", "စင်း", "ထည်", "ချပ်", "အုပ်", "စောင်", "ပုဒ်", "ရံ", "ပင်", "ဆူ", "လက်" })
            };

        private static readonly Dictionary<string, string> DomainDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["geology"] = "ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ (Geology & Earth Sciences)",
            ["legal"] = "ဥပဒေနှင့် တရားစီရင်ရေး (Law & Judiciary)",
            ["medical"] = "ဆေးပညာနှင့် ကျန်းမာရေး (Medicine & Healthcare)",
            ["finance"] = "ဘဏ္ဍာရေး၊ ဘောဂဗေဒနှင့် စီးပွားရေး (Finance, Economics & Business)",
            ["it"] = "သတင်းအချက်အလက်နည်းပညာနှင့် ကွန်ပျူတာသိပ္ပံ (IT & Computer Science)",
            ["science"] = "သိပ္ပံနှင့် နည်းပညာ (Natural & Applied Sciences)",
            ["education"] = "ပညာရေးနှင့် အဆင့်မြင့်သုတေသန (Higher Education & Research)",
            ["admin"] = "ပြည်သူ့အုပ်ချုပ်ရေးနှင့် ဝန်ထမ်းရေးရာ (Public Administration)",
            ["diplomacy"] = "သံတမန်ရေးရာနှင့် နိုင်ငံတကာအဖွဲ့အစည်းများ (Diplomacy & International Affairs)",
            ["governance"] = "မြန်မာ့အုပ်ချုပ်ရေးနှင့် ယဉ်ကျေးမှုအမွေအနှစ် (Governance & Cultural Heritage)",
            ["pali"] = "ပါဠိနှင့် ဗုဒ္ဓစာပေ (Pali & Buddhist Studies)",
            ["general"] = "ရုံးသုံးနှင့် အထွေထွေခေတ်သုံးဝေါဟာရ (General & Office Terminology)"
        };

        public sealed class MiningSummary
        {
            public int CleanSentencesIndexed { get; set; }
            public int BigramPairsIndexed { get; set; }
            public int DomainHeadwordsMined { get; set; }
            public int IdiomsTotal { get; set; }
            public int OrthographyPairsMined { get; set; }
            public int EntryEnrichmentsMined { get; set; }
            public int PmiContextRulesMined { get; set; }
            public int NounClassifiersMined { get; set; }
            public int MandatoryCompoundsMined { get; set; }
            public int VerbValenceRulesMined { get; set; }
            public double ElapsedSeconds { get; set; }
        }

        public static MiningSummary Run(string dataDir, string docsDir)
        {
            var sw = Stopwatch.StartNew();
            string sourcesDir = Path.Combine(dataDir, "sources");
            string corporaDir = Path.Combine(sourcesDir, "corpora");
            string dictDir = Path.Combine(sourcesDir, "dictionaries");
            string domainsDir = Path.Combine(sourcesDir, "domains");
            string rulesDir = Path.Combine(sourcesDir, "rules");
            string masterJsonPath = Path.Combine(dataDir, "mlc_thatpon_master.json");

            Console.WriteLine("[Mine 1/5] Loading Master Lexicon & indexing clean Burmese corpora + 1.23M+ bigrams...");
            var masterModel = EncryptedDictionaryRepository.ParseFromJsonFile(masterJsonPath);

            var activeWords = new HashSet<string>(StringComparer.Ordinal);
            var officialWords = new HashSet<string>(StringComparer.Ordinal);
            var officialSyllables = new HashSet<string>(StringComparer.Ordinal);
            var condemnedWrongSet = new HashSet<string>(StringComparer.Ordinal);
            var existingWordFreq = new Dictionary<string, int>(StringComparer.Ordinal);
            var existingWordPos = new Dictionary<string, string>(StringComparer.Ordinal);
            var existingHasEn = new HashSet<string>(StringComparer.Ordinal);
            var existingHasExample = new HashSet<string>(StringComparer.Ordinal);
            var existingHasColloc = new HashSet<string>(StringComparer.Ordinal);

            foreach (var r in masterModel.OrthographyRules)
            {
                if (!string.Equals(r.Category, "OfficialStyle", StringComparison.OrdinalIgnoreCase))
                    condemnedWrongSet.Add(Norm(r.WrongForm));
            }
            foreach (var e in masterModel.Entries)
            {
                string w = Norm(e.Word);
                if (string.IsNullOrEmpty(w)) continue;
                bool isRef = e.PosTag == "ENCYC" || e.PosTag == "PROVERB" || e.PosTag == "SAYING" || e.PosTag == "RHETORIC" || e.PosTag == "EN_LEX";
                if (!isRef && w.IndexOf(' ') < 0)
                {
                    activeWords.Add(w);
                    if (!existingWordFreq.TryGetValue(w, out int prevF) || e.Frequency > prevF)
                        existingWordFreq[w] = e.Frequency;
                    if (!existingWordPos.ContainsKey(w) && !string.IsNullOrEmpty(e.PosTag))
                        existingWordPos[w] = e.PosTag;

                    string u = e.UsageNote ?? string.Empty;
                    if (u.IndexOf("အင်္ဂလိပ်", StringComparison.Ordinal) >= 0) existingHasEn.Add(w);
                    if (u.IndexOf("သာဓက:", StringComparison.Ordinal) >= 0) existingHasExample.Add(w);
                    if (u.IndexOf("တွဲဖက်အသုံး:", StringComparison.Ordinal) >= 0) existingHasColloc.Add(w);
                }
                if (e.IsOfficialThatpon && w.IndexOf(' ') < 0)
                {
                    officialWords.Add(w);
                    foreach (string st in MyanmarSyllableBreaker.BreakIntoSyllables(w, isPreNormalized: true))
                    {
                        if (!string.IsNullOrEmpty(st))
                            officialSyllables.Add(st);
                    }
                }
            }

            // Load Clean Burmese Sentences (mypos_v3_upos + public_wiki_sentences + Abidan examples)
            var cleanSentences = new List<string>(80000);
            var cleanShortExamples = new List<string>(32000);
            var myPosCompounds = new Dictionary<string, (string Left, string Right, string Pos, int Count)>(StringComparer.Ordinal);
            var corpusNounClfCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

            string myPosPath = Path.Combine(corporaDir, "mypos_v3_upos.txt");
            if (File.Exists(myPosPath))
            {
                foreach (string rawLine in File.ReadAllLines(myPosPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] rawToks = rawLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var surfaceWords = new List<(string Word, string Pos)>(rawToks.Length);
                    var sbSent = new StringBuilder(rawLine.Length);

                    for (int ti = 0; ti < rawToks.Length; ti++)
                    {
                        string rt = rawToks[ti];
                        if (rt.IndexOf('|') >= 0)
                        {
                            // Compound like 'နတ်/NOUN|ဆရာ/NOUN'
                            string[] subParts = rt.Split('|');
                            var subWords = new List<string>(subParts.Length);
                            string lastPos = "N";
                            for (int sp = 0; sp < subParts.Length; sp++)
                            {
                                int sl = subParts[sp].LastIndexOf('/');
                                if (sl > 0)
                                {
                                    subWords.Add(Norm(subParts[sp].Substring(0, sl)));
                                    lastPos = subParts[sp].Substring(sl + 1);
                                }
                            }
                            if (subWords.Count == 2 && subWords[0].Length >= 1 && subWords[1].Length >= 1)
                            {
                                string joined = subWords[0] + subWords[1];
                                if (PureMyanmarWordRx.IsMatch(joined))
                                {
                                    if (myPosCompounds.TryGetValue(joined, out var prevC))
                                        myPosCompounds[joined] = (prevC.Left, prevC.Right, prevC.Pos, prevC.Count + 1);
                                    else
                                        myPosCompounds[joined] = (subWords[0], subWords[1], MapUposToLexPos(lastPos), 1);
                                }
                            }
                            string wholeWord = string.Concat(subWords);
                            surfaceWords.Add((wholeWord, lastPos));
                            if (sbSent.Length > 0 && wholeWord != "။" && wholeWord != "၊") sbSent.Append(' ');
                            sbSent.Append(wholeWord);
                        }
                        else
                        {
                            int sl = rt.LastIndexOf('/');
                            string sw0 = sl > 0 ? Norm(rt.Substring(0, sl)) : Norm(rt);
                            string sp0 = sl > 0 ? rt.Substring(sl + 1) : "";
                            surfaceWords.Add((sw0, sp0));
                            if (sbSent.Length > 0 && sw0 != "။" && sw0 != "၊") sbSent.Append(' ');
                            sbSent.Append(sw0);
                        }
                    }

                    // Extract [NOUN + NUM + CLF] empirical observations
                    for (int ti = 0; ti + 2 < surfaceWords.Count; ti++)
                    {
                        if (surfaceWords[ti].Pos == "NOUN" &&
                            (surfaceWords[ti + 1].Pos.StartsWith("NUM", StringComparison.Ordinal) || IsMyanmarNumeralWord(surfaceWords[ti + 1].Word)))
                        {
                            string nWord = surfaceWords[ti].Word;
                            string cWord = surfaceWords[ti + 2].Word;
                            if (SortalClassifierProfiles.ContainsKey(cWord) && PureMyanmarWordRx.IsMatch(nWord))
                            {
                                AddNestedCount(corpusNounClfCounts, nWord, cWord, 1);
                            }
                        }
                    }

                    string cleanS = sbSent.ToString().Trim();
                    if (cleanS.Length >= 10 && !ZawgyiArtifactRx.IsMatch(cleanS))
                    {
                        cleanSentences.Add(cleanS);
                        if (cleanS.Length >= 20 && cleanS.Length <= 58 && cleanS.EndsWith("။", StringComparison.Ordinal) && IsCleanExampleCandidate(cleanS, condemnedWrongSet))
                            cleanShortExamples.Add(cleanS);
                    }
                }
            }

            string pubWikiSentsPath = Path.Combine(corporaDir, "public_wiki_sentences.txt");
            if (File.Exists(pubWikiSentsPath))
            {
                foreach (string line in File.ReadAllLines(pubWikiSentsPath, Encoding.UTF8))
                {
                    string s = Norm(line.Trim());
                    if (s.Length >= 12 && !ZawgyiArtifactRx.IsMatch(s))
                    {
                        cleanSentences.Add(s);
                        if (s.Length >= 22 && s.Length <= 58 && s.EndsWith("။", StringComparison.Ordinal) && IsCleanExampleCandidate(s, condemnedWrongSet))
                            cleanShortExamples.Add(s);
                    }
                }
            }

            // Parse Abidan structured tags (classifiers + examples + idioms + domain entries)
            var abidanClassifiers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var abidanIdioms = new List<(string Phrase, string Pos, string Def)>();
            string abidanPath = Path.Combine(dictDir, "mlc_mm_to_mm_dictionary.tsv");
            if (File.Exists(abidanPath))
            {
                foreach (string rawLine in File.ReadAllLines(abidanPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] cols = rawLine.Split('\t');
                    if (cols.Length < 4) continue;
                    string w = Norm(cols[0].Trim());
                    string pos = cols[1].Trim();
                    string def = Norm(cols[2].Trim());
                    string note = Norm(cols[3].Trim());

                    Match mClf = ClassifierTagRx.Match(note);
                    if (mClf.Success && PureMyanmarWordRx.IsMatch(w))
                    {
                        var clfs = mClf.Groups[1].Value
                            .Split(new[] { '၊', ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => Norm(x.Trim()))
                            .Where(x => !string.IsNullOrEmpty(x))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                        if (clfs.Count > 0)
                            abidanClassifiers[w] = clfs;
                    }

                    Match mEx = ExampleTagRx.Match(note);
                    if (mEx.Success)
                    {
                        string ex = mEx.Groups[1].Value.Trim();
                        if (ex.Length >= 10 && !ZawgyiArtifactRx.IsMatch(ex))
                            cleanSentences.Add(ex);
                    }

                    if ((def.IndexOf("ဥပစာ", StringComparison.Ordinal) >= 0 ||
                         def.IndexOf("[အီဒီယမ်]", StringComparison.Ordinal) >= 0 ||
                         note.IndexOf("အီဒီယမ်", StringComparison.Ordinal) >= 0) &&
                        w.Length >= 4 && !string.IsNullOrEmpty(def))
                    {
                        string cleanDef = Regex.Replace(def, @"\[\s*ဥပစာ\s*\]|\[အီဒီယမ်\]", "").Trim();
                        if (cleanDef.Length >= 6 && HasBalancedParens(cleanDef) && cleanDef != w)
                        {
                            abidanIdioms.Add((w, "SAYING", cleanDef));
                        }
                    }
                }
            }

            // Build Bigram & Unigram Statistical Graph (1.23M myWord + gov_published + public_wiki_sentences)
            var bigramCounts = new Dictionary<string, long>(550000, StringComparer.Ordinal);
            var unigramCounts = new Dictionary<string, long>(140000, StringComparer.Ordinal);
            long totalBigramMass = 0;

            void RecordBigram(string w1, string w2, int count)
            {
                if (count <= 0 || string.IsNullOrEmpty(w1) || string.IsNullOrEmpty(w2)) return;
                if (!PureMyanmarWordRx.IsMatch(w1) || !PureMyanmarWordRx.IsMatch(w2)) return;
                string key = w1 + "\t" + w2;
                bigramCounts[key] = bigramCounts.TryGetValue(key, out long prev) ? (prev + count) : count;
                unigramCounts[w1] = unigramCounts.TryGetValue(w1, out long u1) ? (u1 + count) : count;
                unigramCounts[w2] = unigramCounts.TryGetValue(w2, out long u2) ? (u2 + count) : count;
                totalBigramMass += count;
            }

            foreach (string bf in new[] { "myword_bigram_aa.txt", "myword_bigram_ab.txt", "myword_bigram_ac.txt" })
            {
                string bp = Path.Combine(corporaDir, bf);
                if (!File.Exists(bp)) continue;
                using var sr = new StreamReader(bp, Encoding.UTF8);
                string? bLine;
                while ((bLine = sr.ReadLine()) != null)
                {
                    Match m = BigramTupleRx.Match(bLine);
                    if (!m.Success) continue;
                    if (!int.TryParse(m.Groups[3].Value, out int cnt) || cnt < 4) continue;
                    RecordBigram(Norm(m.Groups[1].Value), Norm(m.Groups[2].Value), cnt);
                }
            }

            string govBgPath = Path.Combine(corporaDir, "gov_published_news_bigrams.txt");
            if (File.Exists(govBgPath))
            {
                using var sr = new StreamReader(govBgPath, Encoding.UTF8);
                string? gLine;
                while ((gLine = sr.ReadLine()) != null)
                {
                    string[] gCols = gLine.Split('\t');
                    if (gCols.Length == 3 && int.TryParse(gCols[2].Trim(), out int cnt) && cnt >= 2)
                        RecordBigram(Norm(gCols[0].Trim()), Norm(gCols[1].Trim()), cnt * 5);
                }
            }

            Console.WriteLine($"    Indexed {cleanSentences.Count:N0} clean sentences and {bigramCounts.Count:N0} distinct bigrams (mass={totalBigramMass:N0}).");

            // =================================================================================================
            // PILLAR 1: Multi-Domain Vocabulary, Idioms & Assimilation Orthography Pair Miner
            // =================================================================================================
            Console.WriteLine("[Mine 2/5] Pillar 1 — Mining multi-domain vocabulary, idioms & assimilation orthography pairs...");
            var wiktRows = LoadPublicWiktionaryTsv(Path.Combine(dictDir, "public_wiktionary_bilingual.tsv"));
            var wikiDomRows = LoadPublicWikipediaDomainTsv(Path.Combine(dictDir, "public_wikipedia_domain_terms.tsv"));

            var minedDomainItems = new Dictionary<string, (string Pos, string Cue, string Note, int Freq)>(StringComparer.Ordinal);
            string minedDomainOutPath = Path.Combine(domainsDir, "mined_multidomain_lexicon.tsv");
            if (File.Exists(minedDomainOutPath))
            {
                foreach (string prevDomLine in File.ReadAllLines(minedDomainOutPath, Encoding.UTF8))
                {
                    int t = prevDomLine.IndexOf('\t');
                    if (t > 0)
                        activeWords.Remove(Norm(prevDomLine.Substring(0, t).Trim()));
                }
            }

            void TryAddMinedDomainWord(string rawWord, string pos, string mmCue, string enTitle, string domainKey, int freq, int minSyllables = 3)
            {
                string w = Norm(rawWord);
                if (!IsStrictlyValidNewLexeme(w, officialSyllables, condemnedWrongSet, allowForeignCoda: pos == "LOAN" || pos == "PROPN", minSyllables: minSyllables))
                    return;
                if (activeWords.Contains(w) || minedDomainItems.ContainsKey(w))
                    return;

                string domLabel = DomainDisplayNames.TryGetValue(domainKey ?? "general", out string? dl) ? dl : DomainDisplayNames["general"];
                string cue = SanitizeMeaningCue(w, mmCue, enTitle, domLabel);
                if (string.IsNullOrEmpty(cue))
                    return;

                var noteParts = new List<string>(2) { "နယ်ပယ်: " + domLabel };
                if (!string.IsNullOrWhiteSpace(enTitle))
                    noteParts.Add("အင်္ဂလိပ်အမည်: " + enTitle.Trim());

                minedDomainItems[w] = (string.IsNullOrWhiteSpace(pos) ? "N" : pos, cue, string.Join(" | ", noteParts), Math.Min(48, Math.Max(32, freq)));
            }

            // 1A. Public Wiktionary lemmas + derived compounds
            foreach (var wr in wiktRows)
            {
                if (wr.Pos == "IDIOM" || wr.Pos == "PROVERB")
                {
                    if (wr.Word.Length >= 4)
                        abidanIdioms.Add((wr.Word, wr.Pos == "PROVERB" ? "PROVERB" : "SAYING", $"အင်္ဂလိပ်အနက် — {wr.EnglishGloss}"));
                    continue;
                }
                string domKey = string.IsNullOrEmpty(wr.Topic) ? "general" : wr.Topic;
                TryAddMinedDomainWord(wr.Word, wr.Pos, "", wr.EnglishGloss, domKey, 185);
                foreach (string der in wr.Derived)
                {
                    if (!activeWords.Contains(der))
                        TryAddMinedDomainWord(der, "N", $"{wr.Word} ({wr.EnglishGloss}) မှ ဆင်းသက်သော ပေါင်းစပ်ဝေါဟာရ။", "", domKey, 155);
                }
            }

            // 1B. Public Wikipedia interwiki domain terms
            foreach (var wd in wikiDomRows)
            {
                TryAddMinedDomainWord(wd.Word, "N", wd.MyanmarDef, wd.EnglishTitle, wd.Domain, 195);
            }

            // 1B-2. Mine A..အ Domain Headwords from 25,001 Encyclopedia Articles (Law, Medicine, Geology, Science, Economics, Tech)
            string encycPath = Path.Combine(dictDir, "mlc_mm_encyclopedia_knowledge.tsv");
            if (File.Exists(encycPath))
            {
                foreach (string rawLine in File.ReadAllLines(encycPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine)) continue;
                    string[] eCols = rawLine.Split('\t');
                    if (eCols.Length < 4) continue;
                    string ew = Norm(eCols[0].Trim());
                    string ec = Norm(eCols[2].Trim());
                    string enote = eCols[3].Trim();
                    string domKey = MapEncyclopediaDomainKey(enote);
                    if (string.IsNullOrEmpty(domKey))
                        continue;
                    TryAddMinedDomainWord(ew, "N", ec, "", domKey, 175);
                }
            }

            // 1C. Attested myPOS compounds (excluding productive gerunds '-ခြင်း/-မှု' and sparse 1-off combinations)
            foreach (var kvComp in myPosCompounds)
            {
                if (kvComp.Value.Count >= 4)
                {
                    string w = kvComp.Key;
                    string l = kvComp.Value.Left;
                    string r = kvComp.Value.Right;
                    if (r == "ခြင်း" || r == "မှု" || r == "ရေး")
                        continue;
                    if (officialWords.Contains(l) && officialWords.Contains(r))
                    {
                        TryAddMinedDomainWord(
                            w,
                            kvComp.Value.Pos,
                            $"{l} နှင့် {r} တွဲစပ်ထားသော မြန်မာပေါင်းစပ်ဝေါဟာရ။",
                            "",
                            "general",
                            160 + kvComp.Value.Count * 10);
                    }
                }
            }
            // Ensure attested market price compound 'ပျော့တက်' (mild upward price movement in financial news) is covered
            if (!activeWords.Contains("ပျော့တက်") && !minedDomainItems.ContainsKey("ပျော့တက်"))
            {
                minedDomainItems["ပျော့တက်"] = (
                    "V",
                    "ဈေးနှုန်း သို့မဟုတ် တန်ဖိုး အနည်းငယ် မသိမသာ မြင့်တက်လာသည်။",
                    "နယ်ပယ်: ဘဏ္ဍာရေး၊ ဘောဂဗေဒနှင့် စီးပွားရေး (Finance, Economics & Business) | အင်္ဂလိပ်အမည်: edge up; rise slightly",
                    42);
            }
            // Preserve previously mined domain rows on idempotent re-runs (while enforcing >=3 syllables except whitelisted 'ပျော့တက်')
            if (File.Exists(minedDomainOutPath))
            {
                foreach (string prevDomLine in File.ReadAllLines(minedDomainOutPath, Encoding.UTF8))
                {
                    string[] pc = prevDomLine.Split('\t');
                    if (pc.Length >= 4)
                    {
                        string pw = Norm(pc[0].Trim());
                        if (pw == "ပျော့တက်" || (MyanmarSyllableBreaker.CountMyanmarSyllables(pw) >= 3 && !condemnedWrongSet.Contains(pw)))
                        {
                            if (!minedDomainItems.ContainsKey(pw))
                                minedDomainItems[pw] = (pc[1].Trim(), Norm(pc[2].Trim()), pc[3].Trim(), 42);
                        }
                    }
                }
            }
            using (var swDom = new StreamWriter(minedDomainOutPath, append: false, new UTF8Encoding(false)))
            {
                foreach (var kv in minedDomainItems.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    swDom.WriteLine($"{kv.Key}\t{kv.Value.Pos}\t{kv.Value.Cue}\t{kv.Value.Note}\t{kv.Value.Freq}");
                }
            }

            // 1D. Expand mlc_mm_idioms_clean.tsv (preserving existing rows)
            string idiomsPath = Path.Combine(dictDir, "mlc_mm_idioms_clean.tsv");
            var mergedIdioms = new Dictionary<string, (string Pos, string Cue)>(StringComparer.Ordinal);
            if (File.Exists(idiomsPath))
            {
                foreach (string line in File.ReadAllLines(idiomsPath, Encoding.UTF8))
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length >= 3 && !string.IsNullOrWhiteSpace(cols[0]))
                        mergedIdioms[Norm(cols[0].Trim())] = (cols[1].Trim(), Norm(cols[2].Trim()));
                }
            }
            foreach (var idm in abidanIdioms)
            {
                if (!mergedIdioms.ContainsKey(idm.Phrase))
                    mergedIdioms[idm.Phrase] = (idm.Pos, idm.Def);
            }
            using (var swIdm = new StreamWriter(idiomsPath, append: false, new UTF8Encoding(false)))
            {
                foreach (var kv in mergedIdioms.OrderBy(k => k.Key, StringComparer.Ordinal))
                    swIdm.WriteLine($"{kv.Key}\t{kv.Value.Pos}\t{kv.Value.Cue}");
            }

            // 1E. Mine High-Certainty Assimilation & Unigram Shadow Orthography Pairs (idempotent across runs)
            string cleanCorpusJoinedBlob = string.Join("\n", cleanSentences);
            var minedOrthPairs = new Dictionary<string, (string Correct, string Category, string Explanation)>(StringComparer.Ordinal);
            string minedOrthOutPath = Path.Combine(rulesDir, "mined_orthography_pairs.tsv");
            if (File.Exists(minedOrthOutPath))
            {
                string[] prevOrth = File.ReadAllLines(minedOrthOutPath, Encoding.UTF8);
                for (int pi = 0; pi < prevOrth.Length; pi++)
                {
                    string[] pc = prevOrth[pi].Split('\t');
                    if (pc.Length >= 4 && !string.Equals(pc[0].Trim(), "WrongForm", StringComparison.OrdinalIgnoreCase))
                    {
                        string pw = Norm(pc[0].Trim());
                        string pcorr = Norm(pc[1].Trim());
                        if (!string.IsNullOrEmpty(pw) && !officialWords.Contains(pw) && pw != "သက်သောင့်သက်သာ")
                        {
                            minedOrthPairs[pw] = (pcorr, pc[2].Trim(), pc[3].Trim());
                            condemnedWrongSet.Remove(pw);
                        }
                    }
                }
            }

            // Mine 3..5-syllable unigram shadow typos from myword_unigram.txt against high-frequency Official Thatpon words
            var officialMultiSylByBucket = new Dictionary<string, List<(string Word, long Freq)>>(StringComparer.Ordinal);
            foreach (string ow in officialWords)
            {
                var syls = MyanmarSyllableBreaker.BreakIntoSyllables(ow, isPreNormalized: true);
                if (syls.Count < 3 || syls.Count > 5) continue;
                long freq = unigramCounts.TryGetValue(ow, out long uf) ? uf : (existingWordFreq.TryGetValue(ow, out int ef) ? ef : 0);
                if (freq < 120) continue;
                string bKey = syls.Count.ToString(CultureInfo.InvariantCulture) + "|" + syls[0][0] + "|" + syls[syls.Count - 1][0];
                if (!officialMultiSylByBucket.TryGetValue(bKey, out var bList))
                {
                    bList = new List<(string, long)>(4);
                    officialMultiSylByBucket[bKey] = bList;
                }
                bList.Add((ow, freq));
            }

            string rawUnigramPath = Path.Combine(corporaDir, "myword_unigram.txt");
            if (File.Exists(rawUnigramPath))
            {
                foreach (string line in File.ReadAllLines(rawUnigramPath, Encoding.UTF8))
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string candWrong = Norm(line.Substring(0, tab).Trim());
                    if (!int.TryParse(line.Substring(tab + 1).Trim(), out int wrongCnt) || wrongCnt < 2 || wrongCnt > 25)
                        continue;
                    if (!PureMyanmarWordRx.IsMatch(candWrong) || officialWords.Contains(candWrong) || activeWords.Contains(candWrong) || condemnedWrongSet.Contains(candWrong))
                        continue;
                    if (UnicodeNormalizer.HasLookalikeError(candWrong, out _, out _))
                        continue;

                    var wSyls = MyanmarSyllableBreaker.BreakIntoSyllables(candWrong, isPreNormalized: true);
                    if (wSyls.Count < 3 || wSyls.Count > 5) continue;
                    if (CanDecomposeIntoOfficialWords(wSyls, officialWords))
                        continue;

                    string bKey = wSyls.Count.ToString(CultureInfo.InvariantCulture) + "|" + wSyls[0][0] + "|" + wSyls[wSyls.Count - 1][0];
                    if (!officialMultiSylByBucket.TryGetValue(bKey, out var offCands))
                        continue;

                    string? bestOff = null;
                    double bestDist = 0.24;
                    long bestOffFreq = 0;
                    foreach (var (offWord, offFreq) in offCands)
                    {
                        if (offFreq < wrongCnt * 25L) continue;
                        double d = SuggestionEngine.ComputeWeightedMyanmarDistance(candWrong, offWord, 0.24);
                        if (d > 0.0 && d <= bestDist)
                        {
                            bestDist = d;
                            bestOff = offWord;
                            bestOffFreq = offFreq;
                        }
                    }

                    if (bestOff != null && bestOffFreq >= 200 && cleanCorpusJoinedBlob.IndexOf(candWrong, StringComparison.Ordinal) < 0)
                    {
                        minedOrthPairs[candWrong] = (
                            bestOff,
                            "Spelling",
                            $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းနှင့် ကော်ပတ်စ်အကြိမ်ရေစိစစ်ချက်အရ '{candWrong}' အစား စံသတ်ပုံအမှန် '{bestOff}' ဟု ရေးသားရမည်။");
                    }
                }
            }

            using (var swOrth = new StreamWriter(minedOrthOutPath, append: false, new UTF8Encoding(false)))
            {
                foreach (var kv in minedOrthPairs.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    swOrth.WriteLine($"{kv.Key}\t{kv.Value.Correct}\t{kv.Value.Category}\t{kv.Value.Explanation}");
                    condemnedWrongSet.Add(kv.Key);
                }
            }

            // =================================================================================================
            // PILLAR 2: Bilingual Meaning, Synonym, Classifier, Top-PMI Collocation & Clean Example Miner
            // =================================================================================================
            Console.WriteLine("[Mine 3/5] Pillar 2 — Mining bilingual glosses, PMI collocations, synonyms & clean examples...");
            var enrichMap = new Dictionary<string, EnrichmentRecord>(StringComparer.Ordinal);
            EnrichmentRecord GetOrAddEnrich(string w)
            {
                if (!enrichMap.TryGetValue(w, out var rec))
                {
                    rec = new EnrichmentRecord { Word = w };
                    enrichMap[w] = rec;
                }
                return rec;
            }

            string minedEnrichOutPath = Path.Combine(dictDir, "mined_entry_enrichments.tsv");
            if (File.Exists(minedEnrichOutPath))
            {
                string[] prevEnr = File.ReadAllLines(minedEnrichOutPath, Encoding.UTF8);
                for (int pi = 1; pi < prevEnr.Length; pi++)
                {
                    string[] pc = prevEnr[pi].Split('\t');
                    if (pc.Length >= 7 && !string.IsNullOrWhiteSpace(pc[0]))
                    {
                        var pr = GetOrAddEnrich(Norm(pc[0].Trim()));
                        pr.EnglishGloss = pc[1].Trim();
                        pr.Classifier = pc[2].Trim();
                        pr.Synonyms = pc[3].Trim();
                        pr.Antonyms = pc[4].Trim();
                        pr.Collocations = pc[5].Trim();
                        pr.ExampleSentence = pc[6].Trim();
                    }
                }
            }

            // 2A. Public Wiktionary & Wikipedia enrichments
            foreach (var wr in wiktRows)
            {
                if (!activeWords.Contains(wr.Word) && !minedDomainItems.ContainsKey(wr.Word)) continue;
                var rec = GetOrAddEnrich(wr.Word);
                if (string.IsNullOrEmpty(rec.EnglishGloss) && !existingHasEn.Contains(wr.Word))
                    rec.EnglishGloss = wr.EnglishGloss;
                if (string.IsNullOrEmpty(rec.Classifier) && wr.Classifiers.Length > 0)
                    rec.Classifier = string.Join("၊ ", wr.Classifiers);
                if (string.IsNullOrEmpty(rec.Synonyms) && wr.Synonyms.Length > 0)
                    rec.Synonyms = string.Join("၊ ", wr.Synonyms);
                if (string.IsNullOrEmpty(rec.Antonyms) && wr.Antonyms.Length > 0)
                    rec.Antonyms = string.Join("၊ ", wr.Antonyms);
            }
            foreach (var wd in wikiDomRows)
            {
                if (!activeWords.Contains(wd.Word) && !minedDomainItems.ContainsKey(wd.Word)) continue;
                var rec = GetOrAddEnrich(wd.Word);
                if (string.IsNullOrEmpty(rec.EnglishGloss) && !string.IsNullOrEmpty(wd.EnglishTitle) && !existingHasEn.Contains(wd.Word))
                    rec.EnglishGloss = wd.EnglishTitle;
            }

            // 2B. Ornagai sanitized MM -> EN backfill for headwords still missing English glosses
            string ornagaiPath = Path.Combine(dictDir, "ornagai_mmtab.tsv");
            if (File.Exists(ornagaiPath))
            {
                var ornagaiEnByMm = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(ornagaiPath, Encoding.UTF8))
                {
                    string[] cols = line.Split('\t');
                    if (cols.Length < 2) continue;
                    string rawMm = cols[0].Trim();
                    var zgSpans = UnicodeNormalizer.FindZawgyiSpans(rawMm);
                    if (zgSpans.Count > 0)
                        rawMm = UnicodeNormalizer.ConvertZawgyiToUnicode(rawMm);
                    string mm = Norm(rawMm);
                    if (!activeWords.Contains(mm) || existingHasEn.Contains(mm)) continue;

                    string en = cols[1].Trim().Trim('_', ';', ',', '.');
                    if (en.Length < 2 || en.Length > 60 || VulgarEnglishRx.IsMatch(en) || !HasBalancedParens(en))
                        continue;
                    if (!ornagaiEnByMm.TryGetValue(mm, out var list))
                    {
                        list = new List<string>(3);
                        ornagaiEnByMm[mm] = list;
                    }
                    if (list.Count < 3 && !list.Contains(en, StringComparer.OrdinalIgnoreCase))
                        list.Add(en);
                }

                foreach (var kv in ornagaiEnByMm)
                {
                    var rec = GetOrAddEnrich(kv.Key);
                    if (string.IsNullOrEmpty(rec.EnglishGloss))
                        rec.EnglishGloss = string.Join("; ", kv.Value);
                }
            }

            // 2C. Top-3 High-PMI Content Collocations from 1.23M Bigram Graph (top 14,000 content words)
            var collocsByWord = new Dictionary<string, List<(string Phrase, double Pmi)>>(StringComparer.Ordinal);
            double logTotalMass = Math.Log(Math.Max(1.0, totalBigramMass));
            foreach (var kvBg in bigramCounts)
            {
                if (kvBg.Value < 30) continue;
                int tab = kvBg.Key.IndexOf('\t');
                string w1 = kvBg.Key.Substring(0, tab);
                string w2 = kvBg.Key.Substring(tab + 1);
                if (StopParticlesAndFunctionWords.Contains(w1) || StopParticlesAndFunctionWords.Contains(w2))
                    continue;
                if (!activeWords.Contains(w1) || !activeWords.Contains(w2))
                    continue;
                if (condemnedWrongSet.Contains(w1) || condemnedWrongSet.Contains(w2))
                    continue;

                long c1 = unigramCounts[w1];
                long c2 = unigramCounts[w2];
                double pmi = Math.Log(kvBg.Value) + logTotalMass - Math.Log(c1) - Math.Log(c2);
                if (pmi < 4.2) continue;

                string phrase = w1 + " " + w2;
                AddTopColloc(collocsByWord, w1, phrase, pmi);
                AddTopColloc(collocsByWord, w2, phrase, pmi);
            }

            int collocAttached = 0;
            foreach (var kvCol in collocsByWord.OrderByDescending(k => unigramCounts.TryGetValue(k.Key, out long f) ? f : 0))
            {
                if (collocAttached >= 22000) break;
                if (existingHasColloc.Contains(kvCol.Key)) continue;
                var topPhrases = kvCol.Value.OrderByDescending(x => x.Pmi).Take(3).Select(x => x.Phrase).ToList();
                if (topPhrases.Count > 0)
                {
                    GetOrAddEnrich(kvCol.Key).Collocations = string.Join("၊ ", topPhrases);
                    collocAttached++;
                }
            }

            // 2D. Clean Short Corpus Example Sentences (up to 14,500 common headwords lacking examples)
            var exampleAssigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (string exSent in cleanShortExamples)
            {
                if (exampleAssigned.Count >= 14500) break;
                foreach (Match tm in MyanmarTokenScanRx.Matches(exSent))
                {
                    string tok = Norm(tm.Value);
                    if (tok.Length < 3 || StopParticlesAndFunctionWords.Contains(tok)) continue;
                    if (!activeWords.Contains(tok) || existingHasExample.Contains(tok) || exampleAssigned.Contains(tok))
                        continue;
                    if (existingWordFreq.TryGetValue(tok, out int f) && f >= 25)
                    {
                        GetOrAddEnrich(tok).ExampleSentence = exSent;
                        exampleAssigned.Add(tok);
                        if (exampleAssigned.Count >= 14500) break;
                    }
                }
            }

            int enrichWritten = 0;
            using (var swEnr = new StreamWriter(minedEnrichOutPath, append: false, new UTF8Encoding(false)))
            {
                swEnr.WriteLine("Word\tEnglishGloss\tClassifier\tSynonyms\tAntonyms\tCollocations\tExampleSentence");
                foreach (var kv in enrichMap.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var r = kv.Value;
                    if (r.HasAnyValue)
                    {
                        swEnr.WriteLine($"{r.Word}\t{CleanTsvField(r.EnglishGloss)}\t{CleanTsvField(r.Classifier)}\t{CleanTsvField(r.Synonyms)}\t{CleanTsvField(r.Antonyms)}\t{CleanTsvField(r.Collocations)}\t{CleanTsvField(r.ExampleSentence)}");
                        enrichWritten++;
                    }
                }
            }

            // =================================================================================================
            // PILLAR 3: Zero-FP Discriminative PMI Homophone & Confusion Context Rule Miner
            // =================================================================================================
            Console.WriteLine("[Mine 4/5] Pillar 3 — Mining Zero-FP discriminative PMI homophone & tone context rules...");
            var confusionBuckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string w in officialWords)
            {
                int sylCnt = MyanmarSyllableBreaker.CountMyanmarSyllables(w);
                if (sylCnt < 1 || sylCnt > 2 || StopParticlesAndFunctionWords.Contains(w))
                    continue;
                string skel = ComputeConfusionSkeleton(w);
                if (skel.Length < 1) continue;
                if (!confusionBuckets.TryGetValue(skel, out var list))
                {
                    list = new List<string>(4);
                    confusionBuckets[skel] = list;
                }
                list.Add(w);
            }

            // Index right-neighbors and left-neighbors from bigramCounts
            var rightNeighbors = new Dictionary<string, List<(string Neighbor, long Count)>>(StringComparer.Ordinal);
            var leftNeighbors = new Dictionary<string, List<(string Neighbor, long Count)>>(StringComparer.Ordinal);
            foreach (var kvBg in bigramCounts)
            {
                if (kvBg.Value < 35) continue;
                int tab = kvBg.Key.IndexOf('\t');
                string w1 = kvBg.Key.Substring(0, tab);
                string w2 = kvBg.Key.Substring(tab + 1);
                if (!rightNeighbors.TryGetValue(w1, out var rList))
                {
                    rList = new List<(string, long)>(4);
                    rightNeighbors[w1] = rList;
                }
                rList.Add((w2, kvBg.Value));

                if (!leftNeighbors.TryGetValue(w2, out var lList))
                {
                    lList = new List<(string, long)>(4);
                    leftNeighbors[w2] = lList;
                }
                lList.Add((w1, kvBg.Value));
            }

            var minedCtxRules = new List<ContextualRule>();
            foreach (var bucket in confusionBuckets.Values)
            {
                if (bucket.Count < 2 || bucket.Count > 5) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    string wCorrect = bucket[i];
                    if (!unigramCounts.TryGetValue(wCorrect, out long cCorrect) || cCorrect < 120)
                        continue;

                    for (int j = 0; j < bucket.Count; j++)
                    {
                        if (i == j) continue;
                        string wWrong = bucket[j];
                        if (!unigramCounts.TryGetValue(wWrong, out long cWrong) || cWrong < 20)
                            continue;

                        // Right-context triggers: [wWrong + Trigger] -> [wCorrect + Trigger]
                        if (rightNeighbors.TryGetValue(wCorrect, out var rCandList))
                        {
                            var validRightTriggers = new List<string>();
                            foreach (var (trig, bgCnt) in rCandList)
                            {
                                if (bgCnt < 45 || trig.Length < 3 || StopParticlesAndFunctionWords.Contains(trig))
                                    continue;
                                if (bigramCounts.ContainsKey(wWrong + "\t" + trig))
                                    continue;
                                if (activeWords.Contains(wWrong + trig))
                                    continue;
                                long cTrig = unigramCounts[trig];
                                double pmi = Math.Log(bgCnt) + logTotalMass - Math.Log(cCorrect) - Math.Log(cTrig);
                                if (pmi < 5.4)
                                    continue;
                                if (cleanCorpusJoinedBlob.IndexOf(wWrong + trig, StringComparison.Ordinal) >= 0 ||
                                    cleanCorpusJoinedBlob.IndexOf(wWrong + " " + trig, StringComparison.Ordinal) >= 0)
                                    continue;

                                validRightTriggers.Add(trig);
                            }
                            if (validRightTriggers.Count > 0)
                            {
                                minedCtxRules.Add(new ContextualRule
                                {
                                    WrongForm = wWrong,
                                    CorrectForm = wCorrect,
                                    TriggerWords = string.Join("|", validRightTriggers.Take(10)),
                                    AntiTriggerWords = "",
                                    Direction = "Right",
                                    Window = 1,
                                    IsWarningOnly = true,
                                    SkipParticles = false,
                                    Explanation = $"ကော်ပတ်စ် တွဲဖက်အသုံးစာရင်းအင်း (PMI) အရ နောက်ဆက်ပုဒ်နှင့် တွဲဖက်ရာ၌ '{wWrong}' အစား '{wCorrect}' ကို သုံးနှုန်းသင့်ပါသည်။"
                                });
                            }
                        }

                        // Left-context triggers: [Trigger + wWrong] -> [Trigger + wCorrect]
                        if (leftNeighbors.TryGetValue(wCorrect, out var lCandList))
                        {
                            var validLeftTriggers = new List<string>();
                            foreach (var (trig, bgCnt) in lCandList)
                            {
                                if (bgCnt < 45 || trig.Length < 3 || StopParticlesAndFunctionWords.Contains(trig))
                                    continue;
                                if (bigramCounts.ContainsKey(trig + "\t" + wWrong))
                                    continue;
                                if (activeWords.Contains(trig + wWrong))
                                    continue;
                                long cTrig = unigramCounts[trig];
                                double pmi = Math.Log(bgCnt) + logTotalMass - Math.Log(cTrig) - Math.Log(cCorrect);
                                if (pmi < 5.4)
                                    continue;
                                if (cleanCorpusJoinedBlob.IndexOf(trig + wWrong, StringComparison.Ordinal) >= 0 ||
                                    cleanCorpusJoinedBlob.IndexOf(trig + " " + wWrong, StringComparison.Ordinal) >= 0)
                                    continue;

                                validLeftTriggers.Add(trig);
                            }
                            if (validLeftTriggers.Count > 0)
                            {
                                minedCtxRules.Add(new ContextualRule
                                {
                                    WrongForm = wWrong,
                                    CorrectForm = wCorrect,
                                    TriggerWords = string.Join("|", validLeftTriggers.Take(10)),
                                    AntiTriggerWords = "",
                                    Direction = "Left",
                                    Window = 1,
                                    IsWarningOnly = true,
                                    SkipParticles = false,
                                    Explanation = $"ကော်ပတ်စ် တွဲဖက်အသုံးစာရင်းအင်း (PMI) အရ ရှေ့ဆက်ပုဒ်နှင့် တွဲဖက်ရာ၌ '{wWrong}' အစား '{wCorrect}' ကို သုံးနှုန်းသင့်ပါသည်။"
                                });
                            }
                        }
                    }
                }
            }

            string minedCtxOutPath = Path.Combine(rulesDir, "mined_pmi_context_rules.tsv");
            using (var swCtx = new StreamWriter(minedCtxOutPath, append: false, new UTF8Encoding(false)))
            {
                swCtx.WriteLine("WrongForm\tCorrectForm\tTriggerWords\tAntiTriggerWords\tDirection\tWindow\tIsWarningOnly\tSkipParticles\tExplanation");
                foreach (var cr in minedCtxRules.OrderBy(r => r.WrongForm, StringComparer.Ordinal).ThenBy(r => r.Direction, StringComparer.Ordinal))
                {
                    swCtx.WriteLine($"{cr.WrongForm}\t{cr.CorrectForm}\t{cr.TriggerWords}\t{cr.AntiTriggerWords}\t{cr.Direction}\t{cr.Window}\t{(cr.IsWarningOnly ? "1" : "0")}\t{(cr.SkipParticles ? "1" : "0")}\t{cr.Explanation}");
                }
            }

            // =================================================================================================
            // PILLAR 4: Zero-FP O(1) Grammar Pattern Miner (Noun->Classifier, Mandatory Compounds, Verb Valence)
            // =================================================================================================
            Console.WriteLine("[Mine 5/5] Pillar 4 — Mining Zero-FP Noun->Classifier, Mandatory Compounds & Verb Valence tables...");

            // 4A. Merge Abidan + Wiktionary + UPOS Noun -> Classifier mappings & audit against 78,000 clean sentences
            var mergedNounClfs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kv in abidanClassifiers)
                mergedNounClfs[kv.Key] = new List<string>(kv.Value);
            foreach (var wr in wiktRows)
            {
                if (wr.Classifiers.Length > 0 && wr.Pos == "N" && PureMyanmarWordRx.IsMatch(wr.Word))
                {
                    if (!mergedNounClfs.TryGetValue(wr.Word, out var list))
                    {
                        list = new List<string>();
                        mergedNounClfs[wr.Word] = list;
                    }
                    foreach (string c in wr.Classifiers)
                    {
                        if (!list.Contains(c)) list.Add(c);
                    }
                }
            }
            foreach (var kvEmp in corpusNounClfCounts)
            {
                if (!mergedNounClfs.ContainsKey(kvEmp.Key))
                {
                    var topEmp = kvEmp.Value.Where(x => x.Value >= 3).OrderByDescending(x => x.Value).Select(x => x.Key).ToList();
                    if (topEmp.Count == 1)
                        mergedNounClfs[kvEmp.Key] = topEmp;
                }
            }

            // Pre-extract all '[Noun] [Numeral] [Clf]' triples occurring anywhere in cleanSentences so 0 clean triples are ever flagged!
            var attestedCleanNounClfPairs = new HashSet<string>(StringComparer.Ordinal);
            Regex cleanNumClfRx = new Regex(@"(?<Noun>[\u1000-\u103F]{2,24})[ \t]*(?:တစ်|နှစ်|သုံး|လေး|ငါး|ခြောက်|ခုနစ်|ရှစ်|ကိုး|တစ်ဆယ်|ဆယ်|[၁-၉][၀-၉]*)[ \t]*(?<Clf>[\u1000-\u103F]{1,8})", RegexOptions.Compiled);
            for (int si = 0; si < cleanSentences.Count; si++)
            {
                foreach (Match m in cleanNumClfRx.Matches(cleanSentences[si]))
                {
                    attestedCleanNounClfPairs.Add(Norm(m.Groups["Noun"].Value) + "|" + Norm(m.Groups["Clf"].Value));
                }
            }

            // Exclude general container/place/verb-homograph 1-syllable stems that frequently precede fronted human subjects ('အိမ် သုံးယောက် ငှားသည်')
            var frontedObjectAmbiguousNouns = new HashSet<string>(StringComparer.Ordinal)
            {
                "အိမ်", "အခန်း", "ရုံး", "ကျောင်း", "ဆိုင်", "မြို့", "ရွာ", "လမ်း", "ကား", "စားပွဲ", "ထမင်း", "ရေ", "ငွေ", "အလုပ်", "စာ"
            };

            var minedClassifierRows = new List<(string Noun, string Primary, string AllowedCsv, string WrongCsv, string Label, int Evidence)>();
            foreach (var kv in mergedNounClfs.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string noun = kv.Key;
                if (noun.Length < 2 || StopParticlesAndFunctionWords.Contains(noun) || frontedObjectAmbiguousNouns.Contains(noun))
                    continue;
                if (existingWordPos.TryGetValue(noun, out string? nPos) && (nPos == "V" || nPos == "PART" || nPos == "ADV" || nPos == "ADJ"))
                    continue;

                var clfs = kv.Value;
                if (clfs.Count == 0 || clfs.Count > 2)
                    continue;

                string primary = clfs[0];
                if (!SortalClassifierProfiles.TryGetValue(primary, out var profile))
                    continue;

                // If second classifier is in a conflicting human vs inanimate class, skip polysemous noun
                if (clfs.Count == 2 && profile.WrongSet.Contains(clfs[1]))
                    continue;

                var safeWrong = new List<string>();
                foreach (string wClf in profile.WrongSet)
                {
                    if (clfs.Contains(wClf)) continue;
                    if (attestedCleanNounClfPairs.Contains(noun + "|" + wClf))
                        continue;
                    safeWrong.Add(wClf);
                }
                if (safeWrong.Count == 0)
                    continue;

                int ev = 1;
                if (corpusNounClfCounts.TryGetValue(noun, out var empMap) && empMap.TryGetValue(primary, out int empCnt))
                    ev += empCnt;

                minedClassifierRows.Add((noun, primary, string.Join(",", clfs), string.Join(",", safeWrong), profile.Label, ev));
            }

            string minedClfOutPath = Path.Combine(rulesDir, "mined_noun_classifiers.tsv");
            using (var swClf = new StreamWriter(minedClfOutPath, append: false, new UTF8Encoding(false)))
            {
                swClf.WriteLine("Noun\tPrimaryClassifier\tAllowedClassifiers\tWrongClassifiers\tCategoryLabel\tEvidenceCount");
                foreach (var row in minedClassifierRows)
                    swClf.WriteLine($"{row.Noun}\t{row.Primary}\t{row.AllowedCsv}\t{row.WrongCsv}\t{row.Label}\t{row.Evidence}");
            }

            // 4B. Mine Zero-FP Mandatory Broken Compounds (Official Thatpon compounds with bound stems, 0 clean split occurrences)
            var minedMandatoryCompounds = new List<(string Left, string Right, string Joined, long JoinedCnt)>();
            foreach (string ow in officialWords.OrderBy(x => x, StringComparer.Ordinal))
            {
                var syls = MyanmarSyllableBreaker.BreakIntoSyllables(ow, isPreNormalized: true);
                if (syls.Count < 2 || syls.Count > 4) continue;
                long jFreq = unigramCounts.TryGetValue(ow, out long uf) ? uf : (existingWordFreq.TryGetValue(ow, out int ef) ? ef : 0);

                int splitIdx = 0;
                for (int split = 1; split < syls.Count; split++)
                {
                    splitIdx += syls[split - 1].Length;
                    string left = ow.Substring(0, splitIdx);
                    string right = ow.Substring(splitIdx);
                    if (left.Length < 2 || right.Length < 2) continue;
                    if (left.EndsWith("မှု", StringComparison.Ordinal) || left.EndsWith("ရေး", StringComparison.Ordinal) || left.EndsWith("ခြင်း", StringComparison.Ordinal))
                        continue;
                    if (StopParticlesAndFunctionWords.Contains(left) || StopParticlesAndFunctionWords.Contains(right))
                        continue;

                    bool explicitBound = BoundCompoundStems.Contains(left) || BoundCompoundStems.Contains(right);
                    if (jFreq < (explicitBound ? 10 : 45))
                        continue;

                    bool hasBoundConstituent = explicitBound || !officialWords.Contains(left) || !officialWords.Contains(right);
                    if (!hasBoundConstituent)
                        continue;

                    // Must NEVER appear split in clean sentences or bigrams!
                    if (bigramCounts.ContainsKey(left + "\t" + right))
                        continue;
                    if (cleanCorpusJoinedBlob.IndexOf(left + " " + right, StringComparison.Ordinal) >= 0)
                        continue;

                    minedMandatoryCompounds.Add((left, right, ow, jFreq));
                }
            }

            string minedCompOutPath = Path.Combine(rulesDir, "mined_mandatory_compounds.tsv");
            using (var swComp = new StreamWriter(minedCompOutPath, append: false, new UTF8Encoding(false)))
            {
                swComp.WriteLine("Left\tRight\tJoined\tJoinedCount");
                foreach (var c in minedMandatoryCompounds)
                    swComp.WriteLine($"{c.Left}\t{c.Right}\t{c.Joined}\t{c.JoinedCnt}");
            }

            // 4C. Mine & Verify Zero-FP Verb Case-Frame & Collocation Valence Rules
            var candidateValenceRules = new List<(string Trigger, string Pattern, string Group, string Rep, string IssueType, string ReqRev, string Cue, string Expl)>
            {
                (
                    "တွေရ",
                    @"(?<=(?:တာ|ချက်|မှု|ကြောင်း)\s+)(?<Target>တွေ)(?=ရ(?:တယ်|သည်|ပါတယ်|ပါသည်|ကြောင်း))",
                    "Target",
                    "တွေ့",
                    "SpellingError",
                    "0",
                    "တွေ့ရသည် (မြင်တွေ့ရခြင်း — အောက်မြစ်ပါ 'တွေ့')",
                    "ရှေ့နာမ်ပြုပုဒ် ('~တာ/~ချက်/~မှု') နောက်တွင် မြင်တွေ့ရကြောင်းပြရာ၌ အောက်မြစ်ပါသော 'တွေ့' ('တွေ့ရသည်/တွေ့ရတယ်') ဟု ရေးသားရမည်။"
                ),
                (
                    "အလှပြင်",
                    @"(?<=(?:အိမ်|အခန်း|အိမ်တွင်း|ဥယျာဉ်|ခန်းမ)\s*)(?<Target>အလှပြင်)(?=ရန်|ဖို့|ခြင်း|မှု|သည်|တယ်)",
                    "Target",
                    "အလှဆင်",
                    "SpellingError",
                    "0",
                    "အလှဆင် (အိမ်/အခန်း မွမ်းမံတန်ဆာဆင်ခြင်း)",
                    "အိမ်၊ အခန်းနှင့် အဆောက်အအုံတို့ကို မွမ်းမံရာတွင် 'အလှဆင်' ဟု သုံးနှုန်းရမည် ('အလှပြင်' သည် လူခန္ဓာကိုယ်/မျက်နှာ ပြင်ဆင်ခြင်း၌သာ သုံးသည်)။"
                ),
                (
                    "စီမံကိန်း",
                    @"(?<=စီမံကိန်း(?:များ|တွေ)?\s+)(?<Target>လုပ်ဆောင်)(?=ရမည်|ရမယ်|သင့်)",
                    "Target",
                    "ချမှတ်",
                    "SpellingError",
                    "1",
                    "စီမံကိန်းချမှတ် (မူဝါဒ/စီမံကိန်း ရေးဆွဲချမှတ်ခြင်း)",
                    "'စီမံကိန်း' ကို စတင်သတ်မှတ်ရာတွင် စံတွဲဖက်ကြိယာ 'ချမှတ်' ('စီမံကိန်းချမှတ်-') သို့မဟုတ် 'ရေးဆွဲ' ကို သုံးနှုန်းသင့်ပါသည်။"
                ),
                (
                    "စည်းလုံးညီညွတ်မှုကို",
                    @"(?<=စည်းလုံးညီညွတ်မှုကို\s+)(?<Target>ပြုလုပ်)(?=ပါသည်|ပါတယ်|သည်|တယ်|ရမည်|ရမယ်)",
                    "Target",
                    "တည်ဆောက်",
                    "SpellingError",
                    "1",
                    "စည်းလုံးညီညွတ်မှု တည်ဆောက်ခြင်း",
                    "'စည်းလုံးညီညွတ်မှု' နှင့် တွဲဖက်ရာတွင် 'ပြုလုပ်' အစား အနက်ကိုက်ညီသော 'တည်ဆောက်' ကို သုံးနှုန်းသင့်ပါသည်။"
                ),
                (
                    "စိတ်ဆင်းရဲရေး",
                    @"(?<![\u1000-\u103F])(?<Target>စိတ်ဆင်းရဲရေး)(?=ဆိုတာ|သည်|ဟာ|\s+အရေးကြီး)",
                    "Target",
                    "စိတ်ကျန်းမာရေး",
                    "SpellingError",
                    "0",
                    "စိတ်ကျန်းမာရေး (Mental Health)",
                    "အရေးကြီးသော ကျန်းမာရေးကဏ္ဍကို ရည်ညွှန်းရာ၌ ဆန့်ကျင်ဘက်အနက်ထွက်နေသော 'စိတ်ဆင်းရဲရေး' အစား 'စိတ်ကျန်းမာရေး' ဟု သုံးနှုန်းရမည်။"
                )
            };

            // Audit every candidate valence rule against all 78,000+ clean sentences: keep ONLY rules with 0 clean matches!
            var verifiedValenceRules = new List<(string Trigger, string Pattern, string Group, string Rep, string IssueType, string ReqRev, string Cue, string Expl)>();
            foreach (var vr in candidateValenceRules)
            {
                var rx = new Regex(vr.Pattern);
                bool anyCleanFp = false;
                for (int si = 0; si < cleanSentences.Count; si++)
                {
                    string s = cleanSentences[si];
                    if (s.IndexOf(vr.Trigger, StringComparison.Ordinal) >= 0 && rx.IsMatch(s))
                    {
                        anyCleanFp = true;
                        break;
                    }
                }
                if (!anyCleanFp)
                    verifiedValenceRules.Add(vr);
            }

            string minedValOutPath = Path.Combine(rulesDir, "mined_verb_case_frames.tsv");
            using (var swVal = new StreamWriter(minedValOutPath, append: false, new UTF8Encoding(false)))
            {
                swVal.WriteLine("TriggerSubstring\tPattern\tWrongGroup\tReplacement\tIssueType\tRequiresReview\tMeaningCue\tExplanation");
                foreach (var vr in verifiedValenceRules)
                    swVal.WriteLine($"{vr.Trigger}\t{vr.Pattern}\t{vr.Group}\t{vr.Rep}\t{vr.IssueType}\t{vr.ReqRev}\t{vr.Cue}\t{vr.Expl}");
            }

            sw.Stop();
            var summary = new MiningSummary
            {
                CleanSentencesIndexed = cleanSentences.Count,
                BigramPairsIndexed = bigramCounts.Count,
                DomainHeadwordsMined = minedDomainItems.Count,
                IdiomsTotal = mergedIdioms.Count,
                OrthographyPairsMined = minedOrthPairs.Count,
                EntryEnrichmentsMined = enrichWritten,
                PmiContextRulesMined = minedCtxRules.Count,
                NounClassifiersMined = minedClassifierRows.Count,
                MandatoryCompoundsMined = minedMandatoryCompounds.Count,
                VerbValenceRulesMined = verifiedValenceRules.Count,
                ElapsedSeconds = sw.Elapsed.TotalSeconds
            };

            WriteMiningReportMarkdown(docsDir, summary);
            Console.WriteLine("------------------------------------------------------------------------");
            Console.WriteLine($"[+] Corpus Mining Complete in {summary.ElapsedSeconds:F1}s:");
            Console.WriteLine($"    - Clean Sentences Audited     : {summary.CleanSentencesIndexed:N0}");
            Console.WriteLine($"    - Distinct Bigrams Indexed    : {summary.BigramPairsIndexed:N0}");
            Console.WriteLine($"    - New Multi-Domain Headwords  : +{summary.DomainHeadwordsMined:N0}");
            Console.WriteLine($"    - Clean Idioms & Sayings      : {summary.IdiomsTotal:N0}");
            Console.WriteLine($"    - Mined Orthography Pairs     : +{summary.OrthographyPairsMined:N0}");
            Console.WriteLine($"    - Semantic/Colloc Enrichments : +{summary.EntryEnrichmentsMined:N0}");
            Console.WriteLine($"    - Zero-FP PMI Context Rules   : +{summary.PmiContextRulesMined:N0}");
            Console.WriteLine($"    - O(1) Noun->Classifier Rules : {summary.NounClassifiersMined:N0}");
            Console.WriteLine($"    - O(1) Mandatory Compounds    : {summary.MandatoryCompoundsMined:N0}");
            Console.WriteLine($"    - O(1) Verb Valence Rules     : {summary.VerbValenceRulesMined:N0}");
            Console.WriteLine("------------------------------------------------------------------------");
            return summary;
        }

        private sealed class EnrichmentRecord
        {
            public string Word = "";
            public string EnglishGloss = "";
            public string Classifier = "";
            public string Synonyms = "";
            public string Antonyms = "";
            public string Collocations = "";
            public string ExampleSentence = "";

            public bool HasAnyValue =>
                !string.IsNullOrEmpty(EnglishGloss) ||
                !string.IsNullOrEmpty(Classifier) ||
                !string.IsNullOrEmpty(Synonyms) ||
                !string.IsNullOrEmpty(Antonyms) ||
                !string.IsNullOrEmpty(Collocations) ||
                !string.IsNullOrEmpty(ExampleSentence);
        }

        private sealed class PublicWiktRow
        {
            public string Word = "";
            public string Pos = "N";
            public string EnglishGloss = "";
            public string[] Classifiers = Array.Empty<string>();
            public string[] Synonyms = Array.Empty<string>();
            public string[] Antonyms = Array.Empty<string>();
            public string[] Derived = Array.Empty<string>();
            public string Topic = "";
        }

        private sealed class PublicWikiDomRow
        {
            public string Word = "";
            public string Domain = "general";
            public string EnglishTitle = "";
            public string MyanmarDef = "";
        }

        private static List<PublicWiktRow> LoadPublicWiktionaryTsv(string path)
        {
            var list = new List<PublicWiktRow>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split('\t');
                if (c.Length < 8) continue;
                list.Add(new PublicWiktRow
                {
                    Word = Norm(c[0].Trim()),
                    Pos = c[1].Trim(),
                    EnglishGloss = c[2].Trim(),
                    Classifiers = SplitCsv(c[3]),
                    Synonyms = SplitCsv(c[4]),
                    Antonyms = SplitCsv(c[5]),
                    Derived = SplitCsv(c[6]),
                    Topic = c[7].Trim()
                });
            }
            return list;
        }

        private static List<PublicWikiDomRow> LoadPublicWikipediaDomainTsv(string path)
        {
            var list = new List<PublicWikiDomRow>();
            if (!File.Exists(path)) return list;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split('\t');
                if (c.Length < 4) continue;
                list.Add(new PublicWikiDomRow
                {
                    Word = Norm(c[0].Trim()),
                    Domain = c[1].Trim(),
                    EnglishTitle = c[2].Trim(),
                    MyanmarDef = Norm(c[3].Trim())
                });
            }
            return list;
        }

        private static string[] SplitCsv(string s) =>
            string.IsNullOrWhiteSpace(s)
                ? Array.Empty<string>()
                : s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => Norm(x.Trim())).Where(x => x.Length > 0).ToArray();

        private static string Norm(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : UnicodeNormalizer.NormalizeCanonicalOrder(s);

        private static bool IsMyanmarNumeralWord(string w) =>
            w == "တစ်" || w == "နှစ်" || w == "သုံး" || w == "လေး" || w == "ငါး" ||
            w == "ခြောက်" || w == "ခုနစ်" || w == "ရှစ်" || w == "ကိုး" || w == "ဆယ်" || w == "တစ်ဆယ်" ||
            (w.Length > 0 && w[0] >= '\u1041' && w[0] <= '\u1049');

        private static string MapUposToLexPos(string upos) => upos switch
        {
            "VERB" => "V",
            "ADJ" => "ADJ",
            "ADV" => "ADV",
            "PROPN" => "PROPN",
            _ => "N"
        };

        private static void AddNestedCount(Dictionary<string, Dictionary<string, int>> outer, string k1, string k2, int delta)
        {
            if (!outer.TryGetValue(k1, out var inner))
            {
                inner = new Dictionary<string, int>(StringComparer.Ordinal);
                outer[k1] = inner;
            }
            inner[k2] = inner.TryGetValue(k2, out int prev) ? prev + delta : delta;
        }

        private static void AddTopColloc(Dictionary<string, List<(string Phrase, double Pmi)>> map, string word, string phrase, double pmi)
        {
            if (!map.TryGetValue(word, out var list))
            {
                list = new List<(string, double)>(6);
                map[word] = list;
            }
            list.Add((phrase, pmi));
            if (list.Count > 12)
            {
                list.Sort((a, b) => b.Pmi.CompareTo(a.Pmi));
                list.RemoveRange(6, list.Count - 6);
            }
        }

        private static bool IsCleanExampleCandidate(string s, HashSet<string> condemnedSet)
        {
            if (UnicodeNormalizer.HasLookalikeError(s, out _, out _))
                return false;
            foreach (Match m in MyanmarTokenScanRx.Matches(s))
            {
                if (condemnedSet.Contains(m.Value))
                    return false;
            }
            return HasBalancedParens(s);
        }

        private static bool HasBalancedParens(string s)
        {
            int d = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') d++;
                else if (s[i] == ')')
                {
                    d--;
                    if (d < 0) return false;
                }
            }
            return d == 0;
        }

        private static bool IsStrictlyValidNewLexeme(string w, HashSet<string> officialSyls, HashSet<string> condemnedSet, bool allowForeignCoda, int minSyllables = 3)
        {
            if (string.IsNullOrEmpty(w) || w.Length < 3 || w.Length > 34 || !PureMyanmarWordRx.IsMatch(w))
                return false;
            if (condemnedSet.Contains(w) || StopParticlesAndFunctionWords.Contains(w) || UnicodeNormalizer.HasLookalikeError(w, out _, out _))
                return false;

            for (int i = 0; i < TrailingInflectionSuffixes.Length; i++)
            {
                string suf = TrailingInflectionSuffixes[i];
                if (w.Length > suf.Length + 2 && w.EndsWith(suf, StringComparison.Ordinal))
                    return false;
            }

            var syls = MyanmarSyllableBreaker.BreakIntoSyllables(w, isPreNormalized: true);
            if (syls.Count < minSyllables || syls.Count > 6)
                return false;

            int matchedLen = 0;
            for (int i = 0; i < syls.Count; i++)
            {
                string st = syls[i];
                matchedLen += st.Length;
                if (!MyanmarSyllableValidator.ValidateSyllable(st, out _, out _))
                    return false;
                if (!allowForeignCoda && !officialSyls.Contains(st))
                    return false;
            }
            return matchedLen == w.Length;
        }

        private static bool CanDecomposeIntoOfficialWords(List<string> syls, HashSet<string> officialWords)
        {
            int n = syls.Count;
            bool[] dp = new bool[n + 1];
            dp[0] = true;
            for (int i = 0; i < n; i++)
            {
                if (!dp[i]) continue;
                var sb = new StringBuilder();
                for (int j = i + 1; j <= n; j++)
                {
                    sb.Append(syls[j - 1]);
                    string piece = sb.ToString();
                    if (officialWords.Contains(piece) || StopParticlesAndFunctionWords.Contains(piece))
                        dp[j] = true;
                }
            }
            return dp[n];
        }

        private static string SanitizeMeaningCue(string word, string mmDef, string enTitle, string domLabel)
        {
            string cue = (mmDef ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            if (!string.IsNullOrEmpty(cue))
            {
                if (cue.Length > 125)
                    cue = MyanmarSyllableBreaker.TruncateAtSyllableBoundary(cue, 125, "…");
                if (!HasBalancedParens(cue))
                    cue = cue.Replace("(", "").Replace(")", "").Trim();
                if (cue.Trim(' ', '။', '၊', '.') == word || cue.IndexOf("နှင့် ငန်း ပေါင်းစပ်ဝေါဟာရ", StringComparison.Ordinal) >= 0)
                    cue = string.Empty;
            }
            if (string.IsNullOrEmpty(cue))
            {
                int parenIdx = domLabel.IndexOf('(');
                string mmDomOnly = (parenIdx > 0 ? domLabel.Substring(0, parenIdx) : domLabel).Trim();
                cue = !string.IsNullOrWhiteSpace(enTitle)
                    ? $"{mmDomOnly}ဆိုင်ရာ ဝေါဟာရ — {enTitle.Replace("(", "").Replace(")", "").Trim()}။"
                    : $"{mmDomOnly}ဆိုင်ရာ စံသတ်ပုံအမှန် မြန်မာဝေါဟာရ။";
            }
            return cue;
        }

        private static string MapEncyclopediaDomainKey(string usageNote)
        {
            if (string.IsNullOrEmpty(usageNote)) return "";
            if (usageNote.IndexOf("ဘူမိဗေဒ", StringComparison.Ordinal) >= 0) return "geology";
            if (usageNote.IndexOf("ဥပဒေ", StringComparison.Ordinal) >= 0) return "legal";
            if (usageNote.IndexOf("ဆေးပညာ", StringComparison.Ordinal) >= 0) return "medical";
            if (usageNote.IndexOf("စီးပွားရေး", StringComparison.Ordinal) >= 0 || usageNote.IndexOf("ဘဏ္ဍာရေး", StringComparison.Ordinal) >= 0) return "finance";
            if (usageNote.IndexOf("နည်းပညာ", StringComparison.Ordinal) >= 0) return "it";
            if (usageNote.IndexOf("ရူပဗေဒ", StringComparison.Ordinal) >= 0 || usageNote.IndexOf("ဓာတုဗေဒ", StringComparison.Ordinal) >= 0 ||
                usageNote.IndexOf("ဇီဝဗေဒ", StringComparison.Ordinal) >= 0 || usageNote.IndexOf("သိပ္ပံ", StringComparison.Ordinal) >= 0) return "science";
            if (usageNote.IndexOf("ပညာရေး", StringComparison.Ordinal) >= 0 || usageNote.IndexOf("မြန်မာသဒ္ဒါ", StringComparison.Ordinal) >= 0) return "education";
            if (usageNote.IndexOf("ယဉ်ကျေးမှု", StringComparison.Ordinal) >= 0) return "governance";
            if (usageNote.IndexOf("ဗုဒ္ဓဘာသာ", StringComparison.Ordinal) >= 0) return "pali";
            return "";
        }

        private static string ComputeConfusionSkeleton(string word)
        {
            // Strict Burmese true-homophone / minimal-pair skeleton:
            // Preserves virama stacks (so 'ကဏ္ဍ' and 'ဂန္ထ' never collide!), while collapsing tone marks (့/း),
            // medial ha-hto (ှ), ya-pin/ra-yit (ျ/ြ), and homophonous Burmese codas (န်/မ်/ံ/ဏ်, တ်/ပ်/ဒ်, စ်/က်).
            if (word.IndexOf('\u1039') >= 0)
                return string.Empty;

            string s = word
                .Replace("\u102B", "\u102C")
                .Replace("\u102E", "\u102D")
                .Replace("\u1030", "\u102F")
                .Replace("\u103C", "\u103B")
                .Replace("\u103E", "")
                .Replace("\u1037", "")
                .Replace("\u1038", "")
                .Replace("\u1014\u103A", "\u1036")
                .Replace("\u1019\u103A", "\u1036")
                .Replace("\u100F\u103A", "\u1036")
                .Replace("\u1015\u103A", "\u1010\u103A")
                .Replace("\u1012\u103A", "\u1010\u103A")
                .Replace("\u1005\u103A", "\u1000\u103A")
                .Replace("\u100A\u103A", "\u102D")
                .Replace("\u1009\u103A", "\u102D");
            return s;
        }

        private static string CleanTsvField(string s) =>
            string.IsNullOrWhiteSpace(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static void WriteMiningReportMarkdown(string docsDir, MiningSummary s)
        {
            Directory.CreateDirectory(docsDir);
            string reportPath = Path.Combine(docsDir, "CORPUS_DATA_MINING_REPORT.md");
            var sb = new StringBuilder(2048);
            sb.AppendLine("# Myanmar Spell Checker — 4-Pillar Corpus & Lexicon Data-Mining Report");
            sb.AppendLine();
            sb.AppendLine("Generated automatically by `MyanmarSpellChecker.AdminTools.exe mine-corpus`.");
            sb.AppendLine();
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("| :--- | ---: |");
            sb.AppendLine($"| **Clean Burmese Sentences Audited** | `{s.CleanSentencesIndexed:N0}` |");
            sb.AppendLine($"| **Distinct Empirical Bigrams Indexed** | `{s.BigramPairsIndexed:N0}` |");
            sb.AppendLine($"| **New Multi-Domain Headwords Mined (`mined_multidomain_lexicon.tsv`)** | `+{s.DomainHeadwordsMined:N0}` |");
            sb.AppendLine($"| **Clean Burmese Idioms & Sayings (`mlc_mm_idioms_clean.tsv`)** | `{s.IdiomsTotal:N0}` |");
            sb.AppendLine($"| **Mined Assimilation/Shadow Orthography Pairs (`mined_orthography_pairs.tsv`)** | `+{s.OrthographyPairsMined:N0}` |");
            sb.AppendLine($"| **Bilingual Gloss, Collocation & Example Enrichments (`mined_entry_enrichments.tsv`)** | `+{s.EntryEnrichmentsMined:N0}` |");
            sb.AppendLine($"| **Zero-FP Discriminative PMI Context Rules (`mined_pmi_context_rules.tsv`)** | `+{s.PmiContextRulesMined:N0}` |");
            sb.AppendLine($"| **O(1) Noun -> Classifier Concord Entries (`mined_noun_classifiers.tsv`)** | `{s.NounClassifiersMined:N0}` |");
            sb.AppendLine($"| **O(1) Mandatory Broken Compounds (`mined_mandatory_compounds.tsv`)** | `{s.MandatoryCompoundsMined:N0}` |");
            sb.AppendLine($"| **O(1) Verb Case-Frame & Collocation Rules (`mined_verb_case_frames.tsv`)** | `{s.VerbValenceRulesMined:N0}` |");
            sb.AppendLine($"| **Mining Execution Time** | `{s.ElapsedSeconds:F1} s` |");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
