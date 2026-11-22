using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Security;
using MyanmarSpellChecker.Core.Spreadsheet;

namespace MyanmarSpellChecker.AdminTools.Benchmark
{
    /// <summary>
    /// Built-in offline Progressive (Easy -> Medium -> Hard -> Hardest -> Nightmare -> Grandmaster -> Abyssal Mythic -> Official Gov Portal B2G -> Optimised Top-1 Championship)
    /// Accuracy, Clean-FPR, Suggestion-MRR, Full-Sentence Multi-Fix Rate, Latency &amp; Office Throughput Benchmark Suite.
    /// Run via:
    ///   <c>MyanmarSpellChecker.AdminTools.exe benchmark [optionalDbPath]</c>
    ///   <c>MyanmarSpellChecker.AdminTools.exe benchmark --hardest [optionalDbPath]</c>
    ///   <c>MyanmarSpellChecker.AdminTools.exe benchmark --abyssal [optionalDbPath]</c>
    ///   <c>MyanmarSpellChecker.AdminTools.exe benchmark --gov [optionalDbPath]</c>
    ///   <c>MyanmarSpellChecker.AdminTools.exe benchmark --top1 [optionalDbPath]</c>
    /// </summary>
    internal static class ProofingBenchmarkRunner
    {
        private enum BenchmarkDifficulty
        {
            CleanControl = 0,
            Easy = 1,
            Medium = 2,
            Hard = 3,
            Hardest = 4,
            Nightmare = 5,
            GrandmasterTorture = 6,
            AbyssalMythic = 7,
            GovernmentB2G = 8,
            Top1Championship = 9,
            StandardHardest = 10,
            HybridStatistical = 11,
            NationalApex = 12,
            GovLivePrecision = 13,
            CataclysmicNightmare = 14,
            DataMinedMastery = 15
        }

        private readonly struct ExpectedErrorSpan
        {
            public string WrongSpan { get; }
            public string TopCorrection { get; }

            public ExpectedErrorSpan(string wrongSpan, string topCorrection)
            {
                WrongSpan = wrongSpan;
                TopCorrection = topCorrection;
            }
        }

        private sealed class BenchmarkCase
        {
            public string Domain { get; }
            public BenchmarkDifficulty Difficulty { get; }
            public ProofingStyleMode Mode { get; }
            public string Input { get; }
            public IReadOnlyList<ExpectedErrorSpan> ExpectedErrors { get; }

            public BenchmarkCase(
                string domain,
                BenchmarkDifficulty difficulty,
                ProofingStyleMode mode,
                string input)
            {
                Domain = domain;
                Difficulty = difficulty;
                Mode = mode;
                Input = input;
                ExpectedErrors = Array.Empty<ExpectedErrorSpan>();
            }

            public BenchmarkCase(
                string domain,
                BenchmarkDifficulty difficulty,
                ProofingStyleMode mode,
                string input,
                string expectedWrongSpan,
                string expectedTopCorrection)
            {
                Domain = domain;
                Difficulty = difficulty;
                Mode = mode;
                Input = input;
                ExpectedErrors = new[] { new ExpectedErrorSpan(expectedWrongSpan, expectedTopCorrection) };
            }

            public BenchmarkCase(
                string domain,
                BenchmarkDifficulty difficulty,
                ProofingStyleMode mode,
                string input,
                params (string WrongSpan, string TopCorrection)[] multipleErrors)
            {
                Domain = domain;
                Difficulty = difficulty;
                Mode = mode;
                Input = input;
                var list = new List<ExpectedErrorSpan>(multipleErrors.Length);
                for (int i = 0; i < multipleErrors.Length; i++)
                {
                    list.Add(new ExpectedErrorSpan(multipleErrors[i].WrongSpan, multipleErrors[i].TopCorrection));
                }
                ExpectedErrors = list;
            }

            public bool IsClean => ExpectedErrors.Count == 0;
        }

        private sealed class TierAccumulator
        {
            public int Sentences;
            public int PerfectMultiFixSentences;
            public int GoldSpans;
            public int Tp;
            public int Fn;
            public int Fp;
            public int Top1Hits;
            public int EvalSugCount;
            public double MrrSum;
            public readonly List<double> LatenciesMs = new List<double>();
        }

