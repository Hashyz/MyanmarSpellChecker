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
    /// Built-in offline Accuracy, Clean-FPR, Suggestion-MRR, Latency (p50/p95/p99) &amp; Office Throughput Benchmark Suite.
    /// Run via: <c>MyanmarSpellChecker.AdminTools.exe benchmark [optionalDbPath]</c>
    /// </summary>
    internal static class ProofingBenchmarkRunner
    {
        private sealed class BenchmarkCase
        {
            public string Domain { get; }
            public int Tier { get; } // 0 = Clean, 1 = Structural/Syllable, 2 = Lexical/Compound, 3 = Contextual/Grammar
            public ProofingStyleMode Mode { get; }
            public string Input { get; }
            public string? ExpectedWrongSpan { get; }
            public string? ExpectedTopCorrection { get; }

            public BenchmarkCase(
                string domain,
                int tier,
                ProofingStyleMode mode,
                string input,
                string? expectedWrongSpan = null,
                string? expectedTopCorrection = null)
            {
                Domain = domain;
                Tier = tier;
                Mode = mode;
                Input = input;
                ExpectedWrongSpan = expectedWrongSpan;
                ExpectedTopCorrection = expectedTopCorrection;
            }

            public bool IsClean => ExpectedWrongSpan == null;
        }

        public static int Run(string[] args)
        {
            string dbPath = args.Length > 1 && File.Exists(args[1])
                ? args[1]
                : (File.Exists(Path.Combine("artifacts", "mlc_thatpon.db"))
                    ? Path.Combine("artifacts", "mlc_thatpon.db")
                    : Path.Combine("data", "mlc_thatpon.db"));

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

            List<BenchmarkCase> suite = BuildCleanRoomSuite();
            var latenciesMs = new List<double>(suite.Count);

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
                latenciesMs.Add(sw.Elapsed.TotalMilliseconds);

                if (tc.IsClean)
                {
                    cleanTotal++;
                    if (issues.Count > 0)
                    {
                        cleanWithFp++;
                        fp += issues.Count;
                    }
                    continue;
                }

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
                    evalSugCount++;
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
                        if (foundRank == 1) top1Hits++;
                        mrrSum += 1.0 / foundRank;
                    }

                    if (issues.Count > 1)
                        fp += issues.Count - 1;
                }
                else
                {
                    fn++;
                    fp += issues.Count;
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
            Console.WriteLine("========================================================================");
            Console.WriteLine(" Myanmar Spell Checker Suite — Accuracy & Performance Benchmark Report");
            Console.WriteLine("========================================================================");
            Console.WriteLine($" Dictionary DB Size    : {new FileInfo(dbPath).Length / (1024.0 * 1024.0):F2} MB ({trie.WordCount:N0} headwords, {trie.BigramCount:N0} bigrams)");
            Console.WriteLine($" Cold DB Load Time     : {swLoad.Elapsed.TotalMilliseconds:F1} ms");
            Console.WriteLine($" Managed Heap Footprint: {memBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine("------------------------------------------------------------------------");
            Console.WriteLine($" Precision             : {precision * 100.0:F2}%  (TP={tp}, FP={fp}, FN={fn})");
            Console.WriteLine($" Recall                : {recall * 100.0:F2}%");
            Console.WriteLine($" F1 Score              : {f1 * 100.0:F2}%");
            Console.WriteLine($" Clean-Sentence FPR    : {cleanFpr * 100.0:F2}%  ({cleanWithFp}/{cleanTotal} clean sentences)");
            Console.WriteLine($" Top-1 Suggestion Acc  : {top1Acc * 100.0:F2}%");
            Console.WriteLine($" Mean Reciprocal Rank  : {mrr:F4}");
            Console.WriteLine($" Sentence Latency      : p50={p50:F2} ms | p95={p95:F2} ms | p99={p99:F2} ms | mean={meanMs:F2} ms");
            Console.WriteLine($" Excel 2D Block Scan   : {swExcel.Elapsed.TotalMilliseconds:F1} ms for 1,250 cells ({cellsPerSec:N0} cells/sec, {excelIssues.Count} issues)");
            Console.WriteLine($" COMPOSITE SCORE       : {composite:F4}");
            Console.WriteLine("========================================================================");

            return 0;
        }

        private static List<BenchmarkCase> BuildCleanRoomSuite()
        {
            return new List<BenchmarkCase>
            {
                // Clean Control Sentences across Ministry, Judiciary, Literature, Technical & Conversational Domains (Tier 0)
                new BenchmarkCase("ministry", 0, ProofingStyleMode.FormalOfficial, "ပြည်ထောင်စုတရားလွှတ်တော်ချုပ်သည် စာချွန်တော်အမိန့်လျှောက်ထားမှုများကို ဥပဒေနှင့်အညီ စီရင်ဆုံးဖြတ်သည်။"),
                new BenchmarkCase("ministry", 0, ProofingStyleMode.FormalOfficial, "သောက်ရေနှင့် မြစ်ရေ သန့်ရှင်းရေးအတွက် စနစ်တကျ စစ်ဆေးဆောင်ရွက်လျက်ရှိသည်။"),
                new BenchmarkCase("literary", 0, ProofingStyleMode.FormalOfficial, "“ယခု အမိန့်တော်သည် သင့်ပေ၏။ “လိုက်၍ ပြချေ” ဆိုလျှင် ကျွန်ုပ် သွားပါအံ့” ဟု လျှောက်တင်၏။"),
                new BenchmarkCase("literary", 0, ProofingStyleMode.FormalOfficial, "အိမ်တွင် ဘုရားစင် မရှိသဖြင့် ထောင့်ကျသော နေရာတွင် နံရံကို မျက်နှာပြု၍ ထားရှိသည်။"),
                new BenchmarkCase("technical", 0, ProofingStyleMode.FormalOfficial, "ဆော့ဖ်ဝဲလ်အင်ဂျင်နီယာများသည် ဆာဗာလော့ဂ်များကို ပုံမှန်စစ်ဆေးပြုပြင်ကြသည်။"),
                new BenchmarkCase("geographic", 0, ProofingStyleMode.GeneralStandard, "နောက်ရက်တွင် လွိုင်နမ်းဖ ကျေးရွာသို့ ရောက်ရှိခဲ့ပါတယ်။"),
                new BenchmarkCase("conversational", 0, ProofingStyleMode.GeneralStandard, "ကလေးများ ဘောလုံး ကစားကြပြီး ဇာတ်သဘင်အဖွဲ့က ကပြဖျော်ဖြေတယ်။"),
                new BenchmarkCase("conversational", 0, ProofingStyleMode.GeneralStandard, "ရေဘူးနဲ့ ထမင်းဘူး ယူလာပေမဲ့ မအားသေးလို့ မစားရသေးဘူး။"),
                new BenchmarkCase("classical", 0, ProofingStyleMode.FormalOfficial, "ယောက်ျားပီသစွာဖြင့် ကျွန်ုပ်တို့နှင့်အတူ လက်ယာဘက်သို့ လျှောက်လှမ်းသည်။"),

                // Tier 1: UTN #11 Syllable Validation, Shattered Syllables, Detached Coda, Broken Stack, ZWSP & Vowel/Tone Errors
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "လာမ ည့် အပတ်တွင် အစည်းအဝေး ကျင်းပမည်။", "လာမ ည့်", "လာမည့်"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "အပြုအမူပို င်းဆိုင်ရာ လေ့လာချက်ဖြစ်သည်။", "ပို င်း", "ပိုင်း"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "စစ်တက္ ကသိုလ်တွင် သင်တန်းတက်ရောက်သည်။", "စစ်တက္ ကသိုလ်", "စစ်တက္ကသိုလ်"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "သတင်းအချက် အလက်များကို စနစ်တကျ စုဆောင်းသည်။", "အချက် အလက်", "အချက်အလက်"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.GeneralStandard, "သူငယ်ချင်းအသစ်ရှာတာ ပျော်စရာကောင်း့တယ်။", "ကောင်း့", "ကောင်း"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "အရေးကြိးချက်များကို စနစ်တကျ တင်ပြထားပါသည်။", "ကြိး", "ကြီး"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.GeneralStandard, "လုံခြုံစိတ်ချရတဲ့ ငွေပေးချေမှု စနစ်တွေ ရှိပါတယ်.", ".", "။"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "သက်တမး တိုးမြှင့်ပေးရန် လျှောက်ထားသည်။", "တမး", "တမ်း"),
                new BenchmarkCase("structural", 1, ProofingStyleMode.FormalOfficial, "မှတ်ပုံ\u200Bတင် စာရင်းကို စစ်ဆေးသည်။", "မှတ်ပုံ\u200Bတင်", "မှတ်ပုံတင်"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "ကူညီပေးတဲ့အတွက် ကေျးဇူးတင်ပါတယ်။", "ကေျး", "ကျေး"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "ကြိုးပုြတ်သွားလို့ ပြန်ချည်ထားတယ်။", "ပုြတ်", "ပြုတ်"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "စကားပောြဆိုရာမှာ ယဉ်ကျေးသင့်တယ်။", "ပောြ", "ပြော"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.FormalOfficial, "စည်းလုံးညီည်ွတ်စွာ ဆောင်ရွက်ကြသည်။", "ည်ွတ်", "ညွတ်"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.FormalOfficial, "ကမ္ဘာ်ကြီး ငြိမ်းချမ်းသာယာစေရန် ဆုတောင်းသည်။", "ကမ္ဘာ်", "ကမ္ဘာ"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "ပစ္စည်းတွေကို သေချာသိမ်္းထားပြီးပြီ။", "သိမ်္း", "သိမ်း"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "အလုပ်လုပ်ရာတွင် စတ္ိထားရမယ်။", "စတ္ိ", "စိတ်"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "မုန့်ဟငိ်းခါး စားချင်တယ်။", "ဟငိ်း", "ဟင်း"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.FormalOfficial, "အဓိပ္ပာယ္ရှိသော လုပ်ငန်းဖြစ်သည်။", "ယ္", "ယ်"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "စာအုပ် ိကို ယူလာပေးပါ။", "ိကို", "ကို"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.GeneralStandard, "သီချင်းနားထောင် ရင်းိ စာဖတ်တယ်။", "ရင်းိ", "ရင်း"),
                new BenchmarkCase("syllable", 1, ProofingStyleMode.FormalOfficial, "သတင်းအချက်အလက များကို စိစစ်သည်။", "အချက်အလက", "အချက်အလက်"),

                // Tier 2: Hidden Compounds, Pali/Consonant Substitutions, Space-Split Compounds & Dropped 'အ'
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "ပညာရေ ဖွံ့ဖြိုးတိုးတက်မှုအတွက် ဆောင်ရွက်သည်။", "ပညာရေ", "ပညာရေး"),
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "သတင်း အချက်အလတ် စုဆောင်းတင်ပြသည်။", "အချက်အလတ်", "အချက်အလက်"),
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "စီမံကိန်းကို အကောင်ထည်ဖော် ဆောင်ရွက်သည်။", "အကောင်ထည်ဖော်", "အကောင်အထည်ဖော်"),
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "အဖွဲ့အစည်းအကြား ပြေလည်မူရရှိခဲ့သည်။", "ပြေလည်မူ", "ပြေလည်မှု"),
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "စာ ကြည့် တိုက် တွင် စာအုပ်များ ဖတ်ရှုသည်။", "စာ ကြည့် တိုက်", "စာကြည့်တိုက်"),
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "စမ်းသပ်ချက် ရလဓ် ကောင်းမွန်ပါသည်။", "ရလဓ်", "ရလဒ်"),
                new BenchmarkCase("compound", 2, ProofingStyleMode.FormalOfficial, "ကျောင်းတွင် ဃီတပွဲ ကျင်းပသည်။", "ဃီတ", "ဂီတ"),

                // Tier 3: Contextual Homophones, Mode-Aware Attached Particles, Bipartite Negation & Merged Words
                new BenchmarkCase("context", 3, ProofingStyleMode.FormalOfficial, "ကုမ္ပဏီမာ အလုပ်လုပ်ပါသည်။", "မာ", "တွင်"),
                new BenchmarkCase("context", 3, ProofingStyleMode.GeneralStandard, "ကုမ္ပဏီမာ အလုပ်လုပ်ပါတယ်။", "မာ", "မှာ"),
                new BenchmarkCase("context", 3, ProofingStyleMode.FormalOfficial, "နေကောင်းဘူး", "နေကောင်းဘူး", "နေမကောင်းပါ"),
                new BenchmarkCase("context", 3, ProofingStyleMode.FormalOfficial, "မအားနေဖူး", "မအားနေဖူး", "မအားလပ်ပါ"),
                new BenchmarkCase("context", 3, ProofingStyleMode.FormalOfficial, "ထမင်းကို သူကစားသည်။", "ကစား", "က စား"),
                new BenchmarkCase("context", 3, ProofingStyleMode.GeneralStandard, "ခွေးကို အစာ ကြွေး ပါ။", "ကြွေး", "ကျွေး"),
                new BenchmarkCase("context", 3, ProofingStyleMode.GeneralStandard, "ဒီသတင်းကို ကျား ရ တယ်။", "ကျား", "ကြား")
            };
        }
    }
}
