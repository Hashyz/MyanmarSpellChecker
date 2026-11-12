using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyanmarSpellChecker.AdminTools.Compiler;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Security;

namespace MyanmarSpellChecker.AdminTools
{
    /// <summary>
    /// Vendor CLI Tool for:
    /// 1. Compiling raw CSV/Seed "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" data into an AES-256 SQLCipher database (.db)
    /// 2. Generating RSA-2048 KeyPairs and signing offline .lic files (Single-PC or Ministry GPO Bundle)
    /// 3. Running an end-to-end verification of the Proofing Engine
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("========================================================================");
            Console.WriteLine(" မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း (B2G Enterprise Admin & Compiler Tool)");
            Console.WriteLine("========================================================================");

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "demo";

            switch (cmd)
            {
                case "build-lexicon":
                    return RunBuildLexicon(args);
                case "compile-dict":
                    return RunCompileDict(args);
                case "gen-keys":
                    return RunGenerateKeys(args);
                case "sign-license":
                    return RunSignLicense(args);
                default:
                    return RunInteractiveDemo();
            }
        }

        private static int RunBuildLexicon(string[] args)
        {
            string dataDir = args.Length > 1 ? args[1] : "data";
            string? outputJsonPath = args.Length > 2 ? args[2] : null;
            MasterLexiconCompiler.BuildFromDataDirectory(dataDir, outputJsonPath);
            return 0;
        }

        private static int RunCompileDict(string[] args)
        {
            string arg1 = args.Length > 1 ? args[1] : string.Empty;
            bool arg1IsJson = arg1.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            string jsonPath = arg1IsJson ? arg1 : Path.Combine("data", "mlc_thatpon_master.json");
            string csvPath = (!string.IsNullOrEmpty(arg1) && !arg1IsJson) ? arg1 : Path.Combine("data", "mlc_thatpon_sample_import.csv");
            string outDbPath = args.Length > 2 ? args[2] : Path.Combine("artifacts", "mlc_thatpon.db");
            string password = args.Length > 3 ? args[3] : AntiTamperGuard.ReconstructProtectedFallbackKey();

            var entries = new List<DictionaryEntry>(SeedDictionaryProvider.GetOfficialSeedEntries());
            var rules = new List<OrthographyRule>(SeedDictionaryProvider.GetOfficialOrthographyRules());
            var ctxRules = new List<ContextualRule>();
            var bigrams = new List<WordBigramEntry>();

            if (File.Exists(jsonPath))
            {
                var masterJson = EncryptedDictionaryRepository.ParseFromJsonFile(jsonPath);
                entries.AddRange(masterJson.Entries);
                rules.AddRange(masterJson.OrthographyRules);
                if (masterJson.ContextRules != null) ctxRules.AddRange(masterJson.ContextRules);
                if (masterJson.WordBigrams != null) bigrams.AddRange(masterJson.WordBigrams);
                Console.WriteLine($"[+] Loaded {masterJson.Entries.Count} entries, {masterJson.OrthographyRules.Count} rules, {ctxRules.Count} context rules, {bigrams.Count} bigrams from JSON: {jsonPath}");
            }

            if (csvPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && File.Exists(csvPath))
            {
                var csvEntries = EncryptedDictionaryRepository.ParseEntriesFromCsv(csvPath);
                entries.AddRange(csvEntries);
                Console.WriteLine($"[+] Loaded {csvEntries.Count} additional entries from CSV: {csvPath}");
            }

            var repo = new EncryptedDictionaryRepository(outDbPath);
            repo.CompileEncryptedDatabase(password, entries, rules, ctxRules, bigrams);

            var trie = repo.LoadIntoTrie(password);
            Console.WriteLine($"[+] Successfully compiled encrypted AES-256 + HMAC-SHA256 DB: {Path.GetFullPath(outDbPath)}");
            Console.WriteLine($"[+] Total Unique Headwords: {trie.WordCount} | Explicit Rules: {trie.RuleCount} | Context Rules: {trie.ContextualRuleCount} | Bigrams: {trie.BigramCount}");
            return 0;
        }

        private static int RunGenerateKeys(string[] args)
        {
            string outDir = args.Length > 1 ? args[1] : "artifacts";
            Directory.CreateDirectory(outDir);

            var (privXml, pubXml) = LicenseService.GenerateRsaKeyPair();
            string privPath = Path.Combine(outDir, "vendor_private_key.xml");
            string pubPath = Path.Combine(outDir, "vendor_public_key.xml");

            File.WriteAllText(privPath, privXml, Encoding.UTF8);
            File.WriteAllText(pubPath, pubXml, Encoding.UTF8);

            Console.WriteLine($"[+] Generated RSA-2048 Private Key: {Path.GetFullPath(privPath)}");
            Console.WriteLine($"[+] Generated RSA-2048 Public Key:  {Path.GetFullPath(pubPath)}");
            return 0;
        }

        private static int RunSignLicense(string[] args)
        {
            string ministryName = args.Length > 1 ? args[1] : "ပို့ဆောင်ရေးနှင့် ဆက်သွယ်ရေး ဝန်ကြီးဌာန";
            string machineId = args.Length > 2 ? args[2] : HardwareFingerprint.GetMachineId();
            string outLicPath = args.Length > 3 ? args[3] : Path.Combine("artifacts", "license.lic");

            Directory.CreateDirectory(Path.GetDirectoryName(outLicPath) ?? ".");
            var (privXml, pubXml) = LicenseService.GenerateRsaKeyPair();

            var payload = new LicensePayload
            {
                MinistryName = ministryName,
                Mode = LicenseMode.SingleMachine,
                MachineId = machineId,
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddYears(5)
            };

            string signedLic = LicenseService.CreateSignedLicenseJson(payload, "MM-GOV-THATPON-SQLCIPHER-2026!", privXml);
            File.WriteAllText(outLicPath, signedLic, Encoding.UTF8);
            Console.WriteLine($"[+] Signed offline .lic for Machine ID '{machineId}': {Path.GetFullPath(outLicPath)}");
            return 0;
        }

        private static int RunInteractiveDemo()
        {
            string machineId = HardwareFingerprint.GetMachineId();
            Console.WriteLine($"[*] Current Hardware Machine ID: {machineId}");

            string tempDir = Path.Combine(Path.GetTempPath(), "MyanmarSpellCheckerDemo");
            Directory.CreateDirectory(tempDir);
            string dbPath = Path.Combine(tempDir, "mlc_thatpon.db");

            // 1. Generate RSA-2048 KeyPair and Sign Offline License embedding the SQLCipher key
            var (privKey, pubKey) = LicenseService.GenerateRsaKeyPair();
            const string sqlCipherSecret = "B2G-MLC-THATPON-AES256-SECRET-KEY!";

            var licensePayload = new LicensePayload
            {
                MinistryName = "ပြည်ထောင်စုအစိုးရအဖွဲ့ရုံး ဝန်ကြီးဌာန",
                Mode = LicenseMode.SingleMachine,
                MachineId = machineId,
                ExpiresUtc = DateTime.UtcNow.AddYears(3)
            };

            string licJson = LicenseService.CreateSignedLicenseJson(licensePayload, sqlCipherSecret, privKey);
            var validation = LicenseService.ValidateLicenseJson(licJson, pubKey);
            Console.WriteLine($"[*] Offline License Validation: {validation.IsValid} -> {validation.StatusMessage}");

            // 2. Compile Encrypted SQLCipher Database & Load into In-Memory Trie
            var repo = new EncryptedDictionaryRepository(dbPath);
            repo.CompileEncryptedDatabase(
                validation.SqlCipherDatabaseKey!,
                SeedDictionaryProvider.GetOfficialSeedEntries(),
                SeedDictionaryProvider.GetOfficialOrthographyRules());

            MyanmarTrie trie = repo.LoadIntoTrie(validation.SqlCipherDatabaseKey!);
            Console.WriteLine($"[*] Loaded Encrypted SQLCipher Dictionary -> Headwords: {trie.WordCount}, Orthography Rules: {trie.RuleCount}");

            // 3. Test Sample Ministry Paragraph containing:
            //    - Lookalike Digit Zero '၀' in '၀န်ကြီးဌာန'
            //    - Lookalike '၄င်း' instead of '၎င်း'
            //    - 'တယောက်' instead of 'တစ်ယောက်'
            //    - 'တီဆောက်' instead of 'တည်ဆောက်'
            //    - 'လက်မတ်' instead of 'လက်မှတ်'
            //    - 'အား စစ်ဆေး' instead of 'ကို စစ်ဆေး'
            //    - Colloquial 'တွေ' and 'ပါတယ်'
            var engine = new MyanmarProofingEngine(trie);
            string sampleText = "၀န်ကြီးဌာနမှ ဝန်ထမ်းတယောက်သည် ၄င်းစီမံကိန်းအား စစ်ဆေးပြီး တီဆောက်ရေး လက်မတ်ထိုးထားသည့် စာရွက်စာတမ်းတွေ တင်ပြပါတယ်။।";
            sampleText = sampleText.Replace("।", "။"); // ensure Myanmar double danda ။။

            Console.WriteLine();
            Console.WriteLine($"[Input Paragraph]: {sampleText}");
            Console.WriteLine("------------------------------------------------------------------------");

            List<ProofingIssue> issues = engine.CheckText(sampleText);
            foreach (var issue in issues)
            {
                string colorTag = issue.IsContextualWarning
                    ? "[AMBER- အနက်သံသယ]"
                    : (issue.IsGrammarOrStyle ? "[BLUE - ရုံးသုံးသဒ္ဒါ]" : "[RED  - သတ်ပုံအမှား]");
                Console.WriteLine($"{colorTag} '{issue.OriginalText}' (Index {issue.StartIndex}, Len {issue.Length})");
                Console.WriteLine($"   အကြောင်းပြချက် : {issue.Message}");
                if (issue.Suggestions.Count > 0)
                {
                    Console.WriteLine($"   အကြံပြုချက်   : {issue.Suggestions[0].DisplayLabel}");
                }
            }

            return 0;
        }
    }
}

