// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Security;

namespace MyanmarSpellChecker.AdminTools.Benchmark
{
    [DataContract]
    internal sealed class CorpusSpanDto
    {
        [DataMember(Name = "start")]
        public int Start { get; set; }

        [DataMember(Name = "end")]
        public int End { get; set; }
    }

    [DataContract]
    internal sealed class CorpusExpectedErrorDto
    {
        [DataMember(Name = "error_id", EmitDefaultValue = false)]
        public string? ErrorId { get; set; }

        [DataMember(Name = "error_type", EmitDefaultValue = false)]
        public string? ErrorType { get; set; }

        [DataMember(Name = "error_subtype", EmitDefaultValue = false)]
        public string? ErrorSubtype { get; set; }

        [DataMember(Name = "span")]
        public CorpusSpanDto? Span { get; set; }

        [DataMember(Name = "erroneous_text", EmitDefaultValue = false)]
        public string? ErroneousText { get; set; }

        [DataMember(Name = "gold_correction", EmitDefaultValue = false)]
        public string? GoldCorrection { get; set; }

        [DataMember(Name = "correction", EmitDefaultValue = false)]
        public string? Correction { get; set; }

        [DataMember(Name = "scope", EmitDefaultValue = false)]
        public string? Scope { get; set; }
    }

    [DataContract]
    internal sealed class CorpusSentenceDto
    {
        [DataMember(Name = "id")]
        public string Id { get; set; } = string.Empty;

        [DataMember(Name = "input")]
        public string Input { get; set; } = string.Empty;

        [DataMember(Name = "is_clean")]
        public bool IsClean { get; set; }

        [DataMember(Name = "domain", EmitDefaultValue = false)]
        public string? Domain { get; set; }

        [DataMember(Name = "register", EmitDefaultValue = false)]
        public string? Register { get; set; }

        [DataMember(Name = "mode", EmitDefaultValue = false)]
        public string? Mode { get; set; }

        [DataMember(Name = "difficulty_tier", EmitDefaultValue = false)]
        public int? DifficultyTier { get; set; }

        [DataMember(Name = "expected_errors", EmitDefaultValue = false)]
        public List<CorpusExpectedErrorDto>? ExpectedErrors { get; set; }
    }

    internal static class CorpusBenchmarkRunner
    {
        public static int Run(string[] args)
        {
            bool verboseFp = args.Any(a => string.Equals(a, "--verbose-fp", StringComparison.OrdinalIgnoreCase));
            string? explicitJsonArg = args.Skip(1).FirstOrDefault(a =>
                !a.StartsWith("-", StringComparison.Ordinal) &&
                a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(a));

            string? explicitDbArg = args.Skip(1).FirstOrDefault(a =>
                !a.StartsWith("-", StringComparison.Ordinal) &&
                a.EndsWith(".db", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(a));

            string jsonPath = explicitJsonArg
                ?? Path.Combine("data", "myanmar_corpus_benchmark_2084.json");
            if (!File.Exists(jsonPath))
            {
                Console.Error.WriteLine($"[!] Corpus benchmark JSON not found at: {Path.GetFullPath(jsonPath)}");
                return 1;
            }

            string dbPath = explicitDbArg
                ?? (File.Exists(Path.Combine("data", "mlc_thatpon.db"))
                    ? Path.Combine("data", "mlc_thatpon.db")
                    : Path.Combine("artifacts", "mlc_thatpon.db"));
            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"[!] Encrypted dictionary database not found at: {Path.GetFullPath(dbPath)}");
                return 1;
            }

            List<CorpusSentenceDto> corpus;
            using (var fs = File.OpenRead(jsonPath))
            {
                var ser = new DataContractJsonSerializer(typeof(List<CorpusSentenceDto>));
                corpus = (List<CorpusSentenceDto>)(ser.ReadObject(fs) ?? new List<CorpusSentenceDto>());
            }

            var swLoad = Stopwatch.StartNew();
            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            swLoad.Stop();

            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var casualEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.ColloquialPermissive);

            // Warmup JIT & caches
            _ = generalEngine.CheckText("မြန်မာနိုင်ငံသည် သာယာလှပသော နိုင်ငံတစ်ခု ဖြစ်ပါသည်။", cacheParagraph: false);
            _ = formalEngine.CheckText("ဝန်ကြီးဌာနမှ လုပ်ငန်းစဉ်များ ဖော်ပြထားပါသည်။", cacheParagraph: false);
            _ = casualEngine.CheckText("ကျနော် စျေးမှာ စာအုပ်တအုပ် ဝယ်လာတယ်။", cacheParagraph: false);

