// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MyanmarSpellChecker.Core.Models;

namespace MyanmarSpellChecker.Core.Nlp
{
    public sealed partial class ContextDisambiguator
    {
        private static readonly HashSet<string> WaterAndLiquidPrefixes = CreateNormalizedSet(
            "သောက်", "သုံး", "ချိုး", "မြစ်", "ချောင်း", "ကန်", "ပင်လယ်", "မိုး", "တွင်း", "ဆေး", "မျက်",
            "ချွေး", "သွေး", "ပန်း", "သံပရာ", "လက်ဖက်", "ကြံ", "ထန်း", "အုန်း", "ဆား", "ရေခဲ", "စွပ်ပြုတ်", "ဟင်း"
        );

        private static readonly HashSet<string> ExcludedLeadingSingleSylForCreaky = CreateNormalizedSet(
            "အ", "မ", "တ", "သ", "က", "စ", "ပ", "န", "လ", "သူ", "ငါ", "မင်း", "လူ", "မြေ", "ရေ", "နေ", "စာ", "သား", "မိ", "ဖ"
        );

        private static readonly HashSet<string> ExcludedLeadingSingleSylForVisarga = CreateNormalizedSet(
            "အ", "မ", "တ", "သူ", "ငါ", "နင်", "သင်", "ဒါ", "ဒီ", "ဟို", "ဘာ", "ဘယ်", "မည်", "ဤ", "ထို",
            "ပို", "စာ", "ရေ", "လေ", "နေ", "လူ", "အိမ်", "လက်", "ခြေ", "ဆံ", "သွား", "လာ", "စား", "ပေး", "ယူ",
            "က", "ကို", "မှာ", "တွင်", "ကာ", "သာ", "ပါ", "နာ", "သေ", "ရှာ", "ရင်", "လျှင်", "ဝင်", "ထင်", "မြင်", "သင်", "ပြန်", "မှန်", "ကျန်"
        );

        private static readonly Regex MergedSubjectKaSarPattern = new Regex(
            @"(?:^|[\u104A\u104B]\s*)(?=[^\u104A\u104B\r\n]*?(?:ထမင်း|မနက်စာ|နံနက်စာ|နေ့လယ်စာ|ညစာ|ညနေစာ|ဟင်း|မုန့်|မုန့်|ခေါက်ဆွဲ|အစားအစာ|အစာ|သစ်သီး|လက်ဖက်သုပ်|ဗိုက်ဝ))[^\u104A\u104B\r\n]{0,48}?(?<Subj>သူ|ကျွန်တော်|ကျွန်မ|ငါ|မင်း|ဆရာ|အမေ|အဖေ|ဧည့်သည်|ကလေး)(?<Target>ကစား)(?=(?:သည်|တယ်|ခဲ့|ပြီး|လိုက်|သောကြောင့်|သဖြင့်|လို့|ပါ|နေ))",
            RegexOptions.Compiled);

        private static readonly Regex MergedSubjectKaPyaPattern = new Regex(
            @"(?<![\u1000-\u103F])(?<Subj>ဆရာ|ဆရာမ|ဝန်ထမ်း|အင်ဂျင်နီယာ|ဆရာဝန်|သူ|ကျွန်တော်|ကျွန်မ)(?<Target>ကပြ)(?=(?:သည့်|သော|တဲ့)\s*(?:နည်းလမ်း|ပုံစံ|စာအုပ်|လမ်းညွှန်|အချက်))",
            RegexOptions.Compiled);

