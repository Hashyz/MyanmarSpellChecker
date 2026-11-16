// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)

using System;
using System.Globalization;
using System.IO;

namespace MyanmarSpellChecker.Core.Performance
{
    public sealed class ReviewWindowPreferences
    {
        public bool HasSavedBounds { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPinnedTopMost { get; set; } = true;
        public bool IsGroupedByWord { get; set; }
        public bool PreferDockedInTaskPane { get; set; }
    }

    /// <summary>
    /// Persists user-chosen position, size, pin-on-top, grouping, and dock preferences for the modeless
    /// Review &amp; Fix studio across Word and Excel sessions (%LocalAppData%\MyanmarSpellChecker\ui_review_window.cfg).
    /// </summary>
    public static class ReviewWindowPreferencesStore
    {
        private static string GetConfigFilePath(string hostTag)
        {
            string safeHost = string.IsNullOrWhiteSpace(hostTag) ? "office" : hostTag.Trim().ToLowerInvariant();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "MyanmarSpellChecker", $"ui_review_window_{safeHost}.cfg");
        }

        public static ReviewWindowPreferences Load(string hostTag)
        {
            var prefs = new ReviewWindowPreferences();
            try
            {
                string path = GetConfigFilePath(hostTag);
                if (!File.Exists(path))
                    return prefs;

                string[] lines = File.ReadAllLines(path);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    int eq = rawLine.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = rawLine.Substring(0, eq).Trim();
                    string val = rawLine.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "HasBounds":
                            if (bool.TryParse(val, out bool hb)) prefs.HasSavedBounds = hb;
                            break;
                        case "X":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) prefs.X = x;
                            break;
                        case "Y":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) prefs.Y = y;
                            break;
                        case "W":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) prefs.Width = w;
                            break;
                        case "H":
                            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) prefs.Height = h;
                            break;
                        case "Pin":
                            if (bool.TryParse(val, out bool pin)) prefs.IsPinnedTopMost = pin;
                            break;
                        case "Group":
                            if (bool.TryParse(val, out bool grp)) prefs.IsGroupedByWord = grp;
                            break;
                        case "Docked":
                            if (bool.TryParse(val, out bool dck)) prefs.PreferDockedInTaskPane = dck;
                            break;
                    }
                }
            }
            catch
            {
                // Ignore corrupt local preferences; fall back to safe defaults
            }

            return prefs;
        }

        public static void Save(string hostTag, ReviewWindowPreferences prefs)
        {
            if (prefs == null)
                return;

            try
            {
                string path = GetConfigFilePath(hostTag);
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string content = string.Join(
                    Environment.NewLine,
                    $"HasBounds={prefs.HasSavedBounds}",
                    $"X={prefs.X.ToString(CultureInfo.InvariantCulture)}",
                    $"Y={prefs.Y.ToString(CultureInfo.InvariantCulture)}",
                    $"W={prefs.Width.ToString(CultureInfo.InvariantCulture)}",
                    $"H={prefs.Height.ToString(CultureInfo.InvariantCulture)}",
                    $"Pin={prefs.IsPinnedTopMost}",
                    $"Group={prefs.IsGroupedByWord}",
                    $"Docked={prefs.PreferDockedInTaskPane}");

                File.WriteAllText(path, content);
            }
            catch
            {
                // Ignore read-only disk errors
            }
        }
    }
}

