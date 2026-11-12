using System;
using System.IO;
using MyanmarSpellChecker.AdminTools.Compiler;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Tests.Infrastructure
{
    internal static class TestPaths
    {
        private static readonly object s_masterBuildLock = new object();
        public static readonly string RepoRoot = LocateRepoRoot();
        public static readonly string DataDirectory = Path.Combine(RepoRoot, "data");
        private static readonly string s_masterJsonPath = Path.Combine(DataDirectory, "mlc_thatpon_master.json");
        public static readonly string EncryptedDbPath = Path.Combine(DataDirectory, "mlc_thatpon.db");
        public static readonly string SampleCsvPath = Path.Combine(DataDirectory, "mlc_thatpon_sample_import.csv");

        /// <summary>
        /// Returns data/mlc_thatpon_master.json, automatically compiling it from data/sources/
        /// on a fresh Git clone (since the ~81 MB generated JSON is .gitignored to keep Git clones lightweight).
        /// </summary>
        public static string MasterJsonPath
        {
            get
            {
                if (!File.Exists(s_masterJsonPath))
                {
                    lock (s_masterBuildLock)
                    {
                        if (!File.Exists(s_masterJsonPath))
                        {
                            MasterLexiconCompiler.BuildFromDataDirectory(DataDirectory, s_masterJsonPath);
                        }
                    }
                }
                return s_masterJsonPath;
            }
        }

        private static string LocateRepoRoot()
        {
            string? current = AppDomain.CurrentDomain.BaseDirectory;
            while (current != null && current.Length > 0)
            {
                if (File.Exists(Path.Combine(current, "MyanmarSpellChecker.sln")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repository root containing MyanmarSpellChecker.sln.");
        }

        public static MyanmarProofingEngine CreateSeededEngine()
        {
            var trie = new MyanmarTrie();
            foreach (var entry in SeedDictionaryProvider.GetOfficialSeedEntries())
            {
                trie.InsertEntry(entry);
            }
            foreach (var rule in SeedDictionaryProvider.GetOfficialOrthographyRules())
            {
                trie.InsertOrthographyRule(rule);
            }
            return new MyanmarProofingEngine(trie);
        }
    }
}
