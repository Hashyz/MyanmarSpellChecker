using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Dictionary
{
    [DataContract]
    public sealed class MasterDictionaryJsonModel
    {
        [DataMember(Name = "Standard")]
        public string Standard { get; set; } = string.Empty;

        [DataMember(Name = "Version")]
        public string Version { get; set; } = string.Empty;

        [DataMember(Name = "Entries")]
        public List<DictionaryEntry> Entries { get; set; } = new List<DictionaryEntry>();

        [DataMember(Name = "OrthographyRules")]
        public List<OrthographyRule> OrthographyRules { get; set; } = new List<OrthographyRule>();

        [DataMember(Name = "ContextRules")]
        public List<ContextualRule> ContextRules { get; set; } = new List<ContextualRule>();

        [DataMember(Name = "WordBigrams")]
        public List<WordBigramEntry> WordBigrams { get; set; } = new List<WordBigramEntry>();
    }

    /// <summary>
    /// Manages the AES-256 + HMAC-SHA256 encrypted "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" embedded database (.db).
    /// Uses 100% Microsoft-signed .NET Framework 4.8 FIPS-compliant cryptography (zero unsigned 3rd-party native DLLs).
    /// Runtime loading is strictly read-only in memory; no user modifications are permitted.
    /// </summary>
    public sealed class EncryptedDictionaryRepository
    {
        private static readonly byte[] FileMagicV2 = Encoding.ASCII.GetBytes("MMTHATPON2");
        private static readonly byte[] FileMagicV3 = Encoding.ASCII.GetBytes("MMTHATPON3");
        private static readonly byte[] FileMagicV4 = Encoding.ASCII.GetBytes("MMTHATPON4");
        private static readonly byte[] FileMagic = Encoding.ASCII.GetBytes("MMTHATPON5");
        private const int Pbkdf2Iterations = 25000;

        private readonly string _databasePath;

        public EncryptedDictionaryRepository(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));

            _databasePath = databasePath;
        }

        public string DatabasePath => _databasePath;

        public void CompileEncryptedDatabase(string encryptionPassword, MasterDictionaryJsonModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            CompileEncryptedDatabase(
                encryptionPassword,
                model.Entries,
                model.OrthographyRules,
                model.ContextRules,
                model.WordBigrams);
        }

        /// <summary>
        /// Compiles headword entries, orthography rules, contextual rules, and word bigrams into an authenticated AES-256 + HMAC-SHA256 encrypted .db file.
        /// </summary>
        public void CompileEncryptedDatabase(
            string encryptionPassword,
            IEnumerable<DictionaryEntry> entries,
            IEnumerable<OrthographyRule> rules,
            IEnumerable<ContextualRule>? contextRules = null,
            IEnumerable<WordBigramEntry>? wordBigrams = null)
        {
            if (string.IsNullOrWhiteSpace(encryptionPassword))
                throw new ArgumentException("Encryption password is required.", nameof(encryptionPassword));

            string? dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 1. Serialize & GZip-compress payload in memory
            byte[] compressedPayload;
            using (var rawMs = new MemoryStream())
            {
                using (var gzip = new GZipStream(rawMs, CompressionMode.Compress, leaveOpen: true))
                using (var writer = new BinaryWriter(gzip, Encoding.UTF8))
                {
                    var entryList = new List<DictionaryEntry>();
                    var seenEntries = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var e in entries)
                    {
                        if (string.IsNullOrWhiteSpace(e.Word)) continue;
                        string normWord = UnicodeNormalizer.NormalizeCanonicalOrder(e.Word.Trim());
                        string key = $"{normWord}|{e.PosTag}|{e.MeaningCue}";
                        if (!seenEntries.Add(key)) continue;

                        entryList.Add(new DictionaryEntry
                        {
                            Id = entryList.Count + 1,
                            Word = normWord,
                            MeaningCue = e.MeaningCue ?? string.Empty,
                            PosTag = e.PosTag ?? string.Empty,
                            PhoneticKey = MyanmarTrie.ComputePhoneticKey(normWord),
                            UsageNote = e.UsageNote ?? string.Empty,
                            Frequency = e.Frequency,
                            IsOfficialThatpon = e.IsOfficialThatpon
                        });
                    }

                    var ruleList = new List<OrthographyRule>();
                    var seenRules = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var r in rules)
                    {
                        if (string.IsNullOrWhiteSpace(r.WrongForm) || string.IsNullOrWhiteSpace(r.CorrectForm))
                            continue;
                        string normWrong = UnicodeNormalizer.NormalizeCanonicalOrder(r.WrongForm.Trim());
                        if (!seenRules.Add(normWrong)) continue;

                        ruleList.Add(new OrthographyRule
                        {
                            Id = ruleList.Count + 1,
                            WrongForm = normWrong,
                            CorrectForm = UnicodeNormalizer.NormalizeCanonicalOrder(r.CorrectForm.Trim()),
                            Category = r.Category ?? "Spelling",
                            Explanation = r.Explanation ?? string.Empty
                        });
                    }

                    writer.Write(entryList.Count);
                    foreach (var e in entryList)
                    {
                        writer.Write(e.Id);
                        writer.Write(e.Word);
                        writer.Write(e.MeaningCue);
                        writer.Write(e.PosTag);
                        writer.Write(e.PhoneticKey);
                        writer.Write(e.UsageNote);
                        writer.Write(e.Frequency);
                        writer.Write(e.IsOfficialThatpon);
                    }

                    writer.Write(ruleList.Count);
                    foreach (var r in ruleList)
                    {
                        writer.Write(r.Id);
                        writer.Write(r.WrongForm);
                        writer.Write(r.CorrectForm);
                        writer.Write(r.Category);
                        writer.Write(r.Explanation);
                    }

                    var ctxList = new List<ContextualRule>();
                    if (contextRules != null)
                    {
                        foreach (var cr in contextRules)
                        {
                            if (cr == null || string.IsNullOrWhiteSpace(cr.WrongForm) || string.IsNullOrWhiteSpace(cr.CorrectForm) || string.IsNullOrWhiteSpace(cr.TriggerWords))
                                continue;
                            ctxList.Add(new ContextualRule
                            {
                                WrongForm = UnicodeNormalizer.NormalizeCanonicalOrder(cr.WrongForm.Trim()),
                                CorrectForm = UnicodeNormalizer.NormalizeCanonicalOrder(cr.CorrectForm.Trim()),
                                TriggerWords = UnicodeNormalizer.NormalizeCanonicalOrder(cr.TriggerWords.Trim()),
                                AntiTriggerWords = string.IsNullOrWhiteSpace(cr.AntiTriggerWords)
                                    ? string.Empty
                                    : UnicodeNormalizer.NormalizeCanonicalOrder(cr.AntiTriggerWords.Trim()),
                                Direction = cr.Direction ?? "Both",
                                Window = cr.Window > 0 ? cr.Window : 3,
                                IsWarningOnly = cr.IsWarningOnly,
                                SkipParticles = cr.SkipParticles,
                                Explanation = cr.Explanation ?? string.Empty
                            });
                        }
                    }
                    writer.Write(ctxList.Count);
                    foreach (var cr in ctxList)
                    {
                        writer.Write(cr.WrongForm);
                        writer.Write(cr.CorrectForm);
                        writer.Write(cr.TriggerWords);
                        writer.Write(cr.AntiTriggerWords ?? string.Empty);
                        writer.Write(cr.Direction ?? "Both");
                        writer.Write(cr.Window > 0 ? cr.Window : 3);
                        writer.Write(cr.IsWarningOnly);
                        writer.Write(cr.SkipParticles);
                        writer.Write(cr.Explanation ?? string.Empty);
                    }

                    var bgList = new List<WordBigramEntry>();
                    if (wordBigrams != null)
                    {
                        foreach (var bg in wordBigrams)
                        {
                            if (bg == null || bg.Count <= 0 || string.IsNullOrWhiteSpace(bg.LeftWord) || string.IsNullOrWhiteSpace(bg.RightWord))
                                continue;
                            bgList.Add(new WordBigramEntry
                            {
                                LeftWord = UnicodeNormalizer.NormalizeCanonicalOrder(bg.LeftWord.Trim()),
                                RightWord = UnicodeNormalizer.NormalizeCanonicalOrder(bg.RightWord.Trim()),
                                Count = bg.Count
                            });
                        }
                    }
                    writer.Write(bgList.Count);
                    foreach (var bg in bgList)
                    {
                        writer.Write(bg.LeftWord);
                        writer.Write(bg.RightWord);
                        writer.Write(bg.Count);
                    }
                }
                compressedPayload = rawMs.ToArray();
            }

            // 2. Encrypt with AES-256-CBC and authenticate with HMAC-SHA256
            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            byte[] aesKey;
            byte[] hmacKey;
            using (var kdf = new Rfc2898DeriveBytes(encryptionPassword, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
            {
                aesKey = kdf.GetBytes(32);  // 256-bit AES key
                hmacKey = kdf.GetBytes(32); // 256-bit HMAC key
            }

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = aesKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var encMs = new MemoryStream())
                using (var cs = new CryptoStream(encMs, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(compressedPayload, 0, compressedPayload.Length);
                    cs.FlushFinalBlock();
                    ciphertext = encMs.ToArray();
                }
            }

            byte[] hmacTag;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                hmac.TransformBlock(salt, 0, salt.Length, null, 0);
                hmac.TransformBlock(iv, 0, iv.Length, null, 0);
                hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                hmacTag = hmac.Hash!;
            }

            using (var fs = new FileStream(_databasePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(FileMagic, 0, FileMagic.Length);
                fs.Write(salt, 0, salt.Length);
                fs.Write(iv, 0, iv.Length);
                fs.Write(hmacTag, 0, hmacTag.Length);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
        }

        /// <summary>
        /// Ensures the encrypted dictionary file exists on disk; if missing, compiles the official seed dictionary.
        /// </summary>
        public void EnsureDatabaseExists(string encryptionPassword)
        {
            if (!File.Exists(_databasePath))
            {
                CompileEncryptedDatabase(
                    encryptionPassword,
                    SeedDictionaryProvider.GetOfficialSeedEntries(),
                    SeedDictionaryProvider.GetOfficialOrthographyRules());
            }
        }

        /// <summary>
        /// Verifies the HMAC-SHA256 signature, decrypts the AES-256 database in memory, and populates a MyanmarTrie.
        /// Throws CryptographicException if the password is wrong or the database was tampered with.
        /// </summary>
        public MyanmarTrie LoadIntoTrie(string encryptionPassword)
        {
            if (!File.Exists(_databasePath))
                throw new FileNotFoundException("Encrypted dictionary database not found.", _databasePath);

            byte[] fileBytes = File.ReadAllBytes(_databasePath);
            int headerLen = FileMagic.Length + 16 + 16 + 32;
            if (fileBytes.Length <= headerLen)
                throw new CryptographicException("Encrypted dictionary file is truncated or invalid.");

            bool isV5 = true;
            bool isV4 = true;
            bool isV3 = true;
            bool isV2 = true;
            for (int i = 0; i < FileMagic.Length; i++)
            {
                if (fileBytes[i] != FileMagic[i]) isV5 = false;
                if (fileBytes[i] != FileMagicV4[i]) isV4 = false;
                if (fileBytes[i] != FileMagicV3[i]) isV3 = false;
                if (fileBytes[i] != FileMagicV2[i]) isV2 = false;
            }
            if (!isV5 && !isV4 && !isV3 && !isV2)
                throw new CryptographicException("Invalid dictionary file header.");

            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            byte[] expectedHmac = new byte[32];
            int offset = FileMagic.Length;

            Buffer.BlockCopy(fileBytes, offset, salt, 0, 16); offset += 16;
            Buffer.BlockCopy(fileBytes, offset, iv, 0, 16); offset += 16;
            Buffer.BlockCopy(fileBytes, offset, expectedHmac, 0, 32); offset += 32;

            int cipherLen = fileBytes.Length - offset;
            byte[] ciphertext = new byte[cipherLen];
            Buffer.BlockCopy(fileBytes, offset, ciphertext, 0, cipherLen);

            byte[] aesKey;
            byte[] hmacKey;
            using (var kdf = new Rfc2898DeriveBytes(encryptionPassword, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
            {
                aesKey = kdf.GetBytes(32);
                hmacKey = kdf.GetBytes(32);
            }

            byte[] actualHmac;
            using (var hmac = new HMACSHA256(hmacKey))
            {
                hmac.TransformBlock(salt, 0, salt.Length, null, 0);
                hmac.TransformBlock(iv, 0, iv.Length, null, 0);
                hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                actualHmac = hmac.Hash!;
            }

            int diff = 0;
            for (int i = 0; i < 32; i++)
            {
                diff |= expectedHmac[i] ^ actualHmac[i];
            }
            if (diff != 0)
            {
                throw new CryptographicException("Invalid encryption key or tampered dictionary database.");
            }

            byte[] compressedPayload;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Key = aesKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decMs = new MemoryStream())
                using (var cs = new CryptoStream(decMs, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(ciphertext, 0, ciphertext.Length);
                    cs.FlushFinalBlock();
                    compressedPayload = decMs.ToArray();
                }
            }

            var trie = new MyanmarTrie();
            using (var compMs = new MemoryStream(compressedPayload))
            using (var gzip = new GZipStream(compMs, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip, Encoding.UTF8))
            {
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    var entry = new DictionaryEntry
                    {
                        Id = reader.ReadInt32(),
                        Word = reader.ReadString(),
                        MeaningCue = reader.ReadString(),
                        PosTag = reader.ReadString(),
                        PhoneticKey = reader.ReadString(),
                        UsageNote = reader.ReadString()
                    };
                    if (isV3 || isV4 || isV5)
                    {
                        entry.Frequency = reader.ReadInt32();
                        entry.IsOfficialThatpon = reader.ReadBoolean();
                    }
                    trie.InsertEntry(entry, isPreNormalized: true);
                }

                int ruleCount = reader.ReadInt32();
                for (int i = 0; i < ruleCount; i++)
                {
                    trie.InsertOrthographyRule(new OrthographyRule
                    {
                        Id = reader.ReadInt32(),
                        WrongForm = reader.ReadString(),
                        CorrectForm = reader.ReadString(),
                        Category = reader.ReadString(),
                        Explanation = reader.ReadString()
                    }, isPreNormalized: true);
                }

                if (isV5 || isV4)
                {
                    int ctxCount = reader.ReadInt32();
                    for (int i = 0; i < ctxCount; i++)
                    {
                        if (isV5)
                        {
                            trie.InsertContextualRule(new ContextualRule
                            {
                                WrongForm = reader.ReadString(),
                                CorrectForm = reader.ReadString(),
                                TriggerWords = reader.ReadString(),
                                AntiTriggerWords = reader.ReadString(),
                                Direction = reader.ReadString(),
                                Window = reader.ReadInt32(),
                                IsWarningOnly = reader.ReadBoolean(),
                                SkipParticles = reader.ReadBoolean(),
                                Explanation = reader.ReadString()
                            }, isPreNormalized: true);
                        }
                        else
                        {
                            trie.InsertContextualRule(new ContextualRule
                            {
                                WrongForm = reader.ReadString(),
                                CorrectForm = reader.ReadString(),
                                TriggerWords = reader.ReadString(),
                                Direction = reader.ReadString(),
                                Window = reader.ReadInt32(),
                                Explanation = reader.ReadString()
                            }, isPreNormalized: true);
                        }
                    }

                    int bgCount = reader.ReadInt32();
                    for (int i = 0; i < bgCount; i++)
                    {
                        string lw = reader.ReadString();
                        string rw = reader.ReadString();
                        int cnt = reader.ReadInt32();
                        trie.InsertWordBigram(lw, rw, cnt, isPreNormalized: true);
                    }
                }
            }

            return trie;
        }

        /// <summary>
        /// Parses a UTF-8 CSV file of "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း" entries (Columns: Word,MeaningCue,PosTag,UsageNote).
        /// </summary>
        public static List<DictionaryEntry> ParseEntriesFromCsv(string csvFilePath)
        {
            var entries = new List<DictionaryEntry>();
            if (!File.Exists(csvFilePath))
                return entries;

            string[] lines = File.ReadAllLines(csvFilePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                if (i == 0 && line.StartsWith("Word,", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = line.Split(',');
                string word = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(word))
                    continue;

                entries.Add(new DictionaryEntry
                {
                    Word = word,
                    MeaningCue = parts.Length > 1 ? parts[1].Trim() : string.Empty,
                    PosTag = parts.Length > 2 ? parts[2].Trim() : "N",
                    UsageNote = parts.Length > 3 ? parts[3].Trim() : string.Empty
                });
            }

            return entries;
        }

        /// <summary>
        /// Parses a structured UTF-8 JSON file (such as data/mlc_thatpon_master.json).
        /// </summary>
        public static MasterDictionaryJsonModel ParseFromJsonFile(string jsonFilePath)
        {
            if (!File.Exists(jsonFilePath))
                return new MasterDictionaryJsonModel();

            byte[] bytes = File.ReadAllBytes(jsonFilePath);
            using (var ms = new MemoryStream(bytes))
            {
                var serializer = new DataContractJsonSerializer(typeof(MasterDictionaryJsonModel));
                return (MasterDictionaryJsonModel?)serializer.ReadObject(ms) ?? new MasterDictionaryJsonModel();
            }
        }
    }
}
