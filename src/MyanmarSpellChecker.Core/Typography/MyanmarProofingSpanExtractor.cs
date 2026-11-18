// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;

namespace MyanmarSpellChecker.Core.Typography
{
    public readonly struct MyanmarTextSpan
    {
        public int Start { get; }
        public int Length { get; }
        public string Text { get; }

        public MyanmarTextSpan(int start, int length, string text)
        {
            Start = start;
            Length = length;
            Text = text;
        }
    }

    /// <summary>
    /// Pure-managed Myanmar script span classifier and coalescer used by Office host add-ins
    /// to apply <c>NoProofing = 1</c> strictly onto Burmese text spans inside mixed Myanmar + English paragraphs
    /// while leaving English words (<c>Tiktok</c>, <c>Licencing</c>, <c>Thumainbayan</c>) 100% checked by Office's English spellchecker.
    /// </summary>
    public static class MyanmarProofingSpanExtractor
    {
        private const int MaxCoalescedSpanLength = 200;
        private const int MaxBridgeNeutralChars = 12;

        public static bool IsMyanmarScriptCodePoint(char c) =>
            (c >= '\u1000' && c <= '\u109F') ||
            (c >= '\uAA60' && c <= '\uAA7F') ||
            (c >= '\uA9E0' && c <= '\uA9FF');

        public static bool IsLatinLetter(char c) =>
            (c >= 'A' && c <= 'Z') ||
            (c >= 'a' && c <= 'z') ||
            (c >= '\u00C0' && c <= '\u024F');

        public static void ClassifyMyanmarAndLatinPresence(string? text, out bool hasMyanmar, out bool hasLatin)
        {
            hasMyanmar = false;
            hasLatin = false;
            if (string.IsNullOrEmpty(text))
                return;

            string s = text!;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (IsMyanmarScriptCodePoint(c))
                {
                    hasMyanmar = true;
                    if (hasLatin) return;
                }
                else if (IsLatinLetter(c))
                {
                    hasLatin = true;
                    if (hasMyanmar) return;
                }
            }
        }

        /// <summary>
        /// Extracts coalesced Burmese spans from mixed Myanmar + English text, bridging spaces and neutral punctuation
        /// ONLY when immediately followed by more Myanmar script characters (and never crossing Latin letters, tabs,
        /// control characters, or Word <c>^</c> Find escape tokens).
        /// </summary>
        public static List<MyanmarTextSpan> ExtractNonLatinMyanmarSpans(string? text)
        {
            var spans = new List<MyanmarTextSpan>(8);
            if (string.IsNullOrEmpty(text))
                return spans;

            string s = text!;
            int n = s.Length;
            int i = 0;

            while (i < n)
            {
                if (!IsMyanmarScriptCodePoint(s[i]))
                {
                    i++;
                    continue;
                }

                int spanStart = i;
                int lastMyanmarEnd = i + 1;
                i++;

                while (i < n && (i - spanStart) < MaxCoalescedSpanLength)
                {
                    char c = s[i];
                    if (IsMyanmarScriptCodePoint(c))
                    {
                        i++;
                        lastMyanmarEnd = i;
                        continue;
                    }

                    // Hard break on any Latin letter, control character (\t, \r, \n, \a, field markers), or Word Find '^'
                    if (IsLatinLetter(c) || c < ' ' || c == '^')
                        break;

                    // Tentatively peek across neutral spaces/digits/punctuation (e.g. "တွင် ပါဝင်သူများ" or "ကျပ် (၄၅)သိန်း")
                    // ONLY if another Myanmar character follows before any breaker or cap!
                    int peek = i;
                    int neutralCount = 0;
                    bool foundNextMyanmar = false;
                    while (peek < n &&
                           neutralCount < MaxBridgeNeutralChars &&
                           (peek - spanStart) < MaxCoalescedSpanLength)
                    {
                        char pc = s[peek];
                        if (IsMyanmarScriptCodePoint(pc))
                        {
                            foundNextMyanmar = true;
                            break;
                        }
                        if (IsLatinLetter(pc) || pc < ' ' || pc == '^')
                            break;

                        peek++;
                        neutralCount++;
                    }

                    if (foundNextMyanmar)
                    {
                        i = peek + 1;
                        lastMyanmarEnd = i;
                    }
                    else
                    {
                        break;
                    }
                }

                int len = lastMyanmarEnd - spanStart;
                if (len > 0)
                {
                    spans.Add(new MyanmarTextSpan(spanStart, len, s.Substring(spanStart, len)));
                }
                i = Math.Max(i, lastMyanmarEnd);
            }

            return spans;
        }
    }
}