            int tp = 0, fp = 0, fn = 0;
            int cleanTotal = 0, cleanWithFp = 0;
            int top1Hits = 0, top3Hits = 0, sugEvalCount = 0;
            double mrrSum = 0.0;
            var latenciesMs = new List<double>(corpus.Count);
            var tierTotals = new int[5];
            var tierTp = new int[5];

            var sw = new Stopwatch();
            foreach (var item in corpus)
            {
                var engine = string.Equals(item.Mode, "formal", StringComparison.OrdinalIgnoreCase)
                    ? formalEngine
                    : (string.Equals(item.Mode, "casual", StringComparison.OrdinalIgnoreCase) ? casualEngine : generalEngine);

                string rawInput = item.Input ?? string.Empty;
                sw.Restart();
                List<ProofingIssue> preds = engine.CheckText(rawInput, cacheParagraph: false);
                sw.Stop();
                latenciesMs.Add(sw.Elapsed.TotalMilliseconds);

                var golds = item.ExpectedErrors ?? new List<CorpusExpectedErrorDto>();
                bool isClean = item.IsClean && golds.Count == 0;
                if (isClean)
                {
                    cleanTotal++;
                    if (preds.Count > 0)
                    {
                        cleanWithFp++;
                        if (verboseFp)
                        {
                            string rep0 = preds[0].Suggestions.Count > 0 ? preds[0].Suggestions[0].Replacement : "(review)";
                            Console.WriteLine($"  [CORPUS-CLEAN-FP] {item.Id} | '{preds[0].OriginalText}' -> '{rep0}' | {SafeSlice(rawInput, 0, 85)}");
                        }
                    }
                }

                var matchedPreds = new HashSet<int>();
                for (int gi = 0; gi < golds.Count; gi++)
                {
                    var g = golds[gi];
                    int gst = g.Span?.Start ?? 0;
                    int gen = g.Span?.End ?? 0;
                    int rawTier = item.DifficultyTier ?? 2;
                    int tier = Math.Min(4, Math.Max(1, rawTier <= 0 ? 2 : rawTier));
                    tierTotals[tier]++;

                    int bestPredIdx = -1;
                    for (int pi = 0; pi < preds.Count; pi++)
                    {
                        if (matchedPreds.Contains(pi))
                            continue;
                        var p = preds[pi];
                        int pst = p.StartIndex;
                        int pen = p.StartIndex + p.Length;
                        if (Math.Max(gst, pst) < Math.Min(gen, pen))
                        {
                            bestPredIdx = pi;
                            break;
                        }
                    }

                    if (bestPredIdx >= 0)
                    {
                        matchedPreds.Add(bestPredIdx);
                        tp++;
                        tierTp[tier]++;

                        string? goldCorr = !string.IsNullOrEmpty(g.GoldCorrection) ? g.GoldCorrection : g.Correction;
                        var matchedPred = preds[bestPredIdx];
                        if (goldCorr != null && !matchedPred.RequiresReviewBeforeBatchFix)
                        {
                            sugEvalCount++;
                            string targetSent = NormalizeForEval(
                                SafeSlice(rawInput, 0, gst) + goldCorr + SafeSlice(rawInput, gen, rawInput.Length - gen));
                            string goldNorm = NormalizeForEval(goldCorr);

                            int rank = 0;
                            for (int si = 0; si < matchedPred.Suggestions.Count; si++)
                            {
                                string sug = matchedPred.Suggestions[si].Replacement;
                                string candSent = NormalizeForEval(
                                    SafeSlice(rawInput, 0, matchedPred.StartIndex) +
                                    sug +
                                    SafeSlice(rawInput, matchedPred.StartIndex + matchedPred.Length, rawInput.Length - (matchedPred.StartIndex + matchedPred.Length)));
                                if (candSent == targetSent || NormalizeForEval(sug) == goldNorm)
                                {
                                    rank = si + 1;
                                    break;
                                }
                            }

                            if (rank == 1) top1Hits++;
                            if (rank >= 1 && rank <= 3) top3Hits++;
                            if (rank >= 1) mrrSum += 1.0 / rank;
                        }
                    }
                    else
                    {
                        fn++;
                    }
                }

                int unmatched = preds.Count - matchedPreds.Count;
                if (unmatched > 0)
                {
                    fp += unmatched;
                }
            }

