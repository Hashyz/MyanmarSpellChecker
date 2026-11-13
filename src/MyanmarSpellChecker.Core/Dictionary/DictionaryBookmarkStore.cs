using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyanmarSpellChecker.Core.Nlp;

namespace MyanmarSpellChecker.Core.Dictionary
{
    /// <summary>
    /// Persistent offline Dictionary Bookmark store shared live between Microsoft Word and Microsoft Excel
    /// via %LocalAppData%\MyanmarSpellChecker\dictionary_bookmarks.txt.
    /// Automatically reloads when the underlying file timestamp changes in another Office application.
    /// </summary>
    public sealed class DictionaryBookmarkStore
    {
        private readonly string _filePath;
        private readonly object _syncLock = new object();
        private readonly List<string> _orderedWords = new List<string>();
        private readonly HashSet<string> _wordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastLoadedWriteTimeUtc = DateTime.MinValue;
        private long _lastLoadedLength = -1;

        public static DictionaryBookmarkStore Shared { get; } = new DictionaryBookmarkStore();

        public DictionaryBookmarkStore(string? customFilePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customFilePath))
            {
                _filePath = customFilePath!;
            }
            else
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyanmarSpellChecker");
                _filePath = Path.Combine(dir, "dictionary_bookmarks.txt");
            }

            EnsureFreshUnderLock();
        }

        public int Count
        {
            get
            {
                lock (_syncLock)
                {
                    EnsureFreshUnderLock();
                    return _orderedWords.Count;
                }
            }
        }

        public bool IsBookmarked(string? word)
        {
            string norm = NormalizeKey(word);
            if (string.IsNullOrEmpty(norm))
                return false;

            lock (_syncLock)
            {
                EnsureFreshUnderLock();
                return _wordSet.Contains(norm);
            }
        }

        /// <summary>
        /// Toggles bookmark state for <paramref name="word"/>. Returns true if now bookmarked, false if removed.
        /// </summary>
        public bool ToggleBookmark(string? word)
        {
            string norm = NormalizeKey(word);
            if (string.IsNullOrEmpty(norm))
                return false;

            lock (_syncLock)
            {
                EnsureFreshUnderLock();
                bool added;
                if (_wordSet.Remove(norm))
                {
                    _orderedWords.RemoveAll(x => string.Equals(x, norm, StringComparison.OrdinalIgnoreCase));
                    added = false;
                }
                else
                {
                    _wordSet.Add(norm);
                    _orderedWords.Insert(0, norm); // Most recently saved first
                    added = true;
                }

                SaveUnderLock();
                return added;
            }
        }

        public IReadOnlyList<string> GetBookmarkedWords()
        {
            lock (_syncLock)
            {
                EnsureFreshUnderLock();
                return _orderedWords.ToArray();
            }
        }

        private static string NormalizeKey(string? word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;
            return UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word!.Trim()));
        }

        private void EnsureFreshUnderLock()
        {
            try
            {
                var fi = new FileInfo(_filePath);
                if (!fi.Exists)
                {
                    if (_lastLoadedWriteTimeUtc != DateTime.MinValue || _lastLoadedLength != -1)
                    {
                        _orderedWords.Clear();
                        _wordSet.Clear();
                        _lastLoadedWriteTimeUtc = DateTime.MinValue;
                        _lastLoadedLength = -1;
                    }
                    return;
                }

                DateTime writeUtc = fi.LastWriteTimeUtc;
                long len = fi.Length;
                if (writeUtc == _lastLoadedWriteTimeUtc && len == _lastLoadedLength)
                    return;

                _orderedWords.Clear();
                _wordSet.Clear();

                string[] lines = File.ReadAllLines(_filePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string norm = NormalizeKey(lines[i]);
                    if (!string.IsNullOrEmpty(norm) && _wordSet.Add(norm))
                    {
                        _orderedWords.Add(norm);
                    }
                }

                _lastLoadedWriteTimeUtc = writeUtc;
                _lastLoadedLength = len;
            }
            catch
            {
                // Ignore transient file lock or permission error
            }
        }

        private void SaveUnderLock()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllLines(_filePath, _orderedWords, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                var fi = new FileInfo(_filePath);
                _lastLoadedWriteTimeUtc = fi.LastWriteTimeUtc;
                _lastLoadedLength = fi.Length;
            }
            catch
            {
                // Ignore disk write failure in read-only sandbox
            }
        }
    }
}