        private static void ScanMergedParticleVerbAmbiguities(
            string text,
            List<ProofingIssue> existingIssues,
            List<ProofingIssue> issues)
        {
            if (text.IndexOf("ကစား", StringComparison.Ordinal) >= 0)
            {
                foreach (Match m in MergedSubjectKaSarPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExisting(existingIssues, g.Index, g.Length) || OverlapsExisting(issues, g.Index, g.Length))
                        continue;

                    AddNonOverlapping(issues, new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.ContextualMeaningWarning,
                        RequiresReviewBeforeBatchFix = true,
                        Message = "အနက်သံသယ စိစစ်ရန် — အစားအစာ စားသုံးသည့် စကားစပ်အရ 'ကစား' (ဆော့ကစားသည်) အစား ကတ္တားဝိဘတ်နှင့် ကြိယာကို ခြား၍ 'က စား' ဟု ရည်ရွယ်ခြင်း ဖြစ်နိုင်ပါသည်။",
                        GrammaticalReasoning = $"ရှေ့ကတ္တား '{m.Groups["Subj"].Value}' နောက်ရှိ 'က' ဝိဘတ်နှင့် 'စား' ကြိယာ ပူးကပ်သွားသဖြင့် 'ကစား' (Play) ဟူသော အခြားဝေါဟာရနှင့် ရောထွေးနေပါသည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ပုဒ်အဖြတ်အတောက် နှင့် ကတ္တားဝိဘတ် 'က')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = "က စား",
                                MeaningCue = "ကတ္တားဝိဘတ် 'က' + ကြိယာ 'စား' (စားသုံးသည်)",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 94.0
                            }
                        }
                    });
                }
            }

            if (text.IndexOf("ကပြ", StringComparison.Ordinal) >= 0)
            {
                foreach (Match m in MergedSubjectKaPyaPattern.Matches(text))
                {
                    Group g = m.Groups["Target"];
                    if (OverlapsExisting(existingIssues, g.Index, g.Length) || OverlapsExisting(issues, g.Index, g.Length))
                        continue;

                    AddNonOverlapping(issues, new ProofingIssue
                    {
                        StartIndex = g.Index,
                        Length = g.Length,
                        OriginalText = g.Value,
                        IssueType = ProofingIssueType.ContextualMeaningWarning,
                        RequiresReviewBeforeBatchFix = true,
                        Message = "အနက်သံသယ စိစစ်ရန် — လမ်းညွှန်ပြသသည့် စကားစပ်အရ 'ကပြ' (ဇာတ်သဘင် ကပြသည်) အစား ကတ္တားဝိဘတ်နှင့် ကြိယာကို ခြား၍ 'က ပြ' ဟု ရည်ရွယ်ခြင်း ဖြစ်နိုင်ပါသည်။",
                        GrammaticalReasoning = $"ရှေ့ကတ္တား '{m.Groups["Subj"].Value}' နောက်ရှိ 'က' ဝိဘတ်နှင့် 'ပြ' ကြိယာ ပူးကပ်သွားသဖြင့် 'ကပြ' (ဇာတ်ကပြခြင်း) နှင့် အနက်ရောထွေးနေပါသည်။",
                        AuthoritativeSource = "မြန်မာစာအဖွဲ့ မြန်မာသဒ္ဒါ (ပုဒ်အဖြတ်အတောက် နှင့် ကတ္တားဝိဘတ် 'က')",
                        Suggestions = new List<SuggestionCandidate>
                        {
                            new SuggestionCandidate
                            {
                                Replacement = "က ပြ",
                                MeaningCue = "ကတ္တားဝိဘတ် 'က' + ကြိယာ 'ပြ' (ပြသသည်)",
                                PosTag = "V",
                                IsOfficialThatpon = true,
                                Score = 94.0
                            }
                        }
                    });
                }
            }
        }

        private static void ScanHiddenCompoundAndToneNearMisses(
            string text,
            MyanmarTrie trie,
            List<ClauseWordToken> wordTokens,
            List<ProofingIssue> existingIssues,
            List<ProofingIssue> issues)
        {
            int n = wordTokens.Count;
            if (n < 2)
                return;

            // 0. Generalized Contiguous-Chunk Nominalizer '-မူ -> -မှု' Pass (2..6 syllables before 'မူ', e.g. 'ပြေလည်မူ', 'ငွေပေးချေမူ', 'သင်ကြားပို့ချမူကို')
            for (int mIdx = 1; mIdx < n; mIdx++)
            {
                ClauseWordToken muTok = wordTokens[mIdx];
                if (muTok.NormText != "မူ" ||
                    muTok.IsProtectedName ||
                    wordTokens[mIdx - 1].EndIndex != muTok.StartIndex ||
                    OverlapsExisting(existingIssues, muTok.StartIndex, muTok.Length) ||
                    OverlapsExisting(issues, muTok.StartIndex, muTok.Length))
                {
                    continue;
                }

                if (mIdx + 1 < n && muTok.EndIndex == wordTokens[mIdx + 1].StartIndex)
                {
                    string nextGlued = wordTokens[mIdx + 1].NormText;
                    bool allowedNominalPostpos =
                        GenericSyntacticParticles.Contains(nextGlued) ||
                        ProtectedFunctionWords.Contains(nextGlued) ||
                        nextGlued == "ဆိုင်ရာ" || nextGlued == "အပေါ်" || nextGlued == "အောက်" ||
                        nextGlued == "အရ" || nextGlued == "အတွက်";
                    if (!allowedNominalPostpos || trie.ContainsExactWord("မူ" + nextGlued))
                    {
                        continue;
                    }
                }

                int startK = mIdx - 1;
                int leftSyls = 0;
                while (startK >= 0)
                {
                    ClauseWordToken prev = wordTokens[startK];
                    if (prev.IsProtectedName ||
                        (startK < mIdx - 1 && prev.EndIndex != wordTokens[startK + 1].StartIndex) ||
                        GenericSyntacticParticles.Contains(prev.NormText) ||
                        OverlapsExisting(existingIssues, prev.StartIndex, prev.Length) ||
                        OverlapsExisting(issues, prev.StartIndex, prev.Length) ||
                        leftSyls + prev.SyllableCount > 6)
                    {
                        break;
                    }
                    leftSyls += prev.SyllableCount;
                    startK--;
                }
                startK++;

                if (leftSyls >= 2 && startK <= mIdx - 1)
                {
                    var sbLeft = new System.Text.StringBuilder(24);
                    for (int t = startK; t < mIdx; t++)
                        sbLeft.Append(wordTokens[t].NormText);
                    string fullLeftNorm = sbLeft.ToString();

                    if (!trie.ContainsExactWord(fullLeftNorm + "မူ") &&
                        !fullLeftNorm.EndsWith("တော်", StringComparison.Ordinal) &&
                        !fullLeftNorm.EndsWith("အ", StringComparison.Ordinal) &&
                        !fullLeftNorm.EndsWith("ပါ", StringComparison.Ordinal) &&
                        !fullLeftNorm.EndsWith("သော်", StringComparison.Ordinal) &&
                        !fullLeftNorm.EndsWith("စေကာ", StringComparison.Ordinal) &&
                        !fullLeftNorm.EndsWith("ရာ", StringComparison.Ordinal) &&
                        !fullLeftNorm.EndsWith("တွင်", StringComparison.Ordinal))
                    {
                        ClauseWordToken immPrev = wordTokens[mIdx - 1];
                        string tailTwoTokNorm = mIdx - 2 >= startK
                            ? (wordTokens[mIdx - 2].NormText + immPrev.NormText)
                            : immPrev.NormText;

                        var leftSylList = MyanmarSyllableBreaker.BreakIntoSyllables(fullLeftNorm, isPreNormalized: true);
                        string lastTwoSylNorm = leftSylList.Count >= 2
                            ? (leftSylList[leftSylList.Count - 2] + leftSylList[leftSylList.Count - 1])
                            : fullLeftNorm;
                        string lastOneSylNorm = leftSylList.Count >= 1
                            ? leftSylList[leftSylList.Count - 1]
                            : string.Empty;

                        bool confirmedMhuNominalization =
                            trie.ContainsExactWord(fullLeftNorm + "မှု") ||
                            (immPrev.SyllableCount >= 2 && (trie.ContainsExactWord(immPrev.NormText + "မှု") || trie.HasPosTag(immPrev.NormText, "V"))) ||
                            (tailTwoTokNorm.Length >= 4 && (trie.ContainsExactWord(tailTwoTokNorm + "မှု") || (trie.ContainsExactWord(tailTwoTokNorm) && trie.HasPosTag(tailTwoTokNorm, "V")))) ||
                            (leftSylList.Count >= 2 && (trie.ContainsExactWord(lastTwoSylNorm + "မှု") || (trie.ContainsExactWord(lastTwoSylNorm) && trie.HasPosTag(lastTwoSylNorm, "V")))) ||
                            (leftSylList.Count >= 3 && lastOneSylNorm.Length >= 2 && trie.HasPosTag(lastOneSylNorm, "V"));

                        if (confirmedMhuNominalization)
                        {
                            int spanStart = wordTokens[startK].StartIndex;
                            int spanLen = muTok.EndIndex - spanStart;
                            string rawSpan = text.Substring(spanStart, spanLen);
                            string targetMhu = fullLeftNorm + "မှု";
                            AddNonOverlapping(issues, new ProofingIssue
                            {
                                StartIndex = spanStart,
                                Length = spanLen,
                                OriginalText = rawSpan,
                                IssueType = ProofingIssueType.SpellingError,
                                RequiresReviewBeforeBatchFix = false,
                                Message = $"ကြိယာနာမ်ပြုပစ္စည်း စာလုံးပေါင်းအမှန် — '{rawSpan}' အစား '{targetMhu}' (ဟထိုး တစ်ချောင်းငင် '~မှု') ဟု ရေးသားရမည်။",
                                GrammaticalReasoning = $"ရှေ့ကြိယာပေါင်းစပ်ပုဒ် '{fullLeftNorm}' ကို နာမ်ပြောင်းရာတွင် ဟထိုးပါသော 'မှု' ပစ္စည်းကိုသာ သုံးရမည် ('မူ' သည် အပြုအမူ/မူဝါဒ/တော်မူ ၌သာ သုံးသည်)။",
                                AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း နှင့် မြန်မာသဒ္ဒါ (ကြိယာနာမ်ပြုပစ္စည်း 'မှု')",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = targetMhu,
                                        MeaningCue = "ကြိယာနာမ်ပြုပစ္စည်း ('~မှု')",
                                        PosTag = "N",
                                        IsOfficialThatpon = true,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(targetMhu),
                                        DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawSpan, targetMhu),
                                        Score = 100.0
                                    }
                                }
                            });
                        }
                    }
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                ClauseWordToken w1 = wordTokens[i];
                if (w1.IsProtectedName ||
                    OverlapsExisting(existingIssues, w1.StartIndex, w1.Length) ||
                    OverlapsExisting(issues, w1.StartIndex, w1.Length))
                {
                    continue;
                }

                bool w1IsNominalPrefixA = w1.NormText == "အ";
                if (!w1IsNominalPrefixA &&
                    (ProtectedFunctionWords.Contains(w1.NormText) || GenericSyntacticParticles.Contains(w1.NormText)))
                {
                    continue;
                }

                // Try 3-token 'X + အ + Y' window first (e.g. 'အစည်း + အ + ဝေ -> အစည်းအဝေး', 'အချက် + အ + လတ် -> အချက်အလက်'), then 2-token 'w1 + w2'
                for (int winTokens = 3; winTokens >= 2; winTokens--)
                {
                    if (i + winTokens > n)
                        continue;

                    ClauseWordToken wLast = wordTokens[i + winTokens - 1];
                    if (winTokens == 3)
                    {
                        ClauseWordToken wMid = wordTokens[i + 1];
                        if ((wMid.NormText != "အ" && wLast.NormText != "မူ") ||
                            w1.EndIndex != wMid.StartIndex ||
                            wMid.EndIndex != wLast.StartIndex ||
                            wMid.IsProtectedName ||
                            OverlapsExisting(existingIssues, wMid.StartIndex, wMid.Length) ||
                            OverlapsExisting(issues, wMid.StartIndex, wMid.Length))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (w1.EndIndex != wLast.StartIndex)
                            continue;
                    }

                    if (wLast.IsProtectedName ||
                        wLast.NormText == "အ" || wLast.NormText == "မ" || wLast.NormText == "တ" ||
                        ProtectedFunctionWords.Contains(wLast.NormText) ||
                        GenericSyntacticParticles.Contains(wLast.NormText) ||
                        OverlapsExisting(existingIssues, wLast.StartIndex, wLast.Length) ||
                        OverlapsExisting(issues, wLast.StartIndex, wLast.Length))
                    {
                        continue;
                    }

                    int totalSyls = w1.SyllableCount + wLast.SyllableCount + (winTokens == 3 ? wordTokens[i + 1].SyllableCount : 0);
                    if (totalSyls < 2 || totalSyls > 6)
                        continue;

                    // Do not steal wLast if wLast + next glued token already forms an exact compound
                    int nextIdx = i + winTokens;
                    if (nextIdx < n && wLast.EndIndex == wordTokens[nextIdx].StartIndex &&
                        trie.ContainsExactWord(wLast.NormText + wordTokens[nextIdx].NormText))
                    {
                        continue;
                    }

                    // Never slice mid-pat-sint stack across adjacent Pali syllables (e.g. 'စု'+'ဒ္ဒသမ' != 'ဒသမ', 'သဒ္ဓ'+'မ္မနန္ဒ' != 'သတ္တ')
                    if ((i > 0 && wordTokens[i - 1].EndIndex == w1.StartIndex && w1.NormText.Length >= 2 && w1.NormText[1] == '\u1039') ||
                        (nextIdx < n && wordTokens[nextIdx].StartIndex == wLast.EndIndex && wordTokens[nextIdx].NormText.Length >= 2 && wordTokens[nextIdx].NormText[1] == '\u1039'))
                    {
                        continue;
                    }

                    string leftPartNorm = winTokens == 3 ? (w1.NormText + wordTokens[i + 1].NormText) : w1.NormText;
                    string rightPartNorm = wLast.NormText;
                    string combinedNorm = leftPartNorm + rightPartNorm;
                    if (trie.ContainsExactWord(combinedNorm) ||
                        trie.GetBigramCount(w1.NormText, rightPartNorm) > 0)
                    {
                        continue;
                    }

                    int spanStart = w1.StartIndex;
                    int spanLen = wLast.EndIndex - w1.StartIndex;
                    string rawSpan = text.Substring(spanStart, spanLen);

                    // Guard rice-bran context for 'အဖွဲ'
                    if (combinedNorm == "အဖွဲ" &&
                        (text.IndexOf("ဆန်", StringComparison.Ordinal) >= 0 ||
                         text.IndexOf("စပါး", StringComparison.Ordinal) >= 0 ||
                         text.IndexOf("နွားစာ", StringComparison.Ordinal) >= 0 ||
                         text.IndexOf("ကြက်စာ", StringComparison.Ordinal) >= 0))
                    {
                        continue;
                    }

                    // Guard literal water compounds for '+ ရေ', literary copula '+ ပေတည်း', and literal 'ရေထိုးခြင်း / ရေထိုးပိုက်' ("injecting/spraying water")
                    if (rightPartNorm == "ရေ" && WaterAndLiquidPrefixes.Contains(w1.NormText))
                        continue;

                    if (rightPartNorm == "ပေ" && nextIdx < n && wordTokens[nextIdx].NormText == "တည်း")
                        continue;

                    if (combinedNorm == "ရေထိုး" &&
                        (text.IndexOf("လေထိုး", StringComparison.Ordinal) >= 0 ||
                         text.IndexOf("အသား", StringComparison.Ordinal) >= 0 ||
                         text.IndexOf("ပိုက်", StringComparison.Ordinal) >= 0 ||
                         (nextIdx < n && (wordTokens[nextIdx].NormText.StartsWith("ခြင်း", StringComparison.Ordinal) ||
                                          wordTokens[nextIdx].NormText.StartsWith("မှု", StringComparison.Ordinal)))))
                    {
                        continue;
                    }

                    ClauseWordToken wPrev = wordTokens[i + winTokens - 2];
                    DictionaryEntry? tier1Entry = TryFindTier1DeterministicCompoundSlip(
                        trie, w1, wPrev, wLast, winTokens, totalSyls, leftPartNorm, rightPartNorm, combinedNorm);

                    if (tier1Entry != null)
                    {
                        string cue = !string.IsNullOrWhiteSpace(tier1Entry.MeaningCue) ? tier1Entry.MeaningCue : "သတ်ပုံကျမ်းဝင် ပေါင်းစပ်ပုဒ်";
                        AddNonOverlapping(issues, new ProofingIssue
                        {
                            StartIndex = spanStart,
                            Length = spanLen,
                            OriginalText = rawSpan,
                            IssueType = ProofingIssueType.SpellingError,
                            RequiresReviewBeforeBatchFix = false,
                            Message = $"ပေါင်းစပ်ပုဒ် စာလုံးပေါင်းအမှန် — '{rawSpan}' အစား '{tier1Entry.Word}' [{cue}] ဟု ရေးသားရမည်။",
                            GrammaticalReasoning = $"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်းဝင် ပေါင်းစပ်ဝေါဟာရ '{tier1Entry.Word}' တွင် အသံ/အသတ် ဆင်တူချို့ယွင်းချက်ကြောင့် သီးခြားပုဒ်ငယ်များအဖြစ် ကွဲထွက်နေခြင်း ဖြစ်ပါသည်။",
                            AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (ပေါင်းစပ်ပုဒ် စံသတ်ပုံ)",
                            Suggestions = new List<SuggestionCandidate>
                            {
                                new SuggestionCandidate
                                {
                                    Replacement = tier1Entry.Word,
                                    MeaningCue = cue,
                                    PosTag = tier1Entry.PosTag,
                                    IsOfficialThatpon = true,
                                    SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(tier1Entry.Word),
                                    DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawSpan, tier1Entry.Word),
                                    Score = 100.0
                                }
                            }
                        });
                        i += winTokens - 1;
                        break;
                    }

                    // Tier 2: Valid-Word Homophone Compound Ambiguity -> Yellow ContextualMeaningWarning (Review Needed ☐)
                    if (!w1IsNominalPrefixA && totalSyls >= 2 && (winTokens == 2 || wordTokens[i + 1].NormText == "အ"))
                    {
                        DictionaryEntry? tier2Entry = TryFindTier2AmbiguousCompoundHomophone(
                            trie, wordTokens, nextIdx, w1, wLast, totalSyls, combinedNorm);
                        if (tier2Entry != null)
                        {
                            string cue = !string.IsNullOrWhiteSpace(tier2Entry.MeaningCue) ? tier2Entry.MeaningCue : "သတ်ပုံကျမ်းဝင် ပေါင်းစပ်ပုဒ်";
                            AddNonOverlapping(issues, new ProofingIssue
                            {
                                StartIndex = spanStart,
                                Length = spanLen,
                                OriginalText = rawSpan,
                                IssueType = ProofingIssueType.ContextualMeaningWarning,
                                RequiresReviewBeforeBatchFix = true,
                                Message = $"အနက်သံသယ စိစစ်ရန် — '{rawSpan}' အစား ပေါင်းစပ်ဝေါဟာရ '{tier2Entry.Word}' [{cue}] ဖြစ်နိုင်ပါသည်။",
                                GrammaticalReasoning = $"အစိတ်အပိုင်းတစ်ခုချင်းစီသည် အဘိဓာန်ဝင်စာလုံးများ ဖြစ်သော်လည်း ပေါင်းစပ်ဝေါဟာရ '{tier2Entry.Word}' နှင့် အသံထွက်တူနေသဖြင့် ရည်ရွယ်ရင်းအနက်ကို စိစစ်ရန် အကြံပြုပါသည်။",
                                AuthoritativeSource = "မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း (အသံတူ ပေါင်းစပ်ပုဒ် စိစစ်ချက်)",
                                Suggestions = new List<SuggestionCandidate>
                                {
                                    new SuggestionCandidate
                                    {
                                        Replacement = tier2Entry.Word,
                                        MeaningCue = cue,
                                        PosTag = tier2Entry.PosTag,
                                        IsOfficialThatpon = true,
                                        SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(tier2Entry.Word),
                                        DiagnosticDetail = OrthographicDiffExplainer.ExplainDifference(rawSpan, tier2Entry.Word),
                                        Score = 93.0
                                    }
                                }
                            });
                            i += winTokens - 1;
                            break;
                        }
                    }
                }
            }

            // Tier 3 (v71 Hybrid): Two-Tier Statistical Bigram + UPOS-Trigram Intruder-Word Detector (Review-Only Warning ☐)
            DetectStatisticalIntruderWords(text, trie, wordTokens, existingIssues, issues);
        }

        private static readonly HashSet<string> IntruderBareActionVerbs = new HashSet<string>(StringComparer.Ordinal)
        {
            "စား", "သောက်", "ထိုင်", "အိပ်", "ခုန်", "ကိုက်", "ကန်"
        };

        private static readonly HashSet<string> IntruderConcreteCountNouns = new HashSet<string>(StringComparer.Ordinal)
        {
            "ကြောင်", "ခွေး", "ပန်းသီး", "စားပွဲ", "ထိုင်ခုံ", "ကုလားထိုင်", "ဖိနပ်", "ဦးထုပ်", "ထမင်း", "ကျောင်း", "ပွဲ", "ကား", "မြို့"
        };

        private static HashSet<string> BuildCanonicalSet(params string[] items)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Length; i++)
            {
                set.Add(UnicodeNormalizer.NormalizeCanonicalOrder(items[i]));
            }
            return set;
        }

        private static readonly HashSet<string> AbstractTopicHeadNouns = BuildCanonicalSet(
            "နည်းပညာ", "စွမ်းအင်", "စွမ်းအင်သစ်", "သိပ္ပံ", "သိပ္ပံပညာ", "ဒီမိုကရေစီ", "စီးပွားရေး", "ကျန်းမာရေး",
            "ပညာရေး", "ယဉ်ကျေးမှု", "စာနာမှု", "မေတ္တာတရား", "လစာ", "သတ္တုလုပ်ငန်း", "ခံစားချက်", "ပျော်ရွှင်မှု"
        );

        private static readonly HashSet<string> NonSelectingIntransitiveOrMentalHeads = BuildCanonicalSet(
            "နာကျင်", "နုတ်ထွက်", "ထွက်ပြေး", "သစ္စာရှိ", "စိတ်ဝင်စား", "စိတ်ဝင်စားစရာ", "မျှော်လင့်", "မျှော်လင့်ချက်",
            "အားတက်", "စိတ်လှုပ်ရှား", "ဝမ်းသာ", "ဝမ်းနည်း", "ပျော်ရွှင်", "စာရေးစွမ်းရည်", "အသစ်", "အဟောင်း",
            "ထားရှိ", "ခံစား", "ရေရှည်", "ရေရှည်တည်တံ့", "သိရှိ", "နားလည်", "သိရှိနားလည်"
        );

        private static bool IsPureHorizontalSpaceBetween(string text, ClauseWordToken left, ClauseWordToken right)
        {
            int st = left.EndIndex;
            int en = right.StartIndex;
            if (en <= st || en - st > 3)
                return false;
            for (int k = st; k < en; k++)
            {
                char c = text[k];
                if (c != ' ' && c != '\t')
                    return false;
            }
            return true;
        }

        private static bool EndsWithClosedCaseOrClauseLinker(string norm)
        {
            if (string.IsNullOrEmpty(norm))
                return false;
            return norm == "က" || norm == "ဟာ" || norm == "သည်" || norm == "ကို" || norm == "ကနေ" || norm == "မှ" ||
                   norm == "မှာ" || norm == "တွင်" || norm == "၌" || norm == "ပါက" || norm == "လျှင်" || norm == "ရင်" ||
                   norm == "ပြီး" || norm == "၍" || norm == "ဖြင့်" || norm == "အတွက်" || norm == "ကြောင့်" ||
                   norm == "တဲ့" || norm == "သော" || norm == "သည့်" ||
                   (norm.Length >= 3 && (
                       norm.EndsWith("ကနေ", StringComparison.Ordinal) ||
                       norm.EndsWith("ပါက", StringComparison.Ordinal) ||
                       norm.EndsWith("ပြီး", StringComparison.Ordinal) ||
                       norm.EndsWith("ဖြင့်", StringComparison.Ordinal) ||
                       norm.EndsWith("တွေ", StringComparison.Ordinal) ||
                       norm.EndsWith("များ", StringComparison.Ordinal)));
        }

        private static bool IsVerbalFollowerOrTemporalNominal(string norm)
        {
            if (string.IsNullOrEmpty(norm))
                return false;
            return norm == "ပြီး" || norm == "နေ" || norm == "တယ်" || norm == "သည်" || norm == "မယ်" || norm == "မည်" ||
                   norm == "ပါ" || norm == "ပါတယ်" || norm == "ပါသည်" || norm == "ကြ" || norm == "နိုင်" || norm == "ချင်" ||
                   norm == "ဖို့" || norm == "ရန်" || norm == "စရာ" || norm == "ဘူး" || norm == "တော့" || norm == "သေး" ||
                   norm == "လျက်" || norm == "ရင်း" || norm == "ကာ" || norm == "၍" || norm == "သော" || norm == "သည့်" ||
                   norm == "တဲ့" || norm == "အပြီး" || norm == "အပြန်" || norm == "အထွက်" || norm == "အဝင်" ||
                   norm == "စဉ်" || norm == "ခိုက်" || norm == "သောက်" || norm == "စား";
        }

        private static bool MatchesNonSelectingHead(string norm)
        {
            if (string.IsNullOrEmpty(norm))
                return false;
            if (NonSelectingIntransitiveOrMentalHeads.Contains(norm))
                return true;
            foreach (string head in NonSelectingIntransitiveOrMentalHeads)
            {
                if (norm.StartsWith(head, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void DetectStatisticalIntruderWords(
            string text,
            MyanmarTrie trie,
            List<ClauseWordToken> wordTokens,
            List<ProofingIssue> existingIssues,
            List<ProofingIssue> issues)
        {
            int n = wordTokens.Count;
            if (n < 3)
                return;

            for (int i = 1; i < n - 1; i++)
            {
                ClauseWordToken wPrev = wordTokens[i - 1];
                ClauseWordToken wCur = wordTokens[i];
                ClauseWordToken wNext = wordTokens[i + 1];

                if (wCur.IsProtectedName || wNext.IsProtectedName ||
                    wPrev.ClauseId != wCur.ClauseId || wCur.ClauseId != wNext.ClauseId ||
                    wCur.SyllableCount > 2 ||
                    OverlapsExisting(existingIssues, wCur.StartIndex, wCur.Length) ||
                    OverlapsExisting(issues, wCur.StartIndex, wCur.Length))
                {
                    continue;
                }

                // Never flag if wCur has any content-word corpus bigram or lexical compound with either neighbor
                bool prevGluedToLeft = i >= 2 &&
                                       wordTokens[i - 2].ClauseId == wCur.ClauseId &&
                                       wordTokens[i - 2].EndIndex == wPrev.StartIndex;
                bool nextGluedToRight = i + 2 < n &&
                                        wordTokens[i + 2].ClauseId == wCur.ClauseId &&
                                        wNext.EndIndex == wordTokens[i + 2].StartIndex;

                int leftBackIdx = i - 1;
                while (leftBackIdx > 0 &&
                       wordTokens[leftBackIdx - 1].ClauseId == wCur.ClauseId &&
                       (wordTokens[leftBackIdx].NormText == "က" ||
                        (wordTokens[leftBackIdx].NormText == "ကို" && wCur.NormText != "တစ်ခု") ||
                        wordTokens[leftBackIdx].NormText == "မှာ" || wordTokens[leftBackIdx].NormText == "တွင်" ||
                        wordTokens[leftBackIdx].NormText == "နေ" || wordTokens[leftBackIdx].NormText == "ကနေ" ||
                        wordTokens[leftBackIdx].NormText == "ပါ" || wordTokens[leftBackIdx].NormText == "ပါက" ||
                        wordTokens[leftBackIdx].NormText == "တဲ့" || wordTokens[leftBackIdx].NormText == "သော" ||
                        wordTokens[leftBackIdx].NormText == "သည့်" || wordTokens[leftBackIdx].NormText == "တွေ" ||
                        wordTokens[leftBackIdx].NormText == "များ"))
                {
                    leftBackIdx--;
                }
                string leftBigramCheck = wordTokens[leftBackIdx].NormText;

                int rightFwdIdx = i + 1;
                while (rightFwdIdx + 1 < n &&
                       wordTokens[rightFwdIdx + 1].ClauseId == wCur.ClauseId &&
                       (wordTokens[rightFwdIdx].NormText == "အရမ်း" || wordTokens[rightFwdIdx].NormText == "အလွန်" ||
                        wordTokens[rightFwdIdx].NormText == "သိပ်" || wordTokens[rightFwdIdx].NormText == "အမြန်" ||
                        wordTokens[rightFwdIdx].NormText == "အမြန်ဆုံး" || wordTokens[rightFwdIdx].NormText == "ဆုံး"))
                {
                    rightFwdIdx++;
                }
                string rightBigramCheck = wordTokens[rightFwdIdx].NormText;

                bool bypassBigramVeto =
                    (wCur.NormText == "မြို့" && wPrev.NormText.EndsWith("တဲ့", StringComparison.Ordinal) && wNext.NormText.StartsWith("အလုပ်", StringComparison.Ordinal)) ||
                    (wCur.NormText == "လေ" && wPrev.NormText.EndsWith("လည်း", StringComparison.Ordinal) &&
                     wCur.EndIndex == wNext.StartIndex && wNext.NormText.StartsWith("ဖြစ်", StringComparison.Ordinal));

                if (!bypassBigramVeto &&
                    (trie.GetBigramCount(leftBigramCheck, wCur.NormText) > 0 ||
                     trie.GetBigramCount(wCur.NormText, rightBigramCheck) > 0 ||
                     (!prevGluedToLeft && trie.ContainsExactWord(wPrev.NormText + wCur.NormText)) ||
                     (!nextGluedToRight && trie.ContainsExactWord(wCur.NormText + wNext.NormText))))
                {
                    continue;
                }

                // Collect contiguous right-side word phrase (up to 4 tokens with no intervening space) for multi-morpheme predicates like 'အရမ်းသစ္စာရှိပြီး', 'အမြန်ဆုံးထွက်ပြေးပြီး'
                int rightLastIdx = i + 1;
                string rightConcatNorm = wNext.NormText;
                while (rightLastIdx + 1 < n &&
                       wordTokens[rightLastIdx + 1].ClauseId == wCur.ClauseId &&
                       wordTokens[rightLastIdx].EndIndex == wordTokens[rightLastIdx + 1].StartIndex &&
                       rightLastIdx - (i + 1) < 4)
                {
                    rightLastIdx++;
                    rightConcatNorm += wordTokens[rightLastIdx].NormText;
                }
                int rightPhraseEnd = wordTokens[rightLastIdx].EndIndex;
                string rightPhraseRaw = text.Substring(wNext.StartIndex, rightPhraseEnd - wNext.StartIndex);

                bool rightHasNonSelectingHead = MatchesNonSelectingHead(wNext.NormText) || MatchesNonSelectingHead(rightConcatNorm);
                if (!rightHasNonSelectingHead)
                {
                    foreach (string head in NonSelectingIntransitiveOrMentalHeads)
                    {
                        if (head.Length >= 4 && rightConcatNorm.IndexOf(head, StringComparison.Ordinal) > 0)
                        {
                            rightHasNonSelectingHead = true;
                            break;
                        }
                    }
                }

                // Exclude determiners, numerals, possessives, and conjunctions immediately before wCur
                if (wPrev.NormText == "ဤ" || wPrev.NormText == "ထို" || wPrev.NormText == "ယင်း" ||
                    wPrev.NormText == "ဒီ" || wPrev.NormText == "ဟို" || wPrev.NormText == "အဆိုပါ" ||
                    wPrev.NormText == "၏" || wPrev.NormText == "ရဲ့" || wPrev.NormText == "နှင့်" || wPrev.NormText == "နဲ့" ||
                    (wPrev.NormText.EndsWith("\u1037", StringComparison.Ordinal) &&
                     !wPrev.NormText.EndsWith("\u103A\u1037", StringComparison.Ordinal) &&
                     wPrev.NormText != "သို့" &&
                     !wPrev.NormText.EndsWith("တဲ့", StringComparison.Ordinal)))
                {
                    continue;
                }

                bool leftSpace = IsPureHorizontalSpaceBetween(text, wPrev, wCur);
                bool rightSpace = IsPureHorizontalSpaceBetween(text, wCur, wNext);
                bool isIntruder = false;

                // Case 1: Bare monosyllabic action/postural verb wedged before non-verbal/degree/mental head
                if (IntruderBareActionVerbs.Contains(wCur.NormText))
                {
                    if (leftSpace && rightSpace && !IsVerbalFollowerOrTemporalNominal(wNext.NormText))
                    {
                        if (rightHasNonSelectingHead ||
                            (AbstractTopicHeadNouns.Contains(wPrev.NormText) && wNext.NormText.StartsWith("အ", StringComparison.Ordinal)))
                        {
                            isIntruder = true;
                        }
                    }
                    else if (leftSpace && wCur.EndIndex == wNext.StartIndex &&
                             (wCur.NormText == "ထိုင်" || wCur.NormText == "ခုန်") &&
                             rightConcatNorm.StartsWith("ခံစား", StringComparison.Ordinal) &&
                             EndsWithClosedCaseOrClauseLinker(wPrev.NormText))
                    {
                        isIntruder = true;
                    }
                }
                // Case 2: Unmarked concrete count noun wedged in an already-closed or abstract frame before an incompatible predicate
                else if (IntruderConcreteCountNouns.Contains(wCur.NormText))
                {
                    if (leftSpace && rightSpace)
                    {
                        bool prevIsGaNay = wPrev.NormText == "နေ" && i >= 2 && wordTokens[i - 2].NormText.EndsWith("က", StringComparison.Ordinal);
                        bool closedOrAbstractLeft = EndsWithClosedCaseOrClauseLinker(wPrev.NormText) ||
                                                    prevIsGaNay ||
                                                    AbstractTopicHeadNouns.Contains(wPrev.NormText);
                        if ((closedOrAbstractLeft && rightHasNonSelectingHead) ||
                            (wCur.NormText == "မြို့" && wPrev.NormText.EndsWith("တဲ့", StringComparison.Ordinal) && rightConcatNorm.StartsWith("အလုပ်", StringComparison.Ordinal)))
                        {
                            isIntruder = true;
                        }
                    }
                    else if (leftSpace && wCur.EndIndex == wNext.StartIndex)
                    {
                        if ((wNext.NormText == "လို့" || wNext.NormText == "ဟု") && AbstractTopicHeadNouns.Contains(wPrev.NormText))
                        {
                            isIntruder = true;
                        }
                        else if (wCur.NormText == "ထမင်း" && wNext.NormText.StartsWith("ရေရှည်", StringComparison.Ordinal))
                        {
                            isIntruder = true;
                        }
                    }
                    else if (wPrev.EndIndex == wCur.StartIndex && wCur.EndIndex == wNext.StartIndex &&
                             wPrev.NormText == "လစာ" && wCur.NormText == "ကျောင်း" && wNext.NormText.StartsWith("ကောင်း", StringComparison.Ordinal))
                    {
                        isIntruder = true;
                    }
                }
                // Case 3: Post-object stranded 'တစ်ခု' before cognitive predicate, or stranded space-isolated 'များ' after reciprocal 'တစ်ယောက်ကိုတစ်ယောက်'
                else if (leftSpace && rightSpace)
                {
                    if (wCur.NormText == "တစ်ခု" &&
                        i >= 2 && wPrev.NormText == "ကို" && wordTokens[i - 2].SyllableCount >= 2 &&
                        MatchesNonSelectingHead(wNext.NormText))
                    {
                        isIntruder = true;
                    }
                    else if (wCur.NormText == "များ" &&
                             i >= 2 && wPrev.NormText.EndsWith("ယောက်", StringComparison.Ordinal) &&
                             wordTokens[i - 2].NormText == "ကို" &&
                             !wNext.NormText.StartsWith("သည်", StringComparison.Ordinal) &&
                             !wNext.NormText.StartsWith("စွာ", StringComparison.Ordinal))
                    {
                        isIntruder = true;
                    }
                }
                else if (leftSpace && wCur.EndIndex == wNext.StartIndex &&
                         wCur.NormText == "လေ" &&
                         wPrev.NormText.EndsWith("လည်း", StringComparison.Ordinal) &&
                         wNext.NormText.StartsWith("ဖြစ်", StringComparison.Ordinal))
                {
                    isIntruder = true;
                }

                if (!isIntruder)
                    continue;

                // Build clean single-space replacement span so 1-click user confirmation never leaves double spaces
                int repStart;
                int repLen;
                string repSuggestion;
                if (leftSpace && wCur.EndIndex == wNext.StartIndex && (wNext.NormText == "လို့" || wNext.NormText == "ဟု"))
                {
                    repStart = wPrev.StartIndex;
                    repLen = wCur.EndIndex - wPrev.StartIndex;
                    repSuggestion = wPrev.RawText;
                }
                else
                {
                    repStart = wCur.StartIndex;
                    repLen = rightPhraseEnd - wCur.StartIndex;
                    repSuggestion = rightPhraseRaw;
                }

                if (OverlapsExisting(existingIssues, repStart, repLen) || OverlapsExisting(issues, repStart, repLen))
                    continue;

                string origSpan = text.Substring(repStart, repLen);
                AddNonOverlapping(issues, new ProofingIssue
                {
                    StartIndex = repStart,
                    Length = repLen,
                    OriginalText = origSpan,
                    IssueType = ProofingIssueType.ContextualMeaningWarning,
                    RequiresReviewBeforeBatchFix = true,
                    Message = $"ဝါကျစကားစပ်နှင့် မဆီလျော်သော စကားလုံးအပို ('{wCur.RawText}') ဝင်ရောက်နေနိုင်ပါသည် — စိစစ်ဖယ်ရှားရန် အကြံပြုပါသည်။",
                    GrammaticalReasoning = $"ရှေ့နောက်ဝါကျအစိတ်အပိုင်း ('{wPrev.RawText} ... {wNext.RawText}') အကြားတွင် '{wCur.RawText}' သည် တွဲဖက်သုံးစွဲလေ့ရှိသော Bigram အထောက်အထားနှင့် သဒ္ဒါဆက်စပ်မှု မရှိသဖြင့် မှားယွင်းဝင်ရောက်နေသော ပိုလျှံစကားလုံး (Intruder Word) ဖြစ်နိုင်ပါသည်။",
                    AuthoritativeSource = "မြန်မာဝါကျဖွဲ့ထုံးနှင့် ကိန်းဂဏန်းအခြေပြု စကားစပ်စိစစ်ချက် (Hybrid N-Gram Model)",
                    Suggestions = new List<SuggestionCandidate>
                    {
                        new SuggestionCandidate
                        {
                            Replacement = repSuggestion,
                            MeaningCue = $"အပိုစကားလုံး '{wCur.RawText}' ကို ဖယ်ရှားမည်",
                            PosTag = "SYN",
                            IsOfficialThatpon = true,
                            SyllableBreakdown = OrthographicDiffExplainer.FormatSyllableBreakdown(repSuggestion),
                            DiagnosticDetail = $"စကားစပ်မကိုက်ညီသော '{wCur.RawText}' ကို ဖယ်ရှားခြင်း",
                            Score = 92.0
                        }
                    }
                });
            }
        }

        private static DictionaryEntry? TryFindTier1DeterministicCompoundSlip(
            MyanmarTrie trie,
            ClauseWordToken w1,
            ClauseWordToken wPrev,
            ClauseWordToken wLast,
            int winTokens,
            int totalSyls,
            string leftPartNorm,
            string rightPartNorm,
            string combinedNorm)
        {
            DictionaryEntry? best = null;

            // 1A. Trailing dropped visarga 'း' on >=3-syllable compound (e.g. ပညာရေ -> ပညာရေး, အစည်းအဝေ -> အစည်းအဝေး)
            //     or trailing dropped creaky '့' on 'အ + [1-syl]' (e.g. အဖွဲ -> အဖွဲ့)
            if (rightPartNorm.IndexOf('\u1037') < 0 &&
                rightPartNorm.IndexOf('\u1038') < 0 &&
                !EndsWithStopCoda(rightPartNorm))
            {
                if (totalSyls >= 3 || (w1.SyllableCount >= 2 && rightPartNorm == "ရေ"))
                {
                    PickBestOfficialEntry(trie, combinedNorm + "\u1038", minFreq: 25, ref best);
                }

                if (winTokens == 2 && w1.NormText == "အ" && wLast.SyllableCount == 1)
                {
                    PickBestOfficialEntry(trie, combinedNorm + "\u1037", minFreq: 55, ref best);
                }
            }

            // 1B. Leading dropped creaky dot '့' on 2-syllable indivisible Thatpon compound (e.g. ခန်မှန်း -> ခန့်မှန်း, ကန်သတ် -> ကန့်သတ်, ချိုယွင်း -> ချို့ယွင်း)
            if (winTokens == 2 &&
                w1.SyllableCount == 1 &&
                wLast.SyllableCount == 1 &&
                !ExcludedLeadingSingleSylForCreaky.Contains(w1.NormText) &&
                w1.NormText.IndexOf('\u1037') < 0 &&
                w1.NormText.IndexOf('\u1038') < 0 &&
                !EndsWithStopCoda(w1.NormText))
            {
                PickBestOfficialEntry(trie, w1.NormText + "\u1037" + rightPartNorm, minFreq: 25, ref best);
            }

            // 1B-2. Leading dropped visarga 'း' on 2-syllable indivisible Thatpon compound (e.g. ဆွေနွေး -> ဆွေးနွေး, သိမ်ဆည်း -> သိမ်းဆည်း, ဈေကွက် -> ဈေးကွက်, ကျောင်သား -> ကျောင်းသား)
            if (winTokens == 2 &&
                w1.SyllableCount == 1 &&
                wLast.SyllableCount == 1 &&
                !ExcludedLeadingSingleSylForVisarga.Contains(w1.NormText) &&
                !GenericSyntacticParticles.Contains(rightPartNorm) &&
                w1.NormText.IndexOf('\u1037') < 0 &&
                w1.NormText.IndexOf('\u1038') < 0 &&
                !EndsWithStopCoda(w1.NormText))
            {
                PickBestOfficialEntry(trie, w1.NormText + "\u1038" + rightPartNorm, minFreq: 25, ref best);
            }

            // 1C. Homophonous killed-coda slips on final syllable (-တ် <-> -က်, -င် <-> -ဉ်, -ပ် <-> -တ်)
            //     (e.g. အချက်အလတ် -> အချက်အလက်, လုပ်ငန်းစင် -> လုပ်ငန်းစဉ်, ဆောင်ရွက်ချတ် -> ဆောင်ရွက်ချက်, အသီးအရွတ် -> အသီးအရွက်)
            int minCodaFreq = totalSyls >= 3 ? 20 : 45;
            if (w1.NormText != "အ")
            {
                foreach (string rightVariant in EnumerateHomophonousCodaVariants(rightPartNorm))
                {
                    PickBestOfficialEntry(trie, leftPartNorm + rightVariant, minFreq: minCodaFreq, ref best);
                }
            }

            // 1D. Dropped medial 'အ' in 4-syllable symmetric 'အXအY' Official Thatpon compounds (e.g. အကောင်ထည် -> အကောင်အထည်, အသိမှတ်ပြု -> အသိအမှတ်ပြု)
            if (winTokens == 2 &&
                w1.SyllableCount == 2 &&
                w1.NormText.StartsWith("အ", StringComparison.Ordinal) &&
                !rightPartNorm.StartsWith("အ", StringComparison.Ordinal))
            {
                PickBestOfficialEntry(trie, w1.NormText + "အ" + rightPartNorm, minFreq: 10, ref best);
                if (best == null && rightPartNorm.Length > 3 &&
                    (rightPartNorm.EndsWith("ပြု", StringComparison.Ordinal) || rightPartNorm.EndsWith("ဖော်", StringComparison.Ordinal)))
                {
                    string vTail = rightPartNorm.EndsWith("ပြု", StringComparison.Ordinal) ? "ပြု" : "ဖော်";
                    string midStem = rightPartNorm.Substring(0, rightPartNorm.Length - vTail.Length);
                    if (midStem.Length > 0 && trie.ContainsExactWord(w1.NormText + "အ" + midStem))
                    {
                        string full4 = UnicodeNormalizer.NormalizeCanonicalOrder(w1.NormText + "အ" + rightPartNorm);
                        best = new DictionaryEntry
                        {
                            Word = full4,
                            MeaningCue = $"သတ်ပုံကျမ်းဝင် '{w1.NormText}အ{midStem}' ပေါင်းစပ်ပုဒ် ('အ' ကျန်ခဲ့ခြင်း)",
                            PosTag = "V",
                            IsOfficialThatpon = true,
                            Frequency = 50
                        };
                    }
                }
            }

            // 1E. Nominalizer '-မူ -> -မှု' after >=2-syllable established verb/compound (e.g. ပြေလည်မူ -> ပြေလည်မှု, အာဏာသိမ်းမူ -> အာဏာသိမ်းမှု, သင်ကြားပို့ချမူ -> သင်ကြားပို့ချမှု)
            if (winTokens >= 2 && winTokens <= 3 &&
                totalSyls >= 3 &&
                rightPartNorm == "မူ" &&
                !leftPartNorm.EndsWith("တော်", StringComparison.Ordinal))
            {
                PickBestOfficialEntry(trie, leftPartNorm + "မှု", minFreq: 3, ref best);
                if (best == null &&
                    (trie.ContainsExactWord(leftPartNorm + "မှု") ||
                     (winTokens == 2 && w1.SyllableCount >= 2 && trie.HasPosTag(w1.NormText, "V")) ||
                     (winTokens == 3 && wPrev.SyllableCount >= 2 && trie.HasPosTag(wPrev.NormText, "V"))))
                {
                    best = new DictionaryEntry
                    {
                        Word = leftPartNorm + "မှု",
                        MeaningCue = "ကြိယာနာမ်ပြုပစ္စည်း ('~မှု' — ဟထိုး တစ်ချောင်းငင်)",
                        PosTag = "N",
                        IsOfficialThatpon = true,
                        Frequency = 35
                    };
                }
            }

            return best;
        }

        private static DictionaryEntry? TryFindTier2AmbiguousCompoundHomophone(
            MyanmarTrie trie,
            List<ClauseWordToken> wordTokens,
            int nextIdx,
            ClauseWordToken w1,
            ClauseWordToken wLast,
            int totalSyls,
            string combinedNorm)
        {
            int f1 = trie.GetWordFrequency(w1.NormText);
            int f2 = trie.GetWordFrequency(wLast.NormText);
            int minPartFreq = Math.Min(f1, f2);
            if (minPartFreq > 30)
                return null;

            bool followedByVerbalCloser = nextIdx < wordTokens.Count &&
                (wordTokens[nextIdx].NormText == "သည်" || wordTokens[nextIdx].NormText == "တယ်" ||
                 wordTokens[nextIdx].NormText == "မည်" || wordTokens[nextIdx].NormText == "မယ်" ||
                 wordTokens[nextIdx].NormText == "ပါသည်" || wordTokens[nextIdx].NormText == "ပါတယ်");

            List<DictionaryEntry> homophones = trie.LookupStrictHomophones(combinedNorm);
            DictionaryEntry? best = null;
            for (int i = 0; i < homophones.Count; i++)
            {
                var cand = homophones[i];
                if (!cand.IsOfficialThatpon || cand.Word == combinedNorm || cand.Frequency < 65)
                    continue;
                int cSyl = cand.SyllableCount > 0
                    ? cand.SyllableCount
                    : MyanmarSyllableBreaker.BreakIntoSyllables(cand.Word, isPreNormalized: true).Count;
                if (cSyl != totalSyls)
                    continue;
                if (followedByVerbalCloser && string.Equals(cand.PosTag, "N", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (cand.Frequency < Math.Max(65, minPartFreq * 4))
                    continue;
                if (best == null || cand.Frequency > best.Frequency)
                    best = cand;
            }
            return best;
        }

        private static void PickBestOfficialEntry(MyanmarTrie trie, string candidateWord, int minFreq, ref DictionaryEntry? currentBest)
        {
            string normCand = UnicodeNormalizer.NormalizeCanonicalOrder(candidateWord);
            if (!trie.TryGetExactEntries(normCand, out var entries) || entries == null)
                return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!e.IsOfficialThatpon || e.Frequency < minFreq)
                    continue;
                if (currentBest == null || e.Frequency > currentBest.Frequency)
                    currentBest = e;
            }
        }

        private static bool EndsWithStopCoda(string normSyl)
        {
            if (normSyl.Length < 2 || normSyl[normSyl.Length - 1] != '\u103A')
                return false;
            char c = normSyl[normSyl.Length - 2];
            return c == '\u1000' || c == '\u1005' || c == '\u1010' || c == '\u1015';
        }

        private static IEnumerable<string> EnumerateHomophonousCodaVariants(string syl)
        {
            if (string.IsNullOrEmpty(syl) || syl.Length < 2)
                yield break;

            string tone = string.Empty;
            string core = syl;
            char lastCh = syl[syl.Length - 1];
            if (lastCh == '\u1037' || lastCh == '\u1038')
            {
                tone = lastCh.ToString();
                core = syl.Substring(0, syl.Length - 1);
            }

            if (core.Length < 2 || core[core.Length - 1] != '\u103A')
                yield break;

            char coda = core[core.Length - 2];
            string stem = core.Substring(0, core.Length - 2);

            if (coda == '\u1010') // -တ် -> -က်, -ပ်
            {
                yield return stem + "\u1000\u103A" + tone;
                yield return stem + "\u1015\u103A" + tone;
            }
            else if (coda == '\u1000') // -က် -> -တ်, -ပ်
            {
                yield return stem + "\u1010\u103A" + tone;
                yield return stem + "\u1015\u103A" + tone;
            }
            else if (coda == '\u1015') // -ပ် -> -တ်, -က်
            {
                yield return stem + "\u1010\u103A" + tone;
                yield return stem + "\u1000\u103A" + tone;
            }
            else if (coda == '\u1004') // -င် -> -ဉ်
            {
                yield return stem + "\u1009\u103A" + tone;
            }
            else if (coda == '\u1009') // -ဉ် -> -င်
            {
                yield return stem + "\u1004\u103A" + tone;
            }
        }

        private static bool HasInterveningNonSpacePunctuation(string text, ClauseWordToken left, ClauseWordToken right)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            int start = Math.Max(0, left.StartIndex + left.Length);
            int end = Math.Min(text.Length, right.StartIndex);
            for (int k = start; k < end; k++)
            {
                char ch = text[k];
                if (!char.IsWhiteSpace(ch) && ch != '\u200B' && ch != '\u200C' && ch != '\u200D' && ch != '\uFEFF')
                {
                    return true;
                }
            }
            return false;
        }

        private static List<ClauseWordToken> TokenizeIntoClauseWords(
            string text,
            MyanmarTrie trie,
            List<SyllableToken>? precomputedTokens = null,
            int[]? precomputedLongestMatchLengths = null,
            bool[]? personNameMask = null)
        {
            var result = new List<ClauseWordToken>();
            List<SyllableToken> sylTokens = precomputedTokens ?? MyanmarSyllableBreaker.Tokenize(text);
            bool hasMask = precomputedTokens != null && personNameMask != null && personNameMask.Length >= sylTokens.Count;
            int clauseId = 0;
            int idx = 0;

            while (idx < sylTokens.Count)
            {
                SyllableToken st = sylTokens[idx];
                if (st.Kind != TokenKind.MyanmarSyllable)
                {
                    if (st.Text.IndexOf('\u104B') >= 0 || st.Text.IndexOf('\n') >= 0 ||
                        st.Text.IndexOf('?') >= 0 || st.Text.IndexOf('!') >= 0)
                    {
                        clauseId++;
                    }
                    idx++;
                    continue;
                }

                // 1. Check longest exact dictionary span so valid compounds (e.g. လက်ဖက်ရည်, ကျန်းမာရေး, အမိန့်ကြော်ငြာစာ) stay intact
                int matchedLen;
                if (precomputedLongestMatchLengths != null && idx < precomputedLongestMatchLengths.Length)
                {
                    matchedLen = precomputedLongestMatchLengths[idx];
                }
                else
                {
                    trie.MatchLongestAt(sylTokens, idx, out matchedLen, out _, out _);
                }
                if (matchedLen >= 2 && matchedLen <= 8)
                {
                    SyllableToken endTok = sylTokens[idx + matchedLen - 1];
                    int charLen = (endTok.StartIndex + endTok.Length) - st.StartIndex;
                    string raw = text.Substring(st.StartIndex, charLen);
                    bool allPreNormAndContiguous = true;
                    bool anyProtected = false;
                    int sumLen = 0;
                    for (int k = 0; k < matchedLen; k++)
                    {
                        SyllableToken tk = sylTokens[idx + k];
                        sumLen += tk.Length;
                        if (hasMask && personNameMask![idx + k])
                            anyProtected = true;
                        if (!ReferenceEquals(tk.Text, tk.NormalizedText) && tk.Text != tk.NormalizedText)
                        {
                            allPreNormAndContiguous = false;
                        }
                    }
                    string norm = (allPreNormAndContiguous && sumLen == charLen)
                        ? raw
                        : MyanmarProofingEngine.ConcatNormalizedTokens(sylTokens, idx, matchedLen);
                    result.Add(new ClauseWordToken
                    {
                        StartIndex = st.StartIndex,
                        Length = charLen,
                        ClauseId = clauseId,
                        SyllableCount = matchedLen,
                        IsProtectedName = anyProtected,
                        RawText = raw,
                        NormText = norm
                    });
                    idx += matchedLen;
                    continue;
                }

                // 2. Check if a multi-syllable ContextualRule WrongForm (2..3 contiguous syllables) starts here
                int ctxMatchedLen = 0;
                string? matchedCtxNorm = null;
                if (trie.ContextualRuleCount > 0 && trie.IsContextualMultiSyllableStart(st.NormalizedText))
                {
                    int maxCheck = Math.Min(3, sylTokens.Count - idx);
                    for (int len = maxCheck; len >= 2; len--)
                    {
                        bool contiguous = true;
                        for (int k = 0; k < len; k++)
                        {
                            if (sylTokens[idx + k].Kind != TokenKind.MyanmarSyllable) { contiguous = false; break; }
                            if (k > 0 && sylTokens[idx + k].StartIndex != sylTokens[idx + k - 1].StartIndex + sylTokens[idx + k - 1].Length)
                            {
                                contiguous = false;
                                break;
                            }
                        }
                        if (!contiguous) continue;

                        string candNorm = MyanmarProofingEngine.ConcatNormalizedTokens(sylTokens, idx, len);
                        if (trie.TryGetContextualRulesPreNormalized(candNorm, out _))
                        {
                            ctxMatchedLen = len;
                            matchedCtxNorm = candNorm;
                            break;
                        }
                    }
                }

                if (ctxMatchedLen >= 2)
                {
                    SyllableToken endTok = sylTokens[idx + ctxMatchedLen - 1];
                    int charLen = (endTok.StartIndex + endTok.Length) - st.StartIndex;
                    bool anyProtected = false;
                    if (hasMask)
                    {
                        for (int k = 0; k < ctxMatchedLen; k++)
                        {
                            if (personNameMask![idx + k]) { anyProtected = true; break; }
                        }
                    }
                    result.Add(new ClauseWordToken
                    {
                        StartIndex = st.StartIndex,
                        Length = charLen,
                        ClauseId = clauseId,
                        SyllableCount = ctxMatchedLen,
                        IsProtectedName = anyProtected,
                        RawText = text.Substring(st.StartIndex, charLen),
                        NormText = matchedCtxNorm!
                    });
                    idx += ctxMatchedLen;
                    continue;
                }

                // 3. Single syllable fallback
                result.Add(new ClauseWordToken
                {
                    StartIndex = st.StartIndex,
                    Length = st.Length,
                    ClauseId = clauseId,
                    SyllableCount = 1,
                    IsProtectedName = hasMask && personNameMask![idx],
                    RawText = st.Text,
                    NormText = st.NormalizedText
                });
                idx++;
            }

            return result;
        }

        private static bool IsCalendarDayContext(string text, int startIdx, int endIdx)
        {
            int p = startIdx - 1;
            while (p >= 0 && (char.IsWhiteSpace(text[p]) || text[p] == ')' || text[p] == '）' || text[p] == ']'))
                p--;
            if (p >= 0)
            {
                char prev = text[p];
                if ((prev >= '0' && prev <= '9') || (prev >= '\u1040' && prev <= '\u1049') || prev == 'လ')
                    return true;
            }
            if (endIdx + 3 <= text.Length && string.CompareOrdinal(text, endIdx, "နေ့", 0, 3) == 0)
                return true;
            return false;
        }

        private static bool HasInterveningDigit(string text, ClauseWordToken left, ClauseWordToken right)
        {
            int start = left.EndIndex;
            int end = right.StartIndex;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if ((c >= '0' && c <= '9') || (c >= '\u1040' && c <= '\u1049'))
                    return true;
            }
            return false;
        }

        private static bool MatchesContextTrigger(string text, List<ClauseWordToken> tokens, int targetIdx, ContextualRule rule, out string matchedEvidence)
        {
            matchedEvidence = string.Empty;
            ClauseWordToken target = tokens[targetIdx];
            string[] triggers = rule.ParsedTriggers != null && rule.ParsedTriggers.Length > 0
                ? rule.ParsedTriggers
                : (rule.TriggerWords ?? string.Empty).Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (triggers.Length == 0)
                return false;

            string[] antiTriggers = rule.ParsedAntiTriggers != null && rule.ParsedAntiTriggers.Length > 0
                ? rule.ParsedAntiTriggers
                : (!string.IsNullOrWhiteSpace(rule.AntiTriggerWords)
                    ? rule.AntiTriggerWords.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>());

            if (antiTriggers.Length > 0)
            {
                for (int k = targetIdx - 1; k >= 0 && tokens[k].ClauseId == target.ClauseId; k--)
                {
                    if (TokenMatchesAnyTrigger(tokens[k].NormText, antiTriggers))
                        return false;
                }
                for (int k = targetIdx + 1; k < tokens.Count && tokens[k].ClauseId == target.ClauseId; k++)
                {
                    if (TokenMatchesAnyTrigger(tokens[k].NormText, antiTriggers))
                        return false;
                }
            }

            string dir = rule.Direction ?? "Both";
            int win = Math.Max(1, rule.Window);
            bool skipParticles = rule.SkipParticles;

            if (string.Equals(dir, "LeftSuffix", StringComparison.OrdinalIgnoreCase))
            {
                if (targetIdx == 0)
                    return false;
                ClauseWordToken prev = tokens[targetIdx - 1];
                if (prev.ClauseId != target.ClauseId || prev.EndIndex != target.StartIndex)
                    return false;
                for (int t = 0; t < triggers.Length; t++)
                {
                    if (prev.NormText.EndsWith(triggers[t], StringComparison.Ordinal))
                    {
                        matchedEvidence = prev.RawText;
                        return true;
                    }
                }
                return false;
            }

            bool checkLeft = string.Equals(dir, "Left", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(dir, "Both", StringComparison.OrdinalIgnoreCase);
            bool checkRight = string.Equals(dir, "Right", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(dir, "Both", StringComparison.OrdinalIgnoreCase);

            if (checkLeft)
            {
                int steps = 0;
                for (int j = targetIdx - 1; j >= 0 && steps < win; j--)
                {
                    if (tokens[j].ClauseId != target.ClauseId)
                        break;
                    if (HasInterveningDigit(text, tokens[j], target))
                        break;
                    if (win == 1 && !skipParticles && HasInterveningNonSpacePunctuation(text, tokens[j], target))
                        break;
                    string candNorm = tokens[j].NormText;
                    if (skipParticles && GenericSyntacticParticles.Contains(candNorm))
                        continue;
                    steps++;
                    if (!tokens[j].IsProtectedName && TokenMatchesAnyTrigger(candNorm, triggers))
                    {
                        matchedEvidence = tokens[j].RawText;
                        return true;
                    }
                }
            }

            if (checkRight)
            {
                int steps = 0;
                for (int j = targetIdx + 1; j < tokens.Count && steps < win; j++)
                {
                    if (tokens[j].ClauseId != target.ClauseId)
                        break;
                    if (HasInterveningDigit(text, target, tokens[j]))
                        break;
                    if (win == 1 && !skipParticles && HasInterveningNonSpacePunctuation(text, target, tokens[j]))
                        break;
                    string candNorm = tokens[j].NormText;
                    if (skipParticles && GenericSyntacticParticles.Contains(candNorm))
                        continue;
                    steps++;
                    if (!tokens[j].IsProtectedName && TokenMatchesAnyTrigger(candNorm, triggers))
                    {
                        matchedEvidence = tokens[j].RawText;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TokenMatchesAnyTrigger(string tokenNorm, string[] triggers)
        {
            for (int i = 0; i < triggers.Length; i++)
            {
                string trig = triggers[i];
                if (tokenNorm == trig)
                    return true;

                // Allow genuine multi-syllable triggers (>= 2 syllables) to match compound heads/tails
                // (e.g., ကုန်ဈေးနှုန်း ends with ဈေးနှုန်း), never 1-syllable triggers like 'ဆန်း' inside 'အောင်ဆန်း'
                if (trig.Length >= 4 &&
                    MyanmarSyllableBreaker.CountMyanmarSyllables(trig) >= 2 &&
                    (tokenNorm.StartsWith(trig, StringComparison.Ordinal) || tokenNorm.EndsWith(trig, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool OverlapsExisting(List<ProofingIssue> issues, int start, int length)
        {
            int end = start + length;
            for (int i = 0; i < issues.Count; i++)
            {
                int iEnd = issues[i].StartIndex + issues[i].Length;
                if (start < iEnd && end > issues[i].StartIndex)
                    return true;
            }
            return false;
        }

        private static void AddNonOverlapping(List<ProofingIssue> issues, ProofingIssue candidate)
        {
            if (!OverlapsExisting(issues, candidate.StartIndex, candidate.Length))
            {
                issues.Add(candidate);
            }
        }

        private static string ComputeDynamicReplacement(string raw)
        {
            if (raw == "ဈေးလျော့ပေး")
                return "ဈေးလျှော့ပေး";
            if (raw.StartsWith("လျော့", StringComparison.Ordinal))
                return "လျှော့" + raw.Substring("လျော့".Length);
            if (raw.StartsWith("လျှော့", StringComparison.Ordinal))
                return "လျော့" + raw.Substring("လျှော့".Length);
            if (raw.StartsWith("ပြည်", StringComparison.Ordinal) && raw.Length > "ပြည်".Length)
                return "ပြည့်" + raw.Substring("ပြည်".Length);
            if (raw == "ဝက်ဆင်" || raw == "ဝက်စုံ")
                return "ဝတ်" + raw.Substring("ဝက်".Length);
            if (raw == "အကြောင်းအကြိုး")
                return "အကြောင်းအကျိုး";
            if (raw.StartsWith("အကြိုး", StringComparison.Ordinal))
                return "အကျိုး" + raw.Substring("အကြိုး".Length);
            if (raw == "ကျိုးစား" || raw == "ကျိုးပမ်း" || raw == "ကျိုးကုတ်")
                return "ကြိုး" + raw.Substring("ကျိုး".Length);
            if (raw.StartsWith("မြက်", StringComparison.Ordinal) && raw.Length > "မြက်".Length)
                return "မျက်" + raw.Substring("မြက်".Length);
            if (raw.StartsWith("ဖျည့်", StringComparison.Ordinal))
                return "ဖြည့်" + raw.Substring("ဖျည့်".Length);
            if (raw == "အဖျေ")
                return "အဖြေ";
            if (raw.StartsWith("ဖျေ", StringComparison.Ordinal))
                return "ဖြေ" + raw.Substring("ဖျေ".Length);
            if (raw == "သတ်သာ" || raw == "သတ်ရောက်")
                return "သက်" + raw.Substring("သတ်".Length);
            if (raw == "ရက်တန့်" || raw == "ရက်ကွက်" || raw == "ရက်တည်")
                return "ရပ်" + raw.Substring("ရက်".Length);
            if (raw == "ကြယ်ဝန်း" || raw == "ကြယ်ပြန့်" || raw == "ကြယ်ပြော")
                return "ကျယ်" + raw.Substring("ကြယ်".Length);
            if (raw == "ပျံလည်" || raw == "ပျံကြားရေး")
                return "ပြန်" + raw.Substring("ပျံ".Length);
            if (raw == "ပြန်သန်း" || raw == "ပြန်ဝဲ")
                return "ပျံ" + raw.Substring("ပြန်".Length);
            if (raw.EndsWith("ခမ်း", StringComparison.Ordinal) && raw.Length > "ခမ်း".Length)
                return raw.Substring(0, raw.Length - "ခမ်း".Length) + "ခန်း";
            if (raw == "တွေ့ခေါ်" || raw == "တွေ့တော")
                return "တွေး" + raw.Substring("တွေ့".Length);
            if (raw == "တွေးဆုံ" || raw == "တွေးရှိ" || raw == "တွေးကြုံ")
                return "တွေ့" + raw.Substring("တွေး".Length);

            if (raw == "ကျွေးမြီ")
                return "ကြွေးမြီ";
            if (raw == "အကျွေး")
                return "အကြွေး";
            if (raw.EndsWith("မှီ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "မီ";
            if (raw == "ကိုင်းကျွန်းမီ")
                return "ကိုင်းကျွန်းမှီ";
            if (raw == "ကျွန်းကိုင်းမီ")
                return "ကျွန်းကိုင်းမှီ";
            if (raw == "စိုစို")
                return "ဆိုဆို";
            if (raw == "စို")
                return "ဆို";
            if (raw.StartsWith("မီ", StringComparison.Ordinal))
                return "မှီ" + raw.Substring(2);
            if (raw.EndsWith("ယဉ်", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "ယာဉ်";
            if (raw.StartsWith("ယဉ်", StringComparison.Ordinal))
                return "ယာဉ်" + raw.Substring(3);
            if (raw.StartsWith("ယာဉ်", StringComparison.Ordinal))
                return "ယဉ်" + raw.Substring(4);
            if (raw.Contains("ကြော်ငြာ"))
                return raw.Replace("ကြော်ငြာ", "ကြေညာ");
            if (raw == "စာဖက်")
                return "စာဖတ်";
            if (raw.StartsWith("ဖက်", StringComparison.Ordinal))
                return "ဖတ်" + raw.Substring(3);
            if (raw.EndsWith("ဖက်", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "ဘက်";
            if (raw.EndsWith("ကြမ်း", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 5) + "ကျမ်း";
            if (raw.StartsWith("ကျီး", StringComparison.Ordinal))
                return "ကြီး" + raw.Substring(4);
            if (raw.StartsWith("ကြောက်", StringComparison.Ordinal))
                return "ကျောက်" + raw.Substring(6);
            if (raw.StartsWith("ကျောက်", StringComparison.Ordinal))
                return "ကြောက်" + raw.Substring(6);
            if (raw.StartsWith("ကန်", StringComparison.Ordinal))
                return "ကံ" + raw.Substring(3);
            if (raw.StartsWith("လန်း", StringComparison.Ordinal))
                return "လမ်း" + raw.Substring(4);
            if (raw.EndsWith("သက်ပုံ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 6) + "သတ်ပုံ";
            if (raw.StartsWith("အသတ်", StringComparison.Ordinal))
                return "အသက်" + raw.Substring(4);
            if (raw.StartsWith("သတ်သေ", StringComparison.Ordinal))
                return "သက်သေ" + raw.Substring(5);
            if (raw.StartsWith("အားလက်", StringComparison.Ordinal))
                return "အားလပ်" + raw.Substring(6);
            if (raw.Contains("ဒါဏ်"))
                return raw.Replace("ဒါဏ်", "ဒဏ်");
            if (raw.StartsWith("ပိုဒ်မ", StringComparison.Ordinal))
                return "ပုဒ်မ" + raw.Substring(6);
            if (raw.StartsWith("အပုဒ်", StringComparison.Ordinal))
                return "အပိုဒ်" + raw.Substring(5);
            if (raw.StartsWith("စရာ", StringComparison.Ordinal))
                return "ဆရာ" + raw.Substring(3);
            if (raw.Length >= 5 && raw.EndsWith("ဆရာ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "စရာ";
            if (raw.Contains("နဲ"))
                return raw.Replace("နဲ", "နည်း");
            if (raw.EndsWith("ကြ", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 2) + "ကျ";
            if (raw.StartsWith("ဖူး", StringComparison.Ordinal) || raw.StartsWith("ဗူး", StringComparison.Ordinal))
                return "ဘူး" + raw.Substring(3);
            if (raw.EndsWith("ဖူး", StringComparison.Ordinal) || raw.EndsWith("ဗူး", StringComparison.Ordinal))
                return raw.Substring(0, raw.Length - 3) + "ဘူး";
            if (raw.Contains("စိုး"))
                return raw.Replace("စိုး", "ဆိုး");
            if (raw.Contains("ဆိုး"))
                return raw.Replace("ဆိုး", "စိုး");
            if (raw.Contains("ဒီဂရီ"))
                return raw.Replace("ဒီဂရီ", "ဒီကရီ");
            if (raw.Contains("ဒီကရီ"))
                return raw.Replace("ဒီကရီ", "ဒီဂရီ");
            if (raw == "နိုင်ငံသာ" || raw == "ကလေ" || raw == "ငါ" || raw == "စပါ" || raw == "တရာ" || raw == "မုန်တိုင်")
                return raw + "\u1038";
            if (raw == "ခစားကွင်း") return "ကစားကွင်း";
            if (raw == "ကဆား") return "ကစား";
            if (raw == "အရာသာ") return "အရသာ";
            if (raw == "စောင့်လျှောက်") return "စောင့်ရှောက်";
            if (raw == "မြား") return "များ";
            if (raw == "ပျော်ရွှေ့") return "ပျော်ရွှင်";
            if (raw == "မရှိမပြတ်") return "မရှိမဖြစ်";
            if (raw == "အသီးအရွတ်") return "အသီးအရွက်";
            if (raw == "အပန်းချွေ") return "အပန်းဖြေ";
            if (raw == "သတိသား") return "သတိထား";
            if (raw == "အကျိုးတက်ရောက်") return "အကျိုးသက်ရောက်";
            if (raw == "စတုတ္တ") return "စတုတ္ထ";
            if (raw == "အပျိုဘော်ဝင်") return "အပျိုဖော်ဝင်";
            if (raw == "မန်") return "မြန်";
            if (raw == "ပီ") return "ပြီ";
            if (raw == "ရှင်" || raw == "ကာ" || raw == "ပြီ") return raw + "\u1038";
            if (raw.EndsWith("ထဲက", StringComparison.Ordinal) && raw.Length > 3)
                return raw.Substring(0, raw.Length - 3) + "တည်းက";
            if (raw.EndsWith("ထဲ", StringComparison.Ordinal) && raw.Length > 2)
                return raw.Substring(0, raw.Length - 2) + "တည်း";

            return raw;
        }
    }
}