            latenciesMs.Sort();
            double p50 = latenciesMs[(int)(0.50 * latenciesMs.Count)];
            double p95 = latenciesMs[(int)(0.95 * latenciesMs.Count)];
            double p99 = latenciesMs[(int)(0.99 * latenciesMs.Count)];
            double meanMs = latenciesMs.Average();

            double precision = (tp + fp) > 0 ? (double)tp / (tp + fp) : 1.0;
            double recall = (tp + fn) > 0 ? (double)tp / (tp + fn) : 1.0;
            double f1 = (precision + recall) > 0 ? 2.0 * precision * recall / (precision + recall) : 0.0;
            double cleanFpr = cleanTotal > 0 ? (double)cleanWithFp / cleanTotal : 0.0;
            double top1Acc = sugEvalCount > 0 ? (double)top1Hits / sugEvalCount : 1.0;
            double top3Acc = sugEvalCount > 0 ? (double)top3Hits / sugEvalCount : 1.0;
            double mrr = sugEvalCount > 0 ? mrrSum / sugEvalCount : 1.0;
            double composite = 0.30 * f1 + 0.25 * mrr + 0.20 * (1.0 - cleanFpr) + 0.15 * top1Acc + 0.10 * (1.0 - Math.Min(p95 / 500.0, 1.0));

            Console.WriteLine();
            Console.WriteLine("================================================================================================");
            Console.WriteLine(" Myanmar Spell Checker Suite — 2,084-Sentence Multi-Domain Corpus Benchmark Report (--corpus)");
            Console.WriteLine($" Corpus File           : {Path.GetFileName(jsonPath)} ({corpus.Count:N0} sentences)");
            Console.WriteLine($" Dictionary DB         : {new FileInfo(dbPath).Length / (1024.0 * 1024.0):F2} MB ({trie.WordCount:N0} headwords) | Load={swLoad.Elapsed.TotalMilliseconds:F1} ms");
            Console.WriteLine("------------------------------------------------------------------------------------------------");
            for (int t = 1; t <= 4; t++)
            {
                string tName = t switch
                {
                    1 => "Tier 1: Easy (Syllable/Char)",
                    2 => "Tier 2: Medium (Lexical/Phonetic)",
                    3 => "Tier 3: Hard (Boundary/Compound)",
                    _ => "Tier 4: Hardest (Context/Syntax)"
                };
                int tot = tierTotals[t];
                int hit = tierTp[t];
                int miss = tot - hit;
                double rec = tot > 0 ? (double)hit / tot * 100.0 : 100.0;
                Console.WriteLine($"  {tName,-34} | Total={tot,4} | TP={hit,4} | FN={miss,4} | Recall={rec,6:F1}%");
            }
            Console.WriteLine("------------------------------------------------------------------------------------------------");
            Console.WriteLine($" TP={tp}  FP={fp}  FN={fn}  | Clean Sentences={cleanTotal}, Clean w/ FP={cleanWithFp}");
            Console.WriteLine($" Overall Precision     : {precision * 100.0:F2}%");
            Console.WriteLine($" Overall Recall        : {recall * 100.0:F2}%");
            Console.WriteLine($" Overall F1 Score      : {f1 * 100.0:F2}%");
            Console.WriteLine($" Clean-Sentence FPR    : {cleanFpr * 100.0:F2}%  ({cleanWithFp}/{cleanTotal} clean sentences)");
            Console.WriteLine($" Top-1 Suggestion Acc  : {top1Acc * 100.0:F2}%  (Top-3: {top3Acc * 100.0:F2}%, n={sugEvalCount})");
            Console.WriteLine($" Mean Reciprocal Rank  : {mrr:F4}");
            Console.WriteLine($" Sentence Latency      : p50={p50:F2} ms | p95={p95:F2} ms | p99={p99:F2} ms | mean={meanMs:F2} ms");
            Console.WriteLine($" COMPOSITE SCORE       : {composite:F4}");
            Console.WriteLine("================================================================================================");
            return 0;
        }

        private static string SafeSlice(string s, int start, int length)
        {
            if (start < 0) start = 0;
            if (start >= s.Length || length <= 0) return string.Empty;
            if (start + length > s.Length) length = s.Length - start;
            return s.Substring(start, length);
        }

        private static string NormalizeForEval(string s)
        {
            return UnicodeNormalizer.NormalizeCanonicalOrder(s ?? string.Empty).Normalize(NormalizationForm.FormC).Trim();
        }
    }
}