        public static int Run(string[] args)
        {
            bool minedOnly = args.Any(a =>
                string.Equals(a, "--mined", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "mined", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--data-mined", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--v80", StringComparison.OrdinalIgnoreCase));

            bool cataclysmOnly = !minedOnly && args.Any(a =>
                string.Equals(a, "--cataclysm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "cataclysm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--nightmare-ultra", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "nightmare-ultra", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--v78", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--v79", StringComparison.OrdinalIgnoreCase));

            bool govLiveOnly = !minedOnly && !cataclysmOnly && args.Any(a =>
                string.Equals(a, "--gov-live", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "gov-live", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--v76", StringComparison.OrdinalIgnoreCase));

            bool nationalOnly = !minedOnly && !cataclysmOnly && !govLiveOnly && args.Any(a =>
                string.Equals(a, "--national", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "national", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--apex", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "apex", StringComparison.OrdinalIgnoreCase));

            bool hybridOnly = !minedOnly && !cataclysmOnly && !govLiveOnly && !nationalOnly && args.Any(a =>
                string.Equals(a, "--hybrid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "hybrid", StringComparison.OrdinalIgnoreCase));

            bool standardOnly = !minedOnly && !cataclysmOnly && !govLiveOnly && !nationalOnly && !hybridOnly && args.Any(a =>
                string.Equals(a, "--standard", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "standard", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--standard-hardest", StringComparison.OrdinalIgnoreCase));

            bool top1Only = !minedOnly && !cataclysmOnly && !govLiveOnly && !nationalOnly && !hybridOnly && !standardOnly && args.Any(a =>
                string.Equals(a, "--top1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "top1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--optimised", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "optimised", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--optimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "optimized", StringComparison.OrdinalIgnoreCase));

            bool govOnly = !minedOnly && !cataclysmOnly && !govLiveOnly && !nationalOnly && !hybridOnly && !standardOnly && !top1Only && args.Any(a =>
                string.Equals(a, "--gov", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "gov", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--government", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "government", StringComparison.OrdinalIgnoreCase));

            bool abyssalOnly = !minedOnly && !cataclysmOnly && !govLiveOnly && !nationalOnly && !hybridOnly && !standardOnly && !top1Only && !govOnly && args.Any(a =>
                string.Equals(a, "--abyssal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "abyssal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--extreme", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "extreme", StringComparison.OrdinalIgnoreCase));

            bool hardestOnly = !minedOnly && !cataclysmOnly && !govLiveOnly && !nationalOnly && !hybridOnly && !standardOnly && !top1Only && !govOnly && !abyssalOnly && args.Any(a =>
                string.Equals(a, "--hardest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "hardest", StringComparison.OrdinalIgnoreCase));

            string? explicitDbArg = args.Skip(1).FirstOrDefault(a =>
                !a.StartsWith("-", StringComparison.Ordinal) &&
                !string.Equals(a, "mined", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "cataclysm", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "nightmare-ultra", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "gov-live", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "national", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "apex", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "hybrid", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "standard", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "hardest", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "abyssal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "extreme", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "gov", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "government", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "top1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "optimised", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a, "optimized", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(a));

            string dbPath = explicitDbArg
                ?? (File.Exists(Path.Combine("data", "mlc_thatpon.db"))
                    ? Path.Combine("data", "mlc_thatpon.db")
                    : Path.Combine("artifacts", "mlc_thatpon.db"));

            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"[!] Encrypted dictionary database not found at: {Path.GetFullPath(dbPath)}");
                return 1;
            }

            var swLoad = Stopwatch.StartNew();
            var repo = new EncryptedDictionaryRepository(dbPath);
            MyanmarTrie trie = repo.LoadIntoTrie(AntiTamperGuard.ReconstructProtectedFallbackKey());
            swLoad.Stop();

            var generalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.GeneralStandard);
            var formalEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.FormalOfficial);
            var colloquialEngine = new MyanmarProofingEngine(trie, ProofingStyleMode.ColloquialPermissive);

            // JIT warmup
            for (int w = 0; w < 3; w++)
            {
                _ = generalEngine.CheckText("မြန်မာနိုင်ငံသည် သာယာလှပသော နိုင်ငံတစ်ခု ဖြစ်ပါသည်။ ကျောင်းသားက မမေးပဲ ဖြစ်စေမယ် (နိုင်) လာမ ည့်နှစ်။");
                _ = formalEngine.CheckText("ဝန်ကြီးဌာနမှ လုပ်ငန်းစင်များ ဖေါ်ပြထားပါသည်။ စာ ကြည့်တိုက်နှင့် အစီရင် ခံစာကို ၉/ကဆန(နိုင်)၁၂၃၄၅၆ အရ စိစစ်သည်။");
                _ = colloquialEngine.CheckText("ကျနော် စျေးမှာ စာအုပ်တအုပ် ဝယ်လာတယ်။");
            }

            List<BenchmarkCase> fullSuite = BuildCleanRoomProgressiveSuite();
            List<BenchmarkCase> suite = minedOnly
                ? fullSuite.Where(c =>
                    c.Difficulty == BenchmarkDifficulty.CleanControl ||
                    c.Difficulty == BenchmarkDifficulty.DataMinedMastery).ToList()
                : cataclysmOnly
                ? fullSuite.Where(c =>
                    c.Difficulty == BenchmarkDifficulty.CleanControl ||
                    c.Difficulty == BenchmarkDifficulty.CataclysmicNightmare).ToList()
                : govLiveOnly
                    ? fullSuite.Where(c =>
                        c.Difficulty == BenchmarkDifficulty.CleanControl ||
                        c.Difficulty == BenchmarkDifficulty.GovLivePrecision).ToList()
                    : nationalOnly
                        ? fullSuite.Where(c =>
                            c.Difficulty == BenchmarkDifficulty.CleanControl ||
                            c.Difficulty == BenchmarkDifficulty.NationalApex).ToList()
                        : hybridOnly
                            ? fullSuite.Where(c =>
                                c.Difficulty == BenchmarkDifficulty.CleanControl ||
                                c.Difficulty == BenchmarkDifficulty.HybridStatistical).ToList()
                            : standardOnly
                                ? fullSuite.Where(c =>
                                    c.Difficulty == BenchmarkDifficulty.CleanControl ||
                                    c.Difficulty == BenchmarkDifficulty.StandardHardest).ToList()
                                : top1Only
                                    ? fullSuite.Where(c =>
                                        c.Difficulty == BenchmarkDifficulty.CleanControl ||
                                        c.Difficulty == BenchmarkDifficulty.Top1Championship).ToList()
                                    : govOnly
                                        ? fullSuite.Where(c =>
                                            c.Difficulty == BenchmarkDifficulty.CleanControl ||
                                            c.Difficulty == BenchmarkDifficulty.GovernmentB2G).ToList()
                                        : abyssalOnly
                                            ? fullSuite.Where(c =>
                                                c.Difficulty == BenchmarkDifficulty.CleanControl ||
                                                c.Difficulty >= BenchmarkDifficulty.GrandmasterTorture).ToList()
                                            : hardestOnly
                                                ? fullSuite.Where(c =>
                                                    c.Difficulty == BenchmarkDifficulty.CleanControl ||
                                                    c.Difficulty >= BenchmarkDifficulty.Hardest).ToList()
                                                : fullSuite;

            var latenciesMs = new List<double>(suite.Count);
            var tierOrder = minedOnly
                ? new[] { BenchmarkDifficulty.DataMinedMastery }
                : cataclysmOnly
                    ? new[] { BenchmarkDifficulty.CataclysmicNightmare }
                    : govLiveOnly
                        ? new[] { BenchmarkDifficulty.GovLivePrecision }
                        : nationalOnly
                            ? new[] { BenchmarkDifficulty.NationalApex }
                            : hybridOnly
                                ? new[] { BenchmarkDifficulty.HybridStatistical }
                                : standardOnly
                                    ? new[] { BenchmarkDifficulty.StandardHardest }
                                    : top1Only
                                        ? new[] { BenchmarkDifficulty.Top1Championship }
                                        : govOnly
                                            ? new[] { BenchmarkDifficulty.GovernmentB2G }
                                            : abyssalOnly
                                                ? new[] { BenchmarkDifficulty.GrandmasterTorture, BenchmarkDifficulty.AbyssalMythic, BenchmarkDifficulty.GovernmentB2G, BenchmarkDifficulty.Top1Championship, BenchmarkDifficulty.StandardHardest, BenchmarkDifficulty.HybridStatistical, BenchmarkDifficulty.NationalApex, BenchmarkDifficulty.GovLivePrecision, BenchmarkDifficulty.CataclysmicNightmare, BenchmarkDifficulty.DataMinedMastery }
                                                : hardestOnly
                                                    ? new[] { BenchmarkDifficulty.Hardest, BenchmarkDifficulty.Nightmare, BenchmarkDifficulty.GrandmasterTorture, BenchmarkDifficulty.AbyssalMythic, BenchmarkDifficulty.GovernmentB2G, BenchmarkDifficulty.Top1Championship, BenchmarkDifficulty.StandardHardest, BenchmarkDifficulty.HybridStatistical, BenchmarkDifficulty.NationalApex, BenchmarkDifficulty.GovLivePrecision, BenchmarkDifficulty.CataclysmicNightmare, BenchmarkDifficulty.DataMinedMastery }
                                                    : new[]
                                                    {
                                                        BenchmarkDifficulty.Easy,
                                                        BenchmarkDifficulty.Medium,
                                                        BenchmarkDifficulty.Hard,
                                                        BenchmarkDifficulty.Hardest,
                                                        BenchmarkDifficulty.Nightmare,
                                                        BenchmarkDifficulty.GrandmasterTorture,
                                                        BenchmarkDifficulty.AbyssalMythic,
                                                        BenchmarkDifficulty.GovernmentB2G,
                                                        BenchmarkDifficulty.Top1Championship,
                                                        BenchmarkDifficulty.StandardHardest,
                                                        BenchmarkDifficulty.HybridStatistical,
                                                        BenchmarkDifficulty.NationalApex,
                                                        BenchmarkDifficulty.GovLivePrecision,
                                                        BenchmarkDifficulty.CataclysmicNightmare,
                                                        BenchmarkDifficulty.DataMinedMastery
                                                    };

            var tierStats = new Dictionary<BenchmarkDifficulty, TierAccumulator>();
            foreach (var td in tierOrder)
            {
                tierStats[td] = new TierAccumulator();
            }

            int tp = 0, fp = 0, fn = 0;
            int cleanTotal = 0, cleanWithFp = 0;
            int top1Hits = 0, evalSugCount = 0;
            double mrrSum = 0.0;

            var sw = new Stopwatch();
            foreach (var tc in suite)
            {
                var engine = tc.Mode == ProofingStyleMode.FormalOfficial
                    ? formalEngine
                    : (tc.Mode == ProofingStyleMode.ColloquialPermissive ? colloquialEngine : generalEngine);
                sw.Restart();
                List<ProofingIssue> issues = engine.CheckText(tc.Input);
                sw.Stop();
                double elapsedMs = sw.Elapsed.TotalMilliseconds;
                latenciesMs.Add(elapsedMs);

                if (tc.IsClean)
                {
                    cleanTotal++;
                    if (issues.Count > 0)
                    {
                        cleanWithFp++;
                        fp += issues.Count;
                        string fpRep = issues[0].Suggestions.Count > 0 ? issues[0].Suggestions[0].Replacement : "(none)";
                        Console.WriteLine($"  [CLEAN-FP] ({tc.Domain}) '{tc.Input}' -> '{issues[0].OriginalText}' => '{fpRep}'");
                    }
                    continue;
                }

                TierAccumulator acc = tierStats[tc.Difficulty];
                acc.Sentences++;
                acc.GoldSpans += tc.ExpectedErrors.Count;
                acc.LatenciesMs.Add(elapsedMs);

                var matchedIssueIndices = new HashSet<int>();
                bool allSpansTop1InSentence = true;

                for (int eIdx = 0; eIdx < tc.ExpectedErrors.Count; eIdx++)
                {
                    ExpectedErrorSpan gold = tc.ExpectedErrors[eIdx];
                    string normExpectedSpan = UnicodeNormalizer.NormalizeCanonicalOrder(gold.WrongSpan);
                    string normExpectedFix = UnicodeNormalizer.NormalizeCanonicalOrder(gold.TopCorrection);

                    int spanIdx = tc.Input.IndexOf(gold.WrongSpan, StringComparison.Ordinal);
                    int goldEnd = spanIdx >= 0 ? spanIdx + gold.WrongSpan.Length : tc.Input.Length;

                    int matchedIdx = -1;
                    for (int pIdx = 0; pIdx < issues.Count; pIdx++)
                    {
                        if (matchedIssueIndices.Contains(pIdx))
                            continue;
                        ProofingIssue iss = issues[pIdx];
                        string issOrigNorm = UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText);
                        bool spanOverlap = spanIdx >= 0 && Math.Max(spanIdx, iss.StartIndex) < Math.Min(goldEnd, iss.StartIndex + iss.Length);
                        if (spanOverlap || issOrigNorm == normExpectedSpan)
                        {
                            matchedIdx = pIdx;
                            break;
                        }
                    }

                    if (matchedIdx >= 0)
                    {
                        matchedIssueIndices.Add(matchedIdx);
                        ProofingIssue matched = issues[matchedIdx];
                        tp++;
                        acc.Tp++;
                        evalSugCount++;
                        acc.EvalSugCount++;

                        string targetSpliced = spanIdx >= 0
                            ? UnicodeNormalizer.NormalizeCanonicalOrder(
                                tc.Input.Substring(0, spanIdx) + gold.TopCorrection + tc.Input.Substring(goldEnd))
                            : normExpectedFix;

                        int foundRank = 0;
                        for (int r = 0; r < matched.Suggestions.Count; r++)
                        {
                            string sugNorm = UnicodeNormalizer.NormalizeCanonicalOrder(matched.Suggestions[r].Replacement);
                            string candSpliced = UnicodeNormalizer.NormalizeCanonicalOrder(
                                tc.Input.Substring(0, matched.StartIndex) +
                                matched.Suggestions[r].Replacement +
                                tc.Input.Substring(matched.StartIndex + matched.Length));
                            if (sugNorm == normExpectedFix || candSpliced == targetSpliced || sugNorm.Contains(normExpectedFix))
                            {
                                foundRank = r + 1;
                                break;
                            }
                        }

                        if (foundRank > 0)
                        {
                            if (foundRank == 1)
                            {
                                top1Hits++;
                                acc.Top1Hits++;
                            }
                            else
                            {
                                allSpansTop1InSentence = false;
                                Console.WriteLine($"  [RANK-{foundRank}] [{tc.Difficulty}] ({tc.Domain}) '{gold.WrongSpan}' -> expected '{gold.TopCorrection}', got top1 '{(matched.Suggestions.Count > 0 ? matched.Suggestions[0].Replacement : "(none)")}'");
                            }
                            double rr = 1.0 / foundRank;
                            mrrSum += rr;
                            acc.MrrSum += rr;
                        }
                        else
                        {
                            allSpansTop1InSentence = false;
                            string got = matched.Suggestions.Count > 0 ? matched.Suggestions[0].Replacement : "(none)";
                            Console.WriteLine($"  [SUG-MISS] [{tc.Difficulty}] expected '{gold.WrongSpan}'->'{gold.TopCorrection}', got '{matched.OriginalText}'->'{got}'");
                        }
                    }
                    else
                    {
                        allSpansTop1InSentence = false;
                        fn++;
                        acc.Fn++;
                        Console.WriteLine($"  [FN-MISS]  [{tc.Difficulty}] ({tc.Domain}) missed '{gold.WrongSpan}' -> '{gold.TopCorrection}' in '{tc.Input}'");
                    }
                }

                int unmatchedPreds = issues.Count - matchedIssueIndices.Count;
                if (unmatchedPreds > 0)
                {
                    fp += unmatchedPreds;
                    acc.Fp += unmatchedPreds;
                    allSpansTop1InSentence = false;
                    for (int u = 0; u < issues.Count; u++)
                    {
                        if (!matchedIssueIndices.Contains(u))
                        {
                            string uRep = issues[u].Suggestions.Count > 0 ? issues[u].Suggestions[0].Replacement : "(none)";
                            Console.WriteLine($"  [EXTRA-FP] [{tc.Difficulty}] '{issues[u].OriginalText}'->'{uRep}' in '{tc.Input}'");
                        }
                    }
                }

                if (allSpansTop1InSentence)
                {
                    acc.PerfectMultiFixSentences++;
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
            double top1Acc = evalSugCount > 0 ? (double)top1Hits / evalSugCount : 0.0;
            double mrr = evalSugCount > 0 ? mrrSum / evalSugCount : 0.0;
            double composite = 0.30 * f1 + 0.25 * mrr + 0.20 * (1.0 - cleanFpr) + 0.15 * top1Acc + 0.10 * (1.0 - Math.Min(p95 / 500.0, 1.0));

            // 2D Excel Worksheet Block Throughput (1,250 cells)
            const int rows = 250, cols = 5;
            object?[,] valGrid = new object?[rows, cols];
            object?[,] fmlGrid = new object?[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                valGrid[r, 0] = (double)(r + 1); fmlGrid[r, 0] = (double)(r + 1);
                valGrid[r, 1] = "ကျေးလက်လမ်းဖွံ့ဖြိုးရေးဦးစီးဌာန"; fmlGrid[r, 1] = valGrid[r, 1];
                valGrid[r, 2] = "ပညာရေ ဝန်ကြီး ဌာန"; fmlGrid[r, 2] = valGrid[r, 2];
                valGrid[r, 3] = $"MMR-2026-{r + 1:D4}"; fmlGrid[r, 3] = valGrid[r, 3];
                valGrid[r, 4] = "ပညာရေ"; fmlGrid[r, 4] = $"=CONCATENATE(\"ပညာရေ \", C{r + 1})";
            }
            var swExcel = Stopwatch.StartNew();
            var excelIssues = SpreadsheetProofingHelper.ScanBlockValues(
                formalEngine, "Bench.xlsx", "Sheet1", 1, 1, rows, cols, valGrid, fmlGrid, null);
            swExcel.Stop();
            double cellsPerSec = (rows * cols) / Math.Max(0.001, swExcel.Elapsed.TotalSeconds);

            long memBytes = GC.GetTotalMemory(forceFullCollection: true);
            string modeBanner = minedOnly
                ? "4-PILLAR CORPUS DATA-MINING & O(1) GRAMMAR MASTERY (--mined: 2,290+ Classifiers + 4,100+ Compounds + Multi-Domain + PMI)"
                : cataclysmOnly
                    ? "CATACLYSMIC NIGHTMARE ULTRA-TORTURE BENCHMARK (--cataclysm: Multi-Site A-Corruption + Compound-in-SOV + Phonotactic + Collocation + 8x Boss)"
                    : govLiveOnly
                        ? "LIVE *.gov.mm PRECISION & MULTI-SYLLABLE REPAIR BENCHMARK (--gov-live: 25-Portal Zero-FP + Surgical ZWSP + Pat-Sint Recovery)"
                        : nationalOnly
                            ? "NATIONAL APEX MULTI-DOMAIN BENCHMARK (--national: Union Gazette, Judicial, Revenue, University, Hybrid Paste & SOV)"
                            : hybridOnly
                                ? "NEXT-GEN HYBRID STATISTICAL, SOV & INTRUDER MODE (--hybrid: K=3 Beam Viterbi + Noisy Channel + SOV + Casual Mode)"
                                : standardOnly
                                    ? "HARDEST STANDARD ORTHOGRAPHY, SIMPLEX/CAUSATIVE & MLC CONFUSABLES (--standard)"
                                    : top1Only
                                        ? "OPTIMISED TOP-1 RANKING & SPEED CHAMPIONSHIP MODE (--top1 / --optimised: Whole-Stem Coalescer + Context Reranker)"
                                        : govOnly
                                            ? "OFFICIAL B2G GOVERNMENT PORTAL & GAZETTE MODE (--gov: Live .gov.mm Ministry & Gazette Suite)"
                                            : abyssalOnly
                                                ? "ABYSSAL MYTHIC -> DATA-MINED MASTERY SUITE MODE (--abyssal: Grandmaster -> Data-Mined)"
                                                : hardestOnly
                                                    ? "ULTRA-HARDEST STRESS MODE (--hardest: Hardest -> Data-Mined)"
                                                    : "FULL 15-TIER PROGRESSIVE SUITE (Easy -> Abyssal -> Gov B2G -> Top-1 -> Standard -> Hybrid -> National Apex -> Gov Live -> Cataclysmic -> Data-Mined)";

            Console.WriteLine();
            Console.WriteLine("================================================================================================");
            Console.WriteLine(" Myanmar Spell Checker Suite — Progressive & Grandmaster Torture Benchmark Report");
            Console.WriteLine($" Mode                  : {modeBanner}");
            Console.WriteLine("================================================================================================");
            Console.WriteLine($" Dictionary DB Size    : {new FileInfo(dbPath).Length / (1024.0 * 1024.0):F2} MB ({trie.WordCount:N0} headwords, {trie.BigramCount:N0} bigrams)");
            Console.WriteLine($" Cold DB Load Time     : {swLoad.Elapsed.TotalMilliseconds:F1} ms");
            Console.WriteLine($" Managed Heap Footprint: {memBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine("------------------------------------------------------------------------------------------------");
            Console.WriteLine(" Tier                             | Sents | Spans |  TP |  FN | Recall  | Top-1   |   MRR  | p95 ms");
            Console.WriteLine(" ---------------------------------+-------+-------+-----+-----+---------+---------+--------+-------");
            foreach (var td in tierOrder)
            {
                PrintTierRow(GetTierLabel(td), tierStats[td]);
            }
            Console.WriteLine($" Tier 0: Evil-Twin Clean Controls | {cleanTotal,5} | FPR = {cleanFpr * 100.0,5:F2}% ({cleanWithFp}/{cleanTotal} false positives)");
            if (tierStats.TryGetValue(BenchmarkDifficulty.GrandmasterTorture, out var gmAcc) && gmAcc.Sentences > 0)
            {
                double perfectPct = (double)gmAcc.PerfectMultiFixSentences / gmAcc.Sentences * 100.0;
                Console.WriteLine($" Grandmaster All-Errors-In-1-Pass | {gmAcc.PerfectMultiFixSentences,2}/{gmAcc.Sentences,2} sentences ({perfectPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.AbyssalMythic, out var abAcc) && abAcc.Sentences > 0)
            {
                double abPct = (double)abAcc.PerfectMultiFixSentences / abAcc.Sentences * 100.0;
                Console.WriteLine($" Abyssal 5-6x Errors-In-1-Pass    | {abAcc.PerfectMultiFixSentences,2}/{abAcc.Sentences,2} sentences ({abPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.GovernmentB2G, out var govAcc) && govAcc.Sentences > 0)
            {
                double govPct = (double)govAcc.PerfectMultiFixSentences / govAcc.Sentences * 100.0;
                Console.WriteLine($" Gov Portal B2G Multi-Fix 1-Pass  | {govAcc.PerfectMultiFixSentences,2}/{govAcc.Sentences,2} sentences ({govPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.Top1Championship, out var t1Acc) && t1Acc.Sentences > 0)
            {
                double t1Pct = (double)t1Acc.PerfectMultiFixSentences / t1Acc.Sentences * 100.0;
                Console.WriteLine($" Optimised Top-1 Multi-Fix 1-Pass | {t1Acc.PerfectMultiFixSentences,2}/{t1Acc.Sentences,2} sentences ({t1Pct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.StandardHardest, out var stdAcc) && stdAcc.Sentences > 0)
            {
                double stdPct = (double)stdAcc.PerfectMultiFixSentences / stdAcc.Sentences * 100.0;
                Console.WriteLine($" Hardest Standard Multi-Fix 1-Pass| {stdAcc.PerfectMultiFixSentences,2}/{stdAcc.Sentences,2} sentences ({stdPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.HybridStatistical, out var hybAcc) && hybAcc.Sentences > 0)
            {
                double hybPct = (double)hybAcc.PerfectMultiFixSentences / hybAcc.Sentences * 100.0;
                Console.WriteLine($" Hybrid Statistical Multi-Fix 1-P | {hybAcc.PerfectMultiFixSentences,2}/{hybAcc.Sentences,2} sentences ({hybPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.NationalApex, out var natAcc) && natAcc.Sentences > 0)
            {
                double natPct = (double)natAcc.PerfectMultiFixSentences / natAcc.Sentences * 100.0;
                Console.WriteLine($" National Apex Multi-Fix 1-Pass   | {natAcc.PerfectMultiFixSentences,2}/{natAcc.Sentences,2} sentences ({natPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.GovLivePrecision, out var glAcc) && glAcc.Sentences > 0)
            {
                double glPct = (double)glAcc.PerfectMultiFixSentences / glAcc.Sentences * 100.0;
                Console.WriteLine($" Gov Live v76 Multi-Fix 1-Pass    | {glAcc.PerfectMultiFixSentences,2}/{glAcc.Sentences,2} sentences ({glPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.CataclysmicNightmare, out var catAcc) && catAcc.Sentences > 0)
            {
                double catPct = (double)catAcc.PerfectMultiFixSentences / catAcc.Sentences * 100.0;
                Console.WriteLine($" Cataclysmic Nightmare 1-Pass     | {catAcc.PerfectMultiFixSentences,2}/{catAcc.Sentences,2} sentences ({catPct:F1}%) solved 100% simultaneously");
            }
            if (tierStats.TryGetValue(BenchmarkDifficulty.DataMinedMastery, out var minAcc) && minAcc.Sentences > 0)
            {
                double minPct = (double)minAcc.PerfectMultiFixSentences / minAcc.Sentences * 100.0;
                Console.WriteLine($" Data-Mined Corpus Mastery 1-Pass | {minAcc.PerfectMultiFixSentences,2}/{minAcc.Sentences,2} sentences ({minPct:F1}%) solved 100% simultaneously");
            }
            Console.WriteLine("------------------------------------------------------------------------------------------------");
            Console.WriteLine($" Overall Precision     : {precision * 100.0:F2}%  (TP={tp}, FP={fp}, FN={fn})");
            Console.WriteLine($" Overall Recall        : {recall * 100.0:F2}%");
            Console.WriteLine($" Overall F1 Score      : {f1 * 100.0:F2}%");
            Console.WriteLine($" Clean-Sentence FPR    : {cleanFpr * 100.0:F2}%  ({cleanWithFp}/{cleanTotal} clean sentences)");
            Console.WriteLine($" Top-1 Suggestion Acc  : {top1Acc * 100.0:F2}%");
            Console.WriteLine($" Mean Reciprocal Rank  : {mrr:F4}");
            Console.WriteLine($" Sentence Latency      : p50={p50:F2} ms | p95={p95:F2} ms | p99={p99:F2} ms | mean={meanMs:F2} ms");
            Console.WriteLine($" Excel 2D Block Scan   : {swExcel.Elapsed.TotalMilliseconds:F1} ms for 1,250 cells ({cellsPerSec:N0} cells/sec, {excelIssues.Count} issues)");
            Console.WriteLine($" COMPOSITE SCORE       : {composite:F4}");
            Console.WriteLine("================================================================================================");

            return 0;
        }

        private static string GetTierLabel(BenchmarkDifficulty difficulty) => difficulty switch
        {
            BenchmarkDifficulty.Easy => "Tier 1: Easy (Syllable)",
            BenchmarkDifficulty.Medium => "Tier 2: Medium (Lexical)",
            BenchmarkDifficulty.Hard => "Tier 3: Hard (Boundary)",
            BenchmarkDifficulty.Hardest => "Tier 4: Hardest (Syntax)",
            BenchmarkDifficulty.Nightmare => "Tier 5: Nightmare (Deep Trap)",
            BenchmarkDifficulty.GrandmasterTorture => "Tier 6: Grandmaster (3-4x Err)",
            BenchmarkDifficulty.AbyssalMythic => "Tier 7: Abyssal Mythic (5-6x)",
            BenchmarkDifficulty.GovernmentB2G => "Tier 8: Gov Portal B2G (.gov.mm)",
            BenchmarkDifficulty.Top1Championship => "Tier 9: Top-1 & Speed (--top1)",
            BenchmarkDifficulty.StandardHardest => "Tier 10: Hardest Standard (MLC)",
            BenchmarkDifficulty.HybridStatistical => "Tier 11: Hybrid Stat (--hybrid)",
            BenchmarkDifficulty.NationalApex => "Tier 12: National Apex (--apex)",
            BenchmarkDifficulty.GovLivePrecision => "Tier 13: Gov Live (--gov-live)",
            BenchmarkDifficulty.CataclysmicNightmare => "Tier 14: Cataclysmic (--cataclysm)",
            BenchmarkDifficulty.DataMinedMastery => "Tier 15: Data-Mined (--mined)",
            _ => "Tier 0: Clean Controls"
        };

        private static void PrintTierRow(string label, TierAccumulator acc)
        {
            double rec = acc.GoldSpans > 0 ? (double)acc.Tp / acc.GoldSpans * 100.0 : 100.0;
            double top1 = acc.EvalSugCount > 0 ? (double)acc.Top1Hits / acc.EvalSugCount * 100.0 : 0.0;
            double mrr = acc.EvalSugCount > 0 ? acc.MrrSum / acc.EvalSugCount : 0.0;
            double p95 = 0.0;
            if (acc.LatenciesMs.Count > 0)
            {
                var sorted = acc.LatenciesMs.OrderBy(x => x).ToList();
                p95 = sorted[Math.Min(sorted.Count - 1, (int)(0.95 * sorted.Count))];
            }
            Console.WriteLine($" {label,-32} | {acc.Sentences,5} | {acc.GoldSpans,5} | {acc.Tp,3} | {acc.Fn,3} | {rec,6:F1}% | {top1,6:F1}% | {mrr,6:F4} | {p95,6:F2}");
        }

        private static List<BenchmarkCase> BuildCleanRoomProgressiveSuite()
        {
            return new List<BenchmarkCase>
            {
                // =========================================================================================
                // TIER 0: ADVERSARIAL "EVIL-TWIN" CLEAN CONTROLS (30 Clean Sentences — Must Have 0 FPs!)
                // =========================================================================================
                new BenchmarkCase("ministry", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုတရားလွှတ်တော်ချုပ်သည် စာချွန်တော်အမိန့်လျှောက်ထားမှုများကို ဥပဒေနှင့်အညီ စီရင်ဆုံးဖြတ်သည်။"),
                new BenchmarkCase("ministry", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "သောက်ရေနှင့် မြစ်ရေ သန့်ရှင်းရေးအတွက် စနစ်တကျ စစ်ဆေးဆောင်ရွက်လျက်ရှိသည်။"),
                new BenchmarkCase("ministry", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အစိုးရ အဖွဲ့အသီးသီးနှင့် ညှိနှိုင်း၍ ဒေသဖွံ့ဖြိုးရေးလုပ်ငန်းများကို ဆောင်ရွက်သည်။"),
                new BenchmarkCase("literary", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "“ယခု အမိန့်တော်သည် သင့်ပေ၏။ “လိုက်၍ ပြချေ” ဆိုလျှင် ကျွန်ုပ် သွားပါအံ့” ဟု လျှောက်တင်၏။"),
                new BenchmarkCase("literary", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အိမ်တွင် ဘုရားစင် မရှိသဖြင့် ထောင့်ကျသော နေရာတွင် နံရံကို မျက်နှာပြု၍ ထားရှိသည်။"),
                new BenchmarkCase("classical", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ယောက်ျားပီသစွာဖြင့် ကျွန်ုပ်တို့နှင့်အတူ လက်ယာဘက်သို့ လျှောက်လှမ်းသည်။"),
                new BenchmarkCase("technical", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဆော့ဖ်ဝဲလ်အင်ဂျင်နီယာများသည် ဆာဗာလော့ဂ်များကို ပုံမှန်စစ်ဆေးပြုပြင်ကြသည်။"),
                new BenchmarkCase("geology", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဘူမိဗေဒ ကွင်းဆင်းလေ့လာရေးအဖွဲ့သည် ကျောက်လွှာနမူနာများကို ဓာတ်ခွဲစမ်းသပ်သည်။"),
                new BenchmarkCase("geographic", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "နောက်ရက်တွင် လွိုင်နမ်းဖ ကျေးရွာသို့ ရောက်ရှိခဲ့ပါတယ်။"),
                new BenchmarkCase("conversational", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ကလေးများ ဘောလုံး ကစားကြပြီး ဇာတ်သဘင်အဖွဲ့က ကပြဖျော်ဖြေတယ်။"),
                new BenchmarkCase("conversational", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ရေဘူးနဲ့ ထမင်းဘူး ယူလာပေမဲ့ မအားသေးလို့ မစားရသေးဘူး။"),
                new BenchmarkCase("conversational", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ဒီသင်တန်းကို ဆရာမကြီးပဲ ကိုယ်တိုင် ဦးဆောင်သင်ကြားပေးမှာ ဖြစ်ပါတယ်။"),
                new BenchmarkCase("conversational", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ဘာလို့လဲဆိုတော့ သူကိုယ်တိုင်လည်း အဲဒီအစည်းအဝေးကို တက်ရောက်ချင်လို့ပါ။"),
                new BenchmarkCase("conversational", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "တစ်စုံတစ်ယောက်ကို မမေးမြန်းပါဘဲ ဆုံးဖြတ်ချက် မချသင့်ပါဘူး။"),
                new BenchmarkCase("conversational", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ပထမတန်းပဲ တက်ရသေးပေမဲ့ စာဖတ်တာ အရမ်းသွက်လက်တယ်။"),
                new BenchmarkCase("agriculture", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လယ်သမားများသည် နွားဘို့နှင့် ထမ်းပိုးကို သေချာစွာ စစ်ဆေးကြသည်။"),
                new BenchmarkCase("household", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မီးဖိုက မီးခိုးတွေ ထွက်လာလို့ ပြတင်းပေါက် ဖွင့်ထားလိုက်တယ်။"),
                new BenchmarkCase("medical", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပါမောက္ခဆရာဝန်ကြီးသည် ပါကင်ဆန်ရောဂါ ကုသရေးစာတမ်းကို ဖတ်ကြားသည်။"),
                new BenchmarkCase("education", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဘာသာဗေဒနှင့် သဒ္ဒါသင်ခန်းစာများကို တက္ကသိုလ်တွင် စနစ်တကျ သင်ကြားပို့ချသည်။"),
                new BenchmarkCase("commerce", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပို့ကုန်သွင်းကုန် လုပ်ငန်းရှင်များသည် အခွန်စည်းမျဉ်းများကို အတိအကျ လိုက်နာကြသည်။"),
                // 10 Evil-Twin Minimal-Pair Clean Controls for Nightmare & Grandmaster Rules:
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "တပ်ဖွဲ့ဝင်များသည် သေနတ်ပစ်မှု လေ့ကျင့်ရေးကွင်း၌ စနစ်တကျ လေ့ကျင့်ကြသည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပျက်စီးသွားသော ပစ္စည်းတန်ဖိုးကို အပြည့်အဝ ပြန်လည်လျော်ပေးရမည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကလေးများ လက်လှမ်းမမီသော နေရာတွင် ဆေးဝါးများကို သိမ်းဆည်းပါ။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မနက်ဖြန် ရုံးကို စာရွက်စာတမ်းတွေ ယူလာခဲ့မယ် လို့ ပြောသွားတယ်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဆရာတော်ကြီးက တရားဓမ္မ ဟောကြားတော်မူပြီး ပရိသတ်များက သာဓုခေါ်ဆိုကြသည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "နူးညံ့ပျော့ပျောင်းသော ပိုးထည်များကို သေတ္တာထဲတွင် စနစ်တကျ ထည့်ထားသည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဝင်ငွေနှင့် အထွက်စာရင်းများကို လချုပ်အစီရင်ခံစာတွင် တိကျစွာ ဖော်ပြထားသည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ရဲတပ်ဖွဲ့ စခန်းမှူးက ယာဉ်စည်းကမ်း လိုက်နာရေးအတွက် အသိပညာပေး ဟောပြောသည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ဗိုက်ဆာလို့ အားမရှိသလိုပဲ ခံစားနေရပေမဲ့ နှမ်းဆီမွှေးမွှေးလေးနဲ့ စားလိုက်တယ်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကမ်းခြေထီးတစ်ထည်နှင့် အယ်လ်ဂိုရီသမ်စာအုပ်တစ်အုပ်ကို သက်သောင့်သက်သာ ယူဆောင်လာသည်။"),
                new BenchmarkCase("pitaka_nissaya", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ရဟန်းတို့ သူတပါးအကျိုးငှါ တစုံတခုသော ကောင်းမှုကို ပြုရာ၏ဟုဆိုကုန်၏။"),
                new BenchmarkCase("classical_euphonic", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မှန်ကန်သော လုံ့လဝီရိယသည် ကြီးပွားချမ်းသာခြင်း၏ အကြောင်းရင်းပေတည်း။"),
                new BenchmarkCase("foreign_coda", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အစည်းအဝေးသို့ မစ္စတာ ရောဘတ်စ် နှင့် ကိုယ်စားလှယ်အဖွဲ့ တက်ရောက်ကြသည်။"),
                new BenchmarkCase("evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကွင်းဆင်းစစ်ဆေးချက်အရ သက်သေအထောက်အထား အပြည့်အစုံ တွေ့ရှိရကြောင်း တင်ပြသည်။"),
                // 12 Adversarial Mythic Evil-Twin Clean Controls for Tier 7 Abyssal Rules:
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ထင်းမီးဖိုက မီးခိုးများ ထွက်လာသော်လည်း အိုးထဲမှ ဟင်းရည်မှာ မွှေးပျံ့နေသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အချင်းချင်း ရိုင်းပင်းကူညီခြင်းက လူမှုအသိုင်းအဝိုင်းကို စည်းလုံးစေသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "တောင်ခြေရှိ ကျောက်နေရာကို ဘူမိဗေဒပညာရှင်များက အသေးစိတ် စစ်ဆေးကြသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ရိုးရာ ခေါက်ဆွဲပြုလုပ်ပုံ အဆင့်ဆင့်ကို သင်တန်းတွင် လက်တွေ့ပြသပေးသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မြို့အထွက် လမ်းဆုံတွင် ယာဉ်ထိန်းရဲတပ်ဖွဲ့က လုံခြုံရေး စစ်ဆေးလျက်ရှိသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ငယ်သူငယ်ချင်းတွေ့ရတာ ဝမ်းသာလွန်းလို့ စကားတွေ အကြာကြီး ပြောဖြစ်ကြတယ်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ကျောင်းရှေ့က ကံ့ကော်ပင်ကြီးတွေက နွေဦးရာသီမှာ ပန်းရနံ့ မွှေးပျံ့စေတယ်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မင်္ဂလာပါ ခင်ဗျားတို့အားလုံး ဒီနေ့အစည်းအဝေးကို တက်ရောက်ပေးကြလို့ ကျေးဇူးတင်ပါတယ်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အိမ်မွေးခွေးလေး၏ လည်ပတ်ကို သန့်ရှင်းသော ရေဖြင့် ဆေးကြောပေးထားသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လမ်းခွနေရာတွင် လမ်းညွှန်ဆိုင်းဘုတ် အသစ်တစ်ခု စိုက်ထူထားပါသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အားကစားကွင်းအတွင်း လူငယ်များ တက်ကြွစွာ လေ့ကျင့်နေကြသည်ကို တွေ့မြင်ရသည်။"),
                new BenchmarkCase("mythic_evil_twin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "သီချင်းသံစဉ်များနှင့် ရိုးရာအကများကို ပွဲတော်ညတွင် ပျော်ရွှင်စွာ ကြည့်ရှုကြသည်။"),
                // 14 Live .gov.mm B2G Portal & Gazette Clean Controls (MOEZALA, YCDC, Commerce, SECM, MRTV, MOI, MSME, MOEA):
                new BenchmarkCase("gov_moezala", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အိန္ဒိယနိုင်ငံ၊ ချင်းနိုင်မြို့ (Chennai) ၏ အရှေ့တောင်ဘက် ရေမိုင် ၂၄၀ ခန့်အကွာ၌ မုန်တိုင်းငယ် ဗဟိုပြုနေသည်။"),
                new BenchmarkCase("gov_ycdc", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ခရိုင်ကော်လိတ္တော်အရာရှိက မြေငှားဂရန် လျှောက်ထားမှုများကို ဥပဒေနှင့်အညီ စိစစ်ဆောင်ရွက်ပေးသည်။"),
                new BenchmarkCase("gov_commerce", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "နယ်စပ်ကုန်သွယ်ရေးစခန်းတွင် ကားခ (ကုန်ကျစ်) နှင့် (ကုန်ပွ) နှုန်းထားများကို စနစ်တကျ သတ်မှတ်ထားသည်။"),
                new BenchmarkCase("gov_ycdc_vet", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဈေးများအတွင်း အသားငါးများ၌ ရေထိုးခြင်း၊ လေထိုးခြင်းနှင့် ဆိုးဆေးသုံးစွဲခြင်း မပြုလုပ်ရန် စစ်ဆေးသည်။"),
                new BenchmarkCase("gov_mrtv", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်သူများအားလုံး ကိုယ်စိတ်နှစ်ဖြာ ကျန်းမာချမ်းသာကြပါစေကြောင်း ဆုမွန်ကောင်း မေတ္တာ ပို့သအပ်ပါသည်။"),
                new BenchmarkCase("gov_mora_pali", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "စုဒ္ဒသမ တိပိဋကဓရ တိပိဋကကောဝိဒ ရွေးချယ်ရေးစာမေးပွဲကြီးကို နေပြည်တော်၌ ကျင်းပသည်။"),
                new BenchmarkCase("gov_mora_monastery", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "သဒ္ဓမ္မနန္ဒပရိယတ္တိစာသင်တိုက် ဆရာတော်ကြီးအား လှူဖွယ်ဝတ္ထုပစ္စည်းများ ဆက်ကပ်လှူဒါန်းသည်။"),
                new BenchmarkCase("gov_moi", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အခြေခံ သေနတ်ပစ်ခတ်ပုံကအစ စနစ်တကျ လေ့ကျင့်သင်ကြားပေးလျက်ရှိသည်။"),
                new BenchmarkCase("gov_ycdc_plaza", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကမ္ဘာအေးဘုရားလမ်းမပေါ်ရှိ မြန်မာပလာဇာရှေ့ ယာဉ်ကြောရှင်းလင်းရေး ဆောင်ရွက်ထားသည်။"),
                new BenchmarkCase("gov_secm_fintech", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဒီဂျစ်တယ်လ်တီထွင်ဆန်းသစ်မှုနှင့် အယ်ဂိုရီသမ် နည်းပညာဆိုင်ရာ ဆွေးနွေးပွဲသို့ တက်ရောက်သည်။"),
                new BenchmarkCase("gov_secm_teaser", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "(40th ACMF Capital Market Regulators Conference ... ဆက်ဖတ်ရန်"),
                new BenchmarkCase("gov_mminsurance_teaser", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "“(၂၃) ကြိမ်မြောက် အာရှနှင့် ပစိဖိတ်ဒေသဆိုင်ရာ အာမခံညီလာခံ ... ဆက်ဖတ်ရန်"),
                new BenchmarkCase("gov_msme_brand", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "“ဒေးဒေး” ကယားရိုးရာအထည်များ ထုတ်လုပ်ဖြန့်ဖြူးရေးလုပ်ငန်း ပြခန်းကို ကြည့်ရှုအားပေးသည်။"),
                new BenchmarkCase("gov_moea_ethnic", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုနေ့ အခမ်းအနားတွင် တိုင်းကြီး၊ တိုင်းနေ၊ တိုင်းလိ ရိုးရာယဉ်ကျေးမှုအဖွဲ့များက ကပြဖျော်ဖြေကြသည်။"),
                // 6 Adversarial V66 Top-1 Championship Evil-Twin Clean Controls:
                new BenchmarkCase("v66_clean_noun_mapabae", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လက်ငင်းငွေမပါဘဲ အရစ်ကျစနစ်ဖြင့် စနစ်တကျ ပေးချေနိုင်ပါသည်။"),
                new BenchmarkCase("v66_clean_inceptive_sa", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ကိုယ်ပိုင်စီးပွားရေးလုပ်ငန်းတစ်ခုစလုပ်ဖို့ သေချာစွာ ပြင်ဆင်နေပါတယ်။"),
                new BenchmarkCase("v66_clean_classical_htote", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဧည့်သည်တော်များအတွက် စားဖွယ်မုန့်တို့ကိုထုတ်၏။"),
                new BenchmarkCase("v66_clean_ethnic_jogging", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မဂမ်နှင့် မိသားစုဝင်များသည် နံနက်တိုင်း ပန်းခြံ၌ ဂျောဂင်းထွက်ကြသည်။"),
                new BenchmarkCase("v66_clean_quoted_kyama", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "“ကျမလည်း ဒီသင်တန်းကို တက်ရောက်ချင်ပါတယ်” ဟု ပြောကြားသည်။"),
                new BenchmarkCase("v66_clean_additive_lae", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ဘယ်လို ဖွင့်ပေးတာလည်း ဆိုတာတော့ သေချာမသိသေးပါဘူး။"),
                // 14 Adversarial V70 Hardest-Standard Evil-Twin Clean Controls (Simplex Intransitives, Reciprocal MLC Pairs, Relational Postpositions):
                new BenchmarkCase("v70_clean_simplex_pyetsi", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မုန်တိုင်းဒဏ်ကြောင့် ချောင်းကူးတံတားဟောင်း ပျက်စီးသွားခဲ့သည်။"),
                new BenchmarkCase("v70_clean_simplex_pyattauk", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လေပြင်းတိုက်ခတ်သဖြင့် လျှပ်စစ်မီးကြိုးများ ပြတ်တောက်သွားသည်။"),
                new BenchmarkCase("v70_clean_simplex_pyaukpyet", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "သဘာဝရာသီဥတုဒဏ်ကြောင့် ကျောက်စာအချို့ ပျောက်ပျက်လုနီးပါး ဖြစ်နေသည်။"),
                new BenchmarkCase("v70_clean_simplex_lyohnည်း", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဆောင်းရာသီ ရောက်ရှိလာသဖြင့် ညအပူချိန် တဖြည်းဖြည်း လျော့နည်းကျဆင်းလာသည်။"),
                new BenchmarkCase("v70_clean_resign_nokehtwet", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အမှုဆောင်အရာရှိချုပ်သည် မိမိသဘောဆန္ဒအလျောက် ရာထူးမှ နုတ်ထွက်သွားသည်။"),
                new BenchmarkCase("v70_clean_kwailwai_asintasaik", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကော်မတီဝင်များအကြား သဘောထား ကွဲလွဲမှု အနည်းငယ် ရှိသော်လည်း အစဉ်တစိုက် ညှိနှိုင်းကြသည်။"),
                new BenchmarkCase("v70_clean_simplex_nokyar", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "တိုင်းရင်းသားပြည်သူများသည် အမျိုးသားရေးတာဝန်ကို နိုးကြားတက်ကြွစွာ ထမ်းဆောင်ကြသည်။"),
                new BenchmarkCase("v70_clean_reciprocal_nhitsayar_nitna", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "စာပေဗိမာန်စာတမ်းဖတ်ပွဲ၏ အနှစ်သာရမှာ အနစ်နာခံ ဆောင်ရွက်တတ်သော စိတ်ဓာတ်ဖြစ်သည်။"),
                new BenchmarkCase("v70_clean_sayagyi_tathamat", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဆရာကြီးများ၏ လမ်းညွှန်မှုသည် ကျောင်းသားအသီးသီးကို တစ်သမတ်တည်း တိုးတက်စေသည်။"),
                new BenchmarkCase("v70_clean_letshi_htotepyan", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လက်ရှိတွင် သက်ဆိုင်ရာဦးစီးဌာနက ကနဦးစစ်ဆေးချက်အစီရင်ခံစာကို ထုတ်ပြန်ခဲ့သည်။"),
                new BenchmarkCase("v70_clean_parithat_sayadaw_pa", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပရိသတ်များက ဆရာတော် သုံးပါးအား ဝတ္ထုငွေနှင့် လှူဖွယ်ပစ္စည်းများ ဆက်ကပ်ကြသည်။"),
                new BenchmarkCase("v70_clean_limited_investment", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မြန်မာသီလဝါ အထူးစီးပွားရေးဇုန် အများပိုင်ကုမ္ပဏီလီမိတက်တွင် ရင်းနှီးမြှုပ်နှံကြသည်။"),
                new BenchmarkCase("v70_clean_neg_mahtwet", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "အလုပ်မသွားခင် မနက်စာ အရင်စားလေ့ရှိပြီး အပြင်မထွက်တော့ဘူး လို့ ပြောတယ်။"),
                new BenchmarkCase("v70_clean_relational_saingya_akyaung", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကျောင်းဆိုင်ရာ စည်းကမ်းချက်များနှင့် သင်တန်းအကြောင်း အသေးစိတ် ရှင်းလင်းပြောကြားသည်။"),
                // 10 Adversarial V71 Hybrid Evil-Twin Clean Controls (Quoted Spoken Dialogue, ColloquialPermissive Mode, Transitive Objects, Coat vs Code):
                new BenchmarkCase("v71_clean_quoted_dialogue_takhukhu", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ဆရာက “စိတ်ထဲရှိတာ တခုခု ပြောလိုက်။ ကျနော် နားထောင်ပေးမယ်” ဟု အားပေးစကား ပြောကြားသည်။"),
                new BenchmarkCase("v71_clean_permissive_casual_social", BenchmarkDifficulty.CleanControl, ProofingStyleMode.ColloquialPermissive,
                    "ကျနော် ဒီမနက် စျေးမှာ စာအုပ်တအုပ်နဲ့ ဆော့ဝဲလ်အသစ်တခု ဝယ်လာခဲ့တယ် ဒါပေမယ့် မစမ်းရသေးဘူး။"),
                new BenchmarkCase("v71_clean_permissive_classical_pali", BenchmarkDifficulty.CleanControl, ProofingStyleMode.ColloquialPermissive,
                    "ပဋိစ္စသမုပ္ပါဒ်နှင့် ဒွာရခြောက်ပါး တရားတော်ကို လေ့လာမှတ်သားပါတယ်။"),
                new BenchmarkCase("v71_clean_transitive_unmarked_object_apple", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ကလေးက ပန်းသီး အရမ်းကြိုက်တယ် ဆိုပြီး နေ့တိုင်း စားလေ့ရှိပါတယ်။"),
                new BenchmarkCase("v71_clean_transitive_unmarked_object_car", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "အစ်ကိုက ကား အမြန်ဆုံးမောင်းတယ် ဆိုပေမဲ့ စည်းကမ်းတကျ မောင်းနှင်ပါတယ်။"),
                new BenchmarkCase("v71_clean_temporal_nominal_apri", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မနက်စာ စား အပြီးမှာ ရုံးသို့ အချိန်မီ သွားရောက်ခဲ့ပါတယ်။"),
                new BenchmarkCase("v71_clean_coat_and_mogok", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မိုးကုတ်မြို့မှာ ရာသီဥတု အေးလို့ ကုတ်အင်္ကျီအထူ ဝတ်ဆင်ကြပါတယ်။"),
                new BenchmarkCase("v71_clean_bag_interior_htae", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "စာရွက်စာတမ်းတွေကို အိတ်တစ်ခုထဲမှာ သေချာစုစည်းပြီး သိမ်းဆည်းထားပါတယ်။"),
                new BenchmarkCase("v71_clean_polite_news_patae", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ဘီဘီစီမြန်မာပိုင်းသတင်းတွင် ပြေးစရာမြေမရှိသူများနှင့် ပိုးဝင်ရောဂါအကြောင်း ဖော်ပြထားပါတယ်။"),
                new BenchmarkCase("v71_clean_typing_sayaik", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မြန်မာယူနီကုဒ် လက်ကွက်ဖြင့် စာရိုက်လေ့ကျင့်ခြင်းသည် ရုံးလုပ်ငန်းအတွက် အသုံးဝင်သည်။"),
                // 16 Adversarial V75 National Apex Evil-Twin Clean Controls (Gazette vs Ad, Tax vs Discipline, Redup Idioms, Cyclones, Vinaya & University):
                new BenchmarkCase("v75_clean_gazette_amint_kyawnyasar", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုအစိုးရအဖွဲ့ရုံးက အမိန့်ကြော်ငြာစာ အမှတ် (၁၂/၂၀၂၆) ကို ထုတ်ပြန်ခဲ့သည်။"),
                new BenchmarkCase("v75_clean_commercial_ad_kyawnya", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ရုပ်မြင်သံကြား ကြော်ငြာဆိုင်းဘုတ်နှင့် ကုန်ပစ္စည်းကြော်ငြာများကို စနစ်တကျ စစ်ဆေးသည်။"),
                new BenchmarkCase("v75_clean_discipline_sikam_sikyat", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "တပ်ဖွဲ့ဝင်များအား စည်းကမ်းစည်းကျပ် စနစ်တကျ ရှိစေရန် ကြပ်မတ်ဆောင်ရွက်သည်။"),
                new BenchmarkCase("v75_clean_half_redup_malonetalone", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မပွင့်တပွင့် ပြောဆိုခြင်းနှင့် မလုံတလုံ ဝတ်စားဆင်ယင်ခြင်းကို ရှောင်ကြဉ်သင့်သည်။"),
                new BenchmarkCase("v75_clean_spatial_akhanwa_yauk", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကျောင်းသားများသည် အခန်းဝရောက်သောအခါ ဆရာကြီးအား ရိုသေစွာ နှုတ်ဆက်ကြသည်။"),
                new BenchmarkCase("v75_clean_named_cyclones_bijli_mocha", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မိုးလေဝသဌာနက ဘီဂျလီမုန်တိုင်းနှင့် မိုခါမုန်တိုင်း လမ်းကြောင်းကို စောင့်ကြည့်သည်။"),
                new BenchmarkCase("v75_clean_edo_era_hyphenated_foreign", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဂျပန်နိုင်ငံ အဲဒိုခေတ်နှင့် အဇုချိ-မိုမိုယမ ယဉ်ကျေးမှုလက်ရာများကို လေ့လာသည်။"),
                new BenchmarkCase("v75_clean_vinaya_nissaggi_pacittiya", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဝိနည်းဒေသနာတော်လာ နိဿဂ္ဂိယပါစိတ် အာပတ်ဆိုင်ရာ သိက္ခာပုဒ်များကို သင်ယူသည်။"),
                new BenchmarkCase("v75_clean_canonical_nrc_kyaukse", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "နိုင်ငံသားစိစစ်ရေးကတ်ပြားအမှတ် ၉/ကဆန(နိုင်)၁၂၃၄၅၆ ကို တိုက်ဆိုင်စစ်ဆေးသည်။"),
                new BenchmarkCase("v75_clean_kyaukse_univ_geology", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာန၌ အနည်ကျကျောက်နှင့် မီးသင့်ကျောက်များကို လေ့လာသည်။"),
                new BenchmarkCase("v75_clean_postal_code_naypyitaw", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "နေပြည်တော် ဥတ္တရသီရိမြို့နယ်၏ စာတိုက်သင်္ကေတကုဒ်မှာ ၁၅၀၁၁ ဖြစ်ပါသည်။"),
                new BenchmarkCase("v75_clean_judicial_tayarswaiso", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "တရားရုံးတွင် ဥပဒေနှင့်အညီ တရားစွဲဆို၍ စွဲချက်တင် စစ်ဆေးလျက်ရှိသည်။"),
                new BenchmarkCase("v75_clean_revenue_akhun_sikyat", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်တွင်းအခွန်များဦးစီးဌာနက ဝင်ငွေခွန် စည်းကြပ်ကောက်ခံမှုကို စိစစ်သည်။"),
                new BenchmarkCase("v75_clean_honors_myinttin_chimyint", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပညာရေးအရည်အသွေး အဆင့်မြှင့်တင်ရေးနှင့် ထူးချွန်ဆု ချီးမြှင့်ပွဲ ကျင်းပသည်။"),
                new BenchmarkCase("v75_clean_service_phyitsည်း_anhauk", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်သူ့လိုအပ်ချက်များကို အနှောင့်အယှက် ကင်းစွာ ဖြည့်ဆည်းပေးလျက်ရှိသည်။"),
                new BenchmarkCase("v75_clean_inle_shan_state_tashi", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အင်းလေးကန်သည် ရှမ်းပြည်နယ်တွင် တည်ရှိပြီး သဘာဝအလှဖြင့် ထင်ရှားသည်။"),
                // 10 Adversarial V76 Live *.gov.mm Clean Controls (Myanma Insurance, MSME, MOI, MRTV, Putao, Gazette Age-Name, Hukawng):
                new BenchmarkCase("v76_clean_mminsurance_short_term_premium", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မြန်မာ့အာမခံလုပ်ငန်းက နှစ်တိုပရီမီယံ တစ်ကြိမ်ပေး ချေးငွေအာမခံကို ဝန်ဆောင်မှုပေးလျက်ရှိသည်။"),
                new BenchmarkCase("v76_clean_msme_lokesa_kaingsa", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လုပ်ငန်းရှင်များ လုပ်သာ ကိုင်သာရှိသည့် စီးပွားရေးဝန်းကျင်ကောင်းများ ဖန်တီးပေးသည်။"),
                new BenchmarkCase("v76_clean_moi_samannaphala_sacred_month", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ထူးခြားသောလမြတ်တစ်ခုဖြစ်သည့် တန်ဆောင်မုန်းလပြည့် သာမညဖလအခါတော်နေ့၌ ကုသိုလ်ပြုကြသည်။"),
                new BenchmarkCase("v76_clean_msme_alam_thit", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပြည်တွင်းထုတ်ကုန်များအတွက် ဈေးကွက်နှင့် အလမ်းသစ်များနှင့်အတူ တိုးတက်လာသည်။"),
                new BenchmarkCase("v76_clean_kachin_hukawng_hall", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကချင်ပြည်နယ် ပုန်ခုန်းရပ်ကွက်၊ ဟူးကောင်းခန်းမ၌ ညှိနှိုင်းအစည်းအဝေး ကျင်းပပြုလုပ်သည်။"),
                new BenchmarkCase("v76_clean_gazette_resident_age_name", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကျောက်ချပ်ရွာနေ ကျော်လွင်(၆၄)နှစ်၏ နေအိမ်ကို သက်ဆိုင်ရာက စစ်ဆေးခဲ့သည်။"),
                new BenchmarkCase("v76_clean_mrtv_singer_esther", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဂုဏ်ပြုတေးသီချင်း တေးဆို … အက်စတာဒေါ့ချင်းဆုန် တေးရေး … မင်းချစ်သူ တို့က တင်ဆက်သည်။"),
                new BenchmarkCase("v76_clean_mrtv_classical_artist_list", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "တေးဆို - စန္ဒရားမောင်လှအေး၊ နေထွဏ်းဦး၊ ဖုန်းမြင့်တို့က သီဆိုဖျော်ဖြေကြသည်။"),
                new BenchmarkCase("v76_clean_mrtv_cast_list_kyawzawhein", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ရုပ်မြင်သံကြားဇာတ်လမ်း (မေသန်းနု၊ မင်းသူ၊ ကျော်ဇောဟိန်း၊ အိမ့်ကြာဖြူ၊ ဒါရိုက်တာ - မောင်မျိုးမင်း) ပြသမည်။"),
                new BenchmarkCase("v76_clean_putao_ethnic_village", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပူတာအိုမြို့နယ်၊ မာမဲ့ရှီးဒီကျေးရွာ၌ ဒေသဖွံ့ဖြိုးရေးလုပ်ငန်းများ ဆောင်ရွက်ပေးသည်။"),
                // 16 Adversarial V78 Cataclysmic Evil-Twin Clean Controls (Transitive Subject 'က', Positive 'တိုးတက်', Coat/Scratch 'ကုတ်', English 'A', Sonorant Ha-Hto, Quoted Speech):
                new BenchmarkCase("v78_clean_subject_ka_transitive_asate_apaing", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကျောင်းသားခေါင်းဆောင်က အစိတ်အပိုင်းတစ်ခုကို တာဝန်ယူဆောင်ရွက်သည်။"),
                new BenchmarkCase("v78_clean_positive_economy_tech_totak", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "စီးပွားရေးနှင့် နည်းပညာကဏ္ဍသည် နေ့စဉ်နှင့်အမျှ တိုးတက်လာပါသည်။"),
                new BenchmarkCase("v78_clean_winter_coat_and_scratch", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဆောင်းရာသီတွင် ကုတ်အင်္ကျီအထူ ဝတ်ဆင်ပြီး အရေပြားကို လက်သည်းဖြင့် မကုတ်ရပါ။"),
                new BenchmarkCase("v78_clean_english_grade_a_vitamin_a", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဗီတာမင် A နှင့် အဆင့် A ရရှိသူများစာရင်းကို ထုတ်ပြန်ကြေညာသည်။"),
                new BenchmarkCase("v78_clean_attributive_thaw_students", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ထူးချွန်ထက်မြက်သော ကျောင်းသားများသည် နိုင်ငံ့ဂုဏ်ဆောင်ကြသည်။"),
                new BenchmarkCase("v78_clean_beach_listen_waves", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ခရီးသွားများသည် ကမ်းခြေတွင် ပင်လယ်လှိုင်းသံကို နားထောင်ကြသည်။"),
                new BenchmarkCase("v78_clean_human_rights_akhwint_ayay", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "နိုင်ငံသားတိုင်း၏ အခြေခံအခွင့်အရေးများကို ဥပဒေဖြင့် ကာကွယ်စောင့်ရှောက်သည်။"),
                new BenchmarkCase("v78_clean_sonorant_hahto_myanmar_words", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "နှမ်းခင်းနှင့် မှိုခင်းများအနီးတွင် လှည်းလမ်းကြောင်း ဖောက်လုပ်ထားသည်။"),
                new BenchmarkCase("v78_clean_quoted_sov_colloquial_sopri", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "ညီလေးက “အစ်ကို အနားယူတယ် အိမ်မှာ ခဏ” ဆိုပြီး ပြုံး၍ ပြောပြသည်။"),
                new BenchmarkCase("v78_clean_simplex_intransitive_pyatsi_pyattauk", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မုန်တိုင်းကြောင့် တံတား ပျက်စီး၍ ဖုန်းလိုင်းများ ပြတ်တောက်သွားခဲ့သည်။"),
                new BenchmarkCase("v78_clean_causative_periphrastic_pyatsi_say", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ရေကြီးမှုက လမ်းတံတားများကို ပျက်စီးစေခဲ့သည်။"),
                new BenchmarkCase("v78_clean_labour_rights_law", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အလုပ်သမားအခွင့်အရေးဆိုင်ရာ ဥပဒေများကို စနစ်တကျ လိုက်နာရမည်။"),
                new BenchmarkCase("v78_clean_mimu_village_place_code_kud", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အဆိုပါကျေးရွာအုပ်စု၏ ရွာနေရာကုဒ်မှာ ၁၇၄၂၀၁ ဖြစ်ပါသည်။"),
                new BenchmarkCase("v78_clean_cultural_heritage_joined", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပုဂံဒေသရှိ ယဉ်ကျေးမှုအမွေအနှစ်များသည် အဖိုးမဖြတ်နိုင်သော ရတနာများ ဖြစ်သည်။"),
                new BenchmarkCase("v78_clean_payment_system_joined", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဗဟိုဘဏ်က ဒစ်ဂျစ်တယ် ငွေပေးချေမှုစနစ်ကို အဆင့်မြှင့်တင်သည်။"),
                new BenchmarkCase("v78_clean_grand_architecture_khamnar", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပုဂံခေတ်၏ ဗိသုကာလက်ရာများသည် ယနေ့တိုင် ခမ်းနားထည်ဝါလှပါသည်။"),
                // 12 Adversarial V80 Corpus Data-Mining Evil-Twin Clean Controls (Market 'ပျော့တက်', Classifier Modifier Suffixes, Mined Domain Terms, Correct Classifiers):
                new BenchmarkCase("v80_clean_market_price_pyawt_tet", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ဈေးကွက်တွင် ဈေးနှုန်း ပျော့တက်လာသည်ဟု သတင်းရင်းမြစ်များက ဆိုသည်။"),
                new BenchmarkCase("v80_clean_classifier_modifier_seat_and_room", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "အဆိုပါဟိုတယ်တွင် နှစ်ယောက်ခန်းများနှင့် ငါးယောက်စီး မော်တော်ယာဉ်များ ထားရှိသည်။"),
                new BenchmarkCase("v80_clean_personal_grooming_alhapyin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "မင်္ဂလာပွဲသွားဖို့ အစ်မက မှန်ရှေ့မှာ အလှပြင်နေပါတယ်။"),
                new BenchmarkCase("v80_clean_mined_fish_and_plant_classifiers", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "မြစ်အတွင်းမှ ကကတစ် နှစ်ကောင် ဖမ်းမိပြီး ကင်ပွန်း သုံးပင် စိုက်ပျိုးထားသည်။"),
                new BenchmarkCase("v80_clean_mined_garment_and_captain_classifiers", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကက်ပတိန် နှစ်ဦးသည် ဝတ်စုံ သုံးထည်နှင့် ကင်မရာများကို စစ်ဆေးသည်။"),
                new BenchmarkCase("v80_clean_geology_sedimentary_and_metamorphic", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ကျောက်ဆည်တက္ကသိုလ် ဘူမိဗေဒဌာနတွင် အနည်ကျကျောက်နှင့် အသွင်ပြောင်းကျောက် နမူနာများကို လေ့လာသည်။"),
                new BenchmarkCase("v80_clean_comfortable_living_thetsauntthetha", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "သက်သောင့်သက်သာ နေထိုင်လုပ်ကိုင်နိုင်သော ပတ်ဝန်းကျင်ကောင်းများ ဖန်တီးပေးသည်။"),
                new BenchmarkCase("v80_clean_plan_formulation_chamat", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "စီးပွားရေးဖွံ့ဖြိုးတိုးတက်ရန် ရေရှည်စီမံကိန်းများ ချမှတ်ဆောင်ရွက်ရမည်။"),
                new BenchmarkCase("v80_clean_unity_building_taung_sauk", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "လူမှုအသိုင်းအဝိုင်းအတွင်း စည်းလုံးညီညွတ်မှုကို တည်ဆောက်ကြပါသည်။"),
                new BenchmarkCase("v80_clean_mental_health_awareness", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "စိတ်ကျန်းမာရေးဆိုတာ လူတိုင်းအတွက် အရေးကြီးပါတယ်။"),
                new BenchmarkCase("v80_clean_interior_home_decoration_alhasin", BenchmarkDifficulty.CleanControl, ProofingStyleMode.FormalOfficial,
                    "ပန်းပုလက်ရာများသည် အိမ်အလှဆင်ရန်အတွက် အသုံးဝင်သည်။"),
                new BenchmarkCase("v80_clean_nominalized_observation_twera", BenchmarkDifficulty.CleanControl, ProofingStyleMode.GeneralStandard,
                    "လုပ်ငန်းခွင်မှာ နည်းပညာအသစ်တွေ အစားထိုးလာတာ တွေ့ရတယ်။"),

                // =========================================================================================
                // TIER 1: EASY — Character, Encoding, Punctuation & UTN #11 Syllable Fundamentals (20 Cases)
                // =========================================================================================
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "ကူညီပေးတဲ့အတွက် ကေျးဇူးတင်ပါတယ်။", "ကေျး", "ကျေး"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "ကြိုးပုြတ်သွားလို့ ပြန်ချည်ထားတယ်။", "ပုြတ်", "ပြုတ်"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "စကားပောြဆိုရာမှာ ယဉ်ကျေးသင့်တယ်။", "ပောြ", "ပြော"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "စည်းလုံးညီည်ွတ်စွာ ဆောင်ရွက်ကြသည်။", "ည်ွတ်", "ညွတ်"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "ကမ္ဘာ်ကြီး ငြိမ်းချမ်းသာယာစေရန် ဆုတောင်းသည်။", "ကမ္ဘာ်", "ကမ္ဘာ"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "ပစ္စည်းတွေကို သေချာသိမ်္းထားပြီးပြီ။", "သိမ်္း", "သိမ်း"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "အလုပ်လုပ်ရာတွင် စတ္ိထားရမယ်။", "စတ္ိ", "စိတ်"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "မုန့်ဟငိ်းခါး စားချင်တယ်။", "ဟငိ်း", "ဟင်း"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "အဓိပ္ပာယ္ရှိသော လုပ်ငန်းဖြစ်သည်။", "ယ္", "ယ်"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "စာအုပ် ိကို ယူလာပေးပါ။", "ိကို", "ကို"),
                new BenchmarkCase("syllable", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "သီချင်းနားထောင် ရင်းိ စာဖတ်တယ်။", "ရင်းိ", "ရင်း"),
                new BenchmarkCase("structural", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "လာမ ည့် အပတ်တွင် အစည်းအဝေး ကျင်းပမည်။", "လာမ ည့်", "လာမည့်"),
                new BenchmarkCase("structural", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "အပြုအမူပို င်းဆိုင်ရာ လေ့လာချက်ဖြစ်သည်။", "ပို င်း", "ပိုင်း"),
                new BenchmarkCase("structural", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "စစ်တက္ ကသိုလ်တွင် သင်တန်းတက်ရောက်သည်။", "စစ်တက္ ကသိုလ်", "စစ်တက္ကသိုလ်"),
                new BenchmarkCase("structural", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "သူငယ်ချင်းအသစ်ရှာတာ ပျော်စရာကောင်း့တယ်။", "ကောင်း့", "ကောင်း"),
                new BenchmarkCase("structural", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "အရေးကြိးချက်များကို စနစ်တကျ တင်ပြထားပါသည်။", "ကြိး", "ကြီး"),
                new BenchmarkCase("structural", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "သက်တမး တိုးမြှင့်ပေးရန် လျှောက်ထားသည်။", "တမး", "တမ်း"),
                new BenchmarkCase("encoding", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "မှတ်ပုံ\u200Bတင် စာရင်းကို စစ်ဆေးသည်။", "မှတ်ပုံ\u200Bတင်", "မှတ်ပုံတင်"),
                new BenchmarkCase("encoding", BenchmarkDifficulty.Easy, ProofingStyleMode.FormalOfficial,
                    "ကျောင်းသားများ Aားကစား ကွင်း၌ လေ့ကျင့်ကြသည်။", "Aား", "အား"),
                new BenchmarkCase("punctuation", BenchmarkDifficulty.Easy, ProofingStyleMode.GeneralStandard,
                    "လုံခြုံစိတ်ချရတဲ့ ငွေပေးချေမှု စနစ်တွေ ရှိပါတယ်.", ".", "။"),

                // =========================================================================================
                // TIER 2: MEDIUM — Phonetics, Oral-Stop Codas, Pali Consonants & Hidden Compounds (20 Cases)
                // =========================================================================================
                new BenchmarkCase("compound", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "ပညာရေ ဖွံ့ဖြိုးတိုးတက်မှုအတွက် ဆောင်ရွက်သည်။", "ပညာရေ", "ပညာရေး"),
                new BenchmarkCase("compound", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "သတင်းအချက်အလက များကို စိစစ်သည်။", "အချက်အလက", "အချက်အလက်"),
                new BenchmarkCase("coda", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "သတင်း အချက်အလတ် စုဆောင်းတင်ပြသည်။", "အချက်အလတ်", "အချက်အလက်"),
                new BenchmarkCase("coda", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "ယာဉ်မတော်တဆမှုကြောင့် ထိခိုပ်ဒဏ်ရာရရှိခဲ့သည်။", "ထိခိုပ်", "ထိခိုက်"),
                new BenchmarkCase("coda", BenchmarkDifficulty.Medium, ProofingStyleMode.GeneralStandard,
                    "နံရံကို သုပ်ဆေး အသစ် ပြန်သုတ်ထားတယ်။", "သုပ်ဆေး", "သုတ်ဆေး"),
                new BenchmarkCase("pali", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "စမ်းသပ်ချက် ရလဓ် ကောင်းမွန်ပါသည်။", "ရလဓ်", "ရလဒ်"),
                new BenchmarkCase("pali", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "ကျောင်းတွင် ဃီတပွဲ ကျင်းပသည်။", "ဃီတ", "ဂီတ"),
                new BenchmarkCase("dropped_a", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "စီမံကိန်းကို အကောင်ထည်ဖော် ဆောင်ရွက်သည်။", "အကောင်ထည်ဖော်", "အကောင်အထည်ဖော်"),
                new BenchmarkCase("nominalizer", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "အဖွဲ့အစည်းအကြား ပြေလည်မူရရှိခဲ့သည်။", "ပြေလည်မူ", "ပြေလည်မှု"),
                new BenchmarkCase("nominalizer", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "ဒစ်ဂျစ်တယ် ငွေပေးချေမူ စနစ်ကို အသုံးပြုသည်။", "ငွေပေးချေမူ", "ငွေပေးချေမှု"),
                new BenchmarkCase("nominalizer", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "ဆရာများ၏ သင်ကြားပို့ချမူကို ကျောင်းသားများ နှစ်သက်သည်။", "သင်ကြားပို့ချမူ", "သင်ကြားပို့ချမှု"),
                new BenchmarkCase("orthography", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "လုပ်ငန်းများကို ဆောင်ရွက်လျှက်ရှိသည်။", "လျှက်", "လျက်"),
                new BenchmarkCase("numeral", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "နိုင်ငံတဝန်းရှိ ကျောင်းများ ပြန်လည်ဖွင့်လှစ်သည်။", "တဝန်း", "တစ်ဝန်း"),
                new BenchmarkCase("numeral", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "၂၀၂၆ ခုနစ် ဘဏ္ဍာရေးနှစ် စာရင်းကို စစ်ဆေးသည်။", "ခုနစ်", "ခုနှစ်"),
                new BenchmarkCase("ideophone", BenchmarkDifficulty.Medium, ProofingStyleMode.GeneralStandard,
                    "လျှပ်စီး ဝင်းကနဲ လက်သွားတယ်။", "ဝင်းကနဲ", "ဝင်းခနဲ"),
                new BenchmarkCase("colloquial", BenchmarkDifficulty.Medium, ProofingStyleMode.GeneralStandard,
                    "ဒီသတင်းက အမှန်ပဲ ဟုလား။", "ဟုလား", "ဟုတ်လား"),
                new BenchmarkCase("colloquial", BenchmarkDifficulty.Medium, ProofingStyleMode.GeneralStandard,
                    "ဒါက လုံးဝ မဟုဘူး လို့ ပြောချင်တယ်။", "မဟုဘူး", "မဟုတ်ဘူး"),
                new BenchmarkCase("colloquial", BenchmarkDifficulty.Medium, ProofingStyleMode.GeneralStandard,
                    "အိမ်စာတွေ အားလုံး ရေးပြီးပါပီ။", "ပါပီ", "ပါပြီ"),
                new BenchmarkCase("idiom", BenchmarkDifficulty.Medium, ProofingStyleMode.GeneralStandard,
                    "ခရီးသည်များ သက်တောင့်သက်သာ နားနေနိုင်သည်။", "သက်တောင့်သက်သာ", "သက်သောင့်သက်သာ"),
                new BenchmarkCase("idiom", BenchmarkDifficulty.Medium, ProofingStyleMode.FormalOfficial,
                    "မှတ်တမ်းများကို စနစ်တကွ ထိန်းသိမ်းထားသည်။", "စနစ်တကွ", "စနစ်တကျ"),

                // =========================================================================================
                // TIER 3: HARD — Space-Split Compounds, Particle Polarity/Aspect & Mode Boundaries (20 Cases)
                // =========================================================================================
                new BenchmarkCase("broken_compound", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "သတင်းအချက် အလက်များကို စနစ်တကျ စုဆောင်းသည်။", "အချက် အလက်", "အချက်အလက်"),
                new BenchmarkCase("broken_compound", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "စာ ကြည့် တိုက် တွင် စာအုပ်များ ဖတ်ရှုသည်။", "စာ ကြည့် တိုက်", "စာကြည့်တိုက်"),
                new BenchmarkCase("broken_compound", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "ဘာသာ ရေး အခမ်းအနားသို့ တက်ရောက်သည်။", "ဘာသာ ရေး", "ဘာသာရေး"),
                new BenchmarkCase("broken_compound", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "မြို့နယ် အုပ် ချုပ်ရေးမှူး က ရှင်းလင်းပြောကြားသည်။", "အုပ် ချုပ်ရေးမှူး", "အုပ်ချုပ်ရေးမှူး"),
                new BenchmarkCase("bound_suffix", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "ဆရာတော်ကြီးက တရားဟောကြား တော်မူ၍ ပရိသတ်များ နာယူကြသည်။", "ဟောကြား တော်မူ", "ဟောကြားတော်မူ"),
                new BenchmarkCase("bound_suffix", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "အချိန်မီ ကြိုးစားပေ မယ့် မမီလိုက်ဘူး။", "ကြိုးစားပေ မယ့်", "ကြိုးစားပေမယ့်"),
                new BenchmarkCase("bound_suffix", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "မြန်မာနိုင်ငံ မြောက် ပိုင်းရှိ တောင်တန်းများသည် လှပသည်။", "မြောက် ပိုင်း", "မြောက်ပိုင်း"),
                new BenchmarkCase("boundary", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "ကုမ္ပဏီမာ အလုပ်လုပ်ပါသည်။", "မာ", "တွင်"),
                new BenchmarkCase("boundary", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "ကုမ္ပဏီမာ အလုပ်လုပ်ပါတယ်။", "မာ", "မှာ"),
                new BenchmarkCase("polarity", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "ဘာမှ မစဉ်းစားပဲ ဆုံးဖြတ်လိုက်တယ်။", "ပဲ", "ဘဲ"),
                new BenchmarkCase("polarity", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "ပြည်သူများအပေါ် အမြတ်မထုတ်ပဲ ရောင်းချပေးသည်။", "ပဲ", "ဘဲ"),
                new BenchmarkCase("polarity", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "သူပြောသလိုဘဲ လိုက်လုပ်ကြည့်မယ်။", "ဘဲ", "ပဲ"),
                new BenchmarkCase("polarity", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "သူက မနက်ဖြန် ရောက်လာမှာ ဖြစ်ပါဘဲ။", "ဘဲ", "ပဲ"),
                new BenchmarkCase("particle", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "အခု ဘယ်သွားမလို့လည်း။", "လည်း", "လဲ"),
                new BenchmarkCase("particle", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "အနာဂတ်မှာလဲ ဆက်လက်ကြိုးစားသွားမယ်။", "လဲ", "လည်း"),
                new BenchmarkCase("aspect", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "ညစာ စားပြီ အိမ်ပြန်နားမယ်။", "ပြီ", "ပြီး"),
                new BenchmarkCase("aspect", BenchmarkDifficulty.Hard, ProofingStyleMode.FormalOfficial,
                    "လူနာသည် ဆေးရုံမှ ဆင်းခွင့်ရပြီး။", "ပြီး", "ပြီ"),
                new BenchmarkCase("purposive", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "အောင်မြင်ဘို့ အတွက် အစွမ်းကုန် ကြိုးစားရမယ်။", "ဘို့", "ဖို့"),
                new BenchmarkCase("purposive", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "သဘာဝပတ်ဝန်းကျင် ကာကွယ်ဖိုက အားလုံးရဲ့ တာဝန်ပါ။", "ဖို", "ဖို့"),
                new BenchmarkCase("gerund", BenchmarkDifficulty.Hard, ProofingStyleMode.GeneralStandard,
                    "နေ့စဉ် လမ်းလျှောက်ချင်းက ကျန်းမာရေးအတွက် ကောင်းတယ်။", "ချင်း", "ခြင်း"),

                // =========================================================================================
                // TIER 4: HARDEST — Real-Word Collocations, Tense-Aspect & SOV Word Order (20 Cases)
                // =========================================================================================
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "ခွေးကို အစာ ကြွေး ပါ။", "ကြွေး", "ကျွေး"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "ဒီသတင်းကို ကျား ရ တယ်။", "ကျား", "ကြား"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "မနက်တိုင်း အဝတ် လျော် ပါတယ်။", "လျော်", "လျှော်"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "နေပူထဲ အဝတ် လှမ်း ထားတယ်။", "လှမ်း", "လှန်း"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "ကျန်းမာရေးကို အထူး ဂရုဆိုက် သင့်တယ်။", "ဂရုဆိုက်", "ဂရုစိုက်"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "မိဘများက သားသမီးများကို သွန်သင်ဆုံမ ကြသည်။", "သွန်သင်ဆုံမ", "သွန်သင်ဆုံးမ"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "တရားခံသည် ပြင်းထန်သော ပစ်မှု ကျူးလွန်ခဲ့သည်။", "ပစ်မှု", "ပြစ်မှု"),
                new BenchmarkCase("real_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "နိုင်ငံတော် ဖွံ့ဖြိုးတိုးထက် ရေးအတွက် ဆောင်ရွက်သည်။", "ဖွံ့ဖြိုးတိုးထက်", "ဖွံ့ဖြိုးတိုးတက်"),
                new BenchmarkCase("merged_word", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "ထမင်းကို သူကစားသည်။", "ကစား", "က စား"),
                new BenchmarkCase("negation", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "နေကောင်းဘူး", "နေကောင်းဘူး", "နေမကောင်းပါ"),
                new BenchmarkCase("negation", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "မအားနေဖူး", "မအားနေဖူး", "မအားလပ်ပါ"),
                new BenchmarkCase("classifier", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "ဖိနပ် တစ်ခု ဝယ်လာတယ်။", "ခု", "ရံ"),
                new BenchmarkCase("morphosyntax", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "ပြဿနာကို ထိရောက်သော ဖြေရှင်းနိုင်ခဲ့သည်။", "သော", "စွာ"),
                new BenchmarkCase("case_frame", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "သူသည် မန္တလေးမြို့ကို နေထိုင်သည်။", "ကို", "တွင်"),
                new BenchmarkCase("aspect_frame", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "ဝန်ကြီးဌာနသည် စီမံချက်ကို ပြင်ဆင်ကာ ရှိသည်။", "ကာ", "လျက်"),
                new BenchmarkCase("tense", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "လာမည့်နှစ်တွင် အဆောက်အအုံသစ်ကို ဖွင့်လှစ်ခဲ့သည်။", "ခဲ့သည်", "မည်ဖြစ်သည်"),
                new BenchmarkCase("tense", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "ယမန်နှစ်က ညီလာခံကြီးကို အောင်မြင်စွာ ကျင်းပမည်ဖြစ်သည်။", "မည်ဖြစ်သည်", "ခဲ့သည်"),
                new BenchmarkCase("tense", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "ကလေးက အခန်းထဲမှာ စာရေးခဲ့နေပြီ။", "ခဲ့နေပြီ", "နေတယ်"),
                new BenchmarkCase("word_order", BenchmarkDifficulty.Hardest, ProofingStyleMode.FormalOfficial,
                    "ဝန်ကြီးဌာနက တင်သွင်းခဲ့သည် အစီရင်ခံစာသစ်ကို လွှတ်တော်သို့ ဟု ထုတ်ပြန်သည်။",
                    "တင်သွင်းခဲ့သည် အစီရင်ခံစာသစ်ကို လွှတ်တော်သို့",
                    "အစီရင်ခံစာသစ်ကို လွှတ်တော်သို့ တင်သွင်းခဲ့သည်"),
                new BenchmarkCase("word_order", BenchmarkDifficulty.Hardest, ProofingStyleMode.GeneralStandard,
                    "ကျောင်းသားများက လေ့လာချင်ကြတယ် နည်းပညာဘာသာရပ်သစ်ကို။",
                    "လေ့လာချင်ကြတယ် နည်းပညာဘာသာရပ်သစ်ကို",
                    "နည်းပညာဘာသာရပ်သစ်ကို လေ့လာချင်ကြတယ်"),

                // =========================================================================================
                // TIER 5: NIGHTMARE — Deep Unspaced Homophones, Subtle Tone Traps & Case Collisions (20 Cases)
                // =========================================================================================
                new BenchmarkCase("speech_homophone", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "ရပ်ကွက်အတွင်း ဆူညံမှု မပြုလုပ်ရန် တာဝန်ရှိသူများက ဟု ပြော့သည်။", "ပြော့", "ပြော"),
                new BenchmarkCase("plural_homophone", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "စေတနာ့ဝန်ထမ်းတွေက သက်ကြီးဘိုးဘွားတွေကို နွေးထွေးစွာ စောင့်ရှောက်ပေးကြာပါတယ်။", "ကြာ", "ကြ"),
                new BenchmarkCase("verbal_particle", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "ကျန်းမာရေးဝန်ထမ်းတွေက လူနာတွေကို စိတ်ရှည်လက်ရှည် ကုသပေးကြပတ်တယ်။", "ပတ်", "ပါ"),
                new BenchmarkCase("modal_particle", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "ဒီသင်တန်းမှာ ကွန်ပျူတာအခြေခံပညာကို အခမဲ့ လေ့လာလို့ရာတယ်။", "ရာ", "ရ"),
                new BenchmarkCase("postposition_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "သစ်ပင်စိုက်ပျိုးခြင်းသည် ကမ္ဘာကြီးအထွက် အလွန်အကျိုးရှိပါသည်။", "အထွက်", "အတွက်"),
                new BenchmarkCase("possessive_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "ကုမ္ပဏီတစ်ခုရဲ အောင်မြင်မှုဟာ ဝန်ထမ်းတွေရဲ့ ကြိုးစားမှုအပေါ် မူတည်တယ်။", "ရဲ", "ရဲ့"),
                new BenchmarkCase("quotative_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "ဒီစီမံကိန်း ဘယ်တော့ပြီးမလဲလို ပြည်သူတွေက သိချင်နေကြတယ်။", "လို", "လို့"),
                new BenchmarkCase("visarga_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "တောင်ပေါ်ဒေသကို ဆောင်းရာသီမှာ ခရီးသွားရတာ အရမ်းပျော်ဖို့ကောင်တယ်။", "ကောင်", "ကောင်း"),
                new BenchmarkCase("spurious_visarga", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "ကျောင်းသားတွေ စာဖတ်ဝါသနာ ရှိလာအောင်းလည်း ဆရာတွေက ကူညီပေးတယ်။", "အောင်း", "အောင်"),
                new BenchmarkCase("plural_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "ရေဘေးသင့်ပြည်သူတွေးကို အရေးပေါ်ကယ်ဆယ်ရေးပစ္စည်းများ ထောက်ပံ့ပေးသည်။", "တွေး", "တွေ"),
                new BenchmarkCase("hidden_compound", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "အဆိုရရဲ့ ပညာရေးထောက်ပံ့ကြေး အစီအစဉ်ကို ကျောင်းသားများ လျှောက်ထားနိုင်သည်။", "အဆိုရ", "အစိုးရ"),
                new BenchmarkCase("leading_visarga", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "လုပ်ခလစာ တိုးမြှင့်ပေးပါရန် အလုပ်သမားများက တောင်ဆိုကြသည်။", "တောင်ဆို", "တောင်းဆို"),
                new BenchmarkCase("hidden_compound", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "ရှေးဟောင်းယဉ်ကျေးမှု အမွေအနှစ်များ ပျောက်စုန်မသွားအောင် ထိန်းသိမ်းရမည်။", "ပျောက်စုန်", "ပျောက်ဆုံး"),
                new BenchmarkCase("hidden_compound", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "စာကြည့်တိုက်တွင် လိုအပ်သော ကျမ်းကိုးစာအုပ်များကို အလွယ်တကူ ရှာဖွင့်နိုင်သည်။", "ရှာဖွင့်", "ရှာဖွေ"),
                new BenchmarkCase("gerundive_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "တောင်တန်းဒေသ ရှုခင်းများကြည့်ရတာ အရမ်းစိတ်လှုပ်ရှားဆရာကောင်းတယ်။", "စိတ်လှုပ်ရှားဆရာ", "စိတ်လှုပ်ရှားစရာ"),
                new BenchmarkCase("adjective_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "သဘာဝပတ်ဝန်းကျင် ထိန်းသိမ်းရေးသည် နိုင်ငံတိုင်းအတွက် အရေးကြည်သော လုပ်ငန်းဖြစ်သည်။", "အရေးကြည်", "အရေးကြီး"),
                new BenchmarkCase("collocation_trap", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "အားကစားလုပ်ခြင်းသည် စိတ်ဖိစီမှုကို သိသာစွာ လျှော့ချပေးနိုင်သည်။", "စိတ်ဖိစီမှု", "စိတ်ဖိစီးမှု"),
                new BenchmarkCase("case_collision", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "အားလပ်ရက်မှာ ကျွန်တော်ကို ရိုးရာအစားအစာတွေကို အရမ်းကြိုက်တယ်။", "ကျွန်တော်ကို", "ကျွန်တော်က"),
                new BenchmarkCase("copula_collision", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "ဦးအောင်မင်းသည် အတွေ့အကြုံရင့်ကျက်သော ဆရာဝန်ကြီးကို ဖြစ်သည်။", "ကို ဖြစ်သည်", "ဖြစ်သည်"),
                new BenchmarkCase("historical_tense", BenchmarkDifficulty.Nightmare, ProofingStyleMode.GeneralStandard,
                    "ပုဂံခေတ်ဟာ မြန်မာ့ဗိသုကာပညာ အထွန်းကားဆုံးကာလ ဖြစ်ပါလိမ့်မယ်။", "ဖြစ်ပါလိမ့်မယ်", "ဖြစ်ပါတယ်"),
                new BenchmarkCase("unstacked_patsint", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "ယာဉ်အန်ထရာယ် ကင်းရှင်းရေးအတွက် လမ်းစည်းကမ်း လိုက်နာပါ။", "အန်ထရာယ်", "အန္တရာယ်"),
                new BenchmarkCase("unstacked_patsint", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "စက်ကန့်ပိုင်း အတွင်း အဖြေထုတ်ပေးနိုင်သည်။", "စက်ကန့်", "စက္ကန့်"),
                new BenchmarkCase("coda_rerank", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "ရုံးစာတွဲများကို စနတ်တကျ မှတ်တမ်းတင်ထားရှိသည်။", "စနတ်တကျ", "စနစ်တကျ"),
                new BenchmarkCase("lookalike_neighbor", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "တံခါးပေါက်မှ လေအေးများ မ၀င်အောင် သေချာပိတ်ထားသည်။", "၀င်", "ဝင်"),
                new BenchmarkCase("glued_boundary", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "လျှို့ဝှက်စာရွက်စာတမ်းများကို လုံခြုံစွာသိမ်းဆည်း ထားရမည်။", "လုံခြုံစွာသိမ်းဆည်း", "လုံခြုံစွာ သိမ်းဆည်း"),
                new BenchmarkCase("register_clash", BenchmarkDifficulty.Nightmare, ProofingStyleMode.FormalOfficial,
                    "စာမေးပွဲခန်းတွင် ကျောင်းသားများ အေးဆေး နေရမည်။", "အေးဆေး", "တိတ်တဆိတ်"),

                // =========================================================================================
                // TIER 6: GRANDMASTER MULTI-ERROR TORTURE — 3-to-4 Simultaneous Layer Errors/Sentence
                // =========================================================================================
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "ဝန်ကြီးဌာနက အရေးကြိးသော အချက် အလက်များကို ပေးပို့ခဲ့သည် တိုင်းဒေသကြီးရုံးသို့။",
                    ("ကြိး", "ကြီး"),
                    ("အချက် အလက်", "အချက်အလက်"),
                    ("ပေးပို့ခဲ့သည် တိုင်းဒေသကြီးရုံးသို့", "တိုင်းဒေသကြီးရုံးသို့ ပေးပို့ခဲ့သည်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "ကေျာင်းသားများက စာ ကြည့် တိုက် တွင် ဘာမှ မပြောပဲ စာဖတ်နေကြသည်။",
                    ("ကေျာင်း", "ကျောင်း"),
                    ("စာ ကြည့် တိုက်", "စာကြည့်တိုက်"),
                    ("ပဲ", "ဘဲ")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "ယမန်နှစ်က အာဏာသိမ်းမူဆိုင်ရာ ဆွေနွေးပွဲကို အောင်မြင်စွာ ကျင်းပမည်ဖြစ်သည်။",
                    ("အာဏာသိမ်းမူ", "အာဏာသိမ်းမှု"),
                    ("ဆွေနွေး", "ဆွေးနွေး"),
                    ("မည်ဖြစ်သည်", "ခဲ့သည်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "မနက်တိုင်း အဝတ် လျော် ပြီး နေပူထဲ အဝတ် လှမ်း ထားပေ မယ့် မခြောက်သေးဘူး။",
                    ("လျော်", "လျှော်"),
                    ("လှမ်း", "လှန်း"),
                    ("ထားပေ မယ့်", "ထားပေမယ့်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "လာမ ည့် နှစ်တွင် ပညာရေ စီမံကိန်းသစ်ကို စတင်ဆောင်ရွက်ခဲ့သည်။",
                    ("လာမ ည့်", "လာမည့်"),
                    ("ပညာရေ", "ပညာရေး"),
                    ("ခဲ့သည်", "မည်ဖြစ်သည်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "သဘာဝပတ်ဝန်းကျင် ကာကွယ်ဖိုက အရေးကြည်သော လုပ်ငန်းဖြစ်လို့ အမြတ်မထုတ်ပဲ ကူညီသင့်တယ်။",
                    ("ဖို", "ဖို့"),
                    ("အရေးကြည်", "အရေးကြီး"),
                    ("ပဲ", "ဘဲ")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "ဖိနပ် တစ်ခု ဝယ်ပြီး အစ်ကိုက မန္တလေးမြို့ကို နေထိုင်တယ် လို့ ပြော့သည်။",
                    ("ခု", "ရံ"),
                    ("ကို", "မှာ"),
                    ("ပြော့", "ပြော")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "ဆရာတော်ကြီးက တရားဟောကြား တော်မူ၍ တပည့်များက သွန်သင်ဆုံမ ချက်ကို နာယူကြပတ်တယ်။",
                    ("ဟောကြား တော်မူ", "ဟောကြားတော်မူ"),
                    ("သွန်သင်ဆုံမ", "သွန်သင်ဆုံးမ"),
                    ("ပတ်", "ပါ")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "စစ်တက္ ကသိုလ်မှ သုတေသီများက စမ်းသပ်ချက် ရလဓ် ကို စနစ်တကွ မှတ်တမ်းတင်သည်။",
                    ("စစ်တက္ ကသိုလ်", "စစ်တက္ကသိုလ်"),
                    ("ရလဓ်", "ရလဒ်"),
                    ("စနစ်တကွ", "စနစ်တကျ")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "နေ့စဉ် လမ်းလျှောက်ချင်းက စိတ်ဖိစီမှုကို လျော့ကျစေလို့ လူတိုင်း လုပ်ဖို့ကောင်တယ်။",
                    ("ချင်း", "ခြင်း"),
                    ("စိတ်ဖိစီမှု", "စိတ်ဖိစီးမှု"),
                    ("ကောင်", "ကောင်း")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "အဖွဲ့ အစည်းက ပြဿနာကို ထိရောက်သော ဖြေရှင်းနိုင်ရန် စီမံချက်ကို ပြင်ဆင်ကာ ရှိသည်။",
                    ("အဖွဲ့ အစည်း", "အဖွဲ့အစည်း"),
                    ("သော", "စွာ"),
                    ("ကာ", "လျက်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "အဆိုရရဲ့ ဝန်စောင့်မှုလုပ်ငန်းများ ပိုမိုကောင်းမွန်လာအောင်းလည်း ဆောင်ရွက်ပေးသည်။",
                    ("အဆိုရ", "အစိုးရ"),
                    ("ရဲ့", "၏"),
                    ("ဝန်စောင့်မှု", "ဝန်ဆောင်မှု"),
                    ("အောင်း", "အောင်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "ယာဉ်မတော်တဆမှုကြောင့် ထိခိုပ်ဒဏ်ရာရရှိသူတွေးကို ဆေးရုံသို့ ပို့ဆောင်ပြီးပါပီ.",
                    ("ထိခိုပ်", "ထိခိုက်"),
                    ("တွေး", "တွေ"),
                    ("ပါပီ", "ပါပြီ"),
                    (".", "။")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "အနော် ရထာမင်းကြီး လက်ထက်မှာ ဘာသာ ရေး စာပေများ စတင်ထွန်းကားပါလိမ့်မယ်။",
                    ("အနော် ရထာ", "အနော်ရထာ"),
                    ("ဘာသာ ရေး", "ဘာသာရေး"),
                    ("ပါလိမ့်မယ်", "ခဲ့ပါတယ်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.GeneralStandard,
                    "ဒါနပြုခြင်းဖြင့် စိပ်ကြည်နူးမှု ရရှိသလိုဘဲ တိုးတက်စေပါတယ် လူ့အသိုင်းအဝိုင်းကို။",
                    ("စိပ်ကြည်နူး", "စိတ်ကြည်နူး"),
                    ("ဘဲ", "ပဲ"),
                    ("တိုးတက်စေပါတယ် လူ့အသိုင်းအဝိုင်းကို", "လူ့အသိုင်းအဝိုင်းကို တိုးတက်စေပါတယ်")),
                new BenchmarkCase("multi_torture", BenchmarkDifficulty.GrandmasterTorture, ProofingStyleMode.FormalOfficial,
                    "တရား ဥပဒေ စိုးမိုးရေးအတြက္ စာရွက်စာတမ်းများကို စနတ်တကျ လုံခြုံစွာသိမ်းဆည်း ရမည်။",
                    ("တရား ဥပဒေ", "တရားဥပဒေ"),
                    ("အတြက္", "အတွက်"),
                    ("စနတ်တကျ", "စနစ်တကျ"),
                    ("လုံခြုံစွာသိမ်းဆည်း", "လုံခြုံစွာ သိမ်းဆည်း")),

                // =========================================================================================
                // TIER 7: ABYSSAL MYTHIC TORTURE — 5-to-6 Simultaneous Cascading Cross-Layer Errors/Sentence
                // =========================================================================================
                new BenchmarkCase("abyssal_gov", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "အစိုးရ အဖွဲ့က အခြေခံအဆောက်အဦများ တိုးတက်လာဖို့ လုအပ်သော စီမံကန်းများကို အကောင်အထည်ဖော်ဖို ညွှန်ကြားခဲ့သည် တိုင်းဒေသကြီးရုံးများသို့.",
                    ("အစိုးရ အဖွဲ့", "အစိုးရအဖွဲ့"),
                    ("လုအပ်", "လိုအပ်"),
                    ("စီမံကန်း", "စီမံကိန်း"),
                    ("ဖို", "ဖို့"),
                    ("ညွှန်ကြားခဲ့သည် တိုင်းဒေသကြီးရုံးများသို့", "တိုင်းဒေသကြီးရုံးများသို့ ညွှန်ကြားခဲ့သည်"),
                    (".", "။")),
                new BenchmarkCase("abyssal_cascade", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "ဒီမနက် အစ်ကိုက ဖိနဘ် နှစ်ယောက် ဝယ်ယူပီ မိသားစုအထွက် လက်ဆောင်ပေးခြင်တယ် လို့ ပြောတယ်။",
                    ("ဖိနဘ်", "ဖိနပ်"),
                    ("ယောက်", "ရံ"),
                    ("ယူပီ", "ယူပြီး"),
                    ("အထွက်", "အတွက်"),
                    ("ခြင်", "ချင်")),
                new BenchmarkCase("abyssal_causative", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "ပတ် ဝန်းကျင် ထိန်းသိမ်းစောင့်ရှောက်ချင်းက လူသားတွေက ဘေးကင်းလုံခြုံစေပြီး ကျန်းမာရေးကောင်းဖို အဓိကအချက်ပယ်။",
                    ("ပတ် ဝန်းကျင်", "ပတ်ဝန်းကျင်"),
                    ("ချင်း", "ခြင်း"),
                    ("လူသားတွေက", "လူသားတွေကို"),
                    ("ဖို", "ဖို့"),
                    ("ပယ်", "ပဲ")),
                new BenchmarkCase("abyssal_pali", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "ကျောင်း တိုက်များတွင် သတိပဋ္ဌာ တရားပွားများရန် အလည်အပဒ် လာရောက်သူများအား အကူ အညီ ပေးအပ်ကြောင်း ရေသားထားသည်။",
                    ("ကျောင်း တိုက်", "ကျောင်းတိုက်"),
                    ("သတိပဋ္ဌာ", "သတိပဋ္ဌာန်"),
                    ("အလည်အပဒ်", "အလည်အပတ်"),
                    ("အကူ အညီ", "အကူအညီ"),
                    ("ရေသား", "ရေးသား")),
                new BenchmarkCase("abyssal_law", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "မူးယစ် ဆေးဝါး ရောင်းဝယ်ခြင်းတည် ဥပဒေချိုးဖောက်မှုဖြစ်၍ တရားခံများကို အမြန်ဆုံး ဖမ်းစီ အရေးယူခွင်ပြုရန် လုအပ်သည်။",
                    ("မူးယစ် ဆေးဝါး", "မူးယစ်ဆေးဝါး"),
                    ("တည်", "သည်"),
                    ("ဖမ်းစီ", "ဖမ်းဆီး"),
                    ("ခွင်", "ခွင့်"),
                    ("လုအပ်", "လိုအပ်")),
                new BenchmarkCase("abyssal_tech", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "ဒီဆော့ဝဲလ်ကို အသုံးပြုမယ်သူတွေအနေနဲ့ စာရွက် စာတမ်းတွေကို လုံခြုံစွာသိမ်းဆည်း နိုင်ပါတယ်လား။",
                    ("ဆော့ဝဲလ်", "ဆော့ဖ်ဝဲ"),
                    ("မယ်", "မယ့်"),
                    ("စာရွက် စာတမ်း", "စာရွက်စာတမ်း"),
                    ("လုံခြုံစွာသိမ်းဆည်း", "လုံခြုံစွာ သိမ်းဆည်း"),
                    ("ပါတယ်လား", "ပါသလား")),
                new BenchmarkCase("abyssal_narrative", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "ခရစ္စမတ်နေမှာ မိုးကြိုးသံကြားလို့ ရင်ထဲမာ အလွန် ကျောက်နေပေမဲ့ စွပ်ပြုတ်ပူပူလေးက အရမ်းသောက်လိုကောင်းပြီး ပျော့စရာကောင်းတယ်။",
                    ("နေမှာ", "နေ့မှာ"),
                    ("ထဲမာ", "ထဲမှာ"),
                    ("ကျောက်", "ကြောက်"),
                    ("သောက်လို", "သောက်လို့"),
                    ("ပျော့စရာ", "ပျော်စရာ")),
                new BenchmarkCase("abyssal_economy", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "ပွန်ထမ်းများ၏ အရည် အချင်း ပိုမို ကျယ်ပြန်လာစေရန်နှင့် အခွန်ဆောင်းဖို့ ခွင့်မူချက် ထုတ်ပေးထားသည်။",
                    ("ပွန်ထမ်း", "ဝန်ထမ်း"),
                    ("အရည် အချင်း", "အရည်အချင်း"),
                    ("ကျယ်ပြန်", "ကျယ်ပြန့်"),
                    ("အခွန်ဆောင်း", "အခွန်ဆောင်"),
                    ("ခွင့်မူချက်", "ခွင့်ပြုချက်")),
                new BenchmarkCase("abyssal_culinary", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "နံနက် စာအဖြစ် ထမင်း ကျော်၊ လက်ဖက်သုတ်နှင့် ငံ့ပြာရည်ပါသော ခေါက်ဆွဲပြု သောက်ကြတယ်။",
                    ("နံနက် စာ", "နံနက်စာ"),
                    ("ထမင်း ကျော်", "ထမင်းကြော်"),
                    ("လက်ဖက်သုတ်", "လက်ဖက်သုပ်"),
                    ("ငံ့ပြာရည်", "ငံပြာရည်"),
                    ("ပြု", "ပြုတ်")),
                new BenchmarkCase("abyssal_media", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "ကျွန်တော်ရဲ့ ဖုန်းထဲမှ သီချန်ကို နား ထောင်ရင်း ဘောပင်းနဲ့ စာရေးရတာ အရမ်းလှ့တယ်။",
                    ("ကျွန်တော်", "ကျွန်တော့်"),
                    ("သီချန်", "သီချင်း"),
                    ("နား ထောင်", "နားထောင်"),
                    ("ဘောပင်း", "ဘောပင်"),
                    ("လှ့", "လှ")),
                new BenchmarkCase("abyssal_resources", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "သဘာဝသယံဇာ ထုတ်လုပ်သည့် လုပ်ငန်းထွင် သတင်းအချက်အရက် မှန်ကန်မှုက အထောက် အကူပြုပြီး လုပ်ငန်းကို လွယ်ကုစေသည်။",
                    ("သယံဇာ", "သယံဇာတ"),
                    ("ထွင်", "တွင်"),
                    ("သတင်းအချက်အရက်", "သတင်းအချက်အလက်"),
                    ("အထောက် အကူ", "အထောက်အကူ"),
                    ("လွယ်ကု", "လွယ်ကူ")),
                new BenchmarkCase("abyssal_register", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "စာမေးပွဲခန်းတွင် ကျောင်းသားတွေဂ အေးဆေး နေရမည်ဖြစ်ပြီး ယာဉ်အန်ထရာယ် ကင်းရှင်းရေးအတြက္ ဆုတောင်းပေးကြပါစေလား။",
                    ("ဂ", "က"),
                    ("အေးဆေး", "တိတ်တဆိတ်"),
                    ("အန်ထရာယ်", "အန္တရာယ်"),
                    ("အတြက္", "အတွက်"),
                    ("ပါစေလား", "ပါစေ")),
                new BenchmarkCase("abyssal_literary", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "မဟဂျွတ် ကုသိုလ်တရားများကို သတ္တိရမိတိုင်း အခက် အခဲကြုံလျှင် အကူအညီတောင်ပါရန် မျောလင့်မိပါတယ်။",
                    ("မဟဂျွတ်", "မဟဂ္ဂုတ်"),
                    ("သတ္တိရမိ", "သတိရမိ"),
                    ("အခက် အခဲ", "အခက်အခဲ"),
                    ("တောင်", "တောင်း"),
                    ("မျောလင့်", "မျှော်လင့်")),
                new BenchmarkCase("abyssal_dialogue", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.GeneralStandard,
                    "မင်္ဂလာပါ ခင်ဗျား။ ကျွန်တော်မှာ မိတ်ဆွေတွေနဲ့ စကားဖျော်ရင်း ဘာကြောင့် သူနဲ့ လမ်းခွခဲ့သလဲဆိုတာ သီချင်ပါတယ်။",
                    ("ခင်ဗျား", "ခင်ဗျာ"),
                    ("ကျွန်တော်", "ကျွန်တော့်"),
                    ("ဖျော်", "ပြော"),
                    ("လမ်းခွ", "လမ်းခွဲ"),
                    ("သီ", "သိ")),
                new BenchmarkCase("abyssal_boss", BenchmarkDifficulty.AbyssalMythic, ProofingStyleMode.FormalOfficial,
                    "ယမန်နှစ်က စစ်တက္ ကသိုလ်မှ သုတေသီများသည် မှတ်ပုံ\u200Bတင် စာရင်းနှင့် ကေျာင်းသား အချက်အလတ်များကို ပေးပို့ခဲ့သည် ဗဟိုရုံးချုပ်သို့.",
                    ("စစ်တက္ ကသိုလ်", "စစ်တက္ကသိုလ်"),
                    ("မှတ်ပုံ\u200Bတင်", "မှတ်ပုံတင်"),
                    ("ကေျာင်း", "ကျောင်း"),
                    ("အချက်အလတ်", "အချက်အလက်"),
                    ("ပေးပို့ခဲ့သည် ဗဟိုရုံးချုပ်သို့", "ဗဟိုရုံးချုပ်သို့ ပေးပို့ခဲ့သည်"),
                    (".", "။")),

                // =========================================================================================
                // TIER 8: OFFICIAL B2G GOVERNMENT PORTAL & GAZETTE (.gov.mm Authentic Ministry Suite — 20 Cases)
                // =========================================================================================
                new BenchmarkCase("gov_secm", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ချေးငွေသက်သေခံလက်မှတ်လုပ်ငန်း ကြီးကြပ်ရေးကော်မရှင်မှ ရန်ကုန်စတော့အိပ်ချိန်းရှိ အစုရှယ် ယာ ရောင်းဝယ်မှုများကို တပြေးညီ ကြီးကြပ်ဆောင်ရွက်လျှက်ရှိသည်။",
                    ("စတော့အိပ်ချိန်း", "စတော့အိတ်ချိန်း"),
                    ("ရှယ် ယာ", "ရှယ်ယာ"),
                    ("တပြေးညီ", "တစ်ပြေးညီ"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("gov_cbm", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "မြန်မာနိုင်ငံတော်ဗဟိုဘဏ် ဥက္ကဌ ဦးဆောင်သော အစည်း အဝေးတွင် အတိုးနှုန်း လျော့ချ ရေးမူဝါဒကို ကြေငြာ ခဲ့သည်။",
                    ("ဥက္ကဌ", "ဥက္ကဋ္ဌ"),
                    ("အစည်း အဝေး", "အစည်းအဝေး"),
                    ("လျော့ချ", "လျှော့ချ"),
                    ("ကြေငြာ", "ကြေညာ")),
                new BenchmarkCase("gov_moezala", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "မိုးလေဝသနှင့် ဇလဗေဒညွှန်ကြားမှု ဦးစီး ဌာန၏ ထုတ်ပြန်ချက်အရ မိုးရွာသွန်ခြင်းကြောင့် ရေကြီးနစ်မြှုပ်မှု အန်တရာယ် သတိပြုရမည်။",
                    ("ဦးစီး ဌာန", "ဦးစီးဌာန"),
                    ("ရွာသွန်ခြင်း", "ရွာသွန်းခြင်း"),
                    ("နစ်မြှုပ်", "နစ်မြုပ်"),
                    ("အန်တရာယ်", "အန္တရာယ်")),
                new BenchmarkCase("gov_mifer", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ရင်းနှီးမြုပ်နှံမှုနှင့် နိုင်ငံခြားစီးပွားဆက်သွယ်ရေး ဝန်ကြီး ဌာနက ဥပဒေ ပြဌာန်းချက်များနှင့်အညီ ခွင့်မူချက် ထုတ်ပေးသည်။",
                    ("ရင်းနှီးမြုပ်နှံ", "ရင်းနှီးမြှုပ်နှံ"),
                    ("ဝန်ကြီး ဌာန", "ဝန်ကြီးဌာန"),
                    ("ပြဌာန်း", "ပြဋ္ဌာန်း"),
                    ("ခွင့်မူချက်", "ခွင့်ပြုချက်")),
                new BenchmarkCase("gov_ird", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ပြည်တွင်းအခွန်များဦးစီးဌာနသို့ အခွန်ဆောင်း ရမည့် လုပ်ငန်းရှင်များသည် စာရင်းအချက်အလတ်များကို စနစ် တကျ တင်သွင်းရမည်။",
                    ("အခွန်ဆောင်း", "အခွန်ဆောင်"),
                    ("အချက်အလတ်", "အချက်အလက်"),
                    ("စနစ် တကျ", "စနစ်တကျ")),
                new BenchmarkCase("gov_customs", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "အကောက်ခွန်ဦးစီးဌာနမှ ကုန်သွယ်မှု ကွန်ယက် အတွင်း တရားမဝင် ပစ္စည်းများကို စစ်ဆေးဖမ်းစီ၍ အရေးယူခွင်ပြုထားသည်။",
                    ("ကွန်ယက်", "ကွန်ရက်"),
                    ("ဖမ်းစီ", "ဖမ်းဆီး"),
                    ("ခွင်", "ခွင့်")),
                new BenchmarkCase("gov_ycdc", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "မြို့တော်စည်ပင်သာယာရေးကော်မတီက အဆောက်အဦး အသစ်များနှင့် လူသွား စင်္ကြန် များကို ကွင်းဆင်းကြည့်ရှု့ စစ်ဆေးခဲ့သည်။",
                    ("အဆောက်အဦး", "အဆောက်အဦ"),
                    ("စင်္ကြန်", "စင်္ကြံ"),
                    ("ကြည့်ရှု့", "ကြည့်ရှု")),
                new BenchmarkCase("gov_fda", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "အစားအသောက်နှင့် ဆေးဝါးကွပ်ကဲရေးဦးစီးဌာနက သက်တမးလွန် ဆေးဝါးများနှင့် အာမကံဝန်ဆောင်မှု မပါသော ကုန်ပစ္စည်းများကို စိစစ်လျှက်ရှိသည်။",
                    ("သက်တမး", "သက်တမ်း"),
                    ("အာမကံ", "အာမခံ"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("gov_supremecourt", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုတရားလွှတ်တော်ချုပ်က ပစ်မှုဆိုင်ရာ ဥပဒေ ပြဌာန်းချက်များနှင့်အညီ အမှုတွဲ စာရွက် စာတမ်းများကို ပေးပို့ခဲ့သည် တိုင်းဒေသကြီးရုံးများသို့.",
                    ("ပစ်မှု", "ပြစ်မှု"),
                    ("ပြဌာန်း", "ပြဋ္ဌာန်း"),
                    ("စာရွက် စာတမ်း", "စာရွက်စာတမ်း"),
                    ("ပေးပို့ခဲ့သည် တိုင်းဒေသကြီးရုံးများသို့", "တိုင်းဒေသကြီးရုံးများသို့ ပေးပို့ခဲ့သည်"),
                    (".", "။")),
                new BenchmarkCase("gov_msme", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "အသေးစား၊ အငယ်စားနှင့် အလတ်စား စီးပွားရေလုပ်ငန်းများ ဖွံ့ဖြိုးတိုးထက် စေရန် ချေးငွေ အထောက် အကူ ပေးအပ်သည်။",
                    ("ရေ", "ရေး"),
                    ("ဖွံ့ဖြိုးတိုးထက်", "ဖွံ့ဖြိုးတိုးတက်"),
                    ("အထောက် အကူ", "အထောက်အကူ")),
                new BenchmarkCase("gov_moali", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "စိုက်ပျိုးရေး၊ မွေးမြူရေးနှင့် ဆည်မြောင်းဝန်ကြီးဌာနက ကျေးလက် အိမ်ယာ စီမံကိန်းနှင့် တိရိစ္ဆာန် မွေးမြူရေးလုပ်ငန်းများကို ကူညီပံ့ပိုးလျှက်ရှိသည်။",
                    ("အိမ်ယာ", "အိမ်ရာ"),
                    ("တိရိစ္ဆာန်", "တိရစ္ဆာန်"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("gov_moe", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ပညာရေ ဝန်ကြီးဌာနသည် ကျောင်းသားများအတွက် သင်ခဏ်းစာ အသစ်များနှင့် စာပေပို့ချမူ စနစ်ကို အကောင်ထည်ဖော် ဆောင်ရွက်သည်။",
                    ("ပညာရေ", "ပညာရေး"),
                    ("သင်ခဏ်းစာ", "သင်ခန်းစာ"),
                    ("ပို့ချမူ", "ပို့ချမှု"),
                    ("အကောင်ထည်ဖော်", "အကောင်အထည်ဖော်")),
                new BenchmarkCase("gov_moi", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ပြန်ကြားရေးဝန်ကြီးဌာန၏ သတင်းအချက် အလက် ထုတ်ပြန်ချက်တွင် အစိုးရ အဖွဲ့၏ ရှေ့လုပ်ငန်းစင်များကို ဖေါ်ပြထားသည်။",
                    ("အချက် အလက်", "အချက်အလက်"),
                    ("အစိုးရ အဖွဲ့", "အစိုးရအဖွဲ့"),
                    ("လုပ်ငန်းစင်", "လုပ်ငန်းစဉ်"),
                    ("ဖေါ်ပြ", "ဖော်ပြ")),
                new BenchmarkCase("gov_mohs", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ကျန်းမာရေ ဝန်ကြီးဌာနက ပြည်သူ့ဆေးရုံများတွင် ရောဂါ ကာကွယ်ထိန်းသိမ်းပေါင့်ရှောက်ရေး လုပ်ငန်းများကို တိုးမြှင့်ဆောင်ရွက်လျှက်ရှိသည်။",
                    ("ကျန်းမာရေ", "ကျန်းမာရေး"),
                    ("ပေါင့်", "စောင့်"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("gov_mora", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "သာသနာရေးနှင့် ယဉ်ကျေးမှုဝန်ကြီးဌာနက ကျောင်း တိုက်များ၌ ဝိနည်း နိဿဂ္ဂိပါစိတ် ဆိုင်ရာ ဆာပေပို့ချပွဲများ ကျင်း ပသည်။",
                    ("ကျောင်း တိုက်", "ကျောင်းတိုက်"),
                    ("နိဿဂ္ဂိပါစိတ်", "နိဿဂ္ဂိယပါစိတ်"),
                    ("ဆာ", "စာ"),
                    ("ကျင်း ပ", "ကျင်းပ")),
                new BenchmarkCase("gov_mocon", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ဆောက်လုပ်ရေးဝန်ကြီးဌာနမှ မြို့ တွင်းသွား လမ်းမကြီး၏ နံဘေး တွင် ရေနုတ်မြောင်းများ ပြုပြင်တည်ဆောက်လျှက်ရှိသည်။",
                    ("မြို့ တွင်းသွား", "မြို့တွင်းသွား"),
                    ("နံဘေး တွင်", "နံဘေးတွင်"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("gov_mminsurance", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "မြန်မာ့အာမခံလုပ်ငန်းမှ ပြည်သူများအနေဖြင့် အာမခံက ရွေးချယ်နိုင် ပြီး ငွေပေးချေမူ စနစ်ဖြင့် အလွယ် တကူ ပေးသွင်းနိုင်သည်။",
                    ("က", "ကို"),
                    ("ငွေပေးချေမူ", "ငွေပေးချေမှု"),
                    ("အလွယ် တကူ", "အလွယ်တကူ")),
                new BenchmarkCase("gov_molip", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "အလုပ်သမားညွှန်ကြားရေးဦးစီးဌာနက ပွန်ထမ်းများ၏ အကျိုး ခံစားခွင့် များကို အမြတ်မထုတ်ပဲ ဥပဒေနှင့်အညီ စိစစ်ပေးသည်။",
                    ("ပွန်ထမ်း", "ဝန်ထမ်း"),
                    ("အကျိုး ခံစားခွင့်", "အကျိုးခံစားခွင့်"),
                    ("ပဲ", "ဘဲ")),
                new BenchmarkCase("gov_monrec", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "သယံဇာတနှင့် သဘာဝပတ် ဝန်းကျင် ထိန်းသိမ်းရေးဝန်ကြီးဌာနက သစ်တောပြုန်းတီးမှု လျော်ကြစေသည့် စီမံကန်းများကို ဆောင်ရွက်သည်။",
                    ("ပတ် ဝန်းကျင်", "ပတ်ဝန်းကျင်"),
                    ("လျော်ကြ", "လျော့ကျ"),
                    ("စီမံကန်း", "စီမံကိန်း")),
                new BenchmarkCase("gov_gazette_boss", BenchmarkDifficulty.GovernmentB2G, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုအစိုးရ အဖွဲ့၏ အမိန့်ကြော်ငြာစာအရ ကော်မရှင် ဥက္ကဌ သည် ၂၀၂၆ ခုနစ် ဘဏ္ဍာရေးနှစ် အစီ အစဉ်များကို အတည်ပြု ကြေငြာ ခဲ့သည်။",
                    ("အစိုးရ အဖွဲ့", "အစိုးရအဖွဲ့"),
                    ("ဥက္ကဌ", "ဥက္ကဋ္ဌ"),
                    ("ခုနစ်", "ခုနှစ်"),
                    ("အစီ အစဉ်", "အစီအစဉ်"),
                    ("ကြေငြာ", "ကြေညာ")),

                // =========================================================================================
                // TIER 9: OPTIMISED TOP-1 RANKING & WHOLE-STEM COALESCER CHAMPIONSHIP (--top1 — 20 Cases)
                // =========================================================================================
                new BenchmarkCase("top1_coalesce_bell", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "ကျောင်းဝင်းအတွင်းရှိ ကေါင်းလောင်းသံကြားလျှင် ကျောင်းသားများ အစည်း အဝေးခန်းမသို့ စနစ် တကျ ဝင်ရောက်ကြသည်။",
                    ("ကေါင်းလောင်း", "ခေါင်းလောင်း"),
                    ("အစည်း အဝေး", "အစည်းအဝေး"),
                    ("စနစ် တကျ", "စနစ်တကျ")),
                new BenchmarkCase("top1_coalesce_sector", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "စိုက်ပျိုးရေးသည် နိုင်ငံ့စီးပွားရေးအတွက် အရေးပါတဲ့ကဏ္တတစ်ခုဖြစ်ပြီး စုစု ပောင်း ထုတ်ကုန်တိုးတက်စေသည်။",
                    ("ကဏ္တ", "ကဏ္ဍ"),
                    ("စုစု ပောင်း", "စုစုပေါင်း")),
                new BenchmarkCase("top1_coalesce_prevent", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "သဘာဝဘေးအန္တရာယ် တားသိးနိုင်ခွင့်နှင့် ပတ်ဝန်းကျင် ထိးသိမ်းဖို့က အားလုံးအတွက် အရေးကြီးတယ်။",
                    ("တားသိး", "တားဆီး"),
                    ("ထိးသိမ်း", "ထိန်းသိမ်း")),
                new BenchmarkCase("top1_coalesce_component", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "ဒီဆော့ဖ်ဝဲလ်နဲ့ အက်ပလီကေးရှင်းတွေဟာ ခေတ်မီနည်းပညာရဲ့ အစိးအပိုင်းတွေဖြစ်ပြီး အစွနးအစကို ပြသနိုင်တယ်။",
                    ("အစိးအပိုင်း", "အစိတ်အပိုင်း"),
                    ("အစွနးအစ", "အစွမ်းအစ")),
                new BenchmarkCase("top1_coalesce_goldsmith", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "မြန်မာ့ရိုးရာ ပန်းဌိမ်ပစ္စည်းတွေနဲ့ လက်မှုအဖွဲ့အချည်အတွက် နည်းပညာပံ့ပိုးမှု လိုအပ်တယ်။",
                    ("ပန်းဌိမ်", "ပန်းထိမ်"),
                    ("အဖွဲ့အချည်", "အဖွဲ့အစည်း")),
                new BenchmarkCase("top1_phon_ya_ra", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "ညိမ်းချမ်းရေး ဆွေးနွေးပွဲအတွက် အမြာဆုံး ပြင်ဆင်ပြီး မွနကန်စွာ ဆောင်ရွက်ရမယ်။",
                    ("ညိမ်းချမ်းရေး", "ငြိမ်းချမ်းရေး"),
                    ("အမြာဆုံး", "အမြန်ဆုံး"),
                    ("မွနကန်", "မှန်ကန်")),
                new BenchmarkCase("top1_phon_zay_iuk", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "ဒီဇေးဆိုင်က မုန့်ဟင်းခါးကို အရမ်းကြိက်လို့ မနက်တိုင်း ရေသာက်ရင်း စားဖြစ်တယ်။",
                    ("ဇေးဆိုင်", "ဈေးဆိုင်"),
                    ("ကြိက်", "ကြိုက်"),
                    ("သာက်", "သောက်")),
                new BenchmarkCase("top1_phon_um_nya", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "လုပ်ငန်းခွင်တွင် အသိညဏ်နှင့် အတွေ့အကြုံကို အကျိုးရှိစွာ အသုမ်ပြု၍ စီမံခန့်ခွဲရမည်။",
                    ("အသိညဏ်", "အသိဉာဏ်"),
                    ("အသုမ်ပြု", "အသုံးပြု")),
                new BenchmarkCase("top1_ctx_nominalizer", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "လုပ်ငန်းအောင်မြင်နှု ရရှိစေဖို့ ဟင်းချက်နဲတွေကို စနစ် တကျ လေ့လာသင်ယူကြတယ်။",
                    ("နှု", "မှု"),
                    ("နဲ", "နည်း"),
                    ("စနစ် တကျ", "စနစ်တကျ")),
                new BenchmarkCase("top1_ctx_judicial", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "တရားစီရိန်ရေးကဏ္ဍမှာ အချက်အလက်တွေကို နှိုင်းယှင်းပြီး သတင်းထိန်ချိန်ထားခြင်း မပြုရပါ။",
                    ("ရိန်", "ရင်"),
                    ("ယှင်း", "ယှဉ်"),
                    ("ထိန်ချိန်", "ထိမ်ချန်")),
                new BenchmarkCase("top1_ctx_aviation", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "ဒီန့ဲ မနက်ပိုင်း လေယာဉခရီးစဉ်ဖြင့် ရှေးဟောင်းဒေသသို့ သွားရောက်နည်ပတ်ကြတယ်။",
                    ("န့ဲ", "နေ့"),
                    ("လေယာဉ", "လေယာဉ်"),
                    ("ရောက်နည်", "ရောက်လည်")),
                new BenchmarkCase("top1_ctx_reflexive", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "လူငယ်တွေက ကိုယ့်ကိုရ်ကို ယုံကြည်မှုရှိပြီး ကောလဟ်လသတင်းတွေကို ဆန်းစစ်သင့်တယ်။",
                    ("ကိုရ်", "ကိုယ်"),
                    ("ကောလဟ်လ", "ကောလာဟလ")),
                new BenchmarkCase("top1_ctx_nature", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "ယနဲခေတ်မှာ သဘာသအလှကို ထိန်းသိမ်းရင်း စိတ်ဖိစိမှုတွေကို လျှော့ချနိုင်တယ်။",
                    ("ယနဲ", "ယနေ့"),
                    ("သဘာသ", "သဘာဝ"),
                    ("စိတ်ဖိစိမှု", "စိတ်ဖိစီးမှု")),
                new BenchmarkCase("top1_ctx_island", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "ပင်လယ်ပြင်ရှိ ကျွန်းကုမ်းဒေသများတွင် မတော်တဆဖစ်မှု ကင်းဝေးစေရန် စက်ကန့်ပိုင်းအတွင်း သတိပေးနိုင်သည်။",
                    ("ကျွန်းကုမ်း", "ကျွန်းကုန်း"),
                    ("ဖစ်မှု", "ဖြစ်မှု"),
                    ("စက်ကန့်ပိုင်း", "စက္ကန့်ပိုင်း")),
                new BenchmarkCase("top1_colloc_idiom", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "မိသားစုဝင်များသည် အေးအတူပူအမျှာ လက်တွဲပြီး အိမ်သုံပစ္စည်းများကို စနစ် တကျ သိမ်းဆည်းကြသည်။",
                    ("အမျှာ", "အမျှ"),
                    ("သုံ", "သုံး"),
                    ("စနစ် တကျ", "စနစ်တကျ")),
                new BenchmarkCase("top1_colloc_science", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "သိပ္ပံပညာရှင်တွေက ဒါကို သေချာလေ့လာပြီး လေ့ကျင့်ခန်းလုပ်တာက ကျန်းမာရေးကို ကောင်းစေသည်။",
                    ("ဒါ", "ယင်း"),
                    ("လုပ်တာက", "လုပ်ခြင်းသည်")),
                new BenchmarkCase("top1_harmony_civic", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "မျိုးချစ်စိတ်ဓာတ်ဖြင့် ပြည်သူတွေ ပူးပေါင်းဆောင်ရွက်ကြပြီး သူရဲကောင်းတို့ရဲ့ ဂုဏ်ကျေးဇူးကို အောက်မေ့ပါမည်။",
                    ("ပြည်သူတွေ", "ပြည်သူများ"),
                    ("တို့ရဲ့", "တို့၏")),
                new BenchmarkCase("top1_harmony_case", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "စားပွဲထိုးကို စားပွဲပေါ်မှာ အစားအသောက်တင်ပေးပြီး ကျွန်တော့်အမှားကြောင့် ခွင့်လွှတ်ပေးပါဟု ပြောတယ်။",
                    ("ကို", "က"),
                    ("ကြောင့်", "အတွက်")),
                new BenchmarkCase("top1_jogging_time", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.GeneralStandard,
                    "မနက်တိုင်း ပန်းခြံထဲမှာ ဂျောဂင််းပြေးပြီးတဲ့အခါကတော့ ရေနွေးကြမ်း သောက်လေ့ရှိတယ်။",
                    ("ဂျောဂင််း", "ဂျောဂင်း"),
                    ("ကတော့", "မှာတော့")),
                new BenchmarkCase("top1_championship_boss", BenchmarkDifficulty.Top1Championship, ProofingStyleMode.FormalOfficial,
                    "ပညာရေ ဝန်ကြီး ဌာန၏ ထုတ်ပြန်ချက်အရ ကော်မရှင် ဥက္ကဌ သည် အရေးပါတဲ့ကဏ္တတစ်ခုဖြစ်သော သုတေသနလုပ်ငန်းစင်များကို ကြေငြာ ခဲ့သည်.",
                    ("ပညာရေ", "ပညာရေး"),
                    ("ဝန်ကြီး ဌာန", "ဝန်ကြီးဌာန"),
                    ("ဥက္ကဌ", "ဥက္ကဋ္ဌ"),
                    ("ကဏ္တ", "ကဏ္ဍ"),
                    ("လုပ်ငန်းစင်", "လုပ်ငန်းစဉ်"),
                    ("ကြေငြာ", "ကြေညာ"),
                    (".", "။")),

                // =========================================================================================
                // TIER 10: HARDEST STANDARD ORTHOGRAPHY, SIMPLEX/CAUSATIVE & MLC CONFUSABLES (--standard — 25 Cases)
                // =========================================================================================
                new BenchmarkCase("std_causative_destroy", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "တရားမဝင် သစ်ခုတ်သူများသည် သဘာဝသစ်တောများကို ပျက်စီးခဲ့ကြပြီး သက်သေအထောက်အထားကို ပျောက်ဖျက်ရန် ကြိုးပမ်းသည်။",
                    ("ပျက်စီး", "ဖျက်ဆီး"),
                    ("ပျောက်ဖျက်", "ဖျောက်ဖျက်")),
                new BenchmarkCase("std_causative_cut_elevate", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "တာဝန်ရှိသူများက တရားမဝင် ဖုန်းလိုင်းများကို ပြတ်တောက်ခဲ့ပြီး ဆက်သွယ်ရေး အရည်အသွေးကို မြင့်တင်ပေးခဲ့သည်။",
                    ("ပြတ်တောက်", "ဖြတ်တောက်"),
                    ("မြင့်တင်", "မြှင့်တင်")),
                new BenchmarkCase("std_causative_fulfill_divide", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ဝန်ကြီးဋ္ဌာနသည် ဒေသခံပြည်သူများ၏ လိုအပ်ချက်ကို ပြည့်ဆည်းပေးပြီး လုပ်ငန်းတာဝန်များကို စနစ်တကျ ကွဲခြားသတ်မှတ်သည်။",
                    ("ဝန်ကြီးဋ္ဌာန", "ဝန်ကြီးဌာန"),
                    ("ပြည့်ဆည်း", "ဖြည့်ဆည်း"),
                    ("ကွဲခြား", "ခွဲခြား")),
                new BenchmarkCase("std_causative_urge_reduce", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ကျန်းမာရေးဦးစီးဋ္ဌာနက ပြည်သူလူထုကို နိုးဆော်ထားပြီး ရောဂါ ကူးဆက်နှုန်း လျော့ချနိုင်ရေး ဆောင်ရွက်သည်။",
                    ("ဦးစီးဋ္ဌာန", "ဦးစီးဌာန"),
                    ("နိုးဆော်", "နှိုးဆော်"),
                    ("ကူးဆက်", "ကူးစက်"),
                    ("လျော့ချ", "လျှော့ချ")),
                new BenchmarkCase("std_mlc_reciprocal_nhit_nit", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "သူတို့၏ စွန့်လွှတ်အနှစ်နာခံမှုများသည် ဂုဏ်ယူဆရာပင်ဖြစ်ပြီး ဒီမိုကရေဆီ၏ အနစ်သာရကို ဖော်ညွှန်းသည်။",
                    ("အနှစ်နာခံ", "အနစ်နာခံ"),
                    ("ဂုဏ်ယူဆရာ", "ဂုဏ်ယူစရာ"),
                    ("ဒီမိုကရေဆီ", "ဒီမိုကရေစီ"),
                    ("အနစ်သာရ", "အနှစ်သာရ")),
                new BenchmarkCase("std_mlc_mining_resources", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "သတ္တုတွင်းလုပ်ငန်းမှ မြေအောက်သယံဇာတများကို တုဖော်ထုပ်ယူရာတွင် ပတ်ဝန်ကျင် မထိခိုက်စေရန် စီစစ်ရမည်။",
                    ("တုဖော်", "တူးဖော်"),
                    ("ထုပ်ယူ", "ထုတ်ယူ"),
                    ("ပတ်ဝန်ကျင်", "ပတ်ဝန်းကျင်"),
                    ("စီစစ်", "စိစစ်")),
                new BenchmarkCase("std_mlc_pali_doublets_1", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ခိုင်မာသော သန္နိဌာန်နှင့် အဓိဌာန်ထားရှိမှသာ လက်ရှိ ပစ္စုပန်နှင့် အနာဂါတ်တွင် အောင်မြင်မည်။",
                    ("သန္နိဌာန်", "သန္နိဋ္ဌာန်"),
                    ("အဓိဌာန်", "အဓိဋ္ဌာန်"),
                    ("ပစ္စုပန်", "ပစ္စုပ္ပန်"),
                    ("အနာဂါတ်", "အနာဂတ်")),
                new BenchmarkCase("std_mlc_pali_doublets_2", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "နှစ်ဖက် အဖွဲ့အစည်းများသည် ပဋိညာန်စာချုပ်ပါ အချက်များကို တစ်သမက်တည်း အစင်တစိုက် လိုက်နာကြသည်။",
                    ("ပဋိညာန်", "ပဋိညာဉ်"),
                    ("တစ်သမက်တည်း", "တစ်သမတ်တည်း"),
                    ("အစင်တစိုက်", "အစဉ်တစိုက်")),
                new BenchmarkCase("std_mlc_pali_doublets_3", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ပရိတ်သတ်များရှေ့တွင် သဘောထားကွဲလွှဲမှု ပြသနာများကို သံသရကင်းစင်စွာ ဖြေရှင်းပေးခဲ့သည်။",
                    ("ပရိတ်သတ်", "ပရိသတ်"),
                    ("ကွဲလွှဲ", "ကွဲလွဲ"),
                    ("ပြသနာ", "ပြဿနာ"),
                    ("သံသရ", "သံသယ")),
                new BenchmarkCase("std_relational_postposition_guard_1", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ဧရာဝတီတိုင်းတွင် ကျောငးဆိုင်ရာနှင့် သက်တမးဆိုင်ရာ အစီရင်ခံစာများကို အစိုးရ အဖွဲ့ဆိုင်ရာ ကော်မတီသို့ တင်ပြသည်။",
                    ("ကျောငး", "ကျောင်း"),
                    ("သက်တမး", "သက်တမ်း"),
                    ("အစိုးရ အဖွဲ့", "အစိုးရအဖွဲ့")),
                new BenchmarkCase("std_relational_postposition_guard_2", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.GeneralStandard,
                    "စာကြည့်တိုက်မှာ သငတန်းအကြောင်းနဲ့ ဖုနးအကြောင်း ဆွေးနွေးပြီး ကျမ်ကျင်အောင် လေ့လာကြတယ်။",
                    ("သငတန်း", "သင်တန်း"),
                    ("ဖုနး", "ဖုန်း"),
                    ("ကျမ်ကျင်", "ကျွမ်းကျင်")),
                new BenchmarkCase("std_negation_prefix_preservation", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.GeneralStandard,
                    "မိုးသည်းထန်စွာ ရွာလို့ အပြင်မထွတ်တော့ဘူး ဆိုပြီး ဆေးလိပ်နဲ့ အရက်ကိုလည်း လျှော့သာက်ပါတယ်။",
                    ("မထွတ်", "မထွက်"),
                    ("သာက်", "သောက်")),
                new BenchmarkCase("std_context_compound_rerank", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.GeneralStandard,
                    "ရေခဲသေတ္တာထဲမှာ ထည့်သိမ်းတာက အသုံးမျာတဲ့နည်းလမ်းဖြစ်ပြီး အိုဟိုင်ယိုးပြည်နယ်မှာလည်း တွေရတယ်။",
                    ("မျာ", "များ"),
                    ("ဟိုင်", "ဟိုင်း"),
                    ("တွေ", "တွေ့")),
                new BenchmarkCase("std_classifier_sacred_and_footwear", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ရှေးဟောင်း စေတီတော် လေးခု၌ ဆရာတော် သုံးယောက်အား ဖိနပ် နှစ်စီးနှင့် သင်္ကန်းများ ဆက်ကပ်လှူဒါန်းသည်။",
                    ("ခု", "ဆူ"),
                    ("ယောက်", "ပါး"),
                    ("စီး", "ရံ")),
                new BenchmarkCase("std_numeral_ta_khunit_adversarial", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ခုနှစ်ကြိမ်မြောက် ညီလာခံသို့ တနိုင်ငံလုံးမှ ကိုယ်စားလှယ်များ တစ်ဖြည်းဖြည်း ရောက်ရှိလာပြီး အမှတ်တစ်ရ မှတ်တမ်းတင်သည်။",
                    ("ခုနှစ်", "ခုနစ်"),
                    ("တနိုင်ငံလုံး", "တစ်နိုင်ငံလုံး"),
                    ("တစ်ဖြည်းဖြည်း", "တဖြည်းဖြည်း"),
                    ("အမှတ်တစ်ရ", "အမှတ်တရ")),
                new BenchmarkCase("std_admin_orthography_directives", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ဗဟိုဘဏ်မှ ထုတ်ပြန်သည့် ညွန်ကြားချက်နှင့် ရည်ညွန်းချက်များအရ ဝန်ထမ်းများ၏ စွမ်းဆောင်ရေကို အကဲဖြတ်သည်။",
                    ("ညွန်ကြား", "ညွှန်ကြား"),
                    ("ရည်ညွန်း", "ရည်ညွှန်း"),
                    ("စွမ်းဆောင်ရေ", "စွမ်းဆောင်ရည်")),
                new BenchmarkCase("std_policy_impact_practice", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ချမှတ်ထားသော မူဝါဒ၏ အကျိုးသတ်ရောက်မှုကို လက်ခံကျင့်သုံပြီး ပူးပေါင်ဆောင်ရွက်သွားမည်။",
                    ("သတ်ရောက်", "သက်ရောက်"),
                    ("ကျင့်သုံ", "ကျင့်သုံး"),
                    ("ပူးပေါင်", "ပူးပေါင်း")),
                new BenchmarkCase("std_broken_compounds_standard", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စု စာရင်း စစ်ချုပ်ရုံးနှင့် လူမှု ဖူလုံရေးအဖွဲ့တို့က ကျန်းမာရေး အာမခံအစီအစဉ်ကို အဆင့်မြှင့်တင်သည်။",
                    ("စာရင်း စစ်ချုပ်", "စာရင်းစစ်ချုပ်"),
                    ("လူမှု ဖူလုံရေး", "လူမှုဖူလုံရေး"),
                    ("ကျန်းမာရေး အာမခံ", "ကျန်းမာရေးအာမခံ")),
                new BenchmarkCase("std_stuttered_word_with_case", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ပြုပြင်ထိန်းသိမ်းရေးအဖွဲ့သည် တောင်ဘက် တိုင် တိုင်ကို စစ်ဆေးပြီး စာချုပ် စာချုပ်တွင် လက်မှတ်ရေးထိုးသည်။",
                    ("တိုင် တိုင်ကို", "တိုင်ကို"),
                    ("စာချုပ် စာချုပ်တွင်", "စာချုပ်တွင်")),
                new BenchmarkCase("std_unspaced_long_clause_1", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "သဘာဝပတ်ဝန်ကျင်ထိမ်းသိမ်းရေးကော်မတီအစည်းအဝေး၌ သစ်တောပြုန်းတီးမှုအန္တရယ်ကို ဆွေးနွေးကြသည်။",
                    ("ပတ်ဝန်ကျင်", "ပတ်ဝန်းကျင်"),
                    ("ထိမ်းသိမ်း", "ထိန်းသိမ်း"),
                    ("အန္တရယ်", "အန္တရာယ်")),
                new BenchmarkCase("std_unspaced_long_clause_2", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "အမျိုးသားပညာရေမဟာဗျူဟာစီမံကန်းအရ တက္ကသိုလ်အသီးသီး၌ သုတေသနစာတမ်းဖတ်ပွဲများ ကျင်းပလျှက်ရှိသည်။",
                    ("ပညာရေ", "ပညာရေး"),
                    ("စီမံကန်း", "စီမံကိန်း"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("std_grammar_postposition_and_attributive", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "အင်းလေးကန်သည် ရှမ်းပြည်နယ်က တည်ရှိပြီး လာမည်နှစ်တွင် ခရီးသွားဧည့်သည် တိုးတက်များပြားလာမည်။",
                    ("က", "တွင်"),
                    ("လာမည်နှစ်", "လာမည့်နှစ်")),
                new BenchmarkCase("std_judicial_withhold_and_resign", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "တရားရုံးသို့ သက်သေခံအချက်အလတ်များ မထိန်ချိန်ဘဲ တင်ပြရမည်ဖြစ်ပြီး တာဝန်မှ နှုတ်ထွက်ခွင့် လျှောက်ထားသည်။",
                    ("အချက်အလတ်", "အချက်အလက်"),
                    ("ထိန်ချိန်", "ထိမ်ချန်"),
                    ("နှုတ်ထွက်", "နုတ်ထွက်")),
                new BenchmarkCase("std_corrupted_syllable_clusters", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.GeneralStandard,
                    "ယနဲ မနက်ပိုင်း အိမ်မှုကိစ္စတွေ လုပ်ပြီး အလုပ်စဝနိ်ကြရာ သက်တောင့်သက်သာ ရှိလှပါတယ်။",
                    ("ယနဲ", "ယနေ့"),
                    ("ဝနိ်", "ဝင်"),
                    ("သက်တောင့်သက်သာ", "သက်သောင့်သက်သာ")),
                new BenchmarkCase("std_boss_constitutional_gazette", BenchmarkDifficulty.StandardHardest, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုလွှတ်တော်က အတည်ပြု ပြဌာန်းလိုက်သော ဉပဒေကြမ်းသည် နိုင်ငံတော်၏ အချုပ်အချာအာဏာ တည်တန့်ခိုင်မြဲရေးအတွက် အရေးပါသည့် ဝိသေသဏတစ်ရပ် ဖြစ်သည်.",
                    ("ပြဌာန်း", "ပြဋ္ဌာန်း"),
                    ("ဉပဒေ", "ဥပဒေ"),
                    ("အချုပ်အချာ", "အချုပ်အခြာ"),
                    ("တည်တန့်", "တည်တံ့"),
                    ("ဝိသေသဏ", "ဝိသေသန"),
                    (".", "။")),

                // =========================================================================================
                // TIER 11: HYBRID STATISTICAL, SOV CLAUSE PARSER & INTRUDER TORTURE (--hybrid, 20 Cases)
                // =========================================================================================
                new BenchmarkCase("hyb_intruder_bare_verb_tech", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ဆော့ဖ်ဝဲအသစ်တွေနဲ့ နည်းပညာ စား အသစ်တွေကို လေ့လာဖို့ အရေးကြီးပါတယ်။",
                    ("စား အသစ်", "အသစ်")),
                new BenchmarkCase("hyb_intruder_bare_verb_loyal", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "အိမ်မွေးခွေးက သခင်ကို စား အရမ်းသစ္စာရှိပြီး ခြံဝင်းကို စောင့်ရှောက်ပေးတယ်။",
                    ("စား အရမ်းသစ္စာရှိပြီး", "အရမ်းသစ္စာရှိပြီး")),
                new BenchmarkCase("hyb_intruder_noun_resign", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ရာထူးကနေ ကြောင် နုတ်ထွက်တဲ့အခါ အနာဂတ်အတွက် သေချာပြင်ဆင်ထားသင့်တယ်။",
                    ("ကြောင် နုတ်ထွက်", "နုတ်ထွက်")),
                new BenchmarkCase("hyb_intruder_noun_pain", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ဆုံးရှုံးရတဲ့ ခံစားချက်က ပွဲ အရမ်းနာကျင်ပေမဲ့ အားတင်းထားရပါမယ်။",
                    ("ပွဲ အရမ်းနာကျင်ပေမဲ့", "အရမ်းနာကျင်ပေမဲ့")),
                new BenchmarkCase("hyb_intruder_noun_fire_escape", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "မီးလောင်မှုဖြစ်ပွားပါက စားပွဲ အမြန်ဆုံးထွက်ပြေးပြီး အရေးပေါ်ဌာနကို အကြောင်းကြားရမယ်။",
                    ("စားပွဲ အမြန်ဆုံးထွက်ပြေးပြီး", "အမြန်ဆုံးထွက်ပြေးပြီး")),
                new BenchmarkCase("hyb_intruder_noun_hope", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "အခက်အခဲတွေကြားကနေ ခွေး မျှော်လင့်ချက်တွေက လူငယ်တွေကို အားအင်ပေးနေတယ်။",
                    ("ခွေး မျှော်လင့်ချက်တွေက", "မျှော်လင့်ချက်တွေက")),
                new BenchmarkCase("hyb_intruder_noun_quotative", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ရေအားလျှပ်စစ်ဟာ သန့်ရှင်းတဲ့ စွမ်းအင် စားပွဲလို့ ပညာရှင်တွေက ယူဆကြတယ်။",
                    ("စွမ်းအင် စားပွဲ", "စွမ်းအင်")),
                new BenchmarkCase("hyb_sov_extraposition_locative", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "အလုပ်ပြီးရင် အစ်ကို အနားယူတယ် အိမ်မှာ ခဏ ဆိုတာ အိမ်သားတွေ သိကြတယ်။",
                    ("အနားယူတယ် အိမ်မှာ ခဏ", "အိမ်မှာ ခဏ အနားယူတယ်")),
                new BenchmarkCase("hyb_sov_extraposition_market", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "မနက်ဖြန် ကျွန်မ သွားမယ် ဈေးကို စောစော။",
                    ("သွားမယ် ဈေးကို စောစော", "ဈေးကို စောစော သွားမယ်")),
                new BenchmarkCase("hyb_sov_extraposition_glad", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ဒီနေ့ အခမ်းအနားမှာ ဝမ်းသာပါတယ် တွေ့ရတာ။",
                    ("ဝမ်းသာပါတယ် တွေ့ရတာ", "တွေ့ရတာ ဝမ်းသာပါတယ်")),
                new BenchmarkCase("hyb_sov_extraposition_concessive", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ဘဝမှာ ပြန်ထဖို့တော့လိုတယ် စိတ်ဓာတ်ကျရပေမဲ့။",
                    ("ပြန်ထဖို့တော့လိုတယ် စိတ်ဓာတ်ကျရပေမဲ့", "စိတ်ဓာတ်ကျရပေမဲ့ ပြန်ထဖို့တော့လိုတယ်")),
                new BenchmarkCase("hyb_preverbal_comparative_inversion", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "နေရောင်ခြည်စွမ်းအင်က ကျောက်မီးသွေးနဲ့ ရေနံလို ပိုပြီး စွမ်းအင်တွေထက် ရေရှည်တည်တံ့တယ်။",
                    ("ပိုပြီး စွမ်းအင်တွေထက်", "စွမ်းအင်တွေထက် ပိုပြီး")),
                new BenchmarkCase("hyb_preverbal_quantifier_and_postp", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "မွေးနေ့ပွဲတိုင်း အများကြီး ပျော်စရာအမှတ်တရတွေ ရှိပြီး မူးယစ်ဆေးဝါးအကြောင်း အန္တရာယ် သိထားသင့်ပါတယ်။",
                    ("အများကြီး ပျော်စရာအမှတ်တရတွေ", "ပျော်စရာအမှတ်တရတွေ အများကြီး"),
                    ("မူးယစ်ဆေးဝါးအကြောင်း အန္တရာယ်", "မူးယစ်ဆေးဝါးအန္တရာယ်အကြောင်း")),
                new BenchmarkCase("hyb_register_concord_literary_subj_1", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "သိပ္ပံပညာနှင့် ရှာဖွေတွေ့ရှိမှုများသည် အနာဂတ်အတွက် အရေးကြီးတယ်။",
                    ("တယ်", "သည်")),
                new BenchmarkCase("hyb_register_concord_literary_subj_2", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ရဟန်းတော်များနှင့် ကျောင်းတိုက်များသည် မြန်မာ့ယဉ်ကျေးမှု၏ အစိတ်အပိုင်းတစ်ခု ဖြစ်တယ်။",
                    ("ဖြစ်တယ်", "ဖြစ်သည်")),
                new BenchmarkCase("hyb_confusion_pcode_and_postal", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.FormalOfficial,
                    "အဆိုပါ မြို့နယ်၏ စာတိုက်ကုတ်မှာ ၁၁၀၄၁ ဖြစ်ပြီး လျှို့ဝှက်ကုတ်နံပါတ်ကိုလည်း စိစစ်သည်။",
                    ("ကုတ်", "ကုဒ်"),
                    ("ကုတ်", "ကုဒ်")),
                new BenchmarkCase("hyb_confusion_tae_vs_htae", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "သူဟာ ငယ်ငယ်ကထဲက တစ်ယောက်ထဲ ကြိုးစားလေ့လာခဲ့လို့ ထူးချွန်စွာ အောင်မြင်ခဲ့တယ်။",
                    ("ငယ်ငယ်ကထဲက", "ငယ်ငယ်ကတည်းက"),
                    ("တစ်ယောက်ထဲ", "တစ်ယောက်တည်း")),
                new BenchmarkCase("hyb_stacked_particles_and_double_ko", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "တရုတ်ရဲ့ နဲ့ အိမ်နီးချင်းနိုင်ငံများတွင် ခေတ်မီစိုက်ပျိုးရေးနည်းပညာတွေကို အထွက်နှုန်းကို တိုးမြှင့်ပေးတယ်။",
                    ("တရုတ်ရဲ့ နဲ့", "တရုတ်ရဲ့"),
                    ("ခေတ်မီစိုက်ပျိုးရေးနည်းပညာတွေကို", "ခေတ်မီစိုက်ပျိုးရေးနည်းပညာတွေက")),
                new BenchmarkCase("hyb_permissive_catches_real_typos", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.ColloquialPermissive,
                    "ကျနော် စျေးမှာ ပန်းသီးတလုံး ဝယ်ပီး အိမ်ကို အမြန်ပြန်လာခဲ့ပါတယ် ဥက္ကဌခင်ဗျာ။",
                    ("ဝယ်ပီး", "ဝယ်ပြီး"),
                    ("ဥက္ကဌ", "ဥက္ကဋ္ဌ")),
                new BenchmarkCase("hyb_boss_multi_layer_hybrid", BenchmarkDifficulty.HybridStatistical, ProofingStyleMode.GeneralStandard,
                    "ဆရာတော်များနှင့် ဒါယကာများသည် စာတိုက်ကုတ်အလိုက် စာရင်းပြုစုပြီး တစ်ဦးထဲ မကျန်စေရန် ဆောင်ရွတ်ကြတယ်။",
                    ("ကုတ်", "ကုဒ်"),
                    ("တစ်ဦးထဲ", "တစ်ဦးတည်း"),
                    ("ဆောင်ရွတ်", "ဆောင်ရွက်"),
                    ("တယ်", "သည်")),

                // =========================================================================================
                // TIER 12: NATIONAL APEX BENCHMARK (--national / --apex, 25 Multi-Domain Cases, 78 Spans)
                // =========================================================================================
                new BenchmarkCase("nat_gazette_vs_announcement_dual", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ဝန်ကြီးဌာနက အမိန့်ကြေညာစာနှင့် သတင်းထုတ်ပြန်ကြော်ငြာချက်ကို တရားဝင် ကြော်ငြာခဲ့သည်။",
                    ("အမိန့်ကြေညာစာ", "အမိန့်ကြော်ငြာစာ"),
                    ("သတင်းထုတ်ပြန်ကြော်ငြာချက်", "သတင်းထုတ်ပြန်ကြေညာချက်"),
                    ("ကြော်ငြာ", "ကြေညာ")),
                new BenchmarkCase("nat_revenue_tax_assessment_profit", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ကုန်သွယ်လုပ်ငန်းခွန် စည်းကျပ်ရာတွင် ကုမ္ပဏီ၏ အကျိုးအမြက်နှင့် ဘဏ္ဏာရေးရှင်းတမ်းကို စီစစ်သည်။",
                    ("စည်းကျပ်", "စည်းကြပ်"),
                    ("အကျိုးအမြက်", "အကျိုးအမြတ်"),
                    ("ဘဏ္ဏာရေး", "ဘဏ္ဍာရေး"),
                    ("စီစစ်", "စိစစ်")),
                new BenchmarkCase("nat_judicial_prosecution_petition", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "တရားရုံးသို့ အယူခံ လျောက်ထားရာ၌ တရားဆွဲဆိုမှုနှင့် ဆွဲချက်တင်မှုတို့ မနှောက်နှေးစေရန် ဆောင်ရွက်သည်။",
                    ("လျောက်ထား", "လျှောက်ထား"),
                    ("တရားဆွဲဆို", "တရားစွဲဆို"),
                    ("ဆွဲချက်တင်", "စွဲချက်တင်"),
                    ("နှောက်နှေး", "နှောင့်နှေး")),
                new BenchmarkCase("nat_public_service_fulfill_obstacle", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ပြည်သူများ၏ လိုအပ်ချက်ကို အနှောက်အယှက် မရှိစေဘဲ အချိန်မှီ ဖြည့်စည်းပေးလျှက်ရှိသည်။",
                    ("အနှောက်အယှက်", "အနှောင့်အယှက်"),
                    ("အချိန်မှီ", "အချိန်မီ"),
                    ("ဖြည့်စည်း", "ဖြည့်ဆည်း"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("nat_education_upgrade_award_ceremony", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "တက္ကသိုလ်သုတေသန အဆင့်မြင့်တင်ရေး အခမ်းအနားတွင် ထူးချွန်ဆု ချီးမြင့်ခြင်းကို ကျင်းပပါတယ်။",
                    ("မြင့်တင်", "မြှင့်တင်"),
                    ("ချီးမြင့်", "ချီးမြှင့်"),
                    ("ပါတယ်", "ပါသည်")),
                new BenchmarkCase("nat_geology_mineral_excavation_lab", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ဘူမိဗေဒအဖွဲ့သည် သယံဇာတနှင့် သတ္တုနမူနာများ တုဖော်ရာတွင် အနစ်သာရရှိသော ဓါတ်ခွဲချက်ကို တင်ပြသည်။",
                    ("တုဖော်", "တူးဖော်"),
                    ("အနစ်သာရ", "အနှစ်သာရ"),
                    ("ဓါတ်ခွဲ", "ဓာတ်ခွဲ")),
                new BenchmarkCase("nat_gad_postal_code_and_locative", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "အင်းလေးကန်သည် ရှမ်းပြည်နယ်က တည်ရှိပြီး မြို့နယ်၏ စာတိုက်သင်္ကေတကုတ်နှင့် ပို့စ်ကုတ်ကို မှတ်တမ်းတင်သည်။",
                    ("က", "တွင်"),
                    ("စာတိုက်သင်္ကေတကုတ်", "စာတိုက်သင်္ကေတကုဒ်"),
                    ("ပို့စ်ကုတ်", "ပို့စ်ကုဒ်")),
                new BenchmarkCase("nat_nrc_card_type_and_wa_zero", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "လျောက်လွှာရှင်၏ မှတ်ပုံတင်အမှတ် ၉/ကဆန(ပြူ)၁၂ဝ၄၅၆ ကို တိုက်ဆိုင်စစ်ဆေးသည်။",
                    ("လျောက်လွှာ", "လျှောက်လွှာ"),
                    ("ပြူ", "ပြု"),
                    ("၁၂ဝ၄၅၆", "၁၂၀၄၅၆")),
                new BenchmarkCase("nat_hybrid_zawgyi_unicode_paste_1", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "ကလေးတွေကြိုက်တဲ့ မဳံ့ပဲသရေစာတွေက လက်ဆောင် တႏခုအဖြစ် ရွေးချယ်လို့ ကောင်းပါတယ်။",
                    ("မဳံ့ပဲသရေစာတွေက", "မုန့်ပဲသရေစာတွေက"),
                    ("တႏခုအဖြစ်", "တစ်ခုအဖြစ်")),
                new BenchmarkCase("nat_hybrid_zawgyi_unicode_paste_2", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "စမတ် ဖဳံးတွေမှာ လုံခြုံရေးစနစ်ဟာ အဓိကနႏရာကနေ ပါဝင်နေပါတယ်။",
                    ("ဖဳံးတွေမှာ", "ဖုန်းတွေမှာ"),
                    ("အဓိကနႏရာကနေ", "အဓိကနေရာကနေ")),
                new BenchmarkCase("nat_context_compound_rerank_gunpyu", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "နိုင်ငံတော်အတွက် အနှစ်နာခံ ဆောင်ရွတ်ခဲ့သူများကို အထူးကုဏ်ပြုမှတ်တမ်းတင်သည်။",
                    ("အနှစ်နာခံ", "အနစ်နာခံ"),
                    ("ဆောင်ရွတ်", "ဆောင်ရွက်"),
                    ("ကုဏ်", "ဂုဏ်")),
                new BenchmarkCase("nat_context_tiffin_carrier_plural", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "မနက်စာအတွက် ထမင်းချိန့်တွေကို သေချာဆေးကြောပြီး ထုပ်ယူလာခဲ့တယ်။",
                    ("ချိန့်", "ချိုင့်"),
                    ("ထုပ်ယူ", "ထုတ်ယူ")),
                new BenchmarkCase("nat_ecommerce_gerund_and_shopping", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "အွန်လိုင်းစျေးဝင်တာဟာ စျေးဝယ်ချင်အတွက် အချိန်ကုန် သက်သာစေပါတယ်။",
                    ("စျေးဝင်", "ဈေးဝယ်"),
                    ("စျေးဝယ်ချင်", "ဈေးဝယ်ခြင်း")),
                new BenchmarkCase("nat_partitive_genitive_asate_apaing", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "အမှားကနေ သင်ခန်းစာယူတတ်ခြင်းဟာ လူ့ဘဝက အစိတ်အပိုင်းတစ်ခုပဲ ဖြစ်ပါတယ်။",
                    ("က", "ရဲ့")),
                new BenchmarkCase("nat_statistical_intruder_post_ko", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "လူငယ်တိုင်း မူးယစ်ဆေးဝါးအန္တရာယ်ကို တစ်ခု သိရှိနားလည်သင့်ပါတယ်။",
                    ("တစ်ခု သိရှိနားလည်သင့်ပါတယ်", "သိရှိနားလည်သင့်ပါတယ်")),
                new BenchmarkCase("nat_statistical_intruder_city_job", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "ဆော့ဖ်ဝဲရေးသားခြင်းဆိုတာ ကျွမ်းကျင်တဲ့ မြို့ အလုပ်တစ်ခုဖြစ်ပါတယ်။",
                    ("မြို့ အလုပ်တစ်ခုဖြစ်ပါတယ်", "အလုပ်တစ်ခုဖြစ်ပါတယ်")),
                new BenchmarkCase("nat_statistical_intruder_copula_lay", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ကချင်ပြည်နယ်သည် ကျောက်စိမ်းအများဆုံး ထွက်ရှိရာလည်း လေဖြစ်သည်။",
                    ("လေဖြစ်သည်", "ဖြစ်သည်")),
                new BenchmarkCase("nat_causative_transitive_trio", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "မသမာသူများက သက်သေစာရွက်စာတမ်းများကို ပျောက်ဖျက်၍ ဖုန်းလိုင်းများကို ပြတ်တောက်ခဲ့သည်။",
                    ("ပျောက်ဖျက်", "ဖျောက်ဖျက်"),
                    ("ပြတ်တောက်", "ဖြတ်တောက်")),
                new BenchmarkCase("nat_sov_inversion_and_comparative", BenchmarkDifficulty.NationalApex, ProofingStyleMode.GeneralStandard,
                    "နေရောင်ခြည်စွမ်းအင်ဟာ ကျောက်မီးသွေးထက် ပိုပြီး သဘာဝပတ်ဝန်းကျင်ထက် သန့်ရှင်းလို့ မနက်ဖြန် သွားမယ် ဈေးကို စောစော။",
                    ("ပိုပြီး သဘာဝပတ်ဝန်းကျင်ထက်", "သဘာဝပတ်ဝန်းကျင်ထက် ပိုပြီး"),
                    ("သွားမယ် ဈေးကို စောစော", "ဈေးကို စောစော သွားမယ်")),
                new BenchmarkCase("nat_parliamentary_constitutional_act", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုလွှတ်တော်က ပြဌာန်းသော ဉပဒေအရ နိုင်ငံသားအခွင့်အရေးကို တစ်သမှတ်တည်း ကာကွယ်စောင့်လျှောက်ရမည်။",
                    ("ပြဌာန်း", "ပြဋ္ဌာန်း"),
                    ("ဉပဒေ", "ဥပဒေ"),
                    ("တစ်သမှတ်တည်း", "တစ်သမတ်တည်း"),
                    ("စောင့်လျှောက်", "စောင့်ရှောက်")),
                new BenchmarkCase("nat_monastic_honorific_and_classifier", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ပရိတ်သတ်များက ဆရာတော် သုံးယောက်အား ဝတ္တုငွေများ ဆက်ကပ်လှူဒါန်းကြသည်။",
                    ("ပရိတ်သတ်", "ပရိသတ်"),
                    ("ယောက်", "ပါး"),
                    ("ဝတ္တု", "ဝတ္ထု")),
                new BenchmarkCase("nat_healthcare_clinical_report", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ဆေးရုံအုပ်ကြီးက ကူးစက်ရောဂါ ကာကွယ်ဖိုအတွက် ဆေးဝါးလိုအပ်ချက်များကို အချိန်မှီ ဖြည့်စည်းပေးခဲ့ပါတယ်။",
                    ("ဖို", "ဖို့"),
                    ("အချိန်မှီ", "အချိန်မီ"),
                    ("ဖြည့်စည်း", "ဖြည့်ဆည်း"),
                    ("ပါတယ်", "ပါသည်")),
                new BenchmarkCase("nat_broken_space_and_virama_stack", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "စစ်တက္ ကသိုလ်နှင့် ပညာရေ ဝန်ကြီး ဌာနတို့ ပူးပေါင်း၍ သုတေသနစာတမ်း ဖတ်ကြားသည်။",
                    ("စစ်တက္ ကသိုလ်", "စစ်တက္ကသိုလ်"),
                    ("ပညာရေ", "ပညာရေး"),
                    ("ဝန်ကြီး ဌာန", "ဝန်ကြီးဌာန")),
                new BenchmarkCase("nat_boss_ministry_audit_six_errors", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "အမိန့်ကြေညာစာအရ ဝင်ငွေခွန် စည်းကျပ်မှုနှင့် တရားဆွဲဆိုမှုများ မနှောက်နှေးစေရန် အဆင့်မြင့်တင် ဆောင်ရွတ်လျှက်ရှိသည်။",
                    ("အမိန့်ကြေညာစာ", "အမိန့်ကြော်ငြာစာ"),
                    ("စည်းကျပ်", "စည်းကြပ်"),
                    ("တရားဆွဲဆို", "တရားစွဲဆို"),
                    ("နှောက်နှေး", "နှောင့်နှေး"),
                    ("မြင့်တင်", "မြှင့်တင်"),
                    ("ဆောင်ရွတ်", "ဆောင်ရွက်"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("nat_boss_grand_synthesis_seven_errors", BenchmarkDifficulty.NationalApex, ProofingStyleMode.FormalOfficial,
                    "ရှမ်းပြည်နယ်က တည်ရှိသော မြို့နယ်များ၏ စာတိုက်သင်္ကေတကုတ်နှင့် လျောက်ထားလွှာများကို အနှောက်အယှက် ကင်းစွာ အချိန်မှီ စီစစ်ပြီး သတင်းထုတ်ပြန်ကြော်ငြာချက် ထုတ်ပြန်သည်.",
                    ("က", "တွင်"),
                    ("စာတိုက်သင်္ကေတကုတ်", "စာတိုက်သင်္ကေတကုဒ်"),
                    ("လျောက်ထား", "လျှောက်ထား"),
                    ("အနှောက်အယှက်", "အနှောင့်အယှက်"),
                    ("အချိန်မှီ", "အချိန်မီ"),
                    ("စီစစ်", "စိစစ်"),
                    ("သတင်းထုတ်ပြန်ကြော်ငြာချက်", "သတင်းထုတ်ပြန်ကြေညာချက်"),
                    (".", "။")),

                // =========================================================================================
                // TIER 13: LIVE *.gov.mm PRECISION & MULTI-SYLLABLE REPAIR (--gov-live, 12 Cases, 30 Spans)
                // =========================================================================================
                new BenchmarkCase("gov_live_most_dropped_geminate_takkatho", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုဝန်ကြီးသည် သတင်းအချက်အလက်နည်းပညာတ္ကသိုလ်သို့ သွားရောက်ကြည်ရှုစစ်ဆေးသည်။",
                    ("တ္ကသိုလ်", "တက္ကသိုလ်"),
                    ("ကြည်ရှုစစ်ဆေး", "ကြည့်ရှုစစ်ဆေး")),
                new BenchmarkCase("gov_live_moali_virama_medial_wa_khwint", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုဝန်ကြီး တွေ့ဆုံခ္ငင့်နှင့် အစီရင်ခံစာများ တင်ပြရေး ဆွေးနွေးသည်။",
                    ("ခ္ငင့်", "ခွင့်")),
                new BenchmarkCase("gov_live_ayeyarwady_dropped_vowel_and_fridge", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ရောဝတီတိုင်းဒေသကြီးတွင် ဆိုလာပြားနှင့် ရေခဲသတ္တာများ တပ်ဆင်ပေးလျှက်ရှိသည်။",
                    ("ရောဝတီ", "ဧရာဝတီ"),
                    ("ရေခဲသတ္တာ", "ရေခဲသေတ္တာ"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("gov_live_mminsurance_assimilation_chinchint", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ပြည်သူများ အာမခံထားခြင်းခြင့် ဆုံးရုံးမှုများကို ကြိုတင်ကာကွယ်နိုင်သည်။",
                    ("ခြင့်", "ဖြင့်"),
                    ("ဆုံးရုံးမှု", "ဆုံးရှုံးမှု")),
                new BenchmarkCase("gov_live_mrtv_plural_missing_visarga_mya", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ရေစီးရေလာကောင်းမွန်ရေး လုပ်ငန်းမျာအား ကွင်းဆင်းကြည့်ရှု့စစ်ဆေးသည်။",
                    ("လုပ်ငန်းမျာ", "လုပ်ငန်းများ"),
                    ("ကြည့်ရှု့", "ကြည့်ရှု")),
                new BenchmarkCase("gov_live_oagmac_surgical_zwsp_pupawng", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ပြည်ထောင်စုစာရင်းစစ်ချုပ်ရုံးနှင့်ဥပဒေရေးရာဝန်ကြီးဌာနတို့ပူး\u200Cပေါင်း၍ ဥက္ကဌက စိစစ်သည်။",
                    ("ပူး\u200Cပေါင်း", "ပူးပေါင်း"),
                    ("ဥက္ကဌ", "ဥက္ကဋ္ဌ")),
                new BenchmarkCase("gov_live_moe_surgical_zwsp_election", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ပါတီစုံဒီမိုကရေစီအထွေထွေ\u200Cရွေးကောက်ပွဲတွင် အစိရင်ခံစာများ ထုတ်ပြန်ကြေငြာသည်။",
                    ("အထွေထွေ\u200Cရွေးကောက်ပွဲ", "အထွေထွေရွေးကောက်ပွဲ"),
                    ("အစိရင်ခံစာ", "အစီရင်ခံစာ"),
                    ("ကြေငြာ", "ကြေညာ")),
                new BenchmarkCase("gov_live_csostat_indicator_and_lookalike", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "အမျိုးသားအဆင့်ညွှန်ကိန်း မူဘောင်နှင့် ပဥ္စမအကြိမ် အစည်းဝေး ကျင်းပသည်။",
                    ("ညွှန်ကိန်း", "ညွှန်းကိန်း"),
                    ("ပဥ္စမ", "ပဉ္စမ"),
                    ("အစည်းဝေး", "အစည်းအဝေး")),
                new BenchmarkCase("gov_live_unionsupremecourt_pyinae_shudaunt", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "တိုင်းဒေသကြီးနှင့် ပြည့်နယ်တရားလွှတ်တော်များက ဉပဒေရှုဒေါင့်မှ လေ့လာသည်။",
                    ("ပြည့်နယ်", "ပြည်နယ်"),
                    ("ဉပဒေ", "ဥပဒေ"),
                    ("ရှုဒေါင့်", "ရှုထောင့်")),
                new BenchmarkCase("gov_live_bank_code_and_exclusivity_taung", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.GeneralStandard,
                    "ဘဏ်ကုတ်နှင့် အရောင်ကုတ်များကို တစ်ဖက်ထဲကိုပဲ သတ်မှတ်ထားပါတယ်။",
                    ("ကုတ်", "ကုဒ်"),
                    ("ကုတ်", "ကုဒ်"),
                    ("တစ်ဖက်ထဲ", "တစ်ဖက်တည်း")),
                new BenchmarkCase("gov_live_cargo_container_and_safe_box", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ဆိပ်ကမ်းရှိ ကုန်သတ္တာများနှင့် မီးခံသတ္တာများကို တန်ဘိုးတွက်ချက် စစ်ဆေးသည်။",
                    ("ကုန်သတ္တာ", "ကုန်သေတ္တာ"),
                    ("မီးခံသတ္တာ", "မီးခံသေတ္တာ"),
                    ("တန်ဘိုး", "တန်ဖိုး")),
                new BenchmarkCase("gov_live_boss_synthesis_six_errors", BenchmarkDifficulty.GovLivePrecision, ProofingStyleMode.FormalOfficial,
                    "ရောဝတီတိုင်းဒေသကြီးရှိ နည်းပညာတ္ကသိုလ်သို့ တွေ့ဆုံခ္ငင့်နှင့် အာမခံထားခြင်းခြင့် ရေခဲသတ္တာများ ပေးအပ်လျှက်ရှိသည်။",
                    ("ရောဝတီ", "ဧရာဝတီ"),
                    ("တ္ကသိုလ်", "တက္ကသိုလ်"),
                    ("ခ္ငင့်", "ခွင့်"),
                    ("ခြင့်", "ဖြင့်"),
                    ("ရေခဲသတ္တာ", "ရေခဲသေတ္တာ"),
                    ("လျှက်", "လျက်")),

                // =========================================================================================
                // TIER 14: CATACLYSMIC NIGHTMARE ULTRA-TORTURE (--cataclysm / --nightmare-ultra, 26 Cases, 108 Spans)
                // =========================================================================================
                new BenchmarkCase("cat_glued_multi_ascii_a_party_and_kinzi", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "ထို့ပြင် A မျိုးသားဒီမိုကရေစီA ဖွဲ့ချုပ်Aား သကေင်္တသစ်ဖြင့် ဖိတ်ကြားလျှက်ရှိသည်။",
                    ("A မျိုးသားဒီမိုကရေစီA ဖွဲ့ချုပ်Aား", "အမျိုးသားဒီမိုကရေစီအဖွဲ့ချုပ်အား"),
                    ("သကေင်္တ", "သင်္ကေတ"),
                    ("လျှက်", "လျက်")),
                new BenchmarkCase("cat_glued_multi_ascii_a_intl_org_zwsp", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "A ပြည်ပြည်ဆိုင်ရာA ဖွဲ့A စည်းAား ပူး\u200Cပေါင်း၍ အစိရင်ခံစာ တင်ပြသည်။",
                    ("A ပြည်ပြည်ဆိုင်ရာA ဖွဲ့A စည်းAား", "အပြည်ပြည်ဆိုင်ရာအဖွဲ့အစည်းအား"),
                    ("ပူး\u200Cပေါင်း", "ပူးပေါင်း"),
                    ("အစိရင်ခံစာ", "အစီရင်ခံစာ")),
                new BenchmarkCase("cat_compound_inside_sov_ambulance", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "အတန်းပြီးနောက် သင်ကြားရေးအဖွဲ့က ရှင်းပြခဲ့သည် လူနာတင် ယာဉ်ကို အသေးစိတ် ဟု ကျောင်းသားများက မှတ်သားကြသည်။",
                    ("ရှင်းပြခဲ့သည် လူနာတင် ယာဉ်ကို အသေးစိတ်", "လူနာတင်ယာဉ်ကို အသေးစိတ် ရှင်းပြခဲ့သည်")),
                new BenchmarkCase("cat_sov_extraposition_before_sopri_and_typo", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "အလုပ်ပြီးရင် အစ်ခို အနားယူတယ် အိမ်မှာ ခဏ ဆိုပြီး မတ်ထားတယ်။",
                    ("အစ်ခို", "အစ်ကို"),
                    ("အနားယူတယ် အိမ်မှာ ခဏ", "အိမ်မှာ ခဏ အနားယူတယ်"),
                    ("မတ်", "မှတ်")),
                new BenchmarkCase("cat_mimu_village_place_code_census", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "ပန်းတိမ်ကုန်းကျေးရွာအုပ်စု၌ တည်ရှိပြီး ရွာနေရာကုတ်မှာ ၁၇၄၂၀၁ ဖြစ်၍ သန်းခောင်စာရင်းအရ စုစုပောင်း ၅၀၀ ဦးရှိသည်။",
                    ("ကုတ်", "ကုဒ်"),
                    ("သန်းခောင်စာရင်း", "သန်းခေါင်စာရင်း"),
                    ("စုစုပောင်း", "စုစုပေါင်း")),
                new BenchmarkCase("cat_gad_township_district_pcode_and_nrc", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "ခရိုင်ကုတ်နှင့် မြို့နယ်ကုတ်အလိုက် မှတ်ပုံတင် ၉/ကဆန(ပြူ)၁၂ဝ၄၅၆ ကို စီစစ်သည်။",
                    ("ကုတ်", "ကုဒ်"),
                    ("ကုတ်", "ကုဒ်"),
                    ("ပြူ", "ပြု"),
                    ("၁၂ဝ၄၅၆", "၁၂၀၄၅၆"),
                    ("စီစစ်", "စိစစ်")),
                new BenchmarkCase("cat_obstruent_hahto_phonotactic_market", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "စြေးမှား ဘာာတွေ သှားဝယ့်ကြမလဲ ဆိုတာ ပန်းတွေ ပှင့်ချိန်မှာ မေးတယ်။",
                    ("စြေးမှား", "ဈေးမှာ"),
                    ("ဘာာ", "ဘာ"),
                    ("သှား", "သွား"),
                    ("ဝယ့်", "ဝယ်"),
                    ("ပှင့်", "ပွင့်")),
                new BenchmarkCase("cat_copula_drop_partitive_ka_and_payment", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "စတော့ဈေးကွက်ဆိုတာ စီးပွားရေးက အစိတ်အပိုင်းတစ်ခုပဲ။ ယဉ်ကျေးမှု အထောက်အထားတွေလည်း ရှိပါတယ်.",
                    ("က", "ရဲ့"),
                    ("ယဉ်ကျေးမှု အထောက်အထား", "ယဉ်ကျေးမှုအထောက်အထား"),
                    (".", "။")),
                new BenchmarkCase("cat_cultural_heritage_and_identity_compounds", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "သမိုင်းနဲ့ စာ ကြည့်တိုက်တွေဟာ ယဉ်ကျေးမှု အထောက်အထားရဲ့ အနစ်သာရ ဖြစ်ပါတယ်။",
                    ("စာ ကြည့်တိုက်", "စာကြည့်တိုက်"),
                    ("ယဉ်ကျေးမှု အထောက်အထား", "ယဉ်ကျေးမှုအထောက်အထား"),
                    ("အနစ်သာရ", "အနှစ်သာရ")),
                new BenchmarkCase("cat_topic_kathaw_and_beach_relaxation", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "ခရီးသွားတွေကသော ကမ်းခြေမှာ အပန်းဖြေနားထောင်ကြပြီး ထမင်းချိန့်တွေ ထုပ်ယူလာတယ်။",
                    ("သော", "တော့"),
                    ("နားထောင်", "အနားယူ"),
                    ("ချိန့်", "ချိုင့်"),
                    ("ထုပ်ယူ", "ထုတ်ယူ")),
                new BenchmarkCase("cat_negative_pollution_disease_totak_collocation", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "မြို့ပြတွေမှာ ဆူညံသံညစ်ညမ်းမှုက နေ့စဉ်နဲ့အမျှ တိုးတက်လာပြီး စိတ်ဖိစိမှုတွေ များလာတယ်။",
                    ("တိုးတက်လာ", "တိုးပွားလာ"),
                    ("စိတ်ဖိစိမှု", "စိတ်ဖိစီးမှု")),
                new BenchmarkCase("cat_oxymoronic_verbs_and_responsibility", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "တိရစ္ဆာန်တွေကို ဖျက်ဆီးစောင့်ရှောက်သင့်ပြီး ခေါင်းဆောင်တစ်ယောက်ဟာ တာဝန်ယူစိတ်ဓာတ်ပျော့ညံ့ရမယ်။",
                    ("ဖျက်ဆီး", "ဂရုစိုက်"),
                    ("ပျော့ညံ့", "ပြင်းထန်")),
                new BenchmarkCase("cat_oxymoronic_reading_and_job_opportunities", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "စာဖတ်ခြင်းဖြင့် စိတ်အဆင်းရဲပြီး ပျော်ရွှင်နိုင်သလို အလုပ်အကိုင်အခွင့်အရေးတွေလည်း ပိုများစေတယ်။",
                    ("စိတ်အဆင်းရဲပြီး", "စိတ်အပန်းဖြေပြီး"),
                    ("အခွင့်အရေး", "အခွင့်အလမ်း")),
                new BenchmarkCase("cat_bagan_architecture_and_historical_tense", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "ပုဂံခေတ်ရဲ့ ဗိသုကာလက်ရာတွေဟာ ယနေ့အထိ ကြီးကျယ်လှပါတယ်။ အနော်ရထာမင်းကြီး လက်ထက်တွင် စေတီ သုံးယောက် တည်ထားဖြစ်ပါလိမ့်မယ်။",
                    ("ကြီးကျယ်", "ခမ်းနား"),
                    ("ယောက်", "ဆူ"),
                    ("ဖြစ်ပါလိမ့်မယ်", "ဖြစ်ပါသည်")),
                new BenchmarkCase("cat_ablative_source_satin_and_purposive_pho", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "ပျော်ရွှင်မှုဆိုတာ ရိုးရှင်းတဲ့အရာလေးတွေကို စတင်နိုင်ပြီး အသေးစိတ် သိတဲ့သူတွေ မန့်ပေးပို့ မျှော်လင့်ပါတယ်။",
                    ("ကို", "ကနေ"),
                    ("ပေးပို့", "ပေးဖို့")),
                new BenchmarkCase("cat_hybrid_zawgyi_paste_and_converb_ko", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "ဒီစမတ် ဖဳံးနဲ့ မဳံ့ပဲသရေစာကို ငါ့အတွက် ဝယ်ကို ပေးတယ် ဆိုတာ တွေရတယ်။",
                    ("ဖဳံးနဲ့", "ဖုန်းနဲ့"),
                    ("မဳံ့ပဲသရေစာကို", "မုန့်ပဲသရေစာကို"),
                    ("ဝယ်ကို", "ဝယ်ပြီး"),
                    ("တွေ", "တွေ့")),
                new BenchmarkCase("cat_monastic_clergy_honorific_classifier_stack", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "ပရိတ်သတ်များက ဆရာတော်ကြီး ငါးယောက်သည် ကျောင်းတိုက်၌ ထမင်းစားပြီး ဝတ္တုငွေများ ကပ်လှူကြောင်း လျောက်ထားသည်။",
                    ("ပရိတ်သတ်", "ပရိသတ်"),
                    ("ယောက်", "ပါး"),
                    ("ထမင်းစား", "ဆွမ်းဘုဉ်းပေး"),
                    ("ဝတ္တု", "ဝတ္ထု"),
                    ("လျောက်ထား", "လျှောက်ထား")),
                new BenchmarkCase("cat_causative_simplex_trio_and_broken_patsint", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "မသမာသူများက စစ်တက္ ကသိုလ်အနီး သဘာဝပတ်ဝန်းကျင်ကို ပျက်စီး၍ ဖုန်းလိုင်းများကို ပြတ်တောက်ကာ သက်သေများကို ပျောက်ဖျက်ခဲ့သည်။",
                    ("စစ်တက္ ကသိုလ်", "စစ်တက္ကသိုလ်"),
                    ("ပျက်စီး", "ဖျက်ဆီး"),
                    ("ပြတ်တောက်", "ဖြတ်တောက်"),
                    ("ပျောက်ဖျက်", "ဖျောက်ဖျက်")),
                new BenchmarkCase("cat_gazette_proclamation_revenue_judicial_stack", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "အမိန့်ကြေညာစာအရ ကုန်သွယ်လုပ်ငန်းခွန် စည်းကျပ်မှုနှင့် တရားဆွဲဆို ဆွဲချက်တင်မှုများ မနှောက်နှေးစေရန် သတင်းထုတ်ပြန်ကြော်ငြာချက် ထုတ်ပြန်သည်။",
                    ("အမိန့်ကြေညာစာ", "အမိန့်ကြော်ငြာစာ"),
                    ("စည်းကျပ်", "စည်းကြပ်"),
                    ("တရားဆွဲဆို", "တရားစွဲဆို"),
                    ("ဆွဲချက်တင်", "စွဲချက်တင်"),
                    ("နှောက်နှေး", "နှောင့်နှေး"),
                    ("သတင်းထုတ်ပြန်ကြော်ငြာချက်", "သတင်းထုတ်ပြန်ကြေညာချက်")),
                new BenchmarkCase("cat_wh_interrogative_polar_clash_and_creaky_pronoun", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "သူ့သည် မနက်ဖြန် အစည်းဝေးသို့ အဘယ်ကြောင့် မတက်ရောက်ခဲ့ပါသလား။",
                    ("သူ့သည်", "သူသည်"),
                    ("အစည်းဝေး", "အစည်းအဝေး"),
                    ("ခဲ့ပါသလား", "ပါသနည်း")),
                new BenchmarkCase("cat_statistical_intruder_and_tae_htae_confusion", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "သူဟာ ငယ်ငယ်ကထဲက တစ်ယောက်ထဲ ကြိုးစားပြီး မူးယစ်ဆေးဝါးအန္တရာယ်ကို တစ်ခု သိရှိနားလည်သင့်ပါတယ်။",
                    ("ငယ်ငယ်ကထဲက", "ငယ်ငယ်ကတည်းက"),
                    ("တစ်ယောက်ထဲ", "တစ်ယောက်တည်း"),
                    ("တစ်ခု သိရှိနားလည်သင့်ပါတယ်", "သိရှိနားလည်သင့်ပါတယ်")),
                new BenchmarkCase("cat_pleonastic_particles_and_slot_inversion", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "အစည်းအဝေး မတိုင်မီခင် အလို့ငှာအတွက် ဝန်ထမ်းများအားလုံး ပူးပေါင်းဆောင်ရွက်ပါနိုင်သည်။",
                    ("မတိုင်မီခင်", "မတိုင်မီ"),
                    ("အလို့ငှာအတွက်", "အတွက်"),
                    ("ပါနိုင်", "နိုင်ပါ")),
                new BenchmarkCase("cat_geology_mining_excavation_and_university", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "ဘူမိဗေဒအဖွဲ့သည် ရောဝတီတိုင်းဒေသကြီးရှိ နည်းပညာတ္ကသိုလ်နှင့် ပူး\u200Cပေါင်း၍ မြေအောက်သယံဇာတများ တုဖော်ထုပ်ယူကာ ဓါတ်ခွဲစစ်ဆေးသည်။",
                    ("ရောဝတီ", "ဧရာဝတီ"),
                    ("တ္ကသိုလ်", "တက္ကသိုလ်"),
                    ("ပူး\u200Cပေါင်း", "ပူးပေါင်း"),
                    ("တုဖော်", "တူးဖော်"),
                    ("ထုပ်ယူ", "ထုတ်ယူ"),
                    ("ဓါတ်ခွဲ", "ဓာတ်ခွဲ")),
                new BenchmarkCase("cat_boss_1_ministry_cyber_audit_ten_errors", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "A မျိုးသားအဆင့် ညွှန်ကိန်းအရ ရှမ်းပြည်နယ်က တည်ရှိသော မြို့နယ်များ၏ ရွာနေရာကုတ်နှင့် အစီရင် ခံစာများကို အချိန်မှီ ဖြည့်စည်း၍ ဉပဒေရှုဒေါင့်မှ စီစစ်သည်။",
                    ("A မျိုးသား", "အမျိုးသား"),
                    ("ညွှန်ကိန်း", "ညွှန်းကိန်း"),
                    ("က", "တွင်"),
                    ("ကုတ်", "ကုဒ်"),
                    ("အစီရင် ခံစာ", "အစီရင်ခံစာ"),
                    ("အချိန်မှီ", "အချိန်မီ"),
                    ("ဖြည့်စည်း", "ဖြည့်ဆည်း"),
                    ("ဉပဒေ", "ဥပဒေ"),
                    ("ရှုဒေါင့်", "ရှုထောင့်"),
                    ("စီစစ်", "စိစစ်")),
                new BenchmarkCase("cat_boss_2_colloquial_syntax_collocation_nine_errors", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.GeneralStandard,
                    "ခရီးသွားတွေကသော စြေးမှား မဳံ့ပဲသရေစာ သှားဝယ့်ပြီး ကမ်းခြေမှာ အပန်းဖြေနားထောင်ကာ ယဉ်ကျေးမှု အထောက်အထားတွေကို ဖျက်ဆီးစောင့်ရှောက်ကြပါတယ်.",
                    ("သော", "တော့"),
                    ("စြေးမှား", "ဈေးမှာ"),
                    ("မဳံ့ပဲသရေစာ", "မုန့်ပဲသရေစာ"),
                    ("သှား", "သွား"),
                    ("ဝယ့်", "ဝယ်"),
                    ("နားထောင်", "အနားယူ"),
                    ("ယဉ်ကျေးမှု အထောက်အထား", "ယဉ်ကျေးမှုအထောက်အထား"),
                    ("ဖျက်ဆီး", "ဂရုစိုက်"),
                    (".", "။")),
                new BenchmarkCase("cat_boss_3_grand_cataclysm_nine_simultaneous_errors", BenchmarkDifficulty.CataclysmicNightmare, ProofingStyleMode.FormalOfficial,
                    "ထို့ပြင် A မျိုးသားဒီမိုကရေစီA ဖွဲ့ချုပ်Aား တွေ့ဆုံခ္ငင့်နှင့် အာမခံထားခြင်းခြင့် ရေခဲသတ္တာများ ပေးအပ်ပြီး သင်ကြားရေးအဖွဲ့က ရှင်းပြခဲ့သည် လူနာတင် ယာဉ်ကို အသေးစိတ် ဟု သတင်းထုတ်ပြန်ကြော်ငြာချက် ထုတ်ပြန်လျှက်ရှိသည်။",
                    ("A မျိုးသားဒီမိုကရေစီA ဖွဲ့ချုပ်Aား", "အမျိုးသားဒီမိုကရေစီအဖွဲ့ချုပ်အား"),
                    ("ခ္ငင့်", "ခွင့်"),
                    ("ခြင့်", "ဖြင့်"),
                    ("ရေခဲသတ္တာ", "ရေခဲသေတ္တာ"),
                    ("ရှင်းပြခဲ့သည် လူနာတင် ယာဉ်ကို အသေးစိတ်", "လူနာတင်ယာဉ်ကို အသေးစိတ် ရှင်းပြခဲ့သည်"),
                    ("သတင်းထုတ်ပြန်ကြော်ငြာချက်", "သတင်းထုတ်ပြန်ကြေညာချက်"),
                    ("လျှက်", "လျက်")),

                // =========================================================================================
                // TIER 15: 4-PILLAR CORPUS DATA-MINING & O(1) GRAMMAR MASTERY (--mined / --v80, 14 Cases, 38 Spans)
                // =========================================================================================
                new BenchmarkCase("mined_clf_abidan_fish_and_tree_concord", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.FormalOfficial,
                    "ချောင်းအနီးတွင် ကကတစ် နှစ်ယောက် ဖမ်းမိပြီး ကင်ပွန်း သုံးကောင် စိုက်ပျိုးထားသည်။",
                    ("ယောက်", "ကောင်"),
                    ("ကောင်", "ပင်")),
                new BenchmarkCase("mined_clf_captain_and_dancer_human_sortal", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.FormalOfficial,
                    "သင်္ဘောပေါ်ရှိ ကက်ပတိန် နှစ်ကောင်နှင့် ကချေသည် သုံးစီးတို့က နှုတ်ဆက်သည်။",
                    ("ကောင်", "ဦး"),
                    ("စီး", "ယောက်")),
                new BenchmarkCase("mined_clf_compound_head_suffix_resolution", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.FormalOfficial,
                    "ဧည့်သည်များအတွက် ချည်ထည်အင်္ကျီ လေးယောက်နှင့် ရွှေစေတီ နှစ်ကောင်ကို လေ့လာမှတ်တမ်းတင်သည်။",
                    ("ယောက်", "ထည်"),
                    ("ကောင်", "ဆူ")),
                new BenchmarkCase("mined_comp_bound_stems_camera_athlete_pledge", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.FormalOfficial,
                    "အားကစားပြိုင်ပွဲတွင် ကစားသ မားများက ကတိ ကဝတ်ပြု၍ ကင် မရာဖြင့် မှတ်တမ်းတင်သည်။",
                    ("ကစားသ မား", "ကစားသမား"),
                    ("ကတိ ကဝတ်", "ကတိကဝတ်"),
                    ("ကင် မရာ", "ကင်မရာ")),
                new BenchmarkCase("mined_comp_geology_biology_with_case_particles", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.FormalOfficial,
                    "သုတေသီများသည် ဘူမိ ဗေဒကို လေ့လာပြီး ဇီဝ ဗေဒနှင့် ပတ်သတ်၍ စာတမ်းဖတ်သည်။",
                    ("ဘူမိ ဗေဒ", "ဘူမိဗေဒ"),
                    ("ဇီဝ ဗေဒ", "ဇီဝဗေဒ"),
                    ("ပတ်သတ်", "ပတ်သက်")),
                new BenchmarkCase("mined_orth_assimilation_thettauntthetha_kyezutin", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.GeneralStandard,
                    "သက်တောင့်သက်သာ နေထိုင်ရလို့ ကူညီပေးသူတွေကို ကျေဇူးတင်ပါတယ်။",
                    ("သက်တောင့်သက်သာ", "သက်သောင့်သက်သာ"),
                    ("ကျေဇူးတင်", "ကျေးဇူးတင်")),
                new BenchmarkCase("mined_val_interior_alhasin_and_mental_health", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.GeneralStandard,
                    "အိမ်အလှပြင်ရန် ပန်းပုတွေ သုံးသလို စိတ်ဆင်းရဲရေးဆိုတာ လူတိုင်းအတွက် အရေးကြီးပါတယ်။",
                    ("အလှပြင်", "အလှဆင်"),
                    ("စိတ်ဆင်းရဲရေး", "စိတ်ကျန်းမာရေး")),
                new BenchmarkCase("mined_val_plan_chamat_and_unity_taungsauk", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.GeneralStandard,
                    "လုပ်ငန်းအတွက် စီမံကိန်းတွေ လုပ်ဆောင်ရမယ် ဆိုသလို စည်းလုံးညီညွတ်မှုကို ပြုလုပ်ပါတယ်။",
                    ("လုပ်ဆောင်", "ချမှတ်"),
                    ("ပြုလုပ်", "တည်ဆောက်")),
                new BenchmarkCase("mined_val_nominalized_twera_and_broken_state", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.GeneralStandard,
                    "ကချင်ပြည် နယ်မှာ စက်ရုပ်တွေက အလုပ်တွေကို အစားထိုးလာတာ တွေရတယ်။",
                    ("ကချင်ပြည် နယ်", "ကချင်ပြည်နယ်"),
                    ("တွေ", "တွေ့")),
                new BenchmarkCase("mined_boss_synthesis_all_four_pillars", BenchmarkDifficulty.DataMinedMastery, ProofingStyleMode.FormalOfficial,
                    "ဘူမိ ဗေဒကို လေ့လာသော ကက်ပတိန် နှစ်ကောင်သည် သက်တောင့်သက်သာ စခန်းချ၍ အိမ်အလှပြင်ရန် ကတိ ကဝတ်ပြုကြောင်း မှတ်တမ်းတင်ထားချက် တွေရသည်။",
                    ("ဘူမိ ဗေဒ", "ဘူမိဗေဒ"),
                    ("ကောင်", "ဦး"),
                    ("သက်တောင့်သက်သာ", "သက်သောင့်သက်သာ"),
                    ("အလှပြင်", "အလှဆင်"),
                    ("ကတိ ကဝတ်", "ကတိကဝတ်"),
                    ("တွေ", "တွေ့"))
            };
        }
    }
}
