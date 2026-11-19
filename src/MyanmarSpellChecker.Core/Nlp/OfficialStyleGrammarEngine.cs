// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    public sealed partial class OfficialStyleGrammarEngine
    {
        public List<ProofingIssue> AnalyzeGrammarAndStyle(
            string text,
            ProofingStyleMode styleMode = ProofingStyleMode.FormalOfficial,
            MyanmarTrie? trie = null)
        {
            var issues = new List<ProofingIssue>();
            if (string.IsNullOrWhiteSpace(text))
                return issues;

            // Single-pass O(N) character/bigram + 64-bit Myanmar character register mask scan
            ulong mmMask = 0UL;
            bool hasZeroWidthOrDotted = false;
            bool hasLaKaungTrigger = false;
            bool hasVisarga = false;
            bool hasMyanmarDigit = false;
            bool hasNonZeroMyanmarDigit = false;
            bool hasHorizSpace = false;
            bool hasMyanmarSpaceMyanmar = false;
            bool hasShortMyanmarBeforeMyanmar = false;
            bool hasSpaceBeforeCombiningMark = false;
            bool hasStrayVowelAfterAsat = false;
            bool hasAsatVirama = false;
            bool hasTa = false;
            bool hasMa = false;
            bool hasCreakyDot = false;
            bool hasPaukMaGyi = false;
            bool hasConsecutiveMyanmarPunct = false;
            bool hasSpaceBeforeMyanmarPunct = false;
            bool hasBracketOrSmartQuote = false;
            bool hasAsciiDigit = false;
            bool hasAsciiLetter = false;
            bool hasNonWsBefore = false;
            bool hasNrcDelimiter = false;
            bool hasNrcSlashOrDash = false;
            bool prevWasHorizSpace = false;
            int currentMmRunLen = 0;
            int mmRunBeforeHorizSpace = 0;
            char prevChar = '\0';

            for (int idx = 0; idx < text.Length; idx++)
            {
                char c = text[idx];
                if (c == ' ' || c == '\t')
                {
                    if (!prevWasHorizSpace)
                    {
                        mmRunBeforeHorizSpace = currentMmRunLen;
                    }
                    currentMmRunLen = 0;
                    hasHorizSpace = true;
                    prevWasHorizSpace = true;
                    prevChar = c;
                    continue;
                }

                bool wasHorizSpaceBefore = prevWasHorizSpace;
                int mmDelta = c - '\u1000';
                if ((uint)mmDelta < 64u)
                {
                    mmMask |= 1UL << mmDelta;
                    if (prevWasHorizSpace)
                    {
                        if (c >= '\u102B' && c <= '\u103E')
                            hasSpaceBeforeCombiningMark = true;
                        if (mmRunBeforeHorizSpace > 0)
                        {
                            hasMyanmarSpaceMyanmar = true;
                            if (mmRunBeforeHorizSpace <= 12)
                                hasShortMyanmarBeforeMyanmar = true;
                        }
                    }
                    currentMmRunLen++;
                }
                else
                {
                    currentMmRunLen = 0;
                }
                prevWasHorizSpace = false;

                if (c == '/' || c == '\\' || c == '-' || c == '–')
                {
                    hasNrcDelimiter = true;
                    hasNrcSlashOrDash = true;
                }
                else if (c == '(' || c == '（')
                {
                    hasNrcDelimiter = true;
                    hasBracketOrSmartQuote = true;
                }
                else if (c == ')' || c == '）' || c == '[' || c == ']' || c == '【' || c == '】' ||
                         c == '\u201C' || c == '\u201D' || c == '"')
                {
                    hasBracketOrSmartQuote = true;
                }
                else if (c >= '0' && c <= '9')
                {
                    hasAsciiDigit = true;
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    hasAsciiLetter = true;
                }

                if (c >= '\u1000' && c <= '\u104F')
                {
                    if (c == 'တ') hasTa = true;
                    else if (c == 'မ') hasMa = true;
                    else if (c == '\u1037') hasCreakyDot = true;
                    else if (c == '\u1038') hasVisarga = true;
                    else if (c == '\u104B')
                    {
                        hasPaukMaGyi = true;
                        if (prevChar == '\u104A' || prevChar == '\u104B') hasConsecutiveMyanmarPunct = true;
                        if (wasHorizSpaceBefore && hasNonWsBefore) hasSpaceBeforeMyanmarPunct = true;
                    }
                    else if (c == '\u104A')
                    {
                        if (prevChar == '\u104A' || prevChar == '\u104B') hasConsecutiveMyanmarPunct = true;
                        if (wasHorizSpaceBefore && hasNonWsBefore) hasSpaceBeforeMyanmarPunct = true;
                    }
                    else if (c == '\u104E' || c == '\u1044') hasLaKaungTrigger = true;
                    else if (c == '\u1039' && prevChar == '\u103A') hasAsatVirama = true;
                    else if (prevChar == '\u103A' && c >= '\u102D' && c <= '\u1032') hasStrayVowelAfterAsat = true;

                    if (c >= '\u1040' && c <= '\u1049')
                    {
                        hasMyanmarDigit = true;
                        if (c >= '\u1041') hasNonZeroMyanmarDigit = true;
                    }
                }
                else if ((c >= '\u200B' && c <= '\u200D') || c == '\uFEFF' || c == '\u25CC')
                {
                    hasZeroWidthOrDotted = true;
                }
                if (!char.IsWhiteSpace(c))
                    hasNonWsBefore = true;
                prevChar = c;
            }

            // 0. Zero-Width / Dotted-Circle Word Corruption (must run first so invisible U+200B..U+200D inside words are caught cleanly)
            if (hasZeroWidthOrDotted)
            {
                issues.AddRange(FindZeroWidthWordCorruptions(text));
            }

            // 0b. Government Unicode '၎င်း' check ('၄င်း', '၎င်းင်း', '၄င်းင်း', or bare '၎' without 'င်း')
            if (hasLaKaungTrigger)
            {
                foreach (Match m in MisspelledLaKaungMultiTokenPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "၎င်း",
                        ProofingIssueType.LookalikeCharacterError,
                        $"ရုံးသုံး ယူနီကုဒ်စံနှုန်းအရ '{g.Value}' အစား '၎င်း' (U+104E + င်း) ကို တိကျစွာ ရေးသားရမည်။",
                        "ရုံးသုံးနာမ်စား ('၎င်း')", "PRON", true,
                        "ပြည်ထောင်စုယူနီကုဒ် (Pyidaungsu Unicode) တွင် '၎င်း' ကို သင်္ကေတ '၎' (U+104E) နောက်၌ 'င်း' တစ်ကြိမ်သာတွဲ၍ ရေးသားရသည် ('၄' ဂဏန်းသုံးခြင်း သို့မဟုတ် '၎င်းင်း' ဟု နှစ်ထပ်ရိုက်ခြင်းသည် မှားယွင်းသည်)။",
                        "မြန်မာယူနီကုဒ်စံနှုန်း နှင့် ရုံးသုံးမြန်မာစာရေးထုံး");
                }
            }

            // 0c. Time notation using Myanmar visarga ('း', U+1038) instead of colon (':')
            if (hasVisarga && hasMyanmarDigit)
            {
                foreach (Match m in TimeVisargaInsteadOfColonPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string fixedTime = g.Value.Replace('\u1038', ':');
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, fixedTime,
                        ProofingIssueType.LookalikeCharacterError,
                        $"အချိန်နာရီ ဖော်ပြရာတွင် ဝစ္စပေါက် ('း') အစား အစက်နှစ်စက် (':') ကို သုံးရမည် — '{fixedTime}' ဟု ရေးသားရမည်။",
                        "အချိန်နာရီ စံရေးထုံး", "NUM", true,
                        "မြန်မာဂဏန်းနောက်၌ ဝစ္စပေါက် ('း' U+1038) ရိုက်နှိပ်ပါက ယူနီကုဒ်အစက်ဝိုင်း (Dotted Circle) ပေါ်တတ်သဖြင့် အချိန်နာရီတွင် ':' (Colon) ကိုသာ သုံးရမည်။",
                        "မြန်မာယူနီကုဒ်စံနှုန်း နှင့် ရုံးသုံးမြန်မာစာရေးထုံး");
                }
            }

            // 0d. Accidental space before combining vowel/medial/asat/tone mark inside a single syllable
            if (hasSpaceBeforeCombiningMark)
            {
                foreach (Match m in AccidentalSpaceBeforeCombiningMarkPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string joined = StripHorizontalWhitespaceRegex.Replace(g.Value, string.Empty);
                    string cleaned = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(joined));
                    var syls = MyanmarSyllableBreaker.BreakIntoSyllables(cleaned);
                    if (syls.Count == 1 && MyanmarSyllableBreaker.IsStructurallyValidSyllable(syls[0], out _))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, cleaned,
                            ProofingIssueType.LookalikeCharacterError,
                            $"သရ/အသတ်/သင်္ကေတရှေ့တွင် Space မှားခြားမိနေပါသည် — '{cleaned}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            "ယူနီကုဒ် စာလုံးပေါင်းအမှန်", "WORD", true,
                            "မြန်မာယူနီကုဒ်တွင် ဗျည်းနှင့် သရ၊ အသတ် ('်')၊ အောက်မြစ် ('့')၊ ဝစ္စပေါက် ('း') တို့ကြား၌ Space ခြားမိပါက စာလုံးပျက်စီး၍ အစက်ဝိုင်းပေါ်တတ်ပါသည်။",
                            "ပြည်ထောင်စုယူနီကုဒ်စံနှုန်း (ISO/IEC 10646)");
                    }
                }
            }

            // 0e. V29 Official Myanmar NRC Slot Validation (Card-type slot typos '(ပြူ)->(ပြု)', consonant 'ဝ' in NRC digits, & invalid State > 14 / 0)
            List<(int Start, int End)>? validNrcSpans = null;
            if (hasNrcDelimiter)
            {
                foreach (Match mNrc in ValidNrcCodeSpanPattern.Matches(text))
                {
                    validNrcSpans ??= new List<(int Start, int End)>(2);
                    validNrcSpans.Add((mNrc.Index, mNrc.Index + mNrc.Length));
                }

                if (ContainsMm(text, mmMask, "ပြူ") || ContainsMm(text, mmMask, "သာသနာ့"))
                {
                    foreach (Match mCard in MisspelledNrcCardTypePattern.Matches(text))
                    {
                        Group g = mCard.Groups["Target1"].Success ? mCard.Groups["Target1"] : mCard.Groups["Target2"];
                        string wrongCard = g.Value;
                        string rightCard = wrongCard == "ပြူ" ? "ပြု" : "သာသနာ";
                        string cardExplain = wrongCard == "ပြူ"
                            ? "နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမျိုးအစားသင်္ကေတ (MOIP) စံနှုန်းအရ နိုင်ငံသားပြုခွင့်ရသူအတွက် တစ်ချောင်းငင်ဖြင့် 'ပြု' ဟု ရေးသားရမည် ('ပြူ' မဟုတ်ပါ)။"
                            : "သာသနာဝင်စိစစ်ရေးကတ်ပြား အမျိုးအစားသင်္ကေတတွင် အောက်မြစ်မပါဘဲ 'သာသနာ' ဟု ရေးသားရမည်။";
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, wrongCard, rightCard,
                            ProofingIssueType.SpellingError,
                            cardExplain,
                            "နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမျိုးအစားသင်္ကေတ (MOIP)", "N", true,
                            cardExplain,
                            "လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP) နိုင်ငံသားစိစစ်ရေးကတ်ပြား စံနှုန်း");
                    }
                }

                if ((mmMask & (1UL << ('\u101D' - '\u1000'))) != 0UL)
                {
                    foreach (Match mWaSt in MisusedWaInNrcStatePattern.Matches(text))
                    {
                        Group g = mWaSt.Groups["Target"];
                        string fixedSt = g.Value.Replace('\u101D', '\u1040');
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, fixedSt,
                            ProofingIssueType.LookalikeCharacterError,
                            $"နိုင်ငံသားစိစစ်ရေးကတ်ပြား တိုင်းဒေသကြီး/ပြည်နယ်ဂဏန်းတွင် ဗျည်း 'ဝ' (U+101D) အစား သုညဂဏန်း '၀' (U+1040) ဖြင့် '{fixedSt}' ဟု ရေးသားရမည်။",
                            "နိုင်ငံသားစိစစ်ရေးကတ်ပြား ပြည်နယ်/တိုင်းဂဏန်း", "NUM", true,
                            "ဂဏန်းနေရာတွင် ဗျည်းအက္ခရာ 'ဝ' (U+101D) မှားယွင်းရိုက်နှိပ်မိနေပါသည် — သင်္ချာသုညဂဏန်း '၀' (U+1040) ကို သုံးရမည်။",
                            "ပြည်ထောင်စုယူနီကုဒ်စံနှုန်း နှင့် MOIP နိုင်ငံသားစိစစ်ရေးကတ်ပြား စံနှုန်း");
                    }

                    foreach (Match mWaSer in MisusedWaInNrcSerialPattern.Matches(text))
                    {
                        Group g = mWaSer.Groups["Target"];
                        string fixedSer = g.Value.Replace('\u101D', '\u1040');
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, fixedSer,
                            ProofingIssueType.LookalikeCharacterError,
                            $"နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမှတ်စဉ်ဂဏန်းအတွင်း ဗျည်း 'ဝ' (U+101D) အစား သုညဂဏန်း '၀' (U+1040) ဖြင့် '{fixedSer}' ဟု ရေးသားရမည်။",
                            "နိုင်ငံသားစိစစ်ရေးကတ်ပြား အမှတ်စဉ်ဂဏန်း", "NUM", true,
                            "မှတ်ပုံတင်ဂဏန်း ၆ လုံးအတွင်း ဗျည်းအက္ခရာ 'ဝ' (U+101D) ရောနှောနေပါသည် — သင်္ချာသုညဂဏန်း '၀' (U+1040) ကို သုံးရမည်။",
                            "ပြည်ထောင်စုယူနီကုဒ်စံနှုန်း နှင့် MOIP နိုင်ငံသားစိစစ်ရေးကတ်ပြား စံနှုန်း");
                    }
                }

                if (hasNrcSlashOrDash)
                {
                    foreach (Match mStRange in FullNrcStateRangeCheckPattern.Matches(text))
                    {
                        Group gSt = mStRange.Groups["State"];
                        int parsedSt = 0;
                        bool okSt = true;
                        for (int si = 0; si < gSt.Value.Length; si++)
                        {
                            char sc = gSt.Value[si];
                            int dVal = (sc >= '\u1040' && sc <= '\u1049') ? (sc - '\u1040')
                                     : ((sc >= '0' && sc <= '9') ? (sc - '0') : -1);
                            if (dVal < 0) { okSt = false; break; }
                            parsedSt = parsedSt * 10 + dVal;
                        }
                        if (okSt && (parsedSt < 1 || parsedSt > 14))
                        {
                            bool useMmDig = gSt.Value[0] >= '\u1040' && gSt.Value[0] <= '\u1049';
                            string sampleValid = parsedSt == 0 ? (useMmDig ? "၁" : "1") : (useMmDig ? "၁၂" : "12");
                            AddGrammarOrSpellingIssue(
                                issues, gSt.Index, gSt.Length, gSt.Value, sampleValid,
                                ProofingIssueType.SpellingError,
                                $"မြန်မာနိုင်ငံ နိုင်ငံသားစိစစ်ရေးကတ်ပြား တိုင်းဒေသကြီး/ပြည်နယ်သင်္ကေတဂဏန်းသည် (၁) မှ (၁၄) အတွင်းသာ ရှိပါသည် — '{gSt.Value}/' သည် တရားဝင်ပြည်နယ်/တိုင်းဂဏန်း မဟုတ်ပါ။",
                                "ပြည်နယ်/တိုင်းဒေသကြီး သင်္ကေတ (၁ မှ ၁၄)", "NUM", true,
                                "လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP) သတ်မှတ်ချက်အရ ပြည်နယ်နှင့် တိုင်းဒေသကြီး သင်္ကေတဂဏန်းများမှာ ၁ (ကချင်) မှ ၁၄ (ဧရာဝတီ) အထိသာ ရှိပါသည်။",
                                "လူဝင်မှုကြီးကြပ်ရေးနှင့် ပြည်သူ့အင်အားဝန်ကြီးဌာန (MOIP) စံနှုန်း");
                        }
                    }
                }
            }

            // 1. 'တ' vs 'တစ်' Numeral Classifier Rules
            if (hasTa)
            {
                foreach (Match m in TaCompoundNumeralPattern.Matches(text))
                {
                    string first = m.Groups["First"].Value;
                    string second = m.Groups["Second"].Value;
                    string correct = "တစ်" + first + "တစ်" + second;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.NumeralClassifierError,
                        $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈) အရ သင်္ချာဂဏန်း (၁) အရင်းခံ ပေါင်းစပ်ပုဒ်ကို စသတ်ဖြင့် '{correct}' ဟု ရေးသားရမည်။",
                        "သတ်ပုံကျမ်း ဂဏန်း (၁) ပေါင်းစပ်ပုဒ်", "ADV", true,
                        $"'{correct}' အတွင်းရှိ 'တစ်' နှစ်လုံးစလုံးသည် သင်္ချာဂဏန်း (၁) အရင်းခံပုဒ်များဖြစ်သဖြင့် စသတ်ဖြင့် ရေးသားရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈)");
                }

                foreach (Match m in TaNumeralClassifierPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length)) continue;
                    if (validNrcSpans != null)
                    {
                        bool insideNrc = false;
                        int mEnd = m.Index + m.Length;
                        for (int ni = 0; ni < validNrcSpans.Count; ni++)
                        {
                            if (m.Index < validNrcSpans[ni].End && mEnd > validNrcSpans[ni].Start)
                            {
                                insideNrc = true;
                                break;
                            }
                        }
                        if (insideNrc) continue;
                    }
                    if (IsShieldedLexicalTaStem(text, m.Index, m.Length, trie)) continue;
                    string classifier = m.Groups["Classifier"].Value;
                    string correct = "တစ်" + classifier;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.NumeralClassifierError,
                        $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ချာဂဏန်း (၁) အနက်ဆောင်သော မျိုးပြပစ္စည်းရှေ့တွင် '{correct}' ဟု စလုံးသတ်ဖြင့် ရေးသားရမည်။",
                        "ဂဏန်း (၁) နှင့် မျိုးပြပစ္စည်း (သတ်ပုံကျမ်း)", "NUM", true,
                        $"နောက်ကပ်လျက် '{classifier}' သည် မျိုးပြပစ္စည်း/နာမ်ဖြစ်၍ ရှေ့ရှိပုဒ်မှာ သင်္ချာဂဏန်း (၁) အရင်းခံပုဒ် ဖြစ်သည် — 'ရေးတော့အမှန် ဖတ်တော့အသံ' မူအရ စသတ်ဖြင့် '{correct}' ဟု ရေးရမည် (ကြိယာ/မြည်သံစွဲရှေ့ရှိ အာဂုံပစ္စည်း 'တ' ၌သာ စမသတ်ရ)။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၇..၈) နှင့် မြန်မာသဒ္ဒါ");
                }

                if (ContainsMm(text, mmMask, "တစ်"))
                {
                    foreach (Match m in HypercorrectedCompoundTaitPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        string correct = g.Value.Replace("တစ်", "တ");
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, correct,
                            ProofingIssueType.NumeralClassifierError,
                            $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၈) အရ အာဂုံပစ္စည်း 'တ' ကို စမသတ်ဘဲ '{correct}' ဟု ရေးသားရမည်။",
                            "သတ်ပုံကျမ်း အမှာစာ (စာ-၈) အာဂုံပစ္စည်း 'တ'", "ADV", true,
                            $"ပေါင်းစပ်ကြိယာဝိသေသနပုဒ် '{correct}' အတွင်းရှိ 'တ' သည် သင်္ချာကိန်းဂဏန်းမဟုတ်သော အာဂုံပစ္စည်းဖြစ်၍ စမသတ်ရပါ။",
                            "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၈)");
                    }

                    foreach (Match m in HypercorrectedTaitPattern.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, m.Index, m.Length)) continue;
                        string redup = m.Groups["Redup"].Value;
                        string correct = "တ" + redup;
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, correct,
                            ProofingIssueType.NumeralClassifierError,
                            $"သတ်ပုံကျမ်းအရ ဂဏန်း (၁) အနက်မဟုတ်သဖြင့် 'တစ်' အစား '{correct}' ဟု အသတ်မပါဘဲ ရေးသားရမည်။",
                            "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်", "ADV", true,
                            $"'{redup}' နှင့်တွဲသော ရှေ့ဆက်ပစ္စည်းသည် ရေတွက်နိုင်သည့် သင်္ချာဂဏန်း (၁) မဟုတ်ဘဲ ကြိယာဝိသေသန/နာမဝိသေသနပြု အာဂုံပစ္စည်း 'တ' ဖြစ်သဖြင့် စသတ်မထည့်ရပါ။",
                            "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း အမှာစာ (စာ-၈)");
                    }
                }
            }

            if (ContainsMm(text, mmMask, "ကနဲ"))
            {
                foreach (Match m in MisspelledIdeophoneKanaePattern.Matches(text))
                {
                    string stem = m.Groups["Stem"].Value;
                    string correct = stem + "ခနဲ";
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား (စာ-၃၃၇) “ကနဲ(ဝင်း...၊ ထိန်...) → ခနဲ(ဝင်း...၊ ထိန်...)” အရ '{m.Value}' အစား '{correct}' (ခကွေး) ဖြင့် ရေးသားရမည်။",
                        "သတ်ပုံကျမ်း ခွဲထား (စာ-၃၃၇) ရုတ်ခြည်းပြပစ္စည်း '~ခနဲ'", "ADV", true,
                        "ရုတ်ခြည်းဖြစ်ပေါ်ပုံပြ ကြိယာဝိသေသနနောက်ဆက်ပစ္စည်းကို အသံထွက်၌ '/ဂနဲ/' ဟု ဖတ်သော်လည်း ရေးထုံး၌ 'ခနဲ' (ခကွေး) ဖြင့်သာ ရေးရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား (စာ-၃၃၇)");
                }
            }

            // 2. 'ခုနစ်' vs 'ခုနှစ်'
            if (ContainsMm(text, mmMask, "ခုနှစ်"))
            {
                foreach (Match m in MisusedKhuNhitNumberPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ခုနစ်",
                        ProofingIssueType.NumeralClassifierError,
                        "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ သင်္ချာဂဏန်း (၇) ကို 'ခုနစ်' (ဟထိုးမပါ) ဟု ရေးသားရမည်။ ('ခုနှစ်' သည် သက္ကရာဇ်/နှစ်ကာလအတွက်သာ ဖြစ်သည်)။",
                        "သင်္ချာဂဏန်း (၇) (ဟထိုးမပါ)", "NUM", true,
                        "နောက်တွင် မျိုးပြပစ္စည်း လိုက်နေသဖြင့် သင်္ချာကိန်း (၇) အနက်ဖြစ်သည် — ဂဏန်း (၇) ကို 'ခုနစ်' (ဟထိုးမပါ) နှင့် နှစ်သက္ကရာဇ်ပြ နာမ်ကို 'ခုနှစ်' (ဟထိုးပါ) ဟု ခွဲခြားရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ");
                }
            }

            if (ContainsMm(text, mmMask, "ခုနစ်"))
            {
                foreach (Match m in MisusedKhuNitYearPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ခုနှစ်",
                        ProofingIssueType.NumeralClassifierError,
                        "သက္ကရာဇ်/နှစ်ကာလကို ဖော်ပြရာတွင် 'ခုနှစ်' (ဟထိုးပါ) ဟု ရေးသားရမည်။",
                        "သက္ကရာဇ်/နှစ်ကာလ (ဟထိုးပါ)", "N", true,
                        "ရှေ့တွင် သက္ကရာဇ်ဂဏန်း ပါရှိနေသဖြင့် အချိန်ကာလပြ 'နှစ်' အနက်ဖြစ်ရာ ဟထိုးပါသော 'ခုနှစ်' ကို သုံးရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ");
                }
            }

            // 3. Generalized 'မ...ပဲ' -> 'မ...ဘဲ' (including bipartite N-မ-V-ပဲ such as 'အမြတ်မထုတ်ပဲ' -> 'အမြတ်မထုတ်ဘဲ')
            if (hasMa && ContainsMm(text, mmMask, "ပဲ"))
            {
                foreach (Match m in NegativeVerbPePattern.Matches(text))
                {
                    Group maGroup = m.Groups["Ma"];
                    if (maGroup.Index > 0)
                    {
                        char prevCh = text[maGroup.Index - 1];
                        if (prevCh >= '\u1000' && prevCh <= '\u103F')
                        {
                            string verbBody = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["VerbBody"].Value);
                            if (verbBody.StartsWith("ကြီး", StringComparison.Ordinal) ||
                                verbBody.StartsWith("ငယ်", StringComparison.Ordinal) ||
                                verbBody.StartsWith("လေး", StringComparison.Ordinal) ||
                                verbBody.StartsWith("ကလေး", StringComparison.Ordinal) ||
                                verbBody.StartsWith("တို့", StringComparison.Ordinal) ||
                                verbBody.StartsWith("များ", StringComparison.Ordinal) ||
                                verbBody.StartsWith("တွေ", StringComparison.Ordinal) ||
                                verbBody.StartsWith("သာ", StringComparison.Ordinal) ||
                                verbBody.StartsWith("လည်း", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (trie != null)
                            {
                                string verbCore = verbBody;
                                string[] auxSuffixes = { "ပါ", "သေး", "တော့", "နိုင်", "ချင်", "တတ်", "ရ", "ကြ" };
                                for (int aIdx = 0; aIdx < auxSuffixes.Length; aIdx++)
                                {
                                    if (verbCore.Length > auxSuffixes[aIdx].Length &&
                                        verbCore.EndsWith(auxSuffixes[aIdx], StringComparison.Ordinal))
                                    {
                                        verbCore = verbCore.Substring(0, verbCore.Length - auxSuffixes[aIdx].Length);
                                        break;
                                    }
                                }
                                if (!trie.HasPosTag(verbCore, "V") && !trie.HasPosTag(verbCore, "ADJ"))
                                {
                                    continue;
                                }
                            }
                        }
                    }

                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဘဲ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "အငြင်းကြိယာ ('မ...') နောက်တွင် 'ပဲ' အစား 'ဘဲ' (ဘကုန်း) ဖြင့် ရေးသားရမည်။",
                        "အငြင်းကြိယာနောက်ဆက် ('မ...ဘဲ')", "PART", true,
                        "ရှေ့၌ အငြင်းပြပစ္စည်း 'မ' နှင့် ကြိယာတည်ရှိနေသဖြင့် 'မပြုလုပ်ဘဲ' ဟူသော ကြိယာဝိသေသနပြုပစ္စည်း 'ဘဲ' (ဘကုန်း) ဖြစ်ရမည် ('ပဲ' သည် နာမ်/ဝါကျနောက်လိုက် အနက်လေးနက်စေသောပစ္စည်း 'ဒါပဲ/သူပဲ' ၌သာ သုံးသည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိသေသနပြုပစ္စည်း 'ဘဲ')");
                }
            }

            // 4a. Clause Interrogative 'လည်း' -> 'လဲ'
            if (ContainsMm(text, mmMask, "လည်း") &&
                (((mmMask & (1UL << ('ဘ' - '\u1000'))) != 0UL) || ContainsMm(text, mmMask, "မည်")))
            {
                foreach (Match m in InterrogativeLaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လဲ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "အမေးဝါကျနောက်ဆက်ပစ္စည်းတွင် 'လည်း' အစား 'လဲ' ကို သုံးနှုန်းရမည်။",
                        "အမေးပစ္စည်း (သတ်ပုံကျမ်း)", "PART", true,
                        "ဝါကျအတွင်း အမေးနာမ်စား ('ဘာ/ဘယ်/မည်') ပါရှိနေသဖြင့် ဝါကျအဆုံး အမေးပစ္စည်းအဖြစ် 'လဲ' ကို သုံးရမည် ('လည်း' သည် အတူပြ သမ္ဗန္ဓပစ္စည်း ဖြစ်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အမေးပစ္စည်း 'လဲ' နှင့် သမ္ဗန္ဓ 'လည်း')");
                }
            }

            // 4b. Affirmative 'လဲ' -> 'လည်း'
            bool hasLae = ContainsMm(text, mmMask, "လဲ");
            if (hasLae)
            {
                foreach (Match m in AffirmativeLaeMisusePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လည်း",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "'လည်းကောင်း / အတူတူပင်' အနက်ပြ သမ္ဗန္ဓပစ္စည်းတွင် 'လဲ' အစား 'လည်း' (ညကြီးသတ် ဝစ္စပေါက်) ဖြင့် ရေးသားရမည်။",
                        "အတူပြပစ္စည်း ('...လည်း...')", "PART", true,
                        "အမေးဝါကျမဟုတ်ဘဲ နာမ်/နာမ်စားနောက်၌ 'အတူတူပင်/ပါဝင်ခြင်း' အနက်ဖြင့် ဆက်စပ်ပေးသော ပုဒ်ဖြစ်၍ သမ္ဗန္ဓပစ္စည်း 'လည်း' ကို သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သမ္ဗန္ဓပစ္စည်း 'လည်း')");
                }
            }

            // 4c. Universal Orthography & Casual Keyboard Mistakes (apply in BOTH modes)
            if (hasMa && hasCreakyDot && ContainsMm(text, mmMask, "မည့်သည်"))
            {
                foreach (Match m in MisplacedAukmyitMyiThiPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "မည်သည့်",
                        ProofingIssueType.SpellingError,
                        "အောက်မြစ် ('့') နေရာမှားယွင်းနေပါသည် — နာမ်အထူးပြု အမေး/အညွှန်းနာမ်စားကို 'မည်သည့်' ('သည့်' ၌ အောက်မြစ်ပါ) ဟု ရေးသားရမည်။",
                        "နာမ်အထူးပြု အမေး/အညွှန်းနာမ်စား ('မည်သည့်')", "PRON", true,
                        "နာမ်ရှေ့၌ လိုက်သော အထူးပြုနာမ်စား 'မည်သည့်' (ဥပမာ - မည်သည့်အချိန်၊ မည်သည့်ပစ္စည်းနှင့်မဆို) တွင် အောက်မြစ် ('့') ကို ဒုတိယဝဏ္ဏ 'သည့်' အောက်၌သာ ထားရမည် ('မည့်သည်' ဟု မရေးရပါ)။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ");
                }
            }

            if (ContainsMm(text, mmMask, "ပေမယ့်"))
            {
                foreach (Match m in MisspelledDarPayMaePattern.Matches(text))
                {
                    string correct = m.Groups["Prefix"].Value + "မဲ့";
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ '{m.Value}' အစား '{correct}' (နောက်ပစ်၊ အောက်မြစ်) ဟု ရေးသားရမည်။",
                        "သတ်ပုံကျမ်း စာလုံးပေါင်းအမှန်", "CONJ", true);
                }
            }

            if (ContainsMm(text, mmMask, "အုန်း") || ContainsMm(text, mmMask, "အုံး"))
            {
                foreach (Match m in MisspelledVerbOhnPattern.Matches(text))
                {
                    if (IsShieldedOfficialWordSpan(text, m.Index, m.Length, trie)) continue;
                    string wrongPart = m.Groups["Target"].Value;
                    string correct = m.Groups["Prefix"].Value + "ဦး";
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"ကြိယာနောက်ဆက်ပစ္စည်းအဖြစ် '{wrongPart}' အစား 'ဦး' ('{correct}') ဟု ရေးသားရမည်။",
                        "ကြိယာနောက်ဆက်ပစ္စည်း (သတ်ပုံကျမ်း)", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "ဟု"))
            {
                foreach (Match m in MisspelledHuInsteadOfHotePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string sfx = m.Groups["Suffix"].Value;
                    string normalizedSfx = sfx == "ကဲ" ? "ကဲ့"
                        : (sfx == "ပီ" ? "ပြီ"
                        : (sfx == "ပါပီ" ? "ပါပြီ" : sfx));
                    string generalFix = "ဟုတ်" + normalizedSfx;
                    string primaryFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (normalizedSfx == "လား" ? "ဟုတ်ပါသလား"
                            : (normalizedSfx == "တယ်" || normalizedSfx == "ပါတယ်" ? "ဟုတ်ပါသည်" : generalFix))
                        : generalFix;

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, primaryFix,
                        ProofingIssueType.SpellingError,
                        $"တဝမ်းပူသတ် ('တ်') ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{primaryFix}' ဟု ရေးသားရမည် ('ဟု' သည် စာပေအဆိုပြပစ္စည်းသာ ဖြစ်၍ ကြိယာနောက်ဆက် '{normalizedSfx}' နှင့် မတွဲနိုင်ပါ)။",
                        "ဟုတ်မှန်ကြောင်းပြ/အမေး ကြိယာပုဒ် ('ဟုတ်~')", "V", true,
                        $"မြန်မာသဒ္ဒါအရ 'ဟု' သည် အဆိုပြပုဒ်ဆက်ပစ္စည်း (quotative particle) သာဖြစ်၍ အမေး/ဝါကျအဆုံးသတ်ပစ္စည်း '{normalizedSfx}' ရှေ့၌ မလိုက်နိုင်ပါ — 'မှန်ကန်သည်' အနက်ဟော 'ဟုတ်' ကြိယာတွင် တဝမ်းပူသတ် ('တ်') ထည့်၍ '{primaryFix}' ဟု ရေးရမည်။",
                        "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ");
                }

                if (hasMa && ContainsMm(text, mmMask, "မဟု"))
                {
                    foreach (Match m in MisspelledNegatedMaHuPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        string conj = m.Groups["Conj"].Success ? m.Groups["Conj"].Value : string.Empty;
                        string rawSfx = m.Groups["Suffix1"].Success ? m.Groups["Suffix1"].Value
                            : (m.Groups["Suffix2"].Success ? m.Groups["Suffix2"].Value : string.Empty);

                        string normSfx = rawSfx;
                        if (normSfx == "ဖူး") normSfx = "ဘူး";
                        else if (normSfx == "ပါဖူး") normSfx = "ပါဘူး";
                        else if (normSfx == "ပဲ") normSfx = "ဘဲ";
                        else if (normSfx == "လာ") normSfx = "လား";

                        if (styleMode == ProofingStyleMode.FormalOfficial && (normSfx == "ဘူး" || normSfx == "ပါဘူး"))
                        {
                            normSfx = "ပါ";
                        }

                        string correct = conj + "မဟုတ်" + normSfx;
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, correct,
                            ProofingIssueType.SpellingError,
                            $"တဝမ်းပူသတ် ('တ်') ကျန်ခဲ့ပါသည် — '{g.Value}' အစား '{correct}' ဟု ရေးသားရမည်။",
                            "အငြင်းကြိယာ / သမ္ဗန္ဓ ('မဟုတ်~')", "V", true,
                            $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းတွင် 'မဟု' ဟူသော ပုဒ်မရှိပါ — အငြင်းကြိယာ 'မဟုတ်' ၌ တဝမ်းပူသတ် ('တ်') ဖြင့် '{correct}' ဟု အတိအကျ ရေးရမည်။",
                            "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                    }
                }
            }

            bool hasPiOpen = ContainsMm(text, mmMask, "ပီ");
            bool hasPyeeOpen = ContainsMm(text, mmMask, "ပြီ");
            bool hasMaLar = (hasPiOpen || hasPyeeOpen) && ContainsMm(text, mmMask, "မလား");

            if (hasMaLar)
            {
                foreach (Match m in CompletiveMaLarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.SpellingError,
                        Message = $"'{g.Value}' သည် သဒ္ဒါနှင့် စာလုံးပေါင်း မှားယွင်းနေပါသည် — ပြီးစီးမှုအမေးအတွက် 'ပြီးပြီလား' (သို့မဟုတ် အတည်ပြုအမေး 'ပြီးပြီ မဟုတ်လား') ဟု ရေးသားရမည်။",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = "ပြီးပြီလား",
                                MeaningCue = "ပြီးစီးမှု အမေးပုဒ်တွဲ (ဥပမာ - ထမင်းစားပြီးပြီလား)",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("ပြီးပြီလား"),
                                DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(g.Value, "ပြီးပြီလား"),
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = "ပြီးပြီ မဟုတ်လား",
                                MeaningCue = "အတည်ပြု အမေးပုဒ်တွဲ",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("ပြီးပြီမဟုတ်လား"),
                                DiagnosticDetail = "အပြောစကားအတိုကောက် 'မလား' အစား 'မဟုတ်လား'",
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            if (hasPiOpen || (hasPyeeOpen && ContainsMm(text, mmMask, "ပြီးပြီး")))
            {
                foreach (Match m in DoubledOrMedialDroppedPeePyeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (hasMaLar && CompletiveMaLarPattern.IsMatch(text.Substring(g.Index)))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီးပြီ",
                        ProofingIssueType.SpellingError,
                        $"ပြီးစီးကြောင်းပြ ကြိယာနောက်ဆက်တွင် '{g.Value}' အစား 'ပြီးပြီ' (ရှေ့လုံး ဝစ္စပေါက်ပါ၊ နောက်လုံး ဝစ္စပေါက်မပါ၊ ရရစ်ဖြင့်) ဟု ရေးသားရမည်။",
                        "ပြီးစီးကြောင်းပြ ပုဒ်တွဲ ('~ပြီးပြီ')", "PART", true);
                }
            }

            if (hasPiOpen)
            {
                if (ContainsMm(text, mmMask, "ပါပီ"))
                {
                    foreach (Match m in CasualPaPeePattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "ပါပြီ",
                            ProofingIssueType.SpellingError,
                            "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — 'ပါပီ' အစား 'ပါပြီ' ဟု ရေးသားရမည်။",
                            "ယဉ်ကျေးမှုနှင့် ပြီးစီးခြင်းပြပစ္စည်း", "PART", true);
                    }
                }

                foreach (Match m in CasualClauseFinalPiPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (IsShieldedLexicalPiWord(text, g.Index, g.Length, trie)) continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီ",
                        ProofingIssueType.SpellingError,
                        "ကြိယာနောက်ဆက်တွင် ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — 'ပီ' အစား 'ပြီ' ဟု ရေးသားရမည် ('ပီ' သည် ပီပြင်/ပီသ အနက်၌သာ သုံးသည်)။",
                        "ပြီးစီးခြင်းပြ ကြိယာဝိဘတ် ('~ပြီ')", "PART", true);
                }
            }

            if (hasPyeeOpen)
            {
                foreach (Match m in MisusedSequentialPyeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီး",
                        ProofingIssueType.SpellingError,
                        "ရှေ့ကြိယာ ပြီးဆုံး၍ နောက်ဆက်တွဲဖော်ပြရာတွင် ဝစ္စပေါက်ပါသော 'ပြီး' ('~ပြီးတော့ / ~ပြီးလျှင်') ကို သုံးရမည် ('ပြီ' သည် ဝါကျအဆုံးသတ် ဝိဘတ်ဖြစ်သည်)။",
                        "ရှေ့နောက်စဉ်ပြပစ္စည်း ('~ပြီး')", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "ပြီး") && text.IndexOf('။') >= 0)
            {
                foreach (Match m in MisusedSentenceFinalConverbPyeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီ",
                        ProofingIssueType.SpellingError,
                        "ဝါကျအဆုံး ပြီးစီးခြင်းပြ ကြိယာဝိဘတ်တွင် ဝစ္စပေါက်မပါဘဲ 'ပြီ' ဟု ရေးသားရမည် ('ပြီး' သည် ရှေ့နောက်စဉ်ပြပစ္စည်း ဖြစ်သည်)။",
                        "ပြီးစီးခြင်းပြ ကြိယာဝိဘတ် ('~ပြီ')", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "လာ"))
            {
                foreach (Match m in MissingVisargaQuestionLaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လား",
                        ProofingIssueType.SpellingError,
                        "အမေးဝါကျနောက်ဆက်ပစ္စည်းတွင် ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — 'လာ' အစား 'လား' ဟု ရေးသားရမည်။",
                        "အမေးပစ္စည်း ('~လား')", "PART", true);
                }
            }

            bool hasPhu = ContainsMm(text, mmMask, "ဖူး");
            if (hasMa && hasPhu)
            {
                if (styleMode == ProofingStyleMode.FormalOfficial && ContainsMm(text, mmMask, "အားနေ"))
                {
                    foreach (Match m in RedundantMaAhNayPhuPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = g.Value,
                            IssueType = ProofingIssueType.SpellingError,
                            Message = "'မအားနေဖူး' တွင် အငြင်းပိတ်ပစ္စည်း 'ဖူး' စာလုံးပေါင်းမှားနေပြီး ရုံးသုံး/စာပေစနစ်၌ 'မအားလပ်ပါ' သို့မဟုတ် 'မအားပါ' (အပြောစကား၌ 'မအားနေဘူး / မအားဘူး') ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = "အငြင်းပြ 'မ-' နောက်တွင် အတွေ့အကြုံပြ 'ဖူး' မသုံးရဘဲ ဘကုန်းဖြင့် 'ဘူး' (သို့မဟုတ် ရုံးသုံး/စာပေဟန် 'မအားလပ်ပါ / မအားပါ') သုံးနှုန်းရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = "မအားလပ်ပါ",
                                    MeaningCue = "ရုံးသုံး/စာပေ အငြင်းအရေးအသား",
                                    PosTag = "V",
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown("မအားလပ်ပါ"),
                                    DiagnosticDetail = "ရုံးသုံး/စာပေ စံအရေးအသား ('မအားလပ်ပါ')",
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = "မအားပါ",
                                    MeaningCue = "စာပေသုံး အငြင်းကြိယာ ('မ...ပါ')",
                                    PosTag = "V",
                                    IsOfficialThatpon = true,
                                    Score = 97.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = "မအားနေဘူး",
                                    MeaningCue = "အပြောစကား စာလုံးပေါင်းအမှန် ('ဖူး' ➔ 'ဘူး')",
                                    PosTag = "V",
                                    IsOfficialThatpon = false,
                                    Score = 93.0
                                }
                            }
                        });
                    }
                }

                foreach (Match m in MisspelledNegativePhuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "ပါ" : "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.SpellingError,
                        "အငြင်းဝါကျ အဆုံးသတ်ပစ္စည်းတွင် 'ဖူး' အစား 'ဘူး' (ဘကုန်း) ဖြင့် ရေးသားရမည် ('ဖူး' သည် အတွေ့အကြုံပြ 'ရောက်ဖူးသည်' ၌သာ သုံးသည်)။",
                        "အငြင်းကြိယာနောက်ဆက် ('မ...ဘူး')", "PART", false);
                }
            }

            if (ContainsMm(text, mmMask, "ရ်"))
            {
                foreach (Match m in CasualTalMalPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    bool isTal = g.Value == "တရ်";
                    string correct = styleMode == ProofingStyleMode.FormalOfficial
                        ? (isTal ? "သည်" : "မည်")
                        : (isTal ? "တယ်" : "မယ်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.SpellingError,
                        $"အင်တာနက်အတိုကောက် '{g.Value}' သည် စံစာလုံးပေါင်းမဟုတ်ပါ — '{correct}' ဟု ရေးသားရမည်။",
                        "ကြိယာဝိဘတ် စာလုံးပေါင်းအမှန်", "PART", styleMode == ProofingStyleMode.FormalOfficial);
                }
            }

            if (hasMa && ContainsMm(text, mmMask, "ဘူ"))
            {
                foreach (Match m in MissingVisargaBuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "ပါ" : "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.SpellingError,
                        "အငြင်းကြိယာနောက်ဆက်တွင် ဝစ္စပေါက် ('း') ကျန်ခဲ့ပါသည် — 'ဘူး' ဟု ရေးသားရမည်။",
                        "အငြင်းကြိယာနောက်ဆက်", "PART", false);
                }
            }

            if (hasPiOpen && ContainsMm(text, mmMask, "ပီး"))
            {
                foreach (Match m in CasualPeePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ပြီး",
                        ProofingIssueType.SpellingError,
                        "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ပြီး' ဟု ရေးသားရမည်။",
                        "ပြီးစီးခြင်းပြပစ္စည်း (သတ်ပုံကျမ်း)", "PART", true);
                }
            }

            if (ContainsMm(text, mmMask, "ဖစ်"))
            {
                foreach (Match m in CasualPhitPattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဖြစ်",
                        ProofingIssueType.SpellingError,
                        "ရရစ် ('ြ') ကျန်ခဲ့ပါသည် — စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'ဖြစ်' ဟု ရေးသားရမည်။",
                        "ဖြစ်ပေါ်သည် (သတ်ပုံကျမ်း)", "V", true);
                }
            }

            ScanUnspacedAttachedParticleTypos(text, mmMask, styleMode, trie, issues);
            ScanStructuralSyllableAndBoundaryRepairs(text, mmMask, styleMode, trie, issues);

            if (ContainsMm(text, mmMask, "\u101C\u103B\u103E\u1000\u103A"))
            {
                foreach (Match m in MisspelledLyetPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "လျက်",
                        ProofingIssueType.SpellingError,
                        "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်းအရ 'လျှက်' (ဟထိုးပါ) ဟူသော စာလုံးပေါင်းမရှိပါ — 'လျက်' (ဟထိုးမပါ) ဖြင့်သာ ရေးသားရမည်။",
                        "ကြိယာနောက်ဆက် / ကြိယာ ('သွား~ လာ~')", "PART", true);
                }
            }

            // 4d. Stray vowel typed after killed coda consonant inside a word (e.g. 'မိန်ိးမ' -> 'မိန်းမ')
            if (hasStrayVowelAfterAsat)
            {
                foreach (Match m in StrayVowelAfterCodaWordPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string fixedWord = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(g.Value));
                    if (fixedWord != g.Value)
                    {
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, fixedWord,
                            ProofingIssueType.LookalikeCharacterError,
                            $"အသတ်နောက်တွင် သရအစီအစဉ် မှားယွင်းနေပါသည် — '{g.Value}' အစား '{fixedWord}' ဟု ရေးသားရမည်။",
                            "ယူနီကုဒ် အစီအစဉ်အမှန်", "WORD", true);
                    }
                }
            }

            if (hasAsatVirama)
            {
                foreach (Match m in AccidentalViramaAfterCodaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string fixedCoda = g.Value.Substring(0, g.Value.Length - 1);
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, fixedCoda,
                        ProofingIssueType.LookalikeCharacterError,
                        $"အသတ်နောက်တွင် ပါဌ်ဆင့်သင်္ကေတ ('္') ပိုရိုက်မိနေပါသည် — '{g.Value}' အစား '{fixedCoda}' ဟု ရေးသားရမည်။",
                        "ယူနီကုဒ် အသတ်အမှန်", "WORD", true);
                }
            }

            // 4e. Doubled sentence-ending particles ('တင်ပြပါသည်သည်။' -> 'တင်ပြပါသည်။') & Past+Future Marker Clash ('သွားခဲ့မယ်')
            if (ContainsMm(text, mmMask, "သည်သည်") ||
                ContainsMm(text, mmMask, "တယ်တယ်") ||
                ContainsMm(text, mmMask, "မည်မည်") ||
                ContainsMm(text, mmMask, "မယ်မယ်") ||
                ContainsMm(text, mmMask, "ပါပါ"))
            {
                foreach (Match m in DoubledSentenceFinalParticlePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = v.StartsWith("ပါပါ", StringComparison.Ordinal)
                        ? v.Substring(2)
                        : v.Substring(0, v.Length / 2);
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျအဆုံးသတ် ကြိယာဝိဘတ် နှစ်ထပ်ဖြစ်နေပါသည် — '{v}' အစား '{correct}' ဟု ရေးသားရမည်။",
                        "ကြိယာဝိဘတ် အမှန်", "PART", true);
                }
            }

            bool hasKhae = ContainsMm(text, mmMask, "ခဲ့");
            bool hasMyiOrMal = ContainsMm(text, mmMask, "မည်") ||
                               ContainsMm(text, mmMask, "မယ်");
            if (hasKhae)
            {
                if (hasMyiOrMal)
                {
                    foreach (Match m in ConflictingPastFutureMarkerPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        string v = g.Value;
                        bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal);
                        bool hasPa = v.Contains("ပါ");
                        string futForm = hasPa ? (isFormal ? "ပါမည်" : "ပါမယ်") : (isFormal ? "မည်" : "မယ်");
                        string pastForm = hasPa ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်") : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = v,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"အတိတ်ပြပစ္စည်း ('ခဲ့') နှင့် အနာဂတ်ပြဝိဘတ် ('{futForm}') ဆန့်ကျင်ဘက် တွဲဖက်နေပါသည် — '{futForm}' သို့မဟုတ် '{pastForm}' ကိုသာ ရွေးချယ်သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ကြိယာတစ်ခုတည်းနောက်တွင် ပြီးစီးခဲ့ပြီးသော အတိတ်ကာလပြ ကြိယာထောက်ပစ္စည်း ('ခဲ့') နှင့် မဖြစ်သေးသော အနာဂတ်ကာလပြ ကြိယာဝိဘတ် ('{futForm}') တို့သည် ကာလသဘော ဆန့်ကျင်နေသဖြင့် တစ်ပြိုင်နက် မတွဲဖက်နိုင်ပါ။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'ခဲ့' နှင့် ကာလပြကြိယာဝိဘတ်)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = futForm,
                                    MeaningCue = "အနာဂတ်ပြ ကြိယာဝိဘတ်",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = pastForm,
                                    MeaningCue = "အတိတ်ပြ ကြိယာဝိဘတ်",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 96.0
                                }
                            }
                        });
                    }
                }

                // 4f. Temporal Adverb <-> Sentence-Final Tense Agreement
                if (ContainsMm(text, mmMask, "ဖြန်") ||
                    ContainsMm(text, mmMask, "မကြာ") ||
                    ContainsMm(text, mmMask, "လာမ") ||
                    ContainsMm(text, mmMask, "နောက်"))
                {
                    foreach (Match m in FutureAdverbWithPastVerbPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        string preTarget = text.Substring(m.Index, g.Index - m.Index);
                        if (FutureExemptionSubclauseRegex.IsMatch(preTarget))
                            continue;
                        string v = g.Value;
                        bool passivePatientWithoutExplicitAgent =
                            preTarget.IndexOf("ကို ", StringComparison.Ordinal) >= 0 &&
                            preTarget.IndexOf("က ", StringComparison.Ordinal) < 0;

                        string primary;
                        string secondary;
                        if (v == "ခဲ့ကြပါသည်") { primary = "ကြပါမည်"; secondary = "ကြမည်ဖြစ်သည်"; }
                        else if (v == "ခဲ့ကြသည်") { primary = "ကြမည်ဖြစ်သည်"; secondary = "ကြမည်"; }
                        else if (v == "ခဲ့ကြတယ်") { primary = "ကြမယ်"; secondary = "ကြပါမယ်"; }
                        else if (v == "ခဲ့ပါသည်") { primary = "ပါမည်"; secondary = "မည်ဖြစ်သည်"; }
                        else if (v == "ခဲ့ပါတယ်") { primary = "ပါမယ်"; secondary = "မယ်"; }
                        else if (v == "ခဲ့သည်")
                        {
                            primary = passivePatientWithoutExplicitAgent ? "မည်ဖြစ်သည်" : "မည်";
                            secondary = passivePatientWithoutExplicitAgent ? "မည်" : "မည်ဖြစ်သည်";
                        }
                        else
                        {
                            primary = "မယ်";
                            secondary = "ပါမယ်";
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = v,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ရှေ့တွင် အနာဂတ်အချိန်ပြပုဒ် ပါရှိသဖြင့် အတိတ်ပြဝိဘတ် '{v}' အစား အနာဂတ်ပြဝိဘတ် '{primary}' ကို သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ဝါကျအစ၌ အနာဂတ်ကာလပြ ကြိယာဝိသေသန ('မနက်ဖြန်/နောက်လ/လာမည့်...') တည်ရှိပြီး ကြားတွင် ဝါကျခွဲ ('လျှင်/သော်လည်း') မခံသဖြင့် ပင်မကြိယာကို အတိတ်ပြပစ္စည်း '{v}' ဖြင့် မပိတ်ရဘဲ အနာဂတ်ပြ '{primary}' ဖြင့် ညှိနှိုင်းရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိသေသန နှင့် ကြိယာဝိဘတ် လိုက်လျောညီထွေမှု)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = primary,
                                    MeaningCue = "ကာလကိုက်ညီသော အနာဂတ်ပြဝိဘတ်",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = secondary,
                                    MeaningCue = "အနာဂတ်ပြ ကြိယာဝိဘတ်ပုံစံကွဲ",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    Score = 95.0
                                }
                            }
                        });
                    }
                }

                if (ContainsMm(text, mmMask, "ခဲ့နေပြီ"))
                {
                    foreach (Match m in ImpossibleAspectStackPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        string primary = styleMode == ProofingStyleMode.FormalOfficial ? "နေသည်" : "နေတယ်";
                        string secondary = styleMode == ProofingStyleMode.FormalOfficial ? "နေပါသည်" : "နေပါတယ်";
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = g.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ကြိယာထောက်ပစ္စည်း အစီအစဉ်နှင့် ကာလဝိဘတ် မှားယွင်းနေပါသည် — '{g.Value}' အစား '{primary}' ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = "အတိတ်ပြ 'ခဲ့'၊ ဖြစ်ဆဲပြ 'နေ' နှင့် အခြေအနေသစ်ရောက်ပြ 'ပြီ' တို့ကို '*ခဲ့နေပြီ' ဟု တစ်ပြိုင်နက် စီစဉ်တွဲစပ်၍ မရပါ။ ဖြစ်ဆဲအနက်အတွက် 'နေတယ်/နေသည်' ကို သုံးရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်းနှင့် ကာလပြဝိဘတ်)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate { Replacement = primary, MeaningCue = "ဖြစ်ပေါ်ဆဲပြ ကြိယာဝိဘတ်", PosTag = "PART", IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial, Score = 100.0 },
                                new SuggestionCandidate { Replacement = secondary, MeaningCue = "ယဉ်ကျေးမှုပါ ဖြစ်ပေါ်ဆဲပြဝိဘတ်", PosTag = "PART", IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial, Score = 95.0 }
                            }
                        });
                    }
                }

                if (ContainsMm(text, mmMask, "ဆိုတာ") && ContainsMm(text, mmMask, "တတ်ခဲ့တယ်"))
                {
                    foreach (Match m in GenericGnomicHabitualPastPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, g.Index, g.Length))
                            continue;
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "တတ်တယ်",
                            ProofingIssueType.OfficialStyleGrammarError,
                            "ယေဘုယျဓမ္မတာပြဝါကျ ('~ဆိုတာ ...') တွင် အတိတ်ပြ 'ခဲ့' မလိုအပ်ပါ — 'တတ်ခဲ့တယ်' အစား 'တတ်တယ်' ဟု ရေးသားရမည်။",
                            "ဓမ္မတာ/အလေ့အထပြ ကြိယာနောက်ဆက် ('~တတ်တယ်')", "PART", false,
                            "အမြဲမှန်ကန်သော သဘာဝဓမ္မတာ သို့မဟုတ် အလေ့အထကို ဖော်ပြသည့် ဝါကျ၌ အတိတ်ကာလပြပစ္စည်း 'ခဲ့' မတွဲရဘဲ 'တတ်တယ်/တတ်သည်' ဟုသာ သုံးနှုန်းရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဓမ္မတာပြ ပစ္စုပ္ပန်ကာလ)");
                    }
                }
            }

            if (hasMyiOrMal && (hasKhae || ContainsMm(text, mmMask, "နေ့") || ContainsMm(text, mmMask, "ယမန်")))
            {
                foreach (Match m in PastAdverbWithFutureVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string preTarget = text.Substring(m.Index, g.Index - m.Index);
                    if (PastExemptionSubclauseRegex.IsMatch(preTarget))
                        continue;
                    string v = g.Value;
                    string correct;
                    if (v == "မည်ဖြစ်ကြောင်း")
                    {
                        correct = "ခဲ့ကြောင်း";
                    }
                    else if (v == "မည်ဖြစ်သည်")
                    {
                        correct = "ခဲ့သည်";
                    }
                    else
                    {
                        bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal);
                        bool hasPa = v.Contains("ပါ");
                        correct = hasPa
                            ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်")
                            : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                    }
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ရှေ့တွင် အတိတ်အချိန်ပြပုဒ် ပါရှိသဖြင့် အနာဂတ်ပြဝိဘတ် '{v}' အစား အတိတ်ပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        "ကာလကိုက်ညီသော အတိတ်ပြဝိဘတ်", "PART", true,
                        $"ဝါကျအစ၌ ပြီးလွန်ပြီးသော အတိတ်အချိန်ပြပုဒ် ('မနေ့က/ယမန်နှစ်က/ပြီးခဲ့သော...') ပါရှိပြီး အနာဂတ်အကူးအပြောင်းပုဒ် ('ယခု/ဆက်လက်') မပါသဖြင့် အနာဂတ်ပြ '{v}' အစား အတိတ်ပြ '{correct}' ဖြင့် ကာလညှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိသေသန နှင့် ကြိယာဝိဘတ် လိုက်လျောညီထွေမှု)");
                }
            }

            if (hasMyiOrMal && (ContainsMm(text, mmMask, "ခေတ်") || ContainsMm(text, mmMask, "လက်ထက်")))
            {
                foreach (Match m in HistoricalEraWithSpeculativeFuturePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string v = g.Value;
                    bool isFormal = v.EndsWith("မည်", StringComparison.Ordinal) || styleMode == ProofingStyleMode.FormalOfficial;
                    bool isCopulaPhit = v.StartsWith("ဖြစ်", StringComparison.Ordinal);
                    bool hasPa = v.Contains("ပါ");
                    string correct;
                    if (isCopulaPhit)
                    {
                        correct = hasPa
                            ? (isFormal ? "ဖြစ်ပါသည်" : "ဖြစ်ပါတယ်")
                            : (isFormal ? "ဖြစ်သည်" : "ဖြစ်တယ်");
                    }
                    else
                    {
                        correct = hasPa
                            ? (isFormal ? "ခဲ့ပါသည်" : "ခဲ့ပါတယ်")
                            : (isFormal ? "ခဲ့သည်" : "ခဲ့တယ်");
                    }
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သမိုင်းဝင်ခေတ်ကာလကို ဖော်ပြရာတွင် ခန့်မှန်းအနာဂတ်ပြ '{v}' အစား '{correct}' ကို သုံးနှုန်းရမည်။",
                        "သမိုင်းကာလနှင့် ကိုက်ညီသော ကြိယာဝိဘတ်", "PART", isFormal,
                        "ပြီးလွန်ခဲ့ပြီးသော သမိုင်းခေတ်/မင်းလက်ထက် အကြောင်းအရာဖြစ်သဖြင့် မသေချာသော အနာဂတ်ပြ 'လိမ့်မည်/လိမ့်မယ်' မသုံးရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကာလပြ ကြိယာဝိဘတ် စည်းမျဉ်း)");
                }
            }

            if (hasKhae &&
                (ContainsMm(text, mmMask, "လက်ရှိတွင်") ||
                 ContainsMm(text, mmMask, "လောလောဆယ်တွင်")))
            {
                foreach (Match m in PresentOngoingWithPastVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string preTarget = text.Substring(m.Index, g.Index - m.Index);
                    if (FutureExemptionSubclauseRegex.IsMatch(preTarget))
                        continue;
                    string v = g.Value;
                    bool isFormal = v.EndsWith("သည်", StringComparison.Ordinal);
                    bool hasPa = v.Contains("ပါ");
                    string correct = hasPa
                        ? (isFormal ? "နေပါသည်" : "နေပါတယ်")
                        : (isFormal ? "နေသည်" : "နေတယ်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ရှေ့တွင် ပစ္စုပ္ပန်ဆဲဆဲပြပုဒ် ('လက်ရှိတွင်') ပါရှိသဖြင့် အတိတ်ပြ '{v}' အစား ဆောင်ရွက်ဆဲပြ '{correct}' ကို သုံးနှုန်းရမည်။",
                        "ဖြစ်ပေါ်ဆဲပြ ကြိယာထောက်ပစ္စည်း ('~နေသည်')", "PART", true,
                        $"'လက်ရှိတွင်' သည် ယခုလက်ရှိ ဆောင်ရွက်ဆဲအခြေအနေကို ညွှန်းသဖြင့် ပြီးဆုံးခဲ့ပြီးသော အတိတ်ပြ '{v}' အစား ဖြစ်ဆဲပြ ကြိယာထောက်ပစ္စည်း '{correct}' (သို့မဟုတ် 'လျက်ရှိသည်') နှင့် တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'နေ' နှင့် အချိန်ကာလ ဆက်စပ်မှု)");
                }
            }

            // 4g. Numeral Classifier Agreement ('ဖိနပ် နှစ်ယောက် -> နှစ်ရံ', 'ကော်မတီဝင် သုံးကောင် -> သုံးဦး', etc.)
            if (HasAnyMyanmarNumeralCore(text, mmMask, hasNonZeroMyanmarDigit))
            {
                for (int rIdx = 0; rIdx < ClassifierAgreementRules.Length; rIdx++)
                {
                    var cRule = ClassifierAgreementRules[rIdx];
                    if (!HasAnySubstring(text, mmMask, cRule.Triggers))
                        continue;

                    foreach (Match m in cRule.Pattern.Matches(text))
                    {
                        Group wrongGrp = m.Groups["Wrong"];
                        int beforeCnt = issues.Count;
                        AddGrammarOrSpellingIssue(
                            issues, wrongGrp.Index, wrongGrp.Length, wrongGrp.Value, cRule.CorrectClassifier,
                            ProofingIssueType.NumeralClassifierError,
                            $"ရှေ့နာမ်ပုဒ်နှင့် လိုက်ဖက်ညီသော မျိုးပြပစ္စည်း မဟုတ်ပါ — '{wrongGrp.Value}' အစား '{cRule.CorrectClassifier}' ({cRule.CategoryLabel}) ကို သုံးနှုန်းရမည်။",
                            cRule.CategoryLabel, "CLF", true,
                            $"မြန်မာသဒ္ဒါ (နာမ်ပစ္စည်း — မျိုးပြပစ္စည်း) အရ သင်္ချာဂဏန်းနောက်ရှိ မျိုးပြပစ္စည်းသည် ရှေ့နာမ်ပုဒ်၏ သက်ရှိ/သက်မဲ့၊ ပုံသဏ္ဌာန်၊ အမျိုးအစားနှင့် ကိုက်ညီရမည်ဖြစ်ရာ '{cRule.CorrectClassifier}' ကို သုံးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ပစ္စည်း ၉ မျိုး — မျိုးပြပစ္စည်း)");
                        if (cRule.CorrectClassifier == "ရံ" && issues.Count > beforeCnt)
                        {
                            issues[issues.Count - 1].Suggestions.Add(new SuggestionCandidate
                            {
                                Replacement = "စုံ",
                                MeaningCue = "အပြော/အရေး အစုံလိုက်ပြ မျိုးပြပစ္စည်း ('~စုံ')",
                                PosTag = "CLF",
                                IsOfficialThatpon = true,
                                Score = 95.0
                            });
                        }
                    }
                }

                if (ContainsMm(text, mmMask, "များ") || ContainsMm(text, mmMask, "တွေ"))
                {
                    foreach (Match m in ClassifierRedundantPluralPattern.Matches(text))
                    {
                        Group tGrp = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, tGrp.Index, tGrp.Length))
                            continue;
                        string clf = m.Groups["Clf"].Value;
                        string red = m.Groups["Redundant"].Value;
                        string collectiveAlt = clf + "တို့";
                        issues.Add(new ProofingIssue
                        {
                            StartIndex = tGrp.Index,
                            Length = tGrp.Length,
                            OriginalText = tGrp.Value,
                            IssueType = ProofingIssueType.NumeralClassifierError,
                            Message = $"တိကျသော သင်္ချာမျိုးပြပစ္စည်း ('{clf}') နောက်တွင် အများပြပစ္စည်း '{red}' ထပ်မထည့်ရပါ — '{clf}' (သို့မဟုတ် အစုအပေါင်းပြ '{collectiveAlt}') ဟု သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"မြန်မာသဒ္ဒါအရ ကိန်းဂဏန်းနှင့် မျိုးပြပစ္စည်း ('{clf}') ဖြင့် အရေအတွက် အတိအကျ ဖော်ပြပြီးဖြစ်သော နာမ်ပုဒ်နောက်၌ မရေရာအများပြပစ္စည်း '{red}' ထပ်မလိုက်ရပါ (အစုအဖွဲ့ကို အတိအကျ ညွှန်းလိုပါက 'တို့' ကိုသာ သုံးနိုင်သည်)။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကိန်းနှင့် အများပြပစ္စည်း စည်းမျဉ်း)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = clf,
                                    MeaningCue = $"တိကျသော မျိုးပြပစ္စည်း သီးသန့် ('~{clf}')",
                                    PosTag = "CLF",
                                    IsOfficialThatpon = true,
                                    Score = 100.0
                                },
                                new SuggestionCandidate
                                {
                                    Replacement = collectiveAlt,
                                    MeaningCue = $"အစုအပေါင်းပြ ('~{collectiveAlt}')",
                                    PosTag = "CLF",
                                    IsOfficialThatpon = true,
                                    Score = 95.0
                                }
                            }
                        });
                    }
                }
            }

            if (hasTa && ContainsMm(text, mmMask, "တစ်ကောင်"))
            {
                foreach (Match m in ReciprocalHumanClassifierPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = g.Value.Replace("ကောင်", "ယောက်");
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.NumeralClassifierError,
                        $"လူပုဂ္ဂိုလ်ချင်း အပြန်အလှန်ပြုရာတွင် တိရစ္ဆာန်ပြ မျိုးပြပစ္စည်း 'ကောင်' အစား လူပုဂ္ဂိုလ်ပြ '{correct}' ဟု သုံးနှုန်းရမည်။",
                        "လူပုဂ္ဂိုလ် အပြန်အလှန်ပြပုဒ်", "PRON", true,
                        "ရှေ့တွင် လူပုဂ္ဂိုလ်ပြနာမ် ('မိတ်ဆွေ/သူငယ်ချင်း/လူသား...') ပါရှိသဖြင့် တိရစ္ဆာန်ပြ မျိုးပြပစ္စည်း 'ကောင်' မသုံးရဘဲ လူပုဂ္ဂိုလ်ပြ 'ယောက်/ဦး' ဖြင့် ဖွဲ့စည်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (မျိုးပြပစ္စည်း နှင့် ယဉ်ကျေးမှုအသုံးအနှုန်း)");
                }
            }

            // 4i. Audited Burmese Grammar Rules: Negation Framing, Experiential 'ဖူး', Nominalizer 'သောကြောင့်', Particle Chains & Postposition Frames
            bool hasBu = hasVisarga && ContainsMm(text, mmMask, "ဘူး");
            if (hasBu)
            {
                foreach (Match m in ExperientialBuInsteadOfPhuPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ဖူး",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အတွေ့အကြုံပြ ကြိယာထောက်ပစ္စည်းတွင် 'ဘူး' အစား 'ဖူး' ('{verb}ဖူး...') ဟု ရေးသားရမည်။",
                        "အတွေ့အကြုံပြ ကြိယာထောက်ပစ္စည်း ('~ဖူးသည်')", "PART", true,
                        $"ရှေ့၌ အငြင်းပြပစ္စည်း 'မ' မပါဘဲ နောက်၌ ဟုတ်မှန်/အမေး ကြိယာဝိဘတ် ဆက်လိုက်နေသဖြင့် အတိတ်အတွေ့အကြုံပြ ကြိယာထောက်ပစ္စည်း 'ဖူး' (ဖဦးထုပ်) ဖြစ်ရမည် ('ဘူး' သည် 'မ...ဘူး' အငြင်းပိတ်ပစ္စည်းသာ ဖြစ်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း ခွဲထား (စာ-၄၀၈)");
                }
            }

            if (hasMa && (ContainsMm(text, mmMask, "တယ်") || ContainsMm(text, mmMask, "မယ်")))
            {
                foreach (Match m in NegatedVerbWithAffirmativeSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string correct = g.Value.StartsWith("ပါ", StringComparison.Ordinal) ? "ပါဘူး" : "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အငြင်းကြိယာ ('မ{verb}...') ကို ဟုတ်မှန်ပြဝိဘတ် '{g.Value}' ဖြင့် မပိတ်ရပါ — အငြင်းပိတ်ပစ္စည်း '{correct}' ကို သုံးရမည်။",
                        "အငြင်းဝါကျ အဆုံးသတ်ပစ္စည်း ('မ...ဘူး')", "PART", false,
                        $"မြန်မာသဒ္ဒါအရ 'မ' ဖြင့် ငြင်းပယ်ထားသော အပြောစကား ပင်မကြိယာကို ဟုတ်မှန်ကြောင်းပြဝိဘတ် ('တယ်/မယ်') ဖြင့် အဆုံးသတ်၍ မရပါ — 'ဘူး/ပါဘူး' (သို့မဟုတ် အရေးစကား 'ပါ') ဖြင့်သာ ပိတ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း 'မ...ဘူး / မ...ပါ')");
                }
            }

            if (hasBu)
            {
                foreach (Match m in UnpairedNegativeBuWithoutMaPattern.Matches(text))
                {
                    if (BareNegationPrefixCheckRegex.IsMatch(m.Groups["Clause"].Value))
                        continue;
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string posFix = verb + "တယ်";
                    string negFix = verb == "အရသာရှိ" ? "အရသာမရှိဘူး" : ("မ" + verb + "ဘူး");
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ရှေ့တွင် အငြင်းပြပစ္စည်း 'မ' မပါဘဲ အငြင်းပိတ်ပစ္စည်း 'ဘူး' သုံးထားပါသည် — အပြုသဘောဆိုလျှင် '{posFix}'၊ အငြင်းသဘောဆိုလျှင် '{negFix}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = "'ဘူး' သည် အငြင်းပြပစ္စည်း 'မ' နှင့် တွဲဖက်ရသော အငြင်းဝါကျနောက်ဆက် ('မ + ကြိယာ + ဘူး') ဖြစ်သဖြင့် 'မ' မပါဘဲ သီးခြားရပ်တည်၍ မရပါ။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း ဖွဲ့ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = false,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = negFix,
                                MeaningCue = "အငြင်းဝါကျ အပြည့်အစုံ ('မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = false,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            bool hasBareBuShort = ContainsMm(text, mmMask, "ဘူ");
            if (hasBu || hasPhu || hasBareBuShort)
            {
                foreach (Match m in DroppedInfixMaCompoundNegationPattern.Matches(text))
                {
                    string clausePrefix = TrimClauseAfterConjunction(m.Groups["Clause"].Value);
                    if (BareNegationPrefixCheckRegex.IsMatch(clausePrefix))
                        continue;

                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string head = m.Groups["Head"].Value;
                    string root = m.Groups["Root"].Value;
                    string aux = m.Groups["Aux"].Value;
                    string endToken = m.Groups["End"].Value;
                    bool hasSpellingCorruption = endToken == "ဖူး" || endToken == "ဘူ";

                    string formalAux = (aux == "နေ" || aux == "ပါ")
                        ? string.Empty
                        : (aux.EndsWith("ပါ", StringComparison.Ordinal) ? aux.Substring(0, aux.Length - 2) : aux);
                    string colloqClose = aux.EndsWith("ပါ", StringComparison.Ordinal) ? (aux + "ဘူး") : (aux + "ဘူး");
                    string formalNeg = head + "မ" + root + formalAux + "ပါ";
                    string colloqNeg = head + "မ" + root + colloqClose;
                    string primaryNeg = styleMode == ProofingStyleMode.FormalOfficial ? formalNeg : colloqNeg;
                    string secondaryNeg = styleMode == ProofingStyleMode.FormalOfficial ? colloqNeg : formalNeg;
                    string posFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (head + root + "ပါသည်")
                        : (head + root + "တယ်");

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = hasSpellingCorruption ? ProofingIssueType.SpellingError : ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ပေါင်းစပ်ကြိယာ '{head}{root}' ကို ငြင်းပယ်ရာတွင် အလယ်၌ အငြင်းပြ 'မ' ထည့်သွင်းရမည် — '{primaryNeg}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါ ပေါင်းစပ်ကြိယာဖွဲ့ထုံးအရ '{head} + {root}' အငြင်းပုံစံတွင် 'မ-' ကို ကြားညှပ်၍ '{primaryNeg}' ('{head} + မ + {root} + ဘူး/ပါ') ဟု ရေးသားရမည် ('{g.Value}' ဟု 'မ' ချန်လှပ်မရေးရပါ)။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ပေါင်းစပ်ကြိယာ အငြင်းဖွဲ့ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryNeg,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "ရုံးသုံး/စာပေ အငြင်းအရေးအသား"
                                    : "အငြင်းဝါကျ စံအရေးအသား ('~မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primaryNeg),
                                DiagnosticDetail = "အငြင်းပြ 'မ-' ဖြည့်စွက်ချက်",
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = secondaryNeg,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "အပြောစကား အငြင်းအမှန်"
                                    : "ရုံးသုံး/စာပေ အငြင်းအရေးအသား",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode != ProofingStyleMode.FormalOfficial,
                                Score = 95.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 90.0
                            }
                        }
                    });
                }

                foreach (Match m in DroppedMaColloquialNegationPattern.Matches(text))
                {
                    string clausePrefix = TrimClauseAfterConjunction(m.Groups["Clause"].Value);
                    if (BareNegationPrefixCheckRegex.IsMatch(clausePrefix))
                        continue;

                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    if (trie != null && trie.ContainsExactWord(g.Value))
                        continue;

                    string verb = m.Groups["Verb"].Value;
                    string aux = m.Groups["Aux"].Value;
                    string endToken = m.Groups["End"].Value;
                    bool hasSpellingCorruption = endToken == "ဖူး" || endToken == "ဘူ";

                    string formalAux = (aux == "နေ" || aux == "ပါ")
                        ? string.Empty
                        : (aux.EndsWith("ပါ", StringComparison.Ordinal) ? aux.Substring(0, aux.Length - 2) : aux);
                    string formalStem = (verb == "အား" && aux == "နေ") ? "အားလပ်" : verb;
                    string formalNeg = "မ" + formalStem + formalAux + "ပါ";
                    string colloqNeg = "မ" + verb + aux + "ဘူး";

                    string negFix = styleMode == ProofingStyleMode.FormalOfficial ? formalNeg : colloqNeg;
                    string altNegFix = styleMode == ProofingStyleMode.FormalOfficial ? colloqNeg : formalNeg;
                    string posFix = styleMode == ProofingStyleMode.FormalOfficial
                        ? (verb + "ပါသည်")
                        : (verb + "တယ်");

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = hasSpellingCorruption ? ProofingIssueType.SpellingError : ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အငြင်းပြရှေ့ဆက် 'မ' ကျန်ခဲ့ပါသည် — စံမြန်မာစာဖြင့် '{negFix}' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"အပြောစကား၌ ရှေ့ဆက် 'မ-' ကို ချန်လှပ်၍ '{g.Value}' ဟု ပြောလေ့ရှိသော်လည်း စာအရေးအသားတွင် 'မ + ကြိယာ + ဘူး/ပါ' ဖွဲ့ထုံးအရ '{negFix}' ဟု အပြည့်အစုံ ရေးသားရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အငြင်းပြပစ္စည်း 'မ...ဘူး / မ...ပါ')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = negFix,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "ရုံးသုံး/စာပေ အငြင်းအရေးအသား ('မ...ပါ')"
                                    : "အငြင်းဝါကျ စံအရေးအသား ('မ...ဘူး')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(negFix),
                                DiagnosticDetail = "အငြင်းပြရှေ့ဆက် 'မ-' ဖြည့်စွက်ချက်",
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = altNegFix,
                                MeaningCue = styleMode == ProofingStyleMode.FormalOfficial
                                    ? "အပြောစကား အငြင်းအမှန် ('မ...ဘူး')"
                                    : "ရုံးသုံး/စာပေ အငြင်းအရေးအသား ('မ...ပါ')",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode != ProofingStyleMode.FormalOfficial,
                                Score = 95.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = posFix,
                                MeaningCue = "ဟုတ်မှန်ကြောင်းပြ ဝါကျအဆုံးသတ်",
                                PosTag = "V",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 90.0
                            }
                        }
                    });
                }
            }

            if (hasMa && hasPaukMaGyi)
            {
                foreach (Match m in IncompleteColloquialNegationPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = g.Value + "ဘူး";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အပြောစကား အငြင်းဝါကျအဆုံးတွင် အငြင်းပိတ်ပစ္စည်း 'ဘူး' ကျန်ခဲ့ပါသည် — '{correct}' ဟု ရေးသားရမည်။",
                        "အပြောစကား အငြင်းဝါကျအဆုံးသတ် ('မ...ဘူး')", "V", false,
                        "ဝါကျအတွင်း အပြောစကားအသုံးအနှုန်း ('မှာ/တွေ/နဲ့') ပါရှိသော အငြင်းဝါကျဖြစ်သဖြင့် ကြိယာနောက်၌ 'ဘူး' (သို့မဟုတ် 'ပါဘူး') ထည့်သွင်းမှ ဝါကျပြည့်စုံမည် (စာပေစကားပြေ/ကဗျာ၌သာ 'မ+ကြိယာ။' သုံးလေ့ရှိသည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် Okell / Jenny Burmese Reference Grammar");
                }
            }

            if (ContainsMm(text, mmMask, "ကြောင့်"))
            {
                foreach (Match m in BareVerbBeforeKyauntPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string primaryFix = "သောကြောင့်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ကြိယာပုဒ် '{verb}' နောက်တွင် နာမ်ဝိဘတ် 'ကြောင့်' တိုက်ရိုက်မလိုက်ရပါ — '{verb}သောကြောင့်' သို့မဟုတ် '{verb}သဖြင့်' ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"'{verb}' သည် ကြိယာပုဒ်ဖြစ်ပြီး 'ကြောင့်' သည် နာမ်/နာမ်စားနောက်၌သာ လိုက်ရသော အကြောင်းပြ နာမ်ဝိဘတ်ဖြစ်သဖြင့် နာမဝိသေသနပြုပစ္စည်း 'သော/သည့်' ခံ၍ 'သောကြောင့်' ဟု ဆက်စပ်ရမည် (သို့မဟုတ် ကြိယာဆက်သမ္ဗန္ဓ 'သဖြင့် / လို့' သုံးရမည်)။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် 'ကြောင့်' နှင့် ဝါကျဆက်သမ္ဗန္ဓ)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryFix,
                                MeaningCue = $"အကြောင်းပြသမ္ဗန္ဓ ({verb}~)",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = styleMode == ProofingStyleMode.FormalOfficial ? "သဖြင့်" : "လို့",
                                MeaningCue = "ကြိယာနောက်ဆက် အကြောင်းပြသမ္ဗန္ဓ",
                                PosTag = "CONJ",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            if (ContainsMm(text, mmMask, "အတွက်"))
            {
                foreach (Match m in BareNegatedVerbBeforeAtwetPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string verb = m.Groups["Verb"].Value;
                    string primaryFix = styleMode == ProofingStyleMode.FormalOfficial ? "သည့်အတွက်" : "တဲ့အတွက်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အငြင်းကြိယာ '{verb}' နောက်တွင် နာမ်ဝိဘတ် 'အတွက်' တိုက်ရိုက်မလိုက်ရပါ — '{verb}{primaryFix}' (သို့မဟုတ် '{verb}သောကြောင့်') ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = $"'{verb}' သည် ကြိယာပုဒ်ဖြစ်ပြီး 'အတွက်' သည် နာမ်/နာမ်စားနောက်၌သာ လိုက်နိုင်သော နာမ်ဝိဘတ်ဖြစ်သဖြင့် နာမဝိသေသနပြုပစ္စည်း 'သည့်/တဲ့' (သို့မဟုတ် နာမ်ပြုပစ္စည်း 'ခြင်း') ခံ၍ ဆက်စပ်ရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် 'အတွက်' နှင့် ကြိယာနာမ်ပြုဖွဲ့ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryFix,
                                MeaningCue = $"အကြောင်းရင်းပြ ဝါကျဆက် ({verb}~)",
                                PosTag = "PART",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = "သောကြောင့်",
                                MeaningCue = $"အကြောင်းပြသမ္ဗန္ဓ ({verb}သောကြောင့်)",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            if (ContainsMm(text, mmMask, "ကကို"))
            {
                foreach (Match m in AdjacentSubjObjCollisionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string pron = m.Groups["Pron"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ကို",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကတ္တားဝိဘတ် 'က' နှင့် ကံဝိဘတ် 'ကို' တစ်ဆက်တည်း ပူးတွဲသုံးစွဲ၍ မရပါ — '{pron}' ၏ ကာရကအနက်အလိုက် 'ကို' သို့မဟုတ် 'က' တစ်ခုတည်းသာ သုံးရမည်။",
                        "နာမ်ဝိဘတ် အမှန်", "PART", true,
                        "နာမ်စားတစ်ခုတည်းနောက်တွင် ပြုလုပ်သူပြ ကတ္တားဝိဘတ် ('က') နှင့် ခံရသူပြ ကံဝိဘတ် ('ကို') တို့သည် သဒ္ဒါတာဝန် ဆန့်ကျင်နေသဖြင့် ပူးတွဲမလိုက်နိုင်ပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်ဝိဘတ် ၁၇ မျိုး)");
                }
            }

            if (ContainsMm(text, mmMask, "တယ်သည်") ||
                ContainsMm(text, mmMask, "သည်တယ်") ||
                ContainsMm(text, mmMask, "မယ်မည်") ||
                ContainsMm(text, mmMask, "မည်မယ်"))
            {
                foreach (Match m in MixedRegisterSfpCollisionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = styleMode == ProofingStyleMode.FormalOfficial
                        ? (v.Contains("မည်") ? "မည်" : (v.Contains("ပါ") ? "ပါသည်" : "သည်"))
                        : (v.Contains("မယ်") ? "မယ်" : (v.Contains("ပါ") ? "ပါတယ်" : "တယ်"));
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အပြောစကားနှင့် အရေးစကား ဝါကျအဆုံးသတ်ဝိဘတ် နှစ်ထပ်ရောထွေးနေပါသည် — '{correct}' တစ်ခုတည်းသာ သုံးရမည်။",
                        "ကြိယာဝိဘတ် အမှန်", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                        "ဝါကျအဆုံးတွင် အပြောစကားဝိဘတ် ('တယ်/မယ်') နှင့် အရေးစကားဝိဘတ် ('သည်/မည်') တို့ကို ထပ်ဆင့်တွဲစပ်၍ မရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာဝိဘတ်)");
                }
            }

            if ((hasKhae && ContainsMm(text, mmMask, "ခဲ့ခဲ့")) ||
                (hasPyeeOpen && (ContainsMm(text, mmMask, "ပြီပြီ") || ContainsMm(text, mmMask, "သေးပြီ"))))
            {
                foreach (Match m in ContradictoryOrDoubledAspectPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = v == "ခဲ့ခဲ့" ? "ခဲ့" : "ပြီ";
                    string reason = v == "သေးပြီ"
                        ? "ဆက်လက်ဖြစ်ပေါ်ဆဲပြ 'သေး' နှင့် ပြီးစီးပြောင်းလဲပြ 'ပြီ' တို့သည် အနက်ဆန့်ကျင်နေသဖြင့် ပူးတွဲမသုံးရပါ။"
                        : $"ကြိယာနောက်ဆက် '{correct}' နှစ်ထပ်ဖြစ်နေသဖြင့် တစ်လုံးတည်းသာ ရေးရမည်။";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာနောက်ဆက် မှားယွင်းနေပါသည် — '{v}' အစား '{correct}' ဟု ရေးသားရမည်။",
                        "ကြိယာထောက်/နောက်ဆက် အမှန်", "PART", true,
                        reason,
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာပစ္စည်း)");
                }
            }

            if (hasKhae && ContainsMm(text, mmMask, "ပါခဲ့"))
            {
                foreach (Match m in NominalCopulaPastClashPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string numClf = m.Groups["NumClf"].Value;
                    string primaryFix = numClf + "ပါပဲ";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"နာမ်ပုဒ်နောက်တွင် ကြိယာမပါဘဲ အတိတ်ပြပစ္စည်း 'ခဲ့' တိုက်ရိုက်မကပ်ရပါ — '{primaryFix}' (သို့မဟုတ် '{numClf}ဖြစ်ခဲ့သည်') ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = "'ခဲ့' သည် ကြိယာနောက်၌သာ လိုက်နိုင်သော ကြိယာထောက်ပစ္စည်းဖြစ်သဖြင့် နာမ်/မျိုးပြပစ္စည်းနောက်ရှိ လေးနက်ပြပစ္စည်း 'ပါ' နောက်တွင် 'ဖြစ်-' ကြိယာမပါဘဲ '*ပါခဲ့' ဟု တွဲစပ်၍ မရပါ။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'ခဲ့' ၏ တည်နေရာစည်းမျဉ်း)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryFix,
                                MeaningCue = "နာမ်ဝါကျ လေးနက်ပြအဆုံးသတ် ('~ပါပဲ')",
                                PosTag = "PART",
                                IsOfficialThatpon = false,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = numClf + "ဖြစ်ခဲ့သည်",
                                MeaningCue = "အတိတ်ပြ ကြိယာဝါကျ ('~ဖြစ်ခဲ့သည်')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            bool hasKo = ContainsMm(text, mmMask, "ကို");
            bool hasMhar = ContainsMm(text, mmMask, "မှာ");

            if (hasKo && ContainsMm(text, mmMask, "ထဲကို"))
            {
                foreach (Match m in InteriorGoalWithStaticVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "တွင်" : "မှာ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ငြိမ်သက်တည်ရှိပြ ကြိယာရှေ့တွင် ရှေးရှုရာပြဝိဘတ် 'ကို' အစား နေရာပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        $"နေရာပြဝိဘတ် ('~{correct}')", "PART", styleMode == ProofingStyleMode.FormalOfficial,
                        "နောက်ရှိကြိယာ ('အနားယူနေ/ထိုင်နေ/အိပ်နေ/ရှိနေ') သည် ရွေ့လျားသွားလာခြင်းမဟုတ်ဘဲ တစ်နေရာတည်း၌ တည်ရှိဖြစ်ပျက်နေမှုကို ပြသဖြင့် ရှေးရှုရာပြဝိဘတ် 'ကို' အစား နေရာပြဝိဘတ် 'မှာ/တွင်' သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နေရာပြဝိဘတ် နှင့် ရှေးရှုရာပြဝိဘတ်)");
                }
            }

            if (hasMhar && ContainsMm(text, mmMask, "သွား"))
            {
                foreach (Match m in StaticLocativeWithMotionVerbPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string correct = styleMode == ProofingStyleMode.FormalOfficial ? "သို့" : "ကို";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဦးတည်ရွေ့လျားပြ ကြိယာ ('သွား-') ရှေ့တွင် တည်နေရာပြ 'မှာ' အစား ရှေးရှုရာပြဝိဘတ် '{correct}' ကို သုံးနှုန်းရမည်။",
                        $"ရှေးရှုရာပြဝိဘတ် ('~{correct}')", "PART", true,
                        "'သွားသည်/သွားတယ်' သည် ဦးတည်ရာနေရာသို့ ရွေ့လျားမှုကိုပြသော ကြိယာဖြစ်သဖြင့် ငြိမ်သက်တည်နေရာပြ 'မှာ' အစား ရှေးရှုရာပြဝိဘတ် 'ကို/သို့' နှင့် တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ရှေးရှုရာပြဝိဘတ်)");
                }
            }

            if (hasKo && (ContainsMm(text, mmMask, "ထုတ်ယူ") || ContainsMm(text, mmMask, "ပြန်လည်ရယူ")))
            {
                foreach (Match m in SourceRepositoryKoInsteadOfMhaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "မှ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ထုတ်ယူ/ပြန်လည်ရယူရာ မူလရင်းမြစ်နေရာနောက်တွင် ကံဝိဘတ် 'ကို' အစား ထွက်ခွာရာပြဝိဘတ် 'မှ' ကို သုံးနှုန်းရမည်။",
                        "ထွက်ခွာရာပြဝိဘတ် ('~မှ')", "PART", true,
                        "နောက်တွင် ထုတ်ယူခံရသည့် တိုက်ရိုက်ကံပုဒ် သီးခြားပါရှိနေပြီဖြစ်သဖြင့် မူလသိုလှောင်ရာ/ရင်းမြစ်နေရာပုဒ်နောက်၌ ကံဝိဘတ် 'ကို' မသုံးရဘဲ ထွက်ခွာရာပြဝိဘတ် 'မှ' သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ထွက်ခွာရာပြဝိဘတ် 'မှ')");
                }
            }

            if (hasKo && ContainsMm(text, mmMask, "စစ်ဆေးမှု"))
            {
                foreach (Match m in RegionKoBeforeNominalizedInspectionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "တွင်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "'စစ်ဆေးမှု ပြုလုပ်-' ရှေ့ရှိ ဒေသအမည်နောက်တွင် 'ကို' အစား နေရာပြဝိဘတ် 'တွင်' ကို သုံးနှုန်းရမည်။",
                        "နေရာပြဝိဘတ် ('~တွင်')", "PART", true,
                        "'ပြုလုပ်-' ကြိယာ၏ တိုက်ရိုက်ကံပုဒ်အဖြစ် 'စစ်ဆေးမှု' ပါရှိနေပြီဖြစ်ရာ ရှေ့ရှိ ပြည်နယ်/မြို့နယ်ပုဒ်သည် လုပ်ငန်းဆောင်ရွက်ရာ တည်နေရာဖြစ်သဖြင့် နေရာပြဝိဘတ် 'တွင်' သုံးရမည် ('စစ်ဆေးခဲ့သည်' ဟု တိုက်ရိုက်ကြိယာသုံးမှသာ 'ကို' သုံးနိုင်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နေရာပြဝိဘတ် နှင့် ကံဝိဘတ် ခွဲခြားပုံ)");
                }
            }

            if (ContainsMm(text, mmMask, "တည်ရှိ"))
            {
                foreach (Match m in RegionKaBeforeTashiPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    bool formalSentence = styleMode == ProofingStyleMode.FormalOfficial || m.Groups["End"].Value != "တယ်";
                    string primary = formalSentence ? "တွင်" : "မှာ";
                    string secondary = formalSentence ? "မှာ" : "တွင်";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"'တည်ရှိ-' ကြိယာရှေ့ရှိ ပြည်နယ်/တိုင်းဒေသကြီး/မြို့နယ် တည်နေရာပုဒ်နောက်တွင် 'က' အစား နေရာပြဝိဘတ် '{primary}' ကို သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = "'တည်ရှိသည်' သည် တစ်နေရာ၌ တည်နေခြင်းကိုပြသော ကြိယာဖြစ်သဖြင့် ဒေသအမည်နောက်တွင် ထွက်ခွာရာ/ကတ္တားဝိဘတ် 'က' အစား နေရာပြဝိဘတ် 'တွင်/၌/မှာ' ကို သုံးရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် — နေရာပြဝိဘတ် 'တွင်/၌/မှာ')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate { Replacement = primary, MeaningCue = $"နေရာပြဝိဘတ် ('~{primary}')", PosTag = "PART", IsOfficialThatpon = formalSentence, Score = 100.0 },
                            new SuggestionCandidate { Replacement = secondary, MeaningCue = $"နေရာပြဝိဘတ်ပုံစံကွဲ ('~{secondary}')", PosTag = "PART", IsOfficialThatpon = !formalSentence, Score = 95.0 }
                        }
                    });
                }
            }

            if (ContainsMm(text, mmMask, "အစိတ်အပိုင်း"))
            {
                foreach (Match m in PartitiveKaBeforeAsateApaingPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    bool formalSentence = styleMode == ProofingStyleMode.FormalOfficial || m.Groups["End"].Value.EndsWith("သည်", StringComparison.Ordinal);
                    string primary = formalSentence ? "၏" : "ရဲ့";
                    string secondary = formalSentence ? "ရဲ့" : "၏";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"'အစိတ်အပိုင်းတစ်ခု' ရှေ့ရှိ ပိုင်ဆိုင်/ဆိုင်ရာပြပုဒ်နောက်တွင် 'က' အစား ပိုင်ဆိုင်မှုပြဝိဘတ် '{primary}' ကို သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = "'အစိတ်အပိုင်းတစ်ခု' သည် ရှေ့နာမ်ပုဒ်၏ အစိတ်အပိုင်းဖြစ်ကြောင်း ပိုင်ဆိုင်ဆက်စပ်မှုကို ပြသဖြင့် '၏/ရဲ့' ဝိဘတ်ကို သုံးရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အဆက်တွဲဝိဘတ် — ပိုင်ဆိုင်မှုပြဝိဘတ် '၏/ရဲ့')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate { Replacement = primary, MeaningCue = $"ပိုင်ဆိုင်မှုပြဝိဘတ် ('~{primary}')", PosTag = "PART", IsOfficialThatpon = formalSentence, Score = 100.0 },
                            new SuggestionCandidate { Replacement = secondary, MeaningCue = $"ပိုင်ဆိုင်မှုပြဝိဘတ် ('~{secondary}')", PosTag = "PART", IsOfficialThatpon = !formalSentence, Score = 95.0 }
                        }
                    });
                }
            }

            if (ContainsMm(text, mmMask, "လာမည်"))
            {
                foreach (Match m in MissingCreakyAttributiveLamyiPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string unit = m.Groups["Unit"].Value;
                    string fixedAttr = "လာမည့်" + unit;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, fixedAttr,
                        ProofingIssueType.SpellingError,
                        $"အချိန်ကာလပြနာမ် '{unit}' ရှေ့ရှိ အနာဂတ် နာမဝိသေသနပုဒ်တွင် အောက်မြစ်ပါဝင်လျက် '{fixedAttr}' ဟု ရေးသားရမည်။",
                        $"နာမဝိသေသနပြု အောက်မြစ် ('{fixedAttr}')", "ADJ", true,
                        $"နောက်ကပ်လျက် နာမ်ပုဒ် '{unit}' ကို အထူးပြုသော အနာဂတ်ပြပစ္စည်းဖြစ်သဖြင့် ဝါကျအဆုံးသတ် 'မည်' မဟုတ်ဘဲ နာမဝိသေသနပြု 'မည့်' ('လာမည့်{unit}') ဟု အောက်မြစ်ထည့်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }

            if (hasMhar && hasKhae)
            {
                foreach (Match m in BenefactiveMhaInsteadOfAtwetPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "အတွက်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ရည်ရွယ်ပေးအပ်/တင်သွင်းခြင်းကို ပြရာတွင် တည်နေရာပြ 'မှာ' အစား ရည်စူးချက်ပြဝိဘတ် 'အတွက်' (သို့မဟုတ် 'သို့') ကို သုံးနှုန်းရမည်။",
                        "ရည်စူးချက်ပြဝိဘတ် ('~အတွက်')", "PART", true,
                        "လေ့ကျင့်ခန်းပေးခြင်း သို့မဟုတ် အစီရင်ခံစာတင်သွင်းခြင်း၏ ရည်စူးရာ/ဦးတည်ရာဖြစ်သဖြင့် အပြောစကား နေရာပြ 'မှာ' အစား ရည်စူးချက်ပြဝိဘတ် 'အတွက်' (သို့မဟုတ် 'သို့') နှင့် တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ရည်စူးချက်ပြဝိဘတ် 'အတွက်')");
                }
            }

            if (hasKo && hasHorizSpace &&
                (ContainsMm(text, mmMask, "ကျွန်") ||
                 ContainsMm(text, mmMask, "အစ်")))
            {
                foreach (Match m in TripleKoSubjectConfusionPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string subjFix = g.Value.Substring(0, g.Value.Length - 3) + "က";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, subjFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျ၏ ပြုလုပ်သူ (ကတ္တား) နောက်တွင် ကံဝိဘတ် 'ကို' မှားယွင်းသုံးထားပါသည် — '{subjFix}' ဟု ရေးသားရမည်။",
                        "ကတ္တားဝိဘတ် ('~က')", "PART", true,
                        "နောက်ပိုင်းတွင် ကံပုဒ်နှင့် လက်ခံသူပုဒ်များ ပါရှိနေပြီး ရှေ့ဆုံးနာမ်စားသည် ပြောဆို/ပေးပို့သူ ကတ္တားဖြစ်သဖြင့် ကတ္တားဝိဘတ် 'က' ကို သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် 'က')");
                }
            }

            // Universal 'မှ' (ထွက်ခွာရာပြဝိဘတ်) misused as 'က' (ကတ္တားဝိဘတ်) — e.g. 'နေပြည်တော်မှကြိုဆိုပါသည် -> နေပြည်တော်ကကြိုဆိုပါသည်', 'ဝန်ကြီးဌာနမှ အသိပေးသည် -> ဝန်ကြီးဌာနက'
            if (hasMa && ((mmMask & (1UL << ('\u103E' - '\u1000'))) != 0UL) && HasStandaloneMhaParticle(text))
            {
                if (ContainsMm(text, mmMask, "ကြိုဆို"))
                {
                    foreach (Match m in HostWelcomeMhaSubjectPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        string host = m.Groups["Host"].Value.Trim();
                        AddGrammarOrSpellingIssue(
                            issues, pGroup.Index, pGroup.Length, pGroup.Value, "က",
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"'{host}' သည် ကြိုဆိုသူ (ကတ္တား) ဖြစ်သဖြင့် ထွက်ခွာရာပြဝိဘတ် 'မှ' အစား ကတ္တားဝိဘတ် 'က' ကို သုံးနှုန်းရမည် (ဥပမာ - '{host}က ကြိုဆိုပါသည်')။",
                            "ကတ္တားဝိဘတ် (~က ကြိုဆိုပါသည်)", "PART", true,
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါအရ 'မှ' သည် နေရာ/အချိန်မှ စတင်ထွက်ခွာရာကိုပြသော ထွက်ခွာရာပြဝိဘတ်သာဖြစ်ပြီး၊ ကြိုဆိုမှုကို ပြုလုပ်သော မြို့/ဒေသ/အဖွဲ့အစည်း (ကတ္တား) နောက်တွင် ကတ္တားဝိဘတ် 'က' ကိုသာ သုံးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် 'က' နှင့် ထွက်ခွာရာပြဝိဘတ် 'မှ')");
                    }
                }

                foreach (Match m in AgentiveMhaSubjectPattern.Matches(text))
                {
                    Group pGroup = m.Groups["Particle"];
                    string verb = m.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, pGroup.Index, pGroup.Length, pGroup.Value, "က",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ပြုလုပ်သူ (ကတ္တား) နောက်တွင် ထွက်ခွာရာပြဝိဘတ် 'မှ' အစား ကတ္တားဝိဘတ် 'က' (သို့မဟုတ် 'သည်') ကို သုံးနှုန်းရမည်။",
                        $"ကတ္တားဝိဘတ် (~က {verb})", "PART", true,
                        $"မြန်မာသဒ္ဒါအရ 'မှ' သည် နေရာ/အချိန်မှ စတင်ထွက်ခွာရာကိုပြသော ထွက်ခွာရာပြဝိဘတ်သာဖြစ်ပြီး '{verb}-' မှုကို ပြုလုပ်သူ (ကတ္တား) နောက်၌ ကတ္တားဝိဘတ် 'က' သို့မဟုတ် 'သည်' ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကတ္တားဝိဘတ် နှင့် ထွက်ခွာရာပြဝိဘတ်) နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            if (ContainsMm(text, mmMask, "လို့") && ContainsMm(text, mmMask, "မည်ဖြစ်သည်"))
            {
                foreach (Match m in ColloquialLoWithBureaucraticMyiPhitPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    string correct = verb + "မယ်";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အပြောစကား 'လို့' ဝါကျခွဲနှင့် ရုံးသုံး 'မည်ဖြစ်သည်' စကားအဆင့် ရောနှောနေပါသည် — '{correct}' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                        "စကားအဆင့် ညီညွတ်သော အပြောစကားဝိဘတ်", "V", false,
                        "ဝါကျရှေ့ပိုင်း၌ အပြောစကား အကြောင်းပြပစ္စည်း ('...လို့') သုံးထားပါလျက် ဝါကျအဆုံး၌ ရုံးသုံးစာပေဟန် ('...မည်ဖြစ်သည်') သုံးထားသဖြင့် အပြော/အရေး စကားအဆင့် (Register) မညီညွတ်ပါ။",
                        "မောင်ခင်မင်(ဓနုဖြူ) — အပြောစကားနှင့် အရေးစကား ညီညွတ်မှုစည်းမျဉ်း");
                }
            }

            if (ContainsMm(text, mmMask, "တော်တော်") || ContainsMm(text, mmMask, "အူသွားတယ်"))
            {
                foreach (Match m in FormalContextSlangPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string v = g.Value;
                    string correct = v == "တော်တော်ဂွမ်း" ? "အလွန်အားနည်းစွာ"
                                   : v == "အူသွားတယ်" ? "ဝမ်းမြောက်ခဲ့ကြသည်"
                                   : "အလွန်ဆိုးရွားနေသည်";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, v, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စာပေ/သတင်း အရေးအသားအတွင်း ဗန်းစကား '{v}' ပါဝင်နေပါသည် — '{correct}' ဟု စံအရေးစကားဖြင့် ရေးသားရမည်။",
                        "စာပေ/ရုံးသုံး အရေးအသား", "ADV", true,
                        $"ဝါကျသည် စာတမ်း/သတင်း/ရုံးသုံး အရေးစကားပုံစံ ဖြစ်သဖြင့် အရပ်သုံးဗန်းစကား ('{v}') အစား အဓိပ္ပာယ်တိကျသော စာပေအသုံး ('{correct}') ကို သုံးရမည်။",
                        "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            // 4j. Universal Direct-Object 'အား -> ကို', Plural Verb 'ကျ -> ကြ', Modal 'သည့် -> သင့်' & Duplicated Function Words
            bool hasAh = ContainsMm(text, mmMask, "အား");
            if (hasAh)
            {
                foreach (Match m in PhysicalDirectObjectAhPattern.Matches(text))
                {
                    Group pGroup = m.Groups["Particle"];
                    string obj = m.Groups["Obj"].Value;
                    string verb = m.Groups["Verb"].Value;
                    int winStart = Math.Max(0, pGroup.Index - 20);
                    int winEnd = Math.Min(text.Length, m.Index + m.Length + 20);
                    string clauseWin = text.Substring(winStart, winEnd - winStart);
                    if (clauseWin.Contains("ကို") || clauseWin.Contains("စေ"))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, pGroup.Index, pGroup.Length, pGroup.Value, "ကို",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သက်မဲ့ကံပုဒ် '{obj}' နောက်တွင် တိုက်ရိုက်ကြိယာ '{verb}-' လိုက်သဖြင့် လက်ခံပြဝိဘတ် 'အား' အစား ကံဝိဘတ် 'ကို' သုံးနှုန်းရမည်။",
                        $"ကံဝိဘတ် ({obj}ကို {verb}-)", "PART", true,
                        $"မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါအရ 'အား' သည် ပေးကမ်းချီးမြှင့်ခြင်းကို လက်ခံရယူသူပြ လက်ခံပြဝိဘတ်ဖြစ်ပြီး '{obj}' ကို တိုက်ရိုက်ပြုလုပ်သော '{verb}-' ကြိယာရှေ့၌ ကံဝိဘတ် 'ကို' ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကံဝိဘတ် 'ကို' နှင့် လက်ခံပြဝိဘတ် 'အား')");
                }

                foreach (Match m in EventDocumentLawDirectObjectAhPattern.Matches(text))
                {
                    Group pGroup = m.Groups["Particle"];
                    if (OverlapsExistingIssue(issues, pGroup.Index, pGroup.Length))
                        continue;
                    string obj = m.Groups["Obj"].Value;
                    string verb = m.Groups["Verb"].Value;
                    int winStart = Math.Max(0, pGroup.Index - 24);
                    int winEnd = Math.Min(text.Length, m.Index + m.Length + 24);
                    string clauseWin = text.Substring(winStart, winEnd - winStart);
                    if (clauseWin.Contains("ကို") || clauseWin.Contains("စေ") || clauseWin.Contains("အဖြစ်"))
                        continue;

                    AddGrammarOrSpellingIssue(
                        issues, pGroup.Index, pGroup.Length, pGroup.Value, "ကို",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သက်မဲ့ကံပုဒ် '{obj}' နောက်တွင် တိုက်ရိုက်ကံလိုကြိယာ '{verb}-' လိုက်သဖြင့် လက်ခံပြဝိဘတ် 'အား' အစား ကံဝိဘတ် 'ကို' သုံးနှုန်းရမည်။",
                        $"ကံဝိဘတ် ({obj}ကို {verb}-)", "PART", true,
                        $"မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါနှင့် ရုံးသုံးမြန်မာစာအရ 'အား' သည် ပေးကမ်း/လှူဒါန်းခြင်းကို လက်ခံရယူသူ၌သာ သုံးရသော လက်ခံပြဝိဘတ်ဖြစ်ပြီး၊ သက်မဲ့ '{obj}' ကို တိုက်ရိုက်ဆောင်ရွက်သော '{verb}-' ကြိယာရှေ့၌ ကံဝိဘတ် 'ကို' ကိုသာ သုံးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးသားနည်း (မောင်ခင်မင်-ဓနုဖြူ)");
                }
            }

            if (ContainsMm(text, mmMask, "ကျ"))
            {
                foreach (Match m in PluralVerbKyaMisusePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    string verb = m.Groups["Verb"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "ကြ",
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အများပြ ကြိယာထောက်ပစ္စည်းတွင် 'ကျ' (ယပင့်) အစား 'ကြ' (ရရစ် — '{verb}ကြ...') ဟု ရေးသားရမည်။",
                        $"အများပြ ကြိယာထောက်ပစ္စည်း ({verb}ကြ~)", "PART", true,
                        $"'{verb}-' ကြိယာကို အများဝုစ်ဖြင့် ပြုလုပ်ကြောင်းပြရာ၌ အများပြ ကြိယာထောက်ပစ္စည်း 'ကြ' (ကကြီးရရစ်) ကို သုံးရမည် ('ကျ' သည် အောက်သို့ကျဆင်းခြင်း/သဘောကျခြင်း အနက်ဖြစ်သည်)။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း");
                }
            }

            if (hasCreakyDot && ContainsMm(text, mmMask, "သည့်ပါ"))
            {
                foreach (Match m in MisusedSinthBeforePoliteSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "သင့်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "သင့်လျော်ကြောင်းပြ ကြိယာထောက်ပစ္စည်းတွင် 'သည့်' အစား 'သင့်' (ငသတ် အောက်မြစ် — '~သင့်ပါသည်') ဟု ရေးသားရမည်။",
                        "သင့်လျော်ကြောင်းပြ ကြိယာထောက်ပစ္စည်း ('~သင့်ပါသည်')", "PART", true,
                        "နောက်တွင် ယဉ်ကျေးမှုပြပစ္စည်းနှင့် ဝါကျအဆုံးသတ်ဝိဘတ် ('ပါသည်/ပါတယ်') တိုက်ရိုက်လိုက်နေသဖြင့် နာမဝိသေသနပြုပစ္စည်း 'သည့်' မဖြစ်နိုင်ဘဲ သင့်လျော်ကြောင်းပြ ကြိယာထောက်ပစ္စည်း 'သင့်' ဖြစ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း 'သင့်')");
                }
            }

            if (hasMyanmarSpaceMyanmar && MayContainDuplicatedFunctionWord(text, mmMask))
            {
                foreach (Match m in DuplicatedFunctionWordPattern.Matches(text))
                {
                    string singleWord = m.Groups["Word"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, singleWord,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"သဒ္ဒါပစ္စည်း '{singleWord}' နှစ်ထပ်ရိုက်မိနေပါသည် — '{singleWord}' တစ်ကြိမ်သာ ရေးသားရမည်။",
                        $"နှစ်ထပ်စာလုံး ဖယ်ရှားရန် ('{singleWord}')", "PART", true,
                        $"ဝိဘတ်/သမ္ဗန္ဓပစ္စည်း '{singleWord}' ကို Space ခြား၍ နှစ်ကြိမ်ထပ်ရေးမိထားသဖြင့် တစ်လုံးတည်းသာ ထားရှိရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ");
                }
            }

            // 4m. V42 Zero-Regex IndexOf Trie-POS Syntactic Homophone Disambiguation ('ကျောင်း->ကြောင်း', 'ချင်း->ခြင်း', generalized 'ကျ->ကြ')
            if (ContainsMm(text, mmMask, "ကျောင်း"))
            {
                ScanMisusedKyaungNominalizer(text, trie, issues);
            }

            if (trie != null && ContainsMm(text, mmMask, "ချင်း"))
            {
                ScanMisusedChinNominalizer(text, trie, issues);
            }

            if (trie != null && ContainsMm(text, mmMask, "ကျ"))
            {
                ScanMisusedKyaPluralVerb(text, trie, issues);
            }

            // 4n. V42 Smart Accidental Repeated-Word Detection (Myanmar multi-syllable words, stuttered noun+case, & English function words)
            if (hasMyanmarSpaceMyanmar && MayContainAdjacentDuplicateMyanmarWord(text))
            {
                foreach (Match m in DuplicatedMultiSyllableMyanmarWordPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;

                    string singleWord = m.Groups["Word"].Value;
                    if (!IsAccidentalDuplicateMyanmarWord(singleWord, trie))
                        continue;

                    // Exempt measurement range expressions where digits were omitted/stripped before 'ခန့်/လောက်' (e.g. 'လက်မ လက်မ ခန့်')
                    if (singleWord == "လက်မ" || singleWord == "မီတာ" || singleWord == "ကီလို")
                    {
                        int afterDup = m.Index + m.Length;
                        while (afterDup < text.Length && (text[afterDup] == ' ' || text[afterDup] == '\t'))
                            afterDup++;
                        if (MatchesSliceAt(text, afterDup, "ခန့်") || MatchesSliceAt(text, afterDup, "ခန့်") || MatchesSliceAt(text, afterDup, "လောက်"))
                            continue;
                    }

                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, singleWord,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စကားလုံး '{singleWord}' ကို မတော်တဆ နှစ်ကြိမ်ထပ်ရိုက်မိနေပါသည် — '{singleWord}' တစ်ကြိမ်သာ ရေးသားရမည်။",
                        $"နှစ်ထပ်စာလုံး ဖယ်ရှားရန် ('{singleWord}')", "DUP", true,
                        $"စာရိုက်ရာတွင် သို့မဟုတ် ကူးယူရာတွင် '{singleWord}' ကို Space ခြား၍ နှစ်ကြိမ်ဆက်တိုက် ထပ်ရေးမိထားခြင်း ဖြစ်ပါသည်။",
                        "စာတမ်းတည်းဖြတ်ရေးနှင့် ရုံးသုံးမြန်မာစာ စံနှုန်း");
                }

                foreach (Match m in DuplicatedMyanmarWordWithCasePattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;

                    string singleWord = m.Groups["Word"].Value;
                    string singleNorm = UnicodeNormalizer.NormalizeCanonicalOrder(singleWord);
                    if (singleNorm.StartsWith("တစ်", StringComparison.Ordinal) ||
                        singleNorm.StartsWith("တ", StringComparison.Ordinal) && (singleNorm == "တဦး" || singleNorm == "တယောက်" || singleNorm == "တခု") ||
                        singleNorm.StartsWith("အချင်း", StringComparison.Ordinal) ||
                        singleNorm.StartsWith("အသီး", StringComparison.Ordinal) ||
                        singleNorm.StartsWith("အမျိုး", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    bool okDupWithCase = IsAccidentalDuplicateMyanmarWord(singleWord, trie) ||
                        (singleNorm.Length >= 4 && trie != null &&
                         (trie.IsStandaloneWord(singleNorm) || trie.ContainsExactWord(singleNorm)) &&
                         !SpaceSplitBoundaryParticles.Contains(singleNorm));
                    if (!okDupWithCase)
                        continue;

                    string casePart = m.Groups["Case"].Value;
                    string dedupWithCase = singleWord + casePart;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, dedupWithCase,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"နာမ်ပုဒ် '{singleWord}' ကို မတော်တဆ နှစ်ကြိမ်ထပ်ရိုက်မိနေပါသည် — '{dedupWithCase}' ဟု တစ်ကြိမ်သာ ရေးသားရမည်။",
                        $"နှစ်ထပ်နာမ်ပုဒ် ဖယ်ရှားရန် ('{dedupWithCase}')", "DUP", true,
                        $"နာမ်ဝိဘတ် '{casePart}' မတွဲမီ ရှေ့နာမ်ပုဒ် '{singleWord}' ကို Space ခြား၍ နှစ်ကြိမ်ထပ်ရေးမိထားခြင်း ဖြစ်ပါသည်။",
                        "စာတမ်းတည်းဖြတ်ရေးနှင့် မြန်မာသဒ္ဒါ စံနှုန်း");
                }
            }

            if (hasHorizSpace && hasAsciiLetter && MayContainAdjacentDuplicateAsciiWord(text))
            {
                foreach (Match m in DuplicatedEnglishFunctionWordPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;

                    string singleWord = m.Groups["Word"].Value;
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, singleWord,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"အင်္ဂလိပ် သဒ္ဒါစာလုံး '{singleWord}' နှစ်ထပ်ရိုက်မိနေပါသည် — '{singleWord}' တစ်ကြိမ်သာ ထားရှိရမည်။",
                        $"Remove duplicate '{singleWord}'", "PART", true,
                        $"Duplicate function word '{m.Value}' detected across whitespace.",
                        "Bilingual Document Editing Standard");
                }
            }

            // 4k. V19 Deep Myanmar Grammar Pattern Checks (O(1) pre-filtered)
            // 4k-1. WH-Interrogative <-> Polar Question Particle Clash ('လား/သလား' vs 'လဲ/သလဲ/သနည်း')
            if (hasVisarga && ContainsMm(text, mmMask, "လား"))
            {
                foreach (Match m in WhInterrogativeWithPolarLarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string wh = m.Groups["Wh"].Value;
                    string raw = g.Value;
                    bool isLiteraryWh = wh.StartsWith("အဘယ်", StringComparison.Ordinal) || wh.StartsWith("မည်", StringComparison.Ordinal);
                    bool preferFormal = isLiteraryWh || styleMode == ProofingStyleMode.FormalOfficial;
                    string formalFix = raw == "ပါသလား" ? "ပါသနည်း"
                                     : raw == "သလား" ? "သနည်း"
                                     : raw == "ပါမလား" ? "ပါမည်နည်း"
                                     : raw == "မလား" ? "မည်နည်း"
                                     : "သနည်း";
                    string colloqFix = raw.Substring(0, raw.Length - 3) + "လဲ";
                    string primary = preferFormal ? formalFix : colloqFix;
                    string secondary = preferFormal ? colloqFix : formalFix;

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = raw,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အကြောင်းအရာမေးပုဒ် '{wh}' ပါသော မေးခွန်းဝါကျကို 'ဟုတ်/မဟုတ်' အမေးပစ္စည်း '{raw}' ဖြင့် မပိတ်ရပါ — '{primary}' ဟု သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါအရ '{wh}' သည် အချက်အလက်/အကြောင်းရင်းကို ဖြေဆိုရသော အမေးပုဒ်ဖြစ်သဖြင့် ဟုတ်/မဟုတ် မေးခွန်းပိတ်ပစ္စည်း 'လား/သလား' နှင့် သဒ္ဒါသဘော ဆန့်ကျင်နေပါသည် — အကြောင်းအရာအမေးပိတ်ပစ္စည်း '{primary}' ('လဲ/သနည်း') ကိုသာ သုံးရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အမေးပစ္စည်း 'လား'၊ 'လဲ'၊ 'သနည်း' ခွဲခြားပုံ)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primary,
                                MeaningCue = preferFormal ? "စာပေ/ရုံးသုံး အမေးပိတ်ပစ္စည်း ('~နည်း')" : "အကြောင်းအရာမေးပစ္စည်း ('~လဲ')",
                                PosTag = "PART",
                                IsOfficialThatpon = preferFormal,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = secondary,
                                MeaningCue = preferFormal ? "အပြောစကား အမေးပိတ်ပစ္စည်း ('~လဲ')" : "စာပေသုံး အမေးပိတ်ပစ္စည်း ('~နည်း')",
                                PosTag = "PART",
                                IsOfficialThatpon = !preferFormal,
                                Score = 94.0
                            }
                        }
                    });
                }
            }

            if (hasLae)
            {
                foreach (Match m in LiteraryWhWithColloquialLaePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string wh = m.Groups["Wh"].Value;
                    string raw = g.Value;
                    string formalFix = raw == "ပါသလဲ" ? "ပါသနည်း"
                                     : raw == "သလဲ" ? "သနည်း"
                                     : raw == "ပါမလဲ" ? "ပါမည်နည်း"
                                     : raw == "မလဲ" ? "မည်နည်း"
                                     : "သနည်း";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, formalFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စာပေသုံး အမေးပုဒ် '{wh}' နှင့် အပြောစကား '{raw}' စကားအဆင့် ရောထွေးနေပါသည် — '{formalFix}' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                        "စာပေသုံး အမေးပိတ်ပစ္စည်း ('~နည်း')", "PART", true,
                        $"ရှေ့တွင် စာပေအရေးစကား အမေးနာမ်စား '{wh}' သုံးထားသဖြင့် ဝါကျအဆုံး၌လည်း စာပေသုံး အမေးပစ္စည်း '{formalFix}' ဖြင့် စကားအဆင့် ညှိနှိုင်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) (အဆက်တွဲဝိဘတ်)");
                }
            }

            // 4k-2. Oblique Creaky-Tone Pronoun Case Discord & Phonologically Illegal Creaky Pronouns
            if (hasCreakyDot)
            {
                foreach (Match m in CreakyPronounBeforeSubjectOrPluralPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string rawPron = m.Groups["RawPron"].Success ? m.Groups["RawPron"].Value : m.Groups["RawPron2"].Value;
                    string basePron = rawPron.Replace("\u1037", string.Empty);
                    string post = m.Groups["Post"].Success ? m.Groups["Post"].Value : m.Groups["Post2"].Value;
                    bool isBenefactiveObjectRequest =
                        post == "က" &&
                        g.Index + g.Length < text.Length &&
                        Regex.IsMatch(text.Substring(g.Index + g.Length), @"^\s*(?:ကူညီပေး|ခွင့်လွှတ်ပေး|ခွင့်လွှတ်ပေး|ကယ်တင်ပေး)(?:လို့\s*ရပါမလား|ပါ)");
                    string correct = isBenefactiveObjectRequest ? (rawPron + "ကို") : (basePron + post);
                    string roleDesc = post == "တို့" ? "အများပြပစ္စည်း 'တို့'" : $"ကတ္တားဝိဘတ် '{post}'";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"{roleDesc} ရှေ့ရှိ နာမ်စားတွင် အောက်မြစ်သံပြောင်း မထည့်ရပါ — '{g.Value}' အစား '{correct}' ဟု ရေးသားရမည်။",
                        $"မူလသံ နာမ်စား + {roleDesc} ('{correct}')", "PRON", true,
                        $"မြန်မာသဒ္ဒါ နာမ်စားစည်းမျဉ်းအရ ပိုင်ဆိုင်ပြနှင့် ကံ/လက်ခံဝိဘတ် ('ကို/အား/အတွက်') ရှေ့၌သာ အောက်မြစ်သံပြောင်း ('{rawPron}') ဖြစ်ပေါ်ပြီး၊ {roleDesc} ရှေ့တွင် မူလသံ '{basePron}' အတိုင်း '{correct}' ဟုသာ ရေးရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (နာမ်စား အောက်မြစ်သံပြောင်း စည်းမျဉ်း)");
                }

                foreach (Match m in IllegalPronounCreakyTonePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string basePron = g.Value.Replace("\u1037", string.Empty);
                    string possFix = styleMode == ProofingStyleMode.FormalOfficial ? (basePron + "၏") : (basePron + "ရဲ့");
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"နာမ်စား '{basePron}' တွင် အောက်မြစ် ('့') ထပ်မထည့်ရပါ — '{basePron}' (သို့မဟုတ် ပိုင်ဆိုင်ပြ '{possFix}') ဟု ရေးသားရမည်။",
                        GrammaticalReasoning = basePron == "ကျွန်ုပ်"
                            ? "'ကျွန်ုပ်' သည် ပသတ် ('ပ်') အသတ်ပါသော ရပ်သံဝဏ္ဏဖြစ်သဖြင့် အောက်မြစ် ('့') လုံးဝ မတင်နိုင်ပါ (ပိုင်ဆိုင်မှုပြလိုပါက 'ကျွန်ုပ်၏' ဟု ရေးရသည်)။"
                            : $"'{basePron}' ၏ နောက်ဆုံးဝဏ္ဏ 'မ' သည် ပင်ကိုသရတို အောက်မြစ်သံ ပါပြီးဖြစ်သဖြင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းအရ အောက်မြစ် ('့') ထပ်မထည့်ရပါ။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = basePron,
                                MeaningCue = $"စံနာမ်စား စာလုံးပေါင်း ('{basePron}')",
                                PosTag = "PRON",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = possFix,
                                MeaningCue = $"ပိုင်ဆိုင်ပြပုဒ် ('{possFix}')",
                                PosTag = "PRON",
                                IsOfficialThatpon = styleMode == ProofingStyleMode.FormalOfficial,
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            // 4k-3. Pleonastic & Hybrid Grammatical Constructions ('မတိုင်မီခင်', 'အလို့ငှာအတွက်', 'သဖြင့်ကြောင့်', 'များတွေ', 'တွင်၌', etc.)
            if (HasAnySubstring(text, mmMask, PleonasticTriggers))
            {
                foreach (Match m in PleonasticHybridGrammarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string p1;
                    string p2;
                    switch (raw)
                    {
                        case "မတိုင်မီခင်":
                        case "မတိုင်ခင်မီ":
                            p1 = styleMode == ProofingStyleMode.FormalOfficial ? "မတိုင်မီ" : "မတိုင်ခင်";
                            p2 = styleMode == ProofingStyleMode.FormalOfficial ? "မတိုင်ခင်" : "မတိုင်မီ";
                            break;
                        case "အလို့ငှာအတွက်":
                            p1 = "အတွက်";
                            p2 = "အလို့ငှာ";
                            break;
                        case "သဖြင့်ကြောင့်":
                            p1 = "သောကြောင့်";
                            p2 = "သဖြင့်";
                            break;
                        case "သောကြောင့်မို့လို့":
                        case "သောကြောင့်မို့":
                            p1 = "သောကြောင့်";
                            p2 = "ဒါကြောင့်မို့";
                            break;
                        case "သို့သော်လည်းပဲ":
                        case "သို့သော်ငြားလည်းပဲ":
                            p1 = "သို့သော်လည်း";
                            p2 = "ဒါပေမဲ့";
                            break;
                        case "များတွေ":
                            p1 = styleMode == ProofingStyleMode.FormalOfficial ? "များ" : "တွေ";
                            p2 = styleMode == ProofingStyleMode.FormalOfficial ? "တွေ" : "များ";
                            break;
                        case "တွင်၌":
                        case "၌တွင်":
                            p1 = "တွင်";
                            p2 = "၌";
                            break;
                        case "သို့ကို":
                            p1 = "သို့";
                            p2 = "ကို";
                            break;
                        case "ဖြင့်နှင့်":
                        case "နှင့်ဖြင့်":
                            p1 = "ဖြင့်";
                            p2 = "နှင့်";
                            break;
                        default: // မှကနေ
                            p1 = styleMode == ProofingStyleMode.FormalOfficial ? "မှ" : "ကနေ";
                            p2 = styleMode == ProofingStyleMode.FormalOfficial ? "ကနေ" : "မှ";
                            break;
                    }

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = raw,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"အနက်တူ သဒ္ဒါပစ္စည်းနှစ်ခု ထပ်ဆင့်ရောထွေးနေပါသည် ('{raw}') — '{p1}' သို့မဟုတ် '{p2}' တစ်ခုတည်းကိုသာ သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = $"'{raw}' သည် တာဝန်တူ ဝိဘတ်/သမ္ဗန္ဓပစ္စည်းနှစ်ခု ('{p1}' နှင့် '{p2}') ကို နှစ်ထပ်တွဲစပ်ထားသော သဒ္ဒါအနက်ထပ် (Pleonasm) အမှားဖြစ်သဖြင့် စကားအဆင့်နှင့် လျော်ညီသော တစ်ခုတည်းကိုသာ ရွေးချယ်ရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) — သဒ္ဒါအနက်ထပ် အမှားများ",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = p1,
                                MeaningCue = $"ကျစ်လျစ်မှန်ကန်သော သဒ္ဒါပုဒ် ('{p1}')",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = p2,
                                MeaningCue = $"အခြားရွေးချယ်ရန် ပုဒ် ('{p2}')",
                                PosTag = "PART",
                                IsOfficialThatpon = true,
                                Score = 94.0
                            }
                        }
                    });
                }
            }

            // 4k-4. Adverbial 'စသဖြင့်' misused before nominal plural/case postpositions -> 'စသည်...'
            bool hasSaThaPhint = hasCreakyDot && ContainsMm(text, mmMask, "သဖြင့်");
            if (hasSaThaPhint && ContainsMm(text, mmMask, "စသဖြင့်"))
            {
                foreach (Match m in MisusedSaThaPhintNominalPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string post = m.Groups["Post"].Value;
                    string correct = "စသည်" + post;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"'စသဖြင့်' နောက်တွင် နာမ်ပစ္စည်း/နာမ်ဝိဘတ် '{post}' တိုက်ရိုက်မလိုက်ရပါ — '{correct}' ဟု ရေးသားရမည်။",
                        $"နာမ်ပုဒ်တွဲ အမှန် ('{correct}')", "PART", true,
                        "'စသဖြင့်' တွင် အသုံးခံပြဝိဘတ် 'ဖြင့်' ပါဝင်ပြီးဖြစ်၍ ကြိယာဝိသေသနအဖြစ် ကြိယာရှေ့၌သာ သုံးနိုင်သည် — နောက်မှ 'တို့/များ/ကို/၌' စသော နာမ်ပစ္စည်း/နာမ်ဝိဘတ် ဆက်လိုပါက နာမ်သဘောရှိသော 'စသည်' ဖြင့်သာ တွဲဖက်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးသားနည်း");
                }
            }

            // 4k-5. Correlative 'အကယ်၍ ... သောကြောင့်/သဖြင့်' -> 'ပါက/လျှင်', & Dangling Causal Medial Fragment ('...သဖြင့်။')
            if (ContainsMm(text, mmMask, "အကယ်၍"))
            {
                foreach (Match m in HypotheticalAkaeYweWithCausalPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string p1 = styleMode == ProofingStyleMode.FormalOfficial ? "ပါက" : "လျှင်";
                    string p2 = styleMode == ProofingStyleMode.FormalOfficial ? "လျှင်" : "ပါက";
                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ရှေ့တွင် ခြွင်းချက်ပြ 'အကယ်၍' ပါရှိသဖြင့် အကြောင်းပြ '{g.Value}' အစား ခြွင်းချက်ပြသမ္ဗန္ဓ '{p1}' သို့မဟုတ် '{p2}' နှင့် တွဲဖက်ရမည်။",
                        GrammaticalReasoning = "မြန်မာသဒ္ဒါ အရှေ့အနောက် တွဲဖက်သမ္ဗန္ဓစည်းမျဉ်းအရ 'အကယ်၍' သည် ဖြစ်နိုင်ခြေ/ခြွင်းချက်ကိုပြသော ပုဒ်ဖြစ်ရာ နောက်ခံကြိယာကို 'လျှင် / ပါက / သော်' ဖြင့်သာ ပိတ်ရမည် ('သောကြောင့် / သဖြင့်' နှင့် မတွဲဖက်ရပါ)။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (သမ္ဗန္ဓပစ္စည်း တွဲဖက်ထုံး)",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = p1,
                                MeaningCue = $"ခြွင်းချက်ပြသမ္ဗန္ဓ ('အကယ်၍ ... {p1}')",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = p2,
                                MeaningCue = $"ခြွင်းချက်ပြသမ္ဗန္ဓ ('အကယ်၍ ... {p2}')",
                                PosTag = "CONJ",
                                IsOfficialThatpon = true,
                                Score = 96.0
                            }
                        }
                    });
                }
            }

            if (hasPaukMaGyi && hasSaThaPhint)
            {
                foreach (Match m in DanglingCausalMedialSentenceFragmentPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "သောကြောင့် ဖြစ်ပါသည်",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ဝါကျဆက်သမ္ဗန္ဓ 'သဖြင့်' ဖြင့် ဝါကျကို ပင်မကြိယာမပါဘဲ အဆုံးသတ်ထားပါသည် — 'သောကြောင့် ဖြစ်ပါသည်' ဟု ပြည့်စုံစွာ အဆုံးသတ်ရမည်။",
                        "ပြည့်စုံသော အကြောင်းပြဝါကျ အဆုံးသတ်", "CONJ", true,
                        "မြန်မာသဒ္ဒါ ဝါကျဖွဲ့ထုံးအရ 'သဖြင့်' သည် နောက်မှ ပင်မဝါကျ ဆက်လိုက်ရမည့် ဝါကျခွဲဆက်သမ္ဗန္ဓဖြစ်သောကြောင့် ၎င်းနောက်၌ ပုဒ်မကြီး ('။') တိုက်ရိုက်ချ၍ ဝါကျမပိတ်ရပါ။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ဝါကျဖွဲ့ထုံး နှင့် သမ္ဗန္ဓပစ္စည်း)");
                }
            }

            // 4k-6. Clause-Internal Register Disharmony (Universal: catches stark literary+colloquial clash even in GeneralStandard)
            if ((hasBu || ContainsMm(text, mmMask, "ပါတယ်") || ContainsMm(text, mmMask, "ပါမယ်")) &&
                (ContainsMm(text, mmMask, "၌") ||
                 hasSaThaPhint ||
                 ContainsMm(text, mmMask, "အလို့ငှာ") ||
                 (hasLaKaungTrigger && ContainsMm(text, mmMask, "၎င်း"))))
            {
                foreach (Match m in PureLiteraryMarkerWithColloquialSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string litFix = raw == "ပါတယ်" ? "ပါသည်"
                                  : raw == "ပါမယ်" ? "ပါမည်"
                                  : raw == "မဟုတ်ဘူး" ? "မဟုတ်ပါ"
                                  : "ပါ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, litFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ဝါကျအတွင်း စာပေသုံးပစ္စည်း ('၌ / သဖြင့် / အလို့ငှာ / ၎င်းသည်') နှင့် အပြောစကားအဆုံးသတ် '{raw}' ရောထွေးနေပါသည် — '{litFix}' ဟု ညီညွတ်စွာ သုံးနှုန်းရမည်။",
                        $"စာပေဟန် ညီညွတ်သော ကြိယာဝိဘတ် ('{litFix}')", "PART", true,
                        $"တစ်ဝါကျတည်းအတွင်း စာပေသီးသန့်ပစ္စည်းနှင့် အပြောစကားဝိဘတ် '{raw}' ရောနှောသုံးစွဲခြင်းသည် စကားအဆင့် (Register) မညီညွတ်သည့် သဒ္ဒါအမှားဖြစ်သဖြင့် '{litFix}' ဖြင့် ညှိနှိုင်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) — အရေး/အပြော ညီညွတ်မှုစည်းမျဉ်း");
                }
            }

            if (ContainsMm(text, mmMask, "ပေသည်") ||
                ContainsMm(text, mmMask, "ချေသည်") ||
                ContainsMm(text, mmMask, "ပါ၏") ||
                ContainsMm(text, mmMask, "သတည်း"))
            {
                foreach (Match m in ColloquialMarkerWithClassicalSfpPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string litFix = raw == "တွေ" ? "များ"
                                  : raw == "နဲ့" ? "နှင့်"
                                  : raw == "ဒီနေ့" ? "ယနေ့"
                                  : "ထို";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, litFix,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"စာပေအရေးအသားဝါကျ ('~ပေသည်/ချေသည်/ပါ၏') အတွင်း အပြောစကား '{raw}' ရောထွေးနေပါသည် — '{litFix}' ဟု သုံးနှုန်းရမည်။",
                        $"စာပေသုံး စံပုဒ် ('{litFix}')", "PART", true,
                        $"ဝါကျအဆုံး၌ စာပေဟန်မြင့် ကြိယာဝိဘတ် သုံးထားသဖြင့် ဝါကျအတွင်းရှိ အပြောစကား '{raw}' ကို စာပေသုံး '{litFix}' သို့ ပြောင်းလဲညှိနှိုင်းရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ)");
                }
            }

            // 4k-7. Monastic / Clergy Honorific Subject–Verb Concord
            if (ContainsMm(text, mmMask, "ဆရာတော်") ||
                ContainsMm(text, mmMask, "သံဃာတော်") ||
                ContainsMm(text, mmMask, "ရဟန်းတော်") ||
                ContainsMm(text, mmMask, "ဘုန်းတော်ကြီး"))
            {
                foreach (Match m in MonasticHonorificVerbConcordPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string subj = m.Groups["Subj"].Value;
                    string layVerb = g.Value;
                    string h1;
                    string h2;
                    switch (layVerb)
                    {
                        case "ထမင်းစား":
                            h1 = "ဆွမ်းဘုဉ်းပေး";
                            h2 = "ဆွမ်းသုံးဆောင်";
                            break;
                        case "သေဆုံး":
                        case "ကွယ်လွန်":
                            h1 = "ပျံလွန်တော်မူ";
                            h2 = "ဘဝနတ်ထံပျံလွန်တော်မူ";
                            break;
                        case "အိပ်စက်":
                        case "အိပ်ပျော်":
                            h1 = "ကျိန်းစက်တော်မူ";
                            h2 = "ကျိန်းစက်";
                            break;
                        default: // နေထိုင်
                            h1 = "သီတင်းသုံး";
                            h2 = "သီတင်းသုံးနေထိုင်";
                            break;
                    }

                    issues.Add(new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = layVerb,
                        IssueType = ProofingIssueType.OfficialStyleGrammarError,
                        Message = $"ရဟန်းသံဃာ ကတ္တားပုဒ် ('{subj}') နှင့် လူဝတ်ကြောင်သုံးကြိယာ '{layVerb}-' မကိုက်ညီပါ — ရဟန်းသုံး ဂါရဝပြုကြိယာ '{h1}' ကို သုံးနှုန်းရမည်။",
                        GrammaticalReasoning = $"မြန်မာသဒ္ဒါနှင့် ရဟန်းသံဃာသုံး ဝေါဟာရစည်းမျဉ်းအရ ကတ္တားသည် '{subj}' ဖြစ်သောအခါ သာမန်လူပုဂ္ဂိုလ်သုံး '{layVerb}-' အစား သာသနာ့ဝေါဟာရ '{h1}' သို့မဟုတ် '{h2}' ဖြင့် ကတ္တား-ကြိယာ အဆင့်အတန်း ညှိနှိုင်းရမည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံး/သာသနာရေး ဂါရဝပြုဝေါဟာရစံနှုန်း",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = h1,
                                MeaningCue = $"ရဟန်းသံဃာသုံး ဂါရဝပြုကြိယာ ('{h1}~')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 100.0
                            },
                            new SuggestionCandidate
                            {
                                Replacement = h2,
                                MeaningCue = $"ရဟန်းသုံး အခြားရွေးချယ်ရန်ပုဒ် ('{h2}~')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 95.0
                            }
                        }
                    });
                }
            }

            // 4k-8. Auxiliary Verb Particle Slot Inversion ('*ဆောင်ရွက်ပါနိုင်သည် -> ဆောင်ရွက်နိုင်ပါသည်', '*သိရှိစေပါလိုသည် -> သိရှိစေလိုပါသည်', '*ခဲ့နေသည် -> နေခဲ့သည်')
            if (ContainsMm(text, mmMask, "ပါနိုင်") ||
                ContainsMm(text, mmMask, "ပါချင်") ||
                ContainsMm(text, mmMask, "ပါလို") ||
                ContainsMm(text, mmMask, "ပါသင့်"))
            {
                foreach (Match m in InvertedPoliteModalParticlePattern.Matches(text))
                {
                    Group g = m.Groups["Target"].Success ? m.Groups["Target"] : m.Groups["Target2"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    string raw = g.Value;
                    string correct = m.Groups["Target"].Success
                        ? (m.Groups["Modal"].Value + "ပါ")
                        : "စေလိုပါ";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, raw, correct,
                        ProofingIssueType.OfficialStyleGrammarError,
                        $"ကြိယာထောက်ပစ္စည်း အစီအစဉ် လွဲမှားနေပါသည် — '{raw}' အစား '{correct}' ဟု ရှေ့နောက်အစီအစဉ်မှန်ဖြင့် ရေးသားရမည်။",
                        $"ကြိယာထောက်ပစ္စည်း အစီအစဉ်မှန် ('~{correct}')", "PART", true,
                        $"မြန်မာသဒ္ဒါ ကြိယာပစ္စည်းစဉ်ထုံးအရ ယဉ်ကျေးမှုပြပစ္စည်း 'ပါ' သည် စွမ်းဆောင်နိုင်မှု/အလိုဆန္ဒ/သင့်လျော်မှုပြ ကြိယာထောက်ပစ္စည်း ('နိုင်/ချင်/လို/သင့်') ၏ နောက်၌သာ လိုက်ရသဖြင့် '{correct}' ဟု စီစဉ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း အစီအစဉ်စည်းမျဉ်း)");
                }
            }

            if (hasKhae && ContainsMm(text, mmMask, "ခဲ့နေ"))
            {
                foreach (Match m in InvertedPastProgressiveParticlePattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, "နေခဲ့",
                        ProofingIssueType.OfficialStyleGrammarError,
                        "ကြိယာထောက်ပစ္စည်း အစီအစဉ် လွဲမှားနေပါသည် — 'ခဲ့နေ' အစား 'နေခဲ့' ('~နေခဲ့သည်') ဟု ရေးသားရမည်။",
                        "ဖြစ်ပေါ်ဆဲအတိတ်ပြ အစီအစဉ်မှန် ('~နေခဲ့')", "PART", true,
                        "မြန်မာသဒ္ဒါ ကြိယာထောက်ပစ္စည်းစဉ်ထုံးအရ ဖြစ်ဆဲပြ 'နေ' သည် အတိတ်ပြ 'ခဲ့' ရှေ့တွင် တည်ရှိရသဖြင့် 'နေခဲ့' ဟုသာ တွဲစပ်ရမည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကြိယာထောက်ပစ္စည်း အစီအစဉ်)");
                }
            }

            // 4h. Broken Space-Split Mandatory Compounds ('စာ ကြည့်တိုက်' -> 'စာကြည့်တိုက်', 'အစည်း အဝေး' -> 'အစည်းအဝေး')
            if (hasMyanmarSpaceMyanmar)
            {
                foreach (Match m3 in ThreePartBrokenCompoundPattern.Matches(text))
                {
                    string joined3 = UnicodeNormalizer.NormalizeCanonicalOrder(
                        m3.Groups["P1"].Value + m3.Groups["P2"].Value + m3.Groups["P3"].Value);
                    if (VerifiedMandatoryCompounds.Contains(joined3))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, m3.Index, m3.Length, m3.Value, joined3,
                            ProofingIssueType.SpellingError,
                            $"တစ်ပေါင်းတည်း ရေးသားရမည့် ပေါင်းစပ်ပုဒ် ဖြစ်ပါသည် — '{m3.Value}' အစား '{joined3}' ဟု ခြားမရေးဘဲ ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် ပေါင်းစပ်ပုဒ် (သတ်ပုံကျမ်း)", "N", true);
                    }
                }

                foreach (Match m in CuratedBrokenCompoundPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                    if (VerifiedMandatoryCompounds.Contains(joined))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, joined,
                            ProofingIssueType.SpellingError,
                            $"တစ်ပေါင်းတည်း ရေးသားရမည့် ပေါင်းစပ်ပုဒ် ဖြစ်ပါသည် — '{m.Value}' အစား '{joined}' ဟု ခြားမရေးဘဲ ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် ပေါင်းစပ်ပုဒ် (သတ်ပုံကျမ်း)", "N", true);
                    }
                }

                foreach (Match m in BoundStemBrokenCompoundPattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                    if (VerifiedMandatoryCompounds.Contains(joined))
                    {
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, joined,
                            ProofingIssueType.SpellingError,
                            $"တစ်ပေါင်းတည်း ရေးသားရမည့် ပေါင်းစပ်ပုဒ် ဖြစ်ပါသည် — '{m.Value}' အစား '{joined}' ဟု ခြားမရေးဘဲ ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် ပေါင်းစပ်ပုဒ် (သတ်ပုံကျမ်း)", "N", true);
                    }
                }

                if (ContainsMm(text, mmMask, "တော်မူ") ||
                    ContainsMm(text, mmMask, "မှာလား") ||
                    ContainsMm(text, mmMask, "ပေ ") || ContainsMm(text, mmMask, "ပေ\t") ||
                    ContainsMm(text, mmMask, "ခဲ့ ") || ContainsMm(text, mmMask, "ခဲ့\t") ||
                    ContainsMm(text, mmMask, "မှု ") || ContainsMm(text, mmMask, "မှု\t"))
                {
                    foreach (Match m in WildcardLeftBoundSuffixPattern.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, m.Index, m.Length))
                            continue;
                        string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, joined,
                            ProofingIssueType.SpellingError,
                            $"နောက်ဆက်ပစ္စည်းနှင့် ပင်မပုဒ်ကြား နေရာလွတ် (Space) မခြားရပါ — '{m.Value}' အစား '{joined}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            "တစ်ဆက်တည်းရေးရမည့် နောက်ဆက်တွဲပုဒ်", "PART", true);
                    }
                }

                foreach (Match m in BoundSuffixBrokenSpacePattern.Matches(text))
                {
                    if (OverlapsExistingIssue(issues, m.Index, m.Length))
                        continue;
                    string joined = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Left"].Value + m.Groups["Right"].Value);
                    AddGrammarOrSpellingIssue(
                        issues, m.Index, m.Length, m.Value, joined,
                        ProofingIssueType.SpellingError,
                        $"နောက်ဆက်ပစ္စည်းနှင့် ပင်မပုဒ်ကြား နေရာလွတ် (Space) မခြားရပါ — '{m.Value}' အစား '{joined}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                        "တစ်ဆက်တည်းရေးရမည့် နောက်ဆက်တွဲပုဒ်", "PART", true);
                }

                if (ContainsMm(text, mmMask, "အစိုးရ"))
                {
                    foreach (Match mGov in FormalCabinetBrokenCompoundPattern.Matches(text))
                    {
                        if (styleMode != ProofingStyleMode.FormalOfficial &&
                            !MatchesSliceAt(text, mGov.Index + mGov.Length, "ဆိုင်ရာ"))
                        {
                            continue;
                        }
                        if (!OverlapsExistingIssue(issues, mGov.Index, mGov.Length))
                        {
                            AddGrammarOrSpellingIssue(
                                issues, mGov.Index, mGov.Length, mGov.Value, "အစိုးရအဖွဲ့",
                                ProofingIssueType.SpellingError,
                                $"ရုံးသုံးပေါင်းစပ်ပုဒ် '{mGov.Value}' ကို 'အစိုးရအဖွဲ့' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                                "ရုံးသုံးပေါင်းစပ်ပုဒ် ('အစိုးရအဖွဲ့')", "N", true);
                        }
                    }
                }

                for (int cIdx = 0; cIdx < CuratedAddressAndHistoricalBrokenCompounds.Length; cIdx++)
                {
                    var (trigger, pat, targetJoined, cue) = CuratedAddressAndHistoricalBrokenCompounds[cIdx];
                    if (!ContainsMm(text, mmMask, trigger))
                        continue;
                    foreach (Match m in pat.Matches(text))
                    {
                        if (OverlapsExistingIssue(issues, m.Index, m.Length))
                            continue;
                        string normTarget = UnicodeNormalizer.NormalizeCanonicalOrder(targetJoined);
                        AddGrammarOrSpellingIssue(
                            issues, m.Index, m.Length, m.Value, normTarget,
                            ProofingIssueType.SpellingError,
                            $"စာလုံးအတွင်း နေရာလွတ် (Space) ခြားထားခြင်း သို့မဟုတ် စာလုံးပေါင်း လွဲမှားနေပါသည် — '{m.Value}' အစား '{normTarget}' ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            cue, "PROPN", true);
                    }
                }

                if (trie != null && hasShortMyanmarBeforeMyanmar)
                {
                    foreach (Match m in SpaceSplitPairCandidateRegex.Matches(text))
                    {
                        Group tGrp = m.Groups["Target"];
                        if (OverlapsExistingIssue(issues, tGrp.Index, tGrp.Length))
                            continue;

                        Group leftGrp = m.Groups["Left"];
                        if (text[leftGrp.Index] == 'မ')
                            continue;

                        string leftNorm = UnicodeNormalizer.NormalizeCanonicalOrder(leftGrp.Value);
                        if (SpaceSplitBoundaryParticles.Contains(leftNorm) || leftNorm.StartsWith("မ", StringComparison.Ordinal))
                            continue;

                        string rightNorm = UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Right"].Value);
                        if (SpaceSplitBoundaryParticles.Contains(rightNorm))
                            continue;

                        string suffixVal = m.Groups["Suffix"].Success
                            ? UnicodeNormalizer.NormalizeCanonicalOrder(m.Groups["Suffix"].Value)
                            : string.Empty;
                        if (suffixVal.Length > 0 &&
                            trie.ContainsExactWord(leftNorm) &&
                            trie.ContainsExactWord(rightNorm + suffixVal))
                        {
                            continue;
                        }

                        string rawJoined = leftNorm + rightNorm;
                        string joinedNorm = (rightNorm.Length > 0 && rightNorm[0] >= '\u102B' && rightNorm[0] <= '\u103E')
                            ? UnicodeNormalizer.NormalizeCanonicalOrder(rawJoined)
                            : rawJoined;
                        if (VerifiedMandatoryCompounds.Contains(joinedNorm))
                            continue;

                        // Check O(1) dictionary compound existence BEFORE breaking syllables!
                        if (!trie.TryGetExactEntries(joinedNorm, out var jEntries) ||
                            jEntries == null || jEntries.Count == 0 ||
                            (!jEntries[0].IsOfficialThatpon && !jEntries[0].IsProperNoun) ||
                            jEntries[0].Frequency < 20)
                        {
                            continue;
                        }

                        if (trie.ContainsExactWord(leftNorm) && trie.ContainsExactWord(rightNorm))
                            continue;

                        int leftSyls = MyanmarSyllableBreaker.BreakIntoSyllables(leftNorm, isPreNormalized: true).Count;
                        int rightSyls = MyanmarSyllableBreaker.BreakIntoSyllables(rightNorm, isPreNormalized: true).Count;
                        if (leftSyls == 0 || rightSyls == 0 || leftSyls > 2 || rightSyls > 2)
                            continue;

                        string cue = !string.IsNullOrWhiteSpace(jEntries[0].MeaningCue) ? jEntries[0].MeaningCue : "သတ်ပုံကျမ်းဝင် ပေါင်းစပ်ပုဒ်";
                        AddGrammarOrSpellingIssue(
                            issues, tGrp.Index, tGrp.Length, tGrp.Value, joinedNorm,
                            ProofingIssueType.SpellingError,
                            $"စာလုံးအတွင်း နေရာလွတ် (Space) မှားခြားထားပါသည် — '{tGrp.Value}' အစား '{joinedNorm}' [{cue}] ဟု တစ်ဆက်တည်း ရေးသားရမည်။",
                            cue, jEntries[0].PosTag, true);
                    }
                }
            }

            if (styleMode == ProofingStyleMode.FormalOfficial)
            {
                // Exempt colloquial register markers inside balanced verbatim quotation marks (“...”, "...", ‘...’) while keeping spelling/numeral rules active!
                List<(int Start, int End)>? balancedQuoteRanges = ComputeBalancedQuoteRanges(text);

                // 5. Colloquial-to-Literary (ရုံးသုံး / စာပေ အရေးအသား) Rules
                foreach (var rule in ColloquialRules)
                {
                    if (!ContainsMm(text, mmMask, rule.Trigger))
                        continue;

                    foreach (Match m in rule.Pattern.Matches(text))
                    {
                        if (IsInsideAnyRange(balancedQuoteRanges, m.Index, m.Length) ||
                            OverlapsExistingIssue(issues, m.Index, m.Length))
                        {
                            continue;
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = m.Index,
                            Length = m.Length,
                            OriginalText = m.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = rule.Explanation,
                            GrammaticalReasoning = $"ရုံးသုံးနှင့် စာပေအရေးအသားစနစ် (Formal/Official Mode) တွင် အပြောစကားပစ္စည်း '{m.Value}' အစား စာပေ/ရုံးသုံး စံပုဒ် '{rule.Replacement}' ဖြင့် စကားအဆင့် တစ်ပြေးညီ ထိန်းသိမ်းရမည်။",
                            AuthoritativeSource = "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = rule.Replacement,
                                    MeaningCue = rule.MeaningCue,
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(rule.Replacement),
                                    DiagnosticDetail = "အပြောစကားအသုံး ('" + m.Value + "') မှ စာပေ/ရုံးသုံး အရေးစကား ('" + rule.Replacement + "') သို့ ပြောင်းလဲရန်",
                                    Score = 98.0
                                }
                            }
                        });
                    }
                }

                // 5b. General Colloquial Negative ('မ...ဘူး / မ...ပါဘူး / နေမကောင်းဘူး -> ~ပါ') & Bare Polar Questions ('နေကောင်းလား -> နေကောင်းပါသလော') in Formal/Official Mode
                if (hasMa && hasBu)
                {
                    foreach (Match m in ColloquialNegativeBuInOfficialPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (IsInsideAnyRange(balancedQuoteRanges, g.Index, g.Length) ||
                            OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            continue;
                        }

                        string raw = g.Value;
                        string endTok = m.Groups["End"].Value;
                        string prefixBeforeEnd = raw.Substring(0, raw.Length - endTok.Length);
                        string primaryOfficial;
                        string? altOfficial = null;

                        if (raw == "မအားနေဘူး" || raw == "မအားနေပါဘူး")
                        {
                            primaryOfficial = "မအားလပ်ပါ";
                            altOfficial = "မအားပါ";
                        }
                        else if (raw == "မအားဘူး" || raw == "မအားပါဘူး")
                        {
                            primaryOfficial = "မအားပါ";
                            altOfficial = "မအားလပ်ပါ";
                        }
                        else
                        {
                            primaryOfficial = prefixBeforeEnd + "ပါ";
                        }

                        var cands = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = primaryOfficial,
                                MeaningCue = "ရုံးသုံး/စာပေ အငြင်းအရေးအသား ('မ...ပါ')",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primaryOfficial),
                                DiagnosticDetail = $"အပြောစကား '{raw}' မှ ရုံးသုံး/စာပေ '{primaryOfficial}' သို့ ပြောင်းလဲရန်",
                                Score = 98.5
                            }
                        };
                        if (!string.IsNullOrEmpty(altOfficial))
                        {
                            cands.Add(new SuggestionCandidate
                            {
                                Replacement = altOfficial!,
                                MeaningCue = "ရုံးသုံး/စာပေ အခြားရွေးချယ်ရန်ပုဒ်",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 96.0
                            });
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = raw,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား အငြင်းပုံစံ '{raw}' အစား '{primaryOfficial}' ကို သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ရုံးသုံး/စာပေ အရေးစကားတွင် အငြင်းကြိယာကို အပြောစကားပိတ်ပစ္စည်း 'ဘူး/ပါဘူး' အစား 'ပါ' ('{primaryOfficial}') ဖြင့် ယဉ်ကျေးသပ်ရပ်စွာ အဆုံးသတ်ရမည်။",
                            AuthoritativeSource = "ရုံးသုံးမြန်မာစာ လမ်းညွှန် နှင့် မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ",
                            Suggestions = cands
                        });
                    }
                }

                if (hasVisarga && ContainsMm(text, mmMask, "လား"))
                {
                    foreach (Match m in ColloquialBareVerbLarInOfficialPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        if (IsInsideAnyRange(balancedQuoteRanges, g.Index, g.Length) ||
                            OverlapsExistingIssue(issues, g.Index, g.Length))
                        {
                            continue;
                        }

                        string verb = m.Groups["Verb"].Value;
                        string primaryQ = verb.EndsWith("ပြီ", StringComparison.Ordinal)
                            ? (verb + "လော")
                            : (verb + "ပါသလော");

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = g.Index,
                            Length = g.Length,
                            OriginalText = g.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"ရုံးသုံးနှင့် စာပေအရေးအသားတွင် အပြောစကား အမေးပုံစံ '{g.Value}' အစား '{primaryQ}' ကို သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"ရုံးသုံး/စာပေ အရေးစကားတွင် ဟုတ်/မဟုတ် အမေးဝိဘတ် 'လား' အစား စာပေသုံး 'ပါသလော / သလော' ('{primaryQ}') ကို သုံးနှုန်းရမည်။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (အမေးဝိဘတ် 'သလော / လော')",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = primaryQ,
                                    MeaningCue = "ရုံးသုံး/စာပေ အမေးအရေးအသား ('~သလော')",
                                    PosTag = "V",
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(primaryQ),
                                    Score = 98.0
                                }
                            }
                        });
                    }
                }

                // 6a. 'အား' vs 'ကို' particle misuse (with Maung Khin Min's Double-'ကို', 'အဖြစ်', & Causative '-စေ' Clause-Level Exemption!)
                if (hasAh)
                {
                    foreach (Match m in AhInsteadOfKoPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        if (OverlapsExistingIssue(issues, pGroup.Index, pGroup.Length))
                            continue;
                        string verb = m.Groups["Verb"].Value;

                        // Exemption reasoning: If another direct-object postposition 'ကို', role marker 'အဖြစ်' (Gazette appointment formula),
                        // or causative '-စေ' exists within the SAME predicate clause (bounded by ၊ / ။ / newline AND sequential clause-chaining markers ပြီး / ၍ / သဖြင့် / ကာ), 'အား' is grammatically licensed!
                        int winStart = pGroup.Index;
                        int minBound = Math.Max(0, pGroup.Index - 120);
                        while (winStart > minBound)
                        {
                            char ch = text[winStart - 1];
                            if (ch == '\u104A' || ch == '\u104B' || ch == '\r' || ch == '\n')
                                break;
                            winStart--;
                        }

                        int verbEnd = m.Index + m.Length;
                        int winEnd = verbEnd;
                        int maxBound = Math.Min(text.Length, verbEnd + 80);
                        while (winEnd < maxBound)
                        {
                            char ch = text[winEnd];
                            if (ch == '\u104A' || ch == '\u104B' || ch == '\r' || ch == '\n')
                                break;
                            winEnd++;
                        }

                        string leftClause = text.Substring(winStart, pGroup.Index - winStart);
                        MatchCollection leftSplits = ClauseChainingConjunctionRegex.Matches(leftClause);
                        if (leftSplits.Count > 0)
                        {
                            Match lastSplit = leftSplits[leftSplits.Count - 1];
                            leftClause = leftClause.Substring(lastSplit.Index + lastSplit.Length);
                        }

                        string rightClause = text.Substring(verbEnd, winEnd - verbEnd);
                        Match rightSplit = ClauseChainingConjunctionRegex.Match(rightClause);
                        if (rightSplit.Success)
                        {
                            rightClause = rightClause.Substring(0, rightSplit.Index);
                        }

                        string clauseWindow = leftClause + m.Value + rightClause;
                        if (clauseWindow.IndexOf("စေ", StringComparison.Ordinal) >= 0 ||
                            clauseWindow.IndexOf("အဖြစ်", StringComparison.Ordinal) >= 0 ||
                            LicensingKoParticleInClauseRegex.IsMatch(clauseWindow))
                        {
                            continue;
                        }

                        issues.Add(new ProofingIssue
                        {
                            StartIndex = pGroup.Index,
                            Length = pGroup.Length,
                            OriginalText = pGroup.Value,
                            IssueType = ProofingIssueType.OfficialStyleGrammarError,
                            Message = $"'{verb}' သည် တိုက်ရိုက်ကံပုဒ်လို ကြိယာဖြစ်သဖြင့် လက်ခံပြဝိဘတ် 'အား' အစား ကံဝိဘတ် 'ကို' သုံးနှုန်းရမည်။",
                            GrammaticalReasoning = $"မြန်မာသဒ္ဒါအရ 'အား' သည် ပေးကမ်း/ချီးမြှင့်ခြင်းကို လက်ခံရယူသူပြ လက်ခံပြဝိဘတ်ဖြစ်ပြီး '{verb}' မှာ တိုက်ရိုက်ပြုလုပ်ခံရသော ကံပုဒ်လိုကြိယာဖြစ်၍ ကံဝိဘတ် 'ကို' သုံးရမည် (ချွင်းချက်: ဝါကျအတွင်း အခြား 'ကို' သို့မဟုတ် စေခိုင်းပြ 'စေ' ပါရှိမှသာ 'အား' သုံးနိုင်သည်)။",
                            AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ကံဝိဘတ် 'ကို' နှင့် လက်ခံပြဝိဘတ် 'အား') နှင့် မောင်ခင်မင်(ဓနုဖြူ)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = "ကို",
                                    MeaningCue = $"ကံဝိဘတ် (~{verb})",
                                    PosTag = "PART",
                                    IsOfficialThatpon = true,
                                    DiagnosticDetail = $"ကံပုဒ်လိုကြိယာ '{verb}' ရှေ့တွင် ကံဝိဘတ် 'ကို' သုံးရမည်",
                                    Score = 99.0
                                }
                            }
                        });
                    }
                }

                // 6b. 'နှင့်' vs 'ဖြင့်' instrumental postposition in Formal/Official style
                if (hasCreakyDot && ContainsMm(text, mmMask, "နှင့်"))
                {
                    foreach (Match m in InstrumentalNhitInsteadOfPhintPattern.Matches(text))
                    {
                        Group pGroup = m.Groups["Particle"];
                        string verb = m.Groups["Verb"].Value;
                        AddGrammarOrSpellingIssue(
                            issues, pGroup.Index, pGroup.Length, pGroup.Value, "ဖြင့်",
                            ProofingIssueType.OfficialStyleGrammarError,
                            $"ကိရိယာ/နည်းလမ်းကို အသုံးပြု၍ '{verb}-' ရာတွင် ယှဉ်တွဲပြဝိဘတ် 'နှင့်' အစား အသုံးခံပြဝိဘတ် 'ဖြင့်' ကို သုံးနှုန်းရမည်။",
                            $"အသုံးခံပြဝိဘတ် (~ဖြင့် {verb})", "PART", true,
                            "မြန်မာသဒ္ဒါနှင့် ရုံးသုံးမြန်မာစာအရ 'နှင့်' သည် အဖော်ပြု/ပေါင်းစပ်ခြင်းပြ ယှဉ်တွဲပြဝိဘတ်ဖြစ်ပြီး၊ ကိရိယာတန်ဆာပလာ သို့မဟုတ် အထောက်အထားကို အသုံးပြု၍ ဆောင်ရွက်ကြောင်းပြရာ၌ အသုံးခံပြဝိဘတ် 'ဖြင့်' ကို သုံးရမည်။",
                            "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် မောင်ခင်မင်(ဓနုဖြူ) — 'နှင့်' နှင့် 'ဖြင့်'");
                    }
                }

                // 6d. Redundant stacked postpositions 'အတွက်ကို -> အတွက်' (ဝိဘတ်နှစ်ထပ်)
                if (hasKo && ContainsMm(text, mmMask, "အတွက်ကို"))
                {
                    foreach (Match m in RedundantAtwetKoPattern.Matches(text))
                    {
                        Group g = m.Groups["Target"];
                        AddGrammarOrSpellingIssue(
                            issues, g.Index, g.Length, g.Value, "အတွက်",
                            ProofingIssueType.OfficialStyleGrammarError,
                            "စာပေနှင့် ရုံးသုံးမြန်မာစာအရ ရည်စူးချက်ပြပုဒ် 'အတွက်' နောက်၌ ကံဝိဘတ် 'ကို' နှစ်ထပ်မထည့်ဘဲ 'အတွက်' ဟုသာ ရေးသားရမည်။",
                            "ရုံးသုံးသဒ္ဒါ စံနှုန်း ('အတွက်')", "PART", true,
                            "ရည်ရွယ်ချက်/ရည်စူးချက်ပြ ဝိဘတ် 'အတွက်' သည် ဝါကျအတွင်း ဆက်စပ်မှု ပြည့်စုံပြီးဖြစ်၍ နောက်မှ ကံဝိဘတ် 'ကို' ထပ်ဆင့်တွဲရန် မလိုအပ်ပါ (ဝိဘတ်နှစ်ထပ်အမှား)။",
                            "ရုံးသုံးမြန်မာစာ နှင့် မှားတတ်သော မြန်မာစာအသုံးအနှုန်းများ");
                    }
                }

                // (Note: 6c Agentive/Host 'မှ -> က' now runs universally in Step 4 across both GeneralStandard and FormalOfficial modes)
            }

            // 7. Duplicated Punctuation (ပုဒ်ဖြတ်ပုဒ်ရပ် — including '၊၊' typed twice instead of '။' on list numbers / sentence endings)
            if (hasConsecutiveMyanmarPunct &&
                (text.IndexOf("\u104A\u104A", StringComparison.Ordinal) >= 0 ||
                 text.IndexOf("\u104B\u104B", StringComparison.Ordinal) >= 0 ||
                 text.IndexOf("\u104A\u104B", StringComparison.Ordinal) >= 0))
            {
                foreach (Match m in DuplicatePunctuationPattern.Matches(text))
            {
                var puncSuggestions = new List<SuggestionCandidate>(2);
                if (m.Value.Contains("\u104B"))
                {
                    puncSuggestions.Add(new SuggestionCandidate
                    {
                        Replacement = "\u104B",
                        MeaningCue = "ပုဒ်မကြီး ('။' — ဝါကျအဆုံး)",
                        Score = 100.0
                    });
                }
                else
                {
                    int lookStart = Math.Max(0, m.Index - 24);
                    string beforePunc = text.Substring(lookStart, m.Index - lookStart);
                    int afterIdx = m.Index + m.Length;
                    string afterPunc = afterIdx < text.Length
                        ? text.Substring(afterIdx, Math.Min(16, text.Length - afterIdx))
                        : string.Empty;
                    bool insideUnclosedParens = beforePunc.LastIndexOf('(') > beforePunc.LastIndexOf(')');
                    bool followedByDigit = Regex.IsMatch(afterPunc, @"^\s*[\u1040-\u10490-9]");
                    bool precededByCommaListDigit = Regex.IsMatch(beforePunc, @"[\u104A,]\s*[\u1040-\u10490-9]+\s*$");
                    bool preferPaukMaGyi = !insideUnclosedParens && !followedByDigit && !precededByCommaListDigit &&
                        SentenceOrItemBeforeDoublePaukKaleRegex.IsMatch(beforePunc);
                    if (preferPaukMaGyi)
                    {
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104B",
                            MeaningCue = "ပုဒ်မကြီး ('။' — ဝါကျအဆုံး/အမှတ်စဉ်)",
                            Score = 100.0
                        });
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104A",
                            MeaningCue = "ပုဒ်ကလေး ('၊' — ပုဒ်ဖြတ်)",
                            Score = 94.0
                        });
                    }
                    else
                    {
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104A",
                            MeaningCue = "ပုဒ်ကလေး ('၊' — ပုဒ်ဖြတ်)",
                            Score = 100.0
                        });
                        puncSuggestions.Add(new SuggestionCandidate
                        {
                            Replacement = "\u104B",
                            MeaningCue = "ပုဒ်မကြီး ('။' — ဝါကျအဆုံး)",
                            Score = 94.0
                        });
                    }
                }

                issues.Add(new ProofingIssue
                {
                    StartIndex = m.Index,
                    Length = m.Length,
                    OriginalText = m.Value,
                    IssueType = ProofingIssueType.PunctuationError,
                    Message = m.Value == "\u104A\u104A"
                        ? "ပုဒ်ကလေး ('၊') နှစ်ထပ်ဖြစ်နေပါသည် — ပုဒ်မကြီး ('။') သို့မဟုတ် ပုဒ်ကလေး ('၊') တစ်ခုကို ရွေးချယ်ပါ။"
                        : "ပုဒ်ဖြတ်ပုဒ်ရပ် သင်္ကေတ နှစ်ထပ်ဖြစ်နေပါသည်။",
                    GrammaticalReasoning = "မြန်မာစာ ပုဒ်ဖြတ်ပုဒ်ရပ်စနစ်တွင် ပုဒ်ကလေး ('၊' U+104A) နှစ်ချက်ရိုက်၍ ပုဒ်မကြီး ('။' U+104B) အစားထိုးခြင်း မပြုရပါ။",
                    AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ပုဒ်ဖြတ်ပုဒ်ရပ်)",
                    Suggestions = puncSuggestions
                });
                }
            }

            // 7b. V42 Unclosed & Mismatched Brackets / Smart Quotations (LanguageTool-grade structural check, Myanmar paragraphs only)
            if (mmMask != 0 && hasBracketOrSmartQuote)
            {
                AnalyzeUnpairedBracketsAndQuotes(text, issues);
            }

            // 7c. V42 Accidental Space Before Myanmar Punctuation ('၊' / '။' — exempts classical Pali Nissaya '<Pali> ၊ <Gloss>')
            if (hasSpaceBeforeMyanmarPunct)
            {
                foreach (Match m in AccidentalSpaceBeforeMyanmarPunctPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExistingIssue(issues, g.Index, g.Length))
                        continue;

                    string punct = m.Groups["Punct"].Value;
                    if (punct == "\u104A" && IsClassicalPaliNissayaHeadBeforeComma(text, g.Index, trie))
                        continue;

                    string punctName = punct == "\u104B" ? "ပုဒ်မကြီး ('။')" : "ပုဒ်ကလေး ('၊')";
                    AddGrammarOrSpellingIssue(
                        issues, g.Index, g.Length, g.Value, punct,
                        ProofingIssueType.PunctuationError,
                        $"{punctName} ရှေ့တွင် Space မခြားရပါ — စာလုံးနှင့်ကပ်၍ '{punct}' ဟု ရေးသားရမည်။",
                        $"{punctName} ရှေ့ Space ဖယ်ရှားရန်", "PUNCT", true,
                        $"မြန်မာစာ ပုဒ်ဖြတ်ပုဒ်ရပ်စံနှုန်းအရ {punctName} ကို ရှေ့စာလုံးနှင့် တစ်ဆက်တည်းကပ်၍ ရိုက်နှိပ်ရပြီး ပုဒ်ဖြတ်ပုဒ်ရပ်၏ နောက်၌သာ Space ခြားရသည်။",
                        "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ နှင့် ရုံးသုံးမြန်မာစာရေးထုံး");
                }
            }

            // 7d. V42 Smart Bilingual Numeral Consistency in Formal/Official Mode (flags pure-Burmese ASCII numerals, shields English-linked numbers)
            if (styleMode == ProofingStyleMode.FormalOfficial && mmMask != 0 && hasAsciiDigit && (trie == null || !IsCleanCanonicalStandaloneNrc(text, trie)))
            {
                var quoteSpansForNum = ComputeBalancedQuoteRanges(text);
                foreach (Match m in PureBurmeseAsciiNumeralPattern.Matches(text))
                {
                    Group numGroup = m.Groups["Num"];
                    if (OverlapsExistingIssue(issues, numGroup.Index, numGroup.Length) ||
                        IsInsideAnyRange(quoteSpansForNum, numGroup.Index, numGroup.Length))
                    {
                        continue;
                    }

                    if (IsPrecededByEnglishOrTechnicalToken(text, numGroup.Index))
                        continue;

                    string unit = m.Groups["Unit"].Value;
                    string mmDigits = ConvertAsciiDigitsToMyanmar(numGroup.Value);
                    AddGrammarOrSpellingIssue(
                        issues, numGroup.Index, numGroup.Length, numGroup.Value, mmDigits,
                        ProofingIssueType.NumeralClassifierError,
                        $"ရုံးသုံး/စာပေ မြန်မာစာအရေးအသားတွင် မြန်မာမျိုးပြပစ္စည်း/အချိန်ပြပုဒ် ('{unit}') ရှေ့ရှိ ကိန်းဂဏန်းကို မြန်မာဂဏန်း '{mmDigits}' ဖြင့် တစ်ပြေးညီ ရေးသားသင့်ပါသည်။",
                        $"မြန်မာဂဏန်း ('{mmDigits} {unit}')", "NUM", true,
                        $"မြန်မာဝါကျသက်သက်အတွင်း အင်္ဂလိပ်ဂဏန်း ('{numGroup.Value}') နှင့် မြန်မာစာ ('{unit}') ရောနှောနေသဖြင့် ရုံးသုံးမြန်မာစာစံနှုန်းအရ မြန်မာဂဏန်း '{mmDigits}' သုံးသင့်ပါသည် (အင်္ဂလိပ်အမှတ်တံဆိပ်/ယူနစ်တွဲများကို အလိုအလျောက် ချန်လှပ်ပေးထားသည်)။",
                        "ရုံးသုံးမြန်မာစာရေးသားနည်း စံနှုန်း",
                        requiresReviewBeforeBatchFix: true);
                }
            }

            return issues;
        }

    }
}
