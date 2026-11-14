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
        private readonly object _verifiedSessionLock = new object();
        private string? _verifiedFullPath;
        private long _verifiedFileLength;
        private long _verifiedLastWriteTicksUtc;
        private string? _verifiedPassword;
        private byte[]? _verifiedAesKey;
        private byte[]? _verifiedIv;
        private int _verifiedHeaderLen;
        private bool _verifiedIsV3;
        private bool _verifiedIsV4;
        private bool _verifiedIsV5;

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

        private sealed class FastBinaryReader : BinaryReader
        {
            private byte[] _byteBuffer = new byte[4096];
            private char[] _charBuffer = new char[4096];

            public FastBinaryReader(Stream input) : base(input, Encoding.UTF8, leaveOpen: true)
            {
            }

            public string ReadFastString()
            {
                int byteLen = Read7BitEncodedInt();
                if (byteLen <= 0)
                    return string.Empty;

                if (byteLen > _byteBuffer.Length)
                {
                    int grown = Math.Max(byteLen, _byteBuffer.Length * 2);
                    _byteBuffer = new byte[grown];
                    _charBuffer = new char[grown];
                }

                Stream stream = BaseStream;
                int offset = 0;
                while (offset < byteLen)
                {
                    int read = stream.Read(_byteBuffer, offset, byteLen - offset);
                    if (read <= 0)
                        throw new EndOfStreamException("Unexpected end of dictionary stream while reading string.");
                    offset += read;
                }

                int charCount = Encoding.UTF8.GetChars(_byteBuffer, 0, byteLen, _charBuffer, 0);
                return new string(_charBuffer, 0, charCount);
            }

            public void SkipString()
            {
                int byteLen = Read7BitEncodedInt();
                Stream stream = BaseStream;
                while (byteLen > 0)
                {
                    int toRead = byteLen < _byteBuffer.Length ? byteLen : _byteBuffer.Length;
                    int read = stream.Read(_byteBuffer, 0, toRead);
                    if (read <= 0)
                        throw new EndOfStreamException("Unexpected end of dictionary stream while skipping field.");
                    byteLen -= read;
                }
            }

            public int ReadRawBytesInto(ref byte[] target)
            {
                int byteLen = Read7BitEncodedInt();
                if (byteLen <= 0)
                    return 0;

                if (byteLen > target.Length)
                {
                    target = new byte[Math.Max(byteLen, target.Length * 2)];
                }

                Stream stream = BaseStream;
                int offset = 0;
                while (offset < byteLen)
                {
                    int read = stream.Read(target, offset, byteLen - offset);
                    if (read <= 0)
                        throw new EndOfStreamException("Unexpected end of dictionary stream while reading raw field.");
                    offset += read;
                }
                return byteLen;
            }

            public string DecodeUtf8(byte[] buf, int byteLen)
            {
                if (byteLen <= 0)
                    return string.Empty;

                if (byteLen > _charBuffer.Length)
                {
                    _charBuffer = new char[Math.Max(byteLen, _charBuffer.Length * 2)];
                }

                int charCount = Encoding.UTF8.GetChars(buf, 0, byteLen, _charBuffer, 0);
                return new string(_charBuffer, 0, charCount);
            }
        }

        private static bool MatchAsciiCaseInsensitive(byte[] buf, int len, string upperAscii)
        {
            if (len != upperAscii.Length)
                return false;
            for (int i = 0; i < len; i++)
            {
                int b = buf[i];
                if (b >= 'a' && b <= 'z')
                    b -= 32;
                if (b != upperAscii[i])
                    return false;
            }
            return true;
        }

        private static bool IsReferencePosTagBytes(byte[] buf, int len, out bool isEncyc, out bool isEnLex)
        {
            isEncyc = false;
            isEnLex = false;
            if (len < 5 || len > 8)
                return false;

            if (len == 5 && MatchAsciiCaseInsensitive(buf, len, "ENCYC"))
            {
                isEncyc = true;
                return true;
            }
            if (len == 6)
            {
                if (MatchAsciiCaseInsensitive(buf, len, "EN_LEX"))
                {
                    isEnLex = true;
                    return true;
                }
                return MatchAsciiCaseInsensitive(buf, len, "SAYING");
            }
            if (len == 7)
                return MatchAsciiCaseInsensitive(buf, len, "PROVERB");
            if (len == 8)
                return MatchAsciiCaseInsensitive(buf, len, "RHETORIC");

            return false;
        }

        private static bool IsEncyclopediaPosTag(string posTag) =>
            string.Equals(posTag, "ENCYC", StringComparison.OrdinalIgnoreCase);

        private static bool IsBilingualLexiconPosTag(string posTag) =>
            string.Equals(posTag, "EN_LEX", StringComparison.OrdinalIgnoreCase);

        private static bool IsReferencePosTag(string posTag) =>
            !string.IsNullOrEmpty(posTag) &&
            (string.Equals(posTag, "ENCYC", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(posTag, "PROVERB", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(posTag, "SAYING", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(posTag, "RHETORIC", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(posTag, "EN_LEX", StringComparison.OrdinalIgnoreCase));

        private static string PoolIfShort(string value, Dictionary<string, string> pool, int maxLen = 64)
        {
            if (value.Length == 0)
                return string.Empty;
            if (value.Length > maxLen)
                return value;
            if (pool.TryGetValue(value, out string? existing))
                return existing;
            pool[value] = value;
            return value;
        }

        private void WithAuthenticatedReader(
            string encryptionPassword,
            Action<FastBinaryReader, bool, bool, bool> consumer)
        {
            if (!File.Exists(_databasePath))
                throw new FileNotFoundException("Encrypted dictionary database not found.", _databasePath);

            string fullPath = Path.GetFullPath(_databasePath);
            var fi = new FileInfo(fullPath);
            long fileLen = fi.Length;
            long writeTicksUtc = fi.LastWriteTimeUtc.Ticks;

            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536))
            {
                int headerLen = FileMagic.Length + 16 + 16 + 32;
                if (fs.Length <= headerLen)
                    throw new CryptographicException("Encrypted dictionary file is truncated or invalid.");

                byte[]? aesKey = null;
                byte[]? iv = null;
                bool isV3 = false;
                bool isV4 = false;
                bool isV5 = false;
                bool hasVerifiedSession = false;

                lock (_verifiedSessionLock)
                {
                    if (_verifiedAesKey != null &&
                        _verifiedIv != null &&
                        _verifiedHeaderLen == headerLen &&
                        _verifiedFileLength == fileLen &&
                        _verifiedLastWriteTicksUtc == writeTicksUtc &&
                        string.Equals(_verifiedFullPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(_verifiedPassword, encryptionPassword, StringComparison.Ordinal))
                    {
                        aesKey = _verifiedAesKey;
                        iv = _verifiedIv;
                        isV3 = _verifiedIsV3;
                        isV4 = _verifiedIsV4;
                        isV5 = _verifiedIsV5;
                        hasVerifiedSession = true;
                    }
                }

                if (!hasVerifiedSession)
                {
                    byte[] header = new byte[headerLen];
                    int headerRead = 0;
                    while (headerRead < headerLen)
                    {
                        int n = fs.Read(header, headerRead, headerLen - headerRead);
                        if (n <= 0)
                            throw new CryptographicException("Encrypted dictionary file header is truncated.");
                        headerRead += n;
                    }

                    isV5 = true;
                    isV4 = true;
                    isV3 = true;
                    bool isV2 = true;
                    for (int i = 0; i < FileMagic.Length; i++)
                    {
                        if (header[i] != FileMagic[i]) isV5 = false;
                        if (header[i] != FileMagicV4[i]) isV4 = false;
                        if (header[i] != FileMagicV3[i]) isV3 = false;
                        if (header[i] != FileMagicV2[i]) isV2 = false;
                    }
                    if (!isV5 && !isV4 && !isV3 && !isV2)
                        throw new CryptographicException("Invalid dictionary file header.");

                    byte[] salt = new byte[16];
                    iv = new byte[16];
                    byte[] expectedHmac = new byte[32];
                    int offset = FileMagic.Length;
                    Buffer.BlockCopy(header, offset, salt, 0, 16); offset += 16;
                    Buffer.BlockCopy(header, offset, iv, 0, 16); offset += 16;
                    Buffer.BlockCopy(header, offset, expectedHmac, 0, 32);

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
                        byte[] chunk = new byte[65536];
                        int read;
                        while ((read = fs.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            hmac.TransformBlock(chunk, 0, read, null, 0);
                        }
                        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
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

                    lock (_verifiedSessionLock)
                    {
                        _verifiedFullPath = fullPath;
                        _verifiedFileLength = fileLen;
                        _verifiedLastWriteTicksUtc = writeTicksUtc;
                        _verifiedPassword = encryptionPassword;
                        _verifiedAesKey = aesKey;
                        _verifiedIv = iv;
                        _verifiedHeaderLen = headerLen;
                        _verifiedIsV3 = isV3;
                        _verifiedIsV4 = isV4;
                        _verifiedIsV5 = isV5;
                    }
                }

                // Rewind directly to ciphertext and stream FileStream -> CryptoStream -> GZipStream -> FastBinaryReader
                // with zero Large Object Heap (> 85 KB) byte[] allocations.
                fs.Position = headerLen;
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Key = aesKey!;
                    aes.IV = iv!;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var cs = new CryptoStream(fs, decryptor, CryptoStreamMode.Read))
                    {
                        using (var gzip = new GZipStream(cs, CompressionMode.Decompress, leaveOpen: true))
                        using (var buffered = new BufferedStream(gzip, 65536))
                        using (var reader = new FastBinaryReader(buffered))
                        {
                            consumer(reader, isV3, isV4, isV5);
                        }

                        // When a consumer exits early (e.g. LoadReferenceEntriesOnly stops right after entries),
                        // drain remaining AES blocks directly without GZip decompression so CryptoStream.Dispose()
                        // validates PKCS7 padding on the true final block rather than mid-stream ciphertext.
                        if (!cs.HasFlushedFinalBlock)
                        {
                            byte[] drain = new byte[65536];
                            while (cs.Read(drain, 0, drain.Length) > 0)
                            {
                            }
                        }
                    }
                }
            }
        }

        private List<DictionaryEntry> LoadReferenceEntriesOnly(string encryptionPassword)
        {
            var refEntries = new List<DictionaryEntry>(86000);
            if (!File.Exists(_databasePath))
                return refEntries;

            WithAuthenticatedReader(encryptionPassword, (reader, isV3, isV4, isV5) =>
            {
                int entryCount = reader.ReadInt32();
                var tagPool = new Dictionary<string, string>(64, StringComparer.Ordinal);
                byte[] wordBuf = new byte[512];
                byte[] cueBuf = new byte[4096];
                byte[] tagBuf = new byte[64];

                for (int i = 0; i < entryCount; i++)
                {
                    int id = reader.ReadInt32();
                    int wordLen = reader.ReadRawBytesInto(ref wordBuf);
                    int cueLen = reader.ReadRawBytesInto(ref cueBuf);
                    int tagLen = reader.ReadRawBytesInto(ref tagBuf);

                    if (IsReferencePosTagBytes(tagBuf, tagLen, out _, out _))
                    {
                        string word = reader.DecodeUtf8(wordBuf, wordLen);
                        string meaningCue = reader.DecodeUtf8(cueBuf, cueLen);
                        string posTag = PoolIfShort(reader.DecodeUtf8(tagBuf, tagLen), tagPool, 32);
                        string phoneticKey = reader.ReadFastString();
                        string usageNote = reader.ReadFastString();
                        int freq = 0;
                        bool isOfficial = true;
                        if (isV3 || isV4 || isV5)
                        {
                            freq = reader.ReadInt32();
                            isOfficial = reader.ReadBoolean();
                        }

                        refEntries.Add(new DictionaryEntry
                        {
                            Id = id,
                            Word = word,
                            MeaningCue = PoolIfShort(meaningCue, tagPool, 16),
                            PosTag = posTag,
                            PhoneticKey = PoolIfShort(phoneticKey, tagPool, 16),
                            UsageNote = usageNote,
                            Frequency = freq,
                            IsOfficialThatpon = isOfficial
                        });
                    }
                    else
                    {
                        reader.SkipString(); // PhoneticKey
                        reader.SkipString(); // UsageNote
                        if (isV3 || isV4 || isV5)
                        {
                            reader.ReadInt32();
                            reader.ReadBoolean();
                        }
                    }
                }
            });

            return refEntries;
        }

        /// <summary>
        /// Verifies the HMAC-SHA256 signature, streams the AES-256 + GZip database with zero LOH allocations,
        /// interns shared strings, defers multi-paragraph Encyclopedia/Proverb bodies until Dictionary search, and populates a MyanmarTrie.
        /// Throws CryptographicException if the password is wrong or the database was tampered with.
        /// </summary>
        public MyanmarTrie LoadIntoTrie(string encryptionPassword)
        {
            bool isFullCorpus = File.Exists(_databasePath) && new FileInfo(_databasePath).Length >= 1_000_000;
            var trie = new MyanmarTrie(bulkCapacity: isFullCorpus);
            int deferredRefCount = 0;
            int deferredEncycCount = 0;
            int deferredBilingualCount = 0;

            WithAuthenticatedReader(encryptionPassword, (reader, isV3, isV4, isV5) =>
            {
                int entryCount = reader.ReadInt32();
                bool deferReferenceEntries = entryCount >= 10000;
                var pool = new Dictionary<string, string>(entryCount >= 10000 ? 120000 : 512, StringComparer.Ordinal);
                var sylBuf = new List<string>(8);
                byte[] wordBuf = new byte[512];
                byte[] cueBuf = new byte[4096];
                byte[] tagBuf = new byte[64];

                for (int i = 0; i < entryCount; i++)
                {
                    int id = reader.ReadInt32();
                    int wordLen = reader.ReadRawBytesInto(ref wordBuf);
                    int cueLen = reader.ReadRawBytesInto(ref cueBuf);
                    int tagLen = reader.ReadRawBytesInto(ref tagBuf);

                    if (deferReferenceEntries && IsReferencePosTagBytes(tagBuf, tagLen, out bool isEncyc, out bool isEnLex))
                    {
                        reader.SkipString(); // PhoneticKey
                        reader.SkipString(); // UsageNote (skips multi-paragraph ENCYC body without UTF-8 string allocation)
                        if (isV3 || isV4 || isV5)
                        {
                            reader.ReadInt32();
                            reader.ReadBoolean();
                        }
                        deferredRefCount++;
                        if (isEncyc)
                            deferredEncycCount++;
                        else if (isEnLex)
                            deferredBilingualCount++;
                        continue;
                    }

                    string word = reader.DecodeUtf8(wordBuf, wordLen);
                    string meaningCue = reader.DecodeUtf8(cueBuf, cueLen);
                    string posTag = PoolIfShort(reader.DecodeUtf8(tagBuf, tagLen), pool, 32);
                    string phoneticKey = reader.ReadFastString();
                    string usageNote = reader.ReadFastString();
                    int freq = 0;
                    bool isOfficial = true;
                    if (isV3 || isV4 || isV5)
                    {
                        freq = reader.ReadInt32();
                        isOfficial = reader.ReadBoolean();
                    }

                    var entry = new DictionaryEntry
                    {
                        Id = id,
                        Word = word.Length <= 10 ? PoolIfShort(word, pool, 10) : word,
                        MeaningCue = meaningCue.Length == 0 ? string.Empty : PoolIfShort(meaningCue, pool, 16),
                        PosTag = posTag,
                        PhoneticKey = PoolIfShort(phoneticKey, pool, 24),
                        UsageNote = usageNote.Length == 0 ? string.Empty : PoolIfShort(usageNote, pool, 16),
                        Frequency = freq,
                        IsOfficialThatpon = isOfficial
                    };
                    trie.InsertEntryBulkPreNormalized(entry, sylBuf, pool);
                }

                int ruleCount = reader.ReadInt32();
                for (int i = 0; i < ruleCount; i++)
                {
                    trie.InsertOrthographyRuleBulkPreNormalized(new OrthographyRule
                    {
                        Id = reader.ReadInt32(),
                        WrongForm = reader.ReadFastString(),
                        CorrectForm = reader.ReadFastString(),
                        Category = reader.ReadFastString(),
                        Explanation = reader.ReadFastString()
                    }, sylBuf, pool);
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
                                WrongForm = PoolIfShort(reader.ReadFastString(), pool, 64),
                                CorrectForm = PoolIfShort(reader.ReadFastString(), pool, 64),
                                TriggerWords = reader.ReadFastString(),
                                AntiTriggerWords = reader.ReadFastString(),
                                Direction = PoolIfShort(reader.ReadFastString(), pool, 16),
                                Window = reader.ReadInt32(),
                                IsWarningOnly = reader.ReadBoolean(),
                                SkipParticles = reader.ReadBoolean(),
                                Explanation = reader.ReadFastString()
                            }, isPreNormalized: true);
                        }
                        else
                        {
                            trie.InsertContextualRule(new ContextualRule
                            {
                                WrongForm = PoolIfShort(reader.ReadFastString(), pool, 64),
                                CorrectForm = PoolIfShort(reader.ReadFastString(), pool, 64),
                                TriggerWords = reader.ReadFastString(),
                                Direction = PoolIfShort(reader.ReadFastString(), pool, 16),
                                Window = reader.ReadInt32(),
                                Explanation = reader.ReadFastString()
                            }, isPreNormalized: true);
                        }
                    }

                    int bgCount = reader.ReadInt32();
                    for (int i = 0; i < bgCount; i++)
                    {
                        string lw = reader.ReadFastString();
                        string rw = reader.ReadFastString();
                        int cnt = reader.ReadInt32();
                        trie.InsertWordBigramBulkPreNormalized(lw, rw, cnt, pool);
                    }
                }
            });

            if (deferredRefCount > 0)
            {
                trie.AttachLazyReferenceCorpus(
                    deferredRefCount,
                    deferredEncycCount,
                    deferredBilingualCount,
                    () => LoadReferenceEntriesOnly(encryptionPassword));
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

            using (var fs = new FileStream(jsonFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536))
            {
                var serializer = new DataContractJsonSerializer(typeof(MasterDictionaryJsonModel));
                return (MasterDictionaryJsonModel?)serializer.ReadObject(fs) ?? new MasterDictionaryJsonModel();
            }
        }
    }
}
