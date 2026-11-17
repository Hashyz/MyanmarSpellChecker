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
    /// Built-in offline Progressive (Easy -> Medium -> Hard -> Hardest/Expert) Accuracy, Clean-FPR,
    /// Suggestion-MRR, Latency (p50/p95/p99) &amp; Office Throughput Benchmark Suite.
    /// Run via: <c>MyanmarSpellChecker.AdminTools.exe benchmark [optionalDbPath]</c>
    /// </summary>
    internal static class ProofingBenchmarkRunner
    {
        private enum BenchmarkDifficulty
        {
            CleanControl = 0,
            Easy = 1,
            Medium = 2,
            Hard = 3,
            Hardest = 4
        }

        private sealed class BenchmarkCase
        {
            public string Domain { get; }
            public BenchmarkDifficulty Difficulty { get; }
            public ProofingStyleMode Mode { get; }
            public string Input { get; }
            public string? ExpectedWrongSpan { get; }
            public string? ExpectedTopCorrection { get; }

            public BenchmarkCase(
                string domain,
                BenchmarkDifficulty difficulty,
                ProofingStyleMode mode,
                string input,
                string? expectedWrongSpan = null,
                string? expectedTopCorrection = null)
            {
                Domain = domain;
                Difficulty = difficulty;
                Mode = mode;
                Input = input;
                ExpectedWrongSpan = expectedWrongSpan;
                ExpectedTopCorrection = expectedTopCorrection;
            }

            public bool IsClean => ExpectedWrongSpan == null;
        }

        private sealed class TierAccumulator
        {
            public int Cases;
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
            string dbPath = args.Length > 1 && File.Exists(args[1])
                ? args[1]
                : (File.Exists(Path.Combine("data", "mlc_thatpon.db"))
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

            // JIT warmup
            for (int w = 0; w < 3; w++)
            {
                _ = generalEngine.CheckText("မြန်မာနိုင်ငံသည် သာယာလှပသော နိုင်ငံတစ်ခု ဖြစ်ပါသည်။");
                _ = formalEngine.CheckText("ဝန်ကြီးဌာနမှ လုပ်ငန်းစင်များ ဖေါ်ပြထားပါသည်။");
            }

            List<BenchmarkCase> suite = BuildCleanRoomProgressiveSuite();
            var latenciesMs = new List<double>(suite.Count);
            var tierStats = new Dictionary<BenchmarkDifficulty, TierAccumulator>
            {
                [BenchmarkDifficulty.Easy] = new TierAccumulator(),
                [BenchmarkDifficulty.Medium] = new TierAccumulator(),
                [BenchmarkDifficulty.Hard] = new TierAccumulator(),
                [BenchmarkDifficulty.Hardest] = new TierAccumulator()
            };

            int tp = 0, fp = 0, fn = 0;
            int cleanTotal = 0, cleanWithFp = 0;
            int top1Hits = 0, evalSugCount = 0;
            double mrrSum = 0.0;

            var sw = new Stopwatch();
            foreach (var tc in suite)
            {
                var engine = tc.Mode == ProofingStyleMode.FormalOfficial ? formalEngine : generalEngine;
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
                acc.Cases++;
                acc.LatenciesMs.Add(elapsedMs);

                string normExpectedSpan = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedWrongSpan!);
                string normExpectedFix = UnicodeNormalizer.NormalizeCanonicalOrder(tc.ExpectedTopCorrection!);

                int spanIdx = tc.Input.IndexOf(tc.ExpectedWrongSpan!, StringComparison.Ordinal);
                int goldEnd = spanIdx >= 0 ? spanIdx + tc.ExpectedWrongSpan!.Length : tc.Input.Length;

                ProofingIssue? matched = issues.FirstOrDefault(iss =>
                    (spanIdx >= 0 && Math.Max(spanIdx, iss.StartIndex) < Math.Min(goldEnd, iss.StartIndex + iss.Length)) ||
                    UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText).Contains(normExpectedSpan) ||
                    normExpectedSpan.Contains(UnicodeNormalizer.NormalizeCanonicalOrder(iss.OriginalText)));

                if (matched != null)
                {
                    tp++;
                    acc.Tp++;
                    evalSugCount++;
                    acc.EvalSugCount++;

                    string targetSpliced = spanIdx >= 0
                        ? UnicodeNormalizer.NormalizeCanonicalOrder(
                            tc.Input.Substring(0, spanIdx) + tc.ExpectedTopCorrection + tc.Input.Substring(goldEnd))
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
                        double rr = 1.0 / foundRank;
                        mrrSum += rr;
                        acc.MrrSum += rr;
                    }
                    else
                    {
                        string got = matched.Suggestions.Count > 0 ? matched.Suggestions[0].Replacement : "(none)";
                        Console.WriteLine($"  [SUG-MISS] [{tc.Difficulty}] expected '{tc.ExpectedWrongSpan}'->'{tc.ExpectedTopCorrection}', got '{matched.OriginalText}'->'{got}'");
                    }

                    if (issues.Count > 1)
                    {
                        int extraFp = issues.Count - 1;
                        fp += extraFp;
                        acc.Fp += extraFp;
                    }
                }
                else
                {
                    fn++;
                    acc.Fn++;
                    fp += issues.Count;
                    acc.Fp += issues.Count;
                    Console.WriteLine($"  [FN-MISS]  [{tc.Difficulty}] ({tc.Domain}) missed '{tc.ExpectedWrongSpan}' -> '{tc.ExpectedTopCorrection}' in '{tc.Input}'");
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

            long memBytes = GC.GetTotalMemory(forceFullCollection: false);

            Console.WriteLine();
            Console.WriteLine("==========================================================================================");
            Console.WriteLine(" Myanmar Spell Checker Suite — Easy -> Hardest Progressive Accuracy & Performance Report");
            Console.WriteLine("==========================================================================================");
            Console.WriteLine($" Dictionary DB Size    : {new FileInfo(dbPath).Length / (1024.0 * 1024.0):F2} MB ({trie.WordCount:N0} headwords, {trie.BigramCount:N0} bigrams)");
            Console.WriteLine($" Cold DB Load Time     : {swLoad.Elapsed.TotalMilliseconds:F1} ms");
            Console.WriteLine($" Managed Heap Footprint: {memBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine(" Progressive Difficulty Breakdown (Easy -> Medium -> Hard -> Hardest / Expert):");
            Console.WriteLine(" Tier                      | Cases |  TP |  FN | Recall  | Top-1   |   MRR  | p95 ms");
            Console.WriteLine(" --------------------------+-------+-----+-----+---------+---------+--------+-------");
            PrintTierRow("Tier 1: Easy (Syllable)", tierStats[BenchmarkDifficulty.Easy]);
            PrintTierRow("Tier 2: Medium (Lexical)", tierStats[BenchmarkDifficulty.Medium]);
            PrintTierRow("Tier 3: Hard (Boundary)", tierStats[BenchmarkDifficulty.Hard]);
            PrintTierRow("Tier 4: Hardest (Syntax)", tierStats[BenchmarkDifficulty.Hardest]);
            Console.WriteLine($" Tier 0: Clean Controls    | {cleanTotal,5} | FPR = {cleanFpr * 100.0,5:F2}% ({cleanWithFp}/{cleanTotal} false positives)");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine($" Overall Precision     : {precision * 100.0:F2}%  (TP={tp}, FP={fp}, FN={fn})");
            Console.WriteLine($" Overall Recall        : {recall * 100.0:F2}%");
            Console.WriteLine($" Overall F1 Score      : {f1 * 100.0:F2}%");
            Console.WriteLine($" Clean-Sentence FPR    : {cleanFpr * 100.0:F2}%  ({cleanWithFp}/{cleanTotal} clean sentences)");
            Console.WriteLine($" Top-1 Suggestion Acc  : {top1Acc * 100.0:F2}%");
            Console.WriteLine($" Mean Reciprocal Rank  : {mrr:F4}");
            Console.WriteLine($" Sentence Latency      : p50={p50:F2} ms | p95={p95:F2} ms | p99={p99:F2} ms | mean={meanMs:F2} ms");
            Console.WriteLine($" Excel 2D Block Scan   : {swExcel.Elapsed.TotalMilliseconds:F1} ms for 1,250 cells ({cellsPerSec:N0} cells/sec, {excelIssues.Count} issues)");
            Console.WriteLine($" COMPOSITE SCORE       : {composite:F4}");
            Console.WriteLine("==========================================================================================");

            return 0;
        }

        private static void PrintTierRow(string label, TierAccumulator acc)
        {
            double rec = acc.Cases > 0 ? (double)acc.Tp / acc.Cases * 100.0 : 100.0;
            double top1 = acc.EvalSugCount > 0 ? (double)acc.Top1Hits / acc.EvalSugCount * 100.0 : 0.0;
            double mrr = acc.EvalSugCount > 0 ? acc.MrrSum / acc.EvalSugCount : 0.0;
            double p95 = 0.0;
            if (acc.LatenciesMs.Count > 0)
            {
                var sorted = acc.LatenciesMs.OrderBy(x => x).ToList();
                p95 = sorted[Math.Min(sorted.Count - 1, (int)(0.95 * sorted.Count))];
            }
            Console.WriteLine($" {label,-25} | {acc.Cases,5} | {acc.Tp,3} | {acc.Fn,3} | {rec,6:F1}% | {top1,6:F1}% | {mrr,6:F4} | {p95,6:F2}");
        }

        private static List<BenchmarkCase> BuildCleanRoomProgressiveSuite()
        {
            return new List<BenchmarkCase>
            {
                // =========================================================================================
                // TIER 0: ADVERSARIAL CLEAN CONTROLS (20 Clean Sentences — Must Produce 0 False Positives)
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
                // TIER 4: HARDEST / EXPERT — Real-Word Confusions, Tense-Aspect & SOV Word Order (20 Cases)
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
                    "နည်းပညာဘာသာရပ်သစ်ကို လေ့လာချင်ကြတယ်")
            };
        }
    }
}
